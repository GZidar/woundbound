using System.Text.RegularExpressions;

namespace WoundBound.Game.Combat
{
    /// <summary>What an Opportunity bonus does, once the GM spends one on a creature instead of an extra turn.</summary>
    public enum OpportunityBonusKind
    {
        /// <summary>Advantage on its next roll: the default (Core Rules §9).</summary>
        Advantage,

        /// <summary>Its next blow's damage is Direct: armour absorbs none of it.</summary>
        Direct,

        /// <summary>Its next blow is Precise: Direct, with its Tier's baseline added to the damage (a statblock has no Vitality to add).</summary>
        Precise,

        /// <summary>Its next blow may Cleave on into a second foe without the Stamina a Cleave costs.</summary>
        Cleave,

        /// <summary>Another move this turn, after the one it had.</summary>
        ExtraMove,

        /// <summary>+2 to a Threshold of its choice until the start of its next turn.</summary>
        ThresholdBoost,

        /// <summary>A Buff cast on an ally for no wound cost.</summary>
        FreeBuff,

        /// <summary>A Solo's second attack this round, once a round, bought with the Opportunity rather than Stamina; Advantage otherwise.</summary>
        ExtraAttack,
    }

    /// <summary>
    /// A creature's Opportunity bonus, read from its statblock's Opportunity row (GM Reference §9, "On
    /// Opportunity"): "The bonus is Advantage unless the statblock names something else - Cleave,
    /// Precise, Direct damage, a Buff for an ally, extra movement, a Threshold boost." Each is spent
    /// on the creature's next action, in place of an extra turn.
    /// </summary>
    public sealed record OpportunityBonus(OpportunityBonusKind Kind, string Text)
    {
        public static OpportunityBonus Default { get; } = new(OpportunityBonusKind.Advantage, "Advantage");

        /// <summary>Whether the bonus is spent on a blow — taken with the attack it sharpens.</summary>
        public bool RidesOnAttack => Kind is OpportunityBonusKind.Advantage or OpportunityBonusKind.Direct or OpportunityBonusKind.Precise
            or OpportunityBonusKind.Cleave or OpportunityBonusKind.ExtraAttack;

        /// <summary>Whether a Grab, Shove or Trip can take it: only Advantage is any use to one.</summary>
        public bool RidesOnControl => Kind is OpportunityBonusKind.Advantage or OpportunityBonusKind.ExtraAttack;

        /// <summary>
        /// Reads a statblock's Opportunity row. Free text in the rules model, so the words are looked
        /// for in the order a sharper bonus would name them; anything unrecognised is the default.
        /// </summary>
        public static OpportunityBonus Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Default;
            }

            bool Has(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
            var kind = Has(@"\bextra attack\b") ? OpportunityBonusKind.ExtraAttack
                : Has(@"\bbuff\b") ? OpportunityBonusKind.FreeBuff
                : Has(@"\bcleave\b") ? OpportunityBonusKind.Cleave
                : Has(@"\bprecise\b") ? OpportunityBonusKind.Precise
                : Has(@"\bdirect\b") ? OpportunityBonusKind.Direct
                : Has(@"\b(extra|more|further) (movement|move)\b") ? OpportunityBonusKind.ExtraMove
                : Has(@"\bthreshold boost\b|\bboost\b.*\bthreshold\b") ? OpportunityBonusKind.ThresholdBoost
                : OpportunityBonusKind.Advantage;
            return new OpportunityBonus(kind, text.Trim());
        }
    }
}
