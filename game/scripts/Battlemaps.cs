using System;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBoundGame
{
    /// <summary>Reading a battlemap picture and finding the grid drawn on it, for the battle screen and the scene editor alike.</summary>
    public static class Battlemaps
    {
        /// <summary>A picture read from disk, the grid found on it (if any), and a line saying what was found.</summary>
        public sealed record Read(Image Image, Texture2D Texture, GridFinder.Grid? Grid, string Note)
        {
            public int Width => Image.GetWidth();

            public int Height => Image.GetHeight();

            /// <summary>How many whole squares the picture holds across and down, by its grid or, with none found, by a guess.</summary>
            public (int Across, int Down) Squares(int fallbackAcross) =>
                Grid?.Squares(Width, Height) ?? (fallbackAcross, (int)(Height / (Width / (double)fallbackAcross)));

            /// <summary>
            /// The backdrop that lays a board <paramref name="across"/> by <paramref name="down"/> squares
            /// over the middle of the picture's own squares.
            /// </summary>
            public Backdrop Fit(string path, int across, int down)
            {
                var pitch = Grid?.PixelsPerSquare ?? Width / (double)across;
                double offX = Grid?.OffsetX ?? 0, offY = Grid?.OffsetY ?? 0;
                var (have, haveDown) = Squares(across);
                offX += pitch * Math.Max(0, (have - across) / 2);
                offY += pitch * Math.Max(0, (haveDown - down) / 2);
                return new Backdrop(path, (float)offX, (float)offY, (float)pitch);
            }
        }

        /// <summary>Reads <paramref name="path"/> and looks for its grid; null if it isn't a picture Godot can read.</summary>
        public static Read? Load(string path)
        {
            var image = Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
            {
                return null;
            }

            int w = image.GetWidth(), h = image.GetHeight();
            var grey = (Image)image.Duplicate();
            grey.Convert(Image.Format.L8);
            var bytes = grey.GetData();
            var brightness = new float[w * h];
            for (var i = 0; i < brightness.Length; i++)
            {
                brightness[i] = bytes[i] / 255f;
            }

            var grid = GridFinder.Find(brightness, w, h);
            var note = grid is null
                ? "no grid found — set the square size by hand"
                : $"grid found: {grid.PixelsPerSquare:0.##} px squares, {grid.Squares(w, h).Across}×{grid.Squares(w, h).Down} squares"
                  + (grid.Strength < 4 ? " (faint — check it lines up)" : string.Empty);
            return new Read(image, ImageTexture.CreateFromImage(image), grid, note);
        }
    }
}
