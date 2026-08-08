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

**Built** — `EvidenceRack` and `RangeStation`. Every shot racks a block, in shot order,
and nothing clears them automatically. `RangeStation.TryFire` is the join between the
bench and the yard: it spends a round off the shelf, so a test costs ammunition and is a
real choice against the finite night.

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

**Built** — `DesignLibrary.Duplicate` / `GunsmithGame.DuplicateDesign`. The copy is baked
on creation so it is immediately loadable, and it names itself: "Brass Nose" becomes
"Brass Nose Mk2", then Mk3. That numbering is what makes a rack of recovered bullets
readable weeks later. **Keep duplicating cheaper than editing in place, always.**

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

## The bench is four stations

Agreed 2026-08-07, after the tools existed but the *stations* had never been written
down — which is how the bench ended up covering three of the ten things the sim reads
while the rest sat hardcoded and invisible. The player works four stations:

| Station | What it sets | State |
|---|---|---|
| **Propellant mill** | base chemistry, grain shape, web thickness, deterrent coating | **built** |
| **Core bench (lathe)** | projectile geometry, and the materials it is made of | **built** |
| **Press** | assembles case + primer + charge + bullet into rounds | **built** |
| **Case and primer** | case geometry, primer chemistry | **deferred — see below** |

The powder balance and the seating stop are **parts of the press**, not stations of
their own: you weigh a charge, pour it, then seat the bullet against the stop.
`LoadingPress` now feeds off all four tools, so the whole bench composes one cartridge.
The press designs nothing itself — it only gathers what the other tools made, adds a
case and a primer, and pulls the handle.

### The bench refuses what will not go together, never what is dangerous

Decided 2026-08-07 and built. `DesignIssue` carries a `DesignIssueKind`:

- **Assembly** — unknown case, propellant or material; invalid geometry; wrong calibre
  ("it will not chamber"); a charge that will not physically fit; nonsense inputs.
  These are facts about objects in the player's hands, and both the bench and the yard
  refuse them.
- **Ballistic** — overpressure, squib. These **assemble perfectly well, get loaded, and
  get fired.** The player finds out by firing them.

`BakedCartridge.CanAssemble` is the question the bench and the range ask.
`BakedCartridge.IsValid` still means "safe to fire" and is what tests and evaluation use
— **never gate crafting or firing on it.**

A gunsmith can see that a charge will not fit a case. They cannot see peak pressure.
Warning them hands over the answer and removes the reason to walk out to the range,
which is the whole game. A test asserts the bench never leaks the words *pressure,
unsafe, safely, burst, rupture* or *velocity* about a round nobody has fired — that
assertion is the guard on the rule.

**The tell is built** — `FiredCase` / `FiredCaseReader` in the core, `FiredCaseView` in
Unity, racked beside its block by `EvidenceRack`. Peak pressure now reads as brass.

The ordering is the physical part and it is asserted: the primer cup is softer than the
head and unsupported over the pin hole, so it yields first; brass then extrudes into the
pin hole and the ejector hole; then the pocket lets go; then the case splits. A property
test sweeps pressure upward and fails if any sign ever walks backwards — brass does not
un-flow.

**The thresholds start AT the case's rated maximum, not below it**, and that calibration
matters more than it looks. A rating is a working limit brass survives repeatedly, and
the project's own calibrated 9 mm runs at ~95% of the CIP limit. The first attempt put
the first sign at 0.80 and made the *reference load* come back with a flattened primer —
at which point every load looks hot and the gauge says nothing at all.

`FiredCase.PressureFraction` exists for the renderer to scale a bulge by. **It is a
pressure reading by another name and must never be printed, labelled or put in a
tooltip.** A test asserts `Describe()` contains no digit and no predictive word.

Still missing: the gun itself. A ruptured case means gas in the action, and the canon's
one sanctioned hard constraint is that a barrel fed extreme pressure should wear and
eventually let go. Nothing models that yet.

## Crafting: operate tools, don't fill in a form

Sliders in a panel feel like tax software. Freehand-drawing the case was considered and
**rejected** — fiddly, imprecise, and it fights the parametric system that makes the
whole thing work. Instead the numbers live **on the tools, diegetically**:

- **Powder on a beam balance.** Slide the poise to the charge you want, then trickle
  until the beam comes level. **Built** — `PowderBalance`. The moment balance is linear
  in poise position, which is why a real powder beam is evenly divided and why sliding
  a poise is a legitimate way to dial a number without a text field. The beam saturates
  against its stops within about a tenth of a grain, and that near-binary swing is the
  feel. The scale hands over **what was actually weighed**, not what was dialled.
- **The bullet is turned on a lathe**, live mesh reshaping as you work. **Built** —
  `LatheStation` plus `LatheHandle`, one handle per dimension.
- **The stock is chucked, not chosen from a menu.** **Built** — `LatheStation.StockRack`
  is ordered soft to hard, because stepping along it is stepping along the one comparison
  the terminal solver makes: impact stagnation pressure against the nose's yield
  strength. The work takes the colour of the stock in the chuck, and unknown or exotic
  stock is shaded by density so a registered fantasy material looks like something heavy
  without anyone picking a swatch for it. `PayloadRack` packs the cavity, and emptying it
  again is always reachable.
- **Weigh the finished bullet.** One number, on a scale — the one that matters most.
  **Built.**
- **Seat the bullet against a physical stop**, so seating depth is set on a tool.
  **Built** — `SeatingStop`. This is not set dressing: powder burns in the space behind
  the bullet and pressure goes roughly as the inverse of that volume, so seating deeper
  is the sharpest pressure lever on the bench. `Seating_Deeper_Raises_Peak_Pressure`
  guards it — if that ever stops holding, the die has become decoration.

**One handle moves one dimension, and that is load-bearing.** If changing one variable
is one drag and changing three is three drags, players run controlled experiments and
learn causality. `Each_Cut_Changes_Only_Its_Own_Dimension` asserts it across every
operation, allowing only couplings that are real constraints of the shape — a cavity
cannot be wider than the meplat it opens onto, or deeper than the nose containing it.
Anything else leaking between handles is a bug.

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

**Case and primer design — out of the slice, required in the full game.** Confirmed
2026-08-07. The player designing the case and mixing the primer is a **must** for the
finished game, not a maybe. It is out of the vertical slice for one concrete reason: it
is the only station on the list that needs NEW PHYSICS rather than a tool over physics
that already exists — primer brisance has to feed the ignition model, and case geometry
has to become a designed shape rather than a library row. Everything else the bench
still lacks is already fully modelled and merely unreachable.

Until then the case stays a `CartridgeCaseLibrary` pick (9 mm only, and that stands) and
the primer stays a counted consumable in `WorkshopInventory` with no design surface.
**Do not quietly design around this as though it were cut.** It is deferred.

**~~The scene is the user's to build by hand.~~ REVOKED 2026-08-08.** Building, laying out
and art-directing the scene is the agent's job now. See the rule at the top of
**Verification practice**.

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

This has now been violated twice the same way, both times caught by a test, so it is
worth naming the trap: **a serialised Unity field defaults to `float`, and if a solver
input is computed from it, the float's imprecision lands in the physics.** A `float`
beam length made a 6-grain charge weigh 6.0000001 grains; `Mathf.Deg2Rad` made a
20-degree boattail 20.000000078 degrees. Neither mattered visually and both were real
noise on a solver input. If a field feeds a number the solvers read, declare it
`double` and cast to `float` only when assigning to a transform.

**Bake at design time, look up at runtime.** Drag and pressure curves depend only on
shape, which doesn't change in flight. Baking is the real performance win, not
micro-optimisation.

### Measured cost, so this stops being re-litigated

Taken 2026-08-07 on the development machine, Release build:

| | cost |
|---|---|
| Bake a complete cartridge — interior ODE, mass integration, full drag curve | **0.881 ms** (~1135/second) |
| Terminal solve, per impact | **0.030 ms** |
| Trajectory RK4 step | **0.183 µs** |

**Nothing here simulates individual grains, and nothing simulates gas as a fluid.** The
interior model is lumped-parameter: the whole charge is one scalar `z`, the fraction of
the web burnt through, and the grain form is three closed-form coefficients in
`psi(z) = chi*z*(1 + lambda*z + mu*z^2)`. The gas is one pressure from the Nobel-Abel
equation of state, with a covolume term because propellant gas at 300 MPa is dense
enough that its own molecular volume matters — dropping it visibly overestimates peak
pressure.

The only per-grain objects in the project are the ~24 cosmetic spheres
`PropellantMill` drops in its tray so a coarse powder visibly *is* coarse. They are
editor-side presentation and never touch a solver.

**So the "store the energy instead of simulating it" optimisation is already the
architecture**, and there is nothing left to reclaim: a night is six to eight actions,
and you would need to commit a thousand designs a second to notice the bake. If
performance ever does bite it will be mesh rebuilds or scene objects, not the solver.
Measure before trading away physics.

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

### THE RULE, set 2026-08-08 after two sessions of getting this wrong

**Do not run the EditMode suite. Do not write code-level unit tests. If it compiles, that is
enough correctness checking.** Every test from here is a GAMEPLAY test: can a person at a
mouse actually do the thing.

This replaces the practice described below, which stays only as a record of how the physics
was originally validated. The suites are not to be used as evidence that anything works.

**Why, in the user's words:** players do not care if you have the best code in the world if
they cannot play something that represents that code. Twice now an agent reported "199/199
EditMode, 22/22 PlayMode, all green" while the game was unplayable — lean-in camera angles
pointing at nothing, stations that could not be operated, powder grains rendered absurdly
large. The tests asserted that a raycast reached a collider and that objects existed. Nothing
asserted that the game was playable, so nothing caught that it wasn't.

**The failure mode to watch for in yourself:** substituting what is cheap to measure (a
raycast hit, an existence check, a suite total) for what was asked (look at the screen and
judge it). Measuring produces a number and feels like progress. Looking requires entering the
game and forming an opinion. Do the second one.

**So verification means:** press Play, stand where the player stands, walk to the station,
lean in, and LOOK — through the player's own camera, never a staged one. Screenshot it. Judge
it by eye. A geometric probe from a computed eye position is not a player, and a green number
is not evidence.

**The scene is now the agent's to build.** The old rule that the vertical slice scene is the
user's to author by hand is REVOKED as of 2026-08-08 — building, laying out and
art-directing the scene is part of the job.

### How the physics was originally validated (historical)

All three suites stay green: **199/199 EditMode and 17/17 PlayMode in Unity, 70/70
outside** via `dotnet test`. The outside-Unity run is what proves the core is still
portable to the other project — if it breaks, a Unity dependency leaked into the core.

PlayMode went 8 → 17 on 2026-08-08 with `Tests/Runtime/WorkingTheBenchPlayTests`, which
asserts the bench RESPONDS rather than merely existing: that a leaned-in aim ray reaches a
lathe handle, that the station's trigger box no longer shields it, that the die has something
to take hold of, that the shop has a yard, that pulling the handle says what it did, and that
the press never leaks a performance word. `StandingUpPlayTests` proved the gunsmith stands up
in a room; nothing had checked he could operate anything in it. Read the caveat in **The
prefab is frozen** above before trusting any of them about the authored shop.

**Re-measure these numbers when you touch them; do not copy them forward.** They were
stale by seventy tests once already, in a file that warns three paragraphs below about
exactly that.

PlayMode results are not reported through `ICallbacks` reliably, because entering play
mode reloads the domain and drops the callback. Read
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Vertical Slice\TestResults.xml` instead —
it is written by both suites, so note its timestamp before starting a run.

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

**Assert on flattened text when the view wraps it.** `OrderBoardTests` compared the
card against `Order.Brief` with a raw substring check, but the card word-wraps to a
fixed column. The positive assertion failed loudly, which was the easy half. The
dangerous half was the negative one: a leaked technical spec would be wrapped too, so
`Does.Not.Contain` could never have caught the leak the test exists to catch. It was
passing by accident. Collapse whitespace on both sides before comparing display text.

**`TextMesh` has no layout, so never hand-pick a character size.** It does not wrap, does
not clip, and does not know how big the card it is written on is. Both readouts in the
shop were sized by a constant and both were wrong — the order cards rendered **4x wider
and 6x taller than the card**, so three briefs 36 cm apart overprinted into a smear, and
the status ran off the side of the screen. A constant cannot fix it either, because the
text changes: a longer name or one more requirement overflows again. `TextFit` measures
what was actually rendered and scales to fit. Two traps in doing so: bounds are only
valid once the object is fully built, so fit in a SECOND PASS after the cards exist; and
fitting multiplies scale, so reset to a stored resting scale before re-fitting or the
label shrinks away over successive refreshes.

**Check what is in front of what.** The clickable cork board sat at z = 1.18 with the
cards pinned at 1.28, so the board drew over the very cards it was holding. Fixed by
depth ordering, but the general lesson is that a flat-quad UI in world space needs its
layering thought about explicitly.

**A delegate cannot be saved, and a dead fixture has no symptom.** `Interactable.Used`
was a `System.Action` assigned by the builder. That is invisible to serialisation, so
the moment the shop could be written to a prefab, a loaded copy came up with every
fixture present, highlighted, promptable — and inert. No error, no missing object,
nothing in the console. Anything a saved object needs must be a serialised FIELD; the
enum `ShopAction` carries the intent and `WorkshopController.BindFixtures` turns it
back into a method on Awake. Written down because the failure is silent, and because
the same trap catches `UnityEvent`-free wiring of any kind.

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
are all built and green.

**`ProjectileSimulator` has now been flown.** It was previously compile-tested only. A
PlayMode suite (`Tests/Runtime`) fires a real round at a real collider 25 m downrange and
checks it arrives, arrives about when its speed says it should, loses some speed but not
all of it to drag, hits close to square, and **drops** rather than rises. That last one
matters: the simulation-frame-to-Unity conversion happens only inside this component, so
a sign error there is invisible to every EditMode test.

PlayMode tests are the right tool whenever behaviour depends on `Update`, PhysX queries
or the coordinate conversion. Keep them fast — do not write one that waits out
`maxFlightTime` (15 s) in real time; launch slow instead if what you want is the expiry
path.

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

**Verified.** Six tests cover winding and bounds on the generalised lathe, including the
mushroom shoulder — two profile points at the same station, which lathes into a flat
annulus and is the case a naive meridian normal gets wrong. Blocks have also been looked
at in a running scene: the silhouettes are distinct and read correctly at a glance.
(Suite totals live in **Verification practice** above; do not restate them here, or they
go stale in two places at once.)

`Ballistics → Spawn Gel Block Preview` fires the five reference rounds into blocks and
lines them up, which is the evidence rack the design calls for. `Clear Gel Block
Preview` removes them.

**`SampleScene` holds the authored shop, deliberately.** It was previously kept to the
camera, light and volume, on the rule that nothing should be serialised by accident.
That rule stands for ACCIDENTS; the workshop is there on purpose, because a shop that
only exists during Play cannot be edited or art-directed. It is a **prefab instance**, so
the scene is 945 lines rather than the four thousand an inlined hierarchy would cost.

It had also once picked up `~ProjectilePreview` and its five bullets — roughly 2000
lines of throwaway objects saved in by accident — which were cleared and cannot recur.

**Keep it that way, and it now keeps itself.** Every preview spawner tags its whole
hierarchy `HideFlags.DontSave`, so previews are never serialised, the open scene never
goes dirty, and one cannot reach a commit by accident. They vanish on a domain reload,
which is correct — they are disposable and the menu item rebuilds them.

That flag is load-bearing for a second reason: **a dirty scene makes every domain reload
stop and ask "save your changes?"**. Script edits, entering play mode and test runs all
reload the domain, so a preview left in a dirty scene turns into a modal dialog blocking
the editor until a human clicks it. Do not use `Undo.RegisterCreatedObjectUndo` on
preview objects — it dirties the scene and brings the prompt back.

Before this, entering play mode for the PlayMode suite silently wrote ~2900 lines of
bench into `SampleScene` with nobody pressing save. Check `git diff --stat Assets/Scenes`
if you ever suspect it has come back. The vertical slice scene itself is still the user's to build by
hand.

**The lathe is open at `Gunsmith → Open Lathe Bench`.** Drag the coloured handles — with
the mouse in play mode, or with the move gizmo in edit mode, since `LatheStation` is
`[ExecuteAlways]`. The mesh is generated at true size; only the rig transform is scaled
up, so nothing a solver reads is touched.

---

## The workshop you walk around

Built after the canon was last revised, so it is recorded here in full. **Every action
is a PLACE YOU GO**, not an entry in a menu — this replaced a row of buttons, which was
the wrong shape for a game whose whole premise is that you cannot leave the shop.

| Piece | Role |
|---|---|
| `WorkshopBootstrap` | The **only** object saved in the scene. Builds the shop on Awake. |
| `WorkshopBuilder` | Constructs the room and every station **at runtime**. |
| `PlayerRig` | A body that stands in the shop and walks. Input System, not legacy `Input`. |
| `Interactable` | Walk-up-and-use. Prompts are second person and name the object. |
| `WorkshopController` | Joins the stations to `GunsmithGame` — the playable night. |
| `OrderBoardView` | Cards by the door, in the customer's words only. |
| `DeliveryReportView` | The morning-after note; leads with the person, not the number. |
| `EvidenceRack` | Blocks, slugs and fired cases persist so you can walk the row. |

**Construction MUST live in the runtime assembly, not an editor tool.** This is a real
mistake already made and fixed: the shop was assembled by an editor tool and tagged
`HideFlags.DontSave` so previews could never dirty the scene. But `DontSave` means *not
serialised*, so pressing Play reloaded the scene, the workshop evaporated, and the game
was an empty room. Do not move construction back behind an editor menu.

### The shop is authored, not conjured

The fix above overshot: the shop existed ONLY while the game ran, so there was nothing
to select, nothing to move, and no way to art-direct a room you cannot see. Both
properties are now available and `WorkshopBuilder` takes a `persistent` flag:

- **disposable** — everything `DontSave`. A preview that cannot reach the scene file.
- **persistent** — real objects with real materials, meant to be saved.

Three menu items, in order:

| `Gunsmith → Author →` | Does |
|---|---|
| 1. Generate Materials And Palette | Writes 32 `.mat` assets and `WorkshopPalette` |
| 2. Build Editable Workshop | Builds the shop into the scene as ordinary objects |
| 3. Save Workshop As Prefab | Writes `Assets/Prefabs/Workshop Shop.prefab` |

**Materials had to become assets first.** The builder used `new Material(shader)` tagged
`DontSave`, which has no asset behind it, so anything referencing one could not be
serialised — every station was un-prefabbable by construction. `WorkshopPalette` holds
them, empty slots fall back to the old flat colours, and it doubles as the art-direction
surface: swapping the bench to real wood is picking a slot, not editing source.

**`WorkshopBootstrap.Shop` must stay a serialised FIELD.** It was a read-only property,
so it was never saved, so it was always null on Awake, so the bootstrap rebuilt over the
top of any hand-placed layout every single time. It now adopts an assigned or child
`WorkshopController` and only builds when there genuinely is none.

**Strip `DontSave` children before writing a prefab.** They cannot be saved, so the
asset's layout differs from the instance's and Unity warns that data may be lost. The
only ones are the propellant mill's ~24 cosmetic grains, regenerated whenever the powder
changes.

What stays procedural, and should: the projectile mesh, the wound cavity, the recovered
slug, the fired case, the order cards. Those are pictures OF A SIMULATION RESULT. A
bench is not.

Second fix worth keeping: the player is deliberately **not** parented to the bootstrap,
so an edit-mode preview leaves an orphan `PlayerRig` at the scene root that clearing the
preview never touches. They accumulate, each runs its own `Update`, and
`FindAnyObjectByType` then returns whichever it likes. `BuildPlayer` now destroys every
existing rig first. There is exactly one gunsmith.

**The stations do not share a natural scale and cannot be made to.** A 13 mm bullet needs
exaggerating roughly forty times before it is worth looking at; a gel block is already
person-sized. Only rig transforms are scaled — never anything a solver reads.

### Known broken, in priority order — START HERE

Written down 2026-08-08 after actually playing it. Everything below was seen on
screen, not inferred. **Do these before anything else, and verify each by looking at
the running game rather than by measuring the thing you just changed.**

1. **Powder charging is the wrong interaction entirely. BUILT 2026-08-08 — needs a human
   at the mouse to judge the feel.** `PowderMeasure` is the tin: aim at it, hold the left
   button, and pull the mouse back to tip it. Powder falls into the pan and the scale counts
   up. Release to right it; right-click while holding tips the pan back out.

   **And the beam-and-poise was not merely the wrong interaction — it was no interaction at
   all.** `SlidePoise` and `Trickle` were called only from the test assembly and from the
   builders, which poured a fixed 5.5 grains at construction. So the charge weight, the most
   consequential number on the bench, **could not be changed by playing the game**, and
   every load started already at the reference charge. Third station found in that state,
   after the seating die and the press readout.

   **One control gives both rates, and that is the whole trick.** Flow goes as the cube of
   how far the tin is tipped:

   | tip | flow | to fill 5.5 gr |
   |---|---|---|
   | full over | 9.0 gr/s | 0.6 s — the coarse pour |
   | a third | 0.18 gr/s | a tenth of a grain takes half a second |

   Fifty to one from one continuous motion, so the player slams it over and then feathers
   it, which is how the real job is done. A linear pour cannot land a charge — by the time
   you react you are a grain over — and that is why the canon asked for coarse-then-fine.
   `PourThreshold` means a tin resting over the pan does not dribble.

   The scale's readout now shows **what is in the pan**, not what a poise was set to. With a
   pour there is no target, so the setting was a number about nothing. Still a consumption
   figure, which is the sanctioned kind.

   Not new physics, exactly as the canon said: this writes `PowderBalance.PouredGrains`,
   which is what `ApplyTo` has always turned into `CartridgeDesign.ChargeMass`.

   **What is verified and what is not.** Verified: the tin exists in the authored shop, is
   the first thing the aim ray reaches from the lean-in eye at 13.5 cm, the pan starts empty,
   the flow curve has the range above, poured grains reach the design unchanged, and the pan
   can be tipped back. **Not verified: whether `TiltSensitivity` (0.004 per pixel) feels
   right**, because mouse input cannot be synthesised over the MCP bridge. Sit down and pour
   a few charges; if it is twitchy or sluggish that one field is the dial.

2. **`LatheStation` handles do not drag. FIXED 2026-08-08.** Neither candidate cause was
   it — `Mouse.current` was fine and `Camera.main` correctly resolved to the player's head.
   The cause was a third thing, and it was measured in the running shop rather than
   reasoned about: **`WorkshopBuilder.LeanIn` wraps every station in a 17 cm `BoxCollider`
   marked `isTrigger`, and `Physics.queriesHitTriggers` defaults to true**, so the grab ray
   stopped on the station's own box at 6 cm and never reached the handles 11 cm further in:

   ```
   [0] d=0.062  Core bench    trigger=True    <- the ray stopped here
   [1] d=0.169  Cavity mouth  trigger=False
   [2] d=0.170  Meplat        trigger=False
   ```

   Every handle asked "did the ray hit ME", the answer was always no, and nothing was
   dragged. **A trigger in this shop means "you may walk up to this", never "this is
   solid", and must never occlude the work it surrounds.** The grab ray now ignores
   triggers, which also gets the nearest solid hit in one allocation-free raycast. It lives
   in `Aim` (`Interaction/Aim.cs`) and is shared with the die, so the bug cannot be fixed in
   one tool and left standing in the other.

   Secondary, still open: at true 9 mm scale the meplat and cavity-mouth beads sit about
   1.2 mm apart with 2.5 mm spheres, so those two interpenetrate and are hard to tell
   apart. `AxisOf` spreads the handles for exactly this reason and it is not enough at the
   tip.

3. **`SeatingStop` does not respond either. FIXED 2026-08-08 — and it was NOT the same
   cause.** The lathe's handles existed and were occluded; **the die's handle did not
   exist at all.** `SetStop` documented itself as "bound to a draggable handle" and nothing
   outside the test assembly had ever called it. Now `SeatingHandle`, on the stop itself,
   because you take hold of a real die body rather than a bead beside it.

   Two causes, one symptom. Treating them as one bug would have fixed half and left the
   other half looking fixed.

4. **The press handle produces nothing. FIXED 2026-08-08.** It in fact produced rounds
   correctly every time — composed, committed, baked, consumed stock, put 20 rounds on the
   shelf — and reported it to `Debug.Log`, which a player standing in the shop cannot see.
   `BuildBench` wired every station that feeds the press and never assigned
   `LoadingPress.Readout`, and the shop deliberately has no status board, so success and
   failure looked identical: nothing happened. The press now reports what the pull did,
   beside the handle, and reads "press empty" before the first one. Consumption only — a
   test asserts the readout never leaks a performance word.

5. **The lean-in poses are wrong.** `StationView.EyeOffset` values were picked by
   arithmetic, never by eye. The powder balance shows the beam edge-on with the pan
   cut off the side of the screen. Sit in each one and adjust — the gizmos draw the
   eye and the look target for exactly this.

6. **The mill is unreadable.** You cannot tell what it is for or what the knobs do.
   Its purpose is the burn rate, and it is the sharpest pressure lever on the bench —
   on one 5.5 gr charge, changing ONLY the web:

   | web | muzzle velocity | peak pressure |
   |---|---|---|
   | 15 µm | 366 m/s | 499 MPa — the case ruptures |
   | 30 µm | 337 m/s | 262 MPa |
   | 60 µm | 258 m/s | 128 MPa, only 71% burnt |

   Finer is not better. Finer bursts the case; coarser throws unburnt powder out of
   the muzzle as flash. None of that is visible at the station.

   **Still open as of 2026-08-08.** Note before starting: the mill is not merely unreadable,
   **none of its controls can be operated by playing at all.** `SetWeb`, `SetDeterrent` and
   `NextShape` are called only from the builders and the test assembly. Making it readable
   and making it reachable are the same job. `Aim` and the drag handles are there to build
   on now.

### The pattern behind items 1, 3 and 6: tools with no hand attached

Found while fixing the above, and it is the largest single thing wrong with the bench. **A
station having a documented, tested API does not mean a player can touch it.** Every one of
these was called only from the builders — which set a value once at construction — and from
the test assembly:

| Control | Sets | Reachable by playing? |
|---|---|---|
| `PowderBalance.SlidePoise` / `Trickle` | the charge | **was no** — now poured, see 1 |
| `SeatingStop.SetStop` | seating depth | **was no** — now `SeatingHandle`, see 3 |
| `PropellantMill.SetWeb` / `SetDeterrent` / `NextShape` | burn rate | **still no** |
| `LatheStation.NextCoreMaterial` / `NextJacketMaterial` / `NextCavityFill` | the stock in the chuck | **still no** |

So the canon's "the stock is chucked, not chosen from a menu" is presently neither — there
is no way to change stock while playing. Of the ten things the sim reads, only the nine lathe
dimensions and now the charge can actually be set by a person in the shop.

This is why the EditMode suite stayed green through all of it: it drives the API, and the
API was always correct. **Test through the input path, or the test proves the wrong half.**

**Where the list stands:** items 1 to 4 are fixed and verified in the running shop. What is
left is the lean-in poses (5), the mill (6), and the two unreachable control sets above.

### The bug shape that keeps recurring

**A component must find its own parts. Do not trust whoever built it.**

This has now caused four separate failures, each silent:

- `Interactable.Used` is a delegate, so a prefab came back with every fixture inert
- `WorkshopBootstrap.Shop` was a read-only property, so it rebuilt over hand-placed work
- `PlayerRig` cached its rest pose in `Awake`, one line before `Head` was assigned,
  putting the eye 10 cm off the floor
- `WorkshopController.Yard` was null, so firing answered "no yard" forever

The fix is the same every time: resolve lazily, adopt what is already in the
hierarchy, and leave anything explicitly assigned alone. **Check the remaining
construction-time wiring for the same shape before it bites a fifth time.**

#### The fifth one was found, and it was not a wiring bug at all

Hunted 2026-08-08 as instructed, and the answer reframes the fourth entry above.
`WorkshopController.Yard` being null is recorded there as fixed by resolving lazily. **It
was still broken**, and lazy resolution could never have fixed it, because there was
nothing left in the hierarchy to adopt:

**One MonoBehaviour per file, named after it. Unity resolves a script reference BY FILE
NAME.** A behaviour sharing a file with another class cannot be serialised at all — the
editor writes `m_Script: {fileID: 0}` and it comes back as "the referenced script on this
Behaviour is missing", with every serialised field still intact beside a dead pointer.

`RangeStation` lived at the bottom of `EvidenceRack.cs`. So the authored shop's yard had
its `Range`, `MediumId`, `BlockThickness` and `Rack` all sitting correctly in
`Workshop Shop.prefab` next to a null script, `AdoptStations` found no `RangeStation`, and
**firing answered "no yard" forever in the game the user actually plays.** Three
"missing script on 'Shop'" warnings had been in the console the whole time.

`PlayerInteractor` had the same defect inside `Interactable.cs` and had not bitten yet
purely by luck: the player rig is rebuilt at runtime and deliberately never parented to the
bootstrap, so nothing had tried to save it. Prefabbing the player — an obvious next step for
art-directing him — would have made every interaction in the shop go dead with no error.

Both are now in their own files. The already-saved prefab needed a separate one-line repair
to its dead pointer, done surgically rather than by re-authoring the room, because
re-authoring would discard the hand-placed layout the prefab exists to preserve.

**This failure is silent, survives a fully green test suite, and only appears once
something is saved.** Grep for `class \w+ : MonoBehaviour` and check each against its file
name before adding any behaviour.

### The prefab is frozen, so builder changes do not reach the game

Learned 2026-08-08, immediately after a green suite lied about it. **The shop the player
walks around is a PREFAB INSTANCE that `WorkshopBootstrap` ADOPTS rather than rebuilds.**
So a fixture added to `WorkshopBuilder` appears in a freshly-built shop and **never** in the
authored one: the prefab was saved before the new part existed, and the only way to refresh
it is to re-author the room, which throws away the layout the prefab is there to keep.

This produced a false green with exactly the shape the canon already warns about for staged
cameras. Seven new PlayMode tests passed — they build their own shop via `WorkshopBootstrap`
on a bare GameObject, which has no child controller to adopt, so it takes the code-built
path. Probing the *running prefab shop* straight afterwards found no press readout and no
seating handle. **A PlayMode test that builds its own shop does not test the authored one.**

The repair is the canon's own rule pushed one step further: **a component must fit its own
missing parts, not merely adopt them.** `LoadingPress` builds a readout if it has none;
`SeatingStop` fits its own `SeatingHandle`. That works in all three shops — code-built,
prefab-restored, hand-placed. Both are **runtime only** (`Application.isPlaying`), because
creating objects in edit mode dirties the scene and turns every domain reload into a "save
your changes?" dialog.

When adding a fixture, ask which of the three shops it reaches. If the answer is only the
code-built one, it is not built yet.

### A TextMesh reads from its −Z side, not its +Z side

Measured 2026-08-08, because the intuition is backwards and a wrong "fix" here would mirror
every label in the shop. Rendering a glyph from both sides gives identical lit-pixel counts
— `GUI/Text Shader` does not cull — and the glyphs land correctly **only from −Z**. So a
`TextMesh`'s `forward` points AWAY from whoever is reading it.

The player stands on the −Z side of the bench, so **the builder's unrotated labels are
already correct**, and rotating one 180° to "face" the player is what mirrors it. An
intermediate check that used `dot(forward, toEye) > 0` as the readable test had the sign
inverted and briefly condemned all five bench readouts as mirrored; they are fine. If you
touch label orientation, render it and count pixels rather than reasoning about it.

### And the verification rule that was learned the hard way

**Never stage the camera to check something.** Every screenshot taken to "verify" the
shop had the camera positioned by hand first, which bypassed the broken path and made
it look correct while the game was unplayable. 199 EditMode tests passed at the same
time, because not one of them entered play mode with a player in it. If a check cannot
be done by pressing Play and looking, it is not a check. See
`Tests/Runtime/StandingUpPlayTests`.

### Known gap: it does not look like a shop yet

Everything above is built out of `PrimitiveType.Cube`, `Sphere` and `Cylinder` in flat
untextured colour. It reads as furniture floating in a skybox rather than a room: one
back wall, no side walls, no ceiling, and large dead gaps between stations.

This is a deliberate ordering — mechanics before art — and it is **contained to
`WorkshopBuilder.cs`**. Nothing in the stations, the physics or the game loop knows what
mesh it is drawn with. Treat the visual layer as its own pass, and start with **enclosure
and layout** rather than materials: most of the "this is not a shop" feeling comes from
the room not being a room, not from the lack of textures.

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
