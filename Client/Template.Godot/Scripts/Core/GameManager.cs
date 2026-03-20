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

namespace Template.Godot.Core;

public partial class GameManager : Node
{
	[Export] public string ServerIp = "127.0.0.1";
	[Export] public int ServerPort = 9050;
	
	// If true, auto-queues. If false, waits for UI (not implemented yet, so defaults true)
	[Export] public bool AutoConnect = true;

	public GameClient GameClient { get; private set; }
	public Deterministic.GameFramework.Common.Game Game { get; private set; }

	private Task _gameLoopTask;
	private bool _isRunning;

	public override void _Ready()
	{
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
		}
		catch (Exception e)
		{
			GD.PrintErr($"Connection failed: {e}");
		}
	}

	public override void _ExitTree()
	{
		_isRunning = false;
		Game.Loop.Stop();
		GameClient?.Dispose();
	}

	// Expose for other systems
	public bool IsGameRunning => _isRunning;
}
