using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Krofken.Ballistics;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// Delivery: a whole day, from a card on the board to a note in the morning.
    ///
    /// Two rules are load-bearing here and both are asserted directly.
    ///
    /// You never learn whether a round worked at the moment you hand it over. Handing a
    /// box across the counter and being told immediately would turn the customer into a
    /// test instrument and delete the entire reason the range exists.
    ///
    /// And the note leads with what happened to the PERSON. Missing the one requirement
    /// that mattered is a disaster whatever the average says, because averaging it away
    /// would tell a player they did fine when somebody died.
    /// </summary>
    public class DeliveryTests
    {
        private GameObject _host;
        private LoadingPress _press;
        private GunsmithGame _game;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Workshop");

            _press = _host.AddComponent<LoadingPress>();
            _press.Mill = _host.AddComponent<PropellantMill>();
            _press.CoreBench = _host.AddComponent<LatheStation>();
            _press.Balance = _host.AddComponent<PowderBalance>();
            _press.Die = _host.AddComponent<SeatingStop>();

            _press.CoreBench.Geometry = ProjectileGeometry.Default9mmFmj;
            _press.CoreBench.Rebuild();
            _press.Balance.SettingGrains = 5.5;
            _press.Balance.Trickle(5.5);
            _press.Die.Depth = 0.0030;

            _game = new GunsmithGame();
            _game.StartNewGame();

            _game.Inventory.Primers += 500;
            _game.Inventory.AddCases(CartridgeCaseLibrary.NineMillimetre, 500);
            foreach (var material in MaterialLibrary.All) _game.Inventory.AddMass(material.Id, 5.0);
            foreach (var propellant in PropellantLibrary.All) _game.Inventory.AddMass(propellant.Id, 5.0);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        /// <summary>Takes a job, presses enough rounds for it, and hands them over.</summary>
        private AcceptedOrder TakeAndDeliver(out string error)
        {
            var order = _game.Board[0];
            var accepted = _game.AcceptOrder(order);

            _game.AdvancePhase();                       // -> Night

            _press.BatchSize = order.Quantity;
            var crafted = _press.PressBatch(_game, "delivery", "Delivery");
            Assert.That(crafted.Success, Is.True, crafted.Message);

            var design = _game.Designs.Get("delivery");
            _game.SubmitOrder(accepted, design, out error);

            return accepted;
        }

        // ==================================================================

        /// <summary>
        /// THE RULE. Handing the batch over tells you nothing. You sleep first.
        /// </summary>
        [Test]
        public void Handing_The_Batch_Over_Tells_You_Nothing()
        {
            var accepted = TakeAndDeliver(out string error);

            Assert.That(accepted.Submitted, Is.True, error);
            Assert.That(accepted.Reported, Is.False, "the customer reported back at the counter");
            Assert.That(accepted.Evaluation, Is.Null, "the outcome was known before dawn");
        }

        [Test]
        public void The_Answer_Arrives_At_Dawn()
        {
            var accepted = TakeAndDeliver(out _);

            _game.AdvancePhase();                       // -> Dawn

            Assert.That(accepted.Reported, Is.True, "nothing came back in the morning");
            Assert.That(accepted.Evaluation, Is.Not.Null);
            Assert.That(accepted.Evaluation.Feedback, Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// The note has to start with the person, not the measurement. The first line
        /// names the customer; the figures come afterwards, so the lesson is actionable
        /// but the story lands first.
        /// </summary>
        [Test]
        public void The_Note_Leads_With_The_Person_Not_The_Number()
        {
            var accepted = TakeAndDeliver(out _);
            _game.AdvancePhase();

            string feedback = accepted.Evaluation.Feedback;
            string firstLine = feedback.Split('\n')[0];

            Assert.That(firstLine, Does.Contain(accepted.Order.CustomerName),
                "the note opens with a measurement instead of the customer");

            foreach (char c in firstLine)
                Assert.That(char.IsDigit(c), Is.False, $"the opening line carries a number: '{firstLine}'");

            // The readout is still there, further down, so the player can act on it.
            Assert.That(feedback, Does.Contain("What your round actually did"));
        }

        /// <summary>
        /// Missing a critical requirement is a different CATEGORY of outcome, not a
        /// lower score. Averaging it away would tell the player they did fine when
        /// somebody got hurt.
        /// </summary>
        [Test]
        public void Missing_A_Critical_Requirement_Is_A_Disaster_However_Well_The_Rest_Scored()
        {
            // The bodyguard works in crowds: his round must not come out the far side.
            // Handing him a deep penetrator is the exact mistake the rule exists for.
            var evaluation = JudgeWrongLoad();

            Assert.That(evaluation.CriticalFailure, Is.True,
                "a round that goes straight through must fail the crowd requirement");
            Assert.That(evaluation.Outcome, Is.EqualTo(OrderOutcome.Disaster),
                "a critical miss is a category, not a low score");
            Assert.That(evaluation.ReputationChange, Is.LessThan(0));
            Assert.That(evaluation.Feedback, Does.Not.Contain("came back to thank you"));
        }

        /// <summary>A disaster still gets paid — they paid on delivery, and what
        /// happened afterwards is the cost. The reputation is where it lands.</summary>
        [Test]
        public void A_Disaster_Was_Still_Paid_For_At_The_Counter()
        {
            var order = OrderCatalogue.Bodyguard();
            var evaluation = JudgeWrongLoad();

            Assert.That(evaluation.Payment, Is.EqualTo(order.Payment),
                "the money changed hands when the box did");
            Assert.That(evaluation.ReputationChange, Is.LessThanOrEqualTo(-5),
                "the cost of a disaster is that nobody trusts you");
        }

        /// <summary>Fires a deep penetrator at the bodyguard's job — the wrong round for
        /// a man who works in crowds.</summary>
        private static OrderEvaluation JudgeWrongLoad()
        {
            var order = OrderCatalogue.Bodyguard();

            var baked = CartridgeBaker.Bake(ReferenceLoads.Penetrator(), BarrelLibrary.ServicePistol9mm);
            Assert.That(baked.IsValid, Is.True, string.Join("; ", baked.Issues));

            var terminal = TerminalBallisticsSolver.Solve(
                baked.Terminal, order.EvaluationTarget, baked.MuzzleVelocity);

            var measurement = ShotMeasurement.From(
                baked, terminal, order.EvaluationRange, baked.MuzzleVelocity, 0.0, 0.03);

            return OrderEvaluator.Evaluate(order, measurement);
        }

        /// <summary>The morning's notes are readable as objects, one per delivery.</summary>
        [Test]
        public void Every_Delivery_Gets_A_Note_In_The_Morning()
        {
            var accepted = TakeAndDeliver(out _);
            _game.AdvancePhase();

            var view = _host.AddComponent<DeliveryReportView>();
            view.Show(_game);

            Assert.That(view.Notes.Count, Is.EqualTo(1));
            Assert.That(view.Notes[0].name, Does.Contain(accepted.Order.CustomerName));
        }

        /// <summary>Delivering nothing is its own outcome, and it costs you.</summary>
        [Test]
        public void A_Job_You_Never_Delivered_Still_Comes_Back_On_You()
        {
            var order = _game.Board[0];
            var accepted = _game.AcceptOrder(order);

            // Run the clock forward until the job is past due. Orders are not all due
            // the same morning, so a single Day/Night/Dawn cycle is not enough.
            for (int i = 0; i < 9 && !accepted.Reported; i++)
                _game.AdvancePhase();

            Assert.That(accepted.Submitted, Is.False);
            Assert.That(accepted.Reported, Is.True, "an unfilled job was quietly forgotten");
            Assert.That(_game.Reputation, Is.LessThan(0));
        }
    }
}
