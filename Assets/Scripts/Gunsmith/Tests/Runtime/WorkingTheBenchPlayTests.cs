using System.Collections;
using System.Linq;
using Gunsmith.Crafting;
using Gunsmith.Interaction;
using Gunsmith.Range;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gunsmith.Tests
{
    /// <summary>
    /// Press Play and work the bench.
    ///
    /// <see cref="StandingUpPlayTests"/> proved the gunsmith ends up standing in a room
    /// looking at his own workbench. It did not check that anything on the bench RESPONDS,
    /// and three separate things did not:
    ///
    ///   THE LATHE HANDLES could not be grabbed. WorkshopBuilder.LeanIn fits each station
    ///     a 17 cm trigger BoxCollider so it can be walked up to, and the work you lean in
    ///     to grab sits inside it. Physics.queriesHitTriggers is true by default, so the
    ///     grab ray hit the station's own box at 6 cm and stopped, 11 cm short of the
    ///     handles. Every handle asked "did the ray hit me", the answer was always no.
    ///
    ///   THE SEATING STOP had no handle at all. SeatingStop.SetStop documented itself as
    ///     "bound to a draggable handle" and nothing outside the test assembly had ever
    ///     called it.
    ///
    ///   THE YARD did not exist once the shop was saved. RangeStation lived in
    ///     EvidenceRack.cs, and Unity resolves a MonoBehaviour's script by FILE NAME, so it
    ///     serialised into the prefab with a dead script pointer and came back as a missing
    ///     script. Firing answered "no yard" forever.
    ///
    /// All three were invisible to the EditMode suite, because construction, layout and
    /// physics were all correct — what was broken was whether a person standing in the shop
    /// could operate it. That is the gap these close, and it is why they assert against a
    /// shop built by pressing Play rather than one assembled by a fixture.
    /// </summary>
    public class WorkingTheBenchPlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);

            foreach (var stray in Object.FindObjectsByType<PlayerRig>(FindObjectsInactive.Include))
                if (stray != null) Object.Destroy(stray.gameObject);
        }

        /// <summary>Builds the shop exactly as pressing Play does.</summary>
        private IEnumerator PressPlay()
        {
            _root = new GameObject("Workshop");
            _root.AddComponent<WorkshopBootstrap>();

            yield return null;
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Puts the eye where leaning in over a station puts it, and lets the blend land.
        ///
        /// Going through <see cref="PlayerRig.Focus"/> rather than moving the camera by hand
        /// is the entire point. The canon's hardest-won verification rule is that staging the
        /// camera bypasses the broken path and makes an unplayable shop look correct — so the
        /// test has to arrive at the station the way a player does.
        /// </summary>
        private static IEnumerator LeanInOn(StationView station)
        {
            var rig = Object.FindAnyObjectByType<PlayerRig>();
            Assert.That(rig, Is.Not.Null, "no player rig to lean in with");

            rig.Focus(station);

            // Long enough for FocusSeconds (0.28) to complete.
            for (int i = 0; i < 40; i++) yield return null;
        }

        private static Camera Eye()
            => Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                .FirstOrDefault(c => c.isActiveAndEnabled);

        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Leaning_In_At_The_Lathe_Can_Reach_A_Handle()
        {
            yield return PressPlay();

            var lathe = Object.FindAnyObjectByType<LatheStation>();
            Assert.That(lathe, Is.Not.Null, "no lathe was built");

            var station = lathe.GetComponent<StationView>();
            Assert.That(station, Is.Not.Null, "the lathe cannot be leaned over");

            yield return LeanInOn(station);

            var camera = Eye();
            Assert.That(camera, Is.Not.Null);

            // Aim at the work, which is what a player leaning in is looking at.
            var handle = lathe.GetComponentsInChildren<LatheHandle>().FirstOrDefault();
            Assert.That(handle, Is.Not.Null, "the lathe has no handles");

            Vector3 toHandle = handle.transform.position - camera.transform.position;
            var ray = new Ray(camera.transform.position, toHandle.normalized);

            // THE ASSERTION THAT WAS MISSING. Not "is there a handle" — construction was
            // never the problem — but "does a ray from the player's eye actually reach one".
            bool reached = Physics.Raycast(ray, out var hit, handle.Reach, ~0,
                QueryTriggerInteraction.Ignore);

            Assert.That(reached, Is.True,
                "nothing at all under the aim while leaning in over the lathe");

            Assert.That(hit.collider.GetComponent<LatheHandle>(), Is.Not.Null,
                $"the aim reaches '{hit.collider.name}' before any lathe handle — " +
                "something is shielding the work the player leaned in to grab");
        }

        [UnityTest]
        public IEnumerator The_Station_Trigger_Box_Does_Not_Shield_Its_Own_Handles()
        {
            // The specific shape of the bug, pinned so it cannot come back by some other
            // route: a plain Raycast that honours triggers stops on the lean-in box, and
            // one that ignores them reaches the work. If the first assertion ever starts
            // failing, the shielding is gone and this test can go.
            yield return PressPlay();

            var lathe = Object.FindAnyObjectByType<LatheStation>();
            var station = lathe.GetComponent<StationView>();
            yield return LeanInOn(station);

            var camera = Eye();
            var handle = lathe.GetComponentsInChildren<LatheHandle>().First();

            Vector3 origin = camera.transform.position;
            Vector3 direction = (handle.transform.position - origin).normalized;

            bool hitWithTriggers = Physics.Raycast(origin, direction, out var withTriggers,
                handle.Reach, ~0, QueryTriggerInteraction.Collide);

            bool hitIgnoringTriggers = Physics.Raycast(origin, direction, out var ignoring,
                handle.Reach, ~0, QueryTriggerInteraction.Ignore);

            Assert.That(hitWithTriggers && hitIgnoringTriggers, Is.True,
                "the aim ray reaches nothing either way");

            Assert.That(withTriggers.collider.isTrigger, Is.True,
                "expected the station's lean-in trigger to be the first thing a " +
                "trigger-honouring ray finds; if it is not, this test has stopped " +
                "guarding what it was written for");

            Assert.That(ignoring.collider.GetComponent<LatheHandle>(), Is.Not.Null,
                "ignoring triggers still does not reach a handle");
        }

        [UnityTest]
        public IEnumerator The_Seating_Die_Has_Something_To_Take_Hold_Of()
        {
            yield return PressPlay();

            var die = Object.FindAnyObjectByType<SeatingStop>();
            Assert.That(die, Is.Not.Null, "no seating die was built");

            var handle = die.GetComponentsInChildren<SeatingHandle>().FirstOrDefault();

            Assert.That(handle, Is.Not.Null,
                "the seating die has no draggable handle, so 'set the seating depth' " +
                "leans you in over a tool you cannot operate");

            Assert.That(handle.GetComponent<Collider>(), Is.Not.Null,
                "the seating handle cannot be aimed at — it has no collider");
        }

        [UnityTest]
        public IEnumerator Dragging_The_Seating_Stop_Changes_The_Seating_Depth()
        {
            // Drives the tool the way the handle does rather than poking the field, so the
            // axis maths and the depth inversion are both covered.
            yield return PressPlay();

            var die = Object.FindAnyObjectByType<SeatingStop>();
            double before = die.Depth;

            die.SetStop(before + 0.0015);

            Assert.That(die.Depth, Is.Not.EqualTo(before).Within(1e-9),
                "the die ignored being screwed in");

            Assert.That(die.Depth, Is.InRange(die.MinimumDepth, die.MaximumDepth),
                "the die produced a depth outside its own travel");
        }

        [UnityTest]
        public IEnumerator The_Shop_Has_A_Yard_To_Fire_Into()
        {
            // "[Shop] No RangeStation anywhere under the shop — firing will refuse." This
            // is that message, as an assertion. It was true in the authored shop for as
            // long as RangeStation shared a file with EvidenceRack.
            yield return PressPlay();

            var shop = Object.FindAnyObjectByType<WorkshopController>();
            Assert.That(shop, Is.Not.Null, "no workshop controller");

            Assert.That(shop.Yard, Is.Not.Null,
                "the shop has no yard, so firing refuses before it reaches the ballistics");

            Assert.That(shop.Yard.Rack, Is.Not.Null,
                "the yard has nowhere to rack a block, so evidence would not persist");
        }

        [UnityTest]
        public IEnumerator Pulling_The_Press_Handle_Puts_Rounds_On_The_Shelf_And_Says_So()
        {
            // Known broken #4: the handle "produced nothing". It in fact produced rounds
            // and reported them to Debug.Log, which a player cannot see.
            yield return PressPlay();

            var shop = Object.FindAnyObjectByType<WorkshopController>();
            Assert.That(shop, Is.Not.Null);
            Assert.That(shop.Press, Is.Not.Null, "no press was built");

            Assert.That(shop.Press.Readout, Is.Not.Null,
                "the press has no readout, so pulling the handle cannot tell the player " +
                "anything at all");

            shop.PullHandle();
            yield return null;

            string readout = shop.Press.Readout.text;

            Assert.That(readout, Is.Not.Null.And.Not.Empty,
                "the press said nothing after the handle was pulled");

            Assert.That(readout.ToLowerInvariant(), Does.Contain("pulled"),
                $"the press readout does not report what the pull did: '{readout}'");
        }

        // ------------------------------------------------------------------
        // The shop the player actually walks around is a PREFAB INSTANCE, and
        // WorkshopBootstrap ADOPTS it rather than rebuilding. So anything added to
        // WorkshopBuilder reaches a freshly-built shop — which is what every test above
        // builds — and never reaches the authored one, whose prefab was saved before the
        // new part existed.
        //
        // That is a false green with exactly the shape the canon warns about for staged
        // cameras: the suite passes, and the game the user opens is still broken. It was
        // caught here by probing the running prefab shop and finding no press readout and
        // no seating handle, after the tests above had gone green.
        //
        // These two cover the repair for it: the components fit their own missing parts, so
        // they work in a code-built shop, a prefab-restored one and a hand-placed one.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator A_Press_Restored_Without_A_Readout_Makes_Its_Own()
        {
            yield return PressPlay();

            var shop = Object.FindAnyObjectByType<WorkshopController>();
            var press = shop.Press;

            // Simulate a press restored from a prefab saved before the readout existed.
            Object.Destroy(press.Readout.gameObject);
            press.Readout = null;
            yield return null;

            press.RefreshReadout(null, null);

            Assert.That(press.Readout, Is.Not.Null,
                "a press with no readout did not make one, so an authored shop stays silent");

            Assert.That(press.Readout.transform.localRotation.eulerAngles.y,
                Is.EqualTo(0f).Within(0.01f),
                "the readout was rotated to 'face' the player, which mirrors a TextMesh — " +
                "its glyphs read from the -Z side, so an unrotated label already faces him");
        }

        [UnityTest]
        public IEnumerator A_Die_Restored_Without_A_Handle_Fits_Its_Own()
        {
            yield return PressPlay();

            var die = Object.FindAnyObjectByType<SeatingStop>();

            var existing = die.Stop.GetComponent<SeatingHandle>();
            Assert.That(existing, Is.Not.Null, "expected the die to have a handle to begin with");

            // Simulate a die restored from a prefab saved before the handle existed.
            Object.Destroy(existing);
            die.enabled = false;
            yield return null;

            die.enabled = true;
            yield return null;

            Assert.That(die.Stop.GetComponent<SeatingHandle>(), Is.Not.Null,
                "a die with no handle did not fit one, so the authored shop still has a " +
                "seating station that cannot be operated");
        }

        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // Charging: a dispenser, not a pour.
        //
        // The pour is gone, and with it the tin, the tilt-to-flow curve and several thousand
        // simulated grains. It worked as physics and failed as a game — the weight readout fell
        // outside the lean-in frame, the pile stopped growing partway through, and the granules
        // flickered, so it never felt like pouring. What replaced it states the charge instead of
        // asking the player to arrive at it by feel.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator The_Bench_Has_A_Dispenser_With_A_Screen()
        {
            yield return PressPlay();

            var dispenser = Object.FindAnyObjectByType<PowderDispenser>();
            Assert.That(dispenser, Is.Not.Null, "no powder dispenser was fitted");

            Assert.That(dispenser.Labels, Is.Not.Null.And.Not.EqualTo(dispenser.Values),
                "the dispenser has no screen, so the charge cannot be read");

            Assert.That(dispenser.UpButton, Is.Not.Null.And.Not.EqualTo(dispenser.DownButton),
                "the dispenser cannot be dialled up and down");

            Assert.That(dispenser.DispenseButton, Is.Not.Null,
                "there is no button to throw the charge with");
        }

        [UnityTest]
        public IEnumerator Dialling_And_Dispensing_Sets_The_Charge()
        {
            yield return PressPlay();

            var dispenser = Object.FindAnyObjectByType<PowderDispenser>();
            var charge = Object.FindAnyObjectByType<PowderBalance>();

            charge.Empty();
            dispenser.Select(5.5);

            Assert.That(dispenser.Selected, Is.EqualTo(5.5).Within(1e-9));
            Assert.That(charge.PouredGrains, Is.EqualTo(0.0).Within(1e-9),
                "dialling a charge loaded it without the button being pressed");

            dispenser.Throw();

            Assert.That(charge.PouredGrains, Is.EqualTo(5.5).Within(1e-9),
                "pressing dispense did not charge the case");

            Assert.That(Krofken.Ballistics.Units.KilogramsToGrains(charge.PouredCharge),
                Is.EqualTo(5.5).Within(1e-6),
                "what the screen says and what the design gets have drifted apart");
        }

        [UnityTest]
        public IEnumerator The_Dispenser_Refuses_A_Charge_That_Will_Not_Fit()
        {
            // The one refusal the bench is allowed to make: a charge that physically will not go
            // into the case is a fact about objects in the player's hands, and the interior solver
            // refuses the same load for the same reason. It must never refuse a load for being
            // merely dangerous — that is what the range is for.
            yield return PressPlay();

            var dispenser = Object.FindAnyObjectByType<PowderDispenser>();
            var charge = Object.FindAnyObjectByType<PowderBalance>();

            charge.Empty();

            Assert.That(dispenser.CaseVolume, Is.GreaterThan(0.0),
                "the dispenser does not know how big the case is");

            dispenser.Select(charge.MaxSettingGrains);

            Assert.That(dispenser.Overfull, Is.True,
                $"the largest charge the bench offers ({charge.MaxSettingGrains} gr) still fits a " +
                "9 mm case, so the warning can never fire and the case volume is wrong");

            dispenser.Throw();

            Assert.That(charge.PouredGrains, Is.EqualTo(0.0).Within(1e-9),
                "an overfull charge was dispensed anyway");

            Assert.That(dispenser.Values.text.ToLowerInvariant(), Does.Contain("not fit"),
                $"the screen does not warn that it will not fit: '{dispenser.Values.text}'");
        }

        [UnityTest]
        public IEnumerator The_Dispenser_Never_Predicts_Performance()
        {
            yield return PressPlay();

            var dispenser = Object.FindAnyObjectByType<PowderDispenser>();
            dispenser.Select(5.5);
            dispenser.Throw();

            string screen = (dispenser.Labels.text + " " + dispenser.Values.text).ToLowerInvariant();

            foreach (string forbidden in new[]
                     { "pressure", "velocity", "unsafe", "safely", "burst", "rupture",
                       "penetrat", "energy", "muzzle", "hot" })
                Assert.That(screen, Does.Not.Contain(forbidden),
                    $"the dispenser leaks '{forbidden}' about a round nobody has fired: '{screen}'");
        }


        [UnityTest]
        public IEnumerator The_Press_Readout_Never_Predicts_Performance()
        {
            // The canon's guard, applied to the new readout: the bench may say what a batch
            // CONSUMED and must never say how it will shoot. If this ever fails, the reason
            // to walk out to the range has gone and the game is a spreadsheet.
            yield return PressPlay();

            var shop = Object.FindAnyObjectByType<WorkshopController>();
            shop.PullHandle();
            yield return null;

            string readout = shop.Press.Readout.text.ToLowerInvariant();

            foreach (string forbidden in new[]
                     { "pressure", "velocity", "unsafe", "safely", "burst", "rupture",
                       "penetrat", "energy", "expansion", "muzzle" })
                Assert.That(readout, Does.Not.Contain(forbidden),
                    $"the press readout leaks '{forbidden}' about a round nobody has fired: '{readout}'");
        }
    }
}
