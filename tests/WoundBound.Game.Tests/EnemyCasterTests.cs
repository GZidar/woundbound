using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Tests;

/// <summary>GM Reference §9, "On Casters": foes whose Caster trait lets them cast the spells on their statblocks.</summary>
public class EnemyCasterTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    private static Combatant Foe(string statblock, int x, int y) =>
        CombatantFactory.FromAdversary(Data.Adversary(statblock), statblock, new GridPosition(x, y), Data.Spells);

    /// <summary>Mirella and Joren against whichever foes a test sets out, the GM doing nothing it isn't made to.</summary>
    private static Encounter Facing(int[] dice, params Combatant[] foes)
    {
        var combatants = new List<Combatant>
        {
            CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4)),
            CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 6)),
        };
        combatants.AddRange(foes);
        return new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, combatants, Data.RangeBands, new ScriptedDice(dice));
    }

    [Fact]
    public void TheBloodwright_CastsItsAttackLine_AsABloodMagicHarm_AtItsTier()
    {
        // "Blood Lance - +3 v DT / 2d6 Body. Cost: 2 Body wounds" — T2, Small, at Short.
        var bloodwright = Foe("Hollow Bloodwright", 5, 4);
        var lance = Assert.Single(bloodwright.Spells);

        Assert.Equal(("Blood Lance", MagicSource.BloodMagic, Tier.T2, SpellType.Harm, WoundType.Body),
            (lance.Name, lance.Source, lance.SpellTier, lance.SpellType, lance.DamageType));
        Assert.Equal("Unarmed", bloodwright.Attack.Name);

        var encounter = Facing([], bloodwright);
        var plan = encounter.PlanCastFrom(bloodwright, bloodwright.Position,
            new SpellCast(lance) { Target = encounter.Combatants.Single(c => c.Name == "Mirella Vask") }, checkTurn: false);
        Assert.Equal((2, WoundType.Body, 3), (plan.Cost, plan.Track, plan.CastBonus));
    }

    [Fact]
    public void ABloodLance_AtACharacter_RollsPerTheStatblock_AndTheyMayPanicDodge()
    {
        // The Bloodwright's turn: 9+5 +3 = 17 against Mirella's DT 12, a hit — a dodge would make 14,
        // still a hit, so none is offered. 2d6: 4 and 3, less 1 for her leather.
        var bloodwright = Foe("Hollow Bloodwright", 5, 4);
        var encounter = Facing([9, 5, 4, 3], bloodwright);
        var mirella = encounter.Combatants.Single(c => c.Name == "Mirella Vask");

        var gm = new CastingGm(new SpellCast(bloodwright.Spells[0]) { Target = mirella });
        var scripted = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, encounter.Combatants, Data.RangeBands,
            new ScriptedDice(9, 5, 4, 3), gm);
        scripted.TryBeginTurn(mirella);
        scripted.EndTurn();
        scripted.RunAdversaryTurn();
        scripted.DeclineFreeChoices();

        Assert.Equal(mirella.BodyMax - 6, mirella.Body);
        Assert.Equal(bloodwright.BodyMax - 2, bloodwright.Body);
        Assert.True(bloodwright.HasCastSpell);
    }

    [Fact]
    public void TheHexCaller_CastsOnlyTheFearItsGlareIsWrittenFor_ForFourRounds()
    {
        var hex = Foe("Bog Hex-Caller", 5, 6);
        var glare = Assert.Single(hex.Spells);
        var encounter = Facing([], hex);
        var joren = encounter.Combatants.Single(c => c.Name == "Joren Blacke");

        Assert.Equal((SpellType.Control, Tier.T2), (glare.SpellType, glare.SpellTier));
        Assert.Equal(ControlEffect.Fear, SpellArea.FixedEffectOf(glare));
        Assert.Contains("written to cause Fear", encounter.PlanCastFrom(hex, hex.Position,
            new SpellCast(glare) { Target = joren, Effect = ControlEffect.Compulsion }, checkTurn: false).Refusal);
        Assert.Equal(4, SpellCalculator(glare));
    }

    private static int SpellCalculator(Rules.Models.SpellDefinition spell) =>
        Rules.Calculation.SpellCalculator.ControlIllusionDurationRounds(spell.SpellTier);

    [Fact]
    public void TheChanter_HasItsSpellsRows_CastsAtPlusOne_AndHealsThroughBleeding()
    {
        var chanter = Foe("Wyrding Chanter", 6, 4);
        var bandit = CombatantFactory.FromAdversary(SampleEncounters.RoadsideBandit, "Bandit", new GridPosition(6, 5));
        bandit.Body = 2;
        var encounter = Facing([], chanter, bandit);

        Assert.Equal(["Blessing", "Vital Transfer"], chanter.Spells.Select(s => s.Name));
        var heal = encounter.PlanCastFrom(chanter, chanter.Position, new SpellCast(chanter.Spells[1]) { Target = bandit }, checkTurn: false);
        Assert.Null(heal.Refusal);
        Assert.Equal((1, WoundType.Body), (heal.Cost, heal.Track));
        Assert.Contains("never target its own caster",
            encounter.PlanCastFrom(chanter, chanter.Position, new SpellCast(chanter.Spells[1]) { Target = chanter }, checkTurn: false).Refusal);

        var bless = encounter.PlanCastFrom(chanter, chanter.Position, new SpellCast(chanter.Spells[0]) { Target = bandit }, checkTurn: false);
        Assert.Equal((WoundType.Soul, 1), (bless.Track, bless.CastBonus));
    }

    [Fact]
    public void AFrightenedCharacter_GivesGroundOnTheirOwnTurn()
    {
        // The Hex-Caller's glare: 10+8 +3 = 21 against Joren's RT 12 — he spends his Stamina to make it 14,
        // still short — Frightened. Then Mirella's turn, then his: from Short, he must finish in Middle.
        var hex = Foe("Bog Hex-Caller", 5, 6);
        var joren = CombatantFactory.FromCharacter(SampleEncounters.JorenBlacke(Data), new GridPosition(2, 6));
        var mirella = CombatantFactory.FromCharacter(SampleEncounters.MirellaVask(Data), new GridPosition(2, 4));
        var gm = new CastingGm(new SpellCast(hex.Spells[0]) { Target = joren, Effect = ControlEffect.Fear });
        var encounter = new Encounter(SampleEncounters.FieldWidth, SampleEncounters.FieldHeight, [mirella, joren, hex], Data.RangeBands,
            new ScriptedDice(10, 8, 9, 9), gm);

        encounter.TryBeginTurn(mirella);
        encounter.EndTurn();
        encounter.RunAdversaryTurn();
        Assert.Contains(CharacterCondition.Frightened, joren.Conditions);

        if (encounter.Phase == EncounterPhase.AwaitingReaction)
        {
            encounter.RespondToReaction(false);
        }

        Assert.True(encounter.TryBeginTurn(joren) || encounter.Active == joren);
        Assert.InRange(joren.Position.DistanceTo(hex.Position), 5, 8);
        Assert.False(Encounter.CanMoveItself(joren));
    }

    /// <summary>A GM that casts the one spell it's given, once, and otherwise stands still.</summary>
    private sealed class CastingGm(SpellCast cast) : Combat.Brains.SimpleGmBrain
    {
        private bool cast1;

        public override Combat.Brains.GmStep NextStep(Combat.Brains.BattleView view, Combat.Brains.GmTurn turn)
        {
            if (!cast1 && turn.Actor.Spells.Contains(cast.Spell))
            {
                cast1 = true;
                return new Combat.Brains.GmStep.Cast(cast);
            }

            return Combat.Brains.GmStep.End;
        }

        public override Combat.Brains.GmCloseIn? NextCloseIn(Combat.Brains.BattleView view, IReadOnlyList<Combatant> stillIdle) => null;

        public override Combatant ChooseOwedTurn(Combat.Brains.BattleView view) =>
            view.Adversaries.FirstOrDefault(a => a.Spells.Contains(cast.Spell)) ?? base.ChooseOwedTurn(view);
    }
}
