using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// What the local character is doing, as avatar motions (WowMotion one-shots + a WowHold), from the session:
    /// - a main-hand swing or an Auto Shot = PlayerState.AttackCounter stepping (Rules: +1 per swing/shot);
    /// - an off-hand swing = a white melee result (MeleeHit/Miss/Dodge/Parry/Block, SpellId 0, source the player) beyond the
    ///   main-hand steps of the same frame, while dual wielding (Rules: off-hand swings emit events but do not step the counter);
    /// - the weapon picks the motion from its Item.dbc class/subclass (read from the client's Item.dbc like the Rules do:
    ///   15 dagger stab; 1/5/6/8/10/17 two-hand chop and sweep in turn; 13 fist and no weapon: punches; other weapons
    ///   one-hand slash; ranged 2 bow, 3/18 gun/crossbow, 16 thrown, 19 wand);
    /// - spells from the player's spellbook (CastSucceeded, source the player): Spell.dbc DmgClass 2 = the melee swing
    ///   (a shield ability, EquippedItemClass 4 = shield bash), 3 = the ranged shot, else a spell motion (release after a cast
    ///   time, a quick throw for instants, both arms raised for spells on yourself or a friend); channels and on-next-swing
    ///   abilities do not add a one-shot (the hold / the swing shows them);
    /// - holds: casting (CastingSpellId, not a channel), channelling (a channel at a target; at yourself: the casting pose),
    ///   aiming a bow or gun while Auto Shot is on (AttackStarted/AttackStopped with SpellId 75).
    /// The motions go to the local avatar's GearPoser (desktop only) and into our WowNet record for everyone else.
    /// </summary>
    public sealed class GearAnimLocal
    {
        public WowMotion Motion { get; private set; }
        public int Counter { get; private set; }
        public WowHold Hold { get; private set; }
        /// <summary>Iteration 11 (combat feel): GearLocal put the drawn weapons away (a ranged cast, the mount) - no weapon stance loop.</summary>
        public bool WeaponsAway;

        /// <summary>Iteration 11 (avatar): the same moments as WoW AnimationData ids (GearWowAnim rules) - the local puppet / form model
        /// play them and they go into our record (int 32). Kept also in VR while a creature look is shown (the others' bear swipes).</summary>
        public readonly GearWowAnim Wow = new GearWowAnim();

        private int _lastAttackCounter = -1;
        private int _whites;
        private bool _autoShot;
        private int _alt;
        private DbcTable _items;
        private WowFiles _itemsFor;
        private readonly Dictionary<int, int> _classOf = new Dictionary<int, int>();
        // iteration 11
        private int _lastWound = -1;
        private int _defence;            // a parry / dodge / block of ours this frame (its WoW id), 0 none
        private int _emoteLoop;
        private float _emoteAt;
        private bool _wowOn;
        private bool _creature;
        // states fix 2026-10-04: the body state GearLocal's GearStatePose decided this frame (set before Tick) and the eat / drink clock
        public GearBodyState Body;
        public const float EatEvery = 8f;   // ours: s between two 61 EmoteEat one-shots while eating / drinking (WoW's Drink kit plays it once at the start)
        private float _eatAt = -1f;

        public void Clear()
        {
            _lastAttackCounter = -1;
            _whites = 0;
            _autoShot = false;
            Hold = WowHold.None;
            _lastWound = -1;
            _defence = 0;
            _emoteLoop = 0;
            _eatAt = -1f;
            Wow.Clear();
        }

        /// <summary>WowGear.PlayAvatarEmote: an emote animation (69 EmoteDance loops until the avatar moves; others play once).</summary>
        public bool Emote(int animId, bool allowed)
        {
            if (!allowed || animId <= 0 || animId > WowNetWire.MaxAnimId) return false;
            if (animId == GearWowAnim.EmoteDance) { _emoteLoop = animId; _emoteAt = UnityEngine.Time.time; }
            else Wow.Start(animId, UnityEngine.Time.time, false);
            return true;
        }

        /// <summary>Collects this frame's events (WowPlay.Event); the motions start in Tick.</summary>
        public void OnEvent(IWowSession s, GameEvent e, WowFiles files, GearPoser poser, bool animate)
        {
            PlayerState p = s != null ? s.Player : null;
            if (p == null) return;
            switch (e.Type)
            {
                case GameEventType.MeleeHit: case GameEventType.MeleeMiss: case GameEventType.MeleeDodge:
                case GameEventType.MeleeParry: case GameEventType.MeleeBlock:
                    if (e.SourceGuid == p.Guid && e.SpellId == 0) _whites++;
                    // iteration 11: we parried / dodged / blocked a blow aimed at us (the WoW defence animations)
                    if (e.TargetGuid == p.Guid && e.SourceGuid != p.Guid && p.Guid != 0)
                    {
                        if (e.Type == GameEventType.MeleeParry) _defence = GearWowAnim.Parry(Subclass(p, files, 15));
                        else if (e.Type == GameEventType.MeleeDodge) _defence = GearWowAnim.Dodge;
                        else if (e.Type == GameEventType.MeleeBlock) { ItemView off = Equip(p, 16); _defence = off != null && off.InventoryType == 14 ? GearWowAnim.ShieldBlock : GearWowAnim.Block; }
                    }
                    break;
                case GameEventType.AttackStarted:
                    if (e.SourceGuid == p.Guid && e.SpellId == 75) _autoShot = true;
                    break;
                case GameEventType.AttackStopped:
                    if (e.SourceGuid == p.Guid && e.SpellId == 75) _autoShot = false;
                    break;
                case GameEventType.PlayerDied:
                    _autoShot = false;
                    break;
                case GameEventType.CastSucceeded:
                    if (e.SourceGuid == p.Guid) OnCast(s, p, e, files, poser, animate);
                    break;
            }
        }

        private void OnCast(IWowSession s, PlayerState p, GameEvent e, WowFiles files, GearPoser poser, bool animate)
        {
            // lead fix 2026-10-12 (the user: "add the same animations, such as throwing the dynamite"): an item's spell is in no spellbook - one
            // of ours aimed at a place (Rough Dynamite 4054: precast 108 ReadyThrown, cast 107 AttackThrown, harness ThrowAnim) is ours too
            if (e.SpellId == 75 || (!InSpellbook(p, e.SpellId) && !(e.AtDest && e.SourceGuid == p.Guid))) return;   // Auto Shot is the counter; triggered spells are not ours
            if (p.Channeling && e.SpellId != p.CastingSpellId) return;          // a channel's ticks (Arcane Missiles 7268)
            SpellInfo sp = files != null ? SpellDb.Get(files).Find(e.SpellId) : null;
            if (sp != null && sp.IsChanneled && !sp.IsPassive && _wowOn)
            {
                // states fix 2026-10-04: a channel's CAST kit plays once as it starts (SPELL_GO), then the CHANNEL kit loops - Fishing 7620:
                // CAST kit -1/133 FishingCast, CHANNEL kit -1/134 FishingLoop (measured, harness StateAnims)
                SpellVisualDb.Visual cv = VisualOf(files, sp.Id);
                int ck = cv != null && cv.Cast != null ? (cv.Cast.Anim > 0 ? cv.Cast.Anim : cv.Cast.StartAnim) : -1;
                if (ck > 0) Wow.Start(_creature ? ck : GearWowAnim.Adapt(ck, Subclass(p, files, 15)), UnityEngine.Time.time, GearWowAnim.IsAttack(ck));
            }
            if (sp == null || sp.IsPassive || sp.IsChanneled) return;
            // iteration 11: the WoW id of this release (on-next-swing abilities too: Heroic Strike / Maul play their kit when they fire)
            if (_wowOn)
            {
                int id = WowCastId(s, p, sp, e, files);
                if (id > 0) Wow.Start(id, UnityEngine.Time.time, GearWowAnim.IsAttack(id) || sp.DmgClass == 2 || sp.DmgClass == 3);
            }
            if (sp.OnNextSwing) return;
            WowMotion m;
            if (sp.DmgClass == 2) m = sp.EquippedItemClass == 4 ? WowMotion.ShieldBash : MeleeMotion(p, files, false);
            else if (sp.DmgClass == 3) m = RangedMotion(p, files);
            else if (OnFriend(s, p, e.TargetGuid)) m = WowMotion.CastOmni;
            else m = sp.CastTimeMs > 0 ? WowMotion.CastRelease : WowMotion.CastInstant;
            Start(m, poser, animate);
        }

        /// <summary>Steps the swings of this frame and the hold. Call once per frame after the events. Iteration 11: wow = the WoW ids
        /// are wanted (desktop animations on, or a creature look - VR too); creature = the local look is a creature (claws, its own
        /// cast loops); speed = the avatar's planar speed (m/s: an emote loop ends when it moves).</summary>
        public void Tick(IWowSession s, WowFiles files, GearPoser poser, bool animate, float now, bool wow, bool creature, float speed)
        {
            PlayerState p = s != null ? s.Player : null;
            if (p == null) { Clear(); return; }
            if (p.Dead || p.Sheath != SheathState.Ranged) _autoShot = false;
            _wowOn = wow;
            _creature = creature;
            float t = UnityEngine.Time.time;

            int main = 0;
            if (_lastAttackCounter >= 0 && p.AttackCounter != _lastAttackCounter)
            {
                int steps = Math.Min(2, Math.Max(1, p.AttackCounter - _lastAttackCounter));
                bool ranged = p.Sheath == SheathState.Ranged;
                for (int i = 0; i < steps; i++) Start(ranged ? RangedMotion(p, files) : MeleeMotion(p, files, false), poser, animate);
                if (!ranged) main = steps;
                // iteration 11: the WoW swing / shot (an ability kit that fired this instant - Heroic Strike, Maul - is the swing itself)
                if (wow && !(Wow.ShotIsAttack && t - Wow.ShotAt < 0.25f && Wow.OneShot != GearWowAnim.Unarmed))
                {
                    int id = ranged ? GearWowAnim.Shot(Subclass(p, files, 17), InvType(p, 17)) : creature ? GearWowAnim.Unarmed : GearWowAnim.Swing(Subclass(p, files, 15), false);
                    Wow.Start(id, t, true);
                }
            }
            _lastAttackCounter = p.AttackCounter;
            if (_whites > main && p.Sheath == SheathState.Melee && DualWield(p, files))
            {
                Start(MeleeMotion(p, files, true), poser, animate);
                if (wow) Wow.Start(creature ? GearWowAnim.Unarmed : GearWowAnim.Swing(Subclass(p, files, 16), true), t, true);
            }
            _whites = 0;
            // iteration 11: wounds, our parries / dodges / blocks, the WoW loop
            if (_lastWound >= 0 && p.WoundCounter != _lastWound && wow && !p.Dead) Wow.Wound(t);
            _lastWound = p.WoundCounter;
            if (_defence != 0) { if (wow && !p.Dead) Wow.Start(_defence, t, false); _defence = 0; }
            if (_emoteLoop != 0 && (speed > 0.3f || p.Dead || p.InCombat || p.CastingSpellId != 0)) _emoteLoop = 0;
            Wow.SetLoop(wow ? WowLoop(s, p, files, creature) : StateLoop(p));
            // states fix 2026-10-04: eating / drinking = 61 EmoteEat on the sitting body at the start (WoW's Drink 430 CAST kit, measured)
            // and every EatEvery s (ours) while the food / drink lasts
            if (wow && !creature && (Body == GearBodyState.Eat || Body == GearBodyState.Drink))
            {
                if (_eatAt < 0f || t - _eatAt >= EatEvery) { Wow.Start(GearWowAnim.EmoteEat, t, false); _eatAt = t; }
            }
            else _eatAt = -1f;

            WowHold h = WowHold.None;
            if (!p.Dead)
            {
                if (p.CastingSpellId != 0)
                    h = p.Channeling && !OnFriend(s, p, p.CastTargetGuid) ? WowHold.Channel : WowHold.Cast;
                else if (_autoShot)
                {
                    int sub = Subclass(p, files, 17);
                    h = sub == 2 ? WowHold.AimBow : (sub == 3 || sub == 18) ? WowHold.AimGun : WowHold.None;
                }
            }
            if (!animate) h = WowHold.None;
            Hold = h;
            if (poser != null) poser.SetHold(h);
        }

        private void Start(WowMotion m, GearPoser poser, bool animate)
        {
            if (m == WowMotion.None || !animate) return;
            Motion = m;
            Counter = (Counter + 1) & 0xFF;
            if (poser != null) poser.Play(m, UnityEngine.Time.time);
        }

        // ------------------------------------------------------------------------------------------------ choices

        // ------------------------------------------------------------------ iteration 11: the WoW ids (proposal-avatar.md 6.C)

        /// <summary>
        /// The held loop: a forced move's gait (motion hook: WowForcedMove.Gait); dead -> 0; casting -> the spell's PRECAST kit Anim,
        /// else 51 at an enemy / 52 at yourself or a friend; channelling -> the CHANNEL kit Anim, else 124 / 125; Auto Shot -> the aim
        /// of the ranged weapon; in combat with melee drawn -> the main hand's stance (not for creature looks: their viewers stand them
        /// in their own Ready); an emote loop while standing; else 0 (the client's own animation).
        /// </summary>
        private int WowLoop(IWowSession s, PlayerState p, WowFiles files, bool creature)
        {
            if (p.Dead) return p.Ghost ? 0 : GearWowAnim.Death;   // states fix 2026-10-04: WoW holds 1 Death until Release Spirit (was 0)
            if (WowForcedMove.Gait != 0) return WowForcedMove.Gait;
            if (!creature && WowSwim.Active) return GearWowAnim.SwimLoopNow();   // lead 2026-10-26: swimming (forms swim through their ride gait)
            if (p.CastingSpellId != 0)
            {
                SpellVisualDb.Visual v = VisualOf(files, p.CastingSpellId);
                bool friend = OnFriend(s, p, p.CastTargetGuid);
                if (p.Channeling)
                {
                    int ch = v != null && v.Channel != null ? v.Channel.Anim : -1;
                    return ch > 0 ? ch : friend ? GearWowAnim.ChannelOmni : GearWowAnim.ChannelDirected;
                }
                int pre = v != null && v.Precast != null ? v.Precast.Anim : -1;
                return pre > 0 ? pre : friend ? GearWowAnim.PrecastOmni : GearWowAnim.PrecastDirected;
            }
            if (_autoShot) return GearWowAnim.Aim(Subclass(p, files, 17));
            int stand = GearWowAnim.StandLoop(p.StandState);   // states fix 2026-10-04: sit 97 / sleep 100 / kneel 115 / chairs 102-104
            if (stand != 0) return stand;
            if (!creature && (p.InCombat || p.AutoAttacking) && p.Sheath == SheathState.Melee && !WeaponsAway) return GearWowAnim.Stance(Subclass(p, files, 15));
            return _emoteLoop;
        }

        /// <summary>States fix 2026-10-04: the loops viewers read even when no WoW ids are computed here (a VR wearer without a creature look):
        /// lying dead 1 Death, the stand states (GearWowAnim.StandLoop). Their avatar's pose itself comes through the Somnium client.</summary>
        private static int StateLoop(PlayerState p)
        {
            if (p.Dead) return p.Ghost ? 0 : GearWowAnim.Death;
            if (WowSwim.Active) return GearWowAnim.SwimLoopNow();   // lead 2026-10-26: a VR swimmer's viewers kick their legs
            return GearWowAnim.StandLoop(p.StandState);
        }

        /// <summary>The one-shot of an own spell's release: its CAST kit's Anim, else its StartAnim; else (DmgClass 2) the swing, (3) the
        /// ranged shot; else 53 at an enemy / 54 at yourself or a friend; then WoW's weapon adaptation (GearWowAnim.Adapt). Creature looks:
        /// the kit's own animation (Maul 178, Swipe 176 - the druid bears have them), else 16 for melee, else the cast release.</summary>
        private int WowCastId(IWowSession s, PlayerState p, SpellInfo sp, GameEvent e, WowFiles files)
        {
            SpellVisualDb.Visual v = VisualOf(files, sp.Id);
            int kit = v != null && v.Cast != null ? (v.Cast.Anim > 0 ? v.Cast.Anim : v.Cast.StartAnim) : -1;
            int mainSub = _creature ? -1 : Subclass(p, files, 15);
            if (kit > 0) return _creature ? kit : GearWowAnim.Adapt(kit, mainSub);
            if (sp.DmgClass == 2) return sp.EquippedItemClass == 4 && !_creature ? GearWowAnim.ShieldBash : _creature ? GearWowAnim.Unarmed : GearWowAnim.Swing(mainSub, false);
            if (sp.DmgClass == 3) return GearWowAnim.Shot(Subclass(p, files, 17), InvType(p, 17));
            return OnFriend(s, p, e.TargetGuid) ? GearWowAnim.CastOmni : GearWowAnim.CastDirected;
        }

        private static SpellVisualDb.Visual VisualOf(WowFiles files, int spellId)
        {
            if (files == null || spellId <= 0) return null;
            SpellVisualDb.SpellFx fx = SpellVisualDb.Get(files).Spell(spellId);
            return fx != null ? fx.Visual : null;
        }

        private static int InvType(PlayerState p, int slot)
        {
            ItemView v = Equip(p, slot);
            return v != null ? v.InventoryType : 0;
        }

        private WowMotion MeleeMotion(PlayerState p, WowFiles files, bool offHand)
        {
            bool claws = _creature || p.ShapeshiftForm == 1 || p.ShapeshiftForm == 5;   // cat, bear; iteration 11: any creature look
            int sub = claws ? -1 : Subclass(p, files, offHand ? 16 : 15);
            if (sub < 0 || sub == 13) { if (offHand) return WowMotion.PunchOff; return (_alt++ & 1) == 0 ? WowMotion.Punch : WowMotion.PunchOff; }
            switch (sub)
            {
                case 15: return offHand ? WowMotion.StabOff : WowMotion.Stab;
                case 1: case 5: case 6: case 8: case 10: case 17:
                    return (_alt++ & 1) == 0 ? WowMotion.Swing2H : WowMotion.Swing2HSide;
                default: return offHand ? WowMotion.Slash1HOff : WowMotion.Slash1H;
            }
        }

        private WowMotion RangedMotion(PlayerState p, WowFiles files)
        {
            int sub = Subclass(p, files, 17);
            switch (sub)
            {
                case 2: return WowMotion.BowShot;
                case 3: case 18: return WowMotion.GunShot;
                case 16: return WowMotion.Throw;
                case 19: return WowMotion.WandShot;
            }
            ItemView v = Equip(p, 17);                                           // no Item.dbc row: by InventoryType
            int inv = v != null ? v.InventoryType : 0;
            return inv == 25 ? WowMotion.Throw : inv == 26 ? WowMotion.GunShot : WowMotion.BowShot;
        }

        private bool DualWield(PlayerState p, WowFiles files)
        {
            int cls;
            int sub = Subclass(p, files, 16, out cls);
            return sub >= 0 && cls == 2;
        }

        private static bool OnFriend(IWowSession s, PlayerState p, ulong target)
        {
            if (target == 0 || target == p.Guid) return true;
            EntityState e = s.Find(target);
            return e != null && e.Reaction == Reaction.Friendly;
        }

        private static bool InSpellbook(PlayerState p, int spellId)
        {
            List<AbilityView> book = p.Spellbook;
            if (book == null) return false;
            for (int i = 0; i < book.Count; i++) if (book[i] != null && book[i].SpellId == spellId) return true;
            return false;
        }

        private static ItemView Equip(PlayerState p, int slot)
        {
            return p.Equipment != null && p.Equipment.Length > slot ? p.Equipment[slot] : null;
        }

        private int Subclass(PlayerState p, WowFiles files, int slot) { int cls; return Subclass(p, files, slot, out cls); }

        /// <summary>Item.dbc SubclassID of the item in an equipment slot (weapons only: class 2), -1 when empty or not a weapon.</summary>
        private int Subclass(PlayerState p, WowFiles files, int slot, out int cls)
        {
            cls = -1;
            ItemView v = Equip(p, slot);
            if (v == null || v.Entry <= 0 || v.DisplayId <= 0) return -1;
            if (files != _itemsFor)
            {
                _itemsFor = files;
                _classOf.Clear();
                try { _items = files != null ? Dbc.Load(files, "Item") : null; }
                catch (Exception) { _items = null; }
            }
            int packed;
            if (!_classOf.TryGetValue(v.Entry, out packed))
            {
                packed = -1;
                int r = _items != null ? _items.FindRow(v.Entry) : -1;
                if (r >= 0) packed = (_items.GetInt(r, DbcFields.Item.ClassID) << 8) | (_items.GetInt(r, DbcFields.Item.SubclassID) & 0xFF);
                _classOf[v.Entry] = packed;
            }
            if (packed < 0) return -1;
            cls = packed >> 8;
            return cls == 2 ? packed & 0xFF : -1;
        }
    }
}
