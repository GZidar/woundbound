using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Breaking cover (Core Rules §9, "Breaking Cover"; the user's ruling, 2026-11-08): every piece
    /// has a toughness, a blade taken to it hits automatically and wears it down by the damage rolled,
    /// and once it is worn through, Full cover is knocked down to Partial and Partial is gone.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>The damage each piece of cover has taken since it last dropped a grade.</summary>
        private readonly Dictionary<GridPosition, int> coverWear = new();

        /// <summary>Bumped whenever a piece of cover breaks, so the routes worked out around it are worked out again.</summary>
        private int coverVersion;

        /// <summary>What Partial cover holds out against when the map doesn't say: a cart, a barrel, a hedge.</summary>
        public const int PartialToughness = 4;

        /// <summary>What Full cover holds out against when the map doesn't say: a stack of crates, a wagon on its side.</summary>
        public const int FullToughness = 8;

        /// <summary>
        /// How much the piece on <paramref name="square"/> takes to break, as it stands: the map's own
        /// figure for its grade, or the default for it. Null where there is no piece, or one that can't be broken.
        /// </summary>
        public int? CoverToughness(GridPosition square)
        {
            var grade = Setting.CoverAt(square);
            if (grade == Cover.None || Setting.Unbreakable.Contains(square))
            {
                return null;
            }

            var full = Setting.CoverToughness.TryGetValue(square, out var given) ? given : FullToughness;
            return grade == Cover.Full ? full
                : Setting.CoverToughness.ContainsKey(square) ? Math.Max(1, full / 2)
                : PartialToughness;
        }

        /// <summary>What is left of the piece on <paramref name="square"/> before it drops a grade; null where it can't be broken.</summary>
        public int? CoverLeft(GridPosition square) =>
            CoverToughness(square) is { } toughness ? toughness - coverWear.GetValueOrDefault(square) : null;

        /// <summary>The melee attack <paramref name="who"/> would take to the piece on <paramref name="square"/>: whichever does the most, in reach of it.</summary>
        private AttackProfile? BreakingBlow(Combatant who, GridPosition square) =>
            who.Attacks.Where(a => !a.IsRanged && !a.DealsNoDamage && a.Track == WoundType.Body && a.Against == ThresholdType.Dodge
                                   && Footprints.Distance(who.Position, who.Across, square, 1) <= Math.Max(1, a.Reach))
                .MaxBy(a => a.DamageDiceCount * (a.DamageDieSides + 1) / 2.0 + a.DamageModifier);

        /// <summary>Why <paramref name="who"/> can't take a blade to the cover on <paramref name="square"/> now, or null if they can.</summary>
        public string? BreakCoverRefusal(Combatant who, GridPosition square) =>
            Setting.CoverAt(square) == Cover.None ? "There's no cover there to break."
            : CoverToughness(square) is null ? "That won't break."
            : who.IsOutOfFight ? "Out of the fight."
            : who.ActionsRemaining < 1 ? "Breaking cover costs an action, as an attack does."
            : BreakingBlow(who, square) is null ? "Nothing in hand reaches it — breaking cover is close work, for a weapon in reach."
            : null;

        /// <summary>The active character takes a blade to the cover on <paramref name="square"/>, for an action.</summary>
        public bool TryBreakCover(GridPosition square)
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } who || BreakCoverRefusal(who, square) is not null)
            {
                return false;
            }

            BreakCover(who, square);
            return true;
        }

        /// <summary>
        /// A blow at a piece of cover: it hits, the damage rolled comes off its toughness (no armour, no
        /// critical), and once it is worn through the piece drops a grade. It is an attack's action
        /// and no Stamina, so it grants nobody an Opportunity.
        /// </summary>
        private void BreakCover(Combatant who, GridPosition square)
        {
            var blow = BreakingBlow(who, square)!;
            var grade = Setting.CoverAt(square);
            who.ActionsRemaining--;
            var rolled = Enumerable.Range(0, blow.DamageDiceCount).Sum(_ => dice.Roll(blow.DamageDieSides)) + blow.DamageModifier;
            var damage = Math.Max(PlayStateCalculator.DamageFloor, rolled);
            var toughness = CoverToughness(square)!.Value;
            var worn = coverWear.GetValueOrDefault(square) + damage;
            if (worn < toughness)
            {
                coverWear[square] = worn;
                Write($"{who.Name} hacks at the {grade.ToString().ToLowerInvariant()} cover at {square} with the {blow.Name}: {damage}, {toughness - worn} more to break it.");
                return;
            }

            coverWear.Remove(square);
            var pieces = Setting.CoverPieces.ToDictionary(kv => kv.Key, kv => kv.Value);
            if (grade == Cover.Full)
            {
                pieces[square] = Cover.Partial;
                Write($"{who.Name} breaks through the cover at {square} with the {blow.Name}: {damage} — full cover no longer, only partial.");
            }
            else
            {
                pieces.Remove(square);
                Write($"{who.Name} smashes the cover at {square} apart with the {blow.Name}: {damage} — nothing left to hide behind.");
            }

            Setting = Setting with { CoverPieces = pieces };
            coverVersion++;
        }
    }
}
