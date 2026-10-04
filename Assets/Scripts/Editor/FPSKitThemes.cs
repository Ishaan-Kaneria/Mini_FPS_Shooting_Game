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
            "Abandoned Fairground",
            "Mars Colony",
            "The Auger House"
        };

        /// <summary>
        /// The last zone, named here because three generators have to agree about which
        /// arena is the finale: this one builds it small and dark, the level generator
        /// gives it three rungs instead of eight, and the campaign gates it on the other
        /// six being finished.
        /// </summary>
        public const string FinaleName = "The Auger House";

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
        /// <summary>
        /// Who fights in an arena: the shared basics, two of the heavier shared
        /// variants chosen to suit the ground, the zone's own local threat, the two
        /// generic bosses and the person who holds the place.
        ///
        /// <b>The basics are shared on purpose.</b> Grunts, Runners and Marksmen are in
        /// every zone because a level's difficulty step has to mean the same thing
        /// wherever it is played -- if every arena drew from a completely different
        /// roster, the ladder's curve would be six unrelated curves and no number in
        /// FPSKitLevels would describe any of them. What varies is the top half of the
        /// mix, which is the half a player actually remembers.
        ///
        /// The pairing is not decoration. Screamers go where there is room to be rushed
        /// across; Sentinels go where there is cover to break them behind; Brutes go
        /// where the corners are tight enough that backing away runs out of floor.
        /// </summary>
        static List<EnemyArchetype> RosterFor(string themeName)
        {
            string[] shared = { "Grunt", "Runner", "Marksman" };

            string[] heavies = themeName switch
            {
                // The finale fields the worst of everything the campaign has taught the
                // player to handle, all at once and in a house.
                FinaleName => new[] { "Brute", "Sentinel", "Hardsuit", "Stalker", "Spotter" },

                "Industrial Warehouse" => new[] { "Brute" },
                "Snowbound Station" => new[] { "Sentinel" },
                "Desert Outpost" => new[] { "Screamer" },
                "Night Rooftop" => new[] { "Sentinel" },
                "Abandoned Fairground" => new[] { "Screamer" },
                "Mars Colony" => new[] { "Brute", "Sentinel" },
                _ => new[] { "Brute", "Sentinel", "Screamer" }
            };

            var roster = new List<EnemyArchetype>();

            void Add(string name)
            {
                var archetype = FPSKitEnemyRoster.GetOrCreate(name);
                if (archetype != null && !roster.Contains(archetype)) roster.Add(archetype);
            }

            foreach (var name in shared) Add(name);
            foreach (var name in heavies) Add(name);

            // The zone's own. Indexed by where the arena sits in the campaign, so an
            // arena the story does not use simply does not get one.
            int zone = FPSKitCampaign.IndexOfTheme(themeName);

            if (zone >= 0 && zone < FPSKitEnemyRoster.ZoneDebut.Length)
                Add(FPSKitEnemyRoster.ZoneDebut[zone]);

            // Both generic bosses, because the third and sixth rung of every ladder ask
            // for one and LevelManager falls back to whatever Boss-role archetype the
            // roster offers when a level names none.
            foreach (var name in FPSKitEnemyRoster.GenericBossNames) Add(name);

            // And the person standing at the end of it.
            string holder = FPSKitCampaign.SiblingFor(themeName);
            if (!string.IsNullOrEmpty(holder)) Add(holder);

            return roster;
        }

        static void Configure(LevelTheme t, string themeName)
        {
            t.themeName = themeName;
            t.enemyRoster = RosterFor(themeName);

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
                    t.description = "A mud-brick village, a forward base and walled farms on a river of sand. Heat haze and blowing dust.";
                    // Heat and dust (2026-09-23): a hotter, hazier sky than the clean blue it was, which
                    // read as a mild day rather than a desert. Thicker air scatters the blue out
                    // towards the horizon, and the fog is warmer and a little denser so the far
                    // rock sits in haze -- still well short of hiding it.
                    // A dusty violet-rose tint, not orange: the procedural sky multiplies its blue
                    // atmosphere by this, and an orange tint turned the zenith a muddy green.
                    t.skyTint = new Color(0.66f, 0.52f, 0.62f);
                    t.skyGroundColor = new Color(0.52f, 0.38f, 0.26f);
                    // Back to 1.5 (2026-10-03): 1.15 turned the golden horizon blue. The mountains' yellow came from
                    // the fog colour, which stays greyer; the sky keeps its glow.
                    t.atmosphereThickness = 1.5f;
                    t.skyExposure = 1.45f;
                    t.sunColor = new Color(1f, 0.84f, 0.62f);
                    t.sunIntensity = 1.55f;
                    // Mid-afternoon rather than noon, and this is a terrain decision more
                    // than a lighting one. A dune is read entirely through the shading
                    // across its own slope, and a sun sixty-eight degrees up puts almost
                    // the same amount of light on the windward face, the crest and the
                    // slipface -- so a field of eleven-metre dunes came back looking like
                    // a flat plane with a texture on it. Dropped to forty-two the slopes
                    // separate, the crests throw a shadow down their lee side, and the
                    // shape of the ground becomes something the player can actually see
                    // from a distance and navigate by.
                    // Golden hour (2026-10-03, "make it sexier"): thirty-eight degrees, down from
                    // forty-two. (Twenty-six was tried and put the rooftops and the player's hands
                    // in shadow, and read too orange and too dark, worse still on a phone.) The lower the sun the longer the lee-side shadow of every dune,
                    // and the shape of the ground is the one thing the player reads at range.
                    // Ambient is lifted so no face goes black.
                    t.sunAngles = new Vector2(33f, 140f);
                    // An amber haze (2026-10-03, matched to Ishaan's reference screenshot, milder): the real colour
                    // grade only works now that the PostFX asset is saved properly, so this is tuned against the
                    // in-game view, not the editor camera.
                    t.fogColor = new Color(0.90f, 0.68f, 0.45f);
                    t.fogDensity = 0.004f;
                    // Lifted for the open zone. One hard sun over a map with
                    // hundred-metre rock on it puts whole faces in shadow, and at the
                    // walled arena's ambient those faces came back near black -- which
                    // is a place an enemy can stand and not be seen.
                    // Shade is where a fight happens here: a dune's lee side, the foot of a wall,
                    // a roof's shadow. At the old ambient those places came back near black
                    // with the sun behind them (2026-10-03 screenshot), so the shadow side is
                    // lifted to well over half of what the sunlit side gets.
                    t.shadowStrength = 0.55f;
                    t.ambientSky = new Color(0.94f, 0.88f, 0.78f);
                    t.ambientEquator = new Color(0.86f, 0.76f, 0.63f);
                    t.ambientGround = new Color(0.68f, 0.58f, 0.44f);
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

                    // Explicit now that BuildDuneField reads this. It was getting Sand from
                    // a hard-coded string; the default on LevelTheme is Concrete, which
                    // would have quietly turned the dunes to pavement.
                    t.floorDetail = "Sand";
                    t.arenaSize = 450f;
                    t.apronSize = 1100f;
                    t.wallHeight = 5f;

                    // Thin, because the whole point of this arena is that you can see to
                    // the horizon. At the walled arena's 0.004 the mesas are solid fog.
                    t.fogDensity = 0.0022f;

                    t.hazard = LevelTheme.Hazard.River;
                    t.hazardWidth = 58f;
                    t.hazardDepth = 22f;
                    t.hazardOffset = 92f;
                    t.hazardMeander = 30f;
                    // Silt, not lagoon. A river that has crossed a desert to get here is
                    // carrying the desert with it, and the blue it started at read as
                    // tropical water in a canyon.
                    t.hazardColor = new Color(0.30f, 0.43f, 0.36f);
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

                    t.backdropCount = 14;
                    t.backdropDistance = new Vector2(640f, 1180f);
                    t.backdropHeight = new Vector2(60f, 210f);
                    t.backdropWidth = new Vector2(80f, 280f);
                    t.backdropColor = new Color(0.50f, 0.40f, 0.30f);
                    t.landmarkCount = 12;

                    // Six rather than ten since the desert was made full (2026-09-26): the farms,
                    // camps and checkpoints are the compounds now, and the generic ones were
                    // taking the ground they need.
                    t.outpostCount = 6;
                    t.vantageCount = 10;
                    t.coverLineCount = 22;

                    // Raised with the rocks shrunk, not instead of it. A scatter cluster
                    // used to be two to four boulders eleven metres across, which is one
                    // landmark pretending to be cover; it is now four rocks a player can
                    // crouch behind, and it takes more of them to keep the ground between
                    // two places from being a field.
                    t.scatterClusterCount = 58;

                    // Unused while openZone is on -- kept tuned so that turning it off
                    // gives back the arena this used to be rather than an empty field.
                    t.roomCount = 2;
                    t.platformCount = 3;
                    t.coverWallCount = 6;
                    t.crateStackCount = 5;
                    t.pillarCount = 8;
                    t.propCount = 14;

                    // Graded warm and punchy rather than drained: the desaturated pass read as
                    // a dull photograph of a desert, not somewhere you want to look at.
                    t.bloomIntensity = 0.6f;
                    t.postExposure = 0.05f;
                    t.saturation = 9f;
                    t.colorFilter = new Color(1f, 0.93f, 0.84f);
                    t.vignetteIntensity = 0.26f;
                    t.contrast = 14f;
                    t.filmGrain = 0.12f;
                    t.randomSeed = 21;
                    break;

                // ----------------------------------------------------------
                // The finale. A house, not an arena: small, lit from inside, and the
                // only place in the game where the walls are closer than the sightlines.
                //
                // <b>Small is the design.</b> Every other zone is somewhere the player
                // can back off to; six arenas have taught them that disengaging works.
                // Eighty metres of rooms takes that away without changing a single
                // number on an enemy, which is a harder last level than any multiplier
                // would have bought.
                case FinaleName:
                    t.description = "The house he built. Lit from inside, and smaller than it looks.";
                    t.floorDetail = "Tile";
                    t.wallDetail = "Concrete";
                    t.floorDetailSize = 2.5f;
                    t.wallDetailSize = 3f;

                    // Night outside, so every light in the arena is one somebody left on.
                    t.skyTint = new Color(0.10f, 0.11f, 0.16f);
                    t.skyGroundColor = new Color(0.06f, 0.06f, 0.09f);
                    t.atmosphereThickness = 1.1f;
                    t.skyExposure = 0.55f;
                    t.sunColor = new Color(0.55f, 0.62f, 0.85f);

                    // A moon rather than a sun: enough to model the ground and no more.
                    // The park already records what happens when this goes to zero --
                    // the arena is not frightening, it is unreadable.
                    t.sunIntensity = 0.35f;
                    t.sunAngles = new Vector2(52f, 20f);
                    t.fogColor = new Color(0.07f, 0.08f, 0.11f);
                    t.fogDensity = 0.016f;
                    t.ambientSky = new Color(0.14f, 0.15f, 0.20f);
                    t.ambientEquator = new Color(0.12f, 0.11f, 0.13f);
                    t.ambientGround = new Color(0.07f, 0.06f, 0.07f);
                    t.floorColor = new Color(0.30f, 0.26f, 0.23f);
                    t.wallColor = new Color(0.24f, 0.21f, 0.20f);
                    t.coverColors = new[]
                    {
                        new Color(0.34f, 0.26f, 0.18f),
                        new Color(0.22f, 0.19f, 0.18f),
                        new Color(0.40f, 0.33f, 0.24f)
                    };
                    t.coverTag = "Wood";

                    // Eighty metres, more rooms than anywhere else, and pillars instead
                    // of open floor. Every one of those numbers is pushing the same way.
                    t.arenaSize = 80f;
                    t.roomCount = 7;
                    t.platformCount = 2;
                    t.coverWallCount = 14;
                    t.crateStackCount = 6;
                    t.pillarCount = 10;
                    // The practicals are the arena. A moon at 0.35 models the ground and
                    // nothing else, so without these the House is a dark yard with walls
                    // round it -- which is what the first build of it came out as, while
                    // its own description said "lit from inside". The lamps are what make
                    // the rooms rooms, and what makes the dark between them mean anything.
                    t.accentLightColor = new Color(1f, 0.72f, 0.38f);
                    t.accentLightCount = 16;
                    t.accentLightIntensity = 11f;
                    t.accentLightRange = 13f;
                    t.accentLightHeight = 3.2f;
                    t.saturation = -6f;
                    t.contrast = 10f;
                    t.bloomIntensity = 0.95f;
                    t.randomSeed = 81;
                    break;

                // ----------------------------------------------------------
                case "Snowbound Station":
                    t.description = "A polar station, pine forest, ice caves and crevasses. Low sun, drifting snow.";

                    // Snow underfoot, concrete for the station. Snow's map is the softest
                    // of the set on purpose -- what reads as snow is the absence of fine
                    // detail, so sharpening it turns it into pale sand.
                    t.floorDetail = "Snow";
                    t.wallDetail = "Concrete";
                    t.floorDetailSize = 6f;
                    t.wallDetailSize = 3f;
                    // Blizzard and light (2026-09-23): a low, cold, slightly gold sun that throws long
                    // blue shadows across the field -- the one thing that gives white ground a shape
                    // -- under a pale overcast rather than the clear blue it was. Shadows are what
                    // snow is drawn with; at the old 38 degrees there were hardly any.
                    t.skyTint = new Color(0.72f, 0.76f, 0.82f);
                    t.skyGroundColor = new Color(0.80f, 0.84f, 0.90f);

                    // <b>Thin air, and a sun that is up.</b> At 2.2 thickness and 18
                    // degrees of elevation Unity's procedural sky is a sunset, which in a
                    // 95m box behind heavy fog was never visible and at 450m is the whole
                    // top half of the screen: a blazing orange sky over a snow field,
                    // which reads as a desert somebody painted white. Thin atmosphere is
                    // what makes a cold sky pale blue rather than warm.
                    t.atmosphereThickness = 1.35f;
                    t.skyExposure = 1.15f;
                    t.sunColor = new Color(1f, 0.90f, 0.76f);
                    t.sunIntensity = 1.15f;
                    t.sunAngles = new Vector2(17f, 205f);
                    t.fogColor = new Color(0.80f, 0.84f, 0.90f);
                    // <b>Snow bounces.</b> These were written for a 95m box behind thick
                    // fog, where nothing was ever lit by anything but the fog itself; on
                    // an open field under a sun they made every dome and every drift face
                    // that was turned away from the light come back grey. An igloo in
                    // sunlight is not grey -- the ground under it is throwing most of the
                    // light back up at it, which is exactly what ambientGround is for and
                    // why it is the one raised furthest here.
                    t.ambientSky = new Color(0.62f, 0.72f, 0.90f);
                    t.ambientEquator = new Color(0.72f, 0.78f, 0.88f);
                    t.ambientGround = new Color(0.66f, 0.71f, 0.80f);
                    // Snow is the brightest surface in the game and has to be written as
                    // one. At 0.78 grey-blue it came back reading as poured concrete --
                    // correct as a *value* against the old overcast sky, and completely
                    // wrong as a material once there is a sun on it.
                    t.floorColor = new Color(0.93f, 0.95f, 0.98f);
                    t.wallColor = new Color(0.74f, 0.79f, 0.86f);
                    t.coverColors = new[]
                    {
                        new Color(0.86f, 0.90f, 0.95f),
                        new Color(0.58f, 0.64f, 0.72f),
                        new Color(0.72f, 0.78f, 0.84f)
                    };

                    // What the rock breaking through the snow is made of. Dark, because
                    // it is the only thing out here that is not white and it is what the
                    // eye navigates by.
                    t.bankColor = new Color(0.34f, 0.35f, 0.38f);
                    t.coverTag = "Metal";

                    // A frozen field rather than a walled yard. The 95m box this replaced
                    // was the smallest arena in the game and read as a car park with snow
                    // on it; at 450m it is the same size as the desert, which is the
                    // scale the heightfield, the boundary ridge and the backdrop were all
                    // written for.
                    t.snowZone = true;
                    t.arenaSize = 450f;
                    t.apronSize = 900f;

                    // Drifts, not dunes. Lower and shorter than the desert's, because
                    // what snow does is roll -- and because relief this size is what makes
                    // ground uneven at walking pace rather than at map scale. The
                    // relaxation caps everything at 26 degrees either way.
                    // Raised from 5.5/70 after looking at it: at that relief the field
                    // came back as a white sheet with a slight tilt to it, and the whole
                    // reason the ground is a heightfield rather than a plane is that a
                    // player should be able to drop behind a rise. 14.7m of relief over
                    // 450m is a gentle hill; this is about 20, which is a drift you can
                    // stand behind. The relaxation caps the slope at 26 degrees either
                    // way, so more amplitude buys form rather than cliffs.
                    t.duneHeight = 7.5f;
                    t.duneWavelength = 58f;

                    t.iglooCamps = 6;
                    t.lakeRadius = 64f;

                    // Recalibrated for the new size. Fog is exponential-squared, and
                    // 0.028 was written for a 95m box -- at 450m it is opaque by the
                    // first drift, which reads as an empty arena rather than a foggy one.
                    // The desert sits at 0.0011; snow is allowed more, because haze off a
                    // white field is most of what makes it look cold.
                    t.fogDensity = 0.0022f;

                    t.roomCount = 0;
                    t.platformCount = 0;
                    t.coverWallCount = 0;
                    t.crateStackCount = 0;
                    t.pillarCount = 0;
                    t.saturation = -18f;
                    t.contrast = 4f;
                    t.bloomIntensity = 0.6f;
                    t.randomSeed = 44;
                    break;

                // ----------------------------------------------------------
                case "Night Rooftop":
                    // Filed as Night Rooftop, shown as Night Citylife. The key names the save, the campaign
                    // order and the scene; only what a player reads changes (see LevelTheme.displayName).
                    t.displayName = "Night Citylife";
                    t.description = "City night. A lit street grid under a moon: crossings, signals, lamps and roofs to fight across.";

                    // Poured roof deck and rendered parapets. Tiled tighter than the others
                    // because a rooftop is looked at from standing height and nothing is
                    // far away.
                    t.floorDetail = "Concrete";
                    t.wallDetail = "Concrete";
                    t.floorDetailSize = 3f;
                    t.wallDetailSize = 2.5f;
                    t.skyTint = new Color(0.10f, 0.13f, 0.22f);
                    t.skyGroundColor = new Color(0.06f, 0.07f, 0.10f);
                    t.atmosphereThickness = 0.4f;
                    t.skyExposure = 0.45f;
                    t.sunColor = new Color(0.62f, 0.72f, 1.00f);   // moonlight
                    t.sunIntensity = 1.05f;
                    t.sunAngles = new Vector2(32f, 250f);
                    t.shadowStrength = 0.55f;
                    t.fogColor = new Color(0.12f, 0.15f, 0.26f);
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
                    t.arenaSize = 450f;
                    t.rooftopZone = true;
                    t.apronSize = 500f;
                    // 450 m: a night city district. The old 95 m box is gone; the lit windows, bridges and the
                    // fog are what carry the size, so the fog is light (it is exponential-squared).
                    t.fogDensity = 0.0016f;
                    t.skyExposure = 0.55f;
                    t.ambientSky = new Color(0.30f, 0.36f, 0.54f);
                    t.ambientEquator = new Color(0.22f, 0.25f, 0.36f);
                    t.ambientGround = new Color(0.10f, 0.10f, 0.14f);
                    t.backdropCount = 60;
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
                    t.postExposure = 0.85f;
                    t.vignetteIntensity = 0.30f;
                    t.filmGrain = 0.35f;
                    t.randomSeed = 88;
                    break;

                // ----------------------------------------------------------
                case "Abandoned Fairground":
                    t.description = "Overgrown dusk. A planned fairground gone to jungle: broken rides, a half-built hall and holes in the ground.";

                    t.parkZone = true;

                    // The ground is the desert's, subsided: same heightfield, same
                    // relaxation, same smoothing, so it rolls the way ground does rather
                    // than the way noise does. Lower and longer than the dunes, because
                    // this is subsidence under a car park and not a wind form.
                    // Rolling, not rippled: a fairground is laid on level ground, so the heightfield is
                    // only a few metres of swell. Each site then flattens a pad of its own.
                    t.duneHeight = 2.6f;
                    t.duneWavelength = 110f;
                    t.apronSize = 700f;

                    // Packed earth under moss, not the dune field's wind ripples, which
                    // would make this a desert with rides in it. The low-relief concrete
                    // map is what keeps it reading as ground rather than as a green sheet.
                    t.floorDetail = "Adobe";
                    t.wallDetail = "Timber";
                    t.floorDetailSize = 2.4f;
                    t.wallDetailSize = 2f;

                    // <b>Dusk, not night.</b> Ishaan's pick for the overgrown rework: the sun
                    // has just gone, so the sky is a warm band over a violet zenith and the
                    // light is low and orange. A night park hid the trees, the hall and the
                    // fill he asked for in the dark; a low sun lets them throw long shadows
                    // and still leaves the string lights and the lamps worth switching on.
                    t.skyTint = new Color(0.34f, 0.27f, 0.42f);
                    t.skyGroundColor = new Color(0.12f, 0.09f, 0.08f);
                    t.atmosphereThickness = 1.25f;
                    t.skyExposure = 1.0f;
                    t.sunColor = new Color(1f, 0.80f, 0.60f);
                    t.sunIntensity = 1.75f;
                    // Low, so everything on this ground casts a long shadow across it.
                    t.sunAngles = new Vector2(17f, 214f);
                    t.shadowStrength = 0.7f;
                    // Mist, warm at the horizon. 450m of it is what makes the far tree line
                    // read as a jungle rather than a wall; any thicker and it swallows the
                    // wheel, which is the compass.
                    t.fogColor = new Color(0.38f, 0.31f, 0.38f);
                    t.fogDensity = 0.0048f;
                    t.ambientSky = new Color(0.54f, 0.52f, 0.66f);
                    t.ambientEquator = new Color(0.42f, 0.36f, 0.36f);
                    t.ambientGround = new Color(0.12f, 0.12f, 0.08f);
                    // Moss over earth. Lifted from the old grey so the ground carries colour
                    // under a low sun instead of going black.
                    t.floorColor = new Color(0.31f, 0.38f, 0.18f);
                    t.wallColor = new Color(0.46f, 0.40f, 0.33f);
                    // Forested hills on the horizon, hazed towards the fog colour: the desert's
                    // buttes in a jungle's green.
                    t.backdropColor = new Color(0.17f, 0.21f, 0.15f);
                    t.coverColors = new[]
                    {
                        new Color(0.40f, 0.17f, 0.15f),
                        new Color(0.17f, 0.30f, 0.30f),
                        new Color(0.58f, 0.44f, 0.20f)
                    };
                    t.coverTag = "Concrete";
                    // 450 to match the desert, against the 70 this was. That size is why the
                    // arena read as a prototype: a seventy-metre box is not a small station,
                    // it is a room. The claustrophobia now comes from the plan -- halls and
                    // passages -- rather than from the walls being close together.
                    t.arenaSize = 340f;
                    // Dense, not vast: 340m, so the next scene is never more than about forty metres away.
                    // The first replan was 450m and read as rides stood on a lawn.
                    // Tall enough to carry a concourse over the platform halls.
                    t.wallHeight = 13f;
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
                    t.bloomIntensity = 0.9f;
                    t.saturation = -6f;
                    t.contrast = 12f;
                    t.vignetteIntensity = 0.38f;
                    t.filmGrain = 0.3f;
                    t.randomSeed = 13;
                    break;

                // ----------------------------------------------------------
                case "Mars Colony":
                    // Filed as Mars Colony, shown as the Unknown Planet. The key names the
                    // scene, the ladder and the campaign slot, and the ladder's scene name
                    // is what stars are saved under -- renaming it would move the arena
                    // out from under every save that has reached it.
                    t.displayName = "Unknown Planet";
                    t.description = "Nobody has named it. A red sun the size of a hand hangs over rivers of molten rock.";

                    // ---- the sky ----
                    // Painted, not these four: volcanicZone swaps the procedural sky for a
                    // panorama (FPSKitVolcanic.VolcanicSky), because the procedural one
                    // scatters red away and came back green. Kept tuned so turning the
                    // dressing off still gives a warm sky rather than the default blue.
                    t.skyTint = new Color(0.95f, 0.18f, 0.08f);
                    t.skyGroundColor = new Color(0.30f, 0.10f, 0.05f);
                    t.atmosphereThickness = 3.1f;
                    t.skyExposure = 1.15f;
                    // A red giant: the light is red and strong, and low, so the shadows are long and the lit faces burn.
                    t.sunColor = new Color(1f, 0.30f, 0.12f);
                    t.sunIntensity = 1.45f;
                    t.sunAngles = new Vector2(17f, 300f);
                    t.shadowStrength = 0.75f;

                    // Smoky rather than dusty. Thinner than the old colony's 0.014, which
                    // was an interior number: exponential-squared at 450m hides the far
                    // half of the map, and an arena whose volcanoes cannot be seen is an
                    // arena with no volcanoes.
                    t.fogColor = new Color(0.42f, 0.10f, 0.055f);
                    t.fogDensity = 0.0019f;

                    // Lit from below as well as above: the ground colour is the glow off
                    // the river and the vents, and it is what stops every face turned away
                    // from a low sun going black -- the desert records that a face in
                    // shadow dark enough to hide an enemy is a fault, not a mood.
                    t.ambientSky = new Color(0.50f, 0.13f, 0.08f);
                    t.ambientEquator = new Color(0.46f, 0.12f, 0.06f);
                    t.ambientGround = new Color(0.62f, 0.17f, 0.05f);

                    // ---- the ground ----
                    // Cooled basalt: near black, with the glow in its cracks supplied by
                    // the material rather than by the colour. Light enough to model the
                    // relief under a low red sun; at true basalt black the dunes vanish.
                    t.floorColor = new Color(0.20f, 0.14f, 0.13f);
                    t.floorSmoothness = 0.35f;
                    t.floorDetail = "Basalt";
                    t.wallDetail = "Rock";
                    t.floorDetailSize = 9f;
                    t.wallDetailSize = 4f;
                    // Block-built walls, the survey crews' own: dark stone, not mud brick.
                    t.wallColor = new Color(0.30f, 0.26f, 0.24f);
                    t.coverColors = new[]
                    {
                        new Color(0.30f, 0.27f, 0.26f),
                        new Color(0.42f, 0.24f, 0.16f),
                        new Color(0.22f, 0.21f, 0.21f)
                    };
                    t.coverTag = "Concrete";
                    t.coverMetallic = 0.2f;

                    // ---- the layout: the open zone, dressed as a volcanic field ----
                    // The desert's layout because it is the one that has been proven --
                    // the gorge, the bridges and the kill volume that follows the meander
                    // all pass VerifyZone -- and a lava river is the same obstacle as a
                    // water one: an edge you must not cross except where the level says.
                    t.openZone = true;
                    t.volcanicZone = true;
                    t.arenaSize = 450f;
                    t.apronSize = 1100f;
                    t.wallHeight = 5f;

                    t.hazard = LevelTheme.Hazard.Lava;
                    t.hazardWidth = 48f;
                    // Shallower than the desert's twenty-two. The whole point of a lava
                    // river is that you can see it from where you stand, and at twenty
                    // metres down it is a glow at the bottom of a hole.
                    t.hazardDepth = 13f;
                    t.hazardOffset = 96f;
                    t.hazardMeander = 26f;
                    t.hazardColor = new Color(1f, 0.42f, 0.08f);
                    // The canyon rock: basalt with iron in it.
                    t.bankColor = new Color(0.30f, 0.22f, 0.19f);
                    t.bridgeColor = new Color(0.26f, 0.23f, 0.22f);
                    t.bridgeWidth = 9f;
                    t.bridgeCount = 2;

                    // Ash dunes over a lava plain: lower and longer than the desert's sand,
                    // because a cooled flow is broad and rolling rather than crested. Still
                    // relaxed to the same 26 degrees -- the vehicle cap is not a desert rule.
                    t.duneHeight = 8f;
                    t.duneWavelength = 110f;
                    t.duneWindAngle = 58f;

                    // The horizon is volcanoes, several of them smoking.
                    t.backdropCount = 30;
                    t.backdropDistance = new Vector2(620f, 1200f);
                    t.backdropHeight = new Vector2(70f, 240f);
                    t.backdropWidth = new Vector2(120f, 360f);
                    t.backdropColor = new Color(0.20f, 0.12f, 0.10f);
                    t.landmarkCount = 10;

                    t.outpostCount = 8;
                    t.vantageCount = 9;
                    t.coverLineCount = 20;
                    t.scatterClusterCount = 52;

                    // Unused while openZone is on.
                    t.roomCount = 3;
                    t.platformCount = 3;
                    t.coverWallCount = 7;
                    t.crateStackCount = 6;
                    t.pillarCount = 5;
                    t.propCount = 0;
                    t.accentLightCount = 0;
                    t.accentLightColor = new Color(1f, 0.45f, 0.15f);

                    // Bloom is what makes the lava glow rather than merely be orange.
                    t.bloomIntensity = 0.9f;
                    t.saturation = 18f;
                    t.contrast = 16f;
                    t.colorFilter = new Color(1f, 0.80f, 0.72f);
                    t.vignetteIntensity = 0.38f;
                    t.filmGrain = 0.18f;
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
