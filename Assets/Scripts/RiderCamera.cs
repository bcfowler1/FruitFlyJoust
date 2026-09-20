using UnityEngine;

namespace FruitFlyJoust
{
    [DefaultExecutionOrder(100)]
    public sealed class RiderCamera : MonoBehaviour
    {
        public Transform fly;
        public RiderInput rider;
        public float anchorHeight = 1.25f;
        public float cameraRise = .6f;
        public bool followAnchorRotation = true;
        [Range(30, 240)] public float horizontalSensitivity = 120;
        [Range(30, 180)] public float verticalSensitivity = 100;
        private float yaw, pitch = 12;
        private Quaternion surfaceFrame = Quaternion.identity;
        public float FrameTiltDegrees { get { return Vector3.Angle(surfaceFrame*Vector3.up,Vector3.up); } }
        private Transform previousFly;
        private Vector3 previousFlyPosition;
        private Transform orientationSource;
        private bool stableOrientationActive;
        private Quaternion stableOrientation=Quaternion.identity;
        public bool TrackingRiderHead { get { return orientationSource || stableOrientationActive; } }
        public bool TrackingStableRiderFrame { get { return stableOrientationActive; } }
        public bool TrackingStableFlyFrame { get { return orientationSource==fly; } }
        private Quaternion sourceToAnchor=Quaternion.identity;
        public void SetOrientationSource(Transform source,Transform reference)
        {
            stableOrientationActive=false;
            if(source==orientationSource)return;
            orientationSource=source;
            if(source && reference)sourceToAnchor=Quaternion.Inverse(source.rotation)*reference.rotation;
        }
        public void SetStableOrientation(Quaternion rotation)
        {
            orientationSource=null;stableOrientationActive=true;stableOrientation=rotation;
        }
        void LateUpdate()
        {
            if (previousFly == fly) transform.position += fly.position - previousFlyPosition;
            previousFly = fly; previousFlyPosition = fly.position;
            if (rider.recenter || rider.resetRide) { yaw = 0; pitch = 12; }
            yaw += rider.look.x * horizontalSensitivity * Time.deltaTime;
            pitch -= rider.look.y * verticalSensitivity * Time.deltaTime;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxis("Mouse X") * 2;
                pitch -= Input.GetAxis("Mouse Y") * 2;
            }
            yaw = Mathf.Repeat(yaw + 180, 360) - 180;
            pitch = Mathf.Clamp(pitch, -65, 75);
            // Follow the fly's surface orientation smoothly while keeping independent head movement.
            // Mounted view follows the fly's changing surface frame. On foot, retain the camera frame:
            // turning the avatar must not suddenly orbit the camera around it.
            if(followAnchorRotation)
            {
                Quaternion targetFrame=stableOrientationActive ? stableOrientation : orientationSource ? orientationSource.rotation*sourceToAnchor : fly.rotation;
                surfaceFrame = Quaternion.Slerp(surfaceFrame,targetFrame,1-Mathf.Exp(-4*Time.deltaTime));
            }
            else
            {
                // On foot retain the current compass heading, but shed the mounted roll/pitch.
                Vector3 levelForward=Vector3.ProjectOnPlane(surfaceFrame*Vector3.forward,Vector3.up);
                if(levelForward.sqrMagnitude<.001f)levelForward=Vector3.ProjectOnPlane(transform.forward,Vector3.up);
                Quaternion levelFrame=levelForward.sqrMagnitude>.001f ? Quaternion.LookRotation(levelForward.normalized,Vector3.up) : Quaternion.identity;
                surfaceFrame=Quaternion.Slerp(surfaceFrame,levelFrame,1-Mathf.Exp(-5*Time.deltaTime));
            }
            Quaternion rotation = surfaceFrame * Quaternion.Euler(pitch, yaw, 0);
            Vector3 anchor = fly.position + fly.up * anchorHeight;
            Vector3 desired = anchor + rotation * new Vector3(0, cameraRise, -4);
            Vector3 delta = desired - anchor;
            if (Physics.SphereCast(anchor, .2f, delta.normalized, out var hit, delta.magnitude, 1,
                QueryTriggerInteraction.Ignore)) desired = anchor + delta.normalized * Mathf.Max(.2f, hit.distance - .15f);
            transform.position = Vector3.Lerp(transform.position, desired, 1 - Mathf.Exp(-12 * Time.deltaTime));
            transform.rotation = rotation;
        }
    }
}
