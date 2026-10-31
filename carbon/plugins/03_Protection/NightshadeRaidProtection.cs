using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeRaidProtection", "Nightshade", "1.0.4")]
    [Description("PvP-window-only Tool Cupboard raid shields and configurable offline protection; three distinct custom icon vouchers; no legacy six-hour vouchers.")]
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

        private Settings settings;
        private Store store;
        private TimeZoneInfo eastern;

        private class Settings
        {
            [JsonProperty("Enable paid TC raid shields")] public bool PaidShields = true;
            [JsonProperty("Enable automatic offline raid protection during PvP")] public bool OfflineProtection = true;
            [JsonProperty("Minutes everyone authorized on TC must be offline before auto protection")] public int OfflineGraceMinutes = 30;
            [JsonProperty("Offline protection damage reduction percent (100 = fully block)")] public int OfflineReductionPercent = 100;
            [JsonProperty("Prevent teammates authorized on TC from damaging their own shielded building")] public bool ProtectFromAuthorizedPlayers = false;
            [JsonProperty("Show blocked-hit message to attacker (rate limited)")] public bool NotifyAttacker = true;
            [JsonProperty("Maximum total paid shield time on one TC in hours")] public int MaximumStackedHours = 72;
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
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(settings, true);

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            cmd.AddChatCommand("raidvoucher", this, nameof(RedeemCommand));
            cmd.AddChatCommand("raidshield", this, nameof(StatusCommand));
            try { store = Interface.Oxide.DataFileSystem.ReadObject<Store>(DataPath) ?? new Store(); }
            catch (Exception ex) { throw new Exception("Raid shield data could not be loaded: purchases disabled to prevent lost vouchers. " + ex.Message); }
            if (store.Shields == null) store.Shields = new Dictionary<ulong, Shield>();
            if (store.LegitimateVoucherUids == null) store.LegitimateVoucherUids = new HashSet<ulong>();
            if (store.LastSeenUtcTicks == null) store.LastSeenUtcTicks = new Dictionary<ulong, long>();
            try { eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch { eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        }

        private void Save() => Interface.Oxide.DataFileSystem.WriteObject(DataPath, store);
        private void OnServerSave() => Save();
        private void Unload() => Save();
        private void OnNewSave(string filename)
        {
            store.Shields.Clear();
            store.LegitimateVoucherUids.Clear();
            store.LastSeenUtcTicks.Clear();
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
        }
        private void OnEntityKill(BuildingPrivlidge tc)
        {
            if (tc == null || tc.net == null) return;
            store.Shields.Remove(tc.net.ID.Value);
            Save();
        }

        // Returns Friday 8 PM through Monday 3 AM Eastern, DST-safe. Outside that window, returns NEXT weekend.
        private void Weekend(DateTime utc, out DateTime startUtc, out DateTime endUtc, out bool active)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, eastern);
            int back = ((int)local.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            DateTime friday = local.Date.AddDays(-back);
            DateTime candidateStart = friday.AddHours(20);
            DateTime candidateEnd = friday.AddDays(3).AddHours(3);
            active = local >= candidateStart && local < candidateEnd;
            if (!active && local >= candidateEnd) friday = friday.AddDays(7);
            if (!active && local < candidateStart) { /* This week's Friday remains the next start. */ }
            startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(friday.AddHours(20), DateTimeKind.Unspecified), eastern);
            endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(friday.AddDays(3).AddHours(3), DateTimeKind.Unspecified), eastern);
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
            if (paid) { Account(shield, now); paid = shield.RemainingSeconds > 0d && shield.BuildingId == tc.buildingID; }
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
            DateTime now = DateTime.UtcNow;
            DateTime start, end;
            bool active;
            Weekend(now, out start, out end, out active);
            Reply(player, active ? "PvP weekend ends in " + Format(end - now) + "." : "PvP weekend starts in " + Format(start - now) + ".");
            BuildingPrivlidge tc = TargetCupboard(player);
            if (tc == null) tc = player.GetBuildingPrivilege();
            if (tc == null || !Authorized(tc, player.userID)) { Reply(player, "Look at or stand near an authorized TC to inspect its protection."); return; }
            Shield shield;
            if (!store.Shields.TryGetValue(CupboardId(tc), out shield) || shield.ExpiresUtcTicks != end.Ticks)
            { Reply(player, "TC paid shield: none for this weekend."); return; }
            Account(shield, now);
            Reply(player, (active && shield.RemainingSeconds > 0d ? "TC raid shield active: " : active ? "TC raid shield expired: " : "TC raid shield queued: ") + Format(TimeSpan.FromSeconds(Math.Max(0, shield.RemainingSeconds))) +
                ". Unused time expires Monday 3 AM Eastern.");
        }

        private static string Format(TimeSpan t) => ((int)Math.Max(0, t.TotalHours)) + "h " + Math.Max(0, t.Minutes) + "m";
        private static void Reply(BasePlayer player, string message) => player.ChatMessage("<color=#b084ff>[Nightshade]</color> " + message);
    }
}
