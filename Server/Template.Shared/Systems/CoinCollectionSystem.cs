using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class CoinCollectionSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Iterate coins in deterministic order (Filter is already deterministic by entity ID)
        foreach (var coinEntity in state.Filter<CoinComponent, Area2D>())
        {
            ref var area = ref state.GetComponent<Area2D>(coinEntity);

            if (!area.HasOverlappingBodies) continue;

            for (int i = 0; i < area.OverlappingEntities.Count; i++)
            {
                int targetId = area.OverlappingEntities[i];
                var targetEntity = new Entity(targetId);

                // Check if the overlapping entity is a player
                if (state.HasComponent<PlayerEntity>(targetEntity))
                {
                    // Get Component data
                    ref var coin = ref state.GetComponent<CoinComponent>(coinEntity);
                    ref var score = ref state.GetComponent<ScoreComponent>(targetEntity);

                    // Apply Logic
                    score.Value += coin.Value;

                    // Destroy coin
                    state.DeleteEntity(coinEntity);

                    // Stop processing this coin since it's destroyed
                    break;
                }
            }
        }
    }
}



 # love_house.gd
  extends StaticBody2D

  var cow1: Cow = null
  var cow2: Cow = null

  func try_breed() -> Cow:
      if not cow1 or not cow2:
          return null

      var new_cow = preload("res://scenes/cow.tscn").instantiate() # it is ecs entity

      # same tier: 25% upgrade, 1% downgrade
      if cow1.tier == cow2.tier:
          var roll = randf()
          if roll < 0.25:
              new_cow.tier = cow1.tier + 1
          elif roll < 0.26:
              new_cow.tier = max(0, cow1.tier - 1)
          else:
              new_cow.tier = cow1.tier
      else:
          # different tier: fail chance based on gap
          var gap = abs(cow1.tier - cow2.tier)
          if randf() < gap * 0.2:
              new_cow.tier = min(cow1.tier, cow2.tier)
          else:
              new_cow.tier = max(cow1.tier, cow2.tier)

      get_parent().add_child(new_cow)
      new_cow.global_position = global_position
      return new_cow
