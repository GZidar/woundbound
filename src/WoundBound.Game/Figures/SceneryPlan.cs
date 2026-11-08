using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Figures
{
    /// <summary>
    /// Everything the board needs to dress a place: what stands on every solid square and what the
    /// ground is on every square, worked out from the scene's <see cref="Scenery"/> where it says and
    /// from its biome and what each square does where it doesn't.
    ///
    /// <para>The one promise: a prop always fits its square. Full cover and walls get tall props,
    /// Partial cover low ones; a named prop of the wrong height is replaced by the one the square
    /// would have had. So whatever looks like it would hide you does, and as well as it looks.</para>
    /// </summary>
    public sealed class SceneryPlan
    {
        private readonly Setting setting;
        private readonly Scenery scenery;
        private readonly Dictionary<GridPosition, Prop> props = new();

        /// <param name="before">
        /// The plan for the same place before a piece of cover broke, if one did: a tall piece worn
        /// down to Partial keeps its identity — a tree felled to a log, a crate stack knocked down
        /// to a crate, a boulder split to a rock — rather than turning into whatever the square
        /// would otherwise have been.
        /// </param>
        public SceneryPlan(Setting setting, SceneryPlan? before = null)
        {
            this.setting = setting;
            scenery = setting.Scenery;
            foreach (var wall in setting.Walls)
            {
                props[wall] = Fitting(wall, tall: true, wall: true);
            }

            foreach (var (square, grade) in setting.CoverPieces)
            {
                if (grade == Cover.None)
                {
                    continue;
                }

                props[square] = grade == Cover.Partial && before?.PropAt(square) is { } was && was.IsTall()
                    ? WornDown(was)
                    : Fitting(square, tall: grade == Cover.Full, wall: false);
            }
        }

        /// <summary>What a tall prop is once it has been hacked down to Partial cover.</summary>
        public static Prop WornDown(Prop tall) => tall switch
        {
            Prop.Tree or Prop.Pine or Prop.Birch => Prop.Log,
            Prop.CrateStack => Prop.Crate,
            Prop.Boulder or Prop.RockFace => Prop.Rock,
            Prop.StoneWall or Prop.Pillar => Prop.LowWall,
            Prop.HutWall => Prop.Fence,
            _ => tall.IsTall() ? Prop.Rock : tall,
        };

        public Biome Biome => scenery.Biome;

        /// <summary>What stands on every wall and cover square.</summary>
        public IReadOnlyDictionary<GridPosition, Prop> Props => props;

        /// <summary>What stands on <paramref name="square"/>, or null where nothing solid does.</summary>
        public Prop? PropAt(GridPosition square) => props.TryGetValue(square, out var prop) ? prop : null;

        /// <summary>
        /// What the ground is on <paramref name="square"/> — on or off the map, since the map is a
        /// window on ground that goes on. Under water it is the bed: mud, or sand in a river.
        /// </summary>
        public Surface SurfaceAt(GridPosition square)
        {
            if (scenery.Surfaces.TryGetValue(square, out var named))
            {
                return named;
            }

            if (setting.Water.ContainsKey(square))
            {
                return scenery.Biome == Biome.Fen ? Surface.Mud : Surface.Sand;
            }

            if (setting.Raised.Contains(square))
            {
                return scenery.Biome == Biome.Woodland ? Surface.Moss : Surface.Stone;
            }

            var grade = setting.GroundAt(square);
            if (grade == Terrain.Sure)
            {
                return scenery.Biome is Biome.Village ? Surface.Flagstone : Surface.Road;
            }

            if (grade == Terrain.Broken)
            {
                return scenery.Biome switch
                {
                    Biome.Woodland => Surface.Leaves,
                    Biome.Rocky => Surface.Gravel,
                    Biome.Fen => Surface.Mud,
                    _ => Surface.LongGrass,
                };
            }

            // Firm ground at the water's edge is its bank.
            if (setting.Water.Count > 0 && Around(square).Any(setting.Water.ContainsKey))
            {
                return scenery.Biome == Biome.Fen ? Surface.Mud : Surface.Sand;
            }

            return scenery.Biome switch
            {
                Biome.Woodland => Noise(square, 3) == 0 ? Surface.Leaves : Surface.Moss,
                Biome.Rocky => Noise(square, 4) == 0 ? Surface.Gravel : Surface.Grass,
                Biome.Fen => Noise(square, 3) == 0 ? Surface.Mud : Surface.LongGrass,
                Biome.Road => Surface.Grass,
                _ => Surface.Grass,
            };
        }

        /// <summary>Whether <paramref name="square"/> is broken or rough going, for scattering stones and brush.</summary>
        public bool IsRough(GridPosition square) => setting.GroundAt(square) == Terrain.Broken && !setting.Water.ContainsKey(square);

        /// <summary>Whether the scene's own prop for <paramref name="square"/>, if any, fits it.</summary>
        public static bool Fits(Prop prop, bool tall, bool wall) => prop.IsTall() == tall && (!wall || prop.IsWallLike() || prop.IsTall());

        private Prop Fitting(GridPosition square, bool tall, bool wall)
        {
            if (scenery.Props.TryGetValue(square, out var named) && Fits(named, tall, wall))
            {
                return named;
            }

            return wall ? WallFor(square) : tall ? TallFor(square) : LowFor(square);
        }

        private Prop WallFor(GridPosition square)
        {
            // A wall square on its own is a pillar; walls in a run are a wall.
            if (!Beside(square).Any(setting.Walls.Contains))
            {
                return scenery.Biome == Biome.Rocky ? Prop.RockFace : Prop.Pillar;
            }

            return scenery.Biome switch
            {
                Biome.Rocky => Prop.RockFace,
                Biome.Village => Prop.HutWall,
                _ => Prop.StoneWall,
            };
        }

        private Prop TallFor(GridPosition square)
        {
            if (setting.Unbreakable.Contains(square))
            {
                return Prop.Boulder;
            }

            return scenery.Biome switch
            {
                Biome.Woodland => Noise(square, 4) switch { 0 => Prop.Birch, 1 => Prop.Pine, _ => Prop.Tree },
                Biome.Meadow => Noise(square, 5) == 0 ? Prop.Pine : Prop.Tree,
                Biome.Fen => Noise(square, 3) == 0 ? Prop.Birch : Prop.Tree,
                Biome.Rocky => Noise(square, 3) == 0 ? Prop.Pine : Prop.Boulder,
                Biome.Road or Biome.Village => Prop.CrateStack,
                _ => Prop.Tree,
            };
        }

        private Prop LowFor(GridPosition square)
        {
            if (setting.Unbreakable.Contains(square))
            {
                return Prop.Rock;
            }

            // Partial cover in a line of three or more is a hedge, a fence or a low wall; two side by
            // side on a road are a cart.
            var run = RunLength(square);
            return scenery.Biome switch
            {
                Biome.Woodland => run >= 3 ? Prop.Log : Noise(square, 4) == 0 ? Prop.Stump : Prop.Bush,
                Biome.Meadow => run >= 3 ? Prop.Hedge : Prop.Bush,
                Biome.Fen => Noise(square, 2) == 0 ? Prop.Reeds : Prop.Bush,
                Biome.Rocky => Prop.Rock,
                Biome.Village => run >= 3 ? Prop.Fence : Noise(square, 2) == 0 ? Prop.Barrel : Prop.Crate,
                Biome.Road => run == 2 ? Prop.Cart : run >= 3 ? Prop.Crate : Noise(square, 2) == 0 ? Prop.Barrel : Prop.Crate,
                _ => Prop.Bush,
            };
        }

        /// <summary>The longest straight run of Partial cover, along a row or a column, that <paramref name="square"/> is part of.</summary>
        private int RunLength(GridPosition square)
        {
            bool Low(GridPosition s) => setting.CoverAt(s) == Cover.Partial;
            int Count(int dx, int dy)
            {
                var n = 0;
                for (var s = new GridPosition(square.X + dx, square.Y + dy); Low(s); s = new GridPosition(s.X + dx, s.Y + dy))
                {
                    n++;
                }

                return n;
            }

            return 1 + Math.Max(Count(1, 0) + Count(-1, 0), Count(0, 1) + Count(0, -1));
        }

        /// <summary>The four squares sharing a side with <paramref name="square"/>.</summary>
        private static IEnumerable<GridPosition> Beside(GridPosition square)
        {
            yield return new GridPosition(square.X + 1, square.Y);
            yield return new GridPosition(square.X - 1, square.Y);
            yield return new GridPosition(square.X, square.Y + 1);
            yield return new GridPosition(square.X, square.Y - 1);
        }

        /// <summary>The eight squares around <paramref name="square"/>.</summary>
        private static IEnumerable<GridPosition> Around(GridPosition square)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx != 0 || dy != 0)
                    {
                        yield return new GridPosition(square.X + dx, square.Y + dy);
                    }
                }
            }
        }

        /// <summary>A number from 0 to <paramref name="n"/> − 1 that is always the same for the same square: variety that doesn't change between runs.</summary>
        public static int Noise(GridPosition square, int n) => (int)(Hash(square) % (uint)n);

        /// <summary>A well-mixed hash of a square, stable across runs and machines.</summary>
        public static uint Hash(GridPosition square, uint salt = 0)
        {
            unchecked
            {
                var h = (uint)square.X * 73856093u ^ (uint)square.Y * 19349663u ^ salt * 83492791u;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
