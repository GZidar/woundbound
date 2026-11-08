using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The board in three dimensions (the user's direction, 2026-11-03): the ground, its terrain,
    /// water, walls and cover as shapes of their own height, a simple figure for every creature with
    /// the kit it carries drawn on, and an orthographic camera at the isometric angle that zooms,
    /// pans, turns in eighths and tips over to look straight down for the tactical view.
    ///
    /// <para>One world unit is one square: square (x, y) is the unit square from (x, 0, y) to
    /// (x + 1, 0, y + 1). Only presentation lives here — the rules stay in the encounter — and the
    /// battle screen keeps drawing its overlays on top, through <see cref="ScreenOf"/> and
    /// <see cref="SquareAtScreen"/>.</para>
    /// </summary>
    public partial class Board3D : Node3D
    {
        private const float IsoPitch = -35.264f;
        private const float TopPitch = -89.9f;

        private Encounter? encounter;
        private Func<Combatant, Color> colourOf = _ => Colors.White;
        private Camera3D camera = null!;
        private Node3D yaw = null!;
        private Node3D pitch = null!;
        private Node3D terrain = null!;
        private readonly Dictionary<Combatant, Figure> figures = new();
        private FogOfWar? fog;

        private float yawTarget = 45;
        private float pitchTarget = IsoPitch;
        private float zoomTarget = 18;
        private Vector3 focusTarget;

        /// <summary>Whether the camera is looking straight down — the tactical view.</summary>
        public bool TopDown => pitchTarget <= TopPitch + 1;

        public override void _Ready()
        {
            world = new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color(0.09f, 0.09f, 0.11f),
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(0.55f, 0.55f, 0.6f),
                    AmbientLightEnergy = 0.9f,
                },
            };
            AddChild(world);
            sun = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = true, RotationDegrees = new Vector3(-55, 30, 0), DirectionalShadowMaxDistance = 60 };
            AddChild(sun);

            yaw = new Node3D();
            AddChild(yaw);
            pitch = new Node3D();
            yaw.AddChild(pitch);
            camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = zoomTarget, Near = 0.05f, Far = 200, Position = new Vector3(0, 0, 40) };
            pitch.AddChild(camera);
            camera.MakeCurrent();
        }

        /// <summary>Lays out <paramref name="fight"/>'s ground and creatures, and points the camera at the middle of it.</summary>
        public void Build(Encounter fight, Func<Combatant, Color> colours)
        {
            encounter = fight;
            colourOf = colours;
            terrain?.QueueFree();
            terrain = new Node3D();
            AddChild(terrain);
            foreach (var figure in figures.Values)
            {
                figure.QueueFree();
            }

            figures.Clear();
            fog = null;
            propLayer = null;
            BuildScenery(fight);

            // Open over the party, close enough to see who is who; the rest is a pan or a zoom away.
            var party = fight.Combatants.Where(c => c.Side == Side.Party).ToList();
            focusTarget = party.Count == 0
                ? new Vector3(fight.Width / 2f, 0, fight.Height / 2f)
                : new Vector3(party.Average(c => c.Position.X + 0.5f), 0, party.Average(c => c.Position.Y + 0.5f));
            var foes = fight.Combatants.Where(c => c.Side == Side.Adversaries && c.OccupiesSquare).ToList();
            var spread = foes.Concat(party).Select(c => Math.Max(Math.Abs(c.Position.X + 0.5f - focusTarget.X), Math.Abs(c.Position.Y + 0.5f - focusTarget.Z))).DefaultIfEmpty(6).Max();
            zoomTarget = Mathf.Clamp(spread * 1.6f + 4, 8, 22);
            yaw.Position = focusTarget;
            camera.Size = zoomTarget;
            ClearActs();
            Sync(1);
        }

        /// <summary>Throws away <paramref name="creature"/>'s figure so the next frame builds it afresh — after its look has changed.</summary>
        public void Rebuild(Combatant creature)
        {
            if (figures.Remove(creature, out var figure))
            {
                figure.QueueFree();
            }
        }

        // ---- The camera ----

        /// <summary>Zooms in (a positive <paramref name="steps"/>) or out.</summary>
        public void Zoom(float steps) => zoomTarget = Mathf.Clamp(zoomTarget * Mathf.Pow(0.88f, steps), 5, 60);

        /// <summary>Brings the camera round over <paramref name="boardPoint"/> (in squares), close enough to pick it out.</summary>
        public void FocusOn(Vector2 boardPoint)
        {
            focusTarget = new Vector3(boardPoint.X, 0, boardPoint.Y);
            zoomTarget = Math.Min(zoomTarget, 14);
        }

        /// <summary>Turns the board an eighth at a time.</summary>
        public void Turn(int eighths) => yawTarget += 45 * eighths;

        /// <summary>Switches between the isometric view and looking straight down.</summary>
        public void ToggleTopDown()
        {
            if (TopDown)
            {
                pitchTarget = IsoPitch;
                yawTarget = yawBeforeTop;
                return;
            }

            // Straight down, and square to the grid — north up — for counting squares.
            yawBeforeTop = yawTarget;
            yawTarget = Mathf.Round(yawTarget / 360) * 360;
            pitchTarget = TopPitch;
        }

        private float yawBeforeTop = 45;

        /// <summary>Back to the isometric view over the middle of the board.</summary>
        public void Reset()
        {
            if (encounter is { } fight)
            {
                focusTarget = new Vector3(fight.Width / 2f, 0, fight.Height / 2f);
                zoomTarget = Math.Max(fight.Width, fight.Height) * 0.75f;
            }

            yawTarget = 45 + Mathf.Round((yawTarget - 45) / 360) * 360;
            pitchTarget = IsoPitch;
        }

        /// <summary>Pans by <paramref name="screen"/> pixels of the view, kept over the board.</summary>
        public void Pan(Vector2 screen, Vector2 viewSize)
        {
            if (encounter is not { } fight)
            {
                return;
            }

            var perPixel = camera.Size / Math.Max(1, viewSize.Y);
            var right = camera.GlobalTransform.Basis.X with { Y = 0 };
            var up = -camera.GlobalTransform.Basis.Z with { Y = 0 };
            if (up.LengthSquared() < 1e-4f)
            {
                up = camera.GlobalTransform.Basis.Y with { Y = 0 };
            }

            var move = (-right.Normalized() * screen.X + up.Normalized() * screen.Y) * perPixel;

            // Kept over ground the party has seen: nothing to look at in the fog but fog.
            var (x0, y0, x1, y1) = fog?.ExploredBounds is { } known
                ? (known.X0 - 1, known.Y0 - 1, known.X1 + 2, known.Y1 + 2)
                : (0, 0, fight.Width, fight.Height);
            focusTarget = new Vector3(Mathf.Clamp(focusTarget.X + move.X, Math.Max(0, x0), Math.Min(fight.Width, x1)), 0,
                Mathf.Clamp(focusTarget.Z + move.Z, Math.Max(0, y0), Math.Min(fight.Height, y1)));
        }

        // ---- Between the board and the screen ----

        /// <summary>Where a point on the board (in squares) is on the view, in its pixels.</summary>
        /// <param name="on">The square whose ground the point lies on, for a point on its edge; by default the square it falls in.</param>
        public Vector2 ScreenOf(Vector2 boardPoint, float height = 0, GridPosition? on = null) =>
            camera.UnprojectPosition(new Vector3(boardPoint.X, height + SurfaceHeight(on ?? new GridPosition(Mathf.FloorToInt(boardPoint.X), Mathf.FloorToInt(boardPoint.Y))), boardPoint.Y));

        /// <summary>Where a creature's figure stands on the view — eased, as it walks — or null where it has none.</summary>
        public Vector2? FigureScreenOf(Combatant c, float height = 0) =>
            figures.TryGetValue(c, out var figure) ? camera.UnprojectPosition(figure.Position with { Y = figure.Position.Y + height }) : null;

        /// <summary>The square under a point on the view — where the ray through it meets the ground.</summary>
        public GridPosition SquareAtScreen(Vector2 viewPoint)
        {
            var from = camera.ProjectRayOrigin(viewPoint);
            var along = camera.ProjectRayNormal(viewPoint);
            if (Math.Abs(along.Y) < 1e-5f)
            {
                return new GridPosition(-1000, -1000);
            }

            // Where it meets flat ground first, then again at the height of the ground found there:
            // high ground stands up into the ray, water sinks below it.
            GridPosition At(float level)
            {
                var hit = from + along * ((level - from.Y) / along.Y);
                return new GridPosition(Mathf.FloorToInt(hit.X), Mathf.FloorToInt(hit.Z));
            }

            var square = At(0);
            var level = SurfaceHeight(square);
            if (level != 0)
            {
                square = At(level);
            }

            // Looking over the edge of high ground: the ray may meet its top before it reaches the ground behind.
            var near = At(RaisedHeight);
            return SurfaceHeight(near) == RaisedHeight ? near : square;
        }

        /// <summary>Eases the camera and every figure towards where they belong.</summary>
        public void Sync(double delta)
        {
            if (encounter is not { } fight)
            {
                return;
            }

            var ease = 1 - Mathf.Exp(-8 * (float)delta);
            yaw.RotationDegrees = new Vector3(0, Mathf.Lerp(yaw.RotationDegrees.Y, yawTarget, ease), 0);
            pitch.RotationDegrees = new Vector3(Mathf.Lerp(pitch.RotationDegrees.X, pitchTarget, ease), 0, 0);
            yaw.Position = yaw.Position.Lerp(focusTarget, ease);
            camera.Size = Mathf.Lerp(camera.Size, zoomTarget, ease);
            AnimateScenery(fight, delta);
            Animate(delta);

            var present = fight.Combatants.Concat(fight.Steeds).Where(c => !c.HasFled && c.OccupiesSquare || c.IsOutOfFight && !c.HasFled).Distinct().ToList();
            foreach (var gone in figures.Keys.Except(present).ToList())
            {
                figures[gone].QueueFree();
                figures.Remove(gone);
            }

            foreach (var creature in present)
            {
                if (!figures.TryGetValue(creature, out var figure))
                {
                    figure = new Figure(creature, colourOf(creature));
                    AddChild(figure);
                    figures[creature] = figure;
                    figure.Place(Where(creature));
                }

                // A foe out of sight isn't drawn; one coming into sight appears where it is, not
                // gliding there from where it was last seen.
                var shown = fog?.Shows(creature) ?? true;
                if (shown && !figure.Visible)
                {
                    figure.Place(Where(creature));
                }

                figure.Follow(Where(creature), FacingOf(creature, fight), ease);
                figure.Visible = shown && !creature.HasFled;
            }
        }

        /// <summary>Where a creature stands: the middle of its footprint, a rider up on its horse.</summary>
        private Vector3 Where(Combatant c) => Centre(c, c.Position);

        /// <summary>Which way it faces: towards the nearest creature of the other side still in the fight.</summary>
        private static float? FacingOf(Combatant c, Encounter fight)
        {
            // A hold has the two of them face each other.
            if ((c.Holding ?? c.HeldBy) is { } grappled)
            {
                var g = new Vector2(grappled.Position.X - c.Position.X, grappled.Position.Y - c.Position.Y);
                if (g.LengthSquared() > 1e-4f)
                {
                    return Mathf.Atan2(g.X, g.Y);
                }
            }

            var nearest = fight.Combatants.Where(o => o.Side != c.Side && !o.IsOutOfFight && !o.HasFled)
                .OrderBy(o => o.DistanceTo(c)).FirstOrDefault();
            if (nearest is null)
            {
                return null;
            }

            var d = new Vector2(nearest.Position.X - c.Position.X, nearest.Position.Y - c.Position.Y);
            return d.LengthSquared() < 1e-4f ? null : Mathf.Atan2(d.X, d.Y);
        }

        private static readonly Color Void = new(0.09f, 0.09f, 0.11f);

        /// <summary>
        /// Draws <paramref name="sight"/> over the board: never-seen ground blacked out, and the props
        /// and scatter on it gone; remembered ground dimmed, its props darkened. Null lifts it.
        /// Called whenever what the party sees may have changed.
        /// </summary>
        public void ApplyFog(FogOfWar? sight)
        {
            fog = sight;
            if (encounter is { } fight)
            {
                ApplyFogTexture(fight, sight);
            }
        }

        /// <summary>
        /// A creature, simply: a body in its colour, a head, and what it carries — a blade or a bow in
        /// hand, a shield on the arm, armour by the bulk of the torso. A Swarm is a scatter of small
        /// bodies, a horse a long low one. Down, it lies flat.
        /// </summary>
        private sealed partial class Figure : Node3D
        {
            private readonly Combatant creature;
            private readonly Node3D body = new();
            private float facing;

            /// <summary>The body's lean and roll as last laid on, in degrees.</summary>
            private Vector3 tilt;

            public Figure()
            {
                creature = null!;
            }

            public Figure(Combatant creature, Color colour)
            {
                this.creature = creature;
                Colour = colour;
                AddChild(body);
                // Small creatures stand shorter than a person on the same square.
                var small = creature.Statblock?.Size == AdversarySize.Small ? 0.8f : 1f;
                // A bat's wingspan is most of it: drawn at a person's scale, it still fits its square.
                var tiny = WoundBound.Game.Figures.BodyShape.Of(creature).Plan == WoundBound.Game.Figures.BodyPlan.Winged ? 1.1f : 0.7f;
                var size = creature.IsTiny ? tiny : creature.Across * 1.35f * small;
                // A tall character stands taller and a short one shorter, broadening a little with it.
                var stature = creature.Appearance?.HeightScale ?? 1;
                body.Scale = new Vector3(size * (1 + (stature - 1) * 0.35f), size * stature, size * (1 + (stature - 1) * 0.35f));
                Build(creature, colour);
            }

            private void Part(Mesh mesh, Vector3 at, Material material, Vector3? turn = null, Node3D? parent = null) =>
                (parent ?? body).AddChild(new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = material, RotationDegrees = turn ?? Vector3.Zero });

            private static StandardMaterial3D Material(Color colour, float metallic = 0) =>
                new() { AlbedoColor = colour, Roughness = metallic > 0 ? 0.35f : 0.8f, Metallic = metallic };

            /// <summary>
            /// Eases towards <paramref name="to"/>, turning to face <paramref name="toward"/> — or
            /// whoever an action has it facing — with the action's pose laid over it; lies flat
            /// while down, though not before the blow that put it down has been seen to land.
            /// </summary>
            public void Follow(Vector3 to, float? toward, float ease)
            {
                // While an act still to be shown involves it, the acts move it - along the route it
                // walked, not straight to where the rules have already put it.
                if (Holds == 0)
                {
                    var was = Anchor;
                    Anchor = Anchor.DistanceTo(to) < 0.01f ? to : Anchor.Lerp(to, ease);
                    Stride(was.DistanceTo(Anchor));
                }

                walking = Mathf.MoveToward(walking, striding ? 1 : 0, ease * 3);
                striding = false;
                Position = Anchor + Offset;
                if ((Face ?? toward) is { } angle)
                {
                    facing = Mathf.LerpAngle(facing, angle, Face is null ? ease : Mathf.Min(1, ease * 2.5f));
                }

                if (Holds == 0)
                {
                    shownDown = creature.IsOutOfFight || creature.IsProne;
                }

                // Standing, it leans as the act has it, over its own stoop; down, it lies as its body does.
                var standing = new Vector3(Lean + stoop, 0, 0);
                var pose = shownDown ? DownTilt : standing;
                var blend = Holds > 0 && !shownDown ? 1 : ease;
                // Kept here rather than read back off the node: Godot may hand the same turn back as
                // other angles, and blending from those swings the body through nonsense.
                tilt = tilt.Lerp(pose, blend);
                body.RotationDegrees = new Vector3(tilt.X, Mathf.RadToDeg(facing), tilt.Z);
                var step = Mathf.Sin(stridePhase) * walking;
                PoseLimbs(step, -Mathf.Log(Mathf.Max(1e-4f, 1 - ease)) / 8, shownDown);
                body.Position = Vector3.Up * (0.035f * Mathf.Abs(Mathf.Cos(stridePhase)) * walking + (HoverNow(shownDown) + Seat) * BodyScale);
            }
        }
    }
}
