using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Network.Client;
using Deterministic.GameFramework.Network.Interfaces;
using Deterministic.GameFramework.DeltaSync;
using Template.Shared.Factories;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using FixedMathSharp;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Profiler;
using Deterministic.GameFramework.Serialization;
using Deterministic.GameFramework.Debugging;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Systems;
using Template.Godot.Framework.Editor;
using Template.Godot.Twitch;
using Template.Godot.Visuals;
using Template.Shared.Recording;
using FileAccess = Godot.FileAccess;

namespace Template.Godot.Core;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    [Export] public string ServerIp = "127.0.0.1";
    [Export] public int ServerPort = 9050;
    [Export] public string RemoteServerIp = "193.168.49.169";
    [Export] public int RemoteServerPort = 9050;
    [Export] public bool OfflineMode = false;
    [Export] public bool RecordInputs = false;
    [Export] public int SimulatedLatencyMs = 0;

    private InputRecorder _inputRecorder;
    public bool IsLoadedFromSave { get; private set; }

    public GameClient GameClient { get; private set; }
    public Deterministic.GameFramework.Common.Game Game { get; private set; }
    private readonly ReactiveProperty<int> _localPlayerId = new(0);
    public ReadOnlyReactiveProperty<int> LocalPlayerIdReactive => _localPlayerId;
    public int LocalPlayerId
    {
        get => _localPlayerId.Value;
        private set => _localPlayerId.Value = value;
    }
    public Guid OfflineUserId { get; private set; }
    public Guid CurrentLobbyId { get; private set; }

    public event Action<string> OnStatusChanged;
    public event Action<Guid> OnLobbyCreated;
    public event Action OnGameStarted;
    public event Action<string> OnError;

    private const string SaveFilePath = "user://savegame.dat";
    private const int AutoSaveInterval = 300; // 5 seconds at 60hz
    private int _autoSaveCounter;
    private byte[] _pendingLoadState;

    private Task _gameLoopTask;
    private bool _isRunning;
    private IDisposable _localPlayerSubscription;
    private MetricsExporter _metricsExporter;
    private int _metricsExportCounter;
    private DesyncRecorder _desyncRecorder;
    private Action<Guid> _onLobbyCreatedHandler;
    private Guid _orphanOfflineUserIdToPrune;
    private bool _orphanPrunePending;

    public override void _Ready()
    {
        Instance = this;
        FrameworkDebugBridge.GetState = () => Game?.State;
        FrameworkDebugBridge.IsRunning = () => _isRunning;
        GD.Print("=== Initializing Godot Client ===");

        // Phase 4: view-layer smoothing. The manager ticks a ViewSmoother every
        // render frame, so entity visuals lerp toward the authoritative ECS value
        // instead of snapping when the server delta arrives. The manager is a
        // sibling Node; it looks up GameManager.Instance on its own.
        if (ViewSmoothingManager.Instance == null)
        {
            var smoothingManager = new ViewSmoothingManager { Name = "ViewSmoothingManager" };
            AddChild(smoothingManager);
        }

        // Initialize Twitch integration when the game starts
        OnGameStarted += TwitchIntegration.Initialize;
        OnGameStarted += Template.Godot.Visuals.GameOverOverlay.InstallWatcher;

        ILogger.SetLogger(new GodotLogger());

        // 1. Create Game
        // We read the JSON files directly from Godot's virtual filesystem (res://)
        // and pass them as strings to the shared library. This avoids System.IO issues in exported builds.
        var gameDataJson = new System.Collections.Generic.Dictionary<string, string>();

        string[] dataFiles = { "Skins.json" };
        foreach (var fileName in dataFiles)
        {
            var path = $"res://GameData/{fileName}";
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                gameDataJson[fileName] = file.GetAsText();
                GD.Print($"[GameManager] Loaded {fileName} from {path}");
            }
            else
            {
                GD.PrintErr($"[GameManager] Failed to load {fileName} from {path}");
            }
        }

        Game = TemplateGameFactory.CreateGame(tickRate: 60, gameDataJson: gameDataJson);

        // 2. Setup Network
        var networkClient = new LiteNetLibNetworkClient();
        GameClient = new DeltaSyncGameClient(networkClient, $"{ServerIp}:{ServerPort}", Game);
        // Send the persistent local UserId as the auth token so the server's IAuthService
        // returns it as our PlayerId — same identity across reconnects and across publish.
        GameClient.AuthToken = PlayerIdentity.LocalId.ToString();
        GameClient.OnLog += (msg) => GD.Print($"[GameClient] {msg}");
        if (SimulatedLatencyMs > 0)
        {
            GameClient.SimulatedLatencyMs = SimulatedLatencyMs;
            GD.Print($"[GameManager] Simulated latency: {SimulatedLatencyMs}ms");
        }
        OnGameStarted += () =>
        {
            if (!_useRemoteServer)
                GameClient.ActivateSimulatedLatency();
        };

        // Wait for UI to call StartOffline(), CreateLobby(), or JoinLobby()
    }

    private bool _useRemoteServer;

    public void SetUseRemoteServer(bool useRemote)
    {
        _useRemoteServer = useRemote;
        if (useRemote)
            GameClient.SetConnectionString($"{RemoteServerIp}:{RemoteServerPort}");
        else
            GameClient.SetConnectionString($"{ServerIp}:{ServerPort}");
    }

    public void StartOffline()
    {
        GD.Print("Starting in Offline Mode...");
        OfflineMode = true;

        // Use the persistent local UserId so a future PublishOfflineToLobby produces
        // a state whose PlayerEntity.UserId matches the auth token we'll send to the
        // server — AddPlayerActionService then sees the existing player and skips
        // creating a duplicate / helper-player.
        OfflineUserId = PlayerIdentity.LocalId;
        Game.Loop.Schedule(new Template.Shared.Actions.AddPlayerAction(OfflineUserId), Deterministic.GameFramework.ECS.World.Entity);

        Game.Loop.OnTick += _OfflinePlayerDiscoveryTick;

        StartMetricsExport();
        StartInputRecording();
        Game.Loop.OnTick += AutoSaveTick;
        _gameLoopTask = Game.Loop.Start();
        _isRunning = true;
        GameProfiler.Enable(Game);
        OnGameStarted?.Invoke();
    }

    public void StartOfflineFromSave()
    {
        var saveData = LoadGameFromDisk();
        if (saveData == null)
        {
            GD.PrintErr("[GameManager] No save file found");
            OnError?.Invoke("No save file found");
            return;
        }

        GD.Print("Starting Offline from save...");
        OfflineMode = true;

        long savedTick = BitConverter.ToInt64(saveData, 0);
        byte[] stateData = new byte[saveData.Length - 8];
        Array.Copy(saveData, 8, stateData, 0, stateData.Length);

        StateSerializer.Deserialize(Game.State, stateData);
        Game.Loop.ForceSetTick(savedTick);
        ReactiveSystem.Instance.Reset();

        // Find our player in the restored state
        OfflineUserId = PlayerIdentity.LocalId;
        var entities = Game.State.Filter<PlayerEntity>();
        var firstEntity = entities.FirstOrDefault();
        if (firstEntity.Id != 0)
        {
            LocalPlayerId = firstEntity.Id;
            ref var player = ref Game.State.GetComponent<PlayerEntity>(firstEntity);
            OfflineUserId = player.UserId;
            GD.Print($"[GameManager] Restored Player: {LocalPlayerId}");
        }

        StartMetricsExport();
        StartInputRecording();
        Game.Loop.OnTick += AutoSaveTick;
        _gameLoopTask = Game.Loop.Start();
        _isRunning = true;
        GameProfiler.Enable(Game);
        OnGameStarted?.Invoke();
    }

    public void ScheduleOfflineAction<TAction>(TAction action, int targetEntityId) where TAction : struct, IAction
    {
        var id = ComponentId<TAction>.DenseId;
        Game.Scheduler.Schedule(action, id, new Entity(targetEntityId), Game.Loop.CurrentTick + 1);
    }

    public async Task CreateLobby(string lobbyName)
    {
        try
        {
            OnStatusChanged?.Invoke("Connecting to server...");
            await GameClient.ConnectAsync();

            OnStatusChanged?.Invoke("Creating lobby...");
            if (_onLobbyCreatedHandler != null)
                GameClient.OnLobbyCreated -= _onLobbyCreatedHandler;
            _onLobbyCreatedHandler = HandleLobbyCreated;
            GameClient.OnLobbyCreated += _onLobbyCreatedHandler;

            await GameClient.CreateLobbyAsync(lobbyName);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Create lobby failed: {e}");
            OnError?.Invoke(e.Message);
        }
    }

    private void HandleLobbyCreated(Guid lobbyId)
    {
        CurrentLobbyId = lobbyId;
        GD.Print($"[GameManager] Lobby created: {lobbyId}");
        CallDeferred(nameof(EmitLobbyCreated), lobbyId.ToString());
    }

    private void EmitLobbyCreated(string lobbyIdStr)
    {
        OnLobbyCreated?.Invoke(Guid.Parse(lobbyIdStr));
    }

    public async Task StartLobby()
    {
        try
        {
            OnStatusChanged?.Invoke("Starting match...");
            await GameClient.StartLobbyMatchAsync(CurrentLobbyId, _pendingLoadState);
            _pendingLoadState = null;

            // Start recording BEFORE sync so we capture all actions from TickSnapshots
            // that arrive during WaitForSyncAsync. Initial state is captured after sync.
            StartInputRecording();

            OnStatusChanged?.Invoke("Waiting for sync...");
            await GameClient.WaitForSyncAsync();

            // Re-capture initial state now that we have the server's authoritative state
            _inputRecorder?.CaptureInitialState();

            GD.Print("Synced! Starting GameLoop...");
            StartMetricsExport();
            StartDesyncRecording();
            _gameLoopTask = Game.Loop.Start();
            _isRunning = true;
            GameProfiler.Enable(Game);
            SetupLocalPlayerDiscovery();
            OnGameStarted?.Invoke();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Start lobby failed: {e}");
            OnError?.Invoke(e.Message);
        }
    }

    public async Task JoinLobby(Guid lobbyId)
    {
        try
        {
            OnStatusChanged?.Invoke("Connecting to server...");
            await GameClient.ConnectAsync();

            OnStatusChanged?.Invoke("Joining lobby...");
            CurrentLobbyId = lobbyId;
            await GameClient.JoinLobbyAsync(lobbyId);

            // Start recording BEFORE sync
            StartInputRecording();

            OnStatusChanged?.Invoke("Waiting for host to start...");
            await GameClient.WaitForSyncAsync();

            // Re-capture initial state after sync
            _inputRecorder?.CaptureInitialState();

            GD.Print("Synced! Starting GameLoop...");
            StartMetricsExport();
            StartDesyncRecording();
            _gameLoopTask = Game.Loop.Start();
            _isRunning = true;
            GameProfiler.Enable(Game);
            SetupLocalPlayerDiscovery();
            OnGameStarted?.Invoke();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Join lobby failed: {e}");
            OnError?.Invoke(e.Message);
        }
    }


    private void SetupLocalPlayerDiscovery()
    {
        // Simple reactive subscription to find our player entity
        // We hide this complexity here so InputManager doesn't need to know about it
        _localPlayerSubscription = GameClient.Reactive.ObservableCollection<PlayerEntity>()
            .Subscribe(entity =>
            {
                if (Game.State.HasComponent<PlayerEntity>(entity))
                {
                    ref var player = ref Game.State.GetComponent<PlayerEntity>(entity);
                    if (player.UserId.ToString() == GameClient.PlayerId.ToString())
                    {
                        LocalPlayerId = entity.Id;
                        GD.Print($"[GameManager] Found Local Player: {LocalPlayerId}");
                    }
                }
            },
            entity =>
            {
                if (entity.Id == LocalPlayerId) LocalPlayerId = 0;
            });
    }

    public void SaveGame()
    {
        try
        {
            byte[] stateData = StateSerializer.Serialize(Game.State);
            long tick = Game.Loop.CurrentTick;

            byte[] saveData = new byte[8 + stateData.Length];
            BitConverter.TryWriteBytes(new Span<byte>(saveData, 0, 8), tick);
            stateData.CopyTo(saveData, 8);

            using var file = FileAccess.Open(SaveFilePath, FileAccess.ModeFlags.Write);
            if (file != null)
            {
                file.StoreBuffer(saveData);
                GD.Print($"[GameManager] Game saved at tick {tick} ({stateData.Length} bytes)");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[GameManager] Save failed: {e.Message}");
        }
    }

    public byte[] LoadGameFromDisk()
    {
        if (!FileAccess.FileExists(SaveFilePath))
            return null;

        using var file = FileAccess.Open(SaveFilePath, FileAccess.ModeFlags.Read);
        if (file == null) return null;

        var data = file.GetBuffer((long)file.GetLength());
        if (data.Length <= 8) return null;

        GD.Print($"[GameManager] Loaded save file ({data.Length} bytes)");
        return data;
    }

    public bool HasSaveFile()
    {
        return FileAccess.FileExists(SaveFilePath);
    }

    public void SetPendingLoadState(byte[] saveData)
    {
        _pendingLoadState = saveData;
    }

    private void StartInputRecording()
    {
        if (!RecordInputs) return;
        _inputRecorder = new InputRecorder(Game);
        _inputRecorder.CaptureStateAtCheckpoints = true;
        _inputRecorder.Start();
        GD.Print("[GameManager] Input recording STARTED");

        Game.Loop.OnTick += _RecordingAutoSaveTick;
    }

    private void _RecordingAutoSaveTick()
    {
        if (_inputRecorder == null) return;
        if (Game.Loop.CurrentTick == 0 || Game.Loop.CurrentTick % 600 != 0) return;

        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "PizzaJam_Recordings");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "recording_latest.bin");
        _inputRecorder.Save(path);
    }

    private void _OfflinePlayerDiscoveryTick()
    {
        if (LocalPlayerId != 0) return;
        foreach (var entity in Game.State.Filter<PlayerEntity>())
        {
            var p = Game.State.GetComponent<PlayerEntity>(entity);
            if (p.UserId == OfflineUserId)
            {
                LocalPlayerId = entity.Id;
                GD.Print($"[GameManager] Offline Player Created: {LocalPlayerId}");
                break;
            }
        }
    }

    /// <summary>
    /// Call this to save the current recording (e.g. from a UI button or on quit).
    /// Saves to ~/PizzaJam_Recordings/recording_TIMESTAMP.bin
    /// </summary>
    public void SaveInputRecording()
    {
        if (_inputRecorder == null) return;
        _inputRecorder.Stop();

        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "PizzaJam_Recordings");
        System.IO.Directory.CreateDirectory(dir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = System.IO.Path.Combine(dir, $"recording_{timestamp}.bin");
        _inputRecorder.Save(path);
        GD.Print($"[GameManager] Recording saved: {path} ({_inputRecorder.ActionCount} actions)");
    }

    private void StartDesyncRecording()
    {
        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "PizzaJam_DesyncLogs");
        var sessionId = GameClient.CurrentMatchId.ToString();
        var recorder = new DesyncRecorder(Game.Loop.Simulation);
        recorder.Start(
            System.IO.Path.Combine(dir, $"client_{sessionId}.jsonl"),
            "client",
            sessionId,
            Game.Loop.TickRate);
        // DesyncRecorder hooks into the simulation's scheduler events from Start();
        // there is no Recorder slot on GameSimulation, so just keep the disposable alive.
        _desyncRecorder = recorder;
        GD.Print($"[GameManager] Desync recording started: client_{sessionId}.jsonl");
    }

    private void StartMetricsExport()
    {
        if (!OS.IsDebugBuild()) return;

        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "PizzaJam_Metrics");
        _metricsExporter = new MetricsExporter(dir);
        Game.Loop.OnTick += MetricsExportTick;
        GD.Print($"[GameManager] Metrics CSV: {_metricsExporter.FilePath}");
    }

    private void MetricsExportTick()
    {
        _metricsExportCounter++;
        if (_metricsExportCounter < 60) return; // write once per second
        _metricsExportCounter = 0;
        _metricsExporter?.WriteSnapshot(Game.State);
    }

    private void AutoSaveTick()
    {
        _autoSaveCounter++;
        if (_autoSaveCounter >= AutoSaveInterval)
        {
            _autoSaveCounter = 0;
            SaveGame();
        }
    }

    private void DetachAllLoopHandlers()
    {
        Game.Loop.OnTick -= AutoSaveTick;
        Game.Loop.OnTick -= MetricsExportTick;
        Game.Loop.OnTick -= _OfflinePlayerDiscoveryTick;
        Game.Loop.OnTick -= _RecordingAutoSaveTick;
        Game.Loop.OnTick -= _OrphanPruneTick;
        if (_onLobbyCreatedHandler != null && GameClient != null)
        {
            GameClient.OnLobbyCreated -= _onLobbyCreatedHandler;
            _onLobbyCreatedHandler = null;
        }
    }

    public override void _ExitTree()
    {
        _isRunning = false;
        TwitchIntegration.Shutdown();
        _localPlayerSubscription?.Dispose();
        DetachAllLoopHandlers();
        if (_metricsExporter != null)
        {
            var path = _metricsExporter.Finish(Game.State);
            GD.Print($"[GameManager] Metrics saved: {path}");
        }
        SaveInputRecording();
        SaveGame();
        Game.Loop.Stop();
        GameClient?.Dispose();
    }

    /// <summary>
    /// Promote the running offline session to a hosted online lobby. Captures the current
    /// world state, stops the local loop, then walks the standard online path
    /// (CreateLobby → StartLobby) with <see cref="_pendingLoadState"/> set so the server
    /// boots the match from the captured snapshot instead of a fresh world.
    /// </summary>
    public async Task PublishOfflineToLobby(string lobbyName)
    {
        if (!OfflineMode)
        {
            OnError?.Invoke("Not in offline mode.");
            return;
        }

        // Snapshot must include the offline player so the FullState is internally consistent.
        var orphanId = OfflineUserId;

        try
        {
            OnStatusChanged?.Invoke("Capturing world state...");

            // The offline PlayerEntity already has UserId == PlayerIdentity.LocalId, which
            // matches the auth token we'll send to the server on rejoin — so AddPlayerAction
            // sees the existing player and skips creating a duplicate. No anonymization needed.

            // Wrap the state in the same 8-byte tick prefix that TemplateMatchFactory.CreateMatch
            // (and SaveGame/LoadGameFromDisk) expect, otherwise the server's deserializer is offset
            // by 8 bytes and throws EndOfStreamException mid-read.
            byte[] rawState = StateSerializer.Serialize(Game.State);
            long tick = Game.Loop.CurrentTick;
            byte[] stateData = new byte[8 + rawState.Length];
            BitConverter.TryWriteBytes(new Span<byte>(stateData, 0, 8), tick);
            rawState.CopyTo(stateData, 8);

            DetachAllLoopHandlers();
            Game.Loop.Stop();
            try { await _gameLoopTask.ConfigureAwait(false); } catch { }
            _isRunning = false;

            OfflineMode = false;
            LocalPlayerId = 0;
            _pendingLoadState = stateData;

            await CreateLobby(lobbyName);
            // CreateLobby sets CurrentLobbyId via OnLobbyCreated; wait briefly for the event.
            int waited = 0;
            while (CurrentLobbyId == Guid.Empty && waited < 5000)
            {
                await Task.Delay(50);
                waited += 50;
            }
            if (CurrentLobbyId == Guid.Empty)
            {
                OnError?.Invoke("Lobby creation timed out.");
                return;
            }
            await StartLobby();
            // No orphan prune needed — AddPlayerActionService claims the anonymized offline
            // PlayerEntity (UserId = Guid.Empty) on first re-join.
        }
        catch (Exception e)
        {
            GD.PrintErr($"PublishOfflineToLobby failed: {e}");
            OnError?.Invoke(e.Message);
        }
    }

    private void _OrphanPruneTick()
    {
        if (!_orphanPrunePending) return;
        if (LocalPlayerId == 0) return;
        if (GameClient == null || GameClient.PlayerId == Guid.Empty) return;

        bool orphanFound = false;
        foreach (var entity in Game.State.Filter<PlayerEntity>())
        {
            ref var p = ref Game.State.GetComponent<PlayerEntity>(entity);
            if (p.UserId == _orphanOfflineUserIdToPrune)
            {
                orphanFound = true;
                break;
            }
        }

        if (!orphanFound)
        {
            _orphanPrunePending = false;
            Game.Loop.OnTick -= _OrphanPruneTick;
            return;
        }

        GD.Print($"[GameManager] Pruning orphan offline player UserId={_orphanOfflineUserIdToPrune}");
        GameClient.Execute(new Template.Shared.Actions.RemovePlayerAction(_orphanOfflineUserIdToPrune), 0);

        _orphanPrunePending = false;
        Game.Loop.OnTick -= _OrphanPruneTick;
    }

    /// <summary>
    /// Tear down the active session (offline or online) and surface the LobbyMenu so the
    /// user can pick a new mode. Stops the loop, disconnects the network client, clears
    /// session identity, and re-shows the LobbyMenu CanvasLayer instanced in the root scene.
    /// </summary>
    public void EndSessionAndReturnToLobbyMenu()
    {
        try
        {
            _isRunning = false;
            _localPlayerSubscription?.Dispose();
            _localPlayerSubscription = null;
            DetachAllLoopHandlers();
            SaveGame();
            Game.Loop.Stop();

            try { GameClient?.Dispose(); } catch (Exception e) { GD.PrintErr($"GameClient dispose: {e}"); }

            // Rebuild GameClient so a subsequent CreateLobby/JoinLobby has a fresh socket.
            var networkClient = new LiteNetLibNetworkClient();
            GameClient = new DeltaSyncGameClient(networkClient, $"{ServerIp}:{ServerPort}", Game);
            GameClient.OnLog += (msg) => GD.Print($"[GameClient] {msg}");
            if (SimulatedLatencyMs > 0) GameClient.SimulatedLatencyMs = SimulatedLatencyMs;

            CurrentLobbyId = Guid.Empty;
            LocalPlayerId = 0;
            OfflineMode = false;
            _pendingLoadState = null;
            _orphanPrunePending = false;
            _orphanOfflineUserIdToPrune = Guid.Empty;

            var tree = GetTree();
            if (tree != null)
            {
                tree.Paused = false;
                tree.CallDeferred("reload_current_scene");
            }
            else
            {
                GD.PrintErr("[GameManager] EndSession: SceneTree unavailable, cannot reload scene.");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"EndSession failed: {e}");
            OnError?.Invoke(e.Message);
        }
    }

    // Expose for other systems
    public bool IsGameRunning => _isRunning;
}

public class GodotLogger : ILogger
{
    public void _Log(string message)
    {
        GD.Print($"[GodotLogger] {message}");
    }

    public void _LogWarning(string message)
    {
        GD.Print($"[GodotLogger] Warning: {message}");
    }

    public void _LogError(string message)
    {
        GD.Print($"[GodotLogger] Error: {message}");
    }
}
