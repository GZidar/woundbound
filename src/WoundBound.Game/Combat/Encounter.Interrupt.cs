using WoundBound.Rules.Catalog;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// An adversary's declared action a character has Interrupted: <paramref name="Actor"/>, about to act
    /// at <paramref name="Against"/>. A turn that lands on the actor puts it at Disadvantage; one that
    /// takes its target out of reach may leave it nothing to do (Core Rules §3, "Interrupt").
    /// </summary>
    public sealed record Interruption(Combatant Actor, Combatant? Against);

    /// <summary>
    /// The two Secondary Wound rules that answer the GM: Interrupt (Mind), a turn taken inside the
    /// GM's when an adversary declares an action; and Shelter (Soul), an ally's damage taken as Soul
    /// (Core Rules §3, "Secondary Wound Rules").
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>Where the GM's turn picks up again once an Interrupt's turn is over.</summary>
        private Action? interruptResume;

        /// <summary>An adversary whose declared action an Interrupt landed on: it is made with Disadvantage.</summary>
        private Combatant? caughtMidMotion;

        /// <summary>
        /// An adversary's action, declared before it is rolled — "the GM says what each adversary is
        /// about to do before rolling for it", the moment a Mind-secondary character can Interrupt.
        /// If an Interrupt has left the action nothing to do, it is spent.
        /// </summary>
        private void Declare(Combatant actor, string what, Combatant? against, Func<bool> stillPossible, Action resolve, Action spent)
        {
            Write($"{actor.Name} declares: {what}.");
            lastDeclaredTarget = against;
            OfferInterrupt(actor, new Queue<Combatant>(InterruptersFor(actor)), () =>
            {
                if (actor.IsOutOfFight)
                {
                    spent();
                    return;
                }

                if (!stillPossible())
                {
                    actor.ActionsRemaining = Math.Max(0, actor.ActionsRemaining - 1);
                    Write($"{actor.Name}'s {what} has nothing left to do — the action is spent.");
                    caughtMidMotion = null;
                    spent();
                    return;
                }

                resolve();
            });
        }

        /// <summary>
        /// Who could Interrupt now: a Mind-secondary character still fighting, yet to act this round,
        /// with the Mind to pay — and a Sentry who has acted already, with a tactical spend and a
        /// Stamina left for the action it buys (Core Rules §7, "Trained as Sentry").
        /// </summary>
        private IEnumerable<Combatant> InterruptersFor(Combatant actor) =>
            partyStillToAct.Where(c => c.HasInterrupt && !c.IsOutOfFight && !c.HasInterruptedThisRound && Encounter.InterruptRefusal(c) is null)
                .Concat(combatants.Where(c => c.Side == Side.Party && c.IsSentry && !partyStillToAct.Contains(c) && c.HasActedThisRound
                                              && !c.IsOutOfFight && !c.HasInterruptedThisRound && Encounter.InterruptRefusal(c) is null
                                              && TacticalSpendRefusal(c) is null));

        /// <summary>The Sentry taking the one action an Interrupt after acting buys, rather than a turn.</summary>
        private Combatant? sentryAction;

        /// <summary>Whether <paramref name="character"/> would Interrupt now as a Sentry — having acted this round — rather than taking their turn early.</summary>
        public bool InterruptsAsSentry(Combatant character) => character.IsSentry && !partyStillToAct.Contains(character);

        /// <summary>
        /// Why <paramref name="character"/> can't pay an Interrupt's 1 Mind now, or null if they can.
        /// Their last Mind will do: they may Backlash it, or drop to 0 once the turn is taken (Core
        /// Rules §4, "Backlash on a spend").
        /// </summary>
        public static string? InterruptRefusal(Combatant character) =>
            !character.HasInterrupt ? "Only a character with Mind as their Secondary Wound can Interrupt."
            : PlayStateCalculator.CanSpendMindOrSoul(character.Mind, character.Body, 1, useMetaphysicalBacklash: false)
                ? null
                : "No Mind to pay for it.";

        private void OfferInterrupt(Combatant actor, Queue<Combatant> candidates, Action then)
        {
            if (!candidates.TryDequeue(out var interrupter))
            {
                then();
                return;
            }

            OfferReaction(new PendingReaction(ReactionKind.Interrupt, interrupter, actor, null, 0,
                    InterruptsAsSentry(interrupter)
                        ? $"{actor.Name} is about to act. {interrupter.Name}, Trained as Sentry, may Interrupt though they've acted — one action now, for 1 Mind and 1 Stamina (a tactical spend: the GM gains an Opportunity)."
                        : $"{actor.Name} is about to act. {interrupter.Name} may Interrupt — take their turn now, for 1 Mind, before it is rolled.",
                    Against: lastDeclaredTarget),
                takes =>
                {
                    if (!takes)
                    {
                        OfferInterrupt(actor, candidates, then);
                        return;
                    }

                    Interrupt(interrupter, actor, then);
                });
        }

        /// <summary>The declared action the party turn under way was taken inside of, if it's an Interrupt's.</summary>
        public Interruption? Interrupting { get; private set; }

        /// <summary>Who the action being declared is aimed at, for the Interrupt prompt.</summary>
        private Combatant? lastDeclaredTarget;

        /// <summary>
        /// Takes <paramref name="interrupter"/>'s turn now, inside the GM's: 1 Mind, then the whole of
        /// their turn, after which the declared action goes ahead — with Disadvantage if the turn landed
        /// on its maker, unless that is a Solo.
        /// </summary>
        private void Interrupt(Combatant interrupter, Combatant actor, Action then)
        {
            // The last Mind: Backlash it onto Body if the Body can spare it, or let it go to 0 —
            // the turn is taken first, and only then are they Stunned. A cost never cancels what it
            // paid for (Core Rules §4).
            if (interrupter.Mind == 1 && PlayStateCalculator.CanSpendMindOrSoul(1, interrupter.Body, 1, useMetaphysicalBacklash: true))
            {
                OfferReaction(new PendingReaction(ReactionKind.Backlash, interrupter, interrupter, null, 0,
                        $"The Interrupt takes {interrupter.Name}'s last Mind. Backlash holds them at 1 and takes 1 Body instead; " +
                        "or they drop to 0 and are Stunned once the turn is taken.",
                        ImpactWounds: 1),
                    backlash => TakeInterrupt(interrupter, actor, backlash, then));
                return;
            }

            TakeInterrupt(interrupter, actor, backlash: false, then);
        }

        private void TakeInterrupt(Combatant interrupter, Combatant actor, bool backlash, Action then)
        {
            var drops = interrupter.Mind == 1 && !backlash;
            if (!drops)
            {
                PlayStateCalculator.TrySpendMindOrSoul(interrupter.Mind, interrupter.Body, 1, backlash, out var mind, out var body);
                interrupter.Mind = mind;
                interrupter.Body = body;
            }

            interrupter.TendEligible[WoundType.Mind] = true;
            interrupter.HasInterruptedThisRound = true;
            var asSentry = InterruptsAsSentry(interrupter);
            if (asSentry)
            {
                PayTacticalSpend(interrupter, CombatActionCatalog.ExtraActionStaminaCost);
            }

            Write($"{interrupter.Name} Interrupts {actor.Name} — 1 Mind{(backlash ? ", Backlashed onto Body" : drops ? ", their last" : string.Empty)}" +
                  (asSentry ? $" and 1 Stamina ({interrupter.Stamina} left) — and, having acted already, takes one action now." : " — and takes their turn now."));
            if (asSentry)
            {
                GrantImmediateOpportunity(interrupter);
            }

            var before = Snapshot(actor);
            Interrupting = new Interruption(actor, lastDeclaredTarget);
            interruptResume = () =>
            {
                Interrupting = null;
                if (drops && interrupter.Mind > 0)
                {
                    interrupter.Mind = 0;
                    Write($"{interrupter.Name} has spent the last of their Mind, and is Stunned.");
                }

                if (!actor.IsOutOfFight && Snapshot(actor) != before)
                {
                    if (actor.Role == AdversaryRole.Solo)
                    {
                        Write($"{actor.Name} shrugs off being caught mid-motion — a Solo ignores the Disadvantage.");
                    }
                    else
                    {
                        caughtMidMotion = actor;
                        Write($"{actor.Name} is caught mid-motion: its action is made with Disadvantage.");
                    }
                }

                then();
            };

            if (asSentry)
            {
                StartSentryAction(interrupter);
                return;
            }

            StartPartyTurn(interrupter);
        }

        /// <summary>
        /// A Sentry's Interrupt after acting: "one action rather than a whole turn" — no move (an
        /// action can still be spent on one), and nothing that starts or ends a turn.
        /// </summary>
        private void StartSentryAction(Combatant sentry)
        {
            sentryAction = sentry;
            Active = sentry;
            sentry.HasMove = false;
            sentry.ActionsRemaining = 1;
            sentry.OffHandOwed = false;
            Phase = EncounterPhase.PartyTurn;
            Write($"{sentry.Name} acts: one action.");
        }

        /// <summary>Everything an Interrupt's turn could have done to a creature, to tell whether it landed on it.</summary>
        private static string Snapshot(Combatant c) =>
            $"{c.Body}|{c.Mind}|{c.Soul}|{c.Position}|{string.Join(",", c.Conditions)}|{c.HeldBy?.Name}|{c.ThresholdModifiers.Count}|{c.IsOutOfFight}";

        /// <summary>
        /// Damage about to land on <paramref name="hurt"/>, past their armour: a Soul-secondary ally within
        /// Short range who hasn't Sheltered this round may take it instead, as Soul, their own armour no
        /// help — <paramref name="then"/> is told how much is left for <paramref name="hurt"/>.
        /// </summary>
        private void Shelter(Combatant hurt, int amount, WoundType type, Action<int> then)
        {
            if (amount <= 0)
            {
                then(amount);
                return;
            }

            var shelterers = new Queue<Combatant>(combatants.Where(c => c.Side == hurt.Side && c != hurt && c.HasShelter && !c.HasShelteredThisRound
                && !c.IsOutOfFight && ShelterReaches(c, hurt)));
            OfferShelter(hurt, amount, type, shelterers, then);
        }

        /// <summary>
        /// Whether <paramref name="shelterer"/>'s Shelter reaches <paramref name="hurt"/>: within Short —
        /// or, Trained as Field Medic, anywhere they can see them, which in the dark is back to the
        /// ordinary reach (Core Rules §7).
        /// </summary>
        private bool ShelterReaches(Combatant shelterer, Combatant hurt)
        {
            var distance = shelterer.DistanceTo(hurt);
            return distance <= ShortRangeSquares
                   || (shelterer.IsFieldMedic && (Setting.Walls.Count == 0 || Sight.Clear(shelterer.Position, hurt.Position, Setting.IsWall))
                       && CanBeTargeted(hurt, hurt.Position, distance, shelterer));
        }

        /// <summary>The far edge of Short, in squares — Shelter's reach.</summary>
        private int ShortRangeSquares => bands.Bands.Where(b => b.Name == DistanceBand.Short).Select(b => b.MaxSquares).DefaultIfEmpty(4).First();

        private void OfferShelter(Combatant hurt, int amount, WoundType type, Queue<Combatant> shelterers, Action<int> then)
        {
            if (!shelterers.TryDequeue(out var shelterer))
            {
                then(amount);
                return;
            }

            shelterTaking = null;
            OfferReaction(new PendingReaction(ReactionKind.Shelter, shelterer, hurt, null, 0,
                    $"{amount} {type} is about to land on {hurt.Name}. {shelterer.Name} may Shelter them — take any part of it as Soul instead, once this round.",
                    ImpactWounds: amount, DamageType: type),
                takes =>
                {
                    var taking = takes ? Math.Clamp(shelterTaking ?? amount, 1, amount) : 0;
                    shelterTaking = null;
                    if (taking == 0)
                    {
                        OfferShelter(hurt, amount, type, shelterers, then);
                        return;
                    }

                    shelterer.HasShelteredThisRound = true;
                    Write(taking == amount
                        ? $"{shelterer.Name} Shelters {hurt.Name}, and takes the {amount} {type} as Soul."
                        : $"{shelterer.Name} Shelters {hurt.Name}, taking {taking} of the {amount} {type} as Soul.");
                    // Straight onto the shelterer's Soul — what they took for somebody else isn't theirs to pass on.
                    Land(shelterer, WoundType.Soul, taking, 0, () => then(amount - taking));
                });
        }

        /// <summary>How much of the blow the Shelter being answered takes; null for all of it.</summary>
        private int? shelterTaking;

        /// <summary>
        /// Answers a Shelter on offer with how much of the damage to take — "any part of it" (Core Rules
        /// §3): 0 declines it, and the next Soul-secondary ally in reach is asked.
        /// </summary>
        public bool RespondToShelter(int amount)
        {
            if (Pending is not { Kind: ReactionKind.Shelter } pending || amount < 0 || amount > pending.ImpactWounds)
            {
                return false;
            }

            shelterTaking = amount;
            return RespondToReaction(amount > 0);
        }
    }
}
