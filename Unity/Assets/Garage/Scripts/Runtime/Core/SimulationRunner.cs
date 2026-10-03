using System;
using System.IO;
using Garage.Data;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Vehicle;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Bridge between Unity and Garage.Sim: owns the content database, the workshop and the car currently on the lift,
    /// and advances the simulation in FixedUpdate with the same fixed step the CLI and tests use.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [Tooltip("Pasos de simulación por FixedUpdate (1 = tiempo real a 50 Hz)")]
        [Range(1, 20)] public int stepsPerFixedUpdate = 1;

        [Tooltip("Semilla de la partida")]
        public long seed = 20261003;

        /// <summary>Loaded content.</summary>
        public ContentDatabase Content { get; private set; }

        /// <summary>Workshop (meta-game).</summary>
        public Workshop Workshop { get; private set; }

        /// <summary>Car being worked on.</summary>
        public Car Car { get; private set; }

        /// <summary>Job of the current car (null in sandbox).</summary>
        public Job Job { get; private set; }

        /// <summary>Raised after each simulation step batch.</summary>
        public event Action<Car> Stepped;

        /// <summary>Raised when the car on the lift changes.</summary>
        public event Action<Car> CarChanged;

        /// <summary>Directory with base/ and schemas/ (StreamingAssets/data).</summary>
        public static string DataRoot => Path.Combine(Application.streamingAssetsPath, "data");

        /// <summary>Loads content and creates a new game. Safe to call more than once.</summary>
        public void Initialise()
        {
            if (Content != null)
            {
                return;
            }

            string mods = Path.Combine(Application.persistentDataPath, "mods");
            Content = ContentDatabase.Load(DataRoot, Directory.Exists(mods) ? mods : null);
            foreach (var e in Content.Report.Errors)
            {
                Debug.LogWarning("[Garage] " + e);
            }

            Workshop = new Workshop(Content, (ulong)seed);
            Time.fixedDeltaTime = (float)Car.DefaultDt;
        }

        /// <summary>Puts a job's car on the lift.</summary>
        public void Load(Job job)
        {
            Job = job;
            SetCar(job.Car);
        }

        /// <summary>Sandbox: loads a car with an optional scenario.</summary>
        public void LoadSandbox(string carId, string scenarioId = null)
        {
            Initialise();
            var faults = string.IsNullOrEmpty(scenarioId) ? null : Content.ScenarioFaults(scenarioId);
            Job = null;
            SetCar(Content.CreateCar(carId, (ulong)seed, faults));
        }

        private void SetCar(Car car)
        {
            Car = car;
            CarChanged?.Invoke(car);
        }

        private void FixedUpdate()
        {
            if (Car == null)
            {
                return;
            }

            for (int i = 0; i < stepsPerFixedUpdate; i++)
            {
                Car.Step(Car.DefaultDt);
            }

            Stepped?.Invoke(Car);
        }
    }
}
