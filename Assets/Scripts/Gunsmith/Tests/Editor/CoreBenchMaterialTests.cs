using System;
using Gunsmith.Crafting;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The stock rack on the core bench.
    ///
    /// What the projectile is MADE of was hardcoded for as long as the lathe existed,
    /// which meant the bench could shape a bullet but not decide whether it was lead or
    /// tungsten — and that one choice is what separates a round that mushrooms from one
    /// that drives straight through. The comparison the terminal solver makes is impact
    /// stagnation pressure against the nose's yield strength, so stock choice is not a
    /// cosmetic label.
    ///
    /// These tests prove the rack reaches the physics. Directions only.
    /// </summary>
    public class CoreBenchMaterialTests
    {
        private GameObject _host;
        private LatheStation _bench;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Core Bench");
            _bench = _host.AddComponent<LatheStation>();
            _bench.Geometry = ProjectileGeometry.Default9mmFmj;
            _bench.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        // ==================================================================

        /// <summary>Denser stock, same shape, heavier bullet. The scale is the tell.</summary>
        [Test]
        public void Denser_Stock_Comes_Off_The_Scale_Heavier()
        {
            _bench.CoreMaterialId = MaterialLibrary.Polymer;
            _bench.Rebuild();
            double polymer = _bench.MassGrains;

            _bench.CoreMaterialId = MaterialLibrary.Lead;
            _bench.Rebuild();
            double lead = _bench.MassGrains;

            _bench.CoreMaterialId = MaterialLibrary.TungstenHeavyAlloy;
            _bench.Rebuild();
            double tungsten = _bench.MassGrains;

            Assert.That(lead, Is.GreaterThan(polymer), "lead must outweigh polymer");
            Assert.That(tungsten, Is.GreaterThan(lead), "tungsten must outweigh lead");
        }

        /// <summary>
        /// THE COMPARISON THE WHOLE TERMINAL MODEL RESTS ON. A soft core yields to the
        /// stagnation pressure and mushrooms; a hard one does not and keeps driving. No
        /// branch on ammunition type anywhere — just stock choice.
        /// </summary>
        [Test]
        public void Soft_Stock_Mushrooms_Where_Hard_Stock_Drives_Through()
        {
            var design = Baseline();

            _bench.Geometry = HollowPointGeometry();
            _bench.CoreMaterialId = MaterialLibrary.Lead;
            _bench.JacketMaterialId = MaterialLibrary.GildingMetal;
            _bench.Rebuild();
            _bench.ApplyTo(ref design);
            var soft = Fire(design);

            _bench.CoreMaterialId = MaterialLibrary.HardenedSteel;
            _bench.Rebuild();
            _bench.ApplyTo(ref design);
            var hard = Fire(design);

            Assert.That(soft.ExpansionRatio, Is.GreaterThan(hard.ExpansionRatio),
                "a soft core must open wider than a hard one");
            Assert.That(hard.PenetrationDepth, Is.GreaterThan(soft.PenetrationDepth),
                "a hard core must drive deeper than one that opened up");
        }

        /// <summary>Packing the cavity turns a hollow point into a payload round, and
        /// the reactive filler must actually release its energy.</summary>
        [Test]
        public void Packing_The_Cavity_Makes_It_A_Payload_Round()
        {
            var design = Baseline();

            _bench.Geometry = HollowPointGeometry();
            _bench.CoreMaterialId = MaterialLibrary.Lead;
            _bench.CavityFillMaterialId = null;
            _bench.Rebuild();
            Assert.That(_bench.HasPayload, Is.False);

            _bench.ApplyTo(ref design);
            var hollow = Fire(design);

            _bench.CavityFillMaterialId = MaterialLibrary.PhosphorusCompound;
            _bench.Rebuild();
            Assert.That(_bench.HasPayload, Is.True);

            _bench.ApplyTo(ref design);
            var loaded = Fire(design);

            Assert.That(hollow.ReactiveEnergyReleased, Is.Zero, "an empty cavity releases nothing");
            Assert.That(loaded.ReactiveEnergyReleased, Is.GreaterThan(0.0),
                "a phosphorus filler must go off in tissue");
        }

        /// <summary>A thicker jacket resists the core driving it open.</summary>
        [Test]
        public void A_Thicker_Jacket_Resists_Expansion()
        {
            var design = Baseline();

            // A steel jacket adds a lot of mass, and a heavier bullet at a fixed charge
            // raises pressure sharply — the service charge bursts the case outright.
            // Backing the charge off is exactly what a handloader does when they go to a
            // heavier projectile, and it keeps this test about the JACKET.
            design.ChargeMass = Units.GrainsToKilograms(3.8);

            _bench.Geometry = HollowPointGeometry();
            _bench.CoreMaterialId = MaterialLibrary.Lead;
            _bench.JacketMaterialId = MaterialLibrary.MildSteel;

            _bench.Apply(LatheOperation.JacketThickness, _bench.Geometry.Radius - 0.0002);
            _bench.Rebuild();
            _bench.ApplyTo(ref design);
            var thin = Fire(design);

            _bench.Apply(LatheOperation.JacketThickness, _bench.Geometry.Radius - 0.0010);
            _bench.Rebuild();
            _bench.ApplyTo(ref design);
            var thick = Fire(design);

            Assert.That(_bench.JacketThicknessMm, Is.GreaterThan(0.8), "the thick jacket was not cut");
            Assert.That(thick.ExpansionRatio, Is.LessThanOrEqualTo(thin.ExpansionRatio),
                "a thicker jacket must not open wider than a thin one");
        }

        // ------------------------------------------------------------------
        // The rack itself
        // ------------------------------------------------------------------

        [Test]
        public void Stepping_The_Rack_Visits_Every_Stock_And_Comes_Round()
        {
            _bench.CoreMaterialId = LatheStation.StockRack[0];

            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < LatheStation.StockRack.Length; i++)
            {
                seen.Add(_bench.CoreMaterialId);
                _bench.NextCoreMaterial();
            }

            Assert.That(seen.Count, Is.EqualTo(LatheStation.StockRack.Length), "the rack skipped stock");
            Assert.That(_bench.CoreMaterialId, Is.EqualTo(LatheStation.StockRack[0]), "the rack did not come round");
        }

        [Test]
        public void Every_Stock_On_The_Rack_Is_A_Real_Material()
        {
            foreach (string id in LatheStation.StockRack)
                Assert.That(MaterialLibrary.TryGet(id, out _), Is.True, $"unknown stock '{id}'");

            foreach (string id in LatheStation.PayloadRack)
            {
                if (id == null) continue;
                Assert.That(MaterialLibrary.TryGet(id, out _), Is.True, $"unknown filler '{id}'");
            }
        }

        /// <summary>The cavity rack has to include emptying it again, or a player who
        /// packs one can never go back.</summary>
        [Test]
        public void The_Cavity_Can_Always_Be_Emptied_Again()
        {
            _bench.CavityFillMaterialId = MaterialLibrary.Thermite;

            bool emptied = false;
            for (int i = 0; i < LatheStation.PayloadRack.Length + 1; i++)
            {
                _bench.NextCavityFill();
                if (string.IsNullOrEmpty(_bench.CavityFillMaterialId)) { emptied = true; break; }
            }

            Assert.That(emptied, Is.True, "there is no way back to an empty cavity");
        }

        /// <summary>Stock the bench has never heard of still has to look like something,
        /// because the canon says fantasy materials are just a row in the table.</summary>
        [Test]
        public void Unknown_Exotic_Stock_Still_Gets_A_Sensible_Colour()
        {
            MaterialLibrary.Register(new MaterialProperties
            {
                Id = "starmetal_test",
                DisplayName = "Starmetal",
                Density = 90000.0,
                YieldStrength = 4.0e10,
                ElongationAtBreak = 0.02
            });

            _bench.CoreMaterialId = "starmetal_test";
            _bench.Geometry = ProjectileGeometry.Default9mmFmj;
            _bench.Geometry.JacketThickness = 0.0;
            _bench.Rebuild();

            var tint = _bench.StockTint;

            Assert.That(tint.r, Is.InRange(0f, 1f));
            Assert.That(_bench.CoreMaterialName, Is.EqualTo("Starmetal"));
            Assert.That(_bench.MassGrains, Is.GreaterThan(500.0), "an absurdly dense core must weigh absurdly much");
        }

        // ------------------------------------------------------------------

        private static ProjectileGeometry HollowPointGeometry()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            g.MeplatDiameter = 0.005;
            g.CavityDepth = 0.006;
            g.CavityMouthDiameter = 0.004;
            return g;
        }

        private static CartridgeDesign Baseline() => new CartridgeDesign
        {
            Name = "stock test",
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

        private static TerminalResult Fire(in CartridgeDesign design)
        {
            var baked = CartridgeBaker.Bake(design, BarrelLibrary.ServicePistol9mm);
            Assert.That(baked.IsValid, Is.True, string.Join("; ", baked.Issues));

            return TerminalBallisticsSolver.Solve(
                baked.Terminal, TargetMediumLibrary.BareGelatinBlock(), 380.0);
        }
    }
}
