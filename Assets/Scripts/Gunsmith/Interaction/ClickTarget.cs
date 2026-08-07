using System;
using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Something in the workshop you can click.
    ///
    /// Deliberately the crudest possible interaction layer: a collider and
    /// <c>OnMouseDown</c>. The point is not the input system, it is that every action in
    /// the game is a THING IN THE ROOM rather than a row in a menu — you pull the press
    /// handle, you take a card off the board, you pick up the gun. When real input
    /// arrives it replaces this and nothing else has to change, because the actions live
    /// on the stations already.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Gunsmith/Click Target")]
    public sealed class ClickTarget : MonoBehaviour
    {
        /// <summary>What this does when clicked. Wired in code by the setup tool.</summary>
        public Action Clicked;

        /// <summary>Shown beside the object so the player knows what it is.</summary>
        public TextMesh Label;

        [Tooltip("Nudges toward the camera briefly when clicked, so a press feels pressed.")]
        public float PressDepth = 0.02f;

        private Vector3 _rest;
        private float _pressedUntil;

        private void Awake() => _rest = transform.localPosition;
        private void OnEnable() => _rest = transform.localPosition;

        private void OnMouseDown()
        {
            _pressedUntil = Time.time + 0.12f;
            transform.localPosition = _rest - transform.forward * PressDepth;

            Clicked?.Invoke();
        }

        private void Update()
        {
            if (_pressedUntil <= 0f || Time.time < _pressedUntil) return;

            transform.localPosition = _rest;
            _pressedUntil = 0f;
        }

        /// <summary>Sets the text beside the control.</summary>
        public void SetLabel(string text)
        {
            if (Label != null) Label.text = text;
        }
    }
}
