using System;
using System.Globalization;

namespace WipedOut.Online
{
    /// <summary>Estado de la sala (el mismo texto que envía el servidor).</summary>
    public static class OnlineStatus
    {
        public const string WaitingForGuest = "waiting_for_guest";
        public const string Playing = "playing";
        public const string Finished = "finished";
    }

    /// <summary>Fase de la ronda (el mismo texto que envía el servidor).</summary>
    public static class OnlinePhase
    {
        public const string Placement = "placement";
        public const string Memorize = "memorize";
        public const string Search = "search";
        public const string Finished = "finished";
    }

    /// <summary>Veredicto del servidor a una petición. Un rechazo NO es un error: es parte del juego.</summary>
    public static class OnlineOutcome
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

    /// <summary>Qué fue lo último que pasó. Sirve para animar o escribir un mensaje.</summary>
    [Serializable]
    public class OnlineLastEvent
    {
        public string kind;
        public int seat;
        public int cell;
        public bool wasMine;
        public int livesLost;
        public bool levelAdvanced;
    }

    /// <summary>
    /// Lo único que el servidor le cuenta a este cliente. Los nombres de los campos son los del JSON
    /// (camelCase) porque se lee con <c>JsonUtility</c>.
    ///
    /// Fíjate en lo que NO hay: ni la semilla ni la lista de jugadas. Y en la fase de búsqueda tampoco
    /// <see cref="mines"/>: las minas solo viajan mientras el juego las muestra a propósito (al colocarlas,
    /// durante la memorización y al terminar la partida). <see cref="cells"/> trae -1 (casilla blanca, sin
    /// destapar), 0 (segura destapada) o 1 (mina pisada).
    /// </summary>
    [Serializable]
    public class OnlineMatchView
    {
        public int protocolVersion;
        public string matchCode;
        public string status;
        public string phase;
        public int mySeat;
        public int turnNumber;
        public int level;
        public int size;
        public int[] cells;
        public int minesPerPlayer;
        public int placed1;
        public int placed2;
        public int lives1;
        public int lives2;
        public int currentSeat;
        public string turnDeadlineUtc;
        public string memorizeDeadlineUtc;
        public string serverTimeUtc;
        public string outcome;
        public string message;
        public OnlineLastEvent lastEvent;
        public int winnerSeat;
        public string endReason;
        public int[] mines;
        public int[] mineOwners;

        public bool IsMyTurn => status == OnlineStatus.Playing && currentSeat == mySeat && currentSeat != 0;

        public bool IsFinished => status == OnlineStatus.Finished;

        public bool IWon => IsFinished && winnerSeat == mySeat;

        /// <summary>Las minas están a la vista (colocación, memorización o fin de partida).</summary>
        public bool HasMines => mines != null && mines.Length > 0;

        public int LivesOfSeat(int seat) => seat == 1 ? lives1 : lives2;

        public int PlacedOfSeat(int seat) => seat == 1 ? placed1 : placed2;

        /// <summary>Segundos que quedan del plazo del turno según el reloj del SERVIDOR.</summary>
        public double TurnSecondsLeftAtServerTime()
        {
            return SecondsBetween(serverTimeUtc, turnDeadlineUtc);
        }

        /// <summary>Segundos que quedan de memorización según el reloj del SERVIDOR.</summary>
        public double MemorizeSecondsLeftAtServerTime()
        {
            return SecondsBetween(serverTimeUtc, memorizeDeadlineUtc);
        }

        private static double SecondsBetween(string fromUtc, string toUtc)
        {
            if (!TryParse(fromUtc, out DateTime from) || !TryParse(toUtc, out DateTime to))
            {
                return 0;
            }

            return (to - from).TotalSeconds;
        }

        private static bool TryParse(string text, out DateTime value)
        {
            // RoundtripKind respeta la "Z" del servidor (hora UTC). No se puede combinar con
            // AdjustToUniversal: esa mezcla lanza ArgumentException.
            bool ok = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
            if (ok)
            {
                value = value.ToUniversalTime();
            }

            return ok;
        }
    }
}
