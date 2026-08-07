using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Gunsmith.Interaction;
using Gunsmith.Orders;
using Gunsmith.Range;
using Krofken.Ballistics;
using UnityEditor;
using UnityEngine;

namespace Gunsmith.EditorTools
{
    /// <summary>
    /// Builds a playable workshop in the open scene.
    ///
    /// This is a PLAYGROUND, not the vertical slice scene — that one is the user's to
    /// build by hand and this tool must never write it. Everything spawned here is
    /// tagged DontSave, so it never touches the scene file and never prompts to save.
    ///
    /// Press Play and work a night: take a job off the board, drag the coloured handles
    /// on the bench, pull the press handle, fire one at the block, hand the batch over,
    /// then sleep and read what came back.
    /// </summary>
    public static class WorkshopSetup
    {
        private const string RootName = "~Workshop";

        // LAYOUT NOTE, because this is what made the first version unreadable.
        //
        // The stations do not share a natural scale and cannot be made to. A 13 mm
        // bullet has to be exaggerated about forty times before it is worth looking at;
        // a gel block is seventy centimetres long and is already the right size. Putting
        // both at 1:1 in the same room gives a heap.
        //
        // So each group gets its own scale factor chosen to bring it to roughly the same
        // ON-SCREEN size, and then they are spaced far enough apart that nothing
        // overlaps. The numbers below are "how big should this read", not "how big is
        // this really" — and nothing a solver touches is affected, because only the
        // display rigs are scaled.
        private const float BenchScale = 0.45f;
        private const float RackScale = 0.90f;
        private const float BoardScale = 0.85f;

        private static readonly Vector3 BoardAt = new Vector3(-2.9f, 1.15f, 0f);
        private static readonly Vector3 BenchAt = new Vector3(0f, 0.35f, 0f);
        private static readonly Vector3 RackAt = new Vector3(2.25f, 0.95f, 0f);
        private static readonly Vector3 NotesAt = new Vector3(-2.9f, -1.15f, 0f);
        private static readonly Vector3 ControlsAt = new Vector3(0f, -1.75f, 0f);
        private static readonly Vector3 StatusAt = new Vector3(2.45f, -1.20f, 0f);

        [MenuItem("Gunsmith/Open Workshop", priority = -10)]
        public static void Spawn()
        {
            Clear();

            var root = new GameObject(RootName);

            var game = root.AddComponent<GunsmithGameBehaviour>();
            var shop = root.AddComponent<WorkshopController>();
            shop.GameBehaviour = game;

            // ---- the board by the door ------------------------------------
            var boardObject = new GameObject("Order board");
            boardObject.transform.SetParent(root.transform, false);
            boardObject.transform.localPosition = BoardAt;
            boardObject.transform.localScale = Vector3.one * BoardScale;

            var board = boardObject.AddComponent<OrderBoardView>();
            board.CardMaterial = Flat(new Color(0.90f, 0.87f, 0.78f));
            board.AcceptedCardMaterial = Flat(new Color(0.74f, 0.80f, 0.70f));
            shop.Board = board;

            // ---- the bench -------------------------------------------------
            // Reuses the lathe bench exactly as it already is, then hangs a press off
            // the same four tools.
            LatheBenchSetup.Spawn();

            var bench = GameObject.Find("~LatheBench");
            if (bench != null)
            {
                bench.name = "Bench";
                bench.transform.SetParent(root.transform, false);

                // LatheBenchSetup places and rotates itself to face whatever camera it
                // found. Reset all three, or the bench arrives at some angle of its own
                // and the shop never lines up.
                bench.transform.localPosition = BenchAt;
                bench.transform.localRotation = Quaternion.identity;
                bench.transform.localScale = Vector3.one * BenchScale;

                var press = bench.AddComponent<LoadingPress>();
                press.CoreBench = bench.GetComponent<LatheStation>();
                press.Mill = bench.GetComponentInChildren<PropellantMill>();
                press.Balance = bench.GetComponentInChildren<PowderBalance>();
                press.Die = bench.GetComponentInChildren<SeatingStop>();
                press.BatchSize = 20;
                shop.Press = press;
            }

            // ---- the yard --------------------------------------------------
            var rackObject = new GameObject("Evidence rack");
            rackObject.transform.SetParent(root.transform, false);
            rackObject.transform.localPosition = RackAt;
            rackObject.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
            rackObject.transform.localScale = Vector3.one * RackScale;

            var rack = rackObject.AddComponent<EvidenceRack>();
            rack.BlockMaterial = Translucent(new Color(0.70f, 0.78f, 0.72f, 0.16f));
            rack.CavityMaterial = Flat(new Color(0.85f, 0.25f, 0.20f));
            rack.BandMaterial = Flat(new Color(0.10f, 0.10f, 0.12f));
            rack.CardMaterial = Flat(new Color(0.92f, 0.90f, 0.84f));
            rack.ProjectileMaterial = Flat(new Color(0.75f, 0.58f, 0.30f));
            rack.BrassMaterial = Flat(new Color(0.80f, 0.66f, 0.30f));
            rack.PrimerMaterial = Flat(new Color(0.66f, 0.64f, 0.60f));
            rack.MarkMaterial = Flat(new Color(0.20f, 0.18f, 0.16f));
            shop.Rack = rack;

            var yard = root.AddComponent<RangeStation>();
            yard.Rack = rack;
            shop.Yard = yard;

            // ---- the morning's post ----------------------------------------
            var reportsObject = new GameObject("Delivery notes");
            reportsObject.transform.SetParent(root.transform, false);
            reportsObject.transform.localPosition = NotesAt;
            reportsObject.transform.localScale = Vector3.one * BoardScale;

            var reports = reportsObject.AddComponent<DeliveryReportView>();
            reports.NoteMaterial = Flat(new Color(0.92f, 0.90f, 0.82f));
            reports.DisasterNoteMaterial = Flat(new Color(0.88f, 0.72f, 0.68f));
            shop.Reports = reports;

            // ---- the things you click --------------------------------------
            var controls = new GameObject("Controls");
            controls.transform.SetParent(root.transform, false);
            controls.transform.localPosition = ControlsAt;

            AddButton(controls.transform, "Take job", -1.24f, new Color(0.55f, 0.72f, 0.90f), shop.TakeJob);
            AddButton(controls.transform, "Press", -0.62f, new Color(0.90f, 0.78f, 0.35f), shop.PullHandle);
            AddButton(controls.transform, "Fire", 0f, new Color(0.90f, 0.45f, 0.35f), shop.FireOne);
            AddButton(controls.transform, "Hand over", 0.62f, new Color(0.60f, 0.85f, 0.60f), shop.DeliverBatch);
            AddButton(controls.transform, "Sleep", 1.24f, new Color(0.60f, 0.58f, 0.75f), shop.Advance);

            shop.Status = Label(root.transform, "Status", StatusAt, 0.024f);

            // ---- put the camera somewhere useful ----------------------------
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            // Frame the whole shop rather than hanging it off wherever the camera was
            // pointing. It spans about seven metres, so back off far enough to hold it.
            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0.2f, 0f, -6.4f);
                camera.transform.rotation = Quaternion.identity;
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                transform.gameObject.hideFlags = HideFlags.DontSave;

            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();

            Debug.Log("[Workshop] Press Play. Take a job, set the bench, Press, Fire, Hand over, Sleep.");
        }

        [MenuItem("Gunsmith/Close Workshop", priority = -9)]
        public static void Clear()
        {
            LatheBenchSetup.Clear();

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);
        }

        private static void AddButton(
            Transform parent, string label, float x, Color colour, System.Action action)
        {
            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = label;
            button.transform.SetParent(parent, false);
            button.transform.localPosition = new Vector3(x, 0f, 0f);
            button.transform.localScale = new Vector3(0.56f, 0.16f, 0.08f);
            button.GetComponent<MeshRenderer>().sharedMaterial = Flat(colour);

            var click = button.AddComponent<ClickTarget>();
            click.Clicked = action;

            var text = Label(button.transform, "Label", new Vector3(0f, 0f, -0.7f), 0.014f);
            text.transform.localScale = new Vector3(1f / 0.5f, 1f / 0.16f, 1f);
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = new Color(0.10f, 0.09f, 0.08f);
            click.Label = text;
        }

        private static TextMesh Label(Transform parent, string name, Vector3 position, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var text = go.AddComponent<TextMesh>();
            text.characterSize = size;
            text.fontSize = 72;
            text.color = new Color(0.95f, 0.93f, 0.88f);
            text.anchor = TextAnchor.UpperLeft;

            return text;
        }

        private static Material Flat(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.color = colour;
            return material;
        }

        private static Material Translucent(Color colour)
        {
            var material = Flat(colour);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.color = colour;
            return material;
        }
    }
}
