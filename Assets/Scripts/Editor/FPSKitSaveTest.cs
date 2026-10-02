#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Proves that renaming an arena carries a player's progress with it.
    ///
    /// Stars, scores and best times are filed under the arena's name, so a rename that
    /// only changed the label would quietly zero every player who had played it. The check
    /// writes progress under the old name, runs the start-up migration and asks for it
    /// under the new one, then asks again to prove the old keys are gone and a second run
    /// changes nothing. It borrows six PlayerPrefs keys and puts back what was there.
    /// </summary>
    public static class FPSKitSaveTest
    {
        public static void VerifySaveRename()
        {
            const string was = "AbandonedSubway", now = "AbandonedFairground";
            string[] keys =
            {
                $"FPSKit.Level.{was}.3.Stars", $"FPSKit.Level.{was}.3.Score", $"FPSKit.Level.{was}.3.BestTime",
                $"FPSKit.Level.{now}.3.Stars", $"FPSKit.Level.{now}.3.Score", $"FPSKit.Level.{now}.3.BestTime",
                SaveVersionKey
            };

            var borrowed = new (bool had, string value)[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                borrowed[i] = (PlayerPrefs.HasKey(keys[i]), PlayerPrefs.GetString(keys[i], ""));

            try
            {
                foreach (var key in keys) PlayerPrefs.DeleteKey(key);

                // A profile already at the current format, so the wipe does not run and
                // only the rename does.
                PlayerPrefs.SetInt(SaveVersionKey, SaveMigration.Version);
                PlayerPrefs.SetInt(keys[0], 2);
                PlayerPrefs.SetInt(keys[1], 4100);
                PlayerPrefs.SetFloat(keys[2], 187.5f);

                SaveMigration.Apply();

                Expect(LevelProgress.StarsIn(now, 3) == 2, "stars did not follow the rename");
                Expect(LevelProgress.BestScoreIn(now, 3) == 4100, "score did not follow the rename");
                Expect(Mathf.Approximately(LevelProgress.BestTimeIn(now, 3), 187.5f), "best time did not follow the rename");
                Expect(LevelProgress.StarsIn(was, 3) == 0, "the old key was left behind");

                // Idempotent, and never clobbers progress already earned under the new name.
                LevelProgress.Record(now, 3, 3, 5000, 150f);
                SaveMigration.Apply();
                Expect(LevelProgress.StarsIn(now, 3) == 3, "a second run changed the new progress");

                // And the rule has to be what did it: with progress only under the old name
                // and no migration the new name is empty, which is the failure this guards.
                PlayerPrefs.DeleteKey(keys[3]);
                PlayerPrefs.SetInt(keys[0], 1);
                Expect(LevelProgress.StarsIn(now, 3) == 0, "the new name read progress before any migration ran");

                Debug.Log("[FPSKitBatch] verify save rename passed: stars, score and best time moved to the new arena name, once.");
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    if (borrowed[i].had) PlayerPrefs.SetString(keys[i], borrowed[i].value);
                    else PlayerPrefs.DeleteKey(keys[i]);
                }

                PlayerPrefs.Save();
            }
        }

        const string SaveVersionKey = "FPSKit.Save.Version";

        static void Expect(bool ok, string message)
        {
            if (!ok) throw new Exception($"save rename: {message}");
        }
    }
}
#endif
