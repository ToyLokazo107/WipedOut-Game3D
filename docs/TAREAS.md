# Tareas para seguir desarrollando — partida online de Wiped out?

Este PR deja una partida online que **funciona en las pruebas automáticas**, pero hay mucho por hacer. Las tareas
están repartidas entre los tres. Cada una explica **por qué** existe, **dónde** tocar, **qué hacer** y **cómo saber
que terminó**. No traen el código hecho a propósito: la idea es que lo entiendan, no que lo copien.

> **Antes de nada (los tres):** lean `CloudCode.Modules/WipedOut/README.md` y `docs/DIFERENCIAS-GDD.md`, y asegúrense
> de poder contestar, sin mirar el código:
> 1. ¿Por qué el servidor **no envía las minas** en la fase de búsqueda, pero **sí** al colocarlas y al memorizarlas?
> 2. ¿Qué *sí* puede hacer un cliente trucado con lo que vio al memorizar? ¿Se puede evitar?
> 3. ¿Por qué `PlayMove` comprueba el `requestId` **antes** que el turno?
> 4. ¿Por qué el aviso push no lleva el estado y qué pasa si se pierde?
> 5. ¿Por qué el estado es una **lista de jugadas** y no una foto del tablero?

## Cómo trabajamos

- **Cada uno en su propia rama** (`steve/...`, `erwin/...`, `salim/...`) y **un PR por tarea** hacia `main`. Nadie hace
  push directo a `main`, y nunca `--force`.
- **Commits pequeños y descriptivos** (qué cambia y por qué), no "advance".
- Antes de abrir el PR: `dotnet test CloudCode.Modules/WipedOut/WipedOut.sln` en verde y, si tocaron Unity, las pruebas
  EditMode también.
- **Quien revisa no es quien escribe:** cada PR lo revisa otra persona del grupo.
- Todo lo que descubran probando va como respuesta en el PR, con el formato de `docs/PRUEBAS.md`.

## Decisiones de grupo (háganlas primero, en papel, en `docs/DECISIONES.md`)

Respondan cada una con **una frase de justificación** y actualicen el GDD cuando decidan:

1. **¿Pisar una mina termina la ronda?** El GDD dice que sí; el código que tenían decía que no. Hoy manda el GDD.
2. **¿Quién puede pulsar el botón rojo?** El GDD dice cualquiera en su turno; el código decía solo el Jugador 1.
3. **¿Quién abre la fase de búsqueda?** El GDD no lo dice. Hoy: quien empezó la colocación.
4. **Tiempos:** 5 s de memorización, 20 s por turno y 180 s de inactividad son **supuestos**. ¿Qué valores quieren?
5. **Red:** el GDD §9 dice LAN con Netcode, pero esta partida usa Cloud Code. **Háblenlo con el profesor** antes de seguir.
6. **Emparejamiento:** ¿código de sala o Matchmaker de Unity? ¿Qué ganan y qué pierden?

---

## Steve — el tablero 3D

### V1. Dibujar la partida online en el tablero 3D

- **Por qué:** la pantalla online de este PR es un tablero 2D hecho por código, solo para poder probar. El juego
  es 3D, así que falta que el servidor "mande" también en el tablero 3D que ya hicieron.
- **Dónde:** `Assets/Scripts/Board/BoardController.cs` y `Tile3DController`/`PlayerController3D`. La vista llega por
  `OnlineMatchScreen.Service.ViewChanged` (ver `Assets/Scripts/Online/OnlineMatchModels.cs`).
- **Qué hacer:**
  1. Cuando llega una vista, **dibujen** el tablero 3D a partir de ella (casillas blancas, seguras destapadas, minas
     pisadas, y las minas colocadas mientras `phase` sea `placement` o `memorize`).
  2. El clic del mouse **no cambia nada por sí mismo**: solo llama a `PlaceMineAsync(cell)` o `RevealAsync(cell)` del
     servicio. La casilla cambia de color **cuando llega la vista nueva**, no antes.
  3. Las animaciones de colocación (GDD §5) salen de `lastEvent` (`kind`, `seat`, `cell`).
  4. El movimiento WASD y las cámaras siguen siendo locales.
- **Cómo saber que terminó:** dos clientes ven el mismo tablero 3D y, si uno trucara el cliente, el servidor
  seguiría rechazando las jugadas inválidas.
- **Para pensar:** ¿qué pasa en pantalla si llega una vista **vieja** (turno menor)? El servicio ya las descarta; compruébenlo.

### V2. Quitar la lógica duplicada

- **Por qué:** `GameManager` y `BoardController` tienen reglas (vidas, turnos, botón rojo) que ahora viven en
  `Assets/Scripts/Rules` y en el servidor. Tener dos copias hace que algún día no coincidan.
- **Qué hacer:** una vez que V1 funcione, decidan qué parte del juego local sigue usando `GameManager` (¿modo local contra
  un amigo?) y cuál se reemplaza por `WipedOut.Rules.MatchEngine`, que es C# puro y ya tiene pruebas.

---

## Salim — datos del servidor

### S1. Resultados de las partidas, calculados por el servidor

- **Por qué:** hoy no hay estadísticas. Cuando las haya (victorias, derrotas), no pueden escribirse desde el cliente: la
  clase de acceso *Default* de Cloud Save **la puede escribir el propio jugador** con su token, así que cualquiera
  podría ponerse 1000 victorias. Lo que decide el servidor va en **Protected** (el jugador lo lee, pero no lo escribe).
- **Dónde:** `CloudCode.Modules/WipedOut/Project/` (cuando una partida pasa a `finished`).
- **Qué hacer:**
  1. Decidan qué datos son **del servidor** (victorias, derrotas) y cuáles **del jugador** (nombre, preferencias).
  2. Cuando la partida termina, el **servidor** guarda el resultado de cada jugador en datos de jugador con clase de
     acceso **Protected**.
  3. Debe liquidarse **una sola vez**: guarden una marca de "ya liquidado" en `MatchState` para que un reintento no
     sume dos veces.
  4. Escriban una prueba de que liquidar dos veces no suma dos veces.
- **Cómo saber que terminó:** prueba manual: desde el cliente intenten guardar una victoria falsa y vean que Cloud Save
  **lo rechaza**.

---

## Erwin — pruebas, documentación y limpieza

### E1. Mover la reconexión a `MatchRules` y probarla

- **Por qué:** en `WipedOutModule.JoinMatch`, el caso "ya participas, así que te devuelvo el estado" (reconexión) está
  **escrito dentro del módulo**, donde no hay pruebas. La regla es tan simple que cabe en `MatchRules` (que **no**
  depende de Cloud Code y se prueba sin servidor). Es una buena primera tarea para entender cómo está dividido el código.
- **Dónde:** `CloudCode.Modules/WipedOut/Project/MatchRules.cs` y `.../TestProject/MatchRulesTests.cs`.
- **Qué hacer:** creen en `MatchRules` una función pura que diga si un jugador puede entrar, reingresar o debe ser
  rechazado, úsenla desde el módulo y escriban pruebas para: anfitrión que reingresa, invitado que reingresa, tercer
  jugador que intenta entrar a una sala llena, y sala sin invitado.
- **Cómo saber que terminó:** `dotnet test` en verde con las pruebas nuevas.

### E2. README del proyecto y `CLAUDE.md` del repo

- **Por qué:** el `README.md` de la raíz tiene dos líneas y no hay `CLAUDE.md`: quien llega al repo no sabe qué es ni
  cómo ejecutarlo, y Claude Code no conoce las reglas del equipo.
- **Qué hacer:** un `README.md` (qué es el juego, reglas, cómo abrirlo en Unity, cómo correr las pruebas, cómo vincular
  UGS y desplegar el módulo) y un `CLAUDE.md` con las convenciones: **las reglas viven solo en `Assets/Scripts/Rules`**
  (sin `UnityEngine`), el cliente nunca decide nada, el servidor no envía las minas en la búsqueda, y lo que decide el
  servidor va en Protected.
- **Cómo saber que terminó:** alguien que **no** conoce el proyecto lo abre y lo ejecuta siguiendo solo el README.

### E3. Barrido de partidas abandonadas

- **Por qué:** el abandono se resuelve cuando alguien consulta la partida. Si se van **los dos para siempre**, nadie
  consulta y la partida queda guardada eternamente.
- **Qué hacer:** **primero en papel** (`docs/DECISIONES.md`): ¿cómo sabe el barrido qué partidas siguen activas (hace
  falta un índice)? ¿cuándo se considera muerta una partida? ¿qué se borra? Después implementen una función de Cloud
  Code disparada por un **trigger programado** (consulten la documentación de Triggers de Cloud Code).
- **Cómo saber que terminó:** una prueba de la regla ("¿esta partida está muerta?") sin servidor y una prueba manual.

---

## Los tres — probar

Repártanse las pruebas de `docs/PRUEBAS.md`: **Salim T1–T8**, **Steve T9–T13**, **Erwin T14–T18**. Reporten en el PR con
el formato indicado: es lo que más falta, porque el código **nunca se ha ejecutado contra Unity Gaming Services**.

## Para más adelante

- **Configuración fuera del código:** `TurnSeconds`, `MemorizeSeconds` y `AbandonTimeoutSeconds` son constantes. En Remote
  Config se podrían cambiar sin volver a desplegar. Pregunta de diseño: ¿quién la lee, el cliente o solo el servidor?
  (Pista: si la leen los dos por separado, pueden discrepar.)
- **Reconexión sin escribir el código:** guardar en local el código de la sala y ofrecer un botón CONTINUE.
- **Versión del protocolo:** cada vez que cambien un parámetro de un RPC o la forma de la vista, suban
  `GameProtocol.Version` en el servidor **y** en el cliente a la vez. Hay una prueba que comprueba que coinciden.
