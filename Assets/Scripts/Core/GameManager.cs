using System;
using System.Collections.Generic;
using UnityEngine;
using WipedOut.Board;
using WipedOut.Models;
using WipedOut.Rules;

namespace WipedOut.Core
{
    /// <summary>
    /// Vista/controlador del cliente. NO implementa las reglas del juego: toda decisión
    /// (colocación, turnos, vidas, validación del Botón Rojo y progresión de nivel) vive en
    /// <see cref="MatchEngine"/> (WipedOut.Rules), que es la única fuente de verdad y la misma
    /// que usa el servidor de Cloud Code. Aquí solo se traduce el estado del motor a eventos y
    /// se pide al tablero que lo dibuje.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public const int MaxLives = MatchEngine.MaxLives;

        public static GameManager Instance { get; private set; }

        [Header("Configuración de niveles (obsoleta)")]
        [SerializeField, Tooltip("Ya no se usa: la progresión vive en WipedOut.Rules.LevelRules (3x3/2, 4x4/3...).")]
        private BoardConfig[] levelConfigs;

        [SerializeField, Tooltip("Inicia la partida automáticamente al cargar la escena.")]
        private bool autoStart = true;

        [SerializeField, Min(3), Tooltip("Ya no se usa: el tope de grilla vive en LevelRules.MaxGridSize.")]
        private int maxGridSize = 10;

        [Header("Referencias")]
        [SerializeField, Tooltip("Si se deja vacío, se busca automáticamente en la escena.")]
        private BoardController boardController;

        [SerializeField, Tooltip("Si se deja vacío, se busca automáticamente en la escena.")]
        private PlayerController3D player1Controller;

        [SerializeField, Tooltip("Si se deja vacío, se busca automáticamente en la escena.")]
        private PlayerController3D player2Controller;

        private MatchEngine engine;
        private BoardConfig currentConfig;
        private readonly List<BoardConfig> runtimeConfigs = new List<BoardConfig>();

        private int renderedLevel = -1;
        private int lastSeat = -1;
        private int lastLives1 = -1;
        private int lastLives2 = -1;
        private Phase lastPhase = Phase.Finished;
        private bool lastFinished;

        // ------------------------------------------------ estado derivado del motor

        /// <summary>Jugador que tiene el turno actualmente (1 o 2); 0 durante la memorización o al terminar.</summary>
        public int CurrentPlayer => engine != null ? engine.CurrentSeat : 0;

        /// <summary>Jugador que abre el nivel actual.</summary>
        public int StartingPlayer => engine != null ? engine.FirstPlacer : 1;

        /// <summary>Nivel actual, comenzando en 1.</summary>
        public int CurrentLevel => engine != null ? engine.Level : 0;

        /// <summary>Alias de <see cref="CurrentLevel"/>.</summary>
        public int CurrentRound => CurrentLevel;

        public int Player1Lives => engine != null ? engine.Lives1 : 0;
        public int Player2Lives => engine != null ? engine.Lives2 : 0;
        public int Winner => engine != null ? engine.Winner : 0;
        public int GridSize => engine != null ? engine.GridSize : 0;
        public int MinesPerPlayer => engine != null ? engine.MinesPerPlayer : 0;
        public int TotalMines => engine != null ? engine.TotalMines : 0;

        /// <summary>Casillas seguras todavía sin destapar (las que mira el Botón Rojo).</summary>
        public int RemainingSafeCells => engine != null ? engine.RemainingSafeCells : 0;

        public bool MinesVisible => engine != null && engine.MinesVisible;

        public BoardConfig CurrentConfig => currentConfig;

        /// <summary>Estado actual de la máquina de estados de la vista.</summary>
        public GameState CurrentState { get; private set; } = GameState.Preparation;

        public bool IsPlacementPhase => CurrentState == GameState.Placement;
        public bool IsPlayerTurn => CurrentState == GameState.PlayerTurn;
        public bool IsGameOver => CurrentState == GameState.GameOver;
        public int CurrentPlayerLives => GetLives(CurrentPlayer);

        /// <summary>El jugador del turno puede pulsar el Botón Rojo en la fase de búsqueda.</summary>
        public bool CanValidateNextLevel =>
            CurrentState == GameState.PlayerTurn && CurrentPlayer != 0;

        public bool HasHiddenWhiteTiles => RemainingSafeCells > 0;
        public int HiddenWhiteTilesRemaining => RemainingSafeCells;

        // --------------------------------------------------------------- eventos (UI)

        public event Action<GameState, GameState> OnStateChanged;
        public event Action<int, BoardConfig> OnRoundStarted;
        public event Action<int, BoardConfig> OnPlacementStarted;
        public event Action<int> OnGamePhaseStarted;
        public event Action<int> OnTurnChanged;
        public event Action<int, int> OnLivesChanged;
        public event Action<int> OnRoundCompleted;
        public event Action<int, int> OnValidationFailed;
        public event Action<int> OnGameOver;

        // ------------------------------- consultas al motor para que el tablero dibuje

        public bool IsMine(int cell) => engine != null && engine.IsMine(cell);
        public bool IsRevealed(int cell) => engine != null && engine.IsRevealed(cell);
        public int MineOwner(int cell) => engine != null ? engine.MineOwner(cell) : 0;
        public int PlacedBy(int seat) => engine != null ? engine.PlacedBy(seat) : 0;
        public int[] CellStates() => engine != null ? engine.CellStates() : Array.Empty<int>();
        public int[] MineCells() => engine != null ? engine.MineCells() : Array.Empty<int>();
        public int[] MineOwners() => engine != null ? engine.MineOwners() : Array.Empty<int>();

        /// <summary>Índice lineal de una casilla (fila mayor): cell = y * gridSize + x.</summary>
        public static int CellIndex(int x, int y, int gridSize) => y * gridSize + x;
        public static int CellX(int cell, int gridSize) => cell % gridSize;
        public static int CellY(int cell, int gridSize) => cell / gridSize;

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

        [ContextMenu("Iniciar partida")]
        public void StartGame()
        {
            ResolveReferences();
            ClearRuntimeConfigs();

            engine = new MatchEngine(0);
            currentConfig = null;
            renderedLevel = -1;
            lastSeat = -1;
            lastLives1 = -1;
            lastLives2 = -1;
            lastPhase = Phase.Finished;
            lastFinished = false;

            SynchronizeFromEngine();
        }

        /// <summary>Fase de colocación: el jugador del turno coloca una de sus minas.</summary>
        public PlaceOutcome PlaceMine(int playerIndex, Vector2Int position)
        {
            if (engine == null || (playerIndex != 1 && playerIndex != 2))
                return PlaceOutcome.WrongPhase;

            PlaceOutcome outcome = engine
                .PlaceMine(playerIndex, CellIndex(position.x, position.y, engine.GridSize))
                .Outcome;

            if (outcome == PlaceOutcome.Placed)
                SynchronizeFromEngine();

            return outcome;
        }

        /// <summary>Fase de búsqueda: el jugador del turno destapa una casilla.</summary>
        public RevealOutcome RevealTile(int playerIndex, Vector2Int position)
        {
            if (engine == null || (playerIndex != 1 && playerIndex != 2))
                return RevealOutcome.WrongPhase;

            RevealOutcome outcome = engine
                .Reveal(playerIndex, CellIndex(position.x, position.y, engine.GridSize))
                .Outcome;

            if (outcome == RevealOutcome.Safe || outcome == RevealOutcome.Mine)
                SynchronizeFromEngine();

            return outcome;
        }

        /// <summary>Botón Rojo: el jugador del turno afirma que ya no quedan casillas seguras.</summary>
        public ValidateOutcome ValidateNextLevel()
        {
            if (!CanValidateNextLevel || engine == null)
                return ValidateOutcome.NotYourTurn;

            int completedLevel = engine.Level;
            ValidateOutcome outcome = engine.Validate(engine.CurrentSeat).Outcome;

            if (outcome == ValidateOutcome.LevelCompleted)
                OnRoundCompleted?.Invoke(completedLevel);
            else if (outcome == ValidateOutcome.TooEarly)
                OnValidationFailed?.Invoke(engine.CurrentSeat, GetLives(engine.CurrentSeat));

            SynchronizeFromEngine();
            return outcome;
        }

        /// <summary>Comando de confirmación del Botón Rojo.</summary>
        public void ConfirmBoard()
        {
            ValidateNextLevel();
        }

        [ContextMenu("Confirmar tablero")]
        public void OnNextLevelButtonPressed()
        {
            ConfirmBoard();
        }

        public int GetLives(int playerIndex)
        {
            if (playerIndex == 1)
                return Player1Lives;
            if (playerIndex == 2)
                return Player2Lives;
            return 0;
        }

        // ------------------------------------------------- sincronización vista/motor

        private void SynchronizeFromEngine()
        {
            if (engine == null)
                return;

            bool levelChanged = engine.Level != renderedLevel;
            if (levelChanged)
            {
                renderedLevel = engine.Level;
                EnsureConfig(engine.Level);

                if (boardController == null)
                    ResolveReferences();

                if (boardController != null)
                    boardController.GenerateBoard3D(engine.GridSize);
                else
                    Debug.LogError("GameManager: no se encontró ningún BoardController en la escena.", this);

                ConfigurePlayerControllers(engine.GridSize);
                OnRoundStarted?.Invoke(engine.Level, currentConfig);
            }

            // Modo local: no hay temporizador de memorización; las minas se ocultan al instante.
            if (engine.Phase == Phase.Memorize)
                engine.HideMines();

            Phase phase = engine.Phase;

            if (phase != lastPhase || levelChanged)
            {
                if (phase == Phase.Placement)
                    OnPlacementStarted?.Invoke(engine.Level, currentConfig);
                else if (phase == Phase.Search)
                    OnGamePhaseStarted?.Invoke(engine.Level);

                lastPhase = phase;
            }

            if (engine.Lives1 != lastLives1)
            {
                lastLives1 = engine.Lives1;
                OnLivesChanged?.Invoke(1, engine.Lives1);
            }

            if (engine.Lives2 != lastLives2)
            {
                lastLives2 = engine.Lives2;
                OnLivesChanged?.Invoke(2, engine.Lives2);
            }

            if (engine.CurrentSeat != lastSeat)
            {
                lastSeat = engine.CurrentSeat;
                OnTurnChanged?.Invoke(engine.CurrentSeat);
            }

            bool finished = engine.IsFinished;
            if (finished && !lastFinished)
            {
                lastFinished = true;
                OnGameOver?.Invoke(engine.Winner);
            }
            else if (!finished)
            {
                lastFinished = false;
            }

            SetState(ToState(phase), levelChanged);

            if (boardController != null)
                boardController.ApplyLocalState(this);
        }

        private static GameState ToState(Phase phase)
        {
            switch (phase)
            {
                case Phase.Placement:
                    return GameState.Placement;
                case Phase.Memorize:
                    return GameState.Memorization;
                case Phase.Search:
                    return GameState.PlayerTurn;
                default:
                    return GameState.GameOver;
            }
        }

        private void EnsureConfig(int level)
        {
            BoardConfig config = ScriptableObject.CreateInstance<BoardConfig>();
            config.name = "Level" + level + "_Runtime";
            config.hideFlags = HideFlags.DontSave;
            config.gridWidth = LevelRules.GridSize(level);
            config.gridHeight = LevelRules.GridSize(level);
            config.mineCount = LevelRules.MinesPerPlayer(level);
            config.memorizationTime = 0f;

            runtimeConfigs.Add(config);
            currentConfig = config;
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

        private void ConfigurePlayerControllers(int gridSize)
        {
            if (player1Controller != null)
                player1Controller.SetGridSize(gridSize, gridSize, true);

            if (player2Controller != null)
                player2Controller.SetGridSize(gridSize, gridSize, true);
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
            int current = engine != null ? engine.CurrentSeat : 0;
            bool active = CurrentState == GameState.Placement || CurrentState == GameState.PlayerTurn;

            if (player1Controller != null)
                player1Controller.SetInputEnabled(active && current == 1);

            if (player2Controller != null)
                player2Controller.SetInputEnabled(active && current == 2);
        }
    }
}
