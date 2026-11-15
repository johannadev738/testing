using UnityEngine;

namespace SimSafe
{
    public enum InteractKind { Character, Object }

    /// <summary>Marks something the officer can interact with using E.</summary>
    public sealed class Interactable : MonoBehaviour
    {
        public InteractKind Kind;
        public string CharacterId;
        public string DisplayName;
        public float Range = 3.5f;
        /// <summary>For objects: returns the scenario interaction id that applies right now, or null.</summary>
        public System.Func<SimSafe.Core.ScenarioSession, string> InteractionId;
    }

    /// <summary>
    /// First-person officer: WASD + mouse look, a deliberate walking pace (there is no sprint),
    /// and a short ray that finds the Interactable under the crosshair.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        public Transform CameraMount;
        public bool Active;
        public Interactable Focus;
        public float WalkSpeed = 2.0f;
        // Prevent the last typing/submit/cancel keystroke leaking back into movement.
        public int SuppressInputThroughFrame = -1;

        CharacterController cc;
        float yaw, pitch, velocityY;

        public static PlayerController Create()
        {
            var go = new GameObject("Officer");
            go.layer = 2; // Ignore Raycast, so the interaction ray never hits the player
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0f, 0.9f, 0f); cc.stepOffset = 0.3f;
            var p = go.AddComponent<PlayerController>();
            p.cc = cc;
            var mount = new GameObject("CameraMount").transform;
            mount.SetParent(go.transform, false);
            mount.localPosition = new Vector3(0f, 1.62f, 0f);
            p.CameraMount = mount;
            return p;
        }

        public void Teleport(Vector3 position, float yawDegrees)
        {
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
            yaw = yawDegrees; pitch = 0f; velocityY = 0f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            CameraMount.localRotation = Quaternion.identity;
        }

        /// <summary>Walks toward a point at a fixed pace and turns to face it. Returns true once within stopDistance.</summary>
        public bool WalkToward(Vector3 target, float stopDistance, float metresPerSecond, float dt)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            float dist = to.magnitude;
            float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, 140f * dt);
            pitch = Mathf.MoveTowards(pitch, 0f, 60f * dt);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            CameraMount.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (dist <= stopDistance) return true;
            Vector3 move = to.normalized * metresPerSecond;
            ApplyGravity(ref move, dt);
            cc.Move(move * dt);
            return false;
        }

        void ApplyGravity(ref Vector3 move, float dt)
        {
            if (cc.isGrounded && velocityY < 0f) velocityY = -1f;
            velocityY += -9.81f * dt;
            move.y = velocityY;
        }

        void Update()
        {
            if (!Active || Time.frameCount <= SuppressInputThroughFrame) { Focus = null; return; }
            float dt = Time.deltaTime;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float sens = 2f * GameSettings.MouseSensitivity;
                yaw += Input.GetAxis("Mouse X") * sens;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sens, -80f, 80f);
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            CameraMount.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            Vector3 input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")), 1f);
            Vector3 move = transform.TransformDirection(input) * WalkSpeed;
            ApplyGravity(ref move, dt);
            cc.Move(move * dt);

            Focus = null;
            var ray = new Ray(CameraMount.position, CameraMount.forward);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 5f, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                var i = hit.collider.GetComponentInParent<Interactable>();
                if (i != null && hit.distance <= i.Range) Focus = i;
            }
        }
    }
}
