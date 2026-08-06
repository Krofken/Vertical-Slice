using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// A projectile defined as a solid of revolution.
    ///
    /// THE KEY IDEA: a bullet is a profile curve spun around an axis. Eleven numbers
    /// describe essentially every projectile ever manufactured, and the SAME eleven
    /// numbers drive the aerodynamics, the mass properties, and the render mesh.
    /// There is no separate "art" bullet and "physics" bullet that can drift apart,
    /// and no hand-authored mesh is ever needed -- any calibre and any shape the
    /// player dials in is lathed at runtime from this struct.
    ///
    /// Axis convention for the profile: x runs from the tip (x = 0) to the base
    /// (x = OverallLength), r is the radius at that station.
    ///
    ///        meplat
    ///          |___
    ///         /    ----____                 <- nose (ogive arc)
    ///        |             ---____
    ///        |____________________|         <- bearing surface (full calibre)
    ///         \                   |
    ///          \__________________|         <- boattail
    ///
    ///        x=0                            x = OverallLength
    ///
    /// Every field is a real dimension in metres or radians. Nothing here is a
    /// gameplay stat.
    /// </summary>
    [Serializable]
    public struct ProjectileGeometry
    {
        /// <summary>Maximum body diameter, m. This is the bore-riding diameter and
        /// the aerodynamic reference diameter.</summary>
        public double Calibre;

        /// <summary>Axial length of the nose, from the meplat plane to the shoulder, m.
        /// Longer noses cut supersonic wave drag sharply -- this is the single
        /// biggest aerodynamic lever the player has.</summary>
        public double NoseLength;

        /// <summary>
        /// Ogive shape parameter RT, dimensionless, in (0, 1].
        ///   RT = 1   -> tangent ogive: the arc meets the shank with no shoulder.
        ///   RT -> 0  -> the arc radius grows without bound and the nose becomes a cone.
        /// Values below 1 are secant ogives (the "VLD" shape): slightly lower drag,
        /// at the cost of a shoulder that is more sensitive to how the round is seated.
        /// Defined as RT = R_tangent / R_actual, matching the standard aeroprediction
        /// headshape parameter.
        /// </summary>
        public double OgiveShapeParameter;

        /// <summary>Diameter of the flat at the tip, m. Never truly zero on a real
        /// bullet. A large meplat adds a lot of supersonic wave drag but produces a
        /// much wider initial wound channel -- the direct trade behind a wadcutter.</summary>
        public double MeplatDiameter;

        /// <summary>Length of the full-calibre cylindrical section, m. This is what
        /// engraves into the rifling, so it sets both spin transfer and bore friction.</summary>
        public double BearingSurfaceLength;

        /// <summary>Axial length of the tapered tail, m. Zero for a flat-base bullet.</summary>
        public double BoattailLength;

        /// <summary>Half-angle of the boattail taper, rad. Real designs cluster
        /// around 7-9 degrees; past about 12 the flow separates and the drag saving
        /// is lost.</summary>
        public double BoattailAngle;

        /// <summary>Depth of the nose cavity, m. Zero for a solid or full-jacket
        /// projectile. A cavity is what lets the nose peel back and mushroom.</summary>
        public double CavityDepth;

        /// <summary>Diameter of the cavity mouth at the meplat, m.</summary>
        public double CavityMouthDiameter;

        /// <summary>Jacket wall thickness, m. Zero for an unjacketed cast projectile.
        /// A thick jacket resists expansion; a thin one lets the core drive it open.</summary>
        public double JacketThickness;

        /// <summary>Depth of a cavity in the base, m. Used to shift the centre of
        /// gravity forward (improving stability) or to let the skirt obturate.</summary>
        public double BaseCavityDepth;

        /// <summary>Diameter of the base cavity, m.</summary>
        public double BaseCavityDiameter;

        // ---- Derived dimensions -------------------------------------------

        /// <summary>Total length tip to base, m.</summary>
        public double OverallLength => NoseLength + BearingSurfaceLength + BoattailLength;

        /// <summary>Body radius, m.</summary>
        public double Radius => Calibre * 0.5;

        /// <summary>Meplat radius, m.</summary>
        public double MeplatRadius => MeplatDiameter * 0.5;

        /// <summary>Diameter at the base after the boattail taper, m.</summary>
        public double BaseDiameter
        {
            get
            {
                double d = Calibre - 2.0 * BoattailLength * Math.Tan(BoattailAngle);
                return d < 0.0 ? 0.0 : d;
            }
        }

        /// <summary>Aerodynamic reference area (frontal area at full calibre), m^2.</summary>
        public double ReferenceArea => Math.PI * Radius * Radius;

        /// <summary>Overall length expressed in calibres -- the standard way to talk
        /// about how long a bullet is, and the input to stability rules.</summary>
        public double LengthInCalibres => Calibre > 0 ? OverallLength / Calibre : 0.0;

        /// <summary>Nose length in calibres. The dominant wave-drag parameter.</summary>
        public double NoseLengthInCalibres => Calibre > 0 ? NoseLength / Calibre : 0.0;

        /// <summary>True if the nose has an open cavity (a hollow point).</summary>
        public bool IsHollowPoint => CavityDepth > 0.0 && CavityMouthDiameter > 0.0;

        /// <summary>
        /// Radius of the ogive arc, m, after applying the shape parameter.
        /// The tangent-ogive radius through the meplat point (0, r_m) and the
        /// shoulder point (L_n, r_s), with tangency to the shank at the shoulder, is
        ///
        ///     R_tangent = [ L_n^2 + (r_s - r_m)^2 ] / [ 2 * (r_s - r_m) ]
        ///
        /// which reduces to the familiar (L^2 + r^2)/(2r) when the meplat is zero.
        /// The actual radius is then R = R_tangent / RT.
        /// </summary>
        public double OgiveRadius
        {
            get
            {
                double rs = Radius;
                double rm = MeplatRadius;
                double dr = rs - rm;
                if (dr <= 1e-9 || NoseLength <= 0.0) return double.PositiveInfinity;

                double rTangent = (NoseLength * NoseLength + dr * dr) / (2.0 * dr);
                double rt = OgiveShapeParameter;
                if (rt <= 1e-6) return double.PositiveInfinity; // degenerates to a cone
                if (rt > 1.0) rt = 1.0;

                return rTangent / rt;
            }
        }

        /// <summary>
        /// Validates the geometry and explains what is wrong. Called before any
        /// solver touches the struct -- a physically impossible shape produces NaNs
        /// deep inside an integrator otherwise, which is miserable to debug.
        /// </summary>
        public bool Validate(out string error)
        {
            if (Calibre <= 0.0) { error = "Calibre must be positive."; return false; }
            if (NoseLength < 0.0) { error = "Nose length cannot be negative."; return false; }
            if (BearingSurfaceLength < 0.0) { error = "Bearing surface length cannot be negative."; return false; }
            if (BoattailLength < 0.0) { error = "Boattail length cannot be negative."; return false; }
            if (OverallLength <= 0.0) { error = "Overall length must be positive."; return false; }
            if (MeplatDiameter < 0.0 || MeplatDiameter >= Calibre)
            { error = "Meplat diameter must be between zero and the calibre."; return false; }
            if (OgiveShapeParameter <= 0.0 || OgiveShapeParameter > 1.0)
            { error = "Ogive shape parameter must be in (0, 1]."; return false; }
            if (BoattailAngle < 0.0 || BoattailAngle >= Math.PI * 0.5)
            { error = "Boattail angle must be in [0, 90) degrees."; return false; }
            if (BaseDiameter <= 0.0)
            { error = "Boattail is too long or too steep -- it tapers the base to nothing."; return false; }
            if (CavityDepth < 0.0 || CavityDepth >= OverallLength)
            { error = "Cavity depth must be less than the overall length."; return false; }
            if (CavityMouthDiameter < 0.0 || CavityMouthDiameter > MeplatDiameter + 1e-9)
            {
                // The cavity mouth is the hole in the meplat, so it cannot be wider
                // than the meplat itself.
                error = "Cavity mouth cannot be wider than the meplat.";
                return false;
            }
            if (JacketThickness < 0.0 || JacketThickness >= Radius)
            { error = "Jacket thickness must be less than the body radius."; return false; }
            if (BaseCavityDepth < 0.0 || BaseCavityDepth >= OverallLength)
            { error = "Base cavity depth must be less than the overall length."; return false; }
            if (BaseCavityDiameter < 0.0 || BaseCavityDiameter > BaseDiameter + 1e-9)
            { error = "Base cavity cannot be wider than the base."; return false; }

            error = null;
            return true;
        }

        /// <summary>
        /// Outer radius of the body at axial station x, in metres.
        /// This one function defines the whole silhouette: the mesh generator, the
        /// mass integrator and the drag model all call it, so they can never disagree
        /// about what shape the bullet is.
        /// </summary>
        public double RadiusAt(double x)
        {
            double total = OverallLength;
            if (x <= 0.0) return MeplatRadius;
            if (x >= total) return BaseDiameter * 0.5;

            double rs = Radius;

            // --- Nose -------------------------------------------------------
            if (x < NoseLength)
            {
                double rm = MeplatRadius;
                double dr = rs - rm;
                if (dr <= 1e-9) return rs; // degenerate: nose is already full calibre

                double r = OgiveRadius;

                // Cone limit: infinite arc radius means straight-line interpolation.
                if (double.IsInfinity(r) || r > 1e6)
                    return rm + dr * (x / NoseLength);

                // Circle through P1 = (0, rm) and P2 = (NoseLength, rs) with radius r.
                // The centre lies on the perpendicular bisector of the chord; of the
                // two solutions we take the one BELOW the chord so the arc bulges
                // outward, which is the physical nose shape.
                double cx = NoseLength, cy = dr;              // chord vector components
                double chord = Math.Sqrt(cx * cx + cy * cy);
                double half = chord * 0.5;

                double h2 = r * r - half * half;
                if (h2 < 0.0)
                {
                    // Arc radius too small to span the chord. Should be unreachable
                    // after Validate(), but fall back to a cone rather than NaN.
                    return rm + dr * (x / NoseLength);
                }

                double h = Math.Sqrt(h2);
                double mx = NoseLength * 0.5, my = (rm + rs) * 0.5;   // chord midpoint
                double nx = cy / chord, ny = -cx / chord;             // unit normal, ny < 0

                double centreX = mx + h * nx;
                double centreY = my + h * ny;

                double dx = x - centreX;
                double inside = r * r - dx * dx;
                if (inside <= 0.0) return rm + dr * (x / NoseLength);

                return centreY + Math.Sqrt(inside);
            }

            // --- Bearing surface --------------------------------------------
            double boattailStart = NoseLength + BearingSurfaceLength;
            if (x <= boattailStart) return rs;

            // --- Boattail ---------------------------------------------------
            double intoTail = x - boattailStart;
            double r2 = rs - intoTail * Math.Tan(BoattailAngle);
            return r2 < 0.0 ? 0.0 : r2;
        }

        /// <summary>
        /// Inner radius of the void at axial station x, in metres. Zero where the
        /// projectile is solid. Covers both the nose cavity of a hollow point and a
        /// base cavity; the mass integrator subtracts this from the solid area.
        /// </summary>
        public double CavityRadiusAt(double x)
        {
            double inner = 0.0;

            // Nose cavity: widest at the mouth, tapering to a point at its floor.
            // Modelling it as a cone rather than a cylinder matters -- the taper is
            // what makes the jacket peel back progressively instead of all at once.
            if (IsHollowPoint && x >= 0.0 && x < CavityDepth)
            {
                double mouth = CavityMouthDiameter * 0.5;
                inner = mouth * (1.0 - x / CavityDepth);
            }

            // Base cavity: mirrored, opening at the base.
            if (BaseCavityDepth > 0.0 && BaseCavityDiameter > 0.0)
            {
                double total = OverallLength;
                double from = total - BaseCavityDepth;
                if (x > from && x <= total)
                {
                    double mouth = BaseCavityDiameter * 0.5;
                    double t = (x - from) / BaseCavityDepth;
                    double baseInner = mouth * t;
                    if (baseInner > inner) inner = baseInner;
                }
            }

            return inner;
        }

        /// <summary>
        /// A conventional full-metal-jacket 9 mm bullet, useful as a starting point
        /// and as a fixture in tests. 9.02 mm, 115 grain class, flat base.
        /// </summary>
        public static ProjectileGeometry Default9mmFmj => new ProjectileGeometry
        {
            Calibre = 0.00902,
            NoseLength = 0.0055,
            OgiveShapeParameter = 1.0,
            MeplatDiameter = 0.0022,
            BearingSurfaceLength = 0.0075,
            BoattailLength = 0.0,
            BoattailAngle = 0.0,
            CavityDepth = 0.0,
            CavityMouthDiameter = 0.0,
            JacketThickness = 0.0004,
            BaseCavityDepth = 0.0,
            BaseCavityDiameter = 0.0
        };
    }
}
