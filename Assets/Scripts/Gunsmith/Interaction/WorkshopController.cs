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

        private void Start() => Refresh();

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
        }
    }
}
