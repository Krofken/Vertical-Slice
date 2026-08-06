using System;
using Gunsmith.Crafting;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The lathe.
    ///
    /// The load-bearing test here is <see cref="Each_Cut_Changes_Only_Its_Own_Dimension"/>.
    /// The bench's whole value is that one handle moves one variable: that is what lets a
    /// player change a single thing, fire it, and actually learn what that thing did. If
    /// handles start bleeding into each other the player is running an uncontrolled
    /// experiment every time and the game stops teaching anything.
    ///
    /// The rest assert direction, never magnitude — the geometry is a real parametric
    /// model and its exact volumes are the mass integrator's business, not the bench's.
    /// </summary>
    public class LatheStationTests
    {
        private LatheStation _station;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Lathe");
            _station = go.AddComponent<LatheStation>();
            _station.Geometry = ProjectileGeometry.Default9mmFmj;
            _station.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            if (_station != null) UnityEngine.Object.DestroyImmediate(_station.gameObject);
        }

        /// <summary>Every dimension the lathe can cut, and a value to cut it to that is
        /// meaningfully different from the default 9 mm shape.</summary>
        private static (LatheOperation operation, double along)[] Cuts()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            double nose = g.NoseLength;
            double shank = nose + g.BearingSurfaceLength;

            return new[]
            {
                (LatheOperation.MeplatDiameter, g.Calibre * 0.20),
                (LatheOperation.CavityMouth,    g.MeplatDiameter * 0.40),
                (LatheOperation.CavityDepth,    nose * 0.50),
                (LatheOperation.NoseLength,     g.Calibre * 1.60),
                (LatheOperation.OgiveShape,     g.Radius * 0.80),
                (LatheOperation.BearingSurface, shank + g.Calibre * 0.50),
                (LatheOperation.BoattailLength, g.OverallLength + g.Calibre * 0.30),
                (LatheOperation.BoattailAngle,  g.Radius * 0.80)
            };
        }

        /// <summary>
        /// THE PROPERTY THE BENCH EXISTS FOR. One handle, one dimension.
        ///
        /// Some coupling is legitimate and physical — a cavity cannot be wider than the
        /// flat it opens onto, so narrowing the meplat must narrow the cavity with it.
        /// Those are declared here explicitly, and anything NOT declared must not move.
        /// </summary>
        [Test]
        public void Each_Cut_Changes_Only_Its_Own_Dimension()
        {
            foreach (var (operation, along) in Cuts())
            {
                _station.Geometry = ProjectileGeometry.Default9mmFmj;
                _station.Rebuild();

                var before = _station.Geometry;
                _station.Apply(operation, along);
                var after = _station.Geometry;

                foreach (LatheOperation other in Enum.GetValues(typeof(LatheOperation)))
                {
                    if (other == operation) continue;
                    if (IsPermittedConsequence(operation, other)) continue;

                    Assert.That(ValueOf(after, other), Is.EqualTo(ValueOf(before, other)).Within(1e-12),
                        $"cutting {operation} also moved {other}");
                }

                // Calibre is the chambering. The lathe must never touch it.
                Assert.That(after.Calibre, Is.EqualTo(before.Calibre).Within(1e-12),
                    $"cutting {operation} changed the calibre");
            }
        }

        /// <summary>Couplings that are real constraints of the shape, not leakage.</summary>
        private static bool IsPermittedConsequence(LatheOperation cut, LatheOperation other)
        {
            // A cavity cannot be wider than the meplat it opens onto.
            if (cut == LatheOperation.MeplatDiameter && other == LatheOperation.CavityMouth) return true;

            // A cavity cannot be deeper than the nose that contains it.
            if (cut == LatheOperation.NoseLength && other == LatheOperation.CavityDepth) return true;

            return false;
        }

        private static double ValueOf(in ProjectileGeometry g, LatheOperation operation)
        {
            switch (operation)
            {
                case LatheOperation.MeplatDiameter: return g.MeplatDiameter;
                case LatheOperation.CavityMouth: return g.CavityMouthDiameter;
                case LatheOperation.CavityDepth: return g.CavityDepth;
                case LatheOperation.NoseLength: return g.NoseLength;
                case LatheOperation.OgiveShape: return g.OgiveShapeParameter;
                case LatheOperation.BearingSurface: return g.BearingSurfaceLength;
                case LatheOperation.BoattailLength: return g.BoattailLength;
                case LatheOperation.BoattailAngle: return g.BoattailAngle;
            }
            return 0.0;
        }

        // ------------------------------------------------------------------

        /// <summary>Metal removed is metal off the scale. This is the bench's only
        /// number and it has to be honest.</summary>
        [Test]
        public void Drilling_A_Cavity_Takes_Weight_Off_The_Scale()
        {
            _station.Apply(LatheOperation.MeplatDiameter, _station.Geometry.Calibre * 0.25);
            _station.Rebuild();
            double solid = _station.MassGrains;

            _station.Apply(LatheOperation.CavityMouth, _station.Geometry.MeplatDiameter * 0.45);
            _station.Rebuild();
            _station.Apply(LatheOperation.CavityDepth, _station.Geometry.NoseLength * 0.6);
            _station.Rebuild();

            Assert.That(_station.IsHollowPoint, Is.True, "no cavity was cut");
            Assert.That(_station.MassGrains, Is.LessThan(solid), "drilling a hole made it heavier");
        }

        [Test]
        public void Drawing_The_Nose_Out_Makes_It_Longer_And_Heavier()
        {
            double length = _station.OverallLengthMm;
            double mass = _station.MassGrains;

            _station.Apply(LatheOperation.NoseLength, _station.Geometry.Calibre * 2.0);
            _station.Rebuild();

            Assert.That(_station.OverallLengthMm, Is.GreaterThan(length));
            Assert.That(_station.MassGrains, Is.GreaterThan(mass));
            Assert.That(_station.NoseLengthInCalibres, Is.EqualTo(2.0).Within(0.01));
        }

        /// <summary>A boattail is metal taken off the base, so the base gets narrower
        /// and the bullet gets lighter without getting shorter.</summary>
        [Test]
        public void Cutting_A_Boattail_Narrows_The_Base()
        {
            var g = _station.Geometry;
            _station.Apply(LatheOperation.BoattailLength, g.OverallLength + g.Calibre * 0.4);
            _station.Rebuild();

            Assert.That(_station.Geometry.BoattailLength, Is.GreaterThan(0.0), "no tail was cut");

            double baseBefore = _station.BaseDiameterMm;
            double massBefore = _station.MassGrains;

            _station.Apply(LatheOperation.BoattailAngle, _station.Geometry.Radius * 0.75);
            _station.Rebuild();

            Assert.That(_station.BoattailAngleDegrees, Is.GreaterThan(0.0), "no taper was cut");
            Assert.That(_station.BaseDiameterMm, Is.LessThan(baseBefore), "the base did not narrow");
            Assert.That(_station.MassGrains, Is.LessThan(massBefore), "tapering the tail added metal");
        }

        /// <summary>The tail cannot be tapered past the point where the base vanishes.</summary>
        [Test]
        public void Boattail_Cannot_Taper_The_Base_Away_Entirely()
        {
            var g = _station.Geometry;
            _station.Apply(LatheOperation.BoattailLength, g.OverallLength + g.Calibre);
            _station.Rebuild();

            // Ask for a base radius of zero, which would be a needle point.
            _station.Apply(LatheOperation.BoattailAngle, 0.0);
            _station.Rebuild();

            Assert.That(_station.BaseDiameterMm, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(_station.BoattailAngleDegrees, Is.LessThanOrEqualTo(20.0 + 1e-9),
                "the lathe ran past the end of its travel");
        }

        /// <summary>Cuts are bounded by what the lathe can physically do, so the work
        /// never becomes an impossible shape mid-drag.</summary>
        [Test]
        public void Work_Stays_Valid_Through_Every_Extreme_Of_Travel()
        {
            foreach (LatheOperation operation in Enum.GetValues(typeof(LatheOperation)))
            {
                foreach (double along in new[] { -1.0, 0.0, 1.0 })
                {
                    _station.Geometry = ProjectileGeometry.Default9mmFmj;
                    _station.Rebuild();

                    _station.Apply(operation, along);
                    _station.Rebuild();

                    Assert.That(_station.IsValid, Is.True,
                        $"driving {operation} to {along} m produced an impossible shape");
                }
            }
        }

        /// <summary>Handles have to sit ON the work, or grabbing the right one is
        /// guesswork.</summary>
        [Test]
        public void Handles_Sit_On_The_Dimension_They_Cut()
        {
            var g = _station.Geometry;

            var meplat = _station.PositionOf(LatheOperation.MeplatDiameter);
            Assert.That(meplat.x, Is.EqualTo((float)g.MeplatRadius).Within(1e-6f));
            Assert.That(meplat.z, Is.EqualTo(0f).Within(1e-6f), "the meplat is at the tip");

            var nose = _station.PositionOf(LatheOperation.NoseLength);
            Assert.That(-nose.z, Is.EqualTo((float)g.NoseLength).Within(1e-6f));

            var ogive = _station.PositionOf(LatheOperation.OgiveShape);
            Assert.That(ogive.y, Is.EqualTo((float)g.RadiusAt(g.NoseLength * 0.5)).Within(1e-6f),
                "the ogive handle must ride on the nose surface");

            var tail = _station.PositionOf(LatheOperation.BoattailLength);
            Assert.That(-tail.z, Is.EqualTo((float)g.OverallLength).Within(1e-6f));
        }
    }
}
