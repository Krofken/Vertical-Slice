using System.Collections.Generic;
using Gunsmith.GameLoop;
using UnityEngine;

namespace Gunsmith.Orders
{
    /// <summary>
    /// The morning after: what came back about the rounds you sent out.
    ///
    /// Deliveries resolve at DAWN, never at the moment of handover. The player hands
    /// over a batch knowing nothing, sleeps, and finds out in the morning — which is
    /// where the whole weight of the game sits. Handing over a box and being told
    /// immediately whether it worked would turn the customer into a test instrument.
    ///
    /// The note leads with what happened to the person, not with what the round
    /// measured. "You have not seen the hunter since the delivery" lands before
    /// "penetration 4 cm short", because the story is what makes the number worth
    /// reading. That ordering is built in <see cref="OrderEvaluator"/>; this view must
    /// not reorder it.
    /// </summary>
    [AddComponentMenu("Gunsmith/Delivery Reports")]
    public sealed class DeliveryReportView : MonoBehaviour
    {
        [Header("Layout")]
        public float NoteSpacing = 0.52f;
        public Vector2 NoteSize = new Vector2(0.46f, 0.56f);

        [Header("Appearance")]
        public Material NoteMaterial;
        public Material DisasterNoteMaterial;
        public Color TextColour = new Color(0.12f, 0.10f, 0.08f);

        private readonly List<GameObject> _notes = new List<GameObject>();

        /// <summary>Notes currently pinned up.</summary>
        public IReadOnlyList<GameObject> Notes => _notes;

        /// <summary>
        /// Shows every delivery that has come back. Call at Dawn.
        /// </summary>
        public void Show(GunsmithGame game)
        {
            Clear();
            if (game == null) return;

            int index = 0;
            foreach (var accepted in game.Accepted)
            {
                if (!accepted.Reported || accepted.Evaluation == null) continue;

                Pin(accepted.Evaluation, new Vector3(index * NoteSpacing, 0f, 0f));
                index++;
            }
        }

        public void Clear()
        {
            foreach (var note in _notes)
            {
                if (note == null) continue;
                if (Application.isPlaying) Destroy(note); else DestroyImmediate(note);
            }

            _notes.Clear();
        }

        private void OnDestroy() => Clear();

        private void Pin(OrderEvaluation evaluation, Vector3 position)
        {
            var note = GameObject.CreatePrimitive(PrimitiveType.Quad);
            note.name = $"{evaluation.Outcome} — {evaluation.Order.CustomerName}";
            note.transform.SetParent(transform, false);
            note.transform.localPosition = position;
            note.transform.localScale = new Vector3(NoteSize.x, NoteSize.y, 1f);

            // A disaster is a different KIND of outcome, not a low score, so it should
            // not have to be read to be noticed.
            var material = evaluation.Outcome == OrderOutcome.Disaster && DisasterNoteMaterial != null
                ? DisasterNoteMaterial
                : NoteMaterial;

            if (material != null) note.GetComponent<MeshRenderer>().sharedMaterial = material;

            AddText(note.transform, evaluation.Feedback);

            _notes.Add(note);
        }

        private void AddText(Transform parent, string content)
        {
            var go = new GameObject("Note text");
            go.transform.SetParent(parent, false);

            // Undo the quad's scale so the text does not stretch with the note.
            go.transform.localScale = new Vector3(1f / NoteSize.x, 1f / NoteSize.y, 1f);
            go.transform.localPosition = new Vector3(-0.45f, 0.45f, -0.001f);

            var text = go.AddComponent<TextMesh>();
            text.text = content;
            text.characterSize = 0.012f;
            text.fontSize = 72;
            text.color = TextColour;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
        }
    }
}
