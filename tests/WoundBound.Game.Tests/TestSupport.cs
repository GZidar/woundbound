using WoundBound.Game.Combat;
using WoundBound.Game.Data;

namespace WoundBound.Game.Tests;

/// <summary>Dice that come up exactly as a test says, in order, and fail loudly if asked for one more.</summary>
public sealed class ScriptedDice(params int[] faces) : IDiceRoller
{
    private readonly Queue<int> faces = new(faces);

    public int Roll(int sides)
    {
        if (!faces.TryDequeue(out var face))
        {
            throw new InvalidOperationException($"The test scripted no more dice, but a d{sides} was rolled.");
        }

        Assert.InRange(face, 1, sides);
        return face;
    }

    public int Remaining => faces.Count;
}

public static class TestPrompts
{
    /// <summary>
    /// Answers "no" to every Interrupt or Shelter on offer — the free choices a Mind- or Soul-secondary
    /// character is asked about — so a test can get on to the prompt it is about.
    /// </summary>
    public static void DeclineFreeChoices(this Encounter encounter)
    {
        while (encounter.Pending is { Kind: ReactionKind.Interrupt or ReactionKind.Shelter })
        {
            encounter.RespondToReaction(false);
        }
    }
}

public static class TestData
{
    /// <summary>
    /// The catalogue, with the Bramble Ratkin pinned to the numbers the scripted dice in these
    /// tests were written against (+0 to hit, d4, Body 3, DT 11, ST 11). The tests are about the
    /// rules, not the catalogue's prices: re-costing the ratkin to the Minion row (2026-11-06) is a
    /// change to the creature, not to anything a test here checks.
    /// </summary>
    public static GameData Load()
    {
        var data = GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", file)));
        return new GameData
        {
            RangeBands = data.RangeBands,
            Weapons = data.Weapons,
            Environment = data.Environment,
            Spells = data.Spells,
            Adversaries = data.Adversaries.Select(a => a.Name == "Bramble Ratkin"
                ? a with { BodyMax = 3, DodgeThreshold = 11, StabilityThreshold = 11, AttackModifier = 0, DamageModifier = 0 }
                : a).ToList(),
        };
    }
}
