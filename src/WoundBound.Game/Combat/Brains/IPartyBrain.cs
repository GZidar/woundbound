using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains
{
    /// <summary>
    /// A bot at the player's seat: chooses what a party member does, where a person would click.
    ///
    /// <para>Its choices are carried out by <see cref="BotDriver"/> through exactly the commands the
    /// battle screen's buttons call (<see cref="Encounter.TryMove"/>, <see cref="Encounter.TryAttack"/>
    /// and the rest), so a bot can do nothing a player couldn't. A choice the rules refuse throws:
    /// it's a bug in the brain.</para>
    /// </summary>
    public interface IPartyBrain
    {
        /// <summary>A short name for reports: "Charge", "Bow first".</summary>
        string Name { get; }

        /// <summary>
        /// Which of <paramref name="stillToAct"/> takes the next turn (Core Rules §9, "Players
        /// choose their own order"). Asked only when there are at least two to choose between,
        /// all of them still fighting. Must answer one of them.
        /// </summary>
        Combatant ChooseNextCharacter(BattleView view, IReadOnlyList<Combatant> stillToAct);

        /// <summary>What <paramref name="character"/> does next on their turn. Asked until it answers <see cref="PartyStep.End"/>.</summary>
        PartyStep NextStep(BattleView view, Combatant character);

        /// <summary>
        /// Whether to spend 1 Stamina on a panic dodge — or, with a shield in hand, on raising it —
        /// against <paramref name="reaction"/>. Only asked when it would turn the hit and the
        /// character could pay for it.
        /// </summary>
        bool PanicDodges(BattleView view, PendingReaction reaction);

        /// <summary>
        /// Whether to spend 1 Stamina for +2 Stability Threshold against a Grab, Shove, Trip or
        /// break-free that would otherwise work. Only asked when it would stop it.
        /// </summary>
        bool StandsFirm(BattleView view, PendingReaction reaction);

        /// <summary>Whether to spend 1 Stamina arresting a Shove, and stay put.</summary>
        bool ArrestsShove(BattleView view, PendingReaction reaction);

        /// <summary>
        /// What <see cref="PendingReaction.Defender"/> does about <see cref="PendingReaction.Attacker"/>
        /// leaving their reach: one of <see cref="PendingReaction.Options"/>.
        /// </summary>
        LeaverResponse ReactsToLeaver(BattleView view, PendingReaction reaction);

        /// <summary>
        /// Whether to Backlash Mind or Soul damage onto Body — <see cref="PendingReaction.ImpactWounds"/>
        /// of it, Direct — rather than be Stunned or Numbed (Core Rules §4). By default, whenever it
        /// leaves them standing.
        /// </summary>
        bool Backlashes(BattleView view, PendingReaction reaction) => reaction.Defender.Body - reaction.ImpactWounds >= 2;

        /// <summary>
        /// Whether <see cref="PendingReaction.Defender"/> Interrupts the adversary <see cref="PendingReaction.Attacker"/>'s
        /// declared action — at <see cref="PendingReaction.Against"/> — taking their whole turn now for 1 Mind.
        /// By default, never.
        /// </summary>
        bool Interrupts(BattleView view, PendingReaction reaction) => false;

        /// <summary>
        /// Whether <see cref="PendingReaction.Defender"/> Shelters <see cref="PendingReaction.Attacker"/> — taking
        /// <see cref="PendingReaction.ImpactWounds"/> of their damage as Soul. By default, when it would drop them and the
        /// Soul can take it.
        /// </summary>
        bool Shelters(BattleView view, PendingReaction reaction)
        {
            var ally = reaction.Attacker;
            var left = reaction.DamageType == Rules.Enums.WoundType.Body ? ally.Body : ally.Track(reaction.DamageType).Current;
            return reaction.ImpactWounds >= left && reaction.Defender.Soul > reaction.ImpactWounds;
        }

        /// <summary>
        /// How much of it <see cref="PendingReaction.Defender"/> Shelters — "any part of it" (Core Rules §3):
        /// 0 for none. By default, all of it if <see cref="Shelters"/> says so.
        /// </summary>
        int ShelterAmount(BattleView view, PendingReaction reaction) => Shelters(view, reaction) ? reaction.ImpactWounds : 0;

        /// <summary>Whether to pay 1 Stamina to go on holding the spell the character Sustains.</summary>
        bool Sustains(BattleView view, PendingReaction reaction) => reaction.Defender.Stamina > 2;
    }

    /// <summary>One thing a party member does on their turn — one click.</summary>
    public abstract record PartyStep
    {
        private PartyStep()
        {
        }

        public sealed record Move(GridPosition Square) : PartyStep;

        /// <param name="Sacrifice">Pay 1 Body first for Advantage on the roll (Core Rules §3).</param>
        /// <param name="Manoeuvre">A Martial manoeuvre declared with it (Core Rules §9).</param>
        public sealed record Attack(Combatant Target, bool Sacrifice = false, Manoeuvre? Manoeuvre = null) : PartyStep;

        /// <summary>Get up into the saddle from beside its mount, for a move.</summary>
        public sealed record Mount : PartyStep;

        /// <summary>Get down from the saddle, for a move.</summary>
        public sealed record Dismount : PartyStep;

        /// <summary>Throw the throwable weapon in hand: the Attack action, as a shot.</summary>
        public sealed record Throw(Combatant Target) : PartyStep;

        /// <summary>Take a weapon from the pack into hand, for an action.</summary>
        public sealed record DrawWeapon(Rules.Models.WeaponDefinition Weapon) : PartyStep;

        /// <summary>Pick a weapon up off the ground, for an action.</summary>
        public sealed record PickUpWeapon(GroundWeapon Lying) : PartyStep;

        /// <summary>The off-hand strike of a Two Weapons loadout, straight after the Attack action (Core Rules §9).</summary>
        public sealed record OffHand(Combatant Target, Manoeuvre? Manoeuvre = null) : PartyStep;

        /// <summary>Buy a Nimble Exploit — Slip, Precise or Patient — for 1 Stamina.</summary>
        public sealed record Exploit(Rules.Enums.NimbleExploit Which) : PartyStep;

        /// <summary>Cast a spell with the action (Core Rules §9, "Magic").</summary>
        public sealed record Cast(SpellCast Spell) : PartyStep;

        /// <summary>Swap the loadout in hand for the one at the ready.</summary>
        public sealed record Swap : PartyStep;

        /// <summary>Spend 1 Stamina on an extra action.</summary>
        public sealed record BuyExtraAction : PartyStep;

        /// <summary>Grab, Shove or Trip an enemy within the character's own reach.</summary>
        public sealed record Control(ControlAction Action, Combatant Target) : PartyStep;

        /// <summary>Stand up from Prone, for a move.</summary>
        public sealed record StandUp : PartyStep;

        /// <summary>Spend the action trying to break a hold.</summary>
        public sealed record BreakFree : PartyStep;

        /// <summary>Take up the weapon a Disarm knocked to their feet, with the action.</summary>
        public sealed record TakeUpWeapon : PartyStep;

        /// <summary>Help an adjacent ally, or Harry an adjacent enemy.</summary>
        public sealed record Aid(CombatAction Action, Combatant Target) : PartyStep;

        /// <summary>Heal, Meditate or Pray over themselves or an ally beside them.</summary>
        public sealed record Tend(CombatAction Action, Combatant Target) : PartyStep;

        /// <summary>Raise the shield in hand — over an ally, if one is named.</summary>
        public sealed record RaiseShield(Combatant? Ally = null) : PartyStep;

        /// <summary>Move to a square hauling whoever they hold.</summary>
        public sealed record Drag(GridPosition Square) : PartyStep;

        /// <summary>Spend the action regaining the footing a hazard took.</summary>
        public sealed record RegainFooting : PartyStep;

        /// <summary>Let go of whoever they're holding.</summary>
        public sealed record LetGo : PartyStep;

        /// <summary>Take Dodge, Grounded or Resolute.</summary>
        public sealed record StandGround(CombatAction Action) : PartyStep;

        public sealed record EndTurn : PartyStep;

        public static PartyStep End { get; } = new EndTurn();
    }
}
