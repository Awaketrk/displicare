using System;
using System.Runtime.InteropServices;

namespace Displicare
{
    /// <summary>
    /// Full-screen dimming through the Windows Magnification API color effect (the mechanism behind Windows'
    /// own color filters). The compositor applies it to the whole desktop, so unlike the gamma ramp it behaves
    /// correctly in HDR, dims HDR content too, and can go far below the 80-nit SDR content brightness floor.
    /// Windows removes the effect automatically when this process exits, even on a crash.
    /// All calls must come from the same (UI) thread: MagInitialize is per-thread.
    /// </summary>
    public sealed class ColorFilterDimmer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;
        }

        [DllImport("Magnification.dll")] private static extern bool MagInitialize();
        [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);
        [DllImport("Magnification.dll")] private static extern bool MagGetFullscreenColorEffect(ref MAGCOLOREFFECT effect);

        private bool _initialized;
        private float _r = 1f, _g = 1f, _b = 1f;

        private bool IsIdentity { get { return _r >= 1f && _g >= 1f && _b >= 1f; } }

        /// <summary>
        /// Scales red, green and blue separately (1 = no change), e.g. for warmth plus dimming.
        /// Returns false if Windows refused.
        /// </summary>
        public bool Apply(float r, float g, float b)
        {
            r = Math.Max(0.01f, Math.Min(1f, r));
            g = Math.Max(0.01f, Math.Min(1f, g));
            b = Math.Max(0.01f, Math.Min(1f, b));
            if (Math.Abs(r - _r) < 0.0005f && Math.Abs(g - _g) < 0.0005f && Math.Abs(b - _b) < 0.0005f) return true;

            if (!_initialized)
            {
                _initialized = MagInitialize();
                if (!_initialized)
                {
                    App.Log("ColorFilterDimmer: MagInitialize failed");
                    return false;
                }
            }

            var effect = new MAGCOLOREFFECT { transform = new float[25] };
            // 5x5 row-major matrix: scale the RGB diagonal, keep alpha and the homogeneous term at 1
            effect.transform[0] = r;
            effect.transform[6] = g;
            effect.transform[12] = b;
            effect.transform[18] = 1f;
            effect.transform[24] = 1f;

            if (MagSetFullscreenColorEffect(ref effect))
            {
                _r = r; _g = g; _b = b;
                return true;
            }

            // Typically another app (e.g. Windows color filters / Magnifier) currently owns the effect
            App.Log("ColorFilterDimmer: MagSetFullscreenColorEffect failed");
            return false;
        }

        /// <summary>
        /// Reads the effect Windows is actually applying (the RGB diagonal), for the hardware test.
        /// Returns false if it can't be read.
        /// </summary>
        public bool TryRead(out float r, out float g, out float b)
        {
            r = g = b = 1f;
            if (!_initialized)
            {
                _initialized = MagInitialize();
                if (!_initialized) return false;
            }

            var effect = new MAGCOLOREFFECT { transform = new float[25] };
            if (!MagGetFullscreenColorEffect(ref effect)) return false;
            r = effect.transform[0];
            g = effect.transform[6];
            b = effect.transform[12];
            return true;
        }

        public void Clear()
        {
            if (!_initialized || IsIdentity) return;
            Apply(1f, 1f, 1f);
        }

        public void Dispose()
        {
            Clear();
            if (_initialized)
            {
                MagUninitialize();
                _initialized = false;
            }
        }
    }
}
