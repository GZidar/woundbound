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

The Dumb Fighter of Core Rules §12 (longsword, light armour) against three Bramble Ratkin from the
starting adversaries. What it exercises, all through `WoundBound.Rules`:

- Moves priced by distance band and Speed — up to Short for one move, Middle spends the action too.
- Attack rolls, 2d12 + Tier + attack stat vs Dodge Threshold, with criticals and absolute 1s/12s.
- Weapon damage, armour absorption, the Minion rule, and a critical failure wounding the attacker.
- Spending Stamina for an extra action, capped by Tier, and handing the GM an Opportunity — which
  the ratkin spend on Advantage.
- One adversary turn per character per round.

Not yet: reactions (panic dodge, Riposte), manoeuvres, Pack Tactics, multiple party members
choosing their own order, obstacles.

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
