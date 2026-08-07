using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The press, and with it the first time the whole bench joins up: mill a powder,
    /// turn a bullet, weigh a charge, seat it, pull the handle, and there are rounds on
    /// the shelf.
    ///
    /// The tests that matter are that every station actually reaches the finished
    /// cartridge. A press that quietly defaulted a field would mean the player set
    /// something at a tool and the game ignored it, which is worse than not offering the
    /// tool at all.
    /// </summary>
    public class LoadingPressTests
    {
        private GameObject _host;
        private LoadingPress _press;
        private GunsmithGame _game;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Bench");

            _press = _host.AddComponent<LoadingPress>();
            _press.Mill = _host.AddComponent<PropellantMill>();
            _press.CoreBench = _host.AddComponent<LatheStation>();
            _press.Balance = _host.AddComponent<PowderBalance>();
            _press.Die = _host.AddComponent<SeatingStop>();

            _press.CoreBench.Geometry = ProjectileGeometry.Default9mmFmj;
            _press.CoreBench.Rebuild();

            _press.Balance.SettingGrains = 5.5;
            _press.Balance.Trickle(5.5);
            _press.Die.Depth = 0.0030;

            _game = new GunsmithGame();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        /// <summary>Stocks the workshop generously so the tests are about the press and
        /// not about the economy.</summary>
        private void StockUp()
        {
            _game.Inventory.Primers += 500;
            _game.Inventory.AddCases(CartridgeCaseLibrary.NineMillimetre, 500);

            foreach (var material in MaterialLibrary.All)
                _game.Inventory.AddMass(material.Id, 5.0);

            foreach (var propellant in PropellantLibrary.All)
                _game.Inventory.AddMass(propellant.Id, 5.0);
        }

        // ==================================================================

        /// <summary>
        /// THE POINT OF THE STATION. Every tool on the bench has to land in the finished
        /// cartridge, or the player is setting things the game ignores.
        /// </summary>
        [Test]
        public void Every_Station_Reaches_The_Finished_Cartridge()
        {
            _press.Mill.SetShape(GrainShape.SevenPerforated);
            _press.Mill.SetWeb(2.2e-4);
            _press.Mill.SetDeterrent(0.55);

            _press.CoreBench.CoreMaterialId = MaterialLibrary.HardenedSteel;
            _press.CoreBench.CavityFillMaterialId = MaterialLibrary.PhosphorusCompound;
            _press.CoreBench.Apply(LatheOperation.NoseLength, 0.0165);
            _press.CoreBench.Rebuild();

            _press.Balance.Empty();
            _press.Balance.Trickle(4.25);

            _press.Die.SetStop(0.0042);

            var design = _press.Compose();

            Assert.That(design.GrainShape, Is.EqualTo(GrainShape.SevenPerforated), "mill: grain form");
            Assert.That(design.WebThickness, Is.EqualTo(2.2e-4).Within(1e-12), "mill: web");
            Assert.That(design.DeterrentCoating, Is.EqualTo(0.55).Within(1e-12), "mill: deterrent");

            Assert.That(design.Materials.CoreMaterialId, Is.EqualTo(MaterialLibrary.HardenedSteel), "core bench: stock");
            Assert.That(design.Materials.CavityFillMaterialId,
                Is.EqualTo(MaterialLibrary.PhosphorusCompound), "core bench: payload");
            Assert.That(design.Projectile.NoseLength, Is.EqualTo(0.0165).Within(1e-9), "core bench: shape");

            Assert.That(Units.KilogramsToGrains(design.ChargeMass), Is.EqualTo(4.25).Within(1e-9), "balance: charge");
            Assert.That(design.SeatingDepth, Is.EqualTo(0.0042).Within(1e-12), "die: seat");

            Assert.That(design.CaseId, Is.EqualTo(CartridgeCaseLibrary.NineMillimetre), "case");
        }

        /// <summary>The die has to be holding whatever the core bench just turned, or
        /// overall length is computed against a stale bullet.</summary>
        [Test]
        public void The_Die_Seats_The_Bullet_The_Bench_Just_Turned()
        {
            _press.CoreBench.Apply(LatheOperation.NoseLength, 0.0200);
            _press.CoreBench.Rebuild();

            _press.Compose();

            Assert.That(_press.Die.Projectile.NoseLength, Is.EqualTo(0.0200).Within(1e-9));
            Assert.That(_press.OverallLengthMm,
                Is.EqualTo((0.0192 + _press.CoreBench.Geometry.OverallLength - _press.Die.Depth) * 1000.0)
                    .Within(1e-6));
        }

        /// <summary>Pulling the handle turns materials into rounds on the shelf.</summary>
        [Test]
        public void Pulling_The_Handle_Puts_Rounds_On_The_Shelf()
        {
            StockUp();
            _press.BatchSize = 20;

            var result = _press.PressBatch(_game, "brass_nose", "Brass Nose");

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.RoundsProduced, Is.EqualTo(20));
            Assert.That(_game.Workshop.RoundsOf("brass_nose"), Is.EqualTo(20));
        }

        /// <summary>
        /// The bill is the press's one honest number, and it comes from the SIMULATED
        /// bullet — so a longer bullet really does eat more stock. It must also consume
        /// a case and a primer per round.
        /// </summary>
        [Test]
        public void The_Bill_Charges_For_What_The_Bullet_Actually_Weighs()
        {
            StockUp();
            _press.BatchSize = 10;

            _press.CoreBench.Apply(LatheOperation.NoseLength, 0.0090);
            _press.CoreBench.Rebuild();
            var shortDesign = _press.Commit(_game, "short", "Short");
            double shortCore = CoreMassIn(_press.Bill(_game, shortDesign));

            _press.CoreBench.Apply(LatheOperation.NoseLength, 0.0200);
            _press.CoreBench.Rebuild();
            var longDesign = _press.Commit(_game, "long", "Long");
            var longBill = _press.Bill(_game, longDesign);

            Assert.That(CoreMassIn(longBill), Is.GreaterThan(shortCore),
                "a longer bullet must cost more stock");

            int cases = 0, primers = 0;
            foreach (var line in longBill.Lines)
            {
                if (!line.IsCounted) continue;
                if (line.MaterialId == "primer") primers = line.Count;
                else cases = line.Count;
            }

            Assert.That(cases, Is.EqualTo(10), "one case per round");
            Assert.That(primers, Is.EqualTo(10), "one primer per round");
        }

        private static double CoreMassIn(Economy.BillOfMaterials bill)
        {
            foreach (var line in bill.Lines)
                if (!line.IsCounted && line.MaterialId == MaterialLibrary.Lead) return line.Mass;

            return 0.0;
        }

        /// <summary>An empty workshop cannot make rounds, and must say what is missing
        /// rather than silently producing nothing.</summary>
        [Test]
        public void An_Empty_Workshop_Refuses_And_Says_What_Is_Short()
        {
            _press.BatchSize = 5;

            var result = _press.PressBatch(_game, "brass_nose", "Brass Nose");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(_game.Workshop.RoundsOf("brass_nose"), Is.Zero);
        }

        /// <summary>Nothing is consumed by a batch that could not be built. In a game
        /// about scarce materials, a partial consumption would be unforgivable.</summary>
        [Test]
        public void A_Refused_Batch_Consumes_Nothing()
        {
            _game.Inventory.Primers += 3;
            _game.Inventory.AddCases(CartridgeCaseLibrary.NineMillimetre, 100);
            foreach (var material in MaterialLibrary.All) _game.Inventory.AddMass(material.Id, 5.0);
            foreach (var propellant in PropellantLibrary.All) _game.Inventory.AddMass(propellant.Id, 5.0);

            _press.BatchSize = 20;
            int casesBefore = _game.Inventory.CasesOf(CartridgeCaseLibrary.NineMillimetre);
            double leadBefore = _game.Inventory.MassOf(MaterialLibrary.Lead);

            var result = _press.PressBatch(_game, "brass_nose", "Brass Nose");

            Assert.That(result.Success, Is.False, "three primers cannot make twenty rounds");
            Assert.That(_game.Inventory.Primers, Is.EqualTo(3), "primers were consumed by a failed batch");
            Assert.That(_game.Inventory.CasesOf(CartridgeCaseLibrary.NineMillimetre), Is.EqualTo(casesBefore));
            Assert.That(_game.Inventory.MassOf(MaterialLibrary.Lead), Is.EqualTo(leadBefore).Within(1e-12));
        }

        /// <summary>
        /// A pressed batch is a saved design, so it can be duplicated and tweaked. This
        /// is the join between the bench and the highest-value affordance in the game.
        /// </summary>
        [Test]
        public void A_Pressed_Load_Can_Be_Duplicated_And_Tweaked()
        {
            StockUp();
            _press.BatchSize = 10;

            _press.PressBatch(_game, "brass_nose", "Brass Nose");
            var original = _game.Designs.Get("brass_nose");

            var copy = _game.DuplicateDesign(original);

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.Name, Is.EqualTo("Brass Nose Mk2"));
            Assert.That(copy.IsValid, Is.True, "the copy must be ready to load");

            // Change one thing on the copy and press it: the two loads now differ by
            // exactly one variable, which is the whole experiment.
            _press.Balance.Empty();
            _press.Balance.Trickle(4.8);

            var second = _press.PressBatch(_game, copy.Id, copy.Name);

            Assert.That(second.Success, Is.True, second.Message);
            Assert.That(_game.Designs.Get(copy.Id).Design.ChargeMass,
                Is.Not.EqualTo(_game.Designs.Get("brass_nose").Design.ChargeMass).Within(1e-9));
        }
    }
}
