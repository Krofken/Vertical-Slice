using System;

namespace Krofken.Ballistics
{
    /// <summary>Anything that can supply a zero-yaw drag coefficient versus Mach number.</summary>
    public interface IDragModel
    {
        /// <summary>Drag coefficient referenced to the projectile's frontal area,
        /// dimensionless.</summary>
        double DragCoefficient(double mach);
    }

    /// <summary>
    /// Geometric quantities the drag model needs, extracted from the projectile
    /// profile once. Computing these is an integration over the whole body; doing it
    /// per Mach evaluation (let alone per integration step) would be absurd, so it
    /// happens once at design time and the results live here.
    /// </summary>
    public struct AerodynamicShape
    {
        /// <summary>Frontal area at full calibre, m^2. The reference area every
        /// coefficient below is normalised by.</summary>
        public double ReferenceArea;

        /// <summary>Total surface area exposed to the airflow, m^2. Drives skin friction.</summary>
        public double WettedArea;

        /// <summary>Area of the flat base, m^2. A boattail shrinks this, which is the
        /// entire aerodynamic point of a boattail.</summary>
        public double BaseArea;

        /// <summary>Overall length, m. Sets the Reynolds number.</summary>
        public double Length;

        public double Calibre;

        /// <summary>Nose length in calibres. Governs the critical Mach number at
        /// which transonic drag rise begins.</summary>
        public double NoseLengthCalibres;

        /// <summary>
        /// Boattail effectiveness, 0..1. A boattail cuts base drag two ways: it
        /// shrinks the base area (already captured by <see cref="BaseArea"/>), AND it
        /// turns the flow inward so the wake closes more gently and base pressure
        /// recovers. This captures the second effect, which the area term alone
        /// misses. Derived from the axial taper (length in calibres times the tangent
        /// of the half-angle), normalised against the point where the benefit
        /// saturates -- past roughly 9 degrees the flow separates and further taper
        /// stops helping.
        /// </summary>
        public double BoattailEffectiveness;

        /// <summary>
        /// Newtonian pressure integral over the nose, m^2:
        ///
        ///     I = integral of sin^2(delta) over the frontal projected area
        ///
        /// where delta is the local inclination of the surface to the oncoming flow.
        /// The meplat contributes its full projected area (delta = 90 degrees, so
        /// sin^2 = 1), which is why a wide flat tip is aerodynamically expensive.
        /// Dividing by the reference area and multiplying by the stagnation pressure
        /// coefficient gives the nose wave drag directly.
        /// </summary>
        public double NewtonianNoseIntegral;

        /// <summary>
        /// Integrates a projectile geometry into its aerodynamic summary.
        /// Uses the same <see cref="ProjectileGeometry.RadiusAt"/> the mesh generator
        /// and mass solver use, so all three describe the identical shape.
        /// </summary>
        public static AerodynamicShape FromGeometry(in ProjectileGeometry geometry, int segments = 512)
        {
            if (segments < 16) segments = 16;

            double length = geometry.OverallLength;
            double dx = length / segments;

            double wetted = 0.0;
            double noseIntegral = 0.0;

            double previousR = geometry.RadiusAt(0.0);

            // The meplat is a flat face square-on to the flow. Newtonian theory gives
            // it the full stagnation pressure coefficient, so it enters the integral
            // as its entire projected area.
            noseIntegral += Math.PI * geometry.MeplatRadius * geometry.MeplatRadius;

            for (int i = 1; i <= segments; i++)
            {
                double x = i * dx;
                double r = geometry.RadiusAt(x);

                double dr = r - previousR;
                double slantLength = Math.Sqrt(dx * dx + dr * dr);

                // Lateral surface of a conical frustum: pi * (r1 + r2) * slant.
                wetted += Math.PI * (previousR + r) * slantLength;

                // Nose only: the body and boattail see no compressive turning, so
                // they contribute no Newtonian pressure drag.
                if (x <= geometry.NoseLength && dr > 0.0)
                {
                    // Local surface inclination to the flow.
                    double sinDelta = dr / slantLength;

                    // Projected annulus of frontal area swept between the two radii.
                    double frontalRing = Math.PI * (r * r - previousR * previousR);

                    noseIntegral += sinDelta * sinDelta * frontalRing;
                }

                previousR = r;
            }

            double baseRadius = geometry.BaseDiameter * 0.5;

            // Axial taper of the boattail, in calibres of radius reduction.
            double taper = geometry.Calibre > 0.0
                ? (geometry.BoattailLength / geometry.Calibre) * Math.Tan(geometry.BoattailAngle)
                : 0.0;
            double boattailEffectiveness = taper / 0.12;
            if (boattailEffectiveness < 0.0) boattailEffectiveness = 0.0;
            if (boattailEffectiveness > 1.0) boattailEffectiveness = 1.0;

            // The flat base itself is wetted only in the sense that it bounds the
            // separated wake; it is accounted for by base drag, not skin friction.
            return new AerodynamicShape
            {
                ReferenceArea = geometry.ReferenceArea,
                WettedArea = wetted,
                BaseArea = Math.PI * baseRadius * baseRadius,
                Length = length,
                Calibre = geometry.Calibre,
                NoseLengthCalibres = geometry.NoseLengthInCalibres,
                BoattailEffectiveness = boattailEffectiveness,
                NewtonianNoseIntegral = noseIntegral
            };
        }
    }

    /// <summary>
    /// Drag coefficient computed from the projectile's actual geometry.
    ///
    /// DECOMPOSITION -- total drag is the sum of three physically distinct sources,
    /// each of which responds to a different design parameter:
    ///
    ///   SKIN FRICTION   air shearing along the surface.
    ///                   Prandtl-Schlichting turbulent flat-plate correlation,
    ///                       Cf = 0.455 / (log10 Re)^2.58
    ///                   with a compressibility correction. Scales with wetted area,
    ///                   so a longer bullet pays for its length here.
    ///                   Dominates subsonic drag.
    ///
    ///   BASE DRAG       suction on the flat rear face where the flow separates.
    ///                   Scales directly with base area, which is exactly what a
    ///                   boattail reduces. Dominant subsonic and transonic.
    ///
    ///   WAVE DRAG       energy lost to the shock system, supersonic only.
    ///                   Modified Newtonian impact theory integrated over the nose:
    ///                       Cp = Cp_max * sin^2(delta)
    ///                   with Cp_max from the Rayleigh pitot relation behind a normal
    ///                   shock. This handles ogive shape, secant vs tangent, and the
    ///                   meplat in one integration with no special cases.
    ///                   Dominant supersonic.
    ///
    /// HONESTY NOTE -- Newtonian theory is exact only in the hypersonic limit and
    /// systematically UNDER-predicts wave drag at low supersonic Mach, where small
    /// arms actually operate. <see cref="LowSupersonicCorrection"/> bridges that gap.
    /// It is an empirical coefficient fitted so that a conventional 3-calibre-ogive
    /// boattail bullet lands near the published G7 standard drag curve -- it is the
    /// one number in this library that is a fit rather than a derivation.
    ///
    /// What that means in practice: the model reproduces the right TRENDS reliably
    /// (longer nose lowers drag, wider meplat raises it, boattail cuts base drag) and
    /// lands in the right neighbourhood in magnitude, but it is not a substitute for
    /// wind-tunnel or CFD data. If real numbers are ever needed for a specific shape,
    /// bake a <see cref="DragTable"/> from measured data and use that instead -- the
    /// rest of the library neither knows nor cares which drag source it was given.
    /// </summary>
    public sealed class GeometricDragModel : IDragModel
    {
        private readonly AerodynamicShape _shape;
        private readonly double _referenceVelocity;
        private readonly Atmosphere _atmosphere;

        /// <summary>Empirical multiplier bridging Newtonian theory to measured low
        /// supersonic wave drag. See the class remarks.</summary>
        public double LowSupersonicCorrection = 2.4;

        /// <summary>Overall scale applied to the final coefficient. Left at 1.0 for
        /// the physical model; exists so a specific projectile can be calibrated
        /// against a measured ballistic coefficient without touching the physics.</summary>
        public double CalibrationFactor = 1.0;

        /// <summary>Surface roughness multiplier on skin friction. 1.0 is a smooth
        /// drawn jacket; a cast, unpolished or fouled surface is higher.</summary>
        public double RoughnessFactor = 1.0;

        public AerodynamicShape Shape => _shape;

        /// <param name="shape">Pre-integrated geometry summary.</param>
        /// <param name="atmosphere">Air state, for viscosity and speed of sound.</param>
        /// <param name="referenceVelocity">Velocity used to fix the Reynolds number,
        /// m/s. Reynolds number varies along the trajectory but skin friction depends
        /// on it only logarithmically, so pinning it at a representative velocity
        /// costs well under a percent of total drag and removes a transcendental from
        /// the inner loop.</param>
        public GeometricDragModel(in AerodynamicShape shape, in Atmosphere atmosphere, double referenceVelocity = 500.0)
        {
            _shape = shape;
            _atmosphere = atmosphere;
            _referenceVelocity = referenceVelocity > 1.0 ? referenceVelocity : 1.0;
        }

        public double DragCoefficient(double mach)
        {
            if (mach < 0.0) mach = 0.0;

            double cd = SkinFrictionDrag(mach) + BaseDrag(mach) + WaveDrag(mach);
            return cd * CalibrationFactor;
        }

        /// <summary>
        /// Turbulent flat-plate skin friction scaled by the wetted-to-frontal area
        /// ratio. Small-arms projectiles run at Reynolds numbers of order 10^5-10^6
        /// and their surfaces are rough relative to the boundary layer, so the flow is
        /// treated as fully turbulent -- assuming laminar flow would understate this
        /// term substantially.
        /// </summary>
        private double SkinFrictionDrag(double mach)
        {
            if (_shape.ReferenceArea <= 0.0) return 0.0;

            double velocity = _referenceVelocity;
            double reynolds = _atmosphere.Density * velocity * _shape.Length / _atmosphere.DynamicViscosity;
            if (reynolds < 1e4) reynolds = 1e4;

            double logRe = Math.Log10(reynolds);
            double cfIncompressible = 0.455 / Math.Pow(logRe, 2.58);

            // Compressibility thins the boundary layer's effective density and
            // reduces skin friction. Standard engineering correction.
            double compressibility = Math.Pow(1.0 + 0.144 * mach * mach, 0.65);
            double cf = cfIncompressible / compressibility;

            return cf * RoughnessFactor * (_shape.WettedArea / _shape.ReferenceArea);
        }

        /// <summary>
        /// Suction drag on the base. The base pressure coefficient is an empirical
        /// curve: it grows through the subsonic range, peaks around Mach 1, then falls
        /// off roughly as 1/M once supersonic.
        ///
        /// The design consequence is direct and large: base drag is proportional to
        /// base AREA, so a boattail that cuts the base diameter by 25% removes about
        /// 44% of this term.
        /// </summary>
        private double BaseDrag(double mach)
        {
            if (_shape.ReferenceArea <= 0.0) return 0.0;

            double cpBase;
            if (mach < 1.0)
            {
                // Rises from about 0.10 in incompressible flow to about 0.23 at Mach 1.
                cpBase = 0.10 + 0.13 * mach * mach;
            }
            else
            {
                // Supersonic base pressure recovers as the wake narrows.
                cpBase = 0.25 / mach;
            }

            // Base pressure recovery from the boattail, on top of the area reduction.
            cpBase *= 1.0 - 0.55 * _shape.BoattailEffectiveness;

            return cpBase * (_shape.BaseArea / _shape.ReferenceArea);
        }

        /// <summary>
        /// Nose wave drag from modified Newtonian impact theory, faded in through the
        /// transonic region.
        /// </summary>
        private double WaveDrag(double mach)
        {
            if (_shape.ReferenceArea <= 0.0) return 0.0;

            // Below the critical Mach number the flow is entirely subsonic and there
            // is no shock system to lose energy to. A sharper nose delays this onset.
            double criticalMach = CriticalMach();
            if (mach <= criticalMach) return 0.0;

            double cpMax = StagnationPressureCoefficient(mach);
            double newtonian = cpMax * _shape.NewtonianNoseIntegral / _shape.ReferenceArea;

            // Empirical bridge -- see the class remarks. The correction is largest
            // just above Mach 1, where Newtonian theory is weakest, and decays as the
            // flow approaches the hypersonic regime where the theory is exact.
            double correction = 1.0 + LowSupersonicCorrection / Math.Max(mach, 1.0);
            newtonian *= correction;

            // Transonic fade-in. Wave drag does not appear discontinuously at Mcrit;
            // it builds as the supersonic pocket over the nose grows and the shock
            // sweeps back. Smoothstep gives zero slope at both ends, so the drag curve
            // has no kink to upset the trajectory integrator.
            //
            // The window is NARROW -- 0.18 Mach wide. Real drag rise is violent:
            // measured standard-projectile curves roughly double between Mach 0.9 and
            // Mach 1.0. A wider, gentler fade produces a visibly wrong transonic
            // region, which matters because that is exactly where subsonic and
            // supersonic designs have to be told apart.
            double transonicWindow = 0.18;
            double fadeEnd = criticalMach + transonicWindow;
            if (mach < fadeEnd)
            {
                double u = (mach - criticalMach) / transonicWindow;
                if (u < 0.0) u = 0.0;
                if (u > 1.0) u = 1.0;
                newtonian *= u * u * (3.0 - 2.0 * u);
            }

            return newtonian;
        }

        /// <summary>
        /// Mach number at which transonic drag rise begins. A longer, finer nose
        /// accelerates the flow over it less, so the local flow reaches sonic speed
        /// later and the drag rise is delayed.
        /// </summary>
        private double CriticalMach()
        {
            double fineness = _shape.NoseLengthCalibres;
            double t = fineness / 3.0;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return 0.80 + 0.10 * t;
        }

        /// <summary>
        /// Peak pressure coefficient at a stagnation point behind a normal shock,
        /// from the Rayleigh pitot relation. Approaches 1.839 in the hypersonic limit
        /// for air; near Mach 1 it is closer to 1.28.
        /// Returns the incompressible value of 1 below Mach 1, where there is no shock.
        /// </summary>
        public static double StagnationPressureCoefficient(double mach)
        {
            const double gamma = PhysicalConstants.AirHeatCapacityRatio;
            if (mach <= 1.0) return 1.0;

            double m2 = mach * mach;

            double numerator = (gamma + 1.0) * (gamma + 1.0) * m2;
            double denominator = 4.0 * gamma * m2 - 2.0 * (gamma - 1.0);
            double term1 = Math.Pow(numerator / denominator, gamma / (gamma - 1.0));
            double term2 = (1.0 - gamma + 2.0 * gamma * m2) / (gamma + 1.0);

            return (2.0 / (gamma * m2)) * (term1 * term2 - 1.0);
        }
    }

    /// <summary>
    /// A baked drag curve: uniformly spaced Cd samples versus Mach with linear
    /// interpolation between them.
    ///
    /// THIS IS THE PERFORMANCE STRATEGY. Evaluating <see cref="GeometricDragModel"/>
    /// costs several transcendental functions -- fine once, ruinous inside a
    /// trajectory integrator running thousands of steps per shot for dozens of
    /// projectiles. Since a projectile's shape does not change while it is in flight,
    /// the entire curve is computed ONCE when the player commits a design and baked
    /// into this table. The flight integrator then does one multiply, one floor and
    /// one lerp.
    ///
    /// The table is a plain double[], so it can be copied into a NativeArray and used
    /// from a Burst job without any conversion.
    /// </summary>
    [Serializable]
    public struct DragTable
    {
        /// <summary>Mach number of the first sample.</summary>
        public double MachMin;

        /// <summary>Mach spacing between samples.</summary>
        public double MachStep;

        /// <summary>Drag coefficients, one per sample.</summary>
        public double[] Coefficients;

        public bool IsValid => Coefficients != null && Coefficients.Length >= 2 && MachStep > 0.0;

        /// <summary>Highest Mach number the table covers.</summary>
        public double MachMax => IsValid ? MachMin + MachStep * (Coefficients.Length - 1) : 0.0;

        /// <summary>
        /// Bakes a drag model into a lookup table.
        /// </summary>
        /// <param name="model">Source model, typically a <see cref="GeometricDragModel"/>.</param>
        /// <param name="machMin">Lowest Mach to tabulate.</param>
        /// <param name="machMax">Highest Mach to tabulate. Cover well past the muzzle
        /// Mach number -- a hot load in cold air reaches higher than expected.</param>
        /// <param name="samples">Sample count. 200 across Mach 0-5 gives 0.025 Mach
        /// resolution, comfortably finer than the transonic features being resolved.</param>
        public static DragTable Bake(IDragModel model, double machMin = 0.0, double machMax = 5.0, int samples = 200)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (samples < 2) samples = 2;
            if (machMax <= machMin) machMax = machMin + 1.0;

            double step = (machMax - machMin) / (samples - 1);
            var coefficients = new double[samples];

            for (int i = 0; i < samples; i++)
                coefficients[i] = model.DragCoefficient(machMin + i * step);

            return new DragTable
            {
                MachMin = machMin,
                MachStep = step,
                Coefficients = coefficients
            };
        }

        /// <summary>
        /// Drag coefficient at a Mach number. Clamps at both ends rather than
        /// extrapolating: extrapolating a transonic curve produces nonsense, and a
        /// projectile outside the tabulated range is already outside the model's
        /// validity.
        /// </summary>
        public double Evaluate(double mach)
        {
            if (!IsValid) return 0.0;

            double position = (mach - MachMin) / MachStep;
            if (position <= 0.0) return Coefficients[0];

            int last = Coefficients.Length - 1;
            if (position >= last) return Coefficients[last];

            int index = (int)position;
            double fraction = position - index;
            return Coefficients[index] + (Coefficients[index + 1] - Coefficients[index]) * fraction;
        }
    }
}
