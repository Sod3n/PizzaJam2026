namespace Template.Shared;

public static class StateKeys
{
    public const string Idle = "idle";
    public const string Interacted = "interacted";
    public const string NotEnoughResource = "not_enough_resource";
    public const string MilkingEnter = "milking_enter";
    public const string Milking = "milking";
    public const string MilkingExit = "milking_exit";

    // Resource keys (used as Param in EnterStateComponent)
    public const string Grass = "grass";
    public const string Milk = "milk";
    public const string Coins = "coins";
}
