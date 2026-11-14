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
        // A city for each common Windows time zone, by its exact id. Up to 2.5.3 ids were matched by a word in them, so
        // "AUS Eastern Standard Time" (Sydney) got New York and "Central Asia Standard Time" got Chicago: Solar Sync
        // dimmed the screen at midday there.
        private static readonly System.Collections.Generic.Dictionary<string, double[]> ZoneCoordinates =
            new System.Collections.Generic.Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Turkey Standard Time", new[] { 41.01, 28.97 } },                 // Istanbul
            { "Hawaiian Standard Time", new[] { 21.31, -157.86 } },             // Honolulu
            { "Alaskan Standard Time", new[] { 61.22, -149.90 } },              // Anchorage
            { "Pacific Standard Time", new[] { 37.77, -122.41 } },              // San Francisco
            { "Pacific Standard Time (Mexico)", new[] { 32.51, -117.04 } },     // Tijuana
            { "US Mountain Standard Time", new[] { 33.45, -112.07 } },          // Phoenix
            { "Mountain Standard Time", new[] { 39.73, -104.99 } },             // Denver
            { "Mountain Standard Time (Mexico)", new[] { 24.14, -110.31 } },    // La Paz
            { "Central Standard Time", new[] { 41.87, -87.62 } },               // Chicago
            { "Central Standard Time (Mexico)", new[] { 19.43, -99.13 } },      // Mexico City
            { "Canada Central Standard Time", new[] { 50.45, -104.62 } },       // Regina
            { "Central America Standard Time", new[] { 14.63, -90.51 } },       // Guatemala City
            { "Eastern Standard Time", new[] { 40.71, -74.00 } },               // New York
            { "US Eastern Standard Time", new[] { 39.77, -86.16 } },            // Indianapolis
            { "Eastern Standard Time (Mexico)", new[] { 21.16, -86.85 } },      // Cancun
            { "SA Pacific Standard Time", new[] { 4.71, -74.07 } },             // Bogota
            { "Atlantic Standard Time", new[] { 44.65, -63.57 } },              // Halifax
            { "Pacific SA Standard Time", new[] { -33.45, -70.67 } },           // Santiago
            { "E. South America Standard Time", new[] { -23.55, -46.63 } },     // Sao Paulo
            { "Argentina Standard Time", new[] { -34.60, -58.38 } },            // Buenos Aires
            { "GMT Standard Time", new[] { 51.50, -0.12 } },                    // London
            { "Greenwich Standard Time", new[] { 64.15, -21.94 } },             // Reykjavik
            { "W. Europe Standard Time", new[] { 50.11, 8.68 } },               // Frankfurt
            { "Romance Standard Time", new[] { 48.86, 2.35 } },                 // Paris
            { "Central Europe Standard Time", new[] { 50.08, 14.44 } },         // Prague
            { "Central European Standard Time", new[] { 52.23, 21.01 } },       // Warsaw
            { "GTB Standard Time", new[] { 37.98, 23.73 } },                    // Athens
            { "FLE Standard Time", new[] { 60.17, 24.94 } },                    // Helsinki
            { "E. Europe Standard Time", new[] { 47.01, 28.86 } },              // Chisinau
            { "Israel Standard Time", new[] { 31.77, 35.21 } },                 // Jerusalem
            { "Egypt Standard Time", new[] { 30.04, 31.24 } },                  // Cairo
            { "South Africa Standard Time", new[] { -26.20, 28.05 } },          // Johannesburg
            { "Russian Standard Time", new[] { 55.75, 37.62 } },                // Moscow
            { "Arab Standard Time", new[] { 24.71, 46.68 } },                   // Riyadh
            { "Arabian Standard Time", new[] { 25.20, 55.27 } },                // Dubai
            { "India Standard Time", new[] { 28.61, 77.21 } },                  // New Delhi
            { "Central Asia Standard Time", new[] { 42.87, 74.59 } },           // Bishkek
            { "SE Asia Standard Time", new[] { 13.76, 100.50 } },               // Bangkok
            { "China Standard Time", new[] { 39.90, 116.40 } },                 // Beijing
            { "Singapore Standard Time", new[] { 1.35, 103.82 } },              // Singapore
            { "W. Australia Standard Time", new[] { -31.95, 115.86 } },         // Perth
            { "Korea Standard Time", new[] { 37.57, 126.98 } },                 // Seoul
            { "Tokyo Standard Time", new[] { 35.67, 139.65 } },                 // Tokyo
            { "AUS Central Standard Time", new[] { -12.46, 130.84 } },          // Darwin
            { "Cen. Australia Standard Time", new[] { -34.93, 138.60 } },       // Adelaide
            { "E. Australia Standard Time", new[] { -27.47, 153.03 } },         // Brisbane
            { "AUS Eastern Standard Time", new[] { -33.87, 151.21 } },          // Sydney
            { "Tasmania Standard Time", new[] { -42.88, 147.33 } },             // Hobart
            { "West Pacific Standard Time", new[] { 13.44, 144.79 } },          // Guam
            { "Central Pacific Standard Time", new[] { -9.43, 159.95 } },       // Honiara
            { "New Zealand Standard Time", new[] { -36.85, 174.76 } },          // Auckland
        };

        public static void GetDefaultCoordinates(out double lat, out double lon)
        {
            CoordinatesFor(TimeZoneInfo.Local.Id, TimeZoneInfo.Local.BaseUtcOffset, out lat, out lon);
        }

        /// <summary>
        /// Where to take the sun from for a Windows time zone: its city if the zone is in the list, else the middle of the
        /// zone (15 degrees per hour from UTC) at 40° north.
        /// </summary>
        internal static void CoordinatesFor(string timeZoneId, TimeSpan utcOffset, out double lat, out double lon)
        {
            double[] place;
            if (timeZoneId != null && ZoneCoordinates.TryGetValue(timeZoneId, out place))
            {
                lat = place[0];
                lon = place[1];
                return;
            }
            lon = Math.Max(-180.0, Math.Min(180.0, utcOffset.TotalHours * 15.0));
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
            CalculateSunTimes(lat, lon, dateLocal, TimeZoneInfo.Local, out sunriseLocal, out solarNoonLocal, out sunsetLocal);
        }

        /// <summary>
        /// Sunrise, solar noon and sunset of the local day <paramref name="dateLocal"/> falls on, in
        /// <paramref name="zone"/>'s time. Up to 2.5.3 the day was taken in UTC, so for the hours when UTC is on another
        /// date (evenings in America, mornings in Asia) these were the day before's or after's and the day bar read 0% or
        /// 100% with the sun up.
        /// </summary>
        internal static void CalculateSunTimes(double lat, double lon, DateTime dateLocal, TimeZoneInfo zone,
            out DateTime sunriseLocal, out DateTime solarNoonLocal, out DateTime sunsetLocal)
        {
            // The local calendar day, as a UTC midnight to count minutes from
            DateTime utcDate = new DateTime(dateLocal.Year, dateLocal.Month, dateLocal.Day, 0, 0, 0, DateTimeKind.Utc);
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

                TimeZoneInfo tz = zone ?? TimeZoneInfo.Local;
                // Next to the date line the zone's clock and its longitude disagree by a day (UTC+14 at 157° west):
                // keep solar noon on the day asked for
                int offBy = (dateLocal.Date - TimeZoneInfo.ConvertTimeFromUtc(solarNoonUtc, tz).Date).Days;
                if (offBy != 0)
                {
                    sunriseUtc = sunriseUtc.AddDays(offBy);
                    solarNoonUtc = solarNoonUtc.AddDays(offBy);
                    sunsetUtc = sunsetUtc.AddDays(offBy);
                }
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
