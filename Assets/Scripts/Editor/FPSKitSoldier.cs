#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The enemy soldier's shapes and cloth: smooth lofted meshes for every body segment
    /// and piece of kit, a camouflage weave for the uniform, and a worn texture for the gear.
    ///
    /// <b>A loft, not a capsule.</b> A limb is a column of elliptical rings, each with its
    /// own width, depth and forward offset, so a thigh can be thick at the top and taper to
    /// the knee, a calf can bulge behind the shin, a shoulder can carry a deltoid and a
    /// torso can be broad at the chest and narrow at the waist. That silhouette -- not
    /// detail -- is most of what separates a person from a mannequin at twenty metres.
    ///
    /// Every mesh hangs from its pivot the way the joints expect: limbs run down local -Y
    /// from the joint at the origin, the torso and head run up +Y from the hips and neck.
    /// The hitboxes stay the simple primitives they were; these are drawn and never hit.
    ///
    /// Meshes are saved as assets (a prefab cannot hold a mesh that only exists in memory)
    /// and saved over in place, so their GUIDs, and every prefab pointing at them, survive
    /// a rebuild. Textures are grayscale and pure functions of a seed, like FPSKitTextures.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        const string SoldierFolder = "Assets/FPSKit_Generated/Soldier";

        /// <summary>One cross-section of a loft: height along the axis, half-width, half-depth, forward shift.</summary>
        struct Ring
        {
            public float y, rx, rz, cz;
            public Ring(float y, float rx, float rz, float cz = 0f) { this.y = y; this.rx = rx; this.rz = rz; this.cz = cz; }
        }

        static Ring R(float y, float rx, float rz, float cz = 0f) => new Ring(y, rx, rz, cz);

        /// <summary>A pole: the rounded end of a loft, closing it to a point on the axis.</summary>
        static Ring Pole(float y, float cz = 0f) => new Ring(y, 0f, 0f, cz);

        // ==================================================================
        // Shapes
        // ==================================================================

        /// <summary>Hips to the base of the neck: broad at the chest, in at the waist.</summary>
        static Mesh TorsoMesh() => Loft("Soldier_Torso", 18,
            Pole(-0.1f), R(-0.08f, 0.1f, 0.08f), R(-0.03f, 0.165f, 0.115f), R(0.04f, 0.172f, 0.12f),
            R(0.13f, 0.158f, 0.108f, 0.005f), R(0.22f, 0.16f, 0.108f, 0.01f), R(0.32f, 0.182f, 0.12f, 0.01f),
            R(0.42f, 0.2f, 0.128f, 0.012f), R(0.5f, 0.21f, 0.12f, 0.005f), R(0.555f, 0.19f, 0.1f),
            R(0.595f, 0.12f, 0.075f), R(0.62f, 0.06f, 0.055f), Pole(0.63f));

        /// <summary>Neck to crown, with a jaw that comes forward.</summary>
        static Mesh HeadMesh() => Loft("Soldier_Head", 16,
            Pole(-0.02f), R(0f, 0.052f, 0.056f), R(0.06f, 0.058f, 0.062f, 0.005f),
            R(0.1f, 0.072f, 0.085f, 0.018f), R(0.14f, 0.083f, 0.098f, 0.014f), R(0.2f, 0.093f, 0.106f, 0.008f),
            R(0.26f, 0.094f, 0.104f), R(0.31f, 0.076f, 0.084f, -0.004f), R(0.34f, 0.042f, 0.048f, -0.006f),
            Pole(0.355f, -0.006f));

        /// <summary>A scarf over the jaw and mouth, the way men in the field wear one.</summary>
        static Mesh MaskMesh() => Loft("Soldier_Mask", 16,
            Pole(0.02f), R(0.03f, 0.068f, 0.072f, 0.004f), R(0.07f, 0.074f, 0.082f, 0.012f),
            R(0.12f, 0.087f, 0.104f, 0.02f), R(0.17f, 0.094f, 0.111f, 0.015f), Pole(0.172f, 0.015f));

        static Mesh HelmetMesh() => Loft("Soldier_Helmet", 18,
            Pole(0.18f), R(0.185f, 0.122f, 0.135f), R(0.205f, 0.126f, 0.14f, -0.005f),
            R(0.25f, 0.122f, 0.136f, -0.006f), R(0.3f, 0.106f, 0.118f, -0.006f), R(0.34f, 0.074f, 0.084f, -0.005f),
            R(0.365f, 0.036f, 0.04f, -0.004f), Pole(0.372f, -0.004f));

        /// <summary>Shoulder to elbow: the deltoid at the top, the arm thinning to the elbow.</summary>
        static Mesh UpperArmMesh() => Loft("Soldier_UpperArm", 14,
            Pole(0.045f), R(0.03f, 0.042f, 0.046f), R(0f, 0.056f, 0.056f), R(-0.05f, 0.056f, 0.054f, 0.004f),
            R(-0.13f, 0.05f, 0.05f, 0.005f), R(-0.22f, 0.044f, 0.045f), R(-0.29f, 0.04f, 0.041f),
            R(-0.315f, 0.03f, 0.03f), Pole(-0.325f));

        static Mesh ForearmMesh() => Loft("Soldier_Forearm", 14,
            Pole(0.03f), R(0.015f, 0.036f, 0.036f), R(-0.02f, 0.042f, 0.044f), R(-0.07f, 0.046f, 0.043f, 0.004f),
            R(-0.15f, 0.037f, 0.034f), R(-0.23f, 0.03f, 0.026f), R(-0.255f, 0.028f, 0.025f), Pole(-0.262f));

        /// <summary>A gloved hand, loosely closed: flat across the palm, rounded at the knuckles.</summary>
        static Mesh HandMesh() => Loft("Soldier_Hand", 12,
            Pole(-0.235f), R(-0.245f, 0.03f, 0.022f), R(-0.28f, 0.042f, 0.026f, 0.005f),
            R(-0.325f, 0.043f, 0.03f, 0.012f), R(-0.35f, 0.035f, 0.028f, 0.014f), Pole(-0.362f, 0.012f));

        /// <summary>Hip to knee: heavy at the top, the quadriceps forward, narrowing to the knee.</summary>
        static Mesh ThighMesh() => Loft("Soldier_Thigh", 16,
            Pole(0.06f), R(0.04f, 0.07f, 0.075f), R(0f, 0.088f, 0.092f), R(-0.08f, 0.092f, 0.096f, 0.008f),
            R(-0.2f, 0.084f, 0.086f, 0.01f), R(-0.33f, 0.071f, 0.071f, 0.006f), R(-0.43f, 0.06f, 0.06f),
            R(-0.48f, 0.057f, 0.058f), Pole(-0.5f));

        /// <summary>Knee to ankle: a kneecap in front, the calf behind, thin at the ankle.</summary>
        static Mesh ShinMesh() => Loft("Soldier_Shin", 16,
            Pole(0.035f), R(0.015f, 0.055f, 0.056f, 0.004f), R(-0.02f, 0.058f, 0.06f, 0.008f),
            R(-0.11f, 0.056f, 0.066f, -0.012f), R(-0.2f, 0.051f, 0.058f, -0.01f), R(-0.32f, 0.04f, 0.042f, -0.002f),
            R(-0.42f, 0.036f, 0.038f), Pole(-0.45f));

        /// <summary>A boot, lofted heel to toe along +Y and turned to lie along the foot.</summary>
        static Mesh BootMesh() => Loft("Soldier_Boot", 14,
            Pole(-0.085f), R(-0.075f, 0.045f, 0.05f), R(-0.03f, 0.052f, 0.056f), R(0.04f, 0.052f, 0.05f, 0.004f),
            R(0.11f, 0.05f, 0.04f, 0.01f), R(0.16f, 0.044f, 0.03f, 0.014f), Pole(0.18f, 0.016f));

        /// <summary>The plate carrier: over the chest and back, square-shouldered, stopping above the belt.</summary>
        static Mesh VestMesh() => Loft("Soldier_Vest", 18,
            Pole(0.12f), R(0.13f, 0.185f, 0.13f, 0.01f), R(0.2f, 0.185f, 0.132f, 0.014f),
            R(0.32f, 0.2f, 0.142f, 0.016f), R(0.42f, 0.215f, 0.145f, 0.016f), R(0.5f, 0.215f, 0.132f, 0.008f),
            R(0.535f, 0.19f, 0.11f, 0.004f), Pole(0.54f));

        static Mesh PackMesh() => Loft("Soldier_Pack", 12,
            Pole(0.18f), R(0.19f, 0.12f, 0.05f), R(0.28f, 0.14f, 0.065f), R(0.45f, 0.14f, 0.07f),
            R(0.53f, 0.12f, 0.055f), Pole(0.545f));

        static Mesh KneePadMesh() => Loft("Soldier_KneePad", 12,
            Pole(0.05f), R(0.04f, 0.035f, 0.02f), R(0f, 0.05f, 0.03f), R(-0.06f, 0.048f, 0.028f),
            R(-0.085f, 0.03f, 0.02f), Pole(-0.09f));

        // ==================================================================
        // The loft
        // ==================================================================

        /// <summary>
        /// Rings of an ellipse stacked along Y, stitched into a smooth closed surface. A ring
        /// with zero radius is a pole. UVs run around the ring and down the length in metres,
        /// so the uniform's weave is the same size on an arm and a torso.
        /// </summary>
        static Mesh Loft(string name, int sides, params Ring[] rings)
        {
            int columns = sides + 1;
            var vertices = new Vector3[rings.Length * columns];
            var uvs = new Vector2[vertices.Length];
            var triangles = new System.Collections.Generic.List<int>();

            float length = 0f;

            for (int r = 0; r < rings.Length; r++)
            {
                if (r > 0)
                {
                    var a = rings[r - 1];
                    var b = rings[r];
                    length += Mathf.Sqrt((b.y - a.y) * (b.y - a.y) + (b.rx - a.rx) * (b.rx - a.rx));
                }

                for (int c = 0; c < columns; c++)
                {
                    float angle = c / (float)sides * Mathf.PI * 2f;
                    var ring = rings[r];

                    vertices[r * columns + c] = new Vector3(Mathf.Sin(angle) * ring.rx, ring.y,
                                                            Mathf.Cos(angle) * ring.rz + ring.cz);
                    uvs[r * columns + c] = new Vector2(c / (float)sides, length * 2.5f);
                }
            }

            // Wound so the outside faces out whichever way the rings run.
            bool upward = rings[rings.Length - 1].y > rings[0].y;

            for (int r = 0; r < rings.Length - 1; r++)
            {
                for (int c = 0; c < sides; c++)
                {
                    int i0 = r * columns + c, i1 = i0 + 1, j0 = i0 + columns, j1 = j0 + 1;

                    if (upward)
                    {
                        triangles.Add(i0); triangles.Add(i1); triangles.Add(j0);
                        triangles.Add(i1); triangles.Add(j1); triangles.Add(j0);
                    }
                    else
                    {
                        triangles.Add(i0); triangles.Add(j0); triangles.Add(i1);
                        triangles.Add(i1); triangles.Add(j0); triangles.Add(j1);
                    }
                }
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            WeldSeamNormals(mesh, rings.Length, columns);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return SaveMesh(mesh);
        }

        /// <summary>
        /// The first and last column of every ring are the same point with different UVs;
        /// RecalculateNormals shades them separately and leaves a crease down the back of
        /// every limb. Averaged here, the seam disappears.
        /// </summary>
        static void WeldSeamNormals(Mesh mesh, int rings, int columns)
        {
            var normals = mesh.normals;

            for (int r = 0; r < rings; r++)
            {
                int first = r * columns, last = first + columns - 1;
                var n = (normals[first] + normals[last]).normalized;
                normals[first] = normals[last] = n;
            }

            mesh.normals = normals;
        }

        /// <summary>Saved over the existing asset in place, so every reference to it survives.</summary>
        static Mesh SaveMesh(Mesh mesh)
        {
            if (!AssetDatabase.IsValidFolder(SoldierFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Soldier");

            string path = $"{SoldierFolder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ==================================================================
        // Cloth
        // ==================================================================

        /// <summary>
        /// Three-tone disruptive camouflage over a ripstop grid. Grayscale: the archetype's
        /// colour is multiplied over it, so every enemy type wears the same pattern in its
        /// own shade.
        /// </summary>
        static Texture2D UniformTexture() => SoldierTexture("Uniform", 256, (u, v) =>
        {
            float big = Fbm2(u * 5f, v * 5f, 4101, 4);
            float small = Fbm2(u * 11f + 3.1f, v * 11f, 4202, 3);

            float tone = big > 0.12f ? 0.62f : big < -0.14f ? 1f : 0.82f;
            if (small > 0.22f) tone = 0.5f;

            // The weave: faint lines every few pixels, heavier every sixteen.
            int x = Mathf.FloorToInt(u * 256f), y = Mathf.FloorToInt(v * 256f);
            float grid = (x % 16 == 0 || y % 16 == 0) ? 0.9f : ((x + y) % 2 == 0 ? 0.97f : 1f);

            return tone * grid * (0.94f + 0.06f * Fbm2(u * 64f, v * 64f, 4303, 2));
        });

        /// <summary>Scuffed nylon and polymer: nearly flat, with wear.</summary>
        static Texture2D GearTexture() => SoldierTexture("Gear", 128, (u, v) =>
            0.82f + 0.18f * Fbm2(u * 18f, v * 18f, 5101, 3) + ((Mathf.FloorToInt(v * 128f) % 6 == 0) ? -0.06f : 0f));

        static Texture2D SoldierTexture(string name, int size, System.Func<float, float, float> shade)
        {
            if (!AssetDatabase.IsValidFolder(SoldierFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Soldier");

            string path = $"{SoldierFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade(x / (float)size, y / (float)size)) * 255f);
                    pixels[y * size + x] = new Color32(g, g, g, 255);
                }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Puts a texture on a material the builder made, without disturbing anything else on it.</summary>
        static void Dress(Material material, Texture2D texture, float smoothness)
        {
            if (material == null) return;

            if (texture != null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
        }

        /// <summary>A drawn piece of the soldier: never collided with, never a hitbox.</summary>
        static MeshRenderer Visual(Transform parent, string name, Mesh mesh, Material material,
                                   Vector3 position, Vector3 euler, bool gear, Vector3? scale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale ?? Vector3.one;
            go.layer = LayerMask.NameToLayer("Enemy");
            go.tag = gear ? "Metal" : "Flesh";

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        /// <summary>
        /// Turns a hitbox primitive into a collider only: its own capsule or ball stops being
        /// drawn, and the lofted segment drawn over it is what gets stained when it is shot.
        /// </summary>
        static void HideUnder(GameObject hitboxPart, Renderer visual)
        {
            var hitbox = hitboxPart.GetComponent<Hitbox>();
            if (hitbox != null) hitbox.visual = visual;

            Object.DestroyImmediate(hitboxPart.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(hitboxPart.GetComponent<MeshFilter>());
        }
    }
}
#endif
