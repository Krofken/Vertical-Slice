using System.Collections.Generic;
using NUnit.Framework;
using Gunsmith.Economy;
using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Gunsmith.Range;
using Gunsmith.Workshop;
using Krofken.Ballistics;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The vertical slice, end to end.
    ///
    /// The most important assertions in this file are the CROSS tests: a round built
    /// for one brief must fail another. That is the whole design. If one load could
    /// satisfy every customer there would be no game, only a calculator, and no
    /// amount of physical realism underneath would fix it.
    /// </summary>
    public class VerticalSliceTests
    {
        private GunsmithGame _game;

        [SetUp]
        public void SetUp()
        {
            _game = new GunsmithGame { OrdersPerDay = 5 };
            _game.StartNewGame(seed: 1234);
        }

        /// <summary>Saves a design and stocks the workshop so it can be fired.</summary>
        private SavedDesign Prepare(string id, in CartridgeDesign design, int rounds = 40)
        {
            var saved = _game.SaveDesign(id, id, design);
            Assert.That(saved.IsValid, Is.True, $"{id} did not bake cleanly: {Describe(saved)}");

            // Give the workshop whatever this design needs, so material scarcity is
            // not what is under test here.
            var materials = design.Materials;
            _game.Inventory.AddMass(materials.CoreMaterialId, 1.0);
            if (!string.IsNullOrEmpty(materials.JacketMaterialId)) _game.Inventory.AddMass(materials.JacketMaterialId, 1.0);
            if (!string.IsNullOrEmpty(materials.CavityFillMaterialId)) _game.Inventory.AddMass(materials.CavityFillMaterialId, 1.0);
            _game.Inventory.AddMass(design.PropellantId, 1.0);
            _game.Inventory.AddCases(design.CaseId, rounds + 10);
            _game.Inventory.Primers += rounds + 10;

            var craft = _game.Workshop.Craft(saved, rounds);
            Assert.That(craft.Success, Is.True, craft.Message);

            return saved;
        }

        private static string Describe(SavedDesign saved)
        {
            if (saved?.Baked == null) return "no bake";
            var text = new System.Text.StringBuilder();
            foreach (var issue in saved.Baked.Issues) text.Append(issue).Append("; ");
            return text.ToString();
        }

        private OrderEvaluation Judge(Order order, SavedDesign design)
        {
            var measurement = _game.Range.Measure(design.Baked, order.EvaluationRange, order.EvaluationTarget);
            return OrderEvaluator.Evaluate(order, measurement);
        }

        // ------------------------------------------------------------------
        // Each brief is satisfiable by the right round
        // ------------------------------------------------------------------

        [Test]
        public void The_Hunter_Is_Satisfied_By_A_Heavy_NonExpanding_Round()
        {
            var design = Prepare("penetrator", ReferenceLoads.Penetrator());
            var evaluation = Judge(OrderCatalogue.Hunter(), design);

            Assert.That(evaluation.CriticalFailure, Is.False, evaluation.Feedback);
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Excellent), evaluation.Feedback);
        }

        [Test]
        public void The_Bodyguard_Is_Satisfied_By_An_Expanding_Round()
        {
            var design = Prepare("hollow", ReferenceLoads.HollowPoint());
            var evaluation = Judge(OrderCatalogue.Bodyguard(), design);

            Assert.That(evaluation.CriticalFailure, Is.False, evaluation.Feedback);
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Excellent), evaluation.Feedback);
        }

        [Test]
        public void The_Watch_Is_Satisfied_By_A_Hard_Core()
        {
            var design = Prepare("ap", ReferenceLoads.ArmourPiercing());
            var evaluation = Judge(OrderCatalogue.Watchman(), design);

            Assert.That(evaluation.CriticalFailure, Is.False, evaluation.Feedback);
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Excellent), evaluation.Feedback);
        }

        [Test]
        public void The_Granary_Keeper_Is_Satisfied_By_A_Reactive_Payload()
        {
            var design = Prepare("incendiary", ReferenceLoads.Incendiary());
            var evaluation = Judge(OrderCatalogue.Ratcatcher(), design);

            Assert.That(evaluation.CriticalFailure, Is.False, evaluation.Feedback);
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Excellent), evaluation.Feedback);
        }

        [Test]
        public void The_Bosun_Is_Satisfied_By_A_Brittle_Core()
        {
            var design = Prepare("frangible", ReferenceLoads.Frangible());
            var evaluation = Judge(OrderCatalogue.Sailor(), design);

            Assert.That(evaluation.CriticalFailure, Is.False, evaluation.Feedback);
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Excellent), evaluation.Feedback);
        }

        // ------------------------------------------------------------------
        // ...and fails the others. This is the design.
        // ------------------------------------------------------------------

        [Test]
        public void No_Single_Round_Satisfies_Every_Brief()
        {
            var loads = new Dictionary<string, SavedDesign>
            {
                { "penetrator", Prepare("penetrator", ReferenceLoads.Penetrator()) },
                { "hollow", Prepare("hollow", ReferenceLoads.HollowPoint()) },
                { "ap", Prepare("ap", ReferenceLoads.ArmourPiercing()) },
                { "incendiary", Prepare("incendiary", ReferenceLoads.Incendiary()) },
                { "frangible", Prepare("frangible", ReferenceLoads.Frangible()) }
            };

            var orders = OrderCatalogue.All();

            foreach (var pair in loads)
            {
                int satisfied = 0;
                foreach (var order in orders)
                    if (Judge(order, pair.Value).Outcome == OrderOutcome.Excellent) satisfied++;

                Assert.That(satisfied, Is.LessThan(orders.Count),
                    $"'{pair.Key}' satisfies every brief -- the orders do not actually conflict");
            }
        }

        [Test]
        public void The_Hunters_Round_Is_A_Disaster_For_The_Bodyguard()
        {
            // Built to drive through a boar's shoulder, so it goes straight through a
            // person and out the other side. This is the specific failure the
            // bodyguard warned about.
            var design = Prepare("penetrator", ReferenceLoads.Penetrator());
            var evaluation = Judge(OrderCatalogue.Bodyguard(), design);

            Assert.That(evaluation.CriticalFailure, Is.True);
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Disaster));
            Assert.That(evaluation.Measurement.Perforated, Is.True);
        }

        [Test]
        public void The_Bodyguards_Round_Fails_The_Hunter()
        {
            // Expands to nearly 1.5 calibres and stops in the first 25 cm, which is
            // exactly what the hunter cannot use.
            var design = Prepare("hollow", ReferenceLoads.HollowPoint());
            var evaluation = Judge(OrderCatalogue.Hunter(), design);

            Assert.That(evaluation.CriticalFailure, Is.True);
            Assert.That(evaluation.Measurement.PenetrationDepth, Is.LessThan(0.40));
        }

        [Test]
        public void A_Soft_Round_Cannot_Defeat_The_Watchs_Plate()
        {
            var design = Prepare("hollow", ReferenceLoads.HollowPoint());
            var evaluation = Judge(OrderCatalogue.Watchman(), design);

            Assert.That(evaluation.CriticalFailure, Is.True);
        }

        [Test]
        public void Thermite_Fails_The_Granary_Brief_Because_Tissue_Cannot_Initiate_It()
        {
            // A perfectly sensible-looking incendiary that does not work, because its
            // initiation threshold is 200 MPa and soft tissue supplies about 48. The
            // player has to discover this by testing.
            var thermite = ReferenceLoads.Incendiary();
            thermite.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal,
                CavityFillMaterialId = MaterialLibrary.Thermite
            };

            var design = Prepare("thermite", thermite);
            var evaluation = Judge(OrderCatalogue.Ratcatcher(), design);

            Assert.That(evaluation.Measurement.ReactiveEnergyReleased, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(evaluation.CriticalFailure, Is.True);
        }

        // ------------------------------------------------------------------
        // Economy and crafting
        // ------------------------------------------------------------------

        [Test]
        public void Crafting_Consumes_Materials_In_Proportion_To_The_Design()
        {
            var design = _game.SaveDesign("plain", "Plain", ReferenceLoads.Penetrator());

            double leadBefore = _game.Inventory.MassOf(MaterialLibrary.Lead);
            double powderBefore = _game.Inventory.MassOf(PropellantLibrary.SingleBase);
            int casesBefore = _game.Inventory.CasesOf(CartridgeCaseLibrary.NineMillimetre);

            var craft = _game.Workshop.Craft(design, 10);
            Assert.That(craft.Success, Is.True, craft.Message);

            double leadUsed = leadBefore - _game.Inventory.MassOf(MaterialLibrary.Lead);
            double powderUsed = powderBefore - _game.Inventory.MassOf(PropellantLibrary.SingleBase);

            // The lead consumed is the core mass the geometry solver integrated,
            // times ten. There is no second set of numbers.
            Assert.That(leadUsed, Is.EqualTo(design.Baked.Mass.CoreMass * 10).Within(1e-9));
            Assert.That(powderUsed, Is.EqualTo(design.Design.ChargeMass * 10).Within(1e-12));
            Assert.That(_game.Inventory.CasesOf(CartridgeCaseLibrary.NineMillimetre), Is.EqualTo(casesBefore - 10));
            Assert.That(_game.Workshop.RoundsOf(design.Id), Is.EqualTo(10));
        }

        [Test]
        public void Crafting_Without_Materials_Changes_Nothing()
        {
            // A partial consumption that then failed would quietly destroy stock.
            var exotic = ReferenceLoads.ArmourPiercing();
            var design = _game.SaveDesign("ap", "AP", exotic);

            double brassBefore = _game.Inventory.MassOf(MaterialLibrary.GildingMetal);
            int primersBefore = _game.Inventory.Primers;

            var craft = _game.Workshop.Craft(design, 10);

            Assert.That(craft.Success, Is.False, "should fail: no hardened steel in stock");
            Assert.That(_game.Inventory.MassOf(MaterialLibrary.GildingMetal), Is.EqualTo(brassBefore));
            Assert.That(_game.Inventory.Primers, Is.EqualTo(primersBefore));
            Assert.That(_game.Workshop.RoundsOf(design.Id), Is.Zero);
        }

        [Test]
        public void Buying_Materials_Costs_Money_And_Adds_Stock()
        {
            int fundsBefore = _game.Inventory.Funds;
            double before = _game.Inventory.MassOf(MaterialLibrary.HardenedSteel);

            bool bought = Merchant.Buy(_game.Inventory, MaterialLibrary.HardenedSteel, 0.05);

            Assert.That(bought, Is.True);
            Assert.That(_game.Inventory.MassOf(MaterialLibrary.HardenedSteel), Is.EqualTo(before + 0.05).Within(1e-12));
            Assert.That(_game.Inventory.Funds, Is.LessThan(fundsBefore));
        }

        [Test]
        public void You_Cannot_Buy_What_You_Cannot_Afford()
        {
            _game.Inventory.Funds = 5;
            bool bought = Merchant.Buy(_game.Inventory, MaterialLibrary.TungstenCarbide, 1.0);

            Assert.That(bought, Is.False);
            Assert.That(_game.Inventory.Funds, Is.EqualTo(5));
            Assert.That(_game.Inventory.MassOf(MaterialLibrary.TungstenCarbide), Is.Zero);
        }

        // ------------------------------------------------------------------
        // The range
        // ------------------------------------------------------------------

        [Test]
        public void Every_Test_Shot_Costs_A_Round_And_Is_Logged()
        {
            var design = Prepare("plain", ReferenceLoads.Penetrator(), rounds: 3);

            for (int i = 0; i < 3; i++)
            {
                bool fired = _game.Range.TryFire(design, 10.0,
                    TargetMediumLibrary.BareGelatinBlock(), "bare gel", _game.Day,
                    out var entry, out var failure);

                Assert.That(fired, Is.True, failure.ToString());
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.ShotNumber, Is.EqualTo(i + 1));
                Assert.That(entry.Measurement.MuzzleVelocity, Is.GreaterThan(0.0));
            }

            Assert.That(_game.Workshop.RoundsOf(design.Id), Is.Zero);
            Assert.That(_game.Notebook.Count, Is.EqualTo(3));

            // Out of ammunition: cannot keep testing forever.
            bool again = _game.Range.TryFire(design, 10.0,
                TargetMediumLibrary.BareGelatinBlock(), "bare gel", _game.Day, out _, out var reason);

            Assert.That(again, Is.False);
            Assert.That(reason, Is.EqualTo(FireFailure.NoAmmunition));
        }

        [Test]
        public void The_Notebook_Records_The_Full_Recipe_As_Fired()
        {
            var design = Prepare("hollow", ReferenceLoads.HollowPoint(), rounds: 2);

            _game.Range.TryFire(design, 10.0, TargetMediumLibrary.BareGelatinBlock(),
                "bare gel", _game.Day, out var entry, out _);

            Assert.That(entry.CoreMaterial, Is.EqualTo("Pure Lead"));
            Assert.That(entry.JacketMaterial, Is.EqualTo("Copper"));
            Assert.That(entry.ChargeGrains, Is.EqualTo(5.3).Within(0.01));
            Assert.That(entry.CavityMouthMm, Is.GreaterThan(0.0));
            Assert.That(entry.BulletGrains, Is.GreaterThan(50.0));
        }

        [Test]
        public void Clothing_Changes_The_Result_Of_The_Same_Round()
        {
            // The same load, the same range, a different target -- and a completely
            // different outcome. The player has to test against what the customer
            // will actually meet.
            var design = Prepare("hollow", ReferenceLoads.HollowPoint(), rounds: 4);

            _game.Range.TryFire(design, 7.0, TargetMediumLibrary.BareGelatinBlock(),
                "bare gel", _game.Day, out var bare, out _);
            _game.Range.TryFire(design, 7.0, TargetMediumLibrary.ClothedGelatinBlock(),
                "denim over gel", _game.Day, out var clothed, out _);

            Assert.That(clothed.Measurement.CavityPlugged, Is.True);
            Assert.That(clothed.Measurement.ExpansionRatio, Is.LessThan(bare.Measurement.ExpansionRatio));
            Assert.That(clothed.Measurement.PenetrationDepth, Is.GreaterThan(bare.Measurement.PenetrationDepth));
        }

        [Test]
        public void The_Notebook_Reports_What_Changed_Between_Two_Shots()
        {
            var light = ReferenceLoads.Penetrator();
            light.ChargeMass = Units.GrainsToKilograms(4.0);

            var a = Prepare("light", light, rounds: 2);
            var b = Prepare("heavy", ReferenceLoads.Penetrator(), rounds: 2);

            _game.Range.TryFire(a, 10.0, TargetMediumLibrary.BareGelatinBlock(), "gel", 1, out var first, out _);
            _game.Range.TryFire(b, 10.0, TargetMediumLibrary.BareGelatinBlock(), "gel", 1, out var second, out _);

            string comparison = LabNotebook.Compare(first, second);

            Assert.That(comparison, Does.Contain("charge"));
            Assert.That(comparison, Does.Contain("muzzle velocity"));
        }

        // ------------------------------------------------------------------
        // The full loop
        // ------------------------------------------------------------------

        [Test]
        public void A_Complete_Day_Cycle_Runs_End_To_End()
        {
            Assert.That(_game.Phase, Is.EqualTo(DayPhase.Day));
            Assert.That(_game.Board.Count, Is.GreaterThan(0), "no orders posted");

            // DAY: take the bodyguard's job.
            Order chosen = null;
            foreach (var order in _game.Board)
                if (order.Id == "bodyguard_crowd") chosen = order;
            Assert.That(chosen, Is.Not.Null, "expected the bodyguard on the board with this seed");

            var accepted = _game.AcceptOrder(chosen);
            Assert.That(accepted, Is.Not.Null);
            Assert.That(_game.Board, Has.No.Member(chosen), "taken orders leave the board");

            // NIGHT: design, load, test.
            _game.AdvancePhase();
            Assert.That(_game.Phase, Is.EqualTo(DayPhase.Night));

            var design = Prepare("hollow", ReferenceLoads.HollowPoint(), rounds: chosen.Quantity + 2);

            _game.Range.TryFire(design, chosen.EvaluationRange, chosen.EvaluationTarget,
                "customer's target", _game.Day, out var proof, out _);
            Assert.That(proof.Measurement.Perforated, Is.False, "the player would see this before delivering");

            Assert.That(_game.SubmitOrder(accepted, design, out string error), Is.True, error);
            Assert.That(accepted.Submitted, Is.True);
            Assert.That(_game.Workshop.RoundsOf(design.Id), Is.EqualTo(1), "delivery took the batch out of stock");

            // DAWN: word comes back.
            int fundsBefore = _game.Inventory.Funds;
            _game.AdvancePhase();

            Assert.That(_game.Phase, Is.EqualTo(DayPhase.Dawn));
            Assert.That(accepted.Reported, Is.True);
            Assert.That(accepted.Evaluation, Is.Not.Null);
            Assert.That(accepted.Evaluation.Outcome, Is.EqualTo(OrderOutcome.Excellent), accepted.Evaluation.Feedback);
            Assert.That(_game.Inventory.Funds, Is.EqualTo(fundsBefore + accepted.Evaluation.Payment));
            Assert.That(_game.Reputation, Is.GreaterThan(0));

            // Next morning.
            _game.AdvancePhase();
            Assert.That(_game.Phase, Is.EqualTo(DayPhase.Day));
            Assert.That(_game.Day, Is.EqualTo(2));
            Assert.That(_game.Board.Count, Is.GreaterThan(0));
        }

        [Test]
        public void A_Bad_Delivery_Costs_Reputation_And_Explains_Why()
        {
            Order order = null;
            foreach (var candidate in _game.Board)
                if (candidate.Id == "bodyguard_crowd") order = candidate;
            Assert.That(order, Is.Not.Null);

            var accepted = _game.AcceptOrder(order);
            _game.AdvancePhase();

            // Deliver the deep-penetration round to the man who works in crowds.
            var design = Prepare("penetrator", ReferenceLoads.Penetrator(), rounds: order.Quantity);
            Assert.That(_game.SubmitOrder(accepted, design, out string error), Is.True, error);

            _game.AdvancePhase();

            Assert.That(accepted.Evaluation.Outcome, Is.EqualTo(OrderOutcome.Disaster));
            Assert.That(_game.Reputation, Is.LessThan(0));

            // The feedback has to name the consequence, not just fail a checkbox.
            Assert.That(accepted.Evaluation.Feedback, Does.Contain("apprentice"));
            Assert.That(accepted.Evaluation.Feedback, Does.Contain("Passes through"));
        }

        [Test]
        public void An_Undelivered_Order_Expires_And_Costs_Standing()
        {
            var order = _game.Board[0];
            var accepted = _game.AcceptOrder(order);

            _game.AdvancePhase();   // Night
            _game.AdvancePhase();   // Dawn
            _game.AdvancePhase();   // Day 2 -- past the deadline

            Assert.That(accepted.Reported, Is.True);
            Assert.That(accepted.Submitted, Is.False);
            Assert.That(_game.Reputation, Is.LessThan(0));
        }

        [Test]
        public void An_Unsafe_Design_Cannot_Be_Loaded_Or_Delivered()
        {
            var overcharged = ReferenceLoads.Penetrator();
            overcharged.ChargeMass = Units.GrainsToKilograms(10.0);

            var design = _game.SaveDesign("hot", "Hot", overcharged);

            Assert.That(design.IsValid, Is.False, "an overcharge must not validate");

            var craft = _game.Workshop.Craft(design, 5);
            Assert.That(craft.Success, Is.False);
            Assert.That(craft.Message, Does.Contain("safely"));
        }
    }
}
