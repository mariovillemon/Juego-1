using Garage.Sim.Game;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Front desk: shows the customer, the complaint and the quote, and lets the player deliver the car.
    /// Minimal IMGUI placeholder until the diegetic office UI is modelled.
    /// </summary>
    public sealed class CustomerController : MonoBehaviour
    {
        public SimulationRunner runner;
        public bool visible;
        private string _result = "";

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.jKey.wasPressedThisFrame)
            {
                visible = !visible;
                Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = visible;
            }
        }

        private void OnGUI()
        {
            if (!visible || runner == null || runner.Workshop == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(40, 40, 620, 420), GUI.skin.box);
            Workshop w = runner.Workshop;
            GUILayout.Label($"Día {w.Day}  {(int)(w.Minute / 60):00}:{(int)(w.Minute % 60):00}   Caja {w.Money:0} €   Reputación {w.Reputation:0}");
            Job j = runner.Job;
            if (j != null)
            {
                GUILayout.Label($"Cliente: {j.Customer.Name} — {j.Customer.Description}");
                GUILayout.Label($"Coche: {j.Car.Definition.DisplayName}");
                GUILayout.Label($"«{j.Definition.Complaint}»");
                GUILayout.Label($"Presupuesto aceptado: {j.QuotedAmount:0} €");
                if (j.Status == JobStatus.InProgress && GUILayout.Button("Entregar el coche"))
                {
                    JobOutcome o = w.Deliver(j);
                    _result = (o.Success ? "Cliente satisfecho. " : "El cliente no queda satisfecho. ") + string.Join(" ", o.Notes) + $" Cobrado {o.Payment:0} €.";
                }
            }

            if (GUILayout.Button("Siguiente encargo"))
            {
                Job next = w.Offers(1)[0];
                w.ProposeQuote(next, w.SuggestQuote(next));
                runner.Load(next);
            }

            GUILayout.Label(_result);
            GUILayout.EndArea();
        }
    }
}
