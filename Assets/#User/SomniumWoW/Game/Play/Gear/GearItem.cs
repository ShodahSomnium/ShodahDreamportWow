using System;
using System.Collections.Generic;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// A weapon model's measured shape, in the Unity space of the built item (metres; the root = the grip, WowItems' rule).
    /// Measured on the real files (probe GearAxes): every weapon runs along WoW +X from the grip = Unity +Z; blades are thin
    /// in WoW Y (Unity X) and wide in WoW Z (Unity Y); bow limbs run along +Z with the tips curving to Unity +Y (the string
    /// side); shields are thin in WoW Y with the face toward WoW +Y (Unity -X).
    /// </summary>
    public sealed class GearGeometry
    {
        public Vector3 Min, Max;           // bounds of the bind-pose vertices
        public float Length { get { return Max.z - Min.z; } }
        public Vector3 Center { get { return (Min + Max) * 0.5f; } }
        public float HalfWidth;            // half of the widest cross-section (Unity X/Y) = the hit capsule's radius (capped)
        public string ModelPath, Folder;

        public static GearGeometry Measure(M2Model m, string path, string folder)
        {
            var g = new GearGeometry { ModelPath = path, Folder = folder };
            if (m == null || m.VertexCount == 0) { g.Min = new Vector3(-0.02f, -0.02f, -0.1f); g.Max = new Vector3(0.02f, 0.02f, 0.5f); g.HalfWidth = 0.04f; return g; }
            var mn = new Vector3(1e9f, 1e9f, 1e9f);
            var mx = new Vector3(-1e9f, -1e9f, -1e9f);
            for (int v = 0; v < m.VertexCount; v++)
            {
                Vector3 p = WowSpace.PointToUnity(m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2]);
                mn = Vector3.Min(mn, p);
                mx = Vector3.Max(mx, p);
            }
            g.Min = mn;
            g.Max = mx;
            g.HalfWidth = Mathf.Clamp(0.5f * Mathf.Max(mx.x - mn.x, mx.y - mn.y), 0.03f, 0.12f);
            return g;
        }
    }

    /// <summary>Where a weapon hangs when sheathed (Item.dbc SheatheType, see GearPlace).</summary>
    public enum GearSpot { Hidden, BackTwoHand, BackStaff, HipLeft, HipRight, BackShield, BackRanged }

    /// <summary>One equipped weapon built as its WoW model: prepared on a worker thread (DBC lookup, M2 parse, measurement), built on the main thread.</summary>
    public sealed class GearItem
    {
        public WowWireItem Item;
        public int Slot;                    // EquipSlot: 15 main hand, 16 off hand, 17 ranged
        public GameObject Go;
        public Transform T;
        public GearGeometry Geo;
        public GearSpot Spot;
        public bool IsShield, IsBow, IsTwoHand;
        public string Error;

        private volatile int _state;        // 0 queued, 1 prepared, 2 failed
        private readonly WowFiles _files;
        private readonly string _owner;
        private bool _built;

        private static readonly Dictionary<long, GearGeometry> Measured = new Dictionary<long, GearGeometry>();
        private static readonly object Lock = new object();

        public GearItem(WowFiles files, WowWireItem item, int slot, string owner)
        {
            _files = files;
            Item = item;
            Slot = slot;
            _owner = owner;
            bool left = slot == 16;
            long key = ((long)item.DisplayId << 1) | (left ? 1L : 0L);
            lock (Lock)
            {
                GearGeometry g;
                if (Measured.TryGetValue(key, out g)) { Geo = g; _state = g == null ? 2 : 1; }
            }
            if (_state == 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    GearGeometry g = null;
                    try
                    {
                        var info = WowModelDb.ResolveItem(_files, item.DisplayId, left);
                        if (info.Error != null) Error = info.Error;
                        else if (!M2Factory.Preload(_files, info.ModelPath)) Error = "model " + info.ModelPath + " unreadable";
                        else g = GearGeometry.Measure(M2Factory.GetModel(_files, info.ModelPath), info.ModelPath, info.Folder);
                    }
                    catch (Exception e) { Error = e.Message; }
                    lock (Lock) Measured[key] = g;
                    Geo = g;
                    _state = g == null ? 2 : 1;
                });
            }
        }

        /// <summary>Builds the model once prepared (main thread). True when there is a model to place.</summary>
        public bool Ready()
        {
            if (_built) return Go != null;
            if (_state == 0) return false;
            _built = true;
            if (_state == 2 || Geo == null)
            {
                WowLog.Warn("Gear: " + _owner + " slot " + Slot + " display " + Item.DisplayId + " not built: " + (Error ?? "unknown"));
                return false;
            }
            Go = WowItems.Build(_files, Item.DisplayId, Slot == 16);
            if (Go == null) return false;
            Go.name = "Gear " + _owner + " " + Slot + " (" + Item.DisplayId + ")";
            T = Go.transform;
            IsShield = Item.InventoryType == 14 || Item.SheatheType == 4 || string.Equals(Geo.Folder, "Shield", StringComparison.OrdinalIgnoreCase);
            IsBow = Item.InventoryType == 15;
            IsTwoHand = Item.InventoryType == 17;
            Spot = GearPlace.SpotFor(this);
            if (_enchantVisual > 0) WowEnchantVisuals.Apply(_files, Go, _enchantVisual);   // iteration 11 (spellfx): a glow asked for before the build
            WowLog.Info("Gear: " + _owner + " " + GearPlace.SlotName(Slot) + " display " + Item.DisplayId + " (" + Geo.ModelPath + ", " + Geo.Length.ToString("0.00") + " m, grip " +
                        (-Geo.Min.z).ToString("0.00") + " m from the butt, SheatheType " + Item.SheatheType + ", InventoryType " + Item.InventoryType + ") sheathed at " + Spot);
            return true;
        }

        public void Destroy()
        {
            if (Go != null) UnityEngine.Object.Destroy(Go);
            Go = null;
            T = null;
        }

        /// <summary>Iteration 11 (spellfx, proposal-spellfx.md s1.3 / hook 7): show ItemVisuals id itemVisualId on this weapon (the glow of a
        /// weapon enchant, poison or imbue; WowEnchantVisuals.Apply on Go - now when built, else right after Ready() builds it); 0 = none.
        /// The same id twice does nothing (WowEnchantVisuals remembers what the weapon shows).</summary>
        public void SetEnchantVisual(int itemVisualId)
        {
            _enchantVisual = itemVisualId < 0 ? 0 : itemVisualId;
            if (Go != null) WowEnchantVisuals.Apply(_files, Go, _enchantVisual);
        }

        private int _enchantVisual;
    }
}
