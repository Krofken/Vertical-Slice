# Krofken Ballistics

A physical ballistics library. Interior, exterior and terminal simulation driven by
real projectile geometry and real material properties, in SI units throughout.

**Zero dependencies.** No UnityEngine, no Unity.Mathematics, no Collections — plain
.NET structs. It compiles and runs inside Unity, inside a headless dedicated server,
inside a `dotnet test` project, and inside a standalone validation tool, unchanged.

---

## Why it is built this way

**Nothing is a gameplay stat.** Every field is a measurable physical quantity. A
material has a density, a yield strength and an elongation at break — not a "damage
bonus". The four classic ammunition archetypes are not four code paths; they are four
points in one continuous material-and-geometry space, and the solver contains no
branch on ammunition type anywhere.

**One shape, three consumers.** A projectile is a solid of revolution described by
eleven numbers. The same `RadiusAt(x)` function feeds the mass integrator, the drag
model and the render mesh, so the thing simulated is provably the thing drawn. No
hand-authored bullet meshes exist or are needed.

**Bake once, fly cheap.** Committing a design runs an ODE, integrates mass properties
and samples a whole drag curve. Firing the round afterwards costs a table lookup per
step. See `CartridgeBaker`.

**Pure functions over explicit state.** `TrajectoryIntegrator.Step` is a pure function
of `(state, constants, dt)` with no hidden globals. That makes a trajectory replayable,
rewindable and re-simulatable at a past tick — which is what any future netcode needs,
and costs nothing to keep.

---

## Layout

| Path | Contents |
|---|---|
| `Runtime/Core` | SI units, double-precision `Vec3`, ISA atmosphere with humidity |
| `Runtime/Materials` | Material and propellant databases, grain geometry, form functions |
| `Runtime/Geometry` | Parametric projectile, profile sampler, mass/inertia integration |
| `Runtime/Interior` | Propellant burn to muzzle velocity |
| `Runtime/Exterior` | Geometry-driven drag model, baked drag table, RK4 trajectory |
| `Runtime/Terminal` | Penetration, deformation, fragmentation, reactive payloads |
| `Runtime/Cartridge.cs` | The facade: design in, baked round out |
| `Tests/Editor` | 50 EditMode tests |

---

## Usage

```csharp
var design = new CartridgeDesign {
    CaseId       = CartridgeCaseLibrary.NineMillimetre,
    Projectile   = ProjectileGeometry.Default9mmFmj,
    Materials    = ProjectileMaterials.JacketedLead,
    PropellantId = PropellantLibrary.SingleBase,
    GrainShape   = GrainShape.Sphere,
    WebThickness = 3.5e-5,               // metres
    DeterrentCoating = 0.3,
    ChargeMass   = Units.GrainsToKilograms(5.5),
    SeatingDepth = 0.0030,
};

var round = CartridgeBaker.Bake(design, BarrelLibrary.ServicePistol9mm);
if (!round.IsValid) { /* round.Issues explains why */ }

// Fly it
var state = round.CreateMuzzleState(elevation: 0.0);
state = TrajectoryIntegrator.Step(state, round.Aerodynamics,
                                  Atmosphere.Standard, TrajectoryOptions.Default, dt);

// Hit something
var impact = TerminalBallisticsSolver.Solve(
    round.Terminal, TargetMediumLibrary.BareGelatinBlock(), state.Speed);
```

---

## Physics, and how far to trust each part

Stated plainly, because the three stages are not equally rigorous.

### Interior ballistics — rigorous

Standard lumped-parameter model. Vieille's burn law `r = u1 * P^n`, a grain form
function `psi = chi*z*(1 + lambda*z + mu*z^2)` whose coefficients are *derived* from
grain geometry rather than fitted, the Nobel-Abel equation of state including the gas
covolume, and Newton's second law against engraving and bore friction. RK4 at 100 ns
steps.

Heat loss to the barrel is charged as a flat fraction (default 20%). That is a real
and large term a lumped model cannot compute spatially; practical interior ballistics
codes handle it the same way. Without it, efficiency comes out near 50% instead of
the 25–35% real small arms achieve.

### Exterior ballistics — rigorous integration, calibrated drag

The trajectory integrator is exact: it reproduces the closed-form vacuum solution to
better than one part in 10⁶.

The drag model is a physically-structured composite — turbulent skin friction
(Prandtl-Schlichting), empirical base drag scaled by base area with boattail pressure
recovery, and nose wave drag from modified Newtonian impact theory integrated over the
actual profile. **One coefficient is a fit**: Newtonian theory is exact only in the
hypersonic limit and under-predicts at the low supersonic Mach numbers small arms
actually use, so `LowSupersonicCorrection` bridges that gap, calibrated against the G7
standard drag curve.

Measured against G7 for a conventional rifle shape:

| Mach | model | G7 |
|---|---|---|
| 0.5 | 0.126 | 0.120 |
| 0.9 | 0.154 | 0.150 |
| 1.0 | 0.292 | 0.310 |
| 1.2 | 0.365 | 0.380 |
| 2.0 | 0.295 | 0.295 |
| 3.0 | 0.242 | 0.225 |

Within about 8% across the range, and every trend is directionally correct. Good
enough to rank designs and respond correctly to changes; not a substitute for
wind-tunnel or CFD data. If real numbers are ever needed for a specific shape, bake a
`DragTable` from measured data — nothing downstream knows or cares where its drag
curve came from.

### Terminal ballistics — physically structured, empirically calibrated

**This is the least rigorous of the three, and honestly so.** No first-principles model
of a projectile in tissue exists; published work in the field is itself empirical.

What is implemented is Poncelet resistance `F = A*(R_t + 0.5*C_d*rho*v^2)` with a
frontal area that evolves as the projectile deforms, deformation driven by comparing
stagnation pressure to the nose's effective yield strength, fracture governed by
material ductility, and reactive payloads with real initiation thresholds. Media are
calibrated so a conventional 9mm FMJ penetrates the 60–70 cm of 10% ordnance gelatin
that is well established for non-expanding handgun projectiles.

It ranks designs correctly and responds correctly to changes. It is not a wound
ballistics prediction and must not be presented as one.

**Known limitation:** thin-plate perforation is modelled with the same cavity-expansion
resistance as bulk media. Real thin plates fail by plugging and petalling, which is a
different mechanism. Steel-plate results are directionally right but should not be
read quantitatively.

---

## Calibration status

Baseline 9×19, 119 gr jacketed lead, 5.5 gr single-base spherical powder, 85 mm travel:

| Quantity | Model | Real |
|---|---|---|
| Muzzle velocity | 323 m/s | 340–380 m/s |
| Muzzle energy | 403 J | 400–500 J |
| Peak pressure | 223 MPa | ~230 MPa (CIP limit 235) |
| Thermodynamic efficiency | 29% | 25–35% |
| Time to muzzle | 457 µs | 500–900 µs |

Roughly 10% low on velocity at correct pressure. Well inside what a game needs, and
the sensitivities are all correct: more powder is faster *and* disproportionately
higher pressure, a finer web peaks harder, seating deeper raises pressure.

---

## Verification

The maths core is checked against closed-form answers, not eyeballed:

- Cylinder mass and inertia vs analytic — exact to 1 part in 10⁶
- Vacuum trajectory vs `v² sin(2θ)/g` — exact to 1 part in 10⁶
- Standard atmosphere vs ICAO sea level — exact
- Tangent ogive radius vs `(L² + r²)/2r` — exact
- Energy deposited never exceeds energy delivered

The rest is asserted *directionally*, deliberately. Pinning a calibrated model to
three decimal places just breaks every time the calibration improves; what must never
break is the direction.

```bash
dotnet test
```

Run inside Unity via the Test Runner (the package is registered in `testables`), or
outside Unity against the same sources — the tests reference only NUnit and this
package.

---

## Extending

Everything content-shaped is a runtime-registerable table, not an enum:

```csharp
MaterialLibrary.Register(new MaterialProperties { Id = "starmetal", /* ... */ });
PropellantLibrary.Register(/* ... */);
TargetMediumLibrary.Register(/* ... */);
CartridgeCaseLibrary.Register(/* ... */);
```

Fantasy materials work fine. They just have to have a density, a yield strength and a
ductility — and then they behave correctly without any new code, because the solvers
only ever ask physical questions.
