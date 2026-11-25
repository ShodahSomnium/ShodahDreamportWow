using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>A creature display resolved from the DBCs (pure).</summary>
    public sealed class CreatureDisplay
    {
        public int DisplayId, ModelId, ExtraId;
        public string ModelPath;           // .m2 path
        public float Scale = 1f;           // CreatureDisplayInfo scale x CreatureModelData scale
        public string[] Skins = new string[3];   // full .blp paths for replaceable types 11, 12, 13 (null = none)
        public int Race, Gender, Skin, Face, HairStyle, HairColor, FacialHair;   // humanoid NPCs (ExtraId != 0)
        public string BakedTexture;        // Textures\BakedNpcTextures\<BakeName> (humanoid NPCs)
        public int[] NpcItems = new int[0];      // CreatureDisplayInfoExtra.NPCItemDisplay[11]
        public string Error;
    }

    /// <summary>An item display resolved from ItemDisplayInfo.dbc (pure).</summary>
    public sealed class ItemDisplay
    {
        public int DisplayId;
        public string ModelPath;           // Item\ObjectComponents\<Folder>\<name>.m2
        public string TexturePath;         // same folder, <ModelTexture>.blp (the model's replaceable type 2 texture)
        public string Folder;
        public string Error;
    }

    /// <summary>DBC lookups for models (pure; the DBC tables are cached by Dbc.Load).</summary>
    public static class WowModelDb
    {
        /// <summary>Races with player models in 3.3.5a (ChrRaces ids).</summary>
        public static readonly int[] PlayerRaces = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 };

        static string DirOf(string path)
        {
            int i = path.LastIndexOf('\\');
            return i >= 0 ? path.Substring(0, i + 1) : "";
        }

        /// <summary>CreatureDisplayInfo -> CreatureModelData model (.mdx -> .m2) + TextureVariation skins in the model's folder.</summary>
        public static CreatureDisplay ResolveCreature(WowFiles files, int displayId)
        {
            var r = new CreatureDisplay { DisplayId = displayId };
            var cdi = Dbc.Load(files, "CreatureDisplayInfo");
            var cmd = Dbc.Load(files, "CreatureModelData");
            if (cdi == null || cmd == null) { r.Error = "CreatureDisplayInfo/CreatureModelData.dbc missing"; return r; }
            int row = cdi.FindRow(displayId);
            if (row < 0) { r.Error = "display id " + displayId + " not in CreatureDisplayInfo.dbc"; return r; }
            r.ModelId = cdi.GetInt(row, DbcFields.CreatureDisplayInfo.ModelID);
            r.ExtraId = cdi.GetInt(row, DbcFields.CreatureDisplayInfo.ExtendedDisplayInfoID);
            float scale = cdi.GetFloat(row, DbcFields.CreatureDisplayInfo.CreatureModelScale);
            int mrow = cmd.FindRow(r.ModelId);
            if (mrow < 0) { r.Error = "model id " + r.ModelId + " not in CreatureModelData.dbc"; return r; }
            r.ModelPath = M2Model.NormalizeModelPath(cmd.GetString(mrow, DbcFields.CreatureModelData.ModelName));
            float mscale = cmd.GetFloat(mrow, DbcFields.CreatureModelData.ModelScale);
            r.Scale = (scale > 0f ? scale : 1f) * (mscale > 0f ? mscale : 1f);
            string dir = DirOf(r.ModelPath);
            for (int i = 0; i < 3; i++)
            {
                string s = cdi.GetString(row, DbcFields.CreatureDisplayInfo.TextureVariation + i);
                if (!string.IsNullOrEmpty(s)) r.Skins[i] = dir + s + ".blp";
            }
            if (r.ExtraId > 0)
            {
                var ex = Dbc.Load(files, "CreatureDisplayInfoExtra");
                int er = ex == null ? -1 : ex.FindRow(r.ExtraId);
                if (er >= 0)
                {
                    r.Race = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.DisplayRaceID);
                    r.Gender = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.DisplaySexID);
                    r.Skin = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.SkinID);
                    r.Face = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.FaceID);
                    r.HairStyle = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.HairStyleID);
                    r.HairColor = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.HairColorID);
                    r.FacialHair = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.FacialHairID);
                    r.NpcItems = new int[11];
                    for (int i = 0; i < 11; i++) r.NpcItems[i] = ex.GetInt(er, DbcFields.CreatureDisplayInfoExtra.NPCItemDisplay + i);
                    string bake = ex.GetString(er, DbcFields.CreatureDisplayInfoExtra.BakeName);
                    if (!string.IsNullOrEmpty(bake)) r.BakedTexture = @"Textures\BakedNpcTextures\" + bake;
                }
                else r.ExtraId = 0;
            }
            return r;
        }

        /// <summary>
        /// The player model of a race/gender the way the client finds it: ChrRaces.MaleDisplayID/FemaleDisplayID ->
        /// CreatureDisplayInfo -> CreatureModelData.ModelName. Falls back to Character\&lt;ClientFileString&gt;\&lt;Gender&gt;\...
        /// </summary>
        public static string RaceModelPath(WowFiles files, int race, int gender)
        {
            var races = Dbc.Load(files, "ChrRaces");
            int row = races == null ? -1 : races.FindRow(race);
            if (row < 0) return null;
            int display = races.GetInt(row, gender == 1 ? DbcFields.ChrRaces.FemaleDisplayID : DbcFields.ChrRaces.MaleDisplayID);
            var cd = ResolveCreature(files, display);
            if (cd.Error == null && !string.IsNullOrEmpty(cd.ModelPath)) return cd.ModelPath;
            string rn = races.GetString(row, DbcFields.ChrRaces.ClientFileString);
            string g = gender == 1 ? "Female" : "Male";
            return @"Character\" + rn + @"\" + g + @"\" + rn + g + ".m2";
        }

        /// <summary>ChrRaces.ClientFileString (e.g. "Human", "Scourge", "NightElf"), or null.</summary>
        public static string RaceFileName(WowFiles files, int race)
        {
            var races = Dbc.Load(files, "ChrRaces");
            int row = races == null ? -1 : races.FindRow(race);
            return row < 0 ? null : races.GetString(row, DbcFields.ChrRaces.ClientFileString);
        }

        /// <summary>Folders searched (in this order) for an ItemDisplayInfo model name that has no folder.</summary>
        public static readonly string[] ItemFolders = { "Weapon", "Shield", "Quiver", "Head", "Shoulder", "Ammo", "Cape" };

        /// <summary>
        /// ItemDisplayInfo model + texture. leftHand picks ModelName[1]/ModelTexture[1] when it is set (else [0]). The folder
        /// is found by probing Item\ObjectComponents\{Weapon, Shield, ...} for the .m2 (the DBC stores only the file name).
        /// </summary>
        public static ItemDisplay ResolveItem(WowFiles files, int itemDisplayId, bool leftHand)
        {
            var r = new ItemDisplay { DisplayId = itemDisplayId };
            var t = Dbc.Load(files, "ItemDisplayInfo");
            if (t == null) { r.Error = "ItemDisplayInfo.dbc missing"; return r; }
            int row = t.FindRow(itemDisplayId);
            if (row < 0) { r.Error = "item display " + itemDisplayId + " not in ItemDisplayInfo.dbc"; return r; }
            int slot = 0;
            if (leftHand && !string.IsNullOrEmpty(t.GetString(row, DbcFields.ItemDisplayInfo.ModelName + 1))) slot = 1;
            string model = t.GetString(row, DbcFields.ItemDisplayInfo.ModelName + slot);
            string tex = t.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture + slot);
            if (string.IsNullOrEmpty(tex) && slot == 1) tex = t.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture);
            if (string.IsNullOrEmpty(model)) { r.Error = "item display " + itemDisplayId + " has no model"; return r; }
            string m2 = M2Model.NormalizeModelPath(model);
            foreach (var folder in ItemFolders)
            {
                string p = @"Item\ObjectComponents\" + folder + @"\" + m2;
                if (files.Exists(p))
                {
                    r.Folder = folder;
                    r.ModelPath = p;
                    if (!string.IsNullOrEmpty(tex)) r.TexturePath = @"Item\ObjectComponents\" + folder + @"\" + tex + ".blp";
                    return r;
                }
            }
            r.Error = "item model " + m2 + " not found under Item\\ObjectComponents\\{" + string.Join(",", ItemFolders) + "}";
            return r;
        }

        /// <summary>AnimationData.dbc name of an animation id, or null.</summary>
        public static string AnimationName(WowFiles files, int animId)
        {
            var t = Dbc.Load(files, "AnimationData");
            int row = t == null ? -1 : t.FindRow(animId);
            return row < 0 ? null : t.GetString(row, DbcFields.AnimationData.Name);
        }

        /// <summary>AnimationData fallback chain of an animation id (e.g. Run -> Walk -> Stand) used when a model lacks it.</summary>
        public static int AnimationFallback(WowFiles files, int animId)
        {
            var t = Dbc.Load(files, "AnimationData");
            int row = t == null ? -1 : t.FindRow(animId);
            return row < 0 ? -1 : t.GetInt(row, DbcFields.AnimationData.Fallback);
        }
    }
}
