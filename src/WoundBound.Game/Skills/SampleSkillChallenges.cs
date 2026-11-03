using WoundBound.Rules.Calculation;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Skills
{
    /// <summary>
    /// Skill Adversaries from the books, as written there — read through the rules library's own
    /// statblock reader, so what the game plays is what the page says.
    /// </summary>
    public static class SampleSkillChallenges
    {
        private const string CrossTheRiverBlock = @">| **Cross the River** | T1 |
>| --- | --- |
>| *Geographical - Passive Moment* | |
>| **STAKES** | |
>| ***Story:*** Lost time, they may be late reaching their intended destination. | |
>| **APPROACHES** | **Difficulty** |
>| ***Persuasion:*** Guide the wagon across | 18 |
>| ***Menace** or **Intimidate:*** Bully the animals across | 24 |
>| **PROBE** | |
>| ***Survival:*** Find an easier path through the water | 18 |
>| **REVEALS** | |
>| ***Reveals:*** a line of broken water upstream marks where the rocks sit shallow - the animals will take that line if someone leads them. | |
>| ***Reveals - Persuasion:*** Guide the wagon across | 12 |
>| ***Reveals - Menace** or **Intimidate:*** Bully the animals across | 18 |
>| **SUCCESS** | |
>| ***Leads to:*** Wagon breaks on the road | |
>| **FAILURE** | |
>| ***Leads to:*** Wagon breaks in the water | |";

        private const string RepairInTheRiverBlock = @">| **Repair the Wagon in the River** | T1 |
>| --- | --- |
>| *Environmental - Passive Track* | 3 / 3 |
>| **STAKES** | |
>| ***Stamina:*** each failed check costs the character who made it 1, 2 or 3 Stamina, by how far it missed. | |
>| **HURDLE 1 - Calm the animals** | **Difficulty** |
>| ***Approach - Persuasion:*** Keep the animals from freaking out | 18 |
>| ***Probe - Insight:*** See that food might be useful | 12 |
>| ***Reveals - Persuasion:*** Keep the animals from freaking out | 12 |
>| **HURDLE 2 - Lift the frame** | |
>| ***Approach - Athletics:*** Lift the frame | 18 |
>| ***Probe - Search:*** Find a lever | 12 |
>| ***Reveals - Athletics:*** Lift the frame | 12 |
>| **HURDLE 3 - Attach the wheels** | |
>| ***Approach - Sleight of Hand:*** Slip the wheels on | 18 |
>| ***Probe - Investigate:*** Find some grease in the toolbox | 12 |
>| ***Reveals - Sleight of Hand:*** Slip the wheels on | 12 |
>| **SUCCESS** | |
>| ***Leads to:*** Roadside Ambush | |
>| **FAILURE** | |
>| ***Leads to:*** Roadside Ambush, late and worn out | |";

        private const string RepairOnTheRoadBlock = @">| **Repair the Wagon on the Road** | T1 |
>| --- | --- |
>| *Environmental - Passive Track* | 3 / 3 |
>| **STAKES** | |
>| ***Story:*** Time is lost while repairing the wagon. | |
>| **HURDLE 1 - Calm the animals** | **Difficulty** |
>| ***Approach - Persuasion:*** Keep the animals from freaking out | 18 |
>| ***Probe - Insight:*** See that food might be useful | 18 |
>| ***Reveals - Persuasion:*** Keep the animals from freaking out | 12 |
>| **HURDLE 2 - Lift the frame** | |
>| ***Approach - Athletics:*** Lift the frame | 18 |
>| ***Probe - Search:*** Find a lever | 18 |
>| ***Reveals - Athletics:*** Lift the frame | 12 |
>| **HURDLE 3 - Attach the wheels** | |
>| ***Approach - Sleight of Hand:*** Slip the wheels on | 18 |
>| ***Probe - Investigate:*** Find some grease in the toolbox | 18 |
>| ***Reveals - Sleight of Hand:*** Slip the wheels on | 12 |
>| **SUCCESS** | |
>| ***Leads to:*** Roadside Ambush | |
>| **FAILURE** | |
>| ***Leads to:*** Roadside Ambush, later still | |";

        private const string CollapsingStairBlock = @">| **The Collapsing Stair** | T1 |
>| --- | --- |
>| *Geographical - Active Track* | 3 / 3 |
>| **STAKES** | |
>| ***Wound - Body:*** dealt by the Opposition roll, as falling debris and a hard landing. | |
>| **OPPOSITION** | |
>| ***Roll:*** +1 v ST / 1d6 Body. A section giving way underneath whoever's crossing it that round. | |
>| **APPROACHES** | **Difficulty** |
>| ***Hurdles:*** the upper switchback, the broken mid-flight where the treads are gone entirely, and the last drop to solid floor | |
>| ***Acrobatics** or **Athletics:*** pick a safe line down | 18 |
>| **PROBE** | |
>| ***Survival:*** read which stone will hold | 18 |
>| **REVEALS** | |
>| ***Reveals:*** one particular run of steps is sound load-bearing stone rather than rot - sticking to that line turns the Opposition roll against whoever's crossing it that round into Disadvantage for the rest of the descent. | |
>| **RAMP** | |
>| ***Ramp:*** each failure collapses more of the stair, raising the Difficulty by one step on the ladder (see Ramp, §6) - the worked example for this book's Ramp rule. | |
>| **FEATURES** | |
>| ***Don't Rush:*** a party that rushes makes the rest of the descent worse for itself. | |";

        private const string PistonSnareBlock = @">| **The Piston Snare** | T1 |
>| --- | --- |
>| *Trap - Active Track* | 3 / 3 |
>| **STAKES** | |
>| ***Wound - Body:*** dealt by the Opposition roll. | |
>| **OPPOSITION** | |
>| ***Roll:*** +3 v DT / 1d6 Body. Triggers automatically on a failed Tamper attempt, or on anyone who blunders in without checking first. | |
>| **HURDLE 1 - Find the false panel** | **Difficulty** |
>| ***Approach - Search:*** spot it before it triggers | 18 |
>| **HURDLE 2 - Get the panel open without loading the plate** | |
>| ***Approach - Tamper:*** work the panel | 18 |
>| **HURDLE 3 - Pin the spring and disconnect the bar** | |
>| ***Approach - Tamper:*** work the mechanism | 18 |
>| **PROBE** | |
>| ***Search:*** read how it's triggered | 18 |
>| **REVEALS** | |
>| ***Reveals:*** it's pressure-plate, not tripwire. | |
>| ***Reveals - Tamper:*** for the rest of this Skill Adversary | 12 |
>| ***Reveals - Acrobatics:*** step around the plate entirely - a new valid approach | 12 |
>| **FEATURES** | |
>| ***Built to Fight Back:*** a trap built to fight back, not just to block - treat it as dangerous as a Skirmisher-tier Combat Adversary for pacing purposes, even though it never takes a ""turn"" of its own. | |";

        private const string WeepingVentBlock = @">| **The Weeping Vent** | T1 |
>| --- | --- |
>| *Trap - Active Track* | 3 / 3 |
>| **STAKES** | |
>| ***Wound - Body (Direct):*** dealt by the Opposition roll. | |
>| **OPPOSITION** | |
>| ***Roll:*** +2 v FT / 1d6 Body (Direct). A fresh gout, when someone misjudges the timing - it has to be breathed to do anything, which is a question for the lungs rather than the feet. | |
>| **HURDLE 1 - Cross the plated stretch without loading it** | **Difficulty** |
>| ***Approach - Acrobatics:*** cross without putting weight on the plates | 18 |
>| **HURDLE 2 - Break open the corroded housing** | |
>| ***Approach - Tamper:*** open the housing | 18 |
>| **HURDLE 3 - Seal or reroute the vent** | |
>| ***Approach - Tamper:*** seal or reroute it | 18 |
>| **PROBE** | |
>| ***Search:*** look over the grate and its housing | 18 |
>| **REVEALS** | |
>| ***Reveals:*** the corrosion runs both ways - the grate's housing is as weak as its seal. | |
>| ***Reveals - Force:*** drive a wedge into the housing, jamming the vent shut for good rather than merely surviving it - a new valid approach | 12 |
>| **EXPERIENCE** | |
>| ***Experience:*** sealing the vent is the obvious place for a character with a metalwork, mining, or pipefitting background to argue an Experience bonus - the roll is still Tamper, with the Experience added on top (Core Rules §7). | |
>| **FEATURES** | |
>| ***Companion to the Snare:*** worth putting in the same ruin as the Piston Snare for the contrast. The Snare's bar is a physical blow an armoured character largely shrugs off; the Vent's gas does not care in the slightest what anyone is wearing, and it rolls against Fortitude where the Snare rolls against Dodge - so the character to send across is the one with the hardiest constitution, not the steadiest feet or the heaviest plate. A party that has learned to send the plate-wearer in first learns something else here. | |";

        /// <summary>Reads a statblock into the template the game plays; throws if the page can't be read.</summary>
        public static AdversaryTemplate Read(string block) =>
            StatblockReader.Read(block).Adversary ?? throw new InvalidOperationException($"Couldn't read the statblock: {block.Split('\n')[0]}");

        /// <summary>The Quickstart's river (Blood and Vintage): Cross the River, then the wagon breaks — in the water on a failure, on the road on a success.</summary>
        public static IReadOnlyList<SkillNode> TheWagon() =>
        [
            new("Cross the River", Read(CrossTheRiverBlock), OnSuccess: "Repair the Wagon on the Road", OnFailure: "Repair the Wagon in the River"),
            new("Repair the Wagon in the River", Read(RepairInTheRiverBlock), OnSuccess: SkillNode.Cleared + ": Roadside Ambush", OnFailure: SkillNode.Lost + ": Roadside Ambush, late and worn out"),
            new("Repair the Wagon on the Road", Read(RepairOnTheRoadBlock), OnSuccess: SkillNode.Cleared + ": Roadside Ambush", OnFailure: SkillNode.Lost + ": Roadside Ambush, later still"),
        ];

        /// <summary>The Collapsing Stair (GM Reference §12): Geographical, Active, a Track that Ramps.</summary>
        public static IReadOnlyList<SkillNode> TheStair() =>
            [new("The Collapsing Stair", Read(CollapsingStairBlock), OnSuccess: SkillNode.Cleared + ": down", OnFailure: SkillNode.Lost + ": a hard landing")];

        /// <summary>The Piston Snare (GM Reference §12): a Trap whose Opposition fires on a failed check.</summary>
        public static IReadOnlyList<SkillNode> TheSnare() =>
            [new("The Piston Snare", Read(PistonSnareBlock), OnSuccess: SkillNode.Cleared + ": disarmed", OnFailure: SkillNode.Lost + ": still armed")];

        /// <summary>The Weeping Vent (GM Reference §12): gas against Fortitude, Direct.</summary>
        public static IReadOnlyList<SkillNode> TheVent() =>
            [new("The Weeping Vent", Read(WeepingVentBlock), OnSuccess: SkillNode.Cleared + ": sealed", OnFailure: SkillNode.Lost + ": still venting")];
    }
}
