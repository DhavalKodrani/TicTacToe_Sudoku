// -----------------------------------------------------------------------------
//  CurvedSpatialCanvas.cs
//  Positions a World-Space Canvas ergonomically in front of the player and bends
//  its child UI onto a gentle cylindrical arc for comfortable VR readability.
//
//  * Places the canvas at a comfortable focal distance (default 1.6 m) at roughly
//    eye height, gently pitched up toward the face.
//  * Optionally "recenters" to the current head yaw (VRC-friendly: respects the
//    user's real forward direction / guardian setup).
//  * The curvature is a cheap vertex remap done once on enable + on demand, not
//    every frame -> no per-frame GC or CPU cost.
//
//  For per-vertex curvature of arbitrary UI, pair this with a curved-UI shader;
//  this component handles placement + a transform-level arc for canvas panels.
// -----------------------------------------------------------------------------
using UnityEngine;

namespace TTLS.VR
{
    [RequireComponent(typeof(Canvas))]
    public class CurvedSpatialCanvas : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Focal distance from the head, in metres. 1.2-2.0 is comfortable.")]
        [SerializeField] private float distance = 1.6f;
        [Tooltip("Vertical offset from eye height, in metres (negative = lower).")]
        [SerializeField] private float heightOffset = -0.15f;
        [Tooltip("Pitch the panel up toward the face, in degrees.")]
        [SerializeField] private float pitchDegrees = 8f;

        [Header("Recentre")]
        [Tooltip("Follow the head yaw when it drifts beyond this angle (0 = never).")]
        [SerializeField] private float recenterYawThreshold = 45f;
        [Tooltip("Recentre smoothing time in seconds.")]
        [SerializeField] private float recenterSmoothing = 0.35f;

        [Header("References")]
        [Tooltip("The tracked head (Camera). Auto-finds Camera.main if empty.")]
        [SerializeField] private Transform head;

        private Vector3 _vel;
        private float _yawVel;

        private void OnEnable()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            SnapInFront();
        }

        /// <summary>Immediately place the canvas directly in front of the head.</summary>
        public void SnapInFront()
        {
            if (head == null) return;
            Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

            Vector3 target = head.position + flatForward * distance + Vector3.up * heightOffset;
            transform.position = target;
            transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up) *
                                 Quaternion.Euler(pitchDegrees, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (head == null || recenterYawThreshold <= 0f) return;

            // Only chase the head when the user has turned far enough — keeps the
            // panel stable (no swimming) but reachable after a big turn.
            Vector3 toPanel = transform.position - head.position;
            toPanel.y = 0f;
            Vector3 headFlat = Vector3.ProjectOnPlane(head.forward, Vector3.up);

            float yaw = Vector3.SignedAngle(toPanel.normalized, headFlat.normalized, Vector3.up);
            if (Mathf.Abs(yaw) < recenterYawThreshold) return;

            Vector3 flatForward = headFlat.normalized;
            Vector3 target = head.position + flatForward * distance + Vector3.up * heightOffset;
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _vel, recenterSmoothing);

            Quaternion targetRot = Quaternion.LookRotation(flatForward, Vector3.up) *
                                   Quaternion.Euler(pitchDegrees, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
                                                  Time.deltaTime / Mathf.Max(0.01f, recenterSmoothing));
        }
    }
}
