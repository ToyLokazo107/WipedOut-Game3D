using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WipedOut.Board
{
    /// <summary>
    /// Controla el cursor de un jugador sobre la grilla del tablero 3D.
    /// Añade una instancia de este componente por cada jugador (Jugador 1 y Jugador 2).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerController3D : MonoBehaviour
    {
        public enum PlayerNumber
        {
            Player1 = 1,
            Player2 = 2
        }

        [Header("Jugador")]
        [SerializeField, Tooltip("Esquema de control que usará esta instancia.")]
        private PlayerNumber playerNumber = PlayerNumber.Player1;

        [Header("Grilla")]
        [SerializeField, Min(1), Tooltip("Ancho de la grilla. BoardController lo actualiza al generar el tablero.")]
        private int gridWidth = 2;

        [SerializeField, Min(1), Tooltip("Alto de la grilla. BoardController lo actualiza al generar el tablero.")]
        private int gridHeight = 2;

        [Header("Estado")]
        [SerializeField, Tooltip("Casilla enfocada actualmente por este jugador (coordenadas de grilla).")]
        private Vector2Int selectedTile;

        private bool inputEnabled = true;

        /// <summary>Jugador asociado a esta instancia.</summary>
        public PlayerNumber Player => playerNumber;

        /// <summary>Índice del jugador: 1 o 2.</summary>
        public int PlayerIndex => (int)playerNumber;

        /// <summary>Ancho actual de la grilla.</summary>
        public int GridWidth => gridWidth;

        /// <summary>Alto actual de la grilla.</summary>
        public int GridHeight => gridHeight;

        /// <summary>Casilla enfocada actualmente.</summary>
        public Vector2Int SelectedTile => selectedTile;

        /// <summary>Indica si esta instancia está leyendo el teclado.</summary>
        public bool InputEnabled => inputEnabled;

        /// <summary>
        /// Se dispara cuando el cursor cambia de casilla.
        /// El primer parámetro identifica al jugador y el segundo la casilla enfocada.
        /// </summary>
        public event Action<PlayerController3D, Vector2Int> OnSelectionChanged;

        /// <summary>
        /// Se dispara cuando el jugador confirma la casilla enfocada con Espacio (Jugador 1)
        /// o Enter (Jugador 2). BoardController debe usar este evento para resolver la casilla.
        /// </summary>
        public event Action<PlayerController3D, Vector2Int> OnTileSelected;

        private void Awake()
        {
            selectedTile = ClampToGrid(selectedTile);
        }

        private void Start()
        {
            // Notifica la selección inicial para que el tablero pueda resaltarla.
            OnSelectionChanged?.Invoke(this, selectedTile);
        }

        private void Update()
        {
            if (!inputEnabled)
                return;

            Vector2Int direction = ReadMovement();
            if (direction != Vector2Int.zero)
                MoveSelection(direction);

            if (ReadConfirm())
                ConfirmSelection();
        }

        private void OnValidate()
        {
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
            selectedTile = ClampToGrid(selectedTile);
        }

        /// <summary>
        /// Inicializa el controlador y lo asocia a un jugador y a un tamaño de grilla.
        /// </summary>
        public void Configure(PlayerNumber player, int width, int height)
        {
            playerNumber = player;
            SetGridSize(width, height, true);
        }

        /// <summary>
        /// Actualiza el tamaño de la grilla. Por defecto vuelve a centrar el cursor.
        /// </summary>
        public void SetGridSize(int width, int height, bool resetSelection = true)
        {
            gridWidth = Mathf.Max(1, width);
            gridHeight = Mathf.Max(1, height);

            if (resetSelection)
                ResetSelection();
            else
                SetSelection(selectedTile);
        }

        /// <summary>Activa o desactiva la lectura del teclado para este jugador.</summary>
        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        /// <summary>Coloca el cursor en el centro de la grilla.</summary>
        [ContextMenu("Centrar selección")]
        public void ResetSelection()
        {
            SetSelection(new Vector2Int(gridWidth / 2, gridHeight / 2), true);
        }

        /// <summary>Coloca el cursor en una casilla concreta (se ajusta a los límites).</summary>
        public void ResetSelection(Vector2Int position)
        {
            SetSelection(position, true);
        }

        /// <summary>Establece la casilla enfocada y notifica al tablero si cambió.</summary>
        public void SetSelection(Vector2Int position, bool forceNotify = false)
        {
            Vector2Int clamped = ClampToGrid(position);
            bool changed = clamped != selectedTile;
            selectedTile = clamped;

            if (changed || forceNotify)
                OnSelectionChanged?.Invoke(this, selectedTile);
        }

        /// <summary>Mueve el cursor una casilla, respetando los límites del tablero.</summary>
        public void MoveSelection(Vector2Int direction)
        {
            if (direction == Vector2Int.zero)
                return;

            SetSelection(selectedTile + direction);
        }

        /// <summary>
        /// Confirma la casilla enfocada y notifica al tablero mediante <see cref="OnTileSelected"/>.
        /// </summary>
        public void ConfirmSelection()
        {
            OnTileSelected?.Invoke(this, selectedTile);
        }

        private Vector2Int ClampToGrid(Vector2Int position)
        {
            return new Vector2Int(
                Mathf.Clamp(position.x, 0, Mathf.Max(0, gridWidth - 1)),
                Mathf.Clamp(position.y, 0, Mathf.Max(0, gridHeight - 1)));
        }

        private Vector2Int ReadMovement()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (playerNumber == PlayerNumber.Player1)
                {
                    if (keyboard.wKey.wasPressedThisFrame) return Vector2Int.up;
                    if (keyboard.sKey.wasPressedThisFrame) return Vector2Int.down;
                    if (keyboard.aKey.wasPressedThisFrame) return Vector2Int.left;
                    if (keyboard.dKey.wasPressedThisFrame) return Vector2Int.right;
                }
                else
                {
                    if (keyboard.upArrowKey.wasPressedThisFrame) return Vector2Int.up;
                    if (keyboard.downArrowKey.wasPressedThisFrame) return Vector2Int.down;
                    if (keyboard.leftArrowKey.wasPressedThisFrame) return Vector2Int.left;
                    if (keyboard.rightArrowKey.wasPressedThisFrame) return Vector2Int.right;
                }

                return Vector2Int.zero;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (playerNumber == PlayerNumber.Player1)
            {
                if (Input.GetKeyDown(KeyCode.W)) return Vector2Int.up;
                if (Input.GetKeyDown(KeyCode.S)) return Vector2Int.down;
                if (Input.GetKeyDown(KeyCode.A)) return Vector2Int.left;
                if (Input.GetKeyDown(KeyCode.D)) return Vector2Int.right;
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.UpArrow)) return Vector2Int.up;
                if (Input.GetKeyDown(KeyCode.DownArrow)) return Vector2Int.down;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) return Vector2Int.left;
                if (Input.GetKeyDown(KeyCode.RightArrow)) return Vector2Int.right;
            }
#endif

            return Vector2Int.zero;
        }

        private bool ReadConfirm()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (playerNumber == PlayerNumber.Player1)
                    return keyboard.spaceKey.wasPressedThisFrame;

                return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (playerNumber == PlayerNumber.Player1)
                return Input.GetKeyDown(KeyCode.Space);

            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
            return false;
#endif
        }
    }
}
