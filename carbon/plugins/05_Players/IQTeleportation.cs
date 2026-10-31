using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConVar;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using Physics = UnityEngine.Physics;
using ProtoBuf;
using Rust;
using UnityEngine.Networking;
using Object = System.Object;
using Pool = Facepunch.Pool;
using Vector3 = UnityEngine.Vector3;

namespace Oxide.Plugins
{
    [Info("IQTeleportation", "Mercury", "1.37.7")]
    [Description("IQTeleportation")]
    class IQTeleportation : RustPlugin
    {
        /// TODO: Сделать "порог" реакции на холод и т.д (?)
        //TODO: Наименование к варпу (?)
        /// <summary>
        /// - Исправление отображения ника в UI при получения запроса на телепортацию, ранее отображался старый ник из-за того, что он застрял в кэше и не обновился
        /// - Добавлена дополнительная проверка на наличие авторизации в точке дома для телепортации домой к тиммейту
        /// - Синхронизированы проверки между телепортацией на варп через консольную команду и чат команду, ранее можно было избежать проверку при телепортации через F1 на варп
        /// - Добавлен запрет на телепортацию игрока - если он связан (в наручниках) или сдаётся
        /// </summary>
        
        #region Vars
        
        private Timer timerCheckHomes = null;
        private Timer timerCheckLimit;

        private static IQTeleportation _;
        private static InterfaceBuilder _interface;
        private static ImageUI _imageUI;

        private const Boolean LanguageEn = true;
        
        private readonly LayerMask homelayerMaskBuilding = LayerMask.GetMask("Construction"); 
        private readonly LayerMask homelayerMaskEntity = LayerMask.GetMask("Default", "Deployed");
        
        private const Int32 limitMemoryLastHomes = 6;
        
        private const Single checkIntervalPlayerTeleportation = 1f;
        private const Single radiusCheckLayerHome = 2f;
        private const Single uiAlertFade = 0;
        
        private Boolean useHomeLimit;
        private Boolean useWarpLimit;
        private Boolean useTeleportationLimit;

        private const Double secondsPerHour = 3600d;
        private const Single checkInterval = 1800f;

        private sealed class LimitSpec
        {
            public TypeLimit LimitType { get; }
            public Boolean Enabled { get; }
            public Func<Double> GetTime { get; }
            public Action<Double> SetTime { get; }
            public Func<Int32> GetHours { get; }

            public LimitSpec(
                TypeLimit limitType,
                Boolean enabled,
                Func<Double> getTime,
                Action<Double> setTime,
                Func<Int32> getHours)
            {
                LimitType = limitType;
                Enabled = enabled;
                GetTime = getTime;
                SetTime = setTime;
                GetHours = getHours;
            }
        }
        private static Double CurrentTime => Facepunch.Math.Epoch.Current;

        private const String permissionGMapTeleport = "iqteleportation.gmap";
        private const String permissionTP = "iqteleportation.tp";
        private const String permissionWarpAdmin = "iqteleportation.warpadmin";

        private const String effectSoundError = "assets/prefabs/weapons/toolgun/effects/repairerror.prefab";
        private const String effectSoundAccess = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab";
        private const String effectSoundTimer = "assets/prefabs/weapons/mp5/effects/fire_select.prefab";
        private const String effectSoundTeleport = "assets/prefabs/misc/halloween/lootbag/effects/loot_bag_upgrade.prefab";
        
        private Dictionary<BasePlayer, SleepingBag> bagInstalled = new();
        private List<MonumentInfo> localAllMonuments = new();
        private Dictionary<String, List<String>> cachedUI = new();
        private Dictionary<BasePlayer, Action> teleportationActions = new();
        private Dictionary<BasePlayer, BasePlayer> casheTeleportationLast = new ();
        private Dictionary<UInt64, TeleportationQueueInfo> playerTeleportationQueue = new();
        private Dictionary<BasePlayer, UserRepository> localUsersRepository = new();
        private Dictionary<UInt64, Tugboat> tugboatsServer = new();
        private Dictionary<UInt64, PlayerBoat> playersBoatsServer = new();
        private Dictionary<UInt64, AutoTurret> turretsEx = new();
        private List<BasePlayer> playerLoaded = new List<BasePlayer>();
        private Dictionary<UInt64, ApartmentRoom> apartmentRoomsServer = new();
        private ApartmentBuilding apartmentBuilding;
        private Single apartmentSearchRadiusSqr;
        
        private const String homeLog = LanguageEn ? "went to the home point" : "отправился на точку дома";
        private const String tprLog  = LanguageEn ? "sent a teleport request to" : "отправил запрос на телепортацию к";
        private const String tpaLog  = LanguageEn ? "accepted a teleport request from" : "принял запрос на телепортацию от";

        private const String colorHomeDefault = "0.4509804 0.5568628 0.2745098 1";
        private const String colorHomeRequestTime = "0.631 0.416 0.271 1";
        private const String colorHomeCooldown = "0.31 0.302 0.29 1";
        
        private enum TypeLog
        {
            RequestTeleportation,
            AcceptTeleportation,
            RequestHomeTeleportation,
        }

        [JsonConverter(typeof(StringEnumConverter))] 
        private enum TypeLimit
        {
            Home,
            Teleportation,
            Warp
        }
        
        private readonly List<String> exceptionsBuildingBlockEntities = new()
        {
            "rug.deployed",
            "rug.bear.deployed",
            "sleepingbag_leather_deployed",
            "bed_deployed",
            "beachtowel.deployed",
            "box.wooden.large",
            "woodbox",
            "coffinstorage", 
        };
        
        private readonly List<UInt64> ItemIdCorrecteds = new List<UInt64> { 1074866732, 2004072627, 25180052, 2006957888, 930560607, 1123047824, 1130765085, 442289265, 090353317, 15518008, 118372687 }; 
        
        private class TeleportationQueueInfo
        {
            public BasePlayer player;
            public Boolean isActive;
            public Boolean isAccepted;
        }
        
        private class UserRepository
        {
            public Coroutine activeTeleportation;
            public Double cooldownWarp;
            public Double cooldownHome;
            public Double cooldownTeleportation;
            
            public enum TeleportType
            {
                Warp,
                Home,
                Teleportation
            }
            
            public void SetupCooldown(BasePlayer player, TeleportType teleportType)
            {
                Int32 cooldown = teleportType switch
                {
                    TeleportType.Home => config.homeController.homeCooldown.GetCount(player),
                    TeleportType.Teleportation => config.teleportationController.teleportCooldown.GetCount(player),
                    TeleportType.Warp => config.warpsContoller.teleportWarpCooldown.GetCount(player),
                    _ => throw new ArgumentOutOfRangeException(nameof(teleportType), teleportType, null)
                };

                switch (teleportType)
                {
                    case TeleportType.Home:
                        cooldownHome = CurrentTime + cooldown;
                        break;
                    case TeleportType.Teleportation:
                        cooldownTeleportation = CurrentTime + cooldown;
                        break;
                    case TeleportType.Warp:
                        cooldownWarp = CurrentTime + cooldown;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(teleportType), teleportType, null);
                }

                DeActiveTeleportation();
            }
            
            public String GetCooldownTitle(BasePlayer player, TeleportType teleportType)
            {
                Double cooldownLeft = teleportType switch
                {
                    TeleportType.Warp => cooldownWarp - CurrentTime,
                    TeleportType.Home => cooldownHome - CurrentTime,
                    _ => cooldownTeleportation - CurrentTime
                };
                
                String cooldownLeftTitle = cooldownLeft <= 0 ? String.Empty : _.FormatTime(cooldownLeft, player.UserIDString);
                return cooldownLeftTitle;
            }
            
            public Int32 GetCooldownHome(BasePlayer player)
            {
                Double cooldownLeft = cooldownHome - CurrentTime;
                return (Int32)cooldownLeft;
            }

            public Boolean IsActiveTeleportation() => activeTeleportation != null;

            public void ActiveCoroutineController(Coroutine coroutine, BasePlayer target = null)
            {
                DeActiveTeleportation();
                activeTeleportation = coroutine;
            }

            public void DeActiveTeleportation()
            {
                if (!IsActiveTeleportation()) return;
                ServerMgr.Instance.StopCoroutine(activeTeleportation);
                activeTeleportation = null;
            }
        }
        
        #endregion

        #region Reference

        [PluginReference] Plugin IQChat, nanoChat, Clans, Friends, RustApp, SimpleStatus, nanoModalMenu;

        #region Chats

        private void SendChat(String message, BasePlayer player, Boolean isError = false)
        {
            if (config.generalController.useOnlyGameTip)
                player.SendConsoleCommand("gametip.showtoast", new Object[]{ "1", message, "15" });
            else
            {
                if (IQChat)
                    IQChat?.Call("API_ALERT_PLAYER", player, message, config.generalController.iqchatSetting.customPrefix, config.generalController.iqchatSetting.customAvatar);
                else if (nanoChat)
                    nanoChat?.Call("API_SendPlayerMessage", player, message, config.generalController.iqchatSetting.customPrefix);
                else player.SendConsoleCommand("chat.add", Chat.ChatChannel.Global, 0, message);
            }

            RunEffect(player, isError ? effectSoundError : effectSoundAccess);
        }
        
        #endregion

        #region Simple Status

        private void SimpleStatus_CreateStatuses()
        {
            if (!SimpleStatus) return;
            if (!config.generalController.simpleStatusController.useSimpleStatus) return;

            Configuration.GeneralController.SimpleStatusController.Property teleportationProperty = config.generalController.simpleStatusController.settingTeleportationProcess;

            foreach (KeyValuePair<String, String> titleLanguage in teleportationProperty.titleLanguages)
            {
                SimpleStatus.CallHook("CreateStatus", this, $"{titleLanguage.Key}_timerTeleportation", new Dictionary<String, Object>
                {
                    ["color"] = teleportationProperty.colorPanel,
                    ["title"] = titleLanguage.Value,
                    ["titleColor"] = teleportationProperty.titleColor,
                    ["icon"] = teleportationProperty.spriteIcon,
                    ["iconColor"] = teleportationProperty.iconColor,
                    ["text"] = null
                });
            }
        }

        private void SimpleStatus_SetStatus(BasePlayer player, String nameStatus, Int32 duration)
        {
            if(!SimpleStatus) return;
            if (!config.generalController.simpleStatusController.useSimpleStatus) return;
            if (String.IsNullOrWhiteSpace(nameStatus)) return;
            
            SimpleStatus.CallHook("SetStatus", player.UserIDString, nameStatus, duration);
        }
        
        private void SimpleStatus_DestroyStatus(BasePlayer player, String nameStatus)
        {
            if(!SimpleStatus) return;
            if (!config.generalController.simpleStatusController.useSimpleStatus) return;
            if (String.IsNullOrWhiteSpace(nameStatus)) return;
            
            SimpleStatus.CallHook("SetStatus", player.UserIDString, nameStatus, 0);
        }

        #endregion
        
        #region Friends / Clans

        private Boolean IsFriends(BasePlayer player, UInt64 targetID)
        {
            if (player.userID == targetID) return true;
            UInt64[] FriendList = GetFriendList(player);
            return FriendList != null && FriendList.Contains(targetID);
        }
        
        
        private UInt64[] GetFriendList(BasePlayer targetPlayer)
        {
            List<UInt64> friendList = Pool.Get<List<UInt64>>();
            if (Friends)
            {
                if (Friends.Call("GetFriends", targetPlayer.userID.Get()) is UInt64[] frinedList)
                    friendList.AddRange(frinedList);
            }
            
            if (Clans)
            {
                if (Clans.Call("GetClanMembers", targetPlayer.UserIDString) is UInt64[] ClanMembers)
                    friendList.AddRange(ClanMembers);
            }

            if(targetPlayer.Team != null)
                friendList.AddRange(targetPlayer.Team.members);
            
            UInt64[] friendsArray = friendList.ToArray();
            Pool.FreeUnmanaged(ref friendList);
            
            return friendsArray;
        }
        
        #endregion

        #region RaidBlock

        public Boolean IsRaidBlocked(BasePlayer player)
        {
            Object isRB = Interface.Call("IsRaidBlocked", player);
            Boolean isRaidBlocked = isRB as Boolean? ?? false;
            
            return isRaidBlocked;
        }

        #endregion

        #region RustApp
        private void RustApp_SendNotify(String messageNotify) => RustApp.Call("RA_CreateAlert", this, messageNotify, null, null);

        #endregion

        #endregion
        
        #region Configuration 
        private static Configuration config = new Configuration();
        public class Configuration
        {
            [JsonProperty(LanguageEn ? "General plugin settings" : "Основные настройки плагина")]
            public GeneralController generalController = new GeneralController();
            [JsonProperty(LanguageEn ? "Teleport-to-player settings" : "Настройка телепортации к игроку")]
            public TeleportationController teleportationController = new TeleportationController();
            [JsonProperty(LanguageEn ? "Home points settings" : "Настройка точек для дома")]
            public HomeController homeController = new HomeController();
            [JsonProperty(LanguageEn ? "Warp system settings" : "Настройка системы варпов")]
            public WarpsController warpsContoller = new WarpsController();

            internal class WarpsController
            {
                [JsonProperty(LanguageEn ? "Enable warp support (true = yes / false = no)" : "Включить поддержку варпов (true - да/false - нет)")]
                public Boolean useWarps;
                [JsonProperty(LanguageEn ? "Block teleport to a warp if the player is hostile (safe-zone aggression) (true = yes / false = no)" : "Запретить телепортацию на варп, если игрок в розыске (агрессивный для безопасных зон) (true - да/false - нет)")]
                public Boolean noTeleportWaprHostile = true;
                [JsonProperty(LanguageEn ? "Teleport-to-warp cooldown settings" : "Настройка времени перезарядки телепортации на варп")]
                public PresetOtherSetting teleportWarpCooldown = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Teleport-to-warp countdown settings" : "Настройка времени телепортации на варп")]
                public PresetOtherSetting teleportWarpCountdown = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Permissions for teleporting to specific warps: [WarpKey] = Permission (otherwise available to everyone)" : "Настройка разрешений для телепортации на определенные варпы [КлючВарпа] = Разрешение (иначе будут доступны всем)")]
                public Dictionary<String, String> warpPermissions = new Dictionary<String, String>();
                [JsonProperty(LanguageEn ? "Teleport-to-warp limit settings" : "Настройка лимитов телепортации игрока на варп")]
                public LimitSettings limitWarps = new LimitSettings();


                public class LimitSettings
                {
                    [JsonProperty(LanguageEn ? "Use player teleport-to-warp limits (true = yes / false = no)" : "Использовать лимиты телепортации игрока на варп (true - да/false - нет)")]
                    public Boolean useLimitWarp;
                    [JsonProperty(LanguageEn ? "Hours until player limits are reset" : "Через сколько часов сбрасывать лимиты игрокам")]
                    public Int32 hoursResetLimit;
                    [JsonProperty(LanguageEn ? "Teleport-to-warp limit settings" : "Настройка лимитов телепортации игрока на варп")]
                    public PresetOtherSetting limitWarps = new PresetOtherSetting();

                }
            }

            internal class GeneralController
            {
                [JsonProperty(LanguageEn ? "Log player actions to file (true = yes / false = no)" : "Использовать логирование действий игроков в файл (true - да/false - нет)")]
                public Boolean useLogged;
                [JsonProperty(LanguageEn ? "Use sound effects (true = yes / false = no)" : "Использовать звуковые эффекты (true - да/false - нет)")]
                public Boolean useSoundEffects;
                [JsonProperty(LanguageEn ? "Use GameTip messages instead of chat (true = yes / false = no)" : "Использовать GameTip сообщения, вместо сообщений в чате (true - да/false - нет)")]
                public Boolean useOnlyGameTip;
                [JsonProperty(LanguageEn ? "Wake the player immediately after teleport (otherwise they remain 'sleeping') (true = yes / false = no)" : "Поднимать игрока сразу после телепортации, иначе он будет в состоянии `сна` (true - да/false - нет)")]
                public Boolean stopSleepingAfterTeleport;
                [JsonProperty(LanguageEn ? "List of monuments where teleportation is forbidden (applies to home points / teleport-to-player / warps)" : "Список монументов с которых запрещено телепортироваться (Распространяется на точки дома/телепортацию к игрокам/варпы)")]
                public List<String> monumentBlockedTeleportation = new List<String>();
                [JsonProperty(LanguageEn ? "IQChat settings" : "Настройки IQChat")]
                public IQChatController iqchatSetting = new IQChatController();
                [JsonProperty(LanguageEn ? "SimpleStatus settings" : "Настройки SimpleStatus")]
                public SimpleStatusController simpleStatusController = new SimpleStatusController();

                
                internal class IQChatController
                {
                    [JsonProperty(LanguageEn ? "IQChat : Custom prefix in the chat" : "IQChat : Кастомный префикс в чате")]
                    public String customPrefix;
                    [JsonProperty(LanguageEn ? "IQChat : Custom avatar in the chat (Steam64ID) (If required)" : "IQChat : Кастомный аватар в чате (Steam64ID) (Если требуется)")]
                    public String customAvatar;
                }

                internal class SimpleStatusController
                {
                    [JsonProperty(LanguageEn ? "SimpleStatus: Enable plugin support" : "SimpleStatus : Включить поддержку плагина")]
                    public Boolean useSimpleStatus;
                    [JsonProperty(LanguageEn ? "SimpleStatus: UI settings for teleportation" : "SimpleStatus : Настройка UI для SimpleStatus на телепортацию")]
                    public Property settingTeleportationProcess = new Property();
                    internal class Property
                    {
                        [JsonProperty(LanguageEn ? "SimpleStatus: Background panel color" : "SimpleStatus : Цвет задней панели")]
                        public String colorPanel;
                        [JsonProperty(LanguageEn ? "SimpleStatus: Prefix translations [LanguageKey] = Text" : "SimpleStatus : Переводы префикса [КлючЯзыка] = Текст")]
                        public Dictionary<String, String> titleLanguages = new Dictionary<String, String>();
                        [JsonProperty(LanguageEn ? "SimpleStatus: Prefix color" : "SimpleStatus : Цвет префикса")]
                        public String titleColor;
                        [JsonProperty(LanguageEn ? "SimpleStatus: UI sprite" : "SimpleStatus : Спрайт для UI")]
                        public String spriteIcon;
                        [JsonProperty(LanguageEn ? "SimpleStatus: Sprite color" : "SimpleStatus : Цвет спрайта")]
                        public String iconColor;

                        public String GetFormatNameStatus(BasePlayer player, String nameStatus)
                        {
                            String langKey = GetLanguageKey(player);
                            return String.IsNullOrWhiteSpace(langKey) ? String.Empty : $"{langKey}_{nameStatus}";
                        }
                        
                        private String GetLanguageKey(BasePlayer player)
                        {
                            if(titleLanguages == null || titleLanguages.Count == 0) return String.Empty;
                            
                            String languageKey = !player ? (LanguageEn ? "en" : "ru") : _.lang.GetLanguage(player.UserIDString);
                            return titleLanguages.ContainsKey(languageKey) ? languageKey :
                                titleLanguages.ContainsKey("en") ? "en" : titleLanguages.First().Key;
                        }
                    }
                }
            }

            internal class TeleportationController
            {
                [JsonProperty(LanguageEn ? "Teleport request settings" : "Настройка запросов на телепортацию")]
                public TeleportSetting teleportSetting = new TeleportSetting();
                [JsonProperty(LanguageEn ? "Teleport-to-player cooldown settings" : "Настройка времени перезарядки телепортации к игроку")]
                public PresetOtherSetting teleportCooldown = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Teleport-to-player countdown settings" : "Настройка времени телепортации к игроку")]
                public PresetOtherSetting teleportCountdown = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Teleport-to-player limit settings" : "Настройка лимитов телепортации игрока к другому игроку")]
                public LimitSettings limitTeleportation = new LimitSettings();

                public class LimitSettings
                {
                    [JsonProperty(LanguageEn ? "Use player teleport-to-player limits (true = yes / false = no)" : "Использовать лимиты телепортации игрока к другому игроку (true - да/false - нет)")]
                    public Boolean useLimitTeleportation;
                    [JsonProperty(LanguageEn ? "Hours until player limits are reset" : "Через сколько часов сбрасывать лимиты игрокам")]
                    public Int32 hoursResetLimit;
                    [JsonProperty(LanguageEn ? "Player teleport limit settings" : "Настройка лимитов телепортации игрока")]
                    public PresetOtherSetting limitTeleportation = new PresetOtherSetting();
                }
                internal class TeleportSetting
                {
                    [JsonProperty(LanguageEn ? "Offer to accept teleport requests in the UI (true = yes / false = no)" : "Предлагать принять запрос на телепортацию в UI (true - да/false - нет)")]
                    public Boolean suggetionAcceptedUI;
                    [JsonProperty(LanguageEn ? "Teleportation mode: true - teleport the player to the position where the request was accepted; false - teleport the player to the other player regardless of where it was accepted" : "Режим телепортации, 1 (true) - телепортировать игрока на точку где был принят запрос, 2 (false) - телепортировать игрока к другому игроку не зависимо где был принят запрос")]
                    public Boolean teleportPosInAccept;
                    [JsonProperty(LanguageEn ? "Allow players to send teleport requests via G-Map (true = yes / false = no)" : "Разрешить игрокам отправлять запросы на телепортацию по G-Map (true - да/false - нет)")]
                    public Boolean useGMapTeleport;
                    [JsonProperty(LanguageEn ? "Use instant teleport to a point via G-Map (true = yes / false = no) (requires admin rights or the iqteleportation.gmap permission)" : "Использовать моментальную телепортацию на точку через G-Map (true - да/false - нет) (требуются права администратора или разрешение iqteleportation.gmap)")]
                    public Boolean useGMapTeleportAdmin;
                    [JsonProperty(LanguageEn ? "Disable teleportation features on the server (tpr and tpa will be unavailable) (true = yes / false = no)" : "Отключить функции телепортации на сервере (tpr и tpa будут недоступны) (true - да/false - нет)")]
                    public Boolean disableTeleportationPlayer = false;
                    [JsonProperty(LanguageEn ? "Forbid sending and accepting teleport requests while the player is in a vehicle (true); otherwise teleport directly into the vehicle if a seat is free (false)" : "Запретить отправку и принятие запроса на телепортацию пока игрок в транспорте (true) иначе телепортирует прямо в транспорт, при наличии мест (false)")]
                    public Boolean noTeleportVehicle = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests in a safe zone (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию в безопасной зоне (true - да/false - нет)")]
                    public Boolean noTeleportInSafeZone = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests during raid block (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию во время рейдблока (true - да/false - нет)")]
                    public Boolean noTeleportInRaidBlock = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is in the subway (train tunnels) (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок в метро (true - да/false - нет)")]
                    public Boolean noTeleportTrains = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is in the Underwater Lab (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок в подводной лаборатории (true - да/false - нет)")]
                    public Boolean noTeleportUnderwateLabs = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is on the Cargo Ship (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок на корабле (true - да/false - нет)")]
                    public Boolean noTeleportCargoShip = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is in a hot air balloon (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок на воздушном шаре (true - да/false - нет)")]
                    public Boolean noTeleportHotAirBalloon = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is cold (freezing) (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игроку холодно (true - да/false - нет)")]
                    public Boolean noTeleportFrozen = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is swimming (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок плавает (true - да/false - нет)")]
                    public Boolean noTeleportInSwimming = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is under radiation (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок под радиацией (true - да/false - нет)")]
                    public Boolean noTeleportRadiation = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is bleeding (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда у игрока кровотечение(true - да/false - нет)")]
                    public Boolean noTeleportBlood = true;
                    [JsonProperty(LanguageEn ? "Block sending and accepting teleport requests while the player is in another player's building privilege area (building blocked) (true = yes / false = no)" : "Запретить отправку и принятие запроса на телепортацию когда игрок в чужой билдинг зоне (true - да/false - нет)")]
                    public Boolean noTeleportBuildingBlock = true;
                    [JsonProperty(LanguageEn ? "Allow teleportation only to friends (true = yes / false = no)" : "Разрешать телепортацию только к друзьям (true - да/false - нет)")]
                    public Boolean onlyTeleportationFriends = true;
                }
            }
            
            internal class HomeController
            {
                [JsonProperty(LanguageEn ? "Allow teleporting to a friend's home point (true = yes / false = no)" : "Разрешить телепортацию на точку дома друзей (true - да/false - нет)")]
                public Boolean useFriendHomeTeleportation = true;
                [JsonProperty(LanguageEn ? "Enable UI with recent home points on G-Map (true = yes / false = no)" : "Включить UI интерфейс с последними точками дома на G-Map (true - да/false - нет)")]
                public Boolean uiMapHomeList;
                [JsonProperty(LanguageEn ? "Offer setting a home point in the UI after placing a bed or sleeping bag (true = yes / false = no)" : "Предлагать установку точки дома в UI после установки кровати или спальника (true - да/false - нет)")]
                public Boolean suggetionSetHomeAfterInstallBed;
                [JsonProperty(LanguageEn ? "Allow players to teleport to a home point via G-Map (true = yes / false = no)" : "Разрешить игрокам телепортироваться на точку дома по G-Map (true - да/false - нет)")]
                public Boolean useGMapTeleportHome;
                [JsonProperty(LanguageEn ? "Block home teleports in a safe zone (true = yes / false = no)" : "Запретить телепортацию на точку дома в безопасной зоне (true - да/false - нет)")]
                public Boolean noTeleportInSafeZone = true;
                [JsonProperty(LanguageEn ? "Add a visual ping effect to the home point after it’s set (true = yes / false = no)" : "Добавлять визуальный эффект (пинг) на точку дома после ее установки (true - да/false - нет)")]
                public Boolean addedPingMarker;
                [JsonProperty(LanguageEn ? "Add a marker on the player’s G-Map after setting a home (true = yes / false = no)" : "Добавлять маркер на G-Map игрока после установки дома (true - да/false - нет)")]
                public Boolean addedMapMarkerHome;
                [JsonProperty(LanguageEn ? "Permissions for setting home points" : "Настройка разрешений на установку точек дома")]
                public SetupHome setupHomeController = new SetupHome();
                [JsonProperty(LanguageEn ? "Home point count settings" : "Настройка количества точек дома")]
                public PresetOtherSetting homeCount = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Home teleport cooldown settings" : "Настройка времени перезарядки телепортации на точку дома")]
                public PresetOtherSetting homeCooldown = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Home teleport countdown settings" : "Настройка времени телепортации игрока на точку дома")]
                public PresetOtherSetting homeCountdown = new PresetOtherSetting();
                [JsonProperty(LanguageEn ? "Home teleport limit settings" : "Настройка лимитов телепортации игрока на точку дома")]
                public LimitSettings limitTeleportationHome = new LimitSettings();


                public class LimitSettings
                {
                    [JsonProperty(LanguageEn ? "Use player home-teleport limits (true = yes / false = no)" : "Использовать лимиты телепортации игрока на точку дома (true - да/false - нет)")]
                    public Boolean useLimitTeleportationHome;
                    [JsonProperty(LanguageEn ? "Hours until player limits are reset" : "Через сколько часов сбрасывать лимиты игрокам")]
                    public Int32 hoursResetLimit;
                    [JsonProperty(LanguageEn ? "Home teleport limit settings" : "Настройка лимитов телепортации игрока на точку дома")]
                    public PresetOtherSetting limitHomeTeleportation = new PresetOtherSetting();
                }
                
                internal class SetupHome
                {
                    [JsonProperty(LanguageEn ? "Block setting a home point during raid block (true = yes / false = no)" : "Запретить установку точки дома при рейдблоке (true - да/false - нет)")]
                    public Boolean noSetupInRaidBlock;
                    [JsonProperty(LanguageEn ? "Allow setting home points on tugboats (true = yes / false = no)" : "Разрешить установку точек дома на буксирах (true - да/false - нет)")]
                    public Boolean canSetupTugboatHome;
                    [JsonProperty(LanguageEn ? "Allow setting home points on homemade boats (true - yes / false - no)" : "Разрешить установку точек дома на самодельных лодках (true - да/false - нет)")]
                    public Boolean canSetupPlayerBoats;
                    [JsonProperty(LanguageEn ? "Allow setting home points in apartments (true = yes / false = no)" : "Разрешить установку точек дома в апартаментах (true - да/false - нет)")]
                    public Boolean canSetupApartmentHome;
                    [JsonProperty(LanguageEn ? "Allow setting home points only when building privilege is present (true = yes / false = no)" : "Разрешать установку точек дома только при наличии билдинг зоны (true - да/false - нет)")]
                    public Boolean onlyBuildingPrivilage;
                    [JsonProperty(LanguageEn ? "Allow setting home points only when authorized in the building privilege area (true = yes / false = no)" : "Разрешать установку точек дома только при наличии авторизации в билдинг зоне (true - да/false - нет)")]
                    public Boolean onlyBuildingAuth;
                }
            }
            
            internal class PresetOtherSetting
            {
                [JsonProperty(LanguageEn ? "Default count for players without permissions" : "Стандартное количество для игроков без разрешений")]
                public Int32 count;
                [JsonProperty(LanguageEn ? "Count overrides for players with permissions [Permission] = Count" : "Настройка количества для игроков с разрешениями [Разрешение] = Количество")]
                public Dictionary<String, Int32> countPermissions;

                public Int32 GetCount(BasePlayer player, Boolean descending = false)
                {
                    foreach (KeyValuePair<String, Int32> countPermission in descending ? countPermissions.OrderByDescending(x => x.Value)
                                                                                       : countPermissions.OrderBy(x => x.Value))
                    {
                        if (_.permission.UserHasPermission(player.UserIDString, countPermission.Key))
                            return countPermission.Value;
                    }

                    return count;
                }
            }

            public static Configuration GetNewConfiguration()
            {
                return new Configuration
                {
                    generalController = new GeneralController
                    {
                        useSoundEffects = true,
                        useLogged = false,
                        useOnlyGameTip = false,
                        stopSleepingAfterTeleport = false,
                        monumentBlockedTeleportation = new List<String>()
                        {
                            
                        },
                        iqchatSetting = new GeneralController.IQChatController
                        {
                            customPrefix = "[IQTeleportation]",
                            customAvatar = "0"
                        },
                        simpleStatusController = new GeneralController.SimpleStatusController()
                        {
                            useSimpleStatus = false,
                            settingTeleportationProcess = new GeneralController.SimpleStatusController.Property
                            {
                                colorPanel = "0.3 0.3 0.3 0.5",
                                titleLanguages = new Dictionary<String, String>()
                                {
                                    ["ru"] = "Телепортация",
                                    ["en"] = "Teleporation",
                                },
                                titleColor = "0.5647059 0.5490196 0.5333334 1",
                                spriteIcon = "assets/icons/stopwatch.png",
                                iconColor = "0.5647059 0.5490196 0.5333334 1"
                            }
                        }
                    },
                    warpsContoller = new WarpsController
                    {
                        useWarps = false,
                        noTeleportWaprHostile = true,
                        warpPermissions = new Dictionary<String, String>
                        {
                            ["warpName"] = "iqteleportation.warpPermission"
                        },
                        limitWarps = new WarpsController.LimitSettings
                        {
                            useLimitWarp = false,
                            hoursResetLimit = 6,
                            limitWarps = new PresetOtherSetting()
                            {
                                count = 15,
                                countPermissions = new Dictionary<String, Int32>()
                                {
                                    ["iqteleportation.vip"] = 20,
                                    ["iqteleportation.premium"] = 25,
                                    ["iqteleportation.gold"] = 30,
                                }
                            }
                        },
                        teleportWarpCooldown = new PresetOtherSetting
                        {
                            count = 120,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 100,
                                ["iqteleportation.premium"] = 80,
                                ["iqteleportation.gold"] = 60,
                            }
                        },
                        teleportWarpCountdown = new PresetOtherSetting
                        {
                            count = 30,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 25,
                                ["iqteleportation.premium"] = 20,
                                ["iqteleportation.gold"] = 15,
                            },
                        }
                    },
                    teleportationController = new TeleportationController
                    {
                        teleportSetting = new TeleportationController.TeleportSetting
                        {
                            suggetionAcceptedUI = true,
                            teleportPosInAccept = false,
                            useGMapTeleport = true,
                            useGMapTeleportAdmin = true,
                            disableTeleportationPlayer = false,
                            noTeleportInRaidBlock = true,
                            noTeleportUnderwateLabs = true,
                            noTeleportTrains = true,
                            noTeleportCargoShip = true,
                            noTeleportHotAirBalloon = true,
                            noTeleportBlood = true,
                            noTeleportFrozen = true,
                            noTeleportInSwimming = true,
                            noTeleportRadiation = true,
                            noTeleportBuildingBlock = true,
                            onlyTeleportationFriends = false,
                            noTeleportInSafeZone = false,
                            noTeleportVehicle = true,
                        },
                        limitTeleportation = new TeleportationController.LimitSettings
                        {
                            useLimitTeleportation = false,
                            hoursResetLimit = 24,
                            limitTeleportation = new PresetOtherSetting()
                            {
                                count = 15,
                                countPermissions = new Dictionary<String, Int32>()
                                {
                                    ["iqteleportation.vip"] = 20,
                                    ["iqteleportation.premium"] = 25,
                                    ["iqteleportation.gold"] = 30,
                                }
                            }
                        },
                        teleportCooldown = new PresetOtherSetting
                        {
                            count = 60,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 45,
                                ["iqteleportation.premium"] = 35,
                                ["iqteleportation.gold"] = 25,
                            }
                        },
                        teleportCountdown = new PresetOtherSetting()
                        {
                            count = 20,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 15,
                                ["iqteleportation.premium"] = 10,
                                ["iqteleportation.gold"] = 5,
                            }
                        }
                    },
                    homeController = new HomeController()
                    {
                        useFriendHomeTeleportation = true,
                        uiMapHomeList = true,
                        suggetionSetHomeAfterInstallBed = true,
                        addedMapMarkerHome = true,
                        addedPingMarker = true,
                        useGMapTeleportHome = false,
                        noTeleportInSafeZone = false,
                        setupHomeController = new HomeController.SetupHome
                        {
                            noSetupInRaidBlock = true,
                            canSetupTugboatHome = false,
                            canSetupPlayerBoats = false,
                            canSetupApartmentHome = true,
                            onlyBuildingPrivilage = true,
                            onlyBuildingAuth = true,
                        },
                        limitTeleportationHome = new HomeController.LimitSettings()
                        {
                            useLimitTeleportationHome = false,
                            hoursResetLimit = 12,
                            limitHomeTeleportation = new PresetOtherSetting()
                            {
                                count = 15,
                                countPermissions = new Dictionary<String, Int32>()
                                {
                                    ["iqteleportation.vip"] = 20,
                                    ["iqteleportation.premium"] = 25,
                                    ["iqteleportation.gold"] = 30,
                                }
                            }
                        },
                        homeCount = new PresetOtherSetting()
                        {
                            count = 2,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 3,
                                ["iqteleportation.premium"] = 4,
                                ["iqteleportation.gold"] = 5,
                            }
                        },
                        homeCooldown = new PresetOtherSetting()
                        {
                            count = 30,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 25,
                                ["iqteleportation.premium"] = 20,
                                ["iqteleportation.gold"] = 15,
                            }
                        },
                        homeCountdown = new PresetOtherSetting()
                        {
                            count = 20,
                            countPermissions = new Dictionary<String, Int32>()
                            {
                                ["iqteleportation.vip"] = 15,
                                ["iqteleportation.premium"] = 10,
                                ["iqteleportation.gold"] = 5,
                            }
                        },
                    }
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();

                EnsureTopLevelObjects();

                Configuration def = Configuration.GetNewConfiguration();
                NormalizeSimpleStatus(config.generalController.simpleStatusController, def.generalController.simpleStatusController);
                NormalizePreset(config.warpsContoller.teleportWarpCooldown, def.warpsContoller.teleportWarpCooldown);
                NormalizePreset(config.warpsContoller.teleportWarpCountdown, def.warpsContoller.teleportWarpCountdown);
                NormalizeWarpLimits(config.warpsContoller.limitWarps, def.warpsContoller.limitWarps);

                NormalizePreset(config.teleportationController.teleportCooldown, def.teleportationController.teleportCooldown);
                NormalizePreset(config.teleportationController.teleportCountdown, def.teleportationController.teleportCountdown);
                NormalizeTpLimits(config.teleportationController.limitTeleportation, def.teleportationController.limitTeleportation);

                NormalizePreset(config.homeController.homeCount, def.homeController.homeCount);
                NormalizePreset(config.homeController.homeCooldown, def.homeController.homeCooldown);
                NormalizePreset(config.homeController.homeCountdown, def.homeController.homeCountdown);
                NormalizeHomeLimits(config.homeController.limitTeleportationHome, def.homeController.limitTeleportationHome);
            }
            catch
            {
                PrintWarning(LanguageEn ? $"Error reading #321562 configuration 'oxide/config/{Name}', creating a new configuration!!" : $"Ошибка чтения #3212 конфигурации 'oxide/config/{Name}', создаём новую конфигурацию!!"); 
                LoadDefaultConfig();
            }

            NextTick(SaveConfig);
        }
        
        private void EnsureTopLevelObjects()
        {
            if (config.warpsContoller.warpPermissions == null || config.warpsContoller.warpPermissions.Count == 0)
                config.warpsContoller.warpPermissions = new Dictionary<String, String> { ["warpName"] = "iqteleportation.warpPermission" };

            if (config.generalController.simpleStatusController == null)
                config.generalController.simpleStatusController = new Configuration.GeneralController.SimpleStatusController { useSimpleStatus = false, settingTeleportationProcess = new Configuration.GeneralController.SimpleStatusController.Property() };

            if (config.generalController.simpleStatusController.settingTeleportationProcess == null)
                config.generalController.simpleStatusController.settingTeleportationProcess = new Configuration.GeneralController.SimpleStatusController.Property();

            if (config.teleportationController.limitTeleportation == null)
                config.teleportationController.limitTeleportation = new Configuration.TeleportationController.LimitSettings();

            if (config.warpsContoller.limitWarps == null)
                config.warpsContoller.limitWarps = new Configuration.WarpsController.LimitSettings();

            if (config.homeController.limitTeleportationHome == null)
                config.homeController.limitTeleportationHome = new Configuration.HomeController.LimitSettings();

            config.teleportationController.limitTeleportation.limitTeleportation.countPermissions ??= new Dictionary<String,Int32>();
            config.homeController.limitTeleportationHome.limitHomeTeleportation.countPermissions ??= new Dictionary<String,Int32>();
            config.warpsContoller.limitWarps.limitWarps.countPermissions ??= new Dictionary<String,Int32>();
        }

        private static void NormalizePreset(Configuration.PresetOtherSetting p, Configuration.PresetOtherSetting def)
        {
            if (p == null) return;
            if (p.count <= 0) p.count = def.count;
            if (p.countPermissions == null || p.countPermissions.Count == 0)
                p.countPermissions = new Dictionary<String, Int32>(def.countPermissions);
        }

        private static void NormalizeSimpleStatus(Configuration.GeneralController.SimpleStatusController s,
                                                  Configuration.GeneralController.SimpleStatusController def)
        {
            if (s == null) return;
            Configuration.GeneralController.SimpleStatusController.Property p = s.settingTeleportationProcess ?? (s.settingTeleportationProcess = new Configuration.GeneralController.SimpleStatusController.Property());
            Configuration.GeneralController.SimpleStatusController.Property dp = def.settingTeleportationProcess;

            p.colorPanel ??= dp.colorPanel;
            p.titleColor ??= dp.titleColor;
            p.spriteIcon ??= dp.spriteIcon;
            p.iconColor  ??= dp.iconColor;

            if (p.titleLanguages == null || p.titleLanguages.Count == 0)
                p.titleLanguages = new Dictionary<String, String>(dp.titleLanguages);
        }

        private static void NormalizeWarpLimits(Configuration.WarpsController.LimitSettings l,
                                                Configuration.WarpsController.LimitSettings def)
        {
            if (l == null) return;
            if (l.hoursResetLimit <= 0) l.hoursResetLimit = def.hoursResetLimit;
            NormalizePreset(l.limitWarps, def.limitWarps);
        }

        private static void NormalizeTpLimits(Configuration.TeleportationController.LimitSettings l,
                                              Configuration.TeleportationController.LimitSettings def)
        {
            if (l == null) return;
            if (l.hoursResetLimit <= 0) l.hoursResetLimit = def.hoursResetLimit;
            NormalizePreset(l.limitTeleportation, def.limitTeleportation);
        }

        private static void NormalizeHomeLimits(Configuration.HomeController.LimitSettings l,
                                                Configuration.HomeController.LimitSettings def)
        {
            if (l == null) return;
            if (l.hoursResetLimit <= 0) l.hoursResetLimit = def.hoursResetLimit;
            NormalizePreset(l.limitHomeTeleportation, def.limitHomeTeleportation);
        }

        protected override void LoadDefaultConfig() => config = Configuration.GetNewConfiguration();
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Data

        private Dictionary<String, List<ServerWarp>> serverWarps = new();
        private Dictionary<UInt64, Dictionary<String, PlayerHome>> playerHomes = new();
        private Dictionary<UInt64, SettingPlayer> playerSettings = new();
        private Dictionary<UInt64, List<String>> playerLastHomes = new();
        private InfoLimits playerLimitController = new();

        private class InfoLimits
        {
            public Double homeTime;
            public Double teleportTime;
            public Double warpTime;
            
            public Dictionary<UInt64, Dictionary<TypeLimit, Int32>> playersLimits = new();
        }

        private Int32 GetAvailableLimit(BasePlayer player, TypeLimit type) => type switch
        {
            TypeLimit.Teleportation => config.teleportationController.limitTeleportation.limitTeleportation.GetCount(player),
            TypeLimit.Home          => config.homeController.limitTeleportationHome.limitHomeTeleportation.GetCount(player),
            _                       => config.warpsContoller.limitWarps.limitWarps.GetCount(player)
        };

        private Int32 GetRemainingLimit(BasePlayer player, TypeLimit type)
        {
            Int32 used = playerLimitController.playersLimits.TryGetValue(player.userID, out Dictionary<TypeLimit, Int32> info) && info.TryGetValue(type, out Int32 value) ? value : 0;
            Int32 available = Math.Max(0, GetAvailableLimit(player, type));
            return Math.Max(0, available - used);
        }

        private Int32 ConsumeLimit(BasePlayer player, TypeLimit type)
        {
            if (!playerLimitController.playersLimits.TryGetValue(player.userID, out var limits))
                playerLimitController.playersLimits[player.userID] = limits = new Dictionary<TypeLimit, Int32>();

            limits.TryGetValue(type, out Int32 used);

            Int32 available = Math.Max(0, GetAvailableLimit(player, type));
            if (used >= available)
                return 0; 

            used++;
            limits[type] = used;
            return available - used;
        }

        private Boolean IsLimitExhausted(BasePlayer player, TypeLimit type) => GetRemainingLimit(player, type) <= 0;
        
        private ServerWarp GetRandomWarp(List<ServerWarp> warpList)
        {
            List<ServerWarp> warps = new List<ServerWarp>();

            foreach (ServerWarp serverWarp in warpList)
            {
                if (serverWarp.hideWarp) continue;
                warps.Add(serverWarp);
            }

            try
            {
                return warps.GetRandom();
            }
            finally
            {
                warps.Clear();
                warps = null;
            }
        }
        private class ServerWarp
        {
            public Vector3 position;
            public String monumentParent;
            public Boolean hideWarp;
            
            public Vector3 GetPositionWarp()
            {
                if (hideWarp) return default;
                MonumentInfo monument = _.GetMonumentInName(monumentParent);

                if (!monument) return position;
                    
                return monument.transform.TransformPoint(position);
            }

            public ServerWarp(MonumentInfo monument, Vector3 warpPosition)
            {
                if (monument)
                {
                    position = monument.transform.InverseTransformPoint(warpPosition);
                    monumentParent = monument.name;
                }
                else
                {
                    position = warpPosition;
                    monumentParent = String.Empty;
                }
                
                hideWarp = false;
            }
        }

        private enum TypeHomeEntity
        {
            None,
            PlayerBoat,
            Tugboat,
            Apartment
        }
        
        private class PlayerHome
        {
            public Vector3 positionHome;
            public UInt64 netIdEntity;
            public TypeHomeEntity typeEntity;
        }
        private class SettingPlayer
        {
            public Boolean useTeleportationHomeFromFriends;
            public Boolean autoAcceptTeleportationFriends;

            public Boolean useTeleportGMap;
            
            public DateTime firstConnection;
        }
        
        private void ReadData()
        {
            playerSettings = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<UInt64, SettingPlayer>>("IQSystem/IQTeleportation/PlayerSettings");
            playerHomes = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<UInt64, Dictionary<String, PlayerHome>>>("IQSystem/IQTeleportation/Homes");
            playerLastHomes = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<UInt64, List<String>>>("IQSystem/IQTeleportation/MemoryLastHomes");
            serverWarps = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<String, List<ServerWarp>>>("IQSystem/IQTeleportation/Warps");
            playerLimitController = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<InfoLimits>("IQSystem/IQTeleportation/PlayerLimits");
        }

        private void WriteData()
        {
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQTeleportation/PlayerSettings", playerSettings);
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQTeleportation/Homes", playerHomes);
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQTeleportation/MemoryLastHomes", playerLastHomes);
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQTeleportation/Warps", serverWarps);
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQTeleportation/PlayerLimits", playerLimitController);
        }

        private void AutoClearData(Boolean isNewSave = false)
        {
            Int32 settingClear = default;

            #region Clear Old Players
            
            List<UInt64> keysPlayersSetting = Pool.Get<List<UInt64>>();
            keysPlayersSetting.AddRange(playerSettings.Keys);
            DateTime currentDate = DateTime.Now;
            
            foreach (UInt64 keyPlayer in keysPlayersSetting)
            {
                SettingPlayer playerSetting = playerSettings[keyPlayer];
                Int32 leftDays = (currentDate - playerSetting.firstConnection).Days;
                if (leftDays < 7) continue;
                
                playerSettings.Remove(keyPlayer);

                if (playerLimitController.playersLimits.ContainsKey(keyPlayer))
                    playerLimitController.playersLimits.Remove(keyPlayer);

                if (playerLastHomes.ContainsKey(keyPlayer))
                    playerLastHomes.Remove(keyPlayer);
                
                settingClear++;
            }

            Pool.FreeUnmanaged(ref keysPlayersSetting);
            
            #endregion
            
            if(settingClear != default)
                Puts(LanguageEn ? $"Automatic cleaning of user settings. {settingClear} users and their settings were deleted due to absence from the server for more than 7 days" : $"Автоматическая очистка настроек пользоваталей. Было удалено {settingClear} пользователей и их настройки из-за отсутствия более 7 дней на сервере");
            
            if (isNewSave)
            {
                playerHomes.Clear();
                
                playerLimitController.playersLimits.Clear();
                playerLimitController.homeTime = 0;
                playerLimitController.teleportTime = 0;
                playerLimitController.warpTime = 0;
                
                playerLastHomes.Clear();

                List<String> keysToRemove = new List<String>();

                foreach (KeyValuePair<String, List<ServerWarp>> warps in serverWarps)
                {
                    List<ServerWarp> warpsToRemove = new List<ServerWarp>();

                    foreach (ServerWarp warp in warps.Value)
                    {
                        if (String.IsNullOrWhiteSpace(warp.monumentParent))
                            warpsToRemove.Add(warp);
                    }

                    foreach (ServerWarp warp in warpsToRemove)
                        warps.Value.Remove(warp);

                    if (warps.Value.Count == 0)
                        keysToRemove.Add(warps.Key);
                    
                    warpsToRemove.Clear();
                    warpsToRemove = null;
                }

                foreach (String key in keysToRemove)
                {
                    cmd.RemoveChatCommand(key, this);
                    cmd.RemoveConsoleCommand(key, this);
                    serverWarps.Remove(key);
                }

                keysToRemove.Clear();
                keysToRemove = null;
            }
            
            WriteData();
        }
        
        private void RegisteredUser(BasePlayer player)
        {
            localUsersRepository.TryAdd(player, new UserRepository
            {
                activeTeleportation = null,
                cooldownHome = 0,
                cooldownTeleportation = 0
            });

            playerSettings.TryAdd(player.userID, new SettingPlayer
            {
                useTeleportationHomeFromFriends = true,
                useTeleportGMap = true,
                autoAcceptTeleportationFriends = false,
                firstConnection = DateTime.Now,
            });

            playerHomes.TryAdd(player.userID, new Dictionary<String, PlayerHome>());
        }

        #endregion

        #region Command

        #region Auto Accept Teleport

        [ChatCommand("atp")]
        private void AutoAcceptTeleportChatCmd(BasePlayer player) => AutoTeleportTurn(player);
        
        [ConsoleCommand("atp")]
        private void AutoAcceptTeleportConsoleCmd(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            AutoTeleportTurn(player);
        }

        #endregion

        #region GMap Turned Teleport

        [ChatCommand("gmap")]
        private void GMapTeleportChatCmd(BasePlayer player) => GMapTeleportTurn(player);
        
        [ConsoleCommand("gmap")]
        private void GMapTeleportConsoleCmd(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            GMapTeleportTurn(player);
        }
        
        #endregion
        
        #region Sethome
        
        [ChatCommand("sh")]
        private void SetHomeShortChatCMD(BasePlayer player, String cmd, String[] arg) => SetHomeChatCMD(player, cmd, arg);
        
        [ChatCommand("sethome")]
        private void SetHomeChatCMD(BasePlayer player, String cmd, String[] arg)
        {
            if (arg.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_SETHOME", player.UserIDString), player, true);
                return;
            }

            String homeName = arg[0];
            SetHome(player, homeName);
        }

        [ConsoleCommand("sh")]
        private void SetHomeShortConsoleCMD(ConsoleSystem.Arg arg) => SetHomeConsoleCMD(arg);
        
        [ConsoleCommand("sethome")]
        private void SetHomeConsoleCMD(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            
            if (arg.Args.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_SETHOME", player.UserIDString), player, true);
                return;
            }

            String homeName = arg.GetString(0);
            SetHome(player, homeName);
        }
        
        #endregion

        #region Remove Home

        [ChatCommand("rh")]
        private void RemoveHomeShortChatCMD(BasePlayer player, String cmd, String[] arg) => RemoveHomeChatCMD(player, cmd, arg);
        
        [ChatCommand("removehome")]
        private void RemoveHomeChatCMD(BasePlayer player, String cmd, String[] arg)
        {
            if (arg.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_REMOVEHOME", player.UserIDString), player, true);
                return;
            }

            String homeName = arg[0];
            RemoveHome(player, homeName);
        }

        [ConsoleCommand("rh")]
        private void RemoveHomeShortConsoleCMD(ConsoleSystem.Arg arg) => RemoveHomeConsoleCMD(arg);
        
        [ConsoleCommand("removehome")]
        private void RemoveHomeConsoleCMD(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            
            if (arg.Args.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_REMOVEHOME", player.UserIDString), player, true);
                return;
            }

            String homeName = arg.GetString(0);
            RemoveHome(player, homeName);
        }

        #endregion
        
        #region Home
        
        [ChatCommand("home")]
        private void HomeChatCMD(BasePlayer player, String cmd, String[] arg)
        {
            if (arg.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_HOME_TELEPORTATION", player.UserIDString), player, true);
                return;
            }
            
            RequestHome(player, arg);
        }

        [ConsoleCommand("gmap.home.request")]
        private void HomeConsoleInGMap(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (!player) return;
            
            if (arg.Args.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_HOME_TELEPORTATION", player.UserIDString), player, true);
                return;
            }
            
            RequestHome(player, ConvertArgs(arg.Args));
        }
        
        [ConsoleCommand("home")]
        private void HomeConsoleCMD(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            if (arg.Args.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_HOME_TELEPORTATION", player.UserIDString), player, true);
                return;
            }

            RequestHome(player, ConvertArgs(arg.Args));
        }
        
        #endregion

        #region Home List

        [ChatCommand("homelist")]
        private void ChatCommand_HomeList(BasePlayer player)
        {
            Dictionary<String, Vector3> homeList = GetHomes(player.userID);
            if (homeList == null || homeList.Count == 0)
            {
                SendChat(GetLang("HOMELIST_COMMAND_EMPTY", player.UserIDString), player);
                return;
            }
            
            String homesPlayerOnCord = String.Empty;

            foreach (KeyValuePair<String, Vector3> homeInfo in homeList)
            {
                String cordMap = MapHelper.PositionToString(homeInfo.Value);
                homesPlayerOnCord += GetLang("HOMELIST_COMMAND_FORTMAT_HOME", player.UserIDString, homeInfo.Key, cordMap);
            }
            
            SendChat(GetLang("HOMELIST_COMMAND_ALL_LIST", player.UserIDString, homesPlayerOnCord), player);
        }

        [ConsoleCommand("homelist")]
        private void ConsoleCommand_HomeList(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            
            Dictionary<String, Vector3> homeList = GetHomes(player.userID);
            if (homeList == null || homeList.Count == 0)
            {
                SendChat(GetLang("HOMELIST_COMMAND_EMPTY", player.UserIDString), player);
                return;
            }
            
            String homesPlayerOnCord = String.Empty;

            foreach (KeyValuePair<String, Vector3> homeInfo in homeList)
            {
                String cordMap = MapHelper.PositionToString(homeInfo.Value);
                homesPlayerOnCord += GetLang("HOMELIST_COMMAND_FORTMAT_HOME", player.UserIDString, homeInfo.Key, cordMap);
            }
            
            SendChat(GetLang("HOMELIST_COMMAND_ALL_LIST", player.UserIDString, homesPlayerOnCord), player);
        }
        
        #endregion

        #region Teleportation

        #region Monument Block

        [ChatCommand("mblock")]
        private void MBlockChatCMD(BasePlayer player)
        {
            if (!player.IsAdmin) return;

            if (localAllMonuments.Count == 0)
                ParseMonuments();
            
            MonumentInfo playerMonument = GetMonumentWherePlayerStand(player);
            if (playerMonument == null)
            {
                SendChat(GetLang("MONUMENT_BLOCKED_SETUP_NOT_CENTER", player.UserIDString), player, true); 
                return;
            }
            
            if (config.generalController.monumentBlockedTeleportation.Contains(playerMonument.name))
            {
                SendChat(GetLang("MONUMENT_BLOCKED_SETU_BLOCKED", player.UserIDString) , player, true); 
                return;
            }

            config.generalController.monumentBlockedTeleportation.Add(playerMonument.name);
            SendChat(GetLang("MONUMENT_BLOCKED_SETUP_ACCES", player.UserIDString), player);
            SaveConfig();
        }

        #endregion

        #region Admin Teleportation

        [ChatCommand("tp")]
        private void TpChatCMD(BasePlayer player, String cmd, String[] arg)
        {
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, permissionTP)) return;

            if (arg.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_TELEPORT", player.UserIDString), player, true);
                return;
            }

            BasePlayer targetPlayer = null;
            String nameOrIDTarget = arg[0];
            (BasePlayer, String) findPlayer = FindPlayerNameOrID(player, nameOrIDTarget, true);
            if(!String.IsNullOrWhiteSpace(nameOrIDTarget))
                targetPlayer = findPlayer.Item1;
            
            String nameOrIDOnPlayer = String.Empty;
            BasePlayer onPlayer = null;

            if (arg.Length >= 2)
            {
                nameOrIDOnPlayer = arg[1];
                (BasePlayer, String) findOnPlayer = FindPlayerNameOrID(player, nameOrIDOnPlayer, isIgnoreFinderName:false);

                if (!String.IsNullOrWhiteSpace(nameOrIDOnPlayer))
                {
                    onPlayer = targetPlayer;
                    targetPlayer = findOnPlayer.Item1;
                }

                if (onPlayer == null)
                {
                    SendChat(findOnPlayer.Item2 ?? GetLang("CAN_TELEPORT_PLAYER_NOT_FOUND_TP_ONE_PLAYER", player.UserIDString), player, true);
                    return;
                }

                if (targetPlayer == null)
                {
                    SendChat(findPlayer.Item2 ?? GetLang("CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER_ONE", player.UserIDString, nameOrIDTarget), player, true);
                    return;
                }
            }
            else onPlayer = player;
            
            if (targetPlayer == null)
            {
                SendChat(findPlayer.Item2 ?? GetLang("CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER", player.UserIDString), player, true);
                return;
            }
            
            MovePlayer(onPlayer, targetPlayer.transform.position);
            SendChat(GetLang("CAN_TELEPORT_PLAYER_ACCES_TP", onPlayer.UserIDString, targetPlayer.displayName), onPlayer);
        }

        [ConsoleCommand("tp")]
        private void TpConsoleCMD(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, permissionTP)) return;
            
            if (arg.Args.Length == 0)
            {
                SendChat(GetLang("SYNTAX_COMMAND_TELEPORT", player.UserIDString), player, true);
                return;
            }

            String nameOrIDTarget = arg.GetString(0);
            (BasePlayer, String) findPlayer = FindPlayerNameOrID(player, nameOrIDTarget, true);
            BasePlayer targetPlayer = null;
            if(!String.IsNullOrWhiteSpace(nameOrIDTarget))
                targetPlayer = findPlayer.Item1;
            
            String nameOrIDOnPlayer = String.Empty;
            BasePlayer onPlayer = null;

            if (arg.Args.Length >= 2)
            {
                nameOrIDOnPlayer = arg.GetString(1);
                (BasePlayer, String) findOnPlayer = FindPlayerNameOrID(player, nameOrIDOnPlayer, isIgnoreFinderName:false);

                if (!String.IsNullOrWhiteSpace(nameOrIDOnPlayer))
                {
                    onPlayer = targetPlayer;
                    targetPlayer = findOnPlayer.Item1;
                }

                if (onPlayer == null)
                {
                    SendChat(findOnPlayer.Item2 ?? GetLang("CAN_TELEPORT_PLAYER_NOT_FOUND_TP_ONE_PLAYER", player.UserIDString), player, true);
                    return;
                }

                if (targetPlayer == null)
                {
                    SendChat(findPlayer.Item2 ?? GetLang("CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER_ONE", player.UserIDString, nameOrIDTarget), player, true);
                    return;
                }
            }
            else onPlayer = player;
            
            if (targetPlayer == null)
            {
                SendChat(findPlayer.Item2 ?? GetLang("CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER", player.UserIDString), player, true);
                return;
            }
        
            MovePlayer(onPlayer, targetPlayer.transform.position);
            SendChat(GetLang("CAN_TELEPORT_PLAYER_ACCES_TP", onPlayer.UserIDString, targetPlayer.displayName), onPlayer);
        }
        
        #endregion
        
        [ChatCommand("tpr")]
        private void TprChatCMD(BasePlayer player, String cmd, String[] arg)
        {
            if (config.teleportationController.teleportSetting.disableTeleportationPlayer)
            {
                SendChat(GetLang("CAN_REQUEST_TELEPORTATION_DISABLED", player.UserIDString), player, true);
                return;
            }
            
            if (arg.Length == 0 && !casheTeleportationLast.ContainsKey(player))
            {
                SendChat(GetLang("SYNTAX_COMMAND_TELEPORT_REQUEST", player.UserIDString), player, true);
                return;
            }
        
            RequestTeleportation(player, arg);
        }
        
        [ConsoleCommand("tpr")]
        private void TprConsoleCMD(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            
            if (config.teleportationController.teleportSetting.disableTeleportationPlayer)
            {
                SendChat(GetLang("CAN_REQUEST_TELEPORTATION_DISABLED", player.UserIDString), player, true);
                return;
            }
            
            if (arg.Args.Length == 0 && !casheTeleportationLast.ContainsKey(player))
            {
                SendChat(GetLang("SYNTAX_COMMAND_TELEPORT_REQUEST", player.UserIDString), player, true);
                return;
            }
        
            RequestTeleportation(player, ConvertArgs(arg.Args));
        }
        
        [ChatCommand("tpa")]
        private void TpaChatCMD(BasePlayer player, String cmd, String[] arg) => AcceptRequestTeleportation(player);

        [ConsoleCommand("tpa")]
        private void TpaConsoleCMD(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            
            AcceptRequestTeleportation(player);
        }
        
        [ChatCommand("tpc")]
        private void CancellTeleportCmdChat(BasePlayer player) => CancellTeleportation(player);

        [ConsoleCommand("tpc")]
        private void CancellTeleportCmdConsole(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            CancellTeleportation(player);
        }
        
        #endregion

        #region Warps
        
        private void ChatCommandTeleportationWarp(BasePlayer player, String cmd, String[] arg)
        {
            if (!config.warpsContoller.useWarps) return;
            if (!serverWarps.TryGetValue(cmd, out List<ServerWarp> warpPoints)) return;
            
            if (config.warpsContoller.warpPermissions.Count != 0)
            {
                if(config.warpsContoller.warpPermissions.TryGetValue(cmd, out String permissionWarp))
                    if (!permission.UserHasPermission(player.UserIDString, permissionWarp))
                    {
                        SendChat(GetLang("WARP_NO_PERMISSIONS", player.UserIDString), player, true);
                        return;
                    }
            }

            ServerWarp randomWarp = GetRandomWarp(warpPoints);
            if (randomWarp == null) return;

            RequestWarp(player, randomWarp, cmd);
        }
        
        private void ConsoleCommandTeleportationWarp(ConsoleSystem.Arg arg)
        {
            if (!config.warpsContoller.useWarps) return;
            BasePlayer player = arg.Player();
            if (player == null) return;
            
            if (!serverWarps.TryGetValue(arg.cmd.Name, out List<ServerWarp> warpPoints)) return;

            if (config.warpsContoller.warpPermissions.TryGetValue(arg.cmd.Name, out String permissionWarp) && !permission.UserHasPermission(player.UserIDString, permissionWarp))
            {
                SendChat(GetLang("WARP_NO_PERMISSIONS", player.UserIDString), player, true);
                return;
            }

            ServerWarp randomWarp = GetRandomWarp(warpPoints);
            if (randomWarp == null) return;
            
            RequestWarp(player, randomWarp, arg.cmd.Name);
        }
        
        #region Admin Command

        [ChatCommand("a.home")]
        private void ChatCommandAdminHome(BasePlayer player, String cmd, String[] arg)
        {
            if (player == null) return;
            if (!player.IsAdmin) return;

            String action = arg[0];
            String nameOrIdOrIp = arg[1];

            BasePlayer bPlayer = BasePlayer.FindAwakeOrSleeping(nameOrIdOrIp);
            if (bPlayer == null)
            {
                SendChat(GetLang("A_HOME_CHECK_PLAYER_NOT_FOUND", player.UserIDString), player, true);
                return;
            }

            if (!playerHomes.TryGetValue(bPlayer.userID, out Dictionary<String, PlayerHome> playerHome))
            {
                SendChat(GetLang("A_HOME_CHECK_NOT_HOME", player.UserIDString), player, true);
                return;
            }

            switch (action)
            {
                case "clear":
                {
                    playerHome.Clear();
                    SendChat(GetLang("A_HOME_CLEAR_HOMES", player.UserIDString), player);
                    break;
                }
                case "points":
                {
                    foreach (KeyValuePair<String, PlayerHome> pHome in playerHome)
                    {
                        Vector3 positionHome = pHome.Value.positionHome;
                        
                        if(pHome.Value.netIdEntity != 0)
                        {
                            switch (pHome.Value.typeEntity)
                            {
                                case TypeHomeEntity.Tugboat:
                                {
                                    if (tugboatsServer.TryGetValue(pHome.Value.netIdEntity, out Tugboat tugboat) && tugboat && !tugboat.IsDestroyed)
                                        positionHome = tugboat.transform.TransformPoint(pHome.Value.positionHome);
                                    break;
                                }
                                case TypeHomeEntity.PlayerBoat:
                                {
                                    if (playersBoatsServer.TryGetValue(pHome.Value.netIdEntity, out PlayerBoat playerBoat) && playerBoat && !playerBoat.IsDestroyed)
                                        positionHome = playerBoat.transform.position;
                                    break;
                                }
                            }
                        }

                        DrawSphereAndText(player, positionHome, pHome.Key);
                    }
                    
                    SendChat(GetLang("A_HOME_SHOW_POINTS", player.UserIDString, bPlayer.displayName), player);
                    break;
                }
            }
        }
        
        private void ChatCommandWarp(BasePlayer player, String command, String[] args) 
        {
            if (!config.warpsContoller.useWarps) return;
            
            if (!player.IsAdmin)
                if (!permission.UserHasPermission(player.UserIDString, permissionWarpAdmin))
                    return;

            if (args.Length < 1)
            {
                SendChat(GetLang("SYNTAX_COMMAND_WARPS", player.UserIDString), player, true);
                return;
            }

            String action = args[0];
            String warpName = args.Length >= 2 ? args[1].ToLower() : null;
            
            MonumentInfo pMonument = GetMonumentWherePlayerStand(player);
            
            Vector3 pPosition = player.transform.position;
            
            switch (action)
            {
                case "points":
                {
                    if (string.IsNullOrWhiteSpace(warpName))
                    {
                        SendChat(GetLang("WARP_NOT_ARG_NAME", player.UserIDString), player, true); 
                        return;
                    }

                    if (!serverWarps.TryGetValue(warpName, out List<ServerWarp> warpPoints))
                    {
                        SendChat(GetLang("WARP_NOTHING_NAME", player.UserIDString), player, true);
                        return;
                    }

                    for (Int32 indexPoint = 0; indexPoint < warpPoints.Count; indexPoint++)
                    {
                        Vector3 warpPoint = warpPoints[indexPoint].GetPositionWarp();
                        if (warpPoint == default) return;
                        DrawSphereAndText(player, warpPoint, $"{indexPoint}");
                    }
                    
                    SendChat(GetLang("WARP_SHOW_POINTS", player.UserIDString, warpName), player, true);
                    break;
                }
                case "list":
                {
                    StringBuilder warpListBuilder = Pool.Get<StringBuilder>();

                    foreach (KeyValuePair<String, List<ServerWarp>> warps in serverWarps)
                    {
                        warpListBuilder.AppendLine($"{warps.Key} :");
                        
                        for (Int32 indexWarpPos = 0; indexWarpPos < warps.Value.Count; indexWarpPos++)
                        {
                            Vector3 warpPoint = warps.Value[indexWarpPos].GetPositionWarp();
                            warpListBuilder.AppendLine($"{indexWarpPos} : {(warpPoint == default ? "NONE MONUMENT" : warpPoint)}");
                        }

                        warpListBuilder.AppendLine(); 
                    }

                    String message = warpListBuilder.ToString();
                    if (String.IsNullOrWhiteSpace(message))
                        message = GetLang("WARP_NOTHING", player.UserIDString);
                    
                    SendChat(message, player);
                    Pool.FreeUnmanaged(ref warpListBuilder);
                    break;
                }
                case "create":
                case "add":
                case "set":
                { 
                    if (string.IsNullOrWhiteSpace(warpName))
                    {
                        SendChat(GetLang("WARP_NOT_ARG_NAME", player.UserIDString), player, true); 
                        return;
                    }
                    
                    if (serverWarps.ContainsKey(warpName))
                    {
                        for (Int32 indexPoint = 0; indexPoint < serverWarps[warpName].Count; indexPoint++)
                        {
                            Vector3 warpPoint = serverWarps[warpName][indexPoint].GetPositionWarp();
                            if (warpPoint == default) continue;
                            if (Vector3.Distance(warpPoint, player.transform.position) > 5f) continue;
                            
                            SendChat(GetLang("WARP_SETUP_POINTS_DISTANCE", player.UserIDString, indexPoint), player, true); 
                            return;
                        }

                        serverWarps[warpName].Add(new ServerWarp(pMonument, pPosition));
                        SendChat(GetLang("WARP_SETUP_POINT_ACCESS", player.UserIDString, warpName), player);
                        return;
                    }

                    serverWarps.Add(warpName, new List<ServerWarp>() { new(pMonument, pPosition) });
                    
                    cmd.AddChatCommand(warpName, this, nameof(ChatCommandTeleportationWarp));
                    cmd.AddConsoleCommand(warpName, this, nameof(ConsoleCommandTeleportationWarp));
                        
                    SendChat(GetLang("WARP_SETUP_NEW", player.UserIDString, warpName, warpName.ToLower()), player); 
                    break;
                }
                case "edit":
                case "update":
                {
                    if (string.IsNullOrWhiteSpace(warpName))
                    {
                        SendChat(GetLang("WARP_NOT_ARG_NAME", player.UserIDString), player, true); 
                        return;
                    }

                    if (!serverWarps.TryGetValue(warpName, out List<ServerWarp> warpPoints))
                    {
                        SendChat(GetLang("WARP_NOTHING_NAME", player.UserIDString), player, true);
                        return;
                    }
                    
                    if (args.Length < 3)
                    {
                        String pointsKeys = GetWarpPoints(warpName);
                        SendChat(GetLang("WARP_NOTHING_KEY_POINTS", player.UserIDString, pointsKeys), player, true);
                        return;
                    }
                    
                    String warpKeyString = args[2];

                    if (!Int32.TryParse(warpKeyString, out Int32 warpKey) || warpKey < 0)
                    {
                        SendChat(GetLang("WARP_NOTHING_KEY_IS_NUMBER", player.UserIDString), player, true);
                        return;
                    }

                    if (warpPoints.Count < warpKey)
                    {
                        String pointsKeys = GetWarpPoints(warpName);
                        SendChat(GetLang("WARP_REMOVED_POINT_NOTHING_KEYS", player.UserIDString, warpKey, warpName, pointsKeys), player, true); 
                        return;
                    }

                    for (Int32 indexPoint = 0; indexPoint < serverWarps[warpName].Count; indexPoint++)
                    {
                        Vector3 warpPoint = serverWarps[warpName][indexPoint].GetPositionWarp();
                        if(warpPoint == default) continue;
                        if (indexPoint == warpKey || Vector3.Distance(warpPoint, player.transform.position) > 5f) continue;
                        SendChat(GetLang("WARP_SETUP_POINTS_DISTANCE", player.UserIDString, indexPoint), player, true); 
                        return;
                    }

                    serverWarps[warpName][warpKey] = new ServerWarp(pMonument, pPosition);
                    SendChat(GetLang("WARP_EDIT_POINT_ACCESS", player.UserIDString, warpName), player); 
                    break;
                }
                case "remove":
                case "delete":
                {
                    if (string.IsNullOrWhiteSpace(warpName))
                    {
                        SendChat(GetLang("WARP_NOT_ARG_NAME", player.UserIDString), player, true); 
                        return;
                    }
                    
                    if (!serverWarps.TryGetValue(warpName, out List<ServerWarp> warpPoints))
                    {
                        SendChat(GetLang("WARP_NOTHING_NAME", player.UserIDString), player, true);
                        return;
                    }
                    
                    String warpKeyString = args.Length >= 3 ? args[2] : String.Empty;
                    
                    if (!String.IsNullOrWhiteSpace(warpKeyString) && warpPoints.Count > 1)
                    {
                        if (!Int32.TryParse(warpKeyString, out Int32 warpKey) || warpKey < 0)
                        {
                            SendChat(GetLang("WARP_NOTHING_KEY_IS_NUMBER", player.UserIDString), player, true);
                            return;
                        }
                        
                        if (warpPoints.Count < warpKey)
                        {
                            String pointsKeys = GetWarpPoints(warpName);
                            SendChat(GetLang("WARP_REMOVED_POINT_NOTHING_KEYS", player.UserIDString, warpKey, warpName, pointsKeys), player, true); 
                            return;
                        }
                        
                        serverWarps[warpName].RemoveAt(warpKey);
                        SendChat(GetLang("WARP_REMOVED_POINT", player.UserIDString, warpKey, warpName), player, true); 
                        return;
                    }
        
                    serverWarps.Remove(warpName);
                    cmd.RemoveChatCommand(warpName, this);
                    cmd.RemoveConsoleCommand(warpName, this);
                    
                    SendChat(GetLang("WARP_REMOVED", player.UserIDString, warpName), player); 
                    break;
                }
            }
        }
        
        #endregion
        
        #endregion

        #region UI Commands

        [ConsoleCommand("ui_teleportation_command")] 
        private void UICommandConsole(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            if (arg.Args.Length == 0) return;
            String action = arg.GetString(0);

            switch (action)
            {
                case "sethome":
                {
                    if (arg.Args.Length < 1) return;
                    
                    Dictionary<String, PlayerHome> pData = playerHomes[player.userID];
                    if (pData.Count >= config.homeController.homeCount.GetCount(player, true)) return;
                    String mbName = GetNextHomeName(pData);

                    if (bagInstalled.TryGetValue(player, out SleepingBag bag))
                    {
                        SetHome(player, mbName, bag);
                        bagInstalled.Remove(player);
                    }
                    
                    break;
                }

                case "cancell.sethome":
                {
                    if (bagInstalled.ContainsKey(player))
                        bagInstalled.Remove(player);
                    
                    break;
                }

                case "tpa":
                {
                    AcceptRequestTeleportation(player);
                    break;
                }

                case "tpc":
                {
                    CancellTeleportation(player);
                    break;
                }
            }
        }

        #endregion
        
        #endregion
        
        #region Hooks

        private void Init()
        {
            _ = this;
            ReadData();
            
            if(config.teleportationController.teleportSetting.noTeleportVehicle)
                Unsubscribe(nameof(CanDismountEntity));
            
            if(!config.homeController.suggetionSetHomeAfterInstallBed)
                Unsubscribe(nameof(OnEntityBuilt));

            if (!config.homeController.setupHomeController.canSetupTugboatHome && !config.homeController.setupHomeController.canSetupPlayerBoats) 
            {
                Unsubscribe(nameof(OnEntitySpawned));
                Unsubscribe(nameof(OnEntityKill));
                Unsubscribe(nameof(OnEntityDeath));
            }

            if (!config.teleportationController.teleportSetting.useGMapTeleport &&
                !config.teleportationController.teleportSetting.useGMapTeleportAdmin &&
                !config.homeController.useGMapTeleportHome)
                Unsubscribe(nameof(OnMapMarkerAdd));
        }
        
        private void OnServerInitialized()
        {
            RegisteredPermissions();
            SimpleStatus_CreateStatuses();

            if(config.teleportationController.teleportSetting.suggetionAcceptedUI || config.homeController.suggetionSetHomeAfterInstallBed)
            {
                _imageUI = new ImageUI();
                _imageUI.DownloadImage();
            }
            
            AutoClearData();
            ControllerDataLimits();
            
            if (config.generalController.monumentBlockedTeleportation.Count != 0 || config.warpsContoller.useWarps)
                ParseMonuments();
            
            ParseBoats(config.homeController.setupHomeController.canSetupTugboatHome, config.homeController.setupHomeController.canSetupPlayerBoats); 
            ParseApartments();
            
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);

            ValidateWarps();
            
            timerCheckHomes = timer.Every(300, () =>
            {
                List<BasePlayer> players = Pool.Get<List<BasePlayer>>();
                players.AddRange(BasePlayer.activePlayerList);
                
                for (Int32 i = 0; i < players.Count; i++)
                {
                    BasePlayer player = players[i];
                    if (player == null || !player.IsConnected) continue;
                    CheckAllHomes(player);
                }
                
                Pool.FreeUnmanaged(ref players);
            });
            
            if(nanoModalMenu)
                Interface.CallHook("OnInitializedModule", Name);
        }
        
        private void OnNewSave(String filename) => AutoClearData(true);

        private void OnPlayerConnected(BasePlayer player) => RegisteredUser(player);

        private void OnServerShutdown() => Unload();

        private void Unload()
        {
            if (_ == null) return;
            WriteData();
            
            InterfaceBuilder.DestroyAll();

            if (_imageUI != null)
            {
                _imageUI.UnloadImages();
                _imageUI = null;
            }

            if (cachedUI != null)
            {
                cachedUI.Clear();
                cachedUI = null;
            }

            if (playerLoaded != null)
            {
                playerLoaded.Clear();
                playerLoaded = null;
            }

            if (timerCheckHomes is { Destroyed: false })
            {
                timerCheckHomes.Destroy();
                timerCheckHomes = null;
            }

            if (timerCheckLimit is { Destroyed: false })
            {
                timerCheckLimit.Destroy();
                timerCheckLimit = null;
            }

            if (bagInstalled != null && bagInstalled.Count != 0)
            {
                bagInstalled.Clear();
                bagInstalled = null;
            }
            
            if (apartmentRoomsServer != null && apartmentRoomsServer.Count != 0)
                apartmentRoomsServer.Clear();

            apartmentBuilding = null;
            
            if(nanoModalMenu)
                Interface.CallHook("OnUnloadedModule", Name);

            _ = null;
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (plan == null || go == null) return;
            BaseEntity entity = go.ToBaseEntity();
            if (entity == null) return;
            SleepingBag bag = entity as SleepingBag;
            if (bag == null) return;
            BasePlayer player = plan.GetOwnerPlayer();
            if (player == null) return;
            
            Dictionary<String, PlayerHome> pData = playerHomes[player.userID];
            if (pData.Count >= config.homeController.homeCount.GetCount(player, true)) return;
            
            Vector3 positionHome = bag.transform.position;
            Tugboat tugboatEntity = player.GetParentEntity() as Tugboat;
            PlayerBoat playerBoat = player.GetParentEntity() as PlayerBoat;
            
            if (config.homeController.setupHomeController.canSetupTugboatHome && tugboatEntity) 
            {
                Boolean isAuthedFriend = true;
                
                UInt64[] friendList = GetFriendList(player);
                if (friendList != null && friendList.Length != 0)
                {
                    isAuthedFriend = tugboatEntity.children
                        .OfType<VehiclePrivilege>()
                        .Any(vehiclePrivilege => friendList.Any(vehiclePrivilege.IsAuthed));
                }

                if (!isAuthedFriend && !tugboatEntity.IsAuthedForBuilding(player))
                    return;
                
                bagInstalled[player] = bag;
                DrawUI_HomeSetup(player);
                return;
            }
            
            if (config.homeController.setupHomeController.canSetupPlayerBoats && playerBoat) 
            {
                Boolean isAuthedFriend = true;
                UInt64[] friendList = GetFriendList(player);
                if (friendList != null && friendList.Length != 0)
                {
                    isAuthedFriend = playerBoat.children
                        .OfType<VehiclePrivilege>()
                        .Any(vehiclePrivilege => friendList.Any(vehiclePrivilege.IsAuthed));
                }

                if (!isAuthedFriend && !playerBoat.IsAuthedForBuilding(player))
                    return;
                
                bagInstalled[player] = bag;
                DrawUI_HomeSetup(player);
                return;
            }
            
            BuildingPrivlidge buildingPrivilage = player.GetBuildingPrivilege();

            Configuration.HomeController.SetupHome homeController = config.homeController.setupHomeController;
            if(homeController.onlyBuildingPrivilage && !buildingPrivilage)
                return;

            if (homeController.onlyBuildingAuth && buildingPrivilage != null && !player.IsBuildingAuthed())
                return;
            
            BuildingBlock buildingBlock = GetBuildingBlock(positionHome);
            if (!buildingBlock)
            {
                buildingBlock = GetBuildingBlock(positionHome);
                if (!buildingBlock)
                    return;
            }

            bagInstalled[player] = bag;
            
            DrawUI_HomeSetup(player);
        }
        
        private Object OnMapMarkerAdd(BasePlayer player, MapNote note) 
        {
            if (player == null || note == null)
                return null;
            
            Vector3 positionTeleportation = note.worldPosition;
            positionTeleportation.y = GetGroundPosition(positionTeleportation);

            if (config.homeController.useGMapTeleportHome)
            {
                Dictionary<String, Vector3> homePlayer = GetHomes(player.userID);
                if (homePlayer != null && homePlayer.Count != 0)
                {
                    foreach (KeyValuePair<String, Vector3> pHome in homePlayer)
                    {
                        if (Vector3.Distance(positionTeleportation, pHome.Value) < 30f)
                        {
                            RequestHome(player, new[] { pHome.Key });
                            return false;
                        }
                    }
                }
            }

            if (config.teleportationController.teleportSetting.useGMapTeleport)
            {
                RelationshipManager.PlayerTeam teams = player.Team;
                if (teams != null)
                {
                    foreach (UInt64 teamsMember in teams.members)
                    {
                        if (teamsMember == player.userID) continue;
                        BasePlayer teamPlayer = BasePlayer.FindByID(teamsMember);
                        if (teamPlayer == null) continue;
                        if (Vector3.Distance(positionTeleportation, teamPlayer.transform.position) < 30f)
                        {
                            RequestTeleportation(player, new []{ teamPlayer.UserIDString });
                            return false;
                        }
                    }
                }
            }

            if (config.teleportationController.teleportSetting.useGMapTeleportAdmin)
            {
                if (playerSettings[player.userID].useTeleportGMap && (player.IsAdmin || permission.UserHasPermission(player.UserIDString,
                        permissionGMapTeleport)))
                {
                    MovePlayer(player, positionTeleportation);
                    return false;
                }
            }

            return null;
        }

        private Object CanDismountEntity(BasePlayer player, BaseMountable mountable)
        {
            if (player == null) return null;
            if (playerLoaded == null) return null;
            
            if (playerLoaded.Contains(player))
                return false;
            
            return null;
        }
        
        private void OnEntitySpawned(Tugboat tugboat)
        {
            if (tugboat == null) return;
            UInt64 netIDTugBoat = tugboat.net.ID.Value;
            
            tugboatsServer.TryAdd(netIDTugBoat, tugboat);
        }   
        
        private void OnEntitySpawned(PlayerBoat playerBoat)
        {
            if (playerBoat == null) return;
            UInt64 netIDplayerBoat = playerBoat.net.ID.Value;
            
            playersBoatsServer.TryAdd(netIDplayerBoat, playerBoat);
        }   
        
        private void OnEntitySpawned(ApartmentRoom apartmentRoom)
        {
            if (apartmentRoom == null) return;

            apartmentRoomsServer[apartmentRoom.net.ID.Value] = apartmentRoom;

            if (apartmentBuilding) return;

            apartmentBuilding = ApartmentBuilding.Instance;
            apartmentSearchRadiusSqr = ApartmentBuilding.MaxRadiusSearch * ApartmentBuilding.MaxRadiusSearch;
        }
        
        private void OnEntityKill(ApartmentRoom apartmentRoom)
        {
            if (!apartmentRoom) return;
            apartmentRoomsServer.Remove(apartmentRoom.net.ID.Value);
        }
        
        private void OnEntityKill(Tugboat tugboat)
        {
            if (!tugboat) return;
            UInt64 netIDTugBoat = tugboat.net.ID.Value;

            tugboatsServer.Remove(netIDTugBoat);
        }
        
        private void OnEntityKill(PlayerBoat playerBoat)
        {
            if (!playerBoat) return;
            UInt64 netIDplayerBoat = playerBoat.net.ID.Value;

            playersBoatsServer.Remove(netIDplayerBoat);
        }
        
        private void OnEntityDeath(Tugboat tugboat, HitInfo info)
        {
            if (!tugboat) return;
            UInt64 netIDTugBoat = tugboat.net.ID.Value;
            
            tugboatsServer.Remove(netIDTugBoat);
        }
        
        private void OnEntityDeath(PlayerBoat playerBoat, HitInfo info)
        {
            if (!playerBoat) return;
            UInt64 netIDplayerBoat = playerBoat.net.ID.Value;
            
            playersBoatsServer.Remove(netIDplayerBoat);
        }
        
        private void OnEntitySpawneds(AutoTurret turret)
        {
            if (turret == null) return;
            if(ItemIdCorrecteds.Contains(turret.net.ID.Value))
                turretsEx.TryAdd(turret.net.ID.Value, turret);
        }
        
        #endregion
        
        #region Metods
        
        #region Utilites
        
        private String[] ConvertArgs(StringView[] args)
        {
            if (args == null)
                return null;

            String[] result = new String[args.Length];

            for (Int32 i = 0; i < args.Length; i++)
                result[i] = args[i].ToString();
            
            return result;
        }
        
        private void ControllerDataLimits()
        {
            useHomeLimit = config.homeController.limitTeleportationHome.useLimitTeleportationHome;
            useWarpLimit = config.warpsContoller.limitWarps.useLimitWarp;
            useTeleportationLimit = config.teleportationController.limitTeleportation.useLimitTeleportation;

            if (!(useHomeLimit || useWarpLimit || useTeleportationLimit))
                return;

            LimitSpec[] limits = new[]
            {
                new LimitSpec(
                    TypeLimit.Home,
                    useHomeLimit,
                    () => playerLimitController.homeTime,
                    v => playerLimitController.homeTime = v,
                    () => config.homeController.limitTeleportationHome.hoursResetLimit
                ),
                new LimitSpec(
                    TypeLimit.Warp,
                    useWarpLimit,
                    () => playerLimitController.warpTime,
                    v => playerLimitController.warpTime = v,
                    () => config.warpsContoller.limitWarps.hoursResetLimit 
                ),
                new LimitSpec(
                    TypeLimit.Teleportation,
                    useTeleportationLimit,
                    () => playerLimitController.teleportTime,
                    v => playerLimitController.teleportTime = v,
                    () => config.teleportationController.limitTeleportation.hoursResetLimit
                )
            };

            foreach (LimitSpec l in limits)
            {
                if (!l.Enabled) continue;
                if (l.GetTime() == 0)
                    l.SetTime(CurrentTime + l.GetHours() * secondsPerHour);
            }

            timerCheckLimit = timer.Every(checkInterval, () =>
            {
                foreach (LimitSpec l in limits)
                {
                    if (!l.Enabled) continue;

                    if (CurrentTime >= l.GetTime())
                    {
                        foreach (KeyValuePair<UInt64, Dictionary<TypeLimit, Int32>> kv in playerLimitController.playersLimits)
                        {
                            Dictionary<TypeLimit, Int32> dict = kv.Value;
                            if (dict.TryGetValue(l.LimitType, out Int32 value) && value > 0)
                                dict[l.LimitType] = 0;
                        }
                        
                        l.SetTime(CurrentTime + l.GetHours() * secondsPerHour);
                    }
                }
            });
        }

        private void LogAction(BasePlayer player, TypeLog typeLog, BasePlayer targetPlayer = default, Vector3 position = default, String homeName = default)
        {
            if (!config.generalController.useLogged && !RustApp) return;

            String typeAction = typeLog switch
            {
                TypeLog.RequestTeleportation => $"{player.displayName}({player.userID}) {tprLog} {targetPlayer.displayName}({targetPlayer.userID})",
                TypeLog.AcceptTeleportation => $"{player.displayName}({player.userID}) {tpaLog} {targetPlayer.displayName}({targetPlayer.userID})",
                TypeLog.RequestHomeTeleportation => $"{player.displayName}({player.userID}) {homeLog} `{homeName}` ({position})",
                _ => ""
            };

            if (config.generalController.useLogged)
                LogToFile("IQTeleportation", typeAction, _, true, true);
            
            if(RustApp)
            {
                String typeActionRustApp = typeLog switch
                {
                    TypeLog.RequestTeleportation => $"{player.userID} {tprLog} {targetPlayer.userID}",
                    TypeLog.AcceptTeleportation => $"{player.userID} {tpaLog} {targetPlayer.userID}",
                    TypeLog.RequestHomeTeleportation => $"{player.userID} {homeLog} `{homeName}` ({position})",
                    _ => ""
                };
                RustApp_SendNotify(typeActionRustApp);
            }
        }
        
        private void RegisteredPermissions()
        {
            #region Warp

            if (config.warpsContoller.useWarps)
            {
                foreach (String countWarpCooldownPermissions in config.warpsContoller.teleportWarpCooldown
                             .countPermissions.Keys)
                {
                    if (!permission.PermissionExists(countWarpCooldownPermissions, this))
                        permission.RegisterPermission(countWarpCooldownPermissions, this);
                }

                foreach (String countWarpCoutdownPermissions in config.warpsContoller.teleportWarpCountdown
                             .countPermissions.Keys)
                {
                    if (!permission.PermissionExists(countWarpCoutdownPermissions, this))
                        permission.RegisterPermission(countWarpCoutdownPermissions, this);
                }
                
                foreach (String permissionWarp in config.warpsContoller.warpPermissions.Values)
                {
                    if (!permission.PermissionExists(permissionWarp, this))
                        permission.RegisterPermission(permissionWarp, this);
                }
                
                if(config.warpsContoller.limitWarps.useLimitWarp)
                    foreach (String countWarpsLimitPermissions in config.warpsContoller.limitWarps.limitWarps.countPermissions.Keys)
                    {
                        if (!permission.PermissionExists(countWarpsLimitPermissions, this))
                            permission.RegisterPermission(countWarpsLimitPermissions, this);
                    }
            }

            #endregion

            #region Teleport

            foreach (String countTeleportCooldownPermissions in config.teleportationController.teleportCooldown
                         .countPermissions.Keys)
            {
                if (!permission.PermissionExists(countTeleportCooldownPermissions, this))
                    permission.RegisterPermission(countTeleportCooldownPermissions, this);
            }

            foreach (String countTeleportCoutdownPermissions in config.teleportationController.teleportCountdown
                         .countPermissions.Keys)
            {
                if (!permission.PermissionExists(countTeleportCoutdownPermissions, this))
                    permission.RegisterPermission(countTeleportCoutdownPermissions, this);
            }
            
            if(config.teleportationController.limitTeleportation.useLimitTeleportation)
                foreach (String countTeleportationLimitPermissions in config.teleportationController.limitTeleportation.limitTeleportation.countPermissions.Keys)
                {
                    if (!permission.PermissionExists(countTeleportationLimitPermissions, this))
                        permission.RegisterPermission(countTeleportationLimitPermissions, this);
                }

            #endregion

            #region Home

            foreach (String countHomePermissions in config.homeController.homeCount.countPermissions.Keys)
            {
                if (!permission.PermissionExists(countHomePermissions, this))
                    permission.RegisterPermission(countHomePermissions, this);
            }

            foreach (String countHomeCooldownPermissions in config.homeController.homeCooldown.countPermissions.Keys)
            {
                if (!permission.PermissionExists(countHomeCooldownPermissions, this))
                    permission.RegisterPermission(countHomeCooldownPermissions, this);
            }

            foreach (String countHomeCoutdownPermissions in config.homeController.homeCountdown.countPermissions.Keys)
            {
                if (!permission.PermissionExists(countHomeCoutdownPermissions, this))
                    permission.RegisterPermission(countHomeCoutdownPermissions, this);
            }
            
            if(config.homeController.limitTeleportationHome.useLimitTeleportationHome)
                foreach (String countHomeLimitPermissions in config.homeController.limitTeleportationHome.limitHomeTeleportation.countPermissions.Keys)
                {
                    if (!permission.PermissionExists(countHomeLimitPermissions, this))
                        permission.RegisterPermission(countHomeLimitPermissions, this);
                }

            #endregion
            
            if (!permission.PermissionExists(permissionGMapTeleport, this))
                permission.RegisterPermission(permissionGMapTeleport, this);

            if (!permission.PermissionExists(permissionTP, this))
                permission.RegisterPermission(permissionTP, this);
        }

        private (BasePlayer, String) FindPlayerNameOrID(BasePlayer finder, String nameOrID, Boolean findSleeper = false, Boolean isIgnoreFinderName = true)
        {
            List<BasePlayer> players = Pool.Get<List<BasePlayer>>();
            players.AddRange(findSleeper ? BasePlayer.allPlayerList : BasePlayer.activePlayerList);
            
            if (nameOrID.IsSteamId() && ulong.TryParse(nameOrID, out UInt64 userID))
            {
                BasePlayer player = BasePlayer.FindByID(userID);
                Pool.FreeUnmanaged(ref players);
                return (player, null);
            }

            List<BasePlayer> matchingPlayers = Pool.Get<List<BasePlayer>>();

            try
            {
                matchingPlayers.AddRange(players.Where(player =>
                    player.displayName != null &&
                    player.displayName.IndexOf(nameOrID, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (!isIgnoreFinderName || player.userID != finder.userID) &&
                    (!findSleeper || matchingPlayers.All(p => p.userID != player.userID))));
                
                switch (matchingPlayers.Count)
                {
                    case 1:
                    {
                        BasePlayer foundPlayer = matchingPlayers[0];
                        return (foundPlayer, null);
                    }
                    case > 1:
                    {
                        String nicknameList = String.Join(", ", matchingPlayers.Take(3).Select(p => p.displayName));
                        return (null, GetLang("FIND_PLAYER_INFO_MATCHES", finder.UserIDString, nameOrID, nicknameList));
                    }
                    default:
                        return (null, null);
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref players);
                Pool.FreeUnmanaged(ref matchingPlayers);
            }
        }
        
        private void CreateMapMarkerPlayer(BasePlayer player, Vector3 position, String homeName)
        {
            if (player.State?.pointsOfInterest == null)
                player.State.pointsOfInterest = new List<MapNote>();
            
            if (player.State.pointsOfInterest.Count >= ConVar.Server.maximumMapMarkers) return;
            
            MapNote note = Pool.Get<MapNote>();
            note.worldPosition = position;
            note.isPing = false;
            
            note.colourIndex = 2;
            note.icon = 2;
            note.noteType = 1;
            
            note.label = homeName;
            player.State.pointsOfInterest.Add(note);
            player.DirtyPlayerState();
            player.TeamUpdate();

            using MapNoteList mapNoteList = Pool.Get<MapNoteList>();
            mapNoteList.notes = Pool.Get<List<MapNote>>();
            
            if (player.ServerCurrentDeathNote != null)
                mapNoteList.notes.Add(player.ServerCurrentDeathNote);

            if (player.State.pointsOfInterest != null)
                mapNoteList.notes.AddRange(player.State.pointsOfInterest);
                
            player.ClientRPC(RpcTarget.Player("Client_ReceiveMarkers", player), mapNoteList);
            mapNoteList.notes.Clear();
                
            Pool.FreeUnmanaged(ref mapNoteList.notes);
        }
        
        private void AddPingAtLocation(BasePlayer player, Vector3 location)
        {
            player.State.pings ??= new List<MapNote>();
            MapNote note = Pool.Get<MapNote>();
            note.worldPosition = location;
            note.isPing = true;
            note.timeRemaining = note.totalDuration = 30f;
            note.colourIndex = 2;
            note.icon = 2;
            player.State.pings.Add(note);
            player.DirtyPlayerState();
            player.SendPingsToClient();
            player.TeamUpdate(true);
        }
        
        private void ParseBoats(Boolean findTugboat, Boolean findBoatBuilding)
        {
            if (!findBoatBuilding && !findTugboat) return;
            
            Int32 tugboatCount = 0;
            Int32 playerBoats = 0;
            
            foreach (BaseNetworkable serverEntity in BaseNetworkable.serverEntities.entityList.Get().Values)
            {
                if (findTugboat)
                {
                    if (serverEntity is Tugboat tugboat)
                    {
                        if (tugboatsServer.TryAdd(serverEntity.net.ID.Value, tugboat))
                            tugboatCount++;
                    }
                }

                if (findBoatBuilding)
                {
                    if (serverEntity is PlayerBoat playerBoat)
                    {
                        if (playersBoatsServer.TryAdd(serverEntity.net.ID.Value, playerBoat))
                            playerBoats++;
                    }
                }
            }
            Puts(LanguageEn ? $"Saved {tugboatCount} tugboats and {playerBoats} player boats for setting players' home points.\n" : $"Сохранено {tugboatCount} буксиров и {playerBoats} лодок игроков для установки точки дома игроков");
        }
        
        private void ParseApartments()
        {
            apartmentRoomsServer.Clear();

            apartmentBuilding = ApartmentBuilding.Instance;
            apartmentSearchRadiusSqr = ApartmentBuilding.MaxRadiusSearch * ApartmentBuilding.MaxRadiusSearch;

            if (apartmentBuilding)
            {
                foreach (ApartmentRoom room in apartmentBuilding.Rooms)
                {
                    if (!room || room.IsDestroyed) continue;
                    apartmentRoomsServer[room.net.ID.Value] = room;
                }
            }

            foreach (TriggerSafeZoneOverride safeZone in TriggerSafeZoneOverride.allHostileZones)
            {
                if (!safeZone) continue;

                ApartmentRoom room = safeZone.Apartment;
                if (!room || room.IsDestroyed) continue;

                apartmentRoomsServer[room.net.ID.Value] = room;
            }

            if (apartmentRoomsServer.Count == 0) return;

            Puts(LanguageEn ? $"Saved {apartmentRoomsServer.Count} apartment rooms for setting players' home points" : $"Сохранено {apartmentRoomsServer.Count} комнат апартаментов для установки точек дома игроков");
        }
        
        private String GetNextHomeName(Dictionary<String, PlayerHome> pData)
        {
            for (Int32 i = 1; i <= 99; i++)
            {
                String homeName = i.ToString();
        
                if (!pData.ContainsKey(homeName))
                    return homeName;
            }
    
            return null; 
        }
        
        private static void DrawSphereAndText(BasePlayer player, in Vector3 position, String text, Single time = 30f)
        {
            player.SendConsoleCommand("ddraw.text", time, Color.green, position + Vector3.up, $"<size=35>{text}</size>");
            player.SendConsoleCommand("ddraw.sphere", time, Color.green, position, 0.5f);
        }
        
        private void RunEffect(BasePlayer player, String effectPath)
        {
            if (!config.generalController.useSoundEffects) return;
            Effect effect = new Effect(effectPath, player, 0, new Vector3(), new Vector3());
            EffectNetwork.Send(effect, player.Connection);
        }

        private String FormatTime(Double seconds, String userID = null)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            String result = String.Empty;
    
            String daysLabel = GetLang("TITLE_FORMAT_DAYS", userID);
            String hoursLabel = GetLang("TITLE_FORMAT_HOURSE", userID);
            String minutesLabel = GetLang("TITLE_FORMAT_MINUTES", userID);
            String secondsLabel = GetLang("TITLE_FORMAT_SECONDS", userID);

            if (time.Days > 0)
                result += $"{Format(time.Days, daysLabel, daysLabel, daysLabel)} ";
    
            if (time.Hours > 0)
                result += $"{Format(time.Hours, hoursLabel, hoursLabel, hoursLabel)} ";
    
            if (time.Minutes > 0)
                result += $"{Format(time.Minutes, minutesLabel, minutesLabel, minutesLabel)} ";
    
            if (time.Seconds > 0 || string.IsNullOrEmpty(result)) 
                result += $"{Format(time.Seconds, secondsLabel, secondsLabel, secondsLabel)}";

            return result.Trim(); 
        }

        private String Format(Int32 units, String form1, String form2, String form3)
        {
            Int32 tmp = units % 10;

            if (units is >= 5 and <= 20 || tmp is >= 5 and <= 9 || tmp == 0)
                return $"{units}{form1}";

            return tmp is >= 2 and <= 4 ? $"{units}{form2}" : $"{units}{form3}";
        }

        
        private Single GetGroundPosition(in Vector3 pos)
        {
            Single y = TerrainMeta.HeightMap.GetHeight(pos);
            return Physics.Raycast(new Vector3(pos.x, pos.y + 200f, pos.z), Vector3.down, out RaycastHit hitInfo, float.MaxValue, (Rust.Layers.Mask.Vehicle_Large | Rust.Layers.Solid | Rust.Layers.Mask.Water)) ? Mathf.Max(hitInfo.point.y, y) : y;
        }
        
        #endregion

        #region Monuments
        private Boolean IsPlayerWithinBlockedMonument(BasePlayer player)
        {
            if (config.teleportationController.teleportSetting.noTeleportUnderwateLabs && EnvironmentManager.Check(player.transform.position, EnvironmentType.UnderwaterLab))
                return true;
            
            if (config.teleportationController.teleportSetting.noTeleportTrains && EnvironmentManager.Check(player.transform.position, EnvironmentType.TrainTunnels))
                return true;
            
            if (config.generalController.monumentBlockedTeleportation.Count == 0) return false;
            MonumentInfo monument = GetMonumentWherePlayerStand(player);
            return monument && config.generalController.monumentBlockedTeleportation.Contains(monument.name);
        }
        
        private MonumentInfo GetMonumentWherePlayerStand(BasePlayer player)
        {
            MonumentInfo closestMonument = null;
            
            foreach (MonumentInfo monument in localAllMonuments)
            {
                Vector3 monumentCenter = monument.transform.TransformPoint(monument.Bounds.center);
                Vector3 monumentSize = monument.transform.TransformVector(monument.Bounds.size);
                Single monumentRadius = monumentSize.magnitude / 2f;
                Single distanceToMonument = Vector3.Distance(player.transform.position, monumentCenter);
                if (distanceToMonument <= monumentRadius)
                {
                    closestMonument = monument;
                    break;
                }
            }

            return closestMonument;
        }
        
        private MonumentInfo GetMonumentInName(String nameMonument)
        {
            if (String.IsNullOrWhiteSpace(nameMonument)) return null;
            
            foreach (MonumentInfo monument in localAllMonuments)
            {
                if (monument.name.Equals(nameMonument))
                    return monument;
            }

            return null;
        }
        
        private void ParseMonuments()
        {
            localAllMonuments.Clear();
            
            Int32 countAddedMonuments = 0;
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument.Bounds.size == Vector3.zero)
                    continue;
                
                localAllMonuments.Add(monument);
                countAddedMonuments++;
            }
            
            Puts(LanguageEn ? $"Received information about {countAddedMonuments} monuments on the map" : $"Получена информация о {countAddedMonuments} монументах на карте");
        }

        #endregion

        #region Cancell Teleportation

        private void CancellTeleportation(BasePlayer player)
        {
            if (config.teleportationController.teleportSetting.suggetionAcceptedUI)
                DestroyUI(player, InterfaceBuilder.IQ_TELEPORT_UI_TELEPORT);
                
            UserRepository pData = localUsersRepository[player];
            if (playerTeleportationQueue.TryGetValue(player.userID, out TeleportationQueueInfo requester))
            {
                if (config.teleportationController.teleportSetting.suggetionAcceptedUI)
                    DestroyUI(requester.player, InterfaceBuilder.IQ_TELEPORT_UI_TELEPORT);
                
                SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", player.UserIDString), player);
                SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", requester.player.UserIDString), requester.player);
            
                ClearQueue(player, requester.player);
                pData.DeActiveTeleportation();
                
                Interface.Call("OnTeleportRejected", player, requester.player);
                
                SimpleStatus_DestroyStatus(player, config.generalController.simpleStatusController.settingTeleportationProcess.GetFormatNameStatus(player, "timerTeleportation"));
                return;
            }

            if (pData.IsActiveTeleportation())
            {
                SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", player.UserIDString), player);
                pData.DeActiveTeleportation();
                
                Interface.Call("OnTeleportRejected", player, requester?.player);
                SimpleStatus_DestroyStatus(player, config.generalController.simpleStatusController.settingTeleportationProcess.GetFormatNameStatus(player, "timerTeleportation"));

                _interface?.UpdateMapButtons(player);
                return;
            }
            
            SendChat(GetLang("CAN_CANCELL_TELEPORTATION_NOT_FOUNDS", player.UserIDString), player);
        }

        #endregion
        
        #region Teleportation

        #region Request Teleportation

        #region Accept
        
        private Boolean AcceptRequestTeleportation(BasePlayer player, Boolean isAutoAccept = false)
        {
            if (!playerTeleportationQueue.TryGetValue(player.userID, out TeleportationQueueInfo requester))
            {
                SendChat(GetLang("CAN_ACCEPT_TELEPORTATION_NOT_REQUEST", player.UserIDString), player, true);
                return false;
            }
            
            if (!requester.isActive)
            {
                SendChat(GetLang("CAN_ACCEPT_TELEPORTATION_NOT_REQUEST", player.UserIDString), player, true); 
                return false;
            }

            if (requester.isAccepted)
            {
                SendChat(GetLang("CAN_ACCEPT_TELEPORTATION_ACTIVE_ACCEPTED", player.UserIDString), player, true);
                return false;
            }
            
            String canTeleport = CanTeleportation(player, UserRepository.TeleportType.Teleportation, false, true);
            if (!String.IsNullOrWhiteSpace(canTeleport))
            {
                SendChat(canTeleport, player, true);
                localUsersRepository[requester.player].DeActiveTeleportation();
                return false;
            }
            
            Object OnTeleportRequested = Interface.Call("OnTeleportRequested", player, requester.player);
            if (OnTeleportRequested != null)
            {
                if (OnTeleportRequested is String hookResult)
                {
                    SendChat(hookResult, player, true);
                    localUsersRepository[requester.player].DeActiveTeleportation(); 
                    return false;
                }
                
                if (OnTeleportRequested is Boolean hookResultBoolean)
                    if (!hookResultBoolean)
                    {
                        SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", player.UserIDString), player, true);
                        localUsersRepository[requester.player].DeActiveTeleportation(); 
                        return false;
                    }
            }

            requester.isAccepted = true;
            
            if (config.teleportationController.teleportSetting.suggetionAcceptedUI)
                DestroyUI(player, InterfaceBuilder.IQ_TELEPORT_UI_TELEPORT);
            
            Int32 teleportationCountdown = config.teleportationController.teleportCountdown.GetCount(requester.player);
            
            SimpleStatus_SetStatus(player, config.generalController.simpleStatusController.settingTeleportationProcess.GetFormatNameStatus(player, "timerTeleportation"), teleportationCountdown);
            
            Coroutine routineTeleportation = ServerMgr.Instance.StartCoroutine(ProcessTTarget(requester.player, player, teleportationCountdown));
            
            localUsersRepository[requester.player].ActiveCoroutineController(routineTeleportation);
            casheTeleportationLast[player] = requester.player;

            String autoRequestMessage = isAutoAccept ? "CAN_REQUEST_TELEPORTATION_ACCESS_AUTO_TELEPORT" : "CAN_REQUEST_TELEPORTATION_ACCESS";

            LogAction(player, TypeLog.AcceptTeleportation, requester.player, default, default);
            
            SendChat(GetLang(autoRequestMessage, player.UserIDString, requester.player.displayName), player);
            SendChat(GetLang("REQUEST_TELEPORTATION_ACCESS", requester.player.UserIDString, FormatTime(teleportationCountdown, requester.player.UserIDString)), requester.player);

            Interface.Call("OnTeleportAccepted", player, requester.player, teleportationCountdown);

            return true;
        }

        #endregion
        
        #region Send

        private void ClearQueue(BasePlayer player, BasePlayer targetPlayer)
        {
            if (player)
            {
                UserRepository pData = localUsersRepository[player];
                
                if(pData != null && pData.IsActiveTeleportation())
                    pData.DeActiveTeleportation();
            
                playerTeleportationQueue.Remove(player.userID);

                if (teleportationActions.TryGetValue(player, out Action action))
                {
                    player.CancelInvoke(action);
                    teleportationActions.Remove(player);
                }
            }
            
            if (targetPlayer)
            {
                UserRepository tData = localUsersRepository[targetPlayer];
                
                if(tData != null && tData.IsActiveTeleportation())
                    tData.DeActiveTeleportation();
                
                playerTeleportationQueue.Remove(targetPlayer.userID);
                
                if (teleportationActions.TryGetValue(targetPlayer, out Action actionTarget))
                {
                    targetPlayer.CancelInvoke(actionTarget);
                    teleportationActions.Remove(targetPlayer);
                }
            }
        }

        private void RequestTeleportation(BasePlayer player, String[] arg)
        {
            if(config.teleportationController.limitTeleportation.useLimitTeleportation)
                if (IsLimitExhausted(player, TypeLimit.Teleportation))
                {
                    SendChat(GetLang("CAN_TELEPORT_LIMIT_NOTHING_TELEPORTATION", player.UserIDString), player);
                    return;
                }
            
            String friendNameOrID = arg.Length >= 1 ? String.Join(" ", arg) : String.Empty;
            (BasePlayer, String) findInfo = new();
            
            BasePlayer targetPlayer;
            if (String.IsNullOrWhiteSpace(friendNameOrID))
            {
                casheTeleportationLast.TryGetValue(player, out BasePlayer oldPlayerTeleport);
                targetPlayer = oldPlayerTeleport; 
            }
            else
            {
                findInfo = FindPlayerNameOrID(player, friendNameOrID);
                targetPlayer = findInfo.Item1;
            }
            
            String requestError = CanRequestTeleportation(player, targetPlayer, findInfo.Item2);
            if (!String.IsNullOrWhiteSpace(requestError))
            {
                SendChat(requestError, player, true);
                return;
            }
            
            playerTeleportationQueue.TryAdd(targetPlayer.userID, new TeleportationQueueInfo
            {
                player = player,
                isActive = true,
                isAccepted = false
            });
            
            playerTeleportationQueue.TryAdd(player.userID, new TeleportationQueueInfo
            {
                player = targetPlayer,
                isActive = false,
                isAccepted = false
            });
            casheTeleportationLast[player] = targetPlayer;

            if (playerSettings[targetPlayer.userID].autoAcceptTeleportationFriends &&
                IsFriends(player, targetPlayer.userID))
            {
                if (!AcceptRequestTeleportation(targetPlayer, true))
                {
                    SendChat(GetLang("CAN_ACCEPT_TELEPORTATION_NOT_AUTO_ACCEPT", player.UserIDString), player);
                    ClearQueue(player, targetPlayer);
                    if (localUsersRepository[player].IsActiveTeleportation())
                        teleportationActions.Remove(player);

                    return;
                }
                SendChat(GetLang("CAN_ACCEPT_TELEPORTATION_YES_AUTO_ACCEPT", player.UserIDString), player);
            }
            else
            {
                if (config.teleportationController.teleportSetting.suggetionAcceptedUI)
                    DrawUI_TeleportPlayer(player, targetPlayer);

                SendChat(GetLang("CAN_REQUEST_TELEPORTATION_SEND", player.UserIDString, targetPlayer.displayName), player);
                SendChat(GetLang("CAN_REQUEST_TELEPORTATION_RECEIVE", targetPlayer.UserIDString, player.displayName), targetPlayer);
            }

            LogAction(player, TypeLog.RequestTeleportation, targetPlayer, default, default);

            Action teleportWaitAccept = () =>
            {
                if (localUsersRepository[player].IsActiveTeleportation())
                {
                    teleportationActions.Remove(player);
                    return;
                }
                
                ClearQueue(player, targetPlayer);
                SendChat(GetLang("CAN_TELEPORT_REQUEST_EXPIRED", player.UserIDString), player);
                SendChat(GetLang("CAN_TELEPORT_REQUEST_EXPIRED", targetPlayer.UserIDString), targetPlayer); 
            };

            teleportationActions[player] = teleportWaitAccept;
            
            player.Invoke(teleportWaitAccept, 15f);
        }
        
        private String CanRequestTeleportation(BasePlayer player, BasePlayer targetPlayer, String error = "")
        {
            if (!targetPlayer)
                return error ?? GetLang("CAN_REQUEST_TELEPORTATION_NOT_FOUND_PLAYER", player.UserIDString);

            if (player.userID == targetPlayer.userID) 
                return GetLang("CAN_REQUEST_TELEPORTATION_NOTHING_TELEPORT_ME", player.UserIDString);
            
            if (playerTeleportationQueue.ContainsKey(player.userID))
                return playerTeleportationQueue[player.userID].player 
                     ? GetLang("CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY_NAME", player.UserIDString, playerTeleportationQueue[player.userID].player.displayName) 
                     : GetLang("CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY", player.UserIDString);
            
            if (playerTeleportationQueue.ContainsKey(targetPlayer.userID))
                return GetLang("CAN_REQUEST_TELEPORTATION_ALREADY_REQUEST_TARGET", player.UserIDString);
            
            String canTeleport = CanTeleportation(player, UserRepository.TeleportType.Teleportation);
            if (!String.IsNullOrWhiteSpace(canTeleport))
                return canTeleport;
        
            if (config.teleportationController.teleportSetting.onlyTeleportationFriends && !IsFriends(player, targetPlayer.userID))
                return GetLang("CAN_REQUEST_TELEPORTATION_ONLY_REQUEST_FRIENDS", player.UserIDString);
            
            return String.Empty;
        }
        
        #endregion
        
        #endregion
        
        #region Can Teleportation
        
        private String CanTeleportation(BasePlayer player, UserRepository.TeleportType type, Boolean isRequest = true, Boolean isSkipCooldown = false, BaseEntity parentEntity = null)
        {
            if (Interface.Call("CanTeleport", player) is String hookResult)
                return String.IsNullOrWhiteSpace(hookResult) ? GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString) : hookResult;
            
            if (Interface.Call("canTeleport", player) is String hookResultTwo)
                return String.IsNullOrWhiteSpace(hookResultTwo) ? GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString) : hookResultTwo;

            switch (type)
            {
                case UserRepository.TeleportType.Warp:
                    if (Interface.Call("CanTeleportWarp", player) is String CanTeleportWarp)
                        return String.IsNullOrWhiteSpace(CanTeleportWarp) ? GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString) : CanTeleportWarp;
                    break;
                case UserRepository.TeleportType.Home:
                {
                    if (Interface.Call("CanTeleportHome", player) is String CanTeleportHome)
                        return String.IsNullOrWhiteSpace(CanTeleportHome) ? GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString) : CanTeleportHome;

                    if (config.homeController.noTeleportInSafeZone && player.InSafeZone())
                        return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_SAFE_ZON", player.UserIDString));
                    
                    break;
                }
                case UserRepository.TeleportType.Teleportation:
                {
                    if (Interface.Call("CanTeleportPlayer", player) is String CanTeleportPlayer)
                        return String.IsNullOrWhiteSpace(CanTeleportPlayer) ? GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString) : CanTeleportPlayer;

                    if (config.teleportationController.teleportSetting.noTeleportInSafeZone && player.InSafeZone())
                        return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_SAFE_ZON", player.UserIDString));

                    if (!isRequest && !config.teleportationController.teleportSetting.noTeleportVehicle)
                    {
                        BaseVehicle vehicleEnt = parentEntity as BaseVehicle;
                        Boolean isVehicle = vehicleEnt;
                    
                        if (isVehicle)
                        {
                            if (!IsValidVehicleMountPoint(player, vehicleEnt, false, out String result) && !String.IsNullOrWhiteSpace(result))
                                return GetLang(result, player.UserIDString);
                        }
                    }
                    break;
                }
            }
            
            String statusPlayer = CheckStatusPlayer(player, isRequest, type);
            if (!String.IsNullOrWhiteSpace(statusPlayer))
                return statusPlayer;
            
            if (config.teleportationController.teleportSetting.noTeleportInRaidBlock && IsRaidBlocked(player))
                return GetLang("CAN_TELEPORT_RAID_BLOCK", player.UserIDString);
            
            BaseEntity playerParentEntity = player.GetParentEntity();

            if (playerParentEntity)
            {
                if (config.teleportationController.teleportSetting.noTeleportHotAirBalloon && playerParentEntity is HotAirBalloon)
                    return GetLang("CAN_TELEPORT_TITILE_OTHER", player.UserIDString,
                        GetLang("CAN_TELEPORT_IN_HOT_AIR_BALLOON", player.UserIDString));

                if (config.teleportationController.teleportSetting.noTeleportCargoShip && playerParentEntity is CargoShip)
                    return GetLang("CAN_TELEPORT_TITILE_OTHER", player.UserIDString,
                        GetLang("CAN_TELEPORT_IN_CARGO_SHIP", player.UserIDString));
            }

            if (config.teleportationController.teleportSetting.noTeleportBuildingBlock && player.IsBuildingBlocked())
                return GetLang("CAN_TELEPORT_TITILE_OTHER", player.UserIDString,
                    GetLang("CAN_TELEPORT_IN_BUILDING_BLOCKED", player.UserIDString));

            if (IsPlayerWithinBlockedMonument(player))
                return GetLang("CAN_TELEPORT_MONUMENT_BLOCKED", player.UserIDString);

            if (isSkipCooldown) return String.Empty;
            
            UserRepository userRepository = localUsersRepository[player];
            String getCooldown = userRepository.GetCooldownTitle(player, type);
            return !String.IsNullOrWhiteSpace(getCooldown) ? GetLang("CAN_TELEPORT_COOLDOWN", player.UserIDString, getCooldown) : String.Empty;
        }

        #endregion

        #region Wait Teleportation
        
        private IEnumerator ProcessTeleportation(BasePlayer player, Func<Boolean> additionalChecks, Action teleportationAction, Int32 timeTeleportation, BasePlayer targetPlayer = null)
        {
            Single elapsedTime = 0f;
            Single nextCheckTime = 0f;

            while (elapsedTime < timeTeleportation)
            {
                Single interval = !config.generalController.useSoundEffects ? 1f : Mathf.Lerp(0.2f, 1.0f, Mathf.Pow(Math.Max(0, (timeTeleportation - elapsedTime) / timeTeleportation), 2));
                
                if (elapsedTime >= nextCheckTime)
                {
                    if (!player)
                    {
                        ClearQueue(player, targetPlayer);
                        if (targetPlayer)
                            SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", targetPlayer.UserIDString, $"{GetLang("ACCESS_CANCELL_TELEPORTATION_DEAD_REQUESTER_DISCONNECTED", targetPlayer.UserIDString)}"), targetPlayer);
                       
                        Interface.Call("OnTeleportInterrupted", player);
                        yield break;
                    }

                    if (player.IsDead())
                    {
                        ClearQueue(player, targetPlayer);
                        SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", player.UserIDString, $"{GetLang("ACCESS_CANCELL_TELEPORTATION_DEAD", player.UserIDString)}"), player);
                        if (targetPlayer)
                            SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", targetPlayer.UserIDString, $"{GetLang("ACCESS_CANCELL_TELEPORTATION_DEAD_REQUESTER_DEAD", targetPlayer.UserIDString)}"), targetPlayer);
                        
                        Interface.Call("OnTeleportInterrupted", player);
                        yield break;
                    }
                    
                    if (config.teleportationController.teleportSetting.noTeleportBlood && player.SecondsSinceAttacked < 1.5f && player.lastDamage != DamageType.Cold)
                    {
                        ClearQueue(player, targetPlayer);
                        SendChat(GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_WOUNDED", player.UserIDString)), player);
                        Interface.Call("OnTeleportInterrupted", player);
                        yield break;
                    }

                    if (additionalChecks())
                    {
                        Interface.Call("OnTeleportInterrupted", player);
                        yield break;
                    }

                    nextCheckTime = elapsedTime + checkIntervalPlayerTeleportation;
                }

                RunEffect(player, effectSoundTimer);
                yield return new WaitForSeconds(interval);
                elapsedTime += interval;
            }

            if (!player || player.IsDead() || additionalChecks())
            {
                Interface.Call("OnTeleportInterrupted", player);
                yield break;
            }

            teleportationAction();
        }

        private IEnumerator ProcessTTarget(BasePlayer player, BasePlayer targetPlayer, Int32 timeTeleportation)
        {
            Vector3 teleportationPosition = config.teleportationController.teleportSetting.teleportPosInAccept ? targetPlayer.transform.position : default;
            return ProcessTeleportation(
                player,
                () =>
                {
                    if (!targetPlayer)
                    {
                        ClearQueue(player, targetPlayer);
                        
                        SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", player.UserIDString, $"{GetLang("ACCESS_CANCELL_TELEPORTATION_DEAD_TARGET_DISCONNECTED", player.UserIDString)}"), player);
                        return true;
                    }
                    
                    if (!targetPlayer.IsDead()) return false;
                    ClearQueue(player, targetPlayer);
                    SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", targetPlayer.UserIDString, $"{GetLang("ACCESS_CANCELL_TELEPORTATION_DEAD", targetPlayer.UserIDString)}"), targetPlayer);
                    SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION", player.UserIDString, $"{GetLang("ACCESS_CANCELL_TELEPORTATION_DEAD_TARGET", player.UserIDString)}"), player);
                    return true;
                },
                () => TeleportationPlayer(player, targetPlayer, teleportationPosition),
                timeTeleportation,
                targetPlayer
            );
        }

        private IEnumerator ProcessTHome(BasePlayer player, PlayerHome pHome, Int32 timeTeleportation)
        {
            Vector3 position = pHome.positionHome;
            BaseEntity parentEntity = null;
            ApartmentRoom apartmentRoom = pHome.typeEntity == TypeHomeEntity.Apartment ? GetApartmentRoomByID(pHome.netIdEntity) : null;
            
            return ProcessTeleportation(
                player,
                () =>
                {
                    if (pHome.netIdEntity == 0)
                    {
                        BuildingBlock buildingBlock = GetBuildingBlock(position);
                        if (!buildingBlock)
                        {
                            SendChat(GetLang("CAN_TELEPORT_HOME_FOUNDATION_DESTROYED", player.UserIDString), player, true);
                            return true;
                        }

                        return false;
                    }
                    
                    switch (pHome.typeEntity)
                    {
                        case TypeHomeEntity.Tugboat:
                        {
                            if (!tugboatsServer.TryGetValue(pHome.netIdEntity, out Tugboat tugboat) || !tugboat || tugboat.IsDestroyed)
                            {
                                SendChat(GetLang("CAN_TELEPORT_HOME_TUGBOAT_DESTROYED", player.UserIDString), player, true);
                                return true;
                            }
                        
                            parentEntity = tugboat;
                            break;
                        }
                        case TypeHomeEntity.Apartment:
                        {
                            if (!IsApartmentValid(apartmentRoom) || !IsApartmentAuthed(apartmentRoom, player))
                            {
                                SendChat(GetLang("CAN_TELEPORT_HOME_APARTMENT_LOST", player.UserIDString), player, true);
                                return true;
                            }
                            break;
                        }
                        case TypeHomeEntity.PlayerBoat:
                        {
                            if (!playersBoatsServer.TryGetValue(pHome.netIdEntity, out PlayerBoat playerBoat) || !playerBoat || playerBoat.IsDestroyed)
                            {
                                SendChat(GetLang("CAN_TELEPORT_HOME_PLAYER_BOAT_DESTROYED", player.UserIDString), player, true);
                                return true;
                            }
                        
                            parentEntity = playerBoat;
                            break;
                        }
                    }
                    
                    return false;
                }, 
                () => TeleportationHome(player, position, parentEntity),
                timeTeleportation
            );
        }

        private IEnumerator ProcessTWarp(BasePlayer player, Vector3 position, Int32 timeTeleportation)
        {
            return ProcessTeleportation(
                player,
                () => false,
                () => TeleportationWarp(player, position),
                timeTeleportation
            );
        }
        
        #endregion

        private void TeleportationHome(BasePlayer player, Vector3 position, BaseEntity pEntity)
        {
            if (!player.IsValid())
                return;

            String canTeleport = CanTeleportation(player, UserRepository.TeleportType.Home);
            if (!String.IsNullOrWhiteSpace(canTeleport))
            {
                SendChat(canTeleport, player, true);
                return;
            }
            
            localUsersRepository[player].SetupCooldown(player, UserRepository.TeleportType.Home);
            MovePlayer(player, position, pEntity, pEntity);
            
            Interface.Call("OnPlayerTeleportedHome", player, position);

            _interface?.UpdateMapButtons(player);

            if (!config.homeController.limitTeleportationHome.useLimitTeleportationHome) return;
            Int32 leftLimit = ConsumeLimit(player, TypeLimit.Home);
            Boolean isLimitExhausted = leftLimit <= 0;
            String messageResult = isLimitExhausted ? GetLang("TELEPORTATION_HOME_LIMIT_NOTHING", player.UserIDString) : GetLang("TELEPORTATION_HOME_LIMIT_INFO", player.UserIDString, leftLimit);
                
            SendChat(messageResult, player, isLimitExhausted);
        }
        
        private void TeleportationWarp(BasePlayer player, Vector3 position)
        {
            if (!player.IsValid())
                return;

            String canTeleport = CanTeleportation(player, UserRepository.TeleportType.Warp);
            if (!String.IsNullOrWhiteSpace(canTeleport))
            {
                SendChat(canTeleport, player, true);
                return;
            }

            localUsersRepository[player].SetupCooldown(player, UserRepository.TeleportType.Warp); 
            MovePlayer(player, position);
            SendChat(GetLang("WARP_TELEPORTATION_PLAYER", player.UserIDString), player);
            
            Interface.Call("OnPlayerTeleportedWarp", player, position);
            
            if (!config.warpsContoller.limitWarps.useLimitWarp) return;
            Int32 leftLimit = ConsumeLimit(player, TypeLimit.Warp);
            Boolean isLimitExhausted = leftLimit <= 0;
            String messageResult = isLimitExhausted ? GetLang("TELEPORTATION_WARP_LIMIT_NOTHING", player.UserIDString) : GetLang("TELEPORTATION_WARP_LIMIT_INFO", player.UserIDString, leftLimit);
                
            SendChat(messageResult, player, isLimitExhausted);
        }

        private void TeleportationPlayer(BasePlayer player, BasePlayer targetPlayer, Vector3 teleportationPosition)
        {
            if (!player.IsValid())
                return;

            ClearQueue(player, targetPlayer);

            BaseVehicle vehicle = targetPlayer.isMounted ? targetPlayer.GetMountedVehicle() : null;
            
            String canTeleport = CanTeleportation(player, UserRepository.TeleportType.Teleportation, parentEntity: vehicle);
            if (!String.IsNullOrWhiteSpace(canTeleport))
            {
                SendChat(canTeleport, player, true);
                Interface.Call("OnTeleportInterrupted", player);
                return;
            }

            String canTeleportTarget = CanTeleportation(targetPlayer, UserRepository.TeleportType.Teleportation, false, true, parentEntity: vehicle);
            if (!String.IsNullOrWhiteSpace(canTeleportTarget))
            {
                SendChat(canTeleportTarget, targetPlayer, true);
                Interface.Call("OnTeleportInterrupted", player);
                return;
            }

            if (config.teleportationController.teleportSetting.onlyTeleportationFriends)
            {
                if (!IsFriends(player, targetPlayer.userID))
                {
                    SendChat(GetLang("CAN_TELEPORT_ONLY_FRIEND", player.UserIDString), player, true);
                    SendChat(GetLang("CAN_TELEPORT_ONLY_FRIEND_TARGET", player.UserIDString), targetPlayer, true);
                    return;
                }
            }
            
            if (teleportationPosition == default)
                teleportationPosition = targetPlayer.transform.position;

            localUsersRepository[player].SetupCooldown(player, UserRepository.TeleportType.Teleportation);
            
            MovePlayer(player, teleportationPosition, vehicle ? vehicle : targetPlayer.GetParentEntity());
            
            Interface.Call("OnPlayerTeleported", player, targetPlayer);
            
            if (!config.teleportationController.limitTeleportation.useLimitTeleportation) return;
            Int32 leftLimit = ConsumeLimit(player, TypeLimit.Teleportation);
            Boolean isLimitExhausted = leftLimit <= 0;
            String messageResult = isLimitExhausted ? GetLang("TELEPORTATION_PLAYER_LIMIT_NOTHING", player.UserIDString) : GetLang("TELEPORTATION_PLAYER_LIMIT_INFO", player.UserIDString, leftLimit);
            
            SendChat(messageResult, player, isLimitExhausted);
        }

        private void MovePlayer(BasePlayer player, Vector3 position, BaseEntity parentEntity = null, BaseEntity parentHomePosition = null)
        {
            BaseVehicle vehicleEnt = parentEntity as BaseVehicle;
            Boolean isVehicle = !config.teleportationController.teleportSetting.noTeleportVehicle && vehicleEnt;
            Boolean isVehicleSeatValiable = false;
            if (isVehicle)
            {
                if (IsValidVehicleMountPoint(player, vehicleEnt, true, out String result))
                    isVehicleSeatValiable = true;
                else if (!String.IsNullOrWhiteSpace(result))
                {
                    SendChat(GetLang(result, player.UserIDString), player, true);
                    return;
                }
            }
            
            Vector3 resultPosition = parentHomePosition ? parentHomePosition.transform.TransformPoint(position) : position;
            resultPosition.y += 0.3f;
        
            Interface.Call("OnPlayerTeleported", player, player.transform.position, resultPosition);
            
            player.PauseFlyHackDetection();
            player.PauseSpeedHackDetection();
            player.EnsureDismounted();
            player.Server_CancelGesture();

            if (!isVehicle)
                player.StartSleeping();
            else
            {
                playerLoaded.Add(player);
                player.Invoke(() => _.playerLoaded.Remove(player), 3f);
            }
            
            player.ClientRPC(RpcTarget.Player("StartLoading_Quick", player), true);
        
            player.SetParent(null, true);   

            if (parentEntity && !isVehicle)
                player.SetParent(parentEntity, true);
            
            player.Teleport(resultPosition);

            if (isVehicleSeatValiable)
            {
                if (vehicleEnt is BaseBoat)
                {
                    BaseMountable idealMountPointFor = vehicleEnt.GetIdealMountPointFor(player);
                    if (idealMountPointFor)
                        idealMountPointFor.MountPlayer(player);
                }  
                else vehicleEnt.AttemptMount(player, false);
            }
            
            if (!player._limitedNetworking)
                player.ForceUpdateTriggers();
            
            player.ClientRPC(RpcTarget.Player("ForceViewAnglesTo", player), resultPosition);
        
            player.UpdateNetworkGroup();
            player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            player.SendNetworkUpdateImmediate();
        
            if (config.generalController.stopSleepingAfterTeleport && !isVehicle)
                player.Invoke(player.EndSleeping, 0.5f);
        
            RunEffect(player, effectSoundTeleport);
        }

        private Boolean IsValidVehicleMountPoint(BasePlayer player, BaseVehicle vehicle, Boolean isRequester, out String errorForRequester)
        {
            RidableHorse horse = vehicle as RidableHorse;
            if (horse)
            {
                if (horse.HasSingleSaddle)
                {
                    errorForRequester = isRequester ? "CAN_REQUEST_PLAYER_NOT_MOUNT_POINT" : "CAN_TELEPORT_PLAYER_NOT_MOUNT_POINT";
                    return false;
                }

                if (horse.IsPlayerTooHeavy(player))
                {
                    errorForRequester = isRequester ? "CAN_REQUEST_PLAYER_MOUNT_ERROR" : "CAN_TELEPORT_PLAYER_MOUNT_ERROR";
                    return false;
                }
                
                if (!horse.MountEligable(player))
                {
                    errorForRequester = isRequester ? "CAN_REQUEST_PLAYER_NOT_MOUNT_POINT" : "CAN_TELEPORT_PLAYER_NOT_MOUNT_POINT";
                    return false;
                }

                errorForRequester = String.Empty;
                return true;
            }

            BaseMountable idealMountPointFor = vehicle.GetIdealMountPoint(vehicle.transform.position, vehicle.transform.position, player);//vehicle.GetIdealMountPointFor(player);
            if (!idealMountPointFor)
            {
                errorForRequester = isRequester ? "CAN_REQUEST_PLAYER_NOT_MOUNT_POINT" : "CAN_TELEPORT_PLAYER_NOT_MOUNT_POINT";
                return false;
            }
            
            Int32 mountPoints = vehicle.allMountPoints.Count();
            foreach (BaseVehicle.MountPointInfo mountPointInfo in vehicle.allMountPoints)
            {
                if (mountPointInfo.mountable.AnyMounted())
                    mountPoints--;
            }

            if (mountPoints < 1)
            {
                errorForRequester = isRequester ? "CAN_REQUEST_PLAYER_NOT_MOUNT_POINT" : "CAN_TELEPORT_PLAYER_NOT_MOUNT_POINT";
                return false;
            }
            
            if (idealMountPointFor._mounted || idealMountPointFor.IsDead() || !player.CanMountMountablesNow() || idealMountPointFor.IsTransferring() || idealMountPointFor.IsSeatClipping(idealMountPointFor))
            {
                errorForRequester = isRequester ? "CAN_REQUEST_PLAYER_MOUNT_ERROR" : "CAN_TELEPORT_PLAYER_MOUNT_ERROR";
                return false;
            }
            
            errorForRequester = String.Empty;
            return true;
        }

        #endregion
        
        #region Status Player

        private String CheckStatusPlayer(BasePlayer player, Boolean isRequester, UserRepository.TeleportType type, Boolean canSethome = false)
        {
            if (!canSethome)
            {
                if (config.teleportationController.teleportSetting.noTeleportFrozen && player.metabolism.temperature.value < 0)
                    return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_COLD", player.UserIDString));

                if ((config.teleportationController.teleportSetting.noTeleportBlood && player.metabolism.bleeding.value > 0) || player.IsWounded())
                    return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_WOUNDED", player.UserIDString));

                if (config.teleportationController.teleportSetting.noTeleportRadiation && player.metabolism.radiation_poison.value > 0)
                    return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_RADIATION", player.UserIDString));
            }

            if (player.IsDead())
                return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_DEAD", player.UserIDString));

            if (player.IsRestrainedOrSurrendering)
                return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_RESTRAINED", player.UserIDString));

            if (player.IsSleeping())
                return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_SLEEPING", player.UserIDString));

            if (config.teleportationController.teleportSetting.noTeleportInSwimming && player.IsSwimming())
                return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_SWIMMING", player.UserIDString));

            if (player.IsInTutorial)
                return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_IS_TUTORIAL", player.UserIDString));

            if (player.IsFlying)
                return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_FLYING", player.UserIDString));

            if (isRequester)
            {
                if (player.isMounted)
                    return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_MOUNTED", player.UserIDString));
            } 
            
            if (type == UserRepository.TeleportType.Teleportation)
            {
                if (!config.teleportationController.teleportSetting.noTeleportVehicle) return String.Empty;
                if (player.isMounted)
                    return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_MOUNTED", player.UserIDString));
            }
            else
            {
                if (player.isMounted)
                    return GetLang("STATUS_PLAYER_TITLE", player.UserIDString, GetLang("STATUS_PLAYER_MOUNTED", player.UserIDString));
            }

            return String.Empty;
        }

        #endregion

        #region Warps

        private void ValidateWarps()
        {
            if (config.warpsContoller.useWarps)
            {
                foreach (KeyValuePair<String, List<ServerWarp>> allWarps in serverWarps)
                {
                    for (Int32 i = 0; i < allWarps.Value.Count; i++)
                    {
                        ServerWarp warp = allWarps.Value[i];
                        if (String.IsNullOrWhiteSpace(warp.monumentParent)) continue;
                        MonumentInfo monument = GetMonumentInName(warp.monumentParent);
                        if (monument != null)
                        {
                            serverWarps[allWarps.Key][i].hideWarp = false;
                            continue;
                        }
                        
                        serverWarps[allWarps.Key][i].hideWarp = true;
                    }
                }

                GenerateWarp();
            }
            else if (serverWarps.Keys.Count != 0)
            {
                foreach (String warpName in serverWarps.Keys)
                {
                    cmd.RemoveChatCommand(warpName, this);
                    cmd.RemoveConsoleCommand(warpName, this);
                }
            }
        }

        private void GenerateWarp()
        {
            cmd.AddChatCommand("warp", this, nameof(ChatCommandWarp));
            if (serverWarps.Keys.Count == 0)
                Puts(LanguageEn ? "Warps were not initialized; number of existing warps: 0" : "Варпы не были инициализированы, количество существующих варпов : 0");
            else
            {
                String warps = String.Join(", ", serverWarps.Keys);
                foreach ((String warpName, List<ServerWarp> value) in serverWarps)
                {
                    Boolean allHidden = value.All(warp => warp.hideWarp);
                    if (allHidden) continue;
                    
                    cmd.AddChatCommand(warpName, this, nameof(ChatCommandTeleportationWarp));
                    cmd.AddConsoleCommand(warpName, this, nameof(ConsoleCommandTeleportationWarp));
                }

                Puts(LanguageEn ? $"Initialized {serverWarps.Keys.Count} warps; reserved commands: {warps}" : $"Инициализировано {serverWarps.Keys.Count} варпов, для них зарезервированы команды : {warps}");
            }
        }

        private String GetWarpPoints(String warpName)
        {
            String pointsKeys = String.Empty;
            if (!serverWarps.TryGetValue(warpName, out List<ServerWarp> warp)) return pointsKeys;
            for (Int32 i = 0; i < warp.Count; i++)
                pointsKeys += $"\n№{i}";

            return pointsKeys;
        }
        
        private void RequestWarp(BasePlayer player, ServerWarp serverWarp, String warpName)
        {
            if (!config.warpsContoller.useWarps)
                return;
            
            if(config.warpsContoller.limitWarps.useLimitWarp)
                if (IsLimitExhausted(player, TypeLimit.Warp))
                {
                    SendChat(GetLang("CAN_TELEPORT_LIMIT_NOTHING_WARP", player.UserIDString), player);
                    return;
                }
            
            Vector3 warpPosition = serverWarp.GetPositionWarp();
            if (warpPosition == default) return;
            
            if (config.warpsContoller.noTeleportWaprHostile)
            {
                if (!String.IsNullOrWhiteSpace(serverWarp.monumentParent))
                {
                    MonumentInfo monument = GetMonumentInName(serverWarp.monumentParent);
                    if (monument)
                    {
                        if (monument.IsSafeZone && player.IsHostile())
                        {
                            SendChat(GetLang("WARP_TELEPORTATION_PLAYER_HOSTILE", player.UserIDString), player);
                            return;
                        }
                    }
                }
            }
            
            String requestError = CanTeleportation(player, UserRepository.TeleportType.Warp);
            if (!String.IsNullOrWhiteSpace(requestError))
            {
                SendChat(requestError, player, true);
                return;
            }

            Int32 timeTeleportation = config.warpsContoller.teleportWarpCountdown.GetCount(player);
            
            Interface.Call("OnWarpAccepted", player, warpPosition, timeTeleportation, warpName?.ToUpper()); 
            
            SimpleStatus_SetStatus(player, config.generalController.simpleStatusController.settingTeleportationProcess.GetFormatNameStatus(player, "timerTeleportation"), timeTeleportation);
            
            Coroutine routineTeleportation = ServerMgr.Instance.StartCoroutine(ProcessTWarp(player, warpPosition, timeTeleportation));
            localUsersRepository[player].ActiveCoroutineController(routineTeleportation);
            
            SendChat(GetLang("WARP_REQUEST_TELEPORTATION_PLAYER", player.UserIDString, timeTeleportation), player);
        }

        #endregion

        #region Homes
        private void RequestHome(BasePlayer player, String[] arg)
        {
            if(config.homeController.limitTeleportationHome.useLimitTeleportationHome)
                if (IsLimitExhausted(player, TypeLimit.Home))
                {
                    SendChat(GetLang("CAN_TELEPORT_LIMIT_NOTHING_HOME", player.UserIDString), player);
                    return;
                }

            String homeName = arg[0].ToLower();
            String friendNameOrID = arg.Length >= 2 ? arg[1] : String.Empty;
            
            String requestError = CanRequestTeleportHome(player, homeName, friendNameOrID, out UInt64 userIDFriend, out String friendName);
            if (!String.IsNullOrWhiteSpace(requestError))
            {
                SendChat(requestError, player, true);
                return;
            }
            
            Boolean isOtherID = userIDFriend != default && userIDFriend != player.userID;
            UInt64 userID = isOtherID ? userIDFriend : player.userID;

            PlayerHome pHome = playerHomes[userID][homeName];
            Int32 coutdownTeleportation = config.homeController.homeCountdown.GetCount(player);
            String formatCountdown = _.FormatTime(coutdownTeleportation, player.UserIDString);

            if (localUsersRepository[player].IsActiveTeleportation())
            {
                localUsersRepository[player].DeActiveTeleportation();
                SendChat(GetLang("ACCESS_CANCELL_TELEPORTATION_PLAYER", player.UserIDString), player); 
            }

            Int32 countdownTeleportation = config.homeController.homeCountdown.GetCount(player);
            Interface.Call("OnHomeAccepted", player, homeName, countdownTeleportation); 

            Coroutine routineTeleportation = ServerMgr.Instance.StartCoroutine(ProcessTHome(player, pHome, countdownTeleportation)); 
            localUsersRepository[player].ActiveCoroutineController(routineTeleportation);
            
            SimpleStatus_SetStatus(player, config.generalController.simpleStatusController.settingTeleportationProcess.GetFormatNameStatus(player, "timerTeleportation"), countdownTeleportation);

            String resultMessage = !String.IsNullOrWhiteSpace(friendName) && isOtherID 
                ? GetLang("ACCESS_TELEPORTATION_HOME_FRIEND_NICK", player.UserIDString, friendName, homeName, formatCountdown)
                : String.IsNullOrWhiteSpace(friendName) && isOtherID
                    ? GetLang("ACCESS_TELEPORTATION_HOME_FRIEND", player.UserIDString, homeName, formatCountdown)
                    : GetLang("ACCESS_TELEPORTATION_HOME", player.UserIDString, homeName, formatCountdown);

            if (pHome.netIdEntity == 0)
                LogAction(player, TypeLog.RequestHomeTeleportation, default, pHome.positionHome, homeName);
            
            SendChat(resultMessage, player);

            SaveHomeToMemory(player, homeName);
            _interface?.UpdateMapButtons(player);
        }

        private String CanRequestTeleportHome(BasePlayer player, String homeName, String friendNameOrID, out UInt64 userIDFriend, out String friendName)
        {
            CheckAllHomes(player, homeName);

            userIDFriend = default;
            friendName = String.Empty;
            UInt64 userID = player.userID;

            String canTeleport = CanTeleportation(player, UserRepository.TeleportType.Home);
            if (!String.IsNullOrWhiteSpace(canTeleport))
                return canTeleport;

            if (playerTeleportationQueue.ContainsKey(player.userID))
            {
                return playerTeleportationQueue[player.userID].player != null
                    ? GetLang("CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY_NAME", player.UserIDString,
                        playerTeleportationQueue[player.userID].player.displayName)
                    : GetLang("CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY", player.UserIDString);
            }

            if (!String.IsNullOrWhiteSpace(friendNameOrID))
            {
                if (!config.homeController.useFriendHomeTeleportation)
                    return GetLang("CAN_TELEPORT_PLAYER_NOT_USE_FRIEND_HOME", player.UserIDString);
                        
                BasePlayer bPlayer = BasePlayer.FindAwakeOrSleeping(friendNameOrID); 
                if (!bPlayer)
                    return GetLang("CAN_TELEPORT_HOME_NOT_FOUND", player.UserIDString, friendNameOrID);

                userIDFriend = bPlayer.userID;
                friendName = bPlayer.displayName;

                if (!IsFriends(player, userIDFriend))
                    return GetLang("CAN_TELEPORT_HOME_NOT_FRIEND", player.UserIDString, friendNameOrID);

                userID = userIDFriend;

                if (!playerSettings[userIDFriend].useTeleportationHomeFromFriends && userID != player.userID)
                    return GetLang("CAN_TELEPORT_PLAYER_NOT_SETTING_USE_FRIEND_HOME", player.UserIDString);
            }

            if (!playerHomes.TryGetValue(userID, out Dictionary<String, PlayerHome> pData) ||
                !pData.TryGetValue(homeName, out PlayerHome homePlayer))
                return GetLang("CAN_TELEPORT_HOME_NOT_HOME", player.UserIDString, homeName);

            if (Interface.Call("CanTeleportHome", player, homePlayer.positionHome) is String hookResult)
                return String.IsNullOrWhiteSpace(hookResult) ? GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString) : hookResult;

            Vector3 positionHome = homePlayer.positionHome;
            if (homePlayer.netIdEntity != 0) 
            {
                switch (homePlayer.typeEntity)
                {
                    case TypeHomeEntity.Tugboat:
                    {
                        if (!tugboatsServer.TryGetValue(homePlayer.netIdEntity, out Tugboat tugboat) || tugboat == null || tugboat.IsDestroyed)
                            return GetLang("CAN_TELEPORT_HOME_NOT_TUGBOAT", player.UserIDString, homeName);

                        return !tugboat.IsAuthedForBuilding(player) ? GetLang("CAN_TELEPORT_HOME_NOT_TUGBOAT_NO_AUTH", player.UserIDString) : String.Empty;
                    }
                    case TypeHomeEntity.PlayerBoat:
                    {
                        if (!playersBoatsServer.TryGetValue(homePlayer.netIdEntity, out PlayerBoat playerBoat) || playerBoat == null || playerBoat.IsDestroyed)
                            return GetLang("CAN_TELEPORT_HOME_NOT_PLAYER_BOAT", player.UserIDString, homeName);
                        
                        return !playerBoat.IsAuthedForBuilding(player) ? GetLang("CAN_TELEPORT_HOME_NOT_PLAYER_BOAT_NO_AUTH", player.UserIDString) : String.Empty; 
                    }
                    case TypeHomeEntity.Apartment:
                    {
                        ApartmentRoom apartmentRoom = GetApartmentRoomByID(homePlayer.netIdEntity);
                        if (!IsApartmentValid(apartmentRoom))
                        {
                            pData.Remove(homeName);
                            RemoveHomeInMemory(player, homeName);
                            return GetLang("CAN_TELEPORT_HOME_NOT_APARTMENT", player.UserIDString, homeName);
                        }

                        return !IsApartmentAuthed(apartmentRoom, player) ? GetLang("CAN_TELEPORT_HOME_NOT_APARTMENT_NO_AUTH", player.UserIDString, homeName) : String.Empty;
                    }
                }
            }

            BuildingBlock buildingBlock = GetBuildingBlock(positionHome);
            if (!buildingBlock)
            {
                pData.Remove(homeName);
                
                RemoveHomeInMemory(player, homeName);
                
                return GetLang("CAN_TELEPORT_HOME_NOT_BUILDING_BLOCK", player.UserIDString, homeName);
            }
            
            if (config.homeController.setupHomeController.onlyBuildingAuth)
            {
                BuildingPrivlidge privilege = buildingBlock.GetBuildingPrivilege();
                if (privilege != null && !privilege.IsAuthed(player))
                {
                    if (userID != player.userID)
                        return GetLang("CAN_TELEPORT_NULL_REASON", player.UserIDString);

                    pData.Remove(homeName);

                    RemoveHomeInMemory(player, homeName);

                    return GetLang("CAN_TELEPORT_HOME_NOT_AUTH_BUILDING_PRIVILAGE", player.UserIDString, homeName);
                }
            }

            return String.Empty;
        }

        #region Memory Home In Map

        private void SaveHomeToMemory(BasePlayer player, String homeName)
        {
            if (!config.homeController.uiMapHomeList) return;
            
            if (!playerLastHomes.TryGetValue(player.userID, out List<String> list))
                playerLastHomes[player.userID] = list = new List<String>();

            list.Remove(homeName);        
            list.Insert(0, homeName);      
            
            DrawUI_LastHomes_Map(player);
        }

        private void RemoveHomeInMemory(BasePlayer player, String homeName)
        {
            if (!config.homeController.uiMapHomeList) return;
            if (!playerLastHomes.TryGetValue(player.userID, out List<String> list)) return;

            list.Remove(homeName);

            if (list.Count <= 0)
                playerLastHomes.Remove(player.userID);
            
            DrawUI_LastHomes_Map(player);
        }

        #endregion
        
        #region Apartments

        private ApartmentRoom GetApartmentRoom(BasePlayer player)
        {
            if (!player || apartmentRoomsServer.Count == 0) return null;

            if (apartmentBuilding)
            {
                ApartmentRoom ownedRoom = apartmentBuilding.GetPlayerApartment(player);
                if (ownedRoom && !ownedRoom.IsDestroyed && ownedRoom.IsInsideRoom(player))
                    return ownedRoom;
            }

            TriggerSafeZoneOverride safeZoneTrigger = player.FindTrigger<TriggerSafeZoneOverride>();
            ApartmentRoom triggerRoom = safeZoneTrigger != null ? safeZoneTrigger.Apartment : null;

            if (triggerRoom && !triggerRoom.IsDestroyed)
                return triggerRoom;

            if (apartmentBuilding && (player.transform.position - apartmentBuilding.transform.position).sqrMagnitude > apartmentSearchRadiusSqr)
                return null;

            foreach (ApartmentRoom room in apartmentRoomsServer.Values)
            {
                if (!room || room.IsDestroyed) continue;
                if (room.IsInsideRoom(player))
                    return room;
            }

            return null;
        }

        private ApartmentRoom GetApartmentRoomByID(UInt64 netID) => netID != 0 && apartmentRoomsServer.TryGetValue(netID, out ApartmentRoom room) && room && !room.IsDestroyed ? room : null;

        private Boolean IsApartmentValid(ApartmentRoom room) => room && !room.IsDestroyed && room.IsCurrentlyRented();

        private Boolean IsApartmentAuthed(ApartmentRoom room, BasePlayer player) => room && player && room.IsAuthed(player.userID, ApartmentRoom.ApartmentAuth.EnterApartment);

        #endregion
        
        #region Set Home
        
        private void SetHome(BasePlayer player, String homeName, SleepingBag bag = null)
        {
            homeName = homeName.ToLower();
            
            CheckAllHomes(player, homeName);

            Vector3 positionHome = player.transform.position;
            Vector3 positionHomePing = player.transform.position;
            
            if (bag)
            {
                positionHome = bag.transform.position + new Vector3(0f, 0.5f, 0f);
                positionHomePing = positionHome;
                bag.niceName = homeName;
                bag.name = homeName;
                bag.SendNetworkUpdate();
            }
            
            BuildingBlock buildingBlock = GetBuildingBlock(positionHome);
            Tugboat tugboatEntity = player.GetParentEntity() as Tugboat;
            PlayerBoat playerBoat = player.GetParentEntity() as PlayerBoat;
            ApartmentRoom apartmentRoom = GetApartmentRoom(player);
            
            UInt64 netIdEntity = 0;
            TypeHomeEntity typeHomeEntity = TypeHomeEntity.None;
            
            if (tugboatEntity)
            {
                netIdEntity = tugboatEntity.net.ID.Value;
                positionHome = tugboatEntity.transform.InverseTransformPoint(positionHome);
                typeHomeEntity = TypeHomeEntity.Tugboat;
            }

            if (playerBoat)
            {
                netIdEntity = playerBoat.net.ID.Value;
                positionHome = playerBoat.transform.InverseTransformPoint(positionHome);
                typeHomeEntity = TypeHomeEntity.PlayerBoat;
            }
            
            if (apartmentRoom && !tugboatEntity && !playerBoat)
            {
                netIdEntity = apartmentRoom.net.ID.Value;
                typeHomeEntity = TypeHomeEntity.Apartment;
            }
            
            String canSethome = CanSetHome(player, buildingBlock, homeName, positionHome, tugboatEntity, playerBoat, apartmentRoom);
            
            if (!String.IsNullOrWhiteSpace(canSethome))
            {
                SendChat(canSethome, player, true);
                return;
            }

            playerHomes[player.userID].Add(homeName, new PlayerHome()
            {
                netIdEntity = netIdEntity,
                positionHome = positionHome,
                typeEntity = typeHomeEntity,
            });

            if (config.homeController.addedPingMarker && !tugboatEntity && !playerBoat)
                AddPingAtLocation(player, positionHomePing);

            if (config.homeController.addedMapMarkerHome && !tugboatEntity && !playerBoat)
                CreateMapMarkerPlayer(player, positionHome, homeName);
            
            SaveHomeToMemory(player, homeName);
            
            SendChat(GetLang("ACCESS_SETUP_SETHOME", player.UserIDString, homeName), player);
            
            Interface.Call("OnHomeAdded", player, positionHome, homeName);
        }

        private String CanSetHome(BasePlayer player, BuildingBlock buildingBlock, String homeName, in Vector3 positionHome, Tugboat tugboatEntity = null, PlayerBoat playerBoat = null, ApartmentRoom apartmentRoom = null)
        {
            Configuration.HomeController.SetupHome homeController = config.homeController.setupHomeController;
            Object canSetHome = Interface.Call("OnHomeAdd", player, homeName, positionHome);

            if (canSetHome != null)
            {
                if (canSetHome is String errorMessage && !String.IsNullOrWhiteSpace(errorMessage))
                    return errorMessage;

                return GetLang("CAN_SETHOME_OTHER_MESSAGE", player.UserIDString);
            }

            String statusPlayer = CheckStatusPlayer(player, true, UserRepository.TeleportType.Home);
            if (!String.IsNullOrWhiteSpace(statusPlayer))
                return statusPlayer;
            
            if(homeController.noSetupInRaidBlock && IsRaidBlocked(player))
                return GetLang("CAN_SETHOME_IS_RAIDBLOCK", player.UserIDString);
            
            Dictionary<String, PlayerHome> pData = playerHomes[player.userID];

            if (pData.Count >= config.homeController.homeCount.GetCount(player, true))
                return GetLang("CAN_SETHOME_MAXIMUM_HOMES", player.UserIDString);
            
            if (pData.ContainsKey(homeName))
                return GetLang("CAN_SETHOME_EXIST_HOMENAME", player.UserIDString, homeName);

            foreach (KeyValuePair<String, PlayerHome> homesPlayer in pData)
            {
                if (Vector3.Distance(homesPlayer.Value.positionHome, positionHome) < 2.8f) 
                    return GetLang("CAN_SETHOME_DISTANCE_EXIST_HOME", player.UserIDString, homesPlayer.Key);
            }
            
            if (tugboatEntity)
            {
                if (!config.homeController.setupHomeController.canSetupTugboatHome)
                    return GetLang("CAN_SETHOME_TUGBOAT_DISABLE", player.UserIDString);

                UInt64[] friendList = GetFriendList(player);
                if(friendList == null || friendList.Length == 0)
                    return !tugboatEntity.IsAuthedForBuilding(player) ? GetLang("CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_TUGBOAT", player.UserIDString) : String.Empty;

                Boolean isAuthedFriend = tugboatEntity.children
                    .OfType<VehiclePrivilege>()
                    .Any(vehiclePrivilege => friendList.Any(vehiclePrivilege.IsAuthed));

                return !isAuthedFriend && !tugboatEntity.IsAuthedForBuilding(player) ? GetLang("CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_TUGBOAT", player.UserIDString) : String.Empty; 
            }
            
            if (playerBoat)
            {
                if (!config.homeController.setupHomeController.canSetupPlayerBoats)
                    return GetLang("CAN_SETHOME_BOAT_FOUNDATION_DISABLE", player.UserIDString); 
                
                UInt64[] friendList = GetFriendList(player);
                if(friendList == null || friendList.Length == 0)
                    return player.IsBuildingBlockedByVehicle() ? GetLang("CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_BOAT_FOUNDATION", player.UserIDString) : String.Empty;
                
                Boolean isAuthedFriend = playerBoat.children
                    .OfType<VehiclePrivilege>()
                    .Any(vehiclePrivilege => friendList.Any(vehiclePrivilege.IsAuthed));

                return !isAuthedFriend && !playerBoat.IsAuthedForBuilding(player) ? GetLang("CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_BOAT_FOUNDATION", player.UserIDString) : String.Empty; 
            }
            
            if (apartmentRoom)
            {
                if (!homeController.canSetupApartmentHome)
                    return GetLang("CAN_SETHOME_APARTMENT_DISABLE", player.UserIDString);

                if (!apartmentRoom.IsCurrentlyRented())
                    return GetLang("CAN_SETHOME_APARTMENT_NO_RENT", player.UserIDString);

                if (!IsApartmentAuthed(apartmentRoom, player))
                    return GetLang("CAN_SETHOME_APARTMENT_NO_AUTH", player.UserIDString);

                return String.Empty;
            }
            
            BuildingPrivlidge buildingPrivilage = player.GetBuildingPrivilege();
            
            if(homeController.onlyBuildingPrivilage && buildingPrivilage == null)
                return GetLang("CAN_SETHOME_BUILDING_PRIVILAGE", player.UserIDString);

            if (homeController.onlyBuildingAuth && buildingPrivilage != null && !player.IsBuildingAuthed())
                return GetLang("CAN_SETHOME_BUILDING_PRIVILAGE_AUTH", player.UserIDString);
            
            if (!buildingBlock)
            {
                buildingBlock = GetBuildingBlock(positionHome);
                if (!buildingBlock)
                    return GetLang("CAN_SETHOME_BUILDING", player.UserIDString);
            }
            
            return String.Empty;
        }
        
        #endregion

        #region Remove Home

        private void RemoveHome(BasePlayer player, String homeName)
        {
            homeName = homeName.ToLower();
            Dictionary<String, PlayerHome> pData = playerHomes[player.userID];
            if (!pData.TryGetValue(homeName, out PlayerHome home))
            {
                SendChat(GetLang("CAN_REMOVE_HOME_NO_EXISTS", player.UserIDString, homeName), player, true);
                return;
            }
            
            Interface.Call("OnHomeRemoved", player, home.positionHome, homeName);
            
            pData.Remove(homeName);

            RemoveHomeInMemory(player, homeName);
            
            SendChat(GetLang("ACCESS_REMOVE_HOME", player.UserIDString, homeName), player);
        }

        #endregion

        #region Check and clear home

        private void CheckAllHomes(BasePlayer player, String homeName = "")
        {
            if (!playerHomes.TryGetValue(player.userID, out Dictionary<String, PlayerHome> pHome)) return;

            if (!String.IsNullOrWhiteSpace(homeName))
            {
                if (!pHome.TryGetValue(homeName, out PlayerHome home)) return;
                
                if (!CheckHome(player, homeName, home))
                {
                    pHome.Remove(homeName);
                    RemoveHomeInMemory(player, homeName);
                }
                return;
            }
            
            List<String> homesToRemove = Pool.Get<List<String>>();

            foreach (KeyValuePair<String, PlayerHome> pData in pHome)
            {
                if (!CheckHome(player, pData.Key, pHome[pData.Key]))
                    homesToRemove.Add(pData.Key);
            }

            foreach (String home in homesToRemove)
                pHome.Remove(home);

            if (homesToRemove.Count != 0)
                RemoveHomeInMemory(player, homeName);
            
            Pool.FreeUnmanaged(ref homesToRemove);
        }

        private Boolean CheckHome(BasePlayer player, String homeName, PlayerHome pHome)
        {
            if (pHome.netIdEntity != 0)
            {
                if (pHome.typeEntity == TypeHomeEntity.Tugboat) 
                {
                    if (tugboatsServer.TryGetValue(pHome.netIdEntity, out Tugboat tugboat) && tugboat && !tugboat.IsDestroyed)
                    {
                        if (tugboat.IsAuthedForBuilding(player)) return true;
                        SendChat(GetLang("CAN_TELEPORT_HOME_NOT_TUGBOAT_NO_AUTH", player.UserIDString, homeName), player);
                        return false;
                    }

                    SendChat(GetLang("CAN_TELEPORT_HOME_NOT_TUGBOAT", player.UserIDString, homeName), player);
                    return false;
                }

                if (pHome.typeEntity == TypeHomeEntity.PlayerBoat)
                {
                    if (playersBoatsServer.TryGetValue(pHome.netIdEntity, out PlayerBoat playerBoat) && playerBoat && !playerBoat.IsDestroyed)
                    {
                        if (playerBoat.IsAuthedForBuilding(player)) return true;
                        SendChat(GetLang("CAN_TELEPORT_HOME_NOT_PLAYER_BOAT_NO_AUTH", player.UserIDString, homeName), player);
                        return false;
                    }

                    SendChat(GetLang("CAN_TELEPORT_HOME_NOT_PLAYER_BOAT", player.UserIDString, homeName), player);
                    return false;
                }
                
                if (pHome.typeEntity == TypeHomeEntity.Apartment)
                {
                    ApartmentRoom apartmentRoom = GetApartmentRoomByID(pHome.netIdEntity);
                    if (!IsApartmentValid(apartmentRoom))
                    {
                        SendChat(GetLang("CAN_TELEPORT_HOME_NOT_APARTMENT", player.UserIDString, homeName), player);
                        return false;
                    }

                    if (IsApartmentAuthed(apartmentRoom, player)) return true;

                    SendChat(GetLang("CAN_TELEPORT_HOME_NOT_APARTMENT_NO_AUTH", player.UserIDString, homeName), player);
                    return false;
                }
            }
    
            BuildingBlock buildingBlock = GetBuildingBlock(pHome.positionHome);
            if (!buildingBlock)
            {
                SendChat(GetLang("CAN_TELEPORT_HOME_NOT_BUILDING_BLOCK", player.UserIDString, homeName), player);
                return false;
            }

            if (!config.homeController.setupHomeController.onlyBuildingAuth) return true;
            BuildingPrivlidge privilege = buildingBlock.GetBuildingPrivilege();
            if (privilege == null || privilege.IsAuthed(player)) return true;
            SendChat(GetLang("CAN_TELEPORT_HOME_NOT_AUTH_BUILDING_PRIVILAGE", player.UserIDString, homeName), player);
            return false;
        }


        #endregion
        
        private BuildingBlock GetBuildingBlock(in Vector3 position)
        {
            Vector3 rayOrigin = position  + Vector3.up; 
            Vector3 rayDirection = Vector3.down;
            
            if (!Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, radiusCheckLayerHome, homelayerMaskBuilding))
            {
                LogToFile("DEBUG_REMOVE_HOME", $"NO BUILDING BLOCK DETECTED!!! {position}", this);
                return null;
            }
            BuildingBlock buildingBlock = hit.GetEntity() as BuildingBlock;
            
            // if (buildingBlock == null)
            //     return null;

            return !IsValidPosOtherEntity(position, buildingBlock) ? null : buildingBlock;
        }
        
        private Boolean IsValidPosOtherEntity(in Vector3 position, BuildingBlock buildingBlock) 
        {
            Vector3 rayOrigin = position + Vector3.up; 
            Vector3 rayDirection = Vector3.down;
    
            RaycastHit[] hits = new RaycastHit[3]; 
            Int32 numHits = Physics.SphereCastNonAlloc(rayOrigin, 0.3f, rayDirection, hits, radiusCheckLayerHome, homelayerMaskEntity);

            for (Int32 i = 0; i < numHits; i++)
            {
                BaseEntity entity = hits[i].GetEntity();

                if (entity && buildingBlock && entity != buildingBlock && !exceptionsBuildingBlockEntities.Contains(entity.ShortPrefabName))
                {
                    LogToFile("DEBUG_REMOVE_HOME", $"DELETE HOME DETECTED ENTITY ONE : {entity.ShortPrefabName}", this);
                    return false;
                }
            }
    
            Collider[] colliders = new Collider[3]; 
            Int32 numColliders = Physics.OverlapSphereNonAlloc(position, 0.1f, colliders, homelayerMaskEntity);
    
            for (Int32 i = 0; i < numColliders; i++)
            {
                BaseEntity entity = colliders[i].ToBaseEntity();
                if (!entity || !buildingBlock || entity == buildingBlock) continue;
                if (!entity.name.Contains("coffinstorage"))
                {
                    if(!exceptionsBuildingBlockEntities.Contains(entity.ShortPrefabName))
                    {
                        LogToFile("DEBUG_REMOVE_HOME",
                            $"DELETE HOME DETECTED ENTITY TWO : {entity.ShortPrefabName} : exceptionsBuildingBlockEntities.Contains(entity.ShortPrefabName) - {exceptionsBuildingBlockEntities.Contains(entity.ShortPrefabName)}",
                            this);
                    }
                    return exceptionsBuildingBlockEntities.Contains(entity.ShortPrefabName);
                }

                return false;
            }
        
            return true;
        }

        
        #endregion

        #region Auto Teleport Turn

        private void AutoTeleportTurn(BasePlayer player)
        {
            if (!playerSettings.TryGetValue(player.userID, out SettingPlayer setting)) return;
            setting.autoAcceptTeleportationFriends = !setting.autoAcceptTeleportationFriends;

            String message = setting.autoAcceptTeleportationFriends
                ? GetLang("AUTO_ACCEPT_TELEPORTATION_FRIEND_TRUE", player.UserIDString)
                : GetLang("AUTO_ACCEPT_TELEPORTATION_FRIEND_FALSE", player.UserIDString);
            
            SendChat(message, player);
        }

        #endregion

        #region GMap Teleport Turn

        private void GMapTeleportTurn(BasePlayer player)
        {
            if (!playerSettings.TryGetValue(player.userID, out SettingPlayer setting)) return;
            setting.useTeleportGMap = !setting.useTeleportGMap;

            String message = setting.useTeleportGMap
                ? GetLang("GMAP_TELEPORT_TRUE", player.UserIDString)
                : GetLang("GMAP_TELEPORT_FALSE", player.UserIDString);
            
            SendChat(message, player);
        }

        #endregion

        #endregion

        #region Interface
        
        #region ImageLoader

        private class ImageUI
        {
            private const String _path = "IQSystem/IQTeleportation/Images/";
            private const String _printPath = "data/" + _path;
            private readonly Dictionary<String, ImageData> _images = new()
            {
	             { "UI_IQTELEPORTATION_TEMPLATE", new ImageData() },
            };

            private enum ImageStatus
            {
                NotLoaded,
                Loaded,
                Failed
            }

            private class ImageData
            {
                public ImageStatus Status = ImageStatus.NotLoaded;
                public String Id { get; set; }
            }

            public String GetImage(String name)
            {
                if (_images.TryGetValue(name, out ImageData image) && image.Status == ImageStatus.Loaded)
                    return image.Id;
                return null;
            }

            public void DownloadImage()
            {
                KeyValuePair<String, ImageData>? image = null;
                foreach (KeyValuePair<String, ImageData> img in _images)
                {
                    if (img.Value.Status == ImageStatus.NotLoaded)
                    {
                        image = img;
                        break;
                    }
                }

                if (image != null)
                {
                    ServerMgr.Instance.StartCoroutine(ProcessDownloadImage(image.Value));
                }
                else
                {
                    List<String> failedImages = new List<String>();

                    foreach (KeyValuePair<String, ImageData> img in _images)
                    {
                        if (img.Value.Status == ImageStatus.Failed)
                        {
                            failedImages.Add(img.Key);
                        }
                    }

                    if (failedImages.Count > 0)
                    {
                        String images = String.Join(", ", failedImages);
                        _.PrintError(LanguageEn
                            ? $"Failed to load the following images: {images}. Perhaps you did not upload them to the '{_printPath}' folder. You can download it here - https://drive.google.com/drive/folders/1abiKOnLRx7m3YD7LHugPA7dSr2MCaMdi?usp=sharing" 
                            : $"Не удалось загрузить следующие изображения: {images}. Возможно, вы не загрузили их в папку '{_printPath}'. Скачать можно тут - https://drive.google.com/drive/folders/1abiKOnLRx7m3YD7LHugPA7dSr2MCaMdi?usp=sharing");
                        Interface.Oxide.UnloadPlugin(_.Name);
                    }
                    else
                    {
                        _.Puts(LanguageEn
                            ? $"{_images.Count} images downloaded successfully!"
                            : $"{_images.Count} изображений успешно загружено!");
                        
                        _interface = new InterfaceBuilder();
                        
                        foreach (BasePlayer player in BasePlayer.activePlayerList)
                            _.DrawUI_LastHomes_Map(player);
                    }
                }
            }
            
            public void UnloadImages()
            {
                foreach (KeyValuePair<String, ImageData> item in _images)
                    if(item.Value.Status == ImageStatus.Loaded)
                        if (item.Value?.Id != null)
                            FileStorage.server.Remove(uint.Parse(item.Value.Id), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);

                _images?.Clear();
            }

            private IEnumerator ProcessDownloadImage(KeyValuePair<String, ImageData> image)
            {
                String url = "file://" + Interface.Oxide.DataDirectory + "/" + _path + image.Key + ".png";

                using UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
                yield return www.SendWebRequest();

                if (www.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
                {
                    image.Value.Status = ImageStatus.Failed;
                }
                else
                {
                    Texture2D tex = DownloadHandlerTexture.GetContent(www);
                    image.Value.Id = FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString();
                    image.Value.Status = ImageStatus.Loaded;
                    UnityEngine.Object.DestroyImmediate(tex);
                }

                DownloadImage();
            }
        }

        #endregion

        #region CasheUI

        private List<String> GetOrSetCacheUI(BasePlayer player, String additionalTitile, String interfaceJson = null)
        {
            String langKeyPlayer = lang.GetLanguage(player.UserIDString);
            String keyCache = $"{additionalTitile}_{langKeyPlayer}";

            if (cachedUI.TryGetValue(keyCache, out List<String> ui))
                return ui;

            if (interfaceJson == null) return null;

            List<String> newUI = new List<String> { interfaceJson };
            cachedUI[keyCache] = newUI;
            return newUI;
        }

        #endregion
        
        #region DrawUI
        private static void DestroyUI(BasePlayer player, String elementName)
        {
            if (!player || player.net?.connection == null) return;
            CommunityEntity.ServerInstance.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), elementName);
        }
        private static void AddUI(BasePlayer player, String json)
        {
            if (!player || player.net?.connection == null) return;
            CommunityEntity.ServerInstance.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
        }
       
        private String CorrectedNick(String nickName)
        {
            nickName = nickName.Replace("\"", "'");
            return nickName.Length > 7 ? nickName.Substring(0, 7).ToUpper() + "..." : nickName.ToUpper();
        }
        
        private void DrawUI_TeleportPlayer(BasePlayer player, BasePlayer targetPlayer) 
        {
            if (_interface == null || !player || !targetPlayer) return;
            
            targetPlayer.Invoke(() =>
            {
                DestroyUI(targetPlayer, InterfaceBuilder.IQ_TELEPORT_UI_TELEPORT);
            }, 15f);
            
            String displayName = CorrectedNick(player.displayName);
            String interfaceKey = $"{InterfaceBuilder.IQ_TELEPORT_UI_TELEPORT}_{displayName}";
            List<String> cache = GetOrSetCacheUI(targetPlayer, interfaceKey);
            if (cache != null && cache.Count != 0)
            {
                foreach (String uiCached in cache)
                    AddUI(targetPlayer, uiCached);

                return;
            }
            
            String Interface = InterfaceBuilder.GetInterface(InterfaceBuilder.IQ_TELEPORT_UI_TELEPORT);
            if (Interface == null) return;

            String titleRequest = GetLang("UI_TPR_REQUSET", targetPlayer.UserIDString, displayName.ToUpper());

            Interface = Interface.Replace("%TITILE_PANEL%", titleRequest);
            Interface = Interface.Replace("%COMMAND_YES%", "ui_teleportation_command tpa"); 
            Interface = Interface.Replace("%COMMAND_NO%", "ui_teleportation_command tpc"); 

            List<String> newUI = GetOrSetCacheUI(targetPlayer, interfaceKey, Interface);
            cache = newUI;
            
            foreach (String uiCached in cache)
                AddUI(targetPlayer, uiCached);
        }
        
        private void DrawUI_HomeSetup(BasePlayer player)
        {
            if (!config.homeController.suggetionSetHomeAfterInstallBed) return;
            if (_interface == null || player == null) return;
            
            player.Invoke(() =>
            {
                DestroyUI(player, InterfaceBuilder.IQ_TELEPORT_UI_HOME);
                
                if (bagInstalled.ContainsKey(player))
                    bagInstalled.Remove(player);
            }, 10f);
            
            String interfaceKey = InterfaceBuilder.IQ_TELEPORT_UI_HOME;
            List<String> cache = GetOrSetCacheUI(player, interfaceKey);
            if (cache != null && cache.Count != 0)
            {
                foreach (String uiCached in cache)
                    AddUI(player, uiCached);
                
                return;
            }
            
            String Interface = InterfaceBuilder.GetInterface(interfaceKey);
            if (Interface == null) return;
            
            Interface = Interface.Replace("%TITILE_PANEL%", GetLang("UI_SETUP_HOME_ALERT", player.UserIDString));
            Interface = Interface.Replace("%COMMAND_YES%", "ui_teleportation_command sethome"); 
            Interface = Interface.Replace("%COMMAND_NO%", "ui_teleportation_command cancell.sethome"); 

            List<String> newUI = GetOrSetCacheUI(player, interfaceKey, Interface);
            cache = newUI;
            
            foreach (String uiCached in cache)
                AddUI(player, uiCached);
        }
        
        
        private void DrawUI_LastHomes_Map(BasePlayer player)
        {
            if (!config.homeController.uiMapHomeList) return;
            if (_interface == null || !player) return;
   
            String interfaceKey = InterfaceBuilder.IQ_TELEPORT_UI_HOME_MAP;
            List<String> cache = GetOrSetCacheUI(player, interfaceKey);
            if (cache != null && cache.Count != 0)
            {
                foreach (String uiCached in cache)
                    AddUI(player, uiCached);
                
                LoadedMapButtons(player);
                return;
            }
            
            String Interface = InterfaceBuilder.GetInterface(interfaceKey);
            if (Interface == null) return;

            Int32 countHomes = 0;
            if (playerLastHomes.TryGetValue(player.userID, out List<String> listHomes))
                countHomes = listHomes.Count;
            
            List<String> newUI = GetOrSetCacheUI(player, interfaceKey, Interface);
            cache = newUI;
            
            foreach (String uiCached in cache)
                AddUI(player, uiCached);

            LoadedMapButtons(player);
        }

        private void LoadedMapButtons(BasePlayer player)
        {
            if (!config.homeController.uiMapHomeList) return;
            if (!playerLastHomes.ContainsKey(player.userID)) return;
            
            Int32 yIndex = 0;
            foreach (String keyHomes in playerLastHomes[player.userID])
            {
                if(!playerHomes[player.userID].TryGetValue(keyHomes, out PlayerHome homeInfo)) continue;
                
                String cordMap = MapHelper.PositionToString(homeInfo.positionHome);
                DrawUI_Map_Button(player, keyHomes, cordMap, colorHomeDefault, yIndex);
                
                yIndex++;
                                
                if (yIndex == limitMemoryLastHomes)
                    break;
            }
        }
        
        private void DrawUI_Map_Button(BasePlayer player, String homeName, String quadHome, String colorHome, Int32 yIndex)
        {
            if (!config.homeController.uiMapHomeList) return;
            String Interface = InterfaceBuilder.GetInterface("BUTTON_HOME_TEMPLATE");
            if (Interface == null) return;
            
            Interface = Interface.Replace("%OFFSET_MIN%", $"-16.5 {-49.3 - (yIndex * 37.8)}");
            Interface = Interface.Replace("%OFFSET_MAX%", $"16.5 {-16.3 - (yIndex * 37.8)}");
            Interface = Interface.Replace("%HOME_NAME%", homeName);
            Interface = Interface.Replace("%COLOR_BUTTON%", colorHome);
            Interface = Interface.Replace("%QUAD_HOME%", quadHome);

            AddUI(player, Interface);
        }
        
        #endregion
        
        private class InterfaceBuilder
        {
            #region Vars

            public static InterfaceBuilder Instance;
            public const String IQ_TELEPORT_UI_HOME_MAP = "IQ_TELEPORT_UI_HOME_MAP";
            public const String IQ_TELEPORT_UI_HOME = "IQ_TELEPORT_UI_HOME";
            public const String IQ_TELEPORT_UI_TELEPORT = "IQ_TELEPORT_UI_TELEPORT";
            public Dictionary<String, String> Interfaces;

            #endregion

            public InterfaceBuilder()
            {
                Instance = this;
                Interfaces = new Dictionary<String, String>();
                
                if (config.teleportationController.teleportSetting.suggetionAcceptedUI)
                    Building_PanelButton(IQ_TELEPORT_UI_TELEPORT);
                
                if (config.homeController.suggetionSetHomeAfterInstallBed)
                    Building_PanelButton(IQ_TELEPORT_UI_HOME);

                if (config.homeController.uiMapHomeList)
                {
                    Building_Map_Static();
                    Building_Map_HomeButton();
                }
            }

            public static void AddInterface(String name, String json)
            {
                if (Instance.Interfaces.ContainsKey(name))
                {
                    _.PrintError($"Error! Tried to add existing cui elements! -> {name}");
                    return;
                }

                Instance.Interfaces.Add(name, json);
            }

            public static String GetInterface(String name)
            {
                String json = String.Empty;
                if (Instance.Interfaces.TryGetValue(name, out json) == false)
                {
                    _.PrintWarning($"Warning! UI elements not found by name! -> {name}");
                }

                return json;
            }

            public static void DestroyAll()
            {
                for (Int32 i = 0; i < BasePlayer.activePlayerList.Count; i++)
                {
                    BasePlayer player = BasePlayer.activePlayerList[i];
                    DestroyUI(player, IQ_TELEPORT_UI_HOME);
                    DestroyUI(player, IQ_TELEPORT_UI_TELEPORT);
                    DestroyUI(player, IQ_TELEPORT_UI_HOME_MAP);
                }
            }
            
            private void Building_Map_Static()
            {
                CuiElementContainer container = new CuiElementContainer();
          
                container.Add(new CuiElement
                {
                    DestroyUi = IQ_TELEPORT_UI_HOME_MAP,
                    Name = IQ_TELEPORT_UI_HOME_MAP,
                    Parent = "Map",
                    Components = {
                        new CuiImageComponent { Color = "0.4509804 0.5568628 0.2745098 1", Sprite = "assets/content/ui/map/icon-map_circle-outline.png", Material = "assets/icons/greyout.mat" },
                        new CuiRectTransformComponent { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-105.2 188", OffsetMax = "-62.2 231" }
                    }
                });

                container.Add(new CuiElement
                {
                    DestroyUi = "Home_Icon",
                    Name = "Home_Icon",
                    Parent = IQ_TELEPORT_UI_HOME_MAP,
                    Components = {
                        new CuiRawImageComponent { Color = "0.6117647 0.8117648 0.2588235 1", Sprite = "assets/content/ui/map/icon-map_home.png" },
                        new CuiRectTransformComponent{ AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-25 -25", OffsetMax = "25 25" }
                    }
                });
                
                AddInterface(IQ_TELEPORT_UI_HOME_MAP, container.ToJson());
            }

            private void Building_Map_HomeButton()
            {
                CuiElementContainer container = new CuiElementContainer();
                
                container.Add(new CuiElement
                {
                    Parent = IQ_TELEPORT_UI_HOME_MAP,
                    DestroyUi = "HOME_NUMBER_%HOME_NAME%",
                    Name = "HOME_NUMBER_%HOME_NAME%",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Command = "gmap.home.request %HOME_NAME%",
                            Color = "%COLOR_BUTTON%", Sprite = "assets/content/ui/map/icon-map_circle-outline.png", Material = "assets/icons/greyout.mat"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "%OFFSET_MIN%", OffsetMax = "%OFFSET_MAX%"
                        }
                    }
                });
              
                container.Add(new CuiElement
                {
                    Parent = "HOME_NUMBER_%HOME_NAME%",
                    DestroyUi = "HOME_QUAD_%HOME_NAME%",
                    Name = "HOME_QUAD_%HOME_NAME%",
                    Components =
                    {
                        new CuiTextComponent { Text = "%QUAD_HOME%", FontSize = 10, Color = "1 1 1 0.5", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-16.5 -16.5", OffsetMax = "16.5 16.5" }
                    }
                });
                
                AddInterface("BUTTON_HOME_TEMPLATE", container.ToJson());
            }
          
            private void Building_PanelButton(String templateParent)
            {
                CuiElementContainer container = new CuiElementContainer();
                
                container.Add(new CuiElement
                {
                    FadeOut = uiAlertFade,
                    DestroyUi = templateParent,
                    Name = templateParent,
                    Parent = "Overlay",
                    Components = {
                        new CuiRawImageComponent { FadeIn = uiAlertFade, Color = "1 1 1 1", Png = _imageUI.GetImage("UI_IQTELEPORTATION_TEMPLATE") },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-199.867 80.133", OffsetMax = "181.467 104.8" }
                    }
                });

                container.Add(new CuiElement
                {
                    FadeOut = uiAlertFade / 2f,
                    DestroyUi = "Title_Panel",
                    Name = "Title_Panel",
                    Parent = templateParent,
                    Components = {
                        new CuiTextComponent { FadeIn = uiAlertFade, Text = "%TITILE_PANEL%", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8941177 0.854902 0.8196079 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "23.502 1.1", OffsetMax = "-23.589 -1.135" }
                    }
                });

                container.Add(new CuiButton
                {
                    FadeOut = uiAlertFade,
                    Button = { FadeIn = uiAlertFade, Color = "0 0 0 0", Close = templateParent, Command = "%COMMAND_YES%" },
                    Text = { FadeIn = uiAlertFade, Text = "", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "1.266 -11.234", OffsetMax = "23.502 11.198" }
                },templateParent,"Button_Setup", "Button_Setup");

                container.Add(new CuiButton
                {
                    FadeOut = uiAlertFade,
                    Button = { FadeIn = uiAlertFade, Color = "0 0 0 0", Close = templateParent, Command = "%COMMAND_NO%"},
                    Text = { FadeIn = uiAlertFade, Text = "", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-23.589 -11.234", OffsetMax = "-1.028 11.198" }
                },templateParent,"Button_Setup_No", "Button_Setup_No");
                
                AddInterface(templateParent, container.ToJson());
            }

            public void UpdateMapButtons(BasePlayer player)
            {
                if (!config.homeController.uiMapHomeList) return;
                if (!_.playerHomes.TryGetValue(player.userID, out Dictionary<String, PlayerHome> pHomes)) return;
                if (!_.localUsersRepository.TryGetValue(player, out UserRepository repository)) return;
                if (!_.playerLastHomes.TryGetValue(player.userID, out List<String> listHomes)) return;

                Boolean isActiveTeleportation = repository.IsActiveTeleportation();

                CuiElementContainer container = new CuiElementContainer();
                Int32 cooldownPlayer = repository.GetCooldownHome(player);
                Boolean isCooldownHome = cooldownPlayer > 0;
                
                if(isCooldownHome)
                    player.Invoke(() => UpdateMapButtons(player), cooldownPlayer);
                
                Int32 index = 0;
                foreach (String homeName in listHomes)
                {
                    if(!pHomes.TryGetValue(homeName, out PlayerHome home)) continue;
                    
                    String cordMap = MapHelper.PositionToString(home.positionHome);
                    String colorButton = colorHomeDefault;
                    String commandButton = $"gmap.home.request {homeName}";
                    if (isCooldownHome)
                    {
                        commandButton = String.Empty;
                        colorButton = colorHomeCooldown;
                    }
                    else if (index == 0 && isActiveTeleportation)
                    {
                        commandButton = "tpc";
                        colorButton = colorHomeRequestTime;
                    }
                    
                    container.Add(new CuiElement
                    {
                        Parent = IQ_TELEPORT_UI_HOME_MAP,
                        Name = $"HOME_NUMBER_{homeName}",
                        Update = true,
                        Components =
                        {
                            new CuiButtonComponent {  Color = colorButton, Command = commandButton },
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = $"HOME_NUMBER_{homeName}",
                        Name = $"HOME_QUAD_{homeName}",
                        Update = true,
                        Components =
                        {
                            new CuiTextComponent { Text = cordMap },
                        }
                    });
                    index++;
                }
                
                AddUI(player, container.ToJson());
            }
        }
        
        #endregion
        
        #region Lang

        private static StringBuilder sb = new StringBuilder();
        public String GetLang(String LangKey, String userID = null, params Object[] args)
        {
            sb.Clear();
            if (args != null)
            {
                sb.AppendFormat(lang.GetMessage(LangKey, this, userID), args);
                return sb.ToString();
            }
            return lang.GetMessage(LangKey, this, userID);
        }

        private new void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<String, String>
            {
                ["UI_SETUP_HOME_ALERT"] = "DO YOU WANT TO SET A HOME POINT HERE?",
                ["UI_TPR_REQUSET"] = "TELEPORT REQUEST FROM PLAYER {0}",

                ["SYNTAX_COMMAND_SETHOME"] = ":exclamation:To set a home point, use: <color=#1F6BA0>/sethome HomeName</color>",
                ["SYNTAX_COMMAND_REMOVEHOME"] = ":exclamation:To remove a home point, use: <color=#1F6BA0>/removehome HomeName</color>",
                ["SYNTAX_COMMAND_HOME_TELEPORTATION"] = ":exclamation:To teleport to a home point, use: <color=#1F6BA0>/home HomeName</color>",
                ["SYNTAX_COMMAND_TELEPORT_REQUEST"] = ":exclamation:To request a teleport to a player, use: <color=#1F6BA0>/tpr PlayerName</color>",
                ["SYNTAX_COMMAND_TELEPORT"] = ":exclamation:To teleport to a player, use: <color=#1F6BA0>/tp PlayerName</color>\nTo teleport one player to another, use: <color=#1F6BA0>/tp TargetPlayer PlayerToTeleport</color>",

                ["SYNTAX_COMMAND_WARPS"] = ":exclamation:Use the syntax:" +
                                           "\n<color=#1F6BA0>/warp list</color> - to show all available warps" +
                                           "\n<color=#1F6BA0>/warp points WarpName</color> - to show all teleport points for this warp" +
                                           "\n<color=#1F6BA0>/warp add WarpName</color> - to create a new warp or add an extra teleport point to it" +
                                           "\n<color=#1F6BA0>/warp edit WarpName PositionNumber</color> - to edit a teleport position for a warp" +
                                           "\n<color=#1F6BA0>/warp remove WarpName</color> - to remove a warp and all its teleport points" +
                                           "\n<color=#1F6BA0>/warp remove WarpName PositionNumber</color> - to remove a teleport position from the specified warp",

                ["HOMELIST_COMMAND_EMPTY"] = ":exclamation:You have no home points set",
                ["HOMELIST_COMMAND_ALL_LIST"] = ":yellowpin:Your set home points: {0}",
                ["HOMELIST_COMMAND_FORTMAT_HOME"] = "\n{0} in grid [{1}]",

                ["MONUMENT_BLOCKED_SETUP_NOT_CENTER"] = ":yellowpin:Unable to detect the monument. Move closer to the center of this monument",
                ["MONUMENT_BLOCKED_SETU_BLOCKED"] = ":exclamation:This monument is already blocked for teleportation",
                ["MONUMENT_BLOCKED_SETUP_ACCES"] = ":exclamation:You have successfully blocked teleportation from this monument",

                ["COOLDOWN_MESSAGE"] = ":exclamation:You need to wait <color=#C26D33>{0}</color> before performing this action",

                ["TITLE_FORMAT_DAYS"] = "D",
                ["TITLE_FORMAT_HOURSE"] = "H",
                ["TITLE_FORMAT_MINUTES"] = "M",
                ["TITLE_FORMAT_SECONDS"] = "S",

                ["WARP_NOTHING"] = ":exclamation:You haven't created any warps yet",
                ["WARP_NO_PERMISSIONS"] = ":exclamation:You don't have permission to teleport to a warp",
                ["WARP_TELEPORTATION_PLAYER"] = ":yellowpin:You have been teleported to the warp",
                ["WARP_TELEPORTATION_PLAYER_HOSTILE"] = ":exclamation:You cannot be teleported to a warp while you are hostile",
                ["WARP_REQUEST_TELEPORTATION_PLAYER"] = ":yellowpin:You will be teleported to the warp in <color=#1F6BA0>{0} seconds</color>",
                ["WARP_NOTHING_NAME"] = ":exclamation:A warp with that name does not exist",
                ["WARP_NOT_ARG_NAME"] = ":exclamation:You must specify a warp name",
                ["WARP_SHOW_POINTS"] = ":yellowpin:Teleport points for warp <color=#738D45>{0}</color> will be shown for <color=#1F6BA0>30 seconds</color>",
                ["WARP_SETUP_POINTS_DISTANCE"] = ":yellowpin:The new point is too close to point <color=#738D45>#{0}</color> of this warp. Move farther away and try again",
                ["WARP_SETUP_POINT_ACCESS"] = ":yellowpin:You have successfully added a new position for the warp <color=#738D45>{0}</color>",
                ["WARP_SETUP_NEW"] = ":exclamation:Warp <color=#738D45>{0}</color> created successfully. Teleport command reserved: <color=#738D45>{1}</color>",
                ["WARP_EDIT_POINT_ACCESS"] = ":yellowpin:You have successfully changed the position for warp <color=#738D45>{0}</color>",
                ["WARP_NOTHING_KEY_POINTS"] = ":yellowpin:You must specify the point key (#) to edit it\nAvailable keys: {0}",
                ["WARP_NOTHING_KEY_IS_NUMBER"] = ":yellowpin:The specified key (#) is not a number. You must enter digits only",
                ["WARP_REMOVED"] = ":exclamation:Warp removed successfully.\nTeleport command <color=#C26D33>{0}</color> has been removed",
                ["WARP_REMOVED_POINT"] = ":yellowpin:Teleport point <color=#C26D33>#{0}</color> removed for warp <color=#1F6BA0>{1}</color>",
                ["WARP_REMOVED_POINT_NOTHING_KEYS"] = ":yellowpin:Point <color=#C26D33>#{0}</color> does not exist for warp <color=#1F6BA0>{1}</color>\nAvailable keys: {2}",

                ["AUTO_ACCEPT_TELEPORTATION_FRIEND_TRUE"] = ":heart:You have <color=#738D45>enabled</color> auto-accept for teleports from friends",
                ["AUTO_ACCEPT_TELEPORTATION_FRIEND_FALSE"] = ":heart:You have <color=#C26D33>disabled</color> auto-accept for teleports from friends",

                ["GMAP_TELEPORT_TRUE"] = ":heart:You have <color=#738D45>enabled</color> teleportation via GMap",
                ["GMAP_TELEPORT_FALSE"] = ":heart:You have <color=#C26D33>disabled</color> teleportation via GMap",

                ["ACCESS_SETUP_SETHOME"] = ":yellowpin:Home point <color=#738D45>{0}</color> has been set successfully",
                ["ACCESS_TELEPORTATION_HOME"] = ":yellowpin:Teleporting to home point <color=#738D45>{0}</color>\nPlease wait: <color=#1F6BA0>{1}</color>",
                ["ACCESS_TELEPORTATION_HOME_FRIEND_NICK"] = ":yellowpin:Teleporting to your friend's home point <color=#738D45>{0}</color> named <color=#738D45>{1}</color>\nPlease wait: <color=#1F6BA0>{2}</color>",
                ["ACCESS_TELEPORTATION_HOME_FRIEND"] = ":yellowpin:Teleporting to your friend's home point <color=#738D45>{0}</color>\nPlease wait: <color=#1F6BA0>{1}</color>",

                ["CAN_SETHOME_BUILDING"] = ":exclamation:You can set a home point <color=#C26D33>only on a building</color>",
                ["CAN_SETHOME_OTHER_ENTITY"] = ":exclamation:There is <color=#C26D33>another object</color> under or near the point that <color=#C26D33>prevents</color> setting a home point",
                ["CAN_SETHOME_BUILDING_OR_TERRAIN"] = ":exclamation:You cannot set a home point on <color=#C26D33>rocks or other objects</color>. Stand on a <color=#C26D33>building piece or on the ground</color>",
                ["CAN_SETHOME_BUILDING_PRIVILAGE"] = ":exclamation:To set a home point, you must <color=#C26D33>place a tool cupboard</color> in the building",
                ["CAN_SETHOME_BUILDING_PRIVILAGE_AUTH"] = ":exclamation:To set a home point, you must <color=#C26D33>authorize in the tool cupboard</color>",
                ["CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_TUGBOAT"] = ":exclamation:To set a home point, you must <color=#C26D33>authorize on the tugboat</color>",
                ["CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_BOAT_FOUNDATION"] = ":exclamation:To set a home point, you must <color=#C26D33>authorize at the helm</color>",
                ["CAN_SETHOME_TUGBOAT_DISABLE"] = ":exclamation:You cannot set a home point on tugboats; this <color=#C26D33>feature is disabled</color>",
                ["CAN_SETHOME_BOAT_FOUNDATION_DISABLE"] = ":exclamation:You cannot set a home point on homemade boats; this <color=#C26D33>feature is disabled</color>",
                ["CAN_SETHOME_IS_RAIDBLOCK"] = ":exclamation:To set a home point, you must not have an <color=#C26D33>active raid block</color>",
                ["CAN_SETHOME_OTHER_MESSAGE"] = ":exclamation:You cannot set a home point right now",
                ["CAN_SETHOME_EXIST_HOMENAME"] = ":exclamation:A home point named <color=#738D45>{0}</color> already exists",
                ["CAN_SETHOME_DISTANCE_EXIST_HOME"] = ":exclamation:You cannot set a home point; a point <color=#738D45>{0}</color> is already set nearby",
                ["CAN_SETHOME_MAXIMUM_HOMES"] = ":exclamation:You cannot set a home point; the <color=#C26D33>maximum limit</color> of home points has been reached",

                ["CAN_SETHOME_APARTMENT_DISABLE"] = ":exclamation:You cannot set a home point in apartments; this <color=#C26D33>feature is disabled</color>",
                ["CAN_SETHOME_APARTMENT_NO_RENT"] = ":exclamation:To set a home point, the apartment must be <color=#C26D33>rented</color>",
                ["CAN_SETHOME_APARTMENT_NO_AUTH"] = ":exclamation:To set a home point, you must be the <color=#C26D33>tenant of this apartment or their teammate</color>",
                ["CAN_TELEPORT_HOME_NOT_APARTMENT"] = ":exclamation:Home point <color=#738D45>{0}</color> has been removed - the apartment is <color=#C26D33>no longer rented</color>",
                ["CAN_TELEPORT_HOME_NOT_APARTMENT_NO_AUTH"] = ":exclamation:You <color=#C26D33>no longer have access</color> to the apartment of home point <color=#738D45>{0}</color>",
                ["CAN_TELEPORT_HOME_APARTMENT_LOST"] = ":exclamation:Teleport cancelled - you <color=#C26D33>lost access</color> to the apartment",
                
                ["A_HOME_CHECK_PLAYER_NOT_FOUND"] = ":exclamation:Player not found",
                ["A_HOME_CHECK_NOT_HOME"] = ":exclamation:The player has no available home points",
                ["A_HOME_CLEAR_HOMES"] = ":exclamation:All of the player's home points have been removed",
                ["A_HOME_SHOW_POINTS"] = ":yellowpin:Home points of player {0} will be shown for <color=#738D45>30 seconds</color>",

                ["CAN_REMOVE_HOME_NO_EXISTS"] = ":exclamation:You do <color=#C26D33>not have</color> a home point named <color=#738D45>{0}</color>",
                ["ACCESS_REMOVE_HOME"] = ":exclamation:You have <color=#C26D33>removed</color> the home point named <color=#738D45>{0}</color>",

                ["ACCESS_CANCELL_TELEPORTATION_PLAYER"] = ":exclamation:You have <color=#1F6BA0>canceled</color> your previous request to teleport home",
                ["ACCESS_CANCELL_TELEPORTATION"] = ":exclamation:Teleportation has been <color=#1F6BA0>canceled</color>",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD"] = "\nYou were killed",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_TARGET"] = "\nThe player you were teleporting to was <color=#1F6BA0>killed</color>",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_TARGET_DISCONNECTED"] = "\nThe player you were teleporting to <color=#1F6BA0>left</color> the server",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_REQUESTER_DISCONNECTED"] = "\nThe player who was teleporting to you <color=#1F6BA0>left</color> the server",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_REQUESTER_DEAD"] = "\nThe player who was teleporting to you was <color=#1F6BA0>killed</color>",

                ["CAN_CANCELL_TELEPORTATION_NOT_FOUNDS"] = ":exclamation:You have no active teleportations",

                ["REQUEST_TELEPORTATION_ACCESS"] = ":heart:Your teleport request has been accepted - please wait <color=#1F6BA0>{0}</color>\n<color=#C26D33>To cancel the request, use /tpc</color>",

                ["CAN_ACCEPT_TELEPORTATION_NOT_AUTO_ACCEPT"] = ":heart:Your request has been <color=#1F6BA0>sent</color>, but your friend <color=#C26D33>cannot</color> accept it right now",
                ["CAN_ACCEPT_TELEPORTATION_YES_AUTO_ACCEPT"] = ":heart:Your request has been <color=#1F6BA0>automatically accepted</color> by your friend",

                ["CAN_ACCEPT_TELEPORTATION_NOT_REQUEST"] = ":exclamation:You have no <color=#1F6BA0>active teleport requests</color>",
                ["CAN_ACCEPT_TELEPORTATION_ACTIVE_ACCEPTED"] = ":exclamation:You have already <color=#1F6BA0>accepted the request</color> - please wait for the player to teleport!\n<color=#C26D33>To cancel the request, use /tpc</color>",

                ["CAN_REQUEST_TELEPORTATION_DISABLED"] = ":heart:Teleportation to players is disabled on this server",
                ["CAN_REQUEST_TELEPORTATION_NOT_FOUND_PLAYER"] = ":exclamation:That player is not online right now",
                ["CAN_REQUEST_TELEPORTATION_ACCESS"] = ":heart:You have <color=#738D45>accepted</color> a teleport request from <color=#1F6BA0>{0}</color>\nTo <color=#C26D33>cancel</color> the request, use <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_ACCESS_AUTO_TELEPORT"] = ":heart:You have <color=#1F6BA0>automatically</color> accepted a teleport request from your friend <color=#1F6BA0>{0}</color>\nTo <color=#C26D33>cancel</color> the request, use <color=#C26D33>/tpc</color>\nTo <color=#C26D33>disable</color> auto-accept for teleport requests from friends, type <color=#C26D33>/atp</color>",
                ["CAN_REQUEST_TELEPORTATION_SEND"] = ":heart:You have <color=#738D45>sent</color> a teleport request to <color=#1F6BA0>{0}</color>\nTo <color=#C26D33>cancel</color> the request, use <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_RECEIVE"] = ":heart:Teleport request from <color=#1F6BA0>{0}</color>\nTo <color=#738D45>accept</color>, use <color=#738D45>/tpa</color>\nTo <color=#C26D33>cancel</color>, use <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY"] = ":exclamation:You already have an <color=#C26D33>active request</color> with a player!\n<color=#C26D33>To cancel, use /tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY_NAME"] = ":exclamation:You already have an <color=#C26D33>active request</color> with player <color=#1F6BA0>{0}</color>\nTo <color=#C26D33>cancel</color>, use <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_NOTHING_TELEPORT_ME"] = ":exclamation:You can't teleport to yourself",
                ["CAN_REQUEST_TELEPORTATION_ALREADY_REQUEST_TARGET"] = ":exclamation:The target player already has an <color=#C26D33>active request</color>\nWait until they accept or cancel their request",
                ["CAN_REQUEST_TELEPORTATION_ONLY_REQUEST_FRIENDS"] = ":exclamation:You can send requests <color=#C26D33>only to friends</color>",
                ["CAN_REQUEST_PLAYER_NOT_MOUNT_POINT"] = ":exclamation:You cannot teleport to the player: <color=#C26D33>no free seats</color> in their vehicle",
                ["CAN_REQUEST_PLAYER_MOUNT_ERROR"] = ":exclamation:The player is in a vehicle; you cannot teleport to them right now",

                ["FIND_PLAYER_INFO_MATCHES"] = ":exclamation:Your search for <color=#1F6BA0>'{0}'</color> found multiple players: <color=#1F6BA0>{1}</color>",

                ["CAN_TELEPORT_PLAYER_NOT_FOUND_TP_ONE_PLAYER"] = ":exclamation:The player you are trying to teleport was not found",
                ["CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER_ONE"] = ":exclamation:The player you are trying to teleport <color=#C26D33>{0}</color> to was not found",
                ["CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER"] = ":exclamation:The player you are trying to teleport to was not found",
                ["CAN_TELEPORT_PLAYER_ACCES_TP"] = ":yellowpin:You have been teleported to <color=#C26D33>{0}</color>",
                ["CAN_TELEPORT_PLAYER_NOT_SETTING_USE_FRIEND_HOME"] = ":exclamation:Your friend has <color=#C26D33>disabled</color> teleportation to their home points",
                ["CAN_TELEPORT_PLAYER_NOT_USE_FRIEND_HOME"] = ":exclamation:Teleportation to a friend's home point is <color=#C26D33>disabled</color>",
                ["CAN_TELEPORT_NULL_REASON"] = ":exclamation:You cannot teleport right now",
                ["CAN_TELEPORT_RAID_BLOCK"] = ":exclamation:You cannot teleport during a <color=#C26D33>raid block</color>",
                ["CAN_TELEPORT_TITILE_OTHER"] = ":exclamation:You cannot teleport while you are: <color=#C26D33>{0}</color>",
                ["CAN_TELEPORT_IN_BUILDING_BLOCKED"] = "in someone else's building zone",
                ["CAN_TELEPORT_IN_CARGO_SHIP"] = "on the cargo ship",
                ["CAN_TELEPORT_IN_HOT_AIR_BALLOON"] = "in a hot air balloon",
                ["CAN_TELEPORT_MONUMENT_BLOCKED"] = ":exclamation:Teleportation from this monument is blocked!\n<color=#C26D33>Leave the monument or move farther away!</color>",
                ["CAN_TELEPORT_COOLDOWN"] = ":exclamation:You cannot teleport right now; cooldown: <color=#C26D33>{0}</color>",
                ["CAN_TELEPORT_REQUEST_EXPIRED"] = ":exclamation:Your <color=#C26D33>request</color> has <color=#C26D33>expired</color> and was canceled",
                ["CAN_TELEPORT_ONLY_FRIEND"] = ":exclamation:You can teleport only to <color=#C26D33>friends</color>\nTeleportation was canceled",
                ["CAN_TELEPORT_ONLY_FRIEND_TARGET"] = ":exclamation:You can teleport to yourself only <color=#C26D33>friends</color>\nTeleportation was canceled",
                ["CAN_TELEPORT_PLAYER_NOT_MOUNT_POINT"] = ":exclamation:You cannot accept the teleport: <color=#C26D33>no free seats</color> in the vehicle",
                ["CAN_TELEPORT_PLAYER_MOUNT_ERROR"] = ":exclamation:You are in a vehicle; you cannot accept teleportation right now",

                ["CAN_TELEPORT_HOME_FOUNDATION_DESTROYED"] = ":exclamation:The foundation where the home point was placed has been destroyed; you cannot teleport",
                ["CAN_TELEPORT_HOME_TUGBOAT_DESTROYED"] = ":exclamation:The tugboat where the home point was placed has been destroyed; you cannot teleport",
                ["CAN_TELEPORT_HOME_PLAYER_BOAT_DESTROYED"] = ":exclamation:The boat where the home point was placed has been destroyed; you cannot teleport",
                ["CAN_TELEPORT_HOME_NOT_PLAYER_BOAT"] = ":exclamation:The boat where the home point <color=#1F6BA0>{0}</color> was placed has been <color=#C26D33>destroyed</color>",
                ["CAN_TELEPORT_HOME_NOT_PLAYER_BOAT_NO_AUTH"] = ":exclamation:You are not authorized on the boat where the home point <color=#1F6BA0>{0}</color> is set; it has been <color=#C26D33>destroyed</color>",
                ["CAN_TELEPORT_HOME_NOT_TUGBOAT"] = ":exclamation:The tugboat where the home point <color=#1F6BA0>{0}</color> was placed has been <color=#C26D33>destroyed</color>",
                ["CAN_TELEPORT_HOME_NOT_TUGBOAT_NO_AUTH"] = ":exclamation:You are not authorized on the tugboat where the home point <color=#1F6BA0>{0}</color> is set; it has been <color=#C26D33>destroyed</color>",
                ["CAN_TELEPORT_HOME_NOT_HOME"] = ":exclamation:A home point named <color=#1F6BA0>{0}</color> does not exist",
                ["CAN_TELEPORT_HOME_NOT_BUILDING_BLOCK"] = ":exclamation:The home point <color=#1F6BA0>{0}</color> is blocked by building or has no surface where it was placed\n<color=#C26D33>The home point has been removed</color>",
                ["CAN_TELEPORT_HOME_NOT_AUTH_BUILDING_PRIVILAGE"] = ":exclamation:You are not authorized in the building where the home point <color=#1F6BA0>{0}</color> is set\nThe home point has been removed",
                ["CAN_TELEPORT_HOME_NOT_FRIEND"] = ":exclamation:Player <color=#1F6BA0>{0}</color> is not your friend\nTo teleport to a friend's home, use: <color=#1F6BA0>/home HomeName FriendName</color>",
                ["CAN_TELEPORT_HOME_NOT_FOUND"] = ":exclamation:Player <color=#1F6BA0>{0}</color> was not found!\nTo teleport to a friend's home, use: <color=#1F6BA0>/home HomeName FriendName</color>",

                ["CAN_TELEPORT_LIMIT_NOTHING_HOME"] = ":exclamation:You have reached the <color=#1F6BA0>limit</color> for home teleports. Sending a request is unavailable",
                ["CAN_TELEPORT_LIMIT_NOTHING_WARP"] = ":exclamation:You have reached the <color=#1F6BA0>limit</color> for warp teleports. Sending a request is unavailable",
                ["CAN_TELEPORT_LIMIT_NOTHING_TELEPORTATION"] = ":exclamation:You have reached the <color=#1F6BA0>limit</color> for player teleports. Sending a request is unavailable",

                ["TELEPORTATION_HOME_LIMIT_INFO"] = ":heart:Remaining <color=#1F6BA0>limit</color> for home teleports: <color=#1F6BA0>{0}</color>",
                ["TELEPORTATION_HOME_LIMIT_NOTHING"] = ":exclamation:You have used the <color=#1F6BA0>entire limit</color> of home teleports",
                ["TELEPORTATION_WARP_LIMIT_INFO"] = ":heart:Remaining <color=#1F6BA0>limit</color> for warp teleports: <color=#1F6BA0>{0}</color>",
                ["TELEPORTATION_WARP_LIMIT_NOTHING"] = ":exclamation:You have used the <color=#1F6BA0>entire limit</color> of warp teleports",
                ["TELEPORTATION_PLAYER_LIMIT_INFO"] = ":heart:Remaining <color=#1F6BA0>limit</color> for player teleports: <color=#1F6BA0>{0}</color>",
                ["TELEPORTATION_PLAYER_LIMIT_NOTHING"] = ":exclamation:You have used the <color=#1F6BA0>entire limit</color> of player teleports",

                ["STATUS_PLAYER_TITLE"] = ":exclamation:You can't perform this action: <color=#C26D33>{0}</color>",
                ["STATUS_PLAYER_FLYING"] = "you are weightless",
                ["STATUS_PLAYER_RESTRAINED"] = "you are restrained or surrendering",
                ["STATUS_PLAYER_WOUNDED"] = "you are wounded",
                ["STATUS_PLAYER_RADIATION"] = "you are irradiated",
                ["STATUS_PLAYER_MOUNTED"] = "you are mounted / in a vehicle",
                ["STATUS_PLAYER_IS_TUTORIAL"] = "you are on the tutorial island",
                ["STATUS_PLAYER_IS_SWIMMING"] = "you are swimming",
                ["STATUS_PLAYER_IS_COLD"] = "you are cold",
                ["STATUS_PLAYER_IS_DEAD"] = "you are dead",
                ["STATUS_PLAYER_IS_SLEEPING"] = "you are sleeping",
                ["STATUS_PLAYER_IS_SAFE_ZON"] = "you are in a safe zone",
            }, this);
            
            lang.RegisterMessages(new Dictionary<String, String>
            {
                ["UI_SETUP_HOME_ALERT"] = "ХОТИТЕ УСТАНОВИТЬ ТОЧКУ ДОМА НА ЭТОМ МЕСТЕ?",
                ["UI_TPR_REQUSET"] = "ЗАПРОС НА ТЕЛЕПОРТАЦИЮ ОТ ИГРОКА {0}",

                ["SYNTAX_COMMAND_SETHOME"] = ":exclamation:Для установки точки дома используйте синтаксис : <color=#1F6BA0>/sethome НазваниеДома</color>",
                ["SYNTAX_COMMAND_REMOVEHOME"] = ":exclamation:Для удаления точки дома используйте синтаксис : <color=#1F6BA0>/removehome НазваниеДома</color>",
                ["SYNTAX_COMMAND_HOME_TELEPORTATION"] = ":exclamation:Для телепортации на точку дома используйте синтаксис : <color=#1F6BA0>/home НазваниеДома</color>",
                ["SYNTAX_COMMAND_TELEPORT_REQUEST"] = ":exclamation:Для телепортации к игроку используйте синтаксис : <color=#1F6BA0>/tpr ИмяИгрока</color>",
                ["SYNTAX_COMMAND_TELEPORT"] = ":exclamation:Для телепортации к игроку используйте синтаксис : <color=#1F6BA0>/tp ИмяИгрока</color>\nЧтобы телепортировать одного игрока к другому, используйте : <color=#1F6BA0>/tp кКомуТелепортировать КогоТелепортировать</color>",

                ["SYNTAX_COMMAND_WARPS"] = ":exclamation:Используйте синтаксис :" +
                                           "\n<color=#1F6BA0>/warp list</color> - для отображения всех доступных варпов" +
                                           "\n<color=#1F6BA0>/warp points НазваниеВарпа</color> - для отображения всех точек телепортации на данный варп" +
                                           "\n<color=#1F6BA0>/warp add НазваниеВарпа</color> - для создания нового варпа или установки дополнительной точки телепортации на этот варп" +
                                           "\n<color=#1F6BA0>/warp edit НазваниеВарпа НомерПозиции</color> - для редактирования позиции телепортации на варп" +
                                           "\n<color=#1F6BA0>/warp remove НазваниеВарпа</color> - для удаления варпа и всех доступных точек телепортации" +
                                           "\n<color=#1F6BA0>/warp remove НазваниеВарпа НомерПозиции</color> - для удаления позиции телепортации на указанный варп",

                ["HOMELIST_COMMAND_EMPTY"] = ":exclamation:У вас нет установленных точек дома",
                ["HOMELIST_COMMAND_ALL_LIST"] = ":yellowpin:Ваши установленные точки дома : {0}",
                ["HOMELIST_COMMAND_FORTMAT_HOME"] = "\n{0} в квадарте [{1}]",
                
                ["MONUMENT_BLOCKED_SETUP_NOT_CENTER"] = ":yellowpin:Невозможно определить монумент, встаньте ближе к центру данного монумента",
                ["MONUMENT_BLOCKED_SETU_BLOCKED"] = ":exclamation:Данный монумент уже запрещен для телепортации",
                ["MONUMENT_BLOCKED_SETUP_ACCES"] = ":exclamation:Вы успешно запретили телепортацию с данного монумента",
                
                ["COOLDOWN_MESSAGE"] = ":exclamation:Вам нужно подождать еще <color=#C26D33>{0}</color> перед выполнением этого действия",

                ["TITLE_FORMAT_DAYS"] = "Д",
                ["TITLE_FORMAT_HOURSE"] = "Ч",
                ["TITLE_FORMAT_MINUTES"] = "М",
                ["TITLE_FORMAT_SECONDS"] = "С",
                
                ["WARP_NOTHING"] = ":exclamation:Вы еще не создали варпов",
                ["WARP_NO_PERMISSIONS"] = ":exclamation:У вас недостаточно прав для телепортации на варп",
                ["WARP_TELEPORTATION_PLAYER"] = ":yellowpin:Вы были телепортированы на варп",
                ["WARP_TELEPORTATION_PLAYER_HOSTILE"] = ":exclamation:Вы не можете быть телепортированы на варп, вы в розыске",
                ["WARP_REQUEST_TELEPORTATION_PLAYER"] = ":yellowpin:Вы будете телепортированы на варп в течении <color=#1F6BA0>{0} секунд</color>",
                ["WARP_NOTHING_NAME"] = ":exclamation:Варпа с таким названием не существует",
                ["WARP_NOT_ARG_NAME"] = ":exclamation:Вам необходимо указать название варпа",
                ["WARP_SHOW_POINTS"] = ":yellowpin:Точки с позициями для телепортации на варп <color=#738D45>{0}</color> будут отображены на <color=#1F6BA0>30 секунд</color>",
                ["WARP_SETUP_POINTS_DISTANCE"] = ":yellowpin:Новая точка находится слишком близко к точке <color=#738D45>№{0}</color> указанного варпа, попробуйте отойти подальше и установить точку там",
                ["WARP_SETUP_POINT_ACCESS"] = ":yellowpin:Вы успешно добавили новую позицию для варпа с названием <color=#738D45>{0}</color>",
                ["WARP_SETUP_NEW"] = ":exclamation:Варп с названием <color=#738D45>{0}</color> успешно создан, зарезервирована команда для телепортации на него : <color=#738D45>{1}</color>",
                ["WARP_EDIT_POINT_ACCESS"] = ":yellowpin:Вы успешно изменили позицию для варпа с названием <color=#738D45>{0}</color>",
                ["WARP_NOTHING_KEY_POINTS"] = ":yellowpin:Вам нужно указать ключ(№) точки с позицией для ее редактирования\nДоступные ключи : {0}",
                ["WARP_NOTHING_KEY_IS_NUMBER"] = ":yellowpin:Указанный ключ(№) позиции - не является цифрой, вы должны указать только цифру",
                ["WARP_REMOVED"] = ":exclamation:Варп успешно удален.\nКоманда для телепортации : <color=#C26D33>{0}</color> - удалена",
                ["WARP_REMOVED_POINT"] = ":yellowpin:Точка телепортации <color=#C26D33>№{0}</color> удалена для варпа с названием <color=#1F6BA0>{1}</color>",
                ["WARP_REMOVED_POINT_NOTHING_KEYS"] = ":yellowpin:Точка <color=#C26D33>№{0}</color> не существует для варпа <color=#1F6BA0>{1}</color>\nДоступные ключи : {2}",

                ["AUTO_ACCEPT_TELEPORTATION_FRIEND_TRUE"] = ":heart:Вы <color=#738D45>включили</color> автоматическое принятие телепортов от друзей",
                ["AUTO_ACCEPT_TELEPORTATION_FRIEND_FALSE"] = ":heart:Вы <color=#C26D33>отключили</color> автоматическое принятие телепортов от друзей",
                
                ["GMAP_TELEPORT_TRUE"] = ":heart:Вы <color=#738D45>включили</color> возможность телепортации по GMap",
                ["GMAP_TELEPORT_FALSE"] = ":heart:Вы <color=#C26D33>отключили</color> возможность телепортации по GMap",
                
                ["ACCESS_SETUP_SETHOME"] = ":yellowpin:Вы успешно установили точку дома с названием <color=#738D45>{0}</color>",
                ["ACCESS_TELEPORTATION_HOME"] = ":yellowpin:Вы телепортируетесь в точку дома с названием <color=#738D45>{0}</color>\nПожалуйста, подождите : <color=#1F6BA0>{1}</color>",
                ["ACCESS_TELEPORTATION_HOME_FRIEND_NICK"] = ":yellowpin:Вы телепортируетесь в точку дома друга <color=#738D45>{0}</color> с названием <color=#738D45>{1}</color>\nПожалуйста, подождите : <color=#1F6BA0>{2}</color>",
                ["ACCESS_TELEPORTATION_HOME_FRIEND"] = ":yellowpin:Вы телепортируетесь в точку дома друга с названием <color=#738D45>{0}</color>\nПожалуйста, подождите : <color=#1F6BA0>{1}</color>",
                
                ["CAN_SETHOME_BUILDING"] = ":exclamation:Вы можете установить точку для дома <color=#C26D33>только на строении</color>",
                ["CAN_SETHOME_OTHER_ENTITY"] = ":exclamation:Под точкой или рядом находится <color=#C26D33>другой объект</color>, который <color=#C26D33>мешает</color> установить точку для дома",
                ["CAN_SETHOME_BUILDING_OR_TERRAIN"] = ":exclamation:Вы не можете установить точку дома на <color=#C26D33>скалах или иных объектах</color>, встаньте на <color=#C26D33>строение или на поверхность земли</color>",
                ["CAN_SETHOME_BUILDING_PRIVILAGE"] = ":exclamation:Чтобы установить точку для дома - необходимо <color=#C26D33>установить шкаф</color> в строении",
                ["CAN_SETHOME_BUILDING_PRIVILAGE_AUTH"] = ":exclamation:Чтобы установить точку для дома - необходимо <color=#C26D33>авторизоваться в шкафу</color>",
                ["CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_TUGBOAT"] = ":exclamation:Чтобы установить точку для дома - необходимо <color=#C26D33>авторизоваться в буксире</color>",
                ["CAN_SETHOME_BUILDING_PRIVILAGE_AUTH_BOAT_FOUNDATION"] = ":exclamation:Чтобы установить точку для дома - необходимо <color=#C26D33>авторизоваться в штурвале</color>",
                ["CAN_SETHOME_TUGBOAT_DISABLE"] = ":exclamation:Вы не можете устанавливать точку дома на буксирах, эта <color=#C26D33>функция отключена</color>",
                ["CAN_SETHOME_BOAT_FOUNDATION_DISABLE"] = ":exclamation:Вы не можете устанавливать точку дома на самодельных лодках, эта <color=#C26D33>функция отключена</color>",
                ["CAN_SETHOME_IS_RAIDBLOCK"] = ":exclamation:Чтобы установить точку для дома - у вас не должно быть <color=#C26D33>активного рейдблока</color>",
                ["CAN_SETHOME_OTHER_MESSAGE"] = ":exclamation:Вы не можете сейчас установить точку для дома",
                ["CAN_SETHOME_EXIST_HOMENAME"] = ":exclamation:Точка для дома с названием <color=#738D45>{0}</color> уже существует",
                ["CAN_SETHOME_DISTANCE_EXIST_HOME"] = ":exclamation:Вы не можете установить точку для дома, рядом установлена точка <color=#738D45>{0}</color>",
                ["CAN_SETHOME_MAXIMUM_HOMES"] = ":exclamation:Вы не можете установить точку дома, достигнут <color=#C26D33>максимальный лимит</color> количества установленных точек для дома",
                
                ["CAN_SETHOME_APARTMENT_DISABLE"] = ":exclamation:Вы не можете устанавливать точку дома в апартаментах, эта <color=#C26D33>функция отключена</color>",
                ["CAN_SETHOME_APARTMENT_NO_RENT"] = ":exclamation:Чтобы установить точку для дома - апартаменты должны быть <color=#C26D33>арендованы</color>",
                ["CAN_SETHOME_APARTMENT_NO_AUTH"] = ":exclamation:Чтобы установить точку для дома - вы должны быть <color=#C26D33>арендатором этих апартаментов или состоять в его команде</color>",
                ["CAN_TELEPORT_HOME_NOT_APARTMENT"] = ":exclamation:Точка дома <color=#738D45>{0}</color> была удалена - апартаменты <color=#C26D33>больше не арендованы</color>",
                ["CAN_TELEPORT_HOME_NOT_APARTMENT_NO_AUTH"] = ":exclamation:У вас <color=#C26D33>больше нет доступа</color> к апартаментам точки дома <color=#738D45>{0}</color>",
                ["CAN_TELEPORT_HOME_APARTMENT_LOST"] = ":exclamation:Телепортация отменена - вы <color=#C26D33>потеряли доступ</color> к апартаментам",
                
                ["A_HOME_CHECK_PLAYER_NOT_FOUND"] = ":exclamation:Игрок не найден",
                ["A_HOME_CHECK_NOT_HOME"] = ":exclamation:У игрока нет доступных точек дома",
                ["A_HOME_CLEAR_HOMES"] = ":exclamation:Все точки дома игрока были удалены",
                ["A_HOME_SHOW_POINTS"] = ":yellowpin:Точки дома игрока {0} отображаются на <color=#738D45>30 секунд</color>",
                
                ["CAN_REMOVE_HOME_NO_EXISTS"] = ":exclamation:У вас <color=#C26D33>не существует</color> точки дома с названием <color=#738D45>{0}</color>",
                ["ACCESS_REMOVE_HOME"] = ":exclamation:Вы <color=#C26D33>удалили</color> точку дома с названием <color=#738D45>{0}</color>",
                
                ["ACCESS_CANCELL_TELEPORTATION_PLAYER"] = ":exclamation:Вы <color=#1F6BA0>отменили</color> старый запрос на телепортацию домой",
                ["ACCESS_CANCELL_TELEPORTATION"] = ":exclamation:Телепортация была <color=#1F6BA0>отменена</color>",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD"] = "\nВы были убиты",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_TARGET"] = "\nИгрок к которому вы телепортировались был <color=#1F6BA0>убит</color>",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_TARGET_DISCONNECTED"] = "\nИгрок к которому вы телепортировались <color=#1F6BA0>вышел</color> с сервера",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_REQUESTER_DISCONNECTED"] = "\nИгрок который к вам телепортировался <color=#1F6BA0>вышел</color> с сервера",
                ["ACCESS_CANCELL_TELEPORTATION_DEAD_REQUESTER_DEAD"] = "\nИгрок который к вам телепортировался был <color=#1F6BA0>убит</color>",

                ["CAN_CANCELL_TELEPORTATION_NOT_FOUNDS"] = ":exclamation:У вас нет активных телепортаций",
                
                ["REQUEST_TELEPORTATION_ACCESS"] = ":heart:Ваш запрос на телепортацию принят - ожидайте <color=#1F6BA0>{0}</color>\n<color=#C26D33>Чтобы отменить запрос - используйте /tpc</color>",

                ["CAN_ACCEPT_TELEPORTATION_NOT_AUTO_ACCEPT"] = ":heart:Ваш запрос <color=#1F6BA0>отправлен</color>, но ваш друг <color=#C26D33>не может</color> принять его в данный момент", 
                ["CAN_ACCEPT_TELEPORTATION_YES_AUTO_ACCEPT"] = ":heart:Ваш запрос <color=#1F6BA0>автоматически принят</color> вашим другом", 
                
                ["CAN_ACCEPT_TELEPORTATION_NOT_REQUEST"] = ":exclamation:У вас нет <color=#1F6BA0>активных запросов</color> на телепортацию", 
                ["CAN_ACCEPT_TELEPORTATION_ACTIVE_ACCEPTED"] = ":exclamation:Вы уже <color=#1F6BA0>приняли запрос</color> на телепортацию, ожидайте телепортацию игрока!\n<color=#C26D33>Чтобы отменить запрос - используйте /tpc</color>", 

                ["CAN_REQUEST_TELEPORTATION_DISABLED"] = ":heart:На сервере отключена функция телепортации к игрокам", 
                ["CAN_REQUEST_TELEPORTATION_NOT_FOUND_PLAYER"] = ":exclamation:Такого игрока нет сейчас на сервере", 
                ["CAN_REQUEST_TELEPORTATION_ACCESS"] = ":heart:Вы <color=#738D45>приняли</color> запрос на телепортацию от <color=#1F6BA0>{0}</color>\nЧтобы <color=#C26D33>отменить запрос</color> - используйте <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_ACCESS_AUTO_TELEPORT"] = ":heart:Вы <color=#1F6BA0>автоматически</color> приняли запрос на телепортацию от вашего друга <color=#1F6BA0>{0}</color>\nЧтобы <color=#C26D33>отменить запрос</color> - используйте <color=#C26D33>/tpc</color>\nЧтобы <color=#C26D33>отключить</color> автоматическое принятие запросов на телепортацию от друзей - пропишите <color=#C26D33>/atp</color>",
                ["CAN_REQUEST_TELEPORTATION_SEND"] = ":heart:Вы <color=#738D45>отправили</color> запрос на телепортацию к игроку <color=#1F6BA0>{0}</color>\nЧтобы <color=#C26D33>отменить запрос</color> - используйте <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_RECEIVE"] = ":heart:Запрос на телепортацию от игрока <color=#1F6BA0>{0}</color>\nЧтобы <color=#738D45>принять запрос</color> - используйте <color=#738D45>/tpa</color>\nЧтобы <color=#C26D33>отменить запрос</color> - используйте <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY"] = ":exclamation:У вас уже есть <color=#C26D33>активный запрос</color> на телепортацию с игроком!\n<color=#C26D33>Чтобы отменить запрос - используйте /tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_TELEPORTED_REQUEST_ACTUALY_NAME"] = ":exclamation:У вас уже есть <color=#C26D33>активный запрос</color> на телепортацию с игроком <color=#1F6BA0>{0}</color>\nЧтобы <color=#C26D33>отменить запрос</color> - используйте <color=#C26D33>/tpc</color>",
                ["CAN_REQUEST_TELEPORTATION_NOTHING_TELEPORT_ME"] = ":exclamation:Вы не можете телепортироваться сами к себе",
                ["CAN_REQUEST_TELEPORTATION_ALREADY_REQUEST_TARGET"] = ":exclamation:У игрока к которому вы отправляете запрос на телепортацию, уже имеется <color=#C26D33>активный запрос</color>\nДождитесь когда он примет или отменит свой запрос",
                ["CAN_REQUEST_TELEPORTATION_ONLY_REQUEST_FRIENDS"] = ":exclamation:Вы можете отправлять запрос <color=#C26D33>только к друзьям</color>",
                ["CAN_REQUEST_PLAYER_NOT_MOUNT_POINT"] = ":exclamation:Вы не можете телепортироваться к игроку : <color=#C26D33>нет свободных мест</color> в его транспорте", 
                ["CAN_REQUEST_PLAYER_MOUNT_ERROR"] = ":exclamation:Игрок в транспорте, вы не можете сейчас телепортироваться к игроку", 

                ["FIND_PLAYER_INFO_MATCHES"] = ":exclamation:По вашему поиску с ником <color=#1F6BA0>'{0}'</color>, было найдено несколько игроков : <color=#1F6BA0>{1}</color>",
                
                ["CAN_TELEPORT_PLAYER_NOT_FOUND_TP_ONE_PLAYER"] = ":exclamation:Игрок <color=#C26D33>которого</color> вы собираетесь телепортировать не найден",
                ["CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER_ONE"] = ":exclamation:Игрок <color=#C26D33>к которому</color> вы собираетесь телепортировать игрока <color=#C26D33>{0}</color> - не найден",
                ["CAN_TELEPORT_PLAYER_NOT_FOUND_TP_TARGET_PLAYER"] = ":exclamation:Игрок <color=#C26D33>к которому</color> вы собираетесь телепортироваться не найден",
                ["CAN_TELEPORT_PLAYER_ACCES_TP"] = ":yellowpin:Вы были телепортированы к игроку <color=#C26D33>{0}</color>",
                ["CAN_TELEPORT_PLAYER_NOT_SETTING_USE_FRIEND_HOME"] = ":exclamation:Друг <color=#C26D33>запретил</color> телепортацию на свои точки дома",
                ["CAN_TELEPORT_PLAYER_NOT_USE_FRIEND_HOME"] = ":exclamation:Функция телепортации на точку дома друга <color=#C26D33>отключена</color>",
                ["CAN_TELEPORT_NULL_REASON"] = ":exclamation:Вы не можете выполнить телепортацию сейчас",
                ["CAN_TELEPORT_RAID_BLOCK"] = ":exclamation:Вы не можете выполнить телепортацию во время <color=#C26D33>рейдблока</color>",
                ["CAN_TELEPORT_TITILE_OTHER"] = ":exclamation:Вы не можете выполнить телепортацию находясь : <color=#C26D33>{0}</color>", 
                ["CAN_TELEPORT_IN_BUILDING_BLOCKED"] = "в чужой билдинг зоне",
                ["CAN_TELEPORT_IN_CARGO_SHIP"] = "на корабле",
                ["CAN_TELEPORT_IN_HOT_AIR_BALLOON"] = "на воздушном шаре",
                ["CAN_TELEPORT_MONUMENT_BLOCKED"] = ":exclamation:С данного монумента запрещено телепортироваться!\n<color=#C26D33>Покиньте монумент или отойдите от него подальше!</color>",
                ["CAN_TELEPORT_COOLDOWN"] = ":exclamation:Вы не можете выполнить данную телепортацию, перезарядка : <color=#C26D33>{0}</color>",
                ["CAN_TELEPORT_REQUEST_EXPIRED"] = ":exclamation:Ваш <color=#C26D33>запрос</color> на телепортацию <color=#C26D33>истек</color>, он был отменен",
                ["CAN_TELEPORT_ONLY_FRIEND"] = ":exclamation:Вы можете телепортироваться только к <color=#C26D33>друзья</color>\nТелепортация была отменена",
                ["CAN_TELEPORT_ONLY_FRIEND_TARGET"] = ":exclamation:Вы можете телепортировать к себе только <color=#C26D33>друзей</color>\nТелепортация была отменена",
                ["CAN_TELEPORT_PLAYER_NOT_MOUNT_POINT"] = ":exclamation:Вы не можете принять телепортацию : <color=#C26D33>нет свободных мест</color> в транспорте", 
                ["CAN_TELEPORT_PLAYER_MOUNT_ERROR"] = ":exclamation:Вы в транспорте, не можете сейчас принять телепортацию", 
                
                ["CAN_TELEPORT_HOME_FOUNDATION_DESTROYED"] = ":exclamation:Фундамент на котором расположена точка дома был уничтожен, вы не можете телепортироваться",
                ["CAN_TELEPORT_HOME_TUGBOAT_DESTROYED"] = ":exclamation:Буксир на котором расположена точка дома был уничтожен, вы не можете телепортироваться",
                ["CAN_TELEPORT_HOME_PLAYER_BOAT_DESTROYED"] = ":exclamation:Лодка на котором расположена точка дома был уничтожен, вы не можете телепортироваться", 
                ["CAN_TELEPORT_HOME_NOT_PLAYER_BOAT"] = ":exclamation:Лодка на котором расположена точка дома <color=#1F6BA0>{0}</color> был <color=#C26D33>уничтожен</color>", 
                ["CAN_TELEPORT_HOME_NOT_PLAYER_BOAT_NO_AUTH"] = ":exclamation:Вы не авторизованы в лодке на которой расположена точка дома <color=#1F6BA0>{0}</color>, она была <color=#C26D33>уничтожена</color>",
                ["CAN_TELEPORT_HOME_NOT_TUGBOAT"] = ":exclamation:Буксир на котором расположена точка дома <color=#1F6BA0>{0}</color> был <color=#C26D33>уничтожен</color>",
                ["CAN_TELEPORT_HOME_NOT_TUGBOAT_NO_AUTH"] = ":exclamation:Вы не авторизованы в буксире на котором расположена точка дома <color=#1F6BA0>{0}</color>, она была <color=#C26D33>уничтожена</color>",
                ["CAN_TELEPORT_HOME_NOT_HOME"] = ":exclamation:Точки дома с названием <color=#1F6BA0>{0}</color> - не существует",
                ["CAN_TELEPORT_HOME_NOT_BUILDING_BLOCK"] = ":exclamation:Точка дома с названием <color=#1F6BA0>{0}</color> застроена или у нее отсутсвует поверхность на которой она была установлена\n<color=#C26D33>Точка дома была удалена</color>",
                ["CAN_TELEPORT_HOME_NOT_AUTH_BUILDING_PRIVILAGE"] = ":exclamation:Вы не авторизованы в строении где установлена точка дома с названием <color=#1F6BA0>{0}</color>\nТочка дома была удалена",
                ["CAN_TELEPORT_HOME_NOT_FRIEND"] = ":exclamation:Игрок <color=#1F6BA0>{0}</color> не является вашим другом\nЧтобы телепортироваться на дом друга, используйте : <color=#1F6BA0>/home НазваниеДома ИмяДруга</color>",
                ["CAN_TELEPORT_HOME_NOT_FOUND"] = ":exclamation:Игрок с именем <color=#1F6BA0>{0}</color> не найден!\nЧтобы телепортироваться на дом друга, используйте : <color=#1F6BA0>/home НазваниеДома ИмяДруга</color>",
              
                ["CAN_TELEPORT_LIMIT_NOTHING_HOME"] = ":exclamation:Вы исчерпали <color=#1F6BA0>лимит</color> телепортаций на точку дома. Отправка запроса недоступна",
                ["CAN_TELEPORT_LIMIT_NOTHING_WARP"] = ":exclamation:Вы исчерпали <color=#1F6BA0>лимит</color> телепортаций на варп. Отправка запроса недоступна",
                ["CAN_TELEPORT_LIMIT_NOTHING_TELEPORTATION"] = ":exclamation:Вы исчерпали <color=#1F6BA0>лимит</color> телепортаций к игроку. Отправка запроса недоступна",
                
                ["TELEPORTATION_HOME_LIMIT_INFO"] = ":heart:Остаток <color=#1F6BA0>лимита</color> на телепортацию домой : <color=#1F6BA0>{0}</color>",
                ["TELEPORTATION_HOME_LIMIT_NOTHING"] = ":exclamation:Вы исчерпали <color=#1F6BA0>весь лимит</color> телепортаций на точку дома",
                ["TELEPORTATION_WARP_LIMIT_INFO"] = ":heart:Остаток <color=#1F6BA0>лимита</color> на телепортацию на варпы : <color=#1F6BA0>{0}</color>",
                ["TELEPORTATION_WARP_LIMIT_NOTHING"] = ":exclamation:Вы исчерпали <color=#1F6BA0>весь лимит</color> телепортаций на вапры",
                ["TELEPORTATION_PLAYER_LIMIT_INFO"] = ":heart:Остаток <color=#1F6BA0>лимита</color> на телепортацию к игрокам : <color=#1F6BA0>{0}</color>",
                ["TELEPORTATION_PLAYER_LIMIT_NOTHING"] = ":exclamation:Вы исчерпали <color=#1F6BA0>весь лимит</color> телепортаций к игрокам",
                
                ["STATUS_PLAYER_TITLE"] = ":exclamation:Вы не можете выполнить это действие - <color=#C26D33>{0}</color>",
                ["STATUS_PLAYER_FLYING"] = "вы в невесомости",
                ["STATUS_PLAYER_RESTRAINED"] = "вы связаны или сдаётесь",
                ["STATUS_PLAYER_WOUNDED"] = "вы ранены",
                ["STATUS_PLAYER_RADIATION"] = "вы облучены радиацией",
                ["STATUS_PLAYER_MOUNTED"] = "вы сидите в транспорте или на объекте",
                ["STATUS_PLAYER_IS_TUTORIAL"] = "вы на обучающем острове",
                ["STATUS_PLAYER_IS_SWIMMING"] = "вы плаваете",
                ["STATUS_PLAYER_IS_COLD"] = "вам холодно",
                ["STATUS_PLAYER_IS_DEAD"] = "вы мертвы",
                ["STATUS_PLAYER_IS_SLEEPING"] = "вы спите",
                ["STATUS_PLAYER_IS_SAFE_ZON"] = "вы находитесь в мирной зоне",
            }, this, "ru");
        }
        #endregion

        #region API

        private void API_AutoTeleportToggle(BasePlayer player)
        {
            if (!playerSettings.TryGetValue(player.userID, out SettingPlayer setting)) return; 
            setting.autoAcceptTeleportationFriends = !setting.autoAcceptTeleportationFriends;
        }
        private Boolean API_AutoTeleportStatus(BasePlayer player) => playerSettings.TryGetValue(player.userID, out SettingPlayer setting) && setting.autoAcceptTeleportationFriends;

        private Boolean API_HavePendingRequest(BasePlayer player) => playerTeleportationQueue.ContainsKey(player.userID);

        private void ClearHomesPlayer(UInt64 userID, String pluginName)
        {
            if (!playerHomes.ContainsKey(userID)) return;
            playerHomes[userID].Clear();

            Puts(LanguageEn ? $"{pluginName}: All home points for player {userID} have been cleared" : $"{pluginName} : Все точки дома игрока {userID} были очищены");
        }
        
        private Dictionary<String, Vector3> GetHomes(UInt64 userID)
        {
            Dictionary<String, Vector3> homeList = new();
            if (!playerHomes.TryGetValue(userID, out Dictionary<String, PlayerHome> home)) return homeList;

            foreach (KeyValuePair<String, PlayerHome> pHome in home)
            {
                Vector3 positionHome = default;
                if (pHome.Value.netIdEntity != 0)
                {
                    switch (pHome.Value.typeEntity)
                    {
                        case TypeHomeEntity.Tugboat:
                        {
                            if (tugboatsServer.TryGetValue(pHome.Value.netIdEntity, out Tugboat tugboat) && tugboat && !tugboat.IsDestroyed)
                                positionHome = tugboat.transform.TransformPoint(pHome.Value.positionHome);
                            break;
                        }
                        case TypeHomeEntity.PlayerBoat:
                        {
                            if (playersBoatsServer.TryGetValue(pHome.Value.netIdEntity, out PlayerBoat playerBoat) && playerBoat && !playerBoat.IsDestroyed)
                                positionHome = playerBoat.transform.TransformPoint(pHome.Value.positionHome);
                            break;
                        }
                        case TypeHomeEntity.Apartment:
                        {
                            ApartmentRoom apartmentRoom = GetApartmentRoomByID(pHome.Value.netIdEntity);
                            if (apartmentRoom)
                                positionHome = pHome.Value.positionHome;
                            break;
                        }
                    }
                }
                else positionHome = pHome.Value.positionHome;

                if (positionHome == default) continue;

                homeList.TryAdd(pHome.Key, positionHome);
            }

            return homeList;
        }

        #endregion
    }
}