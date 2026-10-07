using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Racing
{
    // Dev tool (-views dir): photographs the countryside from fixed vantage points (interchanges,
    // flyovers, the river viaduct, forest, meadow, a map overview), then quits.
    public class WorldViews : MonoBehaviour
    {
        IEnumerator Start()
        {
            string dir = DevFlags.Get("-views");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>();
            if (chase) chase.enabled = false;
            cam.farClipPlane = 6000f;
            // Scenery only: no race HUD or menu over the views.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            if (RaceManager.Instance && RaceManager.Instance.minimapCamera) RaceManager.Instance.minimapCamera.enabled = false;
            yield return new WaitForSeconds(2f);

            string only = DevFlags.Get("-viewonly");
            foreach (var (name, pos, look, fog) in Views())
            {
                if (!string.IsNullOrEmpty(only) && !name.Contains(only)) continue;
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                bool oldFog = RenderSettings.fog;
                RenderSettings.fog = fog;
                yield return new WaitForSeconds(name.Contains("traffic") ? 6f : 0.4f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return null;
                RenderSettings.fog = oldFog;
            }
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        static IEnumerable<(string, Vector3, Vector3, bool)> Views()
        {
            // -viewat x,y,z: close-ups of one spot from above and from two sides.
            string at = DevFlags.Get("-viewat");
            if (!string.IsNullOrEmpty(at))
            {
                var v = at.Split(',');
                var p = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                yield return ("spot_top", p + new Vector3(0.1f, 30f, -6f), p, true);
                yield return ("spot_a", p + new Vector3(-14f, 4f, -10f), p, true);
                yield return ("spot_b", p + new Vector3(12f, 4f, 10f), p, true);
                yield break;
            }

            var ring = WorldLayout.Highway;
            Vector3 P(int i) => ring.pts[ring.Wrap(i)];
            Vector3 Rt(int i) => ring.right[ring.Wrap(i)];

            for (int e = 0; e < WorldLayout.Interchanges.Count; e++)
            {
                var ic = WorldLayout.Interchanges[e];
                int j = ic.j;
                Vector3 c = P(j), t = (P(j + 1) - P(j - 1)).normalized;
                yield return ($"ic{e}_aerial", c - Rt(j) * 260f - t * 260f + Vector3.up * 170f, c, true);
                if (e < 2)
                {
                    yield return ($"ic{e}_traffic", P(j - 70) + Rt(j - 70) * 19f + Vector3.up * 3.2f, P(j - 20) + Rt(j - 20) * 4f, true);
                    var offR = ic.offOuter;
                    for (int q = 10; q < offR.Count - 20; q += 12)
                        yield return ($"ic{e}_offclimb{q:00}", offR.pts[q] + Vector3.up * 1.8f, offR.pts[q + 12] + Vector3.up * 1.2f, true);
                    yield return ($"ic{e}_under", P(j - 45) + Rt(j - 45) * WorldLayout.CarriageCentre + Vector3.up * 1.6f, c + Vector3.up * 5f + Rt(j) * 6f, true);
                    var nat = ic.national;
                    int k = Mathf.Max(ic.natInner - 30, 0);
                    yield return ($"ic{e}_approach", nat.pts[k] + Vector3.up * 1.6f, nat.pts[ic.natInner + 6] + Vector3.up * 2f, true);
                    var ramp = ic.onOuter;
                    yield return ($"ic{e}_onramp", ramp.pts[4] + Vector3.up * 1.6f, ramp.pts[30] + Vector3.up * 1f, true);
                    Vector3 pad = (ic.padOuter.pts[0] + ic.padOuter.pts[1]) * 0.5f;
                    yield return ($"ic{e}_padTop", pad + Vector3.up * 38f - (P(j + 1) - P(j - 1)).normalized * 25f, pad, true);
                    var off = ic.offOuter;
                    yield return ($"ic{e}_padApproach", off.pts[off.Count - 12] + Vector3.up * 2f, pad + Vector3.up * 1f, true);
                    yield return ($"ic{e}_merge", ramp.pts[45] + Vector3.up * 2.2f, ramp.pts[ramp.Count - 1] + Vector3.up * 1f, true);
                    yield return ($"ic{e}_mergeTop", ramp.pts[60] + Vector3.up * 45f - ramp.right[60] * 30f, ramp.pts[70], true);
                }
            }

            // The longest highway deck (river viaduct): from the side and from the road.
            int best = 0, run = 0, bestRun = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                run = ring.elevated[i] ? run + 1 : 0;
                if (run > bestRun) { bestRun = run; best = i - run / 2; }
            }
            Vector3 m = P(best);
            Vector3 side = m + Rt(best) * 230f + (P(best + 40) - P(best)) * 1.5f;
            side.y = Mathf.Max(WorldLayout.Height(side.x, side.z) + 3f, m.y - 6f);
            yield return ("viaduct_side", side, m + Vector3.down * 6f, true);
            yield return ("viaduct_road", P(best - 90) + Rt(best - 90) * WorldLayout.CarriageCentre + Vector3.up * 1.6f, m + Rt(best) * WorldLayout.CarriageCentre, true);
            Vector3 lake = new Vector3(WorldLayout.LakeCentre.x, WorldLayout.LakeLevel, WorldLayout.LakeCentre.y);
            yield return ("lake", lake + new Vector3(-260f, 70f, -200f), lake + new Vector3(80f, 0f, 80f), true);

            // Forest edge and meadow at eye height beside the east national road.
            var east = WorldLayout.National[0];
            for (int i = 20; i < east.Count; i += 6)
            {
                Vector3 p = east.pts[i] + east.right[i] * 22f;
                if (WorldLayout.RoadClearance(p.x, p.z) < 10f) continue;
                p.y = WorldLayout.Height(p.x, p.z) + 1.7f;
                yield return ("meadow", p, p + east.right[i] * 40f + Vector3.down * 3f, true);
                break;
            }
            Vector3 f = FindForest();
            yield return ("forest", f + Vector3.up * 1.7f, f + new Vector3(30f, 2f, 18f), true);
            yield return ("forest_aerial", f + new Vector3(-120f, 60f, -90f), f, true);
            yield return ("overview", new Vector3(384f, 2600f, -1700f), new Vector3(384f, 0f, 500f), false);
        }

        // A spot inside a dense patch of forest, away from roads.
        static Vector3 FindForest()
        {
            for (float z = 1400f; z > -1400f; z -= 37f)
            for (float x = -1300f; x < 2100f; x += 37f)
            {
                if (Mathf.PerlinNoise(x / 260f + 11.3f, z / 260f + 4.7f) < 0.78f) continue;
                if (x > -100f && x < 870f && z > -100f && z < 870f) continue;
                if (WorldLayout.RoadClearance(x, z) < 60f) continue;
                return new Vector3(x, WorldLayout.Height(x, z), z);
            }
            return Vector3.zero;
        }
    }
}
