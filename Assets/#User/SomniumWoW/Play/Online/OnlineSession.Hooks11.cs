// iteration 11 - owner w0play (wave 0, PLAN11 s4): the partial-method declarations every iteration-11 hook calls.
//
// RULE for the owners: each line below is a classic C# partial method (void, private). Implement it in YOUR file as
//     partial void Name(same parameter types) { ... }
// never as a plain `void Name(...)` (CS0111 / CS0759) and only ONCE in the whole class. Until an owner implements one, the compiler removes
// its calls, so wave 0 is inert. Non-void hooks are not here: they are real stubs in the callee owner's file (see P/reports11/w0play.md).
// This file is frozen after wave 0 like the contract files: a new declaration is a request to the integrator.

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        // ---- Init* (OnlineSession.cs constructor, after InitGroup, before world.Release) / Tick* (Tick, after TickGroup; dt = o2dt) ----
        partial void InitNpc();                       // npc       OnlineSession.Npc.cs (the RegisterNpcService registry is field-initialized: order-free)
        partial void InitVendor();                    // npc       OnlineSession.Vendor.cs
        partial void InitBank();                      // npc       OnlineSession.Bank.cs
        partial void InitItemText();                  // npc       OnlineSession.ItemText.cs
        partial void TickNpc(float dt);               // npc
        partial void TickVendor(float dt);            // npc
        partial void TickBank(float dt);              // npc
        partial void InitTrainer();                   // trainer   OnlineSession.Trainer.cs
        partial void InitProfession();                // trainer   OnlineSession.Profession.cs
        partial void TickTrainer(float dt);           // trainer
        partial void TickProfession(float dt);        // trainer
        partial void InitInstance();                  // world     OnlineSession.Instance.cs
        partial void TickInstance(float dt);          // world
        partial void InitInteract();                  // interact  OnlineSession.Interact.cs (calls its own InitTrade / InitDuel / ... and queues' three below)
        partial void TickInteract(float dt);          // interact
        partial void InitAuction();                   // queues    OnlineSession.Auction.cs      (called by interact's InitInteract)
        partial void InitBattleground();              // queues    OnlineSession.Battleground.cs (called by interact's InitInteract)
        partial void InitLfg();                       // queues    OnlineSession.Lfg.cs          (called by interact's InitInteract)
        partial void TickAuction(float dt);           // queues    (called by interact's TickInteract)
        partial void TickBattleground(float dt);      // queues    (called by interact's TickInteract)
        partial void TickLfg(float dt);               // queues    (called by interact's TickInteract)
        partial void InitTaxi();                      // motion    OnlineSession.Taxi.cs
        partial void InitForced();                    // motion    OnlineSession.Forced.cs
        partial void InitTransport();                 // motion    OnlineSession.Transport.cs
        partial void TickTaxi(float dt);              // motion
        partial void TickForced(float dt);            // motion
        partial void TickTransport(float dt);         // motion
        partial void InitPets();                      // pets      OnlineSession.Pets.cs
        partial void TickPets(float dt);              // pets
        partial void InitVehicle();                   // vehicle   OnlineSession.Vehicle.cs
        partial void TickVehicle(float dt);           // vehicle
        partial void InitTalents();                   // talents   OnlineSession.Talents.cs
        partial void TickTalents(float dt);           // talents
        partial void InitReputation();                // character OnlineSession.Reputation.cs
        partial void InitSocial();                    // social    OnlineSession.Social.cs
        partial void InitGuild();                     // social    OnlineSession.Guild.cs
        partial void InitMail();                      // social    OnlineSession.Mail.cs
        partial void TickSocial(float dt);            // social

        // ---- hooks in OnlineSession.Objects.cs (applied in wave 0) ----
        partial void DynCreated(OnlineObject o);                              // spellfx  Create, a TYPEID_DYNAMICOBJECT after Refresh
        partial void DynRemoved(OnlineObject o);                              // spellfx  Remove, after _objects.Remove
        partial void DynUpdated(OnlineObject o);                              // spellfx  ApplyBlock Values, after Refresh
        partial void DynClear();                                              // spellfx  ClearWorld
        partial void SpFillUnitExtras(OnlineObject o, EntityState s);        // spellfx  Refresh, units / players, last line
        partial void PfRefreshSkinnable(OnlineObject o, EntityState s, uint unitFlags);   // trainer O4  Refresh after Lootable
        partial void PfReadSkills(OnlineObject o);                            // trainer O5  RefreshSelf after ReadSkills
        partial void RefreshSelfMotion(OnlineObject o);                       // motion   RefreshSelf next to UpdateSelfMount (Forced.cs)
        partial void NoteTransport(OnlineObject o);                           // motion   RefreshGameObject, after the EntityState is filled (Transport.cs)
        partial void PetsRefreshSelf(OnlineObject o);                         // pets     RefreshSelf next to UpdateSelfMount
        partial void RefreshSelfPanes(OnlineObject o);                        // talents  RefreshSelf, last line

        // ---- void hooks other owners apply into THEIR files, calling another owner (declared here so both sides compile in any order) ----
        partial void FlushFacing(ulong targetGuid);                           // motion  <- spellfx Combat.cs StartAttack / Spells.cs SpCast
        partial void NoteOwnCast(int spellId);                                // motion  <- spellfx Spells.cs SpCast
        partial void PfRecipesChanged();                                      // trainer <- spellfx Spells.cs RebuildSpells (S1)
        partial void PfStopQueue(string why);                                 // trainer <- spellfx Spells.cs CancelCast (S4)
        partial void PfUseBobber(EntityState e);                              // trainer <- quests Quests.cs QUseGameObject case 17 (Q2)
        partial void TrSendList(ulong guid);                                  // trainer <- quests Quests.cs Interact (Q1 fallback) / its own registry lambda
        partial void NpcReadObjectText(EntityState e);                        // npc     <- quests Quests.cs QUseGameObject case 9 (H3)
        partial void PetsEnterPlayerVehicle(ulong guid);                      // pets    <- quests Quests.cs Interact (H4b)
        partial void VehOnSelfSpline(ulong tGuid, sbyte tSeat, SplinePath s);                       // vehicle <- motion Movement.cs (pets H3e)
        partial void VehOnPassengerSpline(OnlineObject o, ulong tGuid, sbyte tSeat, SplinePath s);  // vehicle <- motion Movement.cs (pets H3f)
        partial void VehOnPassengerInfo(OnlineObject o, MovementInfo mi);                           // vehicle <- motion Movement.cs (pets H3f / g)
        partial void VehAddSeat(MovementInfo mi);                                                    // vehicle <- motion Movement.cs (pets H3b)
        partial void VehMoverSpeed(int type, float speed);                                           // vehicle <- motion Movement.cs (pets H3d)
    }
}
