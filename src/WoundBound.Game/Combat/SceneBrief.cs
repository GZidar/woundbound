using WoundBound.Game.Combat.Brains.Utility;

namespace WoundBound.Game.Combat
{
    /// <summary>How a fight fits the story (the user, 2026-11-08).</summary>
    public enum Stakes
    {
        /// <summary>A patrol, a wandering band, a speed bump on the road: nothing turns on it.</summary>
        Incidental,

        /// <summary>It advances or blocks the story: losing it costs the party something.</summary>
        Relevant,

        /// <summary>A set piece the story turns on.</summary>
        Climactic,
    }

    /// <summary>How much the GM's creatures will pay for what they want.</summary>
    public enum Commitment
    {
        /// <summary>Paid to be here, not to die: the GM lets them break once hurt or half their side is down, whatever its intent.</summary>
        Hireling,

        /// <summary>They mean it. They break as the GM's intent allows (GM Reference §8).</summary>
        Committed,

        /// <summary>They never break unless their own statblock says so.</summary>
        Fanatic,
    }

    /// <summary>
    /// What the GM's creatures want from the fight. It describes their side only: a brief never
    /// assumes or forces what the players do (the user's ruling, 2026-11-08).
    /// </summary>
    public abstract record AdversaryGoal(string Text)
    {
        /// <summary>The party dead or down: the fight as it has always been played.</summary>
        public sealed record Kill() : AdversaryGoal("Kill them");

        /// <summary>
        /// The party taken alive. Characters are knocked down, never struck once they are, and the
        /// goal is met only if the party falls with nobody dead.
        /// </summary>
        public sealed record Capture() : AdversaryGoal("Take them alive");

        /// <summary>
        /// Keep the party out of <paramref name="Zone"/> until the end of round <paramref name="Rounds"/>.
        /// Their creatures fight from inside it and don't chase anyone out of it.
        /// </summary>
        public sealed record Hold(DeploymentZone Zone, int Rounds) : AdversaryGoal($"Hold {Zone.Name} for {Rounds} rounds");

        /// <summary>
        /// Seize <paramref name="Prize"/> and carry it off the board, towards <paramref name="Exit"/>
        /// where there is one. It's met once its carrier gets beyond Long of every character.
        /// </summary>
        public sealed record Take(Prize Prize, DeploymentZone? Exit = null) : AdversaryGoal($"Take {Prize.Name}");

        /// <summary>
        /// Get away: <paramref name="Who"/>, or at least half the band where nobody is named, beyond
        /// Long of every character, towards <paramref name="Exit"/> where there is one.
        /// </summary>
        public sealed record Escape(DeploymentZone? Exit = null, IReadOnlyList<Combatant>? Who = null)
            : AdversaryGoal(Who is { Count: > 0 } ? $"Get {string.Join(" and ", Who.Select(w => w.Name))} away" : "Get away");
    }

    /// <summary>
    /// Something worth carrying off: a strongbox, a relic, a ledger. It lies on the board until a
    /// creature picks it up for an action, and falls where its carrier falls.
    /// </summary>
    public sealed class Prize(string name, GridPosition at)
    {
        public string Name { get; } = name;

        /// <summary>Where it lies, or null while somebody carries it.</summary>
        public GridPosition? At { get; internal set; } = at;

        /// <summary>Who carries it, if anybody.</summary>
        public Combatant? Carrier { get; internal set; }

        /// <summary>Carried off the board.</summary>
        public bool IsGone => Carrier?.HasFled == true;
    }

    /// <summary>
    /// Why a fight is happening and what the GM's side wants from it. It informs the GM's defaults
    /// (<see cref="DefaultChallenge"/>, morale by <see cref="Commitment"/>, how its creatures play the
    /// <see cref="Goal"/>), and the motive is a line for the table to read.
    /// </summary>
    public sealed record SceneBrief(Stakes Stakes, AdversaryGoal Goal, Commitment Commitment = Commitment.Committed, string Motive = "")
    {
        /// <summary>
        /// The challenge a GM steers towards when nobody chooses one: an incidental fight Easy, a
        /// relevant one Average, a climactic one Push. Overridable, and never Merciless, which is only
        /// ever an explicit setting (the user's rulings, 2026-11-08).
        /// </summary>
        public Challenge DefaultChallenge => Stakes switch
        {
            Stakes.Incidental => Challenge.Easy,
            Stakes.Relevant => Challenge.Average,
            _ => Challenge.Push,
        };
    }
}
