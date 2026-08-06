using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Complete state of a projectile in flight.
    ///
    /// This struct IS the simulation state -- there is nothing else. Everything the
    /// integrator needs to advance the projectile is in here, and the step function
    /// is a pure function of (state, constants, dt).
    ///
    /// That property is deliberate and load-bearing. It means a trajectory can be
    /// replayed exactly from any recorded state, rewound, re-simulated at a past tick
    /// for lag compensation, or handed to another machine -- none of which is possible
    /// once simulation state hides inside component fields or static caches. The
    /// single-player game does not need this; keeping it costs nothing and the other
    /// project does.
    /// </summary>
    [Serializable]
    public struct ProjectileState
    {
        /// <summary>Position in world axes, m. (+X downrange, +Y left, +Z up.)</summary>
        public Vec3 Position;

        /// <summary>Velocity in world axes, m/s.</summary>
        public Vec3 Velocity;

        /// <summary>Time since the projectile left the muzzle, s.</summary>
        public double Time;

        /// <summary>Axial spin rate, rad/s. Decays slowly in flight.</summary>
        public double SpinRate;

        /// <summary>Speed, m/s.</summary>
        public double Speed => Velocity.Magnitude;

        /// <summary>Kinetic energy for a given mass, J.</summary>
        public double KineticEnergy(double mass) => 0.5 * mass * Velocity.SqrMagnitude;
    }

    /// <summary>
    /// Everything about a projectile that does NOT change during flight, baked once
    /// at design time.
    ///
    /// Splitting constants from state is what makes the hot loop cheap: the
    /// integrator reads this by reference, never rebuilds it, and never touches the
    /// geometry, materials or drag model that produced it.
    /// </summary>
    [Serializable]
    public struct ProjectileAerodynamics
    {
        /// <summary>Mass, kg.</summary>
        public double Mass;

        /// <summary>Frontal reference area, m^2.</summary>
        public double ReferenceArea;

        /// <summary>Body diameter, m.</summary>
        public double Calibre;

        /// <summary>Baked drag curve.</summary>
        public DragTable Drag;

        /// <summary>Gyroscopic stability factor at the muzzle. Below 1 the projectile
        /// tumbles and the drag model no longer describes it.</summary>
        public double StabilityFactor;

        /// <summary>
        /// Convenience factor: 0.5 * A / m. Drag deceleration is this times
        /// rho * Cd * v^2, so precomputing it removes two divisions from every
        /// derivative evaluation -- and there are four of those per RK4 step.
        /// </summary>
        public double DragFactor => Mass > 0.0 ? 0.5 * ReferenceArea / Mass : 0.0;

        /// <summary>
        /// Builds the flight constants from a finished design.
        /// </summary>
        public static ProjectileAerodynamics Bake(
            in ProjectileGeometry geometry,
            double mass,
            in DragTable drag,
            double stabilityFactor = 1.5) => new ProjectileAerodynamics
            {
                Mass = mass,
                ReferenceArea = geometry.ReferenceArea,
                Calibre = geometry.Calibre,
                Drag = drag,
                StabilityFactor = stabilityFactor
            };
    }

    /// <summary>Which physical effects the integrator includes.</summary>
    [Serializable]
    public struct TrajectoryOptions
    {
        /// <summary>Include gravity. Effectively always on.</summary>
        public bool Gravity;

        /// <summary>Include aerodynamic drag.</summary>
        public bool Drag;

        /// <summary>
        /// Include the Coriolis acceleration from Earth's rotation. Rigorous physics,
        /// but the effect is under a millimetre inside 300 m -- pure cost at the
        /// ranges this game shoots at. Worth having for long-range work.
        /// </summary>
        public bool Coriolis;

        /// <summary>
        /// Include spin drift. NOT rigorous -- see <see cref="TrajectoryIntegrator"/>
        /// remarks. Off by default.
        /// </summary>
        public bool SpinDrift;

        /// <summary>Shooter latitude, rad. Only used by the Coriolis term.</summary>
        public double Latitude;

        /// <summary>Firing azimuth measured clockwise from true north, rad.
        /// Only used by the Coriolis term.</summary>
        public double Azimuth;

        /// <summary>True for right-hand rifling twist, which drifts the projectile
        /// right. Only used by the spin drift term.</summary>
        public bool RightHandTwist;

        /// <summary>Gravity and drag only. The correct default for a backyard range.</summary>
        public static TrajectoryOptions Default => new TrajectoryOptions
        {
            Gravity = true,
            Drag = true,
            Coriolis = false,
            SpinDrift = false,
            RightHandTwist = true
        };

        /// <summary>Every effect enabled, for long-range work.</summary>
        public static TrajectoryOptions Full => new TrajectoryOptions
        {
            Gravity = true,
            Drag = true,
            Coriolis = true,
            SpinDrift = true,
            Latitude = Units.DegreesToRadians(45.0),
            Azimuth = 0.0,
            RightHandTwist = true
        };
    }

    /// <summary>
    /// Exterior ballistics: advances a projectile through the air.
    ///
    /// FORCES
    /// ------
    ///   DRAG      a = -(1/2) * rho * Cd(M) * A * |v_rel| * v_rel / m
    ///             Computed against velocity RELATIVE TO THE AIR, so wind is handled
    ///             correctly and for free -- a crosswind does not push the projectile
    ///             sideways so much as make it fly slightly sideways into its own drag.
    ///
    ///   GRAVITY   a = -g in the up axis. Constant; the variation over any small-arms
    ///             trajectory is far below the model's other error sources.
    ///
    ///   CORIOLIS  a = -2 * Omega x v, with Omega expressed in the local firing frame
    ///             from latitude and azimuth. Exact, cheap, and negligible under a
    ///             few hundred metres -- off by default.
    ///
    ///   SPIN DRIFT  empirical. A spinning projectile flies at a small yaw of repose
    ///             and generates a persistent sideways force. Deriving it properly
    ///             needs the overturning-moment coefficient, which requires measured
    ///             aerodynamic data per shape that this library does not have. The
    ///             correlation used is a published fit for conventional bullets and is
    ///             disabled by default; it is here so long-range work is not silently
    ///             wrong, not because it is rigorous.
    ///
    /// INTEGRATION -- classical RK4 at a fixed step.
    ///
    /// WHY NOT UNITY PHYSICS: at 800 m/s a 50 Hz FixedUpdate advances the projectile
    /// 16 metres per tick. No rigidbody or continuous-collision setting recovers a
    /// trajectory from that. This integrator runs at its own step, independent of
    /// frame rate and of the physics tick, and the Unity layer sweeps a raycast along
    /// each resulting segment to find impacts.
    ///
    /// ALLOCATION: none. Every method here is a pure function over structs.
    /// </summary>
    public static class TrajectoryIntegrator
    {
        /// <summary>
        /// Advances one step. Pure: same inputs always give the same output, with no
        /// hidden state anywhere.
        /// </summary>
        public static ProjectileState Step(
            in ProjectileState state,
            in ProjectileAerodynamics projectile,
            in Atmosphere atmosphere,
            in TrajectoryOptions options,
            double dt)
        {
            // Classical RK4 on position and velocity. Velocity's derivative is
            // acceleration, which depends on velocity (drag) but not on position, so
            // each stage needs one acceleration evaluation.
            Vec3 p0 = state.Position, v0 = state.Velocity;
            double t0 = state.Time;

            Vec3 a1 = Acceleration(v0, t0, projectile, atmosphere, options, state.SpinRate);
            Vec3 k1p = v0, k1v = a1;

            Vec3 v2 = v0 + k1v * (dt * 0.5);
            Vec3 a2 = Acceleration(v2, t0 + dt * 0.5, projectile, atmosphere, options, state.SpinRate);
            Vec3 k2p = v2, k2v = a2;

            Vec3 v3 = v0 + k2v * (dt * 0.5);
            Vec3 a3 = Acceleration(v3, t0 + dt * 0.5, projectile, atmosphere, options, state.SpinRate);
            Vec3 k3p = v3, k3v = a3;

            Vec3 v4 = v0 + k3v * dt;
            Vec3 a4 = Acceleration(v4, t0 + dt, projectile, atmosphere, options, state.SpinRate);
            Vec3 k4p = v4, k4v = a4;

            double sixth = dt / 6.0;

            var next = new ProjectileState
            {
                Position = p0 + (k1p + 2.0 * k2p + 2.0 * k3p + k4p) * sixth,
                Velocity = v0 + (k1v + 2.0 * k2v + 2.0 * k3v + k4v) * sixth,
                Time = t0 + dt,
                SpinRate = DecaySpin(state.SpinRate, dt)
            };

            return next;
        }

        /// <summary>
        /// Total acceleration on the projectile, m/s^2.
        /// </summary>
        public static Vec3 Acceleration(
            in Vec3 velocity,
            double time,
            in ProjectileAerodynamics projectile,
            in Atmosphere atmosphere,
            in TrajectoryOptions options,
            double spinRate)
        {
            Vec3 acceleration = Vec3.Zero;

            if (options.Gravity)
                acceleration.Z -= PhysicalConstants.StandardGravity;

            if (options.Drag)
            {
                // Airspeed, not ground speed. This is the whole reason wind works.
                Vec3 relative = velocity - atmosphere.Wind;
                double speed = relative.Magnitude;

                if (speed > 1e-6)
                {
                    double mach = speed / atmosphere.SpeedOfSound;
                    double cd = projectile.Drag.Evaluate(mach);

                    // a = (1/2) rho Cd A v^2 / m, directed against the relative wind.
                    // DragFactor folds 0.5*A/m together at bake time.
                    double magnitude = projectile.DragFactor * atmosphere.Density * cd * speed * speed;
                    acceleration -= relative * (magnitude / speed);
                }
            }

            if (options.Coriolis)
            {
                // Earth's rotation vector resolved into the local firing frame.
                // North lies at (cos azimuth, sin azimuth, 0) when +X is downrange at
                // the given azimuth and +Y is left; the vertical component is sin(latitude).
                double cosLat = Math.Cos(options.Latitude);
                double sinLat = Math.Sin(options.Latitude);
                double cosAz = Math.Cos(options.Azimuth);
                double sinAz = Math.Sin(options.Azimuth);

                var omega = new Vec3(
                    PhysicalConstants.EarthAngularVelocity * cosLat * cosAz,
                    PhysicalConstants.EarthAngularVelocity * cosLat * sinAz,
                    PhysicalConstants.EarthAngularVelocity * sinLat);

                acceleration -= 2.0 * Vec3.Cross(omega, velocity);
            }

            if (options.SpinDrift && time > 1e-4)
            {
                // EMPIRICAL. Litz's correlation gives lateral drift as
                //     d(t) = 1.25 * (Sg + 1.2) * t^1.83   [inches, t in seconds]
                // Differentiating twice converts that displacement fit into an
                // acceleration so it can live inside the same integrator as the real
                // forces, rather than being bolted on afterwards.
                const double exponent = 1.83;
                double amplitude = Units.InchesToMetres(1.25 * (projectile.StabilityFactor + 1.2));
                double lateral = amplitude * exponent * (exponent - 1.0) * Math.Pow(time, exponent - 2.0);

                // Right-hand twist drifts right, which is -Y in a left-positive frame.
                acceleration.Y += options.RightHandTwist ? -lateral : lateral;
            }

            return acceleration;
        }

        /// <summary>
        /// Spin decays far more slowly than velocity -- surface friction against the
        /// air is a weak torque compared to the pressure drag slowing the projectile
        /// down. The time constant here is representative for small arms; spin loss
        /// only matters at all because gyroscopic stability depends on it.
        /// </summary>
        private static double DecaySpin(double spinRate, double dt)
        {
            const double timeConstant = 8.0; // s
            return spinRate * Math.Exp(-dt / timeConstant);
        }
    }

    /// <summary>One recorded point along a trajectory.</summary>
    [Serializable]
    public struct TrajectorySample
    {
        public Vec3 Position;
        public Vec3 Velocity;
        public double Time;
        public double Speed;
        public double Mach;
        public double Energy;
    }

    /// <summary>Why a trajectory walk stopped.</summary>
    public enum TrajectoryEnd
    {
        /// <summary>Reached the requested maximum range.</summary>
        MaxRange = 0,
        /// <summary>Reached the requested maximum flight time.</summary>
        MaxTime = 1,
        /// <summary>Descended below the ground plane.</summary>
        Ground = 2,
        /// <summary>Ran out of room in the sample buffer.</summary>
        BufferFull = 3,
        /// <summary>Slowed below the useful threshold.</summary>
        Stopped = 4
    }

    /// <summary>
    /// Walks a full trajectory and records it. Used by the ballistic calculator UI
    /// and the range's trajectory replay.
    ///
    /// For a projectile actually flying in the scene the Unity layer steps
    /// <see cref="TrajectoryIntegrator"/> directly and sweeps a raycast between
    /// successive positions, rather than pre-computing a path that a moving target
    /// would invalidate.
    /// </summary>
    public static class TrajectorySolver
    {
        /// <summary>
        /// Integration step, s. One millisecond keeps the RK4 truncation error below
        /// a millimetre over a typical flight -- verified by halving the step and
        /// comparing, which is the only honest way to choose one.
        /// </summary>
        public const double DefaultTimeStep = 1e-3;

        /// <summary>
        /// Fills <paramref name="samples"/> with the trajectory.
        /// </summary>
        /// <param name="initial">State at the muzzle.</param>
        /// <param name="projectile">Baked flight constants.</param>
        /// <param name="atmosphere">Air state.</param>
        /// <param name="options">Which forces to include.</param>
        /// <param name="samples">Destination buffer; never allocated by this method.</param>
        /// <param name="sampleInterval">Seconds between recorded samples. Independent
        /// of the integration step -- integrate finely, record coarsely.</param>
        /// <param name="maxRange">Stop once this downrange distance is passed, m.</param>
        /// <param name="maxTime">Stop after this much flight time, s.</param>
        /// <param name="groundHeight">Stop when the projectile falls below this
        /// height, m. Pass double.NegativeInfinity to disable.</param>
        /// <param name="timeStep">Integration step, s.</param>
        /// <param name="count">Number of samples written.</param>
        /// <returns>Why the walk ended.</returns>
        public static TrajectoryEnd Simulate(
            in ProjectileState initial,
            in ProjectileAerodynamics projectile,
            in Atmosphere atmosphere,
            in TrajectoryOptions options,
            TrajectorySample[] samples,
            out int count,
            double sampleInterval = 0.01,
            double maxRange = 1000.0,
            double maxTime = 10.0,
            double groundHeight = 0.0,
            double timeStep = DefaultTimeStep)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (timeStep <= 0.0) timeStep = DefaultTimeStep;
            if (sampleInterval < timeStep) sampleInterval = timeStep;

            count = 0;
            var state = initial;
            double nextSampleTime = 0.0;

            Record(samples, ref count, state, projectile, atmosphere);
            nextSampleTime += sampleInterval;

            while (true)
            {
                var previous = state;
                state = TrajectoryIntegrator.Step(state, projectile, atmosphere, options, timeStep);

                if (!state.Position.IsFinite || !state.Velocity.IsFinite)
                    return TrajectoryEnd.Stopped;

                if (state.Time >= nextSampleTime)
                {
                    if (count >= samples.Length) return TrajectoryEnd.BufferFull;
                    Record(samples, ref count, state, projectile, atmosphere);
                    nextSampleTime += sampleInterval;
                }

                // Ground plane crossing, interpolated onto the exact intersection so
                // the recorded impact point does not depend on step phasing.
                if (state.Position.Z < groundHeight && previous.Position.Z >= groundHeight)
                {
                    double span = previous.Position.Z - state.Position.Z;
                    double fraction = span > 1e-12 ? (previous.Position.Z - groundHeight) / span : 0.0;
                    var impact = new ProjectileState
                    {
                        Position = Vec3.Lerp(previous.Position, state.Position, fraction),
                        Velocity = Vec3.Lerp(previous.Velocity, state.Velocity, fraction),
                        Time = previous.Time + (state.Time - previous.Time) * fraction,
                        SpinRate = state.SpinRate
                    };
                    if (count < samples.Length) Record(samples, ref count, impact, projectile, atmosphere);
                    return TrajectoryEnd.Ground;
                }

                if (state.Position.X >= maxRange) return TrajectoryEnd.MaxRange;
                if (state.Time >= maxTime) return TrajectoryEnd.MaxTime;
                if (state.Speed < 1.0) return TrajectoryEnd.Stopped;
            }
        }

        private static void Record(
            TrajectorySample[] samples,
            ref int count,
            in ProjectileState state,
            in ProjectileAerodynamics projectile,
            in Atmosphere atmosphere)
        {
            if (count >= samples.Length) return;

            double speed = state.Speed;
            samples[count++] = new TrajectorySample
            {
                Position = state.Position,
                Velocity = state.Velocity,
                Time = state.Time,
                Speed = speed,
                Mach = speed / atmosphere.SpeedOfSound,
                Energy = 0.5 * projectile.Mass * speed * speed
            };
        }

        /// <summary>
        /// Finds the launch elevation, in radians, that puts the projectile on the
        /// point of aim at a given distance.
        ///
        /// There is no closed form once drag is involved, so this is a bisection on
        /// the drop at the target -- monotone in elevation over any sane range, so
        /// bisection is both robust and adequate. Used to zero the range's rest.
        /// </summary>
        /// <remarks>
        /// Takes its struct arguments by value rather than by <c>in</c> because the
        /// bisection body is a local function closing over them, and C# forbids
        /// capturing <c>in</c> parameters. This runs once when zeroing, not per step.
        /// </remarks>
        public static double SolveZeroElevation(
            double muzzleVelocity,
            ProjectileAerodynamics projectile,
            Atmosphere atmosphere,
            TrajectoryOptions options,
            double zeroRange,
            double sightHeight = 0.0,
            double timeStep = DefaultTimeStep)
        {
            const int iterations = 40;
            double low = Units.DegreesToRadians(-5.0);
            double high = Units.DegreesToRadians(15.0);

            for (int i = 0; i < iterations; i++)
            {
                double mid = 0.5 * (low + high);
                double drop = HeightAtRange(mid);

                if (drop > 0.0) high = mid;
                else low = mid;
            }

            return 0.5 * (low + high);

            // Height above the line of sight when the projectile reaches zeroRange.
            double HeightAtRange(double elevation)
            {
                var state = new ProjectileState
                {
                    Position = new Vec3(0.0, 0.0, -sightHeight),
                    Velocity = new Vec3(muzzleVelocity * Math.Cos(elevation), 0.0, muzzleVelocity * Math.Sin(elevation)),
                    Time = 0.0,
                    SpinRate = 0.0
                };

                double previousX = state.Position.X;
                double previousZ = state.Position.Z;

                for (int step = 0; step < 200_000; step++)
                {
                    var next = TrajectoryIntegrator.Step(state, projectile, atmosphere, options, timeStep);

                    if (next.Position.X >= zeroRange)
                    {
                        double span = next.Position.X - previousX;
                        double fraction = span > 1e-12 ? (zeroRange - previousX) / span : 0.0;
                        return previousZ + (next.Position.Z - previousZ) * fraction;
                    }

                    if (next.Speed < 1.0 || !next.Position.IsFinite)
                        return -1.0; // never got there: treat as low

                    previousX = next.Position.X;
                    previousZ = next.Position.Z;
                    state = next;
                }

                return -1.0;
            }
        }
    }
}
