using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WildCardPoker.Core;

namespace WildCardPoker.Unity
{
    /// <summary>Where a seat's nameplate and its two card rows go (canvas coordinates, centers).</summary>
    public struct SeatLayout
    {
        public Vector2 Plate;
        public Vector2 HoleRow;
        public Vector2 UpRow;

        public SeatLayout(Vector2 plate, Vector2 holeRow, Vector2 upRow)
        {
            Plate = plate;
            HoleRow = holeRow;
            UpRow = upRow;
        }
    }

    /// <summary>
    /// A player's spot at the table: nameplate with avatar, stack, dealer button, turn timer, speech bubble,
    /// and two card rows (hole cards behind, rolled cards in front, toward the center).
    /// </summary>
    public sealed class SeatView : MonoBehaviour
    {
        private Text _name;
        private Text _stack;
        private Text _status;
        private Image _plate;
        private Image _avatar;
        private Text _avatarInitial;
        private Image _dealerButton;
        private Image _timer;
        private RectTransform _bubble;
        private Text _bubbleText;
        private Coroutine _bubbleRoutine;

        public int SeatIndex { get; private set; }
        public bool IsHuman { get; private set; }
        public SeatLayout Layout { get; private set; }

        public Vector2 CardSize { get; private set; }
        public List<CardView> HoleCards { get; } = new List<CardView>();
        public List<CardView> UpCards { get; } = new List<CardView>();

        public static SeatView Create(Transform parent, int seatIndex, string name, bool isHuman, SeatLayout layout, Color avatarColor)
        {
            var rt = Ui.Rect("Seat " + seatIndex, parent);
            rt.Place(layout.Plate, new Vector2(260, 92));
            var view = rt.gameObject.AddComponent<SeatView>();
            view.SeatIndex = seatIndex;
            view.IsHuman = isHuman;
            view.Layout = layout;
            view.CardSize = isHuman ? new Vector2(84, 118) : new Vector2(60, 84);
            view.Build(name, avatarColor);
            return view;
        }

        private void Build(string playerName, Color avatarColor)
        {
            _timer = Ui.Image("Timer", transform, Theme.Panel, Theme.Gold, sliced: true);
            _timer.rectTransform.Stretch(-5);
            _timer.enabled = false;

            _plate = Ui.Image("Plate", transform, Theme.Panel, new Color(0.05f, 0.05f, 0.07f, 0.85f), sliced: true);
            _plate.rectTransform.Stretch();

            _avatar = Ui.Image("Avatar", transform, Theme.Circle, avatarColor);
            _avatar.rectTransform.anchorMin = _avatar.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _avatar.rectTransform.sizeDelta = new Vector2(76, 76);
            _avatar.rectTransform.anchoredPosition = new Vector2(46, 0);
            _avatarInitial = Ui.Text("Initial", _avatar.transform, playerName.Substring(0, 1), 38, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _avatarInitial.rectTransform.Stretch();
            _avatarInitial.Shadowed();

            _name = Ui.Text("Name", transform, playerName, 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            _name.rectTransform.Stretch();
            _name.rectTransform.offsetMin = new Vector2(92, 8);
            _name.rectTransform.offsetMax = new Vector2(-8, -10);
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;

            _stack = Ui.Text("Stack", transform, "", 24, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            _stack.rectTransform.Stretch();
            _stack.rectTransform.offsetMin = new Vector2(92, 0);
            _stack.rectTransform.offsetMax = new Vector2(-8, -16);

            _status = Ui.Text("Status", transform, "", 18, Theme.Muted, TextAnchor.LowerLeft, FontStyle.Italic);
            _status.rectTransform.Stretch();
            _status.rectTransform.offsetMin = new Vector2(92, 8);
            _status.rectTransform.offsetMax = new Vector2(-8, -8);

            _dealerButton = Ui.Image("Dealer", transform, Theme.Circle, Color.white);
            _dealerButton.rectTransform.Place(new Vector2(118, 46), new Vector2(34, 34));
            var d = Ui.Text("D", _dealerButton.transform, "D", 20, Theme.CardBlack, TextAnchor.MiddleCenter, FontStyle.Bold);
            d.rectTransform.Stretch();
            _dealerButton.enabled = false;
            d.enabled = false;

            _bubble = Ui.Image("Bubble", transform, Theme.Panel, new Color(1, 1, 1, 0.95f), sliced: true).rectTransform;
            _bubble.Place(new Vector2(0, 86), new Vector2(250, 48));
            _bubbleText = Ui.Text("Say", _bubble, "", 19, Theme.CardBlack, TextAnchor.MiddleCenter, FontStyle.Bold);
            _bubbleText.rectTransform.Stretch(6);
            _bubble.gameObject.SetActive(false);
        }

        public void SetStack(long stack) => _stack.text = stack.ToString("N0");

        public void SetStatus(string text) => _status.text = text ?? "";

        public void SetDealer(bool isDealer)
        {
            _dealerButton.enabled = isDealer;
            _dealerButton.GetComponentInChildren<Text>().enabled = isDealer;
        }

        public void SetActiveTurn(bool active) => _timer.enabled = active;

        public void SetTimer(float fraction)
        {
            _timer.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), Theme.Gold, fraction);
        }

        public void SetFolded(bool folded)
        {
            _plate.color = folded ? new Color(0.05f, 0.05f, 0.07f, 0.45f) : new Color(0.05f, 0.05f, 0.07f, 0.85f);
            foreach (var c in HoleCards) c.gameObject.SetActive(!folded);
            foreach (var c in UpCards) c.gameObject.SetActive(!folded);
        }

        /// <summary>Speech bubble for actions, table talk and dealer warnings.</summary>
        public void Say(string text, float seconds = 2.2f, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_bubbleRoutine != null) StopCoroutine(_bubbleRoutine);
            _bubbleRoutine = StartCoroutine(ShowBubble(text, seconds, color ?? Theme.CardBlack));
        }

        private IEnumerator ShowBubble(string text, float seconds, Color color)
        {
            _bubbleText.text = text;
            _bubbleText.color = color;
            _bubble.gameObject.SetActive(true);
            _bubble.localScale = Vector3.one * 0.6f;
            for (float t = 0; t < 0.12f; t += Time.deltaTime)
            {
                _bubble.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, t / 0.12f);
                yield return null;
            }
            _bubble.localScale = Vector3.one;
            yield return new WaitForSeconds(seconds);
            _bubble.gameObject.SetActive(false);
        }

        /// <summary>Table-space position of hole card <paramref name="index"/>.</summary>
        public Vector2 HoleSlot(int index, int count)
        {
            float spacing = CardSize.x * (IsHuman ? 1.05f : 0.55f);
            return Layout.HoleRow + new Vector2((index - (count - 1) / 2f) * spacing, 0);
        }

        /// <summary>Table-space position of rolled card <paramref name="index"/> (four rolls per hand).</summary>
        public Vector2 UpSlot(int index, int count)
        {
            float spacing = CardSize.x * 0.78f;
            return Layout.UpRow + new Vector2((index - 1.5f) * spacing, 0);
        }

        public void ClearCards()
        {
            foreach (var c in HoleCards) Destroy(c.gameObject);
            foreach (var c in UpCards) Destroy(c.gameObject);
            HoleCards.Clear();
            UpCards.Clear();
        }
    }
}
