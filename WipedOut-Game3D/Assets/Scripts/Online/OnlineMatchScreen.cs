using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WipedOut.Online
{
    /// <summary>
    /// Pantalla de la partida online. Se construye entera por código (no hay que editar la escena) y se
    /// dibuja encima de cualquier escena: un botón ONLINE siempre visible y, al pulsarlo, un lobby y el
    /// tablero en 2D.
    ///
    /// Es una vista tonta: no sabe jugar. Cada pulsación se convierte en una petición al servidor y lo
    /// único que se pinta es la <see cref="OnlineMatchView"/> que llega. Para ver la partida online en
    /// el tablero 3D de la escena, basta con suscribirse a <see cref="Service"/>.ViewChanged y dibujar
    /// esa misma vista (ver docs/TAREAS.md).
    /// </summary>
    public sealed class OnlineMatchScreen : MonoBehaviour
    {
        private const float PollSecondsWithPush = 10f;
        private const float PollSecondsWithoutPush = 3f;
        private const float WakeRetrySeconds = 3f;

        // Misma duración que MatchRules.GraceSeconds del servidor (+0,5 s de colchón).
        private const double GraceBeforeClaimingTimeout = 3.5;

        private static readonly Color Background = new Color(0.12f, 0.14f, 0.18f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.42f, 0.75f);
        private static readonly Color DangerColor = new Color(0.80f, 0.20f, 0.20f);
        private static readonly Color HiddenCell = new Color(0.93f, 0.93f, 0.93f);
        private static readonly Color SafeCell = new Color(0.45f, 0.80f, 0.55f);
        private static readonly Color BoomCell = new Color(0.97f, 0.50f, 0.20f);
        private static readonly Color Player1Color = new Color(0.35f, 0.55f, 0.97f);
        private static readonly Color Player2Color = new Color(0.97f, 0.40f, 0.40f);

        private readonly OnlineMatchService service = new OnlineMatchService();
        private readonly List<Image> cellImages = new List<Image>();
        private readonly List<Text> cellLabels = new List<Text>();

        private Font font;
        private GameObject overlay;
        private GameObject lobbyPanel;
        private GameObject matchPanel;

        private Text lobbyStatus;
        private InputField codeInput;
        private InputField profileInput;

        private Text roomText;
        private Text levelText;
        private Text livesText;
        private Text phaseText;
        private Text timerText;
        private Text infoText;
        private Button validateButton;
        private Button forfeitButton;
        private RectTransform gridRoot;
        private int builtSize = -1;

        private float nextPoll;
        private float nextWake;

        /// <summary>Para que otras vistas (por ejemplo el tablero 3D) puedan suscribirse a las vistas del servidor.</summary>
        public OnlineMatchService Service => service;

        private void Awake()
        {
            service.ViewChanged += Render;
            service.Failed += ShowFailure;
            service.ResyncRequested += RequestResync;
        }

        private void OnDestroy()
        {
            service.ViewChanged -= Render;
            service.Failed -= ShowFailure;
            service.ResyncRequested -= RequestResync;
            _ = service.UnsubscribeAsync();
        }

        private void Update()
        {
            service.Pump();

            OnlineMatchView view = service.Current;
            if (overlay == null || !overlay.activeSelf || view == null || view.status != OnlineStatus.Playing)
            {
                SetText(timerText, string.Empty);
                return;
            }

            double wakeAfter;
            if (view.phase == OnlinePhase.Memorize)
            {
                double left = view.MemorizeSecondsLeftAtServerTime() - Elapsed();
                SetText(timerText, Mathf.Max(0, Mathf.CeilToInt((float)left)) + " s");
                wakeAfter = -0.5;
                Wake(left < wakeAfter);
            }
            else
            {
                double left = view.TurnSecondsLeftAtServerTime() - Elapsed();
                SetText(timerText, Mathf.Max(0, Mathf.CeilToInt((float)left)) + " s");
                wakeAfter = -GraceBeforeClaimingTimeout;
                Wake(left < wakeAfter);
            }

            // Llegó un aviso del rival: el aviso no trae estado, así que se pide el real.
            if (service.HasPendingRefresh && !service.IsBusy)
            {
                _ = service.RefreshAsync();
                return;
            }

            // Sondeo de respaldo: un push puede perderse, el estado leído no.
            if (Time.realtimeSinceStartup >= nextPoll && !service.IsBusy)
            {
                nextPoll = Time.realtimeSinceStartup + (service.IsSubscribed ? PollSecondsWithPush : PollSecondsWithoutPush);
                _ = service.RefreshAsync();
            }
        }

        private double Elapsed()
        {
            return Time.realtimeSinceStartup - service.CurrentReceivedAt;
        }

        /// <summary>
        /// El servidor no tiene relojes: si venció un plazo (ocultar las minas, o el turno de alguien) hay que
        /// "despertarlo" preguntando. Lo hace el primero de los dos que lo note.
        /// </summary>
        private void Wake(bool due)
        {
            if (due && Time.realtimeSinceStartup >= nextWake && !service.IsBusy)
            {
                nextWake = Time.realtimeSinceStartup + WakeRetrySeconds;
                _ = service.RefreshAsync();
            }
        }

        // ------------------------------------------------------------------ acciones

        private async void Open()
        {
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
            Render(service.Current);

            if (await service.EnsureReadyAsync(profileInput != null ? profileInput.text : null))
            {
                await service.SubscribeAsync();
                if (service.Current != null)
                {
                    _ = service.RefreshAsync();
                }
            }
        }

        private void Close()
        {
            overlay.SetActive(false);
        }

        private async void OnCreateClicked()
        {
            SetText(lobbyStatus, "Creando sala...");
            if (await service.EnsureReadyAsync(profileInput.text))
            {
                await service.SubscribeAsync();
                _ = service.CreateAsync();
            }
        }

        private async void OnJoinClicked()
        {
            string code = codeInput.text;
            if (string.IsNullOrWhiteSpace(code))
            {
                SetText(lobbyStatus, "Escribe el código de la sala.");
                return;
            }

            SetText(lobbyStatus, "Uniéndose...");
            if (await service.EnsureReadyAsync(profileInput.text))
            {
                await service.SubscribeAsync();
                _ = service.JoinAsync(code);
            }
        }

        private void OnCellClicked(int index)
        {
            OnlineMatchView view = service.Current;
            if (view == null || view.IsFinished)
            {
                return;
            }

            if (!view.IsMyTurn)
            {
                SetText(infoText, view.phase == OnlinePhase.Memorize ? "Memoriza las minas." : "No es tu turno.");
                return;
            }

            if (view.phase == OnlinePhase.Placement)
            {
                _ = service.PlaceMineAsync(index);
            }
            else if (view.phase == OnlinePhase.Search)
            {
                _ = service.RevealAsync(index);
            }
        }

        private void OnValidateClicked()
        {
            _ = service.ValidateAsync();
        }

        private void OnForfeitClicked()
        {
            _ = service.ForfeitAsync();
        }

        private void OnLeaveClicked()
        {
            service.Leave();
            builtSize = -1;
            Render(null);
        }

        private void RequestResync()
        {
            if (service.Current != null && !service.IsBusy)
            {
                _ = service.RefreshAsync();
            }
        }

        // ------------------------------------------------------------------- vista

        private void Render(OnlineMatchView view)
        {
            if (overlay == null)
            {
                return;
            }

            lobbyPanel.SetActive(view == null);
            matchPanel.SetActive(view != null);

            if (view == null)
            {
                SetText(lobbyStatus, "Crea una sala o únete con el código de tu rival.");
                return;
            }

            nextPoll = Time.realtimeSinceStartup + PollSecondsWithPush;
            SetText(roomText, "ROOM " + view.matchCode);
            SetText(levelText, view.level > 0 ? "LEVEL " + view.level + "  (" + view.size + "x" + view.size + ")" : string.Empty);
            SetText(livesText, LivesLine(view));
            SetText(phaseText, PhaseLine(view));
            SetText(infoText, !string.IsNullOrEmpty(view.message) && view.outcome != OnlineOutcome.Applied
                ? view.message
                : DescribeEvent(view));

            RenderBoard(view);

            validateButton.interactable = view.IsMyTurn && view.phase == OnlinePhase.Search;
            forfeitButton.gameObject.SetActive(view.status == OnlineStatus.Playing);
        }

        private static string PhaseLine(OnlineMatchView view)
        {
            if (view.status == OnlineStatus.WaitingForGuest)
            {
                return "WAITING FOR YOUR RIVAL  -  CODE " + view.matchCode;
            }

            if (view.IsFinished)
            {
                return view.IWon ? "YOU WIN!" : "YOU LOSE";
            }

            switch (view.phase)
            {
                case OnlinePhase.Placement:
                    return view.IsMyTurn
                        ? "PLACE YOUR MINES (" + view.PlacedOfSeat(view.mySeat) + "/" + view.minesPerPlayer + ")"
                        : "RIVAL IS PLACING MINES - MEMORIZE THEM";
                case OnlinePhase.Memorize:
                    return "REMEMBER!";
                case OnlinePhase.Search:
                    return view.IsMyTurn
                        ? "YOUR TURN: PICK A WHITE TILE OR PRESS THE RED BUTTON"
                        : "RIVAL'S TURN";
                default:
                    return string.Empty;
            }
        }

        private void RenderBoard(OnlineMatchView view)
        {
            gridRoot.gameObject.SetActive(view.size > 0 && view.cells != null && view.cells.Length == view.size * view.size);
            if (!gridRoot.gameObject.activeSelf)
            {
                return;
            }

            if (builtSize != view.size)
            {
                BuildGrid(view.size);
            }

            // Las minas solo vienen mientras el juego las muestra a propósito (colocación, memorización, fin).
            var owners = new Dictionary<int, int>();
            if (view.mines != null)
            {
                for (int i = 0; i < view.mines.Length; i++)
                {
                    owners[view.mines[i]] = view.mineOwners != null && i < view.mineOwners.Length ? view.mineOwners[i] : 0;
                }
            }

            for (int i = 0; i < cellImages.Count; i++)
            {
                int state = view.cells[i];
                Image image = cellImages[i];
                Text label = cellLabels[i];

                if (state == 0)
                {
                    image.color = SafeCell;
                    label.text = string.Empty;
                }
                else if (state == 1)
                {
                    image.color = BoomCell;
                    label.text = "X";
                }
                else if (owners.TryGetValue(i, out int owner))
                {
                    image.color = owner == 1 ? Player1Color : owner == 2 ? Player2Color : BoomCell;
                    label.text = "X";
                }
                else
                {
                    image.color = HiddenCell;
                    label.text = string.Empty;
                }
            }
        }

        private void BuildGrid(int size)
        {
            foreach (Transform child in gridRoot)
            {
                Destroy(child.gameObject);
            }

            cellImages.Clear();
            cellLabels.Clear();

            GridLayoutGroup layout = gridRoot.GetComponent<GridLayoutGroup>();
            float spacing = 6f;
            float cell = Mathf.Min(120f, (680f - spacing * (size - 1)) / size);
            layout.cellSize = new Vector2(cell, cell);
            layout.spacing = new Vector2(spacing, spacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = size;

            for (int i = 0; i < size * size; i++)
            {
                int index = i;
                var go = new GameObject("Cell_" + i, typeof(RectTransform));
                go.transform.SetParent(gridRoot, false);
                Image image = go.AddComponent<Image>();
                image.color = HiddenCell;
                Button button = go.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => OnCellClicked(index));

                Text label = CreateLabel(go.transform, string.Empty, 44, Vector2.zero, Vector2.zero, Vector2.zero);
                label.color = Color.white;
                label.raycastTarget = false;
                Stretch((RectTransform)label.transform);

                cellImages.Add(image);
                cellLabels.Add(label);
            }

            builtSize = size;
        }

        private static string LivesLine(OnlineMatchView view)
        {
            string me1 = view.mySeat == 1 ? " (YOU)" : string.Empty;
            string me2 = view.mySeat == 2 ? " (YOU)" : string.Empty;
            return "P1" + me1 + " x" + Mathf.Max(0, view.lives1) + "        P2" + me2 + " x" + Mathf.Max(0, view.lives2);
        }

        private static string DescribeEnd(OnlineMatchView view)
        {
            switch (view.endReason)
            {
                case "forfeit":
                    return view.IWon ? "Tu rival abandonó." : "Abandonaste la partida.";
                case "abandoned":
                    return view.IWon ? "Tu rival se desconectó." : "Se te consideró desconectado por inactividad.";
                default:
                    return "Sin vidas.";
            }
        }

        private static string DescribeEvent(OnlineMatchView view)
        {
            if (view.IsFinished)
            {
                return DescribeEnd(view);
            }

            OnlineLastEvent e = view.lastEvent;
            if (e == null)
            {
                return string.Empty;
            }

            string who = e.seat == view.mySeat ? "Tú" : "Tu rival";
            switch (e.kind)
            {
                case "joined":
                    return "Tu rival se unió. ¡A colocar minas!";
                case "place":
                    return who + " colocó una mina.";
                case "hide":
                    return "Las minas se ocultaron. ¡A buscar!";
                case "reveal":
                    return e.wasMine
                        ? who + " pisó una mina (-1 vida). Empieza el nivel " + view.level + "."
                        : who + " destapó una casilla segura.";
                case "validate":
                    return e.levelAdvanced
                        ? who + " pulsó el botón rojo: ¡nivel completado!"
                        : who + " se precipitó con el botón rojo (-1 vida).";
                case "timeout":
                    return e.cell >= 0
                        ? who + " se quedó sin tiempo: se colocó una mina al azar."
                        : who + " se quedó sin tiempo (-1 vida).";
                default:
                    return string.Empty;
            }
        }

        private void ShowFailure(string message)
        {
            if (lobbyPanel != null && lobbyPanel.activeSelf)
            {
                SetText(lobbyStatus, message);
            }
            else
            {
                SetText(infoText, message);
            }
        }

        // ------------------------------------------------------- construcción de la UI

        /// <summary>Crea la pantalla online (con su propio canvas) y devuelve el componente.</summary>
        public static OnlineMatchScreen Create()
        {
            var go = new GameObject("WipedOutOnline");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var screen = go.AddComponent<OnlineMatchScreen>();
            screen.Build();
            EnsureEventSystem();
            return screen;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var es = new GameObject("EventSystem", typeof(EventSystem));
            DontDestroyOnLoad(es);
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Button entry = CreateButton(transform, "ONLINE", new Vector2(1f, 1f), new Vector2(-150f, -55f), new Vector2(240f, 70f), Open);
            entry.name = "OnlineEntryButton";

            overlay = new GameObject("OnlineOverlay", typeof(RectTransform));
            overlay.transform.SetParent(transform, false);
            overlay.AddComponent<Image>().color = Background;
            Stretch((RectTransform)overlay.transform);

            lobbyPanel = NewPanel("Lobby");
            CreateLabel(lobbyPanel.transform, "WIPED OUT? ONLINE", 80, new Vector2(0.5f, 0.5f), new Vector2(0f, 320f), new Vector2(1400f, 120f));
            lobbyStatus = CreateLabel(lobbyPanel.transform, string.Empty, 36, new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1600f, 90f));
            profileInput = CreateInput(lobbyPanel.transform, "PROFILE (opcional)", 30, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(560f, 70f), 30, InputField.ContentType.Standard);
            CreateButton(lobbyPanel.transform, "CREATE ROOM", new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(560f, 100f), OnCreateClicked);
            codeInput = CreateInput(lobbyPanel.transform, "CODE", 52, new Vector2(0.5f, 0.5f), new Vector2(-170f, -120f), new Vector2(300f, 100f), 4, InputField.ContentType.Alphanumeric);
            CreateButton(lobbyPanel.transform, "JOIN", new Vector2(0.5f, 0.5f), new Vector2(170f, -120f), new Vector2(240f, 100f), OnJoinClicked);
            CreateButton(lobbyPanel.transform, "CLOSE", new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(320f, 90f), Close);

            matchPanel = NewPanel("Match");
            roomText = CreateLabel(matchPanel.transform, string.Empty, 34, new Vector2(0.5f, 1f), new Vector2(0f, -35f), new Vector2(900f, 50f));
            levelText = CreateLabel(matchPanel.transform, string.Empty, 44, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1200f, 60f));
            livesText = CreateLabel(matchPanel.transform, string.Empty, 40, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 60f));
            phaseText = CreateLabel(matchPanel.transform, string.Empty, 44, new Vector2(0.5f, 1f), new Vector2(0f, -215f), new Vector2(1800f, 70f));
            timerText = CreateLabel(matchPanel.transform, string.Empty, 56, new Vector2(1f, 1f), new Vector2(-150f, -150f), new Vector2(240f, 80f));
            infoText = CreateLabel(matchPanel.transform, string.Empty, 32, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1800f, 60f));

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(matchPanel.transform, false);
            gridRoot = (RectTransform)gridGo.transform;
            gridRoot.anchorMin = gridRoot.anchorMax = new Vector2(0.5f, 0.5f);
            gridRoot.sizeDelta = new Vector2(700f, 700f);
            gridRoot.anchoredPosition = new Vector2(0f, -20f);
            GridLayoutGroup layout = gridGo.AddComponent<GridLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;

            validateButton = CreateButton(matchPanel.transform, "RED BUTTON: NO SAFE TILES LEFT", new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(720f, 90f), OnValidateClicked);
            validateButton.GetComponent<Image>().color = DangerColor;
            forfeitButton = CreateButton(matchPanel.transform, "FORFEIT", new Vector2(0f, 0f), new Vector2(190f, 80f), new Vector2(300f, 90f), OnForfeitClicked);
            CreateButton(matchPanel.transform, "LEAVE", new Vector2(1f, 0f), new Vector2(-190f, 80f), new Vector2(300f, 90f), OnLeaveClicked);

            overlay.SetActive(false);
        }

        private GameObject NewPanel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(overlay.transform, false);
            Stretch((RectTransform)go.transform);
            return go;
        }

        private Text CreateLabel(Transform parent, string content, int size, Vector2 anchor, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 dimensions, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Image image = go.AddComponent<Image>();
            image.color = ButtonColor;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateLabel(go.transform, label, 32, new Vector2(0.5f, 0.5f), Vector2.zero, dimensions);
            Stretch((RectTransform)text.transform);
            return button;
        }

        private InputField CreateInput(Transform parent, string hint, int fontSize, Vector2 anchor, Vector2 position, Vector2 dimensions, int limit, InputField.ContentType type)
        {
            var go = new GameObject("Input_" + hint, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Image image = go.AddComponent<Image>();
            image.color = Color.white;

            Text text = CreateLabel(go.transform, string.Empty, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero, dimensions);
            text.color = Color.black;
            text.raycastTarget = true;
            Stretch((RectTransform)text.transform);

            Text placeholder = CreateLabel(go.transform, hint, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero, dimensions);
            placeholder.color = new Color(0.55f, 0.55f, 0.55f);
            Stretch((RectTransform)placeholder.transform);

            InputField input = go.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = limit;
            input.contentType = type;
            return input;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }

    /// <summary>
    /// Arranque automático: añade el botón ONLINE a cualquier escena sin editarla. Para quitarlo basta con
    /// borrar este método (o esta clase): el resto del juego no depende de él.
    /// </summary>
    public static class OnlineBootstrap
    {
        private static OnlineMatchScreen instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (instance == null)
            {
                instance = OnlineMatchScreen.Create();
            }
        }
    }
}
