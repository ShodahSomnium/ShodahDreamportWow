using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.LocalStorage;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The world's storage on this PC, SomniumBridge.LocalStorage (the only storage the Bridge has). In the client it is
    /// BridgeWorldsLocalStorage (E:\Somnium Space\Main Build\Assets\_SomniumSpace\Bridge\Scripts\Components\Runtime):
    /// Application.temporaryCachePath/WorldsLocalStorage/&lt;worldId&gt;/&lt;key&gt;.txt, AES + base64, 50 MB per world.
    /// ReadText gives "" for a missing file AND for one it cannot decrypt, so TextExists is asked first; such a file is
    /// copied raw (usePlainText) to &lt;key&gt;_bad&lt;suffix&gt; before the roster can overwrite it (EtsBridgeStorageSlot's lesson).
    /// Only TextExists / ReadText / WriteText are used: they are the members other worlds passed the server compile with.
    /// </summary>
    public sealed class WowBridgeSlot : IWowSaveSlot
    {
        private readonly ISomniumWorldsLocalStorage _storage;
        private readonly string _suffix;

        public WowBridgeSlot(ISomniumWorldsLocalStorage storage, string suffix)
        {
            _storage = storage;
            _suffix = suffix ?? "";
        }

        public string Name { get { return _suffix.Length == 0 ? "world storage" : "world storage" + _suffix; } }

        public bool TryRead(string key, out string text)
        {
            text = null;

            try
            {
                if (_storage == null || !_storage.TextExists(key + _suffix))
                {
                    return false;
                }

                text = _storage.ReadText(key + _suffix, false) ?? "";

                if (text.Length == 0)
                {
                    KeepUnreadable(key);
                }

                return true;
            }
            catch (Exception e)
            {
                WowLog.Warn("Save: reading " + key + _suffix + " from " + Name + " threw " + e.GetType().Name + ": " + e.Message);
                text = "";
                return true;
            }
        }

        private void KeepUnreadable(string key)
        {
            try
            {
                string raw = _storage.ReadText(key + _suffix, true);

                if (!string.IsNullOrEmpty(raw))
                {
                    _storage.WriteText(key + "_bad" + _suffix, raw, true);
                    WowLog.Warn("Save: " + key + _suffix + " exists but could not be decrypted - the raw file was kept as " + key + "_bad" + _suffix + ".");
                }
            }
            catch (Exception)
            {
                // A slot must not throw; the roster goes on with the other copy.
            }
        }

        public bool Write(string key, string text)
        {
            try
            {
                return _storage != null && _storage.WriteText(key + _suffix, text, false);
            }
            catch (Exception e)
            {
                WowLog.Warn("Save: writing " + key + _suffix + " to " + Name + " threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }
    }

    /// <summary>The local Somnium account (id + nickname) and the roster's storage slots. Null-safe: the editor has no client.</summary>
    public static class WowSaves
    {
        public const string EditorUserId = "editor";
        public const string FallbackName = "Adventurer";

        /// <summary>The account id and nickname once the client has filled them; in the editor "editor"/"Adventurer" after 2 s.</summary>
        public static bool TryIdentity(float secondsSinceStart, out string id, out string nick)
        {
            id = null;
            nick = null;

            try
            {
                var props = SomniumBridge.PlayersContainer?.LocalPlayer?.Properties;
                id = props?.Id;
                nick = props?.NickName;
            }
            catch (Exception)
            {
                // No client, or the player is not built yet.
            }

            if (string.IsNullOrEmpty(id))
            {
                if (Application.isEditor && secondsSinceStart > 2f)
                {
                    id = EditorUserId;
                    nick = FallbackName;
                    return true;
                }

                return false;
            }

            if (string.IsNullOrEmpty(nick))
            {
                nick = FallbackName;
            }

            return true;
        }

        /// <summary>The local player's Somnium nickname now ("Adventurer" when there is none, e.g. in the editor).</summary>
        public static string NickName()
        {
            try
            {
                string n = SomniumBridge.PlayersContainer?.LocalPlayer?.Properties?.NickName;
                return string.IsNullOrEmpty(n) ? FallbackName : n.Trim();
            }
            catch (Exception)
            {
                return FallbackName;
            }
        }

        /// <summary>Opens and loads the account's roster: two world-storage copies in the client, memory in the editor.</summary>
        public static WowRoster Open(string userId, out string storageName)
        {
            var roster = new WowRoster(userId, Slots(out storageName, "characters"));
            roster.Load();
            return roster;
        }

        /// <summary>Iteration 3: the account's settings store (its own versioned key, the same two copies / memory in the editor), loaded.</summary>
        public static WowSettingsStore OpenSettings(string userId, out string storageName, out string body)
        {
            var store = new WowSettingsStore(userId, Slots(out storageName, "settings"));
            body = store.Load();
            return store;
        }

        /// <summary>
        /// Stage A1: the automatic WoW accounts' keys on this PC (WowAutoKeys: "wow_autoaccount_&lt;host&gt;_&lt;NAME&gt;", the same
        /// two world-storage copies; memory in the editor or when the client has no LocalStorage - then the key lasts this visit only).
        /// </summary>
        public static WowAutoKeys OpenAutoKeys(out string storageName)
        {
            return new WowAutoKeys(Slots(out storageName, "automatic account keys"));
        }

        /// <summary>
        /// The game folder the player chose in the room's folder browser, for this Somnium account on this PC: key
        /// WowInstall.SavedKeyFor(id) ("wow_install_path_&lt;id&gt;"), text WowInstall.EncodeSaved, the same two world-storage
        /// copies (memory in the editor). The first copy that decodes wins. Null when none is saved. Main thread; never throws.
        /// The folder itself is NOT checked here (a network drive could stall the frame): the boot worker validates it.
        /// </summary>
        public static string ReadInstallPath(string userId, out string report)
        {
            var sb = new System.Text.StringBuilder();
            string found = null;

            try
            {
                string storage;
                IWowSaveSlot[] slots = Slots(out storage, "the chosen game folder");
                string key = Data.WowInstall.SavedKeyFor(userId);
                sb.Append(storage).Append(": ");

                foreach (IWowSaveSlot slot in slots)
                {
                    string text;

                    if (!slot.TryRead(key, out text))
                    {
                        sb.Append(slot.Name).Append(" none; ");
                        continue;
                    }

                    string path = Data.WowInstall.DecodeSaved(text);

                    if (path == null)
                    {
                        sb.Append(slot.Name).Append(" unreadable; ");
                        continue;
                    }

                    sb.Append(slot.Name).Append(" ok; ");
                    if (found == null) found = path;
                }
            }
            catch (Exception e)
            {
                sb.Append("reading threw ").Append(e.GetType().Name).Append(": ").Append(e.Message);
            }

            report = sb.ToString().TrimEnd(' ', ';');
            return found;
        }

        /// <summary>Saves the chosen game folder (see <see cref="ReadInstallPath"/>) to every copy, one after the other. True when at least one copy took it.</summary>
        public static bool SaveInstallPath(string userId, string path, out string report)
        {
            var sb = new System.Text.StringBuilder();
            int ok = 0;

            try
            {
                string storage;
                IWowSaveSlot[] slots = Slots(out storage, "the chosen game folder");
                string key = Data.WowInstall.SavedKeyFor(userId);
                string text = Data.WowInstall.EncodeSaved(path);
                sb.Append(storage).Append(": ");

                foreach (IWowSaveSlot slot in slots)
                {
                    bool w = slot.Write(key, text);
                    if (w) ok++;
                    sb.Append(slot.Name).Append(w ? " written; " : " FAILED; ");
                }
            }
            catch (Exception e)
            {
                sb.Append("writing threw ").Append(e.GetType().Name).Append(": ").Append(e.Message);
            }

            report = sb.ToString().TrimEnd(' ', ';');
            return ok > 0;
        }

        private static IWowSaveSlot[] Slots(out string storageName, string what)
        {
            var slots = new List<IWowSaveSlot>();
            ISomniumWorldsLocalStorage storage = null;

            try
            {
                // Read once: the getter logs "[Bridge] LocalStorage was not found" when a Bridge exists without storage.
                storage = SomniumBridge.LocalStorage;
            }
            catch (Exception)
            {
                storage = null;
            }

            if (storage != null)
            {
                slots.Add(new WowBridgeSlot(storage, ""));
                slots.Add(new WowBridgeSlot(storage, "_2"));
                storageName = "world storage (2 copies)";
            }
            else
            {
                slots.Add(new WowMemorySlot());
                storageName = "memory (this visit only)";

                if (!Application.isEditor)
                {
                    WowLog.Warn("Save: SomniumBridge.LocalStorage is null in the client - " + what + " are kept for this visit only.");
                }
            }

            return slots.ToArray();
        }

        /// <summary>The id's first four characters for the log, never the whole id.</summary>
        public static string Mask(string id)
        {
            return string.IsNullOrEmpty(id) ? "none" : id.Length <= 4 ? id : id.Substring(0, 4) + "...";
        }
    }
}
