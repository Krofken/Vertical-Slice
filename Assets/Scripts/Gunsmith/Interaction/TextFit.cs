using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Shrinks a <see cref="TextMesh"/> until it fits the space it is meant to occupy.
    ///
    /// TextMesh HAS NO LAYOUT. It does not wrap, it does not clip, and it does not care
    /// how big the thing it is written on happens to be — it just renders at whatever
    /// <see cref="TextMesh.characterSize"/> says and runs off the edge. Two places in
    /// the shop were sized by hand and both were wrong:
    ///
    ///   the order cards, whose text measured 4x the width and 6x the height of the card
    ///   it was pinned to, so three cards 36 cm apart rendered as one unreadable smear;
    ///
    ///   the status readout, whose longest line ran off the side of the screen.
    ///
    /// Hand-tuning a character size cannot fix that class of bug, because the text
    /// changes: a customer with a longer name or an extra requirement overflows again.
    /// Measuring what was actually rendered and scaling to fit does fix it, for any
    /// font, any wrap width and any content.
    /// </summary>
    public static class TextFit
    {
        /// <summary>
        /// Scales a text object down until its rendered bounds sit inside
        /// <paramref name="targetWorldSize"/>.
        ///
        /// Only ever shrinks. Growing small text to fill a card would make a two-line
        /// note as loud as a full brief, and the board would stop reading as a board.
        /// </summary>
        /// <param name="text">The text to fit.</param>
        /// <param name="targetWorldSize">Width and height to fit inside, world units.</param>
        /// <param name="margin">Fraction of the target to actually use, leaving a border.</param>
        /// <returns>True if it was measured and fitted.</returns>
        public static bool Fit(TextMesh text, Vector2 targetWorldSize, float margin = 0.9f)
        {
            if (text == null) return false;
            if (targetWorldSize.x <= 0f || targetWorldSize.y <= 0f) return false;

            var renderer = text.GetComponent<Renderer>();
            if (renderer == null) return false;

            // Bounds come back empty for text that has not been rendered yet, and for
            // genuinely empty strings. Neither is worth scaling by.
            Vector3 size = renderer.bounds.size;
            if (size.x <= 1e-6f || size.y <= 1e-6f) return false;

            float fit = Mathf.Min(
                targetWorldSize.x * margin / size.x,
                targetWorldSize.y * margin / size.y);

            if (fit <= 0f || float.IsNaN(fit) || float.IsInfinity(fit)) return false;
            if (fit >= 1f) return true;   // already fits; never enlarge

            text.transform.localScale *= fit;
            return true;
        }

        /// <summary>
        /// Fits text to the renderer it is drawn on top of — a card, a note, a sign.
        /// </summary>
        public static bool FitTo(TextMesh text, Renderer surface, float margin = 0.9f)
        {
            if (surface == null) return false;

            Vector3 size = surface.bounds.size;
            return Fit(text, new Vector2(size.x, size.y), margin);
        }
    }
}
