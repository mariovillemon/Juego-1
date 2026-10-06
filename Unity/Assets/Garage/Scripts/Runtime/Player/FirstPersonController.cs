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
        public float runSpeed = 3.6f;
        [Tooltip("Altura del salto (m)")] public float jumpHeight = 0.45f;
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
        private InputAction _sprint;
        private InputAction _jump;
        private float _speed;
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
            var keys = GameOptions.Current.Bindings;
            _move.AddCompositeBinding("2DVector").With("Up", keys.PathOf("forward")).With("Down", keys.PathOf("back")).With("Left", keys.PathOf("left")).With("Right", keys.PathOf("right"));
            GameOptions.Track(_move, 1, "forward");
            GameOptions.Track(_move, 2, "back");
            GameOptions.Track(_move, 3, "left");
            GameOptions.Track(_move, 4, "right");
            _move.AddBinding("<Gamepad>/leftStick");
            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");
            _crouch = new InputAction("Crouch", InputActionType.Button, keys.PathOf("crouch"));
            GameOptions.Track(_crouch, 0, "crouch");
            _crouch.AddBinding("<Gamepad>/buttonEast");
            _lamp = new InputAction("Lamp", InputActionType.Button, keys.PathOf("lamp"));
            GameOptions.Track(_lamp, 0, "lamp");
            _lamp.AddBinding("<Gamepad>/dpad/up");
            _sprint = new InputAction("Sprint", InputActionType.Button, keys.PathOf("sprint"));
            _sprint.AddBinding("<Gamepad>/leftStickPress");
            GameOptions.Track(_sprint, 0, "sprint");
            _jump = new InputAction("Jump", InputActionType.Button, keys.PathOf("jump"));
            _jump.AddBinding("<Gamepad>/buttonSouth");
            GameOptions.Track(_jump, 0, "jump");
        }

        private void OnEnable()
        {
            _move.Enable();
            _look.Enable();
            _crouch.Enable();
            _lamp.Enable();
            _sprint.Enable();
            _jump.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDestroy()
        {
            foreach (InputAction a in new[] { _move, _look, _crouch, _lamp, _sprint, _jump })
            {
                GameOptions.Untrack(a);
                a.Dispose();
            }
        }

        private void OnDisable()
        {
            _move.Disable();
            _look.Disable();
            _crouch.Disable();
            _lamp.Disable();
            _sprint.Disable();
            _jump.Disable();
        }

        /// <summary>Freezes look/move (while using a device screen).</summary>
        public bool Frozen { get; set; }

        private void Update()
        {
            if (_lamp.WasPressedThisFrame() && inspectionLamp != null)
            {
                inspectionLamp.enabled = !inspectionLamp.enabled;
            }

            if (Frozen || Garage.Unity.UI.UiState.AnyOpen)
            {
                return;
            }

            var options = GameOptions.Current;
            Vector2 look = _look.ReadValue<Vector2>() * lookSensitivity * (float)options.MouseSensitivity;
            if (options.InvertY)
            {
                look.y = -look.y;
            }

            transform.Rotate(0, look.x, 0);
            _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);

            Crouching = _crouch.IsPressed();
            float targetHeight = Crouching ? crouchHeight : standHeight;
            _cc.height = Mathf.Lerp(_cc.height, targetHeight, Time.deltaTime * 8f);
            _cc.center = new Vector3(0, _cc.height / 2, 0);

            Vector2 mv = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            Vector3 dir = transform.right * mv.x + transform.forward * mv.y;
            bool grounded = _cc.isGrounded;
            // Run only forwards and standing; speed changes smoothly (no instant 0 → 3.6 m/s).
            bool running = _sprint.IsPressed() && !Crouching && mv.y > 0.3f;
            float wanted = mv.sqrMagnitude < 0.01f ? 0f : Crouching ? crouchSpeed : running ? runSpeed : walkSpeed;
            _speed = Mathf.MoveTowards(_speed, wanted, (grounded ? 9f : 2f) * Time.deltaTime);
            if (grounded)
            {
                _verticalSpeed = -1f;
                if (_jump.WasPressedThisFrame() && !Crouching)
                {
                    _verticalSpeed = Mathf.Sqrt(2f * 9.81f * jumpHeight);
                }
            }
            else
            {
                _verticalSpeed -= 9.81f * Time.deltaTime;
            }

            _cc.Move((dir * _speed + Vector3.up * _verticalSpeed) * Time.deltaTime);

            if (head != null)
            {
                // Head bob follows the actual speed: faster and deeper when running, none in the air.
                float moving = grounded ? Mathf.Clamp01(_speed / walkSpeed) : 0f;
                float pace = Mathf.Lerp(1f, 1.45f, Mathf.InverseLerp(walkSpeed, runSpeed, _speed));
                _bobPhase += Time.deltaTime * bobFrequency * pace * Mathf.PI * 2f * Mathf.Min(1f, moving);
                float bob = Mathf.Sin(_bobPhase) * bobAmplitude * moving;
                float eye = _eyeBase * (_cc.height / standHeight);
                head.localPosition = new Vector3(0, eye + bob, 0);
                head.localRotation = Quaternion.Euler(_pitch, 0, Mathf.Sin(_bobPhase * 0.5f) * 0.3f * moving);
            }
        }
    }
}
