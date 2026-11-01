namespace WoundBound.Game.Combat.Brains
{
    /// <summary>What a party bot did over one fight, for the simulator to add up.</summary>
    public sealed class BotTally
    {
        public int Moves { get; internal set; }

        public int Attacks { get; internal set; }

        public int Swaps { get; internal set; }

        public int ExtraActions { get; internal set; }

        public int StoodGround { get; internal set; }

        public int DodgesOffered { get; internal set; }

        public int DodgesTaken { get; internal set; }

        /// <summary>Grabs, Shoves and Trips.</summary>
        public int Manoeuvres { get; internal set; }

        public int StandUps { get; internal set; }

        /// <summary>Stood firm or arrested a shove.</summary>
        public int OtherReactions { get; internal set; }

        /// <summary>Struck at, or reached for, an enemy leaving reach.</summary>
        public int LeaverReactions { get; internal set; }
    }

    /// <summary>
    /// Plays a fight with nobody at the controls: a <see cref="IPartyBrain"/> takes the player's
    /// seat, and the encounter's own <see cref="IGmBrain"/> keeps the GM's. Every party choice goes
    /// through the same public commands the battle screen's buttons call, so a bot gets exactly the
    /// rules a player does.
    /// </summary>
    public sealed class BotDriver
    {
        private readonly Encounter encounter;
        private readonly IPartyBrain party;

        public BotDriver(Encounter encounter, IPartyBrain party)
        {
            this.encounter = encounter;
            this.party = party;
        }

        public BotTally Tally { get; } = new();

        /// <summary>
        /// Makes the next decision, whoever's it is — one party step, one answer to a reaction
        /// prompt, or one adversary turn. Returns false once the fight is over. A step the rules
        /// refuse throws, naming the brain and the refusal.
        /// </summary>
        public bool Step()
        {
            switch (encounter.Phase)
            {
                case EncounterPhase.ChoosingCharacter:
                    var next = party.ChooseNextCharacter(encounter.View, encounter.PartyStillToAct);
                    if (!encounter.TryBeginTurn(next))
                    {
                        throw Refused($"{next.Name} to act next", "they aren't waiting for a turn this round");
                    }

                    return true;

                case EncounterPhase.PartyTurn when encounter.Active is { } character:
                    TakePartyStep(character);
                    return true;

                case EncounterPhase.AwaitingReaction when encounter.Pending is { Kind: ReactionKind.LeaverReaction } leaving:
                    var response = party.ReactsToLeaver(encounter.View, leaving);
                    Tally.LeaverReactions += response == LeaverResponse.LetThemGo ? 0 : 1;
                    if (!encounter.RespondToLeaver(response))
                    {
                        throw Refused($"{response} for {leaving.Defender.Name}", $"the options were {string.Join(", ", leaving.Options ?? [])}");
                    }

                    return true;

                case EncounterPhase.AwaitingReaction when encounter.Pending is { } pending:
                    var spend = pending.Kind switch
                    {
                        ReactionKind.PanicDodge => party.PanicDodges(encounter.View, pending),
                        ReactionKind.StandFirm => party.StandsFirm(encounter.View, pending),
                        _ => party.ArrestsShove(encounter.View, pending),
                    };

                    if (pending.Kind == ReactionKind.PanicDodge)
                    {
                        Tally.DodgesOffered++;
                        Tally.DodgesTaken += spend ? 1 : 0;
                    }
                    else
                    {
                        Tally.OtherReactions += spend ? 1 : 0;
                    }

                    if (!encounter.RespondToReaction(spend))
                    {
                        throw Refused($"a {pending.Kind} for {pending.Defender.Name}", Encounter.TacticalSpendRefusal(pending.Defender));
                    }

                    return true;

                case EncounterPhase.AdversaryTurn:
                    encounter.RunAdversaryTurn();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Plays until somebody wins, or until <paramref name="maxRounds"/> have gone by — a fight
        /// still going then is a stalemate, which is worth knowing about too.
        /// </summary>
        public void PlayToEnd(int maxRounds = 50)
        {
            while (encounter.Round <= maxRounds && Step())
            {
            }
        }

        private void TakePartyStep(Combatant character)
        {
            var step = party.NextStep(encounter.View, character);
            var done = step switch
            {
                PartyStep.Move move => Count(encounter.TryMove(move.Square), () => Tally.Moves++),
                PartyStep.Attack attack => Count(encounter.TryAttack(attack.Target) is not null, () => Tally.Attacks++),
                PartyStep.Swap => Count(encounter.TrySwapLoadout(), () => Tally.Swaps++),
                PartyStep.BuyExtraAction => Count(encounter.TryBuyExtraAction(), () => Tally.ExtraActions++),
                PartyStep.StandGround brace => Count(encounter.TryStandGround(brace.Action), () => Tally.StoodGround++),
                PartyStep.Control control => Count(encounter.TryControl(control.Action, control.Target), () => Tally.Manoeuvres++),
                PartyStep.StandUp => Count(encounter.TryStandUp(), () => Tally.StandUps++),
                PartyStep.BreakFree => encounter.TryBreakFree(),
                PartyStep.LetGo => encounter.TryLetGo(),
                _ => EndTurn(),
            };

            if (!done)
            {
                throw Refused($"{step} for {character.Name}", step switch
                {
                    PartyStep.Swap => Encounter.SwapRefusal(character),
                    PartyStep.BuyExtraAction => encounter.ExtraActionRefusal(character),
                    PartyStep.StandGround brace => Encounter.StandGroundRefusal(character, brace.Action),
                    PartyStep.Control control => Encounter.ControlRefusal(character, control.Action, control.Target),
                    PartyStep.StandUp => Encounter.StandUpRefusal(character),
                    PartyStep.BreakFree => Encounter.BreakFreeRefusal(character),
                    PartyStep.LetGo => "they're holding nobody",
                    PartyStep.Attack attack => encounter.PlanAttack(character, attack.Target).Refusal ?? "no action left",
                    _ => null,
                });
            }
        }

        private bool EndTurn()
        {
            encounter.EndTurn();
            return true;
        }

        private static bool Count(bool done, Action tally)
        {
            if (done)
            {
                tally();
            }

            return done;
        }

        private InvalidOperationException Refused(string what, string? why) =>
            new($"The party brain \"{party.Name}\" chose {what}, which the rules refuse{(why is null ? "." : $": {why}")}");
    }
}
