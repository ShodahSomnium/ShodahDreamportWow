using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// PartyMemberFrame (PartyFrameTemplates.xml PartyMemberFrameTemplate): 128x53, UI-PartyFrame 128x64 at (0, -2), portrait 37x37
    /// at (7, -6), health 70x8 at (47, -12), mana 70x8 at (47, -21) (BarColor 0, 0, 1 then the power colour), name GameFontNormalSmall
    /// BOTTOMLEFT (50, 43), the leader icon UI-Group-LeaderIcon 16x16 at TOPLEFT, the disconnect icon 64x64 at LEFT (-7, -1).
    /// One member of WowParty.Members: the portrait is that Somnium player's profile picture when the party owner has it
    /// (WowPartyMember.Portrait, cut round here like WoW's SetPortraitToTexture), else the class circle; the name in
    /// RAID_CLASS_COLORS, the level on the portrait, health as a percentage. A member of the server group also shows its power
    /// (GroupMemberView), the leader icon, and WoW's portrait tints (PartyMemberHealthCheck, PartyMemberFrame.lua:513-534: dead
    /// 0.35 grey, ghost (0.2, 0.2, 0.75), 20% health or less red) and the offline look (PartyMemberFrame_UpdateOnlineStatus :608-626:
    /// health bar full and grey, portrait greyed, the disconnect icon). Players on another map or in the room are drawn faded.
    /// Stage S1: the frame is a button - click (desktop) or ray + trigger (VR) = target that player (SecureUnitButtonTemplate's
    /// left click), right-click (desktop) = make it the focus (ours; WoW puts "Set Focus" in the frame's menu).
    /// </summary>
    public sealed class UiPartyFrame
    {
        public const string Frame = @"Interface\TargetingFrame\UI-PartyFrame.blp";
        public const string LeaderIcon = @"Interface\GroupFrame\UI-Group-LeaderIcon.blp";
        public const string DisconnectIcon = @"Interface\CharacterFrame\Disconnect-Icon.blp";

        public RectTransform Root;
        public UiButton Button;
        private readonly CanvasGroup _group;
        private readonly RawImage _portrait, _leader, _disconnect, _mpBack;
        private readonly UiBar _hp, _mp;
        private readonly TextMeshProUGUI _name, _level, _hpText;
        private readonly bool _desktop;
        private int _id = int.MinValue, _lvl = -1, _pct = -1, _tint = -1, _pt = -1, _mpShown = -1, _far = -1;
        private ulong _guidShown = ulong.MaxValue;
        private WowClass _cls = (WowClass)255;
        private string _nameShown;
        private Texture2D _pfpSource;
        private bool _pfp, _leaderOn, _offline;

        /// <summary>What the frame shows now (the click / tooltip read it).</summary>
        public WowPartyMember Member;
        public bool HasMember;

        public UiPartyFrame(Transform parent, float x, float y, bool desktop)
        {
            _desktop = desktop;
            Root = UiKit.Rect(parent, "Party Member", x, y, 128f, 53f);
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = true;   // stage S1: the frame is clickable (desktop GraphicRaycaster; VR ignores it)
            _group.interactable = false;
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.5f), 47f, 12f, 70f, 17f);
            _portrait = UiKit.Pic(Root, "Portrait", UiArt.Tex(UiArt.ClassCircles), 7f, 6f, 37f, 37f);
            _hp = UiBar.Make(Root, "Health", 47f, 12f, 70f, 8f, UiArt.HealthGreen, false);
            _mpBack = UiKit.Solid(Root, "Mana background", new Color(0f, 0f, 0f, 0f), 47f, 21f, 70f, 8f);
            _mp = UiBar.Make(Root, "Mana", 47f, 21f, 70f, 8f, new Color(0f, 0f, 1f), false);
            _mp.Root.gameObject.SetActive(false);
            UiKit.Pic(Root, "Frame", UiArt.Tex(Frame), 0f, 2f, 128f, 64f);
            _name = UiKit.Text(Root, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 50f, -4f, 76f, 14f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _level = UiKit.Text(Root, "Level", "", 9f, Color.white, TextAlignmentOptions.Center, 7f, 31f, 37f, 12f);
            _hpText = UiKit.Text(Root, "Health Text", "", 7.5f, Color.white, TextAlignmentOptions.Center, 47f, 11f, 70f, 10f);
            _leader = UiKit.Pic(Root, "Leader", UiArt.Tex(LeaderIcon), 0f, 0f, 16f, 16f);
            _leader.enabled = false;
            _disconnect = UiKit.Pic(Root, "Disconnect", UiArt.Tex(DisconnectIcon), -7f, (53f - 64f) * 0.5f + 1f, 64f, 64f);
            _disconnect.enabled = false;

            // The click area: the portrait and the bars (the name line above it stays free for the frame above).
            Button = UiKit.Button(Root, "Party member", 4f, 3f, 118f, 45f);
            // Ours: a soft highlight while the mouse / the VR laser is on the frame (WoW's party frames have no highlight texture).
            Button.Hilite = UiKit.Pic(Button.transform, "Highlight", UiArt.Add(UiArt.QuestLogTitleHighlight), 40f, 6f, 78f, 26f);
            Button.Hilite.color = new Color(1f, 0.82f, 0f, 0.6f);
            Button.Hilite.enabled = false;
            Button.Click = OnClick;
            Button.RightClick = OnRightClick;
            if (desktop) Button.Tip = Tip;
            Root.gameObject.SetActive(false);
        }

        public void Show(WowPartyMember m, bool sameMap, IWowSession s)
        {
            Member = m;
            HasMember = true;
            if (!Root.gameObject.activeSelf) Root.gameObject.SetActive(true);
            if (m.PlayerId != _id || m.Guid != _guidShown) { _id = m.PlayerId; _guidShown = m.Guid; _lvl = _pct = _far = _tint = _pt = _mpShown = -1; _cls = (WowClass)255; _nameShown = null; _pfpSource = null; _pfp = true; }   // _pfp true: re-pick the portrait below
            if (!ReferenceEquals(m.Name, _nameShown)) { _nameShown = m.Name; _name.text = m.Name ?? ""; }
            GroupMemberView gm = UiUnits.GroupMember(s, m.Guid);

            // Portrait: the Somnium profile picture (round), else the class circle.
            Texture2D src = m.Portrait;
            if (!ReferenceEquals(src, _pfpSource) || (src == null && _pfp))
            {
                _pfpSource = src;
                bool round = false;
                Texture2D t = src != null ? UiRoundPortrait.Of(src, out round) : null;
                if (t != null)
                {
                    _pfp = true;
                    _portrait.texture = t;
                    // A downloaded picture is stored bottom row first (Unity's own orientation): no v flip, unlike a BLP (rule 15).
                    // Not readable -> the square picture inside the round hole (its corners would show over the ring otherwise).
                    UiKit.Coords(_portrait, 0f, 1f, 1f, 0f);
                    float side = round ? 37f : 26f;
                    _portrait.rectTransform.sizeDelta = new Vector2(side, side);
                    UiKit.Move(_portrait.rectTransform, 7f + (37f - side) * 0.5f, 6f + (37f - side) * 0.5f);
                }
                else
                {
                    _pfp = false;
                    _portrait.texture = UiArt.Tex(UiArt.ClassCircles);
                    _portrait.rectTransform.sizeDelta = new Vector2(37f, 37f);
                    UiKit.Move(_portrait.rectTransform, 7f, 6f);
                    _cls = (WowClass)255;   // re-apply the class circle's texcoords below
                }
                _tint = -1;
            }
            if (m.Class != _cls)
            {
                _cls = m.Class;
                if (!_pfp)
                {
                    Vector4 c = UiArt.ClassCoords(m.Class);
                    UiKit.Coords(_portrait, c.x, c.y, c.z, c.w);
                }
                _name.color = UiParty.ClassColor(m.Class);
            }
            if (m.Level != _lvl) { _lvl = m.Level; _level.SetText("{0}", m.Level); }

            bool offline = gm != null && !gm.Online;
            int pct = Mathf.Clamp(Mathf.RoundToInt(m.HealthPct * 100f), 0, 100);
            if (pct != _pct || offline != _offline)
            {
                _pct = pct;
                _offline = offline;
                _hp.Set(offline ? 1f : pct / 100f);
                _hp.SetColor(offline ? new Color(0.5f, 0.5f, 0.5f) : UiArt.HealthGreen);
                if (offline) _hpText.SetText("");
                else _hpText.SetText("{0}%", pct);
                _disconnect.enabled = offline && _disconnect.texture != null;
                _tint = -1;
            }
            // PartyMemberHealthCheck: dead / ghost / low health tints (group members; others: dead at 0% as before).
            bool dead = gm != null ? gm.Dead : pct == 0;
            bool ghost = gm != null && gm.Ghost;
            int tint = offline ? 4 : ghost ? 2 : dead ? 1 : pct > 0 && pct <= 20 && gm != null ? 3 : 0;
            if (tint != _tint)
            {
                _tint = tint;
                _portrait.color = tint == 4 ? new Color(0.5f, 0.5f, 0.5f) : tint == 1 ? new Color(0.35f, 0.35f, 0.35f) : tint == 2 ? new Color(0.2f, 0.2f, 0.75f) : tint == 3 ? new Color(1f, 0f, 0f) : Color.white;
            }
            // Power (the server group only: the Somnium record carries none, so that slot stays dark).
            int mpShown = gm != null && gm.PowerMax > 0 && !offline ? 1 : 0;
            if (mpShown != _mpShown) { _mpShown = mpShown; _mp.Root.gameObject.SetActive(mpShown == 1); }
            if (mpShown == 1)
            {
                if ((int)gm.Power != _pt) { _pt = (int)gm.Power; _mp.SetColor(UiArt.PowerColor(gm.Power)); }
                _mp.Set((float)gm.PowerValue / gm.PowerMax);
            }
            bool leader = s != null && s.Player != null && m.Guid != 0 && s.Player.GroupLeaderGuid == m.Guid;
            if (leader != _leaderOn) { _leaderOn = leader; _leader.enabled = leader && _leader.texture != null; }
            int far = sameMap ? 0 : 1;
            if (far != _far) { _far = far; _group.alpha = far == 1 ? 0.5f : 1f; }
        }

        public void Hide()
        {
            if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false);
            _id = int.MinValue;
            HasMember = false;
        }

        private void OnClick()
        {
            IWowSession s = WowPlay.Session;
            if (s == null || !HasMember) return;
            WowPartyMember m = Member;
            if (m.Guid == 0)
            {
                // Offline the other Somnium players are not units of this game (each player runs their own world); online a member
                // the realm has not shown us yet has no unit either.
                UiParty.Say(UiParty.IsOnline(s) ? (m.Name ?? "That player") + " cannot be targeted yet (the realm has not shown them)." : "Other players can be targeted on the online realm.");   // ours
                return;
            }
            s.SetTarget(m.Guid);   // WowArmUi plays the target sound on the change
            UiParty.Clicks++;
            if (UiParty.Clicks <= 20 || UiParty.Clicks % 50 == 0)
                WowLog.Info("UI: party frame " + (m.Name ?? "?") + " (0x" + m.Guid.ToString("X") + ") clicked - target " + (s.TargetGuid == m.Guid ? "set" : "refused by the session") + " (" + UiParty.Clicks + ").");
        }

        private void OnRightClick()
        {
            IWowSession s = WowPlay.Session;
            if (s == null || !HasMember) return;
            if (Member.Guid == 0) { OnClick(); return; }
            UiGroup.OpenUnitMenu(Member.Guid);   // lead 2026-09-27: WoW's PARTY unit menu (Set Focus, Whisper, Promote to Leader, Uninvite)
        }

        private string Tip()
        {
            if (!HasMember) return null;
            WowPartyMember m = Member;
            string cls = UiArt.ClassName(m.Class);
            return "<size=14>" + (m.Name ?? "") + "</size>\nLevel " + m.Level + (string.IsNullOrEmpty(cls) ? "" : " " + cls)
                + "\n<color=#00ff00>Click: target. Right-click: menu.</color>";   // the hints are ours
        }
    }

    /// <summary>
    /// Up to four party frames (WoW's PartyMemberFrame1-4) from WowParty.Members (the party owner fills it: the server group, the
    /// Somnium players): members of the server group first in the server's order, then members on the local player's map, then the
    /// nearest, then by name. Stage S1: also builds the focus frame (UiFocusFrame, FocusFrame TOPLEFT (250, -240) of UIParent =
    /// 177.5 / 20 units from the first party frame at the small focus scale) and, on the desktop, applies the top-left group's size
    /// and position from the settings (UiHudPlacement) - the desktop layout builds this group once and refreshes it every frame.
    /// </summary>
    public sealed class UiParty
    {
        public const int Max = 4;
        private readonly UiPartyFrame[] _f = new UiPartyFrame[Max];
        private readonly List<Entry> _sorted = new List<Entry>();
        private static int _map;
        private static readonly Color[] Colors = new Color[16];
        private static readonly bool[] ColorKnown = new bool[16];
        private int _lastCount = -1;
        private static bool _loadTried;
        private static List<WowClassEntry> _loaded;
        private readonly UiHudPlacement _place;
        internal static int Clicks;

        public UiFocusFrame Focus;
        public int Count { get; private set; }

        private struct Entry { public WowPartyMember M; public int Rank; }

        /// <summary>Frame i at (x + dx * i, y + step * i); the focus frame on the same surface.</summary>
        public UiParty(Transform parent, float x, float y, float dx, float step) : this(parent, x, y, dx, step, null) { }

        /// <summary>Stage S1: focusSurface = the surface the focus frame's buttons belong to (VR: its own pointable region), null = UiKit.Surface.</summary>
        public UiParty(Transform parent, float x, float y, float dx, float step, UiSurface focusSurface)
        {
            Canvas canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            bool desktop = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
            for (int i = 0; i < Max; i++) _f[i] = new UiPartyFrame(parent, x + dx * i, y + step * i, desktop);
            UiSurface was = UiKit.Surface;
            if (focusSurface != null) UiKit.Surface = focusSurface;
            try { Focus = new UiFocusFrame(parent, x + FocusDx, y + FocusDy, desktop); }
            finally { UiKit.Surface = was; }
            if (desktop) _place = new UiHudPlacement(parent as RectTransform);
            VrFocusButton.Ensure(parent);
        }

        /// <summary>The focus frame's place from the first party frame: FocusFrame TOPLEFT (250, -240) at SMALL_FOCUS_SCALE 0.75
        /// (TargetFrame.xml:658-666, TargetFrame.lua:18) = (187.5, 180) of UIParent; PartyMemberFrame1 is at (10, 160).</summary>
        public const float FocusDx = 177.5f, FocusDy = 20f;

        private static bool SameSubGroup(RaidView raid, ulong guid)
        {
            for (int i = 0; i < raid.Members.Count; i++)
            {
                RaidMemberView r = raid.Members[i];
                if (r != null && r.Guid == guid) return r.SubGroup == raid.MySubGroup;
            }
            return true;   // not listed (yet): shown, as before
        }

        public int Refresh(IWowSession s)
        {
            _sorted.Clear();
            List<WowPartyMember> src = WowParty.Members;
            List<GroupMemberView> group = s != null && s.Player != null ? s.Player.Group : null;
            // Iteration 11 (interact): in a raid WoW's party frames are the player's own raid sub group (party1..4).
            RaidView raid = null;
            try { raid = s != null && s.Interactions != null ? s.Interactions.Raid : null; }
            catch (Exception) { raid = null; }
            bool inRaid = raid != null && raid.IsRaid;
            for (int i = 0; i < src.Count; i++)
            {
                WowPartyMember m = src[i];
                int rank = int.MaxValue;
                if (group != null && m.Guid != 0)
                    for (int k = 0; k < group.Count; k++) if (group[k] != null && group[k].Guid == m.Guid) { rank = k; break; }
                if (inRaid && rank != int.MaxValue && !SameSubGroup(raid, m.Guid)) continue;
                _sorted.Add(new Entry { M = m, Rank = rank });
            }
            _map = s != null ? s.MapId : -1;
            if (_sorted.Count > 1) _sorted.Sort(Compare);
            int n = Math.Min(Max, _sorted.Count);
            for (int i = 0; i < Max; i++)
            {
                if (i < n) _f[i].Show(_sorted[i].M, _sorted[i].M.MapId == _map && _map >= 0, s);
                else _f[i].Hide();
            }
            Count = n;
            if (n != _lastCount)
            {
                if (_lastCount >= 0 || n > 0)
                    WowLog.Info("UI: party frames show " + n + " of " + _sorted.Count + " other player(s)" + (n > 0 ? " (first: " + _sorted[0].M.Name + ", level " + _sorted[0].M.Level + " " + UiArt.ClassName(_sorted[0].M.Class) + ", " + Mathf.RoundToInt(_sorted[0].M.HealthPct * 100f) + "%" + (_sorted[0].Rank != int.MaxValue ? ", in your group" : "") + ")" : "") + ".");
                _lastCount = n;
            }
            if (Focus != null && s != null) Focus.Refresh(s);
            if (_place != null) _place.Apply();
            return n;
        }

        private static readonly Comparison<Entry> Compare = (ea, eb) =>
        {
            if (ea.Rank != eb.Rank) return ea.Rank.CompareTo(eb.Rank);
            WowPartyMember a = ea.M, b = eb.M;
            bool am = a.MapId == _map, bm = b.MapId == _map;
            if (am != bm) return am ? -1 : 1;
            int d = a.Distance.CompareTo(b.Distance);
            if (d != 0) return d;
            d = string.CompareOrdinal(a.Name, b.Name);
            return d != 0 ? d : a.PlayerId.CompareTo(b.PlayerId);
        };

        /// <summary>A yellow line on the HUD (the session's Info path: WowArmUi -> the layout's messages).</summary>
        internal static void Say(string text)
        {
            try { WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = text }); }
            catch (Exception) { }
        }

        internal static bool IsOnline(IWowSession s)
        {
            return s != null && !(s is OfflineSession);
        }

        /// <summary>RAID_CLASS_COLORS as the front end read it from FrameXML\Constants.lua (WowPlay.Classes); gold when unknown.</summary>
        public static Color ClassColor(WowClass c)
        {
            int i = (int)c & 15;
            if (ColorKnown[i]) return Colors[i];
            Color col = UiArt.Gold;
            try
            {
                List<WowClassEntry> list = WowPlay.Classes;
                if (list == null && !_loadTried && UiArt.Files != null)
                {
                    // WowGame sets WowPlay.Classes on start; read them here only if it has not (the lab, an early frame).
                    _loadTried = true;
                    string report;
                    _loaded = WowClassInfo.Load(UiArt.Files, out report);
                }
                if (list == null) list = _loaded;
                if (list == null) return col;   // not known yet: ask again next time
                WowClassEntry e = WowClassInfo.Find(list, c);
                if (e != null) col = new Color(e.R, e.G, e.B);
            }
            catch (Exception) { }
            Colors[i] = col;
            ColorKnown[i] = true;
            return col;
        }
    }

    /// <summary>
    /// Stage S1: a Somnium profile picture cut round for a portrait slot, the way WoW's SetPortraitToTexture masks a picture with
    /// TempPortraitAlphaMask: centre square, 64 x 64, a one-pixel soft edge, made once per picture (memory only, never saved).
    /// A picture that is not readable on the CPU is returned as it is (round = false; the frame shows it smaller inside the ring).
    /// </summary>
    public static class UiRoundPortrait
    {
        public const int Size = 64;
        private static readonly Dictionary<Texture2D, Texture2D> Cache = new Dictionary<Texture2D, Texture2D>();
        private static readonly List<Texture2D> Keys = new List<Texture2D>();
        private static int _made, _flat;

        public static Texture2D Of(Texture2D src, out bool round)
        {
            round = false;
            if (src == null) return null;
            Texture2D r;
            if (!Cache.TryGetValue(src, out r) || r == null)
            {
                Prune();
                r = Make(src);
                if (r == null) r = src;
                Cache[src] = r;
                Keys.Add(src);
            }
            round = !ReferenceEquals(r, src);
            return r;
        }

        private static Texture2D Make(Texture2D src)
        {
            try
            {
                if (!src.isReadable)
                {
                    if (_flat++ < 3) WowLog.Info("UI: party portrait " + src.width + "x" + src.height + " is not readable - shown square inside the ring.");
                    return null;
                }
                float w = src.width, h = src.height, side = Mathf.Min(w, h);
                if (side < 1f) return null;
                float u0 = (w - side) * 0.5f / w, v0 = (h - side) * 0.5f / h, us = side / w, vs = side / h;
                var px = new Color32[Size * Size];
                float half = Size * 0.5f;
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float dx = x + 0.5f - half, dy = y + 0.5f - half;
                        float a = Mathf.Clamp01(half - Mathf.Sqrt(dx * dx + dy * dy));
                        Color c = src.GetPixelBilinear(u0 + (x + 0.5f) / Size * us, v0 + (y + 0.5f) / Size * vs);
                        c.a *= a;
                        px[y * Size + x] = c;
                    }
                }
                var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                t.name = "WoW party portrait (round)";
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                t.SetPixels32(px);
                t.Apply(false, true);
                _made++;
                if (_made <= 8) WowLog.Info("UI: party portrait " + src.width + "x" + src.height + " cut round (" + _made + ").");
                return t;
            }
            catch (Exception e)
            {
                if (_flat++ < 3) WowLog.Warn("UI: party portrait could not be cut round: " + e.Message);
                return null;
            }
        }

        /// <summary>Forgets the copies of pictures that are gone (the party owner's cache let them go).</summary>
        private static void Prune()
        {
            if (Keys.Count < 16) return;
            for (int i = Keys.Count - 1; i >= 0; i--)
            {
                Texture2D k = Keys[i];
                if (k != null) continue;
                Texture2D r;
                if (Cache.TryGetValue(k, out r) && r != null && !ReferenceEquals(r, k)) UnityEngine.Object.Destroy(r);
                Cache.Remove(k);
                Keys.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// TargetFrameSpellBar: TargetSpellBarTemplate 150x10 (TargetFrame.xml:622), UI-CastingBar-Border-Small 197x49 at TOP (0, 20),
    /// text SystemFont_Shadow_Small TOP (0, 4) 150 wide (SetTargetSpellbarAspect, TargetFrame.lua:791-821), the spell icon 16x16
    /// RIGHT of the bar's LEFT (-5, 0) (CastingBarFrame.xml); placed at the target frame's BOTTOMLEFT (25, 7), or under the aura row
    /// (20, -15) when the target has auras (Target_Spellbar_AdjustPosition). Yellow while casting, green and draining for a channel,
    /// red with the reason when it is interrupted (CastFailed / ChannelEnded of the target), then it fades.
    /// </summary>
    public sealed class UiTargetCastBar
    {
        public const string BorderSmall = @"Interface\CastingBar\UI-CastingBar-Border-Small.blp";
        public const string FlashSmall = @"Interface\CastingBar\UI-CastingBar-Flash-Small.blp";

        public RectTransform Root;
        private readonly UiBar _bar;
        private readonly RawImage _spark, _icon;
        private readonly TextMeshProUGUI _text;
        private readonly CanvasGroup _group;
        private ulong _guid;
        private int _spell;
        private bool _casting, _channel, _auras;
        private float _endAt = -1f, _failAt = -10f, _shown = -1f;
        private string _failReason;
        private int _failSpell;
        private ulong _failGuid;

        public UiTargetCastBar(Transform targetFrame)
        {
            Root = UiKit.Rect(targetFrame, "Target Cast Bar", 25f, 93f, 150f, 10f);
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.5f), 0f, 0f, 150f, 10f);
            _bar = UiBar.Make(Root, "Fill", 0f, 0f, 150f, 10f, UiArt.CastYellow, false);
            UiKit.Pic(Root, "Border", UiArt.Tex(BorderSmall), (150f - 197f) * 0.5f, -20f, 197f, 49f);
            _spark = UiKit.Pic(Root, "Spark", UiArt.Add(UiArt.CastSpark), 0f, -11f, 32f, 32f);
            _icon = UiKit.Pic(Root, "Icon", null, -21f, -3f, 16f, 16f);
            _icon.enabled = false;
            _text = UiKit.Text(Root, "Text", "", 9f, Color.white, TextAlignmentOptions.Center, 0f, -4f, 150f, 12f);
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.overflowMode = TextOverflowModes.Ellipsis;
            _group.alpha = 0f;
        }

        // Stage S1: the focus frame's bar is built by the party frames, which get no events; the player's cast bar (UiCastBar.OnEvent,
        // called by every layout for every event) relays them here.
        private static readonly List<UiTargetCastBar> Relayed = new List<UiTargetCastBar>();

        /// <summary>Stage S1: this bar receives every game event through UiCastBar (the focus frame's bar).</summary>
        public void ListenToAll()
        {
            for (int i = Relayed.Count - 1; i >= 0; i--) if (Relayed[i] == null || Relayed[i].Root == null) Relayed.RemoveAt(i);
            if (!Relayed.Contains(this)) Relayed.Add(this);
        }

        internal static void Relay(GameEvent e, float now)
        {
            for (int i = Relayed.Count - 1; i >= 0; i--)
            {
                UiTargetCastBar b = Relayed[i];
                if (b == null || b.Root == null) { Relayed.RemoveAt(i); continue; }
                b.OnEvent(e, now);
            }
        }

        public void OnEvent(GameEvent e, float now)
        {
            if (_guid == 0 || e.SourceGuid != _guid) return;
            if ((e.Type == GameEventType.CastFailed && e.SpellId == _spell) || (e.Type == GameEventType.ChannelEnded && !string.IsNullOrEmpty(e.Text)))
            {
                _failReason = string.IsNullOrEmpty(e.Text) ? "Interrupted" : e.Text;
                _failSpell = _spell;
                _failGuid = _guid;
                _failAt = now;
            }
        }

        public void Refresh(IWowSession s, float now)
        {
            RefreshFor(s, s.TargetGuid, now);
        }

        /// <summary>Stage S1: the same bar for another unit (the focus frame's spell bar: TargetFrame_CreateSpellbar(FocusFrame, ...)).</summary>
        public void RefreshFor(IWowSession s, ulong unit, float now)
        {
            EntityState e = unit != 0 ? s.Find(unit) : null;
            ulong guid = e != null ? e.Guid : 0;
            if (guid != _guid)
            {
                _guid = guid;
                _casting = false;
                _endAt = -1f;
                _group.alpha = 0f;
            }
            if (e == null) return;
            bool auras = e.Auras != null && e.Auras.Count > 0;
            if (auras != _auras)
            {
                _auras = auras;
                UiKit.Move(Root, auras ? 27f : 25f, auras ? 132f : 93f);
            }
            if (e.CastingSpellId != 0 && !e.Dead)
            {
                if (!_casting || e.CastingSpellId != _spell)
                {
                    _casting = true;
                    _spell = e.CastingSpellId;
                    _channel = e.Channeling;
                    SpellInfo sp = Spell(s, _spell);
                    _text.text = sp != null ? sp.Name : "";
                    _text.color = Color.white;
                    Texture2D icon = sp != null && !string.IsNullOrEmpty(sp.IconPath) ? UiArt.Icon(sp.IconPath) : null;
                    _icon.texture = icon;
                    _icon.enabled = icon != null;
                    _iconPath = icon == null && sp != null ? sp.IconPath : null;
                    _bar.SetColor(_channel ? new Color(0f, 1f, 0f) : UiArt.CastYellow);
                    _spark.enabled = true;
                    _group.alpha = 1f;
                    _endAt = -1f;
                    _shown = -1f;
                }
                if (_iconPath != null)
                {
                    // Icons load on a pool thread: ask again until it is there.
                    Texture2D icon = UiArt.Icon(_iconPath);
                    if (icon != null) { _icon.texture = icon; _icon.enabled = true; _iconPath = null; }
                }
                float frac = Mathf.Clamp01(e.CastProgress);
                if (_channel) frac = 1f - frac;
                if (Mathf.Abs(frac - _shown) > 0.002f)
                {
                    _shown = frac;
                    _bar.Set(frac);
                    UiKit.Move(_spark.rectTransform, 150f * frac - 16f, -11f);
                }
                return;
            }
            if (_casting)
            {
                _casting = false;
                _spark.enabled = false;
                bool failed = now - _failAt < 0.4f && _failSpell == _spell && _failGuid == _guid;
                _bar.Set(1f);
                if (failed)
                {
                    _bar.SetColor(new Color(1f, 0f, 0f));
                    _text.text = _failReason;
                    _endAt = now + 1f;
                }
                else
                {
                    _bar.SetColor(new Color(0f, 1f, 0f));
                    _endAt = now + 0.5f;
                }
            }
            if (_endAt > 0f)
            {
                float left = _endAt - now;
                if (left <= 0f) { _endAt = -1f; _group.alpha = 0f; }
                else _group.alpha = Mathf.Clamp01(left / 0.5f);
            }
        }

        private string _iconPath;

        private static SpellInfo Spell(IWowSession s, int id)
        {
            var os = s as OfflineSession;
            try { return os != null && os.Spells != null ? os.Spells.Find(id) : null; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// The VR floating HUD (WowSettings.VrHud): a small canvas drawn over everything, head-locked at the top left of the view with a
    /// gentle lazy follow (the position rides with the head every frame so it never swims; only the direction lags, upright - no
    /// roll). Player frame (class circle, name, level, health, power, buffs), target frame (reaction, level, combo points, auras) and
    /// the target's cast bar, the pet frame and totems, the player's cast bar (with the immersive held state), up to four party
    /// frames, and the error / info messages.
    /// Stage S1: the focus frame (UiFocusFrame, next to the party frames as on the desktop), and the party / focus frames can be
    /// pointed at with the client's laser + trigger (target; the focus frame targets the focus) - only those two regions are
    /// pointable (UiVrLayout.ExtraSurfaces), so the rest of the HUD never stops the ray from reaching the world, and they draw no
    /// pointer dot of their own (the frame lights up instead). Settings > HUD: size (VrHudScale, around the HUD's centre) and
    /// position (VrHudOffsetX / Y, metres in the HUD's own plane); the follow (direction lag, no roll) is unchanged.
    /// </summary>
    public sealed class UiVrHud
    {
        public const float W = 510f, H = 520f;
        public float MetresPerUnit = 0.0007f;
        public float Distance = 1f;
        public float Yaw = -17f;
        public float Pitch = 14f;
        public float Follow = 5f;

        public UiSurface Surface;
        public UiPlayerFrame Player;
        public UiTargetFrame Target;
        public UiTargetCastBar TargetCast;
        public UiCastBar Cast;
        public UiMirrorTimers Mirror;        // lead 2026-10-26: breath / fatigue
        public UiPetFrame Pet;
        public UiTotemBar Totems;
        public UiParty Party;
        public UiMessages Messages;
        /// <summary>Stage S1: the pointable regions of the HUD (party frames; focus frame).</summary>
        public UiSurface PartyRegion, FocusRegion;
        private Quaternion _smooth;
        private bool _hasSmooth, _logged;
        private float _scale = -1f, _offX = float.NaN, _offY = float.NaN;

        // The first party frame (PartyMemberFrame1 as on the desktop, 18 units lower for the HUD's cast bar) and the regions (ours:
        // the frames plus a few units of margin; the focus region includes its "Focus" label and debuff row).
        private const float PartyX = 10f, PartyY = 178f, PartyStep = 83f, PartyDx = -3f;

        /// <summary>Builds the canvas (UiKit.Over must be on: always-on-top materials).</summary>
        public void Build(Transform parent, UiContext ctx)
        {
            Surface = UiSurface.MakeWorld("WoW HUD (VR)", parent, W, H, MetresPerUnit, true, 205);
            UiKit.Surface = Surface;
            Transform c = Surface.Content;
            Player = new UiPlayerFrame(c, 0f, 0f, true);
            Target = new UiTargetFrame(c, 269f, 0f);
            TargetCast = new UiTargetCastBar(Target.Root);
            // PetFrame at PlayerFrame TOPLEFT (80, -60), TotemFrame at PlayerFrame BOTTOMLEFT (99, 38) - as on the desktop.
            Pet = new UiPetFrame(c, ctx, 80f, 60f);
            Totems = new UiTotemBar(c, ctx, 99f, 62f);
            RectTransform castHolder = UiKit.Rect(c, "Cast holder", 0f, 0f, W, H);
            castHolder.gameObject.AddComponent<Canvas>();
            Cast = new UiCastBar(castHolder, 20f, 144f);
            Mirror = new UiMirrorTimers(castHolder, 14.5f, 114f);
            PartyRegion = Region("WoW HUD party frames (VR)", c, 0f, PartyY - 6f, 136f, (UiParty.Max - 1) * PartyStep + 53f + 10f);
            float fx = PartyX + UiParty.FocusDx, fy = PartyY + UiParty.FocusDy, fs = UiFocusFrame.Scale;
            FocusRegion = Region("WoW HUD focus frame (VR)", c, fx - 4f, fy - 16f * fs, 232f * fs + 8f, (100f + 20f + 16f) * fs);
            UiKit.Surface = PartyRegion;
            try { Party = new UiParty(c, PartyX, PartyY, PartyDx, PartyStep, FocusRegion); }
            finally { UiKit.Surface = Surface; }
            Messages = new UiMessages(c, 262f, 150f, 248f, 3, 12f);
            // Iteration 11 (social H6): the new-mail mark (ours: INV_Letter_15 next to the player frame), shown while mail is unread.
            MailMark = UiKit.Pic(c, "Mail", null, 243f, 34f, 18f, 18f);
            MailMark.enabled = false;
            try { UiRunes.AttachVr(Player.Root); }   // iteration 11 (spellfx): the death knight's runes under the HUD's player frame
            catch (Exception e) { WowLog.Error("UI: VR rune frame failed to attach: " + e); }
            Surface.Finish();
            Surface.SetShown(false);
            List<UiSurface> extra = UiVrLayout.ExtraSurfaces;
            for (int i = extra.Count - 1; i >= 0; i--) if (extra[i] == null || extra[i].Canvas == null) extra.RemoveAt(i);   // left from an earlier visit
            extra.Add(PartyRegion);
            extra.Add(FocusRegion);
            WowLog.Info("UI: VR HUD party frames (" + PartyRegion.Buttons.Count + " buttons) and focus frame (" + FocusRegion.Buttons.Count + ") can be pointed at with the laser + trigger.");
        }

        /// <summary>A pointable part of the HUD canvas: its own rectangle for the ray, the HUD's canvas (shown / hidden with it), no tooltip, no dot.</summary>
        private UiSurface Region(string name, Transform content, float x, float y, float w, float h)
        {
            RectTransform r = UiKit.Rect(content, name, x, y, w, h);
            return new UiSurface { Name = name, IsWorld = true, IsArm = true, Width = w, Height = h, Canvas = Surface.Canvas, Root = r, Content = r };
        }

        /// <summary>Refreshes everything on the HUD (only while it is shown).</summary>
        public void Refresh(UiContext ctx, float now)
        {
            IWowSession s = ctx.S;
            PlayerState p = s.Player;
            Player.Refresh(p);
            Target.Refresh(s, p.Class == WowClass.Rogue || p.Class == WowClass.Druid);
            TargetCast.Refresh(s, now);
            Cast.Refresh(ctx, now);
            if (Mirror != null) Mirror.Refresh(p);
            bool pet = Pet.Refresh(s);
            bool totems = Totems.Refresh(s);
            Player.ShiftBuffs(pet || totems ? 20f : 0f);
            Party.Refresh(s);
            RefreshMail(s);
            // Messages: refreshed by the layout (it is the layout's Messages while the HUD takes them).
        }

        /// <summary>Iteration 11 (social H6): the new-mail mark, shown while SocialState.HasUnreadMail.</summary>
        public RawImage MailMark;

        private void RefreshMail(IWowSession s)
        {
            if (MailMark == null) return;
            bool unread = false;
            try { unread = s.Social != null && s.Social.State != null && s.Social.State.HasUnreadMail; }
            catch (Exception) { unread = false; }
            if (unread && MailMark.texture == null)
            {
                Texture2D t = UiArt.Icon(@"Interface\Icons\INV_Letter_15");
                if (t == null) unread = false;   // still loading: next frame
                else { MailMark.texture = t; MailMark.color = Color.white; }
            }
            if (MailMark.enabled != unread) MailMark.enabled = unread;
        }

        public void OnEvent(GameEvent e, float now)
        {
            Cast.OnEvent(e, now);
            TargetCast.OnEvent(e, now);
        }

        /// <summary>
        /// Head-locked with a lazy follow: the direction eases towards where the head looks (1 - e^(-Follow dt) per frame), the
        /// position is recomputed from the current head position every call (LateUpdate and just before rendering).
        /// </summary>
        public void Place(Transform head, float dt, bool beforeRender)
        {
            if (head == null) return;
            Vector3 f = head.forward;
            Quaternion look = Mathf.Abs(f.y) < 0.97f ? Quaternion.LookRotation(f, Vector3.up) : (_hasSmooth ? _smooth : Quaternion.LookRotation(new Vector3(f.x, 0f, f.z).sqrMagnitude > 1e-6f ? new Vector3(f.x, 0f, f.z) : Vector3.forward, Vector3.up));
            if (!_hasSmooth) { _smooth = look; _hasSmooth = true; }
            else if (!beforeRender) _smooth = Quaternion.Slerp(_smooth, look, 1f - Mathf.Exp(-Follow * Mathf.Max(0f, dt)));
            Quaternion rot = _smooth * Quaternion.Euler(-Pitch, Yaw, 0f);
            // Stage S1 (Settings > HUD): the size and a shift in the HUD's own plane (right / up), read every call, applied on change.
            float scale = Mathf.Clamp(WowSettings.VrHudScale, WowSettings.HudScaleMin, WowSettings.HudScaleMax);
            float ox = Mathf.Clamp(WowSettings.VrHudOffsetX, -WowSettings.VrHudOffsetMax, WowSettings.VrHudOffsetMax);
            float oy = Mathf.Clamp(WowSettings.VrHudOffsetY, -WowSettings.VrHudOffsetMax, WowSettings.VrHudOffsetMax);
            if (scale != _scale || ox != _offX || oy != _offY)
            {
                bool first = _scale < 0f;
                _scale = scale; _offX = ox; _offY = oy;
                Surface.Root.localScale = Vector3.one * (MetresPerUnit * scale);
                if (!first || scale != 1f || ox != 0f || oy != 0f)
                    WowLog.Info("UI: VR HUD size " + scale.ToString("0.00") + "x, moved " + ox.ToString("+0.00;-0.00;0.00") + " m right and " + oy.ToString("+0.00;-0.00;0.00") + " m up (Settings > HUD).");
            }
            Vector3 pos = head.position + rot * new Vector3(ox, oy, Distance);
            Surface.Root.SetPositionAndRotation(pos, rot);
            if (!_logged && !beforeRender)
            {
                _logged = true;
                WowLog.Info("UI: VR floating HUD at " + Distance.ToString("0.00") + " m, " + (-Yaw).ToString("0") + " degrees left and " + Pitch.ToString("0") + " degrees up of the view ("
                    + (W * MetresPerUnit).ToString("0.00") + " x " + (H * MetresPerUnit).ToString("0.00") + " m, lazy follow " + Follow.ToString("0.0") + "/s).");
            }
        }

        /// <summary>Snaps the lazy direction to the head (after the HUD was hidden).</summary>
        public void ResetFollow() { _hasSmooth = false; }
    }

    /// <summary>
    /// The VR palm menu: when the LEFT palm faces the head (Body.LeftHand is the client's LeftHandAnchor = the OpenXR grip pose, whose
    /// +X is the palm normal - "forward from left palm", reports2/gear.md) for Dwell seconds, a small row of WoW micro buttons appears
    /// above the palm facing the player: Character, Spellbook, Quest Log, Settings (UI-MicroButton-MainMenu) and the backpack. It
    /// hides when the palm has turned away for HideDelay seconds (hysteresis Enter / Exit on the facing), never while the pointer is
    /// on it. Poke or ray + trigger (UiPointer) opens the windows.
    /// </summary>
    public sealed class UiPalmMenu
    {
        public const float W = 319f, H = 158f;  // lead 2026-10-04: +18 for the XP bar; iteration 5: +32 for the World Map button (Map owner); iteration 7: +31 for the Mounts button;
                                                // stage S1 (integrator): +66 for the Chat (32 + 3) and Party (28 + 3) buttons;
                                                // iteration 11 (uicore H7): two rows - WoW's micro buttons on top, ours below (+62)
        public const float Row2 = 62f;          // ours: row 2 sits 62 units below row 1 (40-unit buttons + their 18-unit art + 4)
        public float MetresPerUnit = 0.0009f;
        public float Enter = 0.55f, Exit = 0.3f, Dwell = 0.2f, HideDelay = 0.3f, MaxReach = 0.9f;
        public float Out = 0.06f, Up = 0.106f;   // iteration 11: Up 0.07 + half the 62-unit second row (0.028 m), so the lower edge stays where it was (ours);
                                                 // lead 2026-10-04: + half the 18-unit XP strip (0.008 m)
        public const float XpY = 138f;          // lead 2026-10-04 (the user: "place experience bar on palm menu in vr"): MainMenuExpBar under row 2 (ours)
        public UiXpBar Xp;

        public UiSurface Surface;
        public UiMicroMenu Micro;   // iteration 11 (uicore H7): row 1 = the registry's micro buttons (was UiMicroBar)
        public bool Open { get; private set; }
        public float Facing { get; private set; }
        private float _since;
        private int _opened;

        public void Build(Transform parent, UiContext ctx)
        {
            Surface = UiSurface.MakeWorld("WoW Palm Menu", parent, W, H, MetresPerUnit, true, 206);
            UiKit.Surface = Surface;
            Transform c = Surface.Content;
            UiKit.Solid(c, "Background", new Color(0f, 0f, 0f, 0.65f), 5f, 20f, W - 10f, H - 24f);
            UiBackdrop edge = UiBackdrop.Make(c, "Border", null, UiArt.TooltipBorder, 16f, 5f, 16f, Color.white);
            edge.Place(0f, 16f, W, H - 16f);
            edge.SetEdgeColor(new Color(0.8f, 0.8f, 0.8f, 1f));
            // Iteration 11 (uicore H7): row 1 = WoW's micro buttons from the registry (Character ... Game Menu, the shown ones packed,
            // art 28x58, the lower 40 units are the button). Row 2 (62 lower) = the World Map, Mounts, Chat and Party buttons where they
            // were, + Settings and the backpack 37x37 (iteration 7 / stage S1 x positions kept).
            Micro = UiMicroMenu.BuildPalmRow(c, ctx, 14f, 10f, 31f);
            // Iteration 5 (Map owner): World Map (Interface\Buttons\UI-MicroButton-World-Up/-Down.blp, 32x64 like the other micro
            // buttons) toggles the WoW Map window above the palm (WowTabletMap).
            UiButton map = UiKit.Button(c, "Micro World Map", 138f, 10f + 18f + Row2, 28f, 40f);
            map.Art = UiKit.Pic(map.transform, "Up", UiArt.Tex(WowTabletMap.MicroWorldUp), 0f, -18f, 28f, 58f);
            map.ArtUp = UiArt.Tex(WowTabletMap.MicroWorldUp);
            map.Pushed = UiKit.Pic(map.transform, "Down", UiArt.Tex(WowTabletMap.MicroWorldDown), 0f, -18f, 28f, 58f);
            map.Pushed.enabled = false;
            map.Hilite = UiKit.Pic(map.transform, "Highlight", UiArt.Add(UiArt.MicroHilight), 0f, -18f, 28f, 58f);
            map.Hilite.enabled = false;
            map.Tip = () => "<size=14>World Map</size>";
            map.Click = () => WowTabletMap.TogglePalmWindow(Surface != null ? Surface.Root : null);
            // Iteration 7 (PLAN7 s5.6, a Dreamport addition - WoW has no micro button for mounts): the Mounts window, icon
            // Interface\Icons\Ability_Mount_RidingHorse (Brown Horse's SpellIcon) 30x30 in the micro buttons' face row, the
            // CheckButtonHilight while the window is open (the backpack's pattern).
            _ctx = ctx;
            _mounts = UiKit.Slot(c, "Micro Mounts", 169f, 10f + 18f + 5f + Row2, 30f, false);
            _mounts.Icon.enabled = false;
            _mounts.Checked = UiKit.Pic(_mounts.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 30f, 30f);
            _mounts.Checked.enabled = false;
            _mounts.Tip = () => "<size=14>Mounts</size>";
            _mounts.Click = () => { if (_ctx != null && _ctx.Toggle != null) _ctx.Toggle(UiWindowKind.Mounts); };
            // Stage S1 (integrator, reports10/chat.md request 2): Chat - WoW's ChatFrameMenuButton art (UiChat.ChatButtonUp / -Down,
            // 32x32, UI-Common-MouseHilight ADD; FloatingChatFrame.xml 905-926) opens / closes the VR chat window above the palm.
            // Checked (CheckButtonHilight) while the window is open; the gold number = lines that came while it was closed (UiChat.Unread).
            _chat = UiKit.ArtButton(c, "Micro Chat", UiChat.ChatButtonUp, UiChat.ChatButtonDown, null, UiChat.ChatButtonHilite, 202f, 32f + Row2, 32f,
                () => UiChat.TogglePalmWindow(Surface != null ? Surface.Root : null));
            _chat.Checked = UiKit.Pic(_chat.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 32f, 32f);
            _chat.Checked.enabled = false;
            _chatBadge = UiKit.Text(_chat.transform, "Unread", "", 11f, UiArt.Gold, TextAlignmentOptions.TopRight, -6f, -6f, 40f, 14f);
            _chat.Tip = () => "<size=14>Chat</size>";   // CHAT (GlobalStrings.lua 1385)
            // Stage S1 (integrator, reports10/party.md request 3): Party - a Dreamport addition for VR (no right-click there):
            // UiGroup.OpenPartyMenu = Invite <target> / Leave party. Art: WoW's Social micro button (UI-MicroButton-Socials-Up / -Down,
            // MainMenuBarMicroButtons.xml:180-190), because WoW keeps its group page in the Social window (FriendsFrame.xml:4818,
            // FriendsFrameTab5 text="RAID"). 28x58 like the World Map button.
            UiButton party = UiKit.Button(c, "Micro Party", 237f, 10f + 18f + Row2, 28f, 40f);
            party.Art = UiKit.Pic(party.transform, "Up", UiArt.Tex(SocialsUp), 0f, -18f, 28f, 58f);
            party.ArtUp = UiArt.Tex(SocialsUp);
            party.Pushed = UiKit.Pic(party.transform, "Down", UiArt.Tex(SocialsDown), 0f, -18f, 28f, 58f);
            party.Pushed.enabled = false;
            party.Hilite = UiKit.Pic(party.transform, "Highlight", UiArt.Add(UiArt.MicroHilight), 0f, -18f, 28f, 58f);
            party.Hilite.enabled = false;
            party.Tip = () => "<size=14>Party</size>";   // PARTY (GlobalStrings.lua 5572)
            party.Click = () => UiGroup.OpenPartyMenu();
            // Iteration 11 (uicore H7): Settings (ours: the Trade_Engineering icon in a 30x30 slot - WoW opens its options from the Game
            // Menu, which is on row 1 too) and the backpack (37x37, Interface\Buttons\Button-Backpack-Up, its old place on row 2).
            _settings = UiKit.Slot(c, "Micro Settings", 14f, 10f + 18f + 5f + Row2, 30f, false);
            _settings.Icon.enabled = false;
            _settings.Checked = UiKit.Pic(_settings.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 30f, 30f);
            _settings.Checked.enabled = false;
            _settings.Tip = () => "<size=14>Settings</size>";   // ours (the window's own title)
            _settings.Click = () => { if (_ctx != null && _ctx.Toggle != null) _ctx.Toggle(UiWindowKind.Settings); };
            _bags = UiKit.Button(c, "Micro Backpack", 273f, 29f + Row2, 37f, 37f);
            _bags.Art = UiKit.Pic(_bags.transform, "Icon", UiArt.Tex(UiArt.Backpack), 0f, 0f, 37f, 37f);
            _bags.Hilite = UiKit.Pic(_bags.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 37f, 37f);
            _bags.Hilite.enabled = false;
            _bags.Checked = UiKit.Pic(_bags.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 37f, 37f);
            _bags.Checked.enabled = false;
            _bags.Tip = () => "<size=14>" + Pure.UiGlobalStrings.G("BACKPACK_TOOLTIP", "Backpack") + "</size>";   // GlobalStrings.lua:670
            _bags.Click = () => { if (_ctx != null && _ctx.Toggle != null) _ctx.Toggle(UiWindowKind.Bags); };
            Xp = new UiXpBar(c, 12f, XpY, W - 24f);   // lead 2026-10-04: WoW's experience bar (UiXpBar: MainMenuExpBar's purple + overlay), "XP n / m"
            Surface.Finish();
            Surface.SetShown(false);
            WowLog.Info("UI: palm menu " + W + " x " + H + " units (" + (W * MetresPerUnit).ToString("0.000") + " m wide): row 1 " + Micro.Describe()
                + "; row 2 Settings, World Map, Mounts, Chat, Party, Backpack; the experience bar under them.");
        }

        public const string SettingsIcon = @"Interface\Icons\Trade_Engineering";   // ours
        private UiButton _settings, _bags;
        private bool _settingsIcon, _settingsOpen, _bagsOpen;

        /// <summary>WoW's Social micro button (LoadMicroButtonTextures(self, "Socials")) - the palm menu's Party button (stage S1).</summary>
        public const string SocialsUp = @"Interface\Buttons\UI-MicroButton-Socials-Up.blp", SocialsDown = @"Interface\Buttons\UI-MicroButton-Socials-Down.blp";
        private UiButton _chat;
        private TextMeshProUGUI _chatBadge;
        private bool _chatOpen;
        private int _chatUnread = -1;

        private UiContext _ctx;
        private UiButton _mounts;
        private bool _mountsIcon, _mountsOpen;

        /// <summary>Iteration 7: the Mounts button's icon (loads over a few frames) and its open state (UiVrLayout.Refresh).</summary>
        public void RefreshExtra()
        {
            if (_mounts == null) return;
            if (!_mountsIcon)
            {
                Texture2D t = UiArt.Icon(UiMounts.MountsIcon);
                if (t != null) { _mounts.Icon.texture = t; _mounts.Icon.color = Color.white; _mounts.Icon.enabled = true; _mountsIcon = true; }
            }
            bool open = _ctx != null && _ctx.IsOpen != null && _ctx.IsOpen(UiWindowKind.Mounts);
            if (open != _mountsOpen) { _mountsOpen = open; _mounts.Checked.enabled = open && _mounts.Checked.texture != null; }
            // Iteration 11 (uicore H7): Settings and the backpack on row 2.
            if (_settings != null)
            {
                if (!_settingsIcon)
                {
                    Texture2D t = UiArt.Icon(SettingsIcon);
                    if (t != null) { _settings.Icon.texture = t; _settings.Icon.color = Color.white; _settings.Icon.enabled = true; _settingsIcon = true; }
                }
                bool so = _ctx != null && _ctx.IsOpen != null && _ctx.IsOpen(UiWindowKind.Settings);
                if (so != _settingsOpen) { _settingsOpen = so; _settings.Checked.enabled = so && _settings.Checked.texture != null; }
            }
            if (_bags != null)
            {
                bool bo = _ctx != null && _ctx.IsOpen != null && _ctx.IsOpen(UiWindowKind.Bags);
                if (bo != _bagsOpen) { _bagsOpen = bo; _bags.Checked.enabled = bo && _bags.Checked.texture != null; }
            }
            // Stage S1: the Chat button's open state and its unread count.
            if (_chat != null)
            {
                bool chat = UiChat.VrOpen;
                if (chat != _chatOpen) { _chatOpen = chat; _chat.Checked.enabled = chat && _chat.Checked.texture != null; }
                int unread = chat ? 0 : UiChat.Unread;
                if (unread != _chatUnread && _chatBadge != null)
                {
                    _chatUnread = unread;
                    _chatBadge.text = unread <= 0 ? "" : unread > 99 ? "99+" : unread.ToString();
                }
            }
        }

        /// <summary>The palm gesture (LateUpdate). keep = the pointer is on the menu (it does not hide then).</summary>
        public bool Step(Transform hand, Transform head, float dt, bool keep)
        {
            bool want;
            if (hand == null || head == null) { want = false; Facing = -1f; }
            else
            {
                Vector3 toHead = head.position - hand.position;
                float dist = toHead.magnitude;
                Facing = dist > 1e-3f ? Vector3.Dot(hand.right, toHead / dist) : -1f;
                want = Open ? (Facing > Exit || keep) : (Facing > Enter && dist < MaxReach);
            }
            if (want != Open)
            {
                _since += dt;
                if (_since >= (Open ? HideDelay : Dwell) || hand == null)
                {
                    Open = want;
                    _since = 0f;
                    if (Open && _opened++ < 3) WowLog.Info("UI: palm menu open (palm facing " + Facing.ToString("0.00") + ").");
                }
            }
            else _since = 0f;
            Surface.SetShown(Open);
            return Open;
        }

        /// <summary>Above the palm, facing the head (LateUpdate and just before rendering).</summary>
        public void Place(Transform hand, Transform head)
        {
            if (!Open || hand == null || head == null) return;
            Vector3 pos = hand.position + hand.right * Out + Vector3.up * Up;
            Vector3 look = pos - head.position;
            if (look.sqrMagnitude < 1e-6f) look = head.forward;
            Surface.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
        }
    }
}
