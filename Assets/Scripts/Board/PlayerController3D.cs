using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.EventSystems;

namespace WipedOut.Board
{
    /// <summary>
    /// Controla al ÚNICO jugador local en primera persona (FPS): el cursor queda bloqueado y se gira
    /// libremente con el ratón, se mueve con WASD por toda la plataforma (relativo a su orientación) y
    /// expone su posición para sincronizarla con el rival. La tecla Escape libera el cursor para poder
    /// pulsar la UI; un clic fuera de la UI (o volver a pulsar Escape) lo vuelve a bloquear.
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
        [SerializeField, Min(1), Tooltip("Ancho de la grilla (compatibilidad con el modo local).")]
        private int gridWidth = 2;

        [SerializeField, Min(1), Tooltip("Alto de la grilla (compatibilidad con el modo local).")]
        private int gridHeight = 2;

        [Header("Movimiento")]
        [SerializeField, Min(0.1f), Tooltip("Velocidad de desplazamiento (unidades de mundo por segundo).")]
        private float moveSpeed = 4f;

        [Header("Primera persona")]
        [SerializeField, Min(0.1f), Tooltip("Altura de los ojos respecto al pivote del jugador.")]
        private float eyeHeight = 0.9f;

        [SerializeField, Min(0.001f), Tooltip("Sensibilidad del ratón (grados por píxel).")]
        private float mouseSensitivity = 0.15f;

        [SerializeField, Range(-90f, 0f), Tooltip("Límite inferior del cabeceo (mirar hacia abajo).")]
        private float pitchMin = -80f;

        [SerializeField, Range(0f, 90f), Tooltip("Límite superior del cabeceo (mirar hacia arriba).")]
        private float pitchMax = 80f;

        private bool inputEnabled = true;
        private float tileStep = 1.08f;

        /// <summary>Cotas amplias del piso/mapa (para acotar el movimiento libre, no la grilla).</summary>
        private Vector2 movementHalfExtents = new Vector2(9.5f, 9.5f);

        private Vector2Int selectedTile;

        private float yaw;
        private float pitch;
        private bool firstPersonReady;
        private bool cursorLocked;

        /// <summary>Jugador asociado a esta instancia.</summary>
        public PlayerNumber Player => playerNumber;

        /// <summary>Índice del jugador: 1 o 2.</summary>
        public int PlayerIndex => (int)playerNumber;

        /// <summary>Ancho actual de la grilla.</summary>
        public int GridWidth => gridWidth;

        /// <summary>Alto actual de la grilla.</summary>
        public int GridHeight => gridHeight;

        /// <summary>Casilla enfocada (compatibilidad con el modo local; ya no se deriva de la posición).</summary>
        public Vector2Int SelectedTile => selectedTile;

        /// <summary>Posición de mundo del avatar (el valor (x, z) que se sincroniza con el rival).</summary>
        public Vector3 BoardPosition => transform.position;

        /// <summary>Indica si esta instancia está leyendo el teclado.</summary>
        public bool InputEnabled => inputEnabled;

        /// <summary>Indica si el cursor está bloqueado (modo FPS).</summary>
        public bool IsCursorLocked => cursorLocked;

        /// <summary>Se dispara al confirmar con Espacio (usado por el modo local).</summary>
        public event Action<PlayerController3D, Vector2Int> OnTileSelected;

        private void Awake()
        {
            selectedTile = ClampToGrid(new Vector2Int(gridWidth / 2, gridHeight / 2));
        }

        private void Update()
        {
            if (!firstPersonReady)
                return;

            HandleCursorToggle();

            if (!inputEnabled)
                return;

            HandleLook();
            HandleMovement();

            if (ReadConfirm())
                ConfirmSelection();
        }

        private void OnDisable()
        {
            SetCursorLocked(false);
        }

        private void OnValidate()
        {
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
        }

        // ------------------------------------------------------------------ primera persona

        /// <summary>Acopla la cámara a la cabeza del jugador, oculta su malla y bloquea el cursor. Idempotente.</summary>
        public void SetupFirstPerson()
        {
            if (firstPersonReady)
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            camera.transform.SetParent(transform, false);
            camera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
            camera.transform.localRotation = Quaternion.identity;

            HideOwnBody();
            firstPersonReady = true;
            SetCursorLocked(true);
        }

        /// <summary>Oculta la malla del jugador local (solo en este cliente) para no tapar la vista.</summary>
        public void HideOwnBody()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
        }

        /// <summary>Libera el cursor (para salir a la UI). No lo bloquea.</summary>
        public void UnlockCursor()
        {
            SetCursorLocked(false);
        }

        /// <summary>Orienta al jugador (guiñada y cabeceo) para mirar hacia un punto del mundo.</summary>
        public void LookTowards(Vector3 worldPoint)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 direction = worldPoint - eye;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            float horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = Mathf.Atan2(-direction.y, horizontal) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
            ApplyRotation();
        }

        private void HandleCursorToggle()
        {
            if (ReadTogglePressed())
            {
                SetCursorLocked(!cursorLocked);
                return;
            }

            // Con el cursor libre, un clic fuera de la UI lo vuelve a bloquear.
            if (!cursorLocked && ReadConfirmClick() && !IsPointerOverUI())
                SetCursorLocked(true);
        }

        private void SetCursorLocked(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void HandleLook()
        {
            Vector2 delta = ReadMouseDelta();
            if (delta == Vector2.zero)
                return;

            yaw += delta.x * mouseSensitivity;
            pitch -= delta.y * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
            ApplyRotation();
        }

        private void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Camera camera = Camera.main;
            if (camera != null && camera.transform.parent == transform)
                camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleMovement()
        {
            Vector2 input = ReadMovementAxes();
            if (input == Vector2.zero)
                return;

            Vector3 direction = MovementDirection(input);
            if (direction == Vector3.zero)
                return;

            transform.position = ClampMovement(transform.position + direction * (moveSpeed * Time.deltaTime));
        }

        private Vector3 MovementDirection(Vector2 input)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up);

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;

            Vector3 direction = right * input.x + forward * input.y;
            return direction.sqrMagnitude < 0.0001f ? Vector3.zero : direction.normalized;
        }

        // ------------------------------------------------------------------ grilla

        /// <summary>Actualiza el tamaño de la grilla (compatibilidad con el modo local).</summary>
        public void ConfigureBoard(int width, int height, float step)
        {
            gridWidth = Mathf.Max(1, width);
            gridHeight = Mathf.Max(1, height);
            tileStep = Mathf.Max(0.01f, step);
            selectedTile = ClampToGrid(selectedTile);
        }

        /// <summary>Actualiza el tamaño de la grilla (compatibilidad con el modo local).</summary>
        public void SetGridSize(int width, int height, bool resetSelection = true)
        {
            gridWidth = Mathf.Max(1, width);
            gridHeight = Mathf.Max(1, height);

            if (resetSelection)
                ResetSelection();
        }

        /// <summary>Establece las cotas amplias del piso por las que puede moverse libremente.</summary>
        public void SetMovementBounds(float halfX, float halfZ)
        {
            movementHalfExtents = new Vector2(Mathf.Max(0.1f, halfX), Mathf.Max(0.1f, halfZ));
        }

        /// <summary>Activa o desactiva la lectura del teclado para este jugador.</summary>
        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        /// <summary>Coloca el avatar en el centro del tablero.</summary>
        [ContextMenu("Centrar selección")]
        public void ResetSelection()
        {
            selectedTile = new Vector2Int(gridWidth / 2, gridHeight / 2);
            TeleportTo(new Vector3(0f, transform.position.y, 0f));
        }

        /// <summary>Teleporta el avatar a una posición de mundo (XZ), respetando los límites del piso.</summary>
        public void TeleportTo(Vector3 worldPosition)
        {
            transform.position = ClampMovement(new Vector3(worldPosition.x, transform.position.y, worldPosition.z));
        }

        /// <summary>Confirma la casilla enfocada (modo local).</summary>
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

        /// <summary>Acota el movimiento libre a las cotas amplias del piso, conservando la altura Y.</summary>
        private Vector3 ClampMovement(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, -movementHalfExtents.x, movementHalfExtents.x),
                position.y,
                Mathf.Clamp(position.z, -movementHalfExtents.y, movementHalfExtents.y));
        }

        // ------------------------------------------------------------------ entrada

        private Vector2 ReadMovementAxes()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Vector2 result = Vector2.zero;
                if (keyboard.wKey.isPressed) result.y += 1f;
                if (keyboard.sKey.isPressed) result.y -= 1f;
                if (keyboard.aKey.isPressed) result.x -= 1f;
                if (keyboard.dKey.isPressed) result.x += 1f;
                return result;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            Vector2 legacy = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) legacy.y += 1f;
            if (Input.GetKey(KeyCode.S)) legacy.y -= 1f;
            if (Input.GetKey(KeyCode.A)) legacy.x -= 1f;
            if (Input.GetKey(KeyCode.D)) legacy.x += 1f;
            return legacy;
#else
            return Vector2.zero;
#endif
        }

        private Vector2 ReadMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return Vector2.zero;
            return mouse.delta.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#else
            return Vector2.zero;
#endif
        }

        private bool ReadTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
                return keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab);
#else
            return false;
#endif
        }

        private bool ReadConfirmClick()
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

        private bool ReadConfirm()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
                return keyboard.spaceKey.wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
