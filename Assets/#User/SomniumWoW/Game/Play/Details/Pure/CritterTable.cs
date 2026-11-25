using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Details
{
    /// <summary>
    /// Which birds circle over a zone (pure). Display ids MEASURED in the player's files (Details report: CreatureModelData row by
    /// model file name -> CreatureDisplayInfo rows -> the .m2 has Fly 135 data). The zone -> climate table lives in
    /// Play/Rules/WowCritters (iteration 5: the ground critters are real session entities now, OfflineSession.Critters); the bird
    /// per climate is ours.
    /// </summary>
    public static class CritterTable
    {
        // birds (all have Fly 135 data)
        public const int StormCrow = 16731, OwlBrown = 4615, OwlWhite = 6212, OwlGrey = 4877, CarrionBird = 410;

        public static WowClimate ClimateOf(int zoneId) { return WowCritters.ClimateOf(zoneId); }

        /// <summary>The bird of a climate by day or night (owls at night).</summary>
        public static int Bird(WowClimate c, bool night)
        {
            switch (c)
            {
                case WowClimate.Dry: return night ? OwlGrey : CarrionBird;
                case WowClimate.Snow: return night ? OwlWhite : StormCrow;
                case WowClimate.Plague: return night ? OwlGrey : StormCrow;
                default: return night ? OwlBrown : StormCrow;
            }
        }

        /// <summary>Extra size factor on top of the display's own scale: the carrion bird is a mob-sized vulture (display 410 is
        /// scale 0.85 of a ~8 yd model), halved to read as a bird circling overhead. Ours.</summary>
        public static float ExtraScale(int displayId)
        {
            return displayId == CarrionBird ? 0.5f : 1f;
        }

        /// <summary>The top zone (ParentAreaID chain up to 0) of an MCNK area id; 0 when unknown.</summary>
        public static int ZoneOf(DbcTable areaTable, int areaId) { return WowCritters.ZoneOf(areaTable, areaId); }
    }
}
