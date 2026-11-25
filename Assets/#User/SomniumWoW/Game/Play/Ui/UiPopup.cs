// iteration 11 - owner uicore (proposal-uicore.md 5.3): WoW's StaticPopup for everyone (UiPopupSpec / UiPopup) and the stack split
// (UiStackSplit). Facts of the user's install (FrameXML dumped to P/harness/out_d11uicore): STATICPOPUP_NUMDIALOGS 4 (StaticPopup.lua:3);
// StaticPopupTemplate 320 x 72, UI-DialogBox-Background / -Border, tile 32, edge 32, insets 11 (StaticPopup.xml:36-49); text
// GameFontHighlight 290 wide at TOP (0,-16) (:53-62); alert icon 48x48 LEFT (24,0) (:65-74); buttons StaticPopupButtonTemplate 128x21
// (:4-27); width 320 / 440 with button3 / 420 with showAlert or closeButton (StaticPopup_Resize :2908-2916); height 32 + text + 8 + 21
// (+ 8 + 32 edit box | + 16 money | + 22 money input) (+ 64 item frame) (:2922-2935); buttons: 1 BOTTOM (0,16), 2 button1 BOTTOMRIGHT at
// BOTTOM (-6,16) + 13, 3 button1 at (-72,16) + button3 + button2 13 apart (:3182-3222); StaticPopup1 TOP (0,-135), 2..4 under the previous
// (:326-348); StaticPopup_Show refuses a not-whileDead popup while dead (OnCancel, :2949-2954), exclusive hides the other exclusive ones
// (:2963-2965), cancels hides that popup with OnCancel "override" (:2967-2975); StaticPopup_EscapePressed hides the hideOnEscape ones
// with OnCancel "clicked" unless noCancelOnEscape (:3535-3553). Ours: a 5th popup replaces the oldest non-exclusive one (WoW shows none);
// the edit box is a dark box with a gold edge (not WoW's UI-ChatInputBorder slices); VR = one world surface in front of the head and the
// headset keyboard (WowKeyboard) for edit boxes.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>One StaticPopupDialogs entry (StaticPopup.lua) as data.</summary>
    public sealed class UiPopupSpec
    {
        public string Which, Text, Button1, Button2, Button3;
        public Action OnAccept, OnCancel, OnAlt;
        public Func<bool> KeepOpen;
        public float Timeout;
        public bool WhileDead, Exclusive, ShowAlert, CloseButton, HideOnEscape = true, EnterClicksFirstButton;
        public bool HasEditBox;
        public int MaxLetters;
        public string EditText;
        public Func<string, bool> Button1Enabled;
        public Action<string> OnAcceptText;
        public int Money = -1;
        public bool HasMoneyInput;
        public Action<int> OnAcceptMoney;
        public ItemView Item;
        public string Cancels, Sound;
        public Func<float> StartDelay;
        public bool NoCancelOnEscape, NoCancelOnReuse, Multiple;   // NoCancelOnEscape: interact R-U1 (CONFIRM_BATTLEFIELD_ENTRY)
        public object Data;
    }

    /// <summary>Up to 4 stacked popups, desktop + VR (proposal-uicore.md 5.3).</summary>
    public static class UiPopup
    {
        public const int MaxShown = 4;                         // STATICPOPUP_NUMDIALOGS (StaticPopup.lua:3)
        public const float TopOffset = 135f;                    // StaticPopup1 TOP (0,-135) (StaticPopup.xml:326-330)
        public const float ButtonW = 128f, ButtonH = 21f;       // StaticPopupButtonTemplate (StaticPopup.xml:4-7)
        public const float TextW = 290f;                        // the text's width (StaticPopup.xml:53-62)
        public const float VrMetresPerUnit = 0.0014f;           // like the death popup (UiVrLayout)
        public const string Alert = @"Interface\DialogFrame\UI-Dialog-Icon-AlertNew.blp";   // STATICPOPUP_TEXTURE_ALERT (StaticPopup.lua:4)

        private sealed class Shown
        {
            public int Handle;
            public UiPopupSpec Spec;
            public float At;
            public RectTransform Root;
            public float W, H;
            public UiButton B1, B2, B3;
            public TMP_InputField Edit;
            public TextMeshProUGUI Txt;   // duelsnd: the text, for SetText
            public TMP_InputField[] Coins;
            public bool Vr;
        }

        private static readonly List<Shown> List = new List<Shown>();
        private static readonly Dictionary<object, float> Reserved = new Dictionary<object, float>();
        private static int _next = 1;
        private static Transform _parent;
        private static bool _vr;
        private static UiSurface _desk, _world;
        private static RectTransform _deskStack;
        private static Vector3 _placedFrom;
        private static bool _placed;
        private static WowKeyboard _keyboard;
        private static int _logs;
        private static float _uiScale = -1f;

        // ------------------------------------------------------------------ the API

        /// <summary>Shows a popup; returns its handle (0 = not shown: refused while dead (OnCancel run) or no UI yet).</summary>
        public static int Show(UiPopupSpec s)
        {
            if (s == null) return 0;
            IWowSession session = WowPlay.Session;
            PlayerState p = session != null ? session.Player : null;
            if (p != null && p.Dead && !s.WhileDead)
            {
                Log(s.Which + " refused (dead, not whileDead)");
                Run(s.OnCancel, s, "dead");
                return 0;
            }
            if (!string.IsNullOrEmpty(s.Cancels))
                for (int i = List.Count - 1; i >= 0; i--)
                    if (List[i].Spec.Which == s.Cancels) { Shown c = List[i]; Remove(c); Run(c.Spec.OnCancel, c.Spec, "override"); }
            if (s.Exclusive)
                for (int i = List.Count - 1; i >= 0; i--)
                    if (List[i].Spec.Exclusive) { Shown c = List[i]; Remove(c); if (!c.Spec.NoCancelOnReuse) Run(c.Spec.OnCancel, c.Spec, "override"); }
            for (int i = List.Count - 1; i >= 0; i--)
            {
                Shown c = List[i];
                if (c.Spec.Which != s.Which || string.IsNullOrEmpty(s.Which)) continue;
                if (s.Multiple && !Equals(c.Spec.Data, s.Data)) continue;
                Remove(c);
                if (!c.Spec.NoCancelOnReuse && !ReferenceEquals(c.Spec, s)) Run(c.Spec.OnCancel, c.Spec, "override");
            }
            if (List.Count >= MaxShown)
            {
                Shown oldest = null;
                for (int i = 0; i < List.Count && oldest == null; i++) if (!List[i].Spec.Exclusive) oldest = List[i];
                if (oldest == null) oldest = List[0];
                Remove(oldest);
                Log(oldest.Spec.Which + " replaced by " + s.Which + " (4 shown - ours: the newest wins)");
                Run(oldest.Spec.OnCancel, oldest.Spec, "replaced");
            }
            var sh = new Shown { Handle = _next++, Spec = s, At = Time.unscaledTime };
            List.Add(sh);
            if (_parent != null) Build(sh);
            // duelsnd: every StaticPopup frame's OnShow plays "igMainMenuOpen" (StaticPopup.xml:308-310 -> StaticPopup_OnShow,
            // StaticPopup.lua:3427-3428), then StaticPopup_Show plays info.sound (:3276-3277)
            UiSound.Added(OpenSound, "StaticPopup_OnShow, StaticPopup.lua:3428");
            if (!string.IsNullOrEmpty(s.Sound)) UiSound.Named(s.Sound);
            string t = s.Text ?? "";
            Log(s.Which + " shown (\"" + (t.Length > 60 ? t.Substring(0, 60) : t).Replace("\n", " ") + "\") on the " + (_vr ? "VR panel" : "desktop - " + UiCursor.Describe()));
            return sh.Handle;
        }

        public const string OpenSound = "igMainMenuOpen", CloseSound = "igMainMenuClose";   // SoundEntries 850 / 851 (harness DsNames)

        /// <summary>duelsnd: StaticPopup_OnUpdate's live text (StaticPopup.lua:3307-3339 re-formats a timer text in place): the shown popup's
        /// text changes without hiding it - the buttons stay (a re-show rebuilt them every second, so a click could land on a destroyed
        /// button) and no OnHide / OnShow sounds play. False = no such popup.</summary>
        public static bool SetText(int handle, string text)
        {
            for (int i = 0; i < List.Count; i++)
            {
                Shown c = List[i];
                if (c.Handle != handle) continue;
                if (c.Spec.Text == text) return true;
                c.Spec.Text = text;
                if (c.Root == null || c.Txt == null) return true;
                float before = c.Txt.rectTransform.sizeDelta.y;
                float h = Mathf.Max(14f, c.Txt.GetPreferredValues(text ?? "", TextW, 0f).y);
                if (Mathf.Abs(h - before) > 0.5f) Rebuild(c);   // a line more or less: the height and the buttons move
                else c.Txt.text = text ?? "";
                return true;
            }
            return false;
        }

        /// <summary>Hides the popup with that handle (no callbacks).</summary>
        public static void Hide(int handle)
        {
            for (int i = List.Count - 1; i >= 0; i--) if (List[i].Handle == handle) Remove(List[i]);
        }

        /// <summary>Hides every popup of that Which (no callbacks).</summary>
        public static void HideWhich(string which)
        {
            for (int i = List.Count - 1; i >= 0; i--) if (List[i].Spec.Which == which) Remove(List[i]);
        }

        public static bool IsShown(string which)
        {
            for (int i = 0; i < List.Count; i++) if (List[i].Spec.Which == which) return true;
            return false;
        }

        /// <summary>The handle of a shown popup of that Which (and Data when given), 0 = none (StaticPopup_FindVisible).</summary>
        public static int FindVisible(string which, object data = null)
        {
            for (int i = 0; i < List.Count; i++)
                if (List[i].Spec.Which == which && (data == null || Equals(List[i].Spec.Data, data))) return List[i].Handle;
            return 0;
        }

        /// <summary>Hides every shown HideOnEscape popup (OnCancel "clicked" unless NoCancelOnEscape; StaticPopup.lua:3535-3553); true = one was shown.</summary>
        public static bool EscapePressed()
        {
            bool any = false;
            for (int i = List.Count - 1; i >= 0; i--)
            {
                Shown c = List[i];
                if (!c.Spec.HideOnEscape) continue;
                any = true;
                Remove(c);
                Log(c.Spec.Which + " -> escape");
                if (!c.Spec.NoCancelOnEscape) Run(c.Spec.OnCancel, c.Spec, null);
            }
            if (UiStackSplit.IsOpen) { UiStackSplit.Cancel("escape"); any = true; }
            return any;
        }

        public static int Count { get { return List.Count; } }

        /// <summary>Desktop: another module's own popup (UiDeath, UiGroup's invite) takes this height above the stack (0 = none).</summary>
        public static void Reserve(object owner, float height)
        {
            if (owner == null) return;
            if (height <= 0f) Reserved.Remove(owner);
            else Reserved[owner] = height;
        }

        // ------------------------------------------------------------------ per frame (WowArmUi.Update)

        /// <summary>Builds / moves the popups for the mode in use, runs timeouts, KeepOpen and the button-1 rules.</summary>
        public static void Tick(Transform parent, bool vr, IWowSession s)
        {
            if (parent == null) return;
            if (_parent != parent || vr != _vr)
            {
                _parent = parent;
                _vr = vr;
                for (int i = 0; i < List.Count; i++) Rebuild(List[i]);
                UiStackSplit.Rebuild();
            }
            float now = Time.unscaledTime;
            for (int i = List.Count - 1; i >= 0; i--)
            {
                if (i >= List.Count) continue;
                Shown c = List[i];
                if (c.Root == null) Build(c);
                UiPopupSpec sp = c.Spec;
                if (sp.KeepOpen != null)
                {
                    bool keep = true;
                    try { keep = sp.KeepOpen(); }
                    catch (Exception e) { WowLog.Warn("UI popup: " + sp.Which + " KeepOpen failed: " + e.Message); }
                    if (!keep) { Remove(c); Log(sp.Which + " hidden (its condition ended)"); continue; }
                }
                if (sp.Timeout > 0f && now - c.At >= sp.Timeout) { Remove(c); Log(sp.Which + " -> timeout"); Run(sp.OnCancel, sp, "timeout"); continue; }
                if (c.B1 != null)
                {
                    bool on = true;
                    if (sp.StartDelay != null) { float d = 0f; try { d = sp.StartDelay(); } catch (Exception) { } on = now - c.At >= d; }
                    if (on && sp.Button1Enabled != null)
                    {
                        try { on = sp.Button1Enabled(c.Edit != null ? c.Edit.text : ""); }
                        catch (Exception) { on = false; }
                    }
                    c.B1.Interactable = on;
                }
                if (!vr && c.Edit != null && c.Edit.isFocused)
                {
                    try { if (Input.GetKeyDown(KeyCode.Escape)) { EscapePressed(); return; } }
                    catch (Exception) { }
                }
            }
            Layout(vr);
            UiStackSplit.Tick(vr);
        }

        /// <summary>The surface the popups are drawn on now (desktop overlay / VR world surface), built when first needed.</summary>
        internal static UiSurface Surface(bool vr)
        {
            if (_parent == null) return null;
            if (vr)
            {
                if (_world == null)
                {
                    bool over = UiKit.Over;
                    UiKit.Over = false;
                    _world = UiSurface.MakeWorld("WoW Popups", _parent, 440f, 200f, VrMetresPerUnit, false, 102);
                    SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_world);   // 13.7: moves with the player (UiCarry)
                    UiKit.Surface = _world;
                    _world.Finish();
                    UiKit.Surface = null;
                    UiKit.Over = over;
                    _world.SetShown(false);
                    if (!UiVrLayout.ExtraSurfaces.Contains(_world)) UiVrLayout.ExtraSurfaces.Add(_world);
                }
                return _world;
            }
            if (_desk == null)
            {
                _desk = UiSurface.MakeScreen("WoW Popups (desktop)", _parent, 6);   // above the HUD (5) and its windows
                _deskStack = _desk.Anchor("Stack", new Vector2(0.5f, 1f), 440f, 768f, 0f, 0f);
                bool pointable = UiKit.Pointable;
                UiKit.Pointable = true;
                UiKit.Surface = _desk;
                _desk.Finish();
                UiKit.Surface = null;
                UiKit.Pointable = pointable;
            }
            return _desk;
        }

        internal static RectTransform DeskStack { get { return _deskStack; } }

        private static void Layout(bool vr)
        {
            UiSurface s = Surface(vr);
            if (s == null) return;
            bool any = List.Count > 0 || UiStackSplit.OnSurface(vr);
            if (!vr)
            {
                if (_world != null && _world.Shown) _world.SetShown(false);
                if (_desk.Shown != any) _desk.SetShown(any);
                // The same UIParent scale as the HUD (WowSettings.UiScale).
                float scale = Mathf.Clamp(WowSettings.UiScale, 0.75f, 1.5f);
                if (scale != _uiScale)
                {
                    _uiScale = scale;
                    var scaler = _desk.Canvas.GetComponent<CanvasScaler>();
                    if (scaler != null) scaler.referenceResolution = new Vector2(1024f / scale, 768f / scale);
                }
                float y = TopOffset;
                foreach (float r in Reserved.Values) y += r;
                for (int i = 0; i < List.Count; i++)
                {
                    Shown c = List[i];
                    if (c.Root == null || c.Vr) continue;
                    UiKit.Move(c.Root, (440f - c.W) * 0.5f, y);
                    y += c.H;
                }
                return;
            }
            if (_desk != null && _desk.Shown) _desk.SetShown(false);
            if (!any) { if (_world.Shown) _world.SetShown(false); _placed = false; return; }
            float h = 0f, w = 320f;
            for (int i = 0; i < List.Count; i++) { h += List[i].H; w = Mathf.Max(w, List[i].W); }
            float split = UiStackSplit.OnSurface(true) ? UiStackSplit.H + 8f : 0f;
            float total = Mathf.Max(40f, h + split);
            if (Mathf.Abs(_world.Width - 440f) > 0.5f || Mathf.Abs(_world.Height - total) > 0.5f) _world.Resize(440f, total);
            float yy = 0f;
            for (int i = 0; i < List.Count; i++)
            {
                Shown c = List[i];
                if (c.Root == null || !c.Vr) continue;
                UiKit.Move(c.Root, (440f - c.W) * 0.5f, yy);
                yy += c.H;
            }
            UiStackSplit.PlaceVr(yy + (List.Count > 0 ? 8f : 0f));
            if (!_world.Shown) _world.SetShown(true);
            Camera head = WowPlayer.Head();
            if (head == null) return;
            Vector3 hp = head.transform.position;
            if (_placed && (hp - _placedFrom).sqrMagnitude < 1.3f * 1.3f) return;
            _placed = true;
            _placedFrom = hp;
            Vector3 f = head.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            _world.Root.SetPositionAndRotation(hp + f * 1.1f - Vector3.up * 0.1f, Quaternion.LookRotation(f, Vector3.up));
        }

        // ------------------------------------------------------------------ building

        private static void Rebuild(Shown c)
        {
            if (c.Root != null) DestroyVisual(c);
            Build(c);
        }

        private static void Build(Shown c)
        {
            UiSurface s = Surface(_vr);
            if (s == null) return;
            UiPopupSpec sp = c.Spec;
            c.Vr = _vr;
            int buttons = (sp.Button1 != null ? 1 : 0) + (sp.Button2 != null ? 1 : 0) + (sp.Button3 != null ? 1 : 0);
            float w = sp.Button3 != null ? 440f : (sp.ShowAlert || sp.CloseButton) ? 420f : 320f;
            Transform container = _vr ? (Transform)s.Content : _deskStack;
            bool pointable = UiKit.Pointable, over = UiKit.Over;
            UiSurface was = UiKit.Surface;
            UiKit.Pointable = !_vr;
            UiKit.Over = false;
            UiKit.Surface = s;
            try
            {
                RectTransform root = UiKit.Rect(container, "StaticPopup " + (sp.Which ?? "?"), 0f, 0f, w, 72f);
                c.Root = root;
                UiKit.Blocker(root);
                UiBackdrop back = UiBackdrop.Make(root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
                TextMeshProUGUI text = UiKit.Text(root, "Text", sp.Text ?? "", 13f, Color.white, TextAlignmentOptions.Top, (w - TextW) * 0.5f, 16f, TextW, 20f);
                c.Txt = text;
                float textH = Mathf.Max(14f, text.GetPreferredValues(sp.Text ?? "", TextW, 0f).y);
                UiKit.Size(text.rectTransform, TextW, textH);
                float h = 32f + textH + 8f + (buttons > 0 ? ButtonH : 0f);
                float y = 16f + textH;
                if (sp.Money >= 0)
                {
                    var money = new UiMoney(root, (w + 132f) * 0.5f, y + 5f, Color.white);
                    money.Set(sp.Money);
                    h += 16f;
                    y += 21f;
                }
                if (sp.HasMoneyInput)
                {
                    c.Coins = new TMP_InputField[3];
                    Texture2D coins = UiArt.Tex(UiArt.MoneyIcons);
                    float x0 = (w - 170f) * 0.5f;
                    float[] widths = { 70f, 34f, 34f };
                    float cx = x0;
                    for (int k = 0; k < 3; k++)
                    {
                        c.Coins[k] = Field(root, "Coins " + k, cx, y + 8f, widths[k], 20f, k == 0 ? 7 : 2, true);
                        UiKit.Pic(root, "Coin " + k, coins, k * 0.25f, k * 0.25f + 0.25f, 0f, 1f, cx + widths[k] + 2f, y + 12f, 13f, 13f);
                        cx += widths[k] + 20f;
                    }
                    h += 22f;
                    y += 30f;
                }
                if (sp.Item != null)
                {
                    float ix = (w - 180f) * 0.5f;
                    UiButton slot = UiKit.Slot(root, "Item", ix, y + 14f, 37f, false);
                    Texture2D icon = UiArt.Icon(sp.Item.IconPath);
                    if (icon != null) { slot.Icon.texture = icon; slot.Icon.color = Color.white; }
                    ItemView iv = sp.Item;
                    slot.Tip = () => iv.Tooltip;
                    UiKit.Pic(root, "Name Frame", UiArt.Tex(UiArt.QuestItemNameFrame), ix + 30f, y + 3f, 140f, 62f);
                    TextMeshProUGUI n = UiKit.Text(root, "Item Name", iv.Name ?? "", 12f, UiArt.QualityColor(iv.Quality), TextAlignmentOptions.MidlineLeft, ix + 45f, y + 14f, 103f, 38f);
                    n.textWrappingMode = TextWrappingModes.Normal;
                    h += 64f;
                    y += 64f;
                }
                if (sp.HasEditBox)
                {
                    h += 8f + 32f;
                    c.Edit = Field(root, "EditBox", (w - 130f) * 0.5f, h - 45f - 32f + 6f, 130f, 20f, sp.MaxLetters > 0 ? sp.MaxLetters : 32, false);
                    if (!string.IsNullOrEmpty(sp.EditText)) c.Edit.SetTextWithoutNotify(sp.EditText);
                    Shown me = c;
                    c.Edit.onSubmit.AddListener(t => EnterPressed(me));
                }
                if (sp.ShowAlert)
                {
                    RawImage a = UiKit.Pic(root, "Alert", UiArt.Tex(Alert), 24f, (h - 48f) * 0.5f, 48f, 48f);
                    a.raycastTarget = false;
                }
                if (sp.CloseButton) UiKit.CloseButton(root, w - 35f, 3f, () => Click(c, 2));
                float by = h - 16f - ButtonH;
                if (buttons == 1)
                {
                    string label = sp.Button1 ?? sp.Button2 ?? sp.Button3;
                    int which = sp.Button1 != null ? 1 : sp.Button2 != null ? 2 : 3;
                    UiButton b = UiKit.PopupButton(root, label, (w - ButtonW) * 0.5f, by, () => Click(c, which));
                    if (which == 1) c.B1 = b; else if (which == 2) c.B2 = b; else c.B3 = b;
                }
                else if (buttons == 2)
                {
                    string l1 = sp.Button1 ?? sp.Button3, l2 = sp.Button2 ?? sp.Button3;
                    int w1 = sp.Button1 != null ? 1 : 3, w2 = sp.Button2 != null ? 2 : 3;
                    UiButton b1 = UiKit.PopupButton(root, l1, w * 0.5f - 6f - ButtonW, by, () => Click(c, w1));
                    UiButton b2 = UiKit.PopupButton(root, l2, w * 0.5f + 7f, by, () => Click(c, w2));
                    if (w1 == 1) c.B1 = b1; else c.B3 = b1;
                    if (w2 == 2) c.B2 = b2; else c.B3 = b2;
                }
                else if (buttons == 3)
                {
                    c.B1 = UiKit.PopupButton(root, sp.Button1, w * 0.5f - 72f - ButtonW, by, () => Click(c, 1));
                    c.B3 = UiKit.PopupButton(root, sp.Button3, w * 0.5f - 59f, by, () => Click(c, 3));
                    c.B2 = UiKit.PopupButton(root, sp.Button2, w * 0.5f + 82f, by, () => Click(c, 2));
                }
                back.Place(0f, 0f, w, h);
                UiKit.Size(root, w, h);
                c.W = w;
                c.H = h;
                s.KeepOnTop();
            }
            finally
            {
                UiKit.Pointable = pointable;
                UiKit.Over = over;
                UiKit.Surface = was;
            }
            if (c.Edit != null)
            {
                if (_vr) OpenKeyboard(c);
                else
                {
                    try
                    {
                        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(c.Edit.gameObject);
                        c.Edit.ActivateInputField();
                    }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>A single-line TMP field (UiChat's MakeField pattern) in a dark box with a gold edge (ours).</summary>
        internal static TMP_InputField Field(Transform parent, string name, float x, float y, float w, float h, int limit, bool digits)
        {
            UiKit.Solid(parent, name + " Box", new Color(0f, 0f, 0f, 0.75f), x - 4f, y - 3f, w + 8f, h + 6f);
            RawImage edge = UiKit.Solid(parent, name + " Edge", new Color(0.55f, 0.45f, 0.2f, 1f), x - 5f, y + h + 2f, w + 10f, 1f);
            edge.raycastTarget = false;
            RectTransform r = UiKit.Rect(parent, name, x, y, w, h);
            GameObject go = r.gameObject;
            go.SetActive(false);
            var catcher = go.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            RectTransform area = UiKit.Rect(r, "Text Area", 0f, 0f, w, h);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = UiKit.Text(area, "Text", "", 13f, Color.white, TextAlignmentOptions.MidlineLeft, 0f, 0f, w, h, null, false);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var f = go.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.targetGraphic = catcher;
            if (UiArt.Friz != null) f.fontAsset = UiArt.Friz;
            f.pointSize = 13f;
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.characterLimit = limit;
            f.richText = false;
            f.customCaretColor = true;
            f.caretColor = Color.white;
            f.caretWidth = 2;
            f.selectionColor = new Color(0.9f, 0.7f, 0.1f, 0.35f);
            f.onFocusSelectAll = false;
            f.shouldHideSoftKeyboard = true;
            f.restoreOriginalTextOnEscape = false;
            f.contentType = digits ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
            go.SetActive(true);
            return f;
        }

        private static void OpenKeyboard(Shown c)
        {
            if (c.Edit == null || _parent == null) return;
            try
            {
                if (_keyboard == null) _keyboard = WowKeyboard.Create(_parent);
                Shown me = c;
                _keyboard.Open(c.Edit, c.Spec.Which ?? "Popup", false, () => EnterPressed(me));
            }
            catch (Exception e) { WowLog.Warn("UI popup: the headset keyboard failed: " + e.Message); }
        }

        private static void EnterPressed(Shown c)
        {
            if (!List.Contains(c)) return;
            if (c.B1 != null && c.B1.Interactable && (c.Spec.EnterClicksFirstButton || c.Spec.HasEditBox)) Click(c, 1);
        }

        private static void Click(Shown c, int which)
        {
            if (!List.Contains(c)) return;
            if (which == 1 && c.B1 != null && !c.B1.Interactable) return;
            UiPopupSpec sp = c.Spec;
            string text = c.Edit != null ? c.Edit.text : null;
            int copper = -1;
            if (c.Coins != null)
            {
                int g, s, k;
                int.TryParse(c.Coins[0].text, out g);
                int.TryParse(c.Coins[1].text, out s);
                int.TryParse(c.Coins[2].text, out k);
                copper = Mathf.Max(0, g) * 10000 + Mathf.Clamp(s, 0, 99) * 100 + Mathf.Clamp(k, 0, 99);
            }
            Remove(c);
            Log(sp.Which + " -> " + (which == 1 ? "accept" : which == 2 ? "cancel" : "alt"));
            if (which == 1)
            {
                if (sp.OnAcceptText != null) { try { sp.OnAcceptText(text ?? ""); } catch (Exception e) { WowLog.Error("UI popup: " + sp.Which + " OnAcceptText failed: " + e); } }
                if (sp.OnAcceptMoney != null && copper >= 0) { try { sp.OnAcceptMoney(copper); } catch (Exception e) { WowLog.Error("UI popup: " + sp.Which + " OnAcceptMoney failed: " + e); } }
                if (sp.OnAcceptText == null) Run(sp.OnAccept, sp, null);
            }
            else if (which == 2) Run(sp.OnCancel, sp, null);
            else Run(sp.OnAlt, sp, null);
        }

        private static void Run(Action a, UiPopupSpec sp, string why)
        {
            if (a == null) return;
            if (why != null) Log((sp != null ? sp.Which : "?") + " -> " + why);
            try { a(); }
            catch (Exception e) { WowLog.Error("UI popup: " + (sp != null ? sp.Which : "?") + " callback failed: " + e); }
        }

        private static void Remove(Shown c) { Remove(c, true); }

        /// <summary>closeSound: StaticPopup_OnHide plays "igMainMenuClose" (StaticPopup.lua:3444-3445) whenever a shown popup hides
        /// (answered, timed out, replaced, its condition ended); not at a session reset (ours: the UI goes with the session).</summary>
        private static void Remove(Shown c, bool closeSound)
        {
            bool was = List.Remove(c);
            if (was && closeSound) UiSound.Added(CloseSound, "StaticPopup_OnHide, StaticPopup.lua:3445");
            DestroyVisual(c);
            if (_keyboard != null && c.Edit != null && _keyboard.IsOpen && _keyboard.Target == c.Edit) _keyboard.Close();
        }

        private static void DestroyVisual(Shown c)
        {
            if (c.Root != null) UnityEngine.Object.Destroy(c.Root.gameObject);
            c.Root = null;
            c.B1 = c.B2 = c.B3 = null;
            c.Edit = null;
            c.Txt = null;
            c.Coins = null;
            if (_desk != null) _desk.Prune();
            if (_world != null) _world.Prune();
        }

        private static void Log(string what)
        {
            _logs++;
            if (_logs <= 40 || _logs % 50 == 0) WowLog.Info("UI popup: " + what + ".");
        }

        /// <summary>Session end: every popup goes without callbacks (their owners reset with the session).</summary>
        public static void Reset(string why)
        {
            if (List.Count > 0) Log(List.Count + " hidden (" + why + ")");
            for (int i = List.Count - 1; i >= 0; i--) Remove(List[i], false);
            Reserved.Clear();
            UiStackSplit.Cancel(why);
            if (_desk != null) _desk.SetShown(false);
            if (_world != null) _world.SetShown(false);
            _placed = false;
        }
    }

    /// <summary>
    /// WoW's StackSplitFrame (Shift-click a stack, StackSplitFrame.xml): 172x96, Interface\MoneyFrame\UI-MoneyFrame (TexCoords 0, 0.671875,
    /// 0, 0.75), the number RIGHT (-50,18), arrows 16x16 at CENTER (-59,18) / (64,18), Okay / Cancel UIPanelButtonTemplate 64x24 at
    /// BOTTOM (-3,32) / (5,32). Arrows +-1, the mouse wheel +-1 and typed digits (ours); Enter = Okay, Escape = Cancel.
    /// </summary>
    public static class UiStackSplit
    {
        public const float W = 172f, H = 96f;

        private static int _max, _value;
        private static Action<int> _picked;
        private static UiButton _near;
        private static RectTransform _root;
        private static TextMeshProUGUI _text;
        private static UiButton _left, _right;
        private static bool _vr, _typedFresh;

        public static bool IsOpen { get { return _picked != null; } }

        public static void Open(int max, UiButton near, Action<int> picked)
        {
            if (picked == null || max <= 1) return;
            Cancel("replaced");
            _max = max;
            _value = 1;
            _picked = picked;
            _near = near;
            _typedFresh = true;
            Rebuild();
            WowLog.Info("UI popup: StackSplitFrame opened (1 of " + max + ").");
        }

        public static void Cancel(string why)
        {
            if (_picked == null) return;
            _picked = null;
            Destroy();
            WowLog.Info("UI popup: StackSplitFrame closed (" + why + ").");
        }

        internal static bool OnSurface(bool vr) { return _picked != null && _root != null && _vr == vr; }

        internal static void Rebuild()
        {
            Destroy();
            if (_picked == null) return;
            UiSurface s = UiPopup.Surface(WowPlayer.XrPresent);
            if (s == null) return;
            _vr = s.IsWorld;
            bool pointable = UiKit.Pointable, over = UiKit.Over;
            UiSurface was = UiKit.Surface;
            UiKit.Pointable = !_vr;
            UiKit.Over = false;
            UiKit.Surface = s;
            try
            {
                Transform parent = _vr ? (Transform)s.Content : s.Content;
                _root = UiKit.Rect(parent, "StackSplitFrame", 0f, 0f, W, H);
                if (!_vr)
                {
                    _root.anchorMin = _root.anchorMax = new Vector2(0f, 0f);
                    _root.pivot = new Vector2(1f, 0f);
                }
                UiKit.Blocker(_root);
                UiKit.Pic(_root, "Background", UiArt.Tex(@"Interface\MoneyFrame\UI-MoneyFrame.blp"), 0f, 0.671875f, 0f, 0.75f, 0f, 0f, W, H);
                _text = UiKit.Text(_root, "StackSplitText", "1", 14f, Color.white, TextAlignmentOptions.MidlineRight, W - 50f - 60f, 22f, 60f, 16f);
                _left = UiKit.ArtButton(_root, "StackSplitLeftButton", @"Interface\MoneyFrame\Arrow-Left-Up.blp", @"Interface\MoneyFrame\Arrow-Left-Down.blp",
                    @"Interface\MoneyFrame\Arrow-Left-Disabled.blp", null, W * 0.5f - 59f - 8f, 30f - 8f, 16f, () => Step(-1));
                _right = UiKit.ArtButton(_root, "StackSplitRightButton", @"Interface\MoneyFrame\Arrow-Right-Up.blp", @"Interface\MoneyFrame\Arrow-Right-Down.blp",
                    @"Interface\MoneyFrame\Arrow-Right-Disabled.blp", null, W * 0.5f + 64f - 8f, 30f - 8f, 16f, () => Step(1));
                UiKit.PanelButton(_root, UiGlobalStrings.G("OKAY", "Okay"), W * 0.5f - 3f - 64f, H - 32f - 12f, 64f, 24f, Okay);   // GlobalStrings.lua:5234
                UiKit.PanelButton(_root, UiGlobalStrings.G("CANCEL", "Cancel"), W * 0.5f + 5f, H - 32f - 12f, 64f, 24f, () => Cancel("cancel"));   // :1338
                s.KeepOnTop();
            }
            finally
            {
                UiKit.Pointable = pointable;
                UiKit.Over = over;
                UiKit.Surface = was;
            }
            Show();
        }

        private static void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }

        private static void Step(int d)
        {
            _value = Mathf.Clamp(_value + d, 1, _max);
            _typedFresh = false;
            Show();
        }

        private static void Show()
        {
            if (_text != null) _text.SetText("{0}", _value);
            if (_left != null) _left.Interactable = _value > 1;
            if (_right != null) _right.Interactable = _value < _max;
        }

        private static void Okay()
        {
            Action<int> p = _picked;
            int v = _value;
            _picked = null;
            Destroy();
            WowLog.Info("UI popup: StackSplitFrame -> " + v + ".");
            if (p != null)
            {
                try { p(v); }
                catch (Exception e) { WowLog.Error("UI popup: the stack split callback failed: " + e); }
            }
        }

        internal static void PlaceVr(float y)
        {
            if (_root == null || !_vr) return;
            UiKit.Move(_root, (440f - W) * 0.5f, y);
        }

        internal static void Tick(bool vr)
        {
            if (_picked == null) return;
            if (_root == null || _vr != vr) { Rebuild(); if (_root == null) return; }
            if (_near == null || !_near.isActiveAndEnabled) { if (!vr) { Cancel("the slot closed"); return; } }
            if (!vr)
            {
                // BOTTOMRIGHT at the slot's TOPRIGHT, clamped to the screen.
                RectTransform root = (RectTransform)_root.parent;
                if (_near != null && _near.Rt != null)
                {
                    Rect r = _near.Rt.rect;
                    Vector3 tr = root.InverseTransformPoint(_near.Rt.TransformPoint(new Vector3(r.xMax, r.yMax, 0f)));
                    Rect pr = root.rect;
                    float x = Mathf.Clamp(tr.x - pr.xMin, W, pr.width);
                    float y = Mathf.Clamp(tr.y - pr.yMin, 0f, pr.height - H);
                    _root.anchoredPosition = new Vector2(x, y);
                }
                try
                {
                    float wheel = Input.mouseScrollDelta.y;
                    if (wheel > 0.01f) Step(1);
                    else if (wheel < -0.01f) Step(-1);
                    for (int d = 0; d <= 9; d++)
                    {
                        if (!Input.GetKeyDown(KeyCode.Alpha0 + d) && !Input.GetKeyDown(KeyCode.Keypad0 + d)) continue;
                        int nv = _typedFresh ? d : _value * 10 + d;
                        _typedFresh = false;
                        _value = Mathf.Clamp(nv, 1, _max);
                        Show();
                    }
                    if (Input.GetKeyDown(KeyCode.Backspace)) { _value = Mathf.Max(1, _value / 10); Show(); }
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Okay();
                }
                catch (Exception) { }
            }
        }

        /// <summary>True while the split frame takes the digit keys (the action bar keys wait).</summary>
        public static bool TakesKeys { get { return _picked != null && !_vr; } }
    }
}
