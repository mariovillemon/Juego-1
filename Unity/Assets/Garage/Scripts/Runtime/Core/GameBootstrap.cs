using System.Linq;
using Garage.Sim.Game;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Entry point of the workshop scene: initialises the simulation, takes the first job from the board and
    /// assembles its car on the lift. Everything else listens to <see cref="SimulationRunner"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public SimulationRunner runner;
        public CarAssembler assembler;
        public WorkshopController workshop;

        [Tooltip("Vacío = primer encargo del tablón. Si no, id de coche para modo sandbox.")]
        public string sandboxCarId = "";
        public string sandboxScenarioId = "";

        private void Awake()
        {
            if (runner == null)
            {
                runner = FindFirstObjectByType<SimulationRunner>() ?? gameObject.AddComponent<SimulationRunner>();
            }

            runner.Initialise();
            runner.CarChanged += car =>
            {
                if (assembler != null)
                {
                    assembler.Build(car);
                }
            };

            Camera main = Camera.main;
            if (main != null)
            {
                main.cullingMask &= ~(1 << UnityCompat.DeviceUiLayer);
            }
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(sandboxCarId))
            {
                runner.LoadSandbox(sandboxCarId, sandboxScenarioId);
                return;
            }

            Job job = runner.Workshop.Offers(1).First();
            runner.Workshop.ProposeQuote(job, runner.Workshop.SuggestQuote(job));
            runner.Load(job);
            if (workshop != null)
            {
                workshop.OnJobLoaded(job);
            }
        }
    }
}
