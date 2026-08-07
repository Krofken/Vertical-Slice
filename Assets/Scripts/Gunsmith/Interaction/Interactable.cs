using System;
using UnityEngine;

namespace Gunsmith.Interaction
{
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

        /// <summary>What using it does. Wired in code by the builder.</summary>
        public Action Used;

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
        [Tooltip("Key that uses whatever is being looked at.")]
        public KeyCode UseKey = KeyCode.E;

        [Tooltip("Where the prompt is drawn. Sits just in front of the eyes.")]
        public TextMesh Prompt;

        private Interactable _looking;

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
                Prompt.text = _looking != null ? $"[{UseKey}]  {_looking.Prompt}" : string.Empty;

            if (_looking != null && Input.GetKeyDown(UseKey)) _looking.Use();
        }

        /// <summary>Nearest interactable the player is actually facing, or null.</summary>
        private Interactable Probe()
        {
            // Generous reach so nothing is out of range; each interactable then applies
            // its own limit, because a press handle wants you closer than a notice board.
            if (!Physics.Raycast(transform.position, transform.forward, out var hit, 6f))
                return null;

            var interactable = hit.collider.GetComponentInParent<Interactable>();
            if (interactable == null) return null;

            return hit.distance <= interactable.Reach ? interactable : null;
        }

        private void OnDisable()
        {
            if (_looking != null) _looking.SetLookedAt(false);
            _looking = null;
        }
    }
}
