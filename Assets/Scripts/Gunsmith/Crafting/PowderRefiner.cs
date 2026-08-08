using Gunsmith.Interaction;
using Krofken.Ballistics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The powder refiner: a machine that presses a recipe. Pick a row, change it, press REFINE, and
    /// the mill makes that powder.
    ///
    /// WHY A MACHINE RATHER THAN GRINDING. Grinding was going to be an act — crank it, watch the
    /// granules break down, stop when you like — and that is the third time this bench would have
    /// asked the player to arrive at a number by feel through a mechanism that has to be perfect
    /// before it is usable at all. The poise failed that way and the pour failed that way.
    ///
    /// The deeper reason it is fine here: the mill's four properties are a SPECIFICATION, not a
    /// feel. Base chemistry, grain form, web and deterrent coat are what a powder IS. Stating them
    /// is honest in a way that pretending to grind them was not.
    ///
    /// WHAT KEEPS IT FROM BEING A FORM is that the pan answers. Change the web and the granules
    /// visibly change — more of them and smaller, at constant volume, because grinding rearranges
    /// powder rather than creating it. Change the die and their shape changes. The screen states
    /// the recipe; the sample shows it. Without that this would be tax software and should be
    /// argued against.
    ///
    /// WHAT IT MUST NEVER SAY: burn rate, impetus, expected pressure, or anything else about what
    /// the powder will DO. Web in millimetres and passes in a drum are dimensions of the thing in
    /// the pan, exactly like the lathe's dimensions. The player learns that fine powder bursts
    /// cases by loading one and reading the brass.
    /// </summary>
    [AddComponentMenu("Gunsmith/Powder Refiner")]
    public sealed class PowderRefiner : MonoBehaviour
    {
        /// <summary>Which line of the recipe the buttons are working on.</summary>
        public enum Row
        {
            Base = 0,
            Grain = 1,
            Web = 2,
            Drum = 3
        }

        [Header("Feeds")]
        public PropellantMill Mill;

        [Header("Screen")]
        public TextMesh Labels;
        public TextMesh Values;
        public Renderer Glass;

        [Range(0.3f, 0.7f)] public float ColumnWidth = 0.52f;

        [Header("Buttons")]
        public Transform RowButton;
        public Transform RefineButton;

        [Header("Dial")]
        [Tooltip("The wheel, turned with the mouse wheel. A dial rather than a pair of arrows " +
                 "because the web has enough steps that clicking through them is tedious, and " +
                 "fine-tuning a grain size is exactly what a wheel is for.")]
        public Transform Dial;

        [Tooltip("Degrees the wheel turns per step, so the amount you have moved is visible.")]
        public float DegreesPerStep = 14f;

        [Header("Handling")]
        public float Reach = 2.4f;

        [Tooltip("Steps across the web's whole range. Generous, because a wheel makes many steps " +
                 "cheap to cross where a button did not — which is the point of having one.")]
        [Range(4, 120)] public int WebSteps = 48;

        [Header("Appearance")]
        public Color Normal = new Color(0.62f, 0.95f, 0.68f);
        public Color Active = new Color(1f, 0.92f, 0.55f);

        /// <summary>Base chemistries the refiner can start from.</summary>
        public static readonly string[] Chemistries =
        {
            PropellantLibrary.BlackPowder,
            PropellantLibrary.SingleBase,
            PropellantLibrary.DoubleBase,
            PropellantLibrary.TripleBase
        };

        /// <summary>Dies the extruder can be fitted with.</summary>
        public static readonly GrainShape[] Dies =
        {
            GrainShape.Sphere,
            GrainShape.Flake,
            GrainShape.Cord,
            GrainShape.SinglePerforated,
            GrainShape.SevenPerforated
        };

        // ---- What is dialled up, before it is pressed ---------------------
        //
        // Held here rather than written straight through to the mill, so the screen can show a
        // recipe being composed and REFINE is the moment it becomes real. Without that the machine
        // has no verb and the button is decoration.

        [SerializeField] private string _base = PropellantLibrary.SingleBase;
        [SerializeField] private GrainShape _die = GrainShape.Sphere;
        [SerializeField] private int _webStep = 4;
        [SerializeField] private int _passes = 2;

        private Row _row = Row.Web;
        private Camera _eye;

        private Camera Aiming => _eye != null && _eye.isActiveAndEnabled ? _eye : Camera.main;

        /// <summary>Web the dialled step corresponds to, metres.</summary>
        public double Web
        {
            get
            {
                if (Mill == null) return 0.0;

                // LOGARITHMIC, because the range is 25:1 and burn time follows the web
                // proportionally — so equal steps should be equal proportional changes.
                double min = System.Math.Log(Mill.MinimumWeb);
                double max = System.Math.Log(Mill.MaximumWeb);

                double t = WebSteps > 1 ? _webStep / (double)(WebSteps - 1) : 0.0;
                return System.Math.Exp(min + (max - min) * t);
            }
        }

        /// <summary>Deterrent the dialled passes correspond to, 0..1.</summary>
        public double Deterrent
        {
            get
            {
                int most = Mill != null ? Mathf.Max(1, Mill.DrumPasses) : 6;
                return Mathf.Clamp01(_passes / (float)most);
            }
        }

        // ------------------------------------------------------------------

        private void OnEnable()
        {
            if (Mill == null) Mill = GetComponentInParent<PropellantMill>();
            ReadFromMill();
            Refresh();
        }

        /// <summary>Bounds are only real after the first frame, so lay the screen out again.</summary>
        private void Start() => Refresh();

        /// <summary>Starts the dial where the mill already is, so REFINE is never a surprise.</summary>
        private void ReadFromMill()
        {
            if (Mill == null) return;

            _base = Mill.BaseId;
            _die = Mill.Shape;
            _passes = (int)System.Math.Round(Mill.DeterrentCoating *
                                             Mathf.Max(1, Mill.DrumPasses));

            // Nearest step to the mill's current web.
            double min = System.Math.Log(Mill.MinimumWeb);
            double max = System.Math.Log(Mill.MaximumWeb);

            if (max - min > 1e-12 && Mill.WebThickness > 0.0)
            {
                double t = (System.Math.Log(Mill.WebThickness) - min) / (max - min);
                _webStep = Mathf.Clamp((int)System.Math.Round(t * (WebSteps - 1)), 0, WebSteps - 1);
            }
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (Pressed(RowButton)) NextRow();
                else if (Pressed(RefineButton)) Refine();
            }

            Turn(mouse);
        }

        /// <summary>
        /// Turns the dial with the mouse wheel.
        ///
        /// ONLY WHILE LEANING IN AT THIS STATION. The wheel is a global input with no aim attached,
        /// so without that gate scrolling anywhere in the shop would quietly re-mill the powder —
        /// and the player would have no idea why their load changed. The canon already says the
        /// mouse belongs to the work once you are at the bench, which is exactly this.
        ///
        /// Stepped rather than continuous: the scroll delta is a notch count on Windows and a
        /// smooth value on a trackpad, so accumulating and consuming whole notches behaves the same
        /// on both instead of racing on one of them.
        /// </summary>
        private void Turn(Mouse mouse)
        {
            if (!Leaning) { _notch = 0f; return; }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            // A Windows notch is 120; a trackpad sends far smaller values.
            _notch += scroll / (Mathf.Abs(scroll) >= 40f ? 120f : 1f);

            while (_notch >= 1f) { _notch -= 1f; Step(+1); }
            while (_notch <= -1f) { _notch += 1f; Step(-1); }
        }

        private float _notch;

        /// <summary>True while the gunsmith is leaned in over this station.</summary>
        private bool Leaning
        {
            get
            {
                if (_station == null) _station = GetComponentInParent<StationView>();
                if (_station == null) return false;

                if (_rig == null) _rig = FindAnyObjectByType<PlayerRig>();
                return _rig != null && _rig.Focused == _station;
            }
        }

        private StationView _station;
        private PlayerRig _rig;

        private bool Pressed(Transform button)
            => button != null && Aim.IsUnderAim(Aiming, button.gameObject, Reach, Mouse.current);

        /// <summary>Moves to the next line of the recipe.</summary>
        public void NextRow()
        {
            _row = (Row)(((int)_row + 1) % 4);
            Refresh();
        }

        private int _turned;

        /// <summary>Changes the selected line.</summary>
        public void Step(int direction)
        {
            _turned += direction;

            switch (_row)
            {
                case Row.Base:
                    _base = Chemistries[Wrap(IndexOf(Chemistries, _base) + direction, Chemistries.Length)];
                    break;

                case Row.Grain:
                    _die = Dies[Wrap(IndexOf(Dies, _die) + direction, Dies.Length)];
                    break;

                case Row.Web:
                    _webStep = Mathf.Clamp(_webStep + direction, 0, WebSteps - 1);
                    break;

                case Row.Drum:
                    int most = Mill != null ? Mathf.Max(1, Mill.DrumPasses) : 6;
                    _passes = Mathf.Clamp(_passes + direction, 0, most);
                    break;
            }

            Refresh();
        }

        /// <summary>
        /// Presses the recipe. The mill rebuilds its sample, so the pan shows what was made.
        /// </summary>
        public void Refine()
        {
            if (Mill == null) return;

            Mill.BaseId = _base;
            Mill.SetShape(_die);
            Mill.SetWeb(Web);
            Mill.SetDeterrent(Deterrent);

            Refresh();
        }

        // ------------------------------------------------------------------

        public void Refresh()
        {
            bool pending = Mill != null &&
                           (Mill.BaseId != _base || Mill.Shape != _die ||
                            System.Math.Abs(Mill.WebThickness - Web) > Web * 1e-6);

            if (Labels != null)
            {
                Labels.text =
                    Mark(Row.Base) + "base\n" +
                    Mark(Row.Grain) + "grain\n" +
                    Mark(Row.Web) + "web\n" +
                    Mark(Row.Drum) + "drum\n";

                Labels.color = Normal;
            }

            if (Values != null)
            {
                var text = new System.Text.StringBuilder();

                text.Append($"{Chemistry}\n");
                text.Append($"{_die}\n");
                text.Append($"{Web * 1000.0:F3} mm\n");
                text.Append(_passes == 0 ? "none\n" : $"{_passes} passes\n");
                text.Append(pending ? "not pressed" : string.Empty);

                Values.text = text.ToString();
                Values.color = pending ? Active : Normal;
            }

            BenchScreen.LayOut(Glass, Labels, Values, ColumnWidth,
                ref _labelRest, ref _labelKnown, ref _valueRest, ref _valueKnown);

            BenchScreen.PlaceMark(RowButton);
            BenchScreen.PlaceMark(RefineButton);

            // The wheel shows how far it has been turned, so the dial is a thing that moves rather
            // than an ornament beside a number.
            if (Dial != null)
                Dial.localRotation = Quaternion.Euler(_turned * DegreesPerStep, 0f, 90f);
        }

        /// <summary>The cursor down the left of the labels, showing which row the buttons work.</summary>
        private string Mark(Row row) => _row == row ? ">" : " ";

        private string Chemistry
            => PropellantLibrary.TryGet(_base, out var powder) ? powder.DisplayName : _base;

        private Vector3 _labelRest, _valueRest;
        private bool _labelKnown, _valueKnown;

        private static int Wrap(int index, int length) => ((index % length) + length) % length;

        private static int IndexOf(string[] all, string value)
        {
            for (int i = 0; i < all.Length; i++)
                if (string.Equals(all[i], value, System.StringComparison.Ordinal)) return i;
            return 0;
        }

        private static int IndexOf(GrainShape[] all, GrainShape value)
        {
            for (int i = 0; i < all.Length; i++)
                if (all[i] == value) return i;
            return 0;
        }
    }
}
