using Gunsmith.Crafting;
using Gunsmith.Workshop;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The other two crafting tools — the powder scale and the seating die — and the
    /// duplicate action that makes them worth using.
    ///
    /// The point of running the bench on tools rather than a form is that each tool
    /// teaches a physical quantity. So these tests check the tools behave like the
    /// instruments they are copying, and that what they hand to the simulation actually
    /// moves the simulation in the direction the tool implies.
    /// </summary>
    public class BenchToolTests
    {
        private GameObject _host;

        [SetUp]
        public void SetUp() => _host = new GameObject("Bench");

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private PowderBalance Balance() => _host.AddComponent<PowderBalance>();
        private SeatingStop Die() => _host.AddComponent<SeatingStop>();

        // ==================================================================
        // Powder balance
        // ==================================================================

        /// <summary>
        /// The moment balance is linear in how far the poise is slid, which is why a
        /// real powder beam is evenly divided. If this stops being linear the engraving
        /// on the beam becomes a lie.
        /// </summary>
        [Test]
        public void Poise_Position_Is_Linear_In_The_Charge_It_Sets()
        {
            var scale = Balance();
            scale.MaxSettingGrains = 12.0;
            scale.BeamTravel = 0.12;

            scale.SlidePoise(0.0);
            Assert.That(scale.SettingGrains, Is.EqualTo(0.0).Within(1e-9));

            scale.SlidePoise(0.06);
            Assert.That(scale.SettingGrains, Is.EqualTo(6.0).Within(1e-9), "half the beam is half the charge");

            scale.SlidePoise(0.12);
            Assert.That(scale.SettingGrains, Is.EqualTo(12.0).Within(1e-9));
        }

        [Test]
        public void Poise_Cannot_Be_Slid_Off_The_End_Of_The_Beam()
        {
            var scale = Balance();
            scale.MaxSettingGrains = 12.0;
            scale.BeamTravel = 0.12;

            scale.SlidePoise(-1.0);
            Assert.That(scale.SettingGrains, Is.EqualTo(0.0).Within(1e-9));

            scale.SlidePoise(5.0);
            Assert.That(scale.SettingGrains, Is.EqualTo(12.0).Within(1e-9));
        }

        /// <summary>An empty pan sits hard on its stop; trickling to the setting brings
        /// the beam level; going past tips it the other way. That swing IS the readout.</summary>
        [Test]
        public void Beam_Swings_From_Stop_To_Level_To_Over()
        {
            var scale = Balance();
            scale.SettingGrains = 5.5;

            Assert.That(scale.IsLevel, Is.False, "an empty pan cannot be balanced");
            Assert.That(scale.BeamAngle, Is.LessThan(0f), "an empty pan must sit poise-down");

            scale.Trickle(5.5);
            Assert.That(scale.IsLevel, Is.True, "trickling to the setting must level the beam");
            Assert.That(scale.BeamAngle, Is.EqualTo(0f).Within(0.01f));

            scale.Trickle(1.0);
            Assert.That(scale.IsOver, Is.True);
            Assert.That(scale.BeamAngle, Is.GreaterThan(0f), "an overcharge must sit pan-down");
        }

        /// <summary>
        /// The beam saturates against its stops, so it reads as level only within what
        /// the scale can actually resolve. A scale that went level at a hundredth of a
        /// grain would be lying about its own precision.
        /// </summary>
        [Test]
        public void Beam_Only_Reads_Level_Within_What_It_Can_Resolve()
        {
            var scale = Balance();
            scale.SaturationGrains = 0.1;
            scale.SettingGrains = 5.0;

            scale.Trickle(4.9);
            Assert.That(scale.IsLevel, Is.False, "a tenth of a grain light is not level");

            scale.Empty();
            scale.Trickle(5.0);
            Assert.That(scale.IsLevel, Is.True);

            Assert.That(scale.BeamAngle, Is.InRange(-scale.SwingDegrees, scale.SwingDegrees),
                "the beam must never swing past its stops");
        }

        /// <summary>The scale hands over what was actually weighed, not what was
        /// intended. Stopping short means loading short.</summary>
        [Test]
        public void Scale_Hands_Over_What_Is_In_The_Pan_Not_What_Was_Dialled()
        {
            var scale = Balance();
            scale.SettingGrains = 5.5;
            scale.Trickle(4.0);

            var design = new CartridgeDesign();
            scale.ApplyTo(ref design);

            Assert.That(Units.KilogramsToGrains(design.ChargeMass), Is.EqualTo(4.0).Within(1e-6));
        }

        [Test]
        public void Emptying_The_Pan_Puts_The_Beam_Back_On_Its_Stop()
        {
            var scale = Balance();
            scale.SettingGrains = 5.0;
            scale.Trickle(5.0);
            Assert.That(scale.IsLevel, Is.True);

            scale.Empty();

            Assert.That(scale.PouredGrains, Is.Zero);
            Assert.That(scale.IsLevel, Is.False);
        }

        // ==================================================================
        // Seating die
        // ==================================================================

        /// <summary>Screwing the stop down drives the bullet deeper and shortens the
        /// finished round. That is the whole tool.</summary>
        [Test]
        public void Screwing_The_Stop_Down_Seats_Deeper_And_Shortens_The_Round()
        {
            var die = Die();
            die.Depth = 0.0020;

            double shallowLength = die.OverallLengthMm;

            die.SetStop(0.0050);

            Assert.That(die.DepthMm, Is.GreaterThan(2.0));
            Assert.That(die.OverallLengthMm, Is.LessThan(shallowLength), "deeper must mean shorter");
        }

        [Test]
        public void Stop_Cannot_Be_Screwed_Past_Its_Travel()
        {
            var die = Die();
            die.MinimumDepth = 0.0010;
            die.MaximumDepth = 0.0090;

            die.SetStop(-1.0);
            Assert.That(die.Depth, Is.EqualTo(0.0010).Within(1e-12));

            die.SetStop(1.0);
            Assert.That(die.Depth, Is.EqualTo(0.0090).Within(1e-12));
        }

        /// <summary>Overall length is case plus the bullet standing proud of the mouth.
        /// Checked against the arithmetic a loader would do with calipers.</summary>
        [Test]
        public void Overall_Length_Is_Case_Plus_What_Stands_Proud()
        {
            var die = Die();
            die.CaseLength = 0.0192;
            die.Projectile = ProjectileGeometry.Default9mmFmj;
            die.Depth = 0.0030;

            double expected = 0.0192 + die.Projectile.OverallLength - 0.0030;
            Assert.That(die.OverallLength, Is.EqualTo(expected).Within(1e-12));
        }

        /// <summary>Opening a saved load must put the tool where that load left it, or
        /// duplicate-and-tweak silently changes a variable nobody touched.</summary>
        [Test]
        public void Die_Reads_The_Seat_Back_Off_A_Design()
        {
            var die = Die();
            var design = new CartridgeDesign
            {
                Projectile = ProjectileGeometry.Default9mmFmj,
                SeatingDepth = 0.0042
            };

            die.ReadFrom(design);

            Assert.That(die.Depth, Is.EqualTo(0.0042).Within(1e-12));
        }

        /// <summary>
        /// THE REASON THE DIE IS A TOOL AND NOT SET DRESSING. Powder burns in the space
        /// left behind the bullet, and pressure goes roughly as the inverse of that
        /// volume — so seating deeper must raise peak pressure, hard. If this ever stops
        /// being true, the seating stop has become a decoration.
        /// </summary>
        [Test]
        public void Seating_Deeper_Raises_Peak_Pressure()
        {
            var design = new CartridgeDesign
            {
                Name = "seat test",
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

            var die = Die();
            var barrel = BarrelLibrary.ServicePistol9mm;

            die.ReadFrom(design);
            die.SetStop(0.0025);
            die.ApplyTo(ref design);
            var shallow = CartridgeBaker.Bake(design, barrel);

            die.SetStop(0.0050);
            die.ApplyTo(ref design);
            var deep = CartridgeBaker.Bake(design, barrel);

            Assert.That(shallow.IsValid, Is.True, string.Join("; ", shallow.Issues));
            Assert.That(deep.Interior.PeakPressure, Is.GreaterThan(shallow.Interior.PeakPressure),
                "seating deeper must raise pressure, or the die is decoration");
        }

        // ==================================================================
        // Duplicate and tweak
        // ==================================================================

        private static CartridgeDesign Baseline() => new CartridgeDesign
        {
            Name = "baseline",
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

        /// <summary>A duplicate must be a fully baked, immediately usable load — not a
        /// stub the player has to repair before firing.</summary>
        [Test]
        public void Duplicate_Is_An_Independent_Ready_To_Load_Copy()
        {
            var library = new DesignLibrary();
            var barrel = BarrelLibrary.ServicePistol9mm;

            var original = library.Save("brass", "Brass Nose", Baseline(), barrel, day: 1);
            var copy = library.Duplicate("brass", barrel, day: 2);

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.Id, Is.Not.EqualTo(original.Id), "the copy must be its own design");
            Assert.That(copy.IsValid, Is.True, "the copy must be baked and loadable");
            Assert.That(copy.LastEditedDay, Is.EqualTo(2));

            // Changing the copy must not disturb the original — the entire point.
            var tweaked = copy.Design;
            tweaked.ChargeMass = Units.GrainsToKilograms(6.5);
            library.Save(copy.Id, copy.Name, tweaked, barrel, day: 2);

            Assert.That(library.Get("brass").Design.ChargeMass,
                Is.EqualTo(Units.GrainsToKilograms(5.5)).Within(1e-12),
                "tweaking the copy changed the original");
        }

        /// <summary>A gunsmith numbers their attempts, and that numbering is what makes
        /// a rack of recovered bullets readable weeks later.</summary>
        [Test]
        public void Duplicates_Number_Themselves()
        {
            var library = new DesignLibrary();
            var barrel = BarrelLibrary.ServicePistol9mm;

            library.Save("brass", "Brass Nose", Baseline(), barrel, day: 1);

            var second = library.Duplicate("brass", barrel, day: 1);
            Assert.That(second.Name, Is.EqualTo("Brass Nose Mk2"));

            var third = library.Duplicate(second.Id, barrel, day: 1);
            Assert.That(third.Name, Is.EqualTo("Brass Nose Mk3"));

            Assert.That(third.Id, Is.Not.EqualTo(second.Id));
        }

        [Test]
        public void Duplicating_Something_That_Does_Not_Exist_Returns_Nothing()
        {
            var library = new DesignLibrary();
            Assert.That(library.Duplicate("nope", BarrelLibrary.ServicePistol9mm, 1), Is.Null);
        }
    }
}
