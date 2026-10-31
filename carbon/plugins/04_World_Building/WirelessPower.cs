using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WirelessPower", "RickySpanish", "1.2.9")]
    [Description("Automatic wireless power distribution - no wires needed")]
    public class WirelessPower : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin RaidableBases;
        [PluginReference] private Plugin Notify;

        private Dictionary<NetworkableId, TCPowerGrid> _tcGrids = new();
        private Dictionary<NetworkableId, ElectricSwitch> _turretSwitches = new();
        private Dictionary<NetworkableId, float> _sprinklerLastDrain = new();
        private Dictionary<NetworkableId, ulong> _sprinklerWaterSource = new();
        private HashSet<ulong> _playersWithUI = new();
        private HashSet<ulong> _playersWithHUD = new();
        private HashSet<ulong> _hiddenHUD = new();
        private HashSet<ulong> _disabledGrids = new();
        private Dictionary<ulong, UIPage> _playerUIPage = new();
        private Dictionary<ulong, int> _playerUIPageNum = new();
        private Dictionary<ulong, DeviceCategory> _playerViewingCategory = new();
        private Dictionary<Vector3, float> _raidableBaseLocations = new();
        private Dictionary<string, string> _displayNameCache = new();
        private HashSet<string> _blacklistedPrefabs = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _whitelistedPrefabs = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _alwaysExcludedPrefabs = new(StringComparer.OrdinalIgnoreCase)
        {
            "elevator", "elevator.static", "elevator.static.office.top", "elevator.static.top",
            "button", "pressbutton", "pressbutton_officeelevatorcall", "pressbutton_trainstairwell",
            "pressbutton_compact", "pressbutton_invisible", "pressbutton_officeelevatordown",
            "pressbutton_officeelevatorup", "doorcontroller.deployed", "doormanipulator",
            "doormanipulator.invisible", "cardreader"
        };
        
        private Dictionary<ulong, Dictionary<string, float>> _notificationCooldowns = new();
        
        private Dictionary<ulong, bool> _previousLowPowerState = new();
        private Dictionary<ulong, bool> _previousPowerLossState = new();
        private Dictionary<ulong, HashSet<ulong>> _previousOnlineTurrets = new();
        
        private Timer _uiRefreshTimer;
        private Timer _permissionRebuildTimer;
        private readonly HashSet<ulong> _pendingPermissionOwners = new HashSet<ulong>();
        private const string PermUse = "wirelesspower.use";
        private bool _firstRun = true;

        private const string SwitchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private static readonly Vector3 TurretSwitchPosition = new(0f, 0.36f, 0.32f);
        
        private const string WaterShortname = "water";
        private const string SaltWaterShortname = "water.salt";
        
        #region Pre-computed UI Strings
        
        private static readonly string[] _powerStrings = new string[501];
        private static readonly Dictionary<int, string> _largePowerStrings = new();
        
        private static readonly string[,] _countStrings = new string[51, 51];
        
        private static readonly string[,] _pageStrings = new string[21, 21];
        
        private static readonly RowAnchors[] _rowAnchors = new RowAnchors[10];
        
        private class RowAnchors
        {
            public readonly string PanelMin;
            public readonly string PanelMax;
            public readonly string SourceNameMin;
            public readonly string SourceNameMax;
            public readonly string SourceValueMin;
            public readonly string SourceValueMax;
            public readonly string CategoryNameMin;
            public readonly string CategoryNameMax;
            public readonly string CategoryWattsMin;
            public readonly string CategoryWattsMax;
            public readonly string CategoryCountMin;
            public readonly string CategoryCountMax;
            public readonly string CategoryToggleBtnMin;
            public readonly string CategoryToggleBtnMax;
            public readonly string CategoryViewBtnMin;
            public readonly string CategoryViewBtnMax;
            public readonly string DeviceNameMin;
            public readonly string DeviceNameMax;
            public readonly string DevicePowerMin;
            public readonly string DevicePowerMax;
            public readonly string DeviceStatusMin;
            public readonly string DeviceStatusMax;
            public readonly string DeviceBtnMin;
            public readonly string DeviceBtnMax;
            public readonly string DeviceRenameBtnMin;
            public readonly string DeviceRenameBtnMax;

            public RowAnchors(int row)
            {
                const float startY = 0.84f;
                const float rowHeight = 0.07f;
                
                float yMax = startY - (row * rowHeight);
                float yMin = yMax - rowHeight + 0.01f;
                float yMinBtn = yMin + 0.01f;
                float yMaxBtn = yMax - 0.01f;

                PanelMin = $"0.03 {yMin}";
                PanelMax = $"0.97 {yMax}";
                
                SourceNameMin = $"0.05 {yMin}";
                SourceNameMax = $"0.75 {yMax}";
                SourceValueMin = $"0.75 {yMin}";
                SourceValueMax = $"0.95 {yMax}";
                
                CategoryNameMin = $"0.05 {yMin}";
                CategoryNameMax = $"0.4 {yMax}";
                CategoryWattsMin = $"0.4 {yMin}";
                CategoryWattsMax = $"0.5 {yMax}";
                CategoryCountMin = $"0.5 {yMin}";
                CategoryCountMax = $"0.62 {yMax}";
                CategoryToggleBtnMin = $"0.62 {yMinBtn}";
                CategoryToggleBtnMax = $"0.78 {yMaxBtn}";
                CategoryViewBtnMin = $"0.8 {yMinBtn}";
                CategoryViewBtnMax = $"0.95 {yMaxBtn}";
                
                DeviceNameMin = $"0.05 {yMin}";
                DeviceNameMax = $"0.45 {yMax}";
                DevicePowerMin = $"0.45 {yMin}";
                DevicePowerMax = $"0.55 {yMax}";
                DeviceStatusMin = $"0.55 {yMin}";
                DeviceStatusMax = $"0.7 {yMax}";
                DeviceBtnMin = $"0.7 {yMinBtn}";
                DeviceBtnMax = $"0.82 {yMaxBtn}";
                DeviceRenameBtnMin = $"0.83 {yMinBtn}";
                DeviceRenameBtnMax = $"0.95 {yMaxBtn}";
            }
        }
        
        private static void DepowerFlags(IOEntity entity)
        {
            if (entity == null) return;
            using (var scope = entity.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
            {
                scope.Set(IOEntity.Flag_HasPower, false);
                scope.Set(BaseEntity.Flags.On, false);
            }
        }

        private static void SetSprinklerRunning(Sprinkler sprinkler, bool running)
        {
            if (sprinkler == null) return;
            sprinkler.RefreshSprinklerState(running ? sprinkler.ConsumptionAmount() : 0);
        }

        private static void InitializeUIStringCache()
        {
            for (int i = 0; i <= 500; i++)
                _powerStrings[i] = i + "W";
            
            for (int powered = 0; powered <= 50; powered++)
                for (int total = 0; total <= 50; total++)
                    _countStrings[powered, total] = powered + "/" + total;
            
            for (int current = 1; current <= 20; current++)
                for (int total = 1; total <= 20; total++)
                    _pageStrings[current, total] = "Page " + current + " / " + total;
            
            for (int i = 0; i < 10; i++)
                _rowAnchors[i] = new RowAnchors(i);
            
            for (int i = 0; i < 29; i++)
            {
                _categoryToggleCommands[i] = "power.togglecategory " + i;
                _categoryViewCommands[i] = "power.viewcategory " + i;
            }
        }
        
        private static readonly string[] _categoryToggleCommands = new string[29];
        private static readonly string[] _categoryViewCommands = new string[29];
        
        private static string GetPowerString(int watts)
        {
            if (watts >= 0 && watts <= 500)
                return _powerStrings[watts];
            
            if (!_largePowerStrings.TryGetValue(watts, out var str))
            {
                str = watts + "W";
                _largePowerStrings[watts] = str;
            }
            return str;
        }
        
        private static string GetCountString(int powered, int total)
        {
            if (powered >= 0 && powered <= 50 && total >= 0 && total <= 50)
                return _countStrings[powered, total];
            return powered + "/" + total;
        }
        
        private static string GetPageString(int currentPage, int totalPages)
        {
            int display = currentPage + 1;
            if (display >= 1 && display <= 20 && totalPages >= 1 && totalPages <= 20)
                return _pageStrings[display, totalPages];
            return "Page " + display + " / " + totalPages;
        }
        
        #endregion

        private enum UIPage { Main, Sources, Consumers, ConsumerDetail }

        private enum DeviceCategory
        {
            Other = 0,
            Turrets,
            Searchlights,
            Defense,
            Lights,
            Sprinklers,
            Splitters,
            Cameras,
            Switches,
            Timers,
            RF,
            Phones,
            Heaters,
            FogMachines,
            SnowMachines,
            Water,
            Igniters,
            Sensors,
            Counters,
            Logic,
            Elevators,
            CarLifts,
            Industrial,
            Alarms,
            Audio,
            Vending,
            Fridges,
            NeonSigns
        }

        private static readonly string[] CategoryNames = new string[]
        {
            "Other",
            "Turrets",
            "Searchlights",
            "Defense",
            "Lights",
            "Sprinklers",
            "Splitters",
            "Cameras",
            "Switches",
            "Timers",
            "RF",
            "Phones",
            "Heaters",
            "Fog Machines",
            "Snow Machines",
            "Water",
            "Igniters",
            "Sensors",
            "Counters",
            "Logic",
            "Elevators",
            "Car Lifts",
            "Industrial",
            "Alarms",
            "Audio",
            "Vending",
            "Fridges",
            "Neon Signs"
        };

        private static readonly Dictionary<string, DeviceCategory> PrefabCategories = new() {
            { "ceilinglight.deployed", DeviceCategory.Lights },
            { "electric.flasherlight.deployed", DeviceCategory.Lights },
            { "electric.fluorescentlight.ceiling.deployed", DeviceCategory.Lights },
            { "electric.fluorescentlight.deployed", DeviceCategory.Lights },
            { "electric.sirenlight.deployed", DeviceCategory.Lights },
            { "industrial.wall.lamp.deployed", DeviceCategory.Lights },
            { "industrial.wall.lamp.green.deployed", DeviceCategory.Lights },
            { "industrial.wall.lamp.red.deployed", DeviceCategory.Lights },
            { "industrial.wall.lamp.blue.deployed", DeviceCategory.Lights },
            { "laserlight.deployed", DeviceCategory.Lights },
            { "simplelight", DeviceCategory.Lights },
            { "weaponracklight", DeviceCategory.Lights },
            { "weaponracklightdouble", DeviceCategory.Lights },
            { "sirenlightorange", DeviceCategory.Lights },
            { "sirenlightblue", DeviceCategory.Lights },
            { "sirenlightgreen", DeviceCategory.Lights },
            { "electric.bulbstringlights.deployed", DeviceCategory.Lights },
            { "electric.chandelier.deployed", DeviceCategory.Lights },
            { "electric.fairylights.deployed", DeviceCategory.Lights },
            { "electric.spotlight.deployed", DeviceCategory.Lights },
            { "electric.spotlight.tripod.deployed", DeviceCategory.Lights },
            { "electric.tablelamp.deployed", DeviceCategory.Lights },
            { "strobelight", DeviceCategory.Lights },
            { "xmas.advanced.lights.deployed", DeviceCategory.Lights },
            { "lightupframe.ioent.large", DeviceCategory.Lights },
            { "lightupframe.ioent.medium", DeviceCategory.Lights },
            { "lightupframe.ioent.small", DeviceCategory.Lights },
            { "lightupframe.ioent.standing", DeviceCategory.Lights },
            { "lightupframe.ioent.xl", DeviceCategory.Lights },
            { "lightupframe.ioent.xxl", DeviceCategory.Lights },
            { "lightupmirror.large", DeviceCategory.Lights },
            { "lightupmirror.medium", DeviceCategory.Lights },
            { "lightupmirror.small", DeviceCategory.Lights },
            { "lightupmirror.standing", DeviceCategory.Lights },
            
            { "searchlight.deployed", DeviceCategory.Searchlights },
            
            { "autoturret_deployed", DeviceCategory.Turrets },
            
            { "sam_site_turret_deployed", DeviceCategory.Defense },
            { "teslacoil.deployed", DeviceCategory.Defense },
            { "reactivetarget_deployed", DeviceCategory.Defense },
            
            { "doorcontroller.deployed", DeviceCategory.Switches },
            { "doormanipulator", DeviceCategory.Switches },
            { "doormanipulator.invisible", DeviceCategory.Switches },
            { "button", DeviceCategory.Switches },
            { "pressbutton", DeviceCategory.Switches },
            { "pressbutton_officeelevatorcall", DeviceCategory.Switches },
            { "pressbutton_trainstairwell", DeviceCategory.Switches },
            { "pressbutton_compact", DeviceCategory.Switches },
            { "pressbutton_invisible", DeviceCategory.Switches },
            { "pressbutton_officeelevatordown", DeviceCategory.Switches },
            { "pressbutton_officeelevatorup", DeviceCategory.Switches },
            { "simpleswitch", DeviceCategory.Switches },
            { "simpleswitch_lightswitch", DeviceCategory.Switches },
            { "smartswitch", DeviceCategory.Switches },
            { "switch", DeviceCategory.Switches },
            { "wheelswitch", DeviceCategory.Switches },
            { "wheelswitch_wheel_only", DeviceCategory.Switches },
            { "caboose_lightswitch", DeviceCategory.Switches },
            { "cardreader", DeviceCategory.Switches },
            
            { "hbhfsensor.deployed", DeviceCategory.Sensors },
            { "laserdetector", DeviceCategory.Sensors },
            { "pressurepad.deployed", DeviceCategory.Sensors },
            { "electric.seismicsensor.deployed", DeviceCategory.Sensors },
            
            { "andswitch", DeviceCategory.Logic },
            { "andswitch.entity", DeviceCategory.Logic },
            { "orswitch", DeviceCategory.Logic },
            { "orswitch.entity", DeviceCategory.Logic },
            { "xorswitch", DeviceCategory.Logic },
            { "xorswitch.entity", DeviceCategory.Logic },
            { "caboose_xorswitch", DeviceCategory.Logic },
            { "electrical.memorycell.deployed", DeviceCategory.Logic },
            { "electrical.random.switch.deployed", DeviceCategory.Logic },
            { "electrical.blocker.deployed", DeviceCategory.Logic },
            { "command.block.deployed", DeviceCategory.Logic },
            
            { "timer", DeviceCategory.Timers },
            { "timerswitch", DeviceCategory.Timers },
            
            { "counter", DeviceCategory.Counters },
            
            { "cctv.static", DeviceCategory.Cameras },
            { "cctv_deployed", DeviceCategory.Cameras },
            { "ptz_cctv_deployed", DeviceCategory.Cameras },
            { "ptz_cctv.static", DeviceCategory.Cameras },
            
            { "rfbroadcaster", DeviceCategory.RF },
            { "rfbroadcaster.static", DeviceCategory.RF },
            { "rfreceiver", DeviceCategory.RF },
            
            { "telephone.deployed", DeviceCategory.Phones },
            
            { "electrical.heater", DeviceCategory.Heaters },
            
            { "fogmachine", DeviceCategory.FogMachines },
            
            { "snowmachine", DeviceCategory.SnowMachines },
            
            { "igniter.deployed", DeviceCategory.Igniters },
            
            { "boombox.deployed", DeviceCategory.Audio },
            { "connectedspeaker.deployed", DeviceCategory.Audio },
            { "connectedspeaker.deployed.static", DeviceCategory.Audio },
            { "microphonestandio.entity.static", DeviceCategory.Audio },
            { "microphonestandio.entity", DeviceCategory.Audio },
            { "discoball.deployed", DeviceCategory.Audio },
            { "discofloor.deployed", DeviceCategory.Audio },
            { "discofloor.largetiles.deployed", DeviceCategory.Audio },
            { "soundlight.deployed", DeviceCategory.Audio },
            { "spookyspeaker", DeviceCategory.Audio },
            
            { "alarmsytstem", DeviceCategory.Alarms },
            { "audioalarm", DeviceCategory.Alarms },
            { "alarmsound", DeviceCategory.Alarms },
            { "smartalarm", DeviceCategory.Alarms },
            
            { "elevator", DeviceCategory.Elevators },
            { "elevator.static", DeviceCategory.Elevators },
            { "elevator.static.office.top", DeviceCategory.Elevators },
            { "elevator.static.top", DeviceCategory.Elevators },
            
            { "electrical.modularcarlift.deployed", DeviceCategory.CarLifts },
            
            { "storageadaptor.deployed", DeviceCategory.Industrial },
            { "storagemonitor.deployed", DeviceCategory.Industrial },
            { "industrialconveyor.deployed", DeviceCategory.Industrial },
            { "industrialcrafter.deployed", DeviceCategory.Industrial },
            { "hopper.deployed", DeviceCategory.Industrial },
            { "electricfurnace.io", DeviceCategory.Industrial },
            
            { "vendingmachine.deployed", DeviceCategory.Vending },
            
            { "fridge.deployed", DeviceCategory.Fridges },
            { "minifridge.deployed", DeviceCategory.Fridges },
            { "electric.wallcabinet.deployed", DeviceCategory.Fridges },
            
            { "sign.neon.125x125", DeviceCategory.NeonSigns },
            { "sign.neon.125x215", DeviceCategory.NeonSigns },
            { "sign.neon.125x215.animated", DeviceCategory.NeonSigns },
            { "sign.neon.xl", DeviceCategory.NeonSigns },
            { "sign.neon.xl.animated", DeviceCategory.NeonSigns },
            { "neonsigntr.deployed", DeviceCategory.NeonSigns },
            
            { "electrical.branch.deployed", DeviceCategory.Splitters },
            { "electrical.combiner.deployed", DeviceCategory.Splitters },
            { "splitter", DeviceCategory.Splitters },
            { "cabletunnel", DeviceCategory.Splitters },
            
            { "sprinkler", DeviceCategory.Sprinklers },
            { "electric.sprinkler", DeviceCategory.Sprinklers },
            { "electric.sprinkler.deployed", DeviceCategory.Sprinklers },
            { "sprinkler.deployed", DeviceCategory.Sprinklers },
            
            { "waterpump", DeviceCategory.Water },
            { "water.pump.deployed", DeviceCategory.Water },
            { "waterpump.deployed", DeviceCategory.Water },
            { "poweredwaterpurifier.deployed", DeviceCategory.Water },
            { "powered.water.purifier.deployed", DeviceCategory.Water },
            { "fluidswitch", DeviceCategory.Water },
            { "fluid.switch.deployed", DeviceCategory.Water },
            
            { "electric.digitalclock.deployed", DeviceCategory.Other }
        };

        #endregion

        #region Classes

        private class CachedSource
        {
            public IOEntity Entity;
            public bool IsBattery;
            public bool IsFuelGen;
            public bool IsGenerator;
            public string DisplayName;
        }

        private class CachedConsumer
        {
            public IOEntity Entity;
            public ulong NetIdValue;
            public bool IsTurret;
            public bool IsBranch;
            public bool IsPassthrough;
            public bool IsSprinkler;
            public int Priority;
            public int PowerRequired;
            public DeviceCategory Category;
            public string DisplayName;
            public string PowerString;
            public string ToggleCommand;
        }

        private class TCPowerGrid
        {
            public int TotalCapacity { get; set; }
            public int TotalDemand { get; set; }
            public int TotalUsed { get; set; }
            public List<CachedSource> Sources { get; set; } = new();
            public List<CachedConsumer> Consumers { get; set; } = new();
            public HashSet<IOEntity> ActiveConsumers { get; set; } = new();
            public HashSet<IOEntity> PreviousActiveConsumers { get; set; } = new();
            public HashSet<ulong> ForcedOff { get; set; } = new();
            public List<LiquidContainer> WaterContainers { get; set; } = new();
            public PlayerBoat Boat { get; set; }
            public bool IsBoatGrid { get; set; }
        }

        private static readonly Comparison<CachedConsumer> ConsumerPriorityComparison = (a, b) => a.Priority.CompareTo(b.Priority);
        private static readonly Comparison<CachedConsumer> ConsumerDisplayNameComparison = (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
        private static readonly Comparison<(string name, string value, string color, string extra)> TupleNameComparison = 
            (a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal);
        private static readonly Comparison<DeviceCategory> CategoryNameComparison = 
            (a, b) => string.Compare(CategoryNames[(int)a], CategoryNames[(int)b], StringComparison.Ordinal);

        #endregion

        #region Configuration

        private Configuration _config;

        public class Configuration
        {
            [JsonProperty(PropertyName = "Default Turret State (true = armed)")]
            public bool DefaultTurretState;

            [JsonProperty(PropertyName = "Enable Turret Switches (physical switch on turrets)")]
            public bool EnableTurretSwitches;

            [JsonProperty(PropertyName = "Power Update Interval (seconds)")]
            public float UpdateInterval;

            [JsonProperty(PropertyName = "Require TC Auth For Switches (PvE)")]
            public bool RequireTCAuthForSwitches;

            [JsonProperty(PropertyName = "Enable Wireless Sprinklers")]
            public bool EnableWirelessSprinklers;

            [JsonProperty(PropertyName = "Sprinkler Drain Amount (ml per tick)")]
            public int SprinklerDrainAmount;

            [JsonProperty(PropertyName = "Sprinkler Drain Interval (seconds)")]
            public float SprinklerDrainInterval;

            [JsonProperty(PropertyName = "Max TC Range (meters)")]
            public float MaxTCRange;

            [JsonProperty(PropertyName = "Power Reserve Percentage (0-100, keep this % in reserve)")]
            public int PowerReservePercent;

            [JsonProperty(PropertyName = "Use Notify Plugin (if installed)")]
            public bool UseNotifyPlugin;

            [JsonProperty(PropertyName = "Notify Type (from Notify plugin config)")]
            public int NotifyType;

            [JsonProperty(PropertyName = "Notification Cooldown (seconds, prevents spam)")]
            public float NotificationCooldown;

            [JsonProperty(PropertyName = "Low Power Warning Threshold (percentage)")]
            public int LowPowerWarningThreshold;

            [JsonProperty(PropertyName = "Enable Low Power Warning")]
            public bool EnableLowPowerWarning;

            [JsonProperty(PropertyName = "Enable Power Loss Notification")]
            public bool EnablePowerLossNotification;

            [JsonProperty(PropertyName = "Enable Turret Offline Notification")]
            public bool EnableTurretOfflineNotification;

            [JsonProperty(PropertyName = "Enable HUD Button (lightning bolt icon near TC)")]
            public bool EnableHUDButton;

            [JsonProperty(PropertyName = "Blacklisted Prefabs (will not be managed)")]
            public List<string> BlacklistedPrefabs;

            [JsonProperty(PropertyName = "Whitelisted Prefabs (if not empty, ONLY these get power)")]
            public List<string> WhitelistedPrefabs;

            [JsonProperty(PropertyName = "Consumer Priority (lower = higher priority)")]
            public Dictionary<string, int> ConsumerPriority;

            public static Configuration DefaultConfig()
            {
                return new Configuration
                {
                    DefaultTurretState = true,
                    EnableTurretSwitches = true,
                    UpdateInterval = 1f,
                    RequireTCAuthForSwitches = true,
                    EnableWirelessSprinklers = true,
                    SprinklerDrainAmount = 2,
                    SprinklerDrainInterval = 5f,
                    MaxTCRange = 60f,
                    PowerReservePercent = 0,
                    UseNotifyPlugin = true,
                    NotifyType = 0,
                    NotificationCooldown = 60f,
                    LowPowerWarningThreshold = 20,
                    EnableLowPowerWarning = true,
                    EnablePowerLossNotification = true,
                    EnableTurretOfflineNotification = true,
                    EnableHUDButton = true,
                    BlacklistedPrefabs = new List<string>(),
                    WhitelistedPrefabs = new List<string>(),
                    ConsumerPriority = new Dictionary<string, int>()
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) LoadDefaultConfig();
                
                bool changed = false;
                if (_config.ConsumerPriority == null)
                {
                    _config.ConsumerPriority = new Dictionary<string, int>();
                    changed = true;
                }
                if (_config.BlacklistedPrefabs == null)
                {
                    _config.BlacklistedPrefabs = new List<string>();
                    changed = true;
                }
                if (_config.WhitelistedPrefabs == null)
                {
                    _config.WhitelistedPrefabs = new List<string>();
                    changed = true;
                }
                if (_config.MaxTCRange <= 0)
                {
                    _config.MaxTCRange = 60f;
                    changed = true;
                }
                if (_config.NotificationCooldown <= 0)
                {
                    _config.NotificationCooldown = 60f;
                    changed = true;
                }
                
                var rawConfig = Config.ReadObject<Dictionary<string, object>>();
                if (!rawConfig.ContainsKey("Enable Turret Switches (physical switch on turrets)"))
                {
                    _config.EnableTurretSwitches = true;
                    changed = true;
                }
                
                if (changed)
                    SaveConfig();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                PrintWarning("Creating new configuration file.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = Configuration.DefaultConfig();
        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Data

        private const string DataFolder = "WirelessPower";

        private class StoredData
        {
            public Dictionary<ulong, ulong> TurretSwitches { get; set; } = new();
            public Dictionary<ulong, List<ulong>> ForcedOff { get; set; } = new();
            public Dictionary<ulong, string> CustomNames { get; set; } = new();
            public HashSet<ulong> DisabledGrids { get; set; } = new();
            public HashSet<ulong> HiddenHUD { get; set; } = new();
        }

        private StoredData _data;

        private void LoadData()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>($"{DataFolder}/data") ?? new StoredData();
            
            if (_data.TurretSwitches == null) _data.TurretSwitches = new Dictionary<ulong, ulong>();
            if (_data.ForcedOff == null) _data.ForcedOff = new Dictionary<ulong, List<ulong>>();
            if (_data.CustomNames == null) _data.CustomNames = new Dictionary<ulong, string>();
            if (_data.DisabledGrids == null) _data.DisabledGrids = new HashSet<ulong>();
            if (_data.HiddenHUD == null) _data.HiddenHUD = new HashSet<ulong>();

            _disabledGrids = new HashSet<ulong>(_data.DisabledGrids);
            _hiddenHUD = new HashSet<ulong>(_data.HiddenHUD);
        }

        private void SaveData()
        {
            _data.TurretSwitches.Clear();
            foreach (var kvp in _turretSwitches)
            {
                if (kvp.Value != null && !kvp.Value.IsDestroyed)
                    _data.TurretSwitches[kvp.Key.Value] = kvp.Value.net.ID.Value;
            }

            _tempRemoveList.Clear();
            foreach (var key in _data.ForcedOff.Keys)
                _tempRemoveList.Add(key);

            foreach (var kvp in _tcGrids)
            {
                var tcKey = kvp.Key.Value;
                if (kvp.Value.ForcedOff.Count > 0)
                {
                    if (!_data.ForcedOff.TryGetValue(tcKey, out var existingList))
                    {
                        existingList = new List<ulong>();
                        _data.ForcedOff[tcKey] = existingList;
                    }
                    else
                    {
                        existingList.Clear();
                    }
                    foreach (var id in kvp.Value.ForcedOff)
                        existingList.Add(id);
                    _tempRemoveList.Remove(tcKey);
                }
            }

            foreach (var staleKey in _tempRemoveList)
                _data.ForcedOff.Remove(staleKey);
            
            _data.DisabledGrids = new HashSet<ulong>(_disabledGrids);
            _data.HiddenHUD = new HashSet<ulong>(_hiddenHUD);

            Interface.Oxide.DataFileSystem.WriteObject($"{DataFolder}/data", _data);
        }

        private static readonly Dictionary<string, string> CustomDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "generator.small", "Test Generator" },
            { "small_fuel_generator", "Small Fuel Generator" }
        };

        private string GetEntityDisplayName(BaseEntity entity)
        {
            if (entity == null) return "Unknown";
            
            var shortName = entity.ShortPrefabName;
            
            if (_displayNameCache.TryGetValue(shortName, out var cachedName))
                return cachedName;
            
            string displayName = null;
            
            if (CustomDisplayNames.TryGetValue(shortName, out var customName))
            {
                displayName = customName;
            }
            else
            {
                var combatEntity = entity as BaseCombatEntity;
                if (combatEntity != null && combatEntity.pickup.itemTarget != null)
                {
                    displayName = combatEntity.pickup.itemTarget.displayName.english;
                }
                else
                {
                    var itemDef = ItemManager.FindItemDefinition(shortName);
                    if (itemDef != null)
                    {
                        displayName = itemDef.displayName.english;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(displayName))
                displayName = GenerateDisplayName(shortName);
            
            _displayNameCache[shortName] = displayName;
            return displayName;
        }

        private static readonly StringBuilder _displayNameBuilder = new(64);
        
        private string GenerateDisplayName(string shortName)
        {
            _displayNameBuilder.Clear();
            
            bool capitalizeNext = true;
            bool lastWasSpace = false;
            int length = shortName.Length;
            
            for (int i = 0; i < length; i++)
            {
                char c = shortName[i];
                
                if (c == '.' || c == '_' || c == '-')
                {
                    int remaining = length - i;
                    if ((remaining == 9 && MatchesSuffix(shortName, i, ".deployed")) ||
                        (remaining == 7 && MatchesSuffix(shortName, i, ".entity")) ||
                        (remaining == 9 && MatchesSuffix(shortName, i, "_deployed")) ||
                        (remaining == 7 && MatchesSuffix(shortName, i, ".prefab")))
                        break;
                    
                    if (!lastWasSpace && _displayNameBuilder.Length > 0)
                    {
                        _displayNameBuilder.Append(' ');
                        lastWasSpace = true;
                    }
                    capitalizeNext = true;
                    continue;
                }
                
                if (capitalizeNext && char.IsLetter(c))
                {
                    _displayNameBuilder.Append(char.ToUpper(c));
                    capitalizeNext = false;
                }
                else
                {
                    _displayNameBuilder.Append(char.ToLower(c));
                }
                lastWasSpace = false;
            }
            
            return _displayNameBuilder.ToString().Trim();
        }

        private static bool MatchesSuffix(string str, int startIndex, string suffix)
        {
            if (startIndex + suffix.Length > str.Length) return false;
            for (int i = 0; i < suffix.Length; i++)
            {
                if (char.ToLowerInvariant(str[startIndex + i]) != char.ToLowerInvariant(suffix[i]))
                    return false;
            }
            return true;
        }

        #endregion

        #region Hooks

        private void Init()
        {
            InitializeUIStringCache();
            permission.RegisterPermission(PermUse, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (_config.BlacklistedPrefabs != null)
            {
                foreach (var prefab in _config.BlacklistedPrefabs)
                {
                    if (!string.IsNullOrEmpty(prefab))
                        _blacklistedPrefabs.Add(prefab.Trim());
                }
            }
            if (_config.WhitelistedPrefabs != null)
            {
                foreach (var prefab in _config.WhitelistedPrefabs)
                {
                    if (!string.IsNullOrEmpty(prefab))
                        _whitelistedPrefabs.Add(prefab.Trim());
                }
            }
            timer.Once(1f, () => InitializeGrids());
        }

        private void OnNewSave(string filename)
        {
            _data.TurretSwitches.Clear();
            _data.ForcedOff.Clear();
            _data.CustomNames.Clear();
            SaveData();
            Puts("Server wipe detected - cleared stored data.");
        }

        private int DepowerGrid(TCPowerGrid grid)
        {
            if (grid == null) return 0;

            int depowered = 0;

            foreach (var cached in grid.Consumers)
            {
                if (cached.Entity == null || cached.Entity.IsDestroyed) continue;

                if (cached.IsSprinkler)
                {
                    var sprinkler = cached.Entity as Sprinkler;
                    SetSprinklerRunning(sprinkler, false);
                }
                else
                {
                    DepowerFlags(cached.Entity);

                    if (cached.IsTurret)
                    {
                        var turret = (AutoTurret)cached.Entity;
                        if (turret.IsOnline())
                            turret.InitiateShutdown();
                    }
                }

                depowered++;
            }

            return depowered;
        }

        private void OnUserPermissionGranted(string id, string permName)
        {
            if (permName == PermUse) QueuePermissionCheck(id);
        }

        private void OnUserPermissionRevoked(string id, string permName)
        {
            if (permName == PermUse) QueuePermissionCheck(id);
        }

        private void OnUserGroupAdded(string id, string groupName)
        {
            if (permission.GroupHasPermission(groupName, PermUse)) QueuePermissionCheck(id);
        }

        private void OnUserGroupRemoved(string id, string groupName)
        {
            if (permission.GroupHasPermission(groupName, PermUse)) QueuePermissionCheck(id);
        }

        private void OnGroupPermissionGranted(string groupName, string permName)
        {
            if (permName == PermUse) QueueGroupCheck(groupName);
        }

        private void OnGroupPermissionRevoked(string groupName, string permName)
        {
            if (permName == PermUse) QueueGroupCheck(groupName);
        }

        private void QueueGroupCheck(string groupName)
        {
            foreach (var entry in permission.GetUsersInGroup(groupName))
            {
                var space = entry.IndexOf(' ');
                QueuePermissionCheck(space > 0 ? entry.Substring(0, space) : entry);
            }
        }

        private void QueuePermissionCheck(string id)
        {
            ulong ownerId;
            if (!ulong.TryParse(id, out ownerId) || ownerId == 0) return;

            _pendingPermissionOwners.Add(ownerId);

            _permissionRebuildTimer?.Destroy();
            _permissionRebuildTimer = timer.Once(1f, ProcessPermissionChanges);
        }

        private void ProcessPermissionChanges()
        {
            _permissionRebuildTimer = null;

            if (_pendingPermissionOwners.Count == 0) return;

            var granted = new HashSet<ulong>();
            var revoked = new HashSet<ulong>();

            foreach (var ownerId in _pendingPermissionOwners)
            {
                if (permission.UserHasPermission(ownerId.ToString(), PermUse))
                    granted.Add(ownerId);
                else
                    revoked.Add(ownerId);
            }

            _pendingPermissionOwners.Clear();

            if (revoked.Count > 0) TeardownOwners(revoked);
            if (granted.Count > 0) BuildForOwners(granted);
        }

        private void TeardownOwners(HashSet<ulong> owners)
        {
            _tempNetIdRemoveList.Clear();
            int depowered = 0;

            foreach (var kvp in _tcGrids)
            {
                var entity = BaseNetworkable.serverEntities.Find(kvp.Key) as BaseEntity;
                if (entity == null || !owners.Contains(entity.OwnerID)) continue;

                depowered += DepowerGrid(kvp.Value);
                _tempNetIdRemoveList.Add(kvp.Key);
            }

            foreach (var id in _tempNetIdRemoveList)
                _tcGrids.Remove(id);

            _tempNetIdRemoveList.Clear();

            int switchesRemoved = 0;

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var turret = entity as AutoTurret;
                if (turret == null || turret.IsDestroyed) continue;
                if (!owners.Contains(turret.OwnerID)) continue;

                var sw = turret.GetComponentInChildren<ElectricSwitch>();
                if (sw != null && !sw.IsDestroyed)
                {
                    sw.SetParent(null);
                    sw.Kill();
                    switchesRemoved++;
                }

                _turretSwitches.Remove(turret.net.ID);
            }

            if (depowered > 0 || switchesRemoved > 0)
                Puts($"Permission revoked: depowered {depowered} devices, removed {switchesRemoved} turret switches and dropped their grids.");
        }

        private void BuildForOwners(HashSet<ulong> owners)
        {
            var tcs = new List<BuildingPrivlidge>();
            var boats = new List<PlayerBoat>();
            var turretsNeedingSwitches = new List<AutoTurret>();

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BuildingPrivlidge tc)
                {
                    if (owners.Contains(tc.OwnerID) && !IsInRaidableBase(tc))
                        tcs.Add(tc);
                }
                else if (entity is PlayerBoat boat)
                {
                    if (owners.Contains(boat.OwnerID))
                        boats.Add(boat);
                }
                else if (entity is AutoTurret turret)
                {
                    if (!_config.EnableTurretSwitches) continue;
                    if (!owners.Contains(turret.OwnerID)) continue;
                    if (IsInRaidableBase(turret)) continue;

                    if (!_turretSwitches.TryGetValue(turret.net.ID, out var sw) || sw == null || sw.IsDestroyed)
                    {
                        var existingSwitch = turret.GetComponentInChildren<ElectricSwitch>();
                        if (existingSwitch != null)
                            _turretSwitches[turret.net.ID] = existingSwitch;
                        else
                            turretsNeedingSwitches.Add(turret);
                    }
                }
            }

            foreach (var tc in tcs)
            {
                if (tc != null && !tc.IsDestroyed)
                    BuildTCGrid(tc);
            }

            foreach (var boat in boats)
            {
                if (boat != null && !boat.IsDestroyed)
                    BuildBoatGrid(boat);
            }

            if (turretsNeedingSwitches.Count > 0)
                SpawnSwitchesSlow(turretsNeedingSwitches, 0);

            if (tcs.Count > 0 || boats.Count > 0)
                Puts($"Permission granted: built {tcs.Count} grids and {boats.Count} boat grids.");
        }

        private void InitializeGrids()
        {
            foreach (var kvp in _data.TurretSwitches)
            {
                var turretId = new NetworkableId(kvp.Key);
                var switchId = new NetworkableId(kvp.Value);
                var sw = BaseNetworkable.serverEntities.Find(switchId) as ElectricSwitch;
                if (sw != null && !sw.IsDestroyed)
                    _turretSwitches[turretId] = sw;
            }

            var tcs = new List<BuildingPrivlidge>();
            var turrets = new List<AutoTurret>();
            var boats = new List<PlayerBoat>();

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BuildingPrivlidge tc && !IsInRaidableBase(tc))
                {
                    if (tc.OwnerID != 0 && permission.UserHasPermission(tc.OwnerID.ToString(), PermUse))
                        tcs.Add(tc);
                }
                else if (entity is AutoTurret turret && turret.OwnerID != 0)
                    turrets.Add(turret);
                else if (entity is PlayerBoat boat && boat.OwnerID != 0)
                {
                    if (permission.UserHasPermission(boat.OwnerID.ToString(), PermUse))
                        boats.Add(boat);
                }
            }

            var turretsNeedingSwitches = new List<AutoTurret>();
            int switchesRemoved = 0;
            int tcIndex = 0;
            int turretIndex = 0;
            const int batchSize = 5;
            int lastProgress = 0;

            Action processBatch = null;
            processBatch = () =>
            {
                for (int i = 0; i < batchSize && tcIndex < tcs.Count; i++, tcIndex++)
                {
                    var tc = tcs[tcIndex];
                    if (tc != null && !tc.IsDestroyed)
                        BuildTCGrid(tc);
                }

                for (int i = 0; i < batchSize && turretIndex < turrets.Count; i++, turretIndex++)
                {
                    var turret = turrets[turretIndex];
                    if (turret == null || turret.IsDestroyed) continue;
                    if (!permission.UserHasPermission(turret.OwnerID.ToString(), PermUse)) continue;
                    if (IsInRaidableBase(turret)) continue;

                    if (_config.EnableTurretSwitches)
                    {
                        if (!_turretSwitches.TryGetValue(turret.net.ID, out var sw) || sw == null || sw.IsDestroyed)
                        {
                            var existingSwitch = turret.GetComponentInChildren<ElectricSwitch>();
                            if (existingSwitch != null)
                                _turretSwitches[turret.net.ID] = existingSwitch;
                            else
                                turretsNeedingSwitches.Add(turret);
                        }
                    }
                    else
                    {
                        var existingSwitch = turret.GetComponentInChildren<ElectricSwitch>();
                        if (existingSwitch != null && !existingSwitch.IsDestroyed)
                        {
                            existingSwitch.SetParent(null);
                            existingSwitch.Kill();
                            switchesRemoved++;
                        }
                        _turretSwitches.Remove(turret.net.ID);
                    }
                }

                if (tcIndex < tcs.Count || turretIndex < turrets.Count)
                {
                    int total = tcs.Count + turrets.Count;
                    int current = tcIndex + turretIndex;
                    int progress = total > 0 ? (current * 100) / total : 100;
                    if (progress >= lastProgress + 25)
                    {
                        lastProgress = (progress / 25) * 25;
                        Puts($"Initialization progress: {lastProgress}%");
                    }
                    timer.Once(0.1f, processBatch);
                }
                else
                {
                    foreach (var kvp in _data.ForcedOff)
                    {
                        var tcId = new NetworkableId(kvp.Key);
                        if (_tcGrids.TryGetValue(tcId, out var grid))
                        {
                            foreach (var entityId in kvp.Value)
                                grid.ForcedOff.Add(entityId);
                        }
                    }

                    foreach (var boat in boats)
                        BuildBoatGrid(boat);

                    timer.Every(_config.UpdateInterval, ProcessAllGrids);
                    timer.Every(30f, CleanupDeadEntities);
                    _uiRefreshTimer = timer.Every(5f, RefreshSourcesUI);
                    timer.Every(2f, UpdateHUDButtons);

                    PopulateConsumerPriorities();
                    
                    ProcessAllGrids();

                    int excluded = tcs.Count - _tcGrids.Count;
                    Puts($"Initialized {_tcGrids.Count} power grids" +
                         (excluded > 0 ? $" (excluded {excluded} in raid zones)" : ""));
                    
                    if (switchesRemoved > 0)
                        Puts($"Removed {switchesRemoved} turret switches (turret switches disabled in config)");
                    
                    if (turretsNeedingSwitches.Count > 0)
                    {
                        Puts($"Spawning {turretsNeedingSwitches.Count} turret switches...");
                        SpawnSwitchesSlow(turretsNeedingSwitches, 0);
                    }
                }
            };

            processBatch();
        }
        
        private void SpawnSwitchesSlow(List<AutoTurret> turrets, int index)
        {
            const int batchSize = 3;
            
            for (int i = 0; i < batchSize && index < turrets.Count; i++, index++)
            {
                var turret = turrets[index];
                if (turret != null && !turret.IsDestroyed)
                    AddSwitchToTurret(turret);
            }
            
            if (index < turrets.Count)
            {
                if (index % 50 == 0)
                    Puts($"Turret switches: {index}/{turrets.Count}");
                timer.Once(0.5f, () => SpawnSwitchesSlow(turrets, index));
            }
            else
            {
                Puts($"Turret switch spawning complete.");
            }
        }


        private void PopulateConsumerPriorities()
        {
            var discoveredPrefabs = new HashSet<string>();
            
            foreach (var prefabPath in GameManifest.Current.entities)
            {
                if (string.IsNullOrEmpty(prefabPath)) continue;
                
                var prefab = GameManager.server.FindPrefab(prefabPath);
                if (prefab == null) continue;
                
                var ioEntity = prefab.GetComponent<IOEntity>();
                if (ioEntity == null) continue;
                
                if (ioEntity is SolarPanel || ioEntity is ElectricWindmill || 
                    ioEntity is FuelGenerator || ioEntity is ElectricBattery || 
                    ioEntity is ElectricGenerator)
                    continue;
                
                if (IsFluidOnlyPrefab(ioEntity)) continue;
                
                if (!HasElectricInput(ioEntity)) continue;
                
                var shortName = ioEntity.ShortPrefabName;
                if (!string.IsNullOrEmpty(shortName) && !_alwaysExcludedPrefabs.Contains(shortName))
                    discoveredPrefabs.Add(shortName);
            }
            
            var sortedPrefabs = new List<string>(discoveredPrefabs);
            sortedPrefabs.Sort(StringComparer.Ordinal);
            
            int added = 0;
            foreach (var prefab in sortedPrefabs)
            {
                if (!_config.ConsumerPriority.ContainsKey(prefab))
                {
                    _config.ConsumerPriority[prefab] = 100;
                    added++;
                }
            }
            
            if (added > 0)
            {
                SaveConfig();
                Puts($"Added {added} new consumer prefabs to config. Edit priorities as desired.");
            }
        }

        private bool HasElectricInput(IOEntity ioEntity)
        {
            if (ioEntity == null) return false;
            
            if (ioEntity is SimpleLight || ioEntity is SearchLight || ioEntity is FlasherLight || 
                ioEntity is SirenLight || ioEntity is CeilingLight)
                return true;
            
            if (ioEntity.inputs == null || ioEntity.inputs.Length == 0) return false;
            
            foreach (var input in ioEntity.inputs)
            {
                if (input.type == IOEntity.IOType.Electric)
                    return true;
            }
            
            return false;
        }

        private bool IsSwimmingPool(BaseEntity entity)
        {
            if (entity == null) return false;
            var name = entity.ShortPrefabName;
            return name.Contains("paddlingpool") || name.Contains("abovegroundpool");
        }

        private bool IsFluidOnlyPrefab(IOEntity ioEntity)
        {
            if (ioEntity == null) return false;

            if (ioEntity is LiquidContainer) return true;

            return false;
        }

        private void ProcessAllGrids()
        {
            foreach (var kvp in _tcGrids)
            {
                ProcessPowerGrid(kvp.Key, kvp.Value);
            }
            _firstRun = false;
        }

        private List<ulong> _tempRemoveList = new();
        private List<NetworkableId> _tempNetIdRemoveList = new();
        private HashSet<ulong> _tempValidNetIds = new();
        private HashSet<IOEntity> _tempIOEntityRemoveSet = new();
        private HashSet<ulong> _tempUlongRemoveSet = new();
        
        private List<(string name, string value, string color, string extra)> _uiDisplayItems = new();
        private List<(string name, string value, string color, string extra)> _uiTempItems = new();
        private Dictionary<DeviceCategory, List<CachedConsumer>> _uiGroupedConsumers = new();
        private List<DeviceCategory> _uiCategoryList = new();
        private List<CachedConsumer> _uiDeviceList = new();
        
        private void RefreshSourcesUI()
        {
            if (_playersWithUI.Count == 0) return;
            
            _tempRemoveList.Clear();
            foreach (var playerId in _playersWithUI)
            {
                var player = BasePlayer.FindByID(playerId);
                if (player == null || !player.IsConnected)
                {
                    _tempRemoveList.Add(playerId);
                    continue;
                }
                
                if (!_playerUIPage.TryGetValue(playerId, out var page))
                    continue;
                
                if (page == UIPage.Sources)
                    ShowSourcesPage(player);
            }
            foreach (var id in _tempRemoveList)
                _playersWithUI.Remove(id);
        }

        private void CleanupDeadEntities()
        {
            _tempNetIdRemoveList.Clear();
            foreach (var kvp in _tcGrids)
            {
                if (kvp.Value.IsBoatGrid)
                {
                    var boat = BaseNetworkable.serverEntities.Find(kvp.Key) as PlayerBoat;
                    if (boat == null || boat.IsDestroyed)
                        _tempNetIdRemoveList.Add(kvp.Key);
                    continue;
                }

                var tc = BaseNetworkable.serverEntities.Find(kvp.Key) as BuildingPrivlidge;
                if (tc == null || tc.IsDestroyed || IsInRaidableBase(tc))
                    _tempNetIdRemoveList.Add(kvp.Key);
            }
            foreach (var id in _tempNetIdRemoveList)
                _tcGrids.Remove(id);
            
            foreach (var grid in _tcGrids.Values)
            {
                for (int i = grid.Sources.Count - 1; i >= 0; i--)
                {
                    var s = grid.Sources[i];
                    if (s.Entity == null || s.Entity.IsDestroyed)
                        grid.Sources.RemoveAt(i);
                }
                
                for (int i = grid.Consumers.Count - 1; i >= 0; i--)
                {
                    var c = grid.Consumers[i];
                    if (c.Entity == null || c.Entity.IsDestroyed)
                        grid.Consumers.RemoveAt(i);
                }
                
                _tempIOEntityRemoveSet.Clear();
                foreach (var e in grid.ActiveConsumers)
                {
                    if (e == null || e.IsDestroyed)
                        _tempIOEntityRemoveSet.Add(e);
                }
                grid.ActiveConsumers.ExceptWith(_tempIOEntityRemoveSet);
                
                for (int i = grid.WaterContainers.Count - 1; i >= 0; i--)
                {
                    var c = grid.WaterContainers[i];
                    if (c == null || c.IsDestroyed)
                        grid.WaterContainers.RemoveAt(i);
                }
                
                _tempValidNetIds.Clear();
                foreach (var c in grid.Consumers)
                {
                    if (c.Entity != null)
                        _tempValidNetIds.Add(c.NetIdValue);
                }
                
                _tempUlongRemoveSet.Clear();
                foreach (var id in grid.ForcedOff)
                {
                    if (!_tempValidNetIds.Contains(id))
                        _tempUlongRemoveSet.Add(id);
                }
                grid.ForcedOff.ExceptWith(_tempUlongRemoveSet);
            }
            
            _tempNetIdRemoveList.Clear();
            foreach (var kvp in _turretSwitches)
            {
                if (kvp.Value == null || kvp.Value.IsDestroyed)
                    _tempNetIdRemoveList.Add(kvp.Key);
            }
            foreach (var id in _tempNetIdRemoveList)
                _turretSwitches.Remove(id);
            
            _tempValidNetIds.Clear();
            foreach (var grid in _tcGrids.Values)
            {
                foreach (var c in grid.Consumers)
                {
                    if (c.IsSprinkler && c.Entity != null)
                        _tempValidNetIds.Add(c.NetIdValue);
                }
            }
            
            _tempNetIdRemoveList.Clear();
            foreach (var kvp in _sprinklerLastDrain)
            {
                if (!_tempValidNetIds.Contains(kvp.Key.Value))
                    _tempNetIdRemoveList.Add(kvp.Key);
            }
            foreach (var id in _tempNetIdRemoveList)
                _sprinklerLastDrain.Remove(id);
            foreach (var id in _tempNetIdRemoveList)
                _sprinklerWaterSource.Remove(id);
        }

        private void Unload()
        {
            _uiRefreshTimer?.Destroy();
            _permissionRebuildTimer?.Destroy();
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
                CuiHelper.DestroyUi(player, PowerHUD);
            }
            
            int depoweredCount = 0;
            foreach (var kvp in _tcGrids)
                depoweredCount += DepowerGrid(kvp.Value);

            if (depoweredCount > 0)
                Puts($"Depowered {depoweredCount} devices.");
            
            SaveData();
            
            _tcGrids.Clear();
            _turretSwitches.Clear();
            _playersWithUI.Clear();
            _sprinklerLastDrain.Clear();
            _sprinklerWaterSource.Clear();
            _raidableBaseLocations.Clear();
            _notificationCooldowns.Clear();
            _previousLowPowerState.Clear();
            _previousPowerLossState.Clear();
            _previousOnlineTurrets.Clear();
            _displayNameCache.Clear();
            _blacklistedPrefabs.Clear();
            _whitelistedPrefabs.Clear();
            _playersWithHUD.Clear();
            _hiddenHUD.Clear();
        }

        private void SendNotification(BasePlayer player, string message)
        {
            if (player == null) return;
            
            if (_config.UseNotifyPlugin && Notify != null)
            {
                Notify.Call("SendNotify", player, _config.NotifyType, message);
            }
            else
            {
                SendReply(player, message);
            }
        }

        private void SendNotificationToTCOwners(BuildingPrivlidge tc, string message, string notifyType)
        {
            if (tc == null) return;
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!tc.IsAuthed(player)) continue;
                
                if (!CanSendNotification(player.userID, notifyType))
                    continue;
                
                SendNotification(player, message);
                SetNotificationCooldown(player.userID, notifyType);
            }
        }

        private bool CanSendNotification(ulong playerId, string notifyType)
        {
            if (!_notificationCooldowns.TryGetValue(playerId, out var cooldowns))
                return true;
            
            if (!cooldowns.TryGetValue(notifyType, out var lastTime))
                return true;
            
            return Time.realtimeSinceStartup - lastTime >= _config.NotificationCooldown;
        }

        private void SetNotificationCooldown(ulong playerId, string notifyType)
        {
            if (!_notificationCooldowns.TryGetValue(playerId, out var cooldowns))
            {
                cooldowns = new Dictionary<string, float>();
                _notificationCooldowns[playerId] = cooldowns;
            }
            
            cooldowns[notifyType] = Time.realtimeSinceStartup;
        }

        private void OnServerSave() => SaveData();

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            
            if (input.WasJustPressed(BUTTON.FIRE_SECONDARY))
            {
                var activeItem = player.GetActiveItem();
                if (activeItem != null && activeItem.info.shortname == "hammer")
                {
                    RaycastHit hammerHit;
                    if (Physics.Raycast(player.eyes.HeadRay(), out hammerHit, 5f))
                    {
                        var turret = hammerHit.GetEntity() as AutoTurret;
                        if (turret != null && _turretSwitches.ContainsKey(turret.net.ID))
                        {
                            var tc = turret.GetBuildingPrivilege();
                            if (tc != null && !tc.IsAuthed(player))
                            {
                                SendNotification(player, Lang("MustBeAuthedRemove", player.UserIDString));
                                return;
                            }
                            
                            var childSwitch = turret.GetComponentInChildren<ElectricSwitch>();
                            if (childSwitch != null && !childSwitch.IsDestroyed)
                            {
                                childSwitch.SetParent(null);
                                childSwitch.Kill();
                            }
                            
                            _turretSwitches.Remove(turret.net.ID);
                            
                            if (tc != null && _tcGrids.TryGetValue(tc.net.ID, out var grid))
                            {
                                grid.ForcedOff.Add(turret.net.ID.Value);
                                ProcessPowerGrid(tc.net.ID, grid);
                            }
                            
                            turret.SetFlagLocal(IOEntity.Flag_HasPower, false);
                            if (turret.IsOnline())
                                turret.InitiateShutdown();
                            
                            SendNotification(player, Lang("SwitchRemoved", player.UserIDString));
                        }
                    }
                }
            }
            
            if (input.WasJustPressed(BUTTON.USE))
            {
                RaycastHit useHit;
                if (Physics.Raycast(player.eyes.HeadRay(), out useHit, 3f))
                {
                    var entity = useHit.GetEntity();
                    if (entity != null)
                    {
                        var ioEntity = entity as IOEntity;
                        if (ioEntity == null)
                            ioEntity = entity.GetComponentInParent<IOEntity>();

                        if (ioEntity != null)
                        {
                            var category = GetEntityCategory(ioEntity);
                            if (category == DeviceCategory.Audio)
                            {
                                var tc = ioEntity.GetBuildingPrivilege();
                                if (tc != null && _tcGrids.TryGetValue(tc.net.ID, out var grid))
                                {
                                    var netIdValue = ioEntity.net.ID.Value;
                                    if (grid.ForcedOff.Contains(netIdValue))
                                    {
                                        grid.ForcedOff.Remove(netIdValue);
                                        
                                        if (HasAvailablePowerAtTC(tc.net.ID, GetConsumerPowerRequired(ioEntity)))
                                        {
                                            ioEntity.SetFlagLocal(IOEntity.Flag_HasPower, true);
                                            ioEntity.SendNetworkUpdateImmediate();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            _playersWithUI.Remove(player.userID);
            _playerUIPage.Remove(player.userID);
            _playerUIPageNum.Remove(player.userID);
            _playersWithHUD.Remove(player.userID);
        }

        private object OnEntityTakeDamage(ElectricSwitch sw, HitInfo info)
        {
            if (sw == null || sw.IsDestroyed) return null;
            
            var turret = sw.GetParentEntity() as AutoTurret;
            if (turret == null) return null;
            
            if (_turretSwitches.TryGetValue(turret.net.ID, out var tracked) && tracked == sw)
            {
                if (info != null)
                {
                    info.damageTypes.ScaleAll(0f);
                    info.DidGather = false;
                    info.DoHitEffects = false;
                }
                return true;
            }
            
            return null;
        }

        private void OnEntitySpawned(AutoTurret turret)
        {
            if (turret == null || turret.OwnerID == 0) return;
            if (!permission.UserHasPermission(turret.OwnerID.ToString(), PermUse)) return;

            timer.Once(1f, () =>
            {
                if (turret == null || turret.IsDestroyed) return;
                
                if (IsInRaidableBase(turret)) return;

                var tc = turret.GetBuildingPrivilege();
                if (tc == null)
                    tc = FindNearestTC(turret.transform.position, turret.OwnerID);
                if (tc != null)
                    BuildTCGrid(tc);
                
                if (_config.EnableTurretSwitches && !_turretSwitches.ContainsKey(turret.net.ID))
                {
                    AddSwitchToTurret(turret);
                }
            });
        }

        private void OnEntitySpawned(IOEntity entity)
        {
            if (entity == null || entity is AutoTurret) return;

            var spawnedOnBoat = PlayerBoat.GetParentPlayerBoat(entity);
            if (spawnedOnBoat != null)
            {
                timer.Once(1f, () =>
                {
                    if (spawnedOnBoat == null || spawnedOnBoat.IsDestroyed) return;
                    BuildBoatGrid(spawnedOnBoat);
                });
                return;
            }

            var parent = entity.GetParentEntity();
            if (parent is BaseVehicle || parent is BaseVehicleModule) return;
            if (parent != null && parent.GetParentEntity() is BaseVehicle) return;

            timer.Once(1f, () =>
            {
                if (entity == null || entity.IsDestroyed) return;

                var tc = entity.GetBuildingPrivilege();
                if (tc == null)
                    tc = FindNearestTC(entity.transform.position, entity.OwnerID);
                if (tc != null)
                    BuildTCGrid(tc);
            });
        }

        private void OnEntitySpawned(WeaponRack rack)
        {
            if (rack == null) return;

            timer.Once(1f, () =>
            {
                if (rack == null || rack.IsDestroyed) return;

                var tc = rack.GetBuildingPrivilege();
                if (tc == null)
                    tc = FindNearestTC(rack.transform.position, rack.OwnerID);
                if (tc != null)
                    BuildTCGrid(tc);
            });
        }

        private void OnEntityKill(PlayerBoat boat)
        {
            if (boat == null || boat.net == null) return;
            _tcGrids.Remove(boat.net.ID);
        }

        private void OnEntitySpawned(PlayerBoat boat)
        {
            if (boat == null || boat.OwnerID == 0) return;
            if (!permission.UserHasPermission(boat.OwnerID.ToString(), PermUse)) return;

            timer.Once(2f, () =>
            {
                if (boat == null || boat.IsDestroyed) return;
                BuildBoatGrid(boat);
            });
        }

        private void OnEntitySpawned(BuildingPrivlidge tc)
        {
            if (tc == null || tc.OwnerID == 0) return;
            if (!permission.UserHasPermission(tc.OwnerID.ToString(), PermUse)) return;

            timer.Once(2f, () =>
            {
                if (tc == null || tc.IsDestroyed) return;
                if (IsInRaidableBase(tc)) return;
                BuildTCGrid(tc);
            });
        }

        private void OnSwitchToggled(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (electricSwitch == null || player == null) return;

            var tc = electricSwitch.GetBuildingPrivilege();
            
            if (_config.RequireTCAuthForSwitches)
            {
                if (tc != null && !tc.IsAuthed(player))
                {
                    electricSwitch.SetFlagLocal(BaseEntity.Flags.On, !electricSwitch.IsOn());
                    electricSwitch.SendNetworkUpdateImmediate();
                    SendNotification(player, Lang("MustBeAuthedSwitch", player.UserIDString));
                    return;
                }
            }

            var turret = electricSwitch.GetParentEntity() as AutoTurret;
            if (turret != null)
            {
                if (!_turretSwitches.TryGetValue(turret.net.ID, out var sw)) return;
                if (sw != electricSwitch) return;

                var turretTC = turret.GetBuildingPrivilege();
                if (turretTC == null) return;
                
                if (!_tcGrids.TryGetValue(turretTC.net.ID, out var grid)) return;

                if (electricSwitch.IsOn())
                {
                    grid.ForcedOff.Remove(turret.net.ID.Value);
                    
                    if (HasAvailablePowerAtTC(turretTC.net.ID, GetConsumerPowerRequired(turret)))
                    {
                        turret.SetFlagLocal(IOEntity.Flag_HasPower, true);
                        turret.InitiateStartup();
                    }
                }
                else
                {
                    grid.ForcedOff.Add(turret.net.ID.Value);
                    
                    turret.SetFlagLocal(IOEntity.Flag_HasPower, false);
                    turret.InitiateShutdown();
                }
                return;
            }

            if (tc == null) return;
            if (!_tcGrids.TryGetValue(tc.net.ID, out var switchGrid)) return;

            var netIdValue = electricSwitch.net.ID.Value;

            if (electricSwitch.IsOn())
            {
                switchGrid.ForcedOff.Remove(netIdValue);
            }
            else
            {
                switchGrid.ForcedOff.Add(netIdValue);
            }
        }

        private void AddSwitchToTurret(AutoTurret turret)
        {
            if (IsInRaidableBase(turret)) return;

            var electricSwitch = GameManager.server.CreateEntity(SwitchPrefab, TurretSwitchPosition, Quaternion.identity) as ElectricSwitch;
            if (electricSwitch == null) return;

            electricSwitch.Spawn();
            electricSwitch.SetParent(turret);
            
            electricSwitch.pickup.enabled = false;
            electricSwitch.SetFlagLocal(BaseEntity.Flags.Reserved8, true);
            electricSwitch.SetFlagLocal(IOEntity.Flag_HasPower, true);
            electricSwitch.InitializeHealth(float.MaxValue, float.MaxValue);
            
            UnityEngine.Object.Destroy(electricSwitch.GetComponent<GroundWatch>());
            UnityEngine.Object.Destroy(electricSwitch.GetComponent<DestroyOnGroundMissing>());

            electricSwitch.SetSwitch(_config.DefaultTurretState);

            _turretSwitches[turret.net.ID] = electricSwitch;
        }

        private bool HasAvailablePowerAtTC(NetworkableId tcId, int powerNeeded)
        {
            if (!_tcGrids.TryGetValue(tcId, out var grid))
                return false;

            int availablePower = grid.TotalCapacity - grid.TotalUsed;
            return availablePower >= powerNeeded;
        }

        private bool IsTurretSwitchOn(AutoTurret turret)
        {
            if (!_turretSwitches.TryGetValue(turret.net.ID, out var sw))
                return true;

            if (sw == null || sw.IsDestroyed) return true;

            return sw.IsOn();
        }

        private bool ProcessWirelessSprinkler(Sprinkler sprinkler, TCPowerGrid grid, NetworkableId tcId)
        {
            if (sprinkler == null || !_config.EnableWirelessSprinklers) return false;

            for (int i = grid.WaterContainers.Count - 1; i >= 0; i--)
            {
                var c = grid.WaterContainers[i];
                if (c == null || c.IsDestroyed)
                    grid.WaterContainers.RemoveAt(i);
            }

            Item waterItem = null;
            ulong useSourceId = 0;
            var sprinklerId = sprinkler.net.ID;
            
            if (_sprinklerWaterSource.TryGetValue(sprinklerId, out ulong savedSourceId))
            {
                foreach (var container in grid.WaterContainers)
                {
                    if (container.net.ID.Value == savedSourceId && container.inventory != null)
                    {
                        foreach (var item in container.inventory.itemList)
                        {
                            if ((item.info.shortname == WaterShortname || item.info.shortname == SaltWaterShortname) && item.amount > 0)
                            {
                                waterItem = item;
                                useSourceId = savedSourceId;
                            }
                        }
                    }
                }
                
                if (waterItem == null)
                {
                    _sprinklerWaterSource.Remove(sprinklerId);
                }
            }
            
            if (waterItem == null)
            {
                foreach (var container in grid.WaterContainers)
                {
                    if (container.inventory == null) continue;

                    foreach (var item in container.inventory.itemList)
                    {
                        if ((item.info.shortname == WaterShortname || item.info.shortname == SaltWaterShortname) && item.amount > 0)
                        {
                            waterItem = item;
                            useSourceId = container.net.ID.Value;
                            break;
                        }
                    }
                    if (waterItem != null) break;
                }
            }

            if (waterItem == null || waterItem.amount <= 0)
            {
                _sprinklerWaterSource.Remove(sprinklerId);
                SetSprinklerRunning(sprinkler, false);
                return false;
            }
            
            _sprinklerWaterSource[sprinklerId] = useSourceId;

            float now = Time.realtimeSinceStartup;
            float lastDrain;
            if (!_sprinklerLastDrain.TryGetValue(sprinklerId, out lastDrain) || now - lastDrain >= _config.SprinklerDrainInterval)
            {
                _sprinklerLastDrain[sprinklerId] = now;
                
                int drainAmount = _config.SprinklerDrainAmount;
                if (waterItem.amount > drainAmount)
                {
                    waterItem.amount -= drainAmount;
                    waterItem.MarkDirty();
                }
                else
                {
                    var fuelInfo = waterItem.info;
                    waterItem.Remove();
                    _sprinklerWaterSource.Remove(sprinklerId);
                    sprinkler.SetFuelType(fuelInfo, null);
                    SetSprinklerRunning(sprinkler, true);
                    return true;
                }
            }

            sprinkler.SetFuelType(waterItem.info, null);
            SetSprinklerRunning(sprinkler, true);

            return true;
        }

        private void HandleRaidableBaseStarted(Vector3 location, float protectionRadius)
        {
            _raidableBaseLocations[location] = protectionRadius;
            
            _tempNetIdRemoveList.Clear();
            foreach (var kvp in _tcGrids)
            {
                if (kvp.Value.IsBoatGrid) continue;

                var tc = BaseNetworkable.serverEntities.Find(kvp.Key) as BuildingPrivlidge;
                if (tc != null && Vector3.Distance(tc.transform.position, location) <= protectionRadius)
                    _tempNetIdRemoveList.Add(kvp.Key);
            }
            foreach (var id in _tempNetIdRemoveList)
                _tcGrids.Remove(id);
        }

        private void OnRaidableBaseStarted(Vector3 location, int level, bool allowPvP, string id, 
            float loadTime, float unk1, float unk2, ulong ownerId, BasePlayer owner, 
            List<BasePlayer> raiders, List<BasePlayer> intruders, List<BaseEntity> entities,
            string baseName, DateTime spawnTime, DateTime despawnTime, float protectionRadius, int lootRemaining)
        {
            HandleRaidableBaseStarted(location, protectionRadius);
        }

        private void OnRaidableBaseStarted(object[] hookObjects)
        {
            if (hookObjects == null || hookObjects.Length < 16) return;
            
            if (hookObjects[0] is Vector3 location && hookObjects[15] is float protectionRadius)
                HandleRaidableBaseStarted(location, protectionRadius);
        }

        private void OnRaidableBaseEnded(object[] hookObjects)
        {
            if (hookObjects == null || hookObjects.Length < 1) return;
            if (hookObjects[0] is Vector3 location)
                _raidableBaseLocations.Remove(location);
        }

        private void OnRaidableBaseDespawn(object[] hookObjects)
        {
            if (hookObjects == null || hookObjects.Length < 1) return;
            if (hookObjects[0] is Vector3 location)
                _raidableBaseLocations.Remove(location);
        }

        #endregion

        #region Core Power System

        private bool IsInRaidableBase(Vector3 position)
        {
            foreach (var kvp in _raidableBaseLocations)
            {
                float radiusSqr = kvp.Value * kvp.Value;
                if ((position - kvp.Key).sqrMagnitude <= radiusSqr)
                    return true;
            }
            
            if (RaidableBases != null)
            {
                var result = RaidableBases.Call("EventTerritory", position, 0f);
                if (result is bool b && b)
                    return true;
            }
            
            return false;
        }

        private bool IsInRaidableBase(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return false;
            return IsInRaidableBase(entity.transform.position);
        }

        private BuildingPrivlidge FindNearestTC(Vector3 position, ulong ownerId)
        {
            BuildingPrivlidge nearest = null;
            float nearestDist = _config.MaxTCRange * _config.MaxTCRange;

            var nearbyEntities = Facepunch.Pool.Get<List<BuildingPrivlidge>>();
            Vis.Entities(position, _config.MaxTCRange, nearbyEntities);

            foreach (var tc in nearbyEntities)
            {
                if (tc == null || tc.IsDestroyed) continue;
                if (tc.OwnerID == 0) continue;
                if (!permission.UserHasPermission(tc.OwnerID.ToString(), PermUse)) continue;

                float dist = (tc.transform.position - position).sqrMagnitude;
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = tc;
                }
            }

            Facepunch.Pool.FreeUnmanaged(ref nearbyEntities);
            return nearest;
        }

        private void DepowerRemovedConsumers(TCPowerGrid existingGrid, TCPowerGrid grid)
        {
        if (existingGrid != null && existingGrid.Consumers != null)
        {
            var newConsumerIds = new HashSet<ulong>();
            foreach (var c in grid.Consumers)
                newConsumerIds.Add(c.NetIdValue);

            foreach (var cached in existingGrid.Consumers)
            {
                if (cached.Entity == null || cached.Entity.IsDestroyed) continue;
                if (newConsumerIds.Contains(cached.NetIdValue)) continue;

                if (cached.IsSprinkler)
                {
                    var sprinkler = cached.Entity as Sprinkler;
                    SetSprinklerRunning(sprinkler, false);
                }
                else
                {
                    cached.Entity.UpdateFromInput(0, 0);
                    DepowerFlags(cached.Entity);

                    if (cached.IsTurret)
                    {
                        var turret = (AutoTurret)cached.Entity;
                        if (turret.IsOnline())
                            turret.InitiateShutdown();
                    }
                }
            }
        }
        }

        private void AddIOEntityToGrid(TCPowerGrid grid, IOEntity ioEntity)
        {
            if (IsPowerSource(ioEntity))
            {
                grid.Sources.Add(new CachedSource
                {
                    Entity = ioEntity,
                    IsBattery = ioEntity is ElectricBattery,
                    IsFuelGen = ioEntity is FuelGenerator,
                    IsGenerator = ioEntity is ElectricGenerator || ioEntity is FuelGenerator,
                    DisplayName = GetEntityDisplayName(ioEntity)
                });
            }
            else if (IsPowerConsumer(ioEntity))
            {
                if (ioEntity is ElectricSwitch && ioEntity.GetParentEntity() is AutoTurret)
                    return;

                if (ioEntity is LiquidContainer lc && !IsSwimmingPool(lc))
                    grid.WaterContainers.Add(lc);

                bool isTurret = ioEntity is AutoTurret;
                bool isBranch = ioEntity is ElectricalBranch;
                bool isSprinkler = ioEntity is Sprinkler;
                bool hasOutputs = ioEntity.outputs != null && ioEntity.outputs.Length > 0;
                bool isPassthrough = !isTurret && hasOutputs;
                int powerRequired = isSprinkler ? 0 : GetConsumerPowerRequired(ioEntity);
                ulong netIdValue = ioEntity.net.ID.Value;

                grid.Consumers.Add(new CachedConsumer
                {
                    Entity = ioEntity,
                    NetIdValue = netIdValue,
                    IsTurret = isTurret,
                    IsBranch = isBranch,
                    IsPassthrough = isPassthrough,
                    IsSprinkler = isSprinkler,
                    Priority = _config.ConsumerPriority?.TryGetValue(ioEntity.ShortPrefabName, out int p) == true ? p : 100,
                    PowerRequired = powerRequired,
                    Category = GetEntityCategory(ioEntity),
                    DisplayName = _data.CustomNames.TryGetValue(netIdValue, out var customName) ? customName : GetEntityDisplayName(ioEntity),
                    PowerString = isSprinkler ? "FLUID" : GetPowerString(powerRequired),
                    ToggleCommand = isTurret ? "power.toggleturret " + netIdValue : "power.toggledevice " + netIdValue
                });
            }
            else if (ioEntity is LiquidContainer lc && !IsSwimmingPool(lc))
            {
                grid.WaterContainers.Add(lc);
            }
        }

        private void BuildBoatGrid(PlayerBoat boat)
        {
            if (boat == null || boat.IsDestroyed || boat.net == null) return;

            if (boat.OwnerID == 0 || !permission.UserHasPermission(boat.OwnerID.ToString(), PermUse))
            {
                _tcGrids.Remove(boat.net.ID);
                return;
            }

            if (_disabledGrids.Contains(boat.net.ID.Value))
            {
                if (_tcGrids.TryGetValue(boat.net.ID, out var disabledGrid))
                {
                    DepowerEntireGrid(disabledGrid);
                    _tcGrids.Remove(boat.net.ID);
                }
                return;
            }

            TCPowerGrid existingGrid;
            _tcGrids.TryGetValue(boat.net.ID, out existingGrid);

            var grid = new TCPowerGrid { Boat = boat, IsBoatGrid = true };
            if (existingGrid != null)
                grid.ForcedOff = existingGrid.ForcedOff;

            var deployables = boat.Deployables?.Cached;
            if (deployables != null)
            {
                for (int i = 0; i < deployables.Count; i++)
                {
                    var entity = deployables[i];
                    if (entity == null || entity.IsDestroyed || entity.net == null) continue;

                    var ioEntity = entity as IOEntity;
                    if (ioEntity == null || ioEntity is Signage) continue;
                    if (ioEntity is ElectricSwitch && ioEntity.GetParentEntity() is AutoTurret) continue;

                    AddIOEntityToGrid(grid, ioEntity);
                }
            }

            grid.Consumers.Sort(ConsumerPriorityComparison);

            DepowerRemovedConsumers(existingGrid, grid);

            _tcGrids[boat.net.ID] = grid;
        }

        private void BuildTCGrid(BuildingPrivlidge tc)
        {
            if (tc == null) return;

            if (tc.OwnerID == 0 || !permission.UserHasPermission(tc.OwnerID.ToString(), PermUse))
            {
                _tcGrids.Remove(tc.net.ID);
                return;
            }

            if (IsInRaidableBase(tc))
            {
                _tcGrids.Remove(tc.net.ID);
                return;
            }

            if (_disabledGrids.Contains(tc.net.ID.Value))
            {
                if (_tcGrids.TryGetValue(tc.net.ID, out var disabledGrid))
                {
                    DepowerEntireGrid(disabledGrid);
                    _tcGrids.Remove(tc.net.ID);
                }
                return;
            }

            HashSet<ulong> existingForcedOff = null;
            if (_tcGrids.TryGetValue(tc.net.ID, out var existingGrid))
            {
                existingForcedOff = existingGrid.ForcedOff;
            }

            var grid = new TCPowerGrid();
            
            if (existingForcedOff != null)
                grid.ForcedOff = existingForcedOff;

            var nearbyEntities = Facepunch.Pool.Get<List<BaseEntity>>();
            Vis.Entities(tc.transform.position, _config.MaxTCRange, nearbyEntities);

            var processedNetIds = new HashSet<ulong>();

            foreach (var entity in nearbyEntities)
            {
                if (entity == null || entity.IsDestroyed) continue;

                var ioEntity = entity as IOEntity;
                if (ioEntity != null)
                {
                    var parent = ioEntity.GetParentEntity();
                    if (parent is BaseVehicle || parent is BaseVehicleModule) continue;
                    if (parent != null && parent.GetParentEntity() is BaseVehicle) continue;

                    var buildingPriv = ioEntity.GetBuildingPrivilege();

                    if (buildingPriv == null && parent != null)
                        buildingPriv = parent.GetBuildingPrivilege();

                    if (buildingPriv != null && buildingPriv.net.ID != tc.net.ID) continue;
                    if (buildingPriv == null)
                    {
                        var nearestTC = FindNearestTC(ioEntity.transform.position, ioEntity.OwnerID);
                        if (nearestTC == null || nearestTC.net.ID != tc.net.ID) continue;
                    }

                    processedNetIds.Add(ioEntity.net.ID.Value);

                    AddIOEntityToGrid(grid, ioEntity);
                    continue;
                }

                var buildingPriv2 = entity.GetBuildingPrivilege();
                if (buildingPriv2 != null && buildingPriv2.net.ID != tc.net.ID) continue;
                if (buildingPriv2 == null)
                {
                    var nearestTC = FindNearestTC(entity.transform.position, entity.OwnerID);
                    if (nearestTC == null || nearestTC.net.ID != tc.net.ID) continue;
                }

                var childEntities = entity.GetComponentsInChildren<IOEntity>();
                if (childEntities == null || childEntities.Length == 0) continue;

                foreach (var childIO in childEntities)
                {
                    if (childIO == null || childIO.IsDestroyed) continue;
                    if (childIO.net == null) continue;
                    if (processedNetIds.Contains(childIO.net.ID.Value)) continue;

                    processedNetIds.Add(childIO.net.ID.Value);

                    if (!IsPowerConsumer(childIO)) continue;

                    bool hasOutputs = childIO.outputs != null && childIO.outputs.Length > 0;
                    int powerRequired = childIO.ConsumptionAmount() > 0 ? childIO.ConsumptionAmount() : 1;
                    ulong netIdValue = childIO.net.ID.Value;
                    var category = GetEntityCategory(childIO);
                    if (category == DeviceCategory.Other)
                        category = DeviceCategory.Lights;

                    grid.Consumers.Add(new CachedConsumer
                    {
                        Entity = childIO,
                        NetIdValue = netIdValue,
                        IsTurret = false,
                        IsBranch = false,
                        IsPassthrough = hasOutputs,
                        IsSprinkler = false,
                        Priority = _config.ConsumerPriority?.TryGetValue(childIO.ShortPrefabName, out int p) == true ? p : 100,
                        PowerRequired = powerRequired,
                        Category = category,
                        DisplayName = _data.CustomNames.TryGetValue(netIdValue, out var customName) ? customName : GetEntityDisplayName(childIO),
                        PowerString = GetPowerString(powerRequired),
                        ToggleCommand = "power.toggledevice " + netIdValue
                    });
                }
            }

            Facepunch.Pool.FreeUnmanaged(ref nearbyEntities);

            grid.Consumers.Sort(ConsumerPriorityComparison);

            DepowerRemovedConsumers(existingGrid, grid);

            _tcGrids[tc.net.ID] = grid;
        }

        private TCPowerGrid GetPlayerCurrentGrid(BasePlayer player)
        {
            if (player == null) return null;

            var tc = player.GetBuildingPrivilege();
            if (tc == null)
            {
                var boat = PlayerBoat.GetParentPlayerBoat(player);
                if (boat == null || boat.net == null) return null;
                _tcGrids.TryGetValue(boat.net.ID, out var boatGrid);
                return boatGrid;
            }

            _tcGrids.TryGetValue(tc.net.ID, out var grid);
            return grid;
        }

        private bool IsPowerSource(IOEntity entity)
        {
            if (entity == null) return false;
            
            if (entity is SolarPanel) return true;
            if (entity is ElectricWindmill) return true;
            if (entity is FuelGenerator) return true;
            if (entity is ElectricBattery) return true;
            if (entity is ElectricGenerator) return true;
            
            if (entity.outputs != null && entity.outputs.Length > 0 && 
                (entity.inputs == null || entity.inputs.Length == 0))
                return true;
            
            return false;
        }

        private bool HasWiredElectricInput(IOEntity entity)
        {
            if (entity == null) return false;
            if (entity.inputs == null || entity.inputs.Length == 0) return false;
            
            foreach (var input in entity.inputs)
            {
                if (input.type == IOEntity.IOType.Electric && input.connectedTo.Get() != null)
                    return true;
            }
            
            return false;
        }

        private bool IsPowerConsumer(IOEntity entity)
        {
            if (entity == null) return false;
            if (IsPowerSource(entity)) return false;

            if (_alwaysExcludedPrefabs.Contains(entity.ShortPrefabName))
                return false;

            if (_blacklistedPrefabs.Contains(entity.ShortPrefabName))
                return false;
            
            var shortName = entity.ShortPrefabName;
            if (shortName.Contains("fluidcombiner") || shortName.Contains("fluid.combiner"))
                return false;
            
            if (entity is LiquidContainer)
            {
                if (entity.inputs == null || entity.inputs.Length == 0)
                    return false;
                    
                bool hasElectricalInput = false;
                foreach (var input in entity.inputs)
                {
                    if (input.type == IOEntity.IOType.Electric)
                    {
                        hasElectricalInput = true;
                        break;
                    }
                }
                if (!hasElectricalInput)
                    return false;
            }
            
            if (entity is Sprinkler) return true;
            
            if (entity is SearchLight || entity is SimpleLight || entity is FlasherLight || 
                entity is SirenLight || entity is CeilingLight)
                return true;
            
            if (entity.inputs != null && entity.inputs.Length > 0)
            {
                int powerRequired = GetConsumerPowerRequired(entity);
                if (powerRequired > 0)
                    return true;
                    
                foreach (var input in entity.inputs)
                {
                    if (input.type == IOEntity.IOType.Electric)
                        return true;
                }
            }
            
            return false;
        }

        private int GetConsumerPowerRequired(IOEntity consumer)
        {
            if (consumer == null) return 0;
            
            if (consumer is ElectricalBranch branch)
                return branch.branchAmount;
            
            int consumption = consumer.ConsumptionAmount();
            if (consumption > 0)
                return consumption;
            
            if (consumer.inputs != null && consumer.inputs.Length > 0)
                return 1;
            
            if (consumer is SimpleLight || consumer is SearchLight || consumer is FlasherLight ||
                consumer is SirenLight || consumer is StrobeLight)
                return 1;
            
            return 0;
        }

        private DeviceCategory GetEntityCategory(IOEntity entity)
        {
            if (entity == null) return DeviceCategory.Other;
            
            if (PrefabCategories.TryGetValue(entity.ShortPrefabName, out var category))
                return category;
            
            return DeviceCategory.Other;
        }

        private int GetSourceCurrentOutput(CachedSource cached)
        {
            var source = cached.Entity;
            if (source == null) return 0;

            if (cached.IsBattery)
            {
                var battery = (ElectricBattery)source;
                return battery.rustWattSeconds > 5f ? battery.maxOutput : 0;
            }
            
            if (cached.IsFuelGen)
            {
                var fuelGen = (FuelGenerator)source;
                return fuelGen.IsOn() ? fuelGen.outputEnergy : 0;
            }

            if (cached.IsGenerator)
            {
                var generator = source as ElectricGenerator;
                int perOutput = generator != null ? (int)generator.electricAmount : 100;
                if (source.outputs != null && source.outputs.Length > 0)
                    return source.outputs.Length * perOutput;
                return perOutput;
            }

            if (source.currentEnergy > 0)
                return Mathf.RoundToInt(source.currentEnergy);

            return 0;
        }

        private void ProcessPowerGrid(NetworkableId tcId, TCPowerGrid grid)
        {
            var tc = BaseNetworkable.serverEntities.Find(tcId) as BuildingPrivlidge;
            
            int totalCapacity = 0;
            
            foreach (var cached in grid.Sources)
            {
                if (cached.Entity == null || cached.Entity.IsDestroyed) continue;
                int output = GetSourceCurrentOutput(cached);
                totalCapacity += output;
            }
            grid.TotalCapacity = totalCapacity;

            int reservePower = (totalCapacity * _config.PowerReservePercent) / 100;
            int availablePower = totalCapacity - reservePower;
            
            int totalDemand = 0;
            foreach (var cached in grid.Consumers)
            {
                if (cached.Entity == null || cached.Entity.IsDestroyed) continue;
                if (cached.IsSprinkler) continue;
                
                int powerNeeded;
                if (cached.IsBranch)
                    powerNeeded = ((ElectricalBranch)cached.Entity).branchAmount;
                else
                    powerNeeded = cached.PowerRequired;
                
                if (powerNeeded > 0)
                    totalDemand += powerNeeded;
            }
            
            int totalUsed = 0;
            
            var temp = grid.PreviousActiveConsumers;
            grid.PreviousActiveConsumers = grid.ActiveConsumers;
            grid.ActiveConsumers = temp;
            grid.ActiveConsumers.Clear();
            
            var tcIdValue = tcId.Value;
            var currentOnlineTurrets = new HashSet<ulong>();

            foreach (var cached in grid.Consumers)
            {
                var consumer = cached.Entity;
                if (consumer == null || consumer.IsDestroyed) continue;

                bool forcedOff = grid.ForcedOff.Contains(cached.NetIdValue);

                if (forcedOff)
                {
                    if (cached.IsSprinkler)
                    {
                        var spr = consumer as Sprinkler;
                        SetSprinklerRunning(spr, false);
                    }
                    else
                    {
                        DepowerConsumer(cached, true);
                        if (cached.IsPassthrough)
                        {
                            int passthroughPower = cached.IsBranch ? ((ElectricalBranch)consumer).branchAmount : cached.PowerRequired;
                            if (availablePower >= passthroughPower)
                            {
                                availablePower -= passthroughPower;
                                totalUsed += passthroughPower;
                                UpdatePassthrough(cached, passthroughPower);
                            }
                        }
                    }
                    continue;
                }

                if (HasWiredElectricInput(consumer))
                    continue;

                if (cached.IsTurret && !IsTurretSwitchOn((AutoTurret)consumer))
                {
                    DepowerConsumer(cached, true);
                    continue;
                }

                if (_blacklistedPrefabs.Count > 0 && _blacklistedPrefabs.Contains(consumer.ShortPrefabName))
                {
                    DepowerConsumer(cached, true);
                    continue;
                }

                if (_whitelistedPrefabs.Count > 0 && !_whitelistedPrefabs.Contains(consumer.ShortPrefabName))
                {
                    DepowerConsumer(cached, true);
                    continue;
                }

                if (cached.IsSprinkler)
                {
                    if (!_config.EnableWirelessSprinklers)
                    {
                        continue;
                    }
                    
                    var sprinkler = consumer as Sprinkler;
                    if (sprinkler != null && ProcessWirelessSprinkler(sprinkler, grid, tcId))
                    {
                        grid.ActiveConsumers.Add(consumer);
                    }
                    continue;
                }

                int powerNeeded;
                if (cached.IsBranch)
                    powerNeeded = ((ElectricalBranch)consumer).branchAmount;
                else
                    powerNeeded = cached.PowerRequired;

                bool wasActive = grid.PreviousActiveConsumers.Contains(consumer);
                bool canPower = availablePower >= powerNeeded;
                
                if (cached.Category == DeviceCategory.Audio && wasActive && consumer.HasFlag(IOEntity.Flag_HasPower) && !consumer.IsOn())
                {
                    grid.ForcedOff.Add(cached.NetIdValue);
                    continue;
                }

                if (cached.IsPassthrough)
                {
                    if (canPower)
                    {
                        grid.ActiveConsumers.Add(consumer);

                        UpdatePassthrough(cached, powerNeeded);

                        availablePower -= powerNeeded;
                        totalUsed += powerNeeded;

                        consumer.SetFlagLocal(IOEntity.Flag_HasPower, true);
                        consumer.SetFlagLocal(BaseEntity.Flags.On, true);
                    }
                    else
                    {
                        DepowerFlags(consumer);
                        UpdatePassthrough(cached, 0);
                    }
                }
                else if (canPower)
                {
                    availablePower -= powerNeeded;
                    totalUsed += powerNeeded;
                    grid.ActiveConsumers.Add(consumer);
                    
                    if (cached.IsTurret)
                    {
                        var turret = (AutoTurret)consumer;
                        currentOnlineTurrets.Add(cached.NetIdValue);
                        if (!turret.HasFlag(IOEntity.Flag_HasPower) || (!turret.IsOnline() && !turret.booting))
                        {
                            PowerConsumer(cached, availablePower);
                        }
                    }
                    else if (!wasActive || !consumer.HasFlag(IOEntity.Flag_HasPower))
                    {
                        PowerConsumer(cached, availablePower);
                    }
                }
                else
                {
                    if (wasActive || consumer.IsOn())
                        DepowerConsumer(cached);
                }
            }

            grid.TotalDemand = totalDemand;
            grid.TotalUsed = totalUsed;

            int nonBatteryPower = 0;
            foreach (var cached in grid.Sources)
            {
                if (cached.Entity == null || cached.Entity.IsDestroyed) continue;
                if (!cached.IsBattery)
                    nonBatteryPower += GetSourceCurrentOutput(cached);
            }

            if (totalUsed > 0)
            {
                int batteryDrainNeeded = totalUsed - nonBatteryPower;
                if (batteryDrainNeeded > 0)
                {
                    float drainWattSeconds = batteryDrainNeeded * _config.UpdateInterval;
                    foreach (var cached in grid.Sources)
                    {
                        if (drainWattSeconds <= 0f) break;
                        if (!cached.IsBattery || cached.Entity == null || cached.Entity.IsDestroyed) continue;

                        var battery = (ElectricBattery)cached.Entity;
                        if (battery.rustWattSeconds <= 0f) continue;

                        int batteryOutput = battery.rustWattSeconds > 5f ? battery.maxOutput : 0;
                        if (batteryOutput <= 0) continue;

                        float thisDrain = Mathf.Min(drainWattSeconds, batteryOutput * _config.UpdateInterval);
                        thisDrain = Mathf.Min(thisDrain, battery.rustWattSeconds);
                        battery.rustWattSeconds -= thisDrain;
                        drainWattSeconds -= thisDrain;

                        if (battery.rustWattSeconds < 0f)
                            battery.rustWattSeconds = 0f;

                        battery.SendNetworkUpdateImmediate();
                    }
                }
            }

            int surplusPower = nonBatteryPower - totalUsed;
            if (surplusPower > 0)
            {
                float chargeWattSeconds = surplusPower * _config.UpdateInterval;
                foreach (var cached in grid.Sources)
                {
                    if (chargeWattSeconds <= 0f) break;
                    if (!cached.IsBattery || cached.Entity == null || cached.Entity.IsDestroyed) continue;

                    var battery = (ElectricBattery)cached.Entity;
                    if (battery.rustWattSeconds >= battery.maxCapactiySeconds) continue;

                    float maxCharge = battery.maxCapactiySeconds - battery.rustWattSeconds;
                    float thisCharge = Mathf.Min(chargeWattSeconds, maxCharge);
                    battery.rustWattSeconds += thisCharge;
                    chargeWattSeconds -= thisCharge;

                    if (battery.rustWattSeconds > battery.maxCapactiySeconds)
                        battery.rustWattSeconds = battery.maxCapactiySeconds;

                    battery.SendNetworkUpdateImmediate();
                }
            }

            if (_firstRun || tc == null)
            {
                _previousLowPowerState[tcIdValue] = false;
                _previousPowerLossState[tcIdValue] = totalCapacity == 0;
                return;
            }
            
            CheckPowerNotifications(tc, tcIdValue, grid, totalCapacity, totalDemand, currentOnlineTurrets);
        }

        private void CheckPowerNotifications(BuildingPrivlidge tc, ulong tcIdValue, TCPowerGrid grid, int totalCapacity, int totalDemand, HashSet<ulong> currentOnlineTurrets)
        {
            if (_config.EnableLowPowerWarning && totalCapacity > 0)
            {
                int powerPercent = (grid.TotalUsed * 100) / totalCapacity;
                int availablePercent = 100 - powerPercent;
                bool isLowPower = availablePercent <= _config.LowPowerWarningThreshold && totalDemand > 0;
                
                _previousLowPowerState.TryGetValue(tcIdValue, out bool wasLowPower);
                
                if (isLowPower && !wasLowPower)
                {
                    string msg = Lang("LowPowerWarning", null, availablePercent);
                    SendNotificationToTCOwners(tc, msg, "lowpower");
                }
                
                _previousLowPowerState[tcIdValue] = isLowPower;
            }
            
            if (_config.EnablePowerLossNotification)
            {
                bool hasPowerLoss = totalCapacity == 0 && totalDemand > 0;
                
                _previousPowerLossState.TryGetValue(tcIdValue, out bool hadPowerLoss);
                
                if (hasPowerLoss && !hadPowerLoss)
                {
                    string msg = Lang("PowerLoss", null);
                    SendNotificationToTCOwners(tc, msg, "powerloss");
                }
                
                _previousPowerLossState[tcIdValue] = hasPowerLoss;
            }
            
            if (_config.EnableTurretOfflineNotification)
            {
                _previousOnlineTurrets.TryGetValue(tcIdValue, out var previousTurrets);

                if (previousTurrets != null && previousTurrets.Count > 0)
                {
                    foreach (var turretId in previousTurrets)
                    {
                        if (!currentOnlineTurrets.Contains(turretId))
                        {
                            string msg = Lang("TurretOffline", null);
                            SendNotificationToTCOwners(tc, msg, "turretoffline");
                            break;
                        }
                    }
                }
                
            }

            _previousOnlineTurrets[tcIdValue] = currentOnlineTurrets;
        }

        private void PowerConsumer(CachedConsumer cached, int availablePower)
        {
            var consumer = cached.Entity;
            if (consumer == null) return;

            consumer.SetFlagLocal(IOEntity.Flag_HasPower, true);

            bool hasInputs = consumer.inputs != null && consumer.inputs.Length > 0;

            if (hasInputs)
            {
                bool hasWiredInput = false;
                foreach (var input in consumer.inputs)
                {
                    if (input.connectedTo.Get() != null)
                    {
                        hasWiredInput = true;
                        break;
                    }
                }

                if (!hasWiredInput)
                {
                    int power;
                    if (cached.IsBranch)
                        power = ((ElectricalBranch)consumer).branchAmount;
                    else
                        power = cached.PowerRequired;

                    consumer.UpdateFromInput(power, 0);
                    consumer.SetFlagLocal(BaseEntity.Flags.On, true);
                }
            }
            else
            {
                consumer.SetFlagLocal(BaseEntity.Flags.On, true);
            }

            if (cached.IsTurret)
            {
                var turret = (AutoTurret)consumer;
                if (!turret.IsOnline() && !turret.booting)
                    turret.InitiateStartup();
            }
        }

        private void UpdatePassthrough(CachedConsumer cached, int availablePower)
        {
            var consumer = cached.Entity;
            if (consumer == null) return;
            
            if (consumer.inputs != null && consumer.inputs.Length > 0)
                consumer.UpdateFromInput(availablePower, 0);
        }

        private void DepowerConsumer(CachedConsumer cached, bool force = false)
        {
            var consumer = cached.Entity;
            if (consumer == null) return;

            if (force)
            {
                consumer.SetFlagLocal(IOEntity.Flag_HasPower, false);
                consumer.SetFlagLocal(BaseEntity.Flags.On, false);
                consumer.SendNetworkUpdateImmediate();
            }
            else
            {
                if (consumer.inputs != null && consumer.inputs.Length > 0)
                {
                    bool hasWiredInput = false;
                    foreach (var input in consumer.inputs)
                    {
                        if (input.connectedTo.Get() != null)
                        {
                            hasWiredInput = true;
                            break;
                        }
                    }

                    if (!hasWiredInput)
                        consumer.UpdateFromInput(0, 0);
                }

                DepowerFlags(consumer);
            }

            if (cached.IsTurret)
            {
                var turret = (AutoTurret)consumer;
                if (turret.IsOnline())
                    turret.InitiateShutdown();
            }
        }

        private void DepowerEntireGrid(TCPowerGrid grid)
        {
            if (grid == null) return;
            foreach (var cached in grid.Consumers)
            {
                if (cached.Entity == null || cached.Entity.IsDestroyed) continue;
                
                if (cached.IsSprinkler)
                {
                    var sprinkler = cached.Entity as Sprinkler;
                    SetSprinklerRunning(sprinkler, false);
                }
                else
                {
                    cached.Entity.UpdateFromInput(0, 0);
                    DepowerFlags(cached.Entity);
                    
                    if (cached.IsTurret)
                    {
                        var turret = (AutoTurret)cached.Entity;
                        if (turret.IsOnline())
                            turret.InitiateShutdown();
                    }
                }
            }
        }

        #endregion

        #region Commands

        [ChatCommand("power")]
        private void CmdPower(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                SendNotification(player, Lang("NoPermission", player.UserIDString));
                return;
            }

            if (args.Length > 0 && args[0].Equals("hide", StringComparison.OrdinalIgnoreCase))
            {
                if (_hiddenHUD.Contains(player.userID))
                {
                    _hiddenHUD.Remove(player.userID);
                    SendNotification(player, Lang("HUDVisible", player.UserIDString));
                }
                else
                {
                    _hiddenHUD.Add(player.userID);
                    CuiHelper.DestroyUi(player, PowerHUD);
                    _playersWithHUD.Remove(player.userID);
                    SendNotification(player, Lang("HUDHidden", player.UserIDString));
                }
                SaveData();
                return;
            }

            if (_playersWithUI.Contains(player.userID))
            {
                _playersWithUI.Remove(player.userID);
                _playerUIPage.Remove(player.userID);
                _playerUIPageNum.Remove(player.userID);
                DestroyUI(player);
            }
            else
            {
                var tc = player.GetBuildingPrivilege();
                if (tc == null)
                {
                    SendNotification(player, Lang("MustBeNearTC", player.UserIDString));
                    return;
                }

                if (!tc.IsAuthed(player))
                {
                    SendNotification(player, Lang("MustBeAuthedView", player.UserIDString));
                    return;
                }

                _playersWithUI.Add(player.userID);
                _playerUIPage[player.userID] = UIPage.Main;
                UpdateUI(player);
            }
        }

        #endregion

        #region UI

        private const string PowerUI = "WirelessPowerUI";
        private const string PowerUI_BG = "WirelessPowerUI_bg";
        private const string PowerUI_Stats = "WirelessPowerUI_stats";
        private const string PowerHUD = "WirelessPowerHUD";
        
        private const string StatValueAnchor_Capacity = "0.27 0.7";
        private const string StatValueAnchor_InUse = "0.72 0.7";
        private const string StatValueAnchor_Demand = "0.27 0.4";
        private const string StatValueAnchor_Status = "0.72 0.4";
        private const string StatValueAnchor_Sources = "0.27 0.1";
        private const string StatValueAnchor_Active = "0.72 0.1";

        private static class UIColors
        {
            public const string PanelDark = "0 0 0 1";
            public const string PanelLight = "0.15 0.15 0.15 1";
            public const string PanelBorder = "0.2 0.5 0.8 1";

            public const string Gold = "1 0.84 0 1";
            public const string Cyan = "0.3 0.9 1 1";
            public const string Green = "0.2 0.9 0.3 1";
            public const string Orange = "1 0.6 0.2 1";
            public const string Red = "0.9 0.2 0.2 1";
            public const string Blue = "0.2 0.5 0.8 1";

            public const string Button = "0.15 0.4 0.6 1";
            public const string ButtonHover = "0.2 0.5 0.7 1";

            public const string TextPrimary = "0.9 0.92 0.95 1";
            public const string TextSecondary = "0.65 0.7 0.75 1";
            public const string TextMuted = "0.45 0.5 0.55 1";
        }

        private void AddStyledPanel(CuiElementContainer container, string parent, string name, string anchorMin, string anchorMax)
        {
            string bgName = string.Concat(name, "_bg");
            string borderName = string.Concat(name, "_border");
            
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, parent, bgName);

            container.Add(new CuiPanel
            {
                Image = { Color = UIColors.PanelBorder },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                CursorEnabled = false
            }, bgName, borderName);

            container.Add(new CuiPanel
            {
                Image = { Color = UIColors.PanelDark },
                RectTransform = { AnchorMin = "0.005 0.008", AnchorMax = "0.995 0.992" },
                CursorEnabled = false
            }, borderName, name);
        }

        private void AddStyledButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, int fontSize = 13)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = UIColors.Button },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = text, FontSize = fontSize, Align = TextAnchor.MiddleCenter, Color = UIColors.TextPrimary }
            }, parent);
        }

        private void AddHeader(CuiElementContainer container, string parent, string text, string subtext = null)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = 22, Align = TextAnchor.MiddleCenter, Color = UIColors.Cyan, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 0.99" }
            }, parent);

            if (!string.IsNullOrEmpty(subtext))
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = subtext, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIColors.TextSecondary },
                    RectTransform = { AnchorMin = "0 0.87", AnchorMax = "1 0.92" }
                }, parent);
            }
        }

        private void UpdateUI(BasePlayer player)
        {
            if (!_playerUIPage.TryGetValue(player.userID, out UIPage page))
                page = UIPage.Main;

            switch (page)
            {
                case UIPage.Sources:
                    ShowSourcesPage(player);
                    break;
                case UIPage.Consumers:
                    ShowConsumersPage(player);
                    break;
                case UIPage.ConsumerDetail:
                    ShowConsumerDetailPage(player);
                    break;
                default:
                    ShowMainPage(player);
                    break;
            }
        }

        private void ShowMainPage(BasePlayer player)
        {
            DestroyUI(player);
            _playerUIPage[player.userID] = UIPage.Main;

            var container = new CuiElementContainer();
            AddStyledPanel(container, "Overlay", PowerUI, "0.3 0.25", "0.7 0.75");

            var uid = player.UserIDString;
            AddHeader(container, PowerUI, Lang("UI.Title", uid), Lang("UI.Subtitle", uid));

            AddStyledButton(container, PowerUI, Lang("UI.Close", uid), "power.close", "0.94 0.94", "0.99 0.99", 16);

            var tc = player.GetBuildingPrivilege();
            bool isDisabled = tc != null && _disabledGrids.Contains(tc.net.ID.Value);

            var grid = GetPlayerCurrentGrid(player);

            string statusText, statusColor;
            if (isDisabled)
            {
                statusText = Lang("UI.StatusDisabled", uid);
                statusColor = UIColors.Red;
            }
            else if (grid == null || grid.Sources.Count == 0)
            {
                statusText = Lang("UI.StatusNoSources", uid);
                statusColor = UIColors.TextMuted;
            }
            else if (grid.TotalCapacity == 0)
            {
                statusText = Lang("UI.StatusNoPower", uid);
                statusColor = UIColors.Red;
            }
            else if (grid.TotalCapacity >= grid.TotalDemand)
            {
                statusText = Lang("UI.StatusOnline", uid);
                statusColor = UIColors.Green;
            }
            else
            {
                statusText = Lang("UI.StatusPartial", uid);
                statusColor = UIColors.Orange;
            }

            container.Add(new CuiPanel
            {
                Image = { Color = UIColors.PanelLight },
                RectTransform = { AnchorMin = "0.05 0.55", AnchorMax = "0.95 0.85" }
            }, PowerUI, PowerUI_Stats);

            int capacity = grid?.TotalCapacity ?? 0;
            int used = grid?.TotalUsed ?? 0;
            int demand = grid?.TotalDemand ?? 0;
            int sources = (grid?.Sources.Count ?? 0) + (grid?.WaterContainers.Count ?? 0);
            int consumers = grid?.Consumers.Count ?? 0;
            int active = grid?.ActiveConsumers.Count ?? 0;

            AddStatLabel(container, PowerUI_Stats, Lang("UI.Capacity", uid), GetPowerString(capacity), UIColors.Green, "0.05 0.7", "0.5 0.95", StatValueAnchor_Capacity);
            AddStatLabel(container, PowerUI_Stats, Lang("UI.InUse", uid), GetPowerString(used), UIColors.Gold, "0.5 0.7", "0.95 0.95", StatValueAnchor_InUse);

            AddStatLabel(container, PowerUI_Stats, Lang("UI.Demand", uid), GetPowerString(demand), UIColors.Orange, "0.05 0.4", "0.5 0.65", StatValueAnchor_Demand);
            AddStatLabel(container, PowerUI_Stats, Lang("UI.Status", uid), statusText, statusColor, "0.5 0.4", "0.95 0.65", StatValueAnchor_Status);

            AddStatLabel(container, PowerUI_Stats, Lang("UI.Sources", uid), sources.ToString(), UIColors.Cyan, "0.05 0.1", "0.5 0.35", StatValueAnchor_Sources);
            AddStatLabel(container, PowerUI_Stats, Lang("UI.Active", uid), GetCountString(active, consumers), UIColors.TextPrimary, "0.5 0.1", "0.95 0.35", StatValueAnchor_Active);

            if (!isDisabled)
            {
                AddStyledButton(container, PowerUI, Lang("UI.ViewSources", uid), "power.page sources", "0.08 0.35", "0.48 0.48", 14);
                AddStyledButton(container, PowerUI, Lang("UI.ViewConsumers", uid), "power.page consumers", "0.52 0.35", "0.92 0.48", 14);
            }

            AddStyledButton(container, PowerUI, Lang("UI.Refresh", uid), "power.refresh", "0.35 0.22", "0.65 0.32", 12);

            string toggleColor = isDisabled ? UIColors.Green : UIColors.Red;
            string toggleText = isDisabled ? Lang("UI.EnableWireless", uid) : Lang("UI.DisableWireless", uid);
            container.Add(new CuiButton
            {
                Button = { Command = "power.togglegrid", Color = toggleColor },
                RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.19" },
                Text = { Text = toggleText, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = UIColors.TextPrimary }
            }, PowerUI);

            container.Add(new CuiPanel
            {
                Image = { Color = UIColors.PanelBorder },
                RectTransform = { AnchorMin = "0.1 0.08", AnchorMax = "0.9 0.085" }
            }, PowerUI);

            container.Add(new CuiLabel
            {
                Text = { Text = Lang("UI.Footer", uid), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIColors.TextMuted },
                RectTransform = { AnchorMin = "0 0.02", AnchorMax = "1 0.07" }
            }, PowerUI);

            CuiHelper.AddUi(player, container);
        }

        private void AddStatLabel(CuiElementContainer container, string parent, string label, string value, string valueColor, string anchorMin, string anchorMax, string valueAnchorMin)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = UIColors.TextSecondary },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);

            container.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 12, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = valueColor },
                RectTransform = { AnchorMin = valueAnchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void ShowSourcesPage(BasePlayer player)
        {
            DestroyUI(player);
            _playerUIPage[player.userID] = UIPage.Sources;

            if (!_playerUIPageNum.ContainsKey(player.userID))
                _playerUIPageNum[player.userID] = 0;

            var container = new CuiElementContainer();
            AddStyledPanel(container, "Overlay", PowerUI, "0.25 0.15", "0.75 0.85");

            var uid = player.UserIDString;
            AddHeader(container, PowerUI, Lang("UI.TitleSources", uid));

            AddStyledButton(container, PowerUI, Lang("UI.Close", uid), "power.close", "0.955 0.955", "0.99 0.99", 14);
            AddStyledButton(container, PowerUI, Lang("UI.Back", uid), "power.page main", "0.01 0.955", "0.1 0.99", 12);

            var grid = GetPlayerCurrentGrid(player);
            var sources = grid?.Sources;
            var waterContainers = grid?.WaterContainers;

            _uiDisplayItems.Clear();
            _uiTempItems.Clear();

            if (sources != null)
            {
                foreach (var cached in sources)
                {
                    var source = cached.Entity;
                    if (source == null || source.IsDestroyed) continue;

                    int output = GetSourceCurrentOutput(cached);
                    string extra = "";
                    string color = output > 0 ? UIColors.Green : UIColors.Red;

                    if (cached.IsBattery)
                    {
                        var battery = (ElectricBattery)source;
                        float minutes = battery.rustWattSeconds / 60f;
                        extra = minutes >= 60f ? $" [{minutes / 60f:F1}h]" : $" [{minutes:F1}m]";
                        if (output == 0) extra = Lang("UI.BatteryEmpty", uid);
                    }
                    else if (cached.IsFuelGen)
                    {
                        var fuelGen = (FuelGenerator)source;
                        extra = fuelGen.IsOn() ? Lang("UI.FuelRunning", uid) : (fuelGen.HasFuel() ? Lang("UI.FuelOff", uid) : Lang("UI.FuelNoFuel", uid));
                    }

                    _uiTempItems.Add((cached.DisplayName + extra, GetPowerString(output), color, "power"));
                }
            }

            if (_uiTempItems.Count > 0)
            {
                _uiTempItems.Sort(TupleNameComparison);
                _uiDisplayItems.Add((Lang("UI.HeaderPowerSources", uid), "", UIColors.Cyan, "header"));
                foreach (var item in _uiTempItems)
                    _uiDisplayItems.Add(item);
            }

            if (_config.EnableWirelessSprinklers && waterContainers != null && waterContainers.Count > 0)
            {
                _uiTempItems.Clear();

                foreach (var wc in waterContainers)
                {
                    if (wc == null || wc.IsDestroyed) continue;

                    int waterAmount = 0;
                    if (wc.inventory != null)
                    {
                        foreach (var item in wc.inventory.itemList)
                        {
                            if (item.info.shortname == WaterShortname || item.info.shortname == SaltWaterShortname)
                                waterAmount += item.amount;
                        }
                    }

                    string color = waterAmount > 0 ? UIColors.Cyan : UIColors.Red;
                    string status = waterAmount > 0 ? Lang("UI.WaterAmount", uid, waterAmount) : Lang("UI.WaterEmpty", uid);
                    _uiTempItems.Add((GetEntityDisplayName(wc), status, color, "water"));
                }

                _uiTempItems.Sort(TupleNameComparison);
                _uiDisplayItems.Add((Lang("UI.HeaderWaterContainers", uid), "", UIColors.Cyan, "header"));
                foreach (var item in _uiTempItems)
                    _uiDisplayItems.Add(item);
            }

            int itemsPerPage = 10;
            int totalPages = Math.Max(1, (int)Math.Ceiling(_uiDisplayItems.Count / (float)itemsPerPage));
            int currentPage = _playerUIPageNum[player.userID];
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;
            _playerUIPageNum[player.userID] = currentPage;

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, _uiDisplayItems.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var item = _uiDisplayItems[i];
                int row = i - startIndex;
                var anchors = _rowAnchors[row];

                if (item.extra == "header")
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.name, FontSize = 12, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = item.color },
                        RectTransform = { AnchorMin = anchors.PanelMin, AnchorMax = anchors.PanelMax }
                    }, PowerUI);
                }
                else
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = UIColors.PanelLight },
                        RectTransform = { AnchorMin = anchors.PanelMin, AnchorMax = anchors.PanelMax }
                    }, PowerUI);

                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.name, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = UIColors.TextPrimary },
                        RectTransform = { AnchorMin = anchors.SourceNameMin, AnchorMax = anchors.SourceNameMax }
                    }, PowerUI);

                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.value, FontSize = 12, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleRight, Color = item.color },
                        RectTransform = { AnchorMin = anchors.SourceValueMin, AnchorMax = anchors.SourceValueMax }
                    }, PowerUI);
                }
            }

            if (_uiDisplayItems.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Lang("UI.NoSourcesFound", uid), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = UIColors.TextMuted },
                    RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = "0.9 0.6" }
                }, PowerUI);
            }

            if (totalPages > 1)
            {
                if (currentPage > 0)
                    AddStyledButton(container, PowerUI, Lang("UI.PrevPage", uid), "power.prevpage", "0.03 0.02", "0.18 0.07", 11);

                container.Add(new CuiLabel
                {
                    Text = { Text = GetPageString(currentPage, totalPages), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = UIColors.Gold },
                    RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.07" }
                }, PowerUI);

                if (currentPage < totalPages - 1)
                    AddStyledButton(container, PowerUI, Lang("UI.NextPage", uid), "power.nextpage", "0.82 0.02", "0.97 0.07", 11);
            }

            CuiHelper.AddUi(player, container);
        }

        private void ShowConsumersPage(BasePlayer player)
        {
            DestroyUI(player);
            _playerUIPage[player.userID] = UIPage.Consumers;

            if (!_playerUIPageNum.ContainsKey(player.userID))
                _playerUIPageNum[player.userID] = 0;

            var container = new CuiElementContainer();
            AddStyledPanel(container, "Overlay", PowerUI, "0.25 0.15", "0.75 0.85");

            var uid = player.UserIDString;
            AddHeader(container, PowerUI, Lang("UI.TitleConsumers", uid));

            AddStyledButton(container, PowerUI, Lang("UI.Close", uid), "power.close", "0.955 0.955", "0.99 0.99", 14);
            AddStyledButton(container, PowerUI, Lang("UI.Back", uid), "power.page main", "0.01 0.955", "0.1 0.99", 12);

            var grid = GetPlayerCurrentGrid(player);
            var consumers = grid?.Consumers;

            _uiGroupedConsumers.Clear();
            if (consumers != null)
            {
                foreach (var cached in consumers)
                {
                    if (cached.Entity == null || cached.Entity.IsDestroyed) continue;
                    var cat = cached.Category;
                    if (!_uiGroupedConsumers.TryGetValue(cat, out var list))
                    {
                        list = Facepunch.Pool.Get<List<CachedConsumer>>();
                        _uiGroupedConsumers[cat] = list;
                    }
                    list.Add(cached);
                }
            }

            _uiCategoryList.Clear();
            _uiCategoryList.AddRange(_uiGroupedConsumers.Keys);
            _uiCategoryList.Sort(CategoryNameComparison);

            int itemsPerPage = 10;
            int totalPages = Math.Max(1, (int)Math.Ceiling(_uiCategoryList.Count / (float)itemsPerPage));
            int currentPage = _playerUIPageNum[player.userID];
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;
            _playerUIPageNum[player.userID] = currentPage;

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, _uiCategoryList.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var category = _uiCategoryList[i];
                var items = _uiGroupedConsumers[category];
                
                int row = i - startIndex;
                var anchors = _rowAnchors[row];

                int poweredCount = 0;
                int poweredWatts = 0;
                int forcedOffCount = 0;
                int validItemCount = 0;
                foreach (var c in items)
                {
                    if (c.Entity == null) continue;
                    validItemCount++;
                    
                    bool isForcedOff = grid.ForcedOff.Contains(c.NetIdValue);
                    bool isTurretSwitchOff = c.IsTurret && !IsTurretSwitchOn((AutoTurret)c.Entity);
                    
                    if (isForcedOff || isTurretSwitchOff)
                        forcedOffCount++;
                    
                    if (!isForcedOff && !isTurretSwitchOff)
                    {
                        bool isPowered = c.IsSprinkler ? c.Entity.IsOn() : c.Entity.HasFlag(IOEntity.Flag_HasPower);
                        if (isPowered)
                        {
                            poweredCount++;
                            int watts = c.IsBranch && c.Entity != null ? ((ElectricalBranch)c.Entity).branchAmount : c.PowerRequired;
                            poweredWatts += watts;
                        }
                    }
                }
                
                bool showEnableButton = validItemCount > 0 && forcedOffCount == validItemCount;
                
                string statusColor = poweredCount == 0 ? UIColors.Red : (poweredCount == validItemCount ? UIColors.Green : UIColors.Orange);
                string statusText = GetCountString(poweredCount, validItemCount);

                container.Add(new CuiPanel
                {
                    Image = { Color = UIColors.PanelLight },
                    RectTransform = { AnchorMin = anchors.PanelMin, AnchorMax = anchors.PanelMax }
                }, PowerUI);

                string categoryName = CategoryNames[(int)category];
                container.Add(new CuiLabel
                {
                    Text = { Text = categoryName + " x" + items.Count, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = UIColors.TextPrimary },
                    RectTransform = { AnchorMin = anchors.CategoryNameMin, AnchorMax = anchors.CategoryNameMax }
                }, PowerUI);

                container.Add(new CuiLabel
                {
                    Text = { Text = GetPowerString(poweredWatts), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIColors.TextSecondary },
                    RectTransform = { AnchorMin = anchors.CategoryWattsMin, AnchorMax = anchors.CategoryWattsMax }
                }, PowerUI);

                container.Add(new CuiLabel
                {
                    Text = { Text = statusText, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = statusColor },
                    RectTransform = { AnchorMin = anchors.CategoryCountMin, AnchorMax = anchors.CategoryCountMax }
                }, PowerUI);

                string toggleColor = showEnableButton ? UIColors.Green : UIColors.Red;
                string toggleText = showEnableButton ? Lang("UI.Enable", uid) : Lang("UI.Disable", uid);
                container.Add(new CuiButton
                {
                    Button = { Command = _categoryToggleCommands[(int)category], Color = toggleColor },
                    RectTransform = { AnchorMin = anchors.CategoryToggleBtnMin, AnchorMax = anchors.CategoryToggleBtnMax },
                    Text = { Text = toggleText, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIColors.TextPrimary }
                }, PowerUI);

                container.Add(new CuiButton
                {
                    Button = { Command = _categoryViewCommands[(int)category], Color = UIColors.Blue },
                    RectTransform = { AnchorMin = anchors.CategoryViewBtnMin, AnchorMax = anchors.CategoryViewBtnMax },
                    Text = { Text = Lang("UI.View", uid), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIColors.TextPrimary }
                }, PowerUI);
            }

            if (_uiCategoryList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Lang("UI.NoConsumersFound", uid), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = UIColors.TextMuted },
                    RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = "0.9 0.6" }
                }, PowerUI);
            }

            if (totalPages > 1)
            {
                if (currentPage > 0)
                    AddStyledButton(container, PowerUI, Lang("UI.PrevPage", uid), "power.prevpage", "0.03 0.02", "0.18 0.07", 11);

                container.Add(new CuiLabel
                {
                    Text = { Text = GetPageString(currentPage, totalPages), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = UIColors.Gold },
                    RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.07" }
                }, PowerUI);

                if (currentPage < totalPages - 1)
                    AddStyledButton(container, PowerUI, Lang("UI.NextPage", uid), "power.nextpage", "0.82 0.02", "0.97 0.07", 11);
            }

            foreach (var kvp in _uiGroupedConsumers)
            {
                var list = kvp.Value;
                Facepunch.Pool.FreeUnmanaged(ref list);
            }
            _uiGroupedConsumers.Clear();
            
            CuiHelper.AddUi(player, container);
        }

        private void UpdateHUDButtons()
        {
            if (!_config.EnableHUDButton)
            {
                if (_playersWithHUD.Count > 0)
                {
                    foreach (var player in BasePlayer.activePlayerList)
                    {
                        if (player != null && _playersWithHUD.Contains(player.userID))
                            CuiHelper.DestroyUi(player, PowerHUD);
                    }
                    _playersWithHUD.Clear();
                }
                return;
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                if (!permission.UserHasPermission(player.UserIDString, PermUse)) continue;

                if (_hiddenHUD.Contains(player.userID))
                {
                    if (_playersWithHUD.Contains(player.userID))
                    {
                        CuiHelper.DestroyUi(player, PowerHUD);
                        _playersWithHUD.Remove(player.userID);
                    }
                    continue;
                }

                var tc = player.GetBuildingPrivilege();
                bool shouldShow = tc != null && tc.IsAuthed(player) &&
                    (_tcGrids.ContainsKey(tc.net.ID) || _disabledGrids.Contains(tc.net.ID.Value));

                bool hasHUD = _playersWithHUD.Contains(player.userID);

                if (shouldShow && !hasHUD)
                {
                    ShowHUDButton(player);
                    _playersWithHUD.Add(player.userID);
                }
                else if (!shouldShow && hasHUD)
                {
                    CuiHelper.DestroyUi(player, PowerHUD);
                    _playersWithHUD.Remove(player.userID);
                }
            }
        }

        private void ShowHUDButton(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, PowerHUD);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.4 0.6 0.85" },
                RectTransform = { AnchorMin = "0.96 0.45", AnchorMax = "0.995 0.52" }
            }, "Overlay", PowerHUD);

            container.Add(new CuiElement
            {
                Parent = PowerHUD,
                Components =
                {
                    new CuiRectTransformComponent { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                    new CuiRawImageComponent { Url = "https://i.ibb.co/d4fPcZdM/wireless-power-icon.png", Color = "1 1 1 1" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Command = "power.openfromhud", Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, PowerHUD);

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("power.openfromhud")]
        private void CmdOpenFromHUD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            if (_playersWithUI.Contains(player.userID))
            {
                _playersWithUI.Remove(player.userID);
                _playerUIPage.Remove(player.userID);
                _playerUIPageNum.Remove(player.userID);
                DestroyUI(player);
            }
            else
            {
                var tc = player.GetBuildingPrivilege();
                if (tc == null || !tc.IsAuthed(player)) return;

                _playersWithUI.Add(player.userID);
                _playerUIPage[player.userID] = UIPage.Main;
                UpdateUI(player);
            }
        }

        [ConsoleCommand("power.togglegrid")]
        private void CmdToggleGrid(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player))
            {
                SendNotification(player, Lang("MustBeAuthed", player.UserIDString));
                return;
            }

            ulong tcIdValue = tc.net.ID.Value;

            if (_disabledGrids.Contains(tcIdValue))
            {
                _disabledGrids.Remove(tcIdValue);
                BuildTCGrid(tc);
                if (_tcGrids.TryGetValue(tc.net.ID, out var newGrid))
                    ProcessPowerGrid(tc.net.ID, newGrid);
                SendNotification(player, Lang("WirelessEnabled", player.UserIDString));
            }
            else
            {
                _disabledGrids.Add(tcIdValue);
                
                if (_tcGrids.TryGetValue(tc.net.ID, out var grid))
                {
                    DepowerEntireGrid(grid);
                    _tcGrids.Remove(tc.net.ID);
                }
                
                SendNotification(player, Lang("WirelessDisabled", player.UserIDString));
            }

            SaveData();
            
            _playersWithUI.Remove(player.userID);
            _playerUIPage.Remove(player.userID);
            _playerUIPageNum.Remove(player.userID);
            DestroyUI(player);
            
            _playersWithUI.Add(player.userID);
            _playerUIPage[player.userID] = UIPage.Main;
            UpdateUI(player);
        }

        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, PowerUI_BG);
            CuiHelper.DestroyUi(player, RenameUI);
        }

        [ConsoleCommand("power.close")]
        private void CmdUIClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            DestroyUI(player);
            _playersWithUI.Remove(player.userID);
            _playerUIPage.Remove(player.userID);
            _playerUIPageNum.Remove(player.userID);
        }

        [ConsoleCommand("power.page")]
        private void CmdUIPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!_playersWithUI.Contains(player.userID)) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player))
            {
                DestroyUI(player);
                _playersWithUI.Remove(player.userID);
                return;
            }

            string pageStr = arg.GetString(0, "main");
            UIPage page;
            if (string.Equals(pageStr, "sources", StringComparison.OrdinalIgnoreCase))
                page = UIPage.Sources;
            else if (string.Equals(pageStr, "consumers", StringComparison.OrdinalIgnoreCase))
                page = UIPage.Consumers;
            else
                page = UIPage.Main;
            _playerUIPage[player.userID] = page;
            _playerUIPageNum[player.userID] = 0;
            UpdateUI(player);
        }

        [ConsoleCommand("power.prevpage")]
        private void CmdUIPrevPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!_playersWithUI.Contains(player.userID)) return;

            if (_playerUIPageNum.TryGetValue(player.userID, out var pageNum))
                _playerUIPageNum[player.userID] = Math.Max(0, pageNum - 1);
            UpdateUI(player);
        }

        [ConsoleCommand("power.nextpage")]
        private void CmdUINextPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!_playersWithUI.Contains(player.userID)) return;

            if (_playerUIPageNum.TryGetValue(player.userID, out var pageNum))
                _playerUIPageNum[player.userID] = pageNum + 1;
            UpdateUI(player);
        }

        [ConsoleCommand("power.refresh")]
        private void CmdUIRefresh(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;
            
            BuildTCGrid(tc);
            if (_tcGrids.TryGetValue(tc.net.ID, out var grid))
                ProcessPowerGrid(tc.net.ID, grid);
            UpdateUI(player);
        }

        [ConsoleCommand("power.toggledevice")]
        private void CmdToggleDevice(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var netIdValue = arg.GetUInt64(0, 0);
            if (netIdValue == 0) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;

            if (!_tcGrids.TryGetValue(tc.net.ID, out var grid)) return;

            CachedConsumer cached = null;
            foreach (var c in grid.Consumers)
            {
                if (c.Entity != null && c.NetIdValue == netIdValue)
                {
                    cached = c;
                    break;
                }
            }
            if (cached == null) return;

            var entityNetId = cached.NetIdValue;
            
            if (grid.ForcedOff.Contains(entityNetId))
                grid.ForcedOff.Remove(entityNetId);
            else
                grid.ForcedOff.Add(entityNetId);

            ProcessPowerGrid(tc.net.ID, grid);
            ShowConsumerDetailPage(player);
        }

        [ConsoleCommand("power.toggleturret")]
        private void CmdToggleTurret(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var netIdValue = arg.GetUInt64(0, 0);
            if (netIdValue == 0) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;

            if (!_tcGrids.TryGetValue(tc.net.ID, out var grid)) return;

            var netId = new NetworkableId(netIdValue);
            
            if (!_turretSwitches.TryGetValue(netId, out var sw) || sw == null || sw.IsDestroyed)
            {
                if (grid.ForcedOff.Contains(netIdValue))
                    grid.ForcedOff.Remove(netIdValue);
                else
                    grid.ForcedOff.Add(netIdValue);
                
                ProcessPowerGrid(tc.net.ID, grid);
                ShowConsumerDetailPage(player);
                return;
            }
            
            var turret = sw.GetParentEntity() as AutoTurret;
            if (turret == null) return;
            
            bool turningOn = !sw.IsOn();
            sw.SetSwitch(turningOn);
            
            if (turningOn)
            {
                grid.ForcedOff.Remove(turret.net.ID.Value);
                
                if (HasAvailablePowerAtTC(tc.net.ID, GetConsumerPowerRequired(turret)))
                {
                    turret.SetFlagLocal(IOEntity.Flag_HasPower, true);
                    if (!turret.IsOnline() && !turret.booting)
                        turret.InitiateStartup();
                }
            }
            else
            {
                grid.ForcedOff.Add(turret.net.ID.Value);
                
                turret.SetFlagLocal(IOEntity.Flag_HasPower, false);
                if (turret.IsOnline())
                    turret.InitiateShutdown();
            }
            
            ProcessPowerGrid(tc.net.ID, grid);
            ShowConsumerDetailPage(player);
        }

        [ConsoleCommand("power.togglecategory")]
        private void CmdToggleCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            int categoryInt = arg.GetInt(0, -1);
            if (categoryInt < 0 || categoryInt >= CategoryNames.Length) return;
            var category = (DeviceCategory)categoryInt;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;

            if (!_tcGrids.TryGetValue(tc.net.ID, out var grid)) return;

            int totalInCategory = 0;
            int offCount = 0;
            foreach (var cached in grid.Consumers)
            {
                if (cached.Category != category || cached.Entity == null) continue;
                totalInCategory++;
                
                bool isForcedOff = grid.ForcedOff.Contains(cached.NetIdValue);
                bool isTurretSwitchOff = cached.IsTurret && !IsTurretSwitchOn((AutoTurret)cached.Entity);
                
                if (isForcedOff || isTurretSwitchOff)
                    offCount++;
            }
            
            bool enabling = totalInCategory > 0 && offCount == totalInCategory;
            
            foreach (var cached in grid.Consumers)
            {
                if (cached.Category != category || cached.Entity == null) continue;
                
                var entityNetId = cached.NetIdValue;
                if (enabling)
                    grid.ForcedOff.Remove(entityNetId);
                else
                    grid.ForcedOff.Add(entityNetId);
                
                if (cached.IsTurret)
                {
                    var turret = (AutoTurret)cached.Entity;
                    if (_turretSwitches.TryGetValue(turret.net.ID, out var sw) && sw != null && !sw.IsDestroyed)
                    {
                        sw.SetSwitch(enabling);
                    }
                }
            }

            ProcessPowerGrid(tc.net.ID, grid);
            ShowConsumersPage(player);
        }

        [ConsoleCommand("power.viewcategory")]
        private void CmdViewCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            int categoryInt = arg.GetInt(0, -1);
            if (categoryInt < 0 || categoryInt >= CategoryNames.Length) return;
            var category = (DeviceCategory)categoryInt;

            _playerViewingCategory[player.userID] = category;
            _playerUIPageNum[player.userID] = 0;
            ShowConsumerDetailPage(player);
        }

        [ConsoleCommand("power.cleanup")]
        private void CmdCleanup(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You must be an admin to use this command.");
                return;
            }
            
            int switchesRemoved = 0;
            foreach (var kvp in _turretSwitches)
            {
                var sw = kvp.Value;
                if (sw != null && !sw.IsDestroyed)
                {
                    sw.Kill();
                    switchesRemoved++;
                }
            }
            _turretSwitches.Clear();
            _data.TurretSwitches.Clear();
            SaveData();
            
            arg.ReplyWith($"Removed {switchesRemoved} turret switches. Plugin can now be safely removed.");
            Puts($"Admin cleanup: removed {switchesRemoved} turret switches.");
        }

        private const string RenameUI = "WirelessPowerRenameUI";

        [ConsoleCommand("power.startrename")]
        private void CmdStartRename(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var netIdValue = arg.GetUInt64(0, 0);
            if (netIdValue == 0) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;

            CuiHelper.DestroyUi(player, RenameUI);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.95" },
                RectTransform = { AnchorMin = "0.3 0.4", AnchorMax = "0.7 0.55" },
                CursorEnabled = true,
                KeyboardEnabled = true
            }, "Overlay", RenameUI);

            var uid = player.UserIDString;
            container.Add(new CuiLabel
            {
                Text = { Text = Lang("UI.TitleRename", uid), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = UIColors.Gold },
                RectTransform = { AnchorMin = "0 0.7", AnchorMax = "1 1" }
            }, RenameUI);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.05 0.3", AnchorMax = "0.95 0.65" }
            }, RenameUI, RenameUI + "_input_bg");

            string existing = _data.CustomNames.TryGetValue(netIdValue, out var n) ? n : "";

            container.Add(new CuiElement
            {
                Name = RenameUI + "_input",
                Parent = RenameUI + "_input_bg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = existing,
                        FontSize = 12,
                        Color = "1 1 1 1",
                        Align = TextAnchor.MiddleCenter,
                        Command = "power.submitrename " + netIdValue,
                        CharsLimit = 32
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
                }
            });

            AddStyledButton(container, RenameUI, Lang("UI.Clear", uid), "power.clearname " + netIdValue, "0.05 0.0", "0.3 0.25", 10);
            AddStyledButton(container, RenameUI, Lang("UI.Save", uid), "power.submitrename " + netIdValue, "0.375 0.0", "0.625 0.25", 10);
            AddStyledButton(container, RenameUI, Lang("UI.Cancel", uid), "power.cancelrename", "0.7 0.0", "0.95 0.25", 10);

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("power.submitrename")]
        private void CmdSubmitRename(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var netIdValue = arg.GetUInt64(0, 0);
            if (netIdValue == 0) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;

            string fullArgs = arg.FullString.ToString();
            int spaceIndex = fullArgs.IndexOf(' ');
            string newName = spaceIndex >= 0 ? fullArgs.Substring(spaceIndex + 1).Trim() : "";

            if (string.IsNullOrEmpty(newName))
            {
                _data.CustomNames.Remove(netIdValue);
            }
            else
            {
                if (newName.Length > 32)
                    newName = newName.Substring(0, 32);
                _data.CustomNames[netIdValue] = newName;
            }

            if (_tcGrids.TryGetValue(tc.net.ID, out var grid))
            {
                foreach (var c in grid.Consumers)
                {
                    if (c.NetIdValue == netIdValue)
                    {
                        c.DisplayName = string.IsNullOrEmpty(newName) ? GetEntityDisplayName(c.Entity) : newName;
                        break;
                    }
                }
            }

            CuiHelper.DestroyUi(player, RenameUI);
            ShowConsumerDetailPage(player);
        }

        [ConsoleCommand("power.clearname")]
        private void CmdClearName(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var netIdValue = arg.GetUInt64(0, 0);
            if (netIdValue == 0) return;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !tc.IsAuthed(player)) return;

            _data.CustomNames.Remove(netIdValue);

            if (_tcGrids.TryGetValue(tc.net.ID, out var grid))
            {
                foreach (var c in grid.Consumers)
                {
                    if (c.NetIdValue == netIdValue)
                    {
                        c.DisplayName = GetEntityDisplayName(c.Entity);
                        break;
                    }
                }
            }

            CuiHelper.DestroyUi(player, RenameUI);
            ShowConsumerDetailPage(player);
        }

        [ConsoleCommand("power.cancelrename")]
        private void CmdCancelRename(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            CuiHelper.DestroyUi(player, RenameUI);
        }

        private void ShowConsumerDetailPage(BasePlayer player)
        {
            DestroyUI(player);
            _playerUIPage[player.userID] = UIPage.ConsumerDetail;

            if (!_playerUIPageNum.ContainsKey(player.userID))
                _playerUIPageNum[player.userID] = 0;

            if (!_playerViewingCategory.TryGetValue(player.userID, out var category))
            {
                ShowConsumersPage(player);
                return;
            }

            var container = new CuiElementContainer();
            AddStyledPanel(container, "Overlay", PowerUI, "0.25 0.15", "0.75 0.85");

            var uid = player.UserIDString;
            AddHeader(container, PowerUI, CategoryNames[(int)category]);

            AddStyledButton(container, PowerUI, Lang("UI.Close", uid), "power.close", "0.955 0.955", "0.99 0.99", 14);
            AddStyledButton(container, PowerUI, Lang("UI.Back", uid), "power.page consumers", "0.01 0.955", "0.1 0.99", 12);

            var grid = GetPlayerCurrentGrid(player);
            _uiDeviceList.Clear();
            if (grid?.Consumers != null)
            {
                foreach (var c in grid.Consumers)
                {
                    if (c.Category == category && c.Entity != null && !c.Entity.IsDestroyed)
                        _uiDeviceList.Add(c);
                }
                _uiDeviceList.Sort(ConsumerDisplayNameComparison);
            }

            int itemsPerPage = 10;
            int totalPages = Math.Max(1, (int)Math.Ceiling(_uiDeviceList.Count / (float)itemsPerPage));
            int currentPage = _playerUIPageNum[player.userID];
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;
            _playerUIPageNum[player.userID] = currentPage;

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, _uiDeviceList.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var cached = _uiDeviceList[i];
                var device = cached.Entity;

                int row = i - startIndex;
                var anchors = _rowAnchors[row];

                bool isForcedOff = grid.ForcedOff.Contains(cached.NetIdValue);
                bool isTurretSwitchOff = cached.IsTurret && !IsTurretSwitchOn((AutoTurret)device);
                bool isPowered = (isForcedOff || isTurretSwitchOff) ? false : (cached.IsSprinkler ? device.IsOn() : device.HasFlag(IOEntity.Flag_HasPower));

                string statusColor = (isForcedOff || isTurretSwitchOff) ? UIColors.Red : (isPowered ? UIColors.Green : UIColors.Orange);
                string statusText = isPowered ? Lang("UI.StatusOn", uid) : Lang("UI.StatusOff", uid);

                container.Add(new CuiPanel
                {
                    Image = { Color = UIColors.PanelLight },
                    RectTransform = { AnchorMin = anchors.PanelMin, AnchorMax = anchors.PanelMax }
                }, PowerUI);

                container.Add(new CuiLabel
                {
                    Text = { Text = cached.DisplayName, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = UIColors.TextPrimary },
                    RectTransform = { AnchorMin = anchors.DeviceNameMin, AnchorMax = anchors.DeviceNameMax }
                }, PowerUI);

                container.Add(new CuiLabel
                {
                    Text = { Text = cached.PowerString, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = UIColors.TextSecondary },
                    RectTransform = { AnchorMin = anchors.DevicePowerMin, AnchorMax = anchors.DevicePowerMax }
                }, PowerUI);

                container.Add(new CuiLabel
                {
                    Text = { Text = statusText, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = statusColor },
                    RectTransform = { AnchorMin = anchors.DeviceStatusMin, AnchorMax = anchors.DeviceStatusMax }
                }, PowerUI);

                string btnColor = isForcedOff ? UIColors.Green : UIColors.Red;
                string btnText = isForcedOff ? Lang("UI.Enable", uid) : Lang("UI.Disable", uid);
                container.Add(new CuiButton
                {
                    Button = { Command = cached.ToggleCommand, Color = btnColor },
                    RectTransform = { AnchorMin = anchors.DeviceBtnMin, AnchorMax = anchors.DeviceBtnMax },
                    Text = { Text = btnText, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIColors.TextPrimary }
                }, PowerUI);

                container.Add(new CuiButton
                {
                    Button = { Command = "power.startrename " + cached.NetIdValue, Color = UIColors.Blue },
                    RectTransform = { AnchorMin = anchors.DeviceRenameBtnMin, AnchorMax = anchors.DeviceRenameBtnMax },
                    Text = { Text = Lang("UI.Rename", uid), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIColors.TextPrimary }
                }, PowerUI);
            }

            if (_uiDeviceList.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = Lang("UI.NoCategoryFound", uid, CategoryNames[(int)category]), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = UIColors.TextMuted },
                    RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = "0.9 0.6" }
                }, PowerUI);
            }

            if (totalPages > 1)
            {
                if (currentPage > 0)
                    AddStyledButton(container, PowerUI, Lang("UI.PrevPage", uid), "power.prevpage", "0.03 0.02", "0.18 0.07", 11);

                container.Add(new CuiLabel
                {
                    Text = { Text = GetPageString(currentPage, totalPages), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = UIColors.Gold },
                    RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.07" }
                }, PowerUI);

                if (currentPage < totalPages - 1)
                    AddStyledButton(container, PowerUI, Lang("UI.NextPage", uid), "power.nextpage", "0.82 0.02", "0.97 0.07", 11);
            }

            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to use this.",
                ["LowPowerWarning"] = "<color=#ffaa00>Low Power Warning!</color>\nOnly {0}% power remaining.",
                ["PowerLoss"] = "<color=#ff4444>Power Lost!</color>\nYour base has no power available.",
                ["TurretOffline"] = "<color=#ff4444>Turret Offline!</color>\nA turret lost power and went offline.",
                ["MustBeNearTC"] = "You must be near a Tool Cupboard to view power!",
                ["MustBeAuthedView"] = "You must be authorized on the Tool Cupboard to view power!",
                ["MustBeAuthed"] = "You must be authorized on the Tool Cupboard.",
                ["MustBeAuthedSwitch"] = "You must be authorized on the Tool Cupboard to use this switch.",
                ["MustBeAuthedRemove"] = "You must be authorized on the Tool Cupboard to remove the switch.",
                ["SwitchRemoved"] = "Switch removed - you can now pick up the turret.",
                ["HUDVisible"] = "HUD button is now <color=#00ff00>visible</color>.",
                ["HUDHidden"] = "HUD button is now <color=#ff0000>hidden</color>. Type <color=#00ff00>/power hide</color> to show it again.",
                ["WirelessEnabled"] = "Wireless power <color=#00ff00>enabled</color> for this base.",
                ["WirelessDisabled"] = "Wireless power <color=#ff4444>disabled</color> for this base.",
                ["UI.Title"] = "POWER GRID",
                ["UI.Subtitle"] = "Wireless Power Distribution",
                ["UI.TitleSources"] = "SOURCES",
                ["UI.TitleConsumers"] = "CONSUMERS",
                ["UI.TitleRename"] = "RENAME DEVICE",
                ["UI.Footer"] = "Wireless Power",
                ["UI.StatusDisabled"] = "DISABLED",
                ["UI.StatusNoSources"] = "NO SOURCES",
                ["UI.StatusNoPower"] = "NO POWER",
                ["UI.StatusOnline"] = "ONLINE",
                ["UI.StatusPartial"] = "PARTIAL",
                ["UI.StatusOn"] = "ON",
                ["UI.StatusOff"] = "OFF",
                ["UI.Capacity"] = "Capacity:",
                ["UI.InUse"] = "In Use:",
                ["UI.Demand"] = "Demand:",
                ["UI.Status"] = "Status:",
                ["UI.Sources"] = "Sources:",
                ["UI.Active"] = "Active:",
                ["UI.Close"] = "X",
                ["UI.Back"] = "BACK",
                ["UI.ViewSources"] = "VIEW SOURCES",
                ["UI.ViewConsumers"] = "VIEW CONSUMERS",
                ["UI.Refresh"] = "REFRESH",
                ["UI.Enable"] = "ENABLE",
                ["UI.Disable"] = "DISABLE",
                ["UI.View"] = "VIEW",
                ["UI.Rename"] = "RENAME",
                ["UI.PrevPage"] = "< PREV",
                ["UI.NextPage"] = "NEXT >",
                ["UI.EnableWireless"] = "ENABLE WIRELESS POWER",
                ["UI.DisableWireless"] = "DISABLE WIRELESS POWER",
                ["UI.Clear"] = "CLEAR",
                ["UI.Save"] = "SAVE",
                ["UI.Cancel"] = "CANCEL",
                ["UI.HeaderPowerSources"] = "--- POWER SOURCES ---",
                ["UI.HeaderWaterContainers"] = "--- WATER CONTAINERS ---",
                ["UI.BatteryEmpty"] = " [EMPTY]",
                ["UI.FuelRunning"] = " [RUNNING]",
                ["UI.FuelOff"] = " [OFF]",
                ["UI.FuelNoFuel"] = " [NO FUEL]",
                ["UI.WaterEmpty"] = "EMPTY",
                ["UI.WaterAmount"] = "{0}ml",
                ["UI.NoSourcesFound"] = "No power sources found.\nPlace generators, solar panels, or batteries!",
                ["UI.NoConsumersFound"] = "No consumers found.\nPlace turrets, lights, or other electrical items!",
                ["UI.NoCategoryFound"] = "No {0} found.",
            }, this);
        }

        private string Lang(string key, string userId = null, params object[] args)
        {
            var msg = lang.GetMessage(key, this, userId);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        #endregion
    }
}
       