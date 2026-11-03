# Módulo WipedOut — servidor autoritativo de Wiped out? (Cloud Code)

Partida online por turnos de dos jugadores. **El servidor (Cloud Code) es la única autoridad**: ahí corren
las reglas y ahí vive el estado de la partida. Los clientes solo envían intenciones y pintan lo que el
servidor les cuenta. El aviso al rival va por **push de Cloud Code**.

## La idea (en 4 líneas)

1. **El cliente nunca decide nada.** Envía "quiero colocar una mina en la casilla 4" o "pulso el botón rojo".
2. **El servidor valida con las mismas reglas del juego** (`Assets/Scripts/Rules`, C# puro) y guarda el resultado.
3. **El estado es una lista de jugadas.** En cada llamada se reproduce con un motor nuevo; no se guarda una foto.
4. **El push solo avisa.** El rival no se fía del mensaje: vuelve a pedir `GetMatchState`.

## La información oculta de este juego

A diferencia de un tres en raya, aquí hay una parte de memoria, y por eso hay información que el servidor
**no debe enviar siempre**:

| Fase | ¿Se envían las minas a los clientes? |
|---|---|
| Colocación | **Sí**, las que ya se colocaron: el GDD pide que el rival vea dónde se puso cada una para memorizarla. |
| Memorización (5 s) | **Sí**, todas. |
| Búsqueda | **No.** Esa es la parte de memoria del juego: las minas están ocultas. |
| Partida terminada | **Sí**, todas. |

En la fase de búsqueda la vista solo trae, por casilla: `-1` blanca (sin destapar), `0` segura destapada,
`1` mina pisada. Hay pruebas que comprueban que en esa fase el JSON **no contiene** `mines`, `mineOwners`,
la semilla ni la lista de jugadas.

> Límite inevitable: un cliente trucado puede **guardarse** las posiciones que vio al colocar y al memorizar.
> Eso no se puede evitar (el juego se las muestra a propósito). Lo que el servidor sí garantiza es que nadie
> pueda consultarlas después, ni jugar fuera de turno, ni destapar casillas ya destapadas, ni inventarse vidas.

## Estructura

```
CloudCode.Modules/WipedOut/
├── Project/        módulo de Cloud Code (net6.0)
│   ├── WipedOutModule.cs   funciones expuestas + push liviano al rival
│   ├── MatchRules.cs       reglas de la sala, SIN dependencias de Cloud Code (se prueban sin servidor)
│   ├── MatchRepository.cs  Cloud Save custom data privado + write lock
│   ├── MatchState.cs       lo que se guarda: semilla + lista de jugadas
│   ├── MatchView.cs        lo único que ve un cliente
│   ├── MatchJson.cs        cómo viaja la vista (JSON camelCase)
│   ├── MatchCode.cs        código de sala derivado del requestId
│   └── GameProtocol.cs     versión del protocolo (gemela de la del cliente)
└── TestProject/    pruebas NUnit
```

El motor **no está duplicado**: `Project/WipedOut.csproj` compila directamente
`WipedOut-Game3D/Assets/Scripts/Rules/*.cs`, los mismos archivos que usa el juego en Unity. Esa carpeta tiene
su propia `asmdef` con `noEngineReferences`: si alguien escribe `using UnityEngine;` ahí, Unity no compila.

## Mensajes (cliente → servidor)

Todas las funciones devuelven el JSON de una `MatchView`. El servidor identifica al jugador por
`context.PlayerId` (el token), **nunca por un campo que mande el cliente**. `matchId` es el código de sala.

| Función | Parámetros | Qué hace |
|---|---|---|
| `CreateMatch` | `requestId` | Crea la sala (jugador 1) y devuelve su código. Idempotente: el código se deriva del `requestId`. |
| `JoinMatch` | `matchId`, `requestId` | Entra el jugador 2 y avisa al anfitrión. Si ya participas, devuelve el estado (reconexión). |
| `PlayMove` | `matchId`, `requestId`, `expectedTurnNumber`, `kind`, `cell` | Valida y aplica una jugada. `kind` = `place`, `reveal` (con `cell`), `validate` (botón rojo) o `forfeit`. |
| `GetMatchState` | `matchId` | Devuelve la vista. Registra la actividad de quien llama y resuelve lo vencido. |

`outcome` de la respuesta: `applied`, `replayed`, `not_started`, `match_finished`, `stale`, `not_your_turn`,
`wrong_phase`, `invalid_cell`, `cell_occupied`, `already_revealed`, `invalid_move`, `conflict`. Los rechazos
viajan ahí, no como excepción: no son errores, son parte normal del juego.

El servidor también registra por su cuenta las jugadas `hide` (ocultar las minas), `timeout` y `abandon`; el
cliente **no puede pedirlas** (recibe `invalid_move`).

## Mensajes (servidor → cliente): push de Cloud Code

Al terminar una jugada que cambió la partida, el servidor envía **al rival**:

```
IPushClient.SendPlayerMessageAsync(context, "{ \"type\": \"match_updated\", \"matchId\": \"ABCD\" }", "match_updated", rivalPlayerId)
```

- **El payload es liviano y nunca lleva el estado.** El cliente no se fía de su contenido: lo usa como señal y
  vuelve a pedir `GetMatchState`.
- El cliente se suscribe con `CloudCodeService.Instance.SubscribeToPlayerMessagesAsync` y filtra por `MessageType`.
- **Sondeo de respaldo** (~10 s) por si se pierde un push.
- Si el push falla, la jugada **no** se estropea: ya quedó guardada.
- Solo funciona con la app abierta.

## Estado: semilla + lista de jugadas (replay)

`MatchState` guarda la **semilla** y `Moves`, una lista **solo de agregar** (`place`, `hide`, `reveal`,
`validate`, `timeout`, `forfeit`, `abandon`, con asiento, casilla y hora). En cada llamada `MatchRules.Replay` la
reproduce con un motor nuevo y se obtiene, de forma determinista, el tablero, el turno y las vidas. La semilla
solo se usa para elegir una casilla cuando a un jugador se le acaba el tiempo al colocar una mina.

## Orden de validación en `PlayMove`

1. ¿Ya procesé este `requestId`? → `replayed` (**antes** que nada: si no, un reintento legítimo se rechazaría con
   "no es tu turno" porque la primera petición sí llegó).
2. ¿Participas en la partida? Si no, error.
3. ¿Empezó y no terminó? → `not_started` / `match_finished`.
4. Se registra tu actividad y se resuelve lo vencido (abandono del rival, fin de la memorización y plazos).
5. ¿`expectedTurnNumber` coincide? → `stale`.
6. Las reglas del juego, en el motor: fase, turno, casilla válida y libre.
7. Se guarda con **write lock**; si otra escritura ganó la carrera → `conflict` (con el estado real).
8. Se avisa al rival por push.

## Tiempos, actividad y abandono (reloj del servidor)

Cloud Code solo ejecuta cuando alguien lo llama: **no hay temporizadores**. Todo se resuelve de forma
**reactiva**: cualquier `GetMatchState` o `PlayMove` calcula primero qué habría pasado si el reloj hubiera corrido.

- **Memorización:** pasados 5 s (`MatchRules.MemorizeSeconds`) de colocada la última mina, el servidor registra `hide`
  y empieza la búsqueda.
- **Plazo de turno:** 20 s (`MatchRules.TurnSeconds`) + 3 s de margen por la latencia. Colocando, vencerlo coloca
  una mina automática en una casilla libre (sin quitar vidas). Buscando, cuesta una vida y pasa el turno.
- **Abandono por inactividad:** cada llamada registra la actividad del jugador. Si el rival lleva más de 180 s sin
  actividad, **quien consulta gana**. Se comprueba **antes** que los plazos: si los dos se fueron, gana el primero
  que vuelve.
- **Abandono voluntario:** `PlayMove` con `kind = forfeit`, en cualquier momento.
- **Reconexión:** el jugador vuelve con el código de sala (`JoinMatch`).

> El GDD no define estos tiempos. **Son supuestos** que el equipo debe confirmar o cambiar (ver `docs/DIFERENCIAS-GDD.md`).

## Versión del protocolo

`GameProtocol.Version` existe en el servidor (`Project/GameProtocol.cs`) y en el cliente
(`Assets/Scripts/Online/GameProtocol.cs`). Cada vista lleva la versión del servidor; si no coincide con la del
cliente, este avisa de que hay que actualizar en lugar de interpretar mal la respuesta. **Hay que subir las dos a
la vez cuando cambie el contrato de un RPC** (una prueba comprueba que coinciden).

## Probar

```bash
dotnet test CloudCode.Modules/WipedOut/WipedOut.sln
```

Corren sin servidor ni Unity: las pruebas del motor (`Assets/Tests/EditMode`), las de `MatchRules` (fases, turnos,
idempotencia, plazos, abandono, **que la vista no contenga las minas ocultas**) y las de contrato con el cliente.

## Desplegar

1. En Unity, con el paquete Deployment, abrir *Deployment* y desplegar `Assets/CloudCode/WipedOut.ccmr`
   (empaqueta `WipedOut.sln`). También con el UGS CLI: `ugs deploy WipedOut-Game3D/Assets/CloudCode/WipedOut.ccmr`.
2. El proyecto debe estar **vinculado a un proyecto de Unity Gaming Services** (Edit > Project Settings > Services)
   con Authentication y Cloud Code habilitados.
3. El módulo usa Cloud Save (`AddGameApiClient`) y mensajes push (`IPushClient`, ya registrado por defecto con
   Core 0.0.5 y Apis 0.0.26).

## Qué NO está probado ni cubierto

- **No se ha ejecutado contra un proyecto UGS real.** Lo verificado: compila contra las librerías de Cloud Code, las
  reglas pasan sus pruebas y el contrato con los modelos del cliente coincide. Falta probar el flujo con dos clientes.
- **Matchmaker:** el emparejamiento es por código de sala, no por ticket.
- **Barrido de partidas abandonadas** y limpieza de salas terminadas: no existen. Solo hay abandono reactivo.
- **Estadísticas, rating y premios:** no existen todavía. Cuando existan, deben calcularse en el servidor y guardarse en
  clase *Protected* de Cloud Save (la clase *Default* la puede escribir el propio jugador).
- Tiempos y umbrales son constantes; podrían vivir en Remote Config.
- Política de acceso de Cloud Code para restringir quién llama a las funciones.
- Creación simultánea del mismo código de sala (límite conocido del patrón de código de sala).
