using System.Linq;
using NUnit.Framework;
using WipedOut.Rules;

namespace WipedOut.Tests.EditMode
{
    public class LevelRulesTests
    {
        [Test]
        public void LaProgresionSigueElGdd()
        {
            // GDD sección 8: nivel 1 = 3x3 y 2 minas por jugador; nivel 2 = 4x4 y 3; nivel 3 = 5x5 y 4.
            Assert.That(LevelRules.GridSize(1), Is.EqualTo(3));
            Assert.That(LevelRules.GridSize(2), Is.EqualTo(4));
            Assert.That(LevelRules.GridSize(3), Is.EqualTo(5));
            Assert.That(LevelRules.MinesPerPlayer(1), Is.EqualTo(2));
            Assert.That(LevelRules.MinesPerPlayer(2), Is.EqualTo(3));
            Assert.That(LevelRules.MinesPerPlayer(3), Is.EqualTo(4));
        }

        [Test]
        public void QuienColocaPrimeroRotaPorNivel()
        {
            Assert.That(LevelRules.FirstPlacer(1), Is.EqualTo(1));
            Assert.That(LevelRules.FirstPlacer(2), Is.EqualTo(2));
            Assert.That(LevelRules.FirstPlacer(3), Is.EqualTo(1));
            Assert.That(LevelRules.FirstPlacer(4), Is.EqualTo(2));
        }

        [Test]
        public void LaGrillaTieneUnTope()
        {
            Assert.That(LevelRules.GridSize(50), Is.EqualTo(LevelRules.MaxGridSize));
        }

        [Test]
        public void SiempreQuedaAlMenosUnaCasillaSegura()
        {
            for (int level = 1; level <= 100; level++)
            {
                int cells = LevelRules.GridSize(level) * LevelRules.GridSize(level);
                int mines = LevelRules.MinesPerPlayer(level) * 2;
                Assert.That(cells - mines, Is.GreaterThanOrEqualTo(1), "nivel " + level);
            }
        }

        [Test]
        public void UnNivelNoValidoSeTrataComoElPrimero()
        {
            Assert.That(LevelRules.GridSize(0), Is.EqualTo(LevelRules.GridSize(1)));
            Assert.That(LevelRules.FirstPlacer(-3), Is.EqualTo(1));
        }
    }

    public class MatchEngineTests
    {
        // Nivel 1: 3x3, minas del Jugador 1 en 0 y 1, minas del Jugador 2 en 2 y 3. Seguras: 4 a 8.
        private static MatchEngine Placed(int seed = 1)
        {
            var match = new MatchEngine(seed);
            Assert.That(match.PlaceMine(1, 0).Outcome, Is.EqualTo(PlaceOutcome.Placed));
            Assert.That(match.PlaceMine(1, 1).Outcome, Is.EqualTo(PlaceOutcome.Placed));
            Assert.That(match.PlaceMine(2, 2).Outcome, Is.EqualTo(PlaceOutcome.Placed));
            Assert.That(match.PlaceMine(2, 3).Outcome, Is.EqualTo(PlaceOutcome.Placed));
            return match;
        }

        private static MatchEngine Searching(int seed = 1)
        {
            MatchEngine match = Placed(seed);
            Assert.That(match.HideMines(), Is.True);
            return match;
        }

        // ------------------------------------------------------------ arranque y colocación

        [Test]
        public void EmpiezaEnElNivelUnoColocandoElJugadorUno()
        {
            var match = new MatchEngine(1);

            Assert.That(match.Level, Is.EqualTo(1));
            Assert.That(match.GridSize, Is.EqualTo(3));
            Assert.That(match.Phase, Is.EqualTo(Phase.Placement));
            Assert.That(match.CurrentSeat, Is.EqualTo(1));
            Assert.That(match.Lives1, Is.EqualTo(3));
            Assert.That(match.Lives2, Is.EqualTo(3));
            Assert.That(match.TurnNumber, Is.EqualTo(0));
        }

        [Test]
        public void CadaJugadorColocaTodasSusMinasUnoDetrasDeOtro()
        {
            var match = new MatchEngine(1);

            match.PlaceMine(1, 0);
            Assert.That(match.CurrentSeat, Is.EqualTo(1), "al Jugador 1 aún le queda una mina");
            match.PlaceMine(1, 1);
            Assert.That(match.CurrentSeat, Is.EqualTo(2), "terminó el Jugador 1, sigue el 2");
            match.PlaceMine(2, 2);
            Assert.That(match.Phase, Is.EqualTo(Phase.Placement));
            match.PlaceMine(2, 3);

            Assert.That(match.Phase, Is.EqualTo(Phase.Memorize));
            Assert.That(match.CurrentSeat, Is.EqualTo(0), "la memorización no tiene turno");
            Assert.That(match.MinesVisible, Is.True);
        }

        [Test]
        public void ElNivelDosEmpiezaColocandoElJugadorDos()
        {
            MatchEngine match = Searching();
            match.Validate(1);   // hay seguras sin destapar: pierde una vida, sigue el nivel 1
            Assert.That(match.Level, Is.EqualTo(1));

            // Pisar una mina termina la ronda y pasa al nivel 2.
            match.Reveal(1, 0);

            Assert.That(match.Level, Is.EqualTo(2));
            Assert.That(match.GridSize, Is.EqualTo(4));
            Assert.That(match.Phase, Is.EqualTo(Phase.Placement));
            Assert.That(match.CurrentSeat, Is.EqualTo(2), "los niveles pares los abre el Jugador 2");
        }

        [Test]
        public void ColocarFueraDeTurnoSeRechaza()
        {
            var match = new MatchEngine(1);

            Assert.That(match.PlaceMine(2, 0).Outcome, Is.EqualTo(PlaceOutcome.NotYourTurn));
            Assert.That(match.PlacedBy(2), Is.EqualTo(0));
            Assert.That(match.TurnNumber, Is.EqualTo(0));
        }

        [Test]
        public void NoSePuedeColocarFueraDelTableroNiSobreOtraMina()
        {
            var match = new MatchEngine(1);
            match.PlaceMine(1, 4);

            Assert.That(match.PlaceMine(1, -1).Outcome, Is.EqualTo(PlaceOutcome.InvalidCell));
            Assert.That(match.PlaceMine(1, 9).Outcome, Is.EqualTo(PlaceOutcome.InvalidCell));
            Assert.That(match.PlaceMine(1, 4).Outcome, Is.EqualTo(PlaceOutcome.CellOccupied));
            Assert.That(match.PlacedBy(1), Is.EqualTo(1), "los rechazos no cuentan");
        }

        [Test]
        public void ColocarOtraVezDespuesDeLaColocacionEsFueraDeFase()
        {
            MatchEngine match = Placed();

            Assert.That(match.PlaceMine(1, 8).Outcome, Is.EqualTo(PlaceOutcome.WrongPhase));
        }

        // -------------------------------------------------------------------- memorización

        [Test]
        public void DuranteLaMemorizacionNoSeJuega()
        {
            MatchEngine match = Placed();

            Assert.That(match.Reveal(1, 5).Outcome, Is.EqualTo(RevealOutcome.WrongPhase));
            Assert.That(match.Validate(1).Outcome, Is.EqualTo(ValidateOutcome.WrongPhase));
        }

        [Test]
        public void AlOcultarLasMinasEmpiezaLaBusquedaYElJugadorUnoAbre()
        {
            MatchEngine match = Placed();

            Assert.That(match.HideMines(), Is.True);

            Assert.That(match.Phase, Is.EqualTo(Phase.Search));
            Assert.That(match.CurrentSeat, Is.EqualTo(1));
            Assert.That(match.MinesVisible, Is.False, "en la búsqueda las minas son información oculta");
        }

        [Test]
        public void SoloSePuedeOcultarDesdeLaMemorizacion()
        {
            var match = new MatchEngine(1);
            Assert.That(match.HideMines(), Is.False);

            MatchEngine searching = Searching();
            Assert.That(searching.HideMines(), Is.False);
        }

        // ---------------------------------------------------------------------- búsqueda

        [Test]
        public void UnaCasillaSeguraSeDestapaYPasaElTurno()
        {
            MatchEngine match = Searching();

            RevealResult result = match.Reveal(1, 4);

            Assert.That(result.Outcome, Is.EqualTo(RevealOutcome.Safe));
            Assert.That(result.LivesLost, Is.EqualTo(0));
            Assert.That(match.IsRevealed(4), Is.True);
            Assert.That(match.CurrentSeat, Is.EqualTo(2));
            Assert.That(match.Level, Is.EqualTo(1));
        }

        [Test]
        public void PisarUnaMinaCuestaUnaVidaYTerminaLaRonda()
        {
            MatchEngine match = Searching();

            RevealResult result = match.Reveal(1, 0);

            Assert.That(result.Outcome, Is.EqualTo(RevealOutcome.Mine));
            Assert.That(result.LivesLost, Is.EqualTo(1));
            Assert.That(result.LevelAdvanced, Is.True);
            Assert.That(match.Lives1, Is.EqualTo(2));
            Assert.That(match.Level, Is.EqualTo(2), "el GDD: la ronda termina de inmediato y se avanza");
            Assert.That(match.CellStates(), Is.All.EqualTo(-1), "el tablero nuevo está limpio");
            Assert.That(match.PlacedBy(1), Is.EqualTo(0));
        }

        [Test]
        public void DestaparFueraDeTurnoOEnOtraFaseSeRechaza()
        {
            MatchEngine searching = Searching();
            Assert.That(searching.Reveal(2, 4).Outcome, Is.EqualTo(RevealOutcome.NotYourTurn));

            var placing = new MatchEngine(1);
            Assert.That(placing.Reveal(1, 4).Outcome, Is.EqualTo(RevealOutcome.WrongPhase));
        }

        [Test]
        public void NoSePuedeDestaparDosVecesNiFueraDelTablero()
        {
            MatchEngine match = Searching();
            match.Reveal(1, 4);

            Assert.That(match.Reveal(2, 4).Outcome, Is.EqualTo(RevealOutcome.AlreadyRevealed));
            Assert.That(match.Reveal(2, -1).Outcome, Is.EqualTo(RevealOutcome.InvalidCell));
            Assert.That(match.Reveal(2, 9).Outcome, Is.EqualTo(RevealOutcome.InvalidCell));
            Assert.That(match.CurrentSeat, Is.EqualTo(2), "las jugadas rechazadas no pasan el turno");
        }

        // ------------------------------------------------------------------- botón rojo

        [Test]
        public void ElBotonRojoConCasillasSegurasPendientesCuestaUnaVidaYLaRondaSigue()
        {
            MatchEngine match = Searching();

            ValidateResult result = match.Validate(1);

            Assert.That(result.Outcome, Is.EqualTo(ValidateOutcome.TooEarly));
            Assert.That(result.LivesLost, Is.EqualTo(1));
            Assert.That(match.Lives1, Is.EqualTo(2));
            Assert.That(match.Level, Is.EqualTo(1), "la ronda continúa");
            Assert.That(match.CurrentSeat, Is.EqualTo(1), "el turno no cambia");
        }

        [Test]
        public void ElBotonRojoSinCasillasSegurasPendientesCompletaElNivelSinPenalizacion()
        {
            MatchEngine match = Searching();
            match.Reveal(1, 4);
            match.Reveal(2, 5);
            match.Reveal(1, 6);
            match.Reveal(2, 7);
            match.Reveal(1, 8);   // las 5 seguras destapadas; ahora le toca al Jugador 2
            Assert.That(match.RemainingSafeCells, Is.EqualTo(0));

            ValidateResult result = match.Validate(2);

            Assert.That(result.Outcome, Is.EqualTo(ValidateOutcome.LevelCompleted));
            Assert.That(result.LivesLost, Is.EqualTo(0));
            Assert.That(match.Lives1, Is.EqualTo(3));
            Assert.That(match.Lives2, Is.EqualTo(3));
            Assert.That(match.Level, Is.EqualTo(2));
            Assert.That(match.Phase, Is.EqualTo(Phase.Placement));
        }

        [Test]
        public void CualquieraDeLosDosPuedePulsarElBotonRojoEnSuTurno()
        {
            MatchEngine match = Searching();
            match.Reveal(1, 4);   // ahora es el turno del Jugador 2

            Assert.That(match.Validate(2).Outcome, Is.EqualTo(ValidateOutcome.TooEarly));
            Assert.That(match.Lives2, Is.EqualTo(2));
        }

        [Test]
        public void ElBotonRojoSoloSePulsaEnTuTurnoYEnLaBusqueda()
        {
            MatchEngine searching = Searching();
            Assert.That(searching.Validate(2).Outcome, Is.EqualTo(ValidateOutcome.NotYourTurn));

            var placing = new MatchEngine(1);
            Assert.That(placing.Validate(1).Outcome, Is.EqualTo(ValidateOutcome.WrongPhase));
        }

        // ------------------------------------------------------------------- fin de partida

        [Test]
        public void QuedarseSinVidasTerminaLaPartidaYGanaElRival()
        {
            MatchEngine match = Searching();

            match.Validate(1);
            match.Validate(1);
            ValidateResult last = match.Validate(1);

            Assert.That(last.MatchEnded, Is.True);
            Assert.That(match.Phase, Is.EqualTo(Phase.Finished));
            Assert.That(match.Winner, Is.EqualTo(2));
            Assert.That(match.Lives1, Is.EqualTo(0));
        }

        [Test]
        public void UnaPartidaTerminadaIgnoraNuevasAcciones()
        {
            MatchEngine match = Searching();
            match.Forfeit(1);

            Assert.That(match.PlaceMine(1, 8).Outcome, Is.EqualTo(PlaceOutcome.MatchFinished));
            Assert.That(match.Reveal(2, 4).Outcome, Is.EqualTo(RevealOutcome.MatchFinished));
            Assert.That(match.Validate(2).Outcome, Is.EqualTo(ValidateOutcome.MatchFinished));
            Assert.That(match.MinesVisible, Is.True, "al terminar se muestran las minas");
        }

        [Test]
        public void AbandonarDaLaVictoriaAlRival()
        {
            var match = new MatchEngine(1);

            match.Forfeit(2);

            Assert.That(match.Winner, Is.EqualTo(1));
            Assert.That(match.IsFinished, Is.True);
            Assert.That(match.Lives2, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- tiempo agotado

        [Test]
        public void SiSeAcabaElTiempoBuscandoPierdesUnaVidaYPasaElTurno()
        {
            MatchEngine match = Searching();

            ExpireResult result = match.ExpireTurn();

            Assert.That(result.Kind, Is.EqualTo(ExpireKind.LifeLost));
            Assert.That(result.Seat, Is.EqualTo(1));
            Assert.That(match.Lives1, Is.EqualTo(2));
            Assert.That(match.CurrentSeat, Is.EqualTo(2));
        }

        [Test]
        public void SiSeAcabaElTiempoColocandoSeColocaUnaMinaAutomaticaSinQuitarVidas()
        {
            var match = new MatchEngine(7);

            ExpireResult result = match.ExpireTurn();

            Assert.That(result.Kind, Is.EqualTo(ExpireKind.AutoPlaced));
            Assert.That(result.Seat, Is.EqualTo(1));
            Assert.That(match.IsMine(result.Cell), Is.True);
            Assert.That(match.MineOwner(result.Cell), Is.EqualTo(1));
            Assert.That(match.PlacedBy(1), Is.EqualTo(1));
            Assert.That(match.Lives1, Is.EqualTo(3));
        }

        [Test]
        public void LaMinaAutomaticaEsDeterministaYNuncaPisaUnaMinaExistente()
        {
            var a = new MatchEngine(7);
            var b = new MatchEngine(7);
            a.PlaceMine(1, 0);
            b.PlaceMine(1, 0);

            int cellA = a.ExpireTurn().Cell;
            int cellB = b.ExpireTurn().Cell;

            Assert.That(cellA, Is.EqualTo(cellB), "misma semilla y mismas acciones: misma casilla");
            Assert.That(cellA, Is.Not.EqualTo(0));
        }

        [Test]
        public void SemillasDistintasNoEligenSiempreLaMismaCasilla()
        {
            var cells = new System.Collections.Generic.HashSet<int>();
            for (int seed = 1; seed <= 30; seed++)
            {
                cells.Add(new MatchEngine(seed).ExpireTurn().Cell);
            }

            Assert.That(cells.Count, Is.GreaterThan(1));
        }

        [Test]
        public void EnLaMemorizacionElTiempoNoHaceNada()
        {
            MatchEngine match = Placed();

            ExpireResult result = match.ExpireTurn();

            Assert.That(result.Kind, Is.EqualTo(ExpireKind.None));
            Assert.That(match.Phase, Is.EqualTo(Phase.Memorize));
        }

        // ------------------------------------------------------------------ determinismo

        [Test]
        public void ElNumeroDeTurnoSoloSubeConAccionesAplicadas()
        {
            var match = new MatchEngine(1);

            match.PlaceMine(2, 0);    // rechazada
            Assert.That(match.TurnNumber, Is.EqualTo(0));

            match.PlaceMine(1, 0);
            match.PlaceMine(1, 0);    // rechazada: casilla ocupada
            Assert.That(match.TurnNumber, Is.EqualTo(1));
        }

        [Test]
        public void LaMismaSemillaYLasMismasAccionesDanLaMismaPartida()
        {
            MatchEngine Play(int seed)
            {
                var m = new MatchEngine(seed);
                m.PlaceMine(1, 4);
                m.ExpireTurn();           // mina automática del Jugador 1
                m.PlaceMine(2, 0);
                m.ExpireTurn();           // mina automática del Jugador 2
                m.HideMines();
                m.Reveal(1, 8);
                m.Validate(2);
                return m;
            }

            MatchEngine a = Play(11);
            MatchEngine b = Play(11);

            Assert.That(b.TurnNumber, Is.EqualTo(a.TurnNumber));
            Assert.That(b.Level, Is.EqualTo(a.Level));
            Assert.That(b.Lives1, Is.EqualTo(a.Lives1));
            Assert.That(b.Lives2, Is.EqualTo(a.Lives2));
            Assert.That(b.CurrentSeat, Is.EqualTo(a.CurrentSeat));
            Assert.That(b.CellStates(), Is.EqualTo(a.CellStates()));
            Assert.That(b.MineCells(), Is.EqualTo(a.MineCells()));
        }

        // -------------------------------------------------------- información que sale

        [Test]
        public void LaVistaPublicaNuncaDelataMinasOcultas()
        {
            MatchEngine match = Searching();
            match.Reveal(1, 4);

            int[] states = match.CellStates();

            Assert.That(match.MinesVisible, Is.False);
            Assert.That(states[4], Is.EqualTo(0));
            for (int i = 0; i < states.Length; i++)
            {
                if (!match.IsRevealed(i))
                {
                    Assert.That(states[i], Is.EqualTo(-1), "la casilla " + i + " no debe distinguirse");
                }
            }
        }

        [Test]
        public void LasMinasSeMuestranSoloColocandoMemorizandoOAlTerminar()
        {
            var match = new MatchEngine(1);
            Assert.That(match.MinesVisible, Is.True, "colocación");

            MatchEngine memorize = Placed();
            Assert.That(memorize.MinesVisible, Is.True, "memorización");
            Assert.That(memorize.MineCells().Length, Is.EqualTo(4));
            Assert.That(memorize.MineOwners().Count(o => o == 1), Is.EqualTo(2));

            memorize.HideMines();
            Assert.That(memorize.MinesVisible, Is.False, "búsqueda");

            memorize.Forfeit(1);
            Assert.That(memorize.MinesVisible, Is.True, "fin de partida");
        }
    }
}
