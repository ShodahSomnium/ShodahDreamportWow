// iteration 11 - owner uicore (wave-0 file by w0game; uicore fills the bodies, the Wave0 list order stays as written)
// The window registry of proposal-uicore.md 5.4.2 / 5.4.8: one UiWindowSpec per window kind. In wave 0 NOTHING reads it yet (WowArmUi and
// UiLayouts still drive the six iteration-10 windows as before), so registering changes no runtime behaviour. Every window whose owner has
// not built it carries Hidden = true (PLAN11 s4: no button, key, palm entry or session opening until the owner flips it in its own Spec()).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>UIPanelWindows "area" (UIParent.lua:16-52); Apart = not a UIPanel (the bag column, the World Map window).</summary>
    public enum UiPanelArea : byte { Left = 0, Center = 1, Doublewide = 2, Full = 3, Apart = 4 }

    /// <summary>
    /// One window of the game UI as the registry knows it (proposal-uicore.md 5.4.2, + the re-run members and Key2, + Hidden of PLAN11 s4).
    /// Build and Ext* are exclusive: Build = the registry builds a UiWindow; Ext* = an owner keeps its own MonoBehaviour / surfaces.
    /// </summary>
    public sealed class UiWindowSpec
    {
        public UiWindowKind Kind;
        public string Frame;                      // WoW's frame name (logs): "MerchantFrame"
        public string TitleKey;                   // GlobalStrings key for tooltips / palm labels ("TALENTS_BUTTON"); Title = literal fallback
        public string Title;
        public UiPanelArea Area = UiPanelArea.Left;
        public int Pushable;                      // UIPanelWindows pushable
        public float Width = 384f, Height = 512f; // canvas units (desktop holder, VR surface size); Width = UIPanelWindows width when given
        public float XOffset, YOffset;            // UIPanelWindows xoffset / yoffset
        public bool WhileDead;                    // else ERR_PLAYER_DEAD (GlobalStrings.lua:3387) and not opened
        public bool SessionWindow;                // opened by the game, not by the player
        public Func<IWowSession, bool> Wanted;    // SessionWindow: open while true (s => s.CurrentMerchant != null)
        public Func<IWowSession, ulong> NearGuid; // SessionWindow, VR placement: the NPC / player it belongs to (0 = in front of the player)
        public Action<IWowSession> Closed;        // the PLAYER closed it (X, Escape, walked away): s => s.CloseMerchant()
        public KeyCode Key = KeyCode.None;        // desktop toggle; validated by uicore's key table (5.5)
        public bool KeyShift;
        public string Binding;                    // WoW binding name for the tooltip ("TOGGLETALENTS")
        public string Micro;                      // micro button art name ("Talents"); "#Character" / "#PVP" = the UI-MicroButtonCharacter variants
        public int MicroOrder = -1;               // 0..9 WoW's order, 10 = the World Map button (ours); -1 = not on the micro bar
        public bool Palm = true;                  // VR palm menu button (toggle windows only)
        public string PalmArt;                    // palm-only art when Micro is null (e.g. the bags, the map)
        public int MinLevel;                      // Constants.lua:129-131
        public string OpenSound, CloseSound;      // FrameXML PlaySound names
        public Func<Transform, UiContext, Action, UiWindow> Build;   // build at (0,0) of parent (top-left, y down); the Action closes it
        public string Owner;                      // designer area (logs)
        // ---- External windows (an owner that keeps its own MonoBehaviour / surfaces): set these instead of Build.
        public Func<bool> ExtIsOpen;              // is it open now (polled every frame)
        public Action<bool> ExtSetOpen;           // open / close it (the player's toggle, Escape, the panel manager hiding it)
        public Action<float, float> ExtPlace;     // desktop: the manager's TOPLEFT (x, y) in UIParent units for its root; null = it places itself
        // ---- (talents U2) a game-side open request for a TOGGLE window: when the value CHANGES and is > 0 the window opens (never closes).
        public Func<IWowSession, int> OpenRequest;
        // ---- a second desktop chord (5.5): Mounts J + Shift+P, Bags B + F12
        public KeyCode Key2 = KeyCode.None;
        public bool Key2Shift;
        // ---- PLAN11 s4 (w0game): true = the window does not exist for the player yet (no micro / palm button, no key, never opened by
        // Wanted / OpenRequest / ExtIsOpen). Wave-0 stubs set it; the OWNER sets it false in its own Spec() when the window works.
        public bool Hidden;
    }

    /// <summary>The registry (proposal-uicore.md 5.4.2): the built-in specs of the existing windows + the lead's Wave0 list.</summary>
    public static class UiRegistry
    {
        public static readonly List<UiWindowSpec> All = new List<UiWindowSpec>();
        /// <summary>+1 on every Register (uicore: the layouts build re-registered windows, the micro menu rebinds).</summary>
        public static int Version { get; private set; }
        private static readonly UiWindowSpec[] ByKind = new UiWindowSpec[(int)UiWindowKind.Max + 1];

        static UiRegistry()
        {
            BuiltIn();
            Wave0();
        }

        /// <summary>The spec of a kind, null when none is registered.</summary>
        public static UiWindowSpec Of(UiWindowKind k)
        {
            int i = (int)k;
            return i > 0 && i < ByKind.Length ? ByKind[i] : null;
        }

        /// <summary>Adds a spec; the same Kind twice: the later wins (logged).</summary>
        public static void Register(UiWindowSpec s)
        {
            if (s == null) return;
            int i = (int)s.Kind;
            if (i <= 0 || i >= ByKind.Length)
            {
                WowLog.Warn("UI registry: window kind " + i + " (" + s.Frame + ", owner " + s.Owner + ") is out of range - not registered.");
                return;
            }
            UiWindowSpec old = ByKind[i];
            if (old != null)
            {
                All.Remove(old);
                WowLog.Info("UI registry: " + s.Kind + " registered again (" + s.Frame + ", owner " + s.Owner + ") - the later spec wins.");
            }
            ByKind[i] = s;
            Version++;
            All.Add(s);
        }

        /// <summary>As Register(spec()), but a Spec() that throws is logged once and skipped (the rest of the UI lives).</summary>
        public static void Register(Func<UiWindowSpec> spec)
        {
            if (spec == null) return;
            UiWindowSpec s;
            try { s = spec(); }
            catch (Exception e)
            {
                WowLog.Error("UI registry: a window spec threw (" + spec.Method.DeclaringType + "): " + e);
                return;
            }
            Register(s);
        }

        /// <summary>The lead's list (wave 0): ONE line per window file; never edited by owners afterwards (they change their own Spec()).</summary>
        private static void Wave0()
        {
            Register(UiGameMenu.Spec);            // uicore
            Register(UiKeyBindings.Spec);         // uicore
            Register(UiHelpStub.Spec);            // uicore (disabled micro button only)
            Register(UiTalentsWindow.Spec);       // talents
            Register(UiAchievementsWindow.Spec);  // character
            Register(UiReputationWindow.Spec);    // character
            Register(UiCurrencyWindow.Spec);      // character
            Register(UiSkills.Spec);              // trainer
            Register(UiTrainer.Spec);             // trainer
            Register(UiTradeSkill.Spec);          // trainer
            Register(UiMerchant.Spec);            // npcui
            Register(UiBank.Spec);                // npcui
            Register(UiItemText.Spec);            // npcui
            Register(UiStable.Spec);              // pets
            Register(UiPetBook.Spec);             // pets
            Register(UiSocial.Spec);              // socialui
            Register(UiMail.Spec);                // socialui
            Register(UiPetition.Spec);            // socialui
            Register(UiGuildRegistrar.Spec);      // socialui
            Register(UiTrade.Spec);               // interact
            Register(UiInspect.Spec);             // interact
            Register(UiPvp.Spec);                 // queues
            Register(UiBattlefield.Spec);         // queues
            Register(UiBattlefieldScore.Spec);    // queues
            Register(UiLfg.Spec);                 // queues
            Register(UiAuction.Spec);             // queues
            Register(UiTaxiSpec.Spec);            // motion (External: FlightMap)
        }

        /// <summary>
        /// The existing windows (iteration 10 tree) as specs with Build = null: UiLayouts keeps building them as today; the specs give
        /// the panel manager, the micro bar, the palm menu and the keys what they need (proposal-uicore.md 5.4.2 / 5.4.8, PLAN11 s2 keys).
        /// </summary>
        private static void BuiltIn()
        {
            Register(new UiWindowSpec
            {
                Kind = UiWindowKind.Character, Frame = "CharacterFrame", TitleKey = "CHARACTER_BUTTON", Title = "Character Info",   // GlobalStrings.lua:1373
                Area = UiPanelArea.Left, Pushable = 3, WhileDead = true, Key = KeyCode.C, Binding = "TOGGLECHARACTER0",
                Micro = "#Character", MicroOrder = 0, Owner = "character (existing UiCharacter)"
            });
            Register(new UiWindowSpec
            {
                Kind = UiWindowKind.Spellbook, Frame = "SpellBookFrame", TitleKey = "SPELLBOOK_ABILITIES_BUTTON", Title = "Spellbook & Abilities",   // :6904
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, Key = KeyCode.P, Binding = "TOGGLESPELLBOOK",
                Micro = "Spellbook", MicroOrder = 1, Owner = "vrcast (existing UiSpellbook)"
            });
            Register(new UiWindowSpec
            {
                // Ours: the 384 single quest log (WoW's QuestLogFrame is doublewide 682 - gap G10 of proposal-uicore.md).
                Kind = UiWindowKind.QuestLog, Frame = "QuestLogFrame", TitleKey = "QUESTLOG_BUTTON", Title = "Quest Log",   // :5874
                Area = UiPanelArea.Left, Pushable = 0, WhileDead = true, Key = KeyCode.L, Binding = "TOGGLEQUESTLOG",
                Micro = "Quest", MicroOrder = 4, Owner = "quests (existing UiQuestLog)"
            });
            Register(new UiWindowSpec
            {
                Kind = UiWindowKind.Bags, Frame = "ContainerFrame1", TitleKey = "BACKPACK_TOOLTIP", Title = "Backpack",   // :670
                Area = UiPanelArea.Apart, Key = KeyCode.B, Key2 = KeyCode.F12, Palm = true, Owner = "npcui (existing UiBags)"
            });
            Register(new UiWindowSpec
            {
                // PLAN11 s2: O = Social; the Settings open from Escape > Interface, the Game Menu micro button and the palm.
                Kind = UiWindowKind.Settings, Frame = "InterfaceOptionsFrame", Title = "Settings",   // ours (the window's own title)
                Area = UiPanelArea.Center, WhileDead = true, Palm = true, Owner = "w0game (existing UiSettingsWindow)"
            });
            Register(new UiWindowSpec
            {
                Kind = UiWindowKind.Mounts, Frame = "PetPaperDollFrame", TitleKey = "MOUNTS", Title = "Mounts",   // :4980
                Area = UiPanelArea.Left, Pushable = 3, WhileDead = true, Key = KeyCode.J, Key2 = KeyCode.P, Key2Shift = true, Palm = true,
                Owner = "uicore (existing UiMounts)"
            });
            Register(new UiWindowSpec
            {
                // uicore (run 1): Closed = the player closed it (Escape, the panel manager) -> the session ends the conversation.
                Kind = UiWindowKind.QuestDialog, Frame = "QuestFrame", Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,
                Wanted = s => s.CurrentDialog != null, Closed = s => s.CloseDialog(),
                NearGuid = s => s.CurrentDialog != null ? s.CurrentDialog.NpcGuid : 0UL, Owner = "quests (existing UiQuestDialog)"
            });
            Register(new UiWindowSpec
            {
                // LootFrame is 256 wide (out_ui/LootFrame.xml:81-84, UiLoot W 256). Shown only while no quest dialog is (iteration 10 rule:
                // the VR dialog surface carries one of them at a time).
                Kind = UiWindowKind.Loot, Frame = "LootFrame", TitleKey = "LOOT", Title = "Loot",   // :4792
                Area = UiPanelArea.Left, Pushable = 7, Width = 256f, Height = 256f, SessionWindow = true, Palm = false,
                Wanted = s => s.CurrentLoot != null && s.CurrentDialog == null, Closed = s => s.CloseLoot(),
                NearGuid = s => s.CurrentLoot != null ? s.CurrentLoot.CorpseGuid : 0UL, Owner = "quests (existing UiLoot)"
            });
            Register(WorldMapSpec());
        }

        /// <summary>The desktop WoW Map window (WowTabletMap, External, proposal-uicore.md H16): key N, the World Map micro button (ours,
        /// MicroOrder 10). It places itself (top right); the palm keeps its own Map button (WowTabletMap.TogglePalmWindow).</summary>
        private static UiWindowSpec WorldMapSpec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.WorldMap, Frame = "WorldMapFrame", TitleKey = "WORLD_MAP", Title = "World Map",   // :8958
                Area = UiPanelArea.Apart, WhileDead = true, Key = KeyCode.N, Micro = "World", MicroOrder = 10, Palm = false, Owner = "uicore (map)",
                ExtIsOpen = () => SomniumSpace.Worlds.SomniumWoW.Game.WowTabletMap.DesktopOpen,
                ExtSetOpen = open => SomniumSpace.Worlds.SomniumWoW.Game.WowTabletMap.SetDesktopWindow(open, "registry"),
                Hidden = false
            };
        }

        /// <summary>The spec exists and is not Hidden (its window works).</summary>
        public static bool Visible(UiWindowKind k)
        {
            UiWindowSpec s = Of(k);
            return s != null && !s.Hidden;
        }

        /// <summary>Title for tooltips / logs: the install's GlobalStrings text of TitleKey, else Title, else the frame name.</summary>
        public static string TitleOf(UiWindowSpec s)
        {
            if (s == null) return "";
            if (!string.IsNullOrEmpty(s.TitleKey)) return Pure.UiGlobalStrings.G(s.TitleKey, s.Title ?? s.Frame ?? s.Kind.ToString());
            return s.Title ?? s.Frame ?? s.Kind.ToString();
        }

        /// <summary>The desktop chord(s) of a spec as text: "C", "Shift+N", "J / Shift+P"; "" = none.</summary>
        public static string KeyText(UiWindowSpec s)
        {
            if (s == null) return "";
            string a = s.Key != KeyCode.None ? (s.KeyShift ? "Shift+" : "") + KeyName(s.Key) : "";
            string b = s.Key2 != KeyCode.None ? (s.Key2Shift ? "Shift+" : "") + KeyName(s.Key2) : "";
            return a.Length > 0 && b.Length > 0 ? a + " / " + b : a + b;
        }

        public static string KeyName(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            switch (k)
            {
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Escape: return "Escape";
                default: return k.ToString();
            }
        }
    }
}
