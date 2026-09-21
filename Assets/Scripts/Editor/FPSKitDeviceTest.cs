#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Whether each class of screen is laid out for the hands that are on it.
    ///
    /// <b>This exists because the question is unanswerable from inside a test run.</b>
    /// Batch mode has exactly one screen, so a check written against
    /// <see cref="PhoneUI.Active"/> measures the build machine and would pass just as
    /// happily with the rule deleted. <see cref="PhoneUI.IsHandset"/> takes its readings as
    /// arguments for that reason, and this drives it across what real hardware reports.
    ///
    /// The row that matters most is the 96 dpi monitor. Every desktop reports it, it is
    /// below the handheld density floor the touch layer clamps to, and routing the question
    /// through that floor substituted a phone's 400 dpi and measured a 1920px monitor as
    /// 122mm across -- a handset. Desktop players lost the arena descriptions and the whole
    /// career panel, and saw the rest at 1.43x, for as long as that stood.
    /// </summary>
    public static class FPSKitDeviceTest
    {
        private readonly struct Device
        {
            public readonly string What;
            public readonly float Dpi;
            public readonly int Width;
            public readonly bool TouchSignal;
            public readonly bool MobilePlatform;
            public readonly int Height;
            public readonly DeviceProfile.Form ExpectForm;
            public readonly DeviceProfile.Reach ExpectReach;

            public bool ExpectHandset => ExpectForm == DeviceProfile.Form.Handset;

            public Device(string what, float dpi, int width, int height, bool touch, bool mobile,
                          DeviceProfile.Form form, DeviceProfile.Reach reach)
            {
                What = what; Dpi = dpi; Width = width; Height = height;
                TouchSignal = touch; MobilePlatform = mobile;
                ExpectForm = form; ExpectReach = reach;
            }
        }

        private static readonly Device[] Fleet =
        {
            // Shorthand, purely so the table below stays readable at a glance.
            // F = form, R = reach.
            //                                dpi    w     h    touch  mobile  form                        reach
            // --- Monitors. The regression lived here: 96 and 109 dpi are what ordinary
            // --- monitors report, and both sit under the touch layer's 120 dpi floor.
            new Device("1080p monitor",       96f, 1920, 1080, false, false, DeviceProfile.Form.Desktop, DeviceProfile.Reach.Pointer),
            new Device("1440p monitor",      109f, 2560, 1440, false, false, DeviceProfile.Form.Desktop, DeviceProfile.Reach.Pointer),
            new Device("4K monitor",         163f, 3840, 2160, false, false, DeviceProfile.Form.Desktop, DeviceProfile.Reach.Pointer),

            // --- Laptops. The retina one is the machine this was developed on, and the
            // --- only pointer device that ever passed the old test, because its density
            // --- happens to fall inside the handheld range and so escaped substitution.
            new Device("13in retina laptop", 166f, 2560, 1600, false, false, DeviceProfile.Form.Laptop,  DeviceProfile.Reach.Pointer),
            new Device("15in 1080p laptop",  141f, 1920, 1080, false, false, DeviceProfile.Form.Laptop,  DeviceProfile.Reach.Pointer),

            // --- A touchscreen laptop is reached with a pointer in practice. Size alone
            // --- cannot say this and neither can the platform; only the touch signal can,
            // --- and there is none, because nothing put on-screen controls up.
            new Device("touchscreen laptop", 157f, 1920, 1080, false, false, DeviceProfile.Form.Laptop,  DeviceProfile.Reach.Pointer),

            // --- Handsets, landscape and portrait. The long edge is what is measured, so
            // --- rotating a device must not change what kind of device it is.
            new Device("flagship, landscape",416f, 2400, 1080, false, true,  DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),
            new Device("flagship, portrait", 416f, 1080, 2400, false, true,  DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),
            new Device("budget phone",       294f, 1600,  720, false, true,  DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),
            new Device("phone, dpi unknown",   0f, 1080, 2400, false, true,  DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),

            // --- Tablets land either side of the handset line and that is deliberate: a
            // --- 10in is 246mm and gets its own layout, a 7in is 151mm and is near enough
            // --- a phone to be treated as one. This is the pair that would move if
            // --- HandsetMaxMm were ever retuned, so both are pinned.
            new Device("10in tablet",        264f, 2560, 1600, false, true,  DeviceProfile.Form.Tablet,  DeviceProfile.Reach.Touch),
            new Device("7in tablet",         323f, 1920, 1200, false, true,  DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),

            // --- A 12.9in iPad is the row that justifies keeping form and reach apart.
            // --- Its long edge is 263mm, which is a laptop's surface and not a tablet's --
            // --- the same glass as a 13in MacBook -- while the only thing touching it is a
            // --- finger. Forcing it into Tablet to make the name feel right would hand it
            // --- a tablet's information density on a laptop-sized screen. Laptop + Touch
            // --- is the true answer and the useful one: laptop density, thumb-sized
            // --- targets. It also calls itself a Macintosh in a browser, so the touch
            // --- signal is the only thing that can establish the reach at all.
            new Device("12.9in iPad, browser",264f, 2732, 2048, true, false, DeviceProfile.Form.Laptop,  DeviceProfile.Reach.Touch),

            // --- And a desktop that admits nothing is still a desktop. Guessing "small"
            // --- from a pixel count here is precisely what broke this.
            new Device("desktop, dpi unknown", 0f, 1920, 1080, false, false, DeviceProfile.Form.Desktop, DeviceProfile.Reach.Pointer),
        };

        public static void VerifyDevices()
        {
            var problems = new List<string>();

            foreach (var s in Fleet)
            {
                string where = $"{s.What} ({s.Dpi:0} dpi, {s.Width}x{s.Height})";

                var form = DeviceProfile.FormFor(s.Dpi, s.Width, s.Height, s.TouchSignal, s.MobilePlatform);
                if (form != s.ExpectForm)
                    problems.Add($"{where}: classed {form}, should be {s.ExpectForm}");

                var reach = DeviceProfile.ReachFor(s.TouchSignal, s.MobilePlatform);
                if (reach != s.ExpectReach)
                    problems.Add($"{where}: reached by {reach}, should be {s.ExpectReach}");

                // The one-line question the existing screens still ask has to keep
                // agreeing with the form, or there are two classifiers again.
                bool handset = PhoneUI.IsHandset(s.Dpi, Mathf.Max(s.Width, s.Height),
                                                 s.TouchSignal, s.MobilePlatform);
                if (handset != s.ExpectHandset)
                    problems.Add($"{where}: PhoneUI.IsHandset says {handset}, form says {s.ExpectForm}");
            }

            if (problems.Count > 0)
                throw new Exception("screens laid out for the wrong hands:\n  - " +
                                    string.Join("\n  - ", problems));

            Debug.Log($"[FPSKitBatch] devices: {Fleet.Length} screen classes, form and reach both correct");
        }
    }
}
#endif
