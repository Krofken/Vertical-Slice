using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Which material sits where inside the projectile. The geometry says what shape
    /// it is; this says what it is made of.
    /// </summary>
    [Serializable]
    public struct ProjectileMaterials
    {
        /// <summary>Material of the inner core. Its yield strength is what decides
        /// whether the projectile deforms on impact.</summary>
        public string CoreMaterialId;

        /// <summary>Material of the outer jacket. Ignored when
        /// <see cref="ProjectileGeometry.JacketThickness"/> is zero.</summary>
        public string JacketMaterialId;

        /// <summary>
        /// Material packed into the nose cavity, or null to leave it empty.
        /// Filling the cavity with a reactive compound is how an incendiary or
        /// tracer round is built -- the cavity stops being a void that promotes
        /// expansion and becomes a payload that adds mass and chemical energy.
        /// </summary>
        public string CavityFillMaterialId;

        public static ProjectileMaterials JacketedLead => new ProjectileMaterials
        {
            CoreMaterialId = MaterialLibrary.Lead,
            JacketMaterialId = MaterialLibrary.GildingMetal,
            CavityFillMaterialId = null
        };
    }

    /// <summary>
    /// Integrated mass properties of a projectile. Everything here is computed from
    /// the geometry and the material densities -- no value is authored by hand, so a
    /// player who lengthens the bullet gets the correct new mass, centre of gravity
    /// and spin inertia automatically.
    /// </summary>
    [Serializable]
    public struct MassProperties
    {
        /// <summary>Total mass, kg.</summary>
        public double Mass;

        /// <summary>Mass of the jacket alone, kg.</summary>
        public double JacketMass;

        /// <summary>Mass of the core alone, kg.</summary>
        public double CoreMass;

        /// <summary>Mass of the cavity payload, kg. Zero when the cavity is empty.</summary>
        public double PayloadMass;

        /// <summary>Total solid volume, m^3 (excludes any empty cavity).</summary>
        public double Volume;

        /// <summary>Distance from the tip to the centre of gravity, m. A forward CG
        /// relative to the centre of pressure is what keeps the projectile pointing
        /// where it is going.</summary>
        public double CentreOfGravity;

        /// <summary>Moment of inertia about the spin (longitudinal) axis, kg*m^2.</summary>
        public double AxialInertia;

        /// <summary>Moment of inertia about a transverse axis through the CG, kg*m^2.
        /// The ratio of this to the axial inertia governs gyroscopic stability.</summary>
        public double TransverseInertia;

        /// <summary>
        /// Sectional density, kg/m^2: mass divided by frontal area. The number that
        /// decides how well a projectile retains velocity and how deep it drives into
        /// a target. Two bullets with identical shape and different sectional density
        /// behave completely differently downrange.
        /// </summary>
        public double SectionalDensity;
    }

    /// <summary>
    /// Numerically integrates a <see cref="ProjectileGeometry"/> into
    /// <see cref="MassProperties"/> using the disk method.
    ///
    /// The projectile is sliced into thin discs perpendicular to the axis. At each
    /// station the cross-section is up to three concentric annuli:
    ///
    ///     [0 .. r_void)   empty cavity, or payload if the cavity is filled
    ///     [r_void .. r_j) core material
    ///     [r_j .. r_out)  jacket material
    ///
    /// Slice contributions are accumulated as moments about the tip, then shifted to
    /// the centre of gravity with the parallel-axis theorem at the end. Doing it in
    /// one pass rather than two avoids re-walking the profile.
    ///
    /// This runs at DESIGN time, once, when the player finishes a bullet -- never
    /// per frame -- so accuracy is worth more here than speed.
    /// </summary>
    public static class MassPropertiesSolver
    {
        /// <summary>Default slice count. 512 slices resolves a typical bullet profile
        /// to better than 0.1% mass error against an analytic cylinder+cone check.</summary>
        public const int DefaultSliceCount = 512;

        public static MassProperties Compute(
            in ProjectileGeometry geometry,
            in ProjectileMaterials materials,
            int sliceCount = DefaultSliceCount)
        {
            double coreDensity = DensityOf(materials.CoreMaterialId);
            double jacketDensity = geometry.JacketThickness > 0.0
                ? DensityOf(materials.JacketMaterialId)
                : 0.0;
            double payloadDensity = DensityOf(materials.CavityFillMaterialId);

            return Compute(geometry, coreDensity, jacketDensity, payloadDensity, sliceCount);
        }

        /// <summary>
        /// Density-driven overload. Lets callers integrate without touching the
        /// material library -- used by tests and by any caller supplying custom
        /// materials directly.
        /// </summary>
        public static MassProperties Compute(
            in ProjectileGeometry geometry,
            double coreDensity,
            double jacketDensity,
            double payloadDensity,
            int sliceCount = DefaultSliceCount)
        {
            if (sliceCount < 8) sliceCount = 8;

            double length = geometry.OverallLength;
            double dx = length / sliceCount;
            double jacketThickness = geometry.JacketThickness;

            double mass = 0.0;
            double jacketMass = 0.0;
            double coreMass = 0.0;
            double payloadMass = 0.0;
            double volume = 0.0;

            double momentX = 0.0;        // sum of m * x, about the tip
            double momentXX = 0.0;       // sum of m * x^2, about the tip
            double axialInertia = 0.0;   // sum of own axial inertia
            double transverseOwn = 0.0;  // sum of own transverse inertia (local axes)

            for (int i = 0; i < sliceCount; i++)
            {
                // Midpoint rule: evaluate at the centre of each slice. Second-order
                // accurate, and unlike the endpoint rule it does not systematically
                // over- or under-estimate a monotonically tapering profile.
                double x = (i + 0.5) * dx;

                double outer = geometry.RadiusAt(x);
                if (outer <= 0.0) continue;

                double voidRadius = geometry.CavityRadiusAt(x);
                if (voidRadius > outer) voidRadius = outer;

                // Jacket follows the outer surface inward by its wall thickness, but
                // can never reach past the cavity wall.
                double jacketInner = outer - jacketThickness;
                if (jacketInner < voidRadius) jacketInner = voidRadius;

                double outer2 = outer * outer;
                double jacketInner2 = jacketInner * jacketInner;
                double void2 = voidRadius * voidRadius;

                double jacketArea = Math.PI * (outer2 - jacketInner2);
                double coreArea = Math.PI * (jacketInner2 - void2);
                double payloadArea = payloadDensity > 0.0 ? Math.PI * void2 : 0.0;

                double dmJacket = jacketArea * dx * jacketDensity;
                double dmCore = coreArea * dx * coreDensity;
                double dmPayload = payloadArea * dx * payloadDensity;
                double dm = dmJacket + dmCore + dmPayload;

                if (dm <= 0.0) continue;

                jacketMass += dmJacket;
                coreMass += dmCore;
                payloadMass += dmPayload;
                mass += dm;
                volume += (jacketArea + coreArea + payloadArea) * dx;

                momentX += dm * x;
                momentXX += dm * x * x;

                // Axial inertia of a hollow disc from a to b: I = 0.5 * m * (a^2 + b^2).
                // Accumulated per annulus because each has its own density.
                axialInertia += 0.5 * dmJacket * (jacketInner2 + outer2);
                axialInertia += 0.5 * dmCore * (void2 + jacketInner2);
                if (dmPayload > 0.0) axialInertia += 0.5 * dmPayload * void2;

                // Transverse inertia of a hollow disc about its own centre:
                //     I = (1/4) * m * (a^2 + b^2) + (1/12) * m * t^2
                // The thickness term is negligible for thin slices but costs nothing.
                double thicknessTerm = dx * dx / 12.0;
                transverseOwn += dmJacket * (0.25 * (jacketInner2 + outer2) + thicknessTerm);
                transverseOwn += dmCore * (0.25 * (void2 + jacketInner2) + thicknessTerm);
                if (dmPayload > 0.0) transverseOwn += dmPayload * (0.25 * void2 + thicknessTerm);
            }

            double cg = mass > 0.0 ? momentX / mass : 0.0;

            // Parallel-axis shift from the tip to the centre of gravity.
            double transverseAboutTip = transverseOwn + momentXX;
            double transverseAboutCg = transverseAboutTip - mass * cg * cg;
            if (transverseAboutCg < 0.0) transverseAboutCg = 0.0; // numerical floor

            double area = geometry.ReferenceArea;

            return new MassProperties
            {
                Mass = mass,
                JacketMass = jacketMass,
                CoreMass = coreMass,
                PayloadMass = payloadMass,
                Volume = volume,
                CentreOfGravity = cg,
                AxialInertia = axialInertia,
                TransverseInertia = transverseAboutCg,
                SectionalDensity = area > 0.0 ? mass / area : 0.0
            };
        }

        private static double DensityOf(string materialId)
        {
            if (string.IsNullOrEmpty(materialId)) return 0.0;
            return MaterialLibrary.TryGet(materialId, out var m) ? m.Density : 0.0;
        }

        /// <summary>
        /// Gyroscopic stability factor Sg from Miller's twist rule.
        ///
        /// Sg &lt; 1.0  the projectile tumbles -- it will keyhole into the target.
        /// Sg ~ 1.4  marginal; adequate in warm dense air, unstable when it gets cold.
        /// Sg &gt; 1.5  stable. Much above 2.5 is over-stabilised, which slightly
        ///            increases drag and stops the nose tracking the trajectory arc.
        ///
        /// This is an EMPIRICAL rule (Miller, 2005), not a first-principles result --
        /// it is a fit to measured data for conventional jacketed bullets and it is
        /// the one place in this library where a correlation stands in for physics.
        /// It is used because the rigorous alternative needs the overturning-moment
        /// coefficient, which requires wind-tunnel or CFD data per shape.
        ///
        /// Consequence for the player, which is the reason it is here at all: making
        /// a bullet longer without changing the barrel's twist rate eventually makes
        /// it unstable. Long heavy projectiles are not free.
        /// </summary>
        /// <param name="geometry">Projectile shape.</param>
        /// <param name="mass">Projectile mass, kg.</param>
        /// <param name="twistRateMetres">Barrel twist: axial distance per full turn, m.</param>
        /// <param name="muzzleVelocity">Muzzle velocity, m/s.</param>
        public static double GyroscopicStability(
            in ProjectileGeometry geometry,
            double mass,
            double twistRateMetres,
            double muzzleVelocity)
        {
            if (twistRateMetres <= 0.0 || geometry.Calibre <= 0.0 || mass <= 0.0)
                return 0.0;

            // Miller's rule is stated in imperial units, so convert rather than
            // silently re-deriving the constant.
            double massGrains = Units.KilogramsToGrains(mass);
            double calibreInches = Units.MetresToInches(geometry.Calibre);
            double twistCalibres = twistRateMetres / geometry.Calibre;
            double lengthCalibres = geometry.LengthInCalibres;

            if (lengthCalibres <= 0.0) return 0.0;

            double denominator = twistCalibres * twistCalibres
                                 * calibreInches * calibreInches * calibreInches
                                 * lengthCalibres
                                 * (1.0 + lengthCalibres * lengthCalibres);
            if (denominator <= 0.0) return 0.0;

            double sg = 30.0 * massGrains / denominator;

            // The rule is calibrated at 2800 ft/s; Miller's velocity correction is a
            // cube root, which is why stability barely changes with load.
            double velocityFps = Units.MetresPerSecondToFeetPerSecond(muzzleVelocity);
            if (velocityFps > 1.0)
                sg *= Math.Pow(velocityFps / 2800.0, 1.0 / 3.0);

            return sg;
        }
    }
}
