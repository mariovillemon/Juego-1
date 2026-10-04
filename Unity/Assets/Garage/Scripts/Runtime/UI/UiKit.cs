using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>
    /// Small uGUI factory: every screen of the game is built in code from these helpers so there are no prefabs
    /// or scene-authored UI to maintain without the editor. Uses TextMeshPro's default font asset.
    /// </summary>
    public static class UiKit
    {
        /// <summary>Overlay canvas with scaler and (once) an Input System event system.</summary>
        public static Canvas Canvas(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortOrder;
            CanvasScaler s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return c;
        }

        /// <summary>Creates the event system if the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>Empty rect child.</summary>
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors a rect to a normalised region with a pixel inset.</summary>
        public static RectTransform Anchor(RectTransform r, Vector2 min, Vector2 max, float inset = 0)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        /// <summary>Stretches to the parent.</summary>
        public static RectTransform Fill(RectTransform r, float inset = 0) => Anchor(r, Vector2.zero, Vector2.one, inset);

        /// <summary>Coloured image panel.</summary>
        public static Image Panel(Transform parent, string name, Color color)
        {
            RectTransform r = Rect(parent, name);
            Image img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>Text label.</summary>
        public static TextMeshProUGUI Label(Transform parent, string text, float size = UiTheme.FontBody, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform r = Rect(parent, "Label");
            TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color ?? UiTheme.Text;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Button with a label.</summary>
        public static Button Button(Transform parent, string text, UnityAction onClick, Color? color = null, float width = 0)
        {
            Image img = Panel(parent, "Button_" + text, color ?? UiTheme.Button);
            Button b = img.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            b.colors = cb;
            b.targetGraphic = img;
            if (onClick != null)
            {
                b.onClick.AddListener(onClick);
            }

            TextMeshProUGUI t = Label(img.transform, text, UiTheme.FontBody, UiTheme.Text, TextAlignmentOptions.Center);
            Fill(t.rectTransform, 4);
            LayoutElement le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = UiTheme.RowHeight;
            le.preferredHeight = UiTheme.RowHeight;
            if (width > 0)
            {
                le.preferredWidth = width;
                le.minWidth = width;
            }
            else
            {
                le.flexibleWidth = 1;
            }

            return b;
        }

        /// <summary>Changes a button caption.</summary>
        public static void SetText(Button b, string text)
        {
            TextMeshProUGUI t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null)
            {
                t.text = text;
            }
        }

        /// <summary>Vertical layout container.</summary>
        public static RectTransform Column(Transform parent, string name, float spacing = 6, bool expandHeight = false)
        {
            RectTransform r = Rect(parent, name);
            VerticalLayoutGroup v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = expandHeight;
            return r;
        }

        /// <summary>Horizontal layout row with a fixed height.</summary>
        public static RectTransform Row(Transform parent, string name, float height = UiTheme.RowHeight, float spacing = 6)
        {
            RectTransform r = Rect(parent, name);
            HorizontalLayoutGroup h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            LayoutElement le = r.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return r;
        }

        /// <summary>Sets layout sizing on any element.</summary>
        public static LayoutElement Size(Component c, float preferredHeight = -1, float preferredWidth = -1, float flexibleWidth = -1, float flexibleHeight = -1)
        {
            LayoutElement le = c.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = c.gameObject.AddComponent<LayoutElement>();
            }

            if (preferredHeight >= 0)
            {
                le.preferredHeight = preferredHeight;
                le.minHeight = preferredHeight;
            }

            if (preferredWidth >= 0)
            {
                le.preferredWidth = preferredWidth;
                le.minWidth = preferredWidth;
            }

            if (flexibleWidth >= 0)
            {
                le.flexibleWidth = flexibleWidth;
            }

            if (flexibleHeight >= 0)
            {
                le.flexibleHeight = flexibleHeight;
            }

            return le;
        }

        /// <summary>Scroll view; returns the content transform (vertical layout, grows with children).</summary>
        public static RectTransform Scroll(Transform parent, string name)
        {
            Image bg = Panel(parent, name, UiTheme.PanelAlt);
            ScrollRect sr = bg.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = 30;
            RectTransform viewport = Fill(Rect(bg.transform, "Viewport"), 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Column(viewport, "Content", 4);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            ContentSizeFitter f = content.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport;
            sr.content = content;
            Size(bg, flexibleHeight: 1, flexibleWidth: 1);
            return content;
        }

        /// <summary>Destroys all children.</summary>
        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
            }
        }

        /// <summary>Single-line text input (numbers or text).</summary>
        public static TMP_InputField Input(Transform parent, string initial, bool numeric, float width = 140)
        {
            Image bg = Panel(parent, "Input", UiTheme.Header);
            RectTransform area = Fill(Rect(bg.transform, "TextArea"), 6);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = Label(area, "", UiTheme.FontBody);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Fill(text.rectTransform);
            TextMeshProUGUI placeholder = Label(area, numeric ? "0" : "…", UiTheme.FontBody, UiTheme.TextDim);
            Fill(placeholder.rectTransform);
            TMP_InputField f = bg.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.placeholder = placeholder;
            f.contentType = numeric ? TMP_InputField.ContentType.DecimalNumber : TMP_InputField.ContentType.Standard;
            f.text = initial;
            Size(bg, UiTheme.RowHeight, width);
            return f;
        }

        /// <summary>Parses a decimal typed with comma or dot.</summary>
        public static double ParseNumber(string s, double fallback = 0)
        {
            return double.TryParse((s ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        /// <summary>Simple full-width row with a label on the left and a value on the right.</summary>
        public static TextMeshProUGUI KeyValue(Transform parent, string key, string value)
        {
            RectTransform row = Row(parent, "KV_" + key, 28);
            TextMeshProUGUI k = Label(row, key, UiTheme.FontSmall, UiTheme.TextDim);
            Size(k, preferredWidth: 220);
            TextMeshProUGUI v = Label(row, value, UiTheme.FontBody);
            Size(v, flexibleWidth: 1);
            return v;
        }

        /// <summary>Runs an action safely and logs exceptions (UI callbacks must never break the frame).</summary>
        public static UnityAction Safe(Action a) => () =>
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        };
    }
}
