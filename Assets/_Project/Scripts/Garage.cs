using UnityEngine;

namespace Racing
{
    // Performance figures of one car model. Speeds in m/s, acceleration in m/s².
    [System.Serializable]
    public class CarSpec
    {
        public string id, displayName, tagline;
        public int price;
        public float maxSpeed, acceleration, tireGrip, rearDriveShare, mass;
        [Tooltip("Police car: used for the cops in Escape. lightBar = the model has its own roof lights.")]
        public bool police, lightBar;

        // 0..1 ratings for the garage bars.
        public float SpeedRating => Mathf.InverseLerp(48f, 66f, maxSpeed);
        public float AccelRating => Mathf.InverseLerp(9f, 15f, acceleration);
        public float GripRating => Mathf.InverseLerp(1.3f, 1.75f, tireGrip);
    }

    // Car line-up, credits and unlocks (saved in PlayerPrefs), and swapping a car body between racers.
    public static class Garage
    {
        public static readonly CarSpec[] Cars =
        {
            Spec("BMW_M3_E30", "BMW M3 E30", "Balanced rear-drive classic", 0, 56f, 11.5f, 1.55f, 0.65f, 1200f),
            Spec("CrownVic_Taxi", "Crown Victoria Taxi", "Heavy, soft, unstoppable", 1500, 54f, 10.5f, 1.45f, 0.7f, 1450f),
            Spec("Mazda_RX7_FC", "Mazda RX-7 FC", "Light and cheap - a drifter's first car", 2000, 55f, 11.8f, 1.52f, 0.75f, 1150f),
            Spec("Pack_SUV", "Street SUV", "Punchy off the line, leans in corners", 3000, 53f, 12.8f, 1.4f, 0.5f, 1500f),
            Spec("Ford_Mustang", "Ford Mustang '65", "V8 muscle - loves to slide", 4000, 58f, 12.6f, 1.42f, 0.8f, 1350f),
            Spec("Mercedes_G", "Mercedes G-Class", "A brick that shoves everything aside", 4500, 52f, 12f, 1.42f, 0.5f, 1700f),
            Spec("CrownVic_Police", "Police Interceptor", "Built for the long straights", 5000, 61f, 12f, 1.5f, 0.7f, 1450f, police: true, lightBar: true),
            Spec("Dodge_Charger_Police", "Charger Pursuit", "Modern cop muscle", 6500, 62f, 12.8f, 1.5f, 0.7f, 1500f, police: true, lightBar: true),
            Spec("Toyota_Supra_A70", "Toyota Supra A70", "Turbo GT with long legs", 7000, 60f, 13f, 1.55f, 0.7f, 1300f),
            Spec("Pack_Sport", "Sport Coupe", "Light and sharp on turn-in", 8000, 62f, 13.5f, 1.6f, 0.6f, 1150f),
            Spec("Mercedes_300SL", "Mercedes 300 SL", "Gullwing grand tourer", 9000, 61f, 12.5f, 1.5f, 0.7f, 1250f),
            Spec("Mazda_RX7_FD", "Mazda RX-7 FD", "Rotary screamer, nimble and quick", 10000, 62f, 13.8f, 1.62f, 0.75f, 1200f),
            Spec("Honda_NSX", "Honda NSX", "Mid-engine precision", 11000, 63f, 13.6f, 1.66f, 0.7f, 1250f),
            Spec("Porsche_930", "Porsche 911 Turbo", "Brutal top speed - tail-happy", 12000, 66f, 14.5f, 1.68f, 0.8f, 1150f),
            Spec("Nissan_R34", "Nissan Skyline R34", "All-wheel-drive grip monster", 14000, 64f, 14.6f, 1.72f, 0.45f, 1400f),
        };

        static CarSpec Spec(string id, string name, string tagline, int price, float speed, float accel, float grip,
            float rear, float mass, bool police = false, bool lightBar = false) => new CarSpec
        {
            id = id, displayName = name, tagline = tagline, price = price, maxSpeed = speed, acceleration = accel,
            tireGrip = grip, rearDriveShare = rear, mass = mass, police = police, lightBar = lightBar,
        };

        // Credits for a finish position (1-based), scaled by race length.
        static readonly int[] PrizeByPosition = { 2000, 1400, 1000, 700, 500, 300 };

        public static CarSpec Find(string id)
        {
            foreach (var c in Cars) if (c.id == id) return c;
            return Cars[0];
        }

        public static int Credits
        {
            get => PlayerPrefs.GetInt("credits", 0);
            set => PlayerPrefs.SetInt("credits", Mathf.Max(0, value));
        }

        public static string Selected
        {
            get
            {
                string id = PlayerPrefs.GetString("car", Cars[0].id);
                return IsUnlocked(id) ? id : Cars[0].id;
            }
            set => PlayerPrefs.SetString("car", value);
        }

        public static bool IsUnlocked(string id) => Find(id).price == 0 || PlayerPrefs.GetInt("unlocked_" + id, 0) == 1;

        public static bool TryBuy(string id)
        {
            var spec = Find(id);
            if (IsUnlocked(id)) return true;
            if (Credits < spec.price) return false;
            Credits -= spec.price;
            PlayerPrefs.SetInt("unlocked_" + id, 1);
            PlayerPrefs.Save();
            return true;
        }

        public static int Prize(int position, int laps)
        {
            int basePrize = PrizeByPosition[Mathf.Clamp(position - 1, 0, PrizeByPosition.Length - 1)];
            return Mathf.RoundToInt(basePrize * Mathf.Max(1, laps) / 2f / 10f) * 10;
        }

        // Exchanges the car models (body, wheels, collider, specs) of two racers; drivers stay put.
        public static void Swap(Racer a, Racer b)
        {
            if (a == b) return;
            // GetComponent rather than Racer.car: this can run before the racers' Awake.
            var ca = a.GetComponent<CarController>();
            var cb = b.GetComponent<CarController>();
            (ca.bodyVisual, cb.bodyVisual) = (cb.bodyVisual, ca.bodyVisual);
            Reparent(ca.bodyVisual, ca.transform);
            Reparent(cb.bodyVisual, cb.transform);
            for (int i = 0; i < 4; i++)
            {
                (ca.wheelVisuals[i], cb.wheelVisuals[i]) = (cb.wheelVisuals[i], ca.wheelVisuals[i]);
                Reparent(ca.wheelVisuals[i], ca.transform);
                Reparent(cb.wheelVisuals[i], cb.transform);
            }
            (ca.wheelAnchors, cb.wheelAnchors) = (cb.wheelAnchors, ca.wheelAnchors);
            (ca.wheelRadius, cb.wheelRadius) = (cb.wheelRadius, ca.wheelRadius);
            var ba = a.GetComponent<BoxCollider>();
            var bb = b.GetComponent<BoxCollider>();
            (ba.center, bb.center) = (bb.center, ba.center);
            (ba.size, bb.size) = (bb.size, ba.size);
            (ca.carId, cb.carId) = (cb.carId, ca.carId);
            ca.ApplySpec(Find(ca.carId));
            cb.ApplySpec(Find(cb.carId));
        }

        static void Reparent(Transform t, Transform parent)
        {
            if (!t) return;
            Vector3 lp = t.localPosition;
            Quaternion lr = t.localRotation;
            t.SetParent(parent, false);
            t.localPosition = lp;
            t.localRotation = lr;
        }

        // Gives the player the car with this id, handing the player's current car to whoever drove it.
        public static void Equip(Racer player, System.Collections.Generic.IEnumerable<Racer> racers, string id)
        {
            if (player.GetComponent<CarController>().carId == id) return;
            foreach (var r in racers)
                if (r != player && r.GetComponent<CarController>().carId == id) { Swap(player, r); return; }
        }
    }
}
