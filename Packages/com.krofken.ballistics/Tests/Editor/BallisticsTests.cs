using System;
using NUnit.Framework;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// End-to-end behaviour of the three solvers.
    ///
    /// Two kinds of assertion here, and the distinction matters:
    ///
    ///   EXACT -- checked against a closed-form answer (vacuum trajectory) or a
    ///            conservation law. These are tight and must never move.
    ///
    ///   DIRECTIONAL -- checked for the right sign and rough magnitude against known
    ///            real-world figures. These are deliberately loose. The model is
    ///            calibrated, not certified, and a test that pins a calibrated model
    ///            to three decimal places just breaks every time the calibration is
    ///            improved. What must never break is the DIRECTION: more powder is
    ///            faster, a longer nose has less drag, an expanding projectile stops
    ///            sooner.
    /// </summary>
    public class BallisticsTests
    {
        private static Barrel Pistol => BarrelLibrary.ServicePistol9mm;

        /// <summary>Baseline 9mm handload used across these tests.</summary>
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

        // ------------------------------------------------------------------
        // Interior ballistics
        // ------------------------------------------------------------------

        [Test]
        public void Baseline_Load_Produces_Plausible_Service_Ballistics()
        {
            var baked = CartridgeBaker.Bake(Baseline(), Pistol);

            Assert.That(baked.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Success), baked.Interior.Message);

            // Real 115-grain 9mm from a 4 inch barrel: 340-380 m/s, 400-500 J,
            // peaking under the 235 MPa CIP limit.
            Assert.That(baked.MuzzleVelocity, Is.InRange(280.0, 400.0), "muzzle velocity");
            Assert.That(baked.MuzzleEnergy, Is.InRange(300.0, 550.0), "muzzle energy");
            Assert.That(Units.PascalsToMegapascals(baked.Interior.PeakPressure), Is.InRange(120.0, 240.0), "peak pressure");
            Assert.That(Units.KilogramsToGrains(baked.Mass.Mass), Is.InRange(105.0, 130.0), "projectile mass");
        }

        [Test]
        public void Thermodynamic_Efficiency_Stays_In_The_Real_Band()
        {
            // Real small arms convert 20-35% of the propellant's chemical energy into
            // projectile kinetic energy. Anything near 50% means heat loss has gone
            // missing from the energy balance.
            var baked = CartridgeBaker.Bake(Baseline(), Pistol);
            Assert.That(baked.Interior.ThermodynamicEfficiency, Is.InRange(0.15, 0.40));
        }

        [Test]
        public void More_Powder_Raises_Velocity_And_Pressure_Monotonically()
        {
            double previousVelocity = 0.0;
            double previousPressure = 0.0;

            for (double grains = 3.0; grains <= 7.0; grains += 0.5)
            {
                var design = Baseline();
                design.ChargeMass = Units.GrainsToKilograms(grains);
                var baked = CartridgeBaker.Bake(design, Pistol);

                Assert.That(baked.MuzzleVelocity, Is.GreaterThan(previousVelocity), $"velocity at {grains} gr");
                Assert.That(baked.Interior.PeakPressure, Is.GreaterThan(previousPressure), $"pressure at {grains} gr");

                previousVelocity = baked.MuzzleVelocity;
                previousPressure = baked.Interior.PeakPressure;
            }
        }

        [Test]
        public void Pressure_Rises_Faster_Than_Velocity()
        {
            // The reason overcharging is dangerous rather than merely energetic:
            // the burn rate feeds back on pressure, so pressure climbs much faster
            // than the velocity it buys.
            var light = Baseline();
            light.ChargeMass = Units.GrainsToKilograms(4.0);
            var heavy = Baseline();
            heavy.ChargeMass = Units.GrainsToKilograms(7.0);

            var a = CartridgeBaker.Bake(light, Pistol);
            var b = CartridgeBaker.Bake(heavy, Pistol);

            double velocityRatio = b.MuzzleVelocity / a.MuzzleVelocity;
            double pressureRatio = b.Interior.PeakPressure / a.Interior.PeakPressure;

            Assert.That(pressureRatio, Is.GreaterThan(velocityRatio * 1.5));
        }

        [Test]
        public void Overcharging_Is_Detected_As_Overpressure()
        {
            var design = Baseline();
            design.ChargeMass = Units.GrainsToKilograms(9.0);

            var baked = CartridgeBaker.Bake(design, Pistol);

            Assert.That(baked.IsValid, Is.False, "an overcharge must not validate");
            Assert.That(baked.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Overpressure));
        }

        [Test]
        public void A_Charge_That_Does_Not_Fit_Is_Rejected()
        {
            var design = Baseline();
            design.GrainShape = GrainShape.Flake;              // bulky, packs badly
            design.ChargeMass = Units.GrainsToKilograms(40.0); // far beyond case volume

            var baked = CartridgeBaker.Bake(design, Pistol);
            Assert.That(baked.IsValid, Is.False);
        }

        [Test]
        public void A_Finer_Web_Burns_Faster_And_Peaks_Higher()
        {
            var coarse = Baseline();
            coarse.WebThickness = 8.0e-5;
            var fine = Baseline();
            fine.WebThickness = 2.0e-5;

            var a = CartridgeBaker.Bake(coarse, Pistol);
            var b = CartridgeBaker.Bake(fine, Pistol);

            Assert.That(b.Interior.PeakPressure, Is.GreaterThan(a.Interior.PeakPressure));
            Assert.That(b.Interior.BurntFractionAtMuzzle, Is.GreaterThan(a.Interior.BurntFractionAtMuzzle));
        }

        [Test]
        public void A_Powder_Far_Too_Slow_Produces_A_Squib()
        {
            var design = Baseline();
            design.WebThickness = 5.0e-3;   // absurdly coarse: it cannot burn in time
            design.ChargeMass = Units.GrainsToKilograms(1.0);

            var baked = CartridgeBaker.Bake(design, Pistol);
            Assert.That(baked.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Squib));
        }

        [Test]
        public void Seating_Deeper_Raises_Pressure()
        {
            // Less room for the powder to burn in. A real and frequently fatal trap.
            var shallow = Baseline();
            shallow.SeatingDepth = 0.002;
            var deep = Baseline();
            deep.SeatingDepth = 0.006;

            var a = CartridgeBaker.Bake(shallow, Pistol);
            var b = CartridgeBaker.Bake(deep, Pistol);

            Assert.That(b.Interior.PeakPressure, Is.GreaterThan(a.Interior.PeakPressure));
        }

        // ------------------------------------------------------------------
        // Exterior ballistics
        // ------------------------------------------------------------------

        [Test]
        public void Vacuum_Trajectory_Matches_The_Closed_Form()
        {
            // The one place with an exact answer: range = v^2 * sin(2*theta) / g.
            // If RK4 is implemented correctly this is accurate to many digits.
            var baked = CartridgeBaker.Bake(Baseline(), Pistol);
            var vacuum = new TrajectoryOptions { Gravity = true, Drag = false };

            const double speed = 141.4213562373095; // 100*sqrt(2), giving 100 m/s per axis
            var state = new ProjectileState
            {
                Position = Vec3.Zero,
                Velocity = new Vec3(100.0, 0.0, 100.0)
            };

            var samples = new TrajectorySample[8192];
            TrajectorySolver.Simulate(state, baked.Aerodynamics, Atmosphere.Standard, vacuum,
                samples, out int count, 0.01, 100000, 120, 0.0);

            double analytic = speed * speed / PhysicalConstants.StandardGravity; // sin(90) = 1
            double actual = samples[count - 1].Position.X;

            Assert.That(actual, Is.EqualTo(analytic).Within(analytic * 1e-6));
        }

        [Test]
        public void Drag_Shortens_The_Trajectory()
        {
            var baked = CartridgeBaker.Bake(Baseline(), Pistol);
            var samples = new TrajectorySample[8192];

            var state = new ProjectileState { Velocity = new Vec3(250.0, 0.0, 250.0) };

            TrajectorySolver.Simulate(state, baked.Aerodynamics, Atmosphere.Standard,
                new TrajectoryOptions { Gravity = true, Drag = false },
                samples, out int vacuumCount, 0.01, 100000, 120, 0.0);
            double vacuumRange = samples[vacuumCount - 1].Position.X;

            TrajectorySolver.Simulate(state, baked.Aerodynamics, Atmosphere.Standard,
                TrajectoryOptions.Default,
                samples, out int dragCount, 0.01, 100000, 120, 0.0);
            double dragRange = samples[dragCount - 1].Position.X;

            Assert.That(dragRange, Is.LessThan(vacuumRange * 0.6));
        }

        [Test]
        public void Crosswind_Deflects_The_Projectile()
        {
            var baked = CartridgeBaker.Bake(Baseline(), Pistol);
            var windy = Atmosphere.Create(288.15, 101325, 0.0, new Vec3(0.0, 10.0, 0.0));

            var state = baked.CreateMuzzleState(0.0);
            var samples = new TrajectorySample[8192];

            TrajectorySolver.Simulate(state, baked.Aerodynamics, windy, TrajectoryOptions.Default,
                samples, out int count, 0.01, 100.0, 2.0, double.NegativeInfinity);

            // Wind blowing towards +Y must push the projectile towards +Y.
            Assert.That(samples[count - 1].Position.Y, Is.GreaterThan(0.01));
        }

        [Test]
        public void A_Longer_Nose_Lowers_Supersonic_Drag()
        {
            var blunt = ProjectileGeometry.Default9mmFmj;
            blunt.NoseLength = blunt.Calibre * 1.0;
            var sharp = ProjectileGeometry.Default9mmFmj;
            sharp.NoseLength = sharp.Calibre * 3.0;

            double bluntCd = CdAtMach(blunt, 2.0);
            double sharpCd = CdAtMach(sharp, 2.0);

            Assert.That(sharpCd, Is.LessThan(bluntCd));
        }

        [Test]
        public void A_Wide_Meplat_Raises_Supersonic_Drag_On_A_Slender_Nose()
        {
            // Deliberately tested on a SLENDER projectile, not the stubby 9mm.
            //
            // The meplat penalty is a slender-nose effect. On a 2.5-calibre ogive the
            // surface near the tip lies almost along the flow, so replacing it with a
            // flat face square-on to the stream is enormously expensive. On the 9mm's
            // 0.6-calibre nose the "point" is already a 38-degree cone, which is
            // barely less blunt than the flat that would replace it -- so truncating
            // it there changes very little, and can even help. That is a real result
            // of the geometry, not a modelling artefact, and it is why this test uses
            // a rifle-like shape.
            var pointed = Slender();
            pointed.MeplatDiameter = pointed.Calibre * 0.05;
            var flat = Slender();
            flat.MeplatDiameter = flat.Calibre * 0.30;

            Assert.That(CdAtMach(flat, 2.0), Is.GreaterThan(CdAtMach(pointed, 2.0) * 1.3));
        }

        /// <summary>A conventional long-range rifle projectile: 2.5 calibre ogive,
        /// 9 degree boattail. Close to the G7 standard shape.</summary>
        private static ProjectileGeometry Slender() => new ProjectileGeometry
        {
            Calibre = 0.00782,
            NoseLength = 0.00782 * 2.5,
            OgiveShapeParameter = 1.0,
            MeplatDiameter = 0.00782 * 0.08,
            BearingSurfaceLength = 0.00782 * 2.9,
            BoattailLength = 0.00782 * 0.6,
            BoattailAngle = Units.DegreesToRadians(9.0),
            JacketThickness = 0.0006
        };

        [Test]
        public void A_Boattail_Lowers_Drag()
        {
            var flatBase = ProjectileGeometry.Default9mmFmj;
            var boattail = flatBase;
            boattail.BoattailLength = flatBase.Calibre * 0.6;
            boattail.BoattailAngle = Units.DegreesToRadians(9.0);

            Assert.That(CdAtMach(boattail, 0.8), Is.LessThan(CdAtMach(flatBase, 0.8)));
            Assert.That(CdAtMach(boattail, 2.0), Is.LessThan(CdAtMach(flatBase, 2.0)));
        }

        [Test]
        public void Drag_Rises_Sharply_Through_The_Transonic_Region()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            double subsonic = CdAtMach(g, 0.8);
            double transonic = CdAtMach(g, 1.2);

            Assert.That(transonic, Is.GreaterThan(subsonic * 1.8));
        }

        [Test]
        public void Baked_Drag_Table_Reproduces_The_Model()
        {
            var shape = AerodynamicShape.FromGeometry(ProjectileGeometry.Default9mmFmj);
            var model = new GeometricDragModel(shape, Atmosphere.Standard, 400.0);
            var table = DragTable.Bake(model, 0.0, 5.0, 200);

            // Sampling exactly on a table node must be exact; between nodes the linear
            // interpolation error must stay small.
            for (double mach = 0.1; mach < 4.9; mach += 0.137)
            {
                double direct = model.DragCoefficient(mach);
                double interpolated = table.Evaluate(mach);
                Assert.That(interpolated, Is.EqualTo(direct).Within(Math.Max(0.01, direct * 0.06)),
                    $"table mismatch at Mach {mach:F3}");
            }
        }

        [Test]
        public void Drag_Table_Clamps_Rather_Than_Extrapolating()
        {
            var shape = AerodynamicShape.FromGeometry(ProjectileGeometry.Default9mmFmj);
            var table = DragTable.Bake(new GeometricDragModel(shape, Atmosphere.Standard), 0.0, 5.0, 200);

            Assert.That(table.Evaluate(-1.0), Is.EqualTo(table.Evaluate(0.0)).Within(1e-12));
            Assert.That(table.Evaluate(99.0), Is.EqualTo(table.Evaluate(5.0)).Within(1e-12));
        }

        private static double CdAtMach(in ProjectileGeometry geometry, double mach)
        {
            var shape = AerodynamicShape.FromGeometry(geometry);
            return new GeometricDragModel(shape, Atmosphere.Standard, 500.0).DragCoefficient(mach);
        }

        // ------------------------------------------------------------------
        // Terminal ballistics -- the four archetypes
        //
        // These are the tests that matter most to the game. They assert that four
        // completely different terminal behaviours emerge from material properties
        // and geometry alone, with no ammunition "type" anywhere in the solver.
        // ------------------------------------------------------------------

        private const double ImpactVelocity = 305.0;

        private static TerminalResult Fire(CartridgeDesign design, TargetLayer[] target)
        {
            var baked = CartridgeBaker.Bake(design, Pistol);
            Assert.That(baked.IsValid, Is.True, $"design did not validate: {Describe(baked)}");
            return TerminalBallisticsSolver.Solve(baked.Terminal, target, ImpactVelocity);
        }

        private static string Describe(BakedCartridge baked)
        {
            var text = new System.Text.StringBuilder();
            foreach (var issue in baked.Issues) text.Append(issue).Append("; ");
            return text.ToString();
        }

        private static CartridgeDesign HollowPoint()
        {
            var d = Baseline();
            d.Projectile.MeplatDiameter = 0.0035;
            d.Projectile.CavityDepth = 0.005;
            d.Projectile.CavityMouthDiameter = 0.0035;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.Copper
            };
            return d;
        }

        private static CartridgeDesign ArmourPiercing()
        {
            var d = Baseline();
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.HardenedSteel,
                JacketMaterialId = MaterialLibrary.GildingMetal
            };
            return d;
        }

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

        [Test]
        public void FullMetalJacket_Stays_Intact_And_Drives_Deep()
        {
            var result = Fire(Baseline(), TargetMediumLibrary.BareGelatinBlock());

            Assert.That(result.Fragmented, Is.False, "a jacketed lead round must not fragment");
            Assert.That(result.ExpansionRatio, Is.LessThan(1.15), "a closed nose barely deforms");
            Assert.That(result.PenetrationDepth, Is.GreaterThan(0.40), "non-expanding handgun rounds run deep");
        }

        [Test]
        public void HollowPoint_Expands_And_Stops_Inside_The_Target()
        {
            var result = Fire(HollowPoint(), TargetMediumLibrary.BareGelatinBlock());

            Assert.That(result.Fragmented, Is.False, "ductile lead must mushroom, not shatter");
            Assert.That(result.ExpansionRatio, Is.InRange(1.25, 2.0), "real hollow points open 1.3-1.8x");
            Assert.That(result.PenetrationDepth, Is.InRange(0.15, 0.40), "must stop inside a person-sized target");
            Assert.That(result.Perforated, Is.False, "the whole point: no exit wound");
        }

        [Test]
        public void HollowPoint_Penetrates_Less_Than_FullMetalJacket()
        {
            var fmj = Fire(Baseline(), TargetMediumLibrary.BareGelatinBlock());
            var hp = Fire(HollowPoint(), TargetMediumLibrary.BareGelatinBlock());

            Assert.That(hp.PenetrationDepth, Is.LessThan(fmj.PenetrationDepth * 0.7));
        }

        [Test]
        public void HollowPoint_Fails_To_Expand_Through_Heavy_Clothing()
        {
            // The classic real failure: fabric packs the cavity, the round cannot
            // open, and it behaves like a full-metal-jacket. A customer who asks for
            // a round that will not over-penetrate through a winter coat is asking
            // the player to solve exactly this.
            var bare = Fire(HollowPoint(), TargetMediumLibrary.BareGelatinBlock());
            var clothed = Fire(HollowPoint(), TargetMediumLibrary.ClothedGelatinBlock());

            Assert.That(clothed.CavityPlugged, Is.True);
            Assert.That(clothed.ExpansionRatio, Is.LessThan(bare.ExpansionRatio * 0.85));
            Assert.That(clothed.PenetrationDepth, Is.GreaterThan(bare.PenetrationDepth * 1.5));
        }

        [Test]
        public void ArmourPiercing_Core_Never_Deforms_In_Soft_Tissue()
        {
            var result = Fire(ArmourPiercing(), TargetMediumLibrary.BareGelatinBlock());

            // Hardened steel yields near 1500 MPa; gelatin at 305 m/s applies about
            // 48 MPa. It is not remotely close, and that is the entire mechanism.
            Assert.That(result.ExpansionRatio, Is.EqualTo(1.0).Within(0.01));
            Assert.That(result.Fragmented, Is.False);
        }

        [Test]
        public void ArmourPiercing_Defeats_Steel_Plate_That_Stops_FullMetalJacket()
        {
            var plate = new[]
            {
                TargetLayer.Of(TargetMediumLibrary.Get(TargetMediumLibrary.MildSteelPlate), 0.003),
                TargetLayer.Of(TargetMediumLibrary.Get(TargetMediumLibrary.Gelatin), 0.40)
            };

            var fmj = Fire(Baseline(), plate);
            var ap = Fire(ArmourPiercing(), plate);

            Assert.That(fmj.PenetrationDepth, Is.LessThan(0.01), "a soft round should not get through the plate");
            Assert.That(ap.PenetrationDepth, Is.GreaterThan(0.05), "a hard core should get through and keep going");
        }

        [Test]
        public void Frangible_Core_Shatters_On_Impact()
        {
            var result = Fire(Frangible(), TargetMediumLibrary.BareGelatinBlock());

            Assert.That(result.Fragmented, Is.True, "a brittle core must break up");
            Assert.That(result.FragmentationDepth, Is.LessThan(0.05), "it should come apart almost immediately");
            Assert.That(result.PenetrationDepth, Is.LessThan(0.15), "a fragment cloud stops fast");
            Assert.That(result.Perforated, Is.False);
        }

        [Test]
        public void Frangible_Dumps_Its_Energy_Far_Faster_Than_FullMetalJacket()
        {
            var fmj = Fire(Baseline(), TargetMediumLibrary.BareGelatinBlock());
            var frangible = Fire(Frangible(), TargetMediumLibrary.BareGelatinBlock());

            Assert.That(frangible.PeakEnergyDepositionRate, Is.GreaterThan(fmj.PeakEnergyDepositionRate * 3.0));
        }

        [Test]
        public void Incendiary_Payload_Releases_Chemical_Energy_Beyond_The_Kinetic_Energy()
        {
            var design = Baseline();
            design.Projectile.MeplatDiameter = 0.003;
            design.Projectile.CavityDepth = 0.006;
            design.Projectile.CavityMouthDiameter = 0.003;
            design.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal,
                CavityFillMaterialId = MaterialLibrary.PhosphorusCompound
            };

            var result = Fire(design, TargetMediumLibrary.BareGelatinBlock());

            Assert.That(result.ReactiveEnergyReleased, Is.GreaterThan(0.0));
            Assert.That(result.EnergyDeposited, Is.GreaterThan(result.ImpactEnergy * 1.5),
                "chemical energy must add to, not replace, the kinetic energy");
        }

        [Test]
        public void Thermite_Does_Not_Initiate_In_Soft_Tissue_But_Phosphorus_Does()
        {
            // Initiation thresholds are real material properties, so a payload that
            // needs a hard hit stays inert in gelatin. This is a design constraint the
            // player has to discover, not a rule written into the solver.
            var basis = Baseline();
            basis.Projectile.MeplatDiameter = 0.003;
            basis.Projectile.CavityDepth = 0.006;
            basis.Projectile.CavityMouthDiameter = 0.003;

            var thermite = basis;
            thermite.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal,
                CavityFillMaterialId = MaterialLibrary.Thermite
            };

            var phosphorus = basis;
            phosphorus.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal,
                CavityFillMaterialId = MaterialLibrary.PhosphorusCompound
            };

            var gel = TargetMediumLibrary.BareGelatinBlock();

            Assert.That(Fire(thermite, gel).ReactiveEnergyReleased, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(Fire(phosphorus, gel).ReactiveEnergyReleased, Is.GreaterThan(0.0));
        }

        [Test]
        public void Energy_Deposited_Never_Exceeds_What_Went_In()
        {
            // Conservation check on an inert projectile: the target cannot absorb more
            // than the projectile's kinetic energy.
            var result = Fire(Baseline(), TargetMediumLibrary.BareGelatinBlock());

            Assert.That(result.EnergyDeposited + result.ExitEnergy,
                Is.LessThanOrEqualTo(result.ImpactEnergy * 1.001));
        }

        [Test]
        public void Energy_Profile_Sums_To_The_Energy_Deposited()
        {
            var result = Fire(HollowPoint(), TargetMediumLibrary.BareGelatinBlock());

            double sum = 0.0;
            for (int i = 0; i < result.EnergyProfile.Length; i++) sum += result.EnergyProfile[i];

            Assert.That(sum, Is.EqualTo(result.EnergyDeposited).Within(result.EnergyDeposited * 0.01));
        }

        [Test]
        public void A_Faster_Impact_Penetrates_Deeper_But_Not_Proportionally()
        {
            // The Poncelet inertial term goes as v^2, so doubling impact velocity
            // does NOT double penetration. Players expect it to; the physics says no.
            var baked = CartridgeBaker.Bake(Baseline(), Pistol);
            var gel = TargetMediumLibrary.BareGelatinBlock(2.0);

            var slow = TerminalBallisticsSolver.Solve(baked.Terminal, gel, 200.0);
            var fast = TerminalBallisticsSolver.Solve(baked.Terminal, gel, 400.0);

            Assert.That(fast.PenetrationDepth, Is.GreaterThan(slow.PenetrationDepth));
            Assert.That(fast.PenetrationDepth, Is.LessThan(slow.PenetrationDepth * 2.0));
        }
    }
}
