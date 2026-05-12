using System.Text.Json;
using System.Text.Json.Serialization;
using MlBot.Lib;

namespace MlBotHost;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Main(string[] args)
    {
        var session = new SimSession();
        Log($"MlBotHost ready. balance_hash={Template.Shared.GameData.Balance.JsonHash}");

        string? line;
        while ((line = Console.In.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;

            Response response;
            try
            {
                var request = JsonSerializer.Deserialize<Request>(line, JsonOpts)
                              ?? throw new InvalidOperationException("empty request");
                response = Dispatch(session, request);
            }
            catch (Exception ex)
            {
                response = new Response { Ok = false, Error = ex.Message };
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOpts));
            Console.Out.Flush();

            if (response.AutoTerminate) break;
        }

        return 0;
    }

    private static Response Dispatch(SimSession session, Request request) => request.Cmd switch
    {
        "reset" => session.Reset(request.Seed ?? 0, request.MaxTicks ?? 108_000),
        "step" => session.Step(
            request.Action ?? throw new InvalidOperationException("step requires 'action'"),
            request.NumTicks ?? 30),
        "ping" => new Response { Ok = true },
        "quit" => new Response { Ok = true, AutoTerminate = true },
        _ => throw new InvalidOperationException($"unknown cmd: {request.Cmd}"),
    };

    internal static void Log(string msg) => Console.Error.WriteLine($"[host] {msg}");
}

internal sealed class Request
{
    public string Cmd { get; set; } = "";
    public int? Seed { get; set; }
    public int? Action { get; set; }
    public int? NumTicks { get; set; }
    public int? MaxTicks { get; set; }
}

internal sealed class Response
{
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
    public WorldObservation? Obs { get; set; }
    public Deltas? Deltas { get; set; }
    public bool Done { get; set; }
    public int[]? ValidActions { get; set; }
    public int Tick { get; set; }
    public bool Accepted { get; set; }
    public string? BalanceHash { get; set; }
    [JsonIgnore] public bool AutoTerminate { get; set; }
}
