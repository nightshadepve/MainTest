using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeRaidProtection", "Nightshade", "1.1.3")]
    [Description("PvP-window Tool Cupboard raid shields, offline protection, and the Eastern-time weekend window that drives TruePVE.")]
    public class NightshadeRaidProtection : RustPlugin
    {
        private const string PermAdmin = "nightshaderaidprotection.admin";
        private const string DataPath = "Nightshade/RaidProtection";
        // These IDs identify the three dedicated custom-icon voucher skins.
        // The shop delivers PAPER items using these IDs. No purchase-registration plugin is needed.
        private const string VoucherShortname = "paper";
        private static readonly Dictionary<string, ulong> VoucherSkins = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            {"1h", 3810298504UL},
            {"24h", 3810324315UL},
            {"weekend", 3810324938UL}
        };

        [PluginReference] private Plugin TruePVE;
        [PluginReference("Nightshade Purge Status")] private Plugin PurgeStatus;

        private Settings settings;
        private Store store;
        private TimeZoneInfo eastern;
        private bool usedZoneFallback;
        private DateTime? fakeUtc;
        private bool windowReady;
        private bool windowActive;
        private bool suppressHooks;
        private Timer boundaryTimer;
        private Timer rulesTimer;
        private Timer shieldTimer;
        private readonly Dictionary<ulong, string> published = new Dictionary<ulong, string>();

        private class Settings
        {
            [JsonProperty("Enable paid TC raid shields")] public bool PaidShields = true;
            [JsonProperty("Enable automatic offline raid protection during PvP")] public bool OfflineProtection = true;
            [JsonProperty("Minutes everyone authorized on TC must be offline before auto protection")] public int OfflineGraceMinutes = 30;
            [JsonProperty("Offline protection damage reduction percent (100 = fully block)")] public int OfflineReductionPercent = 100;
            [JsonProperty("Prevent teammates authorized on TC from damaging their own shielded building")] public bool ProtectFromAuthorizedPlayers = false;
            [JsonProperty("Show blocked-hit message to attacker (rate limited)")] public bool NotifyAttacker = true;
            [JsonProperty("Maximum total paid shield time on one TC in hours")] public int MaximumStackedHours = 72;
            [JsonProperty("Drive TruePVE from the Eastern weekend window")] public bool DriveTruePve = true;
            [JsonProperty("TruePVE ruleset during the weekend window")] public string PvpRuleset = "nightshade_pvp_weekend";
            [JsonProperty("TruePVE ruleset outside the weekend window")] public string PveRuleset = "default";
            [JsonProperty("Yield TruePVE changes while a purge is active")] public bool YieldToPurge = true;
        }

        private class Shield
        {
            public ulong CupboardId;
            public uint BuildingId;
            public double RemainingSeconds;
            public long LastAccountedTicks;
            public long ExpiresUtcTicks;
        }

        private class Store
        {
            public Dictionary<ulong, Shield> Shields = new Dictionary<ulong, Shield>();
            // Legacy UID records retained solely for compatibility with previously saved data.
            public HashSet<ulong> LegitimateVoucherUids = new HashSet<ulong>();
            public Dictionary<ulong, long> LastSeenUtcTicks = new Dictionary<ulong, long>();
        }

        private readonly Dictionary<ulong, float> notifyThrottle = new Dictionary<ulong, float>();

        protected override void LoadDefaultConfig() { settings = new Settings(); }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { settings = Config.ReadObject<Settings>() ?? new Settings(); }
            catch (Exception ex) { PrintError("Invalid config; inspect and repair it: " + ex.Message); settings = new Settings(); }
            if (string.IsNullOrEmpty(settings.PvpRuleset)) settings.PvpRuleset = "nightshade_pvp_weekend";
            if (string.IsNullOrEmpty(settings.PveRuleset)) settings.PveRuleset = "default";
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(settings, true);

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            cmd.AddChatCommand("raidvoucher", this, nameof(RedeemCommand));
            cmd.AddChatCommand("raidshield", this, nameof(StatusCommand));
            cmd.AddConsoleCommand("nrp.faketime", this, nameof(CmdFakeTime));
            cmd.AddConsoleCommand("nrp.window", this, nameof(CmdWindow));
            try { store = Interface.Oxide.DataFileSystem.ReadObject<Store>(DataPath) ?? new Store(); }
            catch (Exception ex) { throw new Exception("Raid shield data could not be loaded: purchases disabled to prevent lost vouchers. " + ex.Message); }
            if (store.Shields == null) store.Shields = new Dictionary<ulong, Shield>();
            if (store.LegitimateVoucherUids == null) store.LegitimateVoucherUids = new HashSet<ulong>();
            if (store.LastSeenUtcTicks == null) store.LastSeenUtcTicks = new Dictionary<ulong, long>();
            eastern = ResolveEastern(out usedZoneFallback);
        }

        private void Save() => Interface.Oxide.DataFileSystem.WriteObject(DataPath, store);
        private void OnServerSave() => Save();
        private void Unload()
        {
            boundaryTimer?.Destroy();
            rulesTimer?.Destroy();
            shieldTimer?.Destroy();
            fakeUtc = null;
            Save();
        }
        private void OnNewSave(string filename)
        {
            store.Shields.Clear();
            store.LegitimateVoucherUids.Clear();
            store.LastSeenUtcTicks.Clear();
            published.Clear();
            Save();
        }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            store.LastSeenUtcTicks[player.userID] = DateTime.UtcNow.Ticks;
        }
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null) store.LastSeenUtcTicks[player.userID] = DateTime.UtcNow.Ticks;
        }
        private void OnServerInitialized()
        {
            foreach (var player in BasePlayer.activePlayerList)
                if (player != null) store.LastSeenUtcTicks[player.userID] = DateTime.UtcNow.Ticks;
            if (usedZoneFallback)
                PrintWarning("America/New_York and Eastern Standard Time were unavailable. Using a built-in US Eastern zone (EST/EDT).");
            SeedShieldStates();
            CheckWindow();
            WarnIfTruePveScheduleEnabled();
            EnsureRuleset();
            rulesTimer = timer.Every(60f, EnsureRuleset);
            Puts("Weekend window is America/New_York Friday 8:00 PM through Monday 3:00 AM.");
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "TruePVE")
                timer.Once(1f, EnsureRuleset);
        }

        private void OnPurgeStarted(double endTimestamp) => timer.Once(1f, EnsureRuleset);
        private void OnPurgeEnded() => timer.Once(1f, EnsureRuleset);

        private void OnEntityKill(BuildingPrivlidge tc)
        {
            if (tc == null || tc.net == null) return;
            ulong id = tc.net.ID.Value;
            Shield shield;
            if (!store.Shields.TryGetValue(id, out shield)) return;
            store.Shields.Remove(id);
            PublishState(id, shield != null ? shield.BuildingId : tc.buildingID, "none", 0d);
            Save();
        }

        #region Public API
        // Other plugins call these with Plugin.Call. Query them on load; the hooks below
        // fire only when the state changes while this plugin is already running.
        //
        // API_IsPvPActive() -> bool
        //     True during Friday 8:00 PM through Monday 3:00 AM America/New_York.
        //     A manual /purge is not included; ask Nightshade Purge Status API_IsPurgeActive.
        // API_SecondsUntilPvPChange() -> double
        //     Seconds until the window opens, or until it closes when it is already open.
        // API_GetPvPStartUtcTicks() / API_GetPvPEndUtcTicks() -> long (DateTime UTC ticks)
        // API_GetWeekend() -> Dictionary
        //     active (bool), startUtcTicks (long), endUtcTicks (long), secondsUntilChange (double), ruleset (string)
        // API_GetPlayerShield(BasePlayer) -> Dictionary
        // API_GetCupboardShield(ulong cupboardNetId) -> Dictionary
        // API_GetBuildingShield(uint buildingId) -> Dictionary
        //     state "active" | "queued" | "none", seconds (double), tcId (ulong), buildingId (uint)
        //
        // Hooks (not fired for the state already in effect at plugin load):
        // OnNightshadePvPStarted(long startUtcTicks, long endUtcTicks)
        // OnNightshadePvPEnded(long startUtcTicks, long endUtcTicks)
        // OnNightshadeShieldStarted(ulong cupboardNetId, uint buildingId, double remainingSeconds)
        // OnNightshadeShieldQueued(ulong cupboardNetId, uint buildingId, double remainingSeconds)
        // OnNightshadeShieldEnded(ulong cupboardNetId, uint buildingId)

        [HookMethod("API_IsPvPActive")]
        public bool API_IsPvPActive()
        {
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            return active;
        }

        [HookMethod("API_SecondsUntilPvPChange")]
        public double API_SecondsUntilPvPChange()
        {
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            return SecondsUntilNow(start, end, active);
        }

        [HookMethod("API_GetPvPStartUtcTicks")]
        public long API_GetPvPStartUtcTicks()
        {
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            return start.Ticks;
        }

        [HookMethod("API_GetPvPEndUtcTicks")]
        public long API_GetPvPEndUtcTicks()
        {
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            return end.Ticks;
        }

        [HookMethod("API_GetWeekend")]
        public Dictionary<string, object> API_GetWeekend()
        {
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            return new Dictionary<string, object>
            {
                ["active"] = active,
                ["startUtcTicks"] = start.Ticks,
                ["endUtcTicks"] = end.Ticks,
                ["secondsUntilChange"] = SecondsUntilNow(start, end, active),
                ["ruleset"] = active ? settings.PvpRuleset : settings.PveRuleset
            };
        }

        // The cupboard the player is standing in, whether or not they are authorized on it.
        // Authorization still gates damage blocking; this is only the status lookup.
        [HookMethod("API_GetPlayerShield")]
        public Dictionary<string, object> API_GetPlayerShield(BasePlayer player)
        {
            if (player == null) return NoneShield();
            BuildingPrivlidge tc = player.GetBuildingPrivilege();
            if (tc == null) return NoneShield();
            return API_GetCupboardShield(CupboardId(tc));
        }

        [HookMethod("API_GetCupboardShield")]
        public Dictionary<string, object> API_GetCupboardShield(ulong cupboardNetId)
        {
            Shield shield;
            if (cupboardNetId == 0UL || !store.Shields.TryGetValue(cupboardNetId, out shield) || shield == null)
                return NoneShield();
            return Describe(shield);
        }

        [HookMethod("API_GetBuildingShield")]
        public Dictionary<string, object> API_GetBuildingShield(uint buildingId)
        {
            if (buildingId == 0u) return NoneShield();
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            Shield best = null;
            double bestSeconds = 0d;
            foreach (var shield in store.Shields.Values)
            {
                if (shield == null || shield.BuildingId != buildingId || shield.ExpiresUtcTicks != end.Ticks) continue;
                Account(shield, DateTime.UtcNow);
                if (shield.RemainingSeconds <= bestSeconds) continue;
                best = shield;
                bestSeconds = shield.RemainingSeconds;
            }
            return best == null ? NoneShield() : Describe(best);
        }

        #endregion

        // Returns Friday 8 PM through Monday 3 AM Eastern, DST-safe. Outside that window, returns NEXT weekend.
        private void Weekend(DateTime utc, out DateTime startUtc, out DateTime endUtc, out bool active)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), eastern);
            int back = ((int)local.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            DateTime friday = local.Date.AddDays(-back);
            DateTime candidateStart = friday.AddHours(20);
            DateTime candidateEnd = friday.AddDays(3).AddHours(3);
            active = local >= candidateStart && local < candidateEnd;
            if (!active && local >= candidateEnd) friday = friday.AddDays(7);
            startUtc = ToUtc(friday.AddHours(20));
            endUtc = ToUtc(friday.AddDays(3).AddHours(3));
        }

        private DateTime ToUtc(DateTime easternWall)
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(easternWall, DateTimeKind.Unspecified), eastern);
        }

        private DateTime NowUtc() => fakeUtc ?? DateTime.UtcNow;

        private double SecondsUntilNow(DateTime start, DateTime end, bool active)
        {
            return Math.Max(0d, ((active ? end : start) - NowUtc()).TotalSeconds);
        }

        private static ulong ItemUid(Item item) => item == null ? 0UL : item.uid.Value;
        private static ulong CupboardId(BuildingPrivlidge tc) => tc == null || tc.net == null ? 0UL : tc.net.ID.Value;
        private static bool Authorized(BuildingPrivlidge tc, ulong steamId) =>
            tc != null && tc.authorizedPlayers != null && tc.authorizedPlayers.Contains(steamId);

        private static BuildingPrivlidge TargetCupboard(BasePlayer player)
        {
            if (player == null) return null;
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5f)) return null;
            if (hit.collider == null) return null;
            return hit.collider.GetComponentInParent<BuildingPrivlidge>();
        }

        private void RedeemCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args == null || args.Length != 2 || !args[0].Equals("redeem", StringComparison.OrdinalIgnoreCase) || !VoucherSkins.ContainsKey(args[1]))
            {
                Reply(player, "Look at your authorized TC and use /raidvoucher redeem 1h | 24h | weekend");
                return;
            }
            if (!settings.PaidShields) { Reply(player, "Voucher redemption is currently disabled."); return; }
            BuildingPrivlidge tc = TargetCupboard(player);
            if (tc == null || !Authorized(tc, player.userID)) { Reply(player, "Look directly at a TC you are authorized on, within 5 metres."); return; }
            // Validate the carrier AND exact skin. Six-hour and all previous skins are retired.
            ulong requiredSkin = VoucherSkins[args[1]];
            Item voucher = null;
            foreach (var container in new[] { player.inventory.containerMain, player.inventory.containerBelt, player.inventory.containerWear })
            {
                if (container == null) continue;
                voucher = container.itemList.FirstOrDefault(i => i != null && i.info != null &&
                    i.info.shortname == VoucherShortname && i.skin == requiredSkin);
                if (voucher != null) break;
            }
            if (voucher == null) { Reply(player, "You need the matching Nightshade Raid Shield Token in your inventory."); return; }

            DateTime now = DateTime.UtcNow;
            DateTime start, end;
            bool active;
            Weekend(now, out start, out end, out active);
            ulong id = CupboardId(tc);
            Shield existing;
            store.Shields.TryGetValue(id, out existing);
            if (existing != null) Account(existing, now);
            if (existing != null && existing.ExpiresUtcTicks != end.Ticks) existing = null;
            double remainingWeekend = (end - (active ? now : start)).TotalSeconds;
            double newSeconds = args[1].Equals("weekend", StringComparison.OrdinalIgnoreCase) ? remainingWeekend :
                args[1].Equals("24h", StringComparison.OrdinalIgnoreCase) ? 86400d :
                3600d;
            if (existing != null && existing.RemainingSeconds > 0d && args[1].Equals("weekend", StringComparison.OrdinalIgnoreCase))
            { Reply(player, "This TC already has a shield. Use the existing time first; the full-weekend voucher was not consumed."); return; }
            double combined = (existing == null ? 0d : existing.RemainingSeconds) + newSeconds;
            if (combined > Math.Max(1, settings.MaximumStackedHours) * 3600d)
            { Reply(player, "This would exceed the maximum shield time on this TC. Voucher retained."); return; }
            Shield previous = existing;
            Shield shield = new Shield();
            shield.CupboardId = id;
            shield.BuildingId = tc.buildingID;
            shield.ExpiresUtcTicks = end.Ticks;
            shield.LastAccountedTicks = now.Ticks;
            shield.RemainingSeconds = Math.Min(combined, remainingWeekend);
            if (shield.RemainingSeconds <= 0d) { Reply(player, "No PvP weekend shield time is available. Voucher retained."); return; }

            // Persist redemption BEFORE removing the in-game item. On persistence failure voucher stays with buyer.
            store.Shields[id] = shield;
            try { Save(); }
            catch (Exception ex)
            {
                if (previous != null) store.Shields[id] = previous;
                else store.Shields.Remove(id);
                PrintError("Could not save redemption: " + ex.Message);
                Reply(player, "Shield could not be saved; voucher not consumed. Contact an admin.");
                return;
            }
            voucher.UseItem(1);
            PublishShield(shield);
            Reply(player, active ? "Shield active for this PvP weekend. Remaining protection expires Monday 3 AM Eastern; unused hours are lost." :
                "Shield queued for Friday at 8 PM Eastern. It does not protect during PvE weekdays. Any unused time expires Monday 3 AM Eastern.");
            Puts("Redeemed " + args[1] + " for TC " + id + " by " + player.UserIDString);
        }

        private void Account(Shield shield, DateTime now)
        {
            if (shield == null) return;
            if (now.Ticks >= shield.ExpiresUtcTicks) { shield.RemainingSeconds = 0d; shield.LastAccountedTicks = now.Ticks; return; }
            DateTime from = new DateTime(Math.Min(shield.LastAccountedTicks, now.Ticks), DateTimeKind.Utc);
            if (from > now) from = now;
            // Shield hours only drain during the one weekend for which this shield was issued.
            DateTime start, end;
            bool active;
            Weekend(new DateTime(shield.ExpiresUtcTicks, DateTimeKind.Utc).AddSeconds(-1), out start, out end, out active);
            DateTime a = from > start ? from : start;
            DateTime b = now < end ? now : end;
            if (b > a) shield.RemainingSeconds = Math.Max(0d, shield.RemainingSeconds - (b - a).TotalSeconds);
            shield.LastAccountedTicks = now.Ticks;
        }

        private bool OfflineProtected(BuildingPrivlidge tc, DateTime now)
        {
            if (!settings.OfflineProtection || tc.authorizedPlayers == null || tc.authorizedPlayers.Count == 0) return false;
            foreach (var auth in tc.authorizedPlayers)
            {
                ulong id = auth;
                if (BasePlayer.activePlayerList.Any(p => p != null && p.userID == id && p.IsConnected)) return false;
                long ticks;
                if (!store.LastSeenUtcTicks.TryGetValue(id, out ticks)) return false; // Fail open rather than guessing offline time.
                if ((now - new DateTime(ticks, DateTimeKind.Utc)).TotalMinutes < Math.Max(0, settings.OfflineGraceMinutes)) return false;
            }
            return true;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hit)
        {
            if (entity == null || hit == null || hit.damageTypes.Has(Rust.DamageType.Decay)) return null;
            // Exclude player/NPC/animal bodies; protect only buildings and their attached deployables.
            if (entity is BasePlayer) return null;
            DateTime now = DateTime.UtcNow;
            DateTime start, end;
            bool active;
            Weekend(now, out start, out end, out active);
            if (!active) return null;
            BuildingPrivlidge tc = entity as BuildingPrivlidge;
            if (tc == null) tc = entity.GetBuildingPrivilege();
            if (tc == null || tc.IsDestroyed) return null;
            // Ensure a structure's attached privilege refers to its OWN building.
            var block = entity as BuildingBlock;
            if (block != null && block.buildingID != tc.buildingID) return null;
            ulong attacker = hit.InitiatorPlayer != null ? hit.InitiatorPlayer.userID :
                hit.Initiator != null ? hit.Initiator.OwnerID : 0UL;
            if (!settings.ProtectFromAuthorizedPlayers && attacker != 0UL && Authorized(tc, attacker)) return null;
            Shield shield = null;
            bool paid = settings.PaidShields && store.Shields.TryGetValue(CupboardId(tc), out shield) && shield != null && shield.ExpiresUtcTicks == end.Ticks;
            if (paid) { Account(shield, now); paid = shield.RemainingSeconds > 0d && shield.BuildingId == tc.buildingID; PublishShield(shield); }
            bool offline = OfflineProtected(tc, now);
            if (!paid && !offline) return null;
            float reduction = paid ? 1f : Mathf.Clamp01(settings.OfflineReductionPercent / 100f);
            if (reduction <= 0f) return null;
            hit.damageTypes.ScaleAll(1f - reduction);
            if (settings.NotifyAttacker && hit.InitiatorPlayer != null)
            {
                float last;
                ulong id = hit.InitiatorPlayer.userID;
                if (!notifyThrottle.TryGetValue(id, out last) || Time.realtimeSinceStartup - last > 8f)
                {
                    notifyThrottle[id] = Time.realtimeSinceStartup;
                    string blockedMessage = paid ? "Nightshade raid shield blocked this hit." : "Offline protection reduced this hit.";
                    Reply(hit.InitiatorPlayer, blockedMessage);
                }
            }
            return null;
        }

        private void StatusCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            DateTime now = NowUtc();
            DateTime start, end;
            bool active;
            Weekend(now, out start, out end, out active);
            if (fakeUtc.HasValue) Reply(player, "Test clock is on. Window checks below are not the live clock. Clear it with nrp.faketime clear.");
            Reply(player, active ? "PvP weekend ends in " + Format(end - now) + "." : "PvP weekend starts in " + Format(start - now) + ".");
            BuildingPrivlidge tc = TargetCupboard(player);
            if (tc == null) tc = player.GetBuildingPrivilege();
            if (tc == null || !Authorized(tc, player.userID)) { Reply(player, "Look at or stand near an authorized TC to inspect its protection."); return; }
            Shield shield;
            DateTime realStart, realEnd;
            bool realActive;
            Weekend(DateTime.UtcNow, out realStart, out realEnd, out realActive);
            if (!store.Shields.TryGetValue(CupboardId(tc), out shield) || shield.ExpiresUtcTicks != realEnd.Ticks)
            { Reply(player, "TC paid shield: none for this weekend."); return; }
            Account(shield, DateTime.UtcNow);
            bool showActive = realActive && shield.RemainingSeconds > 0d;
            Reply(player, (showActive ? "TC raid shield active: " : realActive ? "TC raid shield expired: " : "TC raid shield queued: ") + Format(TimeSpan.FromSeconds(Math.Max(0, shield.RemainingSeconds))) +
                ". Unused time expires Monday 3 AM Eastern.");
        }

        private static string Format(TimeSpan t) => ((int)Math.Max(0, t.TotalHours)) + "h " + Math.Max(0, t.Minutes) + "m";
        private static void Reply(BasePlayer player, string message) => player.ChatMessage("<color=#b084ff>[Nightshade]</color> " + message);

        #region Window, ruleset, shields

        private void CheckWindow()
        {
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            bool changed = windowReady && active != windowActive;
            windowActive = active;
            windowReady = true;
            if (changed)
            {
                RefreshPublished();
                if (active) Interface.CallHook("OnNightshadePvPStarted", start.Ticks, end.Ticks);
                else Interface.CallHook("OnNightshadePvPEnded", start.Ticks, end.Ticks);
                EnsureRuleset();
                RefreshShieldTimer();
                Puts((active ? "PvP window opened. " : "PvP window closed. ") + FormatEdge(start) + " -> " + FormatEdge(end) + (fakeUtc.HasValue ? " (test clock)" : ""));
            }
            else
            {
                RefreshShieldTimer();
            }
            ScheduleBoundary();
        }

        private void ScheduleBoundary()
        {
            boundaryTimer?.Destroy();
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            double seconds = Math.Max(1d, ((active ? end : start) - NowUtc()).TotalSeconds);
            // Wake at least daily so a clock change or DST correction is not missed.
            seconds = Math.Min(seconds, 86400d);
            boundaryTimer = timer.Once((float)seconds, CheckWindow);
        }

        private void RefreshShieldTimer()
        {
            shieldTimer?.Destroy();
            shieldTimer = null;
            DateTime start, end;
            bool active;
            Weekend(DateTime.UtcNow, out start, out end, out active);
            if (!active) return;
            shieldTimer = timer.Every(30f, () =>
            {
                bool ended = false;
                foreach (var shield in store.Shields.Values.ToList())
                {
                    string before;
                    published.TryGetValue(shield.CupboardId, out before);
                    PublishShield(shield);
                    string after;
                    published.TryGetValue(shield.CupboardId, out after);
                    if (before == "active" && after != "active") ended = true;
                }
                if (ended) Save();
            });
        }

        private void SeedShieldStates()
        {
            suppressHooks = true;
            RefreshPublished();
            suppressHooks = false;
        }

        private void RefreshPublished()
        {
            var seen = new HashSet<ulong>();
            foreach (var shield in store.Shields.Values.ToList())
            {
                if (shield == null) continue;
                seen.Add(shield.CupboardId);
                PublishShield(shield);
            }
            foreach (var id in published.Keys.ToList())
            {
                if (seen.Contains(id)) continue;
                PublishState(id, 0u, "none", 0d);
            }
        }

        private void PublishShield(Shield shield)
        {
            if (shield == null) return;
            var described = Describe(shield);
            PublishState(shield.CupboardId, shield.BuildingId, described["state"] as string, described["seconds"] is double seconds ? seconds : 0d);
        }

        private void PublishState(ulong tcId, uint buildingId, string state, double seconds)
        {
            string old;
            published.TryGetValue(tcId, out old);
            if (old == state) return;
            if (state == "none") published.Remove(tcId);
            else published[tcId] = state;
            if (suppressHooks) return;
            if (state == "active") Interface.CallHook("OnNightshadeShieldStarted", tcId, buildingId, seconds);
            else if (state == "queued") Interface.CallHook("OnNightshadeShieldQueued", tcId, buildingId, seconds);
            else if (old == "active" || old == "queued") Interface.CallHook("OnNightshadeShieldEnded", tcId, buildingId);
        }

        private Dictionary<string, object> Describe(Shield shield)
        {
            if (shield == null) return NoneShield();
            DateTime start, end;
            bool active;
            Weekend(DateTime.UtcNow, out start, out end, out active);
            if (shield.ExpiresUtcTicks != end.Ticks) return NoneShield();
            Account(shield, DateTime.UtcNow);
            double seconds = Math.Max(0d, shield.RemainingSeconds);
            string state = seconds <= 0d ? "none" : active ? "active" : "queued";
            // A test clock changes the label other plugins display, without draining stored time.
            if (fakeUtc.HasValue && state != "none")
            {
                DateTime fakeStart, fakeEnd;
                bool fakeActive;
                Weekend(fakeUtc.Value, out fakeStart, out fakeEnd, out fakeActive);
                if (shield.ExpiresUtcTicks == fakeEnd.Ticks || shield.ExpiresUtcTicks == end.Ticks)
                    state = fakeActive ? "active" : "queued";
            }
            return new Dictionary<string, object>
            {
                ["state"] = state,
                ["seconds"] = seconds,
                ["tcId"] = shield.CupboardId,
                ["buildingId"] = shield.BuildingId
            };
        }

        private static Dictionary<string, object> NoneShield()
        {
            return new Dictionary<string, object>
            {
                ["state"] = "none",
                ["seconds"] = 0d,
                ["tcId"] = 0UL,
                ["buildingId"] = 0u
            };
        }

        // TruePVE's schedule command cannot be disabled cleanly: both "tpve.sched disable"
        // and "tpve sched disable" reach HandleScheduleSet with args ["disable"], which
        // logs "Invalid parameters for command tpve.disable". The schedule is left off
        // in configs/TruePVE.json. This only reads that file and warns once if it is on.
        private void WarnIfTruePveScheduleEnabled()
        {
            if (!settings.DriveTruePve) return;
            string path = Path.Combine(Interface.Oxide.ConfigDirectory, "TruePVE.json");
            try
            {
                if (!File.Exists(path))
                {
                    PrintWarning("TruePVE.json was not found. Leave Schedule.enabled false so TruePVE's clock does not fight the Eastern weekend window.");
                    return;
                }
                JToken enabled = JObject.Parse(File.ReadAllText(path))["Schedule"]?["enabled"];
                if (enabled != null && enabled.Type == JTokenType.Boolean && enabled.Value<bool>())
                    PrintWarning("TruePVE Schedule.enabled is true. Set Schedule.enabled to false in carbon/configs/TruePVE.json and reload TruePVE. NightshadeRaidProtection sets the ruleset; TruePVE's own schedule will override it.");
            }
            catch (Exception ex)
            {
                PrintWarning("Could not read TruePVE's schedule flag: " + ex.Message);
            }
        }

        private void EnsureRuleset()
        {
            if (!settings.DriveTruePve || TruePVE == null || !TruePVE.IsLoaded) return;
            if (settings.YieldToPurge && PurgeStatus != null && PurgeStatus.IsLoaded)
            {
                string purgeRuleset = PurgeStatus.Call("API_GetActiveRulesetOverride") as string;
                if (!string.IsNullOrEmpty(purgeRuleset))
                {
                    SetRuleset(purgeRuleset);
                    return;
                }
            }
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            SetRuleset(active ? settings.PvpRuleset : settings.PveRuleset);
        }

        private void SetRuleset(string name)
        {
            if (string.IsNullOrEmpty(name) || TruePVE == null || !TruePVE.IsLoaded) return;
            string current = TruePVE.Call("CurrentRuleSetName") as string;
            if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase)) return;
            Server.Command("tpve.ruleset", name);
            Puts("Set TruePVE ruleset to " + name + (fakeUtc.HasValue ? " (test clock)" : "") + ".");
        }

        private string FormatEdge(DateTime utc)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), eastern);
            return local.ToString("ddd MMM d h:mm tt", CultureInfo.InvariantCulture) + " ET";
        }

        private void CmdWindow(ConsoleSystem.Arg arg)
        {
            if (!ConsoleAdmin(arg)) return;
            DateTime start, end;
            bool active;
            Weekend(NowUtc(), out start, out end, out active);
            string current = TruePVE != null && TruePVE.IsLoaded ? TruePVE.Call("CurrentRuleSetName") as string : "(TruePVE not loaded)";
            string purge = PurgeStatus != null && PurgeStatus.IsLoaded ? (PurgeStatus.Call("API_GetActiveRulesetOverride") as string ?? "inactive") : "(purge plugin not loaded)";
            arg.ReplyWith("Window " + (active ? "ACTIVE" : "inactive")
                + " | " + FormatEdge(start) + " -> " + FormatEdge(end)
                + " | seconds " + Math.Ceiling(SecondsUntilNow(start, end, active))
                + " | target ruleset " + (active ? settings.PvpRuleset : settings.PveRuleset)
                + " | TruePVE now " + current
                + " | purge override " + purge
                + " | zone " + eastern.Id + (usedZoneFallback ? " (fallback)" : "")
                + " | test clock " + (fakeUtc.HasValue ? fakeUtc.Value.ToString("u") : "off"));
        }

        private void CmdFakeTime(ConsoleSystem.Arg arg)
        {
            if (!ConsoleAdmin(arg)) return;
            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith(fakeUtc.HasValue
                    ? "Test clock is " + fakeUtc.Value.ToString("u") + " (" + FormatEdge(fakeUtc.Value) + "). nrp.faketime clear"
                    : "Test clock is off. nrp.faketime 2026-11-02T07:59:00Z   or   nrp.faketime eastern 2026-10-30 20:00");
                return;
            }
            if (arg.Args[0].Equals("clear", StringComparison.OrdinalIgnoreCase) || arg.Args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                fakeUtc = null;
                CheckWindow();
                EnsureRuleset();
                PrintWarning("PvP test clock cleared. The live Eastern window is in use again.");
                arg.ReplyWith("Test clock cleared.");
                return;
            }
            bool easternWall = arg.Args[0].Equals("eastern", StringComparison.OrdinalIgnoreCase) || arg.Args[0].Equals("et", StringComparison.OrdinalIgnoreCase);
            string text = easternWall ? string.Join(" ", arg.Args.Skip(1)) : string.Join(" ", arg.Args);
            DateTime parsed;
            if (easternWall)
            {
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                {
                    arg.ReplyWith("Could not parse that Eastern time. Example: nrp.faketime eastern 2026-10-30 20:00");
                    return;
                }
                try { fakeUtc = ToUtc(parsed); }
                catch (Exception ex)
                {
                    arg.ReplyWith("That Eastern time could not be converted: " + ex.Message);
                    return;
                }
            }
            else
            {
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                {
                    arg.ReplyWith("Could not parse that UTC time. Example: nrp.faketime 2026-11-02T07:59:00Z");
                    return;
                }
                fakeUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            CheckWindow();
            EnsureRuleset();
            PrintWarning("PvP TEST CLOCK is " + fakeUtc.Value.ToString("u") + " (" + FormatEdge(fakeUtc.Value) + "). TruePVE will follow it. Shield drain still uses the real clock. Clear with nrp.faketime clear.");
            arg.ReplyWith("Test clock set to " + fakeUtc.Value.ToString("u") + " (" + FormatEdge(fakeUtc.Value) + ").");
        }

        private bool ConsoleAdmin(ConsoleSystem.Arg arg)
        {
            if (arg == null) return false;
            if (arg.Connection == null) return true;
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (player == null) return false;
            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin)) return true;
            arg.ReplyWith("No permission.");
            return false;
        }

        private static TimeZoneInfo ResolveEastern(out bool fallback)
        {
            fallback = false;
            string[] ids = { "America/New_York", "Eastern Standard Time", "US/Eastern" };
            foreach (string id in ids)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            fallback = true;
            try
            {
                TimeZoneInfo.TransitionTime start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
                TimeZoneInfo.TransitionTime end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
                TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    new DateTime(2007, 1, 1), new DateTime(9999, 12, 31), TimeSpan.FromHours(1), start, end);
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Nightshade-Eastern", TimeSpan.FromHours(-5), "Eastern Time (fallback)",
                    "Eastern Standard Time", "Eastern Daylight Time", new[] { rule });
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("Nightshade-Eastern-EST", TimeSpan.FromHours(-5), "Eastern Time (fixed EST)", "EST");
            }
        }

        #endregion
    }
}
