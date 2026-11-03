using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Mounts (Core Rules §9): a mount changes how its rider moves and gives the table something
    /// more to hit; it takes no turns of its own. Mounting or dismounting costs a move and is not
    /// movement. An attack on a rider may be made at the mount instead, and an area catches both.
    /// A mount at 0 Body goes down and so does its rider, Prone in the next square; a Topple, Trip
    /// or Shift landed on a rider unhorses them the same way.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>Mounts that have gone down, and taken their riders with them.</summary>
        private readonly HashSet<Combatant> fallenSteeds = new();

        /// <summary>Every mount on the board, ridden or standing riderless, still on its feet.</summary>
        public IEnumerable<Combatant> Steeds => combatants.Where(c => c.Steed is { Body: > 0 }).Select(c => c.Steed!);

        /// <summary>Why <paramref name="rider"/> can't get down from the saddle now, or null if they can.</summary>
        public static string? DismountRefusal(Combatant rider) =>
            !rider.IsRiding ? $"{rider.Name} isn't riding."
            : rider.IsOutOfFight ? "Out of the fight."
            : MovesAvailable(rider) < 1 ? "Dismounting costs a move, and there's nothing left to pay with."
            : null;

        /// <summary>Gets down from the saddle, for a move — not movement: nobody's reach is left (Core Rules §9, "Mounts").</summary>
        public bool TryDismount()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } rider || DismountRefusal(rider) is not null)
            {
                return false;
            }

            Dismount(rider);
            return true;
        }

        private void Dismount(Combatant rider)
        {
            var paidWith = PayMoves(rider, 1);
            rider.IsRiding = false;
            Write($"{rider.Name} gets down from {rider.Steed!.Name}, for {paidWith}.");
        }

        /// <summary>Why <paramref name="rider"/> can't get up into the saddle now, or null if they can.</summary>
        public string? MountRefusal(Combatant rider) =>
            rider.Steed is not { } steed ? $"{rider.Name} has no mount."
            : rider.IsRiding ? "Already in the saddle."
            : steed.Body <= 0 ? $"{steed.Name} is down."
            : rider.IsOutOfFight ? "Out of the fight."
            : rider.IsProne ? "On the ground — stand up first."
            : rider.HeldBy is not null ? "Held — nobody gets into a saddle with somebody hanging on."
            : rider.DistanceTo(steed.Position) > 1 ? $"{steed.Name} is out of reach — stand beside it first."
            : At(steed.Position) is { } there && there != rider ? $"Somebody is standing where {steed.Name} is."
            : MovesAvailable(rider) < 1 ? "Mounting costs a move, and there's nothing left to pay with."
            : null;

        /// <summary>Gets up into the saddle from beside the mount, for a move — not movement: nobody's reach is left.</summary>
        public bool TryMount()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } rider || MountRefusal(rider) is not null)
            {
                return false;
            }

            Mount(rider);
            return true;
        }

        private void Mount(Combatant rider)
        {
            var paidWith = PayMoves(rider, 1);
            var steed = rider.Steed!;
            rider.IsRiding = true;
            rider.Position = steed.Position;
            Write($"{rider.Name} mounts {steed.Name}, for {paidWith}.");
            CheckHolds();
        }

        /// <summary>
        /// Throws <paramref name="rider"/> from the saddle: Prone in the next open square, the mount
        /// left standing where it was (Core Rules §9, "Mounts").
        /// </summary>
        private void Unhorse(Combatant rider, string why)
        {
            if (!rider.IsRiding)
            {
                return;
            }

            rider.IsRiding = false;
            var from = rider.Position;
            var landing = Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => new GridPosition(from.X + dx, from.Y + dy)))
                .Where(sq => sq != from && !Setting.IsSolid(sq) && At(sq) is null)
                .OrderBy(sq => Math.Abs(sq.X - from.X) + Math.Abs(sq.Y - from.Y))
                .Cast<GridPosition?>()
                .FirstOrDefault();
            if (landing is { } next)
            {
                rider.Position = next;
            }

            rider.SetCondition(CharacterCondition.Prone, true);
            Write($"{rider.Name} is thrown from the saddle by {why} — Prone in the next square.");
            CheckHolds();
        }

        /// <summary>Puts <paramref name="target"/> on the ground — and a rider out of the saddle first, which is what a Topple or Trip does to one.</summary>
        private void KnockDown(Combatant target, string by)
        {
            if (target.IsRiding)
            {
                Unhorse(target, by);
                return;
            }

            target.SetCondition(CharacterCondition.Prone, true);
        }

        /// <summary>
        /// A mount brought to 0 Body goes down, and its rider with it — Prone in the next square
        /// (Core Rules §9, "Mounts"). Run whenever wounds may have landed.
        /// </summary>
        private void CheckSteeds()
        {
            foreach (var owner in combatants.Where(c => c.Steed is { Body: <= 0 } steed && !fallenSteeds.Contains(steed)).ToList())
            {
                var steed = owner.Steed!;
                fallenSteeds.Add(steed);
                Write($"{steed.Name} goes down.");
                if (owner.IsRiding)
                {
                    Unhorse(owner, $"{steed.Name} falling");
                }
            }
        }

        /// <summary>No Heavy ranged weapon from a moving mount: a longbow or heavy crossbow needs footing a saddle doesn't give.</summary>
        private static bool SaddleForbids(Combatant attacker, AttackProfile attack) =>
            attacker.IsRiding && attack.Weapon is { IsRanged: true, Weight: WeaponWeight.Heavy };
    }
}
