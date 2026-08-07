using Gunsmith.Crafting;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The propellant mill.
    ///
    /// Every setting on this station was hardcoded before it existed, so the tests that
    /// matter are the ones proving the settings REACH THE PHYSICS. A mill that changed
    /// only the display would be the worst kind of decoration: it would look like a
    /// meaningful choice and teach the player nothing.
    ///
    /// Directions only, never magnitudes. The interior ballistics model is calibrated,
    /// and pinning it here would break it every time the calibration improves. What must
    /// never break is that a finer web burns faster than a coarse one.
    /// </summary>
    public class PropellantMillTests
    {
        private GameObject _host;
        private PropellantMill _mill;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Mill");
            _mill = _host.AddComponent<PropellantMill>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private static CartridgeDesign Baseline() => new CartridgeDesign
        {
            Name = "mill test",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Projectile = ProjectileGeometry.Default9mmFmj,
            Materials = ProjectileMaterials.JacketedLead,
            PropellantId = PropellantLibrary.SingleBase,
            GrainShape = GrainShape.Sphere,
            WebThickness = 3.5e-5,
            DeterrentCoating = 0.3,
            ChargeMass = Units.GrainsToKilograms(5.5),
            SeatingDepth = 0.0030
        };

        private static BakedCartridge Bake(in CartridgeDesign design)
            => CartridgeBaker.Bake(design, BarrelLibrary.ServicePistol9mm);

        // ==================================================================

        [Test]
        public void Mill_Writes_Its_Whole_Recipe_Into_A_Design()
        {
            _mill.BaseId = PropellantLibrary.SingleBase;
            _mill.SetShape(GrainShape.SevenPerforated);
            _mill.SetWeb(2.5e-4);
            _mill.SetDeterrent(0.6);

            var design = Baseline();
            _mill.ApplyTo(ref design);

            Assert.That(design.PropellantId, Is.EqualTo(PropellantLibrary.SingleBase));
            Assert.That(design.GrainShape, Is.EqualTo(GrainShape.SevenPerforated));
            Assert.That(design.WebThickness, Is.EqualTo(2.5e-4).Within(1e-12));
            Assert.That(design.DeterrentCoating, Is.EqualTo(0.6).Within(1e-12));
        }

        [Test]
        public void Mill_Reads_A_Saved_Load_Back_Onto_The_Tools()
        {
            var design = Baseline();
            design.GrainShape = GrainShape.Flake;
            design.WebThickness = 1.2e-4;
            design.DeterrentCoating = 0.45;

            _mill.ReadFrom(design);

            Assert.That(_mill.Shape, Is.EqualTo(GrainShape.Flake));
            Assert.That(_mill.WebThickness, Is.EqualTo(1.2e-4).Within(1e-12));
            Assert.That(_mill.DeterrentCoating, Is.EqualTo(0.45).Within(1e-12));
        }

        [Test]
        public void Mill_Cannot_Press_Outside_Its_Travel()
        {
            _mill.SetWeb(-1.0);
            Assert.That(_mill.WebThickness, Is.EqualTo(_mill.MinimumWeb).Within(1e-15));

            _mill.SetWeb(1.0);
            Assert.That(_mill.WebThickness, Is.EqualTo(_mill.MaximumWeb).Within(1e-15));

            _mill.SetDeterrent(-1.0);
            Assert.That(_mill.DeterrentCoating, Is.Zero);

            _mill.SetDeterrent(5.0);
            Assert.That(_mill.DeterrentCoating, Is.EqualTo(1.0).Within(1e-15));
        }

        // ------------------------------------------------------------------
        // The settings must reach the physics
        // ------------------------------------------------------------------

        /// <summary>
        /// THE POINT OF THE STATION. The web is the distance the flame front has to
        /// travel to consume a grain, so a fine powder burns fast and spikes pressure
        /// while a coarse one burns slowly and does not. Same chemistry, same charge.
        /// If this ever stops holding, the mill is decoration.
        /// </summary>
        [Test]
        public void Finer_Grain_Burns_Faster_And_Peaks_Harder()
        {
            var design = Baseline();

            _mill.ReadFrom(design);
            _mill.SetShape(GrainShape.Sphere);

            // Both webs have to produce a load that survives, or the comparison is
            // between a cartridge and a burst case. Pressing finer than this at 5.5
            // grains ruptures it -- which is its own test, below.
            _mill.SetWeb(3.5e-5);
            _mill.ApplyTo(ref design);
            var fine = Bake(design);

            _mill.SetWeb(1.5e-4);
            _mill.ApplyTo(ref design);
            var coarse = Bake(design);

            Assert.That(fine.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Success), fine.Interior.Message);
            Assert.That(coarse.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Success), coarse.Interior.Message);

            Assert.That(fine.Interior.PeakPressure, Is.GreaterThan(coarse.Interior.PeakPressure),
                "a finer web must peak harder at the same charge");
            Assert.That(fine.Interior.BurntFractionAtMuzzle,
                Is.GreaterThan(coarse.Interior.BurntFractionAtMuzzle),
                "a finer web must be further through its burn at the muzzle");
        }

        /// <summary>
        /// The mill lets you press the grain as fine as you like, and the physics
        /// punishes it rather than a rule forbidding it. At a service charge a 25
        /// micrometre web takes a working 9 mm past the case's pressure limit and the
        /// case lets go.
        ///
        /// This is the canon's "never cap fantasy values, let physics do the limiting"
        /// working exactly as intended, and it is the lesson the station exists to
        /// teach: charge weight alone tells you nothing without knowing the powder.
        /// </summary>
        [Test]
        public void Pressing_The_Grain_Too_Fine_Bursts_The_Case()
        {
            var design = Baseline();
            _mill.ReadFrom(design);
            _mill.SetShape(GrainShape.Sphere);

            _mill.SetWeb(2.5e-5);
            _mill.ApplyTo(ref design);
            var overpressure = Bake(design);

            Assert.That(overpressure.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Overpressure),
                "an unusably fine powder at a service charge must burst the case");
            Assert.That(overpressure.IsValid, Is.False, "a burst load must not be loadable");
        }

        /// <summary>
        /// Grain form decides whether the burning surface shrinks, holds or grows. A
        /// sphere's surface shrinks as it is consumed, so it dumps its gas early; a
        /// seven-perforated grain's surface grows, holding pressure up as the bullet
        /// moves. Same web, same charge, different pressure curve.
        /// </summary>
        [Test]
        public void Grain_Form_Changes_The_Pressure_Curve()
        {
            var design = Baseline();
            _mill.ReadFrom(design);
            _mill.SetWeb(1.0e-4);
            _mill.SetDeterrent(0.0);

            _mill.SetShape(GrainShape.Sphere);
            _mill.ApplyTo(ref design);
            var degressive = Bake(design);

            _mill.SetShape(GrainShape.SevenPerforated);
            _mill.ApplyTo(ref design);
            var progressive = Bake(design);

            Assert.That(degressive.Interior.PeakPressure,
                Is.Not.EqualTo(progressive.Interior.PeakPressure).Within(1e6),
                "grain form must reach the pressure curve, not just the picture in the tray");
        }

        /// <summary>A surface deterrent slows the early burn, so it must not leave the
        /// load untouched.</summary>
        [Test]
        public void Deterrent_Coating_Reaches_The_Burn()
        {
            var design = Baseline();
            _mill.ReadFrom(design);
            _mill.SetShape(GrainShape.Sphere);
            _mill.SetWeb(5.0e-5);

            _mill.SetDeterrent(0.0);
            _mill.ApplyTo(ref design);
            var bare = Bake(design);

            _mill.SetDeterrent(0.8);
            _mill.ApplyTo(ref design);
            var coated = Bake(design);

            Assert.That(coated.Interior.PeakPressure,
                Is.Not.EqualTo(bare.Interior.PeakPressure).Within(1e6),
                "coating the grains must change how they burn");
        }

        /// <summary>
        /// Packing is why a bulky powder will not fit in the case. It has no effect on
        /// the burn at all, so it must depend on the grain FORM and not on how fine the
        /// grains are pressed.
        /// </summary>
        [Test]
        public void Grain_Form_Decides_How_Densely_The_Powder_Packs()
        {
            _mill.SetShape(GrainShape.Sphere);
            _mill.SetWeb(5.0e-5);
            double spheres = _mill.PackingFraction;

            _mill.SetShape(GrainShape.Flake);
            double flakes = _mill.PackingFraction;

            Assert.That(spheres, Is.GreaterThan(flakes),
                "spheres tumble into a dense bed; flakes bridge and trap air");

            _mill.SetShape(GrainShape.Sphere);
            _mill.SetWeb(3.0e-4);
            Assert.That(_mill.PackingFraction, Is.EqualTo(spheres).Within(1e-12),
                "packing is a property of the form, not of how fine it is pressed");
        }

        // ------------------------------------------------------------------
        // The no-numbers rule
        // ------------------------------------------------------------------

        /// <summary>
        /// The mill may describe the SHAPE in the pan. It may not predict what the
        /// powder will do — that is what walking out to the range is for.
        /// </summary>
        [Test]
        public void Burn_Character_Describes_The_Grain_Not_The_Result()
        {
            _mill.SetShape(GrainShape.Sphere);
            Assert.That(_mill.BurnCharacter, Does.Contain("shrinks"));

            _mill.SetShape(GrainShape.Flake);
            Assert.That(_mill.BurnCharacter, Does.Contain("holds"));

            _mill.SetShape(GrainShape.SevenPerforated);
            Assert.That(_mill.BurnCharacter, Does.Contain("grows"));

            foreach (GrainShape shape in System.Enum.GetValues(typeof(GrainShape)))
            {
                _mill.SetShape(shape);
                string text = _mill.BurnCharacter.ToLowerInvariant();

                foreach (string banned in new[] { "pressure", "velocity", "fast", "slow", "power", "energy" })
                    Assert.That(text, Does.Not.Contain(banned),
                        $"'{banned}' predicts performance and must not appear at the bench");
            }
        }

        /// <summary>Cycling the die must reach every form the mill can press, and come
        /// back round.</summary>
        [Test]
        public void Cycling_The_Die_Visits_Every_Form()
        {
            _mill.SetShape(GrainShape.Sphere);

            var seen = new System.Collections.Generic.HashSet<GrainShape>();
            for (int i = 0; i < 6; i++)
            {
                seen.Add(_mill.Shape);
                _mill.NextShape();
            }

            Assert.That(seen.Contains(GrainShape.Sphere), Is.True, "sphere die never came round");
            Assert.That(seen.Contains(GrainShape.Flake), Is.True, "flake die never came round");
            Assert.That(seen.Contains(GrainShape.Cord), Is.True, "cord die never came round");
            Assert.That(seen.Contains(GrainShape.SinglePerforated), Is.True, "tube die never came round");
            Assert.That(seen.Contains(GrainShape.SevenPerforated), Is.True, "seven-perf die never came round");

            Assert.That(seen.Contains(GrainShape.Custom), Is.False,
                "Custom takes hand-written coefficients and is not a die the mill can press");
        }
    }
}
