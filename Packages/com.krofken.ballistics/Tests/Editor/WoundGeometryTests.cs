using System;
using NUnit.Framework;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// The geometry that makes an impact READABLE: the cavity the block holds onto, and
    /// the mushroomed slug the player digs out of it.
    ///
    /// These exist because the range shows no numbers. If the silhouette is wrong, the
    /// player is being lied to with the only channel they have — so what is asserted
    /// here is conservation laws and ordering, never calibrated magnitudes.
    /// </summary>
    public class WoundGeometryTests
    {
        private const double ImpactVelocity = 380.0;

        private static Barrel Pistol => BarrelLibrary.ServicePistol9mm;

        private static CartridgeDesign Baseline() => new CartridgeDesign
        {
            Name = "test 9mm",
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

        private static CartridgeDesign Frangible()
        {
            var d = Baseline();
            d.Projectile.MeplatDiameter = 0.004;
            d.Projectile.CavityDepth = 0.005;
            d.Projectile.CavityMouthDiameter = 0.004;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.SinteredIron,
                JacketMaterialId = MaterialLibrary.Copper
            };
            return d;
        }

        private static TerminalResult Fire(CartridgeDesign design, TargetLayer[] target)
        {
            var baked = CartridgeBaker.Bake(design, Pistol);
            return TerminalBallisticsSolver.Solve(baked.Terminal, target, ImpactVelocity);
        }

        private static TargetMedium Gel => TargetMediumLibrary.Get(TargetMediumLibrary.Gelatin);

        // ==================================================================
        // Wound cavity
        // ==================================================================

        /// <summary>
        /// EXACT. The cavity radius must be the closed-form inverse of the
        /// cavity-expansion relation dE/dx = pi * r^2 * R_t. Feed a bin exactly the
        /// energy a 1 cm cavity costs and a 1 cm radius must come back.
        /// </summary>
        [Test]
        public void Cavity_Radius_Inverts_The_Cavity_Expansion_Equation()
        {
            var medium = Gel;
            const double binWidth = 0.005;
            const double expected = 0.01;

            double binEnergy = Math.PI * expected * expected * medium.StrengthTerm * binWidth;

            var result = new TerminalResult
            {
                EnergyProfile = new[] { binEnergy },
                ProfileBinCount = 1,
                ProfileBinWidth = binWidth,
                PenetrationDepth = binWidth
            };

            var profile = WoundCavity.Build(result, medium);

            Assert.That(profile.Length, Is.GreaterThan(0), "a populated profile must produce a cavity");
            Assert.That(profile[0].OuterRadius, Is.EqualTo(expected).Within(1e-12));
        }

        /// <summary>A medium with no strength term cannot hold a cavity open, and the
        /// equation has no bounded solution. An air gap must degrade quietly.</summary>
        [Test]
        public void Medium_With_No_Strength_Yields_No_Cavity()
        {
            var result = new TerminalResult
            {
                EnergyProfile = new[] { 100.0, 100.0 },
                ProfileBinCount = 2,
                ProfileBinWidth = 0.005,
                PenetrationDepth = 0.01
            };

            var air = TargetMediumLibrary.Get(TargetMediumLibrary.Air);
            Assert.That(WoundCavity.Build(result, air).Length, Is.Zero);
        }

        /// <summary>The channel cannot be narrower than the projectile that cut it.</summary>
        [Test]
        public void Cavity_Never_Narrower_Than_The_Bullet_That_Cut_It()
        {
            var result = Fire(Baseline(), TargetMediumLibrary.BareGelatinBlock());
            const double floor = 0.0045;

            var profile = WoundCavity.Build(result, Gel, floor);

            Assert.That(profile.Length, Is.GreaterThan(0));
            foreach (var p in profile)
                Assert.That(p.OuterRadius, Is.GreaterThanOrEqualTo(floor - 1e-15),
                    $"cavity pinched below the bullet radius at {p.X:F3} m");
        }

        /// <summary>The cavity must be widest where the round actually did its work,
        /// because that agreement is the entire reason the silhouette is readable.</summary>
        [Test]
        public void Cavity_Is_Widest_Where_Energy_Deposition_Peaks()
        {
            var result = Fire(Baseline(), TargetMediumLibrary.BareGelatinBlock());
            var profile = WoundCavity.Build(result, Gel);

            double widestAt = 0.0, widest = -1.0;
            foreach (var p in profile)
                if (p.OuterRadius > widest) { widest = p.OuterRadius; widestAt = p.X; }

            Assert.That(widestAt,
                Is.EqualTo(result.PeakEnergyDepositionDepth).Within(result.ProfileBinWidth),
                "the widest point of the cavity must sit at the peak of the energy profile");
        }

        /// <summary>
        /// THE DESIGN CLAIM. A frangible dumps everything at the entry face; a
        /// full-metal-jacket spreads it down a long tunnel. If those two silhouettes
        /// are not obviously different, the player cannot read a shot by looking at it
        /// and the no-numbers rule collapses.
        /// </summary>
        [Test]
        public void Frangible_Cavity_Is_Front_Loaded_Against_A_Full_Metal_Jacket()
        {
            var gel = TargetMediumLibrary.BareGelatinBlock();

            double frangible = FrontLoadedFraction(Fire(Frangible(), gel));
            double fmj = FrontLoadedFraction(Fire(Baseline(), gel));

            Assert.That(frangible, Is.GreaterThan(fmj + 0.25),
                $"frangible put {frangible:P0} of its cavity in the first 10 cm, " +
                $"FMJ {fmj:P0} — these must not look alike");
        }

        /// <summary>Fraction of total cavity volume lying in the first 10 cm.</summary>
        private static double FrontLoadedFraction(in TerminalResult result)
        {
            var profile = WoundCavity.Build(result, Gel);
            Assert.That(profile.Length, Is.GreaterThan(1), "expected a usable cavity");

            const double front = 0.10;
            double total = 0.0, near = 0.0;

            for (int i = 0; i < profile.Length - 1; i++)
            {
                double h = profile[i + 1].X - profile[i].X;
                if (h <= 0.0) continue;

                double a = profile[i].OuterRadius;
                double b = profile[i + 1].OuterRadius;
                double v = Math.PI / 3.0 * h * (a * a + a * b + b * b);

                total += v;
                if (profile[i + 1].X <= front) near += v;
            }

            return total > 0.0 ? near / total : 0.0;
        }

        // ==================================================================
        // Recovered projectile
        // ==================================================================

        private static ProjectileGeometry HollowPoint()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            g.MeplatDiameter = 0.005;
            g.CavityDepth = 0.006;
            g.CavityMouthDiameter = 0.004;
            return g;
        }

        /// <summary>Builds a result that only says "it expanded this much", so the
        /// geometry is tested independently of the terminal solver's calibration.</summary>
        private static TerminalResult Expanded(in ProjectileGeometry g, double ratio) => new TerminalResult
        {
            MaxExpandedDiameter = g.Calibre * ratio,
            ExpansionRatio = ratio,
            Fragmented = false
        };

        /// <summary>
        /// CONSERVATION. Upset is plastic flow and plastic flow conserves volume, so
        /// the recovered slug must contain exactly as much metal as the one that was
        /// loaded. A bullet that came back lighter than it went out is a bug the player
        /// could catch on the bench scale.
        /// </summary>
        [Test]
        public void Recovered_Bullet_Conserves_Volume()
        {
            var geometry = HollowPoint();
            var profile = RecoveredProjectile.Build(geometry, Expanded(geometry, 1.6));

            Assert.That(profile.Length, Is.GreaterThan(2), "an expanded round must yield a shape");

            double recovered = RecoveredProjectile.SolidVolume(profile, profile.Length);

            // Densities are irrelevant here: MassProperties reports solid volume
            // independently, excluding the empty cavity exactly as the lathe does.
            double original = MassPropertiesSolver.Compute(geometry, 1.0, 1.0, 0.0, 4096).Volume;

            Assert.That(recovered, Is.EqualTo(original).Within(0.002 * original),
                "the mushroom must be built from the metal the nose gave up, no more and no less");
        }

        /// <summary>EXACT. The frustum sum is closed-form for straight segments, so a
        /// cylinder must come out as pi*r^2*h to machine precision.</summary>
        [Test]
        public void SolidVolume_Of_A_Cylinder_Matches_The_Closed_Form()
        {
            const double r = 0.0045, h = 0.015;
            var cylinder = new[]
            {
                new ProfilePoint { X = 0.0, OuterRadius = r },
                new ProfilePoint { X = h,   OuterRadius = r }
            };

            Assert.That(RecoveredProjectile.SolidVolume(cylinder, cylinder.Length),
                Is.EqualTo(Math.PI * r * r * h).Within(1e-18));
        }

        /// <summary>A mushroom is wider and — because the metal has to come from
        /// somewhere — shorter than what was loaded.</summary>
        [Test]
        public void Expanded_Bullet_Comes_Back_Wider_And_Shorter()
        {
            var geometry = HollowPoint();
            var profile = RecoveredProjectile.Build(geometry, Expanded(geometry, 1.6));

            double widest = 0.0;
            foreach (var p in profile) if (p.OuterRadius > widest) widest = p.OuterRadius;

            double length = profile[profile.Length - 1].X;

            Assert.That(widest, Is.GreaterThan(geometry.Radius * 1.5), "it should be visibly fatter");
            Assert.That(length, Is.LessThan(geometry.OverallLength), "and visibly stubbier");
        }

        /// <summary>Expanding further must open wider and shorten further — the
        /// ordering the player reads off two slugs sitting side by side.</summary>
        [Test]
        public void More_Expansion_Means_Wider_And_Shorter()
        {
            var geometry = HollowPoint();

            var mild = RecoveredProjectile.Build(geometry, Expanded(geometry, 1.3));
            var hard = RecoveredProjectile.Build(geometry, Expanded(geometry, 1.8));

            Assert.That(hard[0].OuterRadius, Is.GreaterThan(mild[0].OuterRadius));
            Assert.That(hard[hard.Length - 1].X, Is.LessThan(mild[mild.Length - 1].X));
        }

        /// <summary>A round that never opened comes back looking like it went out.</summary>
        [Test]
        public void Undeformed_Bullet_Comes_Back_Unchanged()
        {
            var geometry = ProjectileGeometry.Default9mmFmj;
            var profile = RecoveredProjectile.Build(geometry, Expanded(geometry, 1.0));

            Assert.That(profile.Length, Is.GreaterThan(1));
            Assert.That(profile[profile.Length - 1].X,
                Is.EqualTo(geometry.OverallLength).Within(1e-9));
        }

        /// <summary>Nothing single survives a frangible, so there is nothing to lathe.
        /// The range shows a tray of pieces instead.</summary>
        [Test]
        public void Fragmented_Round_Leaves_Nothing_To_Recover()
        {
            var geometry = HollowPoint();
            var result = Expanded(geometry, 1.6);
            result.Fragmented = true;

            Assert.That(RecoveredProjectile.Build(geometry, result).Length, Is.Zero);
        }
    }
}
