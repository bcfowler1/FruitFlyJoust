using UnityEditor;
using UnityEngine;
using FruitFlyJoust;

public static class SurfaceChecks
{
    [MenuItem("Fruit Fly/Validate Surface Orientation")]
    public static void Run()
    {
        Vector3[] normals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward,
            new Vector3(1, 1, 0).normalized };
        foreach (Vector3 normal in normals)
        {
            Quaternion pose = SurfaceGeometry.Pose(Vector3.forward, normal);
            if (Vector3.Dot(pose * Vector3.up, normal) < .999f ||
                Mathf.Abs(Vector3.Dot(pose * Vector3.forward, normal)) > .001f)
                throw new System.Exception("Invalid foot orientation for surface normal " + normal);
        }
        Quaternion floor=Quaternion.LookRotation(Vector3.forward,Vector3.up);
        Quaternion wall=SurfaceGeometry.CornerPose(floor,Vector3.back);
        Quaternion ceiling=SurfaceGeometry.CornerPose(wall,Vector3.down);
        if(Vector3.Dot(wall*Vector3.up,Vector3.back)<.999f || Vector3.Dot(wall*Vector3.forward,Vector3.up)<.999f)
            throw new System.Exception("Floor-to-wall 90-degree traversal frame is invalid.");
        if(Vector3.Dot(ceiling*Vector3.up,Vector3.down)<.999f)
            throw new System.Exception("Wall-to-ceiling 90-degree traversal frame is invalid.");
        Quaternion tableDown=SurfaceGeometry.CornerPose(floor,Vector3.forward);
        if(Vector3.Dot(tableDown*Vector3.up,Vector3.forward)<.999f || Vector3.Dot(tableDown*Vector3.forward,Vector3.down)<.999f)
            throw new System.Exception("Table-edge downward traversal frame is invalid.");
        var root=new GameObject("Temporary right-angle surface check");
        try
        {
            var testFloor=GameObject.CreatePrimitive(PrimitiveType.Cube);testFloor.transform.SetParent(root.transform);
            testFloor.transform.position=new Vector3(0,-.5f,0);testFloor.transform.localScale=new Vector3(6,1,6);
            var testWall=GameObject.CreatePrimitive(PrimitiveType.Cube);testWall.transform.SetParent(root.transform);
            testWall.transform.position=new Vector3(0,2.5f,3.5f);testWall.transform.localScale=new Vector3(6,6,1);
            var table=GameObject.CreatePrimitive(PrimitiveType.Cube);table.transform.SetParent(root.transform);
            table.transform.position=new Vector3(10,1,0);table.transform.localScale=new Vector3(4,2,4);
            Physics.SyncTransforms();
            if(!SurfaceGeometry.FindRightAngleSurface(new Vector3(0,.525f,2.7f),floor,testFloor.GetComponent<Collider>(),.525f,.28f,out var inner) ||
                Vector3.Dot(inner.normal,Vector3.back)<.99f)
                throw new System.Exception("Concave floor-to-wall probe did not find the 90-degree wall.");
            if(!SurfaceGeometry.FindRightAngleSurface(new Vector3(10,2.525f,1.8f),floor,table.GetComponent<Collider>(),.525f,.28f,out var outer) ||
                Vector3.Dot(outer.normal,Vector3.forward)<.99f)
                throw new System.Exception("Convex table-edge probe did not find the downward side face.");
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("Surface orientation checks passed: floor, wall, ceiling, slope, 90-degree climb, and table-edge descent.");
    }
}
