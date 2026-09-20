using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitFlyJoust
{
    [DefaultExecutionOrder(-40)]
    public sealed class ResearchViewer : MonoBehaviour
    {
        [Serializable] public class MeshData { public int id; public float[] vertices; public int[] triangles; }
        [Serializable] public class GeomData { public string name; public int mesh; public float[] rgba; public float[] pivot; }
        [Serializable] public class Geometry { public MeshData[] meshes; public GeomData[] geoms; }
        [Serializable] public class Frame
        {
            public int protocol, seq, neurons, connections, spikes, active, clipped_actions;
            public string session, body_controller, episode_status;
            public string perch_phase;
            public string walking_surface;
            public bool fast_averaged;
            public float sim_time, neural_time, rate_hz, compute_ratio;
            public float[] positions, rotations;
            public float[] anchor_position, anchor_rotation;
            public float[] surface_position, surface_normal;
            public int contacting_claws;
            public int body_contacts;
            public bool adhesion_enabled;
            public bool paused, episode_ended;
            public bool rider_attached = true;
            public string idle_behavior;
            public bool head_stabilization;
            public bool upstream_steering;
        }
        [Serializable] private class Request
        {
            public int protocol = 1;
            public float rate_hz, turn, climb;
            public float neural_influence, adhesion_strength;
            public bool paused, reset, spur_press, brake_press, land_hold;
            public bool rider_attached = true;
            public bool autonomous_idle;
        }

        public Transform riderAnchor;
        public bool trainedFlight;
        public bool unifiedLab;
        public bool walkingLab;
        public bool autonomousIdle = true;
        public string geometryResource = "FlyResearchGeometry";
        public float displayUnitsPerMeter = 500;
        public float stimulusHz = 200;
        private UdpClient receiver, sender;
        private Transform[] geoms;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private Frame frame;
        private string session;
        private int sequence = -1;
        private float lastFrame, lastRequest;
        private bool stimulate, paused;
        private bool resetPending;
        private bool spurPending, brakePending;
        private bool landingPending;
        private RiderInput riderInput;
        private string error;
        public Frame CurrentFrame { get { return frame; } }
        public float BodyDeltaTime { get; private set; }
        public Vector3 BodyVelocity { get; private set; }
        public bool Connected { get { return frame != null && Time.unscaledTime-lastFrame < 2 && error == null; } }
        public bool IsPerched { get { return Connected && frame.perch_phase == "perched" && frame.contacting_claws >= 3; } }
        public RiderCombat Combat { get; private set; }
        public void RequestReset() { resetPending = true; }
        public void RequestLanding() { landingPending = true; }
        public void RequestPause(bool value) { paused=value; }

        // Reflection swaps Y/Z; quaternion imaginary components reverse sign.
        public static Vector3 Position(float x, float y, float z) { return new Vector3(x, z, y); }
        public static Quaternion Rotation(float w, float x, float y, float z) { return new Quaternion(-x, -z, -y, w); }

        void Start()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f,.55f,.55f);
            try
            {
                var asset = Resources.Load<TextAsset>(geometryResource);
                if (!asset) throw new Exception("Research geometry missing; run Research/export_body.py");
                Geometry geometry;
                using (var data = new MemoryStream(asset.bytes))
                using (var gzip = new GZipStream(data, CompressionMode.Decompress))
                using (var text = new StreamReader(gzip)) geometry = JsonUtility.FromJson<Geometry>(text.ReadToEnd());
                var lookup = new Dictionary<int, Mesh>();
                foreach (MeshData source in geometry.meshes)
                {
                    var vertices = new Vector3[source.vertices.Length / 3];
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = Position(source.vertices[i*3],
                        source.vertices[i*3+1], source.vertices[i*3+2]) * displayUnitsPerMeter;
                    for (int i = 0; i < source.triangles.Length; i += 3)
                    { int first = source.triangles[i]; source.triangles[i] = source.triangles[i+2]; source.triangles[i+2] = first; }
                    var mesh = new Mesh { name = "FlyBody mesh " + source.id, indexFormat = IndexFormat.UInt32 };
                    mesh.vertices = vertices; mesh.triangles = source.triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    lookup.Add(source.id, mesh); meshes.Add(mesh);
                }
                geoms = new Transform[geometry.geoms.Length];
                for (int i = 0; i < geoms.Length; i++)
                {
                    var source = geometry.geoms[i];
                    var obj = new GameObject(source.name); obj.transform.SetParent(transform, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = lookup[source.mesh];
                    var material = CreateBodyMaterial(source);
                    obj.AddComponent<MeshRenderer>().sharedMaterial = material; materials.Add(material);
                    geoms[i] = obj.transform;
                    obj.SetActive(false);
                }
                receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 55371));
                receiver.Client.Blocking = false;
                sender = new UdpClient();
                lastFrame = Time.unscaledTime;
                if (riderAnchor) riderInput = riderAnchor.GetComponent<RiderInput>();
                if (riderAnchor)
                {
                    var old = riderAnchor.GetComponentsInChildren<Renderer>();
                    var visual = gameObject.AddComponent<RiderAnimationVisual>();
                    if (walkingLab || unifiedLab)
                    {
                        visual.visualScale = .78f;
                        visual.mountedSeatHeight = -.33f;
                        visual.mountedSeatForward = -.16f;
                    }
                    if (visual.Create(riderAnchor, old.Length > 0 ? old[0].sharedMaterial : materials[0]))
                    { foreach (var renderer in old) renderer.enabled = false; visual.Pose(riderAnchor, true, 0); }
                    if (walkingLab)
                    {
                        Combat = gameObject.AddComponent<RiderCombat>();
                        Combat.research = this;
                        Combat.mountedVisual = riderAnchor.Find("Mounted rider");
                        Combat.view = FindObjectOfType<RiderCamera>();
                        Combat.riderMaterial = old.Length > 0 ? old[0].sharedMaterial : materials[0];
                        Combat.weaponMaterial = Combat.riderMaterial;
                        Combat.existingAnimation = visual;
                        Combat.message = "Combat on the streamed body; contact handover required to dismount.";
                        var floor = GameObject.Find("Display floor");
                        if (floor) floor.transform.localScale = Vector3.one*20;
                        if (floor && !floor.GetComponent<Collider>()) floor.AddComponent<MeshCollider>();
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; Debug.LogError("Research viewer: " + error); }
        }

        void Update()
        {
            BodyDeltaTime = 0;
            Frame previousFrame = frame;
            Vector3 previousAnchor = riderAnchor ? riderAnchor.position : Vector3.zero;
            if (error != null || receiver == null) return;
            if (Input.GetKeyDown(KeyCode.T)) stimulate = !stimulate;
            if (Input.GetKeyDown(KeyCode.P)) paused = !paused;
            if (trainedFlight || walkingLab || frame != null && frame.surface_normal != null)
            {
                if (Input.GetKeyDown(KeyCode.Backspace) || riderInput && riderInput.resetRide) resetPending = true;
                if (riderInput) { spurPending |= riderInput.ConsumeSpur(); brakePending |= riderInput.ConsumeBrake(); }
            }
            if (Time.unscaledTime - lastRequest > .1f)
            {
                byte[] request = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Request
                    { rate_hz = stimulate ? stimulusHz : 0, paused = paused, reset = resetPending,
                        turn = riderInput && (!Combat || Combat.Mounted) ? riderInput.reins.x : 0,
                        climb = riderInput ? Mathf.Clamp(-riderInput.reins.y + riderInput.lift, -1, 1) : 0,
                        spur_press = spurPending, brake_press = brakePending,
                          land_hold = landingPending || Combat && !Combat.Mounted || riderInput && riderInput.land,
                          neural_influence = SimulationControls.NeuralInfluence,
                          adhesion_strength = SimulationControls.GripStrength,
                          autonomous_idle = autonomousIdle,
                          rider_attached = !Combat || Combat.Mounted }));
                sender.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, 55370));
                resetPending = false;
                spurPending = brakePending = false;
                lastRequest = Time.unscaledTime;
            }
            // Drain queued frames; apply only the newest valid state to avoid visual lag.
            while (receiver.Available > 0)
            {
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] packet = receiver.Receive(ref endpoint);
                if (!IPAddress.IsLoopback(endpoint.Address)) continue;
                try
                {
                    Frame candidate = JsonUtility.FromJson<Frame>(Encoding.UTF8.GetString(packet));
                    if (candidate.protocol != 1 || candidate.positions == null || candidate.rotations == null ||
                        candidate.positions.Length != geoms.Length * 3 || candidate.rotations.Length != geoms.Length * 4 ||
                        candidate.anchor_position == null || candidate.anchor_position.Length != 3 ||
                        candidate.anchor_rotation == null || candidate.anchor_rotation.Length != 4 ||
                        !Valid(candidate.anchor_position) || !Valid(candidate.anchor_rotation) ||
                        !Valid(candidate.positions) || !Valid(candidate.rotations)) continue;
                    if (candidate.session != session) { session = candidate.session; sequence = -1; }
                    if (candidate.seq <= sequence) continue;
                    frame = candidate; sequence = candidate.seq; lastFrame = Time.unscaledTime;
                }
                catch (ArgumentException) { }
            }
            if (frame == null) return;
            if (frame.perch_phase == "perched") landingPending = false;
            if (frame.surface_position != null && frame.surface_position.Length == 3 && Valid(frame.surface_position) &&
                frame.surface_normal != null && frame.surface_normal.Length == 3 && Valid(frame.surface_normal))
            {
                var surface = GameObject.Find("Display floor");
                if (surface)
                {
                    surface.transform.position = Position(frame.surface_position[0],frame.surface_position[1],frame.surface_position[2])*displayUnitsPerMeter;
                    Vector3 normal = Position(frame.surface_normal[0],frame.surface_normal[1],frame.surface_normal[2]);
                    if (normal.sqrMagnitude > .5f) surface.transform.rotation = Quaternion.FromToRotation(Vector3.up,normal);
                }
            }
            for (int i = 0; i < geoms.Length; i++)
            {
                int p = i*3, q = i*4;
                geoms[i].gameObject.SetActive(true);
                geoms[i].localPosition = Position(frame.positions[p], frame.positions[p+1], frame.positions[p+2]) * displayUnitsPerMeter;
                geoms[i].localRotation = Rotation(frame.rotations[q], frame.rotations[q+1], frame.rotations[q+2], frame.rotations[q+3]);
            }
            if (riderAnchor)
            {
                float[] p = frame.anchor_position, q = frame.anchor_rotation;
                riderAnchor.position = transform.TransformPoint(Position(p[0], p[1], p[2]) * displayUnitsPerMeter);
                // FlyBody body X points forward; Unity rider Z points forward.
                riderAnchor.rotation = transform.rotation * Rotation(q[0], q[1], q[2], q[3]) * Quaternion.Euler(0, 90, 0);
                if (previousFrame != null && previousFrame.session == frame.session && !frame.paused)
                    BodyDeltaTime = Mathf.Max(0, frame.sim_time-previousFrame.sim_time);
                BodyVelocity = BodyDeltaTime > 0 ? (riderAnchor.position-previousAnchor)/BodyDeltaTime : BodyVelocity;
            }
        }
        static bool Valid(float[] values)
        { foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) return false; return true; }

        void OnGUI()
        {
            GUI.color = new Color(.04f, .08f, .12f, .95f);
            GUI.DrawTexture(new Rect(16, 16, 700, 165), Texture2D.whiteTexture); GUI.color = Color.white;
            if (walkingLab)
            {
                GUI.Label(new Rect(30,25,670,24), "NEUROMECHFLY WALKING LAB — articulated legs + one-third-mass rider");
                GUI.Label(new Rect(30,50,670,24), error ?? (frame == null ? "Starting backend…" :
                    (Time.unscaledTime-lastFrame > 2 ? "Stream disconnected" : frame.paused ? "Paused" : "Active") +
                    "   Brain: " + frame.neurons + (frame.upstream_steering ? " neurons (upstream PFL3)" : " neurons") + "   Head: " + (frame.head_stabilization ? "trained stabilization" : "passive")));
                GUI.Label(new Rect(30,75,670,24), frame == null ? "Waiting for model" : "Body: " + frame.sim_time.ToString("F3") +
                    " s   Brain: " + frame.neural_time.ToString("F3") + " s   Speed: " + frame.compute_ratio.ToString("F2") + " ×");
                GUI.Label(new Rect(30,100,670,24), "Stick: steer / resume walking   A: spur / perched launch   B: slow / hold to grip   P: pause");
                GUI.Label(new Rect(30,125,670,24), frame == null ? "Waiting for controller" :
                    (frame.neurons > 0 ? frame.upstream_steering ? "Synthetic upstream PFL3 steering" : "Synthetic direct DNa01/DNa02 interface" : "Brain off: engineering gait control") +
                    (string.IsNullOrEmpty(frame.perch_phase) ? "" : "   " + frame.walking_surface + ": " + frame.perch_phase + "   Feet: " + frame.contacting_claws + "/6"));
                GUI.Label(new Rect(30,150,670,24), frame != null && !frame.rider_attached ?
                    "Unloaded fly: " + frame.idle_behavior + "   Remount during a safe claw hold." :
                    "Assisted launch; synthetic brain interface. Combat uses this body's simulation clock.");
                return;
            }
            if (frame != null && frame.surface_normal != null)
            {
                GUI.Label(new Rect(30,25,670,24),"SIX-CLAW CONTACT LAB — articulated MuJoCo body + miniature rider");
                GUI.Label(new Rect(30,50,670,24),"Contacting claws: " + frame.contacting_claws + " / 6   Adhesion: " + (frame.adhesion_enabled ? "on" : "off"));
                GUI.Label(new Rect(30,75,670,24),"Simulated time: " + frame.sim_time.ToString("F3") + " s   " + (Time.unscaledTime-lastFrame > 2 ? "Stream disconnected" : frame.episode_ended ? frame.episode_status + " — reset to replay" : frame.paused ? "Paused" : "Active"));
                GUI.Label(new Rect(30,100,670,24),string.IsNullOrEmpty(frame.perch_phase) ? "A / Space: release grip   B / L: grip at existing contact   Start / Backspace: reset   P: pause" : "Phase: " + frame.perch_phase + "   B / L: approach   A / Space: launch   Start: reset   P: pause");
                GUI.Label(new Rect(30,125,670,24),"Contact-only adhesion; flat surface and fixed leg targets. Biological calibration remains pending.");
                GUI.Label(new Rect(30,150,670,24),string.IsNullOrEmpty(frame.perch_phase) ? "Separate contact experiment; no flight-to-perch approach controller." : "EXPERIMENTAL averaged flight forces; no wing-policy or neural landing control.");
                return;
            }
            GUI.Label(new Rect(30, 25, 670, 24), unifiedLab ? "UNIFIED FLOOR LAB — automated wing landing + walking check" : frame != null && frame.fast_averaged ? "FAST TRANSLATION LAB — empirical loaded-flight response; no brain" : trainedFlight ? "FLIGHT RESEARCH — published trained controller + MuJoCo body" :
                "EMBODIED FLY RESEARCH — full connectome GPU + articulated MuJoCo body");
            GUI.Label(new Rect(30, 50, 670, 24), error ?? (frame == null ? "Waiting for research backend…" :
                (Time.unscaledTime-lastFrame > 2 ? "Stream disconnected" : "Stream active") +
                (trainedFlight ? (frame.neurons > 0 ? "   EXPERIMENTAL brain: " + frame.neurons : "   Neural control: not connected") + "   Action clips: " + frame.clipped_actions : "   Brain: " + frame.neurons + " neurons   Active: " + frame.active + "   Spikes: " + frame.spikes)));
            GUI.Label(new Rect(30, 75, 670, 24), frame == null ? (unifiedLab ? "Starting qualified floor sequence automatically; initial model loading takes a moment." : trainedFlight ?
                "Enter Play, then Fruit Fly → Research → Start Trained Flight Backend" : "Start with Fruit Fly → Research → Start GPU Backend") :
                "Simulated time: " + frame.sim_time.ToString("F3") + " s   " + (frame.episode_ended ? frame.episode_status + " — Backspace to replay" :
                frame.paused ? "Paused" : unifiedLab ? frame.episode_status + "   Claws: " + frame.contacting_claws + "   Body contacts: " + frame.body_contacts : "Speed: " + frame.compute_ratio.ToString("F2") + " × real time"));
            GUI.Label(new Rect(30, 100, 670, 24), trainedFlight ? "P: pause / resume   Backspace: replay   Right stick / mouse: look   R: recenter" :
                "T: toggle sugar sensory stimulation (" + (stimulate ? "on" : "off") + ")   P: pause   Right stick / mouse: look   R: recenter");
            GUI.Label(new Rect(30, 125, 670, 24), unifiedLab ? "Automated trained body policies; live rein control and connectome motor mapping are not connected." : frame != null && frame.fast_averaged ? "Rider cues control averaged motion; yaw is uncalibrated. Fixed wing pose; no collisions or landing." : trainedFlight ? (frame != null && frame.neurons > 0 ?
                "EXPERIMENTAL direct descending excitation + wing residuals; biologically unvalidated." :
                "Measured wing pattern + learned actions; rider flight cues are not connected in this mode.") :
                "Body advances with passive physics; neural-to-flight control has not been connected.");
            GUI.Label(new Rect(30, 150, 670, 24), "Research scene is separate from the playable ride. Rider stays attached to the thorax.");
        }
        public static Material CreateBodyMaterial(GeomData source)
        {
            var material = new Material(Shader.Find("Standard"));
            material.color = new Color(source.rgba[0], source.rgba[1], source.rgba[2], source.rgba[3]);
            material.SetFloat("_Glossiness", .08f);
            if (source.name.IndexOf("wing", StringComparison.OrdinalIgnoreCase) >= 0 && source.rgba[3] < 1)
            {
                material.SetFloat("_Mode", 2);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            return material;
        }
        void OnDestroy()
        {
            receiver?.Close(); sender?.Close();
            foreach (var mesh in meshes) Destroy(mesh);
            foreach (var material in materials) Destroy(material);
        }
    }
}
