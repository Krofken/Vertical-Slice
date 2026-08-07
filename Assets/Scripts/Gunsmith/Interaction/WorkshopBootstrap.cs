using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// The one object that IS saved in the scene, and which builds the shop when you
    /// press Play.
    ///
    /// THIS EXISTS BECAUSE OF A REAL MISTAKE. The workshop was previously assembled by
    /// an editor tool and tagged HideFlags.DontSave, so that spawning it could never
    /// dirty the scene or trigger a save prompt. DontSave means "not serialised", which
    /// also means the whole shop was NOT IN THE SCENE — so pressing Play reloaded the
    /// scene, the workshop evaporated, and the game was an empty room. It was verified
    /// by calling methods in edit mode, which is exactly the test that cannot catch it.
    ///
    /// A bootstrap gets both properties honestly. This component is a normal, saved
    /// scene object and is the ONLY thing committed. Everything it builds is created at
    /// runtime, so nothing else is ever serialised, nothing prompts to save, and the
    /// shop genuinely exists while you are playing.
    ///
    /// Anything spawned here is marked DontSave anyway, so an editor-time preview build
    /// still cannot leak into the scene file.
    /// </summary>
    [AddComponentMenu("Gunsmith/Workshop Bootstrap")]
    public sealed class WorkshopBootstrap : MonoBehaviour
    {
        [Tooltip("Build the shop as soon as the game starts. Turn off to build it " +
                 "yourself from code or a menu item.")]
        public bool BuildOnAwake = true;

        [Tooltip("Drop a player in as well. Off if the scene already has one.")]
        public bool SpawnPlayer = true;

        [Tooltip("Where the gunsmith is standing when the night begins.")]
        public Vector3 PlayerStart = new Vector3(0f, 1.0f, -3.2f);

        [Tooltip("Eye height, metres.")]
        public float EyeHeight = 1.65f;

        [Header("Authoring")]
        [Tooltip("A hand-placed shop. Assign one — or just make it a child of this " +
                 "object — and it is used as-is, so your layout survives pressing Play. " +
                 "Leave empty and a throwaway one is built from code at startup.")]
        public WorkshopController Shop;

        [Tooltip("Surface materials for a code-built shop. Ignored when the shop is " +
                 "hand-placed, since those objects carry their own.")]
        public WorkshopPalette Palette;

        /// <summary>The body, once it exists.</summary>
        public PlayerRig Player { get; private set; }

        private void Awake()
        {
            if (BuildOnAwake) Build();
        }

        /// <summary>
        /// Uses the shop that is already here, or builds one if there is not.
        ///
        /// PREFERRING AN EXISTING SHOP IS THE POINT. <see cref="Shop"/> used to be a
        /// read-only property, so it was never serialised, so it was always null on
        /// Awake, so the bootstrap rebuilt the shop from code every single time — which
        /// meant a hand-placed bench was destroyed the instant you pressed Play. There
        /// was no way to author the room by hand, and therefore no way to art-direct it.
        ///
        /// Now: an assigned or child <see cref="WorkshopController"/> wins, and the code
        /// path is the fallback for a scene that has nothing in it yet.
        /// </summary>
        public void Build()
        {
            // A child counts even if the field was never wired up, so dragging the
            // prefab under this object is enough.
            if (Shop == null) Shop = GetComponentInChildren<WorkshopController>(includeInactive: true);

            if (Shop == null) Shop = WorkshopBuilder.Build(transform, Palette);

            if (SpawnPlayer && Player == null) Player = BuildPlayer();
        }

        /// <summary>
        /// A body with a head and a camera in it.
        ///
        /// The scene's own Main Camera is switched off rather than deleted — it is the
        /// user's object and the playground has no business destroying it.
        /// </summary>
        private PlayerRig BuildPlayer()
        {
            // Kill any previous body first.
            //
            // The player is deliberately NOT parented to the bootstrap, which means an
            // editor-mode preview leaves an orphan at the scene root that clearing the
            // preview never touches. Those accumulate, they run their own Update, and
            // FindAnyObjectByType then returns whichever one it likes — which is how a
            // "player" ended up two hundred metres away at the same coordinates every
            // run. There is exactly one gunsmith.
            foreach (var stale in FindObjectsByType<PlayerRig>(FindObjectsSortMode.None))
            {
                if (Application.isPlaying) Destroy(stale.gameObject);
                else DestroyImmediate(stale.gameObject);
            }

            var existing = Camera.main;
            if (existing != null) existing.gameObject.SetActive(false);

            var body = new GameObject("Gunsmith");
            Disposable(body);

            // NOT parented, and placed in WORLD space.
            //
            // A CharacterController moves in world space and inherits nothing useful
            // from a parent, but it does inherit a parent's position, rotation and
            // scale - so hanging the player off the bootstrap meant the spawn point
            // depended on wherever that object happened to sit. Standing free removes
            // a whole class of "spawns somewhere odd" entirely.
            body.transform.position = transform.TransformPoint(PlayerStart);

            // FLOOR PROBE FIRST, CONTROLLER SECOND. The order matters: a
            // CharacterController is a collider, so probing after adding one means the
            // downward ray hits the player's own capsule before it reaches the floor
            // and stands him on top of his own head, a metre and a half in the air.
            //
            // Physics.SyncTransforms first, because a code-built floor was created this
            // same frame and its collider is not in the physics scene until it is synced
            // - without that the cast finds nothing and the player is dropped into space.
            Physics.SyncTransforms();
            StandOnFloor(body.transform);

            var controller = body.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var head = new GameObject("Head").transform;
            head.SetParent(body.transform, false);
            head.localPosition = new Vector3(0f, EyeHeight, 0f);
            Disposable(head.gameObject);

            var camera = head.gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.tag = "MainCamera";

            head.gameObject.AddComponent<AudioListener>();

            var rig = body.AddComponent<PlayerRig>();
            rig.Head = head;

            // What the player is looking at, and the prompt for using it. Lives on the
            // head so the ray leaves from the eyes.
            var prompt = new GameObject("Prompt");
            prompt.transform.SetParent(head, false);
            prompt.transform.localPosition = new Vector3(0f, -0.16f, 0.9f);
            Disposable(prompt);

            var text = prompt.AddComponent<TextMesh>();
            text.characterSize = 0.028f;
            text.fontSize = 72;
            text.color = new Color(0.97f, 0.95f, 0.88f);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;

            var interactor = head.gameObject.AddComponent<PlayerInteractor>();
            interactor.Prompt = text;

            return rig;
        }

        /// <summary>Editor previews stay out of the scene file; the running game does
        /// not use the flag at all. See WorkshopBuilder.Disposable.</summary>
        private static void Disposable(GameObject go)
        {
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
        }

        /// <summary>
        /// Drops the body until its feet are on whatever is below, so a spawn height
        /// never has to be guessed. Falls back to the requested height if there is
        /// nothing under the spawn point at all.
        /// </summary>
        private static void StandOnFloor(Transform body)
        {
            Vector3 above = body.position + Vector3.up * 4f;

            // RaycastAll and skip anything belonging to the body, rather than a plain
            // Raycast. Calling this before the CharacterController exists is what
            // actually fixes the self-hit, but this makes it stay fixed: the moment
            // somebody hangs a collider off the player for a hitbox or a held item, a
            // single-hit cast would quietly start landing him on himself again.
            var hits = Physics.RaycastAll(above, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = float.MaxValue;
            Vector3 point = Vector3.zero;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider.transform.IsChildOf(body)) continue;
                if (hits[i].distance >= best) continue;

                best = hits[i].distance;
                point = hits[i].point;
                found = true;
            }

            if (!found)
            {
                Debug.LogWarning("[Bootstrap] Nothing under the spawn point - the player " +
                                 "would fall. Leaving them where they are.");
                return;
            }

            // A hair above the surface so the controller settles rather than starting
            // interpenetrated, which makes it tunnel.
            body.position = point + Vector3.up * 0.05f;
        }

        private void Reset() => PlayerStart = new Vector3(0f, 1.0f, -3.2f);
    }
}
