# Gunsmith — vertical slice mechanics

Game logic for the slice. Depends on `com.krofken.ballistics` for all physics.

**No scene is included, by request.** These are the mechanics; the scene is yours.

---

## Wiring a scene

Minimum viable: **one GameObject with `GunsmithGameBehaviour`.** Nothing else is
required. The order board, the workbench and the range all run whether or not
anything is drawn — the range measures shots analytically, so you can play a whole
day through code before a single mesh exists.

Optional second component, `ProjectileSimulator` (from the ballistics package), if
you want to *see* rounds fly at the range. Assign it to the behaviour's
`Projectile Simulator` field. It integrates trajectories at its own step and sweeps
raycasts for hits — deliberately independent of PhysX, which cannot resolve an
800 m/s projectile at a 50 Hz tick.

Everything hangs off `GunsmithGameBehaviour.Game`:

```csharp
var game = FindFirstObjectByType<GunsmithGameBehaviour>().Game;

game.Board            // orders posted this morning
game.Accepted         // orders taken on
game.Inventory        // materials, cases, primers, coin
game.Designs          // saved cartridge designs
game.Workshop         // loading bench and finished-round stock
game.Range            // the yard
game.Notebook         // every shot ever fired
```

Bind UI to the events rather than polling: `PhaseChanged`, `BoardPosted`,
`OrderAccepted`, `OrderSubmitted`, `DeliveryReported`, `OrderMissed`, plus
`Inventory.Changed`, `Workshop.StockChanged` and `Notebook.EntryAdded`.

---

## The loop

| Phase | What the player does |
|---|---|
| **Day** | Orders are posted. Take any, all, or none. |
| **Night** | Buy materials, design and load rounds, test them in the yard. |
| **Dawn** | Batches go out. Word comes back about what they did. |

`game.AdvancePhase()` moves it on. Deliveries are submitted during Night and
resolved at Dawn — the player never learns whether a round worked at the moment they
hand it over.

---

## A day in code

```csharp
// DAY — take a job
var order = game.Board[0];
var accepted = game.AcceptOrder(order);
game.AdvancePhase();                                   // -> Night

// NIGHT — buy what you need
Merchant.Buy(game.Inventory, MaterialLibrary.HardenedSteel, 0.05);

// design a round
var design = new CartridgeDesign { /* geometry, materials, powder */ };
var saved = game.SaveDesign("ap_mk1", "Steel Core Mk1", design);
if (!saved.IsValid) { /* saved.Baked.Issues says exactly what is wrong */ }

// load it
var bill = game.Workshop.BuildBill(saved, 20);         // what it will cost
game.Workshop.Craft(saved, 20);

// prove it before you ship it
game.Range.TryFire(saved, order.EvaluationRange, order.EvaluationTarget,
                   "customer's target", game.Day, out var entry, out var why);
// entry.Measurement holds every number an instrument could read

game.SubmitOrder(accepted, saved, out string error);
game.AdvancePhase();                                   // -> Dawn

// DAWN — find out what happened
Debug.Log(accepted.Evaluation.Feedback);
```

---

## Design rules worth keeping

**Orders may only be written against measured quantities.** Everything in
`MeasuredQuantity` is something the range can read off an instrument. There is no
"stopping power" because no instrument reports one. If you want a new kind of brief,
add the measurement first.

**Briefs are in the customer's words, never in engineering terms.** A house guard
says he works in crowds; he does not say "sub-30 cm penetration, no perforation".
Translating is the game. `OrderRequirement.CustomerWords` is the card;
`OrderRequirement.Technical` is the range readout.

**Consequences, then numbers.** Delivery feedback leads with what happened to the
customer and only then shows the measurements. `FailureConsequence` on each
requirement is what makes a failure land.

**Critical requirements are the ones where someone gets hurt.** They are not "worth
more points" — missing one is a `Disaster` outcome regardless of how well everything
else scored, because averaging that away would tell a player they did fine when
somebody died.

**No hidden rolls, anywhere.** The same design always produces the same result. The
player's uncertainty comes from not having tested yet, and testing costs rounds.

---

## Adding an order

Add a method to `OrderCatalogue` and list it in `All()`. Check it conflicts with at
least one existing brief — `No_Single_Round_Satisfies_Every_Brief` in the tests
enforces that no load satisfies everything, which is the property that makes the game
a game rather than a calculator.

---

## Tests

`Gunsmith.Tests` (EditMode, 22 tests) covers the full loop, the economy, the range,
and the central claim that each brief needs its own round. `ReferenceLoads` holds
worked solutions to all five briefs — kept in the test assembly on purpose, since
shipping them as presets would be shipping the answers.
