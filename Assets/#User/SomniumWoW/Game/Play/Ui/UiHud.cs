using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>A row of aura icons (buffs on the player, debuffs on the target), with the time left.</summary>
    public sealed class UiAuraRow
    {
        private readonly UiButton[] _icons;
        private readonly TextMeshProUGUI[] _times;
        private readonly string[] _paths;
        private readonly int[] _shownSecs;
        private List<AuraView> _list;

        /// <summary>Iteration 7 (PLAN7 s5.6): right-click on a buff (not a harmful aura) calls this with its spell id (the desktop player
        /// frame: IWowSession.CancelAura, as WoW's BuffFrame right-click). Null = right-click does nothing.</summary>
        public System.Action<int> Cancel;

        /// <summary>Iteration 7: the spell id of the ridden mount (its buff tooltip says right-click dismounts), 0 = none.</summary>
        public int MountSpell;

        private void CancelAt(int i)
        {
            if (Cancel == null || _list == null || i >= _list.Count || _list[i] == null) return;
            AuraView a = _list[i];
            if (a.Harmful || a.SpellId <= 0) return;   // 0 = the offline food / drink; below 0 = the Dreamport's own buffs (stage S1: "Immersive Mode") - nothing to cancel
            WowLog.Info("UI: buff '" + (a.Name ?? "?") + "' right-clicked - CancelAura(" + a.SpellId + ")" + (a.SpellId == MountSpell ? " (the mount aura: dismount)" : "") + ".");
            Cancel(a.SpellId);
        }
        private readonly float _x0, _y0, _size;
        private float _shift;

        /// <summary>Moves the whole row down by dy units (the desktop pet frame / totems take its place under the player frame).</summary>
        public void Shift(float dy)
        {
            if (dy == _shift) return;
            _shift = dy;
            for (int i = 0; i < _icons.Length; i++) UiKit.Move(_icons[i].Rt, _x0 + i * (_size + 3f), _y0 + dy);
        }

        public UiAuraRow(Transform parent, string name, float x, float y, float size, int count)
        {
            _icons = new UiButton[count];
            _times = new TextMeshProUGUI[count];
            _paths = new string[count];
            _shownSecs = new int[count];
            _x0 = x; _y0 = y; _size = size;
            for (int i = 0; i < count; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(parent, name + " " + (i + 1), x + i * (size + 3f), y, size, size);
                b.Icon = UiKit.Pic(b.transform, "Icon", null, 0f, 0f, size, size);
                b.Icon.enabled = false;
                _times[i] = UiKit.Text(b.transform, "Time", "", 9f, Color.white, TextAlignmentOptions.Top, -6f, size, size + 12f, 11f, UiArt.Numbers);
                b.Tip = () => AuraTip(k);
                b.RightClick = () => CancelAt(k);
                b.gameObject.SetActive(false);
                _icons[i] = b;
                _shownSecs[i] = -2;
            }
        }

        private string AuraTip(int i)
        {
            if (_list == null || i >= _list.Count || _list[i] == null) return null;
            AuraView a = _list[i];
            string name = string.IsNullOrEmpty(a.Name) ? "Aura" : a.Name;
            string head = "<size=14><color=" + (a.Harmful ? "#ff2020" : "#ffffff") + ">" + name + "</color></size>";
            string tip = a.Duration > 0f ? head + "\n" + Mathf.CeilToInt(a.Remaining) + " sec remaining" : head;
            if (!string.IsNullOrEmpty(a.Tooltip)) tip += "\n" + a.Tooltip;   // stage S1 (integrator): the Dreamport's own buff text ("Immersive Mode")            if (Cancel != null && !a.Harmful && a.SpellId != 0 && a.SpellId == MountSpell) tip += "\n<color=#00ff00>Right-click to dismount.</color>";   // ours (iteration 7)
            return tip;
        }

        public void Refresh(List<AuraView> auras)
        {
            _list = auras;
            int n = auras != null ? auras.Count : 0;
            for (int i = 0; i < _icons.Length; i++)
            {
                UiButton b = _icons[i];
                bool show = i < n && auras[i] != null;
                if (b.gameObject.activeSelf != show) b.gameObject.SetActive(show);
                if (!show) continue;
                AuraView a = auras[i];
                string path = string.IsNullOrEmpty(a.IconPath) ? (a.SpellId == 0 ? @"Interface\Icons\INV_Misc_Food_11" : null) : a.IconPath;
                if (!ReferenceEquals(path, _paths[i]) || !b.Icon.enabled)
                {
                    Texture2D t = UiArt.Icon(path);
                    if (t != null) { b.Icon.texture = t; b.Icon.color = Color.white; b.Icon.enabled = true; _paths[i] = path; }
                }
                int secs = a.Duration > 0f ? Mathf.CeilToInt(a.Remaining) : -1;
                if (secs != _shownSecs[i])
                {
                    _shownSecs[i] = secs;
                    if (secs < 0) _times[i].SetText("");
                    else if (secs >= 60) _times[i].SetText("{0}m", Mathf.CeilToInt(secs / 60f));
                    else _times[i].SetText("{0}s", secs);
                }
            }
        }
    }

    /// <summary>
    /// PlayerFrame (PlayerFrame.xml): 232x100, UI-TargetingFrame mirrored (TexCoords 1, 0.09375, 0, 0.78125), class circle portrait
    /// 64x64 at (42, 12), health and power bars 119x12 at (106, 41) / (106, 52), name centred (50, 19), level centred (-63, -16),
    /// buffs underneath.
    /// </summary>
    public sealed class UiPlayerFrame
    {
        // iteration 11 (w0game, wave-0 stub for interact's hook H6; uicore draws them): the own PvP icon art path (null = none) and the
        // PvP flag timer in seconds (-1 = none), assigned by interact's UiInteract. Unused (null) in wave 0.
        public static System.Func<string> PvpIconOf;
        public static System.Func<float> PvpTimer;

        public RectTransform Root;
        private readonly UiBar _hp, _mp;
        private readonly TextMeshProUGUI _name, _level, _hpText, _mpText;
        private readonly RawImage _portrait;
        private readonly UiUnitPortrait _live;
        private readonly UiAuraRow _buffs;
        private int _h = -1, _hm = -1, _m = -1, _mm = -1, _lvl = -1, _pt = -1;
        private WowClass _cls = (WowClass)255;
        private string _nameShown;
        private bool _dead;

        public UiPlayerFrame(Transform parent, float x, float y, bool buffsBelow)
        {
            Root = UiKit.Rect(parent, "Player Frame", x, y, 232f, 100f);
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.5f), 106f, 22f, 119f, 41f);
            _portrait = UiKit.Pic(Root, "Portrait", UiArt.Tex(UiArt.ClassCircles), 42f, 12f, 64f, 64f);
            _live = UiUnitPortrait.Attach(_portrait, false);   // lead 2026-10-03: your Somnium profile picture over the class circle
            _hp =UiBar.Make(Root, "Health", 106f, 41f, 119f, 12f, UiArt.HealthGreen, false);
            _mp = UiBar.Make(Root, "Power", 106f, 52f, 119f, 12f, UiArt.PowerColor(PowerType.Mana), false);
            UiKit.Pic(Root, "Frame", UiArt.Tex(UiArt.TargetingFrame), 1f, 0.09375f, 0f, 0.78125f, 0f, 0f, 232f, 100f);
            _name = UiKit.Text(Root, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 116f, 25f, 100f, 14f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _level = UiKit.Text(Root, "Level", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 38f, 60f, 30f, 12f);
            _hpText = UiKit.Text(Root, "Health Text", "", 9f, Color.white, TextAlignmentOptions.Center, 106f, 41f, 119f, 12f);
            _mpText = UiKit.Text(Root, "Power Text", "", 9f, Color.white, TextAlignmentOptions.Center, 106f, 52f, 119f, 12f);
            _buffs = new UiAuraRow(Root, "Buff", buffsBelow ? 106f : 106f, 100f, 20f, 8);
            // Lead 2026-09-27 (PlayerFrame.xml:184-217, PlayerFrame_UpdatePartyLeader PlayerFrame.lua:62-84): the leader crown and the
            // master looter icon, 16 x 16 at TOPLEFT (40, -12) / (80, -10).
            _leader = UiKit.Pic(Root, "Leader", UiArt.Tex(@"Interface\GroupFrame\UI-Group-LeaderIcon.blp"), 40f, 12f, 16f, 16f);
            _master = UiKit.Pic(Root, "Master Looter", UiArt.Tex(@"Interface\GroupFrame\UI-Group-MasterLooter.blp"), 80f, 10f, 16f, 16f);
            _leader.raycastTarget = false;
            _master.raycastTarget = false;
            _leader.enabled = false;
            _master.enabled = false;
            // Iteration 11 (interact H6, drawn by uicore): PlayerPVPIcon 64 x 64 at TOPLEFT (18, -20) (PlayerFrame.xml:161-171) and
            // PlayerPVPTimerText (GameFontNormalSmall, CENTER at TOPLEFT (38, -8), :107-115) - ours: a 40 x 12 centred text at (18, 2), "m:ss".
            _pvpIcon = UiKit.Pic(Root, "PvP Icon", null, 18f, 20f, 64f, 64f);
            _pvpIcon.raycastTarget = false;
            _pvpIcon.enabled = false;
            _pvpTimer = UiKit.Text(Root, "PvP Timer", "", 9f, UiArt.Gold, TextAlignmentOptions.Center, 18f, 2f, 40f, 12f);
            _pvpTimer.enabled = false;
            // The unit button over the frame's body: click = target yourself (PlayerFrame's TargetUnit("player")), right-click = the SELF
            // unit menu (UiTargetFrame.ContextClick = UiGroup.OpenUnitMenu).
            _unit = UiKit.Button(Root, "Unit", 36f, 6f, 190f, 72f);
            _unit.Click = () => { IWowSession s = WowPlay.Session; if (s != null && s.Player != null) s.SetTarget(s.Player.Guid); };
            _unit.RightClick = () =>
            {
                IWowSession s = WowPlay.Session;
                System.Action<ulong> h = UiTargetFrame.ContextClick;
                if (h != null && s != null && s.Player != null) h(s.Player.Guid);
            };
        }

        private readonly RawImage _leader, _master;
        private readonly UiButton _unit;

        public void Refresh(PlayerState p)
        {
            if (!ReferenceEquals(p.Name, _nameShown)) { _nameShown = p.Name; _name.text = p.Name ?? ""; }
            if (p.Class != _cls)
            {
                _cls = p.Class;
                Vector4 c = UiArt.ClassCoords(p.Class);
                UiKit.Coords(_portrait, c.x, c.y, c.z, c.w);
            }
            if (p.Level != _lvl) { _lvl = p.Level; _level.SetText("{0}", p.Level); }
            if (p.Health != _h || p.MaxHealth != _hm)
            {
                _h = p.Health; _hm = p.MaxHealth;
                _hp.Set(_hm > 0 ? (float)_h / _hm : 0f);
                _hpText.SetText("{0} / {1}", _h, _hm);
            }
            if ((int)p.Power != _pt) { _pt = (int)p.Power; _mp.SetColor(UiArt.PowerColor(p.Power)); _m = -1; }
            if (p.PowerValue != _m || p.PowerMax != _mm)
            {
                _m = p.PowerValue; _mm = p.PowerMax;
                _mp.Set(_mm > 0 ? (float)_m / _mm : 0f);
                _mpText.SetText("{0} / {1}", _m, _mm);
            }
            if (p.Dead != _dead) { _dead = p.Dead; _portrait.color = p.Dead ? new Color(0.35f, 0.35f, 0.35f) : Color.white; }
            _live.ShowOwn();
            bool leader = p.Group.Count > 0 && p.GroupLeaderGuid == p.Guid, master = p.Group.Count > 0 && p.GroupLootMethod == 2 && p.GroupMasterLooterGuid == p.Guid;
            if (_leader.enabled != leader) _leader.enabled = leader;
            if (_master.enabled != master) _master.enabled = master;
            _buffs.MountSpell = p.Mounted ? p.MountSpellId : 0;
            _buffs.Refresh(BuffRowShown ? p.Auras : null);   // lead 2026-10-04: desktop shows WoW's BuffFrame at the top right instead
            // Iteration 11 (interact H6): the own PvP icon and the PvP flag timer (PlayerFrame.lua:88-118, :413-422).
            string pv = null;
            float timer = -1f;
            try
            {
                pv = PvpIconOf != null ? PvpIconOf() : null;
                timer = PvpTimer != null ? PvpTimer() : -1f;
            }
            catch (System.Exception) { pv = null; timer = -1f; }
            if (!ReferenceEquals(pv, _pvpShown))
            {
                _pvpShown = pv;
                Texture2D t = pv != null ? UiArt.Tex(UiTargetFrame.Blp(pv)) : null;
                _pvpIcon.texture = t;
                _pvpIcon.enabled = t != null;
                if (t != null) UiSound.Named("igPVPUpdate");   // PlayerFrame.lua:88-118: the sound when the icon appears
            }
            int secs = timer >= 0f ? Mathf.FloorToInt(timer) : -1;
            if (secs != _pvpSecs)
            {
                _pvpSecs = secs;
                _pvpTimer.enabled = secs >= 0;
                if (secs >= 0) _pvpTimer.text = (secs / 60) + ":" + (secs % 60).ToString("00");
            }
        }

        private readonly RawImage _pvpIcon;
        private readonly TextMeshProUGUI _pvpTimer;
        private string _pvpShown;
        private int _pvpSecs = -2;

        /// <summary>Iteration 7 (desktop): right-click on one of the player's buffs -> cancel (IWowSession.CancelAura; the mount aura dismounts).</summary>
        public void SetBuffCancel(System.Action<int> cancel)
        {
            _buffs.Cancel = cancel;
        }

        /// <summary>Lead (2026-10-04): the auras under the player frame - the VR HUD keeps them; the desktop has UiBuffFrame.</summary>
        public bool BuffRowShown = true;

        public void ShiftBuffs(float dy)
        {
            _buffs.Shift(dy);
        }
    }

    /// <summary>
    /// TargetFrame (TargetFrame.xml): UI-TargetingFrame (TexCoords 0.09375, 1, 0, 0.78125), name background tinted by reaction
    /// (UnitSelectionColor), portrait 64x64 at TOPRIGHT (-42, -12), bars at TOPRIGHT (-106, -41) / (-106, -52), level coloured by
    /// difficulty, "Dead", combo points (ComboFrame.xml) and the target's auras underneath.
    /// Stage S1: the frame is a unit button (TargetFrameTemplate inherits SecureUnitButtonTemplate): a click on it no longer falls
    /// through to the world behind it (that cleared the target); right-click calls <see cref="ContextClick"/> (the group owner's
    /// Invite menu may use it; nothing when unset). A member of the server group the realm has not shown us (out of view) is drawn
    /// from the group's stats (UiUnits.Resolve). The focus frame (UiFocusFrame) is this frame drawn for WowFocus.
    /// </summary>
    public sealed class UiTargetFrame
    {
        // iteration 11 (w0game, wave-0 stub for interact's hook H6; uicore draws them): the raid target icon (0 none) and the PvP icon art
        // path (null none) of a unit, assigned by interact's UiInteract. Unused (null) in wave 0.
        public static System.Func<ulong, int> RaidIconOf;
        public static System.Func<ulong, string> PvpIconOf;

        /// <summary>Stage S1 hook: right-click on the TARGET frame (desktop) with the target's guid; null = nothing.</summary>
        public static System.Action<ulong> ContextClick;

        public UiButton Button;
        private readonly EntityState _scratch = new EntityState();
        public RectTransform Root;
        private readonly UiBar _hp, _mp;
        private readonly RawImage _nameBg, _portrait;
        private readonly UiUnitPortrait _live;
        private readonly TextMeshProUGUI _name, _level, _hpText, _dead;
        private readonly RawImage[] _cpSocket = new RawImage[5], _cpLit = new RawImage[5];
        private readonly UiAuraRow _debuffs;
        private ulong _guid = ulong.MaxValue;
        private int _h = -1, _hm = -1, _m = -1, _mm = -1, _lvl = -1, _con = -1, _cp = -1, _react = -1, _pt = -1;
        private bool _isDead, _cpShown;
        private string _nameShown;

        // ComboPoint1 TOPRIGHT at the ComboFrame's TOPRIGHT = TargetFrame TOPRIGHT (-44, -9); each next TOP of the previous BOTTOM
        // + (7,4), (5,2), (2,1); the fifth is placed on the same arc.
        private static readonly Vector2[] ComboPos = { new Vector2(176f, 9f), new Vector2(183f, 17f), new Vector2(188f, 27f), new Vector2(190f, 38f), new Vector2(188f, 49f) };

        public UiTargetFrame(Transform parent, float x, float y)
        {
            Root = UiKit.Rect(parent, "Target Frame", x, y, 232f, 100f);
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.5f), 7f, 22f, 119f, 41f);
            _nameBg = UiKit.Pic(Root, "Name Background", UiArt.Tex(UiArt.LevelBackground), 7f, 22f, 119f, 19f);
            _portrait = UiKit.Pic(Root, "Portrait", UiArt.Tex(UiArt.PortraitMonster), 126f, 12f, 64f, 64f);
            _live = UiUnitPortrait.Attach(_portrait, true);   // lead 2026-10-03: the unit's model rendered round (SetPortraitTexture)
            _hp =UiBar.Make(Root, "Health", 7f, 41f, 119f, 12f, UiArt.HealthGreen, false);
            _mp = UiBar.Make(Root, "Power", 7f, 52f, 119f, 12f, UiArt.PowerColor(PowerType.Mana), false);
            _frame = UiKit.Pic(Root, "Frame", UiArt.Tex(UiArt.TargetingFrame), 0.09375f, 1f, 0f, 0.78125f, 0f, 0f, 232f, 100f);
            _name = UiKit.Text(Root, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 16f, 25f, 100f, 14f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _level = UiKit.Text(Root, "Level", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 164f, 60f, 30f, 12f);
            // TargetFrameTextureFrameHighLevelTexture: UI-TargetingFrame-Skull 16 x 16, CENTER on the level text (TargetFrame.xml:298-305)
            _skull = UiKit.Pic(Root, "High Level", UiArt.Tex(SkullArt), 171f, 58f, 16f, 16f);
            _skull.enabled = false;
            _hpText = UiKit.Text(Root, "Health Text", "", 9f, Color.white, TextAlignmentOptions.Center, 7f, 41f, 119f, 12f);
            _dead = UiKit.Text(Root, "Dead", "Dead", 10f, UiArt.Gold, TextAlignmentOptions.Center, 16f, 41f, 100f, 12f);
            _dead.enabled = false;
            Texture2D cp = UiArt.Tex(UiArt.ComboPoint);
            for (int i = 0; i < 5; i++)
            {
                _cpSocket[i] = UiKit.Pic(Root, "Combo " + (i + 1), cp, 0f, 0.375f, 0f, 1f, ComboPos[i].x, ComboPos[i].y, 12f, 16f);
                _cpLit[i] = UiKit.Pic(Root, "Combo " + (i + 1) + " lit", cp, 0.375f, 0.5625f, 0f, 1f, ComboPos[i].x + 2f, ComboPos[i].y, 8f, 16f);
                _cpSocket[i].enabled = false;
                _cpLit[i].enabled = false;
            }
            _debuffs = new UiAuraRow(Root, "Debuff", 7f, 100f, 17f, 8);
            // Iteration 11 (interact H6, drawn by uicore): TargetFrameTextureFramePVPIcon 64 x 64 at TOPRIGHT (3, -20) and
            // ...RaidTargetIcon 26 x 26 CENTER at TOPRIGHT (-73, -14) of the 232 x 100 frame (TargetFrame.xml:322-345).
            _pvpIcon = UiKit.Pic(Root, "PvP Icon", null, 171f, 20f, 64f, 64f);
            _pvpIcon.raycastTarget = false;
            _pvpIcon.enabled = false;
            _raidIcon = UiKit.Pic(Root, "Raid Target Icon", UiArt.Tex(RaidIcons), 146f, 1f, 26f, 26f);
            _raidIcon.raycastTarget = false;
            _raidIcon.enabled = false;
            // Stage S1: the unit button over the frame's body (not the aura row below it, whose icons keep their tooltips).
            Button = UiKit.Button(Root, "Unit", 6f, 6f, 190f, 72f);
            Button.Click = () => { };   // it is the target already (WoW: the click targets the frame's unit)
            Button.RightClick = () =>
            {
                System.Action<ulong> h = ContextClick;
                if (h != null && _guid != ulong.MaxValue && _guid != 0) h(_guid);
            };
            Root.gameObject.SetActive(false);
        }

        public void Refresh(IWowSession s, bool usesCombo)
        {
            RefreshUnit(s, UiUnits.Resolve(s, s.TargetGuid, _scratch), usesCombo);
        }

        /// <summary>Stage S1: draws this unit (null hides the frame) - the target frame and the focus frame.</summary>
        public void RefreshUnit(IWowSession s, EntityState e, bool usesCombo)
        {
            bool show = e != null;
            if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
            if (!show) { _guid = ulong.MaxValue; return; }
            if (e.Guid != _guid)
            {
                _guid = e.Guid;
                _h = _hm = _m = _mm = _lvl = _con = _cp = _react = _pt = _cls = -1;
                _nameShown = null;
            }
            if (!ReferenceEquals(e.Name, _nameShown)) { _nameShown = e.Name; _name.text = e.Name ?? ""; }
            int react = (int)e.Reaction + (e.Dead ? 10 : 0);
            if (react != _react)
            {
                _react = react;
                // TargetFrame.lua:263-268: grey for a dead / tapped unit, else UnitSelectionColor.
                _nameBg.color = e.Dead ? new Color(0.5f, 0.5f, 0.5f) : UiArt.ReactionColor(e.Reaction);
                _portrait.color = e.Dead ? new Color(0.5f, 0.5f, 0.5f) : Color.white;
            }
            // Lead fix 2026-10-03 (the user: "it is not displaying the correct player frame picture when selecting"): the portrait is
            // the unit's model (SetPortraitTexture, UnitFrame.lua:97), a Somnium player's profile picture or their class circle -
            // no longer one fixed picture per kind (UiUnitPortrait).
            _live.ShowUnit(s, e);
            // Lead fix (2026-10-03, the user: "Elite mobs are not being displayed properly"): TargetFrame_CheckClassification (TargetFrame.lua:288-298)
            // - elite / world boss = UI-TargetingFrame-Elite (gold dragon), rare elite = -Rare-Elite, rare = -Rare (silver), else the plain frame;
            // UnitClassification from creature_template.rank (0 normal, 1 elite, 2 rareelite, 3 worldboss, 4 rare).
            int cls = e.Kind == EntityKind.Player ? 0 : e.Classification;
            if (cls != _cls)
            {
                _cls = cls;
                string art = cls == 1 || cls == 3 ? EliteArt : cls == 2 ? RareEliteArt : cls == 4 ? RareArt : UiArt.TargetingFrame;
                Texture2D t = UiArt.Tex(art);
                _frame.texture = t != null ? t : UiArt.Tex(UiArt.TargetingFrame);
            }
            // TargetFrame_CheckLevel (TargetFrame.lua:235-257): a level the player cannot judge (UnitLevel -1: a world boss, or an enemy 10+
            // levels above the player - the client's rule) shows the skull instead of the number.
            PlayerState me = s != null ? s.Player : null;
            bool skull = cls == 3 || (e.Reaction == Reaction.Hostile && me != null && e.Level >= me.Level + 10);
            if (e.Level != _lvl || e.ConColor != _con || skull != _skullShown)
            {
                _lvl = e.Level; _con = e.ConColor; _skullShown = skull;
                _level.SetText("{0}", e.Level);
                _level.color = e.Kind == EntityKind.Npc ? UiArt.Gold : UiArt.ConColor(e.ConColor);
                _level.enabled = !skull && e.Level > 0;   // rescorpse 2026-10-04: a corpse whose owner was never seen alive has no level (ours: no "0")
                _skull.enabled = skull && _skull.texture != null;
            }
            if (e.Health != _h || e.MaxHealth != _hm)
            {
                _h = e.Health; _hm = e.MaxHealth;
                _hp.Set(_hm > 0 ? (float)_h / _hm : 0f);
                if (e.Dead) _hpText.SetText("");
                else _hpText.SetText("{0} / {1}", _h, _hm);
            }
            bool hasPower = e.Power != PowerType.None && e.PowerMax > 0;
            if (_mp.Root.gameObject.activeSelf != hasPower) _mp.Root.gameObject.SetActive(hasPower);
            if (hasPower)
            {
                if ((int)e.Power != _pt) { _pt = (int)e.Power; _mp.SetColor(UiArt.PowerColor(e.Power)); }
                if (e.PowerValue != _m || e.PowerMax != _mm) { _m = e.PowerValue; _mm = e.PowerMax; _mp.Set((float)_m / _mm); }
            }
            if (e.Dead != _isDead) { _isDead = e.Dead; _dead.enabled = e.Dead; }
            bool cpShown = usesCombo && !e.Dead && e.Reaction != Reaction.Friendly;
            int cp = cpShown ? Mathf.Clamp(e.ComboPoints, 0, 5) : 0;
            if (cp != _cp || cpShown != _cpShown)
            {
                _cp = cp;
                _cpShown = cpShown;
                for (int i = 0; i < 5; i++)
                {
                    _cpSocket[i].enabled = cpShown && cp > 0;
                    _cpLit[i].enabled = cpShown && i < cp;
                }
            }
            _debuffs.Refresh(e.Auras);
            // Iteration 11 (interact H6): the raid target icon (SetRaidTargetIconTexture: 4 x 4 cells of 0.25, TargetFrame.lua:692-701)
            // and the PvP icon (TargetFrame_CheckFaction).
            int ri = 0;
            string pv = null;
            try
            {
                ri = RaidIconOf != null ? RaidIconOf(e.Guid) : 0;
                pv = PvpIconOf != null ? PvpIconOf(e.Guid) : null;
            }
            catch (System.Exception) { ri = 0; pv = null; }
            if (ri != _raidIconShown)
            {
                _raidIconShown = ri;
                bool on = ri >= 1 && ri <= 8;
                _raidIcon.enabled = on;
                if (on) { float u = ((ri - 1) % 4) * 0.25f, v = ((ri - 1) / 4) * 0.25f; UiKit.Coords(_raidIcon, u, u + 0.25f, v, v + 0.25f); }
            }
            if (!ReferenceEquals(pv, _pvpShown))
            {
                _pvpShown = pv;
                Texture2D t = pv != null ? UiArt.Tex(Blp(pv)) : null;
                _pvpIcon.texture = t;
                _pvpIcon.enabled = t != null;
            }
        }

        public const string RaidIcons = @"Interface\TargetingFrame\UI-RaidTargetingIcons.blp";
        public const string EliteArt = @"Interface\TargetingFrame\UI-TargetingFrame-Elite.blp";
        public const string RareArt = @"Interface\TargetingFrame\UI-TargetingFrame-Rare.blp";
        public const string RareEliteArt = @"Interface\TargetingFrame\UI-TargetingFrame-Rare-Elite.blp";
        public const string SkullArt = @"Interface\TargetingFrame\UI-TargetingFrame-Skull.blp";
        private RawImage _frame, _skull;
        private int _cls = -1;
        private bool _skullShown;
        private RawImage _raidIcon, _pvpIcon;
        private int _raidIconShown = -1;
        private string _pvpShown;

        /// <summary>An art path with its .blp extension (the owners' delegates may give it without).</summary>
        internal static string Blp(string path)
        {
            return path.EndsWith(".blp", System.StringComparison.OrdinalIgnoreCase) ? path : path + ".blp";
        }
    }

    /// <summary>
    /// CastingBarFrame (CastingBarFrame.xml/.lua): 195x13 UI-StatusBar in UI-CastingBar-Border (256x64, TOP +28), spark at the
    /// fill end, the spell name above; yellow (1, 0.7, 0) while casting, green + flash on success, red "Interrupted" on failure,
    /// fading out.
    /// </summary>
    public sealed class UiCastBar
    {
        public RectTransform Root;
        private readonly UiBar _bar;
        private readonly RawImage _spark, _flash;
        private readonly TextMeshProUGUI _text;
        private readonly CanvasGroup _group;
        private int _spell;
        private float _endAt = -1f, _flashAlpha;
        private bool _casting, _channel;
        private string _failReason;
        private int _failSpell;
        private float _failAt = -10f, _successAt = -10f;
        private float _shownFrac = -1f;
        private bool _held;
        private readonly TextMeshProUGUI _hint;
        private ulong _me = 1UL;   // the player's guid (offline 1 = OfflineSession.PlayerGuid; online the server's), taken in Refresh
        private int _startedSpell;   // stage O2 fix: the last CastStarted / ChannelStarted of ours and its Text (the session's spell name),
        private string _startedName; // for spells outside the spellbook (Opening 3365 ..., item spells such as Hearthstone 8690)

        public UiCastBar(Transform parent, float x, float y)
        {
            Root = UiKit.Rect(parent, "Cast Bar", x, y, 195f, 13f);
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.5f), 0f, 0f, 195f, 13f);
            _bar = UiBar.Make(Root, "Fill", 0f, 0f, 195f, 13f, UiArt.CastYellow, false);
            UiKit.Pic(Root, "Border", UiArt.Tex(UiArt.CastBorder), -30.5f, -28f + 5f, 256f, 64f);
            _flash = UiKit.Pic(Root, "Flash", UiArt.Add(UiArt.CastFlash), -30.5f, -28f + 5f, 256f, 64f);
            _flash.enabled = false;
            _spark = UiKit.Pic(Root, "Spark", UiArt.Add(UiArt.CastSpark), 0f, -9.5f, 32f, 32f);
            _text = UiKit.Text(Root, "Text", "", 11f, Color.white, TextAlignmentOptions.Center, 5f, -1f, 185f, 15f);
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _hint = UiKit.Text(Root, "Held hint", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, -20f, 16f, 235f, 13f);
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
            _hint.enabled = false;
            _group.alpha = 0f;
        }

        /// <summary>
        /// The held look of immersive casting: the bar stays full and pulses between the cast colour and a pale gold with the flash
        /// glowing, the spell name on it and "Push your hand forward to release" under it until the session releases or drops it.
        /// </summary>
        private void ShowHeld(UiContext ctx, int spellId, float now)
        {
            if (!_held || !_casting || _spell != spellId)
            {
                _held = true;
                _casting = true;
                _channel = false;
                _spell = spellId;
                _text.text = NameOf(ctx, spellId);
                _text.color = Color.white;
                _hint.text = ctx.Vr ? "Push your hand forward to release" : "Ready - release to cast";
                _hint.enabled = true;
                _spark.enabled = false;
                _bar.Set(1f);
                _shownFrac = -1f;
                _group.alpha = 1f;
                _endAt = -1f;
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 3f);
            _bar.SetColor(Color.Lerp(UiArt.CastYellow, new Color(1f, 0.95f, 0.55f), pulse));
            _flash.enabled = _flash.texture != null;
            _flash.color = new Color(1f, 1f, 1f, 0.25f + 0.5f * pulse);
        }

        /// <summary>CastFailed / CastSucceeded from WowPlay.Event.</summary>
        public void OnEvent(GameEvent e, float now)
        {
            UiTargetCastBar.Relay(e, now);   // stage S1: the focus frame's spell bar (it gets no events from the layouts)
            if (e.SourceGuid != _me && e.SourceGuid != 0) return;   // stage O2: online our guid is the server's, not 1
            if ((e.Type == GameEventType.CastStarted || e.Type == GameEventType.ChannelStarted) && e.SpellId != 0)
            {
                _startedSpell = e.SpellId;
                _startedName = e.Text;
                // the bar may have started this frame before the event came: fill the name in now
                if (_casting && _spell == e.SpellId && string.IsNullOrEmpty(_text.text) && !string.IsNullOrEmpty(e.Text)) _text.text = e.Text;
                return;
            }
            if (e.Type == GameEventType.CastFailed && e.SpellId == _spell && _casting)
            {
                _failReason = string.IsNullOrEmpty(e.Text) ? "Interrupted" : e.Text;
                _failSpell = e.SpellId;
                _failAt = now;
            }
            else if (e.Type == GameEventType.ChannelEnded && _casting && _channel && !string.IsNullOrEmpty(e.Text))
            {
                _failReason = e.Text;
                _failSpell = _spell;
                _failAt = now;
            }
            else if (e.Type == GameEventType.CastSucceeded && e.SpellId == _spell)
            {
                _successAt = now;
            }
        }

        /// <summary>The bar's text: the spellbook's / mounts' name, else the name our CastStarted / ChannelStarted carried (stage O2 fix).</summary>
        private string NameOf(UiContext ctx, int spellId)
        {
            AbilityView v = ctx.Ability(spellId);
            if (v != null && !string.IsNullOrEmpty(v.Name)) return v.Name;
            return spellId == _startedSpell && _startedName != null ? _startedName : "";
        }

        public void Refresh(UiContext ctx, float now)
        {
            PlayerState p = ctx.P;
            if (p.Guid != 0) _me = p.Guid;
            // Immersive casting (iteration 4): the cast time is over and the spell waits for the push (Player.HeldSpellId).
            if (p.HeldSpellId != 0)
            {
                ShowHeld(ctx, p.HeldSpellId, now);
                return;
            }
            if (_held)
            {
                // Released (CastSucceeded -> the green flash below) or dropped (CastFailed -> red).
                _held = false;
                _hint.enabled = false;
            }
            if (p.CastingSpellId != 0 && p.CastTotal > 0f)
            {
                if (!_casting || p.CastingSpellId != _spell)
                {
                    _casting = true;
                    _spell = p.CastingSpellId;
                    _text.text = NameOf(ctx, _spell);
                    _text.color = Color.white;
                    _channel = p.Channeling;
                    // CastingBarFrame.lua:197 - a channel is green (0, 1, 0) and drains.
                    _bar.SetColor(_channel ? new Color(0f, 1f, 0f) : UiArt.CastYellow);
                    _flash.enabled = false;
                    _spark.enabled = true;
                    _group.alpha = 1f;
                    _endAt = -1f;
                }
                float frac = Mathf.Clamp01(p.CastElapsed / p.CastTotal);
                if (_channel) frac = 1f - frac;
                if (Mathf.Abs(frac - _shownFrac) > 0.002f)
                {
                    _shownFrac = frac;
                    _bar.Set(frac);
                    UiKit.Move(_spark.rectTransform, 195f * frac - 16f, -9.5f);
                }
                return;
            }
            if (_casting)
            {
                _casting = false;
                _spark.enabled = false;
                bool failed = now - _failAt < 0.3f && _failSpell == _spell;
                if (failed)
                {
                    _bar.Set(1f);
                    _bar.SetColor(new Color(1f, 0f, 0f));
                    _text.text = _failReason;
                    _endAt = now + 1f;
                }
                else
                {
                    _bar.Set(1f);
                    _bar.SetColor(new Color(0f, 1f, 0f));
                    _flash.enabled = _flash.texture != null;
                    _flashAlpha = 1f;
                    _endAt = now + 0.5f;
                }
                _shownFrac = -1f;
            }
            if (_endAt > 0f)
            {
                float left = _endAt - now;
                if (left <= 0f) { _endAt = -1f; _group.alpha = 0f; _flash.enabled = false; }
                else
                {
                    _group.alpha = Mathf.Clamp01(left / 0.5f);
                    if (_flash.enabled) { _flashAlpha = Mathf.Clamp01(left / 0.5f); _flash.color = new Color(1f, 1f, 1f, _flashAlpha); }
                }
            }
        }
    }

    /// <summary>
    /// Lead 2026-10-26 (swimming): WoW's MirrorTimer1-3 (MirrorTimer.xml): 206x26 each, stacked downwards; a 195x13 UI-StatusBar at TOP (0, -2)
    /// on a black 0.5 background, UI-CastingBar-Border 256x64 at TOP (0, 25), the label at TOP. MirrorTimerColors (MirrorTimer.lua): Fatigue
    /// (1, 0.9, 0), Breath (0, 0.5, 1), feign death (1, 0.7, 0); labels EXHAUSTION_LABEL "Fatigue" / BREATH_LABEL "Breath" (GlobalStrings.lua).
    /// </summary>
    public sealed class UiMirrorTimers
    {
        public RectTransform Root;
        private readonly RectTransform[] _rows = new RectTransform[3];
        private readonly UiBar[] _bars = new UiBar[3];
        private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[3];
        private readonly int[] _shownKind = { -1, -1, -1 };
        private static readonly Color[] Colors = { new Color(1f, 0.9f, 0f), new Color(0f, 0.5f, 1f), new Color(1f, 0.7f, 0f) };
        private static readonly string[] Labels = { "Fatigue", "Breath", "" };

        public UiMirrorTimers(Transform parent, float x, float y)
        {
            Root = UiKit.Rect(parent, "Mirror timers", x, y, 206f, 78f);
            for (int i = 0; i < 3; i++)
            {
                RectTransform r = UiKit.Rect(Root, "MirrorTimer" + (i + 1), 0f, 26f * i, 206f, 26f);
                UiKit.Solid(r, "Background", new Color(0f, 0f, 0f, 0.5f), 5.5f, 2f, 195f, 13f);
                _bars[i] = UiBar.Make(r, "StatusBar", 5.5f, 2f, 195f, 13f, Colors[1], false);
                UiKit.Pic(r, "Border", UiArt.Tex(UiArt.CastBorder), -25f, -25f + 2f, 256f, 64f);
                _labels[i] = UiKit.Text(r, "Text", "", 11f, Color.white, TextAlignmentOptions.Center, 5.5f, 0f, 195f, 15f);
                _labels[i].textWrappingMode = TextWrappingModes.NoWrap;
                _rows[i] = r;
                r.gameObject.SetActive(false);
            }
        }

        public void Refresh(PlayerState p)
        {
            int row = 0;
            for (int t = 0; t < 3 && p != null; t++)
            {
                MirrorTimerState m = p.Mirror[t];
                if (!m.Active || m.Max <= 0f) continue;
                if (_shownKind[row] != t)
                {
                    _shownKind[row] = t;
                    _bars[row].SetColor(Colors[t]);
                    _labels[row].text = Labels[t];
                }
                _bars[row].Set(Mathf.Clamp01(m.Value / m.Max));
                if (!_rows[row].gameObject.activeSelf) _rows[row].gameObject.SetActive(true);
                row++;
            }
            for (; row < 3; row++)
                if (_rows[row].gameObject.activeSelf) { _rows[row].gameObject.SetActive(false); _shownKind[row] = -1; }
        }
    }

    /// <summary>MainMenuExpBar (MainMenuBar.xml): UI-StatusBar in purple (0.58, 0, 0.55) with the four UI-MainMenuBar-Dwarf XP overlay slices.</summary>
    public sealed class UiXpBar
    {
        public RectTransform Root;
        private readonly UiBar _bar;
        private readonly TextMeshProUGUI _text;
        private int _xp = -1, _need = -1, _lvl = -1;

        // MainMenuXPBarTexture0..3 TexCoords top/bottom (MainMenuBar.xml:47, 60, 73, 86).
        private static readonly float[] SliceTop = { 0.79296875f, 0.54296875f, 0.29296875f, 0.04296875f };

        public UiXpBar(Transform parent, float x, float y, float w)
        {
            Root = UiKit.Rect(parent, "XP Bar", x, y, w, 13f);
            UiKit.Solid(Root, "Background", new Color(0f, 0f, 0f, 0.6f), 0f, 1f, w, 11f);
            _bar = UiBar.Make(Root, "Fill", 0f, 1f, w, 11f, UiArt.XpPurple, false);
            Texture2D art = UiArt.Tex(UiArt.MainMenuBar);
            for (int i = 0; i < 4; i++)
                UiKit.Pic(Root, "Overlay " + i, art, 0f, 1f, SliceTop[i], SliceTop[i] + 0.0390625f, i * w / 4f, 0f, w / 4f, 13f);
            _text = UiKit.Text(Root, "Text", "", 9f, Color.white, TextAlignmentOptions.Center, 0f, 0f, w, 13f);
            _text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        public void Refresh(PlayerState p)
        {
            if (p.Xp == _xp && p.XpToNextLevel == _need && p.Level == _lvl) return;
            _xp = p.Xp; _need = p.XpToNextLevel; _lvl = p.Level;
            if (_need <= 0)
            {
                _bar.Set(1f);
                _text.SetText("Level {0}", p.Level);
            }
            else
            {
                _bar.Set((float)_xp / _need);
                _text.SetText("XP {0} / {1}", _xp, _need);
            }
        }
    }

    /// <summary>
    /// UIErrorsFrame (512x60 at TOP (0, -122), insertMode TOP, displayDuration 5: UIErrorsFrame.xml): red errors, yellow info and
    /// quest progress, purple experience - the newest on top, each fading after 5 s.
    /// </summary>
    public sealed class UiMessages
    {
        private readonly TextMeshProUGUI[] _lines;
        private readonly float[] _at;
        private readonly Color[] _col;
        private readonly string[] _text;
        private readonly float[] _alpha;
        public const float Hold = 5f, Fade = 1f;

        public UiMessages(Transform parent, float x, float y, float w, int count, float size)
        {
            _lines = new TextMeshProUGUI[count];
            _at = new float[count];
            _col = new Color[count];
            _text = new string[count];
            _alpha = new float[count];
            for (int i = 0; i < count; i++)
            {
                float lh = Mathf.Ceil(size * 1.4f);
                _lines[i] = UiKit.Text(parent, "Message " + i, "", size, Color.white, TextAlignmentOptions.Center, x, y + i * lh, w, lh);
                _lines[i].textWrappingMode = TextWrappingModes.NoWrap;
                _lines[i].overflowMode = TextOverflowModes.Ellipsis;
                _at[i] = -100f;
                _lines[i].enabled = false;
            }
        }

        public void Add(string text, Color c, float now)
        {
            if (string.IsNullOrEmpty(text)) return;
            // The same line again (e.g. "Out of range" spam) just refreshes its time.
            if (_text[0] != null && string.Equals(_text[0], text) && now - _at[0] < Hold) { _at[0] = now; return; }
            for (int i = _lines.Length - 1; i > 0; i--) { _text[i] = _text[i - 1]; _col[i] = _col[i - 1]; _at[i] = _at[i - 1]; _alpha[i] = -1f; }
            _text[0] = text; _col[0] = c; _at[0] = now; _alpha[0] = -1f;
            for (int i = 0; i < _lines.Length; i++)
            {
                if (_text[i] == null) continue;
                if (!ReferenceEquals(_lines[i].text, _text[i])) _lines[i].text = _text[i];
            }
        }

        public void Refresh(float now)
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                float age = now - _at[i];
                float a = _text[i] == null ? 0f : age < Hold ? 1f : Mathf.Clamp01(1f - (age - Hold) / Fade);
                if (Mathf.Abs(a - _alpha[i]) < 0.01f) continue;
                _alpha[i] = a;
                bool on = a > 0.001f;
                if (_lines[i].enabled != on) _lines[i].enabled = on;
                if (on) { Color c = _col[i]; c.a = a; _lines[i].color = c; }
            }
        }
    }

    /// <summary>A big centred announcement (level up, quest complete): gold MORPHEUS title + white line, 3 s then a 1 s fade.</summary>
    public sealed class UiBanner
    {
        public RectTransform Root;
        private readonly TextMeshProUGUI _title, _sub;
        private readonly CanvasGroup _group;
        private float _at = -100f;
        public bool Active { get { return _group.alpha > 0.001f; } }

        public UiBanner(Transform parent, float x, float y, float w)
        {
            Root = UiKit.Rect(parent, "Banner", x, y, w, 80f);
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _title = UiKit.Text(Root, "Title", "", 30f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, w, 42f, UiArt.Morpheus);
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Overflow;
            _sub = UiKit.Text(Root, "Sub", "", 15f, Color.white, TextAlignmentOptions.Center, 0f, 44f, w, 24f);
            _group.alpha = 0f;
        }

        public void Show(string title, string sub, float now)
        {
            _title.text = title ?? "";
            _sub.text = sub ?? "";
            _at = now;
            _group.alpha = 1f;
        }

        public void Refresh(float now)
        {
            if (_group.alpha <= 0f) return;
            float age = now - _at;
            float a = age < 3f ? 1f : Mathf.Clamp01(1f - (age - 3f));
            if (Mathf.Abs(a - _group.alpha) > 0.01f || a == 0f) _group.alpha = a;
        }
    }

    /// <summary>Stage S1: units the frames draw - the session's entity, else a member of the server group from its stats.</summary>
    public static class UiUnits
    {
        public static GroupMemberView GroupMember(IWowSession s, ulong guid)
        {
            if (s == null || guid == 0 || s.Player == null) return null;
            List<GroupMemberView> g = s.Player.Group;
            if (g == null) return null;
            for (int i = 0; i < g.Count; i++) if (g[i] != null && g[i].Guid == guid) return g[i];
            return null;
        }

        /// <summary>
        /// The entity with this guid; a server-group member the realm has not shown us (out of view) is written into scratch from
        /// SMSG_PARTY_MEMBER_STATS (GroupMemberView) - a friendly player, level coloured like GetQuestDifficultyColor (UiArt.DifficultyColor's
        /// bands). Null when neither.
        /// </summary>
        public static EntityState Resolve(IWowSession s, ulong guid, EntityState scratch)
        {
            if (s == null || guid == 0) return null;
            EntityState e = s.Find(guid);
            if (e != null || scratch == null) return e;
            GroupMemberView m = GroupMember(s, guid);
            if (m == null) return null;
            scratch.Guid = guid;
            scratch.Kind = EntityKind.Player;
            scratch.Name = m.Name;
            scratch.Level = m.Level;
            scratch.Health = m.Online ? m.Health : 0;
            scratch.MaxHealth = m.MaxHealth;
            scratch.Power = m.Power;
            scratch.PowerValue = m.PowerValue;
            scratch.PowerMax = m.PowerMax;
            scratch.Reaction = Reaction.Friendly;
            scratch.Dead = m.Dead || m.Ghost;
            scratch.CastingSpellId = 0;
            scratch.ComboPoints = 0;
            scratch.Auras.Clear();
            scratch.ConColor = Con(m.Level, s.Player != null ? s.Player.Level : m.Level);
            return scratch;
        }

        /// <summary>UiArt.DifficultyColor's bands as EntityState.ConColor (0 grey .. 4 red).</summary>
        private static int Con(int level, int playerLevel)
        {
            int d = level - playerLevel;
            if (d >= 5) return 4;
            if (d >= 3) return 3;
            if (d >= -2) return 2;
            int gray = playerLevel <= 5 ? 0 : playerLevel - 5 - playerLevel / 10;   // AzerothCore Formulas.h GetGrayLevel (as UiArt)
            return level > gray ? 1 : 0;
        }
    }

    /// <summary>
    /// Stage S1: WoW's focus frame (FocusFrame, TargetFrame.xml:658-686: a TargetFrameTemplate at TOPLEFT (250, -240) of UIParent) at
    /// the small focus size SMALL_FOCUS_SCALE 0.75 (TargetFrame.lua:18; the small size shows debuffs only, :999-1000) with its spell bar
    /// scaled back up by SMALL_FOCUS_UPSCALE 1.333 (:19, :1005), for WowFocus. Shown while the focus is around (as FocusFrame shows
    /// only while UnitExists("focus")), a member of the server group also from its stats. A small "Focus" label (FOCUS,
    /// GlobalStrings.lua:3710 - ours to tell the two frames apart). Click (desktop) / laser + trigger (VR) = target the focus;
    /// right-click (desktop) = Clear Focus (CLEAR_FOCUS, GlobalStrings.lua:1632; WoW has it in the frame's menu).
    /// </summary>
    public sealed class UiFocusFrame
    {
        public const float Scale = 0.75f;

        public RectTransform Root;
        public readonly UiTargetFrame Frame;
        public readonly UiTargetCastBar Cast;
        private readonly EntityState _scratch = new EntityState();
        private ulong _logged = ulong.MaxValue;

        public UiFocusFrame(Transform parent, float x, float y, bool desktop)
        {
            Root = UiKit.Rect(parent, "Focus Frame", x, y, 232f, 100f);
            Root.localScale = Vector3.one * Scale;
            Frame = new UiTargetFrame(Root, 0f, 0f);
            Frame.Root.name = "Focus Target Frame";
            TextMeshProUGUI label = UiKit.Text(Frame.Root, "Focus label", "Focus", 12f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 14f, -15f, 120f, 16f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Cast = new UiTargetCastBar(Frame.Root);
            Cast.Root.localScale = Vector3.one / Scale;
            Cast.ListenToAll();
            Frame.Button.Click = () => WowFocus.Select(WowPlay.Session, "a click on the focus frame");
            Frame.Button.RightClick = () => WowFocus.Clear("a right-click on the focus frame (Clear Focus)");
            if (desktop) Frame.Button.Tip = () =>
            {
                string n = WowFocus.Name;
                return n == null ? null : "<size=14>" + n + "</size>\n<color=#00ff00>Click: target your focus. Right-click: Clear Focus.\n"
                    + VrFocusKeys.Desktop + ": set / target the focus, three times: clear.</color>";   // hints ours
            };
        }

        public void Refresh(IWowSession s)
        {
            ulong g = WowFocus.Guid;
            EntityState e = g != 0 ? UiUnits.Resolve(s, g, _scratch) : null;
            Frame.RefreshUnit(s, e, false);
            if (e != null) Cast.RefreshFor(s, g, Time.unscaledTime);
            ulong shown = e != null ? g : 0;
            if (shown != _logged)
            {
                if (_logged != ulong.MaxValue || shown != 0)
                    WowLog.Info("UI: focus frame " + (shown != 0 ? "shows " + (e.Name ?? "?") + (ReferenceEquals(e, _scratch) ? " (from the group's stats)" : "") : g != 0 ? "hidden - the focus is not around" : "hidden - no focus") + ".");
                _logged = shown;
            }
        }
    }

    /// <summary>The focus button's names for texts (VrFocusButton owns the keys).</summary>
    public static class VrFocusKeys
    {
        public const string Desktop = SomniumSpace.Worlds.SomniumWoW.Game.VrInput.VrFocusButton.DesktopKeyName;
        public const string Vr = SomniumSpace.Worlds.SomniumWoW.Game.VrInput.VrFocusButton.VrButtonName;
    }

    /// <summary>
    /// Stage S1, desktop: the top-left group (player, target, party, focus, pet, totems - the desktop layout's "Top left" container)
    /// sized and moved by Settings > HUD: WowSettings.HudScale around its top-left corner (on top of the interface scale),
    /// HudOffsetX / HudOffsetY as fractions of the screen, kept so the player and target frames stay on the screen (ours).
    /// Applied on change (the settings, the screen size) from the party frames' refresh (they are built in that container).
    /// </summary>
    public sealed class UiHudPlacement
    {
        /// <summary>Ours: the part that must stay on the screen - PlayerFrame (-19, -4) to TargetFrame's right edge (250 + 232) x 104.</summary>
        public const float KeepW = 482f, KeepH = 104f;

        private readonly RectTransform _group;
        private float _scale = -1f, _fx = -1f, _fy = -1f, _cw = -1f, _ch = -1f;

        public UiHudPlacement(RectTransform group) { _group = group; }

        public void Apply()
        {
            if (_group == null) return;
            RectTransform canvas = _group.parent as RectTransform;
            if (canvas == null) return;
            float sc = Mathf.Clamp(WowSettings.HudScale, WowSettings.HudScaleMin, WowSettings.HudScaleMax);
            float fx = Mathf.Clamp(WowSettings.HudOffsetX, 0f, WowSettings.HudOffsetMaxX);
            float fy = Mathf.Clamp(WowSettings.HudOffsetY, 0f, WowSettings.HudOffsetMaxY);
            UnityEngine.Rect r = canvas.rect;
            float cw = r.width, ch = r.height;
            bool settings = sc != _scale || fx != _fx || fy != _fy;
            if (!settings && Mathf.Abs(cw - _cw) < 0.5f && Mathf.Abs(ch - _ch) < 0.5f) return;
            bool first = _scale < 0f;
            _scale = sc; _fx = fx; _fy = fy; _cw = cw; _ch = ch;
            float x = Mathf.Min(fx * cw, Mathf.Max(0f, cw - KeepW * sc));
            float y = Mathf.Min(fy * ch, Mathf.Max(0f, ch - KeepH * sc));
            _group.localScale = Vector3.one * sc;
            _group.anchoredPosition = new Vector2(x, -y);
            if (settings && (!first || sc != 1f || fx != 0f || fy != 0f))
                WowLog.Info("UI: desktop HUD group size " + sc.ToString("0.00") + "x at " + Mathf.RoundToInt(fx * 100f) + "% across, " + Mathf.RoundToInt(fy * 100f) + "% down ("
                    + Mathf.RoundToInt(x) + ", " + Mathf.RoundToInt(y) + " of " + Mathf.RoundToInt(cw) + " x " + Mathf.RoundToInt(ch) + " units; Settings > HUD).");
        }
    }
}
