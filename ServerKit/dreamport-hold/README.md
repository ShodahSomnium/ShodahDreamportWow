# mod-dreamport-hold - immersive VR casting on your own realm

A small **AzerothCore module** (no change to AzerothCore's own files) for the WoW Dreamport's **Immersive casting**
(VR, Settings > Gameplay, off by default):

- you pick a spell with a cast time from the radial menu: **the cast starts at once** (cast bar, glow, sounds as always);
- when the cast time is over the spell **does not fire**: the controller vibrates and the spell glows in your hand, pulsing,
  for as long as you keep it (up to 30 s);
- **push that hand forward** and it fires at the target you started it on;
- instant spells fire at once, as always.

`../setup-hold.sh` installs it (section 9 of `../README.md`).

## Who it affects

Only a player whose client asks for a hold, cast by cast. The Dreamport asks only for a **VR player with Immersive casting
on**, and only for spells with a cast time. Normal WoW clients never ask; desktop (2D) Dreamport players never ask. Every
other cast on the realm is exactly stock AzerothCore.

Every Dreamport client says hello once after entering the world (`DPHOLD HELLO`, to find out whether this realm has the
module); that message changes nothing on the realm and nobody sees it.

## What a held cast is

A **cast in progress** that is not allowed to finish yet - nothing is finished early, nothing is free:

| While held | Why (AzerothCore master, read 2026-09-27) |
|---|---|
| Moving interrupts it (spells that moving interrupts) | Spell::update checks `m_timer != 0 && isMoving() && InterruptFlags & SPELL_INTERRUPT_FLAG_MOVEMENT` in SPELL_STATE_PREPARING (Spell.cpp 4545-4553) - true while held |
| No other spell can be cast meanwhile | Spell::prepare: "Prevent casting at cast another spell (ServerSide check)" -> SPELL_FAILED_SPELL_IN_PROGRESS (Spell.cpp 3618-3622) |
| Mana / rage / energy is paid when it fires | the cost is taken in Spell::_cast (TakePower, Spell.cpp 4051), which runs at the push |
| Silence, stun, a kick, death, a teleport cancel it | they cancel a current cast the normal way; the module then forgets it |
| Cancelled after 30 s without the push (ours) | `DPHOLD EXPIRED`, then Unit::InterruptSpell (the client gets the normal SMSG_SPELL_FAILURE) |
| Never survives a logout | the state lives on the Player object (Player::CustomData, Object.h 237) |
| Other players see your cast bar full until you push | they get the normal SMSG_SPELL_START; SMSG_SPELL_GO comes at the push |

## How it works

1. The client whispers **itself** in the addon language (`LANG_ADDON`) with the prefix `DPHOLD<TAB>`, right before it sends
   the cast (`CMSG_CAST_SPELL`). AzerothCore handles the two in the order they were sent: both come from one queue, and
   `LockedQueue::next(result, check)` (LockedQueue.h 98-113) pops the front packet only when the current filter takes it
   (CMSG_MESSAGECHAT is PROCESS_THREADUNSAFE, CMSG_CAST_SPELL PROCESS_THREADSAFE, Opcodes.cpp 280 / 433).
2. `Player::Whisper` (Player.cpp 9675-9690) asks `sScriptMgr->OnPlayerCanUseChat(this, CHAT_MSG_WHISPER, language, _text, target)`
   **before** it builds or sends anything. The module answers `false` for `DPHOLD` messages, so the message is never sent to
   anyone - not even back to you. (The chat path before it: LANG_ADDON whispers pass when `AddonChannel = 1`, the default,
   worldserver.conf.dist 4319; a whisper to yourself skips the level / trial checks, ChatHandler.cpp `receiver != sender`.)
3. `DPHOLD HOLD <spell> <castCount>` marks the next cast of that spell (first rank, same castCount - HandleCastSpellOpcode sets
   `spell->m_cast_count = castCount` before `prepare`, SpellHandler.cpp 550-551). `AllSpellScript::OnSpellPrepare` (called at the
   end of Spell::prepare, Spell.cpp 3804) takes that spell over if it is the player's current generic spell, still preparing,
   with a cast time > 0, not triggered, not a channel / auto-repeat / next-swing ability. Answer `DPHOLD ARMED`.
4. `UnitScript::OnUnitUpdate` runs at the top of `Unit::Update`, **before** `WorldObject::Update -> m_Events.Update(p_time)` ->
   `SpellEvent::Execute` -> `Spell::update(p_time)` (Unit.cpp 510-517, Object.cpp 1077-1079, Spell.cpp 8491-8495) with the same
   time step. `Spell::update` fires a preparing spell when `m_timer` reaches 0 (Spell.cpp 4556-4567). When
   `GetCastTimeRemaining() <= diff` this update would fire it, so the module calls the public `Spell::ReSetTimer()` (Spell.h 565)
   instead and sends `DPHOLD READY`; it calls `ReSetTimer()` on every update after that, so the timer never runs out.
5. The push: `DPHOLD GO <spell> <castCount>`. On the next update the module calls `Spell::cast(false)` - exactly what
   `Spell::update` does when the timer runs out for a spell with a cast time (`cast(!m_casttime)`), inside `Unit::Update` as
   usual. From here on it is a normal cast: range, line of sight and power are checked again (`CheckCast`), then SMSG_SPELL_GO.
6. Answers go only to that player: `ChatHandler::BuildChatPacket` (ObjectGuid overload, not a GM message) + `SendDirectMessage`
   = SMSG_MESSAGECHAT, whisper, LANG_ADDON, from the player to the player. The real WoW client never shows addon messages; the
   Dreamport reads them and never shows them either.

Threads: the chat hook runs in the world thread (World::UpdateSessions), the spell hooks in the map threads (Map::Update). The
world updates all sessions first, then all maps, and waits for the maps, so the two never run at the same time.

## The messages (TAB-separated, protocol 1)

| Direction | Message | Meaning |
|---|---|---|
| client -> realm | `DPHOLD HELLO 1` | is this realm holding casts? |
| client -> realm | `DPHOLD HOLD <spellId> <castCount>` | hold the cast that CMSG_CAST_SPELL with this castCount starts (used within 5 s, ours) |
| client -> realm | `DPHOLD GO <spellId> <castCount>` | the push: fire it now (before its end: just do not hold it) |
| client -> realm | `DPHOLD BONUS <spellId> <castCount>` | stage S1: the cast CMSG_CAST_SPELL with this castCount sends next was released by Immersive casting without a hold (an instant, a channel, or a cast held on the client) - +10 % damage for it |
| client -> realm | CMSG_CANCEL_CAST (normal) | cancel it |
| realm -> client | `DPHOLD HI 1 30000 10` | the module is installed (protocol, hold time-out in ms, immersive damage bonus in %) |
| realm -> client | `DPHOLD ARMED <spellId> <castCount> <castMs>` | the cast started and will be held |
| realm -> client | `DPHOLD READY <spellId> <castCount>` | the cast time is over: held until GO (the client vibrates here) |
| realm -> client | `DPHOLD EXPIRED <spellId> <castCount>` | 30 s without GO: cancelled |
| realm -> client | `DPHOLD NOHOLD <spellId> <castCount> <why>` | not held (`instant` on the server - a proc or a talent, `late`, `unknown spell`) |

Without the module a stock AzerothCore simply delivers the whisper back to you (an addon message, never shown). The Dreamport
recognises its own `HELLO` coming back (or no answer within 5 s) and then holds immersive casts on your side before sending
them (the stage O2 behaviour), so Immersive casting keeps working on any realm.

## The immersive damage bonus (stage S1 - a Dreamport house rule of your realm)

The user's rule: "give immersive casting a 10% buff to damage, as it can be slower". A spell a VR player **releases with
Immersive casting** deals **+10 % damage** (`ImmersiveDamageBonus = 0.10`, ours; the client uses the same constant):

- a held cast released by the push (`DPHOLD GO`), and a spell cast through Immersive casting without a hold (an instant, a
  channel, or a cast the client held itself before the module's answer came) - the client sends `DPHOLD BONUS` right before
  that one `CMSG_CAST_SPELL`;
- **damage only** (direct hits and the periodic damage of the aura that cast applied), never healing;
- only that cast: a later cast of the same spell without the flag, or its refresh of the aura, is stock again;
- only the player who flagged it. Normal WoW clients and desktop Dreamport players never send `DPHOLD HOLD` / `BONUS`, so
  nothing changes for them. The Dreamport shows the rule as a buff "Immersive Mode" on the character; normal WoW clients do
  not see that buff (it is drawn by the Dreamport only).

| What | AzerothCore master (read 2026-09-27) |
|---|---|
| Which cast | `PlayerScript::OnPlayerSpellCast(Player*, Spell*, bool)` (PlayerScript.h 349) runs in `Spell::_cast` "before the spell is actually executed" (Spell.cpp 3941-3944). `AllSpellScript::OnSpellCast` runs after `handle_immediate` (4155 / 4208), too late for an instant. For an instant `Spell::prepare` calls `cast(true)` before `OnSpellPrepare` (3796-3804), so `BONUS` is matched by first rank + `m_cast_count` at `OnPlayerSpellCast` (like `HOLD`). |
| Direct damage | `UnitScript::ModifySpellDamageTaken(target, attacker, int32& damage, spellInfo)` from `Unit::CalculateSpellDamageTaken` (Unit.cpp 1517-1518), used for every spell hit (Spell.cpp 2906). Counted for 6 s (ours) after the released cast fires, for that spell from that player (a missile may still be in flight). |
| Periodic damage | `UnitScript::ModifyPeriodicDamageAurasTick(target, attacker, uint32& damage, spellInfo)` (SpellAuraEffects.cpp 6341 damage / damage percent, 6454 leech). The same hook runs for periodic **heals** (6659): spells with a periodic heal are never touched. The aura is the one `OnAuraApply` (Unit.cpp 4437 / 4578) saw the released cast apply; each tick checks `Unit::GetAura(spellId, casterGUID)` (Unit.h 1447) is that aura with the same `GetApplyTime()` (SpellAuras.h 129). |

Limits: damage of spells the cast triggers under another spell id (Arcane Missiles' missiles, procs) is not counted; an
unflagged cast of the same spell by the same player within the 6 s drops the bonus of the flagged one still in flight (never
the other way round); a spell whose aura both damages and heals is not boosted.

## Honest limits

- Any client that knows these messages could hold its own casts (at most 30 s, only its own, still a cast in progress). It can
  never touch another player's casts. On your private realm that is the trade-off you chose.
- A cast shorter than one server update (a few tens of ms) cannot be held; very short casts under heavy lag may fire by
  themselves - they are then just normal casts.
- The spell always goes to the target it was started on (as any WoW cast in progress).
- Written without a compiler for AzerothCore at hand (checked against stub headers of the exact declarations); the first
  compile on the server is its real test. `setup-hold.sh` prints the compiler errors if there are any.
