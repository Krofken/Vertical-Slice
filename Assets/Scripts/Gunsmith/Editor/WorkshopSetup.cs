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
            boardObject.transform.localPosition = new Vector3(-2.4f, 1.1f, 0f);

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
                bench.transform.SetParent(root.transform, true);
                bench.transform.localPosition = new Vector3(0f, 0f, 0f);

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
            rackObject.transform.localPosition = new Vector3(2.2f, 0.6f, 0f);
            rackObject.transform.localRotation = Quaternion.Euler(0f, -25f, 0f);

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
            reportsObject.transform.localPosition = new Vector3(-2.4f, -0.6f, 0f);

            var reports = reportsObject.AddComponent<DeliveryReportView>();
            reports.NoteMaterial = Flat(new Color(0.92f, 0.90f, 0.82f));
            reports.DisasterNoteMaterial = Flat(new Color(0.88f, 0.72f, 0.68f));
            shop.Reports = reports;

            // ---- the things you click --------------------------------------
            var controls = new GameObject("Controls");
            controls.transform.SetParent(root.transform, false);
            controls.transform.localPosition = new Vector3(0f, -1.05f, 0f);

            AddButton(controls.transform, "Take job", -0.9f, new Color(0.55f, 0.72f, 0.90f), shop.TakeJob);
            AddButton(controls.transform, "Press", -0.3f, new Color(0.90f, 0.78f, 0.35f), shop.PullHandle);
            AddButton(controls.transform, "Fire", 0.3f, new Color(0.90f, 0.45f, 0.35f), shop.FireOne);
            AddButton(controls.transform, "Hand over", 0.9f, new Color(0.60f, 0.85f, 0.60f), shop.DeliverBatch);
            AddButton(controls.transform, "Sleep", 1.5f, new Color(0.60f, 0.58f, 0.75f), shop.Advance);

            shop.Status = Label(root.transform, "Status", new Vector3(2.2f, -0.9f, 0f), 0.030f);

            // ---- put the camera somewhere useful ----------------------------
            var camera = Camera.main;
            if (camera != null)
            {
                root.transform.position = camera.transform.position + camera.transform.forward * 4.2f;
                root.transform.rotation = camera.transform.rotation;
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
            button.transform.localScale = new Vector3(0.5f, 0.16f, 0.08f);
            button.GetComponent<MeshRenderer>().sharedMaterial = Flat(colour);

            var click = button.AddComponent<ClickTarget>();
            click.Clicked = action;

            var text = Label(button.transform, "Label", new Vector3(0f, 0f, -0.7f), 0.10f);
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
