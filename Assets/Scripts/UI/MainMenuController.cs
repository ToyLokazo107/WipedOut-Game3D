using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WipedOut.UI
{
    /// <summary>
    /// Menú principal de "WIPED OUT?". Construye toda su UI por código (igual que la pantalla online)
    /// sobre el Canvas de la escena: un fondo procedimental de tablero de minas, el título grande y tres
    /// acciones (JUGAR, CRÉDITOS y SALIR). No tiene lógica de juego: JUGAR solo carga la <c>SampleScene</c>,
    /// donde arranca el lobby online (<c>OnlineMatchScreen</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string GameSceneName = "SampleScene";

        private readonly Color TitleColor = new Color(1f, 0.40f, 0.12f, 1f);
        private readonly Color ButtonColor = new Color(0.25f, 0.42f, 0.75f, 1f);
        private readonly Color DangerColor = new Color(0.80f, 0.20f, 0.20f, 1f);
        private readonly Color PanelBackdrop = new Color(0f, 0f, 0f, 0.92f);

        private Canvas canvas;
        private Font font;
        private GameObject creditsPanel;

        private void Awake()
        {
            BuildCanvas();
            EnsureEventSystem();
            Build();
        }

        /// <summary>
        /// Crea SIEMPRE un Canvas nuevo propio de este menú. No reutiliza ninguno existente: si se reutilizara
        /// el Canvas persistente del lobby online, al volver de la SampleScene esta UI quedaría colgada de él y
        /// se ocultaría junto con el overlay.
        /// </summary>
        private void BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;

            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(canvas.transform, false);
            Image bgImage = bg.AddComponent<Image>();
            bgImage.sprite = BuildBackgroundSprite();
            bgImage.color = Color.white;
            Stretch((RectTransform)bg.transform);

            CreateLabel(canvas.transform, "WIPED OUT?", 130, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1600f, 170f), TitleColor);
            CreateLabel(canvas.transform, "PARTIDA ONLINE 1v1 DE MEMORIA Y RIESGO", 34, new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(1600f, 60f), new Color(0.75f, 0.80f, 0.85f, 1f));

            CreateButton(canvas.transform, "JUGAR", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(520f, 100f), ButtonColor, OnPlayClicked);
            CreateButton(canvas.transform, "CRÉDITOS", new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(520f, 100f), ButtonColor, OnCreditsClicked);
            CreateButton(canvas.transform, "SALIR", new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(520f, 100f), DangerColor, OnExitClicked);

            BuildCreditsPanel();
        }

        private void BuildCreditsPanel()
        {
            creditsPanel = new GameObject("CreditsPanel", typeof(RectTransform));
            creditsPanel.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)creditsPanel.transform);
            creditsPanel.AddComponent<Image>().color = PanelBackdrop;

            CreateLabel(creditsPanel.transform, "CRÉDITOS", 84, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1200f, 120f), TitleColor);
            CreateLabel(creditsPanel.transform, "WIPED OUT?\nJuego competitivo de memoria y riesgo para 2 jugadores.", 42, new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(1500f, 150f), Color.white);
            CreateLabel(creditsPanel.transform, "DESARROLLO\nSteve  ·  Erwin  ·  Salim", 48, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1400f, 160f), new Color(0.90f, 0.92f, 0.95f, 1f));
            CreateLabel(creditsPanel.transform, "Proyecto final · Curso de Cloud · Unity 6 (URP)", 32, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1400f, 60f), new Color(0.65f, 0.70f, 0.75f, 1f));
            CreateButton(creditsPanel.transform, "VOLVER", new Vector2(0.5f, 0f), new Vector2(0f, 160f), new Vector2(360f, 90f), ButtonColor, OnCreditsCloseClicked);

            creditsPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- acciones

        private void OnPlayClicked()
        {
            SceneManager.LoadScene(GameSceneName);
        }

        private void OnCreditsClicked()
        {
            creditsPanel.SetActive(true);
        }

        private void OnCreditsCloseClicked()
        {
            creditsPanel.SetActive(false);
        }

        private void OnExitClicked()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------- fondo procedimental

        private static Sprite BuildBackgroundSprite()
        {
            const int width = 960;
            const int height = 540;
            const int cell = 60;

            Color32 dark = new Color32(18, 24, 33, 255);
            Color32 gridLine = new Color32(38, 50, 66, 255);
            Color32 mine = new Color32(240, 108, 30, 255);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = dark;

            for (int x = 0; x < width; x++)
            {
                if (x % cell != 0)
                    continue;

                for (int y = 0; y < height; y++)
                    pixels[y * width + x] = gridLine;
            }

            for (int y = 0; y < height; y++)
            {
                if (y % cell != 0)
                    continue;

                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = gridLine;
            }

            int[,] mines =
            {
                { 3, 2 }, { 11, 6 }, { 6, 4 }, { 13, 3 },
                { 9, 7 }, { 2, 7 }, { 14, 6 },
            };

            for (int m = 0; m < mines.GetLength(0); m++)
            {
                int cx = mines[m, 0] * cell + cell / 2;
                int cy = mines[m, 1] * cell + cell / 2;
                DrawCross(pixels, width, height, cx, cy, 10, mine);
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f));
        }

        private static void DrawCross(Color32[] pixels, int width, int height, int cx, int cy, int radius, Color32 color)
        {
            for (int i = -radius; i <= radius; i++)
            {
                SetPixel(pixels, width, height, cx + i, cy + i, color);
                SetPixel(pixels, width, height, cx + i, cy - i, color);
            }
        }

        private static void SetPixel(Color32[] pixels, int width, int height, int x, int y, Color32 color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;

            pixels[y * width + x] = color;
        }

        // ------------------------------------------------------- helpers de UI

        private Text CreateLabel(Transform parent, string content, int size, Vector2 anchor, Vector2 position, Vector2 dimensions, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 dimensions, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Image image = go.AddComponent<Image>();
            image.color = color;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
            colors.selectedColor = color;
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.5f);
            button.colors = colors;

            Text text = CreateLabel(go.transform, label, 40, new Vector2(0.5f, 0.5f), Vector2.zero, dimensions, Color.white);
            Stretch((RectTransform)text.transform);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
