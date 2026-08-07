using System.Collections.Generic;
using Gunsmith.GameLoop;
using Gunsmith.Interaction;
using UnityEngine;

namespace Gunsmith.Orders
{
    /// <summary>
    /// The board by the door: the jobs that walked in this morning, as cards you can
    /// read and take down.
    ///
    /// THE CARD IS THE BRIEF, AND IT IS IN THE CUSTOMER'S WORDS. A house guard says he
    /// works in crowds. He does not say "sub-30 cm penetration, no perforation", because
    /// he does not know what that means and would not say it if he did. Translating what
    /// he said into something the bench can make is the game — so this view shows
    /// <see cref="Order.Brief"/> and <see cref="OrderRequirement.CustomerWords"/>, and
    /// never <see cref="OrderRequirement.Technical"/>.
    ///
    /// Numbers on a card are limited to what a customer would actually say: how many
    /// rounds they want, what they will pay, and when they need it. Nothing on this
    /// board tells the player what to build.
    /// </summary>
    [AddComponentMenu("Gunsmith/Order Board")]
    public sealed class OrderBoardView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Distance between cards, metres. Must exceed CardSize.x or they touch.")]
        public float CardSpacing = 0.56f;

        public float AcceptedDrop = 0.62f;

        [Header("Appearance")]
        public Material CardMaterial;
        public Material AcceptedCardMaterial;
        public Color TextColour = new Color(0.12f, 0.10f, 0.08f);

        [Tooltip("Card size in metres. A whole brief has to be legible on this, so it " +
                 "is nearer an index card than a business card — the text is fitted to " +
                 "whatever this is, and a small card just means small print.")]
        public Vector2 CardSize = new Vector2(0.50f, 0.62f);

        private readonly List<GameObject> _cards = new List<GameObject>();

        /// <summary>Cards currently pinned up.</summary>
        public IReadOnlyList<GameObject> Cards => _cards;

        /// <summary>
        /// Redraws the board: everything posted this morning across the top, everything
        /// already taken on below it.
        /// </summary>
        public void Show(GunsmithGame game)
        {
            Clear();
            if (game == null) return;

            int index = 0;
            foreach (var order in game.Board)
            {
                Pin(order, new Vector3(index * CardSpacing, 0f, 0f), CardMaterial, taken: false);
                index++;
            }

            int taken = 0;
            foreach (var accepted in game.Accepted)
            {
                Pin(accepted.Order, new Vector3(taken * CardSpacing, -AcceptedDrop, 0f),
                    AcceptedCardMaterial, taken: true);
                taken++;
            }

            FitAll();
        }

        /// <summary>
        /// Scales every card's text down until it fits the card.
        ///
        /// A SECOND PASS, and it has to be. A TextMesh reports empty renderer bounds
        /// immediately after the component is added — the mesh has not been generated
        /// yet — so fitting inside the same call that creates it measures nothing and
        /// silently does nothing at all. That is precisely how the board ended up with
        /// three briefs drawn on top of one another: the fit was there, it just never
        /// ran. Measure once every card exists.
        /// </summary>
        private void FitAll()
        {
            foreach (var card in _cards)
            {
                if (card == null) continue;

                var surface = card.GetComponent<Renderer>();
                var text = card.GetComponentInChildren<TextMesh>();
                if (surface == null || text == null) continue;

                TextFit.FitTo(text, surface, margin: 0.92f);
            }
        }

        public void Clear()
        {
            foreach (var card in _cards)
            {
                if (card == null) continue;
                if (Application.isPlaying) Destroy(card); else DestroyImmediate(card);
            }

            _cards.Clear();
        }

        private void OnDestroy() => Clear();

        /// <summary>
        /// One card. Everything on it is something the customer said or asked for.
        /// </summary>
        private void Pin(Order order, Vector3 position, Material material, bool taken)
        {
            if (order == null) return;

            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = (taken ? "Taken — " : "Posted — ") + order.CustomerName;
            card.transform.SetParent(transform, false);
            card.transform.localPosition = position;
            card.transform.localScale = new Vector3(CardSize.x, CardSize.y, 1f);

            if (material != null)
                card.GetComponent<MeshRenderer>().sharedMaterial = material;

            AddText(card.transform, Compose(order, taken));

            _cards.Add(card);
        }

        /// <summary>
        /// What is written on the card.
        ///
        /// The requirements are listed in the customer's words. If a technical figure
        /// ever appears here the player has been handed the answer, and the whole act of
        /// translating a brief into a load — which is the game — disappears.
        /// </summary>
        public static string Compose(Order order, bool taken)
        {
            if (order == null) return string.Empty;

            var text = new System.Text.StringBuilder();

            text.Append(order.CustomerName).Append('\n');
            text.Append(order.CustomerRole).Append("\n\n");
            text.Append(Wrap(order.Brief)).Append("\n\n");

            for (int i = 0; i < order.Requirements.Count; i++)
            {
                string words = order.Requirements[i].CustomerWords;
                if (string.IsNullOrEmpty(words)) continue;

                text.Append("- ").Append(Wrap(words, 30)).Append('\n');
            }

            text.Append('\n');
            text.Append(order.Quantity).Append(" rounds\n");
            text.Append(order.Payment).Append(" coin");

            if (taken) text.Append("\n\n(taken)");

            return text.ToString();
        }

        /// <summary>
        /// Breaks prose onto lines a card can hold.
        ///
        /// TextMesh does not wrap. A customer's brief is a sentence, so without this one
        /// card runs off in a single line metres long and the whole board becomes
        /// unreadable — which is exactly what it did the first time.
        /// </summary>
        public static string Wrap(string prose, int columns = 32)
        {
            if (string.IsNullOrEmpty(prose)) return string.Empty;

            var wrapped = new System.Text.StringBuilder(prose.Length + 16);
            int lineLength = 0;

            foreach (string word in prose.Split(' '))
            {
                if (word.Length == 0) continue;

                if (lineLength > 0 && lineLength + 1 + word.Length > columns)
                {
                    wrapped.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    wrapped.Append(' ');
                    lineLength++;
                }

                wrapped.Append(word);
                lineLength += word.Length;
            }

            return wrapped.ToString();
        }

        private void AddText(Transform parent, string content)
        {
            var go = new GameObject("Card text");
            go.transform.SetParent(parent, false);

            // The card is a unit quad scaled to size, so the text has to undo that
            // scale or it stretches with the card.
            go.transform.localScale = new Vector3(1f / CardSize.x, 1f / CardSize.y, 1f);

            var text = go.AddComponent<TextMesh>();
            text.text = content;
            text.characterSize = 0.014f;
            text.fontSize = 72;
            text.color = TextColour;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;

            // Top-left corner of the card, in the card's own unit space. Fitting happens
            // in FitAll once every card exists — see the note there for why it cannot
            // happen here.
            go.transform.localPosition = new Vector3(-0.46f, 0.46f, -0.001f);
        }
    }
}
