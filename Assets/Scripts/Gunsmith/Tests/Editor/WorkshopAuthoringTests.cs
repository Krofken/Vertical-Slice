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
                Assert.That(fixture.Action, Is.Not.EqualTo(ShopAction.None),
                    $"'{fixture.name}' has no serialised action, so it would come back " +
                    "from a prefab inert");
        }

        [Test]
        public void Binding_Survives_Losing_Every_Delegate()
        {
            // Exactly what deserialisation does: the enum comes back, the delegate does
            // not. If BindFixtures cannot recover from this, a saved shop is dead.
            var shop = BuildPersistent();
            var fixtures = shop.GetComponentsInChildren<Interactable>(true);

            foreach (var fixture in fixtures) fixture.Used = null;
            Assert.That(fixtures.All(f => f.Used == null), Is.True, "setup failed to clear");

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
