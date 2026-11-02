using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using WoundBound.Game.Combat;

namespace WoundBoundGame
{
    /// <summary>
    /// Turns the Encounter's plain log lines into something a player can follow: a header for each
    /// round and each turn, every action an entry of its own with its actor named in their colour,
    /// what followed from it indented beneath, and the rolls and outcomes picked out. It only reads
    /// the words — the Encounter still writes plain sentences, and decides nothing here.
    /// </summary>
    public sealed partial class BattleLog
    {
        private readonly Func<Combatant, Color> colourOf;
        private IReadOnlyList<Combatant> byName = [];

        /// <summary>Whose turn the last line was written in: a character's name, "GM", or null before the first.</summary>
        private string? turn;

        public BattleLog(Func<Combatant, Color> colourOf) => this.colourOf = colourOf;

        private static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
        private static readonly Color Hit = new(0.45f, 0.85f, 0.5f);
        private static readonly Color Miss = new(0.6f, 0.6f, 0.65f);
        private static readonly Color Hurt = new(0.95f, 0.45f, 0.4f);
        private static readonly Color Muted = new(0.62f, 0.62f, 0.68f);
        private static readonly Color GmRed = new(0.85f, 0.35f, 0.3f);

        /// <summary>
        /// One line as BBCode for a RichTextLabel, with any round or turn header it opens.
        /// <paramref name="gmActing"/> is whether the GM was acting when it was written.
        /// </summary>
        public string Format(string line, IReadOnlyList<Combatant> combatants, bool gmActing)
        {
            byName = combatants.OrderByDescending(c => c.Name.Length).ToList();
            var text = Escape(line);

            if (RoundLine().Match(line) is { Success: true } round)
            {
                turn = null;
                return $"\n[center][color=#{Gold.ToHtml(false)}][b]━━  Round {round.Groups[1].Value}  ━━[/b][/color][/center]\n";
            }

            // "Salla Rinehart's turn." — a character's turn begins.
            if (line.EndsWith("'s turn.") && byName.FirstOrDefault(c => line == c.Name + "'s turn.") is { } character)
            {
                turn = character.Name;
                return Header(Name(character) + "'s turn");
            }

            var header = string.Empty;
            if (gmActing && turn != "GM")
            {
                turn = "GM";
                header = Header($"[color=#{GmRed.ToHtml(false)}]The GM[/color]'s turn");
            }

            // A line that starts with somebody doing something is a new entry; anything else follows from the one before.
            var actor = byName.FirstOrDefault(c => line.StartsWith(c.Name + " ") && !line.StartsWith(c.Name + "'s turn"));
            var body = Outcomes(Rolls(Names(text)));
            if (actor is not null && !IsConsequence(line, actor))
            {
                return header + $"[font_size=4] [/font_size]\n{body}\n";
            }

            return header + $"[indent][color=#{Muted.ToHtml(false)}]▸[/color] {body}[/indent]\n";
        }

        /// <summary>A line that names its subject first but reports what happened to them, not what they did.</summary>
        private static bool IsConsequence(string line, Combatant subject)
        {
            var rest = line[(subject.Name.Length + 1)..];
            return rest.StartsWith("is ") || rest.StartsWith("has ") || rest.StartsWith("takes ") && !rest.StartsWith("takes the ")
                   || rest.StartsWith("falls") || rest.StartsWith("drops") || rest.StartsWith("rolls with") || rest.StartsWith("rolls Harried")
                   || rest.StartsWith("looks again") || rest.StartsWith("lowers") || rest.StartsWith("regains") || rest.StartsWith("is no longer");
        }

        private static string Header(string title) =>
            $"[bgcolor=#ffffff12][b] {title} [/b][/bgcolor]\n";

        private string Name(Combatant c) => $"[b][color=#{colourOf(c).Lightened(0.25f).ToHtml(false)}]{Escape(c.Name)}[/color][/b]";

        /// <summary>Every combatant's name in the line, in their colour.</summary>
        private string Names(string text)
        {
            // Longest first, each name swapped for a placeholder so a shorter one inside it isn't coloured twice.
            var marks = new List<string>();
            foreach (var c in byName)
            {
                var escaped = Escape(c.Name);
                if (text.Contains(escaped, StringComparison.Ordinal))
                {
                    text = text.Replace(escaped, $"\u0001{marks.Count}\u0002");
                    marks.Add(Name(c));
                }
            }

            for (var i = 0; i < marks.Count; i++)
            {
                text = text.Replace($"\u0001{i}\u0002", marks[i]);
            }

            return text;
        }

        /// <summary>"(7+10)+4 = 21" — the dice dimmed, the total bold.</summary>
        private static string Rolls(string text) =>
            RollPattern().Replace(text, m => $"[color=#{Muted.ToHtml(false)}]{m.Groups[1].Value}[/color][b]{m.Groups[2].Value}[/b]");

        /// <summary>Hits, misses, wounds and the fallen, each in its colour.</summary>
        private static string Outcomes(string text)
        {
            text = OutcomeWords().Replace(text, m =>
            {
                var word = m.Value;
                var colour = word.StartsWith("miss", StringComparison.OrdinalIgnoreCase) ? Miss
                    : word.Contains("double one", StringComparison.OrdinalIgnoreCase) || word.Contains("out of the fight") || word.Contains("dead") ? Hurt
                    : word.Contains("Victory") ? Gold
                    : Hit;
                return $"[color=#{colour.ToHtml(false)}][b]{word}[/b][/color]";
            });
            return Wounds().Replace(text, m => $"[color=#{Hurt.ToHtml(false)}]{m.Value}[/color]");
        }

        /// <summary>The log's own square brackets, kept from being read as BBCode.</summary>
        private static string Escape(string text) => text.Replace("[", "[lb]");

        [GeneratedRegex(@"^\W*Round (\d+)\W*$")]
        private static partial Regex RoundLine();

        [GeneratedRegex(@"(\(\d+(?:\+\d+)*\)[+−-]\d+ = )(\d+)")]
        private static partial Regex RollPattern();

        [GeneratedRegex(@"critical hit!|double one!|\bhit\b|\bhits\b|\bmiss(?:es)?\b|out of the fight|is dead|Victory!", RegexOptions.IgnoreCase)]
        private static partial Regex OutcomeWords();

        [GeneratedRegex(@"\b\d+ (?:Body|Mind|Soul)\b(?!/)")]
        private static partial Regex Wounds();
    }
}
