using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VNCreator
{
    public class VNCreator_DisplayUI : DisplayBase
    {
        [Header("Text")]
        public Text characterNameTxt;
        public Text dialogueTxt;
        [Header("Visuals")]
        public Image characterImg;
        public Image backgroundImg;
        [Header("Audio")]
        public AudioSource musicSource;
        public AudioSource soundEffectSource;
        [Header("Buttons")]
        public Button nextBtn;
        public Button previousBtn;
        public Button saveBtn;
        public Button menuButton;
        [Header("Choices")]
        public Button choiceBtn1;
        public Button choiceBtn2;
        public Button choiceBtn3;
        [Header("End")]
        public GameObject endScreen;
        [Header("Main menu")]
        [Scene]
        public string mainMenu;

        Coroutine typingCoroutine;

        void Start()
        {
            WireButton(nextBtn, OnNextButton);
            WireButton(previousBtn, OnPreviousButton);
            WireButton(saveBtn, OnSaveButton);
            WireButton(menuButton, OnMenuButton);
            WireButton(choiceBtn1, OnChoice1Button);
            WireButton(choiceBtn2, OnChoice2Button);
            WireButton(choiceBtn3, OnChoice3Button);

            if (endScreen != null)
                endScreen.SetActive(false);

            StartDialogue();
        }

        // The scene can wire these methods from the Button OnClick inspector.
        // The fallback listener below is only added when the scene has no persistent call.
        void WireButton(Button _button, UnityEngine.Events.UnityAction _action)
        {
            if (_button != null && _button.onClick.GetPersistentEventCount() == 0)
                _button.onClick.AddListener(_action);
        }

        public void OnNextButton()
        {
            NextNode(0);
        }

        public void OnChoice1Button()
        {
            NextNode(0);
        }

        public void OnChoice2Button()
        {
            NextNode(1);
        }

        public void OnChoice3Button()
        {
            NextNode(2);
        }

        public void OnPreviousButton()
        {
            Previous();
        }

        public void OnSaveButton()
        {
            Save();
        }

        public void OnMenuButton()
        {
            ExitGame();
        }

        protected override void NextNode(int _choiceId)
        {
            if (lastNode)
            {
                ShowEndScreen();
                return;
            }

            base.NextNode(_choiceId);
            StartDialogue();
        }

        void StartDialogue()
        {
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);

            typingCoroutine = StartCoroutine(DisplayCurrentNode());
        }

        IEnumerator DisplayCurrentNode()
        {
            NodeData node = currentNode;

            if (node == null)
            {
                Debug.LogError("VNCreator: currentNode is null. Revisa el StoryObject y sus enlaces.");
                yield break;
            }

            if (characterNameTxt != null)
                characterNameTxt.text = node.characterName ?? string.Empty;

            if (characterImg != null)
            {
                if (node.characterSpr != null)
                {
                    characterImg.sprite = node.characterSpr;
                    characterImg.color = Color.white;
                }
                else
                {
                    characterImg.color = new Color(1, 1, 1, 0);
                }
            }

            if (backgroundImg != null && node.backgroundSpr != null)
                backgroundImg.sprite = node.backgroundSpr;

            bool showingChoices = node.choices > 1;

            if (nextBtn != null)
                nextBtn.gameObject.SetActive(!showingChoices);

            SetChoiceButton(choiceBtn1, node, 0, showingChoices);
            SetChoiceButton(choiceBtn2, node, 1, showingChoices);
            SetChoiceButton(choiceBtn3, node, 2, showingChoices);

            if (previousBtn != null)
                previousBtn.gameObject.SetActive(loadList.Count > 1);

            if (node.backgroundMusic != null && VNCreator_MusicSource.instance != null)
                VNCreator_MusicSource.instance.Play(node.backgroundMusic);

            if (node.soundEffect != null && VNCreator_SfxSource.instance != null)
                VNCreator_SfxSource.instance.Play(node.soundEffect);

            if (dialogueTxt == null)
            {
                typingCoroutine = null;
                yield break;
            }

            dialogueTxt.text = string.Empty;
            string fullText = node.dialogueText ?? string.Empty;

            if (GameOptions.isInstantText || fullText.Length == 0)
            {
                dialogueTxt.text = fullText;
            }
            else
            {
                float delay = 0.01f / Mathf.Max(0.01f, GameOptions.readSpeed);
                for (int i = 0; i < fullText.Length; i++)
                {
                    dialogueTxt.text += fullText[i];
                    yield return new WaitForSeconds(delay);
                }
            }

            typingCoroutine = null;
        }

        void SetChoiceButton(Button _button, NodeData _node, int _index, bool _showingChoices)
        {
            if (_button == null)
                return;

            bool hasChoice = _showingChoices
                && _index < _node.choices
                && _node.choiceOptions != null
                && _index < _node.choiceOptions.Count;

            _button.gameObject.SetActive(hasChoice);

            if (!hasChoice)
                return;

            if (_button.transform.childCount > 0)
            {
                Text label = _button.transform.GetChild(0).GetComponent<Text>();
                if (label != null)
                    label.text = _node.choiceOptions[_index];
            }
        }

        void ShowEndScreen()
        {
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            if (endScreen != null)
                endScreen.SetActive(true);
        }

        protected override void Previous()
        {
            base.Previous();
            StartDialogue();
        }

        public void ExitGame()
        {
            if (string.IsNullOrEmpty(mainMenu))
            {
                Debug.LogWarning("VNCreator: no hay escena de menú configurada.");
                return;
            }

            SceneManager.LoadScene(mainMenu, LoadSceneMode.Single);
        }
    }
}
