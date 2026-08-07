using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Gunsmith.Range;
using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Builds the shop AT RUNTIME.
    ///
    /// This used to live in an editor-only tool, which is why the workshop did not exist
    /// when you pressed Play. Construction belongs here, in the runtime assembly, so the
    /// same code makes the shop in the editor, in Play mode and in a build.
    ///
    /// The stations do not share a natural scale and cannot be made to — a 13 mm bullet
    /// needs exaggerating about forty times before it is worth looking at, while a gel
    /// block is already seventy centimetres. Each group therefore gets its own factor,
    /// chosen for how big it should READ. Only display rigs are scaled; nothing a solver
    /// touches is affected.
    /// </summary>
    public static class WorkshopBuilder
    {
        private const float BenchScale = 0.45f;
        private const float RackScale = 0.90f;
        private const float BoardScale = 0.85f;
        private const float BulletDisplayScale = 40f;

        /// <summary>Builds the whole shop under a parent and returns its controller.</summary>
        public static WorkshopController Build(Transform parent)
        {
            var root = new GameObject("Shop");
            root.transform.SetParent(parent, false);
            Disposable(root);

            var game = root.AddComponent<GunsmithGameBehaviour>();
            var shop = root.AddComponent<WorkshopController>();
            shop.GameBehaviour = game;

            BuildFloor(root.transform);

            shop.Board = BuildBoard(root.transform);
            shop.Press = BuildBench(root.transform);
            shop.Rack = BuildRack(root.transform);
            shop.Reports = BuildReports(root.transform);

            var yard = root.AddComponent<RangeStation>();
            yard.Rack = shop.Rack;
            shop.Yard = yard;

            shop.Status = Label(root.transform, "Status", new Vector3(2.45f, 1.05f, 0f), 0.024f);

            BuildStationControls(root.transform, shop);

            return shop;
        }

        // ------------------------------------------------------------------

        /// <summary>Something to stand on. Without it the character controller falls
        /// forever the moment the game starts.</summary>
        private static void BuildFloor(Transform parent)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(16f, 0.2f, 12f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.28f, 0.26f, 0.24f));
            Disposable(floor);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Back wall";
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = new Vector3(0f, 1.6f, 1.4f);
            wall.transform.localScale = new Vector3(16f, 3.4f, 0.2f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.34f, 0.31f, 0.28f));
            Disposable(wall);
        }

        private static OrderBoardView BuildBoard(Transform parent)
        {
            var go = new GameObject("Order board");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-2.9f, 1.7f, 1.28f);
            go.transform.localScale = Vector3.one * BoardScale;
            Disposable(go);

            var board = go.AddComponent<OrderBoardView>();
            board.CardMaterial = Flat(new Color(0.90f, 0.87f, 0.78f));
            board.AcceptedCardMaterial = Flat(new Color(0.74f, 0.80f, 0.70f));
            return board;
        }

        private static DeliveryReportView BuildReports(Transform parent)
        {
            var go = new GameObject("Delivery notes");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-2.9f, 0.75f, 1.28f);
            go.transform.localScale = Vector3.one * BoardScale;
            Disposable(go);

            var reports = go.AddComponent<DeliveryReportView>();
            reports.NoteMaterial = Flat(new Color(0.92f, 0.90f, 0.82f));
            reports.DisasterNoteMaterial = Flat(new Color(0.88f, 0.72f, 0.68f));
            return reports;
        }

        private static EvidenceRack BuildRack(Transform parent)
        {
            var go = new GameObject("Evidence rack");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(2.6f, 1.0f, 1.1f);
            go.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
            go.transform.localScale = Vector3.one * RackScale;
            Disposable(go);

            var rack = go.AddComponent<EvidenceRack>();
            rack.BlockMaterial = Translucent(new Color(0.70f, 0.78f, 0.72f, 0.16f));
            rack.CavityMaterial = Flat(new Color(0.85f, 0.25f, 0.20f));
            rack.BandMaterial = Flat(new Color(0.10f, 0.10f, 0.12f));
            rack.CardMaterial = Flat(new Color(0.92f, 0.90f, 0.84f));
            rack.ProjectileMaterial = Flat(new Color(0.75f, 0.58f, 0.30f));
            rack.BrassMaterial = Flat(new Color(0.80f, 0.66f, 0.30f));
            rack.PrimerMaterial = Flat(new Color(0.66f, 0.64f, 0.60f));
            rack.MarkMaterial = Flat(new Color(0.20f, 0.18f, 0.16f));
            return rack;
        }

        // ------------------------------------------------------------------
        // The bench: four tools feeding one press
        // ------------------------------------------------------------------

        private static LoadingPress BuildBench(Transform parent)
        {
            var bench = new GameObject("Bench");
            bench.transform.SetParent(parent, false);
            bench.transform.localPosition = new Vector3(0f, 1.15f, 0.9f);
            bench.transform.localScale = Vector3.one * BenchScale;
            Disposable(bench);

            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "Bench top";
            top.transform.SetParent(bench.transform, false);
            top.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            top.transform.localScale = new Vector3(7f, 0.12f, 1.6f);
            top.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.36f, 0.27f, 0.19f));
            Disposable(top);

            var station = BuildLathe(bench.transform);
            var mill = BuildMill(bench.transform);
            var balance = BuildBalance(bench.transform);
            var die = BuildDie(bench.transform, station);

            var press = bench.AddComponent<LoadingPress>();
            press.CoreBench = station;
            press.Mill = mill;
            press.Balance = balance;
            press.Die = die;
            press.BatchSize = 20;
            return press;
        }

        private static LatheStation BuildLathe(Transform parent)
        {
            var go = new GameObject("Core bench");
            go.transform.SetParent(parent, false);
            Disposable(go);

            var station = go.AddComponent<LatheStation>();
            station.Geometry = ProjectileGeometry.Default9mmFmj;
            station.ValidMaterial = Flat(new Color(0.76f, 0.60f, 0.32f));
            station.InvalidMaterial = Flat(new Color(0.85f, 0.25f, 0.20f));

            var rig = new GameObject("Rig").transform;
            rig.SetParent(go.transform, false);
            rig.localScale = Vector3.one * BulletDisplayScale;
            rig.localRotation = Quaternion.Euler(0f, -90f, 0f);
            station.Rig = rig;
            Disposable(rig.gameObject);

            var bullet = new GameObject("Projectile");
            bullet.transform.SetParent(rig, false);
            station.BulletMesh = bullet.AddComponent<MeshFilter>();
            station.BulletRenderer = bullet.AddComponent<MeshRenderer>();
            Disposable(bullet);

            station.Handles = new Transform[LatheStation.OperationCount];
            AddHandle(station, rig, LatheOperation.MeplatDiameter, "Meplat", new Color(0.95f, 0.80f, 0.25f));
            AddHandle(station, rig, LatheOperation.CavityMouth, "Cavity mouth", new Color(0.95f, 0.45f, 0.25f));
            AddHandle(station, rig, LatheOperation.CavityDepth, "Cavity depth", new Color(0.90f, 0.35f, 0.45f));
            AddHandle(station, rig, LatheOperation.NoseLength, "Nose length", new Color(0.35f, 0.75f, 0.95f));
            AddHandle(station, rig, LatheOperation.OgiveShape, "Ogive shape", new Color(0.45f, 0.90f, 0.60f));
            AddHandle(station, rig, LatheOperation.BearingSurface, "Bearing surface", new Color(0.60f, 0.60f, 0.95f));
            AddHandle(station, rig, LatheOperation.BoattailLength, "Boattail length", new Color(0.80f, 0.55f, 0.95f));
            AddHandle(station, rig, LatheOperation.BoattailAngle, "Boattail angle", new Color(0.95f, 0.95f, 0.95f));
            AddHandle(station, rig, LatheOperation.JacketThickness, "Jacket", new Color(0.95f, 0.55f, 0.75f));

            station.ScaleReadout = Label(go.transform, "Scale",
                new Vector3(0f, -0.34f, 0f), 0.012f);
            station.Complaint = Label(go.transform, "Complaint",
                new Vector3(0f, -0.46f, 0f), 0.007f);
            station.Complaint.color = new Color(0.95f, 0.35f, 0.30f);

            station.Rebuild();
            return station;
        }

        private static void AddHandle(
            LatheStation station, Transform rig, LatheOperation operation, string label, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = label;
            go.transform.SetParent(rig, false);
            go.transform.localScale = Vector3.one * 0.0014f;
            go.GetComponent<MeshRenderer>().sharedMaterial = Flat(colour);
            Disposable(go);

            var handle = go.AddComponent<LatheHandle>();
            handle.Operation = operation;
            handle.Station = station;

            station.Handles[(int)operation] = go.transform;
        }

        private static PropellantMill BuildMill(Transform parent)
        {
            var go = new GameObject("Propellant mill");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-2.1f, -0.15f, 0f);
            Disposable(go);

            var mill = go.AddComponent<PropellantMill>();
            mill.GrainMaterial = Flat(new Color(0.24f, 0.22f, 0.20f));

            var tray = new GameObject("Grain tray").transform;
            tray.SetParent(go.transform, false);
            tray.localScale = Vector3.one * 900f;
            mill.GrainTray = tray;
            Disposable(tray.gameObject);

            var pan = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pan.name = "Pan";
            pan.transform.SetParent(go.transform, false);
            pan.transform.localPosition = new Vector3(0f, -0.012f, 0f);
            pan.transform.localScale = new Vector3(0.30f, 0.008f, 0.30f);
            pan.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.50f, 0.52f, 0.56f));
            Disposable(pan);

            mill.Readout = Label(go.transform, "Mill readout", new Vector3(0f, -0.14f, 0f), 0.008f);

            mill.SetShape(GrainShape.Sphere);
            mill.SetWeb(3.5e-5);
            mill.SetDeterrent(0.3);
            return mill;
        }

        private static PowderBalance BuildBalance(Transform parent)
        {
            var go = new GameObject("Powder balance");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-1.1f, 0.25f, 0f);
            Disposable(go);

            var balance = go.AddComponent<PowderBalance>();
            balance.BeamTravel = 0.30;
            balance.MaxSettingGrains = 12.0;

            var beam = new GameObject("Beam").transform;
            beam.SetParent(go.transform, false);
            balance.Beam = beam;
            Disposable(beam.gameObject);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.transform.SetParent(beam, false);
            bar.transform.localPosition = new Vector3(0.10f, 0f, 0f);
            bar.transform.localScale = new Vector3(0.44f, 0.012f, 0.012f);
            bar.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.62f, 0.64f, 0.68f));
            Disposable(bar);

            var poise = GameObject.CreatePrimitive(PrimitiveType.Cube);
            poise.name = "Poise";
            poise.transform.SetParent(beam, false);
            poise.transform.localScale = new Vector3(0.022f, 0.05f, 0.05f);
            poise.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.90f, 0.75f, 0.30f));
            balance.Poise = poise.transform;
            Disposable(poise);

            var pan = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pan.name = "Pan";
            pan.transform.SetParent(beam, false);
            pan.transform.localPosition = new Vector3(-0.13f, -0.05f, 0f);
            pan.transform.localScale = new Vector3(0.10f, 0.012f, 0.10f);
            pan.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.55f, 0.58f, 0.62f));
            balance.Pan = pan.transform;
            Disposable(pan);

            balance.BeamReadout = Label(go.transform, "Beam readout", new Vector3(0.10f, -0.16f, 0f), 0.010f);

            balance.SettingGrains = 5.5;
            balance.Trickle(5.5);
            return balance;
        }

        private static SeatingStop BuildDie(Transform parent, LatheStation station)
        {
            var go = new GameObject("Seating die");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(1.6f, 0f, 0f);
            Disposable(go);

            var die = go.AddComponent<SeatingStop>();
            die.Projectile = station.Geometry;

            var rig = new GameObject("Rig").transform;
            rig.SetParent(go.transform, false);
            rig.localScale = Vector3.one * BulletDisplayScale;
            rig.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Disposable(rig.gameObject);

            var casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            casing.name = "Case";
            casing.transform.SetParent(rig, false);
            casing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            casing.transform.localPosition = new Vector3(0f, 0f, -(float)(die.CaseLength * 0.5));
            casing.transform.localScale = new Vector3(0.0095f, (float)(die.CaseLength * 0.5), 0.0095f);
            casing.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.72f, 0.60f, 0.25f));
            Disposable(casing);

            var bullet = new GameObject("Bullet");
            bullet.transform.SetParent(rig, false);
            bullet.AddComponent<MeshFilter>().sharedMesh =
                Krofken.Ballistics.UnityIntegration.ProjectileMeshBuilder.Create(die.Projectile, 24, 24, 0.0);
            bullet.AddComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.78f, 0.62f, 0.34f));
            die.SeatedBullet = bullet.transform;
            Disposable(bullet);

            var stop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stop.name = "Stop";
            stop.transform.SetParent(rig, false);
            stop.transform.localScale = new Vector3(0.011f, 0.011f, 0.0012f);
            stop.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.85f, 0.35f, 0.35f));
            die.Stop = stop.transform;
            Disposable(stop);

            die.DepthReadout = Label(go.transform, "Die readout", new Vector3(0f, -0.16f, 0f), 0.008f);
            die.Depth = 0.0030;
            return die;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Puts every action where it belongs: on the object that performs it.
        ///
        /// There is no control panel. You pull the handle AT the press, you fire AT the
        /// range because you have to walk out there, you hand a batch over AT the
        /// counter, and you read the board by standing in front of it. That the shop is
        /// a room you cross is not decoration — it is why the evidence rack works and
        /// why a test costs you something.
        /// </summary>
        private static void BuildStationControls(Transform parent, WorkshopController shop)
        {
            // The press handle: a lever standing off the end of the bench.
            var lever = Fixture(parent, "Press handle", new Vector3(1.35f, 1.35f, 0.55f),
                new Vector3(0.09f, 0.52f, 0.09f), new Color(0.72f, 0.32f, 0.22f));
            Use(lever, "pull the press handle", 2.2f, shop.PullHandle);

            // The counter by the door, where a customer takes their box away.
            var counter = Fixture(parent, "Counter", new Vector3(-2.2f, 0.5f, 0.2f),
                new Vector3(1.5f, 1.0f, 0.7f), new Color(0.34f, 0.26f, 0.19f));
            Use(counter, "hand the batch over", 2.4f, shop.DeliverBatch);

            // The board itself is what you take a job from.
            var boardFace = Fixture(parent, "Board face", new Vector3(-2.9f, 1.7f, 1.18f),
                new Vector3(1.5f, 1.2f, 0.06f), new Color(0.42f, 0.33f, 0.24f));
            Use(boardFace, "take the next job", 2.6f, shop.TakeJob);

            // The firing point, out at the yard end by the rack.
            var bench = Fixture(parent, "Firing point", new Vector3(3.4f, 0.55f, -0.4f),
                new Vector3(1.1f, 1.1f, 0.6f), new Color(0.30f, 0.30f, 0.32f));
            Use(bench, "fire one into the block", 2.4f, shop.FireOne);

            // The cot in the corner. Sleeping is what resolves the night.
            var cot = Fixture(parent, "Cot", new Vector3(-4.3f, 0.28f, -0.6f),
                new Vector3(0.9f, 0.55f, 2.0f), new Color(0.38f, 0.34f, 0.42f));
            Use(cot, "turn in for the night", 2.6f, shop.Advance);
        }

        private static GameObject Fixture(
            Transform parent, string name, Vector3 position, Vector3 size, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Flat(colour);
            Disposable(go);
            return go;
        }

        private static void Use(GameObject go, string prompt, float reach, System.Action action)
        {
            var interactable = go.AddComponent<Interactable>();
            interactable.Prompt = prompt;
            interactable.Reach = reach;
            interactable.Used = action;
        }

        private static TextMesh Label(Transform parent, string name, Vector3 position, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            Disposable(go);

            var text = go.AddComponent<TextMesh>();
            text.characterSize = size;
            text.fontSize = 72;
            text.color = new Color(0.95f, 0.93f, 0.88f);
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            return text;
        }

        /// <summary>
        /// Keeps an EDITOR-TIME preview out of the scene file.
        ///
        /// Only in the editor, and that condition is the whole point. At runtime nothing
        /// is serialised anyway, so the flag buys nothing there — and DontSave objects
        /// are torn down on a domain reload, which is what made the shop and then the
        /// player evaporate the moment Play was pressed. The flag was correct for
        /// previews and wrong for the game; it now applies only where it was right.
        /// </summary>
        private static void Disposable(GameObject go)
        {
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
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
