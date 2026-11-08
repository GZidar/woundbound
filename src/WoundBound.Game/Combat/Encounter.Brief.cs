using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>The scene brief: why the fight is happening and whether the GM's side got what it wanted.</summary>
    public partial class Encounter
    {
        /// <summary>Why the fight is happening and what the GM's creatures want (null: a fight to the finish, as ever).</summary>
        public SceneBrief? Brief { get; private init; }

        /// <summary>The round a character first stood in the zone the GM's side is holding, if anyone has.</summary>
        public int? HoldBrokenInRound { get; private set; }

        /// <summary>Notes what the brief's goal needs remembered: the first character to stand in a held zone. Called as the board is looked at.</summary>
        private void NoteGoal()
        {
            // A carrier who falls drops the prize where it lies. (The state moves before the line is
            // written, so the look the line takes finds nothing more to do.)
            if (Prize is { Carrier: { IsOutOfFight: true, HasFled: false } carrier } dropped)
            {
                dropped.Carrier = null;
                dropped.At = carrier.Position;
                Write($"{carrier.Name} goes down, and the {dropped.Name} falls where it lies.");
            }

            if (Brief?.Goal is AdversaryGoal.Hold hold && HoldBrokenInRound is null
                && combatants.Any(c => c.Side == Side.Party && !c.IsSteed && !c.IsOutOfFight && c.Footprint.Any(hold.Zone.Squares.Contains)))
            {
                HoldBrokenInRound = Round;
            }
        }

        /// <summary>
        /// Whether the GM's side has got what its brief wanted, read from the fight as it stands.
        /// Null where the fight has no brief.
        /// </summary>
        public ObjectiveStatus? GoalStatus => Brief?.Goal switch
        {
            null => null,
            AdversaryGoal.Kill => Phase == EncounterPhase.Defeat ? ObjectiveStatus.Achieved : IsOver ? ObjectiveStatus.Failed : ObjectiveStatus.InProgress,
            AdversaryGoal.Capture => Phase == EncounterPhase.Defeat
                ? combatants.Any(c => c.Side == Side.Party && c.BodyStatus == BodyStatus.Dead) ? ObjectiveStatus.Failed : ObjectiveStatus.Achieved
                : IsOver ? ObjectiveStatus.Failed : ObjectiveStatus.InProgress,
            AdversaryGoal.Take take => take.Prize.IsGone || Phase == EncounterPhase.Defeat ? ObjectiveStatus.Achieved
                : IsOver ? ObjectiveStatus.Failed
                : ObjectiveStatus.InProgress,
            AdversaryGoal.Escape escape => Escaped(escape) || Phase == EncounterPhase.Defeat ? ObjectiveStatus.Achieved
                : IsOver ? ObjectiveStatus.Failed
                : ObjectiveStatus.InProgress,
            AdversaryGoal.Hold hold => HoldBrokenInRound is { } broken && broken <= hold.Rounds ? ObjectiveStatus.Failed
                : Round > hold.Rounds || Phase == EncounterPhase.Defeat ? ObjectiveStatus.Achieved
                : IsOver ? ObjectiveStatus.Failed
                : ObjectiveStatus.InProgress,
            _ => null,
        };

        /// <summary>Whether enough of the band got away: everyone named, or at least half of it.</summary>
        private bool Escaped(AdversaryGoal.Escape escape)
        {
            if (escape.Who is { Count: > 0 } who)
            {
                return who.All(w => w.HasFled);
            }

            var band = combatants.Where(c => c.Side == Side.Adversaries && !c.IsSteed && !c.IsPhantom).ToList();
            return band.Count(c => c.HasFled) * 2 >= band.Count;
        }

        /// <summary>The prize the GM's side means to carry off, if its brief has one.</summary>
        public Prize? Prize => Brief?.Goal is AdversaryGoal.Take take ? take.Prize : null;

        /// <summary>Why <paramref name="who"/> can't pick up the prize now, or null if they can.</summary>
        public string? TakePrizeRefusal(Combatant who) =>
            Prize is not { } prize ? "There's no prize here."
            : prize.At is not { } at ? $"Somebody already has the {prize.Name}."
            : who.IsOutOfFight ? "Out of the fight."
            : !who.Covers(at) ? $"The {prize.Name} lies at {at} — stand over it to take it."
            : who.ActionsRemaining < 1 ? "Picking something up costs an action."
            : null;

        /// <summary>Picks up the prize, for an action.</summary>
        private void TakePrize(Combatant who)
        {
            var prize = Prize!;
            who.ActionsRemaining--;
            prize.At = null;
            prize.Carrier = who;
            Write($"{who.Name} snatches up the {prize.Name}.");
        }
    }
}