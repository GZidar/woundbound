namespace WoundBound.Game.Figures
{
    /// <summary>
    /// How a character's figure is built (the user's request, 2026-11-08): enough to tell one
    /// character from another at a glance, and no more — gender, height and girth. Only the board
    /// reads it. A creature with none is drawn as before. Hair colour came after (the user, 2026-11-08), bald among it.
    /// </summary>
    public sealed record Appearance(FigureGender Gender = FigureGender.Other, Stature Height = Stature.Medium, Girth Girth = Girth.Medium, HairColour Hair = HairColour.Brown)
    {
        /// <summary>
        /// The Quickstart characters as the books describe them — her, his — with heights and
        /// builds picked so no two of the five look alike. Null for anyone else.
        /// </summary>
        public static Appearance? ForQuickstart(string name) => name switch
        {
            "Salla Rinehart" => new(FigureGender.Female, Stature.Medium, Girth.Thin, HairColour.Black),
            "Drovan Thale" => new(FigureGender.Male, Stature.Short, Girth.Large, HairColour.Red),
            "Joren Blacke" => new(FigureGender.Male, Stature.Tall, Girth.Medium, HairColour.Brown),
            "Mirella Vask" => new(FigureGender.Female, Stature.Short, Girth.Medium, HairColour.Blond),
            "Elthric Dane" => new(FigureGender.Male, Stature.Tall, Girth.Thin, HairColour.Grey),
            "The Dumb Fighter" => new(FigureGender.Male, Stature.Medium, Girth.Large, HairColour.Bald),
            _ => null,
        };

        /// <summary>How much taller (or shorter) than a medium figure: the body's height is scaled by this.</summary>
        public float HeightScale => Height switch { Stature.Tall => 1.12f, Stature.Short => 0.86f, _ => 1f };

        /// <summary>How much broader (or slimmer) than a medium figure: torso, limbs and stance are scaled by this.</summary>
        public float GirthScale => Girth switch { Girth.Large => 1.3f, Girth.Thin => 0.8f, _ => 1f };

        /// <summary>The three choices in a line, for the screen.</summary>
        public string Describe() => $"{Gender}, {Height.ToString().ToLowerInvariant()}, {Girth.ToString().ToLowerInvariant()}, {(Hair == HairColour.Bald ? "bald" : Hair.ToString().ToLowerInvariant() + " hair")}";
    }

    public enum FigureGender
    {
        Male,
        Female,
        Other,
    }

    public enum Stature
    {
        Short,
        Medium,
        Tall,
    }

    public enum Girth
    {
        Thin,
        Medium,
        Large,
    }

    public enum HairColour
    {
        Black,
        Brown,
        Auburn,
        Red,
        Blond,
        Grey,
        White,
        Bald,
    }
}
