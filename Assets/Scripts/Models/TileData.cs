using UnityEngine;

namespace WipedOut.Models
{
    [System.Serializable]
    public class TileData
    {
        public bool isMine;
        public bool isRevealed;
        public Vector2Int gridPosition;
        public GameObject tileInstance;

        [Tooltip("Jugador dueño de la mina: 0 = ninguna, 1 = Jugador 1, 2 = Jugador 2.")]
        public int mineOwner;

        /// <summary>Casilla blanca: segura y todavía sin destapar.</summary>
        public bool IsHiddenWhite => !isMine && !isRevealed;

        public TileData(Vector2Int position, bool isMine = false, int mineOwner = 0)
        {
            this.gridPosition = position;
            this.isMine = isMine;
            this.isRevealed = false;
            this.tileInstance = null;
            this.mineOwner = isMine ? mineOwner : 0;
        }

        /// <summary>Marca la casilla como mina del jugador indicado.</summary>
        public void SetMine(int owner)
        {
            mineOwner = owner;
            isMine = owner == 1 || owner == 2;
        }

        /// <summary>Quita la mina de la casilla.</summary>
        public void ClearMine()
        {
            mineOwner = 0;
            isMine = false;
        }
    }
}
