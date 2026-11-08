using WoundBound.Rules.Enums;

namespace WoundBound.Game.Combat
{
    /// <summary>What kind of thing an adversary has declared it is about to do.</summary>
    public enum IntentKind
    {
        Strike,
        Shot,
        Cast,
        Grab,
        Shove,
        Trip,
        Other,
    }

    /// <summary>
    /// An adversary's declared action, as the table hears it before the dice are rolled (Core Rules
    /// §9, "The GM declares before rolling") — the same choice the GM brain made, so what is shown
    /// can't disagree with what happens.
    /// </summary>
    /// <param name="What">The weapon, the spell, or the hands-on action, by name.</param>
    /// <param name="Target">Who it is aimed at; null for a spell put on the ground.</param>
    /// <param name="Manoeuvre">The manoeuvre declared with a blow, if any.</param>
    /// <param name="Spell">For a cast, what kind of spell it is.</param>
    public sealed record Declaration(
        Combatant Actor, IntentKind Kind, string What, Combatant? Target, WeaponManoeuvre? Manoeuvre = null, SpellType? Spell = null)
    {
        /// <summary>
        /// Why the creature chose it, in a few words the table may hear ("the caster", "already
        /// bloodied"): only what its creatures could have seen, never what the GM read off a sheet
        /// (the user's ruling, 2026-11-08). Null where there's nothing worth saying.
        /// </summary>
        public string? Why { get; init; }
    }
}
