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

            // --- A phone in a browser, measured the way WebDevice.FramebufferDpi measures
            // --- it: CSS pixels at about 150 to the inch, times whatever ratio the page
            // --- renders at. Both rows are the same handset -- 914 CSS pixels across, a
            // --- true 152mm -- drawn at the two sharpnesses the page will choose between,
            // --- and the classification has to be the same for both or raising the
            // --- resolution would silently change which menus the player gets.
            // ---
            // --- This is the row that was failing in the wild. Screen.dpi in a browser
            // --- reports 96 times the render ratio, which at ratio 1 measured this phone
            // --- as 242mm: a tablet, so it got two columns of achievements and the
            // --- desktop's card grid on a 155mm screen.
            new Device("phone in a browser",  150f,  914,  411, true, false, DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),
            new Device("phone, sharper page", 262f, 1600,  719, true, false, DeviceProfile.Form.Handset, DeviceProfile.Reach.Touch),
            new Device("tablet in a browser", 192f, 1366, 1024, true, false, DeviceProfile.Form.Tablet,  DeviceProfile.Reach.Touch),
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

            CheckPhysicalSizing(problems);

            if (problems.Count > 0)
                throw new Exception("screens laid out for the wrong hands:\n  - " +
                                    string.Join("\n  - ", problems));

            Debug.Log($"[FPSKitBatch] devices: {Fleet.Length} screen classes, form and reach " +
                      "both correct, and millimetres survive a scaled framebuffer");
        }

        /// <summary>
        /// That a millimetre is a millimetre, and a swipe is a swipe, on a screen the
        /// browser is scaling.
        ///
        /// <b>Both halves of this shipped broken, three weeks apart, from one cause.</b>
        /// The touch layer is written in millimetres and the look in the pixels a swipe
        /// would cover on a reference screen, and both conversions need the density of
        /// the framebuffer Unity is drawing into. A browser does not report that: it
        /// gives the panel's density while handing Unity a backbuffer at whatever ratio
        /// the page chose. Sizing it wrong made the fire button cover the middle of the
        /// screen; correcting only the sizing left the look reading the substituted 400
        /// against a real 150, so the view turned a third as far as the thumb asked and
        /// the player had to swipe three times to look behind them.
        ///
        /// VerifyTouch cannot see either one. It measures the layer in canvas units, and
        /// in canvas units everything was correct both times -- the whole error lives in
        /// the step from canvas units to glass, which needs a real device or this.
        ///
        /// The invariant is the same for both: <b>the ratio the page renders at must
        /// change nothing the player can feel.</b> More pixels, same physical sizes, same
        /// degrees per swipe.
        /// </summary>
        private static void CheckPhysicalSizing(List<string> problems)
        {
            // One phone -- 914 CSS pixels across, 150 CSS dpi -- drawn at each of the
            // ratios the page might choose between.
            const float CssDpi = 150f;
            const int CssWidth = 914;

            const float ButtonMm = 15f;    // the fire button
            const float SwipeMm = 40f;     // a thumb drag across the look area

            float buttonShare = -1f;
            float swipeReference = -1f;

            foreach (float ratio in new[] { 1f, 1.75f, 2f, 3f })
            {
                float dpi = CssDpi * ratio;
                float pixelsPerMm = TouchMetrics.PixelsPerMillimetreFor(dpi);

                // How much of the screen the button covers. The framebuffer grows with
                // the ratio and so does the button, so this must not move.
                float share = ButtonMm * pixelsPerMm / (CssWidth * ratio);

                if (buttonShare < 0f) buttonShare = share;
                else if (Mathf.Abs(share - buttonShare) > 0.002f)
                    problems.Add($"at render ratio {ratio}, the fire button covers " +
                                 $"{share:P1} of the screen and at ratio 1 it covers " +
                                 $"{buttonShare:P1} -- the page's sharpness is resizing the controls");

                // And how far the view turns for a physical swipe, which is the same
                // question asked of the other conversion.
                float raw = SwipeMm * pixelsPerMm;
                float reference = TouchMetrics.ToReferencePixelsFor(new Vector2(raw, 0f), dpi).x;

                if (swipeReference < 0f) swipeReference = reference;
                else if (Mathf.Abs(reference - swipeReference) > 1f)
                    problems.Add($"at render ratio {ratio}, a {SwipeMm}mm swipe is worth " +
                                 $"{reference:0} reference pixels and at ratio 1 it is worth " +
                                 $"{swipeReference:0} -- the look speed depends on the page");
            }

            // And the absolute answer, not merely a consistent one. A reference pixel is
            // defined at ReferenceDpi, so a 40mm swipe is worth exactly this many of them
            // on every device there has ever been -- which is the statement the shipped
            // code failed, by a factor of two and a half, while being perfectly
            // self-consistent about it.
            float expected = TouchMetrics.MillimetresToReferencePixels(SwipeMm);

            if (Mathf.Abs(swipeReference - expected) > 1f)
                problems.Add($"a {SwipeMm}mm swipe turns the view by {swipeReference:0} " +
                             $"reference pixels and should turn it by {expected:0} -- " +
                             $"the look is {expected / Mathf.Max(1f, swipeReference):0.0}x " +
                             "too slow on a phone");

            // A density the platform cannot possibly mean must fall back rather than
            // divide by nearly nothing.
            float sane = TouchMetrics.PixelsPerMillimetreFor(CssDpi);

            if (TouchMetrics.PixelsPerMillimetreFor(0f) > sane * 3f ||
                TouchMetrics.PixelsPerMillimetreFor(-3f) > sane * 3f ||
                TouchMetrics.PixelsPerMillimetreFor(9000f) > sane * 3f)
                problems.Add("an impossible density was taken at face value, which is a " +
                             "division that makes the controls either dead or uncontrollable");
        }
    }
}
#endif
