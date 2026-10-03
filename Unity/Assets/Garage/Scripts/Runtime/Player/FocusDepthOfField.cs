using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Garage.Unity
{
    /// <summary>
    /// Physical depth of field that focuses on what the player looks at when close (inspecting a part),
    /// and fades out at normal distances.
    /// </summary>
    public sealed class FocusDepthOfField : MonoBehaviour
    {
        public Camera viewCamera;
        public Volume volume;
        [Tooltip("Distancia por debajo de la cual se activa el enfoque de inspección (m)")] public float inspectDistance = 1.2f;
        public float smoothing = 6f;

        private DepthOfField _dof;
        private float _focus = 3f;

        private void Start()
        {
            if (volume != null && volume.profile != null)
            {
                volume.profile.TryGet(out _dof);
            }
        }

        private void LateUpdate()
        {
            if (_dof == null || viewCamera == null)
            {
                return;
            }

            float target = 10f;
            bool close = false;
            if (Physics.Raycast(viewCamera.transform.position, viewCamera.transform.forward, out RaycastHit hit, 15f))
            {
                target = hit.distance;
                close = hit.distance < inspectDistance;
            }

            _focus = Mathf.Lerp(_focus, target, Time.deltaTime * smoothing);
            _dof.focusMode.Override(close ? DepthOfFieldMode.UsePhysicalCamera : DepthOfFieldMode.Off);
            _dof.focusDistance.Override(Mathf.Max(0.1f, _focus));
        }
    }
}
