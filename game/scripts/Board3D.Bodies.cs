using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WoundBound.Game.Combat;
using WoundBound.Game.Figures;
using WoundBound.Rules.Enums;

namespace WoundBoundGame
{
    /// <summary>
    /// The bodies figures are built with (the user's direction, 2026-11-03): people as before, and
    /// for adversaries a small set of body plans — beastfolk with a beast's head and tail, four-legged
    /// beasts, bats on the wing, a swarm, a spirit that floats — in fur, flesh, dead skin, stone, metal
    /// or ghostlight. Each takes the same poses the acts lay on a person — a swing, a reach, a kick,
    /// a lean — read its own way: a beast's swing is a lunge of the head, a bat's a dive.
    /// </summary>
    public partial class Board3D
    {
        private sealed partial class Figure
        {
            private BodyShape shape = BodyShape.Person;

            /// <summary>A beast's front legs; its hind legs are the hips a person walks on.</summary>
            private readonly Node3D foreLeft = new();
            private readonly Node3D foreRight = new();

            /// <summary>Where a beast's head is hung, so a bite can drive it forward and down.</summary>
            private readonly Node3D neck = new();

            private readonly Node3D tail = new();
            private readonly Node3D leftWing = new();
            private readonly Node3D rightWing = new();
            private readonly List<(MeshInstance3D Bit, Vector3 Home, float Phase)> swarm = new();

            /// <summary>The figure's own clock, for what moves whether it acts or not — wings, a swarm's churn, a spirit's drift.</summary>
            private float time;

            /// <summary>Heights and reach in the body's own units: the middle of it, and where a blow or a shot leaves from.</summary>
            private float chestHeight = 0.68f;
            private float handHeight = 0.75f;
            private float handReach = 0.25f;

            /// <summary>How far the body is stooped forward when standing.</summary>
            private float stoop;

            /// <summary>How high above the ground it holds itself: a bat on the wing, a spirit adrift.</summary>
            private float hover;

            private static readonly Color Skin = new(0.91f, 0.86f, 0.78f);
            private static readonly Color DeadSkin = new(0.58f, 0.63f, 0.52f);
            private static readonly Color StoneGrey = new(0.56f, 0.54f, 0.5f);
            private static readonly Color Steel = new(0.62f, 0.64f, 0.68f);
            private static readonly Color GhostLight = new(0.72f, 0.86f, 0.95f);

            /// <summary>A beast's coat, by its kind.</summary>
            private static Color Coat(HeadShape head) => head switch
            {
                HeadShape.Hound => new Color(0.47f, 0.46f, 0.47f),
                HeadShape.Bear => new Color(0.36f, 0.25f, 0.16f),
                HeadShape.Deer => new Color(0.6f, 0.45f, 0.3f),
                HeadShape.Horse => new Color(0.45f, 0.3f, 0.18f),
                HeadShape.Rat => new Color(0.44f, 0.39f, 0.34f),
                HeadShape.Hyena => new Color(0.64f, 0.54f, 0.37f),
                HeadShape.Bat => new Color(0.27f, 0.21f, 0.21f),
                _ => Skin,
            };

            private static StandardMaterial3D Ghostly(Color colour) => new()
            {
                AlbedoColor = colour with { A = 0.55f },
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true,
                Emission = colour * 0.35f,
                Roughness = 0.6f,
            };

            // ---- Building ----

            /// <summary>Builds the body for <paramref name="creature"/>'s shape; its kit is drawn on afterwards, by the caller, for anybody with hands.</summary>
            private void Build(Combatant creature, Color colour)
            {
                shape = BodyShape.Of(creature);
                switch (shape.Plan)
                {
                    case BodyPlan.Quadruped:
                        Beast(colour);
                        break;
                    case BodyPlan.Winged:
                        Bat(colour);
                        break;
                    case BodyPlan.Swarm:
                        Swarm(creature, colour);
                        break;
                    case BodyPlan.Spirit:
                        Spirit(creature, colour);
                        break;
                    default:
                        Person(creature, colour);
                        break;
                }
            }

            /// <summary>Two legs, two arms, a head — in cloth and skin, fur, dead flesh, stone or metal — and what it carries in hand.</summary>
            private void Person(Combatant creature, Color colour)
            {
                var (clothColour, limbColour, headColour) = shape.Stuff switch
                {
                    BodyStuff.Fur => (colour, Coat(shape.Head), Coat(shape.Head)),
                    BodyStuff.Dead => (colour.Darkened(0.45f), DeadSkin, DeadSkin),
                    BodyStuff.Stone => (StoneGrey, StoneGrey, StoneGrey),
                    BodyStuff.Metal => (Steel, Steel, Steel),
                    _ => (colour, colour, Skin),
                };
                var cloth = shape.Stuff == BodyStuff.Metal ? Material(clothColour, metallic: 0.6f) : Material(clothColour);
                var limbs = shape.Stuff == BodyStuff.Metal ? cloth : Material(limbColour);
                var head = Material(headColour);
                var metal = Material(Steel, metallic: 0.6f);
                var wood = Material(new Color(0.48f, 0.33f, 0.2f));
                // A character's own build (gender, height, girth) where the player has given one.
                var look = creature.Appearance;
                var girth = look?.GirthScale ?? 1;
                var shoulders = (look?.Gender == FigureGender.Female ? 0.9f : 1) * (0.88f + 0.12f * girth);
                var bulk = (creature.Armour switch { ArmourWeight.Heavy => 0.34f, ArmourWeight.Medium => 0.3f, ArmourWeight.Light => 0.27f, _ => 0.24f })
                           * (shape.Hulking ? 1.3f : 1) * girth;
                stoop = shape.Hunched ? 16 : 0;

                // Two legs and two arms, each hung from its joint so a walk can swing them.
                arm.Position = Shoulder with { X = Shoulder.X * (shape.Hulking ? 1.25f : 1) * shoulders };
                body.AddChild(arm);
                var legRadius = (shape.Hulking ? 0.07f : 0.05f) * (0.75f + 0.25f * girth);
                foreach (var (hip, x) in new[] { (leftHip, -0.065f), (rightHip, 0.065f) })
                {
                    hip.Position = new Vector3(x * (shape.Hulking ? 1.4f : 1) * (0.8f + 0.2f * girth), 0.45f, 0);
                    body.AddChild(hip);
                    Part(new CylinderMesh { TopRadius = legRadius, BottomRadius = legRadius * 0.8f, Height = 0.45f }, new Vector3(0, -0.225f, 0), limbs, parent: hip);
                }

                var armRadius = (shape.Hulking ? 0.055f : 0.035f) * (0.75f + 0.25f * girth);
                offArm.Position = arm.Position with { X = -arm.Position.X };
                body.AddChild(offArm);
                Part(new CylinderMesh { TopRadius = armRadius, BottomRadius = armRadius * 0.85f, Height = 0.3f }, new Vector3(0, -0.15f, 0), limbs, parent: offArm);
                Part(new CylinderMesh { TopRadius = armRadius, BottomRadius = armRadius * 0.85f, Height = 0.3f }, new Vector3(0, -0.13f, 0.04f), limbs, new Vector3(15, 0, 0), parent: arm);
                Part(new CapsuleMesh { Radius = bulk / 2, Height = 0.5f }, new Vector3(0, 0.66f, 0), cloth);                                       // torso
                if (shape.Stuff is BodyStuff.Stone or BodyStuff.Metal)
                {
                    // Stone and metal wear their side's colour as a sash, so they can still be told apart.
                    Part(new TorusMesh { InnerRadius = bulk / 2 - 0.005f, OuterRadius = bulk / 2 + 0.03f }, new Vector3(0, 0.6f, 0), Material(colour), new Vector3(0, 0, 18));
                }
                else if (creature.Armour is ArmourWeight.Medium or ArmourWeight.Heavy)
                {
                    Part(new CapsuleMesh { Radius = bulk / 2 + 0.02f, Height = 0.36f }, new Vector3(0, 0.7f, 0), metal);                          // mail or plate over it
                }

                if (look?.Gender == FigureGender.Female)
                {
                    // The tunic flares over the hips.
                    Part(new CylinderMesh { TopRadius = bulk / 2, BottomRadius = bulk / 2 * 1.3f, Height = 0.2f, RadialSegments = 12 }, new Vector3(0, 0.47f, 0), cloth);
                }

                var headAt = new Vector3(0, shape.Hulking ? 1.0f : 1.03f, 0);
                Head(headAt, head, metal, creature.Armour == ArmourWeight.Heavy);
                if (look is not null && shape.Head == HeadShape.Human && shape.Stuff == BodyStuff.Flesh)
                {
                    Hair(headAt, look, creature.Armour == ArmourWeight.Heavy);
                }
                if (shape.Tail)
                {
                    tail.Position = new Vector3(0, 0.45f, -0.08f);
                    body.AddChild(tail);
                    Part(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.008f, Height = 0.45f }, new Vector3(0, 0, -0.22f), limbs, new Vector3(-80, 0, 0), parent: tail);
                }

                Kit(creature, metal, wood);
            }

            /// <summary>Each hair colour as drawn.</summary>
            private static Color HairShade(HairColour hair) => hair switch
            {
                HairColour.Black => new Color(0.1f, 0.09f, 0.08f),
                HairColour.Brown => new Color(0.32f, 0.21f, 0.12f),
                HairColour.Auburn => new Color(0.5f, 0.24f, 0.13f),
                HairColour.Red => new Color(0.72f, 0.3f, 0.12f),
                HairColour.Blond => new Color(0.85f, 0.72f, 0.42f),
                HairColour.Grey => new Color(0.6f, 0.59f, 0.57f),
                _ => new Color(0.9f, 0.9f, 0.88f),
            };

            /// <summary>
            /// Hair, as the figure's gender has it: cropped short; long down the back; or gathered
            /// in a knot — in the colour the player chose, or none at all for a bald head. Under a
            /// helm only what hangs below it shows.
            /// </summary>
            private void Hair(Vector3 headAt, WoundBound.Game.Figures.Appearance look, bool helmed)
            {
                if (look.Hair == HairColour.Bald)
                {
                    return;
                }

                var hair = Material(HairShade(look.Hair));
                if (!helmed)
                {
                    // A cap over the crown and the back of the head, leaving the face clear.
                    Part(new SphereMesh { Radius = 0.128f, Height = 0.17f, IsHemisphere = true }, headAt + new Vector3(0, 0.015f, -0.012f), hair, new Vector3(-12, 0, 0));
                }

                switch (look.Gender)
                {
                    case FigureGender.Female:
                        Part(new BoxMesh { Size = new Vector3(0.2f, 0.3f, 0.06f) }, headAt + new Vector3(0, -0.1f, -0.1f), hair, new Vector3(8, 0, 0));
                        break;
                    case FigureGender.Other:
                        Part(new SphereMesh { Radius = 0.065f, Height = 0.13f }, headAt + new Vector3(0, 0.1f, -0.1f), hair);
                        break;
                }
            }

            /// <summary>The head on a person's shoulders: a face, a beast's snout and ears, or a helm with nothing inside.</summary>
            private void Head(Vector3 at, Material head, Material metal, bool helmed)
            {
                switch (shape.Head)
                {
                    case HeadShape.Hollow:
                        Part(new SphereMesh { Radius = 0.13f, Height = 0.26f }, at, metal);
                        Part(new BoxMesh { Size = new Vector3(0.16f, 0.025f, 0.02f) }, at + new Vector3(0, 0.01f, 0.125f), Material(new Color(0.05f, 0.05f, 0.06f))); // the visor, dark
                        return;
                    case HeadShape.Rat or HeadShape.Hyena:
                        Part(new SphereMesh { Radius = 0.11f, Height = 0.22f }, at, head);
                        var snout = shape.Head == HeadShape.Rat ? new Vector3(0.07f, 0.07f, 0.17f) : new Vector3(0.09f, 0.09f, 0.15f);
                        Part(new BoxMesh { Size = snout }, at + new Vector3(0, -0.03f, 0.13f), head);
                        Part(new SphereMesh { Radius = 0.022f, Height = 0.044f }, at + new Vector3(0, -0.02f, 0.21f), Material(new Color(0.12f, 0.1f, 0.1f))); // nose
                        var ear = shape.Head == HeadShape.Rat ? new SphereMesh { Radius = 0.055f, Height = 0.03f } : (Mesh)new PrismMesh { Size = new Vector3(0.07f, 0.1f, 0.03f) };
                        Part(ear, at + new Vector3(-0.08f, 0.1f, -0.01f), head, new Vector3(0, 0, 20));
                        Part(ear, at + new Vector3(0.08f, 0.1f, -0.01f), head, new Vector3(0, 0, -20));
                        return;
                    default:
                        Part(new SphereMesh { Radius = 0.12f, Height = 0.24f }, at, head);
                        if (helmed || shape.Stuff == BodyStuff.Stone)
                        {
                            Part(new SphereMesh { Radius = 0.13f, Height = 0.16f, IsHemisphere = true }, at + new Vector3(0, 0.03f, 0), shape.Stuff == BodyStuff.Stone ? head : metal); // helm, or a stone brow
                        }

                        return;
                }
            }

            /// <summary>What's in hand hangs from the shoulder, so a blow can swing it and a cast raise it.</summary>
            private void Kit(Combatant creature, Material metal, Material wood)
            {
                var hand = new Vector3(0.2f, 0.6f, 0.08f) - Shoulder;
                var armed = creature.Attacks.Any(a => a.Name != "Unarmed");
                if (creature.HasRangedAttack && creature.Attacks.All(a => a.IsRanged || a.Name == "Unarmed"))
                {
                    Part(new BoxMesh { Size = new Vector3(0.03f, 0.65f, 0.05f) }, hand + new Vector3(0, 0.1f, 0.06f), wood, parent: arm);                    // bow stave
                }
                else if (creature.Spells.Count > 0 && !armed)
                {
                    Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.9f }, hand + new Vector3(0, 0.05f, 0), wood, parent: arm);    // a caster's staff
                }
                else if (armed && shape.Stuff != BodyStuff.Stone && !NaturalWeapon(creature))
                {
                    Part(new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.55f) }, hand + new Vector3(0, 0.02f, 0.28f), metal, parent: arm);                  // a blade, held forward
                }
                else if (armed)
                {
                    // A fist of stone, or claws: a heavy hand on the end of the arm.
                    Part(new SphereMesh { Radius = 0.06f, Height = 0.12f }, hand + new Vector3(0, 0, 0.02f), shape.Stuff == BodyStuff.Stone ? Material(StoneGrey) : Material(Coat(shape.Head)), parent: arm);
                }

                if (creature.InHandShield is not null)
                {
                    Part(new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.04f }, new Vector3(-0.2f, 0.66f, 0.1f), wood, new Vector3(90, 0, 0));
                }
            }

            /// <summary>Whether its blow is its own body's — claws, a bite, a fist — rather than a weapon held.</summary>
            private static bool NaturalWeapon(Combatant creature) =>
                creature.Attacks.Where(a => a.Name != "Unarmed").All(a =>
                    a.Name.Contains("Claw", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Bite", StringComparison.OrdinalIgnoreCase)
                    || a.Name.Contains("Fist", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Slam", StringComparison.OrdinalIgnoreCase));

            /// <summary>
            /// Four legs, a body slung between them, a head out front on a neck that can lunge, a tail.
            /// A wolf is lean, a bear heavy and low, a stag long-legged under its antlers, a horse tall
            /// enough to carry a rider. It wears its side's colour as a collar.
            /// </summary>
            private void Beast(Color colour)
            {
                var (legs, girth, length, neckRise, headSize) = shape.Head switch
                {
                    HeadShape.Bear => (0.2f, 0.15f, 0.5f, 0.02f, 0.1f),
                    HeadShape.Deer => (0.32f, 0.09f, 0.46f, 0.16f, 0.065f),
                    HeadShape.Horse => (0.36f, 0.12f, 0.6f, 0.18f, 0.08f),
                    _ => (0.22f, 0.085f, 0.48f, 0.06f, 0.07f),
                };
                var coat = Material(Coat(shape.Head));
                var dark = Material(Coat(shape.Head).Darkened(0.45f));
                var back = legs + girth;
                var half = length / 2;

                // The body, lying along the way it faces.
                Part(new CapsuleMesh { Radius = girth, Height = length + girth * 2 }, new Vector3(0, back, 0), coat, new Vector3(90, 0, 0));

                // Hind legs on the hips a person walks on; front legs on their own joints.
                foreach (var (joint, x, z) in new[] { (leftHip, -1, -1), (rightHip, 1, -1), (foreLeft, -1, 1), (foreRight, 1, 1) })
                {
                    joint.Position = new Vector3(x * girth * 0.55f, back - girth * 0.4f, z * (half - 0.02f));
                    body.AddChild(joint);
                    var leg = legs + girth * 0.4f;
                    Part(new CylinderMesh { TopRadius = girth * 0.38f, BottomRadius = girth * 0.25f, Height = leg }, new Vector3(0, -leg / 2, 0), coat, parent: joint);
                    Part(new CylinderMesh { TopRadius = girth * 0.27f, BottomRadius = girth * 0.3f, Height = 0.03f }, new Vector3(0, -leg + 0.015f, 0), dark, parent: joint); // paw or hoof
                }

                // The neck, and the head on it, out front.
                neck.Position = new Vector3(0, back + girth * 0.3f, half + girth * 0.4f);
                body.AddChild(neck);
                var headAt = new Vector3(0, neckRise + headSize * 0.4f, headSize * 1.1f);
                if (neckRise > 0.1f)
                {
                    Part(new CylinderMesh { TopRadius = headSize * 0.6f, BottomRadius = girth * 0.7f, Height = neckRise + 0.08f }, new Vector3(0, neckRise / 2, headSize * 0.4f), coat, new Vector3(25, 0, 0), parent: neck);
                }

                Part(new SphereMesh { Radius = headSize, Height = headSize * 2 }, headAt, coat, parent: neck);
                var snout = shape.Head switch
                {
                    HeadShape.Bear => new Vector3(0.08f, 0.07f, 0.09f),
                    HeadShape.Deer => new Vector3(0.05f, 0.05f, 0.11f),
                    HeadShape.Horse => new Vector3(0.07f, 0.07f, 0.16f),
                    _ => new Vector3(0.055f, 0.055f, 0.12f),
                };
                Part(new BoxMesh { Size = snout }, headAt + new Vector3(0, -headSize * 0.3f, headSize * 0.7f + snout.Z / 2), coat, parent: neck);
                Part(new SphereMesh { Radius = 0.018f, Height = 0.036f }, headAt + new Vector3(0, -headSize * 0.15f, headSize * 0.7f + snout.Z), Material(new Color(0.1f, 0.08f, 0.08f)), parent: neck);

                // Ears: pricked on a hound and a horse, round on a bear; antlers on a stag.
                Mesh ear = shape.Head == HeadShape.Bear ? new SphereMesh { Radius = 0.035f, Height = 0.05f } : new PrismMesh { Size = new Vector3(0.045f, 0.07f, 0.02f) };
                Part(ear, headAt + new Vector3(-headSize * 0.6f, headSize * 0.9f, -0.01f), coat, new Vector3(0, 0, 15), parent: neck);
                Part(ear, headAt + new Vector3(headSize * 0.6f, headSize * 0.9f, -0.01f), coat, new Vector3(0, 0, -15), parent: neck);
                if (shape.Antlers)
                {
                    var bone = Material(new Color(0.85f, 0.8f, 0.68f));
                    foreach (var side in new[] { -1, 1 })
                    {
                        var root = headAt + new Vector3(side * headSize * 0.5f, headSize * 0.9f, 0);
                        Part(new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.014f, Height = 0.26f }, root + new Vector3(side * 0.05f, 0.11f, -0.02f), bone, new Vector3(-10, 0, -side * 25), parent: neck);
                        Part(new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.01f, Height = 0.12f }, root + new Vector3(side * 0.06f, 0.12f, 0.05f), bone, new Vector3(35, 0, -side * 10), parent: neck);
                        Part(new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.01f, Height = 0.12f }, root + new Vector3(side * 0.13f, 0.2f, 0.01f), bone, new Vector3(20, 0, -side * 50), parent: neck);
                    }
                }

                // Its side's colour, as a collar.
                Part(new TorusMesh { InnerRadius = girth * 0.62f, OuterRadius = girth * 0.82f }, new Vector3(0, 0.02f, 0.04f), Material(colour), new Vector3(70, 0, 0), parent: neck);

                if (shape.Tail)
                {
                    tail.Position = new Vector3(0, back + girth * 0.5f, -half - girth * 0.6f);
                    body.AddChild(tail);
                    var tailLength = shape.Head == HeadShape.Deer ? 0.08f : shape.Head == HeadShape.Horse ? 0.3f : 0.24f;
                    Part(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.012f, Height = tailLength }, new Vector3(0, -tailLength * 0.3f, -tailLength * 0.4f), shape.Head == HeadShape.Horse ? dark : coat, new Vector3(-55, 0, 0), parent: tail);
                }

                chestHeight = back;
                handHeight = neck.Position.Y + headAt.Y;
                handReach = neck.Position.Z + headAt.Z + snout.Z;
            }

            /// <summary>A small furred body between two leathery wings that never stop beating, held up off the ground.</summary>
            private void Bat(Color colour)
            {
                var coat = Material(Coat(HeadShape.Bat));
                var membrane = Material(Coat(HeadShape.Bat).Lightened(0.15f));
                hover = 0.75f;
                Part(new CapsuleMesh { Radius = 0.08f, Height = 0.26f }, Vector3.Zero, coat, new Vector3(70, 0, 0));
                Part(new SphereMesh { Radius = 0.075f, Height = 0.15f }, new Vector3(0, 0.06f, 0.13f), coat);
                Part(new PrismMesh { Size = new Vector3(0.05f, 0.09f, 0.02f) }, new Vector3(-0.045f, 0.15f, 0.12f), coat, new Vector3(0, 0, 15));
                Part(new PrismMesh { Size = new Vector3(0.05f, 0.09f, 0.02f) }, new Vector3(0.045f, 0.15f, 0.12f), coat, new Vector3(0, 0, -15));
                Part(new TorusMesh { InnerRadius = 0.06f, OuterRadius = 0.085f }, new Vector3(0, 0.02f, 0.06f), Material(colour), new Vector3(70, 0, 0));
                foreach (var (wing, side) in new[] { (leftWing, -1), (rightWing, 1) })
                {
                    wing.Position = new Vector3(side * 0.06f, 0.03f, 0);
                    body.AddChild(wing);
                    Part(new BoxMesh { Size = new Vector3(0.36f, 0.012f, 0.2f) }, new Vector3(side * 0.18f, 0, -0.02f), membrane, parent: wing);
                    Part(new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.014f, Height = 0.38f }, new Vector3(side * 0.18f, 0.005f, 0.08f), coat, new Vector3(0, 0, 90), parent: wing);
                }

                chestHeight = 0;
                handHeight = 0.04f;
                handReach = 0.2f;
            }

            /// <summary>A scatter of small bodies over its ground, churning, each a tiny beast.</summary>
            private void Swarm(Combatant creature, Color colour)
            {
                var random = new Random(creature.Name.GetHashCode());
                var coat = Material(Coat(shape.Head));
                var marked = Material(colour);
                for (var i = 0; i < 14; i++)
                {
                    var home = new Vector3((float)random.NextDouble() - 0.5f, 0.05f, (float)random.NextDouble() - 0.5f) * 0.85f;
                    var bit = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.04f, Height = 0.14f }, MaterialOverride = i % 5 == 0 ? marked : coat, Position = home, RotationDegrees = new Vector3(90, random.Next(360), 0) };
                    body.AddChild(bit);
                    swarm.Add((bit, home with { Y = 0.04f }, (float)(random.NextDouble() * Math.PI * 2)));
                }

                chestHeight = 0.08f;
                handHeight = 0.08f;
                handReach = 0.3f;
            }

            /// <summary>A body of ghostlight from the waist up, trailing away below, adrift a little off the ground.</summary>
            private void Spirit(Combatant creature, Color colour)
            {
                var glow = Ghostly(GhostLight);
                var band = Ghostly(colour);
                var bulk = shape.Hulking ? 0.32f : 0.26f;
                hover = 0.12f;
                arm.Position = Shoulder;
                body.AddChild(arm);
                offArm.Position = Shoulder with { X = -Shoulder.X };
                body.AddChild(offArm);
                Part(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.025f, Height = 0.32f }, new Vector3(0, -0.15f, 0), glow, parent: offArm);
                Part(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.025f, Height = 0.32f }, new Vector3(0, -0.13f, 0.04f), glow, new Vector3(15, 0, 0), parent: arm);
                Part(new CapsuleMesh { Radius = bulk / 2, Height = 0.5f }, new Vector3(0, 0.68f, 0), glow);
                Part(new CylinderMesh { TopRadius = bulk / 2, BottomRadius = 0.01f, Height = 0.5f }, new Vector3(0, 0.3f, -0.04f), glow, new Vector3(-12, 0, 0)); // the trail below
                Part(new TorusMesh { InnerRadius = bulk / 2 - 0.01f, OuterRadius = bulk / 2 + 0.025f }, new Vector3(0, 0.6f, 0), band);
                Part(new SphereMesh { Radius = 0.12f, Height = 0.24f }, new Vector3(0, 1.04f, 0), glow);
                Part(new BoxMesh { Size = new Vector3(0.14f, 0.025f, 0.02f) }, new Vector3(0, 1.05f, 0.115f), Material(new Color(0.15f, 0.25f, 0.4f))); // eyes, a dark slot
                if (creature.Attacks.Any(a => a.Name != "Unarmed") && !NaturalWeapon(creature))
                {
                    Part(new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.55f) }, new Vector3(0.2f, 0.6f, 0.08f) - Shoulder + new Vector3(0, 0.02f, 0.28f), Ghostly(GhostLight.Lightened(0.3f)), parent: arm);
                }
            }

            // ---- Posing ----

            /// <summary>
            /// Lays this moment's pose on the body's limbs: a walk's stride, the swing of a blow, a
            /// reach, a kick — each read for the body it's laid on.
            /// </summary>
            /// <param name="step">The stride, from −1 to 1, times how far into walking it is.</param>
            private void PoseLimbs(float step, float dt, bool down)
            {
                time += dt;
                switch (shape.Plan)
                {
                    case BodyPlan.Quadruped:
                    {
                        // A trot: the diagonal pairs swing together. A blow is a lunge of the head — reared
                        // back at the wind-up, driven down and forward through the bite; a grab, jaws
                        // closing; a trip, a forepaw raked through.
                        var stride = down ? 0 : 28 * step;
                        foreLeft.RotationDegrees = new Vector3(-stride, 0, 0);
                        rightHip.RotationDegrees = new Vector3(-stride, 0, 0);
                        foreRight.RotationDegrees = new Vector3(stride - Kick, 0, 0);
                        leftHip.RotationDegrees = new Vector3(stride, 0, 0);
                        neck.RotationDegrees = new Vector3(Mathf.Clamp(0.45f * Swing - 0.2f * OffSwing, -35, 30), 0, 0);
                        tail.RotationDegrees = new Vector3(0, 12 * Mathf.Sin(time * 3 + stridePhase), 0);
                        break;
                    }

                    case BodyPlan.Winged:
                    {
                        // Wings beat all the while it's up, harder as it strikes; down, they lie spread.
                        var effort = 1 + Mathf.Abs(Swing) / 60;
                        var beat = down ? 0 : 40 * Mathf.Sin(time * 14 * effort);
                        leftWing.RotationDegrees = new Vector3(0, 0, -beat);
                        rightWing.RotationDegrees = new Vector3(0, 0, beat);
                        break;
                    }

                    case BodyPlan.Swarm:
                    {
                        // A churn that never settles; a blow is the whole of it surging forward.
                        var surge = Mathf.Max(0, Swing) / 40 + Mathf.Max(0, -OffSwing) / 80;
                        foreach (var (bit, home, phase) in swarm)
                        {
                            var wander = new Vector3(Mathf.Sin(time * 3 + phase), 0, Mathf.Cos(time * 2.3f + phase * 1.7f)) * 0.06f;
                            bit.Position = home + wander + Vector3.Back * 0.25f * surge * (0.5f + 0.5f * Mathf.Sin(phase));
                            bit.RotationDegrees = bit.RotationDegrees with { Y = Mathf.RadToDeg(phase) + 40 * Mathf.Sin(time * 2 + phase) };
                        }

                        break;
                    }

                    case BodyPlan.Spirit:
                        offArm.RotationDegrees = new Vector3(OffSwing + 8 * Mathf.Sin(time * 1.3f), 0, 0);
                        arm.RotationDegrees = new Vector3(Swing + 8 * Mathf.Sin(time * 1.3f + 1), 0, 0);
                        break;

                    default:
                    {
                        if (Riding)
                        {
                            // Astride: legs spread round the horse's flanks, bent forward at the hip.
                            leftHip.RotationDegrees = new Vector3(-35, 0, 38);
                            rightHip.RotationDegrees = new Vector3(-35 - Kick, 0, -38);
                        }
                        else
                        {
                            leftHip.RotationDegrees = new Vector3(32 * step, 0, 0);
                            rightHip.RotationDegrees = new Vector3(-32 * step - Kick, 0, 0);
                        }

                        // A hand kept on whoever it holds, for as long as the hold lasts.
                        var holding = creature.Holding is not null && !down ? -70f : 0f;
                        offArm.RotationDegrees = new Vector3(OffSwing + holding - 22 * step, 0, 0);
                        arm.RotationDegrees = new Vector3(Swing + 12 * step, 0, 0);
                        tail.RotationDegrees = new Vector3(0, 15 * Mathf.Sin(time * 2.5f), 0);
                        break;
                    }
                }
            }

            /// <summary>Up on a horse, and not down off it.</summary>
            private bool Riding => creature.IsRiding && !shownDown;

            /// <summary>How far a rider sinks from standing on the horse's back to sitting astride it, in the body's own units.</summary>
            private float Seat => Riding ? -0.33f : 0;

            /// <summary>How it lies when down: a person falls on their back, a beast rolls on its side, a bat drops flat.</summary>
            private Vector3 DownTilt => shape.Plan switch
            {
                BodyPlan.Quadruped => new Vector3(0, 0, 80),
                BodyPlan.Winged or BodyPlan.Swarm => Vector3.Zero,
                _ => new Vector3(85, 0, 0),
            };

            /// <summary>How high it holds itself this moment: a bat bobbing on its wingbeats and dropping into a dive, a spirit drifting.</summary>
            private float HoverNow(bool down) => down || hover <= 0 ? (shape.Plan == BodyPlan.Winged ? 0.04f : 0)
                : shape.Plan == BodyPlan.Winged ? hover + 0.05f * Mathf.Sin(time * 7) - 0.4f * Mathf.Max(0, Swing) / 40
                : hover + 0.04f * Mathf.Sin(time * 1.6f);
        }
    }
}
