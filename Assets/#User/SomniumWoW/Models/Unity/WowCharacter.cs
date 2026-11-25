using System.Diagnostics;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>A player character's look, as the character list and the object fields describe it.</summary>
    public struct CharacterLook
    {
        public int Race;       // ChrRaces id (1 Human, 2 Orc, 3 Dwarf, 4 Night Elf, 5 Undead, 6 Tauren, 7 Gnome, 8 Troll, 10 Blood Elf, 11 Draenei)
        public int Gender;     // 0 male, 1 female
        public int Skin;
        public int Face;
        public int HairStyle;
        public int HairColor;
        public int FacialHair;
        /// <summary>ItemDisplayInfo ids by equipment slot 0..18 (0 head ... 15 main hand, 16 off hand, 17 ranged, 18 tabard); 0 = empty. May be null.</summary>
        public int[] ItemDisplayIds;
    }

    /// <summary>Player characters: race/gender body, composed skin/face/hair/underwear textures, geosets, equipment.</summary>
    public static class WowCharacter
    {
        /// <summary>
        /// Builds a character (animated, playing Stand). Body texture composed from CharSections (+ armour body textures of
        /// ItemDisplayIds), hair texture, geosets from CharHairGeosets / CharacterFacialHairStyles / armour; main hand weapon
        /// on attachment 1, off-hand weapon on 2, shield on 0 (hands closed round them). Null when the race model is missing.
        /// Helmet and shoulder models are not built (iteration 1).
        /// </summary>
        public static GameObject Build(WowFiles files, CharacterLook look)
        {
            var sw = Stopwatch.StartNew();
            var res = CharacterComposer.Compose(files, look.Race, look.Gender, look.Skin, look.Face, look.HairStyle, look.HairColor, look.FacialHair,
                                                look.ItemDisplayIds, true);
            if (string.IsNullOrEmpty(res.ModelPath)) { WowLog.Warn("WowCharacter: race " + look.Race + " has no model in ChrRaces.dbc"); return null; }
            M2Model m; M2Skin s; string err;
            if (!M2Factory.TryGetParsed(files, res.ModelPath, out m, out s, out err))
            {
                WowLog.Warn("WowCharacter: " + res.ModelPath + " -> " + err);
                return null;
            }
            CharacterComposer.ApplyEyeGlow(res, m, s);
            string name = "Character " + WowModelDb.RaceFileName(files, look.Race) + (look.Gender == 1 ? "Female" : "Male");
            var go = BuildBody(files, res, null, look.ItemDisplayIds, name, m);
            if (go == null) return null;
            WowLog.Info("WowCharacter: " + name + " skin " + look.Skin + " face " + look.Face + " hair " + look.HairStyle + "/" + look.HairColor
                + " facial " + look.FacialHair + ": " + res.Layers.Count + " texture layers, hair geoset " + res.HairGeoset + (res.ShowScalp ? " (scalp)" : "")
                + ", groups 1-3 " + res.GroupValues[1] + "/" + res.GroupValues[2] + "/" + res.GroupValues[3]
                + (res.Notes.Count > 0 ? ", notes: " + string.Join("; ", res.Notes.GetRange(0, System.Math.Min(4, res.Notes.Count)).ToArray()) : "")
                + ", " + sw.ElapsedMilliseconds + " ms");
            return go;
        }

        /// <summary>Shared by characters and humanoid NPCs: body/hair/fur/cape textures, geosets, held items.</summary>
        internal static GameObject BuildBody(WowFiles files, CharacterComposer.Result res, Texture bakedBody, int[] items, string name, M2Model m)
        {
            int bodyFlags = 0;
            foreach (var t in m.Textures) if (t.Type == 1) { bodyFlags = (int)t.Flags; break; }
            Texture2D composed = null;
            Texture body = bakedBody;
            if (body == null && res.BodyRgba != null)
            {
                composed = WowTextures.FromRgba32(CharacterComposer.TextureSize, CharacterComposer.TextureSize, res.BodyRgba,
                                                  WowTextureOptions.ForM2Flags(bodyFlags), name + " body", true);
                body = composed;
            }
            var o = new M2BuildOptions { ShowGeoset = res.ShowGeoset, ShareMaterials = composed == null, Name = name };
            if (body != null) o.ReplaceableTextures[1] = body;
            if (!string.IsNullOrEmpty(res.HairTexture)) o.ReplaceablePaths[6] = res.HairTexture;
            if (!string.IsNullOrEmpty(res.FurTexture)) o.ReplaceablePaths[8] = res.FurTexture;
            if (!string.IsNullOrEmpty(res.CapeTexture)) o.ReplaceablePaths[2] = res.CapeTexture;
            var go = M2Factory.Build(files, res.ModelPath, o);
            if (go == null) { if (composed != null) Object.Destroy(composed); return null; }
            if (composed != null) go.GetComponent<M2OwnedAssets>().Owned.Add(composed);
            if (items != null) Equip(files, go, items);
            return go;
        }

        /// <summary>Main hand (slot 15) on attachment 1; off hand (16) on 2, or on 0 when it is a shield.</summary>
        internal static void Equip(WowFiles files, GameObject character, int[] items)
        {
            var anim = character.GetComponent<M2Animator>();
            if (anim == null) return;
            if (items.Length > CharacterComposer.SlotMainHand && items[CharacterComposer.SlotMainHand] > 0)
                Attach(files, anim, items[CharacterComposer.SlotMainHand], false);
            if (items.Length > CharacterComposer.SlotOffHand && items[CharacterComposer.SlotOffHand] > 0)
                Attach(files, anim, items[CharacterComposer.SlotOffHand], true);
        }

        static void Attach(WowFiles files, M2Animator anim, int itemDisplayId, bool offHand)
        {
            var info = WowModelDb.ResolveItem(files, itemDisplayId, offHand);
            if (info.Error != null) { WowLog.Warn("WowCharacter: item " + itemDisplayId + " -> " + info.Error); return; }
            bool shield = info.Folder == "Shield";
            int attachment = !offHand ? 1 : shield ? 0 : 2;
            var point = anim.Attachment(attachment);
            if (point == null) { WowLog.Warn("WowCharacter: " + anim.Model.Path + " has no attachment " + attachment + " for item " + itemDisplayId); return; }
            var item = WowItems.Build(files, itemDisplayId, offHand);
            if (item == null) return;
            item.transform.SetParent(point, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            if (!shield) anim.CloseHand(!offHand, true);
        }
    }
}
