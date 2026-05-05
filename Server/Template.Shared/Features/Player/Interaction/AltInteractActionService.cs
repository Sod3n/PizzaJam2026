using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Components;

namespace Template.Shared.Actions;

public class AltInteractActionService : ActionService<AltInteractAction, PlayerEntity>
{
    protected override void ExecuteProcess(AltInteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        // Alt interact removed - cow taming is now handled via regular interact
    }
}
