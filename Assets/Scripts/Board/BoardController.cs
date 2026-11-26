using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
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
        [SerializeField, Range(0f, 1f), Tooltip("Intensidad del resaltado de la casilla bajo la mira.")]
        private float hoverTint = 0.25f;
        [SerializeField, Tooltip("Color del resaltado de la casilla disponible bajo la mira.")]
        private Color hoverColor = new Color(1f, 0.9f, 0.2f, 1f);

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

        [Header("Botón rojo (3D)")]
        [SerializeField, Tooltip("Botón rojo de la escena. Solo es interactivo en la fase de búsqueda.")]
        private GameObject redButton;

        [SerializeField, Min(0f), Tooltip("Altura del cartel flotante por encima del botón.")]
        private float redButtonBubbleHeight = 0.25f;

        [SerializeField, Min(0.1f), Tooltip("Segundos para confirmar con el segundo clic.")]
        private float redButtonConfirmSeconds = 5f;

        [SerializeField, Min(0f), Tooltip("Segundos que dura el aviso de 'marca tus casillas primero'.")]
        private float redButtonHintSeconds = 3f;

        [SerializeField, Min(0f), Tooltip("Distancia máxima del jugador al botón para mantener la confirmación pendiente.")]
        private float redButtonMaxDistance = 4f;

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

        private Vector2Int hoveredCell = new Vector2Int(-1, -1);

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

        private readonly HashSet<int> preselectedCells = new HashSet<int>();
        private bool redButtonPending;
        private float redButtonConfirmDeadline;
        private float redButtonHintUntil;

        private bool redButtonHovered;
        private Renderer[] redButtonRenderers = Array.Empty<Renderer>();

        private GameObject bubble;
        private Text bubbleText;

        private MaterialPropertyBlock emptyPropBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            propBlock = new MaterialPropertyBlock();
            emptyPropBlock = new MaterialPropertyBlock();
            ResolveControllers();
            ResolveMapBounds();
            ResolveRedButton();
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
            UpdateRedButton();
            UpdateHover();
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

            bool preselected = preselectedCells.Contains(cell);
            if (preselected)
                color = Color.Lerp(color, player1SelectionColor, selectionTint);

            if (hoveredCell.x >= 0 && tile.gridPosition == hoveredCell)
            {
                // Solo la casilla disponible (oculta y sin mina colocada) se resalta en amarillo; la ya
                // marcada conserva su color de selección y apenas se aclara para no ocultar que está elegida.
                bool available = state == -1 && owner == 0;
                if (available)
                {
                    float strength = preselected ? hoverTint * 0.5f : hoverTint;
                    color = Color.Lerp(color, hoverColor, strength);
                }
            }

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

        /// <summary>El controlador del único jugador local (P1 o P2 según el asiento que asigne el servidor).</summary>
        private PlayerController3D LocalPlayer => player1Controller != null ? player1Controller : player2Controller;

        /// <summary>
        /// Coloca al jugador local en una esquina del piso (fuera de las grillas) y al avatar del rival en la
        /// esquina diagonalmente opuesta, según el asiento: P1 (Host) en la esquina sur-oeste, P2 (Guest) en la
        /// nor-este. Activa la cámara en primera persona y la orienta hacia el tablero central.
        /// </summary>
        private void SetupPlayers(OnlineMatchView view)
        {
            bool firstSpawn = spawnedSeat != view.mySeat;
            spawnedSeat = view.mySeat;

            // Este cliente controla a UN solo jugador local, sea cual sea el asiento (P1 o P2) que le
            // asignó el servidor. El controlador se resuelve y se activa con independencia del asiento.
            PlayerController3D localPlayer = player1Controller != null ? player1Controller : player2Controller;
            if (localPlayer == null)
            {
                ResolveControllers();
                localPlayer = player1Controller != null ? player1Controller : player2Controller;
            }

            if (localPlayer != null)
            {
                localPlayer.gameObject.SetActive(true);
                localPlayer.ConfigureBoard(GridWidth, GridHeight, TileStep);
                localPlayer.SetMovementBounds(movementBounds.x, movementBounds.y);
                localPlayer.SetInputEnabled(true);

                if (firstSpawn)
                {
                    // Esquina del piso, fuera de las grillas (con margen interior para no caer del borde).
                    float x = spawnedSeat == 1 ? -movementBounds.x : movementBounds.x;
                    float z = spawnedSeat == 1 ? movementBounds.y : -movementBounds.y;
                    localPlayer.TeleportTo(new Vector3(x, localPlayer.transform.position.y, z));
                    localPlayer.LookTowards(Vector3.zero);
                }

                // La cámara en primera persona y el bloqueo del cursor se garantizan para P1 y P2:
                // es idempotente, así que se llama en cada vista activa (no solo en el primer spawn).
                localPlayer.SetupFirstPerson();
            }

            // El avatar remoto usa la misma altura que el jugador local.
            remoteAvatarHeight = localPlayer != null ? localPlayer.transform.position.y : remoteAvatarHeight;

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

            // La primitiva usa el material "Standard" por defecto, que en URP se ve magenta. Se sustituye
            // por un material con el shader Lit de URP (con fallback a Standard) y su color de jugador.
            Renderer renderer = avatar.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                if (shader != null)
                {
                    Material material = new Material(shader);
                    Color avatarColor = new Color(1f, 0.25f, 0.2f);
                    if (material.HasProperty(BaseColorId))
                        material.SetColor(BaseColorId, avatarColor);
                    if (material.HasProperty(ColorId))
                        material.SetColor(ColorId, avatarColor);
                    renderer.material = material;
                }
            }

            PlayerController3D localPlayer = LocalPlayer;
            remoteAvatarHeight = localPlayer != null ? localPlayer.transform.position.y : 0.5f;

            remoteAvatar = avatar.transform;
            remoteAvatar.position = new Vector3(remoteTarget.x, remoteAvatarHeight, remoteTarget.z);
        }

        /// <summary>Envía la posición local al servidor (throttled) solo cuando el jugador se mueve.</summary>
        private void SyncLocalMovement()
        {
            PlayerController3D localPlayer = LocalPlayer;
            if (onlineService == null || localPlayer == null)
                return;

            OnlineMatchView view = onlineService.Current;
            if (view == null || view.status != OnlineStatus.Playing)
                return;

            if (Time.realtimeSinceStartup < nextMoveSend)
                return;

            Vector3 position = localPlayer.BoardPosition;
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
        /// Interacción con el ratón: con el cursor bloqueado (FPS), un clic izquierdo dispara la casilla que
        /// está bajo la mira central (raycast desde el centro de la pantalla) según la fase actual. En la fase
        /// de búsqueda, el clic sobre una casilla oculta la marca/desmarca como preselección (no se destapa) y
        /// el botón rojo (doble clic) envía la preselección al servidor.
        /// </summary>
        private void HandleTileClick()
        {
            if (onlineService == null)
                return;

            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            OnlineMatchView view = onlineService.Current;
            if (view == null || view.status != OnlineStatus.Playing || !view.IsMyTurn)
                return;

            if (!ReadLeftClick())
                return;

            if (!TryGetCenterRay(out Ray ray))
                return;

            // El botón rojo se comprueba antes que las casillas: solo interactúa en búsqueda y gana al suelo.
            if (IsRedButtonActive() && RayHitsRedButton(ray))
            {
                HandleRedButtonClick();
                return;
            }

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
            {
                _ = onlineService.PlaceMineAsync(cellIndex);
            }
            else if (view.phase == OnlinePhase.Search)
            {
                TogglePreselection(view, cellIndex);
            }
        }

        /// <summary>Raycast desde el centro exacto de la pantalla (donde está la mira).</summary>
        private bool TryGetCenterRay(out Ray ray)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                ray = default;
                return false;
            }

            ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        /// <summary>Resalta suavemente la casilla que está bajo la mira central y, en búsqueda, el botón rojo.</summary>
        private void UpdateHover()
        {
            Vector2Int cell = new Vector2Int(-1, -1);
            bool buttonHovered = false;

            if (Cursor.lockState == CursorLockMode.Locked && TryGetCenterRay(out Ray ray))
            {
                bool tileFound = false;
                foreach (RaycastHit hit in Physics.RaycastAll(ray, 100f))
                {
                    if (!tileFound && tileCells.TryGetValue(hit.collider, out cell))
                        tileFound = true;

                    if (IsRedButtonActive() && BelongsToRedButton(hit.collider))
                        buttonHovered = true;
                }
            }

            SetHover(cell);
            SetRedButtonHover(buttonHovered);
        }

        private void SetHover(Vector2Int cell)
        {
            if (cell == hoveredCell)
                return;

            hoveredCell = cell;
            RefreshAllTiles();
        }

        // ------------------------------------------- botón rojo 3D (preselección y confirmación)

        /// <summary>El botón rojo solo interactúa en la fase de búsqueda y en el turno del jugador local.</summary>
        private bool IsRedButtonActive()
        {
            if (redButton == null || onlineService == null)
                return false;

            OnlineMatchView view = onlineService.Current;
            return view != null && view.phase == OnlinePhase.Search && view.IsMyTurn;
        }

        /// <summary>Localiza el botón rojo si no se conectó por el inspector (por nombre, como red de seguridad).</summary>
        private void ResolveRedButton()
        {
            if (redButton != null)
                return;

            redButton = GameObject.Find("Red_button") ?? GameObject.Find("red_button");
        }

        /// <summary>
        /// Actualiza cada frame el cartel flotante (billboard hacia la cámara) y la cuenta atrás de la
        /// confirmación pendiente. Si se agota el plazo, el jugador se aleja o cambia de turno, se resetea.
        /// </summary>
        private void UpdateRedButton()
        {
            if (redButton == null)
            {
                ResolveRedButton();
                return;
            }

            bool active = IsRedButtonActive();

            // Cartel flotante estilo Roblox: siempre mira a la cámara.
            if (bubble != null && bubble.activeSelf)
            {
                Camera camera = Camera.main;
                if (camera != null)
                    bubble.transform.rotation = camera.transform.rotation;
            }

            // Fuera de la fase de búsqueda (o del turno) el botón queda apagado y todo se resetea.
            if (!active)
            {
                preselectedCells.Clear();
                ResetRedButtonInteraction();
                return;
            }

            if (redButtonPending)
            {
                bool expired = Time.realtimeSinceStartup >= redButtonConfirmDeadline;
                PlayerController3D localPlayer = LocalPlayer;
                bool walkedAway = localPlayer != null
                    && Vector3.Distance(localPlayer.transform.position, redButton.transform.position) > redButtonMaxDistance;

                if (expired || walkedAway)
                {
                    ResetRedButtonInteraction();
                    return;
                }

                float remaining = Mathf.Max(0f, redButtonConfirmDeadline - Time.realtimeSinceStartup);
                int seconds = Mathf.CeilToInt(remaining);
                ShowBubble("¿Estás seguro que quieres apretar el botón? (" + seconds + "s)");
            }
            else if (redButtonHintUntil > 0f && Time.realtimeSinceStartup >= redButtonHintUntil)
            {
                HideBubble();
                redButtonHintUntil = 0f;
            }
        }

        /// <summary>
        /// Clic sobre el botón rojo: sin preselección muestra un aviso; con preselección, el primer clic
        /// abre la confirmación y el segundo (dentro del plazo) envía las casillas al servidor.
        /// </summary>
        private void HandleRedButtonClick()
        {
            if (preselectedCells.Count == 0)
            {
                redButtonPending = false;
                redButtonHintUntil = Time.realtimeSinceStartup + redButtonHintSeconds;
                ShowBubble("Marca tus casillas primero");
                return;
            }

            if (!redButtonPending)
            {
                redButtonPending = true;
                redButtonConfirmDeadline = Time.realtimeSinceStartup + redButtonConfirmSeconds;
                return;
            }

            ConfirmRedButton();
        }

        /// <summary>Segundo clic dentro del plazo: envía la preselección como una única jugada de destape.</summary>
        private void ConfirmRedButton()
        {
            int[] cells = new int[preselectedCells.Count];
            preselectedCells.CopyTo(cells);

            ResetRedButtonInteraction();
            preselectedCells.Clear();

            _ = onlineService.RevealManyAsync(cells);
        }

        /// <summary>Marca/desmarca una casilla oculta como preselección local (sin revelarla al servidor).</summary>
        private void TogglePreselection(OnlineMatchView view, int cellIndex)
        {
            if (view.cells == null || cellIndex < 0 || cellIndex >= view.cells.Length)
                return;

            // Solo las casillas todavía ocultas (blancas) se pueden preseleccionar.
            if (view.cells[cellIndex] != -1)
                return;

            if (!preselectedCells.Add(cellIndex))
                preselectedCells.Remove(cellIndex);

            RefreshAllTiles();
        }

        private void ResetRedButtonInteraction()
        {
            redButtonPending = false;
            redButtonConfirmDeadline = 0f;
            redButtonHintUntil = 0f;
            HideBubble();
        }

        /// <summary>El collider alcanzado pertenece al botón rojo o a uno de sus hijos.</summary>
        private bool BelongsToRedButton(Collider collider)
        {
            if (redButton == null || collider == null)
                return false;

            // Equivalente a GetComponentInParent sobre la raíz del botón: recorre la jerarquía hacia arriba.
            Transform current = collider.transform;
            while (current != null)
            {
                if (current == redButton.transform)
                    return true;

                current = current.parent;
            }

            return false;
        }

        /// <summary>El rayo central alcanza el collider del botón rojo (o el de sus hijos).</summary>
        private bool RayHitsRedButton(Ray ray)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 100f))
            {
                if (BelongsToRedButton(hit.collider))
                    return true;
            }

            return false;
        }

        private void SetRedButtonHover(bool hovered)
        {
            if (redButtonHovered == hovered)
                return;

            redButtonHovered = hovered;
            RefreshRedButtonHighlight();
        }

        private void EnsureRedButtonRenderers()
        {
            if (redButton == null)
            {
                redButtonRenderers = Array.Empty<Renderer>();
                return;
            }

            if (redButtonRenderers.Length == 0)
                redButtonRenderers = redButton.GetComponentsInChildren<Renderer>();
        }

        /// <summary>Resalta el botón rojo cuando está bajo la mira (solo en búsqueda); en colocación no se resalta.</summary>
        private void RefreshRedButtonHighlight()
        {
            EnsureRedButtonRenderers();

            foreach (Renderer renderer in redButtonRenderers)
            {
                if (renderer == null)
                    continue;

                if (redButtonHovered)
                {
                    Material material = renderer.sharedMaterial;
                    Color baseColor = material != null && material.HasProperty(BaseColorId)
                        ? material.GetColor(BaseColorId)
                        : Color.white;

                    propBlock.Clear();
                    propBlock.SetColor(BaseColorId, Color.Lerp(baseColor, Color.white, hoverTint));
                    propBlock.SetColor(ColorId, Color.Lerp(baseColor, Color.white, hoverTint));
                    renderer.SetPropertyBlock(propBlock);
                }
                else
                {
                    renderer.SetPropertyBlock(emptyPropBlock);
                }
            }
        }

        private void ShowBubble(string message)
        {
            EnsureBubble();

            if (bubbleText != null)
                bubbleText.text = message;

            bubble.transform.position = BubblePosition();
            bubble.SetActive(true);
        }

        private void HideBubble()
        {
            if (bubble != null)
                bubble.SetActive(false);
        }

        private Vector3 BubblePosition()
        {
            if (redButton == null)
                return Vector3.zero;

            Collider collider = redButton.GetComponentInChildren<Collider>();
            if (collider != null)
            {
                Bounds bounds = collider.bounds;
                return bounds.center + Vector3.up * (bounds.extents.y + redButtonBubbleHeight);
            }

            return redButton.transform.position + Vector3.up * redButtonBubbleHeight;
        }

        /// <summary>Construye el cartel flotante (WorldSpace Canvas con fondo oscuro) una sola vez, oculto.</summary>
        private void EnsureBubble()
        {
            if (bubble != null)
                return;

            bubble = new GameObject("RedButtonBubble", typeof(RectTransform));

            Canvas canvas = bubble.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = bubble.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(480f, 64f);

            // Fondo oscuro semi-transparente (burbuja estilo Roblox).
            GameObject backgroundGo = new GameObject("Background", typeof(RectTransform));
            backgroundGo.transform.SetParent(bubble.transform, false);
            RectTransform backgroundRect = (RectTransform)backgroundGo.transform;
            StretchRect(backgroundRect);
            Image background = backgroundGo.AddComponent<Image>();
            background.color = new Color(0.08f, 0.1f, 0.13f, 0.85f);

            // Texto.
            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(bubble.transform, false);
            RectTransform textRect = (RectTransform)textGo.transform;
            StretchRect(textRect);
            bubbleText = textGo.AddComponent<Text>();
            bubbleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bubbleText.fontSize = 30;
            bubbleText.alignment = TextAnchor.MiddleCenter;
            bubbleText.color = Color.white;
            bubbleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bubbleText.verticalOverflow = VerticalWrapMode.Overflow;
            bubbleText.resizeTextForBestFit = true;
            bubbleText.resizeTextMinSize = 10;
            bubbleText.resizeTextMaxSize = 30;

            // Escala el canvas para que las unidades de píxel pasen a unidades de mundo.
            bubble.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            bubble.SetActive(false);
        }

        private static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static bool ReadLeftClick()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
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
                player1Controller.OnTileSelected -= HandleTileSelected;
                player1Controller.OnTileSelected += HandleTileSelected;
            }

            if (player2Controller != null)
            {
                player2Controller.OnTileSelected -= HandleTileSelected;
                player2Controller.OnTileSelected += HandleTileSelected;
            }
        }

        private void UnsubscribeFromControllers()
        {
            if (player1Controller != null)
                player1Controller.OnTileSelected -= HandleTileSelected;

            if (player2Controller != null)
                player2Controller.OnTileSelected -= HandleTileSelected;
        }
    }
}
