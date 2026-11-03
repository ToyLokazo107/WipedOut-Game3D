namespace WipedOut;

/// <summary>Estado de la sala.</summary>
public static class MatchStatus
{
    public const string WaitingForGuest = "waiting_for_guest";
    public const string Playing = "playing";
    public const string Finished = "finished";
}

/// <summary>Tipos de jugada. Los cuatro primeros los puede pedir el cliente; los otros los registra solo el servidor.</summary>
public static class MoveKind
{
    public const string Place = "place";
    public const string Reveal = "reveal";
    public const string Validate = "validate";
    public const string Forfeit = "forfeit";

    // Solo el SERVIDOR las registra. Un cliente que las pida recibe "invalid_move".
    public const string Hide = "hide";
    public const string Timeout = "timeout";
    public const string Abandon = "abandon";
}

/// <summary>
/// Veredicto de una peticion. Las jugadas rechazadas viajan aqui, no como excepcion: no son errores,
/// son parte normal del juego y el cliente necesita distinguirlas.
/// </summary>
public static class MatchOutcome
{
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotStarted = "not_started";
    public const string MatchFinished = "match_finished";
    public const string Stale = "stale";
    public const string NotYourTurn = "not_your_turn";
    public const string WrongPhase = "wrong_phase";
    public const string InvalidCell = "invalid_cell";
    public const string CellOccupied = "cell_occupied";
    public const string AlreadyRevealed = "already_revealed";
    public const string InvalidMove = "invalid_move";
    public const string Conflict = "conflict";
}

/// <summary>Fase de la ronda tal y como viaja en la vista.</summary>
public static class PhaseName
{
    public const string None = "";
    public const string Placement = "placement";
    public const string Memorize = "memorize";
    public const string Search = "search";
    public const string Finished = "finished";
}

/// <summary>Que fue lo ultimo que paso en la partida. Se envia en la vista para animar y escribir mensajes.</summary>
public class LastEvent
{
    /// <summary>"joined" o un <see cref="MoveKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public int Seat { get; set; }
    public int Cell { get; set; } = -1;
    public bool WasMine { get; set; }
    public int LivesLost { get; set; }

    /// <summary>La jugada hizo pasar a la siguiente ronda (nivel completado o mina pisada).</summary>
    public bool LevelAdvanced { get; set; }
}

/// <summary>Una jugada ya aplicada. La lista de jugadas es solo de agregar: nunca se edita ni se borra.</summary>
public class MoveRecord
{
    /// <summary>Vacio en las jugadas que registra el servidor (ocultar, tiempo agotado, abandono).</summary>
    public string RequestId { get; set; } = string.Empty;

    public int Seat { get; set; }

    /// <summary>Un <see cref="MoveKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Casilla (en <see cref="MoveKind.Place"/> y <see cref="MoveKind.Reveal"/>); -1 en las demas.</summary>
    public int Cell { get; set; } = -1;

    public long AtUnixSeconds { get; set; }
}

/// <summary>Resultado de buscar un reintento: la jugada ya aplicada y el turno en que dejo la partida.</summary>
public class ProcessedRequest
{
    public string RequestId { get; set; } = string.Empty;
    public int ResultingTurnNumber { get; set; }
}

/// <summary>
/// Lo que se guarda en Cloud Save (custom data PRIVADO). La semilla solo sirve para elegir una casilla
/// cuando a un jugador se le acaba el tiempo al colocar una mina, pero igualmente no sale nunca del
/// servidor. A los clientes solo les llega <see cref="MatchView"/>.
///
/// No hay una foto del tablero: hay una <see cref="Seed"/> y la lista de <see cref="Moves"/>, que en cada
/// llamada se REPRODUCE con un motor nuevo (ver <c>MatchRules.Replay</c>) para obtener, de forma
/// determinista, el tablero, el turno y las vidas.
/// </summary>
public class MatchState
{
    public string MatchCode { get; set; } = string.Empty;

    /// <summary>Jugador 1 (asiento 1): el que crea la sala.</summary>
    public string HostPlayerId { get; set; } = string.Empty;

    /// <summary>Jugador 2 (asiento 2): el que se une.</summary>
    public string GuestPlayerId { get; set; } = string.Empty;

    public string CreatedByRequestId { get; set; } = string.Empty;

    public string Status { get; set; } = MatchStatus.WaitingForGuest;

    /// <summary>Secreta. 0 hasta que se une el rival.</summary>
    public int Seed { get; set; }

    public List<MoveRecord> Moves { get; set; } = new();

    /// <summary>Cuando empezo el turno actual (reloj del servidor). De aqui sale el plazo.</summary>
    public long TurnStartedAtUnixSeconds { get; set; }

    /// <summary>Cuando se coloco la ultima mina y empezo la memorizacion (reloj del servidor).</summary>
    public long MemorizeStartedAtUnixSeconds { get; set; }

    /// <summary>Ultima vez que el anfitrion llamo al servidor. Sirve para detectar abandono.</summary>
    public long HostLastActivityAtUnixSeconds { get; set; }

    public long GuestLastActivityAtUnixSeconds { get; set; }

    /// <summary>0 mientras no haya ganador; si no, el asiento (1 o 2) que gano.</summary>
    public int WinnerSeat { get; set; }

    /// <summary>"lives", "forfeit" o "abandoned".</summary>
    public string EndReason { get; set; } = string.Empty;

    public long CreatedAtUnixSeconds { get; set; }
    public long UpdatedAtUnixSeconds { get; set; }

    public int SeatOf(string playerId)
    {
        if (playerId.Length == 0) return 0;
        if (playerId == HostPlayerId) return 1;
        if (playerId == GuestPlayerId) return 2;
        return 0;
    }

    public bool IsParticipant(string playerId) => SeatOf(playerId) != 0;

    public string OpponentOf(string playerId)
    {
        return SeatOf(playerId) switch
        {
            1 => GuestPlayerId,
            2 => HostPlayerId,
            _ => string.Empty
        };
    }

    public long LastActivityOfSeat(int seat) => seat == 1 ? HostLastActivityAtUnixSeconds : GuestLastActivityAtUnixSeconds;

    public void SetLastActivityOfSeat(int seat, long unixSeconds)
    {
        if (seat == 1) HostLastActivityAtUnixSeconds = unixSeconds;
        else if (seat == 2) GuestLastActivityAtUnixSeconds = unixSeconds;
    }
}
