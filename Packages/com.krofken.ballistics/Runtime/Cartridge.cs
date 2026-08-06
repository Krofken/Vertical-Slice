using System;
using System.Collections.Generic;

namespace Krofken.Ballistics
{
    /// <summary>
    /// A complete round of ammunition as the player designed it.
    ///
    /// This is the SAVE FORMAT and the unit of sharing: it is pure authored data with
    /// no computed results in it, so it is small, serialises trivially, and can be
    /// handed to another machine which will bake the identical results from it.
    /// Everything derived lives in <see cref="BakedCartridge"/>.
    /// </summary>
    [Serializable]
    public struct CartridgeDesign
    {
        /// <summary>Player-assigned name.</summary>
        public string Name;

        /// <summary>Which case this is built on.</summary>
        public string CaseId;

        /// <summary>Projectile shape.</summary>
        public ProjectileGeometry Projectile;

        /// <summary>What the projectile is made of.</summary>
        public ProjectileMaterials Materials;

        /// <summary>Propellant chemistry.</summary>
        public string PropellantId;

        /// <summary>Grain shape, which sets whether the burn is progressive,
        /// neutral or degressive.</summary>
        public GrainShape GrainShape;

        /// <summary>Propellant web thickness, m. The dominant burn-speed control.</summary>
        public double WebThickness;

        /// <summary>Surface deterrent coating, 0..1.</summary>
        public double DeterrentCoating;

        /// <summary>Propellant charge mass, kg.</summary>
        public double ChargeMass;

        /// <summary>How deep the projectile base sits inside the case mouth, m.
        /// Seating deeper shrinks the space the powder burns in and raises pressure
        /// sharply -- a small change here is not a small change in the result.</summary>
        public double SeatingDepth;
    }

    /// <summary>Severity of something the validator found.</summary>
    public enum DesignIssueSeverity
    {
        /// <summary>Worth knowing, does not prevent firing.</summary>
        Info = 0,
        /// <summary>The round will fire but behave badly.</summary>
        Warning = 1,
        /// <summary>The round cannot be built or is unsafe to fire.</summary>
        Error = 2
    }

    /// <summary>Something the validator has to say about a design.</summary>
    public readonly struct DesignIssue
    {
        public readonly DesignIssueSeverity Severity;
        public readonly string Message;

        public DesignIssue(DesignIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public override string ToString() => $"[{Severity}] {Message}";
    }

    /// <summary>
    /// A design plus everything expensive that can be computed from it once.
    ///
    /// THIS IS THE PERFORMANCE CONTRACT of the whole library. Baking runs the
    /// interior ballistics ODE, integrates the projectile's mass properties and
    /// aerodynamic shape, and samples a full drag curve -- hundreds of thousands of
    /// floating point operations. It happens ONCE, when the player commits a design.
    ///
    /// Everything after that -- firing the round, flying it, hitting something -- reads
    /// only from the baked results and never touches a solver again. Firing a round
    /// costs a table lookup per integration step and nothing else.
    /// </summary>
    public sealed class BakedCartridge
    {
        /// <summary>The design this was baked from.</summary>
        public CartridgeDesign Design;

        /// <summary>The case, resolved from <see cref="CartridgeDesign.CaseId"/>.</summary>
        public CartridgeCase Case;

        /// <summary>Integrated projectile mass, centre of gravity and inertia.</summary>
        public MassProperties Mass;

        /// <summary>Integrated aerodynamic shape summary.</summary>
        public AerodynamicShape Shape;

        /// <summary>Muzzle velocity, peak pressure and the rest of the interior solve.</summary>
        public InteriorBallisticsResult Interior;

        /// <summary>Baked drag curve.</summary>
        public DragTable Drag;

        /// <summary>Flight constants for the trajectory integrator.</summary>
        public ProjectileAerodynamics Aerodynamics;

        /// <summary>Impact properties for the terminal solver.</summary>
        public TerminalProjectile Terminal;

        /// <summary>Gyroscopic stability factor in the barrel it was baked for.</summary>
        public double StabilityFactor;

        /// <summary>Everything the validator found.</summary>
        public readonly List<DesignIssue> Issues = new List<DesignIssue>();

        /// <summary>True if nothing blocking was found and the round can be fired.</summary>
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == DesignIssueSeverity.Error)
                        return false;
                return true;
            }
        }

        /// <summary>Muzzle velocity, m/s. Convenience passthrough.</summary>
        public double MuzzleVelocity => Interior.MuzzleVelocity;

        /// <summary>Muzzle energy, J. Convenience passthrough.</summary>
        public double MuzzleEnergy => Interior.MuzzleEnergy;

        /// <summary>
        /// Builds the state a projectile starts flight with.
        /// </summary>
        /// <param name="elevation">Launch elevation above horizontal, rad.</param>
        /// <param name="origin">Muzzle position in world axes, m.</param>
        /// <param name="twistRate">Barrel twist, m per turn, for the initial spin.</param>
        public ProjectileState CreateMuzzleState(double elevation, Vec3 origin = default, double twistRate = 0.254)
        {
            double v = Interior.MuzzleVelocity;
            return new ProjectileState
            {
                Position = origin,
                Velocity = new Vec3(v * Math.Cos(elevation), 0.0, v * Math.Sin(elevation)),
                Time = 0.0,
                SpinRate = twistRate > 0.0 ? 2.0 * Math.PI * v / twistRate : 0.0
            };
        }
    }

    /// <summary>
    /// Turns a <see cref="CartridgeDesign"/> into a <see cref="BakedCartridge"/>.
    ///
    /// Order matters and is forced by the dependencies:
    ///
    ///   geometry + materials  ->  mass properties
    ///   mass + case + charge  ->  interior ballistics  ->  muzzle velocity
    ///   geometry              ->  aerodynamic shape    ->  drag table
    ///   mass + geometry + velocity + twist             ->  stability
    ///   geometry + materials + mass                    ->  terminal properties
    ///
    /// Validation runs alongside and collects everything wrong with the design rather
    /// than failing on the first problem, so the workshop UI can show the player the
    /// full list at once.
    /// </summary>
    public static class CartridgeBaker
    {
        /// <summary>
        /// Highest Mach number the drag table covers. Generous headroom above any
        /// achievable muzzle velocity -- clamping the table would silently flatten
        /// the drag curve on an unusually hot load.
        /// </summary>
        public const double DragTableMaxMach = 5.0;

        /// <summary>Drag table resolution. 0.025 Mach per sample across the range.</summary>
        public const int DragTableSamples = 200;

        public static BakedCartridge Bake(
            in CartridgeDesign design,
            in Barrel barrel,
            Atmosphere? bakeAtmosphere = null)
        {
            var baked = new BakedCartridge { Design = design };

            // Drag is baked against a fixed reference atmosphere, not the current
            // weather. Cd depends on Mach (handled explicitly at flight time) and on
            // Reynolds number (only logarithmically, so a standard-day bake is within
            // a fraction of a percent). Flight-time density is applied where it
            // actually belongs, in the drag force itself.
            var atmosphere = bakeAtmosphere ?? Atmosphere.Standard;

            // ---- Resolve content ------------------------------------------
            if (!CartridgeCaseLibrary.TryGet(design.CaseId, out var cartridgeCase))
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    $"Unknown case '{design.CaseId}'."));
                return baked;
            }
            baked.Case = cartridgeCase;

            if (!PropellantLibrary.TryGet(design.PropellantId, out var propellant))
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    $"Unknown propellant '{design.PropellantId}'."));
                return baked;
            }

            var geometry = design.Projectile;
            if (!geometry.Validate(out string geometryError))
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error, geometryError));
                return baked;
            }

            if (!MaterialLibrary.TryGet(design.Materials.CoreMaterialId, out _))
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    $"Unknown core material '{design.Materials.CoreMaterialId}'."));
                return baked;
            }

            // ---- Fit checks -------------------------------------------------
            if (Math.Abs(geometry.Calibre - cartridgeCase.Calibre) > 0.0002)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    $"Projectile is {Units.MetresToMillimetres(geometry.Calibre):F2} mm but the case is " +
                    $"{Units.MetresToMillimetres(cartridgeCase.Calibre):F2} mm. It will not chamber."));
            }

            // ---- Mass properties --------------------------------------------
            baked.Mass = MassPropertiesSolver.Compute(geometry, design.Materials);
            if (baked.Mass.Mass <= 0.0)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    "Projectile has no mass -- check that the core material is set."));
                return baked;
            }

            // ---- Case volume accounting --------------------------------------
            double seatingDepth = design.SeatingDepth;
            if (seatingDepth < 0.0) seatingDepth = 0.0;
            if (seatingDepth > geometry.OverallLength) seatingDepth = geometry.OverallLength;

            cartridgeCase.SeatedProjectileVolume = SeatedVolume(geometry, seatingDepth);
            baked.Case = cartridgeCase;

            var grain = design.GrainShape == GrainShape.Custom
                ? GrainGeometry.Custom(design.WebThickness, 1.0, 0.0, 0.0, design.DeterrentCoating)
                : GrainGeometry.Create(design.GrainShape, design.WebThickness, design.DeterrentCoating);

            var charge = new PropellantCharge
            {
                Propellant = propellant,
                Grain = grain,
                Mass = design.ChargeMass
            };

            double chamberVolume = cartridgeCase.ChamberVolume;

            // "Does it fit" is a question about the poured heap, not the solid
            // material -- powder has air between the grains. Using solid volume here
            // would happily accept a charge that physically overflows the case.
            double loadDensity = chamberVolume > 0.0 ? charge.BulkVolume / chamberVolume : double.PositiveInfinity;

            if (loadDensity > 1.0)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    $"Charge does not fit: {design.ChargeMass * 1e3:F2} g of this grain occupies " +
                    $"{charge.BulkVolume * 1e6:F2} cm3 poured, but only {chamberVolume * 1e6:F2} cm3 is available."));
            }
            else if (loadDensity > 0.95)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Warning,
                    $"Compressed load ({loadDensity * 100:F0}% of case volume). Charge weight will be inconsistent."));
            }
            else if (loadDensity < 0.25)
            {
                // Real and genuinely dangerous: a small charge lying along the bottom
                // of a large case can burn abnormally and spike pressure.
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Warning,
                    $"Very low load density ({loadDensity * 100:F0}%). Ignition will be erratic."));
            }

            // ---- Interior ballistics -----------------------------------------
            var interiorInput = InteriorBallisticsSolver.BuildInput(
                charge, cartridgeCase, barrel, geometry, design.Materials, baked.Mass.Mass);

            baked.Interior = InteriorBallisticsSolver.Solve(interiorInput);

            switch (baked.Interior.Status)
            {
                case InteriorBallisticsStatus.InvalidInput:
                    baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error, baked.Interior.Message));
                    break;
                case InteriorBallisticsStatus.Squib:
                    baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error, baked.Interior.Message));
                    break;
                case InteriorBallisticsStatus.Overpressure:
                    baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error, baked.Interior.Message));
                    break;
            }

            if (baked.Interior.BurntFractionAtMuzzle < 0.90 &&
                baked.Interior.Status == InteriorBallisticsStatus.Success)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Warning,
                    $"Only {baked.Interior.BurntFractionAtMuzzle * 100:F0}% of the charge burns before the " +
                    "projectile exits. The rest is thrown out of the muzzle as flash. Use a finer web."));
            }

            if (baked.Interior.PeakPressureTravel < 0.01 &&
                baked.Interior.Status == InteriorBallisticsStatus.Success)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Warning,
                    "Pressure peaks in the first centimetre of travel. This powder is too fast for this case."));
            }

            // ---- Exterior ballistics ------------------------------------------
            baked.Shape = AerodynamicShape.FromGeometry(geometry);

            double referenceVelocity = baked.Interior.MuzzleVelocity > 1.0
                ? baked.Interior.MuzzleVelocity * 0.8   // representative mid-flight speed
                : 340.0;

            var dragModel = new GeometricDragModel(baked.Shape, atmosphere, referenceVelocity);
            baked.Drag = DragTable.Bake(dragModel, 0.0, DragTableMaxMach, DragTableSamples);

            // ---- Stability -----------------------------------------------------
            baked.StabilityFactor = MassPropertiesSolver.GyroscopicStability(
                geometry, baked.Mass.Mass, barrel.TwistRate, baked.Interior.MuzzleVelocity);

            if (baked.StabilityFactor > 0.0 && baked.StabilityFactor < 1.0)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Error,
                    $"Gyroscopic stability is {baked.StabilityFactor:F2}. The projectile will tumble in flight. " +
                    "Shorten it, or fire it from a faster twist."));
            }
            else if (baked.StabilityFactor >= 1.0 && baked.StabilityFactor < 1.4)
            {
                baked.Issues.Add(new DesignIssue(DesignIssueSeverity.Warning,
                    $"Marginal stability ({baked.StabilityFactor:F2}). It will fly, but accuracy will suffer " +
                    "and it may destabilise in cold air."));
            }

            baked.Aerodynamics = ProjectileAerodynamics.Bake(
                geometry, baked.Mass.Mass, baked.Drag, baked.StabilityFactor);

            // ---- Terminal --------------------------------------------------------
            baked.Terminal = TerminalProjectile.FromDesign(geometry, design.Materials, baked.Mass);

            return baked;
        }

        /// <summary>
        /// Volume of the projectile sitting inside the case mouth, m^3.
        /// Integrated from the base upward over the seated length -- a boattailed
        /// bullet displaces noticeably less than a flat-based one seated to the same
        /// depth, and at these case volumes that difference is measurable in pressure.
        /// </summary>
        public static double SeatedVolume(in ProjectileGeometry geometry, double seatingDepth, int slices = 128)
        {
            if (seatingDepth <= 0.0) return 0.0;
            if (slices < 8) slices = 8;

            double total = geometry.OverallLength;
            double start = total - seatingDepth;
            if (start < 0.0) start = 0.0;

            double dx = (total - start) / slices;
            double volume = 0.0;

            for (int i = 0; i < slices; i++)
            {
                double x = start + (i + 0.5) * dx;
                double r = geometry.RadiusAt(x);
                volume += Math.PI * r * r * dx;
            }

            return volume;
        }
    }

    /// <summary>Available cartridge cases. The vertical slice ships one.</summary>
    public static class CartridgeCaseLibrary
    {
        public const string NineMillimetre = "9x19";

        private static readonly Dictionary<string, CartridgeCase> Table = BuildDefaults();

        public static bool TryGet(string id, out CartridgeCase c) => Table.TryGetValue(id, out c);

        public static CartridgeCase Get(string id)
        {
            if (!Table.TryGetValue(id, out var c))
                throw new KeyNotFoundException($"Unknown cartridge case '{id}'.");
            return c;
        }

        public static void Register(CartridgeCase c) => Table[c.Id] = c;

        public static IEnumerable<CartridgeCase> All => Table.Values;

        private static Dictionary<string, CartridgeCase> BuildDefaults()
        {
            var t = new Dictionary<string, CartridgeCase>();

            // 9x19mm. Case capacity is the published water capacity; the pressure
            // ceiling is the CIP maximum.
            t[NineMillimetre] = new CartridgeCase
            {
                Id = NineMillimetre,
                DisplayName = "9x19mm",
                Capacity = 0.86e-6,           // m^3  (0.86 cm3)
                SeatedProjectileVolume = 0.0, // filled in per design at bake time
                Calibre = 0.00902,
                MaximumPressure = 235e6       // Pa
            };

            return t;
        }
    }

    /// <summary>Available barrels. The vertical slice ships one.</summary>
    public static class BarrelLibrary
    {
        /// <summary>
        /// A conventional 4-inch service pistol barrel in 9mm.
        /// Travel is measured from the projectile base at rest to the muzzle, which is
        /// shorter than the catalogue barrel length because the chamber does not count.
        /// </summary>
        public static Barrel ServicePistol9mm => new Barrel
        {
            BoreDiameter = 0.00902,
            Travel = 0.085,
            TwistRate = 0.254,               // 1 turn in 10 inches
            ShotStartPressure = 0.0,         // derived from the projectile's jacket
            BoreFrictionPressure = 0.0,      // derived from shot start
            EngravingDistance = 0.0          // derived from the bearing surface
        };
    }
}
