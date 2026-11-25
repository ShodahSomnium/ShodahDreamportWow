// iteration 11 - owner avatar (proposal-avatar.md 6.A, phase A2): the "armour only" look of a WoW character - the body texture
// composed with ONLY the armour's texture components (no skin, underwear, face or hair: those pixels stay fully transparent) and
// the armour geosets. Pure (no UnityEngine): runs on a worker thread and in the harness (probe ArmShellLook).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Armour
{
    /// <summary>
    /// CharacterComposer.Compose with an "armour only" mode, kept here (Models belongs to another owner): the armour rules of
    /// CharacterComposer.Dress (WMV 0.7.0.5 AddEquipment - mirrored line for line: slot order, layer 10 + k, texture regions,
    /// geoset groups, robe / gloves rules) blitted over a FULLY TRANSPARENT base with alpha kept ("over" compositing), so the shell's
    /// alpha test (0.5) drops every pixel no armour paints. Geosets: 0 (the body, alpha-tested) and the groups armour drives
    /// (4 gloves, 5 boots, 8 sleeves, 9 kneepads, 10 / 11 / 13 trousers and robe, 12 tabard, 14 / 16, 15 cape, 18 belt) at the
    /// variant the items chose; hair (1-99), facial hair (1xx-3xx), ears (7xx) and eye glow (17xx) never.
    /// Belt (ours, measured): Dress sets no group 18; ArmShellGeo counts 65 waist items with GeosetGroup[0] = 1 and HumanMale's only
    /// group-18 geoset is 1802 (97 verts on the waist / pelvis bones) - so group 18 = 1 + the waist's GeosetGroup[0] (the buckle).
    /// </summary>
    public sealed class ArmourShellLook
    {
        public const int Size = CharacterComposer.TextureSize;
        /// <summary>Item slots read (CharacterComposer slot ids = PlayerState.Equipment indices).</summary>
        public static readonly int[] Slots =
        {
            CharacterComposer.SlotShirt, CharacterComposer.SlotHead, CharacterComposer.SlotShoulders, CharacterComposer.SlotLegs,
            CharacterComposer.SlotFeet, CharacterComposer.SlotChest, CharacterComposer.SlotTabard, CharacterComposer.SlotWaist,
            CharacterComposer.SlotWrists, CharacterComposer.SlotHands, CharacterComposer.SlotBack
        };

        public string ModelPath;
        public byte[] Rgba;                     // Size x Size RGBA32, row 0 = v 0; alpha 0 where no armour paints
        public string CapeTexture;              // replaceable type 2 when a cape is worn (and not hidden)
        public readonly int[] Group = new int[19];
        public readonly List<string> Layers = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public int Opaque;                      // pixels with alpha >= 128 (0 = nothing to draw on the body)
        /// <summary>Lead 2026-11-04: the share (0..1) of each CharacterComposer.Region of the body texture the armour paints (alpha >= 128) -
        /// "Hide avatar part" only hides what the armour really covers (ArmourShell.Worn). Measured on the user's NE druid start outfit
        /// (robe 6123 + trousers 6124): arm upper 0.40, arm lower 0, hand 0, torso upper 0.80, torso lower 0.99, legs 1.0, foot 0.</summary>
        public readonly float[] Cover = new float[11];
        public int Race, Gender;

        /// <summary>True when the look draws anything (painted pixels or a cape).</summary>
        public bool Any { get { return Opaque > 0 || CapeTexture != null; } }

        /// <summary>The armour-only geoset rule (see the class summary).</summary>
        public bool ShowGeoset(int id)
        {
            if (id == 0) return true;
            if (id < 100) return false;
            int g = id / 100, v = id % 100;
            if (g >= Group.Length || v == 0) return false;
            if (g <= 3 || g == 7 || g == 17) return false;
            return v == Group[g];
        }

        /// <summary>
        /// The armour-only look of the model of race / gender (the caller picks the race: Human of the wearer's gender by default,
        /// as the helm) wearing items (ItemDisplayInfo ids by equipment slot 0..18; 0 = empty). hideCloak = the wearer's Hide Cloak.
        /// </summary>
        public static ArmourShellLook Compose(WowFiles files, int race, int gender, int[] items, bool hideCloak)
        {
            var res = new ArmourShellLook { Race = race, Gender = gender == 1 ? 1 : 0 };
            gender = res.Gender;
            res.ModelPath = WowModelDb.RaceModelPath(files, race, gender);
            int[] g = res.Group;
            // CharacterComposer.Compose's defaults: every group 1, facial groups 0, ears 2 (unused here)
            for (int i = 1; i < g.Length; i++) g[i] = 1;
            g[1] = g[2] = g[3] = 0;
            g[7] = 2;
            var layers = new List<KeyValuePair<int, KeyValuePair<CharacterComposer.Region, string>>>();
            var idi = Dbc.Load(files, "ItemDisplayInfo");
            if (idi != null && items != null) Dress(files, gender, items, idi, res, layers, hideCloak);
            var dst = new byte[Size * Size * 4];   // all zero = fully transparent
            for (int l = 0; l <= 40; l++)
                foreach (var kv in layers)
                {
                    if (kv.Key != l) continue;
                    if (Blit(files, kv.Value.Value, kv.Value.Key, dst, res)) res.Layers.Add(kv.Value.Key + ": " + kv.Value.Value);
                }
            int opaque = 0;
            for (int i = 3; i < dst.Length; i += 4) if (dst[i] >= 128) opaque++;
            res.Opaque = opaque;
            res.Rgba = dst;
            for (int ri = 1; ri < res.Cover.Length; ri++)
            {
                int rx = CharacterComposer.Regions[ri, 0], ry = CharacterComposer.Regions[ri, 1], rw = CharacterComposer.Regions[ri, 2], rh = CharacterComposer.Regions[ri, 3], op = 0;
                for (int y = ry; y < ry + rh; y++) for (int x = rx; x < rx + rw; x++) if (dst[(y * Size + x) * 4 + 3] >= 128) op++;
                res.Cover[ri] = (float)op / (rw * rh);
            }
            return res;
        }

        // CharacterComposer.Dress (private there), mirrored; + the belt group (ours, measured) and Hide Cloak.
        private static void Dress(WowFiles files, int gender, int[] items, DbcTable idi, ArmourShellLook res,
                                  List<KeyValuePair<int, KeyValuePair<CharacterComposer.Region, string>>> layers, bool hideCloak)
        {
            int[] g = res.Group;
            for (int k = 0; k < Slots.Length; k++)
            {
                int slot = Slots[k], layer = 10 + k;
                int r = slot < items.Length && items[slot] > 0 ? idi.FindRow(items[slot]) : -1;
                if (r < 0) continue;
                if (slot == CharacterComposer.SlotLegs && g[13] == 2) continue;   // a robe hides the trousers
                int row = r;
                Action<CharacterComposer.Region, int> layerOf = (region, ti) =>
                {
                    string p = CharacterComposer.ItemTexturePath(files, region, idi.GetString(row, DbcFields.ItemDisplayInfo.Texture + ti), gender);
                    if (p != null) layers.Add(new KeyValuePair<int, KeyValuePair<CharacterComposer.Region, string>>(layer, new KeyValuePair<CharacterComposer.Region, string>(region, p)));
                };
                Func<int, int> geo = i => idi.GetInt(row, DbcFields.ItemDisplayInfo.GeosetGroup + i);
                switch (slot)
                {
                    case CharacterComposer.SlotShirt:
                    case CharacterComposer.SlotChest:
                        g[8] = 1 + geo(0);
                        layerOf(CharacterComposer.Region.ArmUpper, 0); layerOf(CharacterComposer.Region.ArmLower, 1);
                        layerOf(CharacterComposer.Region.TorsoUpper, 3); layerOf(CharacterComposer.Region.TorsoLower, 4);
                        if (geo(2) == 1) { layerOf(CharacterComposer.Region.LegUpper, 5); layerOf(CharacterComposer.Region.LegLower, 6); }
                        break;
                    case CharacterComposer.SlotWaist:
                        layerOf(CharacterComposer.Region.TorsoLower, 4); layerOf(CharacterComposer.Region.LegUpper, 5);
                        g[18] = 1 + geo(0);   // ours (measured): the belt buckle 1802
                        break;
                    case CharacterComposer.SlotWrists: layerOf(CharacterComposer.Region.ArmLower, 1); break;
                    case CharacterComposer.SlotLegs: g[9] = 1 + geo(1); layerOf(CharacterComposer.Region.LegUpper, 5); layerOf(CharacterComposer.Region.LegLower, 6); break;
                    case CharacterComposer.SlotHands: g[4] = 1 + geo(0); layerOf(CharacterComposer.Region.Hand, 2); layerOf(CharacterComposer.Region.ArmLower, 1); break;
                    case CharacterComposer.SlotFeet: g[5] = 1 + geo(0); layerOf(CharacterComposer.Region.LegLower, 6); layerOf(CharacterComposer.Region.Foot, 7); break;
                    case CharacterComposer.SlotTabard: g[12] = 2; layerOf(CharacterComposer.Region.TorsoUpper, 3); layerOf(CharacterComposer.Region.TorsoLower, 4); break;
                    case CharacterComposer.SlotBack:
                        if (hideCloak) break;
                        g[15] = 1 + geo(0);
                        string cape = idi.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture);
                        if (!string.IsNullOrEmpty(cape)) res.CapeTexture = @"Item\ObjectComponents\Cape\" + cape + ".blp";
                        break;
                }
                if (g[13] == 1) g[13] = 1 + geo(2);   // robe
                if (g[13] == 2) { g[5] = 0; g[12] = 0; }  // robe hides boots and tabard
                if (g[4] > 1) g[8] = 0;                   // gloves over sleeves
            }
            if (res.CapeTexture != null && g[15] <= 1) res.CapeTexture = null;
        }

        /// <summary>One BLP onto its region with Porter-Duff "over" (alpha kept). False when unreadable.</summary>
        private static bool Blit(WowFiles files, string path, CharacterComposer.Region region, byte[] dst, ArmourShellLook res)
        {
            byte[] bytes = files.Read(path);
            if (bytes == null) { res.Notes.Add("missing " + path); return false; }
            BlpImage img;
            try { img = BlpImage.Decode(bytes); }
            catch (Exception e) { res.Notes.Add("undecodable " + path + ": " + e.Message); return false; }
            byte[] src = img.GetRgba32(0);
            int ri = (int)region;
            int rx = CharacterComposer.Regions[ri, 0], ry = CharacterComposer.Regions[ri, 1], rw = CharacterComposer.Regions[ri, 2], rh = CharacterComposer.Regions[ri, 3];
            int sw = img.Width, sh = img.Height;
            var px = new float[4];
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    // CharacterComposer.BlitRgba's bilinear sampling (the armour textures are half their region's size, measured
                    // by the probe ArmShellLook: e.g. Plate_C_01Gold_Chest_TU 128x64 for TorsoUpper 256x128)
                    float fx = (x + 0.5f) * sw / rw - 0.5f, fy = (y + 0.5f) * sh / rh - 0.5f;
                    int x0 = Math.Max(0, Math.Min(sw - 1, (int)Math.Floor(fx))), y0 = Math.Max(0, Math.Min(sh - 1, (int)Math.Floor(fy)));
                    int x1 = Math.Min(sw - 1, x0 + 1), y1 = Math.Min(sh - 1, y0 + 1);
                    float tx = Math.Max(0f, Math.Min(1f, fx - x0)), ty = Math.Max(0f, Math.Min(1f, fy - y0));
                    for (int c = 0; c < 4; c++)
                    {
                        float p00 = src[(y0 * sw + x0) * 4 + c], p10 = src[(y0 * sw + x1) * 4 + c], p01 = src[(y1 * sw + x0) * 4 + c], p11 = src[(y1 * sw + x1) * 4 + c];
                        float top = p00 + (p10 - p00) * tx, bot = p01 + (p11 - p01) * tx;
                        px[c] = top + (bot - top) * ty;
                    }
                    float a = px[3] / 255f;
                    if (a <= 0f) continue;
                    int di = ((ry + y) * Size + rx + x) * 4;
                    float da = dst[di + 3] / 255f, oa = a + da * (1f - a);
                    for (int c = 0; c < 3; c++)
                        dst[di + c] = (byte)Math.Min(255f, (px[c] * a + dst[di + c] * da * (1f - a)) / oa + 0.5f);
                    dst[di + 3] = (byte)Math.Min(255f, oa * 255f + 0.5f);
                }
            return true;
        }

        /// <summary>One line for the log: the groups that differ from bare, the layer count, the painted share.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            int[] show = { 4, 5, 8, 9, 12, 13, 15, 18 };
            foreach (int k in show) if (Group[k] > 1) parts.Add(k * 100 + Group[k] + "");
            return "geosets 0" + (parts.Count > 0 ? "+" + string.Join("+", parts.ToArray()) : "") + ", " + Layers.Count + " texture layers, " +
                   (Opaque * 100 / (Size * Size)) + "% of the body texture painted" + (CapeTexture != null ? ", cape" : "") +
                   (Notes.Count > 0 ? ", notes: " + string.Join("; ", Notes.GetRange(0, Math.Min(3, Notes.Count)).ToArray()) : "");
        }
    }
}
