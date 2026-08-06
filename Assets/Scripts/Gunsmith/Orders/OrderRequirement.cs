using System;
using Krofken.Ballistics;

namespace Gunsmith.Orders
{
    /// <summary>
    /// A quantity the test range can measure. Orders may only be written against
    /// these, which is the rule that keeps the whole design honest -- there is no
    /// "stopping power" here, because no instrument reads it out.
    /// </summary>
    public enum MeasuredQuantity
    {
        MuzzleVelocity = 0,
        MuzzleEnergy = 1,
        PeakPressure = 2,
        StabilityFactor = 3,

        ImpactVelocity = 10,
        ImpactEnergy = 11,
        Drop = 12,

        PenetrationDepth = 20,
        ExpansionRatio = 21,
        ExpandedDiameter = 22,
        ExitEnergy = 23,
        ExitVelocity = 24,
        Perforated = 25,
        Fragmented = 26,
        FragmentCount = 27,

        /// <summary>Depth at which the projectile came apart. Reads as effectively
        /// infinite when it never did, so an "at most" bound correctly fails a round
        /// that stayed whole rather than passing on a sentinel value.</summary>
        FragmentationDepth = 28,

        EnergyDeposited = 30,
        PeakEnergyDepositionRate = 31,
        PeakEnergyDepositionDepth = 32,
        TemporaryCavityDiameter = 33,
        ReactiveEnergyReleased = 34,

        /// <summary>Energy deposited within the first <c>Parameter</c> metres.
        /// The quantity behind every "must do its work up front" brief.</summary>
        EnergyDepositedWithinDepth = 40
    }

    /// <summary>How a measured value is judged.</summary>
    public enum Comparison
    {
        AtLeast = 0,
        AtMost = 1,
        Between = 2,
        MustBeTrue = 3,
        MustBeFalse = 4
    }

    /// <summary>
    /// One condition a delivered round has to satisfy.
    ///
    /// The customer never states these in engineering terms -- a hunter says "it has
    /// to go through the shoulder and still reach the heart", and that is what the
    /// player reads. <see cref="CustomerWords"/> is what appears on the order card;
    /// <see cref="Technical"/> is what appears on the range readout once the player
    /// has learned to translate. Both describe the same inequality.
    /// </summary>
    [Serializable]
    public struct OrderRequirement
    {
        public MeasuredQuantity Quantity;
        public Comparison Comparison;

        /// <summary>Lower bound, SI. Used by AtLeast and Between.</summary>
        public double Minimum;

        /// <summary>Upper bound, SI. Used by AtMost and Between.</summary>
        public double Maximum;

        /// <summary>Extra input for quantities that need one, SI. Currently only the
        /// depth for <see cref="MeasuredQuantity.EnergyDepositedWithinDepth"/>.</summary>
        public double Parameter;

        /// <summary>How the customer put it.</summary>
        public string CustomerWords;

        /// <summary>
        /// Whether failing this fails the whole order.
        ///
        /// The distinction matters: a hunter who wanted more penetration and got a
        /// little less is disappointed. A bodyguard who asked for a round that would
        /// not pass through and got one that did has a much worse problem. Critical
        /// requirements are the ones where failure hurts somebody.
        /// </summary>
        public bool IsCritical;

        /// <summary>
        /// What happens to the customer if this is not met, in their words.
        ///
        /// This is where the delayed-consequence loop lives. The player gets no score
        /// on delivery -- they get told, the following day, what their ammunition did.
        /// "It went straight through him and into the woman behind" is a far more
        /// useful teacher than a failed checkmark, and it is authored per requirement
        /// so the feedback is always about the specific thing that went wrong.
        /// </summary>
        public string FailureConsequence;

        /// <summary>Attaches the consequence text. Fluent so catalogue entries read
        /// as a single expression.</summary>
        public OrderRequirement WithConsequence(string consequence)
        {
            FailureConsequence = consequence;
            return this;
        }

        // ---- Construction helpers -----------------------------------------

        public static OrderRequirement AtLeast(MeasuredQuantity quantity, double minimum, string words, bool critical = false)
            => new OrderRequirement
            {
                Quantity = quantity,
                Comparison = Comparison.AtLeast,
                Minimum = minimum,
                CustomerWords = words,
                IsCritical = critical
            };

        public static OrderRequirement AtMost(MeasuredQuantity quantity, double maximum, string words, bool critical = false)
            => new OrderRequirement
            {
                Quantity = quantity,
                Comparison = Comparison.AtMost,
                Maximum = maximum,
                CustomerWords = words,
                IsCritical = critical
            };

        public static OrderRequirement Between(MeasuredQuantity quantity, double minimum, double maximum, string words, bool critical = false)
            => new OrderRequirement
            {
                Quantity = quantity,
                Comparison = Comparison.Between,
                Minimum = minimum,
                Maximum = maximum,
                CustomerWords = words,
                IsCritical = critical
            };

        public static OrderRequirement MustBe(MeasuredQuantity quantity, bool expected, string words, bool critical = false)
            => new OrderRequirement
            {
                Quantity = quantity,
                Comparison = expected ? Comparison.MustBeTrue : Comparison.MustBeFalse,
                CustomerWords = words,
                IsCritical = critical
            };

        public static OrderRequirement EnergyWithin(double depthMetres, double minimumJoules, string words, bool critical = false)
            => new OrderRequirement
            {
                Quantity = MeasuredQuantity.EnergyDepositedWithinDepth,
                Comparison = Comparison.AtLeast,
                Minimum = minimumJoules,
                Parameter = depthMetres,
                CustomerWords = words,
                IsCritical = critical
            };

        // ---- Evaluation ----------------------------------------------------

        /// <summary>Reads the quantity this requirement judges out of a measurement.
        /// Booleans read as 1 or 0.</summary>
        public double Read(in ShotMeasurement m)
        {
            switch (Quantity)
            {
                case MeasuredQuantity.MuzzleVelocity: return m.MuzzleVelocity;
                case MeasuredQuantity.MuzzleEnergy: return m.MuzzleEnergy;
                case MeasuredQuantity.PeakPressure: return m.PeakPressure;
                case MeasuredQuantity.StabilityFactor: return m.StabilityFactor;

                case MeasuredQuantity.ImpactVelocity: return m.ImpactVelocity;
                case MeasuredQuantity.ImpactEnergy: return m.ImpactEnergy;
                case MeasuredQuantity.Drop: return m.Drop;

                case MeasuredQuantity.PenetrationDepth: return m.PenetrationDepth;
                case MeasuredQuantity.ExpansionRatio: return m.ExpansionRatio;
                case MeasuredQuantity.ExpandedDiameter: return m.ExpandedDiameter;
                case MeasuredQuantity.ExitEnergy: return m.ExitEnergy;
                case MeasuredQuantity.ExitVelocity: return m.ExitVelocity;
                case MeasuredQuantity.Perforated: return m.Perforated ? 1.0 : 0.0;
                case MeasuredQuantity.Fragmented: return m.Fragmented ? 1.0 : 0.0;
                case MeasuredQuantity.FragmentCount: return m.FragmentCount;

                // A round that never broke up carries a sentinel depth of -1. Passing
                // that straight through would let it satisfy "must break up within
                // 5 cm" by never breaking up at all, so it reads as far beyond any
                // block instead.
                case MeasuredQuantity.FragmentationDepth:
                    return m.Fragmented ? m.FragmentationDepth : 99.0;

                case MeasuredQuantity.EnergyDeposited: return m.EnergyDeposited;
                case MeasuredQuantity.PeakEnergyDepositionRate: return m.PeakEnergyDepositionRate;
                case MeasuredQuantity.PeakEnergyDepositionDepth: return m.PeakEnergyDepositionDepth;
                case MeasuredQuantity.TemporaryCavityDiameter: return m.TemporaryCavityDiameter;
                case MeasuredQuantity.ReactiveEnergyReleased: return m.ReactiveEnergyReleased;

                case MeasuredQuantity.EnergyDepositedWithinDepth: return m.EnergyDepositedWithin(Parameter);

                default: return 0.0;
            }
        }

        /// <summary>True if the measurement satisfies this requirement.</summary>
        public bool IsSatisfiedBy(in ShotMeasurement m)
        {
            double value = Read(m);

            switch (Comparison)
            {
                case Comparison.AtLeast: return value >= Minimum;
                case Comparison.AtMost: return value <= Maximum;
                case Comparison.Between: return value >= Minimum && value <= Maximum;
                case Comparison.MustBeTrue: return value > 0.5;
                case Comparison.MustBeFalse: return value <= 0.5;
                default: return false;
            }
        }

        /// <summary>
        /// How close the measurement came, 0..1, where 1 is satisfied.
        ///
        /// Used for partial credit and for the "so close" feedback that makes an
        /// experiment worth repeating: missing a penetration target by a centimetre
        /// should not read the same as missing it by a foot.
        /// </summary>
        public double Satisfaction(in ShotMeasurement m)
        {
            if (IsSatisfiedBy(m)) return 1.0;

            double value = Read(m);

            switch (Comparison)
            {
                case Comparison.AtLeast:
                    return Minimum > 0.0 ? Clamp01(value / Minimum) : 0.0;

                case Comparison.AtMost:
                    // Overshoot measured against the bound itself: twice the limit
                    // scores zero.
                    return Maximum > 0.0 ? Clamp01(2.0 - value / Maximum) : 0.0;

                case Comparison.Between:
                    if (value < Minimum) return Minimum > 0.0 ? Clamp01(value / Minimum) : 0.0;
                    return Maximum > 0.0 ? Clamp01(2.0 - value / Maximum) : 0.0;

                default:
                    // Booleans have no near miss. It either did or it did not.
                    return 0.0;
            }
        }

        /// <summary>
        /// The requirement stated in engineering terms with real units -- what the
        /// player sees on the range readout once they know what the customer meant.
        /// </summary>
        public string Technical
        {
            get
            {
                string name = DisplayName(Quantity);

                switch (Comparison)
                {
                    case Comparison.AtLeast: return $"{name} >= {Format(Quantity, Minimum)}";
                    case Comparison.AtMost: return $"{name} <= {Format(Quantity, Maximum)}";
                    case Comparison.Between: return $"{name} between {Format(Quantity, Minimum)} and {Format(Quantity, Maximum)}";
                    case Comparison.MustBeTrue: return $"{name}: yes";
                    case Comparison.MustBeFalse: return $"{name}: no";
                    default: return name;
                }
            }
        }

        /// <summary>Formats the measured value with the same units as
        /// <see cref="Technical"/>, so the two can be read side by side.</summary>
        public string FormatMeasured(in ShotMeasurement m) => Format(Quantity, Read(m));

        // ---- Presentation --------------------------------------------------

        public static string DisplayName(MeasuredQuantity q)
        {
            switch (q)
            {
                case MeasuredQuantity.MuzzleVelocity: return "Muzzle velocity";
                case MeasuredQuantity.MuzzleEnergy: return "Muzzle energy";
                case MeasuredQuantity.PeakPressure: return "Peak pressure";
                case MeasuredQuantity.StabilityFactor: return "Stability";
                case MeasuredQuantity.ImpactVelocity: return "Impact velocity";
                case MeasuredQuantity.ImpactEnergy: return "Impact energy";
                case MeasuredQuantity.Drop: return "Drop";
                case MeasuredQuantity.PenetrationDepth: return "Penetration";
                case MeasuredQuantity.ExpansionRatio: return "Expansion";
                case MeasuredQuantity.ExpandedDiameter: return "Recovered diameter";
                case MeasuredQuantity.ExitEnergy: return "Exit energy";
                case MeasuredQuantity.ExitVelocity: return "Exit velocity";
                case MeasuredQuantity.Perforated: return "Passes through";
                case MeasuredQuantity.Fragmented: return "Breaks up";
                case MeasuredQuantity.FragmentCount: return "Fragments";
                case MeasuredQuantity.FragmentationDepth: return "Breaks up at";
                case MeasuredQuantity.EnergyDeposited: return "Energy delivered";
                case MeasuredQuantity.PeakEnergyDepositionRate: return "Peak energy transfer";
                case MeasuredQuantity.PeakEnergyDepositionDepth: return "Depth of peak transfer";
                case MeasuredQuantity.TemporaryCavityDiameter: return "Temporary cavity";
                case MeasuredQuantity.ReactiveEnergyReleased: return "Payload energy";
                case MeasuredQuantity.EnergyDepositedWithinDepth: return "Energy delivered up front";
                default: return q.ToString();
            }
        }

        /// <summary>Formats a value in the unit a gunsmith would actually quote.</summary>
        public static string Format(MeasuredQuantity q, double value)
        {
            switch (q)
            {
                case MeasuredQuantity.MuzzleVelocity:
                case MeasuredQuantity.ImpactVelocity:
                case MeasuredQuantity.ExitVelocity:
                    return $"{value:F0} m/s";

                case MeasuredQuantity.MuzzleEnergy:
                case MeasuredQuantity.ImpactEnergy:
                case MeasuredQuantity.ExitEnergy:
                case MeasuredQuantity.EnergyDeposited:
                case MeasuredQuantity.ReactiveEnergyReleased:
                case MeasuredQuantity.EnergyDepositedWithinDepth:
                    return $"{value:F0} J";

                case MeasuredQuantity.PeakPressure:
                    return $"{Units.PascalsToMegapascals(value):F0} MPa";

                case MeasuredQuantity.PenetrationDepth:
                case MeasuredQuantity.PeakEnergyDepositionDepth:
                case MeasuredQuantity.Drop:
                    return $"{value * 100.0:F1} cm";

                case MeasuredQuantity.FragmentationDepth:
                    return value >= 99.0 ? "never" : $"{value * 100.0:F1} cm";

                case MeasuredQuantity.ExpandedDiameter:
                case MeasuredQuantity.TemporaryCavityDiameter:
                    return $"{Units.MetresToMillimetres(value):F1} mm";

                case MeasuredQuantity.ExpansionRatio:
                    return $"{value:F2}x";

                case MeasuredQuantity.PeakEnergyDepositionRate:
                    return $"{value:F0} J/m";

                case MeasuredQuantity.StabilityFactor:
                    return $"{value:F2}";

                case MeasuredQuantity.Perforated:
                case MeasuredQuantity.Fragmented:
                    return value > 0.5 ? "yes" : "no";

                case MeasuredQuantity.FragmentCount:
                    return $"{value:F0}";

                default:
                    return $"{value:F2}";
            }
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
