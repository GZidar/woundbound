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

    /// <summary>
    /// Dice that roll a fixed stream: each roll is a hash of the seed and how many came before it, so
    /// a <see cref="Copy"/> rolls exactly what this one will roll next. The game never needs that; a
    /// GM that cheats does (<see cref="Brains.Utility.Foresight.Prescient"/>), to find how much better
    /// than its best a GM could play at all.
    /// </summary>
    public sealed class StreamDiceRoller(ulong seed, long position = 0) : IDiceRoller
    {
        private long position = position;

        public int Roll(int sides) => 1 + (int)(Mix(seed ^ Mix((ulong)position++)) % (ulong)sides);

        /// <summary>Dice that roll on from here exactly as these will.</summary>
        public StreamDiceRoller Copy() => new(seed, position);

        /// <summary>SplitMix64's finaliser.</summary>
        private static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }
}
