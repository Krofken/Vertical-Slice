using System.Collections;
using System.Linq;
using Gunsmith.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gunsmith.Tests
{
    /// <summary>
    /// Press Play and stand up.
    ///
    /// THIS SUITE EXISTS BECAUSE 199 PASSING TESTS DID NOT NOTICE THE GAME WAS UNPLAYABLE.
    ///
    /// `WorkshopBootstrap` adds `PlayerRig` and assigns its `Head` on the following line,
    /// so `Awake` ran one line too early: Head was null, the rig fell back to the body
    /// transform, and cached the body's WORLD position as the head's local rest offset.
    /// `LateUpdate` then forced the head there every frame, leaving the eye ten
    /// centimetres off the floor and three metres behind the body. The bench surface is
    /// at 92 cm, so the player was looking up at it from below the floor it stands on.
    ///
    /// Every EditMode test still passed, because not one of them enters play mode with a
    /// player in it — they check construction, materials, layout arithmetic and physics.
    /// Worse, every screenshot taken to "verify" the shop had the camera positioned by
    /// hand first, which bypassed the broken path entirely and made it look fine.
    ///
    /// So these assert the only thing that actually matters: that a person who presses
    /// Play ends up standing in a room, at eye height, looking level, able to see the
    /// bench. Anything that cannot be checked without staging the camera is not checked.
    /// </summary>
    public class StandingUpPlayTests
    {
        private GameObject _root;

        /// <summary>Bench work surface height, metres. The eye must clear this.</summary>
        private const float BenchSurface = 0.92f;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);

            foreach (var stray in Object.FindObjectsByType<PlayerRig>(FindObjectsInactive.Include))
                if (stray != null) Object.Destroy(stray.gameObject);
        }

        /// <summary>Builds the shop exactly as pressing Play does, then lets frames run
        /// so Awake, Start and LateUpdate have all happened.</summary>
        private IEnumerator PressPlay()
        {
            _root = new GameObject("Workshop");
            _root.AddComponent<WorkshopBootstrap>();

            // Three frames: one for Awake/Start, two more so LateUpdate has certainly
            // run and any focus blend has settled at rest.
            yield return null;
            yield return null;
            yield return null;
        }

        private static Camera EyeCamera()
            => Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                .FirstOrDefault(c => c.isActiveAndEnabled);

        [UnityTest]
        public IEnumerator The_Gunsmith_Is_Standing_At_Eye_Height()
        {
            yield return PressPlay();

            var camera = EyeCamera();
            Assert.That(camera, Is.Not.Null, "there is no active camera to see through");

            float eye = camera.transform.position.y;

            Assert.That(eye, Is.GreaterThan(BenchSurface),
                $"the eye is at {eye:F2} m and the bench surface is at {BenchSurface:F2} m — " +
                "the player cannot see the top of their own workbench");

            Assert.That(eye, Is.InRange(1.4f, 2.0f),
                $"the eye is at {eye:F2} m, which is not a standing person");
        }

        [UnityTest]
        public IEnumerator The_Eye_Is_Over_The_Body_And_Not_Behind_It()
        {
            // The specific shape of the bug: the head was displaced horizontally by the
            // body's own world position, ending up metres away from the character.
            yield return PressPlay();

            var rig = Object.FindAnyObjectByType<PlayerRig>();
            var camera = EyeCamera();
            Assert.That(rig, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            Vector3 body = rig.transform.position;
            Vector3 eye = camera.transform.position;

            float drift = Vector2.Distance(new Vector2(body.x, body.z), new Vector2(eye.x, eye.z));

            Assert.That(drift, Is.LessThan(0.25f),
                $"the eye is {drift:F2} m from the body horizontally — you are looking at " +
                "yourself from across the room, not out of your own head");
        }

        [UnityTest]
        public IEnumerator The_Gunsmith_Is_Looking_Level_And_Not_At_The_Floor()
        {
            yield return PressPlay();

            var camera = EyeCamera();
            Assert.That(camera, Is.Not.Null);

            float pitch = camera.transform.forward.y;

            Assert.That(Mathf.Abs(pitch), Is.LessThan(0.5f),
                $"the view is pitched {pitch:F2} — the player starts staring at the floor or the ceiling");
        }

        [UnityTest]
        public IEnumerator There_Is_Exactly_One_Gunsmith_And_He_Is_On_The_Floor()
        {
            yield return PressPlay();

            var rigs = Object.FindObjectsByType<PlayerRig>(FindObjectsInactive.Include);
            Assert.That(rigs.Length, Is.EqualTo(1), "more than one player rig in the scene");

            float feet = rigs[0].transform.position.y;
            Assert.That(feet, Is.InRange(-0.1f, 0.4f),
                $"the body is at {feet:F2} m — the gunsmith is buried or floating");
        }

        [UnityTest]
        public IEnumerator The_Bench_Is_Actually_Visible_From_Where_You_Spawn()
        {
            // The end of the chain: eye height, level view and a bench in front of you
            // are only worth anything if the bench is inside the frustum when you arrive.
            yield return PressPlay();

            var camera = EyeCamera();
            var bench = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(t => t.name == "Bench top");

            Assert.That(bench, Is.Not.Null, "no bench was built");

            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var bounds = bench.GetComponent<Renderer>().bounds;

            Assert.That(GeometryUtility.TestPlanesAABB(planes, bounds), Is.True,
                "the workbench is not on screen when the game starts");
        }
    }
}
