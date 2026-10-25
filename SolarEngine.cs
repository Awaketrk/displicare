using System;

namespace Displicare
{
    public class SolarStatus
    {
        public double ElevationDeg { get; set; }
        public double CircadianFactor { get; set; } // 0.0 (night) to 1.0 (peak day)
        public int TargetBrightness { get; set; }
        public int TargetKelvin { get; set; }
        public DateTime SunriseLocal { get; set; }
        public DateTime SolarNoonLocal { get; set; }
        public DateTime SunsetLocal { get; set; }
        public string PhaseName { get; set; }
        public string Description { get; set; }
        public string SunTimesText { get; set; }
        public double DayProgressPercent { get; set; } // 0 to 100%
    }

    public static class SolarEngine
    {
        public static void GetDefaultCoordinates(out double lat, out double lon)
        {
            string tzId = TimeZoneInfo.Local.Id;

            if (tzId.IndexOf("Turkey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tzId.IndexOf("Istanbul", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 41.01;
                lon = 28.97;
                return;
            }
            if (tzId.IndexOf("Eastern", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 40.71;
                lon = -74.00;
                return;
            }
            if (tzId.IndexOf("Pacific", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 37.77;
                lon = -122.41;
                return;
            }
            if (tzId.IndexOf("Central", StringComparison.OrdinalIgnoreCase) >= 0 &&
                tzId.IndexOf("Europe", StringComparison.OrdinalIgnoreCase) < 0)
            {
                lat = 41.87;
                lon = -87.62;
                return;
            }
            if (tzId.IndexOf("Mountain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 39.73;
                lon = -104.99;
                return;
            }
            if (tzId.IndexOf("GMT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tzId.IndexOf("Greenwich", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tzId.IndexOf("London", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 51.50;
                lon = -0.12;
                return;
            }
            if (tzId.IndexOf("W. Europe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tzId.IndexOf("Berlin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tzId.IndexOf("Paris", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 50.11;
                lon = 8.68;
                return;
            }
            if (tzId.IndexOf("Tokyo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tzId.IndexOf("Japan", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lat = 35.67;
                lon = 139.65;
                return;
            }

            // General fallback from UTC offset: 15 degrees per timezone hour
            lon = TimeZoneInfo.Local.BaseUtcOffset.TotalHours * 15.0;
            lat = 40.0;
        }

        public static double CalculateElevation(double lat, double lon, DateTime utcDateTime)
        {
            int d = utcDateTime.DayOfYear;
            double hour = utcDateTime.Hour + (utcDateTime.Minute / 60.0) + (utcDateTime.Second / 3600.0);

            double gamma = 2.0 * Math.PI / 365.0 * (d - 1 + (hour - 12.0) / 24.0);

            // NOAA Equation of time in minutes
            double eqtime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                            - 0.014615 * Math.Cos(2.0 * gamma) - 0.040849 * Math.Sin(2.0 * gamma));

            // NOAA Solar declination in radians
            double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                          - 0.006758 * Math.Cos(2.0 * gamma) + 0.000907 * Math.Sin(2.0 * gamma)
                          - 0.002697 * Math.Cos(3.0 * gamma) + 0.00148 * Math.Sin(3.0 * gamma);

            double timeOffset = eqtime + 4.0 * lon;
            double tst = (utcDateTime.Hour * 60.0 + utcDateTime.Minute + utcDateTime.Second / 60.0 + timeOffset) % 1440.0;
            if (tst < 0) tst += 1440.0;

            double ha = (tst / 4.0) - 180.0;
            double haRad = ha * Math.PI / 180.0;
            double latRad = lat * Math.PI / 180.0;

            double sinElevation = Math.Sin(latRad) * Math.Sin(decl) + Math.Cos(latRad) * Math.Cos(decl) * Math.Cos(haRad);
            double elevationRad = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinElevation)));

            return elevationRad * 180.0 / Math.PI;
        }

        public static void CalculateSunTimes(double lat, double lon, DateTime dateLocal,
            out DateTime sunriseLocal, out DateTime solarNoonLocal, out DateTime sunsetLocal)
        {
            DateTime utcDate = dateLocal.ToUniversalTime().Date;
            int d = utcDate.DayOfYear;
            double gamma = 2.0 * Math.PI / 365.0 * (d - 1);

            double eqtime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                            - 0.014615 * Math.Cos(2.0 * gamma) - 0.040849 * Math.Sin(2.0 * gamma));

            double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                          - 0.006758 * Math.Cos(2.0 * gamma) + 0.000907 * Math.Sin(2.0 * gamma)
                          - 0.002697 * Math.Cos(3.0 * gamma) + 0.00148 * Math.Sin(3.0 * gamma);

            double latRad = lat * Math.PI / 180.0;
            double h0 = -0.833 * Math.PI / 180.0; // Refraction + solar disk center

            double denom = Math.Cos(latRad) * Math.Cos(decl);
            if (Math.Abs(denom) < 1e-7) denom = (denom < 0) ? -1e-7 : 1e-7;

            double cosHa = (Math.Sin(h0) - Math.Sin(latRad) * Math.Sin(decl)) / denom;
            if (double.IsNaN(cosHa)) cosHa = 0.0;
            cosHa = Math.Max(-1.0, Math.Min(1.0, cosHa));

            double haDeg = Math.Acos(cosHa) * 180.0 / Math.PI;
            if (double.IsNaN(haDeg)) haDeg = 90.0;

            double solarNoonUtcMinutes = 720.0 - 4.0 * lon - eqtime;
            if (double.IsNaN(solarNoonUtcMinutes)) solarNoonUtcMinutes = 720.0;

            double sunriseUtcMinutes = solarNoonUtcMinutes - haDeg * 4.0;
            double sunsetUtcMinutes = solarNoonUtcMinutes + haDeg * 4.0;

            try
            {
                DateTime sunriseUtc = utcDate.AddMinutes(sunriseUtcMinutes);
                DateTime solarNoonUtc = utcDate.AddMinutes(solarNoonUtcMinutes);
                DateTime sunsetUtc = utcDate.AddMinutes(sunsetUtcMinutes);

                TimeZoneInfo tz = TimeZoneInfo.Local;
                sunriseLocal = TimeZoneInfo.ConvertTimeFromUtc(sunriseUtc, tz);
                solarNoonLocal = TimeZoneInfo.ConvertTimeFromUtc(solarNoonUtc, tz);
                sunsetLocal = TimeZoneInfo.ConvertTimeFromUtc(sunsetUtc, tz);
            }
            catch
            {
                // Fallback default times if extreme polar coordinates or invalid offsets
                sunriseLocal = dateLocal.Date.AddHours(6);
                solarNoonLocal = dateLocal.Date.AddHours(12);
                sunsetLocal = dateLocal.Date.AddHours(18);
            }
        }

        public static double ElevationToFactor(double elevationDeg)
        {
            // >= 20 degrees: Peak daylight (factor 1.0)
            // <= -12 degrees: Deep night (factor 0.0)
            // Between -12 and +20: smooth cosine S-curve
            if (elevationDeg >= 20.0) return 1.0;
            if (elevationDeg <= -12.0) return 0.0;

            double t = (elevationDeg - (-12.0)) / (20.0 - (-12.0));
            return 0.5 * (1.0 - Math.Cos(t * Math.PI));
        }

        public static SolarStatus ComputeStatus(double lat, double lon, DateTime nowLocal,
            int dayBrightness, int nightBrightness, int dayKelvin, int nightKelvin)
        {
            DateTime utc = nowLocal.ToUniversalTime();
            double elevation = CalculateElevation(lat, lon, utc);
            double factor = ElevationToFactor(elevation);

            DateTime sunrise, noon, sunset;
            CalculateSunTimes(lat, lon, nowLocal, out sunrise, out noon, out sunset);

            int targetBrightness = (int)Math.Round(nightBrightness + factor * (dayBrightness - nightBrightness));
            int targetKelvin = (int)Math.Round(nightKelvin + factor * (dayKelvin - nightKelvin));

            string phaseName;
            string description;

            if (elevation >= 20.0)
            {
                phaseName = "Peak Daylight";
                description = "Maximum daytime brightness & balanced daylight curve";
            }
            else if (elevation >= 5.0)
            {
                phaseName = "Afternoon Daylight";
                description = "Pleasant daytime illumination following sun altitude";
            }
            else if (elevation >= 0.0)
            {
                phaseName = "Golden Hour";
                description = "Sun nearing horizon • Warming color profile & reducing eye strain";
            }
            else if (elevation >= -6.0)
            {
                phaseName = "Civil Twilight";
                description = "Post-sunset twilight • Soft amber warmth protecting your eyes";
            }
            else if (elevation >= -12.0)
            {
                phaseName = "Dusk / Night Transition";
                description = "Blue light suppression active • Preparing for rest";
            }
            else
            {
                phaseName = "Night Comfort";
                description = "Deep blue light suppression & comfortable dimmed level";
            }

            // Calculate progress of day (sunrise to sunset = 0% to 100%)
            double progress = 50.0;
            if (nowLocal < sunrise)
            {
                progress = 0.0;
            }
            else if (nowLocal > sunset)
            {
                progress = 100.0;
            }
            else
            {
                double totalDayMinutes = (sunset - sunrise).TotalMinutes;
                if (totalDayMinutes > 0)
                {
                    progress = (nowLocal - sunrise).TotalMinutes / totalDayMinutes * 100.0;
                }
            }

            var status = new SolarStatus
            {
                ElevationDeg = elevation,
                CircadianFactor = factor,
                TargetBrightness = targetBrightness,
                TargetKelvin = targetKelvin,
                SunriseLocal = sunrise,
                SolarNoonLocal = noon,
                SunsetLocal = sunset,
                PhaseName = phaseName,
                Description = description,
                SunTimesText = string.Format("Sunrise {0} • Sunset {1}", sunrise.ToString("HH:mm"), sunset.ToString("HH:mm")),
                DayProgressPercent = Math.Max(0.0, Math.Min(100.0, progress))
            };

            return status;
        }
    }
}
