using Gunsmith.Crafting;
using Gunsmith.GameLoop;
using Gunsmith.Orders;
using Gunsmith.Range;
using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Interaction
{
    /// <summary>
    /// The shop, joined up and playable.
    ///
    /// Everything this drives already existed and was already tested; what was missing
    /// was somebody able to sit down and DO it. This is the piece that turns a verified
    /// set of systems into a night you can actually work through:
    ///
    ///   take a job off the board  ->  mill a powder, turn a bullet, weigh a charge,
    ///   seat it  ->  pull the press handle  ->  fire one at the block  ->  read the
    ///   block and the brass  ->  hand the batch over  ->  sleep  ->  find out
    ///
    /// It holds no rules of its own. Every decision still lives in GunsmithGame, the
    /// stations and the evaluator — this only wires them to things you can click.
    /// </summary>
    [AddComponentMenu("Gunsmith/Workshop Controller")]
    public sealed class WorkshopController : MonoBehaviour
    {
        [Header("Systems")]
        public GunsmithGameBehaviour GameBehaviour;

        [Header("Stations")]
        public OrderBoardView Board;
        public LoadingPress Press;
        public RangeStation Yard;
        public EvidenceRack Rack;
        public DeliveryReportView Reports;

        [Header("Readout")]
        [Tooltip("The day and phase, and what the shop is short of. No performance figures.")]
        public TextMesh Status;

        [Tooltip("How much wall the status may occupy, metres. It is scaled to fit this, " +
                 "so a longer line can never run off the edge of the screen.")]
        public Vector2 StatusSize = new Vector2(1.30f, 0.70f);

        private Vector3 _statusRestScale;
        private bool _statusScaleKnown;

        /// <summary>
        /// The run in progress.
        ///
        /// <see cref="GunsmithGameBehaviour"/> starts its game in Awake, which does not
        /// run in edit mode — so a shop assembled by the setup tool has no game until
        /// Play is pressed. Starting one on demand means the workshop can be driven
        /// from a menu item or a test as well as by a player, and nothing silently
        /// does nothing.
        /// </summary>
        private GunsmithGame Game
        {
            get
            {
                if (GameBehaviour == null) return null;
                if (GameBehaviour.Game == null) GameBehaviour.StartNewGame();
                return GameBehaviour.Game;
            }
        }

        private int _batch;

        private void Awake()
        {
            AdoptStations();
            BindFixtures();
        }

        /// <summary>
        /// Finds any station whose reference did not survive.
        ///
        /// "[Range] no yard." on every attempt to fire was this: <see cref="Yard"/> was
        /// null, so the shop refused before it got anywhere near the ballistics. The
        /// wiring is done by the builder at construction time, which is fine for a shop
        /// built from code and useless for one restored from a prefab or reassembled by
        /// hand — exactly the situation the authoring pass created.
        ///
        /// Same lesson as the fixtures and the bootstrap's Shop field: a component must
        /// be able to find its own parts rather than trusting whoever built it. Anything
        /// already assigned is left alone, so a hand-wired override still wins.
        /// </summary>
        public void AdoptStations()
        {
            if (GameBehaviour == null) GameBehaviour = GetComponentInChildren<GunsmithGameBehaviour>(true);
            if (Board == null) Board = GetComponentInChildren<OrderBoardView>(true);
            if (Press == null) Press = GetComponentInChildren<LoadingPress>(true);
            if (Yard == null) Yard = GetComponentInChildren<RangeStation>(true);
            if (Rack == null) Rack = GetComponentInChildren<EvidenceRack>(true);
            if (Reports == null) Reports = GetComponentInChildren<DeliveryReportView>(true);

            // The yard needs the rack to put its evidence on.
            if (Yard != null && Yard.Rack == null) Yard.Rack = Rack;

            if (Yard == null)
                Debug.LogWarning("[Shop] No RangeStation anywhere under the shop — " +
                                 "firing will refuse. Rebuild the workshop.", this);
        }

        private void Start() => Refresh();

        /// <summary>
        /// Points every fixture in the shop at the method it performs.
        ///
        /// THE CONTROLLER WIRES ITSELF, rather than relying on whoever built it. That
        /// distinction is the difference between a shop that works and one that only
        /// looks like it does: <see cref="Interactable.Used"/> is a delegate and cannot
        /// be serialised, so a workshop restored from a prefab or placed by hand used to
        /// come up with every fixture present, highlighted, promptable — and inert. No
        /// error, no missing object, just nothing happening when you pulled the handle.
        ///
        /// Binding from the serialised <see cref="Interactable.Action"/> on Awake means
        /// it does not matter how the fixture got there. Duplicate the press handle,
        /// move it across the room, replace its cube with a real lever model: it still
        /// works, because the enum came with it.
        /// </summary>
        public void BindFixtures()
        {
            foreach (var fixture in GetComponentsInChildren<Interactable>(includeInactive: true))
            {
                switch (fixture.Action)
                {
                    case ShopAction.TakeJob: fixture.Used = TakeJob; break;
                    case ShopAction.PullPressHandle: fixture.Used = PullHandle; break;
                    case ShopAction.FireOne: fixture.Used = FireOne; break;
                    case ShopAction.HandOverBatch: fixture.Used = DeliverBatch; break;
                    case ShopAction.TurnInForTheNight: fixture.Used = Advance; break;

                    case ShopAction.None:
                        // A station you lean over needs no action — leaning in is the
                        // action. Everything else with no action and nothing bound is a
                        // fixture that will silently do nothing when used.
                        if (fixture.GetComponentInParent<StationView>() != null) break;

                        if (fixture.Used == null)
                            Debug.LogWarning(
                                $"[Shop] '{fixture.name}' has no action set and nothing bound it. " +
                                "Set Interactable.Action in the inspector.", fixture);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------
        // The actions, one per thing you can click
        // ------------------------------------------------------------------

        /// <summary>Takes the first job still on the board.</summary>
        public void TakeJob()
        {
            var game = Game;
            if (game == null) { Debug.Log("[Shop] no game running."); return; }
            if (game.Board.Count == 0) { Debug.Log("[Board] nothing left to take."); return; }

            game.AcceptOrder(game.Board[0]);
            Refresh();
        }

        /// <summary>Pulls the press handle: composes what the bench is set to and makes
        /// a batch of it.</summary>
        public void PullHandle()
        {
            var game = Game;
            if (game == null || Press == null) { Debug.Log("[Press] nothing set up."); return; }

            _batch++;
            string id = $"load_{_batch}";

            var result = Press.PressBatch(game, id, $"Load {_batch}");
            Debug.Log($"[Press] {(result.Success ? $"made {result.RoundsProduced} rounds" : result.Message)}");

            Refresh();
        }

        /// <summary>Fires one round of the most recent load into a block.</summary>
        public void FireOne()
        {
            var game = Game;
            if (game == null || Yard == null) { Debug.Log("[Range] no yard."); return; }
            if (_batch == 0) { Debug.Log("[Range] nothing loaded yet - press a batch first."); return; }

            if (!Yard.TryFire(game, $"load_{_batch}", out var entry, out string why))
            {
                Debug.Log($"[Range] {why}");
                return;
            }

            // The two readouts, both of them things rather than numbers.
            Debug.Log($"[Range] shot {entry.ShotNumber}: the brass says {Yard.LastCase.Describe()}");
            Refresh();
        }

        /// <summary>Hands the most recent load over against the first job taken.</summary>
        public void DeliverBatch()
        {
            var game = Game;
            if (game == null || _batch == 0) { Debug.Log("[Counter] nothing to hand over."); return; }

            var design = game.Designs.Get($"load_{_batch}");
            if (design == null) { Debug.Log("[Counter] that load is not in the book."); return; }

            foreach (var accepted in game.Accepted)
            {
                if (accepted.Submitted) continue;

                game.SubmitOrder(accepted, design, out string error);
                if (!string.IsNullOrEmpty(error)) Debug.Log($"[Counter] {error}");
                break;
            }

            Refresh();
        }

        /// <summary>Moves the day on. At Dawn the deliveries resolve and the notes go up.</summary>
        public void Advance()
        {
            var game = Game;
            if (game == null) return;

            game.AdvancePhase();

            if (Reports != null) Reports.Show(game);
            Refresh();
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Redraws the board and the status card.
        ///
        /// The status may say what day it is and what the shop has run out of. It may
        /// not say anything about how a round will perform — that is what the range and
        /// the morning are for.
        /// </summary>
        public void Refresh()
        {
            var game = Game;
            if (game == null) return;

            if (Board != null) Board.Show(game);

            if (Status == null) return;

            var text = new System.Text.StringBuilder();
            text.AppendLine($"Day {game.Day} — {game.Phase}");
            text.AppendLine($"{game.Inventory.Funds} coin, reputation {game.Reputation}");
            text.AppendLine($"{game.Inventory.Primers} primers, " +
                            $"{game.Inventory.CasesOf(CartridgeCaseLibrary.NineMillimetre)} cases");

            if (_batch > 0)
                text.AppendLine($"on the shelf: {game.Workshop.RoundsOf($"load_{_batch}")} of Load {_batch}");

            int taken = 0, delivered = 0;
            foreach (var accepted in game.Accepted)
            {
                taken++;
                if (accepted.Submitted) delivered++;
            }

            text.AppendLine($"{taken} jobs taken, {delivered} handed over");
            text.Append($"{game.Notebook.Count} shots fired");

            Status.text = text.ToString();

            // FIT IT AFTER WRITING IT. The readout changes every refresh — a longer
            // phase name, a four-digit purse, a design name on the shelf line — so a
            // character size that fits today runs off the screen tomorrow. That is
            // exactly what it did.
            //
            // Reset to the resting scale first: Fit multiplies, so re-fitting an already
            // shrunk label every refresh would shrink it away to nothing.
            if (!_statusScaleKnown)
            {
                _statusRestScale = Status.transform.localScale;
                _statusScaleKnown = true;
            }

            Status.transform.localScale = _statusRestScale;
            TextFit.Fit(Status, StatusSize);
        }
    }
}
