using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitFlyJoust
{
    // A node-and-internode plant grammar. Dimensions are Unity world units so a
    // designer can compare every generated organ directly with the rideable fly.
    [ExecuteAlways]
    public sealed class ProceduralPlant : MonoBehaviour
    {
        public enum LeafPattern { Spiral, Opposite, Whorled }

        [Header("Growth grammar")]
        public int seed = 17;
        [Range(4, 7)] public int stemFaces = 5;
        [Range(3, 11)] public int internodes = 7;
        [Range(.35f, 2f)] public float internodeLength = 1.05f;
        [Range(.25f, 1.2f)] public float stemRadius = .64f;
        [Range(.2f, .9f)] public float stemTipRadiusRatio = .48f;
        [Range(0, .35f)] public float stemBend = .09f;
        [Range(0, 30)] public float facetTwistDegrees = 7;
        [Range(0, .65f)] public float branchChance = .22f;
        [Range(15, 65)] public float branchAngle = 36;
        [Range(.35f, .85f)] public float branchLengthRatio = .58f;
        public LeafPattern leafPattern = LeafPattern.Spiral;
        [Range(60, 180)] public float spiralDivergenceDegrees = 137.5f;
        [Range(0, .25f)] public float variation = .09f;

        [Header("Leaf outline and posture")]
        [Range(2, 8)] public float leafLength = 4.8f;
        [Range(1.8f, 5)] public float leafWidth = 3.1f;
        [Range(.2f, .75f)] public float widestPoint = .38f;
        [Range(1, 5)] public float edgeFullness = 2.2f;
        [Range(.05f, .75f)] public float petioleLength = .32f;
        [Range(0, 45)] public float leafRiseDegrees = 18;
        [Range(0, 1.3f)] public float leafDroop = .42f;
        [Range(-.4f, .45f)] public float leafCup = .1f;
        [Range(.045f, .18f)] public float leafThickness = .09f;
        [Range(.3f, 1f)] public float tipLeafScale = .58f;

        [Header("Flowering and fruiting")]
        [Range(.5f, 2f)] public float flowerStalkLength = 1.15f;
        [Range(4, 8)] public int petalCount = 5;
        [Range(.3f, 1f)] public float flowerRadius = .63f;
        [Range(.2f, .7f)] public float fruitRadius = .40f;

        [Header("Placement")]
        public bool includePot = true;
        public bool hangingPlant;
        [Range(3, 8)] public int vineCount = 5;
        [Range(2f, 5f)] public float hangerLength = 3.8f;
        [Range(10, 18)] public int potFaces = 12;
        [Range(1.25f, 2.4f)] public float potRadius = 1.65f;
        [Range(1.4f, 2.6f)] public float potHeight = 2.0f;
        [Range(0, 25)] public float potFacetRotation = 12;

        [Header("Materials")]
        public Material stemMaterial;
        public Material leafUpperMaterial;
        public Material leafLowerMaterial;
        public Material veinMaterial;
        public Material potMaterial;
        public Material potRimMaterial;
        public Material soilMaterial;
        public Material budMaterial;
        public Material petalMaterial;
        public Material flowerCenterMaterial;
        public Material youngFruitMaterial;
        public Material ripeFruitMaterial;

        public int GeneratedStemCount { get; private set; }
        public int GeneratedLeafCount { get; private set; }
        public int GeneratedFlowerStalkCount { get; private set; }
        public float BaseHeight { get { return includePot ? potHeight - .12f : 0; } }

        void OnEnable()
        {
            if (Application.isPlaying && !transform.Find("Generated plant")) Rebuild();
        }

        void OnValidate()
        {
            stemFaces = Mathf.Clamp(stemFaces, 4, 7);
            internodes = Mathf.Clamp(internodes, 3, 11);
            potFaces = Mathf.Clamp(potFaces, 10, 18);
            petalCount = Mathf.Clamp(petalCount, 4, 8);
            vineCount = Mathf.Clamp(vineCount, 3, 8);
        }

        public void Rebuild()
        {
            var previous = transform.Find("Generated plant");
            if (previous)
            {
                if (Application.isPlaying) Destroy(previous.gameObject);
                else DestroyImmediate(previous.gameObject);
            }
            var generated = new GameObject("Generated plant").transform;
            generated.SetParent(transform, false);
            GeneratedStemCount = GeneratedLeafCount = GeneratedFlowerStalkCount = 0;
            if (includePot) BuildPot(generated);
            if (hangingPlant && includePot) BuildHanger(generated);
            var growth = new GameObject("Growth").transform;
            growth.SetParent(generated, false);
            growth.localPosition = Vector3.up * BaseHeight;
            var random = new System.Random(seed);
            if (hangingPlant) BuildHangingVines(growth, random);
            else BuildStem(growth, internodes, internodeLength, stemRadius, 0, random);
        }

        void BuildHangingVines(Transform growth, System.Random random)
        {
            for (int vine = 0; vine < vineCount; vine++)
            {
                float azimuth = (vine + .18f) * 360f / vineCount;
                float radians = azimuth * Mathf.Deg2Rad;
                var pivot = new GameObject("Trailing vine " + (vine + 1)).transform;
                pivot.SetParent(growth, false);
                pivot.localPosition = new Vector3(Mathf.Cos(radians), 0,
                    Mathf.Sin(radians)) * (potRadius * .68f);
                pivot.localRotation = Quaternion.AngleAxis(azimuth, Vector3.up) *
                    Quaternion.AngleAxis(157f + vine % 3 * 5f, Vector3.forward);
                float lengthVariation = .68f + .12f * (vine % 5);
                BuildStem(pivot, internodes, internodeLength * lengthVariation,
                    stemRadius * (.88f + .06f * (vine % 3)), 1, random);
                BuildLeaf(growth, 0, azimuth, .47f, 1);
            }
        }

        void BuildHanger(Transform generated)
        {
            var hanger = new GameObject("Ceiling hanger").transform;
            hanger.SetParent(generated, false);
            Vector3 hook = Vector3.up * (potHeight + hangerLength);
            for (int cable = 0; cable < 3; cable++)
            {
                float angle = (cable * 120f + potFacetRotation) * Mathf.Deg2Rad;
                Vector3 rim = new Vector3(Mathf.Cos(angle) * potRadius * .9f,
                    potHeight, Mathf.Sin(angle) * potRadius * .9f);
                AddHangerCable(hanger, "Suspension cable " + (cable + 1), rim, hook);
            }
            AddHangerCable(hanger, "Ceiling hook", hook,
                hook + Vector3.up * .15f);
        }

        void AddHangerCable(Transform parent, string name, Vector3 from, Vector3 to)
        {
            var cable = new GameObject(name).transform;
            cable.SetParent(parent, false);
            cable.localPosition = from;
            Vector3 delta = to - from;
            cable.localRotation = Quaternion.FromToRotation(Vector3.up, delta);
            AddMesh(cable.gameObject, StemMesh(5, 2, delta.magnitude, .035f,
                1f, 0, 0, 0), new[] { potRimMaterial }, false);
        }

        float Vary(System.Random random)
        { return 1f + ((float)random.NextDouble() * 2f - 1f) * variation; }

        void BuildStem(Transform parent, int nodeCount, float nodeLength, float radius, int depth, System.Random random)
        {
            if (nodeCount < 2) return;
            var stem = new GameObject(depth == 0 ? "Main faceted stem" : "Branch faceted stem").transform;
            stem.SetParent(parent, false);
            float length = nodeCount * nodeLength;
            var mesh = StemMesh(stemFaces, nodeCount * 2, length, radius, stemTipRadiusRatio,
                stemBend * (depth == 0 ? 1 : .55f), facetTwistDegrees, seed + GeneratedStemCount * 19);
            AddMesh(stem.gameObject, mesh, new[] { stemMaterial }, true);
            stem.gameObject.AddComponent<FlyGripSurface>().allowFacetWrap = true;
            GeneratedStemCount++;

            for (int node = 1; node <= nodeCount; node++)
            {
                float height = node * nodeLength;
                float yaw = leafPattern == LeafPattern.Spiral ? (node - 1) * spiralDivergenceDegrees :
                    leafPattern == LeafPattern.Opposite ? (node - 1) * 90f : (node - 1) * 37f;
                int leavesAtNode = leafPattern == LeafPattern.Spiral ? 1 :
                    leafPattern == LeafPattern.Opposite ? 2 : 3;
                float tipProgress = Mathf.Clamp01((node / (float)nodeCount - .55f) / .45f);
                float leafSize = Mathf.Lerp(1f, tipLeafScale,
                    tipProgress * tipProgress * (3f - 2f * tipProgress));
                for (int leaf = 0; leaf < leavesAtNode; leaf++)
                {
                    float angle = yaw + leaf * 360f / leavesAtNode;
                    BuildLeaf(stem, height, angle, Vary(random) * leafSize, depth);
                }
                if (depth == 0)
                {
                    FlowerStage stage = node == 2 ? FlowerStage.RipeFruit :
                        node == 3 ? FlowerStage.YoungFruit :
                        node == nodeCount - 1 ? FlowerStage.OpenFlower :
                        node == nodeCount ? FlowerStage.Bud : FlowerStage.None;
                    if (stage != FlowerStage.None)
                        BuildFlowerStalk(stem, height, yaw + 155f, stage);
                }
                if (depth == 0 && node > 1 && node < nodeCount - 1 && random.NextDouble() < branchChance)
                {
                    var branchPivot = new GameObject("Branch node " + node).transform;
                    branchPivot.SetParent(stem, false);
                    branchPivot.localPosition = new Vector3(0, height, 0);
                    branchPivot.localRotation = Quaternion.AngleAxis(yaw + 77, Vector3.up) *
                        Quaternion.AngleAxis(branchAngle * Vary(random), Vector3.forward);
                    int branchNodes = Mathf.Max(2, Mathf.RoundToInt(nodeCount * branchLengthRatio));
                    BuildStem(branchPivot, branchNodes, nodeLength * branchLengthRatio,
                        radius * .6f, depth + 1, random);
                }
            }
        }

        enum FlowerStage { None, Bud, OpenFlower, YoungFruit, RipeFruit }

        void BuildFlowerStalk(Transform stem, float height, float azimuth, FlowerStage stage)
        {
            string stageName = stage == FlowerStage.OpenFlower ? "Open flower" :
                stage == FlowerStage.YoungFruit ? "Young fruit" :
                stage == FlowerStage.RipeFruit ? "Ripe fruit" : "Bud";
            var stalk = new GameObject(stageName + " stalk").transform;
            stalk.SetParent(stem, false);
            stalk.localPosition = Vector3.up * height;
            stalk.localRotation = Quaternion.AngleAxis(azimuth, Vector3.up) *
                Quaternion.AngleAxis(62, Vector3.forward);
            float length = flowerStalkLength * (stage == FlowerStage.Bud ? .8f : 1f);
            AddMesh(stalk.gameObject, StemMesh(5, 3, length, .075f, .55f, .04f, 0, 0),
                new[] { stemMaterial }, false);
            var head = new GameObject(stageName).transform;
            head.SetParent(stalk, false);
            head.localPosition = Vector3.up * length;
            if (stage == FlowerStage.Bud)
                AddMesh(head.gameObject, BulbMesh(9, 7, flowerRadius * .42f, flowerRadius * .85f),
                    new[] { budMaterial }, false);
            else if (stage == FlowerStage.OpenFlower)
            {
                AddMesh(head.gameObject, BlossomMesh(petalCount, flowerRadius),
                    new[] { petalMaterial }, false);
                var center = new GameObject("Pollen center");
                center.transform.SetParent(head, false);
                center.transform.localPosition = Vector3.up * .04f;
                AddMesh(center, BulbMesh(9, 5, flowerRadius * .21f, flowerRadius * .38f),
                    new[] { flowerCenterMaterial }, false);
            }
            else
            {
                bool ripe = stage == FlowerStage.RipeFruit;
                float size = fruitRadius * (ripe ? 1f : .68f);
                AddMesh(head.gameObject, BulbMesh(10, 8, size, size * 1.42f),
                    new[] { ripe ? ripeFruitMaterial : youngFruitMaterial }, false);
                var calyx = new GameObject("Persistent calyx");
                calyx.transform.SetParent(head, false);
                AddMesh(calyx, BlossomMesh(5, size * .62f), new[] { budMaterial }, false);
            }
            GeneratedFlowerStalkCount++;
        }

        static Mesh BlossomMesh(int petals, float radius)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int p = 0; p < petals; p++)
            {
                float angle = p * Mathf.PI * 2f / petals;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 across = new Vector3(-radial.z, 0, radial.x);
                int first = vertices.Count;
                vertices.Add(radial * radius * .09f + Vector3.up * .08f);
                vertices.Add(radial * radius * .53f - across * radius * .36f + Vector3.up * .18f);
                vertices.Add(radial * radius + Vector3.up * .03f);
                vertices.Add(radial * radius * .53f + across * radius * .36f + Vector3.up * .18f);
                int[] front = { 0, 1, 2, 0, 2, 3 };
                for (int i = 0; i < front.Length; i += 3)
                {
                    triangles.Add(first + front[i]);
                    triangles.Add(first + front[i + 1]);
                    triangles.Add(first + front[i + 2]);
                    triangles.Add(first + front[i + 2]);
                    triangles.Add(first + front[i + 1]);
                    triangles.Add(first + front[i]);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BulbMesh(int sides, int rings, float radius, float height)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int ring = 0; ring <= rings; ring++)
            {
                float t = ring / (float)rings;
                float width = radius * Mathf.Sin(Mathf.PI * t) * (1.16f - .32f * t);
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * width, t * height,
                        Mathf.Sin(angle) * width));
                    if (ring == rings) continue;
                    int a = ring * sides + side, b = ring * sides + (side + 1) % sides;
                    int c = (ring + 1) * sides + side, d = (ring + 1) * sides + (side + 1) % sides;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void BuildLeaf(Transform stem, float height, float azimuth, float scale, int depth)
        {
            var leaf = new GameObject("Leaf " + (GeneratedLeafCount + 1)).transform;
            leaf.SetParent(stem, false);
            leaf.localPosition = Vector3.up * height;
            leaf.localRotation = Quaternion.AngleAxis(azimuth, Vector3.up) *
                Quaternion.AngleAxis(-leafRiseDegrees, Vector3.right);
            float length = leafLength * scale * (depth == 0 ? 1 : .78f);
            float width = leafWidth * scale * (depth == 0 ? 1 : .82f);
            var stalk = new GameObject("Petiole");
            stalk.transform.SetParent(leaf, false);
            stalk.transform.localRotation = Quaternion.Euler(90, 0, 0);
            AddMesh(stalk, StemMesh(5, 2, petioleLength, .105f, .8f, 0, 0, 0),
                new[] { stemMaterial }, false);
            var blade = new GameObject("Closed leaf blade");
            blade.transform.SetParent(leaf, false);
            AddMesh(blade, LeafMesh(length, width),
                new[] { leafUpperMaterial, leafLowerMaterial }, true);
            blade.AddComponent<FlyGripSurface>();
            var veins = new GameObject("Upper and lower veins");
            veins.transform.SetParent(leaf, false);
            AddMesh(veins, VeinMesh(length, width), new[] { veinMaterial }, false);
            GeneratedLeafCount++;
        }

        void AddMesh(GameObject obj, Mesh mesh, Material[] materials, bool collision)
        {
            mesh.name = obj.name + " mesh";
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterials = materials;
            if (collision) obj.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static Mesh StemMesh(int faces, int ringCount, float length, float radius, float tipRatio,
            float bend, float twist, int phase)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float phaseAngle = phase * .37f;
            for (int ring = 0; ring <= ringCount; ring++)
            {
                float t = ring / (float)ringCount;
                float r = radius * Mathf.Lerp(1, tipRatio, t);
                Vector3 center = new Vector3(Mathf.Sin(t * 2.2f + phaseAngle) * bend * t * t,
                    t * length, Mathf.Cos(t * 2.2f + phaseAngle) * bend * t * t);
                for (int face = 0; face < faces; face++)
                {
                    float a = (face / (float)faces * 360 + twist * t) * Mathf.Deg2Rad;
                    float b = ((face + 1f) / faces * 360 + twist * t) * Mathf.Deg2Rad;
                    vertices.Add(center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                    vertices.Add(center + new Vector3(Mathf.Cos(b) * r, 0, Mathf.Sin(b) * r));
                    if (ring == ringCount) continue;
                    int i = (ring * faces + face) * 2;
                    int next = i + faces * 2;
                    triangles.Add(i); triangles.Add(next); triangles.Add(i + 1);
                    triangles.Add(i + 1); triangles.Add(next); triangles.Add(next + 1);
                }
            }
            int bottomCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            int topCenter = vertices.Count;
            vertices.Add(new Vector3(Mathf.Sin(2.2f + phaseAngle) * bend, length,
                Mathf.Cos(2.2f + phaseAngle) * bend));
            for (int face = 0; face < faces; face++)
            {
                int first = face * 2;
                triangles.Add(bottomCenter); triangles.Add(first + 1); triangles.Add(first);
                int last = (ringCount * faces + face) * 2;
                triangles.Add(topCenter); triangles.Add(last); triangles.Add(last + 1);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        float LeafWidthProfile(float t)
        {
            float peak = Mathf.Clamp(widestPoint, .2f, .75f);
            float alpha = Mathf.Max(.2f, peak * edgeFullness);
            float beta = Mathf.Max(.2f, (1 - peak) * edgeFullness);
            float clamped = Mathf.Clamp(t, .0001f, .9999f);
            float maximum = Mathf.Pow(peak, alpha) * Mathf.Pow(1 - peak, beta);
            return Mathf.Max(.015f, Mathf.Pow(clamped, alpha) * Mathf.Pow(1 - clamped, beta) / maximum);
        }

        Vector3 LeafPoint(float t, float s, float length, float width, float face)
        {
            float outline = LeafWidthProfile(t);
            float x = s * width * .5f * outline;
            float y = Mathf.Sin(Mathf.PI * t) * .16f - leafDroop * t * t +
                leafCup * s * s * Mathf.Sin(Mathf.PI * t) + face * leafThickness * .5f;
            return new Vector3(x, y, petioleLength + t * length);
        }

        Mesh LeafMesh(float length, float width)
        {
            const int rows = 28, columns = 12;
            int sideSize = (rows + 1) * (columns + 1);
            var vertices = new List<Vector3>(sideSize * 2);
            var top = new List<int>();
            var lowerAndEdge = new List<int>();
            for (int side = 0; side < 2; side++)
                for (int row = 0; row <= rows; row++)
                    for (int column = 0; column <= columns; column++)
                        vertices.Add(LeafPoint(row / (float)rows, column / (float)columns * 2 - 1,
                            length, width, side == 0 ? 1 : -1));
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int a = row * (columns + 1) + column;
                    int b = a + columns + 1;
                    top.Add(a); top.Add(b); top.Add(a + 1);
                    top.Add(a + 1); top.Add(b); top.Add(b + 1);
                    a += sideSize; b += sideSize;
                    lowerAndEdge.Add(a); lowerAndEdge.Add(a + 1); lowerAndEdge.Add(b);
                    lowerAndEdge.Add(a + 1); lowerAndEdge.Add(b + 1); lowerAndEdge.Add(b);
                }
            for (int row = 0; row < rows; row++)
                for (int edge = 0; edge <= columns; edge += columns)
                {
                    int a = row * (columns + 1) + edge;
                    int b = a + columns + 1;
                    AddWall(lowerAndEdge, a, b, b + sideSize, a + sideSize, edge == 0);
                }
            for (int column = 0; column < columns; column++)
            {
                AddWall(lowerAndEdge, column, column + 1, column + 1 + sideSize,
                    column + sideSize, true);
                int a = rows * (columns + 1) + column;
                AddWall(lowerAndEdge, a, a + 1, a + 1 + sideSize, a + sideSize, false);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.subMeshCount = 2;
            mesh.SetTriangles(top, 0); mesh.SetTriangles(lowerAndEdge, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static void AddWall(List<int> triangles, int a, int b, int c, int d, bool reverse)
        {
            if (reverse)
            { triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(a); triangles.Add(d); triangles.Add(c); }
            else
            { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(a); triangles.Add(c); triangles.Add(d); }
        }

        Mesh VeinMesh(float length, float width)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int face = -1; face <= 1; face += 2)
            {
                AddVein(vertices, triangles, length, width, .05f, 0, .96f, 0, face, .027f);
                for (int i = 1; i <= 4; i++)
                {
                    float t = i / 6f;
                    AddVein(vertices, triangles, length, width, t, 0,
                        Mathf.Min(.95f, t + .15f), -.84f, face, .013f);
                    AddVein(vertices, triangles, length, width, t, 0,
                        Mathf.Min(.95f, t + .15f), .84f, face, .013f);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void AddVein(List<Vector3> vertices, List<int> triangles, float length, float width,
            float startT, float startS, float endT, float endS, float face, float halfWidth)
        {
            const int segments = 8;
            int first = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float q = i / (float)segments;
                float t = Mathf.Lerp(startT, endT, q), s = Mathf.Lerp(startS, endS, q);
                Vector3 center = LeafPoint(t, s, length, width, face) + Vector3.up * face * .009f;
                Vector3 along = new Vector3((endS - startS) * width * .5f, 0, (endT - startT) * length).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, along).normalized * halfWidth;
                vertices.Add(center - across); vertices.Add(center + across);
                if (i == segments) continue;
                int a = first + i * 2;
                if (face > 0)
                { triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                  triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3); }
                else
                { triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
                  triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2); }
            }
        }

        void BuildPot(Transform generated)
        {
            var pot = new GameObject("Removable rotated-facet pot").transform;
            pot.SetParent(generated, false);
            var shell = new GameObject("Twisted cylindrical ceramic");
            shell.transform.SetParent(pot, false);
            AddMesh(shell, PotShellMesh(), new[] { potMaterial }, true);
            var rim = new GameObject("Faceted contrasting rim");
            rim.transform.SetParent(pot, false);
            AddMesh(rim, RingMesh(potFaces, potRadius * .94f, potRadius * 1.045f,
                potHeight - .12f, potHeight + .08f, potFacetRotation), new[] { potRimMaterial }, true);
            var soil = new GameObject("Soil surface");
            soil.transform.SetParent(pot, false);
            AddMesh(soil, DiskMesh(potFaces, potRadius * .9f, BaseHeight),
                new[] { soilMaterial }, true);
        }

        Mesh PotShellMesh()
        {
            int faces = potFaces;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] radii = { potRadius * .84f, potRadius * .88f, potRadius * .97f, potRadius };
            float[] heights = { 0, potHeight * .18f, potHeight * .83f, potHeight };
            for (int ring = 0; ring < radii.Length; ring++)
                for (int side = 0; side < faces; side++)
                {
                    float angle = (side * 360f / faces + potFacetRotation * ring / 3f) * Mathf.Deg2Rad;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radii[ring], heights[ring],
                        Mathf.Sin(angle) * radii[ring]));
                }
            for (int ring = 0; ring < radii.Length - 1; ring++)
                for (int side = 0; side < faces; side++)
                {
                    int next = (side + 1) % faces;
                    int a = ring * faces + side, b = ring * faces + next;
                    int c = (ring + 1) * faces + side, d = (ring + 1) * faces + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh RingMesh(int faces, float inner, float outer, float bottom, float top, float twist)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int ring = 0; ring < 4; ring++)
            {
                float radius = ring < 2 ? outer : inner;
                float height = ring % 2 == 0 ? bottom : top;
                for (int side = 0; side < faces; side++)
                {
                    float angle = (side * 360f / faces + twist) * Mathf.Deg2Rad;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, height,
                        Mathf.Sin(angle) * radius));
                }
            }
            for (int side = 0; side < faces; side++)
            {
                int next = (side + 1) % faces;
                for (int wall = 0; wall < 4; wall++)
                {
                    int a = wall * faces + side, b = wall * faces + next;
                    int c = ((wall + 1) % 4) * faces + side, d = ((wall + 1) % 4) * faces + next;
                    if (wall == 2)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(b); triangles.Add(d); triangles.Add(c);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh DiskMesh(int faces, float radius, float height)
        {
            var vertices = new List<Vector3> { new Vector3(0, height, 0) };
            var triangles = new List<int>();
            for (int side = 0; side < faces; side++)
            {
                float angle = side * 2 * Mathf.PI / faces;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, height,
                    Mathf.Sin(angle) * radius));
            }
            for (int side = 0; side < faces; side++)
            {
                triangles.Add(0); triangles.Add(1 + (side + 1) % faces);
                triangles.Add(1 + side);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }

}
