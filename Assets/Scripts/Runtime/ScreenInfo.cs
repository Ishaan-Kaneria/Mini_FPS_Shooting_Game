using UnityEngine;

/// <summary>
/// The screen the game is laid out for: its size, density, safe area and whether it is
/// reached by a finger. Everything that measures the screen asks here rather than reading
/// <see cref="Screen"/> itself.
///
/// It exists so a layout can be checked against a real phone without the phone. Batch mode
/// has one screen, 640x480 with no density, no notch and no touch -- so a check that read
/// <see cref="Screen"/> directly would lay out every device class as that one screen and
/// pass. <see cref="Simulate"/> puts a device's numbers in place (taken from the Device
/// Simulator's own definitions by <c>FPSKitDeviceShots</c>), and every reader below sees
/// that device instead: the density that sizes a thumb target, the safe area that keeps a
/// button out from under a notch, the form that picks a layout.
///
/// Outside a simulation every property is a straight read of <see cref="Screen"/>.
/// </summary>
public static class ScreenInfo
{
    public struct Device
    {
        public string name;
        public int width, height;
        public float dpi;
        /// <summary>In pixels, bottom-left origin, as <see cref="Screen.safeArea"/> reports it.</summary>
        public Rect safeArea;
        public bool touch;
        public bool mobile;
    }

    static bool _simulating;
    static Device _device;

    /// <summary>True while a simulated device is standing in for the screen.</summary>
    public static bool Simulating => _simulating;
    public static string DeviceName => _simulating ? _device.name : SystemInfo.deviceModel;

    public static int Width => _simulating ? _device.width : Screen.width;
    public static int Height => _simulating ? _device.height : Screen.height;
    public static float RawDpi => _simulating ? _device.dpi : Screen.dpi;
    public static Rect SafeArea => _simulating ? _device.safeArea : Screen.safeArea;

    /// <summary>Whether the simulated device is touch-only. Null when not simulating.</summary>
    public static bool? SimulatedTouch => _simulating ? _device.touch : (bool?)null;

    /// <summary>Whether the simulated device is a phone or tablet OS. Null when not simulating.</summary>
    public static bool? SimulatedMobile => _simulating ? _device.mobile : (bool?)null;

    public static void Simulate(Device device)
    {
        _device = device;
        _simulating = true;
    }

    public static void StopSimulating() => _simulating = false;

    static bool _pending;
    static Device _pendingDevice;

    /// <summary>
    /// Asks for a device to be simulated from the start of the next play session. The
    /// session's own reset clears any simulation, so a capture tool cannot set one before
    /// pressing Play and have it survive; this is taken up in <c>BeforeSceneLoad</c>, after
    /// the reset and before the first <c>Awake</c> asks what the screen is. Used once, then
    /// forgotten, so it cannot leak into the session after.
    /// </summary>
    public static void SimulateNextSession(Device device)
    {
        _pendingDevice = device;
        _pending = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SimulationCamera = null;
        _simulating = false;
        _device = default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void TakePending()
    {
        if (!_pending) return;
        _pending = false;
        Simulate(_pendingDevice);
        // With scene reload off the scene's objects already exist here, before any Awake,
        // which is the earliest the canvases can be given the simulated screen: the touch
        // buttons size themselves against the canvas scale in Awake.
        RouteCanvases();
    }

    /// <summary>
    /// While simulating, the camera the interface is drawn by, rendering into a texture the
    /// size of the simulated screen. Null otherwise.
    /// </summary>
    public static Camera SimulationCamera { get; private set; }

    /// <summary>
    /// Points every screen-space canvas at a camera rendering at the simulated device's size.
    ///
    /// It has to happen here -- after every Awake, before any Start -- because the HUD and the
    /// touch layer measure their canvas once, early, and a canvas left on the batch-mode
    /// screen would be measured at 640x480 and laid out for the wrong device.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RouteCanvases()
    {
        if (!_simulating) return;
        if (SimulationCamera != null)
        {
            RouteCanvasesTo(SimulationCamera);
            return;
        }
        var go = new GameObject("SimulatedScreenCamera");
        var cam = go.AddComponent<Camera>();
        // Far from any arena, so its short frustum holds nothing but the canvases.
        go.transform.position = new Vector3(0f, -20000f, 0f);
        cam.orthographic = true;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.enabled = false;
        cam.targetTexture = new RenderTexture(_device.width, _device.height, 24, RenderTextureFormat.ARGB32);
        SimulationCamera = cam;
        RouteCanvasesTo(cam);
    }

    /// <summary>Also called for canvases that appear later, such as the settings screen's host.</summary>
    public static void RouteCanvasesTo(Camera cam)
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 10f;
            // A scaler computes its factor in its own update; toggling it makes it do so now,
            // so nothing measures the canvas at the old screen's scale in between.
            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null && scaler.enabled)
            {
                scaler.enabled = false;
                scaler.enabled = true;
            }
        }
    }
}
