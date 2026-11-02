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

        /// <param name="checkRules">Casting from where the caster stands now: the turn's own refusals apply.</param>
        public IEnumerable<Option> Options(Combatant actor, GridPosition at, bool checkRules, double care, bool usesTricks)
        {
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
                    SpellType.Utility when usesTricks && SpellArea.LeverOf(spell) == Lever.Light && View.IsUnlit => VolumeCorners(spell, enemies)
                        .Select(corner => (new SpellCast(spell) { Shape = SpellShape.Volume, Corner = corner }, (Func<CastPlan, double>)(plan => LightWorth(actor, spell, plan)))),
                    SpellType.Motion when usesTricks && SpellArea.MotionOf(spell) == MotionKind.Shift => enemies.Where(e => !e.IsBroken)
                        .Select(e => (new SpellCast(spell) { Target = e }, (Func<CastPlan, double>)(plan => Denial * ShiftWorth(actor, plan)))),
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
                price += track == WoundType.Body
                    ? plan.WoundsPaid * Math.Max(0.9, 0.6 * assess.PerWound(actor)) + (after <= 2 ? 2 : 0)
                    : plan.WoundsPaid * 0.45 + (after <= 0 ? 0.6 * assess.Value(actor) : after == 1 ? 0.5 : 0);
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
        private double ShiftWorth(Combatant actor, CastPlan plan)
        {
            var target = plan.Targets[0];
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var chance = Odds.Of(plan.CastBonus, target.StabilityThreshold + answer).Success;
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
        public double BestHarmFrom(Combatant actor, GridPosition square)
        {
            double best = 0;
            foreach (var spell in actor.Spells.Where(s => s.SpellType == SpellType.Harm && s.Target == SpellTarget.One))
            {
                foreach (var enemy in View.EnemiesOf(actor).Where(e => e.DistanceTo(square) <= 8))
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
