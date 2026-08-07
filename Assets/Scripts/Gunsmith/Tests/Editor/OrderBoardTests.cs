using Gunsmith.GameLoop;
using Gunsmith.Orders;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// The order board.
    ///
    /// The load-bearing test here is that a card never leaks the technical spec. A house
    /// guard says he works in crowds; he does not say "sub-30 cm penetration". If the
    /// board ever prints the requirement the range measures, the player stops having to
    /// translate a brief into a load — and translating IS the game.
    /// </summary>
    public class OrderBoardTests
    {
        private GameObject _host;
        private OrderBoardView _board;
        private GunsmithGame _game;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Board");
            _board = _host.AddComponent<OrderBoardView>();
            _game = new GunsmithGame();
            _game.StartNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        [Test]
        public void The_Board_Pins_Up_Every_Job_That_Walked_In()
        {
            Assert.That(_game.Board.Count, Is.GreaterThan(0), "no orders were posted");

            _board.Show(_game);

            Assert.That(_board.Cards.Count, Is.EqualTo(_game.Board.Count));
        }

        /// <summary>
        /// THE RULE. Cards carry what the customer said, never what the range measures.
        /// </summary>
        [Test]
        public void A_Card_Never_Prints_The_Technical_Spec()
        {
            foreach (var order in _game.Board)
            {
                // Compare on whitespace-flattened text. The card wraps the brief to a
                // fixed column, so a raw substring check fails on wording that is
                // present and correct.
                //
                // Flattening matters far more for the NEGATIVE assertion below: a
                // leaked technical spec would be wrapped too, so an unflattened
                // Does.Not.Contain would sail past the very leak this test exists to
                // catch. It was passing by accident, not by working.
                string card = Flatten(OrderBoardView.Compose(order, taken: false));

                Assert.That(card, Does.Contain(Flatten(order.CustomerName)));
                Assert.That(card, Does.Contain(Flatten(order.Brief)));

                foreach (var requirement in order.Requirements)
                {
                    if (!string.IsNullOrEmpty(requirement.CustomerWords))
                        Assert.That(card, Does.Contain(Flatten(requirement.CustomerWords)),
                            "the customer's own words must be on the card");

                    string technical = requirement.Technical;
                    if (string.IsNullOrEmpty(technical)) continue;

                    Assert.That(card, Does.Not.Contain(Flatten(technical)),
                        $"the card leaked the technical spec '{technical}' for {order.CustomerName}");
                }
            }
        }

        /// <summary>Collapses every run of whitespace to a single space, so wrapped
        /// display text can be compared against the source wording.</summary>
        private static string Flatten(string text)
            => string.IsNullOrEmpty(text)
                ? text
                : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        /// <summary>A card says what a customer would say: how many, what they pay, and
        /// nothing about how to build it.</summary>
        [Test]
        public void A_Card_Carries_Only_What_A_Customer_Would_Say()
        {
            var order = _game.Board[0];
            string card = OrderBoardView.Compose(order, taken: false);

            Assert.That(card, Does.Contain(order.Quantity.ToString()));
            Assert.That(card, Does.Contain(order.Payment.ToString()));

            foreach (string banned in new[] { "MeasuredQuantity", "PenetrationDepth", "ExpansionRatio", "m/s", "MPa" })
                Assert.That(card, Does.Not.Contain(banned), $"the card leaked '{banned}'");
        }

        /// <summary>Taking a job moves its card off the board and onto the taken row, so
        /// the player can see at a glance what they have committed to.</summary>
        [Test]
        public void Taking_A_Job_Moves_Its_Card_To_The_Taken_Row()
        {
            var order = _game.Board[0];
            _game.AcceptOrder(order);

            _board.Show(_game);

            bool foundTaken = false;
            foreach (var card in _board.Cards)
            {
                if (!card.name.StartsWith("Taken")) continue;
                foundTaken = true;
                Assert.That(card.transform.localPosition.y, Is.LessThan(0f), "taken cards hang below the board");
            }

            Assert.That(foundTaken, Is.True, "the accepted job was not shown as taken");
        }

        /// <summary>Redrawing must not pile cards up — the board is rebuilt, not
        /// appended to.</summary>
        [Test]
        public void Redrawing_The_Board_Does_Not_Duplicate_Cards()
        {
            _board.Show(_game);
            int first = _board.Cards.Count;

            _board.Show(_game);

            Assert.That(_board.Cards.Count, Is.EqualTo(first));
        }

        /// <summary>
        /// The briefs have to conflict, or the player can satisfy everyone with one
        /// load and there is no game. This is the board-level view of the property
        /// No_Single_Round_Satisfies_Every_Brief guards.
        /// </summary>
        [Test]
        public void The_Jobs_On_The_Board_Do_Not_All_Want_The_Same_Thing()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (var order in _game.Board)
                foreach (var requirement in order.Requirements)
                    seen.Add(requirement.Quantity.ToString());

            Assert.That(seen.Count, Is.GreaterThan(1),
                "every job on the board is asking about the same measurement");
        }
    }
}
