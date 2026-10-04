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

        // 0..1 ratings for the garage bars.
        public float SpeedRating => Mathf.InverseLerp(48f, 66f, maxSpeed);
        public float AccelRating => Mathf.InverseLerp(9f, 15f, acceleration);
        public float GripRating => Mathf.InverseLerp(1.3f, 1.7f, tireGrip);
    }

    // Car line-up, credits and unlocks (saved in PlayerPrefs), and swapping a car body between racers.
    public static class Garage
    {
        public static readonly CarSpec[] Cars =
        {
            new CarSpec { id = "BMW_M3_E30", displayName = "BMW M3 E30", tagline = "Balanced rear-drive classic",
                price = 0, maxSpeed = 56f, acceleration = 11.5f, tireGrip = 1.55f, rearDriveShare = 0.65f, mass = 1200f },
            new CarSpec { id = "CrownVic_Taxi", displayName = "Crown Victoria Taxi", tagline = "Heavy, soft, unstoppable",
                price = 1500, maxSpeed = 54f, acceleration = 10.5f, tireGrip = 1.45f, rearDriveShare = 0.7f, mass = 1450f },
            new CarSpec { id = "Pack_SUV", displayName = "Street SUV", tagline = "Punchy off the line, leans in corners",
                price = 3000, maxSpeed = 53f, acceleration = 12.8f, tireGrip = 1.4f, rearDriveShare = 0.5f, mass = 1500f },
            new CarSpec { id = "CrownVic_Police", displayName = "Police Interceptor", tagline = "Built for the long straights",
                price = 5000, maxSpeed = 61f, acceleration = 12f, tireGrip = 1.5f, rearDriveShare = 0.7f, mass = 1450f },
            new CarSpec { id = "Pack_Sport", displayName = "Sport Coupe", tagline = "Light and sharp on turn-in",
                price = 8000, maxSpeed = 62f, acceleration = 13.5f, tireGrip = 1.6f, rearDriveShare = 0.6f, mass = 1150f },
            new CarSpec { id = "Porsche_930", displayName = "Porsche 911 Turbo", tagline = "Fastest of all - tail-happy",
                price = 12000, maxSpeed = 66f, acceleration = 14.5f, tireGrip = 1.68f, rearDriveShare = 0.8f, mass = 1150f },
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
        public static void Equip(Racer player, Racer[] racers, string id)
        {
            if (player.GetComponent<CarController>().carId == id) return;
            foreach (var r in racers)
                if (r != player && r.GetComponent<CarController>().carId == id) { Swap(player, r); return; }
        }
    }
}
