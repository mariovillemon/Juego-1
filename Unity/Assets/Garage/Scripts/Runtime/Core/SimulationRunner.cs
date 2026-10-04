using System;
using System.IO;
using Garage.Data;
using Garage.Game;
using Garage.Sim.Game;
using Garage.Sim.Vehicle;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Bridge between Unity and the game layer: owns the <see cref="GameSession"/> (Garage.Game) shared with the
    /// CLI, follows the car on the lift and advances its simulation in FixedUpdate with the same fixed step the CLI
    /// and tests use. Presentation scripts only read state, listen to <see cref="GameSession.Events"/> and call
    /// session/<see cref="CarWork"/> commands; no game rules live in MonoBehaviours.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        [Tooltip("Pasos de simulación por FixedUpdate (1 = tiempo real a 50 Hz)")]
        [Range(1, 20)] public int stepsPerFixedUpdate = 1;

        [Tooltip("Semilla de la partida")]
        public long seed = 20261003;

        [Tooltip("Modo formación: explicaciones del razonamiento diagnóstico")]
        public bool training = true;

        private Car _sandboxCar;
        private CarWork _sandboxWork;

        /// <summary>Loaded content.</summary>
        public ContentDatabase Content { get; private set; }

        /// <summary>The game (shared application layer).</summary>
        public GameSession Session { get; private set; }

        /// <summary>Workshop (meta-game rules).</summary>
        public Workshop Workshop => Session?.Workshop;

        /// <summary>Car on the lift (active job or sandbox), null if the lift is empty.</summary>
        public Car Car => Session?.ActiveJob?.Car ?? _sandboxCar;

        /// <summary>Job of the car on the lift (null in sandbox or empty lift).</summary>
        public Job Job => Session?.ActiveJob;

        /// <summary>Work context (tools, ECU, dyno) of the car on the lift.</summary>
        public CarWork Work => Session?.Work ?? _sandboxWork;

        /// <summary>Raised after each simulation step batch.</summary>
        public event Action<Car> Stepped;

        /// <summary>Raised when the car on the lift changes (argument may be null).</summary>
        public event Action<Car> CarChanged;

        /// <summary>Directory with base/ and schemas/ (StreamingAssets/data).</summary>
        public static string DataRoot => Path.Combine(Application.streamingAssetsPath, "data");

        /// <summary>Save slots folder.</summary>
        public static string SaveDir => Path.Combine(Application.persistentDataPath, "saves");

        /// <summary>Save slots.</summary>
        public SaveSlots Slots { get; private set; }

        /// <summary>Loads content and creates a new game. Safe to call more than once.</summary>
        public void Initialise()
        {
            if (Session != null)
            {
                return;
            }

            string mods = Path.Combine(Application.persistentDataPath, "mods");
            Content = ContentDatabase.Load(DataRoot, Directory.Exists(mods) ? mods : null);
            foreach (var e in Content.Report.Errors)
            {
                Debug.LogWarning("[Garage] " + e);
            }

            Session = new GameSession(Content, (ulong)seed, training);
            Slots = new SaveSlots(SaveDir);
            Session.Events.Raised += OnGameEvent;
            Time.fixedDeltaTime = (float)Car.DefaultDt;
        }

        private Car _lastCar;

        private void OnGameEvent(GameEvent e)
        {
            if (e.Kind == GameEventKind.ActiveJobChanged || e.Kind == GameEventKind.GameLoaded)
            {
                _sandboxCar = null;
                _sandboxWork = null;
                RaiseCarChangedIfNeeded();
            }
        }

        private void RaiseCarChangedIfNeeded()
        {
            if (Car != _lastCar)
            {
                _lastCar = Car;
                CarChanged?.Invoke(Car);
            }
        }

        /// <summary>Puts an accepted job's car on the lift.</summary>
        public CommandResult Load(Job job)
        {
            Initialise();
            CommandResult r = Session.SetActiveJob(job);
            RaiseCarChangedIfNeeded();
            return r;
        }

        /// <summary>Sandbox: loads a car with an optional scenario (no economy, all tools).</summary>
        public void LoadSandbox(string carId, string scenarioId = null)
        {
            Initialise();
            var faults = string.IsNullOrEmpty(scenarioId) ? null : Content.ScenarioFaults(scenarioId);
            Session.SetActiveJob(null);
            _sandboxCar = Content.CreateCar(carId, (ulong)seed, faults);
            _sandboxWork = GameSession.Sandbox(_sandboxCar, training, Session.Events);
            RaiseCarChangedIfNeeded();
        }

        private void FixedUpdate()
        {
            Car car = Car;
            if (car == null)
            {
                return;
            }

            for (int i = 0; i < stepsPerFixedUpdate; i++)
            {
                car.Step(Car.DefaultDt);
            }

            Stepped?.Invoke(car);
        }
    }
}
