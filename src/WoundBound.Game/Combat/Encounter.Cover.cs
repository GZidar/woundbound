using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Cover (Core Rules §9, "Cover"): something solid between an attacker and their target.
    /// Partial is +2 to Dodge Threshold, Full can't be targeted, a Volume ignores both, and an
    /// attack out of full cover drops the attacker to partial until their next turn unless they
    /// pay 1 Stamina to duck back.
    ///
    /// <para>On the grid, a piece of cover (<see cref="Setting.CoverPieces"/>) shelters a creature
    /// standing right beside it from an attack whose line runs through it — "a character standing
    /// behind it has it". The same piece beside the attacker is something they lean round, not
    /// something in the target's way. A full piece standing off by itself, beside neither, blocks
    /// the line the way a wall does.</para>
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>
        /// The cover <paramref name="target"/>, standing on <paramref name="targetAt"/> (or where it
        /// is), has against an attack coming from <paramref name="from"/> — the best piece in the
        /// way, since cover never stacks with itself. A creature that has shown itself to attack
        /// out of full cover has only partial until its next turn.
        /// </summary>
        public Cover CoverFrom(GridPosition from, Combatant target, GridPosition? targetAt = null)
        {
            var grade = CoverAsPlaced(from, target, targetAt);
            return grade == Cover.Full && target.IsShowingFromCover ? Cover.Partial : grade;
        }

        /// <summary>The cover the pieces on the map give, before anything the target has done about it.</summary>
        private Cover CoverAsPlaced(GridPosition from, Combatant target, GridPosition? targetAt = null)
        {
            if (Setting.CoverPieces.Count == 0)
            {
                return Cover.None;
            }

            var at = targetAt ?? target.Position;
            var stands = Footprints.At(at, target.Across).ToList();
            return CoverCalculator.Best(Sight.Between(from, at)
                .Where(sq => !stands.Contains(sq) && stands.Any(s => s.DistanceTo(sq) <= 1))
                .Select(Setting.CoverAt));
        }

        /// <summary>
        /// Whether a line from <paramref name="from"/> to <paramref name="to"/> is open: no wall on it,
        /// and no full piece of cover that stands beside neither end — a pillar in the middle of
        /// the room blocks a look across it, where one beside the archer is only leant round.
        /// </summary>
        internal bool LineOpen(GridPosition from, GridPosition to) =>
            (Setting.Walls.Count == 0 || Sight.Clear(from, to, Setting.IsWall))
            && (Setting.CoverPieces.Count == 0
                || !Sight.Between(from, to).Any(sq => Setting.CoverAt(sq) == Cover.Full && sq.DistanceTo(from) > 1 && sq.DistanceTo(to) > 1));

        /// <summary>What partial cover adds to Dodge Threshold, by the table.</summary>
        private int CoverDodgeBonus(Cover grade) => grade == Cover.Partial ? CoverCalculator.DodgeThresholdModifier(environment, Cover.Partial) : 0;

        /// <summary>
        /// What cover adds to <paramref name="target"/>'s Dodge Threshold against something coming
        /// from <paramref name="from"/> — nothing against a Volume, which "fills the space it is cast
        /// into and flows around whatever solid matter stands inside it".
        /// </summary>
        internal int CoverDodgeModifier(GridPosition from, Combatant target, bool isVolume = false) =>
            isVolume ? 0 : CoverDodgeBonus(CoverFrom(from, target));

        /// <summary>
        /// <paramref name="attacker"/> has just attacked or cast at <paramref name="targets"/>: if
        /// any of them couldn't have targeted it back for full cover, it "has to show themselves to
        /// do it, and drops to partial cover until the start of their next turn".
        /// </summary>
        private void ShowFromCover(Combatant attacker, IEnumerable<Combatant> targets)
        {
            if (Setting.CoverPieces.Count == 0 || attacker.IsShowingFromCover)
            {
                return;
            }

            if (targets.Any(t => t != attacker && t.OccupiesSquare && CoverAsPlaced(t.Position, attacker) == Cover.Full))
            {
                attacker.IsShowingFromCover = true;
                attacker.MayDuckBack = true;
                Write($"{attacker.Name} shows themselves to do it — partial cover, not full, until their next turn"
                      + (TacticalSpendRefusal(attacker) is null ? ", unless they spend 1 Stamina to duck back." : "."));
            }
        }

        /// <summary>
        /// Why <paramref name="combatant"/> can't duck back into full cover now, or null if they can:
        /// they attacked out of it this turn and a tactical spend is still theirs (Core Rules §9,
        /// "Popping out").
        /// </summary>
        public static string? DuckBackRefusal(Combatant combatant)
        {
            if (!combatant.IsShowingFromCover || !combatant.MayDuckBack)
            {
                return "Only after attacking out of full cover, on the same turn.";
            }

            return TacticalSpendRefusal(combatant);
        }

        /// <summary>Ducks the active character back into full cover: 1 Stamina, tactical, and an Immediate Opportunity for the GM.</summary>
        public bool TryDuckBack()
        {
            if (Phase != EncounterPhase.PartyTurn || Active is not { } character || DuckBackRefusal(character) is not null)
            {
                return false;
            }

            DuckBack(character);
            return true;
        }

        private void DuckBack(Combatant combatant)
        {
            PayTacticalSpend(combatant, CoverCalculator.DuckBackStaminaCost);
            combatant.IsShowingFromCover = false;
            combatant.MayDuckBack = false;
            if (combatant.Side == Side.Party)
            {
                Write($"{combatant.Name} {CombatActionCatalog.DuckBackLogLine()} ({combatant.Stamina} left)");
                GrantImmediateOpportunity(combatant);
            }
            else
            {
                // An adversary's spend hands nobody an Opportunity (GM Reference §9, "On Stamina").
                Write($"{combatant.Name} ducks back into full cover after attacking — can't be targeted from that side (1 Stamina, {combatant.Stamina} left).");
            }
        }
    }
}
