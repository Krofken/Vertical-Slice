using System.Linq;
using Gunsmith.GameLoop;
using Gunsmith.Interaction;
using Gunsmith.Orders;
using NUnit.Framework;
using UnityEngine;

namespace Gunsmith.Tests
{
    /// <summary>
    /// Nothing in the shop may draw outside the thing it is written on.
    ///
    /// `TextMesh` has no layout: it does not wrap, does not clip, and does not care how
    /// big its card is. Both readouts in the shop were sized by a hand-picked constant
    /// and both were wrong — the order cards rendered four times wider and six times
    /// taller than the card they were pinned to, so three briefs 36 cm apart drew on top
    /// of one another and the board was an unreadable smear.
    ///
    /// A constant cannot fix that, because the text changes: a longer customer name or
    /// one more requirement overflows again. These tests assert the FIT, not the size,
    /// so they keep holding as the content changes.
    /// </summary>
    public class BoardLayoutTests
    {
        private GameObject _root;
        private WorkshopController _shop;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("LayoutTest");
            _shop = WorkshopBuilder.Build(_root.transform, palette: null, persistent: true);
            _shop.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private static Renderer TextOf(GameObject card)
            => card.GetComponentInChildren<TextMesh>().GetComponent<Renderer>();

        [Test]
        public void The_Board_Actually_Posted_Something()
        {
            Assert.That(_shop.Board, Is.Not.Null);
            Assert.That(_shop.Board.Cards.Count, Is.GreaterThan(0),
                "no cards, so the rest of this file proves nothing");
        }

        [Test]
        public void No_Card_Text_Spills_Off_Its_Card()
        {
            foreach (var card in _shop.Board.Cards)
            {
                var surface = card.GetComponent<Renderer>().bounds.size;
                var text = TextOf(card).bounds.size;

                Assert.That(text.x, Is.LessThanOrEqualTo(surface.x),
                    $"'{card.name}' text is wider than its card");
                Assert.That(text.y, Is.LessThanOrEqualTo(surface.y),
                    $"'{card.name}' text is taller than its card");
            }
        }

        [Test]
        public void No_Two_Cards_Draw_On_Top_Of_Each_Other()
        {
            // The symptom the player actually saw: three briefs overprinted into mush.
            var texts = _shop.Board.Cards.Select(TextOf).ToArray();

            for (int i = 0; i < texts.Length; i++)
                for (int j = i + 1; j < texts.Length; j++)
                    Assert.That(texts[i].bounds.Intersects(texts[j].bounds), Is.False,
                        $"card {i} and card {j} overlap");
        }

        [Test]
        public void A_Long_Brief_Is_Shrunk_Rather_Than_Allowed_To_Overflow()
        {
            // Fitting must respond to CONTENT, not to a constant chosen once. Give one
            // card far more text than any real order and it must still sit on the card.
            var card = _shop.Board.Cards[0];
            var text = card.GetComponentInChildren<TextMesh>();

            text.text = string.Join("\n", Enumerable.Repeat(
                "a very long line of a customer explaining themselves at length", 40));

            TextFit.FitTo(text, card.GetComponent<Renderer>(), 0.92f);

            var surface = card.GetComponent<Renderer>().bounds.size;
            var rendered = text.GetComponent<Renderer>().bounds.size;

            Assert.That(rendered.x, Is.LessThanOrEqualTo(surface.x));
            Assert.That(rendered.y, Is.LessThanOrEqualTo(surface.y));
        }

        [Test]
        public void The_Status_Readout_Fits_The_Space_It_Is_Given()
        {
            Assert.That(_shop.Status, Is.Not.Null);

            var rendered = _shop.Status.GetComponent<Renderer>().bounds.size;

            Assert.That(rendered.x, Is.LessThanOrEqualTo(_shop.StatusSize.x),
                "the status ran off the side of the screen");
            Assert.That(rendered.y, Is.LessThanOrEqualTo(_shop.StatusSize.y));
        }

        [Test]
        public void Refreshing_Repeatedly_Does_Not_Shrink_The_Status_Away()
        {
            // Fit MULTIPLIES the scale, so re-fitting an already-fitted label every
            // refresh would drive it to nothing over a few in-game days.
            _shop.Refresh();
            float first = _shop.Status.GetComponent<Renderer>().bounds.size.x;

            for (int i = 0; i < 10; i++) _shop.Refresh();
            float after = _shop.Status.GetComponent<Renderer>().bounds.size.x;

            Assert.That(after, Is.EqualTo(first).Within(first * 0.02f),
                "the status shrank as it was refreshed");
        }

        [Test]
        public void Fitting_Never_Enlarges_Small_Text()
        {
            // A two-line note must not be blown up to fill a card, or the board stops
            // reading as a board.
            var go = new GameObject("small") { hideFlags = HideFlags.DontSave };
            try
            {
                var text = go.AddComponent<TextMesh>();
                text.text = "ok";
                text.characterSize = 0.01f;
                text.fontSize = 72;

                Vector3 before = go.transform.localScale;
                TextFit.Fit(text, new Vector2(10f, 10f));

                Assert.That(go.transform.localScale, Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
