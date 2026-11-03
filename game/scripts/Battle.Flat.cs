using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// The flat board's animation: the same <see cref="Happening"/>s the 3D board plays, drawn top
    /// down and played one at a time — a token lunging and a slash across its target, a missile in
    /// flight, a spell gathered and sent, a burst where a blow or a spell lands with the Body it
    /// cost, a sidestep for a dodge. Tokens slide along the routes they walked and leave a dotted
    /// trail until the next turn begins. The rules have already settled each one; this only shows it.
    /// </summary>
    public partial class Battle
    {
        private readonly Queue<Happening> flatQueue = new();
        private FlatAct? flatPlaying;

        /// <summary>Tokens with something still to be shown, kept where they were until it has been.</summary>
        private readonly Dictionary<Combatant, int> flatHolds = new();

        /// <summary>How far an act has pushed a token from its square — a lunge, a flinch, a sidestep.</summary>
        private readonly Dictionary<Combatant, Vector2> flatNudge = new();

        /// <summary>What's left on the board once an act has moved on: sparks, rings, squares lit, numbers rising.</summary>
        private readonly List<FlatEffect> flatEffects = new();

        /// <summary>The squares walked this turn: where, whose colour, and whether it was carried rather than walked.</summary>
        private readonly List<(GridPosition Square, int Across, Color Colour, bool Forced)> crumbs = new();

        private static readonly Color Hurt = Color.FromHtml("#d55e00");
        private static readonly Color Handhold = Color.FromHtml("#f0e442");

        /// <summary>Whether the board is still showing something — the next step waits until it isn't.</summary>
        private bool BoardBusy => In3D ? board3D!.Busy : flatPlaying is not null || flatQueue.Count > 0;

        private void PlayFlat(Happening happening)
        {
            flatQueue.Enqueue(happening);
            foreach (var who in Involved(happening))
            {
                flatHolds[who] = flatHolds.GetValueOrDefault(who) + 1;
            }
        }

        private void ClearFlat()
        {
            flatQueue.Clear();
            flatPlaying = null;
            flatHolds.Clear();
            flatNudge.Clear();
            flatEffects.Clear();
            crumbs.Clear();
        }

        private bool FlatHeld(Combatant c) => flatHolds.GetValueOrDefault(c) > 0;

        private Vector2 NudgeOf(Combatant c) => !In3D && flatNudge.TryGetValue(c, out var by) ? by : Vector2.Zero;

        private static IEnumerable<Combatant> Involved(Happening happening) => happening switch
        {
            Moved m => [m.Mover],
            Handled h => [h.Actor, h.Target],
            Struck s => [s.Attacker, s.Target],
            SpellWorked w => w.Plan.Targets.Prepend(w.Caster),
            SpellStruck s => [s.Caster, s.Target],
            _ => [],
        };

        /// <summary>Plays the flat board's acts on; true while there's anything to draw.</summary>
        private bool StepFlat(double delta)
        {
            for (var i = flatEffects.Count - 1; i >= 0; i--)
            {
                flatEffects[i].Age += delta;
                if (flatEffects[i].Age >= flatEffects[i].Life)
                {
                    flatEffects.RemoveAt(i);
                }
            }

            if (flatPlaying is null && flatQueue.TryDequeue(out var next))
            {
                if (next is Moved first)
                {
                    // Moves one after another — a rider and its horse, foes closing in at the round's end — go together.
                    var together = new List<Moved> { first };
                    while (flatQueue.TryPeek(out var after) && after is Moved also && together.All(m => m.Mover != also.Mover))
                    {
                        flatQueue.Dequeue();
                        together.Add(also);
                    }

                    flatPlaying = FlatWalks(together);
                }
                else
                {
                    flatPlaying = BeginFlat(next);
                    if (flatPlaying is null)
                    {
                        ReleaseFlat(next);
                    }
                }
            }

            if (flatPlaying is { } act)
            {
                act.Time += delta;
                act.Step?.Invoke(Math.Min(act.Time, act.Duration));
                if (act.Time >= act.Duration)
                {
                    foreach (var done in act.Covers)
                    {
                        ReleaseFlat(done);
                    }

                    flatPlaying = null;
                }
            }

            return flatPlaying is not null || flatQueue.Count > 0 || flatEffects.Count > 0;
        }

        private void ReleaseFlat(Happening happening)
        {
            foreach (var who in Involved(happening))
            {
                flatNudge.Remove(who);
                if (flatHolds.TryGetValue(who, out var holds))
                {
                    if (holds <= 1)
                    {
                        flatHolds.Remove(who);
                    }
                    else
                    {
                        flatHolds[who] = holds - 1;
                    }
                }
            }
        }

        /// <summary>Whether the party sees any of it; an act nobody could see is skipped.</summary>
        private bool SeenFlat(params Combatant[] who) => who.Any(Shown);

        private FlatAct? BeginFlat(Happening happening) => happening switch
        {
            Struck { With.IsRanged: true } s when SeenFlat(s.Attacker, s.Target) => FlatShot(s),
            Struck s when SeenFlat(s.Attacker, s.Target) => FlatBlow(s),
            SpellWorked w when SeenFlat(w.Plan.Targets.Prepend(w.Caster).ToArray()) => FlatWorking(w),
            SpellStruck s when SeenFlat(s.Caster, s.Target) => FlatSpellLands(s),
            Handled h when SeenFlat(h.Actor, h.Target) => FlatHandling(h),
            TurnBegan => new FlatAct(happening, 0) { Step = _ => crumbs.Clear() },
            _ => null,
        };

        /// <summary>The centre of <paramref name="c"/>'s token on <paramref name="square"/>, pinned to the edge when off the map.</summary>
        private Vector2 FlatCentre(Combatant c, GridPosition square) =>
            SquareRect(new GridPosition(Mathf.Clamp(square.X, 0, encounter.Width - 1), Mathf.Clamp(square.Y, 0, encounter.Height - 1))).GetCenter()
            + Vector2.One * (CellSize * (c.Across - 1) / 2f);

        /// <summary>Where a token stands now, before any act has nudged it.</summary>
        private Vector2 Standing(Combatant c) => tokenAt.TryGetValue(c, out var at) ? at : SquareCentreOf(c);

        // ---- The acts ----

        /// <summary>Tokens sliding square by square along the routes they took, together; a trail of dots (dashes if carried) left behind.</summary>
        private FlatAct FlatWalks(List<Moved> moves)
        {
            var walks = moves.Where(m => m.Path.Count > 1 && Shown(m.Mover))
                .Select(m => (Move: m, Points: m.Path.Select(sq => FlatCentre(m.Mover, sq)).ToList()))
                .ToList();
            var lengths = walks.Select(w => w.Points.Zip(w.Points.Skip(1), (a, b) => a.DistanceTo(b)).Sum()).ToList();
            var durations = walks.Select((w, i) => Math.Clamp(lengths[i] / CellSize / (w.Move.Forced ? 11.0 : 6.0), 0.12, 1.4)).ToList();
            var dropped = walks.Select(_ => 0).ToList();
            return new FlatAct(moves[0], durations.DefaultIfEmpty(0).Max())
            {
                Covers = moves,
                Step = t =>
                {
                    for (var i = 0; i < walks.Count; i++)
                    {
                        var (move, points) = walks[i];
                        var along = lengths[i] * (float)Math.Clamp(t / durations[i], 0, 1);
                        var (at, passed) = Along(points, along);
                        tokenAt[move.Mover] = at;
                        while (dropped[i] <= passed && dropped[i] < points.Count)
                        {
                            crumbs.Add((move.Path[dropped[i]], move.Mover.Across, ColourOf(move.Mover), move.Forced));
                            dropped[i]++;
                        }
                    }
                },
            };
        }

        /// <summary>The point <paramref name="along"/> pixels down a route, and how many of its corners it has passed.</summary>
        private static (Vector2 At, int Passed) Along(List<Vector2> points, float along)
        {
            for (var k = 1; k < points.Count; k++)
            {
                var leg = points[k - 1].DistanceTo(points[k]);
                if (along <= leg)
                {
                    return (points[k - 1].Lerp(points[k], leg < 1e-3f ? 1 : along / leg), k - 1);
                }

                along -= leg;
            }

            return (points[^1], points.Count - 1);
        }

        /// <summary>A blow: the attacker lunges and a slash crosses the target — a burst and the Body lost, a sidestep, or a sidestep and a slash back.</summary>
        private FlatAct FlatBlow(Struck s)
        {
            var from = Standing(s.Attacker);
            var to = Standing(s.Target);
            var (forward, side) = Across(from, to);
            var cell = CellSize;
            const double impact = 0.3;
            var duration = s.Riposte ? 0.95 : s.Critical ? 0.8 : 0.65;
            var landed = false;
            var countered = false;
            return new FlatAct(s, duration)
            {
                Step = t =>
                {
                    flatNudge[s.Attacker] = forward * cell * (0.3f * Bump(t, 0.1, 0.5) - 0.06f * Bump(t, 0, 0.2));
                    if (s.Hit)
                    {
                        flatNudge[s.Target] = forward * cell * (s.Critical ? 0.18f : 0.1f) * Bump(t, impact, impact + 0.3);
                        if (!landed && t >= impact)
                        {
                            landed = true;
                            Burst(to, s.Critical ? Hurt.Lightened(0.3f) : Hurt, cell * (s.Critical ? 0.55f : 0.38f));
                            Rising(to, Wounds(s.Outcome.Dealt, s.Critical), s.Critical ? Hurt.Lightened(0.35f) : Hurt.Lightened(0.15f));
                        }
                    }
                    else if (s.Dodged)
                    {
                        flatNudge[s.Target] = side * cell * 0.38f * Bump(t, 0.12, s.Riposte ? 0.55 : 0.6);
                        if (!landed && t >= impact)
                        {
                            landed = true;
                            Rising(to, s.Riposte ? "Riposte!" : "dodged", Bone);
                        }

                        if (s.Riposte)
                        {
                            flatNudge[s.Attacker] += -forward * cell * 0.2f * Bump(t, 0.6, 0.95);
                            if (!countered && t >= 0.68)
                            {
                                countered = true;
                                Burst(from, Hurt.Lightened(0.3f), cell * 0.45f);
                            }
                        }
                    }
                    else if (!landed && t >= impact)
                    {
                        landed = true;
                        Rising(to, "miss", Bone with { A = 0.8f });
                    }
                },
                Draw = t =>
                {
                    Slash(to, forward, cell, (t - 0.18) / 0.22, s.Critical);
                    if (s.Riposte)
                    {
                        Slash(from, -forward, cell, (t - 0.55) / 0.22, true);
                    }
                },
            };
        }

        /// <summary>A shot: a missile drawn, loosed and in flight — into the target, past a dodge, or wide.</summary>
        private FlatAct FlatShot(Struck s)
        {
            var from = Standing(s.Attacker);
            var to = Standing(s.Target);
            var (forward, side) = Across(from, to);
            var cell = CellSize;
            var kind = Board3D.MissileOf(s.With);
            var end = s.Hit ? to : to + forward * cell * 1.4f + (s.Dodged ? Vector2.Zero : side * cell * 0.5f);
            var squares = from.DistanceTo(to) / cell;
            const double draw = 0.2;
            var flight = Math.Clamp(squares * 0.05, 0.22, 0.6) * (s.Hit ? 1 : 1.4);
            var arrive = draw + flight * (s.Hit ? 1 : 0.7);
            // Lobbed a little — up the screen — as an arrow or stone would be; a bolt flies flat.
            var lob = cell * squares * kind switch { Board3D.Missile.Arrow => 0.06f, Board3D.Missile.Stone => 0.05f, Board3D.Missile.Thrown => 0.04f, _ => 0.015f };
            var shooterSeen = Shown(s.Attacker);
            var landed = false;
            Vector2 PointAt(double u) => from.Lerp(end, (float)u) + Vector2.Up * lob * 4 * (float)(u * (1 - u));
            return new FlatAct(s, draw + flight + 0.2)
            {
                Step = t =>
                {
                    flatNudge[s.Attacker] = -forward * cell * 0.08f * Bump(t, 0, draw + 0.1);
                    if (s.Hit)
                    {
                        flatNudge[s.Target] = forward * cell * 0.1f * Bump(t, arrive, arrive + 0.25);
                    }
                    else if (s.Dodged)
                    {
                        flatNudge[s.Target] = side * cell * 0.38f * Bump(t, arrive - 0.2, arrive + 0.25);
                    }

                    if (!landed && t >= arrive)
                    {
                        landed = true;
                        if (s.Hit)
                        {
                            Burst(to, s.Critical ? Hurt.Lightened(0.3f) : Hurt, cell * (s.Critical ? 0.5f : 0.32f));
                            Rising(to, Wounds(s.Outcome.Dealt, s.Critical), Hurt.Lightened(0.15f));
                        }
                        else if (s.Riposte)
                        {
                            // A dodged critical shot is a critical failure: the shooter's own hurt.
                            Burst(from, Hurt, cell * 0.3f);
                            Rising(to, "dodged", Bone);
                        }
                        else
                        {
                            Rising(to, s.Dodged ? "dodged" : "miss", Bone with { A = 0.8f });
                        }
                    }
                },
                Draw = t =>
                {
                    var u = (t - draw) / flight;
                    if (u is <= 0 or >= 1 || (!shooterSeen && u < 0.4))
                    {
                        return;
                    }

                    var at = PointAt(u);
                    var heading = (PointAt(Math.Min(1, u + 0.03)) - at).Normalized();
                    // A faint streak behind it, so the eye can find where it came from.
                    DrawLine(PointAt(Math.Max(shooterSeen ? 0 : 0.4, u - 0.25)), at, Bone with { A = 0.25f }, 2);
                    Missile(at, heading, kind, cell, (float)u);
                },
            };
        }

        /// <summary>
        /// A spell worked: gathered at the caster, sent to where it was aimed — or laid on by touch —
        /// and bursting over every square it covers; a heal rises off whoever it mends, and anything
        /// else rings them where they stand.
        /// </summary>
        private FlatAct FlatWorking(SpellWorked w)
        {
            var colour = Board3D.SpellColour(w.Spell);
            var shape = WoundBound.Game.Combat.SpellArea.ShapeOf(new SpellCast(w.Spell));
            var cell = CellSize;
            var from = Standing(w.Caster);
            var origin = SquareRect(w.Plan.Origin).GetCenter();
            var targets = w.Plan.Targets.Where(c => c != w.Caster).ToList();
            var aim = shape == SpellShape.Volume ? origin : targets.Count > 0 ? Standing(targets[0]) : origin;
            var (forward, _) = Across(from, aim);
            var touch = w.Spell.Range == SpellRange.Touch;
            var self = !touch && from.DistanceTo(origin) < cell * 0.6f && targets.Count == 0;
            const double gather = 0.35;
            var travel = touch || self ? 0.0 : Math.Clamp(from.DistanceTo(aim) / cell * 0.035, 0.18, 0.5);
            var lands = gather + (touch ? 0.2 : travel);
            var after = w.Spell.SpellType == SpellType.Harm ? 0.1 : 0.5;
            var marked = false;
            return new FlatAct(w, lands + after)
            {
                Step = t =>
                {
                    flatNudge[w.Caster] = touch ? forward * cell * 0.3f * Bump(t, gather - 0.05, lands + 0.15) : Vector2.Zero;
                    if (marked || t < lands)
                    {
                        return;
                    }

                    marked = true;
                    if (shape == SpellShape.Volume)
                    {
                        foreach (var square in w.Plan.Area)
                        {
                            var rect = SquareRect(square);
                            flatEffects.Add(new FlatEffect(0.8, k =>
                            {
                                DrawRect(rect.Grow(-1), colour with { A = 0.55f * (1 - k) });
                                DrawRect(rect.Grow(-1 - 4 * k), colour with { A = 0.9f * (1 - k) }, filled: false, width: 2);
                            }));
                        }

                        Burst(origin, colour, cell * 0.7f);
                    }
                    else if (targets.Count == 0 && !self)
                    {
                        Burst(aim, colour, cell * 0.3f);
                    }

                    if (w.Spell.SpellType == SpellType.Harm)
                    {
                        return;
                    }

                    foreach (var target in targets.Count > 0 ? targets : self ? [w.Caster] : [])
                    {
                        var at = Standing(target);
                        if (w.Spell.SpellType == SpellType.Heal)
                        {
                            Motes(at, colour, cell);
                        }
                        else
                        {
                            Ripple(at, colour, cell);
                        }
                    }
                },
                Draw = t =>
                {
                    if (t >= lands)
                    {
                        return;
                    }

                    // The orb: swelling at the caster's hand, then on its way, arcing a little.
                    var grow = (float)Math.Clamp(t / gather, 0, 1);
                    var u = t < gather ? 0 : travel > 0 ? Math.Clamp((t - gather) / travel, 0, 1) : (t - gather) / 0.2 * 0.7;
                    var hand = from + forward * cell * 0.3f;
                    var at = hand.Lerp(aim, (float)u) + Vector2.Up * cell * 0.6f * (float)(u * (1 - u)) * (travel > 0 ? 1 : 0);
                    var r = Mathf.Max(5, cell * 0.16f) * (0.35f + 0.65f * grow) * (1 + 0.12f * Mathf.Sin((float)t * 30));
                    DrawCircle(at, r * 1.9f, colour with { A = 0.25f });
                    DrawCircle(at, r, colour);
                    DrawCircle(at, r * 0.45f, Colors.White with { A = 0.85f });
                },
            };
        }

        /// <summary>One creature's share of a Harm spell: a burst in the spell's colour, a sidestep, or a sidestep that turns it back.</summary>
        private FlatAct FlatSpellLands(SpellStruck s)
        {
            var colour = Board3D.SpellColour(s.Spell);
            var cell = CellSize;
            var from = Standing(s.Caster);
            var to = Standing(s.Target);
            var (forward, side) = Across(from, to);
            var landed = false;
            return new FlatAct(s, s.Hit || s.Dodged ? 0.42 : 0.15)
            {
                Step = t =>
                {
                    if (s.Hit)
                    {
                        flatNudge[s.Target] = forward * cell * (s.Critical ? 0.16f : 0.1f) * Bump(t, 0, 0.35);
                    }
                    else if (s.Dodged)
                    {
                        flatNudge[s.Target] = side * cell * 0.38f * Bump(t, 0, 0.4);
                    }

                    if (landed)
                    {
                        return;
                    }

                    landed = true;
                    if (s.Hit)
                    {
                        Burst(to, colour, cell * (s.Critical ? 0.55f : 0.38f));
                        if (s.Critical)
                        {
                            Rising(to, "critical!", colour.Lightened(0.3f));
                        }
                    }
                    else if (s.Dodged)
                    {
                        Rising(to, s.Riposte ? "turned back!" : "dodged", Bone);
                        if (s.Riposte)
                        {
                            Burst(from, colour, cell * 0.4f);
                        }
                    }
                    else
                    {
                        Rising(to, "miss", Bone with { A = 0.8f });
                    }
                },
            };
        }

        /// <summary>
        /// Hands on somebody: a Grab closes a ring round the target, a Shove drives chevrons into it,
        /// a Trip sweeps under it; breaking free bursts a ring outward. Stood firm, dodged or
        /// overbalanced, it says so.
        /// </summary>
        private FlatAct FlatHandling(Handled h)
        {
            var cell = CellSize;
            var from = Standing(h.Actor);
            var to = Standing(h.Target);
            var (forward, side) = Across(from, to);
            const double impact = 0.35;
            var said = false;
            return new FlatAct(h, 0.75)
            {
                Step = t =>
                {
                    flatNudge[h.Actor] = h.Action is null
                        ? side * cell * 0.06f * Mathf.Sin((float)t * 40) * Bump(t, 0, 0.6) - (h.Worked ? forward * cell * 0.2f * Bump(t, 0.4, 0.75) : Vector2.Zero)
                        : forward * cell * (h.Action == ControlAction.Shove ? 0.34f : 0.26f) * Bump(t, 0.1, 0.65);
                    if (h.Worked && h.Action is not null)
                    {
                        flatNudge[h.Target] = h.Action == ControlAction.Grab ? -forward * cell * 0.1f * Bump(t, impact, 0.75) : forward * cell * 0.12f * Bump(t, impact, 0.75);
                    }
                    else if (!h.Worked && !h.Resisted && h.Action is not null)
                    {
                        flatNudge[h.Target] = side * cell * 0.32f * Bump(t, 0.2, 0.7);
                    }

                    if (said || t < impact)
                    {
                        return;
                    }

                    said = true;
                    var verb = h.Action switch { ControlAction.Grab => "Grab", ControlAction.Shove => "Shove", ControlAction.Trip => "Trip", _ => "breaks free" };
                    Rising(h.Action is null ? from : to,
                        h.Worked ? (h.Action is null ? "free!" : verb + (h.Critical ? "!" : string.Empty))
                        : h.Action is null ? "still held" : h.Resisted ? "stood firm" : "dodged",
                        h.Worked ? Handhold : Bone);
                    if (h.Overbalanced)
                    {
                        Rising(from + Vector2.Down * cell * 0.3f, "overbalanced", Bone with { A = 0.8f });
                    }
                },
                Draw = t =>
                {
                    var k = (float)Math.Clamp((t - 0.15) / 0.45, 0, 1);
                    var a = 1 - Mathf.Clamp((float)(t - 0.55) / 0.2f, 0, 1);
                    if (k <= 0 || a <= 0)
                    {
                        return;
                    }

                    var colour = (h.Worked ? Handhold : Bone) with { A = a * (h.Worked ? 1 : 0.6f) };
                    switch (h.Action)
                    {
                        case ControlAction.Grab:
                            // Closing in round them.
                            DrawArc(to, cell * (0.75f - 0.3f * k), 0, Mathf.Tau, 32, colour, 3);
                            break;
                        case ControlAction.Shove:
                            for (var i = 0; i < 2; i++)
                            {
                                var tip = to + forward * cell * (0.1f + 0.18f * i + 0.15f * k);
                                DrawPolyline([tip - forward * cell * 0.14f + side * cell * 0.16f, tip, tip - forward * cell * 0.14f - side * cell * 0.16f], colour, 3);
                            }

                            break;
                        case ControlAction.Trip:
                            // A leg swept through, low, from one side to the other.
                            var low = to + forward * cell * 0.05f;
                            var swept = Mathf.Lerp(-1.3f, 1.3f, k);
                            DrawArc(low, cell * 0.42f, side.Angle() - 1.3f + Mathf.Pi, side.Angle() + swept + Mathf.Pi, 16, colour, 4);
                            break;
                        default:
                            DrawArc(from, cell * (0.42f + 0.35f * k), 0, Mathf.Tau, 32, colour, 3);
                            break;
                    }
                },
            };
        }

        // ---- Effects ----

        private static string Wounds(int dealt, bool critical) => (dealt > 0 ? $"−{dealt}" : "0") + (critical ? "!" : string.Empty);

        /// <summary>A starburst that swells and fades where something landed.</summary>
        private void Burst(Vector2 at, Color colour, float size)
        {
            var spin = (float)GD.RandRange(0, Mathf.Pi);
            flatEffects.Add(new FlatEffect(0.45, k =>
            {
                var a = 1 - k;
                DrawCircle(at, size * (0.35f + 0.4f * k), colour with { A = 0.45f * a });
                for (var i = 0; i < 8; i++)
                {
                    var d = Vector2.FromAngle(spin + Mathf.Tau * i / 8);
                    DrawLine(at + d * size * (0.3f + 0.4f * k), at + d * size * (0.6f + 0.6f * k), colour with { A = a }, 3);
                }
            }));
        }

        /// <summary>A word or a number floating up off a token and fading.</summary>
        private void Rising(Vector2 at, string text, Color colour)
        {
            var font = ThemeDB.FallbackFont;
            var cell = CellSize;
            var size = Mathf.Max(11, cell / 3);
            flatEffects.Add(new FlatEffect(1.1, k =>
            {
                var a = k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f;
                var p = at + new Vector2(-cell, -cell * (0.35f + 0.45f * k));
                DrawString(font, p + new Vector2(1.5f, 1.5f), text, HorizontalAlignment.Center, cell * 2, size, new Color(0, 0, 0, 0.7f * a));
                DrawString(font, p, text, HorizontalAlignment.Center, cell * 2, size, colour with { A = colour.A * a });
            }));
        }

        /// <summary>A ring opening outward round a creature a spell has settled on.</summary>
        private void Ripple(Vector2 at, Color colour, float cell) =>
            flatEffects.Add(new FlatEffect(0.7, k => DrawArc(at, cell * (0.3f + 0.4f * k), 0, Mathf.Tau, 32, colour with { A = 1 - k }, 3)));

        /// <summary>Little crosses rising off a creature that's been mended.</summary>
        private void Motes(Vector2 at, Color colour, float cell)
        {
            for (var i = 0; i < 5; i++)
            {
                var start = at + new Vector2((i - 2) * cell * 0.16f, cell * 0.15f * (i % 2));
                var delay = i * 0.08f;
                flatEffects.Add(new FlatEffect(0.9, k =>
                {
                    var u = Mathf.Clamp((k - delay) / (1 - delay), 0, 1);
                    if (u <= 0)
                    {
                        return;
                    }

                    var p = start + Vector2.Up * cell * 0.6f * u;
                    var arm = cell * 0.07f;
                    var c = colour with { A = 1 - u };
                    DrawLine(p - new Vector2(arm, 0), p + new Vector2(arm, 0), c, 2.5f);
                    DrawLine(p - new Vector2(0, arm), p + new Vector2(0, arm), c, 2.5f);
                }));
            }
        }

        /// <summary>A blade's arc swept across <paramref name="at"/>, <paramref name="k"/> of the way through (0 to 1, then fading).</summary>
        private void Slash(Vector2 at, Vector2 forward, float cell, double k, bool heavy)
        {
            if (k <= 0 || k >= 1.6)
            {
                return;
            }

            var heading = forward.Angle();
            var head = (float)Math.Min(k, 1);
            var tail = (float)Math.Max(0, k - 0.45);
            var a = k < 1 ? 1 : 1 - (float)(k - 1) / 0.6f;
            var radius = cell * 0.42f;
            var centre = at - forward * cell * 0.25f;
            var start = heading - 1.1f;
            var sweep = 2.2f;
            DrawArc(centre, radius, start + sweep * tail, start + sweep * head, 16, Bone with { A = 0.95f * a }, heavy ? 5 : 3);
        }

        /// <summary>A missile at <paramref name="at"/>, heading along <paramref name="heading"/>.</summary>
        private void Missile(Vector2 at, Vector2 heading, Board3D.Missile kind, float cell, float u)
        {
            var colour = Bone;
            switch (kind)
            {
                case Board3D.Missile.Stone:
                    DrawCircle(at, Mathf.Max(2.5f, cell * 0.07f), colour);
                    break;
                case Board3D.Missile.Thrown:
                    var spun = heading.Rotated(u * 14);
                    DrawLine(at - spun * cell * 0.18f, at + spun * cell * 0.18f, colour, 3);
                    break;
                default:
                    var length = cell * (kind == Board3D.Missile.Bolt ? 0.28f : 0.42f);
                    var tip = at + heading * length / 2;
                    var nock = at - heading * length / 2;
                    var across = heading.Orthogonal() * cell * 0.06f;
                    DrawLine(nock, tip, colour, kind == Board3D.Missile.Bolt ? 3 : 2);
                    DrawColoredPolygon([tip + heading * cell * 0.08f, tip + across, tip - across], colour);
                    if (kind == Board3D.Missile.Arrow)
                    {
                        // Fletching.
                        DrawLine(nock, nock - heading * cell * 0.06f + across, colour, 2);
                        DrawLine(nock, nock - heading * cell * 0.06f - across, colour, 2);
                    }

                    break;
            }
        }

        /// <summary>Draws the flat board's trails, then what's in play over the tokens.</summary>
        private void DrawFlatCrumbs()
        {
            foreach (var (square, across, colour, forced) in crumbs)
            {
                if (FogAt(square) != FogState.Seen)
                {
                    continue;
                }

                var centre = SquareRect(square).GetCenter() + Vector2.One * (CellSize * (across - 1) / 2f);
                if (forced)
                {
                    DrawLine(centre - new Vector2(CellSize * 0.1f, 0), centre + new Vector2(CellSize * 0.1f, 0), colour with { A = 0.6f }, 3);
                }
                else
                {
                    DrawCircle(centre, Mathf.Max(2, CellSize * 0.06f), colour with { A = 0.6f });
                }
            }
        }

        private void DrawFlatActs()
        {
            if (flatPlaying is { Draw: { } draw } act)
            {
                draw(Math.Min(act.Time, act.Duration));
            }

            foreach (var effect in flatEffects)
            {
                effect.Draw((float)Math.Clamp(effect.Age / effect.Life, 0, 1));
            }
        }

        // ---- Timing ----

        private static (Vector2 Forward, Vector2 Side) Across(Vector2 from, Vector2 to)
        {
            var forward = (to - from).LengthSquared() > 1e-3f ? (to - from).Normalized() : Vector2.Right;
            return (forward, forward.Orthogonal());
        }

        /// <summary>0 before <paramref name="a"/>, rising to 1 halfway to <paramref name="b"/>, and back to 0 at it.</summary>
        private static float Bump(double t, double a, double b) =>
            t <= a || t >= b ? 0 : Mathf.Sin((float)((t - a) / (b - a)) * Mathf.Pi);

        private sealed class FlatAct(Happening happening, double duration)
        {
            public double Duration { get; } = duration;

            public double Time { get; set; }

            /// <summary>The happenings this act shows — more than one when several creatures move together.</summary>
            public IReadOnlyList<Happening> Covers { get; init; } = [happening];

            /// <summary>Moves the tokens and starts effects; called each frame with the act's time.</summary>
            public Action<double>? Step { get; init; }

            /// <summary>Paints what belongs to the act itself — a missile, an orb, a slash; called from <c>_Draw</c>.</summary>
            public Action<double>? Draw { get; init; }
        }

        /// <summary>Something left on the board for a moment: drawn with how far through its life it is, 0 to 1.</summary>
        private sealed class FlatEffect(double life, Action<float> draw)
        {
            public double Life { get; } = life;

            public double Age { get; set; }

            public void Draw(float k) => draw(k);
        }
    }
}
