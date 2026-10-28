using System;
using System.Collections.Generic;
using UnityEngine;
using WipedOut.Board;
using WipedOut.Models;

namespace WipedOut.Core
{
    /// <summary>
    /// Administra el estado de la partida: colocación manual de minas, turnos,
    /// vidas, validación de final de nivel y avance de nivel.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public const int MaxLives = 3;

        public static GameManager Instance { get; private set; }

        [Header("Configuración de niveles")]
        [SerializeField, Tooltip("Configuraciones opcionales por nivel. Si faltan, se generan automáticamente: 3x3/2, 4x4/3, 5x5/4, etc.")]
        private BoardConfig[] levelConfigs;

        [SerializeField, Tooltip("Inicia la partida automáticamente al cargar la escena.")]
        private bool autoStart = true;

        [SerializeField, Min(3), Tooltip("Tamaño máximo de grilla para los niveles generados automáticamente.")]
        private int maxGridSize = 10;

        [Header("Referencias")]
        [SerializeField, Tooltip("Si se deja vacío, se busca automáticamente en la escena.")]
        private BoardController boardController;

        [SerializeField, Tooltip("Si se deja vacío, se busca automáticamente en la escena.")]
        private PlayerController3D player1Controller;

        [SerializeField, Tooltip("Si se deja vacío, se busca automáticamente en la escena.")]
        private PlayerController3D player2Controller;

        /// <summary>Jugador que inicia la fase de juego del nivel actual: 1 o 2.</summary>
        public int StartingPlayer { get; private set; } = 1;

        /// <summary>Jugador encargado de validar el final del nivel. En este flujo siempre es el Jugador 1.</summary>
        public int NextLevelPlayer { get; private set; } = 1;

        /// <summary>Jugador que tiene el turno actualmente: 1 o 2.</summary>
        public int CurrentPlayer { get; private set; } = 1;

        /// <summary>Nivel actual, comenzando en 1.</summary>
        public int CurrentLevel { get; private set; }

        /// <summary>Alias de <see cref="CurrentLevel"/>.</summary>
        public int CurrentRound => CurrentLevel;

        /// <summary>Estado actual de la máquina de estados.</summary>
        public GameState CurrentState { get; private set; } = GameState.Preparation;

        /// <summary>Vidas restantes del Jugador 1.</summary>
        public int Player1Lives { get; private set; } = MaxLives;

        /// <summary>Vidas restantes del Jugador 2.</summary>
        public int Player2Lives { get; private set; } = MaxLives;

        /// <summary>Jugador ganador: 1, 2 o 0 si todavía no hay ganador o hay empate.</summary>
        public int Winner { get; private set; }

        /// <summary>Configuración del tablero usada en el nivel actual.</summary>
        public BoardConfig CurrentConfig { get; private set; }

        /// <summary>Minas que debe colocar cada jugador en el nivel actual.</summary>
        public int MinesPerPlayer => CurrentConfig != null ? Mathf.Max(0, CurrentConfig.mineCount) : 0;

        /// <summary>Total de minas del nivel actual.</summary>
        public int TotalMines => MinesPerPlayer * 2;

        /// <summary>True durante la Fase de Colocación.</summary>
        public bool IsPlacementPhase => CurrentState == GameState.Placement;

        /// <summary>True durante la Fase de Juego.</summary>
        public bool IsPlayerTurn => CurrentState == GameState.PlayerTurn;

        /// <summary>True cuando la partida terminó.</summary>
        public bool IsGameOver => CurrentState == GameState.GameOver;

        /// <summary>Vidas del jugador que tiene el turno.</summary>
        public int CurrentPlayerLives => GetLives(CurrentPlayer);

        /// <summary>
        /// El Jugador 1 puede validar el tablero durante la Fase de Juego,
        /// sin importar de quién sea el turno actual.
        /// </summary>
        public bool CanValidateNextLevel =>
            CurrentState == GameState.PlayerTurn && NextLevelPlayer == 1;

        /// <summary>True si todavía queda alguna casilla blanca (segura y sin destapar).</summary>
        public bool HasHiddenWhiteTiles
        {
            get
            {
                if (boardController != null)
                    return !boardController.HasBoard || boardController.HasHiddenWhiteTiles();

                if (HiddenTilesQuery != null)
                    return HiddenTilesQuery();

                return true;
            }
        }

        /// <summary>Cantidad de casillas blancas pendientes de revelar.</summary>
        public int HiddenWhiteTilesRemaining
        {
            get
            {
                if (boardController != null && boardController.HasBoard)
                    return boardController.CountHiddenWhiteTiles();

                if (HiddenTilesQuery != null)
                    return HiddenTilesQuery() ? 1 : 0;

                return 0;
            }
        }

        /// <summary>
        /// Consulta opcional de respaldo si no hay BoardController asignado.
        /// Debe devolver true si queda alguna casilla blanca sin destapar.
        /// </summary>
        public Func<bool> HiddenTilesQuery { get; set; }

        /// <summary>Se dispara cuando cambia el estado de la partida.</summary>
        public event Action<GameState, GameState> OnStateChanged;

        /// <summary>Se dispara al comenzar un nivel. En este punto el tablero se regenera.</summary>
        public event Action<int, BoardConfig> OnRoundStarted;

        /// <summary>Se dispara al entrar en la Fase de Colocación.</summary>
        public event Action<int, BoardConfig> OnPlacementStarted;

        /// <summary>Se dispara al comenzar la Fase de Juego.</summary>
        public event Action<int> OnGamePhaseStarted;

        /// <summary>Se dispara al comenzar el turno de un jugador.</summary>
        public event Action<int> OnTurnChanged;

        /// <summary>Se dispara cuando cambian las vidas de un jugador.</summary>
        public event Action<int, int> OnLivesChanged;

        /// <summary>Se dispara cuando se completa correctamente un nivel.</summary>
        public event Action<int> OnRoundCompleted;

        /// <summary>Se dispara cuando falla la validación de final de nivel.</summary>
        public event Action<int, int> OnValidationFailed;

        /// <summary>Se dispara al terminar la partida con el jugador ganador (0 = empate).</summary>
        public event Action<int> OnGameOver;

        private readonly List<BoardConfig> runtimeConfigs = new List<BoardConfig>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("GameManager: ya existe otra instancia. Se destruye el duplicado.", this);
                enabled = false;
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveReferences();

            Player1Lives = MaxLives;
            Player2Lives = MaxLives;

            RefreshPlayerInput();
        }

        private void Start()
        {
            if (autoStart)
                StartGame();
        }

        private void OnDestroy()
        {
            ClearRuntimeConfigs();

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Reinicia vidas y niveles, y comienza una partida nueva.
        /// </summary>
        [ContextMenu("Iniciar partida")]
        public void StartGame()
        {
            ResolveReferences();
            ClearRuntimeConfigs();

            Winner = 0;
            CurrentLevel = 0;
            CurrentPlayer = 1;
            StartingPlayer = 1;
            NextLevelPlayer = 1;
            CurrentConfig = null;

            Player1Lives = MaxLives;
            Player2Lives = MaxLives;

            OnLivesChanged?.Invoke(1, Player1Lives);
            OnLivesChanged?.Invoke(2, Player2Lives);

            StartNextLevel();
        }

        /// <summary>
        /// El tablero llama a este método cuando ambos jugadores terminaron
        /// de colocar todas sus minas.
        /// </summary>
        public void NotifyPlacementComplete()
        {
            if (CurrentState != GameState.Placement)
                return;

            CurrentPlayer = StartingPlayer;

            if (boardController != null)
                boardController.BeginGamePhase();

            SetState(GameState.PlayerTurn);
            OnGamePhaseStarted?.Invoke(CurrentLevel);
            OnTurnChanged?.Invoke(CurrentPlayer);
        }

        /// <summary>
        /// Termina el turno actual y pasa al otro jugador.
        /// </summary>
        public void EndTurn()
        {
            if (CurrentState != GameState.PlayerTurn)
                return;

            SwitchTurn();
        }

        /// <summary>
        /// Resuelve la casilla elegida por el jugador actual.
        /// Si es una mina, descuenta una vida antes de cambiar de turno.
        /// </summary>
        public void ResolveTile(bool isMine)
        {
            if (CurrentState != GameState.PlayerTurn)
                return;

            if (isMine)
            {
                LoseLife(CurrentPlayer);

                if (CurrentState == GameState.GameOver)
                    return;
            }

            EndTurn();
        }

        /// <summary>
        /// Descuenta una vida al jugador indicado y comprueba si la partida termina.
        /// </summary>
        /// <returns>Vidas restantes del jugador.</returns>
        public int LoseLife(int playerIndex)
        {
            if (playerIndex != 1 && playerIndex != 2)
                return 0;

            if (CurrentState == GameState.GameOver)
                return GetLives(playerIndex);

            if (playerIndex == 1)
                Player1Lives = Mathf.Max(0, Player1Lives - 1);
            else
                Player2Lives = Mathf.Max(0, Player2Lives - 1);

            int remainingLives = GetLives(playerIndex);
            OnLivesChanged?.Invoke(playerIndex, remainingLives);

            if (remainingLives <= 0)
                CheckGameOver();

            return remainingLives;
        }

        /// <summary>Devuelve las vidas restantes del jugador indicado (1 o 2).</summary>
        public int GetLives(int playerIndex)
        {
            if (playerIndex == 1)
                return Player1Lives;

            if (playerIndex == 2)
                return Player2Lives;

            return 0;
        }

        /// <summary>
        /// Valida el tablero con el estado real del BoardController.
        /// Solo el Jugador 1 puede ejecutar esta validación.
        /// </summary>
        public void ValidateNextLevel()
        {
            if (!CanValidateNextLevel)
                return;

            if (boardController != null)
            {
                if (!boardController.HasBoard)
                {
                    Debug.LogWarning(
                        "GameManager: el tablero todavía no está generado para validar el nivel.",
                        this);
                    return;
                }

                ValidateNextLevel(boardController.IsBoardCompleted());
                return;
            }

            if (HiddenTilesQuery != null)
            {
                ValidateNextLevel(!HiddenTilesQuery());
                return;
            }

            Debug.LogWarning(
                "GameManager: no hay BoardController ni HiddenTilesQuery para validar el final del nivel.",
                this);
        }

        /// <summary>
        /// Comando de confirmación del Jugador 1 para validar el tablero.
        /// </summary>
        public void ConfirmBoard()
        {
            ValidateNextLevel();
        }

        /// <summary>
        /// Método listo para enlazar al OnClick del botón de confirmación del Jugador 1.
        /// </summary>
        [ContextMenu("Confirmar tablero")]
        public void OnNextLevelButtonPressed()
        {
            ConfirmBoard();
        }

        /// <summary>
        /// Valida el final del nivel con el resultado ya calculado.
        /// </summary>
        /// <param name="hiddenWhiteTilesRemain">True si todavía queda alguna casilla blanca sin destapar.</param>
        public void ValidateNextLevel(bool hiddenWhiteTilesRemain)
        {
            if (!CanValidateNextLevel)
                return;

            SetState(GameState.Validation);

            if (!hiddenWhiteTilesRemain)
            {
                // Tablero completo: se avanza al siguiente nivel con una grilla más grande
                // y más minas por jugador.
                OnRoundCompleted?.Invoke(CurrentLevel);
                StartNextLevel();
                return;
            }

            // El comando pertenece al Jugador 1, por lo que la penalización es para él.
            int pressingPlayer = NextLevelPlayer;
            LoseLife(pressingPlayer);
            OnValidationFailed?.Invoke(pressingPlayer, GetLives(pressingPlayer));

            // La partida continúa con el mismo nivel y el mismo turno.
            if (CurrentState != GameState.GameOver)
                SetState(GameState.PlayerTurn);
        }

        private void StartNextLevel()
        {
            CurrentLevel++;
            CurrentConfig = GetConfigForRound(CurrentLevel);

            if (CurrentConfig == null)
            {
                Debug.LogError(
                    "GameManager: no se pudo obtener la configuración del nivel " + CurrentLevel + ".",
                    this);
                SetState(GameState.Preparation, true);
                return;
            }

            StartingPlayer = (CurrentLevel % 2 == 1) ? 1 : 2;
            NextLevelPlayer = 1;
            CurrentPlayer = StartingPlayer;

            SetState(GameState.Preparation, true);

            ConfigurePlayerControllers(CurrentConfig);
            OnRoundStarted?.Invoke(CurrentLevel, CurrentConfig);

            if (boardController == null)
                ResolveReferences();

            if (boardController != null)
                boardController.GenerateBoard3D(CurrentConfig);
            else
                Debug.LogError("GameManager: no se encontró ningún BoardController en la escena.", this);

            SetState(GameState.Placement);
            OnPlacementStarted?.Invoke(CurrentLevel, CurrentConfig);

            // Un nivel sin minas no necesita Fase de Colocación.
            if (MinesPerPlayer <= 0)
                NotifyPlacementComplete();
        }

        private void SwitchTurn()
        {
            CurrentPlayer = CurrentPlayer == 1 ? 2 : 1;
            RefreshPlayerInput();
            OnTurnChanged?.Invoke(CurrentPlayer);
        }

        private void CheckGameOver()
        {
            if (Player1Lives > 0 && Player2Lives > 0)
                return;

            int winner;

            if (Player1Lives <= 0 && Player2Lives <= 0)
                winner = 0;
            else if (Player1Lives <= 0)
                winner = 2;
            else
                winner = 1;

            EndGame(winner);
        }

        private void EndGame(int winner)
        {
            if (CurrentState == GameState.GameOver)
                return;

            Winner = winner;

            if (boardController != null)
                boardController.RevealAllMines();

            SetState(GameState.GameOver, true);
            OnGameOver?.Invoke(Winner);
        }

        private BoardConfig GetConfigForRound(int level)
        {
            if (levelConfigs != null && level >= 1 && level <= levelConfigs.Length)
            {
                BoardConfig configured = levelConfigs[level - 1];

                if (configured != null)
                    return configured;
            }

            return CreateProceduralConfig(level);
        }

        /// <summary>
        /// Genera la configuración de un nivel cuando no hay un BoardConfig explícito.
        /// Nivel 1: 3x3 y 2 minas por jugador. Cada nivel siguiente suma una fila,
        /// una columna y una mina por jugador.
        /// </summary>
        private BoardConfig CreateProceduralConfig(int level)
        {
            string runtimeName = "Level" + level + "_Runtime";

            foreach (BoardConfig existing in runtimeConfigs)
            {
                if (existing != null && existing.name == runtimeName)
                    return existing;
            }

            int size = Mathf.Clamp(level + 2, 3, Mathf.Max(3, maxGridSize));
            int maxMinesPerPlayer = Mathf.Max(1, (size * size - 1) / 2);
            int minesPerPlayer = Mathf.Clamp(level + 1, 1, maxMinesPerPlayer);

            BoardConfig config = ScriptableObject.CreateInstance<BoardConfig>();
            config.name = runtimeName;
            config.hideFlags = HideFlags.DontSave;
            config.gridWidth = size;
            config.gridHeight = size;
            config.mineCount = minesPerPlayer;
            config.memorizationTime = 0f;

            runtimeConfigs.Add(config);
            return config;
        }

        private void ClearRuntimeConfigs()
        {
            foreach (BoardConfig config in runtimeConfigs)
            {
                if (config == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(config);
                else
                    DestroyImmediate(config);
            }

            runtimeConfigs.Clear();
        }

        private void ConfigurePlayerControllers(BoardConfig config)
        {
            if (player1Controller != null)
                player1Controller.SetGridSize(config.gridWidth, config.gridHeight, true);

            if (player2Controller != null)
                player2Controller.SetGridSize(config.gridWidth, config.gridHeight, true);
        }

        private void ResolveReferences()
        {
            if (boardController == null)
                boardController = FindFirstObjectByType<BoardController>();

            if (player1Controller == null || player2Controller == null)
            {
                PlayerController3D[] controllers =
                    FindObjectsByType<PlayerController3D>(FindObjectsSortMode.None);

                foreach (PlayerController3D controller in controllers)
                {
                    if (controller.PlayerIndex == 1 && player1Controller == null)
                        player1Controller = controller;
                    else if (controller.PlayerIndex == 2 && player2Controller == null)
                        player2Controller = controller;
                }
            }
        }

        private void SetState(GameState newState, bool forceNotify = false)
        {
            GameState previousState = CurrentState;
            CurrentState = newState;
            RefreshPlayerInput();

            if (forceNotify || previousState != newState)
                OnStateChanged?.Invoke(previousState, newState);
        }

        private void RefreshPlayerInput()
        {
            bool placement = CurrentState == GameState.Placement;
            bool playerTurn = CurrentState == GameState.PlayerTurn;

            if (player1Controller != null)
                player1Controller.SetInputEnabled(placement || (playerTurn && CurrentPlayer == 1));

            if (player2Controller != null)
                player2Controller.SetInputEnabled(placement || (playerTurn && CurrentPlayer == 2));
        }
    }
}
