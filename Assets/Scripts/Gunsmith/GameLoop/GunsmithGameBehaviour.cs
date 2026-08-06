using System;
using UnityEngine;
using Krofken.Ballistics;
using Krofken.Ballistics.UnityIntegration;

namespace Gunsmith.GameLoop
{
    /// <summary>
    /// Scene-side shell around <see cref="GunsmithGame"/>.
    ///
    /// Deliberately thin. It owns the game object graph, forwards Unity's lifecycle,
    /// and re-broadcasts events for UI to bind to. All the actual rules live in
    /// <see cref="GunsmithGame"/>, which has no Unity dependency and can be driven
    /// from a test.
    ///
    /// Drop this on one GameObject. Nothing else in the scene is required for the
    /// mechanics to run -- the range, the workbench and the order board are all
    /// simulated whether or not anything is drawn.
    /// </summary>
    [AddComponentMenu("Gunsmith/Gunsmith Game")]
    [DefaultExecutionOrder(-100)]
    public sealed class GunsmithGameBehaviour : MonoBehaviour
    {
        [Header("Run")]
        [Tooltip("Seed deciding which customers turn up. Same seed, same townsfolk.")]
        [SerializeField] private int seed = 0;

        [Tooltip("How many orders are posted each morning.")]
        [SerializeField] private int ordersPerDay = 3;

        [Tooltip("Grant the opening stock of lead, jacket metal, powder, cases and primers.")]
        [SerializeField] private bool grantStartingStock = true;

        [Tooltip("Begin on Awake. Turn off to drive the start from your own code.")]
        [SerializeField] private bool startAutomatically = true;

        [Header("Range conditions")]
        [Tooltip("Air temperature on the range, degrees Celsius.")]
        [SerializeField] private float temperatureCelsius = 15f;

        [Tooltip("Station pressure, pascals.")]
        [SerializeField] private float pressurePascals = 101325f;

        [Tooltip("Relative humidity, 0 to 1.")]
        [Range(0f, 1f)]
        [SerializeField] private float relativeHumidity = 0f;

        [Tooltip("Wind on the range, m/s, in Unity world axes.")]
        [SerializeField] private Vector3 wind = Vector3.zero;

        [Header("Optional")]
        [Tooltip("Drives visible projectiles for the range. Not required -- the range " +
                 "measures shots analytically whether or not anything is drawn.")]
        [SerializeField] private ProjectileSimulator projectileSimulator;

        /// <summary>The game. Null until <see cref="StartNewGame"/> has run.</summary>
        public GunsmithGame Game { get; private set; }

        /// <summary>Raised after <see cref="Game"/> exists, so UI can bind to it.</summary>
        public event Action<GunsmithGame> GameStarted;

        private void Awake()
        {
            if (startAutomatically) StartNewGame();
        }

        /// <summary>Creates a fresh run.</summary>
        public void StartNewGame()
        {
            Game = new GunsmithGame { OrdersPerDay = ordersPerDay };

            ApplyRangeConditions();

            Game.StartNewGame(seed, grantStartingStock);

            if (projectileSimulator != null)
                projectileSimulator.Atmosphere = Game.Range.Atmosphere;

            GameStarted?.Invoke(Game);
        }

        /// <summary>
        /// Pushes the inspector's weather into the simulation.
        ///
        /// Worth exposing because it is not cosmetic: air density scales drag
        /// linearly, so a hot humid day genuinely moves the impact point. Call this
        /// again after changing the fields at runtime.
        /// </summary>
        public void ApplyRangeConditions()
        {
            if (Game == null) return;

            var atmosphere = BallisticsConversion.CreateAtmosphere(
                temperatureCelsius, pressurePascals, relativeHumidity, wind);

            Game.Range.Atmosphere = atmosphere;

            if (projectileSimulator != null)
                projectileSimulator.Atmosphere = atmosphere;
        }

        /// <summary>Advances Day to Night to Dawn and round again.</summary>
        public void AdvancePhase() => Game?.AdvancePhase();
    }
}
