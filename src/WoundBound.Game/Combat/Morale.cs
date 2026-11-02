using WoundBound.Rules.Catalog;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// When a creature stops being willing to fight (GM Reference §1, "Know why each adversary is
    /// there"; §8, "Let non-Solo adversaries break"). The statblock says it: Cowardice —
    /// "reduced below half its Body pool, or watching another of its side go down, it looks for a
    /// chance to break and run" — and Not a Fanatic, the same but at a second ally down. A creature
    /// with no such line fights to the last wound.
    /// </summary>
    /// <param name="Trait">The statblock line it comes from, for the log.</param>
    /// <param name="BreaksBelowHalfBody">Breaks once its Body is below half its pool.</param>
    /// <param name="BreaksWhenAlliesDown">Breaks once this many of its own side have gone down (not fled); 0 never.</param>
    public sealed record Morale(string Trait, bool BreaksBelowHalfBody, int BreaksWhenAlliesDown)
    {
        public static Morale Cowardice { get; } = new("Cowardice", BreaksBelowHalfBody: true, BreaksWhenAlliesDown: 1);

        public static Morale NotAFanatic { get; } = new("Not a Fanatic", BreaksBelowHalfBody: true, BreaksWhenAlliesDown: 2);

        /// <summary>The breaking point a statblock writes down, read off its Features by name; null for a creature that won't break.</summary>
        public static Morale? FromStatblock(StartingAdversaryInfo info) =>
            info.Features?.Select(f => f.Name).Select(name => name switch
            {
                "Cowardice" => Cowardice,
                "Not a Fanatic" => NotAFanatic,
                _ => null,
            }).FirstOrDefault(m => m is not null);

        /// <summary>Whether <paramref name="creature"/> has reached its breaking point, with <paramref name="alliesDown"/> of its side gone down.</summary>
        /// <param name="believedBody">The Body it believes it has — a wound felt and not taken counts, while it's taken in (the user's ruling, 2026-11-02).</param>
        public bool IsBroken(Combatant creature, int alliesDown, int? believedBody = null) =>
            (BreaksBelowHalfBody && (believedBody ?? creature.Body) * 2 < creature.BodyMax)
            || (BreaksWhenAlliesDown > 0 && alliesDown >= BreaksWhenAlliesDown);

        /// <summary>
        /// One step from its breaking point: one more ally down, or one more wound, would do it. Help
        /// seen coming for the other side breaks a creature in this state, and nobody steadier.
        /// </summary>
        public bool IsWavering(Combatant creature, int alliesDown, int believedBody) =>
            !IsBroken(creature, alliesDown, believedBody)
            && ((BreaksBelowHalfBody && (believedBody - 1) * 2 < creature.BodyMax)
                || (BreaksWhenAlliesDown > 0 && alliesDown + 1 >= BreaksWhenAlliesDown));
    }

    /// <summary>Where a goal of the fight stands.</summary>
    public enum ObjectiveStatus
    {
        InProgress,
        Achieved,
        Failed,
    }

    /// <summary>
    /// Something the fight is about besides dropping everybody — a Challenge's stakes (Core Rules
    /// §9, "Scenes and Challenges"). Read from the fight as it stands, never stored, so it can't
    /// disagree with the board.
    /// </summary>
    public abstract record Objective(string Name)
    {
        public abstract ObjectiveStatus StatusIn(Encounter encounter);

        /// <summary>
        /// <paramref name="Who"/> mustn't get away. Fails the moment they leave the board — beyond
        /// Long of every character; met once the fight is won with them down or given up.
        /// </summary>
        public sealed record DontLetEscape(string Name, Combatant Who) : Objective(Name)
        {
            public override ObjectiveStatus StatusIn(Encounter encounter) =>
                Who.HasFled ? ObjectiveStatus.Failed
                : encounter.IsOver && encounter.Phase == EncounterPhase.Victory ? ObjectiveStatus.Achieved
                : encounter.IsOver ? ObjectiveStatus.Failed
                : ObjectiveStatus.InProgress;
        }

        /// <summary>None of the GM's creatures may leave the board.</summary>
        public sealed record NoneEscape(string Name) : Objective(Name)
        {
            public override ObjectiveStatus StatusIn(Encounter encounter) =>
                encounter.Combatants.Any(c => c.Side == Side.Adversaries && c.HasFled) ? ObjectiveStatus.Failed
                : encounter.IsOver && encounter.Phase == EncounterPhase.Victory ? ObjectiveStatus.Achieved
                : encounter.IsOver ? ObjectiveStatus.Failed
                : ObjectiveStatus.InProgress;
        }
    }
}
