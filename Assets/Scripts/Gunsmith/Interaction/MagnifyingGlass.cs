using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// A loupe the gunsmith holds up to his eye.
    ///
    /// This is the canon's own answer to legibility taken one step further. The shop already
    /// refuses to enlarge the work — a gunsmith does not scale up a cartridge, he leans in, which
    /// is what <see cref="StationView"/> does. A magnifying glass is the next honest move for
    /// something genuinely too small to see: half-millimetre powder granules are at the limit of
    /// what leaning in can resolve, and the real trade is that you bring glass to it.
    ///
    /// It also keeps a rule the project cares about. A loupe shows you the THING, at a size you
    /// can inspect — it is not a readout, it prints no numbers, and it cannot be memorised into a
    /// lookup table. Compare it with the sanctioned chronograph: an instrument is allowed when it
    /// hands over an observation rather than a prediction.
    ///
    /// HOW IT WORKS: a second camera at the eye, looking the same way, with its field of view
    /// DIVIDED by the magnification, rendering into a texture the lens samples in screen space.
    /// A narrower field of view over the same screen is magnification, and sampling in screen
    /// space is what keeps the magnified image lined up with the world behind the glass. The
    /// distortion and colour fringing at the rim are in the shader; without them a lens reads as
    /// a hole cut in the air.
    /// </summary>
    [AddComponentMenu("Gunsmith/Magnifying Glass")]
    public sealed class MagnifyingGlass : MonoBehaviour
    {
        [Header("Handling")]
        [Tooltip("Raises and lowers the glass.")]
        public Key RaiseKey = Key.G;

        [Tooltip("How far in front of the eye the glass is held, metres. A real loupe is held " +
                 "close, and the closer it is the more of the view it covers.")]
        public float HoldDistance = 0.20f;

        [Tooltip("How much of the frame's height the glass covers, 0..1. NOT a fixed diameter: " +
                 "the field of view changes every time the player leans into a station, and a " +
                 "glass sized in centimetres would cover the entire screen at 18 degrees.")]
        [Range(0.2f, 0.95f)] public float ScreenFraction = 0.62f;

        [Header("Optics")]
        [Tooltip("How many times magnified. A jeweller's loupe is about ten; four is enough to " +
                 "read a powder granule and still keep some context around it.")]
        [Range(1.5f, 12f)] public float Magnification = 4.5f;

        [Tooltip("Resolution the magnified view is rendered at. It only covers a disc on screen, " +
                 "so it does not need the full frame.")]
        [Range(256, 2048)] public int Resolution = 1024;

        [Header("Parts")]
        public Shader LensShader;

        /// <summary>True while the glass is up.</summary>
        public bool Raised { get; private set; }

        private Camera _eye;
        private Camera _lens;
        private Renderer _glass;
        private RenderTexture _view;
        private readonly List<Renderer> _hidden = new List<Renderer>();

        // ------------------------------------------------------------------

        private void Awake()
        {
            _eye = GetComponentInParent<Camera>();
            if (_eye == null) _eye = Camera.main;
        }

        private void OnDisable() => Lower();

        private void OnDestroy()
        {
            if (_view == null) return;

            _view.Release();
            Destroy(_view);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[RaiseKey].wasPressedThisFrame)
            {
                if (Raised) Lower(); else Raise();
            }
        }

        /// <summary>
        /// Renders the magnified view and holds the glass in front of the eye.
        ///
        /// LateUpdate, because the eye's own pose is written in PlayerRig.LateUpdate while
        /// leaning in — rendering before that would magnify last frame's view and the glass would
        /// lag behind the head every time the player moved.
        /// </summary>
        private void LateUpdate()
        {
            if (!Raised || _eye == null || _lens == null || _glass == null) return;

            // Held centred, at arm's length from the eye, square to the view.
            transform.position = _eye.transform.position + _eye.transform.forward * HoldDistance;
            transform.rotation = _eye.transform.rotation;

            // SIZED FROM THE CAMERA, every frame. Leaning in over a station drops the field of
            // view to about 18 degrees, and a glass fixed at 8.5 cm held 20 cm away subtends 24 —
            // so a hand-picked diameter covers the whole screen exactly when the player is
            // closest to the work. Same trap as the prompt and the station readouts: a constant
            // cannot be right when the lens it is measured against keeps changing.
            float visibleHeight = 2f * HoldDistance *
                                  Mathf.Tan(_eye.fieldOfView * 0.5f * Mathf.Deg2Rad);

            _glass.transform.localScale = Vector3.one * (visibleHeight * ScreenFraction);

            _lens.transform.SetPositionAndRotation(_eye.transform.position, _eye.transform.rotation);

            // A narrower field of view over the same screen IS the magnification.
            _lens.fieldOfView = Mathf.Max(1f, _eye.fieldOfView / Mathf.Max(1f, Magnification));
            _lens.nearClipPlane = _eye.nearClipPlane;
            _lens.farClipPlane = _eye.farClipPlane;

            // ANYTHING CARRIED ON THE HEAD MUST NOT BE IN THE MAGNIFIED VIEW. The lens camera
            // sits at the eye, so without this it photographs the glass itself — a feedback
            // loop — and the interaction prompt, which would appear across the view four times
            // life size. A layer would be the tidier fix, but layers are project settings and
            // this has to work in a scene nobody has configured.
            // TRY/FINALLY, because a leak here is permanent. If Render throws, the head's
            // renderers stay switched off for the rest of the session — the prompt and the glass
            // both silently vanish and there is nothing to suggest why.
            Hide();
            try { _lens.Render(); }
            finally { Reveal(); }
        }

        // ------------------------------------------------------------------

        /// <summary>Brings the glass up, building it the first time.</summary>
        public void Raise()
        {
            if (_eye == null) return;
            if (_lens == null && !Build()) return;

            Raised = true;
            _glass.gameObject.SetActive(true);
        }

        /// <summary>Puts the glass down.</summary>
        public void Lower()
        {
            Raised = false;
            if (_glass != null) _glass.gameObject.SetActive(false);
        }

        private bool Build()
        {
            var shader = LensShader != null ? LensShader : Shader.Find("Gunsmith/Lens");
            if (shader == null)
            {
                Debug.LogError("[Loupe] the Gunsmith/Lens shader is missing, so there is no " +
                               "glass to look through.", this);
                return false;
            }

            _view = new RenderTexture(Resolution, Resolution, 24)
            {
                name = "Loupe view",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };

            // The lens camera. Rendered by hand in LateUpdate, never automatically, so it can be
            // stepped after the head has been placed and with the held tools hidden.
            var lensGo = new GameObject("Loupe camera") { hideFlags = HideFlags.DontSave };
            lensGo.transform.SetParent(transform.parent, false);

            _lens = lensGo.AddComponent<Camera>();
            _lens.enabled = false;
            _lens.targetTexture = _view;
            _lens.clearFlags = _eye.clearFlags;
            _lens.backgroundColor = _eye.backgroundColor;
            _lens.cullingMask = _eye.cullingMask;

            // The glass itself: a quad built by hand, made round by the shader clipping anything
            // outside its own radius.
            //
            // NOT CreatePrimitive, deliberately. That fits a MeshCollider, and Destroy is
            // deferred to the end of the frame — so a primitive quad is a solid object held in
            // front of the player's eye for at least one frame, which is long enough to swallow a
            // click and is how the loupe broke every control in the shop. A tool you carry should
            // never have a collider at all.
            var quad = new GameObject("Loupe glass") { hideFlags = HideFlags.DontSave };
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.identity;

            // Sized properly in LateUpdate from the camera; this is only a sane starting value
            // for the frame before the first update runs.
            quad.transform.localScale = Vector3.one * 0.05f;

            quad.AddComponent<MeshFilter>().sharedMesh = LensQuad();
            _glass = quad.AddComponent<MeshRenderer>();
            _glass.sharedMaterial = new Material(shader) { name = "Loupe glass" };
            _glass.sharedMaterial.SetTexture("_LensTex", _view);

            quad.SetActive(false);
            return true;
        }

        /// <summary>
        /// A unit quad, centred, facing +Z. Built rather than borrowed so it carries no collider.
        /// </summary>
        private static Mesh LensQuad()
        {
            var mesh = new Mesh { name = "Loupe glass", hideFlags = HideFlags.DontSave };

            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f)
            });

            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f)
            });

            // Wound so the face points along -Z, which is the side the player is on.
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>Hides everything carried on the head, so the loupe cannot photograph it.</summary>
        private void Hide()
        {
            _hidden.Clear();

            var head = _eye.transform;

            foreach (var renderer in head.GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (!renderer.enabled) continue;

                renderer.enabled = false;
                _hidden.Add(renderer);
            }
        }

        private void Reveal()
        {
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].enabled = true;

            _hidden.Clear();
        }
    }
}
