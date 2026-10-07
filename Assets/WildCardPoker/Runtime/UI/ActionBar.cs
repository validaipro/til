using System;
using UnityEngine;
using UnityEngine.UI;
using WildCardPoker.Core;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// The human player's controls. FOLD stays enabled whenever the player is in the hand, so they can fold out of
    /// turn like at a live table. Pre-action boxes (Check/Fold, Call any) work like the big online apps.
    /// </summary>
    public sealed class ActionBar
    {
        private readonly RectTransform _root;
        private readonly Button _fold;
        private readonly Button _call;
        private readonly Button _raise;
        private readonly Slider _slider;
        private readonly Text _amount;
        private readonly Text _hint;
        private readonly Toggle _checkFold;
        private readonly Toggle _callAny;
        private readonly Button _halfPot;
        private readonly Button _pot;
        private readonly Button _allIn;
        private LegalActions _legal;
        private long _raiseTo;

        public event Action FoldPressed;
        public event Action<PlayerAction> ActionChosen;

        public PreAction PreAction
        {
            get
            {
                if (_checkFold.isOn) return PreAction.CheckFold;
                if (_callAny.isOn) return PreAction.CallAny;
                return PreAction.None;
            }
        }

        public ActionBar(Transform parent)
        {
            _root = Ui.Rect("Action Bar", parent);
            _root.anchorMin = _root.anchorMax = new Vector2(1, 0);
            _root.pivot = new Vector2(1, 0);
            _root.anchoredPosition = new Vector2(-24, 24);
            _root.sizeDelta = new Vector2(620, 300);

            var bg = Ui.Image("Bg", _root, Theme.Panel, Theme.PanelBg, sliced: true);
            bg.rectTransform.Stretch();

            _hint = Ui.Text("Hint", _root, "", 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Italic);
            _hint.rectTransform.Stretch(14);

            _fold = Ui.Button("Fold", _root, "FOLD", Theme.ButtonFold, () => FoldPressed?.Invoke());
            ((RectTransform)_fold.transform).Place(new Vector2(-205, -60), new Vector2(180, 70));

            _call = Ui.Button("Call", _root, "CHECK", Theme.ButtonCall, OnCall);
            ((RectTransform)_call.transform).Place(new Vector2(0, -60), new Vector2(180, 70));

            _raise = Ui.Button("Raise", _root, "RAISE", Theme.ButtonRaise, OnRaise);
            ((RectTransform)_raise.transform).Place(new Vector2(205, -60), new Vector2(180, 70));

            _slider = Ui.Slider("Amount", _root, OnSlider);
            ((RectTransform)_slider.transform).Place(new Vector2(-40, 10), new Vector2(420, 40));
            _amount = Ui.Text("AmountText", _root, "", 26, Theme.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            ((RectTransform)_amount.transform).Place(new Vector2(240, 10), new Vector2(120, 40));

            _halfPot = Ui.Button("HalfPot", _root, "½ POT", new Color(0.25f, 0.25f, 0.3f), () => SetRaiseFraction(0.5f), 18);
            ((RectTransform)_halfPot.transform).Place(new Vector2(-195, 60), new Vector2(100, 36));
            _pot = Ui.Button("Pot", _root, "POT", new Color(0.25f, 0.25f, 0.3f), () => SetRaiseFraction(1f), 18);
            ((RectTransform)_pot.transform).Place(new Vector2(-85, 60), new Vector2(100, 36));
            _allIn = Ui.Button("AllIn", _root, "ALL-IN", new Color(0.45f, 0.1f, 0.1f), () => SetRaiseTo(long.MaxValue), 18);
            ((RectTransform)_allIn.transform).Place(new Vector2(25, 60), new Vector2(100, 36));

            _checkFold = Ui.Toggle("CheckFold", _root, "Check/Fold", on => { if (on) _callAny.isOn = false; });
            ((RectTransform)_checkFold.transform).Place(new Vector2(-200, -122), new Vector2(180, 32));
            _callAny = Ui.Toggle("CallAny", _root, "Call any", on => { if (on) _checkFold.isOn = false; });
            ((RectTransform)_callAny.transform).Place(new Vector2(-10, -122), new Vector2(160, 32));

            ShowIdle(false, "");
        }

        /// <summary>Not the player's turn. Fold stays live (out-of-turn fold) while they are in the hand.</summary>
        public void ShowIdle(bool inHand, string hint)
        {
            _legal = null;
            _hint.text = hint;
            _fold.interactable = inHand;
            _fold.SetLabel(inHand ? "FOLD" : "FOLDED");
            SetBetControls(false);
            _checkFold.interactable = _callAny.interactable = inHand;
        }

        public void ShowTurn(LegalActions legal)
        {
            _legal = legal;
            _hint.text = "Your action.";
            _fold.interactable = true;
            _fold.SetLabel("FOLD");
            _call.interactable = true;
            _call.SetLabel(legal.CanCheck ? "CHECK" : legal.CallIsAllIn ? $"ALL-IN {legal.ToCall}" : $"CALL {legal.ToCall}");

            bool canRaise = legal.CanBetOrRaise;
            _raise.interactable = canRaise;
            _slider.interactable = _halfPot.interactable = _pot.interactable = _allIn.interactable = canRaise;
            if (canRaise)
            {
                _slider.minValue = 0;
                _slider.maxValue = 1;
                _slider.SetValueWithoutNotify(0);
                SetRaiseTo(legal.MinRaiseTo);
            }
            else
            {
                _raise.SetLabel(legal.IsOpeningBet ? "BET" : "RAISE");
                _amount.text = "";
            }
        }

        public void ClearPreActions()
        {
            _checkFold.SetIsOnWithoutNotify(false);
            _callAny.SetIsOnWithoutNotify(false);
        }

        private void SetBetControls(bool on)
        {
            _call.interactable = on;
            _raise.interactable = on;
            _slider.interactable = _halfPot.interactable = _pot.interactable = _allIn.interactable = on;
            _amount.text = "";
        }

        private void OnCall()
        {
            if (_legal == null) return;
            ActionChosen?.Invoke(_legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call());
        }

        private void OnRaise()
        {
            if (_legal == null || !_legal.CanBetOrRaise) return;
            ActionChosen?.Invoke(_legal.IsOpeningBet ? PlayerAction.BetTo(_raiseTo) : PlayerAction.RaiseTo(_raiseTo));
        }

        private void OnSlider(float value)
        {
            if (_legal == null || !_legal.CanBetOrRaise) return;
            // Quadratic curve gives finer control over small bets.
            long span = _legal.MaxRaiseTo - _legal.MinRaiseTo;
            SetRaiseTo(_legal.MinRaiseTo + (long)Math.Round(span * value * value), updateSlider: false);
        }

        private void SetRaiseFraction(float potFraction)
        {
            if (_legal == null) return;
            long target = _legal.CurrentBet + (long)Math.Round((_legal.Pot + _legal.ToCall) * potFraction);
            SetRaiseTo(target);
        }

        private void SetRaiseTo(long target, bool updateSlider = true)
        {
            if (_legal == null || !_legal.CanBetOrRaise) return;
            _raiseTo = Math.Max(_legal.MinRaiseTo, Math.Min(_legal.MaxRaiseTo, target));
            _amount.text = _raiseTo.ToString("N0");
            bool allIn = _raiseTo == _legal.RoundBet + _legal.Stack;
            _raise.SetLabel(allIn ? "ALL-IN" : (_legal.IsOpeningBet ? "BET" : "RAISE TO"));
            if (updateSlider)
            {
                long span = _legal.MaxRaiseTo - _legal.MinRaiseTo;
                float v = span <= 0 ? 0 : Mathf.Sqrt((_raiseTo - _legal.MinRaiseTo) / (float)span);
                _slider.SetValueWithoutNotify(v);
            }
        }
    }
}
