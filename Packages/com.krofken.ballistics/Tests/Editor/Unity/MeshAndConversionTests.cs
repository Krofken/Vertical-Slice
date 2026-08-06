using System;
using NUnit.Framework;
using UnityEngine;
using Krofken.Ballistics.UnityIntegration;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// Unity adapter layer: coordinate conversion and runtime mesh generation.
    ///
    /// These require UnityEngine and therefore only run in the Unity Test Runner.
    /// Everything else in this suite runs equally well outside the editor.
    ///
    /// The winding test is the important one. Triangle winding was derived by hand
    /// (working out that Unity treats a triangle as front-facing when
    /// Cross(b-a, c-a) points along the outward normal, then checking the ring
    /// stitching order against a cylinder). Hand-derived sign conventions are exactly
    /// the thing that is wrong 50% of the time and only shows up as an
    /// inside-out-looking model much later, so it is asserted rather than assumed.
    /// </summary>
    public class MeshAndConversionTests
    {
        private static ProjectileGeometry HollowPointWithBoattail()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            g.MeplatDiameter = 0.0035;
            g.CavityDepth = 0.005;
            g.CavityMouthDiameter = 0.0035;
            g.BoattailLength = 0.002;
            g.BoattailAngle = Units.DegreesToRadians(8.0);
            return g;
        }

        // ------------------------------------------------------------------
        // Coordinate conversion
        // ------------------------------------------------------------------

        [Test]
        public void Conversion_Round_Trips()
        {
            var original = new Vec3(10.0, 2.0, 3.0);
            var back = BallisticsConversion.ToSimulation(BallisticsConversion.ToUnity(original));

            Assert.That(back.X, Is.EqualTo(original.X).Within(1e-5));
            Assert.That(back.Y, Is.EqualTo(original.Y).Within(1e-5));
            Assert.That(back.Z, Is.EqualTo(original.Z).Within(1e-5));
        }

        [Test]
        public void Downrange_Maps_To_Unity_Forward()
        {
            var unity = BallisticsConversion.ToUnity(Vec3.Downrange);
            Assert.That(unity, Is.EqualTo(Vector3.forward).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void Simulation_Up_Maps_To_Unity_Up()
        {
            var unity = BallisticsConversion.ToUnity(Vec3.Up);
            Assert.That(unity, Is.EqualTo(Vector3.up).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void Simulation_Left_Maps_To_Unity_Left()
        {
            // Simulation +Y is the shooter's left. Facing Unity's +Z, left is -X.
            // If this inverts, every crosswind and spin drift pushes the wrong way.
            var unity = BallisticsConversion.ToUnity(new Vec3(0, 1, 0));
            Assert.That(unity.x, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(Mathf.Abs(unity.y), Is.LessThan(1e-5f));
            Assert.That(Mathf.Abs(unity.z), Is.LessThan(1e-5f));
        }

        [Test]
        public void Gravity_Points_Down_In_Unity_Space()
        {
            var aerodynamics = new ProjectileAerodynamics { Mass = 0.008, ReferenceArea = 6.4e-5 };
            var options = new TrajectoryOptions { Gravity = true, Drag = false };

            var acceleration = TrajectoryIntegrator.Acceleration(
                Vec3.Zero, 0.0, aerodynamics, Atmosphere.Standard, options, 0.0);

            Assert.That(BallisticsConversion.ToUnity(acceleration).y,
                Is.EqualTo(-PhysicalConstants.StandardGravity).Within(1e-4));
        }

        // ------------------------------------------------------------------
        // Mesh generation
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_Bounds_Match_The_Geometry()
        {
            var g = HollowPointWithBoattail();
            var mesh = ProjectileMeshBuilder.Create(g, 24, 24, g.OverallLength * 0.5);

            try
            {
                var size = mesh.bounds.size;
                Assert.That(size.x, Is.EqualTo(g.Calibre).Within(g.Calibre * 0.02), "width");
                Assert.That(size.y, Is.EqualTo(g.Calibre).Within(g.Calibre * 0.02), "height");
                Assert.That(size.z, Is.EqualTo(g.OverallLength).Within(g.OverallLength * 0.02), "length");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void All_Normals_Are_Unit_Length()
        {
            var mesh = ProjectileMeshBuilder.Create(HollowPointWithBoattail(), 24, 24, 0.0);

            try
            {
                var normals = mesh.normals;
                Assert.That(normals.Length, Is.GreaterThan(0));

                for (int i = 0; i < normals.Length; i++)
                    Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(0.02f), $"normal {i}");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Every_Triangle_Is_Wound_To_Face_Outward()
        {
            // Unity renders a triangle as front-facing when Cross(b-a, c-a) points
            // along the outward normal. This must hold for the outer surface, the
            // caps, AND the inward-facing cavity walls -- the cavity reverses both
            // its normals and its winding, so the same relationship survives.
            var mesh = ProjectileMeshBuilder.Create(HollowPointWithBoattail(), 24, 24, 0.0);

            try
            {
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var triangles = mesh.triangles;

                int inverted = 0;
                int tested = 0;
                string firstFailure = null;

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]];
                    Vector3 b = vertices[triangles[t + 1]];
                    Vector3 c = vertices[triangles[t + 2]];

                    // CAUTION: everything here is computed in double and normalised by
                    // hand. A 9mm projectile is about 13 mm long, so its triangle edges
                    // are tens of MICROmetres and the face cross product comes out
                    // around 1e-8. Unity's Vector3.normalized silently returns ZERO for
                    // any vector shorter than kEpsilon (1e-5), which would turn every
                    // comparison below into 0 and report a perfectly good mesh as
                    // entirely inside out. Real-world scale is small enough to fall off
                    // the end of Unity's float helpers.
                    double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
                    double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;

                    double fx = uy * vz - uz * vy;
                    double fy = uz * vx - ux * vz;
                    double fz = ux * vy - uy * vx;

                    double faceMagnitude = Math.Sqrt(fx * fx + fy * fy + fz * fz);
                    if (faceMagnitude < 1e-14) continue;   // degenerate, e.g. at a cone apex

                    Vector3 average = (normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]]) / 3f;
                    double averageMagnitude = average.magnitude;
                    if (averageMagnitude < 1e-6) continue;

                    tested++;

                    double cosine = (fx * average.x + fy * average.y + fz * average.z)
                                    / (faceMagnitude * averageMagnitude);

                    if (cosine < 0.05)
                    {
                        inverted++;
                        firstFailure ??=
                            $"tri[{t / 3}] idx=({triangles[t]},{triangles[t + 1]},{triangles[t + 2]}) " +
                            $"a={a:F6} b={b:F6} c={c:F6} " +
                            $"na={normals[triangles[t]]:F3} " +
                            $"cross=({fx:E2},{fy:E2},{fz:E2}) cos={cosine:F4}";
                    }
                }

                Assert.That(tested, Is.GreaterThan(100), "not enough non-degenerate triangles to be meaningful");
                Assert.That(inverted, Is.Zero, $"{inverted} of {tested} triangles are inside out. First: {firstFailure}");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Bearing_Surface_Normals_Point_Straight_Out()
        {
            // On the full-calibre cylindrical section the surface normal must be
            // purely radial. Anything else means the meridian normal is wrong.
            var g = HollowPointWithBoattail();
            var mesh = ProjectileMeshBuilder.Create(g, 24, 24, 0.0);

            try
            {
                var vertices = mesh.vertices;
                var normals = mesh.normals;

                int tested = 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var radial = new Vector2(vertices[i].x, vertices[i].y);
                    if (radial.magnitude < (float)g.Radius * 0.999f) continue;

                    tested++;
                    var normalRadial = new Vector2(normals[i].x, normals[i].y);
                    Assert.That(Vector2.Dot(radial.normalized, normalRadial.normalized),
                        Is.GreaterThan(0.9f), $"vertex {i} normal is not radial");
                }

                Assert.That(tested, Is.GreaterThan(10), "no full-calibre vertices found");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void A_Hollow_Point_Generates_More_Geometry_Than_A_Solid()
        {
            var solid = ProjectileGeometry.Default9mmFmj;
            var hollow = HollowPointWithBoattail();

            var solidMesh = ProjectileMeshBuilder.Create(solid, 16, 16, 0.0);
            var hollowMesh = ProjectileMeshBuilder.Create(hollow, 16, 16, 0.0);

            try
            {
                Assert.That(hollowMesh.vertexCount, Is.GreaterThan(solidMesh.vertexCount),
                    "the cavity interior should add geometry");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(solidMesh);
                UnityEngine.Object.DestroyImmediate(hollowMesh);
            }
        }

        [Test]
        public void Rebuilding_Into_An_Existing_Mesh_Replaces_Rather_Than_Appends()
        {
            // The design UI rebuilds this on every slider frame. If it accumulated,
            // memory would climb without bound.
            var mesh = new Mesh();

            try
            {
                var g = ProjectileGeometry.Default9mmFmj;
                ProjectileMeshBuilder.Build(g, mesh, 16, 16);
                int first = mesh.vertexCount;

                ProjectileMeshBuilder.Build(g, mesh, 16, 16);
                Assert.That(mesh.vertexCount, Is.EqualTo(first));

                g.BearingSurfaceLength *= 2.0;
                ProjectileMeshBuilder.Build(g, mesh, 16, 16);
                Assert.That(mesh.bounds.size.z, Is.GreaterThan(0.0f));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void The_Pivot_Offset_Positions_The_Mesh()
        {
            var g = ProjectileGeometry.Default9mmFmj;

            var atTip = ProjectileMeshBuilder.Create(g, 12, 12, 0.0);
            var atCentre = ProjectileMeshBuilder.Create(g, 12, 12, g.OverallLength * 0.5);

            try
            {
                // Pivot at the tip puts the whole body behind the origin.
                Assert.That(atTip.bounds.max.z, Is.LessThan(1e-5f));

                // Pivot at the centre straddles it.
                Assert.That(atCentre.bounds.center.z, Is.EqualTo(0f).Within((float)g.OverallLength * 0.05f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atTip);
                UnityEngine.Object.DestroyImmediate(atCentre);
            }
        }
    }

    /// <summary>Tolerant Vector3 comparison for NUnit's <c>Using</c> clause.</summary>
    internal sealed class Vector3EqualityComparer : System.Collections.Generic.IEqualityComparer<Vector3>
    {
        public static readonly Vector3EqualityComparer Instance = new Vector3EqualityComparer();
        public bool Equals(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-8f;
        public int GetHashCode(Vector3 v) => v.GetHashCode();
    }
}
