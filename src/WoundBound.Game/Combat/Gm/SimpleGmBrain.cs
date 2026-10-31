namespace WoundBound.Game.Combat.Gm
{
    /// <summary>
    /// The GM played simply: every creature goes for the nearest character. Whoever can already
    /// strike, or else whoever is nearest, takes the turn; it steps into reach with the move and
    /// keeps the action for the blow when it can, and spends both on closing the distance when it
    /// can't. A placeholder for a GM that thinks — and a baseline to measure one against.
    /// </summary>
    public class SimpleGmBrain : IGmBrain
    {
        public virtual Combatant ChooseOwedTurn(GmView view)
        {
            var targets = view.Characters.ToList();
            return view.Adversaries
                .OrderBy(a => CanStrikeThisTurn(view, a) ? 0 : 1)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .First();
        }

        /// <summary>
        /// GM Reference §7: spend on overextension, not on openings — so only on an adversary that
        /// can strike a character on that turn, preferring one that has already acted, since
        /// chaining two turns back to back is the strongest use of one.
        /// </summary>
        public virtual Combatant? ChooseExtraTurn(GmView view)
        {
            var targets = view.Characters.ToList();
            return view.Adversaries
                .Where(a => CanStrikeThisTurn(view, a))
                .OrderBy(a => a.HasActedThisRound ? 0 : 1)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .FirstOrDefault();
        }

        public virtual GmStep NextStep(GmView view, GmTurn turn)
        {
            var actor = turn.Actor;
            if (NearestTarget(view, actor) is not { } target)
            {
                return GmStep.End;
            }

            if (!turn.HasAttacked && view.CanAttack(actor, target))
            {
                // An extra turn is usually worth more than Advantage, so the bonus only gets an
                // Opportunity when the bank is full and the next one would be wasted.
                var advantage = turn.BonusAvailable && view.BankedOpportunities >= view.MaxBankedOpportunities;
                return new GmStep.Attack(target, advantage);
            }

            if (turn.HasAttacked || turn.HasMoved || actor.CanReach(target.Position))
            {
                return GmStep.End;
            }

            if (StrikeSquare(view, actor, target) is { } strikeFrom)
            {
                return new GmStep.Move(strikeFrom);
            }

            var closest = view.SquaresWithin(actor, view.MovesAvailable(actor))
                .OrderBy(o => o.Key.DistanceTo(target.Position))
                .ThenBy(o => o.Value)
                .Select(o => (GridPosition?)o.Key)
                .FirstOrDefault();

            return closest is { } square ? new GmStep.Move(square) : GmStep.End;
        }

        /// <summary>A Minion dropped by any blow that lands has every reason to turn whatever it can.</summary>
        public virtual bool StrikesAtLeaver(GmView view, Combatant adversary, Combatant leaver) => true;

        public virtual bool PanicDodges(GmView view, PendingReaction reaction) => true;

        /// <summary>Nearest first, each a single move toward the nearest character; any already in reach stay put.</summary>
        public virtual GmCloseIn? NextCloseIn(GmView view, IReadOnlyList<Combatant> stillIdle)
        {
            foreach (var adversary in stillIdle.OrderBy(a => NearestTarget(view, a) is { } t ? t.Position.DistanceTo(a.Position) : int.MaxValue))
            {
                if (NearestTarget(view, adversary) is not { } target || adversary.CanReach(target.Position))
                {
                    continue;
                }

                var destination = StrikeSquare(view, adversary, target)
                    ?? view.SquaresWithin(adversary, 1).Keys
                        .Where(square => square.DistanceTo(target.Position) < adversary.Position.DistanceTo(target.Position))
                        .OrderBy(square => square.DistanceTo(target.Position))
                        .ThenBy(square => square.DistanceTo(adversary.Position))
                        .Select(square => (GridPosition?)square)
                        .FirstOrDefault();

                if (destination is { } square)
                {
                    return new GmCloseIn(adversary, square);
                }
            }

            return null;
        }

        protected static Combatant? NearestTarget(GmView view, Combatant actor) =>
            view.Characters.MinBy(t => actor.Position.DistanceTo(t.Position));

        /// <summary>Whether <paramref name="adversary"/> could hit a character on a fresh turn: from where it stands, or after a single move.</summary>
        protected static bool CanStrikeThisTurn(GmView view, Combatant adversary) =>
            NearestTarget(view, adversary) is { } target
            && (adversary.CanReach(target.Position) || StrikeSquare(view, adversary, target) is not null);

        /// <summary>The nearest square one move away from which <paramref name="actor"/> can strike <paramref name="target"/>, leaving the action for the blow.</summary>
        protected static GridPosition? StrikeSquare(GmView view, Combatant actor, Combatant target)
        {
            var origin = actor.Position;
            return view.SquaresWithin(actor, 1).Keys
                .Where(square => actor.CanReachFrom(square, target.Position))
                .OrderBy(square => square.DistanceTo(origin))
                .Select(square => (GridPosition?)square)
                .FirstOrDefault();
        }
    }
}
