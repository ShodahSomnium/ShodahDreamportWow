// iteration 11 - owner uicore: every desktop key of the Dreamport + the Somnium client's keys (proposal-uicore.md 5.5) - P/harness/Probes/D11/uicore/UiKeyMapProto.cs
// copied as is (namespace changed); harness-tested by probe UicKeyMap (P/harness/Probes/I11/uicore). Pure C#: no UnityEngine.
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure
{
    /// <summary>
    /// PROTOTYPE of W/Game/Play/Ui/Pure/UiKeyMap.cs (pure C#): every desktop key of the Dreamport (ours + the other iteration-11 designers'
    /// proposals) with its WoW binding / default (WTF\DefaultBindings.wtf of the install, dumped to out_d11uicore/DefaultBindings.wtf) and the
    /// Somnium client's own keys (CS Inputs/2D/2D Input Actions.inputactions map 2DPlayer, listed by PowerShell 2026-10-03; legacy
    /// Input.GetKey reads grep'd in CS) - Check() reports chord duplicates among ours and base-key overlaps with the client.
    /// Builders: UiKeys (the dispatcher) reads Entries; the Key Bindings window lists them. Chord syntax: "Shift+1", "Ctrl+F1", "Alt+=".
    /// </summary>
    public static class UiKeyMap
    {
        public sealed class Entry
        {
            public string Chord;      // "C", "Shift+N", "Ctrl+1"
            public string Does;       // what it does here
            public string Binding;    // WoW binding name (BINDING_NAME_* in GlobalStrings) or "ours"
            public string WowDefault; // the install's DefaultBindings.wtf line ("C :94") or "-" (WoW has no default)
            public string Owner;      // designer area / file
            public string Note;       // a decision id or why an overlap is fine
            public Entry(string chord, string does, string binding, string wow, string owner, string note = null)
            { Chord = chord; Does = does; Binding = binding; WowDefault = wow; Owner = owner; Note = note; }
        }

        /// <summary>Somnium client keys: base key -> what the client does with it (any modifiers).</summary>
        public static readonly Dictionary<string, string> Client = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Escape", "2DPlayer/Exit (PlayerInput.prefab:61-65 has NO calls; UI/Cancel usage; NonXRMediaFeature.cs:7 leaves fullscreen media; QuickChatInputView.cs:42 closes)" },
            { "F", "2DPlayer/Interact" }, { "X", "2DPlayer/HideObject + MuteObject" }, { "R", "2DPlayer/PopUpMenu" }, { "Space", "2DPlayer/Jump" },
            { "M", "2DPlayer/Microphone" }, { "W", "Move" }, { "A", "Move" }, { "S", "Move" }, { "D", "Move" },
            { "Up", "Move" }, { "Down", "Move" }, { "Left", "Move" }, { "Right", "Move" }, { "Tab", "2DPlayer/Tablet" },
            { "I", "2DPlayer/Inspector" }, { "Q", "2DPlayer/Rotate" }, { "E", "2DPlayer/Rotate" }, { "Shift", "2DPlayer/Sprint (a modifier for us)" },
            { "Mouse1", "Second Camera Action (turn)" }, { "Mouse2", "First Camera Action (look)" }, { "Wheel", "Zoom" },
            // legacy Input reads in client scripts (grep GetKey*(KeyCode.*) in CS)
            { "LeftControl", "NetworkGUIUtility debug combos Ctrl+Shift+D / Ctrl+F+S" }, { "Z", "Enviro weather TEST script (not in this world's scene)" },
            { "G", "XRAFKModeFeature (XR only)" }, { "U", "MasterClientControl (#if PUN_MC only - not in the live build)" },
        };

        /// <summary>Overlaps with the client that are intended (base key -> why it is fine). Anything else is a finding.</summary>
        public static readonly Dictionary<string, string> Accepted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Escape", "the client's Exit action has no listener in the live player prefab; our Escape chain owns it (fullscreen media and the quick chat still close on it, first)" },
            { "Shift", "Shift+digit: Sprint has no effect on our WoW run speed (WowRunSpeed) - vrcast's finding" },
            { "Space", "mount flying: up (MountInput) rides on the client's jump on purpose" },
            { "LeftControl", "mount flying: down; Ctrl+digits / Ctrl+F-keys are not the client's debug combos" },
            { "W", "mount steering on the client's movement keys" }, { "A", "mount steering" }, { "S", "mount steering" }, { "D", "mount steering" },
            { "Q", "mount steering" }, { "E", "mount steering" }, { "Up", "mount steering / chat history while typing" },
            { "Down", "mount steering / chat history while typing" }, { "Left", "mount steering" }, { "Right", "mount steering" },
            { "Z", "the client's Z is a weather TEST script that is not in this world" },
            { "U", "the client's U exists only in the PUN_MC admin build" },
        };

        public static readonly List<Entry> Entries = new List<Entry>();

        static UiKeyMap()
        {
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            for (int i = 0; i < 12; i++)
                Entries.Add(new Entry(digits[i], "main bar button " + (i + 1) + " (current page / bonus bar)", "ACTIONBUTTON" + (i + 1), digits[i] + " :" + (31 + i), "vrcast (WowArmUi.Keys)"));
            for (int i = 0; i < 6; i++)
                Entries.Add(new Entry("Shift+" + digits[i], "action page " + (i + 1), "ACTIONPAGE" + (i + 1), "SHIFT-" + digits[i] + " :" + (63 + i), "vrcast"));
            for (int i = 0; i < 12; i++)
                Entries.Add(new Entry("Alt+" + digits[i], "Bottom Left Bar button " + (i + 1), "MULTIACTIONBAR1BUTTON" + (i + 1), "-", "vrcast", "ours (WoW binds no extra bar); vrcast U2"));
            for (int i = 0; i < 10; i++)
                Entries.Add(new Entry("Ctrl+" + digits[i], "pet bar button " + (i + 1), "BONUSACTIONBUTTON" + (i + 1), "CTRL-" + digits[i] + " :" + (53 + i), "pets (H9a)"));
            for (int i = 0; i < 10; i++)
                Entries.Add(new Entry("Ctrl+F" + (i + 1), "stance / form button " + (i + 1), "SHAPESHIFTBUTTON" + (i + 1), "CTRL-F" + (i + 1) + " :" + (43 + i), "vrcast"));

            Entries.Add(new Entry("C", "Character", "TOGGLECHARACTER0", "C :94", "uicore registry"));
            Entries.Add(new Entry("P", "Spellbook", "TOGGLESPELLBOOK", "P :102", "uicore registry"));
            Entries.Add(new Entry("Shift+P", "Mounts / companions window", "TOGGLECHARACTER3", "SHIFT-P :104", "uicore registry (J stays)"));
            Entries.Add(new Entry("J", "Mounts window", "ours", "-", "uicore registry (iteration 7)"));
            // NOT bound: Shift+I (TOGGLEPETBOOK, SHIFT-I :103) - the client's Inspector is a plain <Keyboard>/i binding, so Shift+I would open it too
            // (probe UicKeyMap found it, 2026-10-03). The pet book stays reachable from the spellbook's pet tab and the palm.
            Entries.Add(new Entry("L", "Quest Log", "TOGGLEQUESTLOG", "L :113", "uicore registry"));
            Entries.Add(new Entry("B", "backpack", "TOGGLEBACKPACK", "B :95", "npc"));
            Entries.Add(new Entry("F12", "backpack", "TOGGLEBACKPACK", "F12 :96", "npc"));
            Entries.Add(new Entry("Shift+B", "all bags", "OPENALLBAGS", "SHIFT-B :101", "npc (H10)"));
            for (int i = 0; i < 4; i++)
                Entries.Add(new Entry("F" + (8 + i), "bag " + (4 - i) + " (ToggleBag(" + (4 - i) + "), Bindings.xml:617-628)", "TOGGLEBAG" + (i + 1), "F" + (8 + i) + " :" + (97 + i), "npc (optional)"));
            Entries.Add(new Entry("N", "WoW Map (today)", "ours", "N :109 = TOGGLETALENTS", "map (WowTabletMap.cs:37)", "D-K1"));
            Entries.Add(new Entry("Shift+N", "Talents", "TOGGLETALENTS", "N :109", "talents", "D-K1 (talents D1 recommends Shift+N)"));
            Entries.Add(new Entry("U", "Reputation", "TOGGLECHARACTER2", "U :110", "talents"));
            Entries.Add(new Entry("Y", "Achievements", "TOGGLEACHIEVEMENT", "Y :115", "talents"));
            Entries.Add(new Entry("K", "Skills (in the world) - the room key stays K while loading", "TOGGLECHARACTER1", "K :111", "trainer (U9) + front", "D-K2"));
            Entries.Add(new Entry("O", "Social", "TOGGLESOCIAL", "O :112", "social (D1)", "D-K3 (Settings moves to Escape > Interface + the palm)"));
            Entries.Add(new Entry("H", "focus (stage S1)", "ours", "H :108 = TOGGLECHARACTER4 = TogglePVPFrame (Bindings.xml:647-649)", "VrFocusButton.cs:26", "D-K4: PvP on the micro button only"));
            Entries.Add(new Entry("T", "target the nearest enemy", "ours (WoW: TARGETNEARESTENEMY on Tab)", "T :92 = ATTACKTARGET", "WowArmUi"));
            Entries.Add(new Entry("V", "start / stop auto attack", "ours", "V :89 = nameplates", "WowArmUi"));
            Entries.Add(new Entry("Z", "sheath / unsheath", "TOGGLESHEATH", "Z :15", "GearLocal.cs:292"));
            Entries.Add(new Entry("Escape", "Escape chain: popups, menus, cast, cursor, windows, target, else the Game Menu", "TOGGLEGAMEMENU", "ESCAPE :118", "uicore (UiChat first while typing)"));
            Entries.Add(new Entry("Enter", "chat", "OPENCHAT", "ENTER :21", "UiChat"));
            Entries.Add(new Entry("/", "chat with /", "OPENCHATSLASH", "/ :22", "UiChat"));
            Entries.Add(new Entry("Alt", "(hold) cursor in first person", "ours", "-", "uicore UiCursor", "D-C1; holding Alt for Alt+digits shows the cursor too (harmless)"));
            Entries.Add(new Entry("Space", "mount: fly up", "ours", "SPACE = JUMP", "MountInput.cs:70-81"));
            Entries.Add(new Entry("LeftControl", "mount: fly down", "ours", "-", "MountInput.cs:81"));
        }

        static string BaseKey(string chord)
        {
            int p = chord.LastIndexOf('+');
            string k = p >= 0 && p < chord.Length - 1 ? chord.Substring(p + 1) : chord;
            return k;
        }

        static string[] Mods(string chord)
        {
            int p = chord.LastIndexOf('+');
            if (p <= 0 || p == chord.Length - 1) return new string[0];
            return chord.Substring(0, p).Split('+');
        }

        /// <summary>Duplicates among Entries (same chord, different action) and overlaps with the client's keys (base key or modifier).</summary>
        public static List<string> Check(out List<string> accepted)
        {
            var problems = new List<string>();
            accepted = new List<string>();
            var seen = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (Entry e in Entries)
            {
                Entry prev;
                if (seen.TryGetValue(e.Chord, out prev)) problems.Add("DUPLICATE " + e.Chord + ": '" + prev.Does + "' (" + prev.Owner + ") and '" + e.Does + "' (" + e.Owner + ")");
                else seen[e.Chord] = e;
                var keys = new List<string> { BaseKey(e.Chord) };
                foreach (string m in Mods(e.Chord)) keys.Add(m == "Ctrl" ? "LeftControl" : m);
                foreach (string k in keys)
                {
                    string what;
                    if (!Client.TryGetValue(k, out what)) continue;
                    string why;
                    string line = e.Chord + " (" + e.Does + ") uses the client's " + k + " = " + what;
                    if (Accepted.TryGetValue(k, out why)) accepted.Add(line + " -> ok: " + why);
                    else problems.Add("CLIENT " + line);
                }
            }
            return problems;
        }
    }
}
