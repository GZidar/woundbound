using WoundBound.Game.Combat;
using WoundBound.Game.Combat.Brains;
using WoundBound.Game.Combat.Brains.Utility;
using WoundBound.Game.Simulation;

namespace WoundBound.Game.Tests;

/// <summary>A fight forked where it stands carries on as the fight itself would.</summary>
public class ForkTests
{
    private static readonly Game.Data.GameData Data = TestData.Load();

    /// <summary>A GM brain that, at its <c>at</c>-th step, forks the fight and plays the fork to the end, keeping its log.</summary>
    private sealed class ForkingBrain(IGmBrain inner, int at, Func<IPartyBrain> party) : IGmBrain
    {
        private int asked;

        public List<string>? ForkLog { get; private set; }

        public bool PeersThroughFog => inner.PeersThroughFog;

        public Combatant ChooseOwedTurn(BattleView view) => inner.ChooseOwedTurn(view);

        public Combatant? ChooseExtraTurn(BattleView view) => inner.ChooseExtraTurn(view);

        public GmCloseIn? NextCloseIn(BattleView view, IReadOnlyList<Combatant> idle) => inner.NextCloseIn(view, idle);

        public bool StrikesAtLeaver(BattleView view, Combatant enemy, Combatant leaver) => inner.StrikesAtLeaver(view, enemy, leaver);

        public bool PanicDodges(BattleView view, PendingReaction offer) => inner.PanicDodges(view, offer);

        public bool StandsFirm(BattleView view, PendingReaction offer) => inner.StandsFirm(view, offer);

        public bool Backlashes(BattleView view, PendingReaction offer) => inner.Backlashes(view, offer);

        public bool Sustains(BattleView view, PendingReaction offer) => inner.Sustains(view, offer);

        public int ShelterAmount(BattleView view, PendingReaction offer) => inner.ShelterAmount(view, offer);

        public bool Interrupts(BattleView view, PendingReaction offer) => inner.Interrupts(view, offer);

        public LeaverResponse RespondsToLeaver(BattleView view, Combatant enemy, Combatant leaver, IReadOnlyList<LeaverResponse> options) =>
            inner.RespondsToLeaver(view, enemy, leaver, options);

        public bool ArrestsShove(BattleView view, PendingReaction offer) => inner.ArrestsShove(view, offer);

        public GmStep NextStep(BattleView view, GmTurn turn)
        {
            if (++asked == at && view.Encounter.CanForkAtStep)
            {
                // The dice and this brain go into the fork as copies: it then rolls and chooses as this fight will.
                var step = inner.NextStep(view, turn);
                var fork = view.Encounter.Fork(dice: null, gmBrain: null, out var counterpart);
                fork.ResumeWith((GmStep)counterpart(step));
                Simulator.PlayOne(fork, party());
                ForkLog = fork.Log.ToList();
                return step;
            }

            return inner.NextStep(view, turn);
        }
    }

    [Theory]
    [InlineData("rivals", 3, 1)]
    [InlineData("rivals", 9, 4)]
    [InlineData("witches", 5, 2)]
    [InlineData("camp", 7, 3)]
    [InlineData("champion", 4, 5)]
    [InlineData("roadblock", 6, 6)]
    [InlineData("glamour", 8, 7)]
    public void AFork_PlayedOnTheSameDice_EndsAsTheFightDoes_AndLeavesTheFightAlone(string fight, int at, int seed)
    {
        // The same fight with nobody forking it: what both the fight and its fork must come to.
        var baseline = Play(fight, seed, new UtilityGmBrain(BrainSkill.Balanced, new Random(seed), foresight: Foresight.None));

        var brain = new ForkingBrain(new UtilityGmBrain(BrainSkill.Balanced, new Random(seed), foresight: Foresight.None), at,
            () => new TacticalPartyBrain(PartyTactics.Presets[0]));
        var forked = Play(fight, seed, brain);

        Assert.NotNull(brain.ForkLog);
        Assert.Equal(baseline, brain.ForkLog!);
        Assert.Equal(baseline, forked);
    }

    private static List<string> Play(string fight, int seed, IGmBrain gm)
    {
        var dice = new RandomDiceRoller(new Random(seed));
        var encounter = fight switch
        {
            "rivals" => SampleEncounters.RivalBand(Data, dice, gm),
            "witches" => SampleEncounters.HedgeWitchesToll(Data, dice, gm),
            "camp" => SampleEncounters.CampfireAmbush(Data, dice, gm, ambush: true),
            "roadblock" => SampleEncounters.Roadblock(Data, dice, gm, ambush: true),
            "glamour" => SampleEncounters.GlamouredMill(Data, dice, gm),
            _ => SampleEncounters.ChampionOnTheGreen(Data, dice, gm),
        };

        Simulator.PlayOne(encounter, new TacticalPartyBrain(PartyTactics.Presets[0]));
        return encounter.Log.ToList();
    }
}
