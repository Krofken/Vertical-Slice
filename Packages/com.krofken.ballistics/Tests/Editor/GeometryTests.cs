using System;
using NUnit.Framework;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// Parametric geometry and mass integration.
    ///
    /// The mass integrator is checked against shapes with closed-form answers. If it
    /// drifts, every downstream result drifts with it -- mass feeds the interior
    /// solve, the trajectory and the penetration depth alike.
    /// </summary>
    public class GeometryTests
    {
        /// <summary>A plain cylinder: the one projectile shape with an exact answer.</summary>
        private static ProjectileGeometry Cylinder(double calibre, double length) => new ProjectileGeometry
        {
            Calibre = calibre,
            NoseLength = 0.0,
            OgiveShapeParameter = 1.0,
            MeplatDiameter = 0.0,
            BearingSurfaceLength = length,
            BoattailLength = 0.0,
            BoattailAngle = 0.0,
            JacketThickness = 0.0
        };

        [Test]
        public void Cylinder_Mass_Matches_The_Analytic_Volume()
        {
            const double calibre = 0.010;
            const double length = 0.020;
            const double density = 10000.0;

            var mass = MassPropertiesSolver.Compute(Cylinder(calibre, length), density, 0.0, 0.0);

            double expected = Math.PI * 0.005 * 0.005 * length * density;
            Assert.That(mass.Mass, Is.EqualTo(expected).Within(expected * 1e-6));
        }

        [Test]
        public void Cylinder_Centre_Of_Gravity_Is_At_Mid_Length()
        {
            var mass = MassPropertiesSolver.Compute(Cylinder(0.010, 0.020), 10000.0, 0.0, 0.0);
            Assert.That(mass.CentreOfGravity, Is.EqualTo(0.010).Within(1e-7));
        }

        [Test]
        public void Cylinder_Axial_Inertia_Matches_Half_M_R_Squared()
        {
            const double radius = 0.005;
            var mass = MassPropertiesSolver.Compute(Cylinder(0.010, 0.020), 10000.0, 0.0, 0.0);

            double expected = 0.5 * mass.Mass * radius * radius;
            Assert.That(mass.AxialInertia, Is.EqualTo(expected).Within(expected * 1e-6));
        }

        [Test]
        public void Tangent_Ogive_Radius_Matches_The_Closed_Form()
        {
            // For a pointed tangent ogive, R = (L^2 + r^2) / (2r).
            var g = new ProjectileGeometry
            {
                Calibre = 0.008,
                NoseLength = 0.020,
                OgiveShapeParameter = 1.0,
                MeplatDiameter = 0.0,
                BearingSurfaceLength = 0.010,
                JacketThickness = 0.0
            };

            double r = 0.004;
            double expected = (0.020 * 0.020 + r * r) / (2.0 * r);

            Assert.That(g.OgiveRadius, Is.EqualTo(expected).Within(expected * 1e-9));
        }

        [Test]
        public void Profile_Is_Continuous_And_Bounded()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            double previous = g.RadiusAt(0.0);

            for (int i = 1; i <= 500; i++)
            {
                double x = g.OverallLength * i / 500.0;
                double r = g.RadiusAt(x);

                Assert.That(r, Is.GreaterThanOrEqualTo(0.0), $"negative radius at x={x}");
                Assert.That(r, Is.LessThanOrEqualTo(g.Radius + 1e-9), $"radius exceeds calibre at x={x}");

                // No step larger than a tenth of the calibre between adjacent samples:
                // a discontinuity here means the ogive solve fell through to a fallback.
                Assert.That(Math.Abs(r - previous), Is.LessThan(g.Calibre * 0.1), $"discontinuity at x={x}");
                previous = r;
            }
        }

        [Test]
        public void Secant_Ogive_Has_Less_Volume_Than_A_Tangent_Ogive()
        {
            // Lowering RT flattens the arc towards a cone, which removes nose volume
            // and therefore mass. If this inverts, the arc centre is being solved on
            // the wrong side of the chord.
            var tangent = ProjectileGeometry.Default9mmFmj;
            var secant = tangent;
            secant.OgiveShapeParameter = 0.5;

            var tangentMass = MassPropertiesSolver.Compute(tangent, 11000.0, 0.0, 0.0);
            var secantMass = MassPropertiesSolver.Compute(secant, 11000.0, 0.0, 0.0);

            Assert.That(secantMass.Mass, Is.LessThan(tangentMass.Mass));
        }

        [Test]
        public void Hollow_Cavity_Removes_Mass()
        {
            var solid = ProjectileGeometry.Default9mmFmj;
            var hollow = solid;
            hollow.MeplatDiameter = 0.004;
            hollow.CavityDepth = 0.005;
            hollow.CavityMouthDiameter = 0.004;

            var solidMass = MassPropertiesSolver.Compute(solid, ProjectileMaterials.JacketedLead);
            var hollowMass = MassPropertiesSolver.Compute(hollow, ProjectileMaterials.JacketedLead);

            Assert.That(hollowMass.Mass, Is.LessThan(solidMass.Mass));
        }

        [Test]
        public void Boattail_Reduces_Base_Area()
        {
            var flat = ProjectileGeometry.Default9mmFmj;
            var tailed = flat;
            tailed.BoattailLength = 0.002;
            tailed.BoattailAngle = Units.DegreesToRadians(8.0);

            Assert.That(tailed.BaseDiameter, Is.LessThan(flat.BaseDiameter));
        }

        [Test]
        public void Validation_Rejects_Impossible_Geometry()
        {
            var bad = ProjectileGeometry.Default9mmFmj;
            bad.MeplatDiameter = bad.Calibre * 1.5;   // wider than the bullet

            Assert.That(bad.Validate(out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void A_Longer_Projectile_Is_Less_Stable()
        {
            // The core trade the player runs into: heavier means longer, and longer
            // eventually tumbles unless the twist rate keeps up.
            var shortBullet = ProjectileGeometry.Default9mmFmj;
            var longBullet = shortBullet;
            longBullet.BearingSurfaceLength = shortBullet.BearingSurfaceLength * 3.0;

            var shortMass = MassPropertiesSolver.Compute(shortBullet, ProjectileMaterials.JacketedLead);
            var longMass = MassPropertiesSolver.Compute(longBullet, ProjectileMaterials.JacketedLead);

            double shortSg = MassPropertiesSolver.GyroscopicStability(shortBullet, shortMass.Mass, 0.254, 350.0);
            double longSg = MassPropertiesSolver.GyroscopicStability(longBullet, longMass.Mass, 0.254, 350.0);

            Assert.That(longSg, Is.LessThan(shortSg));
        }

        [Test]
        public void Profile_Sampler_Emits_Feature_Boundaries_Exactly()
        {
            var g = new ProjectileGeometry
            {
                Calibre = 0.00782,
                NoseLength = 0.020,
                OgiveShapeParameter = 1.0,
                MeplatDiameter = 0.0008,
                BearingSurfaceLength = 0.020,
                BoattailLength = 0.005,
                BoattailAngle = Units.DegreesToRadians(9.0),
                JacketThickness = 0.0006
            };

            var points = ProfileSampler.Sample(g, 32, 2);

            Assert.That(points.Length, Is.GreaterThan(4));
            Assert.That(points[0].X, Is.EqualTo(0.0).Within(1e-12), "must start at the tip");
            Assert.That(points[points.Length - 1].X, Is.EqualTo(g.OverallLength).Within(1e-9), "must end at the base");

            // Strictly increasing, so revolving it cannot produce inverted triangles.
            for (int i = 1; i < points.Length; i++)
                Assert.That(points[i].X, Is.GreaterThan(points[i - 1].X), $"non-monotonic at index {i}");
        }
    }
}
