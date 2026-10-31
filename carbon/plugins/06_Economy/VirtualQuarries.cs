using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using Facepunch;
using VLB;
#if CARBON
using Carbon.Base;
using Carbon.Modules;
#endif

namespace Oxide.Plugins
{
    [Info("VirtualQuarries", "ThePitereq", "2.5.7")]
    public class VirtualQuarries : RustPlugin
    {
        [PluginReference] private readonly Plugin ImageLibrary, PopUpAPI, ServerRewards, Economics, IQEconomic, BankSystem, ShoppyStock;
        
#if CARBON
        public ImageDatabaseModule imgDb = BaseModule.GetModule<ImageDatabaseModule>();
#endif
        private static bool validImages = false;

        private static VirtualQuarries _plugin;

        private readonly Dictionary<ulong, CachedSurvey> cachedSurveys = new();
        private readonly Dictionary<ulong, List<RequiredItem>> cachedRequirements = new();
        private readonly Dictionary<ulong, string> cachedSort = new();
        private readonly Dictionary<ulong, int> cachedPage = new();
        private readonly Dictionary<ulong, Dictionary<string, int>> cachedQuarryCount = new();
        private readonly Dictionary<ulong, string> cachedSurveyType = new();
        private readonly Dictionary<ulong, float> commandCooldowns = new();
        private static DateTime lastOperation = DateTime.Now;
        private readonly List<Vector3> giantExcPositions = new();

        private class CachedSurvey
        {
            public string profile = string.Empty;
            public readonly List<QuarryResource> resources = new();
        }

        #region Init

        private void Init()
        {
            _plugin = this;
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config);
            LoadData();
            LoadMessages();
        }

        private void OnServerInitialized()
        {
            if (config.economyPlugin == 1 && Economics == null)
                PrintWarning("Economics plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 2 && ServerRewards == null)
                PrintWarning("ServerRewards plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 3 && IQEconomic == null)
                PrintWarning("IQEconomic plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 4 && BankSystem == null)
                PrintWarning("BankSystem plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 5 && ShoppyStock == null)
                PrintWarning("ShoppyStock plugin not found! You will not be able to upgrade your quarries with server currency!");
#if CARBON
            validImages = true;
#else
			validImages = ImageLibrary != null;
			if (!validImages)
                PrintWarning("ImageLibrary plugin not found! It is required for all images to work!");
#endif
            if (PopUpAPI == null)
                PrintWarning("PopUpAPI plugin not found! The Pop-Up messages will not display!");
            else if (PopUpAPI.Version.Major < 2)
                PrintWarning("You are using old version of PopUpAPI! Update it to 2.0.0 or newer, or plugin will print errors or no pop-up messages at all!");
            if (config.surveyThrow)
                Unsubscribe(nameof(OnExplosiveThrown));
            AddImage("https://images.pvrust.eu/ui_icons/VirtualQuarries/arrow_right_0.png");
            AddImage("https://images.pvrust.eu/ui_icons/VirtualQuarries/arrow_left_0.png");
            AddImage("https://images.pvrust.eu/ui_icons/VirtualQuarries/shared_0.png");
            AddImage("https://images.pvrust.eu/ui_icons/VirtualQuarries/search_0.png");
            if (config.requirePermission)
                permission.RegisterPermission("virtualquarries.use", this);
            if (config.sharingRequirePermission)
                permission.RegisterPermission("virtualquarries.share", this);
            if (config.quarryPerm)
                permission.RegisterPermission("virtualquarries.static.quarry", this);
            if (config.pumpJackPerm)
                permission.RegisterPermission("virtualquarries.static.pumpjack", this);
            foreach (var permissionKey in config.permissions.Keys)
                permission.RegisterPermission(permissionKey, this);
            foreach (var permissionKey in config.surveys.Values)
                if (permissionKey.permission != string.Empty)
                    permission.RegisterPermission(permissionKey.permission, this);
            foreach (var permissionKey in config.quarryProfiles.Values)
            {
                if (permissionKey.permission != string.Empty)
                    permission.RegisterPermission(permissionKey.permission, this);
                foreach (var resourceKey in permissionKey.resources.Values)
                    if (resourceKey.permission != string.Empty)
                        permission.RegisterPermission(resourceKey.permission, this);
                foreach (var upgradeKey in permissionKey.upgrades)
                    if (upgradeKey.requiredPerm != string.Empty)
                        permission.RegisterPermission(upgradeKey.requiredPerm, this);
            }
            if (config.storeContainers)
                timer.Every(config.containerSaveInterval, () => SaveContainers());
            foreach (var command in config.commandList)
                cmd.AddChatCommand(command, this, nameof(VirtualQuarriesCommand));
            foreach (var profile in config.quarryProfiles.Values)
            {
                if (profile.icon.iconUrl != "")
                    AddImage(profile.icon.iconUrl);
                foreach (var resource in profile.resources)
                {
                    if (resource.Value.iconUrl != "")
                        AddImage(resource.Value.iconUrl);
                    foreach (var additional in resource.Value.additionalItems)
                        if (additional.iconUrl != "")
                            AddImage(additional.iconUrl);
                }
                foreach (var item in profile.requiredItems)
                    if (item.iconUrl != "")
                        AddImage(item.iconUrl);
                foreach (var upgrade in profile.upgrades)
                    foreach (var item in upgrade.requiredItems)
                        if (item.iconUrl != "")
                            AddImage(item.iconUrl);
            }
            foreach (var survey in config.surveys.Values)
                if (survey.surveyItem.iconUrl != "")
                    AddImage(survey.surveyItem.iconUrl);
            timer.Once(1f, () => SetupQuarries());
            foreach (var player in BasePlayer.activePlayerList)
            {
                playerCache.TryAdd(player.userID, player.displayName);
                playerCache[player.userID] = player.displayName;
                if (config.offlineQuarryOwnerKickTime == 0) continue;
                DateTime now = DateTime.Now;
                foreach (var quarry in data.quarries)
                {
                    if (quarry.Value.owner != player.userID) continue;
                    quarry.Value.lastOwnerOnline = now;
                }
            }
            if (config.offlineQuarryOwnerKickTime > 0)
                CheckQuarryKicks();
            foreach (var monument in TerrainMeta.Path.Landmarks)
            {
                if (!monument.displayPhrase.IsValid()) continue;
                if (monument.displayPhrase.english == "Giant Excavator Pit")
                    giantExcPositions.Add(monument.transform.position);
            }
        }

        private void SetupQuarries()
        {
            if (config.staticQuarries)
            {
                if (!config.disableRunningEffect)
                {
                    foreach (var quarry in BaseNetworkable.serverEntities.OfType<MiningQuarry>())
                    {
                        if (quarry.staticType == MiningQuarry.QuarryType.None && quarry.OwnerID != 0) continue;
                        quarry.SetOn(false);
                        quarry.CancelInvoke();
                        quarry.engineSwitchPrefab.instance.SetFlagLocal(BaseEntity.Flags.On, true);
                        using var update = quarry.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.On, true);
                    }
                }
                foreach (var quarry in data.staticQuarries.ToList())
                {
                    if (quarry.Key == uint.MaxValue) continue;
                    MiningQuarry quarryEnt = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Key)) as MiningQuarry;
                    if (!quarryEnt)
                    {
                        foreach (var user in quarry.Value)
                        {
                            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(user.Value)) as BoxStorage;
                            storage?.Kill();
                        }
                        data.staticQuarries.Remove(quarry.Key);
                        continue;
                    }
                    if (quarryEnt.staticType == MiningQuarry.QuarryType.None) continue;
                    foreach (var user in quarry.Value)
                    {
                        BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(user.Value)) as BoxStorage;
                        if (!storage)
                        {
                            Puts($"[VirtualQuarries ERROR] Static quarry of {user.Key} is missing output container!");
                            continue;
                        }
                        GroundWatch gw = storage.GetComponent<GroundWatch>();
                        if (gw) UnityEngine.Object.Destroy(gw);
                        DestroyOnGroundMissing dogm = storage.GetComponent<DestroyOnGroundMissing>();
                        if (dogm) UnityEngine.Object.Destroy(dogm);
                        if (storage.children.Count == 0)
                        {
                            Puts($"[VirtualQuarries ERROR] Static quarry of {user.Key} is missing fuel container!");
                            continue;
                        }
                        storage.inventory.capacity = config.staticResourceSize;
                        BoxStorage fuelStorage = storage.children[0] as BoxStorage;
                        if (fuelStorage)
                        {
                            gw = fuelStorage.GetComponent<GroundWatch>();
                            if (gw) UnityEngine.Object.Destroy(gw);
                            dogm = fuelStorage.GetComponent<DestroyOnGroundMissing>();
                            if (dogm) UnityEngine.Object.Destroy(dogm);
                            fuelStorage.inventory.capacity = config.staticFuelSize;
                        }
                        VirtualQuarry vQuarry = storage.GetComponent<VirtualQuarry>();
                        if (!vQuarry)
                        {
                            storage.gameObject.AddComponent<VirtualQuarry>();
                            vQuarry = storage.GetComponent<VirtualQuarry>();
                        }
                        vQuarry.SetupQuarry(VirtualQuarry.VqType.Static, linkedQuarry: quarryEnt);
                    }
                }
            }
            else
                Unsubscribe(nameof(OnQuarryToggled));
            if (config.excavatorQuarry)
            {
                if (config.excavatorResources.Count < 4)
                {
                    PrintWarning("You don't have all 4 resources required to run excavator properly! Generating new ones...");
                    config.excavatorResources = new Dictionary<string, List<StaticQuarryOutput>>()
                    {
                        { "Stone", new List<StaticQuarryOutput>() {
                            new StaticQuarryOutput()
                            {
                                shortname = "stones",
                                amount = 5000
                            }
                        } },
                        { "Metal", new List<StaticQuarryOutput>() {
                            new StaticQuarryOutput()
                            {
                                shortname = "metal.fragments",
                                amount = 2500
                            }
                        } },
                        { "Sulfur", new List<StaticQuarryOutput>() {
                            new StaticQuarryOutput()
                            {
                                shortname = "sulfur.ore",
                                amount = 1000
                            }
                        } },
                        { "HQM", new List<StaticQuarryOutput>() {
                            new StaticQuarryOutput()
                            {
                                shortname = "hq.metal.ore",
                                amount = 50
                            }
                        } }
                    };
                    Config.WriteObject(config);
                }
                foreach (var arm in BaseNetworkable.serverEntities.OfType<ExcavatorArm>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(arm.transform.position, pos) > 250) continue;
                        arm.StopMining();
                        using var update = arm.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.Reserved8, true);
                        update.Set(BaseEntity.Flags.On, true);
                        break;
                    }
                }
                foreach (var engin in BaseNetworkable.serverEntities.OfType<DieselEngine>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(engin.transform.position, pos) > 250) continue;
                        engin.cachedFuelTime = float.MaxValue;
                        using var update = engin.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.On, true);
                        break;
                    }
                }
                if (data.staticQuarries.ContainsKey(uint.MaxValue))
                {
                    foreach (var user in data.staticQuarries[uint.MaxValue])
                    {
                        BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(user.Value)) as BoxStorage;
                        if (!storage) continue;
                        GroundWatch gw = storage.GetComponent<GroundWatch>();
                        if (gw) UnityEngine.Object.Destroy(gw);
                        DestroyOnGroundMissing dogm = storage.GetComponent<DestroyOnGroundMissing>();
                        if (dogm) UnityEngine.Object.Destroy(dogm);
                        if (storage.children.Count == 0)
                        {
                            Puts($"[VirtualQuarries ERROR] Excavator quarry of {user.Key} is missing fuel container!");
                            continue;
                        }
                        BoxStorage fuelStorage = storage.children[0] as BoxStorage;
                        if (fuelStorage)
                        {
                            gw = fuelStorage.GetComponent<GroundWatch>();
                            if (gw) UnityEngine.Object.Destroy(gw);
                            dogm = fuelStorage.GetComponent<DestroyOnGroundMissing>();
                            if (dogm) UnityEngine.Object.Destroy(dogm);
                        }
                        VirtualQuarry vQuarry = storage.GetOrAddComponent<VirtualQuarry>();
                        string mineType = data.excavatorOutputs.TryGetValue(user.Key, out string output) ? output : string.Empty;
                        vQuarry.SetupQuarry(VirtualQuarry.VqType.Excavator, resource: mineType);
                    }
                }
            }
            else
                Unsubscribe(nameof(OnExcavatorResourceSet));
            if (!config.staticQuarries && !config.excavatorQuarry)
                Unsubscribe(nameof(CanLootEntity));
            foreach (var quarry in data.quarries)
            {
                cachedQuarryCount.TryAdd(quarry.Value.owner, new Dictionary<string, int>());
                cachedQuarryCount[quarry.Value.owner].TryAdd(quarry.Value.profile, 0);
                cachedQuarryCount[quarry.Value.owner][quarry.Value.profile]++;
                BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Value.netId)) as BoxStorage;
                VirtualQuarry vQuarry = storage?.GetComponent<VirtualQuarry>();
                if (!storage)
                {
                    Puts($"[VirtualQuarries ERROR] Quarry with ID {quarry.Key} had missing resource container! Recreating...");
                    SpawnQuarry(playerId: quarry.Value.owner, restoreId: quarry.Key );
                    storage = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Value.netId)) as BoxStorage;
                }
                else
                {
                    GroundWatch gw = storage.GetComponent<GroundWatch>();
                    if (gw) UnityEngine.Object.Destroy(gw);
                    DestroyOnGroundMissing dogm = storage.GetComponent<DestroyOnGroundMissing>();
                    if (dogm) UnityEngine.Object.Destroy(dogm);
                    BoxStorage fuelStorage = null;
                    List<UpgradeConfig> upgrades = config.quarryProfiles[quarry.Value.profile].upgrades;
                    if (storage.children.Count == 0)
                    {
                        fuelStorage = GameManager.server.CreateEntity(config.storagePrefab, storage.transform.position) as BoxStorage;
                        fuelStorage.Spawn();
                        UnityEngine.Object.Destroy(fuelStorage.GetComponent<GroundWatch>());
                        UnityEngine.Object.Destroy(fuelStorage.GetComponent<DestroyOnGroundMissing>());
                        fuelStorage.SetParent(storage);
                        fuelStorage.transform.position = storage.transform.position + new Vector3(0, 0.7f, 0);
                        if (quarry.Value.level < upgrades.Count)
                            fuelStorage.inventory.capacity = upgrades[quarry.Value.level].fuelCapacity;
                        Puts($"[VirtualQuarries ERROR] Spawning missing quarry fuel container of quarry with ID {quarry.Key}...");
                    }
                    fuelStorage = storage.children[0] as BoxStorage;
                    if (fuelStorage)
                    {
                        gw = fuelStorage.GetComponent<GroundWatch>();
                        if (gw) UnityEngine.Object.Destroy(gw);
                        dogm = fuelStorage.GetComponent<DestroyOnGroundMissing>();
                        if (dogm) UnityEngine.Object.Destroy(dogm);
                    }
                    else
                    {
                        fuelStorage = GameManager.server.CreateEntity(config.storagePrefab, storage.transform.position) as BoxStorage;
                        fuelStorage.Spawn();
                        UnityEngine.Object.Destroy(fuelStorage.GetComponent<GroundWatch>());
                        UnityEngine.Object.Destroy(fuelStorage.GetComponent<DestroyOnGroundMissing>());
                        fuelStorage.SetParent(storage);
                        fuelStorage.transform.position = storage.transform.position + new Vector3(0, 0.7f, 0);
                        if (quarry.Value.level < upgrades.Count)
                            fuelStorage.inventory.capacity = upgrades[quarry.Value.level].fuelCapacity;
                        Puts($"Spawning missing quarry fuel container of quarry with ID {quarry.Key}...");
                    }
                    if (!vQuarry)
                        storage.gameObject.AddComponent<VirtualQuarry>();
                    if (!config.quarryProfiles.ContainsKey(quarry.Value.profile))
                    {
                        PrintWarning($"There is a quarry with profile '{quarry.Value.profile}' but it is missing in the configuration!");
                        continue;
                    }
                    if (quarry.Value.level < upgrades.Count)
                    {
                        (storage.children[0] as BoxStorage).inventory.capacity = upgrades[quarry.Value.level].fuelCapacity;
                        storage.inventory.capacity = upgrades[quarry.Value.level].capacity;
                    }
                    else
                        PrintWarning($"Quarry with ID {quarry.Key} have more upgrades than is added to config! You need to add more levels or remove this quarry from data, or plugin will print errors!");
                }
                vQuarry = storage.GetComponent<VirtualQuarry>();
                vQuarry.SetupQuarry(id: quarry.Key);
            }
        }

        private void Unload()
        {
            if (config.storeContainers)
                SaveContainers();
            if (config.excavatorQuarry)
            {
                foreach (var arm in BaseNetworkable.serverEntities.OfType<ExcavatorArm>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(arm.transform.position, pos) > 250) continue;
                        using var update = arm.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.Reserved8, false);
                        update.Set(BaseEntity.Flags.On, false);
                        break;
                    }
                }
                foreach (var engin in BaseNetworkable.serverEntities.OfType<DieselEngine>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(engin.transform.position, pos) > 250) continue;
                        engin.cachedFuelTime = 0;
                        using var update = engin.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.On, false);
                        break;
                    }
                }
            }
            if (config.staticQuarries)
            {
                foreach (var quarry in BaseNetworkable.serverEntities.OfType<MiningQuarry>())
                {
                    if (quarry.staticType == MiningQuarry.QuarryType.None && quarry.OwnerID != 0) continue;
                    quarry.SetOn(false);
                    quarry.CancelInvoke();
                    quarry.engineSwitchPrefab.instance.SetFlagLocal(BaseEntity.Flags.On, false);
                    using var update = quarry.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                    update.Set(BaseEntity.Flags.On, false);
                }
            }
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_NewCursor");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Back");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Players");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Levels");
            }
            foreach (var netId in data.quarries.Values)
            {
                BoxStorage quarry = BaseNetworkable.serverEntities.Find(new NetworkableId(netId.netId)) as BoxStorage;
                if (quarry)
                    GameObject.Destroy(quarry.GetComponent<VirtualQuarry>());
            }
            foreach (var quarryId in data.staticQuarries.ToList())
            {
                foreach (var player in quarryId.Value)
                {
                    BoxStorage quarry = BaseNetworkable.serverEntities.Find(new NetworkableId(player.Value)) as BoxStorage;
                    if (quarry)
                        GameObject.Destroy(quarry.GetComponent<VirtualQuarry>());
                }
            }
            SaveData();
        }

        private void OnNewSave()
        {
            if (config.wipeData)
            {
                if (config.wipeDataForceOnly)
                {
                    DateTime now = DateTime.Now;
                    if (now.Day < 8 && now.DayOfWeek == DayOfWeek.Thursday)
                    {
                        data = new PluginData();
                        SaveData();
                        Puts("Force wipe found! Plugin data has been wiped successfully!");
                    }
                    return;
                }
                data = new PluginData();
                SaveData();
                Puts("Regular wipe found! Plugin data has been wiped successfully!");
            }
        }
        
        #endregion
        
        #region RUST Hooks

        private void OnPlayerConnected(BasePlayer player)
        {
            playerCache.TryAdd(player.userID, player.displayName);
            playerCache[player.userID] = player.displayName;
            if (config.offlineQuarryOwnerKickTime == 0) return;
            DateTime now = DateTime.Now;
            foreach (var quarry in data.quarries)
            {
                if (quarry.Value.owner != player.userID) continue;
                quarry.Value.lastOwnerOnline = now;
            }
        }

        private void OnLootEntityEnd(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Back");
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Levels");
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item)
        {
            if (item.ShortPrefabName.Contains("survey_charge") && item.skinID == 0)
            {
                entity.Kill();
                Item survey = ItemManager.CreateByName("surveycharge");
                NextTick(() => player.GiveItem(survey));
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoSurveyThrow", player.UserIDString, config.commandList[0]));
            }
        }

        private object OnEntityTakeDamage(BoxStorage entity)
        {
            if (entity.transform.position.y < -390) return config.returnValue;
            return null;
        }

        private void OnQuarryToggled(MiningQuarry quarry, BasePlayer player)
        {
            quarry.SetOn(false);
            quarry.CancelInvoke();
            quarry.engineSwitchPrefab.instance.SetFlagLocal(BaseEntity.Flags.On, true);
            using var update = quarry.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
            update.Set(BaseEntity.Flags.On, true);
        }

        private object OnExcavatorResourceSet(ExcavatorArm arm, string type, BasePlayer player)
        {
            bool valid = false;
            foreach (var pos in giantExcPositions)
            {
                if (Vector3.Distance(arm.transform.position, pos) > 250) continue;
                valid = true;
                break;
            }
            if (!valid) return null;
            if (!data.staticQuarries.ContainsKey(uint.MaxValue) || !data.staticQuarries[uint.MaxValue].ContainsKey(player.userID))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("OpenStorageFirst", player.UserIDString));
                return false;
            }
            ulong storageId = data.staticQuarries[uint.MaxValue][player.userID];
            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(storageId)) as BoxStorage;
            if (!storage)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ErrorOccured", player.UserIDString));
                return false;
            }
			VirtualQuarry vq = storage.GetComponent<VirtualQuarry>();
            vq.SwitchExcavatorType(type);
			vq.SwitchEngine(false);
            data.excavatorOutputs[player.userID] = type;
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("SwitchedResource", player.UserIDString, type));
            if (!arm.IsOn())
            {
                using var update = arm.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                update.Set(BaseEntity.Flags.On, true);
                ExcavatorServerEffects.SetMining(true, true);
            }
            return false;
        }

        private object OnExcavatorSuppliesRequest(ExcavatorArm arm, BasePlayer player)
        {
            bool valid = false;
            foreach (var pos in giantExcPositions)
            {
                if (Vector3.Distance(arm.transform.position, pos) > 250) continue;
                valid = true;
                break;
            }
            if (!valid) return null;
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoAirDrops", player.UserIDString));
            return false;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer storage)
        {
            if (!storage) return null;
            if (config.excavatorQuarry && storage is ExcavatorOutputPile excavatorStorage)
            {
                if (config.excavatorPerm && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.excavator"))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionQuarry", player.UserIDString));
                    return false;
                }
                uint excavatorId = uint.MaxValue;
                data.staticQuarries.TryAdd(excavatorId, new Dictionary<ulong, ulong>());
                if (!data.staticQuarries[excavatorId].ContainsKey(player.userID))
                    SpawnQuarry(VirtualQuarry.VqType.Excavator, player, excavatorStorage);
                else
                    OpenQuarry(player, VirtualQuarry.VqType.Excavator, quarryStorage: excavatorStorage);
                return false;
            }
            if (config.excavatorQuarry && storage is DieselEngine dieselStorage)
            {
                bool valid = false;
                foreach (var pos in giantExcPositions)
                {
                    if (Vector3.Distance(dieselStorage.transform.position, pos) > 250) continue;
                    valid = true;
                    break;
                }
                if (!valid) return null;
                if (config.excavatorPerm && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.excavator"))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionQuarry", player.UserIDString));
                    return false;
                }
                uint excavatorId = uint.MaxValue;
                data.staticQuarries.TryAdd(excavatorId, new Dictionary<ulong, ulong>());
                if (!data.staticQuarries[excavatorId].ContainsKey(player.userID))
                    SpawnQuarry(VirtualQuarry.VqType.Excavator, player, dieselStorage);
                else
                    OpenQuarry(player, VirtualQuarry.VqType.Excavator, quarryStorage: dieselStorage);
                return false;
            }
            if (config.staticQuarries && storage is ResourceExtractorFuelStorage quarryStorage)
            {
                MiningQuarry quarry = quarryStorage.GetParentEntity() as MiningQuarry;
                if (!quarry) return null;
                if (quarry.staticType == MiningQuarry.QuarryType.None && quarry.OwnerID != 0) return null;
                if (config.quarryPerm && quarry.ShortPrefabName == "mininquarry_static" && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.quarry"))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionQuarry", player.UserIDString));
                    return false;
                }
                if (config.pumpJackPerm && quarry.ShortPrefabName == "pumpjack-static" && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.pumpjack"))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionPumpJack", player.UserIDString));
                    return false;
                }
                data.staticQuarries.TryAdd(quarry.net.ID.Value, new Dictionary<ulong, ulong>());
                if (!data.staticQuarries[quarry.net.ID.Value].ContainsKey(player.userID))
                    SpawnQuarry(VirtualQuarry.VqType.Static, player, quarryStorage, quarry);
                else
                    OpenQuarry(player, VirtualQuarry.VqType.Static, true, 0, quarryStorage, quarry);
                return false;
            }
            return null;
        }
        
        #endregion
        
        #region Commands

        [ConsoleCommand("UI_VirtualQuarries")]
        private void VirtualQuarriesConsoleCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (config.commandCooldown > 0)
            {
                if (commandCooldowns.ContainsKey(player.userID) && Time.realtimeSinceStartup - commandCooldowns[player.userID] < config.commandCooldown) return;
                commandCooldowns[player.userID] = Time.realtimeSinceStartup;
            }
            if (config.requirePermission && !permission.UserHasPermission(player.UserIDString, "virtualquarries.use"))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermission", player.UserIDString));
                return;
            }
            switch (arg.GetString(0))
            {
                case "close":
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_NewCursor");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Players");
                    break;
                case "new":
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                    SelectSurveyTypeUI(player);
                    break;
                case "back":
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_NewCursor");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Back");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Players");
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Levels");
                    player.EndLooting();
                    if (arg.Args.Length == 1)
                        ShowQuarryMenuUI(player);
                    else
                        ShowQuarryMenuUI(player, 1, arg.GetInt(1));
                    break;
                case "throw_open":
                    cachedSurveyType.TryAdd(player.userID, "");
                    cachedSurveyType[player.userID] = arg.GetString(1);
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
                    ShowNewQuarryUI(player, true);
                    break;
                case "throw":
                    TrySurveyThrow(player);
                    break;
                case "place":
                    TryPlaceQuarry(player);
                    break;
                case "view":
                    ShowQuarryMenuUI(player, arg.GetInt(1), arg.GetInt(2));
                    break;
                case "engine":
                    TryStartQuarry(player, arg.GetInt(1));
                    break;
                case "fuel":
                    OpenQuarry(player, isFuel: true, dataId: arg.GetInt(1));
                    break;
                case "storage":
                    OpenQuarry(player, dataId: arg.GetInt(1));
                    break;
                case "delete":
                    TryRemoveQuarry(player, arg.GetInt(1));
                    break;
                case "sortOwned":
                    ChangeSort(player, "owned");
                    break;
                case "sortShared":
                    ChangeSort(player, "shared");
                    break;
                case "sortDisable":
                    ChangeSort(player, "disable");
                    break;
                case "page":
                    int page = arg.GetInt(1);
                    cachedPage.TryAdd(player.userID, page);
                    cachedPage[player.userID] = page;
                    ShowQuarryMenuUI(player, page);
                    break;
                case "action":
                    CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                    if (arg.Args.Length == 2)
                        ManageQuarryPlayersUI(player, arg.GetString(1));
                    else if (arg.Args.Length == 3)
                        ManageQuarryPlayersUI(player, arg.GetString(1), arg.GetInt(2));
                    else if (arg.Args.Length == 4)
                        ManageQuarryPlayersUI(player, arg.GetString(1), arg.GetInt(2), arg.GetInt(3));
                    else if (arg.Args.Length >= 5)
                        ManageQuarryPlayersUI(player, arg.GetString(1), arg.GetInt(2), arg.GetInt(3), arg.GetString(4));
                    break;
                case "doAction":
                    ManageQuarryUser(player, arg.GetString(1), arg.GetInt(2), arg.GetUInt64(3));
                    break;
                case "list":
                    ShowQuarryAccessList(player, arg.GetInt(1));
                    break;
                case "upgrade":
                    TryUpgradeQuarry(player, arg.GetInt(1), arg.GetString(2));
                    break;
                default:
                    ShowQuarryMenuUI(player);
                    break;
            }
        }

        private void VirtualQuarriesCommand(BasePlayer player) => ShowQuarryMenuUI(player);
        
        #endregion
        
        #region Function Methods
        
        #region All Quarries
        
        private void OpenQuarry(BasePlayer player, VirtualQuarry.VqType vqType = VirtualQuarry.VqType.Default, bool isFuel = false, int dataId = 0, StorageContainer quarryStorage = null, MiningQuarry quarry = null)
        {
            if (vqType == VirtualQuarry.VqType.Default)
            {
                QuarryData qd = data.quarries[dataId];
                if (qd.owner != player.userID && !qd.authPlayers.Contains(player.userID)) return;
                if (config.lockAccesNoPerm)
                {
                    Dictionary<string, int> playerPerm = new Dictionary<string, int>();
                    foreach (var configPerm in config.permissions)
                        if (permission.UserHasPermission(player.UserIDString, configPerm.Key))
                            playerPerm = configPerm.Value;
                    if (!playerPerm.ContainsKey(qd.profile))
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoLongerProfilePerm", player.UserIDString));
                        return;
                    }
                    if (config.checkQuarryAmount)
                    {
                        int quarryCount = 0;
                        foreach (var quarryCheck in data.quarries)
                        {
                            if (quarryCheck.Value.owner == player.userID && quarryCheck.Value.profile == qd.profile)
                                quarryCount++;
                        }
                        if (quarryCount > playerPerm[qd.profile])
                        {
                            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("TooManyQuarriesPermMissing", player.UserIDString));
                            return;
                        }
                    }
                }
            }

            BoxStorage storage;
            bool isExcavator = false;
            if (vqType == VirtualQuarry.VqType.Default)
            {
                storage = GetQuarry(dataId);
                if (!storage)
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ErrorOccured", player.UserIDString));
                    return;
                }
            }
            else
            {
                isExcavator = vqType == VirtualQuarry.VqType.Excavator;
                ulong quarryId = isExcavator ? uint.MaxValue : quarry.net.ID.Value;
                VirtualQuarry.VqType type = isExcavator ? VirtualQuarry.VqType.Excavator : VirtualQuarry.VqType.Static;
                ulong storageId = data.staticQuarries[quarryId][player.userID];
                storage = BaseNetworkable.serverEntities.Find(new NetworkableId(storageId)) as BoxStorage;
                if (!storage)
                {
                    SpawnQuarry(type, player, quarryStorage, quarry);
                    return;
                }
            }
            player.EndLooting();
            if (vqType == VirtualQuarry.VqType.Default)
            {
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                if (isFuel)
                {
                    storage = storage.children[0] as BoxStorage;
                    storage.inventory.canAcceptItem = (_, item, i) => IsFuel(item, data.quarries[dataId].profile);
                }
                else
                { 
                    storage.inventory.canAcceptItem = (_, item, i) => IsResourceOutput(item, data.quarries[dataId].profile);
                    AddQuarryLevelsUI(player, dataId);
                }
                AddBackButtonUI(player, dataId);
            }
            else
            {
                if (quarryStorage.ShortPrefabName == "fuelstorage")
                {
					BoxStorage parent = storage;
                    storage = storage.children[0] as BoxStorage;
                    storage.inventory.canAcceptItem = (_, item, _) => IsStaticFuel(item);
                    storage.inventory.onItemAddedRemoved = (item, b) => CheckForNewFuel(item, b, parent);
                    storage.inventory.capacity = config.staticFuelSize;
                }
                else if (quarryStorage.ShortPrefabName == "engine")
                {
					BoxStorage parent = storage;
                    storage = storage.children[0] as BoxStorage;
                    storage.inventory.canAcceptItem = (_, item, _) => IsExcavatorFuel(item);
                    storage.inventory.onItemAddedRemoved = (item, b) => CheckForNewFuel(item, b, parent);
                    storage.inventory.capacity = config.excavatorFuelSize;
                }
                else if (!isExcavator)
                {
                    int type = quarry.ShortPrefabName == "pumpjack-static" ? 3 : convertType[quarry.staticType];
                    storage.inventory.canAcceptItem = (_, item, _) => IsStaticResourceOutput(item, type);
                    storage.inventory.capacity = config.staticResourceSize;
                }
                else
                {
                    storage.inventory.canAcceptItem = (_, item, _) => IsStaticResourceOutput(item, 4);
                    storage.inventory.capacity = config.excavatorResourceSize;
                }
				PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("PrivateInventoryInfo", player.UserIDString));
            }
            player.inventory.loot.AddContainer(storage.inventory);
            player.inventory.loot.entitySource = storage;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.SendImmediate();
            player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
            Interface.CallHook("OnLootEntity", player, storage);
        }
        
        //vqType - Quarry Type (All Hooks)
        //player - Static Quarry Types
        //storage - Static Quarry Types
        //quarry - Static Quarry Types
        //playerId - Default Quarry Types
        //restoreId - Default Quarry Types
        private void SpawnQuarry(VirtualQuarry.VqType vqType = VirtualQuarry.VqType.Default, BasePlayer player = null, StorageContainer storage = null, MiningQuarry quarry = null, ulong playerId = 0, int restoreId = -1)
        {
			bool staticQuarry = vqType != VirtualQuarry.VqType.Default;
            if (!staticQuarry && restoreId == -1 && (!cachedSurveys.ContainsKey(playerId) || (cachedSurveys.ContainsKey(playerId) && cachedSurveys[playerId].resources.Count == 0))) return;
            int worldSize = Convert.ToInt32(World.Size);
            float startX = -worldSize / 1.5f;
            float startZ = worldSize / 1.5f;
            Vector3 newLocation = new Vector3(startX + Core.Random.Range(-50, 50), -400, startZ + Core.Random.Range(-50, 50));
            
            BoxStorage resourceStorage = GameManager.server.CreateEntity(config.storagePrefab, newLocation) as BoxStorage;
            UnityEngine.Object.Destroy(resourceStorage.GetComponent<GroundWatch>());
            UnityEngine.Object.Destroy(resourceStorage.GetComponent<DestroyOnGroundMissing>());
            resourceStorage.Spawn();
            if (vqType != VirtualQuarry.VqType.Default)
                resourceStorage.inventory.capacity = staticQuarry ? config.staticResourceSize : config.excavatorResourceSize;
            
            
            BoxStorage fuelStorage = GameManager.server.CreateEntity(config.storagePrefab, newLocation) as BoxStorage;
            UnityEngine.Object.Destroy(fuelStorage.GetComponent<GroundWatch>());
            UnityEngine.Object.Destroy(fuelStorage.GetComponent<DestroyOnGroundMissing>());
            fuelStorage.Spawn();
            fuelStorage.SetParent(resourceStorage);
            fuelStorage.transform.position = newLocation + new Vector3(0, 0.7f, 0);
            VirtualQuarry vQuarry = resourceStorage.GetOrAddComponent<VirtualQuarry>();
            if (vqType != VirtualQuarry.VqType.Default)
            {
                ulong id = !quarry ? uint.MaxValue : quarry.net.ID.Value;
                fuelStorage.inventory.capacity = staticQuarry ? config.staticFuelSize : config.excavatorFuelSize;
                data.staticQuarries[id].TryAdd(player.userID, resourceStorage.net.ID.Value);
                data.staticQuarries[id][player.userID] = resourceStorage.net.ID.Value;
                vQuarry.SetupQuarry(vqType, linkedQuarry: quarry);
                if (storage is ResourceExtractorFuelStorage resourceExtractorFuelStorage)
                    OpenQuarry(player, vqType, true, 0, resourceExtractorFuelStorage, quarry);
                else if (storage is DieselEngine engine)
                    OpenQuarry(player, vqType, true, 0, engine, quarry);
                else if (storage is ExcavatorOutputPile outputPile)
                    OpenQuarry(player, vqType, true, 0, outputPile, quarry);
            }
            else
            {
                string profile;
                int level = 0;
                int quarryId = restoreId == -1 ? data.quarryCount : restoreId;
                if (restoreId == -1)
                {
                    data.quarries.TryAdd(data.quarryCount, new QuarryData()
                    {
                        netId = resourceStorage.net.ID.Value,
                        owner = playerId,
                        resources = cachedSurveys[playerId].resources,
                        profile = cachedSurveys[playerId].profile,
                    });
                    profile = cachedSurveys[playerId].profile;
                    if (config.consoleLogs)
                        Puts($"Player {playerId} placed new quarry with ID {data.quarryCount}.");
                    data.quarryCount++;
                }
                else
                {
                    profile = data.quarries[restoreId].profile;
                    level = data.quarries[restoreId].level;
                    data.quarries[restoreId].netId = resourceStorage.net.ID.Value;
                }
                List<UpgradeConfig> upgrades = config.quarryProfiles[profile].upgrades;
                resourceStorage.inventory.capacity = upgrades[level].capacity;
                fuelStorage.inventory.capacity = upgrades[level].fuelCapacity;
                vQuarry.SetupQuarry(id: quarryId);
                cachedSurveys.Remove(playerId);
                cachedRequirements.Remove(playerId);
                if (config.storeContainers && restoreId != -1 && storageCache.ContainsKey(restoreId))
                {
                    foreach (var item in storageCache[restoreId].resource)
                    {
                        Item restoreItem = ItemManager.CreateByName(item.shortname, item.amount, item.skin);
                        if (!string.IsNullOrEmpty(item.displayName))
                            restoreItem.name = item.displayName;
                        restoreItem.MoveToContainer(resourceStorage.inventory);
                    }
                    foreach (var item in storageCache[restoreId].fuel)
                    {
                        Item restoreItem = ItemManager.CreateByName(item.shortname, item.amount, item.skin);
                        if (!string.IsNullOrEmpty(item.displayName))
                            restoreItem.name = item.displayName;
                        restoreItem.MoveToContainer(fuelStorage.inventory);
                    }
                }
            }
        }
        
        #endregion

        #region Default Quarries
        private void ShowQuarryAccessList(BasePlayer player, int quarryId)
        {
            if (data.quarries[quarryId].owner != player.userID && !data.quarries[quarryId].authPlayers.Contains(player.userID)) return;
            string accessList = Lang("AccessListStart", player.UserIDString);
            foreach (var playerId in data.quarries[quarryId].authPlayers)
                accessList += Lang("AccessListList", player.UserIDString, playerCache[playerId]);
            if (accessList == Lang("AccessListStart", player.UserIDString))
                accessList += Lang("AccessListNoAdded", player.UserIDString);
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, accessList, 12);
        }

        private void TryUpgradeQuarry(BasePlayer player, int quarryId, string type)
        {
            if (data.quarries[quarryId].owner != player.userID && !data.quarries[quarryId].authPlayers.Contains(player.userID)) return;
            QuarryData quarry = data.quarries[quarryId];
            List<UpgradeConfig> upgrades = config.quarryProfiles[quarry.profile].upgrades;
            if (quarry.level >= upgrades.Count - 1) return;
            if (upgrades[quarry.level + 1].requiredPerm != string.Empty && !permission.UserHasPermission(player.UserIDString, upgrades[quarry.level + 1].requiredPerm))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermToUpgrade", player.UserIDString));
                return;
            }
            if (type == "currency")
            {
                if (upgrades[quarry.level + 1].requiredRp == 0) return;
                ulong userId = player.userID.Get();
                if (config.economyPlugin == 1)
                {
                    if (Economics.Call<double>("Balance", userId) >= upgrades[quarry.level + 1].requiredRp)
                    {
                        Economics.Call("Withdraw", userId, Convert.ToDouble(upgrades[quarry.level + 1].requiredRp));
                        UpgradeQuarry(player, quarryId, type);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 2)
                {
                    if (ServerRewards.Call<int>("CheckPoints", userId) >= upgrades[quarry.level + 1].requiredRp)
                    {
                        ServerRewards.Call("TakePoints", userId, upgrades[quarry.level + 1].requiredRp);
                        UpgradeQuarry(player, quarryId, type);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 3)
                {
                    if (IQEconomic.Call<int>("API_GET_BALANCE", userId) >= upgrades[quarry.level + 1].requiredRp)
                    {
                        IQEconomic.Call("API_REMOVE_BALANCE", userId, upgrades[quarry.level + 1].requiredRp);
                        UpgradeQuarry(player, quarryId, type);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 4)
                {
                    if (BankSystem.Call<int>("Balance", userId) >= upgrades[quarry.level + 1].requiredRp)
                    {
                        BankSystem.Call("Withdraw", userId, upgrades[quarry.level + 1].requiredRp);
                        UpgradeQuarry(player, quarryId, type);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 5)
                {
                    if (ShoppyStock.Call<int>("GetCurrencyAmount", config.economyCurrency, userId) >= upgrades[quarry.level + 1].requiredRp)
                    {
                        ShoppyStock.Call("TakeCurrency", config.economyCurrency, userId, upgrades[quarry.level + 1].requiredRp);
                        UpgradeQuarry(player, quarryId, type);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
            }
            else
            {
                DateTime now = DateTime.Now;
                if ((now - lastOperation).TotalSeconds < 1) return;
                if (!TakeResources(player, upgrades[quarry.level + 1].requiredItems))
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughItems", player.UserIDString));
                else
                    UpgradeQuarry(player, quarryId, type);
                lastOperation = now;
            }
        }

        private void UpgradeQuarry(BasePlayer player, int quarryId, string type)
        {
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Levels");
            QuarryData quarry = data.quarries[quarryId];
            quarry.level++;
            bool typeBool = type == "currency";
            quarry.upgradedForRp.TryAdd(quarry.level, typeBool);
            List<UpgradeConfig> upgrades = config.quarryProfiles[quarry.profile].upgrades;
            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.netId)) as BoxStorage;
            if (!storage) return;
            storage.inventory.capacity = upgrades[quarry.level].capacity;
            storage.SendNetworkUpdate();
            BoxStorage fuelStorage = storage.children.Count > 0 ? storage.children[0] as BoxStorage : null;
            if (fuelStorage)
            {
                fuelStorage.inventory.capacity = upgrades[quarry.level].fuelCapacity;
                fuelStorage.SendNetworkUpdate();
            }
            storage.GetComponent<VirtualQuarry>().SetupQuarry(id: quarryId);
            bool anyNewResource = false;
            if (upgrades[quarry.level].additionalResources.Any())
            {
                foreach (var resourceKey in upgrades[quarry.level].additionalResources)
                {
                    if (!config.quarryProfiles[quarry.profile].resources.ContainsKey(resourceKey)) continue;
                    ResourceConfig resource = config.quarryProfiles[quarry.profile].resources[resourceKey];
                    if (resource.permission != string.Empty && !permission.UserHasPermission(player.UserIDString, resource.permission)) continue;
                    quarry.resources.Add(new QuarryResource() { configKey = resourceKey, work = Core.Random.Range(resource.outputMin, resource.outputMax) });
                    anyNewResource = true;
                }
            }

            if (anyNewResource)
            {
                VirtualQuarry qr = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.netId))?.GetComponent<VirtualQuarry>();
                qr?.SetupQuarry(id: quarryId);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NewResourceDug", player.UserIDString));
            }
            Effect.server.Run("assets/prefabs/deployable/quarry/effects/mining-quarry-deploy.prefab", player.transform.position);
            Effect.server.Run("assets/bundled/prefabs/fx/build/promote_toptier.prefab", player.transform.position);
            Interface.CallHook("OnQuarryUpgraded", player, quarry.level, quarry.profile);
            player.EndLooting();
            timer.Once(0.2f, () => OpenQuarry(player, dataId: quarryId));
            if (config.consoleLogs)
                Puts($"Player {player.displayName} ({player.userID}) upgraded his quarry with ID {quarryId} to level {quarry.level + 1}.");
        }

        private void ManageQuarryUser(BasePlayer player, string type, int quarryId, ulong userId)
        {
            if (quarryId != 0 && !data.quarries.ContainsKey(quarryId)) return;
            if (quarryId != 0 && data.quarries[quarryId].owner != player.userID && !data.quarries[quarryId].authPlayers.Contains(player.userID)) return;
            if (type != "remove" && config.shareClanOnly)
            {
                if (player.currentTeam == 0 || !RelationshipManager.ServerInstance.teams[player.currentTeam].members.Contains(userId))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("OnlyTeamShare", player.UserIDString));
                    return;
                }
            }
            if (type.Contains("addAll"))
            {
                foreach (var quarry in data.quarries.Values)
                    if (quarry.owner == player.userID && !quarry.authPlayers.Contains(userId))
                        quarry.authPlayers.Add(userId);
                return;
            }
            else if (type == "remove")
                data.quarries[quarryId].authPlayers.Remove(userId);
            else if (type.Contains("add") && !data.quarries[quarryId].authPlayers.Contains(userId))
                data.quarries[quarryId].authPlayers.Add(userId);
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Players");
            ShowQuarryMenuUI(player, 1, quarryId);
        }

        private void ChangeSort(BasePlayer player, string type)
        {
            cachedSort.Remove(player.userID);
            if (type == "owned")
                cachedSort.TryAdd(player.userID, "owned");
            else if (type == "shared")
                cachedSort.TryAdd(player.userID, "shared");
            ShowQuarryMenuUI(player);
        }

        private void TryRemoveQuarry(BasePlayer player, int quarryId)
        {
            if (!data.quarries.TryGetValue(quarryId, out var dataQuarry))
            {
                Puts($"[VirtualQuarries ERROR] Player {player.displayName} ({player.UserIDString}) is trying to remove quarry more than once!");
                return;
            }
            if (dataQuarry.owner != player.userID) return;
            if (!player.serverInput.IsDown(BUTTON.SPRINT))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoSprintButton", player.UserIDString));
                return;
            }
            BoxStorage quarry = GetQuarry(quarryId);
            bool anyRefund = false;
            if (config.refundRemove)
            {
                anyRefund = true;
                List<RequiredItem> refundItems1 = config.quarryProfiles[dataQuarry.profile].requiredItems;
                foreach (var refundItem in refundItems1)
                {
                    Item item = ItemManager.CreateByName(refundItem.shortname, refundItem.amount, refundItem.skin);
                    if (!string.IsNullOrEmpty(refundItem.displayName))
                        item.name = refundItem.displayName;
                    if (!item.MoveToContainer(player.inventory.containerMain))
                        item.Drop(player.eyes.position, Vector3.zero);
                }
                Dictionary<string, ResourceConfig> refundItems2 = config.quarryProfiles[dataQuarry.profile].resources;
                foreach (var resource in dataQuarry.resources)
                {
                    if (!refundItems2.TryGetValue(resource.configKey, out var value)) continue;
                    foreach (var refundItem in value.additionalItems)
                    {
                        Item item = ItemManager.CreateByName(refundItem.shortname, refundItem.amount, refundItem.skin);
                        if (!string.IsNullOrEmpty(refundItem.displayName))
                            item.name = refundItem.displayName;
                        if (!item.MoveToContainer(player.inventory.containerMain))
                            item.Drop(player.eyes.position, Vector3.zero);
                    }
                }
                foreach (var item in quarry.inventory.itemList.ToList())
                    if (!item.MoveToContainer(player.inventory.containerMain))
                        item.Drop(player.eyes.position, Vector3.zero);
                BoxStorage fuelStorage = quarry.children[0] as BoxStorage;
                if (fuelStorage)
                    foreach (var item in fuelStorage.inventory.itemList.ToList())
                        if (!item.MoveToContainer(player.inventory.containerMain))
                            item.Drop(player.eyes.position, Vector3.zero);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryRemovedRefund", player.UserIDString), 5f, "Overlay");
            }
            if (config.refundUpgrades)
            {
                foreach (var upgrade in dataQuarry.upgradedForRp)
                {
                    if (config.quarryProfiles[dataQuarry.profile].upgrades.Count - 1 >= upgrade.Key)
                    {
                        UpgradeConfig upgradeConfig = config.quarryProfiles[dataQuarry.profile].upgrades[upgrade.Key];
                        if (upgrade.Value)
                        {
                            ulong userId = player.userID.Get();
                            if (config.economyPlugin == 1)
                                Economics.Call("Deposit", userId, Convert.ToDouble(upgradeConfig.requiredRp));
                            else if (config.economyPlugin == 2)
                                ServerRewards.Call("AddPoints", userId, upgradeConfig.requiredRp);
                            else if (config.economyPlugin == 4)
                                BankSystem.Call("Deposit", userId, upgradeConfig.requiredRp);
                            else if (config.economyPlugin == 5)
                                ShoppyStock.Call("GiveCurrency", config.economyCurrency, userId, upgradeConfig.requiredRp);
                        }
                        else
                        {
                            foreach (var refundItem in upgradeConfig.requiredItems)
                            {
                                Item item = ItemManager.CreateByName(refundItem.shortname, refundItem.amount, refundItem.skin);
                                if (!string.IsNullOrEmpty(refundItem.displayName))
                                    item.name = refundItem.displayName;
                                if (!item.MoveToContainer(player.inventory.containerMain))
                                    item.Drop(player.eyes.position, Vector3.zero);
                            }
                        }
                    }
                }
                if (!anyRefund)
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryRemovedRefund", player.UserIDString), 5f, "Overlay");
                anyRefund = true;
            }
            if (!anyRefund)
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryRemoved", player.UserIDString), 5f, "Overlay");
            cachedQuarryCount[player.userID][dataQuarry.profile]--;
            Interface.CallHook("OnQuarryRemoved", player, dataQuarry.profile);
            quarry?.Kill();
            if (config.consoleLogs)
                Puts($"Player {player.displayName} ({player.userID}) removed quarry with ID {quarryId} with level {dataQuarry.level}.");
            data.quarries.Remove(quarryId);
            ShowQuarryMenuUI(player);
        }

        private void TrySurveyThrow(BasePlayer player)
        {
            if (!cachedSurveyType.ContainsKey(player.userID)) return;
            if (config.surveys[cachedSurveyType[player.userID]].permission != "" && !permission.UserHasPermission(player.UserIDString, config.surveys[cachedSurveyType[player.userID]].permission)) return;
            List<RequiredItem> requiredSurvey = new List<RequiredItem>() { config.surveys[cachedSurveyType[player.userID]].surveyItem };
            if (!TakeResources(player, requiredSurvey))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoRequiredSurvey", player.UserIDString, Lang(config.surveys[cachedSurveyType[player.userID]].surveyTranslation, player.UserIDString)));
                return;
            }
            if (config.surveys[cachedSurveyType[player.userID]].effectPath != "")
                EffectNetwork.Send(new Effect(config.surveys[cachedSurveyType[player.userID]].effectPath, player, 0, Vector3.zero, Vector3.up), player.net.connection);
            cachedSurveys.TryAdd(player.userID, new CachedSurvey());
            cachedRequirements.TryAdd(player.userID, new List<RequiredItem>());
            cachedSurveys[player.userID] = new CachedSurvey();
            cachedRequirements[player.userID] = new List<RequiredItem>();
            if (Core.Random.Range(1, 101) > config.surveys[cachedSurveyType[player.userID]].resourceChance)
            {
                ShowNewQuarryUI(player);
                return;
            }
            int sumChance = 0;
            foreach (var profile in config.quarryProfiles.Values)
            {
                if (profile.surveyType != cachedSurveyType[player.userID]) continue;
                if (profile.permission == string.Empty || permission.UserHasPermission(player.UserIDString, profile.permission))
                    sumChance += profile.chance;
            }
            int rolledProfile = Core.Random.Range(0, sumChance + 1);
            sumChance = 0;
            string profileKey = string.Empty;
            foreach (var profile in config.quarryProfiles)
            {
                if (profile.Value.surveyType != cachedSurveyType[player.userID]) continue;
                if (profile.Value.permission == string.Empty || permission.UserHasPermission(player.UserIDString, profile.Value.permission))
                {
                    sumChance += profile.Value.chance;
                    if (sumChance >= rolledProfile)
                    {
                        cachedSurveys[player.userID].profile = profile.Key;
                        profileKey = profile.Key;
                        break;
                    }
                }
            }
            Dictionary<string, ResourceConfig> resources = config.quarryProfiles[profileKey].resources;
            cachedRequirements[player.userID].AddRange(config.quarryProfiles[profileKey].requiredItems);
            foreach (var resource in resources)
                if (resource.Value.alwaysInclude)
                {
                    if (resource.Value.permission != string.Empty && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    cachedSurveys[player.userID].resources.Add(new QuarryResource() { configKey = resource.Key, work = Core.Random.Range(resource.Value.outputMin, resource.Value.outputMax) });
                    cachedRequirements[player.userID].AddRange(resource.Value.additionalItems);
                }
            List<ResourceConfig> resourceList = new List<ResourceConfig>();
            int loopCount = Core.Random.Range(config.quarryProfiles[profileKey].minPerNode, config.quarryProfiles[profileKey].maxPerNode + 1) - cachedSurveys[player.userID].resources.Count;
            for (int i = 0; i < loopCount; i++)
            {
                int chance = 0;
                foreach (var resource in resources)
                {
                    if (resource.Value.alwaysInclude || resourceList.Contains(resource.Value)) continue;
                    if (resource.Value.permission != string.Empty && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    chance += resource.Value.chance;
                }
                int rolledItem = Core.Random.Range(0, chance + 1);
                chance = 0;
                foreach (var resource in resources)
                {
                    if (resource.Value.alwaysInclude || resourceList.Contains(resource.Value)) continue;
                    if (resource.Value.permission != string.Empty && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    chance += resource.Value.chance;
                    if (chance >= rolledItem)
                    {
                        cachedSurveys[player.userID].resources.Add(new QuarryResource() { configKey = resource.Key, work = Core.Random.Range(resource.Value.outputMin, resource.Value.outputMax) });
                        cachedRequirements[player.userID].AddRange(resource.Value.additionalItems);
                        resourceList.Add(resource.Value);
                        break;
                    }
                }
            }
            Interface.CallHook("OnCustomSurveyThrow", player, profileKey);
            ShowNewQuarryUI(player);
        }

        private void TryPlaceQuarry(BasePlayer player)
        {
            if (!cachedSurveys.ContainsKey(player.userID) || cachedSurveys[player.userID].resources.Count == 0) return;
            Dictionary<string, int> playerPerm = null;
            foreach (var configPerm in config.permissions)
                if (permission.UserHasPermission(player.UserIDString, configPerm.Key))
                    playerPerm = configPerm.Value;
            if (playerPerm == null)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotAllowedToPlace", player.UserIDString));
                return;
            }
            cachedQuarryCount.TryAdd(player.userID, new Dictionary<string, int>());
            cachedQuarryCount[player.userID].TryAdd(cachedSurveys[player.userID].profile, 0);
            if (playerPerm.TryGetValue("*", out int value))
            {
                int summedQuarries = cachedQuarryCount[player.userID].Sum(x => x.Value);
                if (summedQuarries >= value)
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("TooManyQuarries", player.UserIDString));
                    return;
                }
            }
            if (playerPerm.ContainsKey(cachedSurveys[player.userID].profile) && playerPerm[cachedSurveys[player.userID].profile] <= cachedQuarryCount[player.userID][cachedSurveys[player.userID].profile])
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("TooManyQuarries", player.UserIDString));
                return;
            }
            if (!TakeResources(player, cachedRequirements[player.userID]))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoRequiredItems", player.UserIDString));
                return;
            }
            cachedQuarryCount[player.userID][cachedSurveys[player.userID].profile]++;
            Effect.server.Run("assets/prefabs/deployable/quarry/effects/mining-quarry-deploy.prefab", player.transform.position);
            Interface.CallHook("OnQuarryPlaced", player, cachedSurveys[player.userID].profile);
            SpawnQuarry(playerId: player.userID);
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_NewCursor");
            ShowQuarryMenuUI(player, 1, data.quarryCount - 1);
        }

        private void TryStartQuarry(BasePlayer player, int quarryId)
        {
            QuarryData qd = data.quarries[quarryId];
            if (qd.owner != player.userID && !qd.authPlayers.Contains(player.userID)) return;
            BoxStorage quarry = GetQuarry(quarryId);
            if (!quarry)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ErrorOccured", player.UserIDString));
                return;
            }
            VirtualQuarry virtualQuarry = quarry.GetComponent<VirtualQuarry>();
            if (!virtualQuarry)
            {
                Puts($"[VirtualQuarries ERROR] Player {player.displayName} ({player.UserIDString}) is trying to open quarry that somehow doesn't have VirtualQuarry module!");
                return;
            }
            bool oldStatus = qd.isRunning;
            virtualQuarry.SwitchEngine();
            ShowQuarryMenuUI(player, 1, quarryId);
            if (oldStatus == false && qd.isRunning == false)
            {
                FuelItem fuelItem = config.quarryProfiles[qd.profile].fuelItem;
                string displayedFuelName = fuelItem.skin == 0 ? Lang($"Fuel_{fuelItem.shortname}", player.UserIDString) : Lang($"Fuel_{fuelItem.skin}", player.UserIDString);
                float levelMultiplier = config.quarryProfiles[qd.profile].upgrades[qd.level].fuelMultiplier;
                int fuelAmount = Mathf.CeilToInt(fuelItem.amount * levelMultiplier);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoFuelHint", player.UserIDString, fuelAmount, displayedFuelName));
            }
            else if (oldStatus == false && config.startSound != "")
                EffectNetwork.Send(new Effect(config.startSound, player, 0, new Vector3(0, 1.7f, 0), Vector3.up), player.net.connection);
            else if (oldStatus == true && config.stopSound != "")
                EffectNetwork.Send(new Effect(config.stopSound, player, 0, new Vector3(0, 1.7f, 0), Vector3.up), player.net.connection);
        }

        private static bool IsResourceOutput(Item item, string profile)
        {
            if (item.GetOwnerPlayer()) return false;
            foreach (var qItem in config.quarryProfiles[profile].resources)
                if (qItem.Value.shortname == item.info.shortname && qItem.Value.skin == item.skin)
                    return true;
            return false;
        }

        private static bool IsFuel(Item item, string profile)
        {
            if (item.info.shortname == config.quarryProfiles[profile].fuelItem.shortname && item.skin == config.quarryProfiles[profile].fuelItem.skin) return true;
            else return false;
        }
        
        private void SaveContainers()
        {
            if (config.consoleLogs)
                Puts("Saving quarry containers...");
            int containerCount = 0;
            foreach (var quarry in data.quarries)
            {
                BoxStorage resources = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Value.netId)) as BoxStorage;
                if (!resources) continue;
                SaveItems(resources, quarry.Key, "resource");
                if (resources.transform.childCount == 0)
                {
                    Puts($"An error occured while trying to save quarry no. {quarry.Key}. Make sure you don't have any plugin that destroys some entities on map like custom decay plugin or offline players purge.");
                    continue;
                }
                BoxStorage fuel = resources.children[0] as BoxStorage;
                if (!fuel) continue;
                SaveItems(fuel, quarry.Key, "fuel");
                containerCount++;
            }
            Interface.Oxide.DataFileSystem.WriteObject($"{this.Name}/storageCache", storageCache);
            if (config.consoleLogs)
                Puts($"Successfully saved {containerCount} quarry containers!");
			
        }

        private void SaveItems(BoxStorage storage, int quarryId, string dataType)
        {
            storageCache.TryAdd(quarryId, new StorageData());
            if (dataType == "resource")
            {
                storageCache[quarryId].resource = new List<RequiredItem>();
                foreach (var item in storage.inventory.itemList.ToList())
                {
                    string name = string.IsNullOrEmpty(item.name) ? null : item.name;
                    storageCache[quarryId].resource.Add(new RequiredItem()
                    {
                        shortname = item.info.shortname,
                        skin = item.skin,
                        amount = item.amount,
                        displayName = name
                    });
                }
            }
            else
            {
                storageCache[quarryId].fuel = new List<RequiredItem>();
                foreach (var item in storage.inventory.itemList.ToList())
                {
                    string name = string.IsNullOrEmpty(item.name) ? null : item.name;
                    storageCache[quarryId].fuel.Add(new RequiredItem()
                    {
                        shortname = item.info.shortname,
                        skin = item.skin,
                        amount = item.amount,
                        displayName = name
                    });
                }
            }
        }

        private BoxStorage GetQuarry(int quarryId)
        {
            if (!data.quarries.TryGetValue(quarryId, out var quarry)) return null;
            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.netId)) as BoxStorage;
            if (storage) return storage;
            SpawnQuarry(restoreId: quarryId);
            storage = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.netId)) as BoxStorage;
            if (storage) return storage;
            return null;
        }

        private void CheckQuarryKicks()
        {
            DateTime now = DateTime.Now;
            int cleared = 0;
            foreach (var quarry in data.quarries.ToList())
            {
                if (quarry.Value.lastOwnerOnline == DateTime.MinValue) continue;
                if ((now - quarry.Value.lastOwnerOnline).TotalDays > config.offlineQuarryOwnerKickTime)
                {
                    data.quarries[quarry.Key].authPlayers.Clear();
                    cleared++;
                }
            }
            if (cleared > 0)
                Puts($"Cleared authorization in {cleared} quarries due to long inactivity of the quarry owner.");
        }

        
        #endregion
        
        #region Static Quarries

        private static readonly Dictionary<MiningQuarry.QuarryType, int> convertType = new Dictionary<MiningQuarry.QuarryType, int>()
        {
            { MiningQuarry.QuarryType.None, 0 },
            { MiningQuarry.QuarryType.Basic, 0 },
            { MiningQuarry.QuarryType.Sulfur, 1 },
            { MiningQuarry.QuarryType.HQM, 2 }
        };
        
        //0 - Metal, 1 - Sulfur, 2 - HQM, 3 - Pump Jack, 4 - Excavator 
        private static bool IsStaticResourceOutput(Item item, int quarryType)
        {
            if (item.GetOwnerPlayer()) return false;
            switch (quarryType)
            {
                case 0:
                    foreach (var res in config.staticMetalOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 1:
                    foreach (var res in config.staticSulfurOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 2:
                    foreach (var res in config.staticHqmOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 3:
                    foreach (var res in config.staticPumpJackOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 4:
                    foreach (var res in config.excavatorResources)
                        foreach (var res2 in res.Value)
                            if (item.info.shortname == res2.shortname && item.skin == res2.skin) return true;
                    break;
            }
            return false;
        }

        private static bool IsStaticFuel(Item item)
        {
            if (item.info.shortname == config.staticFuelItem.shortname && item.skin == config.staticFuelItem.skin) return true;
            else return false;
        }
        
        #endregion
        
        #region Excavator Quarries

        private static bool IsExcavatorFuel(Item item)
        {
            if (item.info.shortname == config.excavatorFuelItem.shortname && item.skin == config.excavatorFuelItem.skin) return true;
            else return false;
        }

        private static void CheckForNewFuel(Item item, bool added, BoxStorage storage)
        {
            if (!added) return; 
            VirtualQuarry vq = storage.GetComponent<VirtualQuarry>();
            vq?.SwitchEngine(false);
        }
        
        #endregion

        private static bool TakeResources(BasePlayer player, List<RequiredItem> items)
        {
			List<RequiredItem> fixedList = Pool.Get<List<RequiredItem>>();
			fixedList.Clear();
			foreach (var item1 in items)
			{
				bool found = false;
				foreach (var item2 in fixedList)
				{
					if (item1.skin == item2.skin && item1.shortname == item2.shortname)
					{
						item2.amount += item1.amount;
						found = true;
						break;
					} 
				}
				if (!found)
					fixedList.Add(new(item1));
			}
            foreach (var requiredItem in fixedList)
            {
                bool haveRequired = false;
                int inventoryAmount = 0;
                foreach (var item in player.inventory.containerMain.itemList)
                {
                    if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                    {
                        inventoryAmount += item.amount;
                        if (inventoryAmount >= requiredItem.amount)
                        {
                            haveRequired = true;
                            break;
                        }
                    }
                }
                if (!haveRequired)
                {
                    foreach (var item in player.inventory.containerBelt.itemList)
                    {
                        if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                        {
                            inventoryAmount += item.amount;
                            if (inventoryAmount >= requiredItem.amount)
                            {
                                haveRequired = true;
                                break;
                            }
                        }
                    }
                }
                if (!haveRequired)
				{
					Pool.FreeUnmanaged(ref fixedList);
                    return false;
				}
            }
            foreach (var requiredItem in fixedList)
            {
                int takenItems = 0;
                List<Item> invItems = Pool.Get<List<Item>>();
                invItems.AddRange(player.inventory.containerMain.itemList);
                invItems.AddRange(player.inventory.containerBelt.itemList);
                foreach (var item in invItems)
                {
                    if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                    {
                        if (takenItems < requiredItem.amount)
                        {
                            if (item.amount > requiredItem.amount - takenItems)
                            {
                                item.amount -= requiredItem.amount - takenItems;
                                item.MarkDirty();
                                break;
                            }
                            if (item.amount <= requiredItem.amount - takenItems)
                            {
                                takenItems += item.amount;
                                item.GetHeldEntity()?.Kill();
                                item.Remove();
                            }
                        }
                        else break;
                    }
                }
                Pool.FreeUnmanaged(ref invItems);
            }
            Pool.FreeUnmanaged(ref fixedList);
            return true;
        }
        
        #endregion
        
        #region UI

        private void ShowQuarryMenuUI(BasePlayer player, int page = 1, int quarryId = -1)
        {
            if (config.requirePermission && !permission.UserHasPermission(player.UserIDString, "virtualquarries.use"))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermission", player.UserIDString));
                return;
            }
            CuiElementContainer container = new CuiElementContainer();
            UI_AddCorePanel(container, "VirtualQuarriesUI_Main", "Hud.Menu", "0.3 0.3 0.3 0.2", "0 0", "1 1", "0 0", "0 0");
            UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.4 0.4 0.4 0.3", "0 0", "1 1", "0 0", "0 0");
            UI_AddBackgroundPanel(container, "VirtualQuarriesUI_Main", "0 0 0 0.8", "0 0", "1 1", "0 0", "0 0");
            UI_AddButton(container, "VirtualQuarriesUI_Main", "0 0 0 0", "", TextAnchor.UpperCenter, 0, "0 0 0 0", "UI_VirtualQuarries close", "0 0", "1 1", "0 0", "0 0"); //Background Close Button
            UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.144 0.128 0.107 1", "0.5 0.5", "0.5 0.5", "-366 -161", "366 161"); //Background Panel
            UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-362 131", "362 157"); //Background Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-358 134", "-338 154", "assets/icons/gear.png"); //Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("VirtualQuarriesTitle", player.UserIDString), TextAnchor.MiddleLeft, 21, "0.5 0.5", "0.5 0.5", "-334 131", "336 157");
            UI_AddButton(container, "VirtualQuarriesUI_Main", "0.941 0.486 0.302 1", "✖", TextAnchor.MiddleCenter, 20, "0.45 0.237 0.194 1", "UI_VirtualQuarries close", "0.5 0.5", "0.5 0.5", "336 131", "362 157");
            UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.252 0.243 0.227 1", "0.5 0.5", "0.5 0.5", "-362 101", "-5 127"); //Background Quarry List Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-358 104", "-338 124", "assets/icons/clear_list.png"); //Quarry List Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("QuarryListTitle", player.UserIDString), TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "-334 101", "-5 127");
            UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.227 0.218 0.206 1", "0.5 0.5", "0.5 0.5", "-1 101", "362 127"); //Background Quarry Info Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "3 104", "23 124", "assets/icons/examine.png"); //Quarry Info Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("QuarryInfoTitle", player.UserIDString), TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "27 101", "362 127");
            int offsetX = -362;
            int offsetY = 56;
            int allCount = 0;
            int count = 0;
            bool morePages = false;
            if (cachedPage.TryGetValue(player.userID, out int value))
                page = value;
            foreach (var quarry in data.quarries)
            {
                if (quarry.Value.owner != player.userID && !quarry.Value.authPlayers.Contains(player.userID)) continue;
                if (cachedSort.ContainsKey(player.userID))
                {
                    if (cachedSort[player.userID] == "owned" && quarry.Value.owner != player.userID) continue;
                    if (cachedSort[player.userID] == "shared" && quarry.Value.owner == player.userID) continue;
                }
                if (!config.quarryProfiles.ContainsKey(quarry.Value.profile))
                {
                    Puts($"Profile {quarry.Value.profile} is missing from the configuration, but is found in data. Quarry with ID {quarry.Key} will not work!");
                    continue;
                }
                if (allCount < (40 * page) - 40)
                {
                    allCount++;
                    continue;
                }
                float alpha = 0.05f;
                if (quarryId != -1 && quarry.Key == quarryId)
                    alpha = 0.1f;
                if (quarry.Value.isRunning)
                    UI_AddButton(container, "VirtualQuarriesUI_Main", $"0.733 0.851 0.533 {alpha}", "", TextAnchor.MiddleCenter, 10, $"0.733 0.851 0.533 {alpha}", $"UI_VirtualQuarries view {page} {quarry.Key}", "0.5 0.5", "0.5 0.5", $"{offsetX} {offsetY}", $"{offsetX + 41} {offsetY + 41}", "VirtualQuarriesUI_Button");
                else
                    UI_AddButton(container, "VirtualQuarriesUI_Main", $"0.941 0.486 0.302 {alpha}", "", TextAnchor.MiddleCenter, 10, $"0.941 0.486 0.302 {alpha}", $"UI_VirtualQuarries view {page} {quarry.Key}", "0.5 0.5", "0.5 0.5", $"{offsetX} {offsetY}", $"{offsetX + 41} {offsetY + 41}", "VirtualQuarriesUI_Button");
                QuarryProfile qp = config.quarryProfiles[quarry.Value.profile];
                UI_AddItemImage(container, "VirtualQuarriesUI_Button", qp.icon.shortname, qp.icon.skin, "0.5 0.5", "0.5 0.5", "-16 -16", "16 16"); //Quarry Icon
                if (quarry.Value.authPlayers.Contains(player.userID))
                    UI_AddImage(container, "VirtualQuarriesUI_Button", "https://images.pvrust.eu/ui_icons/VirtualQuarries/shared_0.png", "1 0", "1 0", "-12 0", "-1 11", "0.733 0.851 0.533 1"); //Quarry Shared Icon
                if (qp.enableUpgrades)
                    UI_AddBoldText(container, "VirtualQuarriesUI_Button", "0.729 0.694 0.659 0.8", $"{quarry.Value.level + 1}", TextAnchor.MiddleCenter, 26, "0.5 0.5", "0.5 0.5", "-18 -18", "18 18"); //Quarry Level
                offsetX += 45;
                count++;
                if (count % 8 == 0)
                {
                    offsetY -= 45;
                    offsetX = -362;
                }
                if (count > 39)
                    morePages = true;
                if (count >= 40) break;
            }
            if (count < 40)
                UI_AddButton(container, "VirtualQuarriesUI_Main", "0.733 0.851 0.533 1", "＋", TextAnchor.MiddleCenter, 33, "0.439 0.538 0.261 1", "UI_VirtualQuarries new", "0.5 0.5", "0.5 0.5", $"{offsetX} {offsetY}", $"{offsetX + 41} {offsetY + 41}");
            if (page > 1)
            {
                UI_AddButton(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", "", TextAnchor.MiddleCenter, 9, "0.227 0.218 0.206 1", $"UI_VirtualQuarries page {page - 1}", "0.5 0.5", "0.5 0.5", "-182 -138", "-96 -128", "VirtualQuarriesUI_Button");
                UI_AddImage(container, "VirtualQuarriesUI_Button", "https://images.pvrust.eu/ui_icons/VirtualQuarries/arrow_left_0.png", "0.5 0.5", "0.5 0.5", "-9 -9", "9 9", "0.729 0.694 0.659 1"); //Arrow Left Icon
            }
            if (morePages)
            {
                UI_AddButton(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", "", TextAnchor.MiddleCenter, 9, "0.227 0.218 0.206 1", $"UI_VirtualQuarries page {page + 1}", "0.5 0.5", "0.5 0.5", "-92 -138", "-6 -128", "VirtualQuarriesUI_Button");
                UI_AddImage(container, "VirtualQuarriesUI_Button", "https://images.pvrust.eu/ui_icons/VirtualQuarries/arrow_right_0.png", "0.5 0.5", "0.5 0.5", "-9 -9", "9 9", "0.729 0.694 0.659 1"); //Arrow Right Icon
            }
            UI_AddButton(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("SortOwnedButton", player.UserIDString), TextAnchor.MiddleCenter, 11, "0.227 0.218 0.206 1", "UI_VirtualQuarries sortOwned", "0.5 0.5", "0.5 0.5", "-362 -157", "-276 -142");
            UI_AddButton(container, "VirtualQuarriesUI_Main", "0.733 0.851 0.533 1", Lang("SortSharedButton", player.UserIDString), TextAnchor.MiddleCenter, 11, "0.439 0.538 0.261 1", "UI_VirtualQuarries sortShared", "0.5 0.5", "0.5 0.5", "-272 -157", "-186 -142");
            UI_AddButton(container, "VirtualQuarriesUI_Main", "0.941 0.486 0.302 1", Lang("DisableSortButton", player.UserIDString), TextAnchor.MiddleCenter, 11, "0.45 0.237 0.194 1", "UI_VirtualQuarries sortDisable", "0.5 0.5", "0.5 0.5", "-182 -157", "-96 -142");
            if (!config.sharingRequirePermission || (config.sharingRequirePermission && permission.UserHasPermission(player.UserIDString, "virtualquarries.share")))
                UI_AddButton(container, "VirtualQuarriesUI_Main", "0.733 0.851 0.533 1", Lang("AddToAllButton", player.UserIDString), TextAnchor.MiddleCenter, 11, "0.439 0.538 0.261 1", "UI_VirtualQuarries action addAll", "0.5 0.5", "0.5 0.5", "-92 -157", "-6 -142");
            if (quarryId != -1 && (data.quarries[quarryId].owner == player.userID || data.quarries[quarryId].authPlayers.Contains(player.userID)))
            {
                QuarryData quarry = data.quarries[quarryId];
                Dictionary<string, ResourceConfig> quarryResources = config.quarryProfiles[quarry.profile].resources;
                List<UpgradeConfig> upgrades = config.quarryProfiles[quarry.profile].upgrades;
                string title = Lang(config.quarryProfiles[quarry.profile].titleTranslation, player.UserIDString);
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-1 -37", "133 97"); //Quarry Icon Background
                UI_AddItemImage(container, "VirtualQuarriesUI_Main", config.quarryProfiles[quarry.profile].icon.shortname, config.quarryProfiles[quarry.profile].icon.skin, "0.5 0.5", "0.5 0.5", "19 -17", "113 77"); //Quarry Icon
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "137 71", "362 97"); //Quarry Title Background
                UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", title, TextAnchor.MiddleCenter, 16, "0.5 0.5", "0.5 0.5", "137 71", "362 97");
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "137 -37", "362 67"); //Quarry Output Background
                offsetX = 145;
                offsetY = 65;
                count = 0;
                foreach (var resource in quarry.resources)
                {
                    if (!config.quarryProfiles[quarry.profile].resources.ContainsKey(resource.configKey))
                    {
                        Puts($"Resource {resource.configKey} is missing from the configuration, but is found in data. Quarry with ID {quarryId} will not work properly!");
                        continue;
                    }
                    UI_AddItemImage(container, "VirtualQuarriesUI_Main", quarryResources[resource.configKey].shortname, quarryResources[resource.configKey].skin, "0.5 0.5", "0.5 0.5", $"{offsetX} {offsetY - 20}", $"{offsetX + 20} {offsetY}"); //Resource Icon
                    float outputAmount = 60f / config.quarryTick * resource.work * upgrades[quarry.level].multiplier;
                    string perMinute = outputAmount < 10 ? $"{outputAmount:0.###}/m" : $"{outputAmount:0.##}/m";
                    UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", perMinute, TextAnchor.MiddleLeft, 14, "0.5 0.5", "0.5 0.5", $"{offsetX + 24} {offsetY - 20}", $"{offsetX + 90} {offsetY}");
                    offsetY -= 16;
                    count++;
                    if (count % 6 == 0)
                    {
                        offsetY = 65;
                        offsetX += 113;
                    }
                    if (count >= 12) break;
                }
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-1 -67", "281 -41"); //Change Engine Status Background
                UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("ChangeEngineStatusTitle", player.UserIDString), TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "5 -67", "281 -41");
                if (quarry.isRunning)
                    UI_AddButton(container, "VirtualQuarriesUI_Main", "0.941 0.486 0.302 1", Lang("Off", player.UserIDString), TextAnchor.MiddleCenter, 16, "0.45 0.237 0.194 1", $"UI_VirtualQuarries engine {quarryId}", "0.5 0.5", "0.5 0.5", "285 -67", "362 -41");
                else
                    UI_AddButton(container, "VirtualQuarriesUI_Main", "0.733 0.851 0.533 1", Lang("On", player.UserIDString), TextAnchor.MiddleCenter, 16, "0.439 0.538 0.261 1", $"UI_VirtualQuarries engine {quarryId}", "0.5 0.5", "0.5 0.5", "285 -67", "362 -41");
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-1 -97", "281 -71"); //Fuel Storage Background
                UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("OpenFuelTitle", player.UserIDString), TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "5 -97", "281 -71");
                UI_AddButton(container, "VirtualQuarriesUI_Main", "0.252 0.243 0.227 1", "", TextAnchor.MiddleCenter, 10, "0.252 0.243 0.227 1", $"UI_VirtualQuarries fuel {quarryId}", "0.5 0.5", "0.5 0.5", "285 -97", "362 -71", "VirtualQuarriesUI_Button");
                UI_AddIcon(container, "VirtualQuarriesUI_Button", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-8 -8", "8 8", "assets/icons/bleeding.png"); //Fuel Storage Open Icon
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-1 -127", "281 -101"); //Resource Storage Background
                UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("OpenResourceTitle", player.UserIDString), TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "5 -127", "281 -101");
                UI_AddButton(container, "VirtualQuarriesUI_Main", "0.252 0.243 0.227 1", "", TextAnchor.MiddleCenter, 10, "0.252 0.243 0.227 1", $"UI_VirtualQuarries storage {quarryId}", "0.5 0.5", "0.5 0.5", "285 -127", "362 -101", "VirtualQuarriesUI_Button");
                UI_AddIcon(container, "VirtualQuarriesUI_Button", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-8 -8", "8 8", "assets/icons/open.png"); //Resource Storage Open Icon
                if (quarry.owner == player.userID)
                {
                    if (!config.sharingRequirePermission || (config.sharingRequirePermission && permission.UserHasPermission(player.UserIDString, "virtualquarries.share")))
                    {
                        UI_AddButton(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("GiveAccessButton", player.UserIDString), TextAnchor.MiddleCenter, 12, "0.227 0.218 0.206 1", $"UI_VirtualQuarries action add {quarryId}", "0.5 0.5", "0.5 0.5", "-1 -157", "90 -131");
                        UI_AddButton(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("RemoveAccessButton", player.UserIDString), TextAnchor.MiddleCenter, 12, "0.227 0.218 0.206 1", $"UI_VirtualQuarries action remove {quarryId}", "0.5 0.5", "0.5 0.5", "94 -157", "186 -131");
                        UI_AddButton(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("AccessListButton", player.UserIDString), TextAnchor.MiddleCenter, 12, "0.227 0.218 0.206 1", $"UI_VirtualQuarries list {quarryId}", "0.5 0.5", "0.5 0.5", "190 -157", "281 -131");
                    }
                    else
                        UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.227 0.218 0.206 1", "0.5 0.5", "0.5 0.5", "-1 -157", "281 -131"); //Empty Panel
                    UI_AddButton(container, "VirtualQuarriesUI_Main", "0.941 0.486 0.302 1", "", TextAnchor.MiddleCenter, 10, "0.45 0.237 0.194 1", $"UI_VirtualQuarries delete {quarryId}", "0.5 0.5", "0.5 0.5", "285 -157", "362 -131", "VirtualQuarriesUI_Button");
                    UI_AddIcon(container, "VirtualQuarriesUI_Button", "0.941 0.486 0.302 1", "0.5 0.5", "0.5 0.5", "-8 -8", "8 8", "assets/icons/clear.png"); //Delete Quarry Icon
                }
                else
                {
                    UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.227 0.218 0.206 1", "0.5 0.5", "0.5 0.5", "-1 -157", "133 -131"); //Quarry Owner Title Background
                    UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.729 0.694 0.659 1", Lang("OwnerTitle", player.UserIDString), TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "5 -157", "133 -131");
                    UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.227 0.218 0.206 1", "0.5 0.5", "0.5 0.5", "137 -157", "362 -131"); //Quarry Owner Background
                    UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.733 0.851 0.533 1", playerCache[quarry.owner], TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "143 -157", "362 -131");
                }
            }
            else
            {
                UI_AddPanel(container, "VirtualQuarriesUI_Main", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-1 -157", "362 97");
                Dictionary<string, int> playerPerm = new Dictionary<string, int>();
                foreach (var configPerm in config.permissions)
                    if (permission.UserHasPermission(player.UserIDString, configPerm.Key))
                        playerPerm = configPerm.Value;
                string text = Lang("SelectQuarryHint", player.UserIDString);
                foreach (var perm in playerPerm)
                {
                    if (perm.Key == "*")
                        text += Lang("PermTranslation", player.UserIDString, Lang("AllQuarries", player.UserIDString), perm.Value);
                    else if (config.quarryProfiles[perm.Key].permission == "" || permission.UserHasPermission(player.UserIDString, config.quarryProfiles[perm.Key].permission))
                        text += Lang("PermTranslation", player.UserIDString, Lang(config.quarryProfiles[perm.Key].titleTranslation, player.UserIDString), perm.Value);
                }
                if (text == Lang("SelectQuarryHint", player.UserIDString))
                    text = Lang("OnlyAccessHint", player.UserIDString);
                UI_AddBoldText(container, "VirtualQuarriesUI_Main", "0.829 0.794 0.759 1", text, TextAnchor.MiddleCenter, config.listedQuarriesFontSize, "0.5 0.5", "0.5 0.5", "29 -157", "332 97");
            }
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
            CuiHelper.AddUi(player, container);
        }

        private void SelectSurveyTypeUI(BasePlayer player)
        {
            int count = 0;
            foreach (var survey in config.surveys)
            {
                if (survey.Value.permission != "" && !permission.UserHasPermission(player.UserIDString, survey.Value.permission)) continue;
                count++;
            }
            if (count == 0)
            {
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
                ShowQuarryMenuUI(player);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionToSurvey", player.UserIDString));
            }
            else if (count == 1)
            {
                foreach (var survey in config.surveys)
                {
                    if (survey.Value.permission != "" && !permission.UserHasPermission(player.UserIDString, survey.Value.permission)) continue;
                    player.Command($"UI_VirtualQuarries throw_open {survey.Key}");
                    return;
                }
            }
            CuiElementContainer container = new CuiElementContainer();
            UI_AddCorePanel(container, "VirtualQuarriesUI_New", "Hud.Menu", "0.3 0.3 0.3 0.2", "0 0", "1 1", "0 0", "0 0");
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.4 0.4 0.4 0.3", "0 0", "1 1", "0 0", "0 0");
            UI_AddBackgroundPanel(container, "VirtualQuarriesUI_New", "0 0 0 0.8", "0 0", "1 1", "0 0", "0 0");
            UI_AddButton(container, "VirtualQuarriesUI_New", "0 0 0 0", "", TextAnchor.UpperCenter, 0, "0 0 0 0", "UI_VirtualQuarries close", "0 0", "1 1", "0 0", "0 0"); //Background Close Button
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.144 0.128 0.107 1", "0.5 0.5", "0.5 0.5", "-222 -30", "223 100"); //Background Panel
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-218 71", "219 96"); //Background Title Panel 
            UI_AddIcon(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-214 74", "-195 93", "assets/icons/gear.png"); //Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("VirtualQuarriesTitle", player.UserIDString), TextAnchor.MiddleLeft, 21, "0.5 0.5", "0.5 0.5", "-191 71", "219 96");
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.252 0.243 0.227 1", "0.5 0.5", "0.5 0.5", "-218 47", "219 67"); //Background Output Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-214 49", "-198 65", "assets/icons/clear_list.png"); //Quarry List Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("SelectSurveyTypeTitle", player.UserIDString), TextAnchor.MiddleLeft, 14, "0.5 0.5", "0.5 0.5", "-194 47", "219 67");
            int offset = -218;
            for (int i = 0; i < 9; i++)
            {
                UI_AddPanel(container, "VirtualQuarriesUI_New", "0.733 0.851 0.533 0.05", "0.5 0.5", "0.5 0.5", $"{offset} -2", $"{offset + 45} 43"); //Background Output Item Tile Panel
                offset += 49;
            }
            offset = -218;
            foreach (var survey in config.surveys)
            {
                if (survey.Value.permission != "" && !permission.UserHasPermission(player.UserIDString, survey.Value.permission)) continue;
                UI_AddItemImage(container, "VirtualQuarriesUI_New", survey.Value.surveyItem.shortname, survey.Value.surveyItem.skin, "0.5 0.5", "0.5 0.5", $"{offset + 5} 3", $"{offset + 40} 38"); //Survey Item Icon
                UI_AddButton(container, "VirtualQuarriesUI_New", "0 0 0 0", "", TextAnchor.UpperCenter, 0, "0 0 0 0", $"UI_VirtualQuarries throw_open {survey.Key}", "0.5 0.5", "0.5 0.5", $"{offset + 5} 3", $"{offset + 40} 38"); //Select Survey Button
                offset += 49;
            }
            UI_AddButton(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("GoBackButton", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.227 0.218 0.206 1", "UI_VirtualQuarries back", "0.5 0.5", "0.5 0.5", "-41 -26", "50 -6");
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
            CuiHelper.AddUi(player, container);
        }

        private void ShowNewQuarryUI(BasePlayer player, bool firstJoin = false)
        {
            CuiElementContainer container = new CuiElementContainer();
            if (firstJoin)
                UI_AddCorePanel(container, "VirtualQuarriesUI_NewCursor", "Hud.Menu", "0 0 0 0", "0 0", "1 1", "0 0", "0 0");
            UI_AddCorePanel(container, "VirtualQuarriesUI_New", "VirtualQuarriesUI_NewCursor", "0.3 0.3 0.3 0.2", "0 0", "1 1", "0 0", "0 0");
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.4 0.4 0.4 0.3", "0 0", "1 1", "0 0", "0 0");
            UI_AddBackgroundPanel(container, "VirtualQuarriesUI_New", "0 0 0 0.8", "0 0", "1 1", "0 0", "0 0");
            UI_AddButton(container, "VirtualQuarriesUI_New", "0 0 0 0", "", TextAnchor.UpperCenter, 0, "0 0 0 0", "UI_VirtualQuarries close", "0 0", "1 1", "0 0", "0 0"); //Background Close Button
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.144 0.128 0.107 1", "0.5 0.5", "0.5 0.5", "-222 -103", "223 100"); //Background Panel
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-218 71", "219 96"); //Background Title Panel 
            UI_AddIcon(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-214 74", "-195 93", "assets/icons/gear.png"); //Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("VirtualQuarriesTitle", player.UserIDString), TextAnchor.MiddleLeft, 21, "0.5 0.5", "0.5 0.5", "-191 71", "219 96");
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.252 0.243 0.227 1", "0.5 0.5", "0.5 0.5", "-218 47", "219 67"); //Background Output Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-214 49", "-198 65", "assets/icons/clear_list.png"); //Quarry List Title Panel Icon
            bool isCached = cachedSurveys.TryGetValue(player.userID, out var survey);
            if (isCached)
                UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("RequiredSurveyTitle", player.UserIDString, Lang(config.surveys[cachedSurveyType[player.userID]].surveyTranslation, player.UserIDString)), TextAnchor.MiddleLeft, 14, "0.5 0.5", "0.5 0.5", "-194 47", "219 67");
            else
            {
				survey = new CachedSurvey();
                if (survey.resources.Count == 0)
                    UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("NoResourcesFoundTitle", player.UserIDString, Lang(config.surveys[cachedSurveyType[player.userID]].surveyTranslation, player.UserIDString)), TextAnchor.MiddleLeft, 14, "0.5 0.5", "0.5 0.5", "-194 47", "219 67");
                else
                    UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("CurrentResourcesTitle", player.UserIDString, Lang(config.surveys[cachedSurveyType[player.userID]].surveyTranslation, player.UserIDString)), TextAnchor.MiddleLeft, 14, "0.5 0.5", "0.5 0.5", "-194 47", "219 67");
            }
            int offset = -218;
            Dictionary<string, ResourceConfig> resources = new Dictionary<string, ResourceConfig>();
            if (isCached && config.quarryProfiles.ContainsKey(survey.profile))
                resources = config.quarryProfiles[survey.profile].resources;
            for (int i = 0; i < 9; i++)
            {
                UI_AddPanel(container, "VirtualQuarriesUI_New", "0.733 0.851 0.533 0.05", "0.5 0.5", "0.5 0.5", $"{offset} -2", $"{offset + 45} 43"); //Background Output Item Tile Panel
                if (isCached && i < survey.resources.Count)
                {
                    UI_AddItemImage(container, "VirtualQuarriesUI_New", resources[survey.resources[i].configKey].shortname, resources[survey.resources[i].configKey].skin, "0.5 0.5", "0.5 0.5", $"{offset + 5} 3", $"{offset + 40} 38"); //Output Item Icon
                    float outputAmount = 60f / config.quarryTick * survey.resources[i].work;
                    string perMinute = outputAmount < 10 ? $"{outputAmount:0.###}/m" : $"{outputAmount:0.##}/m";
                    UI_AddText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", perMinute, TextAnchor.LowerLeft, 10, "0.5 0.5", "0.5 0.5", $"{offset + 2} 0", $"{offset + 43} 41"); //Output Item Amount
                }
                offset += 49;
            }
            UI_AddPanel(container, "VirtualQuarriesUI_New", "0.252 0.243 0.227 1", "0.5 0.5", "0.5 0.5", "-218 -26", "219 -6"); //Background Required Items Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-214 -24", "-198 -8", "assets/icons/examine.png"); //Required Items Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("RequiredItemsTitle", player.UserIDString), TextAnchor.MiddleLeft, 14, "0.5 0.5", "0.5 0.5", "-194 -26", "219 -6");
            offset = -218;
            for (int i = 0; i < 9; i++)
            {
                UI_AddPanel(container, "VirtualQuarriesUI_New", "0.733 0.851 0.533 0.05", "0.5 0.5", "0.5 0.5", $"{offset} -75", $"{offset + 45} -30"); //Background Required Item Tile Panel
                if (cachedRequirements.ContainsKey(player.userID) && i < cachedRequirements[player.userID].Count)
                {
                    UI_AddItemImage(container, "VirtualQuarriesUI_New", cachedRequirements[player.userID][i].shortname, cachedRequirements[player.userID][i].skin, "0.5 0.5", "0.5 0.5", $"{offset + 5} -70", $"{offset + 40} -35"); //Required Item Icon
                    UI_AddText(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", $"x{cachedRequirements[player.userID][i].amount}", TextAnchor.LowerLeft, 10, "0.5 0.5", "0.5 0.5", $"{offset + 2} -73", $"{offset + 43} -32"); //Required Item Amount
                }
                offset += 49;
            }
            if (isCached && survey.resources.Count > 0)
                UI_AddButton(container, "VirtualQuarriesUI_New", "0.733 0.851 0.533 1", Lang("PlaceButton", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.439 0.538 0.261 1", "UI_VirtualQuarries place", "0.5 0.5", "0.5 0.5", "-136 -99", "-45 -79");
            UI_AddButton(container, "VirtualQuarriesUI_New", "0.941 0.486 0.302 1", Lang("ThrowButton", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.45 0.237 0.194 1", "UI_VirtualQuarries throw", "0.5 0.5", "0.5 0.5", "-41 -99", "50 -79");
            UI_AddButton(container, "VirtualQuarriesUI_New", "0.729 0.694 0.659 1", Lang("GoBackButton", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.227 0.218 0.206 1", "UI_VirtualQuarries back", "0.5 0.5", "0.5 0.5", "54 -99", "145 -79");
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
            CuiHelper.AddUi(player, container);
        }

        private void ManageQuarryPlayersUI(BasePlayer player, string type, int quarryId = 0, int page = 1, string search = "")
        {
            CuiElementContainer container = new CuiElementContainer();
            UI_AddCorePanel(container, "VirtualQuarriesUI_Players", "Hud.Menu", "0.3 0.3 0.3 0.2", "0 0", "1 1", "0 0", "0 0");
            UI_AddPanel(container, "VirtualQuarriesUI_Players", "0.4 0.4 0.4 0.3", "0 0", "1 1", "0 0", "0 0");
            UI_AddBackgroundPanel(container, "VirtualQuarriesUI_Players", "0 0 0 0.8", "0 0", "1 1", "0 0", "0 0");
            UI_AddButton(container, "VirtualQuarriesUI_Players", "0 0 0 0", "", TextAnchor.UpperCenter, 0, "0 0 0 0", "UI_VirtualQuarries close", "0 0", "1 1", "0 0", "0 0"); //Background Close Button
            UI_AddPanel(container, "VirtualQuarriesUI_Players", "0.144 0.128 0.107 1", "0.5 0.5", "0.5 0.5", "-368 -157", "368 153"); //Background Panel
            UI_AddPanel(container, "VirtualQuarriesUI_Players", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-364 123", "364 149"); //Background Title Panel
            UI_AddIcon(container, "VirtualQuarriesUI_Players", "0.729 0.694 0.659 1", "0.5 0.5", "0.5 0.5", "-360 126", "-340 146", "assets/icons/gear.png"); //Title Panel Icon
            UI_AddBoldText(container, "VirtualQuarriesUI_Players", "0.729 0.694 0.659 1", Lang("VirtualQuarriesTitle", player.UserIDString), TextAnchor.MiddleLeft, 21, "0.5 0.5", "0.5 0.5", "-336 123", "334 149");
            UI_AddButton(container, "VirtualQuarriesUI_Players", "0.941 0.486 0.302 1", "✖", TextAnchor.MiddleCenter, 20, "0.45 0.237 0.194 1", "UI_VirtualQuarries close", "0.5 0.5", "0.5 0.5", "338 123", "364 149");
            UI_AddPanel(container, "VirtualQuarriesUI_Players", "0.227 0.218 0.206 1", "0.5 0.5", "0.5 0.5", "-364 93", "364 119"); //Background Action Info Title Panel
            string titleMessage = Lang("AddPlayerTitle", player.UserIDString);
            Dictionary<ulong, string> values = new Dictionary<ulong, string>();
            switch (type)
            {
                case "add":
                    foreach (var user in BasePlayer.activePlayerList)
                        values.Add(user.userID, user.displayName);
                    break;
                case "add_offline":
                    titleMessage = Lang("AddOfflinePlayerTitle", player.UserIDString);
                    values = playerCache;
                    break;
                case "remove":
                    titleMessage = Lang("RemovePlayerTitle", player.UserIDString);
                    foreach (var user in data.quarries[quarryId].authPlayers)
                        values.TryAdd(user, playerCache[user]);
                    break;
                case "addAll":
                    titleMessage = Lang("AddAllPlayerTitle", player.UserIDString);
                    foreach (var user in BasePlayer.activePlayerList)
                        values.Add(user.userID, user.displayName);
                    break;
                case "addAll_offline":
                    titleMessage = Lang("AddAllOfflinePlayerTitle", player.UserIDString);
                    values = playerCache;
                    break;
            }
            UI_AddBoldText(container, "VirtualQuarriesUI_Players", "0.729 0.694 0.659 1", titleMessage, TextAnchor.MiddleLeft, 16, "0.5 0.5", "0.5 0.5", "-360 93", "360 119");
            UI_AddPanel(container, "VirtualQuarriesUI_Players", "0.187 0.179 0.172 1", "0.5 0.5", "0.5 0.5", "-146 65", "146 87"); //Background Search Panel
            UI_AddImage(container, "VirtualQuarriesUI_Players", "https://images.pvrust.eu/ui_icons/VirtualQuarries/search_0.png", "0.5 0.5", "0.5 0.5", "126 68", "142 84", "0.729 0.694 0.659 0.2"); //Search Icon
            container.Add(new CuiElement
            {
                Parent = "VirtualQuarriesUI_Players",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color =  "0.729 0.694 0.659 1",
                        IsPassword = false,
                        CharsLimit = 32,
                        Command = $"UI_VirtualQuarries action {type} {quarryId} {page}",
                        HudMenuInput = true
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-146 65",
                        OffsetMax = "146 87"
                    }
                }
            });
            int playerCount = 30 * page - 30;
            int counter = 0;
            int displayed = 0;
            int startX = -362;
            int startY = 59;
            bool morePages = false;
            foreach (var value in values)
            {
                if (value.Key == player.userID) continue;
                if (type.Contains("_offline") && BasePlayer.FindByID(value.Key)) continue;
                if (search != "" && !value.Value.ToLower().Contains(search.ToLower())) continue;
                if (counter < playerCount)
                {
                    counter++;
                    continue;
                }
                displayed++;
                UI_AddButton(container, "VirtualQuarriesUI_Players", "0.729 0.694 0.659 1", value.Value, TextAnchor.MiddleCenter, 13, "0.252 0.243 0.227 1", $"UI_VirtualQuarries doAction {type} {quarryId} {value.Key}", "0.5 0.5", "0.5 0.5", $"{startX} {startY - 26}", $"{startX + 140} {startY}");
                startX += 146;
                if (displayed % 5 == 0)
                {
                    startX = -362;
                    startY -= 32;
                }
                if (displayed >= 30)
                {
                    morePages = true;
                    break;
                }
            }
            UI_AddButton(container, "VirtualQuarriesUI_Players", "0.941 0.486 0.302 1", Lang("GoBackButton", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.45 0.237 0.194 1", $"UI_VirtualQuarries back", "0.5 0.5", "0.5 0.5", "-362 -153", "-271 -133");
            if (type == "add" || type == "addAll")
                UI_AddButton(container, "VirtualQuarriesUI_Players", "0.941 0.486 0.302 1", Lang("OfflinePlayersButton", player.UserIDString), TextAnchor.MiddleCenter, 11, "0.45 0.237 0.194 1", $"UI_VirtualQuarries action {type}_offline {quarryId}", "0.5 0.5", "0.5 0.5", "-267 -153", "-176 -133");
            if (page > 1)
            {
                UI_AddButton(container, "VirtualQuarriesUI_Players", "0.729 0.694 0.659 1", "", TextAnchor.MiddleCenter, 9, "0.439 0.538 0.261 1", $"UI_VirtualQuarries action {type} {quarryId} {page - 1} {search}", "0.5 0.5", "0.5 0.5", "176 -153", "267 -133", "VirtualQuarriesUI_Button");
                UI_AddImage(container, "VirtualQuarriesUI_Button", "https://images.pvrust.eu/ui_icons/VirtualQuarries/arrow_left_0.png", "0.5 0.5", "0.5 0.5", "-12 -12", "12 12", "0.733 0.851 0.533 1"); //Arrow Left Icon
            }
            if (morePages)
            {
                UI_AddButton(container, "VirtualQuarriesUI_Players", "0.729 0.694 0.659 1", "", TextAnchor.MiddleCenter, 9, "0.439 0.538 0.261 1", $"UI_VirtualQuarries action {type} {quarryId} {page + 1} {search}", "0.5 0.5", "0.5 0.5", "271 -153", "362 -133", "VirtualQuarriesUI_Button");
                UI_AddImage(container, "VirtualQuarriesUI_Button", "https://images.pvrust.eu/ui_icons/VirtualQuarries/arrow_right_0.png", "0.5 0.5", "0.5 0.5", "-12 -12", "12 12", "0.733 0.851 0.533 1"); //Arrow Right Icon
            }
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Players");
            CuiHelper.AddUi(player, container);
        }

        private void AddQuarryLevelsUI(BasePlayer player, int quarryId)
        {
            QuarryData quarry = data.quarries[quarryId];
            if (!config.quarryProfiles[quarry.profile].enableUpgrades) return;
            var container = new CuiElementContainer();
            int cornerX = 192;
            int cornerY = 227;
            List<UpgradeConfig> upgrades = config.quarryProfiles[quarry.profile].upgrades;
            int level = quarry.level;
            int rows = Mathf.CeilToInt((upgrades[level].capacity + (upgrades[level].capacity % 6)) / 6f);
            cornerY += 64 * rows - 64;
            if (!config.responsiveUpgrade || rows >= 5)
            {
                cornerX = -198;
                cornerY = 456;
            }
            UI_AddCorePanel(container, "VirtualQuarriesUI_Levels", "Hud.Menu", "0 0 0 0", "0.5 0", "0.5 0", "0 0", "0 0");
            UI_AddCorePanel(container, "VirtualQuarriesUI_LevelsBackground", "VirtualQuarriesUI_Levels", "0 0 0 0.15", "0.5 0", "0.5 0", $"{cornerX} {cornerY}", $"{cornerX + 380} {cornerY + 170}");
            UI_AddPanel(container, "VirtualQuarriesUI_Levels", "0.61 0.6 0.58 0.09", "0.5 0", "0.5 0", $"{cornerX} {cornerY}", $"{cornerX + 380} {cornerY + 170}");
            UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("UpgradesTitle", player.UserIDString), TextAnchor.MiddleLeft, 20, "0.5 0", "0.5 0", $"{cornerX + 1} {cornerY + 168}", $"{cornerX + 380} {cornerY + 201}");
            bool maxLevel = level >= upgrades.Count - 1;
            if (maxLevel)
                UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("NowTitle", player.UserIDString), TextAnchor.MiddleCenter, 15, "0.5 0", "0.5 0", $"{cornerX + 194} {cornerY + 170}", $"{cornerX + 380} {cornerY + 203}");
            else
            {
                UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("NowTitle", player.UserIDString), TextAnchor.MiddleCenter, 15, "0.5 0", "0.5 0", $"{cornerX + 194} {cornerY + 170}", $"{cornerX + 287} {cornerY + 198}");
                UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("AfterTitle", player.UserIDString), TextAnchor.MiddleCenter, 15, "0.5 0", "0.5 0", $"{cornerX + 287} {cornerY + 170}", $"{cornerX + 380} {cornerY + 198}");
            }
            UI_AddPanel(container, "VirtualQuarriesUI_Levels", "0.61 0.6 0.58 0.05", "0.5 0", "0.5 0", $"{cornerX + 0} {cornerY + 90}", $"{cornerX + 380} {cornerY + 170}");
            UI_AddText(container, "VirtualQuarriesUI_Levels", "0.75 0.71 0.67", Lang("UpgradeInfo", player.UserIDString), TextAnchor.MiddleRight, 13, "0.5 0", "0.5 0", $"{cornerX + 0} {cornerY + 90}", $"{cornerX + 193} {cornerY + 170}");
            if (maxLevel)
                UI_AddBoldTextOutline(container, "VirtualQuarriesUI_Levels", "0.733 0.851 0.533", $"{level + 1}\n{upgrades[level].capacity}\nx{upgrades[level].multiplier}\nx{upgrades[level].fuelMultiplier}", TextAnchor.MiddleCenter, 13, "0.5 0", "0.5 0", $"{cornerX + 194} {cornerY + 90}", $"{cornerX + 380} {cornerY + 170}");
            else
            {
                UI_AddIcon(container, "VirtualQuarriesUI_Levels", "0.75 0.71 0.67", "0.5 0.5", "0.5 0.5", $"{cornerX + 281} {cornerY + 124}", $"{cornerX + 293} {cornerY + 136}", "assets/icons/chevron_right.png");
                UI_AddBoldTextOutline(container, "VirtualQuarriesUI_Levels", "0.941 0.486 0.302", $"{level + 1}\n{upgrades[level].capacity}\nx{upgrades[level].multiplier}\nx{upgrades[level].fuelMultiplier}", TextAnchor.MiddleCenter, 13, "0.5 0", "0.5 0", $"{cornerX + 194} {cornerY + 90}", $"{cornerX + 287} {cornerY + 170}");
                UI_AddBoldTextOutline(container, "VirtualQuarriesUI_Levels", "0.733 0.851 0.533", $"{level + 2}\n{upgrades[level + 1].capacity}\nx{upgrades[level + 1].multiplier}\nx{upgrades[level + 1].fuelMultiplier}", TextAnchor.MiddleCenter, 13, "0.5 0", "0.5 0", $"{cornerX + 287} {cornerY + 90}", $"{cornerX + 380} {cornerY + 170}");
            }
            UI_AddItemImage(container, "VirtualQuarriesUI_Levels", config.quarryProfiles[quarry.profile].icon.shortname, config.quarryProfiles[quarry.profile].icon.skin, "0.5 0", "0.5 0", $"{cornerX + 10} {cornerY + 100}", $"{cornerX + 70} {cornerY + 160}");
            if (maxLevel)
                UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("LevelMaxed", player.UserIDString), TextAnchor.MiddleCenter, 20, "0.5 0", "0.5 0", $"{cornerX + 0} {cornerY + 0}", $"{cornerX + 380} {cornerY + 90}");
            else
            {
                UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("RequiredItems", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.5 0", "0.5 0", $"{cornerX + 0} {cornerY + 35}", $"{cornerX + 193} {cornerY + 90}");
                int start = cornerX + 168;
                switch (upgrades[level + 1].requiredItems.Count)
                {
                    case 3:
                        start = cornerX + 218;
                        break;
                    case 2:
                        start = cornerX + 238;
                        break;
                    case 1:
                        start = cornerX + 263;
                        break;
                    default:
                        break;
                }
                foreach (var t in upgrades[level + 1].requiredItems)
                {
                    UI_AddItemImage(container, "VirtualQuarriesUI_Levels", t.shortname, t.skin, "0.5 0", "0.5 0", $"{start + 2} {cornerY + 40}", $"{start + 43} {cornerY + 81}");
                    UI_AddText(container, "VirtualQuarriesUI_Levels", "0.75 0.71 0.67", $"x{t.amount} ", TextAnchor.LowerRight, 11, "0.5 0", "0.5 0", $"{start} {cornerY + 38}", $"{start + 45} {cornerY + 83}");
                    start += 50;
                }
                int buttonStart = 29;
                int buttonEnd = 183;
                if (upgrades[level + 1].requiredRp == 0 || upgrades[level + 1].requiredItems.Count == 0)
                {
                    buttonStart = 79;
                    buttonEnd = 316;
                }
                UI_AddPanel(container, "VirtualQuarriesUI_Levels", "0.61 0.6 0.58 0.05", "0.5 0", "0.5 0", $"{cornerX + 0} {cornerY + 6}", $"{cornerX + 380} {cornerY + 35}");
                UI_AddIcon(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", "0.5 0.5", "0.5 0.5", $"{cornerX + 7} {cornerY + 13}", $"{cornerX + 22} {cornerY + 28}", "assets/icons/store.png");
                UI_AddButton(container, "VirtualQuarriesUI_Levels", "0.733 0.851 0.533 1", Lang("PurchaseItemsButton", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.439 0.538 0.261 1", $"UI_VirtualQuarries upgrade {quarryId} items", "0.5 0", "0.5 0", $"{cornerX + buttonStart} {cornerY + 6}", $"{cornerX + buttonEnd} {cornerY + 35}");
                if (upgrades[level + 1].requiredRp != 0)
                {
                    if (upgrades[level + 1].requiredItems.Count > 0)
                        UI_AddBoldText(container, "VirtualQuarriesUI_Levels", "0.91 0.87 0.83", Lang("Or", player.UserIDString), TextAnchor.MiddleCenter, 13, "0.5 0", "0.5 0", $"{cornerX + 183} {cornerY + 6}", $"{cornerX + 212} {cornerY + 35}");
                    if (upgrades[level + 1].requiredItems.Count == 0)
                        UI_AddButton(container, "VirtualQuarriesUI_Levels", "0.733 0.851 0.533 1", Lang("PurchaseCurrencyButton", player.UserIDString, upgrades[level + 1].requiredRp), TextAnchor.MiddleCenter, 13, "0.439 0.538 0.261 1", $"UI_VirtualQuarries upgrade {quarryId} currency", "0.5 0", "0.5 0", $"{cornerX + buttonStart} {cornerY + 6}", $"{cornerX + buttonEnd} {cornerY + 35}");
                    else
                        UI_AddButton(container, "VirtualQuarriesUI_Levels", "0.733 0.851 0.533 1", Lang("PurchaseCurrencyButton", player.UserIDString, upgrades[level + 1].requiredRp), TextAnchor.MiddleCenter, 13, "0.439 0.538 0.261 1", $"UI_VirtualQuarries upgrade {quarryId} currency", "0.5 0", "0.5 0", $"{cornerX + 212} {cornerY + 6}", $"{cornerX + 366} {cornerY + 35}");
                }
            }
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Levels");
            CuiHelper.AddUi(player, container);
        }

        private void AddBackButtonUI(BasePlayer player, int quarryId)
        {
            CuiElementContainer container = new CuiElementContainer();
            string pos1 = "200 20";
            string pos2 = "416 90";
            if (config.exitButtonLoc != 1)
            {
                pos1 = "570 110";
                pos2 = "627 167";
            }
            UI_AddCorePanel(container, "VirtualQuarriesUI_Back", "Hud.Menu", "0 0 0 0", "0.5 0", "0.5 0", "0 0", "0 0");
            UI_AddButton(container, "VirtualQuarriesUI_Back", "0.941 0.486 0.302 1", Lang("GoBackButton", player.UserIDString), TextAnchor.MiddleCenter, 21, "0.45 0.237 0.194 1", $"UI_VirtualQuarries back {quarryId}", "0.5 0.5", "0.5 0.5", pos1, pos2);
            CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Back");
            CuiHelper.AddUi(player, container);
        }

        private static void UI_AddCorePanel(CuiElementContainer container, string name, string parentName, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = color,
                        Material = "assets/content/ui/uibackgroundblur.mat"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    },
                    new CuiNeedsCursorComponent()
                }
            });
        }

        private static void UI_AddCorePanelNoCursor(CuiElementContainer container, string name, string parentName, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = color,
                        Material = "assets/content/ui/uibackgroundblur.mat"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }

        private static void UI_AddBackgroundPanel(CuiElementContainer container, string parentName, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = color,
                        Material = "assets/content/ui/namefontmaterial.mat",
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }

        private static void UI_AddPanel(CuiElementContainer container, string parentName, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = color,
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }

        private static void UI_AddBoldText(CuiElementContainer container, string parentName, string color, string text, TextAnchor textAnchor, int fontSize, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiTextComponent
                    {
                        Color = color,
                        Text = text,
                        Align = textAnchor,
                        FontSize = fontSize
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }

        private static void UI_AddBoldTextOutline(CuiElementContainer container, string parentName, string color, string text, TextAnchor textAnchor, int fontSize, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiTextComponent
                    {
                        Color = color,
                        Text = text,
                        Align = textAnchor,
                        FontSize = fontSize
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    },
                    new CuiOutlineComponent
                    {
                        Color = "0 0 0 1",
                        Distance = "0.3 0.3",
                    }
                }
            });
        }

        private static void UI_AddText(CuiElementContainer container, string parentName, string color, string text, TextAnchor textAnchor, int fontSize, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiTextComponent
                    {
                        Color = color,
                        Text = text,
                        Align = textAnchor,
                        FontSize = fontSize,
                        Font = "RobotoCondensed-Regular.ttf"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }

        private static void UI_AddButton(CuiElementContainer container, string parentName, string textColor, string text, TextAnchor textAnchor, int fontSize, string buttonColor, string command, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string buttonName = null)
        {
            container.Add(new CuiButton
            {
                Text =
                {
                    Color = textColor,
                    Text = text,
                    Align = textAnchor,
                    FontSize = fontSize
                },
                Button =
                {
                    Color = buttonColor,
                    Material = "assets/content/ui/namefontmaterial.mat",
                    Command = command,
                },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax,
                    OffsetMin = offsetMin,
                    OffsetMax = offsetMax,
                }
            }, parentName, buttonName);
        }

        private static void UI_AddIcon(CuiElementContainer container, string parentName, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string path)
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = color,
                        Sprite = path
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }

        private void UI_AddItemImage(CuiElementContainer container, string parentName, string shortname, ulong skin, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string color = "1 1 1 1")
        {
            ItemDefinition itemDef = ItemManager.FindItemDefinition(shortname);
            if (!itemDef)
            {
                PrintWarning($"Shortname {shortname} is not valid item shortname. Can't load image of this item.");
                return;
            }
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        ItemId = itemDef.itemid,
                        SkinId = skin,
                        Color = color,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                }
            });
        }

        private void UI_AddImage(CuiElementContainer container, string parentName, string url, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string color = "1 1 1 1")
        {
            container.Add(new CuiElement
            {
                Parent = parentName,
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = GetImage(url),
                        Color = color
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    }
                }
            });
        }
        
        #endregion

        private class VirtualQuarry : FacepunchBehaviour
        {
            
            //ALL QUARRIES
            private VqType quarryType = VqType.Default;
            private BoxStorage storage;
            private BoxStorage fuelStorage;
            private bool isRunning = false;
            private List<OutputInfo> output;
            private readonly Dictionary<string, float> nonIntOutput = new();
            
            //DEFAULT
            private int dataId = 0;
            private QuarryData qd;
            
            //STATIC
            private MiningQuarry staticQuarry = null;
            
            //EXCAVATOR
            private string mineType = "";

            private class OutputInfo
            {
                public string configKey;
                public string shortname;
                public ulong skin;
                public string name;
                public float amount;
            }

            public enum VqType
            {
                Default,
                Static,
                Excavator
            }

            private void Awake()
            {
                storage = GetComponent<BoxStorage>();
                fuelStorage = storage.children[0]?.GetComponent<BoxStorage>();
            }

            public void SetupQuarry(VqType type = VqType.Default, int id = 0, string resource = "", MiningQuarry linkedQuarry = null)
            {
                quarryType = type;
                dataId = id;
                mineType = resource;
                staticQuarry = linkedQuarry;
                CancelInvoke(MineResources);
                if (quarryType == VqType.Excavator && mineType != "")
                {
                    ConfigureOutput();
                    InvokeRepeating(MineResources, config.excavatorTick, config.excavatorTick);
                }
                else if (quarryType == VqType.Default)
                {
                    qd = data.quarries[dataId];
                    isRunning = qd.isRunning;
                    if (!config.quarryProfiles.ContainsKey(qd.profile))
                        _plugin.Puts($"Profile {qd.profile} is missing from the configuration, but is found in data. Quarry with ID {dataId} will not work!");
                    ConfigureOutput();
                    if (isRunning)
                        InvokeRepeating(MineResources, config.quarryTick, config.quarryTick);
                }
                else if (quarryType == VqType.Static)
                {
                    ConfigureOutput();
                    InvokeRepeating(MineResources, config.staticQuarryTick, config.staticQuarryTick);
                }
            }
            
            public void SwitchEngine(bool canDisable = true)
            {
                if (canDisable && isRunning)
                {
                    isRunning = false;
                    if (quarryType == VqType.Default)
                        data.quarries[dataId].isRunning = false;
                    CancelInvoke(MineResources);
                }
                else
                {
                    if (!CalculateFuel(false))
                    {
                        isRunning = false;
                        if (quarryType == VqType.Default)
                            data.quarries[dataId].isRunning = false;
                        CancelInvoke(MineResources);
                        return;
                    }
                    isRunning = true;
                    if (quarryType == VqType.Default)
                    {
                        data.quarries[dataId].isRunning = true;
                        InvokeRepeating(MineResources, config.quarryTick, config.quarryTick);
                    }
                    else if (quarryType == VqType.Static)
                        InvokeRepeating(MineResources, config.staticQuarryTick, config.staticQuarryTick);
                    else if (quarryType == VqType.Excavator && mineType != "")
                        InvokeRepeating(MineResources, config.excavatorTick, config.excavatorTick);
                }
            }
            
            public void SwitchExcavatorType(string type) 
			{
				mineType = type;
				ConfigureOutput();
			}

            private void ConfigureOutput()
            {
                output = new List<OutputInfo>();
                if (quarryType == VqType.Default)
                {
                    Dictionary<string, ResourceConfig> configResources = config.quarryProfiles[qd.profile].resources;
                    List<UpgradeConfig> levels = config.quarryProfiles[qd.profile].upgrades;
                    if (qd.level > levels.Count - 1)
                        qd.level = levels.Count - 1;
                    foreach (var resource in qd.resources)
                    {
                        if (!configResources.TryGetValue(resource.configKey, out var rc)) continue;
                        output.Add(new OutputInfo() { configKey = resource.configKey, shortname = rc.shortname, skin = rc.skin, name = rc.name, amount = resource.work * levels[qd.level].multiplier });
                    }
                }
                else if (quarryType == VqType.Static)
                {
                    List<StaticQuarryOutput> quarryOutput;
                    if (staticQuarry.ShortPrefabName == "pumpjack-static")
                    {
                        staticQuarry.staticType = MiningQuarry.QuarryType.Basic;
                        quarryOutput = config.staticPumpJackOutput;
                    }
                    else if (staticQuarry.staticType == MiningQuarry.QuarryType.Sulfur)
                        quarryOutput = config.staticSulfurOutput;
                    else if (staticQuarry.staticType == MiningQuarry.QuarryType.HQM)
                        quarryOutput = config.staticHqmOutput;
                    else if (staticQuarry.staticType == MiningQuarry.QuarryType.Basic)
                        quarryOutput = config.staticMetalOutput;
                    else
                        quarryOutput = new();
                    foreach (var resource in quarryOutput)
                        output.Add(new OutputInfo() { configKey = $"{resource.shortname}_{resource.skin}", shortname = resource.shortname, skin = resource.skin, name = resource.displayName, amount = resource.amount });
                }
                else if (quarryType == VqType.Excavator && mineType != "")
                    foreach (var resource in config.excavatorResources[mineType])
                        output.Add(new OutputInfo() { configKey = $"{resource.shortname}_{resource.skin}", shortname = resource.shortname, skin = resource.skin, name = resource.displayName, amount = resource.amount });
            }

            private void MineResources()
            {
                if (!CalculateFuel())
                {
                    isRunning = false;
                    if (quarryType == VqType.Default)
                        data.quarries[dataId].isRunning = false;
                    CancelInvoke(MineResources);
                    return;
                }
                foreach (var resource in output)
                {
                    nonIntOutput.TryAdd(resource.configKey, 0);
                    int amount = Mathf.FloorToInt(resource.amount);
                    nonIntOutput[resource.configKey] += resource.amount % 1;
                    if (nonIntOutput[resource.configKey] > 1)
                    {
                        nonIntOutput[resource.configKey]--;
                        amount++;
                    }
                    if (amount == 0) continue;
                    Item item = ItemManager.CreateByName(resource.shortname, amount, resource.skin);
                    if (!string.IsNullOrEmpty(resource.name))
                        item.name = resource.name;
                    if (!item.MoveToContainer(storage.inventory))
                    {
                        isRunning = false;
                        if (quarryType == VqType.Default)
                            data.quarries[dataId].isRunning = false;
                        CancelInvoke(MineResources);
                        return;
                    }
                }
            }

            private bool CalculateFuel(bool takeFuel = true)
            {
                if (!fuelStorage) return false;
                if (quarryType == VqType.Default && !config.quarryProfiles.ContainsKey(qd.profile))
                {
                    _plugin.Puts($"Profile {qd.profile} is missing from the configuration, but is found in data. Quarry with ID {dataId} will not work!");
                    return false;
                }

                FuelItem requiredFuel;
                if (quarryType == VqType.Default)
                    requiredFuel = config.quarryProfiles[qd.profile].fuelItem;
                else if (quarryType == VqType.Static)
                    requiredFuel = config.staticFuelItem;
                else
                    requiredFuel = config.excavatorFuelItem;
                float levelMultiplier = quarryType == VqType.Default ? config.quarryProfiles[qd.profile].upgrades[qd.level].fuelMultiplier : 1;
                nonIntOutput.TryAdd("fuel", 0);
                float notRoundedAmount = requiredFuel.amount * levelMultiplier;
                //if (takeFuel)
                //    nonIntOutput["fuel"] += (amount - notRoundedAmount) % 1;
				
                if (TakeFuel(fuelStorage, requiredFuel, notRoundedAmount, takeFuel)) return true;
                return false;
            }

            private bool TakeFuel(BoxStorage storage, FuelItem fuel, float notRoundedAmount, bool takeFuel = true)
            {
                if (nonIntOutput["fuel"] >= notRoundedAmount)
                {
                    if (takeFuel)
                        nonIntOutput["fuel"] -= notRoundedAmount;
                    return true;
                }
				int amount = Mathf.CeilToInt(notRoundedAmount);
                bool haveRequired = false;
                int inventoryAmount = 0;
                foreach (var item in storage.inventory.itemList)
                {
                    if (item.skin == fuel.skin && item.info.shortname == fuel.shortname)
                    {
                        inventoryAmount += item.amount;
                        if (inventoryAmount >= amount)
                        {
                            haveRequired = true;
                            break;
                        }
                    }
                }
                if (!haveRequired) return false;
                int takenItems = 0;
                if (takeFuel)
                    foreach (var item in storage.inventory.itemList.ToList())
                    {
                        if (item.skin == fuel.skin && item.info.shortname == fuel.shortname)
                        {
                            int needToTake = amount - takenItems;
                            if (needToTake <= 0) break;
                            if (item.amount > needToTake)
                            {
                                nonIntOutput["fuel"] += needToTake - notRoundedAmount;
                                item.amount -= needToTake;
                                item.MarkDirty();
                                break;
                            }
							else if (item.amount == needToTake)
							{
                                nonIntOutput["fuel"] += needToTake - notRoundedAmount;
                                item.GetHeldEntity()?.Kill();
                                item.RemoveFromContainer();
                                item.Remove();
                                break;
							}
                            else
                            {
                                takenItems += item.amount;
                                item.GetHeldEntity()?.Kill();
                                item.RemoveFromContainer();
                                item.Remove();
                            }
                        }
                    }
                ItemManager.DoRemoves();
                return true;
            }

            private void OnDestroy() => CancelInvoke();
        }

        private void LoadMessages()
        {
            Dictionary<string, string> translations = new Dictionary<string, string>()
            {
                ["VirtualQuarriesTitle"] = "VIRTUAL QUARRIES",
                ["QuarryListTitle"] = "QUARRY LIST",
                ["QuarryInfoTitle"] = "QUARRY INFO",
                ["SelectSurveyTypeTitle"] = "SELECT WHICH SURVEY TYPE YOU WANT TO USE",
                ["RequiredSurveyTitle"] = "YOU NEED TO USE <color=#bbd988>{0}</color> TO FIND NEW DEPOT",
                ["CurrentResourcesTitle"] = "CURRENT RESOURCES, TO FIND NEW - USE <color=#bbd988>{0}</color>",
                ["NoResourcesFoundTitle"] = "NO DEPOSIT FOUND, TO TRY AGAIN - USE <color=#bbd988>{0}</color>",
                ["RequiredItemsTitle"] = "REQUIRED ITEMS TO PLACE NEW QUARRY",
                ["ChangeEngineStatusTitle"] = "CHANGE ENGINE STATUS",
                ["OpenFuelTitle"] = "OPEN FUEL CONTAINER",
                ["OpenResourceTitle"] = "OPEN RESOURCE CONTAINER",
                ["OwnerTitle"] = "OWNER",
                ["AddPlayerTitle"] = "SELECT AN PLAYER THAT YOU WANT TO ADD TO THIS QUARRY",
                ["AddOfflinePlayerTitle"] = "SELECT AN OFFLINE PLAYER THAT YOU WANT TO ADD TO THIS QUARRY",
                ["RemovePlayerTitle"] = "SELECT AN PLAYER THAT YOU WANT TO REMOVE FROM THIS QUARRY",
                ["AddAllPlayerTitle"] = "SELECT AN PLAYER THAT YOU WANT TO ADD TO ALL YOUR QUARRIES",
                ["AddAllOfflinePlayerTitle"] = "SELECT AN OFFLINE PLAYER THAT YOU WANT TO ADD TO ALL YOUR QUARRIES",
                ["UpgradesTitle"] = "UPGRADES",
                ["NowTitle"] = "NOW",
                ["AfterTitle"] = "AFTER",
                ["Or"] = "OR",
                ["Off"] = "STOP",
                ["On"] = "START",
                ["UpgradeInfo"] = "Current Level:\nCapacity:\nOutput Multiplier:\nFuel Multiplier:",
                ["LevelMaxed"] = "LEVEL MAXED",
                ["RequiredItems"] = "REQUIRED ITEMS",
                ["SelectQuarryHint"] = "SELECT <color=#bbd988>ONE OF YOUR QUARRIES</color> OR <color=#bbd988>PLACE NEW ONE</color> FOR MORE!\n\nYour quarry type limits:",
                ["OnlyAccessHint"] = "SELECT <color=#bbd988>ONE OF YOUR SHARED QUARRIES</color>.\nYOU ARE <color=#bbd988>NOT ALLOWED</color> TO PLACE NEW QUARRIES!",
                ["NoFuelHint"] = "You need to put <color=#5c81ed>x{0} {1}</color> to fuel storage in order to start this quarry!",
                ["PermTranslation"] = "\n<color=#bbd988>{0}</color> - {1}",
                ["AllQuarries"] = "ALL QUARRIES",
                ["NoRequiredSurvey"] = "You need to have <color=#5c81ed>{0}</color> to find new resource deposit!",
                ["NoRequiredItems"] = "You don't have <color=#5c81ed>required items</color> to place this quarry!",
                ["ErrorOccured"] = "An error has occured! Please contact <color=#5c81ed>Administrator</color>!",
                ["NoSprintButton"] = "In order to remove your quarry you need to have Your <color=#5c81ed>SPRINT</color> button pushed down!",
                ["QuarryRemoved"] = "You've <color=#5c81ed>successfully</color> removed your Virtual Quarry!",
                ["QuarryRemovedRefund"] = "You've <color=#5c81ed>successfully</color> removed your Virtual Quarry!\nResources has been <color=#5c81ed>refunded</color> to your inventorry!",
                ["NotEnoughCurrency"] = "You don't have required amount of <color=#5c81ed>currency</color> to upgrade this quarry!",
                ["NotEnoughItems"] = "You don't have required amount of <color=#5c81ed>items</color> to upgrade this quarry!",
                ["AccessListStart"] = "All players added to this quarry:",
                ["AccessListList"] = "\n- <color=#5c81ed>{0}</color>",
                ["AccessListNoAdded"] = "\n<color=#5c81ed>No one is added to this quarry!</color>",
                ["NotAllowedToPlace"] = "You are <color=#5c81ed>not allowed</color> to place new quarries!",
                ["TooManyQuarries"] = "You've reached your limit of placed <color=#5c81ed>Quarries of this type</color>!",
                ["NoSurveyThrow"] = "You can't throw survey charges!\nUse <color=#5c81ed>/{0}</color> instead.",
                ["PlaceButton"] = "PLACE",
                ["ThrowButton"] = "SEARCH",
                ["GoBackButton"] = "GO BACK",
                ["GiveAccessButton"] = "GIVE ACCESS",
                ["RemoveAccessButton"] = "REMOVE ACCESS",
                ["AccessListButton"] = "ACCESS LIST",
                ["SortOwnedButton"] = "SORT OWNED",
                ["SortSharedButton"] = "SORT SHARED",
                ["DisableSortButton"] = "DISABLE SORT",
                ["AddToAllButton"] = "ADD TO ALL",
                ["OfflinePlayersButton"] = "OFFLINE PLAYERS",
                ["PurchaseItemsButton"] = "PURCHASE FOR ITEMS",
                ["PurchaseCurrencyButton"] = "PURCHASE FOR {0} RP",
                ["PrivateInventoryInfo"] = "This inventory and items are visible <color=#5c81ed>only</color> for you!",
                ["NoPermission"] = "You don't have access to <color=#5c81ed>Virtual Quarries</color>!",
                ["NoPermissionQuarry"] = "You don't have access to <color=#5c81ed>Quarries</color>!",
                ["NoPermissionPumpJack"] = "You don't have access to <color=#5c81ed>Pump Jacks</color>!",
                ["OpenStorageFirst"] = "Open your <color=#5c81ed>Excavator Output Storage</color> first!",
                ["NoAirDrops"] = "Air Drops has been <color=#5c81ed>disabled</color>!",
                ["NewResourceDug"] = "During upgrading your quarry you found a <color=#5c81ed>new resource deposit</color>!",
                ["OnlyTeamShare"] = "You can share quarry only with your <color=#5c81ed>teammates</color>!",
                ["NoLongerProfilePerm"] = "You no longer have permission to this quarry!\n<color=#5c81ed>Renew your permission</color> or <color=#5c81ed>remove quarry</color> to continue!",
                ["TooManyQuarriesPermMissing"] = "You no longer have permission to this amount of this type quarries!\n<color=#5c81ed>Renew your permission</color> or <color=#5c81ed>decrease amount of quarries</color> to continue!",
                ["NoPermToUpgrade"] = "You don't have required permissions to upgrade your quarry further!",
                ["SwitchedResource"] = "You've switched excavator resource to <color=#5c81ed>{0}</color>!"
            };
            foreach (var profile in config.quarryProfiles.Values)
            {
                if (profile.fuelItem.skin != 0)
                    translations.TryAdd($"Fuel_{profile.fuelItem.skin}", profile.fuelItem.shortname);
                else if (profile.fuelItem.shortname == "lowgradefuel")
                    translations.TryAdd($"Fuel_{profile.fuelItem.shortname}", "Low Grade Fuel");
                else
                    translations.TryAdd($"Fuel_{profile.fuelItem.shortname}", profile.fuelItem.shortname);
                if (profile.titleTranslation == "QuarryTitle")
                    translations.TryAdd(profile.titleTranslation, "Mining Quarry");
                else if (profile.titleTranslation == "PumpjackTitle")
                    translations.TryAdd(profile.titleTranslation, "Mining Pumpjack");
                else
                    translations.TryAdd(profile.titleTranslation, profile.titleTranslation);
            }
            foreach (var survey in config.surveys.Values)
            {
                if (survey.surveyTranslation == "SurveyCharge")
                    translations.TryAdd(survey.surveyTranslation, "X1 SURVEY CHARGE");
                else
                    translations.TryAdd(survey.surveyTranslation, survey.surveyTranslation);
            }
            lang.RegisterMessages(translations, this);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        
        private void AddImage(string url)
        {
#if CARBON
            imgDb.Queue(url);
#else
           ImageLibrary?.CallHook("AddImage", url, url, 0);
#endif
        }
        
        private string GetImage(string url)
        {
#if CARBON
            return imgDb.GetImageString(url);
#else
            return ImageLibrary.Call<string>("GetImage", url, 0);
#endif
        }

        private static PluginConfig config;
        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(config = new PluginConfig()
            {
                commandList = new List<string>()
                {
                    "qr",
                    "quarry",
                    "quarries",
                    "vq",
                    "virtualquarry",
                    "virtualquarries"
                },
                permissions = new Dictionary<string, Dictionary<string, int>>()
                {
                    { "virtualquarries.default", new Dictionary<string, int>() {
                        { "quarry", 2 },
                        { "pumpjack", 1 }
                    } },
                    { "virtualquarries.vip", new Dictionary<string, int>() {
                        { "quarry", 3 },
                        { "pumpjack", 2 }
                    } }
                },
                surveys = new Dictionary<string, SurveyConfig>()
                {
                    { "survey", new SurveyConfig() {
                        surveyTranslation = "SurveyCharge"
                    } }
                },
                staticMetalOutput = new List<StaticQuarryOutput>()
                {
                    new StaticQuarryOutput()
                    {
                        shortname = "stones",
                        amount = 150
                    },
                    new StaticQuarryOutput()
                    {
                        shortname = "metal.ore",
                        amount = 22.5f
                    }
                },
                staticSulfurOutput = new List<StaticQuarryOutput>()
                {
                    new StaticQuarryOutput()
                    {
                        shortname = "sulfur.ore",
                        amount = 22.5f
                    }
                },
                staticHqmOutput = new List<StaticQuarryOutput>()
                {
                    new StaticQuarryOutput()
                    {
                        shortname = "hq.metal.ore",
                        amount = 1.5f
                    }
                },
                staticPumpJackOutput = new List<StaticQuarryOutput>()
                {
                    new StaticQuarryOutput()
                    {
                        shortname = "crude.oil",
                        amount = 6
                    }
                },
                excavatorResources = new Dictionary<string, List<StaticQuarryOutput>>()
                {
                    { "Stone", new List<StaticQuarryOutput>() {
                        new StaticQuarryOutput()
                        {
                            shortname = "stones",
                            amount = 5000
                        }
                    } },
                    { "Metal", new List<StaticQuarryOutput>() {
                        new StaticQuarryOutput()
                        {
                            shortname = "metal.fragments",
                            amount = 2500
                        }
                    } },
                    { "Sulfur", new List<StaticQuarryOutput>() {
                        new StaticQuarryOutput()
                        {
                            shortname = "sulfur.ore",
                            amount = 1000
                        }
                    } },
                    { "HQM", new List<StaticQuarryOutput>() {
                        new StaticQuarryOutput()
                        {
                            shortname = "hq.metal.ore",
                            amount = 50
                        }
                    } }
                },
                quarryProfiles = new Dictionary<string, QuarryProfile>()
                {
                    { "quarry", new QuarryProfile()
                        {
                            chance = 25,
                            titleTranslation = "QuarryTitle",
                            icon = new RequiredItem() { shortname = "mining.quarry", iconUrl = "" },
                            surveyType = "survey",
                            requiredItems = new List<RequiredItem>()
                            {
                                new RequiredItem() { shortname = "mining.quarry", amount = 1, iconUrl = "", skin = 0 }
                            },
                            resources = new Dictionary<string, ResourceConfig>()
                            {
                                { "stone", new ResourceConfig() { chance = 0, alwaysInclude = true, shortname = "stones", outputMax = 300f, outputMin = 150f } },
                                { "metal", new ResourceConfig() { chance = 50, alwaysInclude = false, shortname = "metal.ore", outputMax = 45f, outputMin = 22.5f, permission = "virtualquarries.metal" } },
                                { "sulfur", new ResourceConfig() { chance = 50, alwaysInclude = false, shortname = "sulfur.ore", outputMax = 30.5f, outputMin = 15.0f } },
                                { "hq", new ResourceConfig() { chance = 10, alwaysInclude = false, shortname = "hq.metal.ore", outputMax = 2.0f, outputMin = 0.3f } },
                                { "scrap", new ResourceConfig() { chance = 5, alwaysInclude = false, shortname = "scrap", outputMax = 1.0f, outputMin = 0.1f, permission = "virtualquarries.scrap" , additionalItems = new List<RequiredItem>() { new RequiredItem() { shortname = "wood", amount = 7000, iconUrl = "", skin = 0 } } } }
                            },
                            upgrades = new List<UpgradeConfig>()
                            {
                                new UpgradeConfig() { capacity = 6, multiplier = 1, requiredItems = new List<RequiredItem>(), requiredRp = 0 },
                                new UpgradeConfig() { capacity = 9, multiplier = 1.2f, requiredItems = new List<RequiredItem>() { new RequiredItem() { shortname = "wood", amount = 7000, iconUrl = "", skin = 0 }, new RequiredItem() { shortname = "stones", amount = 5000, iconUrl = "", skin = 0 } }, requiredRp = 6000, additionalResources = new List<string>() { "scrap" } },
                            },
                        }
                    },
                    { "pumpjack", new QuarryProfile()
                        {
                            permission = "virtualquarries.pumpjack",
                            titleTranslation = "PumpjackTitle",
                            icon = new RequiredItem() { shortname = "mining.pumpjack", iconUrl = "" },
                            surveyType = "survey",
                            requiredItems = new List<RequiredItem>()
                            {
                                new RequiredItem() { shortname = "mining.pumpjack", amount = 1, iconUrl = "", skin = 0 }
                            },
                            resources = new Dictionary<string, ResourceConfig>()
                            {
                                { "crude", new ResourceConfig() { chance = 0, alwaysInclude = true, shortname = "crude.oil", outputMax = 3.0f, outputMin = 0.8f } },
                            },
                            upgrades = new List<UpgradeConfig>()
                            {
                                new UpgradeConfig() { capacity = 6, multiplier = 1, requiredItems = new List<RequiredItem>(), requiredRp = 0 },
                                new UpgradeConfig() { capacity = 9, multiplier = 1.2f, requiredItems = new List<RequiredItem>() { new RequiredItem() { shortname = "wood", amount = 14000, iconUrl = "", skin = 0 }, new RequiredItem() { shortname = "stones", amount = 10000, iconUrl = "", skin = 0 } }, requiredRp = 12000 },
                            },
                        }
                    }
                }
            }, true);
        }

        private class PluginConfig
        {
            [JsonProperty("Command List")]
            public List<string> commandList = new List<string>();

            [JsonProperty("UI Action Cooldown (in seconds, 0 to disable)")]
            public float commandCooldown = 0f;

            [JsonProperty("Enable Console Logs")]
            public bool consoleLogs = true;

            [JsonProperty("PopUpAPI - Preset Name")]
            public string popUpPreset = "Legacy";

            [JsonProperty("Require Permission For Use")]
            public bool requirePermission = false;

            [JsonProperty("Lock Access To Quarry Profiles If Lost Permission")]
            public bool lockAccesNoPerm = false;

            [JsonProperty("Check For Quarry Amount Permission (option above must be set to true to work)")]
            public bool checkQuarryAmount = false;

            [JsonProperty("OnEntityTakeDamage Return Value")]
            public bool returnValue = false;

            [JsonProperty("Mining Quarry/Pump Jack Limit Permissions")]
            public Dictionary<string, Dictionary<string, int>> permissions = new Dictionary<string, Dictionary<string, int>>();

            [JsonProperty("Sharing - Require Permission")]
            public bool sharingRequirePermission = false;

            [JsonProperty("Sharing - Remove Members If Owner Offline More Than X Days (0, to disable)")]
            public int offlineQuarryOwnerKickTime = 0;

            [JsonProperty("Sharing - Share Only To Teammates")]
            public bool shareClanOnly = false;

            [JsonProperty("Data - Enable Data Wipe On Server Wipe")]
            public bool wipeData = false;

            [JsonProperty("Data - Wipe Data Only On Force Wipe")]
            public bool wipeDataForceOnly = false;

            [JsonProperty("Data - Store Container Data In File And Restore On Server Wipe")]
            public bool storeContainers = false;

            [JsonProperty("Data - Store Container Interval (in seconds)")]
            public int containerSaveInterval = 1800;

            [JsonProperty("Quarry Tick (how often quarries dig resources, in seconds)")]
            public int quarryTick = 60;

            [JsonProperty("Static Quarry Tick (how often quarries dig resources, in seconds)")]
            public int staticQuarryTick = 60;

            [JsonProperty("Excavator Quarry Tick (how often quarries dig resources, in seconds)")]
            public int excavatorTick = 60;

            [JsonProperty("Storage Prefab")]
            public string storagePrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";

            [JsonProperty("Sound - Start Sound")]
            public string startSound = "assets/prefabs/npc/autoturret/effects/online.prefab";

            [JsonProperty("Sound - Stop Sound")]
            public string stopSound = "assets/prefabs/npc/autoturret/effects/offline.prefab";

            [JsonProperty("Listed Quarries Permissions Font Size")]
            public int listedQuarriesFontSize = 20;

            [JsonProperty("Survey Charge - Allow Throwing Survey Charges")]
            public bool surveyThrow = false;

            [JsonProperty("Survey Charget Types")]
            public Dictionary<string, SurveyConfig> surveys = new Dictionary<string, SurveyConfig>();

            [JsonProperty("Upgrades - Used Economy Plugin (0 - None, See Website For More Info)")]
            public int economyPlugin = 0;

            [JsonProperty("Upgrades - Economy Currency (If Economy Plugin Is 5 - ShoppyStock)")]
            public string economyCurrency = "rp";

            [JsonProperty("Removing Quarries - Refund Items")]
            public bool refundRemove = true;

            [JsonProperty("Removing Quarries - Refund Upgrades")]
            public bool refundUpgrades = false;

            [JsonProperty("Go Back Button - Position (1-2)")]
            public int exitButtonLoc = 1;

            [JsonProperty("Upgrade UI - Responsive Position")]
            public bool responsiveUpgrade = true;

            [JsonProperty("Static Quarries - Enable")]
            public bool staticQuarries = false;

            [JsonProperty("Static Quarries - Disable Running Effect")]
            public bool disableRunningEffect = false;

            [JsonProperty("Excavator Quarry - Enable")]
            public bool excavatorQuarry = false;

            [JsonProperty("Excavator Quarry - Resource Container Size")]
            public int excavatorResourceSize = 18;

            [JsonProperty("Excavator Quarry - Fuel Container Size")]
            public int excavatorFuelSize = 6;

            [JsonProperty("Static Quarries - Quarry Requires Permission")]
            public bool quarryPerm = false;

            [JsonProperty("Static Quarries - Pump Jack Requires Permission")]
            public bool pumpJackPerm = false;

            [JsonProperty("Static Quarries - Excavator Requires Permission")]
            public bool excavatorPerm = false;

            [JsonProperty("Static Quarries - Resource Container Size")]
            public int staticResourceSize = 18;

            [JsonProperty("Static Quarries - Fuel Container Size")]
            public int staticFuelSize = 6;

            [JsonProperty("Static Quarries - Fuel Item")]
            public FuelItem staticFuelItem = new FuelItem() { shortname = "lowgradefuel" };

            [JsonProperty("Excavator Quarry - Fuel Item")]
            public FuelItem excavatorFuelItem = new FuelItem() { shortname = "diesel_barrel" };

            [JsonProperty("Static Quarries - Metal Quarry Output")]
            public List<StaticQuarryOutput> staticMetalOutput = new List<StaticQuarryOutput>();

            [JsonProperty("Static Quarries - Sulfur Quarry Output")]
            public List<StaticQuarryOutput> staticSulfurOutput = new List<StaticQuarryOutput>();

            [JsonProperty("Static Quarries - HQM Quarry Output")]
            public List<StaticQuarryOutput> staticHqmOutput = new List<StaticQuarryOutput>();

            [JsonProperty("Static Quarries - Pump Jack Output")]
            public List<StaticQuarryOutput> staticPumpJackOutput = new List<StaticQuarryOutput>();

            [JsonProperty("Static Quarries - Excavator Outputs")]
            public Dictionary<string, List<StaticQuarryOutput>> excavatorResources = new Dictionary<string, List<StaticQuarryOutput>>();

            [JsonProperty("Quarry Profiles")]
            public Dictionary<string, QuarryProfile> quarryProfiles = new Dictionary<string, QuarryProfile>();
        }

        private class SurveyConfig
        {
            [JsonProperty("Effect Path")]
            public string effectPath = "assets/bundled/prefabs/fx/survey_explosion.prefab";

            [JsonProperty("Required Permission (empty, if not required)")]
            public string permission = string.Empty;

            [JsonProperty("Chance For Resources (0-100)")]
            public int resourceChance = 75;

            [JsonProperty("Displayed Survey Title Translation Key")]
            public string surveyTranslation = string.Empty;

            [JsonProperty("Required Item")]
            public RequiredItem surveyItem = new RequiredItem() { shortname = "surveycharge", iconUrl = "" };
        }

        private class QuarryProfile
        {
            [JsonProperty("Required Permission (empty, if not required)")]
            public string permission = string.Empty;

            [JsonProperty("Displayed Icon")]
            public RequiredItem icon = new RequiredItem() { shortname = "mining.quarry", iconUrl = "" };

            [JsonProperty("Survey Type")]
            public string surveyType = "";

            [JsonProperty("Displayed Quarry Title Translation Key")]
            public string titleTranslation = string.Empty;

            [JsonProperty("Chance")]
            public int chance = 5;

            [JsonProperty("Minimal Resources Per Node")]
            public int minPerNode = 1;

            [JsonProperty("Maximal Resources Per Node")]
            public int maxPerNode = 2;

            [JsonProperty("Fuel Required Per Tick")]
            public FuelItem fuelItem = new FuelItem() { shortname = "lowgradefuel" };

            [JsonProperty("Enable Upgrades")]
            public bool enableUpgrades = true;

            [JsonProperty("Items Required To Place")]
            public List<RequiredItem> requiredItems = new List<RequiredItem>();

            [JsonProperty("Resources")]
            public Dictionary<string, ResourceConfig> resources = new Dictionary<string, ResourceConfig>();

            [JsonProperty("Upgrades")]
            public List<UpgradeConfig> upgrades = new List<UpgradeConfig>();
        }

        private class ResourceConfig
        {
            [JsonProperty("Output Item - Shortname")]
            public string shortname;

            [JsonProperty("Output Item - Skin")]
            public ulong skin = 0;

            [JsonProperty("Output Item - Display Name")]
            public string name = string.Empty;

            [JsonProperty("Output Item - Icon URL (Required if Skin not 0)")]
            public string iconUrl = string.Empty;

            [JsonProperty("Include Always")]
            public bool alwaysInclude = false;

            [JsonProperty("Required Permission (empty if not required)")]
            public string permission = string.Empty;

            [JsonProperty("Chance")]
            public int chance;

            [JsonProperty("Minimal Output Per Tick")]
            public float outputMin;

            [JsonProperty("Maximal Output Per Tick")]
            public float outputMax;

            [JsonProperty("Additional Items Required To Place")]
            public List<RequiredItem> additionalItems = new List<RequiredItem>();
        }

        private class UpgradeConfig
        {
            [JsonProperty("Required Items")]
            public List<RequiredItem> requiredItems;

            [JsonProperty("Required Permission To Upgrade")]
            public string requiredPerm = "";

            [JsonProperty("Required Currency (0 to disable)")]
            public int requiredRp = 0;

            [JsonProperty("Fuel Storage Capacity")]
            public int fuelCapacity = 6;

            [JsonProperty("Capacity")]
            public int capacity = 6;

            [JsonProperty("Gather Multiplier")]
            public float multiplier = 1;

            [JsonProperty("Fuel Usage Multiplier")]
            public float fuelMultiplier = 1;

            [JsonProperty("Additional Resources (Resources keys)")]
            public List<string> additionalResources = new List<string>();
        }

        private class StaticQuarryOutput
        {
            [JsonProperty("Shortname")]
            public string shortname;

            [JsonProperty("Skin")]
            public ulong skin = 0;

            [JsonProperty("Amount Per Tick")]
            public float amount = 1;

            [JsonProperty("Display Name")]
            public string displayName = "";
        }

        private class FuelItem
        {
            [JsonProperty("Shortname")]
            public string shortname;

            [JsonProperty("Skin")]
            public ulong skin = 0;

            [JsonProperty("Amount")]
            public float amount = 1;

            [JsonProperty("Display Name")]
            public string displayName;

            [JsonProperty("Icon URL")]
            public string iconUrl;
        }

        private class RequiredItem
        {
            [JsonProperty("Shortname")]
            public string shortname;

            [JsonProperty("Skin")]
            public ulong skin = 0;

            [JsonProperty("Amount")]
            public int amount = 1;

            [JsonProperty("Display Name")]
            public string displayName;

            [JsonProperty("Icon URL")]
            public string iconUrl;
			
			public RequiredItem() { }
			
			public RequiredItem(RequiredItem item)
			{
				shortname = item.shortname;
				skin = item.skin;
				amount = item.amount;
				displayName = item.displayName;
				iconUrl = item.iconUrl;
			}
        }

        private static PluginData data;

        [JsonProperty("Player Cache")]
        private Dictionary<ulong, string> playerCache = new Dictionary<ulong, string>();

        [JsonProperty("Storage Cache")]
        private Dictionary<int, StorageData> storageCache = new Dictionary<int, StorageData>();

        private class PluginData
        {
            [JsonProperty("Quarry Count")]
            public int quarryCount = 0;

            [JsonProperty("Quarries")]
            public Dictionary<int, QuarryData> quarries = new Dictionary<int, QuarryData>();

            [JsonProperty("Static Quarries")]
            public Dictionary<ulong, Dictionary<ulong, ulong>> staticQuarries = new Dictionary<ulong, Dictionary<ulong, ulong>>();

            [JsonProperty("Saved Excavator Outputs")]
            public Dictionary<ulong, string> excavatorOutputs = new Dictionary<ulong, string>();
        }

        private class QuarryData
        {
            [JsonProperty("Quarry Network ID")]
            public ulong netId;

            [JsonProperty("Quarry Owner")]
            public ulong owner;

            [JsonProperty("Profile")]
            public string profile;

            [JsonProperty("Authorized Players")]
            public List<ulong> authPlayers = new List<ulong>();

            [JsonProperty("Quarry Level")]
            public int level = 0;

            [JsonProperty("Last Owner Online")]
            public DateTime lastOwnerOnline = DateTime.MinValue;

            [JsonProperty("Quarry Upgrade Types")]
            public Dictionary<int, bool> upgradedForRp = new Dictionary<int, bool>();

            [JsonProperty("Is Running")]
            public bool isRunning = false;

            [JsonProperty("Resources")]
            public List<QuarryResource> resources = new List<QuarryResource>();
        }

        private class QuarryResource
        {
            [JsonProperty("Config Resource Key")]
            public string configKey;

            [JsonProperty("Output Per Tick")]
            public float work;
        }

        private class StorageData
        {
            [JsonProperty("Resource Storage")]
            public List<RequiredItem> resource = new List<RequiredItem>();

            [JsonProperty("Fuel Storage")]
            public List<RequiredItem> fuel = new List<RequiredItem>();
        }

        private void LoadData()
        {
            data = Interface.Oxide.DataFileSystem.ReadObject<PluginData>($"{this.Name}/quarryData");
            playerCache = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, string>>($"{this.Name}/playerCache");
            if (config.storeContainers)
                storageCache = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, StorageData>>($"{this.Name}/storageCache");
            timer.Every(Core.Random.Range(500, 700), SaveData);
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{this.Name}/quarryData", data);
            Interface.Oxide.DataFileSystem.WriteObject($"{this.Name}/playerCache", playerCache);
        }
    }
} 