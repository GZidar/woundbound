namespace WoundBound.Game.Combat
{
    /// <summary>
    /// The one source of randomness in an encounter. <see cref="Rules.Calculation.RollCalculator"/>
    /// takes already-rolled dice so it stays pure; this is where they get rolled, and what a test
    /// replaces to script a fight.
    /// </summary>
    public interface IDiceRoller
    {
        /// <summary>A single die with <paramref name="sides"/> faces, 1 to <paramref name="sides"/>.</summary>
        int Roll(int sides);
    }

    public sealed class RandomDiceRoller(Random? random = null) : IDiceRoller
    {
        private readonly Random random = random ?? Random.Shared;

        public int Roll(int sides) => random.Next(1, sides + 1);
    }
}
