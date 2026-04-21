using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Network.Client;
using Deterministic.GameFramework.Types;
using Template.Shared.Factories;
using Template.Shared.Features.Movement;

namespace StressBot;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var opts = Options.Parse(args);
        if (opts == null) return 2;

        var bot = new Bot(opts);
        return await bot.RunAsync();
    }
}

public sealed class Options
{
    public string ServerIp = "127.0.0.1";
    public int ServerPort = 9050;
    public string Mode = "create"; // create | join
    public Guid LobbyId;
    public int DurationSeconds = 60;
    public string BotId = "bot";
    public string? StatsCsv;
    public int LatencyMs = 0;
    public int ExpectedPlayers = 2;
    public int LobbyWaitSeconds = 5;

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? n = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--server-ip": o.ServerIp = n!; i++; break;
                case "--server-port": o.ServerPort = int.Parse(n!); i++; break;
                case "--mode": o.Mode = n!; i++; break;
                case "--lobby-id": o.LobbyId = Guid.Parse(n!); i++; break;
                case "--duration-seconds": o.DurationSeconds = int.Parse(n!); i++; break;
                case "--bot-id": o.BotId = n!; i++; break;
                case "--stats-csv": o.StatsCsv = n; i++; break;
                case "--latency-ms": o.LatencyMs = int.Parse(n!); i++; break;
                case "--expected-players": o.ExpectedPlayers = int.Parse(n!); i++; break;
                case "--lobby-wait-seconds": o.LobbyWaitSeconds = int.Parse(n!); i++; break;
                case "--help":
                case "-h":
                    Console.Error.WriteLine("StressBot --server-ip IP --server-port P --mode create|join [--lobby-id GUID] --duration-seconds N --bot-id ID [--stats-csv PATH] [--latency-ms N] [--expected-players M] [--lobby-wait-seconds N]");
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown arg: {a}");
                    return null;
            }
        }
        if (o.Mode != "create" && o.Mode != "join")
        {
            Console.Error.WriteLine("--mode must be 'create' or 'join'");
            return null;
        }
        if (o.Mode == "join" && o.LobbyId == Guid.Empty)
        {
            Console.Error.WriteLine("--lobby-id required when --mode=join");
            return null;
        }
        return o;
    }
}

public sealed class Bot
{
    private readonly Options _opt;
    private GameClient _client = null!;
    private Game _game = null!;

    private long _actionsSent;
    private long _deltasReceived;
    private long _tickCount;
    private double _tickDurationSumMs;
    private long _tickDurationSamples;
    private readonly Stopwatch _tickStopwatch = new();
    private volatile bool _connected;
    private volatile bool _unexpectedDisconnect;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<Guid> _lobbyCreatedTcs = new();

    public Bot(Options opt)
    {
        _opt = opt;
    }

    public async Task<int> RunAsync()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _shutdown.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _shutdown.Cancel();

        Log($"Starting. mode={_opt.Mode} server={_opt.ServerIp}:{_opt.ServerPort} duration={_opt.DurationSeconds}s");

        _game = TemplateGameFactory.CreateGame(tickRate: 60);

        var net = new LiteNetLibNetworkClient();
        net.OnTickDeltaReceived += _ => Interlocked.Increment(ref _deltasReceived);
        _client = new GameClient(net, $"{_opt.ServerIp}:{_opt.ServerPort}", _game, SyncMode.DeltaSync);
        _client.OnLog += m => Log(m);
        _client.OnConnected += () => { _connected = true; Log("Connected."); };
        _client.OnDisconnected += () => { _connected = false; if (!_shutdown.IsCancellationRequested) _unexpectedDisconnect = true; Log("Disconnected."); };
        _client.OnLobbyCreated += id => { Log($"LobbyCreated {id}"); _lobbyCreatedTcs.TrySetResult(id); };
        _client.OnLobbyJoined += id => Log($"LobbyJoined {id}");
        _client.OnMatchAssigned += id => Log($"MatchAssigned {id}");

        if (_opt.LatencyMs > 0) _client.SimulatedLatencyMs = _opt.LatencyMs;

        try
        {
            await _client.ConnectAsync();
        }
        catch (Exception ex)
        {
            Log($"Connect failed: {ex.Message}");
            return 10;
        }

        Guid lobbyId;
        if (_opt.Mode == "create")
        {
            await _client.CreateLobbyAsync($"stress-{_opt.BotId}");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                lobbyId = await _lobbyCreatedTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log("Timed out waiting for LobbyCreated.");
                return 11;
            }
            Console.Out.WriteLine($"LOBBY_ID={lobbyId}");
            Console.Out.Flush();

            Log($"Waiting {_opt.LobbyWaitSeconds}s for joiners before starting match...");
            try { await Task.Delay(TimeSpan.FromSeconds(_opt.LobbyWaitSeconds), _shutdown.Token); }
            catch (OperationCanceledException) { return 0; }

            await _client.StartLobbyMatchAsync(lobbyId);
        }
        else
        {
            lobbyId = _opt.LobbyId;
            await _client.JoinLobbyAsync(lobbyId);
        }

        Log("Waiting for sync...");
        var syncCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await Task.Run(() => _client.WaitForSyncAsync()).WaitAsync(syncCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Timed out waiting for sync.");
            return 12;
        }
        Log("Synced.");

        _game.Loop.OnBeforeTick += () =>
        {
            _tickStopwatch.Restart();
        };
        _game.Loop.OnTick += () =>
        {
            _tickStopwatch.Stop();
            _tickDurationSumMs += _tickStopwatch.Elapsed.TotalMilliseconds;
            _tickDurationSamples++;
            Interlocked.Increment(ref _tickCount);
            ScriptedBehavior();
        };

        var loopTask = _game.Loop.Start();

        int seed = StableSeed(_opt.BotId);
        _rng = new Random(seed);

        var statsTask = StatsLoop();
        var endAt = DateTime.UtcNow.AddSeconds(_opt.DurationSeconds);
        try
        {
            while (DateTime.UtcNow < endAt && !_shutdown.IsCancellationRequested && !_unexpectedDisconnect)
            {
                await Task.Delay(200, _shutdown.Token);
            }
        }
        catch (OperationCanceledException) { }

        Log("Shutting down...");
        _game.Loop.Stop();
        try { await loopTask.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        _shutdown.Cancel();
        try { await statsTask; } catch { }

        FlushStatsOnce(final: true);
        _client.Dispose();

        if (_unexpectedDisconnect) return 20;
        return 0;
    }

    private Random _rng = new();
    private int _playerTargetEntityId = 0;

    private void ScriptedBehavior()
    {
        long t = _game.Loop.CurrentTick;
        if (t < 120) return;

        if (_playerTargetEntityId == 0)
        {
            foreach (var e in _game.State.Filter<Template.Shared.Components.PlayerEntity>())
            {
                ref var p = ref _game.State.GetComponent<Template.Shared.Components.PlayerEntity>(e);
                if (p.UserId.ToString() == _client.PlayerId.ToString())
                {
                    _playerTargetEntityId = e.Id;
                    Log($"Discovered local player entity {_playerTargetEntityId}");
                    break;
                }
            }
            if (_playerTargetEntityId == 0) return;
        }

        if (t % 300 == 0)
        {
            var stop = new SetMoveDirectionAction(Vector2.Zero, (Float)0);
            _client.Execute(stop, _playerTargetEntityId);
            Interlocked.Increment(ref _actionsSent);
            return;
        }

        if (t % 60 == 0)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            var dir = new Vector2((Float)(float)Math.Cos(ang), (Float)(float)Math.Sin(ang));
            var act = new SetMoveDirectionAction(dir, (Float)1);
            _client.Execute(act, _playerTargetEntityId);
            Interlocked.Increment(ref _actionsSent);
        }
    }

    private static int StableSeed(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }
    }

    private async Task StatsLoop()
    {
        // Write CSV header once at start
        if (_opt.StatsCsv != null)
        {
            var dir = Path.GetDirectoryName(_opt.StatsCsv);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(_opt.StatsCsv))
            {
                await File.WriteAllTextAsync(_opt.StatsCsv,
                    "timestamp,bot_id,connected,actions_sent,deltas_received,tick_count,tick_ms_avg,current_tick,last_server_tick,drift\n");
            }
        }

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await Task.Delay(5000, _shutdown.Token);
                FlushStatsOnce(final: false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void FlushStatsOnce(bool final)
    {
        long actions = Interlocked.Read(ref _actionsSent);
        long deltas = Interlocked.Read(ref _deltasReceived);
        long ticks = Interlocked.Read(ref _tickCount);
        double avgMs = _tickDurationSamples > 0 ? _tickDurationSumMs / _tickDurationSamples : 0.0;
        long curTick = _game?.Loop.CurrentTick ?? 0;
        long serverTick = _client?.DeltaStrategy?.LastAppliedServerTick ?? -1;
        long drift = serverTick >= 0 ? curTick - serverTick : 0;

        string line = string.Format(CultureInfo.InvariantCulture,
            "{0:O},{1},{2},{3},{4},{5},{6:F3},{7},{8},{9}",
            DateTime.UtcNow, _opt.BotId, _connected, actions, deltas, ticks, avgMs, curTick, serverTick, drift);

        Console.Out.WriteLine($"STATS {line}");
        Console.Out.Flush();

        if (_opt.StatsCsv != null)
        {
            try { File.AppendAllText(_opt.StatsCsv, line + "\n"); } catch { }
        }
    }

    private void Log(string msg)
    {
        Console.Error.WriteLine($"[bot-{_opt.BotId}] {msg}");
    }
}
