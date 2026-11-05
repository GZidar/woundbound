using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace WoundBound.Game.Combat
{
    /// <summary>
    /// Copies a live object graph — an encounter, its creatures, their holds, spells, closures and
    /// all — so that a copy can be played forward without touching the original. Every reference
    /// inside the graph is pointed at its counterpart in the copy; a closure's captured state is
    /// copied with it, so a continuation the encounter is waiting on resumes in the copy.
    /// </summary>
    /// <remarks>
    /// Shared rather than copied: strings, primitives and enums, reflection objects, the loaded game
    /// data and rules catalogue, the few rules models a fight only reads (<see cref="ReadOnlyInPlay"/>),
    /// and anything seeded with <see cref="Seed"/>. Dictionaries and sets are rebuilt rather than copied field by field, since
    /// their buckets hold each key's identity hash.
    /// </remarks>
    internal sealed class GraphFork
    {
        private readonly Dictionary<object, object?> copies;

        /// <param name="scratch">
        /// A map to keep the copies in, emptied first — one kept and handed in again by a caller that
        /// forks over and over saves growing a large one afresh each time. It must not be handed to
        /// another fork while this one's copies are still being looked up.
        /// </param>
        public GraphFork(Dictionary<object, object?>? scratch = null)
        {
            copies = scratch ?? new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
            copies.Clear();
        }

        /// <summary>Makes <paramref name="original"/> come out as <paramref name="replacement"/> wherever it is met — as nothing, given null.</summary>
        public void Seed(object original, object? replacement) => copies[original] = replacement;

        /// <summary>The copy of <paramref name="original"/> — made now if it hasn't been met yet.</summary>
        public T Copy<T>(T original)
        {
            var copy = (T)CopyObject(original)!;
            Drain();
            return copy;
        }

        /// <summary>
        /// Copies met but not yet filled in. A copy is made shallow when first met and its references
        /// fixed up from here, so a long chain — a continuation waiting on a continuation — costs a
        /// queue entry each rather than a stack frame each.
        /// </summary>
        private readonly Queue<Action> toFill = new();

        private void Drain()
        {
            while (toFill.TryDequeue(out var fill))
            {
                fill();
            }
        }

        private object? CopyObject(object? original)
        {
            if (original is null)
            {
                return null;
            }

            if (copies.TryGetValue(original, out var known))
            {
                return known;
            }

            var type = original.GetType();
            if (IsShared(type))
            {
                return original;
            }

            if (IsForeign(type))
            {
                throw new NotSupportedException($"A fight's fork reached a {type.FullName}, which belongs to whatever is running or watching the fight, not to the fight.");
            }

            switch (original)
            {
                case Delegate d:
                    return Remember(original, CopyDelegate(d));
                case Array array:
                    return CopyArray(array);
            }

            if (type.IsGenericType && Rebuilders.TryGetValue(type.GetGenericTypeDefinition(), out var rebuild))
            {
                return rebuild(this, original);
            }

            var copy = Clone(original);
            copies[original] = copy;
            toFill.Enqueue(() => CopyFields(copy, type));
            return copy;
        }

        private object Remember(object original, object copy)
        {
            copies[original] = copy;
            return copy;
        }

        private void CopyFields(object copy, Type type)
        {
            foreach (var field in FieldsOf(type))
            {
                var value = field.GetValue(copy);
                if (value is null)
                {
                    continue;
                }

                if (field.FieldType.IsValueType && !NeedsDeepStruct(field.FieldType))
                {
                    continue;
                }

                field.SetValue(copy, field.FieldType.IsValueType ? CopyStruct(value) : CopyObject(value));
            }
        }

        /// <summary>A struct that holds references — a tuple of a turn and its continuation — has them copied too.</summary>
        private object CopyStruct(object boxed)
        {
            var type = boxed.GetType();
            foreach (var field in FieldsOf(type))
            {
                var value = field.GetValue(boxed);
                if (value is not null && (!field.FieldType.IsValueType || NeedsDeepStruct(field.FieldType)))
                {
                    field.SetValue(boxed, field.FieldType.IsValueType ? CopyStruct(value) : CopyObject(value));
                }
            }

            return boxed;
        }

        private object CopyArray(Array array)
        {
            var copy = (Array)array.Clone();
            copies[array] = copy;
            var element = array.GetType().GetElementType()!;
            if (element.IsValueType && !NeedsDeepStruct(element))
            {
                return copy;
            }

            if (array.Rank != 1)
            {
                throw new NotSupportedException($"Can't fork a {array.Rank}-dimensional array of {element.Name}.");
            }

            toFill.Enqueue(() =>
            {
                for (var i = 0; i < copy.Length; i++)
                {
                    var value = copy.GetValue(i);
                    copy.SetValue(element.IsValueType ? (value is null ? null : CopyStruct(value)) : CopyObject(value), i);
                }
            });

            return copy;
        }

        private object CopyDelegate(Delegate d)
        {
            var calls = d.GetInvocationList();
            if (calls.Length > 1)
            {
                return Delegate.Combine(calls.Select(c => (Delegate)CopyObject(c)!).ToArray())!;
            }

            if (d.Target is null || FieldsOf(d.Target.GetType()).Length == 0)
            {
                return d;
            }

            return Delegate.CreateDelegate(d.GetType(), CopyObject(d.Target), d.Method);
        }

        private static readonly Func<object, object> Clone =
            (Func<object, object>)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Func<object, object>));

        private static bool IsShared(Type type) => SharedTypes.GetOrAdd(type, static t =>
            t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
            || typeof(MemberInfo).IsAssignableFrom(t)
            || ReadOnlyInPlay.Contains(t)
            || (t.Namespace is { } ns && (ns == "WoundBound.Game.Data" || ns.StartsWith("WoundBound.Rules.Catalog", StringComparison.Ordinal))));

        /// <summary>
        /// The models a fight only ever reads: a statblock's lines, a spell as written, the ground. Every
        /// other model is copied — a character's build is played on (a weapon readied or put away sets
        /// flags on the build's own weapon), and copying something needlessly only costs time, where
        /// sharing something played on lets a fork's future leak into the fight it was taken from.
        /// </summary>
        private static readonly HashSet<Type> ReadOnlyInPlay =
        [
            typeof(Rules.Models.SpellDefinition),
            typeof(Rules.Models.AdversaryTemplate),
            typeof(Rules.Models.AdversaryAttack),
            typeof(Rules.Models.AdversaryCasting),
            typeof(Rules.Models.AdversarySpecial),
            typeof(Rules.Models.AdversarySpell),
            typeof(Rules.Models.RangeBandNames),

            // The ground as laid out: an init-only record the fight never replaces or changes.
            typeof(Setting),
        ];

        private static readonly ConcurrentDictionary<Type, bool> SharedTypes = new();

        /// <summary>What a fight must never hold on to: the screen, a test runner, a thread.</summary>
        private static bool IsForeign(Type type) => type.Namespace is { } ns
            && (ns.StartsWith("Godot", StringComparison.Ordinal) || ns.StartsWith("Xunit", StringComparison.Ordinal)
                || ns.StartsWith("System.Threading", StringComparison.Ordinal) || ns.StartsWith("System.IO", StringComparison.Ordinal));

        private static bool NeedsDeepStruct(Type type) => DeepStructs.GetOrAdd(type, static t =>
            !t.IsPrimitive && !t.IsEnum && FieldsOf(t).Any(f => !f.FieldType.IsValueType || (f.FieldType != t && NeedsDeepStruct(f.FieldType))));

        private static readonly ConcurrentDictionary<Type, bool> DeepStructs = new();

        private static FieldInfo[] FieldsOf(Type type) => Fields.GetOrAdd(type, static t =>
        {
            var all = new List<FieldInfo>();
            for (var at = t; at is not null && at != typeof(object); at = at.BaseType)
            {
                all.AddRange(at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }

            return all.ToArray();
        });

        private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new();

        private static readonly Dictionary<Type, Func<GraphFork, object, object>> Rebuilders = new()
        {
            [typeof(Dictionary<,>)] = (fork, original) => Rebuild(fork, original, nameof(RebuildDictionary)),
            [typeof(HashSet<>)] = (fork, original) => Rebuild(fork, original, nameof(RebuildSet)),
        };

        private static object Rebuild(GraphFork fork, object original, string how)
        {
            var method = RebuildMethods.GetOrAdd((original.GetType(), how), static key =>
                typeof(GraphFork).GetMethod(key.How, BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(key.Type.GetGenericArguments()));
            return method.Invoke(null, [fork, original])!;
        }

        private static readonly ConcurrentDictionary<(Type Type, string How), MethodInfo> RebuildMethods = new();

        private static object RebuildDictionary<TKey, TValue>(GraphFork fork, Dictionary<TKey, TValue> original)
            where TKey : notnull
        {
            var copy = new Dictionary<TKey, TValue>(original.Count, original.Comparer);
            fork.copies[original] = copy;
            fork.toFill.Enqueue(() =>
            {
                // Keys filled in before they go in: a record key's hash is taken over its fields.
                var entries = original.Select(e => (Key: fork.CopyValue(e.Key), Value: fork.CopyValue(e.Value))).ToList();
                fork.Drain();
                foreach (var (key, value) in entries)
                {
                    copy[key] = value;
                }
            });

            return copy;
        }

        private static object RebuildSet<T>(GraphFork fork, HashSet<T> original)
        {
            var copy = new HashSet<T>(original.Comparer);
            fork.copies[original] = copy;
            fork.toFill.Enqueue(() =>
            {
                var items = original.Select(fork.CopyValue).ToList();
                fork.Drain();
                foreach (var item in items)
                {
                    copy.Add(item);
                }
            });

            return copy;
        }

        private T CopyValue<T>(T value) =>
            value is null ? value : typeof(T).IsValueType ? (NeedsDeepStruct(typeof(T)) ? (T)CopyStruct(value) : value) : (T)CopyObject(value)!;
    }
}
