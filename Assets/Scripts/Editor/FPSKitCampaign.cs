#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Generates the <see cref="CampaignData"/> asset: the order the zones are played
    /// in, who holds each one, and what falling to the player hands over.
    ///
    /// The same shape as <see cref="FPSKitLevels"/>, <see cref="FPSKitEnemyRoster"/>,
    /// <see cref="FPSKitThemes"/> and <see cref="FPSKitStore"/>, and with the same trap:
    /// an existing asset is left alone by <see cref="GetOrCreate"/>, so <b>a line of
    /// story edited here does not reach the asset the game reads until
    /// <see cref="ResetAll"/> runs.</b> FPSKitBatch.ResetCampaign is the way in.
    ///
    /// <b>This file also owns the order the arenas are played in</b>, in
    /// <see cref="ZoneOrder"/>, and <see cref="FPSKitLevels"/> reads it for the arena
    /// index its difficulty curve is shifted by. Those have to be the same list or the
    /// third zone the player reaches is tuned as though it were the second -- which
    /// compiles, builds and ships as a difficulty curve with a dip in it.
    /// </summary>
    public static class FPSKitCampaign
    {
        public const string CampaignFolder = "Assets/FPSKit_Generated/Campaign";
        public const string CampaignPath = CampaignFolder + "/Campaign.asset";

        /// <summary>
        /// The arenas in the order the story plays them, by theme name.
        ///
        /// Industrial Warehouse is first on purpose, and not only because it is where the
        /// story starts: it is the arena built as the neutral baseline -- an overcast box
        /// with clean sightlines -- which is what a first zone has to be. Mars is last for
        /// the same reason in reverse.
        ///
        /// Deliberately not the same list as <see cref="FPSKitThemes.Names"/>, which is
        /// the order the arenas were built in and means nothing to a player.
        /// </summary>
        public static readonly string[] ZoneOrder =
        {
            "Industrial Warehouse",
            "Snowbound Station",
            "Desert Outpost",
            "Night Rooftop",
            "Abandoned Subway",
            "Mars Colony",
            FPSKitThemes.FinaleName
        };

        /// <summary>
        /// Who holds each zone, in the same order as <see cref="ZoneOrder"/>, and what
        /// they held.
        ///
        /// <b>Here rather than only inside Configure</b>, because two generators need it:
        /// this one builds the sibling list from it, and <see cref="FPSKitLevels"/> names
        /// the last level of each ladder after whoever is standing at the end of it. Two
        /// tables would drift, and the way they would drift is a level called TOVE AUGER
        /// in an arena the campaign says Kestrel holds.
        ///
        /// The eight are longer than the six zones: the last two hold no site.
        /// </summary>
        public static readonly string[] SiblingNames =
        {
            "Tove Auger", "Kestrel Auger", "Aurel Auger", "Ilsa Auger",
            "Dev Auger", "Roan Auger", "Marit Auger", "the eighth name"
        };

        public static readonly string[] SiblingHoldings =
        {
            "the plant", "the quarry", "the convoy road", "the cameras",
            "the works", "the colony", "the company", "nothing yet"
        };

        /// <summary>
        /// One sentence of story for a level, in the voice of the zone it is in.
        ///
        /// Four rungs rather than forty-eight bespoke lines: arriving, settling in,
        /// being noticed, and the last one. <b>A brief is read at a glance on a tile and
        /// for three seconds under a countdown</b>, so what it has to do is say where the
        /// player is and that something is moving -- and forty-eight hand-written lines
        /// would be forty-eight chances for one of them to say something the level does
        /// not do. The objective sentence is added after this by
        /// <see cref="FPSKitLevels"/>, so every brief says a place and a task.
        /// </summary>
        public static string StoryClause(string themeName, int rung) => themeName switch
        {
            "Industrial Warehouse" => rung switch
            {
                0 => "Tove's plant still runs three shifts. Nobody has told the yard why.",
                1 => "The shift supervisors have stopped pretending this is a drill.",
                2 => "They are pulling people off the line to hold the floor now.",
                _ => "Tove is on the hall roof. She has not tried to leave."
            },

            "Snowbound Station" => rung switch
            {
                0 => "Kestrel lit the yard a week ago and has kept it lit.",
                1 => "Her people know the ground and are letting you have the open part of it.",
                2 => "The quarry crews have stopped working and started waiting.",
                _ => "Kestrel is at the cut face, standing where the charges are kept."
            },

            "Desert Outpost" => rung switch
            {
                0 => "Aurel's road carries everything the company still owns.",
                1 => "The convoy escort has been doubled since the quarry went quiet.",
                2 => "They have stopped moving freight and started moving people.",
                _ => "Aurel is at the crossing, with the manifests, waiting to explain."
            },

            "Night Rooftop" => rung switch
            {
                0 => "Ilsa has had you on camera since the airport.",
                1 => "She is not hiding the cameras. She is showing you where they are.",
                2 => "Every lift in the block has been called to the top floor.",
                _ => "Ilsa is on the last roof, and she has turned the recording on."
            },

            "Abandoned Subway" => rung switch
            {
                0 => "Dev turned the lights on for you. Only some of them.",
                1 => "The rides are moving. Nobody is operating them.",
                2 => "He has opened the shafts that were fenced off.",
                _ => "Dev is waiting by the wheel, and he knows your first name."
            },

            "Mars Colony" => rung switch
            {
                0 => "Roan's colony has been sealed since the road went quiet.",
                1 => "The dome crews were told you are a contractor. They know better now.",
                2 => "Someone on the ground is setting the vent schedule against him too.",
                _ => "Roan is in the open, unsealed, with one question left to ask you."
            },

            FPSKitThemes.FinaleName => rung switch
            {
                0 => "The gate is open and nobody is on it.",
                1 => "Her people are in the house, and they were told to expect you.",
                2 => "Every door on this floor has somebody behind it.",
                _ => "Marit is in his study, with the file open, waiting."
            },

            _ => ""
        };

        /// <summary>Who holds this arena, or empty for one the story does not use.</summary>
        public static string SiblingFor(string themeName)
        {
            int index = IndexOfTheme(themeName);
            return index >= 0 && index < SiblingNames.Length ? SiblingNames[index] : "";
        }

        /// <summary>
        /// Where a theme sits in the campaign, or -1 for one the story does not use.
        /// This is what <see cref="FPSKitLevels"/> shifts its difficulty curve by.
        /// </summary>
        public static int IndexOfTheme(string themeName)
        {
            for (int i = 0; i < ZoneOrder.Length; i++)
                if (ZoneOrder[i] == themeName) return i;

            return -1;
        }

        // ==================================================================
        [MenuItem("FPSKit/Create Campaign", false, 48)]
        public static void CreateMenu()
        {
            var data = GetOrCreate();

            EditorUtility.DisplayDialog("FPSKit Campaign",
                $"The campaign is ready in:\n{CampaignPath}\n\n" +
                $"{data.ZoneCount} zones and {data.siblings.Count} names.\n\n" +
                "Reorder the zones to change the order the story is played in, or move " +
                "which zone hands the bomb over -- neither needs a scene rebuilt. The " +
                "dashboard reads this at runtime.", "OK");

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = data;
        }

        [MenuItem("FPSKit/Reset Campaign to Defaults", false, 49)]
        public static void ResetMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset the campaign",
                "Re-applies the built-in zone order, the eight names and every story " +
                "beat.\n\nAny writing you did in the Inspector will be lost. Stars the " +
                "player has already earned are not touched.", "Reset it", "Cancel")) return;

            ResetAll();
        }

        /// <summary>Re-stamps the built-in campaign, with no dialog.</summary>
        public static void ResetAll()
        {
            var data = GetOrCreate();

            Configure(data);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            int wired = 0;
            foreach (var zone in data.zones)
                if (zone != null && zone.levels != null) wired++;

            Debug.Log($"<color=lime>[FPSKit]</color> Campaign reset: {data.ZoneCount} zones " +
                      $"({wired} with a level set), {data.siblings.Count} names.");
        }

        /// <summary>
        /// The campaign asset, created on first use and then left alone. This is what the
        /// dashboard builder wires into the menu.
        /// </summary>
        public static CampaignData GetOrCreate()
        {
            EnsureFolders();

            var existing = AssetDatabase.LoadAssetAtPath<CampaignData>(CampaignPath);
            if (existing != null) return existing;

            var data = ScriptableObject.CreateInstance<CampaignData>();
            AssetDatabase.CreateAsset(data, CampaignPath);

            Configure(data);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<CampaignData>(CampaignPath);
        }

        // ==================================================================
        // The story
        //
        // Eight children, six sites. Six of them hold a zone; the seventh holds the
        // company rather than a place and arrives with an arena of her own; the eighth
        // is the player, which is the turn the whole thing is built on and is why the
        // list is eight long while the ladder is shorter.
        //
        // Tove is first, and it is deliberate that the player gets the name they came
        // for in the opening zone: the revenge is available early and settles nothing,
        // which is what turns the next five zones from a list into a question.
        // ==================================================================

        static void Configure(CampaignData data)
        {
            data.prologue =
                "The town had one employer and one gate, and Halvard Auger owned both.\n\n" +
                "You were eleven the night the plant burned. Your mother put you through " +
                "the gate and did not come through it herself. The report said sixteen " +
                "dead and a fault in a gas line, and it was signed by one of his " +
                "children.\n\n" +
                "The old man is twenty years dead. The eight are still holding his sites.";

            data.identityQuestion = "Before we start -- were you a boy, or a girl?";

            // The beats, in the same order as the table above. Split from the names
            // rather than written beside them so the names stay a table two generators
            // can share.
            var beats = new[]
            {
                "Tove signed the report that called it a gas fault. She did not deny it " +
                "and she did not apologise for it. What she said, at the end, was that " +
                "the gate your mother put you through was not an escape -- it was a " +
                "transfer, and it was arranged.",

                "Kestrel kept the charges her father bought and never had to use. You are " +
                "carrying one of them now. She was the only one of them who seemed to " +
                "think you had a right to be there.",

                "Aurel moved everything the company ever owned, including, once, a child. " +
                "He remembered the manifest. He remembered the weight.",

                "Ilsa had watched you for twenty years and filed none of it. Someone was " +
                "paying her not to. She told you the amount before she told you the name, " +
                "because she thought the amount would mean more.",

                "Dev stopped running the works the year they stopped being worth running " +
                "and moved into what was left. He was the first of them to call you by " +
                "your first name, and he did it without having to be told it.",

                "Roan left before any of it and has spent the years since being the one " +
                "who was not there. He is the only one who asked you a question. It was " +
                "whether you had worked out yet who had been funding you.",

                "Marit has been paying for this since the night it started. Eight ways is " +
                "a bad division of an estate and one way is a better one, and until " +
                "tonight she had never once had to hold a rifle to arrange it.\n\nShe " +
                "was not surprised to see you. She had your file open on the desk.",

                "Marit's ledger has eight columns and the last one has your name at the " +
                "top of it.\n\nHalvard Auger took one child out of that fire on " +
                "purpose. The town buried sixteen and wrote a seventeenth off as gone, " +
                "and the {child} who went through that gate was written into the will " +
                "the same week. You have spent twenty years crossing your own family off " +
                "a list, in the order somebody else put them in.\n\nThere is one name " +
                "left on it."
            };

            data.siblings = new List<CampaignData.Sibling>();

            for (int i = 0; i < SiblingNames.Length; i++)
                data.siblings.Add(Sibling(SiblingNames[i], SiblingHoldings[i], beats[i]));

            data.zones = new List<CampaignData.Zone>
            {
                Zone("Industrial Warehouse", 0, Campaign.BombPower,
                    "The town had one employer and one gate. You were eleven the night " +
                    "this place burned, and the report that called it a gas fault was " +
                    "signed by Halvard Auger's daughter. Twenty years on, Tove still " +
                    "runs the plant."),

                Zone("Snowbound Station", 1, "",
                    "Kestrel took the quarry and everything in its magazine. She has " +
                    "known you were coming since the plant went quiet, and she has been " +
                    "lighting the yard for it."),

                Zone("Desert Outpost", 2, "",
                    "Everything the company ever moved went down this road, and Aurel " +
                    "signed for all of it. Including, once, a manifest with a child on it."),

                Zone("Night Rooftop", 3, "",
                    "Ilsa runs what the company calls security, which is every camera in " +
                    "the city and the decision about which recordings exist. She has " +
                    "watched you for twenty years and filed none of it."),

                Zone("Abandoned Subway", 4, "",
                    "Dev ran the works into the ground and then moved into what was left. " +
                    "The rides still turn when the wind is behind them. He kept the " +
                    "lights on over the shafts, which is either a courtesy or a trap."),

                Zone("Mars Colony", 5, "",
                    "Roan left before any of this happened and has spent every year since " +
                    "being the one who was not there. He is the last name on the list " +
                    "that still lives somewhere with a door on it."),

                Zone(FPSKitThemes.FinaleName, 6, "", extraBeat: 7,
                    opening:
                    "The other six are down and the company has one owner left. Marit " +
                    "has not moved, has not hired anybody, and has not stopped paying " +
                    "you.\n\nThe house is where it always was. The gate is open.")
            };

            // A face per name. Drawn from constants, so this is idempotent and costs
            // nothing to re-run; wired here because a portrait nothing points at is a
            // file on disk rather than a face on a card.
            var names = new string[data.siblings.Count];
            for (int i = 0; i < data.siblings.Count; i++) names[i] = data.siblings[i].displayName;

            FPSKitPortraits.GenerateAll(names);

            for (int i = 0; i < data.siblings.Count; i++)
                data.siblings[i].portrait = FPSKitPortraits.Load(names[i]);

            // Marit's arena is the seventh zone and does not exist yet -- it is a walled
            // box the size of a house rather than another 450m arena, and it is built in
            // its own pass. It is deliberately not stubbed in here with a null level set:
            // a zone with no ladder is a locked card the player can never open, which is
            // indistinguishable on screen from a bug.
        }

        static CampaignData.Sibling Sibling(string name, string holding, string beat)
            => new CampaignData.Sibling { displayName = name, holding = holding, beat = beat };

        /// <summary>
        /// One zone, wired to the level set the level generator made for that theme.
        ///
        /// It asks <see cref="FPSKitLevels.GetOrCreate"/> rather than loading a path, so
        /// a campaign reset on a project whose ladders have never been generated makes
        /// them rather than writing a zone with nothing in it.
        /// </summary>
        static CampaignData.Zone Zone(string themeName, int siblingIndex, string grantsPower,
                                      string opening, int extraBeat = -1)
        {
            var levels = FPSKitLevels.GetOrCreate(themeName);

            return new CampaignData.Zone
            {
                displayName = themeName,
                levels = levels,
                siblingIndex = siblingIndex,
                grantsPower = grantsPower,
                opening = opening,
                extraBeatSibling = extraBeat
            };
        }

        /// <summary>
        /// Why the clock exists in each arena, in that arena's own voice.
        ///
        /// <b>Written here and stamped onto the LevelSet by
        /// <see cref="FPSKitLevels"/>.</b> The line is read in the arena, by the HUD,
        /// during the briefing -- and the arena holds its ladder already while it holds
        /// no campaign asset at all. Putting the string on the ladder means no scene
        /// needs a new reference and no arena has to be rebuilt to change a word of it;
        /// keeping the text here means the story is still written in one file.
        ///
        /// A theme the campaign does not use gets nothing, and the HUD simply shows the
        /// countdown as it always did.
        /// </summary>
        public static string StakesFor(string themeName) => themeName switch
        {
            "Industrial Warehouse" =>
                "The plant runs to a shift clock and the gates seal when it ends. Finish " +
                "this before they do -- nobody opens them from the outside, and nobody is " +
                "coming to look.",

            "Snowbound Station" =>
                "The storm closes the pass on a schedule the weather does not negotiate. " +
                "Be finished before it shuts, or the station keeps you until spring, and " +
                "it will not be keeping you alive.",

            "Desert Outpost" =>
                "The convoy window is the only traffic through here all day. Miss it and " +
                "you are walking, and the distances out here are not walkable.",

            "Night Rooftop" =>
                "Ilsa holds the lifts and the lights. When her cycle ends the roof seals " +
                "with whoever is on it still on it, and the only way down from here is " +
                "the fast one.",

            "Abandoned Subway" =>
                "The ground here is older than the fence around it. When the floodlights " +
                "go, the holes you have been walking round stop being visible -- and they " +
                "are still exactly where they were.",

            "Mars Colony" =>
                "The dome vents to schedule and the schedule is set from the ground, by " +
                "his sister. Be out of the open when it goes, because it will go whether " +
                "or not you are.",

            FPSKitThemes.FinaleName =>
                "Nobody is coming and nothing here is on a timer but her patience. When " +
                "it runs out the shutters come down on the whole house, and the last " +
                "thing this family does is keep you in it.",

            _ => ""
        };

        // ==================================================================
        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(CampaignFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Campaign");
        }
    }
}
#endif
