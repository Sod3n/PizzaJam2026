using System.Reflection;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Navigation2D.Systems;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using Template.Shared.Actions;
using Template.Shared.Systems;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

[Collection("Sequential")]
public class CowFollowTests
{
    private readonly ITestOutputHelper _output;

    public CowFollowTests(ITestOutputHelper output) => _output = output;

    private Game CreateGame()
    {
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        return TemplateGameFactory.CreateGame(tickRate: 60);
    }

    private Entity SpawnPlayer(Game game, System.Guid userId)
    {
        var ctx = new Context(game.State, Entity.Null, null!);
        return PlayerDefinition.Create(ctx, userId, new Vector2(10, 10), 0);
    }

    private void RunTicks(Game game, int count)
    {
        for (int i = 0; i < count; i++)
            game.Loop.RunSingleTick();
    }

    private void DispatchAction<T>(Game game, T action, Entity target) where T : struct, IAction
    {
        game.State.AddComponent(target, action);
        game.Dispatcher.Update(game.State);
        game.Loop.Simulation.SystemRunner.Update(game.State);
    }

    [Fact]
    public void AltInteract_NearCow_ShouldEnterTamingState()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        Entity cow = Entity.Null;
        foreach (var e in game.State.Filter<CowComponent>())
        { cow = e; break; }

        var cowPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = cowPos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);

        var sc = game.State.GetComponent<StateComponent>(player);
        sc.Key.ToString().Should().Be(StateKeys.Taming);
        sc.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AltInteract_AfterTamingComplete_CowShouldFollow()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        Entity cow = Entity.Null;
        foreach (var e in game.State.Filter<CowComponent>())
        { cow = e; break; }

        var cowPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = cowPos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);

        // Wait for taming_enter to complete (60 ticks)
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);

        var cowComp = game.State.GetComponent<CowComponent>(cow);
        cowComp.FollowingPlayer.Should().Be(player);

        var playerState = game.State.GetComponent<PlayerStateComponent>(player);
        playerState.FollowingCow.Should().Be(cow);

        var sc = game.State.GetComponent<StateComponent>(player);
        sc.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void FollowingCow_Navigation_DiagnosticTest()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        Entity cow = Entity.Null;
        foreach (var e in game.State.Filter<CowComponent>())
        { cow = e; break; }

        // Move the other cow far away so it doesn't physically block
        foreach (var e in game.State.Filter<CowComponent>())
        {
            if (e != cow)
            {
                ref var otherT = ref game.State.GetComponent<Transform2D>(e);
                otherT.Position = new Vector2(-10, -10);
            }
        }

        // Tame the cow
        var cowPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = cowPos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);
        game.State.GetComponent<CowComponent>(cow).FollowingPlayer.Should().Be(player);

        // Move player away from cow
        var cowStartPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var playerT = ref game.State.GetComponent<Transform2D>(player);
        playerT.Position = cowStartPos + new Vector2(15, 0);

        _output.WriteLine($"=== INITIAL STATE ===");
        _output.WriteLine($"Cow: ({(float)cowStartPos.X}, {(float)cowStartPos.Y})");
        _output.WriteLine($"Player: ({(float)playerT.Position.X}, {(float)playerT.Position.Y})");

        // Check NavigationWorld2D exists
        bool hasNavWorld = false;
        foreach (var e in game.State.Filter<NavigationWorld2D>())
        {
            var nw = game.State.GetComponent<NavigationWorld2D>(e);
            _output.WriteLine($"NavWorld: Bounds=({(float)nw.BoundsMin.X},{(float)nw.BoundsMin.Y})-({(float)nw.BoundsMax.X},{(float)nw.BoundsMax.Y}) CellSize={nw.CellSize} AgentRadius={nw.AgentRadius} ForceBake={nw.ForceBake}");
            hasNavWorld = true;
        }
        _output.WriteLine($"HasNavWorld: {hasNavWorld}");

        // Run tick-by-tick and log
        _output.WriteLine($"\n=== TICK-BY-TICK ===");
        for (int tick = 0; tick < 120; tick++)
        {
            game.Loop.RunSingleTick();

            if (tick % 20 == 0 || tick < 5)
            {
                var cp = game.State.GetComponent<Transform2D>(cow).Position;
                var nav = game.State.GetComponent<NavigationAgent2D>(cow);
                var body = game.State.GetComponent<CharacterBody2D>(cow);
                var pp = game.State.GetComponent<Transform2D>(player).Position;

                _output.WriteLine($"T{tick}: Cow=({(float)cp.X:F1},{(float)cp.Y:F1}) " +
                    $"NavTarget=({(float)nav.TargetPosition.X:F1},{(float)nav.TargetPosition.Y:F1}) " +
                    $"NavVel=({(float)nav.Velocity.X:F2},{(float)nav.Velocity.Y:F2}) " +
                    $"BodyVel=({(float)body.Velocity.X:F2},{(float)body.Velocity.Y:F2}) " +
                    $"Finished={nav.IsNavigationFinished} Reachable={nav.IsTargetReachable} " +
                    $"Dist={Vector2.Distance(cp, pp):F1}");

            }
        }

        // Final check
        var cowEndPos = game.State.GetComponent<Transform2D>(cow).Position;
        var distBefore = Vector2.Distance(cowStartPos, playerT.Position);
        var distAfter = Vector2.Distance(cowEndPos, playerT.Position);
        _output.WriteLine($"\n=== RESULT ===");
        _output.WriteLine($"Cow moved: {Vector2.Distance(cowStartPos, cowEndPos):F2}");
        _output.WriteLine($"Dist to player: {distBefore:F1} -> {distAfter:F1}");

        distAfter.Should().BeLessThan(distBefore, "Cow should move closer to the player");
    }

    [Fact]
    public void FollowingCow_WhilePlayerMoving_ShouldFollowSmoothly()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        Entity cow = Entity.Null;
        foreach (var e in game.State.Filter<CowComponent>())
        { cow = e; break; }

        // Move other cow away
        foreach (var e in game.State.Filter<CowComponent>())
        {
            if (e != cow)
            {
                ref var ot = ref game.State.GetComponent<Transform2D>(e);
                ot.Position = new Vector2(-10, -10);
            }
        }

        // Tame cow
        var cowPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = cowPos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);
        game.State.GetComponent<CowComponent>(cow).FollowingPlayer.Should().Be(player);

        // Now simulate player moving right at constant velocity
        _output.WriteLine("=== PLAYER MOVING RIGHT, COW FOLLOWING ===");

        // Set player velocity via action
        var moveAction = new Template.Shared.Features.Movement.SetMoveDirectionAction
        {
            Direction = new Vector2(1, 0),
            Speed = 10
        };
        game.State.AddComponent(player, moveAction);
        game.Dispatcher.Update(game.State);

        for (int tick = 0; tick < 120; tick++)
        {
            // Keep sending move action every few ticks (like real input)
            if (tick % 10 == 0)
            {
                game.State.AddComponent(player, moveAction);
                game.Dispatcher.Update(game.State);
            }

            game.Loop.RunSingleTick();

            if (tick % 10 == 0)
            {
                var pp = game.State.GetComponent<Transform2D>(player).Position;
                var cp = game.State.GetComponent<Transform2D>(cow).Position;
                var nav = game.State.GetComponent<NavigationAgent2D>(cow);
                var body = game.State.GetComponent<CharacterBody2D>(cow);
                var dist = Vector2.Distance(pp, cp);

                _output.WriteLine($"T{tick}: Player=({(float)pp.X:F1},{(float)pp.Y:F1}) " +
                    $"Cow=({(float)cp.X:F1},{(float)cp.Y:F1}) " +
                    $"Dist={dist:F1} " +
                    $"NavVel=({(float)nav.Velocity.X:F1},{(float)nav.Velocity.Y:F1}) " +
                    $"BodyVel=({(float)body.Velocity.X:F1},{(float)body.Velocity.Y:F1}) " +
                    $"Finished={nav.IsNavigationFinished} Reachable={nav.IsTargetReachable}");
            }
        }

        // Cow should be following at a reasonable distance (not stuck or oscillating)
        var finalPlayerPos = game.State.GetComponent<Transform2D>(player).Position;
        var finalCowPos = game.State.GetComponent<Transform2D>(cow).Position;
        var finalDist = Vector2.Distance(finalPlayerPos, finalCowPos);
        _output.WriteLine($"\nFinal dist: {finalDist:F1}");

        // Cow should be within reasonable following distance, not stuck far behind
        ((float)finalDist).Should().BeLessThan(8f, "Cow should keep up with the player");
    }

    [Fact]
    public void FollowingCow_WithObstacleBetween_ShouldNavigateAround()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        Entity cow = Entity.Null;
        foreach (var e in game.State.Filter<CowComponent>())
        { cow = e; break; }

        // Move other cow far away
        foreach (var e in game.State.Filter<CowComponent>())
        {
            if (e != cow)
            {
                ref var ot = ref game.State.GetComponent<Transform2D>(e);
                ot.Position = new Vector2(-20, -20);
            }
        }

        // Tame the cow
        var cowPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = cowPos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);
        game.State.GetComponent<CowComponent>(cow).FollowingPlayer.Should().Be(player);

        // Place cow at a known position and a wall between cow and player
        ref var cowT = ref game.State.GetComponent<Transform2D>(cow);
        cowT.Position = new Vector2(20, 20);

        // Create a wall obstacle blocking the direct path (horizontal wall at Y=23, from X=15 to X=30)
        var wall = game.State.CreateEntity();
        game.State.AddComponent(wall, new Transform2D(new Vector2(22, 23), 0, Vector2.One));
        game.State.AddComponent(wall, new StaticBody2D());
        game.State.AddComponent(wall, CollisionShape2D.CreateRectangle(new Vector2(16, 1)));
        game.State.AddComponent(wall, new WallComponent());

        // Player on the other side of the wall
        ref var playerT = ref game.State.GetComponent<Transform2D>(player);
        playerT.Position = new Vector2(20, 28);

        // Force nav mesh rebake so the wall is included
        foreach (var e in game.State.Filter<NavigationWorld2D>())
        {
            ref var nw = ref game.State.GetComponent<NavigationWorld2D>(e);
            nw.ForceBake = true;
        }

        // Let physics + nav mesh bake settle
        RunTicks(game, 5);

        var cowStartPos = game.State.GetComponent<Transform2D>(cow).Position;
        var playerPos = game.State.GetComponent<Transform2D>(player).Position;
        var initialDist = Vector2.Distance(cowStartPos, playerPos);

        _output.WriteLine($"=== OBSTACLE NAVIGATION TEST ===");
        _output.WriteLine($"Cow start: ({(float)cowStartPos.X:F1}, {(float)cowStartPos.Y:F1})");
        _output.WriteLine($"Player: ({(float)playerPos.X:F1}, {(float)playerPos.Y:F1})");
        _output.WriteLine($"Wall: (22, 23) size (16, 1) — blocks direct path");
        _output.WriteLine($"Initial dist: {initialDist:F1}");

        // Run simulation and log cow movement
        for (int tick = 0; tick < 180; tick++)
        {
            game.Loop.RunSingleTick();

            if (tick % 20 == 0 || tick < 5)
            {
                var cp = game.State.GetComponent<Transform2D>(cow).Position;
                var nav = game.State.GetComponent<NavigationAgent2D>(cow);
                var body = game.State.GetComponent<CharacterBody2D>(cow);

                _output.WriteLine($"T{tick}: Cow=({(float)cp.X:F1},{(float)cp.Y:F1}) " +
                    $"NavVel=({(float)nav.Velocity.X:F2},{(float)nav.Velocity.Y:F2}) " +
                    $"BodyVel=({(float)body.Velocity.X:F2},{(float)body.Velocity.Y:F2}) " +
                    $"Finished={nav.IsNavigationFinished} Reachable={nav.IsTargetReachable} " +
                    $"Dist={Vector2.Distance(cp, playerPos):F1}");
            }
        }

        var cowEndPos = game.State.GetComponent<Transform2D>(cow).Position;
        var finalDist = Vector2.Distance(cowEndPos, playerPos);
        _output.WriteLine($"\n=== RESULT ===");
        _output.WriteLine($"Cow end: ({(float)cowEndPos.X:F1}, {(float)cowEndPos.Y:F1})");
        _output.WriteLine($"Dist: {initialDist:F1} -> {finalDist:F1}");
        _output.WriteLine($"Cow moved: {Vector2.Distance(cowStartPos, cowEndPos):F2}");

        // The cow should have moved closer to the player by navigating around the wall
        finalDist.Should().BeLessThan(initialDist, "Cow should navigate around the obstacle to reach the player");

        // Cow should not have gone through the wall — check it actually went around
        // If nav works, cow's X should have deviated from the straight-line path at some point
        // At minimum, cow should be substantially closer
        ((float)finalDist).Should().BeLessThan(5f, "Cow should reach close to the player after navigating around the wall");
    }

    [Fact]
    public void FollowingCow_NearObstacle_PlayerStraightBehind_ShouldNotPathThroughWall()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        Entity cow = Entity.Null;
        foreach (var e in game.State.Filter<CowComponent>())
        { cow = e; break; }

        // Move other cows far away
        foreach (var e in game.State.Filter<CowComponent>())
        {
            if (e != cow)
            {
                ref var ot = ref game.State.GetComponent<Transform2D>(e);
                ot.Position = new Vector2(-20, -20);
            }
        }

        // Tame the cow
        var cowPos = game.State.GetComponent<Transform2D>(cow).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = cowPos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);
        game.State.GetComponent<CowComponent>(cow).FollowingPlayer.Should().Be(player);

        // Create a wide horizontal wall at Y=23
        var wall = game.State.CreateEntity();
        game.State.AddComponent(wall, new Transform2D(new Vector2(22, 23), 0, Vector2.One));
        game.State.AddComponent(wall, new StaticBody2D());
        game.State.AddComponent(wall, CollisionShape2D.CreateRectangle(new Vector2(16, 1)));
        game.State.AddComponent(wall, new WallComponent());

        // Cow near the wall (slightly away from inflation boundary), player directly behind
        ref var cowT = ref game.State.GetComponent<Transform2D>(cow);
        cowT.Position = new Vector2(22, 21.5f);

        ref var playerT = ref game.State.GetComponent<Transform2D>(player);
        playerT.Position = new Vector2(22, 28);

        // Force nav mesh rebake
        foreach (var e in game.State.Filter<NavigationWorld2D>())
        {
            ref var nw = ref game.State.GetComponent<NavigationWorld2D>(e);
            nw.ForceBake = true;
        }
        RunTicks(game, 5);

        var cowStartPos = game.State.GetComponent<Transform2D>(cow).Position;
        var playerPos = game.State.GetComponent<Transform2D>(player).Position;

        _output.WriteLine($"=== COW NEAR WALL, PLAYER STRAIGHT BEHIND ===");
        _output.WriteLine($"Cow: ({(float)cowStartPos.X:F1}, {(float)cowStartPos.Y:F1})");
        _output.WriteLine($"Player: ({(float)playerPos.X:F1}, {(float)playerPos.Y:F1})");
        _output.WriteLine($"Wall: center=(22,23) size=(16,1) covers X=[14..30] Y=[22.5..23.5]");

        // Check nav mesh state
        var navState = game.State.GetCustomData<NavigationState>();
        var map = navState?.Map;
        if (map != null)
        {
            int cowTri = map.FindTriangle(cowStartPos);
            int playerTri = map.FindTriangle(playerPos);
            _output.WriteLine($"Cow triangle: {cowTri} (on-mesh: {cowTri >= 0})");
            _output.WriteLine($"Player triangle: {playerTri} (on-mesh: {playerTri >= 0})");
            _output.WriteLine($"Total triangles: {map.Triangles.Count}");

            // Check wall center
            int wallTri = map.FindTriangle(new Vector2(22, 23));
            _output.WriteLine($"Wall center triangle: {wallTri} (should be -1 if carved)");

            // Check some test points around the wall
            for (float y = 18; y <= 28; y += 2)
            {
                int tri = map.FindTriangle(new Vector2(22, (Float)y));
                _output.WriteLine($"  Point (22, {y}): tri={tri} {(tri >= 0 ? "ON-MESH" : "off-mesh")}");
            }

            if (cowTri < 0)
            {
                var (closestTri, closestPt) = map.FindClosestTriangle(cowStartPos);
                _output.WriteLine($"Cow closest on-mesh point: ({(float)closestPt.X:F1}, {(float)closestPt.Y:F1}) tri={closestTri}");
            }
            if (playerTri < 0)
            {
                var (closestTri, closestPt) = map.FindClosestTriangle(playerPos);
                _output.WriteLine($"Player closest on-mesh point: ({(float)closestPt.X:F1}, {(float)closestPt.Y:F1}) tri={closestTri}");
            }

            // Check NavigationWorld2D settings
            foreach (var e in game.State.Filter<NavigationWorld2D>())
            {
                var nw = game.State.GetComponent<NavigationWorld2D>(e);
                _output.WriteLine($"NavWorld: AgentRadius={nw.AgentRadius} CellSize={nw.CellSize} ChunkSize={nw.ChunkSize}");
            }
        }

        // Wall collision body zone (not inflation zone — the actual physical wall)
        float wallYMin = 22.5f;
        float wallYMax = 23.5f;
        float wallXMin = 14.0f;
        float wallXMax = 30.0f;
        bool pathThroughWall = false;
        bool cowEnteredWall = false;

        for (int tick = 0; tick < 360; tick++)
        {
            game.Loop.RunSingleTick();

            var cp = game.State.GetComponent<Transform2D>(cow).Position;
            float cx = (float)cp.X;
            float cy = (float)cp.Y;

            if (cy >= wallYMin && cy <= wallYMax && cx >= wallXMin && cx <= wallXMax)
            {
                if (!cowEnteredWall)
                {
                    _output.WriteLine($"T{tick}: COW ENTERED WALL ZONE at ({cx:F2},{cy:F2})");
                    cowEnteredWall = true;
                }
            }

            // Check path waypoints for wall crossing
            if (navState != null && navState.AgentPaths.TryGetValue(cow.Id, out var pathData) && pathData.PathPoints.Count > 0)
            {
                for (int i = 0; i < pathData.PathPoints.Count; i++)
                {
                    var wp = pathData.PathPoints[i];
                    float wx = (float)wp.X;
                    float wy = (float)wp.Y;
                    if (wy >= wallYMin && wy <= wallYMax && wx >= wallXMin && wx <= wallXMax)
                    {
                        if (!pathThroughWall)
                        {
                            _output.WriteLine($"T{tick}: PATH WAYPOINT [{i}] INSIDE WALL at ({wx:F2},{wy:F2})");
                            // Log all waypoints
                            for (int j = 0; j < pathData.PathPoints.Count; j++)
                            {
                                var p = pathData.PathPoints[j];
                                _output.WriteLine($"  WP[{j}]: ({(float)p.X:F2},{(float)p.Y:F2})");
                            }
                            pathThroughWall = true;
                        }
                    }
                }
            }

            if (tick % 30 == 0 || tick < 3)
            {
                var nav = game.State.GetComponent<NavigationAgent2D>(cow);
                _output.WriteLine($"T{tick}: Cow=({cx:F1},{cy:F1}) " +
                    $"NavVel=({(float)nav.Velocity.X:F2},{(float)nav.Velocity.Y:F2}) " +
                    $"Finished={nav.IsNavigationFinished} Reachable={nav.IsTargetReachable} " +
                    $"Dist={Vector2.Distance(cp, playerPos):F1}");
            }
        }

        var cowEndPos = game.State.GetComponent<Transform2D>(cow).Position;
        var finalDist = (float)Vector2.Distance(cowEndPos, playerPos);
        var initialDist = (float)Vector2.Distance(cowStartPos, playerPos);

        _output.WriteLine($"\n=== RESULT ===");
        _output.WriteLine($"Cow end: ({(float)cowEndPos.X:F1}, {(float)cowEndPos.Y:F1})");
        _output.WriteLine($"Dist: {initialDist:F1} -> {finalDist:F1}");
        _output.WriteLine($"Path through wall: {pathThroughWall}");
        _output.WriteLine($"Cow body entered wall: {cowEnteredWall}");

        // Path waypoints should NOT cross through the wall body
        pathThroughWall.Should().BeFalse("Nav path should go around the wall, not through it");

        // Cow should move closer to the player (allowing for corner navigation)
        finalDist.Should().BeLessThan(initialDist + 2f, "Cow should not get permanently stuck");
        finalDist.Should().BeLessThan(12f, "Cow should navigate around the wall");

        // Verify the path doesn't have segments that cross the wall body
        if (navState != null && navState.AgentPaths.TryGetValue(cow.Id, out var finalPathData) && finalPathData.PathPoints.Count > 1)
        {
            for (int i = 0; i < finalPathData.PathPoints.Count - 1; i++)
            {
                var a = finalPathData.PathPoints[i];
                var b = finalPathData.PathPoints[i + 1];
                // Check if the segment from a to b crosses the wall body
                bool segmentCrossesWall = SegmentIntersectsRect(
                    (float)a.X, (float)a.Y, (float)b.X, (float)b.Y,
                    wallXMin, wallYMin, wallXMax, wallYMax);
                segmentCrossesWall.Should().BeFalse(
                    $"Path segment [{i}]({(float)a.X:F1},{(float)a.Y:F1})->({(float)b.X:F1},{(float)b.Y:F1}) should not cross wall body");
            }
        }
    }

    /// <summary>
    /// Test if a line segment from (x1,y1)-(x2,y2) intersects a rectangle.
    /// </summary>
    private static bool SegmentIntersectsRect(float x1, float y1, float x2, float y2,
        float rxMin, float ryMin, float rxMax, float ryMax)
    {
        // Cohen-Sutherland-style clipping
        float tMin = 0f, tMax = 1f;
        float dx = x2 - x1, dy = y2 - y1;

        float[] p = { -dx, dx, -dy, dy };
        float[] q = { x1 - rxMin, rxMax - x1, y1 - ryMin, ryMax - y1 };

        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-10f)
            {
                if (q[i] < 0) return false;
            }
            else
            {
                float t = q[i] / p[i];
                if (p[i] < 0) { if (t > tMin) tMin = t; }
                else { if (t < tMax) tMax = t; }
                if (tMin > tMax) return false;
            }
        }
        return true;
    }

    [Fact]
    public void LandPurchase_ShouldNotSpawnCow()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        int initialCowCount = 0;
        foreach (var _ in game.State.Filter<CowComponent>())
            initialCowCount++;
        initialCowCount.Should().Be(2);

        Entity land = Entity.Null;
        foreach (var e in game.State.Filter<LandComponent>())
        { land = e; break; }

        var landPos = game.State.GetComponent<Transform2D>(land).Position;
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = landPos;
        RunTicks(game, 5);

        for (int i = 0; i < 3; i++)
            DispatchAction(game, new InteractAction { UserId = userId }, player);

        int finalCowCount = 0;
        foreach (var _ in game.State.Filter<CowComponent>())
            finalCowCount++;
        finalCowCount.Should().Be(2);
    }

    [Fact]
    public void Crossbreed_WithFollowingCow_ShouldSpawnNewCow()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);

        // Need cows < houses for crossbreeding (2 cows, need 3+ houses)
        // Give enough coins to buy 3 land plots
        foreach (var ge in game.State.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref game.State.GetComponent<GlobalResourcesComponent>(ge);
            gr.Coins = 20;
        }

        // Buy 3 land plots to create 3 houses
        var lands = new System.Collections.Generic.List<Entity>();
        foreach (var e in game.State.Filter<LandComponent>())
            lands.Add(e);

        for (int p = 0; p < 3 && p < lands.Count; p++)
        {
            ref var pt0 = ref game.State.GetComponent<Transform2D>(player);
            pt0.Position = game.State.GetComponent<Transform2D>(lands[p]).Position;
            RunTicks(game, 5);

            for (int i = 0; i < 3; i++)
                DispatchAction(game, new InteractAction { UserId = userId }, player);
            RunTicks(game, 2);
        }

        // Get the two cows
        var cows = new System.Collections.Generic.List<Entity>();
        foreach (var e in game.State.Filter<CowComponent>())
            cows.Add(e);
        cows.Count.Should().Be(2);

        var cow1 = cows[0];
        var cow2 = cows[1];

        // Move player near cow1 and alt-interact to tame
        var cow1Pos = game.State.GetComponent<Transform2D>(cow1).Position;
        ref var pt2 = ref game.State.GetComponent<Transform2D>(player);
        pt2.Position = cow1Pos + new Vector2(1, 0);
        RunTicks(game, 5);

        DispatchAction(game, new AltInteractAction { UserId = userId }, player);
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);

        game.State.GetComponent<CowComponent>(cow1).FollowingPlayer.Should().Be(player);

        // Move near cow2, also move cow1 nearby
        var cow2Pos = game.State.GetComponent<Transform2D>(cow2).Position;
        ref var pt3 = ref game.State.GetComponent<Transform2D>(player);
        pt3.Position = cow2Pos + new Vector2(1, 0);
        ref var cow1T = ref game.State.GetComponent<Transform2D>(cow1);
        cow1T.Position = cow2Pos + new Vector2(0, 1);
        RunTicks(game, 5);

        DispatchAction(game, new InteractAction { UserId = userId }, player);

        var sc = game.State.GetComponent<StateComponent>(player);
        _output.WriteLine($"State after breed interact: Key='{sc.Key}' IsEnabled={sc.IsEnabled}");
        sc.Key.ToString().Should().Be(StateKeys.Breed);

        // Wait for breeding to complete
        RunTicks(game, StateDefinitions.PhaseDurationTicks + 1);

        int cowCount = 0;
        foreach (var _ in game.State.Filter<CowComponent>())
            cowCount++;
        cowCount.Should().Be(3);
    }
}
