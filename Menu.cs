using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInfrastructure.Components.ManagedBehaviours;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPresenters.Pause;
using Il2CppTMPro;
using Il2CppViews.Game;
using Il2CppViews.Generic;
using Il2CppViews.Pause;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PhxCore
{
    static class Menu
    {
        const string Prefix = "PhxCore_";
        const string Title = "PHX MODS";

        enum Page
        {
            None,
            List,
            Mod
        }

        internal static bool BlocksHotkeys => _page != Page.None || _listening != null || UpdateModal.IsOpen;

        static Page _page;
        static bool _failed;
        static PauseScreen _root;
        static PauseScreen _list;
        static PauseScreen _mod;
        static PathHeaderView _listHeader;
        static PathHeaderView _modHeader;
        static MenuLineButton _button;
        static MenuLineButton _respawn;
        static MenuLineButton _apply;
        static UnityAction _buttonClick;
        static UnityAction _applyClick;
        static bool _applyFailed;
        static PausePresenter _pause;
        static ModEntry _open;
        static Hotkey _listening;
        static OptionRow _listeningRow;
        static int _listenFrame;
        static readonly List<UnityAction> _clicks = new List<UnityAction>();
        static readonly List<OptionRow> _rows = new List<OptionRow>();
        static readonly List<PowerPlate> _powers = new List<PowerPlate>();

        /// <summary>An on/off plate and the name row it sits beside.</summary>
        sealed class PowerPlate
        {
            internal MenuLineButton Power;
            internal MenuLineButton Row;
        }

        /// <summary>One line on a mod's page: a full-size pause-menu line reading "LABEL   VALUE".</summary>
        sealed class OptionRow
        {
            internal MenuLineButton Line;
            internal string Label;
            internal string Value;
        }
        internal static void Reset()
        {
            _page = Page.None;
            _failed = false;
            _root = null;
            _list = null;
            _mod = null;
            _listHeader = null;
            _modHeader = null;
            _button = null;
            _respawn = null;
            _apply = null;
            _buttonClick = null;
            _applyClick = null;
            _applyFailed = false;
            _powers.Clear();
            _pause = null;
            _open = null;
            _listening = null;
            _listeningRow = null;
            _clicks.Clear();
            _rows.Clear();
        }

        internal static void Tick()
        {
            bool paused = IsPaused();
            if (!paused && _page != Page.None)
                HideImmediate();
            if (paused)
                EnsureButton();
            RefreshApply();
            if (_listening != null)
                CaptureKey();
        }

        internal static void LateTick()
        {
            if (_page == Page.List)
            {
                for (int i = 0; i < _powers.Count; i++)
                    PlacePower(_powers[i].Row, _powers[i].Power);
            }
            if (_apply != null && _apply.gameObject.activeInHierarchy)
                PlaceApply(_apply);
        }

        static void ShowList()
        {
            if (!EnsureScreens())
                return;
            if (_page != Page.None)
                return;

            Registry.Discover();
            FillMods();
            WriteHeader(_listHeader, Title);
            if (Ride(_root, _list, false))
                _page = Page.List;
        }

        static void OpenMod(ModEntry mod)
        {
            if (_list == null || _mod == null || mod == null || _page != Page.List)
                return;

            _open = mod;
            _listening = null;
            _listeningRow = null;
            FillOptions(mod);
            WriteHeader(_modHeader, mod == Registry.Core ? "SETTINGS" : mod.Name.ToUpperInvariant());
            if (Ride(_list, _mod, false))
                _page = Page.Mod;
        }

        internal static void Back()
        {
            if (_listening != null)
            {
                CancelListen();
                return;
            }

            if (_page == Page.Mod)
            {
                if (Ride(_mod, _list, true))
                    _page = Page.List;
                _open = null;
                return;
            }

            if (_page == Page.List && Ride(_list, _root, true))
                _page = Page.None;
        }

        static void CancelListen()
        {
            _listening = null;
            Highlight(_listeningRow, false);
            _listeningRow = null;
        }

        static void HideImmediate()
        {
            _listening = null;
            _listeningRow = null;
            _open = null;
            _page = Page.None;
            try { if (_mod != null) _mod.Erase(); } catch { }
            try { if (_list != null) _list.Erase(); } catch { }
        }

        static void EnsureButton()
        {
            if (_button != null || _failed)
                return;

            PauseView view = UnityEngine.Object.FindFirstObjectByType<PauseView>();
            if (view == null || view.m_settingsButton == null || view.m_settingsButton.transform.parent == null)
                return;

            MenuLineButton proto = view.m_settingsButton;
            if (_button == null && !_failed)
                _button = AddLine(proto, Prefix + "Button", Title, (UnityAction)(Action)ShowList, ref _buttonClick, ref _failed, "pause");
        }

        static MenuLineButton AddLine(MenuLineButton proto, string name, string word, UnityAction click, ref UnityAction stored, ref bool failed, string what)
        {
            try
            {
                MenuLineButton clone = Clone(proto, proto.transform.parent, name);
                if (clone == null)
                    return null;

                int index = proto.transform.GetSiblingIndex() + 1;
                Transform mods = proto.transform.parent.Find("FruitLib_ModsButton");
                if (mods != null)
                    index = mods.GetSiblingIndex() + 1;
                Transform old = proto.transform.parent.Find("Phx_Button");
                if (old != null)
                    index = old.GetSiblingIndex() + 1;
                Transform phx = proto.transform.parent.Find(Prefix + "Button");
                if (phx != null && name != Prefix + "Button")
                    index = phx.GetSiblingIndex() + 1;
                clone.transform.SetSiblingIndex(index);
                clone.gameObject.SetActive(true);
                clone.SetWord(word);
                if (clone.m_button == null)
                {
                    UnityEngine.Object.Destroy(clone.gameObject);
                    failed = true;
                    return null;
                }

                stored = click;
                clone.m_button.onClick.AddListener(stored);
                MelonLogger.Msg("Pause menu " + word + " button added.");
                return clone;
            }
            catch (Exception e)
            {
                failed = true;
                MelonLogger.Warning("Could not add the " + what + " button: " + e.Message);
                return null;
            }
        }

        const float ApplyGap = 28f;
        const float ApplyWidth = 360f;

        /// <summary>Shows APPLY CHANGES to the right of the kills counter while a staged on/off is waiting.</summary>
        static void RefreshApply()
        {
            bool show = IsPaused() && Registry.HasPending;
            if (!show)
            {
                if (_apply != null)
                    _apply.gameObject.SetActive(false);
                return;
            }

            if (_apply == null)
                EnsureApply();
            if (_apply != null)
                _apply.gameObject.SetActive(true);
        }

        static void EnsureApply()
        {
            if (_apply != null || _applyFailed)
                return;

            PauseView view = UnityEngine.Object.FindFirstObjectByType<PauseView>();
            if (view == null || view.m_settingsButton == null)
                return;

            MenuLineButton proto = view.m_settingsButton;
            Canvas canvas = proto.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            try
            {
                MenuLineButton clone = Clone(proto, canvas.transform, Prefix + "Apply");
                if (clone == null || clone.m_button == null)
                {
                    if (clone != null)
                        UnityEngine.Object.Destroy(clone.gameObject);
                    _applyFailed = true;
                    return;
                }

                clone.gameObject.SetActive(true);
                IgnoreLayout(clone);

                FitPlate(clone);
                Style(clone);
                clone.SetWord("APPLY CHANGES");
                PlaceApply(clone);
                clone.m_button.onClick.RemoveAllListeners();
                _applyClick = (UnityAction)(Action)ApplyChanges;
                _clicks.Add(_applyClick);
                clone.m_button.onClick.AddListener(_applyClick);
                _apply = clone;
                MelonLogger.Msg("Pause menu APPLY CHANGES button added.");
            }
            catch (Exception e)
            {
                _applyFailed = true;
                MelonLogger.Warning("Could not add the apply button: " + e.Message);
            }
        }

        static void ApplyChanges()
        {
            if (!Registry.HasPending)
                return;
            Registry.ApplyPending();
            RefreshApply();
            ReloadMap();
        }

        static void ReloadMap()
        {
            try
            {
                if (_pause == null)
                    _pause = UnityEngine.Object.FindFirstObjectByType<PausePresenter>();
                if (_pause != null && _pause.m_pauseService != null)
                {
                    var pause = _pause.m_pauseService.TryCast<Il2CppGame.PauseService>();
                    if (pause != null)
                        pause.CancelPause();
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not unpause: " + e.Message);
            }

            try
            {
                Scene scene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(scene.buildIndex);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not reload the map: " + e.Message);
            }
        }

        static bool EnsureScreens()
        {
            if (_list != null && _mod != null)
                return true;
            if (_failed)
                return false;

            PauseView view = UnityEngine.Object.FindFirstObjectByType<PauseView>();
            if (view == null)
                return false;

            try
            {
                _root = FindScreen(view, "RootScreen");
                PauseScreen source = FindScreen(view, "SettingsScreen");
                if (_root == null || source == null || source.Timings == null)
                {
                    Fail("the pause screens were not ready");
                    return false;
                }

                _list = CopyScreen(source, Prefix + "List");
                _mod = CopyScreen(source, Prefix + "Mod");
                if (_list == null || _mod == null)
                {
                    Fail("the settings screen could not be copied");
                    return false;
                }

                _listHeader = _list.GetComponentInChildren<PathHeaderView>(true);
                _modHeader = _mod.GetComponentInChildren<PathHeaderView>(true);
                if (LineProto(_mod) == null)
                {
                    Fail("no menu line to copy");
                    return false;
                }

                WriteHeader(_listHeader, Title);
                MelonLogger.Msg("Settings pages built.");
                return true;
            }
            catch (Exception e)
            {
                Fail(e.Message);
                return false;
            }
        }

        static PauseScreen CopyScreen(PauseScreen source, string name)
        {
            PauseScreen clone = Clone(source, source.transform.parent, name);
            if (clone == null)
                return null;
            clone.gameObject.SetActive(true);
            clone.SetRideTimings(source.Timings);
            try { clone.Erase(); } catch { }
            MenuLineButton[] lines = clone.GetComponentsInChildren<MenuLineButton>(true);
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] != null)
                        lines[i].gameObject.SetActive(false);
                }
            }
            return clone;
        }

        static void FillMods()
        {
            Transform column = Column(_list);
            if (column == null)
                return;
            _powers.Clear();

            MenuLineButton[] lines = _list.GetComponentsInChildren<MenuLineButton>(true);
            MenuLineButton proto = null;
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    MenuLineButton line = lines[i];
                    if (line == null)
                        continue;
                    if (line.gameObject.name.StartsWith(Prefix + "Mod_") || line.gameObject.name.StartsWith(Prefix + "Power") || line.gameObject.name.StartsWith(Prefix + "Slot_"))
                    {
                        UnityEngine.Object.Destroy(line.gameObject);
                        continue;
                    }
                    line.gameObject.SetActive(false);
                    if (proto == null)
                        proto = line;
                }
            }
            if (proto == null)
                return;

            MenuLineButton settings = Clone(proto, column, Prefix + "Mod_Settings");
            if (settings != null && settings.m_button != null)
            {
                settings.gameObject.SetActive(true);
                settings.SetWord("SETTINGS");
                UnityAction openSettings = (UnityAction)(Action)(() => OpenMod(Registry.Core));
                _clicks.Add(openSettings);
                settings.m_button.onClick.AddListener(openSettings);
            }

            // RESPAWN lives here rather than on the pause menu itself, which has no room once other mods add their buttons.
            if (Player.CanRespawn)
            {
                MenuLineButton respawn = Clone(proto, column, Prefix + "Mod_Respawn");
                if (respawn != null && respawn.m_button != null)
                {
                    respawn.gameObject.SetActive(true);
                    respawn.SetWord("RESPAWN");
                    UnityAction doRespawn = (UnityAction)(Action)(() =>
                    {
                        Player.Respawn();
                        if (_page == Page.List && Ride(_list, _root, true))
                            _page = Page.None;
                    });
                    _clicks.Add(doRespawn);
                    respawn.m_button.onClick.AddListener(doRespawn);
                }
            }

            if (Registry.All.Count == 0)
            {
                MenuLineButton empty = Clone(proto, column, Prefix + "Mod_Empty");
                if (empty == null)
                    return;
                empty.gameObject.SetActive(true);
                empty.SetWord("NO MODS");
                return;
            }

            for (int i = 0; i < Registry.All.Count; i++)
            {
                ModEntry mod = Registry.All[i];
                MenuLineButton row = Clone(proto, column, Prefix + "Mod_" + i);
                if (row == null || row.m_button == null)
                    continue;
                row.gameObject.SetActive(true);
                row.SetWord(mod.Name.ToUpperInvariant());
                ModEntry captured = mod;
                UnityAction click = (UnityAction)(Action)(() => OpenMod(captured));
                _clicks.Add(click);
                row.m_button.onClick.AddListener(click);
                AddPower(proto, row, captured, i);
            }
        }

        const float PowerWidth = 150f;
        const float PowerGap = 56f;

        /// <summary>ON / OFF is its own plate, sitting to the left of the name with a gap between them.</summary>
        static void AddPower(MenuLineButton proto, MenuLineButton row, ModEntry mod, int index)
        {
            if (proto == null || row == null || !Registry.CanToggle(mod))
                return;

            MenuLineButton power = Clone(proto, row.transform, Prefix + "Power_" + index);
            if (power == null || power.m_button == null)
                return;

            power.gameObject.SetActive(true);
            IgnoreLayout(power);
            // Clip everything the plate draws to its own rect, so hover bars and glow cannot spill onto the name.
            try
            {
                if (power.GetComponent<RectMask2D>() == null)
                    power.gameObject.AddComponent<RectMask2D>();
            }
            catch { }
            _powers.Add(new PowerPlate { Power = power, Row = row });
            PlacePower(row, power);
            FitPlate(power);
            Style(power);
            try
            {
                if (power.m_text != null && power.m_text.m_label != null)
                    power.m_text.m_label.alignment = TextAlignmentOptions.Center;
            }
            catch { }
            ApplyPowerWord(power, mod);

            ModEntry captured = mod;
            MenuLineButton capturedPower = power;
            UnityAction click = (UnityAction)(Action)(() =>
            {
                Registry.StageToggle(captured);
                ApplyPowerWord(capturedPower, captured);
                RefreshApply();
            });
            _clicks.Add(click);
            power.m_button.onClick.RemoveAllListeners();
            power.m_button.onClick.AddListener(click);
        }

        static void IgnoreLayout(Component item)
        {
            if (item == null)
                return;
            LayoutElement layout = item.GetComponent<LayoutElement>();
            if (layout == null)
                layout = item.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        /// <summary>
        /// Right edge stops PowerGap pixels before the name's letters. The plate is capped at PowerWidth and clips what it
        /// draws, so it can never reach the name.
        /// </summary>
        static void PlacePower(MenuLineButton row, MenuLineButton power)
        {
            if (row == null || power == null)
                return;
            RectTransform powerRect = power.GetComponent<RectTransform>();
            RectTransform parent = power.transform.parent != null ? power.transform.parent.TryCast<RectTransform>() : null;
            if (powerRect == null || parent == null)
                return;
            if (!GlyphPoint(row, true, out Vector3 world))
                return;
            if (!ToLocal(parent, world, out Vector2 local))
                return;

            float height = parent.rect.height;
            if (height < 32f)
                height = 64f;
            Vector2 anchor = parent.pivot;
            powerRect.anchorMin = anchor;
            powerRect.anchorMax = anchor;
            float right = local.x - PowerGap;
            float width = PowerWidth;
            powerRect.pivot = new Vector2(1f, 0.5f);
            powerRect.sizeDelta = new Vector2(width, height);
            powerRect.anchoredPosition = new Vector2(right, local.y);
            powerRect.localScale = Vector3.one;
            // The line's hover animation resizes its children each frame; pull them back inside the plate.
            FitPlate(power);
        }

        /// <summary>Left edge starts ApplyGap pixels after the kills counter.</summary>
        static void PlaceApply(MenuLineButton button)
        {
            if (button == null)
                return;
            RectTransform rect = button.GetComponent<RectTransform>();
            RectTransform parent = button.transform.parent != null ? button.transform.parent.TryCast<RectTransform>() : null;
            if (rect == null || parent == null)
                return;

            KillsLine kills = UnityEngine.Object.FindFirstObjectByType<KillsLine>();
            if (kills == null || !RightEdge(kills.gameObject, out Vector3 world))
                return;
            if (!ToLocal(parent, world, out Vector2 local))
                return;

            float height = rect.rect.height;
            if (height < 32f)
                height = rect.sizeDelta.y;
            if (height < 32f)
                height = 64f;
            float width = ApplyWidth;
            try
            {
                TextMeshProUGUI word = button.m_text != null ? button.m_text.m_label : null;
                if (word != null)
                {
                    word.ForceMeshUpdate();
                    if (word.preferredWidth > 40f)
                        width = word.preferredWidth + 96f;
                }
            }
            catch { }

            Vector2 anchor = parent.pivot;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(local.x + ApplyGap, local.y);
            rect.localScale = Vector3.one;
        }

        /// <summary>
        /// World point on the right edge of the last visible letter under host, so "KILLS:" and the count are both
        /// cleared even when they are separate labels.
        /// </summary>
        static bool RightEdge(GameObject host, out Vector3 world)
        {
            world = Vector3.zero;
            if (host == null)
                return false;
            bool found = false;
            float bestX = float.MinValue;
            Vector3 best = Vector3.zero;
            try
            {
                TextMeshProUGUI[] labels = host.GetComponentsInChildren<TextMeshProUGUI>(false);
                for (int i = 0; labels != null && i < labels.Length; i++)
                {
                    TextMeshProUGUI label = labels[i];
                    if (label == null || !label.enabled || string.IsNullOrEmpty(label.text))
                        continue;
                    if (!GlyphPoint(label, label.gameObject, false, out Vector3 point))
                        continue;
                    if (!found || point.x > bestX)
                    {
                        bestX = point.x;
                        best = point;
                        found = true;
                    }
                }
            }
            catch { }
            if (!found)
                return GlyphPoint(host, false, out world);

            world = best;
            return true;
        }

        /// <summary>World point on the left or right edge of a line's letters.</summary>
        static bool GlyphPoint(MenuLineButton line, bool left, out Vector3 world)
        {
            world = Vector3.zero;
            if (line == null)
                return false;
            TextMeshProUGUI label = null;
            try { label = line.m_text != null ? line.m_text.m_label : null; } catch { }
            return GlyphPoint(label, line.gameObject, left, out world);
        }

        static bool GlyphPoint(GameObject host, bool left, out Vector3 world)
        {
            world = Vector3.zero;
            if (host == null)
                return false;
            TextMeshProUGUI label = null;
            try
            {
                TextMeshProUGUI[] labels = host.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (labels != null && labels.Length > 0)
                    label = labels[0];
            }
            catch { }
            return GlyphPoint(label, host, left, out world);
        }

        static bool GlyphPoint(TextMeshProUGUI label, GameObject host, bool left, out Vector3 world)
        {
            world = Vector3.zero;
            if (label != null)
            {
                try
                {
                    label.ForceMeshUpdate();
                    Bounds bounds = label.textBounds;
                    if (bounds.size.x > 1f)
                    {
                        float edge = left ? bounds.min.x : bounds.max.x;
                        world = label.transform.TransformPoint(new Vector3(edge, bounds.center.y, 0f));
                        return true;
                    }
                    float half = Mathf.Max(label.preferredWidth, 8f) * 0.5f;
                    float x = label.rectTransform.rect.center.x + (left ? -half : half);
                    world = label.transform.TransformPoint(new Vector3(x, label.rectTransform.rect.center.y, 0f));
                    return true;
                }
                catch { }
            }

            if (host == null)
                return false;
            RectTransform rect = host.GetComponent<RectTransform>();
            if (rect == null)
                return false;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            world = left ? (corners[0] + corners[1]) * 0.5f : (corners[2] + corners[3]) * 0.5f;
            return true;
        }

        static bool ToLocal(RectTransform parent, Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            if (parent == null)
                return false;
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out local);
        }

        /// <summary>Keeps every piece of the plate inside the button, so a wide child cannot reach the name.</summary>
        static void FitPlate(MenuLineButton line)
        {
            if (line == null)
                return;
            RectTransform[] rects = line.GetComponentsInChildren<RectTransform>(true);
            if (rects == null)
                return;
            for (int i = 0; i < rects.Length; i++)
            {
                RectTransform rect = rects[i];
                if (rect == null || rect == line.transform)
                    continue;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
        }

        static void ApplyPowerWord(MenuLineButton power, ModEntry mod)
        {
            if (power == null || mod == null)
                return;
            try { power.SetWord(Registry.DesiredWord(mod)); } catch { }
        }

        static void FillOptions(ModEntry mod)
        {
            Transform column = Column(_mod);
            MenuLineButton proto = LineProto(_mod);
            if (column == null || proto == null)
                return;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null && _rows[i].Line != null)
                    UnityEngine.Object.Destroy(_rows[i].Line.gameObject);
            }
            _rows.Clear();
            _listening = null;
            _listeningRow = null;

            if (mod == Registry.Core)
                AddRow(proto, column, "PhxCore", "V" + PhxPlugin.Version, null);
            else if (!string.IsNullOrEmpty(mod.Version))
                AddRow(proto, column, "Version", "V" + mod.Version, null);
            if (!mod.HasOptions)
                AddRow(proto, column, "No options", "-", null);

            for (int i = 0; i < mod.Keys.Count; i++)
            {
                Hotkey option = mod.Keys[i];
                OptionRow row = null;
                row = AddRow(proto, column, option.Label, option.Current.ToString(), () =>
                {
                    if (_listening != null)
                    {
                        Write(_listeningRow, _listening.Current.ToString());
                        CancelListen();
                    }
                    _listening = option;
                    _listeningRow = row;
                    _listenFrame = Time.frameCount;
                    Highlight(row, true);
                    Write(row, "PRESS A KEY");
                });
            }
            for (int i = 0; i < mod.Flags.Count; i++)
            {
                Switch option = mod.Flags[i];
                OptionRow row = null;
                row = AddRow(proto, column, option.Label, option.On ? "ON" : "OFF", () =>
                {
                    if (_listening != null)
                        return;
                    option.Set(!option.On);
                    Write(row, option.On ? "ON" : "OFF");
                });
            }
            for (int i = 0; i < mod.Choices.Count; i++)
            {
                Choice option = mod.Choices[i];
                OptionRow row = null;
                row = AddRow(proto, column, option.Label, option.Value, () =>
                {
                    if (_listening != null)
                        return;
                    int next = option.Next();
                    Write(row, option.Options[next]);
                });
            }
        }

        static OptionRow AddRow(MenuLineButton proto, Transform column, string label, string value, Action click)
        {
            MenuLineButton line = Clone(proto, column, Prefix + "Option_" + _rows.Count);
            if (line == null)
                return null;
            line.gameObject.SetActive(true);
            line.SetWord(label.ToUpperInvariant());

            var row = new OptionRow { Line = line, Label = label.ToUpperInvariant() };
            Style(line);
            Write(row, value);
            _rows.Add(row);

            if (line.m_button != null)
            {
                line.m_button.onClick.RemoveAllListeners();
                if (click != null)
                {
                    UnityAction action = (UnityAction)click;
                    _clicks.Add(action);
                    line.m_button.onClick.AddListener(action);
                }
            }
            return row;
        }

        /// <summary>One line of text that shrinks a little if it must, rather than wrapping or running off the plate.</summary>
        static void Style(MenuLineButton line)
        {
            try
            {
                TextMeshProUGUI text = line.m_text != null ? line.m_text.m_label : null;
                if (text == null)
                    return;
                text.enableWordWrapping = false;
                float size = text.fontSize;
                text.fontSizeMax = size;
                text.fontSizeMin = Mathf.Max(8f, size * 0.6f);
                text.enableAutoSizing = true;
            }
            catch { }
        }

        /// <summary>
        /// "LABEL   :   VALUE" on one full-size line. Plain text on purpose: the line's console effect glitches letters at
        /// random, and it would glitch the inside of rich-text tags too.
        /// </summary>
        static void Write(OptionRow row, string value)
        {
            if (row == null || row.Line == null)
                return;
            row.Value = value;
            try { row.Line.SetWord(row.Label + "   :   " + value); } catch { }
        }

        static void Highlight(OptionRow row, bool on)
        {
            if (row == null || row.Line == null)
                return;
            try { row.Line.SetOn(on); } catch { }
        }

        static MenuLineButton LineProto(PauseScreen screen)
        {
            if (screen == null)
                return null;
            MenuLineButton[] lines = screen.GetComponentsInChildren<MenuLineButton>(true);
            if (lines == null)
                return null;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null && !lines[i].gameObject.name.StartsWith(Prefix))
                    return lines[i];
            }
            return null;
        }

        static void CaptureKey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || keyboard.allKeys == null || _listening == null || Time.frameCount == _listenFrame)
                return;

            foreach (KeyControl control in keyboard.allKeys)
            {
                if (control == null || !control.wasPressedThisFrame)
                    continue;
                if (control.keyCode == Key.Escape || control.keyCode == Key.None)
                {
                    Write(_listeningRow, _listening.Current.ToString());
                    CancelListen();
                    return;
                }

                _listening.Set(control.keyCode);
                MelonLogger.Msg("" + _listening.Mod + " " + _listening.Label + " set to " + control.keyCode + ".");
                Write(_listeningRow, control.keyCode.ToString());
                CancelListen();
                return;
            }
        }

        static void WriteHeader(PathHeaderView header, string current)
        {
            if (header == null)
                return;
            try
            {
                if (header.m_trail != null)
                    header.m_trail.text = "pause / phx mods";
                if (header.m_current != null)
                    header.m_current.text = current;
            }
            catch { }
        }

        static Transform Column(PauseScreen screen)
        {
            if (screen == null)
                return null;
            MenuLineButton[] lines = screen.GetComponentsInChildren<MenuLineButton>(true);
            if (lines == null || lines.Length == 0 || lines[0] == null)
                return null;
            return lines[0].transform.parent;
        }

        static bool Ride(PauseScreen outgoing, PauseScreen incoming, bool rewind)
        {
            if (outgoing == null || incoming == null)
                return false;
            try
            {
                MenuLineButton[] buttons = outgoing.GetComponentsInChildren<MenuLineButton>(true);
                if (buttons != null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (buttons[i] != null)
                            buttons[i].Flash.Snap(false);
                    }
                }
            }
            catch { }

            try { incoming.Open(rewind); }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not open the page: " + e.Message);
                return false;
            }

            try { outgoing.Close(rewind); }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not close the page: " + e.Message);
            }
            return true;
        }

        static PauseScreen FindScreen(PauseView view, string name)
        {
            var screens = view.m_screens;
            if (screens == null)
                return null;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                if (screen != null && screen.name == name)
                    return screen.TryCast<PauseScreen>();
            }
            return null;
        }

        static T Clone<T>(T prototype, Transform parent, string name) where T : Component
        {
            RemoveOld(parent, name);
            GameObject crib = new GameObject(Prefix + "Crib");
            crib.SetActive(false);
            try
            {
                T clone = UnityEngine.Object.Instantiate(prototype, crib.transform, false);
                clone.gameObject.name = name;
                Inject(prototype.gameObject, clone.gameObject);
                clone.transform.SetParent(parent, false);
                return clone;
            }
            finally
            {
                UnityEngine.Object.Destroy(crib);
            }
        }

        /// <summary>
        /// Takes away a copy made earlier under the same name: one left by a Phx that was loaded before this one (hot reload), or by a
        /// scene this pause menu outlived. Its buttons still call into the old copy, so it is replaced rather than reused.
        /// </summary>
        static void RemoveOld(Transform parent, string name)
        {
            if (parent == null)
                return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child == null || child.name != name)
                    continue;
                child.name = Prefix + "Old";
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        /// <summary>Takes this copy's button and pages out of the pause menu, so a reloaded Phx starts clean.</summary>
        internal static void Teardown()
        {
            try { if (_button != null) UnityEngine.Object.Destroy(_button.gameObject); } catch { }
            try { if (_respawn != null) UnityEngine.Object.Destroy(_respawn.gameObject); } catch { }
            try { if (_apply != null) UnityEngine.Object.Destroy(_apply.gameObject); } catch { }
            try { if (_list != null) UnityEngine.Object.Destroy(_list.gameObject); } catch { }
            try { if (_mod != null) UnityEngine.Object.Destroy(_mod.gameObject); } catch { }
            Reset();
        }

        static void Inject(GameObject source, GameObject clone)
        {
            ManagedBehaviour[] sources = source.GetComponentsInChildren<ManagedBehaviour>(true);
            Il2CppServices.Infrastructure.IManagedBehaviourCoreServicesProvider provider = null;
            if (sources != null)
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] != null && sources[i].m_coreServicesProvider != null)
                    {
                        provider = sources[i].m_coreServicesProvider;
                        break;
                    }
                }
            }

            if (provider != null)
            {
                ManagedBehaviour[] behaviours = clone.GetComponentsInChildren<ManagedBehaviour>(true);
                if (behaviours != null)
                {
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        if (behaviours[i] != null)
                            behaviours[i].m_coreServicesProvider = provider;
                    }
                }
            }

            SceneRevealEdgeScreenTransition[] rides = source.GetComponentsInChildren<SceneRevealEdgeScreenTransition>(true);
            SceneRevealEdgeScreenTransition template = null;
            if (rides != null)
            {
                for (int i = 0; i < rides.Length; i++)
                {
                    if (rides[i] != null && rides[i].m_revealEdges != null)
                    {
                        template = rides[i];
                        break;
                    }
                }
            }
            if (template == null)
                return;

            SceneRevealEdgeScreenTransition[] copies = clone.GetComponentsInChildren<SceneRevealEdgeScreenTransition>(true);
            if (copies == null)
                return;
            for (int i = 0; i < copies.Length; i++)
            {
                if (copies[i] == null)
                    continue;
                copies[i].m_updateLoop = template.m_updateLoop;
                copies[i].m_revealEdges = template.m_revealEdges;
            }
        }

        internal static bool IsPaused()
        {
            if (_pause == null)
                _pause = UnityEngine.Object.FindFirstObjectByType<PausePresenter>();
            if (_pause == null || _pause.m_pauseService == null)
                return false;
            var pause = _pause.m_pauseService.TryCast<Il2CppGame.PauseService>();
            return pause != null && pause.Paused;
        }

        static void Fail(string reason)
        {
            _failed = true;
            MelonLogger.Warning("Settings pages were not built (" + reason + ").");
        }
    }

    [HarmonyPatch(typeof(PausePresenter), nameof(PausePresenter.Services_UI_IPauseBackNavigation_TryStepBack))]
    static class PhxBackPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!Menu.BlocksHotkeys)
                return true;
            Menu.Back();
            __result = true;
            return false;
        }
    }
}
