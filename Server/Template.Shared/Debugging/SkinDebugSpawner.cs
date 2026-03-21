using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;
using FixedMathSharp;
using System.Linq;

namespace Template.Shared.Debugging;

public static class SkinDebugSpawner
{
    public static void SpawnAllSkinsInLine(Context ctx, Vector2 startPosition, Float spacing)
    {
        var allSkins = GD.SkinsData.GetAll();
        // Collect all unique types (e.g., "Hair", "Bottom1", "Eyes", etc.)
        var allTypes = allSkins.Values.Select(s => s.Type).Distinct().ToList();
        
        int index = 0;

        foreach (var kvp in allSkins)
        {
            var skinId = kvp.Key;
            var skin = kvp.Value;

            // Calculate position
            var position = startPosition + new Vector2(spacing * index, 0);
            
            // Create Player
            // We use a dummy UserID for these debug entities
            var entity = PlayerDefinition.Create(ctx, System.Guid.NewGuid(), position, 0);

            // Override SkinComponent to show ONLY this specific skin
            var skinComponent = new SkinComponent
            {
                Skins = new Dictionary16<FixedString32, int>()
            };
            
            // Populate all slots: Active skin gets its ID, others get -1 (Hidden)
            foreach (var type in allTypes)
            {
                if (type == skin.Type)
                {
                    skinComponent.Skins.Add(new FixedString32(type), skinId);
                }
                else
                {
                    skinComponent.Skins.Add(new FixedString32(type), -1);
                }
            }
            
            // Update the component on the entity
            ctx.AddComponent(entity, skinComponent);
            
            // Optional: Set a name to identify it
            if (ctx.State.HasComponent<PlayerEntity>(entity))
            {
                ref var player = ref ctx.GetComponent<PlayerEntity>(entity);
                player.Name = new FixedString32($"Skin_{skinId}");
            }

            index++;
        }
    }
}
