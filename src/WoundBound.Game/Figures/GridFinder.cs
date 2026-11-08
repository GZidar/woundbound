namespace WoundBound.Game.Figures
{
    /// <summary>
    /// Finds the grid drawn on a battlemap picture (the user's battlemap backdrops, 2026-11-08): how
    /// many pixels a square is, and where the first grid line falls, along each axis. Most published
    /// battlemaps draw their grid as thin lines a little darker than the art around them, evenly
    /// spaced, so the finder measures how much each column (and row) is darker than its neighbours a
    /// few pixels away, and looks for the spacing and phase at which those dark lines repeat best.
    /// It is a first guess: on busy art it can land on twice or half the true size, which is why the
    /// screen offers ×2 and ÷2 beside it.
    /// </summary>
    public static class GridFinder
    {
        /// <summary>The grid found along one axis: the spacing in pixels, where the first line is, and how clearly it stood out (0 = not at all).</summary>
        public sealed record Axis(double Pitch, double Offset, double Strength);

        /// <summary>The grid on a picture: square size and the top-left corner of a square, in pixels, and how sure the finder is.</summary>
        public sealed record Grid(double PixelsPerSquare, double OffsetX, double OffsetY, double Strength)
        {
            /// <summary>How many whole squares the picture holds across and down.</summary>
            public (int Across, int Down) Squares(int width, int height) =>
                ((int)Math.Floor((width - OffsetX) / PixelsPerSquare + 0.01), (int)Math.Floor((height - OffsetY) / PixelsPerSquare + 0.01));
        }

        /// <summary>
        /// Finds the grid on a picture given as brightness, row by row, 0 to 1. Square grids are
        /// assumed: one spacing is chosen that suits both axes, each with its own offset. Null if no
        /// grid stands out.
        /// </summary>
        public static Grid? Find(float[] brightness, int width, int height, double minPitch = 12, double maxPitch = 200)
        {
            var across = Profile(brightness, width, height, alongRows: true);
            var down = Profile(brightness, width, height, alongRows: false);

            // How strongly each spacing repeats, along both axes at once. Evenly spaced thin lines
            // ring at their spacing and at its halves and thirds, but not at twice it, so the grid is
            // the widest spacing that rings nearly as strongly as the strongest.
            var top = Math.Min(maxPitch, Math.Min(width, height) / 4.0);
            var rings = new List<(double Pitch, double Strength)>();
            for (var p = minPitch; p <= top; p += 0.05)
            {
                rings.Add((p, Math.Min(Ring(across, p), Ring(down, p))));
            }

            if (rings.Count == 0)
            {
                return null;
            }

            var loudest = rings.Max(r => r.Strength);
            var typical = rings.Select(r => r.Strength).Order().ElementAt(rings.Count / 2);
            if (loudest <= 0 || typical <= 0)
            {
                return null;
            }

            // The loudest spacing may be a half or a third of the grid's — thin lines ring at their
            // spacing's fractions too, and the line test favours short spacings. So step up: if, at
            // two, three or four times the spacing, one set of lines is much darker than the rest,
            // the grid is that wider one. Repeat until no wider grid stands out.
            var pitch = rings.MaxBy(r => r.Strength).Pitch;
            for (var stepped = true; stepped;)
            {
                stepped = false;
                for (var m = 2; m <= 4 && pitch * m <= top; m++)
                {
                    // Lines that are really every mth must ring at the wider spacing too; a true
                    // grid of this spacing barely does, however its art happens to fall.
                    var wide = Math.Min(Ring(across, pitch * m), Ring(down, pitch * m));
                    var here = Math.Min(Ring(across, pitch), Ring(down, pitch));
                    if (wide >= 0.35 * here && Wider(across, pitch, m) && Wider(down, pitch, m))
                    {
                        pitch *= m;
                        stepped = true;
                        break;
                    }
                }
            }

            var strength = Math.Min(Ring(across, pitch), Ring(down, pitch));
            var (_, offX) = BestPhase(across, pitch);
            var (_, offY) = BestPhase(down, pitch);
            return new Grid(pitch, offX, offY, strength / typical);
        }

        /// <summary>
        /// Whether lines <paramref name="pitch"/> apart are really every <paramref name="m"/>th line of a
        /// grid <paramref name="m"/> times as wide: one of the <paramref name="m"/> interleaved sets much darker than the others.
        /// </summary>
        private static bool Wider(double[] profile, double pitch, int m)
        {
            var (_, phase) = BestPhase(profile, pitch);
            var sets = Enumerable.Range(0, m).Select(j => CombMean(profile, pitch * m, (phase + j * pitch) % (pitch * m))).ToList();
            var darkest = sets.Max();
            var rest = (sets.Sum() - darkest) / (m - 1);
            return darkest > 0 && darkest > 2.5 * Math.Max(rest, 0);
        }

        /// <summary>The average darkness of lines <paramref name="pitch"/> apart starting at <paramref name="phase"/>.</summary>
        private static double CombMean(double[] profile, double pitch, double phase)
        {
            double sum = 0;
            var n = 0;
            for (var at = phase; at < profile.Length; at += pitch)
            {
                var i = (int)Math.Floor(at);
                sum += Math.Max(profile[i], i + 1 < profile.Length ? profile[i + 1] : profile[i]);
                n++;
            }

            return n == 0 ? 0 : sum / n;
        }

        /// <summary>How strongly the profile repeats every <paramref name="pitch"/> pixels: its Fourier magnitude at that period.</summary>
        private static double Ring(double[] profile, double pitch)
        {
            double re = 0, im = 0;
            var w = 2 * Math.PI / pitch;
            for (var i = 0; i < profile.Length; i++)
            {
                re += profile[i] * Math.Cos(w * i);
                im += profile[i] * Math.Sin(w * i);
            }

            return Math.Sqrt(re * re + im * im) / profile.Length;
        }

        /// <summary>For each column (or row), how much darker it is than the pixels a few steps either side, averaged down it, with the slow swell of the art taken out.</summary>
        private static double[] Profile(float[] brightness, int width, int height, bool alongRows)
        {
            var length = alongRows ? width : height;
            var other = alongRows ? height : width;
            var profile = new double[length];
            const int reach = 3;
            for (var i = reach; i < length - reach; i++)
            {
                double sum = 0;
                for (var j = 0; j < other; j++)
                {
                    float At(int k) => alongRows ? brightness[j * width + k] : brightness[k * width + j];
                    var dark = (At(i - reach) + At(i + reach)) / 2 - At(i);
                    if (dark > 0)
                    {
                        sum += dark;
                    }
                }

                profile[i] = sum / other;
            }

            // Take away the local average, so broad dark stretches of the art don't count as lines.
            var smoothed = new double[length];
            const int window = 15;
            for (var i = 0; i < length; i++)
            {
                double s = 0;
                var n = 0;
                for (var k = Math.Max(0, i - window); k <= Math.Min(length - 1, i + window); k++)
                {
                    s += profile[k];
                    n++;
                }

                smoothed[i] = profile[i] - s / n;
            }

            return smoothed;
        }

        /// <summary>The phase at which lines <paramref name="pitch"/> apart sit on the darkest columns, and how dark they are on average there.</summary>
        private static (double Score, double Offset) BestPhase(double[] profile, double pitch)
        {
            var best = (Score: double.MinValue, Offset: 0.0);
            for (var phase = 0.0; phase < pitch; phase += 0.5)
            {
                double sum = 0;
                var n = 0;
                for (var at = phase; at < profile.Length; at += pitch)
                {
                    // The line may fall between pixels: take the darker of the two either side.
                    var i = (int)Math.Floor(at);
                    var v = Math.Max(profile[i], i + 1 < profile.Length ? profile[i + 1] : profile[i]);
                    sum += v;
                    n++;
                }

                if (n >= 4 && sum / n > best.Score)
                {
                    best = (sum / n, phase);
                }
            }

            return best;
        }
    }
}
