using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Air state at the shooter's position. Drag is linear in density and the drag
    /// coefficient is a function of Mach number, so getting density and speed of
    /// sound right matters more than most people expect: a hot humid day at altitude
    /// versus a cold dry day at sea level moves a long-range impact point by a
    /// visible margin.
    ///
    /// Immutable and blittable: build one per shot (or per environment change) and
    /// pass it by <c>in</c> to the integrator.
    /// </summary>
    [Serializable]
    public struct Atmosphere
    {
        /// <summary>Air density, kg/m^3.</summary>
        public double Density;

        /// <summary>Speed of sound, m/s. Divides velocity to give Mach number.</summary>
        public double SpeedOfSound;

        /// <summary>Dynamic viscosity, Pa*s. Feeds the Reynolds number for skin friction.</summary>
        public double DynamicViscosity;

        /// <summary>Absolute temperature, K.</summary>
        public double Temperature;

        /// <summary>Absolute (station) pressure, Pa.</summary>
        public double Pressure;

        /// <summary>Wind velocity in world axes, m/s. This is the velocity OF THE AIR,
        /// so a headwind on a downrange (+X) shot has a negative X component.</summary>
        public Vec3 Wind;

        /// <summary>
        /// ICAO standard atmosphere at sea level, still air, dry.
        /// 15 degrees C, 101325 Pa, 0% relative humidity.
        /// </summary>
        public static Atmosphere Standard => Create(
            temperatureKelvin: 288.15,
            pressurePascals: 101325.0,
            relativeHumidity: 0.0,
            wind: Vec3.Zero);

        /// <summary>
        /// Builds an atmosphere from directly measured conditions -- what a shooter
        /// actually reads off a weather meter (station pressure, not sea-level-corrected).
        /// </summary>
        /// <param name="temperatureKelvin">Ambient air temperature, K.</param>
        /// <param name="pressurePascals">Absolute station pressure, Pa.</param>
        /// <param name="relativeHumidity">0..1.</param>
        /// <param name="wind">Air velocity in world axes, m/s.</param>
        public static Atmosphere Create(
            double temperatureKelvin,
            double pressurePascals,
            double relativeHumidity,
            Vec3 wind)
        {
            double t = temperatureKelvin;
            if (t < 1.0) t = 1.0; // guard: avoid divide-by-zero in the gas law

            double rh = relativeHumidity < 0 ? 0 : (relativeHumidity > 1 ? 1 : relativeHumidity);

            // Partial pressure of water vapour. Saturation pressure from the Buck
            // equation (1981/1996 revision) -- accurate to ~0.05% over -40..+50 C,
            // considerably better than the older Tetens form.
            double tc = t - Units.KelvinAtZeroCelsius;
            double saturationPressure = 611.21 * Math.Exp((18.678 - tc / 234.5) * (tc / (257.14 + tc)));
            double vapourPressure = rh * saturationPressure;
            if (vapourPressure > pressurePascals) vapourPressure = pressurePascals;

            double dryPressure = pressurePascals - vapourPressure;

            // Humid air is LESS dense than dry air at the same pressure, because
            // water (18 g/mol) is lighter than the ~29 g/mol average of dry air.
            // Summing partial densities via each species' own gas constant handles
            // this correctly and needs no fudge factor.
            double density =
                dryPressure / (PhysicalConstants.DryAirGasConstant * t) +
                vapourPressure / (PhysicalConstants.WaterVapourGasConstant * t);

            // Effective specific gas constant of the mixture, so the speed of sound
            // picks up the (small) humidity effect for free.
            double effectiveGasConstant = density > 0 ? pressurePascals / (density * t) : PhysicalConstants.DryAirGasConstant;
            double speedOfSound = Math.Sqrt(PhysicalConstants.AirHeatCapacityRatio * effectiveGasConstant * t);

            return new Atmosphere
            {
                Temperature = t,
                Pressure = pressurePascals,
                Density = density,
                SpeedOfSound = speedOfSound,
                DynamicViscosity = SutherlandViscosity(t),
                Wind = wind
            };
        }

        /// <summary>
        /// Builds an atmosphere from altitude using the ICAO standard lapse rate,
        /// then overrides temperature/humidity if the caller supplies them.
        /// Valid to the tropopause (11 km); beyond that the lapse rate goes to zero
        /// and this model is wrong, which no small-arms trajectory will ever reach.
        /// </summary>
        public static Atmosphere FromAltitude(
            double altitudeMetres,
            double temperatureKelvin = double.NaN,
            double relativeHumidity = 0.0,
            Vec3 wind = default)
        {
            const double seaLevelTemperature = 288.15;   // K
            const double seaLevelPressure = 101325.0;    // Pa
            const double lapseRate = 0.0065;             // K/m

            double h = altitudeMetres < 0 ? 0 : (altitudeMetres > 11000 ? 11000 : altitudeMetres);
            double standardTemperature = seaLevelTemperature - lapseRate * h;

            // Barometric formula for a linear temperature gradient.
            double exponent = PhysicalConstants.StandardGravity /
                              (PhysicalConstants.DryAirGasConstant * lapseRate);
            double pressure = seaLevelPressure * Math.Pow(standardTemperature / seaLevelTemperature, exponent);

            // If the caller measured a real temperature, use it -- pressure still
            // comes from altitude, which is how a ballistic solver is normally fed.
            double temperature = double.IsNaN(temperatureKelvin) ? standardTemperature : temperatureKelvin;

            return Create(temperature, pressure, relativeHumidity, wind);
        }

        /// <summary>
        /// Sutherland's law for the dynamic viscosity of air, Pa*s.
        /// Reference values are for air: mu0 = 1.716e-5 at T0 = 273.15 K, S = 110.4 K.
        /// </summary>
        private static double SutherlandViscosity(double temperatureKelvin)
        {
            const double mu0 = 1.716e-5;
            const double t0 = 273.15;
            const double s = 110.4;

            double t = temperatureKelvin;
            return mu0 * Math.Pow(t / t0, 1.5) * (t0 + s) / (t + s);
        }

        /// <summary>Mach number for a given speed through this air.</summary>
        public double MachNumber(double speedMetresPerSecond) => speedMetresPerSecond / SpeedOfSound;
    }
}
