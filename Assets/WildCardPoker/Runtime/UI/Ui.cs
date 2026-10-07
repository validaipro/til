using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WildCardPoker.Unity
{
    /// <summary>Small helpers that build uGUI elements from code, so the table needs no prefabs or scene setup.</summary>
    public static class Ui
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchoredPosition, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color, bool sliced = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(string name, Transform parent, string value, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Theme.Font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Text Shadowed(this Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        public static Button Button(string name, Transform parent, string label, Color color, UnityAction onClick, int fontSize = 26)
        {
            var img = Image(name, parent, Theme.Panel, color, sliced: true);
            img.raycastTarget = true;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(onClick);
            var text = Text("Label", img.transform, label, fontSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.Stretch(4);
            text.Shadowed();
            return button;
        }

        public static void SetLabel(this Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        public static Slider Slider(string name, Transform parent, Action<float> onChanged)
        {
            var root = Rect(name, parent);
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = Image("Background", root, Theme.Panel, new Color(0, 0, 0, 0.5f), sliced: true);
            bg.rectTransform.anchorMin = new Vector2(0, 0.35f);
            bg.rectTransform.anchorMax = new Vector2(1, 0.65f);
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = Rect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0, 0.35f);
            fillArea.anchorMax = new Vector2(1, 0.65f);
            fillArea.offsetMin = new Vector2(8, 0);
            fillArea.offsetMax = new Vector2(-8, 0);
            var fill = Image("Fill", fillArea, Theme.Panel, Theme.ButtonRaise, sliced: true);
            fill.rectTransform.Stretch();

            var handleArea = Rect("Handle Slide Area", root);
            handleArea.Stretch();
            handleArea.offsetMin = new Vector2(10, 0);
            handleArea.offsetMax = new Vector2(-10, 0);
            var handle = Image("Handle", handleArea, Theme.Circle, Theme.Gold);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(30, 0);
            handle.rectTransform.anchorMin = new Vector2(0, 0);
            handle.rectTransform.anchorMax = new Vector2(0, 1);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        public static InputField Input(string name, Transform parent, string placeholder, int fontSize = 24)
        {
            var bg = Image(name, parent, Theme.Panel, new Color(1, 1, 1, 0.95f), sliced: true);
            bg.raycastTarget = true;
            var input = bg.gameObject.AddComponent<InputField>();
            var text = Text("Text", bg.transform, "", fontSize, Theme.CardBlack, TextAnchor.MiddleLeft);
            text.rectTransform.Stretch(12);
            text.supportRichText = false;
            var hint = Text("Placeholder", bg.transform, placeholder, fontSize, new Color(0.4f, 0.4f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Italic);
            hint.rectTransform.Stretch(12);
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = bg;
            return input;
        }

        public static Toggle Toggle(string name, Transform parent, string label, Action<bool> onChanged)
        {
            var root = Rect(name, parent);
            var toggle = root.gameObject.AddComponent<Toggle>();
            var box = Image("Box", root, Theme.Panel, new Color(1, 1, 1, 0.9f), sliced: true);
            box.raycastTarget = true;
            box.rectTransform.anchorMin = new Vector2(0, 0.5f);
            box.rectTransform.anchorMax = new Vector2(0, 0.5f);
            box.rectTransform.pivot = new Vector2(0, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(30, 30);
            box.rectTransform.anchoredPosition = Vector2.zero;
            var check = Image("Check", box.transform, Theme.Panel, Theme.ButtonCall, sliced: true);
            check.rectTransform.Stretch(5);
            var text = Text("Label", root, label, 20, Theme.Text, TextAnchor.MiddleLeft);
            text.rectTransform.Stretch();
            text.rectTransform.offsetMin = new Vector2(38, 0);
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = false;
            if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }
    }
}
