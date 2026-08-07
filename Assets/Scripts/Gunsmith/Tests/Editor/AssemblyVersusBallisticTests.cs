using Gunsmith.GameLoop;
using Krofken.Ballistics;
using NUnit.Framework;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The line between what the bench refuses and what it lets you make.
    ///
    /// A gunsmith holding the components can see that a bullet will not chamber or that
    /// a charge will not physically fit the case. They cannot see peak pressure. So the
    /// bench refuses the first kind and stays silent about the second — a load that will
    /// burst the case gets made, and the player finds out by firing it.
    ///
    /// Warning at the bench would hand over the answer and remove the reason to test,
    /// which is the whole game. These tests are the guard on that.
    /// </summary>
    public class AssemblyVersusBallisticTests
    {
        private GunsmithGame _game;

        [SetUp]
        public void SetUp()
        {
            _game = new GunsmithGame();

            _game.Inventory.Primers += 500;
            _game.Inventory.AddCases(CartridgeCaseLibrary.NineMillimetre, 500);

            foreach (var material in MaterialLibrary.All) _game.Inventory.AddMass(material.Id, 5.0);
            foreach (var propellant in PropellantLibrary.All) _game.Inventory.AddMass(propellant.Id, 5.0);
        }

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

        // ==================================================================

        /// <summary>
        /// THE RULE. A load fine enough to take a 9 mm past its case pressure limit
        /// still assembles, still loads, and still ends up on the shelf. The bench has
        /// no business knowing what it will do.
        /// </summary>
        [Test]
        public void A_Load_That_Will_Burst_The_Case_Can_Still_Be_Made()
        {
            var design = Baseline();
            design.WebThickness = 2.5e-5;

            var saved = _game.SaveDesign("hot", "Hot", design);

            Assert.That(saved.Baked.Interior.Status, Is.EqualTo(InteriorBallisticsStatus.Overpressure),
                "this load was supposed to be dangerous");
            Assert.That(saved.IsValid, Is.False, "it is still an unsafe round");
            Assert.That(saved.Baked.CanAssemble, Is.True, "but the parts go together perfectly well");

            var result = _game.Workshop.Craft(saved, 20);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(_game.Workshop.RoundsOf("hot"), Is.EqualTo(20));
        }

        /// <summary>Nothing the bench says about a dangerous load may hint at what it
        /// will do. The refusal message is the leak to watch.</summary>
        [Test]
        public void The_Bench_Says_Nothing_About_How_A_Round_Will_Shoot()
        {
            var design = Baseline();
            design.WebThickness = 2.5e-5;

            var saved = _game.SaveDesign("hot", "Hot", design);
            var result = _game.Workshop.Craft(saved, 5);

            string message = (result.Message ?? string.Empty).ToLowerInvariant();

            foreach (string banned in new[] { "pressure", "unsafe", "safely", "burst", "rupture", "velocity" })
                Assert.That(message, Does.Not.Contain(banned),
                    $"the loading bench leaked '{banned}' about a round that has not been fired");
        }

        // ------------------------------------------------------------------

        /// <summary>A charge that will not physically fit the case is visible on the
        /// bench, so the bench refuses it and consumes nothing.</summary>
        [Test]
        public void A_Charge_That_Will_Not_Fit_The_Case_Is_Refused()
        {
            var design = Baseline();
            design.ChargeMass = Units.GrainsToKilograms(40.0);

            var saved = _game.SaveDesign("overfull", "Overfull", design);

            Assert.That(saved.Baked.CanAssemble, Is.False, "that much powder cannot go in the case");

            int primersBefore = _game.Inventory.Primers;
            var result = _game.Workshop.Craft(saved, 10);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("does not fit"));
            Assert.That(_game.Inventory.Primers, Is.EqualTo(primersBefore), "a refused batch consumed stock");
        }

        /// <summary>A bullet of the wrong calibre will not chamber, and you can see that
        /// by holding it against the case.</summary>
        [Test]
        public void A_Bullet_That_Will_Not_Chamber_Is_Refused()
        {
            var design = Baseline();
            design.Projectile.Calibre = 0.0142;

            var saved = _game.SaveDesign("wrong_bore", "Wrong bore", design);

            Assert.That(saved.Baked.CanAssemble, Is.False);
            Assert.That(saved.Baked.FirstAssemblyFault, Does.Contain("chamber"));

            Assert.That(_game.Workshop.Craft(saved, 5).Success, Is.False);
        }

        /// <summary>Stock the workshop does not have is an assembly problem too, and it
        /// is reported as the shortage rather than as a fault in the design.</summary>
        [Test]
        public void Missing_Stock_Is_Still_Refused_As_A_Shortage()
        {
            var empty = new GunsmithGame();
            var saved = empty.SaveDesign("fine", "Fine", Baseline());

            Assert.That(saved.Baked.CanAssemble, Is.True, "the design itself is assemblable");

            var result = empty.Workshop.Craft(saved, 10);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Not enough"));
        }

        /// <summary>A squib assembles too — the bullet sticks in the bore, which is a
        /// firing outcome, not a bench one.</summary>
        [Test]
        public void A_Squib_Load_Can_Still_Be_Made()
        {
            var design = Baseline();
            design.ChargeMass = Units.GrainsToKilograms(0.05);

            var saved = _game.SaveDesign("squib", "Squib", design);

            Assert.That(saved.Baked.CanAssemble, Is.True,
                "a feeble charge still goes in the case; it just will not push the bullet out");

            Assert.That(_game.Workshop.Craft(saved, 5).Success, Is.True);
        }

        /// <summary>Sanity: a good load is unaffected by any of this.</summary>
        [Test]
        public void A_Good_Load_Still_Assembles_And_Is_Still_Safe()
        {
            var saved = _game.SaveDesign("good", "Good", Baseline());

            Assert.That(saved.Baked.CanAssemble, Is.True);
            Assert.That(saved.IsValid, Is.True);
            Assert.That(_game.Workshop.Craft(saved, 10).Success, Is.True);
        }
    }
}
