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
        public static void AddMobileControls()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Replace touch controls",
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

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            // Invisible but still raycastable -- alpha 0 would stop receiving events
            // on some setups, so a hair above zero is safer.
            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.004f);
        }

        private static void BuildJoystick(Transform parent)
        {
            var pad = new GameObject("Joystick", typeof(RectTransform), typeof(Image), typeof(VirtualJoystick));
            pad.transform.SetParent(parent, false);

            var padRect = (RectTransform)pad.transform;
            padRect.anchorMin = padRect.anchorMax = new Vector2(0f, 0f);
            padRect.pivot = new Vector2(0.5f, 0.5f);
            padRect.sizeDelta = new Vector2(300f, 300f);
            padRect.anchoredPosition = new Vector2(260f, 260f);

            var padImage = pad.GetComponent<Image>();
            padImage.sprite = Knob();
            padImage.color = new Color(1f, 1f, 1f, 0.16f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(pad.transform, false);

            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(130f, 130f);
            handleRect.anchoredPosition = Vector2.zero;

            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = Knob();
            handleImage.color = new Color(1f, 1f, 1f, 0.45f);
            handleImage.raycastTarget = false;

            var joystick = pad.GetComponent<VirtualJoystick>();
            joystick.handle = handleRect;
            joystick.radius = 110f;
        }

        // ==================================================================
        private static void BuildButtons(Transform parent)
        {
            // Right thumb cluster, laid out so the two you press most sit lowest.
            MakeButton(parent, "FireButton", "FIRE", TouchButton.ActionKind.Fire, false,
                       new Vector2(-230f, 240f), 200f, new Color(1f, 0.42f, 0.35f, 0.30f));

            MakeButton(parent, "AimButton", "ADS", TouchButton.ActionKind.Aim, true,
                       new Vector2(-440f, 200f), 150f, new Color(1f, 1f, 1f, 0.18f));

            MakeButton(parent, "JumpButton", "JUMP", TouchButton.ActionKind.Jump, false,
                       new Vector2(-190f, 450f), 150f, new Color(1f, 1f, 1f, 0.18f));

            MakeButton(parent, "SprintButton", "RUN", TouchButton.ActionKind.Sprint, true,
                       new Vector2(-400f, 400f), 140f, new Color(0.5f, 0.85f, 1f, 0.20f));

            MakeButton(parent, "CrouchButton", "CROUCH", TouchButton.ActionKind.Crouch, true,
                       new Vector2(-600f, 170f), 140f, new Color(1f, 1f, 1f, 0.18f));

            MakeButton(parent, "ReloadButton", "RELOAD", TouchButton.ActionKind.Reload, false,
                       new Vector2(-600f, 340f), 140f, new Color(1f, 0.85f, 0.4f, 0.20f));
        }

        private static void MakeButton(Transform parent, string name, string label,
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
        }

        // ==================================================================
        /// <summary>Unity's built-in circular UI sprite. No art needed.</summary>
        private static Sprite Knob()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
    }
}
#endif
