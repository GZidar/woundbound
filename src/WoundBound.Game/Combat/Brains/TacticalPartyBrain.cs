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

    /// <summary>What a party bot does about an enemy leaving a character's reach.</summary>
    public enum LeaverPolicy
    {
        /// <summary>Lets them go, and keeps the Stamina.</summary>
        Never,

        /// <summary>Only a creature running for it: grab it if there's a hand free, trip it if not, strike if nothing else.</summary>
        StopRunners,

        /// <summary>Stops runners as above, and strikes at anybody else leaving.</summary>
        StrikeEveryone,
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
    /// <param name="PanicDodges">When to panic dodge — and, by the same policy, when to stand firm against a Grab, Shove or Trip.</param>
    /// <param name="TripForAllies">
    /// Trip an enemy in reach that isn't a Minion, when an ally still to act this round could
    /// strike it too — handing them its −2 DT — before swinging. Only where the trip is no harder
    /// than a blow would be, and never at a Minion, which any hit drops anyway.
    /// </param>
    /// <param name="ReactToLeavers">What to do about an enemy leaving a character's reach.</param>
    /// <param name="HuntRunners">
    /// Go after creatures that have broken — the ones a fight's objectives say mustn't escape
    /// first — and grab one in reach rather than swing at it.
    /// </param>
    /// <param name="RescueTheFallen">Get beside a dying or stable ally and Heal them, before anything else.</param>
    /// <param name="HelpWhenIdle">With nobody to hit this turn, Help an adjacent ally still to act who has — or else Harry an adjacent foe.</param>
    public sealed record PartyTactics(
        string Name,
        bool ShootWhenOutOfReach = false,
        bool BuyExtraAttacks = false,
        int StaminaReserve = 1,
        double BraceBelowBody = 0,
        PanicDodgePolicy PanicDodges = PanicDodgePolicy.Always,
        bool TripForAllies = false,
        LeaverPolicy ReactToLeavers = LeaverPolicy.Never,
        bool HuntRunners = false,
        bool RescueTheFallen = false,
        bool HelpWhenIdle = false)
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
            new("Charge, trip for allies", TripForAllies: true),
            new("Charge, trip for allies, extra attacks", TripForAllies: true, BuyExtraAttacks: true),
            new("Charge, strike at leavers", ReactToLeavers: LeaverPolicy.StrikeEveryone),
            new("Charge, stop runners", ReactToLeavers: LeaverPolicy.StopRunners, HuntRunners: true),
            new("Charge, stop runners, extra attacks", ReactToLeavers: LeaverPolicy.StopRunners, HuntRunners: true, BuyExtraAttacks: true),
            new("Charge, rescue the fallen", RescueTheFallen: true),
            new("Charge, rescue the fallen, help when idle", RescueTheFallen: true, HelpWhenIdle: true),
            new("Stop runners, rescue, help when idle", ReactToLeavers: LeaverPolicy.StopRunners, HuntRunners: true, RescueTheFallen: true, HelpWhenIdle: true),
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

            if (character.NeedsFooting && BattleView.RegainFootingRefusal(character) is null)
            {
                return new PartyStep.RegainFooting();
            }

            // Up off the floor first: Prone is Off-guard, and crawling is dear.
            if (character.IsProne && BattleView.StandUpRefusal(character) is null)
            {
                return new PartyStep.StandUp();
            }

            if (Tactics.RescueTheFallen && Rescue(view, character) is { } rescue)
            {
                return rescue;
            }

            // Bracing has to come first: a character who has moved can't, and one who has can't move.
            if (ShouldBrace(view, character))
            {
                return new PartyStep.StandGround(CombatAction.Dodge);
            }

            if (TripTarget(view, character) is { } toTrip)
            {
                return new PartyStep.Control(ControlAction.Trip, toTrip);
            }

            // A runner in reach: a hold keeps it here, where a blow might not.
            if (Tactics.HuntRunners && view.EnemiesOf(character)
                    .Where(e => e.IsBroken && e.HeldBy is null && BattleView.ControlRefusal(character, ControlAction.Grab, e) is null)
                    .OrderByDescending(e => view.MustNotEscape(e))
                    .FirstOrDefault() is { } runner)
            {
                return new PartyStep.Control(ControlAction.Grab, runner);
            }

            if (BestTarget(view, character) is { } target)
            {
                return new PartyStep.Attack(target);
            }

            if (Tactics.HuntRunners && Runners(view, character).FirstOrDefault() is { } quarry)
            {
                nearest = quarry;
            }

            if (Tactics.HelpWhenIdle && Aid(view, character) is { } aid)
            {
                return aid;
            }

            // Held with nobody to hit: get loose.
            if (character.HeldBy is not null && character.ActionsRemaining > 0)
            {
                return new PartyStep.BreakFree();
            }

            if (character.ActionsRemaining == 0)
            {
                return ShouldBuyAnotherSwing(view, character) ? new PartyStep.BuyExtraAction() : PartyStep.End;
            }

            return character.Attack.IsRanged
                ? WithRangedWeaponInHand(view, character, nearest)
                : WithBladeInHand(view, character, nearest);
        }

        /// <summary>
        /// Whoever can strike now goes first, so a blow lands before the GM's next turn — the
        /// weakest target in reach decides between two who can. Otherwise whoever is nearest an
        /// enemy, so the next to act is the one with most to do.
        /// </summary>
        public virtual Combatant ChooseNextCharacter(BattleView view, IReadOnlyList<Combatant> stillToAct) =>
            stillToAct
                .OrderBy(c => CanStrikeWithoutMoving(view, c) ? 0 : 1)
                .ThenBy(c => view.NearestEnemy(c) is { } e ? c.Position.DistanceTo(e.Position) : int.MaxValue)
                .First();

        /// <summary>Whether an attack could be made at somebody from where <paramref name="character"/> stands, armed as they are.</summary>
        protected static bool CanStrikeWithoutMoving(BattleView view, Combatant character) =>
            view.EnemiesOf(character).Any(e => view.PlanAttack(character, e).CanAttack);

        public virtual bool PanicDodges(BattleView view, PendingReaction reaction) => Tactics.PanicDodges switch
        {
            PanicDodgePolicy.Always => CanSpare(reaction.Defender),
            PanicDodgePolicy.OnlyRipostes => reaction.WouldRiposte && CanSpare(reaction.Defender),
            _ => false,
        };

        /// <summary>Heal a dying or stable ally beside them; or, with the move, step beside one first.</summary>
        private static PartyStep? Rescue(BattleView view, Combatant character)
        {
            var fallen = view.PartyOnBoard.Where(c => c != character && c.IsDefenceless).ToList();
            if (fallen.FirstOrDefault(f => BattleView.TendRefusal(character, CombatAction.Heal, f) is null) is { } beside)
            {
                return new PartyStep.Tend(CombatAction.Heal, beside);
            }

            if (!character.HasMove || character.ActionsRemaining == 0)
            {
                return null;
            }

            return fallen
                .Where(f => f.CanBeTended(WoundType.Body))
                .SelectMany(f => view.SquaresWithin(character, 1).Keys.Where(sq => sq.DistanceTo(f.Position) <= 1))
                .OrderBy(sq => sq.DistanceTo(character.Position))
                .Select(sq => (PartyStep)new PartyStep.Move(sq))
                .FirstOrDefault();
        }

        /// <summary>
        /// Nothing to hit, and no step that would bring anybody into reach: Help an adjacent ally
        /// still to act who has somebody to hit, or else Harry an adjacent foe.
        /// </summary>
        private static PartyStep? Aid(BattleView view, Combatant character)
        {
            if (character.ActionsRemaining == 0
                || (character.HasMove && view.EnemiesOf(character).Any(e => view.StrikeSquare(character, e) is not null)))
            {
                return null;
            }

            var ally = view.PartyStillToAct
                .Where(a => a != character && BattleView.AidRefusal(character, CombatAction.Help, a) is null && !a.IsHelped)
                .FirstOrDefault(a => view.EnemiesOf(a).Any(e => view.PlanAttack(a, e).CanAttack || view.StrikeSquare(a, e) is not null));
            if (ally is not null)
            {
                return new PartyStep.Aid(CombatAction.Help, ally);
            }

            return view.EnemiesOf(character)
                .Where(e => BattleView.AidRefusal(character, CombatAction.Harry, e) is null && !e.IsHarried)
                .Select(e => (PartyStep)new PartyStep.Aid(CombatAction.Harry, e))
                .FirstOrDefault();
        }

        public virtual bool StandsFirm(BattleView view, PendingReaction reaction) =>
            Tactics.PanicDodges == PanicDodgePolicy.Always && CanSpare(reaction.Defender);

        /// <summary>Only when the shove would drive them into something.</summary>
        public virtual bool ArrestsShove(BattleView view, PendingReaction reaction) =>
            reaction.ImpactWounds > 0 && CanSpare(reaction.Defender);

        /// <summary>
        /// With <see cref="PartyTactics.TripForAllies"/>: a non-Minion in reach, still on its feet,
        /// that an ally yet to act this round could strike — from where they stand or a move away.
        /// </summary>
        private Combatant? TripTarget(BattleView view, Combatant character)
        {
            if (!Tactics.TripForAllies || character.ActionsRemaining == 0)
            {
                return null;
            }

            var allies = view.PartyStillToAct.Where(a => a != character && !a.IsOutOfFight).ToList();
            return view.EnemiesOf(character)
                .Where(e => !e.IsMinion && BattleView.ControlRefusal(character, ControlAction.Trip, e) is null)
                .Where(e => e.StabilityThreshold - character.UnarmedAttackModifier
                            <= view.PlanAttack(character, e).DodgeThreshold - character.Attack.AttackModifier)
                .Where(e => allies.Any(a => view.PlanAttack(a, e).CanAttack || view.StrikeSquare(a, e) is not null))
                .OrderByDescending(e => e.Body)
                .FirstOrDefault();
        }

        private bool ShouldBrace(BattleView view, Combatant character) =>
            Tactics.BraceBelowBody > 0
            && character.Body <= character.BodyMax * Tactics.BraceBelowBody
            && character.HasMove
            && BattleView.StandGroundRefusal(character, CombatAction.Dodge) is null
            && view.EnemiesOf(character).Any(e => e.CanReach(character.Position) || view.StrikeSquare(e, character) is not null);

        /// <summary>Whoever can be attacked now — runners first when hunting them — weakest first, then nearest.</summary>
        protected Combatant? BestTarget(BattleView view, Combatant character) =>
            view.EnemiesOf(character)
                .Where(e => view.CanAttack(character, e))
                .OrderBy(e => Tactics.HuntRunners && e.IsBroken ? view.MustNotEscape(e) ? 0 : 1 : 2)
                .ThenBy(e => e.Body)
                .ThenBy(e => character.Position.DistanceTo(e.Position))
                .FirstOrDefault();

        /// <summary>Enemies that have broken, the ones that mustn't escape first, then whichever is nearest getting clear.</summary>
        protected static IEnumerable<Combatant> Runners(BattleView view, Combatant character) =>
            view.EnemiesOf(character)
                .Where(e => e.IsBroken)
                .OrderByDescending(view.MustNotEscape)
                .ThenByDescending(e => view.DistanceToNearestCharacter(e.Position));

        public virtual LeaverResponse ReactsToLeaver(BattleView view, PendingReaction reaction)
        {
            var options = reaction.Options ?? [];
            if (Tactics.ReactToLeavers == LeaverPolicy.Never || !CanSpare(reaction.Defender))
            {
                return LeaverResponse.LetThemGo;
            }

            if (reaction.Attacker.IsBroken)
            {
                foreach (var stop in new[] { LeaverResponse.Grab, LeaverResponse.Trip, LeaverResponse.Strike })
                {
                    if (options.Contains(stop))
                    {
                        return stop;
                    }
                }
            }

            return Tactics.ReactToLeavers == LeaverPolicy.StrikeEveryone && options.Contains(LeaverResponse.Strike)
                ? LeaverResponse.Strike
                : LeaverResponse.LetThemGo;
        }

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

            // Step into reach with the move, keeping the action for the blow — a runner's reach first.
            if (character.HasMove && view.EnemiesOf(character)
                    .OrderBy(e => Tactics.HuntRunners && e.IsBroken ? 0 : 1)
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
