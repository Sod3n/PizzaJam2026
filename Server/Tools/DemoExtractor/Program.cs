using System.Text.Json;
using System.Text.Json.Serialization;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using MlBot.Lib;
using Template.Shared.Components;
using Template.Shared.Factories;
using Template.Shared.Recording;
using Template.Shared.Tests;

namespace DemoExtractor;

internal static class Program
{
    // InteractAction's StableId, from Server/Template.Shared/Features/Player/Interaction/InteractAction.cs.
    private static readonly Guid InteractActionStableId = new("45d1256b-8cf7-5e53-cd19-1882c57de34f");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: DemoExtractor <recording.bin> [output.jsonl]");
            Console.Error.WriteLine("  If output is omitted, JSONL goes to stdout.");
            return 1;
        }

        string recordingPath = args[0];
        TextWriter outWriter = args.Length >= 2
            ? new StreamWriter(args[1])
            : Console.Out;

        try
        {
            Extract(recordingPath, outWriter);
        }
        finally
        {
            if (outWriter != Console.Out) outWriter.Dispose();
        }
        return 0;
    }

    private static void Extract(string recordingPath, TextWriter outWriter)
    {
        var (totalTicks, actions, _, _, _, _) = InputRecording.Load(recordingPath);
        Log($"loaded {actions.Count} actions across {totalTicks} ticks");

        int interactCount = 0;
        foreach (var a in actions) if (a.StableComponentId == InteractActionStableId) interactCount++;
        Log($"of which {interactCount} are InteractAction events");

        var game = TemplateGameFactory.CreateGame(tickRate: 60);

        // Bot brains cached per player entity (built lazily once we see the player).
        var brains = new Dictionary<int, MlBotBrain>();

        int actionIndex = 0;
        long maxTick = totalTicks;
        int emitted = 0;

        for (long tick = 0; tick <= maxTick; tick++)
        {
            // Process all actions scheduled at or before the next tick.
            while (actionIndex < actions.Count && actions[actionIndex].Tick <= game.Loop.CurrentTick + 1)
            {
                var a = actions[actionIndex];

                if (a.StableComponentId == InteractActionStableId)
                {
                    EmitDemo(game, a, brains, outWriter);
                    emitted++;
                }

                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                {
                    game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                }
                actionIndex++;
            }

            game.Loop.RunSingleTick();
        }

        // Process any remaining actions (shouldn't normally happen).
        while (actionIndex < actions.Count)
        {
            var a = actions[actionIndex];
            var stableId = new StableComponentId(a.StableComponentId);
            if (ComponentId.TryGetDense(stableId, out var denseId))
                game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
            actionIndex++;
        }

        outWriter.Flush();
        Log($"emitted {emitted} (obs, intent) records");
    }

    private static void EmitDemo(Game game, RecordedAction a,
        Dictionary<int, MlBotBrain> brains, TextWriter outWriter)
    {
        // Recover the player Entity from the action's target. The recording stores the
        // entity ID; we materialize it as Entity.Null+id by looking at the game state.
        Entity player = FindEntityById(game.State, a.TargetEntityId);
        if (player == Entity.Null || !game.State.HasComponent<PlayerEntity>(player))
            return;

        var pe = game.State.GetComponent<PlayerEntity>(player);

        // Build / cache a bot brain for this player. We never call PreTick — we only
        // use it as a read-only oracle for candidate enumeration.
        if (!brains.TryGetValue(player.Id, out var brain))
        {
            brain = new MlBotBrain(game, player, pe.UserId, learningEnabled: false);
            brains[player.Id] = brain;
        }

        var obs = ObservationBuilder.Build(game, player, (int)a.Tick);
        int? intent = InferIntent(game, player, brain);
        if (intent == null) return; // no classifiable target — skip this record

        var record = new DemoRecord
        {
            Tick = (int)a.Tick,
            PlayerId = player.Id,
            Intent = intent.Value,
            Obs = obs,
        };
        outWriter.WriteLine(JsonSerializer.Serialize(record, JsonOpts));
    }

    /// <summary>
    /// Classify the recorded InteractAction into one of 8 BotAction intents by finding
    /// the candidate target nearest to the player at this tick.
    /// </summary>
    private static int? InferIntent(Game game, Entity player, MlBotBrain brain)
    {
        if (!game.State.HasComponent<Transform2D>(player)) return null;
        var playerPos = game.State.GetComponent<Transform2D>(player).Position;

        int? bestAction = null;
        Float bestDistSq = (Float)999999f;
        foreach (var (action, target) in brain.EnumerateCandidates())
        {
            if (target == Entity.Null) continue;
            if (!game.State.HasComponent<Transform2D>(target)) continue;
            var d = game.State.GetComponent<Transform2D>(target).Position - playerPos;
            var distSq = d.SqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestAction = (int)action;
            }
        }
        return bestAction;
    }

    private static Entity FindEntityById(EntityWorld state, int id)
    {
        foreach (var e in state.Filter<PlayerEntity>())
        {
            if (e.Id == id) return e;
        }
        return Entity.Null;
    }

    private static void Log(string msg) => Console.Error.WriteLine($"[extract] {msg}");
}

internal sealed class DemoRecord
{
    public int Tick { get; set; }
    public int PlayerId { get; set; }
    public int Intent { get; set; }
    public WorldObservation Obs { get; set; } = new();
}
