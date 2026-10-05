using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Racing
{
    // Runtime-built uGUI overlay: race info, countdown, pause and results screens, screen fades.
    public class RaceHUD : MonoBehaviour
    {
        public RaceManager race;

        Text posText, lapText, timeText, bestText, speedText, boardText, centerText, flashText, wrongWayText;
        GameObject hudRoot, pausePanel, resultPanel;
        Text resultTitle, resultBody, againText;
        // Chase modes: status line and a damage / busted meter under the timer.
        GameObject chaseRoot;
        Text chaseLabel;
        Image chaseFill;
        Image fade;
        float flashTimer;

        void Awake()
        {
            var root = UIKit.Canvas("HUDCanvas", transform, 10).transform;

            hudRoot = UIKit.Group("HUD", root);
            var h = hudRoot.transform;
            posText = UIKit.Label(h, 84, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(40f, -24f), new Vector2(500f, 110f));
            lapText = UIKit.Label(h, 40, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(44f, -130f), new Vector2(500f, 56f));
            boardText = UIKit.Label(h, 26, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(44f, -200f), new Vector2(420f, 300f));
            timeText = UIKit.Label(h, 48, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(600f, 64f));
            bestText = UIKit.Label(h, 26, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(800f, 40f));
            speedText = UIKit.Label(h, 30, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-48f, 36f), new Vector2(520f, 140f));
            wrongWayText = UIKit.Label(h, 64, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1000f, 90f));
            wrongWayText.color = new Color(1f, 0.3f, 0.25f);
            wrongWayText.text = "WRONG WAY";
            chaseRoot = UIKit.Group("Chase", h);
            var c = chaseRoot.transform;
            chaseLabel = UIKit.Label(c, 30, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(1000f, 44f));
            UIKit.Rect("MeterBack", c, new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(420f, 18f), new Color(0f, 0f, 0f, 0.55f));
            chaseFill = UIKit.Rect("MeterFill", c, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(420f, 18f), Color.red);
            chaseFill.rectTransform.SetParent(c.Find("MeterBack"), false);
            chaseFill.rectTransform.anchoredPosition = Vector2.zero;

            UIKit.Label(h, 20, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(40f, 24f), new Vector2(1200f, 30f)).text =
                "R reset car   C camera   ESC pause";

            centerText = UIKit.Label(root, 220, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 300f));
            flashText = UIKit.Label(root, 90, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1400f, 160f));
            flashText.color = new Color(1f, 0.85f, 0.2f);

            // Pause.
            pausePanel = UIKit.Panel("Pause", root, new Color(0f, 0f, 0f, 0.6f));
            var p = UIKit.Label(pausePanel.transform, 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 400f));
            p.text = "<size=110><b>PAUSED</b></size>\n\nESC  resume\nR  restart race\nQ  main menu";

            // Results.
            resultPanel = UIKit.Panel("Results", root, new Color(0f, 0f, 0f, 0.6f));
            var rp = resultPanel.transform;
            resultTitle = UIKit.Label(rp, 90, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 320f), new Vector2(1600f, 130f));
            resultTitle.color = new Color(1f, 0.82f, 0.15f);
            resultBody = UIKit.Label(rp, 36, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1100f, 420f));
            var again = againText = UIKit.Label(rp, 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(1400f, 60f));
            again.text = "ENTER  race again        ESC  main menu";
            again.color = new Color(0.6f, 1f, 0.6f);

            // Full-screen fade on its own top-most canvas.
            var fadeRoot = UIKit.Canvas("FadeCanvas", transform, 100).transform;
            fade = UIKit.Rect("Fade", fadeRoot, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.black);
            UIKit.Stretch(fade.rectTransform);
            fade.raycastTarget = true;
            SetFade(0f);
        }

        void SetFade(float a)
        {
            fade.color = new Color(0f, 0f, 0f, a);
            fade.enabled = a > 0.001f;
        }

        public IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                SetFade(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            SetFade(to);
        }

        public void Flash(string text, float seconds)
        {
            flashText.text = text;
            flashTimer = seconds;
        }

        void Update()
        {
            if (!race) return;
            var state = race.State;
            var player = race.Player;

            pausePanel.SetActive(race.Paused);
            resultPanel.SetActive(state == RaceState.Finished && !race.Paused);
            hudRoot.SetActive(state != RaceState.Menu);

            centerText.text = state == RaceState.Countdown ? Mathf.CeilToInt(race.Countdown).ToString() : "";

            flashTimer -= Time.unscaledDeltaTime;
            flashText.enabled = flashTimer > 0f && state != RaceState.Menu;
            if (state == RaceState.Menu) return;

            bool chasing = race.Chasing;
            chaseRoot.SetActive(chasing);
            posText.enabled = lapText.enabled = boardText.enabled = bestText.enabled = !chasing;
            againText.text = chasing ? "ENTER  play again        ESC  main menu" : "ENTER  race again        ESC  main menu";
            if (chasing && player)
            {
                var ch = race.chase;
                float shown = state == RaceState.Countdown ? (race.Mode == GameMode.Pursuit ? ch.pursuitTime : ch.escapeTime) : Mathf.Max(0f, ch.TimeLeft);
                timeText.text = UIKit.FormatTime(shown);
                timeText.color = shown < 15f && state == RaceState.Racing ? new Color(1f, 0.35f, 0.3f) : Color.white;
                speedText.text = $"<size=120><b>{Mathf.RoundToInt(player.car.SpeedKmh)}</b></size> km/h";
                wrongWayText.enabled = false;
                float meter;
                if (race.Mode == GameMode.Pursuit)
                {
                    string warn = ch.LostTimer > 0f ? "   <color=#ff6a5a><b>LOSING THE SUSPECT!</b></color>" : "";
                    chaseLabel.text = $"SUSPECT  <b>{ch.Distance:F0} m</b>{warn}";
                    meter = ch.TargetHealth;
                    chaseFill.color = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(1f, 0.8f, 0.2f), meter);
                }
                else
                {
                    string state2 = ch.Bust > 0.05f ? "   <color=#ff6a5a><b>BUSTED!</b></color>" : ch.LostTimer > 0f ? "   <color=#7dff8a><b>LOSING THEM...</b></color>" : "";
                    chaseLabel.text = $"POLICE  <b>{(ch.Distance < 9999f ? ch.Distance.ToString("F0") : "-")} m</b>   ·   {ch.PoliceCount} units{state2}";
                    meter = ch.Bust;
                    chaseFill.color = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.5f ? new Color(1f, 0.15f, 0.15f) : new Color(0.2f, 0.4f, 1f);
                }
                chaseFill.rectTransform.sizeDelta = new Vector2(420f * Mathf.Clamp01(meter), 18f);
                if (state == RaceState.Finished)
                {
                    resultTitle.text = race.ResultTitle;
                    var cb = new StringBuilder(race.ResultBody).Append('\n');
                    if (race.LastPrize > 0)
                        cb.Append($"\n<color=#ffd23a><b>+{race.LastPrize:N0} CR</b></color>    credits {Garage.Credits:N0} CR\n");
                    resultBody.text = cb.ToString();
                }
                return;
            }

            if (player)
            {
                timeText.color = Color.white;
                int n = race.racers.Length;
                posText.text = $"{player.position}<size=44>/{n}</size>";
                int cps = race.CheckpointCount;
                int inLap = player.cpPassed <= 0 ? 0 : (player.cpPassed - 1) % cps;
                lapText.text = $"LAP {player.CurrentLap(race.laps, cps)}/{race.laps}   <size=28>CP {inLap}/{cps}</size>";
                float lapTime = state == RaceState.Racing ? race.RaceTime - player.lapStart : 0f;
                timeText.text = player.finished ? UIKit.FormatTime(player.finishTime) : UIKit.FormatTime(state == RaceState.Racing ? race.RaceTime : 0f);
                bestText.text = $"LAP {UIKit.FormatTime(lapTime)}    LAST {UIKit.FormatLap(player.lastLap)}    BEST {UIKit.FormatLap(player.bestLap)}";
                speedText.text = $"<size=120><b>{Mathf.RoundToInt(player.car.SpeedKmh)}</b></size> km/h";
                wrongWayText.enabled = state == RaceState.Racing && player.wrongWayTimer > 1f && !player.finished;
            }

            var sb = new StringBuilder();
            foreach (var r in race.Standings)
            {
                string name = r.isPlayer ? $"<b>{r.racerName}</b>" : r.racerName;
                sb.Append($"{r.position}. <color=#{r.ColorHex}>■</color> {name}");
                if (r.finished) sb.Append("  <color=#aaaaaa>fin</color>");
                sb.Append('\n');
            }
            boardText.text = sb.ToString();

            if (state == RaceState.Finished && player)
            {
                resultTitle.text = $"FINISHED  {UIKit.Ordinal(player.position)}";
                var rb = new StringBuilder();
                foreach (var r in race.Standings)
                {
                    string name = r.racerName.PadRight(10);
                    if (r.isPlayer) name = $"<b>{name}</b>";
                    string time = r.finished ? UIKit.FormatTime(r.finishTime) : "racing...";
                    rb.Append($"{r.position}.  <color=#{r.ColorHex}>■</color>  {name}   {time}   best {UIKit.FormatLap(r.bestLap)}\n");
                }
                if (race.LastPrize > 0)
                    rb.Append($"\n<color=#ffd23a><b>+{race.LastPrize:N0} CR</b></color>    credits {Garage.Credits:N0} CR  ·  spend them in the GARAGE\n");
                resultBody.text = rb.ToString();
            }
        }
    }
}
