using System;

namespace Krofken.Ballistics
{
    /// <summary>The barrel the round is fired through.</summary>
    [Serializable]
    public struct Barrel
    {
        /// <summary>Bore diameter across the lands, m. Sets the area the gas pushes on.</summary>
        public double BoreDiameter;

        /// <summary>Distance the projectile base travels from rest to the muzzle, m.
        /// Note this is NOT the catalogue "barrel length", which is measured from the
        /// breech face and includes the chamber.</summary>
        public double Travel;

        /// <summary>Rifling twist: axial distance per full revolution, m.
        /// A 1-in-10-inch twist is 0.254 m.</summary>
        public double TwistRate;

        /// <summary>
        /// Pressure the gas must reach before the projectile starts moving, Pa.
        /// Physically this is the force needed to swage the bearing surface into the
        /// rifling. Leave at zero to have the solver estimate it from the jacket
        /// material and bearing surface.
        /// </summary>
        public double ShotStartPressure;

        /// <summary>Equivalent retarding pressure from bore friction once the
        /// projectile is moving, Pa. Typically an order of magnitude below shot
        /// start.</summary>
        public double BoreFrictionPressure;

        /// <summary>Axial distance over which engraving completes, m. Resistance
        /// falls from shot-start to bore-friction across this distance.</summary>
        public double EngravingDistance;

        /// <summary>Cross-sectional bore area, m^2.</summary>
        public double BoreArea => Math.PI * BoreDiameter * BoreDiameter * 0.25;

        /// <summary>Spin rate imparted at a given muzzle velocity, rad/s.
        /// The projectile turns once per <see cref="TwistRate"/> metres of travel.</summary>
        public double SpinRateAt(double velocity)
            => TwistRate > 0.0 ? 2.0 * Math.PI * velocity / TwistRate : 0.0;
    }

    /// <summary>The cartridge case: how much room the powder has, and how much
    /// pressure the brass can contain before it lets go.</summary>
    [Serializable]
    public struct CartridgeCase
    {
        public string Id;
        public string DisplayName;

        /// <summary>Total internal volume of the case, m^3 (the "water capacity").</summary>
        public double Capacity;

        /// <summary>Volume lost to the seated projectile intruding into the case, m^3.
        /// Seating a long bullet deeper raises pressure sharply by shrinking the
        /// space the powder burns in -- a real and frequently fatal handloading trap.</summary>
        public double SeatedProjectileVolume;

        /// <summary>Case mouth / bore diameter this case feeds, m.</summary>
        public double Calibre;

        /// <summary>Maximum pressure the case can contain before rupture, Pa.</summary>
        public double MaximumPressure;

        /// <summary>Gas volume actually available at rest, m^3.</summary>
        public double ChamberVolume
        {
            get
            {
                double v = Capacity - SeatedProjectileVolume;
                return v < 0.0 ? 0.0 : v;
            }
        }
    }

    /// <summary>Why the interior solve ended.</summary>
    public enum InteriorBallisticsStatus
    {
        /// <summary>Projectile reached the muzzle normally.</summary>
        Success = 0,
        /// <summary>Geometry or charge is physically impossible (e.g. more powder
        /// than fits in the case).</summary>
        InvalidInput = 1,
        /// <summary>Projectile never left the barrel within the step budget -- a squib.
        /// Too little powder, or powder far too slow for the barrel.</summary>
        Squib = 2,
        /// <summary>Peak pressure exceeded what the case can contain. The round would
        /// rupture; the reported muzzle velocity is what the projectile would have
        /// done had the case held.</summary>
        Overpressure = 3
    }

    /// <summary>Output of an interior ballistics solve.</summary>
    [Serializable]
    public struct InteriorBallisticsResult
    {
        public InteriorBallisticsStatus Status;

        /// <summary>Velocity at the muzzle, m/s.</summary>
        public double MuzzleVelocity;

        /// <summary>Highest chamber pressure reached, Pa.</summary>
        public double PeakPressure;

        /// <summary>Projectile travel at peak pressure, m. Peaking very early (in the
        /// first centimetre or two) is the signature of a powder that is too fast for
        /// the case.</summary>
        public double PeakPressureTravel;

        /// <summary>Time from ignition to peak pressure, s.</summary>
        public double PeakPressureTime;

        /// <summary>Residual pressure as the projectile clears the muzzle, Pa.
        /// High muzzle pressure means wasted powder, loud report and large flash.</summary>
        public double MuzzlePressure;

        /// <summary>Fraction of the charge consumed by the time the projectile exits,
        /// 0..1. Below 1 means unburnt powder is being thrown out of the muzzle.</summary>
        public double BurntFractionAtMuzzle;

        /// <summary>Time from ignition to muzzle exit, s.</summary>
        public double TimeToMuzzle;

        /// <summary>Projectile kinetic energy at the muzzle, J.</summary>
        public double MuzzleEnergy;

        /// <summary>Spin rate at the muzzle, rad/s.</summary>
        public double SpinRate;

        /// <summary>
        /// Fraction of the propellant's chemical energy that ended up as projectile
        /// kinetic energy, 0..1. Real small arms manage roughly 0.20-0.35; the rest
        /// goes into hot gas, barrel heating and the muzzle blast. A design that
        /// reports much above 0.4 has a bug in it, not a breakthrough.
        /// </summary>
        public double ThermodynamicEfficiency;

        /// <summary>Explanation when <see cref="Status"/> is not Success.</summary>
        public string Message;
    }

    /// <summary>Optional recorded curves for plotting in the workshop UI.</summary>
    public sealed class InteriorBallisticsTrace
    {
        public double[] Time;      // s
        public double[] Travel;    // m
        public double[] Pressure;  // Pa
        public double[] Velocity;  // m/s
        public double[] BurntFraction; // 0..1
        public int Count;

        public InteriorBallisticsTrace(int capacity = 256)
        {
            Time = new double[capacity];
            Travel = new double[capacity];
            Pressure = new double[capacity];
            Velocity = new double[capacity];
            BurntFraction = new double[capacity];
            Count = 0;
        }

        public int Capacity => Time.Length;

        internal void Reset() => Count = 0;

        internal void Add(double t, double x, double p, double v, double psi)
        {
            if (Count >= Capacity) return;
            Time[Count] = t;
            Travel[Count] = x;
            Pressure[Count] = p;
            Velocity[Count] = v;
            BurntFraction[Count] = psi;
            Count++;
        }
    }

    /// <summary>
    /// Lumped-parameter interior ballistics.
    ///
    /// MODEL
    /// -----
    /// Three coupled ordinary differential equations in the state (z, x, v):
    ///
    ///   z  fraction of the propellant web burnt through, 0..1
    ///   x  projectile travel from rest, m
    ///   v  projectile velocity, m/s
    ///
    /// 1. BURN RATE -- Vieille's law. The flame front eats into the solid at a rate
    ///    that depends on pressure:
    ///
    ///        dz/dt = u1 * P^n / e1
    ///
    ///    with e1 the web thickness. n sits near 0.85 for nitrocellulose, so burn
    ///    rate is very nearly proportional to pressure. That positive feedback --
    ///    more pressure burns powder faster, which makes more pressure -- is exactly
    ///    why a modest overcharge does not produce a modestly worse outcome.
    ///
    /// 2. GAS STATE -- Nobel-Abel equation of state with an energy balance. The
    ///    propellant's chemical energy, minus the work already done accelerating the
    ///    projectile and the gas column, fills the free volume:
    ///
    ///        P = [ f*w*psi + theta*E_primer - theta*phi*m*v^2/2 ] / V_free
    ///        V_free = V0 - (w/rho_p)*(1-psi) - alpha*w*psi + A*x
    ///
    ///    The three subtractions in V_free are, in order: the volume still occupied
    ///    by unburnt solid grains, the covolume of the gas already produced (gas
    ///    molecules are not point particles at 300 MPa), and the volume swept as the
    ///    projectile moves. Dropping the covolume term overestimates peak pressure by
    ///    tens of percent -- it is not a refinement, the model does not work without it.
    ///
    /// 3. MOTION -- Newton, against engraving and bore friction:
    ///
    ///        phi*m*dv/dt = A*(P - P_resist(x)),   dx/dt = v
    ///
    ///    phi is the secondary work factor. Some of the energy goes into accelerating
    ///    the propellant gas and unburnt grains along with the projectile; the
    ///    Lagrange approximation charges this as an extra w/3 of effective mass. The
    ///    remainder covers bore friction and spinning the projectile up.
    ///
    /// Integrated with classical RK4. Pressure is algebraic in the state, not a state
    /// variable, so it is recomputed inside every derivative evaluation.
    ///
    /// COST: this runs at DESIGN time, once, when the player commits a load -- never
    /// per frame. A solve is a few tens of thousands of steps and completes in well
    /// under a millisecond, and the result is baked into the cartridge.
    /// </summary>
    public static class InteriorBallisticsSolver
    {
        /// <summary>Integration step, s. The whole event lasts around a millisecond,
        /// so 100 ns steps give roughly 10,000 steps through the barrel.</summary>
        public const double DefaultTimeStep = 1e-7;

        /// <summary>Hard cap on steps, equal to 10 ms of simulated time at the default
        /// step. Anything still in the barrel by then is a squib.</summary>
        public const int DefaultMaxSteps = 100_000;

        /// <summary>
        /// Fraction of the jacket's yield strength taken as the shot-start pressure
        /// when the barrel does not specify one. Produces roughly 25 MPa for a
        /// gilding-metal jacket, which is the right order for small arms.
        /// </summary>
        public const double EngravingPressureCoefficient = 0.25;

        /// <summary>Bore friction as a fraction of shot-start pressure, used when the
        /// barrel does not specify a value.</summary>
        public const double DefaultFrictionFraction = 0.12;

        /// <summary>Baseline secondary work factor covering friction and spin-up,
        /// before the Lagrange gas-mass term is added.</summary>
        public const double DefaultFrictionWorkFactor = 1.05;

        /// <summary>
        /// Default share of chemical energy lost as heat to the gun. 0.20 puts
        /// thermodynamic efficiency in the 25-35% band that real small arms occupy.
        /// </summary>
        public const double DefaultHeatLossFraction = 0.20;

        /// <summary>Immutable inputs to a solve.</summary>
        public struct Input
        {
            public PropellantCharge Charge;

            /// <summary>Gas volume behind the projectile at rest, m^3.</summary>
            public double ChamberVolume;

            /// <summary>Bore cross-sectional area, m^2.</summary>
            public double BoreArea;

            /// <summary>Projectile travel to the muzzle, m.</summary>
            public double BarrelTravel;

            /// <summary>Projectile mass, kg.</summary>
            public double ProjectileMass;

            /// <summary>Pressure needed to start the projectile moving, Pa.</summary>
            public double ShotStartPressure;

            /// <summary>Retarding pressure from bore friction once moving, Pa.</summary>
            public double BoreFrictionPressure;

            /// <summary>Distance over which engraving resistance decays, m.</summary>
            public double EngravingDistance;

            /// <summary>Energy the primer injects at ignition, J. Typical small-arms
            /// primers deliver 20-60 J; without it the burn law never starts, because
            /// zero pressure means zero burn rate.</summary>
            public double PrimerEnergy;

            /// <summary>Baseline secondary work factor (friction, spin-up).</summary>
            public double FrictionWorkFactor;

            /// <summary>
            /// Fraction of the propellant's chemical energy lost as heat into the
            /// barrel, chamber and case walls, 0..1.
            ///
            /// This is a REAL and LARGE term, not a fudge. Combustion gas leaves the
            /// muzzle at well over 1000 K and the barrel gets hot for a reason: a
            /// substantial share of the chemistry never becomes work at all. A
            /// lumped-parameter model has no spatial temperature field to compute it
            /// from, so it is charged as a flat fraction -- which is how practical
            /// interior ballistics codes handle it too.
            ///
            /// Omitting it produces thermodynamic efficiencies near 50%, roughly
            /// double what real small arms achieve.
            /// </summary>
            public double HeatLossFraction;

            /// <summary>Pressure at which the case ruptures, Pa. Zero disables the check.</summary>
            public double CaseMaximumPressure;
        }

        /// <summary>
        /// Assembles solver input from the player-facing objects, filling in any
        /// values the barrel left unset by deriving them from the projectile's
        /// materials.
        /// </summary>
        public static Input BuildInput(
            in PropellantCharge charge,
            in CartridgeCase cartridgeCase,
            in Barrel barrel,
            in ProjectileGeometry projectile,
            in ProjectileMaterials projectileMaterials,
            double projectileMass)
        {
            // Shot-start pressure comes from how hard the bearing surface is to swage
            // into the rifling, which is a property of whichever material forms the
            // outer surface -- the jacket if there is one, otherwise the core.
            double shotStart = barrel.ShotStartPressure;
            if (shotStart <= 0.0)
            {
                string surfaceId = projectile.JacketThickness > 0.0
                    ? projectileMaterials.JacketMaterialId
                    : projectileMaterials.CoreMaterialId;

                double yield = MaterialLibrary.TryGet(surfaceId, out var surface)
                    ? surface.YieldStrength
                    : 100e6;

                shotStart = EngravingPressureCoefficient * yield;
            }

            double friction = barrel.BoreFrictionPressure;
            if (friction <= 0.0) friction = shotStart * DefaultFrictionFraction;

            double engraving = barrel.EngravingDistance;
            if (engraving <= 0.0)
            {
                // Engraving is complete once the whole bearing surface is in the
                // rifling, so the bearing length is the natural distance.
                engraving = projectile.BearingSurfaceLength > 0.0
                    ? projectile.BearingSurfaceLength
                    : 0.002;
            }

            return new Input
            {
                Charge = charge,
                ChamberVolume = cartridgeCase.ChamberVolume,
                BoreArea = barrel.BoreArea,
                BarrelTravel = barrel.Travel,
                ProjectileMass = projectileMass,
                ShotStartPressure = shotStart,
                BoreFrictionPressure = friction,
                EngravingDistance = engraving,
                PrimerEnergy = 35.0,
                FrictionWorkFactor = DefaultFrictionWorkFactor,
                HeatLossFraction = DefaultHeatLossFraction,
                CaseMaximumPressure = cartridgeCase.MaximumPressure
            };
        }

        /// <remarks>
        /// <paramref name="input"/> is taken by value rather than by <c>in</c>: the
        /// derivative evaluation is written as local functions closing over it, and
        /// C# forbids capturing <c>in</c> parameters. The copy is a handful of
        /// doubles, made once per design bake, and buys a solver body that reads like
        /// the equations it implements.
        /// </remarks>
        public static InteriorBallisticsResult Solve(
            Input input,
            InteriorBallisticsTrace trace = null,
            double timeStep = DefaultTimeStep,
            int maxSteps = DefaultMaxSteps)
        {
            var result = new InteriorBallisticsResult();

            // ---- Validation -------------------------------------------------
            // Every one of these produces NaN or a runaway integration if allowed
            // through, so they are checked rather than clamped.
            if (input.ProjectileMass <= 0.0)
                return Fail(ref result, "Projectile mass must be positive.");
            if (input.BoreArea <= 0.0)
                return Fail(ref result, "Bore area must be positive.");
            if (input.BarrelTravel <= 0.0)
                return Fail(ref result, "Barrel travel must be positive.");
            if (input.Charge.Mass <= 0.0)
                return Fail(ref result, "Propellant charge mass must be positive.");
            if (input.Charge.Grain.WebThickness <= 0.0)
                return Fail(ref result, "Propellant web thickness must be positive.");

            double chargeMass = input.Charge.Mass;
            var propellant = input.Charge.Propellant;
            var grain = input.Charge.Grain;

            double solidVolume = chargeMass / propellant.SolidDensity;
            double airspace = input.ChamberVolume - solidVolume;
            if (airspace <= 0.0)
                return Fail(ref result,
                    $"Charge does not fit: {chargeMass * 1e3:F2} g of propellant occupies " +
                    $"{solidVolume * 1e6:F2} cm^3 but the case holds {input.ChamberVolume * 1e6:F2} cm^3.");

            // Secondary work factor: baseline friction/spin allowance plus the
            // Lagrange correction for accelerating the propellant gas column.
            double frictionFactor = input.FrictionWorkFactor > 0.0
                ? input.FrictionWorkFactor
                : DefaultFrictionWorkFactor;
            double phi = frictionFactor + chargeMass / (3.0 * input.ProjectileMass);
            double effectiveMass = phi * input.ProjectileMass;

            double theta = propellant.Theta;

            // Total chemical energy the charge contains, before any losses. Kept
            // separately so the reported efficiency is measured against what the
            // propellant actually held, not against what survived heat loss.
            double totalChemicalEnergy = propellant.Impetus * chargeMass / theta;

            double heatLoss = input.HeatLossFraction;
            if (heatLoss < 0.0) heatLoss = 0.0;
            if (heatLoss > 0.9) heatLoss = 0.9;

            double impetusEnergy = propellant.Impetus * chargeMass * (1.0 - heatLoss);   // f * w, J
            double primerTerm = theta * input.PrimerEnergy;

            // ---- State ------------------------------------------------------
            double z = 0.0;   // burnt web fraction
            double x = 0.0;   // travel, m
            double v = 0.0;   // velocity, m/s
            double t = 0.0;   // time, s

            double peakPressure = 0.0;
            double peakTravel = 0.0;
            double peakTime = 0.0;

            trace?.Reset();
            int traceInterval = trace != null ? Math.Max(1, maxSteps / Math.Max(1, trace.Capacity - 1)) : int.MaxValue;
            int traceCountdown = 0;

            bool exited = false;
            double dt = timeStep;

            for (int step = 0; step < maxSteps; step++)
            {
                double pressure = Pressure(z, x, v);

                if (pressure > peakPressure)
                {
                    peakPressure = pressure;
                    peakTravel = x;
                    peakTime = t;
                }

                if (trace != null && traceCountdown-- <= 0)
                {
                    trace.Add(t, x, pressure, v, grain.BurntFraction(z));
                    traceCountdown = traceInterval;
                }

                // ---- RK4 over (z, x, v) -------------------------------------
                Derivatives(z, x, v, out double dz1, out double dx1, out double dv1);
                Derivatives(z + 0.5 * dt * dz1, x + 0.5 * dt * dx1, v + 0.5 * dt * dv1,
                    out double dz2, out double dx2, out double dv2);
                Derivatives(z + 0.5 * dt * dz2, x + 0.5 * dt * dx2, v + 0.5 * dt * dv2,
                    out double dz3, out double dx3, out double dv3);
                Derivatives(z + dt * dz3, x + dt * dx3, v + dt * dv3,
                    out double dz4, out double dx4, out double dv4);

                double zNext = z + dt / 6.0 * (dz1 + 2.0 * dz2 + 2.0 * dz3 + dz4);
                double xNext = x + dt / 6.0 * (dx1 + 2.0 * dx2 + 2.0 * dx3 + dx4);
                double vNext = v + dt / 6.0 * (dv1 + 2.0 * dv2 + 2.0 * dv3 + dv4);

                if (zNext > 1.0) zNext = 1.0;
                if (zNext < 0.0) zNext = 0.0;
                if (vNext < 0.0) vNext = 0.0;   // the projectile never reverses
                if (xNext < 0.0) xNext = 0.0;

                if (double.IsNaN(vNext) || double.IsInfinity(vNext) ||
                    double.IsNaN(xNext) || double.IsInfinity(xNext))
                    return Fail(ref result, "Interior solve diverged; the load is far outside a physical regime.");

                // ---- Muzzle exit --------------------------------------------
                if (xNext >= input.BarrelTravel)
                {
                    // Linear interpolation onto the muzzle plane. Without this the
                    // reported velocity depends on where the last step happened to
                    // land, which shows up as jitter between otherwise identical loads.
                    double span = xNext - x;
                    double fraction = span > 1e-15 ? (input.BarrelTravel - x) / span : 1.0;
                    if (fraction < 0.0) fraction = 0.0;
                    if (fraction > 1.0) fraction = 1.0;

                    v = v + (vNext - v) * fraction;
                    z = z + (zNext - z) * fraction;
                    t += dt * fraction;
                    x = input.BarrelTravel;
                    exited = true;
                    break;
                }

                z = zNext;
                x = xNext;
                v = vNext;
                t += dt;
            }

            double burntFraction = grain.BurntFraction(z);
            double muzzlePressure = Pressure(z, x, v);
            double muzzleEnergy = 0.5 * input.ProjectileMass * v * v;

            result.MuzzleVelocity = v;
            result.PeakPressure = peakPressure;
            result.PeakPressureTravel = peakTravel;
            result.PeakPressureTime = peakTime;
            result.MuzzlePressure = muzzlePressure;
            result.BurntFractionAtMuzzle = burntFraction;
            result.TimeToMuzzle = t;
            result.MuzzleEnergy = muzzleEnergy;
            result.ThermodynamicEfficiency = totalChemicalEnergy > 0.0
                ? muzzleEnergy / totalChemicalEnergy
                : 0.0;

            trace?.Add(t, x, muzzlePressure, v, burntFraction);

            if (!exited)
            {
                result.Status = InteriorBallisticsStatus.Squib;
                result.Message =
                    $"Projectile stopped {(input.BarrelTravel - x) * 1e3:F1} mm short of the muzzle. " +
                    "Charge is too small, or the powder is far too slow for this barrel.";
                return result;
            }

            if (input.CaseMaximumPressure > 0.0 && peakPressure > input.CaseMaximumPressure)
            {
                result.Status = InteriorBallisticsStatus.Overpressure;
                result.Message =
                    $"Peak pressure {Units.PascalsToMegapascals(peakPressure):F0} MPa exceeds the case limit of " +
                    $"{Units.PascalsToMegapascals(input.CaseMaximumPressure):F0} MPa. The case ruptures.";
                return result;
            }

            result.Status = InteriorBallisticsStatus.Success;
            return result;

            // ---- Local functions --------------------------------------------

            // Chamber pressure as an algebraic function of the state.
            double Pressure(double zz, double xx, double vv)
            {
                double psi = grain.BurntFraction(zz);

                double freeVolume = input.ChamberVolume
                                    - (chargeMass / propellant.SolidDensity) * (1.0 - psi)  // unburnt solid
                                    - propellant.Covolume * chargeMass * psi                // gas covolume
                                    + input.BoreArea * xx;                                  // swept volume

                // Guard: a badly overloaded case can drive this to zero or below.
                // Clamping to a small positive volume yields a huge (but finite)
                // pressure, which the overpressure check then reports honestly.
                if (freeVolume < 1e-12) freeVolume = 1e-12;

                double kineticWork = 0.5 * effectiveMass * vv * vv;
                double numerator = impetusEnergy * psi + primerTerm - theta * kineticWork;

                // Energy cannot go negative: once the gas has given up everything it
                // had, pressure is zero, not negative.
                return numerator > 0.0 ? numerator / freeVolume : 0.0;
            }

            // Retarding pressure opposing the projectile: high while engraving,
            // dropping to bore friction once the bearing surface is fully in the
            // rifling. Written as a function of x alone so the derivative stays a
            // pure function of state and RK4 stays valid.
            double ResistivePressure(double xx)
            {
                if (xx >= input.EngravingDistance) return input.BoreFrictionPressure;
                double u = input.EngravingDistance > 0.0 ? xx / input.EngravingDistance : 1.0;
                return input.ShotStartPressure + (input.BoreFrictionPressure - input.ShotStartPressure) * u;
            }

            void Derivatives(double zz, double xx, double vv, out double dz, out double dx, out double dv)
            {
                if (zz < 0.0) zz = 0.0;
                if (zz > 1.0) zz = 1.0;
                if (vv < 0.0) vv = 0.0;

                double p = Pressure(zz, xx, vv);

                // Vieille: linear regression rate divided by the web gives dz/dt.
                // Burning stops once the grain is consumed.
                dz = zz >= 1.0
                    ? 0.0
                    : propellant.BurnRateCoefficient * Math.Pow(p, propellant.BurnRateExponent)
                      * grain.DeterrentFactor(zz) / grain.WebThickness;

                double net = p - ResistivePressure(xx);

                // Before the projectile has moved, resistance is static: it holds the
                // projectile in place rather than pushing it backwards.
                if (net < 0.0 && vv <= 0.0) net = 0.0;

                dv = net * input.BoreArea / effectiveMass;
                dx = vv;
            }
        }

        private static InteriorBallisticsResult Fail(ref InteriorBallisticsResult r, string message)
        {
            r.Status = InteriorBallisticsStatus.InvalidInput;
            r.Message = message;
            return r;
        }
    }
}
