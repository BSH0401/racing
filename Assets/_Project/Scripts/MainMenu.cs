using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Racing
{
    // Title screen shown over the attract-mode race: Start / Settings / Controls / Quit.
    // Keyboard, gamepad and mouse all work.
    public class MainMenu : MonoBehaviour
    {
        public RaceManager race;

        static readonly Color Accent = new Color(1f, 0.82f, 0.15f);
        static readonly Color PanelColor = new Color(0.03f, 0.04f, 0.07f, 0.8f);

        enum Page { Main, Settings, Controls }

        class Item
        {
            public Image background, bar;
            public Text label, value;
            public Action<int> onActivate; // +1 confirm / right, -1 left
            public Func<string> getValue;
        }

        GameObject root;
        CanvasGroup group;
        readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        readonly Dictionary<Page, List<Item>> items = new Dictionary<Page, List<Item>>();
        Page page;
        int selected;
        Text recordText, settingsText;
        AudioSource sfx;
        AudioClip tick, confirm;
        float shown;

        void Awake()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            tick = SynthAudio.Tone(880f, 0.05f);
            confirm = SynthAudio.Tone(1320f, 0.12f);

            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var canvas = UIKit.Canvas("MainMenuCanvas", transform, 20);
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            root = canvas.gameObject;
            group = root.AddComponent<CanvasGroup>();
            var t = root.transform;

            // Left panel with logo.
            var panel = UIKit.Rect("Panel", t, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(720f, 1080f), PanelColor);
            panel.rectTransform.anchorMin = new Vector2(0f, 0f);
            panel.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel.rectTransform.pivot = new Vector2(0f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(720f, 0f);
            var edge = UIKit.Rect("Edge", panel.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(6f, 0f), Accent);
            edge.rectTransform.anchorMin = new Vector2(1f, 0f);
            edge.rectTransform.anchorMax = new Vector2(1f, 1f);

            var logo = UIKit.Label(panel.transform, 150, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(72f, -70f), new Vector2(640f, 180f));
            logo.text = "<i><b>RACING</b></i>";
            logo.color = Accent;
            UIKit.Rect("Stripe", panel.transform, new Vector2(0f, 1f), new Vector2(80f, -246f), new Vector2(470f, 10f), new Color(0.9f, 0.12f, 0.1f));
            UIKit.Rect("Stripe2", panel.transform, new Vector2(0f, 1f), new Vector2(560f, -246f), new Vector2(60f, 10f), Color.white);
            var sub = UIKit.Label(panel.transform, 32, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(84f, -276f), new Vector2(600f, 50f));
            sub.text = "O P E N   C I T Y   S T R E E T   R A C E";
            sub.color = new Color(0.85f, 0.87f, 0.92f);

            var footer = UIKit.Label(panel.transform, 22, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(84f, 40f), new Vector2(600f, 60f));
            footer.text = "UP / DOWN  select     ENTER  confirm     ESC  back\nLEFT / RIGHT  change settings     Mouse supported";
            footer.color = new Color(0.6f, 0.62f, 0.68f);

            // Record card (bottom right).
            var card = UIKit.Rect("Card", t, new Vector2(1f, 0f), new Vector2(-60f, 60f), new Vector2(600f, 200f), PanelColor);
            UIKit.Rect("CardBar", card.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 200f), Accent);
            var cardTitle = UIKit.Label(card.transform, 24, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(34f, -22f), new Vector2(540f, 30f));
            cardTitle.text = "RECORDS";
            cardTitle.color = Accent;
            recordText = UIKit.Label(card.transform, 34, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(34f, -58f), new Vector2(540f, 90f));
            settingsText = UIKit.Label(card.transform, 24, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(34f, -150f), new Vector2(540f, 30f));
            settingsText.color = new Color(0.75f, 0.77f, 0.82f);

            var version = UIKit.Label(t, 20, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(1100f, 30f));
            version.text = "v0.6   ·   Cars: Kenney  ·  Textures: ambientCG  ·  HDRI & props: Poly Haven  (CC0)";
            version.color = new Color(1f, 1f, 1f, 0.6f);

            BuildMain(panel.transform);
            BuildSettings(panel.transform);
            BuildControls(panel.transform);
            Show(Page.Main);
        }

        // ---- Pages ----

        void BuildMain(Transform panel)
        {
            var list = NewPage(Page.Main, panel);
            AddItem(Page.Main, list, "START RACE", null, d => { if (d > 0) StartRace(); });
            AddItem(Page.Main, list, "SETTINGS", null, d => { if (d > 0) Show(Page.Settings); });
            AddItem(Page.Main, list, "CONTROLS", null, d => { if (d > 0) Show(Page.Controls); });
            AddItem(Page.Main, list, "QUIT", null, d => { if (d > 0) race.Quit(); });
        }

        void BuildSettings(Transform panel)
        {
            var list = NewPage(Page.Settings, panel);
            AddItem(Page.Settings, list, "LAPS", () => race.laps.ToString(), d => race.SetLaps(Wrap(race.laps + d, 1, 10)));
            AddItem(Page.Settings, list, "TIME", () => race.theme && race.theme.Current == RaceTheme.Night ? "NIGHT" : "DAY", d => { if (race.theme) race.theme.Toggle(); });
            AddItem(Page.Settings, list, "AI", () => race.difficulty.ToString().ToUpper(), d => race.SetDifficulty((Difficulty)Wrap((int)race.difficulty + d, 0, 2)));
            AddItem(Page.Settings, list, "BACK", null, d => { if (d > 0) Show(Page.Main); });
        }

        void BuildControls(Transform panel)
        {
            var list = NewPage(Page.Controls, panel);
            var text = UIKit.Label(pages[Page.Controls].transform, 27, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(84f, -360f), new Vector2(600f, 420f));
            text.text =
                "<color=#ffd23a>KEYBOARD</color>\n" +
                "W / UP          accelerate\n" +
                "S / DOWN       brake / reverse\n" +
                "A D / LEFT RIGHT   steer\n" +
                "SPACE           handbrake\n" +
                "R  reset car     C  camera     ESC  pause\n\n" +
                "<color=#ffd23a>GAMEPAD</color>\n" +
                "RT / LT  throttle / brake     L-stick  steer\n" +
                "A  handbrake     Y  reset     RB  camera";
            list.anchoredPosition = new Vector2(80f, -800f);
            AddItem(Page.Controls, list, "BACK", null, d => { if (d > 0) Show(Page.Main); });
        }

        RectTransform NewPage(Page p, Transform panel)
        {
            var go = UIKit.Group(p.ToString(), panel);
            pages[p] = go;
            items[p] = new List<Item>();
            var list = new GameObject("Items", typeof(RectTransform));
            list.transform.SetParent(go.transform, false);
            var rt = (RectTransform)list.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(80f, -380f);
            rt.sizeDelta = new Vector2(580f, 400f);
            return rt;
        }

        void AddItem(Page p, RectTransform list, string label, Func<string> getValue, Action<int> onActivate)
        {
            int index = items[p].Count;
            var bg = UIKit.Rect("Item_" + label, list, new Vector2(0f, 1f), new Vector2(0f, -index * 92f), new Vector2(580f, 80f), Color.clear);
            bg.raycastTarget = true;
            var bar = UIKit.Rect("Bar", bg.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 80f), Accent);
            var text = UIKit.Label(bg.transform, 44, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(400f, 80f), false);
            text.text = label;
            text.fontStyle = FontStyle.Bold;
            Text value = null;
            if (getValue != null)
            {
                value = UIKit.Label(bg.transform, 40, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(300f, 80f), false);
                value.fontStyle = FontStyle.Bold;
            }

            var item = new Item { background = bg, bar = bar, label = text, value = value, onActivate = onActivate, getValue = getValue };
            items[p].Add(item);

            var hook = bg.gameObject.AddComponent<MenuPointer>();
            hook.onEnter = () => Select(index);
            hook.onClick = right => { Select(index); Activate(right ? 1 : -1, true); };
        }

        void Show(Page p)
        {
            foreach (var kv in pages) kv.Value.SetActive(kv.Key == p);
            page = p;
            selected = 0;
            Refresh();
        }

        // ---- Input ----

        void Update()
        {
            bool visible = race && race.State == RaceState.Menu;
            if (root.activeSelf != visible)
            {
                root.SetActive(visible);
                if (visible) { Show(Page.Main); shown = 0f; }
            }
            if (!visible) return;

            shown += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(shown / 0.4f);
            if (race.Transitioning) return;

            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (gp != null && (gp.dpad.up.wasPressedThisFrame || gp.leftStick.up.wasPressedThisFrame));
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (gp != null && (gp.dpad.down.wasPressedThisFrame || gp.leftStick.down.wasPressedThisFrame));
            bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (gp != null && (gp.dpad.left.wasPressedThisFrame || gp.leftStick.left.wasPressedThisFrame));
            bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (gp != null && (gp.dpad.right.wasPressedThisFrame || gp.leftStick.right.wasPressedThisFrame));
            bool ok = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame));
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);

            int count = items[page].Count;
            if (up) { Select((selected - 1 + count) % count); }
            if (down) { Select((selected + 1) % count); }

            var item = items[page][selected];
            if (item.getValue != null && (left || right)) Activate(right ? 1 : -1, false);
            else if (ok) Activate(1, true);
            if (back && page != Page.Main) { sfx.PlayOneShot(tick, 0.4f); Show(Page.Main); }

            if (kb != null && kb.tKey.wasPressedThisFrame && race.theme) { race.theme.Toggle(); Refresh(); }
            Refresh();
        }

        void Select(int index)
        {
            if (index == selected) return;
            selected = index;
            sfx.PlayOneShot(tick, 0.35f);
            Refresh();
        }

        void Activate(int dir, bool confirmSound)
        {
            var item = items[page][selected];
            sfx.PlayOneShot(confirmSound ? confirm : tick, 0.45f);
            item.onActivate?.Invoke(dir);
            Refresh();
        }

        void StartRace()
        {
            race.StartRace();
        }

        void Refresh()
        {
            if (!items.ContainsKey(page)) return;
            var list = items[page];
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                bool sel = i == selected;
                it.background.color = sel ? new Color(1f, 1f, 1f, 0.12f) : Color.clear;
                it.bar.enabled = sel;
                it.label.color = sel ? Accent : new Color(0.9f, 0.9f, 0.92f);
                it.label.rectTransform.anchoredPosition = new Vector2(sel ? 44f : 34f, 0f);
                if (it.value)
                {
                    it.value.text = sel ? $"<   {it.getValue()}   >" : it.getValue();
                    it.value.color = sel ? Color.white : new Color(0.7f, 0.72f, 0.78f);
                }
            }

            float best = race ? race.BestLapRecord : -1f;
            int finish = race ? race.BestFinishRecord : 0;
            recordText.text = $"BEST LAP   <b>{UIKit.FormatLap(best)}</b>\nBEST FINISH   <b>{(finish > 0 ? UIKit.Ordinal(finish) : "-")}</b>";
            if (race)
            {
                string time = race.theme && race.theme.Current == RaceTheme.Night ? "NIGHT" : "DAY";
                settingsText.text = $"{race.laps} LAPS  ·  {time}  ·  AI {race.difficulty.ToString().ToUpper()}";
            }
        }

        static int Wrap(int v, int min, int max) => v < min ? max : v > max ? min : v;
    }

    // Forwards pointer hover/click on a menu row.
    public class MenuPointer : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public Action onEnter;
        public Action<bool> onClick;

        public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
        public void OnPointerClick(PointerEventData e) => onClick?.Invoke(e.button != PointerEventData.InputButton.Right);
    }
}
