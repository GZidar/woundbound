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

public static class TestData
{
    public static GameData Load() =>
        GameData.Load(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", file)));
}
