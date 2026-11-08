using WoundBound.Game.Data;
using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// What a scene's foes cost against its party, read off GM Reference §5: each creature's Threat
    /// Value by Role (discounted below the party's Tier), summed and set against the party's budget
    /// at each difficulty. A readout for whoever builds the scene, not a rule the fight obeys.
    /// </summary>
    public sealed record SceneThreat(double Spend, int PartySize, int Foes, bool HasSolo, ChallengeDifficulty? Difficulty, IReadOnlyList<string> Notes)
    {
        /// <summary>The party's Tier: the Quickstart characters are all T1.</summary>
        public const Tier PartyTier = Tier.T1;

        /// <summary>The budget at <paramref name="difficulty"/> for this party.</summary>
        public double Budget(ChallengeDifficulty difficulty) => ChallengeCalculator.Budget(PartySize, difficulty);

        /// <summary>Prices <paramref name="scene"/>'s foes (the clearing's band if it names none) against its party (the Quickstart five if it names none).</summary>
        public static SceneThreat Of(GameData data, SceneMap scene)
        {
            var party = scene.Party.Where(SampleEncounters.QuickstartParty.ContainsKey).Distinct().Count();
            if (party == 0)
            {
                party = 5;
            }

            var groups = scene.Foes.Count > 0
                ? scene.Foes.Select(g => (data.Adversaries.FirstOrDefault(a => a.Name == g.Adversary), Math.Clamp(g.Count, 0, 20)))
                : [(data.Adversary("Reedmarsh Skulker"), SampleEncounters.DefaultClearingBand.Skulkers),
                   (data.Adversary("Timber Wolf"), SampleEncounters.DefaultClearingBand.Wolves),
                   (data.Adversary("Bramble Ratkin"), SampleEncounters.DefaultClearingBand.Ratkin)];
            var known = groups.Where(g => g.Item1 is { Kind: AdversaryKind.Combat } && g.Item2 > 0).Select(g => (Info: g.Item1!, Count: g.Item2)).ToList();

            var notes = new List<string>();
            var solo = known.Any(g => g.Info.Role == AdversaryRole.Solo);
            var spend = known.Where(g => g.Info.Role != AdversaryRole.Solo)
                .Sum(g => g.Count * ChallengeCalculator.ThreatValue(g.Info.Role, g.Info.Tier, PartyTier));
            var foes = known.Sum(g => g.Count);

            if (solo)
            {
                notes.Add("A Solo spends the whole budget by itself.");
            }

            if (known.Any(g => g.Info.Role == AdversaryRole.Swarm))
            {
                notes.Add("A Swarm's 1.5 is a midpoint: less in the open, more in a corridor.");
            }

            var casters = known.Where(g => g.Info.Role is AdversaryRole.Adept or AdversaryRole.Controller or AdversaryRole.Support)
                .GroupBy(g => g.Info.Role).Where(r => r.Sum(g => g.Count) > 1).Select(r => r.Key).ToList();
            if (casters.Count > 0)
            {
                notes.Add($"More than one {string.Join("/", casters)}: each one after the first is worth about half.");
            }

            if (foes > party)
            {
                notes.Add($"{foes} foes but only {party} GM turns a round: the rest buy a longer fight, not a sharper one.");
            }

            var difficulty = solo ? ChallengeDifficulty.Standard : ChallengeCalculator.EffectiveDifficulty(spend, party, ChallengeDifficulty.Standard);
            return new SceneThreat(spend, party, foes, solo, difficulty, notes);
        }

        /// <summary>One line: "Threat 4.5 of a 5-strong party: Standard (Easy 3.75 · Standard 5 · Hard 7.5 · Deadly 10)".</summary>
        public string Summary()
        {
            var bands = string.Join(" · ", Enum.GetValues<ChallengeDifficulty>().Select(d => $"{d} {Budget(d):0.##}"));
            var verdict = HasSolo ? "a Solo fight" : Difficulty is { } d ? d.ToString() : "beyond Deadly";
            return $"Threat {Spend:0.##} against {PartySize} characters: {verdict}\n({bands})";
        }
    }
}
