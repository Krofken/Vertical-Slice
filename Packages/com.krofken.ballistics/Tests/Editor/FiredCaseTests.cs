using System;
using NUnit.Framework;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// The fired case — the range's pressure gauge, with no numbers on it.
    ///
    /// What matters is the ORDERING. The primer cup is softer than the case head and
    /// unsupported over the pin hole, so it must yield first; brass must extrude into
    /// unsupported holes before the pocket lets go; the case must not come apart before
    /// showing anything. A gauge whose signs appeared out of order would teach the
    /// player the wrong thing about their own load.
    /// </summary>
    public class FiredCaseTests
    {
        private static CartridgeCase Case() => CartridgeCaseLibrary.Get(CartridgeCaseLibrary.NineMillimetre);

        private static FiredCase At(double fractionOfLimit)
        {
            var c = Case();
            return FiredCaseReader.Read(c.MaximumPressure * fractionOfLimit, c);
        }

        [Test]
        public void A_Healthy_Load_Leaves_Nothing_To_See()
        {
            var fired = At(0.70);

            Assert.That(fired.IsUnremarkable, Is.True, fired.Describe());
            Assert.That(fired.Primer, Is.EqualTo(PrimerCondition.Rounded));
            Assert.That(fired.Ruptured, Is.False);
        }

        /// <summary>The primer is the first thing to go, because it is the softest and
        /// least supported part of the cartridge. This is the sign handloaders read.</summary>
        [Test]
        public void The_Primer_Flattens_Before_Anything_Else_Shows()
        {
            var fired = At(1.02);

            Assert.That(fired.Primer, Is.EqualTo(PrimerCondition.Flattened));
            Assert.That(fired.Head, Is.EqualTo(CaseHeadCondition.Clean), "the head must still be clean");
            Assert.That(fired.NeckSplit, Is.False);
            Assert.That(fired.Ruptured, Is.False);
        }

        [Test]
        public void Past_The_Limit_Brass_Flows_Into_The_Holes()
        {
            var fired = At(1.20);

            Assert.That((int)fired.Primer, Is.GreaterThanOrEqualTo((int)PrimerCondition.Cratered));
            Assert.That(fired.Head, Is.EqualTo(CaseHeadCondition.EjectorMark));
            Assert.That(fired.Ruptured, Is.False, "an ejector mark is a warning, not a failure");
        }

        [Test]
        public void Far_Past_The_Limit_The_Case_Lets_Go()
        {
            var fired = At(1.50);

            Assert.That(fired.Ruptured, Is.True);
            Assert.That(fired.NeckSplit, Is.True);
            Assert.That(fired.Describe(), Does.Contain("let go"));
        }

        /// <summary>
        /// THE ORDERING PROPERTY. Sweeping the pressure up must never walk a sign
        /// backwards — brass does not un-flow.
        /// </summary>
        [Test]
        public void Signs_Only_Ever_Get_Worse_As_Pressure_Rises()
        {
            var previous = At(0.20);

            for (double fraction = 0.25; fraction <= 1.70; fraction += 0.01)
            {
                var fired = At(fraction);

                Assert.That((int)fired.Primer, Is.GreaterThanOrEqualTo((int)previous.Primer),
                    $"the primer recovered between {fraction - 0.01:F2} and {fraction:F2}");
                Assert.That((int)fired.Head, Is.GreaterThanOrEqualTo((int)previous.Head),
                    $"the head recovered between {fraction - 0.01:F2} and {fraction:F2}");

                if (previous.NeckSplit) Assert.That(fired.NeckSplit, Is.True, "a split neck healed");
                if (previous.Ruptured) Assert.That(fired.Ruptured, Is.True, "a ruptured case healed");

                previous = fired;
            }
        }

        /// <summary>A stronger case tolerates more before showing the same sign, because
        /// the thresholds are fractions of what THAT case can hold.</summary>
        [Test]
        public void A_Stronger_Case_Takes_More_Before_It_Shows_Anything()
        {
            var weak = Case();
            var strong = weak;
            strong.MaximumPressure = weak.MaximumPressure * 2.0;

            double pressure = weak.MaximumPressure * 1.20;

            var inWeak = FiredCaseReader.Read(pressure, weak);
            var inStrong = FiredCaseReader.Read(pressure, strong);

            Assert.That(inWeak.Head, Is.EqualTo(CaseHeadCondition.EjectorMark));
            Assert.That(inStrong.IsUnremarkable, Is.True, "the same pressure must be harmless in a stronger case");
        }

        /// <summary>The gauge reads what the interior solve actually produced, so a load
        /// the mill pressed too fine leaves marked brass.</summary>
        [Test]
        public void A_Load_Pressed_Too_Fine_Comes_Back_Marked()
        {
            var design = new CartridgeDesign
            {
                CaseId = CartridgeCaseLibrary.NineMillimetre,
                Projectile = ProjectileGeometry.Default9mmFmj,
                Materials = ProjectileMaterials.JacketedLead,
                PropellantId = PropellantLibrary.SingleBase,
                GrainShape = GrainShape.Sphere,
                WebThickness = 2.5e-5,
                DeterrentCoating = 0.3,
                ChargeMass = Units.GrainsToKilograms(5.5),
                SeatingDepth = 0.0030
            };

            var baked = CartridgeBaker.Bake(design, BarrelLibrary.ServicePistol9mm);
            var fired = FiredCaseReader.Read(baked.Interior.PeakPressure, baked.Case);

            Assert.That(fired.IsUnremarkable, Is.False, "an overpressure load must mark the brass");
            Assert.That(fired.Describe(), Is.Not.Empty);
        }

        /// <summary>The description is what a gunsmith says holding the brass. It must
        /// never contain a figure or a prediction.</summary>
        [Test]
        public void The_Description_Carries_No_Numbers_And_No_Predictions()
        {
            for (double fraction = 0.2; fraction <= 1.7; fraction += 0.05)
            {
                string text = At(fraction).Describe().ToLowerInvariant();

                foreach (char c in text)
                    Assert.That(char.IsDigit(c), Is.False, $"'{text}' puts a number on the gauge");

                foreach (string banned in new[] { "pressure", "mpa", "velocity", "too much", "overpressure" })
                    Assert.That(text, Does.Not.Contain(banned), $"'{text}' leaks '{banned}'");
            }
        }

        [Test]
        public void A_Case_With_No_Rating_Reads_Blank_Rather_Than_Throwing()
        {
            var unrated = Case();
            unrated.MaximumPressure = 0.0;

            var fired = FiredCaseReader.Read(200e6, unrated);

            Assert.That(fired.IsUnremarkable, Is.True);
            Assert.That(fired.PressureFraction, Is.Zero);
        }
    }
}
