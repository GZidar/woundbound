namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// Why a GM creature chose what it did, in a few words the table may hear: what a GM narrating
    /// the fight would say (the user, 2026-11-08). Only what the creatures could have seen goes in:
    /// a caster they have watched cast, a character they can see bleeding, the one who stands nearest.
    /// Never what the GM read off a sheet, such as a Threshold or a caster who hasn't cast yet. That
    /// stays secret (the user's ruling, 2026-11-08).
    /// </summary>
    public static class TableTalk
    {
        /// <summary>Why <paramref name="actor"/> chose <paramref name="choice"/>, or null where there's nothing worth saying.</summary>
        public static string? Why(BattleView view, Combatant actor, Option choice)
        {
            if (choice.Target is not { } target || target.Side == actor.Side)
            {
                return choice.Kind == OptionKind.Cast && choice.Cast is not null ? AreaWhy(view, actor) : null;
            }

            if (target.IsDefenceless)
            {
                return "where they lie";
            }

            if (target.ShowsAsCaster)
            {
                return "the caster";
            }

            if (target.BodyMax > 0 && target.Body * 2 <= target.BodyMax)
            {
                return "already bloodied";
            }

            if (actor.HasPackTactics && view.Adversaries.Any(a => a != actor && a.Kind == actor.Kind && a.CanReach(target.Position)))
            {
                return "with its packmate beside them";
            }

            var nearest = view.EnemiesOf(actor).Where(e => !e.IsOutOfFight).MinBy(e => actor.DistanceTo(e.Position));
            if (nearest == target)
            {
                return "the nearest";
            }

            return view.Brief?.Goal switch
            {
                AdversaryGoal.Hold hold when hold.Zone.Squares.Contains(actor.Position) => $"holding {hold.Zone.Name}",
                AdversaryGoal.Take take when take.Prize.Carrier is { } carrier && carrier != actor && target.DistanceTo(carrier.Position) <= 2 => $"covering the {take.Prize.Name}",
                _ => null,
            };
        }

        /// <summary>A spell with no one target: aimed to catch several, where several stand close together.</summary>
        private static string? AreaWhy(BattleView view, Combatant actor) =>
            view.EnemiesOf(actor).Count(e => !e.IsOutOfFight) >= 2 ? "to catch as many as it can" : null;
    }
}
