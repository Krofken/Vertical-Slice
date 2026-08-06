using System.Collections.Generic;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Starting dataset of real engineering materials.
    ///
    /// PROVENANCE: these are representative published handbook values for the
    /// annealed or commonly-used temper of each material. They are a defensible
    /// starting point, not certified data -- real alloys vary by several tens of
    /// percent with temper and composition. Anything the game exposes as a
    /// "material" is just a row in here, so replacing this table with measured data
    /// requires no code changes anywhere else.
    ///
    /// The library is deliberately a plain dictionary rather than ScriptableObjects:
    /// keeping it in the dependency-free assembly means the solvers, the unit tests
    /// and any offline validation tool all see the same numbers as the game does.
    /// The Unity layer can still author overrides and register them at startup.
    /// </summary>
    public static class MaterialLibrary
    {
        // ---- Stable ids ---------------------------------------------------
        // String ids rather than an enum: an enum would bake the material list
        // into the assembly, and the whole point is that content can be added
        // without recompiling the physics.
        public const string Lead = "lead";
        public const string HardenedLead = "lead_antimony";
        public const string Copper = "copper";
        public const string GildingMetal = "gilding_metal";
        public const string CartridgeBrass = "cartridge_brass";
        public const string MildSteel = "mild_steel";
        public const string HardenedSteel = "hardened_steel";
        public const string TungstenCarbide = "tungsten_carbide";
        public const string TungstenHeavyAlloy = "tungsten_heavy_alloy";
        public const string Aluminium = "aluminium_7075";
        public const string Bismuth = "bismuth";
        public const string SinteredIron = "sintered_iron";
        public const string Polymer = "polymer_hdpe";
        public const string Zinc = "zinc";
        public const string Thermite = "thermite";
        public const string PhosphorusCompound = "phosphorus_compound";

        private static readonly Dictionary<string, MaterialProperties> Table = BuildDefaults();

        /// <summary>Looks up a material by id. Returns false rather than throwing so
        /// content errors surface as a validation message, not a crash mid-simulation.</summary>
        public static bool TryGet(string id, out MaterialProperties material) => Table.TryGetValue(id, out material);

        /// <summary>Looks up a material by id, throwing if absent. Use from code paths
        /// where the id is a compile-time constant from this class.</summary>
        public static MaterialProperties Get(string id)
        {
            if (!Table.TryGetValue(id, out var m))
                throw new KeyNotFoundException($"Unknown material id '{id}'.");
            return m;
        }

        /// <summary>Adds or replaces a material. Lets the Unity layer inject authored
        /// or fantasy materials without touching this file.</summary>
        public static void Register(MaterialProperties material) => Table[material.Id] = material;

        public static IEnumerable<MaterialProperties> All => Table.Values;

        private static Dictionary<string, MaterialProperties> BuildDefaults()
        {
            var t = new Dictionary<string, MaterialProperties>();

            void Add(
                string id, string name,
                double density, double yieldMPa, double utsMPa, double youngsGPa,
                double elongation, double brinell, double meltK, double specificHeat,
                double reactiveEnergy = 0.0, double initiationMPa = 0.0)
            {
                t[id] = new MaterialProperties
                {
                    Id = id,
                    DisplayName = name,
                    Density = density,
                    YieldStrength = yieldMPa * 1e6,
                    UltimateTensileStrength = utsMPa * 1e6,
                    YoungsModulus = youngsGPa * 1e9,
                    ElongationAtBreak = elongation,
                    BrinellHardness = brinell,
                    MeltingPoint = meltK,
                    SpecificHeat = specificHeat,
                    ReactiveEnergyDensity = reactiveEnergy,
                    InitiationThreshold = initiationMPa * 1e6
                };
            }

            //   id                    name                  rho    yield   UTS   E     elong  HB    melt   c

            // Soft, dense, cheap. Deforms readily at any useful impact velocity --
            // the classic expanding-bullet core.
            Add(Lead, "Pure Lead", 11340, 12, 18, 16, 0.50, 5, 600.6, 128);

            // Antimony-alloyed lead. Roughly doubles the yield strength, which is
            // enough to stop it from stripping in the bore at higher velocity and
            // to make expansion less violent.
            Add(HardenedLead, "Hardened Lead", 11000, 25, 40, 17, 0.20, 14, 570.0, 130);

            // Ductile jacket material. Yield sits near the stagnation pressure a
            // bullet sees at typical handgun velocity, so copper jackets are right
            // on the edge of deforming -- which is why jacket design matters so much.
            Add(Copper, "Copper", 8960, 70, 220, 110, 0.45, 45, 1357.8, 385);

            // 95/5 copper-zinc. The standard jacket alloy: harder than copper,
            // still ductile enough to draw.
            Add(GildingMetal, "Gilding Metal", 8860, 100, 280, 115, 0.40, 55, 1330.0, 380);

            // 70/30 cartridge brass in the work-hardened temper a drawn case ends up
            // in. Case strength is what actually contains chamber pressure.
            Add(CartridgeBrass, "Cartridge Brass", 8530, 200, 400, 110, 0.30, 100, 1188.0, 380);

            Add(MildSteel, "Mild Steel", 7850, 250, 400, 200, 0.20, 120, 1723.0, 470);

            // Quenched and tempered alloy steel. Yield is an order of magnitude above
            // any stagnation pressure a bullet meets in soft tissue, so a core of
            // this passes through undeformed -- the armour-piercing behaviour falls
            // out of the material property, it is not a special case in the solver.
            Add(HardenedSteel, "Hardened Steel", 7850, 1500, 1800, 205, 0.10, 500, 1700.0, 470);

            // Extremely hard and dense but brittle. Best penetrator, worst at
            // surviving an oblique impact.
            Add(TungstenCarbide, "Tungsten Carbide", 15600, 2500, 550, 600, 0.01, 1400, 3058.0, 200);

            // Sintered tungsten in a ductile binder: nearly the density of WC with
            // usable toughness.
            Add(TungstenHeavyAlloy, "Tungsten Heavy Alloy", 17600, 1000, 1200, 360, 0.10, 350, 1700.0, 145);

            // Low density. Poor sectional density means it bleeds velocity fast --
            // useful when you want short range or low penetration by design.
            Add(Aluminium, "Aluminium 7075-T6", 2810, 500, 570, 72, 0.11, 150, 750.0, 960);

            // Nearly lead's density, but brittle -- shatters instead of mushrooming.
            Add(Bismuth, "Bismuth", 9780, 20, 30, 32, 0.005, 7, 544.6, 122);

            // Powder-metal compact. Holds together in the bore, disintegrates on
            // impact with anything hard. The frangible round's core.
            // Deliberately under-sintered so it holds together in the bore and comes
            // apart on impact. The low yield is the design intent, not a defect.
            Add(SinteredIron, "Sintered Iron", 6500, 45, 55, 120, 0.01, 60, 1800.0, 450);

            Add(Polymer, "HDPE Polymer", 960, 26, 33, 1, 1.00, 2, 403.0, 1900);

            Add(Zinc, "Zinc", 7140, 100, 150, 100, 0.20, 40, 692.7, 390);

            // ---- Reactive fillers ---------------------------------------------
            // Not structural: these are payloads carried in a cavity. Mechanical
            // numbers describe the packed powder, which is weak; the interesting
            // fields are the last two.

            // Fe2O3 + Al thermite, ~3.9 MJ/kg, needs a hard impact to initiate.
            Add(Thermite, "Thermite Compound", 4200, 5, 5, 5, 0.001, 3, 1800.0, 700,
                reactiveEnergy: 3.9e6, initiationMPa: 200);

            // Phosphorus-based incendiary. Far more energetic per kilogram and
            // initiates on a much softer hit -- it will go off in tissue, where
            // thermite will not.
            Add(PhosphorusCompound, "Phosphorus Compound", 1820, 1, 1, 1, 0.001, 1, 317.3, 770,
                reactiveEnergy: 24.0e6, initiationMPa: 15);

            return t;
        }
    }
}
