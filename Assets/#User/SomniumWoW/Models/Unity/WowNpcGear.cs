using System;
using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Puts a humanoid NPC's helm and shoulder models (NpcGear plan) on the body's attachments 11 / 6 / 5 with identity local
    /// transform (as the held weapons). A rigid piece (NpcGear.IsRigid - 1322 of the 1806 helm / shoulder files NPCs use, harness
    /// NpcGearRigid) is built ONCE per model + texture and then drawn as a plain MeshRenderer sharing that mesh and its (shared)
    /// materials: no animator, no skinning, no per-NPC mesh. The others (animated bones, emitters, uv / colour animation) are built
    /// per NPC with M2Factory.Build like a weapon. Main thread only; the cache lives as long as M2Factory's (app lifetime).
    /// </summary>
    public static class WowNpcGear
    {
        sealed class Piece
        {
            public Mesh Mesh;               // rigid: the shared rest mesh (owned here)
            public Material[] Materials;
            public bool PerInstance;        // not rigid: M2Factory.Build for every NPC
            public string Error;
        }

        static readonly Dictionary<string, Piece> Pieces = new Dictionary<string, Piece>(StringComparer.OrdinalIgnoreCase);
        static int _shared, _instanced;

        /// <summary>Attaches the plan's helm and shoulders to a body built by WowCharacter.BuildBody. Returns a short note for the log.</summary>
        public static string Attach(WowFiles files, GameObject body, NpcGearPlan plan)
        {
            if (body == null || plan == null || !plan.Any) return null;
            var anim = body.GetComponent<M2Animator>();
            if (anim == null) return "no animator";
            string note = null;
            if (plan.HelmModel != null) note += Put(files, anim, NpcGear.AttachHelm, plan.HelmModel, plan.HelmTexture, "Helm");
            if (plan.LeftShoulderModel != null) note += Put(files, anim, NpcGear.AttachLeftShoulder, plan.LeftShoulderModel, plan.LeftShoulderTexture, "Shoulder L");
            if (plan.RightShoulderModel != null) note += Put(files, anim, NpcGear.AttachRightShoulder, plan.RightShoulderModel, plan.RightShoulderTexture, "Shoulder R");
            return note;
        }

        /// <summary>Cache counts for the log.</summary>
        public static string StatsLine()
        {
            return "NPC gear: " + Pieces.Count + " pieces cached, " + _shared + " shared-mesh pieces placed, " + _instanced + " built per NPC";
        }

        static string Put(WowFiles files, M2Animator anim, int attachment, string path, string texture, string label)
        {
            var point = anim.Attachment(attachment);
            if (point == null) return " no attachment " + attachment + " for " + label + ";";
            var piece = Get(files, path, texture);
            GameObject go = null;
            if (piece.Mesh != null)
            {
                go = new GameObject(label);
                go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = piece.Materials;
                _shared++;
            }
            else if (piece.PerInstance)
            {
                go = M2Factory.Build(files, path, Options(label, texture));
                if (go != null) _instanced++;
            }
            if (go == null) return " " + label + " " + NpcGear.Short(path) + " not built" + (piece.Error != null ? " (" + piece.Error + ")" : "") + ";";
            go.transform.SetParent(point, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return null;
        }

        static M2BuildOptions Options(string label, string texture)
        {
            var o = new M2BuildOptions { Name = label };
            if (!string.IsNullOrEmpty(texture)) o.ReplaceablePaths[2] = texture;
            return o;
        }

        static Piece Get(WowFiles files, string path, string texture)
        {
            string key = path + "|" + texture;
            Piece p;
            if (Pieces.TryGetValue(key, out p))
            {
                if (p.Materials == null) return p;                      // built per NPC, or failed
                bool lost = p.Mesh == null;                              // destroyed (Unity null): build the template again
                for (int i = 0; i < p.Materials.Length && !lost; i++) lost = p.Materials[i] == null;
                if (!lost) return p;
            }
            p = new Piece();
            Pieces[key] = p;
            M2Model m; M2Skin s; string err;
            if (!M2Factory.TryGetParsed(files, path, out m, out s, out err)) { p.Error = err; return p; }
            if (!NpcGear.IsRigid(m)) { p.PerInstance = true; return p; }

            // Rigid: build once, keep the rest mesh + materials, drop the template (its bones stand at their pivots = the bind pose).
            var template = M2Factory.Build(files, path, Options("NPC gear template", texture));
            if (template == null) { p.Error = "M2Factory.Build failed"; return p; }
            template.SetActive(false);
            Renderer r = template.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Mesh mesh = r != null ? ((SkinnedMeshRenderer)r).sharedMesh : null;
            if (r == null)
            {
                var mf = template.GetComponentInChildren<MeshFilter>(true);
                r = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                mesh = mf != null ? mf.sharedMesh : null;
            }
            if (r == null || mesh == null) { p.Error = "nothing drawable"; UnityEngine.Object.Destroy(template); return p; }
            var owned = template.GetComponent<M2OwnedAssets>();
            if (owned != null)
            {
                owned.Owned.Remove(mesh);
                foreach (var o in owned.Owned) if (o is Material) { p.PerInstance = true; break; }   // unshared materials: not ours to keep
            }
            if (p.PerInstance) { if (owned != null) owned.Owned.Add(mesh); UnityEngine.Object.Destroy(template); return p; }
            p.Mesh = mesh;
            p.Materials = r.sharedMaterials;
            UnityEngine.Object.Destroy(template);
            return p;
        }
    }
}
