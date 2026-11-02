using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat.Brains.Utility
{
    /// <summary>
    /// Every spell a caster could cast from a square, and what each is worth to their side — in the
    /// same wounds' worth <see cref="Assessor"/> scores blows in, less what the casting costs them:
    /// a Harm at each enemy, a Volume over each cluster, a Heal for each hurt ally in reach of a
    /// hand, a Buff where blows are coming, a Shove that breaks up a melee.
    /// </summary>
    public sealed class SpellPlanner
    {
        private readonly Assessor assess;

        public SpellPlanner(Assessor assess)
        {
            this.assess = assess;
        }

        private BattleView View => assess.View;

        /// <summary>Who of the caster's side will act before it does again, for the Options being weighed; null when it isn't known.</summary>
        private IReadOnlyCollection<Combatant>? stillToAct;

        /// <param name="checkRules">Casting from where the caster stands now: the turn's own refusals apply.</param>
        /// <param name="stillToAct">The caster's allies who'll act before the caster does again, where the brain knows (a party choosing its order); null reads it off who has acted this round.</param>
        /// <param name="bonusAvailable">The turn may spend a GM Opportunity on the caster's bonus — a free Buff, for a creature whose bonus is one.</param>
        /// <param name="bankFull">The GM's bank is full, so an Opportunity unspent is one wasted.</param>
        public IEnumerable<Option> Options(Combatant actor, GridPosition at, bool checkRules, double care, bool usesTricks,
            IReadOnlyCollection<Combatant>? stillToAct = null, bool bonusAvailable = false, bool bankFull = false)
        {
            this.stillToAct = stillToAct;
            if (actor.Spells.Count == 0)
            {
                yield break;
            }

            var enemies = View.EnemiesOf(actor).ToList();
            foreach (var spell in actor.Spells)
            {
                IEnumerable<(SpellCast Cast, Func<CastPlan, double> Worth)> candidates = spell.SpellType switch
                {
                    SpellType.Harm when spell.Target == SpellTarget.Many => AreaCasts(spell, at, enemies)
                        .Select(cast => (cast, (Func<CastPlan, double>)(plan => HarmWorth(actor, spell, plan)))),
                    SpellType.Harm => enemies
                        .Select(e => (new SpellCast(spell) { Target = e }, (Func<CastPlan, double>)(plan => HarmWorth(actor, spell, plan)))),
                    SpellType.Heal => View.PartyOnBoard.Concat(View.Adversaries)
                        .Where(c => c.Side == actor.Side && c != actor && c.DistanceTo(at) <= 1)
                        .Select(c => (Mend(spell, c), (Func<CastPlan, double>)(plan => HealWorth(actor, spell, plan)))),
                    SpellType.Buff when usesTricks => View.PartyOnBoard.Concat(View.Adversaries)
                        .Where(c => c.Side == actor.Side && !c.IsOutOfFight)
                        .Select(c => (new SpellCast(spell) { Target = c, Threshold = ThresholdType.Dodge }, (Func<CastPlan, double>)(plan => Denial * BuffWorth(plan, care)))),
                    SpellType.Control when usesTricks => enemies.Where(e => e.ResolveThreshold is not null && !View.IsControlled(e))
                        .SelectMany(e => (SpellArea.FixedEffectOf(spell) is { } written ? [written] : new[] { ControlEffect.Fear, ControlEffect.Compulsion })
                            .Select(effect => (new SpellCast(spell) { Target = e, Effect = effect }, (Func<CastPlan, double>)(plan => Denial * ControlWorth(actor, effect, plan))))),
                    SpellType.Illusion when usesTricks => IllusionCasts(actor, spell, at, enemies),
                    SpellType.Bane when usesTricks && spell.Target == SpellTarget.Many => VolumeCorners(spell, enemies.Where(e => !e.IsPhantom))
                        .SelectMany(corner => Held(actor, new SpellCast(spell) { Shape = SpellShape.Volume, Corner = corner }))
                        .Select(cast => (cast, (Func<CastPlan, double>)(plan => plan.Targets.Sum(t => HeldBaneWorth(actor, cast, plan, t, spell.Threshold ?? ThresholdType.Dodge))))),
                    SpellType.Bane when usesTricks => enemies.Where(e => !e.IsPhantom && !e.IsOutOfFight)
                        .SelectMany(e => BaneThresholds(spell, e).SelectMany(threshold => Held(actor, new SpellCast(spell) { Target = e, Threshold = threshold })))
                        .Select(cast => (cast, (Func<CastPlan, double>)(plan => HeldBaneWorth(actor, cast, plan, cast.Target!, cast.Threshold!.Value)))),
                    SpellType.Utility when usesTricks && SpellArea.UtilityKindOf(spell) == UtilityKind.Sensing => SenseCorners(actor, spell, enemies)
                        .Select(corner => (new SpellCast(spell) { Shape = SpellShape.Volume, Corner = corner }, (Func<CastPlan, double>)(plan => SenseWorth(actor, plan)))),
                    SpellType.Utility when usesTricks && SpellArea.LeverOf(spell) == Lever.Light && View.IsUnlit => VolumeCorners(spell, enemies)
                        .Select(corner => (new SpellCast(spell) { Shape = SpellShape.Volume, Corner = corner }, (Func<CastPlan, double>)(plan => LightWorth(actor, spell, plan)))),
                    SpellType.Motion when usesTricks && SpellArea.MotionOf(spell) == MotionKind.Shift => enemies.Where(e => !e.IsBroken)
                        .Select(e => (new SpellCast(spell) { Target = e }, (Func<CastPlan, double>)(plan => Denial * ShiftWorth(actor, plan)))),
                    SpellType.Motion when usesTricks && SpellArea.MotionOf(spell) == MotionKind.Bind && spell.Target == SpellTarget.One => enemies
                        .Where(e => !e.IsBroken && !e.IsPhantom)
                        .Select(e => (new SpellCast(spell) { Target = e }, (Func<CastPlan, double>)(plan => Denial * BindChance(plan) * assess.BindWorth(actor, e, Encounter.BindEffect(spell.SpellTier))))),
                    _ => [],
                };

                foreach (var (cast, worth) in candidates)
                {
                    if (Paid(actor, at, cast, checkRules) is not { } paid)
                    {
                        continue;
                    }

                    var score = worth(paid.Plan) - paid.Price;
                    if (score > 0)
                    {
                        yield return new Option(OptionKind.Cast, score, $"{spell.Name} {Describe(paid.Plan)}", Cast: paid.Cast);
                    }

                    // The same Buff on an ally for no wound cost, on the Opportunity (GM Reference §9): worth
                    // it where the wounds it saves are worth more than the extra turn the Opportunity would buy.
                    if (bonusAvailable && worth(paid.Plan) > 0 && Encounter.FreeBuffRefusal(actor, paid.Cast) is null
                        && (bankFull || paid.Price >= TurnPlanner.BonusPrice))
                    {
                        yield return new Option(OptionKind.Cast, worth(paid.Plan), $"{spell.Name} {Describe(paid.Plan)}, free on the Opportunity",
                            Cast: paid.Cast, WithBonus: true);
                    }
                }
            }
        }

        private static string Describe(CastPlan plan) =>
            plan.Area.Count > 1 ? $"over {string.Join(", ", plan.Targets.Select(t => t.Name))}" : plan.Targets.Count > 0 ? $"at {plan.Targets[0].Name}" : "at nobody";

        /// <summary>A Heal mends whichever track has its target down: a stunned mind before a scratch.</summary>
        private static SpellCast Mend(SpellDefinition spell, Combatant patient) => new(spell)
        {
            Target = patient,
            Restores = patient.IsStunned ? WoundType.Mind : patient.IsNumbed ? WoundType.Soul : WoundType.Body,
        };

        /// <summary>The eight ways a Line or a Cone can be aimed.</summary>
        private static readonly (int X, int Y)[] Aims = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

        /// <summary>
        /// Every way worth weighing to put an area spell down — the Shape is the caster's choice at the
        /// moment of casting (Core Rules §11): each block with an enemy under it, and each Line and Cone
        /// aimed any of the eight ways, starting beside the caster or on an enemy.
        /// </summary>
        private static IEnumerable<SpellCast> AreaCasts(SpellDefinition spell, GridPosition at, IReadOnlyList<Combatant> enemies)
        {
            foreach (var corner in VolumeCorners(spell, enemies))
            {
                yield return new SpellCast(spell) { Shape = SpellShape.Volume, Corner = corner };
            }

            foreach (var aim in Aims)
            {
                var starts = enemies.Where(e => e.DistanceTo(at) <= 12).SelectMany(e => e.Footprint)
                    .Append(new GridPosition(at.X + aim.X, at.Y + aim.Y))
                    .Distinct();
                foreach (var start in starts)
                {
                    yield return new SpellCast(spell) { Shape = SpellShape.Line, Origin = start, Aim = aim };
                    yield return new SpellCast(spell) { Shape = SpellShape.Cone, Origin = start, Aim = aim };
                }
            }
        }

        /// <summary>Each block that puts at least one enemy under it.</summary>
        private static IEnumerable<GridPosition> VolumeCorners(SpellDefinition spell, IEnumerable<Combatant> enemies) =>
            enemies.SelectMany(e => e.Footprint).SelectMany(sq => SpellArea.CornersCovering(sq, spell.Size)).Distinct();

        /// <summary>
        /// The cheapest way to pay for <paramref name="cast"/> that the rules allow: its own pool, or
        /// Body by Bleeding for Power, Overclocked where Stamina can spare it — priced in what the
        /// wounds and Stamina are worth to the caster.
        /// </summary>
        private (SpellCast Cast, CastPlan Plan, double Price)? Paid(Combatant actor, GridPosition at, SpellCast cast, bool checkRules)
        {
            (SpellCast, CastPlan, double)? best = null;
            var ways = new List<SpellCast> { cast };
            if (cast.Spell.Source == MagicSource.ElementalMagic)
            {
                // The fire to hand: the light in the hand, a torch on the ground, a built fire in reach.
                ways.Add(cast with { Tap = FuelSource.Torch });
                ways.Add(cast with { Tap = FuelSource.Lantern });
                ways.AddRange(View.LightsOnTheGround.Where(l => l.DistanceTo(at) <= 1).Select(l => cast with { Tap = FuelSource.Torch, TapAt = l }));
                ways.AddRange(View.Fires.Where(f => f.DistanceTo(at) <= 1).Select(f => cast with { Tap = FuelSource.Campfire, TapAt = f }));
            }
            else if (cast.Spell.Source != MagicSource.BloodMagic)
            {
                ways.Add(cast with { PayInBody = true });

                // Paying the last of Mind or Soul: Backlash holds the track at 1 and takes the rest
                // from Body, never below 1 — how a caster keeps fuelling spells when the pool runs low
                // (Core Rules §4, "Backlash on a spend").
                ways.Add(cast with { Backlash = true });
            }

            if (actor.HasOverclock && !actor.HasOverclockedThisRound && actor.Stamina >= 3)
            {
                ways.AddRange(ways.Select(w => w with { Overclock = true }).ToList());
            }

            foreach (var way in ways)
            {
                var plan = checkRules ? View.PlanCast(actor, way) : View.PlanCastFrom(actor, at, way);
                if (!plan.CanCast)
                {
                    continue;
                }

                var price = Price(actor, plan, way);
                if (plan.Fuel is { SourceSpent: > 0 })
                {
                    // Putting a light out is paid in who can be seen: foes slipping into the dark cost
                    // us our aim; our own side slipping into it costs theirs.
                    var (theirs, ours) = View.GradesLostIfTapped(actor, way);
                    price += Math.Max(0, 0.6 * theirs - 0.35 * ours);
                }

                if (best is null || price < best.Value.Item3)
                {
                    best = (way, plan, price);
                }
            }

            return best;
        }

        /// <summary>
        /// What paying costs the caster: a Body wound dearly — Body is the only track that kills —
        /// a Mind or Soul one less, unless it's the last before they're Stunned or Numbed; a Stamina
        /// a little, since the next turn needs one.
        /// </summary>
        private double Price(Combatant actor, CastPlan plan, SpellCast cast)
        {
            double price = 0;
            if (plan.WoundsPaid > 0 && plan.Track is { } track)
            {
                var (now, _) = actor.Track(track);
                var after = now - plan.WoundsPaid;
                // A Body wound paid is one fewer between the caster and the floor: worth what the
                // caster's own wounds are worth — and a creature that casts in blood runs out of both.
                if (track != WoundType.Body && cast.Backlash && after <= 0)
                {
                    // Held at 1, and the excess paid in Body, Direct — dearly, as any Body is.
                    var excess = 1 - after;
                    var bodyAfter = actor.Body - excess;
                    price += (now - 1) * 0.45 + 0.5
                             + excess * Math.Max(0.9, 0.6 * assess.PerWound(actor)) + (bodyAfter <= 2 ? 2 : 0);
                }
                else
                {
                    price += track == WoundType.Body
                        ? plan.WoundsPaid * Math.Max(0.9, 0.6 * assess.PerWound(actor)) + (after <= 2 ? 2 : 0)
                        : plan.WoundsPaid * 0.45 + (after <= 0 ? 0.6 * assess.Value(actor) : after == 1 ? 0.5 : 0);
                }
            }

            if (cast.Overclock)
            {
                price += 0.3;
            }

            if (plan.Fuel is { } fuel)
            {
                price += 0.3 * fuel.FromCarried;
                if (fuel.SourceSpent > 0)
                {
                    // The source is spent whole, and gone; what its light was worth is reckoned by
                    // the caller, who can look at who it lit.
                    price += 0.4;
                }
            }

            return price;
        }

        /// <summary>
        /// What a Harm is worth: for each creature it catches, the chance the cast lands — the target
        /// dodging if it can and it would turn it — times what the dice do on that track. Mind and
        /// Soul ignore armour, and a track run to 0 is a creature out of the fight. Allies caught
        /// count against it.
        /// </summary>
        private double HarmWorth(Combatant actor, SpellDefinition spell, CastPlan plan)
        {
            var type = spell.DamageType ?? WoundType.Body;
            double worth = 0;
            foreach (var target in plan.Targets)
            {
                if (target.IsOutOfFight)
                {
                    continue;
                }

                var dodge = target.DodgeThreshold + View.VisibilityDodgeModifier(target);
                var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
                var odds = Odds.Of(plan.CastBonus, dodge + answer, target.IsDefenceless,
                    actor.IsStandingGround || actor.IsOffGuard || actor.IsHarried);
                var (dealt, kill) = Damage(actor, spell, target, type);
                if (target.IsSwarm)
                {
                    // Only a blanket over the whole mass gets the dice; a corner of it, a wound a square (GM Reference §3).
                    (dealt, kill) = type != WoundType.Body ? (0, 0)
                        : Encounter.SwarmCoverage(target, plan) is { } covered ? (Math.Min(covered, target.Body), covered >= target.Body ? 1 : 0)
                        : (dealt, kill);
                }
                var value = (odds.Success * dealt * PerWound(target, type) + 0.3 * odds.Success * kill * assess.Value(target))
                            * (target.Side == actor.Side ? -1.5 : 1);
                worth += value;
            }

            return worth;
        }

        /// <summary>What one wound on <paramref name="type"/> is worth: its share of the creature's value, spread over what's left of that track.</summary>
        private double PerWound(Combatant target, WoundType type) => type switch
        {
            WoundType.Body => assess.PerWound(target),
            _ when target.IsMinion => assess.Value(target),
            _ => assess.Value(target) / Math.Max(1, target.Track(type).Current),
        };

        /// <summary>The wounds a hit is expected to put on the track after armour, and the chance it drops the creature.</summary>
        private static (double Dealt, double Kill) Damage(Combatant caster, SpellDefinition spell, Combatant target, WoundType type)
        {
            var count = SpellCalculator.HealHarmDiceCount(spell.SpellTier);
            var sides = DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target));
            var grace = caster.Build is { } build ? SpellCalculator.GraceBonus(build, spell) : 0;
            var absorbs = type == WoundType.Body ? InventoryCalculator.ArmourAbsorbedAgainst(target.Armour, WoundType.Body, isDirect: false) : 0;
            var left = type == WoundType.Body ? target.Body : target.Track(type).Current;

            double dealt = 0, kills = 0, outcomes = 0;
            foreach (var sum in Sums(count, sides))
            {
                var through = Math.Max(0, sum + grace - absorbs);
                dealt += Math.Min(through, Math.Max(0, left));
                kills += through > 0 && (target.IsMinion || through >= left) ? 1 : 0;
                outcomes++;
            }

            return (dealt / outcomes, kills / outcomes);
        }

        private static IEnumerable<int> Sums(int count, int sides)
        {
            if (count == 0)
            {
                yield return 0;
                yield break;
            }

            foreach (var rest in Sums(count - 1, sides))
            {
                for (var face = 1; face <= sides; face++)
                {
                    yield return face + rest;
                }
            }
        }

        /// <summary>A Heal is worth the wounds it puts back — and far more for a body back in the fight.</summary>
        private double HealWorth(Combatant actor, SpellDefinition spell, CastPlan plan)
        {
            var patient = plan.Targets[0];
            var track = patient.IsStunned ? WoundType.Mind : patient.IsNumbed ? WoundType.Soul : WoundType.Body;
            var (now, max) = patient.Track(track);
            var grace = actor.Build is { } build ? SpellCalculator.GraceBonus(build, spell) : 0;
            var average = SpellCalculator.HealHarmDiceCount(spell.SpellTier) * (DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target)) + 1) / 2.0 + grace;
            if (patient.IsOutOfFight)
            {
                return patient.HasFled ? 0 : 2 + 0.9 * assess.Value(patient);
            }

            var mended = Math.Min(average, max - now);
            return track == WoundType.Body ? assess.MendWorth(patient, mended) : mended * assess.PerWound(patient) * 0.8;
        }

        /// <summary>
        /// What a Buff, a Control or a Shift is reckoned at against a blow: each prices the turns or blows
        /// it denies as though the other side would have spent them as harmfully as it could, which it
        /// often wouldn't have — so they are discounted (calibrated by simulation, 2026-11-02).
        /// </summary>
        private const double Denial = 0.6;

        /// <summary>
        /// Where a Sensing spell could tell its caster something: over an Illusion that takes them in, or
        /// over an enemy with spells who hasn't shown it yet.
        /// </summary>
        private IEnumerable<GridPosition> SenseCorners(Combatant actor, SpellDefinition spell, IReadOnlyList<Combatant> enemies) =>
            View.Illusions.Where(i => View.Fools(i, actor)).SelectMany(i => i.Squares)
                .Concat(enemies.Where(e => e.Spells.Count > 0 && !e.ShowsAsCaster).SelectMany(e => e.Footprint))
                .SelectMany(sq => SpellArea.CornersCovering(sq, spell.Size)).Distinct();

        /// <summary>
        /// What a Sensing spell is worth (Core Rules §11, "Sensing"): each Illusion taking its caster in,
        /// at the chance the cast roll beats its Deceit — a wall that isn't there, an enemy that isn't —
        /// and each caster found out before they cast, worth going for that much sooner. But the
        /// Sensing caster only knows of the spells it can see, so it weighs an unshown caster lightly.
        /// </summary>
        private double SenseWorth(Combatant actor, CastPlan plan)
        {
            var area = plan.Area.ToHashSet();
            var lies = View.Illusions.Where(i => View.Fools(i, actor) && i.Squares.Any(area.Contains))
                .Sum(i => Odds.Of(plan.CastBonus, i.Deceit).Success * 0.8);
            var unmasked = View.EnemiesOf(actor).Count(e => e.Spells.Count > 0 && !e.ShowsAsCaster && e.Footprint.Any(area.Contains)) * 0.3;
            return lies + unmasked;
        }

        /// <summary>
        /// Conjured light is worth the Dodge it strips from whoever stands in it, for the rest of the
        /// scene: the dark's +2 or +4 off an enemy is so much more landing from every one of ours who
        /// can reach it, and off one of ours, so much more landing on us. It stays where it is put, so
        /// it is reckoned over a couple of rounds, not the whole fight.
        /// </summary>
        private double LightWorth(Combatant actor, SpellDefinition spell, CastPlan plan)
        {
            var light = new Working(actor, spell.Name, Lever.Light, plan.Origin, SpellArea.Across(spell.Size), plan.Area.ToHashSet(),
                spell.Size == SpellSize.Small ? 1 : 2);
            const double perPointPerBlow = 0.05 * 3.5;
            const double rounds = 2;
            double worth = 0;
            foreach (var creature in View.PartyOnBoard.Concat(View.Adversaries).Distinct().Where(c => !c.IsOutOfFight))
            {
                var stripped = View.VisibilityDodgeModifier(creature) - View.DodgeModifierWith(creature, light);
                if (stripped <= 0)
                {
                    continue;
                }

                // At None nobody beyond Close can be targeted at all: lighting it opens it to every bow.
                var opened = View.VisibilityDodgeModifier(creature) >= 4 ? 2 : 0;
                var striking = Math.Min(3, View.EnemiesOf(creature).Count(e => e.DistanceTo(creature) <= 8));
                var value = (stripped + opened) * perPointPerBlow * striking * rounds * assess.PerWound(creature);
                worth += creature.Side == actor.Side ? -value : value;
            }

            return Denial * worth;
        }

        /// <summary>
        /// The Illusions worth weighing (Core Rules §11, with the user's rulings for a fight):
        /// <list type="bullet">
        /// <item>a Phantom Wound into a foe whose Morale breaks below half Body, worth the chance the felt wounds break it;</item>
        /// <item>phantom help, behind the foes, worth every wavering foe it would break;</item>
        /// <item>a phantom figure beside the foes, worth the blows it draws off ours.</item>
        /// </list>
        /// A foe that mustn't get away is never broken on purpose. A phantom wall isn't weighed yet.
        /// </summary>
        private IEnumerable<(SpellCast Cast, Func<CastPlan, double> Worth)> IllusionCasts(
            Combatant actor, SpellDefinition spell, GridPosition at, IReadOnlyList<Combatant> enemies)
        {
            var breakable = enemies.Where(e => !e.IsPhantom && !e.IsBroken && e.Morale is not null && !View.MustNotEscape(e)).ToList();
            switch (Encounter.FormOf(spell))
            {
                case IllusionForm.Wound:
                    foreach (var foe in breakable.Where(e => e.Morale!.BreaksBelowHalfBody && e.DistanceTo(at) <= 1))
                    {
                        yield return (new SpellCast(spell) { Target = foe }, _ => BreakWorth(foe) * FeltBreaks(actor, spell, foe));
                    }

                    break;

                case IllusionForm.Reinforcements:
                    // Put down behind the nearest foe, away from the caster.
                    foreach (var foe in enemies.Where(e => !e.IsPhantom).OrderBy(e => e.DistanceTo(at)).Take(2))
                    {
                        var corner = new GridPosition(foe.Position.X + 2 * Math.Sign(foe.Position.X - at.X), foe.Position.Y + 2 * Math.Sign(foe.Position.Y - at.Y));
                        yield return (new SpellCast(spell) { Shape = SpellShape.Volume, Corner = corner },
                            plan => enemies.Where(e => !e.IsPhantom && View.IsWavering(e) && plan.Area.Any(sq => e.DistanceTo(sq) <= 16))
                                .Sum(e => View.MustNotEscape(e) ? -2 * assess.Value(e) : BreakWorth(e)));
                    }

                    break;

                case IllusionForm.Figure:
                    foreach (var foe in enemies.Where(e => !e.IsPhantom && !e.IsBroken).OrderBy(e => e.DistanceTo(at)).Take(2))
                    {
                        foreach (var square in Aims.Select(d => new GridPosition(foe.Position.X + d.X, foe.Position.Y + d.Y))
                                     .Where(sq => View.At(sq) is null && !View.Setting.IsWall(sq)).Take(3))
                        {
                            yield return (new SpellCast(spell) { Corner = square }, _ => DecoyWorth(actor, square, enemies));
                        }
                    }

                    break;
            }
        }

        /// <summary>A foe broken is a foe that stops fighting — most of what it was worth, since it may yet be caught or turn.</summary>
        private double BreakWorth(Combatant foe) => 0.6 * assess.Value(foe);

        /// <summary>The chance the wounds a Phantom Wound makes its target feel take what it believes it has below half.</summary>
        private double FeltBreaks(Combatant caster, SpellDefinition spell, Combatant foe)
        {
            var count = SpellCalculator.HealHarmDiceCount(spell.SpellTier);
            var sides = DiceNotation.ParseSides(SpellCalculator.HealHarmDieSize(spell.Target));
            var grace = caster.Build is { } build ? SpellCalculator.GraceBonus(build, spell) : 0;
            var believed = View.BelievedBody(foe);
            var sums = Sums(count, sides).ToList();
            return sums.Count(sum => (believed - sum - grace) * 2 < foe.BodyMax) / (double)sums.Count;
        }

        /// <summary>
        /// A phantom figure is worth the blows it draws: each foe that could strike at it might as
        /// well swing at it as at one of ours — about half the time — for the two rounds it stands.
        /// </summary>
        private double DecoyWorth(Combatant actor, GridPosition square, IReadOnlyList<Combatant> enemies)
        {
            var ours = View.AlliesOf(actor).ToList();
            var perWound = ours.Average(c => assess.PerWound(c));
            double worth = 0;
            foreach (var foe in enemies.Where(e => !e.IsPhantom && !e.IsBroken))
            {
                var stride = View.SquaresFor(foe.Speed, 1);
                if (foe.DistanceTo(square) <= foe.Reach + stride || (foe.HasRangedAttack && foe.DistanceTo(square) <= 16))
                {
                    worth += 0.5 * assess.Threat(foe) * perWound;
                }
            }

            return Denial * 2 * worth;
        }

        /// <summary>
        /// A casting as it stands, and — where the caster could keep it — the same declared Sustained:
        /// held from round to round for Stamina, it is worth the blows of every round it stays.
        /// </summary>
        private static IEnumerable<SpellCast> Held(Combatant actor, SpellCast cast)
        {
            yield return cast;
            if (actor.Sustaining is null && actor.Stamina >= 2)
            {
                yield return cast with { Sustain = true };
            }
        }

        /// <summary>Rounds ahead a held Bane is reckoned over — fights here are over in four or five.</summary>
        private const int HeldRounds = 2;

        /// <summary>
        /// A Bane's worth: this round's, and for one declared Sustained, each round after it that it
        /// is paid for and survives its recheck — the caster's cast total against the Threshold
        /// without the Bane, the target spending for +2 if it can (Core Rules §11, "Sustaining a
        /// Spell"). Then every one of ours fighting near it is swinging, not only those still to act.
        /// Each payment costs a Stamina and hands the GM an Opportunity, and is the caster's spend
        /// for the round.
        /// </summary>
        private double HeldBaneWorth(Combatant actor, SpellCast cast, CastPlan plan, Combatant target, ThresholdType threshold)
        {
            var first = BaneWorth(actor, target, threshold, cast.Spell.SpellTier);
            if (!cast.Sustain || target.Side == actor.Side)
            {
                return first;
            }

            var unlowered = threshold switch
            {
                ThresholdType.Dodge => target.DodgeThreshold,
                ThresholdType.Stability => target.StabilityThreshold,
                _ => target.ResolveThreshold ?? 99,
            };
            var holds = Odds.Of(plan.CastBonus, unlowered + (target.Stamina > 0 ? ThresholdModifierCalculator.ReactionBonus : 0)).Success;
            var everyRound = BaneWorth(actor, target, threshold, cast.Spell.SpellTier, wholeRound: true);
            const double perPayment = 1.0;

            // Never paying the last Stamina: that would put the caster down at their next turn, and
            // the spell with them.
            double later = 0, kept = 1;
            for (var round = 0; round < Math.Min(HeldRounds, actor.Stamina - 1); round++)
            {
                later -= kept * perPayment;
                kept *= holds;
                later += kept * everyRound;
            }

            return first + later;
        }

        /// <summary>The Thresholds a Bane could be laid on: the one it is written for, or — a Curse — the caster's pick of those in the game.</summary>
        private static IEnumerable<ThresholdType> BaneThresholds(SpellDefinition spell, Combatant target) =>
            spell.Threshold is { } written
                ? [written]
                : new[] { ThresholdType.Dodge, ThresholdType.Stability, ThresholdType.Resolve }
                    .Where(t => t != ThresholdType.Resolve || target.ResolveThreshold is not null);

        /// <summary>
        /// A Bane is worth what its point off a Threshold lets land before the caster's next turn,
        /// which is when it lapses (Core Rules §11, "Buff / Bane Spells"): off Dodge, the extra
        /// wounds from every blow still to come at the target from those of ours yet to act; off
        /// Resolve, the better odds of a Control from one of ours who has one, or of a hold already
        /// on it staying; off Stability, of a Shove, Trip or Grab from one of ours in reach. Laid on
        /// one of ours — a haze that caught a friend — it costs the same the other way. Nothing for
        /// a point already lost to a Bane of the same kind. Not discounted as a denied turn is: these
        /// are our own blows, from those of ours still to act, at their best.
        /// </summary>
        /// <param name="wholeRound">A round to come rather than this one: everybody of ours fighting near it swings, wherever they stand now.</param>
        private double BaneWorth(Combatant actor, Combatant target, ThresholdType threshold, Tier tier, bool wholeRound = false)
        {
            if (target.IsOutOfFight || View.SpellsInPlay.Any(s => s.Target == target && s.Type == SpellType.Bane && s.Threshold == threshold))
            {
                return 0;
            }

            var amount = -ThresholdModifierCalculator.BaneAmount(tier);
            var sign = target.Side == actor.Side ? -1.0 : 1.0;

            // Those who would be glad of it: the other side from the target, still to act before the
            // caster's next turn — the caster too, with an action to spare after this one.
            var acting = (sign > 0 ? View.AlliesOf(actor) : View.EnemiesOf(actor))
                .Where(a => !a.IsPhantom && !a.IsBroken && (wholeRound ? a.DistanceTo(target) <= 8
                    : a == actor ? actor.ActionsRemaining > 1
                    : sign > 0 && stillToAct is not null ? stillToAct.Contains(a) : !a.HasActedThisRound))
                .ToList();

            double worth = 0;
            switch (threshold)
            {
                case ThresholdType.Dodge:
                    foreach (var attacker in acting)
                    {
                        // This round, what they can strike with from where they stand; a round to
                        // come, their best, since they'll have closed by then.
                        var with = wholeRound
                            ? attacker.Attacks.MaxBy(a => a.DamageDiceCount * a.DamageDieSides + a.AttackModifier)
                            : assess.BestAttack(attacker, attacker.Position, target).With;
                        if (with is null)
                        {
                            continue;
                        }

                        var dodge = target.DodgeThreshold + View.VisibilityDodgeModifier(target);
                        var now = assess.Blow(attacker, with, target, with.DamageDieSides, dodge);
                        var then = assess.Blow(attacker, with, target, with.DamageDieSides, dodge - amount);
                        worth += (then.ExpectedDealt - now.ExpectedDealt) * assess.PerWound(target)
                                 + 0.3 * (then.Kill - now.Kill) * assess.Value(target);
                    }

                    break;

                case ThresholdType.Resolve when target.ResolveThreshold is not null:
                    var minds = acting.Count(a => a.Spells.Any(sp => sp.SpellType == SpellType.Control)) + (View.IsControlled(target) ? 1 : 0);
                    worth = minds * 0.04 * amount * assess.Value(target);
                    break;

                case ThresholdType.Stability:
                    var hands = acting.Count(a => a.DistanceTo(target) <= a.OwnReach + 1 && !a.IsSwarm);
                    worth = hands * 0.02 * amount * assess.Value(target);
                    break;
            }

            return sign * worth;
        }

        /// <summary>A Buff on Dodge is worth the blows it would turn before the caster's next turn.</summary>
        private double BuffWorth(CastPlan plan, double care)
        {
            var ally = plan.Targets[0];
            if (View.SpellsInPlay.Any(s => s.Target == ally && s.Type == SpellType.Buff && s.Threshold == ThresholdType.Dodge))
            {
                return 0;
            }

            var bonus = SpellCalculator.BuffMagnitude(Tier.T1);
            return 0.8 * Math.Max(care, 0.5) * (assess.Exposure(ally, ally.Position) - assess.Exposure(ally, ally.Position, extraDodge: bonus));
        }

        /// <summary>
        /// A Control spell is worth the turns it takes off its target: the chance it lands, then about
        /// one more for each recheck it survives, to its Tier's cap. A kneeling Compulsion takes the
        /// whole turn; Fear takes most of a turn from a creature toe to toe with one of ours, and less
        /// from one shooting from range — and drives off whatever must not get away, so never that.
        /// </summary>
        private double ControlWorth(Combatant actor, ControlEffect effect, CastPlan plan)
        {
            var target = plan.Targets.FirstOrDefault();
            if (target is null || target.IsBroken || target.ResolveThreshold is not { } resolve || target.IsMinion)
            {
                return 0;
            }

            if (effect == ControlEffect.Fear && View.MustNotEscape(target))
            {
                return 0;
            }

            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var solo = target.Role == AdversaryRole.Solo;
            var lands = Odds.Of(plan.CastBonus, resolve + answer, disadvantage: solo).Success;

            // Rechecked at the end of this very turn, and of the next: a turn of the target's is only
            // lost if the spell is still on it then, and it lapses after two rounds at T1.
            var holds = Odds.Of(plan.CastBonus, resolve, disadvantage: solo).Success;
            var turns = holds * (target.HasActedThisRound ? 1 : 1 + holds);

            // A turn lost is the harm it would have done — spread over the party it would have done it to.
            var victims = View.EnemiesOf(target).ToList();
            var perWound = victims.Count == 0 ? 0 : victims.Average(v => assess.PerWound(v));
            var turnWorth = assess.Threat(target) * perWound;

            if (effect == ControlEffect.Compulsion)
            {
                return lands * turns * turnWorth;
            }

            // Fear: driven out to Long it is gone from the Challenge for good — as good as down. Nearer,
            // it costs a creature in our front line most of its turn, and a shooter some of its aim.
            var band = plan.Origin.DistanceTo(actor.Position) switch
            {
                > 8 => 3,
                > 4 => 2,
                _ => 1,
            };
            var gone = band switch
            {
                3 => holds,
                2 => holds * holds,
                _ => 0,
            };
            var engaged = victims.Any(a => a.DistanceTo(target) <= target.Reach);
            return lands * (gone * 0.9 * assess.Value(target) + (1 - gone) * turns * (engaged ? 0.8 : 0.4) * turnWorth);
        }

        /// <summary>
        /// A Shove is worth what it takes away: an enemy toe to toe with one of ours carried out of
        /// reach, and whatever it is driven into.
        /// </summary>
        /// <summary>The chance a Bind takes: the cast against Stability, which the target may raise by standing firm.</summary>
        private static double BindChance(CastPlan plan)
        {
            var target = plan.Targets[0];
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            return Odds.Of(plan.CastBonus, target.StabilityThreshold + answer).Success;
        }

        private double ShiftWorth(Combatant actor, CastPlan plan)
        {
            var target = plan.Targets[0];
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var chance = Odds.Of(plan.CastBonus, target.StabilityThreshold + answer, disadvantage: target.IsUnshakable || target.IsSolo).Success;
            var engaged = View.EnemiesOf(target).Count(a => a.DistanceTo(target) <= target.Reach);
            var separation = engaged > 0 ? 0.6 * assess.Threat(target) * assess.PerWound(actor) : 0;

            var step = (Math.Sign(target.Position.X - actor.Position.X), Math.Sign(target.Position.Y - actor.Position.Y));
            var crossed = 0;
            var at = target.Position;
            var squares = SpellArea.ShiftSquares(plan.Targets.Count > 0 ? Tier.T1 : Tier.T1);
            while (crossed < squares)
            {
                var next = new GridPosition(at.X + step.Item1, at.Y + step.Item2);
                if (View.At(next) is not null || View.Setting.IsWall(next))
                {
                    break;
                }

                at = next;
                crossed++;
            }

            var impact = Math.Max(0, squares - crossed - InventoryCalculator.ArmourAbsorbedAgainst(target.Armour, WoundType.Body, isDirect: false));
            return chance * (separation + impact * assess.PerWound(target));
        }

        /// <summary>The best Harm a caster could throw from <paramref name="square"/> next turn, for where to stand.</summary>
        /// <param name="worthChasing">The enemies worth closing on — not a runner being let go.</param>
        public double BestHarmFrom(Combatant actor, GridPosition square, IEnumerable<Combatant> worthChasing)
        {
            double best = 0;
            var targets = worthChasing.Where(e => e.DistanceTo(square) <= 8).ToList();
            foreach (var spell in actor.Spells.Where(s => s.SpellType == SpellType.Harm && s.Target == SpellTarget.One))
            {
                foreach (var enemy in targets)
                {
                    var plan = View.PlanCastFrom(actor, square, new SpellCast(spell) { Target = enemy });
                    if (plan.CanCast)
                    {
                        best = Math.Max(best, HarmWorth(actor, spell, plan) - 0.45 * plan.Cost);
                    }
                }
            }

            return best;
        }
    }
}
