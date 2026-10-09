using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WildCardPoker.Core;
using WildCardPoker.Core.AI;
using WildCardPoker.Core.DiamondTurnOver;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Runs a Diamond Turn-over table: one human (seat 0) against computer players, with a house dealer.
    /// The rules live in <see cref="DiamondTurnOverEngine"/>; this class turns its events into animation and feeds it
    /// input. The human can press FOLD at any moment, including out of turn.
    /// </summary>
    public sealed class TableController : MonoBehaviour
    {
        private const int Human = 0;

        [Header("Table")]
        [Range(2, 6)] public int playerCount = 6;
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

        private DiamondTurnOverEngine _engine;
        private List<Seat> _seats;
        private readonly Dictionary<int, DiamondBot> _bots = new Dictionary<int, DiamondBot>();
        private SeatView[] _views;
        private readonly Dictionary<(int row, int column), CardView> _boardCards = new Dictionary<(int, int), CardView>();
        private readonly Queue<GameEvent> _queue = new Queue<GameEvent>();
        private readonly List<string> _logLines = new List<string>();
        private readonly System.Random _botRandom = new System.Random();

        private RectTransform _cardLayer;
        private Text _potText;
        private Text _logText;
        private Text _boardHint;
        private Button _passButton;
        private ActionBar _bar;
        private Vector2 _deckPosition;

        private bool _animating;
        private bool _botThinking;
        private bool _humanFoldQueued;
        private PlayerAction? _humanAction;
        private Card? _humanRoll;
        private int? _humanTurnOver;
        private bool _humanPassed;
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
                _bots[i] = new DiamondBot(i, profile, new SeededRandom(_botRandom.Next())) { Samples = botSimulationSamples };
            }

            var settings = new DiamondSettings
            {
                Structure = structure,
                Ante = ante,
                SmallBet = smallBet,
                BigBet = bigBet,
                OutOfTurnFolds = outOfTurnFolds,
                HandRules = new HandRules { NaturalBeatsWild = naturalBeatsWild },
            };
            _engine = new DiamondTurnOverEngine(settings, _seats, new CryptoRandom());

            var layouts = SeatLayouts(playerCount);
            var colors = new[]
            {
                new Color(0.18f, 0.43f, 0.71f), new Color(0.66f, 0.2f, 0.24f), new Color(0.29f, 0.42f, 0.54f),
                new Color(0.25f, 0.48f, 0.29f), new Color(0.69f, 0.38f, 0.16f), new Color(0.48f, 0.29f, 0.66f),
            };
            _views = new SeatView[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                _views[i] = SeatView.Create(_cardLayer, i, _seats[i].Name, i == Human, layouts[i], colors[i % colors.Length]);
                _views[i].SetStack(_seats[i].Stack);
                if (_bots.TryGetValue(i, out var bot)) _views[i].SetStatus(bot.Profile.Archetype.ToString());
            }
        }

        /// <summary>Seats run clockwise from the human at the bottom; the house dealer sits at the top center.</summary>
        private static SeatLayout[] SeatLayouts(int count)
        {
            SeatLayout L(float px, float py, float hx, float hy, float ux, float uy) =>
                new SeatLayout(new Vector2(px, py), new Vector2(hx, hy), new Vector2(ux, uy));

            var human = L(0, -430, -310, -430, 0, -300);
            var bottomLeft = L(-600, -330, -600, -238, -380, -250);
            var left = L(-760, 60, -760, 152, -520, 60);
            var topLeft = L(-500, 350, -500, 435, -500, 255);
            var topRight = L(500, 350, 500, 435, 500, 255);
            var right = L(760, 60, 760, 152, 520, 60);
            switch (count)
            {
                case 2: return new[] { human, topRight };
                case 3: return new[] { human, topLeft, topRight };
                case 4: return new[] { human, left, topLeft, topRight };
                case 5: return new[] { human, left, topLeft, topRight, right };
                default: return new[] { human, bottomLeft, left, topLeft, topRight, right };
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

            var canvasGo = new GameObject("Diamond Turn-over UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            var backdrop = Ui.Image("Backdrop", root, null, new Color(0.04f, 0.05f, 0.06f));
            backdrop.rectTransform.Stretch();

            // Table: leather rail, gold pinstripe, emerald felt (masked to the rail's rounded shape).
            var rail = Ui.Image("Rail", root, Theme.Panel, Theme.Rail, sliced: true);
            rail.pixelsPerUnitMultiplier = 0.08f;
            rail.rectTransform.Place(new Vector2(0, 10), new Vector2(1780, 900));
            var stripe = Ui.Image("Pinstripe", root, Theme.Panel, Theme.Gold * 0.8f, sliced: true);
            stripe.pixelsPerUnitMultiplier = 0.09f;
            stripe.rectTransform.Place(new Vector2(0, 10), new Vector2(1700, 820));
            var feltMask = Ui.Image("FeltMask", root, Theme.Panel, Color.white, sliced: true);
            feltMask.pixelsPerUnitMultiplier = 0.09f;
            feltMask.rectTransform.Place(new Vector2(0, 10), new Vector2(1690, 810));
            feltMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var felt = Ui.Image("Felt", feltMask.transform, Theme.Felt, Color.white);
            felt.rectTransform.Stretch();

            var watermark = Ui.Text("Watermark", root, "DIAMOND TURN-OVER", 34, new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.28f), TextAnchor.MiddleCenter, FontStyle.Bold);
            watermark.rectTransform.Place(new Vector2(0, 238), new Vector2(700, 50));

            BuildDealerStation(root);
            BuildBoardLabels(root);

            _cardLayer = Ui.Rect("Table", root);
            _cardLayer.Stretch();

            var potChip = Ui.Image("PotChip", _cardLayer, ProceduralArt.Chip(new Color(0.7f, 0.07f, 0.16f)), Color.white);
            potChip.rectTransform.Place(new Vector2(-50, -105), new Vector2(40, 40));
            _potText = Ui.Text("Pot", _cardLayer, "", 30, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Shadowed();
            _potText.rectTransform.Place(new Vector2(36, -105), new Vector2(150, 44));

            _boardHint = Ui.Text("BoardHint", _cardLayer, "", 20, Theme.Text, TextAnchor.MiddleCenter).Shadowed();
            _boardHint.rectTransform.Place(new Vector2(0, -150), new Vector2(760, 32));

            _passButton = Ui.Button("Pass", _cardLayer, "PASS — KEEP THE BOARD", new Color(0.11f, 0.12f, 0.14f), () => _humanPassed = true, 20);
            ((RectTransform)_passButton.transform).Place(new Vector2(0, -200), new Vector2(300, 48));
            _passButton.gameObject.SetActive(false);

            var bannerBg = Ui.Image("BannerBg", root, Theme.Panel, Theme.PanelBg, sliced: true);
            bannerBg.rectTransform.anchorMin = bannerBg.rectTransform.anchorMax = new Vector2(0.5f, 1);
            bannerBg.rectTransform.pivot = new Vector2(0.5f, 1);
            bannerBg.rectTransform.anchoredPosition = new Vector2(0, -12);
            bannerBg.rectTransform.sizeDelta = new Vector2(620, 50);
            var banner = Ui.Text("WildBanner", bannerBg.transform, "WILD  ·  Lowest <color=#ff5c6c>♦</color> in the hole & all like it", 23, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            banner.supportRichText = true;
            banner.rectTransform.Stretch(6);

            var logBg = Ui.Image("LogBg", root, Theme.Panel, Theme.PanelBg, sliced: true);
            logBg.rectTransform.anchorMin = logBg.rectTransform.anchorMax = new Vector2(0, 1);
            logBg.rectTransform.pivot = new Vector2(0, 1);
            logBg.rectTransform.anchoredPosition = new Vector2(24, -24);
            logBg.rectTransform.sizeDelta = new Vector2(290, 200);
            _logText = Ui.Text("Log", logBg.transform, "", 15, Theme.Text, TextAnchor.LowerLeft);
            _logText.rectTransform.Stretch(10);
            _logText.verticalOverflow = VerticalWrapMode.Truncate;

            var rules = Ui.Image("RulesBg", root, Theme.Panel, Theme.PanelBg, sliced: true);
            rules.rectTransform.anchorMin = rules.rectTransform.anchorMax = new Vector2(1, 1);
            rules.rectTransform.pivot = new Vector2(1, 1);
            rules.rectTransform.anchoredPosition = new Vector2(-24, -24);
            rules.rectTransform.sizeDelta = new Vector2(290, 150);
            var rulesText = Ui.Text("Rules", rules.transform,
                "<b><color=#d4af5a>MAKE YOUR HAND</color></b>\nUse 1, 2, 3 or 4 face-up cards from ONE row (top or bottom, never both) plus your own cards. Aces high or low.",
                15, Theme.Text, TextAnchor.UpperLeft);
            rulesText.supportRichText = true;
            rulesText.rectTransform.Stretch(12);

            _bar = new ActionBar(root);
            _bar.FoldPressed += OnHumanFold;
            _bar.ActionChosen += a => _humanAction = a;
        }

        /// <summary>The house dealer at the top of the table: badge, chip rack and card shoe.</summary>
        private void BuildDealerStation(RectTransform root)
        {
            var badge = Ui.Image("DealerBadge", root, Theme.Panel, new Color(0.07f, 0.075f, 0.09f), sliced: true);
            badge.rectTransform.Place(new Vector2(0, 425), new Vector2(300, 60));
            var avatar = Ui.Image("DealerAvatar", badge.transform, Theme.Circle, Theme.Gold);
            avatar.rectTransform.Place(new Vector2(-118, 0), new Vector2(46, 46));
            var inner = Ui.Image("DealerAvatarInner", avatar.transform, Theme.Circle, new Color(0.11f, 0.12f, 0.13f));
            inner.rectTransform.Stretch(3);
            var d = Ui.Text("D", inner.transform, "♦", 24, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            d.rectTransform.Stretch();
            var title = Ui.Text("DealerTitle", badge.transform, "HOUSE DEALER", 20, Theme.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            title.rectTransform.Stretch();
            title.rectTransform.offsetMin = new Vector2(70, 6);
            title.rectTransform.offsetMax = new Vector2(-8, -8);
            var sub = Ui.Text("DealerSub", badge.transform, "Sets the board · turns cards over", 14, Theme.Muted, TextAnchor.LowerLeft);
            sub.rectTransform.Stretch();
            sub.rectTransform.offsetMin = new Vector2(70, 8);
            sub.rectTransform.offsetMax = new Vector2(-8, -6);

            var rack = Ui.Image("ChipRack", root, Theme.Panel, new Color(0.03f, 0.035f, 0.04f), sliced: true);
            rack.rectTransform.Place(new Vector2(-20, 358), new Vector2(300, 42));
            var chipColors = new[]
            {
                new Color(0.91f, 0.89f, 0.85f), new Color(0.7f, 0.07f, 0.16f), new Color(0.12f, 0.44f, 0.25f),
                new Color(0.08f, 0.09f, 0.1f), new Color(0.36f, 0.16f, 0.53f), new Color(0.12f, 0.31f, 0.6f),
            };
            for (int i = 0; i < chipColors.Length; i++)
            {
                var chip = Ui.Image("RackChip" + i, rack.transform, ProceduralArt.Chip(chipColors[i]), Color.white);
                chip.rectTransform.Place(new Vector2(-125 + i * 50, 0), new Vector2(34, 34));
            }

            _deckPosition = new Vector2(175, 358);
            var shoe = Ui.Image("Shoe", root, Theme.Panel, new Color(0.03f, 0.035f, 0.04f), sliced: true);
            shoe.rectTransform.Place(_deckPosition, new Vector2(64, 54));
            var top = Ui.Image("ShoeCard", shoe.transform, Theme.CardBack, Color.white);
            top.rectTransform.Place(Vector2.zero, new Vector2(34, 46));
        }

        private void BuildBoardLabels(RectTransform root)
        {
            for (int row = 1; row <= 2; row++)
            {
                var label = Ui.Text("Row" + row, root, "ROW " + row, 18, Theme.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
                label.rectTransform.Place(BoardLayout.RowLabel(row), new Vector2(90, 30));
            }
            for (int col = 0; col < DiamondBoard.Columns; col++)
            {
                var label = Ui.Text("Col" + col, root, (col + 1).ToString(), 16, new Color(1, 1, 1, 0.65f), TextAnchor.MiddleCenter, FontStyle.Bold);
                label.rectTransform.Place(BoardLayout.ColumnLabel(col), new Vector2(40, 24));
            }
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
                _stacksAtStart = _seats.Select(s => s.Stack).ToArray();
                _engine.StartHand();
                yield return WaitIdle();

                while (_engine.Phase != Phase.HandOver)
                {
                    var pending = _engine.Pending;
                    switch (pending.Phase)
                    {
                        case Phase.Rolling: yield return RollingStep(pending.Seat); break;
                        case Phase.TurnOver: yield return TurnOverStep(pending.Seat); break;
                        case Phase.Betting: yield return BettingStep(pending.Seat); break;
                    }
                    yield return WaitIdle();
                }

                for (int i = 0; i < _seats.Count; i++)
                    if (_bots.TryGetValue(i, out var bot))
                        bot.OnHandEnded(_seats[i].Stack - _stacksAtStart[i], _stacksAtStart[i]);

                _bar.ShowIdle(false, "Next hand coming up...");
                yield return new WaitForSeconds(4f / Speed);
            }
        }

        /// <summary>Players roll one by one from the dealer's left; each sees earlier rolls before choosing.</summary>
        private IEnumerator RollingStep(int roller)
        {
            if (roller < 0) yield break;
            HighlightTurn(roller);
            if (roller == Human)
            {
                _humanRoll = null;
                SetHumanCardsSelectable(true);
                _bar.ShowIdle(true, "Your roll: tap one of your hole cards to turn it face up.");
                yield return new WaitUntil(() => _humanRoll != null || !_seats[Human].InHand || _engine.SeatToAct != Human);
                SetHumanCardsSelectable(false);
                if (_humanRoll != null && _engine.Phase == Phase.Rolling && _engine.SeatToAct == Human)
                    _engine.Roll(Human, _humanRoll.Value);
            }
            else
            {
                _bar.ShowIdle(_seats[Human].InHand, $"{_seats[roller].Name} is choosing a card to roll...");
                float end = Time.time + (0.6f + (float)_botRandom.NextDouble() * 0.8f) / Speed;
                while (Time.time < end && _engine.SeatToAct == roller) yield return null;
                if (_engine.Phase == Phase.Rolling && _engine.SeatToAct == roller) _engine.Roll(roller, _bots[roller].ChooseRoll(_engine));
            }
            HighlightTurn(-1);
        }

        /// <summary>The player with the highest hand showing (or, at the end, each player in turn) may turn over a column.</summary>
        private IEnumerator TurnOverStep(int chooser)
        {
            if (chooser < 0) yield break;
            HighlightTurn(chooser);
            if (chooser == Human)
            {
                _humanTurnOver = null;
                _humanPassed = false;
                SetBoardSelectable(true);
                _passButton.gameObject.SetActive(true);
                _boardHint.text = "Your turn-over: tap a board card and the dealer turns its column over.";
                _bar.ShowIdle(true, "Turn over a card, or pass.");
                yield return new WaitUntil(() => _humanTurnOver != null || _humanPassed || !_seats[Human].InHand || _engine.SeatToAct != Human);
                SetBoardSelectable(false);
                _passButton.gameObject.SetActive(false);
                _boardHint.text = "";
                if (_engine.Phase == Phase.TurnOver && _engine.SeatToAct == Human)
                    _engine.TurnOver(Human, _humanPassed ? null : _humanTurnOver);
            }
            else
            {
                _boardHint.text = $"{_seats[chooser].Name} may turn over a card...";
                _bar.ShowIdle(_seats[Human].InHand, $"{_seats[chooser].Name} is deciding on a turn-over...");
                float started = Time.time;
                int? column = null;
                yield return Think(() => _bots[chooser].ChooseTurnOver(_engine), null, c => column = c);
                float end = started + (0.9f + (float)_botRandom.NextDouble() * 1.1f) / Speed;
                while (Time.time < end && _engine.SeatToAct == chooser) yield return null;
                _boardHint.text = "";
                if (_engine.Phase == Phase.TurnOver && _engine.SeatToAct == chooser) _engine.TurnOver(chooser, column);
            }
            HighlightTurn(-1);
        }

        private IEnumerator BettingStep(int toAct)
        {
            if (toAct < 0) yield break;
            HighlightTurn(toAct);

            // Live-table behaviour: impatient players fold before the action reaches them.
            var others = _bots.Values.Where(b => b.SeatIndex != toAct).ToList();
            List<DiamondBot> folders = null;
            yield return Think(() => others.Where(b => b.WantsToFoldOutOfTurn(_engine)).ToList(), new List<DiamondBot>(), r => folders = r);
            foreach (var bot in folders)
            {
                if (_engine.Phase != Phase.Betting || !_engine.Fold(bot.SeatIndex)) continue;
                var line = TableTalk.Maybe(TableTalk.Moment.OutOfTurnFold, bot.Profile, new SeededRandom(_botRandom.Next()), 0.3f);
                if (line != null) _views[bot.SeatIndex].Say(line);
                yield return WaitIdle();
            }
            if (_engine.Phase != Phase.Betting || _engine.SeatToAct != toAct) yield break;

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
            if (!_seats[Human].InHand || _engine.Phase != Phase.Betting || _engine.SeatToAct != Human) yield break;
            var action = _humanAction ?? (legal.CanCheck ? PlayerAction.Check() : PlayerAction.Fold());
            SafeAct(Human, action, legal);
        }

        private IEnumerator BotTurn(int seat, LegalActions legal)
        {
            var bot = _bots[seat];
            _bar.ShowIdle(_seats[Human].InHand, $"{_seats[seat].Name} is thinking...");
            float started = Time.time;
            var fallback = new BotDecision(legal.CanCheck ? PlayerAction.Check() : PlayerAction.Fold(), 0.5f, null);
            BotDecision decision = fallback;
            yield return Think(() => bot.DecideBet(_engine, legal), fallback, d => decision = d);

            float end = started + decision.ThinkSeconds / Speed;
            while (Time.time < end && _engine.Phase == Phase.Betting && _engine.SeatToAct == seat)
            {
                _views[seat].SetTimer(1 - (Time.time - started) / Mathf.Max(0.01f, end - started));
                yield return null;
            }
            if (_engine.Phase != Phase.Betting || _engine.SeatToAct != seat) yield break;
            if (decision.Chat != null) _views[seat].Say(decision.Chat, 2.4f);
            SafeAct(seat, decision.Action, legal);
        }

        /// <summary>
        /// Runs a bot's simulation off the main thread so animation stays smooth. The engine is not changed meanwhile:
        /// a human fold pressed during the calculation is queued and applied right after.
        /// </summary>
        private IEnumerator Think<T>(System.Func<T> work, T fallback, System.Action<T> done)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            done(work());
            yield break;
#else
            _botThinking = true;
            var task = Task.Run(work);
            yield return new WaitUntil(() => task.IsCompleted);
            _botThinking = false;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                done(fallback);
            }
            else done(task.Result);
            if (_humanFoldQueued)
            {
                _humanFoldQueued = false;
                OnHumanFold();
                yield return WaitIdle();
            }
#endif
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
            bool inTurn = _engine.Phase == Phase.Betting && _engine.SeatToAct == Human;
            if (inTurn) _humanAction = PlayerAction.Fold();
            else _engine.Fold(Human); // while someone else rolls, turns over or bets: the engine applies table policy
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
                    yield return Wait(0.3f);
                    break;

                case AntePostedEvent ante:
                    _displayPot += ante.Amount;
                    UpdatePot();
                    _views[ante.Seat].SetStack(_seats[ante.Seat].Stack);
                    break;

                case BoardSetEvent board:
                    yield return SetUpBoard(board);
                    break;

                case CardDealtEvent dealt:
                    yield return DealTo(dealt.Seat, dealt.Card);
                    break;

                case RollRequestedEvent request:
                    foreach (int s in request.Seats) _views[s].SetStatus("rolling...");
                    break;

                case CardRolledEvent rolled:
                    yield return RollCard(rolled.Seat, rolled.Card);
                    _views[rolled.Seat].SetStatus(ShowingHand.Describe(_seats[rolled.Seat].Up) + " showing");
                    break;

                case TurnOverOfferedEvent offer:
                    HighlightTurn(offer.Seat);
                    _views[offer.Seat].Say("Turn-over pick", 1.6f, new Color(0.55f, 0.36f, 0f));
                    break;

                case ColumnTurnedEvent turned:
                    _views[turned.Seat].Say($"Turn over #{turned.Column + 1}!", 1.8f);
                    yield return TurnColumn(turned);
                    break;

                case TurnOverPassedEvent passed:
                    _views[passed.Seat].Say("Pass", 1.4f, new Color(0.35f, 0.35f, 0.35f));
                    yield return Wait(0.3f);
                    break;

                case TurnEvent turn:
                    HighlightTurn(turn.Seat);
                    break;

                case ActionEvent act:
                    _displayPot += act.Committed;
                    UpdatePot();
                    _views[act.Seat].SetStack(_seats[act.Seat].Stack);
                    _views[act.Seat].Say(ActionText(act), 1.8f, act.Action.Type == ActionType.Fold ? new Color(0.4f, 0.4f, 0.4f) : (Color?)null);
                    if (act.Action.Type == ActionType.Fold)
                    {
                        _views[act.Seat].SetFolded(true);
                        RefreshHighlights();
                    }
                    yield return Wait(0.35f);
                    break;

                case OutOfTurnFoldEvent oot:
                    _views[oot.Seat].SetFolded(true);
                    RefreshHighlights();
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

        /// <summary>The dealer lays out two rows of five from the shoe, then turns up the alternating cards.</summary>
        private IEnumerator SetUpBoard(BoardSetEvent board)
        {
            foreach (var c in _boardCards.Values) Destroy(c.gameObject);
            _boardCards.Clear();
            var moves = new List<Coroutine>();
            for (int row = 1; row <= 2; row++)
            {
                var cards = row == 1 ? board.Row1 : board.Row2;
                for (int col = 0; col < DiamondBoard.Columns; col++)
                {
                    var known = cards[col];
                    var view = CardView.Create(_cardLayer, known ?? Card.FromId(0), BoardLayout.CardSize, faceUp: false);
                    view.SnapTo(_deckPosition);
                    int column = col;
                    view.Clicked += _ => _humanTurnOver = column;
                    _boardCards[(row, col)] = view;
                    moves.Add(StartCoroutine(view.MoveTo(BoardLayout.Slot(row, col), 0.35f / Speed)));
                    yield return Wait(0.05f);
                }
            }
            foreach (var m in moves) yield return m;

            var flips = new List<Coroutine>();
            for (int row = 1; row <= 2; row++)
            {
                var cards = row == 1 ? board.Row1 : board.Row2;
                for (int col = 0; col < DiamondBoard.Columns; col++)
                    if (cards[col].HasValue) flips.Add(StartCoroutine(_boardCards[(row, col)].Flip(true, 0.25f / Speed)));
            }
            foreach (var f in flips) yield return f;
            RefreshHighlights();
        }

        /// <summary>Both cards of the column flip together: the revealed card comes up, the other goes down.</summary>
        private IEnumerator TurnColumn(ColumnTurnedEvent turned)
        {
            var up = _boardCards[(turned.RevealedRow, turned.Column)];
            var down = _boardCards[(turned.RevealedRow == 1 ? 2 : 1, turned.Column)];
            up.SetCard(turned.Revealed);
            down.SetWild(false);
            var a = StartCoroutine(up.Flip(true, 0.35f / Speed));
            var b = StartCoroutine(down.Flip(false, 0.35f / Speed));
            yield return a;
            yield return b;
            RefreshHighlights();
            yield return Wait(0.3f);
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
                RefreshHighlights();
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
            RefreshHighlights();
        }

        private IEnumerator Reveal(ShowdownEvent show)
        {
            var view = _views[show.Seat];
            foreach (var cv in view.HoleCards) StartCoroutine(cv.Flip(true, 0.25f / Speed));
            yield return Wait(0.3f);
            var used = new HashSet<Card>(show.Hand.Cards);
            view.SetHighHand(false);
            foreach (var cv in view.HoleCards.Concat(view.UpCards))
            {
                cv.SetWild(show.Wilds.Contains(cv.Card));
                cv.SetDimmed(!used.Contains(cv.Card));
                cv.SetHighlight(used.Contains(cv.Card) ? Theme.BestHandGlow : (Color?)null);
            }
            // Flash the board cards this player's hand uses.
            foreach (var kv in _boardCards)
                kv.Value.SetHighlight(kv.Value.FaceUp && used.Contains(kv.Value.Card) ? Theme.BestHandGlow : (Color?)null);
            string text = show.Hand.Describe() + " · " + show.Hand.Source;
            view.SetStatus(text);
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

        /// <summary>
        /// Flashing cards, so players always know where they stand:
        /// gold = your wild cards (lowest diamond in the hole and every card like it, in your hand and on the board);
        /// blue = the cards that make up your best hand right now (your cards plus the board row it uses);
        /// white + "HIGH HAND" tag = the up cards of the player with the highest hand showing.
        /// Opponents' wilds and best hands depend on hidden cards, so nothing is shown for them.
        /// </summary>
        private void RefreshHighlights()
        {
            if (_engine.Phase == Phase.NotStarted || _engine.Phase == Phase.HandOver || !_engine.Board.IsSet) return;
            var human = _views[Human];
            var mine = _seats[Human].InHand ? _engine.WildsFor(Human) : WildSet.Empty;
            foreach (var cv in human.HoleCards.Concat(human.UpCards)) cv.SetWild(mine.Contains(cv.Card));
            foreach (var kv in _boardCards) kv.Value.SetWild(kv.Value.FaceUp && mine.Contains(kv.Value.Card));

            int high = _engine.HighestShowingSeat();
            for (int i = 0; i < _views.Length; i++)
            {
                bool isHigh = i == high;
                _views[i].SetHighHand(isHigh);
                if (i == Human) continue;
                foreach (var cv in _views[i].UpCards) cv.SetHighlight(isHigh ? Theme.HighHandGlow : (Color?)null);
            }

            var best = _seats[Human].InHand ? _engine.BestHand(Human) : null;
            var inBest = best == null ? new HashSet<Card>() : new HashSet<Card>(best.Cards);
            foreach (var cv in human.HoleCards.Concat(human.UpCards))
            {
                Color? glow = null;
                if (inBest.Contains(cv.Card)) glow = Theme.BestHandGlow;
                else if (high == Human && human.UpCards.Contains(cv)) glow = Theme.HighHandGlow;
                cv.SetHighlight(glow);
            }
            foreach (var kv in _boardCards)
                kv.Value.SetHighlight(kv.Value.FaceUp && inBest.Contains(kv.Value.Card) ? Theme.BestHandGlow : (Color?)null);
            if (best != null) human.SetStatus("Best: " + best.Describe() + " · " + best.Source);
        }

        private void SetHumanCardsSelectable(bool selectable)
        {
            foreach (var cv in _views[Human].HoleCards) cv.Selectable = selectable;
        }

        private void SetBoardSelectable(bool selectable)
        {
            foreach (var cv in _boardCards.Values) cv.Selectable = selectable;
        }

        private void OnHumanCardClicked(CardView cv)
        {
            if (_engine.Phase == Phase.Rolling && _views[Human].HoleCards.Contains(cv)) _humanRoll = cv.Card;
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

        private void UpdatePot() => _potText.text = _displayPot > 0 ? "POT  " + _displayPot.ToString("N0") : "";

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
