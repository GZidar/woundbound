namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// The simple GM, plus one idea: a creature that isn't a Minion, with a character in reach who
    /// is still on their feet, trips them first when another of its side could strike them this
    /// round — handing the pack the −2 DT and the Disadvantage on the character's swings. It picks
    /// whoever in reach is easiest to fell, by Stability Threshold. A second baseline, so the
    /// simulator can say whether controlling beats hitting.
    /// </summary>
    public class TrippingGmBrain : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn)
        {
            var actor = turn.Actor;
            if (!turn.HasAttacked && !actor.IsMinion && !actor.IsProne && actor.ActionsRemaining > 0 && TripTarget(view, actor) is { } target)
            {
                var advantage = turn.BonusAvailable && view.BankedOpportunities >= view.MaxBankedOpportunities;
                return new GmStep.Control(ControlAction.Trip, target, advantage);
            }

            return base.NextStep(view, turn);
        }

        private static Combatant? TripTarget(BattleView view, Combatant actor) =>
            view.EnemiesOf(actor)
                .Where(c => BattleView.ControlRefusal(actor, ControlAction.Trip, c) is null)
                .Where(c => view.Adversaries.Any(a => a != actor && !a.IsProne
                    && (a.CanReach(c.Position) || view.StrikeSquare(a, c) is not null)))
                .OrderBy(c => c.StabilityThreshold)
                .FirstOrDefault();
    }
}
