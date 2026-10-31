# Wound Bound — the game

A computer game built on the **Wound Bound** tabletop rules, in [Godot 4](https://godotengine.org)
with C#.

## Layout

```
WoundBoundGame.sln
external/WoundBound/        git submodule: the rules repo (Azure DevOps) — rules text, WoundBound.Rules, data catalogues
src/WoundBound.Game/        plain C# — encounters, turns, moves, attacks. No Godot dependency.
tests/WoundBound.Game.Tests/ xUnit tests of the above, with scripted dice
game/                       the Godot project — draws the board and forwards clicks. No rules in here.
```

The split mirrors the session tracker's: `WoundBound.Rules` owns every formula, `WoundBound.Game`
owns the flow of a fight, and `game/` only presents it. Anything that can be tested without
opening Godot lives in `src/`.

## Getting started

1. Clone with the submodule (you'll need access to the Azure DevOps repo as well as this one):

   ```
   git clone --recurse-submodules https://github.com/GZidar/woundbound.git
   ```

   Already cloned? `git submodule update --init`.

2. Install the **.NET 10 SDK** and **Godot 4.7.2 — the .NET edition** (not the standard build).
   The editor version must match the `Godot.NET.Sdk/4.7.2` in `game/WoundBoundGame.csproj`.

3. Tests: `dotnet test`

4. Play: open `game/project.godot` in Godot and press **F5** (or Build, then Run). The first build
   copies the rules catalogues into `game/data/rules/`.

## The first slice

The Dumb Fighter of Core Rules §12 (light armour, longsword in hand, short bow at the ready) against
three Bramble Ratkin from the starting adversaries. What it exercises, all through `WoundBound.Rules`:

- Moves priced by distance band and Speed — up to Short for one move, Middle spends the action too.
- Two loadouts, in hand and at the ready, swapped for a move (or an action once the move is spent).
- Melee by Reach in squares, Unwieldy weapons included; ranged attacks by band — none at Close, the
  target's DT raised at Middle and Long, the damage die falling off with range, and a critical's
  bonus die still the weapon's full base die.
- Attack rolls, 2d12 + Tier + attack stat vs Dodge Threshold, with criticals and absolute 1s/12s.
- Weapon damage, armour absorption, the Minion rule, and a critical failure wounding the attacker.
- Tactical Stamina spends, capped by Tier: an extra action, a panic dodge, a reaction attack.
- Panic dodge, offered after the roll and only when +2 Dodge Threshold would turn the hit; against
  a critical it's a Riposte and the attacker takes the wound. The GM's creatures dodge too.
- Reaction attacks against anybody leaving an enemy's reach.
- GM Opportunities: banked from extra actions (at most 3), Immediate from reactions and Threshold
  boosts (lapsing at the spender's next turn); spent on an extra adversary turn — on a creature that
  can strike, ideally one that has already acted — or, with a full bank, on an attack's Advantage.
- One owed adversary turn per character per round, the cap counted when the fight begins and never
  falling as characters drop; at the round's end every adversary that took no turn closes in with a
  single move.

## The GM brain

Every choice the rules leave to the GM goes through `IGmBrain` (`src/WoundBound.Game/Combat/Gm/`):
- who takes an owed turn
- whether to buy an extra turn with an Opportunity
- each step of an adversary's turn (move, attack, with or without Advantage, or end)
- whether to make a reaction attack or panic dodge
- where idle adversaries close in at the round's end

The `Encounter` owns the rules. It asks the brain, checks the answer, and carries it out, so a brain
can never break a rule. An illegal answer throws. Brains see the fight through `GmView`, which
answers rule questions ("could I hit them from there?") but can't change anything. A turn is asked
for one step at a time, so a brain can re-plan after a dodge or a Riposte.

`SimpleGmBrain` is the placeholder: everything goes for the nearest character. Pass a different
brain to `Encounter` (or `SampleEncounters.FighterVersusRatkin`) to change how the GM plays.

Not yet: manoeuvres, Pack Tactics, thrown weapons, drawing from the pack, two-weapon fighting,
shields, Standing Your Ground (Dodge / Grounded / Resolute actions), multiple party members choosing
their own order, obstacles, movement paths (a move is a hop from square to square).

## Updating the rules

The submodule is pinned to a commit. To pick up new rules:

```
cd external/WoundBound
git pull
cd ../..
git add external/WoundBound
git commit -m "Update rules"
```

## Exporting

The catalogues are plain JSON, which Godot does not pack by default. When creating an export
preset, add `data/rules/*.json` under **Resources → Filters to export non-resource files**.
