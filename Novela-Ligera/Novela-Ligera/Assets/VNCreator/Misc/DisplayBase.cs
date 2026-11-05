using System.Collections.Generic;
using UnityEngine;

namespace VNCreator
{
    public class DisplayBase : MonoBehaviour
    {
        public StoryObject story;

        protected NodeData currentNode;
        protected bool lastNode;

        protected List<string> loadList = new List<string>();

        void Awake()
        {
            if (story == null)
            {
                Debug.LogError("VNCreator: no hay un StoryObject asignado al manager.");
                enabled = false;
                return;
            }

            string save = string.IsNullOrEmpty(GameSaveManager.currentLoadName)
                ? string.Empty
                : PlayerPrefs.GetString(GameSaveManager.currentLoadName, string.Empty);

            if (string.IsNullOrEmpty(save))
            {
                StartFromBeginning();
                return;
            }

            loadList = GameSaveManager.Load();
            if (loadList == null || loadList.Count == 0)
            {
                StartFromBeginning();
                return;
            }

            currentNode = story.GetCurrentNode(loadList[loadList.Count - 1]);
            if (currentNode == null)
            {
                StartFromBeginning();
                return;
            }

            lastNode = currentNode.endNode;
        }

        void StartFromBeginning()
        {
            currentNode = story.GetFirstNode();
            loadList = new List<string>();

            if (currentNode != null)
            {
                loadList.Add(currentNode.guid);
                lastNode = currentNode.endNode;
            }
            else
            {
                Debug.LogError("VNCreator: el StoryObject no tiene un nodo inicial.");
            }
        }

        protected virtual void NextNode(int _choiceId)
        {
            if (lastNode || currentNode == null || story == null)
                return;

            NodeData nextNode = story.GetNextNode(currentNode.guid, _choiceId);
            if (nextNode == null)
            {
                Debug.LogWarning("VNCreator: no existe un enlace para la elección " + _choiceId + ".");
                return;
            }

            currentNode = nextNode;
            lastNode = currentNode.endNode;
            loadList.Add(currentNode.guid);
        }

        protected virtual void Previous()
        {
            if (loadList.Count <= 1 || story == null)
                return;

            loadList.RemoveAt(loadList.Count - 1);
            currentNode = story.GetCurrentNode(loadList[loadList.Count - 1]);

            if (currentNode == null)
            {
                StartFromBeginning();
                return;
            }

            lastNode = currentNode.endNode;
        }

        protected void Save()
        {
            if (loadList == null || loadList.Count == 0)
            {
                Debug.LogWarning("VNCreator: no hay progreso para guardar.");
                return;
            }

            if (string.IsNullOrEmpty(GameSaveManager.currentLoadName))
                GameSaveManager.NewLoad("MainGame");

            GameSaveManager.Save(loadList);
        }
    }
}
