using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Racing
{
    public enum RaceState { Menu, Countdown, Racing, Finished }
    public enum Difficulty { Easy, Normal, Hard }

    // Race flow: main menu (attract mode) -> countdown -> racing -> results.
    // Tracks laps, standings, respawns and saved records.
    public class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance { get; private set; }

        public TrackPath track;
        public Racer[] racers;
        [Tooltip("Inactive cars holding the garage models no racer drives; bodies are swapped in from here.")]
        public Racer[] spares = new Racer[0];
        public IEnumerable<Racer> CarPool { get { foreach (var r in racers) yield return r; foreach (var r in spares) yield return r; } }
        public ChaseCamera chaseCamera;
        public RaceHUD hud;
        public ChaseMode chase;
        public ThemeController theme;
        public Camera minimapCamera;
        public int laps = 3;
        public Difficulty difficulty = Difficulty.Normal;

        public float BestLapRecord => PlayerPrefs.GetFloat("bestLap", -1f);
        public int BestFinishRecord => PlayerPrefs.GetInt("bestFinish", 0);

        public RaceState State { get; private set; }
        public float RaceTime { get; private set; }
        public float Countdown { get; private set; }
        public bool Paused { get; private set; }
        public Racer Player { get; private set; }
        public int LastPrize { get; private set; }
        public GameMode Mode { get; private set; }
        public string ResultTitle { get; private set; }
        public string ResultBody { get; private set; }
        public bool Chasing => Mode != GameMode.Race && chase;
        public bool Transitioning { get; private set; }

        // Checkpoints are route samples spaced ~110 m apart; index 0 is the start/finish line.
        public int CheckpointCount => checkpoints.Length;
        public Vector3 CheckpointPosition(int k) => track.Point(checkpoints[k % checkpoints.Length]);
        public float CheckpointSpacing => track.Length / checkpoints.Length;
        public const float CheckpointRadius = 15f;
        int[] checkpoints = new int[0];
        public readonly List<Racer> Standings = new List<Racer>();

        float baseTimeScale = 1f;
        bool autopilot;
        int lastBeep;
        AudioSource sfx;
        AudioClip beep, go;

        // Dev screenshot schedule.
        string shotDir;
        readonly List<float> shotTimes = new List<float>();
        float quitAfter;
        float menuStartAt;

        void Awake()
        {
            Instance = this;
            laps = Mathf.Clamp(PlayerPrefs.GetInt("laps", laps), 1, 10);
            laps = Mathf.Clamp(Mathf.RoundToInt(DevFlags.GetFloat("-laps", laps)), 1, 10);
            difficulty = (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt("difficulty", (int)difficulty), 0, 2);
            baseTimeScale = DevFlags.GetFloat("-timescale", 1f);
            Mode = (GameMode)Mathf.Clamp(PlayerPrefs.GetInt("mode", 0), 0, 2);
            string devMode = DevFlags.Get("-mode");
            if (!string.IsNullOrEmpty(devMode) && System.Enum.TryParse(devMode, true, out GameMode m)) Mode = m;
            autopilot = DevFlags.Has("-autopilot");
            quitAfter = DevFlags.GetFloat("-quitafter", 0f);
            menuStartAt = DevFlags.GetFloat("-menustart", 0f);
            shotDir = DevFlags.Get("-shots");
            var times = DevFlags.Get("-shottimes");
            if (!string.IsNullOrEmpty(shotDir) && !string.IsNullOrEmpty(times))
            {
                foreach (var t in times.Split(','))
                    if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) shotTimes.Add(f);
                Directory.CreateDirectory(shotDir);
            }

            foreach (var r in racers) if (r.isPlayer) Player = r;
            // The player's chosen garage car (dev: -car <id> forces any car).
            string forced = DevFlags.Get("-car");
            if (Player) Garage.Equip(Player, CarPool, string.IsNullOrEmpty(forced) ? Garage.Selected : forced);
            int cpCount = Mathf.Max(8, Mathf.RoundToInt(track.Length / 110f));
            checkpoints = new int[cpCount];
            for (int k = 0; k < cpCount; k++) checkpoints[k] = track.Wrap(track.StartIndex + Mathf.RoundToInt(k * track.Count / (float)cpCount));
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            beep = SynthAudio.Tone(660f, 0.18f);
            go = SynthAudio.Tone(990f, 0.5f);
        }

        void Start()
        {
            Application.targetFrameRate = 120;
            if (DevFlags.Has("-dumpaudio")) { SynthAudio.Dump(DevFlags.Get("-dumpaudio")); Quit(); return; }
            if (DevFlags.Has("-handlingtest"))
            {
                // Scripted handling measurements; race logic stays off so nothing respawns the car.
                PrepareGrid();
                if (Player) Player.car.InputLocked = false;
                gameObject.AddComponent<HandlingProbe>().race = this;
                enabled = false;
                return;
            }
            if (DevFlags.Has("-showcase"))
            {
                PrepareGrid();
                gameObject.AddComponent<CarShowcase>().race = this;
                enabled = false;
                return;
            }
            if (DevFlags.Has("-autostart")) { PrepareGrid(); PrepareChase(); BeginCountdown(); }
            else EnterMenu();
        }

        // ---- Settings (driven by the main menu) ----

        public void SetLaps(int value)
        {
            laps = Mathf.Clamp(value, 1, 10);
            PlayerPrefs.SetInt("laps", laps);
        }

        public void SetMode(GameMode m)
        {
            Mode = m;
            PlayerPrefs.SetInt("mode", (int)m);
        }

        public void SetDifficulty(Difficulty d)
        {
            difficulty = d;
            PlayerPrefs.SetInt("difficulty", (int)d);
        }

        public float DifficultyScale => difficulty switch { Difficulty.Easy => 0.9f, Difficulty.Hard => 1.06f, _ => 1f };

        // ---- Flow ----

        // Main menu: every car (including the player's) drives on autopilot behind the menu.
        void EnterMenu()
        {
            if (chase) chase.Restore();
            PrepareGrid();
            State = RaceState.Menu;
            foreach (var r in racers)
            {
                SetAutopilot(r, true);
                if (r.ai) { r.ai.difficulty = 1f; r.ai.speedScale = 1f; }
                r.car.InputLocked = false;
            }
            if (chaseCamera) chaseCamera.cinematic = true;
            if (minimapCamera) minimapCamera.enabled = false;
        }

        public void StartRace() => Transition(() => { PrepareGrid(); PrepareChase(); BeginCountdown(); });

        void PrepareChase()
        {
            if (Chasing) chase.Prepare(Mode, autopilot);
            else if (chase) chase.Restore();
            if (Player && chaseCamera) chaseCamera.Snap();
        }

        // Called by ChaseMode when a pursuit or escape ends.
        public void EndChase(bool success, string title, string body, int prize)
        {
            State = RaceState.Finished;
            ResultTitle = title;
            ResultBody = body;
            hud.Flash(title, 2f);
            LastPrize = prize;
            if (!autopilot)
            {
                Garage.Credits += prize;
                PlayerPrefs.Save();
            }
            Debug.Log($"[Chase] {Mode} {(success ? "success" : "fail")}: {title} t={RaceTime:F1} prize={prize}");
        }
        public void BackToMenu() => Transition(EnterMenu);

        void Transition(System.Action action)
        {
            if (!Transitioning) StartCoroutine(TransitionRoutine(action));
        }

        IEnumerator TransitionRoutine(System.Action action)
        {
            Transitioning = true;
            yield return hud.Fade(0f, 1f, 0.35f);
            action();
            yield return null;
            yield return hud.Fade(1f, 0f, 0.45f);
            Transitioning = false;
        }

        void PrepareGrid()
        {
            if (chase) chase.Restore();
            SetPaused(false);
            State = RaceState.Countdown;
            Countdown = 3f;
            RaceTime = 0f;
            LastPrize = 0;

            // Player starts at the back of the grid.
            int slot = 0;
            foreach (var r in racers)
            {
                if (r.isPlayer) continue;
                PlaceOnGrid(r, slot++);
            }
            if (Player) PlaceOnGrid(Player, slot);

            foreach (var r in racers)
            {
                r.car.InputLocked = true;
                r.ResetProgress(track.FindClosest(r.transform.position));
                if (r.ai) r.ai.ResetLane();
                SetAutopilot(r, !r.isPlayer || autopilot);
                if (r.ai) { r.ai.difficulty = DifficultyScale; r.ai.speedScale = 1f; }
            }

            UpdateStandings();
            if (minimapCamera) minimapCamera.enabled = true;
            if (chaseCamera)
            {
                chaseCamera.cinematic = false;
                if (Player) chaseCamera.Snap();
            }
        }

        void PlaceOnGrid(Racer r, int slot)
        {
            float back = 8f + slot * 7f;
            int idx = track.Wrap(track.StartIndex - Mathf.RoundToInt(back / track.Spacing));
            float lateral = slot % 2 == 0 ? -3.2f : 3.2f;
            Vector3 pos = track.Point(idx) + track.Right(idx) * lateral + Vector3.up * 0.7f;
            r.car.Teleport(pos, Quaternion.LookRotation(track.FlatTangent(idx)));
        }

        void SetAutopilot(Racer r, bool on)
        {
            if (r.chaser) r.chaser.enabled = false;
            if (r.ai) r.ai.enabled = on;
            if (r.driver) r.driver.enabled = !on;
        }

        public void BeginCountdown()
        {
            State = RaceState.Countdown;
            Countdown = 3f;
            lastBeep = 4;
        }

        void StartRacing()
        {
            State = RaceState.Racing;
            RaceTime = 0f;
            foreach (var r in racers)
            {
                r.car.InputLocked = false;
                r.lapStart = 0f;
                if (r.ai) r.ai.skill = Mathf.Clamp(r.ai.skill, 0.7f, 1f);
            }
            sfx.PlayOneShot(go, 0.4f);
            hud.Flash("GO!", 1f);
            if (Chasing) chase.OnGo();
        }

        void Update()
        {
            HandleInput();

            if (State == RaceState.Countdown && !Paused)
            {
                Countdown -= Time.deltaTime;
                int c = Mathf.CeilToInt(Countdown);
                if (c < lastBeep && c > 0) { lastBeep = c; sfx.PlayOneShot(beep, 0.35f); }
                if (Countdown <= 0f) StartRacing();
            }
            else if (State == RaceState.Racing || State == RaceState.Finished)
            {
                RaceTime += Time.deltaTime;
            }

            if (Chasing && (State == RaceState.Racing || State == RaceState.Finished))
            {
                foreach (var r in racers) if (r.gameObject.activeSelf) UpdateRacer(r, false);
                if (State == RaceState.Racing && !Paused) chase.Tick(Time.deltaTime);
            }
            else if (State == RaceState.Racing || State == RaceState.Finished)
            {
                foreach (var r in racers) UpdateRacer(r);
                RubberBand();
            }
            else if (State == RaceState.Menu)
            {
                foreach (var r in racers) UpdateRacer(r, false);
            }
            UpdateStandings();
            DevTick();
        }

        void HandleInput()
        {
            if (Transitioning || State == RaceState.Menu) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (gp != null && gp.startButton.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame);
            bool reset = (kb != null && kb.rKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame);

            switch (State)
            {
                case RaceState.Countdown:
                case RaceState.Racing:
                    if (back) SetPaused(!Paused);
                    if (Paused)
                    {
                        if (reset) StartRace();
                        else if ((kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame)) BackToMenu();
                    }
                    else if (reset && State == RaceState.Racing && Player && !Player.finished) Respawn(Player);
                    break;

                case RaceState.Finished:
                    if (confirm || (gp != null && gp.buttonSouth.wasPressedThisFrame)) StartRace();
                    else if (back || (gp != null && gp.buttonEast.wasPressedThisFrame)) BackToMenu();
                    break;
            }
        }

        void SetPaused(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0f : baseTimeScale;
            AudioListener.pause = p;
        }

        void UpdateRacer(Racer r, bool countLaps = true)
        {
            Vector3 pos = r.transform.position;

            // Keep the nearest route sample; search the whole loop if the car has wandered off the route.
            Vector3 near = track.Point(r.index);
            bool onRoute = Flat(pos - near).sqrMagnitude < 40f * 40f;
            r.index = track.FindClosest(pos, onRoute ? r.index : -1);

            int n = CheckpointCount;
            int next = r.cpPassed % n;
            Vector3 cp = CheckpointPosition(next);
            if (countLaps && !r.finished && Flat(pos - cp).sqrMagnitude < CheckpointRadius * CheckpointRadius && Mathf.Abs(pos.y - cp.y) < 8f)
            {
                r.cpPassed++;
                r.lastCp = next;
                if (next == 0 && r.cpPassed > 1)
                {
                    float lap = RaceTime - r.lapStart;
                    r.lastLap = lap;
                    if (r.bestLap < 0f || lap < r.bestLap) r.bestLap = lap;
                    r.lapStart = RaceTime;
                    int done = r.LapsDone(n);
                    bool record = r.isPlayer && SaveBestLap(lap);
                    if (record)
                        hud.Flash("NEW BEST LAP", 1.6f);
                    else if (r.isPlayer && done < laps)
                        hud.Flash(done + 1 == laps ? "FINAL LAP" : "LAP " + (done + 1), 1.6f);
                    if (done >= laps) Finish(r);
                }
                else if (r.isPlayer && next != 0)
                {
                    sfx.PlayOneShot(beep, 0.15f);
                }
                next = r.cpPassed % n;
                cp = CheckpointPosition(next);
            }
            float toNext = Flat(pos - cp).magnitude;
            r.progress = r.cpPassed * CheckpointSpacing - Mathf.Min(toNext, CheckpointSpacing);

            if (State == RaceState.Countdown) return;
            float dt = Time.deltaTime;
            var car = r.car;
            Vector3 v = car.Body.linearVelocity;

            // Fell through the world or left the city.
            bool lost = pos.y < CityLayout.Height(pos.x, pos.z) - 4f || !CityLayout.InBounds(pos, 2f);
            r.offTrackTimer = lost ? r.offTrackTimer + dt : 0f;

            bool flipped = r.transform.up.y < 0.3f && v.magnitude < 4f;
            r.flipTimer = flipped ? r.flipTimer + dt : 0f;

            bool stuck = !r.isPlayer || r.ai.enabled || (r.chaser && r.chaser.enabled);
            stuck &= v.magnitude < 2f && !car.InputLocked && !(Chasing && chase.SuppressStuck(r));
            r.stuckTimer = stuck ? r.stuckTimer + dt : 0f;

            // HUD warning: heading away from the next checkpoint during the race.
            Vector3 toCp = Flat(cp - pos);
            bool wrong = countLaps && !r.finished && toCp.sqrMagnitude > 400f && Vector3.Dot(Flat(v), toCp.normalized) < -5f;
            r.wrongWayTimer = wrong ? r.wrongWayTimer + dt : 0f;

            // AI recovery: driving against the route direction while on the route.
            bool reversing = onRoute && Vector3.Dot(v, track.FlatTangent(r.index)) < -3f;
            r.reverseTimer = reversing ? r.reverseTimer + dt : 0f;

            if (r.offTrackTimer > 0.5f || r.flipTimer > 2f || r.stuckTimer > 3f || (r.ai.enabled && r.reverseTimer > 2.5f))
            {
                if (DevFlags.Has("-logrespawns"))
                    Debug.Log($"[Respawn] {r.racerName} t={RaceTime:F1} pos={pos} terrain={CityLayout.Height(pos.x, pos.z):F1} off={r.offTrackTimer:F1} flip={r.flipTimer:F1} stuck={r.stuckTimer:F1} reverse={r.reverseTimer:F1} cp={r.cpPassed}");
                Respawn(r);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Back onto the route: at the nearest route point if close to it, otherwise at the last checkpoint.
        public void Respawn(Racer r)
        {
            int idx = r.index;
            if (Flat(r.transform.position - track.Point(idx)).sqrMagnitude > 30f * 30f)
                idx = r.cpPassed > 0 ? checkpoints[r.lastCp] : track.FindClosest(r.transform.position);
            r.index = idx;
            float lane = 0f;
            Vector3 basePos = track.Point(idx);
            foreach (var other in racers)
            {
                if (other == r) continue;
                if ((other.transform.position - basePos).sqrMagnitude < 36f) { lane = 4f; break; }
            }
            Vector3 pos = basePos + track.Right(idx) * lane + Vector3.up * 1.2f;
            r.car.Teleport(pos, Quaternion.LookRotation(track.FlatTangent(idx)));
            r.stuckTimer = r.flipTimer = r.offTrackTimer = r.wrongWayTimer = r.reverseTimer = 0f;
            r.respawns++;
            if (r.ai) r.ai.ResetLane();
            if (r.isPlayer && chaseCamera) chaseCamera.Snap();
        }

        void Finish(Racer r)
        {
            r.finished = true;
            r.finishTime = RaceTime;
            if (!r.isPlayer) return;

            State = RaceState.Finished;
            SetAutopilot(r, true);
            if (r.ai) r.ai.speedScale = 0.7f;
            UpdateStandings();
            hud.Flash("FINISH!", 2f);
            if (!autopilot && (BestFinishRecord == 0 || r.position < BestFinishRecord)) PlayerPrefs.SetInt("bestFinish", r.position);
            if (!autopilot)
            {
                LastPrize = Garage.Prize(r.position, laps);
                Garage.Credits += LastPrize;
                PlayerPrefs.Save();
            }
        }

        // Records only count when a human is driving.
        bool SaveBestLap(float lap)
        {
            if (autopilot) return false;
            float best = BestLapRecord;
            if (best > 0f && lap >= best) return false;
            PlayerPrefs.SetFloat("bestLap", lap);
            return true;
        }

        void RubberBand()
        {
            if (!Player || Player.finished) return;
            float pp = Player.progress;
            foreach (var r in racers)
            {
                if (r.isPlayer || !r.ai) continue;
                float gap = r.progress - pp;
                r.ai.speedScale = Mathf.Lerp(1.04f, 0.96f, Mathf.InverseLerp(-200f, 200f, gap));
            }
        }

        void UpdateStandings()
        {
            Standings.Clear();
            Standings.AddRange(racers);
            Standings.Sort((a, b) =>
            {
                if (a.finished != b.finished) return a.finished ? -1 : 1;
                if (a.finished) return a.finishTime.CompareTo(b.finishTime);
                return b.progress.CompareTo(a.progress);
            });
            for (int i = 0; i < Standings.Count; i++) Standings[i].position = i + 1;
        }

        void DevTick()
        {
            float t = Time.unscaledTime;
            if (menuStartAt > 0f && t >= menuStartAt && State == RaceState.Menu) { menuStartAt = 0f; StartRace(); }
            if (shotTimes.Count > 0 && t >= shotTimes[0])
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, $"shot_{shotTimes[0]:000}.png"));
                shotTimes.RemoveAt(0);
            }
            if (quitAfter > 0f && t >= quitAfter)
            {
                if (Player)
                    Debug.Log($"[Racing] t={RaceTime:F1} state={State} playerPos={Player.position} lap={Player.CurrentLap(laps, CheckpointCount)} cp={Player.cpPassed} finished={Player.finished} best={Player.bestLap:F2}");
                if (Chasing && chase)
                    Debug.Log($"[Chase] mode={Mode} state={State} timeLeft={chase.TimeLeft:F1} dist={chase.Distance:F0} health={chase.TargetHealth:F2} bust={chase.Bust:F2} police={chase.PoliceCount} result={ResultTitle}");
                foreach (var r in Standings)
                    Debug.Log($"[Racing] P{r.position} {r.racerName} cps={r.cpPassed} finished={r.finished} time={r.finishTime:F2} best={r.bestLap:F2} respawns={r.respawns}");
                quitAfter = 0f;
                Quit();
            }
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
