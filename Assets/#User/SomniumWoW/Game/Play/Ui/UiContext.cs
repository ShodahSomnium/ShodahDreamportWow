using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public enum UiWindowKind
    {
        None = 0, Spellbook = 1, QuestLog = 2, Character = 3, Bags = 4, Settings = 5, Mounts = 6,   // Mounts: iteration 7 (PLAN7 s5.6)
        // Iteration 11 (w0game, wave 0): uicore's registry numbers (proposal-uicore.md 5.4.1 / 5.4.8), appended; [owner]. Toggled windows:
        Talents = 7,          // PlayerTalentFrame (glyphs = its tab)                         [talents]
        Achievements = 8,     // AchievementFrame                                             [character]
        Social = 9,           // FriendsFrame: Friends / Ignore / Who / Guild / Chat / Raid    [socialui]
        PvP = 10,             // PVPParentFrame                                               [queues]
        DungeonFinder = 11,   // LFDParentFrame                                               [queues]
        GameMenu = 12,        // GameMenuFrame                                                [uicore]
        Help = 13,            // HelpFrame - not offered (disabled micro button)              [uicore]
        Reputation = 14,      // CharacterFrame's ReputationFrame tab                         [character]
        Skills = 15,          // CharacterFrame's SkillFrame tab                              [trainer]
        Currency = 16,        // CharacterFrame's TokenFrame tab                              [character]
        PetBook = 17,         // SpellBookFrame's pet tab                                     [pets]
        KeyBindings = 18,     // KeyBindingFrame (ours: the list of the Dreamport's keys)     [uicore]
        RaidBrowser = 19,     // LFRParentFrame - reserved, not built                         [interact]
        WorldMap = 20,        // the desktop WoW Map window (WowTabletMap, External)          [uicore]
        // windows the GAME opens (an NPC, a player, an item): no key; VR = the world spot next to the NPC
        Merchant = 32, Trainer = 33, TradeSkill = 34, Gossip = 35, Bank = 36, Mail = 37, Auction = 38, Trade = 39,
        Inspect = 40, ItemText = 41, Taxi = 42, GuildBank = 43, Stable = 44, Tabard = 45, Petition = 46, GuildRegistrar = 47,
        ArenaRegistrar = 48, Battlefield = 49,
        BattlefieldScore = 50,         // WorldStateScoreFrame (interact R-U2)
        QuestDialog = 52, Loot = 53,   // the existing quest / gossip dialog and loot window (managed panels too)
        Max = 63              // array size - 1 (WowArmUi._open = new bool[(int)UiWindowKind.Max + 1] once uicore drives the registry)
    }

    /// <summary>What every widget needs: the running session, the mode, the spell being placed on the bar, window toggles, messages.</summary>
    public sealed class UiContext
    {
        public IWowSession S;
        public bool Vr;

        /// <summary>The spell being placed on an action bar slot (-1 none, 0 = "clear the slot I tap next", -2 = an item / macro on the
        /// cursor). Iteration 11 (uicore 5.2.1, vrcast hook 6): a read-only view of the ONE cursor, UiPickup.</summary>
        public int PickSpell
        {
            get
            {
                UiPickupPayload c = UiPickup.Current;
                if (c == null || !c.Placeable) return -1;
                if (c.Packed == 0) return 0;
                int sp = WowActionSlots.SpellOf(c.Packed);
                return sp != 0 ? sp : -2;
            }
        }

        public string PickName { get { UiPickupPayload c = UiPickup.Current; return c != null ? c.Name ?? "" : ""; } }

        public Action<UiWindowKind> Toggle;
        public Func<UiWindowKind, bool> IsOpen;
        public Action<string, UnityEngine.Color> Message;

        /// <summary>Iteration 11 (uicore contract 6.2): something is on the cursor (UiPickup).</summary>
        public bool Holding { get { return UiPickup.Holding; } }

        public PlayerState P { get { return S != null ? S.Player : null; } }

        /// <summary>The ability with this spell id in the spellbook, else in Player.Mounts (iteration 7: mounts are not in the spellbook,
        /// PLAN7 s3.1); the lists are rebuilt on level-up / learning, so look it up every frame.</summary>
        public AbilityView Ability(int spellId)
        {
            return Find(P, spellId);
        }

        /// <summary>Spellbook first, then Player.Mounts (also used by the VR radial menus).</summary>
        public static AbilityView Find(PlayerState p, int spellId)
        {
            if (p == null || spellId == 0) return null;
            List<AbilityView> book = p.Spellbook;
            if (book != null) for (int i = 0; i < book.Count; i++) if (book[i] != null && book[i].SpellId == spellId) return book[i];
            List<AbilityView> mounts = p.Mounts;
            if (mounts != null) for (int i = 0; i < mounts.Count; i++) if (mounts[i] != null && mounts[i].SpellId == spellId) return mounts[i];
            // Iteration 11 (trainer U8): an action button that holds a recipe still resolves (recipes are not in the spellbook, WoW).
            List<AbilityView> recipes = p.Recipes;
            if (recipes != null) for (int i = 0; i < recipes.Count; i++) if (recipes[i] != null && recipes[i].SpellId == spellId) return recipes[i];
            return null;
        }

        /// <summary>Iteration 7: the ridden mount's ability shows the "active" state (the session's Active flag, or Player.Mounted with that spell).</summary>
        public static bool IsActive(PlayerState p, AbilityView v)
        {
            if (v == null) return false;
            if (v.Active) return true;
            return v.IsMount && p != null && p.Mounted && p.MountSpellId == v.SpellId;
        }

        public void Say(string text, UnityEngine.Color c)
        {
            if (Message != null) Message(text, c);
        }

        /// <summary>Puts a spell (or, spellId 0, "clear the button I tap next") on the cursor - UiPickup, the one cursor (5.2.1).</summary>
        public void StartPick(int spellId, string name)
        {
            AbilityView v = spellId != 0 ? Ability(spellId) : null;
            bool mount = v != null && v.IsMount;
            UiPickup.PickUp(new UiPickupPayload
            {
                Kind = spellId == 0 ? UiPickupKind.Action : mount ? UiPickupKind.Mount : UiPickupKind.Spell,
                Packed = spellId == 0 ? 0 : WowActionSlots.Spell(spellId),
                SpellId = spellId,
                Name = name ?? (v != null ? v.Name : ""),
                IconPath = v != null ? v.IconPath : null
            }, false, null);
            string n = PickName;
            if (Vr && !WowSettings.VrForearmBar)
                Say(spellId == 0 ? "Grip a hand, twist and push or pull to choose a slot, release to clear it."
                    : "Grip a hand, twist and push or pull to choose a slot, release to place " + n + ".", UiArt.InfoYellow);   // ours (vrcast hook 6)
            else Say(spellId == 0 ? "Tap an action button to clear it." : "Tap an action button to place " + n + ".", UiArt.InfoYellow);
        }

        public void CancelPick()
        {
            if (UiPickup.Holding) UiPickup.Clear("cancel");
        }
    }

    /// <summary>
    /// Interface sounds through the Audio owner's WowAudio (safe before the sound tables are read; never throws into the UI).
    /// The quest / level / loot / death sounds are the Audio driver's own (from WowPlay.Event) - the UI never plays those.
    /// </summary>
    public static class UiSound
    {
        public static void Play(WowUiSound s)
        {
            try { WowAudio.PlayUi(s); }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>A FrameXML PlaySound name (SoundEntries.Name), e.g. "igMainMenuOptionCheckBoxOn".</summary>
        public static void Named(string name)
        {
            try { WowAudio.PlayUiNamed(name); }
            catch (Exception e) { Fail(e); }
        }

        private static readonly HashSet<string> AddedLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>duelsnd (iteration 11): a FrameXML PlaySound the Dreamport did not play before this fix; the first play of each name
        /// logs one line with the FrameXML place that plays it (source, e.g. "StaticPopup_OnShow, StaticPopup.lua:3428").</summary>
        public static void Added(string name, string source)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (AddedLogged.Add(name)) WowLog.Info("UI sound: '" + name + "' played for the first time (" + source + ").");
            Named(name);
        }

        public static void Open(UiWindowKind k, bool open)
        {
            switch (k)
            {
                case UiWindowKind.Spellbook: Play(open ? WowUiSound.SpellbookOpen : WowUiSound.SpellbookClose); break;
                case UiWindowKind.QuestLog: Play(open ? WowUiSound.QuestLogOpen : WowUiSound.QuestLogClose); break;
                case UiWindowKind.Character: Play(open ? WowUiSound.CharacterOpen : WowUiSound.CharacterClose); break;
                case UiWindowKind.Bags: Play(open ? WowUiSound.BagOpen : WowUiSound.BagClose); break;
                case UiWindowKind.Settings: Play(open ? WowUiSound.WindowOpen : WowUiSound.WindowClose); break;
                // The Mounts pane is a tab of WoW's CharacterFrame (PetPaperDollFrame): the character frame's sounds.
                case UiWindowKind.Mounts: Play(open ? WowUiSound.CharacterOpen : WowUiSound.CharacterClose); break;
                default:
                    {
                        // Iteration 11 (uicore 5.4.2): the registered windows play their spec's FrameXML sounds.
                        UiWindowSpec s = UiRegistry.Of(k);
                        string n = s != null ? (open ? s.OpenSound : s.CloseSound) : null;
                        if (!string.IsNullOrEmpty(n)) Named(n);
                        break;
                    }
            }
        }

        private static bool _failed;

        private static void Fail(Exception e)
        {
            if (_failed) return;
            _failed = true;
            WowLog.Warn("UI: interface sound failed (logged once): " + e.Message);
        }
    }
}
