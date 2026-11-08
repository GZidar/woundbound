namespace WoundBound.Game.Combat
{
    /// <summary>
    /// What a side has seen of its foes, for a brain that plays in the fog (the user's direction,
    /// 2026-11-04). Creatures can't act on an opponent they can't see. The rules already refuse a
    /// blow or a spell aimed at one, but a brain that knows where everybody stands still walks
    /// straight to them. So a fogged side knows only the foes it sees now, and where it last saw
    /// the rest: the last square of their walk it could see, or where a blow, shot or spell of
    /// theirs came from. A remembered square one of its creatures goes to and finds empty is forgotten.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>The sides whose sightings are kept, and for each, the foes it sees now and where it last saw the rest.</summary>
        private readonly Dictionary<Side, Sightings> sightings = new();

        /// <summary>Where each creature stood at the last look, so a walk out of sight can be followed to where it vanished.</summary>
        private readonly Dictionary<Combatant, GridPosition> lookedAt = new();

        private sealed class Sightings
        {
            public HashSet<Combatant> SeenNow { get; } = new();

            public Dictionary<Combatant, GridPosition> LastSeen { get; } = new();
        }

        /// <summary>Starts keeping <paramref name="side"/>'s sightings, from what it sees now.</summary>
        internal void TrackSightings(Side side)
        {
            if (sightings.TryAdd(side, new Sightings()))
            {
                LookAround();
            }
        }

        /// <summary>Whether <paramref name="side"/>, whose sightings are kept, sees <paramref name="foe"/> now.</summary>
        internal bool Sighted(Side side, Combatant foe) => sightings.TryGetValue(side, out var s) && s.SeenNow.Contains(foe);

        /// <summary>The foes <paramref name="side"/> doesn't see now but remembers, and where it last saw each.</summary>
        internal IEnumerable<(Combatant Foe, GridPosition At)> Remembered(Side side) =>
            sightings.TryGetValue(side, out var s)
                ? s.LastSeen.Where(kv => !s.SeenNow.Contains(kv.Key)).Select(kv => (kv.Key, kv.Value))
                : [];

        /// <summary>
        /// Looks again for every side whose sightings are kept. Called before every log line and
        /// every happening, as the moves are, and before the GM is asked anything.
        /// </summary>
        internal void LookAround()
        {
            NoteGoal();
            if (sightings.Count == 0)
            {
                return;
            }

            foreach (var (side, seen) in sightings)
            {
                var viewers = combatants.Where(c => c.Side == side && Looks(c)).ToList();
                bool SquareSeen(GridPosition square) => viewers.Any(v => CanSee(v, square));

                seen.SeenNow.Clear();
                foreach (var foe in combatants.Where(c => c.Side != side && !c.IsPhantom))
                {
                    if (foe.HasFled || !foe.OccupiesSquare)
                    {
                        seen.LastSeen.Remove(foe);
                        continue;
                    }

                    if (viewers.Any(v => CanSee(v, foe)))
                    {
                        seen.SeenNow.Add(foe);
                        seen.LastSeen[foe] = foe.Position;
                        continue;
                    }

                    if (!seen.LastSeen.TryGetValue(foe, out var last))
                    {
                        continue;
                    }

                    // It walked out of sight since the last look: it vanished on the last square of the
                    // way it went that is in sight — or where it set out from, if none is.
                    if (lookedAt.TryGetValue(foe, out var was) && was != foe.Position && was == last)
                    {
                        var path = routes.TryGetValue(foe, out var route) && route.Path.Contains(was) && route.Path.Contains(foe.Position)
                            ? route.Path.SkipWhile(sq => sq != was).ToList()
                            : PathBetween(was, foe.Position);
                        var vanished = path.FindLastIndex(SquareSeen);
                        seen.LastSeen[foe] = vanished >= 0 ? path[vanished] : was;
                        continue;
                    }

                    // Gone to look, and nobody is there: where it went, nobody knows.
                    if (viewers.Any(v => v.Footprint.Any(e => e.DistanceTo(last) <= 1)) && !foe.Footprint.Contains(last))
                    {
                        seen.LastSeen.Remove(foe);
                    }
                }
            }

            foreach (var creature in combatants)
            {
                lookedAt[creature] = creature.Position;
            }
        }

        /// <summary>
        /// <paramref name="source"/> struck at, shot at, or cast on <paramref name="target"/>: its side
        /// knows where that came from, seen or not.
        /// </summary>
        private void GiveAway(Combatant source, Combatant target)
        {
            if (source.Side != target.Side && source.OccupiesSquare && sightings.TryGetValue(target.Side, out var seen))
            {
                seen.LastSeen[source] = source.Position;
            }
        }

        /// <summary>What <paramref name="happening"/> gives away to the side it was done to.</summary>
        private void GiveAway(Happening happening)
        {
            switch (happening)
            {
                case Struck struck:
                    GiveAway(struck.Attacker, struck.Target);
                    break;
                case SpellStruck spell:
                    GiveAway(spell.Caster, spell.Target);
                    break;
                case Handled handled:
                    GiveAway(handled.Actor, handled.Target);
                    break;
            }
        }
    }
}
