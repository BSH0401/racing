using System;
using System.Globalization;

namespace Racing
{
    // Command-line switches for automated test runs (e.g. -autostart -autopilot -timescale 4 -quitafter 60).
    public static class DevFlags
    {
        static string[] args;
        static string[] Args => args ??= Environment.GetCommandLineArgs();

        public static bool Has(string flag) => Array.IndexOf(Args, flag) >= 0;

        public static string Get(string flag)
        {
            int i = Array.IndexOf(Args, flag);
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
        }

        public static float GetFloat(string flag, float fallback)
        {
            return float.TryParse(Get(flag), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
