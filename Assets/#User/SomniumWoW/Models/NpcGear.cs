using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>The helm and shoulder models a humanoid NPC wears, and what its helm hides (pure, cached per display; see NpcGear).</summary>
    public sealed class NpcGearPlan
    {
        public int DisplayId, Race, Gender;
        public int HelmItem, ShoulderItem, CapeItem;   // CreatureDisplayInfoExtra.NPCItemDisplay[0], [1], [10] (ItemDisplayInfo ids, 0 = none)
        public string HelmModel, HelmTexture;          // null = no helm drawn
        public string LeftShoulderModel, LeftShoulderTexture, RightShoulderModel, RightShoulderTexture;
        public string CapeTexture;                     // the cape the body shows (CharacterComposer sets the same), for the art worker + log
        public int HelmVisId;                          // HelmetGeosetVisData row used (ItemDisplayInfo.HelmetGeosetVisID[sex]), 0 = none
        public bool HideHair, HideEars;
        public readonly bool[] HideFacial = new bool[4];   // [1..3] = geoset groups 1..3
        public string Notes = "";                      // missing files, for the log
        /// <summary>One line for the log, filled by the first build (main thread).</summary>
        public string Summary;

        public bool Any { get { return HelmModel != null || LeftShoulderModel != null || RightShoulderModel != null; } }
    }

    /// <summary>
    /// Humanoid NPC gear drawn as models (CreatureDisplayInfoExtra.NPCItemDisplay: [0] helm, [1] shoulders; the body texture is the
    /// baked one and the other slots are geosets / the cape, CharacterComposer). Pure; any thread; one resolve per display (cached).
    /// Sources: WoW Model Viewer 0.7 (the 3.3.5-era tool) CharControl::RefreshItem - helm on attachment ATT_HELMET (11) from
    /// Item\ObjectComponents\Head\&lt;ModelName[0] without .mdx&gt;_&lt;ChrRaces.ClientPrefix&gt;&lt;M|F&gt;.m2 with ModelTexture[0], shoulders
    /// ModelName[0] on ATT_LEFT_SHOULDER (6) and ModelName[1] on ATT_RIGHT_SHOULDER (5) (enums.h) with ModelTexture[0] / [1], each
    /// item texture the model's replaceable type 2 (MEASURED harness NpcGear: the helm / shoulder M2s have one texture, type 2).
    /// HelmetGeosetVisData.HideGeoset[7] values are ChrRaces bit masks (bit = 1 &lt;&lt; race id), MEASURED harness NpcHelmVis: column 0 is
    /// -65 = every race but Tauren (6, whose "hair" styles are horns), column 4 is -273 / -1297 = every race but Night Elf (4) / Troll (8)
    /// / Blood Elf (10) - the long-eared ones - so 0 = hair geosets (1..99), 1..3 = facial geoset groups 1..3, 4 = ears (group 7);
    /// columns 5 and 6 (set in 4 of 21 rows, meaning unknown) are not applied. ItemDisplayInfo.HelmetGeosetVisID[2]: [0] male,
    /// [1] female (wowdev ItemDisplayInfo; not measured).
    /// </summary>
    public static class NpcGear
    {
        public const int AttachRightShoulder = 5, AttachLeftShoulder = 6, AttachHelm = 11;   // WMV 0.7 enums.h ATT_*

        static readonly Dictionary<int, NpcGearPlan> Plans = new Dictionary<int, NpcGearPlan>();
        static readonly object Lock = new object();

        /// <summary>The cached plan of a humanoid display (null when the display has no CreatureDisplayInfoExtra row).</summary>
        public static NpcGearPlan Resolve(WowFiles files, CreatureDisplay cd)
        {
            if (cd == null || cd.ExtraId <= 0 || cd.NpcItems == null || cd.NpcItems.Length < 11) return null;
            NpcGearPlan p;
            lock (Lock) if (Plans.TryGetValue(cd.DisplayId, out p)) return p;
            p = Make(files, cd);
            lock (Lock)
            {
                NpcGearPlan had;
                if (Plans.TryGetValue(cd.DisplayId, out had)) return had;
                Plans[cd.DisplayId] = p;
            }
            return p;
        }

        /// <summary>The cached plan of a display already resolved (null when none / not resolved yet).</summary>
        public static NpcGearPlan Cached(int displayId)
        {
            NpcGearPlan p;
            lock (Lock) return Plans.TryGetValue(displayId, out p) ? p : null;
        }

        static NpcGearPlan Make(WowFiles files, CreatureDisplay cd)
        {
            var p = new NpcGearPlan { DisplayId = cd.DisplayId, Race = cd.Race, Gender = cd.Gender, HelmItem = cd.NpcItems[0], ShoulderItem = cd.NpcItems[1], CapeItem = cd.NpcItems[10] };
            var idi = Dbc.Load(files, "ItemDisplayInfo");
            if (idi == null) { p.Notes = "ItemDisplayInfo.dbc missing"; return p; }

            int r = p.HelmItem > 0 ? idi.FindRow(p.HelmItem) : -1;
            if (r >= 0)
            {
                string model = idi.GetString(r, DbcFields.ItemDisplayInfo.ModelName);
                string prefix = RacePrefix(files, cd.Race);
                if (!string.IsNullOrEmpty(model) && prefix != null)
                {
                    int dot = model.LastIndexOf('.');
                    string path = @"Item\ObjectComponents\Head\" + (dot > 0 ? model.Substring(0, dot) : model) + "_" + prefix + (cd.Gender == 1 ? "F" : "M") + ".m2";
                    if (files.Exists(path))
                    {
                        p.HelmModel = path;
                        p.HelmTexture = TexturePath(files, idi, r, 0, @"Item\ObjectComponents\Head\");
                        HelmVis(files, idi, r, cd.Gender, cd.Race, p);
                    }
                    else p.Notes += " helm " + path + " not in the game files;";
                }
                else if (string.IsNullOrEmpty(model)) p.Notes += " helm " + p.HelmItem + " has no model;";
            }

            r = p.ShoulderItem > 0 ? idi.FindRow(p.ShoulderItem) : -1;
            if (r >= 0)
            {
                p.LeftShoulderModel = ShoulderPath(files, idi, r, 0, p);
                p.RightShoulderModel = ShoulderPath(files, idi, r, 1, p);
                if (p.LeftShoulderModel != null) p.LeftShoulderTexture = TexturePath(files, idi, r, 0, @"Item\ObjectComponents\Shoulder\");
                if (p.RightShoulderModel != null) p.RightShoulderTexture = TexturePath(files, idi, r, 1, @"Item\ObjectComponents\Shoulder\") ?? TexturePath(files, idi, r, 0, @"Item\ObjectComponents\Shoulder\");
            }

            r = p.CapeItem > 0 ? idi.FindRow(p.CapeItem) : -1;
            if (r >= 0)
            {
                string cape = idi.GetString(r, DbcFields.ItemDisplayInfo.ModelTexture);
                if (!string.IsNullOrEmpty(cape)) p.CapeTexture = @"Item\ObjectComponents\Cape\" + cape + ".blp";   // as CharacterComposer.Dress
            }
            return p;
        }

        /// <summary>Hides the geosets the drawn helm asks for (HelmetGeosetVisData): hair -> no hair geoset (1..99, as WMV), facial
        /// group g -> variant g*100+1 (the "none" variant facial style 0 uses, MEASURED: HumanMale facial 0 = 101/201/301), ears -> 701.
        /// Nothing is hidden when no helm model is drawn (ours: a model-less helm display would otherwise leave a bald NPC).</summary>
        public static void Apply(NpcGearPlan p, CharacterComposer.Result res)
        {
            if (p == null || res == null || p.HelmModel == null) return;
            if (p.HideHair) { res.HairGeoset = 0; res.ShowScalp = false; }
            for (int g = 1; g <= 3; g++) if (p.HideFacial[g]) res.GroupValues[g] = 1;
            if (p.HideEars) res.GroupValues[7] = 1;
        }

        /// <summary>True when the model draws the same every frame: no bone with keys or billboarding, no emitters, no texture
        /// transforms, no colour / transparency track with more than one key - so its rest mesh can be shared as a static mesh.</summary>
        public static bool IsRigid(M2Model m)
        {
            if (m == null || m.HasEmitters || m.TextureTransforms.Length > 0) return false;
            foreach (var b in m.Bones)
                if (b.IsBillboard || HasKeys(b.Translation, 0) || HasKeys(b.Rotation, 0) || HasKeys(b.Scale, 0)) return false;
            foreach (var c in m.Colors) if (HasKeys(c.Color, 1) || HasKeys(c.Alpha, 1)) return false;
            foreach (var t in m.Transparencies) if (HasKeys(t, 1)) return false;
            return true;
        }

        static bool HasKeys<T>(M2Track<T> t, int more)
        {
            if (t == null) return false;
            foreach (var a in t.Times) if (a != null && a.Length > more) return true;
            return false;
        }

        static void HelmVis(WowFiles files, DbcTable idi, int row, int gender, int race, NpcGearPlan p)
        {
            p.HelmVisId = idi.GetInt(row, DbcFields.ItemDisplayInfo.HelmetGeosetVisID + (gender == 1 ? 1 : 0));
            if (p.HelmVisId <= 0 || race <= 0 || race > 30) return;
            var hgv = Dbc.Load(files, "HelmetGeosetVisData");
            int hr = hgv != null ? hgv.FindRow(p.HelmVisId) : -1;
            if (hr < 0) { p.Notes += " HelmetGeosetVisData " + p.HelmVisId + " missing;"; return; }
            int bit = 1 << race;
            Func<int, bool> hides = c => (hgv.GetInt(hr, DbcFields.HelmetGeosetVisData.HideGeoset + c) & bit) != 0;
            p.HideHair = hides(0);
            for (int g = 1; g <= 3; g++) p.HideFacial[g] = hides(g);
            p.HideEars = hides(4);
        }

        static string ShoulderPath(WowFiles files, DbcTable idi, int row, int side, NpcGearPlan p)
        {
            string model = idi.GetString(row, DbcFields.ItemDisplayInfo.ModelName + side);
            // An empty right name: the left file's RShoulder_ twin (ArmourPieces' rule, measured there - AvArmourFiles)
            if (side == 1 && string.IsNullOrEmpty(model))
            {
                string left = idi.GetString(row, DbcFields.ItemDisplayInfo.ModelName);
                if (!string.IsNullOrEmpty(left) && left.StartsWith("LShoulder_", StringComparison.OrdinalIgnoreCase)) model = "RShoulder_" + left.Substring(10);
            }
            if (string.IsNullOrEmpty(model)) return null;
            string path = @"Item\ObjectComponents\Shoulder\" + M2Model.NormalizeModelPath(model);
            if (files.Exists(path)) return path;
            p.Notes += " shoulder " + path + " not in the game files;";
            return null;
        }

        static string TexturePath(WowFiles files, DbcTable idi, int row, int side, string folder)
        {
            string tex = idi.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture + side);
            return string.IsNullOrEmpty(tex) ? null : folder + tex + ".blp";
        }

        /// <summary>ChrRaces.ClientPrefix (Hu, Or, Dw, Ni, Sc, Ta, Gn, Tr, Gb, Be, Dr, ...), null when unknown.</summary>
        static string RacePrefix(WowFiles files, int race)
        {
            var t = race > 0 ? Dbc.Load(files, "ChrRaces") : null;
            int r = t != null ? t.FindRow(race) : -1;
            if (r < 0) return null;
            string s = t.GetString(r, DbcFields.ChrRaces.ClientPrefix);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>The log text of one NPC look: helm (+ what it hides), shoulders, cape, armour geosets, body texture.</summary>
        public static string Describe(NpcGearPlan p, CharacterComposer.Result res, bool baked)
        {
            var g = res.GroupValues;
            string hides = "";
            if (p.HelmModel != null)
            {
                if (p.HideHair) hides += " hair";
                for (int k = 1; k <= 3; k++) if (p.HideFacial[k]) hides += " facial" + k;
                if (p.HideEars) hides += " ears";
            }
            return "helm " + (p.HelmModel != null ? Short(p.HelmModel) + " (vis " + p.HelmVisId + (hides.Length > 0 ? ", hides" + hides : "") + ")" : p.HelmItem > 0 ? "item " + p.HelmItem + " not drawn" : "none")
                + ", shoulders " + (p.LeftShoulderModel != null || p.RightShoulderModel != null ? Short(p.LeftShoulderModel) + " / " + Short(p.RightShoulderModel) : "none")
                + ", cape " + (res.CapeTexture != null ? Short(res.CapeTexture) + " (geoset " + (1500 + g[15]) + ")" : "none")
                + ", geosets gloves " + (400 + g[4]) + " boots " + (500 + g[5]) + " sleeves " + (800 + g[8]) + " legs " + (900 + g[9]) + " tabard " + (1200 + g[12]) + " robe " + (1300 + g[13])
                + ", body " + (baked ? "baked" : "composed") + (p.Notes.Length > 0 ? ";" + p.Notes : "");
        }

        /// <summary>Short file name without folder and extension (for the log).</summary>
        public static string Short(string path)
        {
            if (string.IsNullOrEmpty(path)) return "none";
            int s = path.LastIndexOf('\\'), d = path.LastIndexOf('.');
            return d > s ? path.Substring(s + 1, d - s - 1) : path.Substring(s + 1);
        }
    }
}
