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
                    SpellType.Harm when spell.Shape == SpellShape.Volume => VolumeCorners(spell, enemies)
                        .Select(corner => (new SpellCast(spell) { Corner = corner }, (Func<CastPlan, double>)(plan => HarmWorth(actor, spell, plan)))),
                    SpellType.Harm => enemies
                        .Select(e => (new SpellCast(spell) { Target = e }, (Func<CastPlan, double>)(plan => HarmWorth(actor, spell, plan)))),
                    SpellType.Heal => View.PartyOnBoard.Concat(View.Adversaries)
                        .Where(c => c.Side == actor.Side && c != actor && c.Position.DistanceTo(at) <= 1)
                        .Select(c => (Mend(spell, c), (Func<CastPlan, double>)(plan => HealWorth(actor, spell, plan)))),
                    SpellType.Buff when usesTricks => View.PartyOnBoard.Concat(View.Adversaries)
                        .Where(c => c.Side == actor.Side && !c.IsOutOfFight)
                        .Select(c => (new SpellCast(spell) { Target = c, Threshold = ThresholdType.Dodge }, (Func<CastPlan, double>)(plan => BuffWorth(plan, care)))),
                    SpellType.Motion when usesTricks && SpellArea.MotionOf(spell) == MotionKind.Shift => enemies.Where(e => !e.IsBroken)
                        .Select(e => (new SpellCast(spell) { Target = e }, (Func<CastPlan, double>)(plan => ShiftWorth(actor, plan)))),
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
            plan.Area.Count > 1 ? $"over {string.Join(", ", plan.Targets.Select(t => t.Name))}" : $"at {plan.Targets[0].Name}";

        /// <summary>A Heal mends whichever track has its target down: a stunned mind before a scratch.</summary>
        private static SpellCast Mend(SpellDefinition spell, Combatant patient) => new(spell)
        {
            Target = patient,
            Restores = patient.IsStunned ? WoundType.Mind : patient.IsNumbed ? WoundType.Soul : WoundType.Body,
        };

        /// <summary>Each block that puts at least one enemy under it.</summary>
        private static IEnumerable<GridPosition> VolumeCorners(SpellDefinition spell, IEnumerable<Combatant> enemies) =>
            enemies.SelectMany(e => SpellArea.CornersCovering(e.Position, spell.Size)).Distinct();

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
                price += track == WoundType.Body
                    ? plan.WoundsPaid * 0.9 + (after <= 2 ? 2 : 0)
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
                    // The source is spent whole, and a burning one was a light: in the dark, putting
                    // it out costs everybody it lit — the party's last light most of all.
                    price += 0.4 + (View.Setting.IsUnlit ? (View.PartyLights <= 1 ? 3.0 : 1.2) : 0);
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

            return Math.Min(average, max - now) * assess.PerWound(patient) * 0.8;
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
        /// A Shove is worth what it takes away: an enemy toe to toe with one of ours carried out of
        /// reach, and whatever it is driven into.
        /// </summary>
        private double ShiftWorth(Combatant actor, CastPlan plan)
        {
            var target = plan.Targets[0];
            var answer = BattleView.TacticalSpendRefusal(target) is null && target.Stamina > 1 ? ThresholdModifierCalculator.ReactionBonus : 0;
            var chance = Odds.Of(plan.CastBonus, target.StabilityThreshold + answer).Success;
            var engaged = View.EnemiesOf(target).Count(a => a.Position.DistanceTo(target.Position) <= target.Reach);
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
            foreach (var spell in actor.Spells.Where(s => s.SpellType == SpellType.Harm && s.Shape != SpellShape.Volume))
            {
                foreach (var enemy in View.EnemiesOf(actor).Where(e => e.Position.DistanceTo(square) <= 8))
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
