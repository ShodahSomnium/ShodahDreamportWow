using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>An aura on a unit (from a spell's APPLY_AURA effects, a form's passive spells, or an item).</summary>
    internal sealed class OfflineAura
    {
        public SpellInfo Spell;
        public ulong Caster;
        public float Remaining;            // seconds, < 0 = permanent
        public float Duration;             // seconds, 0 = permanent
        public int Stacks = 1;
        public bool Harmful;
        public bool Hidden;                // passive form spells: not shown in the aura list
        public bool FromForm;              // removed when the form ends
        public int[] Amount = new int[3];  // per effect: modifier amount / periodic tick value / absorb pool
        public bool[] Active = { true, true, true };   // false for effects of the spell that target someone else
        public float[] TickTimer = new float[3];
        public int[] TickBonus = new int[3];   // extra per tick (Rend weapon part, Serpent Sting RAP part)
        public AuraView View;
        public int DamageTaken;            // damage taken while this aura is on (fear/root break threshold) - iteration 3
        public int Charges = -1;           // proc charges left (Lightning Shield balls), -1 = not started - iteration 3
        public bool Immersive;             // stage S1 (PLAN10 s5): applied by a cast released with Immersive casting - its periodic damage +10 %

        public bool Has(int aura, out SpellEffectInfo eff)
        {
            for (int i = 0; i < 3; i++)
            {
                var e = Spell.Effects[i];
                if (e != null && e.IsAura && e.Aura == aura) { eff = e; return true; }
            }
            eff = null;
            return false;
        }
    }

    /// <summary>What every unit (the player and each creature) has for combat.</summary>
    internal class OfflineUnit
    {
        public ulong Guid;
        public int Level = 1;
        public int Health = 1, MaxHealth = 1;
        public WowPos Pos;
        public bool Dead;
        public float CombatReach = 1.5f;
        public readonly List<OfflineAura> Auras = new List<OfflineAura>();
        // iteration 3
        public int CreatureType;           // CreatureType.dbc id (0 unknown)
        public int Mana, MaxMana;          // creatures/pets that cast
        public float ManaFrac;
        public SpellInfo Casting;          // non-player units: the spell being cast (the player uses the session fields)
        public ulong CastTargetGuid;
        public float CastElapsed, CastTotal;

        public virtual bool IsPlayer { get { return false; } }

        /// <summary>Sum of an aura type's amounts (optionally only effects whose MiscValue has a bit of miscMask; miscMask 0 = all).</summary>
        public int AuraSum(int aura, int miscMask)
        {
            int sum = 0;
            for (int a = 0; a < Auras.Count; a++)
            {
                var au = Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e == null || !e.IsAura || e.Aura != aura || !au.Active[i]) continue;
                    if (miscMask != 0 && (e.MiscValue & miscMask) == 0) continue;
                    sum += au.Amount[i] * au.Stacks;
                }
            }
            return sum;
        }

        /// <summary>Sum for auras whose MiscValue equals value (stat index etc.; -1 in the aura means "all").</summary>
        public int AuraSumMisc(int aura, int value)
        {
            int sum = 0;
            for (int a = 0; a < Auras.Count; a++)
            {
                var au = Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e == null || !e.IsAura || e.Aura != aura || !au.Active[i]) continue;
                    if (e.MiscValue != value && e.MiscValue != -1) continue;
                    sum += au.Amount[i] * au.Stacks;
                }
            }
            return sum;
        }

        /// <summary>Product of (100 + amount)/100 for percent auras (e.g. MOD_MELEE_HASTE, MOD_DAMAGE_PERCENT_TAKEN).</summary>
        public float AuraMultiplier(int aura, int miscMask)
        {
            float m = 1f;
            for (int a = 0; a < Auras.Count; a++)
            {
                var au = Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e == null || !e.IsAura || e.Aura != aura || !au.Active[i]) continue;
                    if (miscMask != 0 && e.MiscValue != 0 && (e.MiscValue & miscMask) == 0) continue;
                    m *= (100f + au.Amount[i] * au.Stacks) / 100f;
                }
            }
            return m;
        }

        public bool HasAuraType(int aura)
        {
            for (int a = 0; a < Auras.Count; a++)
            {
                var au = Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e != null && e.IsAura && e.Aura == aura && au.Active[i]) return true;
                }
            }
            return false;
        }

        public OfflineAura FindAura(int spellId)
        {
            for (int a = 0; a < Auras.Count; a++) if (Auras[a].Spell.Id == spellId) return Auras[a];
            return null;
        }

        public bool Stunned { get { return HasAuraType(SpellAura.ModStun); } }
        public bool Rooted { get { return HasAuraType(SpellAura.ModRoot); } }
        // iteration 3
        public bool Feared { get { return HasAuraType(SpellAura.ModFear); } }
        public bool Confused { get { return HasAuraType(SpellAura.ModConfuse); } }
        public bool Silenced { get { return HasAuraType(SpellAura.ModSilence); } }
        public bool Pacified { get { return HasAuraType(SpellAura.ModPacify); } }
        /// <summary>Stunned, feared or polymorphed: no attacks, no casts.</summary>
        public bool Incapacitated { get { return Stunned || Feared || Confused; } }
    }

    internal enum AiState : byte { Idle, Wander, Chase, Attack, Evade, Dead, Despawned }

    /// <summary>What a creature entry of the session is: a zone creature/NPC, the player's pet or one of the player's totems (iteration 3).</summary>
    internal enum SummonKind : byte { None = 0, Pet = 1, Totem = 2 }

    /// <summary>A creature or NPC of the zone (one per SpawnDef).</summary>
    internal sealed class OfflineCreature : OfflineUnit
    {
        public EntityState State;
        public CreatureTemplate Tpl;
        public SpawnDef Spawn;
        public int SpawnIndex;
        public AiState Ai = AiState.Idle;
        public WowPos Home;
        public bool Present = true;        // false while despawned (between corpse removal and respawn)

        // AI
        public float WaitTimer;            // idle pause before the next wander
        public WowPos MoveTarget;
        public bool HasMoveTarget;
        public float AssistDelay = -1f;    // > 0: joins the fight when it reaches 0
        public float LeashTimer;           // seconds chasing without landing a swing (anti-stuck)
        public float EvadeSafety;

        // combat
        public int Armor;
        public float MinDamage, MaxDamage, AttackSpeed = 2f;   // MinDamage/MaxDamage: display values without auras
        public float DamageBase, BaseAttackPower, DamageMod = 1f;
        public float SwingTimer;
        public bool Engaged;               // in combat with the player
        public bool CanParry;
        public bool Elite;

        // death / loot
        public float CorpseTimer, RespawnTimer;
        public bool LootGenerated, GaveXp, DamagedByPlayer;
        public int LootMoney;
        public readonly List<ItemStack> LootItems = new List<ItemStack>();

        // iteration 3: threat, crowd control, casting, summons
        public readonly Dictionary<ulong, float> Threat = new Dictionary<ulong, float>();   // attacker guid -> threat (player, pet, totems)
        public ulong ForcedTarget;         // taunt: attacks this unit while ForcedTimer > 0
        public float ForcedTimer;
        public float SpellTimer;           // seconds to the next cast of a template spell
        public int SpellIndex;             // rotation through Tpl.Spells
        public float FleeTimer;            // fear: seconds to the next direction change
        public int BaseDisplayId;          // DisplayId without Polymorph
        public SummonKind Summon;          // None for zone creatures
        public ulong Owner;                // pets/totems: the player
        public int TotemSlot;              // SummonProperties.Slot (1 fire, 2 earth, 3 water, 4 air)
        public float SummonRemaining;      // seconds left (totems), < 0 = until dismissed/killed (pets)
        public SpellInfo SummonSpell;      // the spell that summoned it
        public SpellInfo TotemSpell;       // aura / pulse / attack spell of a totem
        public float TotemTimer;           // seconds to the next pulse/attack
        public readonly List<SpellInfo> PetSpells = new List<SpellInfo>();   // pet abilities known at its level (Firebolt, Blood Pact, Torment)
        public float PetRegenTimer;
        public float DeadRemoveTimer;      // summons: seconds a dead pet/totem stays visible
    }
}
