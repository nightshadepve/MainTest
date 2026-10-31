using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Claim Vehicle", "WhiteThunder", "1.8.1")]
    [Description("Allows players to claim ownership of unowned vehicles.")]
    internal class ClaimVehicle : CovalencePlugin
    {
        #region Fields

        private Configuration _config;

        private const string Permission_Unclaim = "claimvehicle.unclaim";
        private const string Permission_NoClaimCooldown = "claimvehicle.nocooldown";
        private const string Permission_Claim_AllVehicles = "claimvehicle.claim.allvehicles";

        private readonly VehicleInfoManager _vehicleInfoManager;
        private CooldownManager _cooldownManager;

        public ClaimVehicle()
        {
            _vehicleInfoManager = new VehicleInfoManager(this);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            _config.Init(this);

            _cooldownManager = new CooldownManager(_config.ClaimCooldownSeconds);

            permission.RegisterPermission(Permission_Unclaim, this);
            permission.RegisterPermission(Permission_NoClaimCooldown, this);
            permission.RegisterPermission(Permission_Claim_AllVehicles, this);
        }

        private void OnServerInitialized()
        {
            _vehicleInfoManager.OnServerInitialized();
        }

        #endregion

        #region Exposed Hooks

        private static class ExposedHooks
        {
            public static object OnVehicleClaim(BasePlayer player, BaseCombatEntity vehicle)
            {
                return Interface.CallHook("OnVehicleClaim", player, vehicle);
            }

            public static object OnVehicleUnclaim(BasePlayer player, BaseCombatEntity vehicle)
            {
                return Interface.CallHook("OnVehicleUnclaim", player, vehicle);
            }

            public static void OnVehicleOwnershipChanged(BaseCombatEntity vehicle)
            {
                Interface.CallHook("OnVehicleOwnershipChanged", vehicle);
            }
        }

        #endregion

        #region Commands

        private void ClaimVehicleCommand(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            var basePlayer = player.Object as BasePlayer;

            if (!VerifySupportedVehicleFound(player, basePlayer, GetLookEntity(basePlayer), out var vehicle, out var vehicleInfo) ||
                !VerifyPermissionAny(player, Permission_Claim_AllVehicles, vehicleInfo.Permission) ||
                !VerifyVehicleIsNotDead(player, vehicle) ||
                !VerifyNotOwned(player, vehicle) ||
                !VerifyOffCooldown(player) ||
                !VerifyCanBuild(player) ||
                !VerifyNoLockRestriction(player, vehicle) ||
                !VerifyNotMounted(player, vehicle) ||
                ClaimWasBlocked(basePlayer, vehicle))
                return;

            ChangeVehicleOwnership(vehicle, basePlayer.userID);
            _cooldownManager.UpdateLastUsedForPlayer(basePlayer.userID);
            ReplyToPlayer(player, "Claim.Success");
        }

        private void UnclaimVehicleCommand(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer || !VerifyPermissionAny(player, Permission_Unclaim))
                return;

            var basePlayer = player.Object as BasePlayer;

            if (!VerifySupportedVehicleFound(player, basePlayer, GetLookEntity(basePlayer), out var vehicle, out _) ||
                !VerifyCurrentlyOwned(player, vehicle) ||
                UnclaimWasBlocked(basePlayer, vehicle))
                return;

            ChangeVehicleOwnership(vehicle, 0);
            ReplyToPlayer(player, "Unclaim.Success");
        }

        #endregion

        #region Helper Methods

        private static bool ClaimWasBlocked(BasePlayer player, BaseCombatEntity vehicle)
        {
            return ExposedHooks.OnVehicleClaim(player, vehicle) is false;
        }

        private static bool UnclaimWasBlocked(BasePlayer player, BaseCombatEntity vehicle)
        {
            return ExposedHooks.OnVehicleUnclaim(player, vehicle) is false;
        }

        private static RidableHorse GetClosestHorse(HitchTrough hitchTrough, BasePlayer player)
        {
            var closestDistance = float.MaxValue;
            RidableHorse closestHorse = null;

            foreach (var hitchSpot in hitchTrough.hitchSpots)
            {
                if (!hitchSpot.IsOccupied())
                    continue;

                var distance = Vector3.Distance(player.transform.position, hitchSpot.tr.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    if (hitchSpot.hitchableEntRef.Get(serverside: true) is RidableHorse ridableHorse)
                    {
                        closestHorse = ridableHorse;
                    }
                }
            }

            return closestHorse;
        }

        private static BaseEntity GetLookEntity(BasePlayer player, float maxDistance = 9)
        {
            return Physics.Raycast(player.eyes.HeadRay(), out var hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.GetEntity()
                : null;
        }

        private static void ChangeVehicleOwnership(BaseCombatEntity vehicle, ulong userId)
        {
            vehicle.OwnerID = userId;
            ExposedHooks.OnVehicleOwnershipChanged(vehicle);
        }

        private static string FormatDuration(double seconds)
        {
            return TimeSpan.FromSeconds(Math.Ceiling(seconds)).ToString("g");
        }

        private static string[] FindPrefabsOfType<T>() where T : BaseEntity
        {
            var prefabList = new List<string>();

            foreach (var assetPath in GameManifest.Current.entities)
            {
                var entity = GameManager.server.FindPrefab(assetPath)?.GetComponent<T>();
                if (entity == null)
                    continue;

                prefabList.Add(entity.PrefabName);
            }

            return prefabList.ToArray();
        }

        private static BaseCombatEntity GetAppropriateVehicle(BaseEntity entity, BasePlayer player)
        {
            var vehicleModule = entity as BaseVehicleModule;
            if ((object)vehicleModule != null)
                return vehicleModule.Vehicle;

            var carLift = entity as ModularCarGarage;
            if ((object)carLift != null)
                return carLift.carOccupant;

            var hitchTrough = entity as HitchTrough;
            if ((object)hitchTrough != null)
                return GetClosestHorse(hitchTrough, player);

            return entity as BaseCombatEntity;
        }

        private bool VerifySupportedVehicleFound(IPlayer player, BasePlayer basePlayer, BaseEntity entity, out BaseCombatEntity vehicle, out IVehicleInfo vehicleInfo)
        {
            vehicle = GetAppropriateVehicle(entity, basePlayer);
            if ((object)vehicle != null)
            {
                vehicleInfo = _vehicleInfoManager.GetVehicleInfo(vehicle);
                if (vehicleInfo != null)
                    return true;
            }

            vehicleInfo = null;
            ReplyToPlayer(player, "Generic.Error.NoSupportedVehicleFound");
            return false;
        }

        private bool VerifyPermissionAny(IPlayer player, params string[] permissionNames)
        {
            foreach (var perm in permissionNames)
            {
                if (permission.UserHasPermission(player.Id, perm))
                    return true;
            }

            ReplyToPlayer(player, "Generic.Error.NoPermission");
            return false;
        }

        private bool VerifyVehicleIsNotDead(IPlayer player, BaseCombatEntity vehicle)
        {
            if (!vehicle.IsDead())
                return true;

            ReplyToPlayer(player, "Generic.Error.VehicleDead");
            return false;
        }

        private bool VerifyNotOwned(IPlayer player, BaseEntity vehicle)
        {
            if (vehicle.OwnerID == 0)
                return true;

            var basePlayer = player.Object as BasePlayer;
            if (vehicle.OwnerID == basePlayer.userID)
            {
                ReplyToPlayer(player, "Claim.Error.AlreadyOwnedByYou");
            }
            else
            {
                ReplyToPlayer(player, "Claim.Error.DifferentOwner");
            }

            return false;
        }

        private bool VerifyOffCooldown(IPlayer player)
        {
            if (player.HasPermission(Permission_NoClaimCooldown))
                return true;

            var basePlayer = player.Object as BasePlayer;
            var secondsRemaining = _cooldownManager.GetSecondsRemaining(basePlayer.userID);
            if (secondsRemaining > 0)
            {
                ReplyToPlayer(player, "Generic.Error.Cooldown", FormatDuration(secondsRemaining));
                return false;
            }

            return true;
        }

        private bool VerifyCanBuild(IPlayer player)
        {
            if ((player.Object as BasePlayer).CanBuild())
                return true;

            ReplyToPlayer(player, "Generic.Error.BuildingBlocked");
            return false;
        }

        private bool VerifyNoLockRestriction(IPlayer player, BaseCombatEntity vehicle)
        {
            var basePlayer = player.Object as BasePlayer;
            var baseLock = vehicle.GetSlot(BaseEntity.Slot.Lock);
            if (baseLock == null || baseLock.OwnerID == basePlayer.userID)
                return true;

            ReplyToPlayer(player, "Claim.Error.LockedByAnother");
            return false;
        }

        private bool VerifyNotMounted(IPlayer player, BaseCombatEntity entity)
        {
            var vehicle = entity as BaseVehicle;
            if (vehicle == null || !vehicle.AnyMounted())
                return true;

            ReplyToPlayer(player, "Claim.Error.Mounted");
            return false;
        }

        private bool VerifyCurrentlyOwned(IPlayer player, BaseCombatEntity vehicle)
        {
            var basePlayer = player.Object as BasePlayer;
            if (vehicle.OwnerID == basePlayer.userID)
                return true;

            ReplyToPlayer(player, "Unclaim.Error.NotOwned");
            return false;
        }

        #endregion

        #region Helper Classes

        private class CooldownManager
        {
            private readonly Dictionary<ulong, float> _cooldownMap = new();
            private readonly float _cooldownDuration;

            public CooldownManager(float duration)
            {
                _cooldownDuration = duration;
            }

            public void UpdateLastUsedForPlayer(ulong userId)
            {
                _cooldownMap[userId] = Time.realtimeSinceStartup;
            }

            public float GetSecondsRemaining(ulong userId)
            {
                return _cooldownMap.TryGetValue(userId, out var duration)
                    ? duration + _cooldownDuration - Time.realtimeSinceStartup
                    : 0;
            }
        }

        private interface IVehicleInfo
        {
            uint[] PrefabIds { get; }
            string Permission { get; }

            void OnServerInitialized(ClaimVehicle plugin);
            bool IsCorrectType(BaseEntity entity);
        }

        private class VehicleInfo<T> : IVehicleInfo where T : BaseEntity
        {
            public uint[] PrefabIds { get; private set; }
            public string Permission { get; private set; }

            public string VehicleName { get; set; }
            public string[] PrefabPaths { get; set; }

            public void OnServerInitialized(ClaimVehicle plugin)
            {
                Permission = $"{nameof(ClaimVehicle)}.claim.{VehicleName}".ToLower();
                plugin.permission.RegisterPermission(Permission, plugin);

                var prefabIds = new List<uint>(PrefabPaths.Length);

                foreach (var prefabName in PrefabPaths)
                {
                    var prefab = GameManager.server.FindPrefab(prefabName)?.GetComponent<T>();
                    if (prefab == null)
                    {
                        plugin.LogError($"Invalid or incorrect prefab. Please alert the plugin maintainer -- {prefabName}");
                        continue;
                    }

                    prefabIds.Add(prefab.prefabID);
                }

                PrefabIds = prefabIds.ToArray();
            }

            public bool IsCorrectType(BaseEntity entity)
            {
                return entity is T;
            }
        }

        private class VehicleInfoManager
        {
            private readonly ClaimVehicle _plugin;
            private readonly Dictionary<uint, IVehicleInfo> _prefabIdToVehicleInfo = new();
            private IVehicleInfo[] _allVehicles;

            public VehicleInfoManager(ClaimVehicle plugin)
            {
                _plugin = plugin;
            }

            public void OnServerInitialized()
            {
                _allVehicles = new IVehicleInfo[]
                {
                    new VehicleInfo<AttackHelicopter>
                    {
                        VehicleName = "attackhelicopter",
                        PrefabPaths = new[] { "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab" },
                    },
                    new VehicleInfo<Ballista>
                    {
                        VehicleName = "ballista",
                        PrefabPaths = new[] { "assets/content/vehicles/siegeweapons/ballista/ballista.entity.prefab" },
                    },
                    new VehicleInfo<BatteringRam>
                    {
                        VehicleName = "batteringram",
                        PrefabPaths = new[] { "assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab" },
                    },
                    new VehicleInfo<TrainCar>
                    {
                        VehicleName = "caboose",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/caboose/traincaboose.entity.prefab" },
                    },
                    new VehicleInfo<Catapult>
                    {
                        VehicleName = "catapult",
                        PrefabPaths = new[] { "assets/content/vehicles/siegeweapons/catapult/catapult.entity.prefab" },
                    },
                    new VehicleInfo<CH47Helicopter>
                    {
                        VehicleName = "chinook",
                        PrefabPaths = new[] { "assets/prefabs/npc/ch47/ch47.entity.prefab" },
                    },
                    new VehicleInfo<SubmarineDuo>
                    {
                        VehicleName = "duosub",
                        PrefabPaths = new[] { "assets/content/vehicles/submarine/submarineduo.entity.prefab" },
                    },
                    new VehicleInfo<HotAirBalloon>
                    {
                        VehicleName = "hotairballoon",
                        PrefabPaths = new[] { "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab" },
                    },
                    new VehicleInfo<TrainEngine>
                    {
                        VehicleName = "locomotive",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab" },
                    },
                    new VehicleInfo<Minicopter>
                    {
                        VehicleName = "minicopter",
                        PrefabPaths = new[] { "assets/content/vehicles/minicopter/minicopter.entity.prefab" },
                    },
                    new VehicleInfo<ModularCar>
                    {
                        VehicleName = "modularcar",
                        PrefabPaths = FindPrefabsOfType<ModularCar>(),
                    },
                    new VehicleInfo<Bike>
                    {
                        VehicleName = "motorbike.sidecar",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/motorbike_sidecar.prefab" },
                    },
                    new VehicleInfo<Bike>
                    {
                        VehicleName = "motorbike",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/motorbike.prefab" },
                    },
                    new VehicleInfo<Bike>
                    {
                        VehicleName = "pedalbike",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/pedalbike.prefab" },
                    },
                    new VehicleInfo<Bike>
                    {
                        VehicleName = "pedaltrike",
                        PrefabPaths = new[] { "assets/content/vehicles/bikes/pedaltrike.prefab" },
                    },
                    new VehicleInfo<RHIB>
                    {
                        VehicleName = "rhib",
                        PrefabPaths = new[] { "assets/content/vehicles/boats/rhib/rhib.prefab" },
                    },
                    new VehicleInfo<RidableHorse>
                    {
                        VehicleName = "ridablehorse",
                        PrefabPaths = new[] { "assets/content/vehicles/horse/ridablehorse.prefab" },
                    },
                    new VehicleInfo<MotorRowboat>
                    {
                        VehicleName = "rowboat",
                        PrefabPaths = new[] { "assets/content/vehicles/boats/rowboat/rowboat.prefab" },
                    },
                    new VehicleInfo<ScrapTransportHelicopter>
                    {
                        VehicleName = "scraptransporthelicopter",
                        PrefabPaths = new[] { "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab" },
                    },
                    new VehicleInfo<BasicCar>
                    {
                        VehicleName = "sedan",
                        PrefabPaths = new[] { "assets/content/vehicles/sedan_a/sedantest.entity.prefab" },
                    },
                    new VehicleInfo<TrainEngine>
                    {
                        VehicleName = "sedanrail",
                        PrefabPaths = new[] { "assets/content/vehicles/sedan_a/sedanrail.entity.prefab" },
                    },
                    new VehicleInfo<SiegeTower>
                    {
                        VehicleName = "siegetower",
                        PrefabPaths = new[] { "assets/content/vehicles/siegeweapons/siegetower/siegetower.entity.prefab" },
                    },
                    new VehicleInfo<Snowmobile>
                    {
                        VehicleName = "snowmobile",
                        PrefabPaths = new[] { "assets/content/vehicles/snowmobiles/snowmobile.prefab" },
                    },
                    new VehicleInfo<BaseSubmarine>
                    {
                        VehicleName = "solosub",
                        PrefabPaths = new[] { "assets/content/vehicles/submarine/submarinesolo.entity.prefab" },
                    },
                    new VehicleInfo<Snowmobile>
                    {
                        VehicleName = "tomaha",
                        PrefabPaths = new[] { "assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab" },
                    },
                    new VehicleInfo<TrainCar>
                    {
                        VehicleName = "wagona",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/wagons/trainwagona.entity.prefab" },
                    },
                    new VehicleInfo<TrainCar>
                    {
                        VehicleName = "wagonb",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab" },
                    },
                    new VehicleInfo<TrainCar>
                    {
                        VehicleName = "wagonc",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/wagons/trainwagonc.entity.prefab" },
                    },
                    new VehicleInfo<TrainEngine>
                    {
                        VehicleName = "workcart",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/workcart/workcart.entity.prefab" },
                    },
                    new VehicleInfo<TrainEngine>
                    {
                        VehicleName = "workcartaboveground",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/workcart/workcart_aboveground.entity.prefab" },
                    },
                    new VehicleInfo<TrainEngine>
                    {
                        VehicleName = "workcartcovered",
                        PrefabPaths = new[] { "assets/content/vehicles/trains/workcart/workcart_aboveground2.entity.prefab" },
                    },
                };

                foreach (var vehicleInfo in _allVehicles)
                {
                    vehicleInfo.OnServerInitialized(_plugin);

                    foreach (var prefabId in vehicleInfo.PrefabIds)
                    {
                        _prefabIdToVehicleInfo[prefabId] = vehicleInfo;
                    }
                }
            }

            public IVehicleInfo GetVehicleInfo(BaseEntity entity)
            {
                return _prefabIdToVehicleInfo.TryGetValue(entity.prefabID, out var vehicleInfo) && vehicleInfo.IsCorrectType(entity)
                    ? vehicleInfo
                    : null;
            }
        }

        #endregion

        #region Configuration

        [JsonObject(MemberSerialization.OptIn)]
        private class Configuration : SerializableConfiguration
        {
            [JsonProperty("ClaimCooldownSeconds")]
            private float DeprecatedClaimCooldownSeconds { set => ClaimCooldownSeconds = value; }

            [JsonProperty("Claim cooldown (seconds)")]
            public float ClaimCooldownSeconds = 3600;

            [JsonProperty("Claim commands")]
            public string[] ClaimCommands = { "vclaim" };

            [JsonProperty("Unclaim commands")]
            public string[] UnclaimCommands = { "vunclaim" };

            public void Init(ClaimVehicle plugin)
            {
                plugin.AddCovalenceCommand(ClaimCommands, nameof(ClaimVehicleCommand));
                plugin.AddCovalenceCommand(UnclaimCommands, nameof(UnclaimVehicleCommand));
            }
        }

        private Configuration GetDefaultConfig() => new();

        #region Configuration Helpers

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var currentDictValue = currentRawValue as Dictionary<string, object>;
                    if (currentWithDefaults[key] is Dictionary<string, object> defaultDictValue)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                        {
                            changed = true;
                        }
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #endregion

        #region Localization

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(string.Format(GetMessage(player, messageName), args));

        private string GetMessage(IPlayer player, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, player.Id);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Generic.Error.NoPermission"] = "You don't have permission to do that.",
                ["Generic.Error.BuildingBlocked"] = "Error: Cannot do that while building blocked.",
                ["Generic.Error.NoSupportedVehicleFound"] = "Error: No supported vehicle found.",
                ["Generic.Error.VehicleDead"] = "Error: That vehicle is dead.",
                ["Generic.Error.Cooldown"] = "Please wait <color=red>{0}</color> and try again.",
                ["Claim.Error.AlreadyOwnedByYou"] = "You already own that vehicle.",
                ["Claim.Error.DifferentOwner"] = "Error: Someone else already owns that vehicle.",
                ["Claim.Error.LockedByAnother"] = "Error: Someone else placed a lock on that vehicle.",
                ["Claim.Error.Mounted"] = "Error: That vehicle is currently occupied.",
                ["Claim.Success"] = "You now own that vehicle.",
                ["Unclaim.Error.NotOwned"] = "Error: You do not own that vehicle.",
                ["Unclaim.Success"] = "You no longer own that vehicle.",
            }, this, "en");
        }

        #endregion
    }
}
