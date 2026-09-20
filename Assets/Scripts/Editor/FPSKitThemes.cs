#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Generates the built-in LevelTheme assets. Each one is a plain asset you
    /// can tweak in the Inspector afterwards -- colours, fog, prop prefabs,
    /// weather, ambience. Adding a seventh theme means adding one entry here
    /// (or just duplicating an asset in the Project window).
    /// </summary>
    public static class FPSKitThemes
    {
        public const string ThemeFolder = "Assets/FPSKit_Generated/Themes";

        public static readonly string[] Names =
        {
            "Industrial Warehouse",
            "Desert Outpost",
            "Snowbound Station",
            "Night Rooftop",
            "Abandoned Subway",
            "Mars Colony"
        };

        [MenuItem("FPSKit/Create Theme Assets", false, 40)]
        public static void CreateAllMenu()
        {
            var themes = GetOrCreateAll();
            EditorUtility.DisplayDialog("FPSKit Themes",
                $"{themes.Count} theme assets are ready in:\n{ThemeFolder}\n\n" +
                "Select any of them to retune colours, fog, props and weather.", "OK");

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = themes[0];
        }

        [MenuItem("FPSKit/Reset Theme Assets to Defaults", false, 41)]
        public static void ResetAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset themes",
                "Re-applies the built-in values to every theme asset.\n\n" +
                "Any tuning you did in the Inspector will be lost.", "Reset them", "Cancel")) return;

            ResetAll();
        }

        /// <summary>
        /// Re-applies <see cref="Configure"/> to every theme asset.
        ///
        /// Public and dialog-free because this is the only way a change to the numbers
        /// in this file ever reaches the assets the builder actually reads. GetOrCreate
        /// deliberately leaves an existing asset alone -- a theme is meant to be tuned in
        /// the Inspector and kept -- so a retune in code that is never reset is a change
        /// that compiles, builds, ships and does nothing at all. Same trap as the enemy
        /// roster, the level ladders and the store, and now the same way out.
        /// </summary>
        public static int ResetAll()
        {
            int count = 0;

            foreach (var name in Names)
            {
                var theme = GetOrCreate(name);
                Configure(theme, name);
                EditorUtility.SetDirty(theme);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<color=lime>[FPSKit]</color> {count} theme asset(s) reset to defaults.");
            return count;
        }

        public static List<LevelTheme> GetOrCreateAll()
        {
            EnsureFolders();

            var result = new List<LevelTheme>();
            foreach (var name in Names) result.Add(GetOrCreate(name));

            AssetDatabase.SaveAssets();
            return result;
        }

        public static LevelTheme GetOrCreate(string themeName)
        {
            EnsureFolders();

            string path = $"{ThemeFolder}/{themeName.Replace(" ", "")}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LevelTheme>(path);
            if (existing != null) return existing;

            var theme = ScriptableObject.CreateInstance<LevelTheme>();
            Configure(theme, themeName);

            AssetDatabase.CreateAsset(theme, path);
            return theme;
        }

        // ==================================================================
        static void Configure(LevelTheme t, string themeName)
        {
            t.themeName = themeName;

            switch (themeName)
            {
                // ----------------------------------------------------------
                case "Industrial Warehouse":
                    t.description = "Overcast steel and concrete. The neutral baseline.";
                    t.skyTint = new Color(0.52f, 0.55f, 0.60f);
                    t.skyGroundColor = new Color(0.30f, 0.29f, 0.28f);
                    t.atmosphereThickness = 1.4f;
                    t.skyExposure = 1.0f;
                    t.sunColor = new Color(0.95f, 0.96f, 1f);
                    t.sunIntensity = 1.15f;
                    t.sunAngles = new Vector2(48f, 32f);
                    t.fogColor = new Color(0.50f, 0.53f, 0.58f);

                    // Thinned for the bigger site, and this is not a taste decision.
                    // Fog is exponential-squared, so 0.009 -- which was right for a
                    // 110m box -- is all but opaque by 250m. On a 500m plant that put
                    // a grey wall across the middle of the map: the far half simply was
                    // not there, and the arena read as empty precisely because you
                    // could not see what was in it.
                    t.fogDensity = 0.0032f;

                    t.ambientSky = new Color(0.45f, 0.50f, 0.60f);
                    t.ambientEquator = new Color(0.30f, 0.30f, 0.33f);
                    t.ambientGround = new Color(0.15f, 0.14f, 0.13f);
                    t.floorColor = new Color(0.33f, 0.33f, 0.35f);
                    t.wallColor = new Color(0.46f, 0.47f, 0.49f);
                    t.coverColors = new[]
                    {
                        new Color(0.40f, 0.42f, 0.45f),
                        new Color(0.52f, 0.45f, 0.30f),
                        new Color(0.30f, 0.33f, 0.36f)
                    };
                    t.coverTag = "Metal";

                    // A working plant on a street grid, not a walled yard. 500m square
                    // is about nine times the old 110x150 box, which is the point: the
                    // complaint it answers was "I am in a box with objects in it", and
                    // that is not fixed by adding objects. It is built at 500 rather
                    // than at a 400x500 rectangle because every other system here --
                    // the spawn ring, the leash, the minimap, the boundary -- is written
                    // against one arenaSize, and a rectangle is four more places for the
                    // two numbers to disagree for no gain the player can see.
                    t.industrialZone = true;
                    t.openZone = false;
                    t.arenaSize = 500f;
                    t.apronSize = 900f;

                    t.zoneWorksCount = 5;
                    t.zoneTankFarmCount = 3;
                    t.zoneContainerYardCount = 4;
                    t.zonePowerHouseCount = 1;

                    // The horizon. A plant that stops at its own fence is a diorama, so
                    // the skyline past it is most of what sells the scale.
                    t.backdropCount = 30;
                    t.backdropDistance = new Vector2(700f, 1300f);

                    // The greybox layout is off: every one of these would now be dropped
                    // on top of a road or through a shed wall. The districts place their
                    // own cover.
                    t.roomCount = 0;
                    t.platformCount = 0;
                    t.coverWallCount = 0;
                    t.crateStackCount = 0;
                    t.pillarCount = 0;
                    t.propCount = 0;

                    t.accentLightCount = 0;
                    t.contrast = 6f;
                    break;

                // ----------------------------------------------------------
                case "Desert Outpost":
                    t.description = "High sun, bleached sand, heat haze. Long sightlines.";
                    t.skyTint = new Color(0.72f, 0.66f, 0.52f);
                    t.skyGroundColor = new Color(0.55f, 0.44f, 0.30f);
                    t.atmosphereThickness = 0.7f;
                    t.skyExposure = 1.35f;
                    t.sunColor = new Color(1f, 0.94f, 0.78f);
                    t.sunIntensity = 1.45f;
                    // Mid-afternoon rather than noon, and this is a terrain decision more
                    // than a lighting one. A dune is read entirely through the shading
                    // across its own slope, and a sun sixty-eight degrees up puts almost
                    // the same amount of light on the windward face, the crest and the
                    // slipface -- so a field of eleven-metre dunes came back looking like
                    // a flat plane with a texture on it. Dropped to forty-two the slopes
                    // separate, the crests throw a shadow down their lee side, and the
                    // shape of the ground becomes something the player can actually see
                    // from a distance and navigate by.
                    t.sunAngles = new Vector2(42f, 140f);
                    t.fogColor = new Color(0.78f, 0.71f, 0.56f);
                    t.fogDensity = 0.004f;
                    // Lifted for the open zone. One hard sun over a map with
                    // hundred-metre rock on it puts whole faces in shadow, and at the
                    // walled arena's ambient those faces came back near black -- which
                    // is a place an enemy can stand and not be seen.
                    t.ambientSky = new Color(0.74f, 0.68f, 0.56f);
                    t.ambientEquator = new Color(0.60f, 0.53f, 0.42f);
                    t.ambientGround = new Color(0.44f, 0.37f, 0.27f);
                    t.floorColor = new Color(0.62f, 0.54f, 0.38f);
                    t.wallColor = new Color(0.70f, 0.62f, 0.46f);
                    t.coverColors = new[]
                    {
                        new Color(0.66f, 0.58f, 0.42f),
                        new Color(0.50f, 0.45f, 0.34f),
                        new Color(0.45f, 0.40f, 0.36f)
                    };
                    t.coverTag = "Concrete";

                    // The open zone. Four hundred and fifty metres of ground with a
                    // river cut through it, rather than a hundred and fifty metres of
                    // floor with walls at the edge -- see FPSKitOpenZone.
                    t.openZone = true;
                    t.arenaSize = 450f;
                    t.apronSize = 1100f;
                    t.wallHeight = 5f;

                    // Thin, because the whole point of this arena is that you can see to
                    // the horizon. At the walled arena's 0.004 the mesas are solid fog.
                    t.fogDensity = 0.0011f;

                    t.hazard = LevelTheme.Hazard.River;
                    t.hazardWidth = 58f;
                    t.hazardDepth = 22f;
                    t.hazardOffset = 92f;
                    t.hazardMeander = 30f;
                    // Silt, not lagoon. A river that has crossed a desert to get here is
                    // carrying the desert with it, and the blue it started at read as
                    // tropical water in a canyon.
                    t.hazardColor = new Color(0.25f, 0.42f, 0.38f);
                    t.bankColor = new Color(0.52f, 0.44f, 0.31f);
                    t.bridgeColor = new Color(0.50f, 0.45f, 0.38f);
                    t.bridgeWidth = 9f;
                    t.bridgeCount = 2;

                    // Dunes. The wavelength is deliberately short enough that the primary
                    // train overshoots the angle of repose and gets cut back to it by the
                    // relaxation pass, because that is what a slipface *is* -- sand piled
                    // until it slides. Long enough not to overshoot and the field comes
                    // back as smooth swells with no crest anywhere in it, which is a
                    // landscape but not a dune field.
                    //
                    // The height is the amplitude of the *primary* train only,
                    // and the basin and hill octaves are scaled off it, so eleven metres
                    // of dune sits on twenty-five-odd metres of broad relief across the
                    // whole map. A crest has to hide a standing enemy from a standing
                    // player or it changes nothing about how the level is fought; a
                    // hundred and twenty metres of wavelength is what keeps that height
                    // drivable, since a dune's slope is the one over the other. Together
                    // they put about four ranges of dune across the arena -- enough that
                    // there is always one between you and somewhere, and few enough that
                    // the map still reads as open ground rather than as a maze with sand
                    // walls.
                    t.duneHeight = 11f;
                    t.duneWavelength = 95f;
                    t.duneWindAngle = 34f;

                    t.backdropCount = 38;
                    t.backdropDistance = new Vector2(640f, 1180f);
                    t.backdropHeight = new Vector2(60f, 210f);
                    t.backdropWidth = new Vector2(80f, 280f);
                    t.backdropColor = new Color(0.50f, 0.40f, 0.30f);
                    t.landmarkCount = 12;

                    t.outpostCount = 10;
                    t.vantageCount = 10;
                    t.coverLineCount = 18;
                    t.scatterClusterCount = 40;

                    // Unused while openZone is on -- kept tuned so that turning it off
                    // gives back the arena this used to be rather than an empty field.
                    t.roomCount = 2;
                    t.platformCount = 3;
                    t.coverWallCount = 6;
                    t.crateStackCount = 5;
                    t.pillarCount = 8;
                    t.propCount = 14;

                    t.saturation = -6f;
                    t.contrast = 6f;
                    t.filmGrain = 0.12f;
                    t.randomSeed = 21;
                    break;

                // ----------------------------------------------------------
                case "Snowbound Station":
                    t.description = "Low winter sun, thick fog, short engagements.";
                    t.skyTint = new Color(0.62f, 0.68f, 0.78f);
                    t.skyGroundColor = new Color(0.72f, 0.76f, 0.82f);
                    t.atmosphereThickness = 2.2f;
                    t.skyExposure = 1.15f;
                    t.sunColor = new Color(0.82f, 0.88f, 1f);
                    t.sunIntensity = 0.95f;
                    t.sunAngles = new Vector2(18f, 200f);
                    t.fogColor = new Color(0.76f, 0.81f, 0.88f);
                    t.fogDensity = 0.028f;
                    t.ambientSky = new Color(0.60f, 0.68f, 0.80f);
                    t.ambientEquator = new Color(0.45f, 0.50f, 0.58f);
                    t.ambientGround = new Color(0.30f, 0.34f, 0.40f);
                    t.floorColor = new Color(0.78f, 0.81f, 0.86f);
                    t.wallColor = new Color(0.60f, 0.64f, 0.70f);
                    t.coverColors = new[]
                    {
                        new Color(0.70f, 0.74f, 0.80f),
                        new Color(0.42f, 0.47f, 0.54f),
                        new Color(0.55f, 0.58f, 0.62f)
                    };
                    t.coverTag = "Metal";
                    t.arenaSize = 95f;
                    t.roomCount = 4;
                    t.platformCount = 2;
                    t.coverWallCount = 9;
                    t.crateStackCount = 9;
                    t.pillarCount = 4;
                    t.saturation = -18f;
                    t.contrast = 4f;
                    t.bloomIntensity = 0.6f;
                    t.randomSeed = 44;
                    break;

                // ----------------------------------------------------------
                case "Night Rooftop":
                    t.description = "City night. Dark sky, warm practicals, heavy bloom.";
                    t.skyTint = new Color(0.10f, 0.13f, 0.22f);
                    t.skyGroundColor = new Color(0.06f, 0.07f, 0.10f);
                    t.atmosphereThickness = 0.4f;
                    t.skyExposure = 0.45f;
                    t.sunColor = new Color(0.55f, 0.65f, 0.95f);   // moonlight
                    t.sunIntensity = 0.35f;
                    t.sunAngles = new Vector2(32f, 250f);
                    t.shadowStrength = 0.6f;
                    t.fogColor = new Color(0.09f, 0.11f, 0.17f);
                    t.fogDensity = 0.016f;
                    t.ambientSky = new Color(0.12f, 0.15f, 0.24f);
                    t.ambientEquator = new Color(0.09f, 0.10f, 0.14f);
                    t.ambientGround = new Color(0.04f, 0.04f, 0.05f);
                    t.floorColor = new Color(0.14f, 0.14f, 0.16f);
                    t.wallColor = new Color(0.18f, 0.19f, 0.22f);
                    t.coverColors = new[]
                    {
                        new Color(0.16f, 0.17f, 0.20f),
                        new Color(0.22f, 0.20f, 0.18f),
                        new Color(0.10f, 0.12f, 0.15f)
                    };
                    t.coverTag = "Metal";
                    t.arenaSize = 95f;
                    // Rooftops live or die on verticality.
                    t.roomCount = 3;
                    t.platformCount = 4;
                    t.coverWallCount = 8;
                    t.crateStackCount = 7;
                    t.pillarCount = 6;
                    t.accentLightCount = 10;
                    t.accentLightColor = new Color(1f, 0.58f, 0.22f);
                    t.accentLightIntensity = 14f;
                    t.accentLightRange = 16f;
                    t.bloomIntensity = 1.1f;
                    t.postExposure = 0.35f;
                    t.vignetteIntensity = 0.38f;
                    t.filmGrain = 0.35f;
                    t.randomSeed = 88;
                    break;

                // ----------------------------------------------------------
                case "Abandoned Subway":
                    t.description = "Tight, dark, claustrophobic. Flickering service lights.";
                    t.skyTint = new Color(0.05f, 0.05f, 0.06f);
                    t.skyGroundColor = new Color(0.03f, 0.03f, 0.03f);
                    t.atmosphereThickness = 0.3f;
                    t.skyExposure = 0.25f;
                    t.sunColor = new Color(0.5f, 0.55f, 0.6f);
                    t.sunIntensity = 0.15f;
                    t.sunAngles = new Vector2(80f, 0f);
                    t.shadowStrength = 0.4f;
                    t.fogColor = new Color(0.05f, 0.055f, 0.06f);
                    t.fogDensity = 0.045f;
                    t.ambientSky = new Color(0.09f, 0.09f, 0.10f);
                    t.ambientEquator = new Color(0.06f, 0.06f, 0.07f);
                    t.ambientGround = new Color(0.02f, 0.02f, 0.02f);
                    t.floorColor = new Color(0.12f, 0.12f, 0.12f);
                    t.wallColor = new Color(0.17f, 0.16f, 0.15f);
                    t.coverColors = new[]
                    {
                        new Color(0.14f, 0.14f, 0.13f),
                        new Color(0.20f, 0.17f, 0.13f),
                        new Color(0.11f, 0.13f, 0.13f)
                    };
                    t.coverTag = "Concrete";
                    t.arenaSize = 70f;
                    t.wallHeight = 5f;
                    // Dense and column-heavy. Nothing should be visible for long.
                    t.roomCount = 4;
                    t.platformCount = 1;
                    t.coverWallCount = 10;
                    t.crateStackCount = 10;
                    t.pillarCount = 12;
                    t.coverHeightRange = new Vector2(1.2f, 1.6f);
                    t.coverWidthRange = new Vector2(3f, 7f);
                    t.accentLightCount = 14;
                    t.accentLightColor = new Color(0.75f, 0.85f, 0.75f);
                    t.accentLightIntensity = 9f;
                    t.accentLightRange = 11f;
                    t.accentLightHeight = 4.2f;
                    t.bloomIntensity = 0.8f;
                    t.saturation = -25f;
                    t.contrast = 18f;
                    t.vignetteIntensity = 0.45f;
                    t.filmGrain = 0.45f;
                    t.randomSeed = 13;
                    break;

                // ----------------------------------------------------------
                case "Mars Colony":
                    t.description = "Rust sky, thin light, drifting dust.";
                    t.skyTint = new Color(0.72f, 0.42f, 0.28f);
                    t.skyGroundColor = new Color(0.42f, 0.22f, 0.14f);
                    t.atmosphereThickness = 1.8f;
                    t.skyExposure = 1.0f;
                    t.sunColor = new Color(1f, 0.80f, 0.62f);
                    t.sunIntensity = 1.05f;
                    t.sunAngles = new Vector2(26f, 300f);
                    t.fogColor = new Color(0.58f, 0.35f, 0.24f);
                    t.fogDensity = 0.014f;
                    t.ambientSky = new Color(0.55f, 0.34f, 0.24f);
                    t.ambientEquator = new Color(0.36f, 0.24f, 0.18f);
                    t.ambientGround = new Color(0.20f, 0.12f, 0.09f);
                    t.floorColor = new Color(0.44f, 0.26f, 0.18f);
                    t.wallColor = new Color(0.52f, 0.50f, 0.49f);
                    t.coverColors = new[]
                    {
                        new Color(0.55f, 0.53f, 0.52f),
                        new Color(0.48f, 0.30f, 0.20f),
                        new Color(0.62f, 0.58f, 0.54f)
                    };
                    t.coverTag = "Metal";
                    t.coverMetallic = 0.35f;
                    t.arenaSize = 120f;
                    t.roomCount = 3;
                    t.platformCount = 3;
                    t.coverWallCount = 7;
                    t.crateStackCount = 6;
                    t.pillarCount = 5;
                    t.accentLightCount = 6;
                    t.accentLightColor = new Color(0.55f, 0.85f, 1f);
                    t.accentLightIntensity = 10f;
                    t.saturation = 8f;
                    t.contrast = 12f;
                    t.randomSeed = 66;
                    break;
            }
        }

        // ==================================================================
        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(ThemeFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Themes");
        }
    }
}
#endif
