/*
 * mod-dreamport-hold - Dreamport ServerKit (WoW 3.3.5a Dreamport, stage S1, 2026-09-27).
 *
 * Immersive VR casting for the Dreamport on YOUR OWN AzerothCore realm: a VR player with Immersive casting on picks a spell,
 * the cast starts at once (cast bar, sounds, everything as normal), and when the cast time is over the spell does NOT fire:
 * it waits in the hand (the controller vibrates) until the player pushes the hand forward. Then it fires.
 *
 * Who is affected: ONLY a player whose client asks for a hold, cast by cast. The Dreamport client asks only for a VR player
 * with Immersive casting switched on, and only for spells with a cast time. A normal WoW client never sends the request, a
 * desktop Dreamport player never sends it, so every other cast on the realm is stock AzerothCore.
 *
 * A held cast is a cast in progress (it is never finished early): moving interrupts it (spells with the movement interrupt
 * flag), no other spell can be cast meanwhile (SPELL_FAILED_SPELL_IN_PROGRESS), power is taken when it fires, silence / stun /
 * death / a kick cancel it as they cancel any cast. It is cancelled after 30 s (ours) and never survives a logout.
 *
 * The messages (the client's request and the server's answers) are LANG_ADDON whispers from the player to ITSELF with the
 * prefix "DPHOLD\t". The module swallows the client's messages before anything is built or sent, so nobody else sees them.
 *   client -> realm:  DPHOLD HELLO <protocol>            is this realm holding casts?
 *                     DPHOLD HOLD <spellId> <castCount>  hold the cast CMSG_CAST_SPELL (castCount) is about to start
 *                     DPHOLD GO <spellId> <castCount>    the push: fire it now (before its end: do not hold it)
 *                     DPHOLD BONUS <spellId> <castCount> (stage S1, PLAN10 s5) the cast CMSG_CAST_SPELL (castCount) is about to send was
 *                                                        released by Immersive casting without a hold (an instant / a channel, or a
 *                                                        cast held on the client): +10 % damage for it
 *                     (cancelling is the normal CMSG_CANCEL_CAST)
 *   realm -> client:  DPHOLD HI <protocol> <timeoutMs> <bonusPct>   this module is installed (bonusPct = the immersive damage bonus, 10)
 *                     DPHOLD ARMED <spellId> <castCount> <castMs>   the cast started and will be held at its end
 *                     DPHOLD READY <spellId> <castCount> the cast time is over; held until GO
 *                     DPHOLD EXPIRED <spellId> <castCount>   held 30 s without GO: cancelled (the normal SMSG_SPELL_FAILURE follows)
 *                     DPHOLD NOHOLD <spellId> <castCount> <why>   not held (instant on the server, channel, too late, unknown spell)
 * (fields separated by a TAB; see ../README.md for the full description and the AzerothCore source lines this relies on)
 *
 * AzerothCore facts (azerothcore-wotlk master, read 2026-09-27):
 * - Player::Whisper (Player.cpp 9675-9690): `if (!sScriptMgr->OnPlayerCanUseChat(this, CHAT_MSG_WHISPER, language, _text, target))
 *   return;` comes before BuildChatPacket / SendDirectMessage - returning false here means nothing is sent to anyone
 *   (ScriptMgrMacros.h CALL_ENABLED_BOOLEAN_HOOKS: the first script that says false stops it). LANG_ADDON is kept for addon
 *   whispers ("bool isAddonMessage = language == LANG_ADDON"). HandleMessagechatOpcode (ChatHandler.cpp) lets LANG_ADDON whispers
 *   through when AddonChannel = 1 (worldserver.conf.dist 4319, the default); AddonChannelCommandHandler only takes "AzerothCore\t".
 * - Unit::Update (Unit.cpp 510-517) calls sScriptMgr->OnUnitUpdate(this, p_time) FIRST, then WorldObject::Update(p_time) ->
 *   m_Events.Update(diff) (Object.cpp 1077-1079) -> SpellEvent::Execute -> m_Spell->update(p_time) (Spell.cpp 8491-8495): the same
 *   diff. Spell::update, SPELL_STATE_PREPARING (Spell.cpp 4556-4567): `if (difftime >= (uint32)m_timer) m_timer = 0; else
 *   m_timer -= difftime;` then `if (m_timer == 0 && !IsNextMeleeSwingSpell() && !IsAutoRepeat()) cast(!m_casttime);`.
 *   So when GetCastTimeRemaining() <= diff in OnUnitUpdate, this update would fire the spell: Spell::ReSetTimer() (public,
 *   Spell.h 565: m_timer = m_casttime) holds it instead, and doing that every update keeps it held.
 * - The movement interrupt (Spell.cpp 4545-4553) needs `m_timer != 0` and SPELL_STATE_PREPARING - true while held.
 *   Spell::prepare refuses a second cast: "Prevent casting at cast another spell (ServerSide check)" -> SPELL_FAILED_SPELL_IN_PROGRESS
 *   (Spell.cpp 3618-3622).
 * - AllSpellScript::OnSpellPrepare is called at the end of Spell::prepare (Spell.cpp 3804) after SetCurrentCastedSpell / SendSpellStart
 *   (3746-3749); HandleCastSpellOpcode sets spell->m_cast_count = castCount before prepare (SpellHandler.cpp 550-551).
 * - Packet order: CMSG_MESSAGECHAT is PROCESS_THREADUNSAFE, CMSG_CAST_SPELL PROCESS_THREADSAFE (Opcodes.cpp 280, 433); both come from
 *   one queue and LockedQueue::next(result, check) (LockedQueue.h 98-113) pops the front packet only when the current filter takes
 *   it, so the HOLD whisper sent before the cast is always handled before the cast.
 * - Threads: the chat hook runs in World::UpdateSessions (world thread), the spell hooks in Map::Update (map threads). The world
 *   updates the sessions, then the maps, and waits for them, so the two never run at the same time; a player's state is only ever
 *   touched by that player's own map thread otherwise.
 * - Player::CustomData (Object.h 237, DataMap.h) keeps our state on the Player object: it is gone with the Player at logout.
 * - ChatHandler::BuildChatPacket (Chat.cpp 273-349, ObjectGuid overload, gmMessage false) = SMSG_MESSAGECHAT: uint8 type; int32 lang;
 *   uint64 sender; uint32 0; uint64 receiver; uint32 len + 1; message; uint8 chatTag.
 *
 * The immersive damage bonus (stage S1, PLAN10 s5 - a Dreamport house rule of the user's realm): a spell a VR Immersive player
 * RELEASED with Immersive casting (a held cast released by DPHOLD GO, or a cast flagged by DPHOLD BONUS) deals +10 % damage
 * (ImmersiveDamageBonus, ours). Damage only, never healing; only that cast; only for the player who flagged it (rule s1.6: normal
 * clients and desktop Dreamport players never send DPHOLD HOLD / BONUS, so they never have the state this needs).
 * - Which cast: PlayerScript::OnPlayerSpellCast(Player*, Spell*, bool skipCheck) (PlayerScript.h 349) runs in Spell::_cast
 *   "before the spell is actually executed" (Spell.cpp 3941-3944), i.e. before handle_immediate (4155) and before a missile is
 *   launched (4135). AllSpellScript::OnSpellCast is NOT usable: it runs after handle_immediate (4208), so an instant's damage
 *   would already be done. For an instant, Spell::prepare calls cast(true) BEFORE OnSpellPrepare (Spell.cpp 3796-3804), so the
 *   BONUS marker is matched at OnPlayerSpellCast by first rank + m_cast_count (Spell.h 539), like HOLD.
 * - Direct damage: UnitScript::ModifySpellDamageTaken(Unit* target, Unit* attacker, int32& damage, SpellInfo const* spellInfo)
 *   (UnitScript.h 74), called by Unit::CalculateSpellDamageTaken (Unit.cpp 1517-1518) for every spell hit (Spell.cpp 2906,
 *   DoAllEffectOnTarget; also aura procs / power burn, SpellAuraEffects.cpp 6881 / 6964). Counted for DreamportBonusDirectMs
 *   after the flagged cast (its missile may still fly) for that spell id from that player.
 * - Periodic damage: UnitScript::ModifyPeriodicDamageAurasTick(Unit* target, Unit* attacker, uint32& damage, SpellInfo const*)
 *   (UnitScript.h 68), SpellAuraEffects.cpp 6341 (periodic damage / damage percent) and 6454 (leech). The SAME hook also runs for
 *   periodic HEALS (6659): only spells with a damaging periodic aura and no periodic heal are ever touched. The aura is the one the
 *   flagged cast applied: UnitScript::OnAuraApply(Unit*, Aura*) (UnitScript.h 84; Unit.cpp 4437 on a stack / refresh, 4578 on a
 *   new application) records it; each tick checks Unit::GetAura(spellId, casterGUID) (Unit.h 1447) is that same Aura with the same
 *   Aura::GetApplyTime() (SpellAuras.h 129). An UNFLAGGED cast of the same spell by that player drops its records first (so a
 *   later re-cast without the flag, or its refresh of the aura, never gets the bonus).
 * This file cannot be compiled on the machine it was written on; the setup script (../../setup-hold.sh) compiles it on the server.
 */

#include "Chat.h"
#include "GameTime.h"
#include "Log.h"
#include "Player.h"
#include "ScriptMgr.h"
#include "SharedDefines.h"
#include "Spell.h"
#include "SpellAuraDefines.h"
#include "SpellAuras.h"
#include "SpellInfo.h"
#include "SpellMgr.h"
#include "WorldPacket.h"
#include <algorithm>
#include <cmath>
#include <ctime>
#include <string>
#include <vector>

namespace
{
    constexpr uint32 DreamportHoldProtocol = 1;
    constexpr uint32 DreamportHoldTimeoutMs = 30000;   // ours (PLAN10 rule 6): a held cast is cancelled after 30 s without the push
    constexpr uint64 DreamportHoldArmWindowMs = 5000;  // ours: a HOLD request is used by a cast that starts within 5 s, else dropped

    // Stage S1 (PLAN10 s5, a Dreamport house rule of the user's realm): +10 % damage for a spell released with Immersive casting.
    constexpr double ImmersiveDamageBonus = 0.10;       // ours - the client's ImmersiveMode.ImmersiveDamageBonus is the same value
    constexpr uint32 ImmersiveDamageBonusPct = 10;      // the same, in the HI answer
    constexpr uint64 DreamportBonusMaxCastMs = 10000;   // ours: a BONUS marker waits at most arm window + this + slack for its cast
    constexpr uint64 DreamportBonusSlackMs = 3000;      // ours: pushback / lag on top of the cast time
    constexpr uint64 DreamportBonusDirectMs = 6000;     // ours: the released cast's direct damage counts this long after it fires (a missile in flight)
    constexpr uint64 DreamportBonusDotCapMs = 600000;   // ours: the periodic damage of its own aura counts at most this long
    char const* const DreamportHoldKey = "DreamportHold";
    char const* const DreamportHoldPrefix = "DPHOLD\t";
    constexpr std::size_t DreamportHoldPrefixLength = 7;

    /// The state of one player, kept in Player::CustomData (dies with the Player object at logout).
    struct DreamportHoldState : public DataMap::Base
    {
        // DPHOLD HOLD waiting for its cast (written by the chat hook, read by OnSpellPrepare)
        uint32 armFirstRank = 0;       // the first rank of the spell asked for (0 = no request)
        uint8 armCount = 0;            // the castCount of the CMSG_CAST_SPELL that will start it
        uint64 armAtMs = 0;            // GameTime::GetGameTimeMS() of the request

        // the cast being held (map thread)
        Spell* spell = nullptr;        // only ever compared with the player's current generic spell; used only while it IS that spell
        uint32 spellId = 0;
        uint8 castCount = 0;
        bool ready = false;            // the cast time is over: held
        uint32 heldMs = 0;             // how long it has been held (sum of the update diffs)
        bool release = false;          // DPHOLD GO arrived (chat hook) - fired by the next update

        // +10 % damage (PLAN10 s5). A DPHOLD BONUS marker waiting for its cast (written by the chat hook, read by OnPlayerSpellCast):
        uint32 markFirstRank = 0;
        uint8 markCount = 0;
        uint64 markAtMs = 0;
        // The held cast DPHOLD GO released (map thread) - only ever compared with the Spell OnPlayerSpellCast gets, never dereferenced:
        Spell* pushedSpell = nullptr;
        uint32 pushedId = 0;
        uint8 pushedCount = 0;
        uint64 pushedUntilMs = 0;
        // Released casts whose direct damage gets the bonus, and the damaging auras they applied (the Aura pointer is only compared
        // with what Unit::GetAura returns, never dereferenced):
        struct Flight { uint32 spellId; uint64 untilMs; };
        struct Dot { Aura* aura; ObjectGuid target; uint32 spellId; time_t applied; uint64 untilMs; };
        std::vector<Flight> flights;
        std::vector<Dot> dots;
        bool bonusSaid = false;        // one log line per login

        void ForgetCast()
        {
            spell = nullptr;
            spellId = 0;
            castCount = 0;
            ready = false;
            heldMs = 0;
            release = false;
        }
    };

    uint64 NowMs()
    {
        return uint64(GameTime::GetGameTimeMS().count());
    }

    /// A LANG_ADDON whisper from the player to itself, to that player's client only (the realm's answer).
    void Tell(Player* player, std::string const& text)
    {
        std::string message = std::string(DreamportHoldPrefix) + text;
        WorldPacket data;
        ChatHandler::BuildChatPacket(data, CHAT_MSG_WHISPER, LANG_ADDON, player->GetGUID(), player->GetGUID(), message, 0);
        player->SendDirectMessage(&data);
    }

    /// "a\tb\tc" -> { "a", "b", "c" }
    std::vector<std::string> Fields(std::string const& text)
    {
        std::vector<std::string> fields;
        std::size_t start = 0;
        while (true)
        {
            std::size_t tab = text.find('\t', start);
            if (tab == std::string::npos)
            {
                fields.push_back(text.substr(start));
                break;
            }
            fields.push_back(text.substr(start, tab - start));
            start = tab + 1;
        }
        return fields;
    }

    /// Decimal digits -> number (anything else -> 0; capped, never throws).
    uint32 Number(std::string const& text)
    {
        uint64 value = 0;
        if (text.empty())
            return 0;
        for (char c : text)
        {
            if (c < '0' || c > '9')
                return 0;
            value = value * 10 + uint64(c - '0');
            if (value > 0xFFFFFFFFull)
                return 0;
        }
        return uint32(value);
    }

    std::string Id(uint32 spellId, uint8 castCount)
    {
        return std::to_string(spellId) + "\t" + std::to_string(uint32(castCount));
    }

    // ---- the immersive damage bonus (PLAN10 s5)

    /// A spell whose aura does periodic DAMAGE (never one that also heals over time: ModifyPeriodicDamageAurasTick runs for heals too).
    bool DamagesOverTime(SpellInfo const* info)
    {
        if (!info || info->HasAura(SPELL_AURA_PERIODIC_HEAL))
            return false;
        return info->HasAura(SPELL_AURA_PERIODIC_DAMAGE) || info->HasAura(SPELL_AURA_PERIODIC_DAMAGE_PERCENT) || info->HasAura(SPELL_AURA_PERIODIC_LEECH);
    }

    /// Drops what has run out (a few entries at most: one per released cast).
    void PruneBonus(DreamportHoldState* h, uint64 now)
    {
        h->flights.erase(std::remove_if(h->flights.begin(), h->flights.end(),
            [now](DreamportHoldState::Flight const& f) { return f.untilMs < now; }), h->flights.end());
        h->dots.erase(std::remove_if(h->dots.begin(), h->dots.end(),
            [now](DreamportHoldState::Dot const& d) { return d.untilMs < now; }), h->dots.end());
    }

    /// An unflagged cast of this spell: nothing of it (nor an aura it refreshes) gets the bonus any more.
    void DropBonus(DreamportHoldState* h, uint32 spellId)
    {
        h->flights.erase(std::remove_if(h->flights.begin(), h->flights.end(),
            [spellId](DreamportHoldState::Flight const& f) { return f.spellId == spellId; }), h->flights.end());
        h->dots.erase(std::remove_if(h->dots.begin(), h->dots.end(),
            [spellId](DreamportHoldState::Dot const& d) { return d.spellId == spellId; }), h->dots.end());
    }

    bool InFlight(DreamportHoldState const* h, uint32 spellId, uint64 now)
    {
        for (DreamportHoldState::Flight const& f : h->flights)
            if (f.spellId == spellId && f.untilMs >= now)
                return true;
        return false;
    }

    uint32 Boosted(uint32 damage)
    {
        return uint32(std::lround(double(damage) * (1.0 + ImmersiveDamageBonus)));
    }
}

/// The messages: DPHOLD HELLO / HOLD / GO, whispered by a player to itself in LANG_ADDON - handled here, never delivered.
class DreamportHoldChat : public PlayerScript
{
public:
    DreamportHoldChat() : PlayerScript("DreamportHoldChat", { PLAYERHOOK_CAN_PLAYER_USE_PRIVATE_CHAT }) { }

    using PlayerScript::OnPlayerCanUseChat;

    bool OnPlayerCanUseChat(Player* player, uint32 type, uint32 language, std::string& msg, Player* receiver) override
    {
        if (type != CHAT_MSG_WHISPER || language != uint32(LANG_ADDON) || msg.compare(0, DreamportHoldPrefixLength, DreamportHoldPrefix) != 0)
            return true;                                        // not ours: AzerothCore carries on as always
        if (!player || receiver != player)
            return false;                                       // our prefix to someone else: dropped, never shown

        std::vector<std::string> f = Fields(msg.substr(DreamportHoldPrefixLength));
        std::string const& verb = f[0];
        DreamportHoldState* h = player->CustomData.GetDefault<DreamportHoldState>(DreamportHoldKey);

        if (verb == "HELLO")
        {
            Tell(player, "HI\t" + std::to_string(DreamportHoldProtocol) + "\t" + std::to_string(DreamportHoldTimeoutMs) + "\t" + std::to_string(ImmersiveDamageBonusPct));
            LOG_INFO("module", "Dreamport hold: {} ({}) said hello (a Dreamport client, protocol {})", player->GetName(), player->GetGUID().ToString(), f.size() > 1 ? f[1] : "?");
        }
        else if (verb == "HOLD" && f.size() >= 3)
        {
            uint32 spellId = Number(f[1]);
            uint8 castCount = uint8(Number(f[2]) & 0xFF);
            SpellInfo const* info = spellId ? sSpellMgr->GetSpellInfo(spellId) : nullptr;
            if (!info)
            {
                Tell(player, "NOHOLD\t" + Id(spellId, castCount) + "\tunknown spell");
                return false;
            }
            h->armFirstRank = info->GetFirstRankSpell()->Id;
            h->armCount = castCount;
            h->armAtMs = NowMs();
        }
        else if (verb == "GO" && f.size() >= 3)
        {
            uint8 castCount = uint8(Number(f[2]) & 0xFF);
            if (h->spell && h->castCount == castCount)
                h->release = true;                              // fired (or, before its end, simply not held) by the next update
            else if (h->armFirstRank && h->armCount == castCount)
                h->armFirstRank = 0;                            // pushed before the cast even started: nothing to hold
        }
        else if (verb == "BONUS" && f.size() >= 3)
        {
            // PLAN10 s5: the CMSG_CAST_SPELL right behind this (same castCount) was released by Immersive casting without a hold
            uint32 spellId = Number(f[1]);
            SpellInfo const* info = spellId ? sSpellMgr->GetSpellInfo(spellId) : nullptr;
            if (info)
            {
                h->markFirstRank = info->GetFirstRankSpell()->Id;
                h->markCount = uint8(Number(f[2]) & 0xFF);
                h->markAtMs = NowMs();
            }
        }
        return false;                                           // never delivered to anyone
    }
};

/// A cast starts: the one the player asked to hold (same spell, same castCount) is taken over.
class DreamportHoldSpell : public AllSpellScript
{
public:
    DreamportHoldSpell() : AllSpellScript("DreamportHoldSpell", { ALLSPELLHOOK_ON_PREPARE }) { }

    void OnSpellPrepare(Spell* spell, Unit* caster, SpellInfo const* spellInfo) override
    {
        if (!spell || !caster || !spellInfo || !caster->IsPlayer())
            return;
        Player* player = caster->ToPlayer();
        DreamportHoldState* h = player->CustomData.Get<DreamportHoldState>(DreamportHoldKey);
        if (!h || !h->armFirstRank)
            return;
        if (spellInfo->GetFirstRankSpell()->Id != h->armFirstRank || spell->m_cast_count != h->armCount)
            return;                                             // another cast (triggered spells have castCount 0): the request waits
        uint8 castCount = h->armCount;
        uint64 age = NowMs() - h->armAtMs;
        h->armFirstRank = 0;
        if (age > DreamportHoldArmWindowMs)
        {
            Tell(player, "NOHOLD\t" + Id(spellInfo->Id, castCount) + "\tlate");
            return;
        }
        if (spell->IsTriggered() || spellInfo->IsChanneled() || spell->IsAutoRepeat() || spell->IsNextMeleeSwingSpell() || spell->GetCastTime() <= 0
            || spell->getState() != SPELL_STATE_PREPARING || player->GetCurrentSpell(CURRENT_GENERIC_SPELL) != spell)
        {
            // instant on the server (a proc, a talent), a channel, or already over: it goes off as it always would
            Tell(player, "NOHOLD\t" + Id(spellInfo->Id, castCount) + "\tinstant");
            return;
        }
        h->ForgetCast();
        h->spell = spell;
        h->spellId = spellInfo->Id;
        h->castCount = castCount;
        Tell(player, "ARMED\t" + Id(h->spellId, h->castCount) + "\t" + std::to_string(spell->GetCastTime()));
    }
};

/// Every update of a player with a held cast, BEFORE the spell's own update (same diff): hold, release, time-out.
class DreamportHoldUnit : public UnitScript
{
public:
    DreamportHoldUnit() : UnitScript("DreamportHoldUnit", true, { UNITHOOK_ON_UNIT_UPDATE }) { }

    void OnUnitUpdate(Unit* unit, uint32 diff) override
    {
        if (!unit || !unit->IsPlayer())
            return;
        Player* player = unit->ToPlayer();
        DreamportHoldState* h = player->CustomData.Get<DreamportHoldState>(DreamportHoldKey);
        if (!h || !h->spell)
            return;

        Spell* cur = player->GetCurrentSpell(CURRENT_GENERIC_SPELL);
        if (cur != h->spell || cur->getState() != SPELL_STATE_PREPARING || cur->GetSpellInfo()->Id != h->spellId || cur->m_cast_count != h->castCount)
        {
            // it ended some other way: moved, interrupted, cancelled (CMSG_CANCEL_CAST), died, teleported, logged out
            h->ForgetCast();
            return;
        }

        if (h->release)
        {
            bool wasReady = h->ready;
            // PLAN10 s5: the cast the push releases gets the immersive damage bonus (DreamportHoldBonusCast, when it is cast - at once
            // below, or at its end when pushed before it: its remaining cast time + slack)
            h->pushedSpell = cur;
            h->pushedId = h->spellId;
            h->pushedCount = h->castCount;
            h->pushedUntilMs = NowMs() + uint64(std::max<int32>(cur->GetCastTimeRemaining(), 0)) + DreamportBonusSlackMs;
            h->ForgetCast();
            if (wasReady)
                cur->cast(false);   // what Spell::update does when m_timer reaches 0 for a spell with a cast time: cast(!m_casttime)
            return;                 // pushed before its end: not held, it finishes by itself
        }

        int32 left = cur->GetCastTimeRemaining();
        if (!h->ready)
        {
            if (left > int32(diff))
                return;             // the cast bar still runs after this update
            cur->ReSetTimer();      // this update would fire it (m_timer -> 0): held instead
            h->ready = true;
            h->heldMs = 0;
            Tell(player, "READY\t" + Id(h->spellId, h->castCount));
            return;
        }

        h->heldMs += diff;
        if (h->heldMs >= DreamportHoldTimeoutMs)
        {
            std::string text = "EXPIRED\t" + Id(h->spellId, h->castCount);
            h->ForgetCast();
            Tell(player, text);
            player->InterruptSpell(CURRENT_GENERIC_SPELL, false);   // Spell::cancel: SMSG_CAST_FAILED + SMSG_SPELL_FAILURE as for any interrupt
            return;
        }
        cur->ReSetTimer();          // held: the timer never runs out
    }
};

/// PLAN10 s5: a cast is executed - is it one the player released with Immersive casting (+10 % damage)?
/// OnPlayerSpellCast runs in Spell::_cast before the spell's effects (Spell.cpp 3941-3944).
class DreamportHoldBonusCast : public PlayerScript
{
public:
    DreamportHoldBonusCast() : PlayerScript("DreamportHoldBonusCast", { PLAYERHOOK_ON_SPELL_CAST }) { }

    void OnPlayerSpellCast(Player* player, Spell* spell, bool /*skipCheck*/) override
    {
        if (!player || !spell || !spell->GetSpellInfo())
            return;
        DreamportHoldState* h = player->CustomData.Get<DreamportHoldState>(DreamportHoldKey);
        if (!h)
            return;                                             // never said DPHOLD: a normal client, nothing to do
        uint64 now = NowMs();
        SpellInfo const* info = spell->GetSpellInfo();
        bool flagged = false;
        if (h->pushedSpell)
        {
            if (h->pushedSpell == spell && h->pushedId == info->Id && h->pushedCount == spell->m_cast_count && now <= h->pushedUntilMs)
            {
                flagged = true;                                 // the held cast the push released
                h->pushedSpell = nullptr;
            }
            else if (now > h->pushedUntilMs)
                h->pushedSpell = nullptr;
        }
        if (!flagged && h->markFirstRank)
        {
            uint64 age = now - h->markAtMs;
            if (!spell->IsTriggered() && info->GetFirstRankSpell()->Id == h->markFirstRank && spell->m_cast_count == h->markCount
                && age <= DreamportHoldArmWindowMs + uint64(std::max<int32>(spell->GetCastTime(), 0)) + DreamportBonusSlackMs)
            {
                flagged = true;                                 // DPHOLD BONUS: an instant / channel / client-held cast released by Immersive casting
                h->markFirstRank = 0;
            }
            else if (age > DreamportHoldArmWindowMs + DreamportBonusMaxCastMs + DreamportBonusSlackMs)
                h->markFirstRank = 0;                           // its cast never came
        }
        PruneBonus(h, now);
        if (!flagged)
        {
            DropBonus(h, info->Id);                             // a normal cast of the same spell: it and its aura are stock again
            return;
        }
        DropBonus(h, info->Id);
        h->flights.push_back({ info->Id, now + DreamportBonusDirectMs });
        if (!h->bonusSaid)
        {
            h->bonusSaid = true;
            LOG_INFO("module", "Dreamport hold: {} released {} ({}) with Immersive casting - +{}% damage for that cast (a Dreamport house rule; logged once per login)",
                player->GetName(), info->Id, uint32(spell->m_cast_count), ImmersiveDamageBonusPct);
        }
    }
};

/// PLAN10 s5: the damage of a released cast (+10 %) - its direct hits and the periodic damage of the aura it applied. Damage only.
class DreamportHoldDamage : public UnitScript
{
public:
    DreamportHoldDamage() : UnitScript("DreamportHoldDamage", true,
        { UNITHOOK_MODIFY_SPELL_DAMAGE_TAKEN, UNITHOOK_MODIFY_PERIODIC_DAMAGE_AURAS_TICK, UNITHOOK_ON_AURA_APPLY }) { }

    // Unit::CalculateSpellDamageTaken (Unit.cpp 1517-1518): a spell hit's damage, before armor / resist / absorb
    void ModifySpellDamageTaken(Unit* /*target*/, Unit* attacker, int32& damage, SpellInfo const* spellInfo) override
    {
        if (!attacker || !spellInfo || damage <= 0 || !attacker->IsPlayer())
            return;
        DreamportHoldState* h = attacker->ToPlayer()->CustomData.Get<DreamportHoldState>(DreamportHoldKey);
        if (!h || h->flights.empty())
            return;
        if (InFlight(h, spellInfo->Id, NowMs()))
            damage = int32(Boosted(uint32(damage)));
    }

    // AuraEffect::HandlePeriodicDamageAurasTick / HandlePeriodicHealthLeechAuraTick (SpellAuraEffects.cpp 6341 / 6454) - and the
    // periodic heal tick (6659), which DamagesOverTime keeps out
    void ModifyPeriodicDamageAurasTick(Unit* target, Unit* attacker, uint32& damage, SpellInfo const* spellInfo) override
    {
        if (!target || !attacker || !spellInfo || damage == 0 || !attacker->IsPlayer() || !DamagesOverTime(spellInfo))
            return;
        DreamportHoldState* h = attacker->ToPlayer()->CustomData.Get<DreamportHoldState>(DreamportHoldKey);
        if (!h || h->dots.empty())
            return;
        uint64 now = NowMs();
        for (DreamportHoldState::Dot const& d : h->dots)
        {
            if (d.spellId != spellInfo->Id || !(d.target == target->GetGUID()) || d.untilMs < now)
                continue;
            Aura* a = target->GetAura(spellInfo->Id, attacker->GetGUID());
            if (a && a == d.aura && a->GetApplyTime() == d.applied)
                damage = Boosted(damage);                       // the aura the released cast applied (not a later re-cast's)
            return;
        }
    }

    // Unit.cpp 4437 (a stack / refresh of an existing aura) and 4578 (a new one): the damaging aura a released cast just applied
    void OnAuraApply(Unit* unit, Aura* aura) override
    {
        if (!unit || !aura || !aura->GetCasterGUID().IsPlayer())
            return;
        SpellInfo const* info = aura->GetSpellInfo();
        if (!DamagesOverTime(info))
            return;
        Unit* caster = aura->GetCaster();
        if (!caster || !caster->IsPlayer())
            return;
        DreamportHoldState* h = caster->ToPlayer()->CustomData.Get<DreamportHoldState>(DreamportHoldKey);
        if (!h || h->flights.empty())
            return;
        uint64 now = NowMs();
        if (!InFlight(h, info->Id, now))
            return;
        ObjectGuid target = unit->GetGUID();
        h->dots.erase(std::remove_if(h->dots.begin(), h->dots.end(),
            [&](DreamportHoldState::Dot const& d) { return d.spellId == info->Id && d.target == target; }), h->dots.end());
        uint64 lasts = aura->GetMaxDuration() > 0 ? uint64(aura->GetMaxDuration()) + DreamportBonusSlackMs : DreamportBonusDotCapMs;
        h->dots.push_back({ aura, target, info->Id, aura->GetApplyTime(), now + std::min(lasts, DreamportBonusDotCapMs) });
    }
};

/// One line at start-up: the setup script (setup-hold.sh) looks for it.
class DreamportHoldWorld : public WorldScript
{
public:
    DreamportHoldWorld() : WorldScript("DreamportHoldWorld", { WORLDHOOK_ON_STARTUP }) { }

    void OnStartup() override
    {
        LOG_INFO("module", "Dreamport hold: module loaded (protocol {}, hold time-out {} ms, arm window {} ms) - only casts a Dreamport VR client asks for are held",
            DreamportHoldProtocol, DreamportHoldTimeoutMs, DreamportHoldArmWindowMs);
        LOG_INFO("module", "Dreamport hold: immersive damage bonus +{}% (ImmersiveDamageBonus {:.2f}) - only for spells a Dreamport VR player releases with Immersive casting; damage only, never healing",
            ImmersiveDamageBonusPct, ImmersiveDamageBonus);
    }
};

void AddSC_dreamport_hold()
{
    new DreamportHoldChat();
    new DreamportHoldSpell();
    new DreamportHoldUnit();
    new DreamportHoldBonusCast();
    new DreamportHoldDamage();
    new DreamportHoldWorld();
}
