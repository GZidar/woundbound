namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Two Weapons (Core Rules §9): a character with a Light weapon in the off hand may, when they
    /// take the Attack action, spend 1 Stamina to strike with it as well — rolled separately, with
    /// its own Grip's modifier. The Stamina is levied by the equipment: no tactical spend, and no
    /// Opportunity for the GM. Either weapon may carry a manoeuvre it permits, at the usual cost.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>The Stamina the off-hand strike costs, levied by the equipment.</summary>
        public const int OffHandStaminaCost = 1;

        /// <summary>
        /// Why <paramref name="attacker"/> can't make their off-hand strike at <paramref name="target"/>
        /// now, or null if they can. Leave the target out to ask only whether there's a strike to make.
        /// </summary>
        public string? OffHandRefusal(Combatant attacker, Combatant? target = null)
        {
            if (attacker.OffHandAttack is not { } offHand)
            {
                return "Only a Two Weapons loadout — a Light weapon in the off hand — strikes twice.";
            }

            if (!attacker.OffHandOwed)
            {
                return $"The {offHand.Name} strikes as part of an Attack action, straight after it.";
            }

            if (attacker.IsOutOfFight || attacker.NeedsFooting)
            {
                return $"{attacker.Name} is in no state to.";
            }

            if (attacker.Stamina < OffHandStaminaCost)
            {
                return $"The {offHand.Name} costs 1 Stamina, and there's none left.";
            }

            if (target is null)
            {
                return null;
            }

            if (target == attacker || (target.IsOutOfFight && !target.IsDefenceless))
            {
                return "Not a target.";
            }

            return PlanAttackWith(attacker, offHand, attacker.Position, target).Refusal;
        }

        /// <summary>The off-hand weapon's attack at <paramref name="target"/> from where the attacker stands, as <see cref="PlanAttack"/> plans the main one.</summary>
        public AttackPlan PlanOffHandAttack(Combatant attacker, Combatant target) =>
            attacker.OffHandAttack is { } offHand
                ? PlanAttackWith(attacker, offHand, attacker.Position, target)
                : new AttackPlan("No off-hand weapon.", 0, 0, null, "No off-hand weapon.", attacker.Attack);

        /// <summary>
        /// Strikes <paramref name="target"/> with the off-hand weapon, after the Attack action that
        /// allowed it: 1 Stamina from the equipment, no action, and its own roll.
        /// </summary>
        /// <param name="manoeuvre">One the off-hand weapon permits, paid for as usual (Core Rules §9, "Two Weapons").</param>
        public AttackOutcome? TryOffHandAttack(Combatant target, Manoeuvre? manoeuvre = null)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } attacker || OffHandRefusal(attacker, target) is not null
                || (manoeuvre is { } m && ManoeuvreRefusal(attacker, target, m, offHand: true) is not null))
            {
                return null;
            }

            var offHand = attacker.OffHandAttack!;
            attacker.OffHandOwed = false;
            attacker.Stamina -= OffHandStaminaCost;
            Write($"{attacker.Name} strikes with the {offHand.Name} in the off hand too (1 Stamina from the equipment, {attacker.Stamina} left — no Opportunity).");

            AttackOutcome? result = null;
            ResolveAttack(attacker, target, asReaction: false, opportunityBonus: false, outcome =>
            {
                result = outcome;
                CheckForEnd();
            }, with: offHand, manoeuvre: manoeuvre, spendsAction: false);

            return result;
        }
    }
}
