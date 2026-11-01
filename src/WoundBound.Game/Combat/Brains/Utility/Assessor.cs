using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// How good a brain is at choosing — the dial an encounter is tuned with. A brain scores every
    /// option it has; <see cref="Temperature"/> says how loosely it picks among them (0 always takes
    /// the best), and <see cref="Care"/> how much it minds the blows a choice invites.
    /// </summary>
    public sealed record BrainSkill(string Name, double Temperature, double Care, bool UsesTricks)
    {
        /// <summary>Always the most effective option.</summary>
        public static BrainSkill Best { get; } = new("Best", Temperature: 0, Care: 1, UsesTricks: true);

        /// <summary>Usually the best, sometimes the second best.</summary>
        public static BrainSkill Balanced { get; } = new("Balanced", Temperature: 0.6, Care: 0.8, UsesTricks: true);

        /// <summary>Picks loosely, barely minds where it stands, and never reaches past a plain blow.</summary>
        public static BrainSkill Poor { get; } = new("Poor", Temperature: 2.0, Care: 0.3, UsesTricks: false);

        public static IReadOnlyList<BrainSkill> Levels { get; } = [Best, Balanced, Poor];
    }

    /// <summary>
    /// What a brain needs to weigh a choice, worked out from the fight as it stands: the odds of
    /// a roll, what a blow is expected to do, what each creature is worth to its side, and the
    /// blows a square invites. Everything is in one currency — wounds' worth of a side's chance —
    /// so a kill, a Heal and a step out of reach can be set against each other.
    /// </summary>
    public sealed class Assessor
    {
        private readonly BattleView view;
        private readonly Dictionary<Combatant, double> threat = new();

        public Assessor(BattleView view)
        {
            this.view = view;
        }

        public BattleView View => view;

        /// <summary>How many rounds of a creature's harm dropping it is reckoned to save — about what's left of a fight.</summary>
        public const double Horizon = 3;

        /// <summary>
        /// How much a creature's side stands to lose if it drops: the harm it would do over the rest
        /// of the fight, plus a little for being one more body on the board — and a spellcaster, once
        /// they have shown themselves by casting, is worth the most of anybody to put down. Both
        /// sides are reckoned in the same currency, so a wound dealt and a wound taken trade evenly:
        /// the damage race that decides a fight.
        /// </summary>
        public double Value(Combatant c)
        {
            var value = 0.5 + Threat(c) * Horizon;
            if (c.HasCastSpell)
            {
                value *= 2.5;
            }

            if (c.Side == Side.Adversaries && view.MustNotEscape(c))
            {
                value += 4;
            }

            return value;
        }

        /// <summary>What one wound to <paramref name="c"/> is worth: its value spread over the wounds it can take — a Minion has one.</summary>
        public double PerWound(Combatant c) => Value(c) / Math.Max(1, c.IsMinion ? 1 : c.Body);

        /// <summary>The harm <paramref name="c"/> does in a turn to the enemy it would best hit, wherever they stand.</summary>
        public double Threat(Combatant c)
        {
            if (threat.TryGetValue(c, out var known))
            {
                return known;
            }

            var best = view.EnemiesOf(c)
                .SelectMany(e => c.Attacks.Select(a => Blow(c, a, e, a.DamageDieSides, e.DodgeThreshold).ExpectedDealt).Append(SpellHarm(c, e)))
                .DefaultIfEmpty(0)
                .Max();
            threat[c] = best;
            return best;
        }

        /// <summary>
        /// What the best Harm on <paramref name="c"/>'s sheet or statblock would do to <paramref name="e"/>
        /// on average — the chance the cast lands, times its dice after armour where it's Body. A
        /// caster's threat is its spells, however feeble the knife in its other hand.
        /// </summary>
        private static double SpellHarm(Combatant c, Combatant e)
        {
            double best = 0;
            foreach (var spell in c.Spells.Where(s => s.SpellType == SpellType.Harm))
            {
                var bonus = c.Build is { } build
                    ? c.MagicAccess.FirstOrDefault(a => a.Source == spell.Source) is { } access ? CharacterCalculator.CastBonus(build, access.CastAbility) : 0
                    : c.StatblockCastBonus;
                var dice = SpellCalculator.HealHarmDiceCount(spell.SpellTier) * (DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target)) + 1) / 2.0
                           + (c.Build is { } b ? SpellCalculator.GraceBonus(b, spell) : 0);
                var absorbs = (spell.DamageType ?? WoundType.Body) == WoundType.Body
                    ? InventoryCalculator.ArmourAbsorbedAgainst(e.Armour, WoundType.Body, isDirect: false)
                    : 0;
                best = Math.Max(best, Odds.Of(bonus, e.DodgeThreshold).Success * Math.Max(0, dice - absorbs));
            }

            return best;
        }

        /// <summary>
        /// What one blow is expected to do: the chance it lands — the defender spending a reaction
        /// to raise their Dodge Threshold if they can and it would turn the blow — the wounds it deals
        /// after armour, and the chance it takes the target out of the fight.
        /// </summary>
        /// <param name="precise">As though Precise were held: Direct, and the attacker's Vitality on the damage.</param>
        public (double Hit, double ExpectedDealt, double Kill) Blow(Combatant attacker, AttackProfile attack, Combatant target, int? dieSides, int dodgeThreshold,
            bool advantage = false, bool disadvantage = false, bool precise = false)
        {
            advantage |= attacker.IsHelped || target.IsDefenceless || attacker.HoldsPatient;
            precise |= attacker.HoldsPrecise;
            disadvantage |= attacker.IsStandingGround || attacker.IsOffGuard || attacker.IsHarried;

            // A defender who can still spend will raise their DT against a blow it would turn.
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1
                ? Math.Max(ThresholdModifierCalculator.ReactionBonus, target.InHandShield is { } shield && target.ShieldBonus == 0
                    ? Rules.Calculation.InventoryCalculator.ShieldDodgeThresholdBonus(shield.Weight)
                    : 0)
                : 0;
            var odds = Odds.Of(attack.AttackModifier, dodgeThreshold + answer, advantage, disadvantage);
            var plainHit = odds.Success - odds.CriticalSuccess;

            var absorbs = Rules.Calculation.InventoryCalculator.ArmourAbsorbedAgainst(target.Armour, WoundType.Body, isDirect: precise);
            var modifier = attack.DamageModifier + (precise && attacker.Build is { } build ? ExploitCalculator.PreciseDamageBonus(build) : 0);
            var (plainDealt, plainKill) = Damage(attack.DamageDiceCount, dieSides, modifier, bonusDie: null, absorbs, target);
            var (critDealt, critKill) = Damage(attack.DamageDiceCount, dieSides, modifier, bonusDie: attack.DamageDieSides, absorbs, target);

            return (odds.Success,
                plainHit * plainDealt + odds.CriticalSuccess * critDealt,
                plainHit * plainKill + odds.CriticalSuccess * critKill);
        }

        /// <summary>
        /// The wounds a hit deals after armour, on average, and the chance that's enough to drop the
        /// target. A critical rolls the maximum on its dice and adds the weapon's base die (Core
        /// Rules §10). A Minion is dropped by any wound that gets through.
        /// </summary>
        private static (double Dealt, double Kill) Damage(int count, int? sides, int modifier, int? bonusDie, int absorbs, Combatant target)
        {
            var critical = bonusDie is not null;
            var fixedDice = critical && sides is { } s ? count * s : 0;
            var rolledDice = new List<int>();
            if (!critical && sides is { } perDie)
            {
                rolledDice.AddRange(Enumerable.Repeat(perDie, count));
            }

            if (bonusDie is { } bonus)
            {
                rolledDice.Add(bonus);
            }

            double dealt = 0, kills = 0, outcomes = 0;
            foreach (var sum in Sums(rolledDice))
            {
                var rolled = Math.Max(PlayStateCalculator.DamageFloor, fixedDice + sum + modifier);
                var through = rolled - Math.Min(rolled, absorbs);
                dealt += through;
                kills += Drops(target, through) ? 1 : 0;
                outcomes++;
            }

            return (dealt / outcomes, kills / outcomes);
        }

        private static bool Drops(Combatant target, int through) =>
            through > 0 && (target.IsMinion || through >= target.Body || target.IsDefenceless && target.Body - 1 <= PlayStateCalculator.MinBodyWounds);

        private static IEnumerable<int> Sums(IReadOnlyList<int> dice)
        {
            if (dice.Count == 0)
            {
                yield return 0;
                yield break;
            }

            foreach (var rest in Sums(dice.Skip(1).ToList()))
            {
                for (var face = 1; face <= dice[0]; face++)
                {
                    yield return face + rest;
                }
            }
        }

        /// <summary>
        /// The best blow <paramref name="attacker"/> could land on <paramref name="target"/> from
        /// <paramref name="from"/>, in wounds' worth: what it deals, and above all the chance of
        /// dropping them.
        /// </summary>
        public (double Worth, AttackProfile? With) BestAttack(Combatant attacker, GridPosition from, Combatant target, bool advantage = false, bool precise = false)
        {
            var best = (Worth: 0.0, With: (AttackProfile?)null);
            foreach (var attack in attacker.Attacks)
            {
                var plan = view.PlanAttackWith(attacker, attack, from, target);
                if (!plan.CanAttack)
                {
                    continue;
                }

                var blow = Blow(attacker, attack, target, plan.DamageDieSides, plan.DodgeThreshold, advantage, precise: precise);
                var worth = Worth(blow, target);
                if (worth > best.Worth)
                {
                    best = (worth, attack);
                }
            }

            return best;
        }

        /// <summary>
        /// What a blow is worth: the wounds it deals at what each is worth to the target's side, and
        /// a little more for dropping the target outright, all at once.
        /// </summary>
        public double Worth((double Hit, double ExpectedDealt, double Kill) blow, Combatant target) =>
            blow.ExpectedDealt * PerWound(target) + 0.3 * blow.Kill * Value(target);

        /// <summary>
        /// The blows <paramref name="creature"/> invites standing on <paramref name="square"/> until
        /// its next turn, in wounds' worth: each enemy that could strike it there — a move and a
        /// blow, or a shot from where it stands — shared out among everybody on its side that enemy
        /// could choose instead.
        /// </summary>
        public double Exposure(Combatant creature, GridPosition square, int extraDodge = 0)
        {
            double expected = 0;
            foreach (var enemy in view.EnemiesOf(creature).Where(e => !e.IsBroken))
            {
                var stride = view.SquaresFor(enemy.Speed, 1);
                double best = 0;
                foreach (var attack in enemy.Attacks)
                {
                    // A shot from where it stands; a blow after a move's worth of closing in.
                    int? dodge = null, die = null;
                    if (attack.IsRanged)
                    {
                        var shot = view.PlanAttackWith(enemy, attack, enemy.Position, creature, square);
                        if (shot.CanAttack)
                        {
                            (dodge, die) = (shot.DodgeThreshold, shot.DamageDieSides);
                        }
                    }
                    else if (square.DistanceTo(enemy.Position) <= attack.Reach + stride)
                    {
                        (dodge, die) = (creature.DodgeThreshold + view.VisibilityDodgeModifier(creature, square), attack.DamageDieSides);
                    }

                    if (dodge is { } dt)
                    {
                        best = Math.Max(best, Blow(enemy, attack, creature, die, dt + extraDodge).ExpectedDealt);
                    }
                }

                if (best <= 0)
                {
                    continue;
                }

                var choices = 1 + view.EnemiesOf(enemy).Count(o => o != creature && o.Position.DistanceTo(enemy.Position) <= enemy.Reach + stride);
                expected += best / choices;
            }

            return expected * PerWound(creature);
        }

        /// <summary>The reaction blows <paramref name="mover"/> draws going from where it stands to <paramref name="to"/>, in wounds' worth.</summary>
        public double LeavingCost(Combatant mover, GridPosition to)
        {
            double cost = 0;
            if (mover.IsSlipping)
            {
                return 0;
            }

            foreach (var enemy in view.EnemiesOf(mover).Where(e => e.CanReach(mover.Position) && !e.CanReach(to) && BattleView.TacticalSpendRefusal(e) is null))
            {
                var plan = view.PlanAttack(enemy, mover);
                if (plan.CanAttack)
                {
                    cost += Blow(enemy, plan.Profile ?? enemy.Attack, mover, plan.DamageDieSides, plan.DodgeThreshold).ExpectedDealt * PerWound(mover);
                }
            }

            return cost;
        }

        /// <summary>The chance a Grab, Shove or Trip lands — against Stability, and the target standing firm if it can.</summary>
        public double ControlChance(Combatant actor, Combatant target, bool againstDodgeToo = false)
        {
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var threshold = againstDodgeToo ? Math.Max(target.DodgeThreshold, target.StabilityThreshold) : target.StabilityThreshold;
            return Odds.Of(actor.UnarmedAttackModifier, threshold + answer, actor.IsHelped,
                actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried).Success;
        }

        /// <summary>
        /// How much a creature of this side minds the blows a choice invites. A Minion is there to be
        /// spent, a Brute to soak them; a Skirmisher and anything fragile keep clear.
        /// </summary>
        public static double RiskAppetite(Combatant c) => c.Role switch
        {
            AdversaryRole.Minion => 0.15,
            AdversaryRole.Brute => 0.4,
            AdversaryRole.Solo => 0.5,
            AdversaryRole.Skirmisher => 1.2,
            AdversaryRole.Adept or AdversaryRole.Support or AdversaryRole.Controller => 1.1,
            AdversaryRole.Swarm => 0.2,
            _ => 0.8,
        };
    }
}
