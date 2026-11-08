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

    /// <summary>
    /// How much a brain knows of the other side (the GM knowledge dial — the user's direction,
    /// 2026-11-02: the GM may cheat on knowledge, never on dice, and how much it knows scales with
    /// the challenge).
    /// </summary>
    public enum Knowledge
    {
        /// <summary>
        /// Only what the fight has shown: each Threshold guessed and narrowed by the rolls made
        /// against it, and a caster known once they cast.
        /// </summary>
        Seen,

        /// <summary>The character sheets: every Threshold true, and who can cast before they do.</summary>
        Sheets,
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

        public Assessor(BattleView view, Knowledge knows = Knowledge.Sheets, Side? side = null)
        {
            this.view = view;
            Knows = knows;
            this.side = side;
        }

        private readonly Side? side;

        /// <summary>
        /// What this brain knows of the other side (the GM knowledge dial, 2026-11-02): their
        /// sheets, or only what it has seen in the fight. A brain with no side named reads every
        /// Threshold true and counts a caster once they've shown themselves, as brains always have.
        /// </summary>
        public Knowledge Knows { get; }

        /// <summary>Whether this brain reads <paramref name="c"/>'s sheet — its own side's, or anybody's at <see cref="Knowledge.Sheets"/>.</summary>
        private bool Reads(Combatant c) => side is null || Knows == Knowledge.Sheets || c.Side == side;

        /// <summary>
        /// <paramref name="c"/>'s <paramref name="threshold"/> as this brain believes it, given
        /// <paramref name="actual"/> — what it faces now, everything declared on top of the sheet
        /// included. Where it hasn't read the sheet, the sheet value is a guess (11 + Tier + a
        /// middling +1) held inside what the rolls so far have shown.
        /// </summary>
        public int Believed(Combatant c, ThresholdType threshold, int actual)
        {
            if (Reads(c) || Encounter.SheetThreshold(c, threshold) is not { } sheet)
            {
                return actual;
            }

            return actual + Guess(c, threshold) - sheet;
        }

        /// <summary>What this brain takes <paramref name="c"/>'s sheet <paramref name="threshold"/> to be, from what it has seen.</summary>
        public int Guess(Combatant c, ThresholdType threshold)
        {
            // What the kit says (the user, 2026-11-03): heavy armour is a fighter's, and a fighter's
            // Strength; a body in little or none is likelier quick, or a caster's sharp mind.
            var guess = 12 + (int)c.Tier + threshold switch
            {
                ThresholdType.Stability => c.Armour switch { ArmourWeight.Heavy => 1, ArmourWeight.None => -1, _ => 0 },
                ThresholdType.Resolve => Softness(c) >= 0.75 ? 1 : c.Armour == ArmourWeight.Heavy ? -1 : 0,
                ThresholdType.Dodge => c.Armour == ArmourWeight.Light ? 1 : 0,
                _ => 0,
            };
            if (view.SeenBounds(c, threshold) is var (min, max))
            {
                guess = min > max ? min : Math.Clamp(guess, min, max);
            }

            return guess;
        }

        public int Dodge(Combatant c) => Believed(c, ThresholdType.Dodge, c.DodgeThreshold);

        public int Stability(Combatant c) => Believed(c, ThresholdType.Stability, c.StabilityThreshold);

        public int? Resolve(Combatant c) => c.ResolveThreshold is { } resolve ? Believed(c, ThresholdType.Resolve, resolve) : null;

        public int? Fortitude(Combatant c) => c.FortitudeThreshold is { } fortitude ? Believed(c, ThresholdType.Fortitude, fortitude) : null;

        /// <summary>What an attack <paramref name="plan"/> on <paramref name="target"/> is believed to roll against.</summary>
        public int PlanThreshold(Combatant target, AttackPlan plan) =>
            Believed(target, plan.Profile?.Against ?? ThresholdType.Dodge, plan.DodgeThreshold);

        /// <summary>
        /// Whether this brain counts <paramref name="c"/> a caster: once they've shown it by casting
        /// or been sensed — or, reading the other side's sheets, before they've cast at all.
        /// </summary>
        public bool KnownCaster(Combatant c) =>
            !c.IsDormantCaster && (c.ShowsAsCaster || (side is not null && Knows == Knowledge.Sheets && c.Side != side && c.Spells.Count > 0));

        /// <summary>
        /// How soft <paramref name="c"/> looks to somebody who hasn't read its sheet, 0 to 1: by the
        /// armour it wears, less for a shield or a second blade. Heavy armour is almost certainly a
        /// fighter; little or none, and few weapons, is the look of a caster — though not always.
        /// </summary>
        public static double Softness(Combatant c)
        {
            var soft = c.Armour switch
            {
                ArmourWeight.None => 1.0,
                ArmourWeight.Light => 0.75,
                ArmourWeight.Medium => 0.4,
                _ => 0.1,
            };
            soft -= c.InHandShield is not null ? 0.2 : 0;
            soft -= c.OffHandWeapon is not null ? 0.15 : 0;
            return Math.Clamp(soft, 0, 1);
        }

        /// <summary>
        /// How likely this brain reckons <paramref name="c"/> a caster who hasn't yet shown it — 0 for
        /// one it knows either way (its own side, a read sheet, a cast seen), otherwise by the look of them.
        /// </summary>
        public double SuspectedCaster(Combatant c) =>
            Reads(c) || c.ShowsAsCaster || c.IsDormantCaster ? 0 : 0.6 * Softness(c);

        /// <summary>Whether this brain knows what spells <paramref name="c"/> could cast at it.</summary>
        public bool KnowsSpells(Combatant c) => Reads(c) || c.ShowsAsCaster;

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
        /// <summary>
        /// Whose eyes the options being scored are seen through: a phantom is worth to it only as
        /// much as it believes the thing real. Null weighs everything as it seems.
        /// </summary>
        public Combatant? Observer { get; set; }

        public double Value(Combatant c) =>
            BaseValue(c) * (Observer is { } observer && c.IsPhantom ? view.BelievedReal(observer, c) : 1);

        private double BaseValue(Combatant c)
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
        /// nothing left to pay for a spell is just another body, and one dropped and brought back is
        /// no caster at all until they cast again — downed, they're out, cleaned up at the end (the
        /// user's direction, 2026-11-02 and 2026-11-03 — for every brain, not only a Solo's).
        /// </summary>
        private double RawValue(Combatant c) => ReckonedValue(c);

        /// <summary>
        /// The harm and help <paramref name="c"/> does in a turn: as measured, where this round was
        /// (<see cref="Measured"/>), blended with what the sheet and the board say it should do.
        /// </summary>
        private double TurnHarm(Combatant c)
        {
            var reckoned = Threat(c) + Support(c);
            return Measured is { } measured && measured.TryGetValue(c, out var perTurn)
                ? (1 - MeasuredWeight) * reckoned + MeasuredWeight * perTurn
                : reckoned;
        }

        /// <summary>
        /// What each enemy was seen to do per turn of its own in games played forward from this round's start,
        /// in wounds dealt and healed (<see cref="UtilityGmBrain.MeasuredHarm"/>) — the harm they
        /// actually do, where the reckoning below can only assume it. Null where nobody measured.
        /// </summary>
        public IReadOnlyDictionary<Combatant, double>? Measured { get; init; }

        /// <summary>How much of a measured creature's worth the measurement sets, 0 to 1.</summary>
        public double MeasuredWeight { get; init; }

        /// <summary>
        /// A creature's worth: the harm it does in a turn (<see cref="TurnHarm"/>) over the turns it
        /// would last, and the doctrine on top — casters first, then whatever shoots.
        /// </summary>
        private double ReckonedValue(Combatant c)
        {
            var value = 0.5 + TurnHarm(c) * Lasts(c);
            if (KnownCaster(c))
            {
                value *= 1 + 1.5 * CastingLeft(c) * Spread(c);
            }
            else if (c.HasRangedAttack && !c.IsDefenceless)
            {
                // Something that shoots is the next thing after a caster (the user, 2026-11-06): the
                // front line can stand between the back line and a blade, never an arrow, so it picks
                // its victim wherever the fight is — worth a premium wherever there's a back line to reach.
                value *= 1 + RangedPremium * Spread(c, ranged: true);
            }
            else if (SuspectedCaster(c) is var suspected and > 0)
            {
                // Not known yet, only guessed from the look of them: worth a share of a caster's
                // premium, taken at a middling pool and reach since neither can be seen.
                value *= 1 + 1.5 * 0.5 * suspected;
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
        private double Spread(Combatant c, bool ranged = false)
        {
            var area = ranged || c.Spells.Any(s => s.Target == SpellTarget.Many && s.SpellType is SpellType.Harm or SpellType.Control && CastsLeft(c, s) > 0);
            var targets = view.EnemiesOf(c).Count(e => !e.IsPhantom && !e.IsOutOfFight);
            return area ? Math.Min(1, (targets - 1) / 3.0) : 0;
        }

        /// <summary>The share added to the worth of something that shoots, at the most (<see cref="RawValue"/>).</summary>
        public const double RangedPremium = 0.5;

        /// <summary>
        /// How much more <paramref name="actor"/> wants <paramref name="target"/> than its worth alone
        /// says. Worth already puts casters first, then whatever shoots, then the rest (the user,
        /// 2026-11-06, for both sides) — "unless already engaged": a creature standing in melee with
        /// an enemy fights what is in front of it rather than breaking off for the back line. On top
        /// of that, a character's training: a Nimble one closes on whatever shoots or casts, using
        /// their speed to get there first; a Martial one takes on the heavy hitters they're built to
        /// hold.
        /// </summary>
        public static double Doctrine(Combatant actor, Combatant target)
        {
            if (target.IsDefenceless)
            {
                return 1;
            }

            var engaged = !actor.IsTiny && (actor.CanReach(target) || target.CanReach(actor)) && !target.HasRangedAttack && target.Spells.Count == 0
                ? Engaged : 1;
            if (actor.Side != Side.Party)
            {
                return engaged;
            }

            if (actor.IsNimble && (target.HasRangedAttack || target.Spells.Count > 0))
            {
                return Math.Max(engaged, 1.3);
            }

            return Math.Max(engaged, actor.IsMartial && target.Role is AdversaryRole.Brute or AdversaryRole.Juggernaut or AdversaryRole.Solo ? 1.2 : 1);
        }

        /// <summary>How much more a creature wants the enemy it's already in melee with (<see cref="Doctrine"/>).</summary>
        public const double Engaged = 1.5;

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
                    .Select(a => Blow(e, a, c, a.DamageDieSides, Dodge(c)).ExpectedDealt)
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

        /// <summary>
        /// What one wound to <paramref name="c"/> is worth: its value spread over the wounds it can take — a Minion has one.
        /// A GM short of ruthless (<see cref="Ruthlessness"/>) holds a wound to a character already hurt as worth
        /// less, so its harm spreads instead of piling on whoever is nearest to dropping.
        /// </summary>
        public double PerWound(Combatant c)
        {
            var perWound = Value(c) / Math.Max(1, c.IsMinion ? 1 : c.Body);
            var mercy = 1 - Mercy(c);
            if (mercy <= 0 || c.IsMinion || c.BodyMax <= 0)
            {
                return perWound;
            }

            var healthy = Math.Clamp((double)c.Body / c.BodyMax, 0, 1);
            return perWound * (1 - mercy + mercy * healthy * healthy);
        }

        /// <summary>What dropping <paramref name="target"/> is worth on top of the wounds, at a chance <paramref name="kill"/>: less to a merciful GM.</summary>
        public double KillWorth(Combatant target, double kill) => 0.3 * kill * Value(target) * Mercy(target);

        /// <summary>
        /// How hard this brain plays for the kill, 0 to 1 — the GM's intent, not its cleverness (the
        /// user's ruling, 2026-11-08). At 1 it focus-fires and finishes the dying; lower, it spreads its
        /// harm, cares less for dropping anyone, buys fewer extra actions, and under
        /// <see cref="SparesTheDownedBelow"/> leaves the dying alone. Every move is still the best one for that intent.
        /// </summary>
        public double Ruthlessness { get; init; } = 1;

        /// <summary>The ruthlessness under which a GM leaves a dying character alone.</summary>
        public const double SparesTheDownedBelow = 0.5;

        /// <summary>
        /// Whether this GM finishes the dying: only at its most extreme, Merciless (<see cref="GmIntent.Merciless"/>).
        /// Below that it lays at most one blow on each, one that can't kill (the user's ruling, 2026-11-08).
        /// </summary>
        public bool FinishesTheDowned { get; init; }

        /// <summary>The downed characters this GM has already laid its one blow on, this fight.</summary>
        public IReadOnlySet<Combatant>? Menaced { get; init; }

        /// <summary>
        /// What one blow on a downed character that can't kill them is worth at full ruthlessness: a little
        /// fear for the table, about a third of a middling blow at someone standing, so it is chosen only
        /// when little better is in reach.
        /// </summary>
        public const double MenaceWorth = 0.4;

        /// <summary>
        /// How hard this brain piles on <paramref name="target"/>, 0 to 1: only a GM's foes are spared anything.
        /// It goes as the square of the ruthlessness, because sims (2026-11-08) found the top half of a straight dial did nothing.
        /// </summary>
        public double Mercy(Combatant target) => side == Side.Adversaries && target.Side != Side.Adversaries ? Pressing : 1;

        /// <summary>How hard this GM presses, from its ruthlessness: <see cref="Mercy"/>'s curve.</summary>
        public double Pressing => Math.Pow(Math.Clamp(Ruthlessness, 0, 1), 2);

        /// <summary>The harm <paramref name="c"/> does in a turn to the enemy it would best hit, wherever they stand.</summary>
        public double Threat(Combatant c)
        {
            if (threat.TryGetValue(c, out var known))
            {
                return known;
            }

            var best = view.EnemiesOf(c)
                .SelectMany(e => c.Attacks.Select(a => Blow(c, a, e, a.DamageDieSides, Dodge(e)).ExpectedDealt).Append(SpellHarm(c, e)))
                .DefaultIfEmpty(0)
                .Max();

            // A second weapon hurts more than one: the off-hand blow paid in Stamina every round it can
            // be (a character's or a rival's), and a statblock's only when the GM spends an Opportunity
            // on it — about half the time.
            var paysStamina = c.OffHandWeapon is not null;
            if (c.OffHandAttack is { } offHand && (paysStamina ? c.Stamina >= 2 : c.OpportunityBonus.Kind == OpportunityBonusKind.OffHand))
            {
                var second = view.EnemiesOf(c).Select(e => Blow(c, offHand, e, offHand.DamageDieSides, Dodge(e)).ExpectedDealt).DefaultIfEmpty(0).Max();
                best += (paysStamina ? 1 : 0.5) * second;
            }

            threat[c] = best;
            return best;
        }

        /// <summary>
        /// What the best Harm on <paramref name="c"/>'s sheet or statblock would do to <paramref name="e"/>
        /// on average — the chance the cast lands, times its dice after armour where it's Body. A
        /// caster's threat is its spells, however feeble the knife in its other hand.
        /// </summary>
        private double SpellHarm(Combatant c, Combatant e) => KnowsSpells(c) && !c.IsDormantCaster ? SpellHarm(c, e, Dodge(e), Cover.None, AreaCatch) : 0;

        /// <summary>
        /// As <see cref="SpellHarm(Combatant, Combatant)"/>, on <paramref name="e"/> alone at
        /// <paramref name="dodge"/>, behind <paramref name="cover"/>: an area spell, reckoned a Volume,
        /// ignores it; one cast at one has partial to beat and can't be cast through full.
        /// </summary>
        private static double SpellHarm(Combatant c, Combatant e, int dodge, Cover cover, double areaCatch = 1)
        {
            double best = 0;
            foreach (var spell in c.Spells.Where(s => s.SpellType == SpellType.Harm))
            {
                var area = spell.Target == SpellTarget.Many;
                if (!area && cover == Cover.Full)
                {
                    continue;
                }

                var bonus = c.Build is { } build
                    ? c.MagicAccess.FirstOrDefault(a => a.Source == spell.Source) is { } access ? CharacterCalculator.CastBonus(build, access.CastAbility) : 0
                    : c.StatblockCastBonus;
                var dice = SpellCalculator.HealHarmDiceCount(spell.SpellTier) * (DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target)) + 1) / 2.0
                           + Encounter.GraceOf(c, spell);
                var absorbs = (spell.DamageType ?? WoundType.Body) == WoundType.Body
                    ? InventoryCalculator.ArmourAbsorbedAgainst(e.Armour, WoundType.Body, isDirect: false)
                    : 0;
                // An area spell lands on more than one: about as many as a caster can count on catching.
                var caught = area ? areaCatch : 1;
                var against = dodge + (!area && cover == Cover.Partial ? 2 : 0);
                best = Math.Max(best, Odds.Of(bonus, against).Success * Math.Max(0, dice - absorbs) * caught);
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
            if (!KnownCaster(c))
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
                                       + Encounter.GraceOf(c, spell))
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
        /// <param name="woundsAlready">Wounds the target is reckoned to have taken already, earlier in the same exchange — a second reaction lands on what the first left.</param>
        public (double Hit, double ExpectedDealt, double Kill) Blow(Combatant attacker, AttackProfile attack, Combatant target, int? dieSides, int dodgeThreshold,
            bool advantage = false, bool disadvantage = false, bool precise = false, int woundsAlready = 0, int extraDamage = 0)
        {
            // An assessor lives for one decision, over which nobody's state moves: the same blow is
            // asked for from every square weighed, and worked out once.
            var key = new BlowKey(attacker, attack, target, dieSides, dodgeThreshold, advantage, disadvantage, precise, woundsAlready, extraDamage);
            if (!blows.TryGetValue(key, out var blow))
            {
                blows[key] = blow = WorkOutBlow(attacker, attack, target, dieSides, dodgeThreshold, advantage, disadvantage, precise, woundsAlready, extraDamage);
            }

            return blow;
        }

        private readonly record struct BlowKey(Combatant Attacker, AttackProfile Attack, Combatant Target, int? DieSides, int Dodge,
            bool Advantage, bool Disadvantage, bool Precise, int WoundsAlready, int ExtraDamage);

        private readonly Dictionary<BlowKey, (double Hit, double ExpectedDealt, double Kill)> blows = new();

        private (double Hit, double ExpectedDealt, double Kill) WorkOutBlow(Combatant attacker, AttackProfile attack, Combatant target, int? dieSides, int dodgeThreshold,
            bool advantage, bool disadvantage, bool precise, int woundsAlready, int extraDamage)
        {
            advantage |= attacker.IsHelped || target.IsOpenToBlows || attacker.HoldsPatient;
            precise |= attacker.HoldsPrecise;
            disadvantage |= attacker.IsStandingGround || attacker.IsOffGuard || attacker.IsHarried;

            // A defender who can still spend will raise their DT against a blow it would turn — by a
            // panic dodge only if nobody has hold of them (the user's ruling, 2026-11-07).
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1
                ? Math.Max(target.HeldBy is null ? ThresholdModifierCalculator.ReactionBonus : 0, target.InHandShield is { } shield && target.ShieldBonus == 0
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
            var modifier = attack.DamageModifier + extraDamage + (precise && attacker.Build is { } build ? ExploitCalculator.PreciseDamageBonus(build) : 0);
            var (plainDealt, plainKill) = Damage(attack.DamageDiceCount, dieSides, modifier, bonusDie: null, absorbs, target, attack.Edge, woundsAlready);
            var (critDealt, critKill) = Damage(attack.DamageDiceCount, dieSides, modifier, bonusDie: attack.DamageDieSides, absorbs, target, attack.Edge, woundsAlready);

            return (odds.Success,
                plainHit * plainDealt + odds.CriticalSuccess * critDealt + PoisonDealt(attack, target, dodgeThreshold + answer, absorbs, advantage, disadvantage),
                plainHit * plainKill + odds.CriticalSuccess * critKill);
        }

        /// <summary>
        /// What a poison carried on the blow adds, on average (Core Rules §10, "Fortitude Threshold"): it
        /// takes hold where the same total hits and meets the target's FT, and the blow broke the skin —
        /// its dice Direct on its track. Mind or Soul wounds count a little under Body's. Nothing for a
        /// target with no living body.
        /// </summary>
        private double PoisonDealt(AttackProfile attack, Combatant target, int dodge, int absorbs, bool advantage, bool disadvantage)
        {
            if (attack.Poison is not { } poison || Fortitude(target) is not { } fortitude)
            {
                return 0;
            }

            var lands = Odds.Of(attack.AttackModifier, Math.Max(dodge, fortitude), advantage, disadvantage).Success;
            var breaksSkin = absorbs <= 0 ? 1 : Math.Clamp((attack.DamageModifier + attack.DamageDiceCount * (attack.DamageDieSides + 1) / 2.0 - absorbs) / 2.0, 0, 1);
            var average = Math.Max(PlayStateCalculator.DamageFloor, poison.DiceCount * (poison.DieSides + 1) / 2.0 + poison.Modifier);
            return lands * breaksSkin * average * (poison.Track == WoundType.Body ? 1 : 0.7);
        }

        /// <summary>
        /// The wounds a hit deals after armour, on average, and the chance that's enough to drop the
        /// target. A critical rolls the maximum on its dice and adds the weapon's base die (Core
        /// Rules §10). A Minion is dropped by any wound that gets through.
        /// </summary>
        private static (double Dealt, double Kill) Damage(int count, int? sides, int modifier, int? bonusDie, int absorbs, Combatant target, WeaponEdge? edge = null, int woundsAlready = 0)
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
                var through = Encounter.ByEdge(edge, target, rolled, absorbs).Dealt;
                dealt += through;
                kills += Drops(target, through, woundsAlready) ? 1 : 0;
                outcomes++;
            }

            return (dealt / outcomes, kills / outcomes);
        }

        private static bool Drops(Combatant target, int through, int woundsAlready = 0) =>
            through > 0 && (target.IsMinion || through >= target.Body - woundsAlready || target.IsDefenceless && target.Body - 1 <= PlayStateCalculator.MinBodyWounds);

        /// <summary>Every way <paramref name="dice"/> can fall, summed — worked out once for each set of dice.</summary>
        private static int[] Sums(IReadOnlyList<int> dice) =>
            SumsMade.GetOrAdd(string.Join(',', dice), _ => EnumerateSums(dice).ToArray());

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int[]> SumsMade = new();

        private static IEnumerable<int> EnumerateSums(IReadOnlyList<int> dice)
        {
            if (dice.Count == 0)
            {
                yield return 0;
                yield break;
            }

            foreach (var rest in EnumerateSums(dice.Skip(1).ToList()))
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
        /// <param name="precise">Direct — and, for a character, Vitality on the damage. A statblock has no Vitality, so for one it is Direct alone.</param>
        /// <param name="extraDamage">Added to each hit's damage: a Precise Opportunity bonus's Tier baseline.</param>
        public (double Worth, AttackProfile? With) BestAttack(Combatant attacker, GridPosition from, Combatant target, bool advantage = false, bool precise = false, int extraDamage = 0)
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

                var blow = Blow(attacker, attack, target, plan.DamageDieSides, PlanThreshold(target, plan),
                    advantage || view.PackMateOf(attacker, target) is not null, precise: precise, extraDamage: extraDamage);
                var worth = (attack.DealsNoDamage ? 0 : Worth(blow, target))
                            + (attack.Imposes is { } bound ? blow.Hit * BindWorth(attacker, target, bound) : 0);
                if (worth > best.Worth)
                {
                    best = (worth, attack);
                }
            }

            return best;
        }

        /// <summary>
        /// What laying <paramref name="bound"/> on <paramref name="target"/> until the attacker's next
        /// turn is worth, before the chance it lands: the turn it denies, in wounds' worth (Core Rules
        /// §11, "Motion Spells"). Frozen denies the whole of it; Stuck, nearly all of it to a creature
        /// that has to move to reach anybody and little to one already in reach; Slowed, a part.
        /// </summary>
        public double BindWorth(Combatant actor, Combatant target, CharacterCondition bound)
        {
            if (target.IsOutOfFight || target.Conditions.Contains(bound) || target.Conditions.Contains(CharacterCondition.Frozen))
            {
                return 0;
            }

            var victims = view.EnemiesOf(target).Where(e => !e.IsOutOfFight).ToList();
            var perWound = victims.Count == 0 ? 0 : victims.Average(PerWound);
            var turn = Threat(target) * perWound;
            var mustMove = !target.HasRangedAttack && !victims.Any(v => target.CanReach(v));
            return bound switch
            {
                CharacterCondition.Frozen => turn + HeldOpenWorth(actor, target),
                CharacterCondition.Stuck => (mustMove ? 0.8 : 0.1) * turn,
                _ => mustMove ? 0.3 * turn : 0,
            };
        }

        /// <summary>
        /// What holding <paramref name="target"/> beside <paramref name="holder"/> is worth, before
        /// the chance it lands (Core Rules §9, "Held"): the turn it would have spent on someone it
        /// would rather hit than whoever is in its reach now, in the difference between the two. A
        /// fighter pinned to a Revenant is a fighter not reaching the caster behind it. Where there is
        /// nobody softer to keep it from, a little for the ground it can no longer give or take.
        /// </summary>
        public double PinWorth(Combatant holder, Combatant target)
        {
            if (target.IsOutOfFight || target.HasRangedAttack)
            {
                return 0.1 * Value(target);
            }

            var victims = view.EnemiesOf(target).Where(e => !e.IsOutOfFight && !e.IsPhantom).ToList();
            // What it would make of each, as it would choose: the blow's worth, weighed by its doctrine.
            double Want(Combatant v, GridPosition from) => BestAttack(target, from, v).Worth * Doctrine(target, v);
            // Held, it can always strike its holder, who has to be beside it to keep hold — and who is
            // then the foe it's engaged with.
            var atHolder = Worth(Blow(target, target.Attack, holder, target.Attack.DamageDieSides, Dodge(holder)), holder) * Engaged;
            var here = victims.Where(v => target.CanReach(v)).Select(v => Want(v, target.Position)).Append(atHolder).Max();
            var away = victims.Where(v => !target.CanReach(v))
                .Select(v => view.StrikeSquare(target, v) is { } square ? Want(v, square) : 0).DefaultIfEmpty(0).Max();
            return 0.1 * Value(target) + Math.Max(0, away - here) + NoDodgeWorth(holder, target);
        }

        /// <summary>
        /// What a hold hands the holder's side against a target that can no longer panic dodge (the
        /// user's ruling, 2026-11-07): the +2 it would have bought taken off every blow from those
        /// near enough to strike it before it gets loose. Nothing where it couldn't have dodged anyway,
        /// or carries a shield that would do the same.
        /// </summary>
        private double NoDodgeWorth(Combatant holder, Combatant target)
        {
            if (BattleView.TacticalSpendRefusal(target) is not null || target.Stamina <= 1
                || (target.InHandShield is { } shield && target.ShieldBonus == 0
                    && Rules.Calculation.InventoryCalculator.ShieldDodgeThresholdBonus(shield.Weight) >= ThresholdModifierCalculator.ReactionBonus))
            {
                return 0;
            }

            double gain = 0;
            var dodge = Dodge(target);
            foreach (var ally in view.AlliesOf(holder).Append(holder).Distinct().Where(a => !a.IsOutOfFight && !a.IsPhantom))
            {
                // Those who'd be at it in melee: a hold keeps a fighter beside its holder, where the
                // blades are, and an archer's shot is better spent where it was going anyway.
                var stride = view.SquaresFor(ally.Speed, 1);
                if (ally.DistanceTo(target) > stride + ally.Reach || ally.HasRangedAttack)
                {
                    continue;
                }

                var attack = ally.Attack;
                gain += Worth(Blow(ally, attack, target, attack.DamageDieSides, dodge - ThresholdModifierCalculator.ReactionBonus), target)
                        - Worth(Blow(ally, attack, target, attack.DamageDieSides, dodge), target);
            }

            return Math.Max(0, gain);
        }

        /// <summary>
        /// What holding <paramref name="target"/> Frozen hands the holder's side besides its lost turn:
        /// Advantage on every blow at it until it breaks free (the user, 2026-11-06), from each of
        /// them near enough to strike it in the meantime.
        /// </summary>
        private double HeldOpenWorth(Combatant actor, Combatant target)
        {
            double gain = 0;
            foreach (var ally in view.AlliesOf(actor).Where(a => !a.IsOutOfFight && !a.IsPhantom))
            {
                var stride = view.SquaresFor(ally.Speed, 1);
                if (ally.DistanceTo(target) > stride + ally.Reach && !ally.HasRangedAttack)
                {
                    continue;
                }

                var attack = ally.Attack;
                var dodge = Dodge(target);
                gain += Worth(Blow(ally, attack, target, attack.DamageDieSides, dodge, advantage: true), target)
                        - Worth(Blow(ally, attack, target, attack.DamageDieSides, dodge), target);
            }

            return gain;
        }

        /// <summary>
        /// What a blow is worth: the wounds it deals at what each is worth to the target's side, and
        /// a little more for dropping the target outright, all at once.
        /// </summary>
        public double Worth((double Hit, double ExpectedDealt, double Kill) blow, Combatant target) =>
            blow.ExpectedDealt * PerWound(target) + KillWorth(target, blow.Kill);

        /// <summary>
        /// The blows <paramref name="creature"/> invites standing on <paramref name="square"/> until
        /// its next turn, in wounds' worth: each enemy that could strike it there — a move and a
        /// blow, or a shot from where it stands — shared out among everybody on its side that enemy
        /// could choose instead.
        /// </summary>
        public double Exposure(Combatant creature, GridPosition square, int extraDodge = 0)
        {
            var key = (creature, square, extraDodge, creature.IsShowingFromCover);
            if (!exposures.TryGetValue(key, out var exposure))
            {
                exposures[key] = exposure = WorkOutExposure(creature, square, extraDodge);
            }

            return exposure;
        }

        private readonly Dictionary<(Combatant, GridPosition, int, bool), double> exposures = new();

        private double WorkOutExposure(Combatant creature, GridPosition square, int extraDodge)
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
                            (dodge, die) = (PlanThreshold(creature, shot), shot.DamageDieSides);
                        }
                    }
                    else if (creature.DistanceFrom(square, enemy) <= attack.Reach + stride)
                    {
                        (dodge, die) = (Dodge(creature) + view.VisibilityDodgeModifier(creature, square), attack.DamageDieSides);
                    }

                    if (dodge is { } dt)
                    {
                        best = Math.Max(best, Blow(enemy, attack, creature, die, dt + extraDodge).ExpectedDealt);
                    }
                }

                // A Harm spell from where it stands: a Ray is stopped by full cover and has partial to
                // beat; a Volume flows round both (Core Rules §9, "Cover").
                if (enemy.Spells.Count > 0 && KnowsSpells(enemy) && enemy.DistanceTo(square) <= view.LongRangeSquares && view.LineOpen(enemy.Position, square))
                {
                    var cover = view.CoverFrom(enemy.Position, creature, square);
                    var dodge = Dodge(creature) + view.VisibilityDodgeModifier(creature, square) + extraDodge;
                    best = Math.Max(best, SpellHarm(enemy, creature, dodge, cover));
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
        public double LeavingCost(Combatant mover, GridPosition to) => LeavingRisk(mover, to).Cost;

        /// <summary>
        /// What leaving the reach of the enemies around <paramref name="mover"/> for <paramref name="to"/>
        /// costs (Core Rules §9, "Leaving an enemy's reach is a trigger"): each enemy whose reach it
        /// leaves, that still has its reaction this round and would spend it, strikes as it goes — one
        /// blow landing on what the last left. <c>Cost</c> is the wounds and the chance of being dropped,
        /// in wounds' worth; <c>DropChance</c> is the chance it never gets where it was going, and so
        /// never does what it moved there to do. An enemy that has spent its reaction this round — or
        /// has no Stamina, or is Ringed — strikes at nobody, and the way out is free of it.
        /// </summary>
        /// <param name="harried">An enemy reckoned Harried by the mover first: its reaction rolls at Disadvantage (Core Rules §9, "Harry").</param>
        public (double Cost, double DropChance) LeavingRisk(Combatant mover, GridPosition to, Combatant? harried = null)
        {
            if (mover.IsSlipping || mover.CanFade)
            {
                return (0, 0);
            }

            double dealt = 0, survives = 1;
            foreach (var enemy in view.EnemiesOf(mover).Where(e => !e.IsBroken && e.CanReach(mover) && !e.CanReachBlock(to, mover.Across)
                                                                   && BattleView.TacticalSpendRefusal(e) is null))
            {
                var plan = view.PlanAttack(enemy, mover);
                if (!plan.CanAttack)
                {
                    continue;
                }

                var blow = Blow(enemy, plan.Profile ?? enemy.Attack, mover, plan.DamageDieSides, PlanThreshold(mover, plan),
                    disadvantage: enemy == harried, woundsAlready: (int)Math.Round(dealt));
                if (!WouldStrikeAtLeaver(enemy, mover, blow))
                {
                    continue;
                }

                dealt += blow.ExpectedDealt;
                survives *= 1 - blow.Kill;
            }

            var dropped = 1 - survives;
            return (dealt * PerWound(mover) + 0.3 * dropped * Value(mover), dropped);
        }

        /// <summary>
        /// Whether <paramref name="enemy"/> would spend its reaction on <paramref name="leaver"/>, as
        /// the brains decide it: the GM for a blow worth 0.4 or more; a party member for one worth the
        /// Stamina, with a Stamina to spare. A blow that would drop the leaver is always taken.
        /// </summary>
        public bool WouldStrikeAtLeaver(Combatant enemy, Combatant leaver, (double Hit, double ExpectedDealt, double Kill) blow)
        {
            if (blow.Kill >= 0.25 && enemy.Stamina >= 2)
            {
                return true;
            }

            var worth = Worth(blow, leaver);
            return enemy.Side == Side.Adversaries
                ? enemy.Stamina >= 2 && worth >= 0.4
                : enemy.Stamina >= 2 && worth > 0.6 + 0.4 * Threat(leaver) * PerWound(enemy);
        }

        /// <summary>The chance a Grab, Shove or Trip lands — against Stability, and the target standing firm if it can.</summary>
        /// <param name="topples">A Shove or a Trip — which an Unshakable target meets at the attacker's Disadvantage.</param>
        public double ControlChance(Combatant actor, Combatant target, bool againstDodgeToo = false, bool topples = false)
        {
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var threshold = againstDodgeToo ? Math.Max(Dodge(target), Stability(target)) : Stability(target);
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
            var dodge = plan.CanAttack ? PlanThreshold(target, plan) : Dodge(target);
            return Odds.Of(with.AttackModifier, Math.Max(dodge, threshold + answer), actor.IsHelped,
                actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried).Success;
        }

        /// <summary>The chance a Sunder misses only because of the shield over <paramref name="target"/> — and still meets its Stability Threshold — splitting the shield.</summary>
        public double ShieldBreakChance(Combatant actor, AttackProfile with, Combatant target, GridPosition from)
        {
            var plan = view.PlanAttackFrom(actor, with, from, target);
            var dodge = plan.CanAttack ? PlanThreshold(target, plan) : Dodge(target);
            var disadvantage = actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried;
            var reachesBare = Odds.Of(with.AttackModifier, Math.Max(dodge - target.ShieldBonus, Stability(target)), actor.IsHelped, disadvantage).Success;
            var hits = Odds.Of(with.AttackModifier, Math.Max(dodge, Stability(target)), actor.IsHelped, disadvantage).Success;
            return Math.Max(0, reachesBare - hits);
        }

        /// <summary>
        /// How much a character minds the blows a choice invites, by what they and the party have
        /// left (the user's direction, 2026-11-05): a player's two priorities are to stay alive and
        /// to keep the party alive. Fresh, with every Stamina and the whole party standing, they'll
        /// take a risk to get the fight done; as their own Body and Stamina and the party's numbers
        /// dwindle, keeping everybody up comes first. An adversary is 1: it is there to challenge the
        /// party, and minds a blow only for the harm it would have done later (<see cref="Value"/>).
        /// </summary>
        public double Caution(Combatant c)
        {
            // A rival is a character on the GM's side, and minds its own and its band's as one does.
            if (!c.DiesLikeACharacter || c.IsSteed || c.IsPhantom)
            {
                return 1;
            }

            var party = view.Combatants.Where(p => p.Side == c.Side && p.DiesLikeACharacter && !p.IsSteed && !p.IsPhantom && !p.HasFled).ToList();
            var standing = party.Count == 0 ? 1 : party.Count(p => !p.IsOutOfFight) / (double)party.Count;
            var partyBody = party.Count == 0 ? 1 : party.Sum(p => Math.Max(0, p.Body)) / (double)Math.Max(1, party.Sum(p => p.BodyMax));
            var body = Math.Max(0, c.Body) / (double)Math.Max(1, c.BodyMax);
            // The last Stamina is never spent — a character starting a turn on none falls unconscious.
            var stamina = c.StaminaMax <= 1 ? 1 : Math.Clamp((c.Stamina - 1) / (double)(c.StaminaMax - 1), 0, 1);
            var left = 0.6 * (0.5 * body + 0.5 * stamina) + 0.4 * (0.5 * standing + 0.5 * partyBody);
            return FreshCaution + (SpentCaution - FreshCaution) * (1 - left);
        }

        /// <summary>A character's caution with everything still in hand.</summary>
        public const double FreshCaution = 0.7;

        /// <summary>A character's caution with nothing left: their own Body and Stamina gone, and the party down.</summary>
        public const double SpentCaution = 1.6;

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
