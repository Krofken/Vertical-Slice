using System;
using System.Runtime.CompilerServices;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Double-precision 3D vector.
    ///
    /// WHY NOT Vector3 / float3:
    ///   1. This package has zero dependencies by design -- it must compile and run
    ///      outside Unity (headless server, unit-test runner, offline validation tool).
    ///   2. Trajectory integration accumulates error over thousands of steps. float32
    ///      carries ~7 significant digits; a 1 km flight resolved to millimetres needs
    ///      6 digits before you've even started, so rounding shows up as visible drift.
    ///
    /// Layout is blittable and sequential, so this is Burst-compatible and can be
    /// memcpy'd into a NativeArray on the Unity side without marshalling.
    ///
    /// Axis convention used throughout the library (right-handed, Z-up):
    ///     +X  downrange
    ///     +Y  left
    ///     +Z  up   (gravity is -Z)
    /// The Unity adapter layer converts to Unity's left-handed Y-up space.
    /// Keeping the physics in a standard aerodynamic frame avoids sign confusion
    /// in the spin-drift and Coriolis terms, which are easy to get backwards.
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public double X;
        public double Y;
        public double Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Vec3(0, 0, 0);
        }

        /// <summary>Unit vector downrange.</summary>
        public static Vec3 Downrange
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Vec3(1, 0, 0);
        }

        /// <summary>Unit vector up (gravity acts along the negative of this).</summary>
        public static Vec3 Up
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Vec3(0, 0, 1);
        }

        // ---- Arithmetic ---------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        // ---- Products and norms -------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        /// <summary>Squared magnitude. Prefer this over <see cref="Magnitude"/> for
        /// comparisons -- it avoids a square root in the integrator's inner loop.</summary>
        public double SqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => X * X + Y * Y + Z * Z;
        }

        public double Magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Math.Sqrt(X * X + Y * Y + Z * Z);
        }

        /// <summary>
        /// Returns the unit vector, or Zero if the vector is degenerate.
        /// Returning Zero rather than NaN matters: a projectile that has come to
        /// rest has no velocity direction, and a NaN there silently poisons the
        /// entire trajectory downstream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 Normalized()
        {
            double m2 = SqrMagnitude;
            if (m2 <= 1e-30) return Zero;
            double inv = 1.0 / Math.Sqrt(m2);
            return new Vec3(X * inv, Y * inv, Z * inv);
        }

        /// <summary>Component-wise linear interpolation. <paramref name="t"/> is not clamped.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => new Vec3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);

        public static double Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        /// <summary>True if any component is NaN or infinite. Used by solver guards.</summary>
        public bool IsFinite =>
            !double.IsNaN(X) && !double.IsInfinity(X) &&
            !double.IsNaN(Y) && !double.IsInfinity(Y) &&
            !double.IsNaN(Z) && !double.IsInfinity(Z);

        // ---- Equality / formatting ----------------------------------------

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X.GetHashCode();
                h = (h * 397) ^ Y.GetHashCode();
                h = (h * 397) ^ Z.GetHashCode();
                return h;
            }
        }

        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }
}
