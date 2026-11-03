using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Rampage (GM Reference §11, the Duskfang Gnoll): "once per round, when its attack drops a
    /// creature to 0 Body, it may spend 1 Stamina on an extra action that is a second attack, against
    /// a different creature - using its move to close first, if it has not yet taken one." The one
    /// Stamina-bought attack On Stamina (GM Reference §9) allows a statblock to name; it counts
    /// against the creature's tactical spends like any other.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>A step the engine takes next in place of asking the brain — the attack a Rampage bought.</summary>
        private GmStep? queuedStep;

        /// <summary>The creature whose attack under way is the one its Rampage bought.</summary>
        private Combatant? rampageAttackFor;

        /// <summary>After <paramref name="attacker"/>'s own attack on its turn: if it just dropped <paramref name="target"/>, its Rampage opens.</summary>
        private void OpenRampage(Combatant attacker, Combatant target, bool wasUp)
        {
            if (!attacker.HasRampage || attacker.HasRampagedThisRound || !wasUp || target.Body > 0 || attacker.IsOutOfFight
                || TacticalSpendRefusal(attacker) is not null)
            {
                return;
            }

            attacker.RampageFrom = target;
            Write($"{attacker.Name} has dropped {target.Name} — Rampage: 1 Stamina would buy it a second attack this turn, at somebody else.");
        }

        /// <summary>
        /// Why <paramref name="actor"/> can't Rampage at <paramref name="target"/> now, or null if it
        /// can: its attack this turn dropped somebody, not yet this round, a different creature, a
        /// tactical spend in hand, and the attack there to be made from where it stands.
        /// </summary>
        public string? RampageRefusal(Combatant actor, Combatant target, AttackProfile? with = null)
        {
            if (!actor.HasRampage)
            {
                return $"{actor.Name} has no Rampage.";
            }

            if (actor.HasRampagedThisRound)
            {
                return "Rampage is once a round.";
            }

            if (actor.RampageFrom is not { } dropped)
            {
                return "Rampage needs its attack this turn to have dropped a creature.";
            }

            if (target == dropped || target == actor)
            {
                return "The second attack goes against a different creature.";
            }

            if (TacticalSpendRefusal(actor) is { } spent)
            {
                return spent;
            }

            if (actor.NeedsFooting || (target.IsOutOfFight && !target.IsDefenceless) || target.IsPhantom)
            {
                return $"{actor.Name} can't attack {target.Name} now.";
            }

            var plan = with is null ? PlanAttack(actor, target) : PlanAttackWith(actor, with, actor.Position, target);
            return plan.CanAttack ? null : plan.Refusal;
        }
    }
}
