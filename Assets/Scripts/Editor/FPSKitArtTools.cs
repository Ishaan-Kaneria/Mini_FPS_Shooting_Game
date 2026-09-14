#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Turns a downloaded art pack into a themed level with no hand-placement.
    ///
    /// Point it at a folder of prefabs and it fills the theme's propPrefabs list;
    /// point it at an HDRI and it builds a panoramic skybox material and wires the
    /// sun to match. Rebuild the scene afterwards and the grey boxes are replaced
    /// by real geometry.
    ///
    /// Menu: FPSKit > Art Pack Setup
    /// </summary>
    public class FPSKitArtTools : EditorWindow
    {
        private LevelTheme _theme;
        private DefaultAsset _propFolder;
        private Texture _hdri;

        private int _maxProps = 24;
        private float _minPropSize = 0.5f;
        private float _maxPropSize = 12f;
        private bool _replaceExisting = true;

        private Vector2 _scroll;
        private string _status = string.Empty;

        [MenuItem("FPSKit/Art Pack Setup", false, 60)]
        public static void Open()
        {
            var window = GetWindow<FPSKitArtTools>(true, "FPSKit Art Pack Setup", true);
            window.minSize = new Vector2(430f, 470f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Art Pack Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. Import an art pack into Assets/ (Asset Store, Fab, Kenney, Sketchfab...)\n" +
                "2. Pick the theme to upgrade and the folder holding the pack's prefabs\n" +
                "3. Apply, then rebuild that theme's scene from the FPSKit menu",
                MessageType.Info);

            EditorGUILayout.Space();
            _theme = (LevelTheme)EditorGUILayout.ObjectField("Theme", _theme, typeof(LevelTheme), false);

            if (_theme == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a theme asset from Assets/FPSKit_Generated/Themes.\n" +
                    "Run FPSKit > Create Theme Assets first if the folder is empty.",
                    MessageType.Warning);

                if (GUILayout.Button("Create Theme Assets")) FPSKitThemes.GetOrCreateAll();

                EditorGUILayout.EndScrollView();
                return;
            }

            // ---- props ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Props", EditorStyles.boldLabel);

            _propFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Prefab Folder", _propFolder, typeof(DefaultAsset), false);

            _maxProps = EditorGUILayout.IntSlider("Max Props", _maxProps, 1, 200);
            EditorGUILayout.MinMaxSlider(
                new GUIContent($"Size Filter ({_minPropSize:0.0}m - {_maxPropSize:0.0}m)"),
                ref _minPropSize, ref _maxPropSize, 0.1f, 30f);
            _replaceExisting = EditorGUILayout.Toggle("Replace Existing List", _replaceExisting);

            using (new EditorGUI.DisabledScope(_propFolder == null))
            {
                if (GUILayout.Button("Scan Folder and Assign Props", GUILayout.Height(26f)))
                    ScanProps();
            }

            int current = _theme.propPrefabs != null ? _theme.propPrefabs.Length : 0;
            EditorGUILayout.LabelField($"Theme currently holds {current} prop prefab(s).");

            // ---- surfaces ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Surfaces", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The floor, walls and pillars are generated geometry, so they stay flat grey " +
                "until you give them a material. This is usually the single biggest step up " +
                "in how real the level looks.", MessageType.None);

            using (new EditorGUI.DisabledScope(_propFolder == null))
            {
                if (GUILayout.Button("Auto-detect Surface Materials in Folder", GUILayout.Height(22f)))
                    AutoDetectSurfaces();
            }

            EditorGUI.BeginChangeCheck();

            var floor = (Material)EditorGUILayout.ObjectField("Floor", _theme.floorMaterial, typeof(Material), false);
            var wall = (Material)EditorGUILayout.ObjectField("Walls", _theme.wallMaterial, typeof(Material), false);
            var cover = (Material)EditorGUILayout.ObjectField("Cover / Pillars", _theme.coverMaterial, typeof(Material), false);
            var crate = (Material)EditorGUILayout.ObjectField("Crates", _theme.crateMaterial, typeof(Material), false);

            float floorSize = EditorGUILayout.Slider("Floor Texture Size (m)", _theme.floorTextureSize, 0.5f, 20f);
            float wallSize = EditorGUILayout.Slider("Wall Texture Size (m)", _theme.wallTextureSize, 0.5f, 20f);
            int propCount = EditorGUILayout.IntSlider("Props To Place", _theme.propCount, 0, 200);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_theme, "Edit theme surfaces");

                _theme.floorMaterial = floor;
                _theme.wallMaterial = wall;
                _theme.coverMaterial = cover;
                _theme.crateMaterial = crate;
                _theme.floorTextureSize = floorSize;
                _theme.wallTextureSize = wallSize;
                _theme.propCount = propCount;

                EditorUtility.SetDirty(_theme);
            }

            // ---- sky ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sky", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "An equirectangular HDRI gives realistic sky and ambient light in one step. " +
                "Poly Haven publishes them CC0. Set the texture's Shape to Cube in its import " +
                "settings for the sharpest result.", MessageType.None);

            _hdri = (Texture)EditorGUILayout.ObjectField("HDRI / Panorama", _hdri, typeof(Texture), false);

            using (new EditorGUI.DisabledScope(_hdri == null))
            {
                if (GUILayout.Button("Build Skybox and Assign", GUILayout.Height(26f)))
                    BuildSkybox();
            }

            if (_theme.skyboxOverride != null)
                EditorGUILayout.LabelField($"Skybox override: {_theme.skyboxOverride.name}");

            // ---- reset ----
            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Props and Skybox Override (back to grey-box)"))
            {
                Undo.RecordObject(_theme, "Clear theme art");
                _theme.propPrefabs = new GameObject[0];
                _theme.skyboxOverride = null;
                EditorUtility.SetDirty(_theme);
                AssetDatabase.SaveAssets();
                _status = "Cleared. Rebuild the scene to see grey boxes again.";
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        // ==================================================================
        private void ScanProps()
        {
            string folder = AssetDatabase.GetAssetPath(_propFolder);

            if (!AssetDatabase.IsValidFolder(folder))
            {
                _status = "That object is not a folder inside Assets.";
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            var accepted = new List<GameObject>();
            int rejectedSize = 0;
            int rejectedEmpty = 0;

            foreach (var guid in guids)
            {
                if (accepted.Count >= _maxProps) break;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length == 0) { rejectedEmpty++; continue; }

                // Combined bounds decide whether this is scenery or a doorknob.
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (size < _minPropSize || size > _maxPropSize) { rejectedSize++; continue; }

                accepted.Add(prefab);
            }

            if (accepted.Count == 0)
            {
                _status = $"Found {guids.Length} prefab(s) but none passed the filters " +
                          $"({rejectedEmpty} had no mesh, {rejectedSize} were outside the size range). " +
                          "Widen the size filter and try again.";
                return;
            }

            Undo.RecordObject(_theme, "Assign theme props");

            _theme.propPrefabs = _replaceExisting || _theme.propPrefabs == null
                ? accepted.ToArray()
                : _theme.propPrefabs.Concat(accepted).Distinct().ToArray();

            EditorUtility.SetDirty(_theme);
            AssetDatabase.SaveAssets();

            _status = $"Assigned {accepted.Count} prop(s) to \"{_theme.themeName}\".\n" +
                      $"Skipped {rejectedEmpty} with no mesh and {rejectedSize} outside the size range.\n\n" +
                      "Now rebuild this theme from FPSKit > Build Scene.";
        }

        // ==================================================================
        /// <summary>
        /// Guesses which materials in the pack are ground, wall and crate surfaces
        /// from their names. Packs name things consistently enough that this lands
        /// most of the time -- and every field stays editable if it does not.
        /// </summary>
        private void AutoDetectSurfaces()
        {
            string folder = AssetDatabase.GetAssetPath(_propFolder);

            if (!AssetDatabase.IsValidFolder(folder))
            {
                _status = "Set the Prefab Folder above to the art pack root first.";
                return;
            }

            var materials = AssetDatabase.FindAssets("t:Material", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m != null)
                .ToList();

            if (materials.Count == 0)
            {
                _status = $"No materials found under {folder}.";
                return;
            }

            Material floor = Pick(materials, "asphalt", "road", "ground", "floor", "tarmac");
            Material wall = Pick(materials, "concrete_wall", "concretewall", "wall", "concrete");
            Material crate = Pick(materials, "wooden_box", "wood", "crate", "box", "palet", "pallet");

            Undo.RecordObject(_theme, "Auto-detect surfaces");

            if (floor != null) _theme.floorMaterial = floor;
            if (wall != null) { _theme.wallMaterial = wall; _theme.coverMaterial = wall; }
            if (crate != null) _theme.crateMaterial = crate;

            EditorUtility.SetDirty(_theme);
            AssetDatabase.SaveAssets();

            _status = "Detected:\n" +
                      $"  Floor  : {(floor != null ? floor.name : "nothing -- assign by hand")}\n" +
                      $"  Walls  : {(wall != null ? wall.name : "nothing -- assign by hand")}\n" +
                      $"  Crates : {(crate != null ? crate.name : "nothing -- assign by hand")}\n\n" +
                      "Check them, then rebuild the scene.";
        }

        /// <summary>First material whose name contains one of the keywords, in priority order.</summary>
        private static Material Pick(List<Material> materials, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                var hit = materials.FirstOrDefault(
                    m => m.name.ToLowerInvariant().Contains(keyword));

                if (hit != null) return hit;
            }

            return null;
        }

        // ==================================================================
        private void BuildSkybox()
        {
            var shader = Shader.Find("Skybox/Panoramic");
            if (shader == null)
            {
                _status = "Shader \"Skybox/Panoramic\" was not found in this project.";
                return;
            }

            string folder = "Assets/FPSKit_Generated/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Materials");

            string path = $"{folder}/Sky_{_theme.themeName.Replace(" ", "")}_HDRI.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetTexture("_MainTex", _hdri);
            mat.SetFloat("_Mapping", 1f);       // 1 = latitude/longitude layout
            mat.SetFloat("_ImageType", 0f);     // 0 = full 360 degrees
            mat.SetFloat("_Exposure", _theme.skyExposure);

            Undo.RecordObject(_theme, "Assign theme skybox");
            _theme.skyboxOverride = mat;

            EditorUtility.SetDirty(mat);
            EditorUtility.SetDirty(_theme);
            AssetDatabase.SaveAssets();

            _status = $"Built {System.IO.Path.GetFileName(path)} and assigned it to \"{_theme.themeName}\".\n\n" +
                      "Rebuild the scene, then nudge the theme's Sun Angles so the shadows " +
                      "line up with the sun in the photo.";
        }
    }
}
#endif
