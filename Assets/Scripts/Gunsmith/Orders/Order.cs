using System;
using System.Collections.Generic;
using System.Text;
using Krofken.Ballistics;

namespace Gunsmith.Orders
{
    /// <summary>How well a delivered batch met its brief.</summary>
    public enum OrderOutcome
    {
        /// <summary>Every requirement met.</summary>
        Excellent = 0,
        /// <summary>Non-critical shortfalls only. Paid, with reservations.</summary>
        Acceptable = 1,
        /// <summary>Missed enough that the customer is unhappy.</summary>
        Poor = 2,
        /// <summary>A critical requirement failed. Somebody got hurt.</summary>
        Disaster = 3
    }

    /// <summary>A request from one of the townsfolk.</summary>
    [Serializable]
    public sealed class Order
    {
        public string Id;

        public string CustomerName;

        /// <summary>What they do. Shapes both the brief and the consequences.</summary>
        public string CustomerRole;

        /// <summary>
        /// The request in the customer's own words -- never in engineering terms.
        /// Translating this into physical requirements IS the game. The player is not
        /// told which numbers matter; they work it out.
        /// </summary>
        public string Brief;

        /// <summary>Which case the round must be built on.</summary>
        public string CaseId;

        /// <summary>How many rounds they need.</summary>
        public int Quantity;

        /// <summary>Payment on acceptable delivery.</summary>
        public int Payment;

        /// <summary>Days until it is due. One in the vertical slice.</summary>
        public int DaysToDeliver = 1;

        /// <summary>
        /// Distance the customer will actually be shooting at, m. The round is judged
        /// at this range, not at the muzzle -- a load that is perfect at three metres
        /// may have shed too much velocity by fifty.
        /// </summary>
        public double EvaluationRange = 10.0;

        /// <summary>
        /// What the round will be judged against. A bare gel block for most work; add
        /// a denim layer and a hollow point that plugs will fail.
        /// </summary>
        public TargetLayer[] EvaluationTarget;

        /// <summary>Everything the round has to satisfy.</summary>
        public List<OrderRequirement> Requirements = new List<OrderRequirement>();

        /// <summary>True if any requirement is critical -- worth flagging on the card
        /// so the player knows this one has teeth.</summary>
        public bool HasCriticalRequirements
        {
            get
            {
                for (int i = 0; i < Requirements.Count; i++)
                    if (Requirements[i].IsCritical) return true;
                return false;
            }
        }
    }

    /// <summary>Result of judging one requirement.</summary>
    public struct RequirementResult
    {
        public OrderRequirement Requirement;
        public bool Satisfied;
        public double Satisfaction;
        public string MeasuredText;
    }

    /// <summary>The verdict on a delivered batch.</summary>
    public sealed class OrderEvaluation
    {
        public Order Order;
        public ShotMeasurement Measurement;
        public readonly List<RequirementResult> Results = new List<RequirementResult>();

        public OrderOutcome Outcome;

        /// <summary>Mean satisfaction across requirements, 0..1.</summary>
        public double Score;

        /// <summary>True if a requirement marked critical was missed.</summary>
        public bool CriticalFailure;

        /// <summary>Coin actually paid.</summary>
        public int Payment;

        /// <summary>Change in standing with the town, positive or negative.</summary>
        public int ReputationChange;

        /// <summary>What the customer (or word of their fate) tells the player the
        /// next morning.</summary>
        public string Feedback;
    }

    /// <summary>
    /// Judges a delivered batch against its brief.
    ///
    /// The evaluation is completely deterministic and completely transparent: the
    /// same measurement always produces the same verdict, and every line of feedback
    /// traces to a specific requirement and a specific number. There is no roll.
    ///
    /// The player still cannot be certain in advance -- but only because they have to
    /// spend materials testing to learn what their design actually does, not because
    /// the game is hiding a die behind the curtain.
    /// </summary>
    public static class OrderEvaluator
    {
        public static OrderEvaluation Evaluate(Order order, in ShotMeasurement measurement)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));

            var evaluation = new OrderEvaluation
            {
                Order = order,
                Measurement = measurement
            };

            double total = 0.0;
            int satisfiedCount = 0;

            for (int i = 0; i < order.Requirements.Count; i++)
            {
                var requirement = order.Requirements[i];
                bool satisfied = requirement.IsSatisfiedBy(measurement);
                double satisfaction = requirement.Satisfaction(measurement);

                evaluation.Results.Add(new RequirementResult
                {
                    Requirement = requirement,
                    Satisfied = satisfied,
                    Satisfaction = satisfaction,
                    MeasuredText = requirement.FormatMeasured(measurement)
                });

                total += satisfaction;
                if (satisfied) satisfiedCount++;
                else if (requirement.IsCritical) evaluation.CriticalFailure = true;
            }

            int count = order.Requirements.Count;
            evaluation.Score = count > 0 ? total / count : 1.0;

            // ---- Outcome ---------------------------------------------------
            // A critical failure is not a low score, it is a different category.
            // Averaging it away would let a player miss the one requirement that
            // mattered and still be told they did fine.
            if (evaluation.CriticalFailure)
                evaluation.Outcome = OrderOutcome.Disaster;
            else if (satisfiedCount == count)
                evaluation.Outcome = OrderOutcome.Excellent;
            else if (evaluation.Score >= 0.75)
                evaluation.Outcome = OrderOutcome.Acceptable;
            else
                evaluation.Outcome = OrderOutcome.Poor;

            switch (evaluation.Outcome)
            {
                case OrderOutcome.Excellent:
                    evaluation.Payment = order.Payment;
                    evaluation.ReputationChange = 2;
                    break;
                case OrderOutcome.Acceptable:
                    evaluation.Payment = (int)Math.Round(order.Payment * 0.8);
                    evaluation.ReputationChange = 0;
                    break;
                case OrderOutcome.Poor:
                    evaluation.Payment = (int)Math.Round(order.Payment * 0.4);
                    evaluation.ReputationChange = -2;
                    break;
                case OrderOutcome.Disaster:
                    // They paid on delivery. What happened afterwards is the cost.
                    evaluation.Payment = order.Payment;
                    evaluation.ReputationChange = -6;
                    break;
            }

            evaluation.Feedback = BuildFeedback(order, evaluation);
            return evaluation;
        }

        /// <summary>
        /// Assembles the next-morning report.
        ///
        /// Deliberately leads with the CONSEQUENCE, not the number. The player finds
        /// out that the hunter did not come back before they find out that penetration
        /// was 4 cm short. The number is there so the lesson is actionable, but the
        /// story is what makes them care.
        /// </summary>
        private static string BuildFeedback(Order order, OrderEvaluation evaluation)
        {
            var text = new StringBuilder();

            switch (evaluation.Outcome)
            {
                case OrderOutcome.Excellent:
                    text.AppendLine($"{order.CustomerName} came back to thank you. It did exactly what they needed.");
                    break;
                case OrderOutcome.Acceptable:
                    text.AppendLine($"{order.CustomerName} came back. It worked, but not quite the way they hoped.");
                    break;
                case OrderOutcome.Poor:
                    text.AppendLine($"{order.CustomerName} came back unhappy. They will think twice before asking again.");
                    break;
                case OrderOutcome.Disaster:
                    text.AppendLine($"You have not seen {order.CustomerName} since the delivery.");
                    break;
            }

            // Consequences first.
            bool wroteConsequence = false;
            for (int i = 0; i < evaluation.Results.Count; i++)
            {
                var result = evaluation.Results[i];
                if (result.Satisfied) continue;
                if (string.IsNullOrEmpty(result.Requirement.FailureConsequence)) continue;

                if (!wroteConsequence) { text.AppendLine(); wroteConsequence = true; }
                text.AppendLine(result.Requirement.FailureConsequence);
            }

            // Then the readout, so the lesson is actionable.
            text.AppendLine();
            text.AppendLine("What your round actually did:");

            for (int i = 0; i < evaluation.Results.Count; i++)
            {
                var result = evaluation.Results[i];
                string mark = result.Satisfied ? "  ok " : "  no ";
                text.AppendLine($"{mark} {result.Requirement.Technical}   (measured {result.MeasuredText})");
            }

            return text.ToString();
        }
    }
}
