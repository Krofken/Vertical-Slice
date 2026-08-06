using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Builds the shape of a projectile AFTER impact -- the mushroomed slug the player
    /// digs out of the block and sets on the bench next to the last one.
    ///
    /// WHY THIS EXISTS: <see cref="TerminalResult.ExpansionRatio"/> is a number, and
    /// numbers are banned at the range. The recovered bullet is the physical form of
    /// that number, and it is strictly better: it persists, it sits next to shot 4 so
    /// the player can see that shot 7 opened wider, and it cannot be memorised into a
    /// lookup table.
    ///
    /// PHYSICS -- volume conservation, which is the whole model.
    ///
    /// Upset is plastic flow, and plastic flow conserves volume: the nose does not
    /// gain or lose metal, it just stops being nose-shaped. So the nose collapses
    /// rearward into a flat head of the expanded radius R, and the length of that head
    /// follows directly:
    ///
    ///     V_nose = integral over the nose of pi * ( r_outer(x)^2 - r_cavity(x)^2 ) dx
    ///     L_head = V_nose / ( pi * R^2 )
    ///
    /// Because R is larger than any radius the nose had, L_head is shorter than the
    /// nose was -- the recovered bullet is stubbier than the one that was loaded,
    /// which is exactly what a real recovered bullet looks like.
    ///
    /// A hollow point's cavity closes during upset, so the head is emitted solid. The
    /// shank behind the nose is untouched and keeps its own geometry, including a base
    /// cavity if it had one.
    ///
    /// The mass therefore comes out unchanged, and
    /// <c>SolidVolume</c> on the returned polyline is asserted against the original in
    /// the test suite. A recovered bullet that weighed less than the one loaded would
    /// be a bug you could see on the bench scale.
    /// </summary>
    public static class RecoveredProjectile
    {
        /// <summary>Slices used to integrate the nose volume. The nose is the only
        /// curved part; everything downstream of it is straight and integrates
        /// exactly.</summary>
        private const int NoseSlices = 1024;

        /// <summary>
        /// Points needed for the worst case at the given segment counts. Sized for
        /// whichever path runs: a mushroom needs only a handful of points, but a round
        /// that came back intact is sampled at full nose resolution.
        /// </summary>
        public static int RequiredCapacity(int noseSegments = 24, int tailSegments = 2)
            => Math.Max(
                ProfileSampler.RequiredCapacity(noseSegments, tailSegments),
                Math.Max(tailSegments, 1) + 6);

        /// <summary>
        /// Builds the recovered shape as a radius-vs-length polyline, tip to base.
        ///
        /// Returns the number of points written, or 0 if the projectile
        /// <see cref="TerminalResult.Fragmented"/> -- there is no single recovered
        /// bullet in that case, and the player gets a tray of pieces instead.
        /// </summary>
        /// <param name="original">Geometry as loaded.</param>
        /// <param name="result">What the impact did to it.</param>
        /// <param name="buffer">Destination, at least <see cref="RequiredCapacity"/>.</param>
        /// <param name="noseSegments">Subdivisions along the nose, used only when the
        /// round came back undeformed and its original ogive has to be resampled. A
        /// mushroom's flat head needs no subdivision at all.</param>
        /// <param name="tailSegments">Subdivisions along the boattail. It is a straight
        /// taper, so 1 is geometrically exact.</param>
        public static int Build(
            in ProjectileGeometry original,
            in TerminalResult result,
            ProfilePoint[] buffer,
            int noseSegments = 24,
            int tailSegments = 2)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (noseSegments < 1) noseSegments = 1;
            if (tailSegments < 1) tailSegments = 1;

            // It came apart. There is nothing single to recover.
            if (result.Fragmented) return 0;

            int required = RequiredCapacity(noseSegments, tailSegments);
            if (buffer.Length < required)
                throw new ArgumentException(
                    $"Buffer too small: need {required} points, got {buffer.Length}.", nameof(buffer));

            double shankRadius = original.Radius;
            double headRadius = result.MaxExpandedDiameter * 0.5;

            // Anything that did not open past its own shank has no visible mushroom --
            // a full-metal-jacket or a hardened core comes back looking as it went in.
            if (headRadius <= shankRadius * (1.0 + 1e-9))
                return CopyUndeformed(original, buffer, noseSegments, tailSegments);

            double noseLength = original.NoseLength;
            double noseVolume = SolidVolume(original, 0.0, noseLength, NoseSlices);

            // A degenerate nose (a wadcutter has none) has no metal to flow forward.
            if (noseVolume <= 0.0 || noseLength <= 0.0)
                return CopyUndeformed(original, buffer, noseSegments, tailSegments);

            double headLength = noseVolume / (Math.PI * headRadius * headRadius);

            int count = 0;

            // ---- Mushroom head -------------------------------------------------
            // A flat face of the expanded radius, then a crisp shoulder back down to
            // the shank. Two points at the same station give the shoulder a hard edge,
            // which is what the lathe needs to produce a flat annulus rather than a
            // smeared cone -- and a real recovered bullet does have that edge.
            buffer[count++] = new ProfilePoint { X = 0.0, OuterRadius = headRadius };
            buffer[count++] = new ProfilePoint { X = headLength, OuterRadius = headRadius };

            // ---- Shank, unchanged ----------------------------------------------
            // The body behind the nose is carried across verbatim, shifted forward by
            // however much the nose shortened.
            double shift = headLength - noseLength;
            double boattailStart = noseLength + original.BearingSurfaceLength;
            double total = original.OverallLength;

            EmitBody(original, buffer, ref count, noseLength, shift);

            if (original.BearingSurfaceLength > 0.0)
                EmitBody(original, buffer, ref count, boattailStart, shift);

            if (original.BoattailLength > 0.0)
            {
                for (int i = 1; i <= tailSegments; i++)
                {
                    double t = (double)i / tailSegments;
                    EmitBody(original, buffer, ref count, boattailStart + t * original.BoattailLength, shift);
                }
            }

            if (count == 0 || buffer[count - 1].X < total + shift - 1e-12)
                EmitBody(original, buffer, ref count, total, shift);

            return count;
        }

        /// <summary>Allocating convenience overload. Not for per-shot use in a loop.</summary>
        public static ProfilePoint[] Build(
            in ProjectileGeometry original,
            in TerminalResult result,
            int noseSegments = 24,
            int tailSegments = 2)
        {
            var buffer = new ProfilePoint[RequiredCapacity(noseSegments, tailSegments)];
            int n = Build(original, result, buffer, noseSegments, tailSegments);
            var trimmed = new ProfilePoint[n];
            Array.Copy(buffer, trimmed, n);
            return trimmed;
        }

        /// <summary>
        /// Solid volume of a radius-vs-length polyline, m^3.
        ///
        /// Each segment is a conical frustum, whose volume
        ///
        ///     V = (pi/3) * h * ( r1^2 + r1*r2 + r2^2 )
        ///
        /// is EXACT for a straight-sided segment -- so for a polyline this is not an
        /// approximation at all. Any cavity carried in
        /// <see cref="ProfilePoint.InnerRadius"/> is subtracted the same way.
        ///
        /// Multiply by density to weigh the thing on the bench.
        /// </summary>
        public static double SolidVolume(ProfilePoint[] profile, int count)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (count < 2) return 0.0;

            const double third = Math.PI / 3.0;
            double volume = 0.0;

            for (int i = 0; i < count - 1; i++)
            {
                double h = profile[i + 1].X - profile[i].X;
                if (h <= 0.0) continue;          // shoulder: zero length, zero volume

                double a = profile[i].OuterRadius;
                double b = profile[i + 1].OuterRadius;
                volume += third * h * (a * a + a * b + b * b);

                double ca = profile[i].InnerRadius;
                double cb = profile[i + 1].InnerRadius;
                if (ca > 0.0 || cb > 0.0)
                    volume -= third * h * (ca * ca + ca * cb + cb * cb);
            }

            return volume;
        }

        /// <summary>
        /// Solid volume of a slice of the original geometry, m^3, by the midpoint rule
        /// over pi * ( r_outer^2 - r_cavity^2 ). Used for the nose, which is the one
        /// curved region.
        /// </summary>
        private static double SolidVolume(in ProjectileGeometry geometry, double from, double to, int slices)
        {
            if (to <= from || slices < 1) return 0.0;

            double step = (to - from) / slices;
            double sum = 0.0;

            for (int i = 0; i < slices; i++)
            {
                double x = from + (i + 0.5) * step;
                double outer = geometry.RadiusAt(x);
                double inner = geometry.CavityRadiusAt(x);
                sum += outer * outer - inner * inner;
            }

            return Math.PI * sum * step;
        }

        private static void EmitBody(
            in ProjectileGeometry geometry, ProfilePoint[] buffer, ref int count, double x, double shift)
        {
            double station = x + shift;

            // Collapse duplicates, which appear whenever a section has zero length.
            if (count > 0 && Math.Abs(buffer[count - 1].X - station) < 1e-12
                          && Math.Abs(buffer[count - 1].OuterRadius - geometry.RadiusAt(x)) < 1e-12)
                return;

            buffer[count++] = new ProfilePoint
            {
                X = station,
                OuterRadius = geometry.RadiusAt(x),
                InnerRadius = geometry.CavityRadiusAt(x)
            };
        }

        /// <summary>Samples the original shape unchanged, for rounds that came back
        /// intact.</summary>
        private static int CopyUndeformed(
            in ProjectileGeometry geometry, ProfilePoint[] buffer, int noseSegments, int tailSegments)
            => ProfileSampler.Sample(geometry, buffer, noseSegments, tailSegments);
    }
}
