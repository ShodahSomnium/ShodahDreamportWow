using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// 13.8 (the user: "we now have a REALLY cool lobby for this dreamport. Please make it so if you are on whitelist you load inside the
    /// WOW_Lobby, make sure they spawn on the centre star ... If you're not on the whitelist, you just see a normal plane like before.
    /// Please place the MENU on the doorway"). The artist's lobby (Main Build: Assets/Assets[Platforms]/DreamPortAreas/WOW_Lobby/WOW_Lobby.unity
    /// = one WOW_LobbyAssets prefab instance, its 63 assets copied to Assets/WowLobby with their GUIDs) merged into the world's scene
    /// under this root, the lobby's file IDs and baked lighting kept (the scene's .meta took the lobby scene's GUID: its LightingData is
    /// bound to it). The root is INACTIVE in the scene file: a visitor never sees any of it (WowGame shows them the plain plane);
    /// WowGame shows it for an allowed player in the menu and hides it in the world.
    ///
    /// Measured on WOW_Lobby.fbx (scratchpad lobby/fbx.py; the file is metres, Y-up, Unity mirrors x) - in this root's space, which is
    /// the model's (the prefab places WOW_Lobby at its root's origin):
    /// - the star: the floor's "Props" material (the compass inlay) spans x -4.36..4.36, z -10.06..-1.34 at y 0 - centre (0, 0, -5.70);
    /// - the menu door (the artist's "Menu_Brazier_Dais_Cyan" lights at x 1.7 / 6.33 flank it): the opening's face at z -12.332, x
    ///   2.796..5.204, straight sides from y 1.05 (top of the steps) to 3.53, the arch to 4.83, facing +z (into the hall).
    /// The root is placed so the star is WowSpace.LoginRoomOrigin (where the black room stood).
    /// </summary>
    public sealed class WowLobby : MonoBehaviour
    {
        public static readonly Vector3 StarLocal = new Vector3(0f, 0f, -5.70f);
        /// <summary>The menu's centre: in the door's straight part, 5 cm in front of its face.</summary>
        public static readonly Vector3 MenuLocal = new Vector3(4.0f, 2.1f, -12.28f);
        public const float DoorWidth = 2.408f;
        /// <summary>The menu's width in the door (ours: 5 cm clear of each side).</summary>
        public const float MenuWidth = 2.3f;

        [SerializeField] private Material _sky;   // WOW_Skybox.mat (the lobby scene's RenderSettings skybox)

        public Material Sky { get { return _sky; } }
        public bool Shown { get { return gameObject.activeSelf; } }

        private static WowLobby _shown;

        /// <summary>A lobby is up (the menu room is the lobby): the login music stays off, the lobby's own sounds play.</summary>
        public static bool AnyShown { get { return _shown != null && _shown.gameObject.activeSelf; } }

        /// <summary>The lobby of the scene (inactive until shown), or null in a scene without one.</summary>
        public static WowLobby Find()
        {
            return FindFirstObjectByType<WowLobby>(FindObjectsInactive.Include);
        }

        public void Show(bool on)
        {
            if (on) _shown = this;
            if (gameObject.activeSelf == on) return;
            gameObject.SetActive(on);
            WowLog.Info("Lobby: " + (on ? "shown (spawn on the star at " + Star.ToString("F2") + ", the menu in the door at " + Menu.ToString("F2") + ")" : "hidden") + ".");
        }

        /// <summary>The star's centre (world): the spawn point.</summary>
        public Vector3 Star { get { return transform.TransformPoint(StarLocal); } }

        /// <summary>The menu's centre (world).</summary>
        public Vector3 Menu { get { return transform.TransformPoint(MenuLocal); } }

        /// <summary>The menu faces into the hall (the door's +z).</summary>
        public Quaternion MenuRotation { get { return transform.rotation * Quaternion.Euler(0f, 180f, 0f); } }

        /// <summary>Standing on the star, facing the menu door (degrees about y).</summary>
        public float SpawnYaw
        {
            get
            {
                Vector3 d = Menu - Star;
                d.y = 0f;
                return d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
            }
        }
    }
}
