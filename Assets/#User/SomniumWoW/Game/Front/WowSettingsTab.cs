using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The room panel's Settings tab: the WowSettings fields as WoW-styled check boxes, sliders and choice rows, in four
    /// pages (Display, Effects, Audio, Gameplay). It shares nothing with the in-game settings window but WowSettings: a
    /// change here sets the field and calls WowSettings.NotifyChanged (WowGame saves it, debounced); a change made anywhere
    /// else (WowSettings.Changed) refreshes the controls shown here.
    /// Stage A1: a fifth page, Online - the server, the automatic WoW account made from the Somnium account, whether this
    /// computer holds its key, and "Forget this computer's key" (asks first; WowGame empties the stored key).
    /// A sixth page, Game: the World of Warcraft 3.3.5a folder in use (or "not found"), how it was found, the folder saved
    /// for this Somnium account on this PC, and "Change..." (WowGame opens the room's folder browser, WowInstallPicker).
    /// </summary>
    public sealed class WowSettingsTab
    {
        public enum Page { Display = 0, Effects = 1, Audio = 2, Gameplay = 3, Online = 4, Game = 5 }

        private static readonly string[] PageNames = { "Display", "Effects", "Audio", "Gameplay", "Online", "Game" };

        /// <summary>What the Online page shows about the automatic account (WowGame fills it when asked).</summary>
        public sealed class AutoKeyState
        {
            public string Server = "";   // the server the key belongs to
            public string Name;          // the automatic account name, null when there is none (editor, no Somnium ID yet)
            public string Why = "";      // why there is none (plain words)
            public bool HasKey;          // this computer holds its key
        }

        /// <summary>Stage A1: asked whenever the Online page is drawn.</summary>
        public Func<AutoKeyState> AutoKey;

        /// <summary>Stage A1: "Forget this computer's key" confirmed.</summary>
        public event Action ForgetKeyConfirmed;

        /// <summary>What the Game page shows about the game folder (WowGame fills it when asked).</summary>
        public sealed class InstallState
        {
            public string Folder;          // the install in use, null when none was found
            public string Locale;          // its language folder
            public string Source = "";     // how it was found (plain words)
            public string Saved;           // the folder saved for this Somnium account on this PC, null when none
            public string SavedProblem;    // why the saved folder was not usable at the last start (plain words), or null
            public bool CanChange;         // "Change..." pressable (only in the room, not while the files are being read)
            public string Why = "";        // why not (plain words)
        }

        /// <summary>Asked whenever the Game page is drawn.</summary>
        public Func<InstallState> Install;

        /// <summary>The Game page's "Change..." pressed.</summary>
        public event Action ChangeFolderPressed;

        private WowRoom _room;
        private GameObject _content;
        private GameObject _dialog;
        private Page _page = Page.Display;
        private readonly List<WowGlueButton> _pageButtons = new List<WowGlueButton>();
        private readonly List<Action> _refreshers = new List<Action>();
        private bool _subscribed;
        private bool _setting;

        /// <summary>Hooks into the room's Settings tab (call after every WowRoom.BuildPanel) and draws the current page.</summary>
        public void Attach(WowRoom room)
        {
            _room = room;
            _content = null;

            if (!_subscribed)
            {
                WowSettings.Changed += OnChanged;
                _subscribed = true;
            }

            Rebuild();
        }

        public void Detach()
        {
            if (_subscribed)
            {
                WowSettings.Changed -= OnChanged;
                _subscribed = false;
            }
        }

        private void OnChanged()
        {
            if (_setting || _content == null) return;
            foreach (Action r in _refreshers)
            {
                try { r(); }
                catch (Exception) { }
            }
        }

        /// <summary>Stage A1: redraws the Online page when it is shown (the key was stored or forgotten, the identity arrived).</summary>
        public void RefreshOnline()
        {
            if (_page == Page.Online && _content != null) Rebuild();
        }

        /// <summary>Redraws the Game page when it is shown (the game folder changed, the files are being read or were read).</summary>
        public void RefreshInstall()
        {
            if (_page == Page.Game && _content != null) Rebuild();
        }

        private void Rebuild()
        {
            CloseDialog();

            if (_content != null)
            {
                UnityEngine.Object.Destroy(_content);
                _content = null;
            }

            _pageButtons.Clear();
            _refreshers.Clear();

            if (_room == null || _room.SettingsRoot == null) return;

            RectTransform root = WowUi.Place(_room.SettingsRoot, "Settings " + _page, 0f, 0f, WowRoom.PanelWidth, WowRoom.TallHeight);
            _content = root.gameObject;

            for (int i = 0; i < PageNames.Length; i++)
            {
                Page p = (Page)i;
                WowGlueButton b = WowUi.Button(root, PageNames[i], 50f + i * 132f, -4f, 128f, 42f, 19f, () => ShowPage(p));
                b.Selected = p == _page;
                _pageButtons.Add(b);
            }

            if (_page != Page.Game)   // the game folder is not a setting: Defaults does not touch it
            {
                WowUi.Button(root, "Defaults", 850f, -4f, 200f, 42f, 19f, () =>
                {
                    WowLog.Info("Settings: defaults restored from the room panel.");
                    WowSettings.ResetDefaults();
                    Rebuild();
                });
            }

            const float L = 60f, R = 580f, W = 460f;
            float y = 56f;

            switch (_page)
            {
                case Page.Display:
                    Check(root, "Nameplates over creatures and NPCs", L, y, W, () => WowSettings.ShowNameplates, v => WowSettings.ShowNameplates = v);
                    Check(root, "NPC names always shown", L, y + 50f, W, () => WowSettings.ShowFriendlyNames, v => WowSettings.ShowFriendlyNames = v);
                    Check(root, "Floating combat text", L, y + 100f, W, () => WowSettings.ShowCombatText, v => WowSettings.ShowCombatText = v);
                    Check(root, "Names over other players", L, y + 150f, W, () => WowSettings.ShowPlayerPlates, v => WowSettings.ShowPlayerPlates = v);
                    Check(root, "Quest markers", L, y + 200f, W, () => WowSettings.ShowQuestMarkers, v => WowSettings.ShowQuestMarkers = v);
                    Slider(root, "Screen interface scale (desktop)", R, y, W, 0.75f, 1.5f, 0.05f, () => WowSettings.UiScale, v => WowSettings.UiScale = v, Times);
                    Slider(root, "Arm panel scale (VR)", R, y + 70f, W, 0.75f, 1.5f, 0.05f, () => WowSettings.ArmUiScale, v => WowSettings.ArmUiScale = v, Times);
                    break;
                case Page.Effects:
                    Check(root, "Spell effects", L, y, W, () => WowSettings.SpellEffects, v => WowSettings.SpellEffects = v);
                    Check(root, "Torches, fires and smoke on the world", L, y + 50f, W, () => WowSettings.WorldParticles, v => WowSettings.WorldParticles = v);
                    Choice(root, "Effects quality", new[] { "Low", "Medium", "High" }, R, y, W, () => WowSettings.EffectsQuality, v => WowSettings.EffectsQuality = v);
                    break;
                case Page.Audio:
                    Slider(root, "Master volume", L, y, W, 0f, 1f, 0.05f, () => WowSettings.MasterVolume, v => WowSettings.MasterVolume = v, null);
                    Slider(root, "Sound effects", L, y + 64f, W, 0f, 1f, 0.05f, () => WowSettings.EffectsVolume, v => WowSettings.EffectsVolume = v, null);
                    Slider(root, "Ambience", L, y + 128f, W, 0f, 1f, 0.05f, () => WowSettings.AmbienceVolume, v => WowSettings.AmbienceVolume = v, null);
                    Slider(root, "Voices", L, y + 192f, W, 0f, 1f, 0.05f, () => WowSettings.VoiceVolume, v => WowSettings.VoiceVolume = v, null);
                    Choice(root, "Race voice", new[] { "Male", "Female" }, L, y + 262f, W, () => WowSettings.VoiceGender, v => WowSettings.VoiceGender = v);
                    Check(root, "Music", R, y, W, () => WowSettings.Music, v => WowSettings.Music = v);
                    Slider(root, "Music volume", R, y + 44f, W, 0f, 1f, 0.05f, () => WowSettings.MusicVolume, v => WowSettings.MusicVolume = v, null);
                    Check(root, "Footsteps", R, y + 118f, W, () => WowSettings.Footsteps, v => WowSettings.Footsteps = v);
                    Slider(root, "Footstep volume", R, y + 162f, W, 0f, 1f, 0.05f, () => WowSettings.FootstepVolume, v => WowSettings.FootstepVolume = v, null);
                    break;
                case Page.Gameplay:
                    Choice(root, "Difficulty", new[] { "Easy", "Normal", "Hard" }, L, y, W, () => WowSettings.Difficulty, v => WowSettings.Difficulty = v);
                    Check(root, "Auto loot", L, y + 74f, W, () => WowSettings.AutoLoot, v => WowSettings.AutoLoot = v);
                    Check(root, "Avatar attack and cast animations (desktop)", L, y + 124f, W, () => WowSettings.AvatarCombatAnimations, v => WowSettings.AvatarCombatAnimations = v);
                    Slider(root, "Sword swing speed for a hit (VR)", R, y, W, 0.5f, 4f, 0.1f, () => WowSettings.SwingSpeed, v => WowSettings.SwingSpeed = v, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
                    Check(root, "Controller vibration (VR)", R, y + 74f, W, () => WowSettings.Haptics, v => WowSettings.Haptics = v);
                    break;
                case Page.Online:
                    BuildOnline(root, y);
                    break;
                case Page.Game:
                    BuildGame(root, y);
                    break;
            }

            TextMeshProUGUI note = WowUi.Text(root, "Note", "Saved for your Somnium account on this PC.", 15f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 388f, WowRoom.PanelWidth - 100f, 24f);
            note.enableAutoSizing = true;
            note.fontSizeMin = 11f;
            note.fontSizeMax = 15f;
        }

        /// <summary>Stage A1: the Online page (see the class summary). The key itself is never shown.</summary>
        private void BuildOnline(RectTransform root, float y)
        {
            AutoKeyState s = null;

            try { if (AutoKey != null) s = AutoKey(); }
            catch (Exception e) { WowLog.Error("Settings: reading the automatic account state threw: " + e); }

            if (s == null) s = new AutoKeyState { Why = "Not available yet." };

            WowUi.Text(root, "Online Title", "Automatic WoW account", 24f, WowUi.GlueGold, TextAlignmentOptions.Left, 60f, y, 700f, 32f);
            Line(root, "Server", "Server", string.IsNullOrEmpty(s.Server) ? "not set" : s.Server, y + 44f);
            Line(root, "Account", "Account", s.Name != null ? s.Name + "  (made from your Somnium account)" : s.Why, y + 80f);
            Line(root, "Key", "Key on this computer", s.Name == null ? "-" : s.HasKey ? "saved (hidden)" : "none", y + 116f);

            WowGlueButton forget = WowUi.Button(root, "Forget this computer's key", 60f, y + 162f, 360f, 46f, 20f, () => ConfirmForget(s));
            forget.Interactable = s.Name != null && s.HasKey;

            TextMeshProUGUI about = WowUi.Text(root, "Online About",
                "The key is the hidden password of your automatic WoW account. It is kept only in this world's storage on this computer and never shown. "
                + "After forgetting it, this computer can use the account again only when the server owner unlocks it and you press \"Play online\".",
                16f, WowUi.Grey, TextAlignmentOptions.Left, 450f, y + 150f, 600f, 110f);
            about.enableAutoSizing = true;
            about.fontSizeMin = 11f;
            about.fontSizeMax = 16f;
        }

        /// <summary>The Game page (see the class summary).</summary>
        private void BuildGame(RectTransform root, float y)
        {
            InstallState s = null;

            try { if (Install != null) s = Install(); }
            catch (Exception e) { WowLog.Error("Settings: reading the game folder state threw: " + e); }

            if (s == null) s = new InstallState { Why = "Not available yet." };

            WowUi.Text(root, "Game Title", "Game folder", 24f, WowUi.GlueGold, TextAlignmentOptions.Left, 60f, y, 700f, 32f);
            Line(root, "Folder", "In use", s.Folder ?? "not found", y + 44f);
            Line(root, "Language", "Language", s.Folder == null ? "-" : (s.Locale ?? "?"), y + 80f);
            Line(root, "Found", "Found by", s.Folder == null || string.IsNullOrEmpty(s.Source) ? "-" : s.Source, y + 116f);
            Line(root, "Saved", "Saved choice", s.Saved == null ? "none - the usual places are searched" : s.Saved + (s.SavedProblem != null ? "  - not usable at the last start: " + s.SavedProblem : ""), y + 152f);

            WowGlueButton change = WowUi.Button(root, "Change...", 60f, y + 206f, 260f, 46f, 21f, () =>
            {
                var h = ChangeFolderPressed;
                if (h != null) h();
            });
            change.Interactable = s.CanChange;

            TextMeshProUGUI about = WowUi.Text(root, "Game About",
                (s.CanChange || string.IsNullOrEmpty(s.Why) ? "" : s.Why + "\n")
                + "Pick the folder with Wow.exe (World of Warcraft 3.3.5a) when the game is somewhere else or you have more than one. "
                + "The choice is saved for your Somnium account on this PC and tried first next time; the game files are then read again here in the room.",
                16f, WowUi.Grey, TextAlignmentOptions.Left, 350f, y + 196f, 700f, 120f);
            about.enableAutoSizing = true;
            about.fontSizeMin = 11f;
            about.fontSizeMax = 16f;
        }

        private static void Line(Transform root, string name, string label, string value, float y)
        {
            WowUi.Text(root, name + " Label", label, 19f, WowUi.GlueGold, TextAlignmentOptions.Left, 60f, y, 260f, 30f);
            TextMeshProUGUI v = WowUi.Text(root, name + " Value", value ?? "", 19f, Color.white, TextAlignmentOptions.Left, 330f, y, 720f, 30f);
            v.enableAutoSizing = true;
            v.fontSizeMin = 12f;
            v.fontSizeMax = 19f;
        }

        private void ConfirmForget(AutoKeyState s)
        {
            if (_room == null || _room.Panel == null || s == null || s.Name == null) return;
            CloseDialog();
            WowLog.Info("Settings: forgetting this computer's key asked (automatic account " + WowSaves.Mask(s.Name) + ").");
            RectTransform blocker = WowUi.Place(_room.Panel.transform, "Forget Key Dialog", 0f, 0f, WowRoom.PanelWidth, WowRoom.PanelHeight);
            _dialog = blocker.gameObject;
            var dim = _dialog.AddComponent<UnityEngine.UI.Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true;
            const float W = 700f, H = 270f;
            float x = (WowRoom.PanelWidth - W) / 2f, top = 320f;
            // WoW's dialog background tile is see-through: an opaque plate behind it, or the Online page shows through the question.
            UnityEngine.UI.RawImage plate = WowUi.Picture(blocker, "Plate", null, new Rect(0f, 0f, 1f, 1f), x + 6f, top + 6f, W - 12f, H - 12f);
            plate.color = new Color(0.02f, 0.02f, 0.025f, 1f);
            WowUi.Backdrop(blocker, x, top, W, H, 32f);
            TextMeshProUGUI q = WowUi.Text(blocker, "Question",
                "Forget this computer's key?\nAccount " + s.Name + ", server " + s.Server + ". This computer can then use that WoW account again only after the server owner unlocks it.",
                21f, Color.white, TextAlignmentOptions.Center, x + 40f, top + 34f, W - 80f, 120f);
            q.enableAutoSizing = true;
            q.fontSizeMin = 13f;
            q.fontSizeMax = 21f;
            WowUi.Button(blocker, "Forget", x + 80f, top + 176f, 220f, 48f, 22f, () =>
            {
                CloseDialog();
                var h = ForgetKeyConfirmed;
                if (h != null) h();
                Rebuild();
            });
            WowUi.Button(blocker, "Cancel", x + W - 300f, top + 176f, 220f, 48f, 22f, () => { WowLog.Info("Settings: forgetting the key cancelled."); CloseDialog(); });
        }

        private void CloseDialog()
        {
            if (_dialog != null)
            {
                UnityEngine.Object.Destroy(_dialog);
                _dialog = null;
            }
        }

        private void ShowPage(Page p)
        {
            if (p == _page) return;
            _page = p;
            WowLog.Info("Settings: page " + p + " (room panel).");
            Rebuild();
        }

        private static string Times(float v) { return "x" + v.ToString("0.00", CultureInfo.InvariantCulture); }

        private void Apply(string what, string value)
        {
            _setting = true;
            try { WowSettings.NotifyChanged(); }
            finally { _setting = false; }
            WowLog.Info("Settings: " + what + " = " + value + " (room panel).");
        }

        private void Check(Transform root, string label, float x, float y, float w, Func<bool> get, Action<bool> set)
        {
            WowCheckBox c = WowCheckBox.Create(root, label, x, y, w, 38f, get(), v => { set(v); Apply(label, v ? "on" : "off"); });
            _refreshers.Add(() => c.Value = get());
        }

        private void Slider(Transform root, string label, float x, float y, float w, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> format)
        {
            WowSlider s = WowSlider.Create(root, label, x, y, w, min, max, step, get(), format, v => { set(v); Apply(label, v.ToString("0.##", CultureInfo.InvariantCulture)); });
            _refreshers.Add(() => s.SetValue(get(), false));
        }

        private void Choice(Transform root, string label, string[] options, float x, float y, float w, Func<int> get, Action<int> set)
        {
            var c = new WowChoice(root, label, options, x, y, w, get(), v => { set(v); Apply(label, v >= 0 && v < options.Length ? options[v] : v.ToString(CultureInfo.InvariantCulture)); });
            _refreshers.Add(() => c.Value = get());
        }
    }
}
