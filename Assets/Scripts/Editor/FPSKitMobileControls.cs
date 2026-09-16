#if UNITY_EDITOR
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

            BuildLookArea(canvas.transform);
            BuildJoystick(canvas.transform);
            BuildButtons(canvas.transform);

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
        private static void BuildLookArea(Transform parent)
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
            go.GetComponent<TouchLookArea>().tapToFire = false;
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
        private static void BuildJoystick(Transform parent)
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
        }

        /// <summary>
        /// Where the move half of the screen ends and the look half begins.
        ///
        /// Left of this is the stick, right of it is look plus the button cluster. The
        /// split is what stops one thumb's job being done by the other.
        /// </summary>
        private const float LeftRegion = 0.38f;

        // ==================================================================
        private static void BuildButtons(Transform parent)
        {
            // A right-thumb cluster, laid out so nothing overlaps and the two pressed
            // most sit lowest and largest. Positions are bottom-right anchored at the
            // canvas's 1920x1080 reference, and the gaps between them are deliberate:
            // a thumb is about 120 reference-pixels wide, so buttons that touch each
            // other are buttons that get pressed together.
            MakeButton(parent, "FireButton", "FIRE", TouchButton.ActionKind.Fire, false,
                       new Vector2(-230f, 230f), 240f, new Color(1f, 0.42f, 0.35f, 0.32f));

            MakeButton(parent, "AimButton", "ADS", TouchButton.ActionKind.Aim, true,
                       new Vector2(-470f, 330f), 160f, new Color(1f, 1f, 1f, 0.20f));

            MakeButton(parent, "JumpButton", "JUMP", TouchButton.ActionKind.Jump, false,
                       new Vector2(-230f, 490f), 160f, new Color(1f, 1f, 1f, 0.20f));

            MakeButton(parent, "SprintButton", "RUN", TouchButton.ActionKind.Sprint, true,
                       new Vector2(-450f, 560f), 150f, new Color(0.5f, 0.85f, 1f, 0.22f));

            MakeButton(parent, "CrouchButton", "CROUCH", TouchButton.ActionKind.Crouch, true,
                       new Vector2(-660f, 400f), 150f, new Color(1f, 1f, 1f, 0.20f));

            MakeButton(parent, "ReloadButton", "RELOAD", TouchButton.ActionKind.Reload, false,
                       new Vector2(-660f, 200f), 150f, new Color(1f, 0.85f, 0.4f, 0.22f));

            // The bomb is a hold: press to bring the ring up, slide the look around to
            // place it, release to throw. It sits above the fire button because those
            // two are the only controls a thumb uses in the middle of a fight, and it is
            // the larger of the pair the thumb has to find without looking.
            MakeButton(parent, "BombButton", "BOMB", TouchButton.ActionKind.Bomb, false,
                       new Vector2(-450f, 760f), 170f, new Color(1f, 0.6f, 0.2f, 0.26f));

            MakeButton(parent, "ItemButton", "DRINK", TouchButton.ActionKind.UseItem, false,
                       new Vector2(-660f, 600f), 150f, new Color(0.4f, 0.9f, 1f, 0.24f));

            // Top right, away from the thumbs, because it is the one button you never
            // want to hit by accident and the only way off this screen: a phone has no
            // Escape key, so without it a touch player cannot pause, cannot quit and
            // cannot get back to the dashboard.
            var pause = MakeButton(parent, "PauseButton", "II", TouchButton.ActionKind.Pause,
                                   false, Vector2.zero, 110f, new Color(1f, 1f, 1f, 0.18f));

            var pauseRect = (RectTransform)pause.transform;
            pauseRect.anchorMin = pauseRect.anchorMax = new Vector2(1f, 1f);
            pauseRect.anchoredPosition = new Vector2(-90f, -90f);
        }

        private static GameObject MakeButton(Transform parent, string name, string label,
                                            TouchButton.ActionKind action, bool toggle,
                                            Vector2 anchoredPosition, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TouchButton));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = anchoredPosition;

            var image = go.GetComponent<Image>();
            image.sprite = Knob();
            image.color = color;

            var button = go.GetComponent<TouchButton>();
            button.action = action;
            button.toggle = toggle;
            button.target = image;
            button.activeColor = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a + 0.45f));

            // Label
            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = size * 0.20f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 1f, 1f, 0.85f);
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
