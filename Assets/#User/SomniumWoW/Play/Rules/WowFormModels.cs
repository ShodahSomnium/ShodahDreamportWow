// iteration 11 - owner avatar (proposal-avatar.md 5.4). Pure: no Unity. Server data, like the baked mounts: what AzerothCore draws a
// player as in a form or a transform, for the OFFLINE session (online the realm sends UNIT_FIELD_DISPLAYID itself) and the probes.
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>AzerothCore's model rules for player forms and transforms: Unit::GetModelForForm (Unit.cpp:15056-15097),
    /// player_shapeshift_model's CustomizationID 255 rows (offline characters have no hair/skin colour: ours), creature_template_model
    /// idx 0 of the transform creatures (deterministic: ours; AC picks by probability, ObjectMgr::ChooseDisplayId).</summary>
    public static class WowFormModels
    {
        /// <summary>SpellShapeshiftForm.dbc model fields: 23 = modelID_A, 24 = modelID_H (measured, harness AvFormRows; SpellFields.cs
        /// names only field 23).</summary>
        public const int FieldModelA = 23, FieldModelH = 24;

        // data/sql/base/db_world/player_shapeshift_model.sql:40-157 - the CustomizationID 255 ("any colour") rows: (form, race) -> display.
        private static readonly int[] FormRaceModel =
        {
            1, 4, 892,   1, 6, 8571,   5, 4, 2281,   5, 6, 2289,   8, 4, 2281,   8, 6, 2289,
            27, 4, 21243,   27, 6, 21244,   29, 4, 20857,   29, 6, 20872,
        };

        // data/sql/base/db_world/creature_template_model.sql: creature entry -> CreatureDisplayID of Idx 0 (ours: AC picks among the rows
        // by Probability). Polymorph 118 sheep, Turtle, Pig, Black Cat, Rabbit, Turkey, Hex frog, the Night Elf ghost's wisp 12861
        // (spell 20584), Noggenfogger 531, costumes 1554 / 15376 / 3681 / 1211 / 24483 / 14886 (measured AvSpells / AvDisplays).
        private static readonly int[] TransformTable =
        {
            16372, 856,   16377, 16361,   16371, 16358,   32570, 28067,   32789, 328,   32822, 22191,   13321, 901,   12861, 1825,
            531, 7550,   1554, 8808,   15376, 10478,   3681, 10045,   1211, 6921,   24483, 5379,   14886, 328,
        };

        private static readonly HashSet<int> _unknownSaid = new HashSet<int>();
        private static readonly object _lock = new object();

        /// <summary>The CreatureDisplayInfo id a player of this race shows in a form (0 = the form has no model: stances, stealth,
        /// shadowform). Unit::GetModelForForm: spells 7090 -> 29414 and 35200 -> 4877 hard-coded; players: player_shapeshift_model
        /// (the "255" rows); else SpellShapeshiftForm modelID_A for the Alliance, modelID_H for the Horde (falling back to A), nothing
        /// when modelID_A is 0.</summary>
        public static int ModelForForm(WowFiles files, int form, int spellId, int race)
        {
            if (spellId == 7090) return 29414;   // Unit.cpp:15061-15062 "Bear form"
            if (spellId == 35200) return 4877;   // Unit.cpp:15063-15064 "Roc form"
            if (form <= 0) return 0;
            for (int i = 0; i + 2 < FormRaceModel.Length; i += 3)
                if (FormRaceModel[i] == form && FormRaceModel[i + 1] == race) return FormRaceModel[i + 2];
            DbcTable t = files != null ? Dbc.Load(files, "SpellShapeshiftForm") : null;
            int r = t != null ? t.FindRow(form) : -1;
            if (r < 0) return 0;
            int a = t.GetInt(r, FieldModelA);
            if (a == 0) return 0;
            if (IsHorde(files, race))
            {
                int h = t.GetInt(r, FieldModelH);
                return h != 0 ? h : a;
            }
            return a;
        }

        /// <summary>The display a TRANSFORM (aura 56) to this creature entry shows, 0 = an entry we do not know (logged once per entry).</summary>
        public static int TransformDisplay(int creatureEntry)
        {
            for (int i = 0; i + 1 < TransformTable.Length; i += 2)
                if (TransformTable[i] == creatureEntry) return TransformTable[i + 1];
            if (creatureEntry > 0)
            {
                bool first;
                lock (_lock) first = _unknownSaid.Add(creatureEntry);
                if (first) WowLog.Info("Forms: transform to creature " + creatureEntry + " is not in our offline table (creature_template_model) - the avatar keeps its own look offline");
            }
            return 0;
        }

        /// <summary>The race's own display (ChrRaces.dbc MaleDisplayID field 4, FemaleDisplayID field 5 when gender == 1), 0 unknown.</summary>
        public static int NativeDisplay(WowFiles files, int race, int gender)
        {
            DbcTable t = files != null ? Dbc.Load(files, "ChrRaces") : null;
            int r = t != null ? t.FindRow(race) : -1;
            if (r < 0) return 0;
            return t.GetInt(r, gender == 1 ? DbcFields.ChrRaces.FemaleDisplayID : DbcFields.ChrRaces.MaleDisplayID);
        }

        /// <summary>Player::TeamIdForRace (Player.cpp:5998-6008): ChrRaces field 7 (AzerothCore's ChrRacesEntry.TeamID) 1 = Horde,
        /// 7 = Alliance; without the DBC the 3.3.5a lists (Horde 2, 5, 6, 8, 10).</summary>
        public static bool IsHorde(WowFiles files, int race)
        {
            DbcTable t = files != null ? Dbc.Load(files, "ChrRaces") : null;
            int r = t != null ? t.FindRow(race) : -1;
            if (r >= 0)
            {
                int team = t.GetInt(r, DbcFields.ChrRaces.BaseLanguage);
                if (team == 1) return true;
                if (team == 7) return false;
            }
            return race == 2 || race == 5 || race == 6 || race == 8 || race == 10;
        }
    }
}
