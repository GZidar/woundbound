using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// The board's animation: every <see cref="Happening"/> the encounter raises becomes a short act
    /// played in turn — a blade swung and the blow taken, dodged or Riposted; an arrow, bolt, stone or
    /// thrown blade in flight; a spell gathered in the hand, sent to where it was aimed and bursting
    /// over the squares it covers. The rules have already settled each one; this only shows it.
    /// </summary>
    public partial class Board3D
    {
        private readonly Queue<Happening> happenings = new();
        private Act? playing;
        private Node3D? effects;

        /// <summary>Whether something is still being shown — the battle screen holds the next step until it isn't.</summary>
        public bool Busy => playing is not null || happenings.Count > 0;

        /// <summary>Queues <paramref name="happening"/> to be shown once whatever is ahead of it has been.</summary>
        public void Play(Happening happening)
        {
            happenings.Enqueue(happening);
            foreach (var who in Involved(happening))
            {
                if (figures.TryGetValue(who, out var figure))
                {
                    figure.Holds++;
                }
            }
        }

        private void ClearActs()
        {
            happenings.Clear();
            playing = null;
            ClearTrails();
            effects?.QueueFree();
            effects = new Node3D();
            AddChild(effects);
        }

        private static IEnumerable<Combatant> Involved(Happening happening) => happening switch
        {
            Moved m => [m.Mover],
            Handled h => [h.Actor, h.Target],
            Struck s => [s.Attacker, s.Target],
            SpellWorked w => w.Plan.Targets.Prepend(w.Caster),
            SpellStruck s => [s.Caster, s.Target],
            _ => [],
        };

        private void Animate(double delta)
        {
            if (playing is null)
            {
                if (!happenings.TryPeek(out var next))
                {
                    return;
                }

                happenings.Dequeue();
                foreach (var who in Involved(next))
                {
                    if (figures.TryGetValue(who, out var figure))
                    {
                        figure.ResetPose();
                    }
                }

                // Moves one after another - a rider and its horse, foes closing in at the round's
                // end - walk together.
                if (next is Moved first)
                {
                    var together = new List<Moved> { first };
                    while (happenings.TryPeek(out var after) && after is Moved also && together.All(m => m.Mover != also.Mover))
                    {
                        happenings.Dequeue();
                        together.Add(also);
                    }

                    playing = Walks(together);
                    return;
                }

                playing = Begin(next);
                if (playing is null)
                {
                    Release(next);
                    return;
                }
            }

            playing.Time += delta;
            playing.Step(Math.Min(playing.Time, playing.Duration));
            if (playing.Time >= playing.Duration)
            {
                playing.Finish();
                foreach (var done in playing.Covers)
                {
                    Release(done);
                }

                playing = null;
            }
        }

        private void Release(Happening happening)
        {
            foreach (var who in Involved(happening))
            {
                if (figures.TryGetValue(who, out var figure))
                {
                    figure.Holds = Math.Max(0, figure.Holds - 1);
                    if (figure.Holds == 0)
                    {
                        figure.ResetPose();
                    }
                }
            }
        }

        /// <summary>Whether anybody can see any of it; an act nobody could see is skipped.</summary>
        private bool Seen(params Combatant[] who) => who.Any(c => (fog?.Shows(c) ?? true) && figures.ContainsKey(c));

        private Act? Begin(Happening happening) => happening switch
        {
            Struck { With.IsRanged: true } s when Seen(s.Attacker, s.Target) => Shot(s),
            Struck s when Seen(s.Attacker, s.Target) => Blow(s),
            SpellWorked w when Seen(w.Plan.Targets.Prepend(w.Caster).ToArray()) => Working(w),
            SpellStruck s when Seen(s.Caster, s.Target) => SpellLands(s),
            Handled h when Seen(h.Actor, h.Target) => Handling(h),
            TurnBegan => new Act(happening, 0, _ => ClearTrails()),
            _ => null,
        };

        // ---- The acts ----

        private const float WalkSpeed = 3.6f;
        private const float CarriedSpeed = 7f;
        private Node3D? trails;

        /// <summary>
        /// Creatures walking their routes square by square, together, stepping as they go; a creature
        /// shoved, swept or dragged slides instead, leaning back. Each leaves a breadcrumb on every
        /// square it passes — dots for a walk, dashes for a carry — which stays until the next turn
        /// begins, so everybody can see exactly which way it went.
        /// </summary>
        private Act Walks(List<Moved> moves)
        {
            var walks = moves.Where(m => figures.ContainsKey(m.Mover) && m.Path.Count > 1 && Seen(m.Mover))
                .Select(m => (Move: m, Figure: figures[m.Mover], Points: m.Path.Select(sq => Centre(m.Mover, sq)).ToList()))
                .ToList();
            foreach (var walk in walks)
            {
                // Start where the walk starts, whatever the figure was last easing towards.
                walk.Figure.Anchor = walk.Points[0];
            }

            var lengths = walks.Select(w => w.Points.Zip(w.Points.Skip(1), (a, b) => a.DistanceTo(b)).Sum()).ToList();
            var durations = walks.Select((w, i) => Math.Min(1.9, lengths[i] / (w.Move.Forced ? CarriedSpeed : WalkSpeed))).ToList();
            var dropped = walks.Select(_ => 0).ToList();
            var act = new Act(moves[0], durations.DefaultIfEmpty(0).Max(), t =>
            {
                for (var i = 0; i < walks.Count; i++)
                {
                    var (move, figure, points) = walks[i];
                    var along = lengths[i] * (float)Math.Clamp(t / Math.Max(0.01, durations[i]), 0, 1);
                    var (at, heading, passed) = Along(points, along);
                    var was = figure.Anchor;
                    figure.Anchor = at;
                    if (heading.LengthSquared() > 1e-4f)
                    {
                        figure.Face = Angle(move.Forced ? -heading : heading);
                    }

                    if (move.Forced)
                    {
                        figure.Lean = along < lengths[i] ? -14 : 0;
                    }
                    else
                    {
                        figure.Stride(was.DistanceTo(at));
                    }

                    // A crumb for every square it has reached, the first included.
                    while (dropped[i] <= passed && dropped[i] < points.Count)
                    {
                        var from = dropped[i] > 0 ? points[dropped[i] - 1] : points[0];
                        Crumb(points[dropped[i]], points[dropped[i]] - from, figure.Colour, move.Forced);
                        dropped[i]++;
                    }
                }
            });
            act.Covers = moves;
            return act;
        }

        /// <summary>Where a creature stands on <paramref name="square"/>: the middle of its footprint, up on its horse if riding.</summary>
        private Vector3 Centre(Combatant c, GridPosition square) =>
            new(square.X + c.Across / 2f, (c.IsRiding ? 0.55f : 0) + Footprints.At(square, c.Across).Max(FootHeight), square.Y + c.Across / 2f);

        /// <summary>The point <paramref name="distance"/> along a route, the way it's heading there, and the last corner passed.</summary>
        private static (Vector3 At, Vector3 Heading, int Passed) Along(List<Vector3> points, float distance)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var leg = points[i].DistanceTo(points[i + 1]);
                if (distance <= leg || i == points.Count - 2)
                {
                    var k = leg < 1e-4f ? 1 : Mathf.Clamp(distance / leg, 0, 1);
                    return (points[i].Lerp(points[i + 1], k), (points[i + 1] - points[i]).Normalized(), k >= 0.999f ? i + 1 : i);
                }

                distance -= leg;
            }

            return (points[^1], Vector3.Zero, points.Count - 1);
        }

        /// <summary>A breadcrumb: a dot where a walk passed, a dash along the way a carry went.</summary>
        private void Crumb(Vector3 at, Vector3 heading, Color colour, bool carried)
        {
            if (trails is null)
            {
                trails = new Node3D();
                AddChild(trails);
            }

            var crumb = new MeshInstance3D
            {
                Mesh = carried
                    ? new BoxMesh { Size = new Vector3(0.09f, 0.02f, 0.38f) }
                    : new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.02f, RadialSegments = 16 },
                MaterialOverride = Shine(colour, 1f),
                Position = at with { Y = 0.02f },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            trails.AddChild(crumb);

            // A dark rim under it, so a crumb in any colour stands off any ground.
            crumb.AddChild(new MeshInstance3D
            {
                Mesh = carried
                    ? new BoxMesh { Size = new Vector3(0.15f, 0.01f, 0.44f) }
                    : new CylinderMesh { TopRadius = 0.125f, BottomRadius = 0.125f, Height = 0.01f, RadialSegments = 16 },
                MaterialOverride = Shine(new Color(0.05f, 0.05f, 0.07f), 0.6f),
                Position = Vector3.Down * 0.006f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            if (carried && heading.LengthSquared() > 1e-4f)
            {
                crumb.Rotation = new Vector3(0, Angle(heading), 0);
            }
        }

        /// <summary>Sweeps the breadcrumbs away: a new turn has begun.</summary>
        private void ClearTrails()
        {
            trails?.QueueFree();
            trails = null;
        }

        /// <summary>A blade swung: wind up, lunge and strike, then back; the target flinches, sidesteps, or sidesteps and strikes back.</summary>
        private Act Blow(Struck s)
        {
            var attacker = figures[s.Attacker];
            var target = figures[s.Target];
            var (forward, side) = Across(attacker, target);
            var impact = 0.36;
            var duration = s.Riposte ? 1.0 : s.Critical ? 0.85 : 0.72;
            var sparked = false;
            var countered = false;
            return new Act(s, duration, t =>
            {
                attacker.Face = Angle(forward);
                attacker.Offset = forward * (0.34f * Bump(t, 0.18, 0.62) - 0.07f * Bump(t, 0, 0.28));
                attacker.Lean = -12 * Bump(t, 0, 0.3) + 14 * Bump(t, 0.25, 0.6);
                attacker.Swing = SwingAt(t, 0, 0.25, 0.4, 0.72);

                if (s.Hit)
                {
                    target.Offset = forward * (s.Critical ? 0.24f : 0.15f) * Bump(t, impact, impact + 0.3);
                    target.Lean = (s.Critical ? -22 : -12) * Bump(t, impact, impact + 0.35);
                    if (!sparked && t >= impact)
                    {
                        sparked = true;
                        Spark(target.Chest, s.Critical ? Hurt.Lightened(0.35f) : Hurt, s.Critical ? 0.42f : 0.26f);
                    }
                }
                else if (s.Dodged)
                {
                    target.Offset = side * 0.4f * Bump(t, 0.18, s.Riposte ? 0.55 : 0.68);
                    if (s.Riposte)
                    {
                        // Turned aside, the critical comes back on the one who made it.
                        target.Face = Angle(-forward);
                        target.Swing = SwingAt(t, 0.42, 0.55, 0.66, 0.95);
                        target.Offset += -forward * 0.3f * Bump(t, 0.5, 0.85);
                        attacker.Offset += -forward * 0.25f * Bump(t, 0.64, 0.95);
                        attacker.Lean += -18 * Bump(t, 0.64, 0.95);
                        if (!countered && t >= 0.66)
                        {
                            countered = true;
                            Spark(attacker.Chest, Hurt.Lightened(0.35f), 0.38f);
                        }
                    }
                }
                else
                {
                    target.Lean = -6 * Bump(t, impact, impact + 0.3);
                }
            });
        }

        /// <summary>
        /// Hands on somebody. A Grab lunges with both hands and, if it takes, pulls the target in; a
        /// Shove thrusts with both (the push itself follows, as a slide); a Trip drops low and sweeps
        /// a leg through, and the target goes down as it lands. Breaking free is a wrench and a step
        /// back. A target that stood firm braces; one that dodged sidesteps; one who overbalanced
        /// goes down when the act ends.
        /// </summary>
        private Act Handling(Handled h)
        {
            var actor = figures[h.Actor];
            var target = figures[h.Target];
            var (forward, side) = Across(actor, target);
            var impact = 0.38;
            var flashed = false;
            return new Act(h, 0.8, t =>
            {
                actor.Face = Angle(forward);
                target.Face = Angle(-forward);
                var reach = Bump(t, 0.12, 0.7);
                switch (h.Action)
                {
                    case ControlAction.Grab:
                        actor.Offset = forward * 0.3f * reach;
                        actor.Swing = -80 * reach;
                        actor.OffSwing = -80 * reach;
                        actor.Lean = 12 * reach;
                        break;
                    case ControlAction.Shove:
                        actor.Offset = forward * (0.38f * Bump(t, 0.2, 0.62) - 0.08f * Bump(t, 0, 0.3));
                        actor.Swing = -85 * Bump(t, 0.1, 0.7);
                        actor.OffSwing = -85 * Bump(t, 0.1, 0.7);
                        actor.Lean = -8 * Bump(t, 0, 0.3) + 18 * Bump(t, 0.25, 0.65);
                        break;
                    case ControlAction.Trip:
                        actor.Offset = forward * 0.25f * reach;
                        actor.Lean = 24 * reach;
                        actor.Kick = 70 * Bump(t, 0.2, 0.62);
                        break;
                    default:
                        // Breaking free: a twist against the hold, then a step back out of it.
                        actor.Lean = 16 * Mathf.Sin((float)t * 22) * reach;
                        actor.OffSwing = -50 * reach;
                        if (h.Worked)
                        {
                            actor.Offset = -forward * 0.25f * Bump(t, 0.45, 0.8);
                        }

                        break;
                }

                if (h.Worked && h.Action is not null)
                {
                    if (!flashed && t >= impact)
                    {
                        flashed = true;
                        Spark(target.Chest, Color.FromHtml("#f0e442"), 0.22f);
                    }

                    target.Offset = h.Action switch
                    {
                        ControlAction.Grab => -forward * 0.12f * Bump(t, impact, 0.8),
                        ControlAction.Shove => forward * 0.14f * Bump(t, impact, 0.8),
                        _ => Vector3.Zero,
                    };
                    target.Lean = h.Action == ControlAction.Trip ? -25 * Bump(t, impact, 0.8) : -10 * Bump(t, impact, 0.75);
                }
                else if (!h.Worked)
                {
                    if (h.Resisted && h.Action is not ControlAction.Trip)
                    {
                        // Stood firm: braced against it, leaning in.
                        target.Lean = 14 * Bump(t, 0.25, 0.8);
                    }
                    else if (h.Action is not null)
                    {
                        target.Offset = side * 0.32f * Bump(t, 0.2, 0.7);
                    }
                }
            });
        }

        /// <summary>A shot: drawn, loosed, and in flight — sticking in the target, passing a dodge, or flying wide.</summary>
        private Act Shot(Struck s)
        {
            var attacker = figures[s.Attacker];
            var target = figures[s.Target];
            var (forward, side) = Across(attacker, target);
            var kind = MissileOf(s.With);
            var from = attacker.Hand(forward);
            var to = target.Chest;
            var distance = from.DistanceTo(to);
            if (!s.Hit)
            {
                // Wide, or through where the target stood a moment ago: on past them, and down.
                to = to + forward * 1.6f + (s.Dodged ? Vector3.Zero : side * 0.45f) + Vector3.Down * 0.3f;
            }

            var draw = 0.3;
            var flight = Mathf.Clamp(distance * 0.06f, 0.32f, 0.85f) * (s.Hit ? 1 : 1.5f);
            var arrive = draw + flight * (s.Hit ? 1 : 0.66);
            var arc = distance * kind switch { Missile.Arrow => 0.07f, Missile.Stone => 0.05f, Missile.Thrown => 0.04f, _ => 0.025f };
            var shown = fog?.Shows(s.Attacker) ?? true;
            var missile = MissileMesh(kind);
            var sparked = false;
            return new Act(s, draw + flight + 0.25, t =>
            {
                attacker.Face = Angle(forward);
                attacker.Offset = -forward * 0.06f * Bump(t, 0, draw + 0.1);
                attacker.Swing = kind == Missile.Thrown ? SwingAt(t, 0, 0.2, draw, draw + 0.3) : -12 * (float)Bump(t, 0, draw + 0.15);

                var u = (t - draw) / flight;
                missile.Visible = u is > 0 and < 1 && (shown || u > 0.4);
                if (missile.Visible)
                {
                    var at = from.Lerp(to, (float)u) + Vector3.Up * arc * 4 * (float)(u * (1 - u));
                    var ahead = from.Lerp(to, (float)Math.Min(1, u + 0.02)) + Vector3.Up * arc * 4 * (float)((u + 0.02) * (0.98 - u));
                    missile.Position = at;
                    if (ahead.DistanceTo(at) > 1e-4f)
                    {
                        missile.LookAt(ahead, Vector3.Up);
                    }

                    if (kind == Missile.Thrown)
                    {
                        missile.RotateObjectLocal(Vector3.Right, (float)(u * 14));
                    }
                }

                if (s.Hit)
                {
                    target.Offset = forward * 0.12f * Bump(t, arrive, arrive + 0.25);
                    target.Lean = (s.Critical ? -20 : -10) * Bump(t, arrive, arrive + 0.25);
                    if (!sparked && t >= arrive)
                    {
                        sparked = true;
                        Spark(target.Chest, s.Critical ? Hurt.Lightened(0.35f) : Hurt, s.Critical ? 0.38f : 0.22f);
                    }
                }
                else if (s.Dodged)
                {
                    target.Offset = side * 0.4f * Bump(t, arrive - 0.2, arrive + 0.25);
                    if (s.Riposte && !sparked && t >= arrive)
                    {
                        // A dodged critical shot is a critical failure: the shooter's own hurt.
                        sparked = true;
                        Spark(attacker.Chest, Hurt, 0.24f);
                    }
                }
            }, () => missile.QueueFree());
        }

        /// <summary>
        /// A spell worked: gathered at the caster's hand, then laid on by touch, sent as a ray, or
        /// thrown to its corner and bursting over every square it covers; a heal rises off whoever
        /// it mends, and anything else rings them where they stand.
        /// </summary>
        private Act Working(SpellWorked w)
        {
            var caster = figures[w.Caster];
            var colour = SpellColour(w.Spell);
            var shape = SpellArea.ShapeOf(new SpellCast(w.Spell));
            var origin = new Vector3(w.Plan.Origin.X + 0.5f, 0.6f, w.Plan.Origin.Y + 0.5f);
            var targets = w.Plan.Targets.Where(c => figures.ContainsKey(c) && c != w.Caster).Select(c => figures[c]).ToList();
            var aim = targets.FirstOrDefault()?.Chest ?? origin;
            var (forward, _) = Across(caster, aim);
            var touch = w.Spell.Range == SpellRange.Touch;
            var self = !touch && caster.Anchor.DistanceTo(origin with { Y = 0 }) < 0.6f && targets.Count == 0;

            var gather = 0.4;
            var from = caster.Hand(forward) + Vector3.Up * 0.2f;
            var travel = touch || self ? 0.0 : Mathf.Clamp(from.DistanceTo(shape == SpellShape.Volume ? origin : aim) * 0.04f, 0.2f, 0.55f);
            var to = shape == SpellShape.Volume ? origin : aim;
            var burst = shape == SpellShape.Volume ? 0.5 : 0.0;
            var after = w.Spell.SpellType == SpellType.Harm ? 0.05 : 0.6;
            var lands = gather + (touch ? 0.25 : travel);

            var orb = Glow(colour, 0.17f);
            orb.AddChild(new OmniLight3D { LightColor = colour, LightEnergy = 2.5f, OmniRange = 2.5f });
            var squares = new List<MeshInstance3D>();
            var bursting = false;
            var marked = false;
            return new Act(w, lands + burst + after, t =>
            {
                caster.Face = Angle(forward);
                caster.Swing = -95 * (float)Bump(t, 0, lands + 0.15);
                caster.Lean = -6 * Bump(t, 0, gather);
                if (touch)
                {
                    caster.Offset = forward * 0.3f * Bump(t, gather - 0.05, lands + 0.15);
                }

                // Gathering, then on its way.
                var grow = (float)Math.Clamp(t / gather, 0, 1);
                var u = travel > 0 ? Math.Clamp((t - gather) / travel, 0, 1) : 1;
                orb.Visible = t < lands;
                orb.Scale = Vector3.One * (0.3f + grow * 0.7f) * (1 + 0.12f * Mathf.Sin((float)t * 30));
                orb.Position = t < gather ? caster.Hand(forward) + Vector3.Up * 0.2f
                    : touch ? caster.Hand(forward).Lerp(aim, (float)((t - gather) / 0.25) * 0.7f)
                    : from.Lerp(to, (float)u) + Vector3.Up * 0.5f * (float)(u * (1 - u));

                if (!bursting && t >= lands && shape == SpellShape.Volume)
                {
                    bursting = true;
                    foreach (var square in w.Plan.Squares ?? [])
                    {
                        var tile = new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.94f, 0.04f, 0.94f) },
                            MaterialOverride = Shine(colour, 0.8f),
                            Position = new Vector3(square.X + 0.5f, 0.03f, square.Y + 0.5f),
                        };
                        effects!.AddChild(tile);
                        squares.Add(tile);
                    }

                    Spark(origin, colour, 0.9f, life: 0.5);
                }

                if (squares.Count > 0)
                {
                    var b = (float)Math.Clamp((t - lands) / Math.Max(0.01, burst + after), 0, 1);
                    foreach (var tile in squares)
                    {
                        tile.Scale = new Vector3(1, 1 + 14 * Mathf.Sin(b * Mathf.Pi), 1);
                        ((StandardMaterial3D)tile.MaterialOverride).AlbedoColor = colour with { A = 0.8f * (1 - b * b) };
                    }
                }

                if (!marked && t >= lands + burst && w.Spell.SpellType != SpellType.Harm)
                {
                    marked = true;
                    foreach (var target in targets.Count > 0 ? targets : self ? [caster] : [])
                    {
                        if (w.Spell.SpellType == SpellType.Heal)
                        {
                            Motes(target.Anchor, colour);
                        }
                        else
                        {
                            Ring(target.Anchor, colour);
                        }
                    }

                    if (targets.Count == 0 && !self && shape != SpellShape.Volume)
                    {
                        Spark(to, colour, 0.3f);
                    }
                }
            }, () =>
            {
                orb.QueueFree();
                foreach (var tile in squares)
                {
                    tile.QueueFree();
                }
            });
        }

        /// <summary>One creature's share of a Harm spell: it flinches in the spell's colour, sidesteps it, or turns it back.</summary>
        private Act SpellLands(SpellStruck s)
        {
            var caster = figures[s.Caster];
            var target = figures[s.Target];
            var (forward, side) = Across(caster, target);
            var colour = SpellColour(s.Spell);
            var sparked = false;
            return new Act(s, s.Hit || s.Dodged ? 0.42 : 0.12, t =>
            {
                if (s.Hit)
                {
                    target.Offset = forward * (s.Critical ? 0.2f : 0.12f) * Bump(t, 0, 0.35);
                    target.Lean = (s.Critical ? -20 : -10) * Bump(t, 0, 0.35);
                    if (!sparked)
                    {
                        sparked = true;
                        Spark(target.Chest, colour, s.Critical ? 0.45f : 0.3f);
                    }
                }
                else if (s.Dodged)
                {
                    target.Offset = side * 0.4f * Bump(t, 0, 0.4);
                    if (s.Riposte && !sparked && t > 0.2)
                    {
                        sparked = true;
                        Spark(caster.Chest, colour, 0.35f);
                    }
                }
            });
        }

        // ---- Effects ----

        private static readonly Color Hurt = Color.FromHtml("#d55e00");

        /// <summary>A flash that swells and fades where something landed.</summary>
        private void Spark(Vector3 at, Color colour, float size, double life = 0.3)
        {
            var spark = Glow(colour, size / 2);
            spark.Position = at;
            var age = 0.0;
            Fade(spark, life, dt =>
            {
                age += dt;
                var k = (float)(age / life);
                spark.Scale = Vector3.One * (0.4f + 1.2f * k);
                ((StandardMaterial3D)spark.MaterialOverride).AlbedoColor = colour with { A = 1 - k };
            });
        }

        /// <summary>Motes rising off a creature: a heal taking.</summary>
        private void Motes(Vector3 at, Color colour)
        {
            var random = new Random();
            for (var i = 0; i < 8; i++)
            {
                var mote = Glow(colour, 0.05f);
                var start = at + new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 0.4f, (float)random.NextDouble() - 0.5f) * 0.6f;
                mote.Position = start;
                var age = -random.NextDouble() * 0.25;
                Fade(mote, 0.9, dt =>
                {
                    age += dt;
                    var k = (float)Math.Clamp(age / 0.65, 0, 1);
                    mote.Position = start + Vector3.Up * 1.1f * k;
                    ((StandardMaterial3D)mote.MaterialOverride).AlbedoColor = colour with { A = k <= 0 ? 0 : 1 - k };
                });
            }
        }

        /// <summary>A ring that spreads from a creature's feet: a blessing, a bane, a hold laid on it.</summary>
        private void Ring(Vector3 at, Color colour)
        {
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.44f },
                MaterialOverride = Shine(colour, 0.9f),
                Position = at + Vector3.Up * 0.05f,
            };
            effects!.AddChild(ring);
            var age = 0.0;
            Fade(ring, 0.6, dt =>
            {
                age += dt;
                var k = (float)(age / 0.6);
                ring.Scale = Vector3.One * (0.6f + 0.8f * k);
                ring.Position = at + Vector3.Up * (0.05f + 0.7f * k);
                ((StandardMaterial3D)ring.MaterialOverride).AlbedoColor = colour with { A = 0.9f * (1 - k) };
            });
        }

        /// <summary>Runs <paramref name="update"/> every frame for <paramref name="life"/> seconds, then frees <paramref name="node"/>.</summary>
        private void Fade(Node3D node, double life, Action<double> update)
        {
            var age = 0.0;
            var ticker = new Ticker(dt =>
            {
                age += dt;
                if (age >= life || !IsInstanceValid(node))
                {
                    return false;
                }

                update(dt);
                return true;
            });
            node.AddChild(ticker);
            ticker.Done += () => node.QueueFree();
        }

        private MeshInstance3D Glow(Color colour, float radius)
        {
            var glow = new MeshInstance3D { Mesh = new SphereMesh { Radius = radius, Height = radius * 2 }, MaterialOverride = Shine(colour, 1) };
            effects!.AddChild(glow);
            return glow;
        }

        private static StandardMaterial3D Shine(Color colour, float alpha) => new()
        {
            AlbedoColor = colour with { A = alpha },
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = false,
        };

        internal enum Missile { Arrow, Bolt, Stone, Thrown }

        internal static Missile MissileOf(AttackProfile with)
        {
            var name = $"{with.Name} {with.Weapon?.Name}".ToLowerInvariant();
            return with.IsThrown ? Missile.Thrown
                : name.Contains("crossbow") ? Missile.Bolt
                : name.Contains("sling") || name.Contains("stone") || name.Contains("rock") ? Missile.Stone
                : Missile.Arrow;
        }

        private MeshInstance3D MissileMesh(Missile kind)
        {
            // Pale and a little larger than life, so a shot reads against the ground at any zoom.
            var wood = new StandardMaterial3D { AlbedoColor = Color.FromHtml("#e8e6e3"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            var metal = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.72f, 0.76f), Metallic = 0.6f, Roughness = 0.35f };
            var missile = new MeshInstance3D
            {
                Mesh = kind switch
                {
                    Missile.Bolt => new BoxMesh { Size = new Vector3(0.07f, 0.07f, 0.4f) },
                    Missile.Stone => new SphereMesh { Radius = 0.08f, Height = 0.16f },
                    Missile.Thrown => new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.5f) },
                    _ => new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.65f) },
                },
                MaterialOverride = kind is Missile.Thrown ? metal : wood,
                Visible = false,
            };
            effects!.AddChild(missile);
            return missile;
        }

        /// <summary>
        /// A spell's colour, from the colour-blind-safe palette: an element its own (fire orange, water
        /// blue, air pale, earth ochre); otherwise by what it does — harm vermilion-violet, heal
        /// bluish green, buff gold, bane and control purple, motion sky blue, illusion bone.
        /// </summary>
        internal static Color SpellColour(WoundBound.Rules.Models.SpellDefinition spell) =>
            SpellArea.ElementOf(spell) switch
            {
                Element.Fire => Color.FromHtml("#e69f00"),
                Element.Water => Color.FromHtml("#0072b2"),
                Element.Air => Color.FromHtml("#cfe8f5"),
                Element.Earth => Color.FromHtml("#a07a3c"),
                _ => spell.SpellType switch
                {
                    SpellType.Harm => Color.FromHtml("#cc79a7"),
                    SpellType.Heal => Color.FromHtml("#009e73"),
                    SpellType.Buff => Color.FromHtml("#f0e442"),
                    SpellType.Bane or SpellType.Control => Color.FromHtml("#8e5cc2"),
                    SpellType.Motion => Color.FromHtml("#56b4e9"),
                    _ => Color.FromHtml("#e8e6e3"),
                },
            };

        // ---- Timing ----

        /// <summary>0 before <paramref name="a"/>, rising to 1 halfway to <paramref name="b"/>, and back to 0 at it.</summary>
        private static float Bump(double t, double a, double b) =>
            t <= a || t >= b ? 0 : Mathf.Sin((float)((t - a) / (b - a)) * Mathf.Pi);

        /// <summary>A swing: back to the wind-up by <paramref name="wound"/>, through the stroke by <paramref name="struck"/>, home by <paramref name="home"/>.</summary>
        private static float SwingAt(double t, double start, double wound, double struck, double home)
        {
            static float Ease(double k) => (float)(0.5 - 0.5 * Math.Cos(Math.Clamp(k, 0, 1) * Math.PI));
            return t < start ? 0
                : t < wound ? -75 * Ease((t - start) / (wound - start))
                : t < struck ? Mathf.Lerp(-75, 40, Ease((t - wound) / (struck - wound)))
                : t < home ? Mathf.Lerp(40, 0, Ease((t - struck) / (home - struck)))
                : 0;
        }

        private static (Vector3 Forward, Vector3 Side) Across(Figure from, Figure to) => Across(from, to.Anchor);

        private static (Vector3 Forward, Vector3 Side) Across(Figure from, Vector3 to)
        {
            var d = (to - from.Anchor) with { Y = 0 };
            var forward = d.LengthSquared() < 1e-4f ? Vector3.Forward : d.Normalized();
            return (forward, forward.Cross(Vector3.Up));
        }

        private static float Angle(Vector3 forward) => Mathf.Atan2(forward.X, forward.Z);

        /// <summary>One act on the board: a length, and what it looks like at each moment of it.</summary>
        private sealed class Act(Happening happening, double duration, Action<double> step, Action? finish = null)
        {
            public Happening Happening { get; } = happening;

            public double Duration { get; } = duration;

            public double Time { get; set; }

            /// <summary>The happenings this act shows - more than one when several creatures walk together.</summary>
            public IReadOnlyList<Happening> Covers { get; set; } = [happening];

            public void Step(double t) => step(t);

            public void Finish() => finish?.Invoke();
        }

        /// <summary>Calls back every frame until the callback says it's done.</summary>
        private sealed partial class Ticker : Node
        {
            private readonly Func<double, bool> tick;

            public event Action? Done;

            public Ticker()
            {
                tick = _ => false;
            }

            public Ticker(Func<double, bool> tick) => this.tick = tick;

            public override void _Process(double delta)
            {
                if (!tick(delta))
                {
                    SetProcess(false);
                    Done?.Invoke();
                }
            }
        }

        // ---- The figure's pose ----

        private sealed partial class Figure
        {
            private static readonly Vector3 Shoulder = new(0.2f, 0.85f, 0);
            private readonly Node3D arm = new();
            private readonly Node3D offArm = new();
            private readonly Node3D leftHip = new();
            private readonly Node3D rightHip = new();
            private float stridePhase;
            private float walking;
            private bool striding;
            private bool shownDown;

            public Color Colour { get; } = Colors.White;

            /// <summary>Where it stands, eased; the pose is laid over this.</summary>
            public Vector3 Anchor { get; set; }

            /// <summary>Carries the stride on by <paramref name="distance"/> travelled: a step every half square or so.</summary>
            public void Stride(float distance)
            {
                if (distance > 1e-4f)
                {
                    stridePhase += distance / 0.5f * Mathf.Pi;
                    striding = true;
                }
            }

            /// <summary>How many acts still to be shown involve it: while any are, it doesn't fall before it's struck.</summary>
            public int Holds { get; set; }

            public Vector3 Offset { get; set; }

            public float? Face { get; set; }

            public float Lean { get; set; }

            public float Swing { get; set; }

            /// <summary>The other arm, raised forward by this many degrees (negative) — a grab, a shove.</summary>
            public float OffSwing { get; set; }

            /// <summary>A leg swept forward — a trip.</summary>
            public float Kick { get; set; }

            public Vector3 Chest => Anchor + Vector3.Up * (chestHeight + hover) * BodyScale;

            private float BodyScale => body.Scale.Y;

            public Vector3 Hand(Vector3 forward) => Anchor + Vector3.Up * (handHeight + hover) * BodyScale + forward * handReach * BodyScale;

            public void Place(Vector3 at)
            {
                Anchor = at;
                Position = at + Offset;
            }

            public void ResetPose()
            {
                Offset = Vector3.Zero;
                Face = null;
                Lean = 0;
                Swing = 0;
                OffSwing = 0;
                Kick = 0;
            }
        }
    }
}
