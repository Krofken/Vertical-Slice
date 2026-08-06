using UnityEngine;

namespace Krofken.Ballistics.UnityIntegration
{
    /// <summary>
    /// Converts between the simulation frame and Unity's frame.
    ///
    /// THE SIMULATION FRAME is the standard aerodynamic one, right-handed:
    ///     +X downrange, +Y left, +Z up.
    /// It is used because that is the frame every ballistics text writes its
    /// equations in. Spin drift, Coriolis and yaw-of-repose terms all have
    /// well-known signs in it, and re-deriving them in a left-handed Y-up frame is a
    /// reliable way to get one of them backwards.
    ///
    /// UNITY'S FRAME is left-handed:
    ///     +X right, +Y up, +Z forward.
    ///
    /// The mapping is
    ///     unity.x = -sim.Y      (sim +Y is LEFT, Unity left is -X)
    ///     unity.y =  sim.Z      (up is up)
    ///     unity.z =  sim.X      (downrange is forward)
    ///
    /// Its determinant is -1, which is correct and intentional: mapping a
    /// right-handed frame into a left-handed one IS a reflection. Positions,
    /// velocities and accelerations all transform with the same matrix, so a
    /// crosswind that pushes the projectile to the shooter's left in the simulation
    /// pushes it to the viewer's left on screen.
    ///
    /// Precision changes here too: the simulation runs in double and Unity renders in
    /// float. That is deliberate -- accumulate in double where error compounds over
    /// thousands of steps, narrow to float only at the point of display.
    /// </summary>
    public static class BallisticsConversion
    {
        /// <summary>Simulation vector to Unity world vector.</summary>
        public static Vector3 ToUnity(in Vec3 v) => new Vector3(
            (float)(-v.Y),
            (float)v.Z,
            (float)v.X);

        /// <summary>Simulation vector to Unity, in double-backed components.
        /// Use when the caller needs the mapping without the precision loss.</summary>
        public static void ToUnity(in Vec3 v, out double x, out double y, out double z)
        {
            x = -v.Y;
            y = v.Z;
            z = v.X;
        }

        /// <summary>Unity world vector to simulation vector.</summary>
        public static Vec3 ToSimulation(in Vector3 v) => new Vec3(
            v.z,
            -v.x,
            v.y);

        /// <summary>
        /// Rotation that orients a projectile mesh (built pointing along its local
        /// +Z) along a simulation-space velocity.
        ///
        /// A stable projectile flies very nearly nose-first along its velocity vector,
        /// so aligning the mesh to velocity is correct to within the yaw of repose --
        /// a fraction of a degree, and far below what is visible. An UNSTABLE
        /// projectile does not, which is why <see cref="ProjectileSimulator"/> tumbles
        /// the mesh instead when the stability factor is below one.
        /// </summary>
        public static Quaternion LookAlongVelocity(in Vec3 velocity)
        {
            Vector3 forward = ToUnity(velocity);
            if (forward.sqrMagnitude < 1e-12f) return Quaternion.identity;
            return Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>Builds an <see cref="Atmosphere"/> from Unity-space wind.</summary>
        public static Atmosphere CreateAtmosphere(
            float temperatureCelsius,
            float pressurePascals,
            float relativeHumidity,
            Vector3 windUnitySpace)
            => Atmosphere.Create(
                Units.CelsiusToKelvin(temperatureCelsius),
                pressurePascals,
                relativeHumidity,
                ToSimulation(windUnitySpace));
    }
}
