using WoundBound.Game.Combat;
using WoundBound.Rules.Enums;

namespace WoundBound.Game.Figures
{
    /// <summary>How a body is laid out: what it stands on, how it moves, where it strikes from.</summary>
    public enum BodyPlan
    {
        /// <summary>Two legs, two arms, a head on top: people, beastfolk, the walking dead, golems.</summary>
        Humanoid,

        /// <summary>Four legs, a body slung between them, a head out front: wolves, bears, stags, horses.</summary>
        Quadruped,

        /// <summary>A small body held up on two wings: bats.</summary>
        Winged,

        /// <summary>A crowd of small bodies, moving as one: a Ratswarm.</summary>
        Swarm,

        /// <summary>A body that trails away below the waist and floats: spirits.</summary>
        Spirit,
    }

    /// <summary>What sits on the shoulders, or out front.</summary>
    public enum HeadShape
    {
        Human,
        Rat,
        Hound,
        Hyena,
        Bear,
        Deer,
        Horse,
        Bat,

        /// <summary>A helm with nothing in it.</summary>
        Hollow,
    }

    /// <summary>What a body is made of, which sets its colours and its sheen.</summary>
    public enum BodyStuff
    {
        Flesh,
        Fur,

        /// <summary>The dead, walking: grey-green skin, rags.</summary>
        Dead,
        Stone,
        Metal,

        /// <summary>Pale, and seen through.</summary>
        Ghost,
    }

    /// <summary>
    /// A creature's body, as the board draws it (the user's direction, 2026-11-03: a small set of
    /// body types, chosen from the statblock's Type and Size, refined by the name). Characters are
    /// all humanoid; their kit is drawn from their sheet.
    /// </summary>
    /// <param name="Hulking">Big through the shoulders: Large humanoids and golems.</param>
    /// <param name="Hunched">Stooped forward: beastfolk, and the dead.</param>
    public sealed record BodyShape(BodyPlan Plan, HeadShape Head, BodyStuff Stuff, bool Tail = false, bool Antlers = false,
        bool Hulking = false, bool Hunched = false)
    {
        public static readonly BodyShape Person = new(BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Flesh);

        public static readonly BodyShape Horse = new(BodyPlan.Quadruped, HeadShape.Horse, BodyStuff.Fur, Tail: true);

        /// <summary>The body <paramref name="creature"/> is drawn with.</summary>
        public static BodyShape Of(Combatant creature)
        {
            if (creature.IsSteed)
            {
                return Horse;
            }

            if (creature.IsSwarm)
            {
                return new BodyShape(BodyPlan.Swarm, Words(creature).Contains("rat") ? HeadShape.Rat : HeadShape.Hound, BodyStuff.Fur);
            }

            if (creature.Statblock is not { } statblock)
            {
                return Person;
            }

            var words = Words(creature);
            bool Says(params string[] any) => any.Any(words.Contains);
            var big = statblock.Size is AdversarySize.Large or AdversarySize.Huge;

            // The name says most: a wolf is a wolf, whatever its Type line.
            if (Says("bat"))
            {
                return new BodyShape(BodyPlan.Winged, HeadShape.Bat, BodyStuff.Fur);
            }

            if (Says("wolf", "hound", "dog", "jackal"))
            {
                return new BodyShape(BodyPlan.Quadruped, HeadShape.Hound, BodyStuff.Fur, Tail: true);
            }

            if (Says("bear"))
            {
                return new BodyShape(BodyPlan.Quadruped, HeadShape.Bear, BodyStuff.Fur, Hulking: true);
            }

            if (Says("stag", "deer", "elk", "hart"))
            {
                return new BodyShape(BodyPlan.Quadruped, HeadShape.Deer, BodyStuff.Fur, Tail: true, Antlers: true);
            }

            if (Says("horse", "steed", "pony", "mule"))
            {
                return Horse;
            }

            if (Says("ratkin", "ratman"))
            {
                return new BodyShape(BodyPlan.Humanoid, HeadShape.Rat, BodyStuff.Fur, Tail: true, Hunched: true);
            }

            if (Says("gnoll", "hyena"))
            {
                return new BodyShape(BodyPlan.Humanoid, HeadShape.Hyena, BodyStuff.Fur, Tail: true, Hunched: true);
            }

            return statblock.CreatureType switch
            {
                CreatureType.Beast => new BodyShape(BodyPlan.Quadruped, HeadShape.Hound, BodyStuff.Fur, Tail: true, Hulking: big),
                CreatureType.Spirit => new BodyShape(BodyPlan.Spirit, HeadShape.Human, BodyStuff.Ghost, Hulking: big),
                CreatureType.Undead => new BodyShape(BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Dead, Hulking: big, Hunched: true),
                CreatureType.Construct when Says("mail", "armour", "armor", "plate", "iron", "brass") =>
                    new BodyShape(BodyPlan.Humanoid, HeadShape.Hollow, BodyStuff.Metal, Hulking: big),
                CreatureType.Construct => new BodyShape(BodyPlan.Humanoid, HeadShape.Human, BodyStuff.Stone, Hulking: true),
                _ => Person with { Hulking = big },
            };
        }

        /// <summary>The words of a creature's name and its statblock's — lower case, for matching.</summary>
        private static HashSet<string> Words(Combatant creature)
        {
            var text = $"{creature.Name} {creature.Statblock?.Name} {creature.Statblock?.Identity}".ToLowerInvariant();
            var words = text.Split([' ', '-', ',', '.', '(', ')', '\'', ';', ':'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            // "Wolves", "rats": a plural names the same beast.
            foreach (var word in words.ToList())
            {
                if (word.EndsWith("ves"))
                {
                    words.Add(word[..^3] + "f");
                }
                else if (word.EndsWith('s'))
                {
                    words.Add(word[..^1]);
                }
            }

            return words;
        }
    }
}
