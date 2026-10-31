using Oxide.Core;
using System;
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;
using UnityEngine; // Time.realtimeSinceStartup

namespace Oxide.Plugins
{
    [Info("PopControl", "Papa", "1.7.0")]
    [Description("A dynamic server population controlled based upon current occupancy.")]
    public class PopControl : CovalencePlugin
    {
        private const string DefaultColorLabel = "#ff2E36";
        private const string DefaultColorNumber = "#FFFFFF";
        private const string DefaultBotName = "Population Bot";
        private const ulong DefaultBotSteamId = 76561199680521298;

        private ConfigData _cfg;
        private Timer _debounceTimer;
        private Timer _broadcastTimer;
        private int _originalMaxPlayers;
        private bool _capturedOriginalMaxPlayers;

        // !pop cooldown tracker
        private double _nextPopAllowed;
        private double _nextChatSendErrorLog;

        private class ConfigData
        {
            // Elastic scaling
            public int   MaxPlayersOffset       = 1;
            public float CooldownPeriodSeconds  = 10f;
            public int   MaximumPopulation      = 200;
            public int   MinimumPopulation      = 100;
            public bool  ConsoleMessagesEnabled = true;
            public bool  NeverBelowOnline       = true;
            public bool  IncludeJoiningInScaling = true;
            public bool  IncludeQueuedInScaling  = false;
            public float QueuedWeight            = 1f;
            public int   QueueBuffer             = 0;

            // Chat features
            public bool  EnableChatCommands        = true;
            public bool  EnablePeriodicBroadcasts  = false;
            public float BroadcastIntervalSeconds  = 300f;

            // !pop global cooldown
            public float PopCommandCooldownSeconds = 10f;
            public bool  ShowPopCooldownFeedback   = true;

            // Colors (labels & numbers)
            public string ColorLabel  = DefaultColorLabel; // red highlight
            public string ColorNumber = DefaultColorNumber; // numbers

            // Bot cosmetics
            public string BotName     = DefaultBotName;

            // SteamID64 used to resolve the avatar shown in chat (0 = disabled / no forced icon)
            public ulong  BotSteamId  = DefaultBotSteamId;

            // NEW: When true, use the same chat.add signature that your working BetterBroadcast uses:
            //   chat.add, 2, <SteamID64>, <Message>
            // When false, use the name-override signature:
            //   chat.add, "<SteamID64>", "<BotName>", <Message>
            public bool   UseMode2ForIcon         = true;
        }

        #region Config
        protected override void LoadDefaultConfig()
        {
            _cfg = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _cfg = Config.ReadObject<ConfigData>() ?? new ConfigData();

            bool changed = false;

            if (_cfg.CooldownPeriodSeconds < 0.1f)     { _cfg.CooldownPeriodSeconds = 0.1f; changed = true; }
            if (_cfg.MaxPlayersOffset < 0)             { _cfg.MaxPlayersOffset = 0; changed = true; }
            if (_cfg.MinimumPopulation < 1)            { _cfg.MinimumPopulation = 1; changed = true; }
            if (_cfg.MaximumPopulation < _cfg.MinimumPopulation)
            { _cfg.MaximumPopulation = _cfg.MinimumPopulation; changed = true; }
            if (_cfg.QueuedWeight < 0f)              { _cfg.QueuedWeight = 0f; changed = true; }
            if (_cfg.QueuedWeight > 5f)              { _cfg.QueuedWeight = 5f; changed = true; }
            if (_cfg.QueueBuffer < 0)                { _cfg.QueueBuffer = 0; changed = true; }
            if (_cfg.QueueBuffer > 500)              { _cfg.QueueBuffer = 500; changed = true; }
            if (_cfg.BroadcastIntervalSeconds < 10f)   { _cfg.BroadcastIntervalSeconds = 10f; changed = true; }
            if (_cfg.PopCommandCooldownSeconds < 1f)   { _cfg.PopCommandCooldownSeconds = 1f; changed = true; }

            if (!IsValidHexColor(_cfg.ColorLabel))
            {
                PrintWarning($"Config ColorLabel '{_cfg.ColorLabel}' is invalid. Resetting to default {DefaultColorLabel}.");
                _cfg.ColorLabel = DefaultColorLabel;
                changed = true;
            }

            if (!IsValidHexColor(_cfg.ColorNumber))
            {
                PrintWarning($"Config ColorNumber '{_cfg.ColorNumber}' is invalid. Resetting to default {DefaultColorNumber}.");
                _cfg.ColorNumber = DefaultColorNumber;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(_cfg.BotName))
            {
                _cfg.BotName = DefaultBotName;
                changed = true;
            }

            if (!IsLikelySteamId64(_cfg.BotSteamId))
            {
                if (_cfg.BotSteamId != 0)
                {
                    PrintWarning($"Config BotSteamId '{_cfg.BotSteamId}' does not look like a SteamID64. " +
                                 $"Resetting to default {DefaultBotSteamId}.");
                    _cfg.BotSteamId = DefaultBotSteamId;
                    changed = true;
                }
            }

            if (changed) SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_cfg, true);

        private bool IsLikelySteamId64(ulong id)
        {
            const ulong MinSteamId64 = 76561197960265728; // universe 1 lower bound
            return id == 0 || id >= MinSteamId64;
        }

        private bool IsValidHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value[0] != '#') return false;
            if (value.Length != 7 && value.Length != 9) return false;

            for (int i = 1; i < value.Length; i++)
            {
                if (!Uri.IsHexDigit(value[i])) return false;
            }

            return true;
        }
        #endregion

        #region Lifecycle
        private void Init()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PopCooldown"] = "!pop is on cooldown ({0:0}s).",
                ["LabelFull"] = "full",
                ["LabelQueued"] = "queued",
                ["LabelJoining"] = "joining",
                ["LabelSleeping"] = "sleeping",
                ["LabelPlayersOnline"] = "players online",
                ["UsageBotId"] = "Usage: ep.botid <steamid64|off>",
                ["UsageIconMode"] = "Usage: ep.iconmode <2|name>",
                ["InsufficientPermission"] = "Insufficient permission.",
                ["ApplyDone"] = "[LWPop] applied.",
                ["BotIdDisabled"] = "[LWPop] BotSteamId disabled (no forced chat icon).",
                ["InvalidSteamId64"] = "[LWPop] Invalid SteamID64. Example: 76561198000000000",
                ["BotIdSet"] = "[LWPop] BotSteamId set to {0}. Try: ep.testmsg",
                ["IconModeSet2"] = "[LWPop] Icon mode set to '2' (chat.add, 2, steamid, message).",
                ["IconModeSetName"] = "[LWPop] Icon mode set to 'name' (chat.add, steamid, botName, message).",
                ["TestMsgPrefix"] = "[Test]",
                ["TestMsgSent"] = "[LWPop] Test message sent to online players."
            }, this);
        }

        private void OnServerInitialized()
        {
            if (!_capturedOriginalMaxPlayers)
            {
                _originalMaxPlayers = ConVar.Server.maxplayers;
                _capturedOriginalMaxPlayers = true;

                if (_cfg.ConsoleMessagesEnabled)
                    Puts($"[LWPop] Captured original maxplayers value: {_originalMaxPlayers}");
            }

            ApplyNow();
            RestartBroadcastTimer();
            _nextPopAllowed = 0; // allow immediate first !pop
        }

        private void Unload()
        {
            _debounceTimer?.Destroy();
            _broadcastTimer?.Destroy();
            _debounceTimer = null;
            _broadcastTimer = null;

            if (_capturedOriginalMaxPlayers)
            {
                ConVar.Server.maxplayers = _originalMaxPlayers;

                if (_cfg.ConsoleMessagesEnabled)
                    Puts($"[LWPop] Restored maxplayers to original value: {_originalMaxPlayers}");
            }
            else if (_cfg.ConsoleMessagesEnabled)
            {
                Puts("[LWPop] Skipped maxplayers restore because original value was not captured.");
            }
        }
        #endregion

        #region Hooks: player count changes
        private void OnPlayerConnected(BasePlayer _) => DebouncedApply();
        private void OnPlayerDisconnected(BasePlayer _, string __) => DebouncedApply();
        #endregion

        #region Elastic core
        private void DebouncedApply()
        {
            _debounceTimer?.Destroy();
            _debounceTimer = timer.Once(_cfg.CooldownPeriodSeconds, () =>
            {
                _debounceTimer = null;
                ApplyNow();
            });
        }

        private void ApplyNow()
        {
            int online = BasePlayer.activePlayerList?.Count ?? 0;
            int joining, queued;
            GetQueueCounts(out joining, out queued);
            int target = ComputeTargetMaxPlayers(online, joining, queued);

            if (ConVar.Server.maxplayers != target)
            {
                ConVar.Server.maxplayers = target;
                if (_cfg.ConsoleMessagesEnabled)
                    Puts($"[LWPop] maxplayers -> {target} (online={online}, joining={joining}, queued={queued}, offset={_cfg.MaxPlayersOffset}, bounds={_cfg.MinimumPopulation}-{_cfg.MaximumPopulation})");
            }
        }

        private int ComputeTargetMaxPlayers(int online, int joining, int queued)
        {
            int scaledQueued = _cfg.IncludeQueuedInScaling
                ? Mathf.RoundToInt(queued * _cfg.QueuedWeight)
                : 0;
            int demand = online;
            if (_cfg.IncludeJoiningInScaling) demand += joining;
            demand += scaledQueued + _cfg.QueueBuffer;

            int target = demand + _cfg.MaxPlayersOffset;
            if (target < _cfg.MinimumPopulation) target = _cfg.MinimumPopulation;
            if (target > _cfg.MaximumPopulation) target = _cfg.MaximumPopulation;
            if (_cfg.NeverBelowOnline && target < online) target = online;
            return target;
        }
        #endregion

        #region Pop stats & rendering
        private struct PopStats
        {
            public int Online;
            public int Max;
            public int Joining;
            public int Queued;
            public int Sleeping;
        }

        private PopStats GetPopStats()
        {
            int online = BasePlayer.activePlayerList?.Count ?? 0;
            int sleeping = BasePlayer.sleepingPlayerList?.Count ?? 0;

            int joining, queued;
            GetQueueCounts(out joining, out queued);

            return new PopStats
            {
                Online   = online,
                Max      = ConVar.Server.maxplayers,
                Joining  = joining,
                Queued   = queued,
                Sleeping = sleeping
            };
        }

        private void GetQueueCounts(out int joining, out int queued)
        {
            joining = 0;
            queued = 0;

            var cq = ServerMgr.Instance?.connectionQueue;
            if (cq == null) return;

            joining = cq.Joining;
            queued = cq.Queued;
        }

        private string N(int v) => v.ToString("N0");

        private string Msg(string key, string playerId = null, params object[] args)
        {
            var value = lang.GetMessage(key, this, playerId);
            if (args == null || args.Length == 0) return value;
            return string.Format(value, args);
        }

        private string BuildMessage(PopStats s, string playerId = null)
        {
            int util = (s.Max <= 0) ? 0 : Mathf.Clamp(Mathf.RoundToInt(100f * (float)s.Online / (float)s.Max), 0, 100);

            // Only show utilization in chat when 90%+
            string utilPart = util >= 90
                ? $" (<color={_cfg.ColorNumber}>{util}% {Msg("LabelFull", playerId)}</color>)"
                : string.Empty;

            string queuedPart = s.Queued >= 1
                ? $" (<color={_cfg.ColorNumber}>{N(s.Queued)}</color> <color={_cfg.ColorLabel}>{Msg("LabelQueued", playerId)}</color>)"
                : string.Empty;

            return
                $"<color={_cfg.ColorNumber}>{N(s.Online)}</color>/<color={_cfg.ColorNumber}>{N(s.Max)}</color> " +
                $"<color={_cfg.ColorLabel}>{Msg("LabelPlayersOnline", playerId)}</color>{utilPart}, " +
                $"<color={_cfg.ColorNumber}>{N(s.Joining)}</color> <color={_cfg.ColorLabel}>{Msg("LabelJoining", playerId)}</color>{queuedPart}, " +
                $"<color={_cfg.ColorNumber}>{N(s.Sleeping)}</color> <color={_cfg.ColorLabel}>{Msg("LabelSleeping", playerId)}</color>";
        }
        #endregion

        #region Chat: /pop (private) and !pop (public with cooldown)
        [Command("pop")] // "/pop" -> PRIVATE reply with bot avatar (if configured)
        private void CmdPop(IPlayer player, string command, string[] args)
        {
            if (!_cfg.EnableChatCommands || player == null) return;

            var s = GetPopStats();
            var msg = BuildMessage(s, player.Id);

            var bp = player.Object as BasePlayer;
            if (bp?.net?.connection != null)
            {
                SendToOneWithIcon(bp, msg);
            }
            else
            {
                player.Reply(msg);
            }
        }

        // "!pop" -> PUBLIC broadcast with global cooldown and bot avatar
        private void OnUserChat(IPlayer player, string message)
        {
            if (!_cfg.EnableChatCommands || player == null || string.IsNullOrWhiteSpace(message)) return;
            if (!message.Trim().Equals("!pop", StringComparison.OrdinalIgnoreCase)) return;

            double now = Time.realtimeSinceStartup;
            if (now < _nextPopAllowed)
            {
                if (_cfg.ShowPopCooldownFeedback)
                {
                    var remaining = Math.Ceiling(_nextPopAllowed - now);
                    player.Reply(Msg("PopCooldown", player.Id, remaining));
                }
                return;
            }
            _nextPopAllowed = now + _cfg.PopCommandCooldownSeconds;

            var s = GetPopStats();
            var msg = BuildMessage(s, player.Id);
            BroadcastWithIcon(msg);
        }
        #endregion

        #region Broadcast helpers
        /// <summary>
        /// Sends a message to one player, forcing the chat icon from BotSteamId.
        /// Mode2 (default):   chat.add, 2, <SteamID64>, <Message>     // uses Steam profile name; matches your working reference
        /// NameOverride mode: chat.add, "<SteamID64>", "<BotName>", <Message>
        /// If BotSteamId == 0, falls back to ChatMessage (no forced icon).
        /// </summary>
        private void SendToOneWithIcon(BasePlayer p, string message)
        {
            try
            {
                if (_cfg.BotSteamId != 0UL)
                {
                    if (_cfg.UseMode2ForIcon)
                    {
                        p.SendConsoleCommand("chat.add", 2, _cfg.BotSteamId, message);
                    }
                    else
                    {
                        p.SendConsoleCommand("chat.add", _cfg.BotSteamId.ToString(), _cfg.BotName, message);
                    }
                }
                else
                {
                    p.ChatMessage(message);
                }
            }
            catch (Exception ex)
            {
                RateLimitedChatError($"SendToOneWithIcon: {p?.UserIDString ?? "unknown"}", ex);
                try
                {
                    p.ChatMessage(message);
                }
                catch (Exception fallbackEx)
                {
                    RateLimitedChatError($"ChatMessageFallback: {p?.UserIDString ?? "unknown"}", fallbackEx);
                }
            }
        }

        private void RateLimitedChatError(string context, Exception ex)
        {
            double now = Time.realtimeSinceStartup;
            if (now < _nextChatSendErrorLog) return;

            _nextChatSendErrorLog = now + 30d;
            PrintWarning($"[LWPop] Chat send failure ({context}): {ex.GetType().Name} - {ex.Message}");
        }

        /// <summary>
        /// Broadcast to all using the chosen chat.add signature so avatar resolves properly.
        /// </summary>
        private void BroadcastWithIcon(string message)
        {
            var list = BasePlayer.activePlayerList;
            if (list == null || list.Count == 0) return;

            foreach (var p in list)
            {
                if (p == null || p.net?.connection == null) continue;
                SendToOneWithIcon(p, message);
            }
        }
        #endregion

        #region Periodic broadcast
        private void RestartBroadcastTimer()
        {
            _broadcastTimer?.Destroy();
            _broadcastTimer = null;

            if (!_cfg.EnablePeriodicBroadcasts) return;

            _broadcastTimer = timer.Every(_cfg.BroadcastIntervalSeconds, () =>
            {
                if ((BasePlayer.activePlayerList?.Count ?? 0) == 0) return;
                var s = GetPopStats();
                BroadcastWithIcon(BuildMessage(s));
            });
        }
        #endregion

        #region Minimal console helpers
        [ConsoleCommand("ep.apply")]
        private void CmdApply(ConsoleSystem.Arg arg)
        {
            if (!IsServerOrOwner(arg)) return;
            ApplyNow();
            arg?.ReplyWith(Msg("ApplyDone", GetArgPlayerId(arg)));
        }

        [ConsoleCommand("ep.status")]
        private void CmdStatus(ConsoleSystem.Arg arg)
        {
            if (!IsServerOrOwner(arg)) return;

            var s = GetPopStats();
            int preview = ComputeTargetMaxPlayers(s.Online, s.Joining, s.Queued);

            int util = (s.Max <= 0) ? 0 : Mathf.Clamp(Mathf.RoundToInt(100f * (float)s.Online / (float)s.Max), 0, 100);

            arg?.ReplyWith(
                $"[LWPop] online={s.Online}, joining={s.Joining}, queued={s.Queued}, sleeping={s.Sleeping}, " +
                $"max={s.Max}, util={util}%, preview={preview}, offset={_cfg.MaxPlayersOffset}, bounds={_cfg.MinimumPopulation}-{_cfg.MaximumPopulation}, " +
                $"includeJoining={_cfg.IncludeJoiningInScaling}, includeQueued={_cfg.IncludeQueuedInScaling}, queuedWeight={_cfg.QueuedWeight:0.##}, queueBuffer={_cfg.QueueBuffer}, " +
                $"cooldown={_cfg.CooldownPeriodSeconds:0.###}s, broadcasts={_cfg.EnablePeriodicBroadcasts} every {_cfg.BroadcastIntervalSeconds:0.#}s, " +
                $"popCooldown={_cfg.PopCommandCooldownSeconds:0.#}s, botName='{_cfg.BotName}', botSteamId={_cfg.BotSteamId}, mode2={_cfg.UseMode2ForIcon}"
            );
        }

        // Set or clear the SteamID64 used for the chat icon at runtime.
        //   ep.botid 7656119xxxxxxxxxx  -> sets and saves
        //   ep.botid off               -> disables forced icon (BotSteamId=0)
        [ConsoleCommand("ep.botid")]
        private void CmdBotId(ConsoleSystem.Arg arg)
        {
            if (!IsServerOrOwner(arg)) return;

            var a = arg?.Args;
            if (a == null || a.Length != 1)
            {
                arg.ReplyWith(Msg("UsageBotId", GetArgPlayerId(arg)));
                return;
            }

            if (a[0].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                _cfg.BotSteamId = 0;
                SaveConfig();
                arg.ReplyWith(Msg("BotIdDisabled", GetArgPlayerId(arg)));
                return;
            }

            if (!ulong.TryParse(a[0], out var newId) || !IsLikelySteamId64(newId))
            {
                arg.ReplyWith(Msg("InvalidSteamId64", GetArgPlayerId(arg)));
                return;
            }

            _cfg.BotSteamId = newId;
            SaveConfig();
            arg.ReplyWith(Msg("BotIdSet", GetArgPlayerId(arg), newId));
        }

        // Toggle the chat.add signature:
        //   ep.iconmode 2     -> use (2, steamid, message)  [default; uses Steam profile name]
        //   ep.iconmode name  -> use (steamid, botName, message)
        [ConsoleCommand("ep.iconmode")]
        private void CmdIconMode(ConsoleSystem.Arg arg)
        {
            if (!IsServerOrOwner(arg)) return;

            var a = arg?.Args;
            if (a == null || a.Length != 1)
            {
                arg.ReplyWith(Msg("UsageIconMode", GetArgPlayerId(arg)));
                return;
            }

            if (a[0].Equals("2", StringComparison.OrdinalIgnoreCase))
            {
                _cfg.UseMode2ForIcon = true;
                SaveConfig();
                arg.ReplyWith(Msg("IconModeSet2", GetArgPlayerId(arg)));
            }
            else if (a[0].Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                _cfg.UseMode2ForIcon = false;
                SaveConfig();
                arg.ReplyWith(Msg("IconModeSetName", GetArgPlayerId(arg)));
            }
            else
            {
                arg.ReplyWith(Msg("UsageIconMode", GetArgPlayerId(arg)));
            }
        }

        // Quick test message with current settings
        [ConsoleCommand("ep.testmsg")]
        private void CmdTestMsg(ConsoleSystem.Arg arg)
        {
            if (!IsServerOrOwner(arg)) return;
            var s = GetPopStats();
            var playerId = GetArgPlayerId(arg);
            BroadcastWithIcon($"{Msg("TestMsgPrefix", playerId)} steamid={_cfg.BotSteamId}, mode2={_cfg.UseMode2ForIcon} | {BuildMessage(s, playerId)}");
            arg.ReplyWith(Msg("TestMsgSent", playerId));
        }
        #endregion

        #region Helpers
        private string GetArgPlayerId(ConsoleSystem.Arg arg) => arg?.Player()?.UserIDString;

        private bool IsServerOrOwner(ConsoleSystem.Arg arg)
        {
            if (arg == null) return false;
            if (arg.Connection == null) return true; // server console/RCON
            if (arg.Connection.authLevel >= 2) return true;
            arg.ReplyWith(Msg("InsufficientPermission", GetArgPlayerId(arg)));
            return false;
        }
        #endregion
    }
}
 