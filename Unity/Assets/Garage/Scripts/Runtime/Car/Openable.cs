using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// A hinged body panel (door, hood, tailgate) on its hinge transform: E toggles it and it swings with an
    /// eased motion to <see cref="openAngle"/> around <see cref="axis"/> (hinge-local). The panel model is a child
    /// with a box collider on the Default layer so the interaction ray hits it (the body itself ignores raycasts).
    /// </summary>
    public sealed class Openable : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float openAngle = 65f;
        [Tooltip("Nombre para el aviso: «la puerta del conductor», «el capó»…")] public string displayName = "la puerta";
        [Tooltip("Segundos para abrir o cerrar del todo")] public float duration = 0.9f;
        public bool open;

        private float _t;

        /// <summary>Text for the interaction prompt.</summary>
        public string Prompt => (open ? "E: cerrar " : "E: abrir ") + displayName;

        /// <summary>Opens or closes.</summary>
        public void Toggle()
        {
            open = !open;
            GameAudio.PlayAt(open ? "body.door_open" : "body.door_close", transform.position);
        }

        /// <summary>Sets the state without animation (car rebuilt).</summary>
        public void SetImmediate(bool isOpen)
        {
            open = isOpen;
            _t = isOpen ? 1f : 0f;
            Apply();
        }

        private void Update()
        {
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(_t, target))
            {
                return;
            }

            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Mathf.Max(0.05f, duration));
            Apply();
        }

        private void Apply()
        {
            // Ease in-out, with a short settle at the end like a real check strap.
            float e = _t * _t * (3f - 2f * _t);
            transform.localRotation = Quaternion.AngleAxis(openAngle * e, axis);
        }
    }
}
