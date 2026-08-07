using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Gunsmith.Range;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// Bench to range: the whole loop in one place.
    ///
    /// Mill a powder, turn a bullet, weigh a charge, seat it, press a batch, walk out to
    /// the yard, fire one, and there is a block on the rack with a cavity in it. Until
    /// this joined up the game was a set of stations; this is the test that says it is a
    /// loop.
    /// </summary>
    public class BenchToRangeTests
    {
        private GameObject _host;
        private LoadingPress _press;
        private RangeStation _yard;
        private EvidenceRack _rack;
        private GunsmithGame _game;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Workshop");

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

            var rackObject = new GameObject("Rack");
            rackObject.transform.SetParent(_host.transform, false);
            _rack = rackObject.AddComponent<EvidenceRack>();

            _yard = _host.AddComponent<RangeStation>();
            _yard.Rack = _rack;

            _game = new GunsmithGame();
            _game.Inventory.Primers += 500;
            _game.Inventory.AddCases(CartridgeCaseLibrary.NineMillimetre, 500);
            foreach (var material in MaterialLibrary.All) _game.Inventory.AddMass(material.Id, 5.0);
            foreach (var propellant in PropellantLibrary.All) _game.Inventory.AddMass(propellant.Id, 5.0);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ==================================================================

        /// <summary>THE LOOP.</summary>
        [Test]
        public void Press_A_Batch_Fire_One_And_Get_A_Block_On_The_Rack()
        {
            _press.BatchSize = 10;

            var crafted = _press.PressBatch(_game, "brass_nose", "Brass Nose");
            Assert.That(crafted.Success, Is.True, crafted.Message);

            Assert.That(_yard.TryFire(_game, "brass_nose", out var entry, out string why), Is.True, why);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Measurement.PenetrationDepth, Is.GreaterThan(0.0), "the round went nowhere");
            Assert.That(_rack.Blocks.Count, Is.EqualTo(1), "no block was racked");
            Assert.That(_game.Notebook.Count, Is.EqualTo(1), "the shot was not written up");
        }

        /// <summary>Testing costs ammunition. That is what makes a shot a real choice
        /// against the finite night rather than a free lookup.</summary>
        [Test]
        public void Firing_Spends_A_Round_From_The_Shelf()
        {
            _press.BatchSize = 5;
            _press.PressBatch(_game, "brass_nose", "Brass Nose");

            Assert.That(_game.Workshop.RoundsOf("brass_nose"), Is.EqualTo(5));

            _yard.TryFire(_game, "brass_nose", out _, out _);

            Assert.That(_game.Workshop.RoundsOf("brass_nose"), Is.EqualTo(4), "the shot was free");
        }

        [Test]
        public void Firing_With_An_Empty_Shelf_Fails_And_Racks_Nothing()
        {
            _press.BatchSize = 1;
            _press.PressBatch(_game, "brass_nose", "Brass Nose");

            Assert.That(_yard.TryFire(_game, "brass_nose", out _, out _), Is.True);
            Assert.That(_yard.TryFire(_game, "brass_nose", out _, out string why), Is.False, "fired ammunition it did not have");

            Assert.That(why, Is.Not.Null.And.Not.Empty);
            Assert.That(_rack.Blocks.Count, Is.EqualTo(1), "a failed shot racked a block");
        }

        /// <summary>
        /// EVIDENCE PERSISTS. Every block stays, in the order it was shot, so the player
        /// compares by walking down the rack rather than by remembering.
        /// </summary>
        [Test]
        public void Blocks_Accumulate_On_The_Rack_In_The_Order_They_Were_Shot()
        {
            _press.BatchSize = 6;
            _press.PressBatch(_game, "brass_nose", "Brass Nose");

            for (int i = 0; i < 3; i++)
                Assert.That(_yard.TryFire(_game, "brass_nose", out _, out string why), Is.True, why);

            Assert.That(_rack.Blocks.Count, Is.EqualTo(3), "the rack forgot a shot");

            // Later blocks stand further along the shelf, so the row reads left to right.
            float first = _rack.Blocks[0].transform.localPosition.x;
            float third = _rack.Blocks[2].transform.localPosition.x;
            Assert.That(third, Is.GreaterThan(first), "the rack is not laid out in shot order");
        }

        /// <summary>
        /// The payoff of duplicate-and-tweak: two loads differing in one variable put
        /// two blocks side by side that differ because of that variable. A hollow point
        /// must not leave the same cavity as a jacketed solid.
        /// </summary>
        [Test]
        public void Two_Loads_Differing_In_One_Thing_Leave_Different_Blocks()
        {
            _press.BatchSize = 2;
            _press.PressBatch(_game, "solid", "Solid");
            _yard.TryFire(_game, "solid", out var solidShot, out string whyA);
            Assert.That(solidShot, Is.Not.Null, whyA);

            // One change: open the nose into a cavity.
            _press.CoreBench.Apply(LatheOperation.MeplatDiameter, 0.0025);
            _press.CoreBench.Apply(LatheOperation.CavityMouth, 0.0020);
            _press.CoreBench.Apply(LatheOperation.CavityDepth, 0.0060);
            _press.CoreBench.Rebuild();

            _press.PressBatch(_game, "hollow", "Hollow");
            _yard.TryFire(_game, "hollow", out var hollowShot, out string whyB);
            Assert.That(hollowShot, Is.Not.Null, whyB);

            Assert.That(hollowShot.Measurement.ExpansionRatio,
                Is.GreaterThan(solidShot.Measurement.ExpansionRatio),
                "opening the nose did not change what the round did");

            Assert.That(_rack.Blocks.Count, Is.EqualTo(2), "both blocks must stand on the rack");
        }

        /// <summary>
        /// A load that will wreck the gun still gets fired. Refusing at the yard would
        /// tell the player the answer, which is exactly what the range exists to make
        /// them earn.
        /// </summary>
        [Test]
        public void A_Dangerous_Load_Is_Still_Fired()
        {
            _press.Mill.SetWeb(2.5e-5);
            _press.BatchSize = 2;

            var crafted = _press.PressBatch(_game, "hot", "Hot");
            Assert.That(crafted.Success, Is.True, "an overpressure load must still assemble");

            var saved = _game.Designs.Get("hot");
            Assert.That(saved.IsValid, Is.False, "this load was supposed to be dangerous");

            Assert.That(_yard.TryFire(_game, "hot", out var entry, out string why), Is.True,
                $"the yard refused to fire a dangerous round: {why}");

            Assert.That(entry, Is.Not.Null);
            Assert.That(_rack.Blocks.Count, Is.EqualTo(1));
        }
    }
}
