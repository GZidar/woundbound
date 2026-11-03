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
        private readonly Dictionary<GridPosition, (MeshInstance3D Box, Color Colour)> blocks = new();
        private Node3D? fogLayer;
        private FogOfWar? fog;

        private float yawTarget = 45;
        private float pitchTarget = IsoPitch;
        private float zoomTarget = 18;
        private Vector3 focusTarget;

        /// <summary>Whether the camera is looking straight down — the tactical view.</summary>
        public bool TopDown => pitchTarget <= TopPitch + 1;

        public override void _Ready()
        {
            var environment = new WorldEnvironment
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
            AddChild(environment);
            var sun = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = true, RotationDegrees = new Vector3(-55, 30, 0) };
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
            blocks.Clear();
            fog = null;
            fogLayer = null;
            BuildGround(fight);

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
            Sync(1);
        }

        // ---- The camera ----

        /// <summary>Zooms in (a positive <paramref name="steps"/>) or out.</summary>
        public void Zoom(float steps) => zoomTarget = Mathf.Clamp(zoomTarget * Mathf.Pow(0.88f, steps), 5, 60);

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
        public Vector2 ScreenOf(Vector2 boardPoint, float height = 0) => camera.UnprojectPosition(new Vector3(boardPoint.X, height, boardPoint.Y));

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

            var t = -from.Y / along.Y;
            var hit = from + along * t;
            return new GridPosition(Mathf.FloorToInt(hit.X), Mathf.FloorToInt(hit.Z));
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
                    figure.Position = Where(creature);
                }

                // A foe out of sight isn't drawn; one coming into sight appears where it is, not
                // gliding there from where it was last seen.
                var shown = fog?.Shows(creature) ?? true;
                if (shown && !figure.Visible)
                {
                    figure.Position = Where(creature);
                }

                figure.Follow(Where(creature), FacingOf(creature, fight), ease);
                figure.Visible = shown && !creature.HasFled;
            }
        }

        /// <summary>Where a creature stands: the middle of its footprint, a rider up on its horse.</summary>
        private static Vector3 Where(Combatant c) =>
            new(c.Position.X + c.Across / 2f, c.IsRiding ? 0.55f : 0, c.Position.Y + c.Across / 2f);

        /// <summary>Which way it faces: towards the nearest creature of the other side still in the fight.</summary>
        private static float? FacingOf(Combatant c, Encounter fight)
        {
            var nearest = fight.Combatants.Where(o => o.Side != c.Side && !o.IsOutOfFight && !o.HasFled)
                .OrderBy(o => o.DistanceTo(c)).FirstOrDefault();
            if (nearest is null)
            {
                return null;
            }

            var d = new Vector2(nearest.Position.X - c.Position.X, nearest.Position.Y - c.Position.Y);
            return d.LengthSquared() < 1e-4f ? null : Mathf.Atan2(d.X, d.Y);
        }

        // ---- The ground ----

        private static readonly Color LightSquare = new(0.33f, 0.36f, 0.30f);
        private static readonly Color DarkSquare = new(0.30f, 0.33f, 0.27f);
        private static readonly Color Rubble = new(0.42f, 0.34f, 0.24f);
        private static readonly Color Flagstone = new(0.45f, 0.45f, 0.48f);
        private static readonly Color Shallow = new(0.24f, 0.42f, 0.58f);
        private static readonly Color Deep = new(0.10f, 0.22f, 0.42f);
        private static readonly Color Stone = new(0.48f, 0.46f, 0.44f);
        private static readonly Color Wood = new(0.50f, 0.36f, 0.22f);

        private void BuildGround(Encounter fight)
        {
            var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            for (var y = 0; y < fight.Height; y++)
            {
                for (var x = 0; x < fight.Width; x++)
                {
                    var square = new GridPosition(x, y);
                    var colour = (x + y) % 2 == 0 ? LightSquare : DarkSquare;
                    var top = 0f;
                    if (fight.Setting.Water.TryGetValue(square, out var depth))
                    {
                        colour = depth == WaterDepth.Deep ? Deep : Shallow;
                        top = depth == WaterDepth.Deep ? -0.12f : -0.05f;
                    }
                    else
                    {
                        colour = fight.GroundAt(square) switch
                        {
                            Terrain.Broken => colour.Lerp(Rubble, 0.65f),
                            Terrain.Sure => colour.Lerp(Flagstone, 0.6f),
                            _ => colour,
                        };
                    }

                    if (fight.Setting.HazardAt(square) is not null)
                    {
                        colour = colour.Lerp(new Color(0.9f, 0.7f, 0.2f), 0.25f);
                    }

                    Quad(surface, x, y, top, colour);
                }
            }

            surface.GenerateNormals();
            var ground = new MeshInstance3D
            {
                Mesh = surface.Commit(),
                MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f },
            };
            terrain.AddChild(ground);

            // The grid, faintly, so squares can be counted from any angle.
            var lines = new ImmediateMesh();
            var ink = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0, 0, 0, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
            lines.SurfaceBegin(Mesh.PrimitiveType.Lines, ink);
            for (var x = 0; x <= fight.Width; x++)
            {
                lines.SurfaceAddVertex(new Vector3(x, 0.01f, 0));
                lines.SurfaceAddVertex(new Vector3(x, 0.01f, fight.Height));
            }

            for (var y = 0; y <= fight.Height; y++)
            {
                lines.SurfaceAddVertex(new Vector3(0, 0.01f, y));
                lines.SurfaceAddVertex(new Vector3(fight.Width, 0.01f, y));
            }

            lines.SurfaceEnd();
            terrain.AddChild(new MeshInstance3D { Mesh = lines });

            // Walls, a man and a half high; cover as tall as it is — a crate stack, a waist-high cart.
            foreach (var wall in fight.Setting.Walls)
            {
                Block(wall, 1.6f, Stone, 0.98f);
            }

            foreach (var (square, grade) in fight.Setting.CoverPieces)
            {
                Block(square, grade == Cover.Full ? 1.25f : 0.55f, Wood, grade == Cover.Full ? 0.8f : 0.9f);
            }
        }

        private static void Quad(SurfaceTool surface, int x, int y, float top, Color colour)
        {
            surface.SetColor(colour);
            Vector3 a = new(x, top, y), b = new(x + 1, top, y), c = new(x + 1, top, y + 1), d = new(x, top, y + 1);
            surface.AddVertex(a);
            surface.AddVertex(b);
            surface.AddVertex(c);
            surface.AddVertex(a);
            surface.AddVertex(c);
            surface.AddVertex(d);
        }

        private void Block(GridPosition square, float height, Color colour, float width)
        {
            var box = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(width, height, width) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f },
                Position = new Vector3(square.X + 0.5f, height / 2, square.Y + 0.5f),
            };
            terrain.AddChild(box);
            blocks[square] = (box, colour);
        }

        // ---- The fog of war ----

        private static readonly Color Void = new(0.09f, 0.09f, 0.11f);

        /// <summary>
        /// Draws <paramref name="sight"/> over the board: never-seen ground blacked out, walls and
        /// cover on it gone; remembered ground dimmed, its walls and cover darkened. Null lifts it.
        /// Called whenever what the party sees may have changed.
        /// </summary>
        public void ApplyFog(FogOfWar? sight)
        {
            fog = sight;
            fogLayer?.QueueFree();
            fogLayer = null;
            if (encounter is not { } fight)
            {
                return;
            }

            foreach (var (square, (box, colour)) in blocks)
            {
                var state = sight?.StateOf(square) ?? FogState.Seen;
                box.Visible = state != FogState.Unseen;
                ((StandardMaterial3D)box.MaterialOverride).AlbedoColor = state == FogState.Remembered ? colour.Darkened(0.55f) : colour;
            }

            if (sight is null)
            {
                return;
            }

            fogLayer = new Node3D();
            AddChild(fogLayer);
            var unseen = new SurfaceTool();
            var remembered = new SurfaceTool();
            unseen.Begin(Mesh.PrimitiveType.Triangles);
            remembered.Begin(Mesh.PrimitiveType.Triangles);
            int unseenCount = 0, rememberedCount = 0;
            for (var y = 0; y < fight.Height; y++)
            {
                for (var x = 0; x < fight.Width; x++)
                {
                    switch (sight.StateOf(new GridPosition(x, y)))
                    {
                        case FogState.Unseen:
                            Quad(unseen, x, y, 0.03f, Void);
                            unseenCount++;
                            break;
                        case FogState.Remembered:
                            Quad(remembered, x, y, 0.03f, new Color(0.02f, 0.02f, 0.04f, 0.55f));
                            rememberedCount++;
                            break;
                    }
                }
            }

            if (unseenCount > 0)
            {
                fogLayer.AddChild(new MeshInstance3D
                {
                    Mesh = unseen.Commit(),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true },
                });
            }

            if (rememberedCount > 0)
            {
                fogLayer.AddChild(new MeshInstance3D
                {
                    Mesh = remembered.Commit(),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        VertexColorUseAsAlbedo = true,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    },
                });
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

            public Figure()
            {
                creature = null!;
            }

            public Figure(Combatant creature, Color colour)
            {
                this.creature = creature;
                AddChild(body);
                var scale = creature.Across * 1.35f;
                body.Scale = Vector3.One * (creature.IsTiny ? 0.7f : scale);
                var cloth = Material(colour);
                var skin = Material(new Color(0.91f, 0.86f, 0.78f));
                var metal = Material(new Color(0.62f, 0.64f, 0.68f), metallic: 0.6f);
                var wood = Material(new Color(0.48f, 0.33f, 0.2f));

                if (creature.IsSteed)
                {
                    Part(new BoxMesh { Size = new Vector3(0.35f, 0.38f, 0.85f) }, new Vector3(0, 0.45f, 0), Material(new Color(0.45f, 0.3f, 0.18f)));
                    Part(new BoxMesh { Size = new Vector3(0.2f, 0.32f, 0.28f) }, new Vector3(0, 0.72f, 0.42f), Material(new Color(0.4f, 0.27f, 0.16f)));
                    return;
                }

                if (creature.IsSwarm)
                {
                    var random = new Random(creature.Name.GetHashCode());
                    for (var i = 0; i < 9; i++)
                    {
                        Part(new SphereMesh { Radius = 0.07f, Height = 0.12f }, new Vector3((float)random.NextDouble() - 0.5f, 0.06f, (float)random.NextDouble() - 0.5f) * 0.8f, cloth);
                    }

                    return;
                }

                var bulk = creature.Armour switch { ArmourWeight.Heavy => 0.34f, ArmourWeight.Medium => 0.3f, ArmourWeight.Light => 0.27f, _ => 0.24f };
                Part(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = 0.45f }, new Vector3(0, 0.23f, 0), cloth);           // legs
                Part(new CapsuleMesh { Radius = bulk / 2, Height = 0.5f }, new Vector3(0, 0.66f, 0), cloth);                                       // torso
                if (creature.Armour is ArmourWeight.Medium or ArmourWeight.Heavy)
                {
                    Part(new CapsuleMesh { Radius = bulk / 2 + 0.02f, Height = 0.36f }, new Vector3(0, 0.7f, 0), metal);                         // mail or plate over it
                }

                Part(new SphereMesh { Radius = 0.12f, Height = 0.24f }, new Vector3(0, 1.03f, 0), skin);                                         // head
                if (creature.Armour == ArmourWeight.Heavy)
                {
                    Part(new SphereMesh { Radius = 0.13f, Height = 0.16f, IsHemisphere = true }, new Vector3(0, 1.06f, 0), metal);              // helm
                }

                var hand = new Vector3(0.2f, 0.6f, 0.08f);
                if (creature.HasRangedAttack && creature.Attacks.All(a => a.IsRanged || a.Name == "Unarmed"))
                {
                    Part(new BoxMesh { Size = new Vector3(0.03f, 0.65f, 0.05f) }, hand + new Vector3(0, 0.1f, 0.06f), wood);                    // bow stave
                }
                else if (creature.Spells.Count > 0 && creature.Attacks.All(a => a.Name == "Unarmed"))
                {
                    Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.9f }, hand + new Vector3(0, 0.05f, 0), wood);    // a caster's staff
                }
                else if (creature.Attacks.Any(a => a.Name != "Unarmed"))
                {
                    Part(new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.55f) }, hand + new Vector3(0, 0.02f, 0.28f), metal);                  // a blade, held forward
                }

                if (creature.InHandShield is not null)
                {
                    Part(new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.04f }, new Vector3(-0.2f, 0.66f, 0.1f), wood, new Vector3(90, 0, 0));
                }
            }

            private void Part(Mesh mesh, Vector3 at, Material material, Vector3? turn = null) =>
                body.AddChild(new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = material, RotationDegrees = turn ?? Vector3.Zero });

            private static StandardMaterial3D Material(Color colour, float metallic = 0) =>
                new() { AlbedoColor = colour, Roughness = metallic > 0 ? 0.35f : 0.8f, Metallic = metallic };

            /// <summary>Eases towards <paramref name="to"/>, turning to face <paramref name="toward"/>; lies flat while down.</summary>
            public void Follow(Vector3 to, float? toward, float ease)
            {
                Position = Position.DistanceTo(to) < 0.01f ? to : Position.Lerp(to, ease);
                if (toward is { } angle)
                {
                    facing = Mathf.LerpAngle(facing, angle, ease);
                }

                var down = creature.IsOutOfFight || creature.IsProne;
                var lie = down ? 85f : 0f;
                body.RotationDegrees = new Vector3(Mathf.Lerp(body.RotationDegrees.X, lie, ease), Mathf.RadToDeg(facing), 0);
            }
        }
    }
}
