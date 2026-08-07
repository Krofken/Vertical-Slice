using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Which of the shop's actions a fixture performs.
    ///
    /// THIS IS AN ENUM AND NOT A DELEGATE FOR ONE REASON: it has to survive being saved.
    /// A <see cref="System.Action"/> assigned in code cannot be serialised, so a shop
    /// loaded from a prefab or placed by hand came up with every fixture inert — the
    /// press handle, the counter, the cot, all present, all doing nothing, with no
    /// error to say so. An enum round-trips through a prefab and a scene file, and
    /// <see cref="WorkshopController"/> binds it on Awake.
    /// </summary>
    public enum ShopAction
    {
        None = 0,
        TakeJob = 1,
        PullPressHandle = 2,
        FireOne = 3,
        HandOverBatch = 4,
        TurnInForTheNight = 5
    }

    /// <summary>
    /// A thing in the shop you can walk up to and use.
    ///
    /// This replaces the row of buttons, which was the wrong shape for this game: the
    /// whole design rests on actions being PLACES you go rather than entries in a menu.
    /// Pulling the press handle happens at the press. Firing happens at the range,
    /// because you have to walk out there. Handing a batch over happens at the counter.
    ///
    /// The prompt is written in the second person and names the object, not the system —
    /// "pull the press handle", never "execute craft".
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Gunsmith/Interactable")]
    public sealed class Interactable : MonoBehaviour
    {
        [Tooltip("Shown when the player is looking at this. Second person, names the " +
                 "object rather than the system.")]
        public string Prompt = "use";

        [Tooltip("Which shop action this performs. Serialised, so it survives being " +
                 "saved into a prefab or a scene — unlike a code-assigned delegate.")]
        public ShopAction Action = ShopAction.None;

        /// <summary>
        /// What using it does, bound at runtime.
        ///
        /// Set by <see cref="WorkshopController"/> from <see cref="Action"/>, or
        /// assigned directly in code for a one-off fixture that has no enum entry.
        /// Never serialised — see <see cref="ShopAction"/> for why that matters.
        /// </summary>
        [NonSerialized] public Action Used;

        [Tooltip("How close you have to be, metres. A shop is small.")]
        public float Reach = 2.6f;

        [Header("Highlight")]
        public Color HighlightTint = new Color(1f, 0.95f, 0.7f);

        private Renderer _renderer;
        private Color _rest;
        private bool _looked;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer != null && _renderer.material != null) _rest = _renderer.material.color;
        }

        /// <summary>Called every frame the player is looking at this, and once when they
        /// stop.</summary>
        public void SetLookedAt(bool looked)
        {
            if (_looked == looked) return;
            _looked = looked;

            if (_renderer == null || _renderer.material == null) return;
            _renderer.material.color = looked ? HighlightTint : _rest;
        }

        /// <summary>Uses it.</summary>
        public void Use()
        {
            // Nudge, so a press reads as pressed even without animation.
            transform.localScale *= 0.94f;
            Invoke(nameof(Restore), 0.09f);

            Used?.Invoke();
        }

        private void Restore() => transform.localScale /= 0.94f;
    }

    /// <summary>
    /// The player's hands: what they are looking at, and using it.
    ///
    /// Lives on the head so the ray comes from the eyes. Deliberately a plain raycast
    /// with a reach limit rather than a trigger volume — you have to be facing a thing,
    /// not merely near it, which is what makes the shop feel like a room you move
    /// through rather than a set of hotspots.
    /// </summary>
    [AddComponentMenu("Gunsmith/Player Interactor")]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("Key that uses whatever is being looked at. Input System, because " +
                 "the project has legacy input handling switched off.")]
        public Key UseKey = Key.E;

        [Tooltip("Where the prompt is drawn. Sits just in front of the eyes.")]
        public TextMesh Prompt;

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
            if (string.IsNullOrEmpty(Prompt.text)) return;

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
                Prompt.text = _looking != null ? $"[{UseKey}]  {_looking.Prompt}" : string.Empty;

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
