# Diferencias entre el GDD, el código actual y la partida online

Al preparar la partida online aparecieron **tres fuentes que no coinciden del todo**: el GDD v1.3 (la referencia),
el código que ya existía en `GameManager`/`BoardController`, y lo que implementa la partida online de este PR.
Aquí queda anotado, regla por regla, para que **decidan ustedes** qué es lo correcto y actualicen el GDD o el
código. Mientras no decidan, el servidor sigue el GDD.

## Reglas

| Tema | GDD v1.3 | Código actual (`GameManager`) | Partida online (este PR) |
|---|---|---|---|
| **Pisar una mina** | Pierde 1 vida y **la ronda termina de inmediato**, avanzando al siguiente nivel (§6). | Pierde 1 vida y solo cambia el turno: **la ronda continúa**. | Sigue el **GDD**: -1 vida y pasa al siguiente nivel. |
| **Botón rojo: quién lo pulsa** | **Cualquier jugador, en su turno** (§2 y §7). | Solo el Jugador 1 (`NextLevelPlayer = 1`), y en cualquier momento. | Sigue el **GDD**: el jugador que tiene el turno. |
| **Botón rojo con casillas seguras pendientes** | Pierde 1 vida el que lo pulsó y la ronda continúa (§7). | Igual. | Igual. El turno **no cambia** (el GDD no lo dice; es un supuesto). |
| **Botón rojo sin casillas seguras pendientes** | Nivel completado, nadie pierde vidas, +1 grilla y +1 mina por jugador (§7). | Igual. | Igual. |
| **Qué cuenta como "pendiente"** | Las casillas blancas **seguras** sin destapar (§3 y §7). Las minas no cuentan. | Igual. | Igual. |
| **Colocación de minas** | Cada jugador coloca las suyas con clic; primero termina uno y luego el otro. El nivel 1 empieza el Jugador 1, el 2 el Jugador 2, y así (§5). | Igual en lo esencial. | Igual: se coloca una mina por turno de colocación, todas las de un jugador y después las del otro. |
| **Quién abre la búsqueda** | **No lo dice.** | Quien empezó la colocación (`StartingPlayer`). | Quien empezó la colocación. **Supuesto.** |
| **Memorización** | Las colocaciones son visibles "antes de que las minas se oculten" (§1 y §5). No da duración. | `memorizationTime` existe pero no se usa. | Tras colocar la última mina, las minas siguen **5 s** visibles y luego se ocultan. **Supuesto.** |
| **Tiempo por turno** | **No lo define.** | No existe. | 20 s al colocar y al buscar. Al vencer, colocando se coloca una mina al azar (sin quitar vida); buscando se pierde 1 vida y pasa el turno. **Supuesto.** |
| **Abandono / desconexión** | **No lo define.** | No existe. | Abandonar voluntariamente y, tras 180 s sin actividad, gana el rival. **Supuesto.** |
| **Niveles** | 3×3/2 minas, 4×4/3, 5×5/4, y +1 grilla +1 mina por jugador (§8). | Igual, con tope de grilla en 10. | Igual, con tope de grilla en 10 y sin superar nunca el total de casillas. |
| **Victoria / derrota** | 0 vidas = eliminado; gana el rival (§3). | Igual. | Igual. |

## Arquitectura

| Tema | GDD v1.3 (§9) | Este PR |
|---|---|---|
| **Red** | LAN con **Netcode for GameObjects**: un PC es el *host* (servidor y Jugador 1) y el otro un *client* por IP. | **Servidor autoritativo en Cloud Code** de Unity Gaming Services, con aviso por push, que es la arquitectura que enseña el curso. |
| **Quién valida** | El host. | El servidor de Cloud Code. |
| **Trampas** | Con host autoritativo, el host ve todo y podría hacer trampa. | El servidor no envía las minas en la fase de búsqueda y rechaza cualquier jugada inválida o fuera de turno. |
| **Requisitos** | Dos PC en la misma red local. | Un proyecto de UGS vinculado (Authentication, Cloud Code) y conexión a internet. |

**Hay que actualizar el GDD §9** o decidir otra cosa: ahora mismo el GDD dice una arquitectura y la partida
online hace otra. Si el equipo prefiere LAN con Netcode, esa es una decisión que hay que hablar con el profesor,
porque cambia por completo qué se entrega y cómo se evalúa.

## Lo que sigue sin sincronizarse

- **El movimiento WASD del personaje 3D y las cámaras seguidoras** (GDD §6 y §10) no pasan por el servidor. Son solo
  presentación local; el GDD ya dice que caminar no activa minas ni revela casillas.
- **Las animaciones de colocación** (GDD §5) las debe dibujar el cliente al recibir la vista: la vista trae las minas
  colocadas y el último evento.
- **Pantalla online en 3D:** la pantalla de este PR es un tablero 2D hecho por código. Dibujar la misma vista en el
  tablero 3D de la escena es una tarea de `docs/TAREAS.md`.

## `INSTRUCCIONES_DESARROLLO.md`

La hoja de ruta que hay en la raíz **describe otro juego**: minas aleatorias, `NEXT LEVEL` por rondas pares/impares
y memorización fija con `BoardConfig.memorizationTime`. Conviene actualizarla (o borrarla) cuando decidan las reglas.
