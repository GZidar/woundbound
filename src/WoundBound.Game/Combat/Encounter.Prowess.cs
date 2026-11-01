using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// A Martial manoeuvre declared with an attack (Core Rules §9, "Manoeuvres"): Cleave sweeping on
    /// into <see cref="Second"/>; Hook drawing the target along <see cref="Direction"/> — towards the
    /// attacker, where none is given.
    /// </summary>
    public sealed record Manoeuvre(WeaponManoeuvre Kind, Combatant? Second = null, (int X, int Y)? Direction = null);

    /// <summary>
    /// The two Principles' signatures in a fight: a Nimble character's footwork and Exploits — paid in
    /// position — and a Martial character's manoeuvres — paid in a roll (Core Rules §3, §9).
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>Whose next attack is Precise, between its being spent and its damage being rolled.</summary>
        private Combatant? preciseOn;

        /// <summary>Whether <paramref name="mover"/>'s free step is still theirs: Nimble, once a round, and not while Slowed.</summary>
        private static bool FreeStepFor(Combatant mover) =>
            mover.IsNimble && !mover.HasUsedFreeStep && !mover.Conditions.Contains(CharacterCondition.Slowed);

        /// <summary>
        /// Why <paramref name="character"/> can't buy <paramref name="exploit"/> now, or null if they can:
        /// Nimble, a tactical spend in hand, the Exploit's condition met, and not already held (Core Rules §9, "Exploits").
        /// </summary>
        public static string? ExploitRefusal(Combatant character, NimbleExploit exploit)
        {
            if (!character.IsNimble)
            {
                return "Only a Nimble character can use an Exploit.";
            }

            if (TacticalSpendRefusal(character) is { } why)
            {
                return why;
            }

            if (exploit switch
                {
                    NimbleExploit.Slip => character.IsSlipping,
                    NimbleExploit.Precise => character.HoldsPrecise,
                    _ => character.HoldsPatient,
                })
            {
                return $"Already holding {exploit}.";
            }

            return ExploitCalculator.ConditionMet(exploit, character.HasMovedSinceLastAttack, character.HasBeenAttackedSinceTurnStart)
                ? null
                : exploit == NimbleExploit.Precise
                    ? "Precise needs a move since your last attack."
                    : "Patient needs nobody to have attacked you since your turn began.";
        }

        /// <summary>
        /// Buys <paramref name="exploit"/> for the active character: 1 Stamina, a tactical spend that
        /// hands the GM no Opportunity — "the position is the price" (Core Rules §9, "Exploits").
        /// </summary>
        public bool TryExploit(NimbleExploit exploit)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } character || ExploitRefusal(character, exploit) is not null)
            {
                return false;
            }

            PayTacticalSpend(character, CombatActionCatalog.ReactionStaminaCost);
            switch (exploit)
            {
                case NimbleExploit.Slip:
                    character.IsSlipping = true;
                    break;
                case NimbleExploit.Precise:
                    character.HoldsPrecise = true;
                    break;
                default:
                    character.HoldsPatient = true;
                    break;
            }

            Write($"{character.Name} buys {exploit} (1 Stamina, {character.Stamina} left) — " + exploit switch
            {
                NimbleExploit.Slip => "no blade on the way out this round.",
                NimbleExploit.Precise => "the next attack finds the gap: Direct, and Vitality on its damage.",
                _ => "the next attack comes with Advantage.",
            });
            return true;
        }

        /// <summary>The manoeuvres the game knows how to resolve.</summary>
        private static readonly WeaponManoeuvre[] Resolved = [WeaponManoeuvre.Cleave, WeaponManoeuvre.Topple, WeaponManoeuvre.Ring, WeaponManoeuvre.Hook];

        /// <summary>The manoeuvres <paramref name="attacker"/>'s weapon in hand permits: Martial, a melee weapon, by its Edge, Grip, Reach and weight.</summary>
        public static IReadOnlyList<WeaponManoeuvre> ManoeuvresOf(Combatant attacker) =>
            attacker.IsMartial && attacker.InHandWeapon is { IsRanged: false } weapon
                ? WeaponCalculator.ManoeuvresFor(weapon)
                : [];

        /// <summary>
        /// Why <paramref name="attacker"/> can't declare <paramref name="manoeuvre"/> with an attack at
        /// <paramref name="target"/> now, or null if they can (Core Rules §9, "Manoeuvres").
        /// </summary>
        public static string? ManoeuvreRefusal(Combatant attacker, Combatant target, Manoeuvre manoeuvre)
        {
            if (!attacker.IsMartial)
            {
                return "Only a Martial character can attempt a manoeuvre.";
            }

            if (!ManoeuvresOf(attacker).Contains(manoeuvre.Kind))
            {
                return $"The {attacker.InHandWeapon?.Name ?? "weapon in hand"} doesn't permit {manoeuvre.Kind}.";
            }

            if (!Resolved.Contains(manoeuvre.Kind))
            {
                return $"{manoeuvre.Kind} isn't in the game yet.";
            }

            if (attacker.Attack.IsRanged)
            {
                return "Manoeuvres are melee only.";
            }

            if (manoeuvre.Kind == WeaponManoeuvre.Cleave
                && (manoeuvre.Second is not { } second || second == target || second.Side == attacker.Side || second.IsOutOfFight || !attacker.CanReach(second.Position)))
            {
                return "Cleave needs a second enemy within your Reach.";
            }

            if ((manoeuvre.Kind == WeaponManoeuvre.Topple || manoeuvre.Kind == WeaponManoeuvre.Hook) && target.IsSwarm)
            {
                return "A Swarm has no footing to take.";
            }

            return TacticalSpendRefusal(attacker);
        }

        /// <summary>
        /// The manoeuvre, once the blow is resolved: the same total against the manoeuvre's Threshold —
        /// which the target may raise by 2 for Stamina after seeing it — and only on a hit (Core Rules §9).
        /// Cleave compares it to the second target's Dodge Threshold and lands the same damage roll there.
        /// </summary>
        private void ApplyManoeuvre(Combatant attacker, Combatant target, Manoeuvre manoeuvre, AttackOutcome outcome, Action then)
        {
            var roll = outcome.Roll;
            if (manoeuvre.Kind == WeaponManoeuvre.Cleave)
            {
                Cleave(attacker, manoeuvre.Second!, outcome, then);
                return;
            }

            if (roll.Success != true || target.IsOutOfFight)
            {
                Write($"The {manoeuvre.Kind} comes to nothing — a blow that never lands moves nobody.");
                then();
                return;
            }

            var (threshold, label, kind) = manoeuvre.Kind == WeaponManoeuvre.Ring
                ? (target.ResolveThreshold ?? 0, "RT", ReactionKind.BraceResolve)
                : (target.StabilityThreshold, "ST", ReactionKind.StandFirm);

            void Resolve(bool braced)
            {
                var total = roll.Total;
                var against = threshold + (braced ? ThresholdModifierCalculator.ReactionBonus : 0);
                if (!roll.IsAbsoluteSuccess && total < against)
                {
                    Write($"{manoeuvre.Kind}: {total} against {target.Name}'s {label} {against}{(braced ? " (+2)" : string.Empty)} — it doesn't take.");
                    then();
                    return;
                }

                switch (manoeuvre.Kind)
                {
                    case WeaponManoeuvre.Topple:
                        target.SetCondition(CharacterCondition.Prone, true);
                        Write($"{manoeuvre.Kind}: {total} against {label} {against} — {target.Name} goes down, Prone.");
                        then();
                        return;
                    case WeaponManoeuvre.Ring:
                        target.IsRinged = true;
                        Write($"{manoeuvre.Kind}: {total} against {label} {against} — {target.Name} is Ringed: no tactical spend until their next turn.");
                        then();
                        return;
                    default:
                        var step = manoeuvre.Direction ?? (Math.Sign(attacker.Position.X - target.Position.X), Math.Sign(attacker.Position.Y - target.Position.Y));
                        Write($"{manoeuvre.Kind}: {total} against {label} {against} — {target.Name} is hooked and hauled.");
                        ForcedMove(target, step, Math.Max(1, attacker.Reach), $"{attacker.Name}'s hook", attacker, meetHazards: true, then);
                        return;
                }
            }

            if (target.ResolveThreshold is null && manoeuvre.Kind == WeaponManoeuvre.Ring)
            {
                Write($"{target.Name} has no mind to Ring.");
                then();
                return;
            }

            if (!target.IsOutOfFight && BoostWouldTurn(roll, threshold))
            {
                OfferReaction(new PendingReaction(kind, target, attacker, roll, threshold,
                    $"{attacker.Name}'s {manoeuvre.Kind} rolls {roll.Total} against {target.Name}'s {label} {threshold}. 1 Stamina for +2 would stop it — the blow still lands."), Resolve);
                return;
            }

            Resolve(false);
        }

        /// <summary>
        /// Cleave: the same total against the second target's Dodge Threshold — which they may raise by
        /// 2 — and on a hit the same damage roll, their armour absorbing as usual (Core Rules §9).
        /// </summary>
        private void Cleave(Combatant attacker, Combatant second, AttackOutcome outcome, Action then)
        {
            var roll = outcome.Roll;
            if (roll.Success != true || outcome.RolledDamage <= 0 || second.IsOutOfFight)
            {
                Write(roll.Success == true ? "The Cleave has nobody left to sweep into." : "The Cleave comes to nothing — the first blow never landed.");
                then();
                return;
            }

            var threshold = second.DodgeThreshold + VisibilityDodgeModifier(second);
            second.HasBeenAttackedSinceTurnStart = true;

            void Resolve(bool dodged)
            {
                var against = threshold + (dodged ? ThresholdModifierCalculator.ReactionBonus : 0);
                if (!roll.IsAbsoluteSuccess && roll.Total < against)
                {
                    Write($"Cleave: {roll.Total} against {second.Name}'s DT {against}{(dodged ? " (+2 panic dodge)" : string.Empty)} — the sweep misses.");
                    then();
                    return;
                }

                var absorbed = Math.Min(outcome.RolledDamage, InventoryCalculator.ArmourAbsorbedAgainst(second.Armour, WoundType.Body, isDirect: false));
                Write($"Cleave: {roll.Total} against {second.Name}'s DT {against} — the sweep carries into {second.Name}: {outcome.RolledDamage} damage" +
                      (absorbed > 0 ? $", armour absorbs {absorbed}." : "."));
                Shelter(second, outcome.RolledDamage - absorbed, WoundType.Body, left =>
                {
                    TakeBodyWounds(second, left);
                    Write(second.IsOutOfFight ? $"{second.Name} is out of the fight." : $"{second.Name} has {second.Body}/{second.BodyMax} Body.");
                    then();
                });
            }

            if (BoostWouldTurn(roll, threshold))
            {
                OfferReaction(new PendingReaction(ReactionKind.PanicDodge, second, attacker, roll, threshold,
                    $"{attacker.Name}'s Cleave sweeps on at {second.Name}: {roll.Total} against DT {threshold}. A panic dodge (1 Stamina, +2) would make it miss."),
                    Resolve);
                return;
            }

            Resolve(false);
        }
    }
}
