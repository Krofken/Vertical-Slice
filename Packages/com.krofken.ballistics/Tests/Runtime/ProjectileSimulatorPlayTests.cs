using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Krofken.Ballistics.UnityIntegration;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// The one part of the chain that cannot be checked without a running scene:
    /// <see cref="ProjectileSimulator"/> actually flying a round and hitting a real
    /// collider.
    ///
    /// Everything else in this codebase is a pure function over explicit state and is
    /// tested outside the editor. This is not — it drives itself from Update, queries
    /// PhysX for collisions, and converts between the simulation frame and Unity's.
    /// Each of those is a place a sign or a unit can be wrong in a way no EditMode test
    /// would notice, which is why this exists as a PlayMode suite.
    ///
    /// What is asserted is deliberately coarse: that a shot arrives, roughly when the
    /// speed of the projectile says it should, having dropped roughly as far as gravity
    /// says it should. Tight numbers here would be re-testing the integrator, which is
    /// already pinned against the closed-form vacuum solution elsewhere.
    /// </summary>
    public class ProjectileSimulatorPlayTests
    {
        private const float TargetDistance = 25f;

        private GameObject _simulatorObject;
        private GameObject _target;

        private bool _hit;
        private ProjectileImpact _impact;
        private int _expiredHandle = -1;

        [SetUp]
        public void SetUp()
        {
            _hit = false;
            _expiredHandle = -1;

            _simulatorObject = new GameObject("Simulator");
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _target.name = "Backstop";
            _target.transform.position = new Vector3(0f, 0f, TargetDistance);

            // Wide and thin: a plate the shot cannot plausibly miss sideways, but that
            // a 50 Hz rigidbody step would skip straight through.
            _target.transform.localScale = new Vector3(6f, 6f, 0.1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_simulatorObject != null) Object.DestroyImmediate(_simulatorObject);
            if (_target != null) Object.DestroyImmediate(_target);
        }

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

        private void RecordImpact(in ProjectileImpact impact, ref ProjectileImpactResponse response)
        {
            _impact = impact;
            _hit = true;
            response.Continue = false;      // stopped in the backstop
        }

        [UnityTest]
        public IEnumerator Projectile_Flies_Downrange_And_Strikes_A_Collider()
        {
            var simulator = _simulatorObject.AddComponent<ProjectileSimulator>();
            simulator.OnImpact = RecordImpact;
            simulator.OnExpired = handle => _expiredHandle = handle;

            // Let the collider register with PhysX before anything is cast at it.
            yield return null;

            var baked = CartridgeBaker.Bake(Baseline(), BarrelLibrary.ServicePistol9mm);
            Assert.That(baked.IsValid, Is.True, string.Join("; ", baked.Issues));

            int ammo = simulator.RegisterAmmo(baked);
            Assert.That(ammo, Is.GreaterThanOrEqualTo(0), "ammunition failed to register");

            int handle = simulator.Fire(
                ammo, Vector3.zero, Vector3.forward, baked.MuzzleVelocity, spinRate: 0.0);

            Assert.That(handle, Is.GreaterThanOrEqualTo(0), "the simulator refused the shot");
            Assert.That(simulator.ActiveCount, Is.EqualTo(1), "nothing is in flight");

            // Generous: the round covers 25 m in under a tenth of a second, so anything
            // approaching this bound means it is not moving downrange at all.
            float deadline = 3f;
            while (!_hit && deadline > 0f)
            {
                deadline -= Time.deltaTime;
                yield return null;
            }

            Assert.That(_expiredHandle, Is.EqualTo(-1), "the projectile expired instead of hitting");
            Assert.That(_hit, Is.True, "the projectile never reached the backstop");

            Assert.That(_impact.Collider, Is.Not.Null);
            Assert.That(_impact.Collider.gameObject.name, Is.EqualTo("Backstop"));

            // Arrived at the plate, not somewhere else.
            Assert.That(_impact.Point.z, Is.EqualTo(TargetDistance).Within(0.5f), "impact depth");
            Assert.That(Mathf.Abs(_impact.Point.x), Is.LessThan(0.2f), "drifted sideways");

            // Time of flight is distance over roughly the muzzle speed. Anything wildly
            // outside this means the unit conversion is wrong somewhere.
            double expected = TargetDistance / baked.MuzzleVelocity;
            Assert.That(_impact.TimeOfFlight, Is.InRange(expected * 0.8, expected * 2.0), "time of flight");

            // Drag must have taken something off, and must not have taken everything.
            Assert.That(_impact.Speed, Is.LessThan(baked.MuzzleVelocity), "no drag was applied");
            Assert.That(_impact.Speed, Is.GreaterThan(baked.MuzzleVelocity * 0.5), "far too much drag");

            Assert.That(_impact.Distance, Is.EqualTo(TargetDistance).Within(1.0), "distance flown");
            Assert.That(_impact.Energy, Is.GreaterThan(0.0));

            // Nearly perpendicular into a flat plate.
            Assert.That(_impact.Obliquity, Is.LessThan(0.2), "should be close to a square hit");

            simulator.Despawn(handle);
        }

        /// <summary>
        /// Gravity must act, and must act DOWN. A sign error here is invisible in every
        /// EditMode test because the conversion between the simulation frame and Unity's
        /// only happens in this component.
        /// </summary>
        [UnityTest]
        public IEnumerator Projectile_Drops_Under_Gravity_By_The_Expected_Amount()
        {
            var simulator = _simulatorObject.AddComponent<ProjectileSimulator>();
            simulator.OnImpact = RecordImpact;

            yield return null;

            var baked = CartridgeBaker.Bake(Baseline(), BarrelLibrary.ServicePistol9mm);
            int ammo = simulator.RegisterAmmo(baked);

            simulator.Fire(ammo, Vector3.zero, Vector3.forward, baked.MuzzleVelocity);

            float deadline = 3f;
            while (!_hit && deadline > 0f)
            {
                deadline -= Time.deltaTime;
                yield return null;
            }

            Assert.That(_hit, Is.True, "the projectile never arrived");

            // Fired flat, so drop is 0.5*g*t^2 with t the time of flight. Drag makes the
            // real figure slightly larger, hence the one-sided band.
            double t = _impact.TimeOfFlight;
            double freeFall = 0.5 * PhysicalConstants.StandardGravity * t * t;

            Assert.That(_impact.Point.y, Is.LessThan(0f), "it rose, so gravity is inverted");
            Assert.That(-_impact.Point.y, Is.InRange(freeFall * 0.5, freeFall * 2.0),
                $"drop of {-_impact.Point.y:F4} m does not match {freeFall:F4} m of free fall over {t:F4} s");
        }

        /// <summary>A shot into empty space must expire rather than live forever.</summary>
        [UnityTest]
        public IEnumerator Projectile_With_Nothing_To_Hit_Expires()
        {
            Object.DestroyImmediate(_target);

            var simulator = _simulatorObject.AddComponent<ProjectileSimulator>();
            simulator.OnImpact = RecordImpact;
            simulator.OnExpired = handle => _expiredHandle = handle;

            yield return null;

            var baked = CartridgeBaker.Bake(Baseline(), BarrelLibrary.ServicePistol9mm);
            int ammo = simulator.RegisterAmmo(baked);

            // Straight up and deliberately SLOW. At a real muzzle velocity this would
            // coast for the full fifteen-second flight-time limit before expiring, and
            // the test would spend fifteen real seconds watching it. Launched just
            // above the minimum-speed threshold it decelerates through that threshold
            // in about half a second, which exercises the same expiry path.
            simulator.Fire(ammo, Vector3.zero, Vector3.up, 20.0);

            float deadline = 10f;
            while (_expiredHandle < 0 && deadline > 0f)
            {
                deadline -= Time.deltaTime;
                yield return null;
            }

            Assert.That(_hit, Is.False, "there was nothing to hit");
            Assert.That(_expiredHandle, Is.GreaterThanOrEqualTo(0), "the projectile never expired");
            Assert.That(simulator.ActiveCount, Is.Zero, "the slot was not released");
        }
    }
}
