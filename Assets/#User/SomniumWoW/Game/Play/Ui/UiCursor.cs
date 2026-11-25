// iteration 11 - owner uicore (proposal-uicore.md 5.1): ONE owner of the desktop cursor.
// - Free(owner, on): a reference set (windows, the tablet map, UiGroup's menu, spellfx's ground targeting). The first Free remembers the
//   lock state; the last release restores it, or - the Somnium client never locks the cursor (NonXRInteractionControl is on no prefab,
//   proposal 3.1) - shows / hides it as the client's camera wants (ClientWantsCursor), which fixes the stale "hidden" restore.
// - The first-person catcher (5.1.3): the client hides the cursor and mouse-looks in first person (FirstPersonCameraState.cs:9-16); a
//   full-screen transparent raycast target on its own overlay canvas (sorting order 4, below the HUD's 5) makes the client's
//   CheckIfMouseOnUI switch to its UI camera state (cursor visible, look + zoom blocked, UICameraState.cs:8-14) while a window / popup is
//   open, something is on the cursor or Alt is held (ours). WowEntityViews.PointerOverUi ignores it (IsCatcher) - world clicks still target.
// - WoW's cursor art (5.1.4): Cursor.SetCursor with the install's Interface\CURSOR\*.blp (hotspot (0,0), measured by probe UicCursors).
//   The hovered entity picks the kind (Attack over a living hostile unit, ...): that mapping is OURS (gap G5 - WoW decides in its C code).
// - World-click modes (5.1.6): the active IUiCursorMode (UiPickup while something is held, spellfx's ground targeting) owns the next
//   plain world click; WowEntityViews.OnEntityClicked / ClearTarget / DesktopClickFallback return while TakesWorldClicks (hook H5).
// - The client's camera state is read by reflection only (NonXRCameraFeature._currentCameraState.Type, IsFirst/SecondCameraActionOn -
//   CS Player/Scripts/Components/Features/NonXR/NonXRCameraFeature.cs:63, :672-674); nothing is written to client objects.
using System;
using System.Collections.Generic;
using System.Reflection;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>A world-click mode of the cursor (UiPickup holding something, spellfx's ground targeting): it owns the next plain world click.</summary>
    public interface IUiCursorMode
    {
        string Name { get; }
        /// <summary>A plain click on the world (not on any UI): button 0 left / 1 right; hovered = the entity under the cursor (0 none).
        /// VR: the trigger released with the ray on no UI surface (button 0). Return true = consumed.</summary>
        bool WorldClick(int button, ulong hovered);
        /// <summary>Escape, a new mode replacing it, session end, VR/desktop switch.</summary>
        void Cancel(string why);
    }

    /// <summary>WoW's cursor art names (Interface\CURSOR\*.blp; the entity -> kind mapping is ours, proposal-uicore.md 5.1.4).</summary>
    public enum UiCursorKind { Point, Attack, Speak, Interact, Buy, Repair, Pickup, Cast, Taxi, Trainer, Mail, Inspect, Quest, QuestTurnIn, Innkeeper, Directions, GatherHerbs, Mine, LootAll, UnablePoint }

    /// <summary>One owner of the desktop cursor (proposal-uicore.md 5.1).</summary>
    public static class UiCursor
    {
        // ------------------------------------------------------------------ Free / restore (5.1.1)

        private static readonly HashSet<object> FreeBy = new HashSet<object>();
        private static bool _freed, _savedVisible;
        private static CursorLockMode _savedLock;
        private static int _freeLogs;

        /// <summary>A module wants the cursor shown and free (a window, the tablet map, a menu). Reference-set by owner.</summary>
        public static void Free(object owner, bool on)
        {
            if (owner == null) return;
            bool changed = on ? FreeBy.Add(owner) : FreeBy.Remove(owner);
            if (!changed) return;
            if (on && _freeLogs < 10)
            {
                _freeLogs++;
                WowLog.Info("UI cursor: freed by " + Describe(owner) + " (" + FreeBy.Count + " holding it).");
            }
            Apply();
        }

        public static bool IsFree { get { return FreeBy.Count > 0; } }

        private static string Describe(object owner)
        {
            var t = owner as Type;
            if (t != null) return t.Name;
            var s = owner as string;
            return s ?? owner.GetType().Name;
        }

        private static void Apply()
        {
            bool want = FreeBy.Count > 0;
            if (want == _freed) return;
            if (want)
            {
                _savedLock = Cursor.lockState;
                _savedVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _freed = true;
                if (_savedLock != CursorLockMode.None || !_savedVisible)
                    WowLog.Info("UI cursor: shown and free (was " + _savedLock + (_savedVisible ? "" : ", hidden") + ").");
            }
            else
            {
                _freed = false;
                if (_savedLock == CursorLockMode.Locked)
                {
                    // An old client that locked it: exactly as it was (iteration 10 behaviour).
                    Cursor.lockState = _savedLock;
                    Cursor.visible = _savedVisible;
                }
                else Cursor.visible = ClientWantsCursor();
            }
        }

        /// <summary>What the client would show now (5.1.1): UI / BLOCKED / third person -> visible unless a camera button (mouse 1 / 2) is
        /// held; first person -> hidden; unreadable -> ThirdPersonCameraState.cs:24's rule on the mouse buttons.</summary>
        public static bool ClientWantsCursor()
        {
            bool held = CameraButtonHeld();
            int st = ClientCameraState;
            if (st == StateFirstPerson) return false;
            return !held;
        }

        // ------------------------------------------------------------------ the client's camera state (5.1.5, read-only reflection)

        public const int StateUnknown = -1, StateNone = 0, StateFirstPerson = 1, StateThirdPerson = 2, StateBlocked = 3, StateUi = 4;

        private static MonoBehaviour _camFeature;
        private static FieldInfo _stateField;
        private static PropertyInfo _typeProp, _firstOn, _secondOn;
        private static float _nextSearch;
        private static int _searches;
        private static bool _loggedCam, _camUnreadable;
        private static int _lastDefault = StateUnknown;

        /// <summary>The client's NonXRCameraFeature camera state (CameraStateType: 0 NONE, 1 FIRST_PERSON_MODE, 2 THIRD_PERSON_MODE,
        /// 3 BLOCKED, 4 UI); -1 = not readable.</summary>
        public static int ClientCameraState
        {
            get
            {
                if (!FindCameraFeature()) return StateUnknown;
                try
                {
                    object st = _stateField.GetValue(_camFeature);
                    if (st == null) return StateNone;
                    object v = _typeProp.GetValue(st, null);
                    return v != null ? Convert.ToInt32(v) : StateUnknown;
                }
                catch (Exception e)
                {
                    Unreadable("reading the state threw " + e.GetType().Name);
                    return StateUnknown;
                }
            }
        }

        /// <summary>The last camera state that was first or third person (the client's UI state hides which one is the default).</summary>
        public static int DefaultCameraState { get { return _lastDefault; } }

        private static bool CameraButtonHeld()
        {
            if (FindCameraFeature() && _firstOn != null && _secondOn != null)
            {
                try
                {
                    return (bool)_firstOn.GetValue(_camFeature, null) || (bool)_secondOn.GetValue(_camFeature, null);
                }
                catch (Exception) { }
            }
            try { return Input.GetMouseButton(1) || Input.GetMouseButton(2); }
            catch (Exception) { return false; }
        }

        private static bool FindCameraFeature()
        {
            if (_camUnreadable) return false;
            if (_camFeature != null && _stateField != null && _typeProp != null) return true;
            if (_camFeature == null && Time.unscaledTime < _nextSearch) return false;
            _nextSearch = Time.unscaledTime + 1f;
            try
            {
                MonoBehaviour found = null;
                Transform root = WowPlayer.Root;
                if (root != null)
                {
                    MonoBehaviour[] near = root.root.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < near.Length && found == null; i++) if (near[i] != null && near[i].GetType().Name == "NonXRCameraFeature") found = near[i];
                }
                if (found == null)
                {
                    MonoBehaviour[] all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    for (int i = 0; i < all.Length && found == null; i++) if (all[i] != null && all[i].GetType().Name == "NonXRCameraFeature") found = all[i];
                }
                if (found == null)
                {
                    if (++_searches >= 30) Unreadable("no NonXRCameraFeature found in 30 searches (VR, or the client changed)");
                    return false;
                }
                Type t = found.GetType();
                FieldInfo f = null;
                for (Type k = t; k != null && f == null; k = k.BaseType) f = k.GetField("_currentCameraState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                // duelsnd (2026-10-04): the user's client has no field of that name (Player.log: "no field _currentCameraState on
                // SomniumSpace.Player.Components.NonXRCameraFeature") - so the first-person catcher only followed Alt and a popup
                // (DUEL_REQUESTED) gave no steady cursor. Found by its type instead: the one non-array field whose type has a public
                // enum property "Type" with a FIRST_PERSON_MODE value (CS NonXRCameraFeature.cs:62-63 ICameraState _currentCameraState,
                // ICameraState.cs:5 CameraStateType Type, CameraStateType.cs:5-9 NONE, FIRST_PERSON_MODE, THIRD_PERSON_MODE, BLOCKED, UI).
                if (f == null) f = StateFieldByType(t);
                if (f == null) { Unreadable("no field _currentCameraState on " + t.FullName + ", and no single field of a camera-state type"); return false; }
                PropertyInfo type = f.FieldType.GetProperty("Type", BindingFlags.Instance | BindingFlags.Public);
                if (type == null) { Unreadable("no property Type on " + f.FieldType.FullName); return false; }
                _camFeature = found;
                _stateField = f;
                _typeProp = type;
                _firstOn = t.GetProperty("IsFirstCameraActionOn", BindingFlags.Instance | BindingFlags.Public);
                _secondOn = t.GetProperty("IsSecondCameraActionOn", BindingFlags.Instance | BindingFlags.Public);
                if (!_loggedCam)
                {
                    _loggedCam = true;
                    object st = _stateField.GetValue(_camFeature);
                    object v = st != null ? _typeProp.GetValue(st, null) : null;
                    WowLog.Info("UI cursor: client camera state readable (" + t.Name + ", now " + (v != null ? v.ToString() : "none") + ").");
                }
                return true;
            }
            catch (Exception e)
            {
                Unreadable("the search threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>duelsnd: the camera-state field found by its type (any name); null when none or more than one qualify.</summary>
        private static FieldInfo StateFieldByType(Type t)
        {
            FieldInfo hit = null;
            int hits = 0;
            for (Type k = t; k != null; k = k.BaseType)
            {
                FieldInfo[] fs = k.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fs.Length; i++)
                {
                    Type ft = fs[i].FieldType;
                    if (ft.IsArray || ft.IsValueType) continue;
                    PropertyInfo p = ft.GetProperty("Type", BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanRead || !p.PropertyType.IsEnum) continue;
                    if (Array.IndexOf(Enum.GetNames(p.PropertyType), "FIRST_PERSON_MODE") < 0) continue;
                    hit = fs[i];
                    hits++;
                }
            }
            if (hits == 1) WowLog.Info("UI cursor: the client's camera state is field '" + hit.Name + "' (" + hit.FieldType.Name + ") - found by its type.");
            return hits == 1 ? hit : null;
        }

        /// <summary>duelsnd: one line for the popup log - is the desktop cursor free, the catcher on, the client's camera state.</summary>
        public static string Describe()
        {
            int st = _camUnreadable ? StateUnknown : ClientCameraState;
            string cam = st == StateFirstPerson ? "first person" : st == StateThirdPerson ? "third person" : st == StateUi ? "UI" : st == StateBlocked ? "blocked" : st == StateNone ? "none" : "unreadable";
            return "cursor " + (IsFree ? "freed" : "not freed") + ", catcher " + (_catcher != null && _catcher.activeSelf ? "on" : "off") + ", client camera " + cam
                   + (_lastDefault == StateFirstPerson ? " (default first person)" : _lastDefault == StateThirdPerson ? " (default third person)" : "");
        }

        private static void Unreadable(string why)
        {
            _camUnreadable = true;
            _camFeature = null;
            WowLog.Info("UI cursor: client camera state not readable (" + why + ") - using the mouse button rule; the first-person cursor follows Alt and the popups only.");
        }

        // ------------------------------------------------------------------ world-click modes (5.1.6)

        private static IUiCursorMode _mode;

        /// <summary>The active world-click mode, null = none.</summary>
        public static IUiCursorMode Mode { get { return _mode; } }
        public static bool TakesWorldClicks { get { return _mode != null; } }

        /// <summary>Starts a mode (cancels the previous one, "replaced").</summary>
        public static void Begin(IUiCursorMode m)
        {
            if (m == null || ReferenceEquals(m, _mode)) return;
            IUiCursorMode old = _mode;
            _mode = m;
            if (old != null)
            {
                try { old.Cancel("replaced"); }
                catch (Exception e) { WowLog.Warn("UI cursor: mode " + old.Name + " Cancel failed: " + e.Message); }
            }
        }

        /// <summary>Ends a mode (only if it is the current one).</summary>
        public static void End(IUiCursorMode m)
        {
            if (m != null && ReferenceEquals(m, _mode)) _mode = null;
        }

        /// <summary>Cancels the active mode (Escape step 6, session end, VR / desktop switch). True when there was one.</summary>
        public static bool CancelMode(string why)
        {
            IUiCursorMode m = _mode;
            if (m == null) return false;
            _mode = null;
            try { m.Cancel(why); }
            catch (Exception e) { WowLog.Warn("UI cursor: mode " + m.Name + " Cancel failed: " + e.Message); }
            return true;
        }

        private static int _clickLogs;

        /// <summary>A plain world click for the active mode (desktop: UiCursor's own tick; VR: UiPointer on a release off every surface).</summary>
        public static void WorldClick(int button, ulong hovered)
        {
            IUiCursorMode m = _mode;
            if (m == null) return;
            bool used = false;
            try { used = m.WorldClick(button, hovered); }
            catch (Exception e) { WowLog.Error("UI cursor: " + m.Name + " world click failed: " + e); }
            if (_clickLogs < 20 || _clickLogs % 50 == 0)
                WowLog.Info("UI cursor: world click " + (button == 0 ? "left" : "right") + " -> " + m.Name + (used ? "" : " (not used)") + (hovered != 0 ? " on 0x" + hovered.ToString("X") : "") + ".");
            _clickLogs++;
        }

        // ------------------------------------------------------------------ WoW cursor art (5.1.4)

        private static readonly Dictionary<string, Texture2D> Art = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static UiCursorKind _kind = UiCursorKind.Point;
        private static bool _artOn, _artFailedLogged;
        private static string _artApplied;
        // Lead fix (2026-10-03, the user: "wrong icon for vendors", "wrong icons for when gathering ore", "No icon for selling"): the art is
        // chosen by WoW's cursor NAME (the creature / gameobject IconName the server sends, the gathering lock, the sell cursor of a bag item),
        // not only by our kinds. Null = the name of _kind.
        private static string _name;
        private static UiButton _uiHover;
        private static int _nameLogs;

        /// <summary>The desktop mouse entered / left one of our buttons (UiButton's pointer events): its HoverCursor may name the cursor.</summary>
        public static void NoteUiHover(UiButton b, bool on)
        {
            if (on) _uiHover = b;
            else if (_uiHover == b) _uiHover = null;
        }

        /// <summary>WoW's cursor art for what is hovered (setting "WoW mouse cursor"); applied by Tick on the desktop.</summary>
        public static void SetKind(UiCursorKind k) { _kind = k; }

        private static Texture2D CursorTexture(UiCursorKind k) { return CursorTexture(k.ToString()); }

        private static Texture2D CursorTexture(string k)
        {
            Texture2D t;
            if (string.IsNullOrEmpty(k)) return null;
            if (Art.TryGetValue(k, out t)) return t;
            t = null;
            WowFiles files = UiArt.Files;
            if (files == null) return null;
            string path = @"Interface\CURSOR\" + k + ".blp";
            try
            {
                byte[] blp = files.Read(path);
                if (blp != null)
                {
                    BlpImage img = BlpImage.Decode(blp);
                    byte[] px = img.GetRgba32(0);
                    int w = img.Width, h = img.Height;
                    if (w > 0 && h > 0 && px != null && px.Length >= w * h * 4)
                    {
                        // BLP row 0 is the picture's top; a Texture2D's row 0 is its bottom: flip so the hotspot (0,0) is the top-left tip.
                        var pixels = new Color32[w * h];
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                int s = (y * w + x) * 4;
                                pixels[(h - 1 - y) * w + x] = new Color32(px[s], px[s + 1], px[s + 2], px[s + 3]);
                            }
                        t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "WoW cursor " + k, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
                        t.SetPixels32(pixels);
                        t.Apply(false, false);   // stays readable (Cursor.SetCursor needs a readable RGBA32 texture without mips)
                    }
                }
            }
            catch (Exception e)
            {
                if (!_artFailedLogged) { _artFailedLogged = true; WowLog.Warn("UI cursor: cursor art " + path + " unreadable: " + e.Message); }
                t = null;
            }
            Art[k] = t;
            return t;
        }

        private static void ApplyArt(bool want)
        {
            if (!want)
            {
                if (_artOn)
                {
                    _artOn = false;
                    _artApplied = null;
                    try { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); } catch (Exception) { }
                }
                return;
            }
            string artName = _name ?? _kind.ToString();
            if (_artOn && string.Equals(_artApplied, artName, StringComparison.OrdinalIgnoreCase)) return;
            Texture2D t = CursorTexture(artName);
            if (t == null && _name != null)
            {
                if (_nameLogs++ < 10) WowLog.Info("UI cursor: no cursor art '" + artName + "' in the install - " + _kind + " instead.");
                t = CursorTexture(_kind);
            }
            if (t == null && _kind != UiCursorKind.Point) t = CursorTexture(UiCursorKind.Point);
            if (t == null) return;
            try
            {
                Cursor.SetCursor(t, Vector2.zero, CursorMode.Auto);   // hotspot (0,0): every Interface\CURSOR BLP's first opaque pixel (probe UicCursors)
                if (!_artOn) WowLog.Info("UI cursor: WoW cursor art on (" + t.name + ", " + t.width + "x" + t.height + ", from the install).");
                _artOn = true;
                _artApplied = artName;
            }
            catch (Exception e)
            {
                if (!_artFailedLogged) { _artFailedLogged = true; WowLog.Warn("UI cursor: Cursor.SetCursor failed: " + e.Message); }
            }
        }

        /// <summary>Ours (gap G5): the kind for the hovered entity - Attack over a living hostile unit, Pickup over a lootable corpse,
        /// GatherHerbs / Mine / Pickup over a skinnable corpse (by its gathering skill), Interact over an object, Taxi over a flight
        /// master, Speak over other living NPCs; Point otherwise.</summary>
        public static UiCursorKind KindFor(EntityState e, ulong me)
        {
            if (e == null || e.Guid == me) return UiCursorKind.Point;
            if (e.Kind == EntityKind.GameObject) return UiCursorKind.Interact;
            if (e.Dead)
            {
                if (e.Lootable) return UiCursorKind.Pickup;
                if (e.Skinnable) return e.SkinSkill == 182 ? UiCursorKind.GatherHerbs : e.SkinSkill == 186 ? UiCursorKind.Mine : UiCursorKind.Pickup;
                return UiCursorKind.Point;
            }
            if (e.Reaction == Reaction.Hostile) return UiCursorKind.Attack;
            if (e.Kind == EntityKind.Npc || e.Kind == EntityKind.Creature)
            {
                if (e.TaxiStatus >= 0) return UiCursorKind.Taxi;
                if (e.Kind == EntityKind.Npc) return UiCursorKind.Speak;
            }
            return UiCursorKind.Point;
        }

        /// <summary>
        /// Lead fix (2026-10-03): WoW's cursor name over an entity, null = KindFor's. Units: Attack over hostiles; over friendly NPCs the quest
        /// cursors (Quest / QuestTurnIn, files the 3.3.5 client ships) for a quest to take / hand in, else the creature's own IconName
        /// ("Buy" vendors, "Directions" guards, "Taxi", "Trainer", "Repair"... creature_template), else KindFor's Taxi / Speak. Dead: LootAll
        /// while lootable, else Skin / GatherHerbs / Mine / EngineerSkin by the skinning skill. Gameobjects: the gameobject_template IconName,
        /// else the quest cursors, else the gathering lock (Mine / GatherHerbs / PickLock / Fishing; "Unable..." while the player's skill is
        /// below the lock's - Lock.dbc Skill), a mailbox Mail, else Interact. The Unable... rule and the gameobject defaults are ours (the
        /// client's own choice is in its binary); the names are the install's Interface\CURSOR files.
        /// </summary>
        public static string NameFor(EntityState e, PlayerState p)
        {
            if (e == null || (p != null && e.Guid == p.Guid)) return null;
            if (e.Kind == EntityKind.GameObject)
            {
                if (!string.IsNullOrEmpty(e.CursorName)) return e.CursorName;
                if (e.Marker == QuestMarker.Available) return "Quest";
                if (e.Marker == QuestMarker.Complete) return "QuestTurnIn";
                switch (e.GatherLockType)
                {
                    case 2: return Able(p, 182, e.GatherSkillNeeded) ? "GatherHerbs" : "UnableGatherHerbs";   // LOCKTYPE_HERBALISM, SKILL_HERBALISM
                    case 3: return Able(p, 186, e.GatherSkillNeeded) ? "Mine" : "UnableMine";                 // LOCKTYPE_MINING, SKILL_MINING
                    case 1: return Able(p, 633, e.GatherSkillNeeded) ? "PickLock" : "UnablePickLock";         // LOCKTYPE_PICKLOCK, SKILL_LOCKPICKING
                    case 19: return "Fishing";                                                                // LOCKTYPE_FISHING
                }
                if (e.GoType == 19) return "Mail";                                                             // GAMEOBJECT_TYPE_MAILBOX
                return "Interact";
            }
            if (e.Dead)
            {
                if (e.Lootable) return "LootAll";
                if (e.Skinnable)
                {
                    switch (e.SkinSkill)
                    {
                        case 393: return Able(p, 393, 0) ? "Skin" : "UnableSkin";
                        case 182: return Able(p, 182, 0) ? "GatherHerbs" : "UnableGatherHerbs";
                        case 186: return Able(p, 186, 0) ? "Mine" : "UnableMine";
                        case 202: return Able(p, 202, 0) ? "EngineerSkin" : "UnableEngineerSkin";
                    }
                }
                return null;
            }
            if (e.Reaction == Reaction.Hostile) return "Attack";
            if (e.Kind == EntityKind.Npc || e.Kind == EntityKind.Creature)
            {
                if (e.Marker == QuestMarker.Available) return "Quest";
                if (e.Marker == QuestMarker.Complete) return "QuestTurnIn";
                if (!string.IsNullOrEmpty(e.CursorName)) return e.CursorName;
            }
            return null;
        }

        /// <summary>The player knows the skill (with the value the lock needs when one is given). No skills list (offline) = able.</summary>
        private static bool Able(PlayerState p, int skillId, int need)
        {
            if (p == null || p.Skills == null || p.Skills.Count == 0) return true;
            for (int i = 0; i < p.Skills.Count; i++)
            {
                SkillView sv = p.Skills[i];
                if (sv != null && sv.SkillId == skillId) return sv.Value >= need;
            }
            return false;
        }

        /// <summary>WoW cursor art off, every Free reference dropped, the first-person catcher off, the mode cancelled.</summary>
        public static void Release()
        {
            Release("released");
        }

        public static void Release(string why)
        {
            bool any = _artOn || FreeBy.Count > 0 || (_catcher != null && _catcher.activeSelf) || _mode != null;
            ApplyArt(false);
            FreeBy.Clear();
            Apply();
            SetCatcher(false, why);
            CancelMode(why);
            _kind = UiCursorKind.Point;
            _name = null;
            _uiHover = null;
            if (any) WowLog.Info("UI cursor: released (" + why + ").");
        }

        // ------------------------------------------------------------------ the first-person catcher (5.1.3)

        private static GameObject _catcher;
        private static int _catcherLogs;
        private static string _catcherWhy = "";

        /// <summary>True for uicore's first-person catcher (WowEntityViews.PointerOverUi ignores it).</summary>
        public static bool IsCatcher(GameObject go)
        {
            return go != null && _catcher != null && (go == _catcher || go.transform.IsChildOf(_catcher.transform));
        }

        private static void EnsureCatcher(Transform parent)
        {
            if (_catcher != null || parent == null) return;
            _catcher = new GameObject("WoW Cursor Catcher (first person)", typeof(RectTransform));
            _catcher.layer = UiKit.Layer;
            _catcher.transform.SetParent(parent, false);
            var canvas = _catcher.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4;   // below the HUD (5): every HUD / window raycast target is hit first
            _catcher.AddComponent<GraphicRaycaster>();
            var img = new GameObject("Catcher", typeof(RectTransform));
            img.layer = UiKit.Layer;
            var r = (RectTransform)img.transform;
            r.SetParent(_catcher.transform, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            var image = img.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            _catcher.SetActive(false);
        }

        private static void SetCatcher(bool on, string why)
        {
            if (_catcher == null || _catcher.activeSelf == on) return;
            _catcher.SetActive(on);
            _catcherLogs++;
            if (_catcherLogs <= 10 || _catcherLogs % 50 == 0)
                WowLog.Info("UI cursor: first-person catcher " + (on ? "on (" + why + ")" : "off") + ".");
        }

        // ------------------------------------------------------------------ per frame (WowArmUi.Update)

        private static bool _downTracked, _downOnUi;
        private static int _downButton = -1;
        private static float _downAt;
        private static Vector2 _downPos;
        private static bool _inputBroken;
        private static ulong _lastHover;

        /// <summary>
        /// Desktop, once a frame from WowArmUi: the catcher, the cursor art and the world-click poll of the active mode.
        /// busy = a window / popup is open or something is on the cursor. VR: everything off (the art and the catcher are desktop only).
        /// </summary>
        public static void Tick(Transform parent, bool vr, bool busy, IWowSession s)
        {
            if (vr || s == null)
            {
                if (_artOn || (_catcher != null && _catcher.activeSelf)) { ApplyArt(false); SetCatcher(false, vr ? "VR" : "no session"); }
                return;
            }
            EnsureCatcher(parent);

            // Which state the client would be in without us (its UI state hides it): first or third person.
            int st = ClientCameraState;
            if (st == StateFirstPerson || st == StateThirdPerson) _lastDefault = st;

            bool alt = false;
            if (!_inputBroken)
            {
                try { alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); }
                catch (Exception) { _inputBroken = true; }
            }
            bool held = CameraButtonHeld();
            bool want = false;
            string why = "";
            if (WowSettings.WowCursorFirstPerson && !held && !UiArtTyping())
            {
                // duelsnd (ours): unreadable - Alt, or a popup / the stack split that waits for a click (StaticPopups need one; a third-person
                // camera cannot be turned by dragging while one is up)
                if (_camUnreadable || st == StateUnknown) { bool ask = UiPopup.Count > 0 || UiStackSplit.IsOpen; want = alt || ask; why = alt ? "Alt held (camera state unreadable)" : "a popup waits for a click (camera state unreadable)"; }
                else if ((st == StateFirstPerson || st == StateUi) && _lastDefault == StateFirstPerson)
                {
                    want = busy || alt;
                    why = alt ? "Alt held" : "a window, popup or held item";
                }
            }
            if (want) _catcherWhy = why;
            SetCatcher(want, _catcherWhy);

            // The art: what is hovered (ours), Point while something is on the cursor or a mode aims.
            ulong hover = WowEntityViews.HoveredGuid;
            string uiName = null;
            if (_mode == null && _uiHover != null && _uiHover.Hovered && _uiHover.isActiveAndEnabled && _uiHover.HoverCursor != null)
            {
                try { uiName = _uiHover.HoverCursor(); } catch (Exception) { uiName = null; }
            }
            if (uiName != null) { _kind = UiCursorKind.Point; _name = uiName; _lastHover = ulong.MaxValue; }
            else if (hover != _lastHover || _mode != null || _name != null || hover != 0)
            {
                // re-judged every frame while an entity is hovered: its quest marker, loot and skill state change under the mouse
                _lastHover = hover;
                _name = null;
                if (_mode != null && _mode.Name == "repair") _kind = UiCursorKind.Repair;   // integrator (npcui request): the merchant's repair cursor art
                else if (_mode != null || hover == 0) _kind = UiCursorKind.Point;
                else
                {
                    EntityState e = null;
                    try { e = s.Find(hover); } catch (Exception) { e = null; }
                    _kind = KindFor(e, s.Player != null ? s.Player.Guid : 0);
                    _name = NameFor(e, s.Player);
                }
            }
            ApplyArt(WowSettings.WowCursorArt);

            PollWorldClicks();
        }

        private static bool UiArtTyping()
        {
            try { return WowArmUi.TypingSomewhere(); }
            catch (Exception) { return false; }
        }

        /// <summary>WoW's plain click (UiPickupRules.PlainClick) on no UI while a mode is active -> Mode.WorldClick(button, hovered).</summary>
        private static void PollWorldClicks()
        {
            if (_inputBroken) return;
            try
            {
                for (int b = 0; b < 2; b++)
                {
                    if (Input.GetMouseButtonDown(b))
                    {
                        _downTracked = true;
                        _downButton = b;
                        _downAt = Time.unscaledTime;
                        _downPos = Input.mousePosition;
                        _downOnUi = WowEntityViews.PointerOverUi();
                    }
                    if (Input.GetMouseButtonUp(b) && _downTracked && _downButton == b)
                    {
                        _downTracked = false;
                        if (_mode == null || _downOnUi) continue;
                        float moved = ((Vector2)Input.mousePosition - _downPos).magnitude;
                        if (!Pure.UiPickupRules.PlainClick(Time.unscaledTime - _downAt, moved)) continue;
                        if (WowEntityViews.PointerOverUi()) continue;
                        WorldClick(b, WowEntityViews.HoveredGuid);
                    }
                }
            }
            catch (Exception e)
            {
                _inputBroken = true;
                WowLog.Warn("UI cursor: legacy mouse input unavailable (" + e.Message + ") - world clicks of the cursor modes are off.");
            }
        }
    }
}
