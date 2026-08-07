using System.Linq;
using Gunsmith.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The shop has to survive being SAVED.
    ///
    /// Until the workshop could be written into a scene it only existed while the game
    /// was running, which meant it could not be moved, re-meshed or art-directed at all.
    /// Making it saveable exposed a whole class of bug that a code-built-every-time shop
    /// never had: anything wired with a C# delegate, or built out of runtime-generated
    /// objects, comes back from a prefab silently broken.
    ///
    /// "Silently" is the operative word. A dead fixture still highlights, still shows its
    /// prompt, still nudges when you use it. It just does nothing, and there is no error
    /// to tell you. These tests exist because that failure has no symptom.
    /// </summary>
    public class WorkshopAuthoringTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("TestWorkshop");

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private WorkshopController BuildPersistent()
            => WorkshopBuilder.Build(_root.transform, palette: null, persistent: true);

        // ------------------------------------------------------------------
        // Fixture wiring
        // ------------------------------------------------------------------

        [Test]
        public void Every_Fixture_Declares_Which_Action_It_Performs()
        {
            var shop = BuildPersistent();

            var fixtures = shop.GetComponentsInChildren<Interactable>(true);
            Assert.That(fixtures.Length, Is.GreaterThan(0), "the shop has no fixtures at all");

            foreach (var fixture in fixtures)
            {
                // A station you lean over declares its intent with a StationView rather
                // than a ShopAction — the action is "bring my eye to the work".
                if (fixture.GetComponentInParent<StationView>() != null) continue;

                Assert.That(fixture.Action, Is.Not.EqualTo(ShopAction.None),
                    $"'{fixture.name}' has no serialised action, so it would come back " +
                    "from a prefab inert");
            }
        }

        [Test]
        public void Every_Bench_Station_Can_Be_Leaned_Over()
        {
            // True scale only works because the camera comes to the work. A station with
            // no StationView is a station you can never actually see.
            var shop = BuildPersistent();

            foreach (var name in new[] { "Core bench", "Propellant mill", "Powder balance", "Seating die" })
            {
                var station = shop.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == name);

                Assert.That(station, Is.Not.Null, $"'{name}' is missing from the bench");
                Assert.That(station.GetComponent<StationView>(), Is.Not.Null,
                    $"'{name}' cannot be leaned over, so at true scale it is unreadable");
            }
        }

        [Test]
        public void Nothing_On_The_Bench_Is_Faked_Larger_Than_Life()
        {
            // THE REGRESSION THAT MATTERS. The bench used to inflate cartridges 40x and
            // powder 900x, so a 13 mm round rendered 23 cm long. A 9 mm bullet must
            // measure 9 mm across, whatever else changes.
            var shop = BuildPersistent();

            var lathe = shop.GetComponentInChildren<Gunsmith.Crafting.LatheStation>(true);
            Assert.That(lathe, Is.Not.Null);
            Assert.That(lathe.Rig, Is.Not.Null);

            float rigScale = lathe.Rig.lossyScale.x;
            Assert.That(rigScale, Is.EqualTo(1f).Within(0.001f),
                "the projectile rig is scaled, so the bullet is not life size");

            var bullet = lathe.BulletRenderer;
            if (bullet != null && bullet.bounds.size.sqrMagnitude > 1e-12f)
            {
                float width = bullet.bounds.size.x;
                Assert.That(width, Is.LessThan(0.02f),
                    $"the bullet renders {width * 100f:F1} cm across; a 9 mm round is 0.9 cm");
            }
        }

        [Test]
        public void Binding_Survives_Losing_Every_Delegate()
        {
            // Exactly what deserialisation does: the enum comes back, the delegate does
            // not. If BindFixtures cannot recover from this, a saved shop is dead.
            var shop = BuildPersistent();

            // Lean-in stations are excluded on purpose: their action is "bring my eye
            // to the work", which PlayerInteractor performs from the StationView. They
            // have no delegate to lose.
            var fixtures = shop.GetComponentsInChildren<Interactable>(true)
                .Where(f => f.GetComponentInParent<StationView>() == null)
                .ToArray();

            Assert.That(fixtures.Length, Is.GreaterThan(0), "nothing to test");

            foreach (var fixture in fixtures) fixture.Used = null;
            shop.BindFixtures();

            foreach (var fixture in fixtures)
                Assert.That(fixture.Used, Is.Not.Null,
                    $"'{fixture.name}' was not re-bound and would do nothing when used");
        }

        [Test]
        public void Every_Shop_Action_Is_Represented_In_The_Shop()
        {
            // A new enum entry with no fixture is a feature the player cannot reach.
            var shop = BuildPersistent();
            var present = shop.GetComponentsInChildren<Interactable>(true)
                .Select(f => f.Action)
                .ToHashSet();

            foreach (ShopAction action in System.Enum.GetValues(typeof(ShopAction)))
            {
                if (action == ShopAction.None) continue;
                Assert.That(present, Does.Contain(action),
                    $"nothing in the shop performs {action}");
            }
        }

        [Test]
        public void Fixtures_Are_Reachable_And_Prompt_In_The_Second_Person()
        {
            var shop = BuildPersistent();

            foreach (var fixture in shop.GetComponentsInChildren<Interactable>(true))
            {
                Assert.That(fixture.Reach, Is.GreaterThan(0f), $"'{fixture.name}' cannot be reached");
                Assert.That(fixture.Prompt, Is.Not.Null.And.Not.Empty, $"'{fixture.name}' has no prompt");

                // The prompt names the object and the act, never the system.
                Assert.That(fixture.Prompt.ToLowerInvariant(),
                    Does.Not.Contain("execute").And.Not.Contain("invoke").And.Not.Contain("trigger"),
                    $"'{fixture.name}' prompts like a debug menu");
            }
        }

        // ------------------------------------------------------------------
        // Saveability
        // ------------------------------------------------------------------

        [Test]
        public void A_Persistent_Build_Leaves_Nothing_Unsaveable_But_Transients()
        {
            var shop = BuildPersistent();

            // DontSave means "will not be written". Anything structural carrying it
            // would vanish from a saved scene. The only legitimate cases are the
            // propellant mill's cosmetic grains, which are regenerated whenever the
            // powder changes.
            var unsaveable = shop.GetComponentsInChildren<Transform>(true)
                .Where(t => (t.gameObject.hideFlags & HideFlags.DontSave) != 0)
                .Where(t => t.parent == null || t.parent.name != "Grain tray")
                .Select(t => t.name)
                .ToArray();

            Assert.That(unsaveable, Is.Empty,
                "structural objects marked DontSave: " + string.Join(", ", unsaveable));
        }

        [Test]
        public void A_Disposable_Build_Really_Is_Disposable()
        {
            // The other half of the contract: a preview must never be able to reach the
            // scene file, or it turns into a save prompt blocking every domain reload.
            var shop = WorkshopBuilder.Build(_root.transform, palette: null, persistent: false);

            var saveable = shop.GetComponentsInChildren<Transform>(true)
                .Where(t => (t.gameObject.hideFlags & HideFlags.DontSave) == 0)
                .Select(t => t.name)
                .ToArray();

            Assert.That(saveable, Is.Empty,
                "preview objects that would be serialised: " + string.Join(", ", saveable));
        }

        [Test]
        public void The_Bootstrap_Uses_A_Shop_That_Is_Already_There()
        {
            // The regression that made hand-editing impossible: Shop was a read-only
            // property, so it was never serialised, so it was always null on Awake, so
            // the bootstrap rebuilt over the top of any hand-placed layout.
            var bootstrap = _root.AddComponent<WorkshopBootstrap>();
            bootstrap.SpawnPlayer = false;

            var placed = BuildPersistent();
            bootstrap.Shop = null;   // as if only the hierarchy survived, not the field

            bootstrap.Build();

            Assert.That(bootstrap.Shop, Is.SameAs(placed), "the bootstrap did not adopt the existing shop");
            Assert.That(_root.GetComponentsInChildren<WorkshopController>(true).Length, Is.EqualTo(1),
                "a second shop was built on top of the first");
        }
    }
}
