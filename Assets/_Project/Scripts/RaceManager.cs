using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Racing
{
    public enum RaceState { Menu, Countdown, Racing, Finished }

    // Race flow: menu -> countdown -> racing -> results. Tracks laps, standings and respawns.
    public class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance { get; private set; }

        public TrackPath track;
        public Racer[] racers;
        public ChaseCamera chaseCamera;
        public RaceHUD hud;
        public ThemeController theme;
        public int laps = 3;

        public RaceState State { get; private set; }
        public float RaceTime { get; private set; }
        public float Countdown { get; private set; }
        public bool Paused { get; private set; }
        public Racer Player { get; private set; }
        public int MenuRow { get; private set; }
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

        void Awake()
        {
            Instance = this;
            laps = Mathf.Clamp(Mathf.RoundToInt(DevFlags.GetFloat("-laps", laps)), 1, 10);
            baseTimeScale = DevFlags.GetFloat("-timescale", 1f);
            autopilot = DevFlags.Has("-autopilot");
            quitAfter = DevFlags.GetFloat("-quitafter", 0f);
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
            ResetRace();
            if (DevFlags.Has("-autostart")) BeginCountdown();
        }

        public void ResetRace()
        {
            Paused = false;
            Time.timeScale = baseTimeScale;
            State = RaceState.Menu;
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
            }

            UpdateStandings();
            if (chaseCamera && Player) chaseCamera.Snap();
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
            UpdateStandings();
            DevTick();
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (gp != null && gp.startButton.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame);
            bool reset = (kb != null && kb.rKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame);

            switch (State)
            {
                case RaceState.Menu:
                    if (confirm || (gp != null && gp.buttonSouth.wasPressedThisFrame)) BeginCountdown();
                    bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (gp != null && gp.dpad.left.wasPressedThisFrame);
                    bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (gp != null && gp.dpad.right.wasPressedThisFrame);
                    bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (gp != null && gp.dpad.up.wasPressedThisFrame);
                    bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (gp != null && gp.dpad.down.wasPressedThisFrame);
                    if (up || down) MenuRow = 1 - MenuRow;
                    if (MenuRow == 0)
                    {
                        if (left) laps = Mathf.Max(1, laps - 1);
                        if (right) laps = Mathf.Min(10, laps + 1);
                    }
                    else if ((left || right) && theme) theme.Toggle();
                    if (kb != null && kb.tKey.wasPressedThisFrame && theme) theme.Toggle();
                    if (back) Quit();
                    break;

                case RaceState.Countdown:
                case RaceState.Racing:
                    if (back) SetPaused(!Paused);
                    if (Paused)
                    {
                        if (reset) { ResetRace(); BeginCountdown(); }
                        else if (kb != null && kb.qKey.wasPressedThisFrame) ResetRace();
                    }
                    else if (reset && State == RaceState.Racing && Player && !Player.finished) Respawn(Player);
                    break;

                case RaceState.Finished:
                    if (confirm) { ResetRace(); BeginCountdown(); }
                    else if (back) ResetRace();
                    break;
            }
        }

        void SetPaused(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0f : baseTimeScale;
            AudioListener.pause = p;
        }

        void UpdateRacer(Racer r)
        {
            int n = track.Count;
            int prevRel = track.Rel(r.index);
            r.index = track.FindClosest(r.transform.position, r.index);
            int rel = track.Rel(r.index);
            if (prevRel > n * 3 / 4 && rel < n / 4) r.crossings++;
            else if (prevRel < n / 4 && rel > n * 3 / 4) r.crossings--;

            if (r.crossings > r.maxCrossings && !r.finished)
            {
                r.maxCrossings = r.crossings;
                if (r.maxCrossings >= 2)
                {
                    float lap = RaceTime - r.lapStart;
                    r.lastLap = lap;
                    if (r.bestLap < 0f || lap < r.bestLap) r.bestLap = lap;
                    r.lapStart = RaceTime;
                    if (r.isPlayer && r.maxCrossings <= laps)
                        hud.Flash(r.maxCrossings == laps ? "FINAL LAP" : "LAP " + r.maxCrossings, 1.6f);
                }
                if (r.maxCrossings > laps) Finish(r);
            }

            if (State != RaceState.Racing && State != RaceState.Finished) return;
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

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
