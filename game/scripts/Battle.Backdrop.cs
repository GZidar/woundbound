using System;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;

namespace WoundBoundGame
{
    /// <summary>
    /// Loading a battlemap picture to fight on (the user, 2026-11-08): pick a JPG or PNG, and the grid
    /// drawn on it is found (<see cref="GridFinder"/>) so its squares land on the board's; the size and
    /// offset can then be nudged by hand. The picture is drawn under the flat board, centred on the
    /// map. Only the look changes: the scene's walls, cover and water are still the scene's own.
    /// </summary>
    public partial class Battle
    {
        /// <summary>A battlemap the player has loaded for this fight, over whatever the scene names.</summary>
        private Backdrop? loadedBackdrop;

        private FlatScenery? flatScenery;

        /// <summary>Draws the ground and props on the flat board; made on first use.</summary>
        private FlatScenery Scenery => flatScenery ??= new FlatScenery(this);

        private Backdrop? CurrentBackdrop => loadedBackdrop ?? encounter?.Setting.Scenery.Backdrop;

        /// <summary>The ground and everything standing on it, beneath the highlights, fog and grid the board draws after.</summary>
        private void DrawFlatScenery() =>
            Scenery.Draw(encounter.Setting, encounter.Width, encounter.Height, SquareRect, CellSize, CurrentBackdrop, encounter.Fires);

        private VBoxContainer backdropRow = null!;
        private Label backdropLabel = null!;
        private SpinBox backdropSize = null!;
        private SpinBox backdropX = null!;
        private SpinBox backdropY = null!;
        private bool fillingBackdrop;

        private void BuildBackdropRow(Control column)
        {
            backdropRow = new VBoxContainer();
            column.AddChild(backdropRow);
            var buttons = new HBoxContainer();
            buttons.AddThemeConstantOverride("separation", 8);
            backdropRow.AddChild(buttons);
            var load = new Button { Text = "Battlemap…", TooltipText = "Draw the flat board on a battlemap picture (JPG or PNG). Its grid is found for you." };
            var clear = new Button { Text = "Clear", TooltipText = "Back to the board's own drawing." };
            buttons.AddChild(load);
            buttons.AddChild(clear);
            backdropLabel = new Label { Text = "No battlemap", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            backdropRow.AddChild(backdropLabel);

            var tune = new HFlowContainer();
            tune.AddThemeConstantOverride("h_separation", 6);
            backdropRow.AddChild(tune);
            backdropSize = Spin("Square px", 4, 400, 0.05, "How many of the picture's pixels make one square.");
            foreach (var (text, by) in new[] { ("×2", 2.0), ("÷2", 0.5) })
            {
                var scale = new Button { Text = text, TooltipText = "The grid finder can land on twice or half the true square size: put it right in one click." };
                scale.Pressed += () => backdropSize.Value *= by;
                tune.AddChild(scale);
            }

            backdropX = Spin("Left px", -4000, 8000, 0.5, "Where square (0, 0)'s left edge is in the picture.");
            backdropY = Spin("Top px", -4000, 8000, 0.5, "Where square (0, 0)'s top edge is in the picture.");

            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = ["*.jpg, *.jpeg, *.png, *.webp ; Battlemap pictures"],
                UseNativeDialog = true,
                Title = "Choose a battlemap",
            };
            AddChild(dialog);
            dialog.FileSelected += LoadBackdrop;
            load.Pressed += () => dialog.PopupCentered(new Vector2I(900, 600));
            clear.Pressed += () =>
            {
                loadedBackdrop = null;
                ShowBackdrop();
                QueueRedraw();
            };
            ShowBackdrop();

            SpinBox Spin(string caption, double min, double max, double step, string tip)
            {
                tune.AddChild(new Label { Text = caption });
                var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, TooltipText = tip, CustomMinimumSize = new Vector2(90, 0) };
                spin.ValueChanged += _ => BackdropTuned();
                tune.AddChild(spin);
                return spin;
            }
        }

        /// <summary>Loads <paramref name="path"/>, finds its grid, and lays the map's squares over the middle of it.</summary>
        private void LoadBackdrop(string path)
        {
            if (Battlemaps.Load(path) is not { } read)
            {
                backdropLabel.Text = $"Couldn't read {System.IO.Path.GetFileName(path)}.";
                return;
            }

            Scenery.Prime(path, read.Texture);
            loadedBackdrop = read.Fit(path, encounter.Width, encounter.Height);
            ShowBackdrop(read.Note);
            QueueRedraw();
        }

        private void ShowBackdrop(string? note = null)
        {
            var backdrop = CurrentBackdrop;
            backdropLabel.Text = backdrop is null
                ? "No battlemap"
                : System.IO.Path.GetFileName(backdrop.Path) + (note is null ? string.Empty : $" — {note}") + (In3D ? "\nShown on the flat board (Options: Flat board)." : string.Empty);
            fillingBackdrop = true;
            backdropSize.Value = backdrop?.PixelsPerSquare ?? 0;
            backdropX.Value = backdrop?.OriginX ?? 0;
            backdropY.Value = backdrop?.OriginY ?? 0;
            backdropSize.Editable = backdropX.Editable = backdropY.Editable = backdrop is not null;
            fillingBackdrop = false;
        }

        private void BackdropTuned()
        {
            if (fillingBackdrop || CurrentBackdrop is not { } backdrop)
            {
                return;
            }

            loadedBackdrop = backdrop with { PixelsPerSquare = (float)backdropSize.Value, OriginX = (float)backdropX.Value, OriginY = (float)backdropY.Value };
            QueueRedraw();
        }
    }
}
