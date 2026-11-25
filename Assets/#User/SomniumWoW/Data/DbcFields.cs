// GENERATED from WoWDBDefs (github.com/wowdev/WoWDBDefs, definitions/<Table>.dbd, the layout whose BUILD line covers
// 3.3.5.12340) by the harness probe DbdGen, then verified on the real 3.3.5a files: for every table the DBC header's
// fieldCount equals FieldCount and fieldCount*4 == recordSize (see the Data report for the numbers and sample rows).
// Field index = position of the 4-byte field in the record. "locstring" = 17 fields (16 locale slots + flags):
// read it with DbcTable.GetLocalizedString(row, <constant>). Arrays: constant = first element, add the element index.
namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>Named field indices of the 3.3.5a (build 12340) client DBCs.</summary>
    public static class DbcFields
    {
        /// <summary>Number of 4-byte fields in a locstring (16 locale slots + flags).</summary>
        public const int LocStringFields = 17;

        /// <summary>FieldCount of a table listed here (by DBC name without extension), or 0 when not listed.</summary>
        public static int ExpectedFieldCount(string table)
        {
            switch (table)
            {
                case "AnimationData": return 8;
                case "AreaTable": return 36;
                case "CharBaseInfo": return 2;
                case "CharHairGeosets": return 6;
                case "CharSections": return 10;
                case "CharacterFacialHairStyles": return 8;
                case "ChrClasses": return 60;
                case "ChrRaces": return 69;
                case "CreatureDisplayInfo": return 16;
                case "CreatureDisplayInfoExtra": return 21;
                case "CreatureModelData": return 28;
                case "GameObjectDisplayInfo": return 19;
                case "GroundEffectDoodad": return 3;
                case "GroundEffectTexture": return 11;
                case "HelmetGeosetVisData": return 8;
                case "Item": return 8;
                case "ItemDisplayInfo": return 25;
                case "Light": return 15;
                case "LightFloatBand": return 34;
                case "LightIntBand": return 34;
                case "LightParams": return 9;
                case "LightSkybox": return 3;
                case "LiquidType": return 45;
                case "LoadingScreens": return 4;
                case "Map": return 66;
                case "QuestSort": return 18;
                case "SoundEntries": return 30;
                case "TaxiNodes": return 24;
                case "WorldSafeLocs": return 22;
                case "ZoneMusic": return 8;
                default: return 0;
            }
        }

        /// <summary>
        /// Tables whose fields are 1 byte (measured: CharBaseInfo.dbc = 62 rows x 2 fields, recordSize 2): read them with
        /// DbcTable.GetByte(row, constant) - the constants are then byte offsets.
        /// </summary>
        public static bool IsByteTable(string table) { return table == "CharBaseInfo"; }

        /// <summary>AnimationData.dbc (build 12340), 8 fields. Source: WoWDBDefs AnimationData.dbd.</summary>
        public static class AnimationData
        {
            public const int FieldCount = 8;
            public const int ID = 0; // int
            public const int Name = 1; // string
            public const int Weaponflags = 2; // int
            public const int Bodyflags = 3; // int
            public const int Flags = 4; // int - and0x10: fallback-related,  and0x20: fallback-related, and0x80: fallback-related
            public const int Fallback = 5; // int - visual equivalent
            public const int BehaviorID = 6; // int - semantic equivalent
            public const int BehaviorTier = 7; // int - 0: Normal, 3: Flying
        }

        /// <summary>AreaTable.dbc (build 12340), 36 fields. Source: WoWDBDefs AreaTable.dbd.</summary>
        public static class AreaTable
        {
            public const int FieldCount = 36;
            public const int ID = 0; // int
            public const int ContinentID = 1; // int
            public const int ParentAreaID = 2; // int
            public const int AreaBit = 3; // int
            public const int Flags = 4; // int
            public const int SoundProviderPref = 5; // int
            public const int SoundProviderPrefUnderwater = 6; // int
            public const int AmbienceID = 7; // int
            public const int ZoneMusic = 8; // int
            public const int IntroSound = 9; // int
            public const int ExplorationLevel = 10; // int
            public const int AreaName_lang = 11; // locstring (16 locales + flags)
            public const int FactionGroupMask = 28; // int
            public const int LiquidTypeID = 29; // int [4]
            public const int MinElevation = 33; // float
            public const int Ambient_multiplier = 34; // float
            public const int LightID = 35; // int
        }

        /// <summary>CharBaseInfo.dbc (build 12340), 2 fields. Source: WoWDBDefs CharBaseInfo.dbd.</summary>
        public static class CharBaseInfo
        {
            public const int FieldCount = 2;
            public const int RaceID = 0; // byte (measured recordSize 2): read with DbcTable.GetByte(row, RaceID)
            public const int ClassID = 1; // byte: DbcTable.GetByte(row, ClassID)
        }

        /// <summary>CharHairGeosets.dbc (build 12340), 6 fields. Source: WoWDBDefs CharHairGeosets.dbd.</summary>
        public static class CharHairGeosets
        {
            public const int FieldCount = 6;
            public const int ID = 0; // int
            public const int RaceID = 1; // int
            public const int SexID = 2; // int
            public const int VariationID = 3; // int
            public const int GeosetID = 4; // int
            public const int Showscalp = 5; // int
        }

        /// <summary>CharSections.dbc (build 12340), 10 fields. Source: WoWDBDefs CharSections.dbd.</summary>
        public static class CharSections
        {
            public const int FieldCount = 10;
            public const int ID = 0; // int
            public const int RaceID = 1; // int
            public const int SexID = 2; // int
            public const int BaseSection = 3; // int
            public const int TextureName = 4; // string [3]
            public const int Flags = 7; // int
            public const int VariationIndex = 8; // int
            public const int ColorIndex = 9; // int
        }

        /// <summary>CharacterFacialHairStyles.dbc (build 12340), 8 fields. Source: WoWDBDefs CharacterFacialHairStyles.dbd.</summary>
        public static class CharacterFacialHairStyles
        {
            public const int FieldCount = 8;
            public const int RaceID = 0; // int
            public const int SexID = 1; // int
            public const int VariationID = 2; // int
            public const int Geoset = 3; // int [5]
        }

        /// <summary>ChrClasses.dbc (build 12340), 60 fields. Source: WoWDBDefs ChrClasses.dbd.</summary>
        public static class ChrClasses
        {
            public const int FieldCount = 60;
            public const int ID = 0; // int
            public const int DamageBonusStat = 1; // int
            public const int DisplayPower = 2; // int - Not really an FK since that table does not exist in all versions, but that enum is still common across all versions.
            public const int PetNameToken = 3; // string
            public const int Name_lang = 4; // locstring (16 locales + flags)
            public const int Name_female_lang = 21; // locstring (16 locales + flags)
            public const int Name_male_lang = 38; // locstring (16 locales + flags)
            public const int Filename = 55; // string
            public const int SpellClassSet = 56; // int
            public const int Flags = 57; // int - and0x10: Mail, and0x20: Plate, and0x800: !allowBoost, and0x2000: earlyFactionChoice
            public const int CinematicSequenceID = 58; // int
            public const int Required_expansion = 59; // int
        }

        /// <summary>ChrRaces.dbc (build 12340), 69 fields. Source: WoWDBDefs ChrRaces.dbd.</summary>
        public static class ChrRaces
        {
            public const int FieldCount = 69;
            public const int ID = 0; // int
            public const int Flags = 1; // int
            public const int FactionID = 2; // int
            public const int ExplorationSoundID = 3; // int
            public const int MaleDisplayID = 4; // int
            public const int FemaleDisplayID = 5; // int
            public const int ClientPrefix = 6; // string
            public const int BaseLanguage = 7; // int
            public const int CreatureType = 8; // int
            public const int ResSicknessSpellID = 9; // int
            public const int SplashSoundID = 10; // int
            public const int ClientFileString = 11; // string
            public const int CinematicSequenceID = 12; // int
            public const int Alliance = 13; // int
            public const int Name_lang = 14; // locstring (16 locales + flags) - quest text $R
            public const int Name_female_lang = 31; // locstring (16 locales + flags) - quest text $R
            public const int Name_male_lang = 48; // locstring (16 locales + flags)
            public const int FacialHairCustomization = 65; // string [2]
            public const int HairCustomization = 67; // string
            public const int Required_expansion = 68; // int
        }

        /// <summary>CreatureDisplayInfo.dbc (build 12340), 16 fields. Source: WoWDBDefs CreatureDisplayInfo.dbd.</summary>
        public static class CreatureDisplayInfo
        {
            public const int FieldCount = 16;
            public const int ID = 0; // int
            public const int ModelID = 1; // int
            public const int SoundID = 2; // int
            public const int ExtendedDisplayInfoID = 3; // int
            public const int CreatureModelScale = 4; // float
            public const int CreatureModelAlpha = 5; // int
            public const int TextureVariation = 6; // string [3]
            public const int PortraitTextureName = 9; // string
            public const int SizeClass = 10; // int
            public const int BloodID = 11; // int
            public const int NPCSoundID = 12; // int
            public const int ParticleColorID = 13; // int
            public const int CreatureGeosetData = 14; // int
            public const int ObjectEffectPackageID = 15; // int
        }

        /// <summary>CreatureDisplayInfoExtra.dbc (build 12340), 21 fields. Source: WoWDBDefs CreatureDisplayInfoExtra.dbd.</summary>
        public static class CreatureDisplayInfoExtra
        {
            public const int FieldCount = 21;
            public const int ID = 0; // int
            public const int DisplayRaceID = 1; // int
            public const int DisplaySexID = 2; // int
            public const int SkinID = 3; // int
            public const int FaceID = 4; // int
            public const int HairStyleID = 5; // int
            public const int HairColorID = 6; // int
            public const int FacialHairID = 7; // int
            public const int NPCItemDisplay = 8; // int [11]
            public const int Flags = 19; // int
            public const int BakeName = 20; // string
        }

        /// <summary>CreatureModelData.dbc (build 12340), 28 fields. Source: WoWDBDefs CreatureModelData.dbd.</summary>
        public static class CreatureModelData
        {
            public const int FieldCount = 28;
            public const int ID = 0; // int
            public const int Flags = 1; // int
            public const int ModelName = 2; // string
            public const int SizeClass = 3; // int
            public const int ModelScale = 4; // float
            public const int BloodID = 5; // int
            public const int FootprintTextureID = 6; // int
            public const int FootprintTextureLength = 7; // float
            public const int FootprintTextureWidth = 8; // float
            public const int FootprintParticleScale = 9; // float
            public const int FoleyMaterialID = 10; // int
            public const int FootstepShakeSize = 11; // int
            public const int DeathThudShakeSize = 12; // int
            public const int SoundID = 13; // int
            public const int CollisionWidth = 14; // float
            public const int CollisionHeight = 15; // float
            public const int MountHeight = 16; // float
            public const int GeoBoxMinX = 17; // float
            public const int GeoBoxMinY = 18; // float
            public const int GeoBoxMinZ = 19; // float
            public const int GeoBoxMaxX = 20; // float
            public const int GeoBoxMaxY = 21; // float
            public const int GeoBoxMaxZ = 22; // float
            public const int WorldEffectScale = 23; // float
            public const int AttachedEffectScale = 24; // float
            public const int MissileCollisionRadius = 25; // float
            public const int MissileCollisionPush = 26; // float
            public const int MissileCollisionRaise = 27; // float
        }

        /// <summary>GameObjectDisplayInfo.dbc (build 12340), 19 fields. Source: WoWDBDefs GameObjectDisplayInfo.dbd.</summary>
        public static class GameObjectDisplayInfo
        {
            public const int FieldCount = 19;
            public const int ID = 0; // int
            public const int ModelName = 1; // string
            public const int Sound = 2; // int [10]
            public const int GeoBoxMin = 12; // float [3]
            public const int GeoBoxMax = 15; // float [3]
            public const int ObjectEffectPackageID = 18; // int
        }

        /// <summary>GroundEffectDoodad.dbc (build 12340), 3 fields. Source: WoWDBDefs GroundEffectDoodad.dbd.</summary>
        public static class GroundEffectDoodad
        {
            public const int FieldCount = 3;
            public const int ID = 0; // int
            public const int Doodadpath = 1; // string
            public const int Flags = 2; // int
        }

        /// <summary>GroundEffectTexture.dbc (build 12340), 11 fields. Source: WoWDBDefs GroundEffectTexture.dbd.</summary>
        public static class GroundEffectTexture
        {
            public const int FieldCount = 11;
            public const int ID = 0; // int
            public const int DoodadID = 1; // int [4]
            public const int DoodadWeight = 5; // int [4]
            public const int Density = 9; // int - 8..24
            public const int Sound = 10; // int
        }

        /// <summary>HelmetGeosetVisData.dbc (build 12340), 8 fields. Source: WoWDBDefs HelmetGeosetVisData.dbd.</summary>
        public static class HelmetGeosetVisData
        {
            public const int FieldCount = 8;
            public const int ID = 0; // int
            public const int HideGeoset = 1; // int [7]
        }

        /// <summary>Item.dbc (build 12340), 8 fields. Source: WoWDBDefs Item.dbd.</summary>
        public static class Item
        {
            public const int FieldCount = 8;
            public const int ID = 0; // int
            public const int ClassID = 1; // int
            public const int SubclassID = 2; // int
            public const int Sound_override_subclassID = 3; // int
            public const int Material = 4; // int
            public const int DisplayInfoID = 5; // int
            public const int InventoryType = 6; // int
            public const int SheatheType = 7; // int
        }

        /// <summary>ItemDisplayInfo.dbc (build 12340), 25 fields. Source: WoWDBDefs ItemDisplayInfo.dbd.</summary>
        public static class ItemDisplayInfo
        {
            public const int FieldCount = 25;
            public const int ID = 0; // int
            public const int ModelName = 1; // string [2]
            public const int ModelTexture = 3; // string [2]
            public const int InventoryIcon = 5; // string [2]
            public const int GeosetGroup = 7; // int [3]
            public const int Flags = 10; // int
            public const int SpellVisualID = 11; // int
            public const int GroupSoundIndex = 12; // int
            public const int HelmetGeosetVisID = 13; // int [2]
            public const int Texture = 15; // string [8]
            public const int ItemVisual = 23; // int
            public const int ParticleColorID = 24; // int
        }

        /// <summary>Light.dbc (build 12340), 15 fields. Source: WoWDBDefs Light.dbd.</summary>
        public static class Light
        {
            public const int FieldCount = 15;
            public const int ID = 0; // int
            public const int ContinentID = 1; // int
            public const int GameCoords = 2; // float [3]
            public const int GameFalloffStart = 5; // float
            public const int GameFalloffEnd = 6; // float
            public const int LightParamsID = 7; // int [8]
        }

        /// <summary>LightFloatBand.dbc (build 12340), 34 fields. Source: WoWDBDefs LightFloatBand.dbd.</summary>
        public static class LightFloatBand
        {
            public const int FieldCount = 34;
            public const int ID = 0; // int
            public const int Num = 1; // int
            public const int Time = 2; // int [16]
            public const int Data = 18; // float [16]
        }

        /// <summary>LightIntBand.dbc (build 12340), 34 fields. Source: WoWDBDefs LightIntBand.dbd.</summary>
        public static class LightIntBand
        {
            public const int FieldCount = 34;
            public const int ID = 0; // int
            public const int Num = 1; // int
            public const int Time = 2; // int [16]
            public const int Data = 18; // int [16]
        }

        /// <summary>LightParams.dbc (build 12340), 9 fields. Source: WoWDBDefs LightParams.dbd.</summary>
        public static class LightParams
        {
            public const int FieldCount = 9;
            public const int ID = 0; // int
            public const int HighlightSky = 1; // int
            public const int LightSkyboxID = 2; // int
            public const int Glow = 3; // float
            public const int WaterShallowAlpha = 4; // float
            public const int WaterDeepAlpha = 5; // float
            public const int OceanShallowAlpha = 6; // float
            public const int OceanDeepAlpha = 7; // float
            public const int Flags = 8; // int
        }

        /// <summary>LightSkybox.dbc (build 12340), 3 fields. Source: WoWDBDefs LightSkybox.dbd.</summary>
        public static class LightSkybox
        {
            public const int FieldCount = 3;
            public const int ID = 0; // int
            public const int Name = 1; // string
            public const int Flags = 2; // int
        }

        /// <summary>LiquidType.dbc (build 12340), 45 fields. Source: WoWDBDefs LiquidType.dbd.</summary>
        public static class LiquidType
        {
            public const int FieldCount = 45;
            public const int ID = 0; // int
            public const int Name = 1; // string
            public const int Flags = 2; // int
            public const int SoundBank = 3; // int
            public const int SoundID = 4; // int
            public const int SpellID = 5; // int
            public const int MaxDarkenDepth = 6; // float
            public const int FogDarkenIntensity = 7; // float
            public const int AmbDarkenIntensity = 8; // float
            public const int DirDarkenIntensity = 9; // float
            public const int LightID = 10; // int
            public const int ParticleScale = 11; // float
            public const int ParticleMovement = 12; // int
            public const int ParticleTexSlots = 13; // int
            public const int MaterialID = 14; // int
            public const int Texture = 15; // string [6]
            public const int Color = 21; // int [2]
            public const int Float = 23; // float [18]
            public const int Int = 41; // int [4]
        }

        /// <summary>LoadingScreens.dbc (build 12340), 4 fields. Source: WoWDBDefs LoadingScreens.dbd.</summary>
        public static class LoadingScreens
        {
            public const int FieldCount = 4;
            public const int ID = 0; // int
            public const int Name = 1; // string
            public const int FileName = 2; // string
            public const int HasWideScreen = 3; // int
        }

        /// <summary>Map.dbc (build 12340), 66 fields. Source: WoWDBDefs Map.dbd.</summary>
        public static class Map
        {
            public const int FieldCount = 66;
            public const int ID = 0; // int
            public const int Directory = 1; // string - reference to World\Map\ [...] \
            public const int InstanceType = 2; // int - Integer 0: none, 1: party, 2: raid, 3: pvp, 4: arena, )=5: none (official from "IsInInstance()")
            public const int Flags = 3; // int - and0x100: CanChangeDifficulty
            public const int PVP = 4; // int
            public const int MapName_lang = 5; // locstring (16 locales + flags)
            public const int AreaTableID = 22; // int
            public const int MapDescription0_lang = 23; // locstring (16 locales + flags) - Horde
            public const int MapDescription1_lang = 40; // locstring (16 locales + flags) - Alliance
            public const int LoadingScreenID = 57; // int
            public const int MinimapIconScale = 58; // float
            public const int CorpseMapID = 59; // int
            public const int Corpse = 60; // float [2] - Array of CorpsePosX/CorpsePosY
            public const int TimeOfDayOverride = 62; // int - Set to -1 for everything but Orgrimmar and Dalaran arena. For those, the time of day will change to this.
            public const int ExpansionID = 63; // int - Vanilla: 0, BC: 1, WotLK: 2 # todo enum
            public const int RaidOffset = 64; // int
            public const int MaxPlayers = 65; // int
        }

        /// <summary>SoundEntries.dbc (build 12340), 30 fields. Source: WoWDBDefs SoundEntries.dbd.</summary>
        public static class SoundEntries
        {
            public const int FieldCount = 30;
            public const int ID = 0; // int
            public const int SoundType = 1; // int
            public const int Name = 2; // string
            public const int File = 3; // string [10]
            public const int Freq = 13; // int [10]
            public const int DirectoryBase = 23; // string
            public const int VolumeFloat = 24; // float
            public const int Flags = 25; // int
            public const int MinDistance = 26; // float
            public const int DistanceCutoff = 27; // float
            public const int EAXDef = 28; // int
            public const int SoundEntriesAdvancedID = 29; // int
        }

        /// <summary>TaxiNodes.dbc (build 12340), 24 fields. Source: WoWDBDefs TaxiNodes.dbd.</summary>
        public static class TaxiNodes
        {
            public const int FieldCount = 24;
            public const int ID = 0; // int
            public const int ContinentID = 1; // int
            public const int Pos = 2; // float [3]
            public const int Name_lang = 5; // locstring (16 locales + flags)
            public const int MountCreatureID = 22; // int [2]
        }

        /// <summary>WorldSafeLocs.dbc (build 12340), 22 fields. Source: WoWDBDefs WorldSafeLocs.dbd.</summary>
        public static class WorldSafeLocs
        {
            public const int FieldCount = 22;
            public const int ID = 0; // int
            public const int Continent = 1; // int
            public const int Loc = 2; // float [3]
            public const int AreaName_lang = 5; // locstring (16 locales + flags)
        }

        /// <summary>QuestSort.dbc (build 12340), 18 fields (13.7: measured - 41 rows, 81 "Warrior", 304 "Cooking"). Source: WoWDBDefs QuestSort.dbd.</summary>
        public static class QuestSort
        {
            public const int FieldCount = 18;
            public const int ID = 0; // int
            public const int SortName_lang = 1; // locstring (16 locales + flags)
        }

        /// <summary>ZoneMusic.dbc (build 12340), 8 fields. Source: WoWDBDefs ZoneMusic.dbd.</summary>
        public static class ZoneMusic
        {
            public const int FieldCount = 8;
            public const int ID = 0; // int
            public const int SetName = 1; // string
            public const int SilenceIntervalMin = 2; // int [2]
            public const int SilenceIntervalMax = 4; // int [2]
            public const int Sounds = 6; // int [2]
        }

    }
}
