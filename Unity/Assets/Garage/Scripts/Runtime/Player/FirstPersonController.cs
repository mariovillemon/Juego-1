using UnityEngine;
using UnityEngine.InputSystem;

namespace Garage.Unity
{
    /// <summary>
    /// First person controller (Input System actions created in code): walk, look, crouch, subtle head bob and an
    /// inspection lamp with a real photometric output. The Cinemachine camera lives under <see cref="head"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        public Transform head;
        public Light inspectionLamp;
        [Header("Movimiento")] public float walkSpeed = 1.4f;
        public float crouchSpeed = 0.7f;
        public float standHeight = 1.75f;
        public float crouchHeight = 1.05f;
        public float lookSensitivity = 0.08f;
        [Header("Balanceo de cabeza")] public float bobAmplitude = 0.012f;
        public float bobFrequency = 1.9f;

        private CharacterController _cc;
        private InputAction _move;
        private InputAction _look;
        private InputAction _crouch;
        private InputAction _lamp;
        private float _pitch;
        private float _bobPhase;
        private float _eyeBase;
        private float _verticalSpeed;

        /// <summary>True while crouching.</summary>
        public bool Crouching { get; private set; }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = standHeight;
            _cc.center = new Vector3(0, standHeight / 2, 0);
            _eyeBase = head != null ? head.localPosition.y : 1.65f;
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");
            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");
            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Gamepad>/buttonEast");
            _lamp = new InputAction("Lamp", InputActionType.Button, "<Keyboard>/f");
            _lamp.AddBinding("<Gamepad>/dpad/up");
        }

        private void OnEnable()
        {
            _move.Enable();
            _look.Enable();
            _crouch.Enable();
            _lamp.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            _move.Disable();
            _look.Disable();
            _crouch.Disable();
            _lamp.Disable();
        }

        /// <summary>Freezes look/move (while using a device screen).</summary>
        public bool Frozen { get; set; }

        private void Update()
        {
            if (_lamp.WasPressedThisFrame() && inspectionLamp != null)
            {
                inspectionLamp.enabled = !inspectionLamp.enabled;
            }

            if (Frozen)
            {
                return;
            }

            Vector2 look = _look.ReadValue<Vector2>() * lookSensitivity;
            transform.Rotate(0, look.x, 0);
            _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);

            Crouching = _crouch.IsPressed();
            float targetHeight = Crouching ? crouchHeight : standHeight;
            _cc.height = Mathf.Lerp(_cc.height, targetHeight, Time.deltaTime * 8f);
            _cc.center = new Vector3(0, _cc.height / 2, 0);

            Vector2 mv = _move.ReadValue<Vector2>();
            Vector3 dir = transform.right * mv.x + transform.forward * mv.y;
            float speed = Crouching ? crouchSpeed : walkSpeed;
            _verticalSpeed = _cc.isGrounded ? -1f : _verticalSpeed - 9.81f * Time.deltaTime;
            _cc.Move((dir * speed + Vector3.up * _verticalSpeed) * Time.deltaTime);

            if (head != null)
            {
                float moving = Mathf.Clamp01(mv.magnitude);
                _bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * 2f * moving;
                float bob = Mathf.Sin(_bobPhase) * bobAmplitude * moving;
                float eye = _eyeBase * (_cc.height / standHeight);
                head.localPosition = new Vector3(0, eye + bob, 0);
                head.localRotation = Quaternion.Euler(_pitch, 0, Mathf.Sin(_bobPhase * 0.5f) * 0.3f * moving);
            }
        }
    }
}
