using System;
using System.Collections;
using UnityEngine;
using WipedOut.Core;
using WipedOut.Models;

namespace WipedOut.Board
{
    /// <summary>
    /// Genera el tablero 3D, gestiona la colocación manual de minas,
    /// el destape de casillas y las consultas usadas por GameManager.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoardController : MonoBehaviour
    {
        [Header("Tablero")]
        [SerializeField, Tooltip("Padre opcional para las casillas generadas.")]
        private Transform boardRoot;

        [SerializeField, Tooltip("Prefab opcional de casilla. Si está vacío se usa un cubo primitivo.")]
        private GameObject tilePrefab;

        [SerializeField, Min(0.1f)] private float tileSize = 1f;
        [SerializeField, Min(0f)] private float tileSpacing = 0.08f;
        [SerializeField, Min(0.01f)] private float tileHeight = 0.2f;

        [Header("Colores")]
        [SerializeField] private Color hiddenTileColor = new Color(0.92f, 0.92f, 0.92f);
        [SerializeField] private Color revealedSafeColor = new Color(0.35f, 0.78f, 0.45f);
        [SerializeField] private Color player1MineColor = new Color(0.2f, 0.5f, 1f);
        [SerializeField] private Color player2MineColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color triggeredMineColor = new Color(1f, 0.5f, 0f);
        [SerializeField] private Color player1SelectionColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color player2SelectionColor = new Color(0.2f, 1f, 0.9f);
        [SerializeField] private Color invalidPlacementColor = new Color(1f, 0f, 1f);

        [Header("Feedback")]
        [SerializeField, Min(0f)] private float invalidFlashDuration = 0.15f;
        [SerializeField, Range(0f, 1f)] private float selectionTint = 0.55f;

        [Header("Referencias (opcional)")]
        [SerializeField] private PlayerController3D player1Controller;
        [SerializeField] private PlayerController3D player2Controller;

        /// <summary>Se dispara al terminar de generar el tablero.</summary>
        public event Action<int, int> OnBoardGenerated;

        /// <summary>Jugador, posición, minas colocadas y total requerido.</summary>
        public event Action<int, Vector2Int, int, int> OnMinePlaced;

        /// <summary>Se dispara cuando una colocación es rechazada.</summary>
        public event Action<int, Vector2Int, string> OnPlacementRejected;

        /// <summary>Se dispara cuando ambos jugadores terminaron de colocar sus minas.</summary>
        public event Action OnPlacementCompleted;

        /// <summary>Jugador, posición y si la casilla contenía una mina.</summary>
        public event Action<int, Vector2Int, bool> OnTileRevealed;

        /// <summary>Se dispara cuando se revela la última casilla blanca del nivel.</summary>
        public event Action OnAllWhiteTilesRevealed;

        public BoardConfig CurrentConfig { get; private set; }
        public int GridWidth { get; private set; }
        public int GridHeight { get; private set; }
        public int MinesPerPlayer { get; private set; }
        public int Player1MinesPlaced { get; private set; }
        public int Player2MinesPlaced { get; private set; }
        public int TotalMinesPlaced => Player1MinesPlaced + Player2MinesPlaced;
        public bool IsPlacementComplete { get; private set; }
        public bool IsGamePhaseActive { get; private set; }
        public bool HasBoard => tiles != null;

        private TileData[,] tiles;
        private Transform tilesRoot;
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Vector2Int player1Selection;
        private Vector2Int player2Selection;
        private bool hasPlayer1Selection;
        private bool hasPlayer2Selection;
        private bool allWhiteTilesRevealedNotified;

        private void Awake()
        {
            ResolveControllers();
        }

        private void OnEnable()
        {
            ResolveControllers();
            SubscribeToControllers();
        }

        private void Start()
        {
            if (player1Controller == null || player2Controller == null)
            {
                ResolveControllers();
                SubscribeToControllers();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromControllers();
        }

        /// <summary>
        /// Genera una grilla vacía para un nuevo nivel. Las minas se colocan después
        /// durante la Fase de Colocación.
        /// </summary>
        public void GenerateBoard3D(BoardConfig config)
        {
            if (config == null)
            {
                Debug.LogError("BoardController: se recibió un BoardConfig nulo.", this);
                return;
            }

            if (player1Controller == null || player2Controller == null)
            {
                ResolveControllers();
                SubscribeToControllers();
            }

            CurrentConfig = config;
            int width = Mathf.Max(1, config.gridWidth);
            int height = Mathf.Max(1, config.gridHeight);

            MinesPerPlayer = Mathf.Max(0, config.mineCount);
            Player1MinesPlaced = 0;
            Player2MinesPlaced = 0;
            IsPlacementComplete = false;
            IsGamePhaseActive = false;
            allWhiteTilesRevealedNotified = false;

            if (MinesPerPlayer * 2 > width * height)
            {
                Debug.LogWarning(
                    "BoardController: el nivel solicita más minas que casillas disponibles.",
                    this);
            }

            BuildBoard(width, height);
            CaptureSelections();
            RefreshAllTiles();

            OnBoardGenerated?.Invoke(width, height);
        }

        /// <summary>Oculta las minas al comenzar la Fase de Juego.</summary>
        public void BeginGamePhase()
        {
            IsPlacementComplete = true;
            IsGamePhaseActive = true;
            RefreshAllTiles();
        }

        /// <summary>
        /// Intenta colocar una mina del jugador indicado en la casilla indicada.
        /// </summary>
        public bool TryPlaceMine(int playerIndex, Vector2Int position)
        {
            GameManager gameManager = GameManager.Instance;

            if (gameManager == null || gameManager.CurrentState != GameState.Placement)
                return false;

            if (IsPlacementComplete || (playerIndex != 1 && playerIndex != 2))
                return false;

            if (MinesPerPlayer <= 0)
                return false;

            TileData tile = GetTile(position);

            if (tile == null)
            {
                RejectPlacement(playerIndex, position, "La posición está fuera del tablero.");
                return false;
            }

            if (tile.isMine)
            {
                RejectPlacement(playerIndex, position, "La casilla ya contiene una mina.");
                return false;
            }

            if (GetMinesPlaced(playerIndex) >= MinesPerPlayer)
            {
                RejectPlacement(playerIndex, position, "Ya colocaste todas tus minas.");
                return false;
            }

            tile.SetMine(playerIndex);
            tile.isRevealed = false;

            if (playerIndex == 1)
                Player1MinesPlaced++;
            else
                Player2MinesPlaced++;

            RefreshTile(tile);
            OnMinePlaced?.Invoke(
                playerIndex,
                position,
                GetMinesPlaced(playerIndex),
                MinesPerPlayer);

            if (Player1MinesPlaced >= MinesPerPlayer && Player2MinesPlaced >= MinesPerPlayer)
                CompletePlacement();

            return true;
        }

        /// <summary>
        /// Destapa una casilla durante la Fase de Juego. Solo el jugador con el turno
        /// actual puede hacerlo y solo una vez por casilla.
        /// </summary>
        public bool TryRevealTile(int playerIndex, Vector2Int position)
        {
            GameManager gameManager = GameManager.Instance;

            if (gameManager == null ||
                gameManager.CurrentState != GameState.PlayerTurn ||
                !IsGamePhaseActive)
            {
                return false;
            }

            if (playerIndex != gameManager.CurrentPlayer)
                return false;

            TileData tile = GetTile(position);

            if (tile == null || tile.isRevealed)
                return false;

            bool isMine = tile.isMine;
            tile.isRevealed = true;

            RefreshTile(tile);
            gameManager.ResolveTile(isMine);

            OnTileRevealed?.Invoke(playerIndex, position, isMine);

            if (gameManager.CurrentState == GameState.PlayerTurn)
                NotifyIfAllWhiteTilesRevealed();

            return true;
        }

        /// <summary>
        /// Cuenta las casillas blancas pendientes: seguras y todavía sin destapar.
        /// Las minas ocultas no cuentan como casillas libres pendientes.
        /// </summary>
        public int CountHiddenWhiteTiles()
        {
            if (tiles == null)
                return 0;

            int count = 0;

            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    if (tiles[x, y] != null && tiles[x, y].IsHiddenWhite)
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Devuelve true si todavía queda alguna casilla blanca (segura y sin destapar).
        /// </summary>
        public bool HasHiddenWhiteTiles()
        {
            return CountHiddenWhiteTiles() > 0;
        }

        /// <summary>
        /// Devuelve true si el tablero existe y ya no quedan casillas blancas por revelar.
        /// </summary>
        public bool IsBoardCompleted()
        {
            return HasBoard && CountHiddenWhiteTiles() == 0;
        }

        /// <summary>Revela todas las minas (se usa al terminar la partida).</summary>
        public void RevealAllMines()
        {
            if (tiles == null)
                return;

            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    TileData tile = tiles[x, y];

                    if (tile != null && tile.isMine)
                        tile.isRevealed = true;
                }
            }

            RefreshAllTiles();
        }

        /// <summary>Devuelve los datos de una casilla o null si está fuera del tablero.</summary>
        public TileData GetTile(Vector2Int position)
        {
            if (tiles == null)
                return null;

            if (position.x < 0 || position.y < 0 ||
                position.x >= GridWidth || position.y >= GridHeight)
            {
                return null;
            }

            return tiles[position.x, position.y];
        }

        /// <summary>Devuelve cuántas minas lleva colocadas el jugador indicado.</summary>
        public int GetMinesPlaced(int playerIndex)
        {
            if (playerIndex == 1)
                return Player1MinesPlaced;

            if (playerIndex == 2)
                return Player2MinesPlaced;

            return 0;
        }

        private void NotifyIfAllWhiteTilesRevealed()
        {
            if (allWhiteTilesRevealedNotified || tiles == null)
                return;

            if (CountHiddenWhiteTiles() != 0)
                return;

            allWhiteTilesRevealedNotified = true;
            OnAllWhiteTilesRevealed?.Invoke();
        }

        private void CompletePlacement()
        {
            if (IsPlacementComplete)
                return;

            IsPlacementComplete = true;
            OnPlacementCompleted?.Invoke();

            if (GameManager.Instance != null)
                GameManager.Instance.NotifyPlacementComplete();
            else
                Debug.LogError(
                    "BoardController: no se encontró GameManager para iniciar la Fase de Juego.",
                    this);
        }

        private void HandleSelectionChanged(PlayerController3D controller, Vector2Int position)
        {
            if (controller == null)
                return;

            if (controller.PlayerIndex == 1)
            {
                player1Selection = position;
                hasPlayer1Selection = true;
            }
            else if (controller.PlayerIndex == 2)
            {
                player2Selection = position;
                hasPlayer2Selection = true;
            }

            RefreshAllTiles();
        }

        private void HandleTileSelected(PlayerController3D controller, Vector2Int position)
        {
            if (controller == null || GameManager.Instance == null)
                return;

            GameState state = GameManager.Instance.CurrentState;

            if (state == GameState.Placement)
                TryPlaceMine(controller.PlayerIndex, position);
            else if (state == GameState.PlayerTurn)
                TryRevealTile(controller.PlayerIndex, position);
        }

        private void BuildBoard(int width, int height)
        {
            GridWidth = width;
            GridHeight = height;

            ClearBoard();
            tiles = new TileData[width, height];

            Transform root = GetTilesRoot();
            float step = Mathf.Max(0.01f, tileSize + tileSpacing);
            float offsetX = (width - 1) * step * 0.5f;
            float offsetZ = (height - 1) * step * 0.5f;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 localPosition = new Vector3(
                        x * step - offsetX,
                        0f,
                        y * step - offsetZ);

                    GameObject tileObject = CreateTileObject(root, localPosition, x, y);

                    tiles[x, y] = new TileData(new Vector2Int(x, y))
                    {
                        tileInstance = tileObject
                    };
                }
            }
        }

        private GameObject CreateTileObject(Transform root, Vector3 localPosition, int x, int y)
        {
            GameObject tileObject;

            if (tilePrefab != null)
            {
                tileObject = Instantiate(tilePrefab, root);
                tileObject.transform.localPosition = localPosition;
            }
            else
            {
                tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tileObject.transform.SetParent(root, false);
                tileObject.transform.localScale = new Vector3(tileSize, tileHeight, tileSize);
                tileObject.transform.localPosition =
                    localPosition + Vector3.up * (tileHeight * 0.5f);
            }

            tileObject.name = "Tile_" + x + "_" + y;
            return tileObject;
        }

        private Transform GetTilesRoot()
        {
            if (tilesRoot != null)
                return tilesRoot;

            Transform parent = boardRoot != null ? boardRoot : transform;
            GameObject rootObject = new GameObject("GeneratedTiles");
            rootObject.transform.SetParent(parent, false);
            tilesRoot = rootObject.transform;

            return tilesRoot;
        }

        private void ClearBoard()
        {
            if (tilesRoot != null)
            {
                tilesRoot.gameObject.SetActive(false);
                Destroy(tilesRoot.gameObject);
            }

            tilesRoot = null;
            tiles = null;
        }

        private void CaptureSelections()
        {
            hasPlayer1Selection = player1Controller != null;
            hasPlayer2Selection = player2Controller != null;

            if (hasPlayer1Selection)
                player1Selection = player1Controller.SelectedTile;

            if (hasPlayer2Selection)
                player2Selection = player2Controller.SelectedTile;
        }

        private void RefreshAllTiles()
        {
            if (tiles == null)
                return;

            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                    RefreshTile(tiles[x, y]);
            }
        }

        private void RefreshTile(TileData tile)
        {
            if (tile == null)
                return;

            ApplyTileColor(tile, GetTileColor(tile));
        }

        private Color GetTileColor(TileData tile)
        {
            Color color;

            if (tile.isMine)
            {
                if (tile.isRevealed)
                {
                    color = triggeredMineColor;
                }
                else if (!IsGamePhaseActive)
                {
                    color = tile.mineOwner == 1
                        ? player1MineColor
                        : player2MineColor;
                }
                else
                {
                    // Durante la Fase de Juego las minas se ocultan y lucen como casillas blancas.
                    color = hiddenTileColor;
                }
            }
            else
            {
                color = tile.isRevealed ? revealedSafeColor : hiddenTileColor;
            }

            if (hasPlayer1Selection && tile.gridPosition == player1Selection)
                color = Color.Lerp(color, player1SelectionColor, selectionTint);

            if (hasPlayer2Selection && tile.gridPosition == player2Selection)
                color = Color.Lerp(color, player2SelectionColor, selectionTint);

            return color;
        }

        private void ApplyTileColor(TileData tile, Color color)
        {
            if (tile == null || tile.tileInstance == null)
                return;

            Renderer tileRenderer = tile.tileInstance.GetComponent<Renderer>();

            if (tileRenderer == null)
                return;

            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            tileRenderer.SetPropertyBlock(propertyBlock);
        }

        private void RejectPlacement(int playerIndex, Vector2Int position, string reason)
        {
            OnPlacementRejected?.Invoke(playerIndex, position, reason);
            Debug.LogWarning(
                "BoardController: el Jugador " + playerIndex +
                " no puede colocar una mina en " + position + ". " + reason,
                this);

            StartCoroutine(FlashInvalidTile(position));
        }

        private IEnumerator FlashInvalidTile(Vector2Int position)
        {
            TileData tile = GetTile(position);

            if (tile == null || tile.tileInstance == null)
                yield break;

            ApplyTileColor(tile, invalidPlacementColor);
            yield return new WaitForSeconds(invalidFlashDuration);

            if (tile != null && tile.tileInstance != null)
                RefreshTile(tile);
        }

        private void ResolveControllers()
        {
            if (player1Controller != null && player2Controller != null)
                return;

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

        private void SubscribeToControllers()
        {
            if (player1Controller != null)
            {
                player1Controller.OnSelectionChanged -= HandleSelectionChanged;
                player1Controller.OnSelectionChanged += HandleSelectionChanged;
                player1Controller.OnTileSelected -= HandleTileSelected;
                player1Controller.OnTileSelected += HandleTileSelected;
            }

            if (player2Controller != null)
            {
                player2Controller.OnSelectionChanged -= HandleSelectionChanged;
                player2Controller.OnSelectionChanged += HandleSelectionChanged;
                player2Controller.OnTileSelected -= HandleTileSelected;
                player2Controller.OnTileSelected += HandleTileSelected;
            }
        }

        private void UnsubscribeFromControllers()
        {
            if (player1Controller != null)
            {
                player1Controller.OnSelectionChanged -= HandleSelectionChanged;
                player1Controller.OnTileSelected -= HandleTileSelected;
            }

            if (player2Controller != null)
            {
                player2Controller.OnSelectionChanged -= HandleSelectionChanged;
                player2Controller.OnTileSelected -= HandleTileSelected;
            }
        }
    }
}
