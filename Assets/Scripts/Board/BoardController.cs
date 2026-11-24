using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.EventSystems;
using WipedOut.Core;
using WipedOut.Models;
using WipedOut.Online;
using WipedOut.Rules;

namespace WipedOut.Board
{
    /// <summary>
    /// Vista del tablero 3D. No decide reglas: dibuja el estado que le pasa
    /// <see cref="GameManager"/> (motor <see cref="MatchEngine"/>) o, en una partida online, la
    /// <see cref="OnlineMatchView"/> que llega del servidor. La entrada del jugador se enruta a
    /// <see cref="GameManager"/> y el resultado de las reglas llega de vuelta al refrescar.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoardController : MonoBehaviour
    {
        [Header("Configuración")]
        [SerializeField, Tooltip("BoardConfig de referencia (legado). El tamaño lo decide WipedOut.Rules.")]
        private BoardConfig boardConfig;

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

        [Header("Movimiento online")]
        [SerializeField, Min(0.05f), Tooltip("Cada cuántos segundos se envía la posición local al servidor.")]
        private float moveSendInterval = 0.2f;

        [SerializeField, Min(0.1f), Tooltip("Velocidad de interpolación del avatar remoto.")]
        private float avatarMoveSpeed = 12f;

        [SerializeField, Min(0f), Tooltip("Margen que se resta a las cotas del piso (Map) para no caer al vacío.")]
        private float mapMargin = 0.5f;

        /// <summary>Se dispara al terminar de generar el tablero.</summary>
        public event Action<int, int> OnBoardGenerated;

        /// <summary>Jugador, posición, minas colocadas y total requerido.</summary>
        public event Action<int, Vector2Int, int, int> OnMinePlaced;

        /// <summary>Se dispara cuando una colocación es rechazada.</summary>
        public event Action<int, Vector2Int, string> OnPlacementRejected;

        /// <summary>Jugador, posición y si la casilla contenía una mina.</summary>
        public event Action<int, Vector2Int, bool> OnTileRevealed;

        public int GridWidth { get; private set; }
        public int GridHeight { get; private set; }
        public bool HasBoard => tiles != null;

        /// <summary>Distancia de mundo entre casillas consecutivas (tamaño + separación).</summary>
        public float TileStep => Mathf.Max(0.01f, tileSize + tileSpacing);

        private TileData[,] tiles;
        private Transform tilesRoot;
        private MaterialPropertyBlock propBlock;

        private int[] stateBuffer = Array.Empty<int>();
        private int[] ownerBuffer = Array.Empty<int>();
        private bool minesVisibleBuffer;

        private Vector2Int player1Selection;
        private Vector2Int player2Selection;
        private bool hasPlayer1Selection;
        private bool hasPlayer2Selection;

        private OnlineMatchScreen onlineScreen;

        private OnlineMatchService onlineService;
        private Transform remoteAvatar;
        private Vector3 remoteTarget;
        private float remoteAvatarHeight = 0.5f;
        private int spawnedSeat = -1;
        private float nextMoveSend;
        private Vector3 lastSentPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        private Vector2 movementBounds = new Vector2(9.5f, 9.5f);
        private readonly Dictionary<Collider, Vector2Int> tileCells = new Dictionary<Collider, Vector2Int>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            propBlock = new MaterialPropertyBlock();
            ResolveControllers();
            ResolveMapBounds();
            SetBoardVisible(false);
        }

        private void OnEnable()
        {
            ResolveControllers();
            SubscribeToControllers();
        }

        private void Start()
        {
            ResolveControllers();
            SubscribeToControllers();
        }

        private void OnDisable()
        {
            UnsubscribeFromControllers();
        }

        private void Update()
        {
            // Enlaza una sola vez con la pantalla online (que se crea al final de cargar la escena)
            // para dibujar en el tablero 3D la vista que devuelve el servidor.
            if (onlineScreen == null)
            {
                onlineScreen = OnlineMatchScreen.Instance;
                if (onlineScreen != null)
                {
                    onlineScreen.Service.ViewChanged += ApplyOnlineView;
                    onlineService = onlineScreen.Service;
                }
            }

            SyncLocalMovement();
            UpdateRemoteAvatar();
            HandleTileClick();
        }

        /// <summary>Genera una grilla vacía cuadrada. Las minas y el revelado los decide el motor.</summary>
        public void GenerateBoard3D(int gridSize)
        {
            int size = Mathf.Max(1, gridSize);
            GridWidth = size;
            GridHeight = size;

            if (player1Controller == null || player2Controller == null)
            {
                ResolveControllers();
                SubscribeToControllers();
            }

            BuildBoard(size, size);
            CaptureSelections();

            stateBuffer = new int[size * size];
            ownerBuffer = new int[size * size];
            minesVisibleBuffer = false;

            RefreshAllTiles();
            OnBoardGenerated?.Invoke(size, size);
        }

        /// <summary>Dibuja el estado del motor de reglas (partida local).</summary>
        public void ApplyLocalState(GameManager gameManager)
        {
            if (gameManager == null)
                return;

            ApplyCellStates(
                gameManager.GridSize,
                gameManager.CellStates(),
                gameManager.MineCells(),
                gameManager.MineOwners(),
                gameManager.MinesVisible);
        }

        /// <summary>Dibuja el estado de una partida online (vista autorizada del servidor).</summary>
        public void ApplyOnlineView(OnlineMatchView view)
        {
            if (view == null)
                return;

            int size = view.size;
            if (size <= 0 || view.cells == null || view.cells.Length != size * size)
                return;

            if (!HasBoard || GridWidth != size)
                GenerateBoard3D(size);

            bool minesVisible = view.status == OnlineStatus.Finished
                || view.phase == OnlinePhase.Placement
                || view.phase == OnlinePhase.Memorize;

            ApplyCellStates(size, view.cells, view.mines, view.mineOwners, minesVisible);
            SetBoardVisible(true);

            SetupPlayers(view);
        }

        private void ApplyCellStates(int gridSize, int[] states, int[] mines, int[] mineOwners, bool minesVisible)
        {
            if (tiles == null)
                return;

            Dictionary<int, int> owners = new Dictionary<int, int>();
            if (mines != null)
            {
                for (int i = 0; i < mines.Length; i++)
                {
                    int owner = mineOwners != null && i < mineOwners.Length ? mineOwners[i] : 0;
                    owners[mines[i]] = owner;
                }
            }

            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    int cell = GameManager.CellIndex(x, y, gridSize);
                    int state = states != null && cell < states.Length ? states[cell] : -1;
                    int owner = owners.TryGetValue(cell, out int o) ? o : 0;

                    if (cell >= 0 && cell < stateBuffer.Length)
                        stateBuffer[cell] = state;

                    if (cell >= 0 && cell < ownerBuffer.Length)
                        ownerBuffer[cell] = owner;
                }
            }

            minesVisibleBuffer = minesVisible;
            RefreshAllTiles();
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
            {
                PlaceOutcome outcome = GameManager.Instance.PlaceMine(controller.PlayerIndex, position);

                if (outcome == PlaceOutcome.Placed)
                {
                    OnMinePlaced?.Invoke(
                        controller.PlayerIndex,
                        position,
                        GameManager.Instance.PlacedBy(controller.PlayerIndex),
                        GameManager.Instance.MinesPerPlayer);
                }
                else
                {
                    OnPlacementRejected?.Invoke(controller.PlayerIndex, position, RejectMessage(outcome));
                    StartCoroutine(FlashInvalidTile(position));
                }
            }
            else if (state == GameState.PlayerTurn)
            {
                RevealOutcome outcome = GameManager.Instance.RevealTile(controller.PlayerIndex, position);

                if (outcome == RevealOutcome.Safe || outcome == RevealOutcome.Mine)
                    OnTileRevealed?.Invoke(controller.PlayerIndex, position, outcome == RevealOutcome.Mine);
            }
        }

        private static string RejectMessage(PlaceOutcome outcome)
        {
            switch (outcome)
            {
                case PlaceOutcome.NotYourTurn:
                    return "No es tu turno para colocar.";
                case PlaceOutcome.InvalidCell:
                    return "La posición está fuera del tablero.";
                case PlaceOutcome.CellOccupied:
                    return "La casilla ya contiene una mina.";
                case PlaceOutcome.WrongPhase:
                    return "No estás en la fase de colocación.";
                default:
                    return "Colocación rechazada.";
            }
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

            int cell = GameManager.CellIndex(tile.gridPosition.x, tile.gridPosition.y, GridWidth);
            int state = cell >= 0 && cell < stateBuffer.Length ? stateBuffer[cell] : -1;
            int owner = cell >= 0 && cell < ownerBuffer.Length ? ownerBuffer[cell] : 0;

            Color color = GetCellColor(state, owner, minesVisibleBuffer);

            if (hasPlayer1Selection && tile.gridPosition == player1Selection)
                color = Color.Lerp(color, player1SelectionColor, selectionTint);

            if (hasPlayer2Selection && tile.gridPosition == player2Selection)
                color = Color.Lerp(color, player2SelectionColor, selectionTint);

            ApplyTileColor(tile, color);
        }

        private Color GetCellColor(int state, int owner, bool minesVisible)
        {
            if (owner != 0)
            {
                if (state == 1)
                    return triggeredMineColor;

                return minesVisible
                    ? (owner == 1 ? player1MineColor : player2MineColor)
                    : hiddenTileColor;
            }

            return state == 0 ? revealedSafeColor : hiddenTileColor;
        }

        private void ApplyTileColor(TileData tile, Color color)
        {
            if (tile == null || tile.tileInstance == null)
                return;

            Renderer tileRenderer = tile.tileInstance.GetComponent<Renderer>();

            if (tileRenderer == null)
                return;

            propBlock.Clear();
            propBlock.SetColor(BaseColorId, color);
            propBlock.SetColor(ColorId, color);
            tileRenderer.SetPropertyBlock(propBlock);
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

        private TileData GetTile(Vector2Int position)
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

                    Collider tileCollider = tileObject.GetComponent<Collider>();
                    if (tileCollider != null)
                    {
                        tileCollider.enabled = true;
                        tileCells[tileCollider] = new Vector2Int(x, y);
                    }
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
                EnsurePositiveScale(tileObject.transform);
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

        /// <summary>Evita que un prefab con escala negativa dispare el aviso del BoxCollider.</summary>
        private static void EnsurePositiveScale(Transform t)
        {
            Vector3 scale = t.localScale;
            t.localScale = new Vector3(
                Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
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
            tileCells.Clear();
        }

        /// <summary>Muestra u oculta el tablero 3D y los jugadores. Oculta hasta que entra una partida online.</summary>
        private void SetBoardVisible(bool visible)
        {
            if (player1Controller != null)
                player1Controller.gameObject.SetActive(visible);

            if (player2Controller != null)
                player2Controller.gameObject.SetActive(visible);

            if (tilesRoot != null)
                tilesRoot.gameObject.SetActive(visible);
        }

        // --------------------------------------------------- jugadores online (spawn y movimiento)

        /// <summary>
        /// Coloca al jugador local en una esquina del piso (fuera de las grillas) y al avatar del rival en la
        /// esquina diagonalmente opuesta, según el asiento: P1 (Host) en la esquina sur-oeste, P2 (Guest) en la
        /// nor-este. Activa la cámara en primera persona y la orienta hacia el tablero central.
        /// </summary>
        private void SetupPlayers(OnlineMatchView view)
        {
            bool firstSpawn = spawnedSeat != view.mySeat;
            spawnedSeat = view.mySeat;

            if (player1Controller == null)
                ResolveControllers();

            if (player1Controller != null)
            {
                player1Controller.ConfigureBoard(GridWidth, GridHeight, TileStep);
                player1Controller.SetMovementBounds(movementBounds.x, movementBounds.y);
                player1Controller.SetInputEnabled(true);

                if (firstSpawn)
                {
                    // Esquina del piso, fuera de las grillas (con margen interior para no caer del borde).
                    float x = spawnedSeat == 1 ? -movementBounds.x : movementBounds.x;
                    float z = spawnedSeat == 1 ? movementBounds.y : -movementBounds.y;
                    player1Controller.TeleportTo(new Vector3(x, player1Controller.transform.position.y, z));
                    player1Controller.SetupFirstPerson();
                    player1Controller.LookTowards(Vector3.zero);
                }
            }

            // El avatar remoto usa la misma altura que el jugador local.
            remoteAvatarHeight = player1Controller != null ? player1Controller.transform.position.y : remoteAvatarHeight;

            if (firstSpawn)
                remoteTarget = RivalCornerPosition();
            else
                remoteTarget = new Vector3(view.rivalX, remoteAvatarHeight, view.rivalZ);

            EnsureRemoteAvatar();
        }

        /// <summary>Esquina opuesta a la del jugador local, donde aparece el avatar del rival.</summary>
        private Vector3 RivalCornerPosition()
        {
            float x = spawnedSeat == 1 ? movementBounds.x : -movementBounds.x;
            float z = spawnedSeat == 1 ? -movementBounds.y : movementBounds.y;
            return new Vector3(x, remoteAvatarHeight, z);
        }

        /// <summary>Crea el avatar del rival (un objeto visual simple, sin input) en el extremo opuesto.</summary>
        private void EnsureRemoteAvatar()
        {
            if (remoteAvatar != null)
                return;

            GameObject avatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            avatar.name = "RemoteRivalAvatar";

            Transform parent = boardRoot != null ? boardRoot : transform;
            avatar.transform.SetParent(parent, false);
            avatar.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

            Collider collider = avatar.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;

            Renderer renderer = avatar.GetComponent<Renderer>();
            if (renderer != null)
            {
                propBlock.Clear();
                propBlock.SetColor(BaseColorId, new Color(1f, 0.25f, 0.2f));
                propBlock.SetColor(ColorId, new Color(1f, 0.25f, 0.2f));
                renderer.SetPropertyBlock(propBlock);
            }

            remoteAvatarHeight = player1Controller != null ? player1Controller.transform.position.y : 0.5f;

            remoteAvatar = avatar.transform;
            remoteAvatar.position = new Vector3(remoteTarget.x, remoteAvatarHeight, remoteTarget.z);
        }

        /// <summary>Envía la posición local al servidor (throttled) solo cuando el jugador se mueve.</summary>
        private void SyncLocalMovement()
        {
            if (onlineService == null || player1Controller == null)
                return;

            OnlineMatchView view = onlineService.Current;
            if (view == null || view.status != OnlineStatus.Playing)
                return;

            if (Time.realtimeSinceStartup < nextMoveSend)
                return;

            Vector3 position = player1Controller.BoardPosition;
            if ((position - lastSentPosition).sqrMagnitude < 0.0001f)
                return;

            lastSentPosition = position;
            nextMoveSend = Time.realtimeSinceStartup + moveSendInterval;
            _ = onlineService.MoveAsync(position.x, position.z);
        }

        /// <summary>Interpola el avatar remoto hacia la última posición recibida del rival.</summary>
        private void UpdateRemoteAvatar()
        {
            if (remoteAvatar == null)
                return;

            remoteAvatar.position = Vector3.MoveTowards(
                remoteAvatar.position,
                remoteTarget,
                avatarMoveSpeed * Time.deltaTime);
        }

        /// <summary>Calcula las cotas amplias del piso (Map) para que el jugador no se caiga al vacío.</summary>
        private void ResolveMapBounds()
        {
            GameObject map = GameObject.Find("Map");
            if (map == null)
                return;

            Collider collider = map.GetComponent<Collider>();
            if (collider == null)
                return;

            Bounds bounds = collider.bounds;
            float halfX = Mathf.Max(0.1f, bounds.extents.x - mapMargin);
            float halfZ = Mathf.Max(0.1f, bounds.extents.z - mapMargin);
            movementBounds = new Vector2(halfX, halfZ);
        }

        /// <summary>
        /// Interacción con el ratón: un clic izquierdo sobre una casilla 3D dispara la acción de la fase
        /// actual (colocar una mina en la colocación, o destapar en la búsqueda).
        /// </summary>
        private void HandleTileClick()
        {
            if (onlineService == null)
                return;

            OnlineMatchView view = onlineService.Current;
            if (view == null || view.status != OnlineStatus.Playing || !view.IsMyTurn)
                return;

            // Si el clic cae sobre un elemento de UI (botón LEAVE/FORFEIT), no se toca el mundo 3D.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (!TryGetMouseRay(out Ray ray))
                return;

            // Se recorre por distancia para saltar colliders que no son casillas (el avatar propio, el piso...).
            Vector2Int cell = default;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 100f))
            {
                if (tileCells.TryGetValue(hit.collider, out cell))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            int cellIndex = GameManager.CellIndex(cell.x, cell.y, GridWidth);

            if (view.phase == OnlinePhase.Placement)
                _ = onlineService.PlaceMineAsync(cellIndex);
            else if (view.phase == OnlinePhase.Search)
                _ = onlineService.RevealAsync(cellIndex);
        }

        private bool TryGetMouseRay(out Ray ray)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                ray = default;
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                ray = default;
                return false;
            }

            Vector2 screen = mouse.position.ReadValue();
            ray = camera.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.GetMouseButtonDown(0))
            {
                ray = default;
                return false;
            }

            ray = camera.ScreenPointToRay(Input.mousePosition);
            return true;
#else
            ray = default;
            return false;
#endif
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
