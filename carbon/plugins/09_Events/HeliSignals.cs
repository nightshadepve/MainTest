using Facepunch;
using HarmonyLib;
using Network.Visibility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Rust;
using Rust.Ai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Reflection;
using Time = UnityEngine.Time;
using Random = UnityEngine.Random;

#region Changelog

/* ToDo:
 - Add API to allow other devs to call HS to a position
    - API_CallHeliSignalToPosition(BasePlayer player, Vector3 position)

 - Add ZoneManager Support

 - Add commands on killed (list)
*/

/* Changelog

1.4.1:
 - Fixed: For 1st Oct 2026 force wipe update

1.4.0:
 - Fixed: Bug where modified SignalCooldown plugin still conflicted
 - Added: Config option for running commands when heli is killed
 - Added: HackableCrate extra loot config options

1.3.3:
 - Fixed: Bug where extra loot item number was ignored

1.3.2:
 - Fixed: "MissingMethodException: Method 'LockedByEntCrate.SetLockingEnt' not found." error

1.3.1:
 - Fixed: For 3rd Sep 2026 Rust update
 - Fixed: HelicopterDebris.tooHotUntil removed and changed to new HelicopterDebris.OnCooledDown()

1.3.0:

 - Fixed: Updated obsolete BaseEntity.SetFlag to BaseEntity.SetFlagLocal
 - Fixed: Updated other deprecated code
 - Fixed: Item.Text not setting on crate loot when specified in the config
 - Fixed: Tightened up logic in OnLootSpawn to protect against config errors
 - Fixed: Tightened up logic in SpawnHeliCrateLoot, SpawnExtraLoot and SpawnHackableCrate to protect against config errors
 - Fixed: Stop players putting heli signals in Tin Can Alarm
 - Fixed: Updated obsolete BaseEntity.SetFlag to BaseEntity.SetFlagLocal
 - Added: "cratesToSpawn" as a return field in GetHeliSignalData hook.
 - Added: "lockedCratesToSpawn" as a return field in GetHeliSignalData hook.
 - Added: Custom heli crate spawning logic overriding native crate spawning
 - Added: Custom reward item config field for "item.text"
 - Added: Loot item config field for "item.text"
 - Added: Custom currency item config field for "item.text" 
 - Added: CanSpawnHeliSignalCrates hook
 - Added: CanSpawnHeliSignalLockedCrates hook
 - Added: OnHeliSignalHeliKill hook
 - Added: OnHeliSignalHeliRetire hook

1.2.35
 - Fixed: XP/Eco/RP not being given in certain cases
 - Fixed: Heli is now visible and can travel to/from Deep Sea (see plugin info page)
 - Added: Config option: "Allow Helis to be Called in Deep Sea": true/false
 - Added: New Config option: "Retire Helis if Calling Player Goes to Deep Sea": true/false
 - Changed: Players are no longer prevented from travelling to Deep Sea with active helis

1.2.34
 - Fixed: Bug that prevented damage to calling players base when enabled in the config on PVE servers
 - Added: Config option to enable/disable debug/verbose logging

1.2.33
 - Fixed: Return to calling player logic fixed so it no longer prevents an active strafe
 - Fixed: Failed to run timer NRE (RetireHeli => RemoveHeliData)
 - Fixed: Added checks to prevent players calling helis at Deep Sea
 - Fixed: Added check to prevent calling player teleporting to Deep Sea with active helis
 - Added: New API method 'GetHeliSignalData' to return basic info about a Heli Signal spawned heli

1.2.32
 - Fixed: For Sep 4th 2025 Rust update
 - Fixed: Invalid item shortnames in loot tables causing server crash
 
*/

#endregion Changelog

namespace Oxide.Plugins
{
    [Info("Heli Signals", "ZEODE", "1.4.1")]
    [Description("Call Patrol Helicopters to your location with custom supply signals.")]
    public class HeliSignals : RustPlugin
    {
        #region Plugin References

        [PluginReference] private Plugin 
            Friends, 
            Clans, 
            ServerRewards, 
            Economics, 
            Vanish, 
            NoEscape, 
            BotReSpawn, 
            XPerience, 
            SkillTree, 
            Skills, 
            NpcSpawn, 
            ZoneManager;

        #endregion Plugin References

        #region Constants

        public static HeliSignals Instance;

        private Harmony harmony;
        private MethodInfo _updateTargetListMethod;
        private MethodInfo _fireRocketMethod;
        private MethodInfo _updateNetworkGroup;
        private const string HarmonyPatchId = "HeliSignals.Internal";
        public static Dictionary<PatrolHelicopterAI, HeliComponent> heliCompCache = new Dictionary<PatrolHelicopterAI, HeliComponent>();

        private static readonly FieldInfo MapMarkerInstanceField = AccessTools.Field(typeof(HackableLockedCrate), "mapMarkerInstance");

        private static System.Random random = new System.Random();

        private const string permAdmin = "helisignals.admin";
        private const string permBuy = "helisignals.buy";
        private const string permBypasscooldown = "helisignals.bypasscooldown";

        private const string heliPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string hackableCrate = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        private const string heliCrate = "assets/prefabs/npc/patrol helicopter/heli_crate.prefab";
        private const string shockSound = "assets/prefabs/locks/keypad/effects/lock.code.shock.prefab";
        private const string deniedSound = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";

        private const string steamProfileUrl = "https://steamcommunity.com/profiles/";
        private const string HELI_AVATAR = "https://zeode.io/images/HeliSignalsBot.png";
        private const string zeodeFooterUrl = "https://zeode.io";
        private const string defaultWebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

        private const int supplySignalId = 1397052267;
        private const int scrapId = -932201673;
        private const uint ENGINE_COL = 224139191;
        private const uint MAIN_ROTOR_COL = 1460989848;
        private const uint TAIL_COL = 2699525250;

        // Default config item skins
        // Single
        private const ulong easySkinID = 2920175997;
        private const ulong medSkinID = 2920176079;
        private const ulong hardSkinID = 2920176050;
        private const ulong eliteSkinID = 2920176024;
        private const ulong expertSkinID = 3099117081;
        private const ulong nightmareSkinID = 3099117372;
        // Multi
        private const ulong easyMultiSkinID = 3083234542;
        private const ulong medMultiSkinID = 3083234833;
        private const ulong hardMultiSkinID = 3083234755;
        private const ulong eliteMultiSkinID = 3083234647;
        private const ulong elxpertMultiSkinID = 3099124338;
        private const ulong nightmareMultiSkinID = 3099124426;
        // Wave
        private const ulong normalWaveSkinID = 3104667036;
        private const ulong hardWaveSkinID = 3104666951;

        // Default config item names
        // Single
        private const string easyHeli = "Heli Signal (Easy)";
        private const string medHeli = "Heli Signal (Medium)";
        private const string hardHeli = "Heli Signal (Hard)";
        private const string expertHeli = "Heli Signal (Expert)";
        private const string nightmareHeli = "Heli Signal (Nightmare)";
        private const string eliteHeli = "Heli Signal (Elite)";
        // Multi
        private const string easyMulti = "Multi Heli (Easy)";
        private const string medMulti = "Multi Heli (Medium)";
        private const string hardMulti = "Multi Heli (Hard)";
        private const string eliteMulti = "Multi Heli (Elite)";
        // Wave
        private const string normalWave = "Heli Wave Signal (Normal)";
        private const string hardWave = "Heli Wave Signal (Hard)";

        #endregion Constants

        #region Language & Log

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SyntaxPlayer"] = "<color=red>Invalid syntax</color>, use:\n\n<color=green>/hsgive <type> <SteamID/PlayerName> <amount></color>",
                ["SyntaxConsole"] = "Invalid syntax, use: hsgive <type> <SteamID/PlayerName> <amount>",
                ["ClearSyntaxPlayer"] = "<color=red>Invalid syntax</color>, use:\n\n<color=green>/hsclearcd</color> (clears all cooldowns)\n\nOr\n\n<color=green>/hsclearcd <SteamID/PlayerName></color>",
                ["ClearSyntaxConsole"] = "Invalid syntax, use: \"hsclearcd\" (clears all cooldowns) or \"hsclearcd <SteamID/PlayerName>\"",
                ["Receive"] = "You received <color=orange>{0}</color> x <color=orange>{1}</color>!",
                ["PlayerReceive"] = "Player {0} ({1}) received {2} x {3}!",
                ["Permission"] = "You do not have permission to use <color=orange>{0}</color>!",
                ["NotInsideDeepSea"] = "You cannot call a <color=orange>{0}</color> while in the Deep Sea!",
                ["NoDeepSeaTeleportWarn"] = "You cannot travel to the Deep Sea while you have active heli signals, turn back now or all active helis will retire!",
                ["NoDeepSeaTeleportRetire"] = "All active helis lost target, broke off engagement and retired back to base.",
                ["RaidBlocked"] = "You cannot use <color=orange>{0}</color> while raid blocked!",
                ["CombatBlocked"] = "You cannot use <color=orange>{0}</color> while combat blocked!",
                ["CooldownTime"] = "Cooldown active! You can call another in {0}!",
                ["TeamCooldownTime"] = "Team cooldown active! You can call another in {0}!",
                ["GlobalLimit"] = "Global limit of {0} active helicopters is reached, please try again later",
                ["PlayerLimit"] = "Player limit of {0} active helicopters is reached, please try again later",
                ["NotAdmin"] = "You do not have permission to use that command!",
                ["PlayerNotFound"] = "Can't find a player with the name or ID: {0}",
                ["InGameOnly"] = "Error: This command is only for use in game!",
                ["PlayerDead"] = "Player with name or ID {0} is dead, try again when they have respawned",
                ["InNamedMonument"] = "You cannot call <color=orange>{0}</color> in or near <color=red>{1}</color>, signal refunded, check inventory.",
                ["InSafeZone"] = "<color=orange>{0}</color> was thrown in a <color=green>Safe Zone</color> and was refunded, check inventory.",
                ["NotInZone"] = "<color=orange>{0}</color> must be thrown into a Heli Signal zone, signal refunded, check inventory.",
                ["InvalidDrop"] = "Signal type \"{0}\" not recognised, please check and try again!",
                ["CannotLoot"] = "You cannot loot this because it is not yours!",
                ["CannotHack"] = "You cannot hack this because it is not yours!",
                ["CannotHarvest"] = "You cannot harvest this because it is not yours!",
                ["BuyCmdSyntax"] = "Buy Command Usage (prefix / for chat):\n\n{0}{1}",
                ["NoBuy"] = "Buy Command for <color=orange>{0}</color> Is Not Enabled!",
                ["BuyPermission"] = "You do not have permission to buy Heli Signal \"<color=orange>{0}</color>\".",
                ["PriceList"] = "Heli Signal Prices:\n\n{0}",
                ["HeliKilledTime"] = "<color=orange>{0}</color> killed by <color=green>{1}</color> in grid <color=green>{2}</color> (Time Taken: {3})",
                ["HeliCalled"] = "<color=green>{0}</color> just called in <color=orange>{1}</color> to their location in grid <color=green>{2}</color>",
                ["PointsGiven"] = "<color=green>{0} {1}</color> received for destroying <color=orange>{2}</color>!",
                ["XPGiven"] = "<color=green>{0} XP</color> received for destroying <color=orange>{1}</color>!",
                ["ScrapGiven"] = "<color=green>{0} Scrap</color> received for destroying <color=orange>{1}</color>!",
                ["CustomGiven"] = "<color=green>{0} {1}</color> received for destroying <color=orange>{2}</color>!",
                ["CannotDamage"] = "You <color=red>Cannot</color> damage this <color=orange>{0}</color>!",
                ["NoTurret"] = "You <color=red>Cannot</color> damage this <color=orange>{0}</color> with remote turrets!",
                ["TooFarAway"] = "You are <color=red>Too Far</color> away to engage this <color=orange>{0}</color> ({1} m)",
                ["CantAfford"] = "You <color=red>Cannot</color> afford this! Cost: <color=orange>{0}</color> Required: <color=orange>{1}</color>",
                ["FullInventory"] = "<color=green>{0}</color> <color=red>NOT</color> purchased - no inventory space. You have not been charged.",
                ["PlayerFull"] = "<color=green>{0}</color> <color=red>NOT</color> given to {1} - No inventory space.",
                ["HeliReportTitle"] = "There are currently <color=orange>{0}</color> active Helicopters",
                ["HeliReportItem"] = "<size=9><color=orange>{0}/{1}</color> - <color=green>{2}</color> (Owner: <color=orange>{3}</color> Grid: <color=orange>{4}</color> Health: <color=orange>{5}</color> Rotors: <color=orange>{6}/{7}</color> State: <color=orange>{8}</color>)\n</size>",
                ["HeliReportTitleCon"] = "There are currently {0} active Helicopters",
                ["HeliReportItemCon"] = "{0}/{1} - {2} (Owner: {3} Grid: {4} Health: {5} Rotors: {6}/{7} State: {8})\n",
                ["HeliReportList"] = "{0}",
                ["RetireHeli"] = "<color=orange>{0}</color> is retiring, you were warned! <color=red>{1}</color>/<color=red>{2}</color>",
                ["RetireWarn"] = "<color=red>Damage Blocked!</color> You may only attack from a base with TC auth. If you continue, the <color=orange>{0}</color> will retire. Warning Level: <color=red>{1}</color>/<color=red>{2}</color>",
                ["DmgReport"] = "<color=orange>Damage</color> / <color=orange>Rotors</color> / <color=orange>Turret Damage</color>\n\n{0}\n{1}",
                ["DmgReportOwner"] = "<size=11>Type: <color=orange>{0}</color> Owner: <color=green>{1}</color> Status: <color=red>{2}</color>\n</size>",
                ["DmgReportIndex"] = "<size=11>{0}. <color=green>{1}</color> -> {2} HP / <color=green>{3}%</color> / <color=red>{4}%</color>\n</size>",
                ["DiscordCall"] = "**{0}** just called a **{1}** to their location in grid **{2}**",
                ["DiscordKill"] = "**{0}** just took down a **{1}** in grid **{2}**",
                ["DiscordRetire"] = "**{0}** called by **{1}** just retired from grid **{2}**",
                ["RetiredHelis"] = "You have retired ALL your (your teams) called Patrol Helicopters",
                ["NoRetiredHelis"] = "You have no active Patrol Helicopters to retire.",
                ["CooldownsCleared"] = "All player cooldowns have been cleared!",
                ["PlayerCooldownCleared"] = "Cooldown cleared for player {0} ({1})",
                ["PlayerNoCooldown"] = "No active cooldown for player {0} ({1})",
                ["NoWaveProfiles"] = "There are no wave heli profiles set up for <color=orange>{0}</color>, please report to an Admin.",
                ["WaveProfileError"] = "There is an error in the plugin config <color=orange>{0}</color>, please report to an Admin.",
                ["FirstHeliCalled"] = "Stand by, {0} <color=red>{1}</color> on route to your location!",
                ["NextHeliDelayed"] = "<color=green>{0}</color> destroyed! Stand by, a <color=red>{1}</color> is enroute to your location, ETA {2} mins",
                ["NextHeliInbound"] = "Look sharp, a <color=red>{0}</color> is closing in on your location!",
                ["WaveFinished"] = "<color=green>{0}</color> destroyed! <color=red>Hostile forces have no more airbourne assets to send to your location.</color> Well Done!",
                ["HeliRetired"] = "<color=green>{0}</color> is low on fuel and is breaking off engagement to return to base.",
                ["WaveRetired"] = "<color=green>{0}</color> finished operations in the area and is retiring. Hostile airbourne forces are returning to base.",
                ["DiscordEmbedTitleCalled"] = "🚁  {0} Called  🚁",
                ["DiscordEmbedTitleKilled"] = "💀  {0} Destroyed  💀",
                ["DiscordEmbedTitleRetire"] = "🚫  {0} Retired  🚫",
                ["DiscordEmbedOwner"] = "Owner: {0} SteamID: {1}",
                ["DiscordEmbedFooter"] = "Heli Signals by ZEODE",
                ["DiscordCalledLocation"] = "Location 📍",
                ["DiscordCalledHealth"] = "Health ❤️",
                ["DiscordCalledDespawn"] = "Despawn Time 🕐",
                ["DiscordKilledLocation"] = "Location 💥",
                ["DiscordKilledTime"] = "Time Taken ⏱️",
                ["DiscordKilledKiller"] = "Killer ⚔️",
                ["DiscordKilledLeaderboard"] = "Damage Leaderboard 🏆",
                ["DiscordRetireLocation"] = "Location 📍",
                ["DiscordRetireTime"] = "Retired After ⏱️",
                ["DiscordRetirePlayer"] = "Retired By 🧑",
                ["DiscordRetireHealth"] = "Health ❤️",
                ["DiscordRetireReport"] = "Damage Report 📋",
                ["DiscordEmbedDamageReportIndex"] = "{0}. {1} Total: {2} HP Rotors: {3} % Turret: {4} %\n",
                ["DiscordRetireNoReport"] = "No Damage",
                ["DiscordEmbedTitleKilledWave"] = "💀  {0} Destroyed (Wave: {1} of {2})  💀",
                ["DiscordWaveTotal"] = "Total Waves #️⃣",
                ["DiscordWaveHealthInfo"] = " - **HP:** {0}  **Rotors:** {1} / {2}",
                ["DiscordRotorHealthInfo"] = "Rotor HP ❤️",
                ["DiscordWaveHelicopters"] = "Wave Helicopters 🌊",
                ["DiscordEmbedTitleWaveComplete"] = "🏁  {0} Complete  🏁",
                ["DiscordWaveCompleteStatus"] = "Wave Status 🌊",
                ["DiscordWaveCompleteHelicopters"] = "Wave Helicopters 🚁",
                ["DiscordWaveCompleteTotal"] = "Total Destroyed 💀",
                ["DiscordWaveCompleteAllDestroyed"] = "All Helicopters Destroyed",
                ["DiscordEmbedTitleWaveRetired"] = "🚫  {0} Retired  🚫",
                ["DiscordMultiOrWaveRetiredBy"] = "Retired by: [{0}]({1})",
                ["DiscordMultiOrWaveRetiredTimeout"] = "Ran Out Of Time",
                ["DiscordWaveRetiredHelicopters"] = "Wave Helicopters 🚁",
                ["DiscordEmbedTitleKilledMulti"] = "💀  {0} Destroyed (Multi: {1} of {2})  💀",
                ["DiscordEmbedTitleMultiRetired"] = "🚫  {0} Retired (Multi Event)  🚫",
                ["DiscordWaveNotSpawned"] = "Not Yet Spawned",
                ["DiscordWavePartiallyDestroyed"] = "Partially Destroyed",
                ["DiscordMultiCompleteStatus"] = "Multi Status 🚁+",
                ["DiscordMultiCompleteHelicopters"] = "Multi Helicopters 🚁",
            }, this);
        }

        private string Lang(string messageKey, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, null);

            return args == null || args.Length == 0
                ? message
                : string.Format(message, args);
        }

        private string PlayerLang(string messageKey, string playerId, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, playerId);

            return args == null || args.Length == 0
                ? message
                : string.Format(message, args);
        }

        private void Message(IPlayer player, string messageKey, params object[] args)
        {
            if (player == null || !player.IsConnected)
                return;

            var message = PlayerLang(messageKey, player.Id, args);

            if (config.options.usePrefix && config.options.chatPrefix != string.Empty)
            {
                if (player.IsServer)
                {
                    Regex regex = new Regex(@"(\[.*?\])");
                    Match match = regex.Match(config.options.chatPrefix);
                    player.Reply($"{match}: {message}");
                }
                else
                {
                    player.Reply($"{config.options.chatPrefix}: {message}");
                }
            }
            else
            {
                player.Reply(message);
            }
        }

        private void Message(BasePlayer player, string messageKey, params object[] args)
        {
            if (player == null || player is NPCPlayer || !player.IPlayer.IsConnected)
                return;

            var message = PlayerLang(messageKey, player.UserIDString, args);

            Player.Message(
                player,
                message,
                config.options.usePrefix ? config.options.chatPrefix : null,
                config.options.chatIcon
            );
        }

        public enum LogLevel
        {
            INFO,
            ERROR,
            WARN,
            DEBUG
        }

        internal void Log(string message, LogLevel level = LogLevel.INFO)
        {
            if (level == LogLevel.DEBUG && !config.options.enableDebug)
                return;

            string prefix = level.ToString();

            switch (level)
            {
                case LogLevel.ERROR:
                    PrintError($"{prefix}: {message}");
                    break;
                case LogLevel.WARN:
                    PrintWarning($"{prefix}: {message}");
                    break;
                case LogLevel.DEBUG:
                case LogLevel.INFO:
                default:
                    Puts($"{prefix}: {message}");
                    break;
            }
        }

        #endregion Language & Log

        #region Plugin Load/Unload

        private void Init()
        {
            Instance = this;

            permission.RegisterPermission(permAdmin, this);
            permission.RegisterPermission(permBuy, this);
            permission.RegisterPermission(permBypasscooldown, this);
            config.rewards.RegisterPermissions(permission, this);
            config.heli.RegisterPermissions(permission, this);

            AddCovalenceCommand(config.options.reportCommand, nameof(CmdReport));
            AddCovalenceCommand(config.purchasing.buyCommand, nameof(CmdBuySignal));
            AddCovalenceCommand(config.heli.retireCommand, nameof(CmdRetireHeli));
            AddCovalenceCommand("hsgive", nameof(CmdGiveSignal));
            AddCovalenceCommand("hsclearcd", nameof(CmdClearCooldown));
        }

        private void OnServerInitialized()
        {
            ApplyHarmonyPatches();

            CheckRewardPlugins();

            NextTick(() =>
            {
                LoadProfileCache();

                if (!config.options.useStacking)
                {
                    Unsubscribe(nameof(CanStackItem));
                    Unsubscribe(nameof(CanCombineDroppedItem));
                }
                if (config.options.noVanillaHeli)
                    Log($"Vanilla patrol Helicopter server event is disabled", LogLevel.INFO);

                if (!config.heli.allowMonuments)
                {
                    foreach (var monument in TerrainMeta.Path.Monuments)
                    {
                        if (config.heli.excludedMonuments.Contains(monument.name))
                            Monuments.Add(monument);
                    }
                    if (Monuments.Count == 0)
                    {
                        Log($"No monument info found. Config options relating to 'Allow Players to Call Helis at Monuments' will not function.", LogLevel.WARN);
                    }
                }
                if (config.heli.allowFlee)
                {
                    RunSilentCommand("PatrolHelicopterAI.use_danger_zones", "True");
                    RunSilentCommand("PatrolHelicopterAI.flee_damage_percentage", $"{config.heli.fleePercent}");
                }
                else if (!config.heli.allowFlee)
                {
                    RunSilentCommand("PatrolHelicopterAI.use_danger_zones", "False");
                    RunSilentCommand("PatrolHelicopterAI.flee_damage_percentage", "0.35");
                }
                if (config.heli.canMonumentCrash)
                    RunSilentCommand("PatrolHelicopterAI.monument_crash", "True");
                else if (!config.heli.canMonumentCrash)
                    RunSilentCommand("PatrolHelicopterAI.monument_crash", "False");
            });
        }

        private void Unload()
        {
            if (heliCompCache != null && heliCompCache.Count > 0)
            {
                foreach (var kvp in heliCompCache)
                {
                    var component = kvp.Value;
                    if (component != null)
                    {
                        component.enabled = false;
                        component.CancelInvoke();
                        component.StopAllCoroutines();
                    }
                }
                heliCompCache.Clear();
            }

            if (config.options.noVanillaHeli)
                Log($"Vanilla patrol Helicopter server event has been re-enabled", LogLevel.INFO);

            var heliDataCopy = new Dictionary<ulong, HeliStats>(HeliSignalData);
            foreach (var netId in heliDataCopy.Keys)
            {
                try
                {
                    var heli = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as PatrolHelicopter;
                    if (heli != null)
                    {
                        HeliComponent heliComp = heli.GetComponent<HeliComponent>();
                        if (heliComp != null)
                        {
                            heliComp.enabled = false;
                            UnityEngine.Object.Destroy(heliComp);
                        }
                        heli.Kill(BaseNetworkable.DestroyMode.None);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Cleaning up Heli: {ex.Message}", LogLevel.ERROR);
                }
            }

            heliDataCopy.Clear();
            HeliSignalData.Clear();
            PlayerCooldowns.Clear();
            TierCooldowns.Clear();
            LockedCrates.Clear();
            HeliProfiles.Clear();
            WaveProfiles.Clear();
            Monuments.Clear();
            HeliProfileCache.Clear();
            SpawnedWaveProfiles.Clear();
            AutoRetiringWaveHelis.Clear();
            AutoRetiringMultiHelis.Clear();

            try
            {
                if (harmony != null)
                {
                    if (_updateTargetListMethod != null)
                        harmony.Unpatch(_updateTargetListMethod, HarmonyPatchType.Prefix, HarmonyPatchId);

                    if (_fireRocketMethod != null)
                        harmony.Unpatch(_fireRocketMethod, HarmonyPatchType.Prefix, HarmonyPatchId);

                    if (_updateNetworkGroup != null)
                        harmony.Unpatch(_updateNetworkGroup, HarmonyPatchType.Prefix, HarmonyPatchId);
                    
                    Log("Harmony patches removed and cache cleared successfully!", LogLevel.INFO);
                }
            }
            catch (System.Exception ex)
            {
                Log($"Cleaning up Harmony patches!", LogLevel.ERROR);
                Log($"Error: {ex.Message}", LogLevel.ERROR);
            }
            Instance = null;
        }

        #endregion Plugin Load/Unload

        #region Misc Carbon/Oxide Hooks

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            // Prevent people putting heli signals in Tin Can Alarm
            if (container?.entityOwner is not TinCanAlarm tinCanAlarm)
                return null;

            if (HeliProfileCache.ContainsKey(item.skin))
                return ItemContainer.CanAcceptResult.CannotAccept;

            return null;
        }

        private object CanTeleportDeepSea(BasePlayer player, DeepSeaPortal portal)
        {
            if (player == null)
                return null;

            List<PatrolHelicopter> helis = GetPlayerActiveHelis(player);
            
            if (helis.Count > 0 && portal?.PortalMode == DeepSeaPortal.PortalModeEnum.Entrance)
                Message(player, "NoDeepSeaTeleportWarn");
            
            return null;
        }

        private object OnDeepSeaTeleport(TriggerDeepSeaPortal triggerDeepSeaPortal, BasePlayer player)
        {
            if (player == null)
                return null;

            List<PatrolHelicopter> helis = GetPlayerActiveHelis(player);
            
            if (helis.Count > 0 && triggerDeepSeaPortal.Portal?.PortalMode == DeepSeaPortal.PortalModeEnum.Entrance)
            {
                //Message(player, "NoDeepSeaTeleportRetire");

                IPlayer iplayer = GetIPlayer(player);
                if (iplayer == null)
                    return null;

                CmdRetireHeli(iplayer, config.heli.retireCommand, null);
            }

            return null;
        }

        private object OnEventTrigger(TriggeredEventPrefab eventPrefab)
        {
            if (eventPrefab.name.Contains("event_helicopter") && config.options.noVanillaHeli)
            {
                timer.Once(1f, () =>
                {
                    if (eventPrefab != null)
                        eventPrefab.Kill();
                });
                return true;
            }

            return null;
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            // Re-Adds supply signal names to items added via kits/loadouts by plugins
            // which don't specify a custom item display name.

            if (item == null || container == null)
                return;

            if (item.info?.itemid == supplySignalId)
                NextTick(() => CheckAndFixSignal(item));
        }

        private object OnExplosiveThrown(BasePlayer player, SupplySignal entity, ThrownWeapon item)
        {
            if (entity == null || entity.IsDestroyed || player == null)
                return null;

            if (!HeliProfileCache.TryGetValue(entity.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                return null;

            entity.EntityToCreate = null;
            entity.CancelInvoke(entity.Explode);
            HeliSignalThrown(player, entity, heliProfile);

            return null;
        }

        private void OnExplosiveDropped(BasePlayer player, SupplySignal entity, ThrownWeapon item) => OnExplosiveThrown(player, entity, item);

        private object CanStackItem(Item item, Item targetItem)
        {
            if (item == null || targetItem == null)
                return null;

            if (HeliProfileCache.ContainsKey(item.skin))
            {
                // Only act on Heli Signal supply drops
                if (item.info.itemid == targetItem.info.itemid && item.skin != targetItem.skin)
                    return false;
            }
            return null;
        }

        private object CanCombineDroppedItem(DroppedItem droppedItem, DroppedItem targetItem)
        {
            if (droppedItem == null || targetItem == null)
                return null;

            if (HeliProfileCache.ContainsKey(droppedItem.item.skin))
            {
                // Only act on Heli Signal supply drops
                if (droppedItem.item.info.itemid == targetItem.item.info.itemid && droppedItem.item.skin != targetItem.item.skin)
                    return true;
            }
            return null;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null)
                return;

            if (entity.ShortPrefabName.Equals("napalm"))
            {
                // Set the owner ID & name of fire/napalm from heli rockets to stop NRE which occurs in
                // OnEntityTakeDamage from left over napalm fire after heli is destroyed.
                NextTick(() =>
                {
                    FireBall napalm = entity as FireBall;
                    if (napalm == null || napalm.creatorEntity == null)
                        return;     

                    if (!HeliProfileCache.ContainsKey(napalm.skinID))
                        return;            

                    napalm.OwnerID = napalm.creatorEntity.OwnerID;
                    napalm._name = napalm.creatorEntity._name;
                    napalm.skinID = napalm.creatorEntity.skinID;
                });
            }
            else if (entity.ShortPrefabName.Equals("heli_crate"))
            {
                entity.UpdateNetworkGroup();
                entity.SendNetworkUpdateImmediate();

                timer.Once(0.25f, () =>
                {
                    LockedByEntCrate crate = entity as LockedByEntCrate;
                    if (crate == null)
                        return;

                    // Fix for crates that fall on Unloadable train cars from being unlootable
                    // This issue also affects vanilla heli, so also fixes that. Hackable crates are not affected.
                    CrateComp crateComp = crate.gameObject.AddComponent<CrateComp>();
                    crateComp.crate = crate;
                    
                    bool isHeliSignalCrate = crate.skinID != 0;
                    bool isVanillaHeliCrate = crate.skinID == 0;

                    if (isHeliSignalCrate && (!config.heli.buoyantHeliCrates || !HeliProfileCache.ContainsKey(crate.skinID)))
                        return;

                    if (isVanillaHeliCrate && !config.heli.buoyantVanillaCrates)
                        return;

                    Buoyancy buoyancy = crate.gameObject.AddComponent<Buoyancy>();
                    buoyancy.buoyancyScale = 1;
                    buoyancy.rigidBody = crate.gameObject.GetComponent<Rigidbody>();
                    buoyancy.SavePointData(true);
                });
            }
            else if (entity.ShortPrefabName.Equals("codelockedhackablecrate"))
            {
                timer.Once(0.25f, () =>
                {
                    var crate = entity as HackableLockedCrate;
                    if (crate == null)
                        return;
                        
                    if (!config.heli.buoyantHackableCrates)
                        return;
                    
                    if (!HeliProfileCache.ContainsKey(crate.skinID) || !LockedCrates.ContainsKey(crate.net.ID.Value))
                        return;
                    
                    Buoyancy buoyancy = crate.gameObject.AddComponent<Buoyancy>();
                    buoyancy.buoyancyScale = 1;
                    buoyancy.rigidBody = crate.gameObject.GetComponent<Rigidbody>();
                    buoyancy.SavePointData(true);
                });
            }
        }

        #endregion Misc Carbon/Oxide Hooks

        #region Damage/Kill Hooks

        private object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (player == null || info?.Initiator == null || info.damageTypes == null)
                return null;

            var Initiator = info.Initiator;

            if (Initiator is PatrolHelicopter)
            {
                PatrolHelicopter heli = Initiator as PatrolHelicopter;
                if (heli == null || heli.IsDestroyed)
                    return null;

                if (heli.net?.ID == null)
                    return null;

                if (!HeliSignalData.ContainsKey(heli.net.ID.Value))
                    return null;

                if (!HeliProfileCache.TryGetValue(heli.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                    return null;

                var heliOwnerId = heli.OwnerID;

                if (config.heli.heliConfig.TryGetValue(heliProfile, out HeliData heliData) && heliData != null)
                {
                    if (heliData.TargetingDamage.BlockPlayerDamage && !IsOwnerOrFriend(heliOwnerId, player.userID))
                    {
                        info.damageTypes.Clear();
                        return true;
                    }

                    if (info.damageTypes.GetMajorityDamageType() == DamageType.Bullet)
                    {
                        float hitProb = (float)random.Next(0, 100);
                        if (heliData.Weapons.BulletAccuracy < hitProb)
                        {
                            info.damageTypes.Clear();
                            return true;
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(Initiator.ShortPrefabName) && Initiator.ShortPrefabName.Equals("napalm"))
            {
                if (!HeliProfileCache.TryGetValue(Initiator.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                    return null;

                if (config.heli.heliConfig.TryGetValue(heliProfile, out HeliData heliData) && heliData != null)
                {
                    var heliOwnerId = Initiator.OwnerID;
                    if (heliData.TargetingDamage.BlockPlayerDamage && !IsOwnerOrFriend(heliOwnerId, player.userID))
                    {
                        info.damageTypes.Clear();
                        return true;
                    }
                }
            }
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.Initiator == null || entity == null || info.damageTypes == null)
                return null;

            var initiator = info.Initiator;
            var ownerId = entity.OwnerID;

            if (!ownerId.IsSteamId())
                return null;

            if (initiator is PatrolHelicopter heli)
            {
                if (heli.net?.ID == null || !HeliSignalData.ContainsKey(heli.net.ID.Value))
                    return null;

                if (!HeliProfileCache.TryGetValue(heli.skinID, out string heliProfile) ||
                    string.IsNullOrEmpty(heliProfile))
                    return null;

                if (config?.heli?.heliConfig?.TryGetValue(heliProfile, out HeliData heliData) != true ||
                    heliData == null)
                    return null;

                if (heliData.TargetingDamage.BlockProtectedList &&
                    config?.heli?.protectedPrefabs?.Contains(entity.name) == true)
                {
                    info.damageTypes.Clear();
                    return true;
                }

                if (heliData.TargetingDamage.BlockOwnerDamage && heliData.TargetingDamage.BlockOtherDamage)
                {
                    info.damageTypes.Clear();
                    return true;
                }

                bool isOwnerOrFriend = IsOwnerOrFriend(heli.OwnerID, ownerId);

                if (heliData.TargetingDamage.BlockOtherDamage && !isOwnerOrFriend && !IsDamageAuthorized(heli, entity))
                {
                    info.damageTypes.Clear();
                    return true;
                }

                if (heliData.TargetingDamage.BlockOwnerDamage && isOwnerOrFriend)
                {
                    info.damageTypes.Clear();
                    return true;
                }
            }
            else if (initiator.ShortPrefabName?.Equals("napalm", StringComparison.Ordinal) == true)
            {
                if (!HeliProfileCache.TryGetValue(initiator.skinID, out string heliProfile) ||
                    string.IsNullOrEmpty(heliProfile))
                    return null;

                if (config?.heli?.heliConfig?.TryGetValue(heliProfile, out HeliData heliData) != true ||
                    heliData == null)
                    return null;

                if (heliData.TargetingDamage.BlockProtectedList &&
                    config?.heli?.protectedPrefabs?.Contains(entity.name) == true)
                {
                    info.damageTypes.Clear();
                    return true;
                }

                bool isOwnerOrFriend = IsOwnerOrFriend(initiator.OwnerID, ownerId);

                if (heliData.TargetingDamage.BlockOtherDamage && !isOwnerOrFriend)
                {
                    info.damageTypes.Clear();
                    return true;
                }

                if (heliData.TargetingDamage.BlockOwnerDamage && isOwnerOrFriend)
                {
                    info.damageTypes.Clear();
                    return true;
                }
            }

            return null;
        }

        private object OnPatrolHelicopterTakeDamage(PatrolHelicopter heli, HitInfo info)
        {
            if (heli?.net?.ID == null || info?.Initiator == null || info.damageTypes == null ||
                info.Initiator == heli)
                return null;

            var initiator = info.Initiator;
            var heliAI = heli.myAI;
            var heliId = heli.net.ID.Value;
            var ownerId = heli.OwnerID;

            bool isVanilla = false;
            string heliDisplayName = string.Empty;

            if (!HeliProfileCache.TryGetValue(heli.skinID, out string heliProfile))
                heliProfile = null;

            if ((string.IsNullOrEmpty(heliProfile) || heliProfile.Contains("patrolhelicopter")) && heli.skinID == 0)
            {
                if (config?.announce?.reportVanilla != true)
                    return null;

                isVanilla = true;
                heliDisplayName = config.announce.vanillaName ?? "Patrol Helicopter";
            }
            else if (string.IsNullOrEmpty(heliProfile) && heli.skinID != 0)
            {
                return null;
            }

            //HeliComponent heliComp = heli.GetComponent<HeliComponent>();
            HeliComponent heliComp = GetCachedHeliSignal(heliAI);
            if (!isVanilla && heliComp == null)
                return null;

            if (heliComp?.isRetiring == true)
            {
                info.damageTypes.Clear();
                return null;
            }

            ulong attackerId = 0;
            BasePlayer attacker = null;
            AutoTurret turret = null;

            if (initiator is BasePlayer player)
            {
                attacker = player;
                attackerId = attacker.userID;
            }
            else if (initiator is AutoTurret autoTurret)
            {
                turret = autoTurret;
                var turretPlayerId = turret.ControllingViewerId?.SteamId;

                if (turretPlayerId == null || turretPlayerId == 0)
                    return null;

                attacker = BasePlayer.FindByID(turretPlayerId.Value);
                if (attacker == null)
                    return null;

                attackerId = attacker.userID;

                if (config?.heli?.allowTurretDamage != true)
                {
                    info.damageTypes.Clear();
                    var station = attacker.GetMounted() as ComputerStation;
                    if (station != null)
                    {
                        timer?.Once(0.25f, () => Effect.server.Run(shockSound, station.transform.position));
                        timer?.Once(0.5f, () => Effect.server.Run(deniedSound, station.transform.position));
                        attacker.EnsureDismounted();
                        Message(attacker, "NoTurret", heliDisplayName);
                    }
                    return true;
                }
            }
            else
            {
                return null;
            }

            if (attacker == null)
                return null;

            if (isVanilla && heliAI != null && (heliAI.isRetiring || heliAI.isDead))
                return null;

            if (!isVanilla)
            {
                if (heliComp == null)
                    return null;

                heliDisplayName = heliComp.heliProfile;

                if (config.heli.heliConfig[heliProfile].TargetingDamage.OwnerDamage && !IsOwnerOrFriend(attackerId, ownerId))
                {
                    info.damageTypes.Clear();
                    Message(attacker, "CannotDamage", heliDisplayName);
                    return true;
                }

                heliComp.MaybeMoveToAttacker(attacker);
            }

            if (!HeliSignalData.ContainsKey(heliId))
            {
                HeliSignalData[heliId] = new HeliStats
                {
                    OwnerID = ownerId,
                    OwnerName = isVanilla ? (config.announce.vanillaOwner ?? "Unknown") :
                            (heliComp.owner.displayName ?? "Unknown"),
                    Attackers = new Dictionary<ulong, AttackersStats>()
                };
            }

            if (!HeliSignalData[heliId].Attackers.ContainsKey(attackerId))
            {
                HeliSignalData[heliId].Attackers[attackerId] = new AttackersStats
                {
                    Name = attacker.displayName ?? "Unknown Player"
                };
            }

            if (!isVanilla && heliComp != null)
            {
                if (heliComp.isDying)
                {
                    info.damageTypes.Clear();
                    return true;
                }

                var damageAmount = info.damageTypes.Total();

                if (initiator is AutoTurret && turret != null)
                {
                    HeliSignalData[heliId].LastTurretAttacker = turret;
                    HeliSignalData[heliId].TurretDamage += damageAmount;
                    HeliSignalData[heliId].Attackers[attackerId].TurretDamage += damageAmount;

                    if (config?.heli?.heliTargetTurret == true && heliAI != null &&
                        config.heli.turretCooldown > 0 &&
                        (Time.realtimeSinceStartup - heliComp.turretCooldown > config.heli.turretCooldown))
                    {
                        if (!heliComp.isStrafingTurret &&
                            HeliSignalData[heliId].TurretDamage > HeliSignalData[heliId].PlayerDamage)
                        {
                            heliComp.turretCooldown = Time.realtimeSinceStartup;
                            heliComp.isStrafing = true;
                            heliComp.isStrafingTurret = true;
                            heliAI.ExitCurrentState();
                            heliAI.State_Strafe_Enter(attacker, false);
                        }
                    }
                }
                else
                {
                    HeliSignalData[heliId].PlayerDamage += damageAmount;
                }

                var maxDist = config?.heli?.maxHitDistance ?? 0;
                if (maxDist > 0)
                {
                    var dist = Vector3.Distance(heli.transform.position, attacker.transform.position);
                    if (dist > maxDist)
                    {
                        info.damageTypes.Clear();
                        Message(attacker, "TooFarAway", heliDisplayName, maxDist);
                        return true;
                    }
                }

                if (config?.heli?.RetireWarning == true &&
                    attacker.IsBuildingBlocked() &&
                    IsOwnerOrFriend(attackerId, ownerId))
                {
                    HeliSignalData[heliId].WarningLevel++;
                    var warningThreshold = config.heli.WarningThreshold;

                    if (!heliComp.isRetiring && HeliSignalData[heliId].WarningLevel >= warningThreshold)
                    {
                        heliComp.isRetiring = true;
                        info.damageTypes.Clear();
                        RetireHeli(heliAI, heliId, 0f);
                        Message(attacker, "RetireHeli", heliDisplayName, HeliSignalData[heliId].WarningLevel, warningThreshold);
                        return true;
                    }

                    info.damageTypes.Clear();
                    if (!heliComp.isRetiring)
                        Message(attacker, "RetireWarn", heliDisplayName, HeliSignalData[heliId].WarningLevel, warningThreshold);
                    return true;
                }
            }

            if (HeliSignalData[heliId].FirstHitTime == 0)
                HeliSignalData[heliId].FirstHitTime = Time.realtimeSinceStartup;

            HeliSignalData[heliId].LastAttacker = attacker;

            var totalDamage = info.damageTypes.Total();
            var actualDamage = totalDamage > heli._health ? heli._health : totalDamage;

            HeliSignalData[heliId].Attackers[attackerId].DamageDealt += actualDamage;
            HeliSignalData[heliId].Attackers[attackerId].TotalHits++;

            if (info.HitMaterial == 2306822461)
            {
                HeliSignalData[heliId].Attackers[attackerId].RotorHits++;

                if (heli.weakspots?.Length >= 2)
                {
                    PatrolHelicopter.weakspot weakspot = null;
                    if (info.HitBone == MAIN_ROTOR_COL || info.HitBone == ENGINE_COL)
                        weakspot = heli.weakspots[0];
                    else if (info.HitBone == TAIL_COL)
                        weakspot = heli.weakspots[1];
                }
            }

            return null;
        }

        private object OnPatrolHelicopterKill(PatrolHelicopter heli, HitInfo info)
        {
            if (heli == null || heli.IsDestroyed || heli.myAI == null)
                return null;
        
            //HeliComponent heliComp = heli?.GetComponent<HeliComponent>();
            HeliComponent heliComp = GetCachedHeliSignal(heli.myAI);
            if (heliComp == null)
                return null;

            var heliProfile = heliComp.heliProfile;
            if (config.heli.heliConfig.ContainsKey(heliProfile))
            {
                if (info.damageTypes.Total() >= heli.health && !heli.myAI.isDead)
                {
                    // Prevent heli blowing up in mid air before crash spiral under certain conditions
                    heli.health = config.heli.heliConfig[heliProfile].Init.Health;
                    heli.myAI.forceTerrainPushback = false;
                    heli.myAI.CriticalDamage();
                    return true;
                }
            }
            return null;
        }

        private object OnEntityKill(PatrolHelicopter heli)
        {
            if (heli?.net?.ID == null || heli.myAI?.isRetiring == true || !HeliSignalData.ContainsKey(heli.net.ID.Value))
                return null;

            bool isVanilla = false;
            var skinId = heli.skinID;
            var heliId = heli.net.ID.Value;
            var position = heli.transform.position;
            var ownerId = heli.OwnerID;
            string heliProfile = heli._name;
            
            if (HeliProfileCache.TryGetValue(skinId, out string cachedProfile))
                heliProfile = cachedProfile;

            if ((heliProfile == null && skinId == 0) || (!string.IsNullOrEmpty(heliProfile) && heliProfile.Contains("patrolhelicopter") && skinId == 0))
            {
                isVanilla = true;
            }
            else if (heliProfile == null && skinId != 0)
            {
                return null;
            }

            string heliDisplayName = string.Empty;
            if (isVanilla)
            {
                heliDisplayName = config.announce.vanillaName;
            }
            else if (!string.IsNullOrEmpty(heliProfile) && config.heli.heliConfig.ContainsKey(heliProfile))
            {
                heliDisplayName = config.heli.heliConfig[heliProfile].Init.HeliName;
            }

            if (string.IsNullOrEmpty(heliDisplayName))
                heliDisplayName = "Patrol helicopter";

            var gridPos = PositionToGrid(position);
            if (string.IsNullOrEmpty(gridPos))
                gridPos = "Unknown";

            if (!HeliSignalData.TryGetValue(heliId, out var _heliSignalData) || _heliSignalData?.LastAttacker == null)
                return null;

            BasePlayer lastAttacker = _heliSignalData.LastAttacker;

            if (config.announce.killChat)
            {
                if (!isVanilla || (isVanilla && config.announce.killVanilla))
                {
                    var firstHitTime = _heliSignalData.FirstHitTime;
                    if (firstHitTime > 0)
                    {
                        TimeSpan timeSpan = TimeSpan.FromSeconds(Time.realtimeSinceStartup - firstHitTime);
                        string time = timeSpan.ToString(@"hh\:mm\:ss");
                        var message = string.Format(lang.GetMessage("HeliKilledTime", this, lastAttacker.UserIDString),
                                                    heliDisplayName, lastAttacker.displayName ?? "Unknown", gridPos, time);

                        AnnounceToChat(lastAttacker, message);
                    }
                }
            }

            //var heliComp = heli.GetComponent<HeliComponent>();
            HeliComponent heliComp = GetCachedHeliSignal(heli.myAI);

            // Damage report
            if (config.announce.reportChat)
            {
                if (!isVanilla || (isVanilla && config.announce.reportVanilla))
                {
                    var heliOwnerName = config.announce.vanillaOwner ?? "Unknown";
                    if (!isVanilla && heliComp.owner.displayName != null)
                        heliOwnerName = heliComp.owner.displayName;

                    var _heliAttackers = _heliSignalData.Attackers;
                    if (_heliAttackers != null && _heliAttackers.Count > 0)
                    {
                        string topReport = string.Empty;
                        string ownerReport = string.Format(lang.GetMessage("DmgReportOwner", this, lastAttacker.UserIDString),
                                                           heliDisplayName, heliOwnerName, "Killed");
                        int count = 1;
                        int maxReported = config.announce.maxReported;

                        var sortedAttackers = new List<ulong>(_heliAttackers.Keys);
                        sortedAttackers.Sort((a, b) => (_heliAttackers[b]?.DamageDealt ?? 0).CompareTo(_heliAttackers[a]?.DamageDealt ?? 0));

                        foreach (var key in sortedAttackers)
                        {
                            if (count >= maxReported)
                                break;

                            var attackerStats = _heliAttackers[key];
                            if (attackerStats == null)
                                continue;

                            string playerName = attackerStats.Name ?? "Unknown";
                            float damageDealt = attackerStats.DamageDealt;
                            int totalHits = attackerStats.TotalHits;
                            int rotorHits = attackerStats.RotorHits;
                            float turretDamage = attackerStats.TurretDamage;

                            double rotorAccuracy = totalHits > 0 ? ((double)rotorHits / (double)totalHits) * 100 : 0;
                            double damageRatio = damageDealt > 0 ? ((double)turretDamage / (double)damageDealt) * 100 : 0;

                            topReport += string.Format(lang.GetMessage("DmgReportIndex", this, lastAttacker.UserIDString),
                                                       count, playerName, Math.Round(damageDealt, 2),
                                                       Math.Round(rotorAccuracy, 2), Math.Round(damageRatio, 2));
                            count++;
                        }

                        var dmgReport = string.Format(lang.GetMessage("DmgReport", this, lastAttacker.UserIDString),
                                                      ownerReport, topReport);

                        AnnounceToChat(lastAttacker, dmgReport);
                    }
                }
            }

            if (!isVanilla)
            {
                // Discord notification
                if (config.discord.sendHeliKill)
                {
                    var firstHitTime = _heliSignalData.FirstHitTime;
                    if (firstHitTime > 0)
                    {
                        TimeSpan timeSpan = TimeSpan.FromSeconds(Time.realtimeSinceStartup - firstHitTime);
                        string time = timeSpan.ToString(@"hh\:mm\:ss");

                        // Check if this is part of a wave or multi
                        bool isWaveKill = false;
                        bool isMultiKill = false;
                        int wavePosition = 0;
                        int waveTotalCount = 0;

                        if (heliComp?.isWaveHeli == true && !string.IsNullOrEmpty(heliComp.waveProfile))
                        {
                            isWaveKill = true;

                            // Calculate wave position based on how many helis have been destroyed
                            foreach (var waveKey in WavesCalled.Keys)
                            {
                                if (WavesCalled[waveKey]?.Contains(heliId) == true)
                                {
                                    var waveHelis = WavesCalled[waveKey];
                                    if (config.heli.waveConfig.TryGetValue(heliComp.waveProfile, out var waveConfig) == true &&
                                        waveConfig.WaveProfiles != null)
                                    {
                                        waveTotalCount = waveConfig.WaveProfiles.Count;
                                        wavePosition = waveTotalCount - (heliComp.waveProfileCache?.Count ?? 0) + 1;
                                    }
                                    break;
                                }
                            }
                        }
                        else if (_heliSignalData.IsMultiHeli)
                        {
                            isMultiKill = true;
                            // Use the actual position from HeliSignalData
                            wavePosition = _heliSignalData.MultiPosition;
                            waveTotalCount = _heliSignalData.MultiTotalCount;
                        }

                        SendDiscordKillReport(heli, _heliSignalData, heliDisplayName, position, gridPos,
                                            lastAttacker, time, isWaveKill, wavePosition, waveTotalCount, isMultiKill);
                    }
                }

                if (config.heli.heliConfig.TryGetValue(heliProfile, out var _heliProfile))
                {
                    if (_heliProfile.Crates.CratesToSpawn > 0)
                        SpawnHeliCrates(heli, lastAttacker, ownerId, skinId, heliProfile, position);

                    if (_heliProfile.Crates.LockedCratesToSpawn > 0)
                        SpawnLockedCrates(heli, lastAttacker, ownerId, skinId, heliProfile, position);

                    RunHeliDeathCommands(_heliProfile.CommandsToRun, ownerId, heliProfile, heliComp);
                }

                NextTick(() =>
                {
                    var ents = Pool.Get<List<BaseEntity>>();
                    try
                    {
                        Vis.Entities(position, 15f, ents);
                        if (ents?.Count > 0)
                        {
                            foreach (var ent in ents)
                            {
                                if (ent != null && (ent is ServerGib || ent is FireBall))
                                {
                                    if (ent.OwnerID != 0)
                                        continue;

                                    ent.OwnerID = ownerId;
                                    ent.skinID = skinId;
                                    ent._name = heliProfile;

                                    if (ent is ServerGib || ent is LockedByEntCrate)
                                    {
                                        // Adding box collider & changing rb values to help stop
                                        // gibs & crates falling through map on occasion
                                        var entGameObject = ent.gameObject;
                                        if (entGameObject != null)
                                        {
                                            BoxCollider box = entGameObject.AddComponent<BoxCollider>();
                                            if (box != null)
                                                box.size = new Vector3(0.6f, 0.6f, 0.6f);

                                            Rigidbody rigidbody = entGameObject.GetComponent<Rigidbody>();
                                            if (rigidbody != null)
                                            {
                                                rigidbody.linearDamping = 0.5f;
                                                rigidbody.angularDamping = 0.5f;
                                                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                                            }
                                        }
                                    }
                                    ProcessHeliEnt(ent, heliProfile);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Pool.FreeUnmanaged(ref ents);
                    }
                });

                // Handle bot respawn
                if (BotReSpawn != null && !string.IsNullOrEmpty(heliProfile) && config?.heli?.heliConfig?.TryGetValue(heliProfile, out var heliConfigForBots) == true)
                {
                    var botReSpawnProfile = heliConfigForBots?.BotReSpawn.BotReSpawnProfile;
                    if (!string.IsNullOrEmpty(botReSpawnProfile))
                    {
                        BotReSpawn.Call("AddGroupSpawn", position, botReSpawnProfile, $"{botReSpawnProfile}Group", 0);
                    }
                }

                Interface.CallHook("OnHeliSignalHeliKill", heli);
            }

            // Clean up helicopter data after delay
            timer.Once(15f, () =>
            {
                if (HeliSignalData.ContainsKey(heliId))
                    HeliSignalData.Remove(heliId);
            });

            return null;
        }

        #endregion Damage/Kill Hooks

        #region Heli Hooks

        private object CanHelicopterStrafeTarget(PatrolHelicopterAI heliAI, BasePlayer player)
        {
            string heliProfile;
            HeliProfileCache.TryGetValue(heliAI.helicopterBase.skinID, out heliProfile);
            if (heliProfile == null)
                return null;

            if (!config.heli.heliConfig[heliProfile].TargetingDamage.TargetOtherPlayers && !IsOwnerOrFriend(player.userID, heliAI.helicopterBase.OwnerID))
            {
                heliAI.ExitCurrentState();
                for (int i = 0; i < heliAI._targetList.Count; i++)
                {
                    if (heliAI._targetList[i].ply == player)
                    {
                        heliAI.ClearAimTarget();
                        heliAI._targetList.Remove(heliAI._targetList[i]);
                        continue;
                    }
                }
                return false;
            }
            return null;
        }

        private object CanHelicopterStrafe(PatrolHelicopterAI heliAI)
        {
            HeliComponent heliComp = heliAI?.helicopterBase.GetComponent<HeliComponent>();
            if (heliComp != null)
            {
                if (heliComp.isRetiring || heliComp.isDying)
                    return false;
                else if (heliComp.isStrafing)
                    return false;
                else if (Time.realtimeSinceStartup - heliAI.lastStrafeTime < config.heli.heliConfig[heliComp.heliProfile].Weapons.StrafeCooldown)
                    return false;
                else
                    return true;
            }
            return null;
        }

        private object OnHelicopterStrafeEnter(PatrolHelicopterAI heliAI, Vector3 strafePosition, BasePlayer strafeTarget)
        {
            HeliComponent heliComp = heliAI?.helicopterBase.GetComponent<HeliComponent>();
            if (heliComp != null)
            {
                if (heliComp.isStrafingTurret)
                {
                    var _heliId = HeliSignalData[heliComp.heliId];
                    if (_heliId.LastTurretAttacker == null)
                        return null;

                    Vector3 turretPos = _heliId.LastTurretAttacker.transform.position;
                    heliAI.strafe_target_position = turretPos;
                    heliAI.lastStrafeTime = Time.realtimeSinceStartup;
                    heliAI._currentState = PatrolHelicopterAI.aiState.STRAFE;
                    heliAI.numRocketsLeft = config.heli.heliConfig[heliComp.heliProfile].Weapons.MaxHeliRockets + 1;
                    heliAI.lastRocketTime = 0f;
                    Vector3 randomOffset = GetRandomOffset(turretPos, 175f, 192.5f, 30f, 40f);
                    heliAI.SetTargetDestination(randomOffset, 10f, 30f);
                    heliAI.SetIdealRotation(heliAI.GetYawRotationTo(randomOffset), -1f);
                    heliAI.puttingDistance = true;
                    heliComp.isStrafing = true;

                    _heliId.TurretDamage = 0f;
                    _heliId.PlayerDamage = 0f;
                    _heliId.LastTurretAttacker = null;
                    return true;
                }
                else
                {
                    heliAI.strafe_target = strafeTarget;
                    heliAI.get_out_of_strafe_distance = Random.Range(13f, 17f);
                    if (heliComp.UseNapalm())
                    {
                        heliAI.passNapalm = true;
                        heliAI.useNapalm = true;
                        heliAI.lastNapalmTime = Time.realtimeSinceStartup;
                    }
                    heliAI.lastStrafeTime = Time.realtimeSinceStartup;
                    heliAI._currentState = PatrolHelicopterAI.aiState.STRAFE;
                    heliAI.RefreshTargetPosition();
                    heliAI.numRocketsLeft = config.heli.heliConfig[heliComp.heliProfile].Weapons.MaxHeliRockets + 1;
                    heliAI.lastRocketTime = 0f;
                    heliAI.movementLockingAiming = true;
                    Vector3 randomOffset = GetRandomOffset(heliAI.strafe_target_position, 175f, 192.5f, 30f, 40f);
                    heliAI.SetTargetDestination(randomOffset, 10f, 30f);
                    heliAI.SetIdealRotation(heliAI.GetYawRotationTo(randomOffset), -1f);
                    heliAI.puttingDistance = true;
                }
                NextTick(() =>
                {
                    heliAI.numRocketsLeft = config.heli.heliConfig[heliComp.heliProfile].Weapons.MaxHeliRockets + 1;
                    heliComp.UpdateSerializeableFields(heliAI.numRocketsLeft);
                });
                return true;
            }
            return null;
        }

        private object CanHelicopterUseNapalm(PatrolHelicopterAI heliAI)
        {
            HeliComponent heliComp = heliAI?.helicopterBase.GetComponent<HeliComponent>();
            if (heliComp != null)
            {
                var heliProfile = heliComp.heliProfile;
                if (heliProfile == null)
                    return null;

                if (heliComp.isRetiring || heliComp.isDying)
                    return false;

                return true;
            }
            return null;
        }

        private object OnHelicopterTarget(HelicopterTurret turret, BasePlayer player)
        {
            PatrolHelicopter heli = turret._heliAI?.helicopterBase;
            if (heli == null)
                return null;

            HeliProfileCache.TryGetValue(heli.skinID, out string heliProfile);
            if (heliProfile == null)
                return null;

            if (config.heli.heliConfig.ContainsKey(heliProfile))
                return OnCustomHelicopterTarget(turret, player, heliProfile);

            return null;
        }

        private object CanHelicopterTarget(PatrolHelicopterAI heliAI, BasePlayer player)
        {
            return HeliProfileCache.TryGetValue(heliAI.helicopterBase.skinID, out string heliProfile) 
                && !string.IsNullOrEmpty(heliProfile)
                ? CanCustomHeliTarget(heliAI, player, heliProfile)
                : null;
        }

        private object OnHelicopterRetire(PatrolHelicopterAI heliAI)
        {
            PatrolHelicopter heli = heliAI?.helicopterBase;
            if (heli == null) return null;

            if (!HeliProfileCache.TryGetValue(heli.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                return null;

            Interface.CallHook("OnHeliSignalHeliRetire", heli);

            if (heliAI != null && heliAI.IsAlive())
            {
                HeliComponent heliComp = heli.GetComponent<HeliComponent>();
                if (heliComp == null)
                    return null;

                heliComp.isRetiring = true;
                BasePlayer owner = heliComp.owner;
                heliAI.leftGun.maxTargetRange = 0f;
                heliAI.rightGun.maxTargetRange = 0f;

                List<BasePlayer> attackers = GetAttackingTeam(owner);
                if (attackers == null || attackers.Count == 0) return null;

                if (!heliComp.retireCmdUsed)
                {
                    if (heliComp.isWaveHeli)
                    {
                        var message = Lang("WaveRetired", heliProfile);
                        AnnounceToChat(owner, message);
                    }
                    else
                    {
                        var message = Lang("HeliRetired", heliProfile);
                        AnnounceToChat(owner, message);
                    }
                }

                if (config.announce.reportChat && config.announce.reportRetire)
                {
                    var heliId = heli.net.ID.Value;
                    BasePlayer lastAttacker = HeliSignalData[heliId].LastAttacker;
                    if (lastAttacker != null)
                    {
                        var _heliAttackers = HeliSignalData[heliId].Attackers;
                        string topReport = string.Empty;
                        string ownerReport = string.Format(lang.GetMessage("DmgReportOwner", this, lastAttacker.UserIDString), config.heli.heliConfig[heliProfile].Init.HeliName, owner.displayName, "Retired");
                        int count = 1;

                        var sortedAttackers = new List<ulong>(_heliAttackers.Keys);
                        sortedAttackers.Sort((a, b) => _heliAttackers[b].DamageDealt.CompareTo(_heliAttackers[a].DamageDealt));

                        foreach (var key in sortedAttackers)
                        {
                            if (count >= config.announce.maxReported)
                                break;

                            string playerName = _heliAttackers[key].Name;
                            float damageDealt = _heliAttackers[key].DamageDealt;
                            int totalHits = _heliAttackers[key].TotalHits;
                            int rotorHits = _heliAttackers[key].RotorHits;
                            float turretDamage = _heliAttackers[key].TurretDamage;
                            double rotorAccuracy = ((double)rotorHits / (double)totalHits) * 100;
                            double damageRatio = ((double)_heliAttackers[key].TurretDamage / (double)_heliAttackers[key].DamageDealt) * 100;
                            topReport += string.Format(lang.GetMessage("DmgReportIndex", this, lastAttacker.UserIDString), count, playerName, Math.Round(damageDealt, 2), Math.Round(rotorAccuracy, 2), Math.Round(damageRatio, 2));
                            count++;
                        }
                        var dmgReport = string.Format(lang.GetMessage("DmgReport", this, lastAttacker.UserIDString), ownerReport, topReport);
                        AnnounceToChat(lastAttacker, dmgReport);
                    }
                }

                if (config.discord.sendHeliRetire)
                {
                    var gridPos = PositionToGrid(heli.transform.position);
                    if (gridPos == null)
                        gridPos = "Unknown";

                    var heliId = heli.net.ID.Value;
                    if (!HeliSignalData.ContainsKey(heliId))
                        return null;

                    var _heliSignalData = HeliSignalData[heliId];

                    // Check if this was retired by command (already handled)
                    if (heliComp.retireCmdUsed)
                    {
                        // Check if already processed
                        if (_heliSignalData.IsMultiHeli && ProcessedRetirements.Contains(_heliSignalData.MultiHeliTime))
                            return null;
                        if (heliComp.isWaveHeli && ProcessedRetirements.Contains(_heliSignalData.WaveTime))
                            return null;
                    }
                    else
                    {
                        // This is an auto-retire (time), collect helis for grouped notification
                        if (_heliSignalData.IsMultiHeli)
                        {
                            float multiTime = _heliSignalData.MultiHeliTime;
                            if (!AutoRetiringMultiHelis.ContainsKey(multiTime))
                                AutoRetiringMultiHelis[multiTime] = new List<PatrolHelicopter>();
                            
                            if (!AutoRetiringMultiHelis[multiTime].Contains(heli))
                                AutoRetiringMultiHelis[multiTime].Add(heli);
                            
                            // Check if all helis in this multi event are retiring
                            int totalMultiHelis = _heliSignalData.MultiTotalCount;
                            if (AutoRetiringMultiHelis[multiTime].Count >= totalMultiHelis)
                            {
                                // All helis are retiring, send grouped notification
                                ProcessedRetirements.Add(multiTime);
                                SendGroupedRetireDiscord(AutoRetiringMultiHelis[multiTime], null, false, true);
                                
                                // Cleanup
                                AutoRetiringMultiHelis.Remove(multiTime);
                                timer.Once(30f, () => ProcessedRetirements.Remove(multiTime));
                            }
                            
                            return null; // Don't send individual notification
                        }
                        else if (heliComp.isWaveHeli)
                        {
                            float waveTime = _heliSignalData.WaveTime;
                            if (!AutoRetiringWaveHelis.ContainsKey(waveTime))
                                AutoRetiringWaveHelis[waveTime] = new List<PatrolHelicopter>();
                            
                            if (!AutoRetiringWaveHelis[waveTime].Contains(heli))
                                AutoRetiringWaveHelis[waveTime].Add(heli);
                            
                            // For waves, need to check if all active helis are retiring
                            bool allActiveRetiring = true;
                            int activeHeliCount = 0;
                            
                            if (WavesCalled.ContainsKey(waveTime))
                            {
                                foreach (var heliId2 in WavesCalled[waveTime])
                                {
                                    var checkHeli = BaseNetworkable.serverEntities.Find(new NetworkableId(heliId2)) as PatrolHelicopter;
                                    if (checkHeli != null)
                                    {
                                        activeHeliCount++;
                                        var checkComp = checkHeli.GetComponent<HeliComponent>();
                                        if (checkComp != null && !checkComp.isRetiring)
                                        {
                                            allActiveRetiring = false;
                                            break;
                                        }
                                    }
                                }
                            }
                            
                            if (allActiveRetiring && activeHeliCount > 0 && AutoRetiringWaveHelis[waveTime].Count >= activeHeliCount)
                            {
                                // All active helis in wave are retiring
                                ProcessedRetirements.Add(waveTime);
                                SendGroupedRetireDiscord(AutoRetiringWaveHelis[waveTime], null, true, false);
                                
                                // Cleanup
                                AutoRetiringWaveHelis.Remove(waveTime);
                                timer.Once(30f, () => ProcessedRetirements.Remove(waveTime));
                            }
                            
                            return null; // Don't send individual notification
                        }
                    }
                    

                    // Individual Discord notification for single helis
                    if (!_heliSignalData.IsMultiHeli && !heliComp.isWaveHeli)
                    {
                        BasePlayer lastAttacker = _heliSignalData.LastAttacker;

                        string steamUrl = steamProfileUrl + _heliSignalData.OwnerID;
                        string displayNameLink = $"[{_heliSignalData.OwnerName}]({steamUrl})";
                        string steamIdLink = $"[{_heliSignalData.OwnerID}]({steamUrl})";

                        string title = Lang("DiscordEmbedTitleRetire", heliProfile);
                        var desc = Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                        var footer = Lang("DiscordEmbedFooter") + $"  |  {zeodeFooterUrl}";
                        string color = "#FFA500"; // Orange embed

                        var fields = new List<DiscordField>
                        {
                            new DiscordField { Name = Lang("DiscordRetireLocation"), Value = gridPos, Inline = true}
                        };

                        TimeSpan timeSpan = TimeSpan.FromSeconds(Time.realtimeSinceStartup - _heliSignalData.FirstHitTime);
                        string time = timeSpan.ToString(@"hh\:mm\:ss");

                        fields.Add(new DiscordField { Name = Lang("DiscordRetireTime"), Value = time, Inline = true });
                        fields.Add(new DiscordField { Name = Lang("DiscordRetireHealth"), Value = $"{heli.health.ToString()}/{config.heli.heliConfig[heliProfile].Init.Health}", Inline = true });

                        // Add damage report if there were attackers
                        if (lastAttacker != null && _heliSignalData.Attackers.Count > 0)
                        {
                            var sorted = new List<KeyValuePair<ulong, AttackersStats>>(_heliSignalData.Attackers);
                            sorted.Sort((a, b) => b.Value.DamageDealt.CompareTo(a.Value.DamageDealt));

                            StringBuilder leaderboardBuilder = new StringBuilder();
                            int leaderboardCount = Math.Min(config.announce.maxReported, sorted.Count);

                            for (int i = 0; i < leaderboardCount; i++)
                            {
                                var entry = sorted[i];

                                leaderboardBuilder.Append(
                                    string.Format(lang.GetMessage("DiscordEmbedDamageReportIndex", this, null),
                                        i + 1,
                                        $"[{entry.Value.Name}]({steamProfileUrl + entry.Key})",
                                        Math.Round(entry.Value.DamageDealt, 2),
                                        Math.Round(((double)entry.Value.RotorHits / (double)entry.Value.TotalHits) * 100, 2),
                                        Math.Round(((double)entry.Value.TurretDamage / (double)entry.Value.DamageDealt) * 100, 2))
                                );
                            }

                            string leaderboard = leaderboardBuilder.ToString();

                            fields.Add(new DiscordField { Name = Lang("DiscordRetireReport"), Value = leaderboard, Inline = false });
                        }
                        else
                        {
                            fields.Add(new DiscordField { Name = Lang("DiscordRetireReport"), Value = Lang("DiscordRetireNoReport"), Inline = false });
                        }

                        SendDiscordEmbed(title, desc, color, footer, fields);
                    }
                }
            }
            return null;
        }

        #endregion Heli Hooks

        #region Crate/Loot/Gibs Hooks

        private object CanLootEntity(BasePlayer player, LockedByEntCrate crate)
        {
            if (crate.OwnerID == 0)
                return null;

            if (!HeliProfileCache.TryGetValue(crate.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                return null;

            if (config.heli.heliConfig[heliProfile].Crates.ProtectCrates)
            {
                if (permission.UserHasPermission(player.UserIDString, permAdmin))
                    return null;

                if (!IsOwnerOrFriend(player.userID, crate.OwnerID))
                {
                    Log($"{player.displayName} cannot loot {crate.ShortPrefabName} (Skin: {crate.skinID} Owner: {crate.OwnerID})");
                    Message(player, "CannotLoot");
                    return true;
                }
            }
            return null;
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            BaseEntity entity = info?.HitEntity;
            if (entity == null)
                return null;

            if (entity.OwnerID == 0)
                return null;

            if (entity is ServerGib)
            {
                if (!HeliProfileCache.TryGetValue(entity.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                    return null;

                if (config.heli.heliConfig[heliProfile].Gibs.ProtectGibs)
                {
                    if (permission.UserHasPermission(attacker.UserIDString, permAdmin))
                        return null;

                    if (!IsOwnerOrFriend(attacker.userID, entity.OwnerID))
                    {
                        info.damageTypes.Clear();
                        Message(attacker, "CannotHarvest");
                        return true;
                    }
                }
            }
            return null;
        }

        private void OnLootSpawn(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.IsDestroyed)
                return;
            
            if (!HeliProfileCache.TryGetValue(lootContainer.skinID, out string heliProfile) || string.IsNullOrEmpty(heliProfile))
                return;

            timer.Once(2f, () =>
            {
                try
                {
                    if (lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                        return;

                    if (!config.heli.heliConfig.TryGetValue(heliProfile, out var _heliProfile) || _heliProfile == null)
                        return;

                    if (lootContainer.ShortPrefabName.Contains("heli_crate"))
                    {
                        if (_heliProfile.Loot?.UseCustomLoot == true)
                            SpawnHeliCrateLoot(lootContainer, heliProfile);

                        if (_heliProfile.ExtraLoot?.UseExtraLoot == true)
                            AddExtraHeliCrateLoot(lootContainer, heliProfile);
                    }
                    else if (lootContainer.ShortPrefabName.Contains("codelockedhackablecrate"))
                    {
                        if (_heliProfile.LockedCrateLoot?.UseLockedCrateLoot == true)
                            SpawnLockedCrateLoot(lootContainer, heliProfile);
                    }
                }
                catch (System.Exception ex)
                {
                    Log($"Error in OnLootSpawn timer: {ex.Message}", LogLevel.ERROR);
                    Log($"StackTrace: {ex.StackTrace}", LogLevel.ERROR);
                }
            });
        }

        private object OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null)
                return null;

            if (HeliProfileCache.TryGetValue(crate.skinID, out string heliProfile) || !string.IsNullOrEmpty(heliProfile))
            {
                NextTick(() =>
                {
                    if (crate == null)
                        return;

                    float hackTime = HackableLockedCrate.requiredHackSeconds - config.heli.heliConfig[heliProfile].Crates.HackSeconds;
                    if (crate.hackSeconds > hackTime)
                        return;

                    crate.hackSeconds = hackTime;
                });
            }
            return null;
        }

        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null)
                return null;
            
            if (!HeliProfileCache.TryGetValue(crate.skinID, out string heliProfile))
                return null;

            if (!LockedCrates.TryGetValue(crate.net.ID.Value, out LockedCrateData crateData) || crateData == null)
                return null;

            // PlayerId == 0 means the configured protection period has expired.
            if (crateData.PlayerId == 0)
                return null;
            
            if (!config.heli.heliConfig.TryGetValue(heliProfile, out HeliData heliData) || heliData == null)
                return null;

            if (heliData.Crates.ProtectCrates && !IsOwnerOrFriend(player.userID, crateData.PlayerId))
            {
                Message(player, "CannotHack");
                return false;
            }
            return null;
        }

        #endregion Crate/Loot/Gibs Hooks

        #region Loot

        private void SpawnHeliCrates(PatrolHelicopter heli, BasePlayer lastAttacker, ulong ownerId, ulong skinId, string heliProfile, Vector3 position)
        {
            if (Interface.CallHook("CanSpawnHeliSignalCrates", heli, lastAttacker, ownerId, skinId, heliProfile) != null)
            {
                Log($"CanSpawnHeliSignalCrates hook prevented Heli crates spawning.", LogLevel.DEBUG);
                return;
            }

            if (!config.heli.heliConfig.TryGetValue(heliProfile, out HeliData heliData) || heliData == null)
                return;

            int cratesToSpawn = heliData.Crates.CratesToSpawn;
            float crateDespawn = heliData.Crates.HeliCrateDespawn;
            float unlockTime = heliData.Crates.UnlockCrates;
            bool protectCrates = heliData.Crates.ProtectCrates;

            for (int i = 0; i < cratesToSpawn; i++)
            {
                Vector3 newPos1 = Random.onUnitSphere;
                newPos1.y = TerrainMeta.HeightMap.GetHeight(position);
                newPos1.Normalize();
                Vector3 newPos2 = (position + new Vector3(0f, 5.0f, 0f) + newPos1); //(newPos1 * Random.Range(2f, 3f)); Too much, makes crates fly

                GameManager gameManager = GameManager.server;

                LockedByEntCrate crate = gameManager.CreateEntity(heli.crateToDrop.resourcePath, newPos2, Quaternion.identity, true) as LockedByEntCrate;
                if (crate == null)
                    continue;

                crate.OwnerID = ownerId;
                crate.skinID = skinId;
                crate._name = heliProfile;
                crate.Spawn();

                crate.CancelInvoke(crate.RemoveMe);
                crate.Invoke(new Action(crate.RemoveMe), crateDespawn);

                Vector3 lastMoveDir = (heli.myAI.GetLastMoveDir() * heli.myAI.GetMoveSpeed()) * 0.75f;

                Rigidbody rigidbody = crate.gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.mass = 2f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.linearVelocity = lastMoveDir + (newPos1 * Random.Range(1f, 3f));
                rigidbody.angularVelocity = Vector3Ex.Range(-1.75f, 1.75f);
                rigidbody.linearDamping = 0.5f * (rigidbody.mass / 5f);
                rigidbody.angularDamping = 0.2f * (rigidbody.mass / 5f);

                NextTick(()=>
                {
                    if (crate == null || crate.IsDestroyed)
                        return;
                
                    FireBall fireball = null;

                    if (heliData.Crates.FireDuration >= 0 && !heliData.Crates.DisableFire)
                    {
                        fireball = gameManager.CreateEntity(heli.fireBall.resourcePath, new Vector3(), new Quaternion(), true) as FireBall;
                        if (fireball)
                        {
                            fireball.SetParent(crate, false, false);
                            fireball.Spawn();
                            fireball.GetComponent<Rigidbody>().isKinematic = true;
                            fireball.GetComponent<Collider>().enabled = false;

                            fireball.tickRate = heliData.Fireball.DamageRate;
                            fireball.lifeTimeMin = heliData.Fireball.MinimumLifeTime;
                            fireball.lifeTimeMax = heliData.Fireball.MaximumLifeTime;
                            fireball.damagePerSecond = heliData.Fireball.DamagePerSecond;
                            fireball.waterToExtinguish = heliData.Fireball.WaterAmountToExtinguish;
                            fireball.generation = (heliData.Fireball.SpreadChance == 0) ? 9f : (1f - heliData.Fireball.SpreadChance) / 0.1f;

                            crate.SetLockingEnt(fireball);
                        }

                        timer.Once(heliData.Crates.FireDuration, () =>
                        {
                            if (crate != null || crate.IsDestroyed)
                                crate.SetLocked(false);
                            
                            if (fireball != null)
                                fireball.Extinguish();
                        });
                    }
                    else
                    {
                        crate.SetLocked(false);

                        if (fireball != null)
                            fireball.Extinguish();
                    }
                });

                timer.Once(2f, () =>
                {
                    if (crate == null || crate.IsDestroyed)
                        return;

                    crate.OwnerID = ownerId;
                    crate.skinID = skinId;
                    crate._name = heliProfile;
                });

                if (protectCrates && unlockTime > 0f)
                    RemoveHeliOwner(crate, unlockTime);
            }
        }

        private void SpawnLockedCrates(PatrolHelicopter heli, BasePlayer lastAttacker, ulong ownerId, ulong skinId, string heliProfile, Vector3 position)
        {
            if (Interface.CallHook("CanSpawnHeliSignalLockedCrates", heli, lastAttacker, ownerId, skinId, heliProfile) != null)
            {
                Log($"CanSpawnHeliSignalLockedCrates hook prevented locked hackable crates spawning.", LogLevel.WARN);
                return;
            }

            if (!config.heli.heliConfig.TryGetValue(heliProfile, out HeliData heliData) || heliData == null)
                return;

            for (int i = 0; i < heliData.Crates.LockedCratesToSpawn; i++)
            {
                Vector2 rand;
                rand = Random.insideUnitCircle * 5f;
                position = position + new Vector3(rand.x, 2f, (rand.y));
                HackableLockedCrate crate = GameManager.server.CreateEntity(hackableCrate, position, new Quaternion()) as HackableLockedCrate;
                if (crate == null)
                    continue;

                crate.OwnerID = ownerId;
                crate.skinID = skinId;
                crate._name = heliProfile;
                crate.Spawn();

                if (heliData.Crates.DisableMapMarker)
                    ConfigureLockedCrateMapMarker(crate);

                if (crate.net == null)
                {
                    crate.Kill();
                    continue;
                }
                
                ulong crateId = crate.net.ID.Value;
                LockedCrates[crateId] = new LockedCrateData
                {
                    PlayerId = ownerId,
                    DecayTime = heliData.Crates.LockedCrateDespawn
                };

                crate.RefreshDecay();

                timer.Once(2f, () =>
                {
                    // Re-add crate details, have seen some plugins change this after I set it
                    if (crate == null || crate.IsDestroyed)
                        return;

                    crate.OwnerID = ownerId;
                    crate.skinID = skinId;
                    crate._name = heliProfile;
                });

                // DEBUG: Remove this
                /*Rigidbody rigidbody = crate.gameObject.GetComponent<Rigidbody>();
                if (rigidbody)
                {
                    rigidbody.linearDamping = 1.0f;
                    rigidbody.angularDamping = 1.0f;
                    rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }*/ 

                float unlockTime = heliData.Crates.UnlockCrates;
                if (heliData.Crates.ProtectCrates && unlockTime > 0f)
                {
                    timer.Once(unlockTime, () =>
                    {
                        // Unlock the locked crates to anyone after time, if set in config
                        if (crate == null || crate.IsDestroyed || crate.net == null)
                            return;

                        crate.OwnerID = 0;

                        if (LockedCrates.TryGetValue(crate.net.ID.Value, out LockedCrateData crateData))
                            crateData.PlayerId = 0;
                    });
                }
            }
        }

        private void SpawnHeliCrateLoot(LootContainer lootContainer, string heliProfile)
        {
            if (string.IsNullOrEmpty(heliProfile) || lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                return;

            if (!config.heli.heliConfig.TryGetValue(heliProfile, out var heliConfig) || heliConfig?.Loot == null || heliConfig.Loot.LootTable == null)
                return;

            var heliLoot = heliConfig.Loot;
            List<LootItem> lootTable = new List<LootItem>(heliLoot.LootTable);
            List<LootItem> items = new List<LootItem>();

            foreach (var lootItem in lootTable)
            {
                if (lootItem == null || string.IsNullOrEmpty(lootItem.ShortName))
                    continue;

                ItemDefinition itemDef = ItemManager.FindItemDefinition(lootItem.ShortName);
                if (itemDef != null)
                    items.Add(lootItem);
            }

            if (items == null || items.Count == 0 || lootTable == null)
                return;

            int minItems = Math.Clamp(heliLoot.MinCrateItems, 0, 48);
            int maxItems = Math.Clamp(heliLoot.MaxCrateItems, minItems, 48);
            int count = Random.Range(minItems, maxItems + 1);
            int given = 0;
            int bps = 0;

            lootContainer.inventory.Clear();
            lootContainer.inventory.capacity = count;
            lootContainer.inventory.maxStackSize = 0; // 0 = use item defaults
            lootContainer.panelName = "generic_resizable";
            lootContainer.SendNetworkUpdateImmediate();

            bool allowDupes = false;
            if (heliLoot.AllowDupes || (!heliLoot.AllowDupes && count > items.Count))
                allowDupes = true; // Force allow dupes if not enough loot items (only if loot table not adequate)
            
            for (int i = 0; i < count; i++)
            {
                LootItem lootItem = items.GetRandom();
                if (lootItem == null)
                {
                    i--;
                    continue;
                }

                if (lootItem.Chance < Random.Range(0f, 100f))
                {
                    if (given < count)
                    {
                        i--;
                        continue;
                    }
                    break;
                }

                if (!allowDupes)
                {
                    // Catch in case items run out
                    items.Remove(lootItem);
                    if (items.Count <= 0)
                        items.AddRange(lootTable);
                }

                ItemDefinition itemDef = ItemManager.FindItemDefinition(lootItem.ShortName);
                if (itemDef == null)
                {
                    i--;
                    continue;
                }

                Item item;
                if (lootItem.BlueprintChance > Random.Range(0f, 100f) && itemDef.Blueprint != null && IsBP(itemDef) && bps < heliLoot.MaxBP)
                {
                    ItemDefinition bpDef = ItemManager.FindItemDefinition("blueprintbase");
                    if (bpDef == null)
                    {
                        i--;
                        continue;
                    }

                    item = ItemManager.Create(bpDef, 1, 0uL);
                    if (item == null || !item.IsValid())
                    {
                        i--;
                        continue;
                    }

                    item.blueprintTarget = itemDef.itemid;
                    bps++;
                }
                else
                {
                    int amount = Random.Range(lootItem.AmountMin, lootItem.AmountMax + 1);
                    if (amount <= 0)
                    {
                        // Catch idiots putting 0 item amounts :/
                        i--;
                        continue;
                    }

                    item = ItemManager.Create(itemDef, amount, lootItem.SkinId);
                    if (item == null || !item.IsValid())
                    {
                        i--;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(lootItem.DisplayName))
                        item.name = lootItem.DisplayName;

                    if (!string.IsNullOrEmpty(lootItem.Text))
                        item.text = lootItem.Text;
                }

                if (item.MoveToContainer(lootContainer.inventory))
                {
                    given++;
                    continue;
                }
                item.Remove(0f);
            }
        }

        private void AddExtraHeliCrateLoot(LootContainer lootContainer, string heliProfile)
        {
            if (string.IsNullOrEmpty(heliProfile) || lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                return;

            if (!config.heli.heliConfig.TryGetValue(heliProfile, out var heliConfig) || heliConfig?.ExtraLoot == null || heliConfig.ExtraLoot.LootTable == null)
                return;

            var heliLoot = heliConfig.ExtraLoot;
            List<LootItem> lootTable = new List<LootItem>(heliLoot.LootTable);
            List<LootItem> items = new List<LootItem>();

            foreach (var lootItem in lootTable)
            {
                if (lootItem == null || string.IsNullOrEmpty(lootItem.ShortName))
                    continue;

                ItemDefinition itemDef = ItemManager.FindItemDefinition(lootItem.ShortName);
                if (itemDef != null)
                    items.Add(lootItem);
            }

            if (items == null || items.Count == 0 || lootTable == null)
                return;

            int minItems = Math.Clamp(heliLoot.MinExtraItems, 0, 48);
            int maxItems = Math.Clamp(heliLoot.MaxExtraItems, minItems, 48);
            int count = Random.Range(minItems, maxItems + 1);
            int given = 0;
            int bps = 0;

            int currentCount = lootContainer.inventory.itemList?.Count ?? 0;
            if ((currentCount + count) > 48)
            {
                count = 48 - currentCount;
                if (count <= 0)
                    return;
            }

            lootContainer.inventory.capacity += count;
            lootContainer.inventory.maxStackSize = 0; // 0 = use item defaults
            lootContainer.panelName = "generic_resizable";
            lootContainer.SendNetworkUpdateImmediate();

            bool allowDupes = false;
            if (heliLoot.AllowDupes || (!heliLoot.AllowDupes && count > items.Count))
            	allowDupes = true; // Force allow dupes if not enough loot items (only if loot table not adequate)

            for (int i = 0; i < count; i++)
            {
                LootItem lootItem = items.GetRandom();
                if (lootItem == null)
                {
                    i--;
                    continue;
                }

                if (lootItem.Chance < Random.Range(0f, 100f))
                {
                    if (given < count)
                    {
                        i--;
                        continue;
                    }
                    break;
                }

                if (!allowDupes)
                {
                    // Catch in case items run out
                    items.Remove(lootItem);
                    if (items.Count <= 0)
                    	items.AddRange(lootTable);
                }

                ItemDefinition itemDef = ItemManager.FindItemDefinition(lootItem.ShortName);
                if (itemDef == null)
                {
                    i--;
                    continue;
                }

                Item item;
                if (lootItem.BlueprintChance > Random.Range(0f, 100f) && itemDef.Blueprint != null && IsBP(itemDef) && bps < heliLoot.MaxBP)
                {
                    ItemDefinition bpDef = ItemManager.FindItemDefinition("blueprintbase");
                    if (bpDef == null)
                    {
                        i--;
                        continue;
                    }

                    item = ItemManager.Create(bpDef, 1, 0uL);
                    if (item == null || !item.IsValid())
                    {
                        i--;
                        continue;
                    }

                    item.blueprintTarget = itemDef.itemid;
                    bps++;
                }
                else
                {
                    int amount = Random.Range(lootItem.AmountMin, lootItem.AmountMax + 1);
                    if (amount <= 0)
                    {
                        // Catch idiots putting 0 item amounts :/
                        i--;
                        continue;
                    }

                    item = ItemManager.Create(itemDef, amount, lootItem.SkinId);
                    if (item == null || !item.IsValid())
                    {
                        i--;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(lootItem.DisplayName))
                        item.name = lootItem.DisplayName;

                    if (!string.IsNullOrEmpty(lootItem.Text))
                        item.text = lootItem.Text;
                }

                lootContainer.inventory.capacity++;
                if (item.MoveToContainer(lootContainer.inventory))
                {
                    given++;            
                    continue;
                }
                item.Remove(0f);
            }
        }

        private void SpawnLockedCrateLoot(LootContainer lootContainer, string heliProfile)
        {
            if (string.IsNullOrEmpty(heliProfile) || lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                return;

            if (!config.heli.heliConfig.TryGetValue(heliProfile, out var heliConfig) || heliConfig?.LockedCrateLoot == null || heliConfig.LockedCrateLoot.LootTable == null)
                return;

            var heliLoot = heliConfig.LockedCrateLoot;
            List<LootItem> lootTable = new List<LootItem>(heliLoot.LootTable);
            List<LootItem> items = new List<LootItem>();

            foreach (var lootItem in lootTable)
            {
                if (lootItem == null || string.IsNullOrEmpty(lootItem.ShortName))
                    continue;

                ItemDefinition itemDef = ItemManager.FindItemDefinition(lootItem.ShortName);
                if (itemDef != null)
                    items.Add(lootItem);
            }

            if (items == null || items.Count == 0 || lootTable == null)
                return;

            int minItems = Math.Clamp(heliLoot.MinLockedCrateItems, 0, 48);
            int maxItems = Math.Clamp(heliLoot.MaxLockedCrateItems, minItems, 48);
            int count = Random.Range(minItems, maxItems + 1);
            int given = 0;
            int bps = 0;

            Action<Item, bool, BasePlayer> itemCallback = lootContainer.inventory.onItemAddedRemoved;
            lootContainer.inventory.onItemAddedRemoved = null;

            try
            {
                lootContainer.inventory.Clear();
            }
            finally
            {
                lootContainer.inventory.onItemAddedRemoved = itemCallback;
            }

            lootContainer.inventory.capacity = count;
            lootContainer.inventory.maxStackSize = 0; // 0 = use item defaults, maybe add custom stack size support later if requested.
            lootContainer.panelName = "generic_resizable";
            lootContainer.SendNetworkUpdateImmediate();

            bool allowDupes = false;
            if (heliLoot.AllowDupes || (!heliLoot.AllowDupes && count > items.Count))
            	allowDupes = true; // Force allow dupes if not enough loot items (only if loot table not adequate)

            for (int i = 0; i < count; i++)
            {
                LootItem lootItem = items.GetRandom();
                if (lootItem == null)
                {
                    i--;
                    continue;
                }

                if (lootItem.Chance < Random.Range(0f, 100f))
                {
                    if (given < count)
                    {
                        i--;
                        continue;
                    }
                    break;
                }

                if (!allowDupes)
                {
                    // Catch in case items run out
                    items.Remove(lootItem);
                    if (items.Count <= 0)
                    	items.AddRange(lootTable);
                }

                ItemDefinition itemDef = ItemManager.FindItemDefinition(lootItem.ShortName);
                if (itemDef == null)
                {
                    i--;
                    continue;
                }

                Item item;
                if (lootItem.BlueprintChance > Random.Range(0f, 100f) && itemDef.Blueprint != null && IsBP(itemDef) && bps < heliLoot.MaxBP)
                {
                    ItemDefinition bpDef = ItemManager.FindItemDefinition("blueprintbase");
                    if (bpDef == null)
                    {
                        i--;
                        continue;
                    }

                    item = ItemManager.Create(bpDef, 1, 0uL);
                    if (item == null || !item.IsValid())
                    {
                        i--;
                        continue;
                    }

                    item.blueprintTarget = itemDef.itemid;
                    bps++;
                }
                else
                {
                    int amount = Random.Range(lootItem.AmountMin, lootItem.AmountMax + 1);
                    if (amount <= 0)
                    {
                        // Catch idiots putting 0 item amounts :/
                        i--;
                        continue;
                    }

                    item = ItemManager.Create(itemDef, amount, lootItem.SkinId);
                    if (item == null || !item.IsValid())
                    {
                        i--;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(lootItem.DisplayName))
                        item.name = lootItem.DisplayName;

                    if (!string.IsNullOrEmpty(lootItem.Text))
                        item.text = lootItem.Text;
                }

                if (item.MoveToContainer(lootContainer.inventory))
                {
                    given++;
                    continue;
                }
                item.Remove(0f);
            }
        }

        #endregion Loot

        #region Discord Announcements

        // Discord embed structures
        public class DiscordEmbed
        {
            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("color")]
            public int Color { get; set; }

            [JsonProperty("timestamp")]
            public string Timestamp { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            [JsonProperty("footer")]
            public DiscordFooter Footer { get; set; }

            [JsonProperty("fields")]
            public List<DiscordField> Fields { get; set; } = new List<DiscordField>();
        }

        public class DiscordFooter
        {
            [JsonProperty("text")]
            public string Text { get; set; }
        }

        public class DiscordField
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("value")]
            public string Value { get; set; }

            [JsonProperty("inline")]
            public bool Inline { get; set; } = false;
        }

        public class DiscordMessage
        {
            [JsonProperty("username")]
            public string Username { get; set; }

            [JsonProperty("avatar_url")]
            public string AvatarUrl { get; set; }

            [JsonProperty("embeds")]
            public List<DiscordEmbed> Embeds { get; set; } = new List<DiscordEmbed>();
        }

        public void SendDiscordEmbed(string title, string description, int color = 7506394, string footerText = null, List<DiscordField> fields = null)
        {
            if (string.IsNullOrEmpty(config.discord.webhookUrl) || config.discord.webhookUrl == defaultWebhookUrl)
            {
                Log("Discord webhook URL not set! Configure in the config and restart the plugin.", LogLevel.ERROR);
                return;
            }

            try
            {
                var embed = new DiscordEmbed
                {
                    Title = title,
                    Description = description,
                    Color = color
                };

                if (!string.IsNullOrEmpty(footerText))
                {
                    embed.Footer = new DiscordFooter { Text = footerText };
                }

                if (fields != null && fields.Count > 0)
                {
                    embed.Fields = fields;
                }

                var message = new DiscordMessage
                {
                    Username = config.discord.defaultBotName,
                    AvatarUrl = GetAvatarUrl(),
                    Embeds = new List<DiscordEmbed> { embed }
                };

                var json = JsonConvert.SerializeObject(message);

                var headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json"
                };

                webrequest.Enqueue(config.discord.webhookUrl, json, (code, response) =>
                {
                    if (code != 200 && code != 204)
                    {
                        Log($"Failed to send Discord message. Status: {code}, Response: {response}", LogLevel.ERROR);
                    }
                }, this, Core.Libraries.RequestMethod.POST, headers, 15000f);
            }
            catch (Exception ex)
            {
                Log($"Error sending Discord message: {ex.Message}", LogLevel.ERROR);
            }
        }

        // Another method using hex color for ease
        public void SendDiscordEmbed(string title, string description, string hexColor, string footerText = null, List<DiscordField> fields = null)
        {
            int color = HexToInt(hexColor);
            SendDiscordEmbed(title, description, color, footerText, fields);
        }

        // Convert hex color to int
        private int HexToInt(string hex)
        {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int result))
                return result;

            return 7506394; // Default blue color
        }

        private string GetAvatarUrl()
        {
            if (config.discord.useCustomAvatar && !string.IsNullOrEmpty(config.discord.customAvatarUrl))
            {
                return config.discord.customAvatarUrl;
            }
            return HELI_AVATAR;
        }

        private string FormatTime(float seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{time.Hours:D2}h {time.Minutes:D2}m {time.Seconds:D2}s";
        }

        private void SendDiscordKillReport(PatrolHelicopter heli, HeliStats data, string heliProfile, Vector3 pos, string gridPos,
            BasePlayer killer, string time, bool isWaveHeli = false, int wavePosition = 0, int waveTotalCount = 0, bool isMultiHeli = false)
        {
            if (data.OwnerName == null) return;

            string steamUrl = steamProfileUrl + data.OwnerID;
            string displayNameLink = $"[{data.OwnerName}]({steamUrl})";
            string steamIdLink = $"[{data.OwnerID}]({steamUrl})";

            string title;
            if (isWaveHeli && wavePosition > 0)
            {
                title = string.Format(lang.GetMessage("DiscordEmbedTitleKilledWave", this), heliProfile, wavePosition, waveTotalCount);
            }
            else if (isMultiHeli && wavePosition > 0)
            {
                // For multi-helis, wavePosition contains the multi position
                title = $"💀  {heliProfile} Destroyed (Multi: {wavePosition} of {waveTotalCount})  💀";
            }
            else
            {
                title = Lang("DiscordEmbedTitleKilled", heliProfile);
            }

            var sorted = new List<KeyValuePair<ulong, AttackersStats>>(data.Attackers);
            sorted.Sort((a, b) => b.Value.DamageDealt.CompareTo(a.Value.DamageDealt));

            StringBuilder leaderboardBuilder = new StringBuilder();
            int leaderboardCount = Math.Min(config.announce.maxReported, sorted.Count);

            for (int i = 0; i < leaderboardCount; i++)
            {
                var entry = sorted[i];

                leaderboardBuilder.Append(
                    string.Format(lang.GetMessage("DiscordEmbedDamageReportIndex", this, null),
                        i + 1,
                        $"[{entry.Value.Name}]({steamProfileUrl + entry.Key})",
                        Math.Round(entry.Value.DamageDealt, 2),
                        Math.Round(((double)entry.Value.RotorHits / (double)entry.Value.TotalHits) * 100, 2),
                        Math.Round(((double)entry.Value.TurretDamage / (double)entry.Value.DamageDealt) * 100, 2))
                );
            }

            string leaderboard = leaderboardBuilder.ToString();

            var fields = new List<DiscordField>
            {
                new DiscordField { Name = Lang("DiscordKilledLocation"), Value = gridPos, Inline = true },
                new DiscordField { Name = Lang("DiscordKilledTime"), Value = time, Inline = true },
                new DiscordField { Name = Lang("DiscordKilledKiller"), Value = $"[{killer.displayName}]({steamProfileUrl + killer.userID})", Inline = true },
                new DiscordField { Name = Lang("DiscordKilledLeaderboard"), Value = leaderboard, Inline = false }
            };

            SendDiscordEmbed(title, Lang("DiscordEmbedOwner", displayNameLink, steamIdLink), 
                             "#FF0000", Lang("DiscordEmbedFooter") + $"  |  {zeodeFooterUrl}", fields);
        }

        private void SendGroupedRetireDiscord(List<PatrolHelicopter> helis, BasePlayer retiringPlayer, bool isWave, bool isMulti)
        {
            if (helis == null || helis.Count == 0) return;

            var firstHeli = helis[0];
            var heliComp = firstHeli.GetComponent<HeliComponent>();
            if (heliComp == null) return;

            var firstHeliData = HeliSignalData[firstHeli.net.ID.Value];
            string steamUrl = steamProfileUrl + firstHeliData.OwnerID;
            string displayNameLink = $"[{firstHeliData.OwnerName}]({steamUrl})";
            string steamIdLink = $"[{firstHeliData.OwnerID}]({steamUrl})";

            string title = string.Empty;
            List<DiscordField> fields = new List<DiscordField>();

            if (isWave)
            {
                title = Lang("DiscordEmbedTitleWaveRetired", heliComp.waveProfile);

                // Get proper status for each wave instance
                var statuses = GetEventHeliStatuses(heliComp.waveTime, true, false, helis);
                List<string> heliStatuses = new List<string>();

                var waveConfig = config.heli.waveConfig[heliComp.waveProfile].WaveProfiles;
                for (int i = 0; i < waveConfig.Count; i++)
                {
                    var profile = waveConfig[i];
                    if (config.heli.heliConfig.ContainsKey(profile))
                    {
                        string heliName = config.heli.heliConfig[profile].Init.HeliName;
                        string statusKey = $"{profile}_{i}";
                        string emoji = "❓"; // Default fallback emoji

                        if (statuses.TryGetValue(statusKey, out var status))
                        {
                            switch (status)
                            {
                                case HeliStatus.Alive:
                                    emoji = "💚";
                                    break;
                                case HeliStatus.Destroyed:
                                    emoji = "💥";
                                    break;
                                case HeliStatus.PartiallyDestroyed:
                                    emoji = "💛";
                                    break;
                                case HeliStatus.NotSpawned:
                                    emoji = "⏳";
                                    break;
                            }
                        }

                        // Add wave number if there are duplicates
                        string displayName = heliName;
                        int profileCount = 0;

                        foreach (string waveProfile in waveConfig)
                        {
                            if (waveProfile == profile)
                            {
                                profileCount++;

                                if (profileCount > 1)
                                    break;
                            }
                        }

                        if (profileCount > 1)
                        {
                            displayName = $"{heliName} (Wave {i + 1})";
                        }

                        heliStatuses.Add($"{emoji} {displayName}");
                    }
                }

                fields.Add(new DiscordField { Name = Lang("DiscordWaveCompleteStatus"), Value = GetRetiredByValue(heliComp, retiringPlayer), Inline = true });
                fields.Add(new DiscordField { Name = Lang("DiscordWaveRetiredHelicopters"), Value = string.Join("\n", heliStatuses), Inline = false });
            }
            else if (isMulti)
            {
                title = $"🚫  {firstHeliData.MultiHeliProfile} Retired (Multi Event)  🚫";

                var statuses = GetEventHeliStatuses(firstHeliData.MultiHeliTime, false, true, helis);
                List<string> heliStatuses = new List<string>();

                for (int i = 1; i <= firstHeliData.MultiTotalCount; i++)
                {
                    string key = $"Position_{i}";
                    string emoji = "❓"; // Default fallback emoji

                    if (statuses.TryGetValue(key, out var status))
                    {
                        emoji = status == HeliStatus.Alive ? "💚" : "💥";
                    }

                    heliStatuses.Add($"{emoji} {firstHeliData.MultiHeliProfile} #{i}");
                }

                fields.Add(new DiscordField { Name = Lang("DiscordMultiCompleteStatus"), Value = GetRetiredByValue(heliComp, retiringPlayer), Inline = true });
                fields.Add(new DiscordField { Name = Lang("DiscordMultiRetiredHelicopters"), Value = string.Join("\n", heliStatuses), Inline = false });
            }

            var desc = Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
            var footer = Lang("DiscordEmbedFooter") + $"  |  {zeodeFooterUrl}";
            string color = "#FFA500"; // Orange embed

            SendDiscordEmbed(title, desc, color, footer, fields);
        }

        private void SendWaveCompletionDiscord(BasePlayer capturedOwner, string capturedWaveProfile)
        {
            if (Instance == null || !config.discord.sendHeliKill)
                return;

            try
            {
                string steamUrl = steamProfileUrl + capturedOwner.userID;
                string displayNameLink = $"[{capturedOwner.displayName}]({steamUrl})";
                string steamIdLink = $"[{capturedOwner.UserIDString}]({steamUrl})";

                List<string> heliNames = new List<string>();
                foreach (var profile in config.heli.waveConfig[capturedWaveProfile].WaveProfiles)
                {
                    if (config.heli.heliConfig.ContainsKey(profile))
                    {
                        heliNames.Add($"💥 {config.heli.heliConfig[profile].Init.HeliName}");
                    }
                }
                string waveComposition = string.Join("\n", heliNames);

                var title = Instance.Lang("DiscordEmbedTitleWaveComplete", capturedWaveProfile);
                var desc = Instance.Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                var footer = Instance.Lang("DiscordEmbedFooter") + $"  |  {zeodeFooterUrl}";
                string color = "#FFD700"; // Gold color

                var field = new List<DiscordField>
                {
                    new DiscordField { Name = Instance.Lang("DiscordWaveCompleteStatus"), Value = Instance.Lang("DiscordWaveCompleteAllDestroyed"), Inline = true},
                    new DiscordField { Name = Instance.Lang("DiscordWaveCompleteTotal"), Value = config.heli.waveConfig[capturedWaveProfile].WaveProfiles.Count.ToString(), Inline = true},
                    new DiscordField { Name = Instance.Lang("DiscordWaveCompleteHelicopters"), Value = waveComposition, Inline = false}
                };

                Instance.SendDiscordEmbed(title, desc, color, footer, field);
            }
            catch (Exception ex)
            {
                if (Instance != null)
                    Instance.Log($"Error sending Discord notification: {ex.Message}", LogLevel.ERROR);
            }
        }

        private string GetRetiredByValue(HeliComponent heliComp, BasePlayer retiringPlayer)
        {
            if (heliComp.retireCmdUsed && retiringPlayer != null)
            {
                string retiringSteamUrl = steamProfileUrl + retiringPlayer.userID;
                string[] retirer = new string[] { retiringPlayer.displayName, retiringSteamUrl };
                return Lang("DiscordMultiOrWaveRetiredBy", retirer);
            }
            return Lang("DiscordMultiOrWaveRetiredTimeout");
        }

        private Dictionary<string, HeliStatus> GetEventHeliStatuses(float eventTime, bool isWave, bool isMulti, List<PatrolHelicopter> retiringHelis)
        {
            var statuses = new Dictionary<string, HeliStatus>();

            if (isWave)
            {
                var firstHeli = retiringHelis != null && retiringHelis.Count > 0 ? retiringHelis[0] : null;
                if (firstHeli == null)
                    return statuses;

                var heliComp = firstHeli.GetComponent<HeliComponent>();
                if (heliComp == null) return statuses;

                var waveConfig = config.heli.waveConfig[heliComp.waveProfile].WaveProfiles;

                // Track status by wave profile index
                Dictionary<int, WaveInstanceStatus> instanceStatuses = new Dictionary<int, WaveInstanceStatus>();

                // Initialize all wave profiles
                for (int i = 0; i < waveConfig.Count; i++)
                {
                    instanceStatuses[i] = new WaveInstanceStatus
                    {
                        ProfileName = waveConfig[i],
                        TotalCount = config.heli.heliConfig[waveConfig[i]].Init.HeliAmount,
                        AliveCount = 0,
                        DestroyedCount = 0,
                        HasSpawned = false
                    };
                }

                // Mark waves as spawned ONLY based on SpawnedWaveProfiles
                if (SpawnedWaveProfiles.ContainsKey(heliComp.waveProfile))
                {
                    foreach (int spawnedIndex in SpawnedWaveProfiles[heliComp.waveProfile])
                    {
                        if (instanceStatuses.ContainsKey(spawnedIndex))
                        {
                            instanceStatuses[spawnedIndex].HasSpawned = true;
                        }
                    }
                }

                // Create a set to track which helis we've already counted
                HashSet<ulong> countedHelis = new HashSet<ulong>();

                // First, count all alive helis from retiring list
                foreach (var retiringHeli in retiringHelis)
                {
                    var comp = retiringHeli.GetComponent<HeliComponent>();
                    if (comp != null && comp.waveProfile == heliComp.waveProfile)
                    {
                        int profileIndex = comp.waveProfileIndex;
                        if (instanceStatuses.ContainsKey(profileIndex) && instanceStatuses[profileIndex].HasSpawned)
                        {
                            instanceStatuses[profileIndex].AliveCount++;
                            countedHelis.Add(retiringHeli.net.ID.Value);
                        }
                    }
                }

                // Count destroyed helis, only once and for waves that were actually spawned
                foreach (var kvp in WavesCalled)
                {
                    foreach (var heliId in kvp.Value)
                    {
                        // Skip if we already counted this heli
                        if (countedHelis.Contains(heliId))
                            continue;

                        // Check if this heli still exists
                        var existingHeli = BaseNetworkable.serverEntities.Find(new NetworkableId(heliId)) as PatrolHelicopter;

                        if (existingHeli == null)
                        {
                            // Heli doesn't exist = it was destroyed
                            // Check data to determine which wave it belonged to
                            if (HeliSignalData.ContainsKey(heliId))
                            {
                                var heliData = HeliSignalData[heliId];
                                if (heliData.ParentWaveProfile == heliComp.waveProfile)
                                {
                                    int waveIndex = heliData.WaveInstanceIndex;

                                    // Only count it if this wave was actually spawned
                                    if (instanceStatuses.ContainsKey(waveIndex) && instanceStatuses[waveIndex].HasSpawned)
                                    {
                                        instanceStatuses[waveIndex].DestroyedCount++;
                                        countedHelis.Add(heliId);
                                    }
                                }
                            }
                        }
                    }
                }

                // Set the final status for each wave profile
                for (int i = 0; i < waveConfig.Count; i++)
                {
                    var instanceStatus = instanceStatuses[i];
                    string statusKey = $"{waveConfig[i]}_{i}";

                    if (!instanceStatus.HasSpawned)
                    {
                        // Wave was NEVER spawned
                        statuses[statusKey] = HeliStatus.NotSpawned;
                    }
                    else
                    {
                        // Wave WAS spawned
                        if (instanceStatus.TotalCount == 1)
                        {
                            // Single heli wave
                            if (instanceStatus.AliveCount > 0)
                                statuses[statusKey] = HeliStatus.Alive;
                            else
                                statuses[statusKey] = HeliStatus.Destroyed;
                        }
                        else
                        {
                            // Multi-heli wave
                            if (instanceStatus.AliveCount == instanceStatus.TotalCount)
                            {
                                // All helis alive
                                statuses[statusKey] = HeliStatus.Alive;
                            }
                            else if (instanceStatus.AliveCount == 0)
                            {
                                // No helis alive - destroyed
                                statuses[statusKey] = HeliStatus.Destroyed;
                            }
                            else if (instanceStatus.AliveCount > 0 && instanceStatus.AliveCount < instanceStatus.TotalCount)
                            {
                                // Some helis alive - partially destroyed
                                statuses[statusKey] = HeliStatus.PartiallyDestroyed;
                            }
                        }
                    }
                }
            }
            else if (isMulti)
            {
                var firstHeliData = HeliSignalData[retiringHelis[0].net.ID.Value];
                float multiTime = firstHeliData.MultiHeliTime;

                HashSet<int> alivePositions = new HashSet<int>();

                if (MultiHelisCalled.ContainsKey(multiTime))
                {
                    foreach (var heliId in MultiHelisCalled[multiTime])
                    {
                        var heli = BaseNetworkable.serverEntities.Find(new NetworkableId(heliId)) as PatrolHelicopter;
                        if (heli != null && retiringHelis.Contains(heli))
                        {
                            if (HeliSignalData.ContainsKey(heliId))
                            {
                                alivePositions.Add(HeliSignalData[heliId].MultiPosition);
                            }
                        }
                    }
                }

                for (int i = 1; i <= firstHeliData.MultiTotalCount; i++)
                {
                    string key = $"Position_{i}";
                    statuses[key] = alivePositions.Contains(i) ? HeliStatus.Alive : HeliStatus.Destroyed;
                }
            }

            return statuses;
        }

        private enum HeliStatus
        {
            Alive,
            Destroyed,
            PartiallyDestroyed,
            NotSpawned
        }

        private class WaveInstanceStatus
        {
            public string ProfileName { get; set; }
            public int TotalCount { get; set; }
            public int AliveCount { get; set; }
            public int DestroyedCount { get; set; }
            public bool HasSpawned { get; set; }
        }

        #endregion Discord Announcements

        #region API

        // [Debug] API ID: AGR4ZQN=
        // Other devs can call these hooks to help with compatability with their plugins if needed
        object IsHeliSignalObject(ulong skinId) => HeliProfileCache.ContainsKey(skinId) ? true : null;

        object IsHeliSignalObject(PatrolHelicopter heli) => HeliProfileCache.ContainsKey(heli.skinID) ? true : null;

        object IsHeliSignalObject(PatrolHelicopterAI heliAI) => HeliProfileCache.ContainsKey(heliAI.helicopterBase.skinID) ? true : null;

        // Call is like: var data = HeliSignals.Call("GetHeliSignalData", heli) as Dictionary<string, object>;
        // Then, in consuming plugin, data["heliProfile"] etc
        object GetHeliSignalData(PatrolHelicopter heli)
        {
            HeliComponent heliComp = heli?.GetComponent<HeliComponent>();
            if (heliComp == null)
                return null;

            if (!HeliSignalData.TryGetValue(heliComp.heliId, out var _heliSignalData) || _heliSignalData?.LastAttacker == null)
                return null;

            return new Dictionary<string, object>
            {
                ["heliProfile"]         = heliComp.heliProfile,                                 // Actual heli profile from config (string)
                ["heliName"]            = heliComp._heliProfile?.Init.HeliName,                 // Heli display name (string)
                ["skinId"]              = heliComp.skinId,                                      // Heli skin ID (ulong)
                ["heliId"]              = heliComp.heliId,                                      // Heli Net.ID.Value (ulong)
                ["owner"]               = heliComp.owner,                                       // Heli owner (BasePlayer)
                ["calledPosition"]      = heliComp.calledPosition,                              // Called heli position (Vector3)
                ["callingTeam"]         = heliComp.callingTeam,                                 // Get calling team as a List
                ["lastAttacker"]        = _heliSignalData?.LastAttacker,                        // Last player to attack heli (BasePlayer)
                ["cratesToSpawn"]       = heliComp._heliProfile.Crates.CratesToSpawn,           // Number of Heli crates to spawn (int)
                ["lockedCratesToSpawn"] = heliComp._heliProfile.Crates.LockedCratesToSpawn,     // Number of locked crates to spawn (int)
            };
        }

        // Play nice with Loot Table & Stacksize GUI
        object OnContainerPopulate(LootContainer lootContainer)
        {
            if (lootContainer == null || !lootContainer.ShortPrefabName.Equals("heli_crate"))
                return null;

            if (HeliProfileCache.TryGetValue(lootContainer.skinID, out string heliProfile) && !string.IsNullOrEmpty(heliProfile))
            {
                if (config.heli.heliConfig.TryGetValue(heliProfile, out var heliConfig) && heliConfig != null)
                {
                    if (lootContainer.ShortPrefabName.Equals("heli_crate"))
                    {
                        if (heliConfig.Loot.UseCustomLoot == true)
                            return true;
                    }
                    else if (lootContainer.ShortPrefabName.Equals("codelockedhackablecrate"))
                    {
                        if (heliConfig.LockedCrateLoot.UseLockedCrateLoot == true)
                            return true;
                    }
                }
            }
            return null;
        }

        // Play nice with BetterNPC
        object CanHelicopterSpawnNpc(PatrolHelicopter helicopter)
        {
            if (helicopter == null)
                return null;

            if (!config.options.useBetterNPC && HeliProfileCache.ContainsKey(helicopter.skinID))
                return true;

            return null;
        }

        // Play nice with FancyDrop
        object ShouldFancyDrop(NetworkableId netId)
        {
            if (netId == null)
                return null;

            var signal = BaseNetworkable.serverEntities.Find(netId) as BaseEntity;
            if (signal != null)
            {
                if (HeliProfileCache.ContainsKey(signal.skinID))
                    return true;
            }
            return null;
        }

        // Play nice with EpicLoot
        object CanReceiveEpicLootFromCrate(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null)
                return null;

            HeliProfileCache.TryGetValue(lootContainer.skinID, out string heliProfile);
            if (heliProfile == null)
                return null;

            if (lootContainer.ShortPrefabName.Equals("heli_crate") && !config.heli.heliConfig[heliProfile].Loot.AllowEpicLoot)
                return true;
            else if (lootContainer.ShortPrefabName.Equals("codelockedhackablecrate") && !config.heli.heliConfig[heliProfile].LockedCrateLoot.AllowEpicLoot)
                return true;

            return null;
        }

        // Play nice with AlphaLoot
        object CanPopulateLoot(LootContainer lootContainer)
        {
            if (lootContainer == null)
                return null;

            HeliProfileCache.TryGetValue(lootContainer.skinID, out string heliProfile);
            if (heliProfile == null)
                return null;

            if (lootContainer.ShortPrefabName.Equals("heli_crate"))
            {
                if (config.heli.heliConfig[heliProfile].Loot.UseCustomLoot)
                    return true;
            }
            else if (lootContainer.ShortPrefabName.Equals("codelockedhackablecrate"))
            {
                if (config.heli.heliConfig[heliProfile].LockedCrateLoot.UseLockedCrateLoot)
                    return true;
            }
            return null;
        }

        // Play nice with PVE plugins
        object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo?.Initiator == null)
                return null;

            // Stop hook conflict with NpcSpawn
            if (NpcSpawn && entity.skinID == 11162132011012)
                return null;

            ulong skinId = hitInfo.Initiator.skinID;

            return HeliProfileCache.ContainsKey(skinId) ? true : null;
        }

        // Play nice with PVE plugins
        object CanEntityBeTargeted(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null)
                return null;

            return HeliProfileCache.ContainsKey(entity.skinID) ? true : null;
        }

        // Play nice with BotReSpawn
        object OnBotReSpawnPatrolHeliKill(PatrolHelicopterAI heliAi)
        {
            if (heliAi == null)
                return null;

            return HeliProfileCache.ContainsKey(heliAi.helicopterBase.skinID) ? true : null;
        }

        // Play nice with Dynamic PvP
        private object OnCreateDynamicPVP(string eventName, BaseEntity entity)
        {
            if (config.options.useDynamicPVP)
                return null;

            if (string.IsNullOrEmpty(eventName) || entity == null)
                return null;

            return HeliProfileCache.ContainsKey(entity.skinID) ? true : null;
        }

        #endregion API

        #region Rewards

        private void CheckRewardPlugins()
        {
            if (config.rewards.enableRewards || CanPluginPurchase())
            {
                if (!plugins.Find(config.rewards.rewardPlugin))
                    Log($"{config.rewards.rewardPlugin} not found, giving/spending reward points is not possible until loaded.", LogLevel.WARN);
            }

            if (config.rewards.enableXP)
            {
                string xpPlugin = config.rewards.pluginXP;
                if (xpPlugin.ToLower() == "xperience" && !XPerience || xpPlugin.ToLower() == "skilltree" && !SkillTree || xpPlugin.ToLower() == "skills" && !Skills)
                    Log($"{xpPlugin} plugin not found, giving XP is not possible until loaded.", LogLevel.WARN);
            }
        }

        private void GiveReward(ulong playerId, double amount, string heliProfile)
        {
            if (!config.rewards.enableRewards)
                return;
            
            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)amount * keyPair.Value;
                }
            }

            BasePlayer player = FindPlayer(playerId.ToString());
            if (player == null)
            {
                Log($"Failed to give reward to: {playerId}, no player found.", LogLevel.ERROR);
                return;
            }

            if (!plugins.Find(config.rewards.rewardPlugin))
            {
                Log($"Failed to give reward to: {playerId}, {config.rewards.rewardPlugin} not loaded.", LogLevel.ERROR);
                return;
            }

            switch (config.rewards.rewardPlugin.ToLower())
            {
                case "serverrewards":
                    ServerRewards?.Call("AddPoints", playerId, (int)amount);
                    Message(player, "PointsGiven", (int)amount, config.rewards.rewardUnit, heliProfile);
                    break;
                case "economics":
                    Economics?.Call("Deposit", playerId, amount);
                    Message(player, "PointsGiven", config.rewards.rewardUnit, (int)amount, heliProfile);
                    break;
                default:
                    break;
            }
            return;
        }

        private void GiveXP(ulong playerId, double amount, string heliProfile)
        {
            if (!config.rewards.enableXP)
                return;

            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)(amount * keyPair.Value);
                }
            }

            BasePlayer player = FindPlayer(playerId.ToString());
            if (player == null)
            {
                Log($"Failed to give XP to: {playerId}, no player found.", LogLevel.ERROR);
                return;
            }

            string xpPlugin = config.rewards.pluginXP.ToLower();
            if (xpPlugin == "xperience")
            {
                if (!XPerience)
                {
                    Log($"Failed to give XP to: {playerId}, XPerience is not loaded", LogLevel.ERROR);
                    return;
                }

                if (config.rewards.boostXP)
                    XPerience?.Call("GiveXP", player, amount);
                else
                    XPerience?.Call("GiveXPBasic", player, amount);
            }
            else if (xpPlugin == "skilltree")
            {
                if (!SkillTree)
                {
                    Log($"Failed to give XP to: {playerId}, SkillTree is not loaded", LogLevel.ERROR);
                    return;
                }
                if (config.rewards.boostXP)
                    SkillTree?.Call("AwardXP", playerId, amount, "HeliSignals", false);
                else
                    SkillTree?.Call("AwardXP", playerId, amount, "HeliSignals", true);
            }
            else if (xpPlugin == "skills")
            {
                if (!Skills)
                {
                    Log($"Failed to give XP to: {playerId}, Skills is not loaded", LogLevel.WARN);
                    return;
                }

                Skills?.Call("API_AddPlayerXP", playerId, (float)amount);
            }

            Message(player, "XPGiven", amount, heliProfile);
        }

        private void GiveScrap(ulong playerId, int amount, string heliProfile)
        {
            if (!config.rewards.enableScrap)
                return;

            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)(amount * keyPair.Value);
                }
            }

            BasePlayer player = FindPlayer(playerId.ToString());
            if (player != null)
            {
                Item scrap = ItemManager.CreateByItemID(scrapId, amount, 0);
                player.inventory.GiveItem(scrap);
                Message(player, "ScrapGiven", amount, heliProfile);
                return;
            }
            Log($"Failed to give scrap to: {playerId}, no player found.", LogLevel.ERROR);
        }

        private void GiveCustomReward(ulong playerId, int amount, string heliProfile)
        {
            if (!config.rewards.enableCustomReward)
                return;

            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)(amount * keyPair.Value);
                }
            }

            BasePlayer player = FindPlayer(playerId.ToString());
            if (player == null)
            {
                Log($"Failed to give custom reward to: {playerId}, no player found.", LogLevel.WARN);
                return;
            }

            var _customRewardItem = config.rewards.customRewardItem;
            if (_customRewardItem == null)
            {
                Log($"Failed to give {amount} custom reward to: {playerId}, no custom", LogLevel.WARN);
                return;
            }

            var itemShortname = _customRewardItem.ShortName;
            if (string.IsNullOrEmpty(itemShortname))
            {
                Log($"Failed to give {amount} custom reward to: {playerId}, no custom " + "item shortname configured.", LogLevel.WARN);
                return;
            }

            var skinId = _customRewardItem.SkinId;
            Item item = ItemManager.CreateByName(itemShortname, amount, skinId);
            if (item == null)
            {
                Log($"Failed to give {amount} custom reward to: {playerId}, item creation failed", LogLevel.WARN);
                return;
            }

            if (!string.IsNullOrEmpty(_customRewardItem.DisplayName))
            {
                item.name = _customRewardItem.DisplayName;
                Message(player, "CustomGiven", amount, item.name, heliProfile);
            }
            else
            {
                Message(player, "CustomGiven", amount, item.info.displayName.translated, heliProfile);
            }

            if (!string.IsNullOrEmpty(_customRewardItem.Text))
                item.text = _customRewardItem.Text;

            player.inventory.GiveItem(item);
            return;
        }

        private void ProcessRewards(ulong heliId, ulong ownerId, string heliProfile)
        {
            if (heliProfile == null)
                return;

            var _heliProfile = config.heli.heliConfig[heliProfile];
            var _heliAttackers = HeliSignalData[heliId].Attackers;

            var totalReward = _heliProfile.Rewards.RewardPoints;
            var totalXP = _heliProfile.Rewards.XPReward;
            var totalScrap = _heliProfile.Rewards.ScrapReward;
            var totalCustom = _heliProfile.Rewards.CustomReward;
            float damageThreshold = _heliProfile.Rewards.DamageThreshold;

            int eligibleAttackers = 0;

            foreach (var attacker in _heliAttackers)
            {
                if (attacker.Value != null && attacker.Value.DamageDealt >= damageThreshold)
                    eligibleAttackers++;
            }

            double turretPenalty = (100 - config.heli.turretPenalty) / 100;

            if (!HeliSignalData.ContainsKey(heliId))
                return;

            var _rewardConf = config.rewards;
            if ((totalReward > 0))
            {
                if (_rewardConf.shareRewards)
                {
                    foreach (var playerId in _heliAttackers.Keys)
                    {
                        if (!_heliAttackers.ContainsKey(playerId))
                            continue;

                        float damageDealt = _heliAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalReward / eligibleAttackers;
                            float damageRatio = (_heliAttackers[playerId].TurretDamage / _heliAttackers[playerId].DamageDealt);
                            if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = amount * turretPenalty;

                            GiveReward(playerId, amount, heliProfile);
                        }
                    }
                }
                else
                {
                    if (!_heliAttackers.ContainsKey(ownerId))
                        return;

                    float damageRatio = (_heliAttackers[ownerId].TurretDamage / _heliAttackers[ownerId].DamageDealt);
                    if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                        totalReward = totalReward * turretPenalty;

                    GiveReward(ownerId, totalReward, heliProfile);
                }
            }

            if (totalXP > 0)
            {
                if (_rewardConf.shareXP)
                {
                    foreach (var playerId in _heliAttackers.Keys)
                    {
                        if (!_heliAttackers.ContainsKey(playerId))
                            continue;

                        float damageDealt = _heliAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalXP / eligibleAttackers;
                            float damageRatio = (_heliAttackers[playerId].TurretDamage / _heliAttackers[playerId].DamageDealt);
                            if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = amount * turretPenalty;

                            GiveXP(playerId, amount, heliProfile);
                        }
                    }
                }
                else
                {
                    if (!_heliAttackers.ContainsKey(ownerId))
                        return;

                    float damageRatio = (_heliAttackers[ownerId].TurretDamage / _heliAttackers[ownerId].DamageDealt);
                    if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                        totalXP = totalXP * turretPenalty;

                    GiveXP(ownerId, totalXP, heliProfile);
                }
            }

            if (totalScrap > 0)
            {
                if (_rewardConf.shareScrap)
                {
                    foreach (var playerId in _heliAttackers.Keys)
                    {
                        if (!_heliAttackers.ContainsKey(playerId))
                            continue;

                        float damageDealt = _heliAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalScrap / eligibleAttackers;
                            float damageRatio = (_heliAttackers[playerId].TurretDamage / _heliAttackers[playerId].DamageDealt);
                            if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = (int)(amount * turretPenalty);

                            GiveScrap(playerId, amount, heliProfile);
                        }
                    }
                }
                else
                {
                    if (!_heliAttackers.ContainsKey(ownerId))
                        return;

                    float damageRatio = (_heliAttackers[ownerId].TurretDamage / _heliAttackers[ownerId].DamageDealt);
                    if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                        totalScrap = (int)(totalScrap * turretPenalty);

                    GiveScrap(ownerId, totalScrap, heliProfile);
                }
            }

            if (totalCustom > 0)
            {
                if (_rewardConf.shareCustomReward)
                {
                    foreach (var playerId in _heliAttackers.Keys)
                    {
                        if (!_heliAttackers.ContainsKey(playerId))
                            continue;

                        float damageDealt = _heliAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalCustom / eligibleAttackers;
                            float damageRatio = (_heliAttackers[playerId].TurretDamage / _heliAttackers[playerId].DamageDealt);
                            if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = (int)(amount * turretPenalty);

                            GiveCustomReward(playerId, amount, heliProfile);
                        }
                    }
                }
                else
                {
                    if (!_heliAttackers.ContainsKey(ownerId))
                        return;

                    float damageRatio = (_heliAttackers[ownerId].TurretDamage / _heliAttackers[ownerId].DamageDealt);
                    if (config.heli.allowTurretDamage && config.heli.turretPenalty > 0f && damageRatio > 0.5f)
                        totalCustom = (int)(totalCustom * turretPenalty);

                    GiveCustomReward(ownerId, totalCustom, heliProfile);
                }
            }
        }

        #endregion Rewards

        #region Helpers

        private void ConfigureLockedCrateMapMarker(HackableLockedCrate crate)
        {
            if (crate == null || crate.IsDestroyed || MapMarkerInstanceField == null)
                return;

            BaseEntity marker = MapMarkerInstanceField.GetValue(crate) as BaseEntity;

            if (marker != null && !marker.IsDestroyed)
                marker.Kill(BaseNetworkable.DestroyMode.None, true);

            // Prevent the crate from later trying to kill the destroyed marker again.
            MapMarkerInstanceField.SetValue(crate, null);
        }

        private void HeliSignalThrown(BasePlayer player, SupplySignal entity, string heliProfile)
        {
            if (player == null || entity == null)
                return;

            ulong skinId = entity.skinID;
            int heliAmount = 1;
            string permSuffix = string.Empty;
            bool isWaveHeli = false;
            List<string> waveProfileCache = new List<string>();
            string initialWaveProfile = string.Empty;

            if (string.IsNullOrEmpty(heliProfile))
            {
                HeliProfileCache.TryGetValue(skinId, out heliProfile);
                if (string.IsNullOrEmpty(heliProfile))
                {
                    if (entity != null || !entity.IsDestroyed)
                        NextTick(()=> entity.Kill());
                    
                    return;
                }
            }

            if (DeepSeaManager.IsInsideDeepSea(player.transform.position) && !config.heli.allowInDeepSea)
            {
                Message(player, "NotInsideDeepSea", heliProfile);
                if (entity != null || !entity.IsDestroyed)
                    NextTick(()=> entity.Kill());
                
                timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                return;
            }

            if (config.heli.heliConfig.ContainsKey(heliProfile))
            {
                heliAmount = config.heli.heliConfig[heliProfile].Init.HeliAmount;
                permSuffix = config.heli.heliConfig[heliProfile].Init.GiveItemCommand.ToLower();
            }
            else if (config.heli.waveConfig.ContainsKey(heliProfile))
            {
                var waveProfile = config.heli.waveConfig[heliProfile].WaveProfiles[0];
                heliAmount = config.heli.heliConfig[waveProfile].Init.HeliAmount;
                permSuffix = config.heli.waveConfig[heliProfile].GiveItemCommand.ToLower();
                isWaveHeli = true;
                if (config.heli.waveConfig[heliProfile].WaveProfiles.Count > 0)
                {
                    foreach (var profile in config.heli.waveConfig[heliProfile].WaveProfiles)
                    {
                        if (!config.heli.heliConfig.ContainsKey(profile))
                        {
                            Log($"WaveHeli config contains a profile with an incorrect name ({profile}) please correct!", LogLevel.ERROR);
                            Message(player, "WaveProfileError", heliProfile);
                            if (entity != null || !entity.IsDestroyed)
                                NextTick(()=> entity.Kill());
                            
                            timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                            return;
                        }
                        waveProfileCache.Add(profile);
                    }
                    initialWaveProfile = waveProfileCache[0];
                }
                else
                {
                    Message(player, "NoWaveProfiles", heliProfile);
                    if (entity != null || !entity.IsDestroyed)
                        NextTick(()=> entity.Kill());
                    
                    timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                    return;
                }
            }

            bool isMultiHeli = false;
            if (!isWaveHeli && heliAmount > 1)
            {
                isMultiHeli = true;
            }

            var perm = $"{Name.ToLower()}.{permSuffix}";
            if (!permission.UserHasPermission(player.UserIDString, perm))
            {
                Message(player, "Permission", heliProfile);
                if (entity != null || !entity.IsDestroyed)
                    NextTick(()=> entity.Kill());
                
                timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                return;
            }

            if (config.heli.useNoEscape && NoEscape)
            {
                if ((bool)NoEscape?.CallHook("IsRaidBlocked", player))
                {
                    Message(player, "RaidBlocked", heliProfile);
                    if (entity != null || !entity.IsDestroyed)
                        NextTick(()=> entity.Kill());
                    
                    timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                    return;
                }
                else if ((bool)NoEscape?.CallHook("IsCombatBlocked", player))
                {
                    Message(player, "CombatBlocked", heliProfile);
                    if (entity != null || !entity.IsDestroyed)
                        NextTick(()=> entity.Kill());
                    
                    timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                    return;
                }
            }

            if (config.heli.playerCooldown > 0f && !permission.UserHasPermission(player.UserIDString, permBypasscooldown))
            {
                float cooldown;
                ulong userId = player.userID;
                player.inventory.containerBelt.SetLocked(false);
                if (config.heli.tierCooldowns && TierCooldowns.ContainsKey(userId))
                {
                    if (TierCooldowns[userId].TryGetValue(heliProfile, out cooldown))
                    {
                        TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                        Message(player, "CooldownTime", time.ToString(@"hh\:mm\:ss"));
                        if (entity != null || !entity.IsDestroyed)
                            NextTick(()=> entity.Kill());
                        
                        timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                        return;
                    }
                    else if (config.heli.teamCooldown)
                    {
                        foreach (var playerId in TierCooldowns.Keys)
                        {
                            if (TierCooldowns[userId].TryGetValue(heliProfile, out cooldown) && IsOwnerOrFriend(userId, playerId))
                            {
                                TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                                Message(player, "TeamCooldownTime", time.ToString(@"hh\:mm\:ss"));
                                if (entity != null || !entity.IsDestroyed)
                                    NextTick(()=> entity.Kill());
                                
                                timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                                return;
                            }
                        }
                    }
                }
                else
                {
                    if (PlayerCooldowns.TryGetValue(userId, out cooldown))
                    {
                        TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                        Message(player, "CooldownTime", time.ToString(@"hh\:mm\:ss"));
                        if (entity != null || !entity.IsDestroyed)
                            NextTick(()=> entity.Kill());
                        
                        timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                        return;
                    }
                    else if (config.heli.teamCooldown)
                    {
                        foreach (var playerId in PlayerCooldowns.Keys)
                        {
                            if (PlayerCooldowns.TryGetValue(playerId, out cooldown) && IsOwnerOrFriend(userId, playerId))
                            {
                                TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                                Message(player, "TeamCooldownTime", time.ToString(@"hh\:mm\:ss"));
                                if (entity != null || !entity.IsDestroyed)
                                    NextTick(()=> entity.Kill());
                                
                                timer.Once(1f, () => { GiveHeliSignal(player, skinId, heliProfile, 1); });
                                return;
                            }
                        }
                    }
                }

                cooldown = config.heli.playerCooldown;
                foreach (KeyValuePair<string, float> keyPair in config.heli.vipCooldowns)
                {
                    if (permission.UserHasPermission(player.UserIDString, keyPair.Key))
                    {
                        if (keyPair.Value < cooldown)
                        {
                            cooldown = keyPair.Value;
                            continue;
                        }
                    }
                }

                if (config.heli.tierCooldowns)
                {
                    if (!TierCooldowns.ContainsKey(userId))
                        TierCooldowns.Add(userId, new Dictionary<string, float>());

                    if (!TierCooldowns[userId].ContainsKey(heliProfile))
                        TierCooldowns[userId].Add(heliProfile, Time.time + cooldown);
                    else
                        TierCooldowns[userId][heliProfile] = Time.time + cooldown;

                    timer.Once(cooldown, () =>
                    {
                        if (!TierCooldowns.ContainsKey(userId))
                            return;

                        if (TierCooldowns[userId].ContainsKey(heliProfile))
                            TierCooldowns[userId].Remove(heliProfile);
                    });
                }
                else
                {
                    if (!PlayerCooldowns.ContainsKey(userId))
                        PlayerCooldowns.Add(userId, Time.time + cooldown);
                    else
                        PlayerCooldowns[userId] = Time.time + cooldown;

                    timer.Once(cooldown, () =>
                    {
                        if (PlayerCooldowns.ContainsKey(userId))
                            PlayerCooldowns.Remove(userId);
                    });
                }
            }

            HeliSignalComponent signalComponent = entity?.gameObject.AddComponent<HeliSignalComponent>();
            if (signalComponent != null)
            {
                signalComponent.signal = entity;
                signalComponent.player = player;
                signalComponent.skinId = isWaveHeli ? config.heli.heliConfig[initialWaveProfile].Init.SignalSkinID : skinId;
                signalComponent.heliProfile = isWaveHeli ? initialWaveProfile : heliProfile;
                signalComponent.heliAmount = heliAmount;
                signalComponent.isWaveHeli = isWaveHeli;
                signalComponent.isMultiHeli = isMultiHeli;
                if (isWaveHeli)
                {
                    signalComponent.waveProfile = heliProfile;
                    signalComponent.waveSkinId = skinId;
                    signalComponent.waveProfileCache = waveProfileCache;
                }
            }
        }

        private void ProcessHeliEnt(BaseEntity entity, string heliProfile)
        {
            if (entity == null || heliProfile == null)
                return;

            var _heliProfile = config.heli.heliConfig[heliProfile];
            if (_heliProfile == null)
                return;

            if (entity is HelicopterDebris)
            {
                HelicopterDebris debris = entity as HelicopterDebris;
                if (debris == null)
                    return;
                
                if (_heliProfile.Gibs.KillGibs)
                {
                    debris.Kill();
                }
                else if (_heliProfile.Crates.DisableFire) // DEBUG: Change this later to use new separate Fireball options
                {
                    debris.CancelInvoke(debris.OnCooledDown);
                    debris.OnCooledDown();
                }
                else if (_heliProfile.Gibs.GibsHotTime > 0)
                {
                    debris.CancelInvoke(debris.OnCooledDown);
                    debris.Invoke(debris.OnCooledDown, _heliProfile.Gibs.GibsHotTime);
                }

                if (_heliProfile.Gibs.ProtectGibs && _heliProfile.Gibs.UnlockGibs > 0)
                {
                    // DEBUG: Change this later to use new separate Fireball options
                    float unlockTime = _heliProfile.Crates.DisableFire ? _heliProfile.Gibs.UnlockGibs :
                                        (_heliProfile.Crates.FireDuration + _heliProfile.Gibs.UnlockGibs);
                    RemoveHeliOwner(debris, unlockTime);
                }
                debris.InitializeHealth(_heliProfile.Gibs.GibsHealth, _heliProfile.Gibs.GibsHealth);
                debris.SendNetworkUpdateImmediate();
            }
            else if (entity is FireBall)
            {
                // DEBUG: Take over fireball creation, write custom OnDied logic, 
                // harmony patch, or is there existing hook we can intercept?

                var fireball = entity as FireBall;
                if (fireball == null)
                    return;
                
                fireball.tickRate = _heliProfile.Fireball.DamageRate;
                fireball.lifeTimeMin = _heliProfile.Fireball.MinimumLifeTime;
                fireball.lifeTimeMax = _heliProfile.Fireball.MaximumLifeTime;
                fireball.damagePerSecond = _heliProfile.Fireball.DamagePerSecond;
                fireball.waterToExtinguish = _heliProfile.Fireball.WaterAmountToExtinguish;
                fireball.generation = (_heliProfile.Fireball.SpreadChance == 0) ? 9f : (1f - _heliProfile.Fireball.SpreadChance) / 0.1f;

                if (_heliProfile.Crates.DisableFire)
                {
                    fireball.Extinguish();
                }
                else
                {
                    float lifeTime = Random.Range(fireball.lifeTimeMax, fireball.lifeTimeMin);
                    fireball.Invoke(() => fireball.Extinguish(), lifeTime);
                    
                    float spreadDelay = lifeTime * _heliProfile.Fireball.SpreadAtLifetimePercent;
                    fireball.Invoke(() => fireball.TryToSpread(), spreadDelay);
                }
            }
            // DEBUG: Check locked crates are handled properly
            /*else if (entity is LockedByEntCrate)
            {
                // DEBUG: Move fireball creation to crate spawning, remove this from here
                var crate = entity as LockedByEntCrate;
                if (crate != null)
                {
                    if (_heliProfile.Crates.FireDuration > 0 && !_heliProfile.Crates.DisableFire)
                    {
                        timer.Once(_heliProfile.Crates.FireDuration, () =>
                        {
                            if (crate != null)
                            {
                                if (crate.lockingEnt != null)
                                {
                                    var lockingEnt = crate.lockingEnt.GetComponent<FireBall>();
                                    NextTick(() =>
                                    {
                                        if (lockingEnt != null)
                                            lockingEnt.Kill();
                                    });
                                }
                                crate.SetLockingEnt(null);
                            }
                        });
                    }
                    else
                    {
                        if (crate.lockingEnt != null)
                        {
                            var lockingEnt = crate.lockingEnt.GetComponent<FireBall>();
                            NextTick(() =>
                            {
                                if (lockingEnt != null)
                                    lockingEnt.Kill();
                            });
                        }
                        crate.SetLockingEnt(null);
                    }

                    if (_heliProfile.Crates.ProtectCrates && _heliProfile.Crates.UnlockCrates > 0)
                    {
                        float unlockTime = _heliProfile.Crates.DisableFire ? _heliProfile.Crates.UnlockCrates :
                                            (_heliProfile.Crates.FireDuration + _heliProfile.Crates.UnlockCrates);

                        RemoveHeliOwner(entity, unlockTime);
                    }

                    crate.CancelInvoke(crate.RemoveMe);
                    crate.Invoke(new Action(crate.RemoveMe), Math.Max(0f, _heliProfile.Crates.HeliCrateDespawn));
                }
            }*/
        }

        private List<PatrolHelicopter> GetPlayerActiveHelis(BasePlayer player)
        {
            if (player == null)
                return null;

            var result = new List<PatrolHelicopter>();

            foreach (var netId in HeliSignalData.Keys)
            {
                var heli = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as PatrolHelicopter;
                if (heli == null)
                    continue;

                if (heli.OwnerID == player.userID)
                    result.Add(heli);
            }

            return result;
        }

        private string PositionToGrid(Vector3 pos)
        {
            if (pos == null)
                return "Unkown";

            return MapHelper.PositionToString(pos);
        }

        private bool TryGetLockedCrateDecayTime(HackableLockedCrate crate, LockedCrateData crateData, out float decayTime)
        {
            decayTime = 0f;

            if (crate == null || crateData == null)
                return false;

            if (!HeliProfileCache.TryGetValue(crate.skinID, out string heliProfile))
                return false;

            if (string.IsNullOrEmpty(heliProfile))
                return false;

            if (!config.heli.heliConfig.TryGetValue(heliProfile, out HeliData heliData) || heliData == null)
                return false;

            decayTime = heliData.Crates.LockedCrateDespawn;

            return true;
        }

        private bool TryRefreshLockedCrateDecay(HackableLockedCrate crate, LockedCrateData crateData)
        {
            if (crate == null || crate.IsDestroyed || crateData == null)
                return false;

            if (!TryGetLockedCrateDecayTime(crate, crateData, out float configuredDecayTime))
                return false;

            crateData.DecayTime = configuredDecayTime;

            crate.CancelInvoke(crate.DelayedDestroy);

            if (crateData.DecayTime <= 0f)
            {
                Log(
                    $"Locked crate {crate.net?.ID.Value} has an invalid decay time " +
                    $"of {crateData.DecayTime}. Allowing native RefreshDecay.",
                    LogLevel.DEBUG
                );

                return false;
            }

            crate.Invoke(crate.DelayedDestroy, crateData.DecayTime);

            Log(
                $"RefreshDecay scheduled locked crate {crate.net?.ID.Value} for " +
                $"{crateData.DecayTime} seconds.",
                LogLevel.DEBUG
            );

            return true;
        }

        private bool IsDamageAuthorized(PatrolHelicopter heli, BaseCombatEntity entity)
        {
            HeliComponent heliComp = heli?.GetComponent<HeliComponent>();
            if (heliComp == null || heliComp.callingTeam == null || entity == null)
                return true;

            foreach (BasePlayer player in heliComp.callingTeam)
            {
                if (entity.GetBuildingPrivilege()?.IsAuthed(player) == true)
                    return true;
            }
            return false;
        }

        public Vector3 GetRandomOffset(Vector3 origin, float minRange, float maxRange = 0f, float minHeight = 30f, float maxHeight = 40f)
        {
            Vector3 vector3 = Random.onUnitSphere;
            vector3.y = 0f;
            vector3.Normalize();
            maxRange = Mathf.Max(minRange, maxRange);
            Vector3 vector31 = origin + (vector3 * Random.Range(minRange, maxRange));
            return GetAppropriatePosition(vector31, minHeight, maxHeight);
        }

        public Vector3 GetAppropriatePosition(Vector3 origin, float minHeight = 30f, float maxHeight = 40f)
        {
            RaycastHit raycastHit;
            float single = 50f;
            Ray ray = new Ray(origin + new Vector3(0f, single, 0f), Vector3.down);
            float single1 = 5f;
            if (Physics.SphereCast(ray, single1, out raycastHit, single * 2f - single1, LayerMask.GetMask(new string[] { "Terrain", "World", "Construction", "Water" })))
            {
                origin = raycastHit.point;
            }
            origin.y += Random.Range(minHeight, maxHeight);
            return origin;
        }

        private object OnCustomHelicopterTarget(HelicopterTurret turret, BasePlayer player, string heliProfile)
        {
            if (turret == null || player == null || heliProfile == null)
                return null;

            if (Vanish && (bool)Vanish?.Call("IsInvisible", player))
                return null;

            PatrolHelicopter heli = turret._heliAI?.helicopterBase;
            if (heli == null)
                return null;

            if (!config.heli.heliConfig[heliProfile].TargetingDamage.TargetOtherPlayers && !IsOwnerOrFriend(player.userID, heli.OwnerID))
                return false;
            else if (!config.heli.targetUnderWater && player.WaterFactor() >= 1f)
                return false;

            if (config.heli.maxHitDistance > 0 && (Vector3.Distance(heli.transform.position, player.transform.position) > config.heli.maxHitDistance))
                return false;

            return null;
        }

        private object CanCustomHeliTarget(PatrolHelicopterAI heliAI, BasePlayer player, string heliProfile)
        {
            if (heliAI == null || player == null || heliProfile == null)
                return null;

            if (Vanish && (bool)Vanish?.Call("IsInvisible", player))
                return null;

            PatrolHelicopter heli = heliAI?.helicopterBase;
            if (heli == null)
                return null;

            if (config.heli.heliConfig.ContainsKey(heliProfile))
            {
                if (!config.heli.heliConfig[heliProfile].TargetingDamage.TargetOtherPlayers && !IsOwnerOrFriend(player.userID, heli.OwnerID))
                {
                    HeliComponent heliComp = heli.GetComponent<HeliComponent>();
                    if (heliComp != null)
                    {
                        heliAI.interestZoneOrigin = heliComp.calledPosition;
                        heliAI.hasInterestZone = true;
                    }
                    return false;
                }
            }
            if (config.heli.maxHitDistance > 0 && (Vector3.Distance(heli.transform.position, player.transform.position) > config.heli.maxHitDistance))
                return false;

            return null;
        }

        private void LoadProfileCache()
        {
            HeliProfileCache.Clear();
            HeliProfiles.Clear();
            WaveProfiles.Clear();

            var _heliConfig = config.heli.heliConfig;

            foreach (var key in _heliConfig.Keys)
            {
                var permSuffix = _heliConfig[key].Init.GiveItemCommand.ToLower();
                var perm = $"{Name.ToLower()}.{permSuffix}";
                permission.RegisterPermission(perm, this);

                if (HeliProfiles.Contains(_heliConfig[key].Init.GiveItemCommand))
                {
                    Log($"Duplicate 'Profile Shortname' for HeliProfile: {key} with Profile Shortname: {_heliConfig[key].Init.GiveItemCommand}, these must be unique for each profile. Correct your config & reload.", LogLevel.ERROR);
                    continue;
                }
                else
                {
                    HeliProfiles.Add(_heliConfig[key].Init.GiveItemCommand);
                }

                if (HeliProfileCache.ContainsKey(_heliConfig[key].Init.SignalSkinID))
                {
                    Log($"Duplicate 'SkinID' for HeliProfile: {key} with SkinID: {_heliConfig[key].Init.SignalSkinID}, these must be unique for each profile. Correct your config & reload.", LogLevel.ERROR);
                    continue;
                }
                else
                {
                    HeliProfileCache.Add(_heliConfig[key].Init.SignalSkinID, key);
                }
            }

            var _waveConfig = config.heli.waveConfig;
            bool doSave = false;

            foreach (var key in _waveConfig.Keys)
            {
                var permSuffix = _waveConfig[key].GiveItemCommand.ToLower();
                var perm = $"{Name.ToLower()}.{permSuffix}";
                permission.RegisterPermission(perm, this);

                if (WaveProfiles.Contains(_waveConfig[key].GiveItemCommand))
                {
                    Log($"Duplicate 'Profile Shortname' for WaveProfile: {key} with Profile Shortname: {_waveConfig[key].GiveItemCommand}, these must be unique for each profile. Correct your config & reload.", LogLevel.ERROR);
                    continue;
                }
                else
                {
                    WaveProfiles.Add(_waveConfig[key].GiveItemCommand);
                }

                if (HeliProfileCache.ContainsKey(_waveConfig[key].SkinId))
                {
                    Log($"Duplicate 'SkinID' for WaveProfile: {key} with SkinID: {_waveConfig[key].SkinId}, these must be unique for each profile. Correct your config & reload.", LogLevel.ERROR);
                    continue;
                }
                else
                {
                    HeliProfileCache.Add(_waveConfig[key].SkinId, key);
                }

                if (_waveConfig[key].WaveProfiles.Count == 0)
                {
                    // If loading empty, populate default wave profiles as examples
                    foreach (var profile in _heliConfig.Keys)
                    {
                        doSave = true;
                        _waveConfig[key].WaveProfiles.Add(profile);
                    }
                }
            }

            if (doSave) SaveConfig();
        }

        private static ConsoleSystem.Arg RunSilentCommand(string strCommand, params object[] args)
        {
            var command = ConsoleSystem.BuildCommand(strCommand, args);
            var arg = new ConsoleSystem.Arg(ConsoleSystem.Option.Unrestricted, command);
            if (arg.Invalid || !arg.cmd.Variable) return null;
            arg.cmd.Call(arg);
            return arg;
        }

        // DEBUG: Check this out, support request regarding incorrect team, ?update team regularly, 
        // people joining the team after event start arent included
        private List<BasePlayer> GetAttackingTeam(BasePlayer player)
        {
            List<BasePlayer> players = new List<BasePlayer>();

            if (!players.Contains(player))
                players.Add(player);

            if (config.options.useTeams)
            {
                RelationshipManager.PlayerTeam team;
                RelationshipManager.ServerInstance.playerToTeam.TryGetValue(player.userID, out team);
                if (team != null)
                {
                    foreach (var memberId in team.members)
                    {
                        BasePlayer member = FindPlayer(memberId.ToString());
                        if (member == null)
                            continue;

                        if (member.IsConnected && !players.Contains(member))
                            players.Add(member);
                    }
                }
            }

            if (config.options.useClans && Clans)
            {
                List<string> clan = (List<string>)Clans?.Call("GetClanMembers", player.UserIDString);
                if (clan != null)
                {
                    foreach (var memberId in clan)
                    {
                        BasePlayer member = FindPlayer(memberId);
                        if (member == null)
                            continue;

                        if (member.IsConnected && !players.Contains(member))
                            players.Add(member);
                    }
                }
            }

            if (config.options.useFriends && Friends)
            {
                string[] friends = (string[])Friends?.Call("GetFriendList", player.UserIDString);
                if (friends != null)
                {
                    foreach (var friendId in friends)
                    {
                        BasePlayer friend = FindPlayer(friendId.ToString());
                        if (friend == null)
                            continue;

                        if (friend.IsConnected && !players.Contains(friend))
                            players.Add(friend);
                    }
                }
            }

            return players;
        }

        private void AnnounceToChat(BasePlayer player, string message)
        {
            if (player == null || message == null)
                return;

            if (config.announce.announceGlobal)
            {
                Server.Broadcast(message, config.options.usePrefix ? config.options.chatPrefix : null, config.options.chatIcon);
                return;
            }

            List<BasePlayer> players = GetAttackingTeam(player);
            if (players == null || players.Count <= 0)
                return;

            foreach (var member in players)
            {
                Player.Message(member, message, config.options.usePrefix ? config.options.chatPrefix : null, config.options.chatIcon);
            }
        }

        private bool IsBP(ItemDefinition itemDef)
        {
            return itemDef?.Blueprint != null && itemDef.Blueprint.isResearchable && !itemDef.Blueprint.defaultBlueprint;
        }

        private bool CanPluginPurchase()
        {
            foreach (var key in config.heli.heliConfig.Keys)
            {
                if (config.heli.heliConfig[key].Init.UseBuyCommand && config.purchasing.defaultCurrency != "Custom")
                    return true;
            }
            return false;
        }

        private bool IsOwnerOrFriend(ulong playerId, ulong targetId)
        {
            if (playerId == 0 || targetId == 0)
                return false;

            if (playerId == targetId)
                return true;

            if (config.options.useTeams)
            {
                if (RelationshipManager.ServerInstance?.playerToTeam != null)
                {
                    if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(playerId, out RelationshipManager.PlayerTeam team) && team?.members != null)
                    {
                        if (team.members.Contains(targetId))
                            return true;
                    }
                }
            }

            if (config.options.useClans && Clans)
            {
                try
                {
                    var result = Clans.Call("IsMemberOrAlly", playerId, targetId);
                    if (result != null && (bool)result)
                        return true;
                }
                catch { }
            }

            if (config.options.useFriends && Friends)
            {
                try
                {
                    var result = Friends.Call("AreFriends", playerId, targetId);
                    if (result != null && (bool)result)
                        return true;
                }
                catch { }
            }

            return false;
        }

        private object CheckAndFixSignal(Item signal)
        {
            if (signal == null)
                return null;

            string heliProfile;
            ulong skinId = signal.skin;
            if (HeliProfileCache.TryGetValue(skinId, out heliProfile))
            {
                signal.name = heliProfile;
                signal.skin = skinId;
                signal.MarkDirty();
            }
            return signal;
        }

        private void RemoveHeliOwner(BaseNetworkable entity, float time)
        {
            timer.Once(time, () =>
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    (entity as BaseEntity).OwnerID = 0;
                    entity.SendNetworkUpdateImmediate();
                }
            });
        }

        private void RetireHeli(PatrolHelicopterAI heliAI, ulong heliId, float retireTime = 1f)
        {
            timer.Once(retireTime, () =>
            {
                if (heliAI == null || heliAI.isDead)
                    return;

                PatrolHelicopter heli = heliAI.helicopterBase;
                if (heli == null || heli.IsDestroyed)
                    return;

                HeliComponent heliComp = heli.GetComponent<HeliComponent>();
                if (heliComp != null)
                {
                    heliComp.isRetiring = true;
                    heliComp.RemoveHeliData(1f);
                }
                else
                {
                    HeliSignalData?.Remove(heliId);
                }

                heliAI.Retire();
            });
        }

        private IPlayer GetIPlayer(BasePlayer player)
        {
            if (player == null)
                return null;

            return covalence.Players.FindPlayerById(player.UserIDString);
        }

        private bool GiveHeliSignal(BasePlayer player, ulong skinId, string heliName, int dropAmount)
        {
            if (player != null && player.IsAlive())
            {
                Item heliSignal = ItemManager.CreateByItemID(supplySignalId, dropAmount, skinId);
                heliSignal.name = heliName;
                heliSignal.MarkDirty();
                if (player.inventory.GiveItem(heliSignal))
                    return true;

                heliSignal.Remove(0);
            }
            return false;
        }

        private bool IsInSafeZone(Vector3 position) // DEBUG: Check this, isn't there a native check for this in BaseEntity?
        {
            int loop = Physics.OverlapSphereNonAlloc(position, 1f, Vis.colBuffer, 1 << 18, QueryTriggerInteraction.Collide);
            for (int i = 0; i < loop; i++)
            {
                Collider collider = Vis.colBuffer[i];
                if (collider.GetComponent<TriggerSafeZone>())
                    return true;
            }
            return false;
        }

        private BasePlayer FindPlayer(string partialNameOrId)
        {
            BasePlayer basePlayer = null;

            foreach (var player in covalence.Players.Connected)
            {
                if (player.Id == partialNameOrId)
                {
                    basePlayer = player?.Object as BasePlayer;
                    break;
                }
                if (player.Name.ToLower().Contains(partialNameOrId.ToLower()))
                {
                    basePlayer = player?.Object as BasePlayer;
                    break;
                }
            }

            return basePlayer;
        }

        private void RunHeliDeathCommands(HeliData.CommandsToRunOptions commandOptions, ulong ownerId, string heliProfile, HeliComponent heliComp)
        {
            if (commandOptions?.RunCommands != true || commandOptions.Commands == null || commandOptions.Commands.Count == 0)
                return;

            string ownerIdString = ownerId.ToString();
            string profileName = heliProfile ?? string.Empty;

            List<BasePlayer> callingTeam = null;

            foreach (string configuredCommand in commandOptions.Commands)
            {
                if (string.IsNullOrWhiteSpace(configuredCommand))
                    continue;

                string command = configuredCommand.Trim();

                // Commands containing {teamIds} are run once for each calling team member
                if (command.Contains("{teamIds}"))
                {
                    if (callingTeam == null)
                    {
                        heliComp?.GetCallingTeam();

                        callingTeam = new List<BasePlayer>();
                        HashSet<ulong> addedPlayers = new HashSet<ulong>();

                        if (heliComp?.callingTeam != null)
                        {
                            foreach (BasePlayer player in heliComp.callingTeam)
                            {
                                if (player != null &&
                                    player.userID.IsSteamId() &&
                                    addedPlayers.Add(player.userID))
                                {
                                    callingTeam.Add(player);
                                }
                            }
                        }
                    }

                    foreach (BasePlayer teamMember in callingTeam)
                    {
                        string teamCommand = command.Replace("{ownerId}", ownerIdString).Replace("{teamIds}", teamMember.UserIDString).Replace("{heliProfile}", profileName);
                        RunServerCommand(teamCommand);
                    }

                    continue;
                }

                // Commands without {teamIds} run once
                command = command.Replace("{ownerId}", ownerIdString).Replace("{heliProfile}", profileName);
                RunServerCommand(command);
            }
        }

        private void RunServerCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                Log($"Running heli death command: {command}", LogLevel.DEBUG);
                Server.Command(command);
            }
            catch (Exception ex)
            {
                Log($"Failed to run heli death command '{command}': {ex.Message}", LogLevel.ERROR);
            }
        }

        #endregion Helpers

        #region Harmony Patch Helpers

        // Helper method that Harmony patch classes can call
        public static HeliComponent GetCachedHeliSignal(PatrolHelicopterAI heliAi)
        {
            if (heliAi == null || heliAi.helicopterBase.skinID == 0)
                return null;

            if (heliCompCache.TryGetValue(heliAi, out var heliComp))
                return heliComp;

            heliComp = heliAi.helicopterBase.GetComponent<HeliComponent>();

            if (heliComp != null)
                heliCompCache[heliAi] = heliComp;

            return heliComp;
        }

        private void ApplyHarmonyPatches()
        {
            try
            {
                harmony = new Harmony(HarmonyPatchId);

                _updateTargetListMethod = AccessTools.Method(typeof(PatrolHelicopterAI), "UpdateTargetList");
                _fireRocketMethod = AccessTools.Method(typeof(PatrolHelicopterAI), "FireRocket");
                _updateNetworkGroup = AccessTools.Method(typeof(BaseEntity), "UpdateNetworkGroup");

                int patchCount = 0;

                if (_updateTargetListMethod != null)
                {
                    harmony.Patch(_updateTargetListMethod, prefix: new HarmonyMethod(typeof(PatrolHeliUpdateTargetListPatch), "Prefix"));
                    
                    Log($"✓ Patched: PatrolHelicopterAI.UpdateTargetList", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("PatrolHelicopterAI.UpdateTargetList method not found!", LogLevel.ERROR);
                }

                if (_fireRocketMethod != null)
                {
                    harmony.Patch(_fireRocketMethod, prefix: new HarmonyMethod(typeof(PatrolHeliFireRocketPatch), "Prefix"));
                    
                    Log($"✓ Patched: PatrolHelicopterAI.FireRocket", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("PatrolHelicopterAI.FireRocket method not found!", LogLevel.ERROR);
                }

                if (_updateNetworkGroup != null)
                {
                    harmony.Patch(_updateNetworkGroup, prefix: new HarmonyMethod(typeof(UpdateNetworkGroupPatch), "Prefix"));
                    
                    Log($"✓ Patched: BaseEntity.UpdateNetworkGroup", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("BaseEntity.UpdateNetworkGroup method not found!", LogLevel.ERROR);
                }

                Log($"Successfully applied {patchCount} Harmony patches!", LogLevel.DEBUG);
            }
            catch (System.Exception ex)
            {
                Log($"Failed to apply Harmony patches:", LogLevel.ERROR);
                Log($"Error Message: {ex.Message}", LogLevel.ERROR);
                Log($"Stack Trace: {ex.StackTrace}", LogLevel.ERROR);
                Log($"Please report to ZEODE @ https://zeode.io", LogLevel.ERROR);
            }
        }

        #endregion Harmony Patch Helpers

        #region Commands

        private void CmdReport(IPlayer player, string command, string[] args)
        {
            string activeHelis = String.Empty;
            int count = 0;
            int total = HeliSignalData.Count;

            if (total == 0)
            {
                Message(player, "HeliReportTitleCon", "NO");
                return;
            }

            if (player.IsServer)
                Message(player, "HeliReportTitleCon", total);
            else
                Message(player, "HeliReportTitle", total);

            foreach (var netId in HeliSignalData.Keys)
            {
                var heli = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as PatrolHelicopter;
                if (heli != null)
                {
                    if (!IsOwnerOrFriend(heli.OwnerID, UInt64.Parse(player.Id)) && !permission.UserHasPermission(player.Id, permAdmin))
                        continue;

                    count++;

                    Vector3 position = heli.transform.position;
                    var gridPos = PositionToGrid(position);
                    if (gridPos == null)
                        gridPos = "Unknown";

                    HeliComponent heliComp = heli.GetComponent<HeliComponent>();
                    if (heliComp == null)
                        continue;

                    BasePlayer owner = heliComp.owner;
                    if (owner == null)
                        continue;

                    string heliProfile = heliComp.heliProfile;
                    if (heliProfile == null)
                        continue;

                    var message = String.Empty;
                    if (player.IsServer)
                        message = PlayerLang(
                            "HeliReportItemCon",
                            player.Id,
                            count,
                            total,
                            config.heli.heliConfig[heliProfile].Init.HeliName,
                            owner.displayName,
                            gridPos,
                            Math.Round((decimal)heli.health, 0),
                            Math.Round((decimal)heli.weakspots[0].health, 0),
                            Math.Round((decimal)heli.weakspots[1].health, 0),
                            heliComp.heliAI._currentState
                        );
                    else
                        message = PlayerLang(
                            "HeliReportItem",
                            player.Id,
                            count,
                            total,
                            config.heli.heliConfig[heliProfile].Init.HeliName,
                            owner.displayName,
                            gridPos,
                            Math.Round((decimal)heli.health, 0),
                            Math.Round((decimal)heli.weakspots[0].health, 0),
                            Math.Round((decimal)heli.weakspots[1].health, 0),
                            heliComp.heliAI._currentState
                        );

                    activeHelis += ($"{message}");
                    message = String.Empty;
                }
            }

            if (config.options.usePrefix)
            {
                config.options.usePrefix = false;
                Message(player, "HeliReportList", activeHelis);
                config.options.usePrefix = true;
            }
            else
            {
                Message(player, "HeliReportList", activeHelis);
            }
            activeHelis = String.Empty;
        }

        private void CmdRetireHeli(IPlayer player, string command, string[] args)
        {
            if (player.IsServer)
            {
                Message(player, "InGameOnly");
                return;
            }

            BasePlayer basePlayer = FindPlayer(player.Id);
            if (basePlayer == null)
                return;

            Dictionary<float, List<PatrolHelicopter>> waveHelis = new Dictionary<float, List<PatrolHelicopter>>();
            Dictionary<float, List<PatrolHelicopter>> multiHelis = new Dictionary<float, List<PatrolHelicopter>>();
            List<PatrolHelicopter> singleHelis = new List<PatrolHelicopter>();
            bool didRetireAny = false;

            // Collect all helis that will be retired
            var helisToRetire = new List<PatrolHelicopter>();

            var heliSignalIds = new List<ulong>(HeliSignalData.Keys);
            foreach (var netId in heliSignalIds)
            {
                var heli = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as PatrolHelicopter;
                if (heli == null)
                    continue;

                var heliComp = heli.GetComponent<HeliComponent>();
                if (heliComp == null)
                    continue;

                if (heliComp.isRetiring || heliComp.isDying)
                    continue;

                if (heli.OwnerID == basePlayer.userID || (config.heli.canTeamRetire && IsOwnerOrFriend(heli.OwnerID, basePlayer.userID)))
                {
                    helisToRetire.Add(heli);
                }
            }

            foreach (var heli in helisToRetire)
            {
                var heliComp = heli.GetComponent<HeliComponent>();

                didRetireAny = true;
                heliComp.isRetiring = true;
                heliComp.retireCmdUsed = true;
                heliComp.retiringPlayer = basePlayer;

                var heliId = heli.net.ID.Value;

                if (heliComp.isWaveHeli && HeliSignalData.ContainsKey(heliId))
                {
                    float waveTime = HeliSignalData[heliId].WaveTime;

                    if (!waveHelis.ContainsKey(waveTime))
                        waveHelis[waveTime] = new List<PatrolHelicopter>();
                    
                    waveHelis[waveTime].Add(heli);
                }
                else if (HeliSignalData.ContainsKey(heliId) && HeliSignalData[heliId].IsMultiHeli)
                {
                    float multiTime = HeliSignalData[heliId].MultiHeliTime;

                    if (!multiHelis.ContainsKey(multiTime))
                        multiHelis[multiTime] = new List<PatrolHelicopter>();
                    
                    multiHelis[multiTime].Add(heli);
                }
                else
                {
                    singleHelis.Add(heli);
                }

                RetireHeli(heli.myAI, heliId, 0f);
            }

            if (didRetireAny)
            {
                // Send grouped Discord notifications
                if (config.discord.sendHeliRetire)
                {
                    // Handle wave
                    foreach (var kvp in waveHelis)
                    {
                        if (!ProcessedRetirements.Contains(kvp.Key))
                        {
                            ProcessedRetirements.Add(kvp.Key);
                            SendGroupedRetireDiscord(kvp.Value, basePlayer, true, false);

                            // Clean up after a delay
                            timer.Once(30f, () => ProcessedRetirements.Remove(kvp.Key));
                        }
                    }

                    // Handle multi
                    foreach (var kvp in multiHelis)
                    {
                        if (!ProcessedRetirements.Contains(kvp.Key))
                        {
                            ProcessedRetirements.Add(kvp.Key);
                            SendGroupedRetireDiscord(kvp.Value, basePlayer, false, true);

                            // Clean up after a delay
                            timer.Once(30f, () => ProcessedRetirements.Remove(kvp.Key));
                        }
                    }

                    // Single helis will get their Discord notification through OnHelicopterRetire
                }

                // Send chat messages
                if (waveHelis.Count > 0)
                {
                    KeyValuePair<float, List<PatrolHelicopter>> firstWave = default(KeyValuePair<float, List<PatrolHelicopter>>);

                    foreach (var kvp in waveHelis)
                    {
                        firstWave = kvp;
                        break;
                    }

                    var heliComp = firstWave.Value[0].GetComponent<HeliComponent>();
                    var message = Lang("WaveRetired", heliComp.waveProfile);
                    AnnounceToChat(basePlayer, message);
                }
                else if (multiHelis.Count > 0)
                {
                    KeyValuePair<float, List<PatrolHelicopter>> firstMulti = default(KeyValuePair<float, List<PatrolHelicopter>>);

                    foreach (var kvp in multiHelis)
                    {
                        firstMulti = kvp;
                        break;
                    }

                    var heliData = HeliSignalData[firstMulti.Value[0].net.ID.Value];
                    var message = Lang("HeliRetired", heliData.MultiHeliProfile);
                    AnnounceToChat(basePlayer, message);
                }
                else if (singleHelis.Count > 0)
                {
                    var heliComp = singleHelis[0].GetComponent<HeliComponent>();
                    var message = Lang("HeliRetired", heliComp.heliProfile);
                    AnnounceToChat(basePlayer, message);
                }
            }
            else
            {
                Message(player, "NoRetiredHelis");
            }
        }

        private void CmdBuySignal(IPlayer player, string command, string[] args)
        {
            var _heliConfig = config.heli.heliConfig;
            if (player.IsServer)
            {
                Message(player, "InGameOnly");
                return;
            }
            else if (args?.Length < 1 || args?.Length > 1)
            {
                string buyHelis = String.Empty;
                foreach (var key in _heliConfig.Keys)
                {
                    if (_heliConfig[key].Init.UseBuyCommand)
                        buyHelis += $"{config.purchasing.buyCommand} {_heliConfig[key].Init.GiveItemCommand}\n";
                }
                buyHelis += ($"<color=green>------------------------</color>\n");

                string buyWaves = String.Empty;
                foreach (var key in config.heli.waveConfig.Keys)
                {
                    if (config.heli.waveConfig[key].UseBuyCommand)
                        buyWaves += $"{config.purchasing.buyCommand} {config.heli.waveConfig[key].GiveItemCommand}\n";
                }

                Message(player, "BuyCmdSyntax", buyHelis, buyWaves);
                return;
            }

            var _customCurrency = config.purchasing.customCurrency;
            string currencyItem = config.purchasing.defaultCurrency;
            string priceUnit = config.purchasing.purchaseUnit;
            string priceFormat;
            if (args?[0].ToLower() == "list")
            {
                string list = String.Empty;
                foreach (var key in _heliConfig.Keys)
                {
                    switch (currencyItem)
                    {
                        case "ServerRewards":
                            priceFormat = $"{_heliConfig[key].Init.CostToBuy} {priceUnit}";
                            break;
                        case "Economics":
                            priceFormat = $"{priceUnit}{_heliConfig[key].Init.CostToBuy}";
                            break;
                        default:
                            priceFormat = $"{_heliConfig[key].Init.CostToBuy} {_customCurrency.DisplayName}";
                            break;
                    }
                    if (_heliConfig[key].Init.UseBuyCommand) list += ($"{_heliConfig[key].Init.HeliName} : {priceFormat}\n");
                }

                list += ($"<color=green>------------------------</color>\n");

                foreach (var key in config.heli.waveConfig.Keys)
                {
                    switch (currencyItem)
                    {
                        case "ServerRewards":
                            priceFormat = $"{config.heli.waveConfig[key].CostToBuy} {priceUnit}";
                            break;
                        case "Economics":
                            priceFormat = $"{priceUnit}{config.heli.waveConfig[key].CostToBuy}";
                            break;
                        default:
                            priceFormat = $"{config.heli.waveConfig[key].CostToBuy} {_customCurrency.DisplayName}";
                            break;
                    }
                    if (config.heli.waveConfig[key].UseBuyCommand) list += ($"{key} : {priceFormat}\n");
                }

                Message(player, "PriceList", list);
                return;
            }

            string type = args[0].ToLower();
            ulong skinId = 0;
            string heliProfile = string.Empty;
            bool isWaveHeli = false;

            if (!HeliProfiles.Contains(type) && !WaveProfiles.Contains(type))
            {
                Message(player, "InvalidDrop", type);
                return;
            }

            if (!Instance.permission.UserHasPermission(player.Id, permBuy))
            {
                Message(player, "BuyPermission", type);
                return;
            }

            foreach (var key in _heliConfig.Keys)
            {
                if (type == _heliConfig[key].Init.GiveItemCommand.ToLower())
                {
                    skinId = _heliConfig[key].Init.SignalSkinID;
                    heliProfile = key;
                    break;
                }
            }
            foreach (var key in config.heli.waveConfig.Keys)
            {
                if (type == config.heli.waveConfig[key].GiveItemCommand.ToLower())
                {
                    skinId = config.heli.waveConfig[key].SkinId;
                    heliProfile = key;
                    isWaveHeli = true;
                    break;
                }
            }

            if (isWaveHeli && !config.heli.waveConfig[heliProfile].UseBuyCommand)
            {
                Message(player, "NoBuy", type);
                return;
            }
            else if (!isWaveHeli && !_heliConfig[heliProfile].Init.UseBuyCommand)
            {
                Message(player, "NoBuy", type);
                return;
            }

            BasePlayer basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return;
            switch (currencyItem)
            {
                case "ServerRewards":
                    {
                        var cost = isWaveHeli ? config.heli.waveConfig[heliProfile].CostToBuy : _heliConfig[heliProfile].Init.CostToBuy;
                        var balance = ServerRewards?.CallHook("CheckPoints", (ulong)basePlayer.userID);
                        if (balance != null)
                        {
                            if (cost <= Convert.ToInt32(balance))
                            {
                                if (GiveHeliSignal(basePlayer, skinId, heliProfile, 1))
                                {
                                    ServerRewards?.CallHook("TakePoints", (ulong)basePlayer.userID, cost);
                                    Message(player, "Receive", 1, heliProfile);
                                    return;
                                }
                                else
                                {
                                    Message(player, "FullInventory", heliProfile);
                                    return;
                                }
                            }
                            else
                            {
                                Message(player, "CantAfford", $"{cost} {priceUnit}", $"{cost - Convert.ToDouble(balance)} {priceUnit}");
                                return;
                            }
                        }
                    }
                    break;
                case "Economics":
                    {
                        var cost = Convert.ToDouble(isWaveHeli ? config.heli.waveConfig[heliProfile].CostToBuy : _heliConfig[heliProfile].Init.CostToBuy);
                        var balance = Economics?.CallHook("Balance", (ulong)basePlayer.userID);
                        if (balance != null)
                        {
                            if (cost <= Convert.ToDouble(balance))
                            {
                                if (GiveHeliSignal(basePlayer, skinId, heliProfile, 1))
                                {
                                    Economics?.CallHook("Withdraw", (ulong)basePlayer.userID, cost);
                                    Message(player, "Receive", 1, heliProfile);
                                    return;
                                }
                                else
                                {
                                    Message(player, "FullInventory", heliProfile);
                                    return;
                                }
                            }
                            else
                            {
                                Message(player, "CantAfford", $"{priceUnit}{cost}", $"{priceUnit}{cost - Convert.ToDouble(balance)}");
                                return;
                            }
                        }
                    }
                    break;
                default:
                    {
                        var shortName = _customCurrency.ShortName;
                        var displayName = _customCurrency.DisplayName;
                        var currencySkin = _customCurrency.SkinId;
                        var cost = isWaveHeli ? config.heli.waveConfig[heliProfile].CostToBuy : _heliConfig[heliProfile].Init.CostToBuy;
                        int balance = 0;

                        ItemDefinition itemDef = ItemManager.FindItemDefinition(shortName);
                        ItemContainer[] inventories = { basePlayer.inventory.containerMain, basePlayer.inventory.containerBelt };
                        for (int i = 0; i < inventories.Length; i++)
                        {
                            foreach (var item in inventories[i].itemList)
                            {
                                if (item.info.shortname == shortName && item.skin == currencySkin)
                                {
                                    balance += item.amount;
                                    if (cost <= balance)
                                    {
                                        if (GiveHeliSignal(basePlayer, skinId, heliProfile, 1))
                                        {
                                            basePlayer.inventory.Take(null, itemDef.itemid, cost);
                                            Message(player, "Receive", 1, heliProfile);
                                        }
                                        else
                                        {
                                            Message(player, "FullInventory", heliProfile);
                                        }

                                        if (item.amount < 1)
                                            item.Remove(0f);

                                        return;
                                    }
                                }
                            }
                        }
                        Message(player, "CantAfford", $"{cost} {displayName}", $"{cost - balance} {displayName}");
                    }
                    break;
            }
        }

        private void CmdGiveSignal(IPlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.Id, permAdmin))
            {
                Message(player, "NotAdmin");
                return;
            }
            else if (args?.Length < 2 || args?.Length > 3)
            {
                if (player.IsServer)
                {
                    Message(player, "SyntaxConsole");
                    return;
                }
                else
                {
                    Message(player, "SyntaxPlayer");
                    return;
                }
            }

            int dropAmount = 1;
            if (args?.Length == 3)
            {
                int amt;
                if (Int32.TryParse(args[2], out amt))
                {
                    dropAmount = amt;
                }
                else
                {
                    if (player.IsServer)
                    {
                        Message(player, "SyntaxConsole");
                        return;
                    }
                    else
                    {
                        Message(player, "SyntaxPlayer");
                        return;
                    }
                }
            }

            BasePlayer target = FindPlayer(args[1]);
            if (target == null)
            {
                Message(player, "PlayerNotFound", args[1]);
                return;
            }
            else if (!target.IsAlive())
            {
                Message(player, "PlayerDead", args[1]);
                return;
            }

            string type = args[0].ToLower();
            ulong skinId = 0;
            string heliProfile = string.Empty;
            foreach (var item in config.heli.heliConfig.Keys)
            {
                if (type == config.heli.heliConfig[item].Init.GiveItemCommand.ToLower())
                {
                    skinId = config.heli.heliConfig[item].Init.SignalSkinID;
                    heliProfile = item;
                    break;
                }
            }
            foreach (var item in config.heli.waveConfig.Keys)
            {
                if (type == config.heli.waveConfig[item].GiveItemCommand.ToLower())
                {
                    skinId = config.heli.waveConfig[item].SkinId;
                    heliProfile = item;
                    break;
                }
            }

            if (skinId == 0)
            {
                Message(player, "InvalidDrop", type);
                return;
            }

            if (GiveHeliSignal(target, skinId, heliProfile, dropAmount))
            {
                Message(target, "Receive", dropAmount, heliProfile);
                Message(player, "PlayerReceive", target.displayName, target.userID, dropAmount, heliProfile);
            }
            else
            {
                Message(player, "PlayerFull", heliProfile, target);
                return;
            }
        }

        private void CmdClearCooldown(IPlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.Id, permAdmin))
            {
                Message(player, "NotAdmin");
                return;
            }
            if (args?.Length > 1)
            {
                if (player.IsServer)
                    Message(player, "ClearSyntaxConsole");
                else
                    Message(player, "ClearSyntaxPlayer");

                return;
            }
            else if (args?.Length == 0)
            {
                PlayerCooldowns.Clear();
                TierCooldowns.Clear();
                Message(player, "CooldownsCleared");
                return;
            }
            BasePlayer target = FindPlayer(args[0]);
            if (target == null)
            {
                Message(player, "PlayerNotFound", args[0]);
                return;
            }

            ulong playerId = target.userID;
            if (PlayerCooldowns.ContainsKey(playerId))
            {
                PlayerCooldowns.Remove(playerId);
                Message(player, "PlayerCooldownCleared", target.displayName, playerId);
            }
            else if (TierCooldowns.ContainsKey(playerId))
            {
                TierCooldowns.Remove(playerId);
                Message(player, "PlayerCooldownCleared", target.displayName, playerId);
            }
            else
            {
                Message(player, "PlayerNoCooldown", target.displayName, playerId);
            }
        }

        #endregion Commands

        #region Monos

        public class HeliSignalComponent : MonoBehaviour
        {
            public SupplySignal signal;
            public BasePlayer player;
            public Vector3 position;
            public ulong skinId;
            public string heliProfile;
            public int heliAmount;
            public bool isWaveHeli;
            public string waveProfile;
            public ulong waveSkinId;
            public float waveTime;
            public List<string> waveProfileCache = new List<string>();
            public bool isMultiHeli;
            public float multiTime;
            public HeliData heliData;

            void Start()
            {
                heliData = config.heli.heliConfig[heliProfile];
                Invoke(nameof(CustomExplode), config.options.signalFuseLength);
            }

            void CustomExplode()
            {
                if (signal == null || player == null)
                    return;

                position = signal.transform.position;
                var playerId = player.userID;
                if (SignalAborted())
                {
                    signal.Kill();

                    ulong refunSkinId = isWaveHeli ? waveSkinId : skinId;
                    if (player != null || !player.IsAlive())
                    {
                        Instance.NextTick(() => Instance.GiveHeliSignal(player, refunSkinId, heliProfile, 1));

                        if (config.heli.playerCooldown > 0f)
                        {
                            if (PlayerCooldowns.ContainsKey(playerId))
                                PlayerCooldowns.Remove(playerId);

                            if (TierCooldowns.ContainsKey(playerId))
                                TierCooldowns.Remove(playerId);
                        }
                    }
                }
                else
                {
                    float finishUp = config.options.smokeDuration;
                    if (finishUp < 0) finishUp = 210f; // Rust default smoke duration is 210 seconds
                    signal.Invoke(new Action(signal.FinishUp), finishUp);
                    signal.SetFlagLocal(BaseEntity.Flags.On, true);
                    signal.SendNetworkUpdateImmediate();

                    HandleAnnouncements();
                    HandleDiscordNotification();
                    InitializeWaveTracking();

                    var arrivalPosition = position + new Vector3(0, config.heli.arrivalHeight, 0);

                    for (int i = 0; i < heliAmount; i++)
                    {
                        // Spawn then move the heli at a random location from the called position
                        // depending on the mapScaleDistance in config.
                        float size = TerrainMeta.Size.x * config.heli.mapScaleDistance;
                        Vector2 rand = Random.insideUnitCircle;
                        Vector3 pos = new Vector3(rand.x, 0, rand.y);
                        pos *= size;
                        pos += arrivalPosition + new Vector3(0f, config.heli.spawnHeight, 0f);

                        // Calculate direction to spawn heli facing calling player
                        Vector3 directionToPlayer = arrivalPosition - pos;
                        directionToPlayer.y = 0;
                        directionToPlayer = directionToPlayer.normalized;
                        Quaternion facePlayerRotation = Quaternion.LookRotation(directionToPlayer);

                        PatrolHelicopter heli = GameManager.server.CreateEntity(heliPrefab, pos, facePlayerRotation, true) as PatrolHelicopter;
                        if (heli == null) return;
                        heli.OwnerID = playerId;
                        heli.skinID = skinId;
                        heli._name = heliProfile;
                        heli.Spawn();

                        // DEBUG: Can we simply not generate the random position and spawn the heli
                        // directly at that location instead of spawning then teleporting it?
                        
                        // TP the heli to the location otherwise spawns map distance away
                        heli.transform.position = pos;

                        var heliId = heli.net.ID.Value;

                        if (isWaveHeli)
                            WavesCalled[waveTime].Add(heliId);
                        else if (isMultiHeli)
                            MultiHelisCalled[multiTime].Add(heliId);

                        int multiPosition = i + 1;
                        int waveInstanceIdx = 0;
                        
                        // Track that this wave profile is being spawned (for initial wave)
                        if (isWaveHeli && !string.IsNullOrEmpty(waveProfile))
                        {
                            // Initial spawn is always the first wave (index 0)
                            waveInstanceIdx = 0;

                            if (!SpawnedWaveProfiles.ContainsKey(waveProfile))
                                SpawnedWaveProfiles[waveProfile] = new HashSet<int>();

                            SpawnedWaveProfiles[waveProfile].Add(0); // First wave is always index 0
                        }
                        
                        Instance.NextTick(() =>
                        {
                            // Calling on NextTick to stop issues with AlphaLoot and other plugins
                            // which alter Heli settings on entity spawn
                            PatrolHelicopterAI heliAI = heli.myAI;
                            heliAI._targetList.Add(new PatrolHelicopterAI.targetinfo(player, player));

                            var heliComp = heli.gameObject.AddComponent<HeliComponent>();
                            heliComp.heli = heli;
                            heliComp.heliAI = heliAI;
                            heliComp.owner = player;
                            heliComp.skinId = skinId;
                            heliComp.heliProfile = heliProfile;
                            heliComp.calledPosition = position;
                            heliComp.isWaveHeli = isWaveHeli;
                            heliComp.waveProfileIndex = 0; // First wave is always index 0
                            heliComp.waveInstanceIndex = waveInstanceIdx; // Set for initial spawn

                            if (isWaveHeli)
                            {
                                heliComp.waveProfile = waveProfile;
                                heliComp.waveSkinId = waveSkinId;
                                heliComp.waveProfileCache = waveProfileCache;
                                heliComp.waveTime = waveTime;
                            }

                            heliAI.hasInterestZone = true;
                            heliAI.interestZoneOrigin = arrivalPosition;
                            heliAI.ExitCurrentState();
                            heliAI.State_Move_Enter(arrivalPosition);

                            HeliSignalData.Add(heliId, new HeliStats
                            {
                                OwnerID = playerId,
                                OwnerName = player.displayName,
                                ParentWaveProfile = isWaveHeli ? waveProfile : null,
                                WavePosition = isWaveHeli ? 1 : 0,  // First Heli is always position 1
                                WaveTotalCount = isWaveHeli ? config.heli.waveConfig[waveProfile].WaveProfiles.Count : 0,
                                WaveTime = waveTime,
                                IsMultiHeli = isMultiHeli,
                                MultiHeliTime = isMultiHeli ? multiTime : 0f,
                                MultiPosition = isMultiHeli ? multiPosition : 0,
                                MultiTotalCount = isMultiHeli ? heliAmount : 0,
                                MultiHeliProfile = isMultiHeli ? heliProfile : null,
                                WaveInstanceIndex = 0 // First wave
                            });

                            var despawnTime = config.heli.heliConfig[heliProfile].Init.DespawnTime;
                            
                            if (despawnTime > 0)
                            {
                                Instance.RetireHeli(heliAI, heliId, despawnTime);
                            }
                        });
                    }

                    if (isWaveHeli)
                    {
                        var message = Instance.PlayerLang(
                            "FirstHeliCalled",
                            player.UserIDString,
                            heliAmount,
                            waveProfileCache[0]
                        );
                        Instance.AnnounceToChat(player, message);
                    }
                }

                Destroy(this);
            }

            private void HandleAnnouncements()
            {
                if (!config.announce.callChat || Instance == null)
                    return;

                var gridPos = Instance.PositionToGrid(position) ?? "Unknown";
                var heliDisplayName = isWaveHeli ? waveProfile : heliData.Init.HeliName;
                var message = Instance.PlayerLang(
                    "HeliCalled",
                    player.UserIDString,
                    player.displayName,
                    heliDisplayName,
                    gridPos
                );
                Instance.AnnounceToChat(player, message);
            }

            private void HandleDiscordNotification()
            {
                if (!config.discord.sendHeliCall || Instance == null)
                    return;

                var discordData = BuildDiscordEmbedData();
                Instance.SendDiscordEmbed(discordData.Title, discordData.Description, discordData.Color, discordData.Footer, discordData.Fields);
            }

            private void InitializeWaveTracking()
            {
                if (isWaveHeli)
                {
                    waveTime = Time.realtimeSinceStartup;
                    WavesCalled.Add(waveTime, new List<ulong>());
                }
                else if (isMultiHeli)
                {
                    multiTime = Time.realtimeSinceStartup;
                    MultiHelisCalled.Add(multiTime, new List<ulong>());
                }
            }

            private DiscordEmbedData BuildDiscordEmbedData()
            {
                string steamUrl = steamProfileUrl + player.userID;
                string displayNameLink = $"[{player.displayName}]({steamUrl})";
                string steamIdLink = $"[{player.UserIDString}]({steamUrl})";

                var gridPos = Instance.PositionToGrid(position) ?? "Unknown";
                string title, embedHeliName, health, despawn, topRotor, tailRotor;

                var fields = new List<DiscordField>
                {
                    new DiscordField { Name = Instance.Lang("DiscordCalledLocation"), Value = gridPos, Inline = true},
                };

                if (isWaveHeli)
                {
                    // For waves, show the wave profile name and list all Helicopters in the wave
                    title = Instance.Lang("DiscordEmbedTitleCalled", waveProfile); // Wave profile name

                    // Build a list of all Helicopter names in the wave
                    List<string> heliNames = new List<string>();
                    foreach (var profile in waveProfileCache)
                    {
                        if (config.heli.heliConfig.ContainsKey(profile))
                        {
                            HeliData _heliData = config.heli.heliConfig[profile];
                            if (_heliData == null)
                                continue;
                            
                            health = _heliData.Init.Health.ToString();
                            topRotor = _heliData.Init.MainRotorHealth.ToString();
                            tailRotor = _heliData.Init.TailRotorHealth.ToString();
                            string hpInfo = Instance.Lang("DiscordWaveHealthInfo", health, topRotor, tailRotor);

                            heliNames.Add($"{_heliData.Init.HeliName} {hpInfo}");
                        }
                    }
                    embedHeliName = string.Join("\n", heliNames);

                    fields.Add(new DiscordField { Name = Instance.Lang("DiscordWaveTotal"), Value = waveProfileCache.Count.ToString(), Inline = true });
                    fields.Add(new DiscordField { Name = Instance.Lang("DiscordWaveHelicopters"), Value = embedHeliName, Inline = false });
                }
                else
                {
                    // For single Helicopter, use the Helicopter name
                    title = Instance.Lang("DiscordEmbedTitleCalled", heliData.Init.HeliName); // Single heli name
                    health = heliData.Init.Health.ToString();
                    topRotor = heliData.Init.MainRotorHealth.ToString();
                    tailRotor = heliData.Init.TailRotorHealth.ToString();
                    despawn = (heliData.Init.DespawnTime / 60f).ToString();

                    fields.Add(new DiscordField { Name = Instance.Lang("DiscordCalledHealth"), Value = health, Inline = true });
                    fields.Add(new DiscordField { Name = Instance.Lang("DiscordRotorHealthInfo"), Value = $"{topRotor}/{tailRotor}", Inline = true });
                    fields.Add(new DiscordField { Name = Instance.Lang("DiscordCalledDespawn"), Value = $"{despawn} mins", Inline = true });
                }

                var desc = Instance.Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                var footer = Instance.Lang("DiscordEmbedFooter") + $"  |  {zeodeFooterUrl}";
                string color = "#008000"; // Green embed

                return new DiscordEmbedData
                {
                    Title = title,
                    Description = desc,
                    Color = color,
                    Footer = footer,
                    Fields = fields
                };
            }

            private struct DiscordEmbedData
            {
                public string Title;
                public string Description;
                public string Color;
                public string Footer;
                public List<DiscordField> Fields;
            }

            // DEBUG: Check this is working correctly, especially ZoneManager logic.
            public bool SignalAborted()
            {
                if (player == null || !player.IsAlive())
                    return true;

                int globalHeliCount = 0;
                int playerHeliCount = 0;

                foreach (var netId in HeliSignalData.Keys)
                {
                    var heli = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as PatrolHelicopter;
                    if (heli == null)
                        continue;
                    
                    globalHeliCount++;

                    if (heli.OwnerID == player.userID)
                        playerHeliCount++;
                }

                if (config.heli.globalLimit > 0 && !Instance.permission.UserHasPermission(player.UserIDString, permAdmin))
                {
                    if ((globalHeliCount + heliAmount) > config.heli.globalLimit)
                    {
                        Instance.Message(player, "GlobalLimit", config.heli.globalLimit);
                        return true;
                    }
                }
                
                if (config.heli.playerLimit > 0 && !Instance.permission.UserHasPermission(player.UserIDString, permAdmin))
                {
                    if ((playerHeliCount + heliAmount) > config.heli.playerLimit)
                    {
                        Instance.Message(player, "PlayerLimit", config.heli.playerLimit);
                        return true;
                    }
                }

                return !IsValidHeliLocation();
            }

            private bool IsValidHeliLocation()
            {
                if (signal == null || Instance == null)
                    return false;

                var signalPos = signal.transform.position;

                if (heliData.ZoneManager.UseZoneManager && heliData.ZoneManager.UseHeliZones)
                {
                    bool isPlayerInZone = false;
                    bool isSignalInZone = false;
                    foreach (var id in heliData.ZoneManager.HeliZoneIDs)
                    {
                        isPlayerInZone = (bool)Instance.ZoneManager?.CallHook("IsPlayerInZone", id, player);
                        isSignalInZone = (bool)Instance.ZoneManager?.CallHook("IsEntityInZone", id, signal);

                        if (isPlayerInZone && isSignalInZone)
                            return true;
                        
                        continue;
                    }
                    Instance.Message(player, "NotInZone", heliProfile);
                    return false;
                }
                
                if (Instance.IsInSafeZone(signalPos))
                {
                    Instance.Message(player, "InSafeZone", heliProfile);
                    return false;
                }
                
                if (!IsValidMonumentLocation(signalPos))
                {
                    return false;
                }

                return true;
            }

            private bool IsValidMonumentLocation(Vector3 position)
            {
                if (TerrainMeta.Path == null || Monuments == null)
                    return true;
                    
                try
                {
                    var monument = TerrainMeta.Path.FindClosest<MonumentInfo>(Monuments, position);
                    if (monument == null)
                        return true;
 					
					bool allowMonuments = config.heli.allowMonuments;
                    float distFromMonuments = config.heli.distFromMonuments;
                    float dist = monument.Distance(position);
                    
                    if (!allowMonuments)
                    {
                        if (config.heli.excludedMonuments.Contains(monument.name))
                            return true;

                    	if (!config.heli.excludedMonuments.Contains(monument.name) && dist < config.heli.distFromMonuments)
                        {
                            Instance.Message(player, "InNamedMonument", heliProfile, monument.displayPhrase.translated);
                            return false;
                        }
                    }
                    else if (allowMonuments)
                    {
                    	if (config.heli.excludedMonuments.Contains(monument.name) && dist < distFromMonuments)
                    	{
                            Instance.Message(player, "InNamedMonument", heliProfile, monument.displayPhrase.translated);
                            return false;
                        }
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    Instance.Log($"Error checking monument location: {ex.Message}", LogLevel.ERROR);
                    return true;
                }
                
                return true;
            }
        }

        public class HeliComponent : MonoBehaviour
        {
            public string heliProfile;
            public BasePlayer owner;
            public ulong skinId;
            public PatrolHelicopterAI heliAI;
            public PatrolHelicopter heli;
            public Vector3 calledPosition;
            public Vector3 arrivalPosition;
            public bool isDying = false;
            public bool isReturning = false;
            public bool isRetiring = false;
            public bool movingToAttacker = false;
            public Vector3 strafePosition;
            public BasePlayer strafeTarget;
            public bool isStrafing = false;
            public bool isOrbitStrafing = false;
            public bool isStrafingTurret = false;
            public float timeSinceSeen = 0f;
            public bool isTeamDead = false;
            public ulong heliId;
            public bool isWaveHeli;
            public string waveProfile;
            public ulong waveSkinId;
            public float waveTime;
            public int waveProfileIndex; // Which position in the wave config this profile is (0, 1, 2...)
            public int waveInstanceIndex;
            public int currentWaveSize = 0; // Total helis in current wave
            public float currentWaveTime = 0f; // Time when current wave was spawned
            public List<string> waveProfileCache = new List<string>();
            public bool isMultiHeli;
            public float multiTime;
            public List<BasePlayer> callingTeam = new List<BasePlayer>();
            public bool retireCmdUsed = false;
            public BasePlayer retiringPlayer;
            public float turretCooldown = 0f;
            public FieldInfo useNapalm;
            public HeliData _heliProfile;
            public HeliData _nextHeliProfile;

            private float lastUpdateTargets;
            private float lastPositionCheck;
            private float lastReturnCheck;
            private float lastStrafeThink;
            private float lastUpdateHeliInfo;
            private float minHeight;
            private float maxHeight;
            private float orbitHeight;
            private float nextWaveTime;
            private float orbitStartTime;

            private float lastNetworkUpdate;

            void Start()
            {
                _heliProfile = config.heli.heliConfig[heliProfile];

                useNapalm = typeof(PatrolHelicopterAI).GetField("useNapalm", (BindingFlags.Instance | BindingFlags.NonPublic));

                minHeight = config.heli.minHeliHeight;
                maxHeight = config.heli.maxHeliHeight;
                orbitHeight = config.heli.maxOrbitHeight;

                var startTime = Time.realtimeSinceStartup;
                turretCooldown = startTime;
                lastUpdateTargets = startTime;
                lastPositionCheck = startTime;
                lastReturnCheck = startTime;
                lastStrafeThink = startTime;
                lastUpdateHeliInfo = startTime;
                lastNetworkUpdate = startTime;
                heliId = heli.net.ID.Value;
                arrivalPosition = calledPosition + new Vector3(0, config.heli.arrivalHeight, 0);
                isReturning = true;

                if (HeliSignalData.ContainsKey(heliId))
                {
                    var heliData = HeliSignalData[heliId];
                    isMultiHeli = heliData.IsMultiHeli;
                    multiTime = heliData.MultiHeliTime;
                }

                // If wave, track that this wave profile has been spawned for later
                if (isWaveHeli && !string.IsNullOrEmpty(waveProfile))
                {
                    if (!SpawnedWaveProfiles.ContainsKey(waveProfile))
                        SpawnedWaveProfiles[waveProfile] = new HashSet<int>();

                    SpawnedWaveProfiles[waveProfile].Add(waveProfileIndex);
                }

                GetCallingTeam();
                SetupHeli();
            }

            void Update()
            {
                if (heli == null)
                    return;

                if (heliAI._currentState == PatrolHelicopterAI.aiState.DEATH || heliAI.isDead)
                    isDying = true;

                if (heliAI.isRetiring && !isRetiring)
                {
                    isRetiring = true;
                    RemoveHeliData(1f);
                }

                StrafeThink();
                PositionCheck();
                ReturnHeliToPlayer();
                UpdateHeliInfo();
            }

            void UpdateHeliInfo()
            {
                if (Time.realtimeSinceStartup - lastUpdateHeliInfo > 1f)
                {
                    lastUpdateHeliInfo = Time.realtimeSinceStartup;

                    if (heli == null || heliAI == null)
                        return;

                    heli.OwnerID = owner.userID;
                    heli._name = heliProfile;
                    heli.skinID = skinId;

                    heli.UpdateNetworkGroup();

                    if (Time.realtimeSinceStartup - orbitStartTime > _heliProfile.Init.MaxOrbitDuration && heliAI._currentState == PatrolHelicopterAI.aiState.ORBIT)
                    {
                        // Prevent when heli gets stuck in semi hostile orbit state
                        orbitStartTime = Time.realtimeSinceStartup;
                        isReturning = true;
                        heliAI.ExitCurrentState();
                        heliAI.interestZoneOrigin = arrivalPosition;
                        heliAI.State_Move_Enter(arrivalPosition);
                    }

                    if (!heliAI.forceTerrainPushback)
                        return;

                    float terrainOrWaterLevel = WaterLevel.GetWaterOrTerrainSurface(heli.transform.position, false, false, null);
                    if (terrainOrWaterLevel == 0)
                    {
                        float targetMinHeight = (minHeight + terrainOrWaterLevel) * 0.5f;
                        float targetMaxHeight = (Random.Range(minHeight, maxHeight) + terrainOrWaterLevel) * 0.5f;
                        float targetOrbitHeight = (orbitHeight + terrainOrWaterLevel) * 0.5f;
                        if (minHeight > 0 && heliAI.destination.y < targetMinHeight)
                        {
                            heliAI.destination.y = targetMinHeight;
                        }
                        else if (orbitHeight > 0 && heliAI._currentState == PatrolHelicopterAI.aiState.ORBIT && heliAI.destination.y > targetOrbitHeight)
                        {
                            heliAI.destination.y = targetOrbitHeight;
                        }
                        else if (maxHeight > 0 && heliAI.destination.y > targetMaxHeight)
                        {
                            if (heliAI._currentState == PatrolHelicopterAI.aiState.PATROL || heliAI._currentState == PatrolHelicopterAI.aiState.FLEE)
                                return;

                            heliAI.destination.y = targetMaxHeight;
                        }
                        return;
                    }
                    else if (terrainOrWaterLevel != 0)
                    {
                        if (minHeight > 0 && heliAI.destination.y < (minHeight + terrainOrWaterLevel))
                        {
                            heliAI.destination.y = minHeight;
                        }
                        else if (orbitHeight > 0 && heliAI._currentState == PatrolHelicopterAI.aiState.ORBIT && heliAI.destination.y > (orbitHeight + terrainOrWaterLevel))
                        {
                            heliAI.destination.y = orbitHeight + terrainOrWaterLevel;
                        }
                        else if (maxHeight > 0 && heliAI.destination.y > (maxHeight + terrainOrWaterLevel))
                        {
                            if (heliAI._currentState == PatrolHelicopterAI.aiState.PATROL || heliAI._currentState == PatrolHelicopterAI.aiState.FLEE)
                                return;

                            heliAI.destination.y = Random.Range(minHeight, maxHeight) + terrainOrWaterLevel;
                        }
                    }
                }
            }

            void SetupHeli()
            {
                Instance.NextTick(() =>
                {
                    heli._maxHealth = _heliProfile.Init.Health;
                    heli.startHealth = heli._maxHealth;
                    heli.InitializeHealth(heli.startHealth, heli.startHealth);
                    heli.weakspots[0].maxHealth = _heliProfile.Init.MainRotorHealth;
                    heli.weakspots[1].maxHealth = _heliProfile.Init.TailRotorHealth;
                    heli.weakspots[0].health = _heliProfile.Init.MainRotorHealth;
                    heli.weakspots[1].health = _heliProfile.Init.TailRotorHealth;
                    heli.maxCratesToSpawn = 0; //_heliProfile.CratesToSpawn; 0 and use our own custom crate spawn logic
                    heli.bulletDamage = _heliProfile.Weapons.BulletDamage;
                    heli.bulletSpeed = _heliProfile.Weapons.BulletSpeed;

                    heliAI.maxSpeed = _heliProfile.Init.InitialSpeed;
                    heliAI.maxRotationSpeed = _heliProfile.Init.MaxRotationSpeed;
                    var dist = Vector3Ex.Distance2D(heliAI.transform.position, heliAI.destination);
                    heliAI.GetThrottleForDistance(dist);
                    heliAI.leftGun.fireRate = _heliProfile.Weapons.GunFireRate;
                    heliAI.rightGun.fireRate = heliAI.leftGun.fireRate;
                    heliAI.leftGun.burstLength = _heliProfile.Weapons.BurstLength;
                    heliAI.rightGun.burstLength = heliAI.leftGun.burstLength;
                    heliAI.leftGun.timeBetweenBursts = _heliProfile.Weapons.TimeBetweenBursts;
                    heliAI.rightGun.timeBetweenBursts = heliAI.leftGun.timeBetweenBursts;
                    heliAI.leftGun.maxTargetRange = _heliProfile.Weapons.MaxTargetRange;
                    heliAI.rightGun.maxTargetRange = heliAI.leftGun.maxTargetRange;
                    heliAI.leftGun.loseTargetAfter = 8f;
                    heliAI.rightGun.loseTargetAfter = 8f;
                    heliAI.timeBetweenRockets = _heliProfile.Weapons.TimeBetweenRockets;
                    heliAI.interestZoneOrigin = arrivalPosition;
                    heliAI.hasInterestZone = true;
                    heliAI.lastStrafeTime = Time.realtimeSinceStartup;
                    heliAI.terrainPushForce = _heliProfile.Init.TerrainPushForce;
                    heliAI.obstaclePushForce = _heliProfile.Init.ObstaclePushForce;
                    heliAI.forceTerrainPushback = true;

                    heli.UpdateNetworkGroup();
                    heli.SendNetworkUpdateImmediate();
                });
            }

            public void PositionCheck()
            {
                if (!isReturning && !movingToAttacker)
                    return;

                if (Time.realtimeSinceStartup - lastPositionCheck > 0.25f)
                {
                    lastPositionCheck = Time.realtimeSinceStartup;
                    if (heli != null || heliAI != null)
                    {
                        if (heliAI._currentState == PatrolHelicopterAI.aiState.DEATH || heliAI.isRetiring)
                            return;

                        float distance = Vector3Ex.Distance2D(heli.transform.position, heliAI.destination);
                        if (distance < (_heliProfile.Init.OrbitRadius + 20f))
                        {
                            isReturning = false;
                            movingToAttacker = false;

                            heliAI.maxSpeed = _heliProfile.Init.MaxSpeed;
                            heliAI.ExitCurrentState();
                            heliAI.SetIdealRotation(heliAI.GetYawRotationTo(heliAI.destination), -1f);
                            if (_heliProfile.Init.MaxOrbitDuration > 0)
                                heliAI.maxOrbitDuration = _heliProfile.Init.MaxOrbitDuration;

                            heliAI.State_Orbit_Enter(_heliProfile.Init.OrbitRadius);
                        }
                    }
                }
            }

            public void CustomUpdateTargetList()
            {
                if (isReturning || isRetiring || isDying)
                    return;

                if (heliAI.isRetiring)
                {
                    isRetiring = true;
                    return;
                }
                else if (heliAI.isDead)
                {
                    isDying = true;
                    return;
                }

                if (config.heli.retireOnKilled)
                {
                    foreach (var member in callingTeam)
                    {
                        if (member.IsAlive())
                        {
                            isTeamDead = false;
                            break;
                        }
                        isTeamDead = true;
                    }

                    if (isTeamDead)
                    {
                        isRetiring = true;
                        heliAI._targetList.Clear();
                        heliAI.Retire();
                    }
                }

                PatrolHelicopterAI.DangerZone dangerZone;
                BasePlayer player = null;
                float score = 0f;
                PatrolHelicopterAI.targetinfo _targetinfo = null;

                for (int i = heliAI._targetList.Count - 1; i >= 0; i--)
                {
                    PatrolHelicopterAI.targetinfo item = heliAI._targetList[i];
                    player = item?.ply;
                    if (item == null || !item.ent.IsValid() || player == null)
                    {
                        heliAI.RemoveTargetAt(i);
                    }
                    else if (config.heli.allowFlee && heliAI.IsInNoGoZone(player.transform.position))
                    {
                        heliAI.RemoveTargetAt(i);
                    }
                    else if (_heliProfile != null && _heliProfile.TargetingDamage.OwnerDamage && !callingTeam.Contains(player))
                    {
                        heliAI.RemoveTargetAt(i);
                    }
                    else if (!config.heli.targetUnderWater && player.WaterFactor() >= 1f)
                    {
                        heliAI.RemoveTargetAt(i);
                    }
                    else
                    {
                        heliAI.UpdateTargetLineOfSightTime(item);
                        bool isPlayerDead = (item.ply ? item.ply.IsDead() : item.ent.Health() <= 0f);
                        if (item.TimeSinceSeen() >= 6f | isPlayerDead)
                        {
                            if (heliAI.CanStrafe() && heliAI.IsAlive() && !isStrafing && !isPlayerDead)
                            {
                                if (player == heliAI.leftGun._target ? true : player == heliAI.rightGun._target)
                                {
                                    isStrafing = true;
                                    strafePosition = player.transform.position;
                                    strafeTarget = player;
                                }
                            }
                        }
                        heliAI.RemoveTargetAt(i);
                        if (heliAI.leftGun._target == item.ply)
                        {
                            heliAI.leftGun._target = null;
                        }
                        if (heliAI.rightGun._target == item.ply)
                        {
                            heliAI.rightGun._target = null;
                        }
                    }
                    if (config.heli.allowFlee && !isStrafing && heliAI.CanStrafe() && heliAI.IsAlive() &&
                       (Time.realtimeSinceStartup - heliAI.lastNapalmTime > 20f ||
                       Time.realtimeSinceStartup - heliAI.lastStrafeTime > 15f) &&
                       heliAI.IsInDangerZone(player.transform.position, out dangerZone) &&
                       dangerZone != null && dangerZone.Score > score)
                    {
                        score = dangerZone.Score;
                        _targetinfo = item;
                    }
                }
                if (config.heli.allowFlee && !isStrafing && _targetinfo != null)
                {
                    isStrafing = true;
                    strafeTarget = _targetinfo.ply;
                    _targetinfo = null;
                }
                AddNewTargetsToList();
                if (isStrafing && !heliAI.isRetiring && !heliAI.isDead)
                {
                    if (heliAI._currentState == PatrolHelicopterAI.aiState.STRAFE || heliAI._currentState == PatrolHelicopterAI.aiState.ORBITSTRAFE)
                        return;
                    else if (strafeTarget == null)
                        return;

                    heliAI.ExitCurrentState();
                    heliAI.State_Strafe_Enter(strafeTarget, UseNapalm());
                }
            }

            private void AddNewTargetsToList()
            {
                using (TimeWarning timeWarning = TimeWarning.New("PatrolHelicoperAI.AddNewTargetsToList", 0))
                {
                    using (PooledList<BasePlayer> pooledList = Pool.Get<PooledList<BasePlayer>>())
                    {
                        var targetRange = _heliProfile?.Weapons.NewTargetRange ?? config.heli.heliConfig[heliProfile].Weapons.NewTargetRange;
                        var blockPlayerDamage = _heliProfile?.TargetingDamage.BlockPlayerDamage ?? config.heli.heliConfig[heliProfile].TargetingDamage.BlockPlayerDamage;

                        BaseEntity.Query.Server.GetPlayersInSphere(heli.transform.position, targetRange, pooledList, BaseEntity.Query.DistanceCheckType.None, false);
                        foreach (BasePlayer basePlayer in pooledList)
                        {
                            if (basePlayer == null || basePlayer.InSafeZone() || SimpleAIMemory.PlayerIgnoreList.Contains(basePlayer))
                                continue;
                            else if (blockPlayerDamage && !callingTeam.Contains(basePlayer))
                                continue;

                            using (TimeWarning timeWarning1 = TimeWarning.New("PatrolHelicoperAI.SafeZone", 0))
                            {
                                if (basePlayer.InSafeZone())
                                {
                                    continue;
                                }
                            }
                            if (basePlayer.IsInTutorial && !callingTeam.Contains(basePlayer))
                            {
                                continue;
                            }
                            using (TimeWarning timeWarning2 = TimeWarning.New("PatrolHelicoperAI.NoGoZone", 0))
                            {
                                if (config.heli.allowFlee && heliAI.IsInNoGoZone(basePlayer.transform.position))
                                {
                                    continue;
                                }
                            }
                            if (heliAI.IsAlreadyInTargets(basePlayer) || basePlayer.GetThreatLevel() <= 0.5f || !heliAI.PlayerVisible(basePlayer))
                            {
                                continue;
                            }
                            heliAI.TryAddTarget(basePlayer);
                        }
                    }
                }
            }

            public void StrafeThink()
            {
                if (isDying || isRetiring)
                    return;

                if (Time.realtimeSinceStartup - lastStrafeThink > _heliProfile.Weapons.TimeBetweenRockets)
                {
                    lastStrafeThink = Time.realtimeSinceStartup;
                    switch (heliAI._currentState)
                    {
                        case PatrolHelicopterAI.aiState.STRAFE:
                            {
                                isStrafing = true;
                                if (strafeTarget != null)
                                {
                                    heliAI.strafe_target = strafeTarget;
                                    heliAI.strafe_target_position = strafeTarget.transform.position;
                                    heliAI.interestZoneOrigin = heliAI.strafe_target_position;
                                    heliAI.lastStrafeTime = Time.realtimeSinceStartup;
                                }

                                if (heliAI.ClipRocketsLeft() <= 1)
                                {
                                    if (Random.Range(0f, 1f) <= _heliProfile.Weapons.OrbitStrafeChance)
                                    {
                                        isOrbitStrafing = true;
                                        heliAI.ExitCurrentState();
                                        heliAI.State_OrbitStrafe_Enter();
                                        var rocketVariance = Random.Range(_heliProfile.Weapons.MinOrbitMultiplier, _heliProfile.Weapons.MaxOrbitMultiplier);
                                        heliAI.numRocketsLeft = _heliProfile.Weapons.MaxOrbitRockets + rocketVariance;
                                        UpdateSerializeableFields(heliAI.numRocketsLeft);
                                        return;
                                    }
                                    heliAI.ExitCurrentState();
                                    heliAI.State_Move_Enter(heliAI.GetAppropriatePosition(heliAI.strafe_target_position + (heli.transform.forward * 120f), config.heli.minHeliHeight, config.heli.maxHeliHeight));
                                }
                                return;
                            }
                        case PatrolHelicopterAI.aiState.ORBITSTRAFE:
                            {
                                isStrafing = true;
                                if (strafeTarget != null)
                                {
                                    heliAI.strafe_target = strafeTarget;
                                    heliAI.strafe_target_position = strafeTarget.transform.position;
                                    heliAI.interestZoneOrigin = heliAI.strafe_target_position;
                                    heliAI.lastStrafeTime = Time.realtimeSinceStartup;
                                }

                                if (!isOrbitStrafing)
                                {
                                    // Prevent or allow Orbit strafe initiated by vanilla game code according to config chance
                                    if (Oxide.Core.Random.Range(0f, 1f) <= _heliProfile.Weapons.OrbitStrafeChance)
                                    {
                                        isOrbitStrafing = true;
                                        var rocketVariance = Random.Range(_heliProfile.Weapons.MinOrbitMultiplier,
                                                                          _heliProfile.Weapons.MaxOrbitMultiplier);

                                        heliAI.numRocketsLeft = _heliProfile.Weapons.MaxOrbitRockets + rocketVariance;
                                        UpdateSerializeableFields(heliAI.numRocketsLeft);
                                        return;
                                    }
                                    heliAI.ExitCurrentState();
                                    heliAI.State_Move_Enter(heliAI.GetAppropriatePosition(heliAI.strafe_target_position + (heli.transform.forward * 120f), config.heli.minHeliHeight, config.heli.maxHeliHeight));
                                }
                                return;
                            }
                        default:
                            {
                                isStrafing = false;
                                isOrbitStrafing = false;
                                if (isStrafingTurret) turretCooldown = Time.realtimeSinceStartup;
                                isStrafingTurret = false;
                                return;
                            }
                    }
                }
            }

            private PatrolHelicopter CreateNextWaveHeli(string nextHeliProfile, int heliAmount, int waveProfileIndex)
            {
                PatrolHelicopter newHeli = GameManager.server.CreateEntity(heliPrefab, arrivalPosition, new Quaternion(), true) as PatrolHelicopter;
                if (newHeli == null) return null;

                newHeli.OwnerID = owner.userID;
                newHeli.skinID = config.heli.heliConfig[nextHeliProfile].Init.SignalSkinID;
                newHeli._name = nextHeliProfile;
                newHeli.Spawn();

                float size = TerrainMeta.Size.x * config.heli.mapScaleDistance;
                Vector2 rand = Random.insideUnitCircle.normalized;
                Vector3 pos = new Vector3(rand.x, 0, rand.y);
                pos *= size;
                pos += arrivalPosition + new Vector3(0f, config.heli.spawnHeight, 0f);
                newHeli.transform.position = pos;

                // Track that this specific wave index is being spawned
                if (!string.IsNullOrEmpty(waveProfile) && SpawnedWaveProfiles.ContainsKey(waveProfile))
                {
                    SpawnedWaveProfiles[waveProfile].Add(waveProfileIndex);
                }
                
                // Capture all necessary data
                var capturedWaveProfile = waveProfile;
                var capturedWaveProfileCache = new List<string>(waveProfileCache);
                var capturedNextWaveTime = nextWaveTime;
                var capturedOwner = owner;
                var capturedCalledPosition = calledPosition;
                var capturedWaveSkinId = waveSkinId;
                var capturedWaveProfileIndex = waveProfileIndex; // Capture the index

                Instance.NextTick(() =>
                {
                    if (newHeli == null || newHeli.IsDestroyed)
                        return;

                    PatrolHelicopterAI newHeliAI = newHeli.myAI;
                    if (newHeliAI == null)
                        return;

                    newHeliAI._targetList.Add(new PatrolHelicopterAI.targetinfo(capturedOwner, capturedOwner));

                    newHeliAI.hasInterestZone = true;
                    newHeliAI.interestZoneOrigin = arrivalPosition;
                    newHeliAI.ExitCurrentState();
                    newHeliAI.State_Move_Enter(arrivalPosition);

                    var heliComp = newHeli.gameObject.AddComponent<HeliComponent>();
                    if (heliComp != null)
                    {
                        heliComp.heli = newHeli;
                        heliComp.heliAI = newHeliAI;
                        heliComp.owner = capturedOwner;
                        heliComp.skinId = config.heli.heliConfig[nextHeliProfile].Init.SignalSkinID;
                        heliComp.heliProfile = nextHeliProfile;
                        heliComp.calledPosition = capturedCalledPosition;
                        heliComp.isWaveHeli = true;
                        heliComp.waveProfile = capturedWaveProfile;
                        heliComp.waveSkinId = capturedWaveSkinId;
                        heliComp.waveTime = capturedNextWaveTime;
                        heliComp.waveProfileCache = capturedWaveProfileCache;
                        heliComp.currentWaveSize = heliAmount;
                        heliComp.currentWaveTime = capturedNextWaveTime;
                        heliComp.waveProfileIndex = capturedWaveProfileIndex; // Set the profile index
                    }

                    AddHeliToWaveTracking(newHeli);
                    AddHeliSignalData(newHeli);
                    HandleHeliWaveRetire(newHeli, nextHeliProfile);
                });

                return newHeli;
            }

            private void HandleHeliWaveRetire(PatrolHelicopter newHeli, string nextHeliProfile)
            {
                if (Instance == null)
                    return;

                if (config.heli.heliConfig[nextHeliProfile].Init.DespawnTime > 0)
                    Instance.RetireHeli(newHeli.myAI, heliId, config.heli.heliConfig[nextHeliProfile].Init.DespawnTime);
            }

            private void AddHeliSignalData(PatrolHelicopter newHeli)
            {
                var heliComp = newHeli.GetComponent<HeliComponent>();
                if (heliComp == null) return;

                var stats = new HeliStats
                {
                    OwnerID = owner.userID,
                    OwnerName = owner.displayName,
                    ParentWaveProfile = isWaveHeli ? waveProfile : null,
                    WavePosition = 0,
                    WaveTotalCount = 0,
                    WaveTime = isWaveHeli ? heliComp.waveTime : 0f,
                    WaveInstanceIndex = 0
                };

                if (isWaveHeli && heliComp != null)
                {
                    stats.WaveInstanceIndex = heliComp.waveProfileIndex; // Use the profile index from component
                    stats.WavePosition = heliComp.waveProfileIndex + 1;
                    stats.WaveTotalCount = config.heli.waveConfig[heliComp.waveProfile]?.WaveProfiles.Count ?? 0;
                    stats.WaveTime = heliComp.waveTime;
                }

                HeliSignalData.Add(newHeli.net.ID.Value, stats);
            }

            private void AddHeliToWaveTracking(PatrolHelicopter newHeli)
            {
                var newHeliId = newHeli.net.ID.Value;
                if (isWaveHeli)
                {
                    if (WavesCalled.ContainsKey(nextWaveTime))
                    {
                        if (!WavesCalled[nextWaveTime].Contains(newHeliId))
                            WavesCalled[nextWaveTime].Add(newHeliId);
                    }
                    else
                    {
                        Instance.Log(
                            $"Wave tracking error: nextWaveTime {nextWaveTime} not " +
                            "found in WavesCalled dictionary", 
                            LogLevel.ERROR
                        );
                    }
                }
            }

            public void CallNextWaveHeli(string nextHeliProfile)
            {
                if (Instance == null)
                    return;

                if (!config.heli.heliConfig.TryGetValue(nextHeliProfile, out _nextHeliProfile))
                {
                    Instance.Log($"ERROR: No such profile: {nextHeliProfile}, check config.", LogLevel.ERROR);
                    return;
                }

                nextWaveTime = Time.realtimeSinceStartup;
                WavesCalled.Add(nextWaveTime, new List<ulong>());

                int heliAmount = config.heli.heliConfig[nextHeliProfile].Init.HeliAmount;

                // Calculate which wave profile index we're spawning
                int currentWaveProfileIndex = 0;
                if (!string.IsNullOrEmpty(waveProfile) && config.heli.waveConfig.ContainsKey(waveProfile))
                {
                    var allProfiles = config.heli.waveConfig[waveProfile].WaveProfiles;
                    currentWaveProfileIndex = allProfiles.Count - waveProfileCache.Count;
                }

                for (int i = 0; i < heliAmount; i++)
                {
                    CreateNextWaveHeli(nextHeliProfile, heliAmount, currentWaveProfileIndex);
                }

                HandleWaveAnnouncement(false);
            }

            private void HandleWaveAnnouncement(bool hasCooldown)
            {
                if (Instance == null)
                    return;

                if (!isWaveHeli)
                    return;

                Instance.NextTick(() =>
                {
                    if (hasCooldown)
                    {
                        var waveCooldown = Math.Max(0, Math.Round(config.heli.waveConfig[waveProfile].WaveCooldown / 60f, 2));
                        var message = Instance.Lang("NextHeliDelayed", new object[] { heliProfile, waveProfileCache[0], waveCooldown });
                        Instance.AnnounceToChat(owner, message);

                    }
                    else
                    {
                        var message = Instance.Lang("NextHeliInbound", waveProfileCache[0]);
                        Instance.AnnounceToChat(owner, message);
                    }
                });
            }

            public void ReturnHeliToPlayer()
            {
                if (!config.heli.returnToPlayer)
                    return;

                if (isRetiring || isDying || isStrafing || isOrbitStrafing || isStrafingTurret)
                    return;

                if (Time.realtimeSinceStartup - lastReturnCheck > 1.0f)
                {
                    lastReturnCheck = Time.realtimeSinceStartup;
                    BasePlayer target = owner;
                    Vector3 returnPosition = new Vector3();
                    if (target == null)
                    {
                        returnPosition = calledPosition;
                    }
                    else if (!target.IsConnected || target.IsDead() || target.IsSleeping() || config.heli.returnToPosition)
                    {
                        returnPosition = calledPosition;
                    }
                    else
                    {
                        heliAI._targetList.Add(new PatrolHelicopterAI.targetinfo(target, target));
                        returnPosition = target.transform.position;
                    }

                    if (Vector3Ex.Distance2D(heliAI.transform.position, returnPosition) > config.heli.maxDistanceFromPlayer)
                    {
                        if (!config.heli.returnIfAttacking)
                        {
                            switch (heliAI._currentState)
                            {
                                case PatrolHelicopterAI.aiState.ORBIT:
                                    return;
                                case PatrolHelicopterAI.aiState.STRAFE:
                                    return;
                                case PatrolHelicopterAI.aiState.DEATH:
                                    isDying = true;
                                    return;
                                default:
                                    break;
                            }
                        }
                        isReturning = true;
                        heliAI.ExitCurrentState();
                        heliAI.interestZoneOrigin = returnPosition;
                        heliAI.State_Move_Enter(returnPosition + new Vector3(0, config.heli.arrivalHeight, 0));
                    }
                }
            }

            public void MaybeMoveToAttacker(BasePlayer target)
            {
                if (isRetiring || isDying || isReturning || movingToAttacker)
                    return;

                if (target == null || !target.IsConnected || target.IsDead() || target.IsSleeping())
                    return;

                Vector3 attackerPosition = target.transform.position;

                switch (heliAI._currentState)
                {
                    case PatrolHelicopterAI.aiState.IDLE:
                        break;
                    case PatrolHelicopterAI.aiState.PATROL:
                        break;
                    case PatrolHelicopterAI.aiState.MOVE:
                        break;
                    case PatrolHelicopterAI.aiState.DEATH:
                        isDying = true;
                        return;
                    default:
                        return;
                }

                movingToAttacker = true;
                heliAI.ExitCurrentState();
                heliAI.interestZoneOrigin = attackerPosition;
                heliAI.State_Move_Enter(attackerPosition + new Vector3(0, config.heli.arrivalHeight, 0));
            }

            public void UpdateSerializeableFields(int rockets)
            {
                heliAI.numRocketsLeft = rockets;
                bool doNapalm = UseNapalm();

                #if CARBON
                heliAI.useNapalm = doNapalm;

                #else
                if (useNapalm != null)
                    useNapalm.SetValue(heliAI, (object)doNapalm);

                #endif
            }

            public void CustomFireRocket()
            {
                Vector3 targetPos = new Vector3();

                switch (heliAI._currentState)
                {
                    case PatrolHelicopterAI.aiState.STRAFE:
                        targetPos = heliAI.strafe_target_position;
                        break;
                    case PatrolHelicopterAI.aiState.ORBITSTRAFE:
                        targetPos = heliAI.interestZoneOrigin;
                        break;
                    default:
                        return;
                }

                string str;
                heliAI.numRocketsLeft--;
                heliAI.lastRocketTime = Time.realtimeSinceStartup;
                float single = Random.Range(3.9f, 4.1f);
                bool flag = heliAI.leftTubeFiredLast;
                heliAI.leftTubeFiredLast = !heliAI.leftTubeFiredLast;
                Transform transforms = (flag ? heliAI.helicopterBase.rocket_tube_left.transform : heliAI.helicopterBase.rocket_tube_right.transform);
                Vector3 vector3 = transforms.position + (transforms.forward * 1f);
                Vector3 modifiedAimConeDirection = (targetPos - vector3).normalized;
                if (single > 0f)
                {
                    modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(single, modifiedAimConeDirection, true);
                }
                Effect.server.Run(heliAI.helicopterBase.rocket_fire_effect.resourcePath, heliAI.helicopterBase, StringPool.Get((flag ? "rocket_tube_left" : "rocket_tube_right")), Vector3.zero, Vector3.forward, null, true, null);
                GameManager gameManager = GameManager.server;
                str = (heliAI.useNapalm ? heliAI.rocketProjectile_Napalm.resourcePath : heliAI.rocketProjectile.resourcePath);
                Quaternion quaternion = new Quaternion();
                BaseEntity baseEntity = gameManager.CreateEntity(str, vector3, quaternion, true);
                if (baseEntity == null)
                {
                    return;
                }
                ServerProjectile component = baseEntity.GetComponent<ServerProjectile>();
                if (component)
                {
                    component.InitializeVelocity(modifiedAimConeDirection * component.speed);
                }
                baseEntity.creatorEntity = heli;
                baseEntity.OwnerID = owner.userID;
                baseEntity.skinID = skinId;
                baseEntity.Spawn();

                SetDamageScale((baseEntity as TimedExplosive), _heliProfile.Weapons.RocketDamageScale);
            }

            private void SetDamageScale(TimedExplosive rocket, float scale)
            {
                foreach (DamageTypeEntry damageType in rocket.damageTypes)
                    damageType.amount *= scale;
            }

            public bool UseNapalm()
            {
                return (Random.Range(0f, 1f) <= _heliProfile.Weapons.NapalmChance);
            }

            public object GetCallingTeam()
            {
                if (!callingTeam.Contains(owner))
                    callingTeam.Add(owner);

                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (Instance.IsOwnerOrFriend(player.userID, owner.userID) && !callingTeam.Contains(player))
                        callingTeam.Add(player);
                }

                return callingTeam;
            }

            private void HandleWaveCompletion()
            {
                if (Instance == null)
                    return;

                var message = Instance.PlayerLang(
                    "WaveFinished",
                    owner.UserIDString,
                    heliProfile
                );
                Instance.AnnounceToChat(owner, message);

                var capturedOwner = owner;
                var capturedWaveProfile = waveProfile;
                
                Instance.timer.Once(1f, () =>
                {
                    SendWaveCompletionDiscord(capturedOwner, capturedWaveProfile);
                });
                
                // Clear spawn tracking for this wave
                if (!string.IsNullOrEmpty(waveProfile) && SpawnedWaveProfiles.ContainsKey(waveProfile))
                {
                    SpawnedWaveProfiles.Remove(waveProfile);
                }

                RemoveHeliData();
            }

            private void SendWaveCompletionDiscord(BasePlayer capturedOwner, string capturedWaveProfile)
            {
                if (Instance == null || !config.discord.sendHeliKill)
                    return;

                try
                {
                    string steamUrl = steamProfileUrl + capturedOwner.userID;
                    string displayNameLink = $"[{capturedOwner.displayName}]({steamUrl})";
                    string steamIdLink = $"[{capturedOwner.UserIDString}]({steamUrl})";

                    List<string> heliNames = new List<string>();
                    foreach (var profile in config.heli.waveConfig[capturedWaveProfile].WaveProfiles)
                    {
                        if (config.heli.heliConfig.ContainsKey(profile))
                        {
                            heliNames.Add($"💥 {config.heli.heliConfig[profile].Init.HeliName}");
                        }
                    }
                    string waveComposition = string.Join("\n", heliNames);

                    var title = Instance.Lang("DiscordEmbedTitleWaveComplete", capturedWaveProfile);
                    var desc = Instance.Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                    var footer = Instance.Lang("DiscordEmbedFooter") + $"  |  {zeodeFooterUrl}";
                    string color = "#FFD700"; // Gold color

                    var field = new List<DiscordField>
                    {
                        new DiscordField { Name = Instance.Lang("DiscordWaveCompleteStatus"), Value = Instance.Lang("DiscordWaveCompleteAllDestroyed"), Inline = true},
                        new DiscordField { Name = Instance.Lang("DiscordWaveCompleteTotal"), Value = config.heli.waveConfig[capturedWaveProfile].WaveProfiles.Count.ToString(), Inline = true},
                        new DiscordField { Name = Instance.Lang("DiscordWaveCompleteHelicopters"), Value = waveComposition, Inline = false}
                    };

                    Instance.SendDiscordEmbed(title, desc, color, footer, field);
                }
                catch (Exception ex)
                {
                    // Log error but continue
                    if (Instance != null)
                        Instance.Log($"Error sending Discord notification: {ex.Message}", LogLevel.ERROR);
                }
            }

            public void RemoveHeliData(float time = 10f)
            {
            	if (Instance == null)
                	return;
                
                Instance.timer.Once(time, () =>
                {
                    if (HeliSignalData == null)
                        return;

                    if (HeliSignalData.ContainsKey(heliId))
                        HeliSignalData.Remove(heliId);
                });
            }

            private void OnDestroy()
            {
                CancelInvoke();

                // Safely remove from cache
                if (heliAI != null && heliCompCache != null && heliCompCache.ContainsKey(heliAI))
                {
                    try
                    {
                        heliCompCache.Remove(heliAI);
                    }
                    catch { }
                }

                if (Instance == null)
                {
                    // Immediate cleanup of data
                    if (HeliSignalData != null && HeliSignalData.ContainsKey(heliId))
                    {
                        try
                        {
                            HeliSignalData.Remove(heliId);
                        }
                        catch { }
                    }
                    return;
                }

                if (!isRetiring)
                    ProcessPostDestroy();

                if (!PatrolHelicopterAI.monument_crash)
                {
                    Instance.timer.Once(11f, () =>
                    {
                        if (heli != null)
                            heli.Hurt(heli.health * 2f, DamageType.Generic, null, false);
                    });
                }
            }

            private void ProcessPostDestroy()
            {
                if (Instance == null)
                    return;

                if (HeliSignalData != null)
                {
                    Instance.ProcessRewards(heliId, owner.userID, heliProfile);
                }

                if (isWaveHeli && waveProfileCache != null && waveProfileCache.Count > 0)
                {
                    // Use the correct waveTime for this helicopter
                    float currentWaveTime = this.waveTime;

                    if (!destroyedHelisPerWave.ContainsKey(currentWaveTime))
                        destroyedHelisPerWave[currentWaveTime] = new List<ulong>();

                    if (!destroyedHelisPerWave[currentWaveTime].Contains(heliId))
                        destroyedHelisPerWave[currentWaveTime].Add(heliId);

                    if (!WavesCalled.ContainsKey(currentWaveTime))
                        return;

                    var waveHeliIds = WavesCalled[currentWaveTime];
                    var destroyedIds = destroyedHelisPerWave[currentWaveTime];

                    // Get the current wave profile info
                    var currentWaveProfile = heliProfile; // This is the profile of the destroyed heli
                    var currentProfileHeliCount = config.heli.heliConfig[currentWaveProfile].Init.HeliAmount;

                    // Check if ALL helis from this specific wave instance are destroyed
                    int aliveFromThisWaveInstance = 0;

                    foreach (var waveHeliId in waveHeliIds)
                    {
                        if (!destroyedIds.Contains(waveHeliId))
                        {
                            // This heli from this wave instance is still alive
                            var aliveHeli = BaseNetworkable.serverEntities.Find(new NetworkableId(waveHeliId)) as PatrolHelicopter;
                            if (aliveHeli != null)
                            {
                                aliveFromThisWaveInstance++;
                            }
                        }
                    }

                    // Only proceed to next wave if ALL helis from this wave instance are destroyed
                    if (aliveFromThisWaveInstance == 0 && waveHeliIds.Count == currentProfileHeliCount)
                    {
                        // Current wave is fully destroyed
                        waveProfileCache.RemoveAt(0);

                        if (waveProfileCache.Count <= 0)
                        {
                            HandleWaveCompletion();
                        }
                        else
                        {
                            HandleWaveAnnouncement(true);

                            if (config.heli.waveConfig != null && !string.IsNullOrEmpty(waveProfile) && config.heli.waveConfig.ContainsKey(waveProfile))
                            {
                                float nextWave = config.heli.waveConfig[waveProfile].WaveCooldown;
                                Instance.timer.Once(nextWave, () =>
                                {
                                    if (waveProfileCache != null && waveProfileCache.Count > 0)
                                        CallNextWaveHeli(waveProfileCache[0]);
                                });
                            }
                        }

                        // Cleanup the tracking data for this wave
                        destroyedHelisPerWave.Remove(currentWaveTime);
                        WavesCalled.Remove(currentWaveTime);
                    }

                    // Clean up HeliSignalData after a delay to ensure status tracking works
                    Instance.timer.Once(5f, () =>
                    {
                        if (HeliSignalData.ContainsKey(heliId))
                            HeliSignalData.Remove(heliId);
                    });
                }
                else if (isMultiHeli && !string.IsNullOrEmpty(heliProfile))
                {
                    // Track destroyed multi helis
                    if (!destroyedMultiHelisPerEvent.ContainsKey(multiTime))
                        destroyedMultiHelisPerEvent[multiTime] = new List<ulong>();

                    if (!destroyedMultiHelisPerEvent[multiTime].Contains(heliId))
                        destroyedMultiHelisPerEvent[multiTime].Add(heliId);

                    if (MultiHelisCalled.ContainsKey(multiTime))
                    {
                        var multiHeliIds = MultiHelisCalled[multiTime];
                        var destroyedIds = destroyedMultiHelisPerEvent[multiTime];

                        // Check if all helis in this multi event are destroyed
                        if (destroyedIds.Count >= multiHeliIds.Count)
                        {
                            // All helis destroyed, clean up
                            destroyedMultiHelisPerEvent.Remove(multiTime);
                            MultiHelisCalled.Remove(multiTime);
                        }
                    }

                    // Clean up HeliSignalData after a delay
                    Instance.timer.Once(5f, () =>
                    {
                        if (HeliSignalData.ContainsKey(heliId))
                            HeliSignalData.Remove(heliId);
                    });
                }
                else if (!isWaveHeli && !isMultiHeli)
                {
                    RemoveHeliData();
                }
            }
        }

        private class CrateComp : MonoBehaviour
        {
            public LockedByEntCrate crate;
            private float lastUpdateTime;
            private float destroyTime = 30f;

            void Start()
            {
                lastUpdateTime = Time.realtimeSinceStartup;

                if (crate.GetParentEntity() is TrainCarUnloadable)
                {
                    crate.inventory.SetLocked(false);
                }
                Invoke(nameof(DestroyThis), destroyTime);
            }

            void Update()
            {
                if (Time.realtimeSinceStartup - lastUpdateTime > 1f)
                {
                    lastUpdateTime = Time.realtimeSinceStartup;

                    if (crate == null)
                        return;

                    if (crate.GetParentEntity() is TrainCarUnloadable)
                    {
                        crate.inventory.SetLocked(false);
                    }
                }
            }

            void DestroyThis()
            {
                // Give time for crates to land and stop moving before killing this component
                Destroy(this);
            }
        }

        #endregion Monos

        #region Temporary Data

        private static Dictionary<ulong, HeliStats> HeliSignalData = new Dictionary<ulong, HeliStats>();
        private static Dictionary<ulong, float> PlayerCooldowns = new Dictionary<ulong, float>();
        private static Dictionary<ulong, Dictionary<string, float>> TierCooldowns = new Dictionary<ulong, Dictionary<string, float>>();
        private static readonly Dictionary<ulong, LockedCrateData> LockedCrates = new Dictionary<ulong, LockedCrateData>();
        private static List<string> HeliProfiles = new List<string>();
        private static List<string> WaveProfiles = new List<string>();
        private static Dictionary<float, List<ulong>> WavesCalled = new Dictionary<float, List<ulong>>();
        private static List<MonumentInfo> Monuments = new List<MonumentInfo>();
        public static Dictionary<ulong, string> HeliProfileCache = new Dictionary<ulong, string>();
        private static Dictionary<float, List<ulong>> MultiHelisCalled = new Dictionary<float, List<ulong>>();
        private static Dictionary<float, List<ulong>> destroyedMultiHelisPerEvent = new Dictionary<float, List<ulong>>();
        private static Dictionary<float, List<PatrolHelicopter>> AutoRetiringMultiHelis = new Dictionary<float, List<PatrolHelicopter>>();
        private static Dictionary<string, HashSet<int>> SpawnedWaveProfiles = new Dictionary<string, HashSet<int>>();
        private static Dictionary<float, List<ulong>> destroyedHelisPerWave = new Dictionary<float, List<ulong>>();
        private static Dictionary<float, List<PatrolHelicopter>> AutoRetiringWaveHelis = new Dictionary<float, List<PatrolHelicopter>>();
        private static HashSet<float> ProcessedRetirements = new HashSet<float>();

        private class LockedCrateData
        {
            public ulong PlayerId;
            public float DecayTime;
        }

        private class HeliStats
        {
            public ulong OwnerID;
            public string OwnerName;
            public float FirstHitTime = 0f;
            public BasePlayer LastAttacker;
            public Dictionary<ulong, AttackersStats> Attackers = new Dictionary<ulong, AttackersStats>();
            public int WarningLevel = 0;
            public float PlayerDamage = 0f;
            public float TurretDamage = 0f;
            public AutoTurret LastTurretAttacker;
            // Wave tracking
            public string ParentWaveProfile;
            public int WavePosition;
            public int WaveTotalCount;
            public float WaveTime;
            // Multi tracking info
            public bool IsMultiHeli;
            public float MultiHeliTime;
            public int MultiPosition;
            public int MultiTotalCount;
            public string MultiHeliProfile;
            public int WaveInstanceIndex; // 0 for first wave, 1 for second, etc.
        }

        private class AttackersStats
        {
            public string Name;
            public float DamageDealt = 0f;
            public float TurretDamage = 0f;
            public int TotalHits = 0;
            public int RotorHits = 0;
        }

        #endregion Temporary Data

        #region Config Options

        private static ConfigData config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "General Options")]
            public Options options { get; set; }
            [JsonProperty(PropertyName = "Announce Options")]
            public Announce announce { get; set; }
            [JsonProperty(PropertyName = "Discord Options")]
            public Discord discord { get; set; }
            [JsonProperty(PropertyName = "Reward Options")]
            public Rewards rewards { get; set; }
            [JsonProperty(PropertyName = "Purchasing Options")]
            public Purchasing purchasing { get; set; }
            [JsonProperty(PropertyName = "Patrol Helicopter Options")]
            public Heli heli { get; set; }

            public class Options
            {
                [JsonProperty(PropertyName = "Use Friends")]
                public bool useFriends { get; set; }
                [JsonProperty(PropertyName = "Use Clans")]
                public bool useClans { get; set; }
                [JsonProperty(PropertyName = "Use Teams")]
                public bool useTeams { get; set; }
                [JsonProperty(PropertyName = "Allow Dynamic PVP to Create PVP Zones")]
                public bool useDynamicPVP { get; set; }
                [JsonProperty(PropertyName = "Allow Better NPC Bots to Guard Heli Crates (If Loaded)")]
                public bool useBetterNPC { get; set; }
                [JsonProperty(PropertyName = "Chat Prefix")]
                public string chatPrefix { get; set; }
                [JsonProperty(PropertyName = "Use Chat Prefix")]
                public bool usePrefix { get; set; }
                [JsonProperty(PropertyName = "Custom Chat Icon (Default = 0)")]
                public ulong chatIcon { get; set; }
                [JsonProperty(PropertyName = "Supply Signal Fuse Length (Rust Default = 3.5)")]
                public float signalFuseLength { get; set; }
                [JsonProperty(PropertyName = "Supply Signal Smoke Duration (Rust Default = 210)")]
                public float smokeDuration { get; set; }
                [JsonProperty(PropertyName = "Disable vanilla Patrol Helicopter")]
                public bool noVanillaHeli { get; set; }
                [JsonProperty(PropertyName = "Use This Plugin to Control Stacking/Combining Heli Signal Items")]
                public bool useStacking { get; set; }
                [JsonProperty(PropertyName = "Command to Show Details of Players Own Active Helis (Admin Perm Allows to See ALL Active Helis)")]
                public string reportCommand { get; set; }
                [JsonProperty(PropertyName = "Enable Debug Logging")]
                public bool enableDebug { get; set; }
            }

            public class Announce
            {
                [JsonProperty(PropertyName = "Announce When Player Calls a Patrol Helicopter in Chat")]
                public bool callChat { get; set; }
                [JsonProperty(PropertyName = "Announce Helicopter Kill in Chat")]
                public bool killChat { get; set; }
                [JsonProperty(PropertyName = "Announce When a Helicopter Retires in Chat")]
                public bool retireChat { get; set; }
                [JsonProperty(PropertyName = "Announce Damage Report in Chat")]
                public bool reportChat { get; set; }
                [JsonProperty(PropertyName = "Also Give Damage Report When Helicopter Retires")]
                public bool reportRetire { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Patrol Helicopter Kill in Chat")]
                public bool killVanilla { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Patrol Helicopter Damage Report in Chat")]
                public bool reportVanilla { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Patrol Helicopter Display Name")]
                public string vanillaName { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Patrol Helicopter Owner Name")]
                public string vanillaOwner { get; set; }
                [JsonProperty(PropertyName = "Max Number Players Displayed in Damage Report")]
                public int maxReported { get; set; }
                [JsonProperty(PropertyName = "Announcements Also go to Global Chat (false = Player/Team Only)")]
                public bool announceGlobal { get; set; }
            }

            public class Discord
            {
                [JsonProperty(PropertyName = "Discord WebHook URL")]
                public string webhookUrl { get; set; }
                [JsonProperty(PropertyName = "Custom Discord Bot Name")]
                public string defaultBotName { get; set; }
                [JsonProperty(PropertyName = "Custom Discord Bot Avatar URL")]
                public string customAvatarUrl { get; set; }
                [JsonProperty(PropertyName = "Use Custom Discord Bot Avatar (false = Use plugin default)")]
                public bool useCustomAvatar { get; set; }
                [JsonProperty(PropertyName = "Announce to Discord when Helicopter is called")]
                public bool sendHeliCall { get; set; }
                [JsonProperty(PropertyName = "Announce to Discord when Helicopter is killed")]
                public bool sendHeliKill { get; set; }
                [JsonProperty(PropertyName = "Announce to Discord when Helicopter retires")]
                public bool sendHeliRetire { get; set; }
            }

            public class Rewards
            {
                [JsonProperty(PropertyName = "Rewards Plugin (ServerRewards | Economics)")]
                public string rewardPlugin { get; set; }
                [JsonProperty(PropertyName = "Currency Unit Displayed e.g: RP | $")]
                public string rewardUnit { get; set; }
                [JsonProperty(PropertyName = "Enable Rewards")]
                public bool enableRewards { get; set; }
                [JsonProperty(PropertyName = "Share Rewards Between Players Above Damage Threshold")]
                public bool shareRewards { get; set; }

                // DEBUG: Update old config field name. Remove this later.
                [JsonProperty("Plugin to Use For Awarding XP (SkillTree | XPerience)")]
                public string GetpluginXP { get { return pluginXP; } set { pluginXP = value; } }
                public bool ShouldSerializeGetpluginXP() => false;
                // #######################

                [JsonProperty(PropertyName = "Plugin to Use For Awarding XP (SkillTree | XPerience | Skills)")]
                public string pluginXP { get; set; }
                [JsonProperty(PropertyName = "Enable XP Reward")]
                public bool enableXP { get; set; }
                [JsonProperty(PropertyName = "Share XP Between Players Above Damage Threshold")]
                public bool shareXP { get; set; }

                // DEBUG: Update old config field name. Remove this later.
                [JsonProperty("Award XP Including Players Existing Boosts")]
                public bool GetboostXP { get { return boostXP; } set { boostXP = value; } }
                public bool ShouldSerializeGetboostXP() => false;
                // #######################

                [JsonProperty(PropertyName = "Award XP Including Players Existing Boosts (Where Available)")]
                public bool boostXP { get; set; }
                [JsonProperty(PropertyName = "Enable Scrap Reward")]
                public bool enableScrap { get; set; }
                [JsonProperty(PropertyName = "Share Scrap Between Players Above Damage Threshold")]
                public bool shareScrap { get; set; }
                [JsonProperty(PropertyName = "Enable Custom Reward Currency")]
                public bool enableCustomReward { get; set; }
                [JsonProperty(PropertyName = "Share Custom Reward Between Players Above Damage Threshold")]
                public bool shareCustomReward { get; set; }
                [JsonProperty(PropertyName = "Custom Reward Currency Item")]
                public CustomRewardItem customRewardItem { get; set; }
                [JsonProperty(PropertyName = "Rewards multipliers by permission")]
                public Hash<string, float> rewardMultipliers { get; set; }

                [JsonIgnore]
                public Permission permission;
                public void RegisterPermissions(Permission permission, HeliSignals plugin)
                {
                    this.permission = permission;
                    foreach (string key in rewardMultipliers.Keys)
                    {
                        if (!permission.PermissionExists(key, plugin))
                        {
                            permission.RegisterPermission(key, plugin);
                        }
                    }
                }
            }

            public class Purchasing
            {
                [JsonProperty(PropertyName = "Player Buy Command (Chat or F1 Console)")]
                public string buyCommand { get; set; }
                [JsonProperty(PropertyName = "Purchasing Currency (ServerRewards|Economics|Custom)")]
                public string defaultCurrency { get; set; }
                [JsonProperty(PropertyName = "Currency Unit Displayed e.g: RP | $ (Not Used for Custom Currency)")]
                public string purchaseUnit { get; set; }

                [JsonProperty(PropertyName = "Custom Currency")]
                [JsonConverter(typeof(CurrencyItemConverter))] // DEBUG: Remove in a later update
                public CurrencyItem customCurrency { get; set; }

                // DEBUG: Converts the old List<CurrencyItem> config format
                // to the new single CurrencyItem format.
                private class CurrencyItemConverter : JsonConverter
                {
                    public override bool CanConvert(Type objectType)
                    {
                        return objectType == typeof(CurrencyItem);
                    }

                    public override object ReadJson(
                        JsonReader reader,
                        Type objectType,
                        object existingValue,
                        JsonSerializer serializer)
                    {
                        if (reader.TokenType == JsonToken.Null)
                            return null;

                        JToken token = JToken.Load(reader);
                        JToken currencyToken;

                        if (token.Type == JTokenType.Object)
                        {
                            currencyToken = token;
                        }
                        else if (token.Type == JTokenType.Array)
                        {
                            currencyToken = token.First;

                            if (currencyToken == null ||
                                currencyToken.Type != JTokenType.Object)
                            {
                                return null;
                            }
                        }
                        else
                        {
                            throw new JsonSerializationException(
                                $"Unexpected JSON token '{token.Type}' for Custom Currency."
                            );
                        }

                        var currencyItem = new CurrencyItem();

                        using (JsonReader itemReader = currencyToken.CreateReader())
                        {
                            serializer.Populate(itemReader, currencyItem);
                        }

                        return currencyItem;
                    }

                    public override void WriteJson(
                        JsonWriter writer,
                        object value,
                        JsonSerializer serializer)
                    {
                        serializer.Serialize(writer, value);
                    }
                }
            }

            public class Heli
            {
                [JsonProperty(PropertyName = "Player Give Up and Retire Command (Retires All of That Players Helis, NO Refund Given)")]
                public string retireCommand { get; set; }
                [JsonProperty(PropertyName = "Team Can Retire Helis Using the Command (Requires Use Friends/Clans/Teams option)")]
                public bool canTeamRetire { get; set; }
                [JsonProperty(PropertyName = "Global Helicopter Limit (0 = No Limit)")]
                public int globalLimit { get; set; }
                [JsonProperty(PropertyName = "Player Helicopter Limit (0 = No Limit)")]
                public int playerLimit { get; set; }
                [JsonProperty(PropertyName = "Allow Helis to be Called in Deep Sea")]
                public bool allowInDeepSea { get; set; }
                [JsonProperty(PropertyName = "Allow Helicopter to crash at nearest monument (Sets server ConVar: 'PatrolHelicopterAI.monument_crash')")]
                public bool canMonumentCrash { get; set; }
                [JsonProperty(PropertyName = "Allow Helicopter to flee attack (Sets server ConVar: 'PatrolHelicopterAI.use_danger_zones')")]
                public bool allowFlee { get; set; }
                [JsonProperty(PropertyName = "Percent damage to trigger helicopter Fleeing (Sets server ConVar: 'PatrolhHlicopterAI.flee_damage_percentage')")]
                public float fleePercent { get; set; }
                [JsonProperty(PropertyName = "Force Helicopter to Return to Player if it Moves Too far Away")]
                public bool returnToPlayer { get; set; }
                [JsonProperty(PropertyName = "Force Helicopter to Return Even if Attacking Other Players")]
                public bool returnIfAttacking { get; set; }
                [JsonProperty(PropertyName = "Force Helicopter to Return To Original Called Position Instead Of Player")]
                public bool returnToPosition { get; set; }
                [JsonProperty(PropertyName = "Max Distance of Helicopter From Player Before Force Return")]
                public float maxDistanceFromPlayer { get; set; }
                [JsonProperty(PropertyName = "Max Distance Helicopter Can Be Damaged By Any Player (0 = Disabled)")]
                public float maxHitDistance { get; set; }
                [JsonProperty(PropertyName = "Map Scale Distance Away to Spawn Helicopter (Default: 1.25 = 1.25 x Map Size Distance)")]
                public float mapScaleDistance { get; set; }
                [JsonProperty(PropertyName = "Height of heli when it arrives at called location")]
                public float arrivalHeight { get; set; }
                [JsonProperty(PropertyName = "Height of heli when it spawns (increase if it spawns under/in terrain)")]
                public float spawnHeight { get; set; }
                [JsonProperty(PropertyName = "Minimum altitude heli should try to maintain (0 = disabled, 50-60 advised)")]
                public float minHeliHeight { get; set; }
                [JsonProperty(PropertyName = "Maximum altitude heli should not fly above when engaging targets (0 = disabled, 70-80 advised)")]
                public float maxHeliHeight { get; set; }
                [JsonProperty(PropertyName = "Maximum altitude heli should not orbit above when engaging targets (0 = disabled, 50-70 advised)")]
                public float maxOrbitHeight { get; set; }
                [JsonProperty(PropertyName = "Heli targets players under water")]
                public bool targetUnderWater { get; set; }

                // DEBUG: Remove this later
                [JsonProperty("Retire if Attacking Player is Building Blocked, While 'Block Damage to Other Players Bases' is True")]
                public bool GetRetireWarning { get { return RetireWarning; } set { RetireWarning = value; } }
                public bool ShouldSerializeGetDisplayName() => false;
                // #######################

                [JsonProperty(PropertyName = "Retire if Attacker is Building Blocked, or Authed But Not in Team, While 'Block Damage to Other Players Bases' is True")]
                public bool RetireWarning { get; set; }
                [JsonProperty(PropertyName = "Retire Warning Threshold (Number of Warnings Allowed Before Retiring)")]
                public int WarningThreshold { get; set; }
                [JsonProperty(PropertyName = "Retire Heli on Calling Player/Team Killed")]
                public bool retireOnKilled { get; set; }
                [JsonProperty(PropertyName = "Retire Helis if Calling Player Goes to Deep Sea")]
                public bool retireOnDeepSeaTP { get; set; }

                // DEBUG: Update old config field name. Remove this later.
                [JsonProperty("Use NoEscape")]
                public bool GetuseNoEscape { get { return useNoEscape; } set { useNoEscape = value; } }
                public bool ShouldSerializeGetuseNoEscape() => false;
                // #######################

                [JsonProperty(PropertyName = "NoEscape: Allow NoEscape to Prevent Calling Heli Signals")]
                public bool useNoEscape { get; set; }
                [JsonProperty(PropertyName = "Player Cooldown (seconds) Between Calls (0 = no cooldown)")]
                public float playerCooldown { get; set; }
                [JsonProperty(PropertyName = "Player Cooldowns Apply to Each Tier Seperately")]
                public bool tierCooldowns { get; set; }
                [JsonProperty(PropertyName = "Cooldown Applies to Clan/Team/Friends (Requires Use Friends/Use Clan/Use Teams)")]
                public bool teamCooldown { get; set; }
                [JsonProperty(PropertyName = "Allow Players to Damage Helis With Remote Auto Turrets")]
                public bool allowTurretDamage { get; set; }
                [JsonProperty(PropertyName = "Heli Rockets Player Controlled Auto Turrets if Majority Damage Comes From Them")]
                public bool heliTargetTurret { get; set; }
                [JsonProperty(PropertyName = "Cooldown Before Heli Can Strafe Player Controlled Turrets Again (seconds)")]
                public float turretCooldown { get; set; }
                [JsonProperty(PropertyName = "Penalize Players With Majority Damage From Auto Turrets by This Percentage (0 = No Penalty)")]
                public double turretPenalty { get; set; }
                [JsonProperty(PropertyName = "Heli Signal heli crates float on water")]
                public bool buoyantHeliCrates { get; set; }
                [JsonProperty(PropertyName = "Vanilla heli crates float on water")]
                public bool buoyantVanillaCrates { get; set; }
                [JsonProperty(PropertyName = "Heli Signal hackable crates float on water")]
                public bool buoyantHackableCrates { get; set; }
                [JsonProperty(PropertyName = "Allow Players to Call Helis at Monuments")]
                public bool allowMonuments { get; set; }
                [JsonProperty(PropertyName = "Minimum Distance From Monuments When Allow at Monuments is False")]
                public float distFromMonuments { get; set; }
                [JsonProperty(PropertyName = "Monuments (Prefabs) to Either Block or Allow When Allow at Monuments is Either True or False")]
                public List<string> excludedMonuments { get; set; }
                [JsonProperty(PropertyName = "VIP/Custom Cooldowns")]
                public Hash<string, float> vipCooldowns { get; set; }
                [JsonProperty(PropertyName = "Protected Prefab List (Prefabs Listed Here Will Never Take Damage)")]
                public List<string> protectedPrefabs { get; set; }
                [JsonProperty(PropertyName = "Heli Wave Options")]
                public Dictionary<string, WaveData> waveConfig { get; set; }
                [JsonProperty(PropertyName = "Profiles")]
                public Dictionary<string, HeliData> heliConfig { get; set; }

                [JsonIgnore]
                public Oxide.Core.Libraries.Permission permission;
                public void RegisterPermissions(Oxide.Core.Libraries.Permission permission, HeliSignals plugin)
                {
                    this.permission = permission;
                    foreach (string key in vipCooldowns.Keys)
                    {
                        if (!permission.PermissionExists(key, plugin))
                        {
                            permission.RegisterPermission(key, plugin);
                        }
                    }
                }
            }
            public VersionNumber Version { get; set; }
        }

        public class CurrencyItem
        {
            [JsonProperty(PropertyName = "ShortName")]
            public string ShortName { get; set; }
            [JsonProperty(PropertyName = "SkinID")]
            public ulong SkinId { get; set; }
            [JsonProperty(PropertyName = "Display Name")]
            public string DisplayName { get; set; }
            [JsonProperty(PropertyName = "Item Text Field ('Item.text', default is blank)")]
            public string Text { get; set; }
        }

        public class CustomRewardItem
        {
            [JsonProperty(PropertyName = "ShortName")]
            public string ShortName { get; set; }
            [JsonProperty(PropertyName = "SkinID")]
            public ulong SkinId { get; set; }
            [JsonProperty(PropertyName = "Custom Display Name (leave blank unless creating custom items)")]
            public string DisplayName { get; set; }
            [JsonProperty(PropertyName = "Item Text Field ('Item.text', default is blank)")]
            public string Text { get; set; }
        }

        public class LootItem
        {
            [JsonProperty(PropertyName = "ShortName")]
            public string ShortName { get; set; }
            [JsonProperty(PropertyName = "Chance (0 - 100)")]
            public float Chance { get; set; }
            [JsonProperty(PropertyName = "Min Amount")]
            public int AmountMin { get; set; }
            [JsonProperty(PropertyName = "Max Amount")]
            public int AmountMax { get; set; }
            [JsonProperty(PropertyName = "SkinID")]
            public ulong SkinId { get; set; }
            [JsonProperty(PropertyName = "Custom Display Name (leave blank unless creating custom items)")]
            public string DisplayName { get; set; }
            [JsonProperty(PropertyName = "Item Text Field ('Item.text', default is blank)")]
            public string Text { get; set; }
            [JsonProperty(PropertyName = "Blueprint Chance Instead of Item, 0 = disabled. (0 - 100)")]
            public float BlueprintChance { get; set; }
        }

        public class WaveData
        {
            [JsonProperty(PropertyName = "SkinID")]
            public ulong SkinId { get; set; }
            [JsonProperty(PropertyName = "Profile shortname (for use in permission and give command)")]
            public string GiveItemCommand { get; set; }
            [JsonProperty(PropertyName = "Enable purchasing using custom currency via the buy command")]
            public bool UseBuyCommand { get; set; }
            [JsonProperty(PropertyName = "Cost to purchase (using buy command)")]
            public int CostToBuy { get; set; }
            [JsonProperty(PropertyName = "Cooldown delay between waves (seconds)")]
            public float WaveCooldown { get; set; }
            [JsonProperty(PropertyName = "Heli Wave Profile List (Helis Called in Order From Top to Bottom)")]
            public List<string> WaveProfiles { get; set; }
        }

        public class HeliData
        {
        	// Init/Setup
            [JsonProperty(PropertyName = "Init & Setup Options")]
            public InitOptions Init { get; set; }

            public class InitOptions
            {
                [JsonProperty(PropertyName = "Number of helicopters called to the player")]
                public int HeliAmount { get; set; }
                [JsonProperty(PropertyName = "Helicopter display name")]
                public string HeliName { get; set; }
                [JsonProperty(PropertyName = "Skin ID of the custom Supply Signal")]
                public ulong SignalSkinID { get; set; }
                [JsonProperty(PropertyName = "Profile shortname (for use in permission and give command)")]
                public string GiveItemCommand { get; set; }
                [JsonProperty(PropertyName = "Enable purchasing using custom currency via the buy command")]
                public bool UseBuyCommand { get; set; }
                [JsonProperty(PropertyName = "Cost to purchase (using buy command)")]
                public int CostToBuy { get; set; }
                [JsonProperty(PropertyName = "Starting health")]
                public float Health { get; set; }
                [JsonProperty(PropertyName = "Main rotor health")]
                public float MainRotorHealth { get; set; }
                [JsonProperty(PropertyName = "Tail rotor health")]
                public float TailRotorHealth { get; set; }
                [JsonProperty(PropertyName = "Initial Helicopter speed until it arrives at location")]
                public float InitialSpeed { get; set; }
                [JsonProperty(PropertyName = "Helicopter max speed (Default = 42)")]
                public float MaxSpeed { get; set; }
                [JsonProperty(PropertyName = "Distance from target when orbiting (Default = 75)")]
                public float OrbitRadius { get; set; }
                [JsonProperty(PropertyName = "Max orbit duration when Helicopter arrives at location (Default = 30)")]
                public float MaxOrbitDuration { get; set; }
                [JsonProperty(PropertyName = "Helicopter max rotation speed SCALE (Default = 1.0)")]
                public float MaxRotationSpeed { get; set; }
                [JsonProperty(PropertyName = "Terrain pushback force (Help stop faster helis glitch under map)")]
                public float TerrainPushForce { get; set; }
                [JsonProperty(PropertyName = "Obstacle pushback force (Help stop faster helis glitch through structures)")]
                public float ObstaclePushForce { get; set; }
                [JsonProperty(PropertyName = "Despawn timer")]
                public float DespawnTime { get; set; }
            }

            // Targeting/Damage
            [JsonProperty(PropertyName = "Targeting & Damage")]
            public TargetingDamageOptions TargetingDamage { get; set; }
            
            public class TargetingDamageOptions
            {
                [JsonProperty(PropertyName = "Only owner can damage (and team if enabled)")]
                public bool OwnerDamage { get; set; }
                [JsonProperty(PropertyName = "Allow Helicopter to target other players")]
                public bool TargetOtherPlayers { get; set; }
                [JsonProperty(PropertyName = "Block damage to calling players bases")]
                public bool BlockOwnerDamage { get; set; }
                [JsonProperty(PropertyName = "Block damage to other players bases")]
                public bool BlockOtherDamage { get; set; }
                [JsonProperty(PropertyName = "Block damage to other players")]
                public bool BlockPlayerDamage { get; set; }
                [JsonProperty(PropertyName = "Block damage ALWAYS to entities in the protected prefab list")]
                public bool BlockProtectedList { get; set; }

            }

            // Crates
            [JsonProperty(PropertyName = "Crate Options")]
            public CrateOptions Crates { get; set; }
            
            public class CrateOptions
            {
                [JsonProperty(PropertyName = "Number of crates to spawn")]
                public int CratesToSpawn { get; set; }
                [JsonProperty(PropertyName = "Heli crate despawn time")]
                public float HeliCrateDespawn { get; set; }
                [JsonProperty(PropertyName = "Disable fire on crates")]
                public bool DisableFire { get; set; }
                [JsonProperty(PropertyName = "Crate fire duration (seconds)")]
                public float FireDuration { get; set; }
                [JsonProperty(PropertyName = "Number of locked hackable crates to spawn")]
                public int LockedCratesToSpawn { get; set; }
                [JsonProperty(PropertyName = "Hack time for locked crate (seconds)")]
                public float HackSeconds { get; set; }
                [JsonProperty(PropertyName = "Locked crate despawn time (seconds)")]
                public float LockedCrateDespawn { get; set; }
                [JsonProperty(PropertyName = "Disable locked crate map markers")]
                public bool DisableMapMarker { get; set; }
                [JsonProperty(PropertyName = "Lock looting crates to owner")]
                public bool ProtectCrates { get; set; }
                [JsonProperty(PropertyName = "Unlock looting crates to others after time in seconds (0 = Never)")]
                public float UnlockCrates { get; set; }
            }

            // Weapons
            [JsonProperty(PropertyName = "Weapons Options")]
            public WeaponOptions Weapons { get; set; }
            
            public class WeaponOptions
            {
                [JsonProperty(PropertyName = "Bullet damage (Default = 20)")]
                public float BulletDamage { get; set; }
                [JsonProperty(PropertyName = "Bullet speed (Default = 250)")]
                public int BulletSpeed { get; set; }
                [JsonProperty(PropertyName = "Gun fire rate (Default = 0.125)")]
                public float GunFireRate { get; set; }
                [JsonProperty(PropertyName = "Gun burst length (Default = 3)")]
                public float BurstLength { get; set; }
                [JsonProperty(PropertyName = "Time between bursts (Default = 3)")]
                public float TimeBetweenBursts { get; set; }
                [JsonProperty(PropertyName = "New target detection range (Default = 150)")]
                public float NewTargetRange { get; set; }
                [JsonProperty(PropertyName = "Max targeting range (Default = 300)")]
                public float MaxTargetRange { get; set; }
                [JsonProperty(PropertyName = "Weapon accuracy % (1 to 100)")]
                public float BulletAccuracy { get; set; }
                [JsonProperty(PropertyName = "Max number of rockets to fire (Default = 12)")]
                public int MaxHeliRockets { get; set; }
                [JsonProperty(PropertyName = "Time between rockets (Default = 0.2)")]
                public float TimeBetweenRockets { get; set; }
                [JsonProperty(PropertyName = "Rocket damage scale (Default = 1.0)")]
                public float RocketDamageScale { get; set; }
                [JsonProperty(PropertyName = "Napalm chance (Default = 0.75)")]
                public float NapalmChance { get; set; }
                [JsonProperty(PropertyName = "Orbit Strafe chance (Default = 0.4)")]
                public float OrbitStrafeChance { get; set; }
                [JsonProperty(PropertyName = "Number of rockets to fire during orbit strafe (Default = 12)")]
                public int MaxOrbitRockets { get; set; }
                [JsonProperty(PropertyName = "Minimum variance to number of rockets fired during orbit strafe (Default = -3)")]
                public int MinOrbitMultiplier { get; set; }
                [JsonProperty(PropertyName = "Maximum variance to number of rockets fired during orbit strafe (Default = 24)")]
                public int MaxOrbitMultiplier { get; set; }
                [JsonProperty(PropertyName = "Minimum time between strafe attacks")]
                public float StrafeCooldown { get; set; }
            }

            // Gibs
            [JsonProperty(PropertyName = "Gibs Options")]
            public GibsOptions Gibs { get; set; }
            
            public class GibsOptions
            {
                [JsonProperty(PropertyName = "Disable Heli gibs")]
                public bool KillGibs { get; set; }
                [JsonProperty(PropertyName = "Gibs too hot to mine time (Seconds)")]
                public float GibsHotTime { get; set; }
                [JsonProperty(PropertyName = "Health of gibs (more health = more resources)")]
                public float GibsHealth { get; set; }
                [JsonProperty(PropertyName = "Lock mining gibs to owner")]
                public bool ProtectGibs { get; set; }
                [JsonProperty(PropertyName = "Unlock mining gibs to others after time in seconds (0 = Never)")]
                public float UnlockGibs { get; set; }
            }

            // Fireballs
            [JsonProperty(PropertyName = "Fireball Options")]
            public FireballOptions Fireball { get; set; }
            
            public class FireballOptions
            {
                [JsonProperty(PropertyName = "Disable Heli fire")]
                public bool DisableFire { get; set; }
                [JsonProperty(PropertyName = "Minimum duration (Rust default = 20.0)")]
                public float MinimumLifeTime { get; set; }
                [JsonProperty(PropertyName = "Maximum duration (Rust default = 40.0)")]
                public float MaximumLifeTime { get; set; }
                [JsonProperty(PropertyName = "Spread chance percent (1.0 = 100%, 0.5 = 50%, etc)")]
                public float SpreadChance { get; set; }
                [JsonProperty(PropertyName = "Spread at lifetime percent (1.0 = 100%, 0.5 = 50%, etc)")]
                public float SpreadAtLifetimePercent { get; set; }
                [JsonProperty(PropertyName = "Damage per second (Default = 2.0)")]
                public float DamagePerSecond { get; set; }
                [JsonProperty(PropertyName = "Damage rate")]
                public float DamageRate { get; set; }
                [JsonProperty(PropertyName = "Water required to extinguish")]
                public int WaterAmountToExtinguish { get; set; }
            }

            //Rewards
            [JsonProperty(PropertyName = "Reward Options")]
            public RewardOptions Rewards { get; set; }
            
            public class RewardOptions
            {
                [JsonProperty(PropertyName = "Reward points issued when destroyed (if enabled)")]
                public double RewardPoints { get; set; }
                [JsonProperty(PropertyName = "XP issued when destroyed (if enabled)")]
                public double XPReward { get; set; }
                [JsonProperty(PropertyName = "Scrap amount issued when destroyed (if enabled)")]
                public int ScrapReward { get; set; }
                [JsonProperty(PropertyName = "Custom reward amount issued when destroyed (if enabled)")]
                public int CustomReward { get; set; }
                [JsonProperty(PropertyName = "Damage Threshold (Min damage player needs to contribute to get rewards)")]
                public float DamageThreshold { get; set; }
            }

            // BotReSpawn
            [JsonProperty(PropertyName = "BotReSpawn Options")]
            public BotReSpawnOptions BotReSpawn { get; set; }
            
            public class BotReSpawnOptions
            {
                [JsonProperty(PropertyName = "BotReSpawn profile to spawn at crash site (leave blank for not using)")]
                public string BotReSpawnProfile { get; set; }
            }

            // ZoneManager
            [JsonProperty(PropertyName = "ZoneManager Options")]
            public ZoneManagerOptions ZoneManager { get; set; }
            
            public class ZoneManagerOptions
            {
                [JsonProperty(PropertyName = "Use Zone Manager options")]
                public bool UseZoneManager { get; set; }
                [JsonProperty(PropertyName = "Players can only call in heli zones")]
                public bool UseHeliZones { get; set; }
                [JsonProperty(PropertyName = "Heli can leave zone")]
                public bool CanLeaveZone { get; set; }
                [JsonProperty(PropertyName = "Drop zone ID list")]
                public List<string> HeliZoneIDs { get; set; }
                [JsonProperty(PropertyName = "Prevent heli entering protected zones")]
                public bool UseProtectedZones { get; set; }
                [JsonProperty(PropertyName = "Protected zone ID list")]
                public List<string> ProtectedZoneIDs { get; set; }
            }

            // Run Commands
            [JsonProperty(PropertyName = "Commands Options")]
            public CommandsToRunOptions CommandsToRun { get; set; }
            
            public class CommandsToRunOptions
            {
                [JsonProperty(PropertyName = "Run List Of Commands On Heli Death")]
                public bool RunCommands { get; set; }
                [JsonProperty(PropertyName = "Command list (top -> bottom). Available placeholders: Owner SteamID: {ownerId} Calling Team SteamID List: {teamIds}  Heli Profile (Name): {heliProfile}")]
                public List<string> Commands { get; set; }
            }

            [JsonProperty(PropertyName = "Loot Options")]
            public LootOptions Loot { get; set; }

            public class LootOptions
            {
                [JsonProperty(PropertyName = "Allow Epic Loot to add items to crates")]
                public bool AllowEpicLoot { get; set; }
                [JsonProperty(PropertyName = "Use custom loot table to override crate loot")]
                public bool UseCustomLoot { get; set; }

                // DEBUG: Remove this later
                [JsonProperty("Minimum number loot items in crate (0 - 12)")]
                public int GetMinCrateItems { get { return MinCrateItems; } set { MinCrateItems = value; } }
                public bool ShouldSerializeGetMinCrateItems() => false;
                // #######################
                [JsonProperty(PropertyName = "Minimum number loot items in crate (1 - 48)")]
                public int MinCrateItems { get; set; }

                // DEBUG: Remove this later
                [JsonProperty("Maximum number loot items in crate (0 - 12)")]
                public int GetMaxCrateItems { get { return MaxCrateItems; } set { MaxCrateItems = value; } }
                public bool ShouldSerializeGetMaxCrateItems() => false;
                // #######################
                [JsonProperty(PropertyName = "Maximum number loot items in crate (1 - 48)")]
                public int MaxCrateItems { get; set; }

                [JsonProperty(PropertyName = "Allow duplication of loot items")]
                public bool AllowDupes { get; set; }
                [JsonProperty(PropertyName = "Maximum number of BPs in each crate")]
                public int MaxBP { get; set; }
                [JsonProperty(PropertyName = "Custom loot table")]
                public List<LootItem> LootTable { get; set; }
            }

            [JsonProperty(PropertyName = "Extra Loot Options")]
            public ExtraLootOptions ExtraLoot { get; set; }

            public class ExtraLootOptions
            {
                // DEBUG: Remove this later
                [JsonProperty("Use extra loot table (NOTE: Total of crate loot + extra items cannot exceed 12")]
                public bool GetUseExtraLoot { get { return UseExtraLoot; } set { UseExtraLoot = value; } }
                public bool ShouldSerializeGetUseExtraLoot() => false;
                // #######################
                [JsonProperty(PropertyName = "Use extra loot table (NOTE: Total loot + extra items should not exceed 48)")]
                public bool UseExtraLoot { get; set; }
                [JsonProperty(PropertyName = "Minimum number extra items to add to crate")]
                public int MinExtraItems { get; set; }
                [JsonProperty(PropertyName = "Maximum number extra items to add to crate")]
                public int MaxExtraItems { get; set; }
                [JsonProperty(PropertyName = "Allow duplication of extra items")]
                public bool AllowDupes { get; set; }
                [JsonProperty(PropertyName = "Maximum number of BPs in each crate")]
                public int MaxBP { get; set; }
                [JsonProperty(PropertyName = "Extra loot table")]
                public List<LootItem> LootTable { get; set; }
            }

            [JsonProperty(PropertyName = "Locked Crate Loot Options")]
            public LockedCrateLootOptions LockedCrateLoot { get; set; }

            public class LockedCrateLootOptions
            {
                [JsonProperty(PropertyName = "Allow Epic Loot to add items to crates")]
                public bool AllowEpicLoot { get; set; }

                // DEBUG: Remove this later
                [JsonProperty("Use locked crate loot table (NOTE: Total items cannot exceed 36)")]
                public bool GetUseLockedCrateLoot { get { return UseLockedCrateLoot; } set { UseLockedCrateLoot = value; } }
                public bool ShouldSerializeGetUseLockedCrateLoot() => false;
                // #######################
                [JsonProperty(PropertyName = "Use locked crate loot table (NOTE: Total should not exceed 48)")]
                public bool UseLockedCrateLoot { get; set; }
                [JsonProperty(PropertyName = "Minimum number items to add to locked crate")]
                public int MinLockedCrateItems { get; set; }
                [JsonProperty(PropertyName = "Maximum number items to add to locked crate")]
                public int MaxLockedCrateItems { get; set; }
                [JsonProperty(PropertyName = "Allow duplication of locked crate items")]
                public bool AllowDupes { get; set; }
                [JsonProperty(PropertyName = "Maximum number of BPs in crate")]
                public int MaxBP { get; set; }
                [JsonProperty(PropertyName = "Locked crate loot table")]
                public List<LootItem> LootTable { get; set; }
            }

            [JsonProperty(PropertyName = "Locked Crate Extra Loot Options")]
            public LockedCrateExtraLootOptions LockedCrateExtraLoot { get; set; }

            public class LockedCrateExtraLootOptions
            {
                [JsonProperty(PropertyName = "Use locked crate extra loot table (NOTE: Total loot + extra items should not exceed 48)")]
                public bool UseExtraLoot { get; set; }
                [JsonProperty(PropertyName = "Minimum number extra items to add to crate")]
                public int MinExtraItems { get; set; }
                [JsonProperty(PropertyName = "Maximum number extra items to add to crate")]
                public int MaxExtraItems { get; set; }
                [JsonProperty(PropertyName = "Allow duplication of extra items")]
                public bool AllowDupes { get; set; }
                [JsonProperty(PropertyName = "Maximum number of BPs in each crate")]
                public int MaxBP { get; set; }
                [JsonProperty(PropertyName = "Locked Crate Extra loot table")]
                public List<LootItem> LootTable { get; set; }
            }
        }

        #endregion Config Options

        #region Config Data

        private ConfigData GetDefaultConfig()
        {
            return new ConfigData
            {
                options = new ConfigData.Options
                {
                    useFriends = false,
                    useClans = false,
                    useTeams = false,
                    useDynamicPVP = false,
                    useBetterNPC = false,
                    chatPrefix = "<color=orange>[Heli Signals]</color>",
                    usePrefix = true,
                    chatIcon = 0,
                    signalFuseLength = 3.5f,
                    smokeDuration = 210f,
                    noVanillaHeli = false,
                    useStacking = true,
                    reportCommand = "hsreport",
                    enableDebug = false
                },
                announce = new ConfigData.Announce
                {
                    killChat = true,
                    callChat = true,
                    retireChat = true,
                    reportChat = true,
                    reportRetire = true,
                    killVanilla = false,
                    reportVanilla = false,
                    vanillaName = "Patrol Helicopter",
                    vanillaOwner = "USAF (SERVER)",
                    maxReported = 5,
                    announceGlobal = true
                },
                discord = new ConfigData.Discord
                {
                    webhookUrl = defaultWebhookUrl,
                    defaultBotName = "HeliSignals Bot",
                    customAvatarUrl = "",
                    useCustomAvatar = false,
                    sendHeliCall = false,
                    sendHeliKill = false,
                    sendHeliRetire = false
                },
                rewards = new ConfigData.Rewards
                {
                    rewardPlugin = "ServerRewards",
                    rewardUnit = "RP",
                    enableRewards = false,
                    shareRewards = false,
                    pluginXP = "XPerience",
                    enableXP = false,
                    shareXP = false,
                    boostXP = false,
                    enableScrap = false,
                    shareScrap = false,
                    enableCustomReward = false,
                    shareCustomReward = false,
                    customRewardItem = new CustomRewardItem
                    {
                        ShortName = "item.shortname",
                        SkinId = 0,
                        DisplayName = string.Empty,
                        Text = string.Empty
                    },
                    rewardMultipliers = new Hash<string, float>
                    {
                        ["helisignals.vip1"] = 1.25f,
                        ["helisignals.vip2"] = 1.50f,
                        ["helisignals.vip3"] = 1.75f
                    }
                },
                purchasing = new ConfigData.Purchasing
                {
                    buyCommand = "hsbuy",
                    defaultCurrency = "ServerRewards",
                    purchaseUnit = "RP",
                    customCurrency = new CurrencyItem
                    {
                        ShortName = "scrap", 
                        SkinId = 0, 
                        DisplayName = "Scrap", 
                        Text = string.Empty
                    }
                },
                heli = new ConfigData.Heli
                {
                    retireCommand = "hsretire",
                    canTeamRetire = false,
                    globalLimit = 10,
                    playerLimit = 3,
                    allowInDeepSea = false,
                    canMonumentCrash = true,
                    allowFlee = true,
                    fleePercent = 0.35f,
                    returnToPlayer = false,
                    returnIfAttacking = false,
                    returnToPosition = false,
                    maxDistanceFromPlayer = 500f,
                    maxHitDistance = 0f,
                    mapScaleDistance = 1.25f,
                    arrivalHeight = 20f,
                    spawnHeight = 100f,
                    minHeliHeight = 50f,
                    maxHeliHeight = 70f,
                    maxOrbitHeight = 60f,
                    targetUnderWater = false,
                    RetireWarning = false,
                    WarningThreshold = 25,
                    retireOnKilled = false,
                    retireOnDeepSeaTP = false,
                    useNoEscape = false,
                    playerCooldown = 3600f,
                    tierCooldowns = true,
                    teamCooldown = true,
                    allowTurretDamage = true,
                    heliTargetTurret = true,
                    turretCooldown = 30f,
                    turretPenalty = 0,
                    buoyantHeliCrates = false,
                    buoyantVanillaCrates = false,
                    buoyantHackableCrates = false,
                    allowMonuments = false,
                    distFromMonuments = 50f,
                    excludedMonuments = new List<string>
                    {
                        "assets/bundled/prefabs/autospawn/monument/arctic_bases/arctic_research_base_a.prefab",
                        "assets/bundled/prefabs/autospawn/monument/harbor/ferry_terminal_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/harbor/harbor_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/harbor/harbor_2.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/airfield_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/excavator_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/military_tunnel_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/powerplant_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/trainyard_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/water_treatment_plant_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/large/trainyard_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/lighthouse/lighthouse.prefab",
                        "assets/bundled/prefabs/autospawn/monument/medium/junkyard_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/medium/nuclear_missile_silo.prefab",
                        "assets/bundled/prefabs/autospawn/monument/medium/radtown_small_3.prefab",
                        "assets/bundled/prefabs/autospawn/monument/military_bases/desert_military_base_a.prefab",
                        "assets/bundled/prefabs/autospawn/monument/military_bases/desert_military_base_b.prefab",
                        "assets/bundled/prefabs/autospawn/monument/military_bases/desert_military_base_c.prefab",
                        "assets/bundled/prefabs/autospawn/monument/military_bases/desert_military_base_d.prefab",
                        "assets/bundled/prefabs/autospawn/monument/offshore/oilrig_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/offshore/oilrig_2.prefab",
                        "assets/bundled/prefabs/autospawn/monument/roadside/gas_station_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/roadside/supermarket_1.prefab",
                        "assets/bundled/prefabs/autospawn/monument/roadside/warehouse.prefab",
                        "assets/bundled/prefabs/autospawn/monument/small/satellite_dish.prefab",
                        "assets/bundled/prefabs/autospawn/monument/small/sphere_tank.prefab",
                        "assets/bundled/prefabs/autospawn/monument/swamp/swamp_a.prefab",
                        "assets/bundled/prefabs/autospawn/monument/swamp/swamp_b.prefab",
                        "assets/bundled/prefabs/autospawn/monument/swamp/swamp_c.prefab",
                        "assets/bundled/prefabs/autospawn/monument/tiny/water_well_a.prefab",
                        "assets/bundled/prefabs/autospawn/monument/tiny/water_well_b.prefab",
                        "assets/bundled/prefabs/autospawn/monument/tiny/water_well_c.prefab",
                        "assets/bundled/prefabs/autospawn/monument/tiny/water_well_d.prefab",
                        "assets/bundled/prefabs/autospawn/monument/tiny/water_well_e.prefab",
                        "assets/bundled/prefabs/autospawn/monument/xlarge/launch_site_1.prefab"
                    },
                    vipCooldowns = new Hash<string, float>
                    {
                        ["helisignals.examplevip1"] = 3000f,
                        ["helisignals.examplevip2"] = 2400f,
                        ["helisignals.examplevip3"] = 1800f
                    },
                    protectedPrefabs = new List<string>
                    {
                        "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
                        "assets/prefabs/deployable/planters/planter.large.deployed.prefab"
                    },
                    waveConfig = new Dictionary<string, WaveData>
                    {
                        [normalWave] = new WaveData
                        {
                            SkinId = normalWaveSkinID,
                            GiveItemCommand = "wave_normal",
                            UseBuyCommand = false,
                            CostToBuy = 10000,
                            WaveCooldown = 120f,
                            WaveProfiles = new List<string>()
                        },
                        [hardWave] = new WaveData
                        {
                            SkinId = hardWaveSkinID,
                            GiveItemCommand = "wave_hard",
                            UseBuyCommand = false,
                            CostToBuy = 20000,
                            WaveCooldown = 120f,
                            WaveProfiles = new List<string>()
                        }
                    },
                    heliConfig = new Dictionary<string, HeliData>
                    {
                        [easyHeli] = new HeliData
                        {
                        	Init = new HeliData.InitOptions
                            {
                                HeliAmount = 1,
                                HeliName = easyHeli,
                                SignalSkinID = easySkinID,
                                GiveItemCommand = "easy",
                                UseBuyCommand = true,
                                CostToBuy = 500,
                                Health = 10000f,
                                MainRotorHealth = 900f,
                                TailRotorHealth = 500f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 1200f,
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 4,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 20f,
                                BulletSpeed = 250,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 300f,
                                BulletAccuracy = 40f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 1000,
                                XPReward = 1000,
                                ScrapReward = 1000,
                                CustomReward = 1000,
                                DamageThreshold = 100f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [medHeli] = new HeliData
                        {
                        	Init = new HeliData.InitOptions
                            {
                                HeliAmount = 1,
                                HeliName = medHeli,
                                SignalSkinID = medSkinID,
                                GiveItemCommand = "medium",
                                UseBuyCommand = true,
                                CostToBuy = 1000,
                                Health = 20000f,
                                MainRotorHealth = 1800f,
                                TailRotorHealth = 1000f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 1800f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 6,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 30f,
                                BulletSpeed = 300,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 320f,
                                BulletAccuracy = 60f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 1000f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 2000,
                                XPReward = 2000,
                                ScrapReward = 2000,
                                CustomReward = 2000,
                                DamageThreshold = 200f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 4,
                                MaxCrateItems = 8,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [hardHeli] = new HeliData
                        {
                            Init = new HeliData.InitOptions
                            {
                                HeliAmount = 1,
                                HeliName = hardHeli,
                                SignalSkinID = hardSkinID,
                                GiveItemCommand = "hard",
                                UseBuyCommand = true,
                                CostToBuy = 2000,
                                Health = 30000f,
                                MainRotorHealth = 2700f,
                                TailRotorHealth = 1500f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 2400f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 8,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 40f,
                                BulletSpeed = 350,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 340f,
                                BulletAccuracy = 80f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 4000,
                                XPReward = 4000,
                                ScrapReward = 4000,
                                CustomReward = 4000,
                                DamageThreshold = 400f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 6,
                                MaxCrateItems = 10,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [eliteHeli] = new HeliData
                        {
                            Init = new HeliData.InitOptions
                            {
                                HeliAmount = 1,
                                HeliName = eliteHeli,
                                SignalSkinID = eliteSkinID,
                                GiveItemCommand = "elite",
                                UseBuyCommand = true,
                                CostToBuy = 4000,
                                Health = 40000f,
                                MainRotorHealth = 3600f,
                                TailRotorHealth = 2000f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 3600f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 10,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 50f,
                                BulletSpeed = 400,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 360f,
                                BulletAccuracy = 40f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f,
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 8000,
                                XPReward = 8000,
                                ScrapReward = 8000,
                                CustomReward = 8000,
                                DamageThreshold = 600f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 8,
                                MaxCrateItems = 12,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [easyMulti] = new HeliData
                        {
                            Init = new HeliData.InitOptions
                            {
                                HeliAmount = 2,
                                HeliName = easyMulti,
                                SignalSkinID = easyMultiSkinID,
                                GiveItemCommand = "easy_multi",
                                UseBuyCommand = true,
                                CostToBuy = 750,
                                Health = 10000f,
                                MainRotorHealth = 900f,
                                TailRotorHealth = 500f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 1200f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 4,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 20f,
                                BulletSpeed = 250,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 300f,
                                BulletAccuracy = 40f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 1000,
                                XPReward = 1000,
                                ScrapReward = 1000,
                                CustomReward = 1000,
                                DamageThreshold = 100f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [medMulti] = new HeliData
                        {
                            Init = new HeliData.InitOptions
                            {
                                HeliAmount = 2,
                                HeliName = medMulti,
                                SignalSkinID = medMultiSkinID,
                                GiveItemCommand = "medium_multi",
                                UseBuyCommand = true,
                                CostToBuy = 1500,
                                Health = 20000f,
                                MainRotorHealth = 1800f,
                                TailRotorHealth = 1000f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 1800f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 6,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 30f,
                                BulletSpeed = 300,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 320f,
                                BulletAccuracy = 60f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 1000f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 2000,
                                XPReward = 2000,
                                ScrapReward = 2000,
                                CustomReward = 2000,
                                DamageThreshold = 200f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 4,
                                MaxCrateItems = 8,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [hardMulti] = new HeliData
                        {
                            Init = new HeliData.InitOptions
                            {
                                HeliAmount = 2,
                                HeliName = hardMulti,
                                SignalSkinID = hardMultiSkinID,
                                GiveItemCommand = "hard_multi",
                                UseBuyCommand = true,
                                CostToBuy = 3000,
                                Health = 30000f,
                                MainRotorHealth = 2700f,
                                TailRotorHealth = 1500f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 2400f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 8,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 40f,
                                BulletSpeed = 350,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 340f,
                                BulletAccuracy = 80f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 4000,
                                XPReward = 4000,
                                ScrapReward = 4000,
                                CustomReward = 4000,
                                DamageThreshold = 400f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 6,
                                MaxCrateItems = 10,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        },
                        [eliteMulti] = new HeliData
                        {
                            Init = new HeliData.InitOptions
                            {
                                HeliAmount = 2,
                                HeliName = eliteMulti,
                                SignalSkinID = eliteMultiSkinID,
                                GiveItemCommand = "elite_multi",
                                UseBuyCommand = true,
                                CostToBuy = 6000,
                                Health = 40000f,
                                MainRotorHealth = 3600f,
                                TailRotorHealth = 2000f,
                                InitialSpeed = 42f,
                                MaxSpeed = 42f,
                                OrbitRadius = 75f,
                                MaxOrbitDuration = 30f,
                                MaxRotationSpeed = 1.0f,
                                TerrainPushForce = 150f,
                                ObstaclePushForce = 150f,
                                DespawnTime = 3600f
                            },
                            TargetingDamage = new HeliData.TargetingDamageOptions
                            {
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new HeliData.CrateOptions
                            {
                                CratesToSpawn = 10,
                                HeliCrateDespawn = 1800f,
                                DisableFire = false,
                                FireDuration = 300f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new HeliData.WeaponOptions
                            {
                                BulletDamage = 50f,
                                BulletSpeed = 400,
                                GunFireRate = 0.125f,
                                BurstLength = 3f,
                                TimeBetweenBursts = 3f,
                                NewTargetRange = 150f,
                                MaxTargetRange = 360f,
                                BulletAccuracy = 40f,
                                MaxHeliRockets = 12,
                                TimeBetweenRockets = 0.2f,
                                RocketDamageScale = 1.0f,
                                NapalmChance = 0.75f,
                                OrbitStrafeChance = 0.4f,
                                MaxOrbitRockets = 12,
                                MinOrbitMultiplier = -3,
                                MaxOrbitMultiplier = 24,
                                StrafeCooldown = 20f
                            },
                            Gibs = new HeliData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new HeliData.FireballOptions
                            {
                                DisableFire = false,
                                MinimumLifeTime = 20f,
                                MaximumLifeTime = 40f,
                                SpreadChance = 0.5f,
                                SpreadAtLifetimePercent = 0.5f,
                                DamagePerSecond = 2f,
                                DamageRate = 0.5f,
                                WaterAmountToExtinguish = 200
                            },
                            Rewards = new HeliData.RewardOptions
                            {
                                RewardPoints = 8000,
                                XPReward = 8000,
                                ScrapReward = 8000,
                                CustomReward = 8000,
                                DamageThreshold = 600f
                            },
                            BotReSpawn = new HeliData.BotReSpawnOptions
                            {
                                BotReSpawnProfile = ""
                            },
                            ZoneManager = new HeliData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseHeliZones = false,
                                CanLeaveZone = false,
                                HeliZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new HeliData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {heliProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            Loot = new HeliData.LootOptions
                            {
                                AllowEpicLoot = false,
                                UseCustomLoot = false,
                                MinCrateItems = 8,
                                MaxCrateItems = 12,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new HeliData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new HeliData.LockedCrateLootOptions
                            {
                                AllowEpicLoot = false,
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 0, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            }
                        }
                    }
                },
                Version = Version
            };
        }

        #endregion Config Data

        #region Config Load/Update

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();

                if (config is null)
                {
                    LoadDefaultConfig();
                }
                else if (config.Version < Version)
                {
                    UpdateConfigValues();
                }
            }
            catch (Exception ex)
            {
                if (ex is JsonSerializationException || ex is NullReferenceException || ex is JsonReaderException || ex is KeyNotFoundException)
                {
                    Log($"Exception Type: {ex.GetType()}", LogLevel.ERROR);
                    Log($"Exception: {ex}", LogLevel.ERROR);
                    return;
                }
                throw;
            }
        }

        protected override void LoadDefaultConfig()
        {
            Log("Configuration file missing or corrupt, creating default config file.", LogLevel.WARN);
            config = GetDefaultConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        private void UpdateConfigValues()
        {
            Log("Config update detected! Updating config file...", LogLevel.WARN);

            ConfigData defaultConfig = GetDefaultConfig();

            if (config.Version < new VersionNumber(1, 3, 0))
            {
                string configPath = Config?.Filename ?? Path.Combine(Interface.Oxide.ConfigDirectory, $"{Name}.json");

                Dictionary<string, HeliData> migratedProfiles = MigrateHeliProfilesFromRawJson(configPath, defaultConfig?.heli?.heliConfig);

                if (migratedProfiles != null && migratedProfiles.Count > 0)
                {
                    config.heli.heliConfig = migratedProfiles;
                }
                else
                {
                    Log("No Heli Signal profiles were migrated. The existing deserialized profile data will be retained.", LogLevel.WARN);
                }

                config.heli.excludedMonuments = defaultConfig.heli.excludedMonuments;
                config.rewards.customRewardItem.Text = defaultConfig.rewards.customRewardItem.Text;

                if (config.purchasing.customCurrency == null)
                {
                    config.purchasing.customCurrency = defaultConfig.purchasing.customCurrency;
                }
                else
                {
                    config.purchasing.customCurrency.ShortName ??= defaultConfig.purchasing.customCurrency.ShortName;
                    config.purchasing.customCurrency.DisplayName ??= defaultConfig.purchasing.customCurrency.DisplayName;
                    config.purchasing.customCurrency.Text ??= defaultConfig.purchasing.customCurrency.Text;
                }
            }

            // Per profile updates
            foreach (var kvp in config.heli.heliConfig)
            {
                const string FALLBACK_PROFILE_KEY = easyHeli;
                string profileName = kvp.Key;
                var heliData = kvp.Value;

                // Try to get matching default profile OR fall back to base profile if not
                HeliData defaultHeli = null;
                if (!defaultConfig.heli.heliConfig.TryGetValue(profileName, out defaultHeli))
                {
                    if (!defaultConfig.heli.heliConfig.TryGetValue(FALLBACK_PROFILE_KEY, out defaultHeli))
                    {
                        Log($"Could not find fallback Heli profile '{FALLBACK_PROFILE_KEY}'. Custom profile '{profileName}' may not patch correctly.", LogLevel.WARN);
                        continue;
                    }
                }

                if (config.Version < new VersionNumber(1, 3, 0))
                {
                    foreach (var item in heliData.Loot.LootTable)
                    {
                        item.Text = string.Empty;
                    }

                    foreach (var item in heliData.ExtraLoot.LootTable)
                    {
                        item.Text = string.Empty;
                    }

                    foreach (var item in heliData.LockedCrateLoot.LootTable)
                    {
                        item.Text = string.Empty;
                    }

                    heliData.Crates.DisableMapMarker = false;
                }

                if (config.Version < new VersionNumber(1, 4, 0))
                {
                    heliData.LockedCrateExtraLoot = new HeliData.LockedCrateExtraLootOptions
                    {
                        UseExtraLoot = defaultHeli.LockedCrateExtraLoot.UseExtraLoot,
                        MinExtraItems = defaultHeli.LockedCrateExtraLoot.MinExtraItems,
                        MaxExtraItems = defaultHeli.LockedCrateExtraLoot.MaxExtraItems,
                        AllowDupes = defaultHeli.LockedCrateExtraLoot.AllowDupes,
                        MaxBP = defaultHeli.LockedCrateExtraLoot.MaxBP,
                        LootTable = defaultHeli.LockedCrateExtraLoot.LootTable
                    };

                    heliData.CommandsToRun = new HeliData.CommandsToRunOptions
                    {
                        RunCommands = defaultHeli.CommandsToRun.RunCommands,
                        Commands = defaultHeli.CommandsToRun.Commands != null
                            ? new List<string>(defaultHeli.CommandsToRun.Commands)
                            : new List<string>()
                    };
                }
            }

            // All other config updates
            if (config.Version < new VersionNumber(1, 2, 34))
            {
                config.options.enableDebug = defaultConfig.options.enableDebug;
            }

            if (config.Version < new VersionNumber(1, 2, 35))
            {
                config.heli.retireOnDeepSeaTP = defaultConfig.heli.retireOnDeepSeaTP;
                config.heli.allowInDeepSea = defaultConfig.heli.allowInDeepSea;
            }

            config.Version = Version;
            SaveConfig();

            Log("Config update complete!", LogLevel.WARN);
        }

        // DEBUG: Config migration methods, remove after a while
        private Dictionary<string, HeliData> MigrateHeliProfilesFromRawJson(string configFilePath, Dictionary<string, HeliData> defaultProfiles)
        {
            var result = new Dictionary<string, HeliData>();

            try
            {
                if (string.IsNullOrEmpty(configFilePath) || !File.Exists(configFilePath))
                {
                    Log("Could not find the Heli Signals config file on disk to migrate.", LogLevel.WARN);
                    return result;
                }

                string json = File.ReadAllText(configFilePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    Log("The Heli Signals config file was empty and could not be migrated.", LogLevel.WARN);
                    return result;
                }

                JObject root = JObject.Parse(json);

                JToken heliToken = root["Patrol Helicopter Options"];

                JObject profilesObject = heliToken?["Profiles"] as JObject;

                if (profilesObject == null)
                {
                    Log("Could not locate 'Patrol Helicopter Options -> Profiles' in the config file.", LogLevel.WARN);
                    return result;
                }

                foreach (JProperty property in profilesObject.Properties())
                {
                    string profileName = property.Name;
                    JObject profileToken = property.Value as JObject;

                    if (profileToken == null)
                    {
                        Log($"Skipped Heli Signal profile '{profileName}' because its config data was invalid.", LogLevel.WARN);
                        continue;
                    }

                    HeliData defaultProfile = GetDefaultHeliProfile(profileName, defaultProfiles);

                    bool usesNewLayout = profileToken["Init & Setup Options"] != null;

                    if (usesNewLayout)
                    {
                        HeliData currentProfile = profileToken.ToObject<HeliData>() ?? new HeliData();
                        EnsureHeliProfileSections(currentProfile, defaultProfile);
                        result[profileName] = currentProfile;
                        continue;
                    }

                    LegacyHeliData legacyProfile = profileToken.ToObject<LegacyHeliData>();
                    HeliData convertedProfile = MapLegacyHeliToNew(legacyProfile, defaultProfile);
                    result[profileName] = convertedProfile;
                }
            }
            catch (Exception ex)
            {
                Log($"Heli Signals config profile migration to 1.3.0 failed: {ex.Message}", LogLevel.ERROR);
            }

            return result;
        }

        private static HeliData GetDefaultHeliProfile(string profileName, Dictionary<string, HeliData> defaultProfiles)
        {
            if (defaultProfiles == null || defaultProfiles.Count == 0)
                return null;

            HeliData defaultProfile;

            // Default profiles receive defaults from their corresponding profile,
            // preserving the different config values between Easy, Medium, Hard, etc.
            if (!string.IsNullOrEmpty(profileName) && defaultProfiles.TryGetValue(profileName, out defaultProfile))
                return defaultProfile;


            // Custom profiles use Easy as their source for genuinely new options,
            // matching the fallback-profile approach used by Bradley Drops.
            if (defaultProfiles.TryGetValue(easyHeli, out defaultProfile))
                return defaultProfile;

            // Final fallback in case the Easy profile was renamed or removed from GetDefaultConfig().
            foreach (KeyValuePair<string, HeliData> entry in defaultProfiles)
            {
                return entry.Value;
            }

            return null;
        }

        private static HeliData MapLegacyHeliToNew(LegacyHeliData oldData, HeliData defaults)
        {
            if (oldData == null)
            {
                var emptyProfile = new HeliData();
                EnsureHeliProfileSections(emptyProfile, defaults);
                return emptyProfile;
            }

            var newData = new HeliData
            {
                Init = new HeliData.InitOptions
                {
                    HeliAmount = oldData.HeliAmount,
                    HeliName = oldData.HeliName,
                    SignalSkinID = oldData.SignalSkinID,
                    GiveItemCommand = oldData.GiveItemCommand,
                    UseBuyCommand = oldData.UseBuyCommand,
                    CostToBuy = oldData.CostToBuy,
                    Health = oldData.Health,
                    MainRotorHealth = oldData.MainRotorHealth,
                    TailRotorHealth = oldData.TailRotorHealth,
                    InitialSpeed = oldData.InitialSpeed,
                    MaxSpeed = oldData.MaxSpeed,
                    OrbitRadius = oldData.OrbitRadius,
                    MaxOrbitDuration = oldData.MaxOrbitDuration,
                    MaxRotationSpeed = oldData.MaxRotationSpeed,
                    TerrainPushForce = oldData.TerrainPushForce,
                    ObstaclePushForce = oldData.ObstaclePushForce,
                    DespawnTime = oldData.DespawnTime
                },

                TargetingDamage = new HeliData.TargetingDamageOptions
                {
                    OwnerDamage = oldData.OwnerDamage,
                    TargetOtherPlayers = oldData.TargetOtherPlayers,
                    BlockOwnerDamage = oldData.BlockOwnerDamage,
                    BlockOtherDamage = oldData.BlockOtherDamage,
                    BlockPlayerDamage = oldData.BlockPlayerDamage,
                    BlockProtectedList = oldData.BlockProtectedList
                },

                Crates = new HeliData.CrateOptions
                {
                    CratesToSpawn = oldData.CratesToSpawn,
                    HeliCrateDespawn = oldData.HeliCrateDespawn,
                    DisableFire = oldData.DisableFire,
                    FireDuration = oldData.FireDuration,
                    LockedCratesToSpawn = oldData.LockedCratesToSpawn,
                    HackSeconds = oldData.HackSeconds,
                    LockedCrateDespawn = oldData.LockedCrateDespawn,
                    ProtectCrates = oldData.ProtectCrates,
                    UnlockCrates = oldData.UnlockCrates
                },

                Weapons = new HeliData.WeaponOptions
                {
                    BulletDamage = oldData.BulletDamage,
                    BulletSpeed = oldData.BulletSpeed,
                    GunFireRate = oldData.GunFireRate,
                    BurstLength = oldData.BurstLength,
                    TimeBetweenBursts = oldData.TimeBetweenBursts,
                    NewTargetRange = oldData.NewTargetRange,
                    MaxTargetRange = oldData.MaxTargetRange,
                    BulletAccuracy = oldData.BulletAccuracy,
                    MaxHeliRockets = oldData.MaxHeliRockets,
                    TimeBetweenRockets = oldData.TimeBetweenRockets,
                    RocketDamageScale = oldData.RocketDamageScale,
                    NapalmChance = oldData.NapalmChance,
                    OrbitStrafeChance = oldData.OrbitStrafeChance,
                    MaxOrbitRockets = oldData.MaxOrbitRockets,
                    MinOrbitMultiplier = oldData.MinOrbitMultiplier,
                    MaxOrbitMultiplier = oldData.MaxOrbitMultiplier,
                    StrafeCooldown = oldData.StrafeCooldown
                },

                Gibs = new HeliData.GibsOptions
                {
                    KillGibs = oldData.KillGibs,
                    GibsHotTime = oldData.GibsHotTime,
                    GibsHealth = oldData.GibsHealth,
                    ProtectGibs = oldData.ProtectGibs,
                    UnlockGibs = oldData.UnlockGibs
                },

                Rewards = new HeliData.RewardOptions
                {
                    RewardPoints = oldData.RewardPoints,
                    XPReward = oldData.XPReward,
                    ScrapReward = oldData.ScrapReward,
                    CustomReward = oldData.CustomReward,
                    DamageThreshold = oldData.DamageThreshold
                },

                BotReSpawn = new HeliData.BotReSpawnOptions
                {
                    BotReSpawnProfile = oldData.BotReSpawnProfile
                },

                Loot = oldData.Loot ?? CloneConfigSection(defaults?.Loot) ?? new HeliData.LootOptions(),

                ExtraLoot = oldData.ExtraLoot ?? CloneConfigSection(defaults?.ExtraLoot) ?? new HeliData.ExtraLootOptions(),

                LockedCrateLoot = oldData.LockedCrateLoot ?? CloneConfigSection(defaults?.LockedCrateLoot) ?? new HeliData.LockedCrateLootOptions(),

                Fireball = CloneConfigSection(defaults?.Fireball) ?? new HeliData.FireballOptions
                {
                    DisableFire = false,
                    MinimumLifeTime = 20f,
                    MaximumLifeTime = 40f,
                    SpreadChance = 0.5f,
                    SpreadAtLifetimePercent = 0.5f,
                    DamagePerSecond = 2f,
                    DamageRate = 0.5f,
                    WaterAmountToExtinguish = 200
                },

                ZoneManager = CloneConfigSection(defaults?.ZoneManager) ?? new HeliData.ZoneManagerOptions
                {
                    UseZoneManager = false,
                    UseHeliZones = false,
                    CanLeaveZone = false,
                    HeliZoneIDs = new List<string>(),
                    UseProtectedZones = false,
                    ProtectedZoneIDs = new List<string>()
                }
            };

            EnsureHeliProfileSections(newData, defaults);

            return newData;
        }

        private static void EnsureHeliProfileSections(HeliData profile, HeliData defaults)
        {
            if (profile == null)
                return;

            profile.Init ??= CloneConfigSection(defaults?.Init) ?? new HeliData.InitOptions();
            profile.TargetingDamage ??= CloneConfigSection(defaults?.TargetingDamage) ?? new HeliData.TargetingDamageOptions();
            profile.Crates ??= CloneConfigSection(defaults?.Crates) ?? new HeliData.CrateOptions();
            profile.Weapons ??= CloneConfigSection(defaults?.Weapons) ?? new HeliData.WeaponOptions();
            profile.Gibs ??= CloneConfigSection(defaults?.Gibs) ?? new HeliData.GibsOptions();
            profile.Fireball ??= CloneConfigSection(defaults?.Fireball) ?? new HeliData.FireballOptions();
            profile.Rewards ??= CloneConfigSection(defaults?.Rewards) ?? new HeliData.RewardOptions();
            profile.BotReSpawn ??= CloneConfigSection(defaults?.BotReSpawn) ?? new HeliData.BotReSpawnOptions();
            profile.ZoneManager ??= CloneConfigSection(defaults?.ZoneManager) ?? new HeliData.ZoneManagerOptions();
            profile.Loot ??= CloneConfigSection(defaults?.Loot) ?? new HeliData.LootOptions();
            profile.ExtraLoot ??= CloneConfigSection(defaults?.ExtraLoot) ?? new HeliData.ExtraLootOptions();
            profile.LockedCrateLoot ??= CloneConfigSection(defaults?.LockedCrateLoot) ?? new HeliData.LockedCrateLootOptions();

            //Protect against partially populated sections in edited or previously generated config files.
            profile.ZoneManager.HeliZoneIDs ??= CloneConfigSection(defaults?.ZoneManager?.HeliZoneIDs) ?? new List<string>();
            profile.ZoneManager.ProtectedZoneIDs ??= CloneConfigSection(defaults?.ZoneManager?.ProtectedZoneIDs) ?? new List<string>();
            profile.Loot.LootTable ??= CloneConfigSection(defaults?.Loot?.LootTable) ?? new List<LootItem>();
            profile.ExtraLoot.LootTable ??= CloneConfigSection(defaults?.ExtraLoot?.LootTable) ?? new List<LootItem>();
            profile.LockedCrateLoot.LootTable ??= CloneConfigSection(defaults?.LockedCrateLoot?.LootTable) ?? new List<LootItem>();
        }

        private static T CloneConfigSection<T>(T value)
        {
            if (value == null)
                return default(T);

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }

        private class LegacyHeliData
        {
            // Init and setup
            [JsonProperty("Number of helicopters called to the player")]
            public int HeliAmount { get; set; }
            [JsonProperty("Helicopter display name")]
            public string HeliName { get; set; }
            [JsonProperty("Skin ID of the custom Supply Signal")]
            public ulong SignalSkinID { get; set; }
            [JsonProperty("Profile shortname (for use in permission and give command)")]
            public string GiveItemCommand { get; set; }
            [JsonProperty("Enable purchasing using custom currency via the buy command")]
            public bool UseBuyCommand { get; set; }
            [JsonProperty("Cost to purchase (using buy command)")]
            public int CostToBuy { get; set; }
            [JsonProperty("Starting health")]
            public float Health { get; set; }
            [JsonProperty("Main rotor health")]
            public float MainRotorHealth { get; set; }
            [JsonProperty("Tail rotor health")]
            public float TailRotorHealth { get; set; }
            [JsonProperty("Initial Helicopter speed until it arrives at location")]
            public float InitialSpeed { get; set; }
            [JsonProperty("Helicopter max speed (Default = 42)")]
            public float MaxSpeed { get; set; }
            [JsonProperty("Distance from target when orbiting (Default = 75)")]
            public float OrbitRadius { get; set; }
            [JsonProperty("Max orbit duration when Helicopter arrives at location (Default = 30)")]
            public float MaxOrbitDuration { get; set; }
            [JsonProperty("Helicopter max rotation speed SCALE (Default = 1.0)")]
            public float MaxRotationSpeed { get; set; }
            [JsonProperty("Terrain pushback force (Help stop faster helis glitch under map)")]
            public float TerrainPushForce { get; set; }
            [JsonProperty("Obstacle pushback force (Help stop faster helis glitch through structures)")]
            public float ObstaclePushForce { get; set; }
            [JsonProperty("Despawn timer")]
            public float DespawnTime { get; set; }

            // Targeting and damage
            [JsonProperty("Only owner can damage (and team if enabled)")]
            public bool OwnerDamage { get; set; }
            [JsonProperty("Allow Helicopter to target other players")]
            public bool TargetOtherPlayers { get; set; }
            [JsonProperty("Block damage to calling players bases")]
            public bool BlockOwnerDamage { get; set; }
            [JsonProperty("Block damage to other players bases")]
            public bool BlockOtherDamage { get; set; }
            [JsonProperty("Block damage to other players")]
            public bool BlockPlayerDamage { get; set; }
            [JsonProperty("Block damage ALWAYS to entities in the protected prefab list")]
            public bool BlockProtectedList { get; set; }

            // Crates
            [JsonProperty("Number of crates to spawn")]
            public int CratesToSpawn { get; set; }
            [JsonProperty("Heli crate despawn time")]
            public float HeliCrateDespawn { get; set; }
            [JsonProperty("Disable fire on crates")]
            public bool DisableFire { get; set; }
            [JsonProperty("Crate fire duration (seconds)")]
            public float FireDuration { get; set; }
            [JsonProperty("Number of locked hackable crates to spawn")]
            public int LockedCratesToSpawn { get; set; }
            [JsonProperty("Hack time for locked crate (seconds)")]
            public float HackSeconds { get; set; }
            [JsonProperty("Locked crate despawn time (seconds)")]
            public float LockedCrateDespawn { get; set; }
            [JsonProperty("Lock looting crates to owner")]
            public bool ProtectCrates { get; set; }
            [JsonProperty("Unlock looting crates to others after time in seconds (0 = Never)")]
            public float UnlockCrates { get; set; }

            // Weapons
            [JsonProperty("Bullet damage (Default = 20)")]
            public float BulletDamage { get; set; }
            [JsonProperty("Bullet speed (Default = 250)")]
            public int BulletSpeed { get; set; }
            [JsonProperty("Gun fire rate (Default = 0.125)")]
            public float GunFireRate { get; set; }
            [JsonProperty("Gun burst length (Default = 3)")]
            public float BurstLength { get; set; }
            [JsonProperty("Time between bursts (Default = 3)")]
            public float TimeBetweenBursts { get; set; }
            [JsonProperty("New target detection range (Default = 150)")]
            public float NewTargetRange { get; set; }
            [JsonProperty("Max targeting range (Default = 300)")]
            public float MaxTargetRange { get; set; }
            [JsonProperty("Weapon accuracy % (1 to 100)")]
            public float BulletAccuracy { get; set; }
            [JsonProperty("Max number of rockets to fire (Default = 12)")]
            public int MaxHeliRockets { get; set; }
            [JsonProperty("Time between rockets (Default = 0.2)")]
            public float TimeBetweenRockets { get; set; }
            [JsonProperty("Rocket damage scale (Default = 1.0)")]
            public float RocketDamageScale { get; set; }
            [JsonProperty("Napalm chance (Default = 0.75)")]
            public float NapalmChance { get; set; }
            [JsonProperty("Orbit Strafe chance (Default = 0.4)")]
            public float OrbitStrafeChance { get; set; }
            [JsonProperty("Number of rockets to fire during orbit strafe (Default = 12)")]
            public int MaxOrbitRockets { get; set; }
            [JsonProperty("Minimum variance to number of rockets fired during orbit strafe (Default = -3)")]
            public int MinOrbitMultiplier { get; set; }
            [JsonProperty("Maximum variance to number of rockets fired during orbit strafe (Default = 24)")]
            public int MaxOrbitMultiplier { get; set; }
            [JsonProperty("Minimum time between strafe attacks")]
            public float StrafeCooldown { get; set; }

            // Gibs
            [JsonProperty("Disable Heli gibs")]
            public bool KillGibs { get; set; }
            [JsonProperty("Gibs too hot to mine time (Seconds)")]
            public float GibsHotTime { get; set; }
            [JsonProperty("Health of gibs (more health = more resources)")]
            public float GibsHealth { get; set; }
            [JsonProperty("Lock mining gibs to owner")]
            public bool ProtectGibs { get; set; }
            [JsonProperty("Unlock mining gibs to others after time in seconds (0 = Never)")]
            public float UnlockGibs { get; set; }

            // Rewards
            [JsonProperty("Reward points issued when destroyed (if enabled)")]
            public double RewardPoints { get; set; }
            [JsonProperty("XP issued when destroyed (if enabled)")]
            public double XPReward { get; set; }
            [JsonProperty("Scrap amount issued when destroyed (if enabled)")]
            public int ScrapReward { get; set; }
            [JsonProperty("Custom reward amount issued when destroyed (if enabled)")]
            public int CustomReward { get; set; }
            [JsonProperty("Damage Threshold (Min damage player needs to contribute to get rewards)")]
            public float DamageThreshold { get; set; }

            // BotReSpawn
            [JsonProperty("BotReSpawn profile to spawn at crash site (leave blank for not using)")]
            public string BotReSpawnProfile { get; set; }

            [JsonProperty("Loot Options")]
            public HeliData.LootOptions Loot { get; set; }
            [JsonProperty("Extra Loot Options")]
            public HeliData.ExtraLootOptions ExtraLoot { get; set; }
            [JsonProperty("Locked Crate Loot Options")]
            public HeliData.LockedCrateLootOptions LockedCrateLoot { get; set; }
        }

        #endregion Config Load/Update

        #region Harmony Patches

        // Harmony patch class for PatrolHelicopterAI.UpdateTargetList method
        public static class PatrolHeliUpdateTargetListPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PatrolHelicopterAI __instance)
            {
                if (__instance == null)
                    return true;

                var heliComp = HeliSignals.GetCachedHeliSignal(__instance);
                if (heliComp != null)
                {
                    heliComp.CustomUpdateTargetList();
                    return false;
                }
                return true;
            }
        }

        // Harmony patch class for PatrolHelicopterAI.FireRocket method
        public static class PatrolHeliFireRocketPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PatrolHelicopterAI __instance)
            {
                if (__instance == null)
                    return true;

                var heliComp = HeliSignals.GetCachedHeliSignal(__instance);
                if (heliComp != null)
                {
                    heliComp.CustomFireRocket();
                    return false;
                }
                return true;
            }
        }

        // Harmony patch class for BaseEntity.UpdateNetworkGroup method
        // Fixes heli visibility in DeepSea vs MainIsland
        public static class UpdateNetworkGroupPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(BaseEntity __instance)
            {
                if (__instance == null || __instance.net == null)
                    return true;

                var heli = __instance as PatrolHelicopter;
                var heliAi = heli?.myAI;
                if (heliAi == null)
                    return true;

                var heliComp = HeliSignals.GetCachedHeliSignal(heliAi);
                if (heliComp != null)
                {
                    Vector3 pos = heli.transform.position;

                    Network.Visibility.Group desired =
                        DeepSeaManager.IsInsideDeepSea(pos)
                            ? BaseNetworkable.DeepSeaGroup
                            : BaseNetworkable.MainIslandGroup;

                    if (!ReferenceEquals(heli.net.group, desired))
                        heli.net.SwitchGroup(desired);

                    return false;
                }
                return true;
            }
        }

        // Harmony patch class for HackableLockedCrate.RefreshDecay method
        public static class HackableLockedCrateRefreshDecayPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(HackableLockedCrate __instance)
            {
                if (__instance == null || __instance.IsDestroyed || __instance.net == null || Instance == null)
                    return true;

                ulong crateId = __instance.net.ID.Value;

                // Not a Heli Signal crate, allow native RefreshDecay
                if (!LockedCrates.TryGetValue(crateId, out LockedCrateData crateData) || crateData == null)
                    return true;

                // If it no longer has custom skinId, stop managing it and allow native logic
                if (!HeliProfileCache.ContainsKey(__instance.skinID))
                {
                    LockedCrates.Remove(crateId);
                    return true;
                }

                // Resolve the config and schedule custom decay. Returning false
                // suppresses native RefreshDecay only for this plugin-owned crate.
                if (!Instance.TryRefreshLockedCrateDecay(__instance, crateData))
                    return true;

                return false;
            }
        }
        
        #endregion Harmony Patches
    }
}  