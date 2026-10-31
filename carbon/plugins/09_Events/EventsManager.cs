using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Facepunch;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Global = Rust.Global;
using Random = UnityEngine.Random;

#if CARBON
using Carbon.Base;
using Carbon.Modules;
#endif

namespace Oxide.Plugins;

[Info("EventsManager", "Mevent & Qbis", "4.2.2")]
internal class EventsManager : RustPlugin
{
    #region Fields

    [PluginReference] private Plugin
        ImageLibrary = null,
        Notify = null,
        UINotify = null;

#if CARBON
    private ImageDatabaseModule imageDatabase;
#endif
    private Dictionary<string, string> _loadedImages = new();

    private static EventsManager Instance;
    private EventController eventController;

    private HashSet<EventSchedule> disabledEvents = new();
    private Harmony _emHarmony;

    private const string PermissionUse = "EventsManager.use", CreatorFacepunch = "Facepunch", ListSeparator = ", ";

    private Dictionary<string, List<int>> _grouped;
    private List<string> _creatorNames;

    #endregion

    #region Configuration

    private Configuration _config;

    private class Configuration
    {
        [JsonProperty(PropertyName = "Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public string[] Commands =
        {
            "em"
        };

        [JsonProperty(PropertyName = "Auto events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, bool> AutoEvents = new();

        [JsonProperty(PropertyName = "Enable event logging to file")]
        public bool EnableEventLogging = false;

        [JsonProperty(PropertyName = "Work with Notify?")]
        public bool UseNotify = true;

        [JsonProperty(PropertyName = "Discord Settings")]
        public DiscordConfig Discord = new();

        public class DiscordConfig
        {
            [JsonProperty(PropertyName = "Webhook URL")]
            public string WebHook = string.Empty;

            [JsonProperty(PropertyName = "Embed title")]
            public string Title = "Event Manager";

            [JsonProperty(PropertyName = "Embed color (hex, e.g. #1F8B4C)")]
            public string EmbedColor = "#1F8B4C";

            [JsonProperty(PropertyName = "Footer icon URL")]
            public string FooterIcon = "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/EventManager/schedule-icon.png";
        }

        public VersionNumber Version;
    }

    private static string AutoEventKey(string displayName)
    {
        return $"Enable {displayName} auto event";
    }

    private bool IsAutoEventEnabled(Event ev)
    {
        if (ev?.creator != CreatorFacepunch) return true;
        return !_config.AutoEvents.TryGetValue(AutoEventKey(ev.displayName), out var enabled) || enabled;
    }

    private static bool ShouldSuppressAutoEvent(string displayName)
    {
        if (Instance?.data?.events == null) return false;
        var ev = Instance.data.events.Find(e =>
            e.creator == CreatorFacepunch && e.displayName == displayName);
        return ev != null && !Instance.IsAutoEventEnabled(ev);
    }

    private static int SecondsFromWipe() =>
        (int)DateTime.UtcNow.Subtract(SaveRestore.SaveCreatedTime.ToUniversalTime()).TotalSeconds;

    private static double LeftWipeBlockTime(double cooldown) =>
        Math.Max(cooldown - SecondsFromWipe(), 0);

    private static string FormatWipeBlockTime(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToShortString();

    private static bool IsEventCurrentlyActive(Event ev)
    {
        if (ev == null || ev.creator != CreatorFacepunch) return false;

        switch (ev.displayName)
        {
            case "Deep Sea":
            {
                var mgr = DeepSeaManager.Get(true);
                return mgr != null && mgr.IsOpen();
            }
            case "Bradley APC":
            {
                var spawner = BradleySpawner.singleton;
                return spawner != null && spawner.spawned != null && spawner.spawned.IsAlive();
            }
            case "Patrol Helicopter":
                return AnyAlive<PatrolHelicopter>();
            case "Cargo Ship":
                return AnyAlive<CargoShip>();
            case "Chinook (CH47)":
                return AnyAlive<CH47Helicopter>();
            case "Travelling Vendor":
                return AnyAlive<TravellingVendor>();
            case "Satellite Crash":
                return AnyAlive<SatelliteCrash>();
        }

        return false;
    }

    private static bool AnyAlive<T>() where T : BaseEntity
    {
        foreach (var ent in BaseNetworkable.serverEntities)
            if (ent is T t && t.IsValid() && !t.IsDestroyed)
                return true;
        return false;
    }

    private static void KillAll<T>() where T : BaseEntity
    {
        var victims = new List<T>();
        foreach (var ent in BaseNetworkable.serverEntities)
            if (ent is T t && t.IsValid() && !t.IsDestroyed)
                victims.Add(t);
        foreach (var t in victims) t.Kill();
    }

    private static void StopActiveEvent(Event ev)
    {
        if (ev == null || ev.creator != CreatorFacepunch) return;

        switch (ev.displayName)
        {
            case "Deep Sea":
                DeepSeaManager.Get(true)?.CloseDeepSea();
                break;
            case "Bradley APC":
            {
                var spawner = BradleySpawner.singleton;
                if (spawner?.spawned != null && spawner.spawned.IsAlive())
                    spawner.spawned.Kill();
                break;
            }
            case "Patrol Helicopter":
                KillAll<PatrolHelicopter>();
                break;
            case "Cargo Ship":
                KillAll<CargoShip>();
                break;
            case "Chinook (CH47)":
                KillAll<CH47Helicopter>();
                break;
            case "Travelling Vendor":
                KillAll<TravellingVendor>();
                break;
            case "Satellite Crash":
                KillAll<SatelliteCrash>();
                break;
        }
    }

    private void CheckAutoEventsConfigPopulated()
    {
        if (data?.events == null) return;
        var changed = false;
        var validKeys = new HashSet<string>();
        foreach (var ev in data.events)
        {
            if (ev.creator != CreatorFacepunch) continue;
            var key = AutoEventKey(ev.displayName);
            validKeys.Add(key);
            if (!_config.AutoEvents.ContainsKey(key))
            {
                _config.AutoEvents[key] = true;
                changed = true;
            }
        }

        var stale = _config.AutoEvents.Keys.Where(k => !validKeys.Contains(k)).ToList();
        foreach (var k in stale)
        {
            _config.AutoEvents.Remove(k);
            changed = true;
        }

        if (changed) SaveConfig();
    }

    protected override void LoadConfig()
    {
        Instance = this;

        base.LoadConfig();
        try
        {
            _config = Config.ReadObject<Configuration>();
            if (_config == null) throw new JsonException();

            if (_config.Version < Version)
                UpdateConfigValues();

            SaveConfig();
        }
        catch (Exception ex)
        {
            PrintError("Your configuration file contains an error!");
            Debug.LogException(ex);
        }
    }

    protected override void SaveConfig()
    {
        Config?.WriteObject(_config, true);
    }

    protected override void LoadDefaultConfig()
    {
        _config = new Configuration();
    }

    private void UpdateConfigValues()
    {
        PrintWarning("Config update detected! Updating config values...");

        if (_config.Version == default)
        {
            _config.Version = Version;
            PrintWarning("Config update completed!");
            return;
        }

        if (_config.Version < new VersionNumber(3, 2, 0))
        {
            CheckDataLoaded();

            var anyChanged = false;
            foreach (var eventData in data.eventTimes)
            {
                eventData.isRandom = eventData.eventsId?.Count > 1;
                if (eventData.isRandom) anyChanged = true;
            }

            if (anyChanged) data?.SaveTimes(false);
        }

        if (_config.Version < new VersionNumber(3, 3, 3))
        {
            CheckDataLoaded();
            data.creators.Add("Mevent");

            data.events.AddRange(new List<Event>
            {
                new()
                {
                    displayName = "Collection Resources", command = "collectionresources.start", creator = "Mevent"
                },
                new() {displayName = "Foundation Drop", command = "foundationdrop.start", creator = "Mevent"},
                new() {displayName = "Gold Rush", command = "goldrush.start", creator = "Mevent"},
                new() {displayName = "Helicopter Pet", command = "helicopterpet.start", creator = "Mevent"},
                new() {displayName = "Hunt Animal", command = "huntanimal.start", creator = "Mevent"},
                new() {displayName = "King Of Hill", command = "kingofhill.start", creator = "Mevent"},
                new() {displayName = "Looking Loot", command = "lookingloot.start", creator = "Mevent"},
                new() {displayName = "Special Cargo", command = "specialcargo.start", creator = "Mevent"}
            });

            data.SaveCreators();
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(3, 3, 5))
        {
            CheckDataLoaded();
            foreach (var eventData in data.events)
                if (eventData.command.Contains("assets/prefabs/misc/supply drop/supply_drop.prefab"))
                    eventData.command = eventData.command.Replace(
                        "assets/prefabs/misc/supply drop/supply_drop.prefab",
                        "assets/prefabs/npc/cargo plane/cargo_plane.prefab");
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(3, 3, 8))
        {
            CheckDataLoaded();
            data.events.Add(new Event
                {displayName = "Operation Fortress", command = "operation.start", creator = "Mevent"});
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(3, 3, 10))
        {
            CheckDataLoaded();
            var eventEntry = data.events.Find(e => e.displayName == "Satellite Dish Event");
            if (eventEntry != null) eventEntry.pluginName = "SatDishEvent";
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(3, 3, 12))
        {
            CheckDataLoaded();
            data.events.Add(new Event
            {
                displayName = "Santa Sleigh",
                command = "em_spawn assets/prefabs/misc/xmas/sleigh/santasleigh.prefab",
                creator = CreatorFacepunch
            });
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(3, 3, 17))
        {
            CheckDataLoaded();
            if (!data.events.Exists(e => e.creator == CreatorFacepunch && e.displayName == "Deep Sea"))
                data.events.Add(new Event
                    {displayName = "Deep Sea", command = "deepsea.open", creator = CreatorFacepunch});
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(4, 0, 0))
        {
            CheckDataLoaded();

            void AddFacepunchIfMissing(string name, string cmd)
            {
                if (data.events.Exists(e => e.creator == CreatorFacepunch && e.displayName == name)) return;
                data.events.Add(new Event {displayName = name, command = cmd, creator = CreatorFacepunch});
            }

            AddFacepunchIfMissing("Deep Sea", "deepsea.open");
            AddFacepunchIfMissing("Christmas Event", "em_spawn assets/prefabs/misc/xmas/sleigh/santasleigh.prefab");
            AddFacepunchIfMissing("Bradley APC", "em_spawn assets/prefabs/npc/m2bradley/bradleyapc.prefab");

            _pendingIsCustomBackfill = true;

            if (Config["Disable DeepSea auto event?"] is bool deepDisabled && deepDisabled)
                _config.AutoEvents[AutoEventKey("Deep Sea")] = false;

            if (Config["Disable vanilla auto events?"] is bool legacyDisable && legacyDisable)
            {
                _legacyDisableVanillaAutoEvents = true;
                _legacyVanillaAllowlist = Config["Allowed list of vanilla events"] as List<string> ?? new List<string>();
            }

            var discordWebHook = Config.Get("Discord web hook")?.ToString();
            if (!string.IsNullOrEmpty(discordWebHook))
                _config.Discord = new Configuration.DiscordConfig {WebHook = discordWebHook};

            data.SaveEvents();
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(4, 1, 3))
        {
            CheckDataLoaded();

            var changed = false;

            foreach (var ev in data.events)
            {
                if (ev.pluginName == "CargoPlaneEvent")
                {
                    ev.pluginName = "CargoPlaneCrash";
                    changed = true;
                }
                else if (ev.pluginName == "HelipadEvent")
                {
                    ev.pluginName = "LaunchSiteHelipadEvent";
                    changed = true;
                }
            }

            if (changed)
            {
                data.SaveEvents();
                SaveAndRefreshTimes();
            }
        }

        if (_config.Version < new VersionNumber(4, 2, 0))
        {
            CheckDataLoaded();
            if (!data.events.Exists(e => e.creator == CreatorFacepunch && e.displayName == "Satellite Crash"))
                data.events.Add(new Event
                    {displayName = "Satellite Crash", command = "satellite.random", creator = CreatorFacepunch});
            SaveAndRefreshTimes();
        }

        if (_config.Version < new VersionNumber(4, 2, 1))
        {
            CheckDataLoaded();
            if (!data.events.Exists(e => e.pluginName == "LastFuel"))
                data.events.Add(new Event
                    {displayName = "Last Fuel", command = "lastfuel.start", creator = "Mevent", pluginName = "LastFuel"});
            SaveAndRefreshTimes();
        }

        _config.Version = Version;
        PrintWarning("Config update completed!");
    }

    #endregion

    #region Data

    private PluginData data;

    private void LoadData()
    {
        data = new PluginData();
        LoadViewPrefs();
    }

    private class PluginData
    {
        private static string DataPath(string file)
        {
            return "EventManager" + Path.DirectorySeparatorChar + file;
        }

        public List<string> creators = new();
        public List<Event> events = new();
        public List<EventTime> eventTimes = new();

        public PluginData()
        {
            LoadData();
        }

        public void SaveData()
        {
            SaveCreators();
            SaveEvents();
            SaveTimes();
        }

        public void SaveCreators()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataPath("CreatorsSettings"), creators);
        }

        public void SaveEvents()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataPath("EventsSettings"), events);
        }

        public void SaveTimes(bool needUpdateEvents = true)
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataPath("TimesSettings"), eventTimes);

            if (needUpdateEvents) Instance.UpdateEventList();
        }

        public void LoadData()
        {
            try
            {
                creators = Interface.Oxide.DataFileSystem.ReadObject<List<string>>(DataPath("CreatorsSettings"));
                events = Interface.Oxide.DataFileSystem.ReadObject<List<Event>>(DataPath("EventsSettings"));
                eventTimes = Interface.Oxide.DataFileSystem.ReadObject<List<EventTime>>(DataPath("TimesSettings"));
            }
            catch (Exception e)
            {
                Instance?.PrintError("Error loading data: " + e);
                Debug.LogException(e);
            }

            eventTimes?.ForEach(e =>
            {
                e.UpdateInfo();
                e.UpdateOffset();

                if (e.lastRun == default || DateTime.Now >= e.lastRun.AddMinutes(e.interval + e.nextOffset))
                    e.lastRun = DateTime.Now;
            });
        }

        public void CheckEmptyData()
        {
            if (creators?.Count == 0 && events?.Count == 0 && eventTimes?.Count == 0)
            {
                ResetToDefault();

                Instance.PrintWarning("Load default data for Event Manager");
            }
        }

        public void ResetToDefault()
        {
            LoadDefaultEvents();
            LoadDefaultCreators();
            eventTimes.Clear();

            SaveData();
            Instance.UpdateEventList();
        }

        public void LoadDefaultCreators()
        {
            creators = new List<string>
            {
                CreatorFacepunch,
                "Mevent",
                "KpucTaJl",
                "Adem",
                "Mercury",
                "Yac Vaguer",
                "DezLife",
                "Cashr",
                "Nikedemos",
                "Ridamees",
                "Fruster",
                "Bazz3l",
                "Nivex",
                "Dana",
                "Razor",
                "ThePitereq",
                "k1lly0u",
                "Wrecks"
            };
        }

        public void LoadDefaultEvents()
        {
            events = BuildDefaultEvents();
            Instance.CheckIsEventsLoaded();
        }

        public static List<Event> BuildDefaultEvents()
        {
            var list = new List<Event>();

            foreach (var schedule in EventSchedule.allEvents)
            {
                var name = schedule.GetName();
                list.Add(new Event
                {
                    displayName = Instance.FormatScheduleName(name),
                    command = $"triggerevent {name}",
                    creator = CreatorFacepunch,
                    scheduleName = name,
                    pluginName = null
                });
            }

            list.AddRange(new List<Event>
            {
                new() {displayName = "Deep Sea", command = "deepsea.open", creator = CreatorFacepunch, pluginName = null},
                new()
                {
                    displayName = "Satellite Crash", command = "satellite.random", creator = CreatorFacepunch,
                    pluginName = null
                },
                new()
                {
                    displayName = "Christmas Event",
                    command = "em_spawn assets/prefabs/misc/xmas/sleigh/santasleigh.prefab", creator = CreatorFacepunch,
                    pluginName = null
                },
                new()
                {
                    displayName = "Bradley APC", command = "em_spawn assets/prefabs/npc/m2bradley/bradleyapc.prefab",
                    creator = CreatorFacepunch, pluginName = null
                },
                new() {displayName = "Operation Fortress", command = "operation.start", creator = "Mevent", pluginName = "OperationFortress"},
                new() {displayName = "Last Fuel", command = "lastfuel.start", creator = "Mevent", pluginName = "LastFuel"},
                new() {displayName = "Collection Resources", command = "collectionresources.start", creator = "Mevent", pluginName = "MultiEventsCollectionResources"},
                new() {displayName = "Foundation Drop", command = "foundationdrop.start", creator = "Mevent", pluginName = "MultiEventsFoundationDrop"},
                new() {displayName = "Gold Rush", command = "goldrush.start", creator = "Mevent", pluginName = "MultiEventsGoldRush"},
                new() {displayName = "Helicopter Pet", command = "helicopterpet.start", creator = "Mevent", pluginName = "MultiEventsHelicopterPet"},
                new() {displayName = "Hunt Animal", command = "huntanimal.start", creator = "Mevent", pluginName = "MultiEventsHuntAnimal"},
                new() {displayName = "King Of Hill", command = "kingofhill.start", creator = "Mevent", pluginName = "MultiEventsKingOfHill"},
                new() {displayName = "Looking Loot", command = "lookingloot.start", creator = "Mevent", pluginName = "MultiEventsLookingLoot"},
                new() {displayName = "Special Cargo", command = "specialcargo.start", creator = "Mevent", pluginName = "MultiEventsSpecialCargo"},
                new() {displayName = "Air Event", command = "airstart", creator = "KpucTaJl", pluginName = "AirEvent"},
                new() {displayName = "Water Event", command = "waterstart", creator = "KpucTaJl", pluginName = "WaterEvent"},
                new() {displayName = "Arctic Base Event", command = "abstart", creator = "KpucTaJl", pluginName = "ArcticBaseEvent"},
                new() {displayName = "Satellite Dish Event", command = "satdishstart", creator = "KpucTaJl", pluginName = "SatDishEvent"},
                new() {displayName = "Junkyard Event", command = "jstart", creator = "KpucTaJl", pluginName = "JunkyardEvent"},
                new() {displayName = "Power Plant Event", command = "ppstart", creator = "KpucTaJl", pluginName = "PowerPlantEvent"},
                new() {displayName = "Harbor Event", command = "harborstart", creator = "KpucTaJl", pluginName = "HarborEvent"},
                new() {displayName = "Squid Game", command = "rlglstart", creator = "KpucTaJl", pluginName = "SquidGame"},
                new() {displayName = "Triangulation", command = "tstart", creator = "KpucTaJl", pluginName = "Triangulation"},
                new() {displayName = "Supermarket Event", command = "supermarketstart", creator = "KpucTaJl", pluginName = "SupermarketEvent"},
                new() {displayName = "Ferry Terminal Event", command = "ftstart", creator = "KpucTaJl", pluginName = "FerryTerminalEvent"},
                new() {displayName = "GasStation Event", command = "gsstart", creator = "KpucTaJl", pluginName = "GasStationEvent"},
                new() {displayName = "Defendable-Bases Start", command = "warstart", creator = "KpucTaJl", pluginName = "DefendableBases"},
                new() {displayName = "Defendable-Bases Stop", command = "warstop", creator = "KpucTaJl", pluginName = "DefendableBases"},
                new() {displayName = "Sputnik", command = "sputnikstart", creator = "Adem", pluginName = "Sputnik"},
                new() {displayName = "Armored Train", command = "atrainstart", creator = "Adem", pluginName = "ArmoredTrain"},
                new() {displayName = "Space", command = "spacestart", creator = "Adem", pluginName = "Space"},
                new() {displayName = "Convoy", command = "convoystart", creator = "Adem", pluginName = "Convoy"},
                new() {displayName = "Shipwreck", command = "shipwreckstart", creator = "Adem", pluginName = "Shipwreck"},
                new() {displayName = "Caravan", command = "caravanstart", creator = "Adem", pluginName = "Caravan"},
                new() {displayName = "IQSphereEvent", command = "iqsp start", creator = "Mercury", pluginName = "IQSphereEvent"},
                new() {displayName = "Water-Treatment-Showdown", command = "wtestart", creator = "Yac Vaguer", pluginName = "WaterTreatmentShowdown"},
                new() {displayName = "Cobalt Laboratory", command = "cl start", creator = "DezLife", pluginName = "XDCobaltLaboratory"},
                new() {displayName = "XDChinookEvent", command = "chinook call", creator = "DezLife", pluginName = "XDChinookEvent"},
                new() {displayName = "Rocket Event", command = "rocket ||", creator = "Cashr", pluginName = "RocketEvent"},
                new() {displayName = "CargoTrainEvent", command = "trainevent_now", creator = "Nikedemos", pluginName = "CargoTrainEvent"},
                new() {displayName = "Stone Event", command = "StoneStart", creator = "Ridamees", pluginName = "StoneEvent"},
                new() {displayName = "Metal Event", command = "MetalStart", creator = "Ridamees", pluginName = "MetalEvent"},
                new() {displayName = "Sulfur Event", command = "SulfurStart", creator = "Ridamees", pluginName = "SulfurEvent"},
                new() {displayName = "RaidRush Event", command = "raidrush.start", creator = "Ridamees", pluginName = "RaidRushEvent"},
                new() {displayName = "BarrelBash Event", command = "StartBarrelBash", creator = "Ridamees", pluginName = "BarrelBashEvent"},
                new() {displayName = "BoxBattle Event", command = "StartBoxBattle", creator = "Ridamees", pluginName = "BoxBattleEvent"},
                new() {displayName = "CargoPlaneEvent", command = "callcargoplane", creator = "Fruster", pluginName = "CargoPlaneEvent"},
                new() {displayName = "AirfieldEvent", command = "afestart", creator = "Fruster", pluginName = "AirfieldEvent"},
                new() {displayName = "HelipadEvent", command = "hpestart", creator = "Fruster", pluginName = "HelipadEvent"},
                new() {displayName = "GuardedCrate Easy", command = "gcrate start Easy", creator = "Bazz3l", pluginName = "GuardedCrate"},
                new() {displayName = "GuardedCrate Medium", command = "gcrate start Medium", creator = "Bazz3l", pluginName = "GuardedCrate"},
                new() {displayName = "GuardedCrate Hard", command = "gcrate start Hard", creator = "Bazz3l", pluginName = "GuardedCrate"},
                new() {displayName = "GuardedCrate Elite", command = "gcrate start Elite", creator = "Bazz3l", pluginName = "GuardedCrate"},
                new() {displayName = "Dangerous Treasures", command = "dtevent", creator = "Nivex", pluginName = "DangerousTreasures"},
                new() {displayName = "RaidableBases Easy", command = "rbevent easy", creator = "Nivex", pluginName = "RaidableBases"},
                new() {displayName = "RaidableBases Medium", command = "rbevent medium", creator = "Nivex", pluginName = "RaidableBases"},
                new() {displayName = "RaidableBases Hard", command = "rbevent hard", creator = "Nivex", pluginName = "RaidableBases"},
                new() {displayName = "RaidableBases Nightmare", command = "rbevent nightmare", creator = "Nivex", pluginName = "RaidableBases"},
                new() {displayName = "Heli Regular", command = "heli.call Regular", creator = "Dana", pluginName = "Heli"},
                new() {displayName = "Heli Millitary", command = "heli.call Millitary", creator = "Dana", pluginName = "Heli"},
                new() {displayName = "Heli Elite", command = "heli.call Elite", creator = "Dana", pluginName = "Heli"},
                new() {displayName = "Jet Event", command = "jet event 2 2 10", creator = "Razor", pluginName = "JetEvent"},
                new() {displayName = "Meteor-Event-Kill", command = "msadmin kill", creator = "ThePitereq", pluginName = "MeteorEvent"},
                new()
                {
                    displayName = "Meteor-Event-Start-Amount-10", command = "msadmin run default 10",
                    creator = "ThePitereq", pluginName = "MeteorEvent"
                },
                new()
                {
                    displayName = "Meteor-Event-Start-Amount-20", command = "msadmin run default 20",
                    creator = "ThePitereq", pluginName = "MeteorEvent"
                },
                new()
                {
                    displayName = "Meteor-Event-Start-Amount-30", command = "msadmin run default 30",
                    creator = "ThePitereq", pluginName = "MeteorEvent"
                },
                new()
                {
                    displayName = "Meteor-Event-Start-Amount-40", command = "msadmin run default 40",
                    creator = "ThePitereq", pluginName = "MeteorEvent"
                },
                new()
                {
                    displayName = "Meteor-Event-Start-Amount-50", command = "msadmin run default 50",
                    creator = "ThePitereq", pluginName = "MeteorEvent"
                },
                new() {displayName = "PilotEjec", command = "pe call", creator = "k1lly0u", pluginName = "PilotEjec"},
                new() {displayName = "HeliRefuel", command = "hr call", creator = "k1lly0u", pluginName = "HeliRefuel"},
                new() {displayName = "Eradication Event", command = "Erad", creator = "Wrecks", pluginName = "EradicationEvent"},
                new() {displayName = "Bot-Purge-Event", command = "Purge", creator = "Wrecks", pluginName = "BotPurgeEvent"}
            });

            return list;
        }
    }

    #region Classes

    private class Event
    {
        public string displayName;
        public string pluginName;
        public string command;
        public string creator;
        public string scheduleName;

        public int eventId = Random.Range(0, int.MaxValue);

        public bool scheduleEnabled = true;
        public bool isCustom;

        [JsonProperty(PropertyName = "Wipe Block (seconds)")]
        public double wipeBlockSeconds;

        [JsonIgnore] public bool isLoaded;
    }

    private class EventTime : ICloneable
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public EventType type = EventType.Static;

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<DayOfWeek> activeDays = new();

        public int hour;
        public int minute;
        public int interval;
        public int offsetMin;
        public int offsetMax;
        public int minPlayers;
        public int maxPlayers;
        public bool isRandom;

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> eventsId = new();

        public DateTime lastRun = DateTime.Now;
        [JsonIgnore] public int lastRunEventId;
        [JsonIgnore] public int nextOffset;
        [JsonIgnore] public bool skipLogged;
        [JsonIgnore] public string displayInfo;
        [JsonIgnore] public string displayTitle;
        [JsonIgnore] public string displayDays;

        public void UpdateOffset()
        {
            nextOffset = offsetMin != 0 && offsetMax != 0 && offsetMax >= offsetMin
                ? Random.Range(offsetMin, offsetMax + 1)
                : 0;
        }

        public void UpdateInfo()
        {
            var sb = Pool.Get<StringBuilder>();

            switch (type)
            {
                case EventType.Static:
                {
                    displayTitle = $"[Static] {hour:00}:{minute:00}";
                    sb.AppendFormat("{0:00}:{1:00} - ", hour, minute);
                    GetDays(ref sb);
                    break;
                }

                case EventType.Every:
                {
                    displayTitle = $"[Every] {interval} min";
                    sb.Append(Instance.Msg(UIEveryXMinutes, interval));
                    break;
                }

                case EventType.Random:
                {
                    displayTitle = $"[Random] {offsetMin}-{offsetMax} min";
                    sb.Append(Instance.Msg(UIEveryRangeMinutes, offsetMin, offsetMax));
                    break;
                }
            }

            displayInfo = sb.ToString();

            sb.Clear();
            GetDays(ref sb);
            displayDays = sb.ToString();

            Pool.FreeUnmanaged(ref sb);
        }

        public void GetDays(ref StringBuilder sb)
        {
            switch (activeDays.Count)
            {
                case 3:
                case 4:
                {
                    foreach (var d in activeDays)
                        sb.Append(Instance.GetDayName(d).Substring(0, 3)).Append(ListSeparator);

                    sb.Length -= 2;
                    break;
                }

                case 5:
                case 6:
                {
                    foreach (var d in activeDays)
                        sb.Append(Instance.GetDayName(d).Substring(0, 1)).Append(ListSeparator);

                    sb.Length -= 2;
                    break;
                }

                case 7:
                case 0:
                {
                    sb.Append(Instance.Msg(UIEveryday));
                    break;
                }

                default:
                {
                    foreach (var d in activeDays)
                        sb.Append(Instance.GetDayName(d)).Append(ListSeparator);

                    sb.Length -= 2;
                    break;
                }
            }
        }

        public object Clone()
        {
            return MemberwiseClone();
        }

        public int GetRandomEvent()
        {
            if (eventsId.Count > 2) return eventsId.FindAll(e => e != lastRunEventId).GetRandom();

            return eventsId.GetRandom();
        }
    }

    private enum EventType
    {
        Static,
        Every,
        Random
    }

    #endregion

    #endregion

    #region Hooks

    private bool _pluginLoaded = false;

    private bool _legacyDisableVanillaAutoEvents;
    private List<string> _legacyVanillaAllowlist;
    private bool _pendingIsCustomBackfill;
    private int _migrationRetryCount;

    private void OnServerInitialized()
    {
        AddCovalenceCommand(_config.Commands, nameof(CmdNewEventManager));

        permission.RegisterPermission(PermissionUse, this);

        LoadData();

        data?.CheckEmptyData();

        RunGameStateMigrations();

        SyncFacepunchEvents();

        CheckAutoEventsConfigPopulated();

        DisableVanillaEvents();

        CreateController();

        UpdateEventList();

        CheckIsEventsLoaded();

        RebuildEventCache();

        LoadImages();

        InitHarmonyPatches();

        _pluginLoaded = true;
    }

    private void Unload()
    {
        try
        {
            DisposeHarmonyPatches();

            DestroyController();

            EnableVanillaEvents();

            DestroyUI();

            data?.SaveTimes(false);
        }
        finally
        {
            Instance = null;
        }
    }

    private void OnServerSave()
    {
        data?.SaveTimes(false);
    }

    private void OnPlayerDisconnected(BasePlayer player, string reason)
    {
        if (player == null) return;

        openedUI.Remove(player.userID);
    }

    private void OnPluginLoaded(Plugin plugin)
    {
        if (plugin == null || !_pluginLoaded) return;

        var events = data?.events?.FindAll(e => e.pluginName == plugin.Name);
        if (events == null || events.Count == 0) return;

        foreach (var targetEvent in events)
            targetEvent.isLoaded = true;
    }

    private void OnPluginUnloaded(Plugin plugin)
    {
        if (plugin == null || !_pluginLoaded) return;

        var events = data?.events?.FindAll(e => e.pluginName == plugin.Name);
        if (events == null || events.Count == 0) return;

        foreach (var targetEvent in events)
            targetEvent.isLoaded = false;
    }

    #endregion

    #region Controller

    private class EventController : FacepunchBehaviour
    {
        private const int DEFER_RETRY_MINUTES = 5;

        private void Awake()
        {
            InvokeRepeating(EventTimer, 10, 10);
        }

        private static bool ExecuteScheduledEvent(EventTime eventTime)
        {
            var id = eventTime.GetRandomEvent();
            var startEvent = Instance.data.events.Find(e => e.eventId == id);
            if (startEvent == null) return false;

            if (IsEventCurrentlyActive(startEvent))
            {
                if (eventTime.type != EventType.Static)
                    eventTime.lastRun = DateTime.Now.AddMinutes(-eventTime.interval + DEFER_RETRY_MINUTES);

                if (!eventTime.skipLogged)
                {
                    Instance.PrintWarning(
                        $"[{startEvent.displayName}] auto-fire skipped — event is already active. Will retry once it ends.");
                    eventTime.skipLogged = true;
                }

                return false;
            }

            eventTime.skipLogged = false;

            if (startEvent.wipeBlockSeconds > 0 && LeftWipeBlockTime(startEvent.wipeBlockSeconds) > 0)
            {
                Instance.Puts($"[WipeBlock] Skipping scheduled '{startEvent.displayName}' - " +
                              $"{FormatWipeBlockTime(LeftWipeBlockTime(startEvent.wipeBlockSeconds))} remaining");
                return false;
            }

            if (startEvent.creator == CreatorFacepunch && !string.IsNullOrEmpty(startEvent.scheduleName))
                TriggerFacepunchEvent(startEvent.scheduleName);
            else
                Instance.Server.Command(startEvent.command);

            eventTime.lastRun = DateTime.Now;
            eventTime.lastRunEventId = id;

            CallEventManagerStartedEvent(startEvent.displayName);

            if (!string.IsNullOrEmpty(Instance._config.Discord.WebHook))
                Instance.SendLogToDiscord(startEvent.displayName);

            return true;
        }

        private void EventTimer()
        {
            var now = DateTime.Now;
            var hour = now.Hour;
            var minute = now.Minute;
            var day = now.DayOfWeek;
            var playersCount = BasePlayer.activePlayerList.Count;

            foreach (var eventTime in Instance.data.eventTimes)
            {
                if (eventTime.eventsId.IsEmpty() || eventTime.minPlayers > playersCount ||
                    (eventTime.maxPlayers > 0 && eventTime.maxPlayers < playersCount)) continue;

                if (!eventTime.activeDays.IsEmpty() && !eventTime.activeDays.Contains(day))
                    continue;

                if (eventTime.eventsId.All(id =>
                        Instance.data.events.Find(e => e.eventId == id)?.scheduleEnabled == false))
                    continue;

                switch (eventTime.type)
                {
                    case EventType.Static:
                    {
                        var scheduledTime = new DateTime(now.Year, now.Month, now.Day,
                                eventTime.hour == 24 ? 0 : eventTime.hour, eventTime.minute, 0)
                            .AddMinutes(eventTime.nextOffset);

                        if (eventTime.lastRun.Date == now.Date &&
                            eventTime.lastRun.Hour == hour &&
                            eventTime.lastRun.Minute == minute)
                            continue;

                        if (hour == scheduledTime.Hour && minute == scheduledTime.Minute)
                            ExecuteScheduledEvent(eventTime);

                        break;
                    }

                    case EventType.Every:
                    case EventType.Random:
                    {
                        var delay = eventTime.interval + eventTime.nextOffset;
                        if (delay <= 0) break;

                        var nextRunTime = eventTime.lastRun.AddMinutes(delay);

                        if (now >= nextRunTime)
                        {
                            eventTime.lastRun = now;
                            ExecuteScheduledEvent(eventTime);
                            eventTime.UpdateOffset();
                        }

                        break;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            CancelInvoke(EventTimer);
        }
    }

    #endregion

    #region Commands

    private string[] ToStringArray(ConsoleSystem.Arg arg)
    {
        if (arg == null || arg.Args == null) return new string[0];
        var result = new string[arg.Args.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = arg.GetString(i);
        return result;
    }

    [ConsoleCommand("em_spawn")]
    private void cmdSpawn(ConsoleSystem.Arg arg)
    {
        if (!arg.HasArgs()) return;

        var player = arg.Player();
        if (player != null && !permission.UserHasPermission(player.UserIDString, PermissionUse)) return;

        var prefabName = string.Join(" ", ToStringArray(arg));
        if (string.IsNullOrEmpty(prefabName)) return;

        var entity = GameManager.server.CreateEntity(prefabName);
        if (entity == null) return;

        if (entity.TryGetComponent<CH47HelicopterAIController>(out var ch47))
        {
            ch47.TriggeredEventSpawn();
        }
        else if (entity.TryGetComponent<CargoShip>(out var cargo))
        {
            cargo.TriggeredEventSpawn();
        }
        else if (entity.TryGetComponent<SupplyDrop>(out var supply))
        {
            var size = TerrainMeta.Size / 3f;
            entity.transform.position = new Vector3(Random.Range(-size.x, size.x), 400f,
                Random.Range(-size.y, size.y));
        }
        else if (entity.TryGetComponent<BradleyAPC>(out _))
        {
            entity.Kill();
            var spawner = BradleySpawner.singleton;
            if (spawner == null)
            {
                PrintWarning("BradleySpawner.singleton is null — map has no Launch Site Bradley path, cannot respawn.");
                return;
            }

            if (spawner.spawned != null && spawner.spawned.IsAlive())
                spawner.spawned.Kill();
            spawner.SpawnBradley();
            return;
        }

        entity.Spawn();
    }

    [ConsoleCommand("eventmanager.reset.default")]
    private void CmdConsoleResetToDefault(ConsoleSystem.Arg arg)
    {
        if (!arg.IsServerside) return;

        data.ResetToDefault();

        SendReply(arg, "Load default data for Event Manager");
    }

    private void CmdNewEventManager(IPlayer covPlayer)
    {
        var player = covPlayer?.Object as BasePlayer;
        if (player == null) return;

        if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
        {
            PrintError($"Player {player.UserIDString} don't have permission {PermissionUse} to use /em.");
            return;
        }

        openedUI[player.userID] = new OpenedPlayerData();
        DrawMainUI(player);
    }

    [ConsoleCommand(Command)]
    private void CmdNewConsoleEventManager(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null || !arg.HasArgs() ||
            !permission.UserHasPermission(player.UserIDString, PermissionUse)) return;

        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        switch (arg.GetString(0))
        {
            case "close":
            {
                CuiHelper.DestroyUi(player, LayerBackground);

                openedUI.Remove(player.userID);
                break;
            }

            case "search":
            {
                adminState.eventSearch = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : null;
                if (string.IsNullOrWhiteSpace(adminState.eventSearch) ||
                    adminState.eventSearch == Msg(player, UIPlaceholderSearchEvent))
                    adminState.eventSearch = null;

                var container = new CuiElementContainer();
                DrawCustomEventResults(player, container, adminState);
                CuiHelper.AddUi(player, container);
                break;
            }

            case "togglecustomfilter":
            {
                adminState.showAllCustomEvents = !adminState.showAllCustomEvents;

                var container = new CuiElementContainer();
                if (IsColumnsMode(player.userID))
                    DrawColumnsPageContent(player, container);
                else
                    DrawCustomEventResults(player, container, adminState);
                CuiHelper.AddUi(player, container);
                break;
            }

            case "toggleviewmode":
            {
                SetColumnsMode(player.userID, !IsColumnsMode(player.userID));
                DrawMainUI(player, true, false);
                break;
            }

            case "creatorsscroll":
            {
                if (arg.Args.Length > 1)
                {
                    var dir = arg.GetString(1);
                    if (dir == "left") adminState.creatorsPage = Mathf.Max(0, adminState.creatorsPage - 1);
                    else if (dir == "right") adminState.creatorsPage++;
                    else if (dir == "goto" && arg.Args.Length > 2 && int.TryParse(arg.GetString(2), out var p))
                        adminState.creatorsPage = p;
                }
            
            var container = new CuiElementContainer();
            DrawColumnsPageContent(player, container);
            CuiHelper.AddUi(player, container);
                break;
            }

            case "events":
            {
                if (arg.Args.Length > 1 && arg.GetString(1) == "rescan")
                {
                    CheckIsEventsLoaded();
                    SyncFacepunchEvents();
                    RebuildEventCache();

                    var container = new CuiElementContainer();

        var isColumns = IsColumnsMode(player.userID);
        if (isColumns)
        {
            DrawColumnsPageContent(player, container);
        }
        else
        {
            DrawDefaultPageContent(player, container, adminState);
        }

                    CuiHelper.AddUi(player, container);
                }

                break;
            }

            case "page":
            {
                if (!bool.TryParse(arg.GetString(1), out var isDefaultPage)) return;

                if (!isDefaultPage)
                {
                    adminState.editTime = new EventTime();
                    adminState.editTimeIndex = -1;
                    adminState.editIndex = -1;
                    adminState.isCreatingNewTimer = true;
                    adminState.editTime.eventsId = new List<int>();
                }

                DrawMainUI(player, isDefaultPage, false);

                break;
            }

            case "next":
            {
                var pages = Mathf.CeilToInt(data.creators.Count / (float) ColumnsOnPage);

                if (adminState.page + 1 < pages)
                    adminState.page++;

                var container = new CuiElementContainer();

        var isColumns = IsColumnsMode(player.userID);
        if (isColumns)
        {
            DrawColumnsPageContent(player, container);
        }
        else
        {
            DrawDefaultPageContent(player, container, adminState);
        }
                CuiHelper.AddUi(player, container);
                break;
            }

            case "prev":
            {
                adminState.page = Mathf.Max(0, adminState.page - 1);

                var container = new CuiElementContainer();

        var isColumns = IsColumnsMode(player.userID);
        if (isColumns)
        {
            DrawColumnsPageContent(player, container);
        }
        else
        {
            DrawDefaultPageContent(player, container, adminState);
        }
                CuiHelper.AddUi(player, container);
                break;
            }

            case "event":
            {
                switch (arg.GetString(1))
                {
                    case "close":
                    {
                        if (IsEditEventDirty(adminState))
                        {
                            DrawSubModalUnsavedChanges(player, "edit_event");
                        }
                        else
                        {
                            adminState.editEvent = null;
                            adminState.editEventSnapshot = null;
                            CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        }

                        break;
                    }

                    case "unsaved_close":
                    {
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        break;
                    }

                    case "unsaved_discard":
                    {
                        if (adminState.editEvent != null && adminState.editEventSnapshot != null)
                        {
                            var live = adminState.editEvent;
                            var snap = adminState.editEventSnapshot;
                            live.displayName = snap.displayName;
                            live.command = snap.command;
                            live.pluginName = snap.pluginName;
                            live.creator = snap.creator;
                            live.scheduleEnabled = snap.scheduleEnabled;
                            live.wipeBlockSeconds = snap.wipeBlockSeconds;
                            data.SaveEvents();
                            DisableVanillaEvents();
                        }

                        adminState.editEvent = null;
                        adminState.editEventSnapshot = null;
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        break;
                    }

                    case "unsaved_save":
                    {
                        data.SaveEvents();
                        CheckIsEventsLoaded();
                        UpdateEventList();
                        var unsavedWasFacepunch = adminState.editEvent?.creator == CreatorFacepunch;
                        adminState.editEvent = null;
                        adminState.editEventSnapshot = null;
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);

                        if (IsColumnsMode(player.userID)) DrawMainUI(player, true, false);
                        else RefreshEventSection(player, adminState, unsavedWasFacepunch);
                        break;
                    }

                    case "edit":
                    {
                        if (!int.TryParse(arg.GetString(2), out var eventId)) return;

                        var targetEvent = data.events.Find(e => e.eventId == eventId);
                        if (targetEvent == null) return;

                        adminState.editEvent = targetEvent;
                        adminState.editEventSnapshot = DeepClone(targetEvent);
                        adminState.editTime = null;
                        adminState.editTimeIndex = -1;
                        adminState.editIndex = -1;
                        adminState.isCreatingNewTimer = false;
                        DrawModal(player, "edit");
                        break;
                    }

                    case "schedule":
                    {
                        if (arg.Args.Length > 2 && arg.GetString(2) == "toggle" && adminState.editEvent != null)
                        {
                            adminState.editEvent.scheduleEnabled = !adminState.editEvent.scheduleEnabled;
                            data.SaveEvents();
                            DrawModal(player, "edit", false);
                        }

                        break;
                    }

                    case "default_schedule":
                    {
                        if (arg.Args.Length > 2 && arg.GetString(2) == "toggle"
                                                && adminState.editEvent != null
                                                && adminState.editEvent.creator == CreatorFacepunch)
                        {
                            var key = AutoEventKey(adminState.editEvent.displayName);
                            var current = !_config.AutoEvents.TryGetValue(key, out var v) || v;
                            _config.AutoEvents[key] = !current;
                            SaveConfig();
                            DisableVanillaEvents();
                            DrawModal(player, "edit", false);
                        }

                        break;
                    }

                    case "new":
                    {
                        var creatorName = arg.Args.Length > 2 ? string.Join(" ", arg.Args.Skip(2)) : null;
                        adminState.editEvent = new Event {isCustom = true, creator = creatorName};
                        adminState.editEventSnapshot = DeepClone(adminState.editEvent);
                        adminState.isEditingEvent = false;
                        DrawModalCreateEvent(player);
                        break;
                    }

                    case "edit_info":
                    {
                        if (!int.TryParse(arg.GetString(2), out var editInfoId)) return;
                        var editInfoEvent = data.events.Find(e => e.eventId == editInfoId);
                        if (editInfoEvent == null) return;
                        adminState.editEvent = editInfoEvent;
                        adminState.editEventSnapshot = DeepClone(editInfoEvent);
                        adminState.isEditingEvent = true;
                        DrawModalCreateEvent(player);
                        break;
                    }

                    case "save":
                    {
                        var ev = adminState.editEvent;
                        if (string.IsNullOrWhiteSpace(ev.displayName))
                        {
                            SendNotify(player, MsgEnterDisplayName, 1);
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(ev.command))
                        {
                            SendNotify(player, MsgEnterCommand, 1);
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(ev.pluginName))
                        {
                            SendNotify(player, MsgEnterPluginName, 1);
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(ev.creator))
                        {
                            SendNotify(player, MsgChooseAuthor, 1);
                            return;
                        }

                        if (!adminState.isEditingEvent)
                            data.events.Add(ev);

                        if (!data.creators.Contains(ev.creator))
                            data.creators.Add(ev.creator);

                        data.SaveEvents();
                        CheckIsEventsLoaded();
                        RebuildEventCache();
                        UpdateEventList();
                        var savedWasFacepunch = ev.creator == CreatorFacepunch;
                        adminState.editEvent = null;
                        adminState.editEventSnapshot = null;
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);

                        if (IsColumnsMode(player.userID)) DrawMainUI(player, true, false);
                        else RefreshEventSection(player, adminState, savedWasFacepunch);
                        break;
                    }

                    case "remove":
                    {
                        if (!int.TryParse(arg.GetString(2), out var eventId)) return;
                        if (data.events.Find(e => e.eventId == eventId) == null) return;

                        DrawConfirmModal(player,
                            Msg(player, UIConfirmTitleDeleteEvent),
                            Msg(player, UIConfirmDescDeleteEvent),
                            Command + " event remove_confirm " + eventId, selfClose: true);
                        break;
                    }

                    case "remove_confirm":
                    {
                        if (!int.TryParse(arg.GetString(2), out var eventId)) return;

                        var targetEvent = data.events.Find(e => e.eventId == eventId);
                        if (targetEvent == null) return;

                        for (var i = 0; i < data.eventTimes.Count; i++)
                        {
                            var time = data.eventTimes.ElementAt(i);
                            if (time == null) continue;
                            if (time.eventsId.Contains(eventId))
                            {
                                time.eventsId.Remove(eventId);
                                if (time.eventsId.Count == 0)
                                {
                                    data.eventTimes.Remove(time);
                                    i--;
                                }
                            }
                        }

                        var wasFacepunch = targetEvent.creator == CreatorFacepunch;
                        data.events.Remove(targetEvent);
                        data.SaveEvents();
                        data.SaveTimes();
                        RebuildEventCache();
                        UpdateEventList();
                        CuiHelper.DestroyUi(player, LayerModal + ".Confirm");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        if (IsColumnsMode(player.userID)) DrawMainUI(player, true, false);
                        else RefreshEventSection(player, adminState, wasFacepunch);
                        break;
                    }

                    case "add":
                    {
                        var input = string.Join(" ", arg.Args.Skip(3));

                        switch (arg.GetString(2))
                        {
                            case "name":
                            {
                                if (string.IsNullOrWhiteSpace(input) ||
                                    input == Msg(player, UIPlaceholderDisplayName))
                                    input = null;

                                adminState.editEvent.displayName = input;
                                break;
                            }

                            case "command":
                            {
                                if (string.IsNullOrWhiteSpace(input) ||
                                    input == Msg(player, UIPlaceholderCommand))
                                    input = null;

                                adminState.editEvent.command = input;
                                break;
                            }

                            case "plugin":
                            {
                                if (string.IsNullOrWhiteSpace(input) ||
                                    input == Msg(player, UIPlaceholderPlugin))
                                    input = null;

                                adminState.editEvent.pluginName = input;
                                break;
                            }

                            case "creator":
                            {
                                if (string.IsNullOrWhiteSpace(input) ||
                                    input == Msg(player, UIPlaceholderAuthor))
                                    input = null;

                                adminState.editEvent.creator = input;
                                break;
                            }

                            case "wipeblock":
                            {
                                adminState.editEvent.wipeBlockSeconds =
                                    double.TryParse(input, NumberStyles.Any,
                                        CultureInfo.InvariantCulture, out var wb) && wb > 0
                                        ? wb
                                        : 0;
                                break;
                            }
                        }

                        break;
                    }

                    case "time":
                    {
                        switch (arg.GetString(2))
                        {
                            case "start":
                            {
                                var eventEntry = adminState.editEvent
                                                 ?? (adminState.editTime != null
                                                     ? data.events.Find(e =>
                                                         e.eventId == adminState.editTime.eventsId[0])
                                                     : null);
                                if (eventEntry == null)
                                    return;

                                if (IsEventCurrentlyActive(eventEntry))
                                {
                                    SendNotify(player, MsgEventAlreadyActive, 1, eventEntry.displayName);
                                    return;
                                }

                                if (eventEntry.wipeBlockSeconds > 0)
                                {
                                    var left = LeftWipeBlockTime(eventEntry.wipeBlockSeconds);
                                    if (left > 0)
                                        SendNotify(player, MsgWipeBlockWarn, 1,
                                            eventEntry.displayName, FormatWipeBlockTime(left));
                                }

                                if (eventEntry.creator == CreatorFacepunch && !string.IsNullOrEmpty(eventEntry.scheduleName))
                                    TriggerFacepunchEvent(eventEntry.scheduleName);
                                else
                                    Instance.Server.Command(eventEntry.command);

                                CallEventManagerStartedEvent(eventEntry.displayName);

                                if (!string.IsNullOrEmpty(_config.Discord.WebHook))
                                    SendLogToDiscord(eventEntry.displayName);

                                DrawModal(player, "edit", false);
                                break;
                            }
                            case "stop":
                            {
                                var eventEntry = adminState.editEvent
                                                 ?? (adminState.editTime != null
                                                     ? data.events.Find(e =>
                                                         e.eventId == adminState.editTime.eventsId[0])
                                                     : null);
                                if (eventEntry == null)
                                    return;

                                StopActiveEvent(eventEntry);
                                DrawModal(player, "edit", false);
                                break;
                            }
                            case "edit":
                            {
                                if (!int.TryParse(arg.GetString(3), out var index) ||
                                    !int.TryParse(arg.GetString(4), out var index2)) return;

                                adminState.editTime = (EventTime) data.eventTimes[index].Clone();
                                adminState.editTimeIndex = index;
                                adminState.isCreatingNewTimer = false;
                                if (adminState.editEvent != null)
                                {
                                    adminState.editTime.eventsId = new List<int> {adminState.editEvent.eventId};
                                    adminState.editIndex = index2;
                                }

                                DrawModal(player, "edit", false);
                                DrawModalTime(player);
                                break;
                            }

                            case "new":
                            {
                                adminState.editTime = new EventTime();
                                adminState.editTimeIndex = -1;
                                adminState.editIndex = -1;
                                adminState.isCreatingNewTimer = true;
                                if (adminState.editEvent != null)
                                    adminState.editTime.eventsId = new List<int> {adminState.editEvent.eventId};

                                DrawModal(player, "edit", false);
                                DrawModalTime(player);
                                break;
                            }

                            case "prev":
                            {
                                if ((int) adminState.editTime.type - 1 < 0)
                                    adminState.editTime.type = EventType.Random;
                                else
                                    adminState.editTime.type = (EventType) ((int) adminState.editTime.type - 1);

                                DrawModalTime(player);
                                break;
                            }

                            case "next":
                            {
                                if ((int) adminState.editTime.type + 1 > 2)
                                    adminState.editTime.type = EventType.Static;
                                else
                                    adminState.editTime.type = (EventType) ((int) adminState.editTime.type + 1);

                                DrawModalTime(player);
                                break;
                            }

                            case "hour":
                            case "minute":
                            case "offsetmin":
                            case "offsetmax":
                            case "interval":
                            case "minplayers":
                            case "maxplayers":
                            {
                                if (!HandleTimeFieldInput(adminState, arg.GetString(2), arg.GetString(3), false))
                                    DrawModalTime(player);
                                break;
                            }

                            case "day":
                            {
                                if (!Enum.TryParse(arg.GetString(3), out DayOfWeek day)) return;

                                if (adminState.editTime.activeDays.Contains(day))
                                    adminState.editTime.activeDays.Remove(day);
                                else
                                    adminState.editTime.activeDays.Add(day);

                                var container = new CuiElementContainer();
                                DrawScheduleDays(player, container, LayerModal + ".Time", adminState,
                                    GetScheduleDaysY(adminState.editTime.type));
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "dayall":
                            {
                                if (adminState.editTime.activeDays.Count == 7)
                                    adminState.editTime.activeDays.Clear();
                                else
                                    foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)))
                                        adminState.editTime.activeDays.Add(d);

                                var container = new CuiElementContainer();
                                DrawScheduleDays(player, container, LayerModal + ".Time", adminState,
                                    GetScheduleDaysY(adminState.editTime.type));
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "save":
                            {
                                if (adminState.editTime.type == EventType.Random)
                                {
                                    adminState.editTime.interval = 0;
                                }
                                else if (adminState.editTime.type == EventType.Every)
                                {
                                    adminState.editTime.offsetMin = 0;
                                    adminState.editTime.offsetMax = 0;
                                }

                                adminState.editTime.UpdateInfo();
                                adminState.editTime.UpdateOffset();
                                adminState.editTime.isRandom = false;

                                if (adminState.editTimeIndex == -1)
                                    data.eventTimes.Add((EventTime) adminState.editTime.Clone());
                                else
                                    data.eventTimes[adminState.editTimeIndex] = (EventTime) adminState.editTime.Clone();


                                data.SaveTimes();
                                adminState.editTime = null;
                                adminState.editTimeIndex = -1;
                                adminState.editIndex = -1;
                                adminState.isCreatingNewTimer = false;
                                DrawModal(player, "edit", false);
                                break;
                            }

                            case "delete":
                            {
                                if (adminState.editTimeIndex == -1) return;
                                DrawConfirmModal(player,
                                    Msg(player, UIConfirmTitleDeleteTimer),
                                    Msg(player, UIConfirmDescDeleteTimer),
                                    Command + " event time delete_confirm");
                                break;
                            }

                            case "delete_confirm":
                            {
                                if (adminState.editTimeIndex != -1)
                                    data.eventTimes.RemoveAt(adminState.editTimeIndex);

                                data.SaveTimes();

                                adminState.editTime = null;
                                adminState.editTimeIndex = -1;
                                adminState.editIndex = -1;
                                adminState.isCreatingNewTimer = false;
                                CuiHelper.DestroyUi(player, LayerModal + ".Confirm");
                                DrawModal(player, "edit", false);
                                break;
                            }
                        }

                        break;
                    }
                }

                break;
            }

            case "creator":
            {
                switch (arg.GetString(1))
                {
                    case "close":
                    {
                        if (IsCreatorDirty(adminState))
                        {
                            DrawSubModalUnsavedChanges(player, "creator");
                        }
                        else
                        {
                            adminState.inputCreator = null;
                            adminState.editCreatorIndex = -1;
                            CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        }

                        break;
                    }

                    case "unsaved_close":
                    {
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        break;
                    }

                    case "unsaved_discard":
                    {
                        adminState.inputCreator = null;
                        adminState.editCreatorIndex = -1;
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        break;
                    }

                    case "unsaved_save":
                    {
                        if (!string.IsNullOrWhiteSpace(adminState.inputCreator))
                        {
                            if (adminState.editCreatorIndex != -1)
                            {
                                var oldCreator = data.creators[adminState.editCreatorIndex];
                                foreach (var eventEntry in data.events)
                                    if (eventEntry.creator == oldCreator)
                                        eventEntry.creator = adminState.inputCreator;
                                data.creators[adminState.editCreatorIndex] = adminState.inputCreator;
                                data.SaveEvents();
                            }
                            else
                            {
                                data.creators.Add(adminState.inputCreator);
                            }

                            data.SaveCreators();
                        }

                        RebuildEventCache();
                        adminState.inputCreator = null;
                        adminState.editCreatorIndex = -1;
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        DrawMainUI(player);
                        break;
                    }

                    case "add":
                    {
                        adminState.editCreatorIndex = -1;
                        DrawModal(player, "new_creator");
                        break;
                    }

                    case "edit":
                    {
                        var name = string.Join(" ", arg.Args.Skip(2));
                        adminState.editCreatorIndex = data.creators.FindIndex(n => n == name);

                        if (adminState.editCreatorIndex == -1)
                            return;

                        adminState.inputCreator = name;
                        DrawModal(player, "edit_creator");
                        break;
                    }

                    case "name":
                    {
                        var name = string.Join(" ", arg.Args.Skip(2));
                        adminState.inputCreator = name;

                        var container = new CuiElementContainer();
                        DrawModalCreator(player, container, name);
                        CuiHelper.AddUi(player, container);
                        break;
                    }

                    case "save":
                    {
                        if (adminState.editCreatorIndex != -1)
                        {
                            var oldCreator = data.creators[adminState.editCreatorIndex];

                            foreach (var eventEntry in data.events)
                                if (eventEntry.creator == oldCreator)
                                    eventEntry.creator = adminState.inputCreator;

                            data.creators[adminState.editCreatorIndex] = adminState.inputCreator;

                            data.SaveEvents();
                            data.SaveCreators();
                            RebuildEventCache();
                            DrawMainUI(player);

                            break;
                        }

                        var name = string.Join(" ", arg.Args.Skip(2));
                        data.creators.Add(name);
                        data.SaveCreators();
                        RebuildEventCache();
                        DrawMainUI(player);
                        break;
                    }
                }

                break;
            }


            case "random":
            {
                switch (arg.GetString(1))
                {
                    case "open":
                    {
                        adminState.editTime = null;
                        adminState.editTimeSnapshot = null;
                        adminState.editTimeIndex = -1;
                        adminState.editIndex = -1;
                        adminState.isCreatingNewTimer = false;
                        DrawRandomPoolModal(player);
                        break;
                    }

                    case "close":
                    {
                        if (IsRandomTimeDirty(adminState))
                        {
                            DrawSubModalUnsavedChanges(player, "random_pool");
                        }
                        else
                        {
                            adminState.editTime = null;
                            adminState.editTimeSnapshot = null;
                            CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        }

                        break;
                    }

                    case "unsaved_close":
                    {
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        break;
                    }

                    case "unsaved_discard":
                    {
                        adminState.editTime = null;
                        adminState.editTimeSnapshot = null;
                        adminState.editTimeIndex = -1;
                        adminState.editIndex = -1;
                        adminState.isCreatingNewTimer = false;
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        break;
                    }

                    case "unsaved_save":
                    {
                        if (adminState.editTime != null)
                        {
                            if (adminState.editTime.type == EventType.Random)
                            {
                                adminState.editTime.interval = 0;
                            }
                            else if (adminState.editTime.type == EventType.Every)
                            {
                                adminState.editTime.offsetMin = 0;
                                adminState.editTime.offsetMax = 0;
                            }

                            adminState.editTime.UpdateInfo();
                            adminState.editTime.UpdateOffset();
                            adminState.editTime.isRandom = true;

                            if (adminState.editTimeIndex == -1)
                                data.eventTimes.Add((EventTime) adminState.editTime.Clone());
                            else
                                data.eventTimes[adminState.editTimeIndex] = (EventTime) adminState.editTime.Clone();

                            data.SaveTimes();
                        }

                        adminState.editTime = null;
                        adminState.editTimeSnapshot = null;
                        adminState.editTimeIndex = -1;
                        adminState.editIndex = -1;
                        adminState.isCreatingNewTimer = false;
                        CuiHelper.DestroyUi(player, LayerModal + ".UnsavedChanges");
                        CuiHelper.DestroyUi(player, LayerBackgroundModal);
                        break;
                    }

                    case "time":
                    {
                        switch (arg.GetString(2))
                        {
                            case "new":
                            {
                                adminState.editTime = new EventTime();
                                adminState.editTimeSnapshot = null;
                                adminState.editTimeIndex = -1;
                                adminState.editIndex = -1;
                                adminState.isCreatingNewTimer = true;
                                adminState.editTime.eventsId = new List<int>();

                                var container = new CuiElementContainer();
                                DrawActiveSchedule(player, container);
                                DrawRandomTime(player, container);
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "edit":
                            {
                                if (!int.TryParse(arg.GetString(3), out var index) ||
                                    !int.TryParse(arg.GetString(4), out var index2)) return;

                                adminState.editTime = (EventTime) data.eventTimes[index].Clone();
                                adminState.editTime.eventsId = new List<int>(data.eventTimes[index].eventsId);
                                adminState.editTimeSnapshot = DeepClone(data.eventTimes[index]);
                                var container = new CuiElementContainer();
                                adminState.editTimeIndex = index;
                                adminState.isCreatingNewTimer = false;
                                adminState.editIndex = index2;

                                DrawActiveSchedule(player, container);
                                DrawRandomTime(player, container);
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "prev":
                            {
                                if ((int) adminState.editTime.type - 1 < 0)
                                    adminState.editTime.type = EventType.Random;
                                else
                                    adminState.editTime.type = (EventType) ((int) adminState.editTime.type - 1);

                                var container = new CuiElementContainer();
                                DrawRandomTimeInner(player, container, LayerModal + ".Time");
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "next":
                            {
                                if ((int) adminState.editTime.type + 1 > 2)
                                    adminState.editTime.type = EventType.Static;
                                else
                                    adminState.editTime.type = (EventType) ((int) adminState.editTime.type + 1);

                                var container = new CuiElementContainer();
                                DrawRandomTimeInner(player, container, LayerModal + ".Time");
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "hour":
                            case "minute":
                            case "offsetmin":
                            case "offsetmax":
                            case "interval":
                            case "minplayers":
                            case "maxplayers":
                            {
                                if (!HandleTimeFieldInput(adminState, arg.GetString(2), arg.GetString(3), true))
                                {
                                    var container = new CuiElementContainer();
                                    DrawRandomTime(player, container);
                                    CuiHelper.AddUi(player, container);
                                }

                                break;
                            }

                            case "day":
                            {
                                if (!Enum.TryParse(arg.GetString(3), out DayOfWeek day)) return;

                                if (adminState.editTime.activeDays.Contains(day))
                                    adminState.editTime.activeDays.Remove(day);
                                else
                                    adminState.editTime.activeDays.Add(day);

                                var container = new CuiElementContainer();
                                DrawRandomTimeInner(player, container, LayerModal + ".Time");
                                CuiHelper.AddUi(player, container);

                                break;
                            }

                            case "dayall":
                            {
                                if (adminState.editTime.activeDays.Count == 7)
                                    adminState.editTime.activeDays.Clear();
                                else
                                    foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)))
                                        adminState.editTime.activeDays.Add(d);

                                var container = new CuiElementContainer();
                                DrawRandomTimeInner(player, container, LayerModal + ".Time");
                                CuiHelper.AddUi(player, container);
                                break;
                            }

                            case "save":
                            {
                                if (adminState.editTime.type == EventType.Random)
                                {
                                    adminState.editTime.interval = 0;
                                }
                                else if (adminState.editTime.type == EventType.Every)
                                {
                                    adminState.editTime.offsetMin = 0;
                                    adminState.editTime.offsetMax = 0;
                                }

                                adminState.editTime.UpdateInfo();
                                adminState.editTime.UpdateOffset();
                                adminState.editTime.isRandom = true;

                                if (adminState.editTimeIndex == -1)
                                    data.eventTimes.Add((EventTime) adminState.editTime.Clone());
                                else
                                    data.eventTimes[adminState.editTimeIndex] = (EventTime) adminState.editTime.Clone();

                                data.SaveTimes();
                                adminState.editTime = null;
                                adminState.editTimeIndex = -1;
                                adminState.editIndex = -1;
                                adminState.isCreatingNewTimer = false;
                                DrawRandomPoolModal(player, false);

                                break;
                            }

                            case "delete":
                            {
                                if (adminState.editTimeIndex == -1) return;
                                DrawConfirmModal(player,
                                    Msg(player, UIConfirmTitleDeleteTimer),
                                    Msg(player, UIConfirmDescDeleteTimer),
                                    Command + " random time delete_confirm");
                                break;
                            }

                            case "delete_confirm":
                            {
                                if (adminState.editTimeIndex != -1)
                                    data.eventTimes.RemoveAt(adminState.editTimeIndex);

                                adminState.editTime = null;
                                adminState.editTimeIndex = -1;
                                adminState.editIndex = -1;
                                adminState.isCreatingNewTimer = false;

                                data.SaveTimes();

                                CuiHelper.DestroyUi(player, LayerModal + ".Confirm");
                                DrawRandomPoolModal(player, false);
                                break;
                            }
                        }

                        break;
                    }

                    case "events":
                    {
                        switch (arg.GetString(2))
                        {
                            case "show":
                            {
                                DrawSelectEvent(player);
                                break;
                            }

                            case "next":
                            {
                                var pages = Mathf.CeilToInt(data.creators.Count / (float) ColumnsOnPageSelect);

                                if (adminState.page + 1 < pages)
                                    adminState.page++;

                                DrawSelectEvent(player);
                                break;
                            }

                            case "prev":
                            {
                                adminState.page = Mathf.Max(0, adminState.page - 1);
                                DrawSelectEvent(player);
                                break;
                            }

                            case "select":
                            {
                                if (!int.TryParse(arg.GetString(3), out var eventId)) return;

                                if (data.events.Find(e => e.eventId == eventId) == null) return;

                                if (!adminState.editTime.eventsId.Contains(eventId))
                                    adminState.editTime.eventsId.Add(eventId);

                                DrawMainUI(player, true, false);
                                DrawRandomPoolModal(player, false);

                                break;
                            }

                            case "back":
                            {
                                DrawMainUI(player, true, false);
                                DrawRandomPoolModal(player, false);
                                break;
                            }

                            case "remove":
                            {
                                if (!int.TryParse(arg.GetString(3), out var eventId)) return;

                                adminState.editTime.eventsId.Remove(eventId);

                                var container = new CuiElementContainer();
                                DrawRandomTime(player, container);
                                CuiHelper.AddUi(player, container);

                                break;
                            }

                            case "toggle":
                            {
                                if (!int.TryParse(arg.GetString(3), out var eventId)) return;
                                if (data.events.Find(e => e.eventId == eventId) == null) return;

                                if (adminState.editTime.eventsId.Contains(eventId))
                                    adminState.editTime.eventsId.Remove(eventId);
                                else
                                    adminState.editTime.eventsId.Add(eventId);

                                var container = new CuiElementContainer();
                                DrawRandomTime(player, container);
                                CuiHelper.AddUi(player, container);

                                break;
                            }

                            case "search":
                            {
                                adminState.eventPoolSearch = arg.Args.Length > 3 ? arg.GetString(3) : null;

                                var container = new CuiElementContainer();
                                DrawRandomTime(player, container);
                                CuiHelper.AddUi(player, container);

                                break;
                            }
                        }

                        break;
                    }
                }

                break;
            }
        }
    }

    #endregion

    #region Interface

    private const string
        Command = "UI_EventsManager",
        LayerBackground = "EventsManager.Background",
        Layer = "EventsManager.Main",
        LayerBackgroundModal = "EventsManager.Background.Modal",
        LayerModal = "EventsManager.Main.Modal";

    private const string
        BTN_GREEN = "#5D7238",
        BTN_GREEN_HOVER = "#738D45",
        BTN_GREEN_PRESSED = "#4A5B2D",
        BTN_RED = "#AA4735",
        BTN_RED_HOVER = "#C2543F",
        BTN_RED_PRESSED = "#8A392B",
        BTN_BLUE = "#175782",
        BTN_BLUE_HOVER = "#1F6DA1",
        BTN_BLUE_PRESSED = "#0E3D5C",
        BTN_DARK = "#222222",
        BTN_DARK_HOVER = "#323232",
        BTN_DARK_PRESSED = "#1A1A1A",
        BTN_GRAY = "#40403D",
        BTN_GRAY_HOVER = "#4F4F4B",
        BTN_GRAY_PRESSED = "#333331";

    private const int TIME_SEPARATOR_GAP = 10,
        TIME_SEPARATOR_HEIGHT = 2,
        TIME_SEPARATOR_TOTAL = TIME_SEPARATOR_GAP + TIME_SEPARATOR_HEIGHT + TIME_SEPARATOR_GAP,
        TIME_SECTION_TYPE_HEIGHT = 32,
        TIME_SECTION_DUAL_INPUT_HEIGHT = 46,
        TIME_SECTION_SINGLE_INPUT_HEIGHT = 46;

    private const int SCHEDULE_DAYS_TOTAL_ITEMS = 8,
        SCHEDULE_DAYS_COLUMNS = 4,
        SCHEDULE_DAYS_COLUMN_WIDTH = 106,
        SCHEDULE_DAYS_CELL_WIDTH = 90,
        SCHEDULE_DAYS_ROW_HEIGHT = 26,
        SCHEDULE_DAYS_CELL_HEIGHT = 16,
        SCHEDULE_DAYS_FIRST_ROW_Y = -42,
        SCHEDULE_DAYS_CHECKBOX_SIZE = 16,
        SCHEDULE_DAYS_LABEL_LEFT = 20,
        SCHEDULE_DAYS_HEIGHT = 84;

    private const float PAGE_RANDOM_SECTION_H = 66f,
        PAGE_SECTION_GAP = 0f,
        PAGE_FP_HEADER_H = 86f,
        PAGE_FP_ROW_H = 38f,
        PAGE_FP_ROW_GAP = 2f,
        PAGE_FP_BOTTOM_PAD = 20f,
        PAGE_CUSTOM_HEADER_H = 96f,
        PAGE_CUSTOM_SEARCH_H = 32f,
        PAGE_CUSTOM_SEARCH_BOTTOM_GAP = 10f,
        PAGE_CUSTOM_ROW_H = 44f,
        PAGE_CUSTOM_ROW_GAP = 2f,
        PAGE_CUSTOM_BOTTOM_PAD = 20f;

    private const int ColumnsOnPage = 5, ColumnsOnPageSelect = 6;

    private Dictionary<ulong, OpenedPlayerData> openedUI = new();

    #region View Preferences
    private Dictionary<ulong, bool> columnsViewPrefs = new();
    private const string ViewPrefsPath = "EventManager/ViewPreferences";

    private bool IsColumnsMode(ulong id) => columnsViewPrefs.TryGetValue(id, out var v) && v;

    private void SetColumnsMode(ulong id, bool v)
    {
        columnsViewPrefs[id] = v;
        Interface.Oxide.DataFileSystem.WriteObject(ViewPrefsPath, columnsViewPrefs);
    }

    private void LoadViewPrefs()
    {
        try
        {
            columnsViewPrefs = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, bool>>(ViewPrefsPath) ??
                               new Dictionary<ulong, bool>();
        }
        catch
        {
            columnsViewPrefs = new Dictionary<ulong, bool>();
        }
    }
    #endregion

    private class OpenedPlayerData
    {
        public string inputCreator;
        public int editCreatorIndex = -1;
        public string deleteCreatorName;

        public Event editEvent;
        public Event editEventSnapshot;
        public EventTime editTime;
        public EventTime editTimeSnapshot;
        public int editTimeIndex = -1;
        public int editIndex = -1;

        public bool isEditingEvent;
        public bool manageAuthorsFromEvent;
        public bool isCreatingNewTimer;

        public int page;
        public int settingsPage;
        public string eventPoolSearch;
        public string eventSearch;
        public bool showAllCustomEvents = true;
        public int creatorsPage;
    }

    private void DrawBackground(BasePlayer player, CuiElementContainer container, string layer, string parent)
    {
        container.Add(new CuiElement
        {
            Name = layer,
            DestroyUi = layer,
            Parent = parent,
            Components =
            {
                new CuiImageComponent
                {
                    Color = "0 0 0 0.95",
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    Sprite = "assets/content/ui/UI.Background.TileTex.psd"
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                },
                new CuiNeedsCursorComponent(),
                new CuiNeedsKeyboardComponent()
            }
        });
    }

    private void DrawMainPanel(BasePlayer player, CuiElementContainer container, string anchorMin, string anchorMax,
        string offsetMin, string offsetMax, string layer, string parent)
    {
        container.Add(new CuiPanel
        {
            Image =
            {
                Color = HexToCuiColor("#2C2C2C")
            },
            RectTransform =
            {
                AnchorMin = anchorMin,
                AnchorMax = anchorMax,
                OffsetMin = offsetMin,
                OffsetMax = offsetMax
            }
        }, parent, layer, layer);
    }

    private void DrawMainUI(BasePlayer player, bool isDefaultPage = true, bool first = true)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        if (_creatorNames == null || _grouped == null) RebuildEventCache();

        var container = new CuiElementContainer();

        if (first)
            DrawBackground(player, container, LayerBackground, "Overlay");

        var isColumns = IsColumnsMode(player.userID);
        var panelOffset = isColumns ? "-505 -281" : "-340 -240";
        var panelSize = isColumns ? "505 281" : "340 240";
        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", panelOffset, panelSize, Layer, LayerBackground);

        #region Header

        var headerLayer = Layer + ".Header";

        container.Add(new CuiPanel
        {
            CursorEnabled = false,
            Image =
            {
                Color = HexToCuiColor("#1B1B1B")
            },
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -42", OffsetMax = "0 0"}
        }, Layer, headerLayer, headerLayer);

        container.Add(new CuiElement
        {
            Parent = headerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UITitle), Font = "robotocondensed-bold.ttf", FontSize = 20,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{(isColumns ? 20 : 40)} 0", OffsetMax = "-460 0"}
            }
        });

        #region Btn.Close

        MeventCuiHelper.AddIconButton(container, headerLayer, Layer + ".Header.Close",
            Command + " close", Layer, HexToCuiColor(BTN_DARK), hoverColor: HexToCuiColor(BTN_DARK_HOVER),
            pressedColor: HexToCuiColor(BTN_DARK_PRESSED),
            anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: $"{(isColumns ? -96 : -116)} -14", offsetMax: $"{(isColumns ? -20 : -40)} 14",
            iconSprite: "assets/icons/exit.png", iconColor: HexToCuiColor("#FFFFFF", 60),
            text: Msg(player, UIBtnClose), textColor: HexToCuiColor("#FFFFFF", 60));

        #endregion

        #region ViewMode Toggle

        var toggleParent = headerLayer + ".ViewToggle";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#000000", 0)},
            RectTransform =
            {
              AnchorMin = "1 0",
              AnchorMax = "1 1",
              OffsetMin = $"{(isColumns ? -386 : -406)} 0",
              OffsetMax = $"{(isColumns ? -106 : -126)} 0"
            }
        }, headerLayer, toggleParent, toggleParent);

        container.Add(new CuiElement
        {
            Parent = toggleParent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = "VIEW MODE:", Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleRight, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "1 0.5", AnchorMax = "1 0.5",
                    OffsetMin = "-278 -21", OffsetMax = "-154 21"
                }
            }
        });

        MeventCuiHelper.AddIconButton(container, toggleParent, toggleParent + ".List",
            Command + " toggleviewmode", null,
            HexToCuiColor(!isColumns ? BTN_BLUE : BTN_DARK),
            "1 0.5", "1 0.5", "-64 -14", "0 14",
            null, HexToCuiColor("#FFFFFF", 100),
            Msg(player, UIViewModeList), HexToCuiColor("#ECE3DB"),
            fontSize: 10, iconPng: GetImage("EM_IconList"),
            hoverColor: HexToCuiColor(!isColumns ? BTN_BLUE_HOVER : BTN_DARK_HOVER),
            pressedColor: HexToCuiColor(!isColumns ? BTN_BLUE_PRESSED : BTN_DARK_PRESSED));

        MeventCuiHelper.AddIconButton(container, toggleParent, toggleParent + ".Columns",
            Command + " toggleviewmode", null,
            HexToCuiColor(isColumns ? BTN_BLUE : BTN_DARK),
            "1 0.5", "1 0.5", "-149 -14", "-69 14",
            null, HexToCuiColor("#FFFFFF", 100),
            Msg(player, UIViewModeColumns), HexToCuiColor("#ECE3DB", 90),
            fontSize: 10, iconPng: GetImage("EM_IconColumns"),
            hoverColor: HexToCuiColor(isColumns ? BTN_BLUE_HOVER : BTN_DARK_HOVER),
            pressedColor: HexToCuiColor(isColumns ? BTN_BLUE_PRESSED : BTN_DARK_PRESSED));

        #endregion

        #endregion

        var footerLayer = Layer + ".Footer";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 42"}
        }, Layer, footerLayer, footerLayer);

        container.Add(new CuiElement
        {
            Parent = footerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomPoolFooterSupport), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleRight, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
              AnchorMin = "1 0",
              AnchorMax = "1 1",
              OffsetMin = $"{-(isColumns ? 360 : 380)} 0",
              OffsetMax = $"{(isColumns ? -20 : -40)} 0"
                }
            }
        });

        #region Server Time

        container.Add(new CuiElement
        {
            Name = headerLayer + ".Time",
            DestroyUi = headerLayer + ".Time",
            Parent = footerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIServerTimeCountdown), Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
              AnchorMin = "0 0",
              AnchorMax = "0 1",
              OffsetMin = $"{(isColumns ? 20 : 40)} 0",
              OffsetMax = $"{(isColumns ? 250 : 270)} 0"
                },
                new CuiCountdownComponent
                {
                    EndTime = (float) new TimeSpan(24, 0, 0).TotalSeconds,
                    StartTime = (float) DateTime.Now.TimeOfDay.TotalSeconds,
                    Step = 1,
                    TimerFormat = TimerFormat.HoursMinutesSeconds,
                    DestroyIfDone = true,
                    Command = "UI_EventManager_UpdateTimer"
                }
            }
        });

        #endregion

        if (isColumns)
            DrawColumnsPageContent(player, container);
        else
            DrawDefaultPageContent(player, container, adminState);

        CuiHelper.AddUi(player, container);
    }

    private void DrawDefaultPageContent(BasePlayer player, CuiElementContainer container, OpenedPlayerData adminState)
    {
        var facepunchEvents = data.events.FindAll(e => e.creator == CreatorFacepunch);

        var customEventsAll = data.events.FindAll(e => e.creator != CreatorFacepunch);
        var customEvents = string.IsNullOrEmpty(adminState.eventSearch)
            ? customEventsAll
            : customEventsAll.FindAll(e =>
                e.displayName != null &&
                e.displayName.IndexOf(adminState.eventSearch, StringComparison.OrdinalIgnoreCase) >= 0);

        var fpRows = Mathf.CeilToInt(facepunchEvents.Count / 2f);
        var fpHeight = PAGE_FP_HEADER_H + (fpRows > 0 ? fpRows * PAGE_FP_ROW_H + (fpRows - 1) * PAGE_FP_ROW_GAP : 0) +
                       PAGE_FP_BOTTOM_PAD;

        var customRows = Mathf.CeilToInt(customEvents.Count / 2f);
        var customHeight = PAGE_CUSTOM_HEADER_H + PAGE_CUSTOM_SEARCH_H + PAGE_CUSTOM_SEARCH_BOTTOM_GAP +
                           (customRows > 0
                               ? customRows * PAGE_CUSTOM_ROW_H + (customRows - 1) * PAGE_CUSTOM_ROW_GAP
                               : 0) + PAGE_CUSTOM_BOTTOM_PAD;

        var contentHeight = PAGE_RANDOM_SECTION_H + PAGE_SECTION_GAP + fpHeight + PAGE_SECTION_GAP + customHeight;

        var scrollView = Layer + ".Scrollview";
        var scrollWrap = scrollView + "Wrap";
        container.Add(new CuiElement
        {
            Name = scrollWrap,
            DestroyUi = scrollWrap,
            Parent = Layer,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                    OffsetMin = "-320 -438", OffsetMax = "334 -62"
                }
            }
        });

        container.Add(new CuiElement
        {
            Name = scrollView,
            DestroyUi = scrollView,
            Parent = scrollWrap,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiScrollViewComponent
                {
                    MovementType = ScrollRect.MovementType.Clamped,
                    Vertical = true, Inertia = true, Horizontal = false,
                    Elasticity = 0.25f, DecelerationRate = 0.3f, ScrollSensitivity = 24f,
                    ContentTransform = new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = $"0 -{contentHeight}", OffsetMax = "0 0"
                    },
                    VerticalScrollbar = new CuiScrollbar
                    {
                        AutoHide = true, Size = 4,
                        HandleColor = HexToCuiColor("#676767"),
                        HighlightColor = HexToCuiColor("#676767"),
                        PressedColor = HexToCuiColor("#676767"),
                        HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                        TrackColor = HexToCuiColor("#282828"),
                        TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                    }
                }
            }
        });

        var y = 0f;

        DrawRandomTeaser(player, container, scrollView, y, PAGE_RANDOM_SECTION_H);
        y -= PAGE_RANDOM_SECTION_H + PAGE_SECTION_GAP;

        DrawFacepunchSection(player, container, scrollView, y, fpHeight, facepunchEvents, PAGE_FP_HEADER_H,
            PAGE_FP_ROW_H, PAGE_FP_ROW_GAP);
        y -= fpHeight + PAGE_SECTION_GAP;

        DrawCustomEventsSection(player, container, scrollView, y, customHeight, adminState);
    }

    private void RefreshEventSection(BasePlayer player, OpenedPlayerData adminState, bool facepunch)
    {
        var facepunchBucket = _grouped.TryGetValue(CreatorFacepunch, out var fpBucketRef) ? fpBucketRef : null;
        var facepunchEvents = facepunchBucket != null
            ? facepunchBucket.ConvertAll(i => data.events[i])
            : new List<Event>();

        var customCount = 0;
        foreach (var name in _creatorNames)
        {
            if (name == CreatorFacepunch) continue;
            foreach (var idx in _grouped[name])
            {
                if (string.IsNullOrEmpty(adminState.eventSearch) ||
                    data.events[idx].displayName != null &&
                    data.events[idx].displayName.IndexOf(adminState.eventSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    customCount++;
            }
        }

        var fpRows = Mathf.CeilToInt(facepunchEvents.Count / 2f);
        var fpHeight = PAGE_FP_HEADER_H + (fpRows > 0 ? fpRows * PAGE_FP_ROW_H + (fpRows - 1) * PAGE_FP_ROW_GAP : 0) +
                       PAGE_FP_BOTTOM_PAD;

        var customRows = Mathf.CeilToInt(customCount / 2f);
        var customHeight = PAGE_CUSTOM_HEADER_H + PAGE_CUSTOM_SEARCH_H + PAGE_CUSTOM_SEARCH_BOTTOM_GAP +
                           (customRows > 0
                               ? customRows * PAGE_CUSTOM_ROW_H + (customRows - 1) * PAGE_CUSTOM_ROW_GAP
                               : 0) + PAGE_CUSTOM_BOTTOM_PAD;

        var scrollView = Layer + ".Scrollview";
        var container = new CuiElementContainer();

        if (facepunch)
        {
            var y = -PAGE_RANDOM_SECTION_H - PAGE_SECTION_GAP;
            DrawFacepunchSection(player, container, scrollView, y, fpHeight, facepunchEvents, PAGE_FP_HEADER_H,
                PAGE_FP_ROW_H, PAGE_FP_ROW_GAP);
        }
        else
        {
            var y = -PAGE_RANDOM_SECTION_H - PAGE_SECTION_GAP - fpHeight - PAGE_SECTION_GAP;
            DrawCustomEventsSection(player, container, scrollView, y, customHeight, adminState);
        }

        CuiHelper.AddUi(player, container);
    }

    private void DrawRandomTeaser(BasePlayer player, CuiElementContainer container, string parent, float y,
        float height)
    {
        var section = parent + ".RandomTeaser";
        container.Add(new CuiElement
        {
            Name = section,
            DestroyUi = section,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - height}", OffsetMax = $"640 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = section,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomScheduleTitle), Font = "robotocondensed-bold.ttf", FontSize = 16,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -24", OffsetMax = "434 0"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = section,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomScheduleDesc), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -66", OffsetMax = "464 -24"
                }
            }
        });

        MeventCuiHelper.CreateButton(container, section,
            command: Command + " random open",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "0 1", anchorMax: "0 1", offsetMin: "454 -47", offsetMax: "620 -19",
            msg: Msg(player, UIBtnManageRandomPool),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));
    }

    private void DrawFacepunchSection(BasePlayer player, CuiElementContainer container, string parent,
        float y, float height, List<Event> events, float headerH, float rowH, float rowGap)
    {
        var section = parent + ".FacepunchSection";
        container.Add(new CuiElement
        {
            Name = section,
            DestroyUi = section,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - height}", OffsetMax = $"640 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = section,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIFacepunchEventTitle), Font = "robotocondensed-bold.ttf", FontSize = 16,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -44", OffsetMax = "470 -20"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = section,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIFacepunchEventDesc), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -76", OffsetMax = "570 -44"
                }
            }
        });

        const float colLeftX = 20f;
        const float colRightX = 330f;
        const float cardWidth = 290f;

        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            var col = i % 2;
            var row = i / 2;
            var x = col == 0 ? colLeftX : colRightX;
            var topY = -headerH - row * (rowH + rowGap);
            var bottomY = topY - rowH;

            MeventCuiHelper.CreateButton(container, section,
                command: Command + $" event edit {ev.eventId}",
                color: HexToCuiColor("#171717"),
                hoverColor: HexToCuiColor("#242424"),
                pressedColor: HexToCuiColor("#0F0F0F"),
                anchorMin: "0 1", anchorMax: "0 1",
                offsetMin: $"{x} {bottomY}", offsetMax: $"{x + cardWidth} {topY}",
                msg: ev.displayName ?? string.Empty,
                fontSize: 14, isBold: true,
                textColor: HexToCuiColor("#FFFFFF"),
                textAnchor: TextAnchor.MiddleLeft,
                textRect: new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "20 0", OffsetMax = "-10 0"
                });
        }
    }

    private void DrawCustomEventsSection(BasePlayer player, CuiElementContainer container, string parent,
        float y, float height, OpenedPlayerData adminState)
    {
        var section = parent + ".CustomSection";
        container.Add(new CuiElement
        {
            Name = section,
            DestroyUi = section,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - height}", OffsetMax = $"640 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = section,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UICustomEventListTitle), Font = "robotocondensed-bold.ttf", FontSize = 16,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -44", OffsetMax = "434 -20"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = section,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UICustomEventListDesc), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -92", OffsetMax = "434 -44"
                }
            }
        });

        MeventCuiHelper.CreateButton(container, section,
            command: Command + " event new",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "0 1", anchorMax: "0 1", offsetMin: "454 -48", offsetMax: "620 -20",
            msg: Msg(player, UIBtnAddNew),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        MeventCuiHelper.CreateButton(container, section,
            command: Command + " events rescan",
            color: HexToCuiColor(BTN_BLUE),
            hoverColor: HexToCuiColor(BTN_BLUE_HOVER),
            pressedColor: HexToCuiColor(BTN_BLUE_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "0 1", anchorMax: "0 1", offsetMin: "454 -82", offsetMax: "620 -54",
            msg: Msg(player, UIBtnRescan),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        DrawCustomEventResults(player, container, adminState);
    }

    private void DrawCustomEventResults(BasePlayer player, CuiElementContainer container, OpenedPlayerData adminState)
    {
        if (_creatorNames == null || _grouped == null) RebuildEventCache();

        const float headerH = 96f;
        const float searchH = 32f;
        const float rowH = 44f;
        const float rowGap = 2f;
        const float colLeftX = 20f;
        const float colRightX = 330f;
        const float cardWidth = 290f;
        const float contentTop = -138f;

        var section = Layer + ".Scrollview.CustomSection";
        var searchBar = section + ".Search";
        var results = section + ".Results";

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#40403D")},
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "0 1",
                OffsetMin = $"20 {-(headerH + searchH)}", OffsetMax = $"310 {-headerH}"
            }
        }, section, searchBar, searchBar);

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#175782")},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "4 0"}
        }, searchBar);

        container.Add(new CuiElement
        {
            Parent = searchBar,
            Components =
            {
                new CuiInputFieldComponent
                {
                    Color = HexToCuiColor("#FFFFFF", string.IsNullOrEmpty(adminState.eventSearch) ? 50 : 100),
                    FontSize = 12, Align = TextAnchor.MiddleLeft,
                    Font = "robotocondensed-bold.ttf",
                    IsPassword = false, Command = $"{Command} search ",
                    Text = adminState.eventSearch ?? Msg(player, UIPlaceholderSearchEvent),
                    NeedsKeyboard = true, CharsLimit = 64
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "14 0", OffsetMax = "-10 0"
                }
            }
        });

        var filterToggle = section + ".FilterToggle";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#000000", 0)},
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "0 1",
                OffsetMin = "350 -128", OffsetMax = "620 -96"
            }
        }, section, filterToggle, filterToggle);

        container.Add(new CuiElement
        {
            Parent = filterToggle,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIShowAllEventBase),
                    Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft,
                    Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "0 0", OffsetMax = "-30 0"
                }
            }
        });

        var checkboxParent = filterToggle + ".Checkbox";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#10100F")},
            RectTransform =
            {
                AnchorMin = "1 0.5", AnchorMax = "1 0.5",
                OffsetMin = "-24 -12", OffsetMax = "0 12"
            }
        }, filterToggle, checkboxParent, checkboxParent);

        if (adminState.showAllCustomEvents)
            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#5D7238")},
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "2 2",
                    OffsetMax = "-2 -2"
                }
            }, checkboxParent);

        container.Add(new CuiButton
        {
            Button = {Color = "0 0 0 0", Command = $"{Command} togglecustomfilter"},
            Text = {Text = ""},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
        }, checkboxParent);

        container.Add(new CuiElement
        {
            Name = results,
            DestroyUi = results,
            Parent = section,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "0 -2000", OffsetMax = $"0 {contentTop}"
                }
            }
        });

        var eventIndex = 0;
        foreach (var creatorKey in _creatorNames)
        {
            if (creatorKey == CreatorFacepunch) continue;
            foreach (var evIdx in _grouped[creatorKey])
            {
                if (!MatchesFilters(evIdx, creatorKey, adminState)) continue;
                var ev = data.events[evIdx];
                var col = eventIndex % 2;
                var row = eventIndex / 2;
                eventIndex++;
            var x = col == 0 ? colLeftX : colRightX;
            var topY = -row * (rowH + rowGap);
            var bottomY = topY - rowH;

            var card = results + $".Custom.{ev.eventId}";
            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#1B1B1B")},
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"{x} {bottomY}", OffsetMax = $"{x + cardWidth} {topY}"
                }
            }, results, card, card);

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor(ev.isLoaded ? "#5D7238" : "#AA4735")},
                RectTransform = {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "4 0"}
            }, card);

            var canRemove = ev.isCustom;
            var bodyRightOffset = canRemove ? "-58" : "-34";

            var cardBody = card + ".Body";
            MeventCuiHelper.CreateButton(container, card,
                name: cardBody,
                command: Command + $" event edit {ev.eventId}",
                color: HexToCuiColor("#1B1B1B"),
                hoverColor: HexToCuiColor("#242424"),
                pressedColor: HexToCuiColor("#0F0F0F"),
                anchorMin: "0 0", anchorMax: "1 1",
                offsetMin: "4 0", offsetMax: $"{bodyRightOffset} 0");

            container.Add(new CuiElement
            {
                Parent = cardBody,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = ev.displayName ?? string.Empty,
                        Font = "robotocondensed-bold.ttf", FontSize = 14,
                        Align = TextAnchor.LowerLeft, Color = HexToCuiColor("#FFFFFF")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.5", AnchorMax = "1 1",
                        OffsetMin = "16 0", OffsetMax = "-4 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = cardBody,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = string.Format(Msg(player, UIEventByAuthor), ev.creator ?? string.Empty),
                        Font = "robotocondensed-regular.ttf", FontSize = 12,
                        Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#ECE3DB")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 0.5",
                        OffsetMin = "16 0", OffsetMax = "-4 0"
                    }
                }
            });

            if (canRemove)
                MeventCuiHelper.CreateButton(container, card,
                    command: Command + $" event remove {ev.eventId}",
                    color: HexToCuiColor(BTN_RED),
                    hoverColor: HexToCuiColor(BTN_RED_HOVER),
                    pressedColor: HexToCuiColor(BTN_RED_PRESSED),
                    anchorMin: "1 0", anchorMax: "1 1", offsetMin: "-24 0", offsetMax: "0 0",
                    iconPath: "assets/icons/clear.png",
                    iconColor: HexToCuiColor("#ECE3DB"),
                    iconRect: new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-9 -9",
                        OffsetMax = "9 9"
                    });

            MeventCuiHelper.CreateButton(container, card,
                command: Command + $" event edit_info {ev.eventId}",
                color: HexToCuiColor(BTN_BLUE),
                hoverColor: HexToCuiColor(BTN_BLUE_HOVER),
                pressedColor: HexToCuiColor(BTN_BLUE_PRESSED),
                anchorMin: "1 0", anchorMax: "1 1",
                offsetMin: canRemove ? "-50 0" : "-26 0",
                offsetMax: canRemove ? "-24 0" : "0 0",
                msg: Msg(player, UIBtnEdit),
                isBold: true, fontSize: 14,
                textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));
            }
        }
    }

    private void DrawColumnsPageContent(BasePlayer player, CuiElementContainer container)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

  container.Add(new CuiElement
  {
    Name = Layer + ".Content",
    DestroyUi = Layer + ".Content",
            Parent = Layer,
      Components = {
          new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
          new CuiRectTransformComponent 
          {
              AnchorMin = "0 0",
              AnchorMax = "1 1",
              OffsetMin = "20 42",
              OffsetMax = "-20 -42"
          }
      }
  });

        #region Random Schedule

        container.Add(new CuiElement
        {
            Parent = Layer + ".Content",
            Components =
            {
                new CuiTextComponent
                {
                    Text = $"<b>{Msg(player, UIRandomScheduleTitle)}</b> - {Msg(player, UIRandomScheduleDesc)}",
                    Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "0 -38", OffsetMax = "810 -10"
                }
            }
        });

        MeventCuiHelper.CreateButton(container, Layer + ".Content",
            command: Command + " random open",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "1 1",
            anchorMax: "1 1",
            offsetMin: "-166 -38",
            offsetMax: "0 -10",
            msg: Msg(player, UIBtnManageRandomPool),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        #endregion

        #region Line

  container.Add(new CuiElement
  {
            Parent = Layer + ".Content",
      Components = {
          new CuiImageComponent { Color = HexToCuiColor("#FFFFFF", 5) },
          new CuiRectTransformComponent 
          {
              AnchorMin = "0 1",
              AnchorMax = "1 1",
              OffsetMin = "0 -48",
              OffsetMax = "0 -46"
          }
      }
  });

        #endregion Line

        #region Section Header

        container.Add(new CuiElement
        {
            Parent = Layer + ".Content",
            Components =
            {
                new CuiTextComponent
                {
                    Text = $"<b>{Msg(player, UICustomEventListTitle)}</b> - {Msg(player, UICustomEventListDesc)}",
                    Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "0 -100", OffsetMax = "620 -60"
                }
            }
        });

        MeventCuiHelper.CreateButton(container, Layer + ".Content",
            command: Command + " events rescan",
            color: HexToCuiColor(BTN_BLUE),
            hoverColor: HexToCuiColor(BTN_BLUE_HOVER),
            pressedColor: HexToCuiColor(BTN_BLUE_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "1 1",
            anchorMax: "1 1",
            offsetMin: "-342 -88",
            offsetMax: "-176 -60",
            msg: Msg(player, UIBtnRescan),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        MeventCuiHelper.CreateButton(container, Layer + ".Content",
            command: Command + " event new",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "1 1",
            anchorMax: "1 1",
            offsetMin: "-166 -88",
            offsetMax: "0 -60",
            msg: Msg(player, UIBtnAddNew),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        #endregion

        #region Search

        var searchSection = Layer + ".ColumnSearch";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#40403D")},
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "0 1",
                OffsetMin = "0 -137", OffsetMax = "382 -105"
            }
        }, Layer + ".Content", searchSection, searchSection);

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#175782")},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "4 0"}
        }, searchSection);

        container.Add(new CuiElement
        {
            Parent = searchSection,
            Components =
            {
                new CuiInputFieldComponent
                {
                    Color = HexToCuiColor("#FFFFFF", string.IsNullOrEmpty(adminState.eventSearch) ? 50 : 100),
                    FontSize = 12, Align = TextAnchor.MiddleLeft,
                    Font = "robotocondensed-bold.ttf",
                    IsPassword = false, Command = $"{Command} search ",
                    Text = adminState.eventSearch ?? Msg(player, UIPlaceholderSearchEvent),
                    NeedsKeyboard = true, CharsLimit = 64
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "14 0", OffsetMax = "-10 0"
                }
            }
        });

        var filterToggle = Layer + ".ColumnsFilterToggle";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#000000", 0)},
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "0 1",
                OffsetMin = "392 -137", OffsetMax = "578 -105"
            }
        }, Layer + ".Content", filterToggle, filterToggle);

        container.Add(new CuiElement
        {
            Parent = filterToggle,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIShowAllEventBase),
                    Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft,
                    Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "0 0", OffsetMax = "-30 0"
                }
            }
        });

        var checkboxParent = filterToggle + ".Checkbox";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#10100F")},
            RectTransform =
            {
                AnchorMin = "1 0.5", AnchorMax = "1 0.5",
                OffsetMin = "-24 -12", OffsetMax = "0 12"
            }
        }, filterToggle, checkboxParent, checkboxParent);

        if (adminState.showAllCustomEvents)
            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#5D7238")},
                RectTransform =
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "2 2", OffsetMax = "-2 -2"
                }
            }, checkboxParent);

        container.Add(new CuiButton
        {
            Button = {Color = "0 0 0 0", Command = $"{Command} togglecustomfilter"},
            Text = {Text = ""},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
        }, checkboxParent);

        #endregion

        #region Creator Cards

        var noFilter = adminState.showAllCustomEvents && string.IsNullOrEmpty(adminState.eventSearch);
        List<string> creatorNames;
        if (noFilter)
        {
            creatorNames = _creatorNames;
        }
        else
        {
            creatorNames = new List<string>();
            foreach (var name in _creatorNames)
                foreach (var idx in _grouped[name])
                    if (MatchesFilters(idx, name, adminState)) { creatorNames.Add(name); break; }
        }

        var creatorCount = creatorNames.Count;
        if (creatorCount == 0) return;

        const float cardWidth = 186f;
        const float cardGap = 10f;
        const float cardHeight = 325f;
        const float eventRowH = 34f;
        const float eventRowGap = 4f;
        const int cardsPerPage = 5;
        var totalPages = Mathf.CeilToInt((float)creatorCount / cardsPerPage);
        adminState.creatorsPage = Mathf.Clamp(adminState.creatorsPage, 0, totalPages - 1);
        var page = adminState.creatorsPage;
        var pageStart = page * cardsPerPage;
        var pageEnd = Mathf.Min(pageStart + cardsPerPage, creatorCount);

        var cardsArea = Layer + ".CreatorCards";
        container.Add(new CuiElement
        {
            Name = cardsArea,
            DestroyUi = cardsArea,
            Parent = Layer + ".Content",
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                    OffsetMin = "-485 -500", OffsetMax = "485 -145"
                }
            }
        });

        var cardsContent = cardsArea + ".Content";
        container.Add(new CuiElement
        {
            Name = cardsContent,
            DestroyUi = cardsContent,
            Parent = cardsArea,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "0 0", OffsetMax = "0 0"
                }
            }
        });

        for (var c = pageStart; c < pageEnd; c++)
        {
            var creatorName = creatorNames[c];
            var bucket = _grouped[creatorName];
            var vi = c - pageStart;
            var x = vi * (cardWidth + cardGap);
            var card = cardsContent + $".Card.{vi}";

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#1F1F1F")},
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"{x} -{cardHeight}", OffsetMax = $"{x + cardWidth} 0"
                }
            }, cardsContent, card, card);

            var authorHeader = card + ".Author";
            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#181818")},
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "0 -38", OffsetMax = "0 0"
                }
            }, card, authorHeader, authorHeader);

            container.Add(new CuiElement
            {
                Parent = authorHeader,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = creatorName, Font = "robotocondensed-bold.ttf", FontSize = 14,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB", 90)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = "20 -38", OffsetMax = "-10 0"
                    }
                }
            });

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#FFFFFF", 10)},
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "0 -38", OffsetMax = "0 -36"
                }
            }, authorHeader);

            var eventsScrollWrap = card + ".EventsWrap";
            container.Add(new CuiElement
            {
                Name = eventsScrollWrap,
                DestroyUi = eventsScrollWrap,
                Parent = card,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = "10 -308", OffsetMax = "-4 -48"
                    }
                }
            });

            var eventCount = noFilter ? bucket.Count : 0;
            if (!noFilter) foreach (var fi in bucket) if (MatchesFilters(fi, creatorName, adminState)) eventCount++;
            var eventsHeight = eventCount * eventRowH + Math.Max(0, eventCount - 1) * eventRowGap;
            eventsHeight = Mathf.Max(eventsHeight, 260);
            var eventsScrollview = card + ".Events";
            container.Add(new CuiElement
            {
                Name = eventsScrollview,
                DestroyUi = eventsScrollview,
                Parent = eventsScrollWrap,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    new CuiScrollViewComponent
                    {
                        MovementType = ScrollRect.MovementType.Clamped,
                        Vertical = true, Horizontal = false, Inertia = true,
                        Elasticity = 0.25f, DecelerationRate = 0.3f, ScrollSensitivity = 24f,
                        ContentTransform = new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "1 1",
                            OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                        },
                        VerticalScrollbar = new CuiScrollbar
                        {
                            AutoHide = true,
                            Size = 4,
                            HandleColor = HexToCuiColor("#676767"),
                            HighlightColor = HexToCuiColor("#676767"),
                            PressedColor = HexToCuiColor("#676767"),
                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                            TrackColor = HexToCuiColor("#181818"),
                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                        }
                    }
                }
            });

            var drawIdx = 0;
            foreach (var evIdx in bucket)
            {
                if (!noFilter && !MatchesFilters(evIdx, creatorName, adminState)) continue;
                var ev = data.events[evIdx];
                var topY = -drawIdx * (eventRowH + eventRowGap);
                var bottomY = topY - eventRowH;
                drawIdx++;

                var evRow = eventsScrollview + $".{ev.eventId}";
                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#181818")},
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"0 {bottomY}", OffsetMax = $"165 {topY}"
                    }
                }, eventsScrollview, evRow, evRow);

                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor(ev.isLoaded ? "#5D7238" : "#AA4735")},
                    RectTransform =
                    {
                        AnchorMin = "0 0", AnchorMax = "0 1",
                        OffsetMin = "0 0", OffsetMax = "4 0"
                    }
                }, evRow);

                var canRemove = ev.isCustom;
                var bodyRightOffset = canRemove ? "-50" : "-26";

                MeventCuiHelper.CreateButton(container, evRow,
                    command: Command + $" event edit {ev.eventId}",
                    color: HexToCuiColor("#181818"),
                    hoverColor: HexToCuiColor("#242424"),
                    pressedColor: HexToCuiColor("#0F0F0F"),
                    anchorMin: "0 0", anchorMax: "1 1",
                    offsetMin: "4 0", offsetMax: $"{bodyRightOffset} 0",
                    msg: ev.displayName ?? string.Empty,
                    fontSize: 12, isBold: true,
                    textColor: HexToCuiColor("#FFFFFF"),
                    textAnchor: TextAnchor.MiddleLeft,
                    textRect: new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "10 0", OffsetMax = "-6 0"
                    });

                if (canRemove)
                    MeventCuiHelper.CreateButton(container, evRow,
                        command: Command + $" event remove {ev.eventId}",
                        color: HexToCuiColor(BTN_RED),
                        hoverColor: HexToCuiColor(BTN_RED_HOVER),
                        pressedColor: HexToCuiColor(BTN_RED_PRESSED),
                        anchorMin: "1 0", anchorMax: "1 1",
                        offsetMin: "-24 0", offsetMax: "0 0",
                        iconPath: "assets/icons/clear.png",
                        iconColor: HexToCuiColor("#ECE3DB"),
                        iconRect: new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = "-7 -7", OffsetMax = "7 7"
                        });

                MeventCuiHelper.CreateButton(container, evRow,
                    command: Command + $" event edit_info {ev.eventId}",
                    color: HexToCuiColor(BTN_BLUE),
                    hoverColor: HexToCuiColor(BTN_BLUE_HOVER),
                    pressedColor: HexToCuiColor(BTN_BLUE_PRESSED),
                    anchorMin: "1 0", anchorMax: "1 1",
                    offsetMin: canRemove ? "-50 0" : "-26 0",
                    offsetMax: canRemove ? "-24 0" : "0 0",
                    msg: Msg(player, UIBtnEdit),
                    isBold: true, fontSize: 14,
                    textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));
            }

            if (creatorName != CreatorFacepunch)
            {
                MeventCuiHelper.CreateButton(container, card,
                    command: Command + $" event new {creatorName}",
                    color: HexToCuiColor(BTN_GREEN),
                    hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
                    pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
                    anchorMin: "1 1",
                    anchorMax: "1 1",
                    offsetMin: "-30 -29",
                    offsetMax: "-10 -9",
                msg: Msg(player, "+"),
                isBold: true, fontSize: 10,
                textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));
            }

        if (totalPages > 1)
        {
            var pagesBar = cardsArea + ".Pages";
            container.Add(new CuiElement
            {
                Name = pagesBar,
                Parent = cardsArea,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#202020")},
                    new CuiRectTransformComponent
                    {
              AnchorMin = "1 1",
              AnchorMax = "1 1",
              OffsetMin = "-166 10",
              OffsetMax = "0 38"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = pagesBar,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = $"{page + 1}/{totalPages}", Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1"
                    }
                }
            });

            MeventCuiHelper.CreateButton(container, pagesBar,
                command: Command + " creatorsscroll left",
                color: HexToCuiColor("#3A3A3A"),
                hoverColor: HexToCuiColor("#4A4A4A"),
                pressedColor: HexToCuiColor("#2A2A2A"),
                anchorMin: "0 0.5", anchorMax: "0 0.5",
                offsetMin: "3 -11", offsetMax: "25 11",
                msg: "‹", isBold: true, fontSize: 14,
                textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#FFFFFF"));

            MeventCuiHelper.CreateButton(container, pagesBar,
                command: Command + " creatorsscroll right",
                color: HexToCuiColor("#3A3A3A"),
                hoverColor: HexToCuiColor("#4A4A4A"),
                pressedColor: HexToCuiColor("#2A2A2A"),
                anchorMin: "1 0.5", anchorMax: "1 0.5",
                offsetMin: "-25 -11", offsetMax: "-3 11",
                msg: "›", isBold: true, fontSize: 14,
                textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#FFFFFF"));
        }

        #endregion
    }
}

    #region Random

    private void DrawRandomPoolModal(BasePlayer player, bool first = true)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var container = new CuiElementContainer();

        if (first)
            DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-505 -281", "505 281", LayerModal,
            LayerBackgroundModal);

        var headerLayer = LayerModal + ".Header";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -42", OffsetMax = "0 0"}
        }, LayerModal, headerLayer, headerLayer);

        container.Add(new CuiElement
        {
            Parent = headerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomPoolEditor), Font = "robotocondensed-bold.ttf", FontSize = 18,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 0", OffsetMax = "-205 0"}
            }
        });

        container.Add(new CuiElement
        {
            Name = headerLayer + ".Time",
            DestroyUi = headerLayer + ".Time",
            Parent = headerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIServerTimeCountdown), Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleRight, Color = HexToCuiColor("#FFFFFF", 90)
                },
                new CuiRectTransformComponent
                    {AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = "-296 0", OffsetMax = "-116 0"},
                new CuiCountdownComponent
                {
                    EndTime = (float) new TimeSpan(24, 0, 0).TotalSeconds,
                    StartTime = (float) DateTime.Now.TimeOfDay.TotalSeconds,
                    Step = 1,
                    TimerFormat = TimerFormat.HoursMinutesSeconds,
                    DestroyIfDone = true,
                    Command = "UI_EventManager_UpdateTimer"
                }
            }
        });

        MeventCuiHelper.AddIconButton(container, headerLayer, LayerModal + ".Header.Close",
            Command + " random close", null, HexToCuiColor(BTN_DARK), hoverColor: HexToCuiColor(BTN_DARK_HOVER),
            pressedColor: HexToCuiColor(BTN_DARK_PRESSED),
            anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-96 -14", offsetMax: "-20 14",
            iconSprite: "assets/icons/exit.png", iconColor: HexToCuiColor("#FFFFFF", 60),
            text: Msg(player, UIBtnClose), textColor: HexToCuiColor("#FFFFFF", 60));

        var footerLayer = LayerModal + ".Footer";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 42"}
        }, LayerModal, footerLayer, footerLayer);

        container.Add(new CuiElement
        {
            Parent = footerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomPoolFooterDesc), Font = "robotocondensed-bold.ttf", FontSize = 16,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "20 -42", OffsetMax = "640 0"}
            }
        });

        container.Add(new CuiElement
        {
            Parent = footerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomPoolFooterSupport), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleRight, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "650 -42", OffsetMax = "990 0"}
            }
        });

        DrawActiveSchedule(player, container);

        if (adminState.editTime != null)
            DrawRandomTime(player, container);
        else
            DrawRandomEmptyState(player, container);

        CuiHelper.AddUi(player, container);
    }

    private void DrawRandomEmptyState(BasePlayer player, CuiElementContainer container)
    {
        var timePanel = LayerModal + ".Time";
        container.Add(new CuiElement
        {
            Name = timePanel,
            DestroyUi = timePanel,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1F1F1F")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-225 -219",
                    OffsetMax = "465 219"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = timePanel,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIEmptyTimeSettings),
                    Font = "robotocondensed-bold.ttf", FontSize = 16,
                    Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 50)
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 20", OffsetMax = "-20 -38"}
            }
        });
    }

    private void DrawActiveSchedule(BasePlayer player, CuiElementContainer container)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;
        var times = data.eventTimes.FindAll(t => t.isRandom);

        var panelName = LayerModal + ".ActiveSchedule";

        container.Add(new CuiElement
        {
            Name = panelName,
            DestroyUi = panelName,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1F1F1F")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-465 -219", OffsetMax = "-235 219"
                }
            }
        });

        var headerPanel = panelName + ".Header";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -38", OffsetMax = "0 0"}
        }, panelName, headerPanel);

        container.Add(new CuiElement
        {
            Parent = headerPanel,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIActiveSchedule), Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 0", OffsetMax = "-20 0"}
            }
        });

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#FFFFFF", 10)},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 2"}
        }, headerPanel);

        var timeHeight = 46f;
        var timeGap = 4f;
        var totalItems = times.Count;
        var contentHeight = totalItems * timeHeight + Math.Max(0, totalItems - 1) * timeGap;
        contentHeight = Mathf.Max(contentHeight, 322f);

        if (times.Count == 0)
            container.Add(new CuiElement
            {
                Parent = panelName,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, UIEmptyActiveSchedule),
                        Font = "robotocondensed-regular.ttf", FontSize = 14,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#ECE3DB", 60)
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 68", OffsetMax = "-20 -38"}
                }
            });

        MeventCuiHelper.CreateButton(container, panelName,
            command: Command + " random time new",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "-95 20", offsetMax: "95 48",
            msg: Msg(player, UIBtnCreateSchedule),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        if (times.Count == 0) return;

        var scrollView = panelName + ".Scroll";
        var scrollWrap = scrollView + "Wrap";
        container.Add(new CuiElement
        {
            Parent = panelName,
            Name = scrollWrap,
            DestroyUi = scrollWrap,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                    {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "20 -370", OffsetMax = "220 -48"}
            }
        });

        container.Add(new CuiElement
        {
            Parent = scrollWrap,
            Name = scrollView,
            DestroyUi = scrollView,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiScrollViewComponent
                {
                    MovementType = ScrollRect.MovementType.Clamped,
                    Vertical = true, Inertia = true, Horizontal = false,
                    Elasticity = 0.25f, DecelerationRate = 0.3f, ScrollSensitivity = 24f,
                    ContentTransform = new CuiRectTransformComponent
                        {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{contentHeight}", OffsetMax = "0 0"},
                    VerticalScrollbar = new CuiScrollbar
                    {
                        AutoHide = true, Size = 4, HandleColor = HexToCuiColor("#676767"),
                        HighlightColor = HexToCuiColor("#676767"), PressedColor = HexToCuiColor("#676767"),
                        HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                        TrackColor = HexToCuiColor("#181818"),
                        TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                    }
                }
            }
        });

        var index = 0;
        foreach (var time in times)
        {
            var offsetY = -(index * (timeHeight + timeGap));
            var isSelected = index == adminState.editIndex;
            var timeLayer = CuiHelper.GetGuid();

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor(isSelected ? "#5D7238" : "#383838")},
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"0 {offsetY - timeHeight}",
                    OffsetMax = $"190 {offsetY}"
                }
            }, scrollView, timeLayer);

            container.Add(new CuiElement
            {
                Parent = timeLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = time.displayTitle ?? time.displayInfo, Font = "robotocondensed-bold.ttf", FontSize = 14,
                        Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#ECE3DB", 90)
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "10 -22", OffsetMax = "180 -4"}
                }
            });

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#FFFFFF", 10)},
                RectTransform = {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "10 -24", OffsetMax = "190 -22"}
            }, timeLayer);

            container.Add(new CuiElement
            {
                Parent = timeLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = BuildDisplayDays(player, time), Font = "robotocondensed-bold.ttf", FontSize = 14,
                        Align = TextAnchor.LowerLeft, Color = HexToCuiColor("#ECE3DB", 90)
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "10 -42", OffsetMax = "180 -24"}
                }
            });

            MeventCuiHelper.CreateButton(container, timeLayer,
                command: Command + $" random time edit {data.eventTimes.IndexOf(time)} {index}",
                color: "0 0 0 0",
                anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0");

            index++;
        }
    }

    private void DrawRandomTime(BasePlayer player, CuiElementContainer container)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var panelName = LayerModal + ".Time";

        #region Time Settings Panel

        container.Add(new CuiElement
        {
            Name = panelName,
            DestroyUi = panelName,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1F1F1F")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-225 -219", OffsetMax = "225 219"
                }
            }
        });

        var tsHeader = panelName + ".TSHeader";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform =
                {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -38", OffsetMax = "0 0"}
        }, panelName, tsHeader);

        container.Add(new CuiElement
        {
            Parent = tsHeader,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UITimeSettings), Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 0", OffsetMax = "-20 0"}
            }
        });

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#FFFFFF", 10)},
            RectTransform =
                {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 2"}
        }, tsHeader);

        DrawRandomTimeInner(player, container, panelName);

        #region Event Pool Panel

        var poolPanel = LayerModal + ".EventPool";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1F1F1F")},
            RectTransform =
            {
                AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                OffsetMin = "235 -167", OffsetMax = "465 219"
            }
        }, LayerModal, poolPanel, poolPanel);

        var poolHeader = poolPanel + ".Header";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -38", OffsetMax = "0 0"}
        }, poolPanel, poolHeader);

        container.Add(new CuiElement
        {
            Parent = poolHeader,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIEventPool), Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB", 90)
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "20 -38", OffsetMax = "210 0"}
            }
        });

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#FFFFFF", 10)},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 2"}
        }, poolHeader);

        var searchBar = poolPanel + ".Search";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#3A3A3A")},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "10 -80", OffsetMax = "-10 -48"}
        }, poolPanel, searchBar);

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#175782")},
            RectTransform = {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "4 0"}
        }, searchBar);

        var hasSearch = !string.IsNullOrEmpty(adminState.eventPoolSearch);
        container.Add(new CuiElement
        {
            Parent = searchBar,
            Components =
            {
                new CuiInputFieldComponent
                {
                    Color = HexToCuiColor("#ECE3DB", hasSearch ? 100 : 50), FontSize = 14,
                    Align = TextAnchor.MiddleLeft,
                    Font = "robotocondensed-bold.ttf",
                    IsPassword = false, Command = $"{Command} random events search",
                    Text = adminState.eventPoolSearch ?? Msg(player, UIPlaceholderSearch),
                    NeedsKeyboard = true
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "14 0", OffsetMax = "-10 0"}
            }
        });

        var allEvents = data.events;
        if (hasSearch)
            allEvents = allEvents.FindAll(e =>
                e.displayName != null &&
                e.displayName.IndexOf(adminState.eventPoolSearch, StringComparison.OrdinalIgnoreCase) >= 0);

        var eventHeight = 38f;
        var eventGap = 4f;
        var poolContentHeight = allEvents.Count * eventHeight + Math.Max(0, allEvents.Count - 1) * eventGap;
        poolContentHeight = Mathf.Max(poolContentHeight, 270f);

        var poolScroll = poolPanel + ".Scroll";
        var poolScrollWrap = poolScroll + "Wrap";
        container.Add(new CuiElement
        {
            Parent = poolPanel,
            Name = poolScrollWrap,
            DestroyUi = poolScrollWrap,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                    {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "10 -366", OffsetMax = "-10 -96"}
            }
        });

        container.Add(new CuiElement
        {
            Parent = poolScrollWrap,
            Name = poolScroll,
            DestroyUi = poolScroll,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiScrollViewComponent
                {
                    MovementType = ScrollRect.MovementType.Clamped,
                    Vertical = true, Inertia = true, Horizontal = false,
                    Elasticity = 0.25f, DecelerationRate = 0.3f, ScrollSensitivity = 24f,
                    ContentTransform = new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{poolContentHeight}", OffsetMax = "0 0"
                    },
                    VerticalScrollbar = new CuiScrollbar
                    {
                        AutoHide = true, Size = 4, HandleColor = HexToCuiColor("#676767"),
                        HighlightColor = HexToCuiColor("#676767"), PressedColor = HexToCuiColor("#676767"),
                        HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                        TrackColor = HexToCuiColor("#181818"),
                        TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                    }
                }
            }
        });

        for (var ei = 0; ei < allEvents.Count; ei++)
        {
            var ev = allEvents[ei];
            DrawEventPoolCard(container, poolScroll, poolPanel + ".Card." + ev.eventId, ev,
                adminState.editTime.eventsId.Contains(ev.eventId),
                -(ei * eventHeight + ei * eventGap), eventHeight);
        }

        MeventCuiHelper.CreateButton(container, LayerModal,
            command: Command + " random time save",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "277 62", offsetMax: "465 94",
            msg: Msg(player, UIBtnSave), isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#ECE3DB"));

        if (!adminState.isCreatingNewTimer)
            MeventCuiHelper.CreateButton(container, LayerModal,
                command: Command + " random time delete",
                color: HexToCuiColor(BTN_RED),
                hoverColor: HexToCuiColor(BTN_RED_HOVER),
                pressedColor: HexToCuiColor(BTN_RED_PRESSED),
                anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "235 62", offsetMax: "267 94",
                iconPath: "assets/icons/clear.png", iconColor: HexToCuiColor("#FFFFFF", 80),
                iconRect: new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-7 -7",
                    OffsetMax = "7 7"
                });

        #endregion
    }

    private void DrawRandomTimeInner(BasePlayer player, CuiElementContainer container, string parentPanel)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var innerPanel = parentPanel + ".Inner";

        container.Add(new CuiElement
        {
            Name = innerPanel,
            DestroyUi = innerPanel,
            Parent = parentPanel,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = "20 -418", OffsetMax = "430 -58"
                }
            }
        });

        var y = 0f;
        var prefix = "random time";

        DrawModalType(player, container, adminState.editTime.type, innerPanel, y, prefix);
        y -= 32f;

        DrawTimeSeparator(container, innerPanel, ref y);

        y -= DrawTimeSection_RequiredOnline(player, container, innerPanel, y, adminState, prefix,
            "robotocondensed-bold.ttf");

        switch (adminState.editTime.type)
        {
            case EventType.Static:
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawTimeSection_Time(player, container, innerPanel, y, adminState, prefix,
                    "robotocondensed-bold.ttf");
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawTimeSection_OffsetTime(player, container, innerPanel, y, adminState, prefix,
                    "robotocondensed-bold.ttf");
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawScheduleDays(player, container, innerPanel, adminState, y, prefix);
                break;

            case EventType.Every:
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawTimeSection_RepeatInterval(player, container, innerPanel, y, adminState, prefix,
                    "robotocondensed-bold.ttf");
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawScheduleDays(player, container, innerPanel, adminState, y, prefix);
                break;

            case EventType.Random:
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawTimeSection_RandomTime(player, container, innerPanel, y, adminState, prefix,
                    "robotocondensed-bold.ttf");
                DrawTimeSeparator(container, innerPanel, ref y);
                y -= DrawScheduleDays(player, container, innerPanel, adminState, y, prefix);
                break;
        }
    }

    #endregion

    private void DrawEventPoolCard(CuiElementContainer container, string parent, string cardName, Event ev, bool inPool,
        float offsetY, float height)
    {
        container.Add(new CuiElement
        {
            Name = cardName,
            DestroyUi = cardName,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#383838")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {offsetY - height}", OffsetMax = $"200 {offsetY}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = cardName,
            Components =
            {
                new CuiTextComponent
                {
                    Text = ev.displayName ?? string.Empty, Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-30 0"}
            }
        });

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-22 -8", OffsetMax = "-6 8"}
        }, cardName);

        if (inPool)
            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#5D7238")},
                RectTransform = {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-20 -6", OffsetMax = "-8 6"}
            }, cardName);

        MeventCuiHelper.CreateButton(container, cardName,
            command: Command + $" random events toggle {ev.eventId}",
            color: "0 0 0 0",
            anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0");
    }

    private void DrawSelectEvent(BasePlayer player)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var container = new CuiElementContainer();

        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-600 -298", "600 298", Layer, LayerBackground);

        #region Header

        var headerLayer = Layer + ".Header";

        container.Add(new CuiPanel
        {
            CursorEnabled = false,
            Image =
            {
                Color = HexToCuiColor("#696969"),
                Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
            },
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -50", OffsetMax = "0 0"}
        }, Layer, headerLayer, headerLayer);

        container.Add(new CuiElement
        {
            Parent = headerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UISelectEvent), Font = "robotocondensed-bold.ttf", FontSize = 18,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "25 0", OffsetMax = "0 0"}
            }
        });

        #region Btn.Close

        MeventCuiHelper.CreateButton(container, headerLayer,
            command: Command + " random events back",
            color: HexToCuiColor(BTN_RED),
            hoverColor: HexToCuiColor(BTN_RED_HOVER),
            pressedColor: HexToCuiColor(BTN_RED_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-48 -48", offsetMax: "0 0",
            iconPath: "assets/icons/exit.png",
            iconRect: new CuiRectTransformComponent
            {
                AnchorMin = "0 0",
                AnchorMax = "1 1",
                OffsetMin = "10 10",
                OffsetMax = "-10 -10"
            });

        #endregion

        #region Server Time

        container.Add(new CuiElement
        {
            Name = headerLayer + "Time",
            DestroyUi = headerLayer + "Time",
            Parent = headerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIServerTimeCountdown),
                    Align = TextAnchor.MiddleRight,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 14,
                    Color = HexToCuiColor("#E2DBD3")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "930 -50",
                    OffsetMax = "1134 0"
                },
                new CuiCountdownComponent
                {
                    EndTime = (float) new TimeSpan(24, 0, 0).TotalSeconds,
                    StartTime = (float) DateTime.Now.TimeOfDay.TotalSeconds,
                    Step = 1,
                    TimerFormat = TimerFormat.HoursMinutesSeconds,
                    DestroyIfDone = true,
                    Command = "UI_EventManager_UpdateTimer"
                }
            }
        });

        #endregion

        #endregion

        #region Content

        int
            columnWidth = 190,
            columnMarginX = 5,
            eventHeight = 38,
            eventMarginY = 4;

        var contentLayer = Layer + ".Content";

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#000000", 0)},
            RectTransform =
            {
                AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-590 -233", OffsetMax = "590 225"
            }
        }, Layer, contentLayer, contentLayer);

        var offsetX = -(ColumnsOnPageSelect * columnWidth + (ColumnsOnPageSelect - 1) * columnMarginX) / 2f;

        var authors = data.creators.Skip(adminState.page * ColumnsOnPageSelect).Take(ColumnsOnPageSelect).ToArray();
        try
        {
            for (var i = 0; i < authors.Length; i++)
            {
                var targetAuthor = authors[i];
                var events = data.events.FindAll(e => e.creator == targetAuthor);

                var offsetLeft = offsetX + columnWidth * i + columnMarginX * i;

                var eventPanel = container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = HexToCuiColor("#696969", 15),
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0", AnchorMax = "0.5 1",
                        OffsetMin = $"{offsetLeft} 0", OffsetMax = $"{offsetLeft + columnWidth} 0"
                    }
                }, contentLayer);

                #region Header

                var headerPanel = container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = HexToCuiColor("#696969", 40),
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    RectTransform =
                        {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -40", OffsetMax = "0 0"}
                }, eventPanel);

                container.Add(new CuiElement
                {
                    Parent = headerPanel,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = targetAuthor, Font = "robotocondensed-bold.ttf", FontSize = 18,
                            Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0"}
                    }
                });

                #endregion

                #region Scroll View

                var scrollView = eventPanel + ".ScrollView";
                var scrollViewWrap = scrollView + "Wrap";

                var eventsHeight = events.Count * eventHeight + (events.Count - 1) * eventMarginY;

                eventsHeight = Mathf.Max(eventsHeight, 408);

                container.Add(new CuiElement
                {
                    Parent = eventPanel,
                    Name = scrollViewWrap,
                    DestroyUi = scrollViewWrap,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 5", OffsetMax = "0 -45"}
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = scrollViewWrap,
                    Name = scrollView,
                    DestroyUi = scrollView,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Clamped,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1", AnchorMax = "1 1",
                                OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                            },
                            VerticalScrollbar = new CuiScrollbar
                            {
                                AutoHide = true,
                                Size = 3,
                                HandleColor = HexToCuiColor("#D74933"),
                                HighlightColor = HexToCuiColor("#D74933"),
                                PressedColor = HexToCuiColor("#D74933"),
                                HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                TrackColor = HexToCuiColor("#38393F", 40),
                                TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                            }
                        }
                    }
                });

                #endregion

                #region Events

                for (var x = 0; x < events.Count; x++)
                {
                    var targetEvent = events[x];

                    var offsetUp = x * eventHeight + x * eventMarginY;

                    var eventLayer = Layer + $".Author.{targetAuthor}.Event.{targetEvent.displayName}";

                    container.Add(new CuiPanel
                    {
                        Image =
                        {
                            Color = HexToCuiColor("#696969", 60),
                            Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                        },
                        RectTransform =
                        {
                            AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                            OffsetMin = $"-90 -{offsetUp + eventHeight}", OffsetMax = $"88 -{offsetUp}"
                        }
                    }, scrollView, eventLayer, eventLayer);

                    container.Add(new CuiPanel
                    {
                        Image = {Color = targetEvent.isLoaded ? HexToCuiColor("#2BDF31") : HexToCuiColor("#DF2B2E")},
                        RectTransform =
                            {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "2 0"}
                    }, eventLayer);

                    container.Add(new CuiElement
                    {
                        Parent = eventLayer,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = targetEvent.displayName ?? string.Empty,
                                Font = "robotocondensed-bold.ttf", FontSize = 14,
                                Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF"),
                                VerticalOverflow = VerticalWrapMode.Overflow
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-20 0"
                            }
                        }
                    });

                    MeventCuiHelper.CreateButton(container, eventLayer,
                        command:
                        Command + $" random events select {targetEvent.eventId}",
                        color: "0 0 0 0",
                        anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0");
                }

                #endregion
            }
        }
        finally
        {
            Array.Clear(authors, 0, authors.Length);
        }

        #endregion

        MeventCuiHelper.CreateButton(container, Layer, command: Command + " random events next",
            color: HexToCuiColor(BTN_RED),
            hoverColor: HexToCuiColor(BTN_RED_HOVER),
            pressedColor: HexToCuiColor(BTN_RED_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            anchorMin: "1 0", anchorMax: "1 0", offsetMin: "-44 7", offsetMax: "-9 42",
            msg: Msg(player, UIBtnNext),
            isBold: true,
            fontSize: 26,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#FFFFFF"));

        MeventCuiHelper.CreateButton(container, Layer, command: Command + " random events prev",
            color: HexToCuiColor("#696969", 30),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            anchorMin: "1 0", anchorMax: "1 0", offsetMin: "-87 7", offsetMax: "-52 42",
            msg: Msg(player, UIBtnPrev),
            isBold: true,
            fontSize: 26,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#FFFFFF"));

        CuiHelper.AddUi(player, container);
    }

    #endregion

    #region Modal

    private void DrawModal(BasePlayer player, string type, bool first = true)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var container = new CuiElementContainer();

        switch (type)
        {
            case "edit":
            {
                if (first)
                    DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-340 -240", "340 240", LayerModal,
                    LayerBackgroundModal);

                #region Header

                container.Add(new CuiElement
                {
                    Name = LayerModal + "Header.Panel",
                    DestroyUi = LayerModal + "Header.Panel",
                    Parent = LayerModal,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            Color = HexToCuiColor("#1B1B1B")
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = "0 -42",
                            OffsetMax = "0 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = LayerModal + "Header.Panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = Msg(player, UIEditEventTitle, adminState.editEvent.displayName.ToUpper()),
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "20 0",
                            OffsetMax = "-205 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = LayerModal + "Header.Time",
                    DestroyUi = LayerModal + "Header.Time",
                    Parent = LayerModal + "Header.Panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = Msg(player, UIServerTimeCountdown), Font = "robotocondensed-bold.ttf", FontSize = 14,
                            Align = TextAnchor.MiddleRight, Color = HexToCuiColor("#FFFFFF", 90)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0",
                            AnchorMax = "1 1",
                            OffsetMin = "-296 0",
                            OffsetMax = "-116 0"
                        },
                        new CuiCountdownComponent
                        {
                            EndTime = (float) new TimeSpan(24, 0, 0).TotalSeconds,
                            StartTime = (float) DateTime.Now.TimeOfDay.TotalSeconds,
                            Step = 1,
                            TimerFormat = TimerFormat.HoursMinutesSeconds,
                            DestroyIfDone = true,
                            Command = "UI_EventManager_UpdateTimer"
                        }
                    }
                });

                MeventCuiHelper.AddIconButton(container, LayerModal + "Header.Panel", LayerModal + "Header.Close",
                    Command + " event close", null, HexToCuiColor(BTN_DARK), hoverColor: HexToCuiColor(BTN_DARK_HOVER),
                    pressedColor: HexToCuiColor(BTN_DARK_PRESSED),
                    anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-96 -14", offsetMax: "-20 14",
                    iconSprite: "assets/icons/exit.png", iconColor: HexToCuiColor("#FFFFFF", 60),
                    text: Msg(player, UIBtnClose), textColor: HexToCuiColor("#FFFFFF", 60));

                #endregion Header

                DrawModalTimes(player, container);
                DrawModalContent(player, container);
                break;
            }

            case "new_creator":
            {
                if (first)
                    DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-170 -24", "170 176", LayerModal,
                    LayerBackgroundModal);

                #region Header

                container.Add(new CuiElement
                {
                    Name = LayerModal + "Header.Panel",
                    DestroyUi = LayerModal + "Header.Panel",
                    Parent = LayerModal,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            Color = HexToCuiColor("#696969"),
                            Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "0 -40",
                            OffsetMax = "340 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = LayerModal + "Header.Panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = Msg(player, UINewCreator), Font = "robotocondensed-bold.ttf", FontSize = 18,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D9D9D9")
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "5 0"
                        }
                    }
                });

                MeventCuiHelper.CreateButton(container, LayerModal + "Header.Panel",
                    command: Command + " creator close",
                    close: null,
                    color: HexToCuiColor(BTN_RED),
                    hoverColor: HexToCuiColor(BTN_RED_HOVER),
                    pressedColor: HexToCuiColor(BTN_RED_PRESSED),
                    material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    sprite: "assets/content/ui/UI.Background.TileTex.psd",
                    anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-40 -40", offsetMax: "0 0",
                    iconPath: "assets/icons/clear.png",
                    iconColor: HexToCuiColor("#FFFFFF", 60),
                    iconRect: new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "9 -31",
                        OffsetMax = "31 -9"
                    });

                #endregion Header

                DrawModalCreator(player, container, Msg(player, UIPlaceholderCreatorName));
                break;
            }

            case "edit_creator":
            {
                if (first)
                    DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-170 -24", "170 176", LayerModal,
                    LayerBackgroundModal);

                #region Header

                container.Add(new CuiElement
                {
                    Name = LayerModal + "Header.Panel",
                    DestroyUi = LayerModal + "Header.Panel",
                    Parent = LayerModal,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            Color = HexToCuiColor("#696969"),
                            Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "0 -40",
                            OffsetMax = "340 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = LayerModal + "Header.Panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = Msg(player, UIEditCreator), Font = "robotocondensed-bold.ttf", FontSize = 18,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D9D9D9")
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "5 0"
                        }
                    }
                });

                MeventCuiHelper.CreateButton(container, LayerModal + "Header.Panel",
                    command: Command + " creator close",
                    close: null,
                    color: HexToCuiColor(BTN_RED),
                    hoverColor: HexToCuiColor(BTN_RED_HOVER),
                    pressedColor: HexToCuiColor(BTN_RED_PRESSED),
                    material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    sprite: "assets/content/ui/UI.Background.TileTex.psd",
                    anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-40 -40", offsetMax: "0 0",
                    iconPath: "assets/icons/clear.png",
                    iconColor: HexToCuiColor("#FFFFFF", 60),
                    iconRect: new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "9 -31",
                        OffsetMax = "31 -9"
                    });

                #endregion Header

                DrawModalCreator(player, container, adminState.inputCreator, adminState.editCreatorIndex);
                break;
            }
        }

        CuiHelper.AddUi(player, container);
    }

    private void DrawModalTimes(BasePlayer player, CuiElementContainer container)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        #region Sidebar

        var leftPanel = LayerModal + ".LeftPanel";
        container.Add(new CuiElement
        {
            Name = leftPanel,
            DestroyUi = leftPanel,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1F1F1F")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "0 -480",
                    OffsetMax = "230 -42"
                }
            }
        });

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#393939")},
            RectTransform =
            {
                AnchorMin = "1 0", AnchorMax = "1 1",
                OffsetMin = "-2 0", OffsetMax = "0 0"
            }
        }, leftPanel);

        var isFacepunch = adminState.editEvent?.creator == CreatorFacepunch;

        if (isFacepunch)
        {
            var defaultRow = leftPanel + ".DefaultSchedule";
            container.Add(new CuiElement
            {
                Name = defaultRow,
                DestroyUi = defaultRow,
                Parent = leftPanel,
                Components =
                {
                    new CuiImageComponent {Color = "0 0 0 0"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = "20 -80", OffsetMax = "210 -52"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = defaultRow,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, UIDefaultSchedule), Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "0 0", OffsetMax = "-76 0"
                    }
                }
            });

            var defaultOn = IsAutoEventEnabled(adminState.editEvent);
            var defaultPill = defaultRow + ".Pill";
            container.Add(new CuiElement
            {
                Name = defaultPill,
                DestroyUi = defaultPill,
                Parent = defaultRow,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor(defaultOn ? "#5D7238" : "#10100F")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5", AnchorMax = "1 0.5",
                        OffsetMin = "-72 -14", OffsetMax = "0 14"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = defaultPill,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, defaultOn ? UIOn : UIOff), Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#ECE3DB")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "0 0", OffsetMax = "0 0"
                    }
                }
            });

            container.Add(new CuiButton
            {
                RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                Button = {Color = "0 0 0 0", Command = $"{Command} event default_schedule toggle"}
            }, defaultPill);
        }

        var timersLabelTop = isFacepunch ? "-116" : "-84";
        var timersLabelBottom = isFacepunch ? "-100" : "-68";
        var scrollTop = isFacepunch ? "-125" : "-93";

        container.Add(new CuiElement
        {
            Parent = leftPanel,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIActiveTimers), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.LowerLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = $"20 {timersLabelTop}",
                    OffsetMax = $"210 {timersLabelBottom}"
                }
            }
        });

        #region Scroll View

        var times = data.eventTimes.FindAll(t => !t.isRandom && t.eventsId.Contains(adminState.editEvent.eventId));

        var timeHeight = 46f;
        var timeMargin = 4f;

        var scrollView = leftPanel + ".Time.Panel";

        var eventsHeight = times.Count * timeHeight + Mathf.Max(0, times.Count - 1) * timeMargin;
        eventsHeight = Mathf.Max(eventsHeight, isFacepunch ? 223 : 255);

        var scrollWrap = scrollView + "Wrap";
        container.Add(new CuiElement
        {
            Parent = leftPanel,
            Name = scrollWrap,
            DestroyUi = scrollWrap,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "20 -348",
                    OffsetMax = $"218 {scrollTop}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = scrollWrap,
            Name = scrollView,
            DestroyUi = scrollView,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiScrollViewComponent
                {
                    MovementType = ScrollRect.MovementType.Clamped,
                    Vertical = true,
                    Inertia = true,
                    Horizontal = false,
                    Elasticity = 0.25f,
                    DecelerationRate = 0.3f,
                    ScrollSensitivity = 24f,
                    ContentTransform = new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                    },
                    VerticalScrollbar = new CuiScrollbar
                    {
                        Size = 4,
                        HandleColor = HexToCuiColor("#676767"),
                        HighlightColor = HexToCuiColor("#676767"),
                        PressedColor = HexToCuiColor("#676767"),
                        HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                        TrackColor = HexToCuiColor("#181818"),
                        TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                    }
                }
            }
        });

        if (times.Count == 0)
        {
            container.Add(new CuiElement
            {
                Parent = scrollView,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, UINoTimersYet), Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 50)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });
        }
        else
        {
            var index = 0;
            foreach (var time in times)
            {
                var offset = -index * timeHeight - index * timeMargin;
                var isSelected = index == adminState.editIndex;
                var timeLayer = CuiHelper.GetGuid();

                container.Add(new CuiElement
                {
                    Name = timeLayer,
                    DestroyUi = timeLayer,
                    Parent = scrollView,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor(isSelected ? "#5D7238" : "#383838")},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"0 {offset - timeHeight}",
                            OffsetMax = $"190 {offset}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = timeLayer,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = time.displayTitle, Font = "robotocondensed-bold.ttf", FontSize = 14,
                            Align = TextAnchor.MiddleLeft,
                            Color = HexToCuiColor(isSelected ? "#BEC7AF" : "#FFFFFF", isSelected ? 100 : 90)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0.5",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0",
                            OffsetMax = "-10 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = timeLayer,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = BuildDisplayDays(player, time), Font = "robotocondensed-regular.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleLeft,
                            Color = HexToCuiColor(isSelected ? "#BEC7AF" : "#FFFFFF", isSelected ? 80 : 60)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 0.5",
                            OffsetMin = "10 0",
                            OffsetMax = "-10 0"
                        }
                    }
                });

                container.Add(new CuiButton
                {
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                    Button =
                    {
                        Color = "0 0 0 0",
                        Command = $"{Command} event time edit {data.eventTimes.IndexOf(time)} {index}"
                    }
                }, timeLayer);

                index++;
            }
        }

        #endregion Scroll View

        #region Use Schedule toggle

        var scheduleRow = leftPanel + ".Schedule";
        container.Add(new CuiElement
        {
            Name = scheduleRow,
            DestroyUi = scheduleRow,
            Parent = leftPanel,
            Components =
            {
                new CuiImageComponent {Color = "0 0 0 0"},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "20 -48",
                    OffsetMax = "210 -20"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = scheduleRow,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIUseSchedule), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "0 0",
                    OffsetMax = "-76 0"
                }
            }
        });

        var scheduleOn = adminState.editEvent.scheduleEnabled;
        var togglePill = scheduleRow + ".Pill";
        container.Add(new CuiElement
        {
            Name = togglePill,
            DestroyUi = togglePill,
            Parent = scheduleRow,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor(scheduleOn ? "#5D7238" : "#10100F")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "1 0.5",
                    AnchorMax = "1 0.5",
                    OffsetMin = "-72 -14",
                    OffsetMax = "0 14"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = togglePill,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, scheduleOn ? UIOn : UIOff), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }
        });

        container.Add(new CuiButton
        {
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
            Button = {Color = "0 0 0 0", Command = $"{Command} event schedule toggle"}
        }, togglePill);

        #endregion

        #region Sidebar bottom actions

        MeventCuiHelper.CreateButton(container, leftPanel, Msg(player, UIBtnCreateTimer),
            $"{Command} event time new",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "-95 52", offsetMax: "95 80",
            isBold: true,
            fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#ECE3DB"));

        var isActive = IsEventCurrentlyActive(adminState.editEvent);
        MeventCuiHelper.CreateButton(container, leftPanel,
            Msg(player, isActive ? UIBtnStopEvent : UIBtnStartEvent),
            $"{Command} event time {(isActive ? "stop" : "start")}",
            color: HexToCuiColor(isActive ? BTN_RED : BTN_BLUE),
            hoverColor: HexToCuiColor(isActive ? BTN_RED_HOVER : BTN_BLUE_HOVER),
            pressedColor: HexToCuiColor(isActive ? BTN_RED_PRESSED : BTN_BLUE_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "-95 20", offsetMax: "95 48",
            isBold: true,
            fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#ECE3DB"));

        #endregion

        #endregion Sidebar
    }

    private void DrawModalContent(BasePlayer player, CuiElementContainer container)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var contentPanel = LayerModal + ".Content";
        container.Add(new CuiElement
        {
            Name = contentPanel,
            DestroyUi = contentPanel,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = "0 0 0 0"},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "230 -480",
                    OffsetMax = "680 -42"
                }
            }
        });

        if (adminState.editTime == null)
        {
            container.Add(new CuiElement
            {
                Parent = contentPanel,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, UIEditEventEmptyHint), Font = "robotocondensed-bold.ttf", FontSize = 16,
                        Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#FFFFFF", 50)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-150 -10",
                        OffsetMax = "150 110"
                    }
                }
            });

            MeventCuiHelper.CreateButton(container, contentPanel, Msg(player, UIBtnCreateTimer),
                $"{Command} event time new",
                color: HexToCuiColor(BTN_GREEN),
                hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
                pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
                anchorMin: "0.5 0.5", anchorMax: "0.5 0.5", offsetMin: "-95 -48", offsetMax: "95 -20",
                isBold: true,
                fontSize: 12,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#ECE3DB"));
        }
    }

    private void DrawModalTime(BasePlayer player)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var container = new CuiElementContainer();
        var panelName = LayerModal + ".Time";

        #region Content

        container.Add(new CuiElement
        {
            Name = panelName,
            DestroyUi = panelName,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "250 -412",
                    OffsetMax = "660 -62"
                }
            }
        });

        #endregion

        var y = 0f;

        DrawModalType(player, container, adminState.editTime.type, panelName, y);
        y -= 32f;

        DrawTimeSeparator(container, panelName, ref y);

        y -= DrawTimeSection_RequiredOnline(player, container, panelName, y, adminState);

        DrawTimeSeparator(container, panelName, ref y);

        switch (adminState.editTime.type)
        {
            case EventType.Static:
                y -= DrawTimeSection_Time(player, container, panelName, y, adminState);
                DrawTimeSeparator(container, panelName, ref y);
                y -= DrawTimeSection_OffsetTime(player, container, panelName, y, adminState);
                DrawTimeSeparator(container, panelName, ref y);
                y -= DrawScheduleDays(player, container, panelName, adminState, y);

                y -= 10f;
                break;

            case EventType.Every:
                y -= DrawTimeSection_RepeatInterval(player, container, panelName, y, adminState);
                DrawTimeSeparator(container, panelName, ref y);
                y -= DrawScheduleDays(player, container, panelName, adminState, y);
                break;

            case EventType.Random:
                y -= DrawTimeSection_RandomTime(player, container, panelName, y, adminState);
                DrawTimeSeparator(container, panelName, ref y);
                y -= DrawScheduleDays(player, container, panelName, adminState, y);
                break;
        }

        #region Save & Delete

        MeventCuiHelper.CreateButton(container, LayerModal, Msg(player, UIBtnSave), Command + " event time save",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "172 20", offsetMax: "320 48",
            isBold: true,
            fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#BEC7AF"));

        if (!adminState.isCreatingNewTimer)
            MeventCuiHelper.CreateButton(container, LayerModal, "", Command + " event time delete",
                color: HexToCuiColor(BTN_RED),
                hoverColor: HexToCuiColor(BTN_RED_HOVER),
                pressedColor: HexToCuiColor(BTN_RED_PRESSED),
                anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "134 20", offsetMax: "162 48",
                iconPath: "assets/icons/clear.png",
                iconColor: HexToCuiColor("#FFFFFF", 60),
                iconRect: new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-7 -7",
                    OffsetMax = "7 7"
                });

        #endregion

        CuiHelper.AddUi(player, container);
    }

    private float GetScheduleDaysY(EventType type)
    {
        float y = -(TIME_SECTION_TYPE_HEIGHT + TIME_SEPARATOR_TOTAL + TIME_SECTION_DUAL_INPUT_HEIGHT +
                    TIME_SEPARATOR_TOTAL);
        switch (type)
        {
            case EventType.Static:
                y -= TIME_SECTION_SINGLE_INPUT_HEIGHT + TIME_SEPARATOR_TOTAL + TIME_SECTION_SINGLE_INPUT_HEIGHT +
                     TIME_SEPARATOR_TOTAL;
                break;
            case EventType.Every:
            case EventType.Random:
                y -= TIME_SECTION_DUAL_INPUT_HEIGHT + TIME_SEPARATOR_TOTAL;
                break;
        }

        return y;
    }

    private void DrawTimeSeparator(CuiElementContainer container, string parent, ref float y)
    {
        y -= TIME_SEPARATOR_GAP;
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#FFFFFF", 5)},
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "0 1",
                OffsetMin = $"0 {y - TIME_SEPARATOR_HEIGHT}", OffsetMax = $"410 {y}"
            }
        }, parent);
        y -= TIME_SEPARATOR_HEIGHT + TIME_SEPARATOR_GAP;
    }

    private float DrawTimeSection_RequiredOnline(BasePlayer player, CuiElementContainer container, string parent,
        float y, OpenedPlayerData adminState, string commandPrefix = "event time", string font = null)
    {
        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRequiredOnline), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - 14}", OffsetMax = $"252 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescPlayerRange),
                    Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"252 {y - 14}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMinPlayers), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"262 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"334 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".MinPlayersOuter", parent + ".MinPlayers",
            $"262 {y - 32}", $"334 {y}", $"{Command} {commandPrefix} minplayers",
            adminState.editTime.minPlayers.ToString(), font: font);

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMaxPlayers), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"338 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"410 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".MaxPlayersOuter", parent + ".MaxPlayers",
            $"338 {y - 32}", $"410 {y}", $"{Command} {commandPrefix} maxplayers",
            adminState.editTime.maxPlayers.ToString(), font: font);

        return TIME_SECTION_DUAL_INPUT_HEIGHT;
    }

    private float DrawTimeSection_Time(BasePlayer player, CuiElementContainer container, string parent, float y,
        OpenedPlayerData adminState, string commandPrefix = "event time", string font = null)
    {
        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UITime), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - 14}", OffsetMax = $"252 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIStaticTimeFormat), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - TIME_SECTION_SINGLE_INPUT_HEIGHT}", OffsetMax = $"252 {y - 14}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIHour), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"262 {y - TIME_SECTION_SINGLE_INPUT_HEIGHT}", OffsetMax = $"334 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".HourOuter", parent + ".HourInner",
            $"262 {y - 32}", $"334 {y}", $"{Command} {commandPrefix} hour", adminState.editTime.hour.ToString(),
            font: font);

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMinute), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"338 {y - TIME_SECTION_SINGLE_INPUT_HEIGHT}", OffsetMax = $"410 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".MinuteOuter", parent + ".MinuteInner",
            $"338 {y - 32}", $"410 {y}", $"{Command} {commandPrefix} minute", adminState.editTime.minute.ToString(),
            font: font);

        return TIME_SECTION_SINGLE_INPUT_HEIGHT;
    }

    private float DrawTimeSection_OffsetTime(BasePlayer player, CuiElementContainer container, string parent, float y,
        OpenedPlayerData adminState, string commandPrefix = "event time", string font = null)
    {
        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIOffsetTime), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - 14}", OffsetMax = $"252 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescOffsetTime), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - TIME_SECTION_SINGLE_INPUT_HEIGHT}", OffsetMax = $"252 {y - 14}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMin), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"262 {y - TIME_SECTION_SINGLE_INPUT_HEIGHT}", OffsetMax = $"334 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".OffsetMinOuter", parent + ".OffsetMinInner",
            $"262 {y - 32}", $"334 {y}", $"{Command} {commandPrefix} offsetmin",
            adminState.editTime.offsetMin > 0 ? adminState.editTime.offsetMin.ToString() : "0", font: font);

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMax), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"338 {y - TIME_SECTION_SINGLE_INPUT_HEIGHT}", OffsetMax = $"410 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".OffsetMaxOuter", parent + ".OffsetMaxInner",
            $"338 {y - 32}", $"410 {y}", $"{Command} {commandPrefix} offsetmax",
            adminState.editTime.offsetMax > 0 ? adminState.editTime.offsetMax.ToString() : "0", font: font);

        return TIME_SECTION_SINGLE_INPUT_HEIGHT;
    }

    private float DrawTimeSection_RepeatInterval(BasePlayer player, CuiElementContainer container, string parent,
        float y, OpenedPlayerData adminState, string commandPrefix = "event time", string font = null)
    {
        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRepeatInterval), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - 14}", OffsetMax = $"252 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescRepeatInterval), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"252 {y - 14}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMinutes), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"262 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"410 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".IntervalOuter", parent + ".IntervalInner",
            $"262 {y - 32}", $"410 {y}", $"{Command} {commandPrefix} interval",
            adminState.editTime.interval > 0 ? adminState.editTime.interval.ToString() : "0", font: font);

        return TIME_SECTION_DUAL_INPUT_HEIGHT;
    }

    private float DrawTimeSection_RandomTime(BasePlayer player, CuiElementContainer container, string parent, float y,
        OpenedPlayerData adminState, string commandPrefix = "event time", string font = null)
    {
        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRandomTime), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - 14}", OffsetMax = $"252 {y}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescRandomTime), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"0 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"252 {y - 14}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMin), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"262 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"334 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".RndMinOuter", parent + ".RndMinInner",
            $"262 {y - 32}", $"334 {y}", $"{Command} {commandPrefix} offsetmin",
            adminState.editTime.offsetMin > 0 ? adminState.editTime.offsetMin.ToString() : "0", font: font);

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIMax), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.LowerCenter, Color = HexToCuiColor("#E0E0E0", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "0 1",
                    OffsetMin = $"338 {y - TIME_SECTION_DUAL_INPUT_HEIGHT}", OffsetMax = $"410 {y - 32}"
                }
            }
        });

        MeventCuiHelper.AddBorderedInput(container, parent, parent + ".RndMaxOuter", parent + ".RndMaxInner",
            $"338 {y - 32}", $"410 {y}", $"{Command} {commandPrefix} offsetmax",
            adminState.editTime.offsetMax > 0 ? adminState.editTime.offsetMax.ToString() : "0", font: font);

        return TIME_SECTION_DUAL_INPUT_HEIGHT;
    }

    private void DrawModalType(BasePlayer player, CuiElementContainer container, EventType curType, string parent,
        float startY = 0f, string commandPrefix = "event time")
    {
        var layerName = parent + ".Type";

        #region Header

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIRepeatType), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = $"0 {startY - 14}",
                    OffsetMax = $"252 {startY}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = parent,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescRepeatType), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = $"0 {startY - 32}",
                    OffsetMax = $"252 {startY - 14}"
                }
            }
        });

        #endregion

        #region Panel

        container.Add(new CuiElement
        {
            Name = layerName,
            DestroyUi = layerName,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#202020")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = $"262 {startY - 32}",
                    OffsetMax = $"410 {startY}"
                }
            }
        });

        var innerType = layerName + ".Inner";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform =
            {
                AnchorMin = "0 0", AnchorMax = "1 1",
                OffsetMin = "2 2", OffsetMax = "-2 -2"
            }
        }, layerName, innerType);

        #endregion

        #region Type

        container.Add(new CuiElement
        {
            Parent = innerType,
            Components =
            {
                new CuiTextComponent
                {
                    Text = GetTypeName(curType), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#E0E0E0")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "28 0", OffsetMax = "-28 0"
                }
            }
        });

        #endregion

        #region Btn.Prev

        container.Add(new CuiButton
        {
            RectTransform =
            {
                AnchorMin = "0 0", AnchorMax = "0 1",
                OffsetMin = "0 0", OffsetMax = "28 0"
            },
            Text =
            {
                Text = Msg(player, UIBtnPrev),
                Align = TextAnchor.MiddleCenter,
                Font = "robotocondensed-bold.ttf",
                FontSize = 20,
                Color = HexToCuiColor("#FFFFFF", 60),
                VerticalOverflow = VerticalWrapMode.Overflow
            },
            Button =
            {
                Command = Command + $" {commandPrefix} prev",
                Color = HexToCuiColor("#3A3A3A")
            }
        }, innerType);

        #endregion

        #region Btn.Next

        container.Add(new CuiButton
        {
            RectTransform =
            {
                AnchorMin = "1 0", AnchorMax = "1 1",
                OffsetMin = "-28 0", OffsetMax = "0 0"
            },
            Text =
            {
                Text = Msg(player, UIBtnNext),
                Align = TextAnchor.MiddleCenter,
                Font = "robotocondensed-bold.ttf",
                FontSize = 20,
                Color = HexToCuiColor("#FFFFFF", 60),
                VerticalOverflow = VerticalWrapMode.Overflow
            },
            Button =
            {
                Command = Command + $" {commandPrefix} next",
                Color = HexToCuiColor("#3A3A3A")
            }
        }, innerType);

        #endregion
    }

    private float DrawScheduleDays(BasePlayer player, CuiElementContainer container, string parent,
        OpenedPlayerData adminState, float startY, string commandPrefix = "event time")
    {
        var layerName = parent + ".ScheduleDays";

        container.Add(new CuiElement
        {
            Name = layerName,
            DestroyUi = layerName,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = $"0 {startY - SCHEDULE_DAYS_HEIGHT}",
                    OffsetMax = $"410 {startY}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = layerName,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIScheduleDays), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "0 -16",
                    OffsetMax = "252 0"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = layerName,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescScheduleDays), Font = "robotocondensed-regular.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "0 -34",
                    OffsetMax = "252 -18"
                }
            }
        });

        for (var i = 0; i < SCHEDULE_DAYS_TOTAL_ITEMS; i++)
        {
            var col = i % SCHEDULE_DAYS_COLUMNS;
            var row = i / SCHEDULE_DAYS_COLUMNS;
            var x = col * SCHEDULE_DAYS_COLUMN_WIDTH;
            var cellY = SCHEDULE_DAYS_FIRST_ROW_Y - row * SCHEDULE_DAYS_ROW_HEIGHT;
            var layer = CuiHelper.GetGuid();

            container.Add(new CuiElement
            {
                Name = layer,
                DestroyUi = layer,
                Parent = layerName,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"{x} {cellY - SCHEDULE_DAYS_CELL_HEIGHT}",
                        OffsetMax = $"{x + SCHEDULE_DAYS_CELL_WIDTH} {cellY}"
                    }
                }
            });

            var isDayItem = i < 7;
            var isActive = isDayItem
                ? adminState.editTime.activeDays.Contains((DayOfWeek) i)
                : adminState.editTime.activeDays.Count == 7;

            MeventCuiHelper.CreateButton(container, layer,
                command: isDayItem
                    ? Command + $" {commandPrefix} day {(DayOfWeek) i}"
                    : Command + $" {commandPrefix} dayall",
                color: HexToCuiColor(isActive ? BTN_GREEN : "#181818"),
                hoverColor: HexToCuiColor(isActive ? BTN_GREEN_HOVER : "#242424"),
                pressedColor: HexToCuiColor(isActive ? BTN_GREEN_PRESSED : "#0F0F0F"),
                anchorMin: "0 1", anchorMax: "0 1",
                offsetMin: $"0 -{SCHEDULE_DAYS_CHECKBOX_SIZE}", offsetMax: $"{SCHEDULE_DAYS_CHECKBOX_SIZE} 0");

            container.Add(new CuiElement
            {
                Parent = layer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = isDayItem ? GetDayName(player, (DayOfWeek) i) : Msg(player, UISelectAll),
                        Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#ECE3DB")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"{SCHEDULE_DAYS_LABEL_LEFT} -{SCHEDULE_DAYS_CELL_HEIGHT}",
                        OffsetMax = $"{SCHEDULE_DAYS_CELL_WIDTH} 0"
                    }
                }
            });
        }

        return SCHEDULE_DAYS_HEIGHT;
    }

    private void DrawModalCreator(BasePlayer player, CuiElementContainer container, string creatorName, int index = -1)
    {
        var layerName = LayerModal + ".NewCreator";

        container.Add(new CuiElement
        {
            Name = layerName,
            DestroyUi = layerName,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#404040", 50)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "0 -200",
                    OffsetMax = "340 -40"
                }
            }
        });


        container.Add(new CuiElement
        {
            Parent = layerName,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UICreatorName), Font = "robotocondensed-regular.ttf", FontSize = 10,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "40 -31",
                    OffsetMax = "177 -14"
                }
            }
        });


        container.Add(new CuiElement
        {
            Name = layerName + ".InputName",
            DestroyUi = layerName + ".InputName",
            Parent = layerName,
            Components =
            {
                new CuiImageComponent
                    {Color = HexToCuiColor("#404040"), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "40 -78",
                    OffsetMax = "300 -36"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = layerName + ".InputName",
            Components =
            {
                new CuiInputFieldComponent
                {
                    Color = HexToCuiColor("#E2DBD3", 50), FontSize = 16, Align = TextAnchor.MiddleLeft,
                    IsPassword = false, Command = $"{Command} creator name", Text = creatorName,
                    NeedsKeyboard = true
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "5 0"
                }
            }
        });


        #region Save

        MeventCuiHelper.CreateButton(container, layerName, Msg(player, UIBtnCreate),
            Command + $" creator save {creatorName}",
            color: HexToCuiColor("#459D30"),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            anchorMin: "0 1", anchorMax: "0 1", offsetMin: "180 -132", offsetMax: "300 -92",
            isBold: true,
            fontSize: 16,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#FFFFFF"));

        #endregion

        #region Delete

        MeventCuiHelper.CreateButton(container, layerName, Msg(player, UIBtnCancel),
            Command + " creator close",
            color: HexToCuiColor(BTN_RED),
            hoverColor: HexToCuiColor(BTN_RED_HOVER),
            pressedColor: HexToCuiColor(BTN_RED_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            anchorMin: "0 1", anchorMax: "0 1", offsetMin: "41 -132", offsetMax: "161 -92",
            isBold: true,
            fontSize: 16,
            textAnchor: TextAnchor.MiddleCenter,
            textColor: HexToCuiColor("#FFFFFF"));

        #endregion
    }

    private void DrawModalCreateEvent(BasePlayer player, bool first = true)
    {
        if (!openedUI.TryGetValue(player.userID, out var adminState)) return;

        var container = new CuiElementContainer();

        if (first)
            DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

        var limitedEdit = adminState.isEditingEvent && !(adminState.editEvent?.isCustom ?? true);

        var modalOffsetMin = limitedEdit ? "-215 -150" : "-215 -265";
        var modalOffsetMax = limitedEdit ? "215 150" : "215 265";

        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", modalOffsetMin, modalOffsetMax, LayerModal,
            LayerBackgroundModal);

        #region Header

        var headerPanel = LayerModal + ".Header";
        container.Add(new CuiElement
        {
            Name = headerPanel,
            DestroyUi = headerPanel,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1B1B1B")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "0 -42", OffsetMax = "0 0"
                }
            }
        });

        var titleText = adminState.isEditingEvent
            ? limitedEdit
                ? Msg(player, UIEditEventSimple)
                : string.Format(Msg(player, UIEditEventTitle),
                    (adminState.editEvent?.displayName ?? string.Empty).ToUpper())
            : Msg(player, UICreateNewEvent);

        container.Add(new CuiElement
        {
            Parent = headerPanel,
            Components =
            {
                new CuiTextComponent
                {
                    Text = titleText, Font = "robotocondensed-bold.ttf",
                    FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "20 0", OffsetMax = "-170 0"
                }
            }
        });

        MeventCuiHelper.AddIconButton(container, headerPanel, headerPanel + ".Close",
            Command + " event close", null, HexToCuiColor(BTN_DARK), hoverColor: HexToCuiColor(BTN_DARK_HOVER),
            pressedColor: HexToCuiColor(BTN_DARK_PRESSED),
            anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-96 -14", offsetMax: "-20 14",
            iconSprite: "assets/icons/exit.png", iconColor: HexToCuiColor("#FFFFFF", 60),
            text: Msg(player, UIBtnClose), textColor: HexToCuiColor("#FFFFFF", 60));

        #endregion

        #region Fields

        if (!limitedEdit)
        {
            DrawCreateEventField(container, player,
                Msg(player, UIDisplayName), 12,
                -82, -62,
                -118, -86,
                Msg(player, UIPlaceholderDisplayName),
                adminState.editEvent?.displayName,
                "name",
                LayerModal + ".InputName");

            DrawCreateEventSeparator(container, -130);

            DrawCreateEventField(container, player,
                Msg(player, UIAuthor), 14,
                -160, -140,
                -196, -164,
                Msg(player, UIPlaceholderAuthor),
                adminState.editEvent?.creator,
                "creator",
                LayerModal + ".InputAuthor");

            DrawCreateEventSeparator(container, -208);

            DrawCreateEventField(container, player,
                Msg(player, UITargetPlugin), 12,
                -238, -218,
                -274, -242,
                Msg(player, UIPlaceholderPlugin),
                adminState.editEvent?.pluginName,
                "plugin",
                LayerModal + ".InputPlugin");

            DrawCreateEventSeparator(container, -286);

            DrawCreateEventField(container, player,
                Msg(player, UIStartConsoleCommand), 12,
                -316, -296,
                -352, -320,
                Msg(player, UIPlaceholderCommand),
                adminState.editEvent?.command,
                "command",
                LayerModal + ".InputCommand");

            DrawCreateEventSeparator(container, -364);

            DrawCreateEventField(container, player,
                Msg(player, UIWipeBlockLabel), 12,
                -394, -374,
                -430, -398,
                Msg(player, UIWipeBlockPlaceholder),
                adminState.editEvent?.wipeBlockSeconds > 0
                    ? adminState.editEvent.wipeBlockSeconds.ToString(CultureInfo.InvariantCulture)
                    : null,
                "wipeblock",
                LayerModal + ".InputWipeBlock");
        }
        else
        {
            DrawCreateEventField(container, player,
                Msg(player, UIStartConsoleCommand), 12,
                -82, -62,
                -118, -86,
                Msg(player, UIPlaceholderCommand),
                adminState.editEvent?.command,
                "command",
                LayerModal + ".InputCommand");

            DrawCreateEventSeparator(container, -130);

            DrawCreateEventField(container, player,
                Msg(player, UIWipeBlockLabel), 12,
                -160, -140,
                -196, -164,
                Msg(player, UIWipeBlockPlaceholder),
                adminState.editEvent?.wipeBlockSeconds > 0
                    ? adminState.editEvent.wipeBlockSeconds.ToString(CultureInfo.InvariantCulture)
                    : null,
                "wipeblock",
                LayerModal + ".InputWipeBlock");
        }

        #endregion

        #region Footer + Create button

        AddHelpFooter(container, LayerModal, player);

        MeventCuiHelper.CreateButton(container, LayerModal,
            command: Command + " event save",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "-195 52", offsetMax: "195 80",
            msg: adminState.isEditingEvent ? Msg(player, UIBtnSaveChanges) : Msg(player, UIBtnCreateEvent),
            isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#BEC7AF"));

        #endregion

        CuiHelper.AddUi(player, container);
    }

    private void DrawCreateEventField(CuiElementContainer container, BasePlayer player,
        string label, int labelFontSize,
        int labelYTop, int labelYBottom,
        int inputYTop, int inputYBottom,
        string placeholder, string value, string fieldKey, string outerName)
    {
        container.Add(new CuiElement
        {
            Parent = LayerModal,
            Components =
            {
                new CuiTextComponent
                {
                    Text = label, Font = "robotocondensed-bold.ttf", FontSize = labelFontSize,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                    OffsetMin = $"-175 {labelYTop}", OffsetMax = $"175 {labelYBottom}"
                }
            }
        });

        container.Add(new CuiElement
        {
            Name = outerName,
            DestroyUi = outerName,
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#202020")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                    OffsetMin = $"-175 {inputYTop}", OffsetMax = $"175 {inputYBottom}"
                }
            }
        });

        var innerName = outerName + ".Inner";
        container.Add(new CuiElement
        {
            Name = innerName,
            DestroyUi = innerName,
            Parent = outerName,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1B1B1B")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "2 2", OffsetMax = "-2 -2"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = innerName,
            Components =
            {
                new CuiInputFieldComponent
                {
                    Color = HexToCuiColor("#E0E0E0", 80), Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft,
                    Command = Command + " event add " + fieldKey,
                    Text = string.IsNullOrEmpty(value) ? placeholder : value,
                    CharsLimit = 128, NeedsKeyboard = true
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "10 0", OffsetMax = "-10 0"
                }
            }
        });
    }

    private void DrawCreateEventSeparator(CuiElementContainer container, int y)
    {
        container.Add(new CuiElement
        {
            Parent = LayerModal,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 5)},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = $"20 {y}", OffsetMax = $"-20 {y + 2}"
                }
            }
        });
    }


    private static T DeepClone<T>(T source) where T : class
    {
        return source == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source));
    }

    private bool IsEditEventDirty(OpenedPlayerData s)
    {
        if (s.editEvent == null || s.editEventSnapshot == null) return false;
        var a = s.editEvent;
        var b = s.editEventSnapshot;
        return a.displayName != b.displayName
               || a.command != b.command
               || a.pluginName != b.pluginName
               || a.creator != b.creator
               || a.scheduleEnabled != b.scheduleEnabled
               || a.wipeBlockSeconds != b.wipeBlockSeconds;
    }

    private bool IsCreatorDirty(OpenedPlayerData s)
    {
        var typed = s.inputCreator ?? string.Empty;
        if (s.editCreatorIndex == -1)
            return !string.IsNullOrWhiteSpace(typed);
        if (s.editCreatorIndex < 0 || s.editCreatorIndex >= data.creators.Count)
            return false;
        return typed != data.creators[s.editCreatorIndex];
    }

    private bool IsRandomTimeDirty(OpenedPlayerData s)
    {
        if (s.editTime == null) return false;
        if (s.editTimeSnapshot == null) return true;
        var a = s.editTime;
        var b = s.editTimeSnapshot;
        return a.type != b.type
               || a.hour != b.hour || a.minute != b.minute
               || a.offsetMin != b.offsetMin || a.offsetMax != b.offsetMax
               || a.interval != b.interval
               || a.minPlayers != b.minPlayers || a.maxPlayers != b.maxPlayers
               || !a.activeDays.SetEquals(b.activeDays)
               || !a.eventsId.SequenceEqual(b.eventsId);
    }

    private void DrawConfirmModal(BasePlayer player, string title, string description, string confirmCommand, bool selfClose = false)
    {
        var container = new CuiElementContainer();
        var layerName = LayerModal + ".Confirm";

        DrawBackground(player, container, layerName, selfClose ? LayerBackground : LayerBackgroundModal);

        var panel = layerName + ".Panel";
        container.Add(new CuiElement
        {
            Name = panel,
            DestroyUi = panel,
            Parent = layerName,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#2C2C2C")},
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-215 -81", OffsetMax = "215 81"}
            }
        });

        var header = panel + ".Header";
        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#1B1B1B")},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -42", OffsetMax = "0 0"}
        }, panel, header);

        container.Add(new CuiElement
        {
            Parent = header,
            Components =
            {
                new CuiTextComponent
                {
                    Text = title, Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft,
                    Color = HexToCuiColor("#FFFFFF", 80)
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 0", OffsetMax = "-170 0"}
            }
        });

        MeventCuiHelper.AddIconButton(container, header, header + ".Close",
            "", layerName, HexToCuiColor(BTN_DARK), hoverColor: HexToCuiColor(BTN_DARK_HOVER),
            pressedColor: HexToCuiColor(BTN_DARK_PRESSED),
            anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-96 -14", offsetMax: "-20 14",
            iconSprite: "assets/icons/exit.png", iconColor: HexToCuiColor("#FFFFFF", 60),
            text: Msg(player, UIBtnClose), textColor: HexToCuiColor("#FFFFFF", 60));

        container.Add(new CuiElement
        {
            Parent = panel,
            Components =
            {
                new CuiTextComponent
                {
                    Text = description, Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "40 -94", OffsetMax = "-40 -52"}
            }
        });

        MeventCuiHelper.CreateButton(container, panel,
            command: confirmCommand,
            color: HexToCuiColor(BTN_RED),
            hoverColor: HexToCuiColor(BTN_RED_HOVER),
            pressedColor: HexToCuiColor(BTN_RED_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "-175 20", offsetMax: "-15 48",
            msg: Msg(player, UIBtnDelete), isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#FFFFFF", 60));

        MeventCuiHelper.CreateButton(container, panel,
            command: "", close: layerName,
            color: HexToCuiColor("#202020"),
            anchorMin: "0.5 0", anchorMax: "0.5 0", offsetMin: "5 20", offsetMax: "175 48",
            msg: Msg(player, UIBtnCancel), isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#E0E0E0"));

        CuiHelper.AddUi(player, container);
    }

    private void DrawSubModalUnsavedChanges(BasePlayer player, string context)
    {
        var container = new CuiElementContainer();

        var layerName = LayerModal + ".UnsavedChanges";

        DrawBackground(player, container, layerName, LayerBackgroundModal);

        var panel = layerName + ".Panel";
        container.Add(new CuiElement
        {
            Name = panel,
            DestroyUi = panel,
            Parent = layerName,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#2C2C2C")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-215 -81", OffsetMax = "215 81"
                }
            }
        });

        var header = panel + ".Header";
        container.Add(new CuiElement
        {
            Name = header,
            DestroyUi = header,
            Parent = panel,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#181818")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "0 -42", OffsetMax = "0 0"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = header,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIUnsavedChanges), Font = "robotocondensed-bold.ttf",
                    FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "20 0", OffsetMax = "-170 0"
                }
            }
        });

        var cmdPrefix = context switch
        {
            "edit_event" => "event",
            "random_pool" => "random",
            "creator" => "creator",
            _ => "event"
        };

        MeventCuiHelper.AddIconButton(container, header, header + ".Close",
            Command + $" {cmdPrefix} unsaved_close", null, HexToCuiColor(BTN_DARK),
            hoverColor: HexToCuiColor(BTN_DARK_HOVER), pressedColor: HexToCuiColor(BTN_DARK_PRESSED),
            anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-96 -14", offsetMax: "-20 14",
            iconSprite: "assets/icons/exit.png", iconColor: HexToCuiColor("#FFFFFF", 60),
            text: Msg(player, UIBtnClose), textColor: HexToCuiColor("#FFFFFF", 60));

        container.Add(new CuiElement
        {
            Parent = panel,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDescUnsavedChanges),
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 14, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "40 -94", OffsetMax = "-40 -52"
                }
            }
        });

        MeventCuiHelper.CreateButton(container, panel,
            command: Command + $" {cmdPrefix} unsaved_discard {context}",
            color: HexToCuiColor(BTN_RED),
            hoverColor: HexToCuiColor(BTN_RED_HOVER),
            pressedColor: HexToCuiColor(BTN_RED_PRESSED),
            anchorMin: "0.5 0", anchorMax: "0.5 0",
            offsetMin: "-175 20", offsetMax: "-15 48",
            msg: Msg(player, UIBtnDiscardAndExit), isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#FFFFFF", 60));

        MeventCuiHelper.CreateButton(container, panel,
            command: Command + $" {cmdPrefix} unsaved_save {context}",
            color: HexToCuiColor(BTN_GREEN),
            hoverColor: HexToCuiColor(BTN_GREEN_HOVER),
            pressedColor: HexToCuiColor(BTN_GREEN_PRESSED),
            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
            sprite: "assets/content/ui/UI.Background.TileTex.psd",
            anchorMin: "0.5 0", anchorMax: "0.5 0",
            offsetMin: "5 20", offsetMax: "175 48",
            msg: Msg(player, UIBtnSaveAndExit), isBold: true, fontSize: 12,
            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#BEC7AF"));

        CuiHelper.AddUi(player, container);
    }

    #endregion

    #region Functions

    private static string HexToCuiColor(string hex, float alpha = 100)
    {
        if (string.IsNullOrEmpty(hex)) hex = "#FFFFFF";

        var str = hex.Trim('#');
        if (str.Length != 6) throw new Exception(hex);
        var r = byte.Parse(str.AsSpan(0, 2), NumberStyles.HexNumber);
        var g = byte.Parse(str.AsSpan(2, 2), NumberStyles.HexNumber);
        var b = byte.Parse(str.AsSpan(4, 2), NumberStyles.HexNumber);

        return $"{(double) r / 255} {(double) g / 255} {(double) b / 255} {alpha / 100f}";
    }

    private void AddHelpFooter(CuiElementContainer container, string parent, BasePlayer player)
    {
        var footerLayer = parent + ".Footer";
        container.Add(new CuiElement
        {
            Name = footerLayer,
            DestroyUi = footerLayer,
            Parent = parent,
            Components =
            {
                new CuiImageComponent {Color = HexToCuiColor("#1B1B1B")},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0", AnchorMax = "1 0",
                    OffsetMin = "0 0", OffsetMax = "0 32"
                }
            }
        });

        container.Add(new CuiElement
        {
            Parent = footerLayer,
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDiscordHelp),
                    Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF")
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "20 0",
                    OffsetMax = "0 0"
                }
            }
        });
    }

    private void CheckDataLoaded()
    {
        if (data == null) LoadData();
    }

    private void SaveAndRefreshTimes()
    {
        data.SaveEvents();
        data.eventTimes?.ForEach(e =>
        {
            e.UpdateInfo();
            e.UpdateOffset();
        });
    }

    #endregion

    #region CuiHelper

    private static class MeventCuiHelper
    {
        public static string CreateButton(CuiElementContainer container, string parent, string msg = null,
            string command = null,
            string close = null,
            string name = null,
            string destroyUi = null,
            string color = "1 1 1 1",
            string hoverColor = null,
            string pressedColor = null,
            string sprite = null,
            string material = null,
            string iconPath = null,
            string iconColor = "1 1 1 1",
            CuiRectTransformComponent iconRect = null,
            string anchorMin = "0 0", string anchorMax = "1 1", string offsetMin = "0 0", string offsetMax = "0 0",
            string textColor = "1 1 1 1",
            int fontSize = 14,
            CuiRectTransformComponent textRect = null,
            TextAnchor textAnchor = TextAnchor.MiddleCenter,
            bool isBold = false)
        {
            if (string.IsNullOrEmpty(name))
                name = CuiHelper.GetGuid();

            var btnComponent = new CuiButtonComponent
            {
                Command = command ?? string.Empty,
                Color = color ?? HexToCuiColor("#FFFFFF")
            };

            if (hoverColor != null && pressedColor != null)
            {
                btnComponent.Color = HexToCuiColor("#FFFFFF");
                btnComponent.NormalColor = color ?? HexToCuiColor("#FFFFFF");
                btnComponent.HighlightedColor = hoverColor;
                btnComponent.PressedColor = pressedColor;
                btnComponent.SelectedColor = pressedColor;
                btnComponent.ColorMultiplier = 1f;
                btnComponent.FadeDuration = -1f;
            }

            if (!string.IsNullOrEmpty(close))
                btnComponent.Close = close;

            if (!string.IsNullOrEmpty(sprite))
                btnComponent.Sprite = sprite;

            if (!string.IsNullOrEmpty(material))
                btnComponent.Material = material;

            var btn = new CuiElement
            {
                Name = name,
                DestroyUi = name,
                Parent = parent,
                Components =
                {
                    btnComponent,
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax
                    }
                }
            };

            if (!string.IsNullOrEmpty(destroyUi))
                btn.DestroyUi = destroyUi;

            container.Add(btn);

            if (!string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(msg))
            {
                ICuiComponent image = iconPath.StartsWith("http")
                    ? new CuiRawImageComponent {Color = iconColor, Url = iconPath}
                    : new CuiImageComponent {Color = iconColor, Sprite = iconPath};

                container.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        image,
                        iconRect ?? new CuiRectTransformComponent
                            {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-35 -7", OffsetMax = "-21 7"}
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = msg,
                            Font = "robotocondensed-" + (!isBold ? "regular" : "bold") + ".ttf",
                            FontSize = fontSize,
                            Align = textAnchor,
                            Color = textColor ?? "1 1 1 1"
                        },
                        textRect ?? new CuiRectTransformComponent
                            {AnchorMin = "0.5 0", AnchorMax = "0.5 1", OffsetMin = "-14 0", OffsetMax = "45 0"}
                    }
                });
            }
            else if (!string.IsNullOrEmpty(msg))
            {
                container.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = msg,
                            Font = "robotocondensed-" + (!isBold ? "regular" : "bold") + ".ttf",
                            FontSize = fontSize,
                            Align = textAnchor,
                            Color = textColor ?? "1 1 1 1"
                        },
                        textRect ?? new CuiRectTransformComponent()
                    }
                });
            }
            else if (!string.IsNullOrEmpty(iconPath))
            {
                ICuiComponent image = iconPath.StartsWith("http")
                    ? new CuiRawImageComponent {Color = iconColor, Url = iconPath}
                    : new CuiImageComponent {Color = iconColor, Sprite = iconPath};

                container.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        image,
                        iconRect ?? new CuiRectTransformComponent()
                    }
                });
            }

            return name;
        }

        public static void AddIconButton(CuiElementContainer container, string parent, string name,
            string command, string close, string buttonColor,
            string anchorMin, string anchorMax, string offsetMin, string offsetMax,
            string iconSprite, string iconColor, string text, string textColor,
            int fontSize = 10, string iconPng = null, string destroyUi = null,
            string material = null, string sprite = null,
            string hoverColor = null, string pressedColor = null)
        {
            var btnComp = new CuiButtonComponent {Color = buttonColor, Command = command ?? ""};
            if (hoverColor != null && pressedColor != null)
            {
                btnComp.Color = HexToCuiColor("#FFFFFF");
                btnComp.NormalColor = buttonColor ?? HexToCuiColor("#FFFFFF");
                btnComp.HighlightedColor = hoverColor;
                btnComp.PressedColor = pressedColor;
                btnComp.SelectedColor = pressedColor;
                btnComp.ColorMultiplier = 1f;
                btnComp.FadeDuration = -1f;
            }

            if (!string.IsNullOrEmpty(close)) btnComp.Close = close;
            if (!string.IsNullOrEmpty(material)) btnComp.Material = material;
            if (!string.IsNullOrEmpty(sprite)) btnComp.Sprite = sprite;

            var btnElement = new CuiElement
            {
                Name = name,
                DestroyUi = name,
                Parent = parent,
                Components =
                {
                    btnComp,
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin, AnchorMax = anchorMax,
                        OffsetMin = offsetMin, OffsetMax = offsetMax
                    }
                }
            };
            if (!string.IsNullOrEmpty(destroyUi)) btnElement.DestroyUi = destroyUi;
            container.Add(btnElement);

            container.Add(new CuiElement
            {
                Name = name + ".Content",
                DestroyUi = name + ".Content",
                Parent = name,
                Components =
                {
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0"},
                    new CuiHorizontalLayoutGroupComponent
                    {
                        ChildAlignment = TextAnchor.MiddleCenter,
                        Spacing = 5f,
                        Padding = "4 0 4 0",
                        ChildControlWidth = false,
                        ChildControlHeight = false
                    },
                    new CuiContentSizeFitterComponent
                    {
                        HorizontalFit = ContentSizeFitter.FitMode.PreferredSize,
                        VerticalFit = ContentSizeFitter.FitMode.PreferredSize
                    }
                }
            });

            ICuiComponent imageComponent = iconPng != null
                ? new CuiRawImageComponent {Color = iconColor, Png = iconPng}
                : new CuiImageComponent {Color = iconColor, Sprite = iconSprite};

            container.Add(new CuiElement
            {
                Name = name + ".Icon",
                DestroyUi = name + ".Icon",
                Parent = name + ".Content",
                Components =
                {
                    imageComponent,
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -6.5", OffsetMax = "13 6.5"}
                }
            });

            container.Add(new CuiElement
            {
                Name = name + ".Text",
                DestroyUi = name + ".Text",
                Parent = name + ".Content",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = text, Font = "robotocondensed-bold.ttf", FontSize = fontSize,
                        Align = TextAnchor.MiddleLeft, Color = textColor
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -8", OffsetMax = "0 8"},
                    new CuiContentSizeFitterComponent
                    {
                        HorizontalFit = ContentSizeFitter.FitMode.PreferredSize,
                        VerticalFit = ContentSizeFitter.FitMode.PreferredSize
                    }
                }
            });
        }

        public static void AddBorderedInput(CuiElementContainer container, string parent,
            string outerName, string innerName,
            string offsetMin, string offsetMax, string command, string text,
            TextAnchor align = TextAnchor.MiddleCenter,
            string inputPadMin = "0 0", string inputPadMax = "0 0",
            string font = null)
        {
            container.Add(new CuiElement
            {
                Name = outerName,
                DestroyUi = outerName,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#202020")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = offsetMin, OffsetMax = offsetMax
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = innerName,
                DestroyUi = innerName,
                Parent = outerName,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#181818")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "2 2", OffsetMax = "-2 -2"
                    }
                }
            });

            var inputComp = new CuiInputFieldComponent
            {
                Color = HexToCuiColor("#E0E0E0"), FontSize = 12, Align = align,
                IsPassword = false, Command = command, Text = text,
                NeedsKeyboard = true
            };
            if (font != null) inputComp.Font = font;

            container.Add(new CuiElement
            {
                Parent = innerName,
                Components =
                {
                    inputComp,
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = inputPadMin, OffsetMax = inputPadMax}
                }
            });
        }
    }

    #endregion

    #endregion

    #region Utils

    private void DisableVanillaEvents()
    {
        disabledEvents.Clear();

        foreach (var ev in data.events)
        {
            if (ev.creator != CreatorFacepunch || string.IsNullOrEmpty(ev.scheduleName)) continue;

            var schedule = EventSchedule.allEvents.FirstOrDefault(s => s.GetName() == ev.scheduleName);
            if (schedule == null) continue;

            var enabled = IsAutoEventEnabled(ev);
            schedule.enabled = enabled;
            if (!enabled)
                disabledEvents.Add(schedule);
        }
    }

    private void EnableVanillaEvents()
    {
        foreach (var ev in disabledEvents)
            ev.enabled = true;
        disabledEvents.Clear();
    }

    private void InitHarmonyPatches()
    {
        try
        {
            _emHarmony = new Harmony(Name + "Patch");
            _emHarmony.Patch(
                AccessTools.Method(typeof(DeepSeaManager), nameof(DeepSeaManager.ServerTick)),
                transpiler: new HarmonyMethod(typeof(DeepSeaServerTickPatch), nameof(DeepSeaServerTickPatch.Transpiler))
            );
        }
        catch (Exception ex)
        {
            PrintError($"Harmony patch failed: {ex.Message}. Plugin will continue without runtime patches.");
        }
    }

    private void DisposeHarmonyPatches()
    {
        _emHarmony?.UnpatchAll(Name + "Patch");
    }

    private static class DeepSeaServerTickPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
                if (instruction.Calls(AccessTools.Method(typeof(DeepSeaManager), nameof(DeepSeaManager.OpenDeepSea))))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(EventsManager), nameof(HandleAutoOpen)));
                else
                    yield return instruction;
        }
    }

    private static void HandleAutoOpen(DeepSeaManager manager)
    {
        if (ShouldSuppressAutoEvent("Deep Sea")) return;
        manager.OpenDeepSea();
    }

    private void CreateController()
    {
        eventController = ServerMgr.Instance.gameObject.AddComponent<EventController>();
    }

    private void DestroyController()
    {
        if (eventController != null)
            UnityEngine.Object.Destroy(eventController);
    }

    private void DestroyUI()
    {
        foreach (var userId in openedUI.Keys)
        {
            var player = BasePlayer.FindByID(userId);
            if (player == null) continue;
            CuiHelper.DestroyUi(player, LayerBackground);
        }
    }

    private void RebuildEventCache()
    {
        if (_grouped == null)
            _grouped = new Dictionary<string, List<int>>();
        else
        {
            foreach (var b in _grouped.Values) b.Clear();
            _grouped.Clear();
        }

        for (var i = 0; i < data.events.Count; i++)
        {
            var key = data.events[i].creator ?? "Unknown";
            if (!_grouped.TryGetValue(key, out var bucket))
                _grouped[key] = bucket = new List<int>();
            bucket.Add(i);
        }

        if (_creatorNames == null) _creatorNames = new List<string>();
        else _creatorNames.Clear();
        foreach (var c in data.creators)
            if (_grouped.ContainsKey(c)) _creatorNames.Add(c);
        foreach (var key in _grouped.Keys)
            if (!_creatorNames.Contains(key)) _creatorNames.Add(key);
    }

    private bool MatchesFilters(int idx, string creatorKey, OpenedPlayerData state)
    {
        var ev = data.events[idx];
        return (creatorKey == CreatorFacepunch || state.showAllCustomEvents || ev.isLoaded) &&
               (string.IsNullOrEmpty(state.eventSearch) ||
                ev.displayName != null &&
                ev.displayName.IndexOf(state.eventSearch, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void CheckIsEventsLoaded()
    {
        foreach (var targetEvent in data.events)
        {
            if (targetEvent.creator == CreatorFacepunch)
            {
                targetEvent.isLoaded = string.IsNullOrEmpty(targetEvent.scheduleName)
                                       || EventSchedule.allEvents.Any(s => s.GetName() == targetEvent.scheduleName);
                continue;
            }

            targetEvent.isLoaded = Instance.plugins.Exists(targetEvent.pluginName);
        }
    }

    private void CheckIsEventsLoaded(string eventName)
    {
        var targetEvent = data.events.Find(e => e.displayName == eventName);
        if (targetEvent == null) return;

        targetEvent.isLoaded = Instance.plugins.Exists(targetEvent.pluginName);
    }

    private void RunGameStateMigrations()
    {
        if (data == null) return;

        var allSchedules = EventSchedule.allEvents;
        if (allSchedules == null || allSchedules.Count == 0)
        {
            if (_pendingIsCustomBackfill || _legacyDisableVanillaAutoEvents)
            {
                if (_migrationRetryCount++ < 12)
                {
                    PrintWarning("EventSchedule not ready during migration; will retry shortly.");
                    timer.In(5f, RunGameStateMigrations);
                }
                else
                    PrintError("EventSchedule still not ready after multiple attempts; pending migration skipped.");
            }

            return;
        }

        var changed = false;
        var currentScheduleNames = new HashSet<string>(allSchedules.Select(s => s.GetName()));

        foreach (var ev in data.events.ToList())
        {
            if (ev.creator != CreatorFacepunch || ev.scheduleName != null) continue;

            string targetName = null;
            foreach (var schedule in allSchedules)
            {
                if (string.Equals(ev.displayName, FormatScheduleName(schedule.GetName()),
                        StringComparison.OrdinalIgnoreCase))
                {
                    targetName = schedule.GetName();
                    break;
                }
            }

            if (targetName == null)
            {
                string FixLegacyScheduleName(string command)
                {
                    if (command?.StartsWith("em_spawn ", StringComparison.OrdinalIgnoreCase) != true)
                        return null;

                    return Path.GetFileName(command[9..].Trim()).ToLowerInvariant() switch
                    {
                        "patrolhelicopter.prefab" => "event_helicopter",
                        "cargo_plane.prefab" => "event_airdrop",
                        "ch47scientists.entity.prefab" => "event_cargoheli",
                        "cargoshiptest.prefab" => "event_cargoship",
                        "santasleigh.prefab" => "event_xmas",
                        "bradleyapc.prefab" => "event_roadbradley",
                        _ => null
                    };
                }

                var guess = FixLegacyScheduleName(ev.command);
                if (guess != null && currentScheduleNames.Contains(guess))
                    targetName = guess;
            }

            if (targetName == null) continue;

            var duplicate = data.events.Find(e =>
                e != ev && e.creator == CreatorFacepunch && e.scheduleName == targetName);

            if (duplicate != null)
            {
                var evUsed = data.eventTimes.Any(t => t.eventsId.Contains(ev.eventId));
                var dupUsed = data.eventTimes.Any(t => t.eventsId.Contains(duplicate.eventId));

                if (evUsed && !dupUsed)
                    data.events.Remove(duplicate);
                else
                {
                    data.events.Remove(ev);
                    continue;
                }
            }

            ev.scheduleName = targetName;
            ev.command = $"triggerevent {targetName}";
            changed = true;
        }

        if (_pendingIsCustomBackfill)
        {
            var defaultEvents = new HashSet<(string, string)>(
                PluginData.BuildDefaultEvents().Select(e => (e.creator, e.displayName)));

            foreach (var ev in data.events)
            {
                if (ev.isCustom) continue;
                if (ev.creator == CreatorFacepunch && !string.IsNullOrEmpty(ev.scheduleName)) continue;
                if (defaultEvents.Contains((ev.creator, ev.displayName))) continue;
                ev.isCustom = true;
                changed = true;
            }

            _pendingIsCustomBackfill = false;
        }

        if (_legacyDisableVanillaAutoEvents)
        {
            var allowlist = _legacyVanillaAllowlist ?? new List<string>();
            var anyDisabled = false;
            foreach (var ev in data.events)
            {
                if (ev.creator != CreatorFacepunch || string.IsNullOrEmpty(ev.scheduleName)) continue;
                if (allowlist.Contains(ev.scheduleName)) continue;
                _config.AutoEvents[AutoEventKey(ev.displayName)] = false;
                anyDisabled = true;
            }

            _legacyDisableVanillaAutoEvents = false;
            _legacyVanillaAllowlist = null;
            if (anyDisabled)
            {
                SaveConfig();
                changed = true;
            }
        }

        if (changed)
        {
            data.SaveEvents();
            SaveAndRefreshTimes();
        }
    }

    private void SyncFacepunchEvents()
    {
        var allSchedules = EventSchedule.allEvents;
        var changed = false;

        var currentScheduleNames = new HashSet<string>();
        foreach (var schedule in allSchedules)
            currentScheduleNames.Add(schedule.GetName());

        foreach (var schedule in allSchedules)
        {
            var name = schedule.GetName();
            if (data.events.Exists(e => e.creator == CreatorFacepunch && e.scheduleName == name))
                continue;

            var displayName = FormatScheduleName(name);

            data.events.Add(new Event
            {
                displayName = displayName,
                command = $"triggerevent {name}",
                creator = CreatorFacepunch,
                scheduleName = name
            });
            changed = true;
            Puts($"Added new Facepunch event: {displayName} ({name})");
        }


        var seenScheduleNames = new HashSet<string>();
        var deduped = data.events.RemoveAll(e =>
        {
            if (e.creator != CreatorFacepunch || e.scheduleName == null) return false;
            return !seenScheduleNames.Add(e.scheduleName);
        });
        if (deduped > 0) changed = true;


        var removed = data.events.RemoveAll(e =>
            e.creator == CreatorFacepunch &&
            e.scheduleName != null && !currentScheduleNames.Contains(e.scheduleName));

        foreach (var ev in data.events)
        {
            if (ev.creator != CreatorFacepunch || string.IsNullOrEmpty(ev.scheduleName)) continue;
            var target = FormatScheduleName(ev.scheduleName);
            if (ev.displayName == target) continue;

            var oldKey = AutoEventKey(ev.displayName);
            var newKey = AutoEventKey(target);
            if (_config.AutoEvents.TryGetValue(oldKey, out var oldVal))
            {
                _config.AutoEvents.Remove(oldKey);
                _config.AutoEvents[newKey] = oldVal;
            }

            ev.displayName = target;
            changed = true;
        }

        if (removed + deduped > 0)
        {
            changed = true;
            Puts($"Removed {removed + deduped} obsolete/duplicate Facepunch event(s)");

            var validIds = new HashSet<int>(data.events.Select(e => e.eventId));
            foreach (var et in data.eventTimes)
                et.eventsId.RemoveAll(id => !validIds.Contains(id));

            data.SaveTimes();
        }

        if (!data.creators.Contains(CreatorFacepunch))
        {
            data.creators.Insert(0, CreatorFacepunch);
            changed = true;
        }

        if (changed)
        {
            data.SaveEvents();
            data.SaveCreators();
        }
    }

    private readonly Dictionary<string, string> ScheduleDisplayNameOverrides = new()
    {
        ["event_helicopter"] = "Patrol Helicopter",
        ["event_cargoheli"] = "Chinook (CH47)",
        ["event_cargoship"] = "Cargo Ship",
        ["event_easter"] = "Easter Event",
        ["event_roadbradley"] = "Road Bradley APC",
        ["event_travellingvendor"] = "Travelling Vendor",
        ["event_airdrop"] = "Airdrop",
        ["event_f15e"] = "F15e"
    };

    private string FormatScheduleName(string scheduleName)
    {
        if (ScheduleDisplayNameOverrides.TryGetValue(scheduleName, out var overrideName))
            return overrideName;

        var name = scheduleName.StartsWith("event_") ? scheduleName.Substring(6) : scheduleName;
        if (name.Length > 0) name = char.ToUpper(name[0]) + name.Substring(1);
        name = name.Replace("_", " ");
        return name;
    }

    private static readonly FieldInfo
        HoursRemainingField =
            typeof(EventSchedule).GetField("hoursRemaining",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
        RoadsField =
            typeof(TerrainPath).GetField("Roads",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
        MainRoadsField =
            typeof(TerrainPath).GetField("MainRoads",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static int GetPathListCount(FieldInfo field, TerrainPath path)
    {
        return (field?.GetValue(path) as IList)?.Count ?? 0;
    }

    private static void TriggerFacepunchEvent(string scheduleName)
    {
        var schedule = EventSchedule.allEvents.FirstOrDefault(s => s.GetName() == scheduleName);
        if (schedule == null) return;

        var components = schedule.GetComponents<TriggeredEvent>();
        if (components.Length == 0) return;

        var picked = components.GetRandom();
        if (picked == null) return;

        HoursRemainingField?.SetValue(schedule,
            Random.Range(schedule.minimumHoursBetween, schedule.maxmumHoursBetween));

        RunTriggeredEventSilent(picked);
    }

    private static void RunTriggeredEventSilent(TriggeredEvent ev)
    {
        switch (ev)
        {
            case TriggeredEventPrefab prefab:
                SpawnTriggeredEventPrefab(prefab);
                return;

            case TravellingVendorEvent vendor:
                SpawnTravellingVendorEvent(vendor);
                return;

            default:
                ev.RunEvent();
                return;
        }
    }

    private static void SpawnTriggeredEventPrefab(TriggeredEventPrefab ev)
    {
        if (Interface.Oxide.CallHook("OnEventTrigger", ev) != null) return;

        var path = ev.targetPrefab?.resourcePath;
        if (string.IsNullOrEmpty(path)) return;

        Debug.Log("[event] " + path);

        var spawned = GameManager.server.CreateEntity(path);
        if (!spawned) return;

        spawned.SendMessage("TriggeredEventSpawn", SendMessageOptions.DontRequireReceiver);
        spawned.Spawn();
        spawned.SendMessage("TriggeredEventPostSpawn", SendMessageOptions.DontRequireReceiver);
        ev.spawnedEntity = spawned;
    }

    private static void SpawnTravellingVendorEvent(TravellingVendorEvent ev)
    {
        var path = TerrainMeta.Path;
        if (TravellingVendorEvent.currentVendor != null || path == null || GetPathListCount(RoadsField, path) == 0 || !TravellingVendor.should_spawn || RoadBradleys.StaticBradleyCount > 0 || GetPathListCount(MainRoadsField, path) == 0) return;

        var vendor = TravellingVendor.SpawnTravellingVendorForEvent();
        if (!vendor)
        {
            Debug.Log("Failed to spawn travelling vendor.");
            return;
        }

        Debug.Log("[event] assets/prefabs/npc/travelling vendor/travellingvendor.prefab");
        TravellingVendorEvent.currentVendor = vendor;
    }

    private void SendLogToDiscord(string eventName)
    {
        var color = 0x1F8B4C;
        if (!string.IsNullOrEmpty(_config.Discord.EmbedColor))
            int.TryParse(_config.Discord.EmbedColor.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);

        new DiscordMessage(null, new DiscordEmbed[1]
        {
            new(_config.Discord.Title, "", color, null,
                new DiscordFooter($"{eventName}", _config.Discord.FooterIcon))
        }).Send(_config.Discord.WebHook);
    }

    #region DiscordEmbedMessage

    public class DiscordMessage
    {
        public string content;
        public DiscordEmbed[] embeds;

        public DiscordMessage(string Content, DiscordEmbed[] Embeds = null)
        {
            content = Content;
            embeds = Embeds;
        }

        public void Send(string url)
        {
            Instance.webrequest.Enqueue(url, JsonConvert.SerializeObject(this), (_, _) => { },
                Instance, RequestMethod.POST, JsonHeaders, 30f);
        }

        public static Dictionary<string, string> JsonHeaders = new()
        {
            ["Content-Type"] = "application/json"
        };
    }

    public class DiscordEmbed
    {
        public string title;
        public string description;
        public int? color;
        public DiscordField[] fields;
        public DiscordFooter footer;
        public DiscordAuthor author;

        public DiscordEmbed(string Title, string Description, int? Color = null, DiscordField[] Fields = null,
            DiscordFooter Footer = null, DiscordAuthor Author = null)
        {
            title = Title;
            description = Description;
            color = Color;
            fields = Fields;
            footer = Footer;
            author = Author;
        }
    }

    public class DiscordFooter
    {
        public string text;
        public string icon_url;
        public string proxy_icon_url;

        public DiscordFooter(string Text, string Icon_url, string Proxy_icon_url = null)
        {
            text = Text;
            icon_url = Icon_url;
            proxy_icon_url = Proxy_icon_url;
        }
    }

    public class DiscordAuthor
    {
        public string name;
        public string url;
        public string icon_url;
        public string proxy_icon_url;

        public DiscordAuthor(string Name, string Url, string Icon_url, string Proxy_icon_url = null)
        {
            name = Name;
            url = Url;
            icon_url = Icon_url;
            proxy_icon_url = Proxy_icon_url;
        }
    }

    public class DiscordField
    {
        public string name;
        public string value;
        public bool inline;

        public DiscordField(string Name, string Value, bool Inline = false)
        {
            name = Name;
            value = Value;
            inline = Inline;
        }
    }

    #endregion DiscordEmbedMessage

    #endregion

    #region Lang

    private const string
        UITitle = "UITitle",
        UIServerTime = "UIServerTime",
        UIEventMode = "UIEventMode",
        UIPage = "UIPage",
        UIActiveSchedule = "UIActiveSchedule",
        UIEmptyActiveSchedule = "UIEmptyActiveSchedule",
        UIEmptyTimeSettings = "UIEmptyTimeSettings",
        UITimeSettings = "UITimeSettings",
        UIEventPool = "UIEventPool",
        UISelectEvent = "UISelectEvent",
        UINewCreator = "UINewCreator",
        UIEditCreator = "UIEditCreator",
        UIActiveTimers = "UIActiveTimers",
        UISettings = "UISettings",
        UIGlobalOverrides = "UIGlobalOverrides",
        UIVanillaEventsControl = "UIVanillaEventsControl",
        UIManageAuthors = "UIManageAuthors",
        UIAuthorList = "UIAuthorList",
        UIEditName = "UIEditName",
        UINewAuthor = "UINewAuthor",
        UIUnsavedChanges = "UIUnsavedChanges",
        UIWarningDeleteAuthor = "UIWarningDeleteAuthor",
        UIModeDefault = "UIModeDefault",
        UIModeRandom = "UIModeRandom",
        UIDescMainPanel = "UIDescMainPanel",
        UIDescDefaultMode = "UIDescDefaultMode",
        UIDescRandomMode = "UIDescRandomMode",
        UIDescPlayerRange = "UIDescPlayerRange",
        UIDescOffsetTime = "UIDescOffsetTime",
        UIDescRepeatInterval = "UIDescRepeatInterval",
        UIDescRandomTime = "UIDescRandomTime",
        UIDescRepeatType = "UIDescRepeatType",
        UIDescScheduleDays = "UIDescScheduleDays",
        UIDescUnsavedChanges = "UIDescUnsavedChanges",
        UIDescDeleteAuthor = "UIDescDeleteAuthor",
        UIDiscordHelp = "UIDiscordHelp",
        UIRequiredOnline = "UIRequiredOnline",
        UIMinPlayers = "UIMinPlayers",
        UIMaxPlayers = "UIMaxPlayers",
        UITime = "UITime",
        UIHour = "UIHour",
        UIMinute = "UIMinute",
        UIOffsetTime = "UIOffsetTime",
        UIMin = "UIMin",
        UIMax = "UIMax",
        UIRepeatInterval = "UIRepeatInterval",
        UIMinutes = "UIMinutes",
        UIRandomTime = "UIRandomTime",
        UIRepeatType = "UIRepeatType",
        UIScheduleDays = "UIScheduleDays",
        UISelectAll = "UISelectAll",
        UIDisplayName = "UIDisplayName",
        UIConsoleCommand = "UIConsoleCommand",
        UITargetPlugin = "UITargetPlugin",
        UICreatorName = "UICreatorName",
        UIAuthorName = "UIAuthorName",
        UIChooseAuthor = "UIChooseAuthor",
        UIStaticTimeFormat = "UIStaticTimeFormat",
        UIServerTimeCountdown = "UIServerTimeCountdown",
        UITimeLeftPlaceholder = "UITimeLeftPlaceholder",
        UIBtnClose = "UIBtnClose",
        UIBtnSettings = "UIBtnSettings",
        UIBtnSave = "UIBtnSave",
        UIBtnSaveChanges = "UIBtnSaveChanges",
        UIBtnSaveAndExit = "UIBtnSaveAndExit",
        UIBtnCreate = "UIBtnCreate",
        UIBtnCreateEvent = "UIBtnCreateEvent",
        UIBtnCancel = "UIBtnCancel",
        UIBtnDiscardAndExit = "UIBtnDiscardAndExit",
        UIBtnDeleteEverything = "UIBtnDeleteEverything",
        UIBtnDelete = "UIBtnDelete",
        UIConfirmTitleDeleteEvent = "UIConfirmTitleDeleteEvent",
        UIConfirmDescDeleteEvent = "UIConfirmDescDeleteEvent",
        UIConfirmTitleDeleteTimer = "UIConfirmTitleDeleteTimer",
        UIConfirmDescDeleteTimer = "UIConfirmDescDeleteTimer",
        UIBtnAddNewEvent = "UIBtnAddNewEvent",
        UIBtnManageAuthors = "UIBtnManageAuthors",
        UIBtnAddNewAuthor = "UIBtnAddNewAuthor",
        UIBtnCreateSchedule = "UIBtnCreateSchedule",
        UIBtnCreateTimer = "UIBtnCreateTimer",
        UIBtnPrev = "UIBtnPrev",
        UIBtnNext = "UIBtnNext",
        UIBtnEdit = "UIBtnEdit",
        UIPlaceholderSearch = "UIPlaceholderSearch",
        UIPlaceholderSearchEvent = "UIPlaceholderSearchEvent",
        UIRandomScheduleTitle = "UIRandomScheduleTitle",
        UIRandomScheduleDesc = "UIRandomScheduleDesc",
        UIFacepunchEventTitle = "UIFacepunchEventTitle",
        UIFacepunchEventDesc = "UIFacepunchEventDesc",
        UICustomEventListTitle = "UICustomEventListTitle",
        UICustomEventListDesc = "UICustomEventListDesc",
        UIShowAllEventBase = "UIShowAllEventBase",
        UIBtnManageRandomPool = "UIBtnManageRandomPool",
        UIBtnAddNew = "UIBtnAddNew",
        UIBtnRescan = "UIBtnRescan",
        UIBtnBack = "UIBtnBack",
        UIRandomPoolEditor = "UIRandomPoolEditor",
        UIRandomPoolFooterDesc = "UIRandomPoolFooterDesc",
        UIRandomPoolFooterSupport = "UIRandomPoolFooterSupport",
        UINoTimersYet = "UINoTimersYet",
        UIUseSchedule = "UIUseSchedule",
        UIOn = "UIOn",
        UIOff = "UIOff",
        UIBtnStartEvent = "UIBtnStartEvent",
        UIBtnStopEvent = "UIBtnStopEvent",
        UIEditEventEmptyHint = "UIEditEventEmptyHint",
        UIEditEventTitle = "UIEditEventTitle",
        UIEditEventSimple = "UIEditEventSimple",
        UICreateNewEvent = "UICreateNewEvent",
        UIEventByAuthor = "UIEventByAuthor",
        UIDefaultSchedule = "UIDefaultSchedule",
        UIPlaceholderDisplayName = "UIPlaceholderDisplayName",
        UIPlaceholderCommand = "UIPlaceholderCommand",
        UIPlaceholderPlugin = "UIPlaceholderPlugin",
        UIPlaceholderCreatorName = "UIPlaceholderCreatorName",
        UIPlaceholderAuthor = "UIPlaceholderAuthor",
        UIAuthor = "UIAuthor",
        UIStartConsoleCommand = "UIStartConsoleCommand",
        UIEveryXMinutes = "UIEveryXMinutes",
        UIEveryRangeMinutes = "UIEveryRangeMinutes",
        UIEveryday = "UIEveryday",
        UITypeStatic = "UITypeStatic",
        UITypeEvery = "UITypeEvery",
        UITypeRandom = "UITypeRandom",
        UIDayMonday = "UIDayMonday",
        UIDayTuesday = "UIDayTuesday",
        UIDayWednesday = "UIDayWednesday",
        UIDayThursday = "UIDayThursday",
        UIDayFriday = "UIDayFriday",
        UIDaySaturday = "UIDaySaturday",
        UIDaySunday = "UIDaySunday",
        MsgEnterDisplayName = "MsgEnterDisplayName",
        MsgEnterCommand = "MsgEnterCommand",
        MsgEnterPluginName = "MsgEnterPluginName",
        MsgChooseAuthor = "MsgChooseAuthor",
        MsgEventAlreadyActive = "MsgEventAlreadyActive",
        UIViewModeList = "UIViewModeList",
        UIViewModeColumns = "UIViewModeColumns",
        UIWipeBlockLabel = "UIWipeBlockLabel",
        UIWipeBlockPlaceholder = "UIWipeBlockPlaceholder",
        MsgWipeBlockWarn = "MsgWipeBlockWarn";

    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            [UITitle] = "EVENT MANAGER",
            [UIServerTime] = "SERVER TIME",
            [UIEventMode] = "EVENT MODE",
            [UIPage] = "PAGE",
            [UIActiveSchedule] = "ACTIVE SCHEDULE",
            [UIEmptyActiveSchedule] = "Create your first schedule\nto manage the event.",
            [UIEmptyTimeSettings] = "Select or create a schedule to edit its settings",
            [UITimeSettings] = "TIME SETTINGS",
            [UIEventPool] = "EVENT POOL",
            [UISelectEvent] = "SELECT EVENT",
            [UINewCreator] = "NEW CREATOR",
            [UIEditCreator] = "EDIT CREATOR",
            [UIActiveTimers] = "ACTIVE TIMERS",
            [UISettings] = "SETTINGS",
            [UIGlobalOverrides] = "GLOBAL OVERRIDES",
            [UIVanillaEventsControl] = "VANILLA EVENTS CONTROL",
            [UIManageAuthors] = "MANAGE AUTHORS",
            [UIAuthorList] = "AUTHOR LIST",
            [UIEditName] = "EDIT NAME",
            [UINewAuthor] = "NEW AUTHOR",
            [UIUnsavedChanges] = "UNSAVED CHANGES",
            [UIWarningDeleteAuthor] = "WARNING! DELETE AUTHOR?",
            [UIModeDefault] = "DEFAULT",
            [UIModeRandom] = "RANDOM",
            [UIDescMainPanel] =
                "Use this panel to manage, create, and monitor all server events. To add a custom location, click 'Add New Creator' and follow the on-screen markers. Need help? Visit our Discord <color=#68C2FF>discord.gg/mevent</color>",
            [UIDescDefaultMode] = "Set precise timers and fixed schedules for individual events.",
            [UIDescRandomMode] = "Automatically cycles through a playlist of events based on random intervals.",
            [UIDescPlayerRange] =
                "Set the player range for the event to start.\n<color=#AA4735>Warning</color> Max players 0 disables the event.",
            [UIDescOffsetTime] = "Optional time offset range.",
            [UIDescRepeatInterval] = "Cooldown starts after the event is finished.",
            [UIDescRandomTime] = "Random interval between min and max.",
            [UIDescRepeatType] = "Defines how the timer calculates the next start.",
            [UIDescScheduleDays] = "Select days when this timer is active.",
            [UIDescUnsavedChanges] = "You have modified settings that are not saved yet.\nWhat would you like to do?",
            [UIDescDeleteAuthor] =
                "THIS ACTION WILL PERMANENTLY REMOVE ALL EVENTS\nASSOCIATED WITH THIS AUTHOR.\nARE YOU SURE YOU WANT TO PROCEED?",
            [UIDiscordHelp] = "Need help? Visit our Discord <color=#68C2FF>discord.gg/mevent</color>",
            [UIRequiredOnline] = "REQUIRED ONLINE",
            [UIMinPlayers] = "MIN PLAYERS",
            [UIMaxPlayers] = "MAX PLAYERS",
            [UITime] = "TIME",
            [UIHour] = "HOUR",
            [UIMinute] = "MINUTE",
            [UIOffsetTime] = "OFFSET TIME (MINUTES)",
            [UIMin] = "MIN",
            [UIMax] = "MAX",
            [UIRepeatInterval] = "REPEAT INTERVAL",
            [UIMinutes] = "MINUTES",
            [UIRandomTime] = "RANDOM TIME (MINUTES)",
            [UIRepeatType] = "REPEAT TYPE",
            [UIScheduleDays] = "SCHEDULE DAYS",
            [UISelectAll] = "SELECT ALL",
            [UIDisplayName] = "DISPLAY NAME",
            [UIConsoleCommand] = "CONSOLE COMMAND",
            [UITargetPlugin] = "TARGET PLUGIN",
            [UICreatorName] = "CREATOR NAME",
            [UIAuthorName] = "AUTHOR NAME",
            [UIChooseAuthor] = "CHOOSE EVENT AUTHOR",
            [UIStaticTimeFormat] = "[Static] {HH:MM}",
            [UIServerTimeCountdown] = "SERVER TIME: %TIME_LEFT%",
            [UITimeLeftPlaceholder] = "%TIME_LEFT%",
            [UIBtnClose] = "CLOSE",
            [UIBtnSettings] = "SETTINGS",
            [UIBtnSave] = "SAVE",
            [UIBtnSaveChanges] = "SAVE CHANGES",
            [UIBtnSaveAndExit] = "SAVE & EXIT",
            [UIBtnCreate] = "CREATE",
            [UIBtnCreateEvent] = "CREATE EVENT",
            [UIBtnCancel] = "CANCEL",
            [UIBtnDiscardAndExit] = "DISCARD & EXIT",
            [UIBtnDeleteEverything] = "DELETE EVERYTHING",
            [UIBtnDelete] = "DELETE",
            [UIConfirmTitleDeleteEvent] = "DELETE EVENT",
            [UIConfirmDescDeleteEvent] =
                "Are you sure you want to delete this event?\nAll its timers will also be removed.",
            [UIConfirmTitleDeleteTimer] = "DELETE EVENT TIMER",
            [UIConfirmDescDeleteTimer] =
                "Are you sure you want to remove this specific schedule?\nOther timers for this event will remain active.",
            [UIBtnAddNewEvent] = "ADD NEW EVENT",
            [UIBtnManageAuthors] = "MANAGE AUTHORS",
            [UIBtnAddNewAuthor] = "ADD NEW AUTHOR",
            [UIBtnCreateSchedule] = "CREATE NEW SCHEDULE",
            [UIBtnCreateTimer] = "CREATE NEW TIMER",
            [UIBtnPrev] = "<",
            [UIBtnNext] = ">",
            [UIBtnEdit] = "✎",
            [UIPlaceholderSearch] = "Search event...",
            [UIPlaceholderSearchEvent] = "SEARCH EVENT NAME...",
            [UIRandomScheduleTitle] = "RANDOM SCHEDULE",
            [UIRandomScheduleDesc] =
                "Allows you to create a pool of events that will run randomly. \nThis works independently and simultaneously with your main schedules.",
            [UIFacepunchEventTitle] = "FACEPUNCH EVENT",
            [UIFacepunchEventDesc] =
                "Official vanilla events developed by Facepunch\nClick to edit schedule. Events without custom settings will run on a default schedule.",
            [UICustomEventListTitle] = "CUSTOM EVENT LIST",
            [UICustomEventListDesc] =
                "All events discovered during the server scan. If a newly added plugin is missing, click 'RESCAN'.\nIf the event is still not found, use 'ADD NEW' to add it manually to our database.",
            [UIShowAllEventBase] = "SHOW ALL EVENTS",
            [UIBtnManageRandomPool] = "MANAGE RANDOM POOL",
            [UIBtnAddNew] = "ADD NEW",
            [UIBtnRescan] = "RESCAN",
            [UIBtnBack] = "BACK",
            [UIRandomPoolEditor] = "RANDOM POOL EDITOR",
            [UIRandomPoolFooterDesc] =
                "Manage the list of events and their spawn conditions for the random rotation system.",
            [UIRandomPoolFooterSupport] = "Support & Suggestions: <color=#68C2FF>discord.gg/mevent</color>",
            [UINoTimersYet] = "NO TIMERS YET",
            [UIUseSchedule] = "USE SCHEDULE",
            [UIOn] = "ON",
            [UIOff] = "OFF",
            [UIBtnStartEvent] = "START EVENT",
            [UIBtnStopEvent] = "STOP EVENT",
            [UIEditEventEmptyHint] =
                "Create your first schedule to manage the event.\n\nRecommended to disable 'Auto-start' in event configuration.",
            [UIEditEventTitle] = "EDIT EVENT: {0}",
            [UIEditEventSimple] = "EDIT EVENT",
            [UICreateNewEvent] = "CREATE NEW EVENT",
            [UIEventByAuthor] = "by {0}",
            [UIDefaultSchedule] = "DEFAULT SCHEDULE",
            [UIPlaceholderDisplayName] = "e.g. My Awesome Event",
            [UIPlaceholderCommand] = "e.g. operation.start",
            [UIPlaceholderPlugin] = "e.g. OperationFortress",
            [UIPlaceholderCreatorName] = "Enter creator name...",
            [UIPlaceholderAuthor] = "e.g. Mevent",
            [UIAuthor] = "AUTHOR",
            [UIStartConsoleCommand] = "START CONSOLE COMMAND",
            [UIEveryXMinutes] = "Every {0} minutes",
            [UIEveryRangeMinutes] = "Every {0} - {1} minutes",
            [UIEveryday] = "Everyday",
            [UITypeStatic] = "STATIC",
            [UITypeEvery] = "EVERY",
            [UITypeRandom] = "RANDOM",
            [UIDayMonday] = "Monday",
            [UIDayTuesday] = "Tuesday",
            [UIDayWednesday] = "Wednesday",
            [UIDayThursday] = "Thursday",
            [UIDayFriday] = "Friday",
            [UIDaySaturday] = "Saturday",
            [UIDaySunday] = "Sunday",
            [MsgEnterDisplayName] = "Please enter a display name for the event.",
            [MsgEnterCommand] = "Please enter a console command for the event.",
            [MsgEnterPluginName] = "Please enter a plugin name for the event.",
            [MsgChooseAuthor] = "Please choose an event author.",
            [MsgEventAlreadyActive] = "{0} is already running. Wait for it to finish before triggering it again.",
            [UIViewModeList] = "LIST",
            [UIViewModeColumns] = "COLUMNS",
            [UIWipeBlockLabel] = "WIPE BLOCK (SECONDS)",
            [UIWipeBlockPlaceholder] = "0 - disabled",
            [MsgWipeBlockWarn] = "Event '{0}' is wipe-blocked for {1}. Starting anyway."
        }, this);

        lang.RegisterMessages(new Dictionary<string, string>
        {
            [UITitle] = "МЕНЕДЖЕР СОБЫТИЙ",
            [UIServerTime] = "ВРЕМЯ СЕРВЕРА",
            [UIEventMode] = "РЕЖИМ СОБЫТИЙ",
            [UIPage] = "СТРАНИЦА",
            [UIActiveSchedule] = "АКТИВНОЕ РАСПИСАНИЕ",
            [UIEmptyActiveSchedule] = "Создайте первое расписание,\nчтобы управлять событием.",
            [UIEmptyTimeSettings] = "Выберите или создайте расписание для настройки",
            [UITimeSettings] = "НАСТРОЙКИ ВРЕМЕНИ",
            [UIEventPool] = "ПУЛ СОБЫТИЙ",
            [UISelectEvent] = "ВЫБРАТЬ СОБЫТИЕ",
            [UINewCreator] = "НОВЫЙ АВТОР",
            [UIEditCreator] = "РЕДАКТИРОВАТЬ АВТОРА",
            [UIActiveTimers] = "АКТИВНЫЕ ТАЙМЕРЫ",
            [UISettings] = "НАСТРОЙКИ",
            [UIGlobalOverrides] = "ГЛОБАЛЬНЫЕ ПЕРЕОПРЕДЕЛЕНИЯ",
            [UIVanillaEventsControl] = "УПРАВЛЕНИЕ ВАНИЛЬНЫМИ СОБЫТИЯМИ",
            [UIManageAuthors] = "УПРАВЛЕНИЕ АВТОРАМИ",
            [UIAuthorList] = "СПИСОК АВТОРОВ",
            [UIEditName] = "РЕДАКТИРОВАТЬ ИМЯ",
            [UINewAuthor] = "НОВЫЙ АВТОР",
            [UIUnsavedChanges] = "НЕСОХРАНЁННЫЕ ИЗМЕНЕНИЯ",
            [UIWarningDeleteAuthor] = "ВНИМАНИЕ! УДАЛИТЬ АВТОРА?",
            [UIModeDefault] = "СТАНДАРТНЫЙ",
            [UIModeRandom] = "СЛУЧАЙНЫЙ",
            [UIDescMainPanel] =
                "Используйте эту панель для управления, создания и мониторинга всех серверных событий. Чтобы добавить пользовательское расположение, нажмите 'Добавить нового автора' и следуйте маркерам на экране. Нужна помощь? Посетите наш Discord <color=#68C2FF>discord.gg/mevent</color>",
            [UIDescDefaultMode] = "Установите точные таймеры и фиксированное расписание для отдельных событий.",
            [UIDescRandomMode] = "Автоматически перебирает список событий на основе случайных интервалов.",
            [UIDescPlayerRange] =
                "Установите диапазон игроков для запуска события.\n<color=#AA4735>Внимание</color> Макс. игроков 0 отключает событие.",
            [UIDescOffsetTime] = "Необязательный диапазон смещения времени.",
            [UIDescRepeatInterval] = "Перезарядка начинается после завершения события.",
            [UIDescRandomTime] = "Случайный интервал между мин. и макс.",
            [UIDescRepeatType] = "Определяет, как таймер рассчитывает следующий запуск.",
            [UIDescScheduleDays] = "Выберите дни, когда этот таймер активен.",
            [UIDescUnsavedChanges] = "Вы изменили настройки, которые ещё не сохранены.\nЧто вы хотите сделать?",
            [UIDescDeleteAuthor] =
                "ЭТО ДЕЙСТВИЕ НАВСЕГДА УДАЛИТ ВСЕ СОБЫТИЯ,\nСВЯЗАННЫЕ С ЭТИМ АВТОРОМ.\nВЫ УВЕРЕНЫ, ЧТО ХОТИТЕ ПРОДОЛЖИТЬ?",
            [UIDiscordHelp] = "Нужна помощь? Посетите наш Discord <color=#68C2FF>discord.gg/mevent</color>",
            [UIRequiredOnline] = "ТРЕБУЕМЫЙ ОНЛАЙН",
            [UIMinPlayers] = "МИН. ИГРОКОВ",
            [UIMaxPlayers] = "МАКС. ИГРОКОВ",
            [UITime] = "ВРЕМЯ",
            [UIHour] = "ЧАС",
            [UIMinute] = "МИНУТА",
            [UIOffsetTime] = "СМЕЩЕНИЕ ВРЕМЕНИ (МИНУТЫ)",
            [UIMin] = "МИН",
            [UIMax] = "МАКС",
            [UIRepeatInterval] = "ИНТЕРВАЛ ПОВТОРА",
            [UIMinutes] = "МИНУТЫ",
            [UIRandomTime] = "СЛУЧАЙНОЕ ВРЕМЯ (МИНУТЫ)",
            [UIRepeatType] = "ТИП ПОВТОРА",
            [UIScheduleDays] = "ДНИ РАСПИСАНИЯ",
            [UISelectAll] = "ВЫБРАТЬ ВСЕ",
            [UIDisplayName] = "ОТОБРАЖАЕМОЕ ИМЯ",
            [UIConsoleCommand] = "КОНСОЛЬНАЯ КОМАНДА",
            [UITargetPlugin] = "ЦЕЛЕВОЙ ПЛАГИН",
            [UICreatorName] = "ИМЯ АВТОРА",
            [UIAuthorName] = "ИМЯ АВТОРА",
            [UIChooseAuthor] = "ВЫБЕРИТЕ АВТОРА СОБЫТИЯ",
            [UIStaticTimeFormat] = "[Статич.] {ЧЧ:ММ}",
            [UIServerTimeCountdown] = "ВРЕМЯ СЕРВЕРА: %TIME_LEFT%",
            [UITimeLeftPlaceholder] = "%TIME_LEFT%",
            [UIBtnClose] = "ЗАКРЫТЬ",
            [UIBtnSettings] = "НАСТРОЙКИ",
            [UIBtnSave] = "СОХРАНИТЬ",
            [UIBtnSaveChanges] = "СОХРАНИТЬ ИЗМЕНЕНИЯ",
            [UIBtnSaveAndExit] = "СОХРАНИТЬ И ВЫЙТИ",
            [UIBtnCreate] = "СОЗДАТЬ",
            [UIBtnCreateEvent] = "СОЗДАТЬ СОБЫТИЕ",
            [UIBtnCancel] = "ОТМЕНА",
            [UIBtnDiscardAndExit] = "ОТМЕНИТЬ И ВЫЙТИ",
            [UIBtnDeleteEverything] = "УДАЛИТЬ ВСЁ",
            [UIBtnDelete] = "УДАЛИТЬ",
            [UIConfirmTitleDeleteEvent] = "УДАЛЕНИЕ СОБЫТИЯ",
            [UIConfirmDescDeleteEvent] =
                "Вы уверены, что хотите удалить это событие?\nВсе его таймеры также будут удалены.",
            [UIConfirmTitleDeleteTimer] = "УДАЛЕНИЕ ТАЙМЕРА",
            [UIConfirmDescDeleteTimer] =
                "Вы уверены, что хотите удалить это расписание?\nДругие таймеры этого события останутся активными.",
            [UIBtnAddNewEvent] = "ДОБАВИТЬ СОБЫТИЕ",
            [UIBtnManageAuthors] = "УПРАВЛЕНИЕ АВТОРАМИ",
            [UIBtnAddNewAuthor] = "ДОБАВИТЬ АВТОРА",
            [UIBtnCreateSchedule] = "СОЗДАТЬ РАСПИСАНИЕ",
            [UIBtnCreateTimer] = "СОЗДАТЬ ТАЙМЕР",
            [UIBtnPrev] = "<",
            [UIBtnNext] = ">",
            [UIBtnEdit] = "✎",
            [UIPlaceholderSearch] = "Поиск события...",
            [UIPlaceholderSearchEvent] = "ПОИСК ПО НАЗВАНИЮ СОБЫТИЯ...",
            [UIRandomScheduleTitle] = "СЛУЧАЙНОЕ РАСПИСАНИЕ",
            [UIRandomScheduleDesc] =
                "Позволяет создать пул событий, которые будут запускаться случайным образом. \nРаботает независимо и одновременно с вашими основными расписаниями.",
            [UIFacepunchEventTitle] = "СОБЫТИЯ FACEPUNCH",
            [UIFacepunchEventDesc] =
                "Официальные ванильные события от Facepunch\nНажмите, чтобы настроить расписание.\nСобытия без своих настроек запускаются по расписанию по умолчанию.",
            [UICustomEventListTitle] = "СПИСОК СОБЫТИЙ",
            [UICustomEventListDesc] =
                "Все события, найденные при сканировании сервера. Если недавно добавленного плагина нет в списке, нажмите «СКАНИРОВАТЬ». \nЕсли события всё ещё нет, добавьте его вручную через «ДОБАВИТЬ».",
            [UIShowAllEventBase] = "ПОКАЗАТЬ ВСЕ",
            [UIBtnManageRandomPool] = "УПРАВЛЕНИЕ ПУЛОМ",
            [UIBtnAddNew] = "ДОБАВИТЬ",
            [UIBtnRescan] = "СКАНИРОВАТЬ",
            [UIBtnBack] = "НАЗАД",
            [UIRandomPoolEditor] = "РЕДАКТОР СЛУЧАЙНОГО ПУЛА",
            [UIRandomPoolFooterDesc] =
                "Управляйте списком событий и условиями их появления для системы случайной ротации.",
            [UIRandomPoolFooterSupport] = "Поддержка и предложения: <color=#68C2FF>discord.gg/mevent</color>",
            [UINoTimersYet] = "ТАЙМЕРОВ ПОКА НЕТ",
            [UIUseSchedule] = "ИСПОЛЬЗОВАТЬ РАСПИСАНИЕ",
            [UIOn] = "ВКЛ",
            [UIOff] = "ВЫКЛ",
            [UIBtnStartEvent] = "ЗАПУСТИТЬ СОБЫТИЕ",
            [UIBtnStopEvent] = "ОСТАНОВИТЬ СОБЫТИЕ",
            [UIEditEventEmptyHint] =
                "Создайте первый таймер для управления событием.\n\nРекомендуется отключить 'Auto-start' в настройках события.",
            [UIEditEventTitle] = "РЕДАКТИРОВАНИЕ СОБЫТИЯ: {0}",
            [UIEditEventSimple] = "РЕДАКТИРОВАНИЕ СОБЫТИЯ",
            [UICreateNewEvent] = "СОЗДАНИЕ СОБЫТИЯ",
            [UIEventByAuthor] = "от {0}",
            [UIDefaultSchedule] = "РАСПИСАНИЕ ПО УМОЛЧАНИЮ",
            [UIPlaceholderDisplayName] = "напр. Моё событие",
            [UIPlaceholderCommand] = "напр. operation.start",
            [UIPlaceholderPlugin] = "напр. OperationFortress",
            [UIPlaceholderCreatorName] = "Введите имя автора...",
            [UIPlaceholderAuthor] = "напр. Mevent",
            [UIAuthor] = "АВТОР",
            [UIStartConsoleCommand] = "ЗАПУСК В КОНСОЛИ",
            [UIEveryXMinutes] = "Каждые {0} минут",
            [UIEveryRangeMinutes] = "Каждые {0} - {1} минут",
            [UIEveryday] = "Ежедневно",
            [UITypeStatic] = "СТАТИЧНЫЙ",
            [UITypeEvery] = "КАЖДЫЕ",
            [UITypeRandom] = "СЛУЧАЙНЫЙ",
            [UIDayMonday] = "Понедельник",
            [UIDayTuesday] = "Вторник",
            [UIDayWednesday] = "Среда",
            [UIDayThursday] = "Четверг",
            [UIDayFriday] = "Пятница",
            [UIDaySaturday] = "Суббота",
            [UIDaySunday] = "Воскресенье",
            [MsgEnterDisplayName] = "Пожалуйста, введите отображаемое имя для события.",
            [MsgEnterCommand] = "Пожалуйста, введите консольную команду для события.",
            [MsgEnterPluginName] = "Пожалуйста, введите имя плагина для события.",
            [MsgChooseAuthor] = "Пожалуйста, выберите автора события.",
            [MsgEventAlreadyActive] = "{0} уже запущено. Дождитесь окончания, прежде чем запускать снова.",
            [UIViewModeList] = "СПИСОК",
            [UIViewModeColumns] = "КОЛОНКИ",
            [UIWipeBlockLabel] = "БЛОКИРОВКА ПОСЛЕ ВАЙПА (СЕК)",
            [UIWipeBlockPlaceholder] = "0 - отключено",
            [MsgWipeBlockWarn] = "Событие '{0}' заблокировано вайпом ещё на {1}. Запускаю принудительно."
        }, this, "ru");
    }

    private string Msg(BasePlayer player, string key, params object[] obj)
    {
        var msg = lang.GetMessage(key, this, player.UserIDString);
        return obj.Length > 0 ? string.Format(msg, obj) : msg;
    }

    private string Msg(string key, params object[] obj)
    {
        var msg = lang.GetMessage(key, this);
        return obj.Length > 0 ? string.Format(msg, obj) : msg;
    }

    private void SendNotify(BasePlayer player, string key, int type, params object[] obj)
    {
        if (_config.UseNotify && (Notify != null || UINotify != null))
            Interface.Oxide.CallHook("SendNotify", player, type, Msg(player, key, obj));
        else
            player.ChatMessage(Msg(player, key, obj));
    }

    private string GetTypeName(EventType type)
    {
        return Msg("UIType" + type);
    }

    private string GetDayName(DayOfWeek day)
    {
        return Msg("UIDay" + day);
    }

    private string GetDayName(BasePlayer player, DayOfWeek day)
    {
        return Msg(player, "UIDay" + day);
    }

    private string BuildDisplayDays(BasePlayer player, EventTime time)
    {
        var days = time.activeDays;
        if (days.Count == 0 || days.Count == 7)
            return Msg(player, UIEveryday);

        var sb = Pool.Get<StringBuilder>();
        try
        {
            var trimTo = days.Count switch {3 or 4 => 3, 5 or 6 => 1, _ => 0};
            foreach (var d in days)
            {
                var name = GetDayName(player, d);
                sb.Append(trimTo > 0 && name.Length > trimTo ? name.Substring(0, trimTo) : name).Append(", ");
            }

            if (sb.Length >= 2) sb.Length -= 2;
            return sb.ToString();
        }
        finally
        {
            Pool.FreeUnmanaged(ref sb);
        }
    }

    #endregion

    #region API

    private static void CallEventManagerStartedEvent(string eventName)
    {
        Interface.Oxide.CallHook("OnEventStarted", eventName);

        if (Instance._config.EnableEventLogging)
            Instance.LogToFile("events", $"[{DateTime.Now}] Event started: {eventName}", Instance);
    }

    private void API_AddEvent(string creatorName, string displayName, string command, string pluginName)
    {
        if (data.events.Find(e => e.displayName == displayName) != null) return;

        if (!data.creators.Contains(creatorName)) data.creators.Add(creatorName);

        data.events.Add(new Event
        {
            creator = creatorName,
            displayName = displayName,
            command = command,
            pluginName = pluginName
        });

        CheckIsEventsLoaded(displayName);
        RebuildEventCache();

        PrintWarning($"Event '{displayName}' added successfully under creator '{creatorName}'.");
    }

    public void UpdateEventList()
    {
        var list = Pool.Get<List<Dictionary<string, object>>>();
        try
        {
            foreach (var ev in data.eventTimes)
            {
                if (ev.type != EventType.Static) continue;

                var events = data.events.FindAll(e => ev.eventsId.Contains(e.eventId));
                if (events.IsNullOrEmpty()) continue;

                var description = $"description.{string.Join(", ", events.Select(e => e.displayName))}";

                list.Add(new Dictionary<string, object>
                {
                    ["Name"] = description,
                    ["Description"] = description,
                    ["Hour"] = ev.hour,
                    ["Minute"] = ev.minute,
                    ["Weeks"] = DaysEnumToArray(ev.activeDays)
                });
            }

            if (list.Count > 0) Interface.Oxide.CallHook("AddEventManagerSchedule", list);
        }
        finally
        {
            Pool.FreeUnmanaged(ref list);
        }
    }

    private bool[] DaysEnumToArray(HashSet<DayOfWeek> days)
    {
        var array = new bool[7];

        if (days.Count == 0)
            for (var i = 0; i < array.Length; i++)
                array[i] = true;
        else
            foreach (var day in days)
                array[(int) day] = true;

        return array;
    }

    private bool HandleTimeFieldInput(OpenedPlayerData adminState, string field, string rawValue, bool isRandom)
    {
        switch (field)
        {
            case "hour":
                if (!int.TryParse(rawValue, out var hour) || hour < 0 || hour > 24) return false;
                adminState.editTime.hour = hour;
                return true;

            case "minute":
                if (!int.TryParse(rawValue, out var min) || min < 0 || min > 60) return false;
                adminState.editTime.minute = min;
                return true;

            case "offsetmin":
                if (!int.TryParse(rawValue, out var offMin) || offMin < 0) return false;
                if (adminState.editTime.offsetMax > 0 && offMin > adminState.editTime.offsetMax)
                {
                    adminState.editTime.offsetMin = adminState.editTime.offsetMax;
                    return false;
                }

                adminState.editTime.offsetMin = offMin;
                return true;

            case "offsetmax":
                if (!int.TryParse(rawValue, out var offMax) || offMax < 0) return false;
                adminState.editTime.offsetMax = offMax;
                return true;

            case "interval":
                if (!int.TryParse(rawValue, out var intv) || intv < 0) return false;
                adminState.editTime.interval = intv;
                return true;

            case "minplayers":
                if (!int.TryParse(rawValue, out var minP) || minP < 0) return false;
                adminState.editTime.minPlayers = minP;
                return true;

            case "maxplayers":
                if (!int.TryParse(rawValue, out var maxP) || maxP < 0) return false;
                adminState.editTime.maxPlayers = maxP;
                return true;

            default:
                return true;
        }
    }

    #endregion

    #region Working with Images

    private string GetImage(string name)
    {
        if (_loadedImages.TryGetValue(name, out var imageID)) return imageID;

#if CARBON
        return imageDatabase?.GetImageString(name);
#else
        return Convert.ToString(ImageLibrary?.Call("GetImage", name));
#endif
    }

    private void AddImage(string url, string fileName, ulong imageId = 0)
    {
#if CARBON
        imageDatabase?.Queue(true, new Dictionary<string, string> { [fileName] = url });
#else
        ImageLibrary?.Call("AddImage", url, fileName, imageId);
#endif
    }

    private void LoadImages()
    {
#if CARBON
        if (imageDatabase == null)
            imageDatabase = BaseModule.GetModule<ImageDatabaseModule>();
#endif
        var imagesList = new Dictionary<string, string>();

        TryLoadImage("EM_IconList", "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/EventManager/IconList.png?raw=true");
        TryLoadImage("EM_IconColumns", "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/EventManager/IconColumns.png?raw=true");

        foreach (var (name, url) in imagesList.ToArray())
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) continue;

            if (url.StartsWith("TheMevent/"))
            {
                imagesList.Remove(name);
                LoadImageFromFS(name, url);
            }
        }

        if (imagesList.Count == 0) return;

#if CARBON
        imageDatabase.Queue(false, imagesList);
#else
        timer.In(1f, () =>
        {
            if (ImageLibrary is not { IsLoaded: true })
            {
                PrintError("IMAGE LIBRARY IS NOT INSTALLED.");
                return;
            }

            ImageLibrary?.Call("ImportImageList", Title, imagesList, 0UL, true);
        });
#endif
        void TryLoadImage(string name, string image)
        {
            if (string.IsNullOrEmpty(image) || string.IsNullOrEmpty(name)) return;
            imagesList.TryAdd(name, image);
        }
    }

    private void LoadImageFromFS(string name, string path)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return;
        Global.Runner.StartCoroutine(LoadImageCoroutine(name, path));
    }

    private IEnumerator LoadImageCoroutine(string name, string path)
    {
        var url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + path;
        using var www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Instance?.PrintError($"Image not found at '{path}'. Verify that the file exists.");
        }
        else
        {
            var texture = DownloadHandlerTexture.GetContent(www);
            try
            {
                var image = texture.EncodeToPNG();
                _loadedImages.TryAdd(name,
                    FileStorage.server.Store(image, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID)
                        .ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }

    #endregion
}
