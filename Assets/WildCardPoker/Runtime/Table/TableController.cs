using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WildCardPoker.Core;
using WildCardPoker.Core.AI;
using WildCardPoker.Core.Game23;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Runs a Game 23 table: one human (seat 0) against computer players.
    /// The rules live in <see cref="Game23Engine"/>; this class turns its events into animation and feeds it input.
    /// The human can press FOLD at any moment, including out of turn.
    /// </summary>
    public sealed class TableController : MonoBehaviour
    {
        private const int Human = 0;

        [Header("Table")]
        [Range(2, 5)] public int playerCount = 5;
        public long startingStack = 1000;
        public string humanName = "You";

        [Header("Rules")]
        public BetStructure structure = BetStructure.NoLimit;
        public long ante = 5;
        public long smallBet = 10;
        public long bigBet = 20;
        public OutOfTurnFoldPolicy outOfTurnFolds = OutOfTurnFoldPolicy.AllowedWithWarning;
        public bool naturalBeatsWild;

        [Header("Feel")]
        [Range(0.25f, 3f)] public float animationSpeed = 1f;
        public float humanTurnSeconds = 30f;
        [Range(10, 200)] public int botSimulationSamples = 60;

        private Game23Engine _engine;
        private List<Seat> _seats;
        private readonly Dictionary<int, Game23Bot> _bots = new Dictionary<int, Game23Bot>();
        private SeatView[] _views;
        private readonly Dictionary<(int side, int slot), CardView> _boardCards = new Dictionary<(int, int), CardView>();
        private readonly Queue<GameEvent> _queue = new Queue<GameEvent>();
        private readonly List<string> _logLines = new List<string>();
        private readonly System.Random _botRandom = new System.Random();

        private RectTransform _cardLayer;
        private Text _potText;
        private Text _wildBanner;
        private Text _logText;
        private ActionBar _bar;
        private WildCallPanel _wildPanel;
        private Vector2 _deckPosition;

        private bool _animating;
        private bool _botThinking;
        private bool _humanFoldQueued;
        private PlayerAction? _humanAction;
        private Card? _humanRoll;
        private WildCall _humanCall;
        private long _displayPot;
        private long[] _stacksAtStart;

        private float Speed => Mathf.Max(0.1f, animationSpeed);

        private void Start()
        {
            BuildScene();
            CreateGame();
            StartCoroutine(EventPump());
            StartCoroutine(GameLoop());
        }

        // ------------------------------------------------------------------ setup

        private void CreateGame()
        {
            var lineup = PlayerProfile.DefaultLineup();
            _seats = new List<Seat> { new Seat(Human, humanName, startingStack, isHuman: true) };
            for (int i = 1; i < playerCount; i++)
            {
                var profile = lineup[(i - 1) % lineup.Count];
                _seats.Add(new Seat(i, profile.Name, startingStack));
                _bots[i] = new Game23Bot(i, profile, new SeededRandom(_botRandom.Next())) { Samples = botSimulationSamples };
            }

            var settings = new Game23Settings
            {
                Structure = structure,
                Ante = ante,
                SmallBet = smallBet,
                BigBet = bigBet,
                OutOfTurnFolds = outOfTurnFolds,
                HandRules = new HandRules { NaturalBeatsWild = naturalBeatsWild },
            };
            _engine = new Game23Engine(settings, _seats, new CryptoRandom());

            var layouts = SeatLayouts(playerCount);
            var colors = new[]
            {
                new Color(0.2f, 0.45f, 0.85f), new Color(0.75f, 0.25f, 0.3f), new Color(0.85f, 0.55f, 0.15f),
                new Color(0.35f, 0.65f, 0.35f), new Color(0.55f, 0.35f, 0.75f),
            };
            _views = new SeatView[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                _views[i] = SeatView.Create(_cardLayer, i, _seats[i].Name, i == Human, layouts[i], colors[i % colors.Length]);
                _views[i].SetStack(_seats[i].Stack);
                if (_bots.TryGetValue(i, out var bot)) _views[i].SetStatus(bot.Profile.Archetype.ToString());
            }
        }

        private static SeatLayout[] SeatLayouts(int count)
        {
            SeatLayout L(float px, float py, float hx, float hy, float ux, float uy) =>
                new SeatLayout(new Vector2(px, py), new Vector2(hx, hy), new Vector2(ux, uy));

            var human = L(0, -420, -330, -420, 0, -290);
            var left = L(-740, -150, -740, -58, -490, -150);
            var right = L(740, -150, 740, -58, 490, -150);
            var topLeft = L(-560, 340, -560, 425, -560, 245);
            var topRight = L(560, 340, 560, 425, 560, 245);
            var top = L(0, 340, 0, 425, 300, 340);
            switch (count)
            {
                case 2: return new[] { human, top };
                case 3: return new[] { human, topLeft, topRight };
                case 4: return new[] { human, left, top, right };
                default: return new[] { human, left, topLeft, topRight, right };
            }
        }

        private void BuildScene()
        {
            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.03f, 0.04f);
                cam.orthographic = true;
            }
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }

            var canvasGo = new GameObject("Wild Card Poker UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            var backdrop = Ui.Image("Backdrop", root, null, new Color(0.04f, 0.04f, 0.05f));
            backdrop.rectTransform.Stretch();

            // Table: wooden rail, gold pinstripe, felt (masked to the rail's rounded shape).
            var rail = Ui.Image("Rail", root, Theme.Panel, Theme.Rail, sliced: true);
            rail.pixelsPerUnitMultiplier = 0.09f;
            rail.rectTransform.Place(new Vector2(0, 10), new Vector2(1780, 900));
            var stripe = Ui.Image("Pinstripe", root, Theme.Panel, Theme.Gold * 0.8f, sliced: true);
            stripe.pixelsPerUnitMultiplier = 0.1f;
            stripe.rectTransform.Place(new Vector2(0, 10), new Vector2(1700, 820));
            var feltMask = Ui.Image("FeltMask", root, Theme.Panel, Color.white, sliced: true);
            feltMask.pixelsPerUnitMultiplier = 0.1f;
            feltMask.rectTransform.Place(new Vector2(0, 10), new Vector2(1690, 810));
            feltMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var felt = Ui.Image("Felt", feltMask.transform, Theme.Felt, Color.white);
            felt.rectTransform.Stretch();

            foreach (int side in new[] { 2, 3 })
            {
                var mark = Ui.Text("Numeral " + side, root, side.ToString(), 420, new Color(1, 1, 1, 0.06f), TextAnchor.MiddleCenter, FontStyle.Bold);
                mark.rectTransform.Place(BoardLayout.LabelPosition(side), new Vector2(400, 520));
            }
            var title = Ui.Text("Title", root, "23", 30, new Color(1, 1, 1, 0.25f), TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 250), new Vector2(200, 40));

            _cardLayer = Ui.Rect("Table", root);
            _cardLayer.Stretch();

            _deckPosition = new Vector2(0, -120);
            var deck = Ui.Image("Deck", _cardLayer, Theme.CardBack, Color.white);
            deck.rectTransform.Place(_deckPosition, BoardLayout.CardSize);

            var potChip = Ui.Image("PotChip", _cardLayer, ProceduralArt.Chip(new Color(0.75f, 0.1f, 0.12f)), Color.white);
            potChip.rectTransform.Place(new Vector2(-46, 70), new Vector2(44, 44));
            _potText = Ui.Text("Pot", _cardLayer, "", 30, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold).Shadowed();
            _potText.rectTransform.Place(new Vector2(40, 70), new Vector2(140, 44));

            var bannerBg = Ui.Image("BannerBg", root, Theme.Panel, Theme.PanelBg, sliced: true);
            bannerBg.rectTransform.anchorMin = bannerBg.rectTransform.anchorMax = new Vector2(0.5f, 1);
            bannerBg.rectTransform.pivot = new Vector2(0.5f, 1);
            bannerBg.rectTransform.anchoredPosition = new Vector2(0, -12);
            bannerBg.rectTransform.sizeDelta = new Vector2(900, 54);
            _wildBanner = Ui.Text("WildBanner", bannerBg.transform, "", 24, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _wildBanner.rectTransform.Stretch(6);

            var logBg = Ui.Image("LogBg", root, Theme.Panel, Theme.PanelBg, sliced: true);
            logBg.rectTransform.anchorMin = logBg.rectTransform.anchorMax = Vector2.zero;
            logBg.rectTransform.pivot = Vector2.zero;
            logBg.rectTransform.anchoredPosition = new Vector2(24, 24);
            logBg.rectTransform.sizeDelta = new Vector2(440, 230);
            _logText = Ui.Text("Log", logBg.transform, "", 17, Theme.Text, TextAnchor.LowerLeft);
            _logText.rectTransform.Stretch(12);
            _logText.verticalOverflow = VerticalWrapMode.Truncate;

            _bar = new ActionBar(root);
            _bar.FoldPressed += OnHumanFold;
            _bar.ActionChosen += a => _humanAction = a;

            _wildPanel = new WildCallPanel(root);
            _wildPanel.Confirmed += call => _humanCall = call;
        }

        // ------------------------------------------------------------------ game loop

        private IEnumerator GameLoop()
        {
            yield return null;
            while (true)
            {
                foreach (var s in _seats.Where(s => s.Stack == 0))
                {
                    s.Stack = startingStack;
                    Log($"{s.Name} rebuys for {startingStack:N0}.");
                    _views[s.Index].SetStack(s.Stack);
                }

                ClearTable();
                WildCall call;
                int dealer = _engine.NextDealerSeat();
                if (dealer == Human)
                {
                    _humanCall = null;
                    _bar.ShowIdle(false, "You're dealing. Call your wild cards.");
                    _wildPanel.Show();
                    yield return new WaitUntil(() => _humanCall != null);
                    call = _humanCall;
                }
                else
                {
                    call = BotCall();
                    _views[dealer].Say(call.Clauses.Count == 0 ? "Straight deuces and treys." : call.Name + "!", 2.6f);
                    yield return new WaitForSeconds(1.2f / Speed);
                }

                _stacksAtStart = _seats.Select(s => s.Stack).ToArray();
                _engine.StartHand(call);
                yield return WaitIdle();

                while (_engine.Phase != Game23Phase.HandOver)
                {
                    var pending = _engine.Pending;
                    if (pending.Phase == Game23Phase.Rolling) yield return RollingStep(pending);
                    else if (pending.Phase == Game23Phase.Betting) yield return BettingStep(pending);
                    yield return WaitIdle();
                }

                for (int i = 0; i < _seats.Count; i++)
                    if (_bots.TryGetValue(i, out var bot))
                        bot.OnHandEnded(_seats[i].Stack - _stacksAtStart[i], _stacksAtStart[i]);

                _bar.ShowIdle(false, "Next hand coming up...");
                yield return new WaitForSeconds(4f / Speed);
            }
        }

        private WildCall BotCall()
        {
            var presets = WildCallPresets.All();
            return _botRandom.NextDouble() < 0.15 ? WildCall.None : presets[_botRandom.Next(presets.Count)];
        }

        private IEnumerator RollingStep(PendingDecision pending)
        {
            // Bots decide immediately; their choice stays hidden until everyone has chosen.
            foreach (int seat in pending.RollSeats)
            {
                if (seat == Human || _engine.Phase != Game23Phase.Rolling) continue;
                if (!_engine.Pending.RollSeats.Contains(seat)) continue;
                _engine.SubmitRoll(seat, _bots[seat].ChooseRoll(_engine));
                _views[seat].SetStatus("chose a roll");
            }

            if (_engine.Phase == Game23Phase.Rolling && _engine.Pending.RollSeats.Contains(Human))
            {
                _humanRoll = null;
                SetHumanCardsSelectable(true);
                _bar.ShowIdle(true, "Tap one of your hole cards to roll it face up.");
                yield return new WaitUntil(() => _humanRoll != null || !_seats[Human].InHand || _engine.Phase != Game23Phase.Rolling);
                SetHumanCardsSelectable(false);
                if (_humanRoll != null && _engine.Phase == Game23Phase.Rolling && _engine.Pending.RollSeats.Contains(Human))
                    _engine.SubmitRoll(Human, _humanRoll.Value);
            }
        }

        private IEnumerator BettingStep(PendingDecision pending)
        {
            int toAct = pending.ToAct;
            if (toAct < 0) yield break;
            HighlightTurn(toAct);

            // Live-table behaviour: impatient players fold before the action reaches them.
            // Their hand-strength simulation runs off the main thread like a normal bot decision.
            var others = _bots.Values.Where(b => b.SeatIndex != toAct).ToList();
            List<Game23Bot> folders;
#if UNITY_WEBGL && !UNITY_EDITOR
            folders = others.Where(b => b.WantsToFoldOutOfTurn(_engine)).ToList();
#else
            _botThinking = true;
            var check = Task.Run(() => others.Where(b => b.WantsToFoldOutOfTurn(_engine)).ToList());
            yield return new WaitUntil(() => check.IsCompleted);
            _botThinking = false;
            folders = check.IsFaulted ? new List<Game23Bot>() : check.Result;
            if (check.IsFaulted) Debug.LogException(check.Exception);
#endif
            foreach (var bot in folders)
            {
                if (_engine.Phase != Game23Phase.Betting || !_engine.Fold(bot.SeatIndex)) continue;
                var line = TableTalk.Maybe(TableTalk.Moment.OutOfTurnFold, bot.Profile, new SeededRandom(_botRandom.Next()), 0.3f);
                if (line != null) _views[bot.SeatIndex].Say(line);
                yield return WaitIdle();
            }
            if (_humanFoldQueued)
            {
                _humanFoldQueued = false;
                OnHumanFold();
                yield return WaitIdle();
            }
            if (_engine.Phase != Game23Phase.Betting || _engine.Pending.ToAct != toAct) yield break;

            var legal = _engine.Pending.Legal;
            if (toAct == Human) yield return HumanTurn(legal);
            else yield return BotTurn(toAct, legal);
            HighlightTurn(-1);
        }

        private IEnumerator HumanTurn(LegalActions legal)
        {
            var pre = PreActions.Resolve(_bar.PreAction, legal);
            _bar.ClearPreActions();
            if (pre.HasValue)
            {
                SafeAct(Human, pre.Value, legal);
                yield break;
            }

            _humanAction = null;
            _bar.ShowTurn(legal);
            float deadline = Time.time + humanTurnSeconds;
            while (_humanAction == null && _seats[Human].InHand && Time.time < deadline)
            {
                _views[Human].SetTimer((deadline - Time.time) / humanTurnSeconds);
                yield return null;
            }
            _bar.ShowIdle(_seats[Human].InHand, "");
            if (!_seats[Human].InHand || _engine.Phase != Game23Phase.Betting || _engine.Pending.ToAct != Human) yield break;
            var action = _humanAction ?? (legal.CanCheck ? PlayerAction.Check() : PlayerAction.Fold());
            SafeAct(Human, action, legal);
        }

        private IEnumerator BotTurn(int seat, LegalActions legal)
        {
            var bot = _bots[seat];
            _bar.ShowIdle(_seats[Human].InHand, $"{_seats[seat].Name} is thinking...");
            float started = Time.time;

            BotDecision decision;
#if UNITY_WEBGL && !UNITY_EDITOR
            decision = bot.DecideBet(_engine, legal);
#else
            // Simulation runs off the main thread; the engine is not modified while it runs
            // (a human fold pressed meanwhile is queued).
            _botThinking = true;
            var task = Task.Run(() => bot.DecideBet(_engine, legal));
            yield return new WaitUntil(() => task.IsCompleted);
            _botThinking = false;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                decision = new BotDecision(legal.CanCheck ? PlayerAction.Check() : PlayerAction.Fold(), 0.5f, null);
            }
            else decision = task.Result;
#endif
            if (_humanFoldQueued)
            {
                _humanFoldQueued = false;
                OnHumanFold();
                yield return WaitIdle();
            }

            float wait = decision.ThinkSeconds / Speed - (Time.time - started);
            float end = Time.time + Mathf.Max(0, wait);
            while (Time.time < end && _engine.Phase == Game23Phase.Betting && _engine.Pending.ToAct == seat)
            {
                _views[seat].SetTimer(1 - (Time.time - started) / Mathf.Max(0.01f, end - started));
                yield return null;
            }
            if (_engine.Phase != Game23Phase.Betting || _engine.Pending.ToAct != seat) yield break;
            if (decision.Chat != null) _views[seat].Say(decision.Chat, 2.4f);
            SafeAct(seat, decision.Action, legal);
        }

        /// <summary>Applies an action; if it is somehow illegal, logs it and falls back to check/fold so the table never stalls.</summary>
        private void SafeAct(int seat, PlayerAction action, LegalActions legal)
        {
            try
            {
                _engine.Act(seat, action);
            }
            catch (System.InvalidOperationException ex)
            {
                Debug.LogWarning($"Rejected {action} from seat {seat}: {ex.Message}");
                _engine.Act(seat, legal.CanCheck ? PlayerAction.Check() : PlayerAction.Fold());
            }
        }

        private void OnHumanFold()
        {
            if (_engine == null || !_seats[Human].InHand) return;
            if (_botThinking)
            {
                _humanFoldQueued = true;
                return;
            }
            bool inTurn = _engine.Phase == Game23Phase.Betting && _engine.Pending.ToAct == Human;
            if (inTurn) _humanAction = PlayerAction.Fold();
            else _engine.Fold(Human); // out of turn (or while choosing rolls): the engine applies table policy
        }

        // ------------------------------------------------------------------ event playback

        private IEnumerator EventPump()
        {
            while (true)
            {
                if (_engine != null && _engine.HasEvents)
                    foreach (var e in _engine.DrainEvents()) _queue.Enqueue(e);
                if (_queue.Count == 0)
                {
                    _animating = false;
                    yield return null;
                    continue;
                }
                _animating = true;
                yield return Animate(_queue.Dequeue());
            }
        }

        private IEnumerator WaitIdle()
        {
            yield return null;
            while (_animating || _queue.Count > 0 || _engine.HasEvents) yield return null;
        }

        private IEnumerator Animate(GameEvent e)
        {
            Log(Named(e.Narration));
            switch (e)
            {
                case HandStartedEvent hs:
                    _displayPot = 0;
                    UpdatePot();
                    for (int i = 0; i < _views.Length; i++) _views[i].SetDealer(i == hs.DealerSeat);
                    _wildBanner.text = "WILD:  " + _engine.EffectiveCall.Describe();
                    yield return Wait(0.3f);
                    break;

                case AntePostedEvent ante:
                    _displayPot += ante.Amount;
                    UpdatePot();
                    _views[ante.Seat].SetStack(_seats[ante.Seat].Stack);
                    break;

                case BoardSetEvent _:
                    yield return SetUpBoard();
                    break;

                case CardDealtEvent dealt:
                    yield return DealTo(dealt.Seat, dealt.Card);
                    break;

                case BoardCardFlippedEvent flip:
                    if (_boardCards.TryGetValue((flip.Side, flip.Slot), out var boardCard))
                    {
                        boardCard.SetCard(flip.Card);
                        yield return boardCard.Flip(true, 0.3f / Speed);
                    }
                    RefreshWilds();
                    break;

                case RollRequestedEvent _:
                    foreach (var v in _views) if (_seats[v.SeatIndex].InHand) v.SetStatus("choosing...");
                    break;

                case CardRolledEvent rolled:
                    yield return RollCard(rolled.Seat, rolled.Card);
                    _views[rolled.Seat].SetStatus("");
                    break;

                case TurnEvent turn:
                    HighlightTurn(turn.Seat);
                    break;

                case ActionEvent act:
                    _displayPot += act.Committed;
                    UpdatePot();
                    _views[act.Seat].SetStack(_seats[act.Seat].Stack);
                    _views[act.Seat].Say(ActionText(act), 1.8f, act.Action.Type == ActionType.Fold ? new Color(0.4f, 0.4f, 0.4f) : (Color?)null);
                    if (act.Action.Type == ActionType.Fold) _views[act.Seat].SetFolded(true);
                    yield return Wait(0.35f);
                    break;

                case OutOfTurnFoldEvent oot:
                    _views[oot.Seat].SetFolded(true);
                    _views[oot.Seat].Say(oot.Penalized ? "Folded out of turn - penalty!" : "Folded out of turn!", 2.4f, new Color(0.75f, 0.1f, 0.1f));
                    if (oot.Seat == Human) _bar.ShowIdle(false, "You folded out of turn. Dealer: \"Please wait for your turn.\"");
                    yield return Wait(0.5f);
                    break;

                case OutOfTurnRejectedEvent rejected:
                    _views[rejected.Seat].Say("Wait for your turn!", 1.8f, new Color(0.75f, 0.1f, 0.1f));
                    break;

                case BettingRoundEndedEvent _:
                    HighlightTurn(-1);
                    yield return Wait(0.25f);
                    break;

                case ShowdownEvent show:
                    yield return Reveal(show);
                    break;

                case PotAwardedEvent award:
                    _displayPot = System.Math.Max(0, _displayPot - award.Amount);
                    UpdatePot();
                    foreach (int w in award.Winners)
                    {
                        _views[w].SetStack(_seats[w].Stack);
                        _views[w].Say($"Wins {award.Amount / award.Winners.Count:N0}!", 3f, new Color(0.1f, 0.45f, 0.15f));
                    }
                    yield return Wait(0.8f);
                    break;
            }
        }

        private IEnumerator SetUpBoard()
        {
            foreach (var c in _boardCards.Values) Destroy(c.gameObject);
            _boardCards.Clear();
            var moves = new List<Coroutine>();
            foreach (int side in new[] { 2, 3 })
            {
                for (int slot = 0; slot < Game23Board.CardsPerSide; slot++)
                {
                    var view = CardView.Create(_cardLayer, Card.FromId(0), BoardLayout.CardSize, faceUp: false);
                    view.SnapTo(_deckPosition);
                    _boardCards[(side, slot)] = view;
                    moves.Add(StartCoroutine(view.MoveTo(BoardLayout.Slot(side, slot), 0.35f / Speed)));
                    yield return Wait(0.04f);
                }
            }
            foreach (var m in moves) yield return m;
        }

        private IEnumerator DealTo(int seat, Card card)
        {
            var view = _views[seat];
            bool mine = seat == Human;
            var cv = CardView.Create(_cardLayer, card, view.CardSize, faceUp: false);
            cv.SnapTo(_deckPosition);
            cv.Clicked += OnHumanCardClicked;
            view.HoleCards.Add(cv);
            yield return cv.MoveTo(view.HoleSlot(view.HoleCards.Count - 1, view.HoleCards.Count), 0.22f / Speed);
            LayoutHole(seat);
            if (mine)
            {
                yield return cv.Flip(true, 0.2f / Speed);
                RefreshWilds();
            }
        }

        private IEnumerator RollCard(int seat, Card card)
        {
            var view = _views[seat];
            var cv = view.HoleCards.FirstOrDefault(c => c.Card == card);
            if (cv == null) yield break;
            view.HoleCards.Remove(cv);
            view.UpCards.Add(cv);
            cv.Selectable = false;
            StartCoroutine(cv.MoveTo(view.UpSlot(view.UpCards.Count - 1, 4), 0.3f / Speed));
            if (!cv.FaceUp) yield return cv.Flip(true, 0.25f / Speed);
            else yield return Wait(0.3f);
            LayoutHole(seat);
            RefreshWilds();
        }

        private IEnumerator Reveal(ShowdownEvent show)
        {
            var view = _views[show.Seat];
            foreach (var cv in view.HoleCards) StartCoroutine(cv.Flip(true, 0.25f / Speed));
            yield return Wait(0.3f);
            var used = new HashSet<Card>(show.Hand.Cards);
            foreach (var cv in view.HoleCards.Concat(view.UpCards))
            {
                cv.SetWild(show.Wilds.Contains(cv.Card));
                cv.SetDimmed(!used.Contains(cv.Card));
            }
            view.SetStatus(show.Hand.Describe());
            view.Say(show.Hand.Describe(), 3.5f);
            yield return Wait(0.9f);
        }

        // ------------------------------------------------------------------ helpers

        private void LayoutHole(int seat)
        {
            var view = _views[seat];
            for (int i = 0; i < view.HoleCards.Count; i++)
                view.HoleCards[i].SnapTo(view.HoleSlot(i, view.HoleCards.Count));
        }

        /// <summary>Gold glow on wild cards, from the human's point of view (no peeking at opponents' hole cards).</summary>
        private void RefreshWilds()
        {
            if (_engine.Phase == Game23Phase.NotStarted) return;
            var mine = _engine.WildsFor(Human);
            foreach (var cv in _views[Human].HoleCards.Concat(_views[Human].UpCards)) cv.SetWild(mine.Contains(cv.Card));
            foreach (var kv in _boardCards) if (kv.Value.FaceUp) kv.Value.SetWild(mine.Contains(kv.Value.Card));

            var board = _engine.Board.AllExposed;
            for (int i = 1; i < _views.Length; i++)
            {
                // Only what can be known: their face-up cards under the parts of the call that don't need hole cards.
                var known = _engine.EffectiveCall.Resolve(new WildContext(new Card[0], _seats[i].Up, board, _engine.Settings.WildOptions));
                foreach (var cv in _views[i].UpCards) cv.SetWild(known.Contains(cv.Card));
            }
        }

        private void SetHumanCardsSelectable(bool selectable)
        {
            foreach (var cv in _views[Human].HoleCards) cv.Selectable = selectable;
        }

        private void OnHumanCardClicked(CardView cv)
        {
            if (_engine.Phase == Game23Phase.Rolling && _views[Human].HoleCards.Contains(cv)) _humanRoll = cv.Card;
        }

        private void HighlightTurn(int seat)
        {
            for (int i = 0; i < _views.Length; i++) _views[i].SetActiveTurn(i == seat);
        }

        private void ClearTable()
        {
            foreach (var v in _views)
            {
                v.ClearCards();
                v.SetFolded(false);
                v.SetStatus(_bots.TryGetValue(v.SeatIndex, out var bot) ? bot.Profile.Archetype.ToString() : "");
                v.SetStack(_seats[v.SeatIndex].Stack);
            }
            foreach (var c in _boardCards.Values) Destroy(c.gameObject);
            _boardCards.Clear();
            _displayPot = 0;
            UpdatePot();
        }

        private void UpdatePot() => _potText.text = _displayPot > 0 ? _displayPot.ToString("N0") : "";

        private static string ActionText(ActionEvent act)
        {
            string text;
            switch (act.Action.Type)
            {
                case ActionType.Fold: text = "Fold"; break;
                case ActionType.Check: text = "Check"; break;
                case ActionType.Call: text = $"Call {act.Committed:N0}"; break;
                case ActionType.Bet: text = $"Bet {act.Action.Amount:N0}"; break;
                default: text = $"Raise to {act.Action.Amount:N0}"; break;
            }
            return act.AllIn ? text + " - ALL IN" : text;
        }

        private string Named(string narration)
        {
            for (int i = _seats.Count - 1; i >= 0; i--)
            {
                narration = narration.Replace($"Seats {i} ", $"{_seats[i].Name} ").Replace($"Seat {i}", _seats[i].Name)
                    .Replace($"seat {i}", _seats[i].Name).Replace($"& {i}", "& " + _seats[i].Name);
            }
            return narration;
        }

        private void Log(string line)
        {
            _logLines.Add(line);
            if (_logLines.Count > 9) _logLines.RemoveAt(0);
            if (_logText != null) _logText.text = string.Join("\n", _logLines);
        }

        private WaitForSeconds Wait(float seconds) => new WaitForSeconds(seconds / Speed);
    }
}
