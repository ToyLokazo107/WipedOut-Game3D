using System;

namespace WipedOut.Rules
{
    /// <summary>
    /// Progresión de niveles según el GDD (sección 8): la grilla crece de 1x1 por nivel y cada
    /// jugador coloca una mina más. Es C# puro, sin UnityEngine: lo comparten el juego y el servidor.
    /// </summary>
    public static class LevelRules
    {
        /// <summary>Tope de la grilla para que el tablero siga siendo jugable en pantalla.</summary>
        public const int MaxGridSize = 10;

        /// <summary>Nivel 1: 3x3, nivel 2: 4x4, nivel 3: 5x5, y así hasta <see cref="MaxGridSize"/>.</summary>
        public static int GridSize(int level)
        {
            return Math.Min(MaxGridSize, Math.Max(3, NormalizeLevel(level) + 2));
        }

        /// <summary>
        /// Nivel 1: 2 minas por jugador, nivel 2: 3, nivel 3: 4... Nunca tantas que no quede una
        /// casilla segura en el tablero.
        /// </summary>
        public static int MinesPerPlayer(int level)
        {
            int size = GridSize(level);
            int cells = size * size;
            int wanted = NormalizeLevel(level) + 1;
            return Math.Min(wanted, (cells - 1) / 2);
        }

        /// <summary>Quien coloca primero: niveles impares el Jugador 1, pares el Jugador 2.</summary>
        public static int FirstPlacer(int level)
        {
            return NormalizeLevel(level) % 2 == 1 ? 1 : 2;
        }

        /// <summary>
        /// Quien abre la fase de búsqueda. El GDD no lo dice; se asume que es quien colocó primero
        /// (es lo que hace el código actual de <c>GameManager</c>).
        /// </summary>
        public static int SearchStarter(int level)
        {
            return FirstPlacer(level);
        }

        public static int Other(int seat)
        {
            return seat == 1 ? 2 : 1;
        }

        private static int NormalizeLevel(int level)
        {
            return level < 1 ? 1 : level;
        }
    }
}
