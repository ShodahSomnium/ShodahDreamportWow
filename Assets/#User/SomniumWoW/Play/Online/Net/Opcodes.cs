namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// World-protocol opcodes of the 3.3.5a client (build 12340). GENERATED from AzerothCore's
    /// src/server/game/Server/Protocol/Opcodes.h (enum Opcodes : uint16, 0x001..0x520, no gaps) - do not edit by hand.
    /// Client-&gt;server opcodes are sent as uint32, server-&gt;client as uint16 (see WorldFraming).
    /// </summary>
    public static class Op
    {
        public const ushort CMSG_BOOTME = 0x001;
        public const ushort CMSG_DBLOOKUP = 0x002;
        public const ushort SMSG_DBLOOKUP = 0x003;
        public const ushort CMSG_QUERY_OBJECT_POSITION = 0x004;
        public const ushort SMSG_QUERY_OBJECT_POSITION = 0x005;
        public const ushort CMSG_QUERY_OBJECT_ROTATION = 0x006;
        public const ushort SMSG_QUERY_OBJECT_ROTATION = 0x007;
        public const ushort CMSG_WORLD_TELEPORT = 0x008;
        public const ushort CMSG_TELEPORT_TO_UNIT = 0x009;
        public const ushort CMSG_ZONE_MAP = 0x00A;
        public const ushort SMSG_ZONE_MAP = 0x00B;
        public const ushort CMSG_DEBUG_CHANGECELLZONE = 0x00C;
        public const ushort CMSG_MOVE_CHARACTER_CHEAT = 0x00D;
        public const ushort SMSG_MOVE_CHARACTER_CHEAT = 0x00E;
        public const ushort CMSG_RECHARGE = 0x00F;
        public const ushort CMSG_LEARN_SPELL = 0x010;
        public const ushort CMSG_CREATEMONSTER = 0x011;
        public const ushort CMSG_DESTROYMONSTER = 0x012;
        public const ushort CMSG_CREATEITEM = 0x013;
        public const ushort CMSG_CREATEGAMEOBJECT = 0x014;
        public const ushort SMSG_CHECK_FOR_BOTS = 0x015;
        public const ushort CMSG_MAKEMONSTERATTACKGUID = 0x016;
        public const ushort CMSG_BOT_DETECTED2 = 0x017;
        public const ushort CMSG_FORCEACTION = 0x018;
        public const ushort CMSG_FORCEACTIONONOTHER = 0x019;
        public const ushort CMSG_FORCEACTIONSHOW = 0x01A;
        public const ushort SMSG_FORCEACTIONSHOW = 0x01B;
        public const ushort CMSG_PETGODMODE = 0x01C;
        public const ushort SMSG_PETGODMODE = 0x01D;
        public const ushort SMSG_REFER_A_FRIEND_EXPIRED = 0x01E;
        public const ushort CMSG_WEATHER_SPEED_CHEAT = 0x01F;
        public const ushort CMSG_UNDRESSPLAYER = 0x020;
        public const ushort CMSG_BEASTMASTER = 0x021;
        public const ushort CMSG_GODMODE = 0x022;
        public const ushort SMSG_GODMODE = 0x023;
        public const ushort CMSG_CHEAT_SETMONEY = 0x024;
        public const ushort CMSG_LEVEL_CHEAT = 0x025;
        public const ushort CMSG_PET_LEVEL_CHEAT = 0x026;
        public const ushort CMSG_SET_WORLDSTATE = 0x027;
        public const ushort CMSG_COOLDOWN_CHEAT = 0x028;
        public const ushort CMSG_USE_SKILL_CHEAT = 0x029;
        public const ushort CMSG_FLAG_QUEST = 0x02A;
        public const ushort CMSG_FLAG_QUEST_FINISH = 0x02B;
        public const ushort CMSG_CLEAR_QUEST = 0x02C;
        public const ushort CMSG_SEND_EVENT = 0x02D;
        public const ushort CMSG_DEBUG_AISTATE = 0x02E;
        public const ushort SMSG_DEBUG_AISTATE = 0x02F;
        public const ushort CMSG_DISABLE_PVP_CHEAT = 0x030;
        public const ushort CMSG_ADVANCE_SPAWN_TIME = 0x031;
        public const ushort SMSG_DESTRUCTIBLE_BUILDING_DAMAGE = 0x032;
        public const ushort CMSG_AUTH_SRP6_BEGIN = 0x033;
        public const ushort CMSG_AUTH_SRP6_PROOF = 0x034;
        public const ushort CMSG_AUTH_SRP6_RECODE = 0x035;
        public const ushort CMSG_CHAR_CREATE = 0x036;
        public const ushort CMSG_CHAR_ENUM = 0x037;
        public const ushort CMSG_CHAR_DELETE = 0x038;
        public const ushort SMSG_AUTH_SRP6_RESPONSE = 0x039;
        public const ushort SMSG_CHAR_CREATE = 0x03A;
        public const ushort SMSG_CHAR_ENUM = 0x03B;
        public const ushort SMSG_CHAR_DELETE = 0x03C;
        public const ushort CMSG_PLAYER_LOGIN = 0x03D;
        public const ushort SMSG_NEW_WORLD = 0x03E;
        public const ushort SMSG_TRANSFER_PENDING = 0x03F;
        public const ushort SMSG_TRANSFER_ABORTED = 0x040;
        public const ushort SMSG_CHARACTER_LOGIN_FAILED = 0x041;
        public const ushort SMSG_LOGIN_SETTIMESPEED = 0x042;
        public const ushort SMSG_GAMETIME_UPDATE = 0x043;
        public const ushort CMSG_GAMETIME_SET = 0x044;
        public const ushort SMSG_GAMETIME_SET = 0x045;
        public const ushort CMSG_GAMESPEED_SET = 0x046;
        public const ushort SMSG_GAMESPEED_SET = 0x047;
        public const ushort CMSG_SERVERTIME = 0x048;
        public const ushort SMSG_SERVERTIME = 0x049;
        public const ushort CMSG_PLAYER_LOGOUT = 0x04A;
        public const ushort CMSG_LOGOUT_REQUEST = 0x04B;
        public const ushort SMSG_LOGOUT_RESPONSE = 0x04C;
        public const ushort SMSG_LOGOUT_COMPLETE = 0x04D;
        public const ushort CMSG_LOGOUT_CANCEL = 0x04E;
        public const ushort SMSG_LOGOUT_CANCEL_ACK = 0x04F;
        public const ushort CMSG_NAME_QUERY = 0x050;
        public const ushort SMSG_NAME_QUERY_RESPONSE = 0x051;
        public const ushort CMSG_PET_NAME_QUERY = 0x052;
        public const ushort SMSG_PET_NAME_QUERY_RESPONSE = 0x053;
        public const ushort CMSG_GUILD_QUERY = 0x054;
        public const ushort SMSG_GUILD_QUERY_RESPONSE = 0x055;
        public const ushort CMSG_ITEM_QUERY_SINGLE = 0x056;
        public const ushort CMSG_ITEM_QUERY_MULTIPLE = 0x057;
        public const ushort SMSG_ITEM_QUERY_SINGLE_RESPONSE = 0x058;
        public const ushort SMSG_ITEM_QUERY_MULTIPLE_RESPONSE = 0x059;
        public const ushort CMSG_PAGE_TEXT_QUERY = 0x05A;
        public const ushort SMSG_PAGE_TEXT_QUERY_RESPONSE = 0x05B;
        public const ushort CMSG_QUEST_QUERY = 0x05C;
        public const ushort SMSG_QUEST_QUERY_RESPONSE = 0x05D;
        public const ushort CMSG_GAMEOBJECT_QUERY = 0x05E;
        public const ushort SMSG_GAMEOBJECT_QUERY_RESPONSE = 0x05F;
        public const ushort CMSG_CREATURE_QUERY = 0x060;
        public const ushort SMSG_CREATURE_QUERY_RESPONSE = 0x061;
        public const ushort CMSG_WHO = 0x062;
        public const ushort SMSG_WHO = 0x063;
        public const ushort CMSG_WHOIS = 0x064;
        public const ushort SMSG_WHOIS = 0x065;
        public const ushort CMSG_CONTACT_LIST = 0x066;
        public const ushort SMSG_CONTACT_LIST = 0x067;
        public const ushort SMSG_FRIEND_STATUS = 0x068;
        public const ushort CMSG_ADD_FRIEND = 0x069;
        public const ushort CMSG_DEL_FRIEND = 0x06A;
        public const ushort CMSG_SET_CONTACT_NOTES = 0x06B;
        public const ushort CMSG_ADD_IGNORE = 0x06C;
        public const ushort CMSG_DEL_IGNORE = 0x06D;
        public const ushort CMSG_GROUP_INVITE = 0x06E;
        public const ushort SMSG_GROUP_INVITE = 0x06F;
        public const ushort CMSG_GROUP_CANCEL = 0x070;
        public const ushort SMSG_GROUP_CANCEL = 0x071;
        public const ushort CMSG_GROUP_ACCEPT = 0x072;
        public const ushort CMSG_GROUP_DECLINE = 0x073;
        public const ushort SMSG_GROUP_DECLINE = 0x074;
        public const ushort CMSG_GROUP_UNINVITE = 0x075;
        public const ushort CMSG_GROUP_UNINVITE_GUID = 0x076;
        public const ushort SMSG_GROUP_UNINVITE = 0x077;
        public const ushort CMSG_GROUP_SET_LEADER = 0x078;
        public const ushort SMSG_GROUP_SET_LEADER = 0x079;
        public const ushort CMSG_LOOT_METHOD = 0x07A;
        public const ushort CMSG_GROUP_DISBAND = 0x07B;
        public const ushort SMSG_GROUP_DESTROYED = 0x07C;
        public const ushort SMSG_GROUP_LIST = 0x07D;
        public const ushort SMSG_PARTY_MEMBER_STATS = 0x07E;
        public const ushort SMSG_PARTY_COMMAND_RESULT = 0x07F;
        public const ushort UMSG_UPDATE_GROUP_MEMBERS = 0x080;
        public const ushort CMSG_GUILD_CREATE = 0x081;
        public const ushort CMSG_GUILD_INVITE = 0x082;
        public const ushort SMSG_GUILD_INVITE = 0x083;
        public const ushort CMSG_GUILD_ACCEPT = 0x084;
        public const ushort CMSG_GUILD_DECLINE = 0x085;
        public const ushort SMSG_GUILD_DECLINE = 0x086;
        public const ushort CMSG_GUILD_INFO = 0x087;
        public const ushort SMSG_GUILD_INFO = 0x088;
        public const ushort CMSG_GUILD_ROSTER = 0x089;
        public const ushort SMSG_GUILD_ROSTER = 0x08A;
        public const ushort CMSG_GUILD_PROMOTE = 0x08B;
        public const ushort CMSG_GUILD_DEMOTE = 0x08C;
        public const ushort CMSG_GUILD_LEAVE = 0x08D;
        public const ushort CMSG_GUILD_REMOVE = 0x08E;
        public const ushort CMSG_GUILD_DISBAND = 0x08F;
        public const ushort CMSG_GUILD_LEADER = 0x090;
        public const ushort CMSG_GUILD_MOTD = 0x091;
        public const ushort SMSG_GUILD_EVENT = 0x092;
        public const ushort SMSG_GUILD_COMMAND_RESULT = 0x093;
        public const ushort UMSG_UPDATE_GUILD = 0x094;
        public const ushort CMSG_MESSAGECHAT = 0x095;
        public const ushort SMSG_MESSAGECHAT = 0x096;
        public const ushort CMSG_JOIN_CHANNEL = 0x097;
        public const ushort CMSG_LEAVE_CHANNEL = 0x098;
        public const ushort SMSG_CHANNEL_NOTIFY = 0x099;
        public const ushort CMSG_CHANNEL_LIST = 0x09A;
        public const ushort SMSG_CHANNEL_LIST = 0x09B;
        public const ushort CMSG_CHANNEL_PASSWORD = 0x09C;
        public const ushort CMSG_CHANNEL_SET_OWNER = 0x09D;
        public const ushort CMSG_CHANNEL_OWNER = 0x09E;
        public const ushort CMSG_CHANNEL_MODERATOR = 0x09F;
        public const ushort CMSG_CHANNEL_UNMODERATOR = 0x0A0;
        public const ushort CMSG_CHANNEL_MUTE = 0x0A1;
        public const ushort CMSG_CHANNEL_UNMUTE = 0x0A2;
        public const ushort CMSG_CHANNEL_INVITE = 0x0A3;
        public const ushort CMSG_CHANNEL_KICK = 0x0A4;
        public const ushort CMSG_CHANNEL_BAN = 0x0A5;
        public const ushort CMSG_CHANNEL_UNBAN = 0x0A6;
        public const ushort CMSG_CHANNEL_ANNOUNCEMENTS = 0x0A7;
        public const ushort CMSG_CHANNEL_MODERATE = 0x0A8;
        public const ushort SMSG_UPDATE_OBJECT = 0x0A9;
        public const ushort SMSG_DESTROY_OBJECT = 0x0AA;
        public const ushort CMSG_USE_ITEM = 0x0AB;
        public const ushort CMSG_OPEN_ITEM = 0x0AC;
        public const ushort CMSG_READ_ITEM = 0x0AD;
        public const ushort SMSG_READ_ITEM_OK = 0x0AE;
        public const ushort SMSG_READ_ITEM_FAILED = 0x0AF;
        public const ushort SMSG_ITEM_COOLDOWN = 0x0B0;
        public const ushort CMSG_GAMEOBJ_USE = 0x0B1;
        public const ushort CMSG_DESTROY_ITEMS = 0x0B2;
        public const ushort SMSG_GAMEOBJECT_CUSTOM_ANIM = 0x0B3;
        public const ushort CMSG_AREATRIGGER = 0x0B4;
        public const ushort MSG_MOVE_START_FORWARD = 0x0B5;
        public const ushort MSG_MOVE_START_BACKWARD = 0x0B6;
        public const ushort MSG_MOVE_STOP = 0x0B7;
        public const ushort MSG_MOVE_START_STRAFE_LEFT = 0x0B8;
        public const ushort MSG_MOVE_START_STRAFE_RIGHT = 0x0B9;
        public const ushort MSG_MOVE_STOP_STRAFE = 0x0BA;
        public const ushort MSG_MOVE_JUMP = 0x0BB;
        public const ushort MSG_MOVE_START_TURN_LEFT = 0x0BC;
        public const ushort MSG_MOVE_START_TURN_RIGHT = 0x0BD;
        public const ushort MSG_MOVE_STOP_TURN = 0x0BE;
        public const ushort MSG_MOVE_START_PITCH_UP = 0x0BF;
        public const ushort MSG_MOVE_START_PITCH_DOWN = 0x0C0;
        public const ushort MSG_MOVE_STOP_PITCH = 0x0C1;
        public const ushort MSG_MOVE_SET_RUN_MODE = 0x0C2;
        public const ushort MSG_MOVE_SET_WALK_MODE = 0x0C3;
        public const ushort MSG_MOVE_TOGGLE_LOGGING = 0x0C4;
        public const ushort MSG_MOVE_TELEPORT = 0x0C5;
        public const ushort MSG_MOVE_TELEPORT_CHEAT = 0x0C6;
        public const ushort MSG_MOVE_TELEPORT_ACK = 0x0C7;
        public const ushort MSG_MOVE_TOGGLE_FALL_LOGGING = 0x0C8;
        public const ushort MSG_MOVE_FALL_LAND = 0x0C9;
        public const ushort MSG_MOVE_START_SWIM = 0x0CA;
        public const ushort MSG_MOVE_STOP_SWIM = 0x0CB;
        public const ushort MSG_MOVE_SET_RUN_SPEED_CHEAT = 0x0CC;
        public const ushort MSG_MOVE_SET_RUN_SPEED = 0x0CD;
        public const ushort MSG_MOVE_SET_RUN_BACK_SPEED_CHEAT = 0x0CE;
        public const ushort MSG_MOVE_SET_RUN_BACK_SPEED = 0x0CF;
        public const ushort MSG_MOVE_SET_WALK_SPEED_CHEAT = 0x0D0;
        public const ushort MSG_MOVE_SET_WALK_SPEED = 0x0D1;
        public const ushort MSG_MOVE_SET_SWIM_SPEED_CHEAT = 0x0D2;
        public const ushort MSG_MOVE_SET_SWIM_SPEED = 0x0D3;
        public const ushort MSG_MOVE_SET_SWIM_BACK_SPEED_CHEAT = 0x0D4;
        public const ushort MSG_MOVE_SET_SWIM_BACK_SPEED = 0x0D5;
        public const ushort MSG_MOVE_SET_ALL_SPEED_CHEAT = 0x0D6;
        public const ushort MSG_MOVE_SET_TURN_RATE_CHEAT = 0x0D7;
        public const ushort MSG_MOVE_SET_TURN_RATE = 0x0D8;
        public const ushort MSG_MOVE_TOGGLE_COLLISION_CHEAT = 0x0D9;
        public const ushort MSG_MOVE_SET_FACING = 0x0DA;
        public const ushort MSG_MOVE_SET_PITCH = 0x0DB;
        public const ushort MSG_MOVE_WORLDPORT_ACK = 0x0DC;
        public const ushort SMSG_MONSTER_MOVE = 0x0DD;
        public const ushort SMSG_MOVE_WATER_WALK = 0x0DE;
        public const ushort SMSG_MOVE_LAND_WALK = 0x0DF;
        public const ushort CMSG_MOVE_CHARM_PORT_CHEAT = 0x0E0;
        public const ushort CMSG_MOVE_SET_RAW_POSITION = 0x0E1;
        public const ushort SMSG_FORCE_RUN_SPEED_CHANGE = 0x0E2;
        public const ushort CMSG_FORCE_RUN_SPEED_CHANGE_ACK = 0x0E3;
        public const ushort SMSG_FORCE_RUN_BACK_SPEED_CHANGE = 0x0E4;
        public const ushort CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK = 0x0E5;
        public const ushort SMSG_FORCE_SWIM_SPEED_CHANGE = 0x0E6;
        public const ushort CMSG_FORCE_SWIM_SPEED_CHANGE_ACK = 0x0E7;
        public const ushort SMSG_FORCE_MOVE_ROOT = 0x0E8;
        public const ushort CMSG_FORCE_MOVE_ROOT_ACK = 0x0E9;
        public const ushort SMSG_FORCE_MOVE_UNROOT = 0x0EA;
        public const ushort CMSG_FORCE_MOVE_UNROOT_ACK = 0x0EB;
        public const ushort MSG_MOVE_ROOT = 0x0EC;
        public const ushort MSG_MOVE_UNROOT = 0x0ED;
        public const ushort MSG_MOVE_HEARTBEAT = 0x0EE;
        public const ushort SMSG_MOVE_KNOCK_BACK = 0x0EF;
        public const ushort CMSG_MOVE_KNOCK_BACK_ACK = 0x0F0;
        public const ushort MSG_MOVE_KNOCK_BACK = 0x0F1;
        public const ushort SMSG_MOVE_FEATHER_FALL = 0x0F2;
        public const ushort SMSG_MOVE_NORMAL_FALL = 0x0F3;
        public const ushort SMSG_MOVE_SET_HOVER = 0x0F4;
        public const ushort SMSG_MOVE_UNSET_HOVER = 0x0F5;
        public const ushort CMSG_MOVE_HOVER_ACK = 0x0F6;
        public const ushort MSG_MOVE_HOVER = 0x0F7;
        public const ushort CMSG_TRIGGER_CINEMATIC_CHEAT = 0x0F8;
        public const ushort CMSG_OPENING_CINEMATIC = 0x0F9;
        public const ushort SMSG_TRIGGER_CINEMATIC = 0x0FA;
        public const ushort CMSG_NEXT_CINEMATIC_CAMERA = 0x0FB;
        public const ushort CMSG_COMPLETE_CINEMATIC = 0x0FC;
        public const ushort SMSG_TUTORIAL_FLAGS = 0x0FD;
        public const ushort CMSG_TUTORIAL_FLAG = 0x0FE;
        public const ushort CMSG_TUTORIAL_CLEAR = 0x0FF;
        public const ushort CMSG_TUTORIAL_RESET = 0x100;
        public const ushort CMSG_STANDSTATECHANGE = 0x101;
        public const ushort CMSG_EMOTE = 0x102;
        public const ushort SMSG_EMOTE = 0x103;
        public const ushort CMSG_TEXT_EMOTE = 0x104;
        public const ushort SMSG_TEXT_EMOTE = 0x105;
        public const ushort CMSG_AUTOEQUIP_GROUND_ITEM = 0x106;
        public const ushort CMSG_AUTOSTORE_GROUND_ITEM = 0x107;
        public const ushort CMSG_AUTOSTORE_LOOT_ITEM = 0x108;
        public const ushort CMSG_STORE_LOOT_IN_SLOT = 0x109;
        public const ushort CMSG_AUTOEQUIP_ITEM = 0x10A;
        public const ushort CMSG_AUTOSTORE_BAG_ITEM = 0x10B;
        public const ushort CMSG_SWAP_ITEM = 0x10C;
        public const ushort CMSG_SWAP_INV_ITEM = 0x10D;
        public const ushort CMSG_SPLIT_ITEM = 0x10E;
        public const ushort CMSG_AUTOEQUIP_ITEM_SLOT = 0x10F;
        public const ushort CMSG_UNCLAIM_LICENSE = 0x110;
        public const ushort CMSG_DESTROYITEM = 0x111;
        public const ushort SMSG_INVENTORY_CHANGE_FAILURE = 0x112;
        public const ushort SMSG_OPEN_CONTAINER = 0x113;
        public const ushort CMSG_INSPECT = 0x114;
        public const ushort SMSG_INSPECT_RESULTS_UPDATE = 0x115;
        public const ushort CMSG_INITIATE_TRADE = 0x116;
        public const ushort CMSG_BEGIN_TRADE = 0x117;
        public const ushort CMSG_BUSY_TRADE = 0x118;
        public const ushort CMSG_IGNORE_TRADE = 0x119;
        public const ushort CMSG_ACCEPT_TRADE = 0x11A;
        public const ushort CMSG_UNACCEPT_TRADE = 0x11B;
        public const ushort CMSG_CANCEL_TRADE = 0x11C;
        public const ushort CMSG_SET_TRADE_ITEM = 0x11D;
        public const ushort CMSG_CLEAR_TRADE_ITEM = 0x11E;
        public const ushort CMSG_SET_TRADE_GOLD = 0x11F;
        public const ushort SMSG_TRADE_STATUS = 0x120;
        public const ushort SMSG_TRADE_STATUS_EXTENDED = 0x121;
        public const ushort SMSG_INITIALIZE_FACTIONS = 0x122;
        public const ushort SMSG_SET_FACTION_VISIBLE = 0x123;
        public const ushort SMSG_SET_FACTION_STANDING = 0x124;
        public const ushort CMSG_SET_FACTION_ATWAR = 0x125;
        public const ushort CMSG_SET_FACTION_CHEAT = 0x126;
        public const ushort SMSG_SET_PROFICIENCY = 0x127;
        public const ushort CMSG_SET_ACTION_BUTTON = 0x128;
        public const ushort SMSG_ACTION_BUTTONS = 0x129;
        public const ushort SMSG_INITIAL_SPELLS = 0x12A;
        public const ushort SMSG_LEARNED_SPELL = 0x12B;
        public const ushort SMSG_SUPERCEDED_SPELL = 0x12C;
        public const ushort CMSG_NEW_SPELL_SLOT = 0x12D;
        public const ushort CMSG_CAST_SPELL = 0x12E;
        public const ushort CMSG_CANCEL_CAST = 0x12F;
        public const ushort SMSG_CAST_FAILED = 0x130;
        public const ushort SMSG_SPELL_START = 0x131;
        public const ushort SMSG_SPELL_GO = 0x132;
        public const ushort SMSG_SPELL_FAILURE = 0x133;
        public const ushort SMSG_SPELL_COOLDOWN = 0x134;
        public const ushort SMSG_COOLDOWN_EVENT = 0x135;
        public const ushort CMSG_CANCEL_AURA = 0x136;
        public const ushort SMSG_EQUIPMENT_SET_SAVED = 0x137;
        public const ushort SMSG_PET_CAST_FAILED = 0x138;
        public const ushort MSG_CHANNEL_START = 0x139;
        public const ushort MSG_CHANNEL_UPDATE = 0x13A;
        public const ushort CMSG_CANCEL_CHANNELLING = 0x13B;
        public const ushort SMSG_AI_REACTION = 0x13C;
        public const ushort CMSG_SET_SELECTION = 0x13D;
        public const ushort CMSG_DELETEEQUIPMENT_SET = 0x13E;
        public const ushort CMSG_INSTANCE_LOCK_RESPONSE = 0x13F;
        public const ushort CMSG_DEBUG_PASSIVE_AURA = 0x140;
        public const ushort CMSG_ATTACKSWING = 0x141;
        public const ushort CMSG_ATTACKSTOP = 0x142;
        public const ushort SMSG_ATTACKSTART = 0x143;
        public const ushort SMSG_ATTACKSTOP = 0x144;
        public const ushort SMSG_ATTACKSWING_NOTINRANGE = 0x145;
        public const ushort SMSG_ATTACKSWING_BADFACING = 0x146;
        public const ushort SMSG_INSTANCE_LOCK_WARNING_QUERY = 0x147;
        public const ushort SMSG_ATTACKSWING_DEADTARGET = 0x148;
        public const ushort SMSG_ATTACKSWING_CANT_ATTACK = 0x149;
        public const ushort SMSG_ATTACKERSTATEUPDATE = 0x14A;
        public const ushort SMSG_BATTLEFIELD_PORT_DENIED = 0x14B;
        public const ushort CMSG_PERFORM_ACTION_SET = 0x14C;
        public const ushort SMSG_RESUME_CAST_BAR = 0x14D;
        public const ushort SMSG_CANCEL_COMBAT = 0x14E;
        public const ushort SMSG_SPELLBREAKLOG = 0x14F;
        public const ushort SMSG_SPELLHEALLOG = 0x150;
        public const ushort SMSG_SPELLENERGIZELOG = 0x151;
        public const ushort SMSG_BREAK_TARGET = 0x152;
        public const ushort CMSG_SAVE_PLAYER = 0x153;
        public const ushort CMSG_SETDEATHBINDPOINT = 0x154;
        public const ushort SMSG_BINDPOINTUPDATE = 0x155;
        public const ushort CMSG_GETDEATHBINDZONE = 0x156;
        public const ushort SMSG_BINDZONEREPLY = 0x157;
        public const ushort SMSG_PLAYERBOUND = 0x158;
        public const ushort SMSG_CLIENT_CONTROL_UPDATE = 0x159;
        public const ushort CMSG_REPOP_REQUEST = 0x15A;
        public const ushort SMSG_RESURRECT_REQUEST = 0x15B;
        public const ushort CMSG_RESURRECT_RESPONSE = 0x15C;
        public const ushort CMSG_LOOT = 0x15D;
        public const ushort CMSG_LOOT_MONEY = 0x15E;
        public const ushort CMSG_LOOT_RELEASE = 0x15F;
        public const ushort SMSG_LOOT_RESPONSE = 0x160;
        public const ushort SMSG_LOOT_RELEASE_RESPONSE = 0x161;
        public const ushort SMSG_LOOT_REMOVED = 0x162;
        public const ushort SMSG_LOOT_MONEY_NOTIFY = 0x163;
        public const ushort SMSG_LOOT_ITEM_NOTIFY = 0x164;
        public const ushort SMSG_LOOT_CLEAR_MONEY = 0x165;
        public const ushort SMSG_ITEM_PUSH_RESULT = 0x166;
        public const ushort SMSG_DUEL_REQUESTED = 0x167;
        public const ushort SMSG_DUEL_OUTOFBOUNDS = 0x168;
        public const ushort SMSG_DUEL_INBOUNDS = 0x169;
        public const ushort SMSG_DUEL_COMPLETE = 0x16A;
        public const ushort SMSG_DUEL_WINNER = 0x16B;
        public const ushort CMSG_DUEL_ACCEPTED = 0x16C;
        public const ushort CMSG_DUEL_CANCELLED = 0x16D;
        public const ushort SMSG_MOUNTRESULT = 0x16E;
        public const ushort SMSG_DISMOUNTRESULT = 0x16F;
        public const ushort SMSG_REMOVED_FROM_PVP_QUEUE = 0x170;
        public const ushort CMSG_MOUNTSPECIAL_ANIM = 0x171;
        public const ushort SMSG_MOUNTSPECIAL_ANIM = 0x172;
        public const ushort SMSG_PET_TAME_FAILURE = 0x173;
        public const ushort CMSG_PET_SET_ACTION = 0x174;
        public const ushort CMSG_PET_ACTION = 0x175;
        public const ushort CMSG_PET_ABANDON = 0x176;
        public const ushort CMSG_PET_RENAME = 0x177;
        public const ushort SMSG_PET_NAME_INVALID = 0x178;
        public const ushort SMSG_PET_SPELLS = 0x179;
        public const ushort SMSG_PET_MODE = 0x17A;
        public const ushort CMSG_GOSSIP_HELLO = 0x17B;
        public const ushort CMSG_GOSSIP_SELECT_OPTION = 0x17C;
        public const ushort SMSG_GOSSIP_MESSAGE = 0x17D;
        public const ushort SMSG_GOSSIP_COMPLETE = 0x17E;
        public const ushort CMSG_NPC_TEXT_QUERY = 0x17F;
        public const ushort SMSG_NPC_TEXT_UPDATE = 0x180;
        public const ushort SMSG_NPC_WONT_TALK = 0x181;
        public const ushort CMSG_QUESTGIVER_STATUS_QUERY = 0x182;
        public const ushort SMSG_QUESTGIVER_STATUS = 0x183;
        public const ushort CMSG_QUESTGIVER_HELLO = 0x184;
        public const ushort SMSG_QUESTGIVER_QUEST_LIST = 0x185;
        public const ushort CMSG_QUESTGIVER_QUERY_QUEST = 0x186;
        public const ushort CMSG_QUESTGIVER_QUEST_AUTOLAUNCH = 0x187;
        public const ushort SMSG_QUESTGIVER_QUEST_DETAILS = 0x188;
        public const ushort CMSG_QUESTGIVER_ACCEPT_QUEST = 0x189;
        public const ushort CMSG_QUESTGIVER_COMPLETE_QUEST = 0x18A;
        public const ushort SMSG_QUESTGIVER_REQUEST_ITEMS = 0x18B;
        public const ushort CMSG_QUESTGIVER_REQUEST_REWARD = 0x18C;
        public const ushort SMSG_QUESTGIVER_OFFER_REWARD = 0x18D;
        public const ushort CMSG_QUESTGIVER_CHOOSE_REWARD = 0x18E;
        public const ushort SMSG_QUESTGIVER_QUEST_INVALID = 0x18F;
        public const ushort CMSG_QUESTGIVER_CANCEL = 0x190;
        public const ushort SMSG_QUESTGIVER_QUEST_COMPLETE = 0x191;
        public const ushort SMSG_QUESTGIVER_QUEST_FAILED = 0x192;
        public const ushort CMSG_QUESTLOG_SWAP_QUEST = 0x193;
        public const ushort CMSG_QUESTLOG_REMOVE_QUEST = 0x194;
        public const ushort SMSG_QUESTLOG_FULL = 0x195;
        public const ushort SMSG_QUESTUPDATE_FAILED = 0x196;
        public const ushort SMSG_QUESTUPDATE_FAILEDTIMER = 0x197;
        public const ushort SMSG_QUESTUPDATE_COMPLETE = 0x198;
        public const ushort SMSG_QUESTUPDATE_ADD_KILL = 0x199;
        public const ushort SMSG_QUESTUPDATE_ADD_ITEM = 0x19A;
        public const ushort CMSG_QUEST_CONFIRM_ACCEPT = 0x19B;
        public const ushort SMSG_QUEST_CONFIRM_ACCEPT = 0x19C;
        public const ushort CMSG_PUSHQUESTTOPARTY = 0x19D;
        public const ushort CMSG_LIST_INVENTORY = 0x19E;
        public const ushort SMSG_LIST_INVENTORY = 0x19F;
        public const ushort CMSG_SELL_ITEM = 0x1A0;
        public const ushort SMSG_SELL_ITEM = 0x1A1;
        public const ushort CMSG_BUY_ITEM = 0x1A2;
        public const ushort CMSG_BUY_ITEM_IN_SLOT = 0x1A3;
        public const ushort SMSG_BUY_ITEM = 0x1A4;
        public const ushort SMSG_BUY_FAILED = 0x1A5;
        public const ushort CMSG_TAXICLEARALLNODES = 0x1A6;
        public const ushort CMSG_TAXIENABLEALLNODES = 0x1A7;
        public const ushort CMSG_TAXISHOWNODES = 0x1A8;
        public const ushort SMSG_SHOWTAXINODES = 0x1A9;
        public const ushort CMSG_TAXINODE_STATUS_QUERY = 0x1AA;
        public const ushort SMSG_TAXINODE_STATUS = 0x1AB;
        public const ushort CMSG_TAXIQUERYAVAILABLENODES = 0x1AC;
        public const ushort CMSG_ACTIVATETAXI = 0x1AD;
        public const ushort SMSG_ACTIVATETAXIREPLY = 0x1AE;
        public const ushort SMSG_NEW_TAXI_PATH = 0x1AF;
        public const ushort CMSG_TRAINER_LIST = 0x1B0;
        public const ushort SMSG_TRAINER_LIST = 0x1B1;
        public const ushort CMSG_TRAINER_BUY_SPELL = 0x1B2;
        public const ushort SMSG_TRAINER_BUY_SUCCEEDED = 0x1B3;
        public const ushort SMSG_TRAINER_BUY_FAILED = 0x1B4;
        public const ushort CMSG_BINDER_ACTIVATE = 0x1B5;
        public const ushort SMSG_PLAYERBINDERROR = 0x1B6;
        public const ushort CMSG_BANKER_ACTIVATE = 0x1B7;
        public const ushort SMSG_SHOW_BANK = 0x1B8;
        public const ushort CMSG_BUY_BANK_SLOT = 0x1B9;
        public const ushort SMSG_BUY_BANK_SLOT_RESULT = 0x1BA;
        public const ushort CMSG_PETITION_SHOWLIST = 0x1BB;
        public const ushort SMSG_PETITION_SHOWLIST = 0x1BC;
        public const ushort CMSG_PETITION_BUY = 0x1BD;
        public const ushort CMSG_PETITION_SHOW_SIGNATURES = 0x1BE;
        public const ushort SMSG_PETITION_SHOW_SIGNATURES = 0x1BF;
        public const ushort CMSG_PETITION_SIGN = 0x1C0;
        public const ushort SMSG_PETITION_SIGN_RESULTS = 0x1C1;
        public const ushort MSG_PETITION_DECLINE = 0x1C2;
        public const ushort CMSG_OFFER_PETITION = 0x1C3;
        public const ushort CMSG_TURN_IN_PETITION = 0x1C4;
        public const ushort SMSG_TURN_IN_PETITION_RESULTS = 0x1C5;
        public const ushort CMSG_PETITION_QUERY = 0x1C6;
        public const ushort SMSG_PETITION_QUERY_RESPONSE = 0x1C7;
        public const ushort SMSG_FISH_NOT_HOOKED = 0x1C8;
        public const ushort SMSG_FISH_ESCAPED = 0x1C9;
        public const ushort CMSG_BUG = 0x1CA;
        public const ushort SMSG_NOTIFICATION = 0x1CB;
        public const ushort CMSG_PLAYED_TIME = 0x1CC;
        public const ushort SMSG_PLAYED_TIME = 0x1CD;
        public const ushort CMSG_QUERY_TIME = 0x1CE;
        public const ushort SMSG_QUERY_TIME_RESPONSE = 0x1CF;
        public const ushort SMSG_LOG_XPGAIN = 0x1D0;
        public const ushort SMSG_AURACASTLOG = 0x1D1;
        public const ushort CMSG_RECLAIM_CORPSE = 0x1D2;
        public const ushort CMSG_WRAP_ITEM = 0x1D3;
        public const ushort SMSG_LEVELUP_INFO = 0x1D4;
        public const ushort MSG_MINIMAP_PING = 0x1D5;
        public const ushort SMSG_RESISTLOG = 0x1D6;
        public const ushort SMSG_ENCHANTMENTLOG = 0x1D7;
        public const ushort CMSG_SET_SKILL_CHEAT = 0x1D8;
        public const ushort SMSG_START_MIRROR_TIMER = 0x1D9;
        public const ushort SMSG_PAUSE_MIRROR_TIMER = 0x1DA;
        public const ushort SMSG_STOP_MIRROR_TIMER = 0x1DB;
        public const ushort CMSG_PING = 0x1DC;
        public const ushort SMSG_PONG = 0x1DD;
        public const ushort SMSG_CLEAR_COOLDOWN = 0x1DE;
        public const ushort SMSG_GAMEOBJECT_PAGETEXT = 0x1DF;
        public const ushort CMSG_SET_SHEATHED = 0x1E0;
        public const ushort SMSG_COOLDOWN_CHEAT = 0x1E1;
        public const ushort SMSG_SPELL_DELAYED = 0x1E2;
        public const ushort CMSG_QUEST_POI_QUERY = 0x1E3;
        public const ushort SMSG_QUEST_POI_QUERY_RESPONSE = 0x1E4;
        public const ushort CMSG_GHOST = 0x1E5;
        public const ushort CMSG_GM_INVIS = 0x1E6;
        public const ushort SMSG_INVALID_PROMOTION_CODE = 0x1E7;
        public const ushort MSG_GM_BIND_OTHER = 0x1E8;
        public const ushort MSG_GM_SUMMON = 0x1E9;
        public const ushort SMSG_ITEM_TIME_UPDATE = 0x1EA;
        public const ushort SMSG_ITEM_ENCHANT_TIME_UPDATE = 0x1EB;
        public const ushort SMSG_AUTH_CHALLENGE = 0x1EC;
        public const ushort CMSG_AUTH_SESSION = 0x1ED;
        public const ushort SMSG_AUTH_RESPONSE = 0x1EE;
        public const ushort MSG_GM_SHOWLABEL = 0x1EF;
        public const ushort CMSG_PET_CAST_SPELL = 0x1F0;
        public const ushort MSG_SAVE_GUILD_EMBLEM = 0x1F1;
        public const ushort MSG_TABARDVENDOR_ACTIVATE = 0x1F2;
        public const ushort SMSG_PLAY_SPELL_VISUAL = 0x1F3;
        public const ushort CMSG_ZONEUPDATE = 0x1F4;
        public const ushort SMSG_PARTYKILLLOG = 0x1F5;
        public const ushort SMSG_COMPRESSED_UPDATE_OBJECT = 0x1F6;
        public const ushort SMSG_PLAY_SPELL_IMPACT = 0x1F7;
        public const ushort SMSG_EXPLORATION_EXPERIENCE = 0x1F8;
        public const ushort CMSG_GM_SET_SECURITY_GROUP = 0x1F9;
        public const ushort CMSG_GM_NUKE = 0x1FA;
        public const ushort MSG_RANDOM_ROLL = 0x1FB;
        public const ushort SMSG_ENVIRONMENTAL_DAMAGE_LOG = 0x1FC;
        public const ushort CMSG_CHANGEPLAYER_DIFFICULTY = 0x1FD;
        public const ushort SMSG_RWHOIS = 0x1FE;
        public const ushort SMSG_LFG_PLAYER_REWARD = 0x1FF;
        public const ushort SMSG_LFG_TELEPORT_DENIED = 0x200;
        public const ushort CMSG_UNLEARN_SPELL = 0x201;
        public const ushort CMSG_UNLEARN_SKILL = 0x202;
        public const ushort SMSG_REMOVED_SPELL = 0x203;
        public const ushort CMSG_DECHARGE = 0x204;
        public const ushort CMSG_GMTICKET_CREATE = 0x205;
        public const ushort SMSG_GMTICKET_CREATE = 0x206;
        public const ushort CMSG_GMTICKET_UPDATETEXT = 0x207;
        public const ushort SMSG_GMTICKET_UPDATETEXT = 0x208;
        public const ushort SMSG_ACCOUNT_DATA_TIMES = 0x209;
        public const ushort CMSG_REQUEST_ACCOUNT_DATA = 0x20A;
        public const ushort CMSG_UPDATE_ACCOUNT_DATA = 0x20B;
        public const ushort SMSG_UPDATE_ACCOUNT_DATA = 0x20C;
        public const ushort SMSG_CLEAR_FAR_SIGHT_IMMEDIATE = 0x20D;
        public const ushort SMSG_CHANGEPLAYER_DIFFICULTY_RESULT = 0x20E;
        public const ushort CMSG_GM_TEACH = 0x20F;
        public const ushort CMSG_GM_CREATE_ITEM_TARGET = 0x210;
        public const ushort CMSG_GMTICKET_GETTICKET = 0x211;
        public const ushort SMSG_GMTICKET_GETTICKET = 0x212;
        public const ushort CMSG_UNLEARN_TALENTS = 0x213;
        public const ushort SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT = 0x214;
        public const ushort SMSG_GAMEOBJECT_DESPAWN_ANIM = 0x215;
        public const ushort MSG_CORPSE_QUERY = 0x216;
        public const ushort CMSG_GMTICKET_DELETETICKET = 0x217;
        public const ushort SMSG_GMTICKET_DELETETICKET = 0x218;
        public const ushort SMSG_CHAT_WRONG_FACTION = 0x219;
        public const ushort CMSG_GMTICKET_SYSTEMSTATUS = 0x21A;
        public const ushort SMSG_GMTICKET_SYSTEMSTATUS = 0x21B;
        public const ushort CMSG_SPIRIT_HEALER_ACTIVATE = 0x21C;
        public const ushort CMSG_SET_STAT_CHEAT = 0x21D;
        public const ushort SMSG_QUEST_FORCE_REMOVE = 0x21E;
        public const ushort CMSG_SKILL_BUY_STEP = 0x21F;
        public const ushort CMSG_SKILL_BUY_RANK = 0x220;
        public const ushort CMSG_XP_CHEAT = 0x221;
        public const ushort SMSG_SPIRIT_HEALER_CONFIRM = 0x222;
        public const ushort CMSG_CHARACTER_POINT_CHEAT = 0x223;
        public const ushort SMSG_GOSSIP_POI = 0x224;
        public const ushort CMSG_CHAT_IGNORED = 0x225;
        public const ushort CMSG_GM_VISION = 0x226;
        public const ushort CMSG_SERVER_COMMAND = 0x227;
        public const ushort CMSG_GM_SILENCE = 0x228;
        public const ushort CMSG_GM_REVEALTO = 0x229;
        public const ushort CMSG_GM_RESURRECT = 0x22A;
        public const ushort CMSG_GM_SUMMONMOB = 0x22B;
        public const ushort CMSG_GM_MOVECORPSE = 0x22C;
        public const ushort CMSG_GM_FREEZE = 0x22D;
        public const ushort CMSG_GM_UBERINVIS = 0x22E;
        public const ushort CMSG_GM_REQUEST_PLAYER_INFO = 0x22F;
        public const ushort SMSG_GM_PLAYER_INFO = 0x230;
        public const ushort CMSG_GUILD_RANK = 0x231;
        public const ushort CMSG_GUILD_ADD_RANK = 0x232;
        public const ushort CMSG_GUILD_DEL_RANK = 0x233;
        public const ushort CMSG_GUILD_SET_PUBLIC_NOTE = 0x234;
        public const ushort CMSG_GUILD_SET_OFFICER_NOTE = 0x235;
        public const ushort SMSG_LOGIN_VERIFY_WORLD = 0x236;
        public const ushort CMSG_CLEAR_EXPLORATION = 0x237;
        public const ushort CMSG_SEND_MAIL = 0x238;
        public const ushort SMSG_SEND_MAIL_RESULT = 0x239;
        public const ushort CMSG_GET_MAIL_LIST = 0x23A;
        public const ushort SMSG_MAIL_LIST_RESULT = 0x23B;
        public const ushort CMSG_BATTLEFIELD_LIST = 0x23C;
        public const ushort SMSG_BATTLEFIELD_LIST = 0x23D;
        public const ushort CMSG_BATTLEFIELD_JOIN = 0x23E;
        public const ushort SMSG_FORCE_SET_VEHICLE_REC_ID = 0x23F;
        public const ushort CMSG_SET_VEHICLE_REC_ID_ACK = 0x240;
        public const ushort CMSG_TAXICLEARNODE = 0x241;
        public const ushort CMSG_TAXIENABLENODE = 0x242;
        public const ushort CMSG_ITEM_TEXT_QUERY = 0x243;
        public const ushort SMSG_ITEM_TEXT_QUERY_RESPONSE = 0x244;
        public const ushort CMSG_MAIL_TAKE_MONEY = 0x245;
        public const ushort CMSG_MAIL_TAKE_ITEM = 0x246;
        public const ushort CMSG_MAIL_MARK_AS_READ = 0x247;
        public const ushort CMSG_MAIL_RETURN_TO_SENDER = 0x248;
        public const ushort CMSG_MAIL_DELETE = 0x249;
        public const ushort CMSG_MAIL_CREATE_TEXT_ITEM = 0x24A;
        public const ushort SMSG_SPELLLOGMISS = 0x24B;
        public const ushort SMSG_SPELLLOGEXECUTE = 0x24C;
        public const ushort SMSG_DEBUGAURAPROC = 0x24D;
        public const ushort SMSG_PERIODICAURALOG = 0x24E;
        public const ushort SMSG_SPELLDAMAGESHIELD = 0x24F;
        public const ushort SMSG_SPELLNONMELEEDAMAGELOG = 0x250;
        public const ushort CMSG_LEARN_TALENT = 0x251;
        public const ushort SMSG_RESURRECT_FAILED = 0x252;
        public const ushort CMSG_TOGGLE_PVP = 0x253;
        public const ushort SMSG_ZONE_UNDER_ATTACK = 0x254;
        public const ushort MSG_AUCTION_HELLO = 0x255;
        public const ushort CMSG_AUCTION_SELL_ITEM = 0x256;
        public const ushort CMSG_AUCTION_REMOVE_ITEM = 0x257;
        public const ushort CMSG_AUCTION_LIST_ITEMS = 0x258;
        public const ushort CMSG_AUCTION_LIST_OWNER_ITEMS = 0x259;
        public const ushort CMSG_AUCTION_PLACE_BID = 0x25A;
        public const ushort SMSG_AUCTION_COMMAND_RESULT = 0x25B;
        public const ushort SMSG_AUCTION_LIST_RESULT = 0x25C;
        public const ushort SMSG_AUCTION_OWNER_LIST_RESULT = 0x25D;
        public const ushort SMSG_AUCTION_BIDDER_NOTIFICATION = 0x25E;
        public const ushort SMSG_AUCTION_OWNER_NOTIFICATION = 0x25F;
        public const ushort SMSG_PROCRESIST = 0x260;
        public const ushort SMSG_COMBAT_EVENT_FAILED = 0x261;
        public const ushort SMSG_DISPEL_FAILED = 0x262;
        public const ushort SMSG_SPELLORDAMAGE_IMMUNE = 0x263;
        public const ushort CMSG_AUCTION_LIST_BIDDER_ITEMS = 0x264;
        public const ushort SMSG_AUCTION_BIDDER_LIST_RESULT = 0x265;
        public const ushort SMSG_SET_FLAT_SPELL_MODIFIER = 0x266;
        public const ushort SMSG_SET_PCT_SPELL_MODIFIER = 0x267;
        public const ushort CMSG_SET_AMMO = 0x268;
        public const ushort SMSG_CORPSE_RECLAIM_DELAY = 0x269;
        public const ushort CMSG_SET_ACTIVE_MOVER = 0x26A;
        public const ushort CMSG_PET_CANCEL_AURA = 0x26B;
        public const ushort CMSG_PLAYER_AI_CHEAT = 0x26C;
        public const ushort CMSG_CANCEL_AUTO_REPEAT_SPELL = 0x26D;
        public const ushort MSG_GM_ACCOUNT_ONLINE = 0x26E;
        public const ushort MSG_LIST_STABLED_PETS = 0x26F;
        public const ushort CMSG_STABLE_PET = 0x270;
        public const ushort CMSG_UNSTABLE_PET = 0x271;
        public const ushort CMSG_BUY_STABLE_SLOT = 0x272;
        public const ushort SMSG_STABLE_RESULT = 0x273;
        public const ushort CMSG_STABLE_REVIVE_PET = 0x274;
        public const ushort CMSG_STABLE_SWAP_PET = 0x275;
        public const ushort MSG_QUEST_PUSH_RESULT = 0x276;
        public const ushort SMSG_PLAY_MUSIC = 0x277;
        public const ushort SMSG_PLAY_OBJECT_SOUND = 0x278;
        public const ushort CMSG_REQUEST_PET_INFO = 0x279;
        public const ushort CMSG_FAR_SIGHT = 0x27A;
        public const ushort SMSG_SPELLDISPELLOG = 0x27B;
        public const ushort SMSG_DAMAGE_CALC_LOG = 0x27C;
        public const ushort CMSG_ENABLE_DAMAGE_LOG = 0x27D;
        public const ushort CMSG_GROUP_CHANGE_SUB_GROUP = 0x27E;
        public const ushort CMSG_REQUEST_PARTY_MEMBER_STATS = 0x27F;
        public const ushort CMSG_GROUP_SWAP_SUB_GROUP = 0x280;
        public const ushort CMSG_RESET_FACTION_CHEAT = 0x281;
        public const ushort CMSG_AUTOSTORE_BANK_ITEM = 0x282;
        public const ushort CMSG_AUTOBANK_ITEM = 0x283;
        public const ushort MSG_QUERY_NEXT_MAIL_TIME = 0x284;
        public const ushort SMSG_RECEIVED_MAIL = 0x285;
        public const ushort SMSG_RAID_GROUP_ONLY = 0x286;
        public const ushort CMSG_SET_DURABILITY_CHEAT = 0x287;
        public const ushort CMSG_SET_PVP_RANK_CHEAT = 0x288;
        public const ushort CMSG_ADD_PVP_MEDAL_CHEAT = 0x289;
        public const ushort CMSG_DEL_PVP_MEDAL_CHEAT = 0x28A;
        public const ushort CMSG_SET_PVP_TITLE = 0x28B;
        public const ushort SMSG_PVP_CREDIT = 0x28C;
        public const ushort SMSG_AUCTION_REMOVED_NOTIFICATION = 0x28D;
        public const ushort CMSG_GROUP_RAID_CONVERT = 0x28E;
        public const ushort CMSG_GROUP_ASSISTANT_LEADER = 0x28F;
        public const ushort CMSG_BUYBACK_ITEM = 0x290;
        public const ushort SMSG_CHAT_SERVER_MESSAGE = 0x291;
        public const ushort CMSG_SET_SAVED_INSTANCE_EXTEND = 0x292;
        public const ushort SMSG_LFG_OFFER_CONTINUE = 0x293;
        public const ushort CMSG_TEST_DROP_RATE = 0x294;
        public const ushort SMSG_TEST_DROP_RATE_RESULT = 0x295;
        public const ushort CMSG_LFG_GET_STATUS = 0x296;
        public const ushort SMSG_SHOW_MAILBOX = 0x297;
        public const ushort SMSG_RESET_RANGED_COMBAT_TIMER = 0x298;
        public const ushort SMSG_CHAT_NOT_IN_PARTY = 0x299;
        public const ushort CMSG_GMTICKETSYSTEM_TOGGLE = 0x29A;
        public const ushort CMSG_CANCEL_GROWTH_AURA = 0x29B;
        public const ushort SMSG_CANCEL_AUTO_REPEAT = 0x29C;
        public const ushort SMSG_STANDSTATE_UPDATE = 0x29D;
        public const ushort SMSG_LOOT_ALL_PASSED = 0x29E;
        public const ushort SMSG_LOOT_ROLL_WON = 0x29F;
        public const ushort CMSG_LOOT_ROLL = 0x2A0;
        public const ushort SMSG_LOOT_START_ROLL = 0x2A1;
        public const ushort SMSG_LOOT_ROLL = 0x2A2;
        public const ushort CMSG_LOOT_MASTER_GIVE = 0x2A3;
        public const ushort SMSG_LOOT_MASTER_LIST = 0x2A4;
        public const ushort SMSG_SET_FORCED_REACTIONS = 0x2A5;
        public const ushort SMSG_SPELL_FAILED_OTHER = 0x2A6;
        public const ushort SMSG_GAMEOBJECT_RESET_STATE = 0x2A7;
        public const ushort CMSG_REPAIR_ITEM = 0x2A8;
        public const ushort SMSG_CHAT_PLAYER_NOT_FOUND = 0x2A9;
        public const ushort MSG_TALENT_WIPE_CONFIRM = 0x2AA;
        public const ushort SMSG_SUMMON_REQUEST = 0x2AB;
        public const ushort CMSG_SUMMON_RESPONSE = 0x2AC;
        public const ushort MSG_DEV_SHOWLABEL = 0x2AD;
        public const ushort SMSG_MONSTER_MOVE_TRANSPORT = 0x2AE;
        public const ushort SMSG_PET_BROKEN = 0x2AF;
        public const ushort MSG_MOVE_FEATHER_FALL = 0x2B0;
        public const ushort MSG_MOVE_WATER_WALK = 0x2B1;
        public const ushort CMSG_SERVER_BROADCAST = 0x2B2;
        public const ushort CMSG_SELF_RES = 0x2B3;
        public const ushort SMSG_FEIGN_DEATH_RESISTED = 0x2B4;
        public const ushort CMSG_RUN_SCRIPT = 0x2B5;
        public const ushort SMSG_SCRIPT_MESSAGE = 0x2B6;
        public const ushort SMSG_DUEL_COUNTDOWN = 0x2B7;
        public const ushort SMSG_AREA_TRIGGER_MESSAGE = 0x2B8;
        public const ushort CMSG_SHOWING_HELM = 0x2B9;
        public const ushort CMSG_SHOWING_CLOAK = 0x2BA;
        public const ushort SMSG_LFG_ROLE_CHOSEN = 0x2BB;
        public const ushort SMSG_PLAYER_SKINNED = 0x2BC;
        public const ushort SMSG_DURABILITY_DAMAGE_DEATH = 0x2BD;
        public const ushort CMSG_SET_EXPLORATION = 0x2BE;
        public const ushort CMSG_SET_ACTIONBAR_TOGGLES = 0x2BF;
        public const ushort UMSG_DELETE_GUILD_CHARTER = 0x2C0;
        public const ushort MSG_PETITION_RENAME = 0x2C1;
        public const ushort SMSG_INIT_WORLD_STATES = 0x2C2;
        public const ushort SMSG_UPDATE_WORLD_STATE = 0x2C3;
        public const ushort CMSG_ITEM_NAME_QUERY = 0x2C4;
        public const ushort SMSG_ITEM_NAME_QUERY_RESPONSE = 0x2C5;
        public const ushort SMSG_PET_ACTION_FEEDBACK = 0x2C6;
        public const ushort CMSG_CHAR_RENAME = 0x2C7;
        public const ushort SMSG_CHAR_RENAME = 0x2C8;
        public const ushort CMSG_MOVE_SPLINE_DONE = 0x2C9;
        public const ushort CMSG_MOVE_FALL_RESET = 0x2CA;
        public const ushort SMSG_INSTANCE_SAVE_CREATED = 0x2CB;
        public const ushort SMSG_RAID_INSTANCE_INFO = 0x2CC;
        public const ushort CMSG_REQUEST_RAID_INFO = 0x2CD;
        public const ushort CMSG_MOVE_TIME_SKIPPED = 0x2CE;
        public const ushort CMSG_MOVE_FEATHER_FALL_ACK = 0x2CF;
        public const ushort CMSG_MOVE_WATER_WALK_ACK = 0x2D0;
        public const ushort CMSG_MOVE_NOT_ACTIVE_MOVER = 0x2D1;
        public const ushort SMSG_PLAY_SOUND = 0x2D2;
        public const ushort CMSG_BATTLEFIELD_STATUS = 0x2D3;
        public const ushort SMSG_BATTLEFIELD_STATUS = 0x2D4;
        public const ushort CMSG_BATTLEFIELD_PORT = 0x2D5;
        public const ushort MSG_INSPECT_HONOR_STATS = 0x2D6;
        public const ushort CMSG_BATTLEMASTER_HELLO = 0x2D7;
        public const ushort CMSG_MOVE_START_SWIM_CHEAT = 0x2D8;
        public const ushort CMSG_MOVE_STOP_SWIM_CHEAT = 0x2D9;
        public const ushort SMSG_FORCE_WALK_SPEED_CHANGE = 0x2DA;
        public const ushort CMSG_FORCE_WALK_SPEED_CHANGE_ACK = 0x2DB;
        public const ushort SMSG_FORCE_SWIM_BACK_SPEED_CHANGE = 0x2DC;
        public const ushort CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK = 0x2DD;
        public const ushort SMSG_FORCE_TURN_RATE_CHANGE = 0x2DE;
        public const ushort CMSG_FORCE_TURN_RATE_CHANGE_ACK = 0x2DF;
        public const ushort MSG_PVP_LOG_DATA = 0x2E0;
        public const ushort CMSG_LEAVE_BATTLEFIELD = 0x2E1;
        public const ushort CMSG_AREA_SPIRIT_HEALER_QUERY = 0x2E2;
        public const ushort CMSG_AREA_SPIRIT_HEALER_QUEUE = 0x2E3;
        public const ushort SMSG_AREA_SPIRIT_HEALER_TIME = 0x2E4;
        public const ushort CMSG_GM_UNTEACH = 0x2E5;
        public const ushort SMSG_WARDEN_DATA = 0x2E6;
        public const ushort CMSG_WARDEN_DATA = 0x2E7;
        public const ushort SMSG_GROUP_JOINED_BATTLEGROUND = 0x2E8;
        public const ushort MSG_BATTLEGROUND_PLAYER_POSITIONS = 0x2E9;
        public const ushort CMSG_PET_STOP_ATTACK = 0x2EA;
        public const ushort SMSG_BINDER_CONFIRM = 0x2EB;
        public const ushort SMSG_BATTLEGROUND_PLAYER_JOINED = 0x2EC;
        public const ushort SMSG_BATTLEGROUND_PLAYER_LEFT = 0x2ED;
        public const ushort CMSG_BATTLEMASTER_JOIN = 0x2EE;
        public const ushort SMSG_ADDON_INFO = 0x2EF;
        public const ushort CMSG_PET_UNLEARN = 0x2F0;
        public const ushort SMSG_PET_UNLEARN_CONFIRM = 0x2F1;
        public const ushort SMSG_PARTY_MEMBER_STATS_FULL = 0x2F2;
        public const ushort CMSG_PET_SPELL_AUTOCAST = 0x2F3;
        public const ushort SMSG_WEATHER = 0x2F4;
        public const ushort SMSG_PLAY_TIME_WARNING = 0x2F5;
        public const ushort SMSG_MINIGAME_SETUP = 0x2F6;
        public const ushort SMSG_MINIGAME_STATE = 0x2F7;
        public const ushort CMSG_MINIGAME_MOVE = 0x2F8;
        public const ushort SMSG_MINIGAME_MOVE_FAILED = 0x2F9;
        public const ushort SMSG_RAID_INSTANCE_MESSAGE = 0x2FA;
        public const ushort SMSG_COMPRESSED_MOVES = 0x2FB;
        public const ushort CMSG_GUILD_INFO_TEXT = 0x2FC;
        public const ushort SMSG_CHAT_RESTRICTED = 0x2FD;
        public const ushort SMSG_SPLINE_SET_RUN_SPEED = 0x2FE;
        public const ushort SMSG_SPLINE_SET_RUN_BACK_SPEED = 0x2FF;
        public const ushort SMSG_SPLINE_SET_SWIM_SPEED = 0x300;
        public const ushort SMSG_SPLINE_SET_WALK_SPEED = 0x301;
        public const ushort SMSG_SPLINE_SET_SWIM_BACK_SPEED = 0x302;
        public const ushort SMSG_SPLINE_SET_TURN_RATE = 0x303;
        public const ushort SMSG_SPLINE_MOVE_UNROOT = 0x304;
        public const ushort SMSG_SPLINE_MOVE_FEATHER_FALL = 0x305;
        public const ushort SMSG_SPLINE_MOVE_NORMAL_FALL = 0x306;
        public const ushort SMSG_SPLINE_MOVE_SET_HOVER = 0x307;
        public const ushort SMSG_SPLINE_MOVE_UNSET_HOVER = 0x308;
        public const ushort SMSG_SPLINE_MOVE_WATER_WALK = 0x309;
        public const ushort SMSG_SPLINE_MOVE_LAND_WALK = 0x30A;
        public const ushort SMSG_SPLINE_MOVE_START_SWIM = 0x30B;
        public const ushort SMSG_SPLINE_MOVE_STOP_SWIM = 0x30C;
        public const ushort SMSG_SPLINE_MOVE_SET_RUN_MODE = 0x30D;
        public const ushort SMSG_SPLINE_MOVE_SET_WALK_MODE = 0x30E;
        public const ushort CMSG_GM_NUKE_ACCOUNT = 0x30F;
        public const ushort MSG_GM_DESTROY_CORPSE = 0x310;
        public const ushort CMSG_GM_DESTROY_ONLINE_CORPSE = 0x311;
        public const ushort CMSG_ACTIVATETAXIEXPRESS = 0x312;
        public const ushort SMSG_SET_FACTION_ATWAR = 0x313;
        public const ushort SMSG_GAMETIMEBIAS_SET = 0x314;
        public const ushort CMSG_DEBUG_ACTIONS_START = 0x315;
        public const ushort CMSG_DEBUG_ACTIONS_STOP = 0x316;
        public const ushort CMSG_SET_FACTION_INACTIVE = 0x317;
        public const ushort CMSG_SET_WATCHED_FACTION = 0x318;
        public const ushort MSG_MOVE_TIME_SKIPPED = 0x319;
        public const ushort SMSG_SPLINE_MOVE_ROOT = 0x31A;
        public const ushort CMSG_SET_EXPLORATION_ALL = 0x31B;
        public const ushort SMSG_INVALIDATE_PLAYER = 0x31C;
        public const ushort CMSG_RESET_INSTANCES = 0x31D;
        public const ushort SMSG_INSTANCE_RESET = 0x31E;
        public const ushort SMSG_INSTANCE_RESET_FAILED = 0x31F;
        public const ushort SMSG_UPDATE_LAST_INSTANCE = 0x320;
        public const ushort MSG_RAID_TARGET_UPDATE = 0x321;
        public const ushort MSG_RAID_READY_CHECK = 0x322;
        public const ushort CMSG_LUA_USAGE = 0x323;
        public const ushort SMSG_PET_ACTION_SOUND = 0x324;
        public const ushort SMSG_PET_DISMISS_SOUND = 0x325;
        public const ushort SMSG_GHOSTEE_GONE = 0x326;
        public const ushort CMSG_GM_UPDATE_TICKET_STATUS = 0x327;
        public const ushort SMSG_GM_TICKET_STATUS_UPDATE = 0x328;
        public const ushort MSG_SET_DUNGEON_DIFFICULTY = 0x329;
        public const ushort CMSG_GMSURVEY_SUBMIT = 0x32A;
        public const ushort SMSG_UPDATE_INSTANCE_OWNERSHIP = 0x32B;
        public const ushort CMSG_IGNORE_KNOCKBACK_CHEAT = 0x32C;
        public const ushort SMSG_CHAT_PLAYER_AMBIGUOUS = 0x32D;
        public const ushort MSG_DELAY_GHOST_TELEPORT = 0x32E;
        public const ushort SMSG_SPELLINSTAKILLLOG = 0x32F;
        public const ushort SMSG_SPELL_UPDATE_CHAIN_TARGETS = 0x330;
        public const ushort CMSG_CHAT_FILTERED = 0x331;
        public const ushort SMSG_EXPECTED_SPAM_RECORDS = 0x332;
        public const ushort SMSG_SPELLSTEALLOG = 0x333;
        public const ushort CMSG_LOTTERY_QUERY_OBSOLETE = 0x334;
        public const ushort SMSG_LOTTERY_QUERY_RESULT_OBSOLETE = 0x335;
        public const ushort CMSG_BUY_LOTTERY_TICKET_OBSOLETE = 0x336;
        public const ushort SMSG_LOTTERY_RESULT_OBSOLETE = 0x337;
        public const ushort SMSG_CHARACTER_PROFILE = 0x338;
        public const ushort SMSG_CHARACTER_PROFILE_REALM_CONNECTED = 0x339;
        public const ushort SMSG_DEFENSE_MESSAGE = 0x33A;
        public const ushort SMSG_INSTANCE_DIFFICULTY = 0x33B;
        public const ushort MSG_GM_RESETINSTANCELIMIT = 0x33C;
        public const ushort SMSG_MOTD = 0x33D;
        public const ushort SMSG_MOVE_SET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY = 0x33E;
        public const ushort SMSG_MOVE_UNSET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY = 0x33F;
        public const ushort CMSG_MOVE_SET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY_ACK = 0x340;
        public const ushort MSG_MOVE_START_SWIM_CHEAT = 0x341;
        public const ushort MSG_MOVE_STOP_SWIM_CHEAT = 0x342;
        public const ushort SMSG_MOVE_SET_CAN_FLY = 0x343;
        public const ushort SMSG_MOVE_UNSET_CAN_FLY = 0x344;
        public const ushort CMSG_MOVE_SET_CAN_FLY_ACK = 0x345;
        public const ushort CMSG_MOVE_SET_FLY = 0x346;
        public const ushort CMSG_SOCKET_GEMS = 0x347;
        public const ushort CMSG_ARENA_TEAM_CREATE = 0x348;
        public const ushort SMSG_ARENA_TEAM_COMMAND_RESULT = 0x349;
        public const ushort MSG_MOVE_UPDATE_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY = 0x34A;
        public const ushort CMSG_ARENA_TEAM_QUERY = 0x34B;
        public const ushort SMSG_ARENA_TEAM_QUERY_RESPONSE = 0x34C;
        public const ushort CMSG_ARENA_TEAM_ROSTER = 0x34D;
        public const ushort SMSG_ARENA_TEAM_ROSTER = 0x34E;
        public const ushort CMSG_ARENA_TEAM_INVITE = 0x34F;
        public const ushort SMSG_ARENA_TEAM_INVITE = 0x350;
        public const ushort CMSG_ARENA_TEAM_ACCEPT = 0x351;
        public const ushort CMSG_ARENA_TEAM_DECLINE = 0x352;
        public const ushort CMSG_ARENA_TEAM_LEAVE = 0x353;
        public const ushort CMSG_ARENA_TEAM_REMOVE = 0x354;
        public const ushort CMSG_ARENA_TEAM_DISBAND = 0x355;
        public const ushort CMSG_ARENA_TEAM_LEADER = 0x356;
        public const ushort SMSG_ARENA_TEAM_EVENT = 0x357;
        public const ushort CMSG_BATTLEMASTER_JOIN_ARENA = 0x358;
        public const ushort MSG_MOVE_START_ASCEND = 0x359;
        public const ushort MSG_MOVE_STOP_ASCEND = 0x35A;
        public const ushort SMSG_ARENA_TEAM_STATS = 0x35B;
        public const ushort CMSG_LFG_JOIN = 0x35C;
        public const ushort CMSG_LFG_LEAVE = 0x35D;
        public const ushort CMSG_SEARCH_LFG_JOIN = 0x35E;
        public const ushort CMSG_SEARCH_LFG_LEAVE = 0x35F;
        public const ushort SMSG_UPDATE_LFG_LIST = 0x360;
        public const ushort SMSG_LFG_PROPOSAL_UPDATE = 0x361;
        public const ushort CMSG_LFG_PROPOSAL_RESULT = 0x362;
        public const ushort SMSG_LFG_ROLE_CHECK_UPDATE = 0x363;
        public const ushort SMSG_LFG_JOIN_RESULT = 0x364;
        public const ushort SMSG_LFG_QUEUE_STATUS = 0x365;
        public const ushort CMSG_SET_LFG_COMMENT = 0x366;
        public const ushort SMSG_LFG_UPDATE_PLAYER = 0x367;
        public const ushort SMSG_LFG_UPDATE_PARTY = 0x368;
        public const ushort SMSG_LFG_UPDATE_SEARCH = 0x369;
        public const ushort CMSG_LFG_SET_ROLES = 0x36A;
        public const ushort CMSG_LFG_SET_NEEDS = 0x36B;
        public const ushort CMSG_LFG_SET_BOOT_VOTE = 0x36C;
        public const ushort SMSG_LFG_BOOT_PROPOSAL_UPDATE = 0x36D;
        public const ushort CMSG_LFD_PLAYER_LOCK_INFO_REQUEST = 0x36E;
        public const ushort SMSG_LFG_PLAYER_INFO = 0x36F;
        public const ushort CMSG_LFG_TELEPORT = 0x370;
        public const ushort CMSG_LFD_PARTY_LOCK_INFO_REQUEST = 0x371;
        public const ushort SMSG_LFG_PARTY_INFO = 0x372;
        public const ushort SMSG_TITLE_EARNED = 0x373;
        public const ushort CMSG_SET_TITLE = 0x374;
        public const ushort CMSG_CANCEL_MOUNT_AURA = 0x375;
        public const ushort SMSG_ARENA_ERROR = 0x376;
        public const ushort MSG_INSPECT_ARENA_TEAMS = 0x377;
        public const ushort SMSG_DEATH_RELEASE_LOC = 0x378;
        public const ushort CMSG_CANCEL_TEMP_ENCHANTMENT = 0x379;
        public const ushort SMSG_FORCED_DEATH_UPDATE = 0x37A;
        public const ushort CMSG_CHEAT_SET_HONOR_CURRENCY = 0x37B;
        public const ushort CMSG_CHEAT_SET_ARENA_CURRENCY = 0x37C;
        public const ushort MSG_MOVE_SET_FLIGHT_SPEED_CHEAT = 0x37D;
        public const ushort MSG_MOVE_SET_FLIGHT_SPEED = 0x37E;
        public const ushort MSG_MOVE_SET_FLIGHT_BACK_SPEED_CHEAT = 0x37F;
        public const ushort MSG_MOVE_SET_FLIGHT_BACK_SPEED = 0x380;
        public const ushort SMSG_FORCE_FLIGHT_SPEED_CHANGE = 0x381;
        public const ushort CMSG_FORCE_FLIGHT_SPEED_CHANGE_ACK = 0x382;
        public const ushort SMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE = 0x383;
        public const ushort CMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK = 0x384;
        public const ushort SMSG_SPLINE_SET_FLIGHT_SPEED = 0x385;
        public const ushort SMSG_SPLINE_SET_FLIGHT_BACK_SPEED = 0x386;
        public const ushort CMSG_MAELSTROM_INVALIDATE_CACHE = 0x387;
        public const ushort SMSG_FLIGHT_SPLINE_SYNC = 0x388;
        public const ushort CMSG_SET_TAXI_BENCHMARK_MODE = 0x389;
        public const ushort SMSG_JOINED_BATTLEGROUND_QUEUE = 0x38A;
        public const ushort SMSG_REALM_SPLIT = 0x38B;
        public const ushort CMSG_REALM_SPLIT = 0x38C;
        public const ushort CMSG_MOVE_CHNG_TRANSPORT = 0x38D;
        public const ushort MSG_PARTY_ASSIGNMENT = 0x38E;
        public const ushort SMSG_OFFER_PETITION_ERROR = 0x38F;
        public const ushort SMSG_TIME_SYNC_REQ = 0x390;
        public const ushort CMSG_TIME_SYNC_RESP = 0x391;
        public const ushort CMSG_SEND_LOCAL_EVENT = 0x392;
        public const ushort CMSG_SEND_GENERAL_TRIGGER = 0x393;
        public const ushort CMSG_SEND_COMBAT_TRIGGER = 0x394;
        public const ushort CMSG_MAELSTROM_GM_SENT_MAIL = 0x395;
        public const ushort SMSG_RESET_FAILED_NOTIFY = 0x396;
        public const ushort SMSG_REAL_GROUP_UPDATE = 0x397;
        public const ushort SMSG_LFG_DISABLED = 0x398;
        public const ushort CMSG_ACTIVE_PVP_CHEAT = 0x399;
        public const ushort CMSG_CHEAT_DUMP_ITEMS_DEBUG_ONLY = 0x39A;
        public const ushort SMSG_CHEAT_DUMP_ITEMS_DEBUG_ONLY_RESPONSE = 0x39B;
        public const ushort SMSG_CHEAT_DUMP_ITEMS_DEBUG_ONLY_RESPONSE_WRITE_FILE = 0x39C;
        public const ushort SMSG_UPDATE_COMBO_POINTS = 0x39D;
        public const ushort SMSG_VOICE_SESSION_ROSTER_UPDATE = 0x39E;
        public const ushort SMSG_VOICE_SESSION_LEAVE = 0x39F;
        public const ushort SMSG_VOICE_SESSION_ADJUST_PRIORITY = 0x3A0;
        public const ushort CMSG_VOICE_SET_TALKER_MUTED_REQUEST = 0x3A1;
        public const ushort SMSG_VOICE_SET_TALKER_MUTED = 0x3A2;
        public const ushort SMSG_INIT_EXTRA_AURA_INFO_OBSOLETE = 0x3A3;
        public const ushort SMSG_SET_EXTRA_AURA_INFO_OBSOLETE = 0x3A4;
        public const ushort SMSG_SET_EXTRA_AURA_INFO_NEED_UPDATE_OBSOLETE = 0x3A5;
        public const ushort SMSG_CLEAR_EXTRA_AURA_INFO_OBSOLETE = 0x3A6;
        public const ushort MSG_MOVE_START_DESCEND = 0x3A7;
        public const ushort CMSG_IGNORE_REQUIREMENTS_CHEAT = 0x3A8;
        public const ushort SMSG_IGNORE_REQUIREMENTS_CHEAT = 0x3A9;
        public const ushort SMSG_SPELL_CHANCE_PROC_LOG = 0x3AA;
        public const ushort CMSG_MOVE_SET_RUN_SPEED = 0x3AB;
        public const ushort SMSG_DISMOUNT = 0x3AC;
        public const ushort MSG_MOVE_UPDATE_CAN_FLY = 0x3AD;
        public const ushort MSG_RAID_READY_CHECK_CONFIRM = 0x3AE;
        public const ushort CMSG_VOICE_SESSION_ENABLE = 0x3AF;
        public const ushort SMSG_VOICE_SESSION_ENABLE = 0x3B0;
        public const ushort SMSG_VOICE_PARENTAL_CONTROLS = 0x3B1;
        public const ushort CMSG_GM_WHISPER = 0x3B2;
        public const ushort SMSG_GM_MESSAGECHAT = 0x3B3;
        public const ushort MSG_GM_GEARRATING = 0x3B4;
        public const ushort CMSG_COMMENTATOR_ENABLE = 0x3B5;
        public const ushort SMSG_COMMENTATOR_STATE_CHANGED = 0x3B6;
        public const ushort CMSG_COMMENTATOR_GET_MAP_INFO = 0x3B7;
        public const ushort SMSG_COMMENTATOR_MAP_INFO = 0x3B8;
        public const ushort CMSG_COMMENTATOR_GET_PLAYER_INFO = 0x3B9;
        public const ushort SMSG_COMMENTATOR_GET_PLAYER_INFO = 0x3BA;
        public const ushort SMSG_COMMENTATOR_PLAYER_INFO = 0x3BB;
        public const ushort CMSG_COMMENTATOR_ENTER_INSTANCE = 0x3BC;
        public const ushort CMSG_COMMENTATOR_EXIT_INSTANCE = 0x3BD;
        public const ushort CMSG_COMMENTATOR_INSTANCE_COMMAND = 0x3BE;
        public const ushort SMSG_CLEAR_TARGET = 0x3BF;
        public const ushort CMSG_BOT_DETECTED = 0x3C0;
        public const ushort SMSG_CROSSED_INEBRIATION_THRESHOLD = 0x3C1;
        public const ushort CMSG_CHEAT_PLAYER_LOGIN = 0x3C2;
        public const ushort CMSG_CHEAT_PLAYER_LOOKUP = 0x3C3;
        public const ushort SMSG_CHEAT_PLAYER_LOOKUP = 0x3C4;
        public const ushort SMSG_KICK_REASON = 0x3C5;
        public const ushort MSG_RAID_READY_CHECK_FINISHED = 0x3C6;
        public const ushort CMSG_COMPLAIN = 0x3C7;
        public const ushort SMSG_COMPLAIN_RESULT = 0x3C8;
        public const ushort SMSG_FEATURE_SYSTEM_STATUS = 0x3C9;
        public const ushort CMSG_GM_SHOW_COMPLAINTS = 0x3CA;
        public const ushort CMSG_GM_UNSQUELCH = 0x3CB;
        public const ushort CMSG_CHANNEL_SILENCE_VOICE = 0x3CC;
        public const ushort CMSG_CHANNEL_SILENCE_ALL = 0x3CD;
        public const ushort CMSG_CHANNEL_UNSILENCE_VOICE = 0x3CE;
        public const ushort CMSG_CHANNEL_UNSILENCE_ALL = 0x3CF;
        public const ushort CMSG_TARGET_CAST = 0x3D0;
        public const ushort CMSG_TARGET_SCRIPT_CAST = 0x3D1;
        public const ushort CMSG_CHANNEL_DISPLAY_LIST = 0x3D2;
        public const ushort CMSG_SET_ACTIVE_VOICE_CHANNEL = 0x3D3;
        public const ushort CMSG_GET_CHANNEL_MEMBER_COUNT = 0x3D4;
        public const ushort SMSG_CHANNEL_MEMBER_COUNT = 0x3D5;
        public const ushort CMSG_CHANNEL_VOICE_ON = 0x3D6;
        public const ushort CMSG_CHANNEL_VOICE_OFF = 0x3D7;
        public const ushort CMSG_DEBUG_LIST_TARGETS = 0x3D8;
        public const ushort SMSG_DEBUG_LIST_TARGETS = 0x3D9;
        public const ushort SMSG_AVAILABLE_VOICE_CHANNEL = 0x3DA;
        public const ushort CMSG_ADD_VOICE_IGNORE = 0x3DB;
        public const ushort CMSG_DEL_VOICE_IGNORE = 0x3DC;
        public const ushort CMSG_PARTY_SILENCE = 0x3DD;
        public const ushort CMSG_PARTY_UNSILENCE = 0x3DE;
        public const ushort MSG_NOTIFY_PARTY_SQUELCH = 0x3DF;
        public const ushort SMSG_COMSAT_RECONNECT_TRY = 0x3E0;
        public const ushort SMSG_COMSAT_DISCONNECT = 0x3E1;
        public const ushort SMSG_COMSAT_CONNECT_FAIL = 0x3E2;
        public const ushort SMSG_VOICE_CHAT_STATUS = 0x3E3;
        public const ushort CMSG_REPORT_PVP_AFK = 0x3E4;
        public const ushort SMSG_REPORT_PVP_AFK_RESULT = 0x3E5;
        public const ushort CMSG_GUILD_BANKER_ACTIVATE = 0x3E6;
        public const ushort CMSG_GUILD_BANK_QUERY_TAB = 0x3E7;
        public const ushort SMSG_GUILD_BANK_LIST = 0x3E8;
        public const ushort CMSG_GUILD_BANK_SWAP_ITEMS = 0x3E9;
        public const ushort CMSG_GUILD_BANK_BUY_TAB = 0x3EA;
        public const ushort CMSG_GUILD_BANK_UPDATE_TAB = 0x3EB;
        public const ushort CMSG_GUILD_BANK_DEPOSIT_MONEY = 0x3EC;
        public const ushort CMSG_GUILD_BANK_WITHDRAW_MONEY = 0x3ED;
        public const ushort MSG_GUILD_BANK_LOG_QUERY = 0x3EE;
        public const ushort CMSG_SET_CHANNEL_WATCH = 0x3EF;
        public const ushort SMSG_USERLIST_ADD = 0x3F0;
        public const ushort SMSG_USERLIST_REMOVE = 0x3F1;
        public const ushort SMSG_USERLIST_UPDATE = 0x3F2;
        public const ushort CMSG_CLEAR_CHANNEL_WATCH = 0x3F3;
        public const ushort SMSG_INSPECT_TALENT = 0x3F4;
        public const ushort SMSG_GOGOGO_OBSOLETE = 0x3F5;
        public const ushort SMSG_ECHO_PARTY_SQUELCH = 0x3F6;
        public const ushort CMSG_SET_TITLE_SUFFIX = 0x3F7;
        public const ushort CMSG_SPELLCLICK = 0x3F8;
        public const ushort SMSG_LOOT_LIST = 0x3F9;
        public const ushort CMSG_GM_CHARACTER_RESTORE = 0x3FA;
        public const ushort CMSG_GM_CHARACTER_SAVE = 0x3FB;
        public const ushort SMSG_VOICESESSION_FULL = 0x3FC;
        public const ushort MSG_GUILD_PERMISSIONS = 0x3FD;
        public const ushort MSG_GUILD_BANK_MONEY_WITHDRAWN = 0x3FE;
        public const ushort MSG_GUILD_EVENT_LOG_QUERY = 0x3FF;
        public const ushort CMSG_MAELSTROM_RENAME_GUILD = 0x400;
        public const ushort CMSG_GET_MIRRORIMAGE_DATA = 0x401;
        public const ushort SMSG_MIRRORIMAGE_DATA = 0x402;
        public const ushort SMSG_FORCE_DISPLAY_UPDATE = 0x403;
        public const ushort SMSG_SPELL_CHANCE_RESIST_PUSHBACK = 0x404;
        public const ushort CMSG_IGNORE_DIMINISHING_RETURNS_CHEAT = 0x405;
        public const ushort SMSG_IGNORE_DIMINISHING_RETURNS_CHEAT = 0x406;
        public const ushort CMSG_KEEP_ALIVE = 0x407;
        public const ushort SMSG_RAID_READY_CHECK_ERROR = 0x408;
        public const ushort CMSG_OPT_OUT_OF_LOOT = 0x409;
        public const ushort MSG_QUERY_GUILD_BANK_TEXT = 0x40A;
        public const ushort CMSG_SET_GUILD_BANK_TEXT = 0x40B;
        public const ushort CMSG_SET_GRANTABLE_LEVELS = 0x40C;
        public const ushort CMSG_GRANT_LEVEL = 0x40D;
        public const ushort CMSG_REFER_A_FRIEND = 0x40E;
        public const ushort MSG_GM_CHANGE_ARENA_RATING = 0x40F;
        public const ushort CMSG_DECLINE_CHANNEL_INVITE = 0x410;
        public const ushort SMSG_GROUPACTION_THROTTLED = 0x411;
        public const ushort SMSG_OVERRIDE_LIGHT = 0x412;
        public const ushort SMSG_TOTEM_CREATED = 0x413;
        public const ushort CMSG_TOTEM_DESTROYED = 0x414;
        public const ushort CMSG_EXPIRE_RAID_INSTANCE = 0x415;
        public const ushort CMSG_NO_SPELL_VARIANCE = 0x416;
        public const ushort CMSG_QUESTGIVER_STATUS_MULTIPLE_QUERY = 0x417;
        public const ushort SMSG_QUESTGIVER_STATUS_MULTIPLE = 0x418;
        public const ushort CMSG_SET_PLAYER_DECLINED_NAMES = 0x419;
        public const ushort SMSG_SET_PLAYER_DECLINED_NAMES_RESULT = 0x41A;
        public const ushort CMSG_QUERY_SERVER_BUCK_DATA = 0x41B;
        public const ushort CMSG_CLEAR_SERVER_BUCK_DATA = 0x41C;
        public const ushort SMSG_SERVER_BUCK_DATA = 0x41D;
        public const ushort SMSG_SEND_UNLEARN_SPELLS = 0x41E;
        public const ushort SMSG_PROPOSE_LEVEL_GRANT = 0x41F;
        public const ushort CMSG_ACCEPT_LEVEL_GRANT = 0x420;
        public const ushort SMSG_REFER_A_FRIEND_FAILURE = 0x421;
        public const ushort SMSG_SPLINE_MOVE_SET_FLYING = 0x422;
        public const ushort SMSG_SPLINE_MOVE_UNSET_FLYING = 0x423;
        public const ushort SMSG_SUMMON_CANCEL = 0x424;
        public const ushort CMSG_CHANGE_PERSONAL_ARENA_RATING = 0x425;
        public const ushort CMSG_ALTER_APPEARANCE = 0x426;
        public const ushort SMSG_ENABLE_BARBER_SHOP = 0x427;
        public const ushort SMSG_BARBER_SHOP_RESULT = 0x428;
        public const ushort CMSG_CALENDAR_GET_CALENDAR = 0x429;
        public const ushort CMSG_CALENDAR_GET_EVENT = 0x42A;
        public const ushort CMSG_CALENDAR_GUILD_FILTER = 0x42B;
        public const ushort CMSG_CALENDAR_ARENA_TEAM = 0x42C;
        public const ushort CMSG_CALENDAR_ADD_EVENT = 0x42D;
        public const ushort CMSG_CALENDAR_UPDATE_EVENT = 0x42E;
        public const ushort CMSG_CALENDAR_REMOVE_EVENT = 0x42F;
        public const ushort CMSG_CALENDAR_COPY_EVENT = 0x430;
        public const ushort CMSG_CALENDAR_EVENT_INVITE = 0x431;
        public const ushort CMSG_CALENDAR_EVENT_RSVP = 0x432;
        public const ushort CMSG_CALENDAR_EVENT_REMOVE_INVITE = 0x433;
        public const ushort CMSG_CALENDAR_EVENT_STATUS = 0x434;
        public const ushort CMSG_CALENDAR_EVENT_MODERATOR_STATUS = 0x435;
        public const ushort SMSG_CALENDAR_SEND_CALENDAR = 0x436;
        public const ushort SMSG_CALENDAR_SEND_EVENT = 0x437;
        public const ushort SMSG_CALENDAR_FILTER_GUILD = 0x438;
        public const ushort SMSG_CALENDAR_ARENA_TEAM = 0x439;
        public const ushort SMSG_CALENDAR_EVENT_INVITE = 0x43A;
        public const ushort SMSG_CALENDAR_EVENT_INVITE_REMOVED = 0x43B;
        public const ushort SMSG_CALENDAR_EVENT_STATUS = 0x43C;
        public const ushort SMSG_CALENDAR_COMMAND_RESULT = 0x43D;
        public const ushort SMSG_CALENDAR_RAID_LOCKOUT_ADDED = 0x43E;
        public const ushort SMSG_CALENDAR_RAID_LOCKOUT_REMOVED = 0x43F;
        public const ushort SMSG_CALENDAR_EVENT_INVITE_ALERT = 0x440;
        public const ushort SMSG_CALENDAR_EVENT_INVITE_REMOVED_ALERT = 0x441;
        public const ushort SMSG_CALENDAR_EVENT_INVITE_STATUS_ALERT = 0x442;
        public const ushort SMSG_CALENDAR_EVENT_REMOVED_ALERT = 0x443;
        public const ushort SMSG_CALENDAR_EVENT_UPDATED_ALERT = 0x444;
        public const ushort SMSG_CALENDAR_EVENT_MODERATOR_STATUS_ALERT = 0x445;
        public const ushort CMSG_CALENDAR_COMPLAIN = 0x446;
        public const ushort CMSG_CALENDAR_GET_NUM_PENDING = 0x447;
        public const ushort SMSG_CALENDAR_SEND_NUM_PENDING = 0x448;
        public const ushort CMSG_SAVE_DANCE = 0x449;
        public const ushort SMSG_NOTIFY_DANCE = 0x44A;
        public const ushort CMSG_PLAY_DANCE = 0x44B;
        public const ushort SMSG_PLAY_DANCE = 0x44C;
        public const ushort CMSG_LOAD_DANCES = 0x44D;
        public const ushort CMSG_STOP_DANCE = 0x44E;
        public const ushort SMSG_STOP_DANCE = 0x44F;
        public const ushort CMSG_SYNC_DANCE = 0x450;
        public const ushort CMSG_DANCE_QUERY = 0x451;
        public const ushort SMSG_DANCE_QUERY_RESPONSE = 0x452;
        public const ushort SMSG_INVALIDATE_DANCE = 0x453;
        public const ushort CMSG_DELETE_DANCE = 0x454;
        public const ushort SMSG_LEARNED_DANCE_MOVES = 0x455;
        public const ushort CMSG_LEARN_DANCE_MOVE = 0x456;
        public const ushort CMSG_UNLEARN_DANCE_MOVE = 0x457;
        public const ushort CMSG_SET_RUNE_COUNT = 0x458;
        public const ushort CMSG_SET_RUNE_COOLDOWN = 0x459;
        public const ushort MSG_MOVE_SET_PITCH_RATE_CHEAT = 0x45A;
        public const ushort MSG_MOVE_SET_PITCH_RATE = 0x45B;
        public const ushort SMSG_FORCE_PITCH_RATE_CHANGE = 0x45C;
        public const ushort CMSG_FORCE_PITCH_RATE_CHANGE_ACK = 0x45D;
        public const ushort SMSG_SPLINE_SET_PITCH_RATE = 0x45E;
        public const ushort CMSG_CALENDAR_EVENT_INVITE_NOTES = 0x45F;
        public const ushort SMSG_CALENDAR_EVENT_INVITE_NOTES = 0x460;
        public const ushort SMSG_CALENDAR_EVENT_INVITE_NOTES_ALERT = 0x461;
        public const ushort CMSG_UPDATE_MISSILE_TRAJECTORY = 0x462;
        public const ushort SMSG_UPDATE_ACCOUNT_DATA_COMPLETE = 0x463;
        public const ushort SMSG_TRIGGER_MOVIE = 0x464;
        public const ushort CMSG_COMPLETE_MOVIE = 0x465;
        public const ushort CMSG_SET_GLYPH_SLOT = 0x466;
        public const ushort CMSG_SET_GLYPH = 0x467;
        public const ushort SMSG_ACHIEVEMENT_EARNED = 0x468;
        public const ushort SMSG_DYNAMIC_DROP_ROLL_RESULT = 0x469;
        public const ushort SMSG_CRITERIA_UPDATE = 0x46A;
        public const ushort CMSG_QUERY_INSPECT_ACHIEVEMENTS = 0x46B;
        public const ushort SMSG_RESPOND_INSPECT_ACHIEVEMENTS = 0x46C;
        public const ushort CMSG_DISMISS_CONTROLLED_VEHICLE = 0x46D;
        public const ushort CMSG_COMPLETE_ACHIEVEMENT_CHEAT = 0x46E;
        public const ushort SMSG_QUESTUPDATE_ADD_PVP_KILL = 0x46F;
        public const ushort CMSG_SET_CRITERIA_CHEAT = 0x470;
        public const ushort SMSG_CALENDAR_RAID_LOCKOUT_UPDATED = 0x471;
        public const ushort CMSG_UNITANIMTIER_CHEAT = 0x472;
        public const ushort CMSG_CHAR_CUSTOMIZE = 0x473;
        public const ushort SMSG_CHAR_CUSTOMIZE = 0x474;
        public const ushort SMSG_PET_RENAMEABLE = 0x475;
        public const ushort CMSG_REQUEST_VEHICLE_EXIT = 0x476;
        public const ushort CMSG_REQUEST_VEHICLE_PREV_SEAT = 0x477;
        public const ushort CMSG_REQUEST_VEHICLE_NEXT_SEAT = 0x478;
        public const ushort CMSG_REQUEST_VEHICLE_SWITCH_SEAT = 0x479;
        public const ushort CMSG_PET_LEARN_TALENT = 0x47A;
        public const ushort CMSG_PET_UNLEARN_TALENTS = 0x47B;
        public const ushort SMSG_SET_PHASE_SHIFT = 0x47C;
        public const ushort SMSG_ALL_ACHIEVEMENT_DATA = 0x47D;
        public const ushort CMSG_FORCE_SAY_CHEAT = 0x47E;
        public const ushort SMSG_HEALTH_UPDATE = 0x47F;
        public const ushort SMSG_POWER_UPDATE = 0x480;
        public const ushort CMSG_GAMEOBJ_REPORT_USE = 0x481;
        public const ushort SMSG_HIGHEST_THREAT_UPDATE = 0x482;
        public const ushort SMSG_THREAT_UPDATE = 0x483;
        public const ushort SMSG_THREAT_REMOVE = 0x484;
        public const ushort SMSG_THREAT_CLEAR = 0x485;
        public const ushort SMSG_CONVERT_RUNE = 0x486;
        public const ushort SMSG_RESYNC_RUNES = 0x487;
        public const ushort SMSG_ADD_RUNE_POWER = 0x488;
        public const ushort CMSG_START_QUEST = 0x489;
        public const ushort CMSG_REMOVE_GLYPH = 0x48A;
        public const ushort CMSG_DUMP_OBJECTS = 0x48B;
        public const ushort SMSG_DUMP_OBJECTS_DATA = 0x48C;
        public const ushort CMSG_DISMISS_CRITTER = 0x48D;
        public const ushort SMSG_NOTIFY_DEST_LOC_SPELL_CAST = 0x48E;
        public const ushort CMSG_AUCTION_LIST_PENDING_SALES = 0x48F;
        public const ushort SMSG_AUCTION_LIST_PENDING_SALES = 0x490;
        public const ushort SMSG_MODIFY_COOLDOWN = 0x491;
        public const ushort SMSG_PET_UPDATE_COMBO_POINTS = 0x492;
        public const ushort CMSG_ENABLETAXI = 0x493;
        public const ushort SMSG_PRE_RESURRECT = 0x494;
        public const ushort SMSG_AURA_UPDATE_ALL = 0x495;
        public const ushort SMSG_AURA_UPDATE = 0x496;
        public const ushort CMSG_FLOOD_GRACE_CHEAT = 0x497;
        public const ushort SMSG_SERVER_FIRST_ACHIEVEMENT = 0x498;
        public const ushort SMSG_PET_LEARNED_SPELL = 0x499;
        public const ushort SMSG_PET_UNLEARNED_SPELL = 0x49A;
        public const ushort CMSG_CHANGE_SEATS_ON_CONTROLLED_VEHICLE = 0x49B;
        public const ushort CMSG_HEARTH_AND_RESURRECT = 0x49C;
        public const ushort SMSG_ON_CANCEL_EXPECTED_RIDE_VEHICLE_AURA = 0x49D;
        public const ushort SMSG_CRITERIA_DELETED = 0x49E;
        public const ushort SMSG_ACHIEVEMENT_DELETED = 0x49F;
        public const ushort CMSG_SERVER_INFO_QUERY = 0x4A0;
        public const ushort SMSG_SERVER_INFO_RESPONSE = 0x4A1;
        public const ushort CMSG_CHECK_LOGIN_CRITERIA = 0x4A2;
        public const ushort SMSG_SERVER_BUCK_DATA_START = 0x4A3;
        public const ushort CMSG_SET_BREATH = 0x4A4;
        public const ushort CMSG_QUERY_VEHICLE_STATUS = 0x4A5;
        public const ushort SMSG_BATTLEGROUND_INFO_THROTTLED = 0x4A6;
        public const ushort SMSG_PLAYER_VEHICLE_DATA = 0x4A7;
        public const ushort CMSG_PLAYER_VEHICLE_ENTER = 0x4A8;
        public const ushort CMSG_CONTROLLER_EJECT_PASSENGER = 0x4A9;
        public const ushort SMSG_PET_GUIDS = 0x4AA;
        public const ushort SMSG_CLIENTCACHE_VERSION = 0x4AB;
        public const ushort CMSG_CHANGE_GDF_ARENA_RATING = 0x4AC;
        public const ushort CMSG_SET_ARENA_TEAM_RATING_BY_INDEX = 0x4AD;
        public const ushort CMSG_SET_ARENA_TEAM_WEEKLY_GAMES = 0x4AE;
        public const ushort CMSG_SET_ARENA_TEAM_SEASON_GAMES = 0x4AF;
        public const ushort CMSG_SET_ARENA_MEMBER_WEEKLY_GAMES = 0x4B0;
        public const ushort CMSG_SET_ARENA_MEMBER_SEASON_GAMES = 0x4B1;
        public const ushort SMSG_ITEM_REFUND_INFO_RESPONSE = 0x4B2;
        public const ushort CMSG_ITEM_REFUND_INFO = 0x4B3;
        public const ushort CMSG_ITEM_REFUND = 0x4B4;
        public const ushort SMSG_ITEM_REFUND_RESULT = 0x4B5;
        public const ushort CMSG_CORPSE_MAP_POSITION_QUERY = 0x4B6;
        public const ushort SMSG_CORPSE_MAP_POSITION_QUERY_RESPONSE = 0x4B7;
        public const ushort CMSG_UNUSED5 = 0x4B8;
        public const ushort CMSG_UNUSED6 = 0x4B9;
        public const ushort CMSG_CALENDAR_EVENT_SIGNUP = 0x4BA;
        public const ushort SMSG_CALENDAR_CLEAR_PENDING_ACTION = 0x4BB;
        public const ushort SMSG_EQUIPMENT_SET_LIST = 0x4BC;
        public const ushort CMSG_EQUIPMENT_SET_SAVE = 0x4BD;
        public const ushort CMSG_UPDATE_PROJECTILE_POSITION = 0x4BE;
        public const ushort SMSG_SET_PROJECTILE_POSITION = 0x4BF;
        public const ushort SMSG_TALENTS_INFO = 0x4C0;
        public const ushort CMSG_LEARN_PREVIEW_TALENTS = 0x4C1;
        public const ushort CMSG_LEARN_PREVIEW_TALENTS_PET = 0x4C2;
        public const ushort CMSG_SET_ACTIVE_TALENT_GROUP_OBSOLETE = 0x4C3;
        public const ushort CMSG_GM_GRANT_ACHIEVEMENT = 0x4C4;
        public const ushort CMSG_GM_REMOVE_ACHIEVEMENT = 0x4C5;
        public const ushort CMSG_GM_SET_CRITERIA_FOR_PLAYER = 0x4C6;
        public const ushort SMSG_ARENA_UNIT_DESTROYED = 0x4C7;
        public const ushort SMSG_ARENA_TEAM_CHANGE_FAILED_QUEUED = 0x4C8;
        public const ushort CMSG_PROFILEDATA_REQUEST = 0x4C9;
        public const ushort SMSG_PROFILEDATA_RESPONSE = 0x4CA;
        public const ushort CMSG_START_BATTLEFIELD_CHEAT = 0x4CB;
        public const ushort CMSG_END_BATTLEFIELD_CHEAT = 0x4CC;
        public const ushort SMSG_MULTIPLE_PACKETS = 0x4CD;
        public const ushort SMSG_MOVE_GRAVITY_DISABLE = 0x4CE;
        public const ushort CMSG_MOVE_GRAVITY_DISABLE_ACK = 0x4CF;
        public const ushort SMSG_MOVE_GRAVITY_ENABLE = 0x4D0;
        public const ushort CMSG_MOVE_GRAVITY_ENABLE_ACK = 0x4D1;
        public const ushort MSG_MOVE_GRAVITY_CHNG = 0x4D2;
        public const ushort SMSG_SPLINE_MOVE_GRAVITY_DISABLE = 0x4D3;
        public const ushort SMSG_SPLINE_MOVE_GRAVITY_ENABLE = 0x4D4;
        public const ushort CMSG_EQUIPMENT_SET_USE = 0x4D5;
        public const ushort SMSG_EQUIPMENT_SET_USE_RESULT = 0x4D6;
        public const ushort CMSG_FORCE_ANIM = 0x4D7;
        public const ushort SMSG_FORCE_ANIM = 0x4D8;
        public const ushort CMSG_CHAR_FACTION_CHANGE = 0x4D9;
        public const ushort SMSG_CHAR_FACTION_CHANGE = 0x4DA;
        public const ushort CMSG_PVP_QUEUE_STATS_REQUEST = 0x4DB;
        public const ushort SMSG_PVP_QUEUE_STATS = 0x4DC;
        public const ushort CMSG_SET_PAID_SERVICE_CHEAT = 0x4DD;
        public const ushort SMSG_BATTLEFIELD_MGR_ENTRY_INVITE = 0x4DE;
        public const ushort CMSG_BATTLEFIELD_MGR_ENTRY_INVITE_RESPONSE = 0x4DF;
        public const ushort SMSG_BATTLEFIELD_MGR_ENTERED = 0x4E0;
        public const ushort SMSG_BATTLEFIELD_MGR_QUEUE_INVITE = 0x4E1;
        public const ushort CMSG_BATTLEFIELD_MGR_QUEUE_INVITE_RESPONSE = 0x4E2;
        public const ushort CMSG_BATTLEFIELD_MGR_QUEUE_REQUEST = 0x4E3;
        public const ushort SMSG_BATTLEFIELD_MGR_QUEUE_REQUEST_RESPONSE = 0x4E4;
        public const ushort SMSG_BATTLEFIELD_MGR_EJECT_PENDING = 0x4E5;
        public const ushort SMSG_BATTLEFIELD_MGR_EJECTED = 0x4E6;
        public const ushort CMSG_BATTLEFIELD_MGR_EXIT_REQUEST = 0x4E7;
        public const ushort SMSG_BATTLEFIELD_MGR_STATE_CHANGE = 0x4E8;
        public const ushort CMSG_BATTLEFIELD_MANAGER_ADVANCE_STATE = 0x4E9;
        public const ushort CMSG_BATTLEFIELD_MANAGER_SET_NEXT_TRANSITION_TIME = 0x4EA;
        public const ushort MSG_SET_RAID_DIFFICULTY = 0x4EB;
        public const ushort CMSG_TOGGLE_XP_GAIN = 0x4EC;
        public const ushort SMSG_TOGGLE_XP_GAIN = 0x4ED;
        public const ushort SMSG_GMRESPONSE_DB_ERROR = 0x4EE;
        public const ushort SMSG_GMRESPONSE_RECEIVED = 0x4EF;
        public const ushort CMSG_GMRESPONSE_RESOLVE = 0x4F0;
        public const ushort SMSG_GMRESPONSE_STATUS_UPDATE = 0x4F1;
        public const ushort SMSG_GMRESPONSE_CREATE_TICKET = 0x4F2;
        public const ushort CMSG_GMRESPONSE_CREATE_TICKET = 0x4F3;
        public const ushort CMSG_SERVERINFO = 0x4F4;
        public const ushort SMSG_SERVERINFO = 0x4F5;
        public const ushort CMSG_WORLD_STATE_UI_TIMER_UPDATE = 0x4F6;
        public const ushort SMSG_WORLD_STATE_UI_TIMER_UPDATE = 0x4F7;
        public const ushort CMSG_CHAR_RACE_CHANGE = 0x4F8;
        public const ushort MSG_VIEW_PHASE_SHIFT = 0x4F9;
        public const ushort SMSG_TALENTS_INVOLUNTARILY_RESET = 0x4FA;
        public const ushort CMSG_DEBUG_SERVER_GEO = 0x4FB;
        public const ushort SMSG_DEBUG_SERVER_GEO = 0x4FC;
        public const ushort SMSG_LOOT_SLOT_CHANGED = 0x4FD;
        public const ushort UMSG_UPDATE_GROUP_INFO = 0x4FE;
        public const ushort CMSG_READY_FOR_ACCOUNT_DATA_TIMES = 0x4FF;
        public const ushort CMSG_QUERY_QUESTS_COMPLETED = 0x500;
        public const ushort SMSG_QUERY_QUESTS_COMPLETED_RESPONSE = 0x501;
        public const ushort CMSG_GM_REPORT_LAG = 0x502;
        public const ushort CMSG_AFK_MONITOR_INFO_REQUEST = 0x503;
        public const ushort SMSG_AFK_MONITOR_INFO_RESPONSE = 0x504;
        public const ushort CMSG_AFK_MONITOR_INFO_CLEAR = 0x505;
        public const ushort SMSG_CORPSE_NOT_IN_INSTANCE = 0x506;
        public const ushort CMSG_GM_NUKE_CHARACTER = 0x507;
        public const ushort CMSG_SET_ALLOW_LOW_LEVEL_RAID1 = 0x508;
        public const ushort CMSG_SET_ALLOW_LOW_LEVEL_RAID2 = 0x509;
        public const ushort SMSG_CAMERA_SHAKE = 0x50A;
        public const ushort SMSG_SOCKET_GEMS_RESULT = 0x50B;
        public const ushort CMSG_SET_CHARACTER_MODEL = 0x50C;
        public const ushort SMSG_REDIRECT_CLIENT = 0x50D;
        public const ushort CMSG_REDIRECTION_FAILED = 0x50E;
        public const ushort SMSG_SUSPEND_COMMS = 0x50F;
        public const ushort CMSG_SUSPEND_COMMS_ACK = 0x510;
        public const ushort SMSG_FORCE_SEND_QUEUED_PACKETS = 0x511;
        public const ushort CMSG_REDIRECTION_AUTH_PROOF = 0x512;
        public const ushort CMSG_DROP_NEW_CONNECTION = 0x513;
        public const ushort SMSG_SEND_ALL_COMBAT_LOG = 0x514;
        public const ushort SMSG_OPEN_LFG_DUNGEON_FINDER = 0x515;
        public const ushort SMSG_MOVE_SET_COLLISION_HGT = 0x516;
        public const ushort CMSG_MOVE_SET_COLLISION_HGT_ACK = 0x517;
        public const ushort MSG_MOVE_SET_COLLISION_HGT = 0x518;
        public const ushort CMSG_CLEAR_RANDOM_BG_WIN_TIME = 0x519;
        public const ushort CMSG_CLEAR_HOLIDAY_BG_WIN_TIME = 0x51A;
        public const ushort CMSG_COMMENTATOR_SKIRMISH_QUEUE_COMMAND = 0x51B;
        public const ushort SMSG_COMMENTATOR_SKIRMISH_QUEUE_RESULT1 = 0x51C;
        public const ushort SMSG_COMMENTATOR_SKIRMISH_QUEUE_RESULT2 = 0x51D;
        public const ushort SMSG_MULTIPLE_MOVES = 0x51E;
        public const ushort TC9_CMSG_PREPARE_FOR_REDIRECT = 0x51F;
        public const ushort TC9_SMSG_READY_FOR_REDIRECT = 0x520;
        /// <summary>NUM_MSG_TYPES: every valid opcode is below this.</summary>
        public const int Count = 0x521;

        static string[] _names;

        /// <summary>The AzerothCore name of an opcode ("SMSG_AUTH_CHALLENGE"), or "0x1234" when unknown. For logs.</summary>
        public static string Name(int opcode)
        {
            if (_names == null)
            {
                var n = new string[Count];
                string[] parts = NameList.Split(',');
                for (int i = 0; i < parts.Length && i + 1 < Count; i++) n[i + 1] = parts[i];
                n[0] = "MSG_NULL_ACTION";
                _names = n;
            }
            if (opcode >= 0 && opcode < Count && _names[opcode] != null) return _names[opcode];
            return "0x" + opcode.ToString("X3");
        }

        // Names in value order starting at 0x001.
        const string NameList =
            "CMSG_BOOTME,CMSG_DBLOOKUP,SMSG_DBLOOKUP,CMSG_QUERY_OBJECT_POSITION,SMSG_QUERY_OBJECT_POSITION," +
            "CMSG_QUERY_OBJECT_ROTATION,SMSG_QUERY_OBJECT_ROTATION,CMSG_WORLD_TELEPORT,CMSG_TELEPORT_TO_UNIT,CMSG_ZONE_MAP," +
            "SMSG_ZONE_MAP,CMSG_DEBUG_CHANGECELLZONE,CMSG_MOVE_CHARACTER_CHEAT,SMSG_MOVE_CHARACTER_CHEAT,CMSG_RECHARGE," +
            "CMSG_LEARN_SPELL,CMSG_CREATEMONSTER,CMSG_DESTROYMONSTER,CMSG_CREATEITEM,CMSG_CREATEGAMEOBJECT," +
            "SMSG_CHECK_FOR_BOTS,CMSG_MAKEMONSTERATTACKGUID,CMSG_BOT_DETECTED2,CMSG_FORCEACTION,CMSG_FORCEACTIONONOTHER," +
            "CMSG_FORCEACTIONSHOW,SMSG_FORCEACTIONSHOW,CMSG_PETGODMODE,SMSG_PETGODMODE,SMSG_REFER_A_FRIEND_EXPIRED," +
            "CMSG_WEATHER_SPEED_CHEAT,CMSG_UNDRESSPLAYER,CMSG_BEASTMASTER,CMSG_GODMODE,SMSG_GODMODE,CMSG_CHEAT_SETMONEY," +
            "CMSG_LEVEL_CHEAT,CMSG_PET_LEVEL_CHEAT,CMSG_SET_WORLDSTATE,CMSG_COOLDOWN_CHEAT,CMSG_USE_SKILL_CHEAT," +
            "CMSG_FLAG_QUEST,CMSG_FLAG_QUEST_FINISH,CMSG_CLEAR_QUEST,CMSG_SEND_EVENT,CMSG_DEBUG_AISTATE,SMSG_DEBUG_AISTATE," +
            "CMSG_DISABLE_PVP_CHEAT,CMSG_ADVANCE_SPAWN_TIME,SMSG_DESTRUCTIBLE_BUILDING_DAMAGE,CMSG_AUTH_SRP6_BEGIN," +
            "CMSG_AUTH_SRP6_PROOF,CMSG_AUTH_SRP6_RECODE,CMSG_CHAR_CREATE,CMSG_CHAR_ENUM,CMSG_CHAR_DELETE," +
            "SMSG_AUTH_SRP6_RESPONSE,SMSG_CHAR_CREATE,SMSG_CHAR_ENUM,SMSG_CHAR_DELETE,CMSG_PLAYER_LOGIN,SMSG_NEW_WORLD," +
            "SMSG_TRANSFER_PENDING,SMSG_TRANSFER_ABORTED,SMSG_CHARACTER_LOGIN_FAILED,SMSG_LOGIN_SETTIMESPEED," +
            "SMSG_GAMETIME_UPDATE,CMSG_GAMETIME_SET,SMSG_GAMETIME_SET,CMSG_GAMESPEED_SET,SMSG_GAMESPEED_SET," +
            "CMSG_SERVERTIME,SMSG_SERVERTIME,CMSG_PLAYER_LOGOUT,CMSG_LOGOUT_REQUEST,SMSG_LOGOUT_RESPONSE," +
            "SMSG_LOGOUT_COMPLETE,CMSG_LOGOUT_CANCEL,SMSG_LOGOUT_CANCEL_ACK,CMSG_NAME_QUERY,SMSG_NAME_QUERY_RESPONSE," +
            "CMSG_PET_NAME_QUERY,SMSG_PET_NAME_QUERY_RESPONSE,CMSG_GUILD_QUERY,SMSG_GUILD_QUERY_RESPONSE," +
            "CMSG_ITEM_QUERY_SINGLE,CMSG_ITEM_QUERY_MULTIPLE,SMSG_ITEM_QUERY_SINGLE_RESPONSE," +
            "SMSG_ITEM_QUERY_MULTIPLE_RESPONSE,CMSG_PAGE_TEXT_QUERY,SMSG_PAGE_TEXT_QUERY_RESPONSE,CMSG_QUEST_QUERY," +
            "SMSG_QUEST_QUERY_RESPONSE,CMSG_GAMEOBJECT_QUERY,SMSG_GAMEOBJECT_QUERY_RESPONSE,CMSG_CREATURE_QUERY," +
            "SMSG_CREATURE_QUERY_RESPONSE,CMSG_WHO,SMSG_WHO,CMSG_WHOIS,SMSG_WHOIS,CMSG_CONTACT_LIST,SMSG_CONTACT_LIST," +
            "SMSG_FRIEND_STATUS,CMSG_ADD_FRIEND,CMSG_DEL_FRIEND,CMSG_SET_CONTACT_NOTES,CMSG_ADD_IGNORE,CMSG_DEL_IGNORE," +
            "CMSG_GROUP_INVITE,SMSG_GROUP_INVITE,CMSG_GROUP_CANCEL,SMSG_GROUP_CANCEL,CMSG_GROUP_ACCEPT,CMSG_GROUP_DECLINE," +
            "SMSG_GROUP_DECLINE,CMSG_GROUP_UNINVITE,CMSG_GROUP_UNINVITE_GUID,SMSG_GROUP_UNINVITE,CMSG_GROUP_SET_LEADER," +
            "SMSG_GROUP_SET_LEADER,CMSG_LOOT_METHOD,CMSG_GROUP_DISBAND,SMSG_GROUP_DESTROYED,SMSG_GROUP_LIST," +
            "SMSG_PARTY_MEMBER_STATS,SMSG_PARTY_COMMAND_RESULT,UMSG_UPDATE_GROUP_MEMBERS,CMSG_GUILD_CREATE," +
            "CMSG_GUILD_INVITE,SMSG_GUILD_INVITE,CMSG_GUILD_ACCEPT,CMSG_GUILD_DECLINE,SMSG_GUILD_DECLINE,CMSG_GUILD_INFO," +
            "SMSG_GUILD_INFO,CMSG_GUILD_ROSTER,SMSG_GUILD_ROSTER,CMSG_GUILD_PROMOTE,CMSG_GUILD_DEMOTE,CMSG_GUILD_LEAVE," +
            "CMSG_GUILD_REMOVE,CMSG_GUILD_DISBAND,CMSG_GUILD_LEADER,CMSG_GUILD_MOTD,SMSG_GUILD_EVENT," +
            "SMSG_GUILD_COMMAND_RESULT,UMSG_UPDATE_GUILD,CMSG_MESSAGECHAT,SMSG_MESSAGECHAT,CMSG_JOIN_CHANNEL," +
            "CMSG_LEAVE_CHANNEL,SMSG_CHANNEL_NOTIFY,CMSG_CHANNEL_LIST,SMSG_CHANNEL_LIST,CMSG_CHANNEL_PASSWORD," +
            "CMSG_CHANNEL_SET_OWNER,CMSG_CHANNEL_OWNER,CMSG_CHANNEL_MODERATOR,CMSG_CHANNEL_UNMODERATOR,CMSG_CHANNEL_MUTE," +
            "CMSG_CHANNEL_UNMUTE,CMSG_CHANNEL_INVITE,CMSG_CHANNEL_KICK,CMSG_CHANNEL_BAN,CMSG_CHANNEL_UNBAN," +
            "CMSG_CHANNEL_ANNOUNCEMENTS,CMSG_CHANNEL_MODERATE,SMSG_UPDATE_OBJECT,SMSG_DESTROY_OBJECT,CMSG_USE_ITEM," +
            "CMSG_OPEN_ITEM,CMSG_READ_ITEM,SMSG_READ_ITEM_OK,SMSG_READ_ITEM_FAILED,SMSG_ITEM_COOLDOWN,CMSG_GAMEOBJ_USE," +
            "CMSG_DESTROY_ITEMS,SMSG_GAMEOBJECT_CUSTOM_ANIM,CMSG_AREATRIGGER,MSG_MOVE_START_FORWARD," +
            "MSG_MOVE_START_BACKWARD,MSG_MOVE_STOP,MSG_MOVE_START_STRAFE_LEFT,MSG_MOVE_START_STRAFE_RIGHT," +
            "MSG_MOVE_STOP_STRAFE,MSG_MOVE_JUMP,MSG_MOVE_START_TURN_LEFT,MSG_MOVE_START_TURN_RIGHT,MSG_MOVE_STOP_TURN," +
            "MSG_MOVE_START_PITCH_UP,MSG_MOVE_START_PITCH_DOWN,MSG_MOVE_STOP_PITCH,MSG_MOVE_SET_RUN_MODE," +
            "MSG_MOVE_SET_WALK_MODE,MSG_MOVE_TOGGLE_LOGGING,MSG_MOVE_TELEPORT,MSG_MOVE_TELEPORT_CHEAT," +
            "MSG_MOVE_TELEPORT_ACK,MSG_MOVE_TOGGLE_FALL_LOGGING,MSG_MOVE_FALL_LAND,MSG_MOVE_START_SWIM,MSG_MOVE_STOP_SWIM," +
            "MSG_MOVE_SET_RUN_SPEED_CHEAT,MSG_MOVE_SET_RUN_SPEED,MSG_MOVE_SET_RUN_BACK_SPEED_CHEAT," +
            "MSG_MOVE_SET_RUN_BACK_SPEED,MSG_MOVE_SET_WALK_SPEED_CHEAT,MSG_MOVE_SET_WALK_SPEED," +
            "MSG_MOVE_SET_SWIM_SPEED_CHEAT,MSG_MOVE_SET_SWIM_SPEED,MSG_MOVE_SET_SWIM_BACK_SPEED_CHEAT," +
            "MSG_MOVE_SET_SWIM_BACK_SPEED,MSG_MOVE_SET_ALL_SPEED_CHEAT,MSG_MOVE_SET_TURN_RATE_CHEAT,MSG_MOVE_SET_TURN_RATE," +
            "MSG_MOVE_TOGGLE_COLLISION_CHEAT,MSG_MOVE_SET_FACING,MSG_MOVE_SET_PITCH,MSG_MOVE_WORLDPORT_ACK," +
            "SMSG_MONSTER_MOVE,SMSG_MOVE_WATER_WALK,SMSG_MOVE_LAND_WALK,CMSG_MOVE_CHARM_PORT_CHEAT," +
            "CMSG_MOVE_SET_RAW_POSITION,SMSG_FORCE_RUN_SPEED_CHANGE,CMSG_FORCE_RUN_SPEED_CHANGE_ACK," +
            "SMSG_FORCE_RUN_BACK_SPEED_CHANGE,CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK,SMSG_FORCE_SWIM_SPEED_CHANGE," +
            "CMSG_FORCE_SWIM_SPEED_CHANGE_ACK,SMSG_FORCE_MOVE_ROOT,CMSG_FORCE_MOVE_ROOT_ACK,SMSG_FORCE_MOVE_UNROOT," +
            "CMSG_FORCE_MOVE_UNROOT_ACK,MSG_MOVE_ROOT,MSG_MOVE_UNROOT,MSG_MOVE_HEARTBEAT,SMSG_MOVE_KNOCK_BACK," +
            "CMSG_MOVE_KNOCK_BACK_ACK,MSG_MOVE_KNOCK_BACK,SMSG_MOVE_FEATHER_FALL,SMSG_MOVE_NORMAL_FALL,SMSG_MOVE_SET_HOVER," +
            "SMSG_MOVE_UNSET_HOVER,CMSG_MOVE_HOVER_ACK,MSG_MOVE_HOVER,CMSG_TRIGGER_CINEMATIC_CHEAT,CMSG_OPENING_CINEMATIC," +
            "SMSG_TRIGGER_CINEMATIC,CMSG_NEXT_CINEMATIC_CAMERA,CMSG_COMPLETE_CINEMATIC,SMSG_TUTORIAL_FLAGS," +
            "CMSG_TUTORIAL_FLAG,CMSG_TUTORIAL_CLEAR,CMSG_TUTORIAL_RESET,CMSG_STANDSTATECHANGE,CMSG_EMOTE,SMSG_EMOTE," +
            "CMSG_TEXT_EMOTE,SMSG_TEXT_EMOTE,CMSG_AUTOEQUIP_GROUND_ITEM,CMSG_AUTOSTORE_GROUND_ITEM," +
            "CMSG_AUTOSTORE_LOOT_ITEM,CMSG_STORE_LOOT_IN_SLOT,CMSG_AUTOEQUIP_ITEM,CMSG_AUTOSTORE_BAG_ITEM,CMSG_SWAP_ITEM," +
            "CMSG_SWAP_INV_ITEM,CMSG_SPLIT_ITEM,CMSG_AUTOEQUIP_ITEM_SLOT,CMSG_UNCLAIM_LICENSE,CMSG_DESTROYITEM," +
            "SMSG_INVENTORY_CHANGE_FAILURE,SMSG_OPEN_CONTAINER,CMSG_INSPECT,SMSG_INSPECT_RESULTS_UPDATE," +
            "CMSG_INITIATE_TRADE,CMSG_BEGIN_TRADE,CMSG_BUSY_TRADE,CMSG_IGNORE_TRADE,CMSG_ACCEPT_TRADE,CMSG_UNACCEPT_TRADE," +
            "CMSG_CANCEL_TRADE,CMSG_SET_TRADE_ITEM,CMSG_CLEAR_TRADE_ITEM,CMSG_SET_TRADE_GOLD,SMSG_TRADE_STATUS," +
            "SMSG_TRADE_STATUS_EXTENDED,SMSG_INITIALIZE_FACTIONS,SMSG_SET_FACTION_VISIBLE,SMSG_SET_FACTION_STANDING," +
            "CMSG_SET_FACTION_ATWAR,CMSG_SET_FACTION_CHEAT,SMSG_SET_PROFICIENCY,CMSG_SET_ACTION_BUTTON,SMSG_ACTION_BUTTONS," +
            "SMSG_INITIAL_SPELLS,SMSG_LEARNED_SPELL,SMSG_SUPERCEDED_SPELL,CMSG_NEW_SPELL_SLOT,CMSG_CAST_SPELL," +
            "CMSG_CANCEL_CAST,SMSG_CAST_FAILED,SMSG_SPELL_START,SMSG_SPELL_GO,SMSG_SPELL_FAILURE,SMSG_SPELL_COOLDOWN," +
            "SMSG_COOLDOWN_EVENT,CMSG_CANCEL_AURA,SMSG_EQUIPMENT_SET_SAVED,SMSG_PET_CAST_FAILED,MSG_CHANNEL_START," +
            "MSG_CHANNEL_UPDATE,CMSG_CANCEL_CHANNELLING,SMSG_AI_REACTION,CMSG_SET_SELECTION,CMSG_DELETEEQUIPMENT_SET," +
            "CMSG_INSTANCE_LOCK_RESPONSE,CMSG_DEBUG_PASSIVE_AURA,CMSG_ATTACKSWING,CMSG_ATTACKSTOP,SMSG_ATTACKSTART," +
            "SMSG_ATTACKSTOP,SMSG_ATTACKSWING_NOTINRANGE,SMSG_ATTACKSWING_BADFACING,SMSG_INSTANCE_LOCK_WARNING_QUERY," +
            "SMSG_ATTACKSWING_DEADTARGET,SMSG_ATTACKSWING_CANT_ATTACK,SMSG_ATTACKERSTATEUPDATE," +
            "SMSG_BATTLEFIELD_PORT_DENIED,CMSG_PERFORM_ACTION_SET,SMSG_RESUME_CAST_BAR,SMSG_CANCEL_COMBAT," +
            "SMSG_SPELLBREAKLOG,SMSG_SPELLHEALLOG,SMSG_SPELLENERGIZELOG,SMSG_BREAK_TARGET,CMSG_SAVE_PLAYER," +
            "CMSG_SETDEATHBINDPOINT,SMSG_BINDPOINTUPDATE,CMSG_GETDEATHBINDZONE,SMSG_BINDZONEREPLY,SMSG_PLAYERBOUND," +
            "SMSG_CLIENT_CONTROL_UPDATE,CMSG_REPOP_REQUEST,SMSG_RESURRECT_REQUEST,CMSG_RESURRECT_RESPONSE,CMSG_LOOT," +
            "CMSG_LOOT_MONEY,CMSG_LOOT_RELEASE,SMSG_LOOT_RESPONSE,SMSG_LOOT_RELEASE_RESPONSE,SMSG_LOOT_REMOVED," +
            "SMSG_LOOT_MONEY_NOTIFY,SMSG_LOOT_ITEM_NOTIFY,SMSG_LOOT_CLEAR_MONEY,SMSG_ITEM_PUSH_RESULT,SMSG_DUEL_REQUESTED," +
            "SMSG_DUEL_OUTOFBOUNDS,SMSG_DUEL_INBOUNDS,SMSG_DUEL_COMPLETE,SMSG_DUEL_WINNER,CMSG_DUEL_ACCEPTED," +
            "CMSG_DUEL_CANCELLED,SMSG_MOUNTRESULT,SMSG_DISMOUNTRESULT,SMSG_REMOVED_FROM_PVP_QUEUE,CMSG_MOUNTSPECIAL_ANIM," +
            "SMSG_MOUNTSPECIAL_ANIM,SMSG_PET_TAME_FAILURE,CMSG_PET_SET_ACTION,CMSG_PET_ACTION,CMSG_PET_ABANDON," +
            "CMSG_PET_RENAME,SMSG_PET_NAME_INVALID,SMSG_PET_SPELLS,SMSG_PET_MODE,CMSG_GOSSIP_HELLO," +
            "CMSG_GOSSIP_SELECT_OPTION,SMSG_GOSSIP_MESSAGE,SMSG_GOSSIP_COMPLETE,CMSG_NPC_TEXT_QUERY,SMSG_NPC_TEXT_UPDATE," +
            "SMSG_NPC_WONT_TALK,CMSG_QUESTGIVER_STATUS_QUERY,SMSG_QUESTGIVER_STATUS,CMSG_QUESTGIVER_HELLO," +
            "SMSG_QUESTGIVER_QUEST_LIST,CMSG_QUESTGIVER_QUERY_QUEST,CMSG_QUESTGIVER_QUEST_AUTOLAUNCH," +
            "SMSG_QUESTGIVER_QUEST_DETAILS,CMSG_QUESTGIVER_ACCEPT_QUEST,CMSG_QUESTGIVER_COMPLETE_QUEST," +
            "SMSG_QUESTGIVER_REQUEST_ITEMS,CMSG_QUESTGIVER_REQUEST_REWARD,SMSG_QUESTGIVER_OFFER_REWARD," +
            "CMSG_QUESTGIVER_CHOOSE_REWARD,SMSG_QUESTGIVER_QUEST_INVALID,CMSG_QUESTGIVER_CANCEL," +
            "SMSG_QUESTGIVER_QUEST_COMPLETE,SMSG_QUESTGIVER_QUEST_FAILED,CMSG_QUESTLOG_SWAP_QUEST," +
            "CMSG_QUESTLOG_REMOVE_QUEST,SMSG_QUESTLOG_FULL,SMSG_QUESTUPDATE_FAILED,SMSG_QUESTUPDATE_FAILEDTIMER," +
            "SMSG_QUESTUPDATE_COMPLETE,SMSG_QUESTUPDATE_ADD_KILL,SMSG_QUESTUPDATE_ADD_ITEM,CMSG_QUEST_CONFIRM_ACCEPT," +
            "SMSG_QUEST_CONFIRM_ACCEPT,CMSG_PUSHQUESTTOPARTY,CMSG_LIST_INVENTORY,SMSG_LIST_INVENTORY,CMSG_SELL_ITEM," +
            "SMSG_SELL_ITEM,CMSG_BUY_ITEM,CMSG_BUY_ITEM_IN_SLOT,SMSG_BUY_ITEM,SMSG_BUY_FAILED,CMSG_TAXICLEARALLNODES," +
            "CMSG_TAXIENABLEALLNODES,CMSG_TAXISHOWNODES,SMSG_SHOWTAXINODES,CMSG_TAXINODE_STATUS_QUERY,SMSG_TAXINODE_STATUS," +
            "CMSG_TAXIQUERYAVAILABLENODES,CMSG_ACTIVATETAXI,SMSG_ACTIVATETAXIREPLY,SMSG_NEW_TAXI_PATH,CMSG_TRAINER_LIST," +
            "SMSG_TRAINER_LIST,CMSG_TRAINER_BUY_SPELL,SMSG_TRAINER_BUY_SUCCEEDED,SMSG_TRAINER_BUY_FAILED," +
            "CMSG_BINDER_ACTIVATE,SMSG_PLAYERBINDERROR,CMSG_BANKER_ACTIVATE,SMSG_SHOW_BANK,CMSG_BUY_BANK_SLOT," +
            "SMSG_BUY_BANK_SLOT_RESULT,CMSG_PETITION_SHOWLIST,SMSG_PETITION_SHOWLIST,CMSG_PETITION_BUY," +
            "CMSG_PETITION_SHOW_SIGNATURES,SMSG_PETITION_SHOW_SIGNATURES,CMSG_PETITION_SIGN,SMSG_PETITION_SIGN_RESULTS," +
            "MSG_PETITION_DECLINE,CMSG_OFFER_PETITION,CMSG_TURN_IN_PETITION,SMSG_TURN_IN_PETITION_RESULTS," +
            "CMSG_PETITION_QUERY,SMSG_PETITION_QUERY_RESPONSE,SMSG_FISH_NOT_HOOKED,SMSG_FISH_ESCAPED,CMSG_BUG," +
            "SMSG_NOTIFICATION,CMSG_PLAYED_TIME,SMSG_PLAYED_TIME,CMSG_QUERY_TIME,SMSG_QUERY_TIME_RESPONSE,SMSG_LOG_XPGAIN," +
            "SMSG_AURACASTLOG,CMSG_RECLAIM_CORPSE,CMSG_WRAP_ITEM,SMSG_LEVELUP_INFO,MSG_MINIMAP_PING,SMSG_RESISTLOG," +
            "SMSG_ENCHANTMENTLOG,CMSG_SET_SKILL_CHEAT,SMSG_START_MIRROR_TIMER,SMSG_PAUSE_MIRROR_TIMER," +
            "SMSG_STOP_MIRROR_TIMER,CMSG_PING,SMSG_PONG,SMSG_CLEAR_COOLDOWN,SMSG_GAMEOBJECT_PAGETEXT,CMSG_SET_SHEATHED," +
            "SMSG_COOLDOWN_CHEAT,SMSG_SPELL_DELAYED,CMSG_QUEST_POI_QUERY,SMSG_QUEST_POI_QUERY_RESPONSE,CMSG_GHOST," +
            "CMSG_GM_INVIS,SMSG_INVALID_PROMOTION_CODE,MSG_GM_BIND_OTHER,MSG_GM_SUMMON,SMSG_ITEM_TIME_UPDATE," +
            "SMSG_ITEM_ENCHANT_TIME_UPDATE,SMSG_AUTH_CHALLENGE,CMSG_AUTH_SESSION,SMSG_AUTH_RESPONSE,MSG_GM_SHOWLABEL," +
            "CMSG_PET_CAST_SPELL,MSG_SAVE_GUILD_EMBLEM,MSG_TABARDVENDOR_ACTIVATE,SMSG_PLAY_SPELL_VISUAL,CMSG_ZONEUPDATE," +
            "SMSG_PARTYKILLLOG,SMSG_COMPRESSED_UPDATE_OBJECT,SMSG_PLAY_SPELL_IMPACT,SMSG_EXPLORATION_EXPERIENCE," +
            "CMSG_GM_SET_SECURITY_GROUP,CMSG_GM_NUKE,MSG_RANDOM_ROLL,SMSG_ENVIRONMENTAL_DAMAGE_LOG," +
            "CMSG_CHANGEPLAYER_DIFFICULTY,SMSG_RWHOIS,SMSG_LFG_PLAYER_REWARD,SMSG_LFG_TELEPORT_DENIED,CMSG_UNLEARN_SPELL," +
            "CMSG_UNLEARN_SKILL,SMSG_REMOVED_SPELL,CMSG_DECHARGE,CMSG_GMTICKET_CREATE,SMSG_GMTICKET_CREATE," +
            "CMSG_GMTICKET_UPDATETEXT,SMSG_GMTICKET_UPDATETEXT,SMSG_ACCOUNT_DATA_TIMES,CMSG_REQUEST_ACCOUNT_DATA," +
            "CMSG_UPDATE_ACCOUNT_DATA,SMSG_UPDATE_ACCOUNT_DATA,SMSG_CLEAR_FAR_SIGHT_IMMEDIATE," +
            "SMSG_CHANGEPLAYER_DIFFICULTY_RESULT,CMSG_GM_TEACH,CMSG_GM_CREATE_ITEM_TARGET,CMSG_GMTICKET_GETTICKET," +
            "SMSG_GMTICKET_GETTICKET,CMSG_UNLEARN_TALENTS,SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT,SMSG_GAMEOBJECT_DESPAWN_ANIM," +
            "MSG_CORPSE_QUERY,CMSG_GMTICKET_DELETETICKET,SMSG_GMTICKET_DELETETICKET,SMSG_CHAT_WRONG_FACTION," +
            "CMSG_GMTICKET_SYSTEMSTATUS,SMSG_GMTICKET_SYSTEMSTATUS,CMSG_SPIRIT_HEALER_ACTIVATE,CMSG_SET_STAT_CHEAT," +
            "SMSG_QUEST_FORCE_REMOVE,CMSG_SKILL_BUY_STEP,CMSG_SKILL_BUY_RANK,CMSG_XP_CHEAT,SMSG_SPIRIT_HEALER_CONFIRM," +
            "CMSG_CHARACTER_POINT_CHEAT,SMSG_GOSSIP_POI,CMSG_CHAT_IGNORED,CMSG_GM_VISION,CMSG_SERVER_COMMAND," +
            "CMSG_GM_SILENCE,CMSG_GM_REVEALTO,CMSG_GM_RESURRECT,CMSG_GM_SUMMONMOB,CMSG_GM_MOVECORPSE,CMSG_GM_FREEZE," +
            "CMSG_GM_UBERINVIS,CMSG_GM_REQUEST_PLAYER_INFO,SMSG_GM_PLAYER_INFO,CMSG_GUILD_RANK,CMSG_GUILD_ADD_RANK," +
            "CMSG_GUILD_DEL_RANK,CMSG_GUILD_SET_PUBLIC_NOTE,CMSG_GUILD_SET_OFFICER_NOTE,SMSG_LOGIN_VERIFY_WORLD," +
            "CMSG_CLEAR_EXPLORATION,CMSG_SEND_MAIL,SMSG_SEND_MAIL_RESULT,CMSG_GET_MAIL_LIST,SMSG_MAIL_LIST_RESULT," +
            "CMSG_BATTLEFIELD_LIST,SMSG_BATTLEFIELD_LIST,CMSG_BATTLEFIELD_JOIN,SMSG_FORCE_SET_VEHICLE_REC_ID," +
            "CMSG_SET_VEHICLE_REC_ID_ACK,CMSG_TAXICLEARNODE,CMSG_TAXIENABLENODE,CMSG_ITEM_TEXT_QUERY," +
            "SMSG_ITEM_TEXT_QUERY_RESPONSE,CMSG_MAIL_TAKE_MONEY,CMSG_MAIL_TAKE_ITEM,CMSG_MAIL_MARK_AS_READ," +
            "CMSG_MAIL_RETURN_TO_SENDER,CMSG_MAIL_DELETE,CMSG_MAIL_CREATE_TEXT_ITEM,SMSG_SPELLLOGMISS,SMSG_SPELLLOGEXECUTE," +
            "SMSG_DEBUGAURAPROC,SMSG_PERIODICAURALOG,SMSG_SPELLDAMAGESHIELD,SMSG_SPELLNONMELEEDAMAGELOG,CMSG_LEARN_TALENT," +
            "SMSG_RESURRECT_FAILED,CMSG_TOGGLE_PVP,SMSG_ZONE_UNDER_ATTACK,MSG_AUCTION_HELLO,CMSG_AUCTION_SELL_ITEM," +
            "CMSG_AUCTION_REMOVE_ITEM,CMSG_AUCTION_LIST_ITEMS,CMSG_AUCTION_LIST_OWNER_ITEMS,CMSG_AUCTION_PLACE_BID," +
            "SMSG_AUCTION_COMMAND_RESULT,SMSG_AUCTION_LIST_RESULT,SMSG_AUCTION_OWNER_LIST_RESULT," +
            "SMSG_AUCTION_BIDDER_NOTIFICATION,SMSG_AUCTION_OWNER_NOTIFICATION,SMSG_PROCRESIST,SMSG_COMBAT_EVENT_FAILED," +
            "SMSG_DISPEL_FAILED,SMSG_SPELLORDAMAGE_IMMUNE,CMSG_AUCTION_LIST_BIDDER_ITEMS,SMSG_AUCTION_BIDDER_LIST_RESULT," +
            "SMSG_SET_FLAT_SPELL_MODIFIER,SMSG_SET_PCT_SPELL_MODIFIER,CMSG_SET_AMMO,SMSG_CORPSE_RECLAIM_DELAY," +
            "CMSG_SET_ACTIVE_MOVER,CMSG_PET_CANCEL_AURA,CMSG_PLAYER_AI_CHEAT,CMSG_CANCEL_AUTO_REPEAT_SPELL," +
            "MSG_GM_ACCOUNT_ONLINE,MSG_LIST_STABLED_PETS,CMSG_STABLE_PET,CMSG_UNSTABLE_PET,CMSG_BUY_STABLE_SLOT," +
            "SMSG_STABLE_RESULT,CMSG_STABLE_REVIVE_PET,CMSG_STABLE_SWAP_PET,MSG_QUEST_PUSH_RESULT,SMSG_PLAY_MUSIC," +
            "SMSG_PLAY_OBJECT_SOUND,CMSG_REQUEST_PET_INFO,CMSG_FAR_SIGHT,SMSG_SPELLDISPELLOG,SMSG_DAMAGE_CALC_LOG," +
            "CMSG_ENABLE_DAMAGE_LOG,CMSG_GROUP_CHANGE_SUB_GROUP,CMSG_REQUEST_PARTY_MEMBER_STATS,CMSG_GROUP_SWAP_SUB_GROUP," +
            "CMSG_RESET_FACTION_CHEAT,CMSG_AUTOSTORE_BANK_ITEM,CMSG_AUTOBANK_ITEM,MSG_QUERY_NEXT_MAIL_TIME," +
            "SMSG_RECEIVED_MAIL,SMSG_RAID_GROUP_ONLY,CMSG_SET_DURABILITY_CHEAT,CMSG_SET_PVP_RANK_CHEAT," +
            "CMSG_ADD_PVP_MEDAL_CHEAT,CMSG_DEL_PVP_MEDAL_CHEAT,CMSG_SET_PVP_TITLE,SMSG_PVP_CREDIT," +
            "SMSG_AUCTION_REMOVED_NOTIFICATION,CMSG_GROUP_RAID_CONVERT,CMSG_GROUP_ASSISTANT_LEADER,CMSG_BUYBACK_ITEM," +
            "SMSG_CHAT_SERVER_MESSAGE,CMSG_SET_SAVED_INSTANCE_EXTEND,SMSG_LFG_OFFER_CONTINUE,CMSG_TEST_DROP_RATE," +
            "SMSG_TEST_DROP_RATE_RESULT,CMSG_LFG_GET_STATUS,SMSG_SHOW_MAILBOX,SMSG_RESET_RANGED_COMBAT_TIMER," +
            "SMSG_CHAT_NOT_IN_PARTY,CMSG_GMTICKETSYSTEM_TOGGLE,CMSG_CANCEL_GROWTH_AURA,SMSG_CANCEL_AUTO_REPEAT," +
            "SMSG_STANDSTATE_UPDATE,SMSG_LOOT_ALL_PASSED,SMSG_LOOT_ROLL_WON,CMSG_LOOT_ROLL,SMSG_LOOT_START_ROLL," +
            "SMSG_LOOT_ROLL,CMSG_LOOT_MASTER_GIVE,SMSG_LOOT_MASTER_LIST,SMSG_SET_FORCED_REACTIONS,SMSG_SPELL_FAILED_OTHER," +
            "SMSG_GAMEOBJECT_RESET_STATE,CMSG_REPAIR_ITEM,SMSG_CHAT_PLAYER_NOT_FOUND,MSG_TALENT_WIPE_CONFIRM," +
            "SMSG_SUMMON_REQUEST,CMSG_SUMMON_RESPONSE,MSG_DEV_SHOWLABEL,SMSG_MONSTER_MOVE_TRANSPORT,SMSG_PET_BROKEN," +
            "MSG_MOVE_FEATHER_FALL,MSG_MOVE_WATER_WALK,CMSG_SERVER_BROADCAST,CMSG_SELF_RES,SMSG_FEIGN_DEATH_RESISTED," +
            "CMSG_RUN_SCRIPT,SMSG_SCRIPT_MESSAGE,SMSG_DUEL_COUNTDOWN,SMSG_AREA_TRIGGER_MESSAGE,CMSG_SHOWING_HELM," +
            "CMSG_SHOWING_CLOAK,SMSG_LFG_ROLE_CHOSEN,SMSG_PLAYER_SKINNED,SMSG_DURABILITY_DAMAGE_DEATH,CMSG_SET_EXPLORATION," +
            "CMSG_SET_ACTIONBAR_TOGGLES,UMSG_DELETE_GUILD_CHARTER,MSG_PETITION_RENAME,SMSG_INIT_WORLD_STATES," +
            "SMSG_UPDATE_WORLD_STATE,CMSG_ITEM_NAME_QUERY,SMSG_ITEM_NAME_QUERY_RESPONSE,SMSG_PET_ACTION_FEEDBACK," +
            "CMSG_CHAR_RENAME,SMSG_CHAR_RENAME,CMSG_MOVE_SPLINE_DONE,CMSG_MOVE_FALL_RESET,SMSG_INSTANCE_SAVE_CREATED," +
            "SMSG_RAID_INSTANCE_INFO,CMSG_REQUEST_RAID_INFO,CMSG_MOVE_TIME_SKIPPED,CMSG_MOVE_FEATHER_FALL_ACK," +
            "CMSG_MOVE_WATER_WALK_ACK,CMSG_MOVE_NOT_ACTIVE_MOVER,SMSG_PLAY_SOUND,CMSG_BATTLEFIELD_STATUS," +
            "SMSG_BATTLEFIELD_STATUS,CMSG_BATTLEFIELD_PORT,MSG_INSPECT_HONOR_STATS,CMSG_BATTLEMASTER_HELLO," +
            "CMSG_MOVE_START_SWIM_CHEAT,CMSG_MOVE_STOP_SWIM_CHEAT,SMSG_FORCE_WALK_SPEED_CHANGE," +
            "CMSG_FORCE_WALK_SPEED_CHANGE_ACK,SMSG_FORCE_SWIM_BACK_SPEED_CHANGE,CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK," +
            "SMSG_FORCE_TURN_RATE_CHANGE,CMSG_FORCE_TURN_RATE_CHANGE_ACK,MSG_PVP_LOG_DATA,CMSG_LEAVE_BATTLEFIELD," +
            "CMSG_AREA_SPIRIT_HEALER_QUERY,CMSG_AREA_SPIRIT_HEALER_QUEUE,SMSG_AREA_SPIRIT_HEALER_TIME,CMSG_GM_UNTEACH," +
            "SMSG_WARDEN_DATA,CMSG_WARDEN_DATA,SMSG_GROUP_JOINED_BATTLEGROUND,MSG_BATTLEGROUND_PLAYER_POSITIONS," +
            "CMSG_PET_STOP_ATTACK,SMSG_BINDER_CONFIRM,SMSG_BATTLEGROUND_PLAYER_JOINED,SMSG_BATTLEGROUND_PLAYER_LEFT," +
            "CMSG_BATTLEMASTER_JOIN,SMSG_ADDON_INFO,CMSG_PET_UNLEARN,SMSG_PET_UNLEARN_CONFIRM,SMSG_PARTY_MEMBER_STATS_FULL," +
            "CMSG_PET_SPELL_AUTOCAST,SMSG_WEATHER,SMSG_PLAY_TIME_WARNING,SMSG_MINIGAME_SETUP,SMSG_MINIGAME_STATE," +
            "CMSG_MINIGAME_MOVE,SMSG_MINIGAME_MOVE_FAILED,SMSG_RAID_INSTANCE_MESSAGE,SMSG_COMPRESSED_MOVES," +
            "CMSG_GUILD_INFO_TEXT,SMSG_CHAT_RESTRICTED,SMSG_SPLINE_SET_RUN_SPEED,SMSG_SPLINE_SET_RUN_BACK_SPEED," +
            "SMSG_SPLINE_SET_SWIM_SPEED,SMSG_SPLINE_SET_WALK_SPEED,SMSG_SPLINE_SET_SWIM_BACK_SPEED," +
            "SMSG_SPLINE_SET_TURN_RATE,SMSG_SPLINE_MOVE_UNROOT,SMSG_SPLINE_MOVE_FEATHER_FALL,SMSG_SPLINE_MOVE_NORMAL_FALL," +
            "SMSG_SPLINE_MOVE_SET_HOVER,SMSG_SPLINE_MOVE_UNSET_HOVER,SMSG_SPLINE_MOVE_WATER_WALK," +
            "SMSG_SPLINE_MOVE_LAND_WALK,SMSG_SPLINE_MOVE_START_SWIM,SMSG_SPLINE_MOVE_STOP_SWIM," +
            "SMSG_SPLINE_MOVE_SET_RUN_MODE,SMSG_SPLINE_MOVE_SET_WALK_MODE,CMSG_GM_NUKE_ACCOUNT,MSG_GM_DESTROY_CORPSE," +
            "CMSG_GM_DESTROY_ONLINE_CORPSE,CMSG_ACTIVATETAXIEXPRESS,SMSG_SET_FACTION_ATWAR,SMSG_GAMETIMEBIAS_SET," +
            "CMSG_DEBUG_ACTIONS_START,CMSG_DEBUG_ACTIONS_STOP,CMSG_SET_FACTION_INACTIVE,CMSG_SET_WATCHED_FACTION," +
            "MSG_MOVE_TIME_SKIPPED,SMSG_SPLINE_MOVE_ROOT,CMSG_SET_EXPLORATION_ALL,SMSG_INVALIDATE_PLAYER," +
            "CMSG_RESET_INSTANCES,SMSG_INSTANCE_RESET,SMSG_INSTANCE_RESET_FAILED,SMSG_UPDATE_LAST_INSTANCE," +
            "MSG_RAID_TARGET_UPDATE,MSG_RAID_READY_CHECK,CMSG_LUA_USAGE,SMSG_PET_ACTION_SOUND,SMSG_PET_DISMISS_SOUND," +
            "SMSG_GHOSTEE_GONE,CMSG_GM_UPDATE_TICKET_STATUS,SMSG_GM_TICKET_STATUS_UPDATE,MSG_SET_DUNGEON_DIFFICULTY," +
            "CMSG_GMSURVEY_SUBMIT,SMSG_UPDATE_INSTANCE_OWNERSHIP,CMSG_IGNORE_KNOCKBACK_CHEAT,SMSG_CHAT_PLAYER_AMBIGUOUS," +
            "MSG_DELAY_GHOST_TELEPORT,SMSG_SPELLINSTAKILLLOG,SMSG_SPELL_UPDATE_CHAIN_TARGETS,CMSG_CHAT_FILTERED," +
            "SMSG_EXPECTED_SPAM_RECORDS,SMSG_SPELLSTEALLOG,CMSG_LOTTERY_QUERY_OBSOLETE,SMSG_LOTTERY_QUERY_RESULT_OBSOLETE," +
            "CMSG_BUY_LOTTERY_TICKET_OBSOLETE,SMSG_LOTTERY_RESULT_OBSOLETE,SMSG_CHARACTER_PROFILE," +
            "SMSG_CHARACTER_PROFILE_REALM_CONNECTED,SMSG_DEFENSE_MESSAGE,SMSG_INSTANCE_DIFFICULTY," +
            "MSG_GM_RESETINSTANCELIMIT,SMSG_MOTD,SMSG_MOVE_SET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY," +
            "SMSG_MOVE_UNSET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY,CMSG_MOVE_SET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY_ACK," +
            "MSG_MOVE_START_SWIM_CHEAT,MSG_MOVE_STOP_SWIM_CHEAT,SMSG_MOVE_SET_CAN_FLY,SMSG_MOVE_UNSET_CAN_FLY," +
            "CMSG_MOVE_SET_CAN_FLY_ACK,CMSG_MOVE_SET_FLY,CMSG_SOCKET_GEMS,CMSG_ARENA_TEAM_CREATE," +
            "SMSG_ARENA_TEAM_COMMAND_RESULT,MSG_MOVE_UPDATE_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY,CMSG_ARENA_TEAM_QUERY," +
            "SMSG_ARENA_TEAM_QUERY_RESPONSE,CMSG_ARENA_TEAM_ROSTER,SMSG_ARENA_TEAM_ROSTER,CMSG_ARENA_TEAM_INVITE," +
            "SMSG_ARENA_TEAM_INVITE,CMSG_ARENA_TEAM_ACCEPT,CMSG_ARENA_TEAM_DECLINE,CMSG_ARENA_TEAM_LEAVE," +
            "CMSG_ARENA_TEAM_REMOVE,CMSG_ARENA_TEAM_DISBAND,CMSG_ARENA_TEAM_LEADER,SMSG_ARENA_TEAM_EVENT," +
            "CMSG_BATTLEMASTER_JOIN_ARENA,MSG_MOVE_START_ASCEND,MSG_MOVE_STOP_ASCEND,SMSG_ARENA_TEAM_STATS,CMSG_LFG_JOIN," +
            "CMSG_LFG_LEAVE,CMSG_SEARCH_LFG_JOIN,CMSG_SEARCH_LFG_LEAVE,SMSG_UPDATE_LFG_LIST,SMSG_LFG_PROPOSAL_UPDATE," +
            "CMSG_LFG_PROPOSAL_RESULT,SMSG_LFG_ROLE_CHECK_UPDATE,SMSG_LFG_JOIN_RESULT,SMSG_LFG_QUEUE_STATUS," +
            "CMSG_SET_LFG_COMMENT,SMSG_LFG_UPDATE_PLAYER,SMSG_LFG_UPDATE_PARTY,SMSG_LFG_UPDATE_SEARCH,CMSG_LFG_SET_ROLES," +
            "CMSG_LFG_SET_NEEDS,CMSG_LFG_SET_BOOT_VOTE,SMSG_LFG_BOOT_PROPOSAL_UPDATE,CMSG_LFD_PLAYER_LOCK_INFO_REQUEST," +
            "SMSG_LFG_PLAYER_INFO,CMSG_LFG_TELEPORT,CMSG_LFD_PARTY_LOCK_INFO_REQUEST,SMSG_LFG_PARTY_INFO,SMSG_TITLE_EARNED," +
            "CMSG_SET_TITLE,CMSG_CANCEL_MOUNT_AURA,SMSG_ARENA_ERROR,MSG_INSPECT_ARENA_TEAMS,SMSG_DEATH_RELEASE_LOC," +
            "CMSG_CANCEL_TEMP_ENCHANTMENT,SMSG_FORCED_DEATH_UPDATE,CMSG_CHEAT_SET_HONOR_CURRENCY," +
            "CMSG_CHEAT_SET_ARENA_CURRENCY,MSG_MOVE_SET_FLIGHT_SPEED_CHEAT,MSG_MOVE_SET_FLIGHT_SPEED," +
            "MSG_MOVE_SET_FLIGHT_BACK_SPEED_CHEAT,MSG_MOVE_SET_FLIGHT_BACK_SPEED,SMSG_FORCE_FLIGHT_SPEED_CHANGE," +
            "CMSG_FORCE_FLIGHT_SPEED_CHANGE_ACK,SMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE," +
            "CMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK,SMSG_SPLINE_SET_FLIGHT_SPEED,SMSG_SPLINE_SET_FLIGHT_BACK_SPEED," +
            "CMSG_MAELSTROM_INVALIDATE_CACHE,SMSG_FLIGHT_SPLINE_SYNC,CMSG_SET_TAXI_BENCHMARK_MODE," +
            "SMSG_JOINED_BATTLEGROUND_QUEUE,SMSG_REALM_SPLIT,CMSG_REALM_SPLIT,CMSG_MOVE_CHNG_TRANSPORT," +
            "MSG_PARTY_ASSIGNMENT,SMSG_OFFER_PETITION_ERROR,SMSG_TIME_SYNC_REQ,CMSG_TIME_SYNC_RESP,CMSG_SEND_LOCAL_EVENT," +
            "CMSG_SEND_GENERAL_TRIGGER,CMSG_SEND_COMBAT_TRIGGER,CMSG_MAELSTROM_GM_SENT_MAIL,SMSG_RESET_FAILED_NOTIFY," +
            "SMSG_REAL_GROUP_UPDATE,SMSG_LFG_DISABLED,CMSG_ACTIVE_PVP_CHEAT,CMSG_CHEAT_DUMP_ITEMS_DEBUG_ONLY," +
            "SMSG_CHEAT_DUMP_ITEMS_DEBUG_ONLY_RESPONSE,SMSG_CHEAT_DUMP_ITEMS_DEBUG_ONLY_RESPONSE_WRITE_FILE," +
            "SMSG_UPDATE_COMBO_POINTS,SMSG_VOICE_SESSION_ROSTER_UPDATE,SMSG_VOICE_SESSION_LEAVE," +
            "SMSG_VOICE_SESSION_ADJUST_PRIORITY,CMSG_VOICE_SET_TALKER_MUTED_REQUEST,SMSG_VOICE_SET_TALKER_MUTED," +
            "SMSG_INIT_EXTRA_AURA_INFO_OBSOLETE,SMSG_SET_EXTRA_AURA_INFO_OBSOLETE," +
            "SMSG_SET_EXTRA_AURA_INFO_NEED_UPDATE_OBSOLETE,SMSG_CLEAR_EXTRA_AURA_INFO_OBSOLETE,MSG_MOVE_START_DESCEND," +
            "CMSG_IGNORE_REQUIREMENTS_CHEAT,SMSG_IGNORE_REQUIREMENTS_CHEAT,SMSG_SPELL_CHANCE_PROC_LOG," +
            "CMSG_MOVE_SET_RUN_SPEED,SMSG_DISMOUNT,MSG_MOVE_UPDATE_CAN_FLY,MSG_RAID_READY_CHECK_CONFIRM," +
            "CMSG_VOICE_SESSION_ENABLE,SMSG_VOICE_SESSION_ENABLE,SMSG_VOICE_PARENTAL_CONTROLS,CMSG_GM_WHISPER," +
            "SMSG_GM_MESSAGECHAT,MSG_GM_GEARRATING,CMSG_COMMENTATOR_ENABLE,SMSG_COMMENTATOR_STATE_CHANGED," +
            "CMSG_COMMENTATOR_GET_MAP_INFO,SMSG_COMMENTATOR_MAP_INFO,CMSG_COMMENTATOR_GET_PLAYER_INFO," +
            "SMSG_COMMENTATOR_GET_PLAYER_INFO,SMSG_COMMENTATOR_PLAYER_INFO,CMSG_COMMENTATOR_ENTER_INSTANCE," +
            "CMSG_COMMENTATOR_EXIT_INSTANCE,CMSG_COMMENTATOR_INSTANCE_COMMAND,SMSG_CLEAR_TARGET,CMSG_BOT_DETECTED," +
            "SMSG_CROSSED_INEBRIATION_THRESHOLD,CMSG_CHEAT_PLAYER_LOGIN,CMSG_CHEAT_PLAYER_LOOKUP,SMSG_CHEAT_PLAYER_LOOKUP," +
            "SMSG_KICK_REASON,MSG_RAID_READY_CHECK_FINISHED,CMSG_COMPLAIN,SMSG_COMPLAIN_RESULT,SMSG_FEATURE_SYSTEM_STATUS," +
            "CMSG_GM_SHOW_COMPLAINTS,CMSG_GM_UNSQUELCH,CMSG_CHANNEL_SILENCE_VOICE,CMSG_CHANNEL_SILENCE_ALL," +
            "CMSG_CHANNEL_UNSILENCE_VOICE,CMSG_CHANNEL_UNSILENCE_ALL,CMSG_TARGET_CAST,CMSG_TARGET_SCRIPT_CAST," +
            "CMSG_CHANNEL_DISPLAY_LIST,CMSG_SET_ACTIVE_VOICE_CHANNEL,CMSG_GET_CHANNEL_MEMBER_COUNT," +
            "SMSG_CHANNEL_MEMBER_COUNT,CMSG_CHANNEL_VOICE_ON,CMSG_CHANNEL_VOICE_OFF,CMSG_DEBUG_LIST_TARGETS," +
            "SMSG_DEBUG_LIST_TARGETS,SMSG_AVAILABLE_VOICE_CHANNEL,CMSG_ADD_VOICE_IGNORE,CMSG_DEL_VOICE_IGNORE," +
            "CMSG_PARTY_SILENCE,CMSG_PARTY_UNSILENCE,MSG_NOTIFY_PARTY_SQUELCH,SMSG_COMSAT_RECONNECT_TRY," +
            "SMSG_COMSAT_DISCONNECT,SMSG_COMSAT_CONNECT_FAIL,SMSG_VOICE_CHAT_STATUS,CMSG_REPORT_PVP_AFK," +
            "SMSG_REPORT_PVP_AFK_RESULT,CMSG_GUILD_BANKER_ACTIVATE,CMSG_GUILD_BANK_QUERY_TAB,SMSG_GUILD_BANK_LIST," +
            "CMSG_GUILD_BANK_SWAP_ITEMS,CMSG_GUILD_BANK_BUY_TAB,CMSG_GUILD_BANK_UPDATE_TAB,CMSG_GUILD_BANK_DEPOSIT_MONEY," +
            "CMSG_GUILD_BANK_WITHDRAW_MONEY,MSG_GUILD_BANK_LOG_QUERY,CMSG_SET_CHANNEL_WATCH,SMSG_USERLIST_ADD," +
            "SMSG_USERLIST_REMOVE,SMSG_USERLIST_UPDATE,CMSG_CLEAR_CHANNEL_WATCH,SMSG_INSPECT_TALENT,SMSG_GOGOGO_OBSOLETE," +
            "SMSG_ECHO_PARTY_SQUELCH,CMSG_SET_TITLE_SUFFIX,CMSG_SPELLCLICK,SMSG_LOOT_LIST,CMSG_GM_CHARACTER_RESTORE," +
            "CMSG_GM_CHARACTER_SAVE,SMSG_VOICESESSION_FULL,MSG_GUILD_PERMISSIONS,MSG_GUILD_BANK_MONEY_WITHDRAWN," +
            "MSG_GUILD_EVENT_LOG_QUERY,CMSG_MAELSTROM_RENAME_GUILD,CMSG_GET_MIRRORIMAGE_DATA,SMSG_MIRRORIMAGE_DATA," +
            "SMSG_FORCE_DISPLAY_UPDATE,SMSG_SPELL_CHANCE_RESIST_PUSHBACK,CMSG_IGNORE_DIMINISHING_RETURNS_CHEAT," +
            "SMSG_IGNORE_DIMINISHING_RETURNS_CHEAT,CMSG_KEEP_ALIVE,SMSG_RAID_READY_CHECK_ERROR,CMSG_OPT_OUT_OF_LOOT," +
            "MSG_QUERY_GUILD_BANK_TEXT,CMSG_SET_GUILD_BANK_TEXT,CMSG_SET_GRANTABLE_LEVELS,CMSG_GRANT_LEVEL," +
            "CMSG_REFER_A_FRIEND,MSG_GM_CHANGE_ARENA_RATING,CMSG_DECLINE_CHANNEL_INVITE,SMSG_GROUPACTION_THROTTLED," +
            "SMSG_OVERRIDE_LIGHT,SMSG_TOTEM_CREATED,CMSG_TOTEM_DESTROYED,CMSG_EXPIRE_RAID_INSTANCE,CMSG_NO_SPELL_VARIANCE," +
            "CMSG_QUESTGIVER_STATUS_MULTIPLE_QUERY,SMSG_QUESTGIVER_STATUS_MULTIPLE,CMSG_SET_PLAYER_DECLINED_NAMES," +
            "SMSG_SET_PLAYER_DECLINED_NAMES_RESULT,CMSG_QUERY_SERVER_BUCK_DATA,CMSG_CLEAR_SERVER_BUCK_DATA," +
            "SMSG_SERVER_BUCK_DATA,SMSG_SEND_UNLEARN_SPELLS,SMSG_PROPOSE_LEVEL_GRANT,CMSG_ACCEPT_LEVEL_GRANT," +
            "SMSG_REFER_A_FRIEND_FAILURE,SMSG_SPLINE_MOVE_SET_FLYING,SMSG_SPLINE_MOVE_UNSET_FLYING,SMSG_SUMMON_CANCEL," +
            "CMSG_CHANGE_PERSONAL_ARENA_RATING,CMSG_ALTER_APPEARANCE,SMSG_ENABLE_BARBER_SHOP,SMSG_BARBER_SHOP_RESULT," +
            "CMSG_CALENDAR_GET_CALENDAR,CMSG_CALENDAR_GET_EVENT,CMSG_CALENDAR_GUILD_FILTER,CMSG_CALENDAR_ARENA_TEAM," +
            "CMSG_CALENDAR_ADD_EVENT,CMSG_CALENDAR_UPDATE_EVENT,CMSG_CALENDAR_REMOVE_EVENT,CMSG_CALENDAR_COPY_EVENT," +
            "CMSG_CALENDAR_EVENT_INVITE,CMSG_CALENDAR_EVENT_RSVP,CMSG_CALENDAR_EVENT_REMOVE_INVITE," +
            "CMSG_CALENDAR_EVENT_STATUS,CMSG_CALENDAR_EVENT_MODERATOR_STATUS,SMSG_CALENDAR_SEND_CALENDAR," +
            "SMSG_CALENDAR_SEND_EVENT,SMSG_CALENDAR_FILTER_GUILD,SMSG_CALENDAR_ARENA_TEAM,SMSG_CALENDAR_EVENT_INVITE," +
            "SMSG_CALENDAR_EVENT_INVITE_REMOVED,SMSG_CALENDAR_EVENT_STATUS,SMSG_CALENDAR_COMMAND_RESULT," +
            "SMSG_CALENDAR_RAID_LOCKOUT_ADDED,SMSG_CALENDAR_RAID_LOCKOUT_REMOVED,SMSG_CALENDAR_EVENT_INVITE_ALERT," +
            "SMSG_CALENDAR_EVENT_INVITE_REMOVED_ALERT,SMSG_CALENDAR_EVENT_INVITE_STATUS_ALERT," +
            "SMSG_CALENDAR_EVENT_REMOVED_ALERT,SMSG_CALENDAR_EVENT_UPDATED_ALERT," +
            "SMSG_CALENDAR_EVENT_MODERATOR_STATUS_ALERT,CMSG_CALENDAR_COMPLAIN,CMSG_CALENDAR_GET_NUM_PENDING," +
            "SMSG_CALENDAR_SEND_NUM_PENDING,CMSG_SAVE_DANCE,SMSG_NOTIFY_DANCE,CMSG_PLAY_DANCE,SMSG_PLAY_DANCE," +
            "CMSG_LOAD_DANCES,CMSG_STOP_DANCE,SMSG_STOP_DANCE,CMSG_SYNC_DANCE,CMSG_DANCE_QUERY,SMSG_DANCE_QUERY_RESPONSE," +
            "SMSG_INVALIDATE_DANCE,CMSG_DELETE_DANCE,SMSG_LEARNED_DANCE_MOVES,CMSG_LEARN_DANCE_MOVE," +
            "CMSG_UNLEARN_DANCE_MOVE,CMSG_SET_RUNE_COUNT,CMSG_SET_RUNE_COOLDOWN,MSG_MOVE_SET_PITCH_RATE_CHEAT," +
            "MSG_MOVE_SET_PITCH_RATE,SMSG_FORCE_PITCH_RATE_CHANGE,CMSG_FORCE_PITCH_RATE_CHANGE_ACK," +
            "SMSG_SPLINE_SET_PITCH_RATE,CMSG_CALENDAR_EVENT_INVITE_NOTES,SMSG_CALENDAR_EVENT_INVITE_NOTES," +
            "SMSG_CALENDAR_EVENT_INVITE_NOTES_ALERT,CMSG_UPDATE_MISSILE_TRAJECTORY,SMSG_UPDATE_ACCOUNT_DATA_COMPLETE," +
            "SMSG_TRIGGER_MOVIE,CMSG_COMPLETE_MOVIE,CMSG_SET_GLYPH_SLOT,CMSG_SET_GLYPH,SMSG_ACHIEVEMENT_EARNED," +
            "SMSG_DYNAMIC_DROP_ROLL_RESULT,SMSG_CRITERIA_UPDATE,CMSG_QUERY_INSPECT_ACHIEVEMENTS," +
            "SMSG_RESPOND_INSPECT_ACHIEVEMENTS,CMSG_DISMISS_CONTROLLED_VEHICLE,CMSG_COMPLETE_ACHIEVEMENT_CHEAT," +
            "SMSG_QUESTUPDATE_ADD_PVP_KILL,CMSG_SET_CRITERIA_CHEAT,SMSG_CALENDAR_RAID_LOCKOUT_UPDATED," +
            "CMSG_UNITANIMTIER_CHEAT,CMSG_CHAR_CUSTOMIZE,SMSG_CHAR_CUSTOMIZE,SMSG_PET_RENAMEABLE,CMSG_REQUEST_VEHICLE_EXIT," +
            "CMSG_REQUEST_VEHICLE_PREV_SEAT,CMSG_REQUEST_VEHICLE_NEXT_SEAT,CMSG_REQUEST_VEHICLE_SWITCH_SEAT," +
            "CMSG_PET_LEARN_TALENT,CMSG_PET_UNLEARN_TALENTS,SMSG_SET_PHASE_SHIFT,SMSG_ALL_ACHIEVEMENT_DATA," +
            "CMSG_FORCE_SAY_CHEAT,SMSG_HEALTH_UPDATE,SMSG_POWER_UPDATE,CMSG_GAMEOBJ_REPORT_USE,SMSG_HIGHEST_THREAT_UPDATE," +
            "SMSG_THREAT_UPDATE,SMSG_THREAT_REMOVE,SMSG_THREAT_CLEAR,SMSG_CONVERT_RUNE,SMSG_RESYNC_RUNES," +
            "SMSG_ADD_RUNE_POWER,CMSG_START_QUEST,CMSG_REMOVE_GLYPH,CMSG_DUMP_OBJECTS,SMSG_DUMP_OBJECTS_DATA," +
            "CMSG_DISMISS_CRITTER,SMSG_NOTIFY_DEST_LOC_SPELL_CAST,CMSG_AUCTION_LIST_PENDING_SALES," +
            "SMSG_AUCTION_LIST_PENDING_SALES,SMSG_MODIFY_COOLDOWN,SMSG_PET_UPDATE_COMBO_POINTS,CMSG_ENABLETAXI," +
            "SMSG_PRE_RESURRECT,SMSG_AURA_UPDATE_ALL,SMSG_AURA_UPDATE,CMSG_FLOOD_GRACE_CHEAT,SMSG_SERVER_FIRST_ACHIEVEMENT," +
            "SMSG_PET_LEARNED_SPELL,SMSG_PET_UNLEARNED_SPELL,CMSG_CHANGE_SEATS_ON_CONTROLLED_VEHICLE," +
            "CMSG_HEARTH_AND_RESURRECT,SMSG_ON_CANCEL_EXPECTED_RIDE_VEHICLE_AURA,SMSG_CRITERIA_DELETED," +
            "SMSG_ACHIEVEMENT_DELETED,CMSG_SERVER_INFO_QUERY,SMSG_SERVER_INFO_RESPONSE,CMSG_CHECK_LOGIN_CRITERIA," +
            "SMSG_SERVER_BUCK_DATA_START,CMSG_SET_BREATH,CMSG_QUERY_VEHICLE_STATUS,SMSG_BATTLEGROUND_INFO_THROTTLED," +
            "SMSG_PLAYER_VEHICLE_DATA,CMSG_PLAYER_VEHICLE_ENTER,CMSG_CONTROLLER_EJECT_PASSENGER,SMSG_PET_GUIDS," +
            "SMSG_CLIENTCACHE_VERSION,CMSG_CHANGE_GDF_ARENA_RATING,CMSG_SET_ARENA_TEAM_RATING_BY_INDEX," +
            "CMSG_SET_ARENA_TEAM_WEEKLY_GAMES,CMSG_SET_ARENA_TEAM_SEASON_GAMES,CMSG_SET_ARENA_MEMBER_WEEKLY_GAMES," +
            "CMSG_SET_ARENA_MEMBER_SEASON_GAMES,SMSG_ITEM_REFUND_INFO_RESPONSE,CMSG_ITEM_REFUND_INFO,CMSG_ITEM_REFUND," +
            "SMSG_ITEM_REFUND_RESULT,CMSG_CORPSE_MAP_POSITION_QUERY,SMSG_CORPSE_MAP_POSITION_QUERY_RESPONSE,CMSG_UNUSED5," +
            "CMSG_UNUSED6,CMSG_CALENDAR_EVENT_SIGNUP,SMSG_CALENDAR_CLEAR_PENDING_ACTION,SMSG_EQUIPMENT_SET_LIST," +
            "CMSG_EQUIPMENT_SET_SAVE,CMSG_UPDATE_PROJECTILE_POSITION,SMSG_SET_PROJECTILE_POSITION,SMSG_TALENTS_INFO," +
            "CMSG_LEARN_PREVIEW_TALENTS,CMSG_LEARN_PREVIEW_TALENTS_PET,CMSG_SET_ACTIVE_TALENT_GROUP_OBSOLETE," +
            "CMSG_GM_GRANT_ACHIEVEMENT,CMSG_GM_REMOVE_ACHIEVEMENT,CMSG_GM_SET_CRITERIA_FOR_PLAYER," +
            "SMSG_ARENA_UNIT_DESTROYED,SMSG_ARENA_TEAM_CHANGE_FAILED_QUEUED,CMSG_PROFILEDATA_REQUEST," +
            "SMSG_PROFILEDATA_RESPONSE,CMSG_START_BATTLEFIELD_CHEAT,CMSG_END_BATTLEFIELD_CHEAT,SMSG_MULTIPLE_PACKETS," +
            "SMSG_MOVE_GRAVITY_DISABLE,CMSG_MOVE_GRAVITY_DISABLE_ACK,SMSG_MOVE_GRAVITY_ENABLE,CMSG_MOVE_GRAVITY_ENABLE_ACK," +
            "MSG_MOVE_GRAVITY_CHNG,SMSG_SPLINE_MOVE_GRAVITY_DISABLE,SMSG_SPLINE_MOVE_GRAVITY_ENABLE,CMSG_EQUIPMENT_SET_USE," +
            "SMSG_EQUIPMENT_SET_USE_RESULT,CMSG_FORCE_ANIM,SMSG_FORCE_ANIM,CMSG_CHAR_FACTION_CHANGE," +
            "SMSG_CHAR_FACTION_CHANGE,CMSG_PVP_QUEUE_STATS_REQUEST,SMSG_PVP_QUEUE_STATS,CMSG_SET_PAID_SERVICE_CHEAT," +
            "SMSG_BATTLEFIELD_MGR_ENTRY_INVITE,CMSG_BATTLEFIELD_MGR_ENTRY_INVITE_RESPONSE,SMSG_BATTLEFIELD_MGR_ENTERED," +
            "SMSG_BATTLEFIELD_MGR_QUEUE_INVITE,CMSG_BATTLEFIELD_MGR_QUEUE_INVITE_RESPONSE," +
            "CMSG_BATTLEFIELD_MGR_QUEUE_REQUEST,SMSG_BATTLEFIELD_MGR_QUEUE_REQUEST_RESPONSE," +
            "SMSG_BATTLEFIELD_MGR_EJECT_PENDING,SMSG_BATTLEFIELD_MGR_EJECTED,CMSG_BATTLEFIELD_MGR_EXIT_REQUEST," +
            "SMSG_BATTLEFIELD_MGR_STATE_CHANGE,CMSG_BATTLEFIELD_MANAGER_ADVANCE_STATE," +
            "CMSG_BATTLEFIELD_MANAGER_SET_NEXT_TRANSITION_TIME,MSG_SET_RAID_DIFFICULTY,CMSG_TOGGLE_XP_GAIN," +
            "SMSG_TOGGLE_XP_GAIN,SMSG_GMRESPONSE_DB_ERROR,SMSG_GMRESPONSE_RECEIVED,CMSG_GMRESPONSE_RESOLVE," +
            "SMSG_GMRESPONSE_STATUS_UPDATE,SMSG_GMRESPONSE_CREATE_TICKET,CMSG_GMRESPONSE_CREATE_TICKET,CMSG_SERVERINFO," +
            "SMSG_SERVERINFO,CMSG_WORLD_STATE_UI_TIMER_UPDATE,SMSG_WORLD_STATE_UI_TIMER_UPDATE,CMSG_CHAR_RACE_CHANGE," +
            "MSG_VIEW_PHASE_SHIFT,SMSG_TALENTS_INVOLUNTARILY_RESET,CMSG_DEBUG_SERVER_GEO,SMSG_DEBUG_SERVER_GEO," +
            "SMSG_LOOT_SLOT_CHANGED,UMSG_UPDATE_GROUP_INFO,CMSG_READY_FOR_ACCOUNT_DATA_TIMES,CMSG_QUERY_QUESTS_COMPLETED," +
            "SMSG_QUERY_QUESTS_COMPLETED_RESPONSE,CMSG_GM_REPORT_LAG,CMSG_AFK_MONITOR_INFO_REQUEST," +
            "SMSG_AFK_MONITOR_INFO_RESPONSE,CMSG_AFK_MONITOR_INFO_CLEAR,SMSG_CORPSE_NOT_IN_INSTANCE,CMSG_GM_NUKE_CHARACTER," +
            "CMSG_SET_ALLOW_LOW_LEVEL_RAID1,CMSG_SET_ALLOW_LOW_LEVEL_RAID2,SMSG_CAMERA_SHAKE,SMSG_SOCKET_GEMS_RESULT," +
            "CMSG_SET_CHARACTER_MODEL,SMSG_REDIRECT_CLIENT,CMSG_REDIRECTION_FAILED,SMSG_SUSPEND_COMMS," +
            "CMSG_SUSPEND_COMMS_ACK,SMSG_FORCE_SEND_QUEUED_PACKETS,CMSG_REDIRECTION_AUTH_PROOF,CMSG_DROP_NEW_CONNECTION," +
            "SMSG_SEND_ALL_COMBAT_LOG,SMSG_OPEN_LFG_DUNGEON_FINDER,SMSG_MOVE_SET_COLLISION_HGT," +
            "CMSG_MOVE_SET_COLLISION_HGT_ACK,MSG_MOVE_SET_COLLISION_HGT,CMSG_CLEAR_RANDOM_BG_WIN_TIME," +
            "CMSG_CLEAR_HOLIDAY_BG_WIN_TIME,CMSG_COMMENTATOR_SKIRMISH_QUEUE_COMMAND," +
            "SMSG_COMMENTATOR_SKIRMISH_QUEUE_RESULT1,SMSG_COMMENTATOR_SKIRMISH_QUEUE_RESULT2,SMSG_MULTIPLE_MOVES," +
            "TC9_CMSG_PREPARE_FOR_REDIRECT,TC9_SMSG_READY_FOR_REDIRECT,";
    }
}
