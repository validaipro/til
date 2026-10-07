using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WildCardPoker.Core;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// One card on the table. Draws a classic face (corner indices + large center pip) or the back,
    /// glows gold when wild, and animates dealing, flipping and lifting.
    /// </summary>
    public sealed class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Image _glow;
        private Image _face;
        private Image _back;
        private Text _cornerTop;
        private Text _cornerBottom;
        private Text _center;
        private Text _wildTag;
        private bool _hoverLift;
        private Vector2 _restPosition;

        public Card Card { get; private set; }
        public bool FaceUp { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        /// <summary>Raised when the player clicks this card (used to choose a roll).</summary>
        public event Action<CardView> Clicked;

        public bool Selectable { get; set; }

        public static CardView Create(Transform parent, Card card, Vector2 size, bool faceUp)
        {
            var rt = Ui.Rect("Card " + card, parent);
            rt.sizeDelta = size;
            var view = rt.gameObject.AddComponent<CardView>();
            view.Build(size);
            view.Card = card;
            view.SetFaceUp(faceUp);
            return view;
        }

        private void Build(Vector2 size)
        {
            float s = size.y / 118f; // design scale

            _glow = Ui.Image("Glow", transform, Theme.Panel, Theme.WildGlow, sliced: true);
            _glow.rectTransform.Stretch(-7 * s);
            _glow.enabled = false;

            var shadow = Ui.Image("Shadow", transform, Theme.CardFace, new Color(0, 0, 0, 0.35f), sliced: true);
            shadow.rectTransform.Stretch();
            shadow.rectTransform.anchoredPosition = new Vector2(2 * s, -3 * s);

            _face = Ui.Image("Face", transform, Theme.CardFace, new Color(0.99f, 0.98f, 0.95f), sliced: true);
            _face.rectTransform.Stretch();
            _face.raycastTarget = true;

            _cornerTop = Ui.Text("Index", _face.transform, "", Mathf.RoundToInt(24 * s), Theme.CardBlack, TextAnchor.UpperLeft, FontStyle.Bold);
            _cornerTop.rectTransform.Stretch(6 * s);
            _cornerTop.lineSpacing = 0.8f;

            _cornerBottom = Ui.Text("Index2", _face.transform, "", Mathf.RoundToInt(24 * s), Theme.CardBlack, TextAnchor.UpperLeft, FontStyle.Bold);
            _cornerBottom.rectTransform.Stretch(6 * s);
            _cornerBottom.lineSpacing = 0.8f;
            _cornerBottom.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);

            _center = Ui.Text("Pip", _face.transform, "", Mathf.RoundToInt(56 * s), Theme.CardBlack);
            _center.rectTransform.Stretch();

            _wildTag = Ui.Text("Wild", _face.transform, "WILD", Mathf.RoundToInt(15 * s), new Color(0.55f, 0.35f, 0f), TextAnchor.LowerCenter, FontStyle.Bold);
            _wildTag.rectTransform.Stretch(4 * s);
            _wildTag.enabled = false;

            _back = Ui.Image("Back", transform, Theme.CardBack, Color.white);
            _back.rectTransform.Stretch();
            _back.raycastTarget = true;
        }

        public void SetCard(Card card)
        {
            Card = card;
            Refresh();
        }

        public void SetFaceUp(bool faceUp)
        {
            FaceUp = faceUp;
            Refresh();
        }

        public void SetWild(bool wild)
        {
            _glow.enabled = wild && FaceUp;
            _wildTag.enabled = wild && FaceUp;
        }

        /// <summary>Dim a card that is not part of the winning five.</summary>
        public void SetDimmed(bool dimmed)
        {
            var color = dimmed ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.99f, 0.98f, 0.95f);
            _face.color = color;
        }

        private void Refresh()
        {
            _face.gameObject.SetActive(FaceUp);
            _back.gameObject.SetActive(!FaceUp);
            if (!FaceUp)
            {
                _glow.enabled = false;
                return;
            }
            var color = Card.IsRed ? Theme.CardRed : Theme.CardBlack;
            string index = Card.RankSymbol(Card.Rank) + "\n" + Card.SuitSymbol(Card.Suit);
            _cornerTop.text = index;
            _cornerBottom.text = index;
            _center.text = Card.SuitSymbol(Card.Suit);
            _cornerTop.color = _cornerBottom.color = _center.color = color;
        }

        public IEnumerator MoveTo(Vector2 target, float duration, float rotation = 0f)
        {
            Vector2 start = Rect.anchoredPosition;
            float startRot = Rect.localEulerAngles.z;
            if (startRot > 180) startRot -= 360;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float k = EaseOutCubic(t / duration);
                Rect.anchoredPosition = Vector2.LerpUnclamped(start, target, k);
                Rect.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(startRot, rotation, k));
                yield return null;
            }
            Rect.anchoredPosition = target;
            Rect.localRotation = Quaternion.Euler(0, 0, rotation);
            _restPosition = target;
        }

        public IEnumerator Flip(bool faceUp, float duration = 0.25f)
        {
            if (FaceUp == faceUp) yield break;
            float half = duration / 2f;
            for (float t = 0; t < half; t += Time.deltaTime)
            {
                transform.localScale = new Vector3(Mathf.Lerp(1, 0, t / half), 1 + 0.08f * (t / half), 1);
                yield return null;
            }
            SetFaceUp(faceUp);
            for (float t = 0; t < half; t += Time.deltaTime)
            {
                transform.localScale = new Vector3(Mathf.Lerp(0, 1, t / half), 1.08f - 0.08f * (t / half), 1);
                yield return null;
            }
            transform.localScale = Vector3.one;
        }

        public void SnapTo(Vector2 position)
        {
            Rect.anchoredPosition = position;
            _restPosition = position;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Selectable) Clicked?.Invoke(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Selectable || _hoverLift) return;
            _hoverLift = true;
            _restPosition = Rect.anchoredPosition;
            Rect.anchoredPosition = _restPosition + new Vector2(0, 18);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_hoverLift) return;
            _hoverLift = false;
            Rect.anchoredPosition = _restPosition;
        }

        private static float EaseOutCubic(float x) => 1 - Mathf.Pow(1 - Mathf.Clamp01(x), 3);
    }
}
