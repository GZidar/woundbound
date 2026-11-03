using Godot;

namespace WoundBoundGame
{
    /// <summary>
    /// The flat board's camera: the wheel zooms about the mouse, middle-drag or WASD and the arrows
    /// pan, Home fits the whole field again. Zoomed out, the field fits the board area, as it always
    /// did; zoomed in, it is a window on it, kept from sliding off the field's edges.
    /// </summary>
    public partial class Battle
    {
        private const int LargestFlatCell = 96;

        /// <summary>How far in the flat board is zoomed: 1 fits the field.</summary>
        private float flatZoom = 1;

        /// <summary>Where the flat field's corner sits from the board area's, in pixels; zero or negative.</summary>
        private Vector2 flatPan;

        private static Rect2 BoardRect => new(BoardOrigin, BoardArea);

        private void ResetFlatCamera()
        {
            flatZoom = 1;
            flatPan = Vector2.Zero;
        }

        private bool HandleFlatCamera(InputEvent @event)
        {
            var mouse = GetLocalMousePosition();
            switch (@event)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when BoardRect.HasPoint(mouse):
                    ZoomFlat(1.15f, mouse);
                    return true;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when BoardRect.HasPoint(mouse):
                    ZoomFlat(1 / 1.15f, mouse);
                    return true;
                case InputEventMouseMotion { ButtonMask: MouseButtonMask.Middle } drag:
                    PanFlat(drag.Relative);
                    return true;
                case InputEventKey { Pressed: true } key:
                    switch (key.Keycode)
                    {
                        case Key.Home:
                            MoveFlatCamera(() => ResetFlatCamera());
                            return true;
                        case Key.W or Key.Up:
                            PanFlat(new Vector2(0, 40));
                            return true;
                        case Key.S or Key.Down:
                            PanFlat(new Vector2(0, -40));
                            return true;
                        case Key.A or Key.Left:
                            PanFlat(new Vector2(40, 0));
                            return true;
                        case Key.D or Key.Right:
                            PanFlat(new Vector2(-40, 0));
                            return true;
                    }

                    break;
            }

            return false;
        }

        /// <summary>Zooms by <paramref name="factor"/>, keeping the point under <paramref name="about"/> where it is.</summary>
        private void ZoomFlat(float factor, Vector2 about) => MoveFlatCamera(() =>
        {
            var square = (about - BoardOrigin - flatPan) / CellSize;
            flatZoom = Mathf.Clamp(flatZoom * factor, 1, Mathf.Max(1, (float)LargestFlatCell / FittedCell));
            flatPan = about - BoardOrigin - square * CellSize;
        });

        private void PanFlat(Vector2 by) => MoveFlatCamera(() => flatPan += by);

        /// <summary>
        /// Moves the camera, keeps the field covering the board area, and carries every token's
        /// drawn place along with it, so nothing glides across the board to catch up.
        /// </summary>
        private void MoveFlatCamera(System.Action move)
        {
            var oldPan = flatPan;
            var oldCell = (float)CellSize;
            move();
            var field = new Vector2(encounter.Width, encounter.Height) * CellSize;
            flatPan = new Vector2(
                field.X <= BoardArea.X ? 0 : Mathf.Clamp(flatPan.X, BoardArea.X - field.X, 0),
                field.Y <= BoardArea.Y ? 0 : Mathf.Clamp(flatPan.Y, BoardArea.Y - field.Y, 0));
            foreach (var who in new System.Collections.Generic.List<WoundBound.Game.Combat.Combatant>(tokenAt.Keys))
            {
                var square = (tokenAt[who] - BoardOrigin - oldPan) / oldCell;
                tokenAt[who] = BoardOrigin + flatPan + square * CellSize;
            }

            if (flatPlaying is not null || flatEffects.Count > 0)
            {
                // What's in the air was laid out for the old view: let it finish at once.
                flatEffects.Clear();
            }

            QueueRedraw();
        }

        /// <summary>Covers whatever of a zoomed field lies outside the board area, before the roster is drawn over the top.</summary>
        private void MaskBoardEdges()
        {
            var back = new Color(0.09f, 0.09f, 0.11f);
            var area = BoardRect;
            var screen = new Vector2(1280, 720);
            DrawRect(new Rect2(0, 0, screen.X, area.Position.Y), back);
            DrawRect(new Rect2(0, area.End.Y, screen.X, screen.Y - area.End.Y), back);
            DrawRect(new Rect2(0, area.Position.Y, area.Position.X, area.Size.Y), back);
            DrawRect(new Rect2(area.End.X, area.Position.Y, screen.X - area.End.X, area.Size.Y), back);
        }
    }
}
