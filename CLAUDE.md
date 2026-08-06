# Gunsmith — project canon

Unity 6.4 (6000.4.0f1), URP 17.4. Single-player. This file is the **design canon**:
the decisions that were reached by discussion and cannot be re-derived from the code.

Read this before changing anything. The two READMEs cover *how* the systems work —
[the physics library](Packages/com.krofken.ballistics/README.md) and
[the slice mechanics](Assets/Scripts/Gunsmith/README.md). This file covers *why*, and
what must not be broken.

---

## The premise

You are a gunsmith in a city you cannot leave. A curse binds you to your shop; the
same entity that cursed you also blessed you, which is why you can make ammunition
nobody else can. Townfolk come to you, describe a problem, and you solve it without
ever seeing the situation yourself.

**The curse is not flavour — it is the design.** It is the reason every job arrives as
a description rather than a demonstration, and the reason the player solves problems
blind. Do not write content that lets the player observe the outcome directly.

## The one thesis

**The game is constraint conflict, not realism.** Realistic ballistics alone is a
calculator. What makes it a game is that the briefs are *physically impossible to
satisfy with a single round* — and real physics gives that for free:

- Penetration and energy dump are opposites. A round that stops inside dumped its
  energy there; one that exits kept some.
- Sectional density fights expansion. Velocity fights pressure and recoil. Powder mass
  fights the pressure limit.

`No_Single_Round_Satisfies_Every_Brief` in `Gunsmith.Tests` enforces this and **fails
the build** if any load ever satisfies all five customers. If you add an order, it must
conflict with at least one existing brief. That test is the game design expressed as an
assertion — never weaken it to make a new order pass.

---

## Non-negotiable design rules

### 1. No numbers at the test range. The artifact is the readout.

The player must never see numeric output in the yard. Only observations: is there an
exit wound, how big, did it break up, did it ricochet, how deep did it stop.

Numeric readouts get memorised into a lookup table, which kills the experimentation
loop the entire game is built on. **Don't show the player what happened — hand them the
thing that happened.** Every value the sim computes has a physical expression, and the
physical expression is strictly better: persistent, comparable, unmemorisable.

| What the sim knows | What the player sees |
|---|---|
| Peak pressure | The fired case — flattened primer, ejector mark, split neck |
| Burnt fraction | Muzzle flash size; unburnt grains left on the bench |
| Stability factor | The witness card — round hole = stable, oval slot = keyholing |
| Muzzle velocity | Recoil, report, and the delay between shot and impact |
| Expansion ratio | The recovered bullet, dug out and set on the bench |
| Fragmented | A tray of pieces instead of one bullet |
| Penetration depth | Which graduated band of the block it stopped in |
| Perforated | A hole in the back face, and a mark on the backstop |
| Energy profile | The shape of the cavity in the gel |
| Temporary cavity | Crack pattern radiating through the gel |
| Reactive payload | It is on fire |

The cavity silhouette is the highest-value one: `EnergyProfile` is already energy per
centimetre of depth — rendering it as cavity shape means the player reads a plot
without knowing it. Narrow tunnel throughout = FMJ. Bulge at 5 cm then tapering = a
hollow point opening. Violent flower at the entry face and nothing after = frangible.
Players learn to read these in about four shots.

**When exposing a new result, find its physical tell first.** If there isn't one, add
the measurement to the sim before adding the brief that needs it.

### 2. Numbers during crafting measure consumption, never predict performance

Numbers are allowed at the bench, and only to say how much of something you used.
**Never add a predicted-performance panel** — it would remove any reason to test, which
is the whole game.

The one sanctioned exception, floated and approved as opt-in: a **chronograph as a
late, expensive purchasable tool**. Early game stays pure observation; a player who
earns it can buy precision. It is diegetic and never forced on anyone.

### 3. Evidence persists

Blocks and recovered bullets **do not disappear**. A rack on the wall. Bullet 7 sits
next to bullet 4, still mushroomed, and you can see 7 opened wider. Blocks line up so
the player can walk down them. This converts "I need to remember" into "I can look."

The notebook is therefore **an index to physical evidence**, not a spreadsheet. Shot
7's page shows the recipe and points at the block on the rack.

### 4. Show difference, never absolute

Two blocks side by side with the delta marked: *deeper, wider, stopped sooner.* Arrows
and outlines, not figures. Relative comparison against the player's own previous
attempt cannot be memorised.

### 5. Duplicate-and-tweak must be the path of least resistance

Every saved load gets a one-click duplicate. If changing one variable is one click and
changing three is nine clicks, players naturally run controlled experiments and learn
causality fast. If changing everything is equally easy, they change everything and
learn nothing. Highest-value UX feature in the game, and nearly free to build.

### 6. Briefs in the customer's words, never engineering terms

A house guard says he works in crowds. He never says "sub-30 cm penetration, no
perforation." **Translating is the game.** `OrderRequirement.CustomerWords` is the
card; `OrderRequirement.Technical` is the range readout.

Known consequence, accepted deliberately: the tutorial problem is real. A player who
can't translate the brief will bounce. The notebook and the range instruments are the
teaching tools — budget UI time for them accordingly.

### 7. Orders may only be written against measured quantities

Everything in `MeasuredQuantity` is something an instrument can read. There is no
"stopping power" because no instrument reports one.

### 8. No hidden rolls, anywhere

The same design always produces the same result. The player's uncertainty comes from
not having tested yet, and testing costs rounds.

### 9. Consequences before numbers; critical requirements are where someone gets hurt

Delivery feedback leads with what happened to the customer. Missing a critical
requirement is a `Disaster` outcome regardless of how well everything else scored —
averaging it away would tell the player they did fine when somebody died.

### 10. Never cap fantasy values. Let physics and scarcity do it.

There is virtually no limit on core density — this is a fantasy world and absurd
materials are the point. **Do not add arbitrary caps.** The physics already punishes
overreach:

- Heavier bullet, same charge → **slower** (same impulse, more mass)
- Heavier usually means longer → gyroscopic stability falls → it keyholes, and the
  witness card shows a slot
- Denser core → more pressure for the same charge → flattened primers, then a case
  rupture
- Penetration gain is **logarithmic** in sectional density — doubling density doubles
  nothing

A shit-ton-density core yields a slow, unstable, brutally recoiling round that costs a
fortune. Self-limiting with no arbitrary rule. **Make exotic materials rare and
expensive, not forbidden.**

The one sanctioned *hard* constraint is **the gun**: a barrel fed extreme pressure
should wear and eventually let go. That makes the firearm a consumable and gives
overreach a cost beyond "it didn't work."

---

## Crafting: operate tools, don't fill in a form

Sliders in a panel feel like tax software. Freehand-drawing the case was considered and
**rejected** — fiddly, imprecise, and it fights the parametric system that makes the
whole thing work. Instead the numbers live **on the tools, diegetically**:

- **Powder on a beam balance.** Set the counterweight, trickle powder until the beam
  tips. Teaches "this much powder" as a felt quantity. The number is on the scale,
  where a number belongs.
- **The bullet is turned on a lathe**, live mesh reshaping as you work. The runtime
  lathe already regenerates fast enough to run on a slider drag.
- **Weigh the finished bullet.** One number, on a scale — the one that matters most.
- **Seat the bullet against a physical stop**, so seating depth is set on a tool.

## Pacing and economy

- **The night is finite** — roughly six to eight actions. Materials alone aren't enough
  of a constraint because a determined player just buys more lead. A finite night makes
  each test a real choice against crafting time, and the gap between "I loaded these"
  and "let's see" is where the anticipation lives.
- **Test rounds draw from a cheaper scrap pool**, separate from delivery stock.
  Without this, the first playtester is too scared to test anything.
- Deliveries resolve at Dawn, never at the moment of handover. The player never learns
  whether a round worked when they hand it over.

---

## Scope of the vertical slice

**In:** 3–4 orders per day with conflicting specs, take any subset; buy materials from
a tab (instant arrival); the full lathe + interior/exterior/terminal chain; the test
range with instruments; next-morning delivery with written feedback.

**Out, deliberately:** gun design (the customer brings the gun and a calibre, the player
perfects the round), multiple calibres (9 mm only), day-count and economy depth, NPC
models beyond primitives.

**The scene is the user's to build by hand.** No scene, no UI, no prefabs. Test scenes
are fine; the vertical slice scene is not to be authored by an agent.

---

## Architecture constraints

**`Packages/com.krofken.ballistics` has zero dependencies** — no UnityEngine, no
Mathematics, no Collections. This is not stylistic: **the package is reused in a second,
multiplayer game.** Portability of the core outranks any netcode feature here. Unity
code lives only in the separate `Krofken.Ballistics.Unity` assembly.

Keep every sim entry point a **pure function over explicit state** —
`TrajectoryIntegrator.Step(state, constants, dt)` is the model. That makes replay,
rollback and lag compensation possible later without paying for them now.

**Never run projectiles through PhysX rigidbodies.** At 800 m/s a 50 Hz fixed step
moves the bullet 16 metres per tick. Use the custom fixed-step integrator with swept
raycasts, fully decoupled from Unity's physics loop and deterministic.

**Doubles inside the solver, floats at the Unity boundary.** The interior-ballistics ODE
is stiff near peak pressure and float32 visibly drifts there.

**Bake at design time, look up at runtime.** Drag and pressure curves depend only on
shape, which doesn't change in flight. Baking is the real performance win, not
micro-optimisation.

**Optimise the solver, not the game loop.** The ballistics core is hard-optimised:
blittable structs, zero GC allocation, Burst-friendly, no LINQ or virtual dispatch in
the inner loop. Orders, inventory and the day cycle run a few times per *second* —
write those plainly. Readability wins there.

**Comment the physics, not the C#.** Every equation gets its term meanings, units, and
source (Vieille's law, McDrag, Poncelet) so the science can be audited later. No noise
comments on ordinary code.

**Content is runtime-registerable tables, never enums** — `MaterialLibrary.Register`,
`PropellantLibrary`, `TargetMediumLibrary`, `CartridgeCaseLibrary`.

**No branch on ammunition type anywhere in the solver.** The archetypes are points in
one continuous material-and-geometry space. If you find yourself writing
`if (type == HollowPoint)`, the model is wrong.

---

## Fantasy ammunition — confirmed viable, not yet built

Three tiers, agreed:

1. **Impossible materials — works today, zero code.** A core at 90,000 kg/m³ with a
   40 GPa yield strength is just a row in the table. The solvers only ask physical
   questions.
2. **Effects that bend the equations** — homing, anti-gravity, downrange acceleration,
   detonation at depth, armour bypass. Planned approach: a small blittable modifier
   struct on the baked data — `GravityScale`, `DragScale`, `ThrustAcceleration`,
   `HomingStrength`, `ArmourBypass`, `DetonationDepth` — read unconditionally, where
   1.0/0.0 mean normal physics. Costs nothing on the normal path, stays Burst-friendly,
   turns fantasy back into data. Roughly half a day, whenever it's actually needed.
   The existing reactive-payload mechanism (initiation threshold → energy at depth)
   already has the right shape for "magic bullet discharges inside the target."
3. **The game layer needs no changes at all.** Orders are judged on `PenetrationDepth`,
   `Perforated`, `EnergyDeposited` — a blessed round driving three metres is checked by
   the same comparison as a lead one.

**The real risk is design, not architecture.** If blessed ammunition is better on every
axis the game collapses, because the whole thing rests on no round satisfying every
brief. Fantasy materials must stay on a tradeoff curve: absurdly dense also means slow,
unstable, and ruinously expensive.

---

## Verification practice

Both suites stay green: **101/101 in Unity, 61/61 outside** via `dotnet test`. The
outside-Unity run is what proves the core is still portable to the other project — if it
breaks, a Unity dependency leaked into the core.

**Driving the Editor over MCP.** `Unity_RunCommand` sandboxes `System.Reflection` and
the dynamic command assembly cannot see the project's own types — so verify by writing
a real test into the package and running it, not by poking at things from a command.
The Test Runner is reachable: implement `ICallbacks` on `CommandScript` itself (nested
classes get mangled by the code rewriter), `Debug.Log` the summary from `RunFinished`,
and read it back with `Unity_GetConsoleLogs`. Editing a script triggers a domain reload
that drops the bridge for a few seconds; the call just needs retrying.

**A green Test Runner result can be a lie.** If a test file fails to compile, the runner
happily executes the last assembly that *did* compile and reports it as passing — the
count simply does not go up. Always check the console for compile errors before
believing a pass, and confirm the total moved by the number of tests you added.

**Verifying without a Unity bridge.** Not every session has a live Editor connection.
Two scratch projects cover most of it, and both just glob the real sources:

- A `dotnet test` project compiling `Runtime/**` minus `Runtime/Unity/**`, plus
  `Tests/Editor/**` minus `Tests/Editor/Unity/**`. Runs the whole core suite.
- A compile-only project referencing
  `C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Data\Managed\UnityEngine\*.dll`,
  which compiles the Unity adapter, the Gunsmith assembly and the Editor tools for
  real. Reference the `UnityEngine\*.dll` glob only — adding `UnityEditor.dll` on top
  makes `MenuItem` ambiguous against `UnityEditor.CoreModule`.

That catches every type and syntax error. It does **not** catch mesh correctness —
winding, normals and anything needing a running Editor still require the Test Runner.

Verify against closed-form answers rather than asserting correctness. Calibrated values
are asserted *directionally* on purpose — pinning a calibrated model to three decimals
breaks every time calibration improves; what must never break is the direction.

Testing caught six real modelling bugs that reading the code would not have:

- Max expansion exceeded fracture strain — every hollow point tore itself apart by
  construction
- The ductile jacket diluted the core's brittleness, so frangible rounds never
  fragmented
- No projectile-hardness model, so armour-piercing lost to FMJ against steel plate
- No cavity-plugging, so the denim test did nothing
- "Due tomorrow" silently granted two nights instead of one

**Unity gotcha worth remembering:** at 9 mm scale a mesh triangle's cross product is
~1e-8, and `Vector3.normalized` silently returns **zero** below `kEpsilon` (1e-5). It
reported a perfectly good mesh as 1632/1632 inside out. Real-world scale falls off the
end of Unity's float helpers.

### Reference behaviour

These fall out of material properties alone, with no per-type branching. Treat as the
regression baseline:

| Round | Gel penetration | Expansion | Outcome |
|---|---|---|---|
| FMJ | 52.9 cm | 1.04× | intact, over-penetrates |
| Hollow point | 23.4 cm | 1.45× | expands, stops inside — no exit |
| Armour piercing | 44.3 cm | 1.00× | defeats 3 mm steel; FMJ stops at 2 mm |
| Frangible | 4.2 cm | fragments at surface | 17 pieces, 8034 J/m peak |
| Incendiary | 26.5 cm | 1.38× | +618 J chemical release |

And the model produced this unprompted: the same hollow point goes from 23.4 cm / 1.45×
bare to **47.3 cm / 1.05× through four layers of denim, cavity plugged.** A real,
documented failure mode — and exactly the kind of brief a player can fail without ever
seeing a random number.

Worked solutions to all five briefs live in `ReferenceLoads` inside the test assembly,
on purpose — shipping them as presets would be shipping the answers.

---

## Current state

Physics core, Unity adapter, orders, economy, crafting, range, notebook and day cycle
are all built and green. `ProjectileSimulator` is written and tested for compile but has
**never had a projectile actually flown through it in a running scene** — it needs
colliders, so make it the first thing to check when the range scene exists.

**The gel block is built.** Four pieces landed:

- `WoundCavity` (core) — turns `EnergyProfile` into a radius-vs-depth curve via
  `r = sqrt( (dE/dx) / (pi * R_t) )`, reusing the medium's Poncelet strength term so
  the drawn cavity and the computed depth share one constant.
- `RecoveredProjectile` (core) — the mushroom, by volume conservation: the nose
  collapses into a flat head of the expanded radius, so the slug comes back shorter and
  wider with its mass intact. Returns nothing when the round fragmented.
- `ProjectileMeshBuilder.BuildFromProfile` — lathes an arbitrary `ProfilePoint`
  polyline, which is the generalisation the cavity and the slug both needed. The
  projectile path was left untouched rather than refactored, since the Editor's mesh
  tests could not be run that session.
- `GelBlockView` — block, graduated bands, cavity as a solid suspended in the
  transparent block, witness card with a round hole or an oval slot, recovered slug or
  a tray of fragments. Entry face at local z = 0, depth running along **-Z**.

**Verified.** **101/101 in Unity, 61/61 outside.** Six of those are winding and bounds
tests on the generalised lathe, including the mushroom shoulder — two profile points at
the same station, which lathes into a flat annulus and is the case a naive meridian
normal gets wrong. Blocks have also been looked at in a running scene: the silhouettes
are distinct and read correctly at a glance.

`Ballistics → Spawn Gel Block Preview` fires the five reference rounds into blocks and
lines them up, which is the evidence rack the design calls for. `Clear Gel Block
Preview` removes them.

**Loose end — `SampleScene` is dirty on disk.** The first session's note said its preview
objects were unsaved. They were not: `~ProjectilePreview` and its five bullets are
committed into the scene file, about 2000 lines of it. They have since been cleared from
the Editor's in-memory copy, and the gel block preview now sits there unsaved instead, so
the file and the Editor disagree.

The scene is deliberately **excluded from version control history** until it is cleaned.
To fix: run both `Ballistics → Clear …Preview` items, save once, then commit the scene.
Preview objects are throwaway — never let them reach a commit.

---

## Working across two Windows profiles

This project is worked on from two Windows users (`aluxi` and `Talha Ozer`). Files are
owned by `aluxi`, but the drive grants `Authenticated Users: Modify`, so both can write.

- **Quit Unity fully before switching users.** Unity holds `Temp/UnityLockfile`, and
  fast user switching leaves the Editor alive in the background session still holding
  it. Two Editors on one `Library/` is how it gets corrupted.
- Each profile needs `git config --global --add safe.directory 'C:/Unity/Vertical Slice'`
  and its own `user.name`/`user.email`, or git refuses to run on ownership grounds.
- Unity Hub sign-in and licence are per Windows profile.
- **The Unity MCP bridge is per profile too.** The relay lives at
  `%USERPROFILE%\.unity\relay\relay_win.exe` and the running Editor advertises itself in
  `%USERPROFILE%\.unity\mcp\connections\`, but the server has to be registered in that
  profile's `~/.claude.json`. Without it an agent has no Editor access and cannot run
  the Test Runner — fall back to the compile-check harness above.
- Claude sessions, memory and MCP config do **not** cross profiles. This file is the
  shared source of truth — put project decisions here, not in per-profile memory.
