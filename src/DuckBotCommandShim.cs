using Oxide.Core;
using Oxide.Core.Plugins;
using System;

namespace Oxide.Plugins
{
    [Info("RustDuckBotCommandShim", "1.0.0", "Duckets")]
    [Description("Emergency /db command shim for RustDuckBot.")]
    public class RustDuckBotCommandShim : RustPlugin
    {
        [PluginReference]
        private Plugin RustDuckBot;

        [ChatCommand("db")]
        private void CmdDb(BasePlayer player, string command, string[] args)
        {
            Dispatch(player, command, args);
        }

        [ChatCommand("duckbot")]
        private void CmdDuckbot(BasePlayer player, string command, string[] args)
        {
            Dispatch(player, command, args);
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null || !IsDuckBotCommand(command)) return null;
            Dispatch(player, command, args);
            return true;
        }

        private void Dispatch(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args == null) args = Array.Empty<string>();

            var plugin = ResolveRustDuckBot();
            if (plugin != null)
            {
                try
                {
                    var result = plugin.Call("CmdDuckBotShim", player, command, args);
                    if (result != null) return;
                }
                catch (Exception ex)
                {
                    PrintWarning("RustDuckBot delegate failed: " + ex.Message);
                }
            }

            PrintToChat(player, "<color=#FFD700>DuckBot:</color> RustDuckBot is not active. The /db shim loaded, so check Oxide compiler logs and make sure RustDuckBot.cs is installed next to RustDuckBotCommandShim.cs.");
        }

        private Plugin ResolveRustDuckBot()
        {
            if (RustDuckBot != null) return RustDuckBot;

            try
            {
                if (Manager == null) return null;

                var plugin = Manager.GetPlugin("RustDuckBot") as Plugin;
                if (plugin != null) return plugin;

                plugin = Manager.GetPlugin("DuckBotMod") as Plugin;
                if (plugin != null) return plugin;

                return Manager.GetPlugin("RustDuckBotMod") as Plugin;
            }
            catch (Exception ex)
            {
                PrintWarning("RustDuckBot lookup failed: " + ex.Message);
                return null;
            }
        }

        private bool IsDuckBotCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            var normalized = command.Trim().TrimStart('/').ToLowerInvariant();
            return normalized == "db" || normalized == "duckbot";
        }
    }
}
