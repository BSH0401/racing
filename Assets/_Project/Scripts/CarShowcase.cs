using System.Collections;
using System.IO;
using UnityEngine;

namespace Racing
{
    // Dev tool (-showcase dir): parks every car on the grid and photographs each one from the side and
    // front three-quarter, then quits. Used to check imported car models, wheels and scale.
    public class CarShowcase : MonoBehaviour
    {
        public RaceManager race;

        IEnumerator Start()
        {
            string dir = DevFlags.Get("-showcase");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>();
            if (chase) chase.enabled = false;
            yield return new WaitForSeconds(2f);

            var views = new (string name, Vector3 offset)[]
            {
                ("side", new Vector3(-5.5f, 0.9f, 0.2f)),
                ("front", new Vector3(-4f, 1.6f, 5f)),
                ("rear", new Vector3(3.5f, 1.4f, -5f)),
            };
            // -showall: every garage model in turn on the player's car; otherwise each racer as it is.
            bool all = DevFlags.Has("-showall");
            int count = all ? Garage.Cars.Length : race.racers.Length;
            for (int i = 0; i < count; i++)
            {
                Racer racer = all ? race.Player : race.racers[i];
                string label = racer.racerName;
                if (all)
                {
                    racer = race.Player;
                    Garage.Equip(racer, race.CarPool, Garage.Cars[i].id);
                    label = Garage.Cars[i].id;
                    yield return new WaitForSeconds(0.3f);
                }
                var car = racer.transform;
                foreach (var v in views)
                {
                    cam.transform.position = car.TransformPoint(v.offset);
                    cam.transform.LookAt(car.position + car.up * 0.6f);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"car{i:00}_{label}_{v.name}.png"));
                    yield return null;
                }
            }
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }
}
