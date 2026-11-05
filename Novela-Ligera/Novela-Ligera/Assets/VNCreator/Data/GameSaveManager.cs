using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNCreator
{
    public static class GameSaveManager
    {
        public static string currentLoadName = string.Empty;

        public static List<string> Load(string loadName)
        {
            if (string.IsNullOrEmpty(loadName))
            {
                currentLoadName = string.Empty;
                return null;
            }

            currentLoadName = loadName;
            return Load();
        }

        public static List<string> Load()
        {
            if (string.IsNullOrEmpty(currentLoadName))
                return null;

            if (!PlayerPrefs.HasKey(currentLoadName))
                return null;

            string loadString = PlayerPrefs.GetString(currentLoadName, string.Empty);
            if (string.IsNullOrEmpty(loadString))
                return null;

            string[] entries = loadString.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            return new List<string>(entries);
        }

        public static void Save(List<string> storyPath)
        {
            if (string.IsNullOrEmpty(currentLoadName) || storyPath == null || storyPath.Count == 0)
                return;

            PlayerPrefs.SetString(currentLoadName, string.Join("_", storyPath.ToArray()));
            PlayerPrefs.Save();
        }

        public static void NewLoad(string saveName)
        {
            currentLoadName = saveName;
            PlayerPrefs.DeleteKey(saveName);
            PlayerPrefs.Save();
        }
    }
}
