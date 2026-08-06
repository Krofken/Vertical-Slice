using System;
using System.Collections.Generic;

namespace Krofken.Ballistics
{
    /// <summary>
    /// A material a projectile has to drive through, described by the two terms of
    /// the Poncelet resistance law:
    ///
    ///     F = A * ( R_t + 0.5 * C_d * rho_t * v^2 )
    ///           \_______/   \___________________/
    ///           strength         inertial
    ///
    /// The STRENGTH term is velocity-independent: the pressure needed to tear, shear
    /// or crush the material out of the way. It dominates at low velocity and is what
    /// finally stops the projectile.
    ///
    /// The INERTIAL term is the cost of accelerating displaced material sideways. It
    /// scales with v^2, so it dominates at high velocity -- which is why doubling
    /// impact velocity does NOT double penetration depth.
    ///
    /// The interplay of those two terms is the reason a fast light projectile and a
    /// slow heavy one with the same energy behave nothing alike.
    /// </summary>
    [Serializable]
    public struct TargetMedium
    {
        public string Id;
        public string DisplayName;

        /// <summary>Density, kg/m^3. Drives the inertial resistance term.</summary>
        public double Density;

        /// <summary>Poncelet strength term R_t, Pa. Roughly the quasi-static pressure
        /// needed to open a cavity in the material.</summary>
        public double StrengthTerm;

        /// <summary>Yield strength, Pa. Used when deciding whether a hard target
        /// deforms the projectile rather than the other way round.</summary>
        public double YieldStrength;

        /// <summary>
        /// True for fluid-like media (tissue, gelatin, water) where a temporary
        /// cavity forms and then collapses. Rigid media crack or spall instead, and
        /// the temporary-cavity readout is meaningless for them.
        /// </summary>
        public bool IsFluidLike;

        /// <summary>
        /// True for fibrous materials that pack into an open nose cavity and stop it
        /// expanding. Fabric, carpet, heavy clothing. This is why defensive ammunition
        /// is tested through denim: a hollow point that plugs behaves like a
        /// full-metal-jacket and over-penetrates.
        /// </summary>
        public bool PlugsCavities;

        public static TargetMedium Create(
            string id, string name, double density, double strengthMPa,
            double yieldMPa, bool fluidLike, bool plugsCavities = false) => new TargetMedium
            {
                Id = id,
                DisplayName = name,
                Density = density,
                StrengthTerm = strengthMPa * 1e6,
                YieldStrength = yieldMPa * 1e6,
                IsFluidLike = fluidLike,
                PlugsCavities = plugsCavities
            };
    }

    /// <summary>A finite thickness of a medium. Targets are built as ordered stacks
    /// of these, so "denim over gelatin" or "sheet steel then wood" is just a list.</summary>
    [Serializable]
    public struct TargetLayer
    {
        public TargetMedium Medium;

        /// <summary>Thickness along the projectile's path, m. Use
        /// double.PositiveInfinity for a semi-infinite backstop.</summary>
        public double Thickness;

        public static TargetLayer Of(in TargetMedium medium, double thickness)
            => new TargetLayer { Medium = medium, Thickness = thickness };
    }

    /// <summary>
    /// Standard test media.
    ///
    /// CALIBRATION NOTE: the gelatin strength term is set so that a conventional
    /// 9 mm full-metal-jacket projectile at typical service velocity penetrates
    /// roughly 60-70 cm, which is the well-established figure for non-expanding
    /// handgun bullets in 10% ordnance gelatin. The remaining media are scaled from
    /// published penetration data of the same kind. Treat them as a calibrated
    /// starting dataset rather than measured constants -- and note that gelatin is a
    /// TISSUE SIMULANT, not tissue: it is used because it is reproducible, which is
    /// exactly why it is the right thing for the player's test range to be made of.
    /// </summary>
    public static class TargetMediumLibrary
    {
        public const string Gelatin = "gelatin_10";
        public const string SoftTissue = "soft_tissue";
        public const string Water = "water";
        public const string Pine = "pine";
        public const string Denim = "denim";
        public const string MildSteelPlate = "mild_steel_plate";
        public const string HardenedSteelPlate = "hardened_steel_plate";
        public const string Aramid = "aramid_fabric";
        public const string Air = "air";

        private static readonly Dictionary<string, TargetMedium> Table = BuildDefaults();

        public static bool TryGet(string id, out TargetMedium medium) => Table.TryGetValue(id, out medium);

        public static TargetMedium Get(string id)
        {
            if (!Table.TryGetValue(id, out var m))
                throw new KeyNotFoundException($"Unknown target medium '{id}'.");
            return m;
        }

        public static void Register(TargetMedium medium) => Table[medium.Id] = medium;

        public static IEnumerable<TargetMedium> All => Table.Values;

        private static Dictionary<string, TargetMedium> BuildDefaults()
        {
            var t = new Dictionary<string, TargetMedium>();

            // The reference test medium. Calibrated as described above.
            t[Gelatin] = TargetMedium.Create(Gelatin, "10% Ordnance Gelatin", 1030, 4.5, 0.1, true);

            // Slightly denser and marginally tougher than gelatin.
            t[SoftTissue] = TargetMedium.Create(SoftTissue, "Soft Tissue", 1040, 5.0, 0.15, true);

            // Essentially no strength term: water resists purely by inertia, which is
            // why projectiles that expand in it stop so abruptly.
            t[Water] = TargetMedium.Create(Water, "Water", 1000, 0.05, 0.0, true);

            t[Pine] = TargetMedium.Create(Pine, "Pine Board", 500, 35.0, 40.0, false);

            // Thin, weak, but genuinely important: fabric plugs a hollow point's
            // cavity and can stop it expanding at all. A classic failure mode.
            t[Denim] = TargetMedium.Create(Denim, "Denim Cloth", 800, 12.0, 15.0, false, plugsCavities: true);

            t[MildSteelPlate] = TargetMedium.Create(MildSteelPlate, "Mild Steel Plate", 7850, 1200.0, 250.0, false);
            t[HardenedSteelPlate] = TargetMedium.Create(HardenedSteelPlate, "Hardened Steel Plate", 7850, 3500.0, 1500.0, false);

            // Fibre armour resists mainly by tension in the weave, which the Poncelet
            // strength term stands in for.
            t[Aramid] = TargetMedium.Create(Aramid, "Aramid Fabric", 1440, 700.0, 500.0, false, plugsCavities: true);

            // Gaps between plates. Present so a layered target can include free space
            // without a special case in the solver.
            t[Air] = TargetMedium.Create(Air, "Air Gap", 1.225, 0.0, 0.0, true);

            return t;
        }

        /// <summary>The standard calibrated test block: a bare 80 cm gelatin block,
        /// which is what the player's range uses by default.</summary>
        public static TargetLayer[] BareGelatinBlock(double thickness = 0.8)
            => new[] { TargetLayer.Of(Get(Gelatin), thickness) };

        /// <summary>Four layers of denim over gelatin -- the standard heavy-clothing
        /// test that exposes hollow points which plug and fail to expand.</summary>
        public static TargetLayer[] ClothedGelatinBlock(double thickness = 0.8)
            => new[]
            {
                TargetLayer.Of(Get(Denim), 0.004),
                TargetLayer.Of(Get(Gelatin), thickness)
            };
    }
}
