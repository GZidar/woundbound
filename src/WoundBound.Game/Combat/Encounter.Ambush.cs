using WoundBound.Game.Combat.Brains;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Catalog;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// An ambush (Core Rules §9, "Ambush"; the Quickstart's Roadside Ambush): adversaries lying in
    /// wait on purpose are met with one Awareness check, made by the party's most attentive
    /// character, against one Stealth roll for the ambushers — 2d12 plus their best Stealth, plus
    /// what the dark where they lie adds, as it would to their Dodge. On a failure the party is
    /// ambushed: before round 1, each adversary lying in wait takes one surprise action from where
    /// it lies — no move, no Stamina for more, and no Interrupt answers it, since nobody has seen it
    /// coming. Then round 1 begins as any other.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>The adversaries still to take their surprise action; empty once the ambush is over or never sprung.</summary>
        private readonly Queue<Combatant> surprising = new();

        /// <summary>Whether the ambush's surprise actions are under way.</summary>
        public bool IsSurprise { get; private set; }

        /// <summary>
        /// Rolls the ambush, if anybody lies in wait: true when the party is caught and the surprise
        /// actions are to come, false when it spotted them or there is no ambush at all.
        /// </summary>
        private bool SpringAmbush()
        {
            var lurkers = combatants.Where(c => c.Side == Side.Adversaries && c.LiesInWait && !c.IsOutOfFight).ToList();
            var party = combatants.Where(c => c.Side == Side.Party && !c.IsOutOfFight).ToList();
            foreach (var lurker in lurkers)
            {
                lurker.LiesInWait = false;
            }

            if (lurkers.Count == 0 || party.Count == 0)
            {
                return false;
            }

            // One roll for the ambush: 2d12 and the best of them at hiding, and the dark they lie in.
            var dice1 = dice.Roll(RollCalculator.HighestFace);
            var dice2 = dice.Roll(RollCalculator.HighestFace);
            var hiding = lurkers.Max(l => l.SkillBonus("Stealth"));
            var dark = environment is { } tables ? lurkers.Max(l => EnvironmentCalculator.DodgeThresholdModifier(tables, VisibilityOf(l))) : 0;
            var stealth = dice1 + dice2 + hiding + dark;

            // Awareness, not Search: nobody is looking for an ambush.
            var watcher = party.MaxBy(c => c.SkillBonus("Awareness"))!;
            var roll = RollAgainst(watcher.SkillBonus("Awareness"), stealth, advantage: false, disadvantage: false, watcher);
            Write($"Something lies in wait. Stealth ({dice1}+{dice2}){Signed(hiding)}" + (dark > 0 ? $" +{dark} for the dark" : string.Empty)
                  + $" = {stealth}; {watcher.Name}'s Awareness {DescribeRoll(roll)}.");
            if (roll.Success == true)
            {
                Write($"{watcher.Name} catches movement where nothing should move — the ambush is seen before it is sprung.");
                return false;
            }

            Write("Nobody sees it coming. The party is ambushed.");
            foreach (var lurker in lurkers)
            {
                surprising.Enqueue(lurker);
            }

            IsSurprise = true;
            Phase = EncounterPhase.AdversaryTurn;
            return true;

            static string Signed(int n) => n >= 0 ? $"+{n}" : $"−{-n}";
        }

        /// <summary>Takes the next surprise action; once the last is taken, round 1 begins.</summary>
        private void TakeSurpriseAction()
        {
            var actor = surprising.Dequeue();
            void Next()
            {
                FinishTurn(actor);
                Active = null;
                if (CheckForEnd())
                {
                    return;
                }

                if (surprising.Count == 0)
                {
                    IsSurprise = false;
                    StartRound();
                }
            }

            if (actor.IsOutOfFight || !ConditionCatalog.CanAct(actor.Conditions))
            {
                Next();
                return;
            }

            Active = actor;
            actor.HasMove = false;
            actor.ActionsRemaining = 1;
            Write($"{actor.Name} strikes from hiding — a surprise action.");
            TakeStep(new GmTurn(actor, isExtraTurn: false, bonusAvailable: false) { IsSurprise = true }, Next);
        }
    }
}
