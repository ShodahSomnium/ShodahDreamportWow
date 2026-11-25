using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW
{
    /// <summary>
    /// A Somnium world ships as an asset bundle of the scene and what it references, so a custom shader only
    /// reaches the upload through a material the scene references. WowGame holds every carrier material in a
    /// serialized array and calls Carry at boot; everything else asks Find(name). Shader.Find is the last resort
    /// (it only finds shaders the client itself ships, e.g. stock URP). Each name logs once where it came from.
    /// </summary>
    public static class WowShaders
    {
        static readonly Dictionary<string, Shader> Carried = new Dictionary<string, Shader>();
        static readonly Dictionary<string, Material> Carriers = new Dictionary<string, Material>();
        static readonly HashSet<string> Logged = new HashSet<string>();

        public static void Carry(Material[] carriers)
        {
            if (carriers == null) return;
            var names = new List<string>();
            foreach (var m in carriers)
            {
                if (m == null || m.shader == null) continue;
                Carried[m.shader.name] = m.shader;
                Carriers[m.shader.name] = m;
                names.Add(m.shader.name);
            }
            WowLog.Info("Shaders carried: " + (names.Count == 0 ? "none" : string.Join(", ", names)));
        }

        /// <summary>The carried shader by its ShaderLab name, else Shader.Find, else null.</summary>
        public static Shader Find(string name)
        {
            Shader s;
            string source;
            if (Carried.TryGetValue(name, out s) && s != null) source = "carried";
            else { s = Shader.Find(name); source = s != null ? "Shader.Find" : "MISSING"; }
            if (Logged.Add(name)) WowLog.Info("Shader '" + name + "': " + source);
            return s;
        }

        /// <summary>The carrier material itself (for copying its keywords/settings), or null.</summary>
        public static Material Carrier(string shaderName)
        {
            Material m;
            return Carriers.TryGetValue(shaderName, out m) ? m : null;
        }
    }
}
