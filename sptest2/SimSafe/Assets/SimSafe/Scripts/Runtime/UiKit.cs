using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SimSafe
{
    /// <summary>Small helpers for building uGUI in code, plus the shared colour palette.</summary>
    public static class UIK
    {
        public static readonly Color Panel = new Color(0.06f, 0.10f, 0.17f, 0.95f);
        public static readonly Color PanelSolid = new Color(0.06f, 0.10f, 0.17f, 1f);
        public static readonly Color Dim = new Color(0.02f, 0.04f, 0.07f, 0.72f);
        public static readonly Color Accent = new Color(0.18f, 0.77f, 0.71f);
        public static readonly Color Text = new Color(0.95f, 0.97f, 0.98f);
        public static readonly Color Muted = new Color(0.66f, 0.74f, 0.82f);
        public static readonly Color Good = new Color(0.30f, 0.76f, 0.54f);
        public static readonly Color Warn = new Color(0.95f, 0.72f, 0.29f);
        public static readonly Color Bad = new Color(0.93f, 0.42f, 0.40f);
        public static readonly Color ButtonBg = new Color(0.14f, 0.21f, 0.31f, 1f);

        public static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }
        public static string Tint(string text, Color c) { return "<color=#" + Hex(c) + ">" + text + "</color>"; }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform r, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
        }

        public static Image Box(Transform parent, string name, Color c)
        {
            var r = Rect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var r = Rect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Gfx.BuiltinFont; t.text = text; t.fontSize = size; t.color = color;
            t.fontStyle = style; t.alignment = anchor; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.lineSpacing = 1.1f;
            return t;
        }

        public static VerticalLayoutGroup VStack(GameObject go, int padH, int padV, float spacing)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padH, padH, padV, padV);
            v.spacing = spacing;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        public static LayoutElement Size(GameObject go, float minW = -1, float minH = -1, float prefW = -1, float prefH = -1, float flexW = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minW >= 0) le.minWidth = minW;
            if (minH >= 0) le.minHeight = minH;
            if (prefW >= 0) le.preferredWidth = prefW;
            if (prefH >= 0) le.preferredHeight = prefH;
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        /// <summary>Button whose height grows with its text. Keyboard focus is shown with the Selected colour.</summary>
        public static Button MakeButton(Transform parent, string label, UnityAction onClick, int size = 28, bool primary = false, float minHeight = 58)
        {
            var r = Rect("Button", parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            Color baseC = primary ? new Color(0.12f, 0.50f, 0.48f) : ButtonBg;
            cb.normalColor = baseC;
            cb.highlightedColor = Color.Lerp(baseC, Color.white, 0.18f);
            cb.pressedColor = Color.Lerp(baseC, Color.black, 0.25f);
            cb.selectedColor = new Color(0.20f, 0.52f, 0.60f);
            cb.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            var nav = btn.navigation; nav.mode = Navigation.Mode.Automatic; btn.navigation = nav;
            VStack(r.gameObject, 24, 14, 0);
            var t = Label(r, label, size, Text, primary ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleLeft);
            Size(r.gameObject, minH: minHeight);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }
    }
}
