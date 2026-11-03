using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Something that just happened in the fight, as a record the screen can animate rather than a
    /// line it would have to read: a blow or a shot and how it went, a spell worked and where, a
    /// spell's roll against one creature. Raised through <see cref="Encounter.Happened"/> at the
    /// moment the matching line is written to the log, so what animates is what happened.
    /// </summary>
    public abstract record Happening;

    /// <summary>A weapon attack, resolved: who swung or shot at whom, with what, and how it landed.</summary>
    public sealed record Struck(AttackOutcome Outcome) : Happening
    {
        public Combatant Attacker => Outcome.Attacker;

        public Combatant Target => Outcome.Target;

        public AttackProfile With => Outcome.Plan.Profile ?? Outcome.Attacker.Attack;

        public bool Hit => Outcome.Roll.Success == true;

        public bool Critical => Hit && Outcome.Roll.IsCritical;

        /// <summary>Turned aside by a panic dodge or a raised shield.</summary>
        public bool Dodged => Outcome.Dodged && !Hit;

        /// <summary>A dodged critical: it comes back on the attacker (Core Rules §10, "Riposte").</summary>
        public bool Riposte => Dodged && Outcome.Roll.IsCritical;
    }

    /// <summary>A spell worked: the caster, the spell, and the plan it was cast on — its origin, squares and who it caught.</summary>
    public sealed record SpellWorked(Combatant Caster, SpellDefinition Spell, CastPlan Plan) : Happening;

    /// <summary>One creature's share of a Harm spell's rolls: hit, missed, dodged or Riposted.</summary>
    public sealed record SpellStruck(Combatant Caster, Combatant Target, SpellDefinition Spell, bool Hit, bool Critical, bool Dodged, bool Riposte) : Happening;
}
