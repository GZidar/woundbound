using WoundBound.Rules.Calculation;
using WoundBound.Rules.Enums;
using WoundBound.Rules.Models;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Elemental Fuel (Core Rules §14): what the place offers of an element, the sources a caster
    /// can tap — spent whole, and a light gone with them — and the lantern that can be refilled.
    /// </summary>
    public sealed partial class Encounter
    {
        /// <summary>Built fires tapped for a spell, burned down to nothing: no more Fuel and no more light.</summary>
        private readonly HashSet<GridPosition> spentFires = new();

        /// <summary>The built fires still burning.</summary>
        public IEnumerable<GridPosition> Fires => Setting.Fires.Where(f => !spentFires.Contains(f));

        /// <summary>
        /// How much of <paramref name="element"/> the place offers someone on <paramref name="square"/>:
        /// what the scene says — a gale, bedrock underground, a building alight — and for Water,
        /// standing in it (Abundant) or within reach of it (Present).
        /// </summary>
        public AmbientPresence AmbientAt(Element element, GridPosition square)
        {
            var scene = Setting.Elements.TryGetValue(element, out var said) ? said : AmbientPresence.None;
            if (element == Element.Water)
            {
                var nearby = Setting.Water.ContainsKey(square) ? AmbientPresence.Abundant
                    : SquaresAround(square, 1).Any(Setting.Water.ContainsKey) ? AmbientPresence.Present
                    : AmbientPresence.None;
                return (AmbientPresence)Math.Max((int)scene, (int)nearby);
            }

            return scene;
        }

        /// <summary>
        /// Why the source <paramref name="cast"/> taps isn't there to be tapped, or null if it is: a
        /// lit torch or lantern in the caster's hand, a torch burning on the ground beside them, or a
        /// built fire within reach of a hand. An unlit light is not fire (Core Rules §14).
        /// </summary>
        private string? FuelRefusal(Combatant caster, GridPosition from, Element element, SpellCast cast)
        {
            if (cast.Tap == FuelSource.None)
            {
                return null;
            }

            if (InventoryCalculator.ElementOf(cast.Tap) != element)
            {
                return $"A {InventoryCalculator.SourceLabel(cast.Tap)} is no source of {element}.";
            }

            switch (cast.Tap)
            {
                case FuelSource.Torch when cast.TapAt is { } at:
                    return from.DistanceTo(at) <= 1 && lightsOnTheGround.Contains(at) ? null : "There's no torch burning on the ground within reach.";
                case FuelSource.Torch or FuelSource.Lantern:
                    return HeldLight(caster, cast.Tap) is null ? $"{caster.Name} isn't holding a lit {(cast.Tap == FuelSource.Torch ? "torch" : "lantern")}." : null;
                case FuelSource.Campfire:
                    return cast.TapAt is { } fire && from.DistanceTo(fire) <= 1 && Fires.Contains(fire) ? null : "There's no built fire within reach.";
                default:
                    return $"There's no {InventoryCalculator.SourceLabel(cast.Tap)} here to draw on.";
            }
        }

        /// <summary>
        /// What tapping the source <paramref name="cast"/> names would do to who can be seen: the
        /// grades of Visibility lost — Good to Poor is one, Poor to None another — summed over the
        /// caster's enemies and over their own side. Worked out by putting the light out, looking,
        /// and lighting it again; nothing is spent.
        /// </summary>
        internal (int Theirs, int Ours) GradesLostIfTapped(Combatant caster, SpellCast cast)
        {
            if (environment is null || Setting.Visibility == Visibility.Good || cast.Tap == FuelSource.None)
            {
                return (0, 0);
            }

            var standing = combatants.Where(c => c.OccupiesSquare && !c.IsOutOfFight).ToList();
            var before = standing.ToDictionary(c => c, c => (int)VisibilityOf(c));
            Action relight;
            switch (cast.Tap)
            {
                case FuelSource.Torch when cast.TapAt is { } at && lightsOnTheGround.Remove(at):
                    relight = () => lightsOnTheGround.Add(at);
                    break;
                case FuelSource.Campfire when cast.TapAt is { } fire && spentFires.Add(fire):
                    relight = () => spentFires.Remove(fire);
                    break;
                case FuelSource.Torch or FuelSource.Lantern when HeldLight(caster, cast.Tap) is { } held:
                    held.IsLit = false;
                    relight = () => held.IsLit = true;
                    break;
                default:
                    return (0, 0);
            }

            var lost = standing.Select(c => (c, Lost: (int)VisibilityOf(c) - before[c])).ToList();
            relight();
            return (lost.Where(x => x.c.Side != caster.Side).Sum(x => x.Lost), lost.Where(x => x.c.Side == caster.Side).Sum(x => x.Lost));
        }

        /// <summary>The lit torch, or lantern, in <paramref name="caster"/>'s hand.</summary>
        private static InventoryItemDefinition? HeldLight(Combatant caster, FuelSource kind) =>
            caster.Build?.InventoryItems.FirstOrDefault(i => InventoryCalculator.IsLitLight(i)
                && i.Name.Contains(kind == FuelSource.Torch ? "torch" : "lantern", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Spends the source <paramref name="cast"/> taps, whole: a torch burns up, a lantern's oil
        /// goes and leaves a cold lantern to refill, a built fire burns down — and each was a light
        /// as well, which the Visibility it gave now loses (Core Rules §11, §14). Says what it was.
        /// </summary>
        private string Tap(Combatant caster, SpellCast cast)
        {
            switch (cast.Tap)
            {
                case FuelSource.Torch when cast.TapAt is { } at:
                    lightsOnTheGround.Remove(at);
                    return "the torch on the ground, which burns out";
                case FuelSource.Torch:
                    var torch = HeldLight(caster, FuelSource.Torch)!;
                    torch.IsLit = false;
                    torch.Quantity--;
                    if (torch.Quantity <= 0)
                    {
                        torch.IsStored = true;
                    }

                    return "the torch in their hand, which goes black";
                case FuelSource.Lantern:
                    var lantern = HeldLight(caster, FuelSource.Lantern)!;
                    lantern.IsLit = false;
                    caster.DrainedLanterns.Add(lantern);
                    return "the lantern's oil — the lantern gutters out";
                default:
                    spentFires.Add(cast.TapAt!.Value);
                    return "the fire, which burns down to nothing";
            }
        }
    }
}
