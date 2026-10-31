using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Carbon.Plugins
{
    [Info("UnifiedLootProtection", "Nightshade", "1.1.1")]
    [Description("Unified loot protection combining damage-based ownership and TC-based container protection.")]
    public class UnifiedLootProtection : CarbonPlugin
    {
        #region Fields

        // entity net ID -> ownership data
        private readonly Dictionary<ulong, OwnershipData> _entityOwnership = new();

        // set of prefab short names that are always publicly accessible
        private static readonly HashSet<string> PublicEntities = new(StringComparer.OrdinalIgnoreCase)
        {
            "mailbox.deployed", "repairbench_deployed", "dropbox",
            "workbench1", "workbench2", "workbench3", "iotable",
            "research.table", "researchtable_deployed", "mixingtable.deployed",
            "electric.switch", "sam_site_turret_deployed", "autoturret_deployed",
            "computerstation", "vendingmachine.deployed"
        };

        private PluginConfig _config;

        // entityNetId -> set of steamIds who have been granted access to that specific entity
        private Dictionary<ulong, HashSet<ulong>> _shareData = new();

        // item UID -> thrower steamId — tracks in-flight supply signals (fixes race condition)
        private readonly Dictionary<ulong, ulong> _pendingSupplyOwners = new();

        // net IDs of CH47s currently carrying a Bradley drop — immune to damage until cargo drops
        private readonly HashSet<ulong> _immuneCh47s = new();

        // Tracks NPC death positions so we can match ownership to the spawned LootableCorpse
        // position -> OwnershipData, written in OnEntityDeath, consumed in OnEntitySpawned(LootableCorpse)
        private readonly Dictionary<Vector3, OwnershipData> _npcDeathPositions = new();

        // steamId -> last known display name for offline share lookups
        private Dictionary<ulong, string> _knownPlayers = new();

        private const string PermAdmin = "unifiedlootprotection.admin";

        [PluginReference] private Plugin RaidableBases, TruePVE;

        #endregion

        #region Config

        private class PluginConfig
        {
            [JsonProperty("Bradley")]
            public BradleyConfig Bradley { get; set; } = new();

            [JsonProperty("Helicopter")]
            public HeliConfig Helicopter { get; set; } = new();

            [JsonProperty("HackableCrate")]
            public HackableCrateConfig HackableCrate { get; set; } = new();

            [JsonProperty("NPC")]
            public NpcConfig NPC { get; set; } = new();

            [JsonProperty("SupplyDrop")]
            public SupplyDropConfig SupplyDrop { get; set; } = new();

            [JsonProperty("StorageProtection")]
            public StorageConfig StorageProtection { get; set; } = new();

            [JsonProperty("CorpseProtection")]
            public CorpseConfig CorpseProtection { get; set; } = new();

            [JsonProperty("WeaponRackProtection")]
            public WeaponRackConfig WeaponRackProtection { get; set; } = new();

            [JsonProperty("ChatAlerts")]
            public ChatAlertsConfig ChatAlerts { get; set; } = new();

            [JsonProperty("WeekendPvP")]
            public WeekendPvPConfig WeekendPvP { get; set; } = new();
        }

        private class WeekendPvPConfig
        {
            [JsonProperty("Disable UnifiedLootProtection access restrictions during PvP weekend")]
            public bool Enabled { get; set; } = true;

            [JsonProperty("Read TruePVE GetPlayerMapping for the active weekend profile")]
            public bool ReadTruePVEProfile { get; set; } = true;

            [JsonProperty("Weekend TruePVE ruleset name")]
            public string WeekendRuleSet { get; set; } = "nightshade_pvp_weekend";

            [JsonProperty("Use Friday 8 PM - Monday 3 AM Eastern clock fallback when TruePVE mapping is unavailable")]
            public bool EasternClockFallback { get; set; } = true;

            [JsonProperty("Disable Bradley, heli, NPC, hackable crate and supply drop ownership locks")]
            public bool DisableEventOwnershipLocks { get; set; } = true;
        }

        private class BradleyConfig
        {
            public bool Enabled { get; set; } = true;
            public float DamageLockThreshold { get; set; } = 0.2f;
            public float TooHotSeconds { get; set; } = 180f;
            public int LockForSeconds { get; set; } = 300;
            public bool BroadcastKillToKiller { get; set; } = true;
            public float XPReward { get; set; } = 200f;
        }

        private class HeliConfig
        {
            public bool Enabled { get; set; } = true;
            public float DamageLockThreshold { get; set; } = 0.35f;
            public float TooHotSeconds { get; set; } = 480f;
            public int LockForSeconds { get; set; } = 480;
            public bool BroadcastKillToKiller { get; set; } = true;
            public bool BroadcastLockedToChat { get; set; } = true;
            public float XPReward { get; set; } = 500f;
            public float RustRewardsRP { get; set; } = 250f;
        }

        private class HackableCrateConfig
        {
            public bool Enabled { get; set; } = true;
            public int LockForSeconds { get; set; } = 900;
        }

        private class NpcConfig
        {
            public bool Enabled { get; set; } = true;
            public float DamageLockThreshold { get; set; } = 0.2f;
            public int LockForSeconds { get; set; } = 300;
            public bool BroadcastKillToKiller { get; set; } = true;
            public float RustRewardsRP { get; set; } = 25f;
        }

        private class SupplyDropConfig
        {
            public bool Enabled { get; set; } = true;
            public int LockForSeconds { get; set; } = 600;
        }

        private class StorageConfig
        {
            public bool Enabled { get; set; } = true;
            public bool UseTeams { get; set; } = true;
            public bool UseCupboard { get; set; } = true;
            public bool OnlyInCupboardRange { get; set; } = true;
        }

        private class CorpseConfig
        {
            public bool Enabled { get; set; } = true;
            public bool UseTeams { get; set; } = true;
            public bool AdminCanLoot { get; set; } = false;
        }

        private class WeaponRackConfig
        {
            public bool Enabled { get; set; } = true;
            public bool UseTeams { get; set; } = true;
        }

        private class ChatAlertsConfig
        {
            [JsonProperty("KillLockNotifyOwner")]
            public bool KillLockNotifyOwner { get; set; } = true;

            [JsonProperty("TooHot")]
            public bool TooHot { get; set; } = true;

            [JsonProperty("LootBlocked")]
            public bool LootBlocked { get; set; } = true;

            [JsonProperty("StorageBlocked")]
            public bool StorageBlocked { get; set; } = true;

            [JsonProperty("CorpseBlocked")]
            public bool CorpseBlocked { get; set; } = true;

            [JsonProperty("WeaponRackBlocked")]
            public bool WeaponRackBlocked { get; set; } = true;

            [JsonProperty("TCAuthBlocked")]
            public bool TCAuthBlocked { get; set; } = true;

            [JsonProperty("VendingAdminBlocked")]
            public bool VendingAdminBlocked { get; set; } = true;

            [JsonProperty("PickupBlocked")]
            public bool PickupBlocked { get; set; } = true;

            [JsonProperty("OvenToggleBlocked")]
            public bool OvenToggleBlocked { get; set; } = true;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                    _config = new PluginConfig();
                if (_config.WeekendPvP == null)
                    _config.WeekendPvP = new WeekendPvPConfig();
                SaveConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Data Persistence

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);

            var stored = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, HashSet<ulong>>>("UnifiedLootProtection_shares");
            if (stored != null) _shareData = stored;

            var knownStored = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, string>>("UnifiedLootProtection_knownplayers");
            if (knownStored != null) _knownPlayers = knownStored;

            Puts($"[UnifiedLootProtection] Loaded share data: {_shareData.Count} entries. Known players: {_knownPlayers.Count}.");
        }

        // Called when a player connects — keep known players list up to date
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            _knownPlayers[player.userID] = player.displayName;
            SaveKnownPlayers();
        }

        private void SaveShareData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("UnifiedLootProtection_shares", _shareData);
        }

        private void SaveKnownPlayers()
        {
            Interface.Oxide.DataFileSystem.WriteObject("UnifiedLootProtection_knownplayers", _knownPlayers);
        }

        #endregion

        #region Data Structures

        private class OwnershipData
        {
            public ulong OwnerId { get; set; }
            public string OwnerName { get; set; }
            public HashSet<ulong> Contributors { get; set; } = new();
            public Dictionary<ulong, float> DamageMap { get; set; } = new();
            public float TotalDamage { get; set; }
            public float EntityMaxHealth { get; set; }
            public DateTime LockedAt { get; set; }
            public int LockDurationSeconds { get; set; }
            public float TooHotSeconds { get; set; }

            public bool IsExpired =>
                LockDurationSeconds > 0 && (DateTime.UtcNow - LockedAt).TotalSeconds >= LockDurationSeconds;

            public bool IsTooHot =>
                TooHotSeconds > 0 && (DateTime.UtcNow - LockedAt).TotalSeconds < TooHotSeconds;

            public bool CanAccess(ulong playerId)
            {
                return OwnerId == playerId || Contributors.Contains(playerId);
            }
        }

        #endregion

        #region Weekend PvP / TruePVE profile integration

        // TruePVE 2.4.4 exposes GetPlayerMapping, NOT a global GetCurrentRuleSet hook.
        // The Nightshade TruePVE config must map nightshade_pvp_weekend to itself to
        // make the current global weekend profile visible through GetPlayerMapping.
        // On weekday monument zones it may return monumentpvp: that is intentionally
        // NOT a signal to disable ULP's server-wide loot protections.
        private string GetTruePveMapping(BasePlayer player)
        {
            if (_config?.WeekendPvP == null || !_config.WeekendPvP.ReadTruePVEProfile ||
                TruePVE == null || !TruePVE.IsLoaded || player == null)
                return null;
            try
            {
                return TruePVE.Call("GetPlayerMapping", player) as string;
            }
            catch (Exception ex)
            {
                PrintWarning("TruePVE GetPlayerMapping failed: " + ex.Message);
                return null;
            }
        }

        private TimeZoneInfo _nightshadeEasternZone;
        private bool _triedEasternZone;
        private bool _warnedEasternZone;

        private bool IsEasternWeekendWindow()
        {
            if (!_triedEasternZone)
            {
                _triedEasternZone = true;
                try { _nightshadeEasternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
                catch
                {
                    try { _nightshadeEasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                    catch { /* Fail closed if neither timezone is installed. */ }
                }
            }
            if (_nightshadeEasternZone == null)
            {
                if (!_warnedEasternZone)
                {
                    _warnedEasternZone = true;
                    PrintError("Could not find Eastern timezone; weekend clock fallback disabled. Check host timezone data.");
                }
                return false;
            }

            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _nightshadeEasternZone);
            switch (local.DayOfWeek)
            {
                case DayOfWeek.Friday: return local.TimeOfDay >= TimeSpan.FromHours(20);
                case DayOfWeek.Saturday:
                case DayOfWeek.Sunday: return true;
                case DayOfWeek.Monday: return local.TimeOfDay < TimeSpan.FromHours(3);
                default: return false;
            }
        }

        private bool IsWeekendPvp(BasePlayer player = null)
        {
            var weekend = _config?.WeekendPvP;
            if (weekend == null || !weekend.Enabled) return false;
            string mapping = GetTruePveMapping(player);
            if (!string.IsNullOrEmpty(mapping) &&
                string.Equals(mapping, weekend.WeekendRuleSet, StringComparison.OrdinalIgnoreCase))
                return true;
            // The clock fallback also handles NPC/cargo events with no player argument,
            // as well as missing/empty TruePVE mapping for the weekend ruleset.
            return weekend.EasternClockFallback && IsEasternWeekendWindow();
        }

        private bool SkipEventOwnership(BasePlayer player = null) =>
            IsWeekendPvp(player) && _config.WeekendPvP.DisableEventOwnershipLocks;

        #endregion

        #region Damage Tracking

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            // Immune CH47 — cancel all damage until cargo is dropped
            // Belt-and-suspenders: add to immune set here in case OnEntitySpawned missed it
            if (entity is CH47Helicopter)
            {
                _immuneCh47s.Add(entity.net.ID.Value);
                info.damageTypes.ScaleAll(0f);
                return true;
            }

            if (SkipEventOwnership(info.InitiatorPlayer)) return null;

            if (!(entity is BradleyAPC || entity is PatrolHelicopter ||
                  entity is BaseNpc || entity is ScientistNPC))
                return null;

            var attacker = info.InitiatorPlayer;
            if (attacker == null || attacker.IsNpc) return null;

            var uid = entity.net.ID.Value;
            float damage = info.damageTypes.Total();

            if (!_entityOwnership.TryGetValue(uid, out var data))
            {
                data = new OwnershipData
                {
                    EntityMaxHealth = entity.MaxHealth(),
                    TooHotSeconds = GetTooHotSeconds(entity),
                    LockDurationSeconds = GetLockDuration(entity)
                };
                _entityOwnership[uid] = data;
            }

            data.TotalDamage += damage;
            data.DamageMap.TryGetValue(attacker.userID, out float existing);
            data.DamageMap[attacker.userID] = existing + damage;

            float threshold = GetDamageThreshold(entity);
            float playerFraction = data.DamageMap[attacker.userID] / data.EntityMaxHealth;

            if (data.OwnerId == 0 && playerFraction >= threshold)
            {
                data.OwnerId = attacker.userID;
                data.OwnerName = attacker.displayName;
                data.LockedAt = DateTime.UtcNow;

                if (attacker.currentTeam != 0)
                {
                    var team = RelationshipManager.ServerInstance.FindTeam(attacker.currentTeam);
                    if (team != null)
                        foreach (var memberId in team.members)
                            data.Contributors.Add(memberId);
                }
            }
            else if (data.OwnerId != 0)
            {
                data.Contributors.Add(attacker.userID);
                if (attacker.currentTeam != 0)
                {
                    var team = RelationshipManager.ServerInstance.FindTeam(attacker.currentTeam);
                    if (team != null)
                        foreach (var memberId in team.members)
                            data.Contributors.Add(memberId);
                }
            }

            return null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return;
            if (SkipEventOwnership(info?.InitiatorPlayer)) return;
            var uid = entity.net.ID.Value;

            if (!_entityOwnership.TryGetValue(uid, out var data)) return;
            if (data.OwnerId == 0) return;

            // For NPCs, record death position so OnEntitySpawned(LootableCorpse) can match it
            if (entity is BaseNpc || entity is ScientistNPC)
                _npcDeathPositions[entity.transform.position] = data;

            var owner = BasePlayer.FindByID(data.OwnerId);
            if (owner == null) return;

            bool shouldNotify = false;
            string entityType = "";

            if (entity is BradleyAPC && _config.Bradley.BroadcastKillToKiller)
            { shouldNotify = true; entityType = "Bradley APC"; }
            else if (entity is PatrolHelicopter && _config.Helicopter.BroadcastKillToKiller)
            { shouldNotify = true; entityType = "Patrol Helicopter"; }
            else if ((entity is BaseNpc || entity is ScientistNPC) && _config.NPC.BroadcastKillToKiller)
            { shouldNotify = true; entityType = "NPC"; }

            if (shouldNotify && _config.ChatAlerts.KillLockNotifyOwner)
                owner.ChatMessage($"<color=#88D66C>You killed the {entityType}! Loot is locked to you and your team.</color>");
        }

        #endregion

        #region Loot Access Control

        // Carbon requires typed overloads — a single BaseEntity signature misses most container types
        private object CanLootEntity(BasePlayer player, StorageContainer container) => CheckLootAccess(player, container);
        private object CanLootEntity(BasePlayer player, ContainerIOEntity container) => CheckLootAccess(player, container);
        private object CanLootEntity(BasePlayer player, LootableCorpse corpse) => CheckLootAccess(player, corpse);
        private object CanLootEntity(BasePlayer player, DroppedItemContainer container) => CheckLootAccess(player, container);
        private object CanLootEntity(BasePlayer player, WorldItem item) => CheckLootAccess(player, item);

        private object CheckLootAccess(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return null;
            if (IsWeekendPvp(player)) return null;

            // Admins always bypass storage/loot protection
            if (player.IsAdmin)
            {
                return null;
            }

            var shortName = entity.ShortPrefabName;
            if (PublicEntities.Contains(shortName))
            {
                return null;
            }

            // Allow players buying from a vending machine (front panel) or depositing into a dropbox
            if (IsVendingOpen(player, entity) || IsDropBoxOpen(player, entity))
            {
                return null;
            }

            var uid = entity.net.ID.Value;

            if (_entityOwnership.TryGetValue(uid, out var data) && data.OwnerId != 0)
            {
                if (data.IsExpired)
                {
                    _entityOwnership.Remove(uid);
                    _shareData.Remove(uid); // FIX 3: clean shares on inline expiry check
                    return null;
                }

                if (data.IsTooHot)
                {
                    if (_config.ChatAlerts.TooHot)
                        player.ChatMessage("<color=#A04A4A>This loot is too hot right now. Wait a moment.</color>");
                    return false;
                }

                if (!data.CanAccess(player.userID))
                {
                    if (_config.ChatAlerts.LootBlocked)
                        player.ChatMessage("<color=#A04A4A>This loot belongs to another player.</color>");
                    return false;
                }

                return null;
            }

            // Per-entity share check
            if (_shareData.TryGetValue(uid, out var entityShared) && entityShared.Contains(player.userID))
                return null;

            // Storage protection — OwnerID + TC, always enforced
            if (_config.StorageProtection.Enabled)
            {
                bool isProtectable = entity is StorageContainer
                    || entity is BaseOven
                    || entity is ContainerIOEntity;

                if (!isProtectable) return null;

                var ownerId = entity.OwnerID;

                // ── OwnerID check ─────────────────────────────────────────────
                if (ownerId != 0)
                {
                    if (ownerId == player.userID)
                    {
                        return null;
                    }

                    if (player.currentTeam != 0)
                    {
                        var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                        if (team != null && team.members.Contains(ownerId))
                        {
                            return null;
                        }
                    }
                }
                else
                {
                }

                // ── TC check ──────────────────────────────────────────────────
                // Supply drops are owner-only — skip TC check entirely
                bool isSupplyDrop = entity is SupplyDrop;

                if (!isSupplyDrop)
                {
                    var privs = new List<BuildingPrivlidge>();
                    Vis.Entities(entity.transform.position, 16f, privs);

                    if (privs.Count > 0)
                    {
                        foreach (var priv in privs)
                        {
                            if (IsAuthorizedOrTeammate(player, priv)) return null;
                        }

                        // TC found but not authed — deny
                        if (_config.ChatAlerts.StorageBlocked)
                            player.ChatMessage("<color=#A04A4A>You are not authorized to access this storage.</color>");
                        return false;
                    }
                }

                // ── No TC (or supply drop), no OwnerID ────────────────────────
                if (ownerId == 0) return null;

                // ── Has OwnerID, player didn't pass owner/team check ──────────
                if (_config.ChatAlerts.StorageBlocked)
                    player.ChatMessage("<color=#A04A4A>You are not authorized to access this storage.</color>");
                return false;
            }

            return null;
        }

        // Protect sleeping players all week, including PvP weekends.
        // Do NOT apply IsWeekendPvp here: CanLootPlayer covers live player inventories.
        // Dead-player corpse containers are handled separately by CanLootEntity.
        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (!_config.CorpseProtection.Enabled) return null;
            if (looter == null || target == null) return null;
            if (looter.IsAdmin && _config.CorpseProtection.AdminCanLoot) return null;
            if (looter.userID == target.userID) return null;

            if (_config.CorpseProtection.UseTeams && AreTeammates(looter, target))
                return null;

            if (_config.ChatAlerts.CorpseBlocked)
                looter.ChatMessage("<color=#A04A4A>You cannot loot another player's corpse.</color>");
            return false;
        }

        private object CanRackedWeaponMount(BasePlayer player, WeaponRack rack) => CheckWeaponRack(player, rack);
        private object CanRackedWeaponTake(BasePlayer player, WeaponRack rack) => CheckWeaponRack(player, rack);
        private object CanRackedWeaponSwap(BasePlayer player, WeaponRack rack) => CheckWeaponRack(player, rack);
        private object CanRackedWeaponLoad(BasePlayer player, WeaponRack rack) => CheckWeaponRack(player, rack);
        private object CanRackedWeaponUnload(BasePlayer player, WeaponRack rack) => CheckWeaponRack(player, rack);

        private object CheckWeaponRack(BasePlayer player, WeaponRack rack)
        {
            if (!_config.WeaponRackProtection.Enabled) return null;
            if (player == null || rack == null) return null;
            if (IsWeekendPvp(player)) return null;
            if (player.IsAdmin) return null;

            if (rack.net != null && _shareData.TryGetValue(rack.net.ID.Value, out var rackShared) && rackShared.Contains(player.userID))
                return null;

            var privs = new List<BuildingPrivlidge>();
            Vis.Entities(rack.transform.position, 16f, privs);
            if (privs.Count == 0) return null;

            foreach (var priv in privs)
            {
                if (IsAuthorizedOrTeammate(player, priv))
                    return null;
            }

            if (_config.ChatAlerts.WeaponRackBlocked)
                player.ChatMessage("<color=#A04A4A>You cannot access this weapon rack.</color>");
            return false;
        }

        // CanHackCrate gives us the actual initiating player — no proximity guessing needed
        private void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (!_config.HackableCrate.Enabled) return;
            if (crate == null || player == null) return;
            if (SkipEventOwnership(player)) return;

            // Set OwnerID directly on the crate so storage protection handles it automatically
            crate.OwnerID = player.userID;

            // Also register in _entityOwnership for the lock timer and contributor tracking
            var uid = crate.net.ID.Value;
            _entityOwnership[uid] = new OwnershipData
            {
                OwnerId = player.userID,
                OwnerName = player.displayName,
                LockedAt = DateTime.UtcNow,
                LockDurationSeconds = _config.HackableCrate.LockForSeconds,
                EntityMaxHealth = 1f
            };

            if (player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null)
                    foreach (var memberId in team.members)
                        _entityOwnership[uid].Contributors.Add(memberId);
            }

            player.ChatMessage($"<color=#88D66C>Hackable crate locked to you for {_config.HackableCrate.LockForSeconds / 60} minutes.</color>");
        }

        // Fired when a supply signal is thrown — track signal item UID -> thrower
        private void OnExplosiveDropped(BaseEntity entity, Item item, BasePlayer player)
        {
            if (!_config.SupplyDrop.Enabled) return;
            if (item?.info?.shortname != "supply.signal") return;
            if (player == null || entity == null) return;
            if (SkipEventOwnership(player)) return;
            _pendingSupplyOwners[entity.net.ID.Value] = player.userID;
        }

        // Fired when an NPC corpse spawns — transfer ownership from the NPC death position
        private void OnEntitySpawned(LootableCorpse corpse)
        {
            if (corpse == null || corpse.playerSteamID.IsSteamId()) return; // skip player corpses
            if (SkipEventOwnership()) return;

            // Find the closest recorded NPC death position within 1m
            Vector3 corpsePos = corpse.transform.position;
            Vector3 bestMatch = default;
            float bestDist = float.MaxValue;

            foreach (var pos in _npcDeathPositions.Keys)
            {
                float dist = Vector3.Distance(pos, corpsePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestMatch = pos;
                }
            }

            if (bestDist > 1f || bestMatch == default) return; // no close match

            var data = _npcDeathPositions[bestMatch];
            _npcDeathPositions.Remove(bestMatch);

            var uid = corpse.net.ID.Value;
            _entityOwnership[uid] = new OwnershipData
            {
                OwnerId = data.OwnerId,
                OwnerName = data.OwnerName,
                Contributors = new HashSet<ulong>(data.Contributors),
                LockedAt = data.LockedAt,
                LockDurationSeconds = data.LockDurationSeconds,
                EntityMaxHealth = 1f
            };
        }

        // Fired when a CH47 spawns — mark all CH47s immune until they drop cargo
        // We can't reliably detect Bradley-drop CH47s at spawn time so we protect all of them;
        // regular patrol/transport CH47s don't carry lootable cargo so there's no downside
        private void OnEntitySpawned(CH47Helicopter ch47)
        {
            if (ch47 == null) return;
            _immuneCh47s.Add(ch47.net.ID.Value);
        }

        // Fired when a CH47 drops its cargo — lift immunity so it can be engaged normally
        private void OnCh47DropCargo(CH47Helicopter ch47, BaseEntity cargo)
        {
            if (ch47 == null) return;
            _immuneCh47s.Remove(ch47.net.ID.Value);
        }

        private void OnSupplyDropLanded(SupplyDrop drop)
        {
            if (!_config.SupplyDrop.Enabled) return;
            if (drop == null) return;
            if (SkipEventOwnership()) return;

            // Match this drop to the signal entity closest to the drop position
            // that still has a pending owner — find the signal by proximity
            ulong ownerId = 0;
            ulong matchedSignalId = 0;
            float closestDist = float.MaxValue;

            foreach (var kvp in _pendingSupplyOwners)
            {
                // The signal entity may already be destroyed; use position fallback
                var signalEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(kvp.Key)) as BaseEntity;
                if (signalEntity == null) continue;
                float dist = Vector3.Distance(signalEntity.transform.position, drop.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    ownerId = kvp.Value;
                    matchedSignalId = kvp.Key;
                }
            }

            // Fallback: if all signal entities are gone (normal — they despawn), take any pending owner
            if (ownerId == 0 && _pendingSupplyOwners.Count > 0)
            {
                foreach (var kvp in _pendingSupplyOwners)
                {
                    ownerId = kvp.Value;
                    matchedSignalId = kvp.Key;
                    break;
                }
            }

            if (ownerId == 0) return;

            _pendingSupplyOwners.Remove(matchedSignalId);

            var uid = drop.net.ID.Value;
            var owner = BasePlayer.FindByID(ownerId);

            _entityOwnership[uid] = new OwnershipData
            {
                OwnerId = ownerId,
                OwnerName = owner?.displayName ?? "Unknown",
                LockedAt = DateTime.UtcNow,
                LockDurationSeconds = _config.SupplyDrop.LockForSeconds,
                EntityMaxHealth = 1f
            };

            if (owner != null && owner.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(owner.currentTeam);
                if (team != null)
                    foreach (var memberId in team.members)
                        _entityOwnership[uid].Contributors.Add(memberId);
            }
        }

        #endregion

        #region TC Authorization Protection

        // Prevents strangers from authorizing themselves on your tool cupboard
        private object OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (privilege == null || player == null) return null;
            if (IsWeekendPvp(player)) return null;
            if (player.IsAdmin) return null;

            // Allow TC auth inside RaidableBases zones
            if (IsRaidableBase(privilege.transform.position)) return null;

            var ownerId = privilege.OwnerID;
            if (ownerId == 0) return null;           // unowned TC, allow
            if (ownerId == player.userID) return null; // it's their TC

            // Team check
            if (player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(ownerId)) return null;
            }

            // Already authed players can re-auth (refresh)
            if (privilege.IsAuthed(player)) return null;

            if (_config.ChatAlerts.TCAuthBlocked)
                player.ChatMessage("<color=#A04A4A>You are not authorized to use this tool cupboard.</color>");
            return false;
        }

        #endregion

        #region Vending Machine Protection

        // Protects the admin/back panel of vending machines from unauthorized editing
        // Buying (PlayerInfront) is always allowed — handled by IsVendingOpen in CanLootEntity
        private object CanAdministerVending(BasePlayer player, VendingMachine machine)
        {
            if (machine == null || player == null) return null;
            if (IsWeekendPvp(player)) return null;
            if (player.IsAdmin) return null;

            // Owner can always manage their own machine
            if (machine.OwnerID == player.userID) return null;
            if (machine.OwnerID == 0) return null;

            // Team check against the machine owner
            if (player.currentTeam != 0)
            {
                var ownerPlayer = BasePlayer.FindByID(machine.OwnerID);
                if (ownerPlayer != null && AreTeammates(player, ownerPlayer)) return null;

                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(machine.OwnerID)) return null;
            }

            // TC auth check
            var privs = new List<BuildingPrivlidge>();
            Vis.Entities(machine.transform.position, 16f, privs);
            foreach (var priv in privs)
                if (IsAuthorizedOrTeammate(player, priv)) return null;

            if (_config.ChatAlerts.VendingAdminBlocked)
                player.ChatMessage("<color=#A04A4A>You cannot administer this vending machine.</color>");
            return false;
        }

        #endregion

        #region Pickup Protection

        // Prevents picking up entities the player doesn't own or have TC auth for
        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if (entity == null || player == null) return null;
            if (IsWeekendPvp(player)) return null;
            if (player.IsAdmin) return null;

            // Exclude vehicles — fuel/storage interaction should not be blocked by pickup protection
            if (entity is BaseVehicle || entity is BaseVehicleModule) return null;

            // Skip protection inside RaidableBases zones
            if (IsRaidableBase(entity.transform.position)) return null;

            var ownerId = entity.OwnerID;
            if (ownerId == 0) return null;          // unowned, allow
            if (ownerId == player.userID) return null; // their own

            // Team check
            if (player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(ownerId)) return null;
            }

            // TC auth check
            var privs = new List<BuildingPrivlidge>();
            Vis.Entities(entity.transform.position, 16f, privs);
            foreach (var priv in privs)
                if (IsAuthorizedOrTeammate(player, priv)) return null;

            if (_config.ChatAlerts.PickupBlocked)
                player.ChatMessage("<color=#A04A4A>You cannot pick up what isn't yours.</color>");
            return false;
        }

        #endregion

        #region Oven Protection

        private object CanToggle(BaseOven oven, BasePlayer player)
        {
            if (oven == null || player == null) return null;
            if (IsWeekendPvp(player)) return null;
            if (player.IsAdmin) return null;

            var privs = new List<BuildingPrivlidge>();
            Vis.Entities(oven.transform.position, 16f, privs);
            if (privs.Count == 0) return null;

            foreach (var priv in privs)
            {
                if (IsAuthorizedOrTeammate(player, priv))
                    return null;
            }

            if (_config.ChatAlerts.OvenToggleBlocked)
                player.ChatMessage("<color=#A04A4A>You cannot toggle this oven.</color>");
            return false;
        }

        #endregion

        #region Helpers

        // Returns true if the player is standing in front of the vending machine (buying side)
        private bool IsVendingOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is VendingMachine vm)
                return vm.PlayerInfront(player);
            return false;
        }

        // Returns true if the player is standing in front of a drop box (depositing side)
        private bool IsDropBoxOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is DropBox db)
                return db.PlayerInfront(player);
            return false;
        }

        // Returns true if the position is inside an active RaidableBases zone
        private bool IsRaidableBase(Vector3 position)
        {
            if (RaidableBases == null) return false;
            return RaidableBases.Call<bool>("EventTerritory", position);
        }

        private bool AreTeammates(BasePlayer a, BasePlayer b)
        {
            if (a.currentTeam == 0) return false;
            return a.currentTeam == b.currentTeam;
        }

        private bool IsAuthorizedOrTeammate(BasePlayer player, BuildingPrivlidge priv)
        {
            // Direct TC auth only — being a teammate of someone who is authed is NOT sufficient
            return priv.IsAuthed(player);
        }

        private float GetDamageThreshold(BaseCombatEntity entity)
        {
            if (entity is BradleyAPC) return _config.Bradley.DamageLockThreshold;
            if (entity is PatrolHelicopter) return _config.Helicopter.DamageLockThreshold;
            return _config.NPC.DamageLockThreshold;
        }

        private int GetLockDuration(BaseCombatEntity entity)
        {
            if (entity is BradleyAPC) return _config.Bradley.LockForSeconds;
            if (entity is PatrolHelicopter) return _config.Helicopter.LockForSeconds;
            return _config.NPC.LockForSeconds;
        }

        private float GetTooHotSeconds(BaseCombatEntity entity)
        {
            if (entity is BradleyAPC) return _config.Bradley.TooHotSeconds;
            if (entity is PatrolHelicopter) return _config.Helicopter.TooHotSeconds;
            return 0f;
        }

        #endregion

        #region Cleanup

        private void OnServerSave()
        {
            var expired = new List<ulong>();
            foreach (var kvp in _entityOwnership)
                if (kvp.Value.IsExpired)
                    expired.Add(kvp.Key);

            foreach (var uid in expired)
            {
                _entityOwnership.Remove(uid);
                _shareData.Remove(uid); // FIX 2: clean up shares for expired ownership records
            }

            // Also clear stale NPC death positions older than 10 seconds
            var stalePositions = new List<Vector3>();
            foreach (var kvp in _npcDeathPositions)
                if ((DateTime.UtcNow - kvp.Value.LockedAt).TotalSeconds > 10)
                    stalePositions.Add(kvp.Key);
            foreach (var pos in stalePositions)
                _npcDeathPositions.Remove(pos);

            Puts($"[UnifiedLootProtection] Cleaned {expired.Count} expired ownership records. Active: {_entityOwnership.Count}");
            SaveShareData();
        }

        // Called when a new save file is created — i.e. a wipe happened
        private void OnNewSave(string filename)
        {
            _shareData.Clear();
            _entityOwnership.Clear();
            SaveShareData();
            Puts("[UnifiedLootProtection] Wipe detected — cleared all share and ownership data.");
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity?.net == null) return;
            var uid = entity.net.ID.Value;
            _entityOwnership.Remove(uid);
            _shareData.Remove(uid);           // FIX 1: clean up shares when entity is destroyed
            _immuneCh47s.Remove(uid);         // clean up CH47 immunity if it dies mid-flight
            _pendingSupplyOwners.Remove(uid); // clean up any signal that was destroyed before landing
        }

        #endregion

        #region Commands

        [ChatCommand("loot")]
        private void CmdLoot(BasePlayer player, string command, string[] args)
        {
            var ray = player.eyes.HeadRay();
            if (!Physics.Raycast(ray, out var hit, 5f)) return;

            var entity = hit.GetEntity();
            if (entity == null)
            {
                player.ChatMessage("No entity found.");
                return;
            }

            var uid = entity.net.ID.Value;
            var msg = new System.Text.StringBuilder();
            msg.AppendLine($"<color=#88D66C>Entity:</color> {entity.ShortPrefabName}");

            if (_entityOwnership.TryGetValue(uid, out var data) && data.OwnerId != 0)
            {
                var remaining = data.LockDurationSeconds > 0
                    ? $"{Math.Max(0, data.LockDurationSeconds - (DateTime.UtcNow - data.LockedAt).TotalSeconds):F0}s remaining"
                    : "no expiry";
                msg.AppendLine($"<color=#88D66C>Owner:</color> {data.OwnerName}");
                msg.AppendLine($"<color=#88D66C>Lock:</color> {remaining}");
                msg.AppendLine($"<color=#88D66C>Contributors:</color> {data.Contributors.Count}");
                msg.AppendLine($"<color=#88D66C>Too Hot:</color> {data.IsTooHot}");
            }
            else
            {
                msg.AppendLine("<color=#88D66C>Ownership:</color> TC-protected (no damage lock)");
            }

            var privs = new List<BuildingPrivlidge>();
            Vis.Entities(entity.transform.position, 16f, privs);
            if (privs.Count > 0)
            {
                msg.AppendLine($"<color=#88D66C>TC Coverage:</color> {privs.Count} cupboard(s)");
                msg.AppendLine($"<color=#88D66C>You are authed:</color> {privs[0].IsAuthed(player)}");
            }
            else
            {
                msg.AppendLine("<color=#88D66C>TC Coverage:</color> None (publicly accessible)");
            }

            if (_shareData.TryGetValue(uid, out var shared) && shared.Count > 0)
            {
                var names = new List<string>();
                foreach (var sid in shared)
                {
                    var p = BasePlayer.FindByID(sid);
                    names.Add(p != null ? p.displayName : $"[offline:{sid}]");
                }
                msg.AppendLine($"<color=#88D66C>Shared with:</color> {string.Join(", ", names)}");
            }
            else
            {
                msg.AppendLine("<color=#88D66C>Shared with:</color> Nobody");
            }

            player.ChatMessage(msg.ToString().TrimEnd());
        }

        [ChatCommand("share")]
        private void CmdShare(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                player.ChatMessage(
                    "<color=#88D66C>Look at a container and use:</color>\n" +
                    "<color=#88D66C>/share <playername></color> — Share this container\n" +
                    "<color=#88D66C>/share remove <playername></color> — Remove their access\n" +
                    "<color=#88D66C>/share list</color> — See who has access to this container\n" +
                    "<color=#88D66C>/share clear</color> — Remove all shares from this container");
                return;
            }

            var entity = GetLookedAtEntity(player);
            if (entity == null)
            {
                player.ChatMessage("<color=#A04A4A>You must be looking at a container to use this command.</color>");
                return;
            }

            // FIX 4: contributors can share, not just the TC owner
            if (!PlayerCanShare(player, entity))
            {
                player.ChatMessage("<color=#A04A4A>You do not have permission to manage sharing on this container.</color>");
                return;
            }

            var entityId = entity.net.ID.Value;
            var entityName = entity.ShortPrefabName;

            if (args[0].ToLower() == "list")
            {
                if (!_shareData.TryGetValue(entityId, out var sharedWith) || sharedWith.Count == 0)
                {
                    player.ChatMessage($"No one has been shared on this {entityName}.");
                    return;
                }
                var names = new List<string>();
                foreach (var uid in sharedWith)
                {
                    var p = BasePlayer.FindByID(uid);
                    if (p != null)
                        names.Add(p.displayName);
                    else if (_knownPlayers.TryGetValue(uid, out var knownName))
                        names.Add($"{knownName} [offline]");
                    else
                        names.Add($"[unknown:{uid}]");
                }
                player.ChatMessage($"<color=#88D66C>{entityName} shared with:</color> {string.Join(", ", names)}");
                return;
            }

            if (args[0].ToLower() == "clear")
            {
                _shareData.Remove(entityId);
                SaveShareData();
                player.ChatMessage($"<color=#88D66C>Cleared all shares from this {entityName}.</color>");
                return;
            }

            if (args[0].ToLower() == "remove" && args.Length >= 2)
            {
                var removeInput = string.Join(" ", args, 1, args.Length - 1);
                ulong removeId = 0;
                string removeName = null;
                BasePlayer removeOnline = null;

                if (ulong.TryParse(removeInput, out ulong parsedRemoveId) && parsedRemoveId > 70000000000000000UL)
                {
                    removeId = parsedRemoveId;
                    removeOnline = BasePlayer.FindByID(removeId);
                    removeName = removeOnline?.displayName
                        ?? (_knownPlayers.TryGetValue(removeId, out var kn) ? kn : $"[unknown:{removeId}]");
                }
                else
                {
                    removeOnline = FindPlayerByName(removeInput);
                    if (removeOnline == null)
                    {
                        player.ChatMessage($"<color=#A04A4A>Player '{removeInput}' not found. Use SteamID to remove offline players.</color>");
                        return;
                    }
                    removeId = removeOnline.userID;
                    removeName = removeOnline.displayName;
                }

                if (_shareData.TryGetValue(entityId, out var sharedWith) && sharedWith.Remove(removeId))
                {
                    SaveShareData();
                    player.ChatMessage($"<color=#88D66C>Removed {removeName}'s access to this {entityName}.</color>");
                    removeOnline?.ChatMessage($"<color=#A04A4A>{player.displayName} removed your access to their {entityName}.</color>");
                }
                else
                {
                    player.ChatMessage($"{removeName} did not have access to this container.");
                }
                return;
            }

            // /share <steamid or playername>
            var input = string.Join(" ", args);
            ulong targetId = 0;
            string targetName = null;
            BasePlayer onlineTarget = null;

            // Try SteamID first
            if (ulong.TryParse(input, out ulong parsedId) && parsedId > 70000000000000000UL)
            {
                targetId = parsedId;
                onlineTarget = BasePlayer.FindByID(targetId);
                if (onlineTarget != null)
                    targetName = onlineTarget.displayName;
                else if (_knownPlayers.TryGetValue(targetId, out var kn))
                    targetName = kn;
                else
                    targetName = $"[unknown:{targetId}]";
            }
            else
            {
                // Fall back to name search (online only)
                onlineTarget = FindPlayerByName(input);
                if (onlineTarget == null)
                {
                    player.ChatMessage($"<color=#A04A4A>Player not found. Use their SteamID to share with offline players.</color>");
                    return;
                }
                targetId = onlineTarget.userID;
                targetName = onlineTarget.displayName;
            }

            if (targetId == player.userID)
            {
                player.ChatMessage("You cannot share with yourself.");
                return;
            }

            if (!_shareData.ContainsKey(entityId))
                _shareData[entityId] = new HashSet<ulong>();

            if (_shareData[entityId].Contains(targetId))
            {
                player.ChatMessage($"{targetName} already has access to this container.");
                return;
            }

            _shareData[entityId].Add(targetId);
            SaveShareData();
            player.ChatMessage($"<color=#88D66C>Shared this {entityName} with {targetName}.</color>");
            onlineTarget?.ChatMessage($"<color=#88D66C>{player.displayName} has shared a {entityName} with you.</color>");
        }

        // FIX 4: bumped raycast from 3m to 5m to match loot interaction range
        private BaseEntity GetLookedAtEntity(BasePlayer player)
        {
            var ray = player.eyes.HeadRay();
            if (!Physics.Raycast(ray, out var hit, 5f)) return null;
            return hit.GetEntity();
        }

        private bool PlayerCanShare(BasePlayer player, BaseEntity entity)
        {
            var uid = entity.net.ID.Value;

            // FIX 4: allow contributors (not just OwnerId) to share
            if (_entityOwnership.TryGetValue(uid, out var data) && data.CanAccess(player.userID))
                return true;

            // TC auth covers storage containers
            var privs = new List<BuildingPrivlidge>();
            Vis.Entities(entity.transform.position, 16f, privs);
            foreach (var priv in privs)
                if (priv.IsAuthed(player)) return true;

            return false;
        }

        [ChatCommand("ulp")]
        private void CmdUlp(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                player.ChatMessage("<color=#A04A4A>You don't have permission to use this command.</color>");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage(
                    "<color=#88D66C>/ulp unlock</color> — Remove ownership lock from entity you're looking at\n" +
                    "<color=#88D66C>/ulp info</color> — Show full ownership info for entity you're looking at\n" +
                    "<color=#88D66C>/ulp weekend</color> — Show TruePVE mapping and weekend bypass status");
                return;
            }

            if (args[0].Equals("weekend", StringComparison.OrdinalIgnoreCase))
            {
                string mapping = GetTruePveMapping(player) ?? "(none / unavailable)";
                bool clock = IsEasternWeekendWindow();
                bool bypass = IsWeekendPvp(player);
                player.ChatMessage($"<color=#B084FF>Nightshade PvP Weekend</color> | TruePVE mapping: {mapping} | Eastern weekend window: {clock} | ULP access bypass: {bypass} | Event ownership bypass: {bypass && _config.WeekendPvP.DisableEventOwnershipLocks}");
                return;
            }

            var entity = GetLookedAtEntity(player);
            if (entity == null)
            {
                player.ChatMessage("<color=#A04A4A>No entity found. Look directly at something within 5 m.</color>");
                return;
            }

            var uid = entity.net.ID.Value;

            if (args[0].ToLower() == "unlock")
            {
                if (!_entityOwnership.TryGetValue(uid, out var data) || data.OwnerId == 0)
                {
                    player.ChatMessage("<color=#A04A4A>This entity has no ownership lock.</color>");
                    return;
                }

                var prevOwner = data.OwnerName;
                _entityOwnership.Remove(uid);
                _shareData.Remove(uid);
                player.ChatMessage($"<color=#88D66C>Unlocked {entity.ShortPrefabName}. Previous owner: {prevOwner}.</color>");
                return;
            }

            if (args[0].ToLower() == "info")
            {
                var msg = new System.Text.StringBuilder();
                msg.AppendLine($"<color=#88D66C>Entity:</color> {entity.ShortPrefabName} (uid: {uid})");

                if (_entityOwnership.TryGetValue(uid, out var data) && data.OwnerId != 0)
                {
                    var remaining = data.LockDurationSeconds > 0
                        ? $"{Math.Max(0, data.LockDurationSeconds - (DateTime.UtcNow - data.LockedAt).TotalSeconds):F0}s remaining"
                        : "no expiry";
                    msg.AppendLine($"<color=#88D66C>Owner:</color> {data.OwnerName} ({data.OwnerId})");
                    msg.AppendLine($"<color=#88D66C>Lock:</color> {remaining}");
                    msg.AppendLine($"<color=#88D66C>Contributors:</color> {data.Contributors.Count}");
                    msg.AppendLine($"<color=#88D66C>Too Hot:</color> {data.IsTooHot}");
                    msg.AppendLine($"<color=#88D66C>Expired:</color> {data.IsExpired}");
                }
                else
                {
                    msg.AppendLine("<color=#88D66C>Ownership:</color> None (TC-protected or public)");
                }

                var privs = new List<BuildingPrivlidge>();
                Vis.Entities(entity.transform.position, 16f, privs);
                msg.AppendLine(privs.Count > 0
                    ? $"<color=#88D66C>TC Coverage:</color> {privs.Count} cupboard(s), you authed: {privs[0].IsAuthed(player)}"
                    : "<color=#88D66C>TC Coverage:</color> None");

                if (_shareData.TryGetValue(uid, out var shared) && shared.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var sid in shared)
                    {
                        var p = BasePlayer.FindByID(sid);
                        if (p != null) names.Add(p.displayName);
                        else if (_knownPlayers.TryGetValue(sid, out var kn)) names.Add($"{kn} [offline]");
                        else names.Add($"[unknown:{sid}]");
                    }
                    msg.AppendLine($"<color=#88D66C>Shared with:</color> {string.Join(", ", names)}");
                }
                else
                {
                    msg.AppendLine("<color=#88D66C>Shared with:</color> Nobody");
                }

                player.ChatMessage(msg.ToString().TrimEnd());
                return;
            }

            player.ChatMessage("<color=#A04A4A>Unknown subcommand. Use /ulp for help.</color>");
        }

        private BasePlayer FindPlayerByName(string name)
        {
            name = name.ToLower();
            BasePlayer found = null;
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p.displayName.ToLower().Contains(name))
                {
                    if (found != null) return null;
                    found = p;
                }
            }
            return found;
        }

        #endregion
    }
}