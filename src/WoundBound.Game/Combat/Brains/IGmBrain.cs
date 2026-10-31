namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// The GM's side of the table: every choice the rules leave to the GM, and nothing else.
    ///
    /// <para>The <see cref="Encounter"/> owns the rules. It asks the brain what to do, checks that
    /// the answer is legal, and carries it out — so a brain can be as clever or as foolish as it
    /// likes without ever breaking a rule. An illegal answer is a bug in the brain, and throws.
    /// Brains see the fight only through <see cref="BattleView"/>, which can look but not touch.</para>
    ///
    /// <para>A turn is asked for one step at a time (<see cref="NextStep"/>), not planned whole, so
    /// a brain can change its mind when a blow is dodged, Riposted or drops its target.</para>
    /// </summary>
    public interface IGmBrain
    {
        /// <summary>
        /// Which adversary takes the turn the GM is owed now (Core Rules §9, "Where adversaries
        /// outnumber the party the GM chooses which of them act"). Must be one still fighting.
        /// </summary>
        Combatant ChooseOwedTurn(BattleView view);

        /// <summary>
        /// Whether to spend an Opportunity on an extra adversary turn here, and for whom; null to
        /// keep it. Only asked while the GM holds at least one. An Immediate Opportunity is always
        /// spent before a banked one, since it lapses.
        /// </summary>
        Combatant? ChooseExtraTurn(BattleView view);

        /// <summary>What <paramref name="turn"/>'s adversary does next. Asked until it answers <see cref="GmStep.End"/>.</summary>
        GmStep NextStep(BattleView view, GmTurn turn);

        /// <summary>
        /// Whether <paramref name="adversary"/> spends a reaction (1 Stamina) to strike at
        /// <paramref name="leaver"/>, who is leaving its reach. Only asked when it could.
        /// </summary>
        bool StrikesAtLeaver(BattleView view, Combatant adversary, Combatant leaver);

        /// <summary>
        /// Whether the adversary <see cref="PendingReaction.Defender"/> spends 1 Stamina on a panic
        /// dodge. Only asked when the dodge would turn the hit and the spend is allowed.
        /// </summary>
        bool PanicDodges(BattleView view, PendingReaction reaction);

        /// <summary>
        /// At the round's end, the next adversary that took no turn to close in, and where to
        /// (Core Rules §9, "Adversaries that didn't act close in"): a single move's worth. Null when
        /// no more are moving. <paramref name="stillIdle"/> shrinks as each one goes.
        /// </summary>
        GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle);
    }

    /// <summary>One thing an adversary does on its turn.</summary>
    public abstract record GmStep
    {
        private GmStep()
        {
        }

        /// <summary>Move to <paramref name="Square"/>, paid for with the move and then actions.</summary>
        public sealed record Move(GridPosition Square) : GmStep;

        /// <summary>
        /// Attack <paramref name="Target"/> with the action. <paramref name="WithAdvantage"/> spends
        /// an Opportunity on the attack's bonus — allowed only when <see cref="GmTurn.BonusAvailable"/>.
        /// </summary>
        public sealed record Attack(Combatant Target, bool WithAdvantage = false) : GmStep;

        /// <summary>
        /// Take the Dodge, Grounded or Resolute action (Core Rules §10, "Standing Your Ground"):
        /// +2 to that Threshold until the creature's next turn, paid for like a move, at the price
        /// of no more moving and Disadvantage on its attacks until then.
        /// </summary>
        public sealed record StandGround(Rules.Enums.CombatAction Action) : GmStep;

        /// <summary>Done for this turn, whatever is left unspent.</summary>
        public sealed record EndTurn : GmStep;

        public static GmStep End { get; } = new EndTurn();
    }

    /// <summary>A round-end move for an adversary that took no turn.</summary>
    public sealed record GmCloseIn(Combatant Adversary, GridPosition Square);

    /// <summary>The adversary turn in progress, as the brain choosing its steps sees it.</summary>
    public sealed class GmTurn
    {
        internal GmTurn(Combatant actor, bool isExtraTurn, bool bonusAvailable)
        {
            Actor = actor;
            IsExtraTurn = isExtraTurn;
            BonusAvailable = bonusAvailable;
        }

        public Combatant Actor { get; }

        /// <summary>Bought with an Opportunity, beyond the round's cap.</summary>
        public bool IsExtraTurn { get; }

        /// <summary>
        /// Whether an attack this turn may spend an Opportunity on Advantage: the GM holds one, and
        /// the adversary has yet to act this round (Core Rules §9, "GM Opportunities").
        /// </summary>
        public bool BonusAvailable { get; internal set; }

        public bool HasMoved { get; internal set; }

        public bool HasAttacked { get; internal set; }

        /// <summary>Has taken Dodge, Grounded or Resolute this turn.</summary>
        public bool HasStoodGround { get; internal set; }

        /// <summary>Steps taken so far this turn.</summary>
        public int Steps { get; internal set; }
    }
}
