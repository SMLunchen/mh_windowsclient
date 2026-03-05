namespace MeshhessenClient.Services;

/// <summary>
/// Calculates sunrise and sunset times using the NOAA solar calculator algorithm.
/// Falls back to 06:00/22:00 when no position is available.
/// </summary>
public static class SunriseSunsetService
{
    private static readonly TimeSpan DefaultSunrise = new(6, 0, 0);
    private static readonly TimeSpan DefaultSunset  = new(22, 0, 0);

    /// <summary>
    /// Returns (sunrise, sunset) as local TimeSpan for the given date and position.
    /// Returns (06:00, 22:00) if lat/lon are zero or calculation fails.
    /// </summary>
    public static (TimeSpan Sunrise, TimeSpan Sunset) GetSunriseSunset(double lat, double lon, DateTime date)
    {
        if (lat == 0.0 && lon == 0.0)
            return (DefaultSunrise, DefaultSunset);

        try
        {
            // Julian Day Number
            int y = date.Year, m = date.Month, d = date.Day;
            double jd = 367 * y
                        - (int)(7 * (y + (int)((m + 9) / 12.0)) / 4.0)
                        + (int)(275 * m / 9.0)
                        + d + 1721013.5;

            // Time zone offset in hours (local time)
            double tzOffset = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;

            // Calculation for noon
            double jnoon = CalcJulianCycle(jd, lon);
            double solar = CalcSolarNoon(jnoon, lon);
            double eqTime = CalcEquationOfTime(solar);
            double decl   = CalcSunDeclination(solar);

            double hourAngle = CalcHourAngleSunrise(lat, decl);
            if (double.IsNaN(hourAngle))
                return (DefaultSunrise, DefaultSunset); // polar day/night

            double sunriseUtc = 720 - 4 * (lon + hourAngle) - eqTime; // minutes
            double sunsetUtc  = 720 - 4 * (lon - hourAngle) - eqTime;

            var sunrise = TimeSpan.FromMinutes(sunriseUtc + tzOffset * 60);
            var sunset  = TimeSpan.FromMinutes(sunsetUtc  + tzOffset * 60);

            // Clamp to valid range
            sunrise = Clamp(sunrise);
            sunset  = Clamp(sunset);

            return (sunrise, sunset);
        }
        catch
        {
            return (DefaultSunrise, DefaultSunset);
        }
    }

    /// <summary>Returns true if the given local time is during daytime.</summary>
    public static bool IsDay(TimeSpan localTime, double lat, double lon, DateTime date)
    {
        var (sunrise, sunset) = GetSunriseSunset(lat, lon, date);
        return localTime >= sunrise && localTime < sunset;
    }

    /// <summary>Returns true if the given UTC DateTime is during daytime at the given position.</summary>
    public static bool IsDay(DateTime utcTime, double lat, double lon)
    {
        var local = utcTime.ToLocalTime();
        return IsDay(local.TimeOfDay, lat, lon, local.Date);
    }

    // ── NOAA Algorithm internals ──────────────────────────────────────────

    private static double CalcJulianCycle(double jd, double lon)
        => Math.Round(jd - 2451545.0009 - lon / 360.0);

    private static double CalcSolarNoon(double jcycle, double lon)
        => 2451545.0009 + lon / 360.0 + jcycle;

    private static double CalcEquationOfTime(double jnoon)
    {
        double t = (jnoon - 2451545.0) / 36525.0;
        double l0 = (280.46646 + t * (36000.76983 + t * 0.0003032)) % 360.0;
        double m  = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double e  = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double c  = Math.Sin(Rad(m)) * (1.9146 - t * (0.004817 + 0.000014 * t))
                  + Math.Sin(Rad(2 * m)) * (0.019993 - 0.000101 * t)
                  + Math.Sin(Rad(3 * m)) * 0.00029;
        double sunLon = l0 + c;
        double omega  = 125.04 - 1934.136 * t;
        double lambda = sunLon - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
        double eps    = CalcObliquity(t) + 0.00256 * Math.Cos(Rad(omega));
        double y      = Math.Tan(Rad(eps / 2));
        y *= y;

        double eqTime = y * Math.Sin(Rad(2 * l0))
                       - 2 * e * Math.Sin(Rad(m))
                       + 4 * e * y * Math.Sin(Rad(m)) * Math.Cos(Rad(2 * l0))
                       - 0.5 * y * y * Math.Sin(Rad(4 * l0))
                       - 1.25 * e * e * Math.Sin(Rad(2 * m));
        return 4 * Deg(eqTime);
    }

    private static double CalcSunDeclination(double jnoon)
    {
        double t = (jnoon - 2451545.0) / 36525.0;
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double l0 = (280.46646 + t * (36000.76983 + t * 0.0003032)) % 360.0;
        double c  = Math.Sin(Rad(m)) * (1.9146 - t * (0.004817 + 0.000014 * t))
                  + Math.Sin(Rad(2 * m)) * (0.019993 - 0.000101 * t)
                  + Math.Sin(Rad(3 * m)) * 0.00029;
        double sunLon = l0 + c;
        double omega  = 125.04 - 1934.136 * t;
        double lambda = sunLon - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
        double eps    = CalcObliquity(t) + 0.00256 * Math.Cos(Rad(omega));
        return Deg(Math.Asin(Math.Sin(Rad(eps)) * Math.Sin(Rad(lambda))));
    }

    private static double CalcObliquity(double t)
    {
        double seconds = 21.448 - t * (46.8150 + t * (0.00059 - t * 0.001813));
        return 23.0 + (26.0 + seconds / 60.0) / 60.0;
    }

    private static double CalcHourAngleSunrise(double lat, double decl)
    {
        // solar zenith = 90.833° (accounts for refraction + solar disc)
        double cosHA = (Math.Cos(Rad(90.833)) - Math.Sin(Rad(lat)) * Math.Sin(Rad(decl)))
                       / (Math.Cos(Rad(lat)) * Math.Cos(Rad(decl)));
        if (cosHA < -1.0 || cosHA > 1.0) return double.NaN;
        return Deg(Math.Acos(cosHA));
    }

    private static double Rad(double deg) => deg * Math.PI / 180.0;
    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    private static TimeSpan Clamp(TimeSpan t)
    {
        if (t < TimeSpan.Zero)         return t + TimeSpan.FromDays(1);
        if (t >= TimeSpan.FromDays(1)) return t - TimeSpan.FromDays(1);
        return t;
    }
}
