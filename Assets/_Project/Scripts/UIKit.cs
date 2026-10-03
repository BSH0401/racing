using UnityEngine;
using UnityEngine.UI;

namespace Racing
{
    // Small helpers for building uGUI screens from code (reference resolution 1920x1080).
    public static class UIKit
    {
        static Font font;
        public static Font Font => font ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas Canvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        public static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            return go;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static GameObject Panel(string name, Transform parent, Color color)
        {
            var go = Group(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        // Rectangle placed by an anchor/pivot point (e.g. (0,1) = top-left) at pos with the given size.
        public static Image Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box, bool shadow = true)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            if (shadow)
            {
                var s = go.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.8f);
                s.effectDistance = new Vector2(2f, -2f);
            }
            return text;
        }

        public static string Ordinal(int n)
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

        public static string FormatLap(float t) => t <= 0f ? "-:--.---" : FormatTime(t);
    }
}
