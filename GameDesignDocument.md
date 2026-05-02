# Game Design Document

*A 2D top-down farming game in the spirit of Don't Starve and Cult of the Lamb — but the cows are cowgirls, the helpers are maids, and the cats run the show.*

---

## 1. High Concept

You play a **farm owner** building a small dairy empire on a star-shaped plot of land. The cows you raise are **anthropomorphic cowgirls** (girls with cow horns, ears, tails, and bell collars). The helpers you hire are **maids** in frilled aprons with **oversized bows on their heads**. The pets that boost your operation are **various cats** — picked up and placed wherever you want them, on cows, on maids, on yourself.

The art style is hand-drawn / painterly with the chunky silhouettes and high-contrast shadows of *Don't Starve*, but warmer in palette — the cozy, slightly cursed pastoral mood of *Cult of the Lamb*. Top-down, soft camera follow, rim lighting at dawn/dusk.

**Session goal:** breed cowgirls, milk them, sell milk and occasionally cows, expand the farm, and eventually build the **Final Structure** at the southern tip of the map.

**Tagline:** *"All cows are girls. All helpers wear bows. All cats are in charge. Build the shrine before the milk runs out."*

---

## 2. Design Philosophy

These rules define what the game is. Every system respects them.

- **Input vocabulary: movement + one contextual button.** No menus, no mouse, no second button.
- **Encounter teaching:** every system is taught by encountering it in the world, never by tutorial or up-front information.
- **Strategic variance from world state:** variance comes from the world changing, not from the player choosing between simultaneous menu options.
- **Cycle-tap on the contextual button is the only allowed form of "selection."**
- **One physical target per semantic action.** Two actions need two targets.
- **Differentiated objects, not duplicated signs.** When a system needs multiple physical targets, those targets must be visually and categorically distinct, not variants of the same object.
- **Bonuses bind to roles, not individuals.** When workers can rotate jobs, upgrades stay anchored to the function being performed.
- **Pacing comes from cooldowns and resets, not clocks.** The player chooses when to advance; the systems enforce minimums, never maximums.
- **Strategic depth comes from gentle scarcity throughout, never from forced choice.** The player should always be able to ignore optimization and still enjoy the farm.
- **Wait states have an active alternative.** If the player is forced to wait, give them an action that meaningfully reduces the wait — *unless the wait is the design point* (sleep/cooldown loops gated to a single shared rhythm are intentional).
- **Multiplayer is asymmetric.** Strategic decisions are owned by Player 1; tactical execution is shared.

> All numeric values mentioned in this document are *illustrative* — the canonical source is `Server/Template.Shared/GameData/Balance.cs`. When the doc and the code disagree, the code wins.

---

## 3. Tone & Visual Direction

| Element | Direction |
|---|---|
| **Cows** | Cowgirls — black-and-white, brown, or tier-rare colorings (mushroom-spotted, apple-blushed). Bell collar, cow ears + small horns, swishing tail. Idle animations: tail flick, hoof tap, bashful blush when "in love." |
| **Helpers** | Maids in classic black-and-white French maid outfits. Each has a giant signature bow identifying her **current** role (green = Gatherer, orange = Builder, gold = Seller, white = Milker). Maids switch bow color when they switch roles. |
| **Pets** | Cats. Sleepy, judgmental, devastatingly effective. Sit at the pet sanctuary until picked up. Players carry them on the shoulder and place them on cows, maids, or themselves. |
| **World** | Star-shaped pasture, five arms radiating from the central Player's House. Each arm transitions biome as you expand outward: pasture → carrot fields → apple orchard → glowing mushroom cave. |

Anime-style emotion symbols above characters' heads communicate state — anger vein for upset, sweat drop for stressed, heart for in-love, music note for content, sleep Z for exhausted, sparkle for milestones. One symbol at a time per cow, highest-priority emotion shown.

---

## 4. Core Loop

```
   ┌── HARVEST food ──► FEED cow ──► MILK cow ──► SELL ──┐
   │                                                      │
   └── BUILD plots ◄── EARN coins ◄────────────────────── ┘
            │
            └──► UNLOCK helpers / cats ──► BREED cowgirls
                                                │
                                                └──► WIN: Final Structure
```

A run starts with a small coin reserve and two grass-locked starter cowgirls in the pasture near the central Player's House. The player taps to milk, walks to the Sell Point, deposits earnings into adjacent land plots to unlock houses, farms, love houses, and eventually the Final Structure at grid distance 7 due south.

Days advance when the player **sleeps** at their house. Each sleep refreshes cow exhaust, food caps, love-house cooldowns, and the Sell Point's accepted-product mode.

**Win condition:** build the Final Structure at grid `(0, –7)`. Cost ramps via era multipliers, so the late game is about volume, not subsistence.

---

## 5. The World — Star Grid

The map is a five-pointed star. Distance from center determines an **era multiplier** that scales every cost — a House near center is cheap; the Final Structure at the southern tip is the most expensive thing in the game by an order of magnitude.

**Building progression by ring.** The player builds outward in concentric "circles" (rings) around the center. Each ring becomes available once the *entire previous ring* is built. New buildings can only be placed within the currently-active ring. Ring boundaries are visually distinguished by ground color/banner tint.

**Key fixed positions:**
- **(0, 0) — Player's House.** The central anchor; spawn point; sleep target; cat-bonding target.
- **Sell Point** — adjacent to the Player's House; accepts milk most days, switches to cow-buyer mode on a cycle.
- **(0, –7) — Final Structure.** The win goal.

Farms (Carrot, Apple, Mushroom) unlock at specific rings, forcing the player to expand outward through the era-cost curve to access higher tiers.

---

## 6. The Cowgirls

### Anatomy

Each cowgirl has:
- A **food preference** — primary food (one of Grass / Carrot / Apple / Mushroom). A minority of cowgirls also have a **secondary** food they're equally happy with.
- A **MaxExhaust** stat — how much milk she can give before she's spent.
- A skin/coloring that's inherited at breeding and visually telegraphs her tier.

Food preference is bound to the cow herself, not to a house. When she moves house, her preference travels with her. The two starter cows are forced to a known preference (grass) so the opening is predictable.

### Discovering preferences

A new cowgirl's preferences are **hidden** until the player feeds her. The food sign next to her house shows a **question mark** for any food the player hasn't yet tested with her. To discover whether she likes a particular food, the player must feed her enough of it to fill one full exhaust cycle (`MaxExhaust` units of that food). After that, the sign shows a **heart** if it's a preference or a **broken heart** if it isn't.

This means picking which food to feed a new cow is itself a small puzzle: spend resources testing, or stick to the food you already know works.

### Milking

The player taps to milk. Milking is a **cycle**: it takes a fixed number of clicks per unit of milk (the cow drains exhaust on every click but only produces milk every Nth click). Holding the button auto-fires at a slightly slower cadence than a fast tap — comfortable for long sessions, but rapid-tapping is still slightly more productive.

If the food is one of the cow's preferences, every produced milk lands. If it isn't, each *produced milk* has a chance to fail (the food is still consumed). When `MaxExhaust` is reached the cow is spent. Mid-cycle clicks are honored even past `MaxExhaust` — she always gets to finish whatever milk she was working on.

Cow exhaust does **not** regenerate passively. It only resets when the player sleeps.

### Breeding

At a **Love House** (capacity 2 cowgirls), the player picks a pair and clicks to breed. The Love House has a **cooldown** between breeds that **only resets when the player sleeps** — no click-to-skip. Breeding cost scales with the parents' average `MaxExhaust`.

**Offspring food preference** is rolled per breed:
- Per-parent **inherit chance** for each parent (each rolled independently). With both parents same-pref, the result is more likely to share that pref; with mixed parents, you get a coin-flip plus randomness.
- Otherwise, a **uniform random** food across all four types.
- Some offspring also gain a **secondary** food preference (rolled at a small chance).

There is no "tier ladder" — preferences are not ordered, and a cross-tier breed is just as likely to produce a low-tier as a high-tier offspring. Tier diversity comes from population size and breed volume, not from a guaranteed climb.

**No empty house is required to breed.** Offspring without an available house follow the player or wait near the love house until the player assigns them.

### Twins and love events

- **Twins** — small chance per breed to spawn two offspring instead of one.
- **Love events** — a "guaranteed positive variance" event between two random housed cowgirls. The next breed between the two cows is the love-confession breed. (Love events can be globally disabled in the code as a tuning lever.)

### Failed-breed depression *(currently disabled)*

The codebase scaffolds a "depression on failed cross-tier breed" mechanic, but it's switched off by default. When enabled, cross-tier breeds can fail (with cost penalties) and put both parents into a temporary timeout. Treat this as a feature toggle, not the current play experience.

---

## 7. The Maids (Helpers)

Maids are unlocked sequentially as your breed counter climbs (one slot at a small breed count, the next a few breeds later, etc.).

**Maids are generic at hire-time** — they don't have fixed roles. Each maid is assigned to a cowgirl-style **House** (the same building cows live in), with a **role sign** attached. Cycling the role sign instantly changes the maid's job:

- **Gatherer** (green bow) — harvests food from farms.
- **Builder** (orange bow) — carries coins from the player to land plots.
- **Seller** (gold bow) — carries milk to the Sell Point.
- **Milker** (white bow) — autonomously milks housed cowgirls.

Mid-trip tasks finish before the role-switch takes effect. The bow color updates so the player can read the whole maid workforce at a glance.

Maid AI states: `Idle → SeekingTarget → MovingToTarget → Working → Returning → Depositing`. They drop resources at the player's feet unless a **Warehouse** has been built, in which case they auto-deposit into central storage.

Maids carry only resources relevant to their current role. A Gatherer carries food; a Milker carries milk; a Builder carries coins. They cannot transfer resources outside their role's normal flow.

---

## 8. The Cats (Pets)

Cats live at the **Pet Sanctuary** building (one unified pet building, not five role-specific ones). The player walks to a cat, presses the contextual button → cat hops onto the player's shoulder.

### Carrying and placing

While carrying a cat, the contextual button **places** the cat instead of performing the target's normal action. Valid drop targets (each shows a faint glow when player is adjacent and carrying a cat):

- **A cowgirl** — cat boosts that cow's milking yield.
- **A maid** — cat boosts whatever role she's currently performing.
- **The player** — cat permanently bonds and rides on the player's shoulder, boosting whatever the player is doing.
- **Back at the Sanctuary** — un-assigns the cat.

Pickup is reversible: walk to a placed cat, press button, it hops back on the shoulder.

### Stacking and the strategic spectrum

Multiple cats stack on the same target, **additive** (each cat adds the same amount of boost; they don't multiply). This creates the central strategic axis of the game:

- **Hermit build** — stack all cats on the player. Player becomes a hyper-efficient one-person workforce. Active, click-heavy late game.
- **Empress build** — distribute cats across cowgirls and maids. Player oversees a boosted workforce. Passive, managerial late game.
- **Hybrid** — any combination in between.

Cat distribution is the player's primary expression of late-game playstyle. It is **reauthorable** — pick up and move cats freely as the farm evolves.

---

## 9. Food & Farms

| Tier | Food | Source |
|---|---|---|
| 0 | Grass | Pasture (everywhere — no farm needed) |
| 1 | Carrot | Carrot Farm |
| 2 | Apple | Apple Orchard |
| 3 | Mushroom | Mushroom Cave |

### Daily food caps

Each food has a **daily production cap** that resets when the player sleeps:

- **Grass** has a flat daily cap that does *not* require a building. It comes for free; it's the floor of the economy.
- **Carrot / Apple / Mushroom** have **no base cap** — you must build farms to produce them. Each farm of that type adds to its tier's daily cap. There's a worldwide max number of farms per type.

Capped food simply stops appearing on its source when the day's allotment is reached. Empty farm = capped, will refresh after sleep.

### Equal milk values

All milk is worth the same regardless of which cow produced it. Tiers do not differentiate milk value.

### Why tiers matter

With equal milk values and per-tier daily caps, the player needs cows of multiple food types to fully utilize their daily food budget. A farm of all grass cows leaves carrot/apple/mushroom production unused. A farm of all mushroom cows can't be fed (cap too small without enough caves). The optimal farm is automatically a portfolio. Tier diversity emerges from supply constraints, not from output value.

The player can always choose to ignore optimization — under-fed cows simply wait without producing, no punishment.

---

## 10. Buildings

| Building | Purpose |
|---|---|
| **House** | Holds 1 cowgirl OR 1 maid. The sign on the side is a **food sign** for cows or a **role sign** for maids. The sign appears only after the house is occupied. |
| **Love House** | Holds 2 cowgirls. Click to breed. Cooldown only resets on sleep. |
| **Sell Point** | Fixed near the Player's House. Sells milk most days; on cow-buyer days it accepts cowgirls instead. |
| **Carrot Farm / Apple Orchard / Mushroom Cave** | Tiered food sources. Each adds to its tier's daily cap. Capped per type worldwide. |
| **Pet Sanctuary** (HelperAssistant) | Houses cats. Cats spawn here, idle until picked up, return here when un-assigned. |
| **Warehouse** | Centralized resource storage. Removes the player as logistics bottleneck. Capped at 1. |
| **Library** | Mid-game opt-in. Displays the cowgirl family tree (lineage + portraits, no stats). Memorial space. Capped at 1. |
| **Player's House** | Fixed at center `(0, 0)`. Sleep target. Cat-bonding target. |
| **Decoration** | Cosmetic. Pure flex. |
| **Final Structure** | Win condition. Distance 7 south. |

### Building selection — the two signs on a plot

Each empty plot has **two physical signs** flanking the land:

- **Type sign (right)** — press the contextual button to **cycle** through the building types valid at this ring. The icon and label update to whatever's currently selected. Only buildings unlocked at the current ring (and whose worldwide / per-ring caps haven't been hit) appear in the cycle.
- **Coin sign (left)** — press the contextual button to **deposit coins** toward the currently-selected type. The progress display updates as you pay it down. Once any coin has been deposited, the type cycle is **locked** for that plot — the building is committed. A "selected" badge appears on the type sign so the choice is visually frozen.

The land plot itself is not interactable. Walk up to the appropriate sign for the action you want.

The Final Structure and the central Player's House are fixed (no cycling) at their reserved coordinates.

---

## 11. Sleep, Days, and the Sell Point Rotation

### Sleep

The player walks to their house and presses the contextual button to sleep. Sleeping advances the day and:
- Resets all cow exhaust to full.
- Refreshes all daily food caps.
- Clears all Love House cooldowns.
- Rotates the Sell Point's accepted-product mode.

Sleep itself has a cooldown to prevent spam. While on cooldown, clicking on the Player's House subtracts a small fixed amount per click — the player always has something meaningful to do, never blocked, never idle-waiting.

The day cycle is **structural pacing, not time pressure.** There is no ticking clock within a day, no sky darkening, no "before the day ends" panic. Days end when the player chooses to sleep.

### Sell Point modes

The Sell Point rotates between two modes on a fixed day cycle (most days = milk; every Nth day = cows). A sign next to the Sell Point displays today's mode:

- **Milk mode** (most days) — sells milk at base rate (1 milk = 1 coin).
- **Cow-buyer mode** (every Nth day) — accepts cowgirls in exchange for coins. Cow price scales with **tier** + **rested exhaust** (a fully-rested high-tier cow sells for more than a tired low-tier one).

When a cow is sold during cow-buyer mode, she walks to a holding area scattered around the Sell Point. She stays there for the rest of the run as part of the world's evolving population — a visual reminder of the herd you've built and let go.

This creates a parallel economy: sell milk (steady income, slow accumulation) vs. breed-and-sell cows (burst income on cow-buyer days). Players choose which build to lean into; hybrids are natural.

---

## 12. Multiplayer (Optional)

Player 2 joins as a **humanized maid**. They share the maid's interaction surface — own bag, role-locked resource carrying, role-cycle on empty interact (no nearby target), and the same drop-at-player-or-warehouse pattern as AI maids.

Player 2 is **tactical**: they pick what to gather first, which cow to milk, where to walk, what role to play. The strategic surface is reserved for Player 1 and **enforced server-side** — when a helper-player tries one of the strategic interactions, the click is silently rejected (no animation, no state change). The locked actions are:

- **Sleep** at the Player's House (advances the day cycle).
- **Breeding** at the Love House (assigning cows, starting the breed).
- **Cycling building type** on a land plot (locks once any coin is invested anyway).
- **Picking up / placing cats**.
- **Selling a cowgirl** on a cow-buyer day. (Selling milk is open to either player — that's tactical.)

Everything else — milking cows, harvesting food, depositing coins via the price sign, cycling a cow's food sign or a maid's role sign, transferring bag contents to/from the main player — stays open to both players.

When Player 2 joins, one of the AI maid breeding-pool slots is removed — Player 2 takes that slot in the workforce, so the unlock cadence still feels right.

Cooperation is mediated through the existing resource flow. If Player 1 wants Player 2 to help build, Player 1 deposits coins where Player 2 can pick them up — same loop the AI Builder follows in single-player.

The asymmetry preserves Player 1's authorship of the farm while giving Player 2 meaningful, varied work.

---

## 13. Strategic Depth

What is the player actually choosing across a run?

1. **Cat distribution.** The central strategic axis — how active vs. passive do you want to be? Hermit, Empress, or hybrid? Reauthorable throughout the run.
2. **Maid role allocation.** With 4 maids and 4 roles, what's your workforce composition? Can be rebalanced as bottlenecks shift through the run.
3. **Which arm of the star to expand into first.** Farms force multi-arm growth; expanding only south reaches the Final Structure faster but starves you of food variety.
4. **Build philosophy.** Lean into the milk economy or the cow-sale economy? Hybrid both? Each shapes which buildings to prioritize.
5. **When to take the Warehouse plunge.** Costs more than a normal building, but transforms maids from "drop at player's feet" to "fully autonomous loop."
6. **Cow portfolio composition.** With daily food caps, how many of each tier do you want? Match your portfolio to your daily food production. Discovery layer adds an information-gathering puzzle: which cow do you bother testing?
7. **Sleep timing.** Sleep early to refresh resources, or push longer to make use of accumulated capacity?
8. **Cow-buyer day strategy.** Save cows for cow-buyer days, or sell continuously into milk?

---

## 14. Game Feel

- **Cowgirls in the follow chain** drift behind the player in a soft serpentine — same swarm-follow algorithm as classic Pikmin / Cult of the Lamb followers, with a separation push so they don't clip.
- **Maids have navmesh avoidance** so they look purposeful, not glued to the player.
- **Cats** ride on the shoulder when carried, leap off into a sleeping pose when placed, and idle-wash themselves when stationary at the Sanctuary.
- Server simulation runs deterministically (server/client lockstep). Inputs are buffered.
- The contextual button has a satisfying "clink" + tiny milk-bottle animation per successful click.
- Anime emotion icons above characters' heads communicate state at a glance — no UI panels needed.

---

## 15. Win State & Replay

When the Final Structure is built at `(0, –7)`, the run ends. Suggested win flourish: the structure unfolds into a small chapel/shrine, every cowgirl on the farm trots to it for a group bow, all maids curtsy in unison, every cat falls asleep. Run-time, breed count, peak coin readout, and sleep count shown on a ribbon.

Replayability comes from:
- Strategic branching — Hermit vs. Empress vs. hybrid produce different runs.
- Cat distribution choices — every run can use the cats differently.
- Build philosophy — milk-focused vs. cow-sale-focused vs. mixed.
- Sleep count as a quiet metric — players who care about optimization see "completed in N sleeps" and can target lower.

The sleep count exists for those who want a metric to chase; players who don't care won't notice it. No leaderboards, no achievements forced on the player.

---

## 16. UI Philosophy

The game has essentially **no traditional UI**. State is communicated through:

- The world itself — empty farms = capped, sleeping cats = unassigned, bow color = role, badge on the type sign = building committed.
- Anime emotion icons above characters' heads.
- Bell colors and charms on cowgirls (subtle bond/trait indicators).
- Glow on valid targets when carrying something (cats).
- Signs flanking houses and plots — food/role/blueprint, cycle-tap to set.
- Question marks and hearts/broken-hearts on food signs to convey discovery state.

The two allowed overlays:
- **Family tree** at the Library building (lineage + portraits, no stats — memorial only).
- **End-of-run ribbon** (stats summary at win).

Both are opt-in and triggered by physical objects in the world.

---

## 17. Engineering Reference

The canonical balance config lives at:
```
Server/Template.Shared/GameData/Balance.cs
```

`Balance.cs` is the single source of truth for every tunable lever — costs, cooldowns, caps, click cadences, fail rolls, era multipliers, ring-unlock thresholds, per-building caps (both world and per-ring), starting state, formulas, and feature toggles (depression, love events, etc.).

> Treat any numbers in this document as illustrative rather than authoritative. If you need an exact value, read `Balance.cs`.

Selected feature toggles worth knowing about while reading the doc:
- `Balance.Cow.DepressionEnabled` — failed-breed depression (currently off).
- `Balance.Love.Enabled` — love events (currently on).
- `Balance.Build.Limit.<Building>.{World, PerRing}` — per-building caps; `-1` on either axis disables that side of the check.

---

## 18. Future Considerations

Ideas scoped out of the current build, kept here for future iteration after the core loop is validated:

- **Social layer.** Cowgirls develop bonds with the player and with each other; relationships affect breeding and milk production.
- **Exhaust-cost breeding + depression risk.** Breeding consumes exhaust from both cows; tired cows are more likely to fail emotionally and become depressed. Some scaffolding already exists in code, gated behind `Balance.Cow.DepressionEnabled = false`.
- **Buyer-NPC tier acquisition.** Currently the cow-buyer is just a Sell Point mode (you sell *to* them, you don't buy *from* them). A future expansion could let the player *purchase* fresh cows of new food tiers from the visiting buyer.
- **Cow cart pickup.** Currently sold cows stay around the Sell Point indefinitely as flavor. A future polish pass could add a periodic cart that hauls accumulated sold cows off-screen.
- **Post-hoc accomplishment scrapbook** at the player's house.
- **Multiple win conditions** — Shrine of Abundance / Devotion / Prosperity, each requiring a different farm shape.
- **Map randomization** beyond food layout.

---

*"All cows are girls. All helpers wear bows. All cats are in charge. Build the shrine before the milk runs out."*
