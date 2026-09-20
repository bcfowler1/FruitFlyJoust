using UnityEngine;

namespace FruitFlyJoust
{
    public static class SurfaceGeometry
    {
        public static Quaternion Pose(Vector3 forward, Vector3 normal)
        {
            normal.Normalize();
            Vector3 tangent = Vector3.ProjectOnPlane(forward, normal);
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.ProjectOnPlane(Vector3.forward, normal);
            return Quaternion.LookRotation(tangent.normalized, normal);
        }
        public static float Clearance(CapsuleCollider body, Vector3 surfaceNormal)
        {
            // Capsule's centerline is along local Z in this prototype.
            Vector3 scale = body.transform.lossyScale;
            float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            float halfLine = Mathf.Max(0, body.height * Mathf.Abs(scale.z) * .5f - radius);
            return radius + halfLine * Mathf.Abs(Vector3.Dot(body.transform.forward, surfaceNormal)) + .025f;
        }
        public static Quaternion CornerPose(Quaternion currentPose, Vector3 nextNormal)
        {
            Vector3 currentNormal=currentPose*Vector3.up;
            Vector3 bentForward=Quaternion.FromToRotation(currentNormal,nextNormal)*(currentPose*Vector3.forward);
            return Pose(bentForward,nextNormal);
        }
        public static bool FindRightAngleSurface(Vector3 position,Quaternion pose,Collider current,
            float clearance,float probeDistance,out RaycastHit corner)
        {
            Vector3 normal=pose*Vector3.up,travel=pose*Vector3.forward;
            float reach=Mathf.Max(.2f,clearance+probeDistance);
            // Concave corner: the next plane is directly ahead (floor -> wall, wall -> ceiling).
            if(Physics.Raycast(position+normal*.03f,travel,out corner,reach,1,QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(Vector3.Dot(normal,corner.normal))<.25f)return true;
            // Convex corner: probe outside and below the lip, back toward its vertical face.
            Vector3 outside=position+travel*reach-normal*reach;
            if(Physics.Raycast(outside,-travel,out corner,reach*2,1,QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(Vector3.Dot(normal,corner.normal))<.25f)return true;
            corner=default(RaycastHit);return false;
        }
    }
}
