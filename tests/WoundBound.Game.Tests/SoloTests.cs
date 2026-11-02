using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Tests;

/// <summary>GM Reference §3, "Solo Adversaries": Solo Actions, one attack a round, Solo Resilience, and an area attack.</summary>
public class SoloTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>The simple GM, made to do what a test says on each of its turns.</summary>
    private sealed class ScriptedGm(Func<BattleView, GmTurn, GmStep?> step) : SimpleGmBrain
    {
        public override GmStep NextStep(BattleView view, GmTurn turn) => step(view, turn) ?? GmStep.End;

        public override GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> stillIdle) => null;

        public override bool PanicDodges(BattleView view, PendingReaction reaction) => false;
    }

    private static Combatant Stag(int x, int y) => CombatantFactory.FromAdversary(SampleEncounters.HollowStag(Data), "Stag", new GridPosition(x, y));

    private static Encounter Fight(int[] dice, IGmBrain? gm, params Combatant[] combatants) =>
        new(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice), gm);

    private static void PassTheRound(Encounter encounter, params Combatant[] party)
    {
        foreach (var character in party)
        {
            if (encounter.IsOver)
            {
                return;
            }

            encounter.TryBeginTurn(character);
            encounter.EndTurn();
            while (encounter.Pending is not null)
            {
                encounter.RespondToReaction(false);
            }

            encounter.RunAdversaryTurn();
            while (encounter.Pending is not null)
            {
                encounter.RespondToReaction(false);
            }
        }
    }

    [Fact]
    public void AT1Solo_TakesTwoTurnsARound()
    {
        var stag = Stag(10, 4);
        Assert.Equal(2, stag.TurnsPerRound);
        var party = Enumerable.Range(0, 3).Select(i => CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 2 + i * 2))).ToArray();
        var turns = 0;
        var encounter = Fight([], new ScriptedGm((_, turn) => { if (turn.Actor == stag && !turn.HasMoved) { turns++; } return GmStep.End; }), [.. party, stag]);

        PassTheRound(encounter, party);

        Assert.True(2 == turns, string.Join("\n", encounter.Log));
    }

    [Fact]
    public void ASolosSecondAttackInARound_CostsAStamina_AndCantBeHadWithout()
    {
        var stag = Stag(3, 4);
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 4));
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 6));
        var gm = new ScriptedGm((view, turn) => turn.HasAttacked || turn.Actor != stag ? GmStep.End : new GmStep.Attack(joren));
        var encounter = Fight([2, 3, 2, 3], gm, joren, mirella, stag);

        PassTheRound(encounter, joren, mirella);

        // By now a new round has begun, its counters fresh: the log says what the last one was.
        Assert.True(2 == encounter.Log.Count(l => l.StartsWith("Stag attacks Joren")), string.Join("\n", encounter.Log));
        Assert.Equal(stag.StaminaMax - 1, stag.Stamina);
        Assert.Contains(encounter.Log, l => l.Contains("spends 1 Stamina for another attack"));

        // Its attack made, and no Stamina left to buy another.
        stag.AttacksThisRound = 1;
        stag.Stamina = 0;
        Assert.NotNull(Encounter.SoloAttackRefusal(stag));
        Assert.False(new BattleView(encounter).CanAttack(stag, joren));
    }

    [Fact]
    public void ASolo_IsResilient_AgainstBeingTripped()
    {
        // Three dice for Disadvantage — 12, 3 and 2: the lowest two, 3+2, plus his bonus, fall short of ST 16.
        var stag = Stag(3, 4);
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 4));
        var encounter = Fight([12, 3, 2], null, joren, stag);
        stag.Stamina = 0;
        encounter.TryBeginTurn(joren);

        Assert.Null(Encounter.ControlRefusal(joren, ControlAction.Trip, stag));
        Assert.True(encounter.TryControl(ControlAction.Trip, stag), string.Join("\n", encounter.Log));

        Assert.Contains(encounter.Log, l => l.Contains("a Solo, Resilient: Disadvantage on the trip"));
        Assert.False(stag.IsProne);
    }

    [Fact]
    public void TheStampingFury_FallsOnEveryoneWithinASquare_AsOneAttack_EveryOtherRound()
    {
        var stag = Stag(3, 4);
        var fury = stag.Attacks.Single(a => a.Name == "Stamping Fury");
        Assert.Equal((1, true), (fury.AreaSquares, fury.EveryOtherRound));

        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 4));
        var salla = CombatantFactory.FromCharacter(SampleEncounters.SallaRinehart(Data), new GridPosition(5, 5));
        var far = CombatantFactory.FromCharacter(SampleEncounters.DrovanThale(Data), new GridPosition(9, 9));
        var gm = new ScriptedGm((view, turn) => turn.HasAttacked || turn.Actor != stag ? GmStep.End : new GmStep.Attack(joren, With: fury));
        var encounter = Fight([9, 8, 2, 9, 8, 2], gm, joren, salla, far, stag);

        PassTheRound(encounter, joren);

        var stamped = encounter.Log.Where(l => l.StartsWith("Stag attacks")).ToList();
        Assert.Equal(2, stamped.Count);
        Assert.Contains(stamped, l => l.Contains("Joren"));
        Assert.Contains(stamped, l => l.Contains("Salla"));
        Assert.Equal(1, stag.LastAreaAttackRound);
    }
}
