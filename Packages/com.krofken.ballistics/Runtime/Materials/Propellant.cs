using System;
using System.Collections.Generic;

namespace Krofken.Ballistics
{
    /// <summary>
    /// Thermochemical properties of a propellant. These describe the CHEMISTRY only.
    /// How fast a charge actually burns is chemistry combined with grain geometry
    /// (see <see cref="GrainGeometry"/>), which is why the same powder chemistry can
    /// be sold as a fast pistol powder or a slow magnum powder -- the difference is
    /// the size of the grain, not what it is made of.
    ///
    /// Splitting them this way gives the player two independent, physically
    /// meaningful knobs instead of one opaque "powder type" dropdown.
    /// </summary>
    [Serializable]
    public struct PropellantProperties
    {
        public string Id;
        public string DisplayName;

        /// <summary>Density of the solid propellant, kg/m^3. Sets how much charge
        /// physically fits in a case before it is compressed.</summary>
        public double SolidDensity;

        /// <summary>
        /// Impetus (force constant) f, J/kg. f = R * T_flame / M, where M is the mean
        /// molar mass of the combustion gases. This is the single most important
        /// number: it is the work the propellant can theoretically do per kilogram.
        /// Smokeless powders run roughly 950 kJ/kg (single-base) to 1250 kJ/kg
        /// (hot double-base); black powder is under a third of that.
        /// </summary>
        public double Impetus;

        /// <summary>
        /// Covolume alpha, m^3/kg. The volume the combustion gas molecules themselves
        /// occupy, from the Nobel-Abel equation of state P*(V - alpha*m) = m*f.
        /// At the ~300 MPa inside a rifle chamber the gas is dense enough that
        /// ignoring this term overestimates pressure badly -- it is not a refinement,
        /// it is required for the model to be usable at all.
        /// </summary>
        public double Covolume;

        /// <summary>Ratio of specific heats, gamma. The solver uses theta = gamma - 1.</summary>
        public double HeatCapacityRatio;

        /// <summary>Adiabatic flame temperature, K. Drives barrel erosion, muzzle
        /// flash, and the incendiary potential of the muzzle gases.</summary>
        public double FlameTemperature;

        /// <summary>
        /// Coefficient u1 in Vieille's burn law, r = u1 * P^n, in m/(s*Pa^n).
        /// r is the linear regression rate: how fast the burning surface eats into
        /// the solid, perpendicular to that surface.
        /// </summary>
        public double BurnRateCoefficient;

        /// <summary>
        /// Pressure exponent n in Vieille's law, dimensionless. Nitrocellulose
        /// propellants sit near 0.8-0.9, meaning burn rate is very nearly
        /// proportional to pressure -- this positive feedback (more pressure, faster
        /// burn, more gas, more pressure) is exactly why overcharging goes
        /// catastrophic rather than merely hot.
        /// </summary>
        public double BurnRateExponent;

        /// <summary>theta = gamma - 1, the term appearing in the energy balance.</summary>
        public double Theta => HeatCapacityRatio - 1.0;
    }

    /// <summary>Idealised grain shapes, each with a closed-form burn geometry.</summary>
    public enum GrainShape
    {
        /// <summary>Spherical "ball" powder. Strongly degressive.</summary>
        Sphere = 0,
        /// <summary>Thin flake or disc, burning on its two large faces. Neutral.</summary>
        Flake = 1,
        /// <summary>Solid extruded cord with no perforation. Degressive.</summary>
        Cord = 2,
        /// <summary>Single-perforated tube, burning inside and out. Neutral.</summary>
        SinglePerforated = 3,
        /// <summary>Seven-perforated cylinder. Progressive.</summary>
        SevenPerforated = 4,
        /// <summary>Caller supplies chi/lambda/mu directly.</summary>
        Custom = 5
    }

    /// <summary>
    /// Physical geometry of the propellant grains.
    ///
    /// The burnt-mass fraction is expressed through the standard form function
    ///
    ///     psi(z) = chi * z * (1 + lambda*z + mu*z^2)
    ///
    /// where z is the fraction of the web burnt through (0 at ignition, 1 when the
    /// grain is consumed). Three coefficients cover every common grain shape, and
    /// each preset below is DERIVED, not fitted -- assume uniform regression at rate
    /// r into the solid and integrate the remaining volume:
    ///
    ///   Sphere, web = radius R:
    ///       V(z) = V0*(1-z)^3  ->  psi = 1-(1-z)^3 = 3z - 3z^2 + z^3
    ///       chi = 3, lambda = -1, mu = 1/3.               Surface shrinks: DEGRESSIVE.
    ///
    ///   Long solid cord, web = radius R:
    ///       V(z) = V0*(1-z)^2  ->  psi = 2z - z^2
    ///       chi = 2, lambda = -1/2, mu = 0.               DEGRESSIVE.
    ///
    ///   Flake of thickness 2e burning on both faces:
    ///       area is constant, V(z) = V0*(1-z)  ->  psi = z
    ///       chi = 1, lambda = 0, mu = 0.                  NEUTRAL.
    ///
    ///   Tube burning inner and outer surfaces:
    ///       outer circumference shrinks exactly as fast as the inner grows, so total
    ///       area is constant and psi = z again.
    ///       chi = 1, lambda = 0, mu = 0.                  NEUTRAL.
    ///
    ///   Seven-perforated: seven inner surfaces grow while one outer shrinks, so
    ///       total burning area INCREASES with z.          PROGRESSIVE.
    ///       The coefficients below are approximate for the pre-sliver phase;
    ///       slivering (the leftover corners after the perforations meet, which burn
    ///       degressively at the very end) is not modelled.
    ///
    /// Why this matters to the player: a degressive grain dumps its gas early and
    /// spikes pressure near the chamber; a progressive grain keeps generating gas as
    /// the bullet moves and the volume grows, holding pressure up for longer. Same
    /// charge mass, same chemistry, very different peak pressure and muzzle velocity.
    /// </summary>
    [Serializable]
    public struct GrainGeometry
    {
        /// <summary>
        /// Web thickness e1, in metres: the distance the flame front must travel to
        /// consume the grain. THE dominant control on burn speed -- doubling the web
        /// roughly doubles the burn time at fixed pressure.
        /// Fast pistol powders are around 25 micrometres; slow magnum rifle powders
        /// reach half a millimetre.
        /// </summary>
        public double WebThickness;

        public GrainShape Shape;

        /// <summary>Form function coefficient chi.</summary>
        public double Chi;
        /// <summary>Form function coefficient lambda.</summary>
        public double Lambda;
        /// <summary>Form function coefficient mu.</summary>
        public double Mu;

        /// <summary>
        /// Surface deterrent coating factor, 0..1. Real progressive-burning powders
        /// are surface-treated with a burn inhibitor so the outside burns slower than
        /// the core. 0 = untreated, higher = more strongly deterred early burn.
        /// Applied as a multiplier that relaxes towards 1 as z increases.
        /// </summary>
        public double DeterrentCoating;

        /// <summary>
        /// Fraction of a poured volume that is actually solid propellant, 0..1.
        ///
        /// This is a PACKING property, not a chemical one, which is why it lives on
        /// the grain rather than the propellant: spheres tumble into a dense bed,
        /// flakes bridge and leave a lot of air. It has no effect on the burn -- the
        /// solver correctly subtracts only the solid volume -- but it decides whether
        /// a charge PHYSICALLY FITS in the case, which is a hard limit the player
        /// runs into constantly.
        ///
        /// Solid propellant is about 1600 kg/m^3, so a packing fraction of 0.6 gives
        /// the ~0.95 g/cm^3 bulk density of a real ball powder, and 0.35 gives the
        /// ~0.55 g/cm^3 of a bulky flake powder.
        /// </summary>
        public double PackingFraction;

        public static GrainGeometry Create(GrainShape shape, double webThickness, double deterrentCoating = 0.0)
        {
            var g = new GrainGeometry
            {
                Shape = shape,
                WebThickness = webThickness,
                DeterrentCoating = Clamp01(deterrentCoating)
            };

            switch (shape)
            {
                case GrainShape.Sphere:
                    g.Chi = 3.0; g.Lambda = -1.0; g.Mu = 1.0 / 3.0;
                    // Random close packing of spheres.
                    g.PackingFraction = 0.62;
                    break;
                case GrainShape.Cord:
                    g.Chi = 2.0; g.Lambda = -0.5; g.Mu = 0.0;
                    g.PackingFraction = 0.55;
                    break;
                case GrainShape.Flake:
                    g.Chi = 1.0; g.Lambda = 0.0; g.Mu = 0.0;
                    // Flakes bridge against each other and pack badly -- which is why
                    // bulky flake powders fill a case at a low charge weight.
                    g.PackingFraction = 0.35;
                    break;
                case GrainShape.SinglePerforated:
                    g.Chi = 1.0; g.Lambda = 0.0; g.Mu = 0.0;
                    // The perforation is dead volume on top of the packing loss.
                    g.PackingFraction = 0.50;
                    break;
                case GrainShape.SevenPerforated:
                    // Approximate pre-sliver coefficients; chi < 1 with lambda > 0 is
                    // the signature of a progressive grain.
                    g.Chi = 0.72; g.Lambda = 0.40; g.Mu = -0.08;
                    g.PackingFraction = 0.48;
                    break;
                default:
                    g.Chi = 1.0; g.Lambda = 0.0; g.Mu = 0.0;
                    g.PackingFraction = 0.55;
                    break;
            }

            return g;
        }

        /// <summary>Creates a grain with hand-specified form function coefficients.</summary>
        public static GrainGeometry Custom(double webThickness, double chi, double lambda, double mu,
            double deterrentCoating = 0.0) => new GrainGeometry
            {
                Shape = GrainShape.Custom,
                WebThickness = webThickness,
                Chi = chi,
                Lambda = lambda,
                Mu = mu,
                DeterrentCoating = Clamp01(deterrentCoating),
                PackingFraction = 0.55
            };

        /// <summary>
        /// Burnt mass fraction psi for a burnt-web fraction z. Clamped to [0,1]:
        /// the polynomial is only valid on that interval and lets the integrator
        /// overshoot slightly at large steps.
        /// </summary>
        public double BurntFraction(double z)
        {
            if (z <= 0.0) return 0.0;
            if (z >= 1.0) return 1.0;

            double psi = Chi * z * (1.0 + Lambda * z + Mu * z * z);
            return psi < 0.0 ? 0.0 : (psi > 1.0 ? 1.0 : psi);
        }

        /// <summary>
        /// Burn rate multiplier from the deterrent coating at burnt-web fraction z.
        /// Starts at (1 - coating) and relaxes to 1 as the flame front passes beyond
        /// the treated surface layer. The exponential shape is a modelling choice,
        /// not a measured law; the physically real part is that deterrence decays.
        /// </summary>
        public double DeterrentFactor(double z)
        {
            if (DeterrentCoating <= 0.0) return 1.0;

            // Fraction of the web that carries deterrent. Real treated powders are
            // deterred well into the grain, not just at the surface -- the point is to
            // hold the early burn back long enough for the projectile to start moving
            // and open up some volume, which is what stops pressure spiking in the
            // first few millimetres of travel.
            const double penetrationDepth = 0.45;

            double decay = Math.Exp(-z / penetrationDepth);
            return 1.0 - DeterrentCoating * decay;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }

    /// <summary>A quantity of a specific propellant with a specific grain geometry.
    /// This is what a player actually loads into a case.</summary>
    [Serializable]
    public struct PropellantCharge
    {
        public PropellantProperties Propellant;
        public GrainGeometry Grain;

        /// <summary>Charge mass, kg.</summary>
        public double Mass;

        /// <summary>
        /// Volume the solid propellant material occupies, m^3. This is the term the
        /// gas equation of state needs -- the space genuinely unavailable to the gas.
        /// </summary>
        public double SolidVolume => Propellant.SolidDensity > 0 ? Mass / Propellant.SolidDensity : 0.0;

        /// <summary>
        /// Volume the charge occupies as a poured heap, m^3, including the air
        /// between grains. THIS is the number to compare against case capacity when
        /// asking "does it fit" -- the solid volume would say a charge fits when in
        /// reality it overflows the case mouth.
        /// </summary>
        public double BulkVolume
        {
            get
            {
                double packing = Grain.PackingFraction > 0.0 ? Grain.PackingFraction : 0.55;
                return SolidVolume / packing;
            }
        }
    }

    /// <summary>
    /// Representative propellant chemistries. As with the material library these are
    /// plausible published-range values, not certified lot data, and the table is
    /// replaceable at runtime.
    /// </summary>
    public static class PropellantLibrary
    {
        public const string SingleBase = "nc_single_base";
        public const string DoubleBase = "nc_double_base";
        public const string TripleBase = "nc_triple_base";
        public const string BlackPowder = "black_powder";

        private static readonly Dictionary<string, PropellantProperties> Table = BuildDefaults();

        public static bool TryGet(string id, out PropellantProperties p) => Table.TryGetValue(id, out p);

        public static PropellantProperties Get(string id)
        {
            if (!Table.TryGetValue(id, out var p))
                throw new KeyNotFoundException($"Unknown propellant id '{id}'.");
            return p;
        }

        public static void Register(PropellantProperties p) => Table[p.Id] = p;

        public static IEnumerable<PropellantProperties> All => Table.Values;

        private static Dictionary<string, PropellantProperties> BuildDefaults()
        {
            var t = new Dictionary<string, PropellantProperties>();

            // Plain nitrocellulose. The workhorse: cool-burning, easy on barrels.
            t[SingleBase] = new PropellantProperties
            {
                Id = SingleBase,
                DisplayName = "Single-Base Nitrocellulose",
                SolidDensity = 1600,
                Impetus = 980e3,
                Covolume = 1.00e-3,
                HeatCapacityRatio = 1.25,
                FlameTemperature = 2900,
                BurnRateCoefficient = 1.0e-8,
                BurnRateExponent = 0.85
            };

            // Nitrocellulose + nitroglycerin. More energy per kilogram, hotter, and
            // correspondingly harder on the throat of the barrel.
            t[DoubleBase] = new PropellantProperties
            {
                Id = DoubleBase,
                DisplayName = "Double-Base (NC/NG)",
                SolidDensity = 1620,
                Impetus = 1150e3,
                Covolume = 0.95e-3,
                HeatCapacityRatio = 1.22,
                FlameTemperature = 3500,
                BurnRateCoefficient = 1.35e-8,
                BurnRateExponent = 0.87
            };

            // Nitroguanidine added as a coolant: nearly double-base energy at
            // single-base flame temperature. Expensive; used where barrel life matters.
            t[TripleBase] = new PropellantProperties
            {
                Id = TripleBase,
                DisplayName = "Triple-Base (NC/NG/NQ)",
                SolidDensity = 1660,
                Impetus = 1060e3,
                Covolume = 1.05e-3,
                HeatCapacityRatio = 1.24,
                FlameTemperature = 2650,
                BurnRateCoefficient = 0.95e-8,
                BurnRateExponent = 0.83
            };

            // Roughly a third the impetus, and it leaves most of its mass behind as
            // solid residue. Included as the low-tech baseline.
            t[BlackPowder] = new PropellantProperties
            {
                Id = BlackPowder,
                DisplayName = "Black Powder",
                SolidDensity = 1750,
                Impetus = 300e3,
                Covolume = 0.60e-3,
                HeatCapacityRatio = 1.22,
                FlameTemperature = 2200,
                BurnRateCoefficient = 3.2e-4,
                BurnRateExponent = 0.30
            };

            return t;
        }
    }
}
