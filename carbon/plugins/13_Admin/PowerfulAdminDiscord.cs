using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.Builders;
using Oxide.Ext.Discord.Clients;
using Oxide.Ext.Discord.Connections;
using Oxide.Ext.Discord.Entities;
using Oxide.Ext.Discord.Interfaces;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PowerfulAdmin Discord", "codeboy", "1.1.0")]
    [Description("Optional Discord bot companion for PowerfulAdmin")]
    public class PowerfulAdminDiscord : RustPlugin, IDiscordPlugin
    {
        public DiscordClient Client { get; set; }

        [PluginReference] private Plugin PowerfulAdmin;

        private readonly HashSet<string> _registeredGuilds = new HashSet<string>();

        private readonly Dictionary<string, DiscordGuild> _guilds = new Dictionary<string, DiscordGuild>();

        private enum BotStatus { Offline, Connecting, Online, Error }
        private BotStatus _status = BotStatus.Offline;

        private Timer _statusTimer;

        private bool _msgContentFailed;

        private int _connectGen;

        #region Settings bridge (read PowerfulAdmin's config via Call)

        private string PaInput(string key) => PowerfulAdmin?.Call("API_GetInput", key) as string ?? string.Empty;
        private bool PaToggle(string key, bool def) => PowerfulAdmin?.Call("API_GetToggle", key, def) as bool? ?? def;
        private string PaSelect(string key, string def) => PowerfulAdmin?.Call("API_GetSelect", key, def) as string ?? def;

        private void SetStatus(BotStatus status)
        {
            _status = status;
            PowerfulAdmin?.Call("API_SetBotStatus", status.ToString());
        }

        #endregion

        #region Connect

        private void OnDiscordClientCreated() => ConnectBot();

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "PowerfulAdmin")
            {
                SetStatus(_status);
                ConnectBot();
                PushAllGuilds();
            }
        }

        private void Unload()
        {
            _statusTimer?.Destroy();
            SetStatus(BotStatus.Offline);
        }

        private object API_RequestSync()
        {
            PushAllGuilds();
            return null;
        }

        private void PushAllGuilds()
        {
            PushGuilds();
            var want = PaInput("d.guild").Trim();
            foreach (var g in _guilds.Values)
            {
                if (want.Length > 0 && g.Id.ToString() != want)
                    continue;
                PushRoles(g);
                PushChannels(g);
            }
        }

        private void ConnectBot()
        {
            if (Client == null)
                return;
            if (Client.IsConnected())
                return;
            if (PowerfulAdmin == null)
            {
                PrintWarning("PowerfulAdmin not loaded yet - bot idle (will connect once it loads).");
                return;
            }

            if (!PaToggle("d.enabled", true) || !PaToggle("d.bot", false))
            {
                Puts("Discord bot is disabled in PowerfulAdmin settings - bot idle.");
                SetStatus(BotStatus.Offline);
                return;
            }

            var token = PaInput("d.token");
            if (string.IsNullOrEmpty(token))
            {
                Puts("No bot token set (PowerfulAdmin -> Settings -> Discord -> Bot) - bot idle.");
                SetStatus(BotStatus.Offline);
                return;
            }

            try
            {
                Puts("Connecting bot...");
                SetStatus(BotStatus.Connecting);
                var intents = GatewayIntents.Guilds | GatewayIntents.GuildMessages;
                var wantMsg = PaToggle("d.dcgame", false) && !_msgContentFailed;
                if (wantMsg)
                    intents |= GatewayIntents.MessageContent;
                Client.Connect(new BotConnection
                {
                    ApiToken = token,
                    Intents = intents
                });

                if (wantMsg)
                {
                    var gen = ++_connectGen;
                    timer.Once(12f, () =>
                    {
                        if (gen != _connectGen)
                            return;
                        if (Client != null && Client.IsConnected())
                            return;
                        _msgContentFailed = true;
                        PrintWarning("Bot didn't come online with the Message Content intent - it's most likely NOT enabled in the Discord Developer Portal (Bot -> Privileged Gateway Intents). Reconnecting WITHOUT it; the Discord -> game chat bridge needs that intent to work.");
                        SetStatus(BotStatus.Error);
                        try { if (Client != null && Client.IsConnected()) Client.Disconnect(); } catch { }
                        ConnectBot();
                    });
                }
            }
            catch (Exception ex)
            {
                PrintWarning("bot connect failed: " + ex);
                SetStatus(BotStatus.Error);
            }
        }

        private void OnDiscordGatewayReady(GatewayReadyEvent ready)
        {
            Puts("Bot is online (gateway ready).");
            _connectGen++;
            SetStatus(BotStatus.Online);
            UpdateBotStatus();
            _statusTimer?.Destroy();
            _statusTimer = timer.Every(30f, UpdateBotStatus);
        }

        private void UpdateBotStatus()
        {
            if (Client == null || !Client.IsConnected())
                return;
            var text = PaInput("d.actext").Trim();
            if (text.Length == 0)
                return;
            text = ApplyStatusPlaceholders(text);
            var type = PaSelect("d.actype", "playing") switch
            {
                "watching" => ActivityType.Watching,
                "listening" => ActivityType.Listening,
                "competing" => ActivityType.Competing,
                _ => ActivityType.Playing
            };
            try
            {
                Client.UpdateStatus(new UpdatePresenceCommand
                {
                    Status = UserStatusType.Online,
                    Activities = new List<DiscordActivity> { new DiscordActivity { Name = text, Type = type } }
                });
            }
            catch (Exception ex)
            {
                PrintWarning("status update failed: " + ex);
            }
        }

        private string ApplyStatusPlaceholders(string text)
        {
            var online = BasePlayer.activePlayerList.Count;
            var max = ConVar.Server.maxplayers;
            int queue = 0, joining = 0;
#if DEBUG_SERVER
            queue = 5; joining = 2;
#else
            try
            {
                var q = ServerMgr.Instance?.connectionQueue;
                if (q != null) { queue = q.Queued; joining = q.Joining; }
            }
            catch { }
#endif

            return text.Replace("{online}", online.ToString())
                .Replace("{maxonline}", max.ToString())
                .Replace("{queue}", queue.ToString())
                .Replace("{connecting}", joining.ToString());
        }

        [ConsoleCommand("pa.dcconnect")]
        private void CmdReconnect(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, "powerfuladmin.settings"))
                return;
            ConnectBot();
        }

        private object API_BotConnect() { ConnectBot(); return null; }

        private object API_BotDisconnect()
        {
            _connectGen++;
            _statusTimer?.Destroy();
            _statusTimer = null;
            try { if (Client != null && Client.IsConnected()) Client.Disconnect(); }
            catch (Exception ex) { PrintWarning("bot disconnect failed: " + ex); }
            SetStatus(BotStatus.Offline);
            return null;
        }

        private object API_UpdateStatus() { UpdateBotStatus(); return null; }

        private object API_BotReconnect()
        {
            _connectGen++;
            _msgContentFailed = false;
            _statusTimer?.Destroy();
            _statusTimer = null;
            try { if (Client != null && Client.IsConnected()) Client.Disconnect(); }
            catch (Exception ex) { PrintWarning("bot reconnect (disconnect) failed: " + ex); }
            SetStatus(BotStatus.Connecting);
            timer.Once(3f, ConnectBot);
            return null;
        }

        #endregion

        #region Guild ready -> register commands + push roles

        private void OnDiscordGuildCreated(DiscordGuild guild)
        {
            if (guild == null)
                return;
            _guilds[guild.Id.ToString()] = guild;

            PushGuilds();

            var want = PaInput("d.guild").Trim();
            if (want.Length > 0 && guild.Id.ToString() != want)
                return;

            PushRoles(guild);
            PushChannels(guild);
            RegisterCommands(guild);
        }

        private void PushRoles(DiscordGuild guild)
        {
            if (PowerfulAdmin == null || guild.Roles == null)
                return;
            var sb = new System.Text.StringBuilder();
            foreach (var role in guild.Roles.Values
                         .Where(r => r.Id != guild.Id)
                         .OrderByDescending(r => r.Position))
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(role.Id.ToString()).Append('\t').Append(role.Name).Append('\t').Append(role.Color.ToHex());
            }

            PowerfulAdmin.Call("API_SetRoles", sb.ToString());
            Puts($"Pushed {guild.Roles.Count} guild roles to PowerfulAdmin for the role dropdowns.");
        }

        private void PushChannels(DiscordGuild guild)
        {
            if (PowerfulAdmin == null || guild.Channels == null)
                return;
            var sb = new System.Text.StringBuilder();
            foreach (var ch in guild.Channels.Values)
            {
                if (ch.Type != ChannelType.GuildText)
                    continue;
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(ch.Id.ToString()).Append('\t').Append(ch.Name);
            }

            PowerfulAdmin.Call("API_SetChannels", sb.ToString());
        }

        private void PushGuilds()
        {
            if (PowerfulAdmin == null)
                return;
            var sb = new System.Text.StringBuilder();
            foreach (var g in _guilds.Values)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(g.Id.ToString()).Append('\t').Append(g.Name);
            }

            PowerfulAdmin.Call("API_SetGuilds", sb.ToString());
        }

        private void RegisterCommands(DiscordGuild guild)
        {
            if (Client?.Bot == null)
                return;
            var gid = guild.Id.ToString();
            if (!_registeredGuilds.Add(gid))
                return;

            var commands = new List<CommandCreate>();
            if (PaToggle("sl.online", true))
                commands.Add(new ApplicationCommandBuilder("online", "Show the current online player count", ApplicationCommandType.ChatInput).Build());
            if (PaToggle("sl.players", true))
                commands.Add(new ApplicationCommandBuilder("players", "List players currently online", ApplicationCommandType.ChatInput).Build());
            if (PaToggle("sl.kick", true))
            {
                var kick = new ApplicationCommandBuilder("kick", "Kick an online player (admin role required)", ApplicationCommandType.ChatInput)
                    .AddOption(CommandOptionType.String, "nick", "Player name or steamid", o => o.Required(true).AutoComplete(true));
                kick.AddOption(CommandOptionType.String, "template", "Reason template", o => { foreach (var t in TemplateChoices()) o.AddChoice(t, t); });
                kick.AddOption(CommandOptionType.String, "reason", "Custom reason (overrides template)", o => o.Required(false));
                kick.AddOption(CommandOptionType.String, "comment", "Internal admin note", o => o.Required(false));
                commands.Add(kick.Build());
            }
            if (PaToggle("sl.mute", true))
            {
                var mute = new ApplicationCommandBuilder("mute", "Mute an online player (admin role required)", ApplicationCommandType.ChatInput)
                    .AddOption(CommandOptionType.String, "nick", "Player name or steamid", o => o.Required(true).AutoComplete(true));
                mute.AddOption(CommandOptionType.String, "duration", "Mute length (default permanent)", o =>
                {
                    foreach (var d in new[] { "10m", "30m", "1h", "6h", "12h", "1d", "7d", "perm" }) o.AddChoice(d, d);
                });
                mute.AddOption(CommandOptionType.String, "template", "Reason template", o => { foreach (var t in TemplateChoices()) o.AddChoice(t, t); });
                mute.AddOption(CommandOptionType.String, "reason", "Custom reason (overrides template)", o => o.Required(false));
                mute.AddOption(CommandOptionType.String, "comment", "Internal admin note", o => o.Required(false));
                commands.Add(mute.Build());
            }
            var bridge = new ApplicationCommandBuilder("chatbridge", "Enable/disable the Discord -> game chat relay (super-role only)", ApplicationCommandType.ChatInput);
            bridge.AddOption(CommandOptionType.String, "state", "Turn the bridge on or off", o =>
            {
                o.Required(true);
                o.AddChoice("on", "on");
                o.AddChoice("off", "off");
            });
            commands.Add(bridge.Build());

            foreach (var cmd in commands)
            {
                Client.Bot.Application.CreateGuildCommand(Client, guild.Id, cmd)
                    .Then(c => Puts($"Registered /{c.Name} for guild {gid}."))
                    .Catch(err => PrintWarning($"Failed to register /{cmd.Name}: {err}"));
            }

            if (commands.Count == 0)
                Puts("No slash commands enabled (Settings -> Discord -> Bot -> Slash) - none registered.");
        }

        #endregion

        #region Slash command handlers

        [DiscordApplicationCommand("online")]
        private void CmdSlashOnline(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            if (!PaToggle("sl.online", true)) { Reply(interaction, "This command is disabled."); return; }
            var online = BasePlayer.activePlayerList.Count;
            var max = ConVar.Server.maxplayers;
            var sleepers = BasePlayer.sleepingPlayerList?.Count ?? 0;
            Reply(interaction, $"**Online:** {online}/{max}   **Sleepers:** {sleepers}");
        }

        [DiscordApplicationCommand("players")]
        private void CmdSlashPlayers(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            if (!PaToggle("sl.players", true)) { Reply(interaction, "This command is disabled."); return; }
            var list = BasePlayer.activePlayerList;
            if (list.Count == 0) { Reply(interaction, "No players online."); return; }

            const int cap = 40;
            var lines = list.Take(cap).Select(b =>
                $"{b.displayName} ([{b.userID}](https://steamcommunity.com/profiles/{b.userID}))");
            var text = $"**Online players ({list.Count}):**\n" + string.Join("\n", lines);
            if (list.Count > cap)
                text += $"\n... +{list.Count - cap} more";
            if (text.Length > 1900)
                text = text.Substring(0, 1900) + "...";
            Reply(interaction, text);
        }

        [DiscordApplicationCommand("kick")]
        private void CmdSlashKick(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            if (!PaToggle("sl.kick", true)) { Reply(interaction, "This command is disabled."); return; }
            if (!HasCmdRole(interaction, "kick")) { Reply(interaction, "⛔ You don't have the role required to use admin commands."); return; }
            if (PowerfulAdmin == null) { Reply(interaction, "PowerfulAdmin is not loaded."); return; }

            var nick = parsed.Args.GetString("nick");
            var template = parsed.Args.GetString("template");
            var reason = parsed.Args.GetString("reason");
            var comment = parsed.Args.GetString("comment");
            if (string.IsNullOrEmpty(reason))
                reason = template;
            var by = interaction.Member?.User?.Username ?? interaction.User?.Username ?? "Discord";

            var result = PowerfulAdmin.Call("API_Kick", nick, reason, by, comment) as string ?? "Kick failed.";
            Reply(interaction, result);
        }

        [DiscordAutoCompleteCommand("kick", "nick")]
        private void AutoCompleteKickNick(DiscordInteraction interaction, InteractionDataOption focused)
        {
            var typed = (focused?.GetString() ?? string.Empty).ToLower();
            var builder = new InteractionAutoCompleteBuilder(interaction);
            var added = 0;
            foreach (var b in BasePlayer.activePlayerList)
            {
                if (b == null)
                    continue;
                var name = b.displayName ?? string.Empty;
                if (typed.Length > 0 && name.ToLower().IndexOf(typed) < 0 && !b.UserIDString.Contains(typed))
                    continue;
                builder.AddChoice($"{name} ({b.userID})", b.UserIDString);
                if (++added >= 25)
                    break;
            }

            try { interaction.CreateResponse(Client, builder); } catch { }
        }

        [DiscordApplicationCommand("mute")]
        private void CmdSlashMute(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            if (!PaToggle("sl.mute", true)) { Reply(interaction, "This command is disabled."); return; }
            if (!HasCmdRole(interaction, "mute")) { Reply(interaction, "⛔ You don't have the role required to use admin commands."); return; }
            if (PowerfulAdmin == null) { Reply(interaction, "PowerfulAdmin is not loaded."); return; }

            var nick = parsed.Args.GetString("nick");
            var duration = parsed.Args.GetString("duration");
            var template = parsed.Args.GetString("template");
            var reason = parsed.Args.GetString("reason");
            var comment = parsed.Args.GetString("comment");
            if (string.IsNullOrEmpty(reason))
                reason = template;
            var by = interaction.Member?.User?.Username ?? interaction.User?.Username ?? "Discord";

            var result = PowerfulAdmin.Call("API_Mute", nick, duration, reason, by, comment) as string ?? "Mute failed.";
            Reply(interaction, result);
        }

        [DiscordAutoCompleteCommand("mute", "nick")]
        private void AutoCompleteMuteNick(DiscordInteraction interaction, InteractionDataOption focused)
        {
            var typed = (focused?.GetString() ?? string.Empty).ToLower();
            var builder = new InteractionAutoCompleteBuilder(interaction);
            var added = 0;
            foreach (var b in BasePlayer.activePlayerList)
            {
                if (b == null)
                    continue;
                var name = b.displayName ?? string.Empty;
                if (typed.Length > 0 && name.ToLower().IndexOf(typed) < 0 && !b.UserIDString.Contains(typed))
                    continue;
                builder.AddChoice($"{name} ({b.userID})", b.UserIDString);
                if (++added >= 25)
                    break;
            }

            try { interaction.CreateResponse(Client, builder); } catch { }
        }

        [DiscordApplicationCommand("chatbridge")]
        private void CmdSlashChatBridge(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            if (PowerfulAdmin == null) { Reply(interaction, "PowerfulAdmin is not loaded."); return; }
            if (!HasCmdRole(interaction, "chatbridge")) { Reply(interaction, "⛔ Only the super-role may change the chat bridge."); return; }
            var on = parsed.Args.GetString("state") == "on";
            PowerfulAdmin.Call("API_SetChatBridge", on);
            Reply(interaction, on
                ? "✅ Discord -> game chat bridge enabled. The bot reconnects to apply the Message Content intent (make sure it's enabled in the Developer Portal)."
                : "Discord -> game chat bridge disabled.");
        }

        #endregion

        #region Discord -> game chat relay

        private void OnDiscordGuildMessageCreated(DiscordMessage message)
        {
            if (PowerfulAdmin == null || message == null)
                return;
            if (!PaToggle("d.dcgame", false))
                return;
            if (message.Author == null || message.Author.Bot == true)
                return;
            var content = (message.Content ?? string.Empty).Trim();
            if (content.Length == 0)
                return;

            var chatChan = PaInput("d.cchat").Trim();
            if (chatChan.Length == 0 || message.ChannelId.ToString() != chatChan)
                return;

            var nick = message.Member?.Nickname;
            if (string.IsNullOrEmpty(nick))
                nick = message.Author.Username;

            string roleName = "", roleColor = "";
            try
            {
                if (_guilds.TryGetValue(message.GuildId.ToString(), out var guild)
                    && guild?.Roles != null && message.Member?.Roles != null)
                {
                    DiscordRole best = null;
                    foreach (var rid in message.Member.Roles)
                        if (guild.Roles.TryGetValue(rid, out var role) && role.Id != guild.Id
                            && (best == null || role.Position > best.Position))
                            best = role;
                    if (best != null) { roleName = best.Name; roleColor = best.Color.ToHex(); }
                }
            }
            catch (Exception ex) { PrintWarning("chat bridge role lookup failed: " + ex); }

            PowerfulAdmin.Call("API_RelayChatToGame", nick, roleName, roleColor, content);
        }

        #endregion

        #region Log transport (PowerfulAdmin -> here, when delivery uses the bot)

        private object API_BotSend(string channelId, string title, string desc, string content, string thumbUrl)
        {
            if (Client == null || !Client.IsConnected())
                return null;
            if (!ulong.TryParse((channelId ?? string.Empty).Trim(), out var cid) || cid == 0)
                return null;
            var channel = new Snowflake(cid);

            if (!string.IsNullOrEmpty(content))
            {
                DiscordMessage.Create(Client, channel, content)
                    .Catch(err => PrintWarning($"bot message failed: {err}"));
                return null;
            }

            var builder = new DiscordEmbedBuilder()
                .AddTitle(title ?? string.Empty)
                .AddDescription(desc ?? string.Empty);
            var color = PaInput("d.color").Trim();
            if (color.Length > 0)
            {
                try { builder.AddColor(color); }
                catch { }
            }

            var server = ConVar.Server.hostname ?? string.Empty;
            if (server.Length > 100)
                server = server.Substring(0, 100);
            var icon = !string.IsNullOrEmpty(thumbUrl) ? thumbUrl : PaInput("d.avatar").Trim();
            if (server.Length > 0)
            {
                try { builder.AddAuthor(server, null, icon.Length > 0 ? icon : null); }
                catch { }
            }

            var footer = PaInput("d.footer").Trim();
            if (footer.Length > 0)
                builder.AddFooter(footer);

            DiscordMessage.Create(Client, channel, builder.Build())
                .Catch(err => PrintWarning($"bot embed failed: {err}"));
            return null;
        }

        private object API_BotChat(string channelId, string name, string steamId, string message)
        {
            if (Client == null || !Client.IsConnected())
                return null;
            if (!ulong.TryParse((channelId ?? string.Empty).Trim(), out var cid) || cid == 0)
                return null;
            var builder = new DiscordEmbedBuilder()
                .AddAuthor(name, "https://steamcommunity.com/profiles/" + steamId, "https://companion-rust.facepunch.com/api/avatar/" + steamId)
                .AddDescription(message ?? string.Empty);
            var color = PaInput("d.color").Trim();
            if (color.Length > 0) { try { builder.AddColor(color); } catch { } }
            DiscordMessage.Create(Client, new Snowflake(cid), builder.Build())
                .Catch(err => PrintWarning($"bot chat relay failed: {err}"));
            return null;
        }

        #endregion

        #region Helpers

        private bool HasCmdRole(DiscordInteraction interaction, string cmd)
        {
            var roles = interaction.Member?.Roles;
            if (roles == null)
                return false;

            var super = PaInput("d.rsuper").Trim();
            if (super.Length > 0 && roles.Any(r => r.ToString() == super))
                return true;

            var key = cmd == "kick" ? "d.rkick" : cmd == "mute" ? "d.rmute" : cmd == "ban" ? "d.rban" : string.Empty;
            if (key.Length == 0)
                return false;
            var allowed = new HashSet<string>(PaInput(key)
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0));
            if (allowed.Count == 0)
                return false;
            return roles.Any(r => allowed.Contains(r.ToString()));
        }

        private List<string> TemplateChoices()
        {
            var list = new List<string>();
            foreach (var part in PaInput("sec.templates").Split('|'))
            {
                var p = part.Trim();
                if (p.Length > 0)
                    list.Add(p);
            }

            if (list.Count == 0)
                list.AddRange(new[] { "AFK", "Toxicity", "Cheating", "Suspicious" });
            return list.Count > 25 ? list.GetRange(0, 25) : list;
        }

        private void Reply(DiscordInteraction interaction, string text, bool ephemeral = true)
        {
            var builder = interaction.GetResponseBuilder().AddContent(text);
            if (ephemeral)
                builder.AsEphemeral();
            interaction.CreateResponse(Client, InteractionResponseType.ChannelMessageWithSource, builder);
        }

        #endregion
    }
}
