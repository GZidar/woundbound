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

        /// <summary>+2 Stability Threshold against a Grab, Shove or Trip — or a break-free — that would succeed (Core Rules §10).</summary>
        StandFirm,

        /// <summary>Stop a Shove before it carries them anywhere (Core Rules §9, "Forced Movement").</summary>
        Arrest,
    }
}
