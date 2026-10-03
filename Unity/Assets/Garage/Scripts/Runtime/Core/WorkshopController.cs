using Garage.Sim.Game;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>Workshop day cycle: maps game time to the sun (time of day) and exposes the current job.</summary>
    public sealed class WorkshopController : MonoBehaviour
    {
        public SimulationRunner runner;
        public Light sun;
        [Tooltip("Hora inicial si no hay partida (0-24)")] [Range(0, 24)] public float hour = 10f;
        [Tooltip("Latitud aproximada para la altura del sol")] public float latitude = 40f;

        /// <summary>Current job.</summary>
        public Job CurrentJob { get; private set; }

        /// <summary>Called by the bootstrap.</summary>
        public void OnJobLoaded(Job job) => CurrentJob = job;

        private void Update()
        {
            if (runner != null && runner.Workshop != null)
            {
                hour = (float)(runner.Workshop.Minute / 60.0);
            }

            if (sun != null)
            {
                ApplySun(sun.transform, hour, latitude);
            }
        }

        /// <summary>Simple solar elevation model (equinox): rises at 6, peaks at 13 (local summer time).</summary>
        public static void ApplySun(Transform sunTransform, float hourOfDay, float latitudeDeg)
        {
            float hourAngle = (hourOfDay - 13f) * 15f;
            float elevation = (90f - latitudeDeg) * Mathf.Cos(hourAngle * Mathf.Deg2Rad);
            sunTransform.rotation = Quaternion.Euler(elevation, 180f + hourAngle, 0f);
        }
    }
}
