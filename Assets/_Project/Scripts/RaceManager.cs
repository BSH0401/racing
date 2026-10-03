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
        public ChaseCamera chaseCamera;
        public RaceHUD hud;
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
        public bool Transitioning { get; private set; }
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
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            beep = SynthAudio.Tone(660f, 0.18f);
            go = SynthAudio.Tone(990f, 0.5f);
        }

        void Start()
        {
            Application.targetFrameRate = 120;
            if (DevFlags.Has("-autostart")) { PrepareGrid(); BeginCountdown(); }
            else EnterMenu();
        }

        // ---- Settings (driven by the main menu) ----

        public void SetLaps(int value)
        {
            laps = Mathf.Clamp(value, 1, 10);
            PlayerPrefs.SetInt("laps", laps);
        }

        public void SetDifficulty(Difficulty d)
        {
            difficulty = d;
            PlayerPrefs.SetInt("difficulty", (int)d);
        }

        float DifficultyScale => difficulty switch { Difficulty.Easy => 0.9f, Difficulty.Hard => 1.06f, _ => 1f };

        // ---- Flow ----

        // Main menu: every car (including the player's) drives on autopilot behind the menu.
        void EnterMenu()
        {
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

        public void StartRace() => Transition(() => { PrepareGrid(); BeginCountdown(); });
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
            SetPaused(false);
            State = RaceState.Countdown;
            Countdown = 3f;
            RaceTime = 0f;

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
            sfx.PlayOneShot(go, 0.6f);
            hud.Flash("GO!", 1f);
        }

        void Update()
        {
            HandleInput();

            if (State == RaceState.Countdown && !Paused)
            {
                Countdown -= Time.deltaTime;
                int c = Mathf.CeilToInt(Countdown);
                if (c < lastBeep && c > 0) { lastBeep = c; sfx.PlayOneShot(beep, 0.6f); }
                if (Countdown <= 0f) StartRacing();
            }
            else if (State == RaceState.Racing || State == RaceState.Finished)
            {
                RaceTime += Time.deltaTime;
            }

            if (State == RaceState.Racing || State == RaceState.Finished)
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
            int n = track.Count;
            int prevRel = track.Rel(r.index);
            r.index = track.FindClosest(r.transform.position, r.index);
            int rel = track.Rel(r.index);
            if (prevRel > n * 3 / 4 && rel < n / 4) r.crossings++;
            else if (prevRel < n / 4 && rel > n * 3 / 4) r.crossings--;

            if (countLaps && r.crossings > r.maxCrossings && !r.finished)
            {
                r.maxCrossings = r.crossings;
                if (r.maxCrossings >= 2)
                {
                    float lap = RaceTime - r.lapStart;
                    r.lastLap = lap;
                    if (r.bestLap < 0f || lap < r.bestLap) r.bestLap = lap;
                    r.lapStart = RaceTime;
                    bool record = r.isPlayer && SaveBestLap(lap);
                    if (record)
                        hud.Flash("NEW BEST LAP", 1.6f);
                    else if (r.isPlayer && r.maxCrossings <= laps)
                        hud.Flash(r.maxCrossings == laps ? "FINAL LAP" : "LAP " + r.maxCrossings, 1.6f);
                }
                if (r.maxCrossings > laps) Finish(r);
            }

            if (State == RaceState.Countdown) return;
            float dt = Time.deltaTime;
            var car = r.car;

            // Off the track (over a wall, fell off): respawn quickly.
            float lateral = Mathf.Abs(track.LateralOffset(r.transform.position, r.index));
            float drop = track.Point(r.index).y - r.transform.position.y;
            bool lost = lateral > track.roadHalfWidth + 4.5f || drop > 6f;
            r.offTrackTimer = lost ? r.offTrackTimer + dt : 0f;

            bool flipped = r.transform.up.y < 0.3f && car.Body.linearVelocity.magnitude < 4f;
            r.flipTimer = flipped ? r.flipTimer + dt : 0f;

            bool stuck = !r.isPlayer || r.ai.enabled;
            stuck &= car.Body.linearVelocity.magnitude < 2f;
            r.stuckTimer = stuck ? r.stuckTimer + dt : 0f;

            float along = Vector3.Dot(car.Body.linearVelocity, track.FlatTangent(r.index));
            r.wrongWayTimer = along < -3f ? r.wrongWayTimer + dt : 0f;

            if (r.offTrackTimer > 1.2f || r.flipTimer > 2f || r.stuckTimer > 3f || (r.ai.enabled && r.wrongWayTimer > 2.5f))
                Respawn(r);
        }

        public void Respawn(Racer r)
        {
            int idx = r.index;
            float lane = 0f;
            Vector3 basePos = track.Point(idx);
            foreach (var other in racers)
            {
                if (other == r) continue;
                if ((other.transform.position - basePos).sqrMagnitude < 36f) { lane = 4f; break; }
            }
            Vector3 pos = basePos + track.Right(idx) * lane + Vector3.up * 1.2f;
            r.car.Teleport(pos, Quaternion.LookRotation(track.FlatTangent(idx)));
            r.stuckTimer = r.flipTimer = r.offTrackTimer = r.wrongWayTimer = 0f;
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
            float pp = Player.Progress(track);
            float perMeter = 1f / track.Spacing;
            foreach (var r in racers)
            {
                if (r.isPlayer || !r.ai) continue;
                float gap = (r.Progress(track) - pp) / perMeter;
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
                return b.Progress(track).CompareTo(a.Progress(track));
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
                    Debug.Log($"[Racing] t={RaceTime:F1} state={State} playerPos={Player.position} lap={Player.CurrentLap(laps)} finished={Player.finished} best={Player.bestLap:F2}");
                foreach (var r in Standings)
                    Debug.Log($"[Racing] P{r.position} {r.racerName} laps={r.maxCrossings} finished={r.finished} time={r.finishTime:F2} best={r.bestLap:F2}");
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
