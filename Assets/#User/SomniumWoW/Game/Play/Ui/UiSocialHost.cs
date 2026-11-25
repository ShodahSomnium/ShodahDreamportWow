// iteration 11 - owner socialui (proposal-social.md 6.5.6; wave-0 stub by w0game, filled by socialui)
// The always-on social parts (added by WowGame after UiChat, hook H7): the GUILD_INVITE popup, the desktop new-mail icon, a text emote of
// our own on the Somnium avatar (WowGear.PlayAvatarEmote), and the headset keyboard of the social / guild / mail text fields. Also the
// shared helpers of the social windows (SocialUi: GlobalStrings texts with Lua's format rules, text fields, list rows, tabs).
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiSocialHost : MonoBehaviour
    {
        // ---- WoW's (Minimap.xml:102-135 MiniMapMailFrame; ours: where it sits - we have no minimap, proposal-social.md 4.2 / 4.7)
        const float MailFrameSize = 33f, MailIconSize = 18f, MailIconX = 7f, MailIconY = 6f, MailBorderSize = 52f;
        const float MailFrameRight = -180f, MailFrameTop = -10f;   // ours: the desktop canvas TOPRIGHT (-180, -10), left of WoW's minimap cluster
        const string MailIcon = @"Interface\Icons\INV_Letter_15.blp", MailBorder = @"Interface\Minimap\MiniMap-TrackingBorder.blp";
        const float GuildInviteTimeout = 60f;                      // StaticPopupDialogs["GUILD_INVITE"] timeout = 60 (StaticPopup.lua:1329-1342)

        public static UiSocialHost Instance { get; private set; }

        private WowKeyboard _keyboard;

        /// <summary>The headset keyboard of the social / guild / mail text fields (VR). UiPopup opens its own for the popups.</summary>
        public static WowKeyboard Keyboard
        {
            get
            {
                UiSocialHost h = Instance;
                if (h == null) return null;
                if (h._keyboard == null) h._keyboard = WowKeyboard.Create(h.transform);
                return h._keyboard;
            }
        }

        /// <summary>The keyboard if it was ever built (no building just to ask).</summary>
        internal static WowKeyboard KeyboardIfAny { get { UiSocialHost h = Instance; return h != null ? h._keyboard : null; } }

        private UiSurface _desk;
        private RectTransform _mailFrame;
        private CanvasScaler _deskScaler;
        private float _deskScale = 1f;
        private bool _subscribed, _failed, _loggedMail, _loggedEmote;
        private int _invitePopup;

        private void Awake() { Instance = this; }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnEnable()
        {
            if (_subscribed) return;
            WowPlay.Event += OnGameEvent;
            WowPlay.Started += OnStarted;
            WowPlay.Ended += OnEnded;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            WowPlay.Event -= OnGameEvent;
            WowPlay.Started -= OnStarted;
            WowPlay.Ended -= OnEnded;
            _subscribed = false;
        }

        private void OnStarted(IWowSession s)
        {
            _invitePopup = 0;
            WowLog.Info("UI: social host ready (" + (s is OnlineSession ? "online: guild invites, the new-mail icon, text emotes on the avatar" : "offline: text emotes on the avatar") + ").");
        }

        private void OnEnded()
        {
            UiPopup.HideWhich("GUILD_INVITE");
            _invitePopup = 0;
            SocialMenu.Close("the game ended");
            if (_keyboard != null) _keyboard.Close();
            if (_desk != null) _desk.SetShown(false);
        }

        private void OnGameEvent(GameEvent e)
        {
            try
            {
                if (e.Type == GameEventType.GuildInvite) ShowGuildInvite();
                else if (e.Type == GameEventType.TextEmote) AvatarEmote(e);
            }
            catch (Exception ex)
            {
                if (!_failed) { _failed = true; WowLog.Error("UI: social host failed (logged once): " + ex); }
            }
        }

        /// <summary>StaticPopupDialogs["GUILD_INVITE"] (StaticPopup.lua:1329-1342): GUILD_INVITATION, Accept / Decline, timeout 60, whileDead,
        /// hideOnEscape; OnCancel = DeclineGuild (also the timeout and Escape). No sound (neither the dialog nor UIParent.lua:552-555 plays one).</summary>
        private void ShowGuildInvite()
        {
            IWowSession s = WowPlay.Session;
            IWowSocial soc = SocialUi.Soc(s);
            if (soc == null || string.IsNullOrEmpty(soc.State.PendingGuildInviteFrom)) return;
            string from = soc.State.PendingGuildInviteFrom, guild = soc.State.PendingGuildInviteGuild ?? "";
            _invitePopup = UiPopup.Show(new UiPopupSpec
            {
                Which = "GUILD_INVITE",
                Text = SocialUi.F("GUILD_INVITATION", "%s invites you to join the guild: %s", from, guild),   // GlobalStrings.lua:3955
                Button1 = SocialUi.G("ACCEPT", "Accept"), Button2 = SocialUi.G("DECLINE", "Decline"),
                Timeout = GuildInviteTimeout, WhileDead = true, HideOnEscape = true,
                OnAccept = () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.AnswerGuildInvite(true); },
                OnCancel = () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.AnswerGuildInvite(false); },
            });
            WowLog.Info("UI: social - guild invitation popup " + (_invitePopup > 0 ? "shown (60 s)." : "not shown (the popup system refused it)."));
        }

        /// <summary>proposal-social.md H11: a text emote of OUR character -> the Somnium avatar plays its animation (avatar 5.6: desktop only,
        /// false in VR - your body is your own there; loops stop when you move). Other players see it through the avatar sync.</summary>
        private void AvatarEmote(GameEvent e)
        {
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null || e.SourceGuid == 0 || e.SourceGuid != s.Player.Guid || e.Amount <= 0) return;
            bool played = WowGear.PlayAvatarEmote(e.Amount);
            if (!_loggedEmote)
            {
                _loggedEmote = true;
                WowLog.Info("UI: social - text emote on the avatar (animation " + e.Amount + "): " + (played ? "played" : "not played (VR, or the avatar cannot play it)") + " (logged once).");
            }
        }

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            try
            {
                bool want = s is OnlineSession && !WowPlayer.XrPresent && UiArt.Files != null;
                IWowSocial soc = want ? SocialUi.Soc(s) : null;
                bool mail = soc != null && soc.State.HasUnreadMail;
                if (mail) EnsureDesk();
                if (_desk != null)
                {
                    if (_desk.Shown != mail) _desk.SetShown(mail);
                    if (mail) DeskScale();
                }
                if (s == null && _keyboard != null && _keyboard.IsOpen) _keyboard.Close();
                SocialMenu.Tick();   // the FRIEND / channel menus: timeout, click elsewhere, Escape, the host hiding
            }
            catch (Exception ex)
            {
                if (!_failed) { _failed = true; WowLog.Error("UI: social host failed (logged once): " + ex); }
            }
        }

        /// <summary>WoW's MiniMapMailFrame on a screen surface of its own (UiChat's pattern): the letter in the tracking border, the tooltip
        /// HAVE_MAIL_FROM + up to 2 names (MSG_QUERY_NEXT_MAIL_TIME) or HAVE_MAIL (Minimap.xml MiniMapMailFrame OnEnter).</summary>
        private void EnsureDesk()
        {
            if (_desk != null) return;
            bool pointable = UiKit.Pointable, over = UiKit.Over;
            try
            {
                UiKit.Over = false;
                UiKit.Pointable = true;
                _desk = UiSurface.MakeScreen("WoW Social (desktop)", transform, 4);
                UiKit.Surface = _desk;
                _deskScaler = _desk.Canvas.GetComponent<CanvasScaler>();
                _mailFrame = _desk.Anchor("MiniMapMailFrame", new Vector2(1f, 1f), MailFrameSize, MailFrameSize, MailFrameRight, MailFrameTop);
                UiKit.Pic(_mailFrame, "MiniMapMailIcon", UiArt.Tex(MailIcon), MailIconX, MailIconY, MailIconSize, MailIconSize);
                UiKit.Pic(_mailFrame, "MiniMapMailBorder", UiArt.Tex(MailBorder), 0f, 0f, MailBorderSize, MailBorderSize);
                UiButton b = UiKit.Button(_mailFrame, "Mail", 0f, 0f, MailFrameSize, MailFrameSize);
                b.Tip = MailTip;
                _desk.Finish();
                _desk.SetShown(false);
                if (!_loggedMail)
                {
                    _loggedMail = true;
                    WowLog.Info("UI: social - the new-mail icon (WoW's MiniMapMailFrame 33 x 33, INV_Letter_15) at the desktop TOPRIGHT (" + MailFrameRight + ", " + MailFrameTop
                                + ") (ours: no minimap); VR shows the mark on the floating HUD.");
                }
            }
            finally
            {
                UiKit.Surface = null;
                UiKit.Pointable = pointable;
                UiKit.Over = over;
            }
        }

        private void DeskScale()
        {
            float scale = Mathf.Clamp(WowSettings.UiScale, 0.75f, 1.5f);
            if (_deskScaler != null && scale != _deskScale)
            {
                _deskScale = scale;
                _deskScaler.referenceResolution = new Vector2(1024f / scale, 768f / scale);
            }
        }

        private static string MailTip()
        {
            IWowSocial soc = SocialUi.Soc(WowPlay.Session);
            if (soc == null) return null;
            List<string> from = soc.State.UnreadMailFrom;
            if (from == null || from.Count == 0) return SocialUi.G("HAVE_MAIL", "You have unread mail");   // GlobalStrings.lua:4002
            var sb = new StringBuilder(SocialUi.G("HAVE_MAIL_FROM", "Unread mail from:"));               // :4003
            for (int i = 0; i < from.Count && i < 2; i++) sb.Append('\n').Append(SocialUi.NoTags(from[i]));
            return sb.ToString();
        }
    }

    /// <summary>
    /// The shared helpers of the social windows (UiSocial, UiGuildTab, UiPetition, UiGuildRegistrar, UiMail, UiChat): texts from the
    /// install's GlobalStrings (UiGlobalStrings.G with the enUS fallback quoted at the caller) with Lua's format rules, text fields that
    /// type with the mouse + keyboard on the desktop and with the headset keyboard in VR, WoW's tab buttons and list rows.
    /// </summary>
    internal static class SocialUi
    {
        /// <summary>The session's social side, null without a session (IWowSession.Social is never null by contract; guarded anyway).</summary>
        public static IWowSocial Soc(IWowSession s)
        {
            if (s == null) return null;
            try { return s.Social; }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ texts

        /// <summary>A GlobalStrings text of the install (fallback = the enUS text, quoted with its line by the caller), escapes resolved.</summary>
        public static string G(string key, string fallback)
        {
            return Lua(Raw(key, fallback), false, null);
        }

        /// <summary>As G, then Lua's string.format with args (%s %d %n$s %n$d %%, |4one:many; by the last number).</summary>
        public static string F(string key, string fallback, params object[] args)
        {
            return Lua(Raw(key, fallback), false, args);
        }

        /// <summary>As F, keeping |cffRRGGBB ... |r as TMP colour tags (for labels that show rich text).</summary>
        public static string FRich(string key, string fallback, params object[] args)
        {
            return Lua(Raw(key, fallback), true, args);
        }

        private static string Raw(string key, string fallback)
        {
            string s = null;
            try { s = UiGlobalStrings.G(key, fallback); }
            catch (Exception) { s = null; }
            // wave 0's UiGlobalStrings answers the fallback; uicore's reader the install's text. A key echoed back = not found.
            if (string.IsNullOrEmpty(s) || s == key) s = fallback ?? key ?? "";
            return s;
        }

        /// <summary>
        /// Lua's format over a GlobalStrings text: %s / %d in order, %1$s / %2$d by position, %% = %; the client's escapes: |4one:many; picks
        /// by the last number formatted before it (FrameXML's plural form), |cffRRGGBB / |r = colour (rich) or nothing, |Hlink|h / |h link
        /// heads are left out (UiChat strips them the same way), |n = a new line, || = |; a literal "\32" (an unparsed Lua escape) = space.
        /// </summary>
        public static string Lua(string fmt, bool rich, object[] args)
        {
            if (string.IsNullOrEmpty(fmt)) return "";
            if (fmt.IndexOf('%') < 0 && fmt.IndexOf('|') < 0 && fmt.IndexOf('\\') < 0) return fmt;
            var sb = new StringBuilder(fmt.Length + 32);
            int next = 0;
            double last = 0;
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c == '%' && i + 1 < fmt.Length)
                {
                    char d = fmt[i + 1];
                    if (d == '%') { sb.Append('%'); i++; continue; }
                    int j = i + 1, pos = -1;
                    // %n$x: a position
                    int k = j;
                    while (k < fmt.Length && char.IsDigit(fmt[k])) k++;
                    if (k > j && k < fmt.Length && fmt[k] == '$') { pos = int.Parse(fmt.Substring(j, k - j)) - 1; j = k + 1; }
                    // flags / width / precision (Lua accepts them; we keep the plain value)
                    while (j < fmt.Length && (fmt[j] == '-' || fmt[j] == '.' || fmt[j] == '+' || fmt[j] == ' ' || char.IsDigit(fmt[j]))) j++;
                    if (j >= fmt.Length) { sb.Append(c); continue; }
                    char t = fmt[j];
                    if ("sdifgxXcuoeE".IndexOf(t) < 0) { sb.Append(c); continue; }
                    int ai = pos >= 0 ? pos : next++;
                    object a = args != null && ai >= 0 && ai < args.Length ? args[ai] : null;
                    if (a is int || a is long || a is float || a is double || a is uint)
                    {
                        double v = Convert.ToDouble(a);
                        last = v;
                        if (t == 'd' || t == 'i' || t == 'u') sb.Append(((long)v).ToString());
                        else sb.Append(a.ToString());
                    }
                    else sb.Append(a != null ? a.ToString() : "");
                    i = j;
                    continue;
                }
                if (c == '|' && i + 1 < fmt.Length)
                {
                    char d = fmt[i + 1];
                    if (d == '|') { sb.Append('|'); i++; continue; }
                    if (d == 'n') { sb.Append('\n'); i++; continue; }
                    if (d == 'r') { if (rich) sb.Append("</color>"); i++; continue; }
                    if (d == 'h') { i++; continue; }
                    if (d == 'c' && i + 9 < fmt.Length)
                    {
                        if (rich) sb.Append("<color=#").Append(fmt, i + 4, 6).Append('>');
                        i += 9;
                        continue;
                    }
                    if (d == 'H')
                    {
                        int end = fmt.IndexOf("|h", i + 2, StringComparison.Ordinal);
                        if (end > 0) { i = end + 1; continue; }
                    }
                    if (d == '4')
                    {
                        int colon = fmt.IndexOf(':', i + 2), semi = fmt.IndexOf(';', i + 2);
                        if (colon > 0 && semi > colon)
                        {
                            sb.Append(last == 1 ? fmt.Substring(i + 2, colon - i - 2) : fmt.Substring(colon + 1, semi - colon - 1));
                            i = semi;
                            continue;
                        }
                    }
                }
                if (c == '\\' && i + 2 < fmt.Length && fmt[i + 1] == '3' && fmt[i + 2] == '2') { sb.Append(' '); i += 2; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>A player's / server's text for a label with rich text ON: TMP tags made harmless ('&lt;' -> a look-alike is not
        /// wanted - the label is set to richText false instead where it can be; this is for mixed lines).</summary>
        public static string NoTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOf('<') < 0 ? s : "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";
        }

        /// <summary>A name for the lists: the name, or UNKNOWN (GlobalStrings.lua:7899) while the name query has not answered.</summary>
        public static string Name(string n) { return string.IsNullOrEmpty(n) ? G("UNKNOWN", "Unknown") : n; }

        /// <summary>WoW's class name of the install's ChrClasses (the existing helper), "" for none.</summary>
        public static string ClassName(WowClass c)
        {
            try { return UiArt.ClassName(c) ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>The roster's "Last Online" (FriendsFrame.lua GuildStatus_Update / RecentTimeDate): years / months / days / hours /
        /// minutes from the float days of SMSG_GUILD_ROSTER - LASTONLINE_YEARS ... LASTONLINE_SECS (GlobalStrings.lua 4672-4678).</summary>
        public static string LastOnline(float days)
        {
            if (days >= 365f) return F("LASTONLINE_YEARS", "%d |4year:years;", (int)(days / 365f));
            if (days >= 30f) return F("LASTONLINE_MONTHS", "%d |4month:months;", (int)(days / 30f));
            if (days >= 1f) return F("LASTONLINE_DAYS", "%d |4day:days;", (int)days);
            float hours = days * 24f;
            if (hours >= 1f) return F("LASTONLINE_HOURS", "%d |4hour:hours;", (int)hours);
            float minutes = hours * 60f;
            if (minutes >= 1f) return F("LASTONLINE_MINUTES", "%d |4minute:minutes;", (int)minutes);
            return G("LASTONLINE_SECS", "< a minute");
        }

        public static string FirstWord(string s)
        {
            s = (s ?? "").Trim();
            int sp = s.IndexOf(' ');
            return sp < 0 ? s : s.Substring(0, sp);
        }

        // ------------------------------------------------------------------ popups (uicore's UiPopup = WoW's StaticPopup)

        private static bool _loggedRefused;

        /// <summary>A StaticPopup with an edit box (ADD_FRIEND, ADD_IGNORE, SET_FRIENDNOTE, ADD_GUILDMEMBER, SET_GUILDMOTD, notes, ADD_GUILDRANK,
        /// RENAME_GUILD, ADD_RAIDMEMBER): Accept / Cancel, Enter = Accept, Escape hides; accept gets the trimmed text (empty only when allowEmpty).</summary>
        public static void Ask(string which, string text, int maxLetters, string initial, Action<string> accept, bool allowEmpty = false)
        {
            if (which != null) Asked[which] = new AskArgs { Text = text, MaxLetters = maxLetters, Accept = accept, AllowEmpty = allowEmpty };
            int h = UiPopup.Show(new UiPopupSpec
            {
                Which = which, Text = text, Button1 = G("ACCEPT", "Accept"), Button2 = G("CANCEL", "Cancel"),   // GlobalStrings.lua:11 / :1338
                HasEditBox = true, MaxLetters = maxLetters, EditText = initial ?? "", EnterClicksFirstButton = true, HideOnEscape = true, WhileDead = true,
                OnAcceptText = t =>
                {
                    t = (t ?? "").Trim();
                    if (t.Length == 0 && !allowEmpty) return;
                    try { accept(t); }
                    catch (Exception e) { WowLog.Error("UI: social popup " + which + " failed: " + e); }
                }
            });
            if (h == 0) Refused(which);
        }

        private sealed class AskArgs { public string Text; public int MaxLetters; public Action<string> Accept; public bool AllowEmpty; }
        private static readonly Dictionary<string, AskArgs> Asked = new Dictionary<string, AskArgs>();
        private static readonly string[] NamePopups = { "ADD_IGNORE", "ADD_FRIEND", "ADD_GUILDMEMBER" };   // ItemRef.lua:13-56 (the ones we show)

        /// <summary>A Shift-click on a chat name (ItemRef.lua:13-56): an open ADD_IGNORE / ADD_FRIEND / ADD_GUILDMEMBER popup gets the name in
        /// its edit box - the popup is shown again with the name as its text (UiPopup has no setter for a shown edit box; ours). True = taken.</summary>
        public static bool PutNameInPopup(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < NamePopups.Length; i++)
            {
                string which = NamePopups[i];
                AskArgs a;
                if (!UiPopup.IsShown(which) || !Asked.TryGetValue(which, out a)) continue;
                UiPopup.HideWhich(which);
                Ask(which, a.Text, a.MaxLetters, name, a.Accept, a.AllowEmpty);
                return true;
            }
            return false;
        }

        /// <summary>A StaticPopup with two buttons (CONFIRM_GUILD_LEAVE, REMOVE_GUILDMEMBER, DELETE_MAIL ...); money >= 0 = its money frame.</summary>
        public static void Confirm(string which, string text, string button1, string button2, Action accept, int money = -1)
        {
            int h = UiPopup.Show(new UiPopupSpec
            {
                Which = which, Text = text, Button1 = button1, Button2 = button2, Money = money, HideOnEscape = true, WhileDead = true,
                OnAccept = () =>
                {
                    try { if (accept != null) accept(); }
                    catch (Exception e) { WowLog.Error("UI: social popup " + which + " failed: " + e); }
                }
            });
            if (h == 0) Refused(which);
        }

        private static void Refused(string which)
        {
            if (_loggedRefused) return;
            _loggedRefused = true;
            WowLog.Warn("UI: social - the popup " + which + " was not shown (UiPopup refused it, logged once).");
        }

        /// <summary>The UiKit build state of a window (its surface, desktop raycasts, arm materials), to build parts later (lazy pages, menus).</summary>
        public sealed class Scope
        {
            public UiSurface Surface;
            public bool Pointable, Over;

            public static Scope Capture() { return new Scope { Surface = UiKit.Surface, Pointable = UiKit.Pointable, Over = UiKit.Over }; }

            public void Run(Action build)
            {
                UiSurface s = UiKit.Surface;
                bool p = UiKit.Pointable, o = UiKit.Over;
                try
                {
                    UiKit.Surface = Surface;
                    UiKit.Pointable = Pointable;
                    UiKit.Over = Over;
                    build();
                }
                finally
                {
                    UiKit.Surface = s;
                    UiKit.Pointable = p;
                    UiKit.Over = o;
                }
            }
        }

        public static bool Shift()
        {
            try { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ text fields

        /// <summary>
        /// A text field: desktop = a TMP input field the mouse focuses (Enter = onEnter for single lines); VR = the same field, a press on it
        /// opens the headset keyboard (UiSocialHost.Keyboard, Game/Front/WowKeyboard) titled with title, its Enter = onEnter. The VR press
        /// button is built first (under the field), so on the desktop the field itself takes the mouse.
        /// </summary>
        public static TMP_InputField Field(Transform parent, string name, float x, float y, float w, float h, int limit, bool multiLine, float size,
            string title, Action onEnter)
        {
            UiButton press = UiKit.Button(parent, name + " (press)", x, y, w, h);
            RectTransform r = UiKit.Rect(parent, name, x, y, w, h);
            GameObject go = r.gameObject;
            go.SetActive(false);   // configure before TMP_InputField's OnEnable runs
            RawImage bg = UiKit.Solid(r, "Box", new Color(0f, 0f, 0f, 0.55f), 0f, 0f, w, h);
            bg.raycastTarget = UiKit.Pointable;
            RectTransform area = UiKit.Rect(r, "Text Area", 5f, multiLine ? 3f : 0f, w - 10f, multiLine ? h - 6f : h);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = UiKit.Text(area, "Text", "", size, Color.white, multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft,
                0f, 0f, w - 10f, multiLine ? h - 6f : h, null, false);
            text.richText = false;
            text.textWrappingMode = multiLine ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            var f = go.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.targetGraphic = bg;
            if (UiArt.Friz != null) f.fontAsset = UiArt.Friz;
            f.pointSize = size;
            f.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            f.characterLimit = limit;
            f.richText = false;
            f.customCaretColor = true;
            f.caretColor = Color.white;
            f.caretWidth = 2;
            f.selectionColor = new Color(0.9f, 0.7f, 0.1f, 0.35f);
            f.onFocusSelectAll = false;
            f.shouldHideSoftKeyboard = true;
            f.restoreOriginalTextOnEscape = false;
            f.contentType = TMP_InputField.ContentType.Standard;
            go.SetActive(true);
            if (onEnter != null && !multiLine) f.onSubmit.AddListener(_ => { try { onEnter(); } catch (Exception e) { WowLog.Error("UI: social field '" + name + "' failed: " + e); } });
            string t = title;
            press.Click = () => Type(f, t, multiLine ? null : onEnter);
            return f;
        }

        /// <summary>Starts typing into a field: VR = the headset keyboard; desktop = the EventSystem focus.</summary>
        public static void Type(TMP_InputField f, string title, Action onEnter)
        {
            if (f == null) return;
            if (WowPlayer.XrPresent)
            {
                WowKeyboard k = UiSocialHost.Keyboard;
                if (k != null) k.Open(f, title ?? "", false, onEnter);
                return;
            }
            EventSystem es = EventSystem.current;
            if (es == null) return;
            es.SetSelectedGameObject(f.gameObject);
            f.ActivateInputField();
        }

        /// <summary>The keyboard was typing into one of these fields: closed (a window closing / a page change).</summary>
        public static void StopTyping(TMP_InputField f)
        {
            if (f == null) return;
            WowKeyboard k = UiSocialHost.KeyboardIfAny;
            if (k != null && k.IsOpen && k.Target == f) k.Close();
            if (f.isFocused) f.DeactivateInputField();
        }

        public static string Text(TMP_InputField f) { return f != null ? (f.text ?? "").Trim() : ""; }

        // ------------------------------------------------------------------ tabs and rows

        /// <summary>
        /// FriendsFrameTabTemplate / MailFrameTab = CharacterFrameTabButtonTemplate (CharacterFrameTemplates.xml): UI-Character-InActiveTab
        /// 20 + middle + 20 (texcoords 0-0.15625 / -0.84375 / -1), the selected tab UI-Character-ActiveTab drawn 5 units higher, the
        /// highlight UI-Character-Tab-Highlight (ADD) inset 10, GameFontNormalSmall; the width = the text + 40 (PanelTemplates_TabResize).
        /// </summary>
        public sealed class Tab
        {
            public UiButton B;
            public RawImage[] Off = new RawImage[3], On = new RawImage[3];
            public TextMeshProUGUI Label;
            public float W;
            public float LabelOnY, LabelOffY;   // the text follows the art (the selected bottom tab stands 5 higher, a sub tab 3 lower) - ours
            private bool _selected = true;

            public bool Selected { get { return _selected; } }

            public void Select(bool on)
            {
                if (on == _selected) return;
                _selected = on;
                for (int i = 0; i < 3; i++) { Off[i].enabled = !on; On[i].enabled = on; }
                Label.color = on ? Color.white : (B.Interactable ? UiArt.Gold : UiArt.Grey);
                UiKit.Move(Label.rectTransform, 0f, on ? LabelOnY : LabelOffY);
            }

            public bool Enabled
            {
                set
                {
                    B.Interactable = value;
                    if (!_selected) Label.color = value ? UiArt.Gold : UiArt.Grey;
                }
            }
        }

        const string TabOff = @"Interface\PaperDollInfoFrame\UI-Character-InActiveTab.blp", TabOn = @"Interface\PaperDollInfoFrame\UI-Character-ActiveTab.blp",
            TabHilite = @"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp";

        public static Tab BottomTab(Transform parent, string label, float x, float y, Action click)
        {
            var t = new Tab { LabelOnY = -4f, LabelOffY = -1f };
            t.B = UiKit.Button(parent, "Tab " + label, x, y, 100f, 32f);
            t.Label = UiKit.Text(t.B.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 100f, 32f);
            t.Label.textWrappingMode = TextWrappingModes.NoWrap;
            float tw = Mathf.Ceil(t.Label.GetPreferredValues(label).x);
            t.W = Mathf.Max(60f, tw + 40f);
            float mid = t.W - 40f;
            UiKit.Size(t.B.Rt, t.W, 32f);
            UiKit.Size(t.Label.rectTransform, t.W, 32f);
            Texture2D off = UiArt.Tex(TabOff), on = UiArt.Tex(TabOn);
            t.Off[0] = UiKit.Pic(t.B.transform, "L", off, 0f, 0.15625f, 0f, 1f, 0f, 0f, 20f, 32f);
            t.Off[1] = UiKit.Pic(t.B.transform, "M", off, 0.15625f, 0.84375f, 0f, 1f, 20f, 0f, mid, 32f);
            t.Off[2] = UiKit.Pic(t.B.transform, "R", off, 0.84375f, 1f, 0f, 1f, 20f + mid, 0f, 20f, 32f);
            t.On[0] = UiKit.Pic(t.B.transform, "L on", on, 0f, 0.15625f, 0f, 1f, 0f, -5f, 20f, 32f);
            t.On[1] = UiKit.Pic(t.B.transform, "M on", on, 0.15625f, 0.84375f, 0f, 1f, 20f, -5f, mid, 32f);
            t.On[2] = UiKit.Pic(t.B.transform, "R on", on, 0.84375f, 1f, 0f, 1f, 20f + mid, -5f, 20f, 32f);
            t.B.Hilite = UiKit.Pic(t.B.transform, "Highlight", UiArt.Add(TabHilite), 10f, 2f, t.W - 20f, 32f);
            t.B.Hilite.enabled = false;
            t.Label.transform.SetAsLastSibling();   // over the art
            t.B.Click = click;
            t.Select(false);
            return t;
        }

        const string SubOff = @"Interface\HelpFrame\HelpFrameTab-Inactive.blp", SubOn = @"Interface\HelpFrame\HelpFrameTab-Active.blp";

        /// <summary>TabButtonTemplate (UIPanelTemplates.xml): HelpFrameTab-Inactive / -Active 16 + middle + 16 (texcoords 0-0.25 / -0.75 / -1),
        /// the active one 3 units lower; ours 80 x 24 (proposal-social.md 6.5.1).</summary>
        public static Tab SubTab(Transform parent, string label, float x, float y, float w, Action click)
        {
            var t = new Tab { W = w, LabelOnY = 2f, LabelOffY = 0f };
            const float h = 24f;
            float mid = w - 32f;
            t.B = UiKit.Button(parent, "Sub tab " + label, x, y, w, h);
            Texture2D off = UiArt.Tex(SubOff), on = UiArt.Tex(SubOn);
            t.Off[0] = UiKit.Pic(t.B.transform, "L", off, 0f, 0.25f, 0f, 1f, 0f, 0f, 16f, h);
            t.Off[1] = UiKit.Pic(t.B.transform, "M", off, 0.25f, 0.75f, 0f, 1f, 16f, 0f, mid, h);
            t.Off[2] = UiKit.Pic(t.B.transform, "R", off, 0.75f, 1f, 0f, 1f, 16f + mid, 0f, 16f, h);
            t.On[0] = UiKit.Pic(t.B.transform, "L on", on, 0f, 0.25f, 0f, 1f, 0f, 3f, 16f, h);
            t.On[1] = UiKit.Pic(t.B.transform, "M on", on, 0.25f, 0.75f, 0f, 1f, 16f, 3f, mid, h);
            t.On[2] = UiKit.Pic(t.B.transform, "R on", on, 0.75f, 1f, 0f, 1f, 16f + mid, 3f, 16f, h);
            t.B.Hilite = UiKit.Pic(t.B.transform, "Highlight", UiArt.Add(TabHilite), 6f, 2f, w - 12f, h);
            t.B.Hilite.enabled = false;
            t.Label = UiKit.Text(t.B.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, w, h);
            t.Label.textWrappingMode = TextWrappingModes.NoWrap;
            t.B.Click = click;
            t.Select(false);
            return t;
        }

        /// <summary>One clickable list row with up to 4 text columns (FriendsFrameButtonTemplate / WhoFrameButtonTemplate / GuildFrame
        /// buttons): the highlight bar on hover, a gold "selected" bar. Left click = Click, right click = RightClick (desktop).</summary>
        public sealed class Row
        {
            public UiButton B;
            public RawImage Sel, Icon;
            public TextMeshProUGUI[] Col;
            public int Index;
        }

        const string HighlightBar = @"Interface\FriendsFrame\UI-FriendsFrame-HighlightBar.blp";   // FriendsFrame.xml:946 (ADD)

        public static Row MakeRow(Transform parent, string name, float x, float y, float w, float h, float[] colX, float[] colW, float size, bool icon)
        {
            var r = new Row();
            r.B = UiKit.Button(parent, name, x, y, w, h);
            r.Sel = UiKit.Pic(r.B.transform, "Selected", UiArt.Add(HighlightBar), 0f, 0f, w, h);
            r.Sel.color = new Color(1f, 0.82f, 0f, 0.6f);
            r.Sel.enabled = false;
            r.B.Hilite = UiKit.Pic(r.B.transform, "Highlight", UiArt.Add(HighlightBar), 0f, 0f, w, h);
            r.B.Hilite.enabled = false;
            if (icon) r.Icon = UiKit.Pic(r.B.transform, "Icon", null, 2f, (h - 16f) * 0.5f, 16f, 16f);
            r.Col = new TextMeshProUGUI[colX.Length];
            for (int i = 0; i < colX.Length; i++)
            {
                TextMeshProUGUI t = UiKit.Text(r.B.transform, "Col " + i, "", size, UiArt.Gold, TextAlignmentOptions.MidlineLeft, colX[i], 0f, colW[i], h);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.richText = false;
                r.Col[i] = t;
            }
            r.B.gameObject.SetActive(false);
            return r;
        }

        public static void SetText(TextMeshProUGUI t, string s, Color c)
        {
            if (t == null) return;
            s = s ?? "";
            if (!string.Equals(t.text, s)) t.text = s;
            if (t.color != c) t.color = c;
        }

        public static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>A plain check box (UICheckButtonTemplate: UI-CheckBox-Up / -Check), 20 x 20 + its label; Checked shown while on.</summary>
        public static UiButton Check(Transform parent, string label, float x, float y, float labelW, Action click)
        {
            UiButton b = UiKit.Button(parent, "Check " + label, x, y, 20f + labelW, 20f);
            b.Art = UiKit.Pic(b.transform, "Up", UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Up.blp"), -2f, -2f, 24f, 24f);
            b.Checked = UiKit.Pic(b.transform, "Check", UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Check.blp"), -2f, -2f, 24f, 24f);
            b.Checked.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(@"Interface\Buttons\UI-CheckBox-Highlight.blp"), -2f, -2f, 24f, 24f);
            b.Hilite.enabled = false;
            TextMeshProUGUI t = UiKit.Text(b.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 24f, 0f, labelW, 20f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            b.Click = click;
            return b;
        }

        /// <summary>A 16-unit high scroll pair (UIPanelScrollUpButtonTemplate / Down) at the right edge of a list; by = rows per press.</summary>
        public static void ScrollPair(Transform parent, float x, float yTop, float yBottom, Action up, Action down)
        {
            UiKit.ArtButton(parent, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, x, yTop, 16f, up);
            UiKit.ArtButton(parent, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, x, yBottom, 16f, down);
        }

        /// <summary>The mouse wheel over a rect (desktop): +1 up, -1 down, 0 none.</summary>
        public static int Wheel(RectTransform r)
        {
            if (r == null || WowPlayer.XrPresent) return 0;
            try
            {
                float y = Input.mouseScrollDelta.y;
                if (Mathf.Abs(y) < 0.01f) return 0;
                if (!RectTransformUtility.RectangleContainsScreenPoint(r, Input.mousePosition, null)) return 0;
                return y > 0f ? 1 : -1;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>A desktop panel drawn outside its window's 384 x 512 (GuildMemberDetailFrame, OpenMailFrame): a transparent catcher, so
        /// a click on it never reaches the world.</summary>
        public static void Catch(RectTransform r)
        {
            if (!UiKit.Pointable || r == null) return;
            Image img = r.gameObject.GetComponent<Image>();
            if (img == null) img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
        }
    }
}
