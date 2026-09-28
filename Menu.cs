using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInfrastructure.Components.ManagedBehaviours;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPresenters.Pause;
using Il2CppTMPro;
using Il2CppViews.Generic;
using Il2CppViews.Pause;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
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
        static UnityAction _buttonClick;
        static PausePresenter _pause;
        static ModEntry _open;
        static Hotkey _listening;
        static OptionRow _listeningRow;
        static int _listenFrame;
        static readonly List<UnityAction> _clicks = new List<UnityAction>();
        static readonly List<OptionRow> _rows = new List<OptionRow>();

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
            _buttonClick = null;
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
            if (_listening != null)
                CaptureKey();
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

            MenuLineButton[] lines = _list.GetComponentsInChildren<MenuLineButton>(true);
            MenuLineButton proto = null;
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    MenuLineButton line = lines[i];
                    if (line == null)
                        continue;
                    if (line.gameObject.name.StartsWith(Prefix + "Mod_"))
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
            }
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
