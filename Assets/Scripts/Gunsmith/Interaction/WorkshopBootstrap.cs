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

        /// <summary>The shop this built, once it has.</summary>
        public WorkshopController Shop { get; private set; }

        /// <summary>The body, once it exists.</summary>
        public PlayerRig Player { get; private set; }

        private void Awake()
        {
            if (BuildOnAwake) Build();
        }

        /// <summary>
        /// Builds the shop and, optionally, the person standing in it.
        /// </summary>
        public void Build()
        {
            if (Shop == null) Shop = WorkshopBuilder.Build(transform);
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
            var existing = Camera.main;
            if (existing != null) existing.gameObject.SetActive(false);

            var body = new GameObject("Gunsmith");
            body.transform.SetParent(transform, false);
            body.transform.localPosition = PlayerStart;
            body.hideFlags = HideFlags.DontSave;

            var controller = body.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var head = new GameObject("Head").transform;
            head.SetParent(body.transform, false);
            head.localPosition = new Vector3(0f, EyeHeight, 0f);
            head.gameObject.hideFlags = HideFlags.DontSave;

            var camera = head.gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.tag = "MainCamera";

            head.gameObject.AddComponent<AudioListener>();

            var rig = body.AddComponent<PlayerRig>();
            rig.Head = head;

            return rig;
        }

        /// <summary>Gives the shop a floor to stand on, so the controller has something
        /// to rest against.</summary>
        private void Reset() => PlayerStart = new Vector3(0f, 1.0f, -3.2f);
    }
}
