using System;
using System.Collections.Generic;
using System.Text;
using Gunsmith.Orders;
using Gunsmith.Workshop;
using Krofken.Ballistics;

namespace Gunsmith.Range
{
    /// <summary>
    /// One logged shot: the full recipe that went in and the full readout that came
    /// out.
    ///
    /// The recipe is flattened into readable numbers rather than stored as a design
    /// reference on purpose -- an entry has to stay truthful about what was fired even
    /// after the player edits that design twenty times. A notebook that silently
    /// rewrites its own history is worse than no notebook.
    /// </summary>
    [Serializable]
    public sealed class NotebookEntry
    {
        public int Day;
        public int ShotNumber;

        public string DesignId;
        public string DesignName;

        // ---- Recipe, as fired ----------------------------------------------
        public string CoreMaterial;
        public string JacketMaterial;
        public string PayloadMaterial;
        public string Propellant;
        public string GrainShape;

        public double ChargeGrains;
        public double BulletGrains;
        public double WebMicrons;
        public double DeterrentCoating;
        public double SeatingDepthMm;

        public double CalibreMm;
        public double OverallLengthMm;
        public double MeplatMm;
        public double CavityDepthMm;
        public double CavityMouthMm;
        public double OgiveShape;
        public double JacketThicknessMm;

        // ---- Conditions ------------------------------------------------------
        public double Range;
        public string TargetName;

        // ---- Readout ---------------------------------------------------------
        public ShotMeasurement Measurement;

        /// <summary>Free-text the player can add.</summary>
        public string Notes;

        /// <summary>One-line summary for a list view.</summary>
        public string Summary =>
            $"#{ShotNumber} d{Day} {DesignName}: {Measurement.MuzzleVelocity:F0} m/s, " +
            $"{Measurement.PenetrationDepth * 100:F1} cm, {Measurement.ExpansionRatio:F2}x" +
            (Measurement.Fragmented ? ", frag" : "") +
            (Measurement.Perforated ? ", THROUGH" : "");
    }

    /// <summary>
    /// Automatic record of every shot ever fired.
    ///
    /// In a game about experimentation this is not a convenience, it is the save
    /// file. A player will fire sixty test rounds across a session; expecting them to
    /// remember what 2.3 grains behind a 0.6 mm cavity did versus 2.6 is expecting
    /// them to quit. Every shot is logged automatically, with the complete recipe,
    /// and can be sorted and compared afterwards.
    /// </summary>
    [Serializable]
    public sealed class LabNotebook
    {
        private readonly List<NotebookEntry> _entries = new List<NotebookEntry>();

        public event Action<NotebookEntry> EntryAdded;

        public IReadOnlyList<NotebookEntry> Entries => _entries;
        public int Count => _entries.Count;

        public void Add(NotebookEntry entry)
        {
            if (entry == null) return;
            entry.ShotNumber = _entries.Count + 1;
            _entries.Add(entry);
            EntryAdded?.Invoke(entry);
        }

        /// <summary>Every shot fired with a given design, oldest first.</summary>
        public List<NotebookEntry> ForDesign(string designId)
        {
            var result = new List<NotebookEntry>();
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].DesignId == designId) result.Add(_entries[i]);
            return result;
        }

        /// <summary>
        /// The best shot so far against a given order, by evaluation score.
        /// Lets the player ask "which of my forty attempts came closest?" without
        /// scrolling through forty attempts.
        /// </summary>
        public NotebookEntry BestFor(Order order)
        {
            NotebookEntry best = null;
            double bestScore = double.NegativeInfinity;

            for (int i = 0; i < _entries.Count; i++)
            {
                var evaluation = OrderEvaluator.Evaluate(order, _entries[i].Measurement);
                double score = evaluation.CriticalFailure ? evaluation.Score - 1.0 : evaluation.Score;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = _entries[i];
                }
            }

            return best;
        }

        /// <summary>
        /// Side-by-side comparison of two shots, listing only what actually differs.
        /// This is the tool that turns sixty scattered data points into a controlled
        /// experiment.
        /// </summary>
        public static string Compare(NotebookEntry a, NotebookEntry b)
        {
            if (a == null || b == null) return "Need two shots to compare.";

            var text = new StringBuilder();
            text.AppendLine($"Shot #{a.ShotNumber} vs #{b.ShotNumber}");
            text.AppendLine();
            text.AppendLine("CHANGED:");

            bool any = false;
            void Diff(string label, double x, double y, string unit, string format = "F2")
            {
                if (Math.Abs(x - y) < 1e-9) return;
                any = true;
                text.AppendLine($"  {label}: {x.ToString(format)} -> {y.ToString(format)} {unit}");
            }

            void DiffText(string label, string x, string y)
            {
                if (x == y) return;
                any = true;
                text.AppendLine($"  {label}: {x} -> {y}");
            }

            DiffText("core", a.CoreMaterial, b.CoreMaterial);
            DiffText("jacket", a.JacketMaterial, b.JacketMaterial);
            DiffText("payload", a.PayloadMaterial ?? "none", b.PayloadMaterial ?? "none");
            DiffText("propellant", a.Propellant, b.Propellant);
            DiffText("grain", a.GrainShape, b.GrainShape);
            Diff("charge", a.ChargeGrains, b.ChargeGrains, "gr");
            Diff("web", a.WebMicrons, b.WebMicrons, "um", "F1");
            Diff("deterrent", a.DeterrentCoating, b.DeterrentCoating, "");
            Diff("bullet mass", a.BulletGrains, b.BulletGrains, "gr", "F1");
            Diff("meplat", a.MeplatMm, b.MeplatMm, "mm");
            Diff("cavity depth", a.CavityDepthMm, b.CavityDepthMm, "mm");
            Diff("cavity mouth", a.CavityMouthMm, b.CavityMouthMm, "mm");
            Diff("ogive", a.OgiveShape, b.OgiveShape, "");
            Diff("jacket wall", a.JacketThicknessMm, b.JacketThicknessMm, "mm");
            Diff("seating", a.SeatingDepthMm, b.SeatingDepthMm, "mm");
            Diff("range", a.Range, b.Range, "m", "F0");

            if (!any) text.AppendLine("  (nothing -- same load, same conditions)");

            text.AppendLine();
            text.AppendLine("RESULT:");
            Diff("muzzle velocity", a.Measurement.MuzzleVelocity, b.Measurement.MuzzleVelocity, "m/s", "F0");
            Diff("peak pressure", Units.PascalsToMegapascals(a.Measurement.PeakPressure),
                Units.PascalsToMegapascals(b.Measurement.PeakPressure), "MPa", "F0");
            Diff("impact energy", a.Measurement.ImpactEnergy, b.Measurement.ImpactEnergy, "J", "F0");
            Diff("penetration", a.Measurement.PenetrationDepth * 100, b.Measurement.PenetrationDepth * 100, "cm", "F1");
            Diff("expansion", a.Measurement.ExpansionRatio, b.Measurement.ExpansionRatio, "x");
            Diff("energy delivered", a.Measurement.EnergyDeposited, b.Measurement.EnergyDeposited, "J", "F0");

            if (a.Measurement.Fragmented != b.Measurement.Fragmented)
                text.AppendLine($"  broke up: {a.Measurement.Fragmented} -> {b.Measurement.Fragmented}");
            if (a.Measurement.Perforated != b.Measurement.Perforated)
                text.AppendLine($"  passed through: {a.Measurement.Perforated} -> {b.Measurement.Perforated}");

            return text.ToString();
        }
    }

    /// <summary>Why a test shot could not be fired.</summary>
    public enum FireFailure
    {
        None = 0,
        NoDesign = 1,
        DesignUnsafe = 2,
        NoAmmunition = 3
    }

    /// <summary>
    /// The backyard range.
    ///
    /// Fires a round from stock, flies it to the target, drives it into the block,
    /// and hands back every number an instrument could read. Costs one round from
    /// stock, every time -- which is the scarcity that stops the player brute-forcing
    /// a design by testing a hundred variants.
    ///
    /// The instruments are the interface to the physics. Without a chronograph at the
    /// muzzle and graduations on the block, real numbers are just noise.
    /// </summary>
    public sealed class TestRange
    {
        private readonly AmmunitionWorkshop _workshop;
        private readonly LabNotebook _notebook;
        private readonly TrajectorySample[] _trajectoryBuffer = new TrajectorySample[8192];

        /// <summary>Air on the range. Changing it changes results, correctly.</summary>
        public Atmosphere Atmosphere = Atmosphere.Standard;

        /// <summary>Which forces the flight model includes.</summary>
        public TrajectoryOptions Options = TrajectoryOptions.Default;

        /// <summary>Barrel the range's test fixture uses.</summary>
        public Barrel Barrel = BarrelLibrary.ServicePistol9mm;

        public event Action<NotebookEntry> ShotFired;

        public TestRange(AmmunitionWorkshop workshop, LabNotebook notebook)
        {
            _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
            _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        }

        /// <summary>
        /// Fires one round and records the result.
        /// </summary>
        /// <param name="design">What to fire.</param>
        /// <param name="range">Distance to the block, m.</param>
        /// <param name="target">What the block is made of.</param>
        /// <param name="targetName">Label for the notebook.</param>
        /// <param name="day">Current day, for the notebook.</param>
        /// <param name="entry">The logged entry, on success.</param>
        /// <param name="failure">Why it could not fire, on failure.</param>
        public bool TryFire(
            SavedDesign design,
            double range,
            TargetLayer[] target,
            string targetName,
            int day,
            out NotebookEntry entry,
            out FireFailure failure)
        {
            entry = null;

            if (design?.Baked == null) { failure = FireFailure.NoDesign; return false; }
            if (!design.Baked.IsValid) { failure = FireFailure.DesignUnsafe; return false; }
            if (!_workshop.TryConsumeRounds(design.Id, 1)) { failure = FireFailure.NoAmmunition; return false; }

            failure = FireFailure.None;

            var measurement = Measure(design.Baked, range, target);
            entry = BuildEntry(design, range, targetName, day, measurement);

            _notebook.Add(entry);
            ShotFired?.Invoke(entry);
            return true;
        }

        /// <summary>
        /// Runs the flight and impact for a round WITHOUT consuming one.
        ///
        /// Used to judge a delivered batch -- the game already knows what the round
        /// does, and charging the player ammunition to find out what they shipped
        /// would be absurd. Also used for the ballistic calculator preview.
        /// </summary>
        public ShotMeasurement Measure(BakedCartridge round, double range, TargetLayer[] target)
        {
            if (round == null) return default;

            var state = round.CreateMuzzleState(0.0, Vec3.Zero, Barrel.TwistRate);

            TrajectorySolver.Simulate(
                state, round.Aerodynamics, Atmosphere, Options,
                _trajectoryBuffer, out int count,
                sampleInterval: 0.001,
                maxRange: Math.Max(range, 1.0),
                maxTime: 10.0,
                groundHeight: double.NegativeInfinity);

            // Interpolate onto the target plane rather than taking the last sample,
            // so a shot at 40 m is measured at 40 m and not at 40.3 m.
            double impactVelocity = round.MuzzleVelocity;
            double drop = 0.0;
            double timeOfFlight = 0.0;

            for (int i = 1; i < count; i++)
            {
                if (_trajectoryBuffer[i].Position.X < range) continue;

                var previous = _trajectoryBuffer[i - 1];
                var current = _trajectoryBuffer[i];

                double span = current.Position.X - previous.Position.X;
                double t = span > 1e-9 ? (range - previous.Position.X) / span : 0.0;

                impactVelocity = previous.Speed + (current.Speed - previous.Speed) * t;
                drop = previous.Position.Z + (current.Position.Z - previous.Position.Z) * t;
                timeOfFlight = previous.Time + (current.Time - previous.Time) * t;
                break;
            }

            // Fresh profile buffer per shot: the notebook keeps these, so they cannot
            // be pooled and overwritten by the next shot.
            var profile = new double[256];
            var terminal = TerminalBallisticsSolver.Solve(round.Terminal, target, impactVelocity, profile);

            return ShotMeasurement.From(round, terminal, range, impactVelocity, drop, timeOfFlight);
        }

        private static NotebookEntry BuildEntry(
            SavedDesign design, double range, string targetName, int day, in ShotMeasurement measurement)
        {
            var d = design.Design;
            var g = d.Projectile;

            return new NotebookEntry
            {
                Day = day,
                DesignId = design.Id,
                DesignName = design.Name,

                CoreMaterial = NameOfMaterial(d.Materials.CoreMaterialId),
                JacketMaterial = g.JacketThickness > 0 ? NameOfMaterial(d.Materials.JacketMaterialId) : "none",
                PayloadMaterial = string.IsNullOrEmpty(d.Materials.CavityFillMaterialId)
                    ? null
                    : NameOfMaterial(d.Materials.CavityFillMaterialId),
                Propellant = NameOfPropellant(d.PropellantId),
                GrainShape = d.GrainShape.ToString(),

                ChargeGrains = Units.KilogramsToGrains(d.ChargeMass),
                BulletGrains = Units.KilogramsToGrains(design.Baked.Mass.Mass),
                WebMicrons = d.WebThickness * 1e6,
                DeterrentCoating = d.DeterrentCoating,
                SeatingDepthMm = Units.MetresToMillimetres(d.SeatingDepth),

                CalibreMm = Units.MetresToMillimetres(g.Calibre),
                OverallLengthMm = Units.MetresToMillimetres(g.OverallLength),
                MeplatMm = Units.MetresToMillimetres(g.MeplatDiameter),
                CavityDepthMm = Units.MetresToMillimetres(g.CavityDepth),
                CavityMouthMm = Units.MetresToMillimetres(g.CavityMouthDiameter),
                OgiveShape = g.OgiveShapeParameter,
                JacketThicknessMm = Units.MetresToMillimetres(g.JacketThickness),

                Range = range,
                TargetName = targetName,
                Measurement = measurement
            };
        }

        private static string NameOfMaterial(string id)
            => MaterialLibrary.TryGet(id, out var m) ? m.DisplayName : id;

        private static string NameOfPropellant(string id)
            => PropellantLibrary.TryGet(id, out var p) ? p.DisplayName : id;
    }
}
