using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Oxide.Plugins
{
    [Info("Nightshade Purge Status", "Nightshade", "1.1.1")]
    [Description("Manual purge period toggle: join notification, AdvancedStatus countdown bar, and a small API.")]
    public class NightshadePurgeStatus : RustPlugin
    {
        [PluginReference] private Plugin AdvancedStatus;
        [PluginReference] private Plugin TruePVE;

        private const string PermAdmin = "nightshadepurgestatus.admin";
        private const string BarId = "NightshadePurge";

        private PurgeData _data;
        private Timer _endTimer;

        #region Data

        private class PurgeData
        {
            public bool Active;
            public double StartTime;
            public double EndTime;
            public string PreviousTruePveRuleset;
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private void LoadData()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<PurgeData>(Name) ?? new PurgeData();
        }

        #endregion

        #region Config

        private PluginConfig _config;

        private class PluginConfig
        {
            [JsonProperty("Chat/console command")]
            public string Command = "purge";

            [JsonProperty("Join message (use {time} for remaining time)")]
            public string JoinMessage = "<color=#FF2D75>[Purge]</color> The purge is <color=#FF3B3B>ACTIVE</color> until end of wipe! <color=#FFAA00>{time}</color> remaining — no mercy.";

            [JsonProperty("Broadcast message on start (use {time} for remaining time)")]
            public string StartBroadcast = "<color=#FF2D75>[Purge]</color> The purge has begun! Chaos reigns for the next <color=#FFAA00>{time}</color>.";

            [JsonProperty("Broadcast message on end")]
            public string StopBroadcast = "<color=#FF2D75>[Purge]</color> The purge has ended.";

            [JsonProperty("Bar text")]
            public string BarText = "PURGE";

            [JsonProperty("Bar order (position among other AdvancedStatus bars)")]
            public int BarOrder = 50;

            [JsonProperty("Bar background color")]
            public string BarMainColor = "#000000";

            [JsonProperty("Bar progress fill color")]
            public string BarProgressColor = "#8B0000";

            [JsonProperty("Bar text color")]
            public string BarTextColor = "#FF0000";

            [JsonProperty("Bar subtext (countdown) color")]
            public string BarSubTextColor = "#FF0000";

            [JsonProperty("Bar local image name (blank = use AdvancedStatus default)")]
            public string BarImage = "";

            [JsonProperty("Use filling progress bar (false = plain countdown text)")]
            public bool UseProgress = false;

            [JsonProperty("Sync TruePVE default ruleset with purge state")]
            public bool SyncTruePve = true;

            [JsonProperty("TruePVE ruleset name to switch to when purge starts")]
            public string TruePvePurgeRuleset = "purge";

            [JsonProperty("TruePVE config file path (blank = auto-detect oxide/config/TruePVE.json)")]
            public string TruePveConfigPath = "";
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintWarning("Config file is corrupt, creating a new one.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
            cmd.AddChatCommand(_config.Command, this, nameof(CmdPurgeChat));
            cmd.AddConsoleCommand(_config.Command, this, nameof(CmdPurgeConsole));
        }

        private void OnServerInitialized()
        {
            if (_data.Active)
            {
                double remaining = _data.EndTime - Network.TimeEx.currentTimestamp;
                if (remaining <= 0)
                    EndPurge(false);
                else
                {
                    ScheduleEndTimer(remaining);
                    ApplyBarToAll();
                    // The weekend scheduler may already have replaced the ruleset during boot.
                    if (_config.SyncTruePve)
                        timer.Once(1f, ReassertPurgeRuleset);
                }
            }
        }

        private void Unload()
        {
            _endTimer?.Destroy();
            RemoveBar();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!_data.Active) return;
            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected) return;
                SendPurgeJoinMessage(player);
                ApplyBar(player);
            });
        }

        // Reapply bars if AdvancedStatus reloads/loads after we're already running
        private void OnAdvancedStatusLoaded()
        {
            if (_data.Active) ApplyBarToAll();
        }

        // CUI can get wiped on respawn - make sure the bar comes back so it's always visible
        private void OnPlayerRespawned(BasePlayer player)
        {
            if (!_data.Active || player == null) return;
            timer.Once(1f, () =>
            {
                if (player == null || !player.IsConnected) return;
                ApplyBar(player);
            });
        }

        #endregion

        #region Commands

        private void CmdPurgeChat(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
            {
                SendReply(player, "You don't have permission to use this command.");
                return;
            }
            HandleCommand(args, msg => SendReply(player, msg));
        }

        private void CmdPurgeConsole(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Connection != null ? arg.Connection.player as BasePlayer : null;
            if (caller != null && !IsAdmin(caller))
            {
                arg.ReplyWith("No permission.");
                return;
            }
            string[] args = arg.Args != null ? arg.Args.Select(a => a.ToString()).ToArray() : Array.Empty<string>();
            HandleCommand(args, arg.ReplyWith);
        }

        private bool IsAdmin(BasePlayer player)
        {
            if (player == null) return true; // server console
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        private void HandleCommand(string[] args, Action<string> reply)
        {
            if (args == null || args.Length == 0)
            {
                reply(UsageText());
                return;
            }

            switch (args[0].ToLower())
            {
                case "start":
                {
                    double endTimestamp;
                    bool hasManualArg = args.Length >= 2
                        && !string.Equals(args[1], "auto", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(args[1], "thursday", StringComparison.OrdinalIgnoreCase);

                    if (hasManualArg)
                    {
                        double manualSeconds = ParseDuration(args[1]);
                        if (manualSeconds <= 0) { reply("Couldn't parse that duration. Try 5d, 12h, 2d6h, 30m, or omit for auto (next Thursday 2pm ET)."); return; }
                        endTimestamp = Network.TimeEx.currentTimestamp + manualSeconds;
                    }
                    else
                    {
                        endTimestamp = GetNextThursday2pmEasternUnix(DateTime.UtcNow);
                    }

                    StartPurge(endTimestamp);
                    double remaining = endTimestamp - Network.TimeEx.currentTimestamp;
                    reply($"Purge started. Ends {FormatAbsoluteEastern(endTimestamp)} ({FormatTimeSpan(remaining)} remaining).");
                    break;
                }

                case "extend":
                    if (!_data.Active) { reply("Purge isn't active."); return; }
                    if (args.Length < 2) { reply($"Usage: /{_config.Command} extend <duration>"); return; }
                    double extra = ParseDuration(args[1]);
                    if (extra <= 0) { reply("Couldn't parse that duration."); return; }
                    ExtendPurge(extra);
                    reply($"Purge extended by {FormatTimeSpan(extra)}. New remaining time: {FormatTimeSpan(_data.EndTime - Network.TimeEx.currentTimestamp)}.");
                    break;

                case "stop":
                    if (!_data.Active) { reply("Purge isn't active."); return; }
                    EndPurge(true);
                    reply("Purge ended.");
                    break;

                case "status":
                    reply(_data.Active
                        ? $"Purge is active. Time remaining: {FormatTimeSpan(_data.EndTime - Network.TimeEx.currentTimestamp)}."
                        : "Purge is not active.");
                    break;

                default:
                    reply(UsageText());
                    break;
            }
        }

        private string UsageText() => $"Usage: /{_config.Command} start [duration] | extend <duration> | stop | status  (start with no duration = auto, ends next Thursday 2pm ET)";

        #endregion

        #region Purge Logic

        private void StartPurge(double endTimestamp)
        {
            _endTimer?.Destroy();
            double now = Network.TimeEx.currentTimestamp;
            _data.Active = true;
            _data.StartTime = now;
            _data.EndTime = endTimestamp;

            if (_config.SyncTruePve)
            {
                if (TrySetTruePveDefaultRuleset(_config.TruePvePurgeRuleset, out string previous))
                    _data.PreviousTruePveRuleset = previous;
                else
                    PrintWarning("Could not sync TruePVE ruleset on purge start - continuing without it.");
            }

            SaveData();

            double remaining = endTimestamp - now;
            ScheduleEndTimer(remaining);
            ApplyBarToAll();
            Broadcast(_config.StartBroadcast.Replace("{time}", FormatTimeSpan(remaining)));
            Interface.CallHook("OnPurgeStarted", endTimestamp);
        }

        private void ExtendPurge(double extraSeconds)
        {
            _data.EndTime += extraSeconds;
            SaveData();
            double remaining = _data.EndTime - Network.TimeEx.currentTimestamp;
            ScheduleEndTimer(remaining);
            ApplyBarToAll();
        }

        private void EndPurge(bool announce)
        {
            _endTimer?.Destroy();
            _data.Active = false;

            if (_config.SyncTruePve && !string.IsNullOrEmpty(_data.PreviousTruePveRuleset))
            {
                if (!TrySetTruePveDefaultRuleset(_data.PreviousTruePveRuleset, out _))
                    PrintWarning("Could not restore TruePVE's previous ruleset - check TruePVE.json manually.");
                _data.PreviousTruePveRuleset = null;
            }

            SaveData();

            RemoveBar();

            if (announce)
                Broadcast(_config.StopBroadcast);
            Interface.CallHook("OnPurgeEnded");
        }

        // In-memory only. StartPurge already wrote the purge ruleset into TruePVE's config.
        private void ReassertPurgeRuleset()
        {
            if (!_data.Active || !_config.SyncTruePve || string.IsNullOrEmpty(_config.TruePvePurgeRuleset)) return;
            Server.Command("tpve.ruleset", _config.TruePvePurgeRuleset);
        }

        #region API
        // API_IsPurgeActive() -> bool
        // API_GetPurgeEndTimestamp() -> double unix seconds, or 0 when inactive
        // API_GetActiveRulesetOverride() -> string TruePVE ruleset while a purge is active, otherwise null
        // Hooks: OnPurgeStarted(double endUnixTimestamp), OnPurgeEnded()
        // Hooks fire when a purge starts or ends, not when this plugin reloads onto an active purge.

        public bool API_IsPurgeActive() => _data != null && _data.Active;

        public double API_GetPurgeEndTimestamp() => _data != null && _data.Active ? _data.EndTime : 0d;

        public string API_GetActiveRulesetOverride()
        {
            if (_data == null || !_data.Active || _config == null || !_config.SyncTruePve) return null;
            return _config.TruePvePurgeRuleset;
        }

        #endregion

        private void ScheduleEndTimer(double seconds)
        {
            _endTimer?.Destroy();
            _endTimer = timer.Once((float)Math.Max(1, seconds), () => EndPurge(true));
        }

        private void Broadcast(string message)
        {
            foreach (var player in BasePlayer.activePlayerList)
                SendReply(player, message);
        }

        private void SendPurgeJoinMessage(BasePlayer player)
        {
            double remaining = _data.EndTime - Network.TimeEx.currentTimestamp;
            SendReply(player, _config.JoinMessage.Replace("{time}", FormatTimeSpan(remaining)));
        }

        #endregion

        #region AdvancedStatus Bar

        private void ApplyBarToAll()
        {
            if (AdvancedStatus == null) return;
            foreach (var player in BasePlayer.activePlayerList)
                ApplyBar(player);
        }

        private void RemoveBar()
        {
            if (AdvancedStatus == null)
            {
                PrintWarning("AdvancedStatus isn't loaded - can't remove the purge bar.");
                return;
            }

            foreach (var player in BasePlayer.activePlayerList)
                AdvancedStatus.Call("DeleteBar", player.userID.Get(), BarId, Name);

            // Safety net in case a bar exists for someone not currently in activePlayerList
            AdvancedStatus.Call("DeleteAllBars", Name);
        }

        private void ApplyBar(BasePlayer player)
        {
            if (AdvancedStatus == null || player == null || !player.IsConnected) return;
            if ((bool?)AdvancedStatus.Call("IsReady") != true) return;

            var parameters = new Dictionary<string, object>
            {
                { "Id", BarId },
                { "BarType", _config.UseProgress ? "TimeProgressCounter" : "TimeCounter" },
                { "Plugin", Name },
                { "Category", "Purge" },
                { "Order", _config.BarOrder },
                { "Text", _config.BarText },
                { "Text_Color", _config.BarTextColor },
                { "SubText_Color", _config.BarSubTextColor },
                { "Main_Color", _config.BarMainColor },
                { "Progress_Color", _config.BarProgressColor },
                { "TimeStampStart", _data.StartTime },
                { "TimeStamp", _data.EndTime },
            };

            if (!string.IsNullOrEmpty(_config.BarImage))
                parameters["Image_Local"] = _config.BarImage;

            AdvancedStatus.Call("CreateBar", player.userID.Get(), parameters);
        }

        #endregion

        #region TruePVE Sync

        private string GetTruePveConfigPath()
        {
            if (!string.IsNullOrEmpty(_config.TruePveConfigPath))
                return _config.TruePveConfigPath;

            return Path.Combine(Interface.Oxide.ConfigDirectory, "TruePVE.json");
        }

        // Edits the "Default RuleSet" field directly in TruePVE's config file and asks
        // TruePVE to reload. TruePVE's plugin API only exposes per-zone mapping
        // add/remove hooks (AddOrUpdateMapping/RemoveMapping) - there's no documented
        // API to change the global default ruleset at runtime, so this goes straight
        // to the config file instead.
        private bool TrySetTruePveDefaultRuleset(string newRuleset, out string previousRuleset)
        {
            previousRuleset = null;
            string path = GetTruePveConfigPath();

            if (TruePVE == null)
                PrintWarning("TruePVE doesn't appear to be loaded - updating its config anyway, but the reload may not take effect.");

            try
            {
                if (!File.Exists(path))
                {
                    PrintWarning($"TruePVE config not found at {path} - skipping ruleset sync. Set 'TruePVE config file path' in config if it lives somewhere else.");
                    return false;
                }

                var json = JObject.Parse(File.ReadAllText(path));
                var token = json["Default RuleSet"];
                if (token == null)
                {
                    PrintWarning("Couldn't find 'Default RuleSet' in TruePVE's config - skipping ruleset sync.");
                    return false;
                }

                previousRuleset = token.Value<string>();

                if (previousRuleset == newRuleset)
                    return true; // already set, nothing to do

                json["Default RuleSet"] = newRuleset;
                File.WriteAllText(path, json.ToString(Formatting.Indented));

                ReloadTruePve();
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to update TruePVE config: {ex.Message}");
                return false;
            }
        }

        private void ReloadTruePve()
        {
            Server.Command("c.reload TruePVE");
        }

        #endregion

        #region Helpers

        // Parses combined duration strings like "5d", "12h", "2d6h", "90m"
        private static double ParseDuration(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            input = input.Trim().ToLowerInvariant();
            double total = 0;
            var sb = new StringBuilder();

            foreach (char c in input)
            {
                if (char.IsDigit(c) || c == '.')
                {
                    sb.Append(c);
                    continue;
                }

                if (sb.Length == 0) continue;
                double.TryParse(sb.ToString(), out double value);
                sb.Clear();

                switch (c)
                {
                    case 'd': total += value * 86400; break;
                    case 'h': total += value * 3600; break;
                    case 'm': total += value * 60; break;
                    case 's': total += value; break;
                }
            }

            return total;
        }

        private static string FormatTimeSpan(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            var parts = new List<string>();
            if (ts.Days > 0) parts.Add($"{ts.Days}d");
            if (ts.Hours > 0) parts.Add($"{ts.Hours}h");
            if (ts.Days == 0 && ts.Minutes > 0) parts.Add($"{ts.Minutes}m");
            if (parts.Count == 0) parts.Add($"{ts.Seconds}s");
            return string.Join(" ", parts);
        }

        // Tries the IANA id first (Linux/most Rust hosting), falls back to the Windows id.
        private static TimeZoneInfo GetEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { return TimeZoneInfo.Utc; }
            }
        }

        // Returns the unix timestamp for 2:00 PM Eastern on the Thursday of the current
        // Eastern-time week. If that moment has already passed (e.g. starting the purge
        // on a Thursday after 2pm), it rolls forward to the following Thursday.
        private static double GetNextThursday2pmEasternUnix(DateTime utcNow)
        {
            var est = GetEasternTimeZone();
            DateTime nowEastern = TimeZoneInfo.ConvertTimeFromUtc(utcNow, est);

            int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)nowEastern.DayOfWeek + 7) % 7;
            DateTime targetWall = nowEastern.Date.AddDays(daysUntilThursday).AddHours(14);

            if (daysUntilThursday == 0 && nowEastern >= targetWall)
                targetWall = targetWall.AddDays(7);

            DateTime targetUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(targetWall, DateTimeKind.Unspecified), est);
            return new DateTimeOffset(targetUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        private static string FormatAbsoluteEastern(double unixTimestamp)
        {
            var est = GetEasternTimeZone();
            DateTime utc = DateTimeOffset.FromUnixTimeSeconds((long)unixTimestamp).UtcDateTime;
            DateTime eastern = TimeZoneInfo.ConvertTimeFromUtc(utc, est);
            return eastern.ToString("ddd MMM d, h:mm tt") + " ET";
        }

        #endregion
    }
}