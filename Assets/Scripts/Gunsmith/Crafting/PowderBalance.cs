using Krofken.Ballistics;
using UnityEngine;

namespace Gunsmith.Crafting
{
    /// <summary>
    /// The powder scale: a beam balance with a sliding poise.
    ///
    /// You do not type a charge weight. You slide the poise along the beam to the
    /// weight you want, then trickle powder into the pan until the beam comes level.
    /// The number is engraved on the beam, under the poise, which is where a number
    /// belongs — it says how much you INTEND to use, and the beam tells you when you
    /// have used it. Nothing here predicts what the charge will do.
    ///
    /// HOW A BEAM BALANCE ACTUALLY WORKS, because it is the whole mechanic:
    ///
    /// The beam pivots on a knife edge. The pan hangs at a fixed distance from the
    /// pivot; the poise slides along the other arm. It balances when the two moments
    /// about the pivot are equal:
    ///
    ///     m_powder * L_pan  =  m_poise * d_poise
    ///
    /// so for a fixed pan arm and poise mass, the charge the scale is set to is simply
    /// LINEAR in how far out the poise has been slid. That is why a real powder scale
    /// has an evenly divided beam, and it is why sliding the poise is a legitimate way
    /// to dial a number without a text field.
    ///
    /// The beam then tips by the moment IMBALANCE, and saturates against its stops
    /// almost immediately — a scale reading in tenths of a grain sits hard on the
    /// bottom stop until you are within a fraction of a grain, then swings. That
    /// near-binary behaviour is the feel: it teaches "this much powder" as a felt
    /// quantity rather than a typed one.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Gunsmith/Powder Balance")]
    public sealed class PowderBalance : MonoBehaviour
    {
        [Header("Beam")]
        [Tooltip("Charge the poise is set to at the far end of its travel, grains.")]
        public double MaxSettingGrains = 12.0;

        // DOUBLE, not float, and deliberately. The poise position divides into this to
        // produce the charge weight, and that charge goes straight into the interior
        // ballistics ODE. A serialised float here is not exactly 0.12, so 0.06/0.12f
        // comes out 0.50000001 and the scale reads 6.0000001 grains — precision noise
        // injected into a solver input by a display field. Floats belong at the Unity
        // boundary; this is not the boundary.
        [Tooltip("Length of the poise's travel along the beam, metres. Sliding the " +
                 "poise this far dials MaxSettingGrains.")]
        public double BeamTravel = 0.12;

        [Tooltip("How far the beam swings when fully out of balance, degrees.")]
        public float SwingDegrees = 9f;

        [Tooltip("Imbalance that puts the beam hard against a stop, grains. A real " +
                 "powder scale resolves about a tenth of a grain.")]
        public double SaturationGrains = 0.1;

        [Header("Parts")]
        public Transform Beam;
        public Transform Poise;
        public Transform Pan;

        [Tooltip("The engraving under the poise. Shows the SETTING, not the contents.")]
        public TextMesh BeamReadout;

        // ------------------------------------------------------------------

        [SerializeField] private double _settingGrains = 5.5;
        [SerializeField] private double _pouredGrains;

        /// <summary>Charge the poise is set to, grains. What you are aiming for.</summary>
        public double SettingGrains
        {
            get => _settingGrains;
            set { _settingGrains = Clamp(value, 0.0, MaxSettingGrains); Refresh(); }
        }

        /// <summary>Powder actually in the pan, grains.</summary>
        public double PouredGrains
        {
            get => _pouredGrains;
            private set { _pouredGrains = value < 0.0 ? 0.0 : value; Refresh(); }
        }

        /// <summary>Charge in kilograms, which is what a cartridge design wants.</summary>
        public double PouredCharge => Units.GrainsToKilograms(_pouredGrains);

        /// <summary>How far out of balance the beam is, grains. Positive is over.</summary>
        public double Imbalance => _pouredGrains - _settingGrains;

        /// <summary>True when the beam has come level — within what the scale can
        /// actually resolve, not to machine precision.</summary>
        public bool IsLevel => System.Math.Abs(Imbalance) <= SaturationGrains * 0.25;

        /// <summary>True once the pan holds more than the poise is set to. Overcharging
        /// is not blocked — the scale simply shows it, and the consequences belong to
        /// the physics, not to a validation rule.</summary>
        public bool IsOver => Imbalance > SaturationGrains * 0.25;

        /// <summary>Beam tilt, degrees. Positive is pan-down.</summary>
        public float BeamAngle
        {
            get
            {
                if (SaturationGrains <= 0.0) return 0f;

                double t = Imbalance / SaturationGrains;
                if (t > 1.0) t = 1.0;
                else if (t < -1.0) t = -1.0;

                return (float)(t * SwingDegrees);
            }
        }

        private void OnEnable() => Refresh();
        private void OnValidate() => Refresh();

        // ------------------------------------------------------------------

        /// <summary>Adds powder to the pan. Call it repeatedly while the player holds
        /// the trickler — the point is that the last tenth of a grain takes care.</summary>
        public void Trickle(double grains)
        {
            if (grains <= 0.0) return;
            PouredGrains = _pouredGrains + grains;
        }

        /// <summary>Tips the pan back into the powder tin.</summary>
        public void Empty() => PouredGrains = 0.0;

        /// <summary>Slides the poise. Distance is measured along the beam from the
        /// pivot, in metres; the setting is linear in it, exactly as the moment
        /// balance says it must be.</summary>
        public void SlidePoise(double distanceAlongBeam)
        {
            if (BeamTravel <= 0.0) return;
            SettingGrains = MaxSettingGrains * Clamp(distanceAlongBeam / BeamTravel, 0.0, 1.0);
        }

        /// <summary>Applies the poured charge to a design. The scale hands over what is
        /// in the pan, not what the poise was set to — if the player did not trickle all
        /// the way, they load what they actually weighed.</summary>
        public void ApplyTo(ref CartridgeDesign design) => design.ChargeMass = PouredCharge;

        // ------------------------------------------------------------------

        private void Refresh()
        {
            if (Beam != null)
                Beam.localRotation = Quaternion.Euler(BeamAngle, 0f, 0f);

            if (Poise != null && BeamTravel > 0.0)
            {
                // Cast to float here and nowhere earlier: a transform IS the Unity
                // boundary, and losing precision on a position is harmless.
                double fraction = MaxSettingGrains > 0.0 ? _settingGrains / MaxSettingGrains : 0.0;
                Poise.localPosition = new Vector3((float)(fraction * BeamTravel), 0f, 0f);
            }

            if (BeamReadout != null)
                BeamReadout.text = $"{_settingGrains:F1} gr";
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}
