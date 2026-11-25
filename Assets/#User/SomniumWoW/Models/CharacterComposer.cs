using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Player-character look -> body texture, hair texture and visible geosets (pure, runs in the harness).
    /// Sources: WoW Model Viewer 0.7.0.5 (the 3.3.5-era tool) CharControl::RefreshModel / CharTexture::compose and its region
    /// table (charcontrol.h, 256x256 layout x REGION_FAC 2 = 512x512), CharSections section types 0 skin, 1 face, 2 facial
    /// hair, 3 hair, 4 underwear; CharacterFacialHairStyles.Geoset[0..2] = geoset groups 1, 3, 2; CharHairGeosets.
    /// The region sizes are checked against the real CharSections BLP sizes by the harness probe ModelsCharTextures.
    /// </summary>
    public static class CharacterComposer
    {
        public const int TextureSize = 512;

        public enum Region { Base = 0, ArmUpper, ArmLower, Hand, FaceUpper, FaceLower, TorsoUpper, TorsoLower, LegUpper, LegLower, Foot }

        /// <summary>x, y, w, h of each region in the 512x512 body texture (row 0 = first stored row = v 0).</summary>
        public static readonly int[,] Regions =
        {
            { 0, 0, 512, 512 },      // base
            { 0, 0, 256, 128 },      // arm upper
            { 0, 128, 256, 128 },    // arm lower
            { 0, 256, 256, 64 },     // hand
            { 0, 320, 256, 64 },     // face upper
            { 0, 384, 256, 128 },    // face lower
            { 256, 0, 256, 128 },    // torso upper
            { 256, 128, 256, 64 },   // torso lower
            { 256, 192, 256, 128 },  // leg (pelvis) upper
            { 256, 320, 256, 128 },  // leg (pelvis) lower
            { 256, 448, 256, 64 },   // foot
        };

        public const int SectionSkin = 0, SectionFace = 1, SectionFacialHair = 2, SectionHair = 3, SectionUnderwear = 4;

        /// <summary>The composed result.</summary>
        public sealed class Result
        {
            public string ModelPath;
            public byte[] BodyRgba;              // TextureSize x TextureSize RGBA32 (null when no base skin was found)
            public string HairTexture;           // replaceable type 6
            public string FurTexture;            // replaceable type 8 (CharSections skin texture 2, e.g. tauren fur)
            public string CapeTexture;           // replaceable type 2 (Item\ObjectComponents\Cape\...) when a cape is worn
            public int HairGeoset;               // CharHairGeosets.GeosetID (0 = none)
            public bool ShowScalp;
            public int[] GroupValues = new int[19];   // geoset group -> variant (group 0 unused)
            public readonly List<string> Layers = new List<string>();
            public readonly List<string> Notes = new List<string>();

            /// <summary>WMV's rule: 0 shown; hair geosets 1..99 -> only HairGeoset (and 1 = scalp when ShowScalp);
            /// group g (g*100+1 .. g*100+99) -> only g*100 + GroupValues[g].</summary>
            public bool ShowGeoset(int id)
            {
                if (id == 0) return true;
                if (id < 100)
                {
                    if (id == 1 && ShowScalp) return true;
                    return id == HairGeoset;
                }
                int g = id / 100;
                if (g >= GroupValues.Length) return false;
                if (id % 100 == 0) return false;
                return id == g * 100 + GroupValues[g];
            }
        }

        /// <summary>First CharSections row matching race/sex/section/variation/color (NPC-flagged rows allowed when npc).</summary>
        public static int FindSection(DbcTable cs, int race, int sex, int section, int variation, int color)
        {
            if (cs == null) return -1;
            int found = -1;
            for (int r = 0; r < cs.RecordCount; r++)
            {
                if (cs.GetInt(r, DbcFields.CharSections.RaceID) != race || cs.GetInt(r, DbcFields.CharSections.SexID) != sex) continue;
                if (cs.GetInt(r, DbcFields.CharSections.BaseSection) != section) continue;
                if (cs.GetInt(r, DbcFields.CharSections.VariationIndex) != variation || cs.GetInt(r, DbcFields.CharSections.ColorIndex) != color) continue;
                found = r;
                break;
            }
            return found;
        }

        /// <summary>Composes a character (race, gender 0/1, skin, face, hair style/colour, facial hair).</summary>
        public static Result Compose(WowFiles files, int race, int gender, int skin, int face, int hairStyle, int hairColor, int facialHair)
        {
            return Compose(files, race, gender, skin, face, hairStyle, hairColor, facialHair, null, true);
        }

        /// <summary>
        /// Composes a character. itemDisplayIds (equipment slots 0..18, may be null) adds the armour's body-texture layers and
        /// geoset groups (WMV AddEquipment rules). composeTexture = false skips the blit (NPCs with a baked texture).
        /// </summary>
        public static Result Compose(WowFiles files, int race, int gender, int skin, int face, int hairStyle, int hairColor, int facialHair,
                                     int[] itemDisplayIds, bool composeTexture)
        {
            var res = new Result();
            res.ModelPath = WowModelDb.RaceModelPath(files, race, gender);
            for (int g = 1; g < res.GroupValues.Length; g++) res.GroupValues[g] = 1;
            res.GroupValues[1] = res.GroupValues[2] = res.GroupValues[3] = 0;
            res.GroupValues[7] = 2;   // ears shown (WMV default showEars)

            var cs = Dbc.Load(files, "CharSections");
            var layers = new List<KeyValuePair<int, KeyValuePair<Region, string>>>();   // layer -> (region, path)
            Action<int, Region, string> add = (layer, region, path) =>
            {
                if (!string.IsNullOrEmpty(path)) layers.Add(new KeyValuePair<int, KeyValuePair<Region, string>>(layer, new KeyValuePair<Region, string>(region, path)));
            };

            int row = FindSection(cs, race, gender, SectionSkin, 0, skin);
            if (row < 0) { res.Notes.Add("no skin section for skin " + skin + ", using 0"); row = FindSection(cs, race, gender, SectionSkin, 0, 0); }
            if (row >= 0)
            {
                add(0, Region.Base, cs.GetString(row, DbcFields.CharSections.TextureName));
                string fur = cs.GetString(row, DbcFields.CharSections.TextureName + 1);
                if (!string.IsNullOrEmpty(fur)) res.FurTexture = fur;
            }
            row = FindSection(cs, race, gender, SectionUnderwear, 0, skin);
            if (row < 0) row = FindSection(cs, race, gender, SectionUnderwear, 0, 0);
            if (row >= 0)
            {
                add(1, Region.LegUpper, cs.GetString(row, DbcFields.CharSections.TextureName));
                add(1, Region.TorsoUpper, cs.GetString(row, DbcFields.CharSections.TextureName + 1));
            }
            row = FindSection(cs, race, gender, SectionFace, face, skin);
            if (row < 0) { res.Notes.Add("no face " + face + " for skin " + skin); row = FindSection(cs, race, gender, SectionFace, 0, skin); }
            if (row >= 0)
            {
                add(1, Region.FaceLower, cs.GetString(row, DbcFields.CharSections.TextureName));
                add(1, Region.FaceUpper, cs.GetString(row, DbcFields.CharSections.TextureName + 1));
            }

            var fh = Dbc.Load(files, "CharacterFacialHairStyles");
            if (fh != null)
            {
                for (int r = 0; r < fh.RecordCount; r++)
                {
                    if (fh.GetInt(r, DbcFields.CharacterFacialHairStyles.RaceID) != race || fh.GetInt(r, DbcFields.CharacterFacialHairStyles.SexID) != gender
                        || fh.GetInt(r, DbcFields.CharacterFacialHairStyles.VariationID) != facialHair) continue;
                    res.GroupValues[1] = fh.GetInt(r, DbcFields.CharacterFacialHairStyles.Geoset);
                    res.GroupValues[3] = fh.GetInt(r, DbcFields.CharacterFacialHairStyles.Geoset + 1);
                    res.GroupValues[2] = fh.GetInt(r, DbcFields.CharacterFacialHairStyles.Geoset + 2);
                    break;
                }
            }
            row = FindSection(cs, race, gender, SectionFacialHair, facialHair, hairColor);
            if (row >= 0)
            {
                add(2, Region.FaceLower, cs.GetString(row, DbcFields.CharSections.TextureName));
                add(2, Region.FaceUpper, cs.GetString(row, DbcFields.CharSections.TextureName + 1));
            }

            var hg = Dbc.Load(files, "CharHairGeosets");
            if (hg != null)
            {
                for (int r = 0; r < hg.RecordCount; r++)
                {
                    if (hg.GetInt(r, DbcFields.CharHairGeosets.RaceID) != race || hg.GetInt(r, DbcFields.CharHairGeosets.SexID) != gender
                        || hg.GetInt(r, DbcFields.CharHairGeosets.VariationID) != hairStyle) continue;
                    res.HairGeoset = hg.GetInt(r, DbcFields.CharHairGeosets.GeosetID);
                    res.ShowScalp = hg.GetInt(r, DbcFields.CharHairGeosets.Showscalp) != 0;
                    break;
                }
            }
            row = FindSection(cs, race, gender, SectionHair, hairStyle, hairColor);
            if (row >= 0)
            {
                string hairTex = cs.GetString(row, DbcFields.CharSections.TextureName);
                if (string.IsNullOrEmpty(hairTex))
                {
                    // WMV: some styles (bald orcs with a beard) have no hair texture -> use style 0's.
                    int r0 = FindSection(cs, race, gender, SectionHair, 0, hairColor);
                    if (r0 >= 0) hairTex = cs.GetString(r0, DbcFields.CharSections.TextureName);
                }
                res.HairTexture = string.IsNullOrEmpty(hairTex) ? null : hairTex;
                if (!res.ShowScalp)
                {
                    add(3, Region.FaceLower, cs.GetString(row, DbcFields.CharSections.TextureName + 1));
                    add(3, Region.FaceUpper, cs.GetString(row, DbcFields.CharSections.TextureName + 2));
                }
            }
            else res.Notes.Add("no hair section style " + hairStyle + " colour " + hairColor);

            if (itemDisplayIds != null) Dress(files, gender, itemDisplayIds, res, add);
            if (!composeTexture) return res;

            // Stable sort by layer, then blit.
            var ordered = new List<KeyValuePair<int, KeyValuePair<Region, string>>>();
            for (int l = 0; l <= 40; l++) foreach (var kv in layers) if (kv.Key == l) ordered.Add(kv);
            if (ordered.Count == 0 || ordered[0].Value.Key != Region.Base) { res.Notes.Add("no base skin texture"); return res; }
            var dst = new byte[TextureSize * TextureSize * 4];
            foreach (var kv in ordered)
            {
                var region = kv.Value.Key;
                string path = kv.Value.Value;
                if (!BlitLayer(files, path, region, dst, res)) continue;
                res.Layers.Add(region + ": " + path);
            }
            res.BodyRgba = dst;
            return res;
        }

        /// <summary>Equipment slot ids (WoW EQUIPMENT_SLOT_*) of CharacterLook.ItemDisplayIds.</summary>
        public const int SlotHead = 0, SlotShoulders = 2, SlotShirt = 3, SlotChest = 4, SlotWaist = 5, SlotLegs = 6, SlotFeet = 7,
                         SlotWrists = 8, SlotHands = 9, SlotBack = 14, SlotMainHand = 15, SlotOffHand = 16, SlotRanged = 17, SlotTabard = 18;

        static readonly string[] TextureComponentDirs =
        {
            null, @"Item\TextureComponents\ArmUpperTexture\", @"Item\TextureComponents\ArmLowerTexture\", @"Item\TextureComponents\HandTexture\",
            null, null, @"Item\TextureComponents\TorsoUpperTexture\", @"Item\TextureComponents\TorsoLowerTexture\",
            @"Item\TextureComponents\LegUpperTexture\", @"Item\TextureComponents\LegLowerTexture\", @"Item\TextureComponents\FootTexture\"
        };

        /// <summary>
        /// ItemDisplayInfo body texture -> file (WMV makeItemTexture): &lt;RegionDir&gt;&lt;name&gt;_&lt;M|F&gt;.blp, or _U when the name's
        /// second-to-last letter is 'l'/'L' (leggings); then the other variant; null when neither exists.
        /// </summary>
        public static string ItemTexturePath(WowFiles files, Region region, string name, int gender)
        {
            string dir = TextureComponentDirs[(int)region];
            if (dir == null || string.IsNullOrEmpty(name) || name.Length < 3) return null;
            char l = name[name.Length - 2];
            bool leg = l == 'l' || l == 'L';
            string g = gender == 1 ? "F" : "M";
            string first = dir + name + "_" + (leg ? "U" : g) + ".blp", second = dir + name + "_" + (leg ? g : "U") + ".blp";
            if (files.Exists(first)) return first;
            if (files.Exists(second)) return second;
            return null;
        }

        /// <summary>WMV AddEquipment: armour body textures (layer 10+) and geoset groups of the worn items.</summary>
        static void Dress(WowFiles files, int gender, int[] items, Result res, Action<int, Region, string> add)
        {
            var idi = Dbc.Load(files, "ItemDisplayInfo");
            if (idi == null) return;
            int[] order = { SlotShirt, SlotHead, SlotShoulders, SlotLegs, SlotFeet, SlotChest, SlotTabard, SlotWaist, SlotWrists, SlotHands, SlotBack };
            Func<int, int> row = slot => slot < items.Length && items[slot] > 0 ? idi.FindRow(items[slot]) : -1;
            Func<int, int, string> tex = (r, i) => idi.GetString(r, DbcFields.ItemDisplayInfo.Texture + i);
            Func<int, int, int> geo = (r, i) => idi.GetInt(r, DbcFields.ItemDisplayInfo.GeosetGroup + i);
            int[] g = res.GroupValues;
            for (int k = 0; k < order.Length; k++)
            {
                int slot = order[k], r = row(slot), layer = 10 + k;
                if (r < 0) continue;
                if (slot == SlotLegs && g[13] == 2) continue;   // a robe hides the trousers
                Action<Region, int> layerOf = (region, ti) =>
                {
                    string p = ItemTexturePath(files, region, tex(r, ti), gender);
                    if (p != null) add(layer, region, p);
                };
                switch (slot)
                {
                    case SlotShirt:
                    case SlotChest:
                        g[8] = 1 + geo(r, 0);
                        layerOf(Region.ArmUpper, 0); layerOf(Region.ArmLower, 1); layerOf(Region.TorsoUpper, 3); layerOf(Region.TorsoLower, 4);
                        if (geo(r, 2) == 1) { layerOf(Region.LegUpper, 5); layerOf(Region.LegLower, 6); }
                        break;
                    case SlotWaist: layerOf(Region.TorsoLower, 4); layerOf(Region.LegUpper, 5); break;
                    case SlotWrists: layerOf(Region.ArmLower, 1); break;
                    case SlotLegs: g[9] = 1 + geo(r, 1); layerOf(Region.LegUpper, 5); layerOf(Region.LegLower, 6); break;
                    case SlotHands: g[4] = 1 + geo(r, 0); layerOf(Region.Hand, 2); layerOf(Region.ArmLower, 1); break;
                    case SlotFeet: g[5] = 1 + geo(r, 0); layerOf(Region.LegLower, 6); layerOf(Region.Foot, 7); break;
                    case SlotTabard: g[12] = 2; layerOf(Region.TorsoUpper, 3); layerOf(Region.TorsoLower, 4); break;
                    case SlotBack:
                        g[15] = 1 + geo(r, 0);
                        string cape = idi.GetString(r, DbcFields.ItemDisplayInfo.ModelTexture);
                        if (!string.IsNullOrEmpty(cape)) res.CapeTexture = @"Item\ObjectComponents\Cape\" + cape + ".blp";
                        break;
                }
                if (g[13] == 1) g[13] = 1 + geo(r, 2);   // robe
                if (g[13] == 2) { g[5] = 0; g[12] = 0; }  // robe hides boots and tabard
                if (g[4] > 1) g[8] = 0;                   // gloves over sleeves
            }
        }

        /// <summary>
        /// Eye glow (WMV): of the group-17 geosets, show the one whose texture is an "eyeglow" but not the death knight's
        /// (night elves' glowing eyes). Sets GroupValues[17]; leaves it (no glow) when the model has none.
        /// </summary>
        public static void ApplyEyeGlow(Result res, M2Model m, M2Skin s)
        {
            foreach (var b in s.Batches)
            {
                if (b.SubmeshIndex < 0 || b.SubmeshIndex >= s.Submeshes.Length) continue;
                int id = s.Submeshes[b.SubmeshIndex].GeosetId;
                if (id / 100 != 17) continue;
                var info = M2MeshData.Describe(m, b, 0);
                string f = (info.TextureFile ?? "").ToLowerInvariant();
                if (f.Contains("eyeglow") && !f.Contains("deathknight")) { res.GroupValues[17] = id % 100; return; }
            }
        }

        /// <summary>Alpha-blends one BLP onto its region (scaled with bilinear filtering if its size differs). False when unreadable.</summary>
        public static bool BlitLayer(WowFiles files, string path, Region region, byte[] dst, Result res)
        {
            byte[] bytes = files.Read(path);
            if (bytes == null) { if (res != null) res.Notes.Add("missing " + path); return false; }
            BlpImage img;
            try { img = BlpImage.Decode(bytes); }
            catch (Exception e) { if (res != null) res.Notes.Add("undecodable " + path + ": " + e.Message); return false; }
            var px = img.GetRgba32(0);
            int ri = (int)region;
            int rx = Regions[ri, 0], ry = Regions[ri, 1], rw = Regions[ri, 2], rh = Regions[ri, 3];
            if ((img.Width != rw || img.Height != rh) && res != null) res.Notes.Add(path + " " + img.Width + "x" + img.Height + " scaled to region " + region + " " + rw + "x" + rh);
            bool isBase = region == Region.Base;
            BlitRgba(px, img.Width, img.Height, dst, TextureSize, rx, ry, rw, rh, !isBase);
            return true;
        }

        /// <summary>Draws src (sw x sh RGBA) into dst's rect (rx, ry, rw, rh); blend = alpha over, else copy. Result alpha 255.</summary>
        public static void BlitRgba(byte[] src, int sw, int sh, byte[] dst, int dstW, int rx, int ry, int rw, int rh, bool blend)
        {
            bool same = sw == rw && sh == rh;
            for (int y = 0; y < rh; y++)
            {
                for (int x = 0; x < rw; x++)
                {
                    float r, g, b, a;
                    if (same)
                    {
                        int si = (y * sw + x) * 4;
                        r = src[si]; g = src[si + 1]; b = src[si + 2]; a = src[si + 3];
                    }
                    else
                    {
                        float fx = (x + 0.5f) * sw / rw - 0.5f, fy = (y + 0.5f) * sh / rh - 0.5f;
                        int x0 = Math.Max(0, Math.Min(sw - 1, (int)Math.Floor(fx))), y0 = Math.Max(0, Math.Min(sh - 1, (int)Math.Floor(fy)));
                        int x1 = Math.Min(sw - 1, x0 + 1), y1 = Math.Min(sh - 1, y0 + 1);
                        float tx = Math.Max(0f, Math.Min(1f, fx - x0)), ty = Math.Max(0f, Math.Min(1f, fy - y0));
                        r = Bi(src, sw, x0, y0, x1, y1, tx, ty, 0); g = Bi(src, sw, x0, y0, x1, y1, tx, ty, 1);
                        b = Bi(src, sw, x0, y0, x1, y1, tx, ty, 2); a = Bi(src, sw, x0, y0, x1, y1, tx, ty, 3);
                    }
                    int di = ((ry + y) * dstW + rx + x) * 4;
                    if (blend)
                    {
                        float f = a / 255f, inv = 1f - f;
                        dst[di] = (byte)(dst[di] * inv + r * f + 0.5f);
                        dst[di + 1] = (byte)(dst[di + 1] * inv + g * f + 0.5f);
                        dst[di + 2] = (byte)(dst[di + 2] * inv + b * f + 0.5f);
                    }
                    else
                    {
                        dst[di] = (byte)(r + 0.5f); dst[di + 1] = (byte)(g + 0.5f); dst[di + 2] = (byte)(b + 0.5f);
                    }
                    dst[di + 3] = 255;
                }
            }
        }

        static float Bi(byte[] s, int w, int x0, int y0, int x1, int y1, float tx, float ty, int c)
        {
            float a = s[(y0 * w + x0) * 4 + c], b = s[(y0 * w + x1) * 4 + c], d = s[(y1 * w + x0) * 4 + c], e = s[(y1 * w + x1) * 4 + c];
            float top = a + (b - a) * tx, bot = d + (e - d) * tx;
            return top + (bot - top) * ty;
        }
    }
}
