using System;
using UnityEngine;
using UnityEngine.UI;
using WildCardPoker.Core;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Dealer's choice: when the human deals, they call the wild cards by typing a call in plain words
    /// ("lowest club in the hole and all like it") or tapping a preset. The parsed rule is previewed live.
    /// </summary>
    public sealed class WildCallPanel
    {
        private readonly RectTransform _root;
        private readonly InputField _input;
        private readonly Text _preview;
        private readonly Button _deal;
        private readonly WildCallParser _parser = new WildCallParser();
        private WildCall _current;

        public event Action<WildCall> Confirmed;

        public WildCallPanel(Transform parent)
        {
            _root = Ui.Rect("Wild Call Panel", parent);
            _root.Stretch();

            var dim = Ui.Image("Dim", _root, null, new Color(0, 0, 0, 0.6f));
            dim.rectTransform.Stretch();
            dim.raycastTarget = true;

            var panel = Ui.Image("Panel", _root, Theme.Panel, new Color(0.08f, 0.09f, 0.11f, 0.97f), sliced: true).rectTransform;
            panel.Place(new Vector2(0, 20), new Vector2(980, 640));

            var title = Ui.Text("Title", panel, "You're the dealer — call your wild cards", 38, Theme.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 270), new Vector2(940, 60));
            var sub = Ui.Text("Sub", panel, "Deuces and treys are always wild in Game 23. Type your own call or pick one below.",
                22, Theme.Muted, TextAnchor.UpperCenter);
            sub.rectTransform.Place(new Vector2(0, 220), new Vector2(940, 40));

            _input = Ui.Input("Call", panel, "e.g.  lowest heart in the hole and all like it", 26);
            ((RectTransform)_input.transform).Place(new Vector2(0, 160), new Vector2(900, 60));
            _input.onValueChanged.AddListener(OnTyped);

            _preview = Ui.Text("Preview", panel, "", 22, Theme.Text, TextAnchor.UpperLeft);
            _preview.rectTransform.Place(new Vector2(0, 90), new Vector2(900, 70));

            var presets = WildCallPresets.All();
            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                int col = i % 3, row = i / 3;
                var b = Ui.Button("Preset " + i, panel, preset.Name, new Color(0.18f, 0.22f, 0.3f), () => Choose(preset), 19);
                ((RectTransform)b.transform).Place(new Vector2(-300 + col * 300, 10 - row * 64), new Vector2(288, 54));
            }

            var none = Ui.Button("None", panel, "Just deuces & treys", new Color(0.25f, 0.25f, 0.25f), () => Choose(WildCall.None), 20);
            ((RectTransform)none.transform).Place(new Vector2(-230, -255), new Vector2(300, 64));

            _deal = Ui.Button("Deal", panel, "DEAL", new Color(0.12f, 0.5f, 0.25f), Confirm, 30);
            ((RectTransform)_deal.transform).Place(new Vector2(230, -255), new Vector2(300, 64));

            Hide();
        }

        public void Show()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _input.text = "";
            Choose(WildCallPresets.MatchingLow());
        }

        public void Hide() => _root.gameObject.SetActive(false);

        private void Choose(WildCall call)
        {
            _current = call;
            _input.SetTextWithoutNotify(call.Clauses.Count == 0 ? "" : call.Name);
            _preview.text = "Called: " + (call.Clauses.Count == 0 ? "no extra wild cards" : call.Describe());
            _preview.color = Theme.Text;
            _deal.interactable = true;
        }

        private void OnTyped(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _current = WildCall.None;
                _preview.text = "Called: no extra wild cards";
                _preview.color = Theme.Text;
                _deal.interactable = true;
                return;
            }
            var result = _parser.Parse(text, text.Trim());
            _current = result.Call;
            _deal.interactable = result.Success;
            if (result.Success)
            {
                _preview.text = "Called: " + result.Call.Describe() +
                                (result.Warnings.Count > 0 ? "\n<i>Ignored: " + string.Join(" ", result.Warnings) + "</i>" : "");
                _preview.color = Theme.Text;
            }
            else
            {
                _preview.text = string.Join(" ", result.Warnings);
                _preview.color = new Color(1f, 0.55f, 0.5f);
            }
        }

        private void Confirm()
        {
            if (_current == null) return;
            Hide();
            Confirmed?.Invoke(_current);
        }
    }
}
