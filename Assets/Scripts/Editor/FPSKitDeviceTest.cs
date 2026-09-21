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
            public readonly bool ExpectHandset;

            public Device(string what, float dpi, int width, bool touch, bool mobile, bool expect)
            {
                What = what; Dpi = dpi; Width = width;
                TouchSignal = touch; MobilePlatform = mobile; ExpectHandset = expect;
            }
        }

        private static readonly Device[] Fleet =
        {
            // The regression. 96 and 109 dpi are what ordinary monitors report, and both
            // sit under the touch layer's 120 dpi floor.
            new Device("1080p monitor",        96f, 1920, false, false, false),
            new Device("1440p monitor",       109f, 2560, false, false, false),
            new Device("4K monitor",          163f, 3840, false, false, false),

            // Laptops. The high-dpi one is the machine this was developed on, and the only
            // desktop that ever passed, because its density is inside the handheld range.
            new Device("13in retina laptop",  166f, 2560, false, false, false),
            new Device("15in 1080p laptop",   141f, 1920, false, false, false),

            // Handsets, which must stay handsets.
            new Device("flagship phone",      416f, 2400, false, true,  true),
            new Device("budget phone",        294f, 1600, false, true,  true),
            new Device("phone, dpi unknown",    0f, 1080, false, true,  true),

            // Tablets are judged on width like everything else, and they land either side
            // of the line: a 10in is 246mm and gets the desktop layout, a 7in is 151mm and
            // does not. That is deliberate rather than a miss -- 151mm held at arm's length
            // is nearer a phone than a monitor -- and it is the one row here that would
            // change if PhoneWidthMm were ever retuned.
            new Device("10in tablet",         264f, 2560, false, true,  false),
            new Device("7in tablet",          323f, 1920, false, true,  true),

            // A touch signal outranks every measurement, including a desktop-sized one --
            // an iPad in a browser calls itself a Macintosh, so the finger is the evidence.
            new Device("iPad in browser",     264f, 2732, true,  false, true),

            // And a desktop that admits nothing is still a desktop. Guessing "small" from
            // pixels here is precisely the bug.
            new Device("desktop, dpi unknown",  0f, 1920, false, false, false),
        };

        public static void VerifyDevices()
        {
            var problems = new List<string>();

            foreach (var s in Fleet)
            {
                bool got = PhoneUI.IsHandset(s.Dpi, s.Width, s.TouchSignal, s.MobilePlatform);
                if (got == s.ExpectHandset) continue;

                problems.Add($"{s.What} ({s.Dpi:0} dpi, {s.Width}px): " +
                             $"laid out for {(got ? "a thumb" : "a monitor")}, " +
                             $"should be {(s.ExpectHandset ? "a thumb" : "a monitor")}");
            }

            if (problems.Count > 0)
                throw new Exception("screens laid out for the wrong hands:\n  - " +
                                    string.Join("\n  - ", problems));

            Debug.Log($"[FPSKitBatch] devices: {Fleet.Length} screen classes each laid out correctly");
        }
    }
}
#endif
