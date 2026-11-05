using WoundBound.Game.Combat.Brains;

namespace WoundBound.Game.Combat
{
    public sealed partial class Encounter
    {
        /// <summary>
        /// The GM turn waiting on its brain's next step, and what follows the turn — set only while
        /// <see cref="IGmBrain.NextStep"/> is being asked, so a fork taken then can carry it on.
        /// </summary>
        private (GmTurn Turn, Action Done)? stepAwaited;

        /// <summary>Whether a GM brain is being asked for a step on a turn a fork could take up: not a surprise action.</summary>
        internal bool CanForkAtStep => stepAwaited is { } awaited && !awaited.Turn.IsSurprise && Pending is null;

        /// <summary>
        /// A copy of the fight as it stands, to be played forward on its own — the dice it rolls with
        /// and the GM brain it asks from here on put in, and nobody watching its log. Left null, the
        /// dice and the brain are copied too, which only a test that the copy is faithful wants.
        /// </summary>
        /// <param name="counterpart">Finds, for anything of this fight's, its counterpart in the copy.</param>
        /// <param name="scratch">A map for the copying to reuse, kept by a caller that forks many times on one thread (<see cref="GraphFork(Dictionary{object, object?}?)"/>).</param>
        internal Encounter Fork(IDiceRoller? dice, IGmBrain? gmBrain, out Func<object, object> counterpart, Dictionary<object, object?>? scratch = null)
        {
            var fork = new GraphFork(scratch);
            if (dice is not null)
            {
                fork.Seed(this.dice, dice);
            }

            if (gmBrain is not null)
            {
                fork.Seed(gm, gmBrain);
            }

            // Whatever watches this fight stays with this fight. The original is only read, so
            // forks may be taken side by side.
            if (Logged is { } logged)
            {
                fork.Seed(logged, null);
            }

            if (happened is { } watching)
            {
                fork.Seed(watching, null);
            }

            var copy = fork.Copy(this);
            counterpart = fork.Copy;
            return copy;
        }

        /// <summary>
        /// In a fork taken while a GM brain was being asked for a step: takes <paramref name="step"/>
        /// as that step, and plays on until the fight next waits on somebody.
        /// </summary>
        internal void ResumeWith(GmStep step)
        {
            if (stepAwaited is not { } awaited)
            {
                throw new InvalidOperationException("This fight isn't waiting on a GM step.");
            }

            stepAwaited = null;
            queuedStep = step;
            TakeStep(awaited.Turn, awaited.Done);
        }
    }
}
