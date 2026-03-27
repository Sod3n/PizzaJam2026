namespace Template.Shared;

public static class StateKeys
{
    public const string Idle = "idle";
    public const string Interacted = "interacted";
    public const string NotEnoughResource = "not_enough_resource";
    public const string Milking = "milking";
    public const string Taming = "taming";
    public const string Release = "release";
    public const string Assign = "assign";
    public const string Breed = "breed";

    // Resource keys (used as Param in EnterStateComponent)
    public const string Grass = "grass";
    public const string Milk = "milk";
    public const string Coins = "coins";
    public const string Houses = "houses";
}
