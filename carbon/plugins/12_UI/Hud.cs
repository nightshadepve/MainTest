// Reference: ZString

using System;
using Network;
using Facepunch;
using System.IO;
using Oxide.Core;
using UnityEngine;
using Cysharp.Text;
using UnityEngine.UI;
using Newtonsoft.Json;
using System.Reflection;
using Oxide.Core.Plugins;
using System.Collections;
using Oxide.Game.Rust.Cui;
using System.Globalization;
using JetBrains.Annotations;
using System.Collections.Generic;
#if CARBON
using Carbon.Pooling;
#endif

namespace Oxide.Plugins;

[Info("Hud", "ahigao", "3.5.2")]
internal class Hud : RustPlugin
{
    private const string PERM_STREAMER = "hud.streamer";

    private const string HELP_TEXT =
        "All commands:\n<color=yellow>/h open</color> - open ServerHud UI\n<color=yellow>/h events</color> - open Events only\n<color=yellow>/h hide</color> - hide ServerHud UI\n<color=yellow>/h close</color> - close ServerHud UI";

    private readonly ClockHandler _clockHandler = new();

    private EconomyHandler _economyHandler;
    private ImageHandler _imageHandler;
    private EventsHandler _eventsHandler;
    private SettingsMenuHandler _settingsMenuHandler;
    private HudHandler _hudHandler;

    private Configuration _config;
    private Data _data;

    [PluginReference("Economics")]
    private Plugin _economics;

    [PluginReference("ServerRewards")]
    private Plugin _serverRewards;

    [PluginReference("ShoppyStock")]
    private Plugin _shoppyStock;

    [PluginReference("ImageLibrary")]
    private Plugin _imageLibrary;

#region Settings

    private class Configuration
    {
        [JsonIgnore]
        public string JSON;

        [JsonProperty("Auto reload [If you change the config and save the file the plugin will reload itself]")]
        public readonly bool AutoReload = false;

        [JsonProperty(PropertyName = "Other images")]
        public OtherImages Images = new();

        [JsonProperty("Main setup")]
        public MainSetup MainSetup = new()
        {
            IsOverall = false,
            Size = 80,
            Logo = "https://i.ibb.co/3F3566J/logo.png",
            EventsBGOpacity = 80,
            BGOpacity = 60,
            Position = new Postition
            {
                Align = "TopLeft",
                LeftOffSet = 40,
                TopOffSet = 25
            },
            ServerName = "Your Server Name",
            ActivePlayersAppearance = new ActiveAppearance
            {
                Color = "#fff",
                Icon = "https://i.ibb.co/7vG49W1/active.png",
                IsEnable = true,
                IsAdminsCount = false
            },
            SleepPlayersAppearance = new BaseAppearance
            {
                Color = "#fff",
                Icon = "https://i.ibb.co/2q5mzmC/sleep.png",
                IsEnable = true
            },
            QueuePlayersAppearance = new BaseAppearance
            {
                Color = "#fff",
                Icon = "https://i.ibb.co/Cm65TY9/line.png",
                IsEnable = true
            },
            Time = new TimeAppearance
            {
                IsAutomaticDetection = true,
                IsAmPm = false,
                Color = "#fff",
                IsEnable = true
            },
            PlayerPosition = new PlayerPositionAppearance
            {
                IsGrid = true,
                Color = "cyan",
                IsEnable = true
            },
            Economy = new EconomyAppearance
            {
                Currency = "$",
                Plugin = "ServerRewards",
                Color = "#10ff10",
                IsEnable = false
            },
            InfoMessages = new InfoMessage
            {
                UpdateInterval = 60,
                Align = "BottomCenter",
                Width = 260,
                OffsetTB = 0,
                OffsetR = 0,
                OutlineColor = "#000",
                IsOverall = true,
                IsEnable = true,
                Messages = new List<string>
                {
                    "Welcome to Your Server Name",
                    "Good luck"
                }
            },
            AdditionalMenu = new AdditionalMenu
            {
                AutoCloseTime = 60,
                AutoCloseCommand = true,
                ButtonColor = "yellow",
                CommandsOpacity = 100,
                IsEnable = true,
                Commands = new List<CommandAppearance>
                {
                    new()
                    {
                        Command = "chat.say Hello there",
                        Icon = "https://i.ibb.co/7vG49W1/active.png",
                        Image = "https://i.ibb.co/6Z5SCrW/command.png",
                        Text = "Say Something",
                        OutlineColor = "#000",
                        IsConsole = true
                    },
                    new()
                    {
                        Command = "/shop",
                        Icon = "",
                        Image = "https://i.ibb.co/6Z5SCrW/command.png",
                        Text = "Say Something",
                        OutlineColor = "#000",
                        IsConsole = false
                    }
                }
            }
        };

        [JsonProperty("Base Events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<BaseEventsAppearance> BaseEvents = new()
        {
            new BaseEventsAppearance
            {
                Name = "Bradley",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/X8xVTjc/bradley.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "PatrolHeli",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/m6wwkhH/heli.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "CH47",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/CVCPVpN/ch47.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "Cargo",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/QmT6MFX/cargo.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "AirDrop",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/580XRGw/airdrop.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "HarborCargoArrive",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/qysRW6X/harborevent2.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "SmallOilRig",
                Color = "#fff",
                ActiveColor = "#10ff10",
                ContestedColor = "#ff1010",
                Icon = "https://i.ibb.co/cbDJj7r/harborevent3.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "LargeOilRig",
                Color = "#fff",
                ActiveColor = "#10ff10",
                ContestedColor = "#ff1010",
                Icon = "https://i.ibb.co/QPGXQHr/harborevent2.png",
                IsEnable = true
            },
            new BaseEventsAppearance
            {
                Name = "DeepSea",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/rRPSfXnh/watertreatmenticon.png",
                IsEnable = true
            }
        };

        [JsonProperty("Custom Events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<CustomEventsAppearance> CustomEvents = new()
        {
            new CustomEventsAppearance
            {
                Name = "Convoy",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/qRyD0DY/convoy.png",
                IsEnable = false,
                OnEventStart = new List<string>
                {
                    "OnConvoyStart"
                },
                OnEventEnd = new List<string>
                {
                    "OnConvoyStop"
                }
            },
            new CustomEventsAppearance
            {
                Name = "Sputnik",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/9Tkb44Z/sputnik.png",
                IsEnable = false,
                OnEventStart = new List<string>
                {
                    "OnSputnikEventStart"
                },
                OnEventEnd = new List<string>
                {
                    "OnSputnikEventStop"
                }
            },
            new CustomEventsAppearance
            {
                Name = "ArmoredTrain",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/3v3JcQ8/train.png",
                IsEnable = false,
                OnEventStart = new List<string>
                {
                    "OnArmoredTrainEventStart"
                },
                OnEventEnd = new List<string>
                {
                    "OnArmoredTrainEventStop"
                }
            },
            new CustomEventsAppearance
            {
                Name = "Harbor",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/P91x3zR/harbor.png",
                IsEnable = false,
                OnEventStart = new List<string>
                {
                    "OnHarborEventStart"
                },
                OnEventEnd = new List<string>
                {
                    "OnHarborEventEnd"
                }
            }
        };

        public BaseEventsAppearance GetEventByName(string name)
        {
            foreach (BaseEventsAppearance baseEvent in BaseEvents)
            {
                if (string.Equals(baseEvent.Name, name, StringComparison.OrdinalIgnoreCase))
                    return baseEvent;
            }

            foreach (CustomEventsAppearance customEvent in CustomEvents)
            {
                if (string.Equals(customEvent.Name, name, StringComparison.OrdinalIgnoreCase))
                    return customEvent;
            }

            return null;
        }
    }

    private class OtherImages
    {
        [JsonProperty(PropertyName = "Backgound Main")]
        public readonly string Background = "https://i.ibb.co/Zf9GxqZ/background.png";

        [JsonProperty(PropertyName = "Backgound Events")]
        public readonly string Events = "https://i.ibb.co/1fk9m74/events.png";

        [JsonProperty(PropertyName = "Lip")]
        public readonly string Lip = "https://i.ibb.co/10Gg3j5/lip.png";

        [JsonProperty(PropertyName = "Drop menu")]
        public readonly string DropMenu = "https://i.ibb.co/vqPQ3nv/dropmenu.png";

        [JsonProperty(PropertyName = "Close drop menu")]
        public readonly string CloseDropMenu = "https://i.ibb.co/PwyWzwH/closedropmenu.png";
    }

    private class Postition
    {
        [JsonProperty("Align [TopLeft | TopRight | BottomLeft | BottomRight")]
        public string Align;

        [JsonProperty("Left | Right - offset")]
        public int LeftOffSet;

        [JsonProperty("Top | Bottom - offset")]
        public int TopOffSet;

        public bool IsAlignRight()
        {
            return ContainsIgnoreCase(Align, "right");
        }

        public bool IsAlignBottom()
        {
            return ContainsIgnoreCase(Align, "bottom");
        }
    }

    private class MainSetup
    {
        [JsonProperty("Overall layer [you will see the hud in your inventory]")]
        public bool IsOverall;

        [JsonProperty("Show only active events")]
        public bool OnlyActive;

        [JsonProperty("Hide the HUD while a player is in a Computer Station (CCTV/Drone) view")]
        public readonly bool HideInComputerStation = true;

        [JsonProperty("Size ALL [0% - inf]")]
        public int Size;

        [JsonProperty("Logo [HUD interact button]")]
        public string Logo;

        [JsonProperty("Background opacity [0% - 100%]")]
        public int EventsBGOpacity;

        [JsonProperty("Events background opacity [0% - 100%]")]
        public int BGOpacity;

        [JsonProperty("Position")]
        public Postition Position;

        [JsonProperty("Server name")]
        public string ServerName;

        [JsonProperty("Active players")]
        public ActiveAppearance ActivePlayersAppearance;

        [JsonProperty("Sleep players")]
        public BaseAppearance SleepPlayersAppearance;

        [JsonProperty("Queue players")]
        public BaseAppearance QueuePlayersAppearance;

        [JsonProperty("Time")]
        public TimeAppearance Time = new();

        [JsonProperty("Player position [hide permisson - hud.streamer]")]
        public PlayerPositionAppearance PlayerPosition = new();

        [JsonProperty("Economy plugin [Economics | ServerRewards | ShoppyStock]")]
        public EconomyAppearance Economy = new();

        [JsonProperty("Second economy plugin [optional, shows a 2nd balance next to the first]")]
        public EconomyAppearance SecondEconomy = new();

        [JsonProperty("Info messages")]
        public InfoMessage InfoMessages = new();

        [JsonProperty("Additional menu")]
        public AdditionalMenu AdditionalMenu = new();
    }

    private class EconomyAppearance
    {
        [JsonProperty("Currency")]
        public string Currency;

        [JsonProperty("Value color")]
        public string Color;

        [JsonProperty("Enable")]
        public bool IsEnable;

        [JsonProperty("Which plugin use [ServerRewards | Economics | ShoppyStock]")]
        public string Plugin;

        [JsonProperty("Shop name [only for ShoppyStock - the currency/shop name]")]
        public string ShopName = "";
    }

    private class TimeAppearance
    {
        [JsonProperty("Use automatic detection of the time format for each player")]
        public bool IsAutomaticDetection;

        [JsonProperty("Use AM/PM format [If you use this turn off automatic detection]")]
        public bool IsAmPm;

        [JsonProperty("Value color")]
        public string Color;

        [JsonProperty("Enable")]
        public bool IsEnable;
    }

    private class BaseAppearance
    {
        [JsonProperty("Icon")]
        public string Icon;

        [JsonProperty("Color")]
        public string Color;

        [JsonProperty("Enable")]
        public bool IsEnable;
    }

    private class ActiveAppearance : BaseAppearance
    {
        [JsonProperty("Count admins")]
        public bool IsAdminsCount;
    }

    private class BaseEventsAppearance : BaseAppearance
    {
        [JsonIgnore]
        public bool IsActive;

        [JsonIgnore]
        public bool IsContested;

        [JsonProperty("Name")]
        public string Name;

        [JsonProperty("Active color")]
        public string ActiveColor;

        [JsonProperty("Contested color [oil rigs: crate is being hacked]")]
        public string ContestedColor;

        public string GetColor()
        {
            if (IsContested && !string.IsNullOrEmpty(ContestedColor))
                return ContestedColor;

            return IsActive ? ActiveColor : Color;
        }
    }

    private class CustomEventsAppearance : BaseEventsAppearance
    {
        [JsonProperty("Hook OnEventStart", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> OnEventStart;

        [JsonProperty("Hook OnEventStop", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> OnEventEnd;
    }

    private class AdditionalMenu
    {
        [JsonProperty("Auto close timer [seconds | 0 - disable]")]
        public int AutoCloseTime;

        [JsonProperty("Auto close after command use")]
        public bool AutoCloseCommand;

        [JsonProperty("Open/Close button color")]
        public string ButtonColor;

        [JsonProperty("Commands background opacity [0% - 100%]")]
        public int CommandsOpacity;

        [JsonProperty("Enable")]
        public bool IsEnable;

        [JsonProperty("Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<CommandAppearance> Commands = new();
    }

    private class CommandAppearance
    {
        [JsonProperty("Background image")]
        public string Image;

        [JsonProperty("Icon [optional]")]
        public string Icon;

        [JsonProperty("Command")]
        public string Command;

        [JsonProperty("Permission to see")]
        public string PermissionToSee;

        [JsonProperty("Text")]
        public string Text;

        [JsonProperty("Outline color")]
        public string OutlineColor;

        [JsonProperty("Is Console")]
        public bool IsConsole;
    }

    private class InfoMessage
    {
        [JsonProperty("Update interval [in seconds]")]
        public int UpdateInterval;

        [JsonProperty("Align [BottomCenter | TopCenter | TopRight]")]
        public string Align;

        [JsonProperty("Width [in px]")]
        public int Width;

        [JsonProperty("Offset [top | bottom]")]
        public int OffsetTB;

        [JsonProperty("Offset [Center]")]
        public int OffsetR;

        [JsonProperty("Use Outline")]
        public bool UseOutline = true;

        [JsonProperty("Outline color")]
        public string OutlineColor;

        [JsonProperty("Overall [you will see messages in your invenotory]")]
        public bool IsOverall;

        [JsonProperty("Enable")]
        public bool IsEnable;

        [JsonProperty("Messages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Messages = new();
    }

    private class PlayerPositionAppearance
    {
        [JsonProperty("Enable")]
        public bool IsEnable;

        [JsonProperty("true - grid | false - x,z coordinates")]
        public bool IsGrid;

        [JsonProperty("Color")]
        public string Color;
    }

    protected override void LoadConfig()
    {
        base.LoadConfig();
        try
        {
            _config = Config.ReadObject<Configuration>();
            if (_config == null)
                throw new Exception();
            SaveConfig();
        }
        catch
        {
            PrintError("Your configuration file contains an error. Using default configuration values.");
            LoadDefaultConfig();
        }
    }

    protected override void SaveConfig()
    {
        if (_config.AutoReload)
            _config.JSON = JsonConvert.SerializeObject(_config);

        Config.WriteObject(_config);
    }

    protected override void LoadDefaultConfig()
    {
        _config = new Configuration();
    }

    private static bool IsNullEntry<TEntry>(TEntry entry) where TEntry : class
    {
        return entry == null;
    }

    private void ValidateConfig()
    {
        _config ??= new Configuration();
        _config.Images ??= new OtherImages();
        _config.BaseEvents ??= new List<BaseEventsAppearance>();
        _config.CustomEvents ??= new List<CustomEventsAppearance>();
        _config.MainSetup ??= new MainSetup();

        _config.BaseEvents.RemoveAll(IsNullEntry);
        _config.CustomEvents.RemoveAll(IsNullEntry);

        MainSetup mainSetup = _config.MainSetup;

        mainSetup.Position ??= new Postition { Align = "TopLeft", LeftOffSet = 40, TopOffSet = 25 };
        mainSetup.ActivePlayersAppearance ??= new ActiveAppearance { Color = "#fff" };
        mainSetup.SleepPlayersAppearance ??= new BaseAppearance { Color = "#fff" };
        mainSetup.QueuePlayersAppearance ??= new BaseAppearance { Color = "#fff" };
        mainSetup.Time ??= new TimeAppearance { Color = "#fff" };
        mainSetup.PlayerPosition ??= new PlayerPositionAppearance { Color = "cyan" };
        mainSetup.Economy ??= new EconomyAppearance();
        mainSetup.SecondEconomy ??= new EconomyAppearance();
        mainSetup.InfoMessages ??= new InfoMessage();
        mainSetup.InfoMessages.Messages ??= new List<string>();
        mainSetup.AdditionalMenu ??= new AdditionalMenu();
        mainSetup.AdditionalMenu.Commands ??= new List<CommandAppearance>();
        mainSetup.AdditionalMenu.Commands.RemoveAll(IsNullEntry);

        foreach (CustomEventsAppearance customEvent in _config.CustomEvents)
        {
            if (customEvent == null)
                continue;

            customEvent.OnEventStart ??= new List<string>();
            customEvent.OnEventEnd ??= new List<string>();
        }

        SaveConfig();
    }

    private static readonly string[] s_oilRigEventNames = { "SmallOilRig", "LargeOilRig" };

    private void MigrateEventDefaults()
    {
        if (_config.GetEventByName("HarborCargoArrive") == null)
            _config.BaseEvents.Add(new BaseEventsAppearance
            {
                Name = "HarborCargoArrive",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/qysRW6X/harborevent2.png",
                IsEnable = true
            });

        if (_config.GetEventByName("SmallOilRig") == null)
            _config.BaseEvents.Add(new BaseEventsAppearance
            {
                Name = "SmallOilRig",
                Color = "#fff",
                ActiveColor = "#10ff10",
                ContestedColor = "#ff1010",
                Icon = "https://i.ibb.co/cbDJj7r/harborevent3.png",
                IsEnable = true
            });

        if (_config.GetEventByName("LargeOilRig") == null)
            _config.BaseEvents.Add(new BaseEventsAppearance
            {
                Name = "LargeOilRig",
                Color = "#fff",
                ActiveColor = "#10ff10",
                ContestedColor = "#ff1010",
                Icon = "https://i.ibb.co/QPGXQHr/harborevent2.png",
                IsEnable = true
            });

        foreach (string oilRigName in s_oilRigEventNames)
        {
            BaseEventsAppearance oilRigEvent = _config.GetEventByName(oilRigName);
            if (oilRigEvent != null && string.IsNullOrEmpty(oilRigEvent.ContestedColor))
                oilRigEvent.ContestedColor = "#ff1010";
        }

        if (_config.GetEventByName("DeepSea") == null)
            _config.BaseEvents.Add(new BaseEventsAppearance
            {
                Name = "DeepSea",
                Color = "#fff",
                ActiveColor = "#10ff10",
                Icon = "https://i.ibb.co/rRPSfXnh/watertreatmenticon.png",
                IsEnable = true
            });
    }

    private Coroutine _configCheckCoroutine;

    private IEnumerator ConfigCheck()
    {
        var wait = new WaitForSeconds(2f);
        string configPath = Path.Combine(Manager.ConfigPath, Name + ".json");
        string reportedError = null;
        DateTime lastWriteTime = File.GetLastWriteTimeUtc(configPath);

        while (true)
        {
            if (!IsLoaded)
                yield break;

            DateTime writeTime = File.GetLastWriteTimeUtc(configPath);

            if (writeTime != lastWriteTime)
            {
                lastWriteTime = writeTime;
                reportedError = ApplyExternalConfigChange(configPath, reportedError);
            }

            yield return wait;
        }
    }

    private string ApplyExternalConfigChange(string configPath, string reportedError)
    {
        Configuration checkConfig;

        try
        {
            checkConfig = Config.ReadObject<Configuration>();
        }
        catch
        {
            checkConfig = null;
        }

        if (checkConfig == null)
            return ReportConfigError(configPath, reportedError);

        if (JsonConvert.SerializeObject(checkConfig) != _config.JSON)
            Interface.Oxide.ReloadPlugin(Name);

        return null;
    }

    private string ReportConfigError(string configPath, string reportedError)
    {
        string content = File.ReadAllText(configPath);

        if (content == reportedError)
            return reportedError;

        PrintError("Your configuration file contains an error. Using default configuration values.");

        return content;
    }

#endregion

#region Data

    private class Data
    {
        [JsonProperty("Players' state hud", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public readonly Dictionary<string, States> PlayersState = new();

        [JsonProperty(PropertyName = "First Run")]
        public bool FirstRun = true;
    }

    private enum States
    {
        FULL,
        EVENTS,
        HIDE,
        CLOSE
    }

    private void LoadData()
    {
        _data = Interface.Oxide.DataFileSystem.ExistsDatafile($"{Name}/data")
            ? Interface.Oxide.DataFileSystem.ReadObject<Data>($"{Name}/data")
            : new Data();
    }

    private void SaveData()
    {
        if (_data != null)
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/data", _data);
    }

#endregion

#region Hooks

#if CARBON
    public override object InternalCallHook(uint hook, object[] args)
    {
        if (_eventsHandler != null && _eventsHandler.HandleCustomHook(HookStringPool.GetOrAdd(hook)))
            return null;

        if (HookPool == null || !HookPool.TryGetValue(hook, out CachedHookInstance instance) || !instance.IsValid())
            return null;

        for (int index = 0; index < instance.Hooks.Count; index++)
        {
            CachedHook cachedHook = instance.Hooks[index];

            if (!AcceptsArguments(cachedHook, args))
                continue;

            try
            {
                object result = cachedHook.Method.Invoke(this, args);

                if (result != null)
                    return result;
            }
            catch (Exception exception)
            {
                PrintError($"Failed to call hook '{cachedHook.Name}': {exception.InnerException ?? exception}");
                OnException(hook);
            }
        }

        return null;
    }
#else
    protected override object InvokeMethod(HookMethod method, object[] args)
    {
        if (_eventsHandler != null && _eventsHandler.HandleCustomHook(method.Name))
            return null;

        return base.InvokeMethod(method, args);
    }
#endif

    [UsedImplicitly]
    [HookMethod(nameof(Init))]
    private void Init()
    {
        _economyHandler = new EconomyHandler(this);
        _imageHandler = new ImageHandler(this);
        _eventsHandler = new EventsHandler(this);
        _settingsMenuHandler = new SettingsMenuHandler(this);
        _hudHandler = new HudHandler(this);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnServerInitialized))]
    private void OnServerInitialized()
    {
        ValidateConfig();

        if (_config.AutoReload)
            _configCheckCoroutine = ServerMgr.Instance.StartCoroutine(ConfigCheck());

        MigrateEventDefaults();
        SaveConfig();

        _imageHandler.LoadImages();
        LoadData();

        RegisterCommandPermissions();

        _eventsHandler.RefreshCustomHooks();
        _eventsHandler.Initialize();

        if (_data.FirstRun)
            timer.In(5, ReloadAfterFirstRun);

        permission.RegisterPermission(PERM_STREAMER, this);

        UI.SetSize(_config.MainSetup.Size / 100f);

        RefreshHookSubscriptions();

        _hudHandler.Initialize();
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnServerSave))]
    private void OnServerSave()
    {
        SaveData();
    }

    [UsedImplicitly]
    [HookMethod(nameof(Unload))]
    private void Unload()
    {
        SaveData();

        StopServerCoroutine(ref _configCheckCoroutine);

        _hudHandler.Shutdown();
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnPlayerConnected))]
    private void OnPlayerConnected(BasePlayer player)
    {
        _hudHandler.HandlePlayerConnected(player);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnConnectionQueue))]
    private void OnConnectionQueue(Connection connection)
    {
        _hudHandler.HandlePlayersCountChanged();
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnPlayerSleep))]
    private void OnPlayerSleep(BasePlayer player)
    {
        _hudHandler.HandlePlayersCountChanged();
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnPlayerDisconnected))]
    private void OnPlayerDisconnected(BasePlayer player)
    {
        _hudHandler.HandlePlayerDisconnected(player);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnPlayerSleepEnded))]
    private void OnPlayerSleepEnded(BasePlayer player)
    {
        _hudHandler.HandlePlayersCountChanged();
    }

#region ComputerStation

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityMounted))]
    private void OnEntityMounted(ComputerStation entity, BasePlayer player)
    {
        if (entity == null || player == null)
            return;

        _hudHandler.HandleComputerStationMounted(player);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityDismounted))]
    private void OnEntityDismounted(ComputerStation entity, BasePlayer player)
    {
        _hudHandler.HandleComputerStationDismounted(player);
    }

#endregion

#region Events

    [UsedImplicitly]
    [HookMethod(nameof(OnEntitySpawned))]
    private void OnEntitySpawned(BradleyAPC entity)
    {
        _eventsHandler.HandleSpawned(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityKill))]
    private void OnEntityKill(BradleyAPC entity)
    {
        _eventsHandler.HandleKilled(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntitySpawned))]
    private void OnEntitySpawned(PatrolHelicopter entity)
    {
        _eventsHandler.HandleSpawned(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityKill))]
    private void OnEntityKill(PatrolHelicopter entity)
    {
        _eventsHandler.HandleKilled(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntitySpawned))]
    private void OnEntitySpawned(CH47Helicopter entity)
    {
        _eventsHandler.HandleSpawned(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityKill))]
    private void OnEntityKill(CH47Helicopter entity)
    {
        _eventsHandler.HandleKilled(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntitySpawned))]
    private void OnEntitySpawned(CargoShip entity)
    {
        _eventsHandler.HandleSpawned(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityKill))]
    private void OnEntityKill(CargoShip entity)
    {
        _eventsHandler.HandleKilled(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntitySpawned))]
    private void OnEntitySpawned(SupplyDrop entity)
    {
        _eventsHandler.HandleSpawned(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityKill))]
    private void OnEntityKill(SupplyDrop entity)
    {
        _eventsHandler.HandleKilled(entity);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntitySpawned))]
    private void OnEntitySpawned(HackableLockedCrate crate)
    {
        _eventsHandler.HandleCrateSpawned(crate);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnEntityKill))]
    private void OnEntityKill(HackableLockedCrate crate)
    {
        _eventsHandler.HandleCrateKilled(crate);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnCrateHack))]
    private void OnCrateHack(HackableLockedCrate crate)
    {
        _eventsHandler.HandleCrateHackChanged(crate, true);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnCrateHackEnd))]
    private void OnCrateHackEnd(HackableLockedCrate crate)
    {
        _eventsHandler.HandleCrateHackChanged(crate, false);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnCargoShipHarborArrived))]
    private void OnCargoShipHarborArrived(CargoShip entity)
    {
        _eventsHandler.HandleHarborArrivalChanged(true);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnCargoShipHarborLeave))]
    private void OnCargoShipHarborLeave(CargoShip entity)
    {
        _eventsHandler.HandleHarborArrivalChanged(false);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnDeepSeaOpened))]
    private void OnDeepSeaOpened(DeepSeaManager deepSeaManager)
    {
        _eventsHandler.HandleDeepSeaChanged(true);
    }

    [UsedImplicitly]
    [HookMethod(nameof(OnDeepSeaClosed))]
    private void OnDeepSeaClosed(DeepSeaManager deepSeaManager)
    {
        _eventsHandler.HandleDeepSeaChanged(false);
    }

#endregion

#endregion

#region Commands

    [UsedImplicitly]
    [ConsoleCommand("UI_H")]
    private void HandleConsoleCommand(ConsoleSystem.Arg arg)
    {
        if (!arg.HasArgs())
            return;

        BasePlayer player = arg.Player();

        switch (arg.GetString(0))
        {
            case "CHANGESTATE":
                _hudHandler.ToggleState(player);
                break;
            case "CHANGEADMENUSTATE":
                _hudHandler.ToggleAdditionalMenu(player, arg.GetBool(1));
                break;
            case "USECOMMAND":
                _hudHandler.RunMenuCommand(player, arg.GetBool(1), JoinArguments(arg.Args, 2));
                break;
            case "s":
                _settingsMenuHandler.HandleCommand(player, arg);
                break;
        }
    }

    [UsedImplicitly]
    [ChatCommand("h")]
    private void HandleChatCommand(BasePlayer player, string command, string[] args)
    {
        if (player == null)
            return;

        if (args.Length != 1)
        {
            SendReply(player, HELP_TEXT);
            return;
        }

        if (args[0] == "setup")
        {
            if (player.IsAdmin)
                _settingsMenuHandler.Open(player);

            return;
        }

        _hudHandler.HandleStateCommand(player, args[0]);
    }

#endregion

#region Methods

    private static bool ContainsIgnoreCase(string source, string value)
    {
        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void StopServerCoroutine(ref Coroutine coroutine)
    {
        if (coroutine == null)
            return;

        if (ServerMgr.Instance != null)
            ServerMgr.Instance.StopCoroutine(coroutine);

        coroutine = null;
    }

    private static string JoinArguments(StringView[] arguments, int startIndex)
    {
        if (arguments == null || startIndex >= arguments.Length)
            return string.Empty;

        using Utf16ValueStringBuilder builder = ZString.CreateStringBuilder();

        for (int index = startIndex; index < arguments.Length; index++)
        {
            if (index > startIndex)
                builder.Append(' ');

            builder.Append(arguments[index].ToString());
        }

        return builder.ToString();
    }

#if CARBON
    private static bool AcceptsArguments(CachedHook hook, object[] args)
    {
        int count = args == null ? 0 : args.Length;

        if (hook.Parameters.Length != count)
            return false;

        for (int index = 0; index < count; index++)
        {
            object argument = args[index];

            if (argument != null && !hook.Parameters[index].IsInstanceOfType(argument))
                return false;
        }

        return true;
    }

    private void RegisterCustomHook(string hookName)
    {
        uint hookId = HookStringPool.GetOrAdd(hookName);

        if (!HookPool.ContainsKey(hookId))
            HookPool.Add(hookId, new CachedHookInstance { Hooks = new List<CachedHook>() });

        Subscribe(hookName);
    }
#else
    private static readonly MethodInfo s_customHookTarget =
        typeof(Hud).GetMethod(nameof(OnCustomEventHook), BindingFlags.Instance | BindingFlags.NonPublic);

    private void OnCustomEventHook()
    {
        PrintError("A custom event hook was dispatched without a resolvable name and has been ignored.");
    }

    private void RegisterCustomHook(string hookName)
    {
        if (!Hooks.ContainsKey(hookName))
            Hooks[hookName] = new List<HookMethod> { new(s_customHookTarget) { Name = hookName } };

        Subscribe(hookName);
    }
#endif

    private void ReloadAfterFirstRun()
    {
        _data.FirstRun = false;

        Interface.Oxide.ReloadPlugin("ImageLibrary");
        Interface.Oxide.ReloadPlugin("Hud");
    }

    [UsedImplicitly]
    private string API_PlayerHudState(string id)
    {
        return _data.PlayersState[id].ToString();
    }

    private void RegisterCommandPermissions()
    {
        foreach (CommandAppearance command in _config.MainSetup.AdditionalMenu.Commands)
        {
            if (string.IsNullOrEmpty(command.PermissionToSee))
                continue;

            permission.RegisterPermission(command.PermissionToSee, this);
        }
    }

    private void RefreshHookSubscriptions()
    {
        bool isSleepEnabled = _config.MainSetup.SleepPlayersAppearance.IsEnable;

        SetHookSubscription(nameof(OnConnectionQueue), _config.MainSetup.QueuePlayersAppearance.IsEnable);
        SetHookSubscription(nameof(OnPlayerSleep), isSleepEnabled);
        SetHookSubscription(nameof(OnPlayerSleepEnded), isSleepEnabled);
    }

    private void SetHookSubscription(string hookName, bool isEnabled)
    {
        if (isEnabled)
        {
            Subscribe(hookName);
            return;
        }

        Unsubscribe(hookName);
    }

#endregion

#region Handlers

    private class ClockHandler
    {
        private readonly Dictionary<string, CultureInfo> _cachedCultures = new();
        private readonly Dictionary<string, string> _cachedTimeTexts = new();

        private long _cachedClockMinute = long.MinValue;

        public CultureInfo GetCulture(string name)
        {
            if (_cachedCultures.TryGetValue(name, out CultureInfo cultureInfo))
                return cultureInfo;

            cultureInfo = FindCulture(name);
            _cachedCultures.TryAdd(name, cultureInfo);

            return cultureInfo;
        }

        public string GetClockText(string pattern)
        {
            DateTime now = TOD_Sky.Instance.Cycle.DateTime;
            long minute = now.Ticks / TimeSpan.TicksPerMinute;

            if (minute != _cachedClockMinute)
            {
                _cachedClockMinute = minute;
                _cachedTimeTexts.Clear();
            }

            if (_cachedTimeTexts.TryGetValue(pattern, out string text))
                return text;

            text = now.ToString(pattern);
            _cachedTimeTexts[pattern] = text;

            return text;
        }

#region Methods

        private static CultureInfo FindCulture(string name)
        {
            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
            {
                if (string.Equals(culture.Name, name, StringComparison.OrdinalIgnoreCase))
                    return CultureInfo.GetCultureInfo(name);
            }

            return CultureInfo.GetCultureInfo("en");
        }

#endregion
    }

    private class EconomyHandler
    {
        private readonly Hud _plugin;
        private readonly HashSet<string> _warnedMissingEconomies = new();

        public EconomyHandler(Hud plugin)
        {
            _plugin = plugin;
        }

        public bool IsAnyEnabled()
        {
            MainSetup mainSetup = _plugin._config.MainSetup;

            return (mainSetup.Economy?.IsEnable ?? false) || (mainSetup.SecondEconomy?.IsEnable ?? false);
        }

        public int CountEnabled()
        {
            MainSetup mainSetup = _plugin._config.MainSetup;
            int enabledCount = 0;

            if (mainSetup.Economy != null && mainSetup.Economy.IsEnable)
                ++enabledCount;

            if (mainSetup.SecondEconomy != null && mainSetup.SecondEconomy.IsEnable)
                ++enabledCount;

            return enabledCount;
        }

        public EconomyAppearance GetEnabled(int index)
        {
            MainSetup mainSetup = _plugin._config.MainSetup;
            int enabledCount = 0;

            if (mainSetup.Economy != null && mainSetup.Economy.IsEnable)
            {
                if (enabledCount == index)
                    return mainSetup.Economy;

                ++enabledCount;
            }

            if (mainSetup.SecondEconomy != null && mainSetup.SecondEconomy.IsEnable)
                if (enabledCount == index)
                    return mainSetup.SecondEconomy;

            return null;
        }

        public int GetBalance(EconomyAppearance economy, BasePlayer player)
        {
            if (economy == null || player == null)
                return 0;

            if (string.Equals(economy.Plugin, "serverrewards", StringComparison.OrdinalIgnoreCase))
            {
                if (IsMissing(_plugin._serverRewards, "ServerRewards"))
                    return 0;

                return _plugin._serverRewards.Call<int>("CheckPoints", player.UserIDString);
            }

            if (string.Equals(economy.Plugin, "shoppystock", StringComparison.OrdinalIgnoreCase))
            {
                if (IsMissing(_plugin._shoppyStock, "ShoppyStock"))
                    return 0;

                return _plugin._shoppyStock.Call<int>("GetCurrencyAmount", economy.ShopName ?? string.Empty, player);
            }

            if (IsMissing(_plugin._economics, "Economics"))
                return 0;

            return (int)_plugin._economics.Call<double>("Balance", player.UserIDString);
        }

#region Methods

        private bool IsMissing(Plugin economyPlugin, string name)
        {
            if (economyPlugin != null && economyPlugin.IsLoaded)
                return false;

            if (_warnedMissingEconomies.Add(name))
                _plugin.PrintWarning($"An economy is enabled in the config with plugin '{name}', but that plugin is not loaded - the balance will stay at 0.");

            return true;
        }

#endregion
    }

    private class ImageHandler
    {
        private static readonly Dictionary<string, string> s_legacyLinks = new()
        {
            ["https://www.dropbox.com/scl/fi/9j4fv475el3gfbq9wvoox/logo.png?rlkey=rq710tuayr8syuk54fljluqqa&dl=1"] = "https://i.ibb.co/3F3566J/logo.png",
            ["https://www.dropbox.com/scl/fi/weax6ug55o0u4f87bhchs/active.png?rlkey=lbpuhpqs7tutpgv1i4t3b1107&dl=1"] = "https://i.ibb.co/7vG49W1/active.png",
            ["https://www.dropbox.com/scl/fi/3d76i9rg4i34zprgekr3j/sleep.png?rlkey=s57ehdeoc16h58ace6s5yuhcl&dl=1"] = "https://i.ibb.co/2q5mzmC/sleep.png",
            ["https://www.dropbox.com/scl/fi/aneas55vx6pnrwmg8bh1z/line.png?rlkey=kwmlqihaopckijm98ta0qn9p2&dl=1"] = "https://i.ibb.co/Cm65TY9/line.png",
            ["https://www.dropbox.com/scl/fi/ypbnnbs07i2t322x1pu4c/command.png?rlkey=a693i43bn70wiy2p0v0216uw7&dl=1"] = "https://i.ibb.co/6Z5SCrW/command.png",
            ["https://www.dropbox.com/scl/fi/i7531lubr8jy62w6zugkt/bradley.png?rlkey=fpnpmqndjgt63hamyrd0jzyc2&dl=1"] = "https://i.ibb.co/X8xVTjc/bradley.png",
            ["https://www.dropbox.com/scl/fi/140cku9l167y8i778br2i/heli.png?rlkey=25wxs16n0s9dk8uea7bn0p7sd&dl=1"] = "https://i.ibb.co/m6wwkhH/heli.png",
            ["https://www.dropbox.com/scl/fi/a0cuylwwtrwqrxr933jpd/ch47.png?rlkey=w9rv4ap58hmr3fwtkp4et4dn1&dl=1"] = "https://i.ibb.co/CVCPVpN/ch47.png",
            ["https://www.dropbox.com/scl/fi/lt4b833tybzzyas7bxst0/cargo.png?rlkey=i0xn03ctyz93a4rbybol2x5c2&dl=1"] = "https://i.ibb.co/QmT6MFX/cargo.png",
            ["https://www.dropbox.com/scl/fi/udt3221xk7yk2zwqdd7id/airdrop.png?rlkey=7642d4ye3n63rby8nz0ej9ayr&dl=1"] = "https://i.ibb.co/580XRGw/airdrop.png",
            ["https://www.dropbox.com/scl/fi/s2ovq1yo8umvkn213cno0/convoy.png?rlkey=9mxlg4ypbypk5be59b57qgck3&dl=1"] = "https://i.ibb.co/qRyD0DY/convoy.png",
            ["https://www.dropbox.com/scl/fi/tlys9ulw51i74fpm6oec0/sputnik.png?rlkey=oe27iqf5ali445c90mgmcilea&dl=1"] = "https://i.ibb.co/9Tkb44Z/sputnik.png",
            ["https://www.dropbox.com/scl/fi/d78jg4yrer0vqdspkxpg2/train.png?rlkey=9m4xll5wv6qrr0u2kb3pd2o4z&dl=1"] = "https://i.ibb.co/3v3JcQ8/train.png",
            ["https://www.dropbox.com/scl/fi/3qzyayoa4dxog2t8gyvzw/harbor.png?rlkey=v7jnpk8tyngwywvhbgrfhk5a4&dl=1"] = "https://i.ibb.co/P91x3zR/harbor.png"
        };

        private const int MAX_LIBRARY_CHECKS = 5;
        private const int MAX_IDLE_CHECKS = 3;
        private const float REFRESH_INTERVAL = 5f;

        private readonly Hud _plugin;
        private readonly HashSet<string> _addedImages = new();
        private readonly HashSet<string> _pendingImages = new();

        private Timer _refreshTimer;
        private int _libraryChecks;
        private int _idleChecks;
        private bool _hasStoredImages;

        public ImageHandler(Hud plugin)
        {
            _plugin = plugin;
        }

        public string GetImage(string url)
        {
            string image = _plugin._imageLibrary?.Call<string>("GetImage", url);

            return image ?? url;
        }

        public void LoadImages()
        {
            if (!_plugin._imageLibrary)
            {
                EnsureLibraryLoaded();
                return;
            }

            ReplaceLegacyLinks();
            RegisterConfiguredImages();
        }

        public void RegisterConfiguredImages()
        {
            Configuration config = _plugin._config;

            AddImage(config.Images.Background);
            AddImage(config.Images.Events);

            AddImage(config.Images.Lip);
            AddImage(config.Images.DropMenu);
            AddImage(config.Images.CloseDropMenu);

            MainSetup mainSetup = config.MainSetup;

            AddImage(mainSetup.Logo);
            AddImage(mainSetup.ActivePlayersAppearance.Icon);
            AddImage(mainSetup.SleepPlayersAppearance.Icon);
            AddImage(mainSetup.QueuePlayersAppearance.Icon);

            foreach (CommandAppearance command in mainSetup.AdditionalMenu.Commands)
            {
                AddImage(command.Image);
                AddImage(command.Icon);
            }

            foreach (BaseEventsAppearance baseEvent in config.BaseEvents)
            {
                AddImage(baseEvent.Icon);
            }

            foreach (CustomEventsAppearance customEvent in config.CustomEvents)
            {
                AddImage(customEvent.Icon);
            }

            RefreshWhenReady();
        }

#region Methods

        private void AddImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !_addedImages.Add(url))
                return;

            if (!_plugin._imageLibrary || IsImageStored(url))
                return;

            _pendingImages.Add(url);
            _plugin._imageLibrary.Call("AddImage", url, url, 0UL);
        }

        private void RefreshWhenReady()
        {
            if (_pendingImages.Count == 0)
                return;

            _idleChecks = 0;
            _hasStoredImages = false;

            StopRefresh();
            _refreshTimer = _plugin.timer.Every(REFRESH_INTERVAL, RefreshPendingImages);
        }

        private void StopRefresh()
        {
            _refreshTimer?.Destroy();
            _refreshTimer = null;
        }

        private void RefreshPendingImages()
        {
            if (!_plugin._imageLibrary)
                return;

            if (_pendingImages.RemoveWhere(IsImageStored) != 0)
                _hasStoredImages = true;

            if (_pendingImages.Count != 0)
            {
                if (!_plugin._imageLibrary.Call<bool>("IsReady"))
                    return;

                if (_idleChecks < MAX_IDLE_CHECKS)
                {
                    _idleChecks++;
                    RequeuePendingImages();
                    return;
                }

                _plugin.PrintWarning($"ImageLibrary was unable to store these images, they will stay invisible until the links are fixed: {string.Join(", ", _pendingImages)}");
            }

            StopRefresh();

            if (!_hasStoredImages)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                _plugin._hudHandler.HandlePlayerConnected(player);
            }
        }

        private void RequeuePendingImages()
        {
            foreach (string url in _pendingImages)
            {
                _plugin._imageLibrary.Call("AddImage", url, url, 0UL);
            }
        }

        private bool IsImageStored(string url)
        {
            return _plugin._imageLibrary.Call<bool>("HasImage", url, 0UL);
        }

        private void EnsureLibraryLoaded()
        {
            if (_plugin._imageLibrary)
            {
                LoadImages();
                return;
            }

            if (_libraryChecks == MAX_LIBRARY_CHECKS)
            {
                _plugin.PrintError("ImageLibrary not found. You can download the plugin here - https://umod.org/plugins/image-library.");
                return;
            }

            _libraryChecks++;
            _plugin.timer.In(1, EnsureLibraryLoaded);
        }

        private void ReplaceLegacyLinks()
        {
            MainSetup mainSetup = _plugin._config.MainSetup;

            mainSetup.Logo = ReplaceLegacyLink(mainSetup.Logo);
            mainSetup.SleepPlayersAppearance.Icon = ReplaceLegacyLink(mainSetup.SleepPlayersAppearance.Icon);
            mainSetup.QueuePlayersAppearance.Icon = ReplaceLegacyLink(mainSetup.QueuePlayersAppearance.Icon);
            mainSetup.ActivePlayersAppearance.Icon = ReplaceLegacyLink(mainSetup.ActivePlayersAppearance.Icon);

            foreach (CommandAppearance command in mainSetup.AdditionalMenu.Commands)
            {
                command.Icon = ReplaceLegacyLink(command.Icon);
                command.Image = ReplaceLegacyLink(command.Image);
            }

            foreach (BaseEventsAppearance baseEvent in _plugin._config.BaseEvents)
            {
                baseEvent.Icon = ReplaceLegacyLink(baseEvent.Icon);
            }

            foreach (CustomEventsAppearance customEvent in _plugin._config.CustomEvents)
            {
                customEvent.Icon = ReplaceLegacyLink(customEvent.Icon);
            }

            _plugin.SaveConfig();
        }

        private static string ReplaceLegacyLink(string url)
        {
            string replaced = url;

            foreach (KeyValuePair<string, string> legacyLink in s_legacyLinks)
            {
                replaced = replaced.Replace(legacyLink.Key, legacyLink.Value);
            }

            return replaced;
        }

#endregion
    }

    private class EventsHandler
    {
        private const int ICON_WIDTH = 34;
        private const int OIL_RIG_RADIUS = 50;

        private readonly struct CustomHookBinding
        {
            public readonly CustomEventsAppearance Event;
            public readonly bool IsStart;

            public CustomHookBinding(CustomEventsAppearance customEvent, bool isStart)
            {
                Event = customEvent;
                IsStart = isStart;
            }
        }

        private readonly Hud _plugin;
        private readonly Action _processTouchedCallback;

        private readonly Dictionary<string, CustomHookBinding> _customHooks = new();
        private readonly List<BaseEntity> _touchedEntities = new();

        private readonly HashSet<BaseEntity> _bradleys = new();
        private readonly HashSet<BaseEntity> _patrolHelicopters = new();
        private readonly HashSet<BaseEntity> _ch47Helicopters = new();
        private readonly HashSet<BaseEntity> _supplyDrops = new();
        private readonly HashSet<CargoShip> _cargoShips = new();
        private readonly HashSet<HackableLockedCrate> _hackableCrates = new();

        private int _enabledCount, _startPosX;
        private bool _isTouchQueued;
        private Vector3 _largeOilRigPosition, _smallOilRigPosition;

        public int EnabledCount => _enabledCount;
        public int StartPosX => _startPosX;

        public EventsHandler(Hud plugin)
        {
            _plugin = plugin;
            _processTouchedCallback = ProcessTouchedEntities;
        }

        public void Initialize()
        {
            FindOilRigPositions();
            CalculateWidth();

            _touchedEntities.Clear();
            _bradleys.Clear();
            _patrolHelicopters.Clear();
            _ch47Helicopters.Clear();
            _supplyDrops.Clear();
            _cargoShips.Clear();
            _hackableCrates.Clear();

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                var entity = networkable as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                    continue;

                var crate = entity as HackableLockedCrate;
                if (crate != null)
                {
                    _hackableCrates.Add(crate);

                    string name = GetOilRigEventName(crate);
                    if (name == null)
                        continue;

                    SetActive(name, true);
                    SetContested(name, crate.IsBeingHacked());
                    continue;
                }

                if (entity.OwnerID != 0)
                    continue;

                if (entity is BradleyAPC)
                    _bradleys.Add(entity);

                else if (entity is PatrolHelicopter)
                    _patrolHelicopters.Add(entity);

                else if (entity is CH47Helicopter)
                    _ch47Helicopters.Add(entity);

                else if (entity is CargoShip cargoShip)
                    _cargoShips.Add(cargoShip);

                else if (entity is SupplyDrop)
                    _supplyDrops.Add(entity);
            }

            if (_bradleys.Count > 0)
                SetActive("Bradley", true);

            if (_patrolHelicopters.Count > 0)
                SetActive("PatrolHeli", true);

            if (_ch47Helicopters.Count > 0)
                SetActive("CH47", true);

            if (_supplyDrops.Count > 0)
                SetActive("AirDrop", true);

            if (_cargoShips.Count > 0)
            {
                SetActive("Cargo", HasCargoShip(false));
                SetActive("HarborCargoArrive", HasCargoShip(true));
            }
        }

        public void CalculateWidth()
        {
            Configuration config = _plugin._config;
            bool onlyActive = config.MainSetup.OnlyActive;

            _startPosX = 2;

            foreach (BaseEventsAppearance baseEvent in config.BaseEvents)
            {
                if (baseEvent.IsEnable && (!onlyActive || baseEvent.IsActive))
                    _startPosX -= ICON_WIDTH / 2;
            }

            foreach (CustomEventsAppearance customEvent in config.CustomEvents)
            {
                if (customEvent.IsEnable && (!onlyActive || customEvent.IsActive))
                    _startPosX -= ICON_WIDTH / 2;
            }

            _enabledCount = Math.Abs(_startPosX - 2) / (ICON_WIDTH / 2);

            if (_enabledCount > 5)
                _startPosX += config.MainSetup.Position.IsAlignRight() ? -4 : 14;
        }

        public void RefreshCustomHooks()
        {
            _customHooks.Clear();

            foreach (CustomEventsAppearance customEvent in _plugin._config.CustomEvents)
            {
                if (!customEvent.IsEnable)
                    continue;

                MapHooks(customEvent, customEvent.OnEventStart, true);
                MapHooks(customEvent, customEvent.OnEventEnd, false);
            }

            foreach (string hookName in _customHooks.Keys)
            {
                _plugin.RegisterCustomHook(hookName);
            }
        }

        public bool HandleCustomHook(string name)
        {
            if (!_customHooks.TryGetValue(name, out CustomHookBinding binding))
                return false;

            binding.Event.IsActive = binding.IsStart;

            _plugin._hudHandler.ShowEvents();

            return true;
        }

        public void HandleSpawned(BradleyAPC entity)
        {
            _bradleys.Add(entity);
            Touch(entity);
        }

        public void HandleKilled(BradleyAPC entity)
        {
            _bradleys.Remove(entity);
            Touch(entity);
        }

        public void HandleSpawned(PatrolHelicopter entity)
        {
            _patrolHelicopters.Add(entity);
            Touch(entity);
        }

        public void HandleKilled(PatrolHelicopter entity)
        {
            _patrolHelicopters.Remove(entity);
            Touch(entity);
        }

        public void HandleSpawned(CH47Helicopter entity)
        {
            _ch47Helicopters.Add(entity);
            Touch(entity);
        }

        public void HandleKilled(CH47Helicopter entity)
        {
            _ch47Helicopters.Remove(entity);
            Touch(entity);
        }

        public void HandleSpawned(CargoShip entity)
        {
            _cargoShips.Add(entity);
            Touch(entity);
        }

        public void HandleKilled(CargoShip entity)
        {
            _cargoShips.Remove(entity);
            Touch(entity);
        }

        public void HandleSpawned(SupplyDrop entity)
        {
            _supplyDrops.Add(entity);
            Touch(entity);
        }

        public void HandleKilled(SupplyDrop entity)
        {
            _supplyDrops.Remove(entity);
            Touch(entity);
        }

        public void HandleHarborArrivalChanged(bool arrived)
        {
            _plugin._config.GetEventByName("HarborCargoArrive").IsActive = arrived;

            _plugin.NextTick(() =>
            {
                SetActive("Cargo", HasCargoShip(false));
                _plugin._hudHandler.ShowEvents();
            });
        }

        public void HandleDeepSeaChanged(bool opened)
        {
            SetActive("DeepSea", opened);
            _plugin._hudHandler.ShowEvents();
        }

        public void HandleCrateSpawned(HackableLockedCrate crate)
        {
            _hackableCrates.Add(crate);

            _plugin.NextTick(() =>
            {
                string name = GetOilRigEventName(crate);
                if (name == null)
                    return;

                SetActive(name, true);
                SetContested(name, crate.IsBeingHacked());
                _plugin._hudHandler.ShowEvents();
            });
        }

        public void HandleCrateHackChanged(HackableLockedCrate crate, bool hacking)
        {
            string name = GetOilRigEventName(crate);
            if (name == null)
                return;

            if (hacking)
                SetActive(name, true);

            SetContested(name, hacking);
            _plugin._hudHandler.ShowEvents();
        }

        public void HandleCrateKilled(HackableLockedCrate crate)
        {
            _hackableCrates.Remove(crate);

            string name = GetOilRigEventName(crate);
            if (name == null)
                return;

            _plugin.NextTick(() =>
            {
                bool stillPresent = false;

                foreach (HackableLockedCrate otherCrate in _hackableCrates)
                {
                    if (otherCrate == crate || GetOilRigEventName(otherCrate) != name)
                        continue;

                    stillPresent = true;
                    break;
                }

                SetActive(name, stillPresent);
                SetContested(name, false);
                _plugin._hudHandler.ShowEvents();
            });
        }

#region Methods

        private void MapHooks(CustomEventsAppearance customEvent, List<string> hookNames, bool isStart)
        {
            foreach (string hookName in hookNames)
            {
                if (string.IsNullOrEmpty(hookName))
                    continue;

                _customHooks.TryAdd(hookName, new CustomHookBinding(customEvent, isStart));
            }
        }

        private void FindOilRigPositions()
        {
            if (TerrainMeta.Path?.Monuments == null)
                return;

            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null)
                    continue;

                if (monument.name == "assets/bundled/prefabs/autospawn/monument/offshore/oilrig_1.prefab")
                    _largeOilRigPosition = monument.transform.position;

                else if (monument.name == "assets/bundled/prefabs/autospawn/monument/offshore/oilrig_2.prefab")
                    _smallOilRigPosition = monument.transform.position;
            }
        }

        private void SetActive(string name, bool value)
        {
            BaseEventsAppearance gameEvent = _plugin._config.GetEventByName(name);
            if (gameEvent != null)
                gameEvent.IsActive = value;
        }

        private void SetContested(string name, bool value)
        {
            BaseEventsAppearance gameEvent = _plugin._config.GetEventByName(name);
            if (gameEvent != null)
                gameEvent.IsContested = value;
        }

        private bool HasCargoShip(bool docked)
        {
            foreach (CargoShip cargoShip in _cargoShips)
            {
                if (cargoShip != null && cargoShip.IsShipDocked == docked)
                    return true;
            }

            return false;
        }

        private string GetOilRigEventName(HackableLockedCrate crate)
        {
            if (crate == null || crate.IsDestroyed)
                return null;

            Vector3 position = crate.transform.position;

            if (Vector3.Distance(position, _smallOilRigPosition) <= OIL_RIG_RADIUS)
                return "SmallOilRig";

            if (Vector3.Distance(position, _largeOilRigPosition) <= OIL_RIG_RADIUS)
                return "LargeOilRig";

            return null;
        }

        private void Touch(BaseEntity entity)
        {
            _touchedEntities.Add(entity);

            if (_isTouchQueued)
                return;

            _isTouchQueued = true;
            _plugin.NextTick(_processTouchedCallback);
        }

        private void ProcessTouchedEntities()
        {
            _isTouchQueued = false;

            bool hasChanges = false;

            foreach (BaseEntity entity in _touchedEntities)
            {
                if (entity?.OwnerID != 0)
                    continue;

                if (entity is BradleyAPC)
                    SetActive("Bradley", _bradleys.Count > 0);

                else if (entity is PatrolHelicopter)
                    SetActive("PatrolHeli", _patrolHelicopters.Count > 0);

                else if (entity is CH47Helicopter)
                    SetActive("CH47", _ch47Helicopters.Count > 0);

                else if (entity is CargoShip)
                    SetActive("Cargo", _cargoShips.Count > 0);

                else if (entity is SupplyDrop)
                    SetActive("AirDrop", _supplyDrops.Count > 0);

                else
                    continue;

                hasChanges = true;
            }

            _touchedEntities.Clear();

            if (hasChanges)
                _plugin._hudHandler.ShowEvents();
        }

#endregion
    }

    private class SettingsMenuHandler
    {
        private static readonly HashSet<string> s_navigationCommands = new()
        {
            "activeopen", "sleepopen", "queueopen", "timeopen", "playerposopen", "economyopen",
            "openinfo", "amopen", "amedit", "ceventhookedit", "beopen", "ceopen"
        };

        private readonly Hud _plugin;

        public SettingsMenuHandler(Hud plugin)
        {
            _plugin = plugin;
        }

        public void HandleCommand(BasePlayer player, ConsoleSystem.Arg arg)
        {
            string command = arg.GetString(1);

            BaseAppearance appearance;
            switch (command)
            {
                case "io":
                    _plugin._config.MainSetup.IsOverall = !_plugin._config.MainSetup.IsOverall;
                    ShowRoot(player);
                    break;
                case "oa":
                    _plugin._config.MainSetup.OnlyActive = !_plugin._config.MainSetup.OnlyActive;
                    _plugin._eventsHandler.CalculateWidth();
                    ShowRoot(player);
                    break;
                case "size":
                    _plugin._config.MainSetup.Size = arg.GetInt(2);
                    UI.SetSize(_plugin._config.MainSetup.Size / 100f);
                    ShowRoot(player);
                    break;
                case "logo":
                    _plugin._config.MainSetup.Logo = arg.GetString(2);
                    ShowRoot(player);
                    break;
                case "eventsbgop":
                    _plugin._config.MainSetup.EventsBGOpacity = arg.GetInt(2);
                    ShowRoot(player);
                    break;
                case "bgop":
                    _plugin._config.MainSetup.BGOpacity = arg.GetInt(2);
                    ShowRoot(player);
                    break;
                case "posalign":
                    _plugin._config.MainSetup.Position.Align = arg.GetString(2);
                    ShowRoot(player);
                    break;
                case "posleft":
                    _plugin._config.MainSetup.Position.LeftOffSet = arg.GetInt(2);
                    ShowRoot(player);
                    break;
                case "postop":
                    _plugin._config.MainSetup.Position.TopOffSet = arg.GetInt(2);
                    ShowRoot(player);
                    break;
                case "name":
                    _plugin._config.MainSetup.ServerName = JoinArguments(arg.Args, 2);
                    ShowRoot(player);
                    break;
                case "activeopen":
                    OpenActivePlayers(player);
                    break;
                case "sleepopen":
                    appearance = _plugin._config.MainSetup.SleepPlayersAppearance;
                    OpenPlayers(player, "Sleep players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s senable", "UI_H s sicon ",
                        "UI_H s scolor ");
                    break;
                case "queueopen":
                    appearance = _plugin._config.MainSetup.QueuePlayersAppearance;
                    OpenPlayers(player, "Queue players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s qenable", "UI_H s qicon ",
                        "UI_H s qcolor ");
                    break;
                case "aenable":
                    appearance = _plugin._config.MainSetup.ActivePlayersAppearance;
                    appearance.IsEnable = !appearance.IsEnable;
                    OpenActivePlayers(player);
                    break;
                case "aadminscount":
                    ActiveAppearance activePlayers = _plugin._config.MainSetup.ActivePlayersAppearance;
                    activePlayers.IsAdminsCount = !activePlayers.IsAdminsCount;
                    OpenActivePlayers(player);
                    break;
                case "aicon":
                    _plugin._config.MainSetup.ActivePlayersAppearance.Icon = arg.GetString(2);
                    OpenActivePlayers(player);
                    break;
                case "acolor":
                    _plugin._config.MainSetup.ActivePlayersAppearance.Color = arg.GetString(2);
                    OpenActivePlayers(player);
                    break;
                case "senable":
                    appearance = _plugin._config.MainSetup.SleepPlayersAppearance;
                    appearance.IsEnable = !appearance.IsEnable;
                    OpenPlayers(player, "Active players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s senable", "UI_H s sicon ",
                        "UI_H s scolor ");
                    break;
                case "sicon":
                    appearance = _plugin._config.MainSetup.SleepPlayersAppearance;
                    appearance.Icon = arg.GetString(2);
                    OpenPlayers(player, "Active players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s senable", "UI_H s sicon ",
                        "UI_H s scolor ");
                    break;
                case "scolor":
                    appearance = _plugin._config.MainSetup.SleepPlayersAppearance;
                    appearance.Color = arg.GetString(2);
                    OpenPlayers(player, "Active players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s senable", "UI_H s sicon ",
                        "UI_H s scolor ");
                    break;
                case "qenable":
                    appearance = _plugin._config.MainSetup.QueuePlayersAppearance;
                    appearance.IsEnable = !appearance.IsEnable;
                    OpenPlayers(player, "Active players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s qenable", "UI_H s qicon ",
                        "UI_H s qcolor ");
                    break;
                case "qicon":
                    appearance = _plugin._config.MainSetup.QueuePlayersAppearance;
                    appearance.Icon = arg.GetString(2);
                    OpenPlayers(player, "Active players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s qenable", "UI_H s qicon ",
                        "UI_H s qcolor ");
                    break;
                case "qcolor":
                    appearance = _plugin._config.MainSetup.QueuePlayersAppearance;
                    appearance.Color = arg.GetString(2);
                    OpenPlayers(player, "Active players", appearance.IsEnable, appearance.Icon, appearance.Color, "UI_H s qenable", "UI_H s qicon ",
                        "UI_H s qcolor ");
                    break;
                case "timeopen":
                    OpenClock(player);
                    break;
                case "playerposopen":
                    OpenPlayerPosition(player);
                    break;
                case "economyopen":
                    OpenEconomy(player);
                    break;
                case "tautodetect":
                    _plugin._config.MainSetup.Time.IsAutomaticDetection = !_plugin._config.MainSetup.Time.IsAutomaticDetection;
                    OpenClock(player);
                    break;
                case "tisam":
                    _plugin._config.MainSetup.Time.IsAmPm = !_plugin._config.MainSetup.Time.IsAmPm;
                    OpenClock(player);
                    break;
                case "tcolor":
                    _plugin._config.MainSetup.Time.Color = arg.GetString(2);
                    OpenClock(player);
                    break;
                case "tenable":
                    _plugin._config.MainSetup.Time.IsEnable = !_plugin._config.MainSetup.Time.IsEnable;
                    OpenClock(player);
                    break;
                case "ppisgrid":
                    _plugin._config.MainSetup.PlayerPosition.IsGrid = !_plugin._config.MainSetup.PlayerPosition.IsGrid;
                    OpenPlayerPosition(player);
                    break;
                case "ppcolor":
                    _plugin._config.MainSetup.PlayerPosition.Color = arg.GetString(2);
                    OpenPlayerPosition(player);
                    break;
                case "ppenabled":
                    _plugin._config.MainSetup.PlayerPosition.IsEnable = !_plugin._config.MainSetup.PlayerPosition.IsEnable;
                    OpenPlayerPosition(player);
                    break;
                case "ecurrency":
                    _plugin._config.MainSetup.Economy.Currency = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "eplugin":
                    _plugin._config.MainSetup.Economy.Plugin = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "eshop":
                    _plugin._config.MainSetup.Economy.ShopName = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "ecolor":
                    _plugin._config.MainSetup.Economy.Color = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "eenable":
                    _plugin._config.MainSetup.Economy.IsEnable = !_plugin._config.MainSetup.Economy.IsEnable;
                    OpenEconomy(player);
                    break;
                case "e2currency":
                    _plugin._config.MainSetup.SecondEconomy.Currency = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "e2plugin":
                    _plugin._config.MainSetup.SecondEconomy.Plugin = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "e2shop":
                    _plugin._config.MainSetup.SecondEconomy.ShopName = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "e2color":
                    _plugin._config.MainSetup.SecondEconomy.Color = arg.GetString(2);
                    OpenEconomy(player);
                    break;
                case "e2enable":
                    _plugin._config.MainSetup.SecondEconomy.IsEnable = !_plugin._config.MainSetup.SecondEconomy.IsEnable;
                    OpenEconomy(player);
                    break;
                case "openinfo":
                    OpenInfoMessages(player);
                    break;
                case "ienable":
                    _plugin._config.MainSetup.InfoMessages.IsEnable = !_plugin._config.MainSetup.InfoMessages.IsEnable;

                    if (_plugin._config.MainSetup.InfoMessages.IsEnable)
                    {
                        _plugin._hudHandler.RestartInfoMessageTimer();
                    }
                    else
                    {
                        _plugin._hudHandler.StopInfoMessageTimer();

                        UI.DestroyToAll(".bg.Messages.bg");
                    }

                    OpenInfoMessages(player);
                    break;
                case "ioverall":
                    _plugin._config.MainSetup.InfoMessages.IsOverall = !_plugin._config.MainSetup.InfoMessages.IsOverall;
                    OpenInfoMessages(player);
                    break;
                case "iuseoutline":
                    _plugin._config.MainSetup.InfoMessages.UseOutline = !_plugin._config.MainSetup.InfoMessages.UseOutline;
                    OpenInfoMessages(player);
                    break;
                case "iupdateint":
                    _plugin._config.MainSetup.InfoMessages.UpdateInterval = arg.GetInt(2);
                    if (_plugin._config.MainSetup.InfoMessages.IsEnable)
                        _plugin._hudHandler.RestartInfoMessageTimer();

                    OpenInfoMessages(player);
                    break;
                case "ialign":
                    _plugin._config.MainSetup.InfoMessages.Align = arg.GetString(2);
                    OpenInfoMessages(player);
                    break;
                case "iwidth":
                    _plugin._config.MainSetup.InfoMessages.Width = arg.GetInt(2);
                    OpenInfoMessages(player);
                    break;
                case "itboff":
                    _plugin._config.MainSetup.InfoMessages.OffsetTB = arg.GetInt(2);
                    OpenInfoMessages(player);
                    break;
                case "iroff":
                    _plugin._config.MainSetup.InfoMessages.OffsetR = arg.GetInt(2);
                    OpenInfoMessages(player);
                    break;
                case "ioutlinecolor":
                    _plugin._config.MainSetup.InfoMessages.OutlineColor = arg.GetString(2);
                    OpenInfoMessages(player);
                    break;
                case "imremove":
                    _plugin._config.MainSetup.InfoMessages.Messages.RemoveAt(arg.GetInt(2));
                    OpenInfoMessages(player);
                    break;
                case "imadd":
                    _plugin._config.MainSetup.InfoMessages.Messages.Add(JoinArguments(arg.Args, 2));
                    OpenInfoMessages(player);
                    break;
                case "amenable":
                    _plugin._config.MainSetup.AdditionalMenu.IsEnable = !_plugin._config.MainSetup.AdditionalMenu.IsEnable;
                    OpenAdditionalMenu(player);
                    break;
                case "amautoclose":
                    _plugin._config.MainSetup.AdditionalMenu.AutoCloseCommand = !_plugin._config.MainSetup.AdditionalMenu.AutoCloseCommand;
                    OpenAdditionalMenu(player);
                    break;
                case "amclosetimer":
                    _plugin._config.MainSetup.AdditionalMenu.AutoCloseTime = arg.GetInt(2);
                    OpenAdditionalMenu(player);
                    break;
                case "amopacity":
                    _plugin._config.MainSetup.AdditionalMenu.CommandsOpacity = arg.GetInt(2);
                    OpenAdditionalMenu(player);
                    break;
                case "amcolor":
                    _plugin._config.MainSetup.AdditionalMenu.ButtonColor = arg.GetString(2);
                    OpenAdditionalMenu(player);
                    break;
                case "amopen":
                    OpenAdditionalMenu(player);
                    break;
                case "amcreatenew":
                    _plugin._config.MainSetup.AdditionalMenu.Commands.Add(new CommandAppearance
                    {
                        Text = "Nex Example",
                        Command = "chat.say Hello there",
                        Icon = "https://www.dropbox.com/scl/fi/weax6ug55o0u4f87bhchs/active.png?rlkey=lbpuhpqs7tutpgv1i4t3b1107&dl=1",
                        Image = "https://www.dropbox.com/scl/fi/ypbnnbs07i2t322x1pu4c/command.png?rlkey=a693i43bn70wiy2p0v0216uw7&dl=1",
                        IsConsole = true,
                        OutlineColor = "#000",
                        PermissionToSee = ""
                    });
                    OpenAdditionalMenu(player);
                    break;
                case "amedit":
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "ceventhookedit":
                    OpenEventHookList(player, arg.GetInt(2), arg.GetInt(3));
                    break;
                case "amebtext":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].Text = JoinArguments(arg.Args, 3);
                    OpenAdditionalMenu(player);
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "amebcommand":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].Command = JoinArguments(arg.Args, 3);
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "ameboutlinecolor":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].OutlineColor = arg.GetString(3);
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "amebicon":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].Icon = arg.GetString(3);
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "amebimage":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].Image = arg.GetString(3);
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "amebisconsole":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].IsConsole =
                        !_plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].IsConsole;
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "apermtosee":
                    _plugin._config.MainSetup.AdditionalMenu.Commands[arg.GetInt(2)].PermissionToSee = arg.GetString(3);
                    OpenAdditionalMenuButton(player, arg.GetInt(2));
                    break;
                case "ambdelete":
                    _plugin._config.MainSetup.AdditionalMenu.Commands.RemoveAt(arg.GetInt(2));
                    OpenAdditionalMenu(player);
                    break;
                case "beopen":
                    OpenBaseEvent(player, arg.GetInt(2));
                    break;
                case "beenable":
                    _plugin._config.BaseEvents[arg.GetInt(2)].IsEnable = !_plugin._config.BaseEvents[arg.GetInt(2)].IsEnable;
                    _plugin._eventsHandler.CalculateWidth();
                    OpenBaseEvent(player, arg.GetInt(2));
                    break;
                case "beicon":
                    _plugin._config.BaseEvents[arg.GetInt(2)].Icon = arg.GetString(3);
                    ShowRoot(player);
                    OpenBaseEvent(player, arg.GetInt(2));
                    break;
                case "beacolor":
                    _plugin._config.BaseEvents[arg.GetInt(2)].ActiveColor = arg.GetString(3);
                    OpenBaseEvent(player, arg.GetInt(2));
                    break;
                case "becolor":
                    _plugin._config.BaseEvents[arg.GetInt(2)].Color = arg.GetString(3);
                    OpenBaseEvent(player, arg.GetInt(2));
                    break;
                case "ceopen":
                    OpenCustomEvent(player, arg.GetInt(2));
                    break;
                case "ceenable":
                    _plugin._config.CustomEvents[arg.GetInt(2)].IsEnable = !_plugin._config.CustomEvents[arg.GetInt(2)].IsEnable;
                    _plugin._eventsHandler.CalculateWidth();
                    OpenCustomEvent(player, arg.GetInt(2));
                    break;
                case "cename":
                    _plugin._config.CustomEvents[arg.GetInt(2)].Name = arg.GetString(3);
                    OpenCustomEvent(player, arg.GetInt(2));
                    break;
                case "ceicon":
                    _plugin._config.CustomEvents[arg.GetInt(2)].Icon = arg.GetString(3);
                    OpenCustomEvent(player, arg.GetInt(2));
                    break;
                case "ceactivecolor":
                    _plugin._config.CustomEvents[arg.GetInt(2)].ActiveColor = arg.GetString(3);
                    OpenCustomEvent(player, arg.GetInt(2));
                    break;
                case "cecolor":
                    _plugin._config.CustomEvents[arg.GetInt(2)].Color = arg.GetString(3);
                    OpenCustomEvent(player, arg.GetInt(2));
                    break;
                case "cedelete":
                    _plugin._config.CustomEvents.RemoveAt(arg.GetInt(2));
                    _plugin._eventsHandler.CalculateWidth();
                    ShowRoot(player);
                    break;
                case "ceventhooksstartrem":
                    _plugin._config.CustomEvents[arg.GetInt(2)].OnEventStart.RemoveAt(arg.GetInt(3));
                    OpenEventHookList(player, arg.GetInt(2), 1);
                    break;
                case "ceventhooksendrem":
                    _plugin._config.CustomEvents[arg.GetInt(2)].OnEventEnd.RemoveAt(arg.GetInt(3));
                    OpenEventHookList(player, arg.GetInt(2), 0);
                    break;
                case "ceventhooksaddstart":
                    _plugin._config.CustomEvents[arg.GetInt(2)].OnEventStart.Add(arg.GetString(3));
                    OpenEventHookList(player, arg.GetInt(2), 1);
                    break;
                case "ceventhooksaddend":
                    _plugin._config.CustomEvents[arg.GetInt(2)].OnEventEnd.Add(arg.GetString(3));
                    OpenEventHookList(player, arg.GetInt(2), 0);
                    break;
                case "ceadd":
                    _plugin._config.CustomEvents.Add(new CustomEventsAppearance
                    {
                        Name = "CustomEvent",
                        IsEnable = true,
                        Color = "#fff",
                        Icon = "https://www.dropbox.com/scl/fi/3qzyayoa4dxog2t8gyvzw/harbor.png?rlkey=v7jnpk8tyngwywvhbgrfhk5a4&dl=1",
                        ActiveColor = "#10ff10",
                        OnEventStart = new List<string>
                        {
                            "OnHarborEventStart"
                        },
                        OnEventEnd = new List<string>
                        {
                            "OnHarborEventEnd"
                        }
                    });
                    _plugin._eventsHandler.CalculateWidth();
                    ShowRoot(player);
                    break;
            }

            if (s_navigationCommands.Contains(command))
                return;

            _plugin._eventsHandler.RefreshCustomHooks();
            _plugin.RefreshHookSubscriptions();
            _plugin._imageHandler.RegisterConfiguredImages();
            _plugin.SaveConfig();

            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                _plugin._hudHandler.ShowBackground(activePlayer);
            }
        }

#region UI

        public void Open(BasePlayer player)
        {
            var container = new CuiElementContainer();

            UI.MainParent(ref container, "Settings", "0 0", "1 1", sprite: "assets/content/ui/ui.background.transparent.radial.psd",
                color: "0.1691 0.1619 0.143 0.741");
            UI.Panel(ref container, ".bgSettings", aMin: "0 0", aMax: "1 1", bgColor: "0.1691 0.1619 0.143 0.747",
                sprite: "assets/content/ui/ui.background.tile.psd", material: "assets/content/ui/uibackgroundblur-ingamemenu.mat", ignoreSize: true);
            UI.Label(ref container, ".bgSettings", aMin: "0.5 0.5", aMax: "0.5 0.5", oXMin: -190, oXMax: 190,
                oYMin: _plugin._config.CustomEvents.Count > 11 ? -302 : -238,
                oYMax: -208, text: "Click anywhere outside the panel to exit", color: "0.9686 0.9216 0.8824 0.502", fontSize: 12, ignoreSize: true);
            UI.TextButton(ref container, ".bgSettings", aMin: "0 0", aMax: "1 1", close: ".bgSettings", ignoreSize: true);

            UI.Create(player, container);
            ShowRoot(player);
        }

        private void ShowRoot(BasePlayer player)
        {
            int offsetY = -30;
            MainSetup main = _plugin._config.MainSetup;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings", ".bgSettings.Main", ".bgSettings.Main", "0.5 0.5", "0.5 0.5", -198,
                _plugin._config.CustomEvents.Count > 11 ? -272 : -238, 198, 268, "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat",
                "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main", null, null, "0.5 1", "0.5 1", -198, 0, 198, 35, "HUD SETUP", 28, outlineDistance: "0.5 0.5",
                color: "0.9686 0.9216 0.8824 0.797", font: "robotocondensed-bold.ttf", ignoreSize: true);

            UI.Label(ref container, ".bgSettings.Main", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Main Setup", 16, "0.9686 0.9216 0.8824 0.547",
                font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main", ref offsetY, "Use [Overlay] layer for Hud:", main.IsOverall, "UI_H s io");
            AddBoolRow(ref container, ".bgSettings.Main", ref offsetY, "Show only active events:", main.OnlyActive, "UI_H s oa");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Server Name:", main.ServerName, "UI_H s name ");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Logo URL [HUD interact button]:", main.Logo, "UI_H s logo ");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Hud scale:", main.Size.ToString(), "UI_H s size");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Events background opacity:", main.BGOpacity.ToString(), "UI_H s bgop");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Background opacity:", main.EventsBGOpacity.ToString(), "UI_H s eventsbgop");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Hud align [TopLeft | TopRight | BottomLeft | BottomRight]:", main.Position.Align,
                "UI_H s posalign");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Left/Right Offset:", main.Position.LeftOffSet.ToString(), "UI_H s posleft");
            AddInputRow(ref container, ".bgSettings.Main", ref offsetY, "Top/Bottom Offset:", main.Position.TopOffSet.ToString(), "UI_H s postop");

            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Active players:", "UI_H s activeopen");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Sleep players:", "UI_H s sleepopen");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Queue players:", "UI_H s queueopen");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Clock settings:", "UI_H s timeopen");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Player position:", "UI_H s playerposopen");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Economy plugin:", "UI_H s economyopen");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Info message:", "UI_H s openinfo");
            AddButtonRow(ref container, ".bgSettings.Main", ref offsetY, "Additional menu:", "UI_H s amopen");

            UI.Label(ref container, ".bgSettings.Main", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Events", 16,
                "0.9686 0.9216 0.8824 0.547",
                font: "robotocondensed-bold.ttf", ignoreSize: true);

            offsetY -= 25;

            AddBaseEventIcons(ref container, ref offsetY);
            AddCustomEventIcons(ref container, ref offsetY);

            UI.Create(player, container);
        }

        private void AddCustomEventIcons(ref CuiElementContainer container, ref int offsetY)
        {
            int offsetX = _plugin._config.CustomEvents.Count <= 11
                ? (4 * (_plugin._config.CustomEvents.Count - 1) + 30 * _plugin._config.CustomEvents.Count) / -2
                : -185;

            for (int index = 0; index < _plugin._config.CustomEvents.Count; index++)
            {
                UI.Image(ref container, ".bgSettings.Main", aMin: "0.5 1", aMax: "0.5 1", oXMin: offsetX, oYMin: offsetY - 30, oXMax: offsetX + 30,
                    oYMax: offsetY,
                    image: _plugin._imageHandler.GetImage(_plugin._config.CustomEvents[index].Icon), color: _plugin._config.CustomEvents[index].Color,
                    ignoreSize: true);
                UI.TextButton(ref container, ".bgSettings.Main", null, null, "0.5 1", "0.5 1", offsetX, offsetY - 30, offsetX + 30, offsetY,
                    command: $"UI_H s ceopen {index}",
                    ignoreSize: true);

                offsetX += 34;
                if (index != 10)
                    continue;

                offsetX = (4 * (_plugin._config.CustomEvents.Count - 12) + 30 * (_plugin._config.CustomEvents.Count - 11)) / -2;
                offsetY -= 34;
            }

            if (_plugin._config.CustomEvents.Count < 22)
                UI.TextButton(ref container, ".bgSettings.Main", null, null, "0.5 1", "0.5 1", offsetX, offsetY - 30, offsetX + 30, offsetY, "+", 25,
                    command: "UI_H s ceadd",
                    ignoreSize: true);

            offsetY -= 34;
        }

        private void AddBaseEventIcons(ref CuiElementContainer container, ref int offsetY)
        {
            int offsetX = (4 * (_plugin._config.BaseEvents.Count - 1) + 30 * _plugin._config.BaseEvents.Count) / -2;

            for (int index = 0; index < _plugin._config.BaseEvents.Count; index++)
            {
                UI.Image(ref container, ".bgSettings.Main", aMin: "0.5 1", aMax: "0.5 1", oXMin: offsetX, oYMin: offsetY - 30, oXMax: offsetX + 30,
                    oYMax: offsetY,
                    image: _plugin._imageHandler.GetImage(_plugin._config.BaseEvents[index].Icon), color: _plugin._config.BaseEvents[index].Color,
                    ignoreSize: true);
                UI.TextButton(ref container, ".bgSettings.Main", null, null, "0.5 1", "0.5 1", offsetX, offsetY - 30, offsetX + 30, offsetY,
                    command: $"UI_H s beopen {index}",
                    ignoreSize: true);

                offsetX += 34;
            }

            offsetY -= 34;
        }

        private void OpenCustomEvent(BasePlayer player, int index)
        {
            int offsetY = -30;
            CustomEventsAppearance menu = _plugin._config.CustomEvents[index];
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -205, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, $"{menu.Name} settings", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", menu.IsEnable, $"UI_H s ceenable {index}");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Name:", menu.Name, $"UI_H s cename {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Icon:", menu.Icon, $"UI_H s ceicon {index} ");

            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY, "OnEventStart Hooks:", 12,
                "0.9686 0.9216 0.8824 0.502", TextAnchor.MiddleLeft, font: "robotocondensed-regular.ttf", ignoreSize: true);
            UI.TextButton(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Edit", 12,
                command: $"UI_H s ceventhookedit {index} 1", bgColor: "0 0 0 0", color: "0.1471 0.6496 1 1", font: "robotocondensed-regular.ttf",
                ignoreSize: true);

            offsetY -= 20;

            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY, "OnEventEnd Hooks:", 12,
                "0.9686 0.9216 0.8824 0.502", TextAnchor.MiddleLeft, font: "robotocondensed-regular.ttf", ignoreSize: true);
            UI.TextButton(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Edit", 12,
                command: $"UI_H s ceventhookedit {index} 0", bgColor: "0 0 0 0", color: "0.1471 0.6496 1 1", font: "robotocondensed-regular.ttf",
                ignoreSize: true);

            offsetY -= 20;

            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Active color:", menu.ActiveColor, $"UI_H s ceactivecolor {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Default color:", menu.Color, $"UI_H s cecolor {index} ");

            UI.Image(ref container, ".bgSettings.Main.Additional", aMin: "0.5 1", aMax: "0.5 1", oXMin: -32, oYMin: offsetY - 30, oXMax: -2, oYMax: offsetY,
                image: _plugin._imageHandler.GetImage(menu.Icon), color: menu.Color, ignoreSize: true);
            UI.Image(ref container, ".bgSettings.Main.Additional", aMin: "0.5 1", aMax: "0.5 1", oXMin: 2, oYMin: offsetY - 30, oXMax: 32, oYMax: offsetY,
                image: _plugin._imageHandler.GetImage(menu.Icon), color: menu.ActiveColor, ignoreSize: true);

            UI.TextButton(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", 118, offsetY - 25, 180, offsetY - 5, "Delete", 12,
                command: $"UI_H s cedelete {index}", bgColor: "0.1691 0.1619 0.143 0.409", color: "0.9686 0.9216 0.8824 0.522",
                font: "robotocondensed-bold.ttf",
                ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", aMin: "0.5 0", aMax: "0.5 0", oXMin: -190, oXMax: 190, oYMin: -40, oYMax: -5,
                text: "After changing OnEventStart/OnEventEnd Hook,\nreload the plugin for the changes to take effect", color: "0.9686 0.9216 0.8824 0.502",
                fontSize: 12, ignoreSize: true);

            UI.Create(player, container);
        }

        private void OpenBaseEvent(BasePlayer player, int index)
        {
            int offsetY = -30;
            BaseEventsAppearance menu = _plugin._config.BaseEvents[index];
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -165, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, $"{menu.Name} settings", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", menu.IsEnable, $"UI_H s beenable {index}");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Name:", menu.Name, "", false);
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Icon:", menu.Icon, $"UI_H s beicon {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Active color:", menu.ActiveColor, $"UI_H s beacolor {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Default color:", menu.Color, $"UI_H s becolor {index} ");

            UI.Image(ref container, ".bgSettings.Main.Additional", aMin: "0.5 1", aMax: "0.5 1", oXMin: -32, oYMin: offsetY - 30, oXMax: -2, oYMax: offsetY,
                image: _plugin._imageHandler.GetImage(menu.Icon), color: menu.Color, ignoreSize: true);
            UI.Image(ref container, ".bgSettings.Main.Additional", aMin: "0.5 1", aMax: "0.5 1", oXMin: 2, oYMin: offsetY - 30, oXMax: 32, oYMax: offsetY,
                image: _plugin._imageHandler.GetImage(menu.Icon), color: menu.ActiveColor, ignoreSize: true);

            UI.Create(player, container);
        }

        private void OpenEventHookList(BasePlayer player, int index, int start)
        {
            int offsetY = -30;
            CustomEventsAppearance menu = _plugin._config.CustomEvents[index];
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main.Additional", ".bgSettings.Main.Additional.Button", ".bgSettings.Main.Additional.Button", "0 1", "0 1",
                -804,
                -180, -404, 0, "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, $"{menu.Name} ", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            if (start == 1)
            {
                for (int hookIndex = 0; hookIndex < menu.OnEventStart.Count; hookIndex++)
                {
                    UI.Label(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY,
                        menu.OnEventStart[hookIndex], 12,
                        "0.9686 0.9216 0.8824 0.502", TextAnchor.MiddleLeft, font: "robotocondensed-regular.ttf", ignoreSize: true);
                    UI.TextButton(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Remove",
                        12,
                        command: $"UI_H s ceventhooksstartrem {index} {hookIndex}", bgColor: "0 0 0 0", color: "1 0.5279 0.1838 1",
                        font: "robotocondensed-regular.ttf",
                        ignoreSize: true);

                    offsetY -= 20;
                }

                if (menu.OnEventStart.Count < 6)
                {
                    UI.Label(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Add hook",
                        16,
                        "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

                    offsetY -= 25;

                    UI.Panel(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -173, offsetY - 15, 173, offsetY,
                        "0.1137 0.1255 0.1216 0.403",
                        sprite: "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
                    UI.Input(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -173, offsetY - 15, 173, offsetY,
                        command: $"UI_H s ceventhooksaddstart {index} ", color: "0.9686 0.9216 0.8824 0.502", text: "Enter your new hook here",
                        align: TextAnchor.MiddleLeft, fontSize: 12, font: "robotocondensed-regular.ttf", ignoreSize: true);
                }
            }
            else
            {
                for (int hookIndex = 0; hookIndex < menu.OnEventEnd.Count; hookIndex++)
                {
                    UI.Label(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY,
                        menu.OnEventEnd[hookIndex], 12,
                        "0.9686 0.9216 0.8824 0.502", TextAnchor.MiddleLeft, font: "robotocondensed-regular.ttf", ignoreSize: true);
                    UI.TextButton(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Remove",
                        12,
                        command: $"UI_H s ceventhooksendrem {index} {hookIndex}", bgColor: "0 0 0 0", color: "1 0.5279 0.1838 1",
                        font: "robotocondensed-regular.ttf",
                        ignoreSize: true);

                    offsetY -= 20;
                }

                if (menu.OnEventEnd.Count < 6)
                {
                    UI.Label(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Add hook",
                        16,
                        "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

                    offsetY -= 25;

                    UI.Panel(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -173, offsetY - 15, 173, offsetY,
                        "0.1137 0.1255 0.1216 0.403",
                        sprite: "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
                    UI.Input(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -173, offsetY - 15, 173, offsetY,
                        command: $"UI_H s ceventhooksaddend {index} ", color: "0.9686 0.9216 0.8824 0.502", text: "Enter your new hook here...",
                        align: TextAnchor.MiddleLeft, fontSize: 12, font: "robotocondensed-regular.ttf", ignoreSize: true);
                }
            }

            UI.Create(player, container);
        }

        private void OpenAdditionalMenuButton(BasePlayer player, int index)
        {
            int offsetY = -30;
            CommandAppearance menu = _plugin._config.MainSetup.AdditionalMenu.Commands[index];
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main.Additional", ".bgSettings.Main.Additional.Button", ".bgSettings.Main.Additional.Button", "0 1", "0 1",
                -804,
                -205, -404, 0, "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, $"'{menu.Text}' settings", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddInputRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Text:", menu.Text, $"UI_H s amebtext {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Command:", menu.Command, $"UI_H s amebcommand {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Outline color:", menu.OutlineColor,
                $"UI_H s ameboutlinecolor {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Icon [optional]:", menu.Icon, $"UI_H s amebicon {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Background image URL:", menu.Image, $"UI_H s amebimage {index} ");
            AddInputRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Permission to see:", menu.PermissionToSee,
                $"UI_H s apermtosee {index} ");
            AddBoolRow(ref container, ".bgSettings.Main.Additional.Button", ref offsetY, "Is console command:", menu.IsConsole,
                $"UI_H s amebisconsole {index}");

            UI.TextButton(ref container, ".bgSettings.Main.Additional.Button", null, null, "0.5 1", "0.5 1", 118, offsetY - 25, 180, offsetY - 5, "Delete", 12,
                command: $"UI_H s ambdelete {index}", bgColor: "0.1691 0.1619 0.143 0.409", color: "0.9686 0.9216 0.8824 0.522",
                font: "robotocondensed-bold.ttf",
                ignoreSize: true);

            UI.Create(player, container);
        }

        private void OpenAdditionalMenu(BasePlayer player)
        {
            int offsetY = -30;
            AdditionalMenu menu = _plugin._config.MainSetup.AdditionalMenu;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -476, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Additional menu", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", menu.IsEnable, "UI_H s amenable");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Auto close after command use:", menu.AutoCloseCommand, "UI_H s amautoclose");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Auto close timer [seconds] [0 - disable]:", menu.AutoCloseTime.ToString(),
                "UI_H s amclosetimer ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Command background opacity:", menu.CommandsOpacity.ToString(),
                "UI_H s amopacity ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Open/Close button color:", menu.ButtonColor, "UI_H s amcolor ");

            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Commands list", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            offsetY -= 25;

            for (int index = 0; index < menu.Commands.Count; index++)
            {
                if (index > 15)
                    break;

                UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY,
                    menu.Commands[index].Text, 12,
                    "0.9686 0.9216 0.8824 0.502", TextAnchor.MiddleLeft, font: "robotocondensed-regular.ttf", ignoreSize: true);
                UI.TextButton(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Edit", 12,
                    command: $"UI_H s amedit {index}", bgColor: "0 0 0 0", color: "0.1471 0.6496 1 1", font: "robotocondensed-regular.ttf", ignoreSize: true);

                offsetY -= 20;
            }

            if (menu.Commands.Count < 16)
                UI.TextButton(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -45, offsetY - 15, 45, offsetY, "New", 12,
                    command: "UI_H s amcreatenew", bgColor: "0 0 0 0", color: "0.711 0.8676 0.4338 1", font: "robotocondensed-regular.ttf", ignoreSize: true);

            UI.Create(player, container);
        }

        private void OpenInfoMessages(BasePlayer player)
        {
            int offsetY = -30;
            InfoMessage info = _plugin._config.MainSetup.InfoMessages;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -476, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Info messages", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", info.IsEnable, "UI_H s ienable");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Use [Overlay] layer for messages:", info.IsOverall, "UI_H s ioverall");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Use Outline:", info.UseOutline, "UI_H s iuseoutline");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Align [BottomCenter | TopCenter | TopRight]:", info.Align,
                "UI_H s ialign ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Update interval [in seconds]:", info.UpdateInterval.ToString(),
                "UI_H s iupdateint ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Width [px]:", info.Width.ToString(), "UI_H s iwidth ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Top/Bottom Offset:", info.OffsetTB.ToString(), "UI_H s itboff ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Center Offset:", info.OffsetR.ToString(), "UI_H s iroff ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Outline color:", info.OutlineColor, "UI_H s ioutlinecolor ");

            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Messages list", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            offsetY -= 25;

            for (int index = 0; index < info.Messages.Count; index++)
            {
                if (index > 10)
                    break;

                UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY, info.Messages[index], 12,
                    "0.9686 0.9216 0.8824 0.502", TextAnchor.MiddleLeft, font: "robotocondensed-regular.ttf", ignoreSize: true);
                UI.TextButton(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Remove", 12,
                    command: $"UI_H s imremove {index}", bgColor: "0 0 0 0", color: "1 0.5279 0.1838 1", font: "robotocondensed-regular.ttf", ignoreSize: true);
                offsetY -= 20;
            }

            if (info.Messages.Count < 11)
            {
                UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Add message", 16,
                    "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

                offsetY -= 25;

                UI.Panel(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -173, offsetY - 15, 173, offsetY,
                    "0.1137 0.1255 0.1216 0.403",
                    sprite: "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
                UI.Input(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -173, offsetY - 15, 173, offsetY, command: "UI_H s imadd",
                    color: "0.9686 0.9216 0.8824 0.502", text: "Enter your new message here...", align: TextAnchor.MiddleLeft, fontSize: 12,
                    font: "robotocondensed-regular.ttf", ignoreSize: true);
            }

            UI.Create(player, container);
        }

        private void OpenEconomy(BasePlayer player)
        {
            int offsetY = -30;
            EconomyAppearance economy = _plugin._config.MainSetup.Economy;
            EconomyAppearance second = _plugin._config.MainSetup.SecondEconomy;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -310, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Economy plugin", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", economy.IsEnable, "UI_H s eenable");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Plugin [ServerRewards | Economics | ShoppyStock]:", economy.Plugin,
                "UI_H s eplugin");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Shop name [ShoppyStock only]:", economy.ShopName, "UI_H s eshop");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Currency symbol:", economy.Currency, "UI_H s ecurrency");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Color:", economy.Color, "UI_H s ecolor");

            offsetY -= 8;
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, offsetY - 20, 198, offsetY, "Second economy (optional)",
                14,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);
            offsetY -= 24;

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", second.IsEnable, "UI_H s e2enable");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Plugin [ServerRewards | Economics | ShoppyStock]:", second.Plugin,
                "UI_H s e2plugin");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Shop name [ShoppyStock only]:", second.ShopName, "UI_H s e2shop");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Currency symbol:", second.Currency, "UI_H s e2currency");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Color:", second.Color, "UI_H s e2color");

            UI.Create(player, container);
        }

        private void OpenPlayerPosition(BasePlayer player)
        {
            int offsetY = -30;
            PlayerPositionAppearance pos = _plugin._config.MainSetup.PlayerPosition;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -90, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Player position", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", pos.IsEnable, "UI_H s ppenabled");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Use Grid(True - Grid | False - X|Y coordinates):", pos.IsGrid,
                "UI_H s ppisgrid");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Color:", pos.Color, "UI_H s ppcolor");

            UI.Create(player, container);
        }

        private void OpenClock(BasePlayer player)
        {
            int offsetY = -30;
            TimeAppearance time = _plugin._config.MainSetup.Time;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -110, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Clock Settings", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", time.IsEnable, "UI_H s tenable");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Use automatic detection of the time format for each player:",
                time.IsAutomaticDetection, "UI_H s tautodetect ");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Use AM/PM format [If you use this turn off automatic detection]:",
                time.IsAmPm, "UI_H s tisam ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Color:", time.Color, "UI_H s tcolor ");

            UI.Create(player, container);
        }

        private void OpenActivePlayers(BasePlayer player)
        {
            int offsetY = -30;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -115, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, "Active players", 16,
                "0.9686 0.9216 0.8824 0.547", font: "robotocondensed-bold.ttf", ignoreSize: true);

            ActiveAppearance appearance = _plugin._config.MainSetup.ActivePlayersAppearance;
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", appearance.IsEnable, "UI_H s aenable");
            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Count admins:", !appearance.IsAdminsCount, "UI_H s aadminscount");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Icon URL:", appearance.Icon, "UI_H s aicon ");
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Color:", appearance.Color, "UI_H s acolor ");

            UI.Create(player, container);
        }

        private void OpenPlayers(BasePlayer player, string header, bool isEnabled, string icon, string color, string enableCommand, string iconCommand,
            string colorCommand)
        {
            int offsetY = -30;
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".bgSettings.Main", ".bgSettings.Main.Additional", ".bgSettings.Main.Additional", "1 1", "1 1", 4, -90, 400, 0,
                "0.9686 0.9216 0.8824 0.0392", "assets/icons/greyout.mat", "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Label(ref container, ".bgSettings.Main.Additional", null, null, "0.5 1", "0.5 1", -198, -25, 198, -5, header, 16, "0.9686 0.9216 0.8824 0.547",
                font: "robotocondensed-bold.ttf", ignoreSize: true);

            AddBoolRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Enabled:", isEnabled, enableCommand);
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Icon URL:", icon, iconCommand);
            AddInputRow(ref container, ".bgSettings.Main.Additional", ref offsetY, "Color:", color, colorCommand);

            UI.Create(player, container);
        }

        private void AddButtonRow(ref CuiElementContainer container, string layer, ref int offsetY, string text, string command)
        {
            UI.Label(ref container, layer, null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY, text, 12, "0.9686 0.9216 0.8824 0.502",
                TextAnchor.MiddleLeft,
                font: "robotocondensed-regular.ttf", ignoreSize: true);
            UI.TextButton(ref container, layer, null, null, "0.5 1", "0.5 1", 125, offsetY - 15, 173, offsetY, "Configure", 12, command: command,
                bgColor: "0 0 0 0",
                color: "0.1471 0.6496 1 1", font: "robotocondensed-regular.ttf", ignoreSize: true);

            offsetY -= 20;
        }

        private void AddInputRow(ref CuiElementContainer container, string layer, ref int offsetY, string text, string inputText, string command,
            bool editable = true)
        {
            UI.Label(ref container, layer, null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY, text, 12, "0.9686 0.9216 0.8824 0.502",
                TextAnchor.MiddleLeft,
                font: "robotocondensed-regular.ttf", ignoreSize: true);
            UI.Panel(ref container, layer, null, null, "0.5 1", "0.5 1", 118, offsetY - 15, 180, offsetY, "0.1137 0.1255 0.1216 0.403",
                sprite: "assets/content/ui/ui.background.tile.psd", ignoreSize: true);
            UI.Input(ref container, layer, null, null, "0.5 1", "0.5 1", 118, offsetY - 15, 180, offsetY, command: command, color: "0.9686 0.9216 0.8824 0.502",
                text: inputText, fontSize: 12, font: "robotocondensed-regular.ttf", readOnly: !editable, ignoreSize: true);

            offsetY -= 20;
        }

        private void AddBoolRow(ref CuiElementContainer container, string layer, ref int offsetY, string text, bool state, string command)
        {
            UI.Label(ref container, layer, null, null, "0.5 1", "0.5 1", -190, offsetY - 15, 198, offsetY, text, 12, "0.9686 0.9216 0.8824 0.502",
                TextAnchor.MiddleLeft,
                font: "robotocondensed-regular.ttf", ignoreSize: true);
            UI.TextButton(ref container, layer, null, null, "0.5 1", "0.5 1", 128, offsetY - 15, 170, offsetY, state ? "True" : "False", 12, command: command,
                bgColor: "0 0 0 0", color: state ? "0.711 0.8676 0.4338 1" : "1 0.5279 0.1838 1", font: "robotocondensed-regular.ttf", ignoreSize: true);

            offsetY -= 20;
        }

#endregion
    }

    private class HudHandler
    {
        private static readonly string[] s_balanceSlotMin = { "0 0", "0.3 0" };
        private static readonly string[] s_balanceSlotMax = { "0.3 0", "0.6 0" };
        private static readonly string[] s_balanceSlotNames = { ".Balance0", ".Balance1" };

        private readonly Hud _plugin;
        private readonly Action _showPlayersCountsCallback;
        private readonly HashSet<ulong> _inComputerStation = new();
        private readonly Dictionary<ulong, string> _lastTimeText = new();
        private readonly Dictionary<ulong, long> _lastPositionKey = new();
        private readonly Dictionary<ulong, long> _lastBalanceKey = new();

        private Timer _infoMessageUpdateTimer;
        private Coroutine _infoUpdateCoroutine;
        private bool _isPlayersCountQueued;

        public HudHandler(Hud plugin)
        {
            _plugin = plugin;
            _showPlayersCountsCallback = ShowPlayersCountsToAll;
        }

        public void Initialize()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                HandlePlayerConnected(player);
            }

            if (_plugin._config.MainSetup.InfoMessages.IsEnable)
                RestartInfoMessageTimer();

            _infoUpdateCoroutine = ServerMgr.Instance.StartCoroutine(InfoUpdate());
        }

        public void Shutdown()
        {
            StopInfoMessageTimer();
            StopServerCoroutine(ref _infoUpdateCoroutine);

            UI.DestroyToAll(".bg");
            UI.DestroyToAll(".bg.Messages.bg");
            UI.DestroyToAll(".bgSettings");
        }

        public void RestartInfoMessageTimer()
        {
            _infoMessageUpdateTimer?.Destroy();
            _infoMessageUpdateTimer = _plugin.timer.Every(_plugin._config.MainSetup.InfoMessages.UpdateInterval, ShowMessagesInfoToAll);
        }

        public void StopInfoMessageTimer()
        {
            _infoMessageUpdateTimer?.Destroy();
            _infoMessageUpdateTimer = null;
        }

        public void HandlePlayerConnected(BasePlayer player)
        {
            if (player == null)
                return;

            _plugin._data.PlayersState.TryAdd(player.UserIDString, States.FULL);

            ShowBackground(player);
            HandlePlayersCountChanged();
        }

        public void HandlePlayerDisconnected(BasePlayer player)
        {
            if (player != null)
            {
                _inComputerStation.Remove(player.userID);
                InvalidateInfoText(player.userID);
            }

            HandlePlayersCountChanged();
        }

        public void HandlePlayersCountChanged()
        {
            if (_isPlayersCountQueued)
                return;

            _isPlayersCountQueued = true;
            _plugin.NextTick(_showPlayersCountsCallback);
        }

        public void HandleComputerStationMounted(BasePlayer player)
        {
            if (!_plugin._config.MainSetup.HideInComputerStation)
                return;

            if (!_inComputerStation.Add(player.userID))
                return;

            InvalidateInfoText(player.userID);

            UI.Destroy(player, ".bg");
            UI.Destroy(player, ".bg.Messages.bg");
        }

        public void HandleComputerStationDismounted(BasePlayer player)
        {
            if (player == null || !_inComputerStation.Remove(player.userID))
                return;

            if (_plugin._data.PlayersState.ContainsKey(player.UserIDString))
                ShowBackground(player);
        }

        public void HandleStateCommand(BasePlayer player, string argument)
        {
            States playerState = _plugin._data.PlayersState[player.UserIDString];

            if (!player.IsAdmin && IsStateChangeBlocked(player, playerState))
                return;

            switch (argument)
            {
                case "open":
                    _plugin._data.PlayersState[player.UserIDString] = States.FULL;
                    break;
                case "events":
                    _plugin._data.PlayersState[player.UserIDString] = States.EVENTS;
                    break;
                case "hide":
                    _plugin._data.PlayersState[player.UserIDString] = States.HIDE;
                    break;
                case "close":
                    _plugin._data.PlayersState[player.UserIDString] = States.CLOSE;

                    UI.Destroy(player, ".bg");
                    UI.Destroy(player, ".bg.Messages.bg");
                    return;
                default:
                    _plugin.SendReply(player, HELP_TEXT);
                    break;
            }

            if (playerState == States.CLOSE)
                ShowBackground(player);

            ShowMain(player);
        }

        public void ToggleState(BasePlayer player)
        {
            States state = _plugin._data.PlayersState[player.UserIDString];

            if (IsStateChangeBlocked(player, state))
                return;

            _plugin._data.PlayersState[player.UserIDString] = GetNextState(state);
            ShowMain(player);
        }

        public void ToggleAdditionalMenu(BasePlayer player, bool wasOpen)
        {
            ShowAdditionalMenu(player, !wasOpen);

            if (wasOpen)
                return;

            int autoCloseTime = _plugin._config.MainSetup.AdditionalMenu.AutoCloseTime;
            if (autoCloseTime > 0)
                _plugin.timer.Once(autoCloseTime, () => ShowAdditionalMenu(player));
        }

        public void RunMenuCommand(BasePlayer player, bool isConsole, string commandText)
        {
            player.SendConsoleCommand(isConsole ? commandText : ZString.Concat("chat.say \"", commandText, "\""));

            if (_plugin._config.MainSetup.AdditionalMenu.AutoCloseCommand)
                ShowAdditionalMenu(player);
        }

#region Methods

        private static States GetNextState(States state)
        {
            if (state == States.FULL)
                return States.EVENTS;

            if (state == States.EVENTS)
                return States.HIDE;

            return States.FULL;
        }

        private static bool IsStateChangeBlocked(BasePlayer player, States state)
        {
            return Interface.CallHook("CanHudChangeState", player, state.ToString(), GetNextState(state).ToString()) != null;
        }

        private void ShowPlayersCountsToAll()
        {
            _isPlayersCountQueued = false;

            ShowPlayersCounts();
        }

        private void ShowMessagesInfoToAll()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                ShowMessagesInfo(player);
            }
        }

        private IEnumerator InfoUpdate()
        {
            var wait = new WaitForSeconds(1f);

            while (true)
            {
                if (!_plugin.IsLoaded)
                    yield break;

                bool showTime = _plugin._config.MainSetup.Time.IsEnable;
                bool showPosition = _plugin._config.MainSetup.PlayerPosition.IsEnable;
                bool showBalance = _plugin._economyHandler.IsAnyEnabled();

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    if (_inComputerStation.Contains(player.userID))
                        continue;

                    if (showTime)
                        ShowTime(player);

                    if (showPosition)
                        ShowPosition(player);

                    if (showBalance)
                        ShowBalance(player);
                }

                yield return wait;
            }
        }

        private static bool HasInfoTextChanged(Dictionary<ulong, string> cache, ulong userID, string text)
        {
            if (cache.TryGetValue(userID, out string lastText) && lastText == text)
                return false;

            cache[userID] = text;

            return true;
        }

        private bool HasPositionChanged(ulong userID, long positionKey)
        {
            if (_lastPositionKey.TryGetValue(userID, out long lastKey) && lastKey == positionKey)
                return false;

            _lastPositionKey[userID] = positionKey;

            return true;
        }

        private bool HasBalanceChanged(ulong userID, int firstBalance, int secondBalance)
        {
            long balanceKey = PackCoords(firstBalance, secondBalance);

            if (_lastBalanceKey.TryGetValue(userID, out long lastKey) && lastKey == balanceKey)
                return false;

            _lastBalanceKey[userID] = balanceKey;

            return true;
        }

        private void InvalidateInfoText(ulong userID)
        {
            _lastTimeText.Remove(userID);
            _lastPositionKey.Remove(userID);
            _lastBalanceKey.Remove(userID);
        }

        private int CountActivePlayers()
        {
            if (!_plugin._config.MainSetup.ActivePlayersAppearance.IsAdminsCount)
                return BasePlayer.activePlayerList.Count;

            int activeCount = 0;

            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                if (!activePlayer.IsAdmin)
                    ++activeCount;
            }

            return activeCount;
        }

        private bool IsEventsHiddenFor(BasePlayer player)
        {
            if (_inComputerStation.Contains(player.userID))
                return true;

            States state = _plugin._data.PlayersState[player.UserIDString];

            return state == States.HIDE || state == States.CLOSE;
        }

        private static long GetGridCell(Vector3 position)
        {
            const int gridScale = 7;

            var normalized = new Vector2(TerrainMeta.NormalizeX(position.x), TerrainMeta.NormalizeZ(position.z));
            float cellSize = TerrainMeta.Size.x / 1024f;
            Vector2 cell = normalized * cellSize * gridScale;

            int column = (int)(Mathf.Floor(cell.x) + 1f);
            int row = (int)Mathf.Floor(cellSize * gridScale - cell.y);

            return PackCoords(column, row);
        }

        private static string FormatGridCell(long gridCell)
        {
            float column = (int)(gridCell >> 32);
            float row = (int)(uint)gridCell;
            float majorLetter = column / 26f;
            float minorLetter = column % 26f;

            if (minorLetter == 0f)
                minorLetter = 26f;

            using Utf16ValueStringBuilder builder = ZString.CreateStringBuilder();

            if (majorLetter is > 1f and <= 26)
                builder.Append(Convert.ToChar(64 + (int)majorLetter));

            if (minorLetter is >= 1f and <= 26)
                builder.Append(Convert.ToChar(64 + (int)minorLetter));

            builder.Append((int)row);

            return builder.ToString();
        }

        private static long PackCoords(int first, int second)
        {
            return ((long)first << 32) | (uint)second;
        }

#endregion

#region UI

        private void AddEventIcons<TEvent>(ref CuiElementContainer container, List<TEvent> events, ref int posX) where TEvent : BaseEventsAppearance
        {
            bool onlyActive = _plugin._config.MainSetup.OnlyActive;

            foreach (TEvent gameEvent in events)
            {
                if (!gameEvent.IsEnable || (onlyActive && !gameEvent.IsActive))
                    continue;

                UI.Image(ref container, ".Events", aMin: "0 0", aMax: "0 0", oXMin: posX, oYMin: -4, oXMax: posX + 30, oYMax: 26,
                    image: _plugin._imageHandler.GetImage(gameEvent.Icon), color: gameEvent.GetColor());

                posX += 34;
            }
        }

        public void ShowEvents(BasePlayer player = null)
        {
            if (player != null && IsEventsHiddenFor(player))
                return;

            if (_plugin._config.MainSetup.OnlyActive)
                _plugin._eventsHandler.CalculateWidth();

            var container = new CuiElementContainer();

            UI.Panel(ref container, ".Events.bg", ".Events", ".Events", "0.5 0.5", "0.5 0.5", bgColor: null);

            int posX = _plugin._eventsHandler.StartPosX;

            AddEventIcons(ref container, _plugin._config.BaseEvents, ref posX);
            AddEventIcons(ref container, _plugin._config.CustomEvents, ref posX);

            if (player == null)
            {
                string json = container.ToJson();

                foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
                {
                    if (!IsEventsHiddenFor(activePlayer))
                        CuiHelper.AddUi(activePlayer, json);
                }
            }

            UI.Create(player, container);
        }

        private void ShowAdditionalMenuCommands(BasePlayer player)
        {
            var container = new CuiElementContainer();

            bool alignBottom = _plugin._config.MainSetup.Position.IsAlignBottom();
            string commandsOpacityColor = UI.OpacityColor(_plugin._config.MainSetup.AdditionalMenu.CommandsOpacity);
            int posY = alignBottom ? 104 : -50;

            foreach (CommandAppearance command in _plugin._config.MainSetup.AdditionalMenu.Commands)
            {
                if (_plugin.permission.PermissionExists(command.PermissionToSee) &&
                    !_plugin.permission.UserHasPermission(player.UserIDString, command.PermissionToSee))
                    continue;

                string commandLayer = ZString.Concat(".AdditionalMenu.Command.bg", posY);
                bool hasIcon = !string.IsNullOrEmpty(command.Icon);

                UI.Panel(ref container, ".AdditionalMenuButton.bg", commandLayer, commandLayer, "0.5 1", "0.5 1", -80, posY, 80, posY + 30, null);

                if (!string.IsNullOrEmpty(command.Image))
                    UI.Image(ref container, commandLayer, image: _plugin._imageHandler.GetImage(command.Image), color: commandsOpacityColor);

                if (hasIcon)
                    UI.Image(ref container, commandLayer, aMin: "0 0.5", aMax: "0 0.5", oXMin: 10, oYMin: -10, oXMax: 30, oYMax: 10,
                        image: _plugin._imageHandler.GetImage(command.Icon));

                UI.Label(ref container, commandLayer, aMin: "0 0", aMax: "1 1", oXMin: hasIcon ? 40 : 10, oYMin: 0, oXMax: -10, oYMax: 0, text: command.Text,
                    align: hasIcon ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, outlineColor: command.OutlineColor, outlineDistance: "-0.5 -0.5");

                UI.ImageButton(ref container, commandLayer, ZString.Concat("UI_H USECOMMAND ", command.IsConsole, " ", command.Command));

                posY -= alignBottom ? -35 : 35;
            }

            UI.Create(player, container);
        }

        private void ShowAdditionalMenu(BasePlayer player, bool isOpen = false)
        {
            var container = new CuiElementContainer();

            bool isFullState = _plugin._data.PlayersState[player.UserIDString] == States.FULL;

            UI.Panel(ref container, isFullState ? ".Main.bg" : ".Events.bg", ".AdditionalMenuButton.bg", ".AdditionalMenuButton.bg", "0.5 0", "0.5 0",
                bgColor: null);

            UI.Image(ref container, ".AdditionalMenuButton.bg", ".AdditionalMenuButton", oXMin: -16, oYMin: isFullState ? -17 : 4, oXMax: 16,
                oYMax: isFullState ? 4 : 25, image: _plugin._imageHandler.GetImage(_plugin._config.Images.Lip),
                color: UI.OpacityColor(_plugin._config.MainSetup.BGOpacity));

            UI.Image(ref container, ".AdditionalMenuButton",
                image: _plugin._imageHandler.GetImage(isOpen ? _plugin._config.Images.CloseDropMenu : _plugin._config.Images.DropMenu),
                color: _plugin._config.MainSetup.AdditionalMenu.ButtonColor);

            UI.ImageButton(ref container, ".AdditionalMenuButton", isOpen ? "UI_H CHANGEADMENUSTATE True" : "UI_H CHANGEADMENUSTATE False");

            UI.Create(player, container);

            if (isOpen)
                ShowAdditionalMenuCommands(player);
        }

        private void ShowBalance(BasePlayer player)
        {
            if (_plugin._data.PlayersState[player.UserIDString] != States.FULL || _inComputerStation.Contains(player.userID))
                return;

            EconomyAppearance firstEconomy = _plugin._economyHandler.GetEnabled(0);
            if (firstEconomy == null)
                return;

            EconomyAppearance secondEconomy = _plugin._economyHandler.GetEnabled(1);

            int firstBalance = _plugin._economyHandler.GetBalance(firstEconomy, player);
            int secondBalance = secondEconomy == null ? int.MinValue : _plugin._economyHandler.GetBalance(secondEconomy, player);

            if (!HasBalanceChanged(player.userID, firstBalance, secondBalance))
                return;

            string firstText = ZString.Concat(firstEconomy.Currency ?? string.Empty, firstBalance);
            string secondText = secondEconomy == null ? string.Empty : ZString.Concat(secondEconomy.Currency ?? string.Empty, secondBalance);

            var container = new CuiElementContainer();

            UI.Label(ref container, ".Main.bg", s_balanceSlotNames[0], s_balanceSlotNames[0], s_balanceSlotMin[0], s_balanceSlotMax[0], 10, 7, 0, 24,
                firstText, 14, align: TextAnchor.MiddleLeft, color: firstEconomy.Color);

            if (secondEconomy != null)
                UI.Label(ref container, ".Main.bg", s_balanceSlotNames[1], s_balanceSlotNames[1], s_balanceSlotMin[1], s_balanceSlotMax[1], 10, 7, 0, 24,
                    secondText, 14, align: TextAnchor.MiddleLeft, color: secondEconomy.Color);
            else
                UI.Destroy(player, s_balanceSlotNames[1]);

            UI.Create(player, container);
        }

        private void ShowPosition(BasePlayer player)
        {
            if (_plugin._data.PlayersState[player.UserIDString] != States.FULL)
                return;

            if (_plugin.permission.UserHasPermission(player.UserIDString, PERM_STREAMER))
            {
                UI.Destroy(player, ".Position");
                return;
            }

            Vector3 position = player.transform.position;
            bool isGrid = _plugin._config.MainSetup.PlayerPosition.IsGrid;
            long positionKey = isGrid ? GetGridCell(position) : PackCoords((int)position.x, (int)position.z);

            if (!HasPositionChanged(player.userID, positionKey))
                return;

            string positionText = isGrid ? FormatGridCell(positionKey) : ZString.Concat("X:", (int)position.x, "\nZ:", (int)position.z);

            var container = new CuiElementContainer();

            UI.Label(ref container, ".Main.bg", ".Position", ".Position", "1 1", "1 1", -45, -75, -5, -45,
                positionText, _plugin._config.MainSetup.PlayerPosition.IsGrid ? 18 : 10,
                align: TextAnchor.MiddleLeft, color: _plugin._config.MainSetup.PlayerPosition.Color);

            UI.Create(player, container);
        }

        private void ShowTime(BasePlayer player)
        {
            if (_plugin._data.PlayersState[player.UserIDString] != States.FULL)
                return;

            string timeText = _plugin._config.MainSetup.Time.IsAutomaticDetection
                ? _plugin._clockHandler.GetClockText(_plugin._clockHandler.GetCulture(_plugin.lang.GetLanguage(player.UserIDString)).DateTimeFormat
                    .ShortTimePattern)
                : _plugin._clockHandler.GetClockText(_plugin._config.MainSetup.Time.IsAmPm ? "hh:mm tt" : "HH:mm");

            if (!HasInfoTextChanged(_lastTimeText, player.userID, timeText))
                return;

            var container = new CuiElementContainer();

            UI.Label(ref container, ".Main.bg", ".Time", ".Time", "0 1", "0 1", 180, -75, 250, -45,
                timeText, 18, _plugin._config.MainSetup.Time.Color);

            UI.Create(player, container);
        }

        private void ShowPlayersCounts(BasePlayer player = null)
        {
            if (player != null && (_inComputerStation.Contains(player.userID) || _plugin._data.PlayersState[player.UserIDString] != States.FULL))
                return;

            var container = new CuiElementContainer();

            if (_plugin._config.MainSetup.ActivePlayersAppearance.IsEnable)
                UI.Label(ref container, ".Main.bg", ".ActivePlayers.count", ".ActivePlayers.count", "0 1", "1 1", 44, -70, 0, -50,
                    CountActivePlayers().ToString(), align: TextAnchor.MiddleLeft);

            if (_plugin._config.MainSetup.SleepPlayersAppearance.IsEnable)
                UI.Label(ref container, ".Main.bg", ".SleepPlayers.count", ".SleepPlayers.count", "0 1", "1 1", 99, -70, 0, -50,
                    BasePlayer.sleepingPlayerList.Count.ToString(), align: TextAnchor.MiddleLeft);

            if (_plugin._config.MainSetup.QueuePlayersAppearance.IsEnable)
                UI.Label(ref container, ".Main.bg", ".QueuePlayers.count", ".QueuePlayers.count", "0 1", "1 1", 154, -70, 0, -50,
                    (ServerMgr.Instance.connectionQueue.Queued + ServerMgr.Instance.connectionQueue.Joining).ToString(), align: TextAnchor.MiddleLeft);

            if (player == null)
            {
                string json = container.ToJson();
                foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
                {
                    if (!_inComputerStation.Contains(activePlayer.userID) && _plugin._data.PlayersState[activePlayer.UserIDString] == States.FULL)
                        CuiHelper.AddUi(activePlayer, json);
                }
            }

            else
            {
                UI.Create(player, container);
            }
        }

        private void ShowBaseInfo(BasePlayer player)
        {
            var container = new CuiElementContainer();

            int economyCount = _plugin._economyHandler.CountEnabled();
            string serverNameMinX = economyCount >= 2 ? "0.6" : economyCount == 1 ? "0.3" : "0";
            UI.Label(ref container, ".Main.bg", aMin: $"{serverNameMinX} 0", aMax: "1 0", oXMin: 5, oYMin: 7, oXMax: -5, oYMax: 24,
                text: _plugin._config.MainSetup.ServerName, fontSize: 14);

            if (_plugin._config.MainSetup.ActivePlayersAppearance.IsEnable)
                UI.Image(ref container, ".Main.bg", aMin: "0 1", aMax: "0 1", oXMin: 20, oYMin: -70, oXMax: 40, oYMax: -50,
                    image: _plugin._imageHandler.GetImage(_plugin._config.MainSetup.ActivePlayersAppearance.Icon),
                    color: _plugin._config.MainSetup.ActivePlayersAppearance.Color);

            if (_plugin._config.MainSetup.SleepPlayersAppearance.IsEnable)
                UI.Image(ref container, ".Main.bg", aMin: "0 1", aMax: "0 1", oXMin: 75, oYMin: -70, oXMax: 95, oYMax: -50,
                    image: _plugin._imageHandler.GetImage(_plugin._config.MainSetup.SleepPlayersAppearance.Icon),
                    color: _plugin._config.MainSetup.SleepPlayersAppearance.Color);

            if (_plugin._config.MainSetup.QueuePlayersAppearance.IsEnable)
                UI.Image(ref container, ".Main.bg", aMin: "0 1", aMax: "0 1", oXMin: 130, oYMin: -70, oXMax: 150, oYMax: -50,
                    image: _plugin._imageHandler.GetImage(_plugin._config.MainSetup.QueuePlayersAppearance.Icon),
                    color: _plugin._config.MainSetup.QueuePlayersAppearance.Color);

            UI.Create(player, container);

            ShowPlayersCounts(player);

            if (_plugin._config.MainSetup.Time.IsEnable)
                ShowTime(player);

            if (_plugin._config.MainSetup.PlayerPosition.IsEnable)
                ShowPosition(player);

            if (_plugin._economyHandler.IsAnyEnabled())
                ShowBalance(player);
        }

        private void ShowMainButton(BasePlayer player)
        {
            var container = new CuiElementContainer();

            string anchor = _plugin._config.MainSetup.Position.IsAlignRight() ? "1 1" : "0 1";

            UI.Image(ref container, ".Offsets.bg", ".Burger", ".Burger", anchor, anchor, -35, -65, 45, 15,
                _plugin._imageHandler.GetImage(_plugin._config.MainSetup.Logo));

            UI.ImageButton(ref container, ".Burger", "UI_H CHANGESTATE");

            UI.Create(player, container);
        }

        private void ShowMain(BasePlayer player)
        {
            if (_inComputerStation.Contains(player.userID))
                return;

            InvalidateInfoText(player.userID);

            States state = _plugin._data.PlayersState[player.UserIDString];
            bool alignRight = _plugin._config.MainSetup.Position.IsAlignRight();
            int eventsOverflow = _plugin._eventsHandler.EnabledCount > 7 ? (_plugin._eventsHandler.EnabledCount - 7) * 34 : 0;

            var container = new CuiElementContainer();

            if (state == States.FULL)
                UI.Image(ref container, ".Offsets.bg", ".Main.bg", null, null, null, alignRight ? -300 : 0, -100, alignRight ? 0 : 300, 0,
                    _plugin._imageHandler.GetImage(_plugin._config.Images.Background), UI.OpacityColor(_plugin._config.MainSetup.EventsBGOpacity));

            if (state != States.HIDE)
                UI.Image(ref container, ".Offsets.bg", ".Events.bg", null, "0 1", "0 1", alignRight ? -300 - eventsOverflow : 0, -60,
                    alignRight ? 0 : 300 + eventsOverflow, 0, _plugin._imageHandler.GetImage(_plugin._config.Images.Events),
                    UI.OpacityColor(_plugin._config.MainSetup.BGOpacity));

            UI.Destroy(player, ".Main.bg");
            UI.Destroy(player, ".Events.bg");
            UI.Destroy(player, ".bg.Messages.bg");

            UI.Create(player, container);

            if (_plugin._config.MainSetup.AdditionalMenu.IsEnable && state != States.HIDE && state != States.CLOSE)
                ShowAdditionalMenu(player);

            if (_plugin._config.MainSetup.InfoMessages.IsEnable)
                ShowMessagesInfo(player);

            if (state == States.FULL)
                ShowBaseInfo(player);

            ShowEvents(player);

            ShowMainButton(player);
        }

        private void ShowMessagesInfo(BasePlayer player)
        {
            if (_inComputerStation.Contains(player.userID) || _plugin._data.PlayersState[player.UserIDString] == States.HIDE ||
                _plugin._data.PlayersState[player.UserIDString] == States.CLOSE)
                return;

            var container = new CuiElementContainer();

            InfoMessage infoMessages = _plugin._config.MainSetup.InfoMessages;
            string align = infoMessages.Align;
            string outline = infoMessages.UseOutline ? "-0.5 -0.5" : null;
            string anchor = ContainsIgnoreCase(align, "bottom") ? "0.5 0" : ContainsIgnoreCase(align, "right") ? "1 1" : "0.5 1";

            UI.MainParent(ref container, ".Messages.bg", anchor, anchor, infoMessages.IsOverall, false, false);

            if (string.Equals(align, "bottomcenter", StringComparison.OrdinalIgnoreCase))
                UI.Label(ref container, ".bg.Messages.bg", aMin: "0 0", aMax: "0 0", oXMin: infoMessages.Width / -2 + infoMessages.OffsetR,
                    oYMin: infoMessages.OffsetTB, oXMax: infoMessages.Width / 2 + infoMessages.OffsetR, oYMax: infoMessages.OffsetTB + 20,
                    text: infoMessages.Messages.GetRandom(), fontSize: 14, outlineDistance: outline, outlineColor: infoMessages.OutlineColor,
                    wrapMode: VerticalWrapMode.Overflow);

            else if (string.Equals(align, "topcenter", StringComparison.OrdinalIgnoreCase))
                UI.Label(ref container, ".bg.Messages.bg", aMin: "0 0", aMax: "0 0", oXMin: infoMessages.Width / -2 + infoMessages.OffsetR,
                    oYMin: -infoMessages.OffsetTB - 20, oXMax: infoMessages.Width / 2 + infoMessages.OffsetR, oYMax: -infoMessages.OffsetTB,
                    text: infoMessages.Messages.GetRandom(), fontSize: 14, outlineDistance: outline, outlineColor: infoMessages.OutlineColor,
                    wrapMode: VerticalWrapMode.Overflow);

            else if (string.Equals(align, "topright", StringComparison.OrdinalIgnoreCase))
                UI.Label(ref container, ".bg.Messages.bg", aMin: "0 0", aMax: "0 0", oXMin: -infoMessages.Width - infoMessages.OffsetR,
                    oYMin: -infoMessages.OffsetTB - 20, oXMax: -infoMessages.OffsetR, oYMax: -infoMessages.OffsetTB,
                    text: infoMessages.Messages.GetRandom(), fontSize: 14, outlineDistance: outline, outlineColor: infoMessages.OutlineColor,
                    wrapMode: VerticalWrapMode.Overflow);

            UI.Create(player, container);
        }

        public void ShowBackground(BasePlayer player)
        {
            if (_plugin._data.PlayersState[player.UserIDString] == States.CLOSE)
                return;

            if (_inComputerStation.Contains(player.userID))
            {
                UI.Destroy(player, ".bg");
                UI.Destroy(player, ".bg.Messages.bg");
                return;
            }

            var container = new CuiElementContainer();

            string align = _plugin._config.MainSetup.Position.Align;

            if (string.Equals(align, "topright", StringComparison.OrdinalIgnoreCase))
            {
                UI.MainParent(ref container, null, "1 1", "1 1", _plugin._config.MainSetup.IsOverall, false, false);
                UI.Panel(ref container, ".bg", ".Offsets.bg", null, "0.5 0", "0.5 0", -_plugin._config.MainSetup.Position.LeftOffSet,
                    -_plugin._config.MainSetup.Position.TopOffSet, -_plugin._config.MainSetup.Position.LeftOffSet,
                    -_plugin._config.MainSetup.Position.TopOffSet, null);
            }

            else if (string.Equals(align, "bottomleft", StringComparison.OrdinalIgnoreCase))
            {
                UI.MainParent(ref container, null, "0 0", "0 0", _plugin._config.MainSetup.IsOverall, false, false);
                UI.Panel(ref container, ".bg", ".Offsets.bg", null, "0.5 0", "0.5 0", _plugin._config.MainSetup.Position.LeftOffSet,
                    120 + _plugin._config.MainSetup.Position.TopOffSet, _plugin._config.MainSetup.Position.LeftOffSet,
                    120 + _plugin._config.MainSetup.Position.TopOffSet, null);
            }

            else if (string.Equals(align, "bottomright", StringComparison.OrdinalIgnoreCase))
            {
                UI.MainParent(ref container, null, "1 0", "1 0", _plugin._config.MainSetup.IsOverall, false, false);
                UI.Panel(ref container, ".bg", ".Offsets.bg", null, "0.5 0", "0.5 0", -_plugin._config.MainSetup.Position.LeftOffSet,
                    120 + _plugin._config.MainSetup.Position.TopOffSet, -_plugin._config.MainSetup.Position.LeftOffSet,
                    120 + _plugin._config.MainSetup.Position.TopOffSet, null);
            }

            else
            {
                UI.MainParent(ref container, null, "0 1", "0 1", _plugin._config.MainSetup.IsOverall, false, false);
                UI.Panel(ref container, ".bg", ".Offsets.bg", null, "0.5 0", "0.5 0", _plugin._config.MainSetup.Position.LeftOffSet,
                    -_plugin._config.MainSetup.Position.TopOffSet, _plugin._config.MainSetup.Position.LeftOffSet, -_plugin._config.MainSetup.Position.TopOffSet,
                    null);
            }

            UI.Create(player, container);

            ShowMain(player);
        }

#endregion
    }

#endregion

#region Utility

    private static class UI
    {
        private const string LAYER = "UI_Hud";
        private const string MAIN_LAYER = LAYER + ".bg";

        private static readonly Dictionary<string, string> s_colorCache = new();
        private static readonly Dictionary<string, string> s_layerCache = new();
        private static readonly Dictionary<string, string> s_mainLayerCache = new();
        private static readonly Dictionary<long, string> s_scaledOffsets = new();
        private static readonly Dictionary<long, string> s_rawOffsets = new();
        private static readonly string[] s_opacityColors = new string[101];

        private static float s_size = 1;

        public static void SetSize(float size)
        {
            s_size = size;
            s_scaledOffsets.Clear();
        }

        public static void Create(BasePlayer player, CuiElementContainer container)
        {
            if (player != null)
                CuiHelper.AddUi(player, container);
        }

        public static void Destroy(BasePlayer player, string layer)
        {
            CuiHelper.DestroyUi(player, Layer(layer));
        }

        public static void DestroyToAll(string layer)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                Destroy(player, layer);
            }
        }

        public static string OpacityColor(int opacity)
        {
            if (opacity < 0 || opacity > 100)
                return BuildOpacityColor(opacity);

            string color = s_opacityColors[opacity];
            if (color != null)
                return color;

            color = BuildOpacityColor(opacity);
            s_opacityColors[opacity] = color;

            return color;
        }

        private static string HexToRustFormat(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return "0 0 0 0";

            if (s_colorCache.TryGetValue(hex, out string formatted))
                return formatted;

            formatted = BuildRustFormat(hex);
            s_colorCache[hex] = formatted;

            return formatted;
        }

        public static void MainParent(ref CuiElementContainer container, string name = null, string aMin = "0.5 0.5", string aMax = "0.5 0.5",
            bool overAll = true, bool keyboardEnabled = true, bool cursorEnabled = true, string color = "0 0 0 0", string material = null, string sprite = null)
        {
            string layer = MainLayer(name);

            container.Add(new CuiPanel
            {
                KeyboardEnabled = keyboardEnabled,
                CursorEnabled = cursorEnabled,
                RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                Image = { Color = color, Material = material, Sprite = sprite }
            }, overAll ? "Overlay" : "Hud", layer, layer);
        }

        public static void Panel(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
            string aMax = "1 1", int oXMin = 0, int oYMin = 0, int oXMax = 0, int oYMax = 0, string bgColor = "0.33 0.33 0.33 1", string material = null,
            string sprite = null, int itemID = 0, ulong skinID = 0, bool ignoreSize = false)
        {
            container.Add(new CuiElement
            {
                Parent = Layer(parent),
                Name = Layer(name),
                DestroyUi = destroy == null ? null : Layer(destroy),
                Components =
                {
                    new CuiRectTransformComponent
                    {
                        AnchorMin = aMin, AnchorMax = aMax, OffsetMin = Offset(oXMin, oYMin, ignoreSize), OffsetMax = Offset(oXMax, oYMax, ignoreSize)
                    },
                    new CuiImageComponent { Color = HexToRustFormat(bgColor), Material = material, Sprite = sprite, ItemId = itemID, SkinId = skinID }
                }
            });
        }

        public static void Image(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
            string aMax = "1 1", int oXMin = 0, int oYMin = 0, int oXMax = 0, int oYMax = 0, string image = "", string color = "1 1 1 1",
            bool ignoreSize = false)
        {
            container.Add(new CuiElement
            {
                Parent = Layer(parent),
                Name = Layer(name),
                DestroyUi = destroy == null ? null : Layer(destroy),
                Components =
                {
                    new CuiRectTransformComponent
                    {
                        AnchorMin = aMin, AnchorMax = aMax, OffsetMin = Offset(oXMin, oYMin, ignoreSize), OffsetMax = Offset(oXMax, oYMax, ignoreSize)
                    },
                    new CuiRawImageComponent
                    {
                        Png = !image.StartsWith("http") && !image.StartsWith("www") ? image : null,
                        Url = image.StartsWith("http") || image.StartsWith("www") ? image : null, Color = HexToRustFormat(color),
                        Sprite = "assets/content/textures/generic/fulltransparent.tga"
                    }
                }
            });
        }

        public static void Label(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
            string aMax = "1 1", int oXMin = 0, int oYMin = 0, int oXMax = 0, int oYMax = 0, string text = null, int fontSize = 16, string color = "1 1 1 1",
            TextAnchor align = TextAnchor.MiddleCenter, string outlineDistance = null, string outlineColor = "0 0 0 1",
            VerticalWrapMode wrapMode = VerticalWrapMode.Truncate, string font = "robotocondensed-regular.ttf", bool ignoreSize = false)
        {
            container.Add(new CuiElement
            {
                Parent = Layer(parent),
                Name = Layer(name),
                DestroyUi = destroy == null ? null : Layer(destroy),
                Components =
                {
                    new CuiRectTransformComponent
                    {
                        AnchorMin = aMin, AnchorMax = aMax, OffsetMin = Offset(oXMin, oYMin, ignoreSize), OffsetMax = Offset(oXMax, oYMax, ignoreSize)
                    },
                    new CuiTextComponent
                    {
                        Text = text, FontSize = ignoreSize ? fontSize : (int)(fontSize * s_size), Color = HexToRustFormat(color), Align = align, Font = font,
                        VerticalOverflow = wrapMode
                    },
                    outlineDistance == null
                        ? new CuiOutlineComponent { Distance = "0 0", Color = "0 0 0 0" }
                        : new CuiOutlineComponent { Distance = outlineDistance, Color = HexToRustFormat(outlineColor) }
                }
            });
        }

        public static void Input(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
            string aMax = "1 1", int oXMin = 0, int oYMin = 0, int oXMax = 0, int oYMax = 0, string text = null, int limit = 256, int fontSize = 16,
            string color = "1 1 1 1", string command = null, TextAnchor align = TextAnchor.MiddleCenter, bool autoFocus = false, bool hudMenuInput = false,
            bool readOnly = false, bool isPassword = false, bool needsKeyboard = false, bool singleLine = true, string font = "robotocondensed-regular.ttf",
            bool ignoreSize = false)
        {
            container.Add(new CuiElement
            {
                Parent = Layer(parent),
                Name = Layer(name),
                DestroyUi = destroy == null ? null : Layer(destroy),
                Components =
                {
                    new CuiRectTransformComponent
                    {
                        AnchorMin = aMin, AnchorMax = aMax, OffsetMin = Offset(oXMin, oYMin, ignoreSize), OffsetMax = Offset(oXMax, oYMax, ignoreSize)
                    },
                    new CuiInputFieldComponent
                    {
                        Text = text, Command = command, CharsLimit = limit, FontSize = ignoreSize ? fontSize : (int)(fontSize * s_size),
                        Color = HexToRustFormat(color), Align = align, Font = font, Autofocus = autoFocus, IsPassword = isPassword, ReadOnly = readOnly,
                        HudMenuInput = hudMenuInput, NeedsKeyboard = needsKeyboard,
                        LineType = singleLine ? InputField.LineType.SingleLine : InputField.LineType.MultiLineNewline
                    }
                }
            });
        }

        public static void TextButton(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
            string aMax = "1 1", int oXMin = 0, int oYMin = 0, int oXMax = 0, int oYMax = 0, string text = null, int fontSize = 16, string color = "1 1 1 1",
            string command = null, string bgColor = "0 0 0 0", VerticalWrapMode wrapMode = VerticalWrapMode.Truncate,
            TextAnchor align = TextAnchor.MiddleCenter, string font = "robotocondensed-regular.ttf", string material = null, string close = null,
            bool ignoreSize = false)
        {
            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = aMin, AnchorMax = aMax, OffsetMin = Offset(oXMin, oYMin, ignoreSize), OffsetMax = Offset(oXMax, oYMax, ignoreSize)
                },
                Text =
                {
                    Text = text, FontSize = ignoreSize ? fontSize : (int)(fontSize * s_size), Color = HexToRustFormat(color), Align = align, Font = font,
                    VerticalOverflow = wrapMode
                },
                Button =
                {
                    Command = command, Close = close != null ? Layer(close) : command == null ? Layer(name) : null, Color = HexToRustFormat(bgColor),
                    Material = material
                }
            }, Layer(parent), Layer(name), destroy == null ? null : Layer(destroy));
        }

        public static void ImageButton(ref CuiElementContainer container, string parent, string command = null, string name = null, string aMin = "0 0",
            string aMax = "1 1", int oXMin = 0, int oYMin = 0, int oXMax = 0, int oYMax = 0, bool ignoreSize = false)
        {
            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = aMin, AnchorMax = aMax, OffsetMin = Offset(oXMin, oYMin, ignoreSize), OffsetMax = Offset(oXMax, oYMax, ignoreSize)
                },
                Button = { Command = command, Close = command == null ? name : null, Color = "0 0 0 0" }
            }, Layer(parent), Layer(name));
        }

#region Methods

        private static string Layer(string name)
        {
            if (name == null)
                return LAYER;

            if (s_layerCache.TryGetValue(name, out string layer))
                return layer;

            layer = string.Concat(LAYER, name);
            s_layerCache[name] = layer;

            return layer;
        }

        private static string MainLayer(string name)
        {
            if (name == null)
                return MAIN_LAYER;

            if (s_mainLayerCache.TryGetValue(name, out string layer))
                return layer;

            layer = string.Concat(MAIN_LAYER, name);
            s_mainLayerCache[name] = layer;

            return layer;
        }

        private static string Offset(int x, int y, bool ignoreSize)
        {
            long key = ((long)x << 32) | (uint)y;

            if (ignoreSize)
            {
                if (s_rawOffsets.TryGetValue(key, out string rawOffset))
                    return rawOffset;

                rawOffset = ZString.Concat(x, " ", y);
                s_rawOffsets[key] = rawOffset;

                return rawOffset;
            }

            if (s_scaledOffsets.TryGetValue(key, out string offset))
                return offset;

            offset = string.Concat((x * s_size).ToString(CultureInfo.InvariantCulture), " ", (y * s_size).ToString(CultureInfo.InvariantCulture));
            s_scaledOffsets[key] = offset;

            return offset;
        }

        private static string BuildOpacityColor(int opacity)
        {
            return string.Concat("1 1 1 ", (opacity / 100f).ToString(CultureInfo.InvariantCulture));
        }

        private static string BuildRustFormat(string hex)
        {
            int separator = hex.IndexOf(":", StringComparison.Ordinal);

            if (separator >= 0)
                return ColorUtility.TryParseHtmlString(hex.Substring(0, separator), out Color parsed)
                    ? $"{parsed.r:F2} {parsed.g:F2} {parsed.b:F2} {hex.Substring(separator + 1, hex.Length - separator - 1)}"
                    : hex;

            return ColorUtility.TryParseHtmlString(hex, out Color color) ? $"{color.r:F2} {color.g:F2} {color.b:F2} {color.a:F2}" : hex;
        }

#endregion
    }

#endregion
} 