namespace Krofken.Ballistics
{
    /// <summary>
    /// The library is SI-internal WITHOUT EXCEPTION.
    ///
    ///   length      metres      (m)
    ///   mass        kilograms   (kg)
    ///   time        seconds     (s)
    ///   pressure    pascals     (Pa)
    ///   energy      joules      (J)
    ///   temperature kelvin      (K)
    ///   angle       radians     (rad)
    ///
    /// Conversion happens ONLY at the presentation layer. Never store an imperial
    /// value in a simulation struct -- mixed-unit state is the single most common
    /// source of silent error in ballistics code.
    /// </summary>
    public static class Units
    {
        // ---- Length -------------------------------------------------------
        public const double MetresPerInch = 0.0254;
        public const double MetresPerFoot = 0.3048;
        public const double MetresPerYard = 0.9144;
        public const double MetresPerMillimetre = 1e-3;

        public static double InchesToMetres(double inches) => inches * MetresPerInch;
        public static double MetresToInches(double metres) => metres / MetresPerInch;
        public static double MillimetresToMetres(double mm) => mm * MetresPerMillimetre;
        public static double MetresToMillimetres(double m) => m / MetresPerMillimetre;
        public static double YardsToMetres(double yards) => yards * MetresPerYard;
        public static double MetresToYards(double metres) => metres / MetresPerYard;

        // ---- Mass ---------------------------------------------------------
        // The grain is the standard small-arms mass unit: exactly 1/7000 pound.
        public const double KilogramsPerGrain = 0.00006479891;
        public const double KilogramsPerPound = 0.45359237;

        public static double GrainsToKilograms(double grains) => grains * KilogramsPerGrain;
        public static double KilogramsToGrains(double kg) => kg / KilogramsPerGrain;
        public static double GramsToKilograms(double grams) => grams * 1e-3;
        public static double KilogramsToGrams(double kg) => kg * 1e3;

        // ---- Velocity -----------------------------------------------------
        public static double FeetPerSecondToMetresPerSecond(double fps) => fps * MetresPerFoot;
        public static double MetresPerSecondToFeetPerSecond(double mps) => mps / MetresPerFoot;

        // ---- Pressure -----------------------------------------------------
        // Small-arms pressure is quoted in PSI (US, usually copper-crusher or
        // piezo) or bar/MPa (CIP, Europe). Internally always Pa.
        public const double PascalsPerPsi = 6894.757293168;
        public const double PascalsPerBar = 1e5;
        public const double PascalsPerAtmosphere = 101325.0;

        public static double PsiToPascals(double psi) => psi * PascalsPerPsi;
        public static double PascalsToPsi(double pa) => pa / PascalsPerPsi;
        public static double MegapascalsToPascals(double mpa) => mpa * 1e6;
        public static double PascalsToMegapascals(double pa) => pa * 1e-6;
        public static double BarToPascals(double bar) => bar * PascalsPerBar;

        // ---- Energy -------------------------------------------------------
        public const double JoulesPerFootPound = 1.3558179483314;

        public static double FootPoundsToJoules(double ftlb) => ftlb * JoulesPerFootPound;
        public static double JoulesToFootPounds(double j) => j / JoulesPerFootPound;

        // ---- Temperature --------------------------------------------------
        public const double KelvinAtZeroCelsius = 273.15;

        public static double CelsiusToKelvin(double c) => c + KelvinAtZeroCelsius;
        public static double KelvinToCelsius(double k) => k - KelvinAtZeroCelsius;

        // ---- Angle --------------------------------------------------------
        public const double RadiansPerDegree = System.Math.PI / 180.0;

        // The MOA and the milliradian are the two sight-adjustment units.
        // 1 MOA = 1/60 degree. "mil" here is the true milliradian (1/1000 rad),
        // not any of the military approximations (6400/6000 mil circles).
        public const double RadiansPerMoa = RadiansPerDegree / 60.0;
        public const double RadiansPerMil = 1e-3;

        public static double DegreesToRadians(double deg) => deg * RadiansPerDegree;
        public static double RadiansToDegrees(double rad) => rad / RadiansPerDegree;
        public static double MoaToRadians(double moa) => moa * RadiansPerMoa;
        public static double RadiansToMoa(double rad) => rad / RadiansPerMoa;

        // ---- Ballistic coefficient ----------------------------------------
        // BC is conventionally quoted in lb/in^2 (an imperial artefact that
        // survives because every ballistic table in existence uses it).
        // SI equivalent is kg/m^2.
        public const double KgPerM2PerLbPerIn2 = KilogramsPerPound / (MetresPerInch * MetresPerInch);

        public static double BallisticCoefficientToSi(double lbPerIn2) => lbPerIn2 * KgPerM2PerLbPerIn2;
        public static double BallisticCoefficientFromSi(double kgPerM2) => kgPerM2 / KgPerM2PerLbPerIn2;
    }

    /// <summary>Universal physical constants used across the solvers.</summary>
    public static class PhysicalConstants
    {
        /// <summary>Universal gas constant, J/(mol*K).</summary>
        public const double UniversalGasConstant = 8.31446261815324;

        /// <summary>Specific gas constant for dry air, J/(kg*K).</summary>
        public const double DryAirGasConstant = 287.052874;

        /// <summary>Specific gas constant for water vapour, J/(kg*K).</summary>
        public const double WaterVapourGasConstant = 461.523;

        /// <summary>Ratio of specific heats for air at standard conditions (dimensionless).</summary>
        public const double AirHeatCapacityRatio = 1.4;

        /// <summary>Standard gravity, m/s^2.</summary>
        public const double StandardGravity = 9.80665;

        /// <summary>
        /// Earth's angular rotation rate, rad/s. Used for the Coriolis term,
        /// which is negligible under ~300 m but real for long-range shots.
        /// </summary>
        public const double EarthAngularVelocity = 7.292115e-5;
    }
}
