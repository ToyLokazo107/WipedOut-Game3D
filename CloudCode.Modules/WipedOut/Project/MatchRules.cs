using System.Globalization;
using WipedOut.Rules;

namespace WipedOut;

/// <summary>Resultado de intentar aplicar una jugada.</summary>
/// <param name="Outcome">Ver <see cref="MatchOutcome"/>.</param>
/// <param name="Changed">
/// Hay que guardar el estado. Puede ser true aunque la jugada se rechace: el servidor no tiene relojes,
/// asi que al recibir CUALQUIER peticion aplica antes lo que ya vencio (ocultar minas, plazos, abandono)
/// y eso tambien hay que persistirlo.
/// </param>
/// <param name="GameChanged">La partida cambio de verdad y hay que avisar al rival (no basta con la marca de actividad).</param>
public readonly record struct MoveResult(string Outcome, bool Changed, bool GameChanged);

/// <summary>Resultado de <see cref="MatchRules.Observe"/>.</summary>
public readonly record struct ObserveResult(bool Changed, bool GameChanged);

/// <summary>El motor tras reproducir la lista de jugadas, y que fue lo ultimo que paso.</summary>
public sealed record ReplayResult(MatchEngine? Engine, LastEvent? LastEvent);

/// <summary>
/// Las reglas de la sala online, sin ninguna dependencia de Cloud Code ni de Cloud Save, por lo que se
/// prueban sin servidor. Aqui es donde "el servidor es la autoridad": el motor (<see cref="MatchEngine"/>)
/// se ejecuta SOLO aqui, y al cliente solo le llega lo que <see cref="BuildView"/> decide.
///
/// El estado de una partida es la semilla mas la lista de jugadas, solo de agregar. Para saber como esta
/// la partida se REPRODUCE esa lista con un motor nuevo (<see cref="Replay"/>); no se guarda una foto.
/// </summary>
public static class MatchRules
{
    /// <summary>Plazo por turno, al colocar y al buscar. El GDD no lo define; se asume (pendiente de decidir).</summary>
    public const long TurnSeconds = 20;

    /// <summary>Margen para que una jugada enviada a tiempo no se pierda por la latencia.</summary>
    public const long GraceSeconds = 3;

    /// <summary>Segundos que las minas siguen visibles tras colocar la ultima. El GDD no lo define; se asume.</summary>
    public const long MemorizeSeconds = 5;

    /// <summary>Sin actividad de un jugador durante este tiempo, quien consulte le gana por abandono.</summary>
    public const long AbandonTimeoutSeconds = 180;

    /// <summary>Margen para no escribir en Cloud Save en cada sondeo solo por anotar la actividad.</summary>
    public const long ActivityResolutionSeconds = 30;

    /// <summary>Tope de eventos de tiempo encadenados que se resuelven de golpe.</summary>
    private const int MaxChainedTimeEvents = 60;

    public static long Unix(DateTime time) => new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeSeconds();

    public static string Iso(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    public static string Iso(DateTime time) => time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    public static MatchState NewMatch(string code, string hostPlayerId, string requestId, DateTime now)
    {
        var at = Unix(now);
        return new MatchState
        {
            MatchCode = code,
            HostPlayerId = hostPlayerId,
            CreatedByRequestId = requestId,
            Status = MatchStatus.WaitingForGuest,
            HostLastActivityAtUnixSeconds = at,
            CreatedAtUnixSeconds = at,
            UpdatedAtUnixSeconds = at
        };
    }

    /// <summary>Entra el segundo jugador y arranca la partida. La semilla la elige el servidor (no puede ser 0).</summary>
    public static void StartWithGuest(MatchState state, string guestPlayerId, int seed, DateTime now)
    {
        if (seed == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seed), "La semilla no puede ser 0.");
        }

        var at = Unix(now);
        state.GuestPlayerId = guestPlayerId;
        state.Seed = seed;
        state.Status = MatchStatus.Playing;
        state.TurnStartedAtUnixSeconds = at;

        // El tiempo que estuvo la sala esperando no cuenta como inactividad: si no, un anfitrion que
        // creo la sala hace 5 minutos se declararia "abandonado" en la primera llamada.
        state.HostLastActivityAtUnixSeconds = at;
        state.GuestLastActivityAtUnixSeconds = at;
        state.UpdatedAtUnixSeconds = at;
    }

    /// <summary>Reproduce la lista de jugadas con un motor nuevo. Determinista: misma semilla y jugadas, misma partida.</summary>
    public static ReplayResult Replay(MatchState state)
    {
        if (state.Seed == 0)
        {
            return new ReplayResult(null, null);
        }

        var engine = new MatchEngine(state.Seed);
        var last = new LastEvent { Kind = "joined", Seat = 2 };

        foreach (var move in state.Moves)
        {
            last = ApplyRecorded(engine, move);
        }

        return new ReplayResult(engine, last);
    }

    /// <summary>Busca si esta peticion ya se aplico (un reintento). Devuelve null si es nueva.</summary>
    public static ProcessedRequest? FindProcessed(MatchState state, string requestId)
    {
        var index = state.Moves.FindIndex(m => m.RequestId.Length > 0 && m.RequestId == requestId);
        if (index < 0)
        {
            return null;
        }

        // Cada jugada sube el numero de turno exactamente una vez.
        return new ProcessedRequest { RequestId = requestId, ResultingTurnNumber = index + 1 };
    }

    /// <summary>
    /// Se llama en cada peticion de un jugador (consultar o jugar): registra su actividad y resuelve lo
    /// que ya vencio. El servidor no tiene temporizadores (Cloud Code solo ejecuta cuando alguien lo
    /// llama), asi que se hace "perezosamente": se calcula que habria pasado si el reloj hubiera corrido.
    /// </summary>
    public static ObserveResult Observe(MatchState state, string playerId, DateTime now)
    {
        var touched = Touch(state, playerId, Unix(now), force: false);
        var gameChanged = ResolveTime(state, playerId, now);
        return new ObserveResult(touched || gameChanged, gameChanged);
    }

    /// <summary>Anota la actividad del jugador. Devuelve true si cambio algo que haya que guardar.</summary>
    public static bool Touch(MatchState state, string playerId, long nowUnix, bool force)
    {
        var seat = state.SeatOf(playerId);
        if (seat == 0)
        {
            return false;
        }

        if (!force && nowUnix - state.LastActivityOfSeat(seat) < ActivityResolutionSeconds)
        {
            return false;
        }

        state.SetLastActivityOfSeat(seat, nowUnix);
        return true;
    }

    /// <summary>
    /// Resuelve lo que ya vencio, en este orden:
    ///  1. <b>Abandono:</b> si el RIVAL de quien llama lleva mas de <see cref="AbandonTimeoutSeconds"/> sin
    ///     actividad, pierde por abandono. Va primero a proposito: si los dos se fueron, gana el primero que
    ///     vuelve (si se aplicaran antes los plazos, perderia quien primero dejo de jugar, aunque sea quien
    ///     vuelve). Quien llama esta activo por definicion, asi que solo se puede declarar abandonado al rival.
    ///  2. <b>Fin de la memorizacion:</b> pasados <see cref="MemorizeSeconds"/> las minas se ocultan.
    ///  3. <b>Plazos de turno:</b> colocando, se coloca una mina automatica (sin quitar vidas); buscando, el
    ///     jugador pierde una vida y pasa el turno. Cada plazo se cuenta desde el anterior, no desde "ahora".
    /// </summary>
    /// <returns>true si la partida cambio.</returns>
    public static bool ResolveTime(MatchState state, string callerId, DateTime now)
    {
        if (state.Status != MatchStatus.Playing)
        {
            return false;
        }

        var engine = Replay(state).Engine!;
        var nowUnix = Unix(now);
        var changed = false;

        var callerSeat = state.SeatOf(callerId);
        var rivalSeat = 3 - callerSeat;
        if (callerSeat != 0 && nowUnix - state.LastActivityOfSeat(rivalSeat) > AbandonTimeoutSeconds)
        {
            engine.Forfeit(rivalSeat);
            state.Moves.Add(new MoveRecord { Seat = rivalSeat, Kind = MoveKind.Abandon, AtUnixSeconds = nowUnix });
            state.UpdatedAtUnixSeconds = nowUnix;
            Finish(state, engine, "abandoned");
            return true;
        }

        for (var i = 0; i < MaxChainedTimeEvents && !engine.IsFinished; i++)
        {
            if (engine.Phase == Phase.Memorize)
            {
                var hideAt = state.MemorizeStartedAtUnixSeconds + MemorizeSeconds;
                if (nowUnix < hideAt)
                {
                    break;
                }

                engine.HideMines();
                state.Moves.Add(new MoveRecord { Seat = 0, Kind = MoveKind.Hide, AtUnixSeconds = hideAt });
                state.TurnStartedAtUnixSeconds = hideAt;
                changed = true;
                continue;
            }

            var deadline = state.TurnStartedAtUnixSeconds + TurnSeconds;
            if (nowUnix <= deadline + GraceSeconds)
            {
                break;
            }

            var who = engine.CurrentSeat;
            engine.ExpireTurn();
            state.Moves.Add(new MoveRecord { Seat = who, Kind = MoveKind.Timeout, AtUnixSeconds = deadline });
            state.TurnStartedAtUnixSeconds = deadline;
            if (engine.Phase == Phase.Memorize)
            {
                state.MemorizeStartedAtUnixSeconds = deadline;
            }

            changed = true;
        }

        if (changed)
        {
            state.UpdatedAtUnixSeconds = nowUnix;
            if (engine.IsFinished)
            {
                Finish(state, engine, "lives");
            }
        }

        return changed;
    }

    /// <summary>
    /// Aplica una jugada pedida por un cliente. Orden de las comprobaciones: estado de la sala, tipo de
    /// jugada, lo vencido, version del turno, y por ultimo las reglas del juego (fase, turno, casilla).
    /// </summary>
    public static MoveResult ApplyPlay(
        MatchState state, string playerId, string requestId, int expectedTurnNumber,
        string kind, int cell, DateTime now)
    {
        if (state.Status == MatchStatus.WaitingForGuest)
        {
            return new MoveResult(MatchOutcome.NotStarted, false, false);
        }

        if (state.Status == MatchStatus.Finished)
        {
            return new MoveResult(MatchOutcome.MatchFinished, false, false);
        }

        if (kind is not (MoveKind.Place or MoveKind.Reveal or MoveKind.Validate or MoveKind.Forfeit))
        {
            return new MoveResult(MatchOutcome.InvalidMove, false, false);
        }

        var nowUnix = Unix(now);
        var touched = Touch(state, playerId, nowUnix, force: false);
        var gameChanged = ResolveTime(state, playerId, now);
        var changed = touched || gameChanged;

        if (state.Status == MatchStatus.Finished)
        {
            return new MoveResult(MatchOutcome.MatchFinished, changed, gameChanged);
        }

        var engine = Replay(state).Engine!;
        var seat = state.SeatOf(playerId);

        // Abandonar se permite en cualquier momento, aunque no sea tu turno.
        if (kind == MoveKind.Forfeit)
        {
            engine.Forfeit(seat);
            Commit(state, engine, requestId, seat, MoveKind.Forfeit, -1, nowUnix);
            Finish(state, engine, "forfeit");
            return new MoveResult(MatchOutcome.Applied, true, true);
        }

        if (expectedTurnNumber != engine.TurnNumber)
        {
            return new MoveResult(MatchOutcome.Stale, changed, gameChanged);
        }

        string? rejection = kind switch
        {
            MoveKind.Place => Reject(engine.PlaceMine(seat, cell).Outcome),
            MoveKind.Reveal => Reject(engine.Reveal(seat, cell).Outcome),
            _ => Reject(engine.Validate(seat).Outcome)
        };

        if (rejection != null)
        {
            return new MoveResult(rejection, changed, gameChanged);
        }

        Commit(state, engine, requestId, seat, kind, kind is MoveKind.Place or MoveKind.Reveal ? cell : -1, nowUnix);
        if (engine.IsFinished)
        {
            Finish(state, engine, "lives");
        }

        return new MoveResult(MatchOutcome.Applied, true, true);
    }

    /// <summary>
    /// La vista que se envia al jugador <paramref name="viewerPlayerId"/>. Se construye desde cero y solo
    /// con datos publicos. Las minas solo se incluyen mientras el juego las muestra a proposito (colocacion,
    /// memorizacion y fin de partida): no existe ningun camino por el que lleguen en la fase de busqueda
    /// (hay pruebas que lo comprueban).
    /// </summary>
    public static MatchView BuildView(
        MatchState state, string viewerPlayerId, string outcome, string message, DateTime now)
    {
        var view = new MatchView
        {
            MatchCode = state.MatchCode,
            Status = state.Status,
            MySeat = state.SeatOf(viewerPlayerId),
            Outcome = outcome,
            Message = message,
            ServerTimeUtc = Iso(now),
            WinnerSeat = state.WinnerSeat,
            EndReason = state.EndReason
        };

        var replay = Replay(state);
        if (replay.Engine == null)
        {
            return view;
        }

        var engine = replay.Engine;
        view.LastEvent = replay.LastEvent;
        view.TurnNumber = engine.TurnNumber;
        view.Level = engine.Level;
        view.Size = engine.GridSize;
        view.Cells = engine.CellStates();
        view.MinesPerPlayer = engine.MinesPerPlayer;
        view.Placed1 = engine.PlacedBy(1);
        view.Placed2 = engine.PlacedBy(2);
        view.Lives1 = engine.Lives1;
        view.Lives2 = engine.Lives2;
        view.CurrentSeat = engine.CurrentSeat;
        view.Phase = engine.Phase switch
        {
            Phase.Placement => PhaseName.Placement,
            Phase.Memorize => PhaseName.Memorize,
            Phase.Search => PhaseName.Search,
            _ => PhaseName.Finished
        };

        if (engine.Phase is Phase.Placement or Phase.Search)
        {
            view.TurnDeadlineUtc = Iso(state.TurnStartedAtUnixSeconds + TurnSeconds);
        }

        if (engine.Phase == Phase.Memorize)
        {
            view.MemorizeDeadlineUtc = Iso(state.MemorizeStartedAtUnixSeconds + MemorizeSeconds);
        }

        if (engine.MinesVisible)
        {
            view.Mines = engine.MineCells();
            view.MineOwners = engine.MineOwners();
        }

        return view;
    }

    private static string? Reject(PlaceOutcome outcome) => outcome switch
    {
        PlaceOutcome.Placed => null,
        PlaceOutcome.WrongPhase => MatchOutcome.WrongPhase,
        PlaceOutcome.NotYourTurn => MatchOutcome.NotYourTurn,
        PlaceOutcome.InvalidCell => MatchOutcome.InvalidCell,
        PlaceOutcome.CellOccupied => MatchOutcome.CellOccupied,
        _ => MatchOutcome.MatchFinished
    };

    private static string? Reject(RevealOutcome outcome) => outcome switch
    {
        RevealOutcome.Safe or RevealOutcome.Mine => null,
        RevealOutcome.WrongPhase => MatchOutcome.WrongPhase,
        RevealOutcome.NotYourTurn => MatchOutcome.NotYourTurn,
        RevealOutcome.InvalidCell => MatchOutcome.InvalidCell,
        RevealOutcome.AlreadyRevealed => MatchOutcome.AlreadyRevealed,
        _ => MatchOutcome.MatchFinished
    };

    private static string? Reject(ValidateOutcome outcome) => outcome switch
    {
        ValidateOutcome.LevelCompleted or ValidateOutcome.TooEarly => null,
        ValidateOutcome.WrongPhase => MatchOutcome.WrongPhase,
        ValidateOutcome.NotYourTurn => MatchOutcome.NotYourTurn,
        _ => MatchOutcome.MatchFinished
    };

    private static LastEvent ApplyRecorded(MatchEngine engine, MoveRecord move)
    {
        switch (move.Kind)
        {
            case MoveKind.Place:
                engine.PlaceMine(move.Seat, move.Cell);
                return new LastEvent { Kind = MoveKind.Place, Seat = move.Seat, Cell = move.Cell };

            case MoveKind.Hide:
                engine.HideMines();
                return new LastEvent { Kind = MoveKind.Hide };

            case MoveKind.Reveal:
                var reveal = engine.Reveal(move.Seat, move.Cell);
                return new LastEvent
                {
                    Kind = MoveKind.Reveal,
                    Seat = move.Seat,
                    Cell = move.Cell,
                    WasMine = reveal.Outcome == RevealOutcome.Mine,
                    LivesLost = reveal.LivesLost,
                    LevelAdvanced = reveal.LevelAdvanced
                };

            case MoveKind.Validate:
                var validate = engine.Validate(move.Seat);
                return new LastEvent
                {
                    Kind = MoveKind.Validate,
                    Seat = move.Seat,
                    LivesLost = validate.LivesLost,
                    LevelAdvanced = validate.LevelAdvanced
                };

            case MoveKind.Timeout:
                var expired = engine.ExpireTurn();
                return new LastEvent
                {
                    Kind = MoveKind.Timeout,
                    Seat = move.Seat,
                    Cell = expired.Cell,
                    LivesLost = expired.LivesLost
                };

            case MoveKind.Forfeit:
            case MoveKind.Abandon:
                engine.Forfeit(move.Seat);
                return new LastEvent { Kind = move.Kind, Seat = move.Seat };

            default:
                throw new InvalidOperationException($"Jugada desconocida en el registro: '{move.Kind}'.");
        }
    }

    private static void Commit(MatchState state, MatchEngine engine, string requestId, int seat, string kind, int cell, long nowUnix)
    {
        state.Moves.Add(new MoveRecord
        {
            RequestId = requestId,
            Seat = seat,
            Kind = kind,
            Cell = cell,
            AtUnixSeconds = nowUnix
        });

        state.TurnStartedAtUnixSeconds = nowUnix;
        state.UpdatedAtUnixSeconds = nowUnix;

        // Al colocarse la ultima mina empieza la memorizacion.
        if (engine.Phase == Phase.Memorize)
        {
            state.MemorizeStartedAtUnixSeconds = nowUnix;
        }

        // Quien juega acaba de demostrar que esta activo, sin esperar al margen de escritura.
        state.SetLastActivityOfSeat(seat, nowUnix);
    }

    private static void Finish(MatchState state, MatchEngine engine, string reason)
    {
        state.Status = MatchStatus.Finished;
        state.WinnerSeat = engine.Winner;
        state.EndReason = reason;
    }
}
