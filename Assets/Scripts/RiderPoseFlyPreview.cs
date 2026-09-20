using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitFlyJoust
{
    // Editor pose reference built from the same exported mesh and rest pose as DetailedFlyVisual.
    [ExecuteAlways]
    public sealed class RiderPoseFlyPreview : MonoBehaviour
    {
        [Serializable] sealed class Pose { public float[] positions, rotations; }
        [Serializable] sealed class Data
        {
            public ResearchViewer.MeshData[] meshes;
            public ResearchViewer.GeomData[] geoms;
            public Pose[] poses;
        }

        Transform previewRoot;
        readonly List<Mesh> generatedMeshes = new List<Mesh>();
        readonly List<Material> generatedMaterials = new List<Material>();

        public void Rebuild()
        {
            Clear();
            var asset = Resources.Load<TextAsset>("FlyPlayableGeometry");
            if (!asset) { Debug.LogError("Rider pose lab could not load FlyPlayableGeometry."); return; }
            Data data;
            using (var stream = new MemoryStream(asset.bytes))
            using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
                data = JsonUtility.FromJson<Data>(reader.ReadToEnd());
            if (data == null || data.meshes == null || data.geoms == null || data.poses == null || data.poses.Length == 0)
            { Debug.LogError("Rider pose lab fly geometry is incomplete."); return; }

            previewRoot = new GameObject("Actual gameplay fly model (do not pose)").transform;
            previewRoot.SetParent(transform, false);
            previewRoot.localRotation = Quaternion.Euler(0, -90, 0); // Same as DetailedFlyVisual.
            var lookup = new Dictionary<int, Mesh>();
            foreach (var source in data.meshes)
            {
                var vertices = new Vector3[source.vertices.Length / 3];
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = ResearchViewer.Position(source.vertices[i*3], source.vertices[i*3+1], source.vertices[i*3+2]) * 500;
                var triangles = (int[])source.triangles.Clone();
                for (int i = 0; i < triangles.Length; i += 3)
                { int first = triangles[i]; triangles[i] = triangles[i+2]; triangles[i+2] = first; }
                var mesh = new Mesh { name = "Pose lab NeuroMechFly " + source.id, indexFormat = IndexFormat.UInt32 };
                mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                generatedMeshes.Add(mesh); lookup[source.id] = mesh;
            }
            var rest = data.poses[0];
            for (int i = 0; i < data.geoms.Length; i++)
            {
                var source = data.geoms[i]; int p = i * 3, q = i * 4;
                var part = new GameObject(source.name).transform; part.SetParent(previewRoot, false);
                part.localPosition = ResearchViewer.Position(rest.positions[p], rest.positions[p+1], rest.positions[p+2]) * 500;
                part.localRotation = ResearchViewer.Rotation(rest.rotations[q], rest.rotations[q+1], rest.rotations[q+2], rest.rotations[q+3]);
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = lookup[source.mesh];
                var material = ResearchViewer.CreateBodyMaterial(source); generatedMaterials.Add(material);
                part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        void OnEnable() { if (!previewRoot) Rebuild(); }
        void Clear()
        {
            var old = transform.Find("Actual gameplay fly model (do not pose)");
            if (old) DestroyImmediate(old.gameObject);
            foreach (var mesh in generatedMeshes) if (mesh) DestroyImmediate(mesh);
            foreach (var material in generatedMaterials) if (material) DestroyImmediate(material);
            generatedMeshes.Clear(); generatedMaterials.Clear(); previewRoot = null;
        }
        void OnDestroy() { Clear(); }
    }
}
