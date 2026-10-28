# Orden de Trabajo y Hoja de Ruta de Desarrollo (3D) — Wiped out?

## Contexto del Proyecto
- **Nombre:** Wiped out? (Versión 3D)
- **Motor:** Unity 3D (C#)
- **Documento Referencia:** `GDD.pdf` (en la raíz del repositorio)
- **Objetivo:** Implementar desde cero la arquitectura C# y el bucle de juego (MVP) para 2 jugadores locales en un tablero 3D competitivo de memoria y riesgo.

---

## Fases de Implementación Pendientes

### Paso 1: Estructura de Carpetas, Modelos y Configuración
**Archivos a crear:**
- `Assets/Scripts/Models/TileData.cs`
- `Assets/Scripts/Models/BoardConfig.cs`

**Instrucciones:**
1. Crear la estructura de carpetas: `Assets/Scripts/Core`, `Assets/Scripts/Models`, `Assets/Scripts/Board`, `Assets/Scripts/UI`.
2. Crear la clase `TileData.cs` que almacene:
   - `bool isMine`
   - `bool isRevealed`
   - `Vector2Int gridPosition`
   - `GameObject tileInstance`
3. Crear el ScriptableObject `BoardConfig.cs` con los parámetros por nivel:
   - `int gridWidth`, `int gridHeight`
   - `int mineCount`
   - `float memorizationTime`

**Verificación:**
- Crear instancias de `BoardConfig` en Unity para los Niveles 1 a 5 según la tabla de progresión del GDD (Sección 16).

---

### Paso 2: Administrador del Estado del Juego (Game Loop y Turnos)
**Archivos a crear:**
- `Assets/Scripts/Core/GameManager.cs`
- `Assets/Scripts/Core/GameState.cs` (Enum)

**Instrucciones:**
1. Crear un `GameManager` (Singleton) con máquina de estados: `Preparation`, `Memorization`, `PlayerTurn`, `Validation`, `GameOver`.
2. Controlar las 3 vidas de Player 1 y Player 2, así como la rotación de inicio y responsabilidad del botón `NEXT LEVEL`:
   - Rondas impares: Inicia y liquida Player 1.
   - Rondas pares: Inicia y liquida Player 2.
3. En `Memorization`, iniciar un temporizador según `BoardConfig.memorizationTime` para mostrar las minas y luego ocultarlas.

**Verificación:**
- Probar en la consola de Unity que los estados cambian en orden y la rotación de responsabilidad respeta cada ronda.

---

### Paso 3: Generador de Tablero 3D e Interacción con Raycast
**Archivos a crear:**
- `Assets/Scripts/Board/BoardController.cs`
- `Assets/Scripts/Board/Tile3DController.cs`

**Instrucciones:**
1. En `BoardController.cs`, escribir `GenerateBoard3D(BoardConfig config)` que instancie cubos/prefabs 3D en un grid centrado frente a la cámara.
2. Distribuir aleatoriamente `config.mineCount` minas.
3. En `Tile3DController.cs`, detectar clics usando `OnMouseDown()` o `Physics.Raycast`:
   - **Casilla Segura:** Cambiar color/animación 3D a "descubierta" y cambiar turno al rival.
   - **Casilla Mina:** Ejecutar efecto visual `BOOM!`, restar 1 vida al jugador activo, revelar la mina y cambiar turno al rival.

**Verificación:**
- Hacer clic sobre los objetos 3D en Play Mode; confirmar que las minas restan vidas, las casillas seguras revelan estado y no permiten doble clic.

---

### Paso 4: Lógica de Riesgo del Botón "NEXT LEVEL"
**Archivos a crear:**
- `Assets/Scripts/UI/UIManager.cs`

**Instrucciones:**
1. Crear el Canvas UI con el botón `NEXT LEVEL`. Solo debe habilitarse durante el turno del jugador responsable de la ronda.
2. Implementar `ValidateNextLevel()` en `GameManager.cs`:
   - Comprobar si existe alguna casilla 3D con `isRevealed == false`.
   - **Éxito:** Si no quedan casillas ocultas, avanzar de nivel/ronda y regenerar el tablero 3D.
   - **Error:** Si queda al menos 1 casilla oculta, restar 1 vida al jugador responsable y continuar la ronda.

**Verificación:**
- Presionar `NEXT LEVEL` con el tablero incompleto debe restar 1 vida.
- Presionarlo con todas las casillas descubiertas debe avanzar de nivel sin penalizaciones.