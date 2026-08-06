using System;
using NUnit.Framework;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// Units, vector maths and the atmosphere model.
    ///
    /// These look trivial and are not. A unit conversion that is silently wrong by a
    /// factor of 7000 produces a simulation that runs perfectly and lies about
    /// everything, and there is no obvious symptom until someone compares against
    /// real data. Pinning the conversions is cheap insurance.
    /// </summary>
    public class CoreTests
    {
        [Test]
        public void Grain_Is_Exactly_One_SevenThousandth_Of_A_Pound()
        {
            Assert.That(Units.KilogramsPerGrain * 7000.0,
                Is.EqualTo(Units.KilogramsPerPound).Within(1e-12));
        }

        [Test]
        public void Mass_Conversions_Round_Trip()
        {
            const double grains = 147.0;
            double kg = Units.GrainsToKilograms(grains);
            Assert.That(Units.KilogramsToGrains(kg), Is.EqualTo(grains).Within(1e-9));

            // A 147 grain projectile is 9.52 g. If this ever reads 147 or 0.147,
            // something is treating grains as grams.
            Assert.That(kg * 1000.0, Is.EqualTo(9.526).Within(0.01));
        }

        [Test]
        public void Length_And_Pressure_Conversions_Are_Correct()
        {
            Assert.That(Units.InchesToMetres(1.0), Is.EqualTo(0.0254).Within(1e-12));
            Assert.That(Units.PsiToPascals(1.0), Is.EqualTo(6894.757).Within(0.001));

            // 35,000 psi is a common rifle pressure; it should be about 241 MPa.
            Assert.That(Units.PascalsToMegapascals(Units.PsiToPascals(35000)),
                Is.EqualTo(241.3).Within(0.5));
        }

        [Test]
        public void Vec3_Cross_Product_Is_Right_Handed()
        {
            var x = new Vec3(1, 0, 0);
            var y = new Vec3(0, 1, 0);
            var z = Vec3.Cross(x, y);

            Assert.That(z.X, Is.EqualTo(0).Within(1e-12));
            Assert.That(z.Y, Is.EqualTo(0).Within(1e-12));
            Assert.That(z.Z, Is.EqualTo(1).Within(1e-12));
        }

        [Test]
        public void Vec3_Normalising_A_Zero_Vector_Yields_Zero_Not_NaN()
        {
            // A projectile at rest has no direction of travel. Returning NaN here
            // silently poisons every downstream calculation.
            var n = Vec3.Zero.Normalized();
            Assert.That(n.IsFinite, Is.True);
            Assert.That(n.Magnitude, Is.EqualTo(0).Within(1e-12));
        }

        [Test]
        public void Standard_Atmosphere_Matches_ICAO_Sea_Level()
        {
            var a = Atmosphere.Standard;
            Assert.That(a.Density, Is.EqualTo(1.2250).Within(0.001), "sea level density");
            Assert.That(a.SpeedOfSound, Is.EqualTo(340.29).Within(0.5), "speed of sound");
            Assert.That(a.Temperature, Is.EqualTo(288.15).Within(1e-6));
        }

        [Test]
        public void Humid_Air_Is_Less_Dense_Than_Dry_Air()
        {
            // Water is 18 g/mol against dry air's ~29, so adding vapour at constant
            // pressure LOWERS density. Getting this backwards is a classic error.
            var dry = Atmosphere.Create(293.15, 101325, 0.0, Vec3.Zero);
            var humid = Atmosphere.Create(293.15, 101325, 1.0, Vec3.Zero);

            Assert.That(humid.Density, Is.LessThan(dry.Density));
        }

        [Test]
        public void Density_Falls_With_Altitude()
        {
            var sea = Atmosphere.FromAltitude(0);
            var high = Atmosphere.FromAltitude(2000);

            Assert.That(high.Density, Is.LessThan(sea.Density));
            Assert.That(high.Density, Is.EqualTo(1.007).Within(0.01));
        }
    }
}
