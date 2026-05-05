namespace Template.Shared.Components;

public static class StateComponentExtensions
{
    public static void ResetState(ref this StateComponent sc)
    {
        sc.Key = "";
        sc.CurrentTime = 0;
        sc.MaxTime = 0;
        sc.IsEnabled = false;
    }
}
