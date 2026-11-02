using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

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
            mover.HasFootwork && !mover.HasUsedFreeStep && !mover.Conditions.Contains(CharacterCondition.Slowed);

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

            BuyExploit(character, exploit);
            return true;
        }

        /// <summary>Pays for <paramref name="exploit"/> and holds it, for a character or a foe built with it.</summary>
        private void BuyExploit(Combatant character, NimbleExploit exploit)
        {
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
        }

        /// <summary>The manoeuvres the game knows how to resolve.</summary>
        private static readonly WeaponManoeuvre[] Resolved = Enum.GetValues<WeaponManoeuvre>();

        /// <summary>
        /// The manoeuvres <paramref name="attacker"/> may declare: a Martial character's weapon in hand
        /// permits them by its Edge, Grip, Reach and weight; a generic foe has whatever its Attack line
        /// names, while it still has a melee line to swing (GM Reference §2).
        /// </summary>
        /// <param name="offHand">For the off-hand weapon of a Two Weapons loadout, which offers its own set (Core Rules §9, "Two Weapons").</param>
        public static IReadOnlyList<WeaponManoeuvre> ManoeuvresOf(Combatant attacker, bool offHand = false) =>
            attacker.Build is null
                ? attacker.MeleeAttack.IsRanged || offHand ? [] : attacker.StatblockManoeuvres
                : attacker.IsMartial && (offHand ? attacker.OffHandWeapon : attacker.InHandWeapon) is { IsRanged: false } weapon
                    ? WeaponCalculator.ManoeuvresFor(weapon)
                    : [];

        /// <summary>
        /// Why <paramref name="attacker"/> can't declare <paramref name="manoeuvre"/> with an attack at
        /// <paramref name="target"/> now, or null if they can (Core Rules §9, "Manoeuvres").
        /// </summary>
        /// <param name="offHand">Made with the off-hand weapon of a Two Weapons loadout.</param>
        public static string? ManoeuvreRefusal(Combatant attacker, Combatant target, Manoeuvre manoeuvre, bool offHand = false)
        {
            if (attacker.Build is not null && !attacker.IsMartial)
            {
                return "Only a Martial character can attempt a manoeuvre.";
            }

            if (!ManoeuvresOf(attacker, offHand).Contains(manoeuvre.Kind))
            {
                return $"The {(offHand ? attacker.OffHandWeapon : attacker.InHandWeapon)?.Name ?? "weapon in hand"} doesn't permit {manoeuvre.Kind}.";
            }

            if (!Resolved.Contains(manoeuvre.Kind))
            {
                return $"{manoeuvre.Kind} isn't in the game yet.";
            }

            if ((offHand ? attacker.OffHandAttack ?? attacker.Attack : attacker.Attack).IsRanged)
            {
                return "Manoeuvres are melee only.";
            }

            if (manoeuvre.Kind == WeaponManoeuvre.Cleave
                && (manoeuvre.Second is not { } second || second == target || second.Side == attacker.Side || second.IsOutOfFight || !attacker.CanReach(second)))
            {
                return "Cleave needs a second enemy within your Reach.";
            }

            if ((manoeuvre.Kind == WeaponManoeuvre.Topple || manoeuvre.Kind == WeaponManoeuvre.Hook) && target.IsSwarm)
            {
                return "A Swarm has no footing to take.";
            }

            if (manoeuvre.Kind == WeaponManoeuvre.Disarm && !target.WieldsWeapon)
            {
                return $"{target.Name} has no weapon in hand to lose.";
            }

            if (manoeuvre.Kind == WeaponManoeuvre.Sunder && target.HasBeenSundered)
            {
                return $"{target.Name} has already been Sundered this combat.";
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

            var shieldBroke = false;
            if (roll.Success != true || target.IsOutOfFight)
            {
                // Sunder alone acts on a miss: where the shield raised over the target is all that
                // stopped the blow, the shield takes it instead (Core Rules §9, "The Manoeuvres").
                if (manoeuvre.Kind != WeaponManoeuvre.Sunder || target.IsOutOfFight || !ShieldAloneStopped(target, outcome))
                {
                    Write($"The {manoeuvre.Kind} comes to nothing — a blow that never lands moves nobody.");
                    then();
                    return;
                }

                shieldBroke = true;
            }

            var (threshold, label, kind) = manoeuvre.Kind == WeaponManoeuvre.Ring
                ? (target.ResolveThreshold ?? 0, "RT", ReactionKind.BraceResolve)
                : (target.StabilityThreshold, "ST", ReactionKind.StandFirm);

            // Unshakable: the check to put it down is made as though at Disadvantage — a third die
            // rolled, and the attacker keeps the lowest two.
            if (((manoeuvre.Kind == WeaponManoeuvre.Topple && target.IsUnshakable)
                 || (manoeuvre.Kind is WeaponManoeuvre.Topple or WeaponManoeuvre.Hook && target.IsSolo))
                && roll.CountedDice.Count == 2)
            {
                var dice = roll.CountedDice.Append(this.dice.Roll(Rules.Calculation.RollCalculator.HighestFace)).OrderBy(d => d).Take(2).ToList();
                roll = roll with { Total = roll.Modifier + dice.Sum(), CountedDice = dice };
                Write($"{target.Name} is {(target.IsSolo ? "a Solo, Resilient" : "Unshakable")}: the {manoeuvre.Kind} is checked at Disadvantage — {string.Join("+", dice)}{(roll.Modifier >= 0 ? "+" : "−")}{Math.Abs(roll.Modifier)} = {roll.Total}.");
            }

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
                    case WeaponManoeuvre.Sunder when shieldBroke:
                        Write($"Sunder: {total} against {label} {against} — only the shield stopped the blow, and the shield pays for it.");
                        BreakShield(target);
                        then();
                        return;
                    case WeaponManoeuvre.Sunder:
                        Sunder(target, total, label, against);
                        then();
                        return;
                    case WeaponManoeuvre.Disarm:
                        Write($"Disarm: {total} against {label} {against} — {Disarm(target)}");
                        then();
                        return;
                    case WeaponManoeuvre.Topple:
                        Write($"{manoeuvre.Kind}: {total} against {label} {against} — {target.Name} goes down, Prone.");
                        KnockDown(target, $"{attacker.Name}'s Topple");
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
        /// Whether the Sunder that missed <paramref name="target"/> would have hit with no shield over
        /// them: the total reaches the Dodge Threshold it was turned by, less the shield's own
        /// contribution and nothing else's (Core Rules §9, "The Manoeuvres").
        /// </summary>
        private static bool ShieldAloneStopped(Combatant target, AttackOutcome outcome)
        {
            var shield = target.ShieldBonus;
            return shield > 0 && outcome.Roll.Total >= outcome.Plan.DodgeThreshold + outcome.Boost - shield;
        }

        /// <summary>A Sunder that hit: the target's worn armour drops one type for the rest of the combat.</summary>
        private void Sunder(Combatant target, int total, string label, int against)
        {
            if (target.Armour == ArmourWeight.None)
            {
                Write($"Sunder: {total} against {label} {against} — but {target.Name} wears nothing to break.");
                return;
            }

            var was = target.Armour;
            target.Armour = was - 1;
            target.HasBeenSundered = true;
            Write($"Sunder: {total} against {label} {against} — {target.Name}'s {was} armour is battered down to " +
                  $"{(target.Armour == ArmourWeight.None ? "nothing" : target.Armour.ToString())} for the rest of the fight.");
        }

        /// <summary>
        /// The shield raised over <paramref name="target"/> — whoever bears it — broken one type,
        /// permanently: Heavy to Medium, Medium to Light, and a Light one split and dropped. The
        /// reduced bonus counts from now. It spends the target's one Sunder.
        /// </summary>
        private void BreakShield(Combatant target)
        {
            target.HasBeenSundered = true;
            var bearer = target.CoveredBy.MaxBy(c => InventoryCalculator.ShieldDodgeThresholdBonus(c.Weight)).Bearer;
            if (bearer.InHandShield is not { } shield)
            {
                return;
            }

            if (shield.Weight == ShieldWeight.Light)
            {
                bearer.Build!.Shields.Remove(shield);
                foreach (var covered in combatants)
                {
                    covered.CoveredBy.RemoveAll(c => c.Bearer == bearer);
                }

                Write($"{bearer.Name}'s {shield.Name} splits apart and is dropped — {target.Name} is DT {target.DodgeThreshold} now.");
                return;
            }

            shield.Weight--;
            foreach (var covered in combatants)
            {
                for (var i = 0; i < covered.CoveredBy.Count; i++)
                {
                    if (covered.CoveredBy[i].Bearer == bearer)
                    {
                        covered.CoveredBy[i] = (bearer, shield.Weight);
                    }
                }
            }

            Write($"{bearer.Name}'s {shield.Name} cracks, down to {shield.Weight} for good — {target.Name} is DT {target.DodgeThreshold} now.");
        }

        /// <summary>
        /// Knocks the weapon from <paramref name="target"/>'s hand to the ground at their feet: Disarmed,
        /// and so Off-guard, until they take it up — an action — or swap another into hand (Core Rules §9, "Conditions").
        /// </summary>
        private string Disarm(Combatant target)
        {
            target.SetCondition(CharacterCondition.Disarmed, true);
            target.DroppedAt = target.Position;
            if (target.Build is { } build && target.InHandWeapon is { } weapon)
            {
                build.Weapons.Remove(weapon);
                target.DroppedWeapon = weapon;
                return $"{target.Name}'s {weapon.Name} goes spinning to the ground at their feet. Disarmed, and Off-guard.";
            }

            var line = target.StatblockAttacks.FirstOrDefault(a => Combatant.IsWieldedLine(a.Name));
            target.DroppedWeapon = new WeaponDefinition { Name = line?.Name ?? "weapon" };
            return $"{target.Name}'s {target.DroppedWeapon.Name} is knocked from its grip. Disarmed, and Off-guard.";
        }

        /// <summary>Why <paramref name="taker"/> can't take up their dropped weapon now, or null if they can: standing over it, with an action.</summary>
        public static string? TakeUpRefusal(Combatant taker) =>
            taker.DroppedWeapon is null ? "There's no dropped weapon to take up."
            : taker.IsOutOfFight ? "Out of the fight."
            : taker.DroppedAt != taker.Position ? "The weapon lies where it fell — stand over it to take it up."
            : taker.ActionsRemaining <= 0 ? "Taking a weapon up costs an action."
            : null;

        /// <summary>Takes up the dropped weapon — an action — and ends Disarmed (Core Rules §9, "Conditions").</summary>
        private void TakeUp(Combatant taker)
        {
            taker.ActionsRemaining--;
            var weapon = taker.DroppedWeapon!;
            if (taker.Build is { } build)
            {
                // Back in hand — in place of whatever was put there since, if the hands are full.
                var inHand = InventoryCalculator.InHandWeapons(build);
                var hands = inHand.Sum(w => InventoryCalculator.FillsBothHands(w) ? 2 : 1) + InventoryCalculator.InHandShields(build).Count
                            + (InventoryCalculator.FillsBothHands(weapon) ? 2 : 1);
                if (hands > 2)
                {
                    foreach (var held in inHand)
                    {
                        held.IsEquipped = false;
                        held.IsAtReady = true;
                    }
                }

                weapon.IsEquipped = true;
                build.Weapons.Add(weapon);
            }

            taker.DroppedWeapon = null;
            taker.DroppedAt = null;
            taker.SetCondition(CharacterCondition.Disarmed, false);
            Write($"{taker.Name} takes up their {weapon.Name} again.");
        }

        /// <summary>The party's active character takes up their dropped weapon (Core Rules §9, "Disarmed").</summary>
        public bool TryTakeUpWeapon()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } taker || TakeUpRefusal(taker) is not null)
            {
                return false;
            }

            TakeUp(taker);
            return true;
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

            var threshold = second.DodgeThreshold + VisibilityDodgeModifier(second, seer: attacker);
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
                var through = second.IsSwarm ? 1 : outcome.RolledDamage - absorbed;
                Write($"Cleave: {roll.Total} against {second.Name}'s DT {against} — the sweep carries into {second.Name}: " +
                      (second.IsSwarm ? "1 wound, a Swarm's due from a single blow." : $"{outcome.RolledDamage} damage" + (absorbed > 0 ? $", armour absorbs {absorbed}." : ".")));
                Shelter(second, through, WoundType.Body, left =>
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
