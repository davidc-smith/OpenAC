using System;
using System.Collections.Generic;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;
using RynthCore.Engine.UI.Sections;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore's /rc commands. OpenAC has only their Mini Remote branch
    /// (/rc miniremote|remote [show|hide|toggle|tab &lt;tab&gt;|section &lt;id&gt;]) /rc inventory, /rc hotkeys and /rc hotkey, with their texts;
    /// every other /rc verb says it isn't here.
    /// </summary>
    internal static class RynthCoreChatCommands
    {
        /// <summary>Handles the text after "/rc". Always true (the verb is ours).</summary>
        public static bool TryHandle(string arguments, Action<string> reply)
        {
            string sub = (arguments ?? string.Empty).Trim();
            if (sub.Length == 0)
            {
                reply("Usage: /rc miniremote [show|hide|toggle|tab <tab>|section <id>] | inventory | hotkeys | hotkey <list|add|remove|...>");
                return true;
            }
            if (IsVerb(sub, "inventory", out _) || IsVerb(sub, "inv", out _))
            {
                // The ImGui Inventory panel (open/close), like its bar button.
                if (!ImGuiPanelHost.HasFace(InventoryFace.Title))
                    reply("The Inventory panel isn't available (the ImGui layer is off?).");
                else
                    UI.PanelRouter.Toggle(InventoryFace.Title);
                return true;
            }
            // /rc hotkeys : Settings > Hotkeys. /rc hotkey list|add|remove|enable|disable|actions|status|on|off
            if (IsVerb(sub, "hotkeys", out string hotkeyArgs) && hotkeyArgs.Length == 0)
            {
                if (!ImGuiPanelHost.HasFace(SettingsFace.Title))
                    reply("The Settings panel isn't available (the ImGui layer is off?). /rc hotkey list|add|remove work here in chat.");
                else
                {
                    SettingsFace.RequestTab(HotkeysSettingsUi.TabName);
                    if (!ImGuiPanelHost.IsOpen(SettingsFace.Title))
                        UI.PanelRouter.Toggle(SettingsFace.Title);
                }
                return true;
            }
            if (IsVerb(sub, "hotkey", out hotkeyArgs) || IsVerb(sub, "hotkeys", out hotkeyArgs))
            {
                foreach (string line in Hotkeys.HotkeyService.HandleCommand(hotkeyArgs))
                    reply(line);
                return true;
            }
            if (!IsVerb(sub, "miniremote", out string remoteArgs) && !IsVerb(sub, "remote", out remoteArgs))
            {
                string verb = sub.Split(' ', 2)[0];
                reply($"/rc {verb} isn't in OpenAC (only /rc miniremote, /rc inventory, /rc hotkeys and /rc hotkey).");
                return true;
            }

            if (IsVerb(remoteArgs, "tab", out string moved) && MovedTabCommand(moved) is string movedTo)
                UI.ServerMenus.Run(movedTo);
            else if (!ImGuiPanelHost.HasFace(MiniRemoteFace.Title))
                reply("The Mini Remote panel isn't available (the ImGui layer is off?).");
            else if (IsVerb(remoteArgs, "tab", out string tab)
                     && tab.Length > 0 && MiniRemoteFace.TryRequestSurface("section:" + tab.Trim().ToLowerInvariant()))
                ImGuiPanelHost.Open(MiniRemoteFace.Title);
            else if (IsVerb(remoteArgs, "tab", out tab))
            {
                MiniRemoteFace.RequestSection(SectionCatalog.RemoteId);
                ImGuiPanelHost.Open(MiniRemoteFace.Title);
                RynthAiCommands.ApplyRemoteCommand("hub", tab.Length > 0 ? "tab " + tab : "tab");
            }
            else if (IsVerb(remoteArgs, "section", out string section))
            {
                string id = SectionManifestParser.Id(section);
                SectionRegistry.Snapshot snap = SectionRegistry.Current;
                if (id != SectionCatalog.RemoteId && snap.Find(id) == null)
                {
                    var names = new List<string> { SectionCatalog.RemoteId };
                    foreach (SectionView v in snap.Sections) names.Add(v.Id);
                    reply($"Usage: /rc miniremote section <{string.Join("|", names)}>");
                }
                else
                {
                    MiniRemoteFace.RequestSection(id);
                    ImGuiPanelHost.Open(MiniRemoteFace.Title);
                }
            }
            else if (!TryPanelMode(remoteArgs, out bool? show))
                reply("Usage: /rc miniremote [show|hide|toggle|tab <tab>|section <id>]");
            else
                SetPanelOpen(MiniRemoteFace.Title, show);
            return true;
        }

        /// <summary>RynthCore's: "" and "toggle" toggle (null), show|on|open true, hide|off|close false.</summary>
        private static bool TryPanelMode(string args, out bool? show)
        {
            show = null;
            switch (args.Trim().ToLowerInvariant())
            {
                case "": case "toggle": return true;
                case "show": case "on": case "open": show = true; return true;
                case "hide": case "off": case "close": show = false; return true;
                default: return false;
            }
        }

        /// <summary>Opens (true), closes (false) or toggles (null) an ImGui panel, like its bar button.</summary>
        private static void SetPanelOpen(string title, bool? show)
        {
            if (show == null || show.Value != ImGuiPanelHost.IsOpen(title))
                UI.PanelRouter.Toggle(title);
        }

        /// <summary>
        /// The command for a Mini Remote tab that moved out (RynthCore 2026.10.5.54): "character" (the generic
        /// overview) is RynthOracle's Character tab. Null for a tab still in the Mini Remote.
        /// </summary>
        internal static string? MovedTabCommand(string tab) => tab.Trim().ToLowerInvariant() switch
        {
            "character" or "char" => "/ro character",
            _ => null,
        };

        private static bool IsVerb(string sub, string verb, out string args)
        {
            args = string.Empty;
            if (sub.Equals(verb, StringComparison.OrdinalIgnoreCase)) return true;
            if (!sub.StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase)) return false;
            args = sub.Substring(verb.Length + 1).Trim();
            return true;
        }
    }
}
