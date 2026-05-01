using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.GGPO;
using Deterministic.GameFramework.Serialization;

namespace Template.Shared.Tests;

internal static class GGPOTestHelper
{
    internal static StateHistory SetupGGPO(this Game game)
    {
        return GGPOSetup.EnableGGPO(game.Loop.Simulation);
    }

    internal static StateHistory GetHistory(this GameSimulation sim)
    {
        return GGPOSetup.GetHistory(sim)!;
    }
}
