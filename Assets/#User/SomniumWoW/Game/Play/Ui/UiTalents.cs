// iteration 11 - owner talents (new file): the talent tree view of WoW's PlayerTalentFrame (proposal-talents.md 4.2 / 5.4), its tooltip and the
// small shared pieces of the talent and glyph frames (WoW's strings, the CharacterFrame tab, desaturated icons).
// FrameXML of the player's install (dumps in P/harness/out_d11talents/): Blizzard_TalentUI.xml (scroll frame :417-500, backgrounds :433-470),
// TalentFrameTemplates.xml (TalentButtonTemplate: ItemButtonTemplate 37x37 + UI-EmptySlot-White 64x64 + TalentFrame-RankBorder 32x32 on the
// BOTTOMRIGHT + GameFontNormalSmall rank), TalentFrameBase.lua (TalentFrame_Update :100-312: background per tree, button colours :196-219,
// forceDesaturated :177-181, tiers :183-188; branches via Play.Spells.TalentBranches).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>WoW's strings through uicore's GlobalStrings reader (the install's language when it is built; the enUS text quoted with its line
    /// otherwise) and Lua's string.format subset these strings use.</summary>
    internal static class UiTalentText
    {
        public static string G(string key, string fallback) { return UiGlobalStrings.G(key, fallback); }

        /// <summary>%s / %d in order, %1$s / %2$d by position, %% and WoW's "|4singular:plural;" grammar (picked by the number printed just before).</summary>
        public static string Fmt(string template, params object[] args)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var sb = new StringBuilder(template.Length + 16);
            int next = 0;
            string last = null;
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c == '%' && i + 1 < template.Length)
                {
                    int j = i + 1, pos = -1, k = j, num = 0;
                    while (k < template.Length && template[k] >= '0' && template[k] <= '9') { num = num * 10 + (template[k] - '0'); k++; }
                    if (k > j && k < template.Length && template[k] == '$') { pos = num - 1; j = k + 1; }
                    if (j < template.Length && (template[j] == 's' || template[j] == 'd'))
                    {
                        int idx = pos >= 0 ? pos : next++;
                        string v = args != null && idx >= 0 && idx < args.Length && args[idx] != null ? Convert.ToString(args[idx], CultureInfo.InvariantCulture) : "";
                        sb.Append(v);
                        last = v;
                        i = j;
                        continue;
                    }
                    if (template[j] == '%') { sb.Append('%'); i = j; continue; }
                }
                if (c == '|' && i + 1 < template.Length && template[i + 1] == '4')
                {
                    int semi = template.IndexOf(';', i + 2);
                    int colon = semi > 0 ? template.IndexOf(':', i + 2, semi - i - 2) : -1;
                    if (semi > 0 && colon > 0)
                    {
                        sb.Append(last == "1" ? template.Substring(i + 2, colon - i - 2) : template.Substring(colon + 1, semi - colon - 1));
                        i = semi;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>CharacterFrameTabButtonTemplate (CharacterFrameTemplates.xml): UI-Character-InActiveTab 20 + middle + 20 (texcoords 0-0.15625 /
    /// -0.84375 / -1), the selected tab UI-Character-ActiveTab drawn 5 units higher, highlight UI-Character-Tab-Highlight (ADD) inset 10,
    /// GameFontNormalSmall; width = the text + 40 (PanelTemplates_TabResize).</summary>
    internal sealed class UiTalentTab
    {
        const string TabOff = @"Interface\PaperDollInfoFrame\UI-Character-InActiveTab.blp", TabOn = @"Interface\PaperDollInfoFrame\UI-Character-ActiveTab.blp",
            TabHilite = @"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp";
        public UiButton B;
        public float W;
        readonly RawImage[] _off = new RawImage[3], _on = new RawImage[3];
        readonly RawImage[] _all = new RawImage[6];
        TextMeshProUGUI _label;
        bool _selected = true;

        public UiTalentTab(Transform parent, string name, float x, float y, Action click)
        {
            B = UiKit.Button(parent, "Tab " + name, x, y, 100f, 32f);
            _label = UiKit.Text(B.transform, "Text", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 100f, 32f);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            Texture2D off = UiArt.Tex(TabOff), on = UiArt.Tex(TabOn);
            _off[0] = UiKit.Pic(B.transform, "L", off, 0f, 0.15625f, 0f, 1f, 0f, 0f, 20f, 32f);
            _off[1] = UiKit.Pic(B.transform, "M", off, 0.15625f, 0.84375f, 0f, 1f, 20f, 0f, 60f, 32f);
            _off[2] = UiKit.Pic(B.transform, "R", off, 0.84375f, 1f, 0f, 1f, 80f, 0f, 20f, 32f);
            _on[0] = UiKit.Pic(B.transform, "L on", on, 0f, 0.15625f, 0f, 1f, 0f, -5f, 20f, 32f);
            _on[1] = UiKit.Pic(B.transform, "M on", on, 0.15625f, 0.84375f, 0f, 1f, 20f, -5f, 60f, 32f);
            _on[2] = UiKit.Pic(B.transform, "R on", on, 0.84375f, 1f, 0f, 1f, 80f, -5f, 20f, 32f);
            B.Hilite = UiKit.Pic(B.transform, "Highlight", UiArt.Add(TabHilite), 10f, 2f, 80f, 32f);
            B.Hilite.enabled = false;
            _label.transform.SetAsLastSibling();
            B.Click = click;
            Select(false);
        }

        float _maxW = -1f;

        /// <summary>Sets the text and resizes (PanelTemplates_TabResize: text + 40, at least 60 - ours; at most maxW, the Lua shaves wide tabs).</summary>
        public void SetText(string text, float maxW)
        {
            text = text ?? "";
            if (_label.text == text && Mathf.Abs(maxW - _maxW) < 0.5f) return;
            _maxW = maxW;
            _label.text = text;
            float tw = Mathf.Ceil(_label.GetPreferredValues(_label.text).x);
            W = Mathf.Clamp(tw + 40f, 60f, Mathf.Max(60f, maxW));
            float mid = W - 40f;
            UiKit.Size(B.Rt, W, 32f);
            UiKit.Size(_label.rectTransform, W, 32f);
            for (int k = 0; k < 2; k++)
            {
                RawImage[] set = k == 0 ? _off : _on;
                float y = k == 0 ? 0f : -5f;
                UiKit.Move(set[1].rectTransform, 20f, y); UiKit.Size(set[1].rectTransform, mid, 32f);
                UiKit.Move(set[2].rectTransform, 20f + mid, y);
            }
            UiKit.Size(B.Hilite.rectTransform, W - 20f, 32f);
        }

        public void Select(bool on)
        {
            if (on == _selected) return;
            _selected = on;
            for (int i = 0; i < 3; i++) { _off[i].enabled = !on; _on[i].enabled = on; }
            _label.color = on ? Color.white : UiArt.Gold;
            UiKit.Move(_label.rectTransform, 0f, on ? -4f : -1f);
        }

        public void Show(bool on) { if (B.gameObject.activeSelf != on) B.gameObject.SetActive(on); }
        public void Place(float x, float y) { UiKit.Move(B.Rt, x, y); }
    }

    /// <summary>Desaturated talent icons (TalentFrameBase.lua:213 SetItemButtonDesaturated(button, 1, 0.65, 0.65, 0.65)): uGUI has no desaturation,
    /// so the icon's BLP is decoded on a pool thread, made grey (luminance x 0.65) and uploaded on the main thread (Pump) - like UiArt.Icon.</summary>
    internal static class UiTalentIcons
    {
        static readonly object Lock = new object();
        static readonly Dictionary<string, Texture2D> Done = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> Pending = new HashSet<string>();
        static readonly List<KeyValuePair<string, byte[]>> Ready = new List<KeyValuePair<string, byte[]>>();
        static readonly Dictionary<string, Vector2Int> Sizes = new Dictionary<string, Vector2Int>();
        static WowFiles _files;

        /// <summary>The grey icon, or null while it is prepared (ask again next frame; a missing icon stays null).</summary>
        public static Texture2D Grey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            WowFiles files = UiArt.Files;
            if (files == null) return null;
            lock (Lock)
            {
                if (!ReferenceEquals(files, _files)) { _files = files; Done.Clear(); Pending.Clear(); Ready.Clear(); Sizes.Clear(); }
                Texture2D t;
                if (Done.TryGetValue(raw, out t)) return t;
                if (Pending.Contains(raw)) return null;
                Pending.Add(raw);
            }
            string path = raw.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".blp";
            string key = raw;
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] px = null;
                int w = 0, h = 0;
                try
                {
                    byte[] blp = files.Read(path);
                    if (blp != null)
                    {
                        BlpImage img = BlpImage.Decode(blp);
                        px = img.GetRgba32(0);
                        w = img.Width; h = img.Height;
                        for (int i = 0; i < px.Length; i += 4)
                        {
                            int g = (int)((px[i] * 0.3f + px[i + 1] * 0.59f + px[i + 2] * 0.11f) * 0.65f);
                            px[i] = px[i + 1] = px[i + 2] = (byte)Math.Min(255, g);
                        }
                    }
                }
                catch (Exception) { px = null; }
                lock (Lock)
                {
                    if (!ReferenceEquals(files, _files)) return;
                    Ready.Add(new KeyValuePair<string, byte[]>(key, px));
                    Sizes[key] = new Vector2Int(w, h);
                }
            });
            return null;
        }

        /// <summary>Main thread: uploads the prepared icons (a few per frame).</summary>
        public static void Pump(int max)
        {
            for (int n = 0; n < max; n++)
            {
                KeyValuePair<string, byte[]> item;
                Vector2Int size;
                lock (Lock)
                {
                    if (Ready.Count == 0) return;
                    item = Ready[Ready.Count - 1];
                    Ready.RemoveAt(Ready.Count - 1);
                    Sizes.TryGetValue(item.Key, out size);
                }
                Texture2D t = null;
                if (item.Value != null && size.x > 0 && size.y > 0)
                {
                    try { t = WowTextures.FromRgba32(size.x, size.y, item.Value, WowTextureOptions.Ui, "TalentGrey", true); }
                    catch (Exception e) { WowLog.Warn("UI talents: grey icon " + item.Key + ": " + e.Message); }
                }
                lock (Lock) { Done[item.Key] = t; Pending.Remove(item.Key); }
            }
        }
    }

    /// <summary>What a tree / tooltip shows: the server's ranks of one talent group plus the preview points (TalentFrame's GetTalentInfo
    /// previewRank, GetGroupPreviewTalentPointsSpent - C code in the client; ours here, by the server's rules).</summary>
    public sealed class UiTalentModel
    {
        public TalentDb Db;
        public SpellDb Spells;
        public PlayerState P;
        public bool Pet;                                    // the hunter pet's tree
        public int PetType = -1;                            // CreatureFamily petTalentType of the pet
        public TalentSpecView Spec;                         // the server's ranks of the shown group
        public readonly Dictionary<int, int> Preview = new Dictionary<int, int>();   // talent -> points added in preview
        public bool Previewing;                             // "Preview Talent Changes" (always in VR)
        public bool Active;                                 // the shown group is the active one (or the pet's)
        public int Free;                                    // the server's unspent points of the shown group
        public bool Offline;
        public Func<int, bool> SimulatedOffline;            // offline: does the offline rule set apply this rank spell

        public int PerTier { get { return Pet ? TalentDb.MaxPetTalentRank : TalentDb.MaxTalentRank; } }

        public int Added(int id) { int v; return Previewing && Preview.TryGetValue(id, out v) ? v : 0; }
        public int Rank(int id) { return (Spec != null ? Spec.Rank(id) : 0) + Added(id); }
        public int PreviewSpent { get { int n = 0; if (Previewing) foreach (var kv in Preview) n += kv.Value; return n; } }
        public int Remaining { get { return Free - PreviewSpent; } }

        public int TabPoints(int tabId)
        {
            int n = 0;
            var tab = Db != null ? Db.Tab(tabId) : null;
            if (tab == null) return 0;
            foreach (var t in tab.Talents) n += Rank(t.Id);
            return n;
        }

        int TabPointsBelow(int tabId, int row)
        {
            int n = 0;
            var tab = Db != null ? Db.Tab(tabId) : null;
            if (tab == null) return 0;
            foreach (var t in tab.Talents) if (t.Row < row) n += Rank(t.Id);
            return n;
        }

        public bool TierOpen(TalentInfo t) { return t.Row * PerTier <= TabPoints(t.TabId); }   // TalentFrameBase.lua:183-188
        public bool PrereqMet(TalentInfo t) { return t.PrereqTalent <= 0 || Rank(t.PrereqTalent) >= t.PrereqRank; }
        /// <summary>TalentFrame_SetPrereqs's requirementsMet: tier unlocked, not force-desaturated, prerequisite met.</summary>
        public bool RequirementsMet(TalentInfo t)
        {
            bool force = (Remaining <= 0 || !Active) && Rank(t.Id) == 0;          // TalentFrameBase.lua:177-181
            return !force && TierOpen(t) && PrereqMet(t);
        }
        public bool CanAdd(TalentInfo t)
        {
            return Active && Remaining > 0 && Rank(t.Id) < t.MaxRank && TierOpen(t) && PrereqMet(t);
        }

        /// <summary>A preview point may go when every talent with points keeps its tier (points in LOWER tiers, the server learns by tier) and
        /// its prerequisite (ours: WoW's rule is C code).</summary>
        public bool CanRemove(TalentInfo t)
        {
            int add;
            if (!Previewing || !Preview.TryGetValue(t.Id, out add) || add <= 0) return false;
            Preview[t.Id] = add - 1;
            bool ok = true;
            var tab = Db.Tab(t.TabId);
            if (tab != null)
                foreach (var x in tab.Talents)
                {
                    if (Rank(x.Id) <= 0) continue;
                    if (x.Row > 0 && TabPointsBelow(x.TabId, x.Row) < x.Row * PerTier) { ok = false; break; }
                    if (!PrereqMet(x)) { ok = false; break; }
                }
            Preview[t.Id] = add;
            return ok;
        }

        public void Add(TalentInfo t, int by)
        {
            int v;
            Preview.TryGetValue(t.Id, out v);
            v += by;
            if (v <= 0) Preview.Remove(t.Id); else Preview[t.Id] = v;
        }

        /// <summary>The preview as the session's pairs (talent -> the rank COUNT to reach).</summary>
        public List<KeyValuePair<int, int>> PreviewPairs()
        {
            var l = new List<KeyValuePair<int, int>>();
            foreach (var kv in Preview) if (kv.Value > 0) l.Add(new KeyValuePair<int, int>(kv.Key, (Spec != null ? Spec.Rank(kv.Key) : 0) + kv.Value));
            return l;
        }

        /// <summary>Drops preview points the new server state made impossible (ranks over max, no points left, a broken tier / prerequisite).</summary>
        public void Sanitize()
        {
            if (Preview.Count == 0) return;
            bool bad = Remaining < 0;
            foreach (var kv in Preview)
            {
                var t = Db != null ? Db.Find(kv.Key) : null;
                if (t == null || Rank(kv.Key) > t.MaxRank) { bad = true; break; }
            }
            if (!bad && Db != null)
                foreach (var kv in Preview)
                {
                    var t = Db.Find(kv.Key);
                    if (t.Row > 0 && TabPointsBelow(t.TabId, t.Row) < t.Row * PerTier) { bad = true; break; }
                    if (!PrereqMet(t)) { bad = true; break; }
                }
            if (bad) Preview.Clear();
        }
    }

    /// <summary>The talent tooltip (GameTooltip:SetTalent is C code - the lines are ours, from WoW's strings; proposal-talents.md 5.4).</summary>
    public static class UiTalentTooltip
    {
        static readonly StringBuilder Sb = new StringBuilder(512);

        public static string Build(UiTalentModel m, TalentInfo t, bool vr)
        {
            if (m == null || t == null || m.Spells == null) return null;
            int rank = m.Rank(t.Id), max = t.MaxRank;
            var first = m.Spells.Find(TalentDb.RankSpell(t, 1));
            var cur = m.Spells.Find(TalentDb.RankSpell(t, Math.Max(1, rank)));
            var next = rank > 0 && rank < max ? m.Spells.Find(TalentDb.RankSpell(t, rank + 1)) : null;
            var ctx = new SpellTextContext { CasterLevel = m.P != null ? Math.Max(1, m.P.Level) : 1 };
            if (m.P != null && m.P.Stats != null) { ctx.AttackPower = m.P.Stats.AttackPower; ctx.RangedAttackPower = m.P.Stats.RangedAttackPower; }
            Sb.Length = 0;
            Sb.Append("<size=14><color=#ffffff>").Append(first != null ? first.Name : "Talent " + t.Id).Append("</color></size>");
            Sb.Append("\n<color=#ffffff>").Append(UiTalentText.Fmt(UiTalentText.G("TOOLTIP_TALENT_RANK", "Rank %d/%d"), rank, max)).Append("</color>");   // GS 7601
            if (t.AddToSpellBook && cur != null) AppendAbility(m, cur);
            if (cur != null)
            {
                string d = SpellText.Resolve(m.Spells, cur, cur.Description, ctx);
                if (!string.IsNullOrEmpty(d)) Sb.Append("\n<color=#ffd100>").Append(d).Append("</color>");
            }
            if (next != null)
            {
                Sb.Append("\n\n<color=#ffffff>").Append(UiTalentText.G("TOOLTIP_TALENT_NEXT_RANK", "Next rank:")).Append("</color>");   // GS 7599
                string d = SpellText.Resolve(m.Spells, next, next.Description, ctx);
                if (!string.IsNullOrEmpty(d)) Sb.Append("\n<color=#ffd100>").Append(d).Append("</color>");
            }
            if (rank < max)
            {
                var tab = m.Db.Tab(t.TabId);
                if (!m.TierOpen(t))
                    Sb.Append("\n<color=#ff2020>").Append(UiTalentText.Fmt(UiTalentText.G("TOOLTIP_TALENT_TIER_POINTS", "Requires %1$d points in %2$s Talents"),   // GS 7602
                        t.Row * m.PerTier, tab != null ? tab.Name : "")).Append("</color>");
                if (!m.PrereqMet(t))
                {
                    var pre = m.Db.Find(t.PrereqTalent);
                    var preSpell = pre != null ? m.Spells.Find(TalentDb.RankSpell(pre, 1)) : null;
                    Sb.Append("\n<color=#ff2020>").Append(UiTalentText.Fmt(UiTalentText.G("TOOLTIP_TALENT_PREREQ", "Requires %1$d |4point:points; in %2$s"),   // GS 7600
                        t.PrereqRank, preSpell != null ? preSpell.Name : "")).Append("</color>");
                }
            }
            if (m.Offline && cur != null && m.SimulatedOffline != null && !m.SimulatedOffline(cur.Id))
                Sb.Append("\n<color=#808080>Not simulated offline (online the realm applies it).</color>");   // ours (proposal-talents.md 7)
            if (m.Active)
            {
                if (m.Previewing)
                {
                    if (m.CanAdd(t)) Sb.Append("\n<color=#00ff00>").Append(vr ? "Tap to add a point" : UiTalentText.G("TALENT_TOOLTIP_ADDPREVIEWPOINT", "Left click to add a point")).Append("</color>");   // GS 7461 (VR: ours)
                    if (m.CanRemove(t)) Sb.Append("\n<color=#00ff00>").Append(vr ? "Tap - to remove a point" : UiTalentText.G("TALENT_TOOLTIP_REMOVEPREVIEWPOINT", "Right click to remove a point")).Append("</color>");   // GS 7463 (VR: ours)
                }
                else if (m.CanAdd(t)) Sb.Append("\n<color=#00ff00>").Append(UiTalentText.G("TOOLTIP_TALENT_LEARN", "Click to learn")).Append("</color>");   // GS 7598
            }
            return Sb.ToString();
        }

        /// <summary>An ability talent: cost / range / cast / cooldown like the spellbook's tooltip (the AbilityView text when the spell is known).</summary>
        static void AppendAbility(UiTalentModel m, SpellInfo s)
        {
            AbilityView v = m.P != null ? UiContext.Find(m.P, s.Id) : null;
            if (v != null && !string.IsNullOrEmpty(v.Tooltip))
            {
                string[] lines = v.Tooltip.Split('\n');
                for (int i = 1; i <= 2 && i < lines.Length; i++) if (lines[i].Length > 0) Sb.Append("\n<color=#ffffff>").Append(lines[i]).Append("</color>");
                return;
            }
            var line = new StringBuilder();
            if (s.ManaCost > 0 && s.ManaCostPercentage == 0)
            {
                int cost = s.PowerType == 1 || s.PowerType == 6 ? s.ManaCost / 10 : s.ManaCost;
                line.Append(cost).Append(' ').Append(s.PowerType == 1 ? "Rage" : s.PowerType == 3 ? "Energy" : s.PowerType == 6 ? "Runic Power" : s.PowerType == 2 ? "Focus" : "Mana").Append("   ");
            }
            if (s.RangeMax > 5f) line.Append(s.RangeMin > 0 ? s.RangeMin.ToString("0") + "-" : "").Append(s.RangeMax.ToString("0")).Append(" yd range");
            else if (s.RangeMax > 0f) line.Append("Melee Range");
            if (line.Length > 0) Sb.Append("\n<color=#ffffff>").Append(line).Append("</color>");
            Sb.Append("\n<color=#ffffff>").Append(s.IsChanneled ? "Channeled" : s.CastTimeMs > 0 ? (s.CastTimeMs / 1000f).ToString("0.#") + " sec cast" : "Instant");
            int cd = Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs);
            if (cd > 0) Sb.Append("   ").Append(SpellText.DurationText(cd)).Append(" cooldown");
            Sb.Append("</color>");
        }
    }

    /// <summary>
    /// One talent tree: PlayerTalentFrameScrollFrame (296 x 328 at (23, 77); 306 tall while the preview bar shows) with the tree's four background
    /// pieces (Interface\TalentFrame\&lt;BackgroundFile&gt;-TopLeft 256x252 / -TopRight 40x252 (0..0.6875) / -BottomLeft 256x76 (0..0.5859375) /
    /// -BottomRight, Blizzard_TalentUI.xml:433-470, desaturated for an inactive spec), the scroll child (320 wide) with up to 40 talent buttons
    /// at (Col * 63 + 35, Row * 63 + 20), the branch pieces under them and the arrows over them (TalentBranches). Scrolls by half its height
    /// (ScrollFrame_OnMouseWheel / PlayerTalentFrameDownArrow_OnClick) with the arrow buttons or the mouse wheel. In VR a tap adds a preview
    /// point and selects the talent; small "+" / "-" buttons beside it are VR's right click (ours).
    /// </summary>
    public sealed class UiTalentTree
    {
        public const float FrameX = 23f, FrameY = 77f, FrameW = 296f, FrameH = 328f, ChildW = 320f;
        const int MaxButtons = 40, MaxBranch = TalentBranches.MaxBranchTextures, MaxArrow = TalentBranches.MaxArrowTextures;
        const string Branches = @"Interface\TalentFrame\UI-TalentBranches.blp", Arrows = @"Interface\TalentFrame\UI-TalentArrows.blp";
        const string EmptySlotWhite = @"Interface\Buttons\UI-EmptySlot-White.blp", RankBorder = @"Interface\TalentFrame\TalentFrame-RankBorder.blp";
        static readonly Color GreenFont = new Color(0.1f, 1f, 0.1f);   // GREEN_FONT_COLOR

        sealed class Slot
        {
            public UiButton B;
            public RawImage Back, Normal, Border;
            public TextMeshProUGUI Rank;
            public TalentInfo T;
            public string IconPath;
            public bool Grey, IconShown;
            public float Y;
        }

        public readonly RectTransform Root;
        public Action<TalentInfo, bool> Clicked;            // (talent, right / VR minus)
        public Func<TalentInfo, string> Tip;
        readonly UiContext _ctx;
        readonly RawImage[] _bg = new RawImage[4];
        readonly RectTransform _child, _branchLayer, _slotLayer, _arrowLayer;
        readonly Slot[] _slots = new Slot[MaxButtons];
        readonly RawImage[] _branch = new RawImage[MaxBranch], _arrow = new RawImage[MaxArrow];
        readonly UiButton _up, _down, _plus, _minus;
        readonly List<TalentQuad> _quads = new List<TalentQuad>();
        readonly List<string> _problems = new List<string>();
        readonly HashSet<int> _loggedProblems = new HashSet<int>();
        string _bgShown;
        bool _bgGrey;
        float _scroll, _contentH = FrameH, _frameH = FrameH;
        TalentInfo _selected;
        TalentTabInfo _tab;

        public UiTalentTree(RectTransform parent, UiContext ctx)
        {
            _ctx = ctx;
            Root = UiKit.Rect(parent, "PlayerTalentFrameScrollFrame", FrameX, FrameY, FrameW, FrameH);
            _bg[0] = UiKit.Pic(Root, "BackgroundTopLeft", null, 0f, 1f, 0f, 1f, 0f, 0f, 256f, 252f);
            _bg[1] = UiKit.Pic(Root, "BackgroundTopRight", null, 0f, 0.6875f, 0f, 1f, 256f, 0f, 40f, 252f);
            _bg[2] = UiKit.Pic(Root, "BackgroundBottomLeft", null, 0f, 1f, 0f, 0.5859375f, 0f, 252f, 256f, 76f);
            _bg[3] = UiKit.Pic(Root, "BackgroundBottomRight", null, 0f, 0.6875f, 0f, 0.5859375f, 256f, 252f, 40f, 76f);
            RectTransform clip = UiKit.Rect(Root, "Clip", 0f, 0f, FrameW, FrameH);
            clip.gameObject.AddComponent<RectMask2D>();
            _child = UiKit.Rect(clip, "PlayerTalentFrameScrollChildFrame", 0f, 0f, ChildW, FrameH);
            _branchLayer = UiKit.Rect(_child, "Branches", 0f, 0f, ChildW, FrameH);
            _slotLayer = UiKit.Rect(_child, "Talents", 0f, 0f, ChildW, FrameH);
            _arrowLayer = UiKit.Rect(_child, "Arrows", 0f, 0f, ChildW, FrameH);
            Texture2D br = UiArt.Tex(Branches), ar = UiArt.Tex(Arrows);
            for (int i = 0; i < MaxBranch; i++) { _branch[i] = UiKit.Pic(_branchLayer, "Branch " + (i + 1), br, 0f, 0f, 32f, 32f); _branch[i].enabled = false; }
            Texture2D slot = UiArt.Tex(EmptySlotWhite), border = UiArt.Tex(RankBorder), normal = UiArt.Tex(UiArt.Quickslot);
            for (int i = 0; i < MaxButtons; i++)
            {
                var s = new Slot();
                int k = i;
                s.B = UiKit.Button(_slotLayer, "PlayerTalentFrameTalent" + (i + 1), 0f, 0f, 37f, 37f);
                s.Back = UiKit.Pic(s.B.transform, "Slot", slot, -13.5f, -13.5f, 64f, 64f);
                s.B.Icon = UiKit.Pic(s.B.transform, "Icon", null, 0f, 0f, 37f, 37f);
                s.Normal = UiKit.Pic(s.B.transform, "Normal", normal, -13.5f, -12.5f, 64f, 64f);
                s.B.Pushed = UiKit.Pic(s.B.transform, "Pushed", UiArt.Tex(UiArt.QuickslotDepress), 0f, 0f, 37f, 37f);
                s.B.Pushed.enabled = false;
                s.B.Hilite = UiKit.Pic(s.B.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 37f, 37f);
                s.B.Hilite.enabled = false;
                s.Border = UiKit.Pic(s.B.transform, "RankBorder", border, 21f, 21f, 32f, 32f);
                s.Rank = UiKit.Text(s.B.transform, "Rank", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 21f, 29f, 32f, 15f);
                s.Rank.textWrappingMode = TextWrappingModes.NoWrap;
                s.B.Click = () => Press(k, false);
                s.B.RightClick = () => Press(k, true);
                s.B.Tip = () => _slots[k].T != null && Tip != null ? Tip(_slots[k].T) : null;
                s.B.gameObject.SetActive(false);
                _slots[i] = s;
            }
            for (int i = 0; i < MaxArrow; i++) { _arrow[i] = UiKit.Pic(_arrowLayer, "Arrow " + (i + 1), ar, 0f, 0f, 32f, 32f); _arrow[i].enabled = false; }
            // VR's right click: "+" / "-" beside the selected talent (ours)
            _plus = UiKit.PanelButton(_arrowLayer, "+", 0f, 0f, 22f, 18f, () => { if (_selected != null && Clicked != null) Clicked(_selected, false); });
            _minus = UiKit.PanelButton(_arrowLayer, "-", 0f, 0f, 22f, 18f, () => { if (_selected != null && Clicked != null) Clicked(_selected, true); });
            _plus.gameObject.SetActive(false);
            _minus.gameObject.SetActive(false);
            // UIPanelScrollFrameTemplate's arrows (ours: two 24-unit arrow buttons at the frame's right edge)
            _up = UiKit.ArtButton(parent, "PlayerTalentFrameScrollFrameScrollBarScrollUpButton", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, FrameX + FrameW + 3f, FrameY, 24f, () => ScrollBy(-1));
            _down = UiKit.ArtButton(parent, "PlayerTalentFrameScrollFrameScrollBarScrollDownButton", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, FrameX + FrameW + 3f, FrameY + FrameH - 24f, 24f, () => ScrollBy(1));
        }

        public void Show(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            if (!on) { _up.gameObject.SetActive(false); _down.gameObject.SetActive(false); }
        }

        /// <summary>The visible height: 328, or 306 while the preview bar squeezes the points bar up (Blizzard_TalentUI.lua:511).</summary>
        public void SetFrameHeight(float h)
        {
            if (Mathf.Abs(h - _frameH) < 0.5f) return;
            _frameH = h;
            UiKit.Size(Root, FrameW, h);
            UiKit.Size((RectTransform)_child.parent, FrameW, h);
            UiKit.Move(_down.Rt, FrameX + FrameW + 3f, FrameY + h - 24f);
            ScrollTo(_scroll);
        }

        /// <summary>The mouse wheel over the frame (desktop): WoW scrolls by half the frame's height.</summary>
        public void Wheel(float delta)
        {
            if (delta > 0.01f) ScrollBy(-1); else if (delta < -0.01f) ScrollBy(1);
        }

        Canvas _canvas;

        /// <summary>Desktop: the mouse is over the visible tree.</summary>
        public bool Hovered
        {
            get
            {
                if (!Root.gameObject.activeInHierarchy) return false;
                if (_canvas == null) _canvas = Root.GetComponentInParent<Canvas>();
                Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
                return RectTransformUtility.RectangleContainsScreenPoint(Root, Input.mousePosition, cam);
            }
        }

        void ScrollBy(int dir)
        {
            UiSound.Named("UChatScrollButton");                     // PlayerTalentFrameDownArrow_OnClick
            ScrollTo(_scroll + dir * _frameH * 0.5f);
        }

        void ScrollTo(float y)
        {
            float max = Mathf.Max(0f, _contentH - _frameH);
            _scroll = Mathf.Clamp(y, 0f, max);
            UiKit.Move(_child, 0f, -_scroll);
            _up.Interactable = _scroll > 0.5f;
            _down.Interactable = _scroll < max - 0.5f;
            bool bar = max > 0.5f && Root.gameObject.activeSelf;
            _up.gameObject.SetActive(bar);
            _down.gameObject.SetActive(bar);
            for (int i = 0; i < MaxButtons; i++)
            {
                var s = _slots[i];
                if (s.T == null) continue;
                float top = s.Y - _scroll, bottom = top + 37f;
                bool any = bottom > 0f && top < _frameH, full = top >= -0.5f && bottom <= _frameH + 0.5f;
                if (s.B.gameObject.activeSelf != any) s.B.gameObject.SetActive(any);
                s.B.Interactable = full;                            // VR's hit test ignores the mask: half-hidden buttons take no presses (ours)
            }
            PlaceVrButtons();
        }

        void Press(int k, bool right)
        {
            var t = _slots[k].T;
            if (t == null || Clicked == null) return;
            if (_ctx != null && _ctx.Vr) { _selected = t; PlaceVrButtons(); }
            Clicked(t, right);
        }

        void PlaceVrButtons()
        {
            bool show = _ctx != null && _ctx.Vr && _selected != null && _tab != null && _tab.Talents.Contains(_selected);
            if (show)
            {
                float x = TalentBranches.ButtonX(_selected) + 39f, y = TalentBranches.ButtonY(_selected);
                UiKit.Move(_plus.Rt, x, y - 1f);
                UiKit.Move(_minus.Rt, x, y + 19f);
                float top = y - _scroll;
                show = top >= -0.5f && top + 37f <= _frameH + 0.5f;
            }
            if (_plus.gameObject.activeSelf != show) _plus.gameObject.SetActive(show);
            if (_minus.gameObject.activeSelf != show) _minus.gameObject.SetActive(show);
        }

        /// <summary>Draws a tree (TalentFrame_Update): buttons, colours, rank texts, branches and arrows.</summary>
        public void Draw(TalentTabInfo tab, UiTalentModel m, bool resetScroll)
        {
            if (!ReferenceEquals(tab, _tab)) { _tab = tab; _selected = null; resetScroll = true; }
            if (tab == null || m == null) { for (int i = 0; i < MaxButtons; i++) { _slots[i].T = null; _slots[i].B.gameObject.SetActive(false); } HideQuads(); PlaceVrButtons(); return; }
            // background (TalentFrameBase.lua:116-137), desaturated when the shown group is not the active one (ours: darkened - uGUI has no desaturation)
            string bgBase = @"Interface\TalentFrame\" + (string.IsNullOrEmpty(tab.Background) ? "MageFire" : tab.Background) + "-";
            if (bgBase != _bgShown || _bgGrey != !m.Active)
            {
                _bgShown = bgBase;
                _bgGrey = !m.Active;
                string[] part = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
                Color tint = _bgGrey ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
                for (int i = 0; i < 4; i++) { _bg[i].texture = UiArt.Tex(bgBase + part[i] + ".blp"); _bg[i].color = _bg[i].texture != null ? tint : new Color(0f, 0f, 0f, 0f); }
            }
            int maxRow = 0;
            int n = Math.Min(tab.Talents.Count, MaxButtons);
            for (int i = 0; i < MaxButtons; i++)
            {
                var s = _slots[i];
                if (i >= n) { s.T = null; if (s.B.gameObject.activeSelf) s.B.gameObject.SetActive(false); continue; }
                var t = tab.Talents[i];
                s.T = t;
                if (t.Row > maxRow) maxRow = t.Row;
                float x = TalentBranches.ButtonX(t), y = TalentBranches.ButtonY(t);
                s.Y = y;
                UiKit.Move(s.B.Rt, x, y);
                int rank = m.Rank(t.Id);
                bool met = m.RequirementsMet(t);
                var sp = m.Spells != null ? m.Spells.Find(TalentDb.RankSpell(t, 1)) : null;
                string icon = sp != null ? sp.IconPath : null;
                if (!ReferenceEquals(icon, s.IconPath) || s.Grey == met) { s.IconPath = icon; s.Grey = !met; s.IconShown = false; }
                s.Rank.text = rank.ToString(CultureInfo.InvariantCulture);
                if (met)
                {
                    s.Back.color = rank < t.MaxRank ? new Color(0.1f, 1f, 0.1f) : new Color(1f, 0.82f, 0f);   // TalentFrameBase.lua:199-205
                    s.Rank.color = rank < t.MaxRank ? GreenFont : UiArt.Gold;
                    s.Border.color = Color.white;
                    s.Border.enabled = true; s.Rank.enabled = true;
                }
                else
                {
                    s.Back.color = new Color(0.5f, 0.5f, 0.5f);                                             // :211-219
                    bool show = rank > 0;
                    s.Border.enabled = show; s.Rank.enabled = show;
                    s.Border.color = new Color(0.5f, 0.5f, 0.5f);
                    s.Rank.color = UiArt.Grey;
                }
            }
            _contentH = Mathf.Max(FrameH, (maxRow + 1) * TalentBranches.ButtonStep + TalentBranches.OffsetY + 20f);
            UiKit.Size(_child, ChildW, _contentH);
            // branches and arrows (TalentFrame_SetPrereqs / DrawLines / Update)
            _quads.Clear(); _problems.Clear();
            TalentBranches.Build(tab, m.RequirementsMet, _quads, _problems);
            if (_problems.Count > 0 && _loggedProblems.Add(tab.Id)) WowLog.Warn("UI talents: tree " + tab.Id + " " + tab.Name + ": " + string.Join("; ", _problems.ToArray()));
            int b = 0, a = 0;
            for (int i = 0; i < _quads.Count; i++)
            {
                var q = _quads[i];
                RawImage img = q.Arrow ? (a < MaxArrow ? _arrow[a++] : null) : (b < MaxBranch ? _branch[b++] : null);
                if (img == null) continue;
                UiKit.Coords(img, q.L, q.R, q.T, q.B);
                UiKit.Move(img.rectTransform, q.X, q.Y);
                img.enabled = img.texture != null;
            }
            for (int i = b; i < MaxBranch; i++) _branch[i].enabled = false;
            for (int i = a; i < MaxArrow; i++) _arrow[i].enabled = false;
            ScrollTo(resetScroll ? 0f : _scroll);
            TickIcons();
        }

        void HideQuads()
        {
            for (int i = 0; i < MaxBranch; i++) _branch[i].enabled = false;
            for (int i = 0; i < MaxArrow; i++) _arrow[i].enabled = false;
        }

        /// <summary>Every frame: icons that finished loading (colour icons through UiArt.Icon, grey ones through UiTalentIcons).</summary>
        public void TickIcons()
        {
            UiTalentIcons.Pump(4);
            for (int i = 0; i < MaxButtons; i++)
            {
                var s = _slots[i];
                if (s.T == null || s.IconShown) continue;
                Texture2D t = s.Grey ? UiTalentIcons.Grey(s.IconPath ?? UiArt.QuestionMark) : UiArt.Icon(s.IconPath);
                if (t == null) continue;
                s.B.Icon.texture = t;
                s.B.Icon.color = Color.white;
                UiKit.Coords(s.B.Icon, 0f, 1f, 0f, 1f);
                s.IconShown = true;
            }
        }
    }
}
