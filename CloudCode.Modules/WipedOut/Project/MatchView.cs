namespace WipedOut;

/// <summary>
/// Lo UNICO que ve un cliente. Se construye siempre desde cero a partir del estado: no tiene la
/// semilla ni la lista de jugadas. Las minas (<see cref="Mines"/>) solo viajan mientras el juego las
/// muestra a proposito: al colocarlas, durante la memorizacion y cuando termina la partida. En la fase
/// de busqueda NO se envian: esa es la parte de memoria del juego y la informacion oculta del servidor.
///
/// Se serializa en camelCase (ver <see cref="MatchJson"/>) y se envia como texto JSON.
/// </summary>
public class MatchView
{
    /// <summary>Version del contrato (ver <see cref="GameProtocol"/>). El cliente la compara con la suya.</summary>
    public int ProtocolVersion { get; set; } = GameProtocol.Version;

    public string MatchCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>Un <see cref="PhaseName"/>.</summary>
    public string Phase { get; set; } = PhaseName.None;

    /// <summary>Asiento de quien recibe la vista: 1 o 2.</summary>
    public int MySeat { get; set; }

    public int TurnNumber { get; set; }

    public int Level { get; set; }

    public int Size { get; set; }

    /// <summary>
    /// Una entrada por casilla: -1 casilla blanca (sin destapar), 0 segura destapada, 1 mina pisada.
    /// Vacia mientras la sala espera al rival.
    /// </summary>
    public int[] Cells { get; set; } = Array.Empty<int>();

    public int MinesPerPlayer { get; set; }

    public int Placed1 { get; set; }

    public int Placed2 { get; set; }

    public int Lives1 { get; set; }

    public int Lives2 { get; set; }

    /// <summary>Asiento al que le toca (colocar o buscar). 0 en memorizacion y al terminar.</summary>
    public int CurrentSeat { get; set; }

    /// <summary>Plazo del turno actual (solo en colocacion y busqueda).</summary>
    public string TurnDeadlineUtc { get; set; } = string.Empty;

    /// <summary>Hora en que se ocultan las minas (solo en memorizacion).</summary>
    public string MemorizeDeadlineUtc { get; set; } = string.Empty;

    public string ServerTimeUtc { get; set; } = string.Empty;

    /// <summary>Resultado de la peticion que origino esta vista (ver <see cref="MatchOutcome"/>).</summary>
    public string Outcome { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public LastEvent? LastEvent { get; set; }

    public int WinnerSeat { get; set; }

    public string EndReason { get; set; } = string.Empty;

    /// <summary>Casillas con mina. Null (y ni se serializa) en la fase de busqueda.</summary>
    public int[]? Mines { get; set; }

    /// <summary>Quien coloco cada mina de <see cref="Mines"/>, en el mismo orden.</summary>
    public int[]? MineOwners { get; set; }
}
