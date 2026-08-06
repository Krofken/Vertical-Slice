using System;
using System.Collections.Generic;
using Gunsmith.Economy;
using Gunsmith.Orders;
using Gunsmith.Range;
using Gunsmith.Workshop;
using Krofken.Ballistics;

namespace Gunsmith.GameLoop
{
    /// <summary>Where in the cycle the player is.</summary>
    public enum DayPhase
    {
        /// <summary>Townsfolk come to the shop. Orders are posted and chosen.</summary>
        Day = 0,

        /// <summary>The shop is shut. Buy, design, load, and test in the yard.</summary>
        Night = 1,

        /// <summary>Deliveries go out and word comes back about the last batch.</summary>
        Dawn = 2
    }

    /// <summary>An order the player has taken on.</summary>
    public sealed class AcceptedOrder
    {
        public Order Order;
        public int DayAccepted;

        /// <summary>Design handed over, once submitted.</summary>
        public string SubmittedDesignId;

        /// <summary>True once rounds have been handed over and are awaiting word.</summary>
        public bool Submitted;

        /// <summary>The verdict, available from the Dawn after submission.</summary>
        public OrderEvaluation Evaluation;

        /// <summary>True once the verdict has been reported to the player.</summary>
        public bool Reported;

        /// <summary>
        /// The morning by which the batch must already have gone out.
        ///
        /// An order taken on day N with a one-day deadline must be delivered on the
        /// night of day N -- so it is overdue the moment day N+1 begins. Treating the
        /// deadline as inclusive would quietly hand the player a second night, which
        /// takes all the pressure out of choosing how many orders to accept.
        /// </summary>
        public int DueDay => DayAccepted + Order.DaysToDeliver;
    }

    /// <summary>
    /// The whole vertical slice, as a plain C# object.
    ///
    /// Deliberately NOT a MonoBehaviour. All the state lives here, no part of it
    /// depends on a scene existing, and it can be driven from a test without ever
    /// entering play mode. The scene layer is a thin shell over this.
    ///
    /// THE LOOP
    ///   Day    orders are posted; the player takes as many or as few as they like
    ///   Night  buy materials, design and load rounds, test them in the yard
    ///   Dawn   submitted batches go out; word comes back about the PREVIOUS batch
    ///
    /// The delayed feedback is the point. Nothing tells the player their round was
    /// wrong at the moment they hand it over -- they find out the next morning, from
    /// whoever used it, or from the fact that nobody comes back.
    /// </summary>
    public sealed class GunsmithGame
    {
        // ---- Systems ------------------------------------------------------
        public readonly WorkshopInventory Inventory = new WorkshopInventory();
        public readonly DesignLibrary Designs = new DesignLibrary();
        public readonly LabNotebook Notebook = new LabNotebook();
        public readonly AmmunitionWorkshop Workshop;
        public readonly TestRange Range;

        // ---- State ---------------------------------------------------------
        public int Day { get; private set; } = 1;
        public DayPhase Phase { get; private set; } = DayPhase.Day;

        /// <summary>Standing with the town. Falls hard when somebody gets hurt.</summary>
        public int Reputation { get; private set; }

        /// <summary>Orders posted today that have not been taken.</summary>
        public readonly List<Order> Board = new List<Order>();

        /// <summary>Orders the player has taken on.</summary>
        public readonly List<AcceptedOrder> Accepted = new List<AcceptedOrder>();

        /// <summary>Barrel every design is baked and tested against in the slice.</summary>
        public Barrel ReferenceBarrel = BarrelLibrary.ServicePistol9mm;

        /// <summary>Seed for which customers turn up. Fixed per save.</summary>
        public int Seed;

        /// <summary>How many orders are posted each day.</summary>
        public int OrdersPerDay = 3;

        // ---- Events ---------------------------------------------------------
        public event Action<DayPhase> PhaseChanged;
        public event Action<IReadOnlyList<Order>> BoardPosted;
        public event Action<AcceptedOrder> OrderAccepted;
        public event Action<AcceptedOrder> OrderSubmitted;

        /// <summary>Raised at Dawn, once per batch that has come back with news.</summary>
        public event Action<AcceptedOrder> DeliveryReported;

        /// <summary>Raised when an order runs past its deadline undelivered.</summary>
        public event Action<AcceptedOrder> OrderMissed;

        public GunsmithGame()
        {
            Workshop = new AmmunitionWorkshop(Inventory);
            Range = new TestRange(Workshop, Notebook);
            Range.Barrel = ReferenceBarrel;
        }

        /// <summary>Sets up a new run and posts the first day's orders.</summary>
        public void StartNewGame(int seed = 0, bool grantStartingStock = true)
        {
            Seed = seed;
            Day = 1;
            Phase = DayPhase.Day;
            Reputation = 0;
            Board.Clear();
            Accepted.Clear();

            if (grantStartingStock) GrantStartingStock();

            PostBoard();
            PhaseChanged?.Invoke(Phase);
        }

        /// <summary>
        /// Enough to get started and not a coin more.
        ///
        /// The starting stock deliberately covers plain jacketed lead only. Everything
        /// exotic -- hardened steel, tungsten, reactive fillers -- has to be bought,
        /// which means the first time a brief needs one the player has to decide
        /// whether the order is worth the outlay.
        /// </summary>
        private void GrantStartingStock()
        {
            Inventory.Funds = 200;

            Inventory.AddMass(MaterialLibrary.Lead, 0.60);           // ~90 plain cores
            Inventory.AddMass(MaterialLibrary.GildingMetal, 0.12);   // ~100 jackets
            Inventory.AddMass(MaterialLibrary.Copper, 0.06);
            Inventory.AddMass(PropellantLibrary.SingleBase, 0.045);  // ~125 charges

            Inventory.AddCases(CartridgeCaseLibrary.NineMillimetre, 120);
            Inventory.Primers = 120;
            Inventory.NotifyChanged();
        }

        // ------------------------------------------------------------------
        // Phase machine
        // ------------------------------------------------------------------

        /// <summary>Moves to the next phase, running whatever that transition owes.</summary>
        public void AdvancePhase()
        {
            switch (Phase)
            {
                case DayPhase.Day:
                    // The board closes. Anything not taken is gone -- the townsfolk
                    // went elsewhere.
                    Board.Clear();
                    Phase = DayPhase.Night;
                    break;

                case DayPhase.Night:
                    Phase = DayPhase.Dawn;
                    ResolveDeliveries();
                    break;

                case DayPhase.Dawn:
                    Day++;
                    Phase = DayPhase.Day;
                    ExpireOverdueOrders();
                    PostBoard();
                    break;
            }

            PhaseChanged?.Invoke(Phase);
        }

        private void PostBoard()
        {
            Board.Clear();

            var posted = OrderCatalogue.ForDay(Day, OrdersPerDay, Seed);

            // Do not post something the player is already working on.
            for (int i = 0; i < posted.Count; i++)
            {
                if (IsActive(posted[i].Id)) continue;
                Board.Add(posted[i]);
            }

            BoardPosted?.Invoke(Board);
        }

        private bool IsActive(string orderId)
        {
            for (int i = 0; i < Accepted.Count; i++)
                if (Accepted[i].Order.Id == orderId && !Accepted[i].Reported) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Player actions
        // ------------------------------------------------------------------

        /// <summary>
        /// Takes an order. The player may take any number of the day's orders,
        /// including all of them or none -- overcommitting is a real and available
        /// mistake.
        /// </summary>
        public AcceptedOrder AcceptOrder(Order order)
        {
            if (order == null || Phase != DayPhase.Day) return null;
            if (!Board.Remove(order)) return null;

            var accepted = new AcceptedOrder { Order = order, DayAccepted = Day };
            Accepted.Add(accepted);

            OrderAccepted?.Invoke(accepted);
            return accepted;
        }

        /// <summary>
        /// Hands over a batch. Consumes the rounds immediately; the verdict does not
        /// arrive until Dawn.
        /// </summary>
        public bool SubmitOrder(AcceptedOrder accepted, SavedDesign design, out string error)
        {
            error = null;

            if (accepted == null || design == null) { error = "Nothing to deliver."; return false; }
            if (accepted.Submitted) { error = "Already delivered."; return false; }
            if (Phase != DayPhase.Night) { error = "The shop is not loading right now."; return false; }

            if (design.Design.CaseId != accepted.Order.CaseId)
            {
                error = $"{accepted.Order.CustomerName} needs {accepted.Order.CaseId}; this is {design.Design.CaseId}.";
                return false;
            }

            if (!design.IsValid) { error = "That design is not safe to fire."; return false; }

            int required = accepted.Order.Quantity;
            if (Workshop.RoundsOf(design.Id) < required)
            {
                error = $"Need {required} rounds, have {Workshop.RoundsOf(design.Id)}.";
                return false;
            }

            if (!Workshop.TryConsumeRounds(design.Id, required))
            {
                error = "Could not take the rounds from stock.";
                return false;
            }

            accepted.SubmittedDesignId = design.Id;
            accepted.Submitted = true;

            OrderSubmitted?.Invoke(accepted);
            return true;
        }

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Works out what every delivered batch actually did, and reports it.
        ///
        /// The evaluation runs the delivered design against the order's OWN range and
        /// target -- not against whatever the player happened to test with. A round
        /// proven at seven metres into bare gel may behave differently at forty
        /// metres through a shoulder, and finding that out is the lesson.
        /// </summary>
        private void ResolveDeliveries()
        {
            for (int i = 0; i < Accepted.Count; i++)
            {
                var accepted = Accepted[i];
                if (!accepted.Submitted || accepted.Reported) continue;

                var design = Designs.Get(accepted.SubmittedDesignId);
                if (design?.Baked == null) continue;

                var measurement = Range.Measure(
                    design.Baked,
                    accepted.Order.EvaluationRange,
                    accepted.Order.EvaluationTarget);

                accepted.Evaluation = OrderEvaluator.Evaluate(accepted.Order, measurement);
                accepted.Reported = true;

                Inventory.Earn(accepted.Evaluation.Payment);
                Reputation += accepted.Evaluation.ReputationChange;

                DeliveryReported?.Invoke(accepted);
            }
        }

        /// <summary>Orders that went past their deadline without a delivery.</summary>
        private void ExpireOverdueOrders()
        {
            for (int i = 0; i < Accepted.Count; i++)
            {
                var accepted = Accepted[i];
                if (accepted.Submitted || accepted.Reported) continue;
                if (Day < accepted.DueDay) continue;

                accepted.Reported = true;
                Reputation -= 3;

                OrderMissed?.Invoke(accepted);
            }
        }

        /// <summary>Orders still waiting on the player.</summary>
        public IEnumerable<AcceptedOrder> Outstanding
        {
            get
            {
                for (int i = 0; i < Accepted.Count; i++)
                    if (!Accepted[i].Reported) yield return Accepted[i];
            }
        }

        /// <summary>Convenience: save a design against the reference barrel.</summary>
        public SavedDesign SaveDesign(string id, string name, in CartridgeDesign design)
            => Designs.Save(id, name, design, ReferenceBarrel, Day);
    }
}
