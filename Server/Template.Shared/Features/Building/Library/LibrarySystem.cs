using Deterministic.GameFramework.ECS;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class LibrarySystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var libraryEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.HasComponent<LibraryComponent>(libraryEntity)) continue;

            InteractFeedback.Success(state.Ctx(playerEntity), playerEntity, libraryEntity);
        }
    }
}
