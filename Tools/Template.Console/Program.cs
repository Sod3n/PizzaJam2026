using System.Globalization;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;

namespace Template.Console;

public class GameConsole
{
    private Game _game = null!;
    private readonly Dictionary<string, (Entity entity, Guid userId)> _players = new();
    private int _tickCount;

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var console = new GameConsole();

        if (args.Length > 0)
        {
            // Script mode: run commands from file
            if (!File.Exists(args[0]))
            {
                System.Console.Error.WriteLine($"File not found: {args[0]}");
                return 1;
            }
            var lines = File.ReadAllLines(args[0]);
            foreach (var line in lines)
            {
                if (!console.ExecuteLine(line))
                    return 1;
            }
            return 0;
        }

        // Interactive REPL
        System.Console.WriteLine("Game Console — type 'help' for commands, 'quit' to exit");
        console.ExecuteLine("init");

        while (true)
        {
            System.Console.Write($"[T{console._tickCount}]> ");
            var line = System.Console.ReadLine();
            if (line == null) break;
            if (line.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
            console.ExecuteLine(line);
        }

        return 0;
    }

    public bool ExecuteLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith('#')) return true;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "init": Init(); break;
                case "spawn": CmdSpawn(parts); break;
                case "tick": CmdTick(parts); break;
                case "move": CmdMove(parts); break;
                case "teleport": CmdTeleport(parts); break;
                case "interact": CmdInteract(parts); break;
                case "status": CmdStatus(parts); break;
                case "list": CmdList(parts); break;
                case "resources": CmdResources(); break;
                case "set-resources": CmdSetResources(parts); break;
                case "assert": CmdAssert(parts); break;
                case "help": PrintHelp(); break;
                case "print": CmdPrint(parts); break;
                default:
                    System.Console.WriteLine($"Unknown command: {cmd}. Type 'help' for commands.");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"ERROR: {ex.Message}");
            return false;
        }

        return true;
    }

    private void Init()
    {
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        _game = TemplateGameFactory.CreateGame(tickRate: 60);
        _players.Clear();
        _tickCount = 0;
        System.Console.WriteLine("Game initialized (60Hz). Scene loaded with starting entities.");
        PrintEntityCounts();
    }

    private void CmdSpawn(string[] parts)
    {
        // spawn <name> [x y]
        if (parts.Length < 2) { System.Console.WriteLine("Usage: spawn <name> [x y]"); return; }
        var name = parts[1];
        float x = parts.Length > 3 ? float.Parse(parts[2]) : 10f;
        float y = parts.Length > 3 ? float.Parse(parts[3]) : 10f;

        var userId = Guid.NewGuid();
        var ctx = new Context(_game.State, Entity.Null, null!);
        var entity = PlayerDefinition.Create(ctx, userId, new Vector2((Float)x, (Float)y), 0);
        _players[name] = (entity, userId);
        System.Console.WriteLine($"Spawned player '{name}' (entity {entity.Id}) at ({x}, {y})");
    }

    private void CmdTick(string[] parts)
    {
        // tick [count]
        int count = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        for (int i = 0; i < count; i++)
        {
            _game.Loop.RunSingleTick();
            _tickCount++;
        }
        System.Console.WriteLine($"Advanced {count} tick(s) → T{_tickCount}");
    }

    private void CmdMove(string[] parts)
    {
        // move <player> <dx> <dy> [speed] [ticks]
        if (parts.Length < 4) { System.Console.WriteLine("Usage: move <player> <dx> <dy> [speed] [ticks]"); return; }
        var (entity, _) = GetPlayer(parts[1]);
        float dx = float.Parse(parts[2]);
        float dy = float.Parse(parts[3]);
        float speed = parts.Length > 4 ? float.Parse(parts[4]) : 10f;
        int ticks = parts.Length > 5 ? int.Parse(parts[5]) : 60;

        var moveAction = new Template.Shared.Features.Movement.SetMoveDirectionAction
        {
            Direction = new Vector2((Float)dx, (Float)dy),
            Speed = (Float)speed
        };

        for (int i = 0; i < ticks; i++)
        {
            if (i % 10 == 0)
            {
                _game.State.AddComponent(entity, moveAction);
                _game.Dispatcher.Update(_game.State);
            }
            _game.Loop.RunSingleTick();
            _tickCount++;
        }

        var pos = _game.State.GetComponent<Transform2D>(entity).Position;
        System.Console.WriteLine($"Moved for {ticks} ticks → position ({(float)pos.X:F1}, {(float)pos.Y:F1})");
    }

    private void CmdTeleport(string[] parts)
    {
        // teleport <player> <x> <y>
        if (parts.Length < 4) { System.Console.WriteLine("Usage: teleport <player> <x> <y>"); return; }
        var (entity, _) = GetPlayer(parts[1]);
        float x = float.Parse(parts[2]);
        float y = float.Parse(parts[3]);
        ref var t = ref _game.State.GetComponent<Transform2D>(entity);
        t.Position = new Vector2((Float)x, (Float)y);
        System.Console.WriteLine($"Teleported '{parts[1]}' to ({x}, {y})");
    }

    private void CmdInteract(string[] parts)
    {
        // interact <player>
        if (parts.Length < 2) { System.Console.WriteLine("Usage: interact <player>"); return; }
        var (entity, userId) = GetPlayer(parts[1]);
        DispatchAction(_game, new InteractAction { UserId = userId }, entity);
        var sc = _game.State.GetComponent<StateComponent>(entity);
        System.Console.WriteLine($"Interact → state: Key='{sc.Key}' Phase={sc.Phase} Enabled={sc.IsEnabled}");
    }


    private void CmdStatus(string[] parts)
    {
        // status <player> | status cow [index] | status <entity-id>
        if (parts.Length < 2) { System.Console.WriteLine("Usage: status <player|cow|entity-id> [index]"); return; }

        var target = parts[1].ToLowerInvariant();

        if (_players.ContainsKey(parts[1]))
        {
            var (entity, _) = _players[parts[1]];
            PrintPlayerStatus(parts[1], entity);
            return;
        }

        if (target == "cow")
        {
            int index = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            int i = 0;
            foreach (var e in _game.State.Filter<CowComponent>())
            {
                if (i == index) { PrintCowStatus(e); return; }
                i++;
            }
            System.Console.WriteLine($"No cow at index {index}");
            return;
        }

        // Try entity ID
        if (int.TryParse(parts[1], out var id))
        {
            var entity = new Entity { Id = id };
            var pos = _game.State.GetComponent<Transform2D>(entity).Position;
            System.Console.WriteLine($"Entity {id}: pos=({(float)pos.X:F1}, {(float)pos.Y:F1})");
            return;
        }

        System.Console.WriteLine($"Unknown target: {parts[1]}");
    }

    private void CmdList(string[] parts)
    {
        // list [type]
        var type = parts.Length > 1 ? parts[1].ToLowerInvariant() : "all";

        if (type == "all" || type == "players")
        {
            System.Console.WriteLine("=== Players ===");
            foreach (var (name, (entity, _)) in _players)
            {
                var pos = _game.State.GetComponent<Transform2D>(entity).Position;
                System.Console.WriteLine($"  {name}: entity={entity.Id} pos=({(float)pos.X:F1}, {(float)pos.Y:F1})");
            }
        }
        if (type == "all" || type == "cows")
        {
            System.Console.WriteLine("=== Cows ===");
            int i = 0;
            foreach (var e in _game.State.Filter<CowComponent>())
            {
                var pos = _game.State.GetComponent<Transform2D>(e).Position;
                var cow = _game.State.GetComponent<CowComponent>(e);
                var following = cow.FollowingPlayer != Entity.Null ? $"following={cow.FollowingPlayer.Id}" : "wild";
                System.Console.WriteLine($"  [{i}] entity={e.Id} pos=({(float)pos.X:F1}, {(float)pos.Y:F1}) {following} exhaust={cow.Exhaust}/{cow.MaxExhaust}");
                i++;
            }
        }
        if (type == "all" || type == "houses")
        {
            System.Console.WriteLine("=== Houses ===");
            int i = 0;
            foreach (var e in _game.State.Filter<HouseComponent>())
            {
                var pos = _game.State.GetComponent<Transform2D>(e).Position;
                var house = _game.State.GetComponent<HouseComponent>(e);
                System.Console.WriteLine($"  [{i}] entity={e.Id} pos=({(float)pos.X:F1}, {(float)pos.Y:F1}) cowId={house.CowId.Id}");
                i++;
            }
        }
        if (type == "all" || type == "grass")
        {
            int count = 0;
            foreach (var _ in _game.State.Filter<GrassComponent>()) count++;
            System.Console.WriteLine($"=== Grass: {count} patches ===");
        }
        if (type == "all" || type == "land")
        {
            System.Console.WriteLine("=== Land Plots ===");
            int i = 0;
            foreach (var e in _game.State.Filter<LandComponent>())
            {
                var pos = _game.State.GetComponent<Transform2D>(e).Position;
                var land = _game.State.GetComponent<LandComponent>(e);
                System.Console.WriteLine($"  [{i}] entity={e.Id} pos=({(float)pos.X:F1}, {(float)pos.Y:F1}) coins={land.CurrentCoins}/{land.Threshold}");
                i++;
            }
        }

        PrintEntityCounts();
    }

    private void CmdResources()
    {
        foreach (var e in _game.State.Filter<GlobalResourcesComponent>())
        {
            var r = _game.State.GetComponent<GlobalResourcesComponent>(e);
            System.Console.WriteLine($"Resources: grass={r.Grass} milk={r.Milk} coins={r.Coins}");
            return;
        }
        System.Console.WriteLine("No GlobalResourcesComponent found");
    }

    private void CmdSetResources(string[] parts)
    {
        // set-resources <grass> <milk> <coins>
        if (parts.Length < 4) { System.Console.WriteLine("Usage: set-resources <grass> <milk> <coins>"); return; }
        foreach (var e in _game.State.Filter<GlobalResourcesComponent>())
        {
            ref var r = ref _game.State.GetComponent<GlobalResourcesComponent>(e);
            r.Grass = int.Parse(parts[1]);
            r.Milk = int.Parse(parts[2]);
            r.Coins = int.Parse(parts[3]);
            System.Console.WriteLine($"Resources set: grass={r.Grass} milk={r.Milk} coins={r.Coins}");
            return;
        }
    }

    private void CmdAssert(string[] parts)
    {
        // assert <player> state <key>
        // assert <player> following-cow
        // assert <player> no-state
        // assert cows <count>
        // assert resources coins <value>
        if (parts.Length < 3) { System.Console.WriteLine("Usage: assert <target> <condition> [value]"); return; }

        var target = parts[1];
        var condition = parts[2].ToLowerInvariant();

        if (target == "cows")
        {
            int expected = int.Parse(parts[2]);
            int actual = 0;
            foreach (var _ in _game.State.Filter<CowComponent>()) actual++;
            AssertEqual("cow count", expected, actual);
            return;
        }

        if (target == "resources")
        {
            var field = condition;
            int expected = int.Parse(parts[3]);
            foreach (var e in _game.State.Filter<GlobalResourcesComponent>())
            {
                var r = _game.State.GetComponent<GlobalResourcesComponent>(e);
                int actual = field switch
                {
                    "grass" => r.Grass,
                    "milk" => r.Milk,
                    "coins" => r.Coins,
                    _ => throw new Exception($"Unknown resource: {field}")
                };
                AssertEqual($"resources.{field}", expected, actual);
                return;
            }
        }

        var (entity, _) = GetPlayer(target);
        switch (condition)
        {
            case "state":
                var sc = _game.State.GetComponent<StateComponent>(entity);
                var expectedKey = parts.Length > 3 ? parts[3] : "";
                AssertEqual("state.Key", expectedKey, sc.Key.ToString());
                break;

            case "no-state":
                var sc2 = _game.State.GetComponent<StateComponent>(entity);
                AssertEqual("state.IsEnabled", false, sc2.IsEnabled);
                break;

            case "following-cow":
                var ps = _game.State.GetComponent<PlayerStateComponent>(entity);
                if (ps.FollowingCow == Entity.Null)
                    throw new Exception("ASSERT FAILED: player is not following any cow");
                System.Console.WriteLine($"ASSERT OK: player following cow entity {ps.FollowingCow.Id}");
                break;

            case "not-following":
                var ps2 = _game.State.GetComponent<PlayerStateComponent>(entity);
                AssertEqual("FollowingCow", Entity.Null.Id, ps2.FollowingCow.Id);
                break;

            case "near":
                if (parts.Length < 4) { System.Console.WriteLine("Usage: assert <player> near <x> <y> [maxDist]"); return; }
                float ex = float.Parse(parts[3]);
                float ey = float.Parse(parts[4]);
                float maxDist = parts.Length > 5 ? float.Parse(parts[5]) : 3f;
                var pos = _game.State.GetComponent<Transform2D>(entity).Position;
                var dist = (float)Vector2.Distance(pos, new Vector2((Float)ex, (Float)ey));
                if (dist > maxDist)
                    throw new Exception($"ASSERT FAILED: distance {dist:F1} > {maxDist:F1}");
                System.Console.WriteLine($"ASSERT OK: distance={dist:F1} <= {maxDist:F1}");
                break;

            default:
                System.Console.WriteLine($"Unknown assert condition: {condition}");
                break;
        }
    }

    private void CmdPrint(string[] parts)
    {
        // print <message...>
        System.Console.WriteLine(string.Join(' ', parts.Skip(1)));
    }

    private void PrintHelp()
    {
        System.Console.WriteLine(@"Commands:
  init                              Reset and initialize game
  spawn <name> [x y]                Spawn a player
  tick [count]                      Advance simulation ticks (default: 1)
  move <player> <dx> <dy> [speed] [ticks]  Move player in direction
  teleport <player> <x> <y>        Set player position directly
  interact <player>                 Primary interact (harvest, buy, breed)
  alt-interact <player>             Alt interact (tame/release cow)
  status <player|cow> [index]       Show entity status
  list [players|cows|houses|grass|land]  List entities
  resources                         Show global resources
  set-resources <grass> <milk> <coins>  Set resources directly
  assert <target> <condition> [val] Assert game state (fails on mismatch)
    assert <player> state <key>       Check player state key
    assert <player> no-state          Check player state is disabled
    assert <player> following-cow     Check player has a following cow
    assert <player> not-following     Check player has no following cow
    assert <player> near <x> <y> [d]  Check player is near position
    assert cows <count>               Check cow count
    assert resources <field> <value>  Check resource value
  print <message>                   Print a message
  # comment                         Lines starting with # are ignored
  quit                              Exit");
    }

    private static void AssertEqual<T>(string label, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"ASSERT FAILED: {label} expected={expected} actual={actual}");
        System.Console.WriteLine($"ASSERT OK: {label} = {actual}");
    }

    private void PrintPlayerStatus(string name, Entity entity)
    {
        var pos = _game.State.GetComponent<Transform2D>(entity).Position;
        var sc = _game.State.GetComponent<StateComponent>(entity);
        var ps = _game.State.GetComponent<PlayerStateComponent>(entity);

        System.Console.WriteLine($"Player '{name}' (entity {entity.Id}):");
        System.Console.WriteLine($"  Position: ({(float)pos.X:F1}, {(float)pos.Y:F1})");
        System.Console.WriteLine($"  State: Key='{sc.Key}' Phase={sc.Phase} Time={sc.CurrentTime}/{sc.MaxTime} Enabled={sc.IsEnabled}");
        System.Console.WriteLine($"  FollowingCow: {(ps.FollowingCow != Entity.Null ? ps.FollowingCow.Id.ToString() : "none")}");
        System.Console.WriteLine($"  InteractionTarget: {(ps.InteractionTarget != Entity.Null ? ps.InteractionTarget.Id.ToString() : "none")}");
    }

    private void PrintCowStatus(Entity entity)
    {
        var pos = _game.State.GetComponent<Transform2D>(entity).Position;
        var cow = _game.State.GetComponent<CowComponent>(entity);

        System.Console.WriteLine($"Cow (entity {entity.Id}):");
        System.Console.WriteLine($"  Position: ({(float)pos.X:F1}, {(float)pos.Y:F1})");
        System.Console.WriteLine($"  Exhaust: {cow.Exhaust}/{cow.MaxExhaust}");
        System.Console.WriteLine($"  IsMilking: {cow.IsMilking}");
        System.Console.WriteLine($"  FollowingPlayer: {(cow.FollowingPlayer != Entity.Null ? cow.FollowingPlayer.Id.ToString() : "none")}");
        System.Console.WriteLine($"  HouseId: {(cow.HouseId != Entity.Null ? cow.HouseId.Id.ToString() : "none")}");
    }

    private void PrintEntityCounts()
    {
        int cows = 0, grass = 0, houses = 0, land = 0, walls = 0;
        foreach (var _ in _game.State.Filter<CowComponent>()) cows++;
        foreach (var _ in _game.State.Filter<GrassComponent>()) grass++;
        foreach (var _ in _game.State.Filter<HouseComponent>()) houses++;
        foreach (var _ in _game.State.Filter<LandComponent>()) land++;
        foreach (var _ in _game.State.Filter<WallComponent>()) walls++;
        System.Console.WriteLine($"Entities: {cows} cows, {grass} grass, {houses} houses, {land} land, {walls} walls");
    }

    private (Entity entity, Guid userId) GetPlayer(string name)
    {
        if (!_players.TryGetValue(name, out var player))
            throw new Exception($"Unknown player: '{name}'. Use 'spawn {name}' first.");
        return player;
    }

    private void DispatchAction<T>(Game game, T action, Entity target) where T : struct, IAction
    {
        game.State.AddComponent(target, action);
        game.Dispatcher.Update(game.State);
        game.Loop.Simulation.SystemRunner.Update(game.State);
    }
}
