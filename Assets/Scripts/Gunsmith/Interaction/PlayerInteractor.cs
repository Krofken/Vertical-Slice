using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// The player's hands: what they are looking at, and using it.
    ///
    /// Lives on the head so the ray comes from the eyes. Deliberately a plain raycast
    /// with a reach limit rather than a trigger volume — you have to be facing a thing,
    /// not merely near it, which is what makes the shop feel like a room you move
    /// through rather than a set of hotspots.
    ///
    /// SPLIT OUT OF Interactable.cs, for the same reason <see cref="Gunsmith.Range.RangeStation"/>
    /// was split out of EvidenceRack.cs: Unity resolves a MonoBehaviour's script reference
    /// by FILE NAME, so a behaviour sharing a file with another class cannot be serialised —
    /// it comes back as "the referenced script on this Behaviour is missing" with all its
    /// data intact and a dead script pointer. That is what killed the yard in the shop
    /// prefab.
    ///
    /// This one had not bitten yet, and only by luck: the player rig is rebuilt from
    /// scratch at runtime and is deliberately never parented to the bootstrap, so nothing
    /// ever tried to save it. The moment anyone prefabs the player — which is an obvious
    /// next step for art-directing him — every interaction in the shop would have gone
    /// dead with no error. Fixed before it could.
    /// </summary>
    [AddComponentMenu("Gunsmith/Player Interactor")]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("Key that uses whatever is being looked at. Input System, because " +
                 "the project has legacy input handling switched off.")]
        public Key UseKey = Key.E;

        [Tooltip("Where the prompt is drawn. Sits just in front of the eyes.")]
        public TextMesh Prompt;

        [Tooltip("Dark plate drawn behind the prompt so it reads against a lit wall, a " +
                 "brass case or a highlighted object. Sized to the text automatically.")]
        public Transform PromptBacking;

        [Tooltip("Margin around the caption text, metres at the prompt's own distance.")]
        public Vector2 PromptPadding = new Vector2(0.045f, 0.022f);

        [Tooltip("Fraction of the screen width the prompt may occupy at most.")]
        [Range(0.1f, 0.9f)]
        public float PromptWidthFraction = 0.42f;

        [Tooltip("Fraction of the screen height the prompt may occupy at most.")]
        [Range(0.02f, 0.5f)]
        public float PromptHeightFraction = 0.09f;

        private Interactable _looking;
        private PlayerRig _rig;
        private Camera _camera;
        private Vector3 _promptRestScale;
        private bool _promptRestKnown;

        private void Awake()
        {
            _rig = GetComponentInParent<PlayerRig>();
            _camera = GetComponentInParent<Camera>();
            if (_camera == null) _camera = Camera.main;
        }

        /// <summary>
        /// Scales the prompt so it always occupies the same slice of the screen.
        ///
        /// IT WAS NEVER FITTED TO ANYTHING. The prompt hangs 90 cm from the eye at a
        /// hand-picked character size, so "[E] fire one into the block" rendered about
        /// 2.4 m wide where only 1.85 m is visible — 130% of the screen, which is why it
        /// covered everything. Leaning in made it far worse: focus drops the field of
        /// view to 18 degrees, which shrinks the visible width at that distance to half
        /// a metre, so the same line became roughly 470% of the screen.
        ///
        /// The fix has to be computed from the CAMERA, not tuned as a constant, because
        /// the field of view changes every time the player leans over a station. Work
        /// out how much world space is actually visible at the prompt's distance, then
        /// fit the text into a fraction of it.
        /// </summary>
        private void FitPrompt()
        {
            if (Prompt == null || _camera == null) return;

            // Nothing to caption: hide the plate too, or a dark rectangle floats in
            // front of the player's face whenever they are looking at nothing.
            if (string.IsNullOrEmpty(Prompt.text))
            {
                if (PromptBacking != null && PromptBacking.gameObject.activeSelf)
                    PromptBacking.gameObject.SetActive(false);
                return;
            }

            if (!_promptRestKnown)
            {
                _promptRestScale = Prompt.transform.localScale;
                _promptRestKnown = true;
            }

            // Fit multiplies, so always start from the resting scale or the prompt
            // ratchets smaller every frame it is shown.
            Prompt.transform.localScale = _promptRestScale;

            float distance = Vector3.Distance(Prompt.transform.position, _camera.transform.position);
            if (distance <= 0.01f) return;

            float visibleHeight = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float visibleWidth = visibleHeight * _camera.aspect;

            TextFit.Fit(Prompt,
                new Vector2(visibleWidth * PromptWidthFraction, visibleHeight * PromptHeightFraction),
                margin: 1f);

            SizeBackingPlate();
        }

        /// <summary>
        /// Stretches the plate to whatever the caption ended up measuring.
        ///
        /// Has to run AFTER the fit, and every frame: the text changes with what you are
        /// looking at, and its size changes again as the field of view narrows while you
        /// lean into a station. A fixed-size plate would clip a long prompt and hang off
        /// the end of a short one.
        /// </summary>
        private void SizeBackingPlate()
        {
            if (PromptBacking == null) return;

            var textRenderer = Prompt.GetComponent<Renderer>();
            if (textRenderer == null) return;

            if (!PromptBacking.gameObject.activeSelf) PromptBacking.gameObject.SetActive(true);

            var parent = PromptBacking.parent;
            Vector3 size = textRenderer.bounds.size;

            // The head is unscaled, so world extents are usable as local ones directly.
            PromptBacking.localScale = new Vector3(
                size.x + PromptPadding.x,
                size.y + PromptPadding.y,
                1f);

            // Centred on the text and nudged behind it, so the text always wins the
            // depth test against its own backing.
            Vector3 centre = parent != null
                ? parent.InverseTransformPoint(textRenderer.bounds.center)
                : textRenderer.bounds.center;

            centre.z = Prompt.transform.localPosition.z + 0.004f;
            PromptBacking.localPosition = centre;
            PromptBacking.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            var found = Probe();

            if (found != _looking)
            {
                if (_looking != null) _looking.SetLookedAt(false);
                _looking = found;
                if (_looking != null) _looking.SetLookedAt(true);
            }

            if (Prompt != null)
            {
                // SILENT WHILE LEANING IN. Once the player is at a station the caption has done its
                // job — it told them what the station was on the way in — and from then on it is a
                // line of text printed across the work they came to look at. The station's own
                // readouts are what should be talking at that range.
                //
                // Only the text goes. Everything else about the interactor keeps running, so
                // whatever is under the aim is still highlighted and still usable.
                bool leaning = _rig != null && _rig.Focused != null;

                Prompt.text = _looking != null && !leaning
                    ? $"[{UseKey}]  {_looking.Prompt}"
                    : string.Empty;

                // Every frame, not just on change: the field of view moves continuously
                // while leaning in and out of a station.
                FitPrompt();
            }

            var keyboard = Keyboard.current;
            if (_looking == null || keyboard == null || !keyboard[UseKey].wasPressedThisFrame) return;

            // A station you can lean over gets leaned over. Everything else — the cot,
            // the counter, the press handle — just does its thing where it stands.
            var station = _looking.GetComponentInParent<StationView>();
            if (station != null && _rig != null) _rig.ToggleFocus(station);

            _looking.Use();
        }

        /// <summary>
        /// Nearest interactable the player is actually facing, or null.
        ///
        /// SKIPS THE PLAYER'S OWN BODY. The head sits inside the CharacterController
        /// capsule, so a plain raycast looking down hits 'Gunsmith' at eight centimetres
        /// and stops there — every bench station is at 92 cm, below eye level, so the
        /// moment you looked down to work you were staring at the inside of your own
        /// chest. Looking level instead sent the ray straight over the bench. Between
        /// the two, nothing on the bench could ever be used.
        ///
        /// Same mistake as the spawn probe in WorkshopBootstrap, in a second place: a
        /// single Raycast from inside a collider finds that collider first.
        /// </summary>
        private Interactable Probe()
        {
            var body = _rig != null ? _rig.transform : null;

            // Generous reach so nothing is out of range; each interactable then applies
            // its own limit, because a press handle wants you closer than a notice board.
            var hits = Physics.RaycastAll(transform.position, transform.forward, 6f,
                ~0, QueryTriggerInteraction.Collide);

            Interactable best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (body != null && collider.transform.IsChildOf(body)) continue;
                if (hits[i].distance >= nearest) continue;

                var interactable = collider.GetComponentInParent<Interactable>();
                if (interactable == null) continue;
                if (hits[i].distance > interactable.Reach) continue;

                nearest = hits[i].distance;
                best = interactable;
            }

            return best;
        }

        private void OnDisable()
        {
            if (_looking != null) _looking.SetLookedAt(false);
            _looking = null;
        }
    }
}
