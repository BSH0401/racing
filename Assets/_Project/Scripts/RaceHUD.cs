using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Racing
{
    // Runtime-built uGUI overlay: race info, countdown, menu, pause and results screens.
    public class RaceHUD : MonoBehaviour
    {
        public RaceManager race;

        Font font;
        Text posText, lapText, timeText, bestText, speedText, boardText, centerText, flashText, wrongWayText;
        GameObject hudRoot, menuPanel, pausePanel, resultPanel;
        Text menuLaps, resultTitle, resultBody;
        float flashTimer;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var root = canvasGo.transform;

            hudRoot = Group("HUD", root);
            var h = hudRoot.transform;
            posText = Label(h, 84, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(40f, -24f), new Vector2(500f, 110f));
            lapText = Label(h, 40, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(44f, -130f), new Vector2(500f, 56f));
            boardText = Label(h, 26, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(44f, -200f), new Vector2(420f, 300f));
            timeText = Label(h, 48, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(600f, 64f));
            bestText = Label(h, 26, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(800f, 40f));
            speedText = Label(h, 30, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-48f, 36f), new Vector2(520f, 140f));
            speedText.supportRichText = true;
            wrongWayText = Label(h, 64, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1000f, 90f));
            wrongWayText.color = new Color(1f, 0.3f, 0.25f);
            wrongWayText.text = "WRONG WAY";
            Label(h, 20, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(40f, 24f), new Vector2(1200f, 30f)).text =
                "R reset car   C camera   ESC pause";

            centerText = Label(root, 220, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 300f));
            flashText = Label(root, 90, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1400f, 160f));
            flashText.color = new Color(1f, 0.85f, 0.2f);

            // Menu.
            menuPanel = Panel("Menu", root, new Color(0f, 0f, 0f, 0.55f));
            var m = menuPanel.transform;
            var title = Label(m, 150, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1600f, 200f));
            title.text = "RACING";
            title.fontStyle = FontStyle.BoldAndItalic;
            title.color = new Color(1f, 0.82f, 0.15f);
            Label(m, 36, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1600f, 50f)).text = "6-car circuit race";
            menuLaps = Label(m, 54, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1000f, 80f));
            var start = Label(m, 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(1400f, 60f));
            start.text = "Press ENTER  (gamepad A / Start)  to race";
            start.color = new Color(0.6f, 1f, 0.6f);
            var controls = Label(m, 28, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(1600f, 260f));
            controls.text =
                "W / Up  accelerate      S / Down  brake & reverse      A D / Left Right  steer\n" +
                "SPACE  handbrake      R  reset car      C  camera      ESC  pause\n" +
                "Gamepad: RT / LT throttle & brake, left stick steer, A handbrake, Y reset";
            controls.color = new Color(0.85f, 0.85f, 0.85f);

            // Pause.
            pausePanel = Panel("Pause", root, new Color(0f, 0f, 0f, 0.6f));
            var p = Label(pausePanel.transform, 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 400f));
            p.text = "<size=110><b>PAUSED</b></size>\n\nESC  resume\nR  restart race\nQ  back to menu";
            p.supportRichText = true;

            // Results.
            resultPanel = Panel("Results", root, new Color(0f, 0f, 0f, 0.6f));
            var rp = resultPanel.transform;
            resultTitle = Label(rp, 90, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 320f), new Vector2(1600f, 130f));
            resultTitle.color = new Color(1f, 0.82f, 0.15f);
            resultBody = Label(rp, 36, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1100f, 420f));
            resultBody.supportRichText = true;
            var again = Label(rp, 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(1400f, 60f));
            again.text = "ENTER  race again        ESC  menu";
            again.color = new Color(0.6f, 1f, 0.6f);
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

            menuPanel.SetActive(state == RaceState.Menu);
            pausePanel.SetActive(race.Paused);
            resultPanel.SetActive(state == RaceState.Finished && !race.Paused);
            hudRoot.SetActive(state != RaceState.Menu);

            if (state == RaceState.Menu)
                menuLaps.text = $"LAPS   <   {race.laps}   >";
            menuLaps.supportRichText = false;

            centerText.text = state == RaceState.Countdown ? Mathf.CeilToInt(race.Countdown).ToString() : "";

            flashTimer -= Time.unscaledDeltaTime;
            flashText.enabled = flashTimer > 0f && state != RaceState.Menu;

            if (player)
            {
                int n = race.racers.Length;
                posText.text = $"{player.position}<size=44>/{n}</size>";
                posText.supportRichText = true;
                lapText.text = $"LAP {player.CurrentLap(race.laps)}/{race.laps}";
                float lapTime = state == RaceState.Racing ? race.RaceTime - player.lapStart : 0f;
                timeText.text = player.finished ? FormatTime(player.finishTime) : FormatTime(state == RaceState.Racing ? race.RaceTime : 0f);
                bestText.text = $"LAP {FormatTime(lapTime)}    LAST {FormatLap(player.lastLap)}    BEST {FormatLap(player.bestLap)}";
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
            boardText.supportRichText = true;
            boardText.text = sb.ToString();

            if (state == RaceState.Finished && player)
            {
                resultTitle.text = $"FINISHED  {Ordinal(player.position)}";
                var rb = new StringBuilder();
                foreach (var r in race.Standings)
                {
                    string name = r.racerName.PadRight(10);
                    if (r.isPlayer) name = $"<b>{name}</b>";
                    string time = r.finished ? FormatTime(r.finishTime) : "racing...";
                    rb.Append($"{r.position}.  <color=#{r.ColorHex}>■</color>  {name}   {time}   best {FormatLap(r.bestLap)}\n");
                }
                resultBody.text = rb.ToString();
            }
        }

        static string Ordinal(int n)
        {
            string suf = (n % 100 >= 11 && n % 100 <= 13) ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n + suf;
        }

        public static string FormatTime(float t)
        {
            if (t < 0f) t = 0f;
            int m = (int)(t / 60f);
            float s = t - m * 60f;
            return $"{m}:{s:00.000}";
        }

        static string FormatLap(float t) => t < 0f || t == 0f ? "-:--.---" : FormatTime(t);

        GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        GameObject Panel(string name, Transform parent, Color color)
        {
            var go = Group(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        Text Label(Transform parent, int size, TextAnchor anchor, Vector2 anchorPoint, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchorPoint;
            rt.pivot = anchorPoint;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }
}
