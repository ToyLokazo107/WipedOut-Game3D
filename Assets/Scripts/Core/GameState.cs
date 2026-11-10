namespace WipedOut.Core
{
    /// <summary>
    /// Estados del bucle principal de una partida de Wiped out?.
    /// </summary>
    public enum GameState
    {
        Preparation = 0,

        /// <summary>Reservado para una fase de memorización opcional; el flujo actual no lo usa.</summary>
        Memorization = 1,

        PlayerTurn = 2,
        Validation = 3,
        GameOver = 4,

        /// <summary>
        /// Fase previa a la partida en la que ambos jugadores colocan sus minas.
        /// </summary>
        Placement = 5
    }
}
