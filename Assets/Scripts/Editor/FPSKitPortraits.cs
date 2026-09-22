#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Draws a portrait for each of the villain's children.
    ///
    /// <b>They are surveillance stills, and that is a design decision rather than an
    /// apology for having no art.</b> This project has no character models and no
    /// photographs; the honest options were a flat colour plate with an initial on it,
    /// which reads as a missing asset, or a picture that explains its own crudeness. One
    /// of the eight runs every camera the company owns, and the player is somebody those
    /// cameras have been watching for twenty years -- so a low, grainy, duotone frame
    /// with a timecode on it is both the cheapest thing to draw and the most diegetic
    /// thing to show. A beat card with no face is a paragraph on a dark screen, and six
    /// of those in a row are six paragraphs nobody tells apart.
    ///
    /// Written the way <see cref="FPSKitTextures"/> is, and for the same reasons:
    ///
    /// - <b>A pure function of constants</b>, so the PNG bytes are identical on every
    ///   run and git sees no change. There is therefore no "regenerate the portraits"
    ///   step to forget, unlike the theme, roster, level, store and campaign generators.
    /// - <b>Written as PNGs and imported</b> rather than created as Texture2D assets, so
    ///   the importer settles the compression and the sprite settings rather than
    ///   whatever the code happened to allocate.
    /// - <b>Seeded per sibling</b>, so the eight faces differ from each other in build,
    ///   posture and framing without anybody choosing eight sets of numbers -- and the
    ///   same sibling is the same face every time, which is the point of a portrait.
    /// </summary>
    public static class FPSKitPortraits
    {
        public const string PortraitFolder = "Assets/FPSKit_Generated/Portraits";

        /// <summary>
        /// Small on purpose. It is shown at about 320 reference units on the widest
        /// screen this game runs on, and a coarse frame is what sells the camera.
        /// </summary>
        const int Width = 384, Height = 512;

        [MenuItem("FPSKit/Generate Portraits", false, 50)]
        public static void GenerateMenu()
        {
            int made = GenerateAll();

            EditorUtility.DisplayDialog("FPSKit Portraits",
                $"{made} portraits are in:\n{PortraitFolder}\n\n" +
                "They are redrawn from constants every time, so they never need " +
                "resetting -- run FPSKit > Reset Campaign to Defaults to point the " +
                "siblings at them.", "OK");
        }

        /// <summary>
        /// Draws one portrait per name and returns how many were written. Called by the
        /// campaign generator, so a campaign reset always has faces to wire.
        /// </summary>
        public static int GenerateAll(string[] names = null)
        {
            names ??= DefaultNames;

            EnsureFolders();

            for (int i = 0; i < names.Length; i++)
                Write(names[i], i);

            AssetDatabase.Refresh();
            return names.Length;
        }

        /// <summary>
        /// The eight, matching <see cref="FPSKitCampaign"/>'s sibling list. Kept here as
        /// a fallback only -- the campaign generator passes its own names in, so the two
        /// cannot drift into eight portraits for seven people.
        /// </summary>
        static readonly string[] DefaultNames =
        {
            "Tove Auger", "Kestrel Auger", "Aurel Auger", "Ilsa Auger",
            "Dev Auger", "Roan Auger", "Marit Auger", "the eighth name"
        };

        public static string PathFor(string name)
            => $"{PortraitFolder}/{Safe(name)}.png";

        public static Texture2D Load(string name)
            => AssetDatabase.LoadAssetAtPath<Texture2D>(PathFor(name));

        // ==================================================================
        static void Write(string name, int index)
        {
            var tex = Draw(index);

            File.WriteAllBytes(PathFor(name), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(PathFor(name), ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(PathFor(name)) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;

            // No compression. These are shown at nearly their native size on a flat
            // panel, where a block artefact across a face is the one thing the eye is
            // built to notice, and eight small textures cost nothing either way.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            importer.SaveAndReimport();
        }

        /// <summary>
        /// One frame: a body lit from one side against a dark room, through a lens that
        /// is not very good.
        ///
        /// Drawn as fields rather than as shapes -- for each pixel, how far inside the
        /// silhouette it is and how much light reaches it -- because a silhouette made of
        /// hard shapes reads as clip art, and the whole effect here depends on the edge
        /// being soft and slightly wrong.
        /// </summary>
        static Texture2D Draw(int index)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[Width * Height];

            // Everything below is in units of the frame's HEIGHT, with x corrected for
            // the aspect, so a radius means the same distance in both directions. The
            // first version divided an x measured across the full width by the same
            // number as a y measured across the full height, which at 384x512 makes every
            // circle three and a half times taller than it is wide: the head came out as
            // a tall egg running off the top of the frame with a dome under it. It did
            // not read as a person at all, and no amount of tuning the numbers would have
            // fixed it, because the units were wrong rather than the values.
            const float Aspect = Width / (float)Height;

            float seed = index * 7.31f;

            float build = 0.255f + 0.045f * Mathf.Sin(seed * 1.7f);          // shoulder width
            float headR = 0.101f + 0.010f * Mathf.Sin(seed * 2.3f + 1.1f);   // head radius
            float headY = 0.640f + 0.022f * Mathf.Sin(seed * 1.3f + 2.2f);   // how high they sit
            float lean = 0.030f * Mathf.Sin(seed * 3.1f + 0.4f);             // off the vertical

            // Which side the room's one light is on, alternating so two cards shown one
            // after another are not the same photograph twice.
            float light = (index % 2 == 0) ? -1f : 1f;

            var cold = new Vector3(0.26f, 0.31f, 0.40f);
            var warm = new Vector3(0.96f, 0.82f, 0.60f);

            for (int y = 0; y < Height; y++)
            {
                float ny = y / (float)(Height - 1);

                for (int x = 0; x < Width; x++)
                {
                    float nx = (x / (float)(Width - 1) - 0.5f) * Aspect;

                    float body = BodyField(nx, ny, build, headR, headY, lean);

                    // The room behind: darker towards the top, with the light's own bloom
                    // low on one side.
                    float glow = Mathf.Clamp01(1f - Dist(nx - light * 0.30f, ny - 0.34f) * 1.9f);
                    float room = 0.075f + 0.070f * (1f - ny) + 0.12f * glow * glow;

                    // How much of that one light the figure catches. This is the whole
                    // model: a body is lit on the side facing the lamp and not on the
                    // other, and the line between the two is what makes it read as solid.
                    float facing = Mathf.Clamp01(0.5f + 0.5f * (nx * light) / 0.26f);
                    float lit = Mathf.Clamp01(facing * facing * 1.25f);

                    // A bright edge where the silhouette ends on the lit side -- the one
                    // thing that stops a dark figure on a dark ground being a blob.
                    //
                    // It has to be an *edge*. At the first multiplier this was a band a
                    // fifth of the frame wide down one side, which is not a rim light, it
                    // is a glowing outline -- and an outline turns a silhouette into a
                    // pictogram, which is how the first draft came out looking like a
                    // keyhole. The number is large because the field it measures is soft
                    // on purpose.
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(body - 0.5f) * 26f);
                    float rim = edge * Mathf.Clamp01(facing * 1.6f - 0.35f);

                    float inside = Mathf.SmoothStep(0f, 1f, body);

                    // Not black on the unlit side. A body that falls to zero has no form
                    // in it at all and reads as a hole cut in the picture; a little fill
                    // light is what lets the far shoulder still be a shoulder.
                    float tone = Mathf.Lerp(room, 0.105f + 0.40f * lit, inside);
                    tone += rim * 0.50f;

                    var colour = Vector3.Lerp(cold, warm, Mathf.Clamp01(lit * 0.8f + rim * 0.7f));
                    colour *= tone;

                    // The lens. Grain, then scanline, then vignette, in that order -- a
                    // vignette applied before the grain darkens the noise along with the
                    // corners, and the frame stops looking like one exposure.
                    float grain = (Hash(x * 3 + index * 977, y * 5 + index * 131) - 0.5f) * 0.075f;
                    float scan = 1f - 0.05f * Mathf.Abs(Mathf.Sin(y * 1.15f));
                    float vignette = 1f - 0.55f * Mathf.Clamp01(
                        Dist(nx * 1.5f, (ny - 0.5f) * 1.7f) - 0.34f);

                    colour = (colour + new Vector3(grain, grain, grain)) * (scan * vignette);

                    pixels[y * Width + x] = new Color32(
                        Byte(colour.x), Byte(colour.y), Byte(colour.z), 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// How far inside the figure a point is: below 0.5 outside, above it within, with
        /// the crossing soft because the edge is what sells the lens.
        ///
        /// A head, a neck and a pair of shoulders is the whole anatomy a portrait at this
        /// size has, and the shoulders run off the bottom of the frame, because a figure
        /// that ends inside its own picture is a cut-out rather than a photograph.
        ///
        /// <b>The shoulders are a rounded box, not a dome.</b> A curve that falls away
        /// from the centre is a hill, and a hill with a ball on top of it is a snowman --
        /// which is exactly what the first attempt drew. What makes a pair of shoulders
        /// read as a pair of shoulders is that the top of them is nearly *flat* between
        /// two rounded deltoids, so the silhouette has two corners in it and the neck
        /// meets the flat rather than the summit.
        /// </summary>
        static float BodyField(float nx, float ny, float build, float headR, float headY,
                               float lean)
        {
            // The lean tilts the whole figure about the shoulders rather than sliding it.
            float x = nx - lean * (ny - 0.42f);

            // Head: an ellipse a little taller than it is wide, as a skull is.
            float hx = x / headR;
            float hy = (ny - headY) / (headR * 1.20f);
            float head = 1f - Dist(hx, hy);

            float shoulderY = headY - headR * 1.62f;

            // Neck: short and thick. A thin one at this scale is a stick, and a stick is
            // the other half of what made the first draft a lollipop.
            float neck = ny < headY && ny > shoulderY - 0.02f
                ? 1f - Mathf.Abs(x) / (headR * 0.60f)
                : -1f;

            // Shoulders and torso: a box with rounded top corners, widening a little as
            // it falls so the figure is cut off by the bottom edge rather than tapering
            // away from it.
            float spread = build * (1f + 0.16f * Mathf.Clamp01((shoulderY - ny) / 0.45f));
            float corner = build * 0.60f;

            float qx = Mathf.Abs(x) - (spread - corner);
            float qy = ny - (shoulderY - corner);

            // Standard rounded-box distance, with only the top corners rounded because qy
            // is only ever positive above the box's own top.
            float outside = Dist(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);

            float shoulders = corner - (outside + inside);

            float field = Mathf.Max(head, Mathf.Max(neck, shoulders));

            // Scaled so the transition is a couple of pixels wide: soft enough to be a
            // lens rather than a stencil, tight enough that the rim above is a line.
            return Mathf.Clamp01(0.5f + field * 6.5f);
        }

        static float Dist(float x, float y) => Mathf.Sqrt(x * x + y * y);

        static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>Deterministic value noise. Same input, same byte, every run.</summary>
        static float Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        static string Safe(string name) => name.Replace(" ", "_");

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(PortraitFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Portraits");
        }
    }
}
#endif
