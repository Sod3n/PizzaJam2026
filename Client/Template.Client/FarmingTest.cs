using System;
using System.Threading.Tasks;
using System.Linq;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.Network.Client;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using Template.Shared.Actions;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Features.Movement;
using FixedMathSharp;

namespace Template.Client;

public class FarmingTest
{
    private Game _game = null!;
    private GameClient _client = null!;
    private Guid _playerId;
    private int _playerEntityId;

    public async Task Run()
    {
        Console.WriteLine("=== Starting Farming Test ===");

        // 1. Setup Local Game
        _game = TemplateGameFactory.CreateGame(tickRate: 60);
        
        // 2. Setup Mock/Loopback Client
        var networkClient = new LiteNetLibNetworkClient();
        _client = new GameClient(networkClient, "127.0.0.1:9050", _game);
        
        // 3. Connect & Join Default Match
        var defaultMatchId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Console.WriteLine($"Connecting and joining match {defaultMatchId}...");
        await _client.ConnectAsync(defaultMatchId);
        
        Console.WriteLine("Connected to server and joined match.");
        
        _playerId = _client.PlayerId;
        Console.WriteLine($"Player ID: {_playerId}");

        // 4. Start Game Loop
        _ = _game.Loop.Start();
        
        // Wait for player entity to spawn
        Console.WriteLine("Waiting for player spawn...");
        _playerEntityId = await WaitForPlayerSpawn();
        Console.WriteLine($"Player Spawned! Entity ID: {_playerEntityId}");
        
        // 5. Run Test Scenario
        try 
        {
            await TestScenario();
            Console.WriteLine("=== TEST PASSED ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== TEST FAILED: {ex.Message} ===");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            await _client.DisposeAsync();
        }
    }

    private async Task TestScenario()
    {
        // Initial State Check
        var globalRes = GetGlobalResources();
        Console.WriteLine($"Initial Resources: Grass={globalRes.Grass}, Milk={globalRes.Milk}, Coins={globalRes.Coins}");
        
        // Step 1: Harvest Grass
        Console.WriteLine("\n--- Step 1: Harvest Grass ---");
        var grassEntity = FindNearestEntity<GrassComponent>();
        if (grassEntity == Entity.Null) throw new Exception("No Grass found!");
        
        await MoveToEntity(grassEntity);
        await Interact();
        await Task.Delay(500); // Wait for sync/logic

        globalRes = GetGlobalResources();
        Console.WriteLine($"Resources after Harvest: Grass={globalRes.Grass}, Milk={globalRes.Milk}, Coins={globalRes.Coins}");
        if (globalRes.Grass <= 0) throw new Exception("Grass was not harvested!");

        // Step 2: Milk Cow
        Console.WriteLine("\n--- Step 2: Milk Cow ---");
        
        // Check if there is a cow.
        var cowEntity = FindNearestEntity<CowComponent>();
        if (cowEntity == Entity.Null)
        {
            Console.WriteLine("No Cow found. Must buy Land first.");
            
            // Step 1.5: Buy Land (Need 3 coins, we start with 5)
            Console.WriteLine("\n--- Step 1.5: Buy Land ---");
            var landEntity = FindNearestEntity<LandComponent>();
            if (landEntity == Entity.Null) throw new Exception("No Land found!");
            
            await MoveToEntity(landEntity);
            
            // Land costs 3. We have 5. Need to interact 3 times (1 coin per interact).
            for(int i=0; i<3; i++)
            {
                await Interact();
                await Task.Delay(200);
            }
            
            await Task.Delay(1000); // Wait for spawn
            
            cowEntity = FindNearestEntity<CowComponent>();
            if (cowEntity == Entity.Null) throw new Exception("Cow did not spawn after buying land!");
            Console.WriteLine("Cow Spawned!");
        }

        // Now Milk Cow
        await MoveToEntity(cowEntity);
        
        // Clicker Mechanic Test: Milk multiple times
        // We have 1 Grass from Step 1. We need Grass to milk.
        // Current Grass: 1.
        
        int startMilk = globalRes.Milk;
        Console.WriteLine($"Milking Cow... (Grass: {globalRes.Grass})");
        
        // 1. Enter House
        Console.WriteLine("Action: Enter House");
        await Interact(); 
        await Task.Delay(1100); // Wait for Enter phase (1s) to complete

        // 2. Click to Milk
        Console.WriteLine("Action: Click to Milk");
        await Interact(); 
        await Task.Delay(200); // Process click
        
        globalRes = GetGlobalResources();
        if (globalRes.Milk <= startMilk) throw new Exception("Clicker Milking failed! Milk did not increase.");
        Console.WriteLine($"Milk produced! Current Milk: {globalRes.Milk}");
        
        // 3. Wait for Exit (Auto-triggered because Grass 1 -> 0)
        Console.WriteLine("Waiting for Exit House (1s)...");
        await Task.Delay(1100);

        // Try to milk again (Should fail or start entering again if we had resources, but we don't)
        // Step 3: Sell Milk
        Console.WriteLine("\n--- Step 3: Sell Milk ---");
        var sellPoint = FindNearestEntity<SellPointComponent>();
        if (sellPoint == Entity.Null) throw new Exception("No SellPoint found!");
        
        await MoveToEntity(sellPoint);
        await Interact();
        await Task.Delay(500);

        globalRes = GetGlobalResources();
        Console.WriteLine($"Resources after Selling: Grass={globalRes.Grass}, Milk={globalRes.Milk}, Coins={globalRes.Coins}");
        
        // Expected: Started with 5 coins.
        // If we didn't buy land: 5 coins.
        // Harvested 1 grass.
        // Milked 1 milk (0 grass left).
        // Sold 1 milk (+1 coin).
        // Result: 6 coins.
        
        // If we bought land:
        // Started 5.
        // Bought land (-3) = 2.
        // Sold milk (+1) = 3.
        
        int expectedCoins = 6; // Assuming we found the pre-spawned cow
        if (globalRes.Coins < 3) throw new Exception("Coin count too low!");
        
        if (globalRes.Coins != expectedCoins)
        {
             Console.WriteLine($"[Warning] Final coins {globalRes.Coins} != Expected {expectedCoins}. (Did we buy land?)");
        }
        
        Console.WriteLine($"Final Coins: {globalRes.Coins}");
    }

    private async Task<int> WaitForPlayerSpawn()
    {
        for (int i = 0; i < 100; i++)
        {
            try
            {
                foreach (var entity in _game.State.Filter<PlayerEntity>())
                {
                    if (_game.State.HasComponent<PlayerEntity>(entity))
                    {
                        var p = _game.State.GetComponent<PlayerEntity>(entity);
                        if (p.UserId == _playerId) return entity.Id;
                    }
                }
            }
            catch {}
            await Task.Delay(100);
        }
        throw new Exception("Player failed to spawn");
    }

    private async Task MoveToEntity(Entity target)
    {
        if (!SafeHasComponent<Transform2D>(target))
        {
            Console.WriteLine($"[MoveToEntity] Target {target.Id} lost or invalid.");
            return;
        }
        
        var targetPos = _game.State.GetComponent<Transform2D>(target).Position;
        
        Console.WriteLine($"Moving to {targetPos}...");
        
        // Simple loop to move closer
        for (int i = 0; i < 200; i++) // Max 200 steps (~3 seconds)
        {
            try
            {
                if (!SafeHasComponent<Transform2D>(new Entity(_playerEntityId))) break;
                var myPos = _game.State.GetComponent<Transform2D>(new Entity(_playerEntityId)).Position;
                
                var diff = targetPos - myPos;
                if (diff.SqrMagnitude < new Float(1.0f)) break; // Close enough
                
                var dir = diff.Normalized;
                var action = new SetMoveDirectionAction { Direction = dir, Speed = 10 };
                _client.Execute(action, _playerEntityId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MoveToEntity] Error during step: {ex.Message}. Retrying...");
            }
            
            await Task.Delay(16);
        }
        
        // Stop
        _client.Execute(new SetMoveDirectionAction { Direction = Vector2.Zero, Speed = 0 }, _playerEntityId);
        Console.WriteLine("Arrived.");
        await Task.Delay(200); // Allow server to settle position
    }

    private async Task Interact()
    {
        Console.WriteLine("Interacting...");
        var action = new InteractAction { UserId = _playerId };
        _client.Execute(action, _playerEntityId);
        await Task.CompletedTask; 
    }

    private bool SafeHasComponent<T>(Entity entity) where T : struct, IComponent
    {
        try
        {
            return _game.State.HasComponent<T>(entity);
        }
        catch
        {
            return false;
        }
    }

    private Entity FindNearestEntity<T>() where T : unmanaged, IComponent
    {
        Entity nearest = Entity.Null;
        Float minDistSq = new Float(1000000); 
        
        if (!SafeHasComponent<Transform2D>(new Entity(_playerEntityId))) return nearest;
        var myPos = _game.State.GetComponent<Transform2D>(new Entity(_playerEntityId)).Position;

        try
        {
            foreach (var entity in _game.State.Filter<T>())
            {
                if (SafeHasComponent<HiddenComponent>(entity)) continue; // Skip hidden entities

                if (_game.State.HasComponent<Transform2D>(entity))
                {
                    var pos = _game.State.GetComponent<Transform2D>(entity).Position;
                    var distSq = Vector2.DistanceSquared(myPos, pos);
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        nearest = entity;
                    }
                }
            }
        }
        catch (Exception ex)
        {
             Console.WriteLine($"[FindNearestEntity] Error: {ex.Message}");
        }
        return nearest;
    }

    private GlobalResourcesComponent GetGlobalResources()
    {
        try 
        {
            foreach (var entity in _game.State.Filter<GlobalResourcesComponent>())
            {
                return _game.State.GetComponent<GlobalResourcesComponent>(entity);
            }
        }
        catch {}
        return default;
    }
}
