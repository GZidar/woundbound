using System;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The player's say in how their character's figure looks (the user's request, 2026-11-08):
    /// gender, height, girth and hair colour, picked in the Character tab and kept between fights in
    /// user://appearances.cfg, by character name. The Quickstart five start from the books.
    /// </summary>
    public partial class Battle
    {
        private const string AppearanceFile = "user://appearances.cfg";

        private HBoxContainer figureRow = null!;
        private OptionButton genderPick = null!;
        private OptionButton heightPick = null!;
        private OptionButton girthPick = null!;
        private OptionButton hairPick = null!;
        private Combatant? figureFor;
        private bool fillingFigure;

        /// <summary>The row of three choices, under the character sheet.</summary>
        private void BuildFigureRow(Control column)
        {
            figureRow = new HBoxContainer();
            figureRow.AddThemeConstantOverride("separation", 8);
            figureRow.AddChild(new Label { Text = "Figure:" });
            genderPick = Pick<FigureGender>("How the figure is drawn: hair and build.");
            heightPick = Pick<Stature>("How tall the figure stands.");
            girthPick = Pick<Girth>("How broad the figure is.");
            hairPick = Pick<HairColour>("The figure's hair colour, or bald.");
            column.AddChild(figureRow);

            OptionButton Pick<T>(string tip)
                where T : struct, Enum
            {
                var pick = new OptionButton { TooltipText = tip };
                foreach (var value in Enum.GetValues<T>())
                {
                    pick.AddItem(value.ToString());
                }

                pick.ItemSelected += _ => FigureChanged();
                figureRow.AddChild(pick);
                return pick;
            }
        }

        /// <summary>Shows the choices for <paramref name="character"/>, or hides them for anybody who isn't a player's.</summary>
        private void ShowFigure(Combatant character)
        {
            figureRow.Visible = character.Side == Side.Party && character.Build is not null;
            if (!figureRow.Visible || ReferenceEquals(character, figureFor))
            {
                return;
            }

            figureFor = character;
            var look = character.Appearance ?? new Appearance();
            fillingFigure = true;
            genderPick.Select((int)look.Gender);
            heightPick.Select((int)look.Height);
            girthPick.Select((int)look.Girth);
            hairPick.Select((int)look.Hair);
            fillingFigure = false;
        }

        private void FigureChanged()
        {
            if (fillingFigure || figureFor is not { } character)
            {
                return;
            }

            character.Appearance = new Appearance((FigureGender)genderPick.Selected, (Stature)heightPick.Selected, (Girth)girthPick.Selected, (HairColour)hairPick.Selected);
            SaveAppearance(character.Name, character.Appearance);
            board3D?.Rebuild(character);
        }

        /// <summary>Gives every character in a new fight the figure their player chose for them, where they chose one.</summary>
        private void ApplyAppearances(Encounter fight)
        {
            figureFor = null;
            var book = new ConfigFile();
            if (book.Load(AppearanceFile) != Error.Ok)
            {
                return;
            }

            foreach (var character in fight.Combatants.Where(c => c.Side == Side.Party && c.Build is not null))
            {
                if (book.HasSection(character.Name))
                {
                    character.Appearance = new Appearance(
                        Read(book, character.Name, "gender", FigureGender.Other),
                        Read(book, character.Name, "height", Stature.Medium),
                        Read(book, character.Name, "girth", Girth.Medium),
                        Read(book, character.Name, "hair", character.Appearance?.Hair ?? HairColour.Brown));
                }
            }

            static T Read<T>(ConfigFile book, string section, string key, T fallback)
                where T : struct, Enum =>
                Enum.TryParse<T>((string)book.GetValue(section, key, fallback.ToString()), out var value) ? value : fallback;
        }

        private static void SaveAppearance(string name, Appearance look)
        {
            var book = new ConfigFile();
            book.Load(AppearanceFile);
            book.SetValue(name, "gender", look.Gender.ToString());
            book.SetValue(name, "height", look.Height.ToString());
            book.SetValue(name, "girth", look.Girth.ToString());
            book.SetValue(name, "hair", look.Hair.ToString());
            book.Save(AppearanceFile);
        }
    }
}
