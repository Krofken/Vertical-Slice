using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The parts a bench machine is made of: a cabinet, a screen with two columns of text on it,
    /// and buttons with their job written on the face.
    ///
    /// SHARED BECAUSE THERE ARE NOW TWO MACHINES. The dispenser and the refiner have the same
    /// idiom, and every awkward detail here was learned the hard way once already — a hand-picked
    /// character size renders four times the size of the machine, a mark placed at build time lands
    /// in the sky because Renderer.bounds is meaningless in the frame an object is created, and a
    /// TextMesh glyph is about six times its characterSize rather than one times. Duplicating that
    /// into a second machine would mean re-learning it in the second machine.
    /// </summary>
    public static class BenchScreen
    {
        /// <summary>A coloured solid. Keeps its collider, so it can be aimed at.</summary>
        public static Transform Body(Transform parent, string name, Vector3 position,
            Vector3 scale, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = Interaction.WorkshopPalette.Flat(colour);
            return go.transform;
        }

        /// <summary>
        /// A cabinet with a recessed screen in its face, and the glass to print on.
        ///
        /// The player always stands on -Z, and a TextMesh reads from its own -Z side, so the face
        /// is the machine's minimum Z and nothing here is rotated. Turning a label to "face" the
        /// player is what mirrors it.
        /// </summary>
        public static Renderer Cabinet(Transform parent, Vector3 centre, Vector3 size, float face)
        {
            Body(parent, "Cabinet", centre, size, new Color(0.20f, 0.21f, 0.24f));

            float screenY = centre.y + size.y * 0.15f;

            Body(parent, "Screen bezel", new Vector3(centre.x, screenY, face),
                new Vector3(size.x * 0.80f, size.y * 0.59f, 0.004f),
                new Color(0.08f, 0.09f, 0.10f));

            var glass = Body(parent, "Screen glass", new Vector3(centre.x, screenY, face - 0.0025f),
                new Vector3(size.x * 0.74f, size.y * 0.47f, 0.002f),
                new Color(0.045f, 0.075f, 0.06f));

            // Nothing aims at the glass; the buttons are what you press.
            var solid = glass.GetComponent<Collider>();
            if (solid != null) Object.Destroy(solid);

            return glass.GetComponent<Renderer>();
        }

        /// <summary>One column of readout. Size is NOT set here — it is fitted to the glass.</summary>
        public static TextMesh Column(Transform parent, string name, TextAnchor anchor,
            TextAlignment alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMesh>();
            text.fontSize = 72;
            text.anchor = anchor;
            text.alignment = alignment;
            text.lineSpacing = 1.0f;
            return text;
        }

        /// <summary>A button, with its mark on a sibling so a squashed cube cannot squash the glyph.</summary>
        public static Transform Button(Transform parent, string name, string mark, Vector3 position,
            Color colour)
        {
            var button = Body(parent, name, position, new Vector3(0.020f, 0.014f, 0.008f), colour);

            var cap = new GameObject(name + " mark");
            cap.transform.SetParent(parent, false);

            var text = cap.AddComponent<TextMesh>();
            text.text = mark;
            text.fontSize = 72;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.06f, 0.06f, 0.07f);

            return button;
        }

        /// <summary>
        /// Puts a button's mark on its face. MUST run after the frame the button was built in,
        /// because Renderer.bounds is not valid until the hierarchy has been updated.
        /// </summary>
        public static void PlaceMark(Transform button)
        {
            if (button == null || button.parent == null) return;

            var mark = button.parent.Find(button.name + " mark");
            if (mark == null) return;

            var text = mark.GetComponent<TextMesh>();
            var face = button.GetComponent<Renderer>();
            if (text == null || face == null) return;

            var bounds = face.bounds;
            if (bounds.size.x <= 1e-5f) return;

            mark.localScale = Vector3.one;

            // A glyph renders about six times its characterSize, and a longer mark has to shrink
            // to fit the same face.
            float across = Mathf.Max(1, text.text.Length);
            text.characterSize = bounds.size.y * 0.62f / 6f / Mathf.Sqrt(across);

            mark.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 0.0012f);
        }

        /// <summary>
        /// Lays two columns of text onto the glass and scales them to fit.
        ///
        /// Reset to the resting scale first: fitting MULTIPLIES, so refitting an already shrunken
        /// column every refresh ratchets it away to nothing.
        /// </summary>
        public static void LayOut(Renderer glass, TextMesh labels, TextMesh values,
            float columnWidth, ref Vector3 labelRest, ref bool labelKnown,
            ref Vector3 valueRest, ref bool valueKnown)
        {
            if (glass == null) return;

            var bounds = glass.bounds;
            var area = new Vector2(bounds.size.x * columnWidth, bounds.size.y);

            Fit(labels, area, ref labelRest, ref labelKnown);
            Fit(values, area, ref valueRest, ref valueKnown);

            float inset = bounds.size.y * 0.06f;
            float front = bounds.min.z - 0.0015f;

            if (labels != null)
                labels.transform.position =
                    new Vector3(bounds.min.x + inset, bounds.max.y - inset, front);

            if (values != null)
                values.transform.position =
                    new Vector3(bounds.max.x - inset, bounds.max.y - inset, front);
        }

        private static void Fit(TextMesh text, Vector2 area, ref Vector3 rest, ref bool known)
        {
            if (text == null) return;

            if (!known) { rest = text.transform.localScale; known = true; }

            text.transform.localScale = rest;
            Interaction.TextFit.Fit(text, area);
        }
    }
}
