namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Putting hands on an enemy instead of striking it (Core Rules §9, "Grab, Shove, Trip and
    /// Drag"). Open to anybody, no Stamina, no weapon: an unarmed attack roll against the target's
    /// Stability Threshold, dealing no damage. Drag isn't in yet.
    /// </summary>
    public enum ControlAction
    {
        /// <summary>Needs a free hand. The target is Held for as long as the hold lasts.</summary>
        Grab,

        /// <summary>The target is pushed up to your own reach, straight away from you — Forced Movement.</summary>
        Shove,

        /// <summary>The target is knocked Prone.</summary>
        Trip,
    }

    /// <summary>What a defender is being asked to spend 1 Stamina on, once they've seen what's coming.</summary>
    public enum ReactionKind
    {
        /// <summary>+2 Dodge Threshold against an attack that would hit — a Riposte, against a critical.</summary>
        PanicDodge,

        /// <summary>
        /// Raise the shield in hand against an attack that would hit: its whole bonus to DT, and it
        /// stays up until the bearer's next turn — the better reaction, where there's a shield to
        /// raise (Core Rules §9, "Shields"). A Riposte against a critical, as a dodge is.
        /// </summary>
        RaiseShield,

        /// <summary>+2 Stability Threshold against a Grab, Shove or Trip — or a break-free — that would succeed (Core Rules §10).</summary>
        StandFirm,

        /// <summary>Stop a Shove before it carries them anywhere (Core Rules §9, "Forced Movement").</summary>
        Arrest,

        /// <summary>
        /// An enemy is leaving a character's reach: strike as they go, or Grab, Shove or Trip them
        /// (Core Rules §9, "Movement" and "Against a creature on the move"). Answered with
        /// <see cref="Encounter.RespondToLeaver"/>, not a yes or no.
        /// </summary>
        LeaverReaction,

        /// <summary>
        /// Mind or Soul damage is about to take a track to 0: hold at 1 instead and take the excess
        /// as Body, Direct (Core Rules §4, "Metaphysical Backlash"). A choice, not a Stamina spend —
        /// it costs no Stamina and hands the GM nothing.
        /// </summary>
        Backlash,

        /// <summary>
        /// A spell the caster declared Sustained would end now, at the start of their turn: 1 Stamina
        /// keeps it until the start of the next — a tactical spend that hands the GM a banked
        /// Opportunity (Core Rules §11, "Sustaining a Spell").
        /// </summary>
        Sustain,

        /// <summary>
        /// +2 Resolve Threshold against a spell that would land or hold — the reactive boost against
        /// a Control spell, or against a held Bane on Resolve (Core Rules §10, §11).
        /// </summary>
        BraceResolve,
    }

    /// <summary>What a character does about an enemy leaving their reach.</summary>
    public enum LeaverResponse
    {
        /// <summary>Nothing: they go, and the Stamina is kept.</summary>
        LetThemGo,

        /// <summary>A reaction attack with whatever is in hand.</summary>
        Strike,

        /// <summary>Catch hold of them — rolled against DT and ST both. Caught, they're Held where they were caught.</summary>
        Grab,

        /// <summary>Push them on from where they're caught.</summary>
        Shove,

        /// <summary>Bring them down where they're caught.</summary>
        Trip,
    }
}
