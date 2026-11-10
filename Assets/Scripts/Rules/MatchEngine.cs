using System;
using System.Collections.Generic;

namespace WipedOut.Rules
{
    /// <summary>Fase de una ronda (GDD sección 4).</summary>
    public enum Phase
    {
        /// <summary>Cada jugador coloca sus minas a mano, uno detrás de otro. Las colocaciones son visibles.</summary>
        Placement = 0,

        /// <summary>Ya están todas las minas colocadas y siguen visibles unos segundos para memorizarlas.</summary>
        Memorize = 1,

        /// <summary>Las minas están ocultas y los jugadores se turnan para destapar casillas.</summary>
        Search = 2,

        Finished = 3,
    }

    public enum PlaceOutcome
    {
        Placed,
        WrongPhase,
        NotYourTurn,
        InvalidCell,
        CellOccupied,
        MatchFinished,
    }

    public enum RevealOutcome
    {
        /// <summary>Casilla segura: queda destapada y el turno pasa al rival.</summary>
        Safe,

        /// <summary>Mina: el jugador pierde una vida y la ronda termina (avanza al siguiente nivel).</summary>
        Mine,

        WrongPhase,
        NotYourTurn,
        InvalidCell,
        AlreadyRevealed,
        MatchFinished,
    }

    public enum ValidateOutcome
    {
        /// <summary>No quedaban casillas seguras: nivel completado, nadie pierde vidas.</summary>
        LevelCompleted,

        /// <summary>Aún quedaba alguna casilla segura: el jugador pierde una vida y la ronda continúa.</summary>
        TooEarly,

        WrongPhase,
        NotYourTurn,
        MatchFinished,
    }

    public enum ExpireKind
    {
        /// <summary>Nada que hacer (partida terminada o fase de memorización, que no tiene turno).</summary>
        None,

        /// <summary>Se agotó el tiempo al colocar: se colocó una mina en una casilla libre elegida por el motor.</summary>
        AutoPlaced,

        /// <summary>Se agotó el tiempo al buscar: el jugador pierde una vida y pasa el turno.</summary>
        LifeLost,
    }

    public readonly struct PlaceResult
    {
        public PlaceResult(PlaceOutcome outcome, int seat, int cell)
        {
            Outcome = outcome;
            Seat = seat;
            Cell = cell;
        }

        public PlaceOutcome Outcome { get; }

        public int Seat { get; }

        public int Cell { get; }
    }

    public readonly struct RevealResult
    {
        public RevealResult(RevealOutcome outcome, int seat, int cell, int livesLost, bool levelAdvanced, bool matchEnded)
        {
            Outcome = outcome;
            Seat = seat;
            Cell = cell;
            LivesLost = livesLost;
            LevelAdvanced = levelAdvanced;
            MatchEnded = matchEnded;
        }

        public RevealOutcome Outcome { get; }

        public int Seat { get; }

        public int Cell { get; }

        public int LivesLost { get; }

        public bool LevelAdvanced { get; }

        public bool MatchEnded { get; }
    }

    public readonly struct ValidateResult
    {
        public ValidateResult(ValidateOutcome outcome, int seat, int livesLost, bool levelAdvanced, bool matchEnded)
        {
            Outcome = outcome;
            Seat = seat;
            LivesLost = livesLost;
            LevelAdvanced = levelAdvanced;
            MatchEnded = matchEnded;
        }

        public ValidateOutcome Outcome { get; }

        public int Seat { get; }

        public int LivesLost { get; }

        public bool LevelAdvanced { get; }

        public bool MatchEnded { get; }
    }

    public readonly struct ExpireResult
    {
        public ExpireResult(ExpireKind kind, int seat, int cell, int livesLost, bool matchEnded)
        {
            Kind = kind;
            Seat = seat;
            Cell = cell;
            LivesLost = livesLost;
            MatchEnded = matchEnded;
        }

        public ExpireKind Kind { get; }

        public int Seat { get; }

        /// <summary>Casilla donde se colocó la mina (solo en <see cref="ExpireKind.AutoPlaced"/>); -1 si no aplica.</summary>
        public int Cell { get; }

        public int LivesLost { get; }

        public bool MatchEnded { get; }
    }

    /// <summary>
    /// Reglas de Wiped out? (GDD v1.3), sin UnityEngine ni nada de red. Hoy las usa el servidor
    /// de Cloud Code; el juego local también puede usarlas. Es la única fuente de verdad de las reglas.
    ///
    /// Una ronda tiene tres fases: <see cref="Phase.Placement"/> (cada jugador coloca sus minas,
    /// visibles para el rival), <see cref="Phase.Memorize"/> (siguen visibles unos segundos) y
    /// <see cref="Phase.Search"/> (se ocultan y se juega por turnos). Reglas de vidas:
    /// pisar una mina cuesta una vida y termina la ronda; pulsar el botón rojo (validar) con casillas
    /// seguras sin destapar cuesta una vida y la ronda sigue. Quien llega a 0 vidas pierde.
    ///
    /// Los asientos son 1 y 2. Cada acción comprueba fase y turno y devuelve un resultado explícito
    /// (no lanza excepciones): un rechazo es parte normal del juego. El motor es determinista: la
    /// misma semilla y la misma lista de acciones dan siempre la misma partida (la semilla solo
    /// se usa para colocar una mina automática cuando se agota el tiempo al colocar).
    /// </summary>
    public sealed class MatchEngine
    {
        public const int MaxLives = 3;

        private readonly int seed;
        private readonly int[] lives = { 0, MaxLives, MaxLives };
        private readonly int[] placed = { 0, 0, 0 };

        private int[] mineOwner;
        private bool[] revealed;
        private int revealedSafe;

        public MatchEngine(int seed = 0)
        {
            this.seed = seed;
            Level = 1;
            StartLevel();
        }

        public int Level { get; private set; }

        public int GridSize { get; private set; }

        public int CellCount => GridSize * GridSize;

        public int MinesPerPlayer { get; private set; }

        public Phase Phase { get; private set; }

        /// <summary>De quién es el turno (colocar o buscar). 0 en la fase de memorización y al terminar.</summary>
        public int CurrentSeat { get; private set; }

        /// <summary>
        /// Cuenta las acciones que han cambiado la partida (colocar, ocultar, destapar, validar, tiempo
        /// agotado, abandono). Una acción rechazada no la incrementa.
        /// </summary>
        public int TurnNumber { get; private set; }

        /// <summary>0 mientras la partida sigue; si no, el asiento ganador.</summary>
        public int Winner { get; private set; }

        public int Lives1 => lives[1];

        public int Lives2 => lives[2];

        public int LivesOf(int seat) => seat == 1 || seat == 2 ? lives[seat] : 0;

        public int PlacedBy(int seat) => seat == 1 || seat == 2 ? placed[seat] : 0;

        public bool IsFinished => Phase == Phase.Finished;

        /// <summary>Quien coloca primero en el nivel actual.</summary>
        public int FirstPlacer => LevelRules.FirstPlacer(Level);

        public int TotalMines => MinesPerPlayer * 2;

        /// <summary>Casillas seguras (sin mina) del tablero. Solo es exacto cuando ya están todas las minas colocadas.</summary>
        public int TotalSafeCells => CellCount - TotalMines;

        /// <summary>Casillas seguras todavía sin destapar: las que mira el botón rojo.</summary>
        public int RemainingSafeCells => TotalSafeCells - revealedSafe;

        /// <summary>
        /// Las minas son visibles mientras se colocan, durante la memorización y cuando termina la
        /// partida. En la fase de búsqueda están ocultas y el servidor no debe enviarlas a nadie.
        /// </summary>
        public bool MinesVisible => Phase != Phase.Search;

        public bool IsMine(int cell) => InRange(cell) && mineOwner[cell] != 0;

        public bool IsRevealed(int cell) => InRange(cell) && revealed[cell];

        /// <summary>Quién colocó la mina de esa casilla (1 o 2); 0 si no hay mina.</summary>
        public int MineOwner(int cell) => InRange(cell) ? mineOwner[cell] : 0;

        public static int Other(int seat) => LevelRules.Other(seat);

        /// <summary>
        /// Fase de colocación: el jugador pone una de sus minas. Primero coloca todas las suyas quien
        /// abre el nivel y después el otro. Al colocarse la última mina empieza la memorización.
        /// </summary>
        public PlaceResult PlaceMine(int seat, int cell)
        {
            if (Phase == Phase.Finished)
            {
                return new PlaceResult(PlaceOutcome.MatchFinished, seat, cell);
            }

            if (Phase != Phase.Placement)
            {
                return new PlaceResult(PlaceOutcome.WrongPhase, seat, cell);
            }

            if (seat != CurrentSeat)
            {
                return new PlaceResult(PlaceOutcome.NotYourTurn, seat, cell);
            }

            if (!InRange(cell))
            {
                return new PlaceResult(PlaceOutcome.InvalidCell, seat, cell);
            }

            if (mineOwner[cell] != 0)
            {
                return new PlaceResult(PlaceOutcome.CellOccupied, seat, cell);
            }

            mineOwner[cell] = seat;
            placed[seat]++;
            TurnNumber++;
            AdvancePlacementTurn();
            return new PlaceResult(PlaceOutcome.Placed, seat, cell);
        }

        /// <summary>
        /// Termina la memorización: las minas se ocultan y empieza la búsqueda. Lo decide el reloj del
        /// servidor (o un temporizador en el juego local). Devuelve false si no estaba en memorización.
        /// </summary>
        public bool HideMines()
        {
            if (Phase != Phase.Memorize)
            {
                return false;
            }

            Phase = Phase.Search;
            CurrentSeat = LevelRules.SearchStarter(Level);
            TurnNumber++;
            return true;
        }

        /// <summary>
        /// Fase de búsqueda: el jugador del turno destapa una casilla. Segura: queda destapada y pasa
        /// el turno. Mina: pierde una vida y la ronda termina de inmediato (se pasa al siguiente nivel).
        /// </summary>
        public RevealResult Reveal(int seat, int cell)
        {
            if (Phase == Phase.Finished)
            {
                return new RevealResult(RevealOutcome.MatchFinished, seat, cell, 0, false, true);
            }

            if (Phase != Phase.Search)
            {
                return new RevealResult(RevealOutcome.WrongPhase, seat, cell, 0, false, false);
            }

            if (seat != CurrentSeat)
            {
                return new RevealResult(RevealOutcome.NotYourTurn, seat, cell, 0, false, false);
            }

            if (!InRange(cell))
            {
                return new RevealResult(RevealOutcome.InvalidCell, seat, cell, 0, false, false);
            }

            if (revealed[cell])
            {
                return new RevealResult(RevealOutcome.AlreadyRevealed, seat, cell, 0, false, false);
            }

            revealed[cell] = true;
            TurnNumber++;

            if (mineOwner[cell] != 0)
            {
                bool ended = LoseLife(seat);
                bool advanced = false;
                if (!ended)
                {
                    Level++;
                    StartLevel();
                    advanced = true;
                }

                return new RevealResult(RevealOutcome.Mine, seat, cell, 1, advanced, ended);
            }

            revealedSafe++;
            CurrentSeat = Other(seat);
            return new RevealResult(RevealOutcome.Safe, seat, cell, 0, false, false);
        }

        /// <summary>
        /// Botón rojo: el jugador del turno afirma que ya no quedan casillas seguras. Si es verdad, se
        /// completa el nivel sin perder vidas. Si queda alguna, pierde una vida y la ronda sigue (el turno
        /// no cambia).
        /// </summary>
        public ValidateResult Validate(int seat)
        {
            if (Phase == Phase.Finished)
            {
                return new ValidateResult(ValidateOutcome.MatchFinished, seat, 0, false, true);
            }

            if (Phase != Phase.Search)
            {
                return new ValidateResult(ValidateOutcome.WrongPhase, seat, 0, false, false);
            }

            if (seat != CurrentSeat)
            {
                return new ValidateResult(ValidateOutcome.NotYourTurn, seat, 0, false, false);
            }

            TurnNumber++;

            if (RemainingSafeCells <= 0)
            {
                Level++;
                StartLevel();
                return new ValidateResult(ValidateOutcome.LevelCompleted, seat, 0, true, false);
            }

            bool ended = LoseLife(seat);
            return new ValidateResult(ValidateOutcome.TooEarly, seat, 1, false, ended);
        }

        /// <summary>
        /// Se agotó el tiempo del turno. Colocando: se pone una mina automática en una casilla libre
        /// (sin quitar vidas). Buscando: el jugador pierde una vida y pasa el turno.
        /// </summary>
        public ExpireResult ExpireTurn()
        {
            if (Phase == Phase.Placement)
            {
                int seat = CurrentSeat;
                int cell = PickFreeCell();
                PlaceMine(seat, cell);
                return new ExpireResult(ExpireKind.AutoPlaced, seat, cell, 0, false);
            }

            if (Phase == Phase.Search)
            {
                int seat = CurrentSeat;
                TurnNumber++;
                bool ended = LoseLife(seat);
                if (!ended)
                {
                    CurrentSeat = Other(seat);
                }

                return new ExpireResult(ExpireKind.LifeLost, seat, -1, 1, ended);
            }

            return new ExpireResult(ExpireKind.None, 0, -1, 0, Phase == Phase.Finished);
        }

        /// <summary>El jugador abandona: pierde todas sus vidas y gana el rival.</summary>
        public void Forfeit(int seat)
        {
            if (Phase == Phase.Finished || (seat != 1 && seat != 2))
            {
                return;
            }

            lives[seat] = 0;
            Winner = Other(seat);
            Phase = Phase.Finished;
            CurrentSeat = 0;
            TurnNumber++;
        }

        /// <summary>
        /// Una entrada por casilla, siempre pública: -1 casilla blanca (sin destapar), 0 casilla segura
        /// destapada, 1 mina ya pisada. NUNCA revela minas ocultas.
        /// </summary>
        public int[] CellStates()
        {
            var result = new int[CellCount];
            for (int i = 0; i < result.Length; i++)
            {
                if (!revealed[i])
                {
                    result[i] = -1;
                }
                else
                {
                    result[i] = mineOwner[i] != 0 ? 1 : 0;
                }
            }

            return result;
        }

        /// <summary>
        /// Casillas con mina colocadas hasta ahora. Solo debe enviarse a un cliente cuando
        /// <see cref="MinesVisible"/> es true.
        /// </summary>
        public int[] MineCells()
        {
            var cells = new List<int>();
            for (int i = 0; i < mineOwner.Length; i++)
            {
                if (mineOwner[i] != 0)
                {
                    cells.Add(i);
                }
            }

            return cells.ToArray();
        }

        /// <summary>Dueño (1 o 2) de cada casilla devuelta por <see cref="MineCells"/>, en el mismo orden.</summary>
        public int[] MineOwners()
        {
            var owners = new List<int>();
            for (int i = 0; i < mineOwner.Length; i++)
            {
                if (mineOwner[i] != 0)
                {
                    owners.Add(mineOwner[i]);
                }
            }

            return owners.ToArray();
        }

        private bool InRange(int cell)
        {
            return cell >= 0 && cell < CellCount;
        }

        private void StartLevel()
        {
            GridSize = LevelRules.GridSize(Level);
            MinesPerPlayer = LevelRules.MinesPerPlayer(Level);
            mineOwner = new int[CellCount];
            revealed = new bool[CellCount];
            revealedSafe = 0;
            placed[1] = 0;
            placed[2] = 0;
            Phase = Phase.Placement;
            CurrentSeat = LevelRules.FirstPlacer(Level);
        }

        private void AdvancePlacementTurn()
        {
            if (placed[CurrentSeat] < MinesPerPlayer)
            {
                return;
            }

            int other = Other(CurrentSeat);
            if (placed[other] >= MinesPerPlayer)
            {
                Phase = Phase.Memorize;
                CurrentSeat = 0;
            }
            else
            {
                CurrentSeat = other;
            }
        }

        private bool LoseLife(int seat)
        {
            lives[seat]--;
            if (lives[seat] > 0)
            {
                return false;
            }

            lives[seat] = 0;
            Winner = Other(seat);
            Phase = Phase.Finished;
            CurrentSeat = 0;
            return true;
        }

        private int PickFreeCell()
        {
            var free = new List<int>();
            for (int i = 0; i < mineOwner.Length; i++)
            {
                if (mineOwner[i] == 0)
                {
                    free.Add(i);
                }
            }

            // Determinista: la misma semilla y el mismo número de turno eligen siempre la misma casilla.
            var random = new Random(unchecked(seed * 486187739 + TurnNumber * 16777619));
            return free[random.Next(free.Count)];
        }
    }
}
