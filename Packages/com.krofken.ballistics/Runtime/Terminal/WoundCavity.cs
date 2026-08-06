using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Turns the energy-deposition profile of an impact into the SHAPE of the cavity
    /// it cut, as a radius-vs-depth curve ready to be revolved into a mesh.
    ///
    /// WHY THIS EXISTS: the test range must never show the player a number.
    /// <see cref="TerminalResult.EnergyProfile"/> is already a plot of where the round
    /// did its work, so rendering it as the actual cavity in the block lets the player
    /// read that plot by looking at it. A narrow tunnel the whole way is a
    /// full-metal-jacket. A bulge a few centimetres in that tapers after is a hollow
    /// point opening up. A violent flower at the entry face and nothing behind it is a
    /// frangible.
    ///
    /// PHYSICS -- cavity expansion, sharing the penetration solver's constant.
    ///
    /// Driving a cavity of radius r through unit depth means pushing the medium aside
    /// against its quasi-static cavity-expansion pressure R_t. The work done per unit
    /// depth is that pressure times the area opened:
    ///
    ///     dE/dx = pi * r^2 * R_t                      [ J/m = Pa * m^2 ]
    ///
    /// R_t is exactly <see cref="TargetMedium.StrengthTerm"/>, the strength term of the
    /// Poncelet law
    ///
    ///     F = A * ( R_t + 0.5 * C_d * rho_t * v^2 )
    ///
    /// that <see cref="TerminalBallisticsSolver"/> integrates for penetration depth. So
    /// the cavity drawn here and the depth computed there come from ONE material
    /// constant, not two independent ones -- retune the gelatin and the picture and the
    /// number move together.
    ///
    /// Inverting for the radius in each depth bin:
    ///
    ///     r(x) = sqrt( (dE/dx) / (pi * R_t) )
    ///
    /// WHAT THIS IS NOT: this is the TEMPORARY cavity envelope, the widest the medium
    /// was pushed aside. In a fluid-like medium most of that collapses elastically and
    /// the permanent channel is far narrower. Gelatin is used precisely because it
    /// retains the crack pattern, which is why the block shows the player anything at
    /// all. Do not present this as a wound profile; it is the block's response.
    /// </summary>
    public static class WoundCavity
    {
        /// <summary>Points needed for a given result. Size a reusable buffer with this
        /// once rather than allocating per shot.</summary>
        public static int RequiredCapacity(in TerminalResult result)
            => Math.Max(result.ProfileBinCount, 0) + 2;

        /// <summary>
        /// Builds the cavity as a radius-vs-depth polyline, tip-to-base ordering, with
        /// <see cref="ProfilePoint.X"/> measured in metres from the entry face.
        ///
        /// Returns the number of points written, or 0 when the impact produced no
        /// readable cavity (no profile, or a medium with no strength term such as an
        /// air gap, where the equation has no bounded solution).
        /// </summary>
        /// <param name="result">Impact to render.</param>
        /// <param name="medium">Medium the cavity was cut in -- supplies R_t. For a
        /// layered target this is the layer being drawn, normally the gelatin.</param>
        /// <param name="buffer">Destination, at least <see cref="RequiredCapacity"/>.</param>
        /// <param name="minimumRadius">Floor on the radius, m. The channel cannot be
        /// narrower than the projectile that cut it, so pass the expanded radius
        /// (<see cref="TerminalResult.MaxExpandedDiameter"/> * 0.5) or the calibre.</param>
        public static int Build(
            in TerminalResult result,
            in TargetMedium medium,
            ProfilePoint[] buffer,
            double minimumRadius = 0.0)
            => Build(result.EnergyProfile, result.ProfileBinCount, result.ProfileBinWidth,
                     result.PenetrationDepth, result.Perforated,
                     medium.StrengthTerm, buffer, minimumRadius);

        /// <summary>
        /// Primitive overload, so a caller holding the measurements rather than the
        /// solver's own result struct — the game's notebook entry, for instance — can
        /// rebuild a block without inventing a <see cref="TerminalResult"/> to pass.
        /// </summary>
        /// <param name="strengthTerm">The medium's Poncelet strength term R_t, Pa.</param>
        public static int Build(
            double[] energy,
            int binCount,
            double binWidth,
            double penetrationDepth,
            bool perforated,
            double strengthTerm,
            ProfilePoint[] buffer,
            double minimumRadius = 0.0)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (energy == null) return 0;

            int bins = Math.Min(binCount, energy.Length);
            double strength = strengthTerm;

            // No strength term means nothing resists the cavity opening, so r has no
            // bounded solution. An air gap is the case that reaches here.
            if (bins <= 0 || binWidth <= 0.0 || strength <= 0.0) return 0;

            int required = bins + 2;
            if (buffer.Length < required)
                throw new ArgumentException(
                    $"Buffer too small: need {required} points, got {buffer.Length}.", nameof(buffer));

            if (minimumRadius < 0.0) minimumRadius = 0.0;

            int count = 0;

            // The cavity is open at the entry face, at whatever width the first bin
            // earned. Emitting x = 0 explicitly keeps the mouth flush with the block
            // face instead of starting half a bin inside it.
            double entryRadius = RadiusFor(energy[0], binWidth, strength, minimumRadius);
            buffer[count++] = new ProfilePoint { X = 0.0, OuterRadius = entryRadius };

            // One point per bin, at the bin's centre -- the energy figure is the total
            // for the bin, so the centre is where it belongs.
            for (int i = 0; i < bins; i++)
            {
                double x = (i + 0.5) * binWidth;
                buffer[count++] = new ProfilePoint
                {
                    X = x,
                    OuterRadius = RadiusFor(energy[i], binWidth, strength, minimumRadius)
                };
            }

            // Close the far end at the true stopping depth, which generally falls
            // inside the last bin rather than on its centre.
            double lastX = buffer[count - 1].X;

            if (penetrationDepth > lastX + 1e-9)
            {
                // A projectile that stopped inside leaves a channel that closes down to
                // the bullet sitting at the bottom of it. One that went through leaves
                // the channel open at the back face at the width it had there.
                double closingRadius = perforated ? buffer[count - 1].OuterRadius : minimumRadius;
                buffer[count++] = new ProfilePoint { X = penetrationDepth, OuterRadius = closingRadius };
            }

            return count;
        }

        /// <summary>Allocating convenience overload. Not for per-shot use in a loop.</summary>
        public static ProfilePoint[] Build(
            in TerminalResult result,
            in TargetMedium medium,
            double minimumRadius = 0.0)
        {
            var buffer = new ProfilePoint[RequiredCapacity(result)];
            int n = Build(result, medium, buffer, minimumRadius);
            var trimmed = new ProfilePoint[n];
            Array.Copy(buffer, trimmed, n);
            return trimmed;
        }

        /// <summary>r = sqrt( (dE/dx) / (pi * R_t) ), floored at the physical hole.</summary>
        private static double RadiusFor(double binEnergy, double binWidth, double strength, double minimumRadius)
        {
            if (binEnergy <= 0.0) return minimumRadius;

            double perMetre = binEnergy / binWidth;
            double radius = Math.Sqrt(perMetre / (Math.PI * strength));
            return radius > minimumRadius ? radius : minimumRadius;
        }
    }
}
