using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Network.Client;
using Deterministic.GameFramework.Network.Interfaces;
using Template.Shared.Factories;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using FixedMathSharp;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Profiler;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Systems;
using Template.Godot.Framework.Editor;
using Template.Godot.Twitch;
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
    public int LocalPlayerId { get; private set; }
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

    public override void _Ready()
    {
        Instance = this;
        FrameworkDebugBridge.GetState = () => Game?.State;
        FrameworkDebugBridge.IsRunning = () => _isRunning;
        GD.Print("=== Initializing Godot Client ===");

        // Initialize Twitch integration when the game starts
        OnGameStarted += TwitchIntegration.Initialize;

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
        GameClient = new GameClient(networkClient, $"{ServerIp}:{ServerPort}", Game);
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

        OfflineUserId = Guid.NewGuid();
        Game.Loop.Schedule(new Template.Shared.Actions.AddPlayerAction(OfflineUserId), Deterministic.GameFramework.ECS.World.Entity);

        // Player will be created on first tick — find it after game loop starts
        Game.Loop.OnTick += () =>
        {
            if (LocalPlayerId != 0) return;
            foreach (var entity in Game.State.Filter<Template.Shared.Components.PlayerEntity>())
            {
                var p = Game.State.GetComponent<Template.Shared.Components.PlayerEntity>(entity);
                if (p.UserId == OfflineUserId)
                {
                    LocalPlayerId = entity.Id;
                    GD.Print($"[GameManager] Offline Player Created: {LocalPlayerId}");
                    break;
                }
            }
        };

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
        OfflineUserId = Guid.NewGuid();
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
            GameClient.OnLobbyCreated += (lobbyId) =>
            {
                CurrentLobbyId = lobbyId;
                GD.Print($"[GameManager] Lobby created: {lobbyId}");
                CallDeferred(nameof(EmitLobbyCreated), lobbyId.ToString());
            };

            await GameClient.CreateLobbyAsync(lobbyName);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Create lobby failed: {e}");
            OnError?.Invoke(e.Message);
        }
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

        // Auto-save recording every 10 seconds in case of unclean exit
        Game.Loop.OnTick += () =>
        {
            if (_inputRecorder != null && Game.Loop.CurrentTick % 600 == 0 && Game.Loop.CurrentTick > 0)
            {
                var dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                    "PizzaJam_Recordings");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "recording_latest.bin");
                _inputRecorder.Save(path);
            }
        };
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

    public override void _ExitTree()
    {
        _isRunning = false;
        TwitchIntegration.Shutdown();
        _localPlayerSubscription?.Dispose();
        Game.Loop.OnTick -= AutoSaveTick;
        Game.Loop.OnTick -= MetricsExportTick;
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
