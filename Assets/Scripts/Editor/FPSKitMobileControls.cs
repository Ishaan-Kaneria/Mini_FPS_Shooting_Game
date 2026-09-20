#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds the on-screen control layer into whatever scene is open: thumbstick,
    /// look area, and buttons for fire, aim, jump, sprint, crouch and reload.
    ///
    /// Also adds the EventSystem, without which no UI touch or click event fires
    /// at all -- the generated HUD never had one.
    ///
    /// Menu: FPSKit > Add Mobile Touch Controls
    /// </summary>
    public static class FPSKitMobileControls
    {
        private const string RootName = "Mobile Controls";

        [MenuItem("FPSKit/Add Mobile Touch Controls", false, 22)]
        private static void AddMobileControlsMenu() => AddMobileControls();

        /// <summary>
        /// Builds the control layer into the open scene.
        ///
        /// <paramref name="askFirst"/> exists for the same reason BuildScene has one:
        /// EditorUtility.DisplayDialog cannot be answered in batch mode, so a headless
        /// caller that went through the prompts would cancel on an existing layer and
        /// report success having done nothing. A WebGL build is exactly such a caller.
        /// </summary>
        public static void AddMobileControls(bool askFirst = true)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                if (askFirst && !EditorUtility.DisplayDialog("Replace touch controls",
                    "This scene already has on-screen controls.\n\nRebuild them?",
                    "Rebuild", "Cancel")) return;

                Object.DestroyImmediate(existing);
            }

            EnsureEventSystem();

            var canvas = BuildCanvas();
            var group = canvas.gameObject.AddComponent<CanvasGroup>();

            var controls = canvas.gameObject.AddComponent<TouchControls>();
            controls.group = group;

            var profile = GetOrCreateProfile();

            // Everything the player touches hangs off the safe area rather than off the
            // canvas. A canvas fills the panel, cutout included -- so the pause button,
            // anchored to the top-right corner, is drawn under the front camera on most
            // modern phones held in landscape. It is also the only way out of a level.
            var safeArea = BuildSafeArea(canvas.transform);

            BuildLookArea(safeArea, profile);
            BuildJoystick(safeArea, profile);
            BuildButtons(safeArea, profile);

            AttachAimAssist(profile);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = canvas.gameObject;

            if (!askFirst)
            {
                Debug.Log("[FPSKit] on-screen touch controls added to " + scene.name);
                return;
            }

            EditorUtility.DisplayDialog("FPSKit",
                "On-screen controls added.\n\n" +
                "Left thumbstick moves. Drag anywhere on the right to look, tap to shoot. " +
                "Buttons: FIRE, ADS, JUMP, RUN, CROUCH, RELOAD.\n\n" +
                "They are visible in the editor so you can test with the mouse, and hide " +
                "themselves in a desktop build. Save the scene to keep them.",
                "Got it");
        }

        // ==================================================================
        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));

#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>
        /// Puts aim assist on the player and points the motor at it.
        ///
        /// Built here rather than by the scene builder because it is touch-only: it
        /// compensates for a thumb, and a scene that never gets on-screen controls must
        /// never get it. Wiring it into the arena itself would hand it to desktop
        /// players too, which is both unfair and worse to play.
        /// </summary>
        private static void AttachAimAssist(TouchProfile profile)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("[FPSKit] no Player in this scene, so aim assist was not added.");
                return;
            }

            var motor = player.GetComponent<PlayerMotor>();
            if (motor == null) return;

            var assist = player.GetComponent<TouchAimAssist>();
            if (assist == null) assist = player.AddComponent<TouchAimAssist>();

            assist.profile = profile;
            assist.eye = player.GetComponentInChildren<Camera>();

            // The same mask the enemies use to decide they can see the player, so the
            // two agree about what counts as a wall.
            int environment = LayerMask.NameToLayer("Environment");
            assist.sightBlockers = environment >= 0 ? 1 << environment : ~0;

            motor.aimAssist = assist;
        }

        /// <summary>Where the touch tuning lives, created on first use.</summary>
        public const string ProfilePath = "Assets/FPSKit_Generated/TouchProfile.asset";

        /// <summary>
        /// The one asset every touch control reads its tuning from.
        ///
        /// Created rather than required, so a scene built on a machine that has never
        /// seen one still gets working controls -- and generated rather than hand-made,
        /// so the defaults live in code and can be re-stamped like every other generated
        /// asset in the kit.
        /// </summary>
        public static TouchProfile GetOrCreateProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TouchProfile>(ProfilePath);
            if (existing != null) return existing;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProfilePath));

            var profile = ScriptableObject.CreateInstance<TouchProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[FPSKit] created {ProfilePath}");
            return profile;
        }

        /// <summary>
        /// A full-screen child that insets itself to the safe area at runtime. The
        /// controls parent to this rather than to the canvas, whose RectTransform is
        /// driven by the Canvas component and cannot be inset.
        /// </summary>
        private static Transform BuildSafeArea(Transform parent)
        {
            var go = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            return go.transform;
        }

        private static Canvas BuildCanvas()
        {
            var go = new GameObject(RootName);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;          // above the HUD, below nothing

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ==================================================================
        /// <summary>
        /// Full-screen invisible drag surface. Built first so every button added
        /// afterwards sits above it and swallows its own touches.
        /// </summary>
        private static void BuildLookArea(Transform parent, TouchProfile profile)
        {
            var go = new GameObject("LookArea", typeof(RectTransform), typeof(Image), typeof(TouchLookArea));
            go.transform.SetParent(parent, false);

            // The right of the screen only. It used to cover all of it, which put the
            // look surface underneath the thumbstick as well: a drag on the left either
            // moved *and* turned, or turned instead of moving, depending on which of the
            // two the touch happened to land on.
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(LeftRegion, 0f);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            // Invisible but still raycastable -- alpha 0 would stop receiving events
            // on some setups, so a hair above zero is safer.
            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.004f);

            // Tap-to-fire stays off. With a FIRE button on screen it is redundant, and
            // because this surface is most of the screen it meant every tap that missed
            // a button by a few pixels fired the weapon.
            var look = go.GetComponent<TouchLookArea>();
            look.tapToFire = false;
            look.profile = profile;
        }

        /// <summary>
        /// The move stick: the whole left region is the stick, and the ring appears
        /// wherever the thumb lands.
        ///
        /// A fixed ring in the corner is the wrong shape for a phone -- there is nothing
        /// under the thumb to feel for, so the player misses it, and the miss used to
        /// fall through to the look surface behind. A region that accepts a thumb
        /// anywhere cannot be missed.
        /// </summary>
        private static void BuildJoystick(Transform parent, TouchProfile profile)
        {
            var region = new GameObject("MoveRegion", typeof(RectTransform), typeof(Image),
                                        typeof(VirtualJoystick));
            region.transform.SetParent(parent, false);

            var regionRect = (RectTransform)region.transform;
            regionRect.anchorMin = Vector2.zero;
            regionRect.anchorMax = new Vector2(LeftRegion, 1f);
            regionRect.offsetMin = regionRect.offsetMax = Vector2.zero;
            regionRect.pivot = new Vector2(0.5f, 0.5f);

            // Invisible, and the same near-zero alpha the look area uses so it keeps
            // receiving touches.
            var regionImage = region.GetComponent<Image>();
            regionImage.color = new Color(0f, 0f, 0f, 0.004f);

            var pad = new GameObject("Pad", typeof(RectTransform), typeof(Image),
                                     typeof(CanvasGroup));
            pad.transform.SetParent(region.transform, false);

            var padRect = (RectTransform)pad.transform;
            padRect.anchorMin = padRect.anchorMax = new Vector2(0.5f, 0.5f);
            padRect.pivot = new Vector2(0.5f, 0.5f);
            padRect.sizeDelta = new Vector2(340f, 340f);
            padRect.anchoredPosition = Vector2.zero;

            var padImage = pad.GetComponent<Image>();
            padImage.sprite = Knob();
            padImage.color = new Color(1f, 1f, 1f, 0.14f);
            padImage.raycastTarget = false;

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(pad.transform, false);

            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(150f, 150f);
            handleRect.anchoredPosition = Vector2.zero;

            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = Knob();
            handleImage.color = new Color(1f, 1f, 1f, 0.42f);
            handleImage.raycastTarget = false;

            var joystick = region.GetComponent<VirtualJoystick>();
            joystick.pad = padRect;
            joystick.handle = handleRect;
            joystick.radius = 150f;
            joystick.hideWhenIdle = true;
            joystick.profile = profile;
        }

        /// <summary>
        /// Where the move half of the screen ends and the look half begins.
        ///
        /// Left of this is the stick, right of it is look plus the button cluster. The
        /// split is what stops one thumb's job being done by the other.
        /// </summary>
        private const float LeftRegion = 0.38f;

        // ==================================================================
        /// <summary>
        /// Builds the right-thumb cluster as a set of logical slots and lets
        /// <see cref="TouchCluster"/> work out the geometry on the real device.
        ///
        /// <b>Nothing here is positioned in pixels any more, and that is the fix.</b>
        /// The old layout hand-placed eight buttons against the 1920x1080 reference with
        /// 35 to 60 units between them, which reads like clearance and measures 2.4mm on
        /// every phone tried. A thumb is about 20mm wide. Every check passed because
        /// nothing technically overlapped.
        ///
        /// It is also four buttons rather than eight at rest. Sprint and crouch are gone
        /// by default -- the stick already sprints when pushed to its edge, and crouch is
        /// the least used action on a phone -- and the bomb and the drink appear only
        /// while they are being carried, the same rule the HUD's key strip follows. What
        /// is left is what a thumb uses in a fight.
        /// </summary>
        private static void BuildButtons(Transform parent, TouchProfile profile)
        {
            var clusterGo = new GameObject("ButtonCluster", typeof(RectTransform));
            clusterGo.transform.SetParent(parent, false);

            var clusterRect = (RectTransform)clusterGo.transform;
            clusterRect.anchorMin = Vector2.zero;
            clusterRect.anchorMax = Vector2.one;
            clusterRect.offsetMin = clusterRect.offsetMax = Vector2.zero;

            var cluster = clusterGo.AddComponent<TouchCluster>();
            cluster.profile = profile;
            cluster.slots = new List<TouchCluster.Slot>();

            // Column 0 is nearest the thumb, row 0 is lowest. The sizes and the gaps are
            // millimetres and live on the cluster, not here.
            //
            // FIRE is drag-fire: press it and keep dragging, and the trigger stays down
            // while the same gesture turns the view. Without that the right thumb cannot
            // shoot and aim at once, and every fight becomes a choice between firing at
            // where the enemy was and tracking them without firing.
            var fire = MakeButton(parent, "FireButton", "FIRE", TouchButton.ActionKind.Fire,
                                  false, Accent, profile);

            fire.GetComponent<TouchButton>().dragFire = profile == null || profile.dragFire;
            Slot(cluster, fire, column: 0, row: 0, primary: true);

            var jump = MakeButton(parent, "JumpButton", "JUMP", TouchButton.ActionKind.Jump,
                                  false, Neutral, profile);
            Slot(cluster, jump, column: 0, row: 1);

            var ads = MakeButton(parent, "AimButton", "ADS", TouchButton.ActionKind.Aim,
                                 true, Neutral, profile);
            Slot(cluster, ads, column: 1, row: 0);

            var reload = MakeButton(parent, "ReloadButton", "RELOAD", TouchButton.ActionKind.Reload,
                                    false, Neutral, profile);
            Slot(cluster, reload, column: 1, row: 1);

            // The bomb is a hold: press to bring the ring up, slide the look around to
            // place it, release to throw.
            var bomb = MakeButton(parent, "BombButton", "BOMB", TouchButton.ActionKind.Bomb,
                                  false, BombTint, profile);
            Slot(cluster, bomb, column: 2, row: 0, situational: true);

            var drink = MakeButton(parent, "ItemButton", "DRINK", TouchButton.ActionKind.UseItem,
                                   false, DrinkTint, profile);
            Slot(cluster, drink, column: 2, row: 1, situational: true);

            // Both off by default. They are still built so that flipping the profile is
            // the whole change, rather than a code edit and a rebuild.
            if (profile != null && profile.showSprintButton)
            {
                var run = MakeButton(parent, "SprintButton", "RUN", TouchButton.ActionKind.Sprint,
                                     true, Neutral, profile);
                Slot(cluster, run, column: 1, row: 2);
            }

            if (profile != null && profile.showCrouchButton)
            {
                var crouch = MakeButton(parent, "CrouchButton", "CROUCH", TouchButton.ActionKind.Crouch,
                                        true, Neutral, profile);
                Slot(cluster, crouch, column: 0, row: 2);
            }

            // Top right, away from the thumbs, because it is the one button you never
            // want to hit by accident and the only way off this screen: a phone has no
            // Escape key, so without it a touch player cannot pause, cannot quit and
            // cannot get back to the dashboard. Outside the cluster because it is the
            // only control that is not under a thumb, and TouchButton floors it at the
            // profile's minimum size on its own.
            var pause = MakeButton(parent, "PauseButton", "II", TouchButton.ActionKind.Pause,
                                   false, Neutral, profile);

            var pauseRect = (RectTransform)pause.transform;
            pauseRect.anchorMin = pauseRect.anchorMax = new Vector2(1f, 1f);
            pauseRect.sizeDelta = new Vector2(130f, 130f);
            pauseRect.anchoredPosition = new Vector2(-110f, -110f);

            // Lay it out once now, so the scene that gets saved is a real arrangement
            // rather than nine buttons stacked on the origin waiting for Start. The
            // device does it again at runtime with its own density -- but a scene is
            // also read by the editor, by the build, and by anything that inspects it
            // without entering play mode, and all three deserve to see the truth.
            cluster.Rebuild();
        }

        static void Slot(TouchCluster cluster, GameObject button, int column, int row,
                         bool primary = false, bool situational = false)
        {
            cluster.slots.Add(new TouchCluster.Slot
            {
                button = button.GetComponent<TouchButton>(),
                column = column,
                row = row,
                primary = primary,
                situational = situational
            });
        }

        // ------------------------------------------------------------------
        // One shape for every button: a dark disc with a bright ring and a bright label.
        //
        // What it replaces was five hues at five alphas between 0.18 and 0.32, which is
        // most of what "the controls look noisy" means -- at that opacity a button has no
        // edge, so it is a smudge whose boundary the player has to guess, and five
        // different smudges read as five unrelated things. A ring is a boundary, and one
        // boundary drawn the same way everywhere is what makes a control panel look
        // deliberate. Colour is then free to mean something: the accent is the primary,
        // and the two situational buttons keep a tint so they are recognisable in the
        // corner of the eye.
        // ------------------------------------------------------------------
        private static readonly Color Accent = new Color(1f, 0.73f, 0.25f);
        private static readonly Color Neutral = new Color(0.82f, 0.88f, 0.95f);
        private static readonly Color BombTint = new Color(1f, 0.62f, 0.25f);
        private static readonly Color DrinkTint = new Color(0.45f, 0.9f, 1f);

        /// <summary>
        /// One button: a dark disc, a bright ring around it, and a bright label.
        ///
        /// Size and position are deliberately not set here -- <see cref="TouchCluster"/>
        /// owns both, in millimetres, at runtime. Anything set here would be in reference
        /// units, which is how the cluster came to be 2.4mm apart in the first place.
        /// </summary>
        private static GameObject MakeButton(Transform parent, string name, string label,
                                             TouchButton.ActionKind action, bool toggle,
                                             Color tint, TouchProfile profile)
        {
            float opacity = profile != null ? profile.buttonOpacity : 1f;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TouchButton));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160f, 160f);

            // The ring. This is the graphic that gets hit, so it stays the raycast target.
            var ring = go.GetComponent<Image>();
            ring.sprite = Knob();
            ring.color = new Color(tint.r, tint.g, tint.b, 0.85f * opacity);

            // The fill, inset to leave the ring showing. Dark rather than tinted, because
            // a button sits over whatever the arena happens to be -- bright sand, dark
            // subway -- and a dark disc is the one fill a bright label reads on
            // everywhere. It is not a raycast target: the ring above already is, and two
            // targets on one button is two ways for a press to be attributed.
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(go.transform, false);

            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(7f, 7f);
            fillRect.offsetMax = new Vector2(-7f, -7f);

            var fill = fillGo.GetComponent<Image>();
            fill.sprite = Knob();
            fill.color = new Color(0.03f, 0.05f, 0.07f, 0.62f * opacity);
            fill.raycastTarget = false;

            var button = go.GetComponent<TouchButton>();
            button.action = action;
            button.toggle = toggle;
            button.target = fill;
            button.profile = profile;

            // Pressed brightens the fill towards the button's own colour, so the feedback
            // is on the thing the thumb is covering rather than on the ring around it.
            button.activeColor = new Color(tint.r * 0.55f, tint.g * 0.55f, tint.b * 0.55f,
                                           Mathf.Min(1f, 0.85f * opacity));

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 10f);
            textRect.offsetMax = new Vector2(-10f, -10f);

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;

            // Auto-sized, because the cluster decides how big the button is and a fixed
            // point size would either overflow "RELOAD" or waste half of "II". The floor
            // is what keeps it legible on a small phone.
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 40f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = new Color(1f, 1f, 1f, 0.96f);
            text.raycastTarget = false;

            return go;
        }

        // ==================================================================
        /// <summary>Unity's built-in circular UI sprite. No art needed.</summary>
        private static Sprite Knob()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
    }
}
#endif
