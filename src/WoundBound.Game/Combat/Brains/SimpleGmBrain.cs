namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// The GM played simply: every creature goes for the nearest character. Whoever can already
    /// strike, or else whoever is nearest, takes the turn; it steps into reach with the move and
    /// keeps the action for the blow when it can, and spends both on closing the distance when it
    /// can't. A creature that has broken runs, putting as much ground between it and the party
    /// as it can, until no character has it within Long and it's off the board.
    /// A placeholder for a GM that thinks — and a baseline to measure one against.
    /// </summary>
    public class SimpleGmBrain : IGmBrain
    {
        public virtual Combatant ChooseOwedTurn(BattleView view)
        {
            var targets = view.Characters.ToList();
            return view.Adversaries
                .OrderBy(a => a.IsBroken ? 1 : CanStrikeThisTurn(view, a) ? 0 : 2)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .First();
        }

        /// <summary>
        /// GM Reference §7: spend on overextension, not on openings — so only on an adversary that
        /// can strike a character on that turn, preferring one that has already acted, since
        /// chaining two turns back to back is the strongest use of one.
        /// </summary>
        public virtual Combatant? ChooseExtraTurn(BattleView view)
        {
            var targets = view.Characters.ToList();
            return view.Adversaries
                .Where(a => !a.IsBroken && CanStrikeThisTurn(view, a))
                .OrderBy(a => a.HasActedThisRound ? 0 : 1)
                .ThenBy(a => targets.Min(t => a.Position.DistanceTo(t.Position)))
                .FirstOrDefault();
        }

        public virtual GmStep NextStep(BattleView view, GmTurn turn)
        {
            var actor = turn.Actor;
            if (NearestTarget(view, actor) is not { } target)
            {
                return GmStep.End;
            }

            if (actor.IsBroken)
            {
                return Run(view, turn);
            }

            // Off its footing, the action goes on getting it back.
            if (actor.NeedsFooting && BattleView.RegainFootingRefusal(actor) is null)
            {
                return new GmStep.RegainFooting();
            }

            // On its feet before anything else: a Prone creature is Off-guard and crawls.
            if (actor.IsProne && BattleView.StandUpRefusal(actor) is null)
            {
                return new GmStep.StandUp();
            }

            // Held, with nobody to hit: spend the action getting loose.
            if (actor.HeldBy is not null && !turn.HasAttacked && !view.EnemiesOf(actor).Any(e => view.CanAttack(actor, e))
                && BattleView.BreakFreeRefusal(actor) is null)
            {
                return new GmStep.BreakFree();
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

        /// <summary>
        /// A broken creature's turn: loose from any hold, up off the floor, and away from the party
        /// as far as what it has left will carry it.
        /// </summary>
        protected static GmStep Run(BattleView view, GmTurn turn)
        {
            var actor = turn.Actor;
            if (actor.HeldBy is not null)
            {
                return BattleView.BreakFreeRefusal(actor) is null ? new GmStep.BreakFree() : GmStep.End;
            }

            if (actor.IsProne && BattleView.StandUpRefusal(actor) is null)
            {
                return new GmStep.StandUp();
            }

            return AwayFromParty(view, actor, view.MovesAvailable(actor)) is { } square ? new GmStep.Move(square) : GmStep.End;
        }

        /// <summary>
        /// The square within <paramref name="moves"/> furthest from the nearest character — out of
        /// everyone's Long, if it can get there — the cheaper where it's no further; null if none is
        /// further than where it stands.
        /// </summary>
        protected static GridPosition? AwayFromParty(BattleView view, Combatant actor, int moves)
        {
            // The map's edges are open, so straight away from the nearest of them is always a way out.
            double Score(GridPosition square) =>
                (view.IsWithinLongOfParty(square) ? 0 : 100) + view.DistanceToNearestCharacter(square);

            var here = Score(actor.Position);
            return view.SquaresWithin(actor, moves)
                .Where(o => Score(o.Key) > here)
                .OrderByDescending(o => Score(o.Key))
                .ThenBy(o => o.Value)
                .Select(o => (GridPosition?)o.Key)
                .FirstOrDefault();
        }

        /// <summary>A Minion dropped by any blow that lands has every reason to turn whatever it can — unless it's running.</summary>
        public virtual bool StrikesAtLeaver(BattleView view, Combatant adversary, Combatant leaver) => !adversary.IsBroken;

        public virtual bool PanicDodges(BattleView view, PendingReaction reaction) => true;

        public virtual bool StandsFirm(BattleView view, PendingReaction reaction) => true;

        /// <summary>Only when the shove would hurt: a square of ground isn't worth the Stamina otherwise.</summary>
        public virtual bool ArrestsShove(BattleView view, PendingReaction reaction) => reaction.ImpactWounds > 0;

        /// <summary>Nearest first, each a single move toward the nearest character; any already in reach stay put.</summary>
        public virtual GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle)
        {
            if (stillIdle.FirstOrDefault(a => a.IsProne) is { } fallen)
            {
                return new GmCloseIn(fallen, fallen.Position, StandUp: true);
            }

            // The broken spend the move getting away.
            foreach (var runner in stillIdle.Where(a => a.IsBroken && Encounter.CanMoveItself(a)))
            {
                if (AwayFromParty(view, runner, 1) is { } square)
                {
                    return new GmCloseIn(runner, square);
                }
            }

            foreach (var adversary in stillIdle.OrderBy(a => NearestTarget(view, a) is { } t ? t.Position.DistanceTo(a.Position) : int.MaxValue))
            {
                if (adversary.IsBroken || !Encounter.CanMoveItself(adversary) || NearestTarget(view, adversary) is not { } target || adversary.CanReach(target.Position))
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

        protected static Combatant? NearestTarget(BattleView view, Combatant actor) => view.NearestEnemy(actor);

        /// <summary>Whether <paramref name="adversary"/> could hit a character on a fresh turn: from where it stands, or after a single move.</summary>
        protected static bool CanStrikeThisTurn(BattleView view, Combatant adversary) =>
            NearestTarget(view, adversary) is { } target
            && (adversary.CanReach(target.Position) || StrikeSquare(view, adversary, target) is not null);

        /// <summary>The nearest square one move away from which <paramref name="actor"/> can strike <paramref name="target"/>, leaving the action for the blow.</summary>
        protected static GridPosition? StrikeSquare(BattleView view, Combatant actor, Combatant target) =>
            view.StrikeSquare(actor, target, moves: 1);
    }
}
