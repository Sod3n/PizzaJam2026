using Godot;
using System;
using System.Threading.Tasks;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.Network.Client;
using Deterministic.GameFramework.Network.Interfaces;
using Template.Shared.Factories;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using FixedMathSharp;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Core;

public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; }

	[Export] public string ServerIp = "127.0.0.1";
	[Export] public int ServerPort = 9050;

	// If true, auto-queues. If false, waits for UI (not implemented yet, so defaults true)
	[Export] public bool AutoConnect = true;

	public GameClient GameClient { get; private set; }
	public Deterministic.GameFramework.Common.Game Game { get; private set; }
	public int LocalPlayerId { get; private set; }

	private Task _gameLoopTask;
	private bool _isRunning;
	private IDisposable _localPlayerSubscription;

	public override void _Ready()
	{
		Instance = this;
		GD.Print("=== Initializing Godot Client ===");

		// 1. Create Game
		Game = TemplateGameFactory.CreateGame(tickRate: 60);

		// 2. Setup Network
		var serverUrl = $"{ServerIp}:{ServerPort}";
		var networkClient = new LiteNetLibNetworkClient();

		GameClient = new GameClient(networkClient, serverUrl, Game);
		GameClient.OnLog += (msg) => GD.Print($"[GameClient] {msg}");

		if (AutoConnect)
		{
			_ = ConnectAndStart();
		}
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
