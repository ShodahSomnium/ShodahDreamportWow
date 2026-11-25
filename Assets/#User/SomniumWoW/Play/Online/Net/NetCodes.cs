namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// Player-readable texts for every result code the server can send. Values from AzerothCore:
    /// authserver/Authentication/AuthCodes.h (enum AuthResult), shared/SharedDefines.h (enum ResponseCodes,
    /// enum class LoginFailureReason).
    /// </summary>
    public static class NetCodes
    {
        // AuthResult (auth server, AUTH_LOGON_CHALLENGE / AUTH_LOGON_PROOF error byte)
        public const byte WOW_SUCCESS = 0x00, WOW_FAIL_BANNED = 0x03, WOW_FAIL_UNKNOWN_ACCOUNT = 0x04, WOW_FAIL_INCORRECT_PASSWORD = 0x05,
            WOW_FAIL_ALREADY_ONLINE = 0x06, WOW_FAIL_NO_TIME = 0x07, WOW_FAIL_DB_BUSY = 0x08, WOW_FAIL_VERSION_INVALID = 0x09,
            WOW_FAIL_VERSION_UPDATE = 0x0A, WOW_FAIL_INVALID_SERVER = 0x0B, WOW_FAIL_SUSPENDED = 0x0C, WOW_FAIL_FAIL_NOACCESS = 0x0D,
            WOW_SUCCESS_SURVEY = 0x0E, WOW_FAIL_PARENTCONTROL = 0x0F, WOW_FAIL_LOCKED_ENFORCED = 0x10, WOW_FAIL_TRIAL_ENDED = 0x11,
            WOW_FAIL_USE_BATTLENET = 0x12, WOW_FAIL_ANTI_INDULGENCE = 0x13, WOW_FAIL_EXPIRED = 0x14, WOW_FAIL_NO_GAME_ACCOUNT = 0x15,
            WOW_FAIL_CHARGEBACK = 0x16, WOW_FAIL_INTERNET_GAME_ROOM_WITHOUT_BNET = 0x17, WOW_FAIL_GAME_ACCOUNT_LOCKED = 0x18,
            WOW_FAIL_UNLOCKABLE_LOCK = 0x19, WOW_FAIL_CONVERSION_REQUIRED = 0x20, WOW_FAIL_DISCONNECTED = 0xFF;

        /// <summary>Auth-server result. inProof: AzerothCore answers a wrong password with WOW_FAIL_UNKNOWN_ACCOUNT in the proof step.</summary>
        public static string AuthResult(byte code, bool inProof)
        {
            switch (code)
            {
                case WOW_SUCCESS: case WOW_SUCCESS_SURVEY: return "Success";
                case WOW_FAIL_BANNED: return "This account has been banned.";
                case WOW_FAIL_UNKNOWN_ACCOUNT: return inProof ? "Wrong account name or password." : "Unknown account name.";
                case WOW_FAIL_INCORRECT_PASSWORD: return "Wrong password.";
                case WOW_FAIL_ALREADY_ONLINE: return "This account is already logged in.";
                case WOW_FAIL_NO_TIME: return "This account has no game time.";
                case WOW_FAIL_DB_BUSY: return "The server's database is busy - try again shortly.";
                case WOW_FAIL_VERSION_INVALID: return "The server does not accept this client version (3.3.5a, build 12340).";
                case WOW_FAIL_VERSION_UPDATE: return "The server wants a client update (this world speaks 3.3.5a, build 12340).";
                case WOW_FAIL_INVALID_SERVER: return "Invalid server.";
                case WOW_FAIL_SUSPENDED: return "This account is temporarily suspended.";
                case WOW_FAIL_FAIL_NOACCESS: return "This account has no access.";
                case WOW_FAIL_PARENTCONTROL: return "Blocked by parental controls.";
                case WOW_FAIL_LOCKED_ENFORCED: return "This account is locked to another IP address.";
                case WOW_FAIL_TRIAL_ENDED: return "The trial has ended.";
                case WOW_FAIL_USE_BATTLENET: return "The server asks for a Battle.net login (not supported).";
                case WOW_FAIL_ANTI_INDULGENCE: return "Blocked by play-time limits.";
                case WOW_FAIL_EXPIRED: return "This account has expired.";
                case WOW_FAIL_NO_GAME_ACCOUNT: return "No game account.";
                case WOW_FAIL_CHARGEBACK: return "This account is locked (chargeback).";
                case WOW_FAIL_INTERNET_GAME_ROOM_WITHOUT_BNET: return "Internet game room login is not supported.";
                case WOW_FAIL_GAME_ACCOUNT_LOCKED: return "This game account is locked.";
                case WOW_FAIL_UNLOCKABLE_LOCK: return "This account is locked to another country.";
                case WOW_FAIL_CONVERSION_REQUIRED: return "The account needs a conversion on the server.";
                case WOW_FAIL_DISCONNECTED: return "Disconnected by the server.";
                default: return "Login failed (auth code 0x" + code.ToString("X2") + ").";
            }
        }

        // ResponseCodes (world server) used here
        public const byte AUTH_OK = 0x0C, AUTH_FAILED = 0x0D, AUTH_REJECT = 0x0E, AUTH_BAD_SERVER_PROOF = 0x0F, AUTH_UNAVAILABLE = 0x10,
            AUTH_SYSTEM_ERROR = 0x11, AUTH_BILLING_ERROR = 0x12, AUTH_BILLING_EXPIRED = 0x13, AUTH_VERSION_MISMATCH = 0x14,
            AUTH_UNKNOWN_ACCOUNT = 0x15, AUTH_INCORRECT_PASSWORD = 0x16, AUTH_SESSION_EXPIRED = 0x17, AUTH_SERVER_SHUTTING_DOWN = 0x18,
            AUTH_ALREADY_LOGGING_IN = 0x19, AUTH_LOGIN_SERVER_NOT_FOUND = 0x1A, AUTH_WAIT_QUEUE = 0x1B, AUTH_BANNED = 0x1C,
            AUTH_ALREADY_ONLINE = 0x1D, AUTH_NO_TIME = 0x1E, AUTH_DB_BUSY = 0x1F, AUTH_SUSPENDED = 0x20, AUTH_PARENTAL_CONTROL = 0x21,
            AUTH_LOCKED_ENFORCED = 0x22, REALM_LIST_REALM_NOT_FOUND = 0x27,
            CHAR_CREATE_SUCCESS = 0x2F, CHAR_DELETE_SUCCESS = 0x47, CHAR_LOGIN_SUCCESS = 0x4D;

        /// <summary>SMSG_AUTH_RESPONSE code (world server).</summary>
        public static string WorldAuth(byte code)
        {
            switch (code)
            {
                case AUTH_OK: return "Success";
                case AUTH_FAILED: return "The world server rejected the session (session key mismatch or locked account).";
                case AUTH_REJECT: return "The world server rejected the login (world closed, or Warden needs a Windows/Mac client - switch Warden off in the ServerKit).";
                case AUTH_BAD_SERVER_PROOF: return "Bad server proof.";
                case AUTH_UNAVAILABLE: return "The realm is not open to players right now.";
                case AUTH_SYSTEM_ERROR: return "The world server had a system error.";
                case AUTH_BILLING_ERROR: case AUTH_BILLING_EXPIRED: return "Billing problem on this account.";
                case AUTH_VERSION_MISMATCH: return "Client version mismatch.";
                case AUTH_UNKNOWN_ACCOUNT: return "The world server does not know this account (log in again).";
                case AUTH_INCORRECT_PASSWORD: return "Wrong password.";
                case AUTH_SESSION_EXPIRED: return "The session expired - log in again.";
                case AUTH_SERVER_SHUTTING_DOWN: return "The server is shutting down.";
                case AUTH_ALREADY_LOGGING_IN: return "This account is already logging in.";
                case AUTH_LOGIN_SERVER_NOT_FOUND: return "Login server not found.";
                case AUTH_WAIT_QUEUE: return "Waiting in the login queue.";
                case AUTH_BANNED: return "This account has been banned.";
                case AUTH_ALREADY_ONLINE: return "This account is already online.";
                case AUTH_NO_TIME: return "This account has no game time.";
                case AUTH_DB_BUSY: return "The server's database is busy.";
                case AUTH_SUSPENDED: return "This account is suspended.";
                case AUTH_PARENTAL_CONTROL: return "Blocked by parental controls.";
                case AUTH_LOCKED_ENFORCED: return "This account is locked.";
                case REALM_LIST_REALM_NOT_FOUND: return "The world server is not the realm picked in the realm list.";
                default: return "The world server refused the session (code 0x" + code.ToString("X2") + ").";
            }
        }

        /// <summary>SMSG_CHAR_CREATE result.</summary>
        public static string CharCreate(byte code)
        {
            switch (code)
            {
                case 0x2E: return "Creating character...";
                case 0x2F: return "Character created.";
                case 0x30: return "Error creating character.";
                case 0x31: return "Character creation failed.";
                case 0x32: return "That name is already taken.";
                case 0x33: return "Creating characters of this race/class is disabled on this realm.";
                case 0x34: return "You cannot have characters of both factions on this realm (PvP realm).";
                case 0x35: return "The realm has reached its character limit.";
                case 0x36: return "You already have the maximum number of characters on this account.";
                case 0x37: return "The realm is queued - try again later.";
                case 0x38: return "Only existing characters may log in right now.";
                case 0x39: return "This race needs an expansion the account does not have.";
                case 0x3A: return "This class needs an expansion the account does not have.";
                case 0x3B: return "You need a higher-level character on this realm to create a Death Knight.";
                case 0x3C: return "You can only have one character of this class on this realm.";
                case 0x3D: return "Character in a guild.";
                case 0x3E: return "This race/class combination is not allowed.";
                case 0x3F: return "Choose a race.";
                case 0x40: return "Character is an arena team leader.";
                case 0x41: return "Character has mail to delete first.";
                case 0x42: return "Faction change in progress.";
                case 0x43: return "Race change only.";
                case 0x44: return "Gold limit reached.";
                case 0x45: return "Force login.";
                case 0x57: return "Name OK.";
                case 0x58: return "Invalid name.";
                case 0x59: return "Enter a name.";
                case 0x5A: return "That name is too short.";
                case 0x5B: return "That name is too long.";
                case 0x5C: return "Names can only contain letters.";
                case 0x5D: return "Names cannot mix languages.";
                case 0x5E: return "That name is not allowed.";
                case 0x5F: return "That name is reserved.";
                case 0x60: return "Apostrophe not allowed there.";
                case 0x61: return "Only one apostrophe is allowed.";
                case 0x62: return "No letter may appear three times in a row.";
                case 0x63: return "Spaces are not allowed.";
                case 0x64: return "No consecutive spaces.";
                default: return "Character creation failed (code 0x" + code.ToString("X2") + ").";
            }
        }

        /// <summary>SMSG_CHAR_DELETE result.</summary>
        public static string CharDelete(byte code)
        {
            switch (code)
            {
                case 0x46: return "Deleting character...";
                case 0x47: return "Character deleted.";
                case 0x48: return "Character deletion failed.";
                case 0x49: return "The character is locked for a transfer.";
                case 0x4A: return "A guild master cannot be deleted - pass the guild on first.";
                case 0x4B: return "An arena team captain cannot be deleted.";
                default: return "Character deletion failed (code 0x" + code.ToString("X2") + ").";
            }
        }

        /// <summary>SMSG_CHARACTER_LOGIN_FAILED reason (LoginFailureReason).</summary>
        public static string LoginFailed(byte code)
        {
            switch (code)
            {
                case 0: return "Entering the world failed.";
                case 1: return "The world server is down.";
                case 2: return "A character with that name is already in the world.";
                case 3: return "No instance servers are available.";
                case 4: return "Logging in with this race, class or character is disabled.";
                case 5: return "Character not found.";
                case 6: return "The character is locked for a transfer.";
                case 7: return "The character is locked (billing).";
                case 8: return "Cannot log in while using WoW Remote.";
                default: return "Entering the world failed (code " + code + ").";
            }
        }
    }
}
