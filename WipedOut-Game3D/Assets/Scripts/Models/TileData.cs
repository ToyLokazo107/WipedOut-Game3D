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

        public TileData(Vector2Int position, bool isMine = false)
        {
            this.gridPosition = position;
            this.isMine = isMine;
            this.isRevealed = false;
            this.tileInstance = null;
        }
    }
}