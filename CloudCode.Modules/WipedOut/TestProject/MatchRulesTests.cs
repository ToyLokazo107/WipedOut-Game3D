using System.Globalization;
using Newtonsoft.Json.Linq;
using WipedOut.Rules;

namespace WipedOut.Tests;

/// <summary>
/// Pruebas de las reglas de la sala online. Corren sin servidor ni Unity porque <see cref="MatchRules"/>
/// no sabe que existen Cloud Code ni Cloud Save.
/// </summary>
public class MatchRulesTests
{
    private const string Host = "player-host";
    private const string Guest = "player-guest";
    private const int Seed = 1234;

    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    private static MatchState Waiting() => MatchRules.NewMatch("ABCD", Host, "req-create", T0);

    private static MatchState Playing()
    {
        var state = Waiting();
        MatchRules.StartWithGuest(state, Guest, Seed, T0);
        return state;
    }

    private static MatchEngine EngineOf(MatchState state) => MatchRules.Replay(state).Engine!;

    private static MoveResult Play(MatchState s, string who, string req, int turn, string kind, int cell, double t) =>
        MatchRules.ApplyPlay(s, who, req, turn, kind, cell, At(t));

    private static MatchView ViewFor(MatchState s, string who) =>
        MatchRules.BuildView(s, who, MatchOutcome.Applied, "", T0);

    private static DateTime Parse(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    /// <summary>
    /// Nivel 1 (3x3): el anfitrión pone minas en 0 y 1, el invitado en 2 y 3. Las seguras son 4 a 8.
    /// Las 4 colocaciones ocurren en t = 1..4 y la memorización empieza en t = 4.
    /// </summary>
    private static MatchState Placed()
    {
        var s = Playing();
        Assert.That(Play(s, Host, "p1", 0, MoveKind.Place, 0, 1).Outcome, Is.EqualTo(MatchOutcome.Applied));
        Assert.That(Play(s, Host, "p2", 1, MoveKind.Place, 1, 2).Outcome, Is.EqualTo(MatchOutcome.Applied));
        Assert.That(Play(s, Guest, "p3", 2, MoveKind.Place, 2, 3).Outcome, Is.EqualTo(MatchOutcome.Applied));
        Assert.That(Play(s, Guest, "p4", 3, MoveKind.Place, 3, 4).Outcome, Is.EqualTo(MatchOutcome.Applied));
        return s;
    }

    /// <summary>Como <see cref="Placed"/>, pero ya con las minas ocultas (t = 9) y turno 5.</summary>
    private static MatchState Searching()
    {
        var s = Placed();
        MatchRules.ResolveTime(s, Host, At(4 + MatchRules.MemorizeSeconds));
        Assert.That(EngineOf(s).Phase, Is.EqualTo(Phase.Search));
        return s;
    }

    private static double SearchStart => 4 + MatchRules.MemorizeSeconds;

    // ---------------------------------------------------------------- arranque

    [Test]
    public void UnaSalaRecienCreadaEsperaAlRival()
    {
        var view = ViewFor(Waiting(), Host);

        Assert.That(view.Status, Is.EqualTo(MatchStatus.WaitingForGuest));
        Assert.That(view.MySeat, Is.EqualTo(1));
        Assert.That(view.Cells, Is.Empty, "todavía no hay tablero");
        Assert.That(view.Phase, Is.EqualTo(PhaseName.None));
    }

    [Test]
    public void AlUnirseElRivalEmpiezaLaColocacionDelJugadorUno()
    {
        var view = ViewFor(Playing(), Guest);

        Assert.That(view.Status, Is.EqualTo(MatchStatus.Playing));
        Assert.That(view.Phase, Is.EqualTo(PhaseName.Placement));
        Assert.That(view.MySeat, Is.EqualTo(2));
        Assert.That(view.Level, Is.EqualTo(1));
        Assert.That(view.Size, Is.EqualTo(3));
        Assert.That(view.CurrentSeat, Is.EqualTo(1));
        Assert.That(view.MinesPerPlayer, Is.EqualTo(2));
        Assert.That(view.Lives1, Is.EqualTo(3));
        Assert.That(view.Lives2, Is.EqualTo(3));
        Assert.That(Parse(view.TurnDeadlineUtc), Is.EqualTo(T0.AddSeconds(MatchRules.TurnSeconds)));
        Assert.That(view.LastEvent!.Kind, Is.EqualTo("joined"));
    }

    [Test]
    public void LaSemillaNoPuedeSerCero()
    {
        Assert.That(() => MatchRules.StartWithGuest(Waiting(), Guest, 0, T0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // -------------------------------------------------------------- colocación

    [Test]
    public void LasMinasColocadasSeVenEnLaVistaDeLosDosJugadores()
    {
        var state = Playing();
        Play(state, Host, "p1", 0, MoveKind.Place, 4, 1);

        foreach (var viewer in new[] { Host, Guest })
        {
            var view = ViewFor(state, viewer);
            Assert.That(view.Mines, Is.EqualTo(new[] { 4 }), "el GDD: el rival ve dónde se puso para memorizarla");
            Assert.That(view.MineOwners, Is.EqualTo(new[] { 1 }));
            Assert.That(view.Placed1, Is.EqualTo(1));
        }
    }

    [Test]
    public void LaColocacionSigueElOrdenYValidaCadaMina()
    {
        var state = Playing();

        Assert.That(Play(state, Guest, "g", 0, MoveKind.Place, 0, 1).Outcome, Is.EqualTo(MatchOutcome.NotYourTurn));
        Assert.That(Play(state, Host, "a", 0, MoveKind.Place, -1, 1).Outcome, Is.EqualTo(MatchOutcome.InvalidCell));
        Assert.That(Play(state, Host, "b", 0, MoveKind.Place, 9, 1).Outcome, Is.EqualTo(MatchOutcome.InvalidCell));
        Play(state, Host, "c", 0, MoveKind.Place, 4, 1);
        Assert.That(Play(state, Host, "d", 1, MoveKind.Place, 4, 2).Outcome, Is.EqualTo(MatchOutcome.CellOccupied));
        Assert.That(state.Moves, Has.Count.EqualTo(1), "solo queda registrada la jugada aplicada");
    }

    [Test]
    public void AlColocarLaUltimaMinaEmpiezaLaMemorizacion()
    {
        var state = Placed();
        var view = ViewFor(state, Host);

        Assert.That(view.Phase, Is.EqualTo(PhaseName.Memorize));
        Assert.That(view.CurrentSeat, Is.EqualTo(0));
        Assert.That(view.Mines, Has.Length.EqualTo(4));
        Assert.That(Parse(view.MemorizeDeadlineUtc), Is.EqualTo(At(4 + MatchRules.MemorizeSeconds)));
    }

    [Test]
    public void DuranteLaMemorizacionNoSeJuega()
    {
        var state = Placed();

        Assert.That(Play(state, Host, "r", 4, MoveKind.Reveal, 5, 5).Outcome, Is.EqualTo(MatchOutcome.WrongPhase));
        Assert.That(Play(state, Host, "v", 4, MoveKind.Validate, -1, 5).Outcome, Is.EqualTo(MatchOutcome.WrongPhase));
        Assert.That(Play(state, Host, "p", 4, MoveKind.Place, 8, 5).Outcome, Is.EqualTo(MatchOutcome.WrongPhase));
    }

    [Test]
    public void LasMinasSeOcultanSolasCuandoPasaElTiempoDeMemorizacion()
    {
        var state = Placed();

        Assert.That(MatchRules.ResolveTime(state, Host, At(4 + MatchRules.MemorizeSeconds - 1)), Is.False, "aún se memoriza");
        Assert.That(MatchRules.ResolveTime(state, Host, At(4 + MatchRules.MemorizeSeconds)), Is.True);

        Assert.That(state.Moves.Last().Kind, Is.EqualTo(MoveKind.Hide));
        Assert.That(state.Moves.Last().RequestId, Is.Empty, "la registra el servidor, no un cliente");
        Assert.That(ViewFor(state, Host).Phase, Is.EqualTo(PhaseName.Search));
    }

    // ------------------------------------------------- información oculta (clave)

    [Test]
    public void EnLaBusquedaLaVistaNoContieneMinasSemillaNiJugadas()
    {
        var state = Searching();

        foreach (var viewer in new[] { Host, Guest })
        {
            var json = MatchJson.Serialize(ViewFor(state, viewer));

            Assert.That(json, Does.Not.Contain("\"mines\""), "las minas ocultas no salen del servidor");
            Assert.That(json, Does.Not.Contain("\"mineOwners\""));
            Assert.That(json, Does.Not.Contain("seed").IgnoreCase);
            Assert.That(json, Does.Not.Contain(Seed.ToString()));
            Assert.That(json, Does.Not.Contain("moves").IgnoreCase);
        }
    }

    [Test]
    public void EnLaBusquedaTodasLasCasillasSinDestaparSeVenIgual()
    {
        var state = Searching();

        Assert.That(ViewFor(state, Host).Cells, Is.All.EqualTo(-1));
    }

    [Test]
    public void SoloLasMinasYaPisadasYLasSegurasDestapadasSeMuestranEnLaBusqueda()
    {
        var state = Searching();
        Play(state, Host, "r1", 5, MoveKind.Reveal, 4, SearchStart + 1);   // segura

        var view = ViewFor(state, Guest);

        Assert.That(view.Cells[4], Is.EqualTo(0));
        Assert.That(view.Cells.Count(c => c == -1), Is.EqualTo(8));
        Assert.That(view.Mines, Is.Null);
    }

    [Test]
    public void AlTerminarLaPartidaSiSeRevelanTodasLasMinas()
    {
        var state = Searching();
        Play(state, Guest, "ff", 5, MoveKind.Forfeit, -1, SearchStart + 1);

        var view = ViewFor(state, Host);

        Assert.That(view.Phase, Is.EqualTo(PhaseName.Finished));
        Assert.That(view.Mines, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void LaVistaSerializaEnCamelCaseYLlevaLaVersionDelProtocolo()
    {
        var json = MatchJson.Serialize(ViewFor(Playing(), Host));

        Assert.That(json, Does.Contain("\"matchCode\""));
        Assert.That(json, Does.Contain("\"turnNumber\""));
        Assert.That(json, Does.Contain("\"cells\""));
        Assert.That(json, Does.Contain($"\"protocolVersion\":{GameProtocol.Version}"));
        Assert.That(json, Does.Not.Contain("\"MatchCode\""));
    }

    [Test]
    public void ElEstadoGuardadoSobreviveAlViajeAJsonYDaLaMismaVista()
    {
        var state = Searching();
        Play(state, Host, "r1", 5, MoveKind.Reveal, 4, SearchStart + 1);

        // Es lo que hace MatchRepository: JObject.FromObject al guardar, ToObject al leer.
        var restored = JObject.FromObject(state).ToObject<MatchState>()!;

        Assert.That(MatchJson.Serialize(ViewFor(restored, Host)), Is.EqualTo(MatchJson.Serialize(ViewFor(state, Host))));
    }

    // ------------------------------------------------------------ replay (estado)

    [Test]
    public void ElEstadoEsLaSemillaMasLaListaDeJugadasYSeReproduce()
    {
        var state = Searching();
        Play(state, Host, "r1", 5, MoveKind.Reveal, 4, SearchStart + 1);
        Play(state, Guest, "r2", 6, MoveKind.Reveal, 5, SearchStart + 2);

        var replayed = EngineOf(state);

        Assert.That(state.Moves, Has.Count.EqualTo(7), "4 colocaciones, ocultar y 2 destapes");
        Assert.That(replayed.TurnNumber, Is.EqualTo(7));
        Assert.That(replayed.IsRevealed(4) && replayed.IsRevealed(5), Is.True);
        Assert.That(replayed.CurrentSeat, Is.EqualTo(1));
    }

    [Test]
    public void LaListaDeJugadasEsSoloDeAgregar()
    {
        var state = Placed();
        var first = JObject.FromObject(state.Moves[0]).ToString();

        MatchRules.ResolveTime(state, Host, At(SearchStart));
        Play(state, Host, "r1", 5, MoveKind.Reveal, 4, SearchStart + 1);

        Assert.That(state.Moves.Count, Is.EqualTo(6));
        Assert.That(JObject.FromObject(state.Moves[0]).ToString(), Is.EqualTo(first), "las jugadas anteriores no se tocan");
    }

    [Test]
    public void ElRegistroDeJugadasGuardaQuienQueYCuando()
    {
        var state = Playing();

        Play(state, Host, "mi-jugada", 0, MoveKind.Place, 7, 2);

        var move = state.Moves.Single();
        Assert.That(move.RequestId, Is.EqualTo("mi-jugada"));
        Assert.That(move.Seat, Is.EqualTo(1));
        Assert.That(move.Kind, Is.EqualTo(MoveKind.Place));
        Assert.That(move.Cell, Is.EqualTo(7));
        Assert.That(move.AtUnixSeconds, Is.EqualTo(MatchRules.Unix(At(2))));
    }

    // ---------------------------------------------------------------- búsqueda

    [Test]
    public void DestaparUnaCasillaSeguraPasaElTurnoYQuedaEnElUltimoEvento()
    {
        var state = Searching();

        var result = Play(state, Host, "r1", 5, MoveKind.Reveal, 4, SearchStart + 2);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Applied));
        Assert.That(result.GameChanged, Is.True);
        var view = ViewFor(state, Guest);
        Assert.That(view.CurrentSeat, Is.EqualTo(2));
        Assert.That(view.LastEvent!.Kind, Is.EqualTo(MoveKind.Reveal));
        Assert.That(view.LastEvent.Cell, Is.EqualTo(4));
        Assert.That(view.LastEvent.WasMine, Is.False);
        Assert.That(Parse(view.TurnDeadlineUtc), Is.EqualTo(At(SearchStart + 2 + MatchRules.TurnSeconds)),
            "el plazo del siguiente turno cuenta desde la jugada");
    }

    [Test]
    public void PisarUnaMinaCuestaUnaVidaTerminaLaRondaYEmpiezaElNivelDos()
    {
        var state = Searching();

        Play(state, Host, "r1", 5, MoveKind.Reveal, 0, SearchStart + 1);

        var view = ViewFor(state, Host);
        Assert.That(view.Lives1, Is.EqualTo(2));
        Assert.That(view.LastEvent!.WasMine, Is.True);
        Assert.That(view.LastEvent.LevelAdvanced, Is.True);
        Assert.That(view.Level, Is.EqualTo(2));
        Assert.That(view.Size, Is.EqualTo(4));
        Assert.That(view.Phase, Is.EqualTo(PhaseName.Placement));
        Assert.That(view.CurrentSeat, Is.EqualTo(2), "el nivel 2 lo abre el Jugador 2");
        Assert.That(view.MinesPerPlayer, Is.EqualTo(3));
        Assert.That(view.Cells, Is.All.EqualTo(-1));
    }

    [Test]
    public void JugarFueraDeTurnoSeRechazaSinTocarNada()
    {
        var state = Searching();
        var before = JObject.FromObject(state).ToString();

        var result = Play(state, Guest, "r1", 5, MoveKind.Reveal, 4, SearchStart + 1);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.NotYourTurn));
        Assert.That(result.Changed, Is.False);
        Assert.That(JObject.FromObject(state).ToString(), Is.EqualTo(before));
    }

    [Test]
    public void UnaJugadaSobreUnEstadoViejoSeRechazaComoStale()
    {
        var state = Searching();
        Play(state, Host, "r1", 5, MoveKind.Reveal, 4, SearchStart + 1);

        // El Jugador 2 cree estar todavía en el turno 5.
        var result = Play(state, Guest, "r2", 5, MoveKind.Reveal, 5, SearchStart + 2);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Stale));
    }

    [Test]
    public void LasCasillasInvalidasOYaDestapadasSeRechazan()
    {
        var state = Searching();

        Assert.That(Play(state, Host, "a", 5, MoveKind.Reveal, -1, SearchStart + 1).Outcome, Is.EqualTo(MatchOutcome.InvalidCell));
        Assert.That(Play(state, Host, "b", 5, MoveKind.Reveal, 9999, SearchStart + 1).Outcome, Is.EqualTo(MatchOutcome.InvalidCell));
        Play(state, Host, "c", 5, MoveKind.Reveal, 4, SearchStart + 1);
        Assert.That(Play(state, Guest, "d", 6, MoveKind.Reveal, 4, SearchStart + 2).Outcome, Is.EqualTo(MatchOutcome.AlreadyRevealed));
    }

    [Test]
    public void UnTipoDeJugadaDesconocidoSeRechaza()
    {
        var state = Searching();

        var result = Play(state, Host, "r1", 5, "teletransporte", 0, SearchStart + 1);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.InvalidMove));
        Assert.That(result.Changed, Is.False);
    }

    [Test]
    public void ElClienteNoPuedeFabricarseOcultarTimeoutNiAbandonoDelRival()
    {
        var state = Placed();
        var before = state.Moves.Count;

        foreach (var kind in new[] { MoveKind.Hide, MoveKind.Timeout, MoveKind.Abandon })
        {
            Assert.That(Play(state, Host, "x-" + kind, 4, kind, -1, 5).Outcome, Is.EqualTo(MatchOutcome.InvalidMove), kind);
        }

        Assert.That(state.Moves, Has.Count.EqualTo(before));
    }

    [Test]
    public void SinRivalNoSePuedeJugar()
    {
        var state = Waiting();

        Assert.That(Play(state, Host, "r1", 0, MoveKind.Place, 0, 0).Outcome, Is.EqualTo(MatchOutcome.NotStarted));
        Assert.That(Play(state, Host, "r2", 0, MoveKind.Validate, -1, 0).Outcome, Is.EqualTo(MatchOutcome.NotStarted));
        Assert.That(Play(state, Host, "r3", 0, MoveKind.Forfeit, -1, 0).Outcome, Is.EqualTo(MatchOutcome.NotStarted));
    }

    // ------------------------------------------------------------- botón rojo

    [Test]
    public void ElBotonRojoConSegurasPendientesCuestaUnaVidaYLaRondaSigue()
    {
        var state = Searching();

        var result = Play(state, Host, "v1", 5, MoveKind.Validate, -1, SearchStart + 1);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Applied));
        var view = ViewFor(state, Host);
        Assert.That(view.Lives1, Is.EqualTo(2));
        Assert.That(view.LastEvent!.Kind, Is.EqualTo(MoveKind.Validate));
        Assert.That(view.LastEvent.LivesLost, Is.EqualTo(1));
        Assert.That(view.Level, Is.EqualTo(1));
        Assert.That(view.CurrentSeat, Is.EqualTo(1), "el turno no cambia");
    }

    [Test]
    public void ElBotonRojoSinSegurasPendientesCompletaElNivelSinPerderVidas()
    {
        var state = Searching();
        var turn = 5;
        var seat = new[] { Host, Guest };
        for (var i = 0; i < 5; i++)
        {
            Assert.That(Play(state, seat[i % 2], $"r{i}", turn++, MoveKind.Reveal, 4 + i, SearchStart + 1 + i).Outcome,
                Is.EqualTo(MatchOutcome.Applied));
        }

        // Las 5 seguras destapadas: ahora le toca al Jugador 2.
        var result = Play(state, Guest, "v", turn, MoveKind.Validate, -1, SearchStart + 10);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Applied));
        var view = ViewFor(state, Guest);
        Assert.That(view.Level, Is.EqualTo(2));
        Assert.That(view.Lives1, Is.EqualTo(3));
        Assert.That(view.Lives2, Is.EqualTo(3));
        Assert.That(view.LastEvent!.LevelAdvanced, Is.True);
        Assert.That(view.Phase, Is.EqualTo(PhaseName.Placement));
    }

    [Test]
    public void ElBotonRojoSoloSePulsaEnTuTurno()
    {
        var state = Searching();

        Assert.That(Play(state, Guest, "v", 5, MoveKind.Validate, -1, SearchStart + 1).Outcome, Is.EqualTo(MatchOutcome.NotYourTurn));
    }

    // ----------------------------------------------------------- idempotencia

    [Test]
    public void UnaPeticionAplicadaQuedaRegistradaParaDetectarReintentos()
    {
        var state = Searching();
        Play(state, Host, "mi-jugada", 5, MoveKind.Reveal, 4, SearchStart + 1);

        var processed = MatchRules.FindProcessed(state, "mi-jugada");

        Assert.That(processed, Is.Not.Null);
        Assert.That(processed!.ResultingTurnNumber, Is.EqualTo(6));
        Assert.That(MatchRules.FindProcessed(state, "otra"), Is.Null);
    }

    [Test]
    public void UnaJugadaRechazadaNoSeRegistraComoAplicada()
    {
        var state = Searching();
        Play(state, Guest, "fuera-de-turno", 5, MoveKind.Reveal, 4, SearchStart + 1);

        Assert.That(MatchRules.FindProcessed(state, "fuera-de-turno"), Is.Null,
            "si se registrara, el reintento posterior (ya en su turno) se tomaría por repetido");
    }

    [Test]
    public void LasJugadasDelServidorNoSeConfundenConReintentos()
    {
        var state = Searching();
        MatchRules.ResolveTime(state, Host, At(SearchStart + MatchRules.TurnSeconds + MatchRules.GraceSeconds + 1));

        Assert.That(state.Moves.Last().Kind, Is.EqualTo(MoveKind.Timeout));
        Assert.That(state.Moves.Last().RequestId, Is.Empty);
        Assert.That(MatchRules.FindProcessed(state, ""), Is.Null);
    }

    // ------------------------------------------------------------------ tiempo

    [Test]
    public void DentroDelMargenDeCortesiaNoSeAplicaNingunTimeout()
    {
        var state = Searching();

        var changed = MatchRules.ResolveTime(state, Guest,
            At(SearchStart + MatchRules.TurnSeconds + MatchRules.GraceSeconds - 1));

        Assert.That(changed, Is.False);
    }

    [Test]
    public void AgotarElTiempoBuscandoCuestaUnaVidaYPasaElTurno()
    {
        var state = Searching();

        var changed = MatchRules.ResolveTime(state, Guest,
            At(SearchStart + MatchRules.TurnSeconds + MatchRules.GraceSeconds + 1));

        Assert.That(changed, Is.True);
        var view = ViewFor(state, Guest);
        Assert.That(view.Lives1, Is.EqualTo(2));
        Assert.That(view.CurrentSeat, Is.EqualTo(2));
        Assert.That(view.LastEvent!.Kind, Is.EqualTo(MoveKind.Timeout));
        Assert.That(Parse(view.TurnDeadlineUtc), Is.EqualTo(At(SearchStart + 2 * MatchRules.TurnSeconds)),
            "el plazo del rival se cuenta desde el vencimiento anterior, no desde 'ahora'");
    }

    [Test]
    public void AgotarElTiempoColocandoColocaUnaMinaAutomaticaSinQuitarVidas()
    {
        var state = Playing();

        var changed = MatchRules.ResolveTime(state, Guest, At(MatchRules.TurnSeconds + MatchRules.GraceSeconds + 1));

        Assert.That(changed, Is.True);
        var view = ViewFor(state, Guest);
        Assert.That(view.Placed1, Is.EqualTo(1), "se colocó una mina del Jugador 1");
        Assert.That(view.Lives1, Is.EqualTo(3), "colocar tarde no cuesta vidas");
        Assert.That(view.LastEvent!.Kind, Is.EqualTo(MoveKind.Timeout));
        Assert.That(view.Mines, Has.Length.EqualTo(1));
    }

    [Test]
    public void UnaJugadaTardiaSeRechazaPeroElTimeoutQuedaGuardado()
    {
        var state = Searching();

        // El anfitrión llega 30 s tarde con una jugada que estaba sobre el turno 5.
        var result = Play(state, Host, "tarde", 5, MoveKind.Reveal, 4, SearchStart + 30);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Stale));
        Assert.That(result.Changed, Is.True, "hay que persistir el timeout aunque la jugada se rechace");
        Assert.That(result.GameChanged, Is.True);
        Assert.That(EngineOf(state).Lives1, Is.EqualTo(2));
    }

    [Test]
    public void SiNadieJuegaLaPartidaTerminaSolaPorVidas()
    {
        var state = Playing();

        // Pasa un buen rato sin jugadas, pero con los dos "presentes": se encadenan las colocaciones
        // automáticas, la memorización y los timeouts de la búsqueda hasta que alguien se queda sin vidas.
        MatchRules.Touch(state, Host, MatchRules.Unix(At(1000)), force: true);
        MatchRules.Touch(state, Guest, MatchRules.Unix(At(1000)), force: true);
        MatchRules.ResolveTime(state, Host, At(1000));

        Assert.That(state.Status, Is.EqualTo(MatchStatus.Finished));
        Assert.That(state.EndReason, Is.EqualTo("lives"));
        Assert.That(state.WinnerSeat, Is.EqualTo(2), "turnos virtuales J1, J2, J1, J2, J1: el 1 llega a 0 primero");
    }

    // ------------------------------------------------- actividad y abandono

    [Test]
    public void ConsultarRegistraLaActividadPeroSoloSiLaMarcaEstaVieja()
    {
        // Una sala que espera rival: el anfitrión sondea y no hay turnos que puedan vencer.
        var state = Waiting();

        Assert.That(MatchRules.Observe(state, Host, At(5)).Changed, Is.False, "marca reciente: no se escribe");
        var late = MatchRules.Observe(state, Host, At(MatchRules.ActivityResolutionSeconds + 1));

        Assert.That(late.Changed, Is.True);
        Assert.That(late.GameChanged, Is.False, "solo cambió la actividad: no hay nada que avisar al rival");
    }

    [Test]
    public void SiElRivalLlevaMuchoSinActividadGanaQuienConsulta()
    {
        var state = Searching();

        // El invitado jugó por última vez en t = 4 (su última mina). El anfitrión sigue activo.
        var later = 4 + MatchRules.AbandonTimeoutSeconds + 5;
        MatchRules.Touch(state, Host, MatchRules.Unix(At(later)), force: true);
        MatchRules.Observe(state, Host, At(later + 1));

        Assert.That(state.Status, Is.EqualTo(MatchStatus.Finished));
        Assert.That(state.EndReason, Is.EqualTo("abandoned"));
        Assert.That(state.WinnerSeat, Is.EqualTo(1));
        Assert.That(state.Moves.Last().Kind, Is.EqualTo(MoveKind.Abandon));
        Assert.That(state.Moves.Last().Seat, Is.EqualTo(2));
    }

    [Test]
    public void SiSeFueronLosDosGanaElPrimeroQueVuelve()
    {
        var state = Searching();

        MatchRules.Observe(state, Guest, At(3600));

        Assert.That(state.EndReason, Is.EqualTo("abandoned"));
        Assert.That(state.WinnerSeat, Is.EqualTo(2), "quien vuelve gana; el anfitrión no estaba");
    }

    [Test]
    public void LaEsperaEnLaSalaNoCuentaComoInactividadDelAnfitrion()
    {
        var state = Waiting();

        MatchRules.StartWithGuest(state, Guest, Seed, At(300));
        MatchRules.Observe(state, Guest, At(301));

        Assert.That(state.Status, Is.EqualTo(MatchStatus.Playing), "el anfitrión no se declara abandonado nada más empezar");
    }

    // ----------------------------------------------------- abandono voluntario

    [Test]
    public void AbandonarDaLaVictoriaAlRivalEnCualquierMomento()
    {
        var state = Placed();

        var result = Play(state, Guest, "ff", 4, MoveKind.Forfeit, -1, 5);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Applied));
        Assert.That(state.Status, Is.EqualTo(MatchStatus.Finished));
        Assert.That(state.WinnerSeat, Is.EqualTo(1));
        Assert.That(state.EndReason, Is.EqualTo("forfeit"));
    }

    [Test]
    public void UnaPartidaTerminadaNoAceptaMasJugadas()
    {
        var state = Searching();
        Play(state, Guest, "ff", 5, MoveKind.Forfeit, -1, SearchStart + 1);

        Assert.That(Play(state, Host, "r", 6, MoveKind.Reveal, 4, SearchStart + 2).Outcome, Is.EqualTo(MatchOutcome.MatchFinished));
        Assert.That(Play(state, Host, "f", 6, MoveKind.Forfeit, -1, SearchStart + 2).Outcome, Is.EqualTo(MatchOutcome.MatchFinished));
    }

    // ------------------------------------------------------------ código/sala

    [Test]
    public void ElCodigoSeDerivaDelRequestIdYEsLegible()
    {
        var a = MatchCode.FromRequestId("abc");
        var b = MatchCode.FromRequestId("abc");

        Assert.That(a, Is.EqualTo(b), "reintentar 'crear sala' cae en el mismo código");
        Assert.That(MatchCode.IsWellFormed(a), Is.True);
        Assert.That(a, Does.Not.Contain("0").And.Not.Contain("1").And.Not.Contain("O").And.Not.Contain("I"));
        Assert.That(MatchCode.FromRequestId("abc", 1), Is.Not.EqualTo(a), "otro intento, otro candidato");
    }

    [Test]
    public void ElCodigoTecleadoSeNormaliza()
    {
        Assert.That(MatchCode.Normalize("  abcd "), Is.EqualTo("ABCD"));
        Assert.That(MatchCode.IsWellFormed("ab1d"), Is.False);
        Assert.That(MatchCode.IsWellFormed("ABC"), Is.False);
    }
}
