// iteration 11 - owner avatar (proposal-avatar.md 5.3; pets' Hidden = 5 + its Decide rule, proposal-pets.md s5.6 / H8, merged by w0play)
// Pure: no Unity. The Game layer (Game/Play/Forms, WowGear, GearLocal / GearRemote) asks Decide every frame for the local player
// and FromEntity for another player's server copy when their record does not say a look (an older build, online only).
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>What a Somnium avatar shows instead of / on top of itself (on the wire: WowNetWire int 31 bits 16-19).
    /// Hidden (pets H8): a vehicle seat that hides its passenger, or possessing a unit - no avatar, no model.</summary>
    public enum AvatarLookKind : byte { Own = 0, Creature = 1, GhostShell = 2, StealthShell = 3, ShadowTint = 4, Hidden = 5 }

    public struct AvatarLook
    {
        public AvatarLookKind Kind;
        public int DisplayId;       // CreatureDisplayInfo id when Kind == Creature, else 0
        public float Scale;         // object scale (1 = normal)
        public int Form;            // SpellShapeshiftForm id (log / animation hints), 0 none
        public int SpellId;         // the aura that made it (newest TRANSFORM 56, else the MOD_SHAPESHIFT 36 aura), 0 unknown (log only)
        public bool HidesAvatar { get { return Kind == AvatarLookKind.Creature || Kind == AvatarLookKind.GhostShell || Kind == AvatarLookKind.StealthShell || Kind == AvatarLookKind.Hidden; } }
        public bool HidesWeapons { get { return Kind == AvatarLookKind.Creature || Kind == AvatarLookKind.Hidden; } }
        public bool Same(in AvatarLook o) { return Kind == o.Kind && DisplayId == o.DisplayId && System.Math.Abs(Scale - o.Scale) < 0.01f; }
    }

    public static class AvatarLooks
    {
        /// <summary>SpellShapeshiftForm ids with a look but no model (measured AvFormRows: both have model 0).</summary>
        public const int FormShadow = 28, FormStealth = 30;

        /// <summary>
        /// The look WoW gives the player now. Order: in a vehicle seat that hides the passenger / possessing (pets H8) -> Hidden.
        /// Ghost: DisplayId != Native (the Night Elf wisp, Player.cpp BuildPlayerRepop casts 20584 = TRANSFORM 12861) -> Creature,
        /// else GhostShell (8326 SPELL_AURA_GHOST is a client-side look only). Dead (not ghost) -> Own (the corpse). DisplayId > 0,
        /// Native > 0 and different -> Creature (forms, transforms, costumes, another race's look: Unit::SetDisplayId / RestoreDisplayId,
        /// Unit.cpp:12771-12845). Form 28 Shadowform -> ShadowTint. Stealthed or form 30 -> StealthShell. Else Own. Mounted / taxi never
        /// change DisplayId (UNIT_FIELD_MOUNTDISPLAYID is separate). db may be null (SpellId stays 0 - it is for the log only).
        /// </summary>
        public static AvatarLook Decide(PlayerState p, SpellDb db)
        {
            var l = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f };
            if (p == null) return l;
            l.Form = p.ShapeshiftForm;
            if (p.Vehicle != null && p.Vehicle.HideAvatar) { l.Kind = AvatarLookKind.Hidden; return l; }
            bool transformed = p.DisplayId > 0 && p.NativeDisplayId > 0 && p.DisplayId != p.NativeDisplayId;
            float scale = p.ObjectScale > 0.01f && p.ObjectScale < 100f ? p.ObjectScale : 1f;
            if (p.Ghost)
            {
                if (transformed) { l.Kind = AvatarLookKind.Creature; l.DisplayId = p.DisplayId; l.Scale = scale; l.SpellId = LookSpell(p, db); }
                else l.Kind = AvatarLookKind.GhostShell;
                return l;
            }
            if (p.Dead) return l;
            if (transformed)
            {
                l.Kind = AvatarLookKind.Creature;
                l.DisplayId = p.DisplayId;
                l.Scale = scale;
                l.SpellId = LookSpell(p, db);
                return l;
            }
            if (p.ShapeshiftForm == FormShadow) { l.Kind = AvatarLookKind.ShadowTint; l.SpellId = FormSpell(p, db, FormShadow); return l; }
            if (p.Stealthed || p.ShapeshiftForm == FormStealth) { l.Kind = AvatarLookKind.StealthShell; l.SpellId = FormSpell(p, db, FormStealth); return l; }
            return l;
        }

        /// <summary>The same rule for another player's server copy (online fallback for an older record): DisplayId vs NativeDisplayId,
        /// then UNIT_STAND_FLAGS_CREEP (EntityState.Stealthed). NativeDisplayId 0 (offline / not known) -> Own.</summary>
        public static AvatarLook FromEntity(EntityState e)
        {
            var l = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f };
            if (e == null) return l;
            if (e.DisplayId > 0 && e.NativeDisplayId > 0 && e.DisplayId != e.NativeDisplayId)
            {
                l.Kind = AvatarLookKind.Creature;
                l.DisplayId = e.DisplayId;
                l.Scale = e.Scale > 0.01f && e.Scale < 100f ? e.Scale : 1f;
                return l;
            }
            if (e.Stealthed && !e.Dead) l.Kind = AvatarLookKind.StealthShell;
            return l;
        }

        /// <summary>"Creature display 2281 (form 5, spell 5487) x1.00" for the log.</summary>
        public static string Describe(in AvatarLook l)
        {
            string s = l.Kind == AvatarLookKind.Creature ? "Creature display " + l.DisplayId : l.Kind.ToString();
            if (l.Form != 0 || l.SpellId != 0) s += " (" + (l.Form != 0 ? "form " + l.Form : "no form") + (l.SpellId != 0 ? ", spell " + l.SpellId : "") + ")";
            if (l.Kind == AvatarLookKind.Creature) s += " x" + (l.Scale > 0f ? l.Scale : 1f).ToString("0.00");
            return s;
        }

        /// <summary>The newest TRANSFORM (56) aura on the player, else the MOD_SHAPESHIFT (36) aura of the current form; 0 unknown.</summary>
        public static int LookSpell(PlayerState p, SpellDb db)
        {
            if (p == null || db == null || p.Auras == null) return 0;
            for (int i = p.Auras.Count - 1; i >= 0; i--)
            {
                AuraView a = p.Auras[i];
                if (a != null && HasAura(db, a.SpellId, SpellAura.Transform, -1)) return a.SpellId;
            }
            return p.ShapeshiftForm != 0 ? FormSpell(p, db, p.ShapeshiftForm) : 0;
        }

        private static int FormSpell(PlayerState p, SpellDb db, int form)
        {
            if (p == null || db == null || p.Auras == null || form == 0) return 0;
            for (int i = p.Auras.Count - 1; i >= 0; i--)
            {
                AuraView a = p.Auras[i];
                if (a != null && HasAura(db, a.SpellId, SpellAura.ModShapeshift, form)) return a.SpellId;
            }
            return 0;
        }

        private static bool HasAura(SpellDb db, int spellId, int aura, int misc)
        {
            SpellInfo s = spellId > 0 ? db.Find(spellId) : null;
            if (s == null || s.Effects == null) return false;
            for (int e = 0; e < s.Effects.Length; e++)
            {
                SpellEffectInfo x = s.Effects[e];
                if (x != null && x.Aura == aura && (misc < 0 || x.MiscValue == misc)) return true;
            }
            return false;
        }
    }
}
