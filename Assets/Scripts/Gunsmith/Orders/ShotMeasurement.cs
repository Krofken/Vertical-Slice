using System;
using Krofken.Ballistics;

namespace Gunsmith.Orders
{
    /// <summary>
    /// Everything an instrumented test shot produces.
    ///
    /// THIS IS THE CONTRACT BETWEEN PHYSICS AND GAMEPLAY. Orders are written against
    /// these fields and nothing else, which is what keeps the design honest: a
    /// customer cannot ask for "more stopping power" because there is no such number.
    /// They ask for a penetration depth, an energy figure, an expansion ratio -- things
    /// the range can actually measure.
    ///
    /// Every field here is also something the player can READ off an instrument in
    /// the backyard. Nothing is hidden. If an order can be judged on it, the player
    /// can measure it before delivering.
    /// </summary>
    [Serializable]
    public struct ShotMeasurement
    {
        // ---- Chronograph, at the muzzle -----------------------------------
        /// <summary>m/s</summary>
        public double MuzzleVelocity;
        /// <summary>J</summary>
        public double MuzzleEnergy;
        /// <summary>Pa. Read from the pressure trace, not the chronograph.</summary>
        public double PeakPressure;
        /// <summary>Gyroscopic stability factor. Below 1 the round keyholes.</summary>
        public double StabilityFactor;
        /// <summary>Fraction of the charge burnt before muzzle exit, 0..1.</summary>
        public double BurntFraction;

        // ---- Downrange, at the target -------------------------------------
        /// <summary>Distance to the target, m.</summary>
        public double Range;
        /// <summary>m/s at the target.</summary>
        public double ImpactVelocity;
        /// <summary>J at the target.</summary>
        public double ImpactEnergy;
        /// <summary>Drop below the line of departure at the target, m.</summary>
        public double Drop;
        /// <summary>Time of flight, s.</summary>
        public double TimeOfFlight;

        // ---- Recovered from the block --------------------------------------
        /// <summary>Total penetration, m.</summary>
        public double PenetrationDepth;
        /// <summary>True if it came out the far side. The "exit wound" question.</summary>
        public bool Perforated;
        /// <summary>m/s on exit, zero if it stopped inside.</summary>
        public double ExitVelocity;
        /// <summary>J carried out the far side -- energy the target never received.</summary>
        public double ExitEnergy;
        /// <summary>Recovered frontal diameter as a multiple of calibre.</summary>
        public double ExpansionRatio;
        /// <summary>Recovered frontal diameter, m.</summary>
        public double ExpandedDiameter;
        /// <summary>True if the projectile came apart.</summary>
        public bool Fragmented;
        /// <summary>Depth at which it came apart, m. Negative if it did not.</summary>
        public double FragmentationDepth;
        /// <summary>Approximate recovered fragment count.</summary>
        public int FragmentCount;
        /// <summary>True if a fibrous layer packed the cavity and stopped expansion.</summary>
        public bool CavityPlugged;

        // ---- Wound channel --------------------------------------------------
        /// <summary>Total energy given up inside the target, J.</summary>
        public double EnergyDeposited;
        /// <summary>Highest energy deposition rate, J/m.</summary>
        public double PeakEnergyDepositionRate;
        /// <summary>Depth of peak deposition, m.</summary>
        public double PeakEnergyDepositionDepth;
        /// <summary>Widest temporary cavity, m.</summary>
        public double TemporaryCavityDiameter;
        /// <summary>Chemical energy released by a payload, J.</summary>
        public double ReactiveEnergyReleased;

        /// <summary>Energy deposited per depth bin, J. The wound channel plot.</summary>
        public double[] EnergyProfile;
        /// <summary>Bin width of <see cref="EnergyProfile"/>, m.</summary>
        public double EnergyProfileBinWidth;
        /// <summary>Populated bin count.</summary>
        public int EnergyProfileBinCount;

        /// <summary>
        /// Energy deposited within the first <paramref name="depth"/> metres, J.
        /// Orders about crowd safety and about not over-penetrating are written
        /// against this: it is the energy the intended target actually received.
        /// </summary>
        public double EnergyDepositedWithin(double depth)
        {
            if (EnergyProfile == null || EnergyProfileBinWidth <= 0.0) return 0.0;

            int bins = (int)Math.Ceiling(depth / EnergyProfileBinWidth);
            if (bins > EnergyProfileBinCount) bins = EnergyProfileBinCount;

            double total = 0.0;
            for (int i = 0; i < bins && i < EnergyProfile.Length; i++) total += EnergyProfile[i];
            return total;
        }

        /// <summary>
        /// Assembles a measurement from a baked round, its flight and its impact.
        /// The three solvers each contribute their own part; nothing is invented here.
        /// </summary>
        public static ShotMeasurement From(
            BakedCartridge round,
            in TerminalResult terminal,
            double range,
            double impactVelocity,
            double drop,
            double timeOfFlight)
        {
            return new ShotMeasurement
            {
                MuzzleVelocity = round.MuzzleVelocity,
                MuzzleEnergy = round.MuzzleEnergy,
                PeakPressure = round.Interior.PeakPressure,
                StabilityFactor = round.StabilityFactor,
                BurntFraction = round.Interior.BurntFractionAtMuzzle,

                Range = range,
                ImpactVelocity = impactVelocity,
                ImpactEnergy = terminal.ImpactEnergy,
                Drop = drop,
                TimeOfFlight = timeOfFlight,

                PenetrationDepth = terminal.PenetrationDepth,
                Perforated = terminal.Perforated,
                ExitVelocity = terminal.ExitVelocity,
                ExitEnergy = terminal.ExitEnergy,
                ExpansionRatio = terminal.ExpansionRatio,
                ExpandedDiameter = terminal.MaxExpandedDiameter,
                Fragmented = terminal.Fragmented,
                FragmentationDepth = terminal.FragmentationDepth,
                FragmentCount = terminal.FragmentCount,
                CavityPlugged = terminal.CavityPlugged,

                EnergyDeposited = terminal.EnergyDeposited,
                PeakEnergyDepositionRate = terminal.PeakEnergyDepositionRate,
                PeakEnergyDepositionDepth = terminal.PeakEnergyDepositionDepth,
                TemporaryCavityDiameter = terminal.MaxTemporaryCavityDiameter,
                ReactiveEnergyReleased = terminal.ReactiveEnergyReleased,

                EnergyProfile = terminal.EnergyProfile,
                EnergyProfileBinWidth = terminal.ProfileBinWidth,
                EnergyProfileBinCount = terminal.ProfileBinCount
            };
        }
    }
}
