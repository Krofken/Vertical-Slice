using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Gunsmith.Range;
using Krofken.Ballistics;
using UnityEngine;
using Defaults = Gunsmith.Interaction.WorkshopPalette.Defaults;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// Builds the shop.
    ///
    /// TWO MODES, and the difference is the whole reason this class was reworked:
    ///
    ///   DISPOSABLE (default) — everything is tagged <c>HideFlags.DontSave</c> in the
    ///     editor, so a preview can never leak into the scene file or raise a save
    ///     prompt. Correct for a look-and-throw-away preview. At runtime the flag is
    ///     skipped, because nothing is serialised in Play mode anyway and DontSave
    ///     objects are destroyed by a domain reload.
    ///
    ///   PERSISTENT — real objects with real materials, meant to be saved into the
    ///     scene and turned into prefabs. THIS is what makes the shop editable by hand.
    ///     Until it existed the workshop only came into being when you pressed Play, so
    ///     there was nothing to move, nothing to re-mesh, and nothing to art-direct.
    ///
    /// Construction stays in the RUNTIME assembly, not an editor tool. That is not
    /// stylistic: it was moved here because an editor-only builder meant the shop did
    /// not exist in a build or in Play mode at all. The persistent path is driven from
    /// an editor tool, but the code that knows how to make a bench lives here.
    ///
    /// The stations do not share a natural scale and cannot be made to — a 13 mm bullet
    /// needs exaggerating about forty times before it is worth looking at, while a gel
    /// block is already seventy centimetres. Each group gets its own factor, chosen for
    /// how big it should READ. Only display rigs are scaled; nothing a solver touches is
    /// affected.
    /// </summary>
    public sealed class WorkshopBuilder
    {
        private const float RackScale = 0.90f;
        private const float BoardScale = 0.85f;

        /// <summary>
        /// TRUE SIZE. Not a display factor — there is deliberately no longer one.
        ///
        /// The bench used to inflate its cartridges 40x and its powder 900x, so a 13 mm
        /// round rendered 23 cm long and a grain of powder came out the size of a
        /// baseball. It was done for an honest reason — a real cartridge on a waist-high
        /// bench seen from standing is about four pixels — but the cure was worse than
        /// the disease, and no amount of tuning fixes it: a cartridge scaled to be
        /// legible from standing height has stopped being a cartridge.
        ///
        /// A gunsmith does not enlarge the round. He leans in. <see cref="StationView"/>
        /// moves the eye to the work and narrows the field of view instead, which costs
        /// nothing and is what a person actually does.
        /// </summary>
        private const float TrueScale = 1f;

        /// <summary>
        /// Powder is the one exception, and a small one.
        ///
        /// The sim's web thickness is a BURN DISTANCE, not the size of a granule — 35
        /// micrometres for a fast pistol powder — so drawing spheres of that diameter
        /// would show nothing at all. This lifts them to about a millimetre, which is
        /// roughly what a real ball-powder granule measures. It is a correction towards
        /// life size, not away from it.
        /// </summary>
        private const float GrainLegibility = 14f;

        private readonly WorkshopPalette _palette;
        private readonly bool _persistent;

        private WorkshopBuilder(WorkshopPalette palette, bool persistent)
        {
            _palette = palette;
            _persistent = persistent;
        }

        /// <summary>
        /// Builds the whole shop under a parent and returns its controller.
        /// </summary>
        /// <param name="parent">Where to hang it.</param>
        /// <param name="palette">Surface materials. Null, or slots left empty, fall back
        /// to the flat colours the shop used before there was a palette.</param>
        /// <param name="persistent">True to produce real, saveable objects. False for a
        /// disposable preview.</param>
        public static WorkshopController Build(
            Transform parent, WorkshopPalette palette = null, bool persistent = false)
            => new WorkshopBuilder(palette, persistent).BuildShop(parent);

        private WorkshopController BuildShop(Transform parent)
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

            // NO STATUS READOUT. Deliberately none.
            //
            // This used to be a block of text floating on the back wall listing the day,
            // the purse and the primer count. Nobody asked for it, it reads as graffiti
            // scrawled on a gunsmith's wall, and a wall is not where a HUD goes. It was
            // put there by me to stop it clipping off the edge of the screen, which
            // solved the clipping and created a worse problem.
            //
            // WorkshopController.Refresh already handles a null Status, so the shop runs
            // without one. Where this information belongs — a HUD, a ledger on the desk,
            // a calendar by the door — is a design decision, not something to guess at.
            shop.Status = null;

            BuildStationControls(root.transform, shop);

            return shop;
        }

        // ------------------------------------------------------------------

        /// <summary>Something to stand on. Without it the character controller falls
        /// forever the moment the game starts.</summary>
        private void BuildFloor(Transform parent)
        {
            Primitive(PrimitiveType.Cube, parent, "Floor",
                new Vector3(0f, -0.1f, 0f), new Vector3(16f, 0.2f, 12f),
                Mat(_palette?.Floor, Defaults.Floor));

            Primitive(PrimitiveType.Cube, parent, "Back wall",
                new Vector3(0f, 1.6f, 1.4f), new Vector3(16f, 3.4f, 0.2f),
                Mat(_palette?.Wall, Defaults.Wall));
        }

        private OrderBoardView BuildBoard(Transform parent)
        {
            // IN FRONT of the backing board, not behind it. The wall is at z = 1.30 and
            // the cork the cards are pinned to sits just off it; a card at 1.28 with the
            // backing at 1.18 was hidden behind the very thing it is pinned to.
            var go = Empty(parent, "Order board",
                new Vector3(-2.9f, 1.7f, 1.22f), Vector3.one * BoardScale);

            var board = go.AddComponent<OrderBoardView>();
            board.CardMaterial = Mat(_palette?.Card, Defaults.Card);
            board.AcceptedCardMaterial = Mat(_palette?.CardAccepted, Defaults.CardAccepted);
            return board;
        }

        private DeliveryReportView BuildReports(Transform parent)
        {
            var go = Empty(parent, "Delivery notes",
                new Vector3(-2.9f, 0.75f, 1.28f), Vector3.one * BoardScale);

            var reports = go.AddComponent<DeliveryReportView>();
            reports.NoteMaterial = Mat(_palette?.Note, Defaults.Note);
            reports.DisasterNoteMaterial = Mat(_palette?.NoteDisaster, Defaults.NoteDisaster);
            return reports;
        }

        private EvidenceRack BuildRack(Transform parent)
        {
            var go = Empty(parent, "Evidence rack",
                new Vector3(2.6f, 1.0f, 1.1f), Vector3.one * RackScale);
            go.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);

            var rack = go.AddComponent<EvidenceRack>();

            // The block is the only translucent surface in the shop; the cavity is a
            // solid suspended inside it, which is why it reads as a wound channel.
            rack.BlockMaterial = _palette?.GelBlock != null
                ? _palette.GelBlock
                : WorkshopPalette.Translucent(Defaults.GelBlock);

            rack.CavityMaterial = Mat(_palette?.Cavity, Defaults.Cavity);
            rack.BandMaterial = Mat(_palette?.DepthBand, Defaults.DepthBand);
            rack.CardMaterial = Mat(_palette?.WitnessCard, Defaults.WitnessCard);
            rack.ProjectileMaterial = Mat(_palette?.RecoveredSlug, Defaults.RecoveredSlug);
            rack.BrassMaterial = Mat(_palette?.Brass, Defaults.Brass);
            rack.PrimerMaterial = Mat(_palette?.Primer, Defaults.Primer);
            rack.MarkMaterial = Mat(_palette?.Mark, Defaults.Mark);
            return rack;
        }

        // ------------------------------------------------------------------
        // The bench: four tools feeding one press
        // ------------------------------------------------------------------

        private LoadingPress BuildBench(Transform parent)
        {
            // A real bench: 2.4 m long, 70 cm deep, work surface at 92 cm. Everything on
            // it is placed in metres from here on, because everything on it is now the
            // size it really is.
            var bench = Empty(parent, "Bench",
                new Vector3(0f, 0.92f, 0.85f), Vector3.one * TrueScale);

            Primitive(PrimitiveType.Cube, bench.transform, "Bench top",
                new Vector3(0f, -0.03f, 0f), new Vector3(2.4f, 0.06f, 0.7f),
                Mat(_palette?.BenchTop, Defaults.BenchTop));

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

            // THE ONE THING THE PRESS WAS NEVER GIVEN. Every station that feeds it was
            // wired up and the press's own readout was left null, so pulling the handle
            // consumed materials, put rounds on the shelf and told nobody. It sits beside
            // the handle at the right-hand end of the bench, because that is where the
            // player is standing when they pull it.
            //
            // Named, because LoadingPress adopts it by name when it was not handed one.
            press.Readout = Label(bench.transform, "Press readout",
                new Vector3(0.95f, 0.34f, -0.24f), 0.006f);
            return press;
        }

        private LatheStation BuildLathe(Transform parent)
        {
            var go = Empty(parent, "Core bench", new Vector3(0.25f, 0.02f, 0f), Vector3.one);
            LeanIn(go, "turn the bullet", new Vector3(0f, 0.11f, -0.13f), fieldOfView: 18f);

            var station = go.AddComponent<LatheStation>();
            station.Geometry = ProjectileGeometry.Default9mmFmj;
            station.ValidMaterial = Mat(_palette?.Projectile, Defaults.Projectile);
            station.InvalidMaterial = Mat(_palette?.ProjectileInvalid, Defaults.ProjectileInvalid);

            var rig = Empty(go.transform, "Rig", Vector3.zero, Vector3.one * TrueScale).transform;
            rig.localRotation = Quaternion.Euler(0f, -90f, 0f);
            station.Rig = rig;

            var bullet = Empty(rig, "Projectile", Vector3.zero, Vector3.one);
            station.BulletMesh = bullet.AddComponent<MeshFilter>();
            station.BulletRenderer = bullet.AddComponent<MeshRenderer>();

            station.Handles = new Transform[LatheStation.OperationCount];
            AddHandle(station, rig, LatheOperation.MeplatDiameter, "Meplat");
            AddHandle(station, rig, LatheOperation.CavityMouth, "Cavity mouth");
            AddHandle(station, rig, LatheOperation.CavityDepth, "Cavity depth");
            AddHandle(station, rig, LatheOperation.NoseLength, "Nose length");
            AddHandle(station, rig, LatheOperation.OgiveShape, "Ogive shape");
            AddHandle(station, rig, LatheOperation.BearingSurface, "Bearing surface");
            AddHandle(station, rig, LatheOperation.BoattailLength, "Boattail length");
            AddHandle(station, rig, LatheOperation.BoattailAngle, "Boattail angle");
            AddHandle(station, rig, LatheOperation.JacketThickness, "Jacket");

            station.ScaleReadout = Label(go.transform, "Scale", new Vector3(0f, 0.055f, 0f), 0.0016f);
            station.Complaint = Label(go.transform, "Complaint", new Vector3(0f, -0.030f, 0f), 0.0011f);
            station.Complaint.color = new Color(0.95f, 0.35f, 0.30f);

            station.Rebuild();
            return station;
        }

        private void AddHandle(LatheStation station, Transform rig, LatheOperation operation, string label)
        {
            int index = (int)operation;
            Color fallback = index < Defaults.Handles.Length ? Defaults.Handles[index] : Color.white;

            Material material = _palette != null
                ? _palette.ResolveHandle(index, fallback)
                : WorkshopPalette.Flat(fallback);

            // 2.5 mm, which is a bead you can pinch. It has to be grabbable while leaning
            // in over a 13 mm bullet without burying the bullet underneath it.
            var go = Primitive(PrimitiveType.Sphere, rig, label,
                Vector3.zero, Vector3.one * 0.0025f, material);

            var handle = go.AddComponent<LatheHandle>();
            handle.Operation = operation;
            handle.Station = station;

            station.Handles[index] = go.transform;
        }

        private PropellantMill BuildMill(Transform parent)
        {
            var go = Empty(parent, "Propellant mill", new Vector3(-0.85f, 0.01f, 0f), Vector3.one);
            LeanIn(go, "mill the powder", new Vector3(0f, 0.13f, -0.15f), fieldOfView: 24f);

            var mill = go.AddComponent<PropellantMill>();
            mill.GrainMaterial = Mat(_palette?.PowderGrain, Defaults.PowderGrain);

            var tray = Empty(go.transform, "Grain tray",
                new Vector3(0f, 0.004f, 0f), Vector3.one * GrainLegibility).transform;
            mill.GrainTray = tray;

            // An 8 cm sample pan, which is what one actually is.
            Primitive(PrimitiveType.Cylinder, go.transform, "Pan",
                new Vector3(0f, 0f, 0f), new Vector3(0.08f, 0.004f, 0.08f),
                Mat(_palette?.Metal, Defaults.Metal));

            mill.Readout = Label(go.transform, "Mill readout", new Vector3(0f, 0.045f, 0f), 0.0013f);

            // THE THREE THINGS THE MILL MAKES, each on something you can work. Before these
            // the station was a readout with no inputs: SetWeb, SetDeterrent and NextShape were
            // reachable only from the builder and the tests, so a player could look at a recipe
            // and change nothing about it.
            // Each slide gets a TRACK. The control places itself along it from the mill's
            // current value, so where it sits always states what it is set to.
            var grindTrack = new Vector3(-0.062f, 0.014f, 0.052f);
            var wheel = Primitive(PrimitiveType.Sphere, go.transform, "Grinding wheel",
                grindTrack, Vector3.one * 0.014f, Mat(_palette?.Metal, Defaults.Metal));
            Control(wheel, mill, go.transform, MillAdjustment.Grind, grindTrack, travel: 0.124f);

            var drumTrack = new Vector3(0.062f, 0.014f, -0.045f);
            var drum = Primitive(PrimitiveType.Sphere, go.transform, "Coating drum",
                drumTrack, Vector3.one * 0.014f, Mat(_palette?.Poise, Defaults.Poise));
            Control(drum, mill, go.transform, MillAdjustment.Drum, drumTrack, travel: 0.090f);

            var diePlate = new Vector3(0f, 0.014f, -0.058f);
            var die = Primitive(PrimitiveType.Cube, go.transform, "Extrusion die",
                diePlate, new Vector3(0.020f, 0.007f, 0.020f),
                Mat(_palette?.Fixture, Defaults.Fixture));
            Control(die, mill, go.transform, MillAdjustment.Die, diePlate, travel: 0f);

            mill.SetShape(GrainShape.Sphere);
            mill.SetWeb(3.5e-5);
            mill.SetDeterrent(0.3);
            return mill;
        }

        private static void Control(
            GameObject go, PropellantMill mill, Transform rig, MillAdjustment adjustment,
            Vector3 trackStart, float travel)
        {
            var control = go.AddComponent<MillControl>();
            control.Adjustment = adjustment;
            control.Mill = mill;
            control.Rig = rig;
            control.TrackStart = trackStart;
            control.Travel = travel;
        }

        private PowderBalance BuildBalance(Transform parent)
        {
            var go = Empty(parent, "Powder balance", new Vector3(-0.30f, 0.02f, 0f), Vector3.one);
            LeanIn(go, "pour the charge", new Vector3(0.03f, 0.12f, -0.16f), fieldOfView: 26f);

            var balance = go.AddComponent<PowderBalance>();
            balance.BeamTravel = 0.30;
            balance.MaxSettingGrains = 12.0;

            var beam = Empty(go.transform, "Beam", Vector3.zero, Vector3.one).transform;
            balance.Beam = beam;

            // A 26 cm beam, which is about what a real powder scale has.
            Primitive(PrimitiveType.Cube, beam, "Bar",
                new Vector3(0.06f, 0.03f, 0f), new Vector3(0.26f, 0.007f, 0.007f),
                Mat(_palette?.Metal, Defaults.Metal));

            var poise = Primitive(PrimitiveType.Cube, beam, "Poise",
                new Vector3(0f, 0.03f, 0f), new Vector3(0.012f, 0.026f, 0.026f),
                Mat(_palette?.Poise, Defaults.Poise));
            balance.Poise = poise.transform;

            var pan = Primitive(PrimitiveType.Cylinder, beam, "Pan",
                new Vector3(-0.075f, 0.012f, 0f), new Vector3(0.055f, 0.005f, 0.055f),
                Mat(_palette?.Metal, Defaults.Metal));
            balance.Pan = pan.transform;

            balance.BeamReadout = Label(go.transform, "Beam readout", new Vector3(0.06f, -0.012f, 0f), 0.0015f);

            // THE DISPENSER IS FITTED BY PowderBalance ITSELF, not here. It has to be, because the
            // shop the player walks around is a frozen prefab this builder never runs for — so a
            // machine built here would exist only in a freshly-built shop. Same reason the press
            // fits its own readout and the die its own handle.

            // The case starts EMPTY. It used to be handed 5.5 grains at construction, which is the
            // reference charge, so every load began already correct and charging was something a
            // player never had to do.
            balance.Empty();
            return balance;
        }

        private SeatingStop BuildDie(Transform parent, LatheStation station)
        {
            var go = Empty(parent, "Seating die", new Vector3(0.80f, 0.01f, 0f), Vector3.one);
            LeanIn(go, "set the seating depth", new Vector3(0f, 0.10f, -0.12f), fieldOfView: 18f);

            var die = go.AddComponent<SeatingStop>();
            die.Projectile = station.Geometry;

            var rig = Empty(go.transform, "Rig", Vector3.zero, Vector3.one * TrueScale).transform;
            rig.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var casing = Primitive(PrimitiveType.Cylinder, rig, "Case",
                new Vector3(0f, 0f, -(float)(die.CaseLength * 0.5)),
                new Vector3(0.0095f, (float)(die.CaseLength * 0.5), 0.0095f),
                Mat(_palette?.Case, Defaults.Case));
            casing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var bullet = Empty(rig, "Bullet", Vector3.zero, Vector3.one);
            bullet.AddComponent<MeshFilter>().sharedMesh =
                Krofken.Ballistics.UnityIntegration.ProjectileMeshBuilder.Create(die.Projectile, 24, 24, 0.0);
            bullet.AddComponent<MeshRenderer>().sharedMaterial =
                Mat(_palette?.Projectile, Defaults.Projectile);
            die.SeatedBullet = bullet.transform;

            var stop = Primitive(PrimitiveType.Cube, rig, "Stop",
                Vector3.zero, new Vector3(0.011f, 0.011f, 0.0012f),
                Mat(_palette?.SeatingStop, Defaults.SeatingStop));
            die.Stop = stop.transform;

            // MAKE IT GRABBABLE. The stop had a collider and a SetStop method and nothing
            // that connected the two, so the die leaned you in over a tool you could not
            // operate. The handle IS the stop rather than a bead beside it — you take hold
            // of the thing itself, which is what you do to a real die body.
            var seating = stop.AddComponent<SeatingHandle>();
            seating.Die = die;
            seating.Rig = rig;

            die.DepthReadout = Label(go.transform, "Die readout", new Vector3(0f, -0.022f, 0f), 0.0013f);
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
        private void BuildStationControls(Transform parent, WorkshopController shop)
        {
            var fixtureMaterial = Mat(_palette?.Fixture, Defaults.Fixture);

            var lever = Primitive(PrimitiveType.Cube, parent, "Press handle",
                new Vector3(1.35f, 1.35f, 0.55f), new Vector3(0.09f, 0.52f, 0.09f), fixtureMaterial);
            Use(lever, "pull the press handle", 2.2f, ShopAction.PullPressHandle);

            var counter = Primitive(PrimitiveType.Cube, parent, "Counter",
                new Vector3(-2.2f, 0.5f, 0.2f), new Vector3(1.5f, 1.0f, 0.7f), fixtureMaterial);
            Use(counter, "hand the batch over", 2.4f, ShopAction.HandOverBatch);

            // The cork behind the cards, flat against the wall. Must sit at a HIGHER z
            // than the cards or it draws over them.
            var boardFace = Primitive(PrimitiveType.Cube, parent, "Board face",
                new Vector3(-2.9f, 1.7f, 1.27f), new Vector3(1.9f, 1.5f, 0.05f), fixtureMaterial);
            Use(boardFace, "take the next job", 2.6f, ShopAction.TakeJob);

            var firing = Primitive(PrimitiveType.Cube, parent, "Firing point",
                new Vector3(3.4f, 0.55f, -0.4f), new Vector3(1.1f, 1.1f, 0.6f), fixtureMaterial);
            Use(firing, "fire one into the block", 2.4f, ShopAction.FireOne);

            // Beside the balance, and deliberately NOT parented to it: a fixture inside a
            // station's hierarchy is found by PlayerInteractor's GetComponentInParent
            // <StationView>, so pressing E on it would both tip the pan and lean the player
            // in or out of the station at the same time.
            var bin = Primitive(PrimitiveType.Cylinder, parent, "Powder bin",
                new Vector3(-0.46f, 0.98f, 0.70f), new Vector3(0.09f, 0.06f, 0.09f),
                fixtureMaterial);
            Use(bin, "tip the pan back into the tin", 2.2f, ShopAction.TipThePan);

            var cot = Primitive(PrimitiveType.Cube, parent, "Cot",
                new Vector3(-4.3f, 0.28f, -0.6f), new Vector3(0.9f, 0.55f, 2.0f), fixtureMaterial);
            Use(cot, "turn in for the night", 2.6f, ShopAction.TurnInForTheNight);

            // Bind immediately so a code-built shop works without waiting for Awake,
            // which never runs in edit mode.
            shop.BindFixtures();
        }

        // ------------------------------------------------------------------
        // Construction helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Makes a station something you can walk up to and lean over.
        ///
        /// This is what pays for true scale. The work stays 13 mm across; pressing E
        /// brings the eye to <paramref name="eyeOffset"/> and narrows the lens, and a
        /// real cartridge fills the screen because you are looking at it from 15 cm
        /// away rather than because somebody made it 23 cm long.
        /// </summary>
        private void LeanIn(GameObject station, string prompt, Vector3 eyeOffset, float fieldOfView)
        {
            var view = station.AddComponent<StationView>();
            view.EyeOffset = eyeOffset;
            view.LookOffset = Vector3.zero;
            view.FieldOfView = fieldOfView;

            // A trigger, not a solid box. It has to be hittable by the interaction ray
            // without becoming a knee-high wall the player cannot walk past — these sit
            // right at bench height, in the middle of the room.
            var box = station.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.02f, 0f);
            box.size = new Vector3(0.17f, 0.10f, 0.17f);
            box.isTrigger = true;

            var interactable = station.AddComponent<Interactable>();
            interactable.Prompt = prompt;
            interactable.Reach = 1.5f;

            // No ShopAction: leaning in IS what this does. WorkshopController skips the
            // "nothing bound this" warning for anything carrying a StationView.
            interactable.Action = ShopAction.None;
        }

        private GameObject Empty(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Disposable(go);
            return go;
        }

        private GameObject Primitive(
            PrimitiveType type, Transform parent, string name,
            Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Disposable(go);
            return go;
        }

        /// <summary>
        /// Marks a fixture as performing one of the shop's actions.
        ///
        /// Sets the SERIALISED enum, not the delegate. The controller binds the actual
        /// method on Awake, so the fixture keeps working after being saved into a
        /// prefab, duplicated, or moved across the room by hand.
        /// </summary>
        private static void Use(GameObject go, string prompt, float reach, ShopAction action)
        {
            var interactable = go.AddComponent<Interactable>();
            interactable.Prompt = prompt;
            interactable.Reach = reach;
            interactable.Action = action;
        }

        private TextMesh Label(Transform parent, string name, Vector3 position, float size)
        {
            var go = Empty(parent, name, position, Vector3.one);

            var text = go.AddComponent<TextMesh>();
            text.characterSize = size;
            text.fontSize = 72;
            text.color = new Color(0.95f, 0.93f, 0.88f);
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            return text;
        }

        /// <summary>
        /// Keeps a DISPOSABLE build out of the scene file.
        ///
        /// Skipped entirely for a persistent build — that is the point of one — and
        /// skipped at runtime, where nothing is serialised anyway and DontSave objects
        /// are torn down by a domain reload. That last case is what once made the whole
        /// shop, and then the player standing in it, evaporate the moment Play was
        /// pressed.
        /// </summary>
        private void Disposable(GameObject go)
        {
            if (_persistent) return;
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
        }

        /// <summary>Palette slot if assigned, flat fallback colour otherwise.</summary>
        private static Material Mat(Material assigned, Color fallback)
            => WorkshopPalette.Resolve(assigned, fallback);
    }
}
