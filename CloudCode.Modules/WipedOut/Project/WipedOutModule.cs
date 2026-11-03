using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace WipedOut;

/// <summary>
/// Wiped out? online: juego de memoria por turnos para dos jugadores con el servidor como UNICA
/// autoridad. Sigue el patron de TTTXO:
///
///   - <c>CreateMatch</c> / <c>JoinMatch</c>: crean y llenan la sala (aqui por codigo de sala).
///   - <c>PlayMove</c>: valida y aplica una jugada con el mismo motor de reglas
///     (<c>Assets/Scripts/Rules</c>): colocar una mina, destapar una casilla, el boton rojo o abandonar.
///   - <c>GetMatchState</c>: devuelve la vista; sirve para reconectar y, como el servidor no tiene
///     relojes, tambien resuelve lo vencido (ocultar las minas, plazos de turno y abandono).
///   - **Push de Cloud Code (Wire):** al terminar cada jugada se avisa al rival con un mensaje LIVIANO
///     <c>{ type, matchId }</c> que nunca lleva el estado. El rival no se fia del contenido: lo usa como
///     senal para volver a pedir <c>GetMatchState</c>. Si el push falla, la jugada ya quedo guardada.
///
/// Todas las funciones devuelven el JSON de una <see cref="MatchView"/> (texto).
/// </summary>
public class WipedOutModule
{
    /// <summary>Tipo (y campo <c>type</c> del payload) de los mensajes push. El cliente filtra por el.</summary>
    public const string PushMessageType = "match_updated";

    private const int MaxCodeAttempts = 5;
    private const int MaxRequestIdLength = 64;

    private readonly MatchRepository m_Repository;
    private readonly IPushClient m_PushClient;
    private readonly ILogger<WipedOutModule> m_Logger;

    public WipedOutModule(IGameApiClient gameApiClient, IPushClient pushClient, ILogger<WipedOutModule> logger)
    {
        m_Repository = new MatchRepository(gameApiClient);
        m_PushClient = pushClient;
        m_Logger = logger;
    }

    /// <summary>Crea una sala y devuelve su codigo. Idempotente: el codigo se deriva del requestId.</summary>
    [CloudCodeFunction("CreateMatch")]
    public async Task<string> CreateMatch(IExecutionContext context, string requestId)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);
        var now = DateTime.UtcNow;

        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = MatchCode.FromRequestId(requestId, attempt);
            var stored = await m_Repository.LoadAsync(context, code);

            if (stored == null)
            {
                var state = MatchRules.NewMatch(code, playerId, requestId, now);
                if (!await m_Repository.TrySaveAsync(context, state, null))
                {
                    return Respond(state, playerId, MatchOutcome.Conflict,
                        "Otra escritura se adelanto al crear la sala. Vuelve a intentarlo.", now);
                }

                m_Logger.LogInformation("Sala {Code} creada por {PlayerId}", code, playerId);
                return Respond(state, playerId, MatchOutcome.Applied, string.Empty, now);
            }

            // Salio de esta misma peticion: el intento anterior si llego.
            if (stored.State.CreatedByRequestId == requestId && stored.State.HostPlayerId == playerId)
            {
                return Respond(stored.State, playerId, MatchOutcome.Replayed,
                    "Esta sala ya se habia creado con esta misma peticion.", now);
            }

            // Colision con la sala de otra persona: siguiente candidato.
        }

        throw new Exception("No se ha encontrado un codigo de sala libre. Reintenta con una peticion nueva.");
    }

    /// <summary>Une al jugador a una sala, elige la semilla del servidor y avisa al anfitrion.</summary>
    [CloudCodeFunction("JoinMatch")]
    public async Task<string> JoinMatch(IExecutionContext context, string matchId, string requestId)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);
        var code = RequireCode(matchId);
        var now = DateTime.UtcNow;

        var stored = await LoadOrThrowAsync(context, code);
        var state = stored.State;

        // Reconexion: quien ya participa (anfitrion o invitado) y vuelve a "unirse" con el codigo recibe el
        // estado actual en lugar de un error. Es lo que permite volver a una partida despues de cerrar la
        // app. No cambia nada, asi que un reintento es inofensivo.
        if (state.IsParticipant(playerId))
        {
            return Respond(state, playerId, MatchOutcome.Replayed, "Ya estabas en esta partida.", now);
        }

        if (state.GuestPlayerId.Length > 0)
        {
            throw new Exception("Esta sala ya tiene dos jugadores.");
        }

        // La semilla la elige el servidor con aleatoriedad criptografica.
        MatchRules.StartWithGuest(state, playerId, RandomNumberGenerator.GetInt32(1, int.MaxValue), now);

        if (!await m_Repository.TrySaveAsync(context, state, stored.WriteLock))
        {
            return await RespondConflictAsync(context, code, playerId, now);
        }

        m_Logger.LogInformation("{PlayerId} se unio a la sala {Code}", playerId, code);
        await NotifyOpponentAsync(context, state, playerId);
        return Respond(state, playerId, MatchOutcome.Applied, string.Empty, now);
    }

    /// <summary>Valida y aplica una jugada. El cliente envia solo la intencion; el servidor decide.</summary>
    /// <param name="kind"><c>place</c> y <c>reveal</c> (con <paramref name="cell"/>), <c>validate</c> (boton rojo) o <c>forfeit</c>.</param>
    /// <param name="requestId">
    /// Identificador de ESTA jugada: se reutiliza en los reintentos. Se comprueba antes que nada, porque si no un
    /// reintento legitimo se rechazaria con "no es tu turno" (la primera peticion si llego y el turno ya paso).
    /// </param>
    /// <param name="expectedTurnNumber">Turno sobre el que el cliente cree estar jugando.</param>
    [CloudCodeFunction("PlayMove")]
    public async Task<string> PlayMove(
        IExecutionContext context, string matchId, string requestId, int expectedTurnNumber, string kind, int cell)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);
        var code = RequireCode(matchId);
        var now = DateTime.UtcNow;

        var stored = await LoadOrThrowAsync(context, code);
        var state = stored.State;

        // 1. Ya aplicamos esta peticion?
        var processed = MatchRules.FindProcessed(state, requestId);
        if (processed != null)
        {
            return Respond(state, playerId, MatchOutcome.Replayed,
                $"Esta jugada ya se habia aplicado; dejo la partida en el turno {processed.ResultingTurnNumber}.", now);
        }

        // 2. Participas en esta partida?
        RequireParticipant(state, playerId);

        // 3. Lo vencido, turno y reglas del juego.
        var result = MatchRules.ApplyPlay(state, playerId, requestId, expectedTurnNumber, kind ?? string.Empty, cell, now);

        if (!result.Changed)
        {
            return Respond(state, playerId, result.Outcome, Describe(result.Outcome), now);
        }

        // 4. Guardar con write lock. Si otra escritura gano la carrera, se devuelve el estado REAL.
        if (!await m_Repository.TrySaveAsync(context, state, stored.WriteLock))
        {
            return await RespondConflictAsync(context, code, playerId, now);
        }

        // 5. Avisar al rival. Un fallo del push nunca estropea la jugada.
        if (result.GameChanged)
        {
            await NotifyOpponentAsync(context, state, playerId);
        }

        return Respond(state, playerId, result.Outcome, Describe(result.Outcome), now);
    }

    /// <summary>
    /// Devuelve el estado actual (la vista de quien llama). Ademas registra su actividad y aplica lo que ya
    /// vencio: ocultar las minas tras la memorizacion, plazos de turno y abandono del rival. Cloud Code no tiene
    /// temporizadores, asi que cualquier jugador que consulta "despierta" la partida.
    /// </summary>
    [CloudCodeFunction("GetMatchState")]
    public async Task<string> GetMatchState(IExecutionContext context, string matchId)
    {
        var playerId = RequirePlayer(context);
        var code = RequireCode(matchId);
        var now = DateTime.UtcNow;

        var stored = await LoadOrThrowAsync(context, code);
        var state = stored.State;
        RequireParticipant(state, playerId);

        var observed = MatchRules.Observe(state, playerId, now);
        if (observed.Changed)
        {
            if (!await m_Repository.TrySaveAsync(context, state, stored.WriteLock))
            {
                // Otra escritura se adelanto (por ejemplo, una jugada del rival). Una consulta no es una
                // jugada: basta devolver el estado real.
                var fresh = await LoadOrThrowAsync(context, code);
                return Respond(fresh.State, playerId, MatchOutcome.Applied, string.Empty, now);
            }

            if (observed.GameChanged)
            {
                await NotifyOpponentAsync(context, state, playerId);
            }
        }

        return Respond(state, playerId, MatchOutcome.Applied, string.Empty, now);
    }

    private async Task NotifyOpponentAsync(IExecutionContext context, MatchState state, string callerId)
    {
        var opponentId = state.OpponentOf(callerId);
        if (opponentId.Length == 0)
        {
            return;
        }

        try
        {
            // Payload liviano: solo "algo cambio en esta partida". Nunca lleva el estado.
            var payload = JsonConvert.SerializeObject(new { type = PushMessageType, matchId = state.MatchCode });
            await m_PushClient.SendPlayerMessageAsync(context, payload, PushMessageType, opponentId);
        }
        catch (Exception e)
        {
            // El rival se enterara en su siguiente GetMatchState (sondeo de respaldo).
            m_Logger.LogWarning(e, "No se pudo enviar el push a {PlayerId} en la sala {Code}", opponentId, state.MatchCode);
        }
    }

    private async Task<string> RespondConflictAsync(
        IExecutionContext context, string code, string playerId, DateTime now)
    {
        var fresh = await LoadOrThrowAsync(context, code);
        return Respond(fresh.State, playerId, MatchOutcome.Conflict,
            "Otra jugada se adelanto a la tuya. Este es el estado real; vuelve a intentarlo.", now);
    }

    private static string Respond(MatchState state, string playerId, string outcome, string message, DateTime now)
    {
        return MatchJson.Serialize(MatchRules.BuildView(state, playerId, outcome, message, now));
    }

    private async Task<StoredMatch> LoadOrThrowAsync(IExecutionContext context, string code)
    {
        return await m_Repository.LoadAsync(context, code)
               ?? throw new Exception($"No existe ninguna sala con el codigo {code}.");
    }

    private static string Describe(string outcome)
    {
        return outcome switch
        {
            MatchOutcome.NotStarted => "La partida todavia no ha empezado: falta el rival.",
            MatchOutcome.MatchFinished => "La partida ya termino.",
            MatchOutcome.Stale => "Tu jugada era sobre un estado viejo. Este es el estado actual.",
            MatchOutcome.NotYourTurn => "No es tu turno.",
            MatchOutcome.WrongPhase => "Esa accion no se puede hacer en esta fase de la ronda.",
            MatchOutcome.CellOccupied => "Ya hay una mina en esa casilla.",
            MatchOutcome.AlreadyRevealed => "Esa casilla ya esta destapada.",
            MatchOutcome.InvalidCell => "Esa casilla no existe en el tablero.",
            MatchOutcome.InvalidMove => "Jugada desconocida.",
            _ => string.Empty
        };
    }

    private static string RequirePlayer(IExecutionContext context)
    {
        if (string.IsNullOrEmpty(context.PlayerId))
        {
            throw new Exception("Hay que iniciar sesion para jugar.");
        }

        return context.PlayerId;
    }

    private static void RequireParticipant(MatchState state, string playerId)
    {
        if (!state.IsParticipant(playerId))
        {
            throw new Exception("No participas en esta partida.");
        }
    }

    private static void RequireRequestId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > MaxRequestIdLength)
        {
            throw new Exception($"requestId es obligatorio y admite hasta {MaxRequestIdLength} caracteres.");
        }
    }

    private static string RequireCode(string matchId)
    {
        var code = MatchCode.Normalize(matchId);
        if (!MatchCode.IsWellFormed(code))
        {
            throw new Exception("El codigo de sala no es valido.");
        }

        return code;
    }
}
