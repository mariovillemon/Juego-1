using UnityEngine;
using UnityEngine.SceneManagement;

namespace Garage.Unity
{
    /// <summary>Main menu: slow camera orbit and a minimal menu (IMGUI placeholder).</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        public Vector3 pivot = new Vector3(0, 1.5f, 0);
        public float orbitSpeed = 3f;

        private void Update()
        {
            transform.RotateAround(pivot, Vector3.up, orbitSpeed * Time.deltaTime);
            transform.LookAt(pivot);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(60, Screen.height / 2f - 100, 320, 220), GUI.skin.box);
            GUILayout.Label("TALLER — Simulador de diagnosis");
            if (GUILayout.Button("Nueva partida"))
            {
                SceneManager.LoadScene("Workshop");
            }

            if (GUILayout.Button("Sala del banco de potencia"))
            {
                SceneManager.LoadScene("Dyno");
            }

            if (GUILayout.Button("Salir"))
            {
                Application.Quit();
            }

            GUILayout.EndArea();
        }
    }
}
