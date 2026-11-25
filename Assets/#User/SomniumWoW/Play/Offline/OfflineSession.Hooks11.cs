// iteration 11 - owner w0play (wave 0, PLAN11 s4): the partial-method declarations the iteration-11 offline hooks call.
//
// RULE for the owners: each line below is a classic C# partial method (void, private). Implement it in YOUR file as
//     partial void Name(same parameter types) { ... }
// never as a plain `void Name(...)` and only ONCE in the whole class. Until an owner implements one, the compiler removes its calls, so
// wave 0 is inert. Non-void hooks are real stubs in the callee owner's file (see P/reports11/w0play.md). Frozen after wave 0.
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        // ---- in w0play's files (applied in wave 0)
        partial void TickMotion(float dt);                    // motion  17d   OfflineSession.cs Step, after StepIter4 (OfflineSession.Motion.cs)
        partial void TickStates(float dt);                    // states  2026-10-04: OfflineSession.cs Step, after TickMotion (OfflineSession.States.cs)
        partial void TrOfflineTick(float dt);                 // trainer F2    OfflineSession.Stats.cs, after the walk-away lines (OfflineSession.Trainer.cs)
        partial void TrOfflineLevelChanged();                 // trainer F4    OfflineSession.Stats.cs LevelUp, after RefreshMarkers

        // ---- void hooks other owners apply into THEIR files, calling another owner
        partial void RefreshAvatarLook(PlayerState p);        // avatar  <- npc OfflineSession.Items.cs, after `p.Stealthed = ...` (OfflineSession.Avatar.cs)
        partial void AbFormBarFirstFill();                    // vrcast  <- npc OfflineSession.Items.cs:403, after `p.ShapeshiftForm = _form` (OfflineSession.ActionBars.cs)
        partial void TrOfflineAfterDialog(OfflineCreature c); // trainer <- quests OfflineSession.Quests.cs:37 (F1 fallback, after OpenDialog(c))
        partial void TrOfflineAddOption(OfflineCreature npc, NpcDialog d);   // trainer <- npc OfflineSession.Vendor.cs (F3, after the vendor option)
        partial void OfflineChargeMove(OfflineCreature c, SpellInfo s);                         // motion <- spellfx OfflineSession.Spells.cs ApplyEffect Charge (17b)
        partial void OfflineTeleportBehind(OfflineCreature c);                                  // motion <- spellfx Spells.cs TeleportUnits TargetB 65 (17b)
        partial void OfflineLeap(SpellInfo s, SpellEffectInfo e);                               // motion <- spellfx Spells.cs effect 29 (17b)
        partial void OfflineLeapBack(SpellInfo s, SpellEffectInfo e);                           // motion <- spellfx Spells.cs effect 138 (17b)
        partial void OfflineJumpDest(OfflineCreature c, SpellInfo s, SpellEffectInfo e);        // motion <- spellfx Spells.cs effect 42 (17b)
        partial void OfflineKnockbackFrom(OfflineUnit caster, SpellEffectInfo e);               // motion <- spellfx OfflineSession.UnitSpells.cs effect 98 on the player (17c)
    }
}
