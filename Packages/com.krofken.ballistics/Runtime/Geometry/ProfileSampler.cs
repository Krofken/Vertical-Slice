using System;

namespace Krofken.Ballistics
{
    /// <summary>One station along the projectile's axis.</summary>
    [Serializable]
    public struct ProfilePoint
    {
        /// <summary>Axial position from the tip, m.</summary>
        public double X;

        /// <summary>Outer surface radius at this station, m.</summary>
        public double OuterRadius;

        /// <summary>Cavity radius at this station, m. Zero where solid.</summary>
        public double InnerRadius;
    }

    /// <summary>
    /// Turns a <see cref="ProjectileGeometry"/> into a polyline that can be revolved
    /// into a mesh.
    ///
    /// This is the single source of truth for the projectile's silhouette. The Unity
    /// mesh generator revolves exactly these points, and the drag and mass solvers
    /// read the same <see cref="ProjectileGeometry.RadiusAt"/> the sampler does -- so
    /// what the player sees is provably the shape that was simulated.
    ///
    /// Sampling is non-uniform on purpose. The nose is a curve and needs resolution;
    /// the bearing surface is a straight cylinder and needs exactly two points. Feature
    /// boundaries (meplat edge, shoulder, boattail junction, base) are always emitted
    /// exactly so the resulting mesh has crisp edges where the real bullet has crisp
    /// edges, rather than a rounded-off approximation.
    ///
    /// Fills a caller-supplied buffer and returns the count written -- no allocation,
    /// so this can be called on a slider drag without generating garbage.
    /// </summary>
    public static class ProfileSampler
    {
        /// <summary>Points needed for the worst case at the given segment counts.
        /// Use this to size a reusable buffer once.</summary>
        public static int RequiredCapacity(int noseSegments, int tailSegments)
            => Math.Max(noseSegments, 1) + Math.Max(tailSegments, 1) + 8;

        /// <summary>
        /// Samples the profile from tip to base.
        /// </summary>
        /// <param name="geometry">Shape to sample.</param>
        /// <param name="buffer">Destination, sized at least <see cref="RequiredCapacity"/>.</param>
        /// <param name="noseSegments">Subdivisions along the ogive. 24-48 is smooth
        /// at inspection distance; 12 is fine for a distant projectile.</param>
        /// <param name="tailSegments">Subdivisions along the boattail. The boattail is
        /// a straight taper, so 1 is geometrically exact -- more only helps if a
        /// shader needs the extra vertices.</param>
        /// <returns>Number of points written.</returns>
        public static int Sample(
            in ProjectileGeometry geometry,
            ProfilePoint[] buffer,
            int noseSegments = 32,
            int tailSegments = 2)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (noseSegments < 1) noseSegments = 1;
            if (tailSegments < 1) tailSegments = 1;

            int required = RequiredCapacity(noseSegments, tailSegments);
            if (buffer.Length < required)
                throw new ArgumentException(
                    $"Buffer too small: need {required} points, got {buffer.Length}.", nameof(buffer));

            int count = 0;
            double total = geometry.OverallLength;
            double noseLength = geometry.NoseLength;
            double shoulder = noseLength;
            double boattailStart = noseLength + geometry.BearingSurfaceLength;

            // --- Nose -------------------------------------------------------
            if (noseLength > 0.0)
            {
                // Cosine spacing clusters points towards the tip, where the ogive
                // curves hardest and a uniform spacing visibly facets.
                for (int i = 0; i < noseSegments; i++)
                {
                    double t = (double)i / noseSegments;
                    double eased = 1.0 - Math.Cos(t * Math.PI * 0.5);
                    double x = eased * noseLength;
                    Emit(geometry, buffer, ref count, x);
                }
            }

            // Shoulder: emitted exactly so the nose-to-shank junction is sharp on a
            // secant ogive (which really does have a corner there) and smooth on a
            // tangent ogive (where the surfaces meet at the same angle anyway).
            Emit(geometry, buffer, ref count, shoulder);

            // --- Bearing surface --------------------------------------------
            if (geometry.BearingSurfaceLength > 0.0)
                Emit(geometry, buffer, ref count, boattailStart);

            // --- Boattail ---------------------------------------------------
            if (geometry.BoattailLength > 0.0)
            {
                for (int i = 1; i <= tailSegments; i++)
                {
                    double t = (double)i / tailSegments;
                    double x = boattailStart + t * geometry.BoattailLength;
                    Emit(geometry, buffer, ref count, x);
                }
            }

            // Base, always exact.
            if (count == 0 || buffer[count - 1].X < total - 1e-12)
                Emit(geometry, buffer, ref count, total);

            return count;
        }

        /// <summary>Allocating convenience overload. Do not call this per frame.</summary>
        public static ProfilePoint[] Sample(
            in ProjectileGeometry geometry,
            int noseSegments = 32,
            int tailSegments = 2)
        {
            var buffer = new ProfilePoint[RequiredCapacity(noseSegments, tailSegments)];
            int n = Sample(geometry, buffer, noseSegments, tailSegments);
            var result = new ProfilePoint[n];
            Array.Copy(buffer, result, n);
            return result;
        }

        private static void Emit(in ProjectileGeometry geometry, ProfilePoint[] buffer, ref int count, double x)
        {
            // Collapse duplicate stations, which occur whenever a section has zero
            // length (a flat-base bullet, or one with no bearing surface).
            if (count > 0 && Math.Abs(buffer[count - 1].X - x) < 1e-12)
                return;

            buffer[count++] = new ProfilePoint
            {
                X = x,
                OuterRadius = geometry.RadiusAt(x),
                InnerRadius = geometry.CavityRadiusAt(x)
            };
        }
    }
}
