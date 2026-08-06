using System;
using UnityEngine;

namespace Krofken.Ballistics.UnityIntegration
{
    /// <summary>Details of a projectile striking something.</summary>
    public struct ProjectileImpact
    {
        /// <summary>Handle of the projectile that struck.</summary>
        public int Handle;

        /// <summary>Identifier of the ammunition registration it was fired with.</summary>
        public int AmmoId;

        /// <summary>Impact point, Unity world space.</summary>
        public Vector3 Point;

        /// <summary>Surface normal at the impact point, Unity world space.</summary>
        public Vector3 Normal;

        /// <summary>What was hit.</summary>
        public Collider Collider;

        /// <summary>Velocity at impact, simulation frame.</summary>
        public Vec3 Velocity;

        /// <summary>Speed at impact, m/s. This is what the terminal solver wants.</summary>
        public double Speed;

        /// <summary>Kinetic energy at impact, J.</summary>
        public double Energy;

        /// <summary>Time since the muzzle, s.</summary>
        public double TimeOfFlight;

        /// <summary>Distance flown, m.</summary>
        public double Distance;

        /// <summary>
        /// Angle between the velocity and the surface normal, rad. Zero is a
        /// perpendicular hit. Oblique impacts penetrate through more material and are
        /// far more likely to deflect.
        /// </summary>
        public double Obliquity;
    }

    /// <summary>How the game wants a projectile to continue after an impact.</summary>
    public struct ProjectileImpactResponse
    {
        /// <summary>Set true to keep the projectile alive -- it perforated the target
        /// and came out the other side. Leave false and it is despawned.</summary>
        public bool Continue;

        /// <summary>Where it re-emerges, Unity world space.</summary>
        public Vector3 ExitPoint;

        /// <summary>Velocity on exit, simulation frame.</summary>
        public Vec3 ExitVelocity;
    }

    /// <summary>Called for every impact. Written as a delegate rather than an event so
    /// the response can be passed by reference and cost no allocation.</summary>
    public delegate void ProjectileImpactHandler(in ProjectileImpact impact, ref ProjectileImpactResponse response);

    /// <summary>
    /// Drives every projectile in flight.
    ///
    /// WHY THIS EXISTS INSTEAD OF RIGIDBODIES
    /// --------------------------------------
    /// At 800 m/s a projectile covers 16 metres in one 50 Hz FixedUpdate tick. No
    /// rigidbody, no continuous-collision-detection setting and no interpolation mode
    /// recovers a trajectory from that. Unity's physics also integrates with a simple
    /// symplectic Euler step and applies drag as a per-tick velocity multiplier, which
    /// is not aerodynamic drag in any sense.
    ///
    /// So projectiles are not physics objects at all. This component integrates them
    /// itself with RK4 at its own step, then sweeps a raycast along each resulting
    /// segment to find what was hit. PhysX is used only as a spatial query structure,
    /// never as an integrator.
    ///
    /// STEP SIZING is bounded by DISTANCE, not time. The step is chosen so no
    /// projectile advances more than <see cref="maxSegmentLength"/> per substep, which
    /// keeps raycast cost proportional to distance flown rather than to flight time —
    /// a slow projectile does not burn the same number of queries as a fast one
    /// covering ten times the ground.
    ///
    /// ALLOCATION: none per frame. Fixed-capacity arrays, a free list of slots, and a
    /// preallocated raycast hit buffer.
    ///
    /// One component drives all projectiles. There is deliberately no per-projectile
    /// MonoBehaviour: the per-object Update call overhead dominates the actual maths
    /// once more than a handful are in the air.
    /// </summary>
    [AddComponentMenu("Ballistics/Projectile Simulator")]
    [DefaultExecutionOrder(-50)]
    public sealed class ProjectileSimulator : MonoBehaviour
    {
        [Header("Capacity")]
        [Tooltip("Maximum projectiles in flight at once. Slots are recycled.")]
        [SerializeField] private int maxProjectiles = 128;

        [Tooltip("Maximum distinct ammunition types that can be registered.")]
        [SerializeField] private int maxAmmoTypes = 64;

        [Header("Integration")]
        [Tooltip("Longest distance a projectile may advance in one substep, metres. " +
                 "Smaller means more raycasts and finer collision resolution.")]
        [SerializeField] private double maxSegmentLength = 0.5;

        [Tooltip("Longest substep, seconds. Bounds integration error for slow projectiles.")]
        [SerializeField] private double maxTimeStep = 0.005;

        [Tooltip("Shortest substep, seconds. Stops an extremely fast projectile from " +
                 "consuming an unbounded number of steps.")]
        [SerializeField] private double minTimeStep = 0.0002;

        [Header("Collision")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Cast radius in metres. Zero uses a plain raycast, which is what a " +
                 "bullet-sized projectile should use; a small radius helps against " +
                 "thin or fast-moving targets at some cost.")]
        [SerializeField] private float castRadius = 0.0f;

        [Header("Lifetime")]
        [SerializeField] private double maxFlightTime = 15.0;
        [SerializeField] private double maxRange = 3000.0;

        [Tooltip("Despawn once the projectile slows below this, m/s.")]
        [SerializeField] private double minimumSpeed = 15.0;

        [Tooltip("Despawn below this world height. Set very low to disable.")]
        [SerializeField] private float killPlaneHeight = -500f;

        /// <summary>Air the projectiles fly through. Assign from the environment.</summary>
        public Atmosphere Atmosphere = Atmosphere.Standard;

        /// <summary>Which forces are simulated.</summary>
        public TrajectoryOptions Options = TrajectoryOptions.Default;

        /// <summary>Raised for every impact.</summary>
        public ProjectileImpactHandler OnImpact;

        /// <summary>Raised when a projectile expires without hitting anything.</summary>
        public Action<int> OnExpired;

        // ---- Storage -------------------------------------------------------
        // Structure-of-arrays: the integrator touches state every substep and touches
        // nothing else, so keeping the cold fields in separate arrays keeps the hot
        // loop's cache lines full of the data it actually reads.
        private ProjectileState[] _states;
        private int[] _ammoIds;
        private double[] _distances;
        private Transform[] _visuals;
        private bool[] _alive;
        private int[] _freeSlots;
        private int _freeCount;
        private int _highWater;

        private ProjectileAerodynamics[] _ammo;
        private bool[] _ammoUsed;

        private RaycastHit[] _hitBuffer;

        /// <summary>Number of projectiles currently in flight.</summary>
        public int ActiveCount { get; private set; }

        private void Awake()
        {
            if (maxProjectiles < 1) maxProjectiles = 1;
            if (maxAmmoTypes < 1) maxAmmoTypes = 1;

            _states = new ProjectileState[maxProjectiles];
            _ammoIds = new int[maxProjectiles];
            _distances = new double[maxProjectiles];
            _visuals = new Transform[maxProjectiles];
            _alive = new bool[maxProjectiles];
            _freeSlots = new int[maxProjectiles];

            for (int i = 0; i < maxProjectiles; i++) _freeSlots[i] = maxProjectiles - 1 - i;
            _freeCount = maxProjectiles;

            _ammo = new ProjectileAerodynamics[maxAmmoTypes];
            _ammoUsed = new bool[maxAmmoTypes];

            _hitBuffer = new RaycastHit[8];
        }

        // ------------------------------------------------------------------
        // Ammunition registration
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers a baked cartridge's flight constants and returns an id to fire it
        /// with. Register once when the player commits a design, not per shot -- the
        /// point of baking is that the expensive work happens exactly once.
        /// </summary>
        public int RegisterAmmo(BakedCartridge cartridge)
        {
            if (cartridge == null) throw new ArgumentNullException(nameof(cartridge));
            return RegisterAmmo(cartridge.Aerodynamics);
        }

        /// <summary>Registers flight constants directly.</summary>
        public int RegisterAmmo(in ProjectileAerodynamics aerodynamics)
        {
            for (int i = 0; i < _ammoUsed.Length; i++)
            {
                if (_ammoUsed[i]) continue;
                _ammo[i] = aerodynamics;
                _ammoUsed[i] = true;
                return i;
            }

            Debug.LogError($"[Ballistics] Ammunition registry is full ({_ammoUsed.Length} entries).", this);
            return -1;
        }

        /// <summary>Updates an already-registered entry in place, so a re-baked design
        /// keeps its id and every reference to it stays valid.</summary>
        public void UpdateAmmo(int ammoId, in ProjectileAerodynamics aerodynamics)
        {
            if (!IsValidAmmo(ammoId)) return;
            _ammo[ammoId] = aerodynamics;
        }

        /// <summary>Releases a registration.</summary>
        public void UnregisterAmmo(int ammoId)
        {
            if (!IsValidAmmo(ammoId)) return;
            _ammoUsed[ammoId] = false;
        }

        private bool IsValidAmmo(int ammoId) =>
            ammoId >= 0 && ammoId < _ammoUsed.Length && _ammoUsed[ammoId];

        // ------------------------------------------------------------------
        // Firing
        // ------------------------------------------------------------------

        /// <summary>
        /// Launches a projectile.
        /// </summary>
        /// <param name="ammoId">Registration returned by <see cref="RegisterAmmo(BakedCartridge)"/>.</param>
        /// <param name="muzzlePosition">Muzzle position, Unity world space.</param>
        /// <param name="direction">Aim direction, Unity world space. Normalised internally.</param>
        /// <param name="muzzleVelocity">Speed at the muzzle, m/s.</param>
        /// <param name="spinRate">Axial spin, rad/s. Use <c>Barrel.SpinRateAt</c>.</param>
        /// <param name="visual">Optional transform to drive. May be null for a
        /// projectile that is simulated but not drawn.</param>
        /// <returns>A handle, or -1 if the simulator is full.</returns>
        public int Fire(
            int ammoId,
            Vector3 muzzlePosition,
            Vector3 direction,
            double muzzleVelocity,
            double spinRate = 0.0,
            Transform visual = null)
        {
            if (!IsValidAmmo(ammoId))
            {
                Debug.LogError($"[Ballistics] Fire called with unregistered ammo id {ammoId}.", this);
                return -1;
            }

            if (_freeCount == 0)
            {
                Debug.LogWarning("[Ballistics] No free projectile slots; shot dropped.", this);
                return -1;
            }

            Vector3 unitDirection = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.forward;

            int handle = _freeSlots[--_freeCount];

            _states[handle] = new ProjectileState
            {
                Position = BallisticsConversion.ToSimulation(muzzlePosition),
                Velocity = BallisticsConversion.ToSimulation(unitDirection) * muzzleVelocity,
                Time = 0.0,
                SpinRate = spinRate
            };

            _ammoIds[handle] = ammoId;
            _distances[handle] = 0.0;
            _visuals[handle] = visual;
            _alive[handle] = true;

            if (handle >= _highWater) _highWater = handle + 1;
            ActiveCount++;

            if (visual != null)
            {
                visual.SetPositionAndRotation(
                    muzzlePosition,
                    BallisticsConversion.LookAlongVelocity(_states[handle].Velocity));
            }

            return handle;
        }

        /// <summary>Removes a projectile without raising an impact.</summary>
        public void Despawn(int handle)
        {
            if (handle < 0 || handle >= _alive.Length || !_alive[handle]) return;

            _alive[handle] = false;
            _visuals[handle] = null;
            _freeSlots[_freeCount++] = handle;
            ActiveCount--;
        }

        /// <summary>Current state of a projectile in flight, for inspection or replay.</summary>
        public bool TryGetState(int handle, out ProjectileState state)
        {
            if (handle < 0 || handle >= _alive.Length || !_alive[handle])
            {
                state = default;
                return false;
            }

            state = _states[handle];
            return true;
        }

        // ------------------------------------------------------------------
        // Simulation
        // ------------------------------------------------------------------

        private void Update()
        {
            if (ActiveCount == 0) return;

            double frameTime = Time.deltaTime;
            if (frameTime <= 0.0) return;

            // Guard against a hitch dumping a huge dt in and stalling for a second
            // while it catches up.
            if (frameTime > 0.25) frameTime = 0.25;

            for (int handle = 0; handle < _highWater; handle++)
            {
                if (!_alive[handle]) continue;
                Advance(handle, frameTime);
            }

            UpdateVisuals();
        }

        /// <summary>Advances one projectile through a frame's worth of time.</summary>
        private void Advance(int handle, double frameTime)
        {
            int ammoId = _ammoIds[handle];
            ref var aerodynamics = ref _ammo[ammoId];

            double remaining = frameTime;

            while (remaining > 0.0 && _alive[handle])
            {
                var state = _states[handle];
                double speed = state.Speed;

                // Step chosen so the projectile advances no further than
                // maxSegmentLength -- collision fidelity is a function of distance,
                // not of time.
                double step = speed > 1e-6 ? maxSegmentLength / speed : maxTimeStep;
                if (step > maxTimeStep) step = maxTimeStep;
                if (step < minTimeStep) step = minTimeStep;
                if (step > remaining) step = remaining;

                var next = TrajectoryIntegrator.Step(state, aerodynamics, Atmosphere, Options, step);

                if (!next.Position.IsFinite || !next.Velocity.IsFinite)
                {
                    Despawn(handle);
                    return;
                }

                Vector3 from = BallisticsConversion.ToUnity(state.Position);
                Vector3 to = BallisticsConversion.ToUnity(next.Position);
                Vector3 segment = to - from;
                float segmentLength = segment.magnitude;

                if (segmentLength > 1e-6f && SweepSegment(from, segment / segmentLength, segmentLength, out var hit))
                {
                    // Interpolate the state onto the impact point rather than using
                    // the end of the step, so reported impact velocity matches where
                    // the projectile actually was.
                    double fraction = segmentLength > 1e-9f ? hit.distance / segmentLength : 0.0;
                    var atImpact = new ProjectileState
                    {
                        Position = Vec3.Lerp(state.Position, next.Position, fraction),
                        Velocity = Vec3.Lerp(state.Velocity, next.Velocity, fraction),
                        Time = state.Time + step * fraction,
                        SpinRate = next.SpinRate
                    };

                    _distances[handle] += segmentLength * fraction;
                    _states[handle] = atImpact;

                    ResolveImpact(handle, ammoId, hit, atImpact);
                    return;
                }

                _distances[handle] += segmentLength;
                _states[handle] = next;
                remaining -= step;

                if (ShouldExpire(handle, next))
                {
                    OnExpired?.Invoke(handle);
                    Despawn(handle);
                    return;
                }
            }
        }

        private bool ShouldExpire(int handle, in ProjectileState state)
        {
            if (state.Time >= maxFlightTime) return true;
            if (_distances[handle] >= maxRange) return true;
            if (state.Speed <= minimumSpeed) return true;
            if (state.Position.Z <= killPlaneHeight) return true;
            return false;
        }

        /// <summary>
        /// Casts along one segment. Uses the non-allocating overloads and picks the
        /// nearest hit manually, because <c>Physics.Raycast</c> does not guarantee the
        /// closest result when several colliders overlap the ray origin.
        /// </summary>
        private bool SweepSegment(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            int count = castRadius > 0f
                ? Physics.SphereCastNonAlloc(origin, castRadius, direction, _hitBuffer, distance, hitMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, direction, _hitBuffer, distance, hitMask, QueryTriggerInteraction.Ignore);

            if (count <= 0)
            {
                nearest = default;
                return false;
            }

            int best = 0;
            for (int i = 1; i < count; i++)
                if (_hitBuffer[i].distance < _hitBuffer[best].distance)
                    best = i;

            nearest = _hitBuffer[best];
            return true;
        }

        private void ResolveImpact(int handle, int ammoId, in RaycastHit hit, in ProjectileState state)
        {
            double speed = state.Speed;
            var normal = hit.normal;

            // Angle between the incoming path and the surface normal. A perpendicular
            // hit is zero; a glancing hit approaches 90 degrees and drives the
            // projectile through far more material than the plate is thick.
            Vector3 travel = BallisticsConversion.ToUnity(state.Velocity).normalized;
            double obliquity = Mathf.Acos(Mathf.Clamp(Vector3.Dot(-travel, normal), -1f, 1f));

            var impact = new ProjectileImpact
            {
                Handle = handle,
                AmmoId = ammoId,
                Point = hit.point,
                Normal = normal,
                Collider = hit.collider,
                Velocity = state.Velocity,
                Speed = speed,
                Energy = 0.5 * _ammo[ammoId].Mass * speed * speed,
                TimeOfFlight = state.Time,
                Distance = _distances[handle],
                Obliquity = obliquity
            };

            var response = new ProjectileImpactResponse();
            OnImpact?.Invoke(impact, ref response);

            if (!response.Continue)
            {
                Despawn(handle);
                return;
            }

            // Perforated: resume from the exit point. Nudged along the exit velocity
            // so the projectile does not immediately re-hit the collider it just left.
            var exitVelocity = response.ExitVelocity;
            Vector3 exitPoint = response.ExitPoint;
            Vector3 exitDirection = BallisticsConversion.ToUnity(exitVelocity);

            if (exitDirection.sqrMagnitude > 1e-12f)
                exitPoint += exitDirection.normalized * 0.001f;

            _states[handle] = new ProjectileState
            {
                Position = BallisticsConversion.ToSimulation(exitPoint),
                Velocity = exitVelocity,
                Time = state.Time,
                SpinRate = state.SpinRate
            };
        }

        /// <summary>
        /// Pushes final positions to the visual transforms, once per frame rather than
        /// once per substep. A projectile can take dozens of substeps in a frame and
        /// only the last one is ever seen.
        /// </summary>
        private void UpdateVisuals()
        {
            for (int handle = 0; handle < _highWater; handle++)
            {
                if (!_alive[handle]) continue;

                var visual = _visuals[handle];
                if (visual == null) continue;

                var state = _states[handle];
                Vector3 position = BallisticsConversion.ToUnity(state.Position);

                // A gyroscopically stable projectile tracks its velocity vector to
                // within a fraction of a degree. An unstable one does not -- it
                // tumbles, and showing that is the whole point of letting the player
                // build one that does.
                Quaternion rotation;
                if (_ammo[_ammoIds[handle]].StabilityFactor >= 1.0)
                {
                    rotation = BallisticsConversion.LookAlongVelocity(state.Velocity);
                }
                else
                {
                    float tumble = (float)(state.Time * 40.0);
                    rotation = BallisticsConversion.LookAlongVelocity(state.Velocity)
                               * Quaternion.Euler(tumble * 57.3f, tumble * 23.1f, 0f);
                }

                visual.SetPositionAndRotation(position, rotation);
            }
        }
    }
}
