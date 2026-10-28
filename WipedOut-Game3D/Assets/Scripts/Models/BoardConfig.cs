using UnityEngine;

namespace WipedOut.Models
{
    [CreateAssetMenu(fileName = "BoardConfig", menuName = "WipedOut/BoardConfig")]
    public class BoardConfig : ScriptableObject
    {
        [Header("Configuración del Tablero")]
        public int gridWidth = 2;
        public int gridHeight = 2;
        public int mineCount = 2;
        public float memorizationTime = 5f;
    }
}