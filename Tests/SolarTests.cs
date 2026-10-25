using System;

namespace Displicare.Tests
{
    /// <summary>Sun position and Solar Sync targets. Istanbul (41.01 N, 28.97 E) as the reference location.</summary>
    public static class SolarTests
    {
        private const double Lat = 41.01, Lon = 28.97;

        // Results are in the machine's local time zone; convert everything through UTC so tests pass anywhere
        private static DateTime Utc(int month, int day, int hour, int minute)
        {
            return new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc);
        }

        [Test]
        public static void ElevationAtSummerSolarNoon()
        {
            // Solar noon in Istanbul on the June solstice is ~10:06 UTC; max elevation = 90 - 41.01 + 23.44
            double elevation = SolarEngine.CalculateElevation(Lat, Lon, Utc(6, 21, 10, 6));
            Assert.Near(72.4, elevation, 0.5, "solstice noon elevation");
        }

        [Test]
        public static void ElevationAtWinterSolarNoon()
        {
            double elevation = SolarEngine.CalculateElevation(Lat, Lon, Utc(12, 21, 10, 2));
            Assert.Near(25.6, elevation, 0.5, "winter solstice noon elevation");
        }

        [Test]
        public static void SunIsWellBelowHorizonAtNight()
        {
            double elevation = SolarEngine.CalculateElevation(Lat, Lon, Utc(6, 21, 22, 0)); // 01:00 local
            Assert.True(elevation < -12, "night elevation should be below -12°, got " + elevation);
        }

        [Test]
        public static void SunriseAndSunsetOnSummerSolstice()
        {
            // Published times for Istanbul on 21 June: sunrise ~05:32, sunset ~20:39 (UTC+3)
            DateTime sunrise, noon, sunset;
            SolarEngine.CalculateSunTimes(Lat, Lon, Utc(6, 21, 12, 0).ToLocalTime(), out sunrise, out noon, out sunset);
            Assert.Near((Utc(6, 21, 2, 32) - sunrise.ToUniversalTime()).TotalMinutes, 0, 5, "sunrise (minutes off)");
            Assert.Near((Utc(6, 21, 17, 39) - sunset.ToUniversalTime()).TotalMinutes, 0, 5, "sunset (minutes off)");
            Assert.True(sunrise < noon && noon < sunset, "sunrise < noon < sunset");
        }

        [Test]
        public static void FactorCurveEndpointsAndMidpoint()
        {
            Assert.Near(1.0, SolarEngine.ElevationToFactor(20), 1e-9, "factor at +20°");
            Assert.Near(1.0, SolarEngine.ElevationToFactor(60), 1e-9, "factor above +20°");
            Assert.Near(0.0, SolarEngine.ElevationToFactor(-12), 1e-9, "factor at -12°");
            Assert.Near(0.0, SolarEngine.ElevationToFactor(-40), 1e-9, "factor below -12°");
            Assert.Near(0.5, SolarEngine.ElevationToFactor(4), 1e-9, "factor halfway (+4°)");

            double prev = -1;
            for (double e = -14; e <= 22; e += 0.5)
            {
                double f = SolarEngine.ElevationToFactor(e);
                Assert.True(f >= prev, "factor must not decrease at " + e + "°");
                prev = f;
            }
        }

        [Test]
        public static void TargetsAtNightUseNightLimits()
        {
            var status = SolarEngine.ComputeStatus(Lat, Lon, Utc(6, 21, 22, 0).ToLocalTime(), 100, 15, 6500, 4500);
            Assert.Equal(15, status.TargetBrightness, "night brightness");
            Assert.Equal(4500, status.TargetKelvin, "night warmth");
        }

        [Test]
        public static void TargetsAtNoonUseDayLimits()
        {
            var status = SolarEngine.ComputeStatus(Lat, Lon, Utc(6, 21, 10, 6).ToLocalTime(), 100, 15, 6500, 4500);
            Assert.Equal(100, status.TargetBrightness, "day brightness");
            Assert.Equal(6500, status.TargetKelvin, "day color");
        }
    }
}
