# Wound Bound — the game

A computer game built on the **Wound Bound** tabletop rules, in [Godot 4](https://godotengine.org)
with C#.

## Layout

```
WoundBoundGame.sln
external/WoundBound/        git submodule: the rules repo (Azure DevOps) — rules text, WoundBound.Rules, data catalogues
src/WoundBound.Game/        plain C# — encounters, turns, moves, attacks. No Godot dependency.
tests/WoundBound.Game.Tests/ xUnit tests of the above, with scripted dice
tools/WoundBound.Sim/       bot v bot simulator: plays many seeded fights per tactic and compares them
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

## The fights

Pick one on the battle screen:

- **Signpost ambush:** the Quickstart Adventure's own (Blood and Vintage, "Roadside Ambush"), for a
  table of three — the lantern-bearer under the signpost, an ambusher on the ridge and another in the
  trees, all Skirmishers with Cowardice. The objective: take the lantern-bearer, since a captured
  bandit can be made to talk. The ambushers' crossbows wait for statblock ranged attacks, so they use
  the knives their statblock gives them for close work; the night, the lights and the opening volley
  aren't in yet.
- **Roadside ambush:** a party of three — the Dumb Fighter of Core Rules §12, and Salla Rinehart
  (Nimble Duelist, rapier) and Drovan Thale (Nimble Ranger, short bow, knife at the ready) from the
  Quickstart Adventure — against a Tollroad Bandit, a Timber Wolf and two Bramble Ratkin: a Threat
  Value of 3, a Standard fight for three (GM Reference §5). The catalogue has no stiletto or hunting
  knife, so a dagger stands in for each.
- **The first slice:** the Dumb Fighter alone (light armour, longsword in hand, short bow at the
  ready) against three Bramble Ratkin.

What they exercise, all through `WoundBound.Rules`:

- Players choose their own order: at the start of each round, and after each gap, the players pick
  which character still to act goes next (click a token with a pale ring, or its button). The last
  one left goes without being asked. A character who is down is passed over, and the GM still takes
  the adversary turn that follows them.

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
- Standing Your Ground: Dodge, Grounded and Resolute, +2 DT / ST / RT until the next turn. The first
  takes the move, a second the action, a third an extra action. None for anyone who has moved,
  none twice a turn, no moving after, and Disadvantage on attacks while it lasts. Stacks with a
  panic dodge. The GM's creatures can do it too, though the simple GM never does.
- Grab, Shove and Trip (Core Rules §9), for anybody: an unarmed attack roll (a foe's Attack bonus)
  against the target's Stability Threshold, within the body's own reach, for the action and no
  Stamina. Trip leaves the target Prone — Off-guard (−2 DT, Disadvantage on attacks), crawling until
  it spends a move to stand. Shove is Forced Movement a square straight away: into a creature or the
  field's edge, the rest is Body wounds less armour; the target may arrest it, and being shoved out of
  a foe's reach draws its reaction. Grab needs a free hand and leaves the target Held — no moving
  itself until it breaks free with its action, or the hold ends (the holder lets go, moves away, takes
  up something two-handed, or drops). Criticals add the other effect, a critical failure puts the one
  who lunged on the ground, and a Swarm can't be put hands on. The target may stand firm after seeing
  the roll: 1 Stamina for +2 ST, a tactical spend. Grounded finally has something to resist.
- Foes that break and run (GM Reference §8): Cowardice breaks a creature below half its Body or when
  it sees one of its side go down; Not a Fanatic, at a second. A broken creature runs, putting ground
  between itself and the party. The fight stays in effect until every adversary has left the board —
  and an adversary has left the board once no character has it within Long (16 squares). Down, gone,
  or — broken and held fast with nobody left beside it — given up. The party fights are on a 28×18
  field so there's room to get that far; the battle screen scales the squares to fit.
- Reactions to a foe leaving a character's reach — moving, closing in at the round's end, or shoved:
  strike as they go, or Grab, Shove or Trip them, rolled against Dodge and
  Stability both and stopping them where they're caught (Core Rules §9, "Against a creature on the
  move"). 1 Stamina, a tactical spend, an Immediate Opportunity for the GM. The foe answers as it
  would a manoeuvre, and a dodged critical is a Riposte.
- Objectives: what a fight is about besides dropping everybody — so far, someone who mustn't get
  away (`Objective.DontLetEscape`, `Objective.NoneEscape`). Shown on screen, logged at the end, and
  counted by the simulator.
- GM Opportunities: banked from extra actions (at most 3), Immediate from reactions and Threshold
  boosts (lapsing at the spender's next turn); spent on an extra adversary turn — on a creature that
  can strike, ideally one that has already acted — or, with a full bank, on an attack's Advantage.
- One owed adversary turn per character per round, the cap counted when the fight begins and never
  falling as characters drop; at the round's end every adversary that took no turn closes in with a
  single move.

## The GM brain

Every choice the rules leave to the GM goes through `IGmBrain` (`src/WoundBound.Game/Combat/Brains/`):
- who takes an owed turn
- whether to buy an extra turn with an Opportunity
- each step of an adversary's turn (move, attack or Grab/Shove/Trip — with or without Advantage —
  stand up, break free, let go, or end)
- whether to make a reaction attack, panic dodge, stand firm or arrest a shove
- where a broken creature runs
- where idle adversaries close in at the round's end

The `Encounter` owns the rules. It asks the brain, checks the answer, and carries it out, so a brain
can never break a rule. An illegal answer throws. Brains see the fight through `BattleView`, which
answers rule questions ("could I hit them from there?") but can't change anything. A turn is asked
for one step at a time, so a brain can re-plan after a dodge or a Riposte.

`SimpleGmBrain` is the placeholder: everything goes for the nearest character. Pass a different
brain to `Encounter` (or `SampleEncounters.FighterVersusRatkin`) to change how the GM plays.

Not yet for fleeing: the GM's creatures reacting to a leaver with anything but a strike; Frightened;
terrain to run into, round or behind.

`TrippingGmBrain` is a second baseline: a creature that isn't a Minion trips a character its pack can
also reach before swinging.

Not yet: Drag, and Grab/Shove/Trip taken as a reaction against somebody leaving your reach; the Martial
manoeuvres (Cleave, Topple, Ring, Hook, Disarm, Sunder); Solo Resilience and Unshakable; Pack Tactics, thrown weapons, drawing from the pack, two-weapon fighting,
shields, frights (so Resolute has nothing to resist yet), obstacles, movement
paths (a move is a hop from square to square). Characters make no reaction attacks yet against an
adversary leaving their reach. The Quickstart characters' class features — Nimble's free step and
Exploits, Sacrifice, Interrupt — aren't in, and statblock ranged attacks (the Reedmarsh Skulker's
bow) aren't modelled, so the sample fights use melee foes. The GM can't yet hold an owed turn for a
later gap.

## Bot v bot

A party brain (`IPartyBrain`) can take the players' seat while the GM brain keeps its own, so a
fight plays itself. It also chooses who acts next: `TacticalPartyBrain` sends in whoever can
already strike, and otherwise whoever is nearest an enemy.
- **The driver:** `BotDriver` carries out the party's choices through the same `Encounter` commands
  the buttons call, so a bot gets exactly the rules a player does. A refused choice throws.
- **The tactics:** `TacticalPartyBrain` plays by `PartyTactics`, a handful of switches the
  simulator can measure. They cover shooting first with a bow at the ready, buying extra attacks,
  bracing when hurt, a Stamina reserve, and when to panic dodge. The presets are
  `PartyTactics.Presets`.
- **In the game:** turn on **Bot plays the party** and pick a tactic to watch it play.
- **The simulator:** plays every preset over the same seeded fights and compares them.

  ```
  dotnet run --project tools/WoundBound.Sim -- --fights 1000 --seed 1
  ```

  The signpost ambush is the default, and the table leads with how often every objective was met;
  `--fight party` plays the roadside ambush, `--fight solo` the Dumb Fighter against the ratkin, and
  `--gm tripping` swaps in the tripping GM. Party tactics now include reacting to foes leaving reach
  (strike at everyone, or only stop runners) and hunting runners down.

  The simulator measures one thing — winning a straight fight — and not every rule exists for that.
  Grab, Shove and Trip are there for the moments the story makes them matter: slowing a foe who is
  running, hauling a friend back from a cliff edge, taking a prisoner. In a plain fight to the last
  wound, trading the action for a Trip loses to simply hitting, on both sides; that says the ambush
  has no such moment in it yet, not that the rule is wasted. Fights with objectives other than
  "drop everyone" (an escape to stop, someone to save, ground that matters) are where they'll earn
  their place, and where the simulator should measure them.

  The signpost is the first: with foes that run until they're beyond Long, it becomes a chase, and
  parties that react to runners and hunt them down take the lantern-bearer most often (about 87–89%
  of fights, against 82–84% for those that don't).

As the rules grow, each new choice becomes a new switch, and the simulator says whether it wins
fights.

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
