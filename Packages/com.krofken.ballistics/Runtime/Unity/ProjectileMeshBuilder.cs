using System;
using System.Collections.Generic;
using UnityEngine;

namespace Krofken.Ballistics.UnityIntegration
{
    /// <summary>
    /// Lathes a <see cref="ProjectileGeometry"/> into a Unity mesh at runtime.
    ///
    /// THIS IS THE ANSWER TO "we would need a 3D model for every bullet".
    /// We would not. A projectile is a solid of revolution: sample its profile
    /// curve, spin the curve around the axis, done. Any calibre, any ogive, any
    /// meplat, any boattail, any cavity the player dials in — one code path, no
    /// authored assets, and it regenerates fast enough to run live on a slider drag.
    ///
    /// Critically, it revolves the SAME <see cref="ProjectileGeometry.RadiusAt"/> that
    /// the mass integrator and the drag model read. The mesh cannot drift away from
    /// the simulated shape, because they are the same function.
    ///
    /// The mesh is built pointing along local +Z, with the pivot placed wherever the
    /// caller asks — pass the centre of gravity and the projectile will spin and
    /// tumble about the correct point.
    ///
    /// Parts generated:
    ///     outer surface      the silhouette
    ///     tip cap            annulus between the cavity mouth and the meplat
    ///     nose cavity        the inside of a hollow point, inward-facing
    ///     base cap           annulus between the base cavity and the base
    ///     base cavity        the inside of a hollow base, inward-facing
    ///
    /// ALLOCATION: reuses caller-supplied buffers and writes into an existing Mesh.
    /// Rebuilding while the player drags a slider produces no garbage after the first
    /// call.
    /// </summary>
    public static class ProjectileMeshBuilder
    {
        /// <summary>Reusable scratch buffers. Not thread-safe; call from the main thread
        /// or give each thread its own instance.</summary>
        public sealed class Buffers
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(2048);
            public readonly List<Vector3> Normals = new List<Vector3>(2048);
            public readonly List<Vector2> Uvs = new List<Vector2>(2048);
            public readonly List<int> Triangles = new List<int>(6144);
            public ProfilePoint[] Profile = new ProfilePoint[128];

            public void Clear()
            {
                Vertices.Clear();
                Normals.Clear();
                Uvs.Clear();
                Triangles.Clear();
            }

            public void EnsureProfileCapacity(int required)
            {
                if (Profile.Length < required) Profile = new ProfilePoint[required];
            }
        }

        private static Buffers _shared;

        /// <summary>
        /// Builds (or rebuilds) the mesh for a projectile.
        /// </summary>
        /// <param name="geometry">Shape to lathe.</param>
        /// <param name="mesh">Existing mesh to overwrite. Never null.</param>
        /// <param name="radialSegments">Segments around the axis. 24 is smooth in the
        /// hand, 12 is plenty for a projectile in flight.</param>
        /// <param name="noseSegments">Subdivisions along the ogive curve.</param>
        /// <param name="pivotFromTip">Where the local origin sits, measured from the
        /// tip in metres. Pass the centre of gravity for correct rotation.</param>
        /// <param name="buffers">Optional scratch buffers to reuse.</param>
        public static void Build(
            in ProjectileGeometry geometry,
            Mesh mesh,
            int radialSegments = 24,
            int noseSegments = 24,
            double pivotFromTip = 0.0,
            Buffers buffers = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (radialSegments < 3) radialSegments = 3;
            if (noseSegments < 1) noseSegments = 1;

            var b = buffers ?? (_shared ??= new Buffers());
            b.Clear();

            int capacity = ProfileSampler.RequiredCapacity(noseSegments, 2);
            b.EnsureProfileCapacity(capacity);
            int pointCount = ProfileSampler.Sample(geometry, b.Profile, noseSegments, 2);

            double length = geometry.OverallLength;
            if (pointCount < 2 || length <= 0.0)
            {
                mesh.Clear();
                return;
            }

            var context = new LatheContext(b, radialSegments, pivotFromTip);

            // ---- Outer surface ------------------------------------------------
            int previousRing = -1;
            for (int i = 0; i < pointCount; i++)
            {
                var point = b.Profile[i];
                MeridianNormal(b.Profile, pointCount, i, out double axial, out double radial);

                int ring = context.AddRing(
                    point.OuterRadius, point.X, axial, radial,
                    (float)(point.X / length));

                if (previousRing >= 0) context.Stitch(previousRing, ring, inward: false);
                previousRing = ring;
            }

            double meplatRadius = geometry.MeplatRadius;
            double cavityMouthRadius = geometry.IsHollowPoint ? geometry.CavityMouthDiameter * 0.5 : 0.0;

            // ---- Tip cap -------------------------------------------------------
            // The flat annulus of the meplat, facing forward (+Z). Degenerates to a
            // disc when there is no cavity, and vanishes when there is no meplat.
            if (meplatRadius > 1e-6)
            {
                int inner = context.AddRing(cavityMouthRadius, 0.0, -1.0, 0.0, 0.0f);
                int outer = context.AddRing(meplatRadius, 0.0, -1.0, 0.0, 0.02f);
                context.Stitch(inner, outer, inward: false);
            }

            // ---- Nose cavity ---------------------------------------------------
            // The inside of a hollow point: a cone from the mouth down to a point,
            // with normals facing inward and reversed winding so it is visible from
            // outside the mouth.
            if (geometry.IsHollowPoint)
            {
                const int cavitySegments = 8;
                int previousCavityRing = -1;

                for (int i = 0; i <= cavitySegments; i++)
                {
                    double t = (double)i / cavitySegments;
                    double x = t * geometry.CavityDepth;
                    double r = cavityMouthRadius * (1.0 - t);

                    // The cavity wall is a straight cone, so its meridian normal is
                    // constant: tangent (depth, -mouth) gives inward normal
                    // (-mouth, -depth) before normalisation.
                    double dx = geometry.CavityDepth;
                    double dr = -cavityMouthRadius;
                    double len = Math.Sqrt(dx * dx + dr * dr);
                    double axial = len > 0 ? dr / len : 0.0;
                    double radial = len > 0 ? -dx / len : -1.0;

                    int ring = context.AddRing(r, x, axial, radial, (float)t);
                    if (previousCavityRing >= 0) context.Stitch(previousCavityRing, ring, inward: true);
                    previousCavityRing = ring;
                }
            }

            // ---- Base cap ------------------------------------------------------
            double baseRadius = geometry.BaseDiameter * 0.5;
            double baseCavityRadius = geometry.BaseCavityDepth > 0.0
                ? geometry.BaseCavityDiameter * 0.5
                : 0.0;

            if (baseRadius > 1e-6)
            {
                // Facing backward (-Z), so the rings are stitched outer-to-inner.
                int outer = context.AddRing(baseRadius, length, 1.0, 0.0, 1.0f);
                int inner = context.AddRing(baseCavityRadius, length, 1.0, 0.0, 0.98f);
                context.Stitch(outer, inner, inward: false);
            }

            // ---- Base cavity ---------------------------------------------------
            if (geometry.BaseCavityDepth > 0.0 && baseCavityRadius > 1e-6)
            {
                const int cavitySegments = 6;
                int previousCavityRing = -1;
                double from = length - geometry.BaseCavityDepth;

                for (int i = 0; i <= cavitySegments; i++)
                {
                    double t = (double)i / cavitySegments;
                    double x = from + t * geometry.BaseCavityDepth;
                    double r = baseCavityRadius * t;

                    double dx = geometry.BaseCavityDepth;
                    double dr = baseCavityRadius;
                    double len = Math.Sqrt(dx * dx + dr * dr);
                    double axial = len > 0 ? dr / len : 0.0;
                    double radial = len > 0 ? -dx / len : -1.0;

                    int ring = context.AddRing(r, x, axial, radial, (float)t);
                    if (previousCavityRing >= 0) context.Stitch(previousCavityRing, ring, inward: true);
                    previousCavityRing = ring;
                }
            }

            Upload(mesh, b);
        }

        /// <summary>Pushes the scratch buffers into a mesh.</summary>
        private static void Upload(Mesh mesh, Buffers b)
        {
            mesh.Clear();

            // Projectiles are small meshes, but a heavily subdivided design preview
            // can pass the 16-bit index limit. Cheap to guard, expensive to debug.
            mesh.indexFormat = b.Vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(b.Vertices);
            mesh.SetNormals(b.Normals);
            mesh.SetUVs(0, b.Uvs);
            mesh.SetTriangles(b.Triangles, 0, true);
            mesh.RecalculateBounds();
        }

        /// <summary>Allocates a new mesh for a projectile. Prefer the overload that
        /// reuses an existing mesh when rebuilding repeatedly.</summary>
        public static Mesh Create(
            in ProjectileGeometry geometry,
            int radialSegments = 24,
            int noseSegments = 24,
            double pivotFromTip = 0.0)
        {
            var mesh = new Mesh { name = "Projectile" };
            Build(geometry, mesh, radialSegments, noseSegments, pivotFromTip);
            return mesh;
        }

        /// <summary>
        /// Lathes an ARBITRARY radius-vs-length curve, not just a projectile.
        ///
        /// The projectile path above is the special case that knows about meplats and
        /// cavities. This one takes any <see cref="ProfilePoint"/> polyline and spins
        /// it, which is what lets the same code draw things a bullet is not:
        ///
        ///   - the wound cavity, from <see cref="WoundCavity.Build"/>
        ///   - the recovered mushroomed slug, from <see cref="RecoveredProjectile.Build"/>
        ///
        /// Points must be ordered by increasing <see cref="ProfilePoint.X"/>. Repeated
        /// stations are allowed and are how a crisp shoulder is expressed: two points
        /// at the same X with different radii lathe into a flat annulus, which is
        /// exactly the edge a recovered bullet has where its mushroom meets the shank.
        /// </summary>
        /// <param name="profile">Radius-vs-length curve, ascending in X.</param>
        /// <param name="count">Points to read from <paramref name="profile"/>.</param>
        /// <param name="mesh">Existing mesh to overwrite. Never null.</param>
        /// <param name="radialSegments">Segments around the axis.</param>
        /// <param name="pivotFromStart">Where the local origin sits along the curve, m.</param>
        /// <param name="inward">Face the surface inwards, for a hole seen from inside
        /// it. The default builds a solid, which is what the gel block wants: the
        /// cavity is rendered as a solid shape suspended in a transparent block.</param>
        /// <param name="capEnds">Close both ends with flat discs. Leave on for a solid;
        /// turn off when the curve already starts and ends at zero radius.</param>
        /// <param name="buffers">Optional scratch buffers to reuse.</param>
        public static void BuildFromProfile(
            ProfilePoint[] profile,
            int count,
            Mesh mesh,
            int radialSegments = 24,
            double pivotFromStart = 0.0,
            bool inward = false,
            bool capEnds = true,
            Buffers buffers = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (radialSegments < 3) radialSegments = 3;

            if (count < 2 || count > profile.Length)
            {
                mesh.Clear();
                return;
            }

            double span = profile[count - 1].X - profile[0].X;
            if (span <= 0.0)
            {
                mesh.Clear();
                return;
            }

            var b = buffers ?? (_shared ??= new Buffers());
            b.Clear();

            var context = new LatheContext(b, radialSegments, pivotFromStart);
            double start = profile[0].X;

            // ---- Surface --------------------------------------------------------
            int previousRing = -1;
            for (int i = 0; i < count; i++)
            {
                MeridianNormal(profile, count, i, out double axial, out double radial);

                // An inward-facing wall is the same surface with its normal reversed;
                // the winding is flipped separately, by Stitch.
                if (inward) { axial = -axial; radial = -radial; }

                int ring = context.AddRing(
                    profile[i].OuterRadius, profile[i].X, axial, radial,
                    (float)((profile[i].X - start) / span));

                if (previousRing >= 0) context.Stitch(previousRing, ring, inward);
                previousRing = ring;
            }

            // ---- End caps -------------------------------------------------------
            // Flat discs closing a solid. Degenerate to nothing when the curve already
            // tapers to a point, since a zero-radius disc has no area.
            if (capEnds && !inward)
            {
                double startRadius = profile[0].OuterRadius;
                if (startRadius > 1e-9)
                {
                    // Faces along -x, which is +Z in local space.
                    int inner = context.AddRing(0.0, profile[0].X, -1.0, 0.0, 0.0f);
                    int outer = context.AddRing(startRadius, profile[0].X, -1.0, 0.0, 0.0f);
                    context.Stitch(inner, outer, inward: false);
                }

                double endRadius = profile[count - 1].OuterRadius;
                if (endRadius > 1e-9)
                {
                    // Faces along +x, which is -Z in local space, so the rings are
                    // stitched outer-to-inner instead.
                    int outer = context.AddRing(endRadius, profile[count - 1].X, 1.0, 0.0, 1.0f);
                    int inner = context.AddRing(0.0, profile[count - 1].X, 1.0, 0.0, 1.0f);
                    context.Stitch(outer, inner, inward: false);
                }
            }

            Upload(mesh, b);
        }

        /// <summary>Allocating convenience overload for a one-off curve.</summary>
        public static Mesh CreateFromProfile(
            ProfilePoint[] profile,
            int count,
            string name = "Lathed",
            int radialSegments = 24,
            double pivotFromStart = 0.0,
            bool inward = false,
            bool capEnds = true)
        {
            var mesh = new Mesh { name = name };
            BuildFromProfile(profile, count, mesh, radialSegments, pivotFromStart, inward, capEnds);
            return mesh;
        }

        /// <summary>
        /// Outward normal in the meridian (axial, radial) plane at a profile point,
        /// by central difference.
        ///
        /// For a profile tangent (dx, dr) the outward normal is (-dr, dx): on a
        /// cylinder that gives (0, 1), purely radial, and on an expanding nose it
        /// tilts forward towards the tip, which is correct.
        /// </summary>
        private static void MeridianNormal(
            ProfilePoint[] profile, int count, int index,
            out double axial, out double radial)
        {
            int previous = index > 0 ? index - 1 : index;
            int next = index < count - 1 ? index + 1 : index;

            double dx = profile[next].X - profile[previous].X;
            double dr = profile[next].OuterRadius - profile[previous].OuterRadius;

            double length = Math.Sqrt(dx * dx + dr * dr);
            if (length < 1e-12)
            {
                axial = 0.0;
                radial = 1.0;
                return;
            }

            axial = -dr / length;
            radial = dx / length;
        }

        /// <summary>Ring emission and stitching against the shared buffers.</summary>
        private readonly struct LatheContext
        {
            private readonly Buffers _buffers;
            private readonly int _radialSegments;
            private readonly double _pivot;

            public LatheContext(Buffers buffers, int radialSegments, double pivot)
            {
                _buffers = buffers;
                _radialSegments = radialSegments;
                _pivot = pivot;
            }

            /// <summary>
            /// Emits one ring of vertices and returns its start index.
            /// Emits radialSegments + 1 vertices: the duplicate at the seam carries
            /// u = 1 instead of u = 0, without which the texture mirrors across the
            /// wrap.
            /// </summary>
            public int AddRing(double radius, double x, double normalAxial, double normalRadial, float v)
            {
                int start = _buffers.Vertices.Count;
                float z = (float)(_pivot - x);

                for (int j = 0; j <= _radialSegments; j++)
                {
                    double t = (double)j / _radialSegments;
                    double angle = t * Math.PI * 2.0;
                    double cos = Math.Cos(angle);
                    double sin = Math.Sin(angle);

                    _buffers.Vertices.Add(new Vector3(
                        (float)(radius * cos),
                        (float)(radius * sin),
                        z));

                    // The axial component flips sign because local +Z runs from the
                    // base towards the tip, opposite to the profile's x.
                    _buffers.Normals.Add(new Vector3(
                        (float)(normalRadial * cos),
                        (float)(normalRadial * sin),
                        (float)(-normalAxial)));

                    _buffers.Uvs.Add(new Vector2((float)t, v));
                }

                return start;
            }

            /// <summary>
            /// Joins two rings with triangles.
            ///
            /// Winding: Unity treats a triangle as front-facing when the vertices run
            /// clockwise as seen from the front, which is equivalent to
            /// Cross(b - a, c - a) pointing along the outward normal. For rings where
            /// <paramref name="ringA"/> is nearer the tip, the order
            /// (a, b, c) = (A_j, B_j, A_j+1) satisfies that -- worked through for a
            /// cylinder, where it yields a purely radial outward face normal.
            /// Inward-facing surfaces (cavity walls) swap b and c.
            /// </summary>
            public void Stitch(int ringA, int ringB, bool inward)
            {
                var triangles = _buffers.Triangles;

                for (int j = 0; j < _radialSegments; j++)
                {
                    int a = ringA + j;
                    int b = ringB + j;
                    int c = ringA + j + 1;
                    int d = ringB + j + 1;

                    if (!inward)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(c); triangles.Add(b); triangles.Add(d);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(c); triangles.Add(d); triangles.Add(b);
                    }
                }
            }
        }
    }
}
