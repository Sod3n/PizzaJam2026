using Deterministic.GameFramework.Network.Server;
using Deterministic.GameFramework.ServerV2;
using Template.Server;

new Deterministic.GameFramework.Utils.Logging.ConsoleLogger();
// Respect LOG_LEVEL env var (Debug/Info/Warning/Error/None). Default Info. On VPS set
// `LOG_LEVEL=None` (or `Warning`) to silence per-tick logs — stdout's SyncTextWriter lock
// dominated ~33 % of per-tick CPU in profiling at 15 lobbies.
Deterministic.GameFramework.Utils.Logging.ILogger.ReadLevelFromEnvironment();
Console.WriteLine($"[Startup] LogLevel = {Deterministic.GameFramework.Utils.Logging.ILogger.MinLevel}");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();

// 1. Framework Services
builder.Services.AddSingleton<IAuthService, BasicAuthService>();
builder.Services.AddSingleton<TemplateMatchFactory>();
builder.Services.AddSingleton<IMatchFactory>(sp => sp.GetRequiredService<TemplateMatchFactory>());
builder.Services.AddDeterministicGameServer(options =>
{
    options.MinPlayersPerMatch = 2;
    options.MaxPlayersPerMatch = 2;
    options.SyncMode = Deterministic.GameFramework.Common.SyncMode.DeltaSync;
}); // This adds MatchManager, MatchBroadcaster, IMatchmakingService

// 2. Network Layer
// builder.Services.AddSingleton<SignalRNetworkServer>();
builder.Services.AddSingleton<LiteNetLibPacketRouter>();

builder.Services.AddSingleton<INetworkServer>(sp => 
{
    var router = sp.GetRequiredService<LiteNetLibPacketRouter>();
    return new LiteNetLibNetworkServer(
        9050, 
        router.OnPacketReceived,
        router.OnPeerConnected,
        router.OnPeerDisconnected
    );
});

builder.Services.AddSingleton<GamePacketProcessor>();
// MatchBroadcaster is already added by AddDeterministicGameServer()
// matchBroadcaster is already added by AddDeterministicGameServer()

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();
app.MapHub<GameHub>("/gamehub");

// Start Broadcaster (it hooks into MatchManager)
Console.WriteLine("Resolving MatchBroadcaster...");
app.Services.UseDeterministicGameServer(); // This forces instantiation of MatchBroadcaster
Console.WriteLine("MatchBroadcaster resolved.");

// Ensure Network Server is started
var networkServer = app.Services.GetRequiredService<INetworkServer>();
Console.WriteLine($"NetworkServer resolved: {networkServer.GetType().Name}");

var matchManager = app.Services.GetRequiredService<MatchManager>();

// // Auto-create a default match for testing
// var defaultMatchId = System.Guid.Parse("00000000-0000-0000-0000-000000000001");
// Console.WriteLine($"Creating default match: {defaultMatchId}");
// matchManager.CreateMatch(defaultMatchId);

app.Run();


// Player: {Health: 100; "Position": {X: 0, Y: 0}}