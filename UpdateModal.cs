using System;
using System.Collections.Generic;
using Il2CppTMPro;
using Il2CppViews.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PhxCore
{
    /// <summary>
    /// "UPDATES AVAILABLE" window shown at startup when any Phoenix557 mod is out of date. One line per mod with its
    /// name, installed and latest version, and a DOWNLOAD button that opens that release on GitHub. X or Esc closes it.
    ///
    /// It takes its font and colours from the game's own pause-menu lines so it reads as part of FRUKT's menus.
    /// Clicks are hit-tested here rather than through the EventSystem, so it works on any screen the game is on.
    /// </summary>
    internal static class UpdateModal
    {
        const float PanelWidth = 1040f;
        const float HeaderHeight = 118f;
        const float RowHeight = 78f;
        const float FooterHeight = 74f;
        const float Pad = 36f;
        const int MaxRows = 8;
        const float FontWait = 8f;

        sealed class Hit
        {
            public RectTransform Rect;
            public Image Plate;
            public TextMeshProUGUI Label;
            public Action Click;
            public bool Hovered;
            public bool IsClose;
        }

        static readonly object Holder = new object();
        static readonly List<Hit> Hits = new List<Hit>();

        static List<Updates.Outdated> _pending;
        static float _waitingSince = -1f;
        static GameObject _root;
        static bool _open;
        static CursorLockMode _savedLock;
        static bool _savedVisible;
        static Sprite _solid;

        // Game look, read from a pause-menu line when one exists. Fallbacks are close to FRUKT's menus.
        static TMP_FontAsset _font;
        static Color _text = new Color(0.86f, 0.86f, 0.82f);
        static Color _dim = new Color(0.55f, 0.55f, 0.52f);
        static Color _accent = new Color(0.95f, 0.76f, 0.2f);
        static Color _accentText = new Color(0.06f, 0.06f, 0.05f);
        static Color _panel = new Color(0.035f, 0.035f, 0.035f, 0.97f);
        static Color _line = new Color(1f, 1f, 1f, 0.07f);

        internal static bool IsOpen => _open;

        internal static void Show(List<Updates.Outdated> outdated)
        {
            if (outdated == null || outdated.Count == 0 || _open)
                return;
            _pending = outdated;
            _waitingSince = Time.unscaledTime;
        }

        internal static void Tick()
        {
            if (_pending != null && !_open)
                TryOpen();
            if (!_open)
                return;

            if (_root == null)
            {
                Close();
                return;
            }
            HoldCursor();

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Vector2 point = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            for (int i = 0; i < Hits.Count; i++)
            {
                Hit hit = Hits[i];
                if (hit.Rect == null)
                    continue;
                bool over = RectTransformUtility.RectangleContainsScreenPoint(hit.Rect, point, null);
                if (over != hit.Hovered)
                {
                    hit.Hovered = over;
                    Paint(hit);
                }
                if (over && pressed)
                {
                    try { hit.Click(); }
                    catch (Exception e) { MelonLogger.Warning("Update window: " + e.Message); }
                    return;
                }
            }
        }

        internal static void LateTick()
        {
            if (_open)
                HoldCursor();
        }

        static void TryOpen()
        {
            bool gameLook = ReadGameLook();
            if (!gameLook && Time.unscaledTime - _waitingSince < FontWait)
                return;
            if (_font == null && !FindAnyFont())
                return;

            try
            {
                Build(_pending);
                _pending = null;
                _open = true;
                _savedLock = Cursor.lockState;
                _savedVisible = Cursor.visible;
                Player.Freeze(Holder);
                MelonLogger.Msg("Update window opened (" + (gameLook ? "game style" : "fallback style") + ").");
            }
            catch (Exception e)
            {
                _pending = null;
                MelonLogger.Warning("Could not show the update window: " + e.Message);
                if (_root != null)
                    Object.Destroy(_root);
                _root = null;
            }
        }

        static void Close()
        {
            _open = false;
            Hits.Clear();
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            Player.Unfreeze(Holder);
            Cursor.lockState = _savedLock;
            Cursor.visible = _savedVisible;
        }

        static void HoldCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static bool ReadGameLook()
        {
            try
            {
                MenuLineButton[] lines = Object.FindObjectsByType<MenuLineButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (lines == null)
                    return false;
                for (int i = 0; i < lines.Length; i++)
                {
                    MenuLineButton line = lines[i];
                    if (line == null || line.m_text == null || line.m_text.m_label == null || line.m_text.m_label.font == null)
                        continue;
                    _font = line.m_text.m_label.font;
                    _text = Opaque(line.m_restLabelColor, _text);
                    _accent = Opaque(line.m_selectedPlateColor, _accent);
                    _accentText = Opaque(line.m_selectedLabelColor, _accentText);
                    _dim = Color.Lerp(_text, new Color(0f, 0f, 0f, 1f), 0.35f);
                    return true;
                }
            }
            catch { }
            return false;
        }

        static Color Opaque(Color color, Color fallback)
        {
            if (color.a < 0.05f)
                return fallback;
            color.a = 1f;
            return color;
        }

        static bool FindAnyFont()
        {
            TextMeshProUGUI[] texts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (texts == null)
                return false;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].font != null)
                {
                    _font = texts[i].font;
                    return true;
                }
            }
            return false;
        }

        static void Build(List<Updates.Outdated> outdated)
        {
            Hits.Clear();
            _root = new GameObject("PhxCore_UpdateWindow");
            Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5002;
            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Dim the game behind the window and swallow clicks meant for the menus underneath.
            RectTransform shade = Box(_root.transform, "Shade", new Color(0f, 0f, 0f, 0.62f));
            Stretch(shade);
            shade.GetComponent<Image>().raycastTarget = true;

            int shown = Math.Min(outdated.Count, MaxRows);
            bool more = outdated.Count > shown;
            float height = HeaderHeight + shown * RowHeight + (more ? 44f : 0f) + FooterHeight;

            RectTransform panel = Box(_root.transform, "Panel", _panel);
            Center(panel, PanelWidth, height);

            // Accent rule along the top edge, like the game's selected plates.
            RectTransform rule = Box(panel, "Rule", _accent);
            Place(rule, 0f, 0f, PanelWidth, 4f);

            Text(panel, "pause / phx mods / updates", 20f, _dim, TextAlignmentOptions.MidlineLeft, Pad, 22f, 600f, 28f);
            Text(panel, "UPDATES AVAILABLE", 46f, _text, TextAlignmentOptions.MidlineLeft, Pad, 48f, 760f, 56f);

            // X in the top-right corner.
            RectTransform close = Box(panel, "Close", new Color(1f, 1f, 1f, 0f));
            Place(close, PanelWidth - 24f - 60f, 24f, 60f, 60f);
            TextMeshProUGUI cross = Text(close, "X", 34f, _text, TextAlignmentOptions.Center, 0f, 0f, 60f, 60f);
            Hits.Add(new Hit { Rect = close, Plate = close.GetComponent<Image>(), Label = cross, Click = Close, IsClose = true });

            // Column captions.
            float y = HeaderHeight - 6f;
            Text(panel, "MOD", 18f, _dim, TextAlignmentOptions.MidlineLeft, Pad, y - 24f, 400f, 22f);
            Text(panel, "INSTALLED", 18f, _dim, TextAlignmentOptions.MidlineLeft, 470f, y - 24f, 160f, 22f);
            Text(panel, "LATEST", 18f, _dim, TextAlignmentOptions.MidlineLeft, 630f, y - 24f, 160f, 22f);

            for (int i = 0; i < shown; i++)
            {
                Updates.Outdated mod = outdated[i];
                float top = HeaderHeight + i * RowHeight;

                RectTransform divider = Box(panel, "Divider", _line);
                Place(divider, Pad, top, PanelWidth - Pad * 2f, 2f);

                float mid = top + RowHeight * 0.5f;
                Text(panel, mod.Name.ToUpperInvariant(), 30f, _text, TextAlignmentOptions.MidlineLeft, Pad, mid - 20f, 420f, 40f);
                Text(panel, "V" + mod.Installed, 26f, _dim, TextAlignmentOptions.MidlineLeft, 470f, mid - 18f, 150f, 36f);
                Text(panel, "V" + mod.Latest, 26f, _accent, TextAlignmentOptions.MidlineLeft, 630f, mid - 18f, 150f, 36f);

                RectTransform button = Box(panel, "Download", new Color(_accent.r, _accent.g, _accent.b, 0.14f));
                Place(button, PanelWidth - Pad - 210f, mid - 25f, 210f, 50f);
                TextMeshProUGUI word = Text(button, "DOWNLOAD", 24f, _accent, TextAlignmentOptions.Center, 0f, 0f, 210f, 50f);
                string url = mod.Url;
                string name = mod.Name;
                Hits.Add(new Hit { Rect = button, Plate = button.GetComponent<Image>(), Label = word, Click = () => OpenRelease(name, url) });
            }

            float bottom = HeaderHeight + shown * RowHeight;
            RectTransform last = Box(panel, "Divider", _line);
            Place(last, Pad, bottom, PanelWidth - Pad * 2f, 2f);
            if (more)
            {
                Text(panel, "+" + (outdated.Count - shown) + " MORE IN THE MELONLOADER LOG", 20f, _dim, TextAlignmentOptions.MidlineLeft, Pad, bottom + 10f, 700f, 28f);
                bottom += 44f;
            }
            Text(panel, "Close FRUKT before replacing the files in the Mods folder.   ESC to close", 20f, _dim,
                TextAlignmentOptions.MidlineLeft, Pad, bottom + (FooterHeight - 28f) * 0.5f, PanelWidth - Pad * 2f, 28f);
        }

        static void OpenRelease(string name, string url)
        {
            if (!Updates.IsOurRelease(url))
            {
                MelonLogger.Warning("Refused to open an unexpected link for " + name + ".");
                return;
            }
            MelonLogger.Msg("Opening the " + name + " release: " + url);
            Application.OpenURL(url);
        }

        static void Paint(Hit hit)
        {
            if (hit.Plate != null)
            {
                Color rest = hit.IsClose ? new Color(1f, 1f, 1f, 0f) : new Color(_accent.r, _accent.g, _accent.b, 0.14f);
                hit.Plate.color = hit.Hovered ? _accent : rest;
            }
            if (hit.Label != null)
            {
                hit.Label.color = hit.Hovered ? _accentText : (hit.IsClose ? _text : _accent);
            }
        }

        static RectTransform Box(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = Solid();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h)
        {
            var go = new GameObject("Text");
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Top-left based placement inside the parent, in reference pixels.</summary>
        static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        static void Center(RectTransform rect, float w, float h)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Sprite Solid()
        {
            if (_solid != null)
                return _solid;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            Object.DontDestroyOnLoad(tex);
            _solid = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            Object.DontDestroyOnLoad(_solid);
            return _solid;
        }
    }
}
