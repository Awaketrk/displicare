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

        private bool _initialized;
        private float _currentScale = 1f;

        public float CurrentScale { get { return _currentScale; } }

        /// <summary>Scales red, green and blue by <paramref name="scale"/> (1 = no change). Returns false if Windows refused.</summary>
        public bool Apply(float scale)
        {
            scale = Math.Max(0.01f, Math.Min(1f, scale));
            if (Math.Abs(scale - _currentScale) < 0.0005f) return true;

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
            effect.transform[0] = scale;
            effect.transform[6] = scale;
            effect.transform[12] = scale;
            effect.transform[18] = 1f;
            effect.transform[24] = 1f;

            if (MagSetFullscreenColorEffect(ref effect))
            {
                _currentScale = scale;
                return true;
            }

            // Typically another app (e.g. Windows color filters / Magnifier) currently owns the effect
            App.Log("ColorFilterDimmer: MagSetFullscreenColorEffect failed");
            return false;
        }

        public void Clear()
        {
            if (!_initialized || _currentScale >= 1f) return;
            Apply(1f);
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
