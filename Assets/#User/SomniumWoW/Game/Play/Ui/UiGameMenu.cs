// iteration 11 - owner uicore (proposal-uicore.md 5.4.6): WoW's Game Menu (GameMenuFrame.xml): a registered Center window opened by
// Escape (the Escape chain, 5.1.7), the MainMenu micro button and the VR palm. Dialog backdrop, UI-DialogBox-Header 256 x 64 at TOP
// (0, 12) with MAIN_MENU (:3-43); UIPanelButtonTemplate 144 x 21 buttons from CENTER of TOP (0, -37), 1 apart, "Return to Game" 16 below
// (:47-231). Offered (ours, gap G2): Interface (the Settings window), Key Bindings (the read-only list), Logout (the character list), Exit
// Game (back to the room) - not Video / Sound (our Settings window holds them) / Macros / Ratings; the frame is 172 tall for them (ours).
using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiGameMenu : UiWindow
    {
        public const float Width = 195f, Height = 172f;   // GameMenuFrame 195 wide (:3-9); 172 tall for our 5 buttons (ours, WoW 240 for 10)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                // proposal-uicore.md 5.4.6: Escape (the Escape chain 5.1.7), the MainMenu micro button, the palm.
                Kind = UiWindowKind.GameMenu, Frame = "GameMenuFrame", TitleKey = "MAINMENU_BUTTON", Title = "Game Menu",   // GlobalStrings MAINMENU_BUTTON "Game Menu"
                Area = UiPanelArea.Center, Width = Width, Height = Height, WhileDead = true, Binding = "TOGGLEGAMEMENU", Micro = "MainMenu", MicroOrder = 8,
                OpenSound = "igMainMenuOpen", CloseSound = "igMainMenuQuit",   // SoundEntries 850 / 854 (DbcRows 2026-10-03)
                Owner = "uicore", Hidden = false,
                Build = (parent, ctx, close) => new UiGameMenu(parent, ctx, close)
            };
        }

        public UiGameMenu(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            W = Width;
            H = Height;
            Root = UiKit.Rect(parent, "GameMenuFrame", 0f, 0f, W, H);
            UiBackdrop back = UiBackdrop.Make(Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            back.Place(0f, 0f, W, H);
            UiKit.Pic(Root, "Header", UiArt.Tex(@"Interface\DialogFrame\UI-DialogBox-Header.blp"), (W - 256f) * 0.5f, -12f, 256f, 64f);
            TextMeshProUGUI title = UiKit.Text(Root, "Title", UiGlobalStrings.G("MAIN_MENU", "Main Menu"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, -12f + 14f - 4f, W, 18f);   // GlobalStrings.lua:4881
            title.textWrappingMode = TextWrappingModes.NoWrap;
            float x = (W - 144f) * 0.5f, y = 37f - 10.5f;
            UiKit.PanelButton(Root, UiGlobalStrings.G("UIOPTIONS_MENU", "Interface"), x, y, 144f, 21f, () => Switch(UiWindowKind.Settings));
            y += 22f;
            UiKit.PanelButton(Root, UiGlobalStrings.G("KEY_BINDINGS", "Key Bindings"), x, y, 144f, 21f, () => Switch(UiWindowKind.KeyBindings));
            y += 22f;
            UiKit.PanelButton(Root, UiGlobalStrings.G("LOGOUT", "Logout"), x, y, 144f, 21f, Logout);
            y += 22f;
            UiKit.PanelButton(Root, UiGlobalStrings.G("EXIT_GAME", "Exit Game"), x, y, 144f, 21f, Exit);
            y += 21f + 16f;
            UiKit.PanelButton(Root, UiGlobalStrings.G("RETURN_TO_GAME", "Return to Game"), x, y, 144f, 21f, () =>
            {
                UiSound.Named("igMainMenuContinue");   // GameMenuFrame.xml:228-231 (SoundEntries 855)
                if (close != null) close();
            });
            Root.gameObject.SetActive(false);
        }

        private void Switch(UiWindowKind to)
        {
            // WoW: the Game Menu hides first, then the panel opens (GameMenuButtonOptions / GameMenuButtonKeybindings OnClick).
            if (Ctx.Toggle == null) return;
            if (Ctx.IsOpen != null && Ctx.IsOpen(UiWindowKind.GameMenu)) Ctx.Toggle(UiWindowKind.GameMenu);
            if (Ctx.IsOpen == null || !Ctx.IsOpen(to)) Ctx.Toggle(to);
        }

        private void Logout()
        {
            WowLog.Info("UI: Game Menu > Logout.");
            if (Ctx.Toggle != null && Ctx.IsOpen != null && Ctx.IsOpen(UiWindowKind.GameMenu)) Ctx.Toggle(UiWindowKind.GameMenu);
            WowGame g = WowGame.Current;
            if (g != null) g.RequestLogout();
            else WowLog.Warn("UI: Logout - no game object.");
        }

        private void Exit()
        {
            WowLog.Info("UI: Game Menu > Exit Game (back to the room).");
            if (Ctx.Toggle != null && Ctx.IsOpen != null && Ctx.IsOpen(UiWindowKind.GameMenu)) Ctx.Toggle(UiWindowKind.GameMenu);
            WowGame.ReturnToRoomFromUi("the Game Menu's Exit Game");
        }

        public override void Refresh(float now) { }
    }
}
