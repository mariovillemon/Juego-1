using UnityEngine;

namespace Garage.Unity
{
    /// <summary>How the workshop scene should start (set by the main menu before loading it).</summary>
    public static class GameLaunch
    {
        /// <summary>Save slot to load (0 = new game).</summary>
        public static int LoadSlot;

        /// <summary>Start the first-car tutorial on a new game.</summary>
        public static bool Tutorial = true;

        /// <summary>Training mode (diagnostic reasoning).</summary>
        public static bool Training = true;

        /// <summary>Set when the menu configured the launch (otherwise the scene's own defaults apply).</summary>
        public static bool FromMenu;
    }

    /// <summary>
    /// Entry point of the workshop scene: creates or loads the game, starts the tutorial on a new game and rebuilds
    /// the car on the lift whenever it changes. The lift starts empty: the player accepts a job on the board.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public SimulationRunner runner;
        public CarAssembler assembler;
        public WorkshopController workshop;

        [Tooltip("Si no es vacío: modo libre con este coche (sin economía)")]
        public string sandboxCarId = "";
        public string sandboxScenarioId = "";

        [Tooltip("Iniciar el tutorial del primer coche en partida nueva")]
        public bool tutorialOnNewGame = true;

        private void Awake()
        {
            if (runner == null)
            {
                runner = FindFirstObjectByType<SimulationRunner>();
            }

            if (runner == null)
            {
                runner = gameObject.AddComponent<SimulationRunner>();
            }

            if (GameLaunch.FromMenu)
            {
                runner.training = GameLaunch.Training;
            }

            runner.Initialise();
            runner.CarChanged += car =>
            {
                if (assembler == null)
                {
                    return;
                }

                if (car == null)
                {
                    assembler.Clear();
                }
                else
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

            if (GameLaunch.FromMenu && GameLaunch.LoadSlot > 0)
            {
                runner.Slots.Load(runner.Session, GameLaunch.LoadSlot);
            }
            else if (GameLaunch.FromMenu ? GameLaunch.Tutorial : tutorialOnNewGame)
            {
                runner.Session.StartTutorial("tut_first_car");
            }

            GameLaunch.FromMenu = false;
            if (workshop != null && runner.Job != null)
            {
                workshop.OnJobLoaded(runner.Job);
            }
        }
    }
}
