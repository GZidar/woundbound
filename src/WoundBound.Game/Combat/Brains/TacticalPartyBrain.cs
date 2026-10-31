using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains
{
    /// <summary>When a party bot spends Stamina on a panic dodge.</summary>
    public enum PanicDodgePolicy
    {
        /// <summary>Never: keep the Stamina, and give the GM nothing.</summary>
        Never,

        /// <summary>Only against a critical hit, where the dodge is a Riposte and wounds the attacker.</summary>
        OnlyRipostes,

        /// <summary>Whenever it would turn the hit.</summary>
        Always,
    }

    /// <summary>
    /// The knobs a <see cref="TacticalPartyBrain"/> plays by. Each one is a tactic the simulator
    /// can switch on or off and measure, which is how a guess ("the bow is worth the swap") becomes
    /// a number. Defaults are the plain fighter: walk up and hit things.
    /// </summary>
    /// <param name="Name">What the report calls this way of playing.</param>
    /// <param name="ShootWhenOutOfReach">Swap to a ranged weapon at the ready and shoot, rather than walk in, when no enemy can be struck this turn.</param>
    /// <param name="BuyExtraAttacks">Spend Stamina on an extra action to attack again — banking the GM an Opportunity.</param>
    /// <param name="StaminaReserve">Never spend below this. At 0, a character who starts a turn falls unconscious.</param>
    /// <param name="BraceBelowBody">Take the Dodge action when Body has fallen to this fraction of its maximum or below, and an enemy can reach. 0 never braces.</param>
    /// <param name="PanicDodges">When to panic dodge.</param>
    public sealed record PartyTactics(
        string Name,
        bool ShootWhenOutOfReach = false,
        bool BuyExtraAttacks = false,
        int StaminaReserve = 1,
        double BraceBelowBody = 0,
        PanicDodgePolicy PanicDodges = PanicDodgePolicy.Always)
    {
        /// <summary>The ways of playing the simulator compares and the battle screen's bot can be set to.</summary>
        public static IReadOnlyList<PartyTactics> Presets { get; } =
        [
            new("Charge"),
            new("Charge, never dodge", PanicDodges: PanicDodgePolicy.Never),
            new("Charge, dodge only to Riposte", PanicDodges: PanicDodgePolicy.OnlyRipostes),
            new("Charge, extra attacks", BuyExtraAttacks: true),
            new("Charge, brace when hurt", BraceBelowBody: 0.5),
            new("Bow first", ShootWhenOutOfReach: true),
            new("Bow first, extra attacks", ShootWhenOutOfReach: true, BuyExtraAttacks: true),
            new("Bow first, brace when hurt", ShootWhenOutOfReach: true, BraceBelowBody: 0.5),
        ];
    }

    /// <summary>
    /// A rule-following party bot driven by <see cref="PartyTactics"/>: brace if the tactics say
    /// so, hit whatever can be hit (the weakest first), buy another swing if allowed, and otherwise
    /// close in — or, given a bow at the ready, shoot from where it stands.
    /// </summary>
    public class TacticalPartyBrain(PartyTactics tactics) : IPartyBrain
    {
        public PartyTactics Tactics { get; } = tactics;

        public string Name => Tactics.Name;

        public virtual PartyStep NextStep(BattleView view, Combatant character)
        {
            if (view.NearestEnemy(character) is not { } nearest)
            {
                return PartyStep.End;
            }

            // Bracing has to come first: a character who has moved can't, and one who has can't move.
            if (ShouldBrace(view, character))
            {
                return new PartyStep.StandGround(CombatAction.Dodge);
            }

            if (BestTarget(view, character) is { } target)
            {
                return new PartyStep.Attack(target);
            }

            if (character.ActionsRemaining == 0)
            {
                return ShouldBuyAnotherSwing(view, character) ? new PartyStep.BuyExtraAction() : PartyStep.End;
            }

            return character.Attack.IsRanged
                ? WithRangedWeaponInHand(view, character, nearest)
                : WithBladeInHand(view, character, nearest);
        }

        public virtual bool PanicDodges(BattleView view, PendingReaction reaction) => Tactics.PanicDodges switch
        {
            PanicDodgePolicy.Always => CanSpare(reaction.Defender),
            PanicDodgePolicy.OnlyRipostes => reaction.WouldRiposte && CanSpare(reaction.Defender),
            _ => false,
        };

        private bool ShouldBrace(BattleView view, Combatant character) =>
            Tactics.BraceBelowBody > 0
            && character.Body <= character.BodyMax * Tactics.BraceBelowBody
            && character.HasMove
            && BattleView.StandGroundRefusal(character, CombatAction.Dodge) is null
            && view.EnemiesOf(character).Any(e => e.CanReach(character.Position) || view.StrikeSquare(e, character) is not null);

        /// <summary>Whoever can be attacked now, weakest first, then nearest.</summary>
        protected static Combatant? BestTarget(BattleView view, Combatant character) =>
            view.EnemiesOf(character)
                .Where(e => view.CanAttack(character, e))
                .OrderBy(e => e.Body)
                .ThenBy(e => character.Position.DistanceTo(e.Position))
                .FirstOrDefault();

        private bool ShouldBuyAnotherSwing(BattleView view, Combatant character) =>
            Tactics.BuyExtraAttacks
            && character.Stamina - 1 >= Tactics.StaminaReserve
            && BattleView.TacticalSpendRefusal(character) is null
            && view.EnemiesOf(character).Any(e => view.PlanAttack(character, e).CanAttack);

        private bool CanSpare(Combatant character) => character.Stamina - 1 >= Tactics.StaminaReserve;

        private PartyStep WithBladeInHand(BattleView view, Combatant character, Combatant nearest)
        {
            if (character.IsStandingGround)
            {
                return PartyStep.End;
            }

            // Step into reach with the move, keeping the action for the blow.
            if (character.HasMove && view.EnemiesOf(character)
                    .Select(e => view.StrikeSquare(character, e))
                    .FirstOrDefault(s => s is not null) is { } strikeFrom)
            {
                return new PartyStep.Move(strikeFrom);
            }

            if (Tactics.ShootWhenOutOfReach && character.ReadyWeapon is { IsRanged: true } && BattleView.SwapRefusal(character) is null
                && character.HasMove)
            {
                return new PartyStep.Swap();
            }

            return CloseIn(view, character, nearest);
        }

        private PartyStep WithRangedWeaponInHand(BattleView view, Combatant character, Combatant nearest)
        {
            // Somebody at Close, where a bow can't shoot: back to the blade, if there is one.
            if (character.Position.DistanceTo(nearest.Position) <= 1 && character.ReadyWeapon is { IsRanged: false }
                && BattleView.SwapRefusal(character) is null)
            {
                return new PartyStep.Swap();
            }

            return character.IsStandingGround ? PartyStep.End : CloseIn(view, character, nearest);
        }

        /// <summary>As near to <paramref name="nearest"/> as what's left will carry them, the cheaper move where it's no further.</summary>
        private static PartyStep CloseIn(BattleView view, Combatant character, Combatant nearest) =>
            view.SquaresWithin(character, view.MovesAvailable(character))
                .Where(o => o.Key.DistanceTo(nearest.Position) < character.Position.DistanceTo(nearest.Position))
                .OrderBy(o => o.Key.DistanceTo(nearest.Position))
                .ThenBy(o => o.Value)
                .Select(o => (PartyStep)new PartyStep.Move(o.Key))
                .FirstOrDefault() ?? PartyStep.End;
    }
}
