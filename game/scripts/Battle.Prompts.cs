using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using Side = WoundBound.Game.Combat.Side;

namespace WoundBoundGame
{
    /// <summary>
    /// The rings the game opens by itself, when the moment calls for a player's choice: a reaction
    /// offered mid-blow (a panic dodge, a shield raised in time, an Interrupt, Shelter...) around the
    /// one being asked; who goes next, when the players choose; and a character's actions as their turn
    /// begins. Each stays only as long as its moment lasts.
    /// </summary>
    public partial class Battle
    {
        private enum RadialPurpose
        {
            Actions,
            Reaction,
            Choosing,
        }

        private RadialPurpose radialPurpose;

        /// <summary>The reaction a ring was last opened for, so it isn't reopened on every redraw once closed.</summary>
        private PendingReaction? promptedReaction;

        /// <summary>Who could be chosen when the chooser last opened — reopened only when that changes.</summary>
        private string promptedChoice = string.Empty;

        /// <summary>The turn whose actions last opened by themselves.</summary>
        private (int Round, Combatant? Who, int Turns) promptedTurn;

        /// <summary>
        /// After every redraw: closes a ring whose moment has passed, refreshes an actions ring, and
        /// opens whatever ring the moment now calls for.
        /// </summary>
        private void UpdatePrompts()
        {
            if (radial.IsOpen)
            {
                var stillWanted = bot is null && radialPurpose switch
                {
                    RadialPurpose.Actions => encounter.Phase == EncounterPhase.PartyTurn,
                    RadialPurpose.Reaction => encounter.Pending is not null && ReferenceEquals(encounter.Pending, promptedReaction),
                    _ => encounter.Phase == EncounterPhase.ChoosingCharacter,
                };

                if (!stillWanted)
                {
                    radial.CloseAll();
                }
                else if (radialPurpose == RadialPurpose.Actions)
                {
                    radial.Reload();
                }
            }

            if (bot is not null || demo)
            {
                return;
            }

            if (encounter.Pending is { } pending && pending.Defender.Side == Side.Party)
            {
                if (!ReferenceEquals(pending, promptedReaction))
                {
                    promptedReaction = pending;
                    OpenReaction(pending);
                }

                return;
            }

            promptedReaction = null;
            if (encounter.Phase == EncounterPhase.ChoosingCharacter)
            {
                var key = $"{encounter.Round}|{string.Join("|", encounter.PartyStillToAct.Select(c => c.Name))}";
                if (key != promptedChoice)
                {
                    promptedChoice = key;
                    OpenChooser();
                }

                return;
            }

            // A turn just begun — chosen, or won by an Interrupt in the middle of the GM's — opens its actions.
            if (encounter.Phase == EncounterPhase.PartyTurn && encounter.Active is { } acting)
            {
                var turn = (encounter.Round, (Combatant?)acting, acting.TurnsTakenThisRound);
                if (turn != promptedTurn)
                {
                    promptedTurn = turn;
                    OpenActions();
                }

                // The Attack action of a Two Weapons loadout leaves the off-hand strike to take:
                // straight back to the Attack ring, once, to offer it.
                if (!acting.OffHandOwed)
                {
                    offHandPrompted = false;
                }
                else if (!offHandPrompted && !radial.IsOpen)
                {
                    offHandPrompted = true;
                    OpenActions();
                    radial.Follow("Attack");
                }
            }
        }

        /// <summary>Whether the Attack ring has reopened for the off-hand strike now owed.</summary>
        private bool offHandPrompted;

        /// <summary>
        /// The reaction on offer, in a ring round whoever is asked: what they could spend, and letting
        /// the moment pass. For a foe leaving their reach, each thing they may do about it.
        /// </summary>
        private void OpenReaction(PendingReaction pending)
        {
            var hint = reactionExplained;
            List<RadialItem> Items()
            {
                if (pending.Kind == ReactionKind.LeaverReaction)
                {
                    return (pending.Options ?? [])
                        .OrderBy(o => o == LeaverResponse.LetThemGo)
                        .Select(option => new RadialItem(option == LeaverResponse.LetThemGo ? "Let them go" : $"{option} (1 Stamina)", null, hint)
                        {
                            Icon = option == LeaverResponse.LetThemGo ? "let-pass" : option.ToString().ToLowerInvariant(),
                            Pick = () =>
                            {
                                encounter.RespondToLeaver(option);
                                Refresh();
                            },
                        })
                        .ToList();
                }

                var (spend, pass) = ReactionChoices(pending);
                return
                [
                    new RadialItem(spend, null, hint)
                    {
                        Icon = ReactionIcon(pending.Kind),
                        Pick = () => Respond(true),
                    },
                    new RadialItem(pass, null, hint)
                    {
                        Icon = "let-pass",
                        Pick = () => Respond(false),
                    },
                ];
            }

            radialPurpose = RadialPurpose.Reaction;
            var centre = GetGlobalTransformWithCanvas() * SquareCentreOf(pending.Defender);
            radial.Open(centre, pending.Defender.Name.Split(' ').First(w => w != "The"), Items);
        }

        private void Respond(bool spend)
        {
            encounter.RespondToReaction(spend);
            Refresh();
        }

        /// <summary>What the spend and the letting-pass are called, for a reaction of this kind.</summary>
        private static (string Spend, string Pass) ReactionChoices(PendingReaction pending) => pending.Kind switch
        {
            ReactionKind.PanicDodge => ("Panic dodge (1 Stamina)", "Take the hit"),
            ReactionKind.StandFirm => ("Stand firm (1 Stamina, +2 ST)", "Let it happen"),
            ReactionKind.RaiseShield => ($"Raise shield (1 Stamina, +{pending.Bonus})", "Take the hit"),
            ReactionKind.CoverAlly => ($"Shield {pending.Struck.Name} (1 Stamina, +{pending.Bonus})", "Let it land"),
            ReactionKind.Backlash => ($"Backlash ({pending.ImpactWounds} Body)", "Drop to 0"),
            ReactionKind.Sustain => ("Hold it (1 Stamina)", "Let it go"),
            ReactionKind.BraceResolve => ("Steel yourself (1 Stamina, +2)", "Let it take hold"),
            ReactionKind.Interrupt => ("Interrupt (1 Mind)", "Let it happen"),
            ReactionKind.Shelter => ($"Shelter them ({pending.ImpactWounds} Soul)", "Let them take it"),
            _ => ("Arrest (1 Stamina)", "Let it carry them"),
        };

        private static string ReactionIcon(ReactionKind kind) => kind switch
        {
            ReactionKind.PanicDodge => "dodge",
            ReactionKind.RaiseShield or ReactionKind.CoverAlly => "raise-shield",
            ReactionKind.Backlash => "backlash",
            ReactionKind.Sustain => "sustain",
            ReactionKind.BraceResolve => "resolute",
            ReactionKind.Interrupt => "interrupt",
            ReactionKind.Shelter => "shelter",
            _ => "grounded", // standing firm, and arresting a shove: feet planted
        };

        /// <summary>
        /// Who goes next, from the characters still to act this round, in a ring among them. Choosing
        /// one begins their turn; a click on a token still does the same.
        /// </summary>
        private void OpenChooser()
        {
            var candidates = encounter.PartyStillToAct.ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            IReadOnlyList<RadialItem> Items() => encounter.PartyStillToAct
                .Select(c => new RadialItem(c.Name, null,
                    $"Body {c.Body}/{c.BodyMax}, Mind {c.Mind}/{c.MindMax}, Soul {c.Soul}/{c.SoulMax}, Stamina {c.Stamina}/{c.StaminaMax}.\nIn hand: {c.Attack.Name}.")
                {
                    Pick = () =>
                    {
                        encounter.TryBeginTurn(c);
                        Refresh();
                    },
                })
                .ToList();

            // In the middle of those still to act.
            var middle = candidates.Select(c => SquareCentreOf(c)).Aggregate(Vector2.Zero, (sum, at) => sum + at) / candidates.Count;
            radialPurpose = RadialPurpose.Choosing;
            radial.Open(GetGlobalTransformWithCanvas() * middle, "Who's next?", Items);
        }
    }
}
