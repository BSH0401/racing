using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Free roam with a GTA-style wanted level. Crimes (wrecking traffic, ramming police, smashing
    // street furniture, speeding through the city) build heat; every full point is a star, and each
    // star brings one more police car (the AI racers in police bodies), faster and - from three
    // stars - on nitro. Out of every cop's sight long enough, the heat is off and the stars pay out;
    // boxed in at a standstill, you're busted and fined.
    public partial class ChaseMode
    {
        float heat, evadeTimer, spawnTimer;
        // Where the police last had eyes on the player: they search around it.
        Vector3 lastSeen;
        readonly Dictionary<string, float> lastCrime = new Dictionary<string, float>();

        public int Stars => Active && Mode == GameMode.FreeRoam ? Mathf.Clamp(Mathf.FloorToInt(heat), 0, 5) : 0;
        public float EvadeTime => 6f + 2.5f * Stars;
        public float SearchRadius => 120f + 50f * Stars;
        // Outside the search area and out of sight, but the search hasn't been called off yet.
        public bool Hiding { get; private set; }
        public float EvadeProgress => Stars > 0 ? Mathf.Clamp01(evadeTimer / EvadeTime) : 0f;
        public int SessionCredits { get; private set; }

        void PrepareFreeRoam(List<Racer> others)
        {
            TimeLeft = 0f;
            heat = evadeTimer = spawnTimer = 0f;
            SessionCredits = 0;
            lastCrime.Clear();
            Place(Player, 0f, 0f);
            foreach (var r in others)
            {
                r.gameObject.SetActive(false);
                reserve.Add(r);
            }
            DressPolice(others);
        }

        void OnFreeRoamGo()
        {
            // Dev: -wanted N starts with N stars.
            float w = DevFlags.GetFloat("-wanted", 0f);
            if (w > 0f) Crime(w, "WANTED");
        }

        // Adds heat for a crime by the player. Repeats of the same crime within a second don't stack
        // (one crash fires several collision events); continuous ones (speeding) pass amount * dt.
        public void Crime(float amount, string label, bool continuous = false)
        {
            if (!Active || Mode != GameMode.FreeRoam || race.State != RaceState.Racing) return;
            if (!continuous)
            {
                if (lastCrime.TryGetValue(label, out float t) && Time.time - t < 1f) return;
                lastCrime[label] = Time.time;
            }
            int before = Stars;
            heat = Mathf.Min(5.99f, heat + amount);
            evadeTimer = 0f;
            lastSeen = Player.transform.position;
            int now = Stars;
            if (now > before)
            {
                if (before == 0) race.hud.Flash("WANTED", 1.4f);
                race.hud.Toast(label);
                if (DevFlags.Has("-logchase")) Debug.Log($"[Wanted] {label} stars={now} heat={heat:F2} t={elapsed:F1}");
            }
        }

        public void OnNearMiss(int combo)
        {
            if (!Active || Mode != GameMode.FreeRoam) return;
            int cr = 10 * Mathf.Min(combo, 10);
            Garage.Credits += cr;
            SessionCredits += cr;
        }

        void FreeRoamImpact(Racer self, Collision c)
        {
            if (!police.Contains(self) || c.rigidbody != Player.car.Body) return;
            if (c.impulse.magnitude / self.car.Body.mass > 2.5f) Crime(0.6f, "ASSAULTING POLICE");
        }

        void TickFreeRoam(float dt)
        {
            Vector3 pp = Player.transform.position;
            int stars = Stars;

            // Speeding through the city draws a first star; heat below one star cools off.
            bool inCity = pp.x > 0f && pp.x < CityLayout.Size && pp.z > 0f && pp.z < CityLayout.Size;
            if (stars == 0)
            {
                if (inCity && Player.car.SpeedKmh > 130f) Crime(0.22f * dt, "SPEEDING", true);
                else heat = Mathf.Max(0f, heat - 0.05f * dt);
                stars = Stars;
            }

            // One police car per star, the first straight away, then one every five seconds.
            int want = Mathf.Min(stars, police.Count + reserve.Count);
            spawnTimer -= dt;
            if (police.Count < want && spawnTimer <= 0f)
            {
                var r = reserve[0];
                reserve.RemoveAt(0);
                r.gameObject.SetActive(true);
                SpawnBehindPlayer(r);
                Hunt(r, Player);
                Siren(r, true);
                if (r.TryGetComponent(out Nitro n)) n.Refill(1f);
                if (r.TryGetComponent(out CarDamage cd)) cd.Repair();
                police.Add(r);
                spawnTimer = 5f;
            }

            float nearest = float.MaxValue;
            bool seen = false;
            foreach (var r in police)
            {
                Vector3 cp = r.transform.position;
                float d = Flat(cp - pp).magnitude;
                nearest = Mathf.Min(nearest, d);
                float match = Player.car.maxSpeed / Mathf.Max(1f, r.car.maxSpeed);
                r.chaser.speedScale = match * (d > 160f ? 1.12f : 0.98f) * (0.94f + 0.03f * stars) * race.DifficultyScale;
                r.chaser.useNitro = stars >= 3;
                if (d < 60f || (d < 240f && ChaseDriver.Visible(cp, pp))) seen = true;
            }
            Distance = nearest;

            if (stars == 0) { Bust = 0f; Hiding = false; return; }

            // Seen: the search centres on the player again. Out of sight and clear of the search
            // area, the search winds down; seen again, it starts over.
            if (seen || police.Count == 0) lastSeen = pp;
            Hiding = !seen && police.Count > 0 && Flat(pp - lastSeen).magnitude > SearchRadius;
            evadeTimer = Hiding ? evadeTimer + dt : Mathf.Max(0f, evadeTimer - dt * 2f);
            if (evadeTimer >= EvadeTime) { Evaded(); return; }

            bool pinned = Player.car.SpeedKmh < 12f && nearest < 11f;
            Bust = Mathf.Clamp01(Bust + (pinned ? dt / 2.5f : -dt / 1.5f));
            if (Bust >= 1f) Busted();
        }

        void Evaded()
        {
            int stars = Stars;
            int reward = 250 * stars;
            Garage.Credits += reward;
            SessionCredits += reward;
            PlayerPrefs.Save();
            race.hud.Flash("EVADED", 1.8f);
            race.hud.Toast($"+{reward:N0} CR");
            if (DevFlags.Has("-logchase")) Debug.Log($"[Wanted] evaded stars={stars} t={elapsed:F1}");
            ClearWanted();
        }

        void Busted()
        {
            int stars = Stars;
            int fine = Mathf.Min(Garage.Credits, 300 * stars);
            Garage.Credits -= fine;
            SessionCredits -= fine;
            PlayerPrefs.Save();
            race.hud.Flash("BUSTED", 2f);
            if (fine > 0) race.hud.Toast($"FINE  -{fine:N0} CR");
            if (DevFlags.Has("-logchase")) Debug.Log($"[Wanted] busted stars={stars} fine={fine} t={elapsed:F1}");
            ClearWanted();
            // Released back in the city with a repaired car.
            race.Transition(() =>
            {
                Place(Player, 0f, 0f);
                Player.car.InputLocked = false;
                                CarDamage.RepairAll();
                if (race.chaseCamera) race.chaseCamera.Snap();
            });
        }

        // Every cop goes home.
        void ClearWanted()
        {
            heat = evadeTimer = 0f;
            Bust = 0f;
            foreach (var r in police)
            {
                Siren(r, false);
                r.chaser.enabled = false;
                r.gameObject.SetActive(false);
                reserve.Add(r);
            }
            police.Clear();
        }

        // Free roam respawn: cops come back behind the player, the player on the nearest road node.
        public void RespawnFree(Racer r)
        {
            if (r != Player) { SpawnBehindPlayer(r); return; }
            RespawnOnRoad(r);
        }

        // Back onto the nearest road graph node, facing along the road closest to the old heading.
        public void RespawnOnRoad(Racer r)
        {
            Vector3 p = r.transform.position;
            var graph = WorldLayout.Graph;
            int n = WorldLayout.NearestNode(p, Vector3.zero);
            Vector3 pos = n >= 0 ? graph[n].pos : p;
            Vector3 dir = Flat(r.transform.forward).normalized;
            if (n >= 0)
            {
                float best = -2f;
                foreach (int nb in graph[n].next)
                {
                    Vector3 e = Flat(graph[nb].pos - pos).normalized;
                    float dot = Vector3.Dot(e, dir);
                    if (dot > best) { best = dot; dir = e; }
                }
            }
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            pos.y = WorldLayout.SurfaceHeight(pos.x, pos.z, pos.y);
            r.car.Teleport(pos + Vector3.up * 0.8f, Quaternion.LookRotation(dir));
        }
    }
}
