# Game Design Document

*A 2D top-down farming game in the spirit of Don't Starve and Cult of the Lamb — but the cows are cowgirls, the helpers are maids, and the cats run the show.*

---

## 1. High Concept

You play a **farm owner** building a small dairy empire on a star-shaped plot of land. The cows you raise are **anthropomorphic cowgirls** (girls with cow horns, ears, tails, and bell collars). The helpers you hire are **maids** in frilled aprons with **oversized bows on their heads**. The pets that boost your operation are **various cats** — calico apprentices, black-cat foremen, fluffy upgrade specialists.

The art style is hand-drawn / painterly with the chunky silhouettes and high-contrast shadows of *Don't Starve*, but warmer in palette — the cozy, slightly cursed pastoral mood of *Cult of the Lamb*. Top-down, soft camera follow, rim lighting at dawn/dusk.

**Session goal:** breed cowgirls, milk them, sell milk, expand the farm, and eventually build the **Final Structure** at the southern tip of the map.

---

## 2. Tone & Visual Direction

| Element | Direction |
|---|---|
| **Cows** | Cowgirls — black-and-white, brown, or tier-rare colorings (mushroom-spotted, apple-blushed). Bell collar, cow ears + small horns, swishing tail. Idle animations: tail flick, hoof tap, bashful blush when "in love." |
| **Helpers** | Maids in classic black-and-white French maid outfits. Each has a **giant signature bow** in a unique color identifying their role (Ame the Assistant: pink; Lefantis the Gatherer: green; Mochi the Seller: gold; Brix the Builder: orange; Daisy the Milker: white). |
| **Pets** | Cats. The Assistant pets are tabby kittens that shadow the player. |
| **World** | Star-shaped pasture, five arms radiating from the central Sell Point. Each arm transitions biome as you expand outward: pasture → carrot fields → apple orchard → glowing mushroom cave. |

---

## 3. Core Loop

```
   ┌──── HARVEST food ────► FEED cow ────► MILK cow ────► SELL milk ─┐
   │                                                                  │
   └──── BUILD plots ◄──── EARN coins ◄────────────────────────────── ┘
                │
                └──► UNLOCK helpers / pets / farms ──► BREED stronger cowgirls
                                                            │
                                                            └──► WIN: Final Structure
```

A run starts with **50 coins, two starter cowgirls, and one Sell Point**. The player clicks to milk, walks to sell, then deposits earnings into adjacent land plots to unlock houses, farms, love houses, and eventually the Final Structure at grid distance 7 due south.

**Win condition:** build the Final Structure at grid (0, –7). Cost ramps to roughly 1680 coins by era multipliers, so the late game is about volume, not subsistence.

---

## 4. The World — Star Grid

The map is a five-pointed star, each arm 7 grid steps long (12.6 world units per step). Distance from center determines an **era multiplier** that scales every cost:

| Grid distance | Era multiplier |
|---|---|
| 1–2 | ×1 |
| 3 | ×2 |
| 4 | ×3 |
| 5 | ×4 |
| 6+ | ×6 |

Building cost = `gridDist × era × priceMultiplier × 10`. So a House at distance 1 is 10 coins; the Final Structure at distance 7 is 1680.

Key fixed positions:
- **(0, 0)** — Sell Point (the central well/altar where milk becomes coins).
- **(1, 0)** — Love House (the first breeding pen, gated behind early income).
- **(0, –7)** — Final Structure (the win goal).

Farms (Carrot, Apple, Mushroom) spawn on every 5th house slot at staggered angles, forcing the player to expand into multiple arms instead of tunneling one direction.

---

## 5. The Cowgirls

### Anatomy
Each cowgirl has:
- A **preferred food**: Grass (50% of cows), Carrot (28%), Apple (15%), Mushroom (7%).
- A **MaxExhaust** stat (≥10) — how much milk she can give before she needs to rest.
- A skin/coloring that's inherited at breeding and visually telegraphs her tier.

### Milking
The player taps to milk. Each tap consumes 1 unit of food and produces 1 unit of milk **if** the food matches her preference. Non-preferred food has a **50% chance to fail**. When MaxExhaust is reached, she's spent and needs reassignment or rest.

### Breeding
At a **Love House** (capacity 2 cowgirls), the player picks a pair and clicks to breed. Cost = sum of both cows' current exhaust.

| Pair type | Outcome |
|---|---|
| Same-preference | Always succeeds. 1% chance of twins. No tier upgrade. |
| Cross-tier (1 step) | 50% fail. Success = tier upgrade. |
| Cross-tier (2 steps) | 75% fail. |
| Cross-tier (3 steps) | 90% fail. |

A **failed breed** sends both cowgirls into **Depression** for 30 seconds — they sit slumped under the love house, non-interactable, then recover.

### Love Events
Every 2–5 breeds, a **love event** triggers: a random housed cowgirl gets infatuated with the highest-tier target on the farm. The next breed between them is a **guaranteed success and tier upgrade**. Storytelling-wise, this is the cowgirl's "anime confession" moment — a brief cutscene flourish before the love house shudders with hearts.

---

## 6. The Maids (Helpers)

Helpers are unlocked sequentially as your breed counter climbs:

| Unlock breed # | Maid | Bow color | Job | Capacity | Speed |
|---|---|---|---|---|---|
| 2 | **Lefantis** the Gatherer | Green | Harvests food from farms | 75 → 120 | 2 → 6 |
| 4 | **Brix** the Builder | Orange | Walks coins from player to land plots | 500 → 1000 | 2 → 6 |
| 6 | **Mochi** the Seller | Gold | Carries milk to the Sell Point | 500 → 1000 | 2 → 6 |
| 10 | **Daisy** the Milker | White | Autonomously milks housed cowgirls | 125 → 250 | 2 → 6 |

Maid AI states: `Idle → SeekingTarget → MovingToTarget → Working → Returning → Depositing`. They drop resources at the player's feet unless a **Warehouse** has been built, in which case they auto-deposit into central storage. This is a major late-game unlock — it removes the player as a logistics bottleneck.

**Maids are spawned from the Love House** when conditions are met (so breeding is also recruitment).

---

## 7. The Cats (Pets)

Pets are upgrades that ride alongside helpers — sleepy, judgmental, devastatingly effective.

- **Assistant cats** sit at the **HelperAssistant building** (grid dist 2, then 6). The first cat doubles your click speed (×2), the second multiplies it again (up to ×10 with the second-tier UpgradeAssistant building). Visually they perch on the player's shoulder or scamper alongside.
- **Upgrade cats** spawn at **UpgradeGatherer / UpgradeBuilder / UpgradeSeller / UpgradeMilker** buildings. Each one **doubles the capacity** and **triples the speed** of its target maid.

---

## 8. Food & Farms

| Tier | Food | Source | Rarity |
|---|---|---|---|
| 0 | Grass | Pasture (everywhere) | 50% cow preference |
| 1 | Carrot | Carrot Farm (dist 2, 3) | 28% |
| 2 | Apple | Apple Orchard (dist 4, 5) | 15% |
| 3 | Mushroom | Mushroom Cave (dist 6, 7) | 7% |

Food is harvested by clicking the plant, or by Lefantis the Gatherer once she's hired. Farms regrow durability over time. The deeper-tier foods are required to milk rare-preference cowgirls efficiently — so unlocking the mushroom cave is a real mid-game goal, not just decoration.

Houses display a **food sign** indicating which food the assigned cowgirl will eat. Mismatched signs mean failed milkings, so this is a small puzzle layer.

---

## 9. Buildings

| Building | Cost mult | Purpose |
|---|---|---|
| House | ×1 | Holds 1 cowgirl. Food sign chooses what she eats. |
| Love House | ×2 | Holds 2. Click to breed. Spawns helpers when conditions trigger. |
| Sell Point | – | Fixed at center. Milk → coins, 1:1. |
| Carrot Farm / Apple Orchard / Mushroom Cave | ×1 | Tiered food sources. |
| HelperAssistant / UpgradeAssistant | ×1 | Spawns assistant cats (click multipliers). |
| UpgradeGatherer / Builder / Seller / Milker | ×1 | Spawns upgrade cats for that maid. |
| Warehouse | ×2 | Removes the player from the logistics chain. |
| Decoration | ×0.25 | Cosmetic. Pure flex. |
| **Final Structure** | ×4 | Win condition. Distance 7 south. |

---

## 10. Strategic Depth — What's the Player Actually Choosing?

1. **Who do I breed with whom?** Safe same-tier breeds are reliable income; cross-tier gambles risk 30s of dead time but give tier-upgrades that compound forever.
2. **Which arm of the star do I expand into first?** Farms force multi-arm growth; expanding only south reaches the Final Structure faster but starves you of food variety.
3. **Helpers vs. self-reliance.** A no-helper run takes 30+ minutes of clicking. A selective-helper run (Gatherer + assistant cats early, then Milker late) closes in ~20.
4. **When do I take the Warehouse plunge?** Costs 2× a normal building, but transforms maids from "drop at player's feet" to "fully autonomous loop."
5. **Click economy.** Stacking Assistant cats up to ×10 click speed makes the player themself the most powerful milker in the game — but only if you spend the building budget on it.

The metrics system tracks cumulative food/milk/coins, peaks, and "zero-tick" bottlenecks (how often a resource ran dry), and feeds an ML bot trainer for balance tuning.

---

## 11. Game Feel

- **Cowgirls in the follow chain** drift behind the player in a soft serpentine — same swarm-follow algorithm as classic Pikmin / Cult of the Lamb followers, with a 1.8-unit separation push so they don't clip.
- **Maids have navmesh avoidance** so they look purposeful, not glued to the player.
- **Cats just teleport when off-screen.** They're cats.
- Ticks are 60 TPS, deterministic (server/client lockstep). Inputs are buffered — the click feel should be tight, with a satisfying "clink" + tiny milk-bottle animation per successful click.

---

## 12. Win State & Replay

When the Final Structure is built at (0, –7), the run ends. Suggested win flourish: the structure unfolds into a small chapel/shrine, every cowgirl on the farm trots to it for a group bow, all maids curtsy in unison, every cat falls asleep. Run-time, breed count, and peak coin readout shown on a ribbon.

Replay value comes from:
- Speedrun targets (20 / 25 / 30 minute tiers).
- Skin / cat collection completion.
- Self-imposed challenges (no helpers, no cross-breeding, single-arm run).

---

## 13. Reference Constants (Engineering)

For balancing reference, the current shipped values (subject to tuning):

- Start: 50 coins, 2 grass cows, 1 Sell Point.
- Milk → coin: 1:1.
- Cow depression: 1800 ticks (30 s).
- Love event interval: 2–5 breeds; deferred 0–180 s.
- Twin chance: 1%.
- Cross-tier breed fail: 50% / 75% / 90%.
- Helper unlocks: breed #2, #4, #6, #10.
- Capacity upgrades: ×2. Speed upgrades: ×3.
- Click multipliers: ×2 (Assistant), ×5 (UpgradeAssistant) — stack up to ~×10.
- Grid step: 12.6 units. Final structure: dist 7.

---

*"All cows are girls. All helpers wear bows. All cats are in charge. Build the shrine before the milk runs out."*
