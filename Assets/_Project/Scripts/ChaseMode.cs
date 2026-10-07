using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    public enum GameMode { Race, Pursuit, Escape, FreeRoam }

    // The two chase modes, layered on top of RaceManager's countdown / pause / results flow.
    //  Pursuit: a suspect flees round the city; ram it until its damage bar runs out before time is up
    //           or it gets too far away.
    //  Escape:  police cars hunt the player through the street grid; survive the timer or get far
    //           enough away to lose them. Stopping with a police car alongside fills the BUSTED meter.
    //  Free roam: drive anywhere; crimes raise a wanted level (see ChaseMode.FreeRoam.cs).
    public partial class ChaseMode : MonoBehaviour
    {
        public RaceManager race;
        public Material sirenRed, sirenBlue;
        public float pursuitTime = 150f;
        public float escapeTime = 90f;

        public static ChaseMode Instance { get; private set; }
        public GameMode Mode { get; private set; }
        public bool Active { get; private set; }
        public Racer Target { get; private set; }
        public float TargetHealth { get; private set; }
        public float Bust { get; private set; }
        public float TimeLeft { get; private set; }
        public float Distance { get; private set; }
        public int PoliceCount => police.Count;
        public float LostTimer => lostTimer;

        readonly List<Racer> police = new List<Racer>();
        readonly List<Racer> reserve = new List<Racer>();
        // Body swaps made to put the cops in police cars, undone by Restore().
        readonly List<(Racer a, Racer b)> swaps = new List<(Racer, Racer)>();
        float elapsed, lostTimer, hitCooldown, release, breakaway;

        const float LoseDistance = 300f;
        const float EscapeDistance = 350f;

        void Awake() => Instance = this;

        Racer Player => race.Player;

        // Lays the chase out after RaceManager has reset everyone for a race start.
        public void Prepare(GameMode mode, bool autopilot)
        {
            Restore();
            Mode = mode;
            Active = true;
            elapsed = lostTimer = hitCooldown = breakaway = 0f;
            Bust = 0f;
            TargetHealth = 1f;
            var others = new List<Racer>();
            foreach (var r in race.racers) if (r != Player) others.Add(r);

            if (mode == GameMode.FreeRoam)
            {
                PrepareFreeRoam(others);
            }
            else if (mode == GameMode.Pursuit)
            {
                TimeLeft = pursuitTime;
                Target = others[Random.Range(0, others.Count)];
                foreach (var r in others) if (r != Target) r.gameObject.SetActive(false);
                Place(Player, 0f, 0f);
                Place(Target, 75f, 0f);
                Target.ai.skill = 0.97f;
                Siren(Player, true);
                if (autopilot) { Hunt(Player, Target); Player.chaser.useNitro = true; }
            }
            else
            {
                TimeLeft = escapeTime;
                Place(Player, 0f, 0f);
                for (int i = 0; i < others.Count; i++)
                {
                    var r = others[i];
                    if (i < 2)
                    {
                        Place(r, -40f - i * 18f, i == 0 ? -3.2f : 3.2f);
                        Hunt(r, Player);
                        Siren(r, true);
                        police.Add(r);
                    }
                    else
                    {
                        r.gameObject.SetActive(false);
                        reserve.Add(r);
                    }
                }
                DressPolice(others);
                foreach (var r in police) Siren(r, true);
            }
        }

        // The first cops (and the first reinforcements) drive the real police cars when the player isn't in one.
        void DressPolice(List<Racer> units)
        {
            var policeIds = new List<string>();
            foreach (var spec in Garage.Cars) if (spec.police && spec.id != Player.car.carId) policeIds.Add(spec.id);
            int next = 0;
            foreach (var id in policeIds)
            {
                Racer holder = null;
                foreach (var r in race.CarPool) if (r.GetComponent<CarController>().carId == id) holder = r; // spares never ran Awake
                if (!holder || units.Contains(holder)) continue; // missing, or already a cop
                while (next < units.Count && Garage.Find(units[next].car.carId).police) next++;
                if (next >= units.Count) break;
                Garage.Swap(units[next], holder);
                swaps.Add((units[next], holder));
                next++;
            }
        }

        void Place(Racer r, float metersFromStart, float lateral)
        {
            var track = race.track;
            int idx = track.Wrap(track.StartIndex + Mathf.RoundToInt(metersFromStart / track.Spacing));
            Vector3 pos = track.Point(idx) + track.Right(idx) * lateral + Vector3.up * 0.7f;
            r.car.Teleport(pos, Quaternion.LookRotation(track.FlatTangent(idx)));
            r.ResetProgress(idx);
            r.car.InputLocked = true;
        }

        void Hunt(Racer r, Racer prey)
        {
            if (r.ai) r.ai.enabled = false;
            if (r.driver) r.driver.enabled = false;
            r.chaser.target = prey;
            r.chaser.speedScale = 1f;
            r.chaser.useNitro = false;
            r.chaser.enabled = true;
            if (r.TryGetComponent(out CarDamage cd)) cd.toughness = 0.4f;
        }

        void Siren(Racer r, bool on)
        {
            var s = r.GetComponent<SirenLights>();
            if (!s) return;
            s.red = sirenRed;
            s.blue = sirenBlue;
            s.Set(on, !Garage.Find(r.car.carId).lightBar);
        }

        // Back to normal: everyone active, no sirens, no chase drivers.
        public void Restore()
        {
            Active = false;
            Target = null;
            police.Clear();
            reserve.Clear();
            for (int i = swaps.Count - 1; i >= 0; i--) Garage.Swap(swaps[i].a, swaps[i].b);
            swaps.Clear();
            foreach (var r in race.racers)
            {
                r.gameObject.SetActive(true);
                if (r.chaser) r.chaser.enabled = false;
                if (r.ai) r.ai.nitroAllowed = true;
                if (r.TryGetComponent(out CarDamage cd)) cd.toughness = 1f;
                Siren(r, false);
            }
        }

        public void OnGo()
        {
            release = Mode == GameMode.Escape ? 2f : 0f;
            race.hud.Flash(Mode switch { GameMode.Pursuit => "TAKE THEM DOWN!", GameMode.FreeRoam => "FREE ROAM", _ => "LOSE THE COPS!" }, 1.6f);
            if (Mode == GameMode.FreeRoam) OnFreeRoamGo();
        }

        // True while a police car is pinning the player: don't treat it as stuck.
        public bool SuppressStuck(Racer r) =>
            Active && police.Contains(r) && (r.transform.position - Player.transform.position).sqrMagnitude < 25f * 25f;

        public void Tick(float dt)
        {
            if (!Active) return;
            elapsed += dt;
            TimeLeft -= dt;
            if (DevFlags.Has("-logchase") && Mathf.FloorToInt(elapsed / 5f) != Mathf.FloorToInt((elapsed - dt) / 5f))
                Debug.Log($"[Chase] t={elapsed:F0} dist={Distance:F0} health={TargetHealth:F2} bust={Bust:F2} police={police.Count} playerKmh={Player.car.SpeedKmh:F0}");
            hitCooldown -= dt;
            if (Mode == GameMode.Pursuit) TickPursuit(dt);
            else if (Mode == GameMode.FreeRoam) TickFreeRoam(dt);
            else TickEscape(dt);
        }

        void TickPursuit(float dt)
        {
            Distance = Flat(Target.transform.position - Player.transform.position).magnitude;
            // The suspect eases off when far ahead and floors it when the player is close.
            // A burst of nitro only to break away when the player closes in: one short window per
            // approach, then none for a while, or every ramming run ends with the suspect boosting off.
            breakaway -= dt;
            bool close = Distance < 45f;
            if (close && breakaway <= 0f) breakaway = 15f;
            Target.ai.nitroAllowed = close && breakaway > 13f;
            Target.ai.speedScale = Player.car.maxSpeed / Mathf.Max(1f, Target.car.maxSpeed) * Mathf.Lerp(1.02f, 0.84f, Mathf.InverseLerp(40f, 260f, Distance));
            lostTimer = Distance > LoseDistance ? lostTimer + dt : 0f;
            if (lostTimer > 6f) End(false, "SUSPECT ESCAPED", "The suspect got away.");
            else if (TimeLeft <= 0f) End(false, "OUT OF TIME", "The suspect is still on the loose.");
        }

        void TickEscape(float dt)
        {
            if (release > 0f)
            {
                release -= dt;
                foreach (var r in police) r.car.InputLocked = release > 0f;
            }

            // Reinforcements every 20 s, out of sight behind the player.
            if (reserve.Count > 0 && elapsed > 20f * (police.Count - 1))
            {
                var r = reserve[0];
                reserve.RemoveAt(0);
                r.gameObject.SetActive(true);
                SpawnBehindPlayer(r);
                Hunt(r, Player);
                Siren(r, true);
                police.Add(r);
                race.hud.Flash("MORE POLICE!", 1.2f);
            }

            float nearest = float.MaxValue;
            foreach (var r in police)
            {
                float d = Flat(r.transform.position - Player.transform.position).magnitude;
                nearest = Mathf.Min(nearest, d);
                // Cop top speed follows the player's car (not their own body), so every car is fair game.
                float match = Player.car.maxSpeed / Mathf.Max(1f, r.car.maxSpeed);
                r.chaser.speedScale = match * (d > 160f ? 1.1f : 0.97f) * race.DifficultyScale;
            }
            Distance = nearest;

            bool pinned = Player.car.SpeedKmh < 15f && nearest < 11f;
            Bust = Mathf.Clamp01(Bust + (pinned ? dt / 2.5f : -dt / 1.5f));
            lostTimer = nearest > EscapeDistance ? lostTimer + dt : 0f;

            if (Bust >= 1f) End(false, "BUSTED", "The police boxed you in.");
            else if (lostTimer > 5f) End(true, "LOST THEM!", $"You shook off the police with {TimeLeft:F0} s to spare.");
            else if (TimeLeft <= 0f) End(true, "ESCAPED", "You outlasted the police.");
        }

        // Reinforcements appear on a road node 170-320 m away, preferably behind the player,
        // facing along the road towards them.
        void SpawnBehindPlayer(Racer r)
        {
            Vector3 pp = Player.transform.position;
            Vector3 fwd = Flat(Player.transform.forward).normalized;
            int best = -1;
            float bestScore = float.MinValue;
            var graph = WorldLayout.Graph;
            for (int i = 0; i < graph.Count; i++)
            {
                Vector3 c = graph[i].pos;
                float d = Flat(c - pp).magnitude;
                if (d < 170f || d > 320f) continue;
                float score = -Vector3.Dot(Flat(c - pp).normalized, fwd) - Mathf.Abs(d - 230f) / 200f;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            Vector3 pos = best >= 0 ? graph[best].pos : pp - fwd * 220f;
            Vector3 dir = Flat(pp - pos).normalized;
            if (best >= 0)
            {
                // Face along the road edge that heads most towards the player.
                float bestDot = -2f;
                foreach (int nb in graph[best].next)
                {
                    Vector3 e = Flat(graph[nb].pos - pos).normalized;
                    float dot = Vector3.Dot(e, Flat(pp - pos).normalized);
                    if (dot > bestDot) { bestDot = dot; dir = e; }
                }
            }
            pos.y = WorldLayout.SurfaceHeight(pos.x, pos.z, pos.y);
            r.car.Teleport(pos + Vector3.up * 0.8f, Quaternion.LookRotation(dir));
            r.ResetProgress(race.track.FindClosest(pos));
            r.car.InputLocked = false;
        }

        // Ramming the suspect: damage scales with the velocity change of the hit.
        public void OnImpact(Racer self, Collision c)
        {
            if (Active && Mode == GameMode.FreeRoam) { FreeRoamImpact(self, c); return; }
            if (!Active || Mode != GameMode.Pursuit || self != Target || race.State != RaceState.Racing) return;
            if (c.rigidbody != Player.car.Body || hitCooldown > 0f) return;
            float dv = c.impulse.magnitude / Target.car.Body.mass;
            if (dv < 1.5f) return;
            hitCooldown = 0.6f;
            TargetHealth = Mathf.Max(0f, TargetHealth - Mathf.Min(dv, 8f) * 0.03f);
            if (DevFlags.Has("-logchase")) Debug.Log($"[Chase] hit dv={dv:F1} health={TargetHealth:F2} t={elapsed:F1}");
            if (TargetHealth <= 0f) End(true, "TAKEDOWN!", $"Suspect stopped with {TimeLeft:F0} s left.");
            else race.hud.Flash("HIT!", 0.5f);
        }

        void End(bool success, string title, string body)
        {
            Active = false;
            foreach (var r in race.racers) if (r.gameObject.activeSelf) r.car.InputLocked = true;
            int prize;
            if (Mode == GameMode.Pursuit)
                prize = success ? 1500 + Mathf.RoundToInt(TimeLeft) * 10 : 200;
            else
                prize = success ? 1500 + (TimeLeft > 0f ? Mathf.RoundToInt(TimeLeft) * 15 : 0) : 200;
            prize = Mathf.RoundToInt(prize / 10f) * 10;
            race.EndChase(success, title, body, prize);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
