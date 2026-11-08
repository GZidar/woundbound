namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Ground a scene allows the GM's creatures to start on: the rocks above a camp, the far side of
    /// a barricade, the reeds past a ford. A scenario names its zones with the map (<see cref="Setting.Deployment"/>),
    /// and the GM's setup picks the squares inside them (<see cref="Brains.Placement"/>). This is
    /// the third GM lever, beside cleverness and knowledge (the user's direction, 2026-11-03).
    /// </summary>
    /// <param name="Name">What the table calls it: "the rocks", "behind the crates".</param>
    /// <param name="Squares">Every square a creature may start on.</param>
    /// <param name="Hidden">
    /// Whether a band set up here may lie in wait for an ambush (Core Rules §9, "Ambush"): brush,
    /// rocks and the dark, yes; the open road, no.
    /// </param>
    public sealed record DeploymentZone(string Name, IReadOnlySet<GridPosition> Squares, bool Hidden = true)
    {
        /// <summary>A zone of the given rectangles, corners inclusive.</summary>
        public static DeploymentZone Of(string name, bool hidden, params (int X0, int Y0, int X1, int Y1)[] areas) =>
            new(name, Setting.Squares(areas), hidden);
    }

    /// <summary>
    /// How the GM sets up a fight whose creatures it places itself: how well (<see cref="Brains.SetupSkill"/>),
    /// whether they are lying in wait, and a seed so each fight is placed afresh. No setup means
    /// the scenario's own hand-laid squares stand.
    /// </summary>
    public sealed record GmSetup(Brains.SetupSkill Skill, bool Prepared = true, int Seed = 7);
}
