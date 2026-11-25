// iteration 11 - owner talents (new file): WoW's GlyphFrame inside the Talents window (proposal-talents.md 3.5 / 4.2).
// FrameXML of the player's install (P/harness/out_d11talents/): Blizzard_GlyphUI.xml:108-210 (GlyphFrame 384x512: background UI-GlyphFrame
// 352x441 texcoords 0..0.6875 x 0..0.861328125, title GLYPHS TOP (0,-18), glow UI-GlyphFrame-Glow at (-9,-38); six 90x90 GlyphTemplate buttons:
// Glyph1 CENTER (-15,140), Glyph2 CENTER (-14,-103), Glyph3 TOPLEFT (28,-133), Glyph4 BOTTOMRIGHT (-56,168), Glyph5 TOPRIGHT (-56,-133),
// Glyph6 BOTTOMLEFT (26,168)), Blizzard_GlyphUI.lua (GLYPH_SLOTS texcoords :8-18, GLYPH_MAJOR / GLYPH_MINOR tints :4-5, GlyphFrameGlyph_UpdateSlot
// :60-104, _SetGlyphType :105-146 (major setting 108 / ring 82 / background 70, minor 86 / 62 / 64), _OnUpdate pulses :159-224, _OnClick
// :225-251: left = PlaceGlyphInSocket (CONFIRM_GLYPH_PLACEMENT when a filled socket matches the waiting glyph), Shift + right = CONFIRM_REMOVE_GLYPH
// on the active spec; sounds Glyph_Major/MinorCreate / Destroy).
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiGlyphFrame
    {
        const string FrameArt = @"Interface\Spellbook\UI-GlyphFrame.blp", GlowArt = @"Interface\Spellbook\UI-GlyphFrame-Glow.blp";
        const string LockedArt = @"Interface\Spellbook\UI-GlyphFrame-Locked.blp", Rune1 = @"Interface\Spellbook\UI-Glyph-Rune1.blp";
        /// <summary>Socket 0..5 (= GlyphFrame button id - 1) centres in frame units, y down (from the XML anchors above).</summary>
        static readonly Vector2[] Centre = { new Vector2(177f, 116f), new Vector2(178f, 359f), new Vector2(73f, 178f), new Vector2(283f, 299f), new Vector2(283f, 178f), new Vector2(71f, 299f) };
        /// <summary>GLYPH_SLOTS (Blizzard_GlyphUI.lua:8-18): [0] empty, [1..6] by button id.</summary>
        static readonly Vector4[] SlotCoords =
        {
            new Vector4(0.78125f, 0.91015625f, 0.69921875f, 0.828125f),
            new Vector4(0f, 0.12890625f, 0.87109375f, 1f), new Vector4(0.130859375f, 0.259765625f, 0.87109375f, 1f),
            new Vector4(0.392578125f, 0.521484375f, 0.87109375f, 1f), new Vector4(0.5234375f, 0.65234375f, 0.87109375f, 1f),
            new Vector4(0.26171875f, 0.390625f, 0.87109375f, 1f), new Vector4(0.654296875f, 0.783203125f, 0.87109375f, 1f),
        };
        /// <summary>GlyphSlot.dbc types of sockets 0..5 (measured: ids 21..26 = major, minor, minor, major, minor, major) when the field is not known (offline).</summary>
        static readonly int[] DefaultType = { 0, 1, 1, 0, 1, 0 };
        static readonly Color MajorTint = new Color(1f, 0.25f, 0f), MinorTint = new Color(0f, 0.25f, 1f);   // GLYPH_MAJOR / GLYPH_MINOR

        sealed class Socket
        {
            public UiButton B;
            public RawImage Setting, Highlight, Background, Glyph, Ring, Shine;
            public int Type = -1, GlyphId = -1;
            public bool Locked, Matches, Configured;
            public float Elapsed, Tint;
        }

        public readonly RectTransform Root;
        /// <summary>(socket, remove): a left click / tap, or Shift + right click.</summary>
        public Action<int, bool> Clicked;
        public Func<int, string> Tip;
        readonly Socket[] _s = new Socket[6];
        readonly RawImage _glow;
        readonly TextMeshProUGUI _title;
        float _glowT = -1f;

        public UiGlyphFrame(RectTransform parent, UiContext ctx)
        {
            Root = UiKit.Rect(parent, "GlyphFrame", 0f, 0f, 384f, 512f);
            Texture2D art = UiArt.Tex(FrameArt);
            UiKit.Pic(Root, "GlyphFrameBackground", art, 0f, 0.6875f, 0f, 0.861328125f, 0f, 0f, 352f, 441f);
            _title = UiKit.Text(Root, "GlyphFrameTitleText", UiTalentText.G("GLYPHS", "Glyphs"), 13f, UiArt.Gold, TextAlignmentOptions.Center, 78f, 18f, 240f, 17f);   // GS 3797
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                var s = new Socket();
                s.B = UiKit.Button(Root, "GlyphFrameGlyph" + (i + 1), Centre[i].x - 45f, Centre[i].y - 45f, 90f, 90f);
                s.Setting = UiKit.Pic(s.B.transform, "Setting", art, 0f, 0f, 86f, 86f);
                s.Highlight = UiKit.Pic(s.B.transform, "Highlight", UiArt.Add(FrameArt), 0f, 0f, 86f, 86f);
                s.Highlight.enabled = false;
                s.Background = UiKit.Pic(s.B.transform, "Background", art, 0f, 0f, 64f, 64f);
                s.Glyph = UiKit.Pic(s.B.transform, "Glyph", UiArt.Tex(Rune1), 0f, 0f, 53f, 53f);
                s.Ring = UiKit.Pic(s.B.transform, "Ring", art, 0f, 0f, 62f, 62f);
                s.Shine = UiKit.Pic(s.B.transform, "Shine", art, 0f, 0f, 16f, 16f);
                Place(s.Glyph, 53f, 0f, 0f);
                Place(s.Shine, 16f, -9f, -12f);
                s.B.Click = () => { if (Clicked != null) Clicked(k, false); };
                s.B.RightClick = () => { if (Clicked != null && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) Clicked(k, true); };
                s.B.Tip = () => Tip != null ? Tip(k) : null;
                _s[i] = s;
            }
            _glow = UiKit.Pic(Root, "GlyphFrameGlow", UiArt.Add(GlowArt), 0f, 0.6875f, 0f, 0.861328125f, -9f, 38f, 352f, 441f);
            _glow.enabled = false;
            Root.gameObject.SetActive(false);
        }

        static void Place(RawImage img, float size, float dx, float dy)
        {
            UiKit.Size(img.rectTransform, size, size);
            UiKit.Move(img.rectTransform, 45f - size * 0.5f + dx, 45f - size * 0.5f + dy);
        }

        public void Show(bool on) { if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on); }
        public void SetTitle(string text) { if (_title.text != text) _title.text = text; }

        /// <summary>The socket type (0 major / 1 minor) shown for socket i.</summary>
        public static int TypeOf(TalentsView T, TalentDb db, int i)
        {
            int t = db != null && T.GlyphSlotIds[i] != 0 ? db.GlyphSlotType(T.GlyphSlotIds[i]) : -1;
            return t >= 0 ? t : DefaultType[i];
        }

        /// <summary>GlyphMatchesSocket: a glyph waits, the socket is open and of the glyph's type.</summary>
        public static bool Matches(TalentsView T, TalentDb db, int i)
        {
            if (!T.GlyphPick || (T.GlyphsEnabledMask & (1 << i)) == 0 || db == null) return false;
            var g = db.Glyph(T.GlyphPickGlyphId);
            return g != null && g.Type == TypeOf(T, db, i);
        }

        /// <summary>Every frame while shown: socket art (GlyphFrameGlyph_UpdateSlot / _SetGlyphType) and the pulses (_OnUpdate).</summary>
        public void Refresh(TalentsView T, TalentDb db, int spec, bool activeShown, bool sounds, float dt)
        {
            var glyphs = T.Specs[spec >= 0 && spec < 2 ? spec : 0].Glyphs;
            Texture2D art = UiArt.Tex(FrameArt);
            for (int i = 0; i < 6; i++)
            {
                var s = _s[i];
                int type = TypeOf(T, db, i);
                bool locked = (T.GlyphsEnabledMask & (1 << i)) == 0;
                int glyph = glyphs[i];
                bool matches = activeShown && Matches(T, db, i);
                if (type != s.Type || locked != s.Locked || glyph != s.GlyphId || !s.Configured)
                {
                    if (s.Configured && sounds && activeShown && !locked && glyph != s.GlyphId)
                    {
                        if (glyph != 0) { UiSound.Named(type == 0 ? "Glyph_MajorCreate" : "Glyph_MinorCreate"); _glowT = 0f; }   // GlyphFrame_OnEvent GLYPH_ADDED / UPDATED
                        else UiSound.Named(type == 0 ? "Glyph_MajorDestroy" : "Glyph_MinorDestroy");                             // GLYPH_REMOVED
                    }
                    s.Configured = true;
                    s.Type = type; s.Locked = locked; s.GlyphId = glyph;
                    bool major = type == 0;
                    float set = major ? 108f : 86f, ring = major ? 82f : 62f, back = major ? 70f : 64f;
                    Place(s.Setting, set, 0f, 0f);
                    Place(s.Highlight, set, 0f, 0f);
                    Place(s.Ring, ring, 0f, major ? 1f : -1f);
                    Place(s.Background, back, 0f, 0f);
                    if (major)
                    {
                        UiKit.Coords(s.Highlight, 0.740234375f, 0.953125f, 0.484375f, 0.697265625f);
                        UiKit.Coords(s.Ring, 0.767578125f, 0.92578125f, 0.32421875f, 0.482421875f);
                        UiKit.Coords(s.Shine, 0.9609375f, 1f, 0.9609375f, 1f);
                    }
                    else
                    {
                        UiKit.Coords(s.Highlight, 0.765625f, 0.927734375f, 0.15625f, 0.31640625f);
                        UiKit.Coords(s.Ring, 0.787109375f, 0.908203125f, 0.033203125f, 0.154296875f);
                        UiKit.Coords(s.Shine, 0.9609375f, 1f, 0.921875f, 0.9609375f);
                    }
                    s.Glyph.color = major ? MajorTint : MinorTint;
                    if (locked)
                    {
                        s.Setting.texture = UiArt.Tex(LockedArt);
                        UiKit.Coords(s.Setting, 0.1f, 0.9f, 0.1f, 0.9f);
                        s.Shine.enabled = s.Background.enabled = s.Glyph.enabled = s.Ring.enabled = false;
                    }
                    else
                    {
                        s.Setting.texture = art;
                        if (major) UiKit.Coords(s.Setting, 0.740234375f, 0.953125f, 0.484375f, 0.697265625f);
                        else UiKit.Coords(s.Setting, 0.765625f, 0.927734375f, 0.15625f, 0.31640625f);
                        s.Shine.enabled = s.Background.enabled = s.Ring.enabled = true;
                        var g = glyph != 0 && db != null ? db.Glyph(glyph) : null;
                        s.Glyph.enabled = glyph != 0;
                        if (glyph != 0)
                        {
                            Texture2D icon = g != null && !string.IsNullOrEmpty(g.IconPath) ? UiArt.Tex(g.IconPath.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? g.IconPath : g.IconPath + ".blp") : null;
                            s.Glyph.texture = icon ?? UiArt.Tex(Rune1);
                        }
                        Vector4 c = glyph != 0 ? SlotCoords[i + 1] : SlotCoords[0];
                        UiKit.Coords(s.Background, c.x, c.y, c.z, c.w);
                    }
                    s.Elapsed = 0f; s.Tint = 0f;
                }
                // GlyphFrameGlyph_OnUpdate: the setting breathes on a filled socket (6 s), a matching empty socket pulses (1.6 s)
                float settingA = 0.6f;
                if (!locked && glyph != 0)
                {
                    s.Elapsed += dt;
                    if (s.Elapsed >= 6f) s.Elapsed = 0f;
                    float e = s.Elapsed;
                    settingA = e <= 2f ? 0.6f + 0.4f * e / 2f : e >= 4f ? 1f - 0.4f * (e - 4f) / 2f : 1f;
                }
                SetAlpha(s.Setting, settingA);
                if (!locked && glyph == 0 && matches)
                {
                    s.Tint += dt;
                    if (s.Tint >= 1.6f) s.Tint = 0f;
                    float e = s.Tint, a = e <= 0.6f ? 1f - 0.6f * e / 0.6f : e >= 0.8f ? 0.4f + 0.6f * (e - 0.8f) / 0.8f : 0.4f;
                    Vector4 c = SlotCoords[i + 1];
                    UiKit.Coords(s.Background, c.x, c.y, c.z, c.w);
                    SetAlpha(s.Background, a);
                    s.Highlight.enabled = true;
                    SetAlpha(s.Highlight, s.B.Hovered ? 0.4f : 0.4f * a);
                }
                else
                {
                    if (s.Matches && glyph == 0 && !locked) { Vector4 c = SlotCoords[0]; UiKit.Coords(s.Background, c.x, c.y, c.z, c.w); }
                    SetAlpha(s.Background, 1f);
                    s.Highlight.enabled = s.B.Hovered && !locked;
                    SetAlpha(s.Highlight, 0.25f);
                }
                s.Matches = matches;
            }
            // GlyphFrame_PulseGlow: alpha 0 -> 1 in 0.1 s, 1 -> 0 in 1.5 s
            if (_glowT >= 0f)
            {
                _glowT += dt;
                float a = _glowT <= 0.1f ? _glowT / 0.1f : 1f - (_glowT - 0.1f) / 1.5f;
                if (a <= 0f) { _glowT = -1f; _glow.enabled = false; }
                else { _glow.enabled = _glow.texture != null; SetAlpha(_glow, a); }
            }
        }

        static void SetAlpha(RawImage img, float a)
        {
            Color c = img.color;
            if (Mathf.Abs(c.a - a) < 0.004f) return;
            c.a = a;
            img.color = c;
        }

        /// <summary>The glyph tooltip (GameTooltip:SetGlyph is C code - the lines are ours, from WoW's strings).</summary>
        public static string Tooltip(TalentsView T, TalentDb db, SpellDb spells, int spec, bool activeShown, int i, bool vr, int level)
        {
            if (T == null) return null;
            int type = TypeOf(T, db, i);
            string typeLine = type == 0 ? UiTalentText.G("MAJOR_GLYPH", "Major Glyph") : UiTalentText.G("MINOR_GLYPH", "Minor Glyph");   // GS 4883 / 4950
            var sb = new System.Text.StringBuilder(256);
            if ((T.GlyphsEnabledMask & (1 << i)) == 0)
            {
                sb.Append("<size=14><color=#808080>").Append(UiTalentText.G("GLYPH_LOCKED", "Locked")).Append("</color></size>");   // GS 3802
                sb.Append("\n<color=#ffffff>").Append(typeLine).Append("</color>");
                sb.Append("\n<color=#ff2020>").Append(UiTalentText.G("GLYPH_SLOT_TOOLTIP" + (i + 1), "Unlocked at level " + TalentDb.GlyphSocketLevel[i] + ".")).Append("</color>");   // GS 3804-3809
                return sb.ToString();
            }
            int glyph = T.Specs[spec >= 0 && spec < 2 ? spec : 0].Glyphs[i];
            var g = glyph != 0 && db != null ? db.Glyph(glyph) : null;
            var s = g != null && spells != null ? spells.Find(g.SpellId) : null;
            if (glyph == 0)
            {
                sb.Append("<size=14><color=#ffffff>").Append(UiTalentText.G("GLYPH_EMPTY", "Empty")).Append("</color></size>");   // GS 3798
                sb.Append("\n<color=#ffffff>").Append(typeLine).Append("</color>");
                sb.Append("\n<color=#ffd100>").Append(UiTalentText.G("GLYPH_EMPTY_DESC", "Use a Glyph from your inventory to inscribe your spellbook.")).Append("</color>");   // GS 3799
            }
            else
            {
                sb.Append("<size=14><color=#ffffff>").Append(s != null ? s.Name : "Glyph " + glyph).Append("</color></size>");
                sb.Append("\n<color=#ffffff>").Append(typeLine).Append("</color>");
                if (s != null)
                {
                    string d = SpellText.Resolve(spells, s, s.Description, new SpellTextContext { CasterLevel = Math.Max(1, level) });
                    if (!string.IsNullOrEmpty(d)) sb.Append("\n<color=#ffd100>").Append(d).Append("</color>");
                }
                if (activeShown && !T.GlyphPick)
                    sb.Append("\n<color=#00ff00><noparse>").Append(vr ? "Tap to remove (the glyph is destroyed)" : UiTalentText.G("GLYPH_SLOT_REMOVE_TOOLTIP", "<Shift Right Click to Remove>")).Append("</noparse></color>");   // GS 3803 (VR: ours)
            }
            if (activeShown && T.GlyphPick)
            {
                if (Matches(T, db, i)) sb.Append("\n<color=#00ff00>").Append(vr ? "Tap to inscribe the waiting glyph" : "Click to inscribe the waiting glyph").Append("</color>");   // ours
                else sb.Append("\n<color=#ff2020>").Append("That Glyph cannot be inscribed there.").Append("</color>");   // SPELL_FAILED_INVALID_GLYPH (GlobalStrings.lua:7068)
            }
            return sb.ToString();
        }
    }
}
