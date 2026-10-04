using System.Collections;
using System.Text;
using UnityEngine;

namespace Racing
{
    // Dev-only (-handlingtest): drives the player car through scripted manoeuvres on a flat pad far
    // from the track and logs lateral g, yaw rate and drift angle, then quits.
    public class HandlingProbe : MonoBehaviour
    {
        public RaceManager race;

        struct Step { public float time, throttle, steer; public bool handbrake; }

        IEnumerator Start()
        {
            var pad = new GameObject("ProbePad").AddComponent<BoxCollider>();
            Vector3 origin = new Vector3(0f, 0f, -1200f);
            pad.transform.position = origin + Vector3.down * 0.05f;
            pad.size = new Vector3(800f, 0.1f, 800f);

            var r = race.Player;
            var car = r.car;
            if (r.ai) r.ai.enabled = false;
            if (r.driver) r.driver.enabled = false;
            yield return new WaitForSeconds(0.5f);

            yield return Run(car, origin, "A full-throttle corner @100", 28f, new[] { new Step { time = 4f, throttle = 1f, steer = 1f } });
            yield return Run(car, origin, "B coast full-lock @140", 39f, new[] { new Step { time = 3f, throttle = 0f, steer = 1f } });
            yield return Run(car, origin, "C half-steer @140", 39f, new[] { new Step { time = 3f, throttle = 0.6f, steer = 0.5f } });
            yield return Run(car, origin, "D handbrake drift @80 then recover", 22f, new[]
            {
                new Step { time = 0.8f, throttle = 0.5f, steer = 1f, handbrake = true },
                new Step { time = 2.5f, throttle = 0.7f, steer = 0.3f },
            });
            yield return Run(car, origin, "E trail-brake @120", 33f, new[] { new Step { time = 2.5f, throttle = -1f, steer = 0.7f } });
            yield return Run(car, origin, "F slalom @110", 30f, new[]
            {
                new Step { time = 0.8f, throttle = 0.8f, steer = 1f },
                new Step { time = 0.8f, throttle = 0.8f, steer = -1f },
                new Step { time = 0.8f, throttle = 0.8f, steer = 1f },
                new Step { time = 1.5f, throttle = 0.8f, steer = 0f },
            });

            yield return Run(car, origin, "G turn-in @100 (0.4s full steer)", 28f, new[] { new Step { time = 0.4f, throttle = 0.5f, steer = 1f } });
            yield return Run(car, origin, "H city corner @60 full lock", 17f, new[] { new Step { time = 2f, throttle = 0.4f, steer = 1f } });

            race.Quit();
        }

        IEnumerator Run(CarController car, Vector3 origin, string name, float speed, Step[] steps)
        {
            car.Teleport(origin + Vector3.up * 0.6f, Quaternion.identity);
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.3f);
            car.Body.linearVelocity = car.transform.forward * speed;

            var sb = new StringBuilder();
            sb.Append($"[Probe] {name}\n");
            float maxLatG = 0f, maxDrift = 0f, minUp = 1f, sumLatG = 0f;
            int samples = 0;
            Vector3 lastV = car.Body.linearVelocity;
            foreach (var s in steps)
            {
                for (float t = 0f; t < s.time; t += Time.fixedDeltaTime)
                {
                    car.Throttle = s.throttle;
                    car.Steer = s.steer;
                    car.Handbrake = s.handbrake;
                    yield return new WaitForFixedUpdate();
                    Vector3 v = car.Body.linearVelocity;
                    Vector3 a = (v - lastV) / Time.fixedDeltaTime;
                    lastV = v;
                    float latG = Mathf.Abs(Vector3.Dot(a, car.transform.right)) / 9.81f;
                    maxLatG = Mathf.Max(maxLatG, latG);
                    sumLatG += latG;
                    samples++;
                    maxDrift = Mathf.Max(maxDrift, Mathf.Abs(car.DriftAngle));
                    minUp = Mathf.Min(minUp, car.transform.up.y);
                }
                float yaw = Vector3.Dot(car.Body.angularVelocity, car.transform.up) * Mathf.Rad2Deg;
                float radius = Mathf.Abs(yaw) > 1f ? car.Body.linearVelocity.magnitude / (Mathf.Abs(yaw) * Mathf.Deg2Rad) : 0f;
                sb.Append($"   after {s.time:F1}s thr={s.throttle} steer={s.steer} hb={s.handbrake}: speed={car.SpeedKmh:F0}km/h yaw={yaw:F0}deg/s radius={radius:F0}m drift={car.DriftAngle:F1} steerAngle={car.SteerAngle:F1}\n");
            }
            sb.Append($"   maxLatG={maxLatG:F2} avgLatG={sumLatG / Mathf.Max(1, samples):F2} maxDrift={maxDrift:F1} minUp={minUp:F2} spun={(maxDrift > 90f)} flipped={(minUp < 0.5f)}");
            Debug.Log(sb.ToString());
            car.Throttle = car.Steer = 0f;
            car.Handbrake = false;
        }
    }
}
