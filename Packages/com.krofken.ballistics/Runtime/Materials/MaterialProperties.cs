using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Bulk mechanical properties of a solid used in a projectile (core, jacket,
    /// case) or in a target.
    ///
    /// These are not gameplay stats -- every field is a real measurable quantity in
    /// SI units, and every one of them is read by a solver:
    ///
    ///   Density                 -> projectile mass, hence momentum and sectional density
    ///   YieldStrength           -> whether the projectile deforms on impact at all
    ///   UltimateTensileStrength -> whether it holds together or fragments once deforming
    ///   ElongationAtBreak       -> how far it can mushroom before it tears apart
    ///   BrinellHardness         -> engraving resistance in the bore, and target resistance
    ///   YoungsModulus           -> elastic response, shock transmission
    ///
    /// The design consequence to keep in mind: a projectile deforms when the
    /// stagnation pressure it experiences (roughly 0.5 * rho_target * v^2) exceeds
    /// its core's yield strength. Lead yields near 12 MPa, copper near 70 MPa,
    /// hardened steel past 1 GPa. At 400 m/s into tissue-density medium the
    /// stagnation pressure is about 80 MPa -- which is why lead mushrooms, gilding
    /// metal barely does, and a steel core sails straight through unchanged.
    /// That single comparison produces four completely different terminal
    /// behaviours from one set of equations.
    /// </summary>
    [Serializable]
    public struct MaterialProperties
    {
        /// <summary>Stable identifier. Used for save data and cross-referencing the
        /// game-side economy; the physics never branches on it.</summary>
        public string Id;

        /// <summary>Human-readable name for UI.</summary>
        public string DisplayName;

        /// <summary>Mass density, kg/m^3.</summary>
        public double Density;

        /// <summary>Tensile yield strength, Pa. Onset of permanent deformation.</summary>
        public double YieldStrength;

        /// <summary>Ultimate tensile strength, Pa. Onset of fracture.</summary>
        public double UltimateTensileStrength;

        /// <summary>Young's modulus, Pa.</summary>
        public double YoungsModulus;

        /// <summary>
        /// Elongation at break, dimensionless strain (0.5 = 50% stretch before fracture).
        /// This is the ductility knob that separates "mushrooms into a wide, intact
        /// mass" from "shatters into fragments". Sintered frangible metals sit near
        /// zero; annealed lead and copper are highly ductile.
        /// </summary>
        public double ElongationAtBreak;

        /// <summary>Brinell hardness number, HB (kgf/mm^2 by convention -- left in its
        /// traditional unit because every published material table uses it; converted
        /// where a solver needs pressure).</summary>
        public double BrinellHardness;

        /// <summary>Melting point, K. Relevant to incendiary and high-friction effects.</summary>
        public double MeltingPoint;

        /// <summary>Specific heat capacity, J/(kg*K).</summary>
        public double SpecificHeat;

        /// <summary>
        /// Chemical energy released on reaction, J/kg. Zero for inert structural
        /// metals. Non-zero for incendiary and reactive fillers, where it is released
        /// into the target once the payload is initiated by impact.
        /// </summary>
        public double ReactiveEnergyDensity;

        /// <summary>
        /// Impact stress required to initiate <see cref="ReactiveEnergyDensity"/>, Pa.
        /// Ignored when the material is inert. This is what stops an incendiary from
        /// going off in a soft target -- it needs a hard enough hit.
        /// </summary>
        public double InitiationThreshold;

        /// <summary>Brinell hardness expressed as a pressure in Pa (1 HB ~= 9.80665 MPa).</summary>
        public double HardnessPascals => BrinellHardness * 9.80665e6;

        /// <summary>True if the material carries a chemical payload.</summary>
        public bool IsReactive => ReactiveEnergyDensity > 0.0;
    }
}
