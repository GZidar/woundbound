using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>A weapon lying on the board — dropped, or thrown and fallen where it struck.</summary>
    public sealed record GroundWeapon(GridPosition At, WeaponDefinition Weapon);

    /// <summary>
    /// Everything that goes through the pack or the ground (Core Rules §15, "Equipped Items"): taking a
    /// weapon from the pack into hand, or putting one away, costs an action, as does picking one up off
    /// the ground; dropping what's in hand costs nothing. A Heavy weapon taken from the pack mid-fight
    /// costs its 1 Stamina there and then (§9, "Equip Cost"). And a weapon that "can be thrown" may be
    /// — a shot by its own Weight past Close, which leaves the hand and lands where it struck (§9,
    /// "Weapon Range").
    /// </summary>
    public sealed partial class Encounter
    {
        private readonly List<GroundWeapon> groundWeapons = new();

        /// <summary>Weapons lying on the board, for anyone to pick up.</summary>
        public IReadOnlyList<GroundWeapon> GroundWeapons => groundWeapons;

        /// <summary>The weapons in <paramref name="who"/>'s pack: carried, but neither in hand nor at the ready.</summary>
        public static IReadOnlyList<WeaponDefinition> PackWeapons(Combatant who) =>
            who.Build is { } build ? build.Weapons.Where(w => !w.IsEquipped && !w.IsAtReady && !w.IsStored).ToList() : [];

        /// <summary>
        /// Why the loadout in hand can't take <paramref name="weapon"/> as well, or null if it can: tried
        /// against the rules' own loadout check — two hands, the off-hand blade Light, no light beside a
        /// Two-handed weapon.
        /// </summary>
        private static string? HandsRefusal(CharacterBuild build, WeaponDefinition weapon)
        {
            var (wasEquipped, wasReady, wasInBuild) = (weapon.IsEquipped, weapon.IsAtReady, build.Weapons.Contains(weapon));
            if (!wasInBuild)
            {
                build.Weapons.Add(weapon);
            }

            weapon.IsEquipped = true;
            weapon.IsAtReady = false;
            var check = InventoryCalculator.ValidateEquippedLoadout(build);
            (weapon.IsEquipped, weapon.IsAtReady) = (wasEquipped, wasReady);
            if (!wasInBuild)
            {
                build.Weapons.Remove(weapon);
            }

            return check.IsValid ? null : $"No room in hand for the {weapon.Name}: {check.Errors.FirstOrDefault() ?? "the hands are full"} Drop or stow what's held first.";
        }

        /// <summary>Why <paramref name="who"/> can't take <paramref name="weapon"/> from the pack into hand now, or null if they can.</summary>
        public static string? DrawRefusal(Combatant who, WeaponDefinition weapon)
        {
            if (who.Build is not { } build || !PackWeapons(who).Contains(weapon))
            {
                return $"The {weapon.Name} isn't in the pack.";
            }

            if (who.IsOutOfFight)
            {
                return "Out of the fight.";
            }

            if (who.ActionsRemaining < 1)
            {
                return "Taking a weapon from the pack costs an action.";
            }

            if (weapon.Weight == WeaponWeight.Heavy && who.Stamina < 1)
            {
                return $"The {weapon.Name} is Heavy: equipping it costs 1 Stamina, and there's none left.";
            }

            return HandsRefusal(build, weapon);
        }

        /// <summary>Takes <paramref name="weapon"/> from the pack into hand: an action, and 1 Stamina for a Heavy one, levied by the equipment.</summary>
        public bool TryDrawWeapon(WeaponDefinition weapon)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } who || DrawRefusal(who, weapon) is not null)
            {
                return false;
            }

            Draw(who, weapon);
            return true;
        }

        private void Draw(Combatant who, WeaponDefinition weapon)
        {
            who.ActionsRemaining--;
            weapon.IsEquipped = true;
            weapon.IsAtReady = false;
            var heavy = weapon.Weight == WeaponWeight.Heavy;
            if (heavy)
            {
                who.Stamina--;
            }

            Write($"{who.Name} takes the {weapon.Name} from the pack into hand" +
                  (heavy ? $" — Heavy, so 1 Stamina to equip ({who.Stamina} left; no Opportunity)." : "."));
            Rearmed(who);
        }

        /// <summary>Why <paramref name="who"/> can't put the weapon in hand away into the pack, or null if they can.</summary>
        public static string? StowRefusal(Combatant who, WeaponDefinition weapon) =>
            who.Build is null || !InventoryCalculator.IsInHandWeapon(weapon) || !who.Build.Weapons.Contains(weapon) ? $"The {weapon.Name} isn't in hand."
            : who.IsOutOfFight ? "Out of the fight."
            : who.ActionsRemaining < 1 ? "Putting a weapon away into the pack costs an action."
            : null;

        /// <summary>Puts a weapon in hand away into the pack, for an action.</summary>
        public bool TryStowWeapon(WeaponDefinition weapon)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } who || StowRefusal(who, weapon) is not null)
            {
                return false;
            }

            who.ActionsRemaining--;
            weapon.IsEquipped = false;
            Write($"{who.Name} puts the {weapon.Name} away into the pack.");
            CheckHolds();
            return true;
        }

        /// <summary>Drops a weapon in hand at <paramref name="who"/>'s feet: free, and anyone may pick it up.</summary>
        public bool TryDropWeapon(WeaponDefinition weapon)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } who || who.Build is not { } build
                || !InventoryCalculator.IsInHandWeapon(weapon) || !build.Weapons.Remove(weapon))
            {
                return false;
            }

            groundWeapons.Add(new GroundWeapon(who.Position, weapon));
            Write($"{who.Name} drops the {weapon.Name} — it costs nothing.");
            CheckHolds();
            return true;
        }

        /// <summary>Why <paramref name="who"/> can't pick <paramref name="lying"/> up off the ground now, or null if they can.</summary>
        public string? PickUpRefusal(Combatant who, GroundWeapon lying) =>
            !groundWeapons.Contains(lying) ? "There's nothing there to pick up."
            : who.Build is null ? $"{who.Name} has no use for it."
            : who.IsOutOfFight ? "Out of the fight."
            : !who.Covers(lying.At) ? $"The {lying.Weapon.Name} lies where it fell — stand over it to pick it up."
            : who.ActionsRemaining < 1 ? "Picking something up off the ground costs an action."
            : null;

        /// <summary>
        /// Picks a weapon up off the ground, for an action: into hand where the hands have room for
        /// it, into the pack where they don't.
        /// </summary>
        public bool TryPickUpWeapon(GroundWeapon lying)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } who || PickUpRefusal(who, lying) is not null)
            {
                return false;
            }

            PickUp(who, lying);
            return true;
        }

        private void PickUp(Combatant who, GroundWeapon lying)
        {
            var build = who.Build!;
            who.ActionsRemaining--;
            groundWeapons.Remove(lying);
            var weapon = lying.Weapon;
            weapon.IsAtReady = false;
            weapon.IsEquipped = HandsRefusal(build, weapon) is null;
            build.Weapons.Add(weapon);
            Write($"{who.Name} picks up the {weapon.Name}" + (weapon.IsEquipped ? " — in hand." : ", with no hand free for it — into the pack."));
            if (weapon.IsEquipped)
            {
                Rearmed(who);
            }
        }

        /// <summary>A Disarmed character with a weapon in hand again is armed, and no longer Off-guard.</summary>
        private void Rearmed(Combatant who)
        {
            if (who.IsDisarmed && who.InHandWeapon is not null)
            {
                who.SetCondition(CharacterCondition.Disarmed, false);
                Write($"{who.Name} is armed again, and no longer Off-guard.");
            }

            CheckHolds();
        }

        /// <summary>
        /// The throw of a weapon in hand that "can be thrown" (Core Rules §9, "Starting Weapons"): its
        /// own attack, made as a shot — by its Weight on the range table — so only past Close. Null
        /// for anyone holding nothing throwable.
        /// </summary>
        public static AttackProfile? ThrowOf(Combatant who) =>
            who.Build is { } build && InventoryCalculator.InHandWeapons(build).FirstOrDefault(w => w.CanBeThrown && !w.IsRanged) is { } throwable
                ? AttackProfile.ForWeapon(build, throwable) with { IsThrown = true }
                : null;

        /// <summary>The throw at <paramref name="target"/> from where <paramref name="who"/> stands, as <see cref="PlanAttack"/> plans a blow.</summary>
        public AttackPlan PlanThrow(Combatant who, Combatant target) =>
            ThrowOf(who) is { } thrown
                ? PlanAttackWith(who, thrown, who.Position, target)
                : new AttackPlan("Nothing to throw.", 0, 0, null, "Nothing to throw.", who.Attack);

        /// <summary>Why <paramref name="who"/> can't throw at <paramref name="target"/> now, or null if they can.</summary>
        public string? ThrowRefusal(Combatant who, Combatant target)
        {
            if (ThrowOf(who) is not { } thrown)
            {
                return "Nothing in hand that can be thrown.";
            }

            if (who.ActionsRemaining < 1 || who.NeedsFooting || who.IsOutOfFight)
            {
                return "Throwing is an attack, and takes an action.";
            }

            if (target == who || (target.IsOutOfFight && !target.IsDefenceless))
            {
                return "Not a target.";
            }

            if (who.DistanceTo(target) <= 1)
            {
                return $"At Close, the {thrown.Name} is swung, not thrown.";
            }

            return PlanAttackWith(who, thrown, who.Position, target).Refusal;
        }

        /// <summary>
        /// Throws the throwable weapon in hand at <paramref name="target"/>: the Attack action, rolled
        /// as a shot at the range it flies, no manoeuvre (manoeuvres are melee only). It leaves the
        /// hand and lies where the target stands, hit or miss.
        /// </summary>
        public AttackOutcome? TryThrow(Combatant target, bool sacrifice = false)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } thrower || ThrowRefusal(thrower, target) is not null
                || (sacrifice && SacrificeRefusal(thrower) is not null))
            {
                return null;
            }

            AttackOutcome? result = null;
            DeclareForParty(thrower, $"{ThrowOf(thrower)!.Name}, thrown at {target.Name}", target,
                () => ThrowRefusal(thrower, target) is null && (!sacrifice || SacrificeRefusal(thrower) is null),
                () => Throw(thrower, target, sacrifice, opportunityBonus: false, outcome =>
                {
                    result = outcome;
                    CheckForEnd();
                }),
                new Declaration(thrower, IntentKind.Shot, ThrowOf(thrower)!.Name, target));

            return result;
        }

        private void Throw(Combatant thrower, Combatant target, bool sacrifice, bool opportunityBonus, Action<AttackOutcome> then)
        {
            var thrown = ThrowOf(thrower)!;
            var weapon = thrown.Weapon!;
            ResolveAttack(thrower, target, asReaction: false, opportunityBonus, outcome =>
            {
                thrower.Build!.Weapons.Remove(weapon);
                groundWeapons.Add(new GroundWeapon(target.Position, weapon));
                Write($"The {weapon.Name} leaves {thrower.Name}'s hand, and lies where {target.Name} stands.");
                CheckHolds();
                then(outcome);
            }, with: thrown, sacrifice: sacrifice);
        }
    }
}
