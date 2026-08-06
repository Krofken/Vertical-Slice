using System;
using NUnit.Framework;
using UnityEngine;
using Krofken.Ballistics.UnityIntegration;

namespace Krofken.Ballistics.Tests
{
    /// <summary>
    /// The generalised lathe: <see cref="ProjectileMeshBuilder.BuildFromProfile"/>, which
    /// spins an arbitrary radius-vs-length curve rather than a projectile specifically.
    /// It draws the two things the gel block is made of — the wound cavity and the
    /// recovered slug.
    ///
    /// Winding is asserted rather than assumed, for the same reason as the projectile
    /// path: the ring-stitching order was derived on paper, and hand-derived sign
    /// conventions are wrong half the time and only surface as an inside-out model much
    /// later. The recovered slug is the sharper test of the two — its mushroom shoulder
    /// is two profile points at the SAME station, which lathes into a flat annulus and
    /// is precisely the case a naive meridian normal gets wrong.
    /// </summary>
    public class GelBlockMeshTests
    {
        private const double ImpactVelocity = 380.0;

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

        private static ProjectileGeometry HollowPointGeometry()
        {
            var g = ProjectileGeometry.Default9mmFmj;
            g.MeplatDiameter = 0.005;
            g.CavityDepth = 0.006;
            g.CavityMouthDiameter = 0.004;
            return g;
        }

        private static TerminalResult FireHollowPoint()
        {
            var d = Baseline();
            d.Projectile = HollowPointGeometry();
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal
            };

            var baked = CartridgeBaker.Bake(d, BarrelLibrary.ServicePistol9mm);
            return TerminalBallisticsSolver.Solve(
                baked.Terminal, TargetMediumLibrary.BareGelatinBlock(), ImpactVelocity);
        }

        private static ProfilePoint[] CavityProfile(out TerminalResult result)
        {
            result = FireHollowPoint();
            var gel = TargetMediumLibrary.Get(TargetMediumLibrary.Gelatin);
            return WoundCavity.Build(result, gel, result.MaxExpandedDiameter * 0.5);
        }

        private static ProfilePoint[] SlugProfile()
        {
            var geometry = HollowPointGeometry();
            var result = new TerminalResult
            {
                MaxExpandedDiameter = geometry.Calibre * 1.6,
                ExpansionRatio = 1.6,
                Fragmented = false
            };
            return RecoveredProjectile.Build(geometry, result);
        }

        // ------------------------------------------------------------------

        [Test]
        public void Cavity_Lathes_Into_A_Populated_Mesh()
        {
            var profile = CavityProfile(out var result);
            Assert.That(profile.Length, Is.GreaterThan(2), "expected a usable cavity profile");

            var mesh = ProjectileMeshBuilder.CreateFromProfile(profile, profile.Length);

            try
            {
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.triangles.Length, Is.GreaterThan(0));

                // The mesh runs along -Z from the entry face, so its extent along Z is
                // the penetration depth.
                Assert.That(mesh.bounds.size.z,
                    Is.EqualTo((float)result.PenetrationDepth).Within((float)result.PenetrationDepth * 0.05f),
                    "cavity length must match how deep the round actually went");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Cavity_Triangles_Are_Wound_To_Face_Outward()
        {
            var profile = CavityProfile(out _);
            var mesh = ProjectileMeshBuilder.CreateFromProfile(profile, profile.Length);

            try { AssertWoundOutward(mesh, "cavity"); }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Recovered_Slug_Triangles_Are_Wound_To_Face_Outward()
        {
            var profile = SlugProfile();
            Assert.That(profile.Length, Is.GreaterThan(2));

            var mesh = ProjectileMeshBuilder.CreateFromProfile(profile, profile.Length);

            try { AssertWoundOutward(mesh, "recovered slug"); }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Recovered_Slug_Mesh_Matches_Its_Profile()
        {
            var profile = SlugProfile();

            double widest = 0.0;
            foreach (var p in profile) if (p.OuterRadius > widest) widest = p.OuterRadius;
            double length = profile[profile.Length - 1].X - profile[0].X;

            var mesh = ProjectileMeshBuilder.CreateFromProfile(profile, profile.Length);

            try
            {
                var size = mesh.bounds.size;
                Assert.That(size.x, Is.EqualTo((float)(widest * 2.0)).Within((float)widest * 0.05f), "width");
                Assert.That(size.z, Is.EqualTo((float)length).Within((float)length * 0.05f), "length");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Lathed_Normals_Are_Unit_Length_And_Finite()
        {
            // The mushroom shoulder is a zero-length profile segment. If the meridian
            // normal divided by that length without guarding, this is where NaN appears.
            var mesh = ProjectileMeshBuilder.CreateFromProfile(SlugProfile(), SlugProfile().Length);

            try
            {
                var normals = mesh.normals;
                Assert.That(normals.Length, Is.GreaterThan(0));

                for (int i = 0; i < normals.Length; i++)
                {
                    Assert.That(float.IsNaN(normals[i].x) || float.IsNaN(normals[i].y) || float.IsNaN(normals[i].z),
                        Is.False, $"normal {i} is NaN");
                    Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(0.02f), $"normal {i}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Degenerate_Input_Yields_An_Empty_Mesh_Rather_Than_Throwing()
        {
            var flat = new[]
            {
                new ProfilePoint { X = 0.0, OuterRadius = 0.004 },
                new ProfilePoint { X = 0.0, OuterRadius = 0.004 }
            };

            var mesh = ProjectileMeshBuilder.CreateFromProfile(flat, flat.Length);

            try { Assert.That(mesh.vertexCount, Is.Zero, "a zero-length curve has no surface"); }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Asserts every non-degenerate triangle faces the way its vertex normals do.
        ///
        /// CAUTION: computed in double and normalised by hand. At 9 mm scale the face
        /// cross product is around 1e-8, and Unity's Vector3.normalized silently returns
        /// ZERO below kEpsilon (1e-5) — which would report a perfectly good mesh as
        /// entirely inside out.
        /// </summary>
        private static void AssertWoundOutward(Mesh mesh, string what)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;

            int inverted = 0, tested = 0;
            string firstFailure = null;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                Vector3 b = vertices[triangles[t + 1]];
                Vector3 c = vertices[triangles[t + 2]];

                double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
                double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;

                double fx = uy * vz - uz * vy;
                double fy = uz * vx - ux * vz;
                double fz = ux * vy - uy * vx;

                double faceMagnitude = Math.Sqrt(fx * fx + fy * fy + fz * fz);
                if (faceMagnitude < 1e-14) continue;    // the axis vertices of an end cap

                Vector3 average =
                    (normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]]) / 3f;
                double averageMagnitude = average.magnitude;
                if (averageMagnitude < 1e-6) continue;

                tested++;

                double cosine = (fx * average.x + fy * average.y + fz * average.z)
                                / (faceMagnitude * averageMagnitude);

                if (cosine < 0.05)
                {
                    inverted++;
                    firstFailure ??=
                        $"tri[{t / 3}] a={a:F6} b={b:F6} c={c:F6} " +
                        $"cross=({fx:E2},{fy:E2},{fz:E2}) cos={cosine:F4}";
                }
            }

            Assert.That(tested, Is.GreaterThan(50), $"{what}: too few non-degenerate triangles to be meaningful");
            Assert.That(inverted, Is.Zero, $"{what}: {inverted} of {tested} triangles are inside out. First: {firstFailure}");
        }
    }
}
