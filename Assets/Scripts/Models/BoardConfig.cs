using UnityEngine;

namespace WipedOut.Models
{
    [CreateAssetMenu(fileName = "BoardConfig", menuName = "WipedOut/BoardConfig")]
    public class BoardConfig : ScriptableObject
    {
        [Header("Configuración del Tablero")]
        public int gridWidth = 3;
        public int gridHeight = 3;

        [Tooltip("Cantidad de minas que coloca cada jugador durante la Fase de Colocación.")]
        public int mineCount = 2;

        [Tooltip("Tiempo reservado para una fase de memorización opcional.")]
        public float memorizationTime = 5f;
    }
}
