#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Stamps audio import settings onto a clip the moment it lands in the project,
    /// so audio arrives already optimized instead of being audited after the fact.
    ///
    /// The kit ships with no audio assets, so this policy defines the convention
    /// rather than describing one. Drop clips under Assets/Audio/&lt;Category&gt;/ and
    /// the importer picks load type, codec, channel count and mobile sample rate to
    /// match how that category is actually played back:
    ///
    ///   Assets/Audio/Music/     long, 2D, one at a time            -> Streaming
    ///   Assets/Audio/Ambience/  LevelTheme.ambienceLoop            -> Streaming
    ///   Assets/Audio/Voice/     barks, callouts                    -> CompressedInMemory
    ///   Assets/Audio/UI/        HUD clicks and blips               -> DecompressOnLoad
    ///   Assets/Audio/SFX/       weapons, enemies, impacts, steps   -> DecompressOnLoad
    ///
    /// A clip outside those folders falls back to a filename keyword scan, then to
    /// SFX, because that is what nearly every clip in this kit is.
    ///
    /// Settings are stamped on FIRST import only -- when Unity is still creating the
    /// .meta file. A clip you have since hand-tuned in the importer inspector is
    /// never overwritten by a later reimport, which matters because a branch switch
    /// or a Library rebuild reimports everything. To deliberately re-stamp, use
    /// FPSKit > Audio > Apply Import Policy to All Clips.
    ///
    /// This is the one part of the kit that is NOT regenerable from the scene
    /// builder: import settings live in .meta files, and only a real import writes
    /// them. Hand-editing a .meta does not apply the change.
    /// </summary>
    public class FPSKitAudioImportPolicy : AssetPostprocessor
    {
        // ==================================================================
        // Thresholds
        // ==================================================================

        /// <summary>
        /// Above this estimated decompressed size, Decompress On Load stops being a
        /// free win and starts being the memory bloat it is meant to avoid: the clip
        /// sits in RAM as raw PCM for the whole scene. Short weapon and impact SFX
        /// land far under it; a five-second ambience tail does not.
        /// </summary>
        private const long DecompressOnLoadMaxBytes = 512 * 1024;

        /// <summary>
        /// Above this, even holding the compressed bytes in memory is wasteful and
        /// the clip should stream off disk instead. Sits well above any SFX and well
        /// below any music bed, so the middle band is genuinely "occasional clip".
        /// </summary>
        private const long CompressedInMemoryMaxBytes = 4 * 1024 * 1024;

        /// <summary>
        /// A compressed source file expands roughly tenfold when decoded to 16-bit
        /// PCM (a 128 kbps Vorbis against 1411 kbps stereo). Used to estimate the
        /// runtime cost of an .ogg or .mp3 from its size on disk, since the clip
        /// does not exist yet while the importer is being configured.
        /// </summary>
        private const long CompressedSourceExpansion = 10;

        /// <summary>
        /// Mobile SFX at 22050 Hz cost exactly half the PCM memory of 44100 Hz and
        /// the difference is inaudible on a phone speaker. Music and ambience keep
        /// their authored rate -- that is where halving is audible.
        /// </summary>
        private const uint MobileSfxSampleRate = 22050;

        /// <summary>
        /// Platform names for the sample-setting overrides. These are
        /// UnityEditor.BuildTargetGroup member names, not build target names --
        /// "iOS", never "iPhone" (the legacy alias) and never "StandaloneLinux64".
        /// </summary>
        private const string AndroidPlatform = "Android";
        private const string IosPlatform = "iOS";

        // ==================================================================
        // Categories
        // ==================================================================

        private enum Category { Music, Ambience, Voice, UI, Sfx }

        /// <summary>
        /// Everything the importer needs to decide, resolved from the category and
        /// the source file size before the clip itself exists.
        /// </summary>
        private struct Policy
        {
            public AudioClipLoadType LoadType;
            public AudioCompressionFormat Format;
            public float Quality;
            public bool ForceToMono;
            public bool LoadInBackground;
            public bool PreloadAudioData;

            /// <summary>Zero means "keep whatever the file was authored at".</summary>
            public uint MobileSampleRate;
        }

        // ==================================================================
        // Import hook
        // ==================================================================

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/")) return;

            var importer = (AudioImporter)assetImporter;

            // importSettingsMissing is true only while Unity is creating the .meta
            // for the first time. Gating on it is the whole reason a later reimport
            // cannot wipe hand-tuning.
            if (!importer.importSettingsMissing) return;

            Apply(importer, PolicyFor(assetPath));
        }

        // ==================================================================
        // Policy resolution
        // ==================================================================

        private static Category Classify(string assetPath)
        {
            string lower = assetPath.ToLowerInvariant();

            // Folder wins: it is the convention this policy is asking people to use.
            if (lower.Contains("/audio/music/")) return Category.Music;
            if (lower.Contains("/audio/ambience/") || lower.Contains("/audio/ambient/")) return Category.Ambience;
            if (lower.Contains("/audio/voice/") || lower.Contains("/audio/vo/")) return Category.Voice;
            if (lower.Contains("/audio/ui/")) return Category.UI;
            if (lower.Contains("/audio/sfx/")) return Category.Sfx;

            // Filename fallback, so a folder of loose clips still classifies sensibly
            // instead of silently defaulting.
            string name = Path.GetFileNameWithoutExtension(lower);
            if (name.Contains("music") || name.Contains("theme") || name.Contains("track")) return Category.Music;
            if (name.Contains("ambien") || name.Contains("wind") || name.Contains("room")) return Category.Ambience;
            if (name.Contains("voice") || name.Contains("bark") || name.Contains("dialog")) return Category.Voice;
            if (name.Contains("ui_") || name.Contains("click") || name.Contains("hover") || name.Contains("menu")) return Category.UI;

            return Category.Sfx;
        }

        private static Policy PolicyFor(string assetPath)
        {
            Category category = Classify(assetPath);
            long pcmBytes = EstimatedPcmBytes(assetPath);

            switch (category)
            {
                case Category.Music:
                case Category.Ambience:
                    // Always streamed, whatever the size, so RAM stays flat no matter
                    // how long the bed is. Stereo is kept on purpose: both play 2D
                    // (the builder sets spatialBlend 0 on the Ambience source), so the
                    // stereo image is the point rather than wasted memory.
                    return Build(AudioClipLoadType.Streaming, 0.5f, forceToMono: false, mobileRate: 0);

                case Category.Voice:
                    // Vorbis at the 0.5 default is audibly rough on speech, so this is
                    // the one category that pays for quality. Mono because a bark is
                    // positional, and a stereo source on a 3D AudioSource plays only
                    // its left channel. Never Decompress On Load -- a line of dialogue
                    // is long enough that holding raw PCM is the wrong trade even when
                    // it squeaks under the threshold.
                    return Build(
                        AtLeast(LoadTypeForSize(pcmBytes), AudioClipLoadType.CompressedInMemory),
                        0.8f, forceToMono: true, mobileRate: MobileSfxSampleRate);

                case Category.UI:
                    // 2D and tiny. Mono anyway -- a HUD click carries no stereo
                    // information worth double the memory. Capped below Streaming
                    // because a UI sound has to be audible on the same frame it is
                    // triggered, and streaming puts a disk read in that path.
                    return Build(
                        AtMost(LoadTypeForSize(pcmBytes), AudioClipLoadType.CompressedInMemory),
                        0.7f, forceToMono: true, mobileRate: MobileSfxSampleRate);

                default:
                    // The kit's bread and butter: weapon fire, impacts, footsteps,
                    // enemy barks, pickups. Force To Mono is not an optimization here
                    // but a correctness fix -- the enemy AudioSource the builder
                    // creates runs at spatialBlend 1 (FPSKitSceneBuilder.cs:1068),
                    // where a stereo clip plays its left channel only.
                    return Build(LoadTypeForSize(pcmBytes), 0.7f, forceToMono: true, mobileRate: MobileSfxSampleRate);
            }
        }

        /// <summary>
        /// Derives the flags that must agree with the load type, so no category can
        /// ask for a contradiction: only a streamed clip wants background loading,
        /// and only a fully decompressed one is worth preloading with the scene.
        /// </summary>
        private static Policy Build(AudioClipLoadType loadType, float quality, bool forceToMono, uint mobileRate)
        {
            return new Policy
            {
                LoadType = loadType,
                Format = AudioCompressionFormat.Vorbis,
                Quality = quality,
                ForceToMono = forceToMono,
                LoadInBackground = loadType == AudioClipLoadType.Streaming,
                PreloadAudioData = loadType == AudioClipLoadType.DecompressOnLoad,
                MobileSampleRate = mobileRate
            };
        }

        /// <summary>
        /// Raises a load type to a floor, where the floor is expressed as "keep at
        /// most this much audio resident". Lets a category insist on at least
        /// Compressed In Memory while the size check can still push a big clip all
        /// the way to Streaming.
        /// </summary>
        private static AudioClipLoadType AtLeast(AudioClipLoadType chosen, AudioClipLoadType floor)
        {
            return Residency(chosen) >= Residency(floor) ? chosen : floor;
        }

        /// <summary>
        /// The mirror of <see cref="AtLeast"/>: clamps a load type to a ceiling, so a
        /// category can refuse to stream no matter how large the file turns out to be.
        /// </summary>
        private static AudioClipLoadType AtMost(AudioClipLoadType chosen, AudioClipLoadType ceiling)
        {
            return Residency(chosen) <= Residency(ceiling) ? chosen : ceiling;
        }

        /// <summary>
        /// Orders the load types by how much audio each keeps in memory: 0 is fully
        /// decompressed PCM, 2 is streamed off disk. Only the ordering matters.
        /// </summary>
        private static int Residency(AudioClipLoadType loadType)
        {
            switch (loadType)
            {
                case AudioClipLoadType.DecompressOnLoad: return 0;
                case AudioClipLoadType.CompressedInMemory: return 1;
                default: return 2;
            }
        }

        private static AudioClipLoadType LoadTypeForSize(long pcmBytes)
        {
            if (pcmBytes <= DecompressOnLoadMaxBytes) return AudioClipLoadType.DecompressOnLoad;
            if (pcmBytes <= CompressedInMemoryMaxBytes) return AudioClipLoadType.CompressedInMemory;
            return AudioClipLoadType.Streaming;
        }

        /// <summary>
        /// Estimates what the clip will cost as raw PCM. The AudioClip does not exist
        /// while OnPreprocessAudio runs, so this reads the source file instead: for a
        /// lossless source the file size already is the PCM size, and for a lossy one
        /// it is scaled by the usual decode expansion.
        /// </summary>
        private static long EstimatedPcmBytes(string assetPath)
        {
            long fileBytes;
            try
            {
                fileBytes = new FileInfo(assetPath).Length;
            }
            catch (IOException)
            {
                // Unreadable source: assume small, so a clip is never pushed to
                // Streaming (the costliest mistake) on the strength of a failed stat.
                return 0;
            }

            string ext = Path.GetExtension(assetPath).ToLowerInvariant();
            bool lossless = ext == ".wav" || ext == ".aiff" || ext == ".aif";
            return lossless ? fileBytes : fileBytes * CompressedSourceExpansion;
        }

        // ==================================================================
        // Application
        // ==================================================================

        private static void Apply(AudioImporter importer, Policy policy)
        {
            importer.forceToMono = policy.ForceToMono;
            importer.loadInBackground = policy.LoadInBackground;

            // Normalize (the checkbox beside Force To Mono) is left alone. It defaults
            // on, which is what preserves perceived level when two channels are summed
            // to one, and it has no stable public setter on AudioImporter.

            var defaults = importer.defaultSampleSettings;
            defaults.loadType = policy.LoadType;
            defaults.compressionFormat = policy.Format;
            defaults.quality = policy.Quality;
            defaults.preloadAudioData = policy.PreloadAudioData;
            defaults.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = defaults;

            // Mobile overrides rather than a switch on the active build target: an
            // override is stored in the .meta and stays correct when someone changes
            // platform, whereas a target-dependent default would silently be whatever
            // platform happened to be active at import time.
            var android = defaults;
            android.compressionFormat = AudioCompressionFormat.Vorbis;
            ApplyMobileRate(ref android, policy.MobileSampleRate);
            SetOverride(importer, AndroidPlatform, android);

            // AAC on iOS decodes in hardware, so it is both cheaper on CPU and better
            // at equal bitrate than Vorbis.
            var ios = defaults;
            ios.compressionFormat = AudioCompressionFormat.AAC;
            ApplyMobileRate(ref ios, policy.MobileSampleRate);
            SetOverride(importer, IosPlatform, ios);
        }

        /// <summary>
        /// SetOverrideSampleSettings reports an unrecognised platform name by
        /// returning false rather than throwing, which would leave the override
        /// quietly unapplied and the clip shipping at its desktop settings. Surface
        /// it instead -- a typo here is otherwise invisible until a device profile.
        /// </summary>
        private static void SetOverride(AudioImporter importer, string platform, AudioImporterSampleSettings settings)
        {
            if (importer.SetOverrideSampleSettings(platform, settings)) return;

            Debug.LogWarning(
                $"[FPSKit Audio] Unity rejected the '{platform}' sample-setting override for " +
                $"{importer.assetPath}. That platform name must match a UnityEditor.BuildTargetGroup " +
                "member; the clip keeps its default settings on that platform.");
        }

        private static void ApplyMobileRate(ref AudioImporterSampleSettings settings, uint rate)
        {
            if (rate == 0)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                return;
            }

            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = rate;
        }

        // ==================================================================
        // Menu: deliberate re-stamp
        // ==================================================================

        [MenuItem("FPSKit/Audio/Apply Import Policy to All Clips", false, 62)]
        private static void ApplyToAllClips()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "FPSKit Audio",
                    "No AudioClips found under Assets.\n\nDrop clips into Assets/Audio/<Category>/ and they will be configured on import.",
                    "OK");
                return;
            }

            bool go = EditorUtility.DisplayDialog(
                "Apply Audio Import Policy",
                $"Re-stamp import settings on {guids.Length} clip(s)?\n\nThis overwrites any hand-tuning done in the audio importer inspector.",
                "Apply",
                "Cancel");
            if (!go) return;

            int processed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("Applying audio import policy", path, (float)i / guids.Length);

                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null) continue;

                    Apply(importer, PolicyFor(path));
                    importer.SaveAndReimport();
                    processed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[FPSKit Audio] Applied import policy to {processed} clip(s).");
        }

        [MenuItem("FPSKit/Audio/Log Import Policy Report", false, 63)]
        private static void LogReport()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
            if (guids.Length == 0)
            {
                Debug.Log("[FPSKit Audio] No AudioClips under Assets. Nothing to report.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"[FPSKit Audio] Import policy report for {guids.Length} clip(s):");

            int drift = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                Policy want = PolicyFor(path);
                var have = importer.defaultSampleSettings;

                bool matches = have.loadType == want.LoadType
                               && have.compressionFormat == want.Format
                               && Mathf.Approximately(have.quality, want.Quality)
                               && importer.forceToMono == want.ForceToMono
                               && importer.loadInBackground == want.LoadInBackground;

                report.AppendLine(
                    $"  {(matches ? "ok  " : "DRIFT")} {path}\n" +
                    $"        is:     {have.loadType}, {have.compressionFormat} q{have.quality:0.00}, " +
                    $"mono={importer.forceToMono}, bg={importer.loadInBackground}\n" +
                    $"        policy: {want.LoadType}, {want.Format} q{want.Quality:0.00}, " +
                    $"mono={want.ForceToMono}, bg={want.LoadInBackground}");

                if (!matches) drift++;
            }

            report.AppendLine(drift == 0
                ? "  All clips match policy."
                : $"  {drift} clip(s) differ from policy. FPSKit > Audio > Apply Import Policy to All Clips re-stamps them.");

            Debug.Log(report.ToString());
        }
    }
}
#endif
