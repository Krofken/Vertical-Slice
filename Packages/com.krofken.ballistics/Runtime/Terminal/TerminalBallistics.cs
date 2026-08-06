using System;

namespace Krofken.Ballistics
{
    /// <summary>
    /// The impact-relevant reduction of a projectile design: everything the terminal
    /// solver needs, pre-computed from geometry and materials.
    ///
    /// The fields are exposed rather than hidden because the workshop UI explains
    /// them back to the player -- "core yields at 12 MPa, impact drives 82 MPa, so it
    /// mushrooms" is the sentence that teaches the mechanic. A hidden stat would not.
    /// </summary>
    [Serializable]
    public struct TerminalProjectile
    {
        /// <summary>Mass at impact, kg.</summary>
        public double Mass;

        /// <summary>Undeformed diameter, m.</summary>
        public double Calibre;

        /// <summary>Flat tip diameter, m. A wide meplat starts the projectile with a
        /// much higher drag coefficient inside the target.</summary>
        public double MeplatDiameter;

        /// <summary>
        /// Effective yield strength of the nose structure, Pa: the area-weighted
        /// average of the core and jacket, reduced by any cavity that lets the nose
        /// fold outward. Deformation begins when the stagnation pressure of the
        /// impact exceeds this.
        /// </summary>
        public double EffectiveYieldStrength;

        /// <summary>Largest diameter the nose can reach before the material tears,
        /// as a multiple of the calibre. Set by ductility and by how open the nose is.</summary>
        public double MaxExpansionRatio;

        /// <summary>Hoop strain the nose material tolerates before fracture.
        /// Exceeding it while still deforming is what causes fragmentation.</summary>
        public double ElongationLimit;

        /// <summary>
        /// True when the nose material fractures instead of flowing.
        ///
        /// This is the frangible/expanding fork, and it is a MATERIAL property, not
        /// an ammunition category: a material loaded past its yield point either
        /// deforms plastically (lead, copper -- tens of percent of usable strain) or
        /// cracks apart (sintered compacts, bismuth -- a fraction of a percent). The
        /// solver asks only "is it brittle", never "is this a frangible round".
        /// </summary>
        public bool IsBrittle;

        /// <summary>
        /// Expansion ceiling once the nose cavity has been plugged, as a multiple of
        /// calibre. A hollow point that drives through heavy fabric packs its cavity
        /// solid, and a packed cavity cannot initiate a petal -- so the round behaves
        /// like a full-metal-jacket and drives straight through. This is a
        /// well-documented real failure mode and the reason the clothed-gelatin test
        /// exists at all.
        /// </summary>
        public double PluggedExpansionRatio;

        /// <summary>Mass of reactive payload carried, kg. Zero for an inert projectile.</summary>
        public double PayloadMass;

        /// <summary>Chemical energy per kilogram of payload, J/kg.</summary>
        public double PayloadEnergyDensity;

        /// <summary>Impact stagnation pressure needed to set the payload off, Pa.</summary>
        public double PayloadInitiationThreshold;

        /// <summary>
        /// Elongation at break below which a material is treated as brittle. 5% strain
        /// separates the structural metals cleanly: lead and copper sit at 20-50%,
        /// sintered compacts and bismuth at well under 1%. Nothing real sits near the
        /// boundary, so the threshold is not a delicate tuning parameter.
        /// </summary>
        public const double BrittleElongationThreshold = 0.05;

        /// <summary>Undeformed frontal area, m^2.</summary>
        public double FrontalArea => Math.PI * Calibre * Calibre * 0.25;

        /// <summary>Sectional density, kg/m^2.</summary>
        public double SectionalDensity => FrontalArea > 0 ? Mass / FrontalArea : 0.0;

        /// <summary>
        /// Reduces a full design to its impact-relevant properties.
        ///
        /// TWO MODELLING DECISIONS ARE MADE HERE, both flagged because they are
        /// choices rather than derivations:
        ///
        /// 1. EXPANSION REQUIRES AN OPENING. A projectile with a continuous jacket
        ///    over the nose and no cavity has no free edge for a petal to start from,
        ///    and its core is fully confined -- which is why real full-metal-jacket
        ///    bullets barely deform even well past their material's yield stress.
        ///    Openness is therefore a GATE on how far the nose can expand, not merely
        ///    a modifier. Without this a jacketed round would incorrectly mushroom.
        ///
        /// 2. THE CAVITY WEAKENS THE NOSE. A hollow point yields at lower pressure
        ///    than a solid of the same material, because the wall is thin and
        ///    unsupported. Modelled as a multiplicative reduction scaled by how wide
        ///    and how deep the cavity is.
        ///
        /// Everything else -- whether it expands at all, how far, whether it tears
        /// apart -- follows from the material properties with no per-archetype
        /// special-casing anywhere in the solver.
        /// </summary>
        public static TerminalProjectile FromDesign(
            in ProjectileGeometry geometry,
            in ProjectileMaterials materials,
            in MassProperties mass)
        {
            var core = MaterialLibrary.TryGet(materials.CoreMaterialId, out var c)
                ? c
                : MaterialLibrary.Get(MaterialLibrary.Lead);

            bool hasJacket = geometry.JacketThickness > 0.0;
            var jacket = hasJacket && MaterialLibrary.TryGet(materials.JacketMaterialId, out var j)
                ? j
                : core;

            // Area-weighted blend across the nose cross-section.
            double radius = geometry.Radius;
            double innerRadius = Math.Max(0.0, radius - geometry.JacketThickness);
            double totalArea = radius * radius;                       // pi cancels in the ratio
            double coreArea = innerRadius * innerRadius;
            double jacketArea = totalArea - coreArea;

            double blendedYield = totalArea > 0.0
                ? (core.YieldStrength * coreArea + jacket.YieldStrength * jacketArea) / totalArea
                : core.YieldStrength;

            double blendedElongation = totalArea > 0.0
                ? (core.ElongationAtBreak * coreArea + jacket.ElongationAtBreak * jacketArea) / totalArea
                : core.ElongationAtBreak;

            // --- Cavity weakening -------------------------------------------
            double mouthRatio = geometry.Calibre > 0.0 ? geometry.CavityMouthDiameter / geometry.Calibre : 0.0;
            double depthRatio = geometry.Calibre > 0.0 ? geometry.CavityDepth / geometry.Calibre : 0.0;
            if (mouthRatio < 0.0) mouthRatio = 0.0;
            if (depthRatio > 1.0) depthRatio = 1.0;

            double weakening = 0.7 * Math.Sqrt(Math.Min(mouthRatio / 0.4, 1.0)) * Math.Min(depthRatio, 1.0);
            double effectiveYield = blendedYield * (1.0 - weakening);
            if (effectiveYield < 1e5) effectiveYield = 1e5; // floor: nothing is infinitely weak

            // --- Expansion ceiling ------------------------------------------
            // Openness normalised against a mouth of 0.4 calibre, which is about as
            // wide as a hollow point is ever made.
            double openness = Math.Min(mouthRatio / 0.4, 1.0);

            // How far the nose can open before the material runs out.
            //
            // Expanding a nose from d0 to D stretches it circumferentially by
            // (D - d0)/d0, so the ductility limit and the expansion limit are THE SAME
            // NUMBER -- a material that tolerates 50% strain can open to 1.5 calibres
            // and no further. Getting this wrong (letting the ceiling exceed the
            // fracture strain) makes every expanding projectile tear itself apart by
            // construction, which is exactly the bug the validation harness caught.
            //
            // Openness gates how much of that ductility is actually reachable: a
            // continuous jacket over a solid nose has no free edge to peel from and
            // gets almost none of it.
            double reachableDuctility = blendedElongation * (0.10 + 0.90 * openness);
            double maxExpansion = 1.0 + reachableDuctility;

            // --- Payload ------------------------------------------------------
            double payloadEnergyDensity = 0.0;
            double initiationThreshold = double.PositiveInfinity;
            if (!string.IsNullOrEmpty(materials.CavityFillMaterialId) &&
                MaterialLibrary.TryGet(materials.CavityFillMaterialId, out var fill) &&
                fill.IsReactive)
            {
                payloadEnergyDensity = fill.ReactiveEnergyDensity;
                initiationThreshold = fill.InitiationThreshold;
            }

            return new TerminalProjectile
            {
                Mass = mass.Mass,
                Calibre = geometry.Calibre,
                MeplatDiameter = geometry.MeplatDiameter,
                EffectiveYieldStrength = effectiveYield,
                MaxExpansionRatio = maxExpansion,
                ElongationLimit = blendedElongation,
                PayloadMass = mass.PayloadMass,
                PayloadEnergyDensity = payloadEnergyDensity,
                PayloadInitiationThreshold = initiationThreshold,

                // Brittleness is judged on the CORE ALONE, not the core/jacket blend.
                // The core is the overwhelming majority of the mass, and a thin
                // ductile jacket cannot hold a powdered-metal core together once it
                // starts to break up -- averaging the jacket's 45% elongation into a
                // core that tears at 1% wrongly makes a frangible projectile behave
                // like a solid one.
                IsBrittle = core.ElongationAtBreak < BrittleElongationThreshold,

                // What the nose could still open to if its cavity gets plugged.
                PluggedExpansionRatio = 1.0 + blendedElongation * 0.10
            };
        }
    }

    /// <summary>Outcome of an impact.</summary>
    [Serializable]
    public struct TerminalResult
    {
        /// <summary>Total distance driven into the target stack, m.</summary>
        public double PenetrationDepth;

        /// <summary>True if the projectile came out the far side of the last layer.
        /// This is the pass/fail an order asking for "no exit wound" is checked against.</summary>
        public bool Perforated;

        /// <summary>Velocity on exit, m/s. Zero when the projectile stopped inside.</summary>
        public double ExitVelocity;

        /// <summary>Impact velocity, m/s.</summary>
        public double ImpactVelocity;

        /// <summary>Kinetic energy at impact, J.</summary>
        public double ImpactEnergy;

        /// <summary>Energy left in the projectile on exit, J. Wasted energy, from the
        /// target's point of view.</summary>
        public double ExitEnergy;

        /// <summary>Energy given up inside the target, J, including any payload.</summary>
        public double EnergyDeposited;

        /// <summary>Chemical energy released by a reactive payload, J.</summary>
        public double ReactiveEnergyReleased;

        /// <summary>Depth at which the payload initiated, m. Negative if it never did.</summary>
        public double PayloadInitiationDepth;

        /// <summary>Largest frontal diameter reached, m.</summary>
        public double MaxExpandedDiameter;

        /// <summary>Expansion as a multiple of the original calibre.</summary>
        public double ExpansionRatio;

        /// <summary>True if the projectile tore apart.</summary>
        public bool Fragmented;

        /// <summary>True if a fibrous layer packed the nose cavity and prevented the
        /// expansion the design was relying on.</summary>
        public bool CavityPlugged;

        /// <summary>Depth at which fragmentation began, m. Negative if it never did.</summary>
        public double FragmentationDepth;

        /// <summary>Rough fragment count, for display only.</summary>
        public int FragmentCount;

        /// <summary>Widest temporary cavity opened in a fluid-like medium, m.
        /// Meaningless for rigid targets.</summary>
        public double MaxTemporaryCavityDiameter;

        /// <summary>Depth of the widest temporary cavity, m.</summary>
        public double MaxTemporaryCavityDepth;

        /// <summary>Index of the deepest layer reached.</summary>
        public int DeepestLayerReached;

        /// <summary>Bin width of <see cref="EnergyProfile"/>, m.</summary>
        public double ProfileBinWidth;

        /// <summary>
        /// Energy deposited per bin along the path, J. This IS the wound channel:
        /// plotted against depth it shows the player exactly where their round did
        /// its work -- all at the front for a frangible, spread evenly for a
        /// full-metal-jacket, concentrated after expansion for a hollow point.
        /// </summary>
        public double[] EnergyProfile;

        /// <summary>Number of populated bins in <see cref="EnergyProfile"/>.</summary>
        public int ProfileBinCount;

        /// <summary>Peak energy deposition rate, J/m, and where it occurred.</summary>
        public double PeakEnergyDepositionRate;
        public double PeakEnergyDepositionDepth;
    }

    /// <summary>
    /// Terminal ballistics: what happens after impact.
    ///
    /// MODEL -- one loop, marched forward in depth, containing four coupled effects:
    ///
    /// 1. RESISTANCE (Poncelet).
    ///        F = A * ( R_t + 0.5 * C_d * rho_t * v^2 )
    ///    with A the CURRENT frontal area, which changes as the projectile deforms.
    ///    That coupling is the entire model: expansion raises A, higher A raises F,
    ///    higher F sheds velocity faster, which lowers the pressure driving further
    ///    expansion. It settles itself.
    ///
    /// 2. DEFORMATION. The nose sees a stagnation pressure of roughly
    ///        q = 0.5 * rho_target * v^2
    ///    and yields when q exceeds its effective yield strength. This ONE comparison
    ///    is what separates the archetypes, with no branching on ammunition type:
    ///
    ///        400 m/s into gelatin  ->  q ~ 82 MPa
    ///          lead core           yields at ~12 MPa   -> mushrooms hard
    ///          gilding metal       yields at ~100 MPa  -> barely deforms
    ///          hardened steel core yields at ~1500 MPa -> passes through untouched
    ///          sintered iron       yields at ~100 MPa, tears at 1% strain -> shatters
    ///
    /// 3. FRACTURE. Expansion stretches the nose circumferentially. Once that hoop
    ///    strain passes the material's elongation at break, it tears. Ductile lead
    ///    reaches 50% strain and holds together as a mushroom; sintered iron and
    ///    bismuth reach 1% and come apart. Same equation, opposite outcomes.
    ///
    /// 4. REACTIVE PAYLOAD. A filled cavity initiates when impact pressure exceeds
    ///    its threshold, dumping chemical energy at that depth. A phosphorus filler
    ///    goes off in tissue; thermite needs to hit something hard first.
    ///
    /// LIMITS OF THE MODEL -- stated plainly because this module is the least
    /// rigorous of the three. Interior and exterior ballistics are textbook physics
    /// with well-established governing equations. Terminal ballistics in soft tissue
    /// is NOT: no first-principles model exists, and published work in the field is
    /// itself empirical. What is implemented here is a physically-structured model
    /// with real material properties driving it, calibrated against known penetration
    /// figures. It will rank designs correctly and respond correctly to changes. It
    /// is not a wound-ballistics prediction and should not be presented as one.
    /// </summary>
    public static class TerminalBallisticsSolver
    {
        /// <summary>Depth step, m. Half a millimetre resolves the expansion transient,
        /// which happens within the first two or three centimetres.</summary>
        public const double DefaultDepthStep = 5e-4;

        /// <summary>Below this speed the projectile is done, m/s.</summary>
        public const double StopVelocity = 5.0;

        /// <summary>Hard step cap, equal to 5 m of penetration at the default step.</summary>
        public const int MaxSteps = 10_000;

        /// <summary>Default energy-profile bin width, m.</summary>
        public const double DefaultBinWidth = 0.01;

        /// <summary>
        /// Distance over which a deforming nose relaxes towards its final diameter,
        /// expressed in calibres. Expansion completing within a few calibres of
        /// penetration is what is actually observed in recovered projectiles.
        /// </summary>
        public const double ExpansionLengthCalibres = 4.0;

        /// <summary>Half-angle at which a fragment cloud spreads, rad.</summary>
        public static readonly double FragmentSpreadAngle = Units.DegreesToRadians(12.0);

        /// <summary>
        /// Ceiling on the resistance multiplier applied when the target is harder than
        /// the projectile. Uncapped, a lead projectile against hardened steel would
        /// see a multiplier over a hundred, which is neither physical nor useful --
        /// beyond a certain point the projectile simply fails at the surface, and the
        /// outcome is the same whatever the ratio.
        /// </summary>
        public const double MaxHardnessPenalty = 4.0;

        public static TerminalResult Solve(
            in TerminalProjectile projectile,
            TargetLayer[] layers,
            double impactVelocity,
            double[] energyProfileBuffer = null,
            double binWidth = DefaultBinWidth,
            double depthStep = DefaultDepthStep)
        {
            var result = new TerminalResult
            {
                ImpactVelocity = impactVelocity,
                ImpactEnergy = 0.5 * projectile.Mass * impactVelocity * impactVelocity,
                PayloadInitiationDepth = -1.0,
                FragmentationDepth = -1.0,
                ProfileBinWidth = binWidth
            };

            if (layers == null || layers.Length == 0 || impactVelocity <= StopVelocity || projectile.Mass <= 0.0)
            {
                result.ExitVelocity = impactVelocity;
                result.Perforated = true;
                result.ExitEnergy = result.ImpactEnergy;
                return result;
            }

            var profile = energyProfileBuffer ?? new double[256];
            Array.Clear(profile, 0, profile.Length);
            result.EnergyProfile = profile;

            double totalThickness = 0.0;
            for (int i = 0; i < layers.Length; i++)
            {
                double thickness = layers[i].Thickness;
                if (double.IsPositiveInfinity(thickness)) { totalThickness = double.PositiveInfinity; break; }
                totalThickness += thickness;
            }

            // ---- State -------------------------------------------------------
            double depth = 0.0;
            double velocity = impactVelocity;
            double diameter = projectile.Calibre;
            double maxDiameter = diameter;
            double energyDeposited = 0.0;
            bool fragmented = false;
            bool payloadInitiated = false;
            bool cavityPlugged = false;

            double expansionLength = ExpansionLengthCalibres * projectile.Calibre;
            double fragmentSpreadRate = 2.0 * Math.Tan(FragmentSpreadAngle);
            double maxDiameterAllowed = projectile.Calibre * projectile.MaxExpansionRatio;

            double meplatRatio = projectile.Calibre > 0.0 ? projectile.MeplatDiameter / projectile.Calibre : 0.0;

            for (int step = 0; step < MaxSteps; step++)
            {
                if (velocity <= StopVelocity) break;
                if (!double.IsPositiveInfinity(totalThickness) && depth >= totalThickness) break;

                var medium = MediumAt(layers, depth, out int layerIndex);
                result.DeepestLayerReached = layerIndex;

                // ---- Cavity plugging ------------------------------------------
                // Driving through fabric packs the nose cavity with fibre. A packed
                // cavity cannot start a petal, so the expansion ceiling collapses to
                // roughly what a closed nose manages. The projectile then behaves like
                // a full-metal-jacket for the rest of its path -- which is exactly the
                // real failure that heavy-clothing testing exists to catch.
                if (!cavityPlugged && medium.PlugsCavities &&
                    projectile.MaxExpansionRatio > projectile.PluggedExpansionRatio)
                {
                    cavityPlugged = true;
                    result.CavityPlugged = true;

                    maxDiameterAllowed = projectile.Calibre * projectile.PluggedExpansionRatio;

                    // Whatever expansion already happened is not undone.
                    if (maxDiameterAllowed < diameter) maxDiameterAllowed = diameter;
                }

                // ---- Stagnation pressure driving deformation ------------------
                double stagnation = 0.5 * medium.Density * velocity * velocity;

                // ---- Reactive payload ----------------------------------------
                if (!payloadInitiated &&
                    projectile.PayloadMass > 0.0 &&
                    projectile.PayloadEnergyDensity > 0.0 &&
                    stagnation >= projectile.PayloadInitiationThreshold)
                {
                    payloadInitiated = true;
                    double released = projectile.PayloadMass * projectile.PayloadEnergyDensity;
                    result.ReactiveEnergyReleased = released;
                    result.PayloadInitiationDepth = depth;
                    energyDeposited += released;
                    Deposit(profile, binWidth, depth, released);
                }

                // ---- Deformation ---------------------------------------------
                if (!fragmented)
                {
                    bool yielding = stagnation > projectile.EffectiveYieldStrength;

                    if (yielding && projectile.IsBrittle)
                    {
                        // A brittle nose does not mushroom on its way to failing --
                        // it fails. The moment the load passes yield, it comes apart.
                        fragmented = true;
                        result.Fragmented = true;
                        result.FragmentationDepth = depth;
                        result.FragmentCount = EstimateFragmentCount(projectile, velocity);
                    }
                    else if (yielding && diameter < maxDiameterAllowed)
                    {
                        // Ductile: the nose flows outward, relaxing towards the
                        // ceiling at a rate set by how far the driving pressure
                        // exceeds the material's yield.
                        double overload = stagnation / projectile.EffectiveYieldStrength - 1.0;
                        if (overload > 1.0) overload = 1.0;

                        double growth = (maxDiameterAllowed - diameter) / expansionLength * overload;
                        diameter += growth * depthStep;
                        if (diameter > maxDiameterAllowed) diameter = maxDiameterAllowed;
                    }
                }
                else
                {
                    // Fragment cloud: the pieces diverge, so the swept frontal area
                    // grows linearly with depth. Modelling the cloud in aggregate
                    // rather than tracking individual fragments is a simplification,
                    // but it reproduces the behaviour that matters -- a very shallow
                    // channel with almost all the energy dumped at the front.
                    diameter += fragmentSpreadRate * depthStep;
                }

                if (diameter > maxDiameter) maxDiameter = diameter;

                // ---- Resistance ----------------------------------------------
                double area = Math.PI * diameter * diameter * 0.25;
                double dragCoefficient = MediumDragCoefficient(diameter, projectile.Calibre, meplatRatio, fragmented);

                // A projectile softer than what it is hitting cannot cut its way in --
                // it upsets and splashes, and the target's effective resistance rises
                // sharply. This one factor is what separates a hard penetrator from a
                // soft one against armour, and it is why an armour-piercing core is
                // about HARDNESS rather than mass or velocity. Against soft targets
                // (gelatin yields at 0.1 MPa) it is always 1 and costs nothing.
                double hardnessFactor = 1.0;
                if (projectile.EffectiveYieldStrength > 0.0 &&
                    medium.YieldStrength > projectile.EffectiveYieldStrength)
                {
                    hardnessFactor = medium.YieldStrength / projectile.EffectiveYieldStrength;
                    if (hardnessFactor > MaxHardnessPenalty) hardnessFactor = MaxHardnessPenalty;
                }

                double force = area * (medium.StrengthTerm * hardnessFactor
                                       + 0.5 * dragCoefficient * medium.Density * velocity * velocity);

                // ---- Advance --------------------------------------------------
                // m*v*dv/dx = -F, so dv = -F*dx/(m*v). Marching in depth rather than
                // time gives the energy-versus-depth profile directly, which is the
                // quantity the player is actually judged on.
                double dv = -force * depthStep / (projectile.Mass * velocity);
                double newVelocity = velocity + dv;
                if (newVelocity < 0.0) newVelocity = 0.0;

                double energyLost = 0.5 * projectile.Mass * (velocity * velocity - newVelocity * newVelocity);
                if (energyLost > 0.0)
                {
                    energyDeposited += energyLost;
                    Deposit(profile, binWidth, depth, energyLost);

                    double rate = energyLost / depthStep;
                    if (rate > result.PeakEnergyDepositionRate)
                    {
                        result.PeakEnergyDepositionRate = rate;
                        result.PeakEnergyDepositionDepth = depth;
                    }

                    // Temporary cavity: the medium is pushed aside to whatever radius
                    // the local energy deposition can pay for against its strength.
                    // Area = (dE/dx) / R_t is dimensionally exact; only meaningful in
                    // media that actually form a cavity.
                    if (medium.IsFluidLike && medium.StrengthTerm > 0.0)
                    {
                        double cavityArea = rate / medium.StrengthTerm;
                        double cavityDiameter = 2.0 * Math.Sqrt(cavityArea / Math.PI);
                        if (cavityDiameter > result.MaxTemporaryCavityDiameter)
                        {
                            result.MaxTemporaryCavityDiameter = cavityDiameter;
                            result.MaxTemporaryCavityDepth = depth;
                        }
                    }
                }

                velocity = newVelocity;
                depth += depthStep;
            }

            result.PenetrationDepth = depth;
            result.MaxExpandedDiameter = maxDiameter;
            result.ExpansionRatio = projectile.Calibre > 0.0 ? maxDiameter / projectile.Calibre : 1.0;
            result.EnergyDeposited = energyDeposited;
            result.ProfileBinCount = binWidth > 0.0
                ? Math.Min(profile.Length, (int)(depth / binWidth) + 1)
                : 0;

            bool exited = !double.IsPositiveInfinity(totalThickness)
                          && depth >= totalThickness
                          && velocity > StopVelocity;

            result.Perforated = exited;
            result.ExitVelocity = exited ? velocity : 0.0;
            result.ExitEnergy = exited ? 0.5 * projectile.Mass * velocity * velocity : 0.0;

            return result;
        }

        /// <summary>Finds which layer a given depth falls in.</summary>
        private static TargetMedium MediumAt(TargetLayer[] layers, double depth, out int index)
        {
            double accumulated = 0.0;
            for (int i = 0; i < layers.Length; i++)
            {
                accumulated += layers[i].Thickness;
                if (depth < accumulated || double.IsPositiveInfinity(layers[i].Thickness))
                {
                    index = i;
                    return layers[i].Medium;
                }
            }

            index = layers.Length - 1;
            return layers[index].Medium;
        }

        /// <summary>
        /// Drag coefficient of the projectile inside the medium.
        ///
        /// A sharp nose parts the material efficiently; a mushroomed one shoves it
        /// aside like a plate. This is a second, independent penalty for expansion --
        /// on top of the frontal area increase -- and together they are why an
        /// expanded projectile stops so much faster than an intact one.
        /// </summary>
        private static double MediumDragCoefficient(double diameter, double calibre, double meplatRatio, bool fragmented)
        {
            // An irregular fragment cloud is about as bad a shape as exists.
            if (fragmented) return 1.2;

            // A flat tip behaves partly like a disc even before any deformation.
            double baseline = 0.25 + 0.60 * meplatRatio * meplatRatio;

            // An expanded nose is blunter, but the frontal AREA increase already
            // carries most of the penalty (it goes as the square of the expansion).
            // This slope is deliberately modest so the two effects together land near
            // the observed roughly 2x reduction in penetration for an expanding
            // projectile, rather than the 6x that an aggressive slope produces.
            double expansion = calibre > 0.0 ? diameter / calibre : 1.0;
            double cd = baseline + 0.30 * (expansion - 1.0);

            if (cd > 1.2) cd = 1.2;
            if (cd < 0.15) cd = 0.15;
            return cd;
        }

        /// <summary>
        /// Rough fragment count. Presented to the player as an approximate figure and
        /// used nowhere in the physics -- the aggregate cloud model does not care how
        /// many pieces there are. Scaled by how much kinetic energy is available
        /// relative to the energy needed to create new fracture surface.
        /// </summary>
        private static int EstimateFragmentCount(in TerminalProjectile projectile, double velocity)
        {
            double kinetic = 0.5 * projectile.Mass * velocity * velocity;

            // Order-of-magnitude fracture energy per fragment for a brittle metal
            // fragment of a few tens of milligrams.
            const double energyPerFragment = 15.0; // J

            int count = (int)Math.Round(kinetic / energyPerFragment);
            if (count < 2) count = 2;
            if (count > 500) count = 500;
            return count;
        }

        /// <summary>Adds energy into the bin covering a depth.</summary>
        private static void Deposit(double[] profile, double binWidth, double depth, double energy)
        {
            if (profile == null || binWidth <= 0.0) return;
            int bin = (int)(depth / binWidth);
            if (bin < 0 || bin >= profile.Length) return;
            profile[bin] += energy;
        }
    }
}
