using UnityEngine;

namespace Racing
{
    // GTA-style race guidance for the player: a glowing beam and ring on the next checkpoint,
    // plus the route line and a checkpoint blip that only the minimap camera sees.
    public class RouteDisplay : MonoBehaviour
    {
        public RaceManager race;
        public Material beamMaterial, routeMaterial, blipMaterial;
        public int worldLayer = 30;
        public int minimapLayer = 31;

        Transform beam, ring, blip;
        LineRenderer route;

        void Start()
        {
            var track = race.track;

            var lineGo = new GameObject("RouteLine");
            lineGo.transform.SetParent(transform, false);
            lineGo.layer = minimapLayer;
            lineGo.transform.rotation = Quaternion.LookRotation(Vector3.up);
            route = lineGo.AddComponent<LineRenderer>();
            route.sharedMaterial = routeMaterial;
            route.useWorldSpace = true;
            route.loop = true;
            route.alignment = LineAlignment.TransformZ;
            route.widthMultiplier = 7f;
            route.numCornerVertices = 2;
            route.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            int step = 3, count = track.Count / step;
            route.positionCount = count;
            for (int i = 0; i < count; i++) route.SetPosition(i, track.Point(i * step) + Vector3.up * 4f);

            beam = Primitive("CheckpointBeam", PrimitiveType.Cylinder, beamMaterial, worldLayer, new Vector3(2.4f, 20f, 2.4f));
            ring = Primitive("CheckpointRing", PrimitiveType.Cylinder, beamMaterial, worldLayer, new Vector3(RaceManager.CheckpointRadius * 1.3f, 0.04f, RaceManager.CheckpointRadius * 1.3f));
            blip = Primitive("CheckpointBlip", PrimitiveType.Sphere, blipMaterial, minimapLayer, new Vector3(20f, 1f, 20f));
        }

        Transform Primitive(string name, PrimitiveType type, Material mat, int layer, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = scale;
            go.layer = layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void LateUpdate()
        {
            var p = race.Player;
            bool show = p && !p.finished && (race.State == RaceState.Racing || race.State == RaceState.Countdown);
            beam.gameObject.SetActive(show);
            ring.gameObject.SetActive(show);
            blip.gameObject.SetActive(show);
            route.enabled = race.State != RaceState.Menu;
            if (!show) return;

            Vector3 cp = race.CheckpointPosition(p.cpPassed % race.CheckpointCount);
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 5f);
            beam.position = cp + Vector3.up * 20f;
            ring.position = cp + Vector3.up * 0.12f;
            ring.localScale = new Vector3(RaceManager.CheckpointRadius * 1.3f * pulse, 0.04f, RaceManager.CheckpointRadius * 1.3f * pulse);
            blip.position = cp + Vector3.up * 45f;
        }
    }
}
