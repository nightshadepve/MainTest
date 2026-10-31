using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeShieldStatus", "Nightshade", "1.0.0")]
    [Description("Read-only Advanced Status bars for NightshadeRaidProtection; no modifications to the raid-protection plugin or its source.")]
    public class NightshadeShieldStatus : RustPlugin
    {
        [PluginReference] private Plugin AdvancedStatus, NightshadeRaidProtection;

        private const string DataPath = "Nightshade/RaidProtection";
        private const string PvPBarId = "NightshadeShieldStatus_PvP";
        private const string TcBarId = "NightshadeShieldStatus_TC";
        private const string IconPvP = "assets/icons/info.png";
        private const string IconTC = "assets/icons/home.png";

        private ConfigData config;
        private TimeZoneInfo eastern;
        private Timer updateTimer;
        private ShieldStore snapshot = new ShieldStore();
        private bool dataWarningPrinted;

        // Fingerprints ensure we only send a new bar on a change of state, TC, or deadline.
        // AdvancedStatus performs the second-by-second countdown by itself.
        private readonly Dictionary<ulong, string> pvpBars = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> tcBars = new Dictionary<ulong, string>();

        private class ConfigData
        {
            [JsonProperty("Enable PvP countdown bar")] public bool ShowPvP = true;
            [JsonProperty("Enable authorized TC shield bar")] public bool ShowTC = true;
            [JsonProperty("Check TC privilege and saved shields every X seconds")] public float RefreshSeconds = 5f;
            [JsonProperty("PvP status bar order")] public int PvPOrder = 20;
            [JsonProperty("TC status bar order")] public int TCOrder = 21;
            [JsonProperty("PvP icon sprite")] public string PvPIcon = IconPvP;
            [JsonProperty("TC icon sprite")] public string TCIcon = IconTC;
        }

        // Exact shape of the protection plugin's data file. Read-only: never write it.
        private class ShieldStore
        {
            public Dictionary<ulong, Shield> Shields = new Dictionary<ulong, Shield>();
        }

        private class Shield
        {
            public ulong CupboardId;
            public uint BuildingId;
            public double RemainingSeconds;
            public long LastAccountedTicks;
            public long ExpiresUtcTicks;
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<ConfigData>() ?? new ConfigData(); }
            catch (Exception ex)
            {
                PrintWarning("Invalid status configuration: " + ex.Message + ". Using defaults.");
                config = new ConfigData();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void Init()
        {
            cmd.AddChatCommand("shieldbar", this, nameof(ShieldBarCommand));
            try { eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch { eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        }

        private void OnServerInitialized()
        {
            if (updateTimer != null) updateTimer.Destroy();
            updateTimer = timer.Every(Mathf.Clamp(config.RefreshSeconds, 2f, 30f), Refresh);
            Refresh();
        }

        private void OnAdvancedStatusLoaded()
        {
            pvpBars.Clear();
            tcBars.Clear();
            NextTick(Refresh);
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null) return;
            if (plugin.Name == "AdvancedStatus")
            {
                AdvancedStatus = plugin;
                pvpBars.Clear();
                tcBars.Clear();
                timer.Once(2f, Refresh);
            }
            else if (plugin.Name == "NightshadeRaidProtection")
            {
                NightshadeRaidProtection = plugin;
                NextTick(Refresh);
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null) return;
            if (plugin.Name == "AdvancedStatus")
            {
                pvpBars.Clear();
                tcBars.Clear();
                AdvancedStatus = null;
            }
            else if (plugin.Name == "NightshadeRaidProtection")
            {
                NightshadeRaidProtection = null;
                foreach (var player in BasePlayer.activePlayerList)
                    if (player != null) DeleteBar(player, TcBarId, tcBars);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null) NextTick(Refresh);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            // The AdvancedStatus plugin handles disconnected clients, but drop our cached state.
            pvpBars.Remove(player.userID);
            tcBars.Remove(player.userID);
        }

        private void OnPlayerGainedBuildingPrivilege(BasePlayer player)
        {
            if (player != null) NextTick(Refresh);
        }

        private void OnPlayerLostBuildingPrivilege(BasePlayer player)
        {
            if (player != null) NextTick(Refresh);
        }

        private void Unload()
        {
            if (updateTimer != null) updateTimer.Destroy();
            // Only bars owned by this companion plugin are removed.
            if (AdvancedStatus != null)
            {
                AdvancedStatus.Call("DeleteBar", PvPBarId, Name);
                AdvancedStatus.Call("DeleteBar", TcBarId, Name);
            }
            pvpBars.Clear();
            tcBars.Clear();
        }

        // Non-destructive diagnostics: /shieldbar debug reports the player's CURRENT TC only.
        private void ShieldBarCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args != null && args.Length > 0 && args[0].Equals("refresh", StringComparison.OrdinalIgnoreCase))
            {
                Refresh();
                player.ChatMessage("[Nightshade] Status bars refreshed.");
                return;
            }
            ReadShieldSnapshot();
            DateTime now = DateTime.UtcNow;
            DateTime start, end;
            bool active;
            GetWeekend(now, out start, out end, out active);
            player.ChatMessage("[Nightshade] AdvancedStatus ready: " + Ready() +
                " | Raid protection loaded: " + (NightshadeRaidProtection != null && NightshadeRaidProtection.IsLoaded) +
                " | PvP active: " + active);
            BuildingPrivlidge tc = player.GetBuildingPrivilege();
            if (tc == null || tc.net == null)
            {
                player.ChatMessage("[Nightshade] TC status: no current building privilege.");
                return;
            }
            bool authorized = tc.authorizedPlayers != null && tc.authorizedPlayers.Contains(player.userID);
            player.ChatMessage("[Nightshade] TC " + tc.net.ID.Value + " | authorized: " + authorized);
            if (!authorized) return;
            Shield shield;
            if (!snapshot.Shields.TryGetValue(tc.net.ID.Value, out shield) || shield == null)
            {
                player.ChatMessage("[Nightshade] No SAVED paid shield for this TC. Check /raidshield.");
                return;
            }
            bool buildingMatches = shield.BuildingId == tc.buildingID;
            bool weekendMatches = shield.ExpiresUtcTicks == end.Ticks;
            double seconds = RemainingPaidSeconds(shield, now, start, end);
            player.ChatMessage("[Nightshade] Shield: " + FormatDuration(seconds) +
                " | building matches: " + buildingMatches +
                " | weekend matches: " + weekendMatches +
                " | bar should show: " + (buildingMatches && weekendMatches && seconds > 0d));
        }

        private bool Ready()
        {
            if (AdvancedStatus == null) return false;
            var ready = AdvancedStatus.Call("IsReady");
            return ready is bool && (bool)ready;
        }

        private void ReadShieldSnapshot()
        {
            try
            {
                ShieldStore loaded = Interface.Oxide.DataFileSystem.ReadObject<ShieldStore>(DataPath);
                snapshot = loaded ?? new ShieldStore();
                if (snapshot.Shields == null) snapshot.Shields = new Dictionary<ulong, Shield>();
                dataWarningPrinted = false;
            }
            catch (Exception ex)
            {
                // Keep last good snapshot; do not overwrite protection data on a transient read failure.
                if (!dataWarningPrinted)
                {
                    PrintWarning("Unable to read protection data for status bars: " + ex.Message);
                    dataWarningPrinted = true;
                }
            }
        }

        private void Refresh()
        {
            if (!Ready()) return;
            ReadShieldSnapshot(); // Once per refresh, not once per player.

            DateTime now = DateTime.UtcNow;
            DateTime start, end;
            bool active;
            GetWeekend(now, out start, out end, out active);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;

                if (config.ShowPvP)
                    ShowCountdown(player, PvPBarId, active ? "PVP ENDS IN" : "PVP STARTS IN",
                        active ? end : start, config.PvPOrder, "#B084FF", config.PvPIcon, pvpBars);
                else
                    DeleteBar(player, PvPBarId, pvpBars);

                if (!config.ShowTC || NightshadeRaidProtection == null || !NightshadeRaidProtection.IsLoaded)
                {
                    DeleteBar(player, TcBarId, tcBars);
                    continue;
                }

                // Same building-privilege and TC-authorization checks used by the raid plugin.
                BuildingPrivlidge tc = player.GetBuildingPrivilege();
                if (tc == null || tc.IsDestroyed || tc.net == null || tc.authorizedPlayers == null ||
                    !tc.authorizedPlayers.Contains(player.userID))
                {
                    DeleteBar(player, TcBarId, tcBars);
                    continue;
                }

                Shield shield;
                if (!snapshot.Shields.TryGetValue(tc.net.ID.Value, out shield) || shield == null ||
                    shield.BuildingId != tc.buildingID || shield.ExpiresUtcTicks != end.Ticks)
                {
                    DeleteBar(player, TcBarId, tcBars);
                    continue;
                }

                double secondsLeft = RemainingPaidSeconds(shield, now, start, end);
                if (secondsLeft <= 0d)
                {
                    DeleteBar(player, TcBarId, tcBars);
                    continue;
                }

                if (active)
                {
                    // The paid shield ends at the earlier of exhausted time and Monday 3 AM ET.
                    DateTime effectiveEnd = now.AddSeconds(Math.Min(secondsLeft, (end - now).TotalSeconds));
                    ShowCountdown(player, TcBarId, "TC RAID SHIELD", effectiveEnd,
                        config.TCOrder, "#69D8A0", config.TCIcon, tcBars);
                }
                else
                {
                    string label = FormatDuration(secondsLeft);
                    ShowStatic(player, TcBarId, "TC SHIELD QUEUED", label,
                        config.TCOrder, "#E2B968", config.TCIcon, tcBars);
                }
            }
        }

        private static double RemainingPaidSeconds(Shield shield, DateTime now, DateTime start, DateTime end)
        {
            if (shield == null || shield.ExpiresUtcTicks != end.Ticks || now >= end) return 0d;
            if (shield.RemainingSeconds <= 0d) return 0d;
            if (now <= start) return shield.RemainingSeconds;

            // Reconstruct the core plugin's in-memory accounting from its persisted snapshot.
            // The protection plugin persists every purchase; later accounting may be held in RAM.
            long ticks = Math.Max(shield.LastAccountedTicks, start.Ticks);
            DateTime chargeFrom = new DateTime(Math.Min(ticks, now.Ticks), DateTimeKind.Utc);
            DateTime chargeUntil = now < end ? now : end;
            double used = Math.Max(0d, (chargeUntil - chargeFrom).TotalSeconds);
            return Math.Max(0d, shield.RemainingSeconds - used);
        }

        // Same Eastern-time weekend boundaries as the unmodified protection plugin.
        private void GetWeekend(DateTime utc, out DateTime startUtc, out DateTime endUtc, out bool active)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, eastern);
            int back = ((int)local.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            DateTime friday = local.Date.AddDays(-back);
            DateTime localStart = friday.AddHours(20);
            DateTime localEnd = friday.AddDays(3).AddHours(3);
            active = local >= localStart && local < localEnd;
            if (!active && local >= localEnd) friday = friday.AddDays(7);

            startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(friday.AddHours(20), DateTimeKind.Unspecified), eastern);
            endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(friday.AddDays(3).AddHours(3), DateTimeKind.Unspecified), eastern);
        }

        private void ShowCountdown(BasePlayer player, string id, string text, DateTime endUtc,
            int order, string tint, string icon, Dictionary<ulong, string> cache)
        {
            double seconds = (endUtc - DateTime.UtcNow).TotalSeconds;
            if (seconds <= 0d)
            {
                DeleteBar(player, id, cache);
                return;
            }
            // Round the deadline to seconds so the fingerprint is stable on periodic refreshes.
            long endSecond = (long)Math.Floor((endUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            string fingerprint = "COUNT|" + text + "|" + endSecond;
            string old;
            if (cache.TryGetValue(player.userID, out old) && old == fingerprint) return;

            if (old != null) AdvancedStatus.Call("DeleteBar", player.userID, id, Name);

            var parameters = BaseParameters(id, "TimeCounter", text, order, tint, icon);
            parameters[29] = Network.TimeEx.currentTimestamp + seconds;
            AdvancedStatus.Call("CreateBar", player.userID, parameters);
            cache[player.userID] = fingerprint;
        }

        private void ShowStatic(BasePlayer player, string id, string text, string subtext,
            int order, string tint, string icon, Dictionary<ulong, string> cache)
        {
            string fingerprint = "QUEUED|" + text + "|" + subtext;
            string old;
            if (cache.TryGetValue(player.userID, out old) && old == fingerprint) return;
            if (old != null) AdvancedStatus.Call("DeleteBar", player.userID, id, Name);

            var parameters = BaseParameters(id, "Default", text, order, tint, icon);
            parameters[22] = subtext;
            AdvancedStatus.Call("CreateBar", player.userID, parameters);
            cache[player.userID] = fingerprint;
        }

        // Integer keys, player.userID (ulong), and TimeCounter follow the user's RestartStatus.cs.
        private Dictionary<int, object> BaseParameters(string id, string type, string text,
            int order, string tint, string icon)
        {
            return new Dictionary<int, object>
            {
                { 0, id },                  // Id
                { 1, Name },                // Plugin
                { 2, type },                // BarType
                { 4, order },               // Order
                { 5, 26 },                  // Height
                { 6, "#241634" },           // Main_Color
                { 10, string.IsNullOrEmpty(icon) ? IconPvP : icon }, // Image_Sprite
                { 11, false },              // Is_RawImage
                { 12, tint },               // Image_Color
                { 15, text },               // Text
                { 16, 12 },                 // Text_Size
                { 17, "#F2E6FF" },         // Text_Color
                { 23, 12 },                 // SubText_Size
                { 24, tint }                // SubText_Color
            };
        }

        private void DeleteBar(BasePlayer player, string id, Dictionary<ulong, string> cache)
        {
            if (player == null) return;
            if (!cache.Remove(player.userID)) return;
            if (AdvancedStatus != null) AdvancedStatus.Call("DeleteBar", player.userID, id, Name);
        }

        private static string FormatDuration(double seconds)
        {
            int totalMinutes = (int)Math.Ceiling(Math.Max(0d, seconds) / 60d);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return hours + "h " + minutes.ToString("00") + "m";
        }
    }
}
