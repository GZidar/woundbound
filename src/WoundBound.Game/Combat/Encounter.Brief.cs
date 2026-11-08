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
            AdversaryGoal.Hold hold => HoldBrokenInRound is { } broken && broken <= hold.Rounds ? ObjectiveStatus.Failed
                : Round > hold.Rounds || Phase == EncounterPhase.Defeat ? ObjectiveStatus.Achieved
                : IsOver ? ObjectiveStatus.Failed
                : ObjectiveStatus.InProgress,
            _ => null,
        };
    }
}
