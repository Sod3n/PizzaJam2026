using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Network.Client;
using Deterministic.GameFramework.Network.Interfaces;
using Template.Shared.Factories;
using Template.Shared.Features.Movement;
using Template.Shared.Components; // Added for ScoreComponent/PlayerEntity
using Template.Client.Visuals; // Added for ConsoleVisualizer
using Microsoft.Extensions.Logging;
using FixedMathSharp;
using Guid = System.Guid;

namespace Template.Client;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== V2 Multiplayer Example Client ===");

        // 1. Setup Game via Factory (Shared with Server)
        var game = TemplateGameFactory.CreateGame(tickRate: 60);

        // 2. Setup Network V2 Client
        var serverUrl = "127.0.0.1:9050"; 
        
        // Choose your transport!
        INetworkClient networkClient;
        
        // Option A: SignalR (WebSocket)
        // networkClient = new SignalRNetworkClient("http://localhost:5000/gamehub");
        
        // Option B: LiteNetLib (UDP) - REQUIRED by user
        networkClient = new LiteNetLibNetworkClient();
        
        await using var client = new GameClient(networkClient, serverUrl, game);
        
        // Setup MVVM Visualizer
        using var visualizer = new ConsoleVisualizer(client);
        visualizer.Initialize();

        // Simple logging
        client.OnLog += Console.WriteLine;

        try 
        {
            // 3. Connect to Server (without matchId initially)
        
            if (args.Length > 0 && args[0] == "test")
            {
                await new FarmingTest().Run();
                return;
            }
            if (args.Length > 0 && args[0] == "collision-test")
            {
                await new CollisionTest().Run();
                return;
            }

            await client.ConnectAsync();
            
            string? choice;
            if (args.Length > 0)
            {
                choice = args[0];
                Console.WriteLine($"Auto-selecting mode: {choice}");
            }
            else
            {
                Console.WriteLine("Connected to server. Choose mode: [1] Queue, [2] Create Lobby, [3] Join Lobby");
                choice = Console.ReadLine();
            }

            bool running = true;
            System.Guid? currentLobbyId = null;

            if (choice == "1")
            {
                Console.WriteLine("Entering Queue...");
                await client.EnqueuePlayerAsync();
            }
            else if (choice == "2")
            {
                Console.WriteLine("Creating Lobby 'MyRoom'...");
                client.OnLobbyCreated += (lobbyId) => {
                    currentLobbyId = lobbyId;
                    Console.WriteLine($"Lobby Created: {lobbyId}. Press 'S' to start when players join.");
                };
                await client.CreateLobbyAsync("MyRoom");
                
                // Keep reading keys for lobby owner
                _ = Task.Run(async () => {
                    while (running) {
                        if (Console.KeyAvailable) {
                            var key = Console.ReadKey(true).Key;
                            if (key == ConsoleKey.S && currentLobbyId.HasValue) {
                                Console.WriteLine("Starting match from lobby...");
                                await client.StartLobbyMatchAsync(currentLobbyId.Value);
                            }
                        }
                        await Task.Delay(100);
                    }
                });
            }
            else if (choice == "3")
            {
                Console.WriteLine("Enter Lobby ID to join:");
                var idStr = Console.ReadLine();
                if (System.Guid.TryParse(idStr, out var lobbyId))
                {
                    await client.JoinLobbyAsync(lobbyId);
                }
            }

            Console.WriteLine("Waiting for match assignment and server state...");
            await client.WaitForSyncAsync();
            Console.WriteLine($"Synced! Starting at Tick {game.Loop.CurrentTick}");
            
            Console.WriteLine("Starting game loop...");
            _ = game.Loop.Start();
            Console.WriteLine("Game loop started!");

            Console.WriteLine("Connected! Press 'W' to move up, 'Q' to quit.");
            
            while (running)
            {
                try 
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Q)
                        {
                            running = false;
                            break;
                        }
                        
                        // Find local player entity
                        int targetId = 0;
                        foreach (var entity in game.State.Filter<PlayerEntity>())
                        {
                            var player = game.State.GetComponent<PlayerEntity>(entity);
                            if (player.UserId.ToString() == client.PlayerId.ToString())
                            {
                                targetId = entity.Id;
                                break;
                            }
                        }

                        if (targetId != 0)
                        {
                            if (key == ConsoleKey.W)
                            {
                                var action = new SetMoveDirectionAction { Direction = new Vector2(0, -1), Speed = 10 }; // Move Up
                                client.Execute(action, targetId); 
                                Console.WriteLine($"Action Sent! (Move Up)");
                            }
                            // Add other keys...
                        }
                    }
                }
                catch (InvalidOperationException) 
                {
                    // Ignore console errors in non-interactive environments
                }
                
                // Display Score every 60 ticks (~1 second)
                if (game.Loop.CurrentTick % 60 == 0)
                {
                    // Log heartbeat to confirm loop is alive
                    Console.WriteLine($"[ClientLoop] Tick: {game.Loop.CurrentTick}");
                    
                    foreach (var entity in game.State.Filter<PlayerEntity>())
                    {
                        // Copy values instead of ref to avoid async limitations
                        var player = game.State.GetComponent<PlayerEntity>(entity);
                        
                        // Compare GUIDs (Network GUID vs System GUID)
                        // PlayerEntity.UserId is Deterministic.GameFramework.Core.Guid
                        // client.PlayerId is System.Guid
                        // We need to convert or compare string representations/bytes
                        
                        if (player.UserId.ToString() == client.PlayerId.ToString())
                        {
                            if (game.State.HasComponent<ScoreComponent>(entity))
                            {
                                var score = game.State.GetComponent<ScoreComponent>(entity);
                                Console.WriteLine($"[Score] Your Score: {score.Value}");
                            }
                            break;
                        }
                    }
                }
                
                await Task.Delay(16);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

    }
}
