# GDD — Wiped out?

**Versión:** 2.0 (Actualizada a Cloud Code / Online)  
**Género:** Puzzle / Memoria / Competitivo / Estrategia  
**Jugadores:** 2 Jugadores (Multijugador Online por Turnos en Dispositivos Distintos)  
**Plataforma:** PC  
**Motor:** Unity 3D  
**Cámara:** 3D / Cámara Seguidora en Tercera Persona  
**Duración estimada:** 5–15 minutos por partida

## 1. Concepto del juego

Wiped out? es un juego competitivo de memoria y selección táctica para dos jugadores conectados en dispositivos distintos a través de internet (Online), basado en el minijuego de las minas de los Squid Craft Games.

Al comienzo de cada nivel, ambos jugadores colocan sus minas manualmente mediante clic del mouse sobre la grilla 3D de forma turnada. Cada colocación activa una animación y un indicador visual sincronizado para que el rival memorice la ubicación antes de que las minas se oculten.

En la fase de juego, los jugadores se turnan para hacer clic con el mouse en las casillas blancas (libres) que consideran seguras para revelarlas, mientras desplazan libremente a su avatar 3D con las teclas WASD. Cuando un jugador deduce que ya no quedan casillas seguras por destapar, arriesga presionando el Botón Rojo de Validación.

Cada jugador cuenta con 3 vidas. Gana el jugador que sobreviva mientras los niveles avanzan y el tablero crece.

## 2. Pilares del juego

- **Memoria y Observación:** Recordar la ubicación exacta de las minas colocadas por el rival durante la fase de animación inicial.
- **Interacción por Clic de Mouse:** Mecánica idéntica tanto para la colocación de minas como para la selección/revelación de casillas en tu turno.
- **Perspectiva 3D con WASD:** Navegación libre del personaje con cámara seguidora para inspeccionar el tablero desde cualquier ángulo.
- **Gestión del Riesgo (Botón Rojo):** Cualquier jugador puede intentar cerrar el nivel presionando el Botón Rojo si considera que solo quedan minas en el tablero.
- **Competencia Online Autoritativa (Cloud Code):** Enfrentamiento 1v1 en tiempo real donde el servidor valida cada tiro para garantizar un juego justo.

## 3. Objetivo y vidas

El objetivo principal es ser el último jugador con vidas en el tablero.

- **Vida Inicial:** Cada jugador comienza con 3 vidas.
- **Pérdida de Vida:**
  1. Si un jugador en su turno hace clic sobre una casilla que contiene una mina → Explota y pierde 1 vida.
  2. Si un jugador presiona el Botón Rojo de Validación y aún quedan casillas blancas seguras por destapar en el tablero.
- **Eliminación y Victoria:** Al llegar a 0 vidas, el jugador queda eliminado y el rival se proclama ganador de la partida.

## 4. Estructura de un nivel / ronda

1. Conexión Online y Matchmaking (Crear / Unirse a Sala por Código)
   ↓
2. Fase 1: Colocación de Minas (Turnos por Clic de Mouse + Animación Visual)
   ↓
3. Ocultar Minas (Servidor guarda ubicaciones en Cloud Save Protected)
   ↓
4. Fase 2: Búsqueda de Zonas Blancas (Turnos por Clic de Mouse + Movimiento WASD)
   ↓
5. Botón Rojo / Validación de Nivel

## 5. Fase de colocación manual de minas

Cada jugador ubica sus minas haciendo clic izquierdo con el mouse directamente sobre las casillas de la grilla 3D.

- **Animación e Indicador Visual:** Al colocar una mina, se dispara un efecto visual en esa casilla (sincronizado a través del servidor), permitiendo a ambos jugadores memorizar su posición antes de ocultarse.
- **Orden de Colocación por Nivel:**
  - Nivel 1: Empieza el Jugador 1 colocando sus 2 minas → luego el Jugador 2 sus 2 minas.
  - Nivel 2: Empieza el Jugador 2 colocando sus 3 minas → luego el Jugador 1 sus 3 minas.
  - Nivel 3+: Continúa rotando el turno de inicio en cada nivel.

## 6. Fase de búsqueda y selección de zonas blancas (por turnos)

Una vez ocultas las minas, inicia la fase de juego por turnos:

1. **Selección de Casillas con el Mouse (Mecánica principal):**
   - En su turno, el jugador usa el cursor del mouse para hacer clic sobre una casilla blanca (sin destapar) que recuerde como segura.
   - Si es una casilla segura: La casilla se revela limpia. El turno pasa automáticamente al rival.
   - Si es una mina: La casilla explota, el jugador pierde 1 vida y la ronda finaliza inmediatamente, avanzando al siguiente nivel.
2. **Movimiento del Personaje (WASD):**
   - El jugador puede desplazar su avatar 3D con las teclas WASD para acercarse, cambiar de ángulo y observar el tablero.
   - Nota de diseño: Caminar o pisar las casillas físicas NO activa minas ni revela casillas; la interacción es exclusivamente mediante el clic del mouse sobre el tablero.

## 7. Mecánica del Botón Rojo de Validación

Cuando un jugador considera en su turno que ya se seleccionaron/destaparon todas las zonas blancas seguras y que únicamente quedan minas, puede accionar el Botón Rojo de Validación:

- **Caso A — Conteo Correcto (No quedan zonas blancas seguras sin destapar):**
  - El servidor de Cloud Code valida la grilla.
  - Resultado: ¡Nivel completado! Ningún jugador pierde vidas y avanza al siguiente nivel (+1 a la grilla, +1 mina por jugador).
- **Caso B — Conteo Incorrecto (Aún queda al menos 1 zona blanca segura por tocar):**
  - El servidor detecta que la validación fue precipitada.
  - Resultado: El jugador que presionó el botón pierde 1 vida, y la ronda continúa.

## 8. Progresión de dificultad por nivel

| Nivel | Grilla | Minas/Jugador | Minas Totales | Inicia Colocación |
| --- | --- | --- | --- | --- |
| Nivel 1 | 3x3 (9 casillas) | 2 minas | 4 minas | Jugador 1 |
| Nivel 2 | 4x4 (16 casillas) | 3 minas | 6 minas | Jugador 2 |
| Nivel 3 | 5x5 (25 casillas) | 4 minas | 8 minas | Jugador 1 |
| Nivel 4+ | +1x+1 | +1 mina | +2 minas | Rotativo |

## 9. Arquitectura técnica (multijugador online cloud-based)

- **Motor:** Unity 3D (Cliente) + Unity Cloud Code Services (Backend Autorritativo C#).
- **Rol de los Dispositivos (Clientes Independientes):**
  - Ninguna PC actúa como Host. Ambos dispositivos se conectan como clientes independientes a los servicios en la nube de Unity.
- **Autoridad del Servidor:**
  - Las reglas de juego viven únicamente en `CloudCode.Modules`. El cliente nunca decide la posición de las minas ni la validez de un turno.
  - Los datos críticos (posiciones de minas y vidas) se guardan en Cloud Save Protected (inaccesibles por el jugador).
- **Manejo de Red y Desconexión:**
  - Cada jugada (PlayMove) valida un `requestId` único para prevenir errores por latencia o reintentos de red.
  - Las señales en tiempo real notifican los cambios de turno; si la red falla, el cliente vuelve a consultar `GetMatchState` al reconectarse.

## 10. Interfaz (UI)

- **Cabecera:** Vidas del Jugador 1, Nivel Actual / Tamaño de Grilla, Vidas del Jugador 2.
- **Centro:** Escenario 3D interactivo con personajes controlados por WASD y selección de casillas por Raycast del mouse.
- **Indicador de Estado:** Texto superior que indica el turno ("Turno de Jugador 1: Haz clic en una zona blanca", "Esperando al rival...", etc.).
- **Botón Rojo de Validación:** Botón destacado en pantalla para validar la resolución del nivel.
