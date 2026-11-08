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

        public int OffHandStrikes { get; internal set; }

        public int StandUps { get; internal set; }

        /// <summary>Helps and Harries.</summary>
        public int Aids { get; internal set; }

        /// <summary>Heal, Meditate and Pray.</summary>
        public int Tends { get; internal set; }

        /// <summary>Shields raised with the action.</summary>
        public int ShieldsRaised { get; internal set; }

        /// <summary>Stood firm or arrested a shove.</summary>
        public int OtherReactions { get; internal set; }

        /// <summary>Struck at, or reached for, an enemy leaving reach.</summary>
        public int LeaverReactions { get; internal set; }

        /// <summary>Nimble Exploits bought.</summary>
        public int Exploits { get; internal set; }

        /// <summary>Ducks back into full cover after attacking out of it.</summary>
        public int DuckBacks { get; internal set; }

        /// <summary>Interrupts taken.</summary>
        public int Interrupts { get; internal set; }

        /// <summary>Shelters given.</summary>
        public int Shelters { get; internal set; }

        /// <summary>Wounds taken as Soul through Shelters.</summary>
        public int ShelteredWounds { get; internal set; }

        /// <summary>Spells cast.</summary>
        public int Casts { get; internal set; }

        /// <summary>Sacrifices made, for Advantage on an attack or a cast.</summary>
        public int Sacrifices { get; internal set; }
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

                case EncounterPhase.AwaitingReaction when encounter.AwaitingNarration:
                    encounter.ContinueNarration(); // a bot table has nobody to narrate to
                    return true;

                case EncounterPhase.AwaitingReaction when encounter.Pending is { Kind: ReactionKind.LeaverReaction } leaving:
                    var response = party.ReactsToLeaver(encounter.View, leaving);
                    Tally.LeaverReactions += response == LeaverResponse.LetThemGo ? 0 : 1;
                    if (!encounter.RespondToLeaver(response))
                    {
                        throw Refused($"{response} for {leaving.Defender.Name}", $"the options were {string.Join(", ", leaving.Options ?? [])}");
                    }

                    return true;

                case EncounterPhase.AwaitingReaction when encounter.Pending is { Kind: ReactionKind.Shelter } shelter:
                    var taking = Math.Clamp(party.ShelterAmount(encounter.View, shelter), 0, shelter.ImpactWounds);
                    Tally.Shelters += taking > 0 ? 1 : 0;
                    Tally.ShelteredWounds += taking;
                    if (!encounter.RespondToShelter(taking))
                    {
                        throw Refused($"a Shelter for {shelter.Defender.Name}", null);
                    }

                    return true;

                case EncounterPhase.AwaitingReaction when encounter.Pending is { } pending:
                    var spend = pending.Kind switch
                    {
                        ReactionKind.PanicDodge or ReactionKind.RaiseShield or ReactionKind.CoverAlly => party.PanicDodges(encounter.View, pending),
                        ReactionKind.StandFirm or ReactionKind.BraceResolve => party.StandsFirm(encounter.View, pending),
                        ReactionKind.Backlash => party.Backlashes(encounter.View, pending),
                        ReactionKind.Sustain => party.Sustains(encounter.View, pending),
                        ReactionKind.Interrupt => party.Interrupts(encounter.View, pending),
                        ReactionKind.Shelter => party.Shelters(encounter.View, pending),
                        _ => party.ArrestsShove(encounter.View, pending),
                    };

                    Tally.Interrupts += pending.Kind == ReactionKind.Interrupt && spend ? 1 : 0;
                    Tally.Shelters += pending.Kind == ReactionKind.Shelter && spend ? 1 : 0;
                    if (pending.Kind is ReactionKind.PanicDodge or ReactionKind.RaiseShield)
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
                PartyStep.Attack attack => Count(encounter.TryAttack(attack.Target, attack.Sacrifice, attack.Manoeuvre) is not null || Answered(), () =>
                {
                    Tally.Attacks++;
                    Tally.Sacrifices += attack.Sacrifice ? 1 : 0;
                    Tally.Manoeuvres += attack.Manoeuvre is null ? 0 : 1;
                }),
                PartyStep.Mount => encounter.TryMount(),
                PartyStep.Dismount => encounter.TryDismount(),
                PartyStep.Throw toss => Count(encounter.TryThrow(toss.Target) is not null || Answered(), () => Tally.Attacks++),
                PartyStep.DrawWeapon draw => encounter.TryDrawWeapon(draw.Weapon),
                PartyStep.PickUpWeapon pick => encounter.TryPickUpWeapon(pick.Lying),
                PartyStep.BreakCover breaking => encounter.TryBreakCover(breaking.Square),
                PartyStep.OffHand offHand => Count(encounter.TryOffHandAttack(offHand.Target, offHand.Manoeuvre) is not null, () =>
                {
                    Tally.OffHandStrikes++;
                    Tally.Manoeuvres += offHand.Manoeuvre is null ? 0 : 1;
                }),
                PartyStep.Exploit exploit => Count(encounter.TryExploit(exploit.Which), () => Tally.Exploits++),
                PartyStep.DuckBack => Count(encounter.TryDuckBack(), () => Tally.DuckBacks++),
                PartyStep.Cast cast => Count(encounter.TryCast(cast.Spell), () =>
                {
                    Tally.Casts++;
                    Tally.Sacrifices += cast.Spell.Sacrifice ? 1 : 0;
                }),
                PartyStep.Swap => Count(encounter.TrySwapLoadout(), () => Tally.Swaps++),
                PartyStep.BuyExtraAction => Count(encounter.TryBuyExtraAction(), () => Tally.ExtraActions++),
                PartyStep.StandGround brace => Count(encounter.TryStandGround(brace.Action), () => Tally.StoodGround++),
                PartyStep.Control control => Count(encounter.TryControl(control.Action, control.Target), () => Tally.Manoeuvres++),
                PartyStep.StandUp => Count(encounter.TryStandUp(), () => Tally.StandUps++),
                PartyStep.BreakFree => encounter.TryBreakFree(),
                PartyStep.TakeUpWeapon => encounter.TryTakeUpWeapon(),
                PartyStep.LetGo => encounter.TryLetGo(),
                PartyStep.RegainFooting => encounter.TryRegainFooting(),
                PartyStep.Aid aid => Count(encounter.TryAid(aid.Action, aid.Target), () => Tally.Aids++),
                PartyStep.Tend tend => Count(encounter.TryTend(tend.Action, tend.Target), () => Tally.Tends++),
                PartyStep.RaiseShield raise => Count(encounter.TryRaiseShield(raise.Ally), () => Tally.ShieldsRaised++),
                PartyStep.Drag drag => encounter.TryDrag(drag.Square),
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
                    PartyStep.TakeUpWeapon => Encounter.TakeUpRefusal(character),
                    PartyStep.LetGo => "they're holding nobody",
                    PartyStep.RegainFooting => Encounter.RegainFootingRefusal(character),
                    PartyStep.Aid aid => Encounter.AidRefusal(character, aid.Action, aid.Target),
                    PartyStep.Tend tend => Encounter.TendRefusal(character, tend.Action, tend.Target),
                    PartyStep.RaiseShield raise => Encounter.ShieldRefusal(character, raise.Ally),
                    PartyStep.Drag => "they can't drag there",
                    PartyStep.Attack attack => encounter.PlanAttack(character, attack.Target).Refusal
                        ?? (attack.Sacrifice ? Encounter.SacrificeRefusal(character) : null)
                        ?? (attack.Manoeuvre is { } m ? Encounter.ManoeuvreRefusal(character, attack.Target, m) : null) ?? "no action left",
                    PartyStep.Mount => encounter.MountRefusal(character),
                    PartyStep.Dismount => Encounter.DismountRefusal(character),
                    PartyStep.Throw toss => encounter.ThrowRefusal(character, toss.Target),
                    PartyStep.DrawWeapon draw => Encounter.DrawRefusal(character, draw.Weapon),
                    PartyStep.PickUpWeapon pick => encounter.PickUpRefusal(character, pick.Lying),
                    PartyStep.BreakCover breaking => encounter.BreakCoverRefusal(character, breaking.Square),
                    PartyStep.OffHand offHand => encounter.OffHandRefusal(character, offHand.Target)
                        ?? (offHand.Manoeuvre is { } om ? Encounter.ManoeuvreRefusal(character, offHand.Target, om, offHand: true) : null),
                    PartyStep.Exploit exploit => Encounter.ExploitRefusal(character, exploit.Which),
                    PartyStep.DuckBack => Encounter.DuckBackRefusal(character),
                    PartyStep.Cast cast => encounter.PlanCast(character, cast.Spell).Refusal,
                    _ => null,
                });
            }
        }

        /// <summary>
        /// A declared action that is waiting on somebody's answer — a reaction in the rival's
        /// Interrupt it drew — or that the Interrupt left with nothing to do: accepted, not refused.
        /// </summary>
        private bool Answered() => encounter.Pending is not null || encounter.Phase != EncounterPhase.PartyTurn || encounter.LastDeclarationSpent;

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
