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
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Godot.Core;

public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; }

	[Export] public string ServerIp = "127.0.0.1";
	[Export] public int ServerPort = 9050;

	// If true, auto-queues. If false, waits for UI (not implemented yet, so defaults true)
	[Export] public bool AutoConnect = true;
	[Export] public bool OfflineMode = false;

	public GameClient GameClient { get; private set; }
	public Deterministic.GameFramework.Common.Game Game { get; private set; }
	public int LocalPlayerId { get; private set; }
	public Guid OfflineUserId { get; private set; }

	private Task _gameLoopTask;
	private bool _isRunning;
	private IDisposable _localPlayerSubscription;

	public override void _Ready()
	{
		Instance = this;
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

		if (OfflineMode)
		{
			StartOffline();
		}
		else if (AutoConnect)
		{
			_ = ConnectAndStart();
		}
	}

	private void StartOffline()
	{
		GD.Print("Starting in Offline Mode...");

		// 1. Initialize Local Player
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

		// 2. Start Loop
		_gameLoopTask = Game.Loop.Start();
		_isRunning = true;
	}

	public void ScheduleOfflineAction<TAction>(TAction action, int targetEntityId) where TAction : struct, IAction
	{
		var id = ComponentId<TAction>.DenseId;
		Game.Scheduler.Schedule(action, id, new Entity(targetEntityId), Game.Loop.CurrentTick + 1);
	}

	private async Task ConnectAndStart()
	{
		try
		{
			GD.Print("Connecting...");
			await GameClient.ConnectAsync();

			GD.Print("Enqueuing...");
			await GameClient.EnqueuePlayerAsync();

			GD.Print("Waiting for match...");
			await GameClient.WaitForSyncAsync();

			GD.Print("Synced! Starting GameLoop...");
			_gameLoopTask = Game.Loop.Start();
			_isRunning = true;

			// Find Local Player automatically
			SetupLocalPlayerDiscovery();
		}
		catch (Exception e)
		{
			GD.PrintErr($"Connection failed: {e}");
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

	public override void _ExitTree()
	{
		_isRunning = false;
		_localPlayerSubscription?.Dispose();
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
		GD.PrintErr($"[GodotLogger] Warning: {message}");
	}

	public void _LogError(string message)
	{
		GD.PrintErr($"[GodotLogger] Error: {message}");
	}
}
