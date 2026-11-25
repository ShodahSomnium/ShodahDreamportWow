using System.Diagnostics;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>Creatures and NPCs by CreatureDisplayInfo id (including humanoid NPCs through CreatureDisplayInfoExtra).</summary>
    public static class WowCreatures
    {
        /// <summary>CreatureDisplayInfoExtra.NPCItemDisplay[0..10] -> equipment slot (WMV NPCDB: helm, shoulder, shirt, chest,
        /// belt, pants, boots, bracers, gloves, tabard, cape).</summary>
        static readonly int[] NpcItemSlots = { 0, 2, 3, 4, 5, 6, 7, 8, 9, 18, 14 };

        /// <summary>
        /// CreatureDisplayInfo -> CreatureModelData model + TextureVariation skins (replaceable types 11-13, in the model's
        /// folder). Humanoid NPCs (ExtendedDisplayInfoID): race/gender model with the baked NPC texture
        /// (Textures\BakedNpcTextures\...) as body, CharSections hair, geosets from hair/facial hair/NPC items.
        /// The root's localScale carries CreatureDisplayInfo x CreatureModelData scale (multiply, do not overwrite).
        /// Null when the display or its model is missing.
        /// </summary>
        public static GameObject Build(WowFiles files, int creatureDisplayId)
        {
            var sw = Stopwatch.StartNew();
            var cd = WowModelDb.ResolveCreature(files, creatureDisplayId);
            if (cd.Error != null) { WowLog.Warn("WowCreatures: " + cd.Error); return null; }
            string name = "Creature " + creatureDisplayId;
            GameObject go;
            string detail;
            if (cd.ExtraId > 0)
            {
                var items = new int[19];
                for (int i = 0; i < cd.NpcItems.Length && i < NpcItemSlots.Length; i++) items[NpcItemSlots[i]] = cd.NpcItems[i];
                bool baked = !string.IsNullOrEmpty(cd.BakedTexture) && files.Exists(cd.BakedTexture);
                var res = CharacterComposer.Compose(files, cd.Race, cd.Gender, cd.Skin, cd.Face, cd.HairStyle, cd.HairColor, cd.FacialHair, items, !baked);
                res.ModelPath = cd.ModelPath;
                M2Model m; M2Skin s; string err;
                if (!M2Factory.TryGetParsed(files, cd.ModelPath, out m, out s, out err)) { WowLog.Warn("WowCreatures: " + cd.ModelPath + " -> " + err); return null; }
                CharacterComposer.ApplyEyeGlow(res, m, s);
                var gear = NpcGear.Resolve(files, cd);   // NPC gear fix (iteration 11): helm + shoulder models, the helm's geoset hiding
                NpcGear.Apply(gear, res);
                Texture body = null;
                if (baked)
                {
                    int flags = 0;
                    foreach (var t in m.Textures) if (t.Type == 1) { flags = (int)t.Flags; break; }
                    body = WowTextures.Get(files, cd.BakedTexture, WowTextureOptions.ForM2Flags(flags));
                }
                go = WowCharacter.BuildBody(files, res, body, null, name, m);
                string gearNote = go != null ? WowNpcGear.Attach(files, go, gear) : null;
                if (gear != null && gear.Summary == null) gear.Summary = NpcGear.Describe(gear, res, baked) + gearNote;
                detail = "humanoid race " + cd.Race + " g" + cd.Gender + (baked ? " baked " + cd.BakedTexture : " composed (no baked texture)");
            }
            else
            {
                var o = new M2BuildOptions { Name = name };
                for (int i = 0; i < 3; i++)
                    if (!string.IsNullOrEmpty(cd.Skins[i])) o.ReplaceablePaths[11 + i] = cd.Skins[i];
                go = M2Factory.Build(files, cd.ModelPath, o);
                detail = "skins " + string.Join(", ", System.Array.FindAll(cd.Skins, x => x != null));
            }
            if (go == null) return null;
            go.transform.localScale = Vector3.one * cd.Scale;
            WowLog.Info("WowCreatures: display " + creatureDisplayId + " -> " + cd.ModelPath + " scale " + cd.Scale.ToString("0.##") + ", " + detail + ", " + sw.ElapsedMilliseconds + " ms");
            return go;
        }
    }
}
