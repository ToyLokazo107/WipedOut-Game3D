# Actividades de testing — partida online de Wiped out?

Este documento es para que **ustedes** prueben lo que trae el PR y nos cuenten qué pasó. Aquí hay código que
**compila y pasa pruebas automáticas**, pero que **nunca se ha ejecutado contra Unity Gaming Services (UGS) ni se
ha visto funcionando en pantalla**. Lo que ustedes encuentren es justo la retroalimentación que se necesita.

## Cómo reportar

Para cada prueba, respondan en el PR (o abran un *issue*) con este formato mínimo:

```
Prueba: T6
Resultado: OK / FALLA / A MEDIAS
Qué pasó: (una o dos frases)
Evidencia: captura de pantalla y/o texto de la consola de Unity (Window > General > Console)
Datos: código de sala, hora aproximada, qué cliente (editor o build)
```

Si algo **falla**, no lo arreglen primero en silencio: reporten. Un fallo bien descrito es más valioso que un parche
que no entienden. Si quieren corregirlo ustedes, háganlo en una rama propia y abran su PR (ver `docs/TAREAS.md`).

## Requisitos previos

- Unity **6000.3.22f1** (la de `WipedOut-Game3D/ProjectSettings/ProjectVersion.txt`). Al abrir, Unity actualizará
  `packages-lock.json` con los paquetes nuevos (Authentication, Cloud Code, Deployment): **es normal, súbanlo**.
- El proyecto **vinculado a un proyecto de UGS** (Edit > Project Settings > Services) con **Authentication y Cloud Code**
  habilitados en el dashboard de Unity. Usen un entorno de desarrollo.
- `dotnet` SDK instalado (para la prueba T1).
- Leer `docs/DIFERENCIAS-GDD.md`: algunas reglas son **supuestos** que no están en el GDD.

---

## Parte A — Sin servidor (rápidas, hagan estas primero)

### T1. Pruebas automáticas del módulo y del motor (`dotnet`)

- **Pasos:** desde la raíz del repo: `dotnet test CloudCode.Modules/WipedOut/WipedOut.sln`
- **Esperado:** `Correctas ... Con error: 0` (83 pruebas).
- **Si falla, reporten:** el nombre de la prueba y el mensaje completo.

### T2. Pruebas EditMode en Unity

- **Pasos:** abran `WipedOut-Game3D`, `Window > General > Test Runner` > pestaña **EditMode** > **Run All**.
- **Esperado:** todas en verde (son las del motor de reglas, 34).
- **Si falla, reporten:** nombre y error. Si hay errores de **compilación** en la Console, copien el primero.

### T3. El botón ONLINE aparece y no rompe el juego local

- **Pasos:** abran `SampleScene` y pulsen Play.
- **Esperado:**
  1. Aparece un botón **ONLINE** arriba a la derecha.
  2. El juego local (colocar minas y jugar con el mouse, WASD) funciona igual que antes.
  3. Al pulsar ONLINE se ve el lobby: título, campo **PROFILE**, **CREATE ROOM**, **CODE**, **JOIN** y **CLOSE**.
  4. **CLOSE** cierra el lobby y vuelve al juego.
- **Si falla, reporten:** captura (botón mal colocado, texto cortado, el lobby no abre, el clic del juego deja de
  funcionar) y la Console. *Ojo:* esta pantalla se construye por código (`Assets/Scripts/Online/OnlineMatchScreen.cs`)
  y es lo menos probado.

---

## Parte B — Contra UGS (necesita servidor desplegado)

### T4. Desplegar el módulo de Cloud Code

- **Pasos:** `Window > Deployment`, marcar `Assets/CloudCode/WipedOut.ccmr`, **Deploy**. (Alternativa con el UGS CLI:
  `ugs deploy WipedOut-Game3D/Assets/CloudCode/WipedOut.ccmr`.)
- **Esperado:** el módulo **WipedOut** aparece desplegado con `CreateMatch`, `JoinMatch`, `PlayMove` y `GetMatchState`.
- **Si falla, reporten:** el mensaje de error (suele ser de `dotnet`, NuGet o de permisos).

### Cómo montar dos clientes (T5 en adelante)

Necesitan **dos jugadores distintos**. Cada instalación entra como jugador anónimo distinto, pero **el Editor y una
build en la misma máquina comparten sesión** si no se les da un perfil distinto. Por eso el lobby tiene el campo
**PROFILE**: escriban `A` en un cliente y `B` en el otro **antes** de pulsar CREATE ROOM / JOIN (solo cuenta la primera
vez que se conecta ese proceso). Llamaremos **A** al que crea la sala y **B** al que se une.

### T5. Crear sala y unirse

- **Pasos:** A pulsa CREATE ROOM y anota el código de 4 letras. B lo escribe en CODE y pulsa JOIN.
- **Esperado:** A ve "WAITING FOR YOUR RIVAL" con el código; al unirse B, **A pasa solo a la fase de colocación** (le
  llegó el aviso) y le dice que le toca (**Nivel 1, 3×3, 2 minas por jugador**, empieza A).
- **Si falla, reporten:** quién no avanzó, cuánto esperaron, y los errores de Console de ambos.

### T6. Colocación de minas (se ven las del rival)

- **Pasos:** A coloca sus 2 minas (clic en dos casillas). Luego B coloca las suyas.
- **Esperado:** cada mina de A **aparece en azul** también en la pantalla de B (casi al instante), y las de B en rojo
  en la de A. No se puede colocar sobre una mina existente. Cuando B coloca la última, aparece **REMEMBER!** con
  una cuenta atrás de 5 s y las 4 minas siguen visibles.
- **Reporten:** si alguna mina no aparece en el otro cliente, cuánto tardó, y si dejó colocar sobre una ocupada.

### T7. Las minas se ocultan (la fase de memoria)

- **Pasos:** esperen a que termine la cuenta atrás de REMEMBER!.
- **Esperado:** las minas **desaparecen en los dos clientes** (todas las casillas quedan blancas), y A abre la búsqueda
  ("YOUR TURN: PICK A WHITE TILE...").
- **Reporten:** si en alguno las minas siguieron a la vista, o si la búsqueda tardó más de unos segundos en abrirse.

### T8. Destapar casillas

- **Pasos:** A destapa una casilla que **recuerda** como segura. Luego B hace lo mismo.
- **Esperado:** una **segura** se pone verde en ambos y pasa el turno. Una **mina** se pone naranja, el que la pisó
  **pierde una vida** y **la ronda termina**: empieza el nivel 2 (4×4, 3 minas por jugador) y **coloca primero B**.
- **Reporten:** si pisar una mina no terminó la ronda, o si el nivel 2 empezó con otro jugador.

### T9. Jugar fuera de turno

- **Pasos:** cuando le toca a B, A pulsa una casilla.
- **Esperado:** no pasa nada en el tablero y A lee "No es tu turno." El turno no cambia.
- **Reporten:** si el servidor aceptó la jugada (sería un fallo grave de autoridad).

### T10. El botón rojo

- **Pasos (error):** con casillas seguras todavía sin destapar, quien tiene el turno pulsa el botón rojo.
- **Esperado:** **pierde 1 vida** y la ronda **continúa** (el turno no cambia).
- **Pasos (acierto):** destapen **todas** las casillas seguras (en el nivel 1 son 5) y pulse el botón rojo quien tenga
  el turno. **Cualquiera de los dos puede hacerlo si es su turno.**
- **Esperado:** nivel completado, **nadie pierde vidas**, empieza el nivel siguiente.
- **Reporten:** si el botón rojo funcionó fuera de turno, o si la penalización cayó en el jugador equivocado.

### T11. Reintento por pérdida de red

- **Pasos:** A desconecta la red (modo avión o desactivar el adaptador) y destapa una casilla; reconecta la red y
  vuelve a pulsar **la misma casilla**.
- **Esperado:** la jugada cuenta **una sola vez**. No se pasan dos turnos ni se pierde la jugada.
- **Por qué importa:** la red pierde respuestas, no peticiones. El cliente reutiliza el mismo `requestId` en el
  reintento y el servidor lo reconoce (`replayed`).
- **Reporten:** si el turno avanzó dos veces o si la casilla quedó sin destapar.

### T12. Plazo de turno vencido

- **Pasos (buscando):** cuando le toca a A, **no hagan nada** durante ~25 s.
- **Esperado:** pasado el plazo, A pierde **1 vida** y el turno pasa a B. Lo resuelve el servidor, no el cliente.
- **Pasos (colocando):** en la fase de colocación, A **no coloca** una mina durante ~25 s.
- **Esperado:** el servidor **coloca una mina al azar** por A, **sin quitarle vidas**.
- **Reporten:** a los cuántos segundos se resolvió y si coincidían los contadores de ambos.

### T13. Abandonar

- **Pasos:** A pulsa FORFEIT (en cualquier momento, aunque no sea su turno).
- **Esperado:** la partida termina; A ve "YOU LOSE", B ve "YOU WIN!" y **se ven todas las minas** en ambos.

### T14. Un jugador desaparece (y, aparte, desaparecen los dos)

**T14a — se va solo uno (~2 minutos).** En plena partida cierren el cliente **B** y dejen **A abierto sin tocar nada**.
*Esperado:* los plazos de B se van venciendo y, al quedarse sin vidas, **A gana**. Lo importante es que la partida
**termine sola con A como ganador** y no se quede colgada.

**T14b — se van los dos (~4 minutos).** Cierren **los dos** clientes y esperen **más de 3 minutos**. Abran **solo A**
(mismo PROFILE), y en JOIN escriban el mismo código. *Esperado:* **A gana por abandono del rival** ("Tu rival se
desconectó."). Gana el **primero que vuelve**.

### T15. Reconexión

- **Pasos:** en plena partida, A cierra la app y la vuelve a abrir (en menos de 3 minutos), con el mismo PROFILE. En el
  lobby escribe el **mismo código** y pulsa JOIN.
- **Esperado:** A **vuelve a su partida** y ve el tablero como estaba. (El cliente no guarda el código: hay que
  escribirlo de nuevo. Es una mejora pendiente.)

### T16. La información oculta no sale del servidor (la más importante)

- **Pasos:** añadan temporalmente en `OnlineMatchService.CallAsync`, justo después de recibir `json`, la línea
  `Debug.Log(json);`. Jueguen y miren la Console. **No suban ese `Debug.Log`.**
- **Esperado:** en la fase de **búsqueda**, el texto de la respuesta **no contiene** `seed`, `moves`, `mines` ni
  `mineOwners`, y `cells` tiene `-1` en todas las casillas sin destapar (no distingue minas de seguras). Las minas
  sí aparecen en **colocación**, **memorización** y al **terminar** la partida.
- **Si falla, reporten:** el texto exacto. Si ven una mina oculta en la búsqueda, o la semilla, es un fallo de seguridad.
- **Para pensar:** ¿qué *sí* puede hacer un cliente trucado con lo que vio durante la memorización? ¿Se puede evitar?

### T17. Versión del protocolo

- **Pasos:** en el cliente A cambien `GameProtocol.Version` a `2` en `Assets/Scripts/Online/GameProtocol.cs` (no en el
  servidor) y pulsen CREATE ROOM. Después devuelvan la constante a `1`.
- **Esperado:** el cliente muestra "Tu versión del juego no coincide con el servidor. Actualiza el juego." y **no**
  intenta pintar la respuesta.

### T18. Trampa desde el cliente (opcional, para los curiosos)

- **Idea:** intenten hacer trampa llamando a `PlayMove` a mano: `kind = "timeout"`, `"hide"` o `"abandon"`; una casilla
  fuera del tablero (`cell = 9999`); destapar durante la memorización; o con el `matchId` de una partida ajena.
- **Esperado:** el servidor rechaza todo (`invalid_move`, `invalid_cell`, `wrong_phase`, error "No participas...").
- **Reporten:** cualquier cosa que el servidor **acepte** y no debería.

---

## Lo que ya sabemos que NO funciona o falta (no hace falta reportarlo)

- La pantalla online es un tablero **2D**; todavía no se dibuja en el tablero 3D de la escena.
- El movimiento WASD y las cámaras seguidoras no pasan por el servidor.
- El emparejamiento es por **código de sala**, no con Matchmaker.
- No hay barrido de partidas abandonadas ni limpieza de salas viejas.
- Los tiempos (5 s de memorización, 20 s por turno, 180 s de inactividad) son **supuestos** y están en constantes.
- El cliente no recuerda el código de sala al reabrir la app.
- No hay estadísticas ni rating todavía.
