using System;

namespace Krofken.Ballistics
{
    /// <summary>What the primer looks like after firing, in the order the signs appear.</summary>
    public enum PrimerCondition
    {
        /// <summary>Corner radius still visible. A healthy load.</summary>
        Rounded = 0,
        /// <summary>The radius has gone and the edge is square to the pocket.</summary>
        Flattened = 1,
        /// <summary>Brass has flowed back around the firing pin, raising a crater.</summary>
        Cratered = 2,
        /// <summary>The cup has been pierced by the pin. Gas in the action.</summary>
        Pierced = 3,
        /// <summary>The pocket has opened up and no longer grips. The case is scrap.</summary>
        PocketLoose = 4
    }

    /// <summary>What the case head looks like after firing.</summary>
    public enum CaseHeadCondition
    {
        Clean = 0,
        /// <summary>A bright smear where the extractor dragged over it.</summary>
        ExtractorSwipe = 1,
        /// <summary>Brass extruded into the ejector hole, leaving a raised pip.</summary>
        EjectorMark = 2,
        /// <summary>A bright ring above the web where the case is about to come apart.</summary>
        IncipientSeparation = 3
    }

    /// <summary>
    /// The fired case: what the player picks up off the bench and reads.
    ///
    /// THIS IS THE PRESSURE GAUGE, and it has no numbers on it. The range must never
    /// show peak pressure, so the case carries it instead — exactly as a real handloader
    /// works out that a load is hot by looking at the brass rather than by owning a
    /// pressure trace.
    ///
    /// WHY THE SIGNS APPEAR IN THIS ORDER, which is the physical part:
    ///
    ///   The PRIMER CUP goes first. It is much softer than the case head and it is
    ///   unsupported over the firing-pin hole, so it is the first thing in the cartridge
    ///   to yield. Its corner radius irons out against the bolt face and the edge goes
    ///   square. This is why handloaders read the primer before anything else.
    ///
    ///   Then brass FLOWS INTO THE HOLES. The pin hole first, raising a crater around
    ///   the dimple; then the ejector hole in the bolt face, leaving a raised pip on the
    ///   case head. Both are unsupported area, and brass extrudes into whatever is not
    ///   holding it.
    ///
    ///   Then the PRIMER POCKET opens. Once the head has taken enough plastic strain the
    ///   pocket no longer grips, and the primer falls out on extraction.
    ///
    ///   Finally the case RUPTURES. The neck and the web are the thinnest and the most
    ///   work-hardened, so that is where it splits.
    ///
    /// The thresholds are expressed as fractions of the case's own
    /// <see cref="CartridgeCase.MaximumPressure"/>, so a stronger case tolerates more
    /// before showing the same sign. They are CALIBRATED presentation values, not
    /// measured constants — the ordering is physical, the exact fractions are chosen so
    /// the tells arrive at a readable rate.
    /// </summary>
    [Serializable]
    public struct FiredCase
    {
        public PrimerCondition Primer;
        public CaseHeadCondition Head;

        /// <summary>The neck has split open.</summary>
        public bool NeckSplit;

        /// <summary>The case came apart. Gas escaped into the action, and whatever the
        /// round was fired from is not fine.</summary>
        public bool Ruptured;

        /// <summary>
        /// Peak pressure as a fraction of what this case can hold. 1.0 is the limit.
        ///
        /// FOR THE VIEW ONLY — it is what a renderer scales a dent or a bulge by. It is
        /// a pressure reading by another name, so it must never be printed, labelled or
        /// put in a tooltip. If this number ever reaches the player the case has stopped
        /// being a gauge with no numbers on it and the range has been given away.
        /// </summary>
        public double PressureFraction;

        /// <summary>True when there is nothing unusual to see. The load was fine.</summary>
        public bool IsUnremarkable =>
            Primer == PrimerCondition.Rounded && Head == CaseHeadCondition.Clean &&
            !NeckSplit && !Ruptured;

        /// <summary>
        /// What a gunsmith would say looking at it. Observations about the brass in
        /// their hand, never a figure and never a prediction.
        /// </summary>
        public string Describe()
        {
            if (Ruptured) return "the case has let go";

            string primer;
            switch (Primer)
            {
                case PrimerCondition.Flattened: primer = "primer flattened"; break;
                case PrimerCondition.Cratered: primer = "primer cratered"; break;
                case PrimerCondition.Pierced: primer = "primer pierced"; break;
                case PrimerCondition.PocketLoose: primer = "primer pocket loose"; break;
                default: primer = "primer looks right"; break;
            }

            string head;
            switch (Head)
            {
                case CaseHeadCondition.ExtractorSwipe: head = ", extractor swipe"; break;
                case CaseHeadCondition.EjectorMark: head = ", ejector mark"; break;
                case CaseHeadCondition.IncipientSeparation: head = ", bright ring above the web"; break;
                default: head = string.Empty; break;
            }

            return primer + head + (NeckSplit ? ", neck split" : string.Empty);
        }
    }

    /// <summary>Reads a fired case from what the interior solve actually did.</summary>
    public static class FiredCaseReader
    {
        // Fractions of the case's own maximum pressure at which each sign appears.
        // Ordering is physical; the values are calibrated for a readable progression.
        // Primer signs, in the order the cup fails: the radius irons out, brass flows
        // back around the pin, the pin punches through, and finally the head has taken
        // enough strain that the pocket no longer grips. A loose pocket is the last and
        // most definitive sign, which is why it ranks above a pierced cup.
        //
        // CALIBRATION NOTE, and it matters: the signs start AT the case's rated maximum,
        // not below it. A rating is a working limit that brass is expected to survive
        // repeatedly, and cases are proof-tested well above it — so a load running at
        // the limit is a healthy load and must come back clean. The project's own
        // calibrated 9 mm sits at about 95% of the CIP limit; if that left flattened
        // primers then every load would look hot and the gauge would say nothing.
        private const double FlattenedAt = 1.00;
        private const double CrateredAt = 1.12;
        private const double PiercedAt = 1.24;
        private const double PocketLooseAt = 1.36;

        // Head signs, in the order the head fails.
        private const double SwipeAt = 1.06;
        private const double EjectorMarkAt = 1.16;
        private const double SeparationAt = 1.30;

        private const double SplitAt = 1.28;
        private const double RupturedAt = 1.45;

        /// <summary>
        /// Reads the brass.
        /// </summary>
        /// <param name="peakPressure">Peak chamber pressure, Pa.</param>
        /// <param name="cartridgeCase">The case it was fired in.</param>
        public static FiredCase Read(double peakPressure, in CartridgeCase cartridgeCase)
        {
            var fired = new FiredCase();

            double limit = cartridgeCase.MaximumPressure;
            if (limit <= 0.0 || peakPressure <= 0.0) return fired;

            double fraction = peakPressure / limit;
            fired.PressureFraction = fraction;

            if (fraction >= RupturedAt)
            {
                fired.Ruptured = true;
                fired.NeckSplit = true;
                fired.Primer = PrimerCondition.PocketLoose;
                fired.Head = CaseHeadCondition.IncipientSeparation;
                return fired;
            }

            if (fraction >= PocketLooseAt) fired.Primer = PrimerCondition.PocketLoose;
            else if (fraction >= PiercedAt) fired.Primer = PrimerCondition.Pierced;
            else if (fraction >= CrateredAt) fired.Primer = PrimerCondition.Cratered;
            else if (fraction >= FlattenedAt) fired.Primer = PrimerCondition.Flattened;
            else fired.Primer = PrimerCondition.Rounded;

            if (fraction >= SeparationAt) fired.Head = CaseHeadCondition.IncipientSeparation;
            else if (fraction >= EjectorMarkAt) fired.Head = CaseHeadCondition.EjectorMark;
            else if (fraction >= SwipeAt) fired.Head = CaseHeadCondition.ExtractorSwipe;
            else fired.Head = CaseHeadCondition.Clean;

            fired.NeckSplit = fraction >= SplitAt;

            return fired;
        }
    }
}
