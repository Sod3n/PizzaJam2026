using Godot;
using System;
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
using Template.Godot.Framework.Editor;
using FileAccess = Godot.FileAccess;

namespace Template.Godot.Core;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    [Export] public string ServerIp = "127.0.0.1";
    [Export] public int ServerPort = 9050;
    [Export] public bool OfflineMode = false;

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

    public override void _Ready()
    {
        Instance = this;
        FrameworkDebugBridge.GetState = () => Game?.State;
        FrameworkDebugBridge.IsRunning = () => _isRunning;
        GD.Print("=== Initializing Godot Client ===");

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
        var serverUrl = $"{ServerIp}:{ServerPort}";
        var networkClient = new LiteNetLibNetworkClient();

        GameClient = new GameClient(networkClient, serverUrl, Game);
        GameClient.OnLog += (msg) => GD.Print($"[GameClient] {msg}");

        // Wait for UI to call StartOffline(), CreateLobby(), or JoinLobby()
    }

    public void StartOffline()
    {
        GD.Print("Starting in Offline Mode...");
        OfflineMode = true;

        OfflineUserId = Guid.NewGuid();
        var context = new Deterministic.GameFramework.DAR.Context(Game.State, Deterministic.GameFramework.ECS.Entity.Null, null!);
        var playerEntity = Template.Shared.Definitions.PlayerDefinition.Create(
            context,
            OfflineUserId,
            new Deterministic.GameFramework.Types.Vector2(0, 0),
            0
        );

        LocalPlayerId = playerEntity.Id;
        GD.Print($"[GameManager] Offline Player Created: {LocalPlayerId}");

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
        if (entities.Length > 0)
        {
            LocalPlayerId = entities[0].Id;
            ref var player = ref Game.State.GetComponent<PlayerEntity>(entities[0]);
            OfflineUserId = player.UserId;
            GD.Print($"[GameManager] Restored Player: {LocalPlayerId}");
        }

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

            OnStatusChanged?.Invoke("Waiting for sync...");
            await GameClient.WaitForSyncAsync();

            GD.Print("Synced! Starting GameLoop...");
            _gameLoopTask = Game.Loop.Start();
            _isRunning = true;
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

            OnStatusChanged?.Invoke("Waiting for host to start...");
            await GameClient.WaitForSyncAsync();

            GD.Print("Synced! Starting GameLoop...");
            _gameLoopTask = Game.Loop.Start();
            _isRunning = true;
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
        _localPlayerSubscription?.Dispose();
        Game.Loop.OnTick -= AutoSaveTick;
        if (_isRunning) SaveGame();
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
