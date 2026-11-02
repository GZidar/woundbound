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
        /// <summary>Tricks this brain leaves alone even though it uses the rest — for measuring what each is worth.</summary>
        public IReadOnlySet<Trick> Off { get; init; } = new HashSet<Trick>();

        /// <summary>
        /// How many of the top-scored options a loose pick is made among. Without it, a choice
        /// worth little more than standing about — a blow that costs a Swarm 1 wound — is drowned by
        /// the hundreds of squares there are to walk to, and the brain wanders.
        /// </summary>
        public int Breadth { get; init; } = int.MaxValue;

        /// <summary>Whether this brain reaches for <paramref name="trick"/>.</summary>
        public bool Uses(Trick trick) => UsesTricks && !Off.Contains(trick);

        /// <summary>Always the most effective option.</summary>
        public static BrainSkill Best { get; } = new("Best", Temperature: 0, Care: 1, UsesTricks: true);

        /// <summary>Usually the best, sometimes the second best.</summary>
        public static BrainSkill Balanced { get; } = new("Balanced", Temperature: 0.6, Care: 0.8, UsesTricks: true) { Breadth = 3 };

        /// <summary>Picks loosely, barely minds where it stands, and never reaches past a plain blow.</summary>
        public static BrainSkill Poor { get; } = new("Poor", Temperature: 2.0, Care: 0.3, UsesTricks: false) { Breadth = 8 };

        public static IReadOnlyList<BrainSkill> Levels { get; } = [Best, Balanced, Poor];
    }

    /// <summary>The kinds of play past a plain blow, move or spell, each of which a brain can be told to leave alone.</summary>
    public enum Trick
    {
        /// <summary>Slip, Precise and Patient.</summary>
        Exploits,

        /// <summary>Cleave, Topple, Ring, Disarm, Sunder.</summary>
        Manoeuvres,

        /// <summary>Standing ground with the Dodge action.</summary>
        Brace,

        /// <summary>Buff, Control and Shift spells.</summary>
        SpellTricks,

        /// <summary>Trip and Grab, on its turn or at a leaver.</summary>
        Grapple,

        /// <summary>Help and Harry.</summary>
        Aid,

        /// <summary>The Heal action.</summary>
        Tend,

        /// <summary>Raising a shield as an action.</summary>
        Shield,
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
        private readonly Dictionary<Combatant, double> support = new();
        private readonly Dictionary<Combatant, double> lasts = new();

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
            if (values.TryGetValue(c, out var known))
            {
                return known;
            }

            // The harm it would do is counted in its victims' wounds, so it is converted at what a
            // wound to them is worth against the board's average: a wound to the Solo's victims and
            // a wound to the Solo are then the same coin, and protecting a caster from one blow is
            // no longer worth more than several blows on the thing about to hit her.
            var value = 0.5 + (RawValue(c) - 0.5) * Exchange(c);
            if (c.Side == Side.Adversaries && view.MustNotEscape(c))
            {
                value += 4;
            }

            values[c] = value;
            return value;
        }

        private readonly Dictionary<Combatant, double> values = new();

        /// <summary>
        /// A creature's worth before any exchange: its harm and help over the rounds it would last,
        /// in raw wounds. Casters first — but only for as long as they can still cast: a caster with
        /// nothing left to pay for a spell is just another body, and one already dropped once, and
        /// brought back on a wound or two, is no longer the threat that made them the target (the
        /// user's direction, 2026-11-02 — for every brain, not only a Solo's).
        /// </summary>
        private double RawValue(Combatant c)
        {
            var value = 0.5 + (Threat(c) + Support(c)) * Lasts(c);
            if (c.HasCastSpell)
            {
                value *= 1 + (c.HasBeenDropped ? 0.5 : 1.5) * CastingLeft(c) * Spread(c);
            }

            return value;
        }

        /// <summary>
        /// How much of the other side a caster's area spells could catch at once, 0 to 1 — the reason
        /// casters come first: a creature that can harm the whole group (the user's direction,
        /// 2026-11-02). Facing one creature — a Solo — there is no group to harm, and the caster is
        /// worth whatever harm they do it, like anybody; nor does a caster with only single-target
        /// spells earn it.
        /// </summary>
        private double Spread(Combatant c)
        {
            var area = c.Spells.Any(s => s.Target == SpellTarget.Many && s.SpellType is SpellType.Harm or SpellType.Control && CastsLeft(c, s) > 0);
            var targets = view.EnemiesOf(c).Count(e => !e.IsPhantom && !e.IsOutOfFight);
            return area ? Math.Min(1, (targets - 1) / 3.0) : 0;
        }

        private double RawPerWound(Combatant c) => RawValue(c) / Math.Max(1, c.IsMinion ? 1 : c.Body);

        /// <summary>
        /// What a wound <paramref name="c"/> deals is worth, relative to the board: the raw worth of a
        /// wound to its enemies over the raw worth of a wound to anybody fighting. About 1 where the
        /// sides are matched; well under it for a party whose every blow lands on a Solo's deep Body.
        /// </summary>
        private double Exchange(Combatant c)
        {
            var everybody = view.Combatants.Where(x => !x.IsOutOfFight && !x.HasFled).ToList();
            var enemies = view.EnemiesOf(c).Where(e => !e.IsPhantom && !e.IsOutOfFight).ToList();
            if (enemies.Count == 0 || everybody.Count == 0)
            {
                return 1;
            }

            var average = everybody.Average(RawPerWound);
            return average <= 0 ? 1 : Math.Clamp(enemies.Average(RawPerWound) / average, 0.25, 4);
        }

        /// <summary>
        /// The rounds of a creature's harm that dropping it saves: what's left of the fight, at the
        /// least — but a creature with Body enough to outlast that is worth its harm for as long as
        /// it would take its enemies to wear it down, to a cap. Without it a Solo's sixty wounds are
        /// each worth next to nothing, though every one brings the end of the whole fight nearer.
        /// </summary>
        public double Lasts(Combatant c)
        {
            if (lasts.TryGetValue(c, out var known))
            {
                return known;
            }

            var output = view.EnemiesOf(c).Where(e => !e.IsPhantom && !e.IsBroken)
                .Sum(e => e.Attacks.Where(a => a.AreaSquares is null)
                    .Select(a => Blow(e, a, c, a.DamageDieSides, c.DodgeThreshold).ExpectedDealt)
                    .DefaultIfEmpty(0).Max());
            var rounds = Math.Clamp(c.Body / Math.Max(1, output), Horizon, MaxHorizon);
            lasts[c] = rounds;
            return rounds;
        }

        /// <summary>The longest a creature's harm is reckoned over, however much Body it has.</summary>
        public const double MaxHorizon = 8;

        /// <summary>
        /// How many one-wound castings of <paramref name="spell"/>'s tradition <paramref name="c"/>
        /// could still pay for from what they have: their native pool — Mind or Soul to the last
        /// point, Body only down to 1 — and Body down to 1 again if they can Bleed for Power (Core
        /// Rules §4, "No Spending Yourself to Death"; §11). Elemental Fuel is reckoned as a couple.
        /// </summary>
        public static int CastsLeft(Combatant c, Rules.Models.SpellDefinition spell)
        {
            if (c.MagicAccess.FirstOrDefault(a => a.Source == spell.Source) is not { } access)
            {
                return 0;
            }

            if (spell.Source == MagicSource.ElementalMagic)
            {
                return 2;
            }

            var native = access.NativeCostTrack switch
            {
                WoundType.Mind => c.Mind,
                WoundType.Soul => c.Soul,
                _ => c.Body - 1,
            };
            var bled = access.CanBleedForPower && access.NativeCostTrack is WoundType.Mind or WoundType.Soul ? c.Body - 1 : 0;
            return Math.Max(0, native) + Math.Max(0, bled);
        }

        /// <summary>How much casting <paramref name="c"/> has left, 0 to 1: full with two or more castings still payable.</summary>
        public static double CastingLeft(Combatant c) =>
            c.Spells.Count == 0 ? 0 : Math.Min(1, c.Spells.Max(s => CastsLeft(c, s)) / 2.0);

        /// <summary>
        /// What <paramref name="amount"/> Body put back on <paramref name="c"/> is worth: how much it lowers
        /// the chance they drop before they act again — the blows coming at them against what they have
        /// left — times what they are worth standing, and a little for the longer fight. A wound mended
        /// on somebody the next blow drops anyway, or nobody can reach, changes little.
        /// </summary>
        public double MendWorth(Combatant c, double amount)
        {
            var incoming = 1.5 * Exposure(c, c.Position) / Math.Max(1e-6, PerWound(c));
            double Drops(double body) => body <= 0 ? 1 : Math.Min(1, incoming / body);
            return Value(c) * (Drops(c.Body) - Drops(c.Body + amount)) + 0.3 * amount * Value(c) / Math.Max(1, c.BodyMax);
        }

        /// <summary>
        /// What spending a tactical Stamina now costs <paramref name="c"/> besides the Stamina: where it is
        /// the round's last, the extra action or the panic dodge it rules out — about half a blow.
        /// </summary>
        public static double SpendPrice(Combatant c, double blow) =>
            c.TacticalSpendsPerRound - c.TacticalSpendsThisRound <= 1 ? 0.5 + 0.5 * Math.Max(0, blow) : 0.5;

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
                // An area spell lands on more than one: about as many as a caster can count on catching.
                var caught = spell.Target == SpellTarget.Many ? AreaCatch : 1;
                best = Math.Max(best, Odds.Of(bonus, e.DodgeThreshold).Success * Math.Max(0, dice - absorbs) * caught);
            }

            return best;
        }

        /// <summary>How many an area spell is reckoned to catch, for what its caster is worth.</summary>
        public const double AreaCatch = 1.8;

        /// <summary>
        /// What <paramref name="c"/> does for its own side in a turn besides harm, in the same wounds'
        /// worth: a Heal's dice put back on whoever needs them — keeping the fighters standing to
        /// hit — and a Control, Buff, Bane or Bind's turns taken from the other side. A healer is
        /// worth putting down for the wounds they'd undo.
        /// </summary>
        public double Support(Combatant c)
        {
            if (support.TryGetValue(c, out var known))
            {
                return known;
            }

            // A caster shows what they are by casting; until then they're reckoned like anybody else.
            if (!c.HasCastSpell)
            {
                support[c] = 0;
                return 0;
            }

            double best = 0;
            var hurtAllies = view.AlliesOf(c).Count(a => a != c && a.Body < a.BodyMax);
            // Only what they can still pay for: a healer on 1 Body heals nobody with a Body-paid spell.
            foreach (var spell in c.Spells.Where(s => CastsLeft(c, s) > 0))
            {
                var worth = spell.SpellType switch
                {
                    SpellType.Heal => (SpellCalculator.HealHarmDiceCount(spell.SpellTier) * (DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target)) + 1) / 2.0
                                       + (c.Build is { } b ? SpellCalculator.GraceBonus(b, spell) : 0))
                                      * (spell.Target == SpellTarget.Many ? AreaCatch : 1) * (hurtAllies > 0 ? 1 : 0.6),
                    SpellType.Control => 1.5,
                    SpellType.Buff or SpellType.Bane or SpellType.Motion => 0.8,
                    _ => 0,
                };
                best = Math.Max(best, worth);
            }

            support[c] = best;
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

            // A single blow costs a Swarm exactly 1 wound, whatever is rolled (GM Reference §3).
            if (target.IsSwarm)
            {
                return (odds.Success, odds.Success, target.Body <= 1 ? odds.Success : 0);
            }

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

            // An area attack is weighed by everybody it catches, not as a blow at one (TurnPlanner).
            foreach (var attack in attacker.Attacks.Where(a => a.AreaSquares is null))
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
                    else if (creature.DistanceFrom(square, enemy) <= attack.Reach + stride)
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

                var choices = 1 + view.EnemiesOf(enemy).Count(o => o != creature && o.DistanceTo(enemy) <= enemy.Reach + stride);
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

            foreach (var enemy in view.EnemiesOf(mover).Where(e => e.CanReach(mover) && !e.CanReachBlock(to, mover.Across) && BattleView.TacticalSpendRefusal(e) is null))
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
        /// <param name="topples">A Shove or a Trip — which an Unshakable target meets at the attacker's Disadvantage.</param>
        public double ControlChance(Combatant actor, Combatant target, bool againstDodgeToo = false, bool topples = false)
        {
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var threshold = againstDodgeToo ? Math.Max(target.DodgeThreshold, target.StabilityThreshold) : target.StabilityThreshold;
            return Odds.Of(actor.UnarmedAttackModifier, threshold + answer, actor.IsHelped,
                actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried || (topples && target.IsUnshakable) || target.IsSolo).Success;
        }

        /// <summary>
        /// The chance a manoeuvre declared with <paramref name="with"/> takes: one total that must hit
        /// the Dodge Threshold and meet <paramref name="threshold"/> too — each with the +2 the target
        /// can buy after seeing it, where it has the Stamina.
        /// </summary>
        public double ManoeuvreChance(Combatant actor, AttackProfile with, Combatant target, GridPosition from, int threshold)
        {
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var plan = view.PlanAttackFrom(actor, with, from, target);
            var dodge = plan.CanAttack ? plan.DodgeThreshold : target.DodgeThreshold;
            return Odds.Of(with.AttackModifier, Math.Max(dodge, threshold + answer), actor.IsHelped,
                actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried).Success;
        }

        /// <summary>The chance a Sunder misses only because of the shield over <paramref name="target"/> — and still meets its Stability Threshold — splitting the shield.</summary>
        public double ShieldBreakChance(Combatant actor, AttackProfile with, Combatant target, GridPosition from)
        {
            var plan = view.PlanAttackFrom(actor, with, from, target);
            var dodge = plan.CanAttack ? plan.DodgeThreshold : target.DodgeThreshold;
            var disadvantage = actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried;
            var reachesBare = Odds.Of(with.AttackModifier, Math.Max(dodge - target.ShieldBonus, target.StabilityThreshold), actor.IsHelped, disadvantage).Success;
            var hits = Odds.Of(with.AttackModifier, Math.Max(dodge, target.StabilityThreshold), actor.IsHelped, disadvantage).Success;
            return Math.Max(0, reachesBare - hits);
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
