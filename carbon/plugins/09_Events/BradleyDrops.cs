using Facepunch;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Rust;
using Rust.Ai;
using Rust.Ai.Gen2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using Time = UnityEngine.Time;
using Random = UnityEngine.Random;

#region Changelog

/* ToDo:

 - Option to specify specific or random Bradley profile applied to vanilla spawned event - ? possibly

 - Add API to allow other devs to call BD to a position
    - API_CallBradleyDropToPosition(BasePlayer player, Vector3 position)

 - Haven't forgotten, totally did, will get aroud to it, at some point!

*/

/* Changelog

1.4.1:
 - Fixed: For 1st Oct 2026 force wipe update

1.4.0:
 - Fixed: Bug where modified SignalCooldown plugin still conflicted
 - Added: Config option for running commands when bradley is killed
 - Added: HackableCrate extra loot config options

1.3.16:
 - Fixed: Bug where extra loot item number was ignored

1.3.15
 - Fixed: "MissingMethodException: Method 'LockedByEntCrate.SetLockingEnt' not found." error

1.3.14
 - Fixed: For 3rd Sep 2026 Rust update
 - Fixed: HelicopterDebris.tooHotUntil removed and changed to new HelicopterDebris.OnCooledDown()

1.3.13
 - Fixed: Removed debug code left over by mistake
 - Fixed: Updated obsolete BaseEntity.SetFlag to BaseEntity.SetFlagLocal
 - Fixed: Updated other deprecated code
 - Fixed: Item.Text not setting on crate loot when specified in the config
 - Fixed: CH47 spawned hackable crate incorrectly using Bradley spawned hackable crate hack time
 - Added: CanSpawnBradleyDropCrates hook
 - Added: CanSpawnBradleyDropLockedCrates hook
 - Added: OnBradleyDropApcKill hook
 - Added: OnBradleyDropCH47Kill hook
 - Added: Config options to disable the map marker on hackable crates

1.3.12
 - Fixed: Occasional NRE during GiveDeployedKit
 - Fixed: Stopped players putting Bradley signals in Tin Can Alarm
 - Fixed: Hackable crates becoming imortal and never decaying
 - Fixed: Bradley Scientists could become immortal in certain conditions
 - Added: Skills plugin XP config option
 - Added: Allow on foundation config option (Bradley arena compatibility)
 - Added: Custom reward item config field for "item.text" (by request)
 - Added: Loot item config field for "item.text" (by request)
 - Added: Config options 'Build Blocking' for creating no build zones around Bradleys or Drop Zones
 - Added: New perm 'bradleydrops.bypassbuildingdamage', bypasses all damage from Bradley to that players entities

1.3.11
 - Fixed: Bug where auto turrets could not damage Bradley NPCs when allowed true

1.3.10
 - Fixed: Bug that made HackableLockedCrate never despawn
 - Fixed: Bug where using buy command for wave profiles caused NRE

1.3.9
 - Fixed: For 2nd July Rust Update 

1.3.8
 - Fixed: Bug where giving XP was sometimes unset after server restart
 - Fixed: Bug where despawn command would not work
 - Fixed: Bug that caused Imortal NPC now fixed
 - Fixed: For 2nd July Rust Update 
 
1.3.7
 - Fixed: Removed debugging code which spammed console, sorry!

1.3.6
 - Fixed: For March 5th force update
 - Added: Config option: "Allow Players to Call Bradleys in Deep Sea": true/false
    - Note: Currently only cargo plane/balloon delivery works in Deep Sea (CH47 will not work)

1.3.5
 - Updated: For Feb 5th Naval Rust update
 - Updated: Removed all LINQ usage to make Grimm530 happy :D
 - Fixed: Typo in lootContainer.inventory.capacity =+ count; (corrected to +=)
 - Fixed: 'InvalidOperationException: Nullable object must have a value.' error (Hostil drone issue)
 - Fixed: Hook conflict with NpcSpawn
 - Fixed: Bradley now stops targeting previously hostile drones after they are deactivated
 - Added: New API method 'GetBradleyDropData' to return basic BradleyDrop data

 */

 #endregion Changelog

namespace Oxide.Plugins
{
    [Info("Bradley Drops", "ZEODE", "1.4.1")]
    [Description("Call a Bradley APC to your location with custom supply signals.")]
    public class BradleyDrops: RustPlugin
    {
        #region Plugin References
        
        [PluginReference] private Plugin 
            Friends, 
            Clans, 
            Kits, 
            ServerRewards, 
            Economics, 
            Vanish, 
            NoEscape, 
            BotReSpawn, 
            XPerience, 
            SkillTree, 
            Skills, 
            ZoneManager, 
            NpcSpawn;
        
        #endregion Plugin References

        #region Constants

        private static BradleyDrops Instance;

        private Harmony harmony;
        private MethodInfo _doWeaponAimingMethod;
        private MethodInfo _doWeaponsMethod;
        private MethodInfo _killSpawnedScientistsMethod;
        private MethodInfo _calculateDesiredAltitudeMethod;
        private MethodInfo _refreshDecayMethod;
        private const string HarmonyPatchId = "BradleyDrops.Internal";
		private static Dictionary<BradleyAPC, BradleyDrop> bradCompCache = new Dictionary<BradleyAPC, BradleyDrop>();
        private static Dictionary<CH47HelicopterAIController, CH47DropComponent> ch47CompCache = new Dictionary<CH47HelicopterAIController, CH47DropComponent>();

        private static readonly FieldInfo MapMarkerInstanceField = AccessTools.Field(typeof(HackableLockedCrate), "mapMarkerInstance");

        private static System.Random random = new System.Random();
		// Permissions
        private const string permAdmin = "bradleydrops.admin";
        private const string permBuy = "bradleydrops.buy";
        private const string permBypasscooldown = "bradleydrops.bypasscooldown";
        private const string permBypassBuildingDamage = "bradleydrops.bypassbuildingdamage";
		// Prefabs
        private const string bradleyPrefab = "assets/prefabs/npc/m2bradley/bradleyapc.prefab";
        private const string balloonPrefab = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab";
        private const string ch47Prefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string planePrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private const string bradleyExplosion = "assets/prefabs/npc/m2bradley/effects/bradley_explosion.prefab";
        private const string bradleyCrate = "assets/prefabs/npc/m2bradley/bradley_crate.prefab";
        private const string hackableCrate = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        private const string shockSound = "assets/prefabs/locks/keypad/effects/lock.code.shock.prefab";
        private const string deniedSound = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
        private const string balloonExplosion = "assets/content/vehicles/minicopter/effects/mincopter_explosion.prefab";
        private const string woodGibSound = "assets/bundled/prefabs/fx/building/wood_gib.prefab";
        private const string stoneGibSound = "assets/bundled/prefabs/fx/building/stone_gib.prefab";
        private const string metalGibSound = "assets/bundled/prefabs/fx/building/metal_sheet_gib.prefab";
        private const string heliFlares = "assets/content/vehicles/attackhelicopter/effects/pfx_flares_attackhelicopter.prefab";
        private const string grenadePrefab = "assets/prefabs/ammo/40mmgrenade/40mm_grenade_smoke.prefab";
        private const string mpgLauncherFx = "assets/prefabs/weapons/grenade launcher/effects/attack.prefab";
		// URLs
        private const string defaultWebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";
        private const string steamProfileUrl = "https://steamcommunity.com/profiles/";
        private const string bradleyAvatar = "https://zeode.io/images/BradleyBot.png";
        private const string zeodeFooterUrl = "https://zeode.io";
		// Item IDs
        private const int supplySignalId = 1397052267;
        private const int scrapId = -932201673;
		// Default config skin IDs
        private const ulong easySkinID = 2905355269;
        private const ulong medSkinID = 2905355312;
        private const ulong hardSkinID = 2905355296;
        private const ulong eliteSkinID = 2911864795;
        private const ulong expertSkinID = 3361673979;
        private const ulong nightmareSkinID = 3361674045;
        // Wave skin IDs
        private const ulong normalWaveSkinID = 3502926194;
        private const ulong hardWaveSkinID = 3502926112;
        // Default config item names
        private const string easyDrop = "Bradley Drop (Easy)";
        private const string medDrop = "Bradley Drop (Medium)";
        private const string hardDrop = "Bradley Drop (Hard)";
        private const string eliteDrop = "Bradley Drop (Elite)";
        private const string expertDrop = "Bradley Drop (Expert)";
        private const string nightmareDrop = "Bradley Drop (Nightmare)";
        // Wave item names
        private const string normalWave = "Bradley Drop Wave (Normal)";
        private const string hardWave = "Bradley Drop Wave (Hard)";
        // Ignored landing colliders
        public List<string> ignoredColliders = new List<string>{ "clutter", "collectable", "driftwood", "forwardhurttrigger", "lowercrushtrigger" };
        // Layers
        public const int layerHeliGibs = Layers.Mask.Ragdoll | Layers.Mask.Default;
        
        #endregion Constants

        #region Language & Log
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SyntaxPlayer"] = "<color=red>Invalid syntax</color>, use:\n\n<color=green>/bdgive <type> <SteamID/PlayerName> <amount></color>",
                ["SyntaxConsole"] = "Invalid syntax, use: bdgive <type> <SteamID/PlayerName> <amount>",
                ["ClearSyntaxPlayer"] = "<color=red>Invalid syntax</color>, use:\n\n<color=green>/bdclearcd</color> (clears all cooldowns)\n\nOr\n\n<color=green>/bdclearcd <SteamID/PlayerName></color>",
                ["ClearSyntaxConsole"] = "Invalid syntax, use: \"bdclearcd\" (clears all cooldowns) or \"bdclearcd <SteamID/PlayerName>\"",
                ["Receive"] = "You received <color=orange>{0}</color> x <color=orange>{1}</color>!",
                ["PlayerReceive"] = "Player {0} ({1}) received {2} x {3}!",
                ["Permission"] = "You do not have permission to use <color=orange>{0}</color>!",
                ["CooldownTime"] = "Cooldown active! You can call another in {0}!",
                ["TeamCooldownTime"] = "Team cooldown active! You can call another in {0}!",
                ["GlobalLimit"] = "Global limit of {0} active Bradley APCs is reached, please try again later",
                ["PlayerLimit"] = "Player limit of {0} active Bradley APCs is reached, please try again later",
                ["NotInsideDeepSea"] = "You may not call a <color=orange>{0}</color> while in the Deep Sea!",
                ["NotAdmin"] = "You do not have permission to use that command!",
                ["PlayerNotFound"] = "Can't find a player with the name or ID: {0}",
                ["InGameOnly"] = "Error: This command is only for use in game!",
                ["PlayerDead"] = "Player with name or ID {0} is dead, try again when they have respawned",
                ["InNamedMonument"] = "You cannot call <color=orange>{0}</color> in or near <color=red>{1}</color>, signal refunded, check inventory.",
                ["NotOnGround"] = "<color=orange>{0}</color> must be thrown onto the floor, signal refunded, check inventory.",
                ["NotInZone"] = "<color=orange>{0}</color> must be thrown into a Bradley Drop zone, signal refunded, check inventory.",
                ["InMonument"] = "You cannot call <color=orange>{0}</color> in a <color=red>monument</color>, signal refunded, check inventory.",
                ["TooCloseToWater"] = "<color=orange>{0}</color> was thrown too close to <color=blue>water</color> and was refunded, check inventory.",
                ["IntoWater"] = "<color=orange>{0}</color> went into deep <color=blue>water</color> and was destroyed.",
                ["InSafeZone"] = "<color=orange>{0}</color> was thrown in a <color=green>Safe Zone</color> and was refunded, check inventory.",
                ["IntoSafeZone"] = "<color=orange>{0}</color> moved into a <color=green>Safe Zone</color> and was destroyed.",
                ["BuildingPriv"] = "<color=orange>{0}</color> was thrown too close to a players base and was refunded, check inventory.",
                ["NearCollider"] = "<color=orange>{0}</color> was thrown too close to a object, please throw in more open ground.",
                ["LZCleared"] = "Entities placed near the landing zone of <color=orange>{0}</color> have been destroyed.",
                ["Inside"] = "<color=orange>{0}</color> was thrown inside and was refunded, check inventory.",
                ["InvalidDrop"] = "Drop type \"{0}\" not recognised, please check and try again!",
                ["CannotLoot"] = "You cannot loot this because it is not yours!",
                ["CannotHack"] = "You cannot hack this because it is not yours!",
                ["CannotMine"] = "You cannot mine this because it is not yours!",
                ["BuildBlocked"] = "You cannot place that near an active Bradley Drop!",
                ["BuyCmdSyntax"] = "Buy Command Usage (prefix / for chat):\n\n{0}{1}", 
                ["NoBuy"] = "Buy Command for <color=orange>{0}</color> Is Not Enabled!",
                ["BuyPermission"] = "You do not have permission to buy Bradley Drop \"<color=orange>{0}</color>\".",
                ["PriceList"] = "Bradley Drop Prices:\n\n{0}",
                ["BradleyKilledTime"] = "<color=orange>{0}</color> killed by <color=green>{1}</color> in grid <color=green>{2}</color> (Time Taken: {3})",
                ["BradleyCalled"] = "<color=green>{0}</color> just called in a <color=orange>{1}</color> to their location in grid <color=green>{2}</color>",
                ["XPGiven"] = "<color=green>{0} XP</color> received for destroying <color=orange>{1}</color>!",
                ["RewardGiven"] = "<color=green>{0} {1}</color> points received for destroying <color=orange>{2}</color>!",
                ["ScrapGiven"] = "<color=green>{0}</color> Scrap received for destroying <color=orange>{1}</color>!",
                ["CannotDamage"] = "You <color=red>Cannot</color> damage this <color=orange>{0}</color>!",
                ["NoTurret"] = "You <color=red>Cannot</color> damage this <color=orange>{0}</color> with remote turrets!",
                ["TooFarAway"] = "You are <color=red>Too Far</color> away to engage this <color=orange>{0}</color> ({1} m)",
                ["CantAfford"] = "You <color=red>Cannot</color> afford this! Cost: <color=orange>{0}</color> Required: <color=orange>{1}</color>",
                ["FullInventory"] = "<color=green>{0}</color> <color=red>NOT</color> given! No inventory space!",
                ["NotLanded"] = "You <color=red>Cannot</color> damage <color=orange>{0}</color> until it's landed!",
                ["ApcReportTitle"] = "There are currently <color=orange>{0}</color> active dropped Bradley APCs",
                ["ApcReportItem"] = "<size=9><color=orange>{0}/{1}</color> - <color=green>{2}</color> (Owner: <color=orange>{3}</color> Grid: <color=orange>{4}</color> Health: <color=orange>{5}</color>)\n</size>",
                ["ApcReportTitleCon"] = "There are currently {0} active dropped Bradley APCs",
                ["ApcReportItemCon"] = "{0}/{1} - {2} (Owner: {3} Grid: {4} Health: {5})\n",
                ["ApcReportList"] = "{0}",
                ["DespawnApc"] = "<color=orange>{0}</color> is despawning, you were warned! <color=red>{1}</color>/<color=red>{2}</color>",
                ["DespawnWarn"] = "<color=red>Damage Blocked!</color> You may only attack from a base with TC auth. If you continue, the <color=orange>{0}</color> will despawn. Warning Level: <color=red>{1}</color>/<color=red>{2}</color>",
                ["DamageReport"] = "<color=orange>Damage Report</color>\n\n{0}",
                ["DamageReportIndex"] = "<size=11>{0}. <color=green>{1}</color> -> {2} HP\n</size>",
                ["DiscordCall"] = "**{0}** just called a **{1}** to their location in grid **{2}**",
                ["DiscordKill"] = "**{0}** just destroyed a **{1}** in grid **{2}**",
                ["DiscordDespawn"] = "**{0}** called by **{1}** just despawned at grid **{2}**",
                ["DespawnedBradleys"] = "You have retired ALL your (your teams) called Bradley APCs",
                ["NoDespawnedBradleys"] = "You have no active Bradley APCs to despawn",
                ["CooldownsCleared"] = "All player cooldowns have been cleared!",
                ["PlayerCooldownCleared"] = "Cooldown cleared for player {0} ({1})",
                ["PlayerNoCooldown"] = "No active cooldown for player {0} ({1})",
                ["WaveProfileError"] = "There is an error in the plugin config <color=orange>{0}</color>, please report to an Admin.",
                ["FirstApcCalled"] = "Stand by, <color=red>{0}</color> on route to your location!",
                ["NextApcInbound"] = "Look sharp, a <color=red>{0}</color> is closing in on the LZ!",
                ["NextApcCalledDelayed"] = "<color=green>{0}</color> destroyed! Stand by, a <color=red>{1}</color> is enroute to the LZ, ETA {2} minutes!",
                ["WaveFinished"] = "<color=green>{0}</color> destroyed! <color=red>Hostile forces have no more armoured assets to send to your location.</color> Well Done!",
                ["BradleyWaveRetired"] = "Bradley wave has been retired!",
                ["DiscordEmbedTitleCalled"] = "🛡️  {0} Called  🛡️",
                ["DiscordEmbedTitleKilled"] = "💀  {0} Destroyed  💀",
                ["DiscordEmbedTitleRetire"] = "🚫  {0} Retired  🚫",
                ["DiscordEmbedOwner"] = "Owner: {0} SteamID: {1}",
                ["DiscordEmbedFooter"] = $"Bradley Drops by ZEODE",
                ["DiscordCalledLocation"] = "Location 📍",
                ["DiscordCalledHealth"] = "Health ❤️",
                ["DiscordCalledDespawn"] = "Despawn Time 🕐",
                ["DiscordKilledLocation"] = "Location 💥",
                ["DiscordKilledTime"] = "Time Taken ⏱️",
                ["DiscordKilledKiller"] = "Killer ⚔️",
                ["DiscordKilledLeaderboard"] = "Leaderboard 🏆",
                ["DiscordRetireLocation"] = "Location 📍",
                ["DiscordRetireTime"] = "Retired After ⏱️",
                ["DiscordRetirePlayer"] = "Retired By 🧑",
                ["DiscordRetireHealth"] = "Health ❤️",
                ["DiscordRetireReport"] = "Damage Report 📋",
                ["DiscordEmbedDamageReportIndex"] = "{0}. {1} Damage Total: {2} HP\n",
                ["DiscordRetireNoReport"] = "No Damage",
                ["DiscordEmbedTitleKilledWave"] = "💀  {0} Destroyed (Wave: {1} of {2})  💀",
                ["DiscordWaveBradleys"] = "Wave Bradleys 🌊",
                ["DiscordEmbedTitleWaveComplete"] = "🏁  {0} Complete  🏁",
                ["DiscordWaveCompleteStatus"] = "Wave Status 🌊",
                ["DiscordWaveCompleteBradleys"] = "Wave Bradleys 🛡️",
                ["DiscordWaveCompleteTotal"] = "Total Destroyed 💀",
                ["DiscordWaveCompleteAllDestroyed"] = "All Bradleys Destroyed",
                ["DiscordEmbedTitleWaveRetired"] = "🚫  {0} Retired  🚫",
                ["DiscordWaveRetiredStatus"] = "Wave Status 🌊",    
                ["DiscordWaveRetiredBy"] = "Retired by: [{0}]({1})",
                ["DiscordWaveRetiredTimeout"] = "Ran Out Of Time",
                ["DiscordWaveRetiredBradleys"] = "Wave Bradleys 🛡️"
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
            Player.Message(player, message, config.options.usePrefix ? config.options.chatPrefix : null, config.options.chatIcon);

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
            if (level == LogLevel.DEBUG && !config.options.debug)
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
            permission.RegisterPermission(permBypassBuildingDamage, this);
            config.rewards.RegisterPermissions(permission, this);
            config.bradley.RegisterPermissions(permission, this);

            AddCovalenceCommand(config.options.reportCommand, nameof(CmdReport));
            AddCovalenceCommand(config.purchasing.buyCommand, nameof(CmdBuyDrop));
            AddCovalenceCommand(config.bradley.despawnCommand, nameof(CmdDespawnApc));
            AddCovalenceCommand("bdgive", nameof(CmdGiveDrop));
            AddCovalenceCommand("bdclearcd", nameof(CmdClearCooldown));
        }

        private void OnServerInitialized()
        {
            ApplyHarmonyPatches();

            CheckRewardPlugins();
            
            NextTick(()=>
            {
                LoadProfileCache();

                if (!config.options.useStacking)
                {
                    Unsubscribe(nameof(CanStackItem));
                    Unsubscribe(nameof(CanCombineDroppedItem));
                }

                if (config.options.noVanillaApc)
                {
                    ConVar.Bradley.enabled = false;
                    Log($"INFO: Vanilla Bradley APC server event at Launch Site is disabled", LogLevel.INFO);
                }

                foreach (var monument in TerrainMeta.Path.Monuments)
                {
                    if (monument.shouldDisplayOnMap)
                        Monuments.Add(monument);
                }
                if (Monuments.Count == 0)
                {
                    Log($"WARNING: No monument info found. Config options regarding calling Bradleys at Monuments will not function.", LogLevel.WARN);
                }
            });
        }

        private void Unload()
        {
            if (bradCompCache != null && bradCompCache.Count > 0)
            {
                var bradCompCacheCopy = new List<KeyValuePair<BradleyAPC, BradleyDrop>>(bradCompCache);
                foreach (var kvp in bradCompCacheCopy)
                {
                    var component = kvp.Value;
                    if (component != null)
                    {
                        component.enabled = false;
                        component.CancelInvoke();
                        component.StopAllCoroutines();
                    }
                }
            }

            var bradleyDataCopy = new Dictionary<ulong, ApcStats>(BradleyDropData);
            var ch47ListCopy = new List<CH47Helicopter>(CH47List);
            var cargoPlaneListCopy = new List<CargoPlane>(CargoPlaneList);

            BradleyDropData.Clear();
            CH47List.Clear();
            CargoPlaneList.Clear();
            bradCompCache.Clear();
            ch47CompCache.Clear();

            foreach (var netId in bradleyDataCopy.Keys)
            {
                var bradley = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BradleyAPC;
                if (bradley != null && !bradley.IsDestroyed)
                {
                    try
                    {
                        var component = bradley.GetComponent<BradleyDrop>();
                        if (component != null)
                        {
                            component.enabled = false;
                            UnityEngine.Object.DestroyImmediate(component);
                        }
                        bradley.Kill(BaseNetworkable.DestroyMode.None);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error cleaning up Bradley: {ex.Message}", LogLevel.ERROR);
                    }
                }
            }

            foreach (var ch47 in ch47ListCopy)
            {
                if (ch47 != null && !ch47.IsDestroyed)
                {
                    try
                    {
                        var component = ch47.GetComponent<CH47DropComponent>();
                        if (component != null)
                        {
                            component.enabled = false;
                            component.StopAllCoroutines();
                            UnityEngine.Object.DestroyImmediate(component);
                        }

                        var aiController = ch47.GetComponent<CH47HelicopterAIController>();
                        if (aiController != null)
                        {
                            aiController.DismountAllPlayers();
                            aiController.enabled = false;
                            aiController.CancelInvoke();
                            aiController.ClearLandingTarget();

                            var brain = aiController.GetComponent<CH47AIBrain>();
                            if (brain != null)
                            {
                                brain.SetEnabled(false);
                            }
                        }
                        ch47.Kill(BaseNetworkable.DestroyMode.None);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error cleaning up CH47: {ex.Message}", LogLevel.ERROR);
                        try
                        {
                            ch47.Kill(BaseNetworkable.DestroyMode.None);
                        }
                        catch { /* Silently catch and continue */ }
                    }
                }
            }

            foreach (var plane in cargoPlaneListCopy)
            {
                if (plane != null && !plane.IsDestroyed)
                {
                    try
                    {
                        var component = plane.GetComponent<BradleyDropPlane>();
                        if (component != null)
                        {
                            component.enabled = false;
                            UnityEngine.Object.DestroyImmediate(component);
                        }
                        
                        plane.Kill(BaseNetworkable.DestroyMode.None);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error cleaning up CargoPlane: {ex.Message}", LogLevel.ERROR);
                    }
                }
            }

            if (config.options.noVanillaApc)
            {
                ConVar.Bradley.enabled = true;
                Log($"Vanilla Bradley APC event at Launch Site has been re-enabled", LogLevel.INFO);
            }

            PlayerCooldowns.Clear();
            TierCooldowns.Clear();
            LockedCrates.Clear();
            ApcProfiles.Clear();
            WaveProfiles.Clear();
            Monuments.Clear();
            BradleyProfileCache.Clear();
            
            try
            {
                if (harmony != null)
                {
                    if (_doWeaponAimingMethod != null)
                        harmony.Unpatch(_doWeaponAimingMethod, HarmonyPatchType.Prefix, HarmonyPatchId);

                    if (_doWeaponsMethod != null)
                        harmony.Unpatch(_doWeaponsMethod, HarmonyPatchType.Prefix, HarmonyPatchId);
                    
                    if (_killSpawnedScientistsMethod != null)
                        harmony.Unpatch(_killSpawnedScientistsMethod, HarmonyPatchType.Prefix, HarmonyPatchId);

                    if (_calculateDesiredAltitudeMethod != null)
                        harmony.Unpatch(_calculateDesiredAltitudeMethod, HarmonyPatchType.Prefix, HarmonyPatchId);

                    if (_refreshDecayMethod != null)
                        harmony.Unpatch(_refreshDecayMethod, HarmonyPatchType.Prefix, HarmonyPatchId);
                    
                    Log("INFO: Harmony patches removed and cache cleared successfully!", LogLevel.WARN);
                }
            }
            catch (System.Exception ex)
            {
                Log($"ERROR Cleaning up Harmony patches!", LogLevel.ERROR);
                Log($"Exception: {ex.Message}", LogLevel.ERROR);
            }
            Instance = null;
        }

        #endregion Plugin Load/Unload

        #region Hooks

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            // Prevent people putting bradley drop signals in Tin Can Alarm
            if (container?.entityOwner is not TinCanAlarm tinCanAlarm)
                return null;

            if (BradleyProfileCache.ContainsKey(item.skin))
                return ItemContainer.CanAcceptResult.CannotAccept;

            return null;
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item == null)
                return;
            
            if (item.info?.itemid == supplySignalId)
                NextTick(()=> CheckAndFixSignal(item));
        }

        private object OnExplosiveThrown(BasePlayer player, SupplySignal entity, ThrownWeapon item)
        {
            if (item == null || entity == null)
                return null;
            
            var signal = item.GetItem();
            if (signal == null)
                return null;
            
            if (BradleyProfileCache.ContainsKey(signal.skin))
            {
                entity.EntityToCreate = null;
                entity.CancelInvoke(entity.Explode);
                entity.skinID = signal.skin;
                BradleySignalThrown(player, entity, signal);
            }
            return null;
        }
        private void OnExplosiveDropped(BasePlayer player, SupplySignal entity, ThrownWeapon item) => OnExplosiveThrown(player, entity, item);

        private object CanStackItem(Item item, Item targetItem)
        {
            if (item == null || targetItem == null)
                return null;
            
            if (BradleyProfileCache.ContainsKey(item.skin))
            {
                if (item.info.itemid == targetItem.info.itemid && item.skin != targetItem.skin)
                    return false;
            }
            return null;
        }

        private object CanCombineDroppedItem(DroppedItem droppedItem, DroppedItem targetItem)
        {
            if (droppedItem == null || targetItem == null)
                return null;
            
            if (BradleyProfileCache.ContainsKey(droppedItem.item.skin))
            {
                if (droppedItem.item.info.itemid == targetItem.item.info.itemid && droppedItem.item.skin != targetItem.item.skin)
                    return true;
            }
            return null;
        }

        private object CanBuild(Planner planner, Construction construction, Construction.Target target)
        {
            if (config?.bradley?.buildBlocking?.EnableBuildBlockZone != true)
                return null;

            BasePlayer player = planner?.GetOwnerPlayer();

            if (player == null || construction == null)
                return null;

            string prefabPath = construction.fullName;

            if (IsBuildBlockPrefabAllowed(prefabPath))
                return null;

            Vector3 buildPosition = target.position;

            if (!IsInsideBradleyBuildBlockZone(buildPosition))
                return null;

            Message(player, "BuildBlocked");

            return false;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null)
                return;

            if (entity.ShortPrefabName.Equals("oilfireballsmall"))
            {
                // Setting the owner ID & name of fire on Bradley crates to stop NRE from
                // OnEntityTakeDamage since LockedEnt crate fires arent assigned this.
                NextTick(()=>
                {
                    BaseEntity parentEnt = entity.GetParentEntity();
                    if (parentEnt == null)
                        return;

                    if (!BradleyProfileCache.ContainsKey(parentEnt.skinID))
                        return;
                    
                    FireBall fireball = entity as FireBall;
                    if (fireball == null || fireball.creatorEntity == null)
                        return;

                    fireball.OwnerID = parentEnt.OwnerID;
                    fireball._name = parentEnt._name;
                    fireball.skinID = parentEnt.skinID;
                });
            }
        }

        private object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (info?.Initiator == null || player == null || !player.userID.IsSteamId())
                return null;

            var initiator = info.Initiator;
            
            if (!BradleyProfileCache.TryGetValue(initiator.skinID, out string apcProfile))
                return null;
            
            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return null;
            
            if (apcData.TargetingDamage.BlockPlayerDamage && !IsOwnerOrFriend(initiator.OwnerID, player.userID))
            {
                info.damageTypes.Clear();
                return true;
            }
            
            var majorityDamage = info.damageTypes.GetMajorityDamageType();
            
            if (majorityDamage == DamageType.Bullet)
            {
                info.damageTypes.ScaleAll(apcData.Weapons.GunDamage);
            }
            else if (majorityDamage == DamageType.Blunt)
            {
                info.damageTypes.ScaleAll(apcData.Weapons.ShellDamageScale);
            }
            
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.Initiator == null || entity == null || !entity.OwnerID.IsSteamId())
                return null;

            var initiator = info.Initiator;
            
            if (!BradleyProfileCache.TryGetValue(initiator.skinID, out string apcProfile))
                return null;
            
            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return null;
            
            if (apcData.TargetingDamage.BlockProtectedList && config.bradley.protectedPrefabs.Contains(entity.name))
            {
                info.damageTypes.Clear();
                return true;
            }

            BradleyAPC bradley = initiator as BradleyAPC;
            if (bradley == null)
                return null;
            
            if (permission.UserHasPermission(entity.OwnerID.ToString(), permBypassBuildingDamage) && bradley.OwnerID == entity.OwnerID)
            {
                info.damageTypes.Clear();
                return true;
            }

            bool isOwnerOrFriend = IsOwnerOrFriend(bradley.OwnerID, entity.OwnerID);

            if (apcData.TargetingDamage.BlockOtherDamage && !isOwnerOrFriend && !IsDamageAuthorized(bradley, entity))
            {
                info.damageTypes.Clear();
                return true;
            }
            
            if (apcData.TargetingDamage.BlockOwnerDamage && isOwnerOrFriend)
            {
                info.damageTypes.Clear();
                return true;
            }
            
            var majorityDamage = info.damageTypes.GetMajorityDamageType();
            
            if (majorityDamage == DamageType.Bullet)
            {
                info.damageTypes.ScaleAll(apcData.Weapons.GunBuildDamage);
            }
            else if (majorityDamage == DamageType.Blunt)
            {
                info.damageTypes.ScaleAll(apcData.Weapons.ShellBuildDamageScale);
            }
            
            return null;
        }

        private object OnEntityTakeDamage(BradleyAPC bradley, HitInfo info)
        {
            var initiator = info?.Initiator;
            if (bradley == null || initiator == null || initiator == bradley)
                return null;

            string apcProfile = ResolveApcProfile(bradley);
            if (apcProfile == null && bradley.skinID != 0)
                return null;

            bool isVanilla = IsVanillaBradley(bradley, apcProfile);
            if (isVanilla && !config.announce.reportVanilla)
                return null;

            string apcName = GetBradleyDisplayName(apcProfile, isVanilla);
            ulong bradleyId = bradley.net.ID.Value;
            ulong ownerId = bradley.OwnerID;

            if (!BradleyDropData.TryGetValue(bradleyId, out var dropData))
            {
                BradleyDrop bradComp = bradley.GetComponent<BradleyDrop>();
                if (bradComp == null && !isVanilla)
                    return null;

                dropData = new ApcStats
                {
                    Owner = bradComp?.owner,
                    OwnerID = ownerId,
                    OwnerName = isVanilla ? config.announce.vanillaOwner : bradComp.owner?.displayName
                };
                BradleyDropData[bradleyId] = dropData;
            }

            if (!TryGetAttacker(info, initiator, apcName, out var attacker, out var attackerId, out var turret, out var blockDamage))
                return blockDamage;

            if (!dropData.Attackers.ContainsKey(attackerId))
                dropData.Attackers[attackerId] = new AttackersStats { Name = attacker.displayName };

            if (!isVanilla)
            {
                var bradComp = bradley.GetComponent<BradleyDrop>();
                if (!ValidateDamagePermissions(bradComp, attacker, initiator, info, apcName, apcProfile))
                    return true;

                if (initiator is AutoTurret)
                    HandleTurretDamage(dropData, turret, bradComp, info);
                else
                    dropData.PlayerDamage += info.damageTypes.Total();

                if (info.damageTypes.Total() > bradley._health)
                    bradComp.isDying = true;
            }

            if (config.bradley.DespawnWarning && attacker.IsBuildingBlocked() && IsOwnerOrFriend(attackerId, ownerId))
            {
                dropData.WarningLevel++;
                var comp = bradley.GetComponent<BradleyDrop>();

                if (!comp.isDespawning && dropData.WarningLevel >= config.bradley.WarningThreshold)
                {
                    comp.isDespawning = true;
                    info.damageTypes.Clear();
                    DespawnAPC(bradley, apcProfile);
                    Message(attacker, "DespawnApc", apcName, dropData.WarningLevel, config.bradley.WarningThreshold);
                    return true;
                }

                info.damageTypes.Clear();
                Message(attacker, "DespawnWarn", apcName, dropData.WarningLevel, config.bradley.WarningThreshold);
                return true;
            }

            dropData.LastAttacker = attacker;
            float damage = Mathf.Min(info.damageTypes.Total(), bradley._health);
            dropData.Attackers[attackerId].DamageDealt += damage;
            dropData.Attackers[attackerId].TotalHits++;

            return null;
        }

        private object OnEntityTakeDamage(CH47Helicopter ch47, HitInfo info)
        {
            if (ch47 == null)
                return null;

            if (BradleyProfileCache.TryGetValue(ch47.skinID, out string apcProfile))
            {
                if (info?.Initiator == null || info.damageTypes == null)
                    return null;

                if (!config.bradley.apcConfig[apcProfile].CH47.CanDamage)
                {
                    info?.damageTypes.Clear();
                    return true;
                }
                
                CH47DropComponent ch47Comp = ch47.GetComponent<CH47DropComponent>();
                if (ch47Comp == null)
                    return null;

                if (info?.damageTypes.Total() > ch47._health)
                    ch47Comp.isDying = true;

                var Initiator = info?.Initiator;
                if (Initiator is BasePlayer)
                {
                    BasePlayer attacker = Initiator as BasePlayer;
                    if (attacker == null)
                        return null;

                    ch47Comp.lastAttacker = attacker;
                }
            }
            return null;
        }

        private object OnEntityTakeDamage(ScientistNPC npc, HitInfo info)
        {
            if (npc == null || info == null)
                return null;

            if (BradleyProfileCache.TryGetValue(npc.skinID, out string apcProfile))
            {
                var initiator = info.Initiator;
                if (initiator == null)
                    return null;

                var bradleyScientists = config.bradley.apcConfig[apcProfile].BradleyScientists;

                if (initiator is BasePlayer)
                {
                    BasePlayer attacker = initiator as BasePlayer;
                    if (bradleyScientists.OwnDeployedNPC && !IsOwnerOrFriend(npc.OwnerID, attacker.userID))
                    {
                        info.damageTypes.Clear();
                        return true;
                    }
                }
                else if (initiator is AutoTurret)
                {
                    AutoTurret turret = initiator as AutoTurret;
                    ulong attackerId = turret.ControllingViewerId.GetValueOrDefault().SteamId;

                    if (attackerId.IsSteamId() && bradleyScientists.OwnDeployedNPC && !IsOwnerOrFriend(npc.OwnerID, attackerId))
                    {
                        // Player controlled turrets
                        info.damageTypes.Clear();
                        return true;
                    }
                    else if (!bradleyScientists.CanTurretTargetNpc || (bradleyScientists.OwnDeployedNPC && !IsOwnerOrFriend(npc.OwnerID, turret.OwnerID)))
                    {
                        // Non player controlled turrets
                        info.damageTypes.Clear();
                        return true;
                    }
                }
            }
            
            return null;
        }

        private object OnTurretTarget(AutoTurret turret, BradleyAPC bradley)
        {
            if (bradley == null)
                return null;

			if (BradleyProfileCache.TryGetValue(bradley.skinID, out string apcProfile))
            {
                if (!turret.IsBeingControlled)
                {
                    turret.target = null;
                    return true;
                }
            }
            return null;
        }

        private object OnTurretTarget(AutoTurret turret, ScientistNPC npc)
        {
            if (turret == null || npc == null)
                return null;

            if (BradleyProfileCache.TryGetValue(npc.skinID, out string apcProfile))
            {
                if (!config.bradley.apcConfig[apcProfile].BradleyScientists.CanTurretTargetNpc)
                {
                    return true;
                }
            }
            return null;
        }

        private object OnEntityDestroy(BradleyAPC bradley)
        {
            return HandleBradleyDestroy(bradley);
        }

        private object OnEntityDestroy(CH47Helicopter ch47)
        {
            var ch47Ai = ch47.GetComponent<CH47HelicopterAIController>();
            if (ch47Ai != null)
                ch47Ai.numCrates = 0; // Cancel onboard crates added to make CH47 orbit, but allow custom crate spawn logic only to spawn crates

            return HandleCH47Destroy(ch47);
        }

        private object OnEntityKill(HackableLockedCrate crate)
        {
            LockedCrates.Remove(crate.net.ID.Value);
            return null;
        }

        /*private object OnEntityDestroy(BaseNetworkable entity)
        {
            if (entity is BradleyAPC bradley)
            {
                Puts($"OnEntityDestroy has been called! APC: {bradley}");
                return HandleBradleyDestroy(bradley);
            }

            if (entity is CH47Helicopter ch47)
            {
                Puts($"OnEntityDestroy has been called! CH47: {ch47}");
                var ch47Ai = ch47.GetComponent<CH47HelicopterAIController>();
                if (ch47Ai != null)
                    ch47Ai.numCrates = 0; // Cancel onboard crates added to make CH47 orbit, but allow custom crate spawn logic only to spawn crates

                return HandleCH47Destroy(ch47);
            }

            return null;
        }*/

        private object CanLootEntity(BasePlayer player, LockedByEntCrate entity)
        {
            if (entity.OwnerID == 0)
                return null;

            if (BradleyProfileCache.TryGetValue(entity.skinID, out string apcProfile) && config.bradley.apcConfig[apcProfile].Crates.ProtectCrates)
            {
                if (permission.UserHasPermission(player.UserIDString, permAdmin))
                    return null;

                if (!IsOwnerOrFriend(player.userID, entity.OwnerID))
                {
                    Message(player, "CannotLoot");
                    return false;
                }
            }
            return null;
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            BaseEntity entity = info?.HitEntity;
            if (entity == null || entity.OwnerID == 0 || entity is not ServerGib || entity.IsNpc)
                return null;

            if (BradleyProfileCache.TryGetValue(entity.skinID, out string apcProfile))
			{
                if (config.bradley.apcConfig[apcProfile].Gibs.ProtectGibs)
                {
                    if (permission.UserHasPermission(attacker.UserIDString, permAdmin))
                        return null;

                    if (!IsOwnerOrFriend(attacker.userID, entity.OwnerID))
                    {
                        info.damageTypes.Clear();
                        Message(attacker, "CannotMine");
                        return true;
                    }
                }
            }

            return null;
        }

        private object OnLootSpawn(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.IsDestroyed)
                return null;
            
            if (!BradleyProfileCache.TryGetValue(lootContainer.skinID, out string apcProfile) || string.IsNullOrEmpty(apcProfile))
                return null;

            timer.Once(2f, () =>
            {
                try
                {
                    if (lootContainer == null || lootContainer.IsDestroyed)
                        return;
                    
                    if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData _apcProfile) || _apcProfile == null)
                        return;
                    
                    if (lootContainer.ShortPrefabName.Contains("bradley_crate"))
                    {
                        if (_apcProfile.Loot.UseCustomLoot)
                            SpawnBradleyCrateLoot(lootContainer, apcProfile);

                        if (_apcProfile.ExtraLoot.UseExtraLoot)
                            AddExtraBradleyCrateLoot(lootContainer, apcProfile);
                    }
                    else if (lootContainer.ShortPrefabName.Contains("codelockedhackablecrate"))
                    {
                        if (_apcProfile.LockedCrateLoot.UseLockedCrateLoot)
                            SpawnLockedCrateLoot(lootContainer, apcProfile);
                    }
                }
                catch (System.Exception ex)
                {
                   Log($"Error in OnLootSpawn timer: {ex.Message}", LogLevel.ERROR);
                   Log($"StackTrace: {ex.StackTrace}", LogLevel.ERROR);
                }
            });
            return null;
        }

        private object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity target)
        {
            if (target == null)
                return null;
                
            if (!BradleyProfileCache.TryGetValue(bradley.skinID, out string apcProfile))
                return null;
                
            var bradComp = bradley?.GetComponent<BradleyDrop>();
            if (bradComp == null)
                return null;
                
            var apcConfig = config.bradley.apcConfig[apcProfile];

            if (target.IsNpc)
                return false;
                
            if (target is BasePlayer player)
            {
                if (Vanish && (bool)Vanish?.Call("IsInvisible", player))
                    return false;
                    
                if (!apcConfig.TargetingDamage.TargetSleepers && player.IsSleeping())
                    return false;
                    
                bool isOwnerOrFriend = IsOwnerOrFriend(player.userID, bradley.OwnerID);
                
                if (!apcConfig.TargetingDamage.AttackOwner && isOwnerOrFriend)
                    return false;
                    
                if (!apcConfig.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                    return false;
                    
                var turretPos = bradley.mainTurret.transform.position;
                bool hasLineOfSight = bradley.IsVisible(player.eyes.position, turretPos, bradley.viewDistance) ||
                                      bradley.IsVisible(player.transform.position + (Vector3.up * 0.1f), turretPos, bradley.viewDistance); 
                
                if (!hasLineOfSight)
                {
                    // Check if player is mounted in a vehicle
                    var mountedEntity = player.GetMountedVehicle();
                    if (mountedEntity != null && mountedEntity is BaseVehicle)
                        return true;
                    
                    // Check what's blocking the line of sight
                    var blockingEntity = GetBlockingEntity(turretPos, player, bradley);
                    if (blockingEntity != null)
                    {
                        // Allow targeting if blocked by a vehicle
                        if (blockingEntity is BaseVehicle)
                            return true;

                        List<string> wallFrames = new List<string>
                        {
                            "wall.frame.cell.gate",
                            "wall.frame.cell",
                            "wall.frame.netting"
                        };
                        if (wallFrames.Contains(blockingEntity.ShortPrefabName))
                            return true;
                    }
                    return false;
                }
            }
            else if (target is AutoTurret turret)
            {
                if (!config.bradley.allowTurretDamage || !config.bradley.apcTargetTurret)
                    return false;
                
                bool cooldownExpired = Time.realtimeSinceStartup - bradComp.turretCooldown > config.bradley.turretCooldown;
                return cooldownExpired;
            }
            return null;
        }

        private object OnBradleyApcInitialize(BradleyAPC bradley)
        {
        	NextTick(()=>
            {
                if (bradley != null && BradleyProfileCache.TryGetValue(bradley.skinID, out string apcProfile))
                {
                    bradley._maxHealth = config.bradley.apcConfig[apcProfile].Init.Health;
                    bradley.health = bradley._maxHealth;
                    bradley.viewDistance = 0f;  // Set to 0 so APC doesn't target while parachuting (prevents tumbling)
                    bradley.searchRange = 0f;   // Same
                }
            });
            return null;
        }

        private object CanDeployScientists(BradleyAPC bradley, BasePlayer attacker, List<GameObjectRef> scientistPrefabs, List<Vector3> spawnPositions)
        {
            if (bradley != null && BradleyProfileCache.TryGetValue(bradley.skinID, out string apcProfile))
			{
                if (!config.bradley.apcConfig[apcProfile].BradleyScientists.DeployScientists)
                    return false;
            }
            return null;
        }

        private object OnNpcTargetSense (ScientistNPC npc, BasePlayer player, AIBrainSenses brainSenses)
        {
        	if (npc == null || player == null || player.IsNpc)
            	return null;
               
            if (BradleyProfileCache.TryGetValue(npc.skinID, out string apcProfile))
            {
                var _apcProfile = config.bradley.apcConfig[apcProfile];

                if (!_apcProfile.TargetingDamage.TargetSleepers && player.IsSleeping())
                    return true;
                
                bool isOwnerOrFriend = IsOwnerOrFriend(npc.OwnerID, player.userID);
                if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                {
                    return true;
                }
                
                if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                {
                    return true;
                }
            }

            return null;
        }

        private void OnScientistInitialized(BradleyAPC bradley, ScientistNPC npc, Vector3 spawnPos)
        {
            if (bradley == null || npc == null)
                return;
            
            ulong skinId = bradley.skinID;

            if (!BradleyProfileCache.TryGetValue(skinId, out string apcProfile))
                return;
            
            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData _apcData))
                return;

            NextTick(()=>
            {
                if (npc == null || npc.IsDestroyed)
                    return;
                
                if (bradley == null || bradley.IsDestroyed)
                    return;

                npc.skinID = skinId;

                if (config.bradley.apcConfig[apcProfile].BradleyScientists.OwnDeployedNPC)
                {
                    npc.OwnerID = bradley.OwnerID;
                    npc.creatorEntity = bradley;
                }

                var _bradleyScientists = _apcData.BradleyScientists;

                switch (npc.ShortPrefabName)
                {
                    case "scientistnpc_bradley_heavy":
                        npc.startHealth  = _bradleyScientists.HeavyHealth;
                        npc.damageScale  = _bradleyScientists.HeavyDamageScale;
                        npc.aimConeScale = _bradleyScientists.HeavyAimCone;
                        break;
                    case "scientistnpc_bradley":
                        npc.startHealth  = _bradleyScientists.ScientistHealth;
                        npc.damageScale  = _bradleyScientists.ScientistDamageScale;
                        npc.aimConeScale = _bradleyScientists.ScientistAimCone;
                        break;
                    default:
                        break;
                }

                npc.Brain.Events.Memory.Entity.Clear();
                npc.InitializeHealth(npc.startHealth, npc.startHealth);

                if (config.bradley.apcConfig[apcProfile].BradleyScientists.UseCustomKits)
                    GiveDeployedKit(npc, apcProfile);
            });
        }

        private object OnScientistRecalled(BradleyAPC bradley, ScientistNPC npc)
        {
            if (bradley == null || bradley.IsDestroyed)
                return null;
            
            if (BradleyProfileCache.TryGetValue(bradley.skinID, out string apcProfile))
            {
                if (npc == null || npc.IsDestroyed)
                    return null;

                npc.skinID = bradley.skinID;

                if (config.bradley.apcConfig[apcProfile].BradleyScientists.OwnDeployedNPC)
                {
                    npc.OwnerID = bradley.OwnerID;
                    npc.creatorEntity = bradley;
                }
            }
            return null;
        }

        private object OnNpcTarget(NPCPlayer npc, BasePlayer player)
        {
        	if (npc == null || player == null || player.IsNpc)
            	return null;
               
            if (BradleyProfileCache.TryGetValue(npc.skinID, out string apcProfile))
            {
                var _apcProfile = config.bradley.apcConfig[apcProfile];
                if (!_apcProfile.TargetingDamage.TargetSleepers && player.IsSleeping())
                {
                    return true;
                }
                
                bool isOwnerOrFriend = IsOwnerOrFriend(npc.OwnerID, player.userID);
                if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                {
                    return true;
                }
                
                if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                {
                    return true;
                }
            }
            
            return null;
        }

        private bool CanBeHomingTargeted(CH47Helicopter ch47)
        {
            if (ch47 == null)
                return false;

            if (BradleyProfileCache.TryGetValue(ch47.skinID, out string apcProfile))
            {
                if (!config.bradley.apcConfig[apcProfile].CH47.IsHomingTarget)
                    return false;
            }
            return true;
        }

        private object OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null || crate.net == null)
                return null;

            if (!BradleyProfileCache.TryGetValue(crate.skinID, out string apcProfile))
                return null;

            if (!LockedCrates.TryGetValue(crate.net.ID.Value, out LockedCrateData crateData) || crateData == null)
                return null;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return null;
            
            bool isCH47 = crateData.IsCH47;

            float hackSeconds = isCH47 ? apcData.CH47.HackSeconds : apcData.Crates.HackSeconds;
            
            NextTick(()=>
            {
                if (crate == null || crate.net == null)
                    return;
                
                float hackTime = HackableLockedCrate.requiredHackSeconds - hackSeconds;
                if (crate.hackSeconds > hackTime)
                    return;

                crate.hackSeconds = hackTime;
            });

            return null;
        }

        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null || crate.net == null)
                return null;

            if (!BradleyProfileCache.TryGetValue(crate.skinID, out string apcProfile))
                return null;

            if (!LockedCrates.TryGetValue(crate.net.ID.Value, out LockedCrateData crateData) || crateData == null)
                return null;

            // PlayerId == 0 means the configured protection period has expired.
            if (crateData.PlayerId == 0)
                return null;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return null;

            bool protectCrate = crateData.IsCH47 ? apcData.CH47.ProtectCrates : apcData.Crates.ProtectCrates;

            if (protectCrate && !IsOwnerOrFriend(player.userID, crateData.PlayerId))
            {
                Message(player, "CannotHack");
                return false;
            }

            return null;
        }

        private object OnCrateLaptopAttack(HackableLockedCrate crate, HitInfo info)
        {
            if (crate == null || crate.net == null || info == null)
                return null;

            BasePlayer attacker = info.Initiator as BasePlayer;
            if (attacker == null)
                return null;

            if (!LockedCrates.TryGetValue(crate.net.ID.Value, out LockedCrateData crateData) || crateData == null)
                return null;

            if (!BradleyProfileCache.TryGetValue(crate.skinID, out string apcProfile))
                return null;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return null;

            bool protectCrate = crateData.IsCH47 ? apcData.CH47.ProtectCrates : apcData.Crates.ProtectCrates;

            if (protectCrate && crateData.PlayerId != 0)
                return true;

            return null;
        }

        #endregion Hooks

        #region Loot

        private void SpawnBradleyCrates(BradleyAPC bradley, BasePlayer lastAttacker, ulong ownerId, ulong skinId, string apcProfile, Vector3 position)
        {
            if (Interface.CallHook("CanSpawnBradleyDropCrates", bradley, lastAttacker, ownerId, skinId, apcProfile) != null)
            {
                Log($"CanSpawnBradleyDropCrates hook prevented Bradley crates spawning.", LogLevel.DEBUG);
                return;
            }

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return;

            int cratesToSpawn = apcData.Crates.CratesToSpawn;
            float crateDespawn = apcData.Crates.BradleyCrateDespawn;
            float unlockTime = apcData.Crates.UnlockCrates;
            bool protectCrates = apcData.Crates.ProtectCrates;

            for (int i = 0; i < cratesToSpawn; i++)
            {
                Vector3 newPos1 = Random.onUnitSphere;
                newPos1.y = TerrainMeta.HeightMap.GetHeight(position);
                newPos1.Normalize();
                Vector3 newPos2 = (position + new Vector3(0f, 5.0f, 0f) + newPos1); //(newPos1 * Random.Range(2f, 3f)); Too much, makes crates fly

                GameManager gameManager = GameManager.server;

                LockedByEntCrate crate = gameManager.CreateEntity(bradley.crateToDrop.resourcePath, newPos2, Quaternion.identity, true) as LockedByEntCrate;
                if (crate == null)
                    continue;

                crate.OwnerID = ownerId;
                crate.skinID = skinId;
                crate._name = apcProfile;
                crate.Spawn();

                if (crate == null || crate.IsDestroyed)
                    return;

                crate.CancelInvoke(crate.RemoveMe);
                crate.Invoke(new Action(crate.RemoveMe), crateDespawn);

                Rigidbody rigidbody = crate.gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.mass = 2f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.linearVelocity = Vector3.zero + (newPos1 * Random.Range(1f, 3f));
                rigidbody.angularVelocity = Vector3Ex.Range(-1.75f, 1.75f);
                rigidbody.linearDamping = 0.5f * (rigidbody.mass / 5f);
                rigidbody.angularDamping = 0.2f * (rigidbody.mass / 5f);

                NextTick(()=>
                {
                    if (crate == null || crate.IsDestroyed)
                        return;
                
                    FireBall fireball = null;

                    if (apcData.Crates.FireDuration >= 0 && !apcData.Crates.DisableFire)
                    {
                        fireball = gameManager.CreateEntity(bradley.fireBall.resourcePath, new Vector3(), new Quaternion(), true) as FireBall;
                        if (fireball)
                        {
                            fireball.SetParent(crate, false, false);
                            fireball.Spawn();
                            fireball.GetComponent<Rigidbody>().isKinematic = true;
                            fireball.GetComponent<Collider>().enabled = false;

                            fireball.tickRate = apcData.Fireball.DamageRate;
                            fireball.lifeTimeMin = apcData.Fireball.MinimumLifeTime;
                            fireball.lifeTimeMax = apcData.Fireball.MaximumLifeTime;
                            fireball.damagePerSecond = apcData.Fireball.DamagePerSecond;
                            fireball.waterToExtinguish = apcData.Fireball.WaterAmountToExtinguish;
                            fireball.generation = (apcData.Fireball.SpreadChance == 0) ? 9f : (1f - apcData.Fireball.SpreadChance) / 0.1f;

                            crate.SetLockingEnt(fireball);
                        }

                        timer.Once(apcData.Crates.FireDuration, () =>
                        {
                            if (crate != null || !crate.IsDestroyed)
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
                    crate._name = apcProfile;
                });

                if (protectCrates && unlockTime > 0f)
                    RemoveBradleyOwner(crate, unlockTime);
            }
        }

        private void SpawnLockedCrates(BaseEntity entity, BasePlayer lastAttacker, ulong ownerId, ulong skinId, string apcProfile, Vector3 position)
        {
            if (Interface.CallHook("CanSpawnBradleyDropLockedCrates", entity, lastAttacker, ownerId, skinId, apcProfile) != null)
            {
                Log($"CanSpawnBradleyDropLockedCrates hook prevented locked hackable crates spawning.", LogLevel.WARN);
                return;
            }

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) || apcData == null)
                return;

            bool isCH47 = false;

            if (entity is CH47Helicopter)
                isCH47 = true;

            int lockedCratesToSpawn = isCH47
                ? apcData.CH47.LockedCratesToSpawn
                : apcData.Crates.LockedCratesToSpawn;

            float lockedCrateDespawn = isCH47
                ? apcData.CH47.LockedCrateDespawn
                : apcData.Crates.LockedCrateDespawn;

            bool disableMapMarker = isCH47
                ? apcData.CH47.DisableMapMarker
                : apcData.Crates.DisableMapMarker;
            
            bool protectCrates = isCH47
                ? apcData.CH47.ProtectCrates
                : apcData.Crates.ProtectCrates;

            float unlockTime = isCH47
                ? apcData.CH47.UnlockCrates
                : apcData.Crates.UnlockCrates;

            for (int i = 0; i < lockedCratesToSpawn; i++)
            {
                Vector3 newPos = position + Random.onUnitSphere * 5f;
                newPos.y = isCH47 ? newPos.y : TerrainMeta.HeightMap.GetHeight(newPos) + 7f;
                HackableLockedCrate crate = GameManager.server.CreateEntity(hackableCrate, newPos, Quaternion.identity) as HackableLockedCrate;
                if (crate == null)
                    continue;

                crate.OwnerID = ownerId;
                crate.skinID = skinId;
                crate._name = apcProfile;
                crate.Spawn();

                if (disableMapMarker)
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
                    IsCH47 = isCH47,
                    DecayTime = lockedCrateDespawn
                };

                crate.RefreshDecay();

                timer.Once(2f, () =>
                {
                    if (crate == null || crate.IsDestroyed)
                        return;

                    crate.OwnerID = ownerId;
                    crate.skinID = skinId;
                    crate._name = apcProfile;
                });

                if (protectCrates && unlockTime > 0f)
                {
                    timer.Once(unlockTime, () =>
                    {
                        if (crate == null || crate.IsDestroyed || crate.net == null)
                            return;

                        crate.OwnerID = 0;

                        if (LockedCrates.TryGetValue(crate.net.ID.Value, out LockedCrateData crateData))
                            crateData.PlayerId = 0;
                    });
                }
            }
        }

        private void SpawnBradleyCrateLoot(LootContainer lootContainer, string apcProfile)
        {
            if (string.IsNullOrEmpty(apcProfile) || lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                return;
            
            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out var apcConfig) || apcConfig?.Loot == null || apcConfig.Loot.LootTable == null)
                return;

            var _apcLoot = apcConfig.Loot;
            List<LootItem> lootTable = new List<LootItem>(_apcLoot.LootTable);
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

            var minItems = Math.Clamp(_apcLoot.MinCrateItems, 0, 48);
            var maxItems = Math.Clamp(_apcLoot.MaxCrateItems, minItems, 48);
            int count = Random.Range(minItems, maxItems + 1);
            int given = 0;
            int bps = 0;

            lootContainer.inventory.Clear();
            lootContainer.inventory.capacity = count;
            lootContainer.inventory.maxStackSize = 0; // 0 = use item defaults
            lootContainer.panelName = "generic_resizable";
            lootContainer.SendNetworkUpdateImmediate();

            bool allowDupes = false;
            if (_apcLoot.AllowDupes || (!_apcLoot.AllowDupes && count > items.Count))
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
                if (lootItem.BlueprintChance > Random.Range(0f, 100f) && itemDef.Blueprint != null && IsBP(itemDef) && (bps < _apcLoot.MaxBP))
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

        private void AddExtraBradleyCrateLoot (LootContainer lootContainer, string apcProfile)
        {
            if (string.IsNullOrEmpty(apcProfile) || lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                return;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out var apcConfig) || apcConfig?.ExtraLoot == null || apcConfig.ExtraLoot.LootTable == null)
                return;

            var _apcLoot = apcConfig.ExtraLoot;
            List<LootItem> lootTable = new List<LootItem>(_apcLoot.LootTable);
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

            var minItems = Math.Clamp(_apcLoot.MinExtraItems, 0, 48);
            var maxItems = Math.Clamp(_apcLoot.MaxExtraItems, minItems, 48);
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
            if (_apcLoot.AllowDupes || (!_apcLoot.AllowDupes && count > items.Count))
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
                if (lootItem.BlueprintChance > Random.Range(0f, 100f) && itemDef.Blueprint != null && IsBP(itemDef) && (bps < _apcLoot.MaxBP))
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
                    var amount = Random.Range(lootItem.AmountMin, lootItem.AmountMax + 1);
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

        private void SpawnLockedCrateLoot(LootContainer lootContainer, string apcProfile)
        {
            if (string.IsNullOrEmpty(apcProfile) || lootContainer == null || lootContainer.inventory == null || lootContainer.IsDestroyed || lootContainer.net == null)
                return;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out var apcConfig) || apcConfig?.LockedCrateLoot == null || apcConfig.LockedCrateLoot.LootTable == null)
                return;

            var _apcLoot = apcConfig.LockedCrateLoot;
            List<LootItem> lootTable = new List<LootItem>(_apcLoot.LootTable);
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

            var minItems = Math.Clamp(_apcLoot.MinLockedCrateItems, 0, 48);
            var maxItems = Math.Clamp(_apcLoot.MaxLockedCrateItems, minItems, 48);
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
            if (_apcLoot.AllowDupes || (!_apcLoot.AllowDupes && count > items.Count))
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
                if (lootItem.BlueprintChance > Random.Range(0f, 100f) && itemDef.Blueprint != null && IsBP(itemDef) && (bps < _apcLoot.MaxBP))
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
                    var amount = Random.Range(lootItem.AmountMin, lootItem.AmountMax + 1);
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

        public void SendDiscordEmbed(string title, string description, string hexColor, string footerText = null, List<DiscordField> fields = null)
        {
            int color = HexToInt(hexColor);
            SendDiscordEmbed(title, description, color, footerText, fields);
        }

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
            return bradleyAvatar;
        }

        private string FormatTime(float seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{time.Hours:D2}h {time.Minutes:D2}m {time.Seconds:D2}s";
        }

        #endregion Discord Announcements

        #region API

        // Other devs can call these hooks to help with compatability with their plugins
        object IsBradleyDrop(ulong skinId) => BradleyProfileCache.ContainsKey(skinId) ? true : null;

        object IsBradleyDrop(string skinIdString)
        {
            if(!ulong.TryParse(skinIdString, out ulong skinId))
                return null;

            return BradleyProfileCache.ContainsKey(skinId) ? true : null;
        }

        object IsBradleyDrop(BradleyAPC bradley)
        {
            if (bradley == null || bradley.IsDestroyed)
                return null;
            
            return BradleyProfileCache.ContainsKey(bradley.skinID) ? true : null;
        }

        // Call is like: var bdata = BradleyDrops.Call("GetBradleyDropData", bradley) as Dictionary<string, object>;
        // Then, bdata["apcProfile"] etc
        object GetBradleyDropData(BradleyAPC bradley)
        {
            BradleyDrop bradComp = bradley?.GetComponent<BradleyDrop>();
            if (bradComp == null)
                return null;

            return new Dictionary<string, object>
            {
                ["apcProfile"]          = bradComp.apcProfile,                                  // string - Actual bradley profile (from config)
                ["apcName"]             = bradComp._apcProfile?.Init.APCName,                   // string - Bradley display name
                ["skinId"]              = bradComp.skinId,                                      // ulong
                ["bradleyId"]           = bradComp.bradleyId,                                   // ulong (Net.ID.Value)
                ["owner"]               = bradComp.owner,                                       // BasePlayer
                ["calledPosition"]      = bradComp.position,                                    // Vector3
                ["callingTeam"]         = bradComp.callingTeam,                                 // List
                ["lastAttacker"]        = bradComp.lastAttacker,                                // BasePlayer
                ["ch47"]                = bradComp.ch47,                                        // CH47Helicopter
                ["plane"]               = bradComp.plane,                                       // CargoPlane
                ["balloon"]             = bradComp.balloon,                                     // HotAirBalloon
                ["currentState"]        = bradComp.currentState,                                // enum BradleyState (PATROL, ENGAGE, HUNT, MOVETOENGAGE)
                ["cratesToSpawn"]       = bradComp._apcProfile.Crates.CratesToSpawn,            // Number of Bradley crates to spawn
                ["lockedCratesToSpawn"] = bradComp._apcProfile.Crates.LockedCratesToSpawn       // Number of Locked crates to spawn
            };
        }

        #endregion API

        #region Other Plugin API Hooks

        // Play nice with Bradley Options
        object CanBradleyOptionsEdit(BradleyAPC bradley)
        {
            if (bradley == null || bradley.IsDestroyed)
                return null;
            
            return BradleyProfileCache.ContainsKey(bradley.skinID) ? true : null;
        }

        // Prevent NPCKits messing with APC NPCs
        object OnNpcKits(NPCPlayer npc)
        {
            if (npc == null || npc.IsDestroyed)
                return null;
            
            return BradleyProfileCache.ContainsKey(npc.skinID) ? true : null;
        }

        // Play nice with FancyDrop
        object ShouldFancyDrop(NetworkableId netId)
        {
            if (netId == null)
                return null;
            
            var signal = BaseNetworkable.serverEntities.Find(netId) as BaseEntity;
            if (signal == null || signal.IsDestroyed)
                return null;
            
            return BradleyProfileCache.ContainsKey(signal.skinID) ? true : null;
        }

        // Play nice with EpicLoot
        object CanReceiveEpicLootFromCrate(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null || config.options.allowEpicLoot)
                return null;
            
            return BradleyProfileCache.ContainsKey(lootContainer.skinID) ? true : null;
        }

        // Play nice with AlphaLoot
        object CanPopulateLoot(LootContainer lootContainer)
        {
            if (lootContainer == null)
                return null;

            BradleyProfileCache.TryGetValue(lootContainer.skinID, out string apcProfile);
            if (string.IsNullOrEmpty(apcProfile))
                return null;

            var _apcProfile = config?.bradley?.apcConfig[apcProfile];
            if (_apcProfile == null)
                return null;

            string crateShortName = lootContainer.ShortPrefabName;
            if (crateShortName.Equals("bradley_crate"))
            {
                if (_apcProfile.Loot.UseCustomLoot)
                    return true;
            }
            else if (crateShortName.Equals("codelockedhackablecrate"))
            {
                if (_apcProfile.LockedCrateLoot.UseLockedCrateLoot)
                    return true;
            }
            return null;
        }

        // Play nice with PVE plugins
        object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null || entity.IsDestroyed || hitInfo?.Initiator == null)
                return null;

            if (NpcSpawn && entity.skinID == 11162132011012)
                return null;

            ulong skinId = hitInfo.Initiator.skinID;
            
            return BradleyProfileCache.ContainsKey(skinId) ? true : null;
        }

        // Play nice with BotReSpawn
        object OnBotReSpawnAPCKill(BradleyAPC bradley)
        {
        	if (bradley == null || bradley.IsDestroyed)
                return null;

            ulong bradleyId = bradley.net.ID.Value;
            
            return BradleyDropData.ContainsKey(bradleyId) ? true : null;
        }

        // Play nice with Dynamic PvP
        private object OnCreateDynamicPVP(string eventName, BaseEntity entity)
        {
            if (string.IsNullOrEmpty(eventName) || entity == null || entity.IsDestroyed)
                return null;
        
            if (config.options.useDynamicPVP)
                return null;
            
            return BradleyProfileCache.ContainsKey(entity.skinID) ? true : null;
        }

        #endregion Other Plugin API Hooks

        #region Rewards

        private void CheckRewardPlugins()
        {
            if (config.rewards.enableRewards || CanPurchaseAnySignal())
            {
                if (!plugins.Find(config.rewards.rewardPlugin))
                {
                    Log(
                        $"{config.rewards.rewardPlugin} not found, giving/spending reward points " +
                        "will not be not possible until loaded.", 
                        LogLevel.WARN
                    );
                }
            }
            
            if (config.rewards.enableXP)
            {
                string xpPlugin = config.rewards.pluginXP.ToLower();
                if (xpPlugin == "xperience" && !XPerience || 
                    xpPlugin == "skilltree" && !SkillTree ||
                    xpPlugin == "skills" && !Skills)
                {
                    Log(
                        $"{config.rewards.pluginXP} plugin not found, giving XP will not be " +
                        "not possible until loaded.",
                        LogLevel.WARN
                    );
                }
            }
        }

        private void GiveReward(ulong playerId, double amount, string apcProfile)
        {
            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)amount * keyPair.Value;
                }
            }

            BasePlayer player = FindPlayer(playerId.ToString())?.Object as BasePlayer;
            if (!player)
            {
                Log($"Failed to give reward to: {playerId}, no player found.", LogLevel.WARN);
                return;
            }
            else if (!plugins.Find(config.rewards.rewardPlugin))
            {
                Log(
                    $"Failed to give reward to: {playerId}, {config.rewards.rewardPlugin} not loaded.", LogLevel.WARN
                );
                return;
            }
            switch (config.rewards.rewardPlugin.ToLower())
            {
                case "serverrewards":
                    ServerRewards?.Call("AddPoints", playerId, (int)amount);
                    Message(player, "RewardGiven", (int)amount, config.rewards.rewardUnit, apcProfile);
                    break;
                case "economics":
                    Economics?.Call("Deposit", playerId, amount);
                    Message(player, "RewardGiven", config.rewards.rewardUnit, (int)amount, apcProfile);
                    break;
                default:
                    break;
            }
        }

        private void GiveXP(ulong playerId, double amount, string apcProfile)
        {
            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)(amount * keyPair.Value);
                }
            }
            
            BasePlayer player = FindPlayer(playerId.ToString())?.Object as BasePlayer;
            if (!player)
            {
                Log($"Failed to give XP to: {playerId}, no player found.", LogLevel.WARN);
                return;
            }

            string xpPlugin = config.rewards.pluginXP.ToLower();
            if (string.IsNullOrEmpty(xpPlugin))
            {
                Log($"Failed to give XP to: {playerId}, no XP plugin configured.", LogLevel.WARN);
                return;
            }
            
            if (xpPlugin == "xperience")
            {
                if (!XPerience)
                {
                    Log($"Failed to give XP to: {playerId}, XPerience is not loaded", LogLevel.WARN);
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
                    Log($"Failed to give XP to: {playerId}, SkillTree is not loaded", LogLevel.WARN);
                    return;
                }
                if (config.rewards.boostXP)
                    SkillTree?.Call("AwardXP", player, amount, "BradleyDrops", false);
                else
                    SkillTree?.Call("AwardXP", player, amount, "BradleyDrops", true);
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

            Message(player, "XPGiven", amount, apcProfile);
        }

        private void GiveScrap(ulong playerId, int amount, string apcProfile)
        {
            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)(amount * keyPair.Value);
                }
            }

            BasePlayer player = FindPlayer(playerId.ToString())?.Object as BasePlayer;
            if (player == null)
            {
                Log($"Failed to give {amount} scrap to: {playerId}, no player found.", LogLevel.WARN);
                return;
            }
            Item scrap = ItemManager.CreateByItemID(scrapId, amount, 0);
            player.inventory.GiveItem(scrap);
            Message(player, "ScrapGiven", amount, apcProfile);
        }

        private void GiveCustomReward(ulong playerId, int amount, string apcProfile)
        {
            foreach (KeyValuePair<string, float> keyPair in config.rewards.rewardMultipliers)
            {
                if (permission.UserHasPermission(playerId.ToString(), keyPair.Key))
                {
                    if (keyPair.Value > 1.0f)
                        amount = (int)(amount * keyPair.Value);
                }
            }
            
            BasePlayer player = FindPlayer(playerId.ToString())?.Object as BasePlayer;
            if (player == null)
            {
                Log($"Failed to give custom reward to: {playerId}, no player found.", LogLevel.WARN);
                return;
            }

            var _customRewardItem = config.rewards.customRewardItem;
            if (_customRewardItem == null)
            {
                Log(
                    $"Failed to give {amount} custom reward to: {playerId}, no custom", 
                    LogLevel.WARN
                );
                return;
            }

            var itemShortname = _customRewardItem.ShortName;
            if (string.IsNullOrEmpty(itemShortname))
            {
                Log(
                    $"Failed to give {amount} custom reward to: {playerId}, no custom " +
                    "item shortname configured.", 
                    LogLevel.WARN
                );
                return;
            }
            
            var skinId = _customRewardItem.SkinId;
            Item item = ItemManager.CreateByName(itemShortname, amount, skinId);
            if (item == null)
            {
                Log(
                    $"Failed to give {amount} custom reward to: {playerId}, item creation failed", 
                    LogLevel.WARN
                );
                return;
            }

            if (!string.IsNullOrEmpty(_customRewardItem.DisplayName))
            {
                item.name = _customRewardItem.DisplayName;
                Message(player, "CustomGiven", amount, item.name, apcProfile);
            }
            else
            {
                Message(player, "CustomGiven", amount, item.info.displayName.translated, apcProfile);
            }

            if (!string.IsNullOrEmpty(_customRewardItem.Text))
                item.text = _customRewardItem.Text;
            
            player.inventory.GiveItem(item);
            return;
        }

        private void ProcessRewards(ulong bradleyId, ulong ownerId, string apcProfile)
        {
            if (apcProfile == null)
                return;
                
            if (!BradleyDropData.ContainsKey(bradleyId))
                return;

            var _profileRewards = config.bradley.apcConfig[apcProfile].Rewards;
            if (_profileRewards == null)
                return;
            
            var _bradleyAttackers = BradleyDropData[bradleyId].Attackers;
            if (_bradleyAttackers == null)
                return;

            var totalReward = _profileRewards.RewardPoints;
            var totalXP = _profileRewards.XPReward;
            var totalScrap = _profileRewards.ScrapReward;
            var totalCustom = _profileRewards.CustomReward;
            float damageThreshold = _profileRewards.DamageThreshold;
            double turretPenalty = (100 - config.bradley.turretPenalty) / 100;
            
            int eligibleAttackers = 0;
            foreach (var kvp in _bradleyAttackers)
            {
                if (kvp.Value.DamageDealt >= damageThreshold)
                    eligibleAttackers++;
            }

            var _rewardConf = config.rewards;
            if ((_rewardConf.enableRewards && totalReward > 0))
            {
                if (_rewardConf.shareRewards && eligibleAttackers != 0)
                {
                    foreach (var playerId in _bradleyAttackers.Keys)
                    {
                        float damageDealt = _bradleyAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalReward / eligibleAttackers;
                            float damageRatio = (_bradleyAttackers[playerId].TurretDamage / _bradleyAttackers[playerId].DamageDealt);
                            if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = amount * turretPenalty;
                            
                            GiveReward(playerId, amount, apcProfile);
                        }
                    }
                }
                else
                {
                    float damageRatio = (_bradleyAttackers[ownerId].TurretDamage / _bradleyAttackers[ownerId].DamageDealt);
                    if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                        totalReward = totalReward * turretPenalty;
                    
                    GiveReward(ownerId, totalReward, apcProfile);
                }
            }

            if (_rewardConf.enableXP && totalXP > 0)
            {
                if (_rewardConf.shareXP && eligibleAttackers != 0)
                {
                    foreach (var playerId in _bradleyAttackers.Keys)
                    {
                        float damageDealt = _bradleyAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalXP / eligibleAttackers;
                            float damageRatio = (_bradleyAttackers[playerId].TurretDamage / _bradleyAttackers[playerId].DamageDealt);
                            if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = amount * turretPenalty;
                            
                            GiveXP(playerId, amount, apcProfile);
                        }
                    }
                }
                else
                {
                    float damageRatio = (_bradleyAttackers[ownerId].TurretDamage / _bradleyAttackers[ownerId].DamageDealt);
                    if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                        totalXP = totalXP * turretPenalty;
                    
                    GiveXP(ownerId, totalXP, apcProfile);
                }
            }

            if (_rewardConf.enableScrap && totalScrap > 0)
            {
                if (_rewardConf.shareScrap && eligibleAttackers != 0)
                {
                    foreach (var playerId in _bradleyAttackers.Keys)
                    {
                        float damageDealt = _bradleyAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalScrap / eligibleAttackers;
                            float damageRatio = (_bradleyAttackers[playerId].TurretDamage / _bradleyAttackers[playerId].DamageDealt);
                            if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = (int)(amount * turretPenalty);
                            
                            GiveScrap(playerId, amount, apcProfile);
                        }
                    }
                }
                else
                {
                    float damageRatio = (_bradleyAttackers[ownerId].TurretDamage / _bradleyAttackers[ownerId].DamageDealt);
                    if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                        totalScrap = (int)(totalScrap * turretPenalty);
                    
                    GiveScrap(ownerId, totalScrap, apcProfile);
                }
            }

            if (_rewardConf.enableCustomReward && totalCustom > 0)
            {
                if (_rewardConf.shareCustomReward && eligibleAttackers != 0)
                {
                    foreach (var playerId in _bradleyAttackers.Keys)
                    {
                        float damageDealt = _bradleyAttackers[playerId].DamageDealt;
                        if (damageDealt >= damageThreshold)
                        {
                            var amount = totalCustom / eligibleAttackers;
                            float damageRatio = (_bradleyAttackers[playerId].TurretDamage / _bradleyAttackers[playerId].DamageDealt);
                            if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                                amount = (int)(amount * turretPenalty);
                            
                            GiveCustomReward(playerId, amount, apcProfile);
                        }
                    }
                }
                else
                {
                    float damageRatio = (_bradleyAttackers[ownerId].TurretDamage / _bradleyAttackers[ownerId].DamageDealt);
                    if (config.bradley.allowTurretDamage && config.bradley.turretPenalty > 0f && damageRatio > 0.5f)
                        totalCustom = (int)(totalCustom * turretPenalty);
                    
                    GiveCustomReward(ownerId, totalCustom, apcProfile);
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

        private void BradleySignalThrown(BasePlayer player, SupplySignal entity, Item signal)
        {
            if (player == null || entity == null || signal == null)
                return;

            ulong skinId = signal.skin;
            string permSuffix = string.Empty;
            bool isWaveBradley = false;
            List<string> waveProfileCache = new List<string>();
            string initialWaveProfile = string.Empty;
            string originalProfile = string.Empty;  // Store the original profile from cache
            
            if (!BradleyProfileCache.TryGetValue(skinId, out originalProfile))
            {
                if (entity != null) entity.Kill();
                return;
            }

            string apcProfile = originalProfile;  // This is the actual Bradley profile to use
            string waveProfile = string.Empty;   // This is the wave profile name if it's a wave

            if (DeepSeaManager.IsInsideDeepSea(player.transform.position) && !config.bradley.allowInDeepSea)
            {
                Message(player, "NotInsideDeepSea", apcProfile);
                if (entity != null) entity.Kill();
                timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, originalProfile, 1, "refund"));
                return;
            }
            
            if (config.bradley.apcConfig.ContainsKey(originalProfile))
            {
                // Single Bradley
                permSuffix = config.bradley.apcConfig[originalProfile].Init.GiveItemCommand.ToLower();
            }
            else if (config.bradley.waveConfig.ContainsKey(originalProfile))
            {
                // Wave Bradley
                waveProfile = originalProfile;  // Store the wave profile name
                permSuffix = config.bradley.waveConfig[originalProfile].GiveItemCommand.ToLower();
                isWaveBradley = true;
                if (config.bradley.waveConfig[originalProfile].WaveProfiles.Count > 0)
                {
                    foreach (var item in config.bradley.waveConfig[originalProfile].WaveProfiles)
                    {
                        if (!config.bradley.apcConfig.ContainsKey(item))
                        {
                            Log(
                                $"WaveBradley config contains a profile with an incorrect name " +
                                " ({item}) please correct & reload the plugin!", 
                                LogLevel.ERROR
                            );
                            Message(player, "WaveProfileError", originalProfile);
                            if (entity != null) entity.Kill();
                            timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, originalProfile, 1, "refund"));
                            return;
                        }
                        waveProfileCache.Add(item);
                    }
                    if (waveProfileCache.Count > 0)
                    {
                        apcProfile = waveProfileCache[0];  // Set apcProfile to the first Bradley in the wave
                    }
                    else
                    {
                        Message(player, "WaveProfileError", originalProfile);
                        if (entity != null) entity.Kill();
                        timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, originalProfile, 1, "refund"));
                        return;
                    }
                }
                else
                {
                    Message(player, "NoWaveProfiles", originalProfile);
                    if (entity != null) entity.Kill();
                    timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, originalProfile, 1, "refund"));
                    return;
                }
            }
            
            var perm = $"{Name.ToLower()}.{permSuffix}";
            if (!permission.UserHasPermission(player.UserIDString, perm))
            {
                Message(player, "Permission", isWaveBradley ? waveProfile : apcProfile);
                if (entity != null) entity.Kill();
                timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, isWaveBradley ? waveProfile : apcProfile, 1, "refund"));
                return;
            }
            
            if (config.bradley.useNoEscape && NoEscape)
            {
                if ((bool)NoEscape.CallHook("IsRaidBlocked", player))
                {
                    Message(player, "RaidBlocked", isWaveBradley ? waveProfile : apcProfile);
                    if (entity != null) entity.Kill();
                    timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, isWaveBradley ? waveProfile : apcProfile, 1, "refund"));
                    return;
                }
                else if ((bool)NoEscape.CallHook("IsCombatBlocked", player))
                {
                    Message(player, "CombatBlocked", isWaveBradley ? waveProfile : apcProfile);
                    if (entity != null) entity.Kill();
                    timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, isWaveBradley ? waveProfile : apcProfile, 1, "refund"));
                    return;
                }
            }
            
            if (config.bradley.playerCooldown > 0f && !permission.UserHasPermission(player.UserIDString, permBypasscooldown))
            {
                float cooldown;
                ulong userId = player.userID;
                if (!config.bradley.tierCooldowns)
                {
                    if (PlayerCooldowns.TryGetValue(player.userID, out cooldown))
                    {
                        TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                        Message(player, "CooldownTime", time.ToString(@"hh\:mm\:ss"));
                        if (entity != null) entity.Kill();
                        timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, apcProfile, 1, "refund"));
                        return;
                    }
                    else if (config.bradley.teamCooldown)
                    {
                        foreach (var playerId in PlayerCooldowns.Keys)
                        {
                            if (PlayerCooldowns.TryGetValue(playerId, out cooldown) && IsOwnerOrFriend(player.userID, playerId))
                            {
                                TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                                Message(player, "TeamCooldownTime", time.ToString(@"hh\:mm\:ss"));
                                if (entity != null) entity.Kill();
                                timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, apcProfile, 1, "refund"));
                                return;
                            }
                        }
                    }
                }
                else
                {
                    if (TierCooldowns.ContainsKey(userId))
                    {
                        if (TierCooldowns[userId].TryGetValue(apcProfile, out cooldown))
                        {
                            TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                            Message(player, "CooldownTime", time.ToString(@"hh\:mm\:ss"));
                            if (entity != null) entity.Kill();
                            timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, apcProfile, 1, "refund"));
                            return;
                        }
                        else if (config.bradley.teamCooldown)
                        {
                            foreach (var playerId in TierCooldowns.Keys)
                            {
                                if (TierCooldowns[userId].TryGetValue(apcProfile, out cooldown) && IsOwnerOrFriend(userId, playerId))
                                {
                                    TimeSpan time = TimeSpan.FromSeconds(cooldown - Time.time);
                                    Message(player, "TeamCooldownTime", time.ToString(@"hh\:mm\:ss"));
                                    if (entity != null) entity.Kill();
                                    timer.Once(1f, ()=> GiveBradleyDrop(player, skinId, apcProfile, 1, "refund"));
                                    return;
                                }
                            }
                        }
                    }
                }

                cooldown = config.bradley.playerCooldown;
                foreach (KeyValuePair<string, float> keyPair in config.bradley.vipCooldowns)
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

                if (!config.bradley.tierCooldowns)
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
                else
                {
                    if (!TierCooldowns.ContainsKey(userId))
                        TierCooldowns.Add(userId, new Dictionary<string, float>());

                    if (!TierCooldowns[userId].ContainsKey(apcProfile))
                        TierCooldowns[userId].Add(apcProfile, Time.time + cooldown);
                    else
                        TierCooldowns[userId][apcProfile] = Time.time + cooldown;

                    timer.Once(cooldown, () =>
                    {
                        if (!TierCooldowns.ContainsKey(userId))
                            return;
                        
                        if (TierCooldowns[userId].ContainsKey(apcProfile))
                            TierCooldowns[userId].Remove(apcProfile);
                    });
                }
            }

            BradleySignalComponent signalComponent = entity.gameObject.AddComponent<BradleySignalComponent>();
            if (signalComponent != null)
            {
                signalComponent.signal = entity;
                signalComponent.player = player;
                signalComponent.skinId = isWaveBradley ? config.bradley.apcConfig[apcProfile].Init.SignalSkinID : skinId;
                signalComponent.apcProfile = apcProfile;
                signalComponent.isWaveBradley = isWaveBradley;
                if (isWaveBradley)
                {
                    signalComponent.waveSkinId = skinId;
                    signalComponent.waveProfile = waveProfile;
                    signalComponent.waveProfileCache = waveProfileCache;
                }
            }
        }

        private void ProcessBradleyEnt(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return;
            
            if (!BradleyProfileCache.TryGetValue(entity.skinID, out string apcProfile))
                return;

            var _apcProfile = config.bradley.apcConfig[apcProfile];
            if (_apcProfile == null)
                return;

            if (entity is HelicopterDebris)
            {
                HelicopterDebris debris = entity as HelicopterDebris;
                if (debris == null)
                    return;
                
                debris.InitializeHealth(_apcProfile.Gibs.GibsHealth, _apcProfile.Gibs.GibsHealth);

                if (_apcProfile.Gibs.KillGibs)
                    debris.Kill();
                else if (_apcProfile.Fireball.DisableFire)
                {
                    debris.CancelInvoke(debris.OnCooledDown);
                    debris.OnCooledDown();
                }
                else if (_apcProfile.Gibs.GibsHotTime >= 0)
                {
                    debris.CancelInvoke(debris.OnCooledDown);
                    debris.Invoke(debris.OnCooledDown, _apcProfile.Gibs.GibsHotTime);
                }

                if (_apcProfile.Gibs.ProtectGibs && _apcProfile.Gibs.UnlockGibs > 0)
                    RemoveBradleyOwner(debris, _apcProfile.Gibs.UnlockGibs);
            }
            
            if (entity is FireBall)
            {
                FireBall fireball = entity as FireBall;
                if (fireball == null)
                    return;
                
                fireball.tickRate = _apcProfile.Fireball.DamageRate;
                fireball.lifeTimeMin = _apcProfile.Fireball.MinimumLifeTime;
                fireball.lifeTimeMax = _apcProfile.Fireball.MaximumLifeTime;
                fireball.damagePerSecond = _apcProfile.Fireball.DamagePerSecond;
                fireball.waterToExtinguish = _apcProfile.Fireball.WaterAmountToExtinguish;
                fireball.generation = (_apcProfile.Fireball.SpreadChance == 0) ? 9f : (1f - _apcProfile.Fireball.SpreadChance) / 0.1f;

                if (_apcProfile.Fireball.DisableFire)
                {
                    fireball.Extinguish();
                }
                else
                {
                    float lifeTime = Random.Range(fireball.lifeTimeMax, fireball.lifeTimeMin);
                    fireball.Invoke(() => fireball.Extinguish(), lifeTime);
                    
                    float spreadDelay = lifeTime * _apcProfile.Fireball.SpreadAtLifetimePercent;
                    fireball.Invoke(() => fireball.TryToSpread(), spreadDelay);
                }	
            }
        }

        private void DespawnAPC(BradleyAPC bradley, string apcProfile, float despawnTime = 1f, BasePlayer retiringPlayer = null)
        {
            timer.Once(despawnTime, () =>
            {
                if (bradley == null || bradley.IsDestroyed)
                    return;
                
                var _bradleyDropData = BradleyDropData[bradley.net.ID.Value];
                if (_bradleyDropData == null)
                    return;

                BasePlayer lastAttacker = _bradleyDropData.LastAttacker;
                var bradComp = bradley.GetComponent<BradleyDrop>();
                if (bradComp == null)
                    return;

                if (string.IsNullOrEmpty(apcProfile))
                    apcProfile = bradComp.apcProfile;

                if (bradComp.ch47Ai != null)
                    bradComp.ch47Ai.UnHostile();
                
                bool isWaveBradley = bradComp.isWaveBradley;
                if (isWaveBradley && bradComp.waveProfileCache != null && bradComp.waveProfileCache.Count > 0)
                {
                    // Clear remaining wave cache to prevent spawning more bradleys
                    bradComp.waveProfileCache.Clear();
                }
                
                if (config.discord.sendApcDespawn)
                {
                    var gridPos = PositionToGrid(bradley.transform.position);
                    if (gridPos == null)
                        gridPos = "Unknown";

                    BasePlayer ownerPlayer = _bradleyDropData.Owner;
                    if (ownerPlayer == null)
                        return;

                    string ownerSteamUrl = steamProfileUrl + ownerPlayer.userID;
                    string displayNameLink = $"[{ownerPlayer.displayName}]({ownerSteamUrl})";
                    string steamIdLink = $"[{ownerPlayer.UserIDString}]({ownerSteamUrl})";

                    if (isWaveBradley)
                    {
                        var waveProfile = bradComp.waveProfile;
                        
                        List<string> bradleyNames = new List<string>();
                        var waveConfig = config.bradley.waveConfig[waveProfile].WaveProfiles;
                        int totalInWave = waveConfig.Count;
                        
                        // Calculate how many were destroyed based on the wave position of current bradley
                        int destroyedCount = _bradleyDropData.WavePosition -1;
                        
                        for (int i = 0; i < waveConfig.Count; i++)
                        {
                            var profile = waveConfig[i];
                            if (config.bradley.apcConfig.ContainsKey(profile))
                            {
                                string bradleyName = config.bradley.apcConfig[profile].Init.APCName;
                                if (i < destroyedCount)
                                {
                                    bradleyNames.Add($"💥 {bradleyName}");
                                }
                                else
                                {
                                    bradleyNames.Add($"💚 {bradleyName}");
                                }
                            }
                        }
                        string waveComposition = string.Join("\n", bradleyNames);
                        var title = Lang("DiscordEmbedTitleWaveRetired", waveProfile);
                        var desc = Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                        var footer = Lang("DiscordEmbedFooter", string.Empty) + $"  |  {zeodeFooterUrl}";
                        string color = "#FFA500"; // Orange color for retirement                            
                        
                        string retiredByValue;
                        if (retiringPlayer != null)
                        {
                            string retiringSteamUrl = steamProfileUrl + retiringPlayer.userID;
                            string[] retirer = new string[] { retiringPlayer.displayName, retiringSteamUrl };
                            retiredByValue = Lang("DiscordWaveRetiredBy", retirer);
                        }
                        else
                        {
                            retiredByValue = Lang("DiscordWaveRetiredTimeout", string.Empty);
                        }
                        
                        var field = new List<DiscordField>
                        {
                            new DiscordField { Name = Lang("DiscordWaveRetiredStatus", string.Empty), Value = retiredByValue, Inline = true},
                            new DiscordField { Name = Lang("DiscordWaveCompleteTotal", string.Empty), Value = destroyedCount.ToString(), Inline = true},
                            new DiscordField { Name = Lang("DiscordWaveRetiredBradleys", string.Empty), Value = waveComposition, Inline = false}
                        };       

                        SendDiscordEmbed(title, desc, color, footer, field);
                    }
                    else
                    {
                        string leaderBoard = string.Empty;
                        if (lastAttacker != null)
                        {
                            int count = 1;

                            // Copy attackers into a list for sorting
                            var attackerList = new List<KeyValuePair<ulong, AttackersStats>>(_bradleyDropData.Attackers);

                            // Sort by damage descending
                            attackerList.Sort((a, b) => b.Value.DamageDealt.CompareTo(a.Value.DamageDealt));

                            int max = config.announce.maxReported;
                            for (int i = 0; i < attackerList.Count && count <= max; i++)
                            {
                                var entry = attackerList[i];

                                string playerName = $"[{entry.Value.Name}]({steamProfileUrl + entry.Key}) ";
                                float damageDealt = entry.Value.DamageDealt;

                                leaderBoard += string.Format(
                                    lang.GetMessage("DiscordEmbedDamageReportIndex", this, lastAttacker.UserIDString),
                                    count,
                                    playerName,
                                    Math.Round(damageDealt, 2)
                                );
                                count++;
                            }
                        }

                        TimeSpan timeSpan = TimeSpan.FromSeconds(Time.realtimeSinceStartup - _bradleyDropData.StartTime);
                        string time = timeSpan.ToString(@"hh\:mm\:ss");
                        
                        bool isRetirePlayer = false;
                        string retiring = string.Empty;
                        if (retiringPlayer != null)
                        {
                            isRetirePlayer = true;
                            string retiringSteamUrl = steamProfileUrl + retiringPlayer.userID;
                            retiring = ($"[{retiringPlayer.displayName}]({retiringSteamUrl})");
                        }

                        var title = Lang("DiscordEmbedTitleRetire", apcProfile);
                        var desc = Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                        var footer = Lang("DiscordEmbedFooter", string.Empty) + $"  |  {zeodeFooterUrl}";
                        var noreport = Lang("DiscordRetireNoReport", string.Empty);
                        string color = "#FFA500"; // Orange embed
                        string health = config.bradley.apcConfig[apcProfile].Init.Health.ToString();
                
                        List<DiscordField> field;
                        field = new List<DiscordField>
                        {
                            new DiscordField { Name = Lang("DiscordRetireLocation", string.Empty), Value = gridPos, Inline = true},
                            new DiscordField { Name = Lang("DiscordRetirePlayer", string.Empty), Value = isRetirePlayer ? retiring : "None (Despawned)", Inline = true},
                            new DiscordField { Name = Lang("DiscordRetireTime", string.Empty), Value = time, Inline = true},
                            new DiscordField { Name = Lang("DiscordRetireHealth", string.Empty), Value = $"{bradley._health.ToString()}/{health}", Inline = true},
                        };
                        
                        if (lastAttacker != null)
                            field.Add(new DiscordField { Name = Lang("DiscordRetireReport", string.Empty), Value = leaderBoard, Inline = false});
                        else
                            field.Add(new DiscordField { Name = Lang("DiscordRetireReport", string.Empty), Value = noreport, Inline = false});
                        
                        SendDiscordEmbed(title, desc, color, footer, field);
                    }
                }
                
                if (bradley != null) 
                {
                    if(config.bradley.despawnExplosion)
                        Effect.server.Run(bradleyExplosion, bradley.transform.position);

                    bradComp.isDespawning = true;
                    bradley.Kill();
                }
            });
        }

        private object HandleBradleyDestroy(BradleyAPC bradley)
        {
            if (bradley == null || bradley.IsDestroyed)
                return null;

            if (!BradleyDropData.TryGetValue(bradley.net.ID.Value, out var dropData))
                return null;

            ulong bradleyId = bradley.net.ID.Value;

            string apcProfile = ResolveApcProfile(bradley);
            if (apcProfile == null)
                return null;

            ulong skinId = bradley.skinID;

            Vector3 position = bradley.transform.position;
            ulong ownerId = bradley.OwnerID;
            string gridPos = PositionToGrid(position) ?? "Unknown";
            bool isVanilla = IsVanillaBradley(bradley, apcProfile);

            if (!isVanilla)
            {
                var bradComp = bradley.GetComponent<BradleyDrop>();
                if (bradComp != null)
                    bradComp.isDying = true;
            }

            BasePlayer lastAttacker = dropData.LastAttacker;
            if (lastAttacker == null)
                return null;

            List<BasePlayer> attackers = GetAttackingTeam(lastAttacker);
            if (attackers == null || attackers.Count == 0)
                return null;

            string time = FormatTimeSpan(Time.realtimeSinceStartup - dropData.StartTime);
            string displayName = GetBradleyDisplayName(apcProfile, isVanilla);

            if (config.announce.killChat)
                AnnounceKillToChat(displayName, lastAttacker, gridPos, time, attackers);

            if (config.announce.reportChat)
                AnnounceDamageReport(dropData, lastAttacker, attackers);

            if (config.discord.sendApcKill)
                SendDiscordReport(dropData, apcProfile, position, gridPos, lastAttacker, time);

            if (config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData))
            {
                if (apcData.Crates.CratesToSpawn > 0)
                    SpawnBradleyCrates(bradley, lastAttacker, ownerId, skinId, apcProfile, position);

                if (apcData.Crates.LockedCratesToSpawn > 0)
                    SpawnLockedCrates(bradley, lastAttacker, ownerId, skinId, apcProfile, position);

                if (BotReSpawn && !string.IsNullOrEmpty(apcData.BotReSpawn.BotReSpawnProfile))
                    BotReSpawn?.Call("AddGroupSpawn", position, apcData.BotReSpawn.BotReSpawnProfile, $"{apcData.BotReSpawn.BotReSpawnProfile}Group", 0);
            
                Interface.CallHook("OnBradleyDropApcKill", bradley);
            }

            timer.Once(0.25f, () => TagNearbyEntities(position, ownerId, skinId, apcProfile));

            timer.Once(10f, () => BradleyDropData.Remove(bradleyId));

            return null;
        }

        private void KillOrDespawnDeployedScientists(BradleyAPC bradley, APCData apcData, bool despawn = false)
        {
            var scientists = bradley.activeScientists;

            if (scientists == null || scientists.Count == 0)
                return;

            if (bradley == null | bradley.IsDestroyed)
                return;

            foreach (var npc in scientists.ToArray())
            {
                if (npc == null || npc.IsDestroyed)
                    continue;

                if (despawn)
                {
                    npc.Brain.LoadAIDesignAtIndex(1);

                    timer.Once(apcData.BradleyScientists.DespawnTime, ()=>
                    {
                        if (npc == null || npc.IsDestroyed)
                            return;
                        
                        npc.Kill(BaseNetworkable.DestroyMode.None, true);
                    });
                }
                else
                {
                    npc.Kill(BaseNetworkable.DestroyMode.None, true);
                }
            }
            bradley.activeScientists.Clear();
            bradley.numberOfScientistsToSpawn = 0;
        }

        private object HandleCH47Destroy(CH47Helicopter ch47)
        {
            if (!BradleyProfileCache.TryGetValue(ch47.skinID, out string apcProfile))
                return null;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData))
                return null;

            var controller = ch47.GetComponent<CH47HelicopterAIController>();
            controller?.DismountAllPlayers();

            var ch47Comp = ch47.GetComponent<CH47DropComponent>();
            if (ch47Comp == null)
                return null;

            ch47Comp.isDying = true;

            if (apcData.CH47.LockedCratesToSpawn > 0)
                SpawnLockedCrates(ch47, ch47Comp.lastAttacker, ch47.OwnerID, ch47.skinID, apcProfile, ch47.transform.position);

            Interface.CallHook("OnBradleyDropCH47Kill", ch47);

            return null;
        }

        private bool TryGetLockedCrateDecayTime(HackableLockedCrate crate, LockedCrateData crateData, out float decayTime)
        {
            decayTime = 0f;

            if (crate == null || crateData == null)
                return false;

            if (!BradleyProfileCache.TryGetValue(crate.skinID, out string apcProfile))
                return false;

            if (string.IsNullOrEmpty(apcProfile))
                return false;

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData) ||
                apcData == null)
            {
                return false;
            }

            decayTime = crateData.IsCH47 ? apcData.CH47.LockedCrateDespawn : apcData.Crates.LockedCrateDespawn;

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
                return false;

            crate.Invoke(crate.DelayedDestroy, crateData.DecayTime);

            return true;
        }

        private bool IsDamageAuthorized(BradleyAPC bradley, BaseEntity entity)
        {
            // Prevents players authing on TC and not teaming with base owner to exploit event
            // and receive no base damage. Now if they have TC = damage (if set in config)
            BradleyDrop bradComp = bradley.GetComponent<BradleyDrop>();
            if (bradComp == null || bradComp.callingTeam == null)
                return true;

            foreach (BasePlayer player in bradComp.callingTeam)
            {
                if (entity.GetBuildingPrivilege()?.IsAuthed(player) == true)
                    return true;
            }
            return false;
        }

        private string ResolveApcProfile(BradleyAPC bradley)
        {
            string profile = bradley._name;
            if (!BradleyProfileCache.TryGetValue(bradley.skinID, out profile))
            {
                if (bradley.skinID == 0)
                    return "bradleyapc"; // Assume vanilla fallback
                
                return null;
            }
            return profile;
        }

        private bool IsVanillaBradley(BradleyAPC bradley, string profile)
        {
            return bradley.skinID == 0 && (string.IsNullOrEmpty(profile) || profile.Contains("bradleyapc"));
        }

        private string FormatTimeSpan(float seconds)
        {
            return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
        }

        private string GetBradleyDisplayName(string profile, bool isVanilla)
        {
            if (isVanilla)
                return config.announce.vanillaName ?? "Bradley APC";

            return config.bradley.apcConfig.TryGetValue(profile, out var _apcProfile)
                ? _apcProfile.Init.APCName ?? "Bradley APC" : "Bradley APC";
        }

        private void HandleTurretDamage(ApcStats data, AutoTurret turret, BradleyDrop comp, HitInfo info)
        {
            float dmg = info.damageTypes.Total();

            data.LastTurretAttacker = turret;
            data.TurretDamage += dmg;
            data.Attackers[turret.ControllingViewerId.GetValueOrDefault().SteamId].TurretDamage += dmg;

            if (!config.bradley.apcTargetTurret || Time.realtimeSinceStartup - comp.turretCooldown <= config.bradley.turretCooldown)
                return;

            if (!comp.isShellingTurret && data.TurretDamage > data.PlayerDamage)
            {
                comp.turretAttackTime = Time.realtimeSinceStartup;
                comp.isShellingTurret = true;

                var targetInfo = Pool.Get<BradleyAPC.TargetInfo>();
                targetInfo.Setup(turret, Time.time);
                comp.turretTargets.Add(targetInfo);
            }
        }

        private bool TryGetAttacker(HitInfo info, BaseEntity initiator, string apcName, out BasePlayer attacker, 
                                    out ulong attackerId, out AutoTurret turret, out object blockDamage)
        {
            attacker = null;
            attackerId = 0;
            turret = null;
            blockDamage = null;

            switch (initiator)
            {
                case BasePlayer player:
                    attacker = player;
                    attackerId = attacker.userID;
                    return true;
                case AutoTurret autoTurret:
                    turret = autoTurret;
                    ulong controllingId = turret.ControllingViewerId.GetValueOrDefault().SteamId;

                    if (controllingId == 0)
                        return false;

                    attacker = BasePlayer.FindByID(controllingId);
                    if (attacker == null)
                        return false;

                    attackerId = attacker.userID;

                    if (!config.bradley.allowTurretDamage)
                    {
                        info.damageTypes.Clear();
                        if (attacker.GetMounted() is ComputerStation station)
                        {
                            timer.Once(0.25f, () => Effect.server.Run(shockSound, station.transform.position));
                            timer.Once(0.5f, () => Effect.server.Run(deniedSound, station.transform.position));
                            attacker.EnsureDismounted();
                            Message(attacker, "NoTurret", apcName);
                        }

                        blockDamage = true;
                        return false;
                    }

                    return true;

                default:
                    return false;
            }
        }

        private bool ValidateDamagePermissions(BradleyDrop comp, BasePlayer attacker, BaseEntity initiator, HitInfo info, string apcName, string apcProfile)
        {
            if (comp == null || initiator.ShortPrefabName.Contains("fireball"))
                return false;

            var _apcProfile = config.bradley.apcConfig[apcProfile];
            
            if (_apcProfile.TargetingDamage.OwnerDamage && !IsOwnerOrFriend(attacker.userID, comp.owner.userID))
            {
                info.damageTypes.Clear();
                Message(attacker, "CannotDamage", apcName);
                return false;
            }

            if (!comp.hasLanded && _apcProfile.Init.ChuteProtected)
            {
                info.damageTypes.Clear();
                Message(attacker, "NotLanded", apcName);
                return false;
            }

            float distance = Vector3.Distance(comp.bradley.transform.position, attacker.transform.position);
            float maxDistance = config.bradley.maxHitDistance;
            if (maxDistance > 0 && distance > maxDistance)
            {
                info.damageTypes.Clear();
                Message(attacker, "TooFarAway", apcName, maxDistance);
                return false;
            }

            if (comp.isDespawning || comp.isDying)
            {
                info.damageTypes.Clear();
                return false;
            }

            return true;
        }

        private void AnnounceKillToChat(string displayName, BasePlayer killer, string gridPos, string time, List<BasePlayer> team)
        {
            string msg = string.Format(lang.GetMessage("BradleyKilledTime", this), displayName, killer.displayName, gridPos, time);
            AnnounceToChat(killer, team, msg);
        }

        private void AnnounceDamageReport(ApcStats data, BasePlayer target, List<BasePlayer> team)
        {
            var attackers = new List<KeyValuePair<ulong, AttackersStats>>(data.Attackers);

            attackers.Sort((a, b) =>
                b.Value.DamageDealt.CompareTo(a.Value.DamageDealt));

            int max = config.announce.maxReported;
            int count = Math.Min(max, attackers.Count);

            var sb = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                var entry = attackers[i];
                sb.AppendFormat(
                    lang.GetMessage("DamageReportIndex", this, target.UserIDString),
                    i + 1,
                    entry.Value.Name,
                    Math.Round(entry.Value.DamageDealt, 2)
                );
            }

            string fullMsg = string.Format(
                lang.GetMessage("DamageReport", this, target.UserIDString),
                sb.ToString());

            AnnounceToChat(target, team, fullMsg);
        }

        private void SendDiscordReport(ApcStats data, string profile, Vector3 pos, string gridPos, BasePlayer killer, string time)
        {
            if (data.Owner == null)
                return;

            string steamUrl = steamProfileUrl + data.Owner.userID;
            string displayNameLink = $"[{data.Owner.displayName}]({steamUrl})";
            string steamIdLink = $"[{data.Owner.UserIDString}]({steamUrl})";

            string title = !string.IsNullOrEmpty(data.ParentWaveProfile) && data.WavePosition > 0
                ? string.Format(lang.GetMessage("DiscordEmbedTitleKilledWave", this), profile, data.WavePosition, data.WaveTotalCount)
                : Lang("DiscordEmbedTitleKilled", profile);

            var attackerList = new List<KeyValuePair<ulong, AttackersStats>>(data.Attackers);

            // Sort by damage descending
            attackerList.Sort((a, b) => b.Value.DamageDealt.CompareTo(a.Value.DamageDealt));

            int max = Math.Min(config.announce.maxReported, attackerList.Count);
            var sb = new StringBuilder();

            for (int i = 0; i < max; i++)
            {
                var entry = attackerList[i];
                sb.AppendFormat(
                    lang.GetMessage("DiscordEmbedDamageReportIndex", this, null),
                    i + 1,
                    $"[{entry.Value.Name}]({steamProfileUrl + entry.Key})",
                    Math.Round(entry.Value.DamageDealt, 2)
                );
            }

            var fields = new List<DiscordField>
            {
                new() { Name = Lang("DiscordKilledLocation", string.Empty), Value = gridPos, Inline = true },
                new() { Name = Lang("DiscordKilledTime", string.Empty), Value = time, Inline = true },
                new() { Name = Lang("DiscordKilledKiller", string.Empty), Value = $"[{killer.displayName}]({steamUrl})", Inline = true },
                new() { Name = Lang("DiscordKilledLeaderboard", string.Empty), Value = sb.ToString(), Inline = false }
            };

            SendDiscordEmbed(
                title,
                Lang("DiscordEmbedOwner", displayNameLink, steamIdLink),
                "#FF0000",
                Lang("DiscordEmbedFooter", string.Empty) + $"  |  {zeodeFooterUrl}",
                fields);
        }

        private void TagNearbyEntities(Vector3 position, ulong ownerId, ulong skinId, string apcProfile)
        {
            List<BaseEntity> ents = Pool.Get<List<BaseEntity>>();
            Vis.Entities(position, 15f, ents, layerHeliGibs, QueryTriggerInteraction.Ignore);

            foreach (BaseEntity ent in ents)
            {
                if (ent.OwnerID != 0)
                    continue;

                if (ent is HelicopterDebris or LockedByEntCrate or FireBall)
                {
                    ent.OwnerID = ownerId;
                    ent.skinID = skinId;
                    ent._name = apcProfile;
                    ProcessBradleyEnt(ent);
                }
            }

            Pool.FreeUnmanaged(ref ents);
        }

        private bool IsPlayerBehindVehicle(Vector3 bradleyPos, BasePlayer player, BradleyAPC bradley)
        {
            var direction = (player.transform.position - bradleyPos).normalized;
            var distance = Vector3.Distance(bradleyPos, player.transform.position);
            
            int layerMask = LayerMask.GetMask("Vehicle Detailed", "Vehicle World", "Prevent Building", "Player (Server)");
            RaycastHit[] hits = Physics.RaycastAll(bradleyPos, direction, distance + 2f, layerMask);
            
            if (hits.Length == 0)
                return false;
            
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;
                
                var entity = hit.collider.GetComponentInParent<BaseEntity>() ?? hit.collider.GetComponent<BaseEntity>();
                if (entity == null)
                    continue;
                
                if (entity == bradley)
                    continue;
                
                if (entity is BaseVehicle)
                {
                    foreach (var nextHit in hits)
                    {
                        if (nextHit.distance <= hit.distance)
                            continue;
                        
                        var nextEntity = nextHit.collider.GetComponentInParent<BaseEntity>() ?? nextHit.collider.GetComponent<BaseEntity>();
                        if (nextEntity == player)
                            return true;
                    }
                }
                if (entity == player)
                    return false;
            }
            return false;
        }

        private BaseEntity GetBlockingEntity(Vector3 bradleyPos, BasePlayer player, BradleyAPC bradley)
        {
            var direction = (player.transform.position - bradleyPos).normalized;
            var distance = Vector3.Distance(bradleyPos, player.transform.position);
            
            int layerMask = LayerMask.GetMask("Construction", "Deployed", "Vehicle Detailed", "Vehicle World", "Prevent Building", "Player (Server)");
            RaycastHit[] hits = Physics.RaycastAll(bradleyPos, direction, distance + 2f, layerMask);
            
            if (hits.Length == 0)
                return null;
            
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            int playerIndex = -1;
            for (int i = 0; i < hits.Length; i++)
            {
                var entity = hits[i].collider.GetComponentInParent<BaseEntity>() ?? hits[i].collider.GetComponent<BaseEntity>();
                if (entity == player)
                {
                    playerIndex = i;
                    break;
                }
            }
            
            if (playerIndex == -1)
                return null;
            
            // Find what's immediately before the player
            for (int i = playerIndex - 1; i >= 0; i--)
            {
                var hit = hits[i];
                if (hit.collider == null)
                    continue;
                
                var entity = hit.collider.GetComponentInParent<BaseEntity>() ?? hit.collider.GetComponent<BaseEntity>();
                if (entity == null || entity == bradley)
                    continue;
                
                return entity;
            }
            return null;
        }

        private string PositionToGrid(Vector3 pos)
        {
            if (pos == null)
                return "Unkown";

            return MapHelper.PositionToString(pos);
        }

        private void LoadProfileCache()
        {
            foreach (var key in config.bradley.apcConfig.Keys)
            {
                var permSuffix = config.bradley.apcConfig[key].Init.GiveItemCommand.ToLower();
                if (string.IsNullOrEmpty(permSuffix))
                {
                    Log($"Profile '{key}' has no profile shortname, please correct your config & reload.", LogLevel.ERROR);
                    continue;
                }

                var skinId = config.bradley.apcConfig[key].Init.SignalSkinID;
                if (skinId == 0)
                {
                    Log($"Profile '{key}' has no/invalid skin ID, please correct your config & reload.", LogLevel.ERROR);
                    continue;
                }

                var perm = $"{Name.ToLower()}.{permSuffix}";
                permission.RegisterPermission(perm, this);

                if (ApcProfiles.Contains(permSuffix))
                {
                    Log(
                        $"Config already contains a profile with shortname '{permSuffix}', " +
                        "these must be unique for each profile. Correct your config & reload.", 
                        LogLevel.ERROR
                    );
                    continue;
                }
                else
                {
                    ApcProfiles.Add(permSuffix);
                }

                if (BradleyProfileCache.ContainsKey(skinId))
                {
                    Log(
                        $"Config already contains a profile with skin ID '{skinId}', " +
                        "these must be unique for each profile. Correct your config & reload.",
                        LogLevel.ERROR
                    );
                    continue;
                }
                else
                {
                    BradleyProfileCache.Add(skinId, key);
                }
            }
            
            foreach (var key in config.bradley.waveConfig.Keys)
            {
                var permSuffix = config.bradley.waveConfig[key].GiveItemCommand.ToLower();
                if (string.IsNullOrEmpty(permSuffix))
                {
                    Log($"Wave profile '{key}' has no profile shortname, please correct your config & reload.", LogLevel.ERROR);
                    continue;
                }

                var skinId = config.bradley.waveConfig[key].SkinId;
                if (skinId == 0)
                {
                    Log($"Wave profile '{key}' has no/invalid skin ID, please correct your config & reload.", LogLevel.ERROR);
                    continue;
                }

                var perm = $"{Name.ToLower()}.{permSuffix}";
                permission.RegisterPermission(perm, this);

                if (WaveProfiles.Contains(permSuffix))
                {
                    Log(
                        $"Config already contains a wave profile with shortname '{permSuffix}', " +
                        "these must be unique for each profile. Correct your config & reload.", 
                        LogLevel.ERROR
                    );
                    continue;
                }
                else
                {
                    WaveProfiles.Add(permSuffix);
                }

                if (BradleyProfileCache.ContainsKey(skinId))
                {
                    Log(
                        $"Config already contains a wave profile with skin ID '{skinId}', " +
                        "these must be unique for each profile. Correct your config & reload.",
                        LogLevel.ERROR
                    );
                    continue;
                }
                else
                {
                    BradleyProfileCache.Add(skinId, key);
                }

                if (config.bradley.waveConfig[key].WaveProfiles.Count == 0)
                {
                    // If loading fresh, populate default wave profiles
                    foreach (var profile in config.bradley.apcConfig.Keys)
                        config.bradley.waveConfig[key].WaveProfiles.Add(profile);  
                    
                    SaveConfig();
                }
            }
        }

        private void AnnounceToChat(BasePlayer player, List<BasePlayer> players, string message = null)
        {
            if (player == null || message == null)
                return;
            
            if (config.announce.announceGlobal)
            {
                Server.Broadcast(message, config.options.usePrefix ? config.options.chatPrefix : null, config.options.chatIcon);
                return;
            }

            bool hasTeam = false;
            foreach (var member in players)
            {
                if(IsOwnerOrFriend(player.userID, member.userID))
                {
                    hasTeam = true;
                    Player.Message(member, message, config.options.usePrefix ? config.options.chatPrefix : null, config.options.chatIcon);
                }
            }
            if (!hasTeam)
            {
                Player.Message(player, message, config.options.usePrefix ? config.options.chatPrefix : null, config.options.chatIcon);
            }
        }

        private List<BasePlayer> GetAttackingTeam(BasePlayer player)
        {
            List<BasePlayer> players = new List<BasePlayer>();
            if (config.options.useClans && Clans)
            {
                List<string> clan = (List<string>)Clans?.Call("GetClanMembers", player.UserIDString);
                if (clan != null)
                {
                    foreach (var memberId in clan)
                    {
                        var member = FindPlayer(memberId);
                        if (member == null)
                            continue;
                        
                        if (member.IsConnected)
                        {
                            BasePlayer p = member.Object as BasePlayer;
                            if (!players.Contains(p))
                                players.Add(p);
                        }
                    }
                }
            }
            if (config.options.useTeams)
            {
                RelationshipManager.PlayerTeam team;
                RelationshipManager.ServerInstance.playerToTeam.TryGetValue(player.userID, out team);
                if (team != null)
                {
                    foreach (var memberId in team.members)
                    {
                        var member = FindPlayer(memberId.ToString());
                        if (member == null)
                            continue;
                        
                        if (member.IsConnected)
                        {
                            BasePlayer p = member.Object as BasePlayer;
                            if (!players.Contains(p))
                                players.Add(p);
                        }
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
                        var friend = FindPlayer(friendId.ToString());
                        if (friend == null)
                            continue;
                        
                        if (friend.IsConnected)
                        {
                            BasePlayer p = friend.Object as BasePlayer;
                            if (!players.Contains(p))
                                players.Add(p);
                        }
                    }
                }
            }
            if (!players.Contains(player))
                players.Add(player);
            
            return players;
        }

        private bool IsBP(ItemDefinition itemDef) => itemDef?.Blueprint != null && itemDef.Blueprint.isResearchable && !itemDef.Blueprint.defaultBlueprint;

        private bool CanPurchaseAnySignal()
        {
            foreach (var key in config.bradley.apcConfig.Keys)
            {
                if (config.bradley.apcConfig[key].Init.UseBuyCommand && config.purchasing.defaultCurrency != "Custom")
                    return true;
            }
            return false;
        }

        private void SetDamageScale(TimedExplosive shell, float scale)
        {
            foreach (DamageTypeEntry damageType in shell.damageTypes)
                damageType.amount *= scale;
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
                catch {}
            }
            
            if (config.options.useFriends && Friends)
            {
                try
                {
                    var result = Friends.Call("AreFriends", playerId, targetId);
                    if (result != null && (bool)result)
                        return true;
                }
                catch {}
            }
            return false;
        }

        private object CheckAndFixSignal(Item signal)
        {
            if (signal == null)
                return null;
            
            ulong skinId = signal.skin;
            if (BradleyProfileCache.TryGetValue(skinId, out string apcProfile))
            {
                signal.name = apcProfile;
                signal.skin = skinId;
                signal.MarkDirty();
            }
            return signal;
        }

        private void RemoveBradleyOwner(BaseNetworkable entity, float time)
        {
            timer.Once(time, () =>
            {
                if (entity != null || entity.IsDestroyed)
                {
                    (entity as BaseEntity).OwnerID = 0;
                    entity.SendNetworkUpdateImmediate();
                }
            });
        }
        
        private bool GiveBradleyDrop(BasePlayer player, ulong skinId, string dropName, int dropAmount, string reason)
        {
            if (player != null && player.IsAlive())
            {
                Item apcDrop = ItemManager.CreateByItemID(supplySignalId, dropAmount, skinId);
                apcDrop.name = dropName;
                if (player.inventory.GiveItem(apcDrop))
                    return true;
                
                apcDrop.Remove(0f);
            }
            return false;
        }

        private IPlayer FindPlayer(string nameOrIdOrIp)
        {
            foreach (var activePlayer in covalence.Players.Connected)
            {
                if (activePlayer.Id == nameOrIdOrIp)
                    return activePlayer;
                if (activePlayer.Name.Contains(nameOrIdOrIp))
                    return activePlayer;
                if (activePlayer.Name.ToLower().Contains(nameOrIdOrIp.ToLower()))
                    return activePlayer;
                if (activePlayer.Address == nameOrIdOrIp)
                    return activePlayer;
            }
            return null;
        }

        private bool IsInSafeZone(Vector3 position)
        {
            var colliders = Facepunch.Pool.Get<List<Collider>>();
            bool inSafeZone = false;
            
            try
            {
                GamePhysics.OverlapSphere(position, 1f, colliders, 1 << 18, QueryTriggerInteraction.Collide);
                
                foreach (var collider in colliders)
                {
                    if (collider != null && collider.GetComponent<TriggerSafeZone>() != null)
                    {
                        inSafeZone = true;
                        break;
                    }
                }
            }
            finally
            {
                Facepunch.Pool.FreeUnmanaged(ref colliders);
            }
            
            return inSafeZone;
        }

        private bool IsNearCollider(BaseEntity entity, bool destroy = false)
        {
            LayerMask layerMask = (1 << 20) | LayerMask.GetMask("Default", "Construction", "Tree", "Deployed");
            Collider[] hitColliders = Physics.OverlapSphere(entity.transform.position, config.options.proximityRadius, layerMask, QueryTriggerInteraction.UseGlobal);
            
            bool didHit = false;
            
            foreach (var collider in hitColliders)
            {
                if (collider?.name == null) continue;
                
                if (ignoredColliders.Contains(collider.name.ToLower()))
                    continue;
                
                //BaseEntity hitEntity = hit.collider?.ToBaseEntity(); ??
                var hitEntity = collider.GetComponentInParent<BaseEntity>();
                if (hitEntity == null || hitEntity == entity || hitEntity is BradleyAPC || hitEntity is LockedByEntCrate)
                    continue;

                didHit = true;
                
                if (destroy)
                {
                    if (config.options.clearTimedExplosives && hitEntity is TimedExplosive)
                    {
                        hitEntity.Kill();
                    }
                    else if (config.options.clearLandingZone)
                    {
                        hitEntity.Kill();
                    }
                }
                else
                {
                    return true;
                }
            }
            
            return didHit;
        }

        private bool IsOnFoundation(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return false;

            Vector3 startPosition = entity.transform.position + Vector3.up * 0.1f;
            Vector3 endPosition = startPosition + Vector3.down * 10f;

            RaycastHit[] hits = Physics.RaycastAll(
                startPosition,
                Vector3.down,
                10f,
                Layers.Mask.Construction,
                QueryTriggerInteraction.Ignore
            );

            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                BaseEntity hitEntity = hit.collider?.ToBaseEntity();

                if (hitEntity == null || hitEntity == entity)
                    continue;

                BuildingBlock buildingBlock = hitEntity as BuildingBlock;

                bool isFoundation =
                    buildingBlock != null &&
                    buildingBlock.blockDefinition != null &&
                    buildingBlock.blockDefinition.hierachyName.Contains("foundation");

                return isFoundation;
            }

            return false;
        }

        private void GiveDeployedKit(NPCPlayer npc, string apcProfile)
        {
            if (!npc || npc.IsDestroyed)
                return;

            if (string.IsNullOrEmpty(apcProfile))
            {
                Log($"APC profile is null or empty for {npc}.", LogLevel.DEBUG);
                return;
            }

            if (config?.bradley?.apcConfig == null)
            {
                Log("Bradley APC configuration is null.", LogLevel.DEBUG);
                return;
            }

            if (!config.bradley.apcConfig.TryGetValue(apcProfile, out var profile) || profile == null)
            {
                Log($"APC profile \"{apcProfile}\" does not exist or is null.", LogLevel.DEBUG);
                return;
            }

            List<string> npcKits;

            switch (npc.ShortPrefabName)
            {
                case "scientistnpc_bradley_heavy":
                    npcKits = profile.BradleyScientists?.HeavyKits;
                    break;

                case "scientistnpc_bradley":
                    npcKits = profile.BradleyScientists?.ScientistKits;
                    break;

                case "scientistnpc_ch47_gunner":
                    npcKits = profile.CH47?.ScientistKits;
                    break;

                default:
                    return;
            }

            if (npcKits == null || npcKits.Count == 0)
            {
                Log($"Kit list is null or empty; using default kit for {npc}.", LogLevel.DEBUG);
                return;
            }

            string kit = npcKits[Random.Range(0, npcKits.Count)];

            if (string.IsNullOrEmpty(kit))
            {
                Log($"Selected kit name is null or empty; using default kit for {npc}.", LogLevel.DEBUG);
                return;
            }

            if (Kits?.CallHook("GetKitInfo", kit) == null)
            {
                Log($"Kit \"{kit}\" does not exist; using default kit for {npc}.", LogLevel.DEBUG);
                return;
            }

            if (!npc || npc.IsDestroyed || npc.inventory == null)
            {
                Log($"NPC or NPC inventory is no longer valid while applying kit \"{kit}\".", LogLevel.DEBUG);
                return;
            }

            npc.inventory.Strip();
            Kits.Call("GiveKit", npc, kit, false);
        }

        private void UnregisterBuildBlockZone(BradleyBuildBlockZone zone)
        {
            if (zone != null)
                activeBuildBlockZones.Remove(zone);
        }

        private bool IsBuildBlockPrefabAllowed(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
                return false;

            List<string> allowedPrefabs = config.bradley.buildBlocking.NoBlockPrefabs;

            if (allowedPrefabs == null || allowedPrefabs.Count == 0)
                return false;

            if (allowedPrefabs.Contains(prefabPath))
                return true;

            return false;
        }

        private bool IsInsideBradleyBuildBlockZone(Vector3 position)
        {
            if (activeBuildBlockZones == null || activeBuildBlockZones.Count == 0)
                return false;

            var _buildBlocking = config.bradley.buildBlocking;
            if (_buildBlocking == null)
                return false;

            float configuredRadius = Mathf.Max(0f, _buildBlocking.BuildBlockZoneRadius);

            if (configuredRadius <= 0f)
                return false;

            float radiusSquared = configuredRadius * configuredRadius;

            foreach (BradleyBuildBlockZone zone in activeBuildBlockZones)
            {
                if (zone == null)
                    continue;

                BaseEntity entity = zone.entity;

                if (entity == null || entity.IsDestroyed)
                    continue;

                Vector3 centre = zone.centre;

                if (entity is BradleyAPC)
                {
                    var bradComp = zone.bradComp;
                    if (bradComp == null)
                        continue;

                    if (_buildBlocking.BuildBlockZoneFollowsBradley)
                        centre = bradComp.hasLanded ? entity.transform.position : zone.centre;
                }

                float deltaX = position.x - centre.x;
                float deltaZ = position.z - centre.z;

                float distanceSquared = deltaX * deltaX + deltaZ * deltaZ;

                if (distanceSquared <= radiusSquared)
                    return true;
            }

            return false;
        }

        private void RunHeliDeathCommands(APCData.CommandsToRunOptions commandOptions, ulong ownerId, string apcProfile, BradleyDrop apcComp)
        {
            if (commandOptions?.RunCommands != true || commandOptions.Commands == null || commandOptions.Commands.Count == 0)
                return;

            string ownerIdString = ownerId.ToString();
            string profileName = apcProfile ?? string.Empty;

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
                        apcComp?.GetCallingTeam();

                        callingTeam = new List<BasePlayer>();
                        HashSet<ulong> addedPlayers = new HashSet<ulong>();

                        if (apcComp?.callingTeam != null)
                        {
                            foreach (BasePlayer player in apcComp.callingTeam)
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
                        string teamCommand = command.Replace("{ownerId}", ownerIdString).Replace("{teamIds}", teamMember.UserIDString).Replace("{apcProfile}", profileName);
                        RunServerCommand(teamCommand);
                    }

                    continue;
                }

                // Commands without {teamIds} run once
                command = command.Replace("{ownerId}", ownerIdString).Replace("{apcProfile}", profileName);
                RunServerCommand(command);
            }
        }

        private void RunServerCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                Log($"Running Bradley death command: {command}", LogLevel.DEBUG);
                Server.Command(command);
            }
            catch (Exception ex)
            {
                Log($"Failed to run Bradley death command '{command}': {ex.Message}", LogLevel.ERROR);
            }
        }

        #endregion Helpers
        
        #region Harmony Patch Helpers

        public static BradleyDrop GetCachedBradleyDrop(BradleyAPC bradley)
        {
            if (bradley == null)
                return null;
            
            if (!bradCompCache.TryGetValue(bradley, out var bradComp))
            {
                bradComp = bradley.GetComponent<BradleyDrop>();
                bradCompCache[bradley] = bradComp;
            }
            return bradComp;
        }

        public static CH47DropComponent GetCachedCH47DropComponent(CH47HelicopterAIController ch47Ai)
        {
            if (ch47Ai == null)
                return null;
            
            if (!ch47CompCache.TryGetValue(ch47Ai, out var ch47Comp))
            {
                ch47Comp = ch47Ai.GetComponent<CH47DropComponent>();
                ch47CompCache[ch47Ai] = ch47Comp;
            }
            return ch47Comp;
        }

        private void ApplyHarmonyPatches()
        {
            try
            {
                harmony = new Harmony(HarmonyPatchId);

                _doWeaponAimingMethod = AccessTools.Method(typeof(BradleyAPC), "DoWeaponAiming");
                _doWeaponsMethod = AccessTools.Method(typeof(BradleyAPC), "DoWeapons");
                _killSpawnedScientistsMethod = AccessTools.Method(typeof(BradleyAPC), "KillSpawnedScientists");
                _calculateDesiredAltitudeMethod = AccessTools.Method(typeof(CH47HelicopterAIController), "CalculateDesiredAltitude");
                _refreshDecayMethod = AccessTools.Method(typeof(HackableLockedCrate), "RefreshDecay");

                int patchCount = 0;

                if (_doWeaponAimingMethod != null)
                {
                    harmony.Patch(_doWeaponAimingMethod, prefix: new HarmonyMethod(typeof(BradleyDoWeaponAimingPatch), "Prefix"));
                    
                    Log($"✓ Patched: BradleyAPC.DoWeaponAiming", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("BradleyAPC.DoWeaponAiming method not found!", LogLevel.WARN);
                }
                
                if (_doWeaponsMethod != null)
                {
                    harmony.Patch(_doWeaponsMethod, prefix: new HarmonyMethod(typeof(BradleyDoWeaponsPatch), "Prefix"));
                    
                    Log($"✓ Patched: BradleyAPC.DoWeapons", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("BradleyAPC.DoWeapons method not found!", LogLevel.WARN);
                }

                if (_killSpawnedScientistsMethod != null)
                {
                    harmony.Patch(_killSpawnedScientistsMethod, prefix: new HarmonyMethod(typeof(BradleyKillSpawnedScientistsPatch), "Prefix"));
                    
                    Log($"✓ Patched: BradleyAPC.KillSpawnedScientists", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("BradleyAPC.KillSpawnedScientists method not found!", LogLevel.WARN);
                }

                if (_calculateDesiredAltitudeMethod != null)
                {
                    harmony.Patch(_calculateDesiredAltitudeMethod, prefix: new HarmonyMethod(typeof(CH47CalculateDesiredAltitudePatch), "Prefix"));
                    
                    Log($"✓ Patched: CH47HelicopterAIController.CalculateDesiredAltitude", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("CH47HelicopterAIController.CalculateDesiredAltitude method not found!", LogLevel.WARN);
                }
                
                if (_refreshDecayMethod != null)
                {
                    harmony.Patch(_refreshDecayMethod, prefix: new HarmonyMethod(typeof(HackableLockedCrateRefreshDecayPatch), "Prefix"));
                    
                    Log($"✓ Patched: HackableLockedCrate.RefreshDecay", LogLevel.DEBUG);
                    patchCount++;
                }
                else
                {
                    Log("HackableLockedCrate.RefreshDecay method not found!", LogLevel.WARN);
                }

                Log($"Applied {patchCount} Harmony patches!", LogLevel.DEBUG);
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
            string activeApcs = String.Empty;
            int count = 0;
            int total = BradleyDropData.Count;

            if (total == 0)
            {
                Message(player, "ApcReportTitleCon", "NO");
                return;
            }

            if (player.IsServer)
                Message(player, "ApcReportTitleCon", total);
            else
                Message(player, "ApcReportTitle", total);

            foreach (var netId in BradleyDropData.Keys)
            {
                var bradley = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BradleyAPC;
                if (bradley != null)
                {
                    if (!IsOwnerOrFriend(bradley.OwnerID, UInt64.Parse(player.Id)) && !permission.UserHasPermission(player.Id, permAdmin))
                        continue;
                    
                    count++;

                    Vector3 position = bradley.transform.position;
                    var gridPos = PositionToGrid(position);
                    if (gridPos == null)
                        gridPos = "Unknown";

                    BradleyDrop bradComp = bradley.GetComponent<BradleyDrop>();
                    if (bradComp == null)
                        continue;

                    BasePlayer owner = bradComp.owner;
                    if (owner == null)
                        continue;
                    
                    string apcProfile = bradComp.apcProfile;
                    if (apcProfile == null)
                        continue;

                    var message = String.Empty;
                    if (player.IsServer)
                        message = PlayerLang("ApcReportItemCon", player.Id, count, total, config.bradley.apcConfig[apcProfile].Init.APCName, owner.displayName, gridPos, Math.Round((decimal)bradley.health, 0));
                    else
                        message = PlayerLang("ApcReportItem", player.Id, count, total, config.bradley.apcConfig[apcProfile].Init.APCName, owner.displayName, gridPos, Math.Round((decimal)bradley.health, 0));
                    
                    activeApcs += ($"{message}");
                    message = String.Empty;
                }
            }

            if (config.options.usePrefix)
            {
                config.options.usePrefix = false;
                Message(player, "ApcReportList", activeApcs);
                config.options.usePrefix = true;
            }
            else
            {
                Message(player, "ApcReportList", activeApcs);
            }
            activeApcs = String.Empty;
        }

        private void CmdDespawnApc(IPlayer player, string command, string[] args)
        {
            if (player.IsServer)
            {
                Message(player, "InGameOnly");
                return;
            }

            bool didDespawn = false;
            foreach (var netId in BradleyDropData.Keys)
            {
                var bradley = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BradleyAPC;
                if (bradley == null || bradley.IsDestroyed)
                {
                    NextTick (()=> BradleyDropData.Remove(netId));
                    continue;
                }

                BradleyDrop bradComp = bradley.GetComponent<BradleyDrop>();
                if (bradComp == null)
                    continue;

                BasePlayer basePlayer = bradComp.owner;
                if (basePlayer == null) return;

                string apcProfile = bradComp.apcProfile;
                if (apcProfile == null)
                    continue;

                if (
                    bradley.OwnerID == basePlayer.userID ||
                    (config.bradley.canTeamDespawn && 
                    IsOwnerOrFriend(bradley.OwnerID, basePlayer.userID)))
                {
                    didDespawn = true;
                    BasePlayer retirePlayer = player.Object as BasePlayer;
                    DespawnAPC(bradley, bradComp.apcProfile, 1f, retirePlayer);
                }
            }

            if (didDespawn)
                Message(player, "DespawnedBradleys");
            else
                Message(player, "NoDespawnedBradleys");
        }

        private void CmdBuyDrop(IPlayer player, string command, string[] args)
        {
            if (player.IsServer)
            {
                Message(player, "InGameOnly");
                return;
            }
            else if (args?.Length < 1 || args?.Length > 1)
            {
                string buyApcs = String.Empty;
                foreach (var key in config.bradley.apcConfig.Keys)
                {
                    if (config.bradley.apcConfig[key].Init.UseBuyCommand)
                        buyApcs += $"{config.purchasing.buyCommand} {config.bradley.apcConfig[key].Init.GiveItemCommand}\n";
                }
                buyApcs += ($"<color=green>-----------------</color>\n");

                string buyWaves = String.Empty;
                foreach (var key in config.bradley.waveConfig.Keys)
                {
                    if (config.bradley.waveConfig[key].UseBuyCommand)
                        buyWaves += $"{config.purchasing.buyCommand} {config.bradley.waveConfig[key].GiveItemCommand}\n";
                }

                Message(player, "BuyCmdSyntax", buyApcs, buyWaves);
                return;
            }
            
            string currencyItem = config.purchasing.defaultCurrency;
            string priceFormat;
            string priceUnit = config.purchasing.purchaseUnit;

            if (args?[0].ToLower() == "list")
            {
                string list = String.Empty;
                foreach (var key in config.bradley.apcConfig.Keys)
                {
                    switch (currencyItem)
                    {
                        case "ServerRewards":
                            {
                                priceFormat = $"{config.bradley.apcConfig[key].Init.CostToBuy} {priceUnit}";
                            }
                            break;
                        case "Economics":
                            {
                                priceFormat = $"{priceUnit}{config.bradley.apcConfig[key].Init.CostToBuy}";
                            }
                            break;
                        default:
                            {
                                priceFormat = $"{config.bradley.apcConfig[key].Init.CostToBuy} {config.purchasing.customCurrency[0].DisplayName}";
                            }
                            break;
                    }
                    list += ($"{config.bradley.apcConfig[key].Init.APCName} : {priceFormat}\n");
                }

                list += ($"<color=green>-----------------</color>\n");

                foreach (var key in config.bradley.waveConfig.Keys)
                {
                    switch (currencyItem)
                    {
                        case "ServerRewards":
                            {
                                priceFormat = $"{config.bradley.waveConfig[key].CostToBuy} {priceUnit}";
                            }
                            break;
                        case "Economics":
                            {
                                priceFormat = $"{priceUnit}{config.bradley.waveConfig[key].CostToBuy}";
                            }
                            break;
                        default:
                            {
                                priceFormat = $"{config.bradley.waveConfig[key].CostToBuy} {config.purchasing.customCurrency[0].DisplayName}";
                            }
                            break;
                    }
                    if (config.bradley.waveConfig[key].UseBuyCommand) list += ($"{key} : {priceFormat}\n");
                }

                Message(player, "PriceList", list);
                return;
            }

            string type = args[0].ToLower();
            ulong skinId = 0;
            string apcProfile = string.Empty;
            bool isWaveBradley = false;

            if (!ApcProfiles.Contains(type) && !WaveProfiles.Contains(type))
            {
                Message(player, "InvalidDrop", type);
                return;
            }

            if (!permission.UserHasPermission(player.Id, permBuy))
            {
                Message(player, "BuyPermission", type);
                return;
            }
            
            foreach (var key in config.bradley.apcConfig.Keys)
            {
                if (type == config.bradley.apcConfig[key].Init.GiveItemCommand.ToLower())
                {
                    skinId = config.bradley.apcConfig[key].Init.SignalSkinID;
                    apcProfile = key;
                    break;
                }
            }
            foreach (var key in config.bradley.waveConfig.Keys)
            {
                if (type == config.bradley.waveConfig[key].GiveItemCommand.ToLower())
                {
                    skinId = config.bradley.waveConfig[key].SkinId;
                    apcProfile = key;
                    isWaveBradley = true;
                    break;
                }
            }

            if (isWaveBradley && !config.bradley.waveConfig[apcProfile].UseBuyCommand)
            {
                Message(player, "NoBuy", type);
                return;
            }
            else if (!isWaveBradley && !config.bradley.apcConfig[apcProfile].Init.UseBuyCommand)
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
                        var cost = isWaveBradley ? config.bradley.waveConfig[apcProfile].CostToBuy : config.bradley.apcConfig[apcProfile].Init.CostToBuy;
                        var balance = ServerRewards?.CallHook("CheckPoints", basePlayer.userID);
                        if (balance != null)
                        {
                            if (cost <= Convert.ToInt32(balance))
                            {
                                if (GiveBradleyDrop(basePlayer, skinId, apcProfile, 1, "give"))
                                {
                                    ServerRewards?.CallHook("TakePoints", basePlayer.userID, cost);
                                    Message(player, "Receive", 1, apcProfile);
                                    return;
                                }
                                else
                                {
                                    Message(player, "FullInventory", apcProfile);
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
                        var cost = Convert.ToDouble(isWaveBradley ? config.bradley.waveConfig[apcProfile].CostToBuy : config.bradley.apcConfig[apcProfile].Init.CostToBuy);
                        var balance = Economics?.CallHook("Balance", basePlayer.userID);
                        if (balance != null)
                        {
                            if (cost <= Convert.ToDouble(balance))
                            {
                                if (GiveBradleyDrop(basePlayer, skinId, apcProfile, 1, "give"))
                                {
                                    Economics?.CallHook("Withdraw", basePlayer.userID, cost);
                                    Message(player, "Receive", 1, apcProfile);
                                    return;
                                }
                                else
                                {
                                    Message(player, "FullInventory", apcProfile);
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
                        var shortName = config.purchasing.customCurrency[0].ShortName;
                        var displayName = config.purchasing.customCurrency[0].DisplayName;
                        var currencySkin = config.purchasing.customCurrency[0].SkinId;
                        var cost = isWaveBradley ? config.bradley.waveConfig[apcProfile].CostToBuy : config.bradley.apcConfig[apcProfile].Init.CostToBuy;
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
                                        if (GiveBradleyDrop(basePlayer, skinId, apcProfile, 1, "give"))
                                        {
                                            basePlayer.inventory.Take(null, itemDef.itemid, cost);
                                            Message(player, "Receive", 1, apcProfile);
                                        }
                                        else
                                        {
                                            Message(player, "FullInventory", apcProfile);
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

        private void CmdGiveDrop(IPlayer player, string command, string[] args)
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

            var target = FindPlayer(args[1])?.Object as BasePlayer;
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
            string apcProfile = string.Empty;
            foreach (var item in config.bradley.apcConfig.Keys)
            {
                if (type == config.bradley.apcConfig[item].Init.GiveItemCommand.ToLower())
                {
                    skinId = config.bradley.apcConfig[item].Init.SignalSkinID;
                    apcProfile = item;
                    break;
                }
            }
            foreach (var item in config.bradley.waveConfig.Keys)
            {
                if (type == config.bradley.waveConfig[item].GiveItemCommand.ToLower())
                {
                    skinId = config.bradley.waveConfig[item].SkinId;
                    apcProfile = item;
                    break;
                }
            }

            if (skinId == 0)
            {
                Message(player, "InvalidDrop", type);
                return;
            }

            if (GiveBradleyDrop(target, skinId, apcProfile, dropAmount, "give"))
            {
                Message(target, "Receive", dropAmount, apcProfile);
                Message(player, "PlayerReceive", target.displayName, target.userID, dropAmount, apcProfile);
            }
            else
            {
                Message(player, "FullInventory", apcProfile);
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
            var target = FindPlayer(args[0])?.Object as BasePlayer;
            if (target == null)
            {
                Message(player, "PlayerNotFound", args[0]);
                return;
            }

            var playerId = target.userID;
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

        #region Monos etc

        private class BradleySignalComponent : MonoBehaviour
        {
            public SupplySignal signal;
            public BasePlayer player;
            public Vector3 position;
            public ulong skinId;
            public string apcProfile;
            public bool isWaveBradley;
            public string waveProfile;
            public ulong waveSkinId;
            public float waveTime;
            public List<string> waveProfileCache;
            public CH47Helicopter ch47;
            public CH47HelicopterAIController ch47Ai;
            public CH47AIBrain ch47Brain;
            public CH47DropComponent ch47Comp;
            public CargoPlane plane;
            public BradleyDropPlane planeComp;
            public BradleyAPC bradley;
            public APCData _apcProfile;
            private BradleyDrop bradComp;

            void Start()
            {
                _apcProfile = config.bradley.apcConfig[apcProfile];
                Invoke(nameof(CustomExplode), config.options.signalFuseLength);
            }

            public void CustomExplode()
            {
                if (signal == null || player == null || Instance == null)
                    return;
                
                position = signal.transform.position;
                var playerId = player.userID;
                
                if (DropAborted())
                {
                    signal.Kill();

                    if (player != null && player.IsAlive())
                    {
                        ulong refundSkinId = isWaveBradley ? waveSkinId : skinId;
                        Instance.NextTick(() => Instance.GiveBradleyDrop(player, refundSkinId, apcProfile, 1, "refund"));
                        
                        if (config.bradley.playerCooldown > 0f)
                        {
                            if (PlayerCooldowns.ContainsKey(playerId))
                                PlayerCooldowns.Remove(playerId);
                            
                            if (TierCooldowns.ContainsKey(playerId))
                                TierCooldowns.Remove(playerId);
                        }
                    }
                    return;
                }

                float finishUp = config.options.smokeDuration > 0 ? config.options.smokeDuration : 210f;
                signal.Invoke(new Action(signal.FinishUp), finishUp);
                signal.SetFlagLocal(BaseEntity.Flags.On, true);
                signal.SendNetworkUpdateImmediate();
                
                HandleAnnouncements();
                HandleDiscordNotification();
                InitializeWaveTracking();
                
                var deliveryMethod = config.options.deliveryMethod.ToLower();
                switch (deliveryMethod)
                {
                    case "ch47":
                        HandleCH47Delivery();
                        break;
                    case "balloon" when config.plane.skipCargoPlane:
                        HandleBalloonDelivery();
                        break;
                    default:
                        HandleCargoPlaneDelivery();
                        break;
                }
                
                HandleWaveFirstBradleyMessage();
                DestroyImmediate(this);
            }

            private void HandleAnnouncements()
            {
                if (!config.announce.callChat || Instance == null)
                    return;
                
                var gridPos = Instance.PositionToGrid(position) ?? "Unknown";
                var apcName = isWaveBradley ? waveProfile : _apcProfile.Init.APCName;
                var message = Instance.PlayerLang("BradleyCalled", player.UserIDString, player.displayName, apcName, gridPos);
                
                var attackers = Instance.GetAttackingTeam(player);
                if (attackers?.Count > 0)
                {
                    Instance.AnnounceToChat(player, attackers, message);
                }
            }

            private void HandleDiscordNotification()
            {
                if (!config.discord.sendApcCall || Instance == null)
                    return;
                
                var discordData = BuildDiscordEmbedData();
                Instance.SendDiscordEmbed(discordData.Title, discordData.Description, discordData.Color, discordData.Footer, discordData.Fields);
            }

            private DiscordEmbedData BuildDiscordEmbedData()
            {
                string steamUrl = steamProfileUrl + player.userID;
                string displayNameLink = $"[{player.displayName}]({steamUrl})";
                string steamIdLink = $"[{player.UserIDString}]({steamUrl})";
                
                var gridPos = Instance.PositionToGrid(position) ?? "Unknown";
                var desc = Instance.Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                var footer = Instance.Lang("DiscordEmbedFooter", string.Empty) + $"  |  {zeodeFooterUrl}";
                
                string title, embedApcName, health, despawn;
                
                if (isWaveBradley)
                {
                    title = Instance.Lang("DiscordEmbedTitleCalled", waveProfile);
                    embedApcName = BuildWaveBradleyNames();
                    health = config.bradley.apcConfig[waveProfileCache[0]].Init.Health.ToString();
                    despawn = (config.bradley.apcConfig[waveProfileCache[0]].Init.DespawnTime / 60f).ToString();
                }
                else
                {
                    title = Instance.Lang("DiscordEmbedTitleCalled", _apcProfile.Init.APCName);
                    embedApcName = _apcProfile.Init.APCName;
                    health = _apcProfile.Init.Health.ToString();
                    despawn = (_apcProfile.Init.DespawnTime / 60f).ToString();
                }
                
                var fields = new List<DiscordField>
                {
                    new DiscordField { Name = Instance.Lang("DiscordCalledLocation", string.Empty), Value = gridPos, Inline = true },
                    new DiscordField { Name = Instance.Lang("DiscordCalledHealth", string.Empty), Value = health, Inline = true },
                    new DiscordField { Name = Instance.Lang("DiscordCalledDespawn", string.Empty), Value = $"{despawn} mins", Inline = true }
                };
                
                if (isWaveBradley)
                {
                    fields.Add(new DiscordField { Name = Instance.Lang("DiscordWaveBradleys", string.Empty), Value = embedApcName, Inline = false });
                }
                
                return new DiscordEmbedData
                {
                    Title = title,
                    Description = desc,
                    Color = "#008000",
                    Footer = footer,
                    Fields = fields
                };
            }

            private string BuildWaveBradleyNames()
            {
                var bradleyNames = new List<string>();
                foreach (var profile in waveProfileCache)
                {
                    if (config.bradley.apcConfig.ContainsKey(profile))
                    {
                        bradleyNames.Add(config.bradley.apcConfig[profile].Init.APCName);
                    }
                }
                return string.Join(",\n", bradleyNames);
            }

            private void InitializeWaveTracking()
            {
                waveTime = Time.realtimeSinceStartup;
                if (isWaveBradley)
                {
                    WavesCalled.Add(waveTime, new List<ulong>());
                }
            }

            private void HandleCH47Delivery()
            {
                ch47 = CreateCH47();
                if (ch47 == null)
                    return;
                
                bradley = CreateBradleyForCH47();
                if (bradley == null)
                    return;
                
                SetupBradleyComponent();
                SetupCH47Component();
            }

            private CH47Helicopter CreateCH47()
            {
                float size = TerrainMeta.Size.x * config.ch47.mapScaleDistance;
                Vector2 rand = Random.insideUnitCircle.normalized;
                Vector3 pos = new Vector3(rand.x, 0, rand.y) * size;
                pos += position + new Vector3(0f, config.ch47.ch47SpawnHeight, 0f);
                
                ch47 = GameManager.server.CreateEntity(ch47Prefab, pos, new Quaternion(), true) as CH47Helicopter;
                if (ch47 == null) 
                    return null;
                
                ch47Ai = ch47.GetComponent<CH47HelicopterAIController>();
                ch47Brain = ch47.GetComponent<CH47AIBrain>();
                
                if (ch47Ai == null || ch47Brain == null)
                    return null;
                
                ConfigureCH47();
                CH47List.Add(ch47);
                
                return ch47;
            }

            private void ConfigureCH47()
            {
                if (ch47 == null || ch47Ai == null || ch47Brain == null)
                    return;
                
                ch47.OwnerID = player.userID;
                ch47.skinID = skinId;
                ch47.EnableGlobalBroadcast(true);
                ch47.Spawn();

                ch47._maxHealth = _apcProfile.CH47.StartHealth;
                ch47.health = ch47._maxHealth;
                ch47.InitializeHealth(ch47._maxHealth, ch47._maxHealth);

                ch47Ai.CancelInvoke(ch47Ai.CheckSpawnScientists);
                ch47Ai.OwnerID = player.userID;
                ch47Ai.skinID = skinId;
                ch47Ai.SetAimDirection(position);
                ch47Ai.SetDropDoorOpen(true);
                ch47Ai.SetMinHoverHeight(config.ch47.minHoverHeight);
                ch47Ai.SetLandingTarget(position);
                ch47Ai.numCrates = 0;
                
                ch47Brain.mainInterestPoint = position;

                ch47.UpdateNetworkGroup();
                ch47.SendNetworkUpdateImmediate();
            }

            private BradleyAPC CreateBradleyForCH47()
            {
                bradley = GameManager.server.CreateEntity(bradleyPrefab, ch47.transform.position, ch47.transform.rotation, false) as BradleyAPC;
                if (bradley == null)
                    return null;
                
                ulong bradleySkinId = isWaveBradley ? _apcProfile.Init.SignalSkinID : skinId;
                
                bradley.OwnerID = player.userID;
                bradley.skinID = skinId;
                bradley._name = apcProfile;
                bradley.EnableGlobalBroadcast(true);
                bradley.myRigidBody.detectCollisions = false;
                bradley.myRigidBody.isKinematic = true;
                bradley.Spawn();
                bradley.SetParent(ch47);
                bradley.transform.localPosition = new Vector3(0f, -3.25f, 0f);
                bradley.gameObject.AwakeFromInstantiate();

                bradley.UpdateNetworkGroup();
                bradley.SendNetworkUpdateImmediate();
                
                TrackBradleyInWave();
                return bradley;
            }

            private void HandleBalloonDelivery()
            {
                bradley = CreateBradleyForBalloon();
                if (bradley == null)
                    return;
                
                SetupBradleyComponent();
            }

            private BradleyAPC CreateBradleyForBalloon()
            {
                var spawnPos = GetHighAltitudeSpawnPosition();
                bradley = GameManager.server.CreateEntity(bradleyPrefab, spawnPos, new Quaternion(), true) as BradleyAPC;
                if (bradley == null)
                    return null;
                
                bradley.OwnerID = player.userID;
                bradley.skinID = skinId;
                bradley._name = apcProfile;
                bradley.Spawn();
                
                TrackBradleyInWave();
                return bradley;
            }

            private void HandleCargoPlaneDelivery()
            {
                plane = CreateCargoPlane();
                if (plane == null)
                    return;
                
                SetupCargoPlaneComponent();
            }

            private CargoPlane CreateCargoPlane()
            {
                var spawnPos = GetHighAltitudeSpawnPosition();
                plane = GameManager.server.CreateEntity(planePrefab, spawnPos, new Quaternion(), true) as CargoPlane;
                
                plane.OwnerID = player.userID;
                plane.skinID = signal.skinID;
                plane._name = apcProfile;
                plane.SendMessage("InitDropPosition", spawnPos, SendMessageOptions.DontRequireReceiver);
                plane.Spawn();
                
                CargoPlaneList.Add(plane);
                return plane;
            }

            private Vector3 GetHighAltitudeSpawnPosition()
            {
                float highestPoint = TerrainMeta.HighestPoint.y;
                Vector3 spawnPos = position;
                spawnPos.y = highestPoint + config.plane.planeHeight;
                return spawnPos;
            }

            private void SetupBradleyComponent()
            {
                if (bradley == null)
                    return;
                
                bradComp = bradley.gameObject.AddComponent<BradleyDrop>();
                if (bradComp == null)
                    return;
                
                ConfigureBradleyComponent();
                AddBradleyToDropData();
                
                if (_apcProfile.Init.DespawnTime > 0)
                {
                    Instance.DespawnAPC(bradley, apcProfile, _apcProfile.Init.DespawnTime);
                }
            }

            private void ConfigureBradleyComponent()
            {
                if (player == null || signal == null || bradley == null)
                    return;
                
                bradComp.owner = player;
                bradComp.bradley = bradley;
                bradComp.apcProfile = apcProfile;
                bradComp.position = position;
                bradComp.skinId = isWaveBradley ? _apcProfile.Init.SignalSkinID : skinId;
                bradComp.isWaveBradley = isWaveBradley;
                bradComp.waveProfile = waveProfile;
                bradComp.waveSkinId = waveSkinId;
                bradComp.waveTime = waveTime;
                bradComp.waveProfileCache = waveProfileCache;
                bradComp.atDropZone = true;
                bradComp._apcProfile = _apcProfile;

                if (ch47Ai != null)
                    bradComp.ch47Ai = ch47Ai;
            }

            private void SetupCH47Component()
            {
                if (ch47 == null || ch47Ai == null || bradley == null || ch47Brain == null)
                    return;
                
                ch47Comp = ch47.gameObject.AddComponent<CH47DropComponent>();
                if (ch47Comp == null)
                    return;
                
                ch47Comp.ch47 = ch47;
                ch47Comp.ch47Ai = ch47Ai;
                ch47Comp.ch47Brain = ch47Brain;
                ch47Comp.position = position;
                ch47Comp.bradley = bradley;
                ch47Comp.bradComp = bradComp;
                ch47Comp._apcProfile = _apcProfile;
            }

            private void SetupCargoPlaneComponent()
            {
                if (plane == null || player == null)
                    return;

                planeComp = plane.gameObject.AddComponent<BradleyDropPlane>();
                if (planeComp == null)
                    return;
                
                planeComp.plane = plane;
                planeComp.player = player;
                planeComp.skinId = isWaveBradley ? _apcProfile.Init.SignalSkinID : skinId;
                planeComp.apcProfile = apcProfile;
                planeComp.calledPosition = position;
                planeComp.isWaveBradley = isWaveBradley;
                planeComp.waveProfile = waveProfile;
                planeComp.waveSkinId = waveSkinId;
                planeComp.waveTime = waveTime;
                planeComp.waveProfileCache = waveProfileCache;
                planeComp._apcProfile = _apcProfile;
            }

            private void TrackBradleyInWave()
            {
                if (bradley == null)
                    return;

                var bradleyId = bradley.net.ID.Value;
                if (isWaveBradley)
                {
                    WavesCalled[waveTime].Add(bradleyId);
                }
            }

            private void AddBradleyToDropData()
            {
                if (bradley == null || player == null)
                    return;

                BradleyDropData.Add(bradley.net.ID.Value, new ApcStats
                {
                    Owner = player,
                    OwnerID = player.userID,
                    OwnerName = player.displayName,
                    ParentWaveProfile = isWaveBradley ? waveProfile : null,
                    WavePosition = isWaveBradley ? 1 : 0,
                    WaveTotalCount = isWaveBradley ? config.bradley.waveConfig[waveProfile].WaveProfiles.Count : 0
                });
            }

            private void HandleWaveFirstBradleyMessage()
            {
                if (Instance == null || player == null)
                    return;

                if (!isWaveBradley)
                    return;
                
                var message = Instance.PlayerLang("FirstApcCalled", player.UserIDString, waveProfileCache[0]);
                var attackers = Instance.GetAttackingTeam(player);
                
                if (attackers?.Count > 0)
                {
                    Instance.AnnounceToChat(player, attackers, message);
                }
            }

            private struct DiscordEmbedData
            {
                public string Title;
                public string Description;
                public string Color;
                public string Footer;
                public List<DiscordField> Fields;
            }

            public bool DropAborted()
            {
                if (player == null || !player.IsAlive())
                    return true;

                bool isAdmin = Instance.permission.UserHasPermission(player.UserIDString, permAdmin);
                if (!isAdmin)
                {
                    int globalCount = 0;
                    int playerCount = 0;

                    foreach (var netId in BradleyDropData.Keys)
                    {
                        var bradley = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BradleyAPC;
                        if (bradley == null)
                            continue;

                        globalCount++;

                        if (bradley.OwnerID == player.userID)
                            playerCount++;
                    }

                    if (config.options.deliveryMethod.ToLower() == "balloon" && !config.plane.skipCargoPlane)
                    {
                        foreach (var plane in CargoPlaneList)
                        {
                            if (plane == null || plane.IsDestroyed)
                                continue;
                            
                            globalCount++;

                            if (plane.OwnerID == player.userID)
                                playerCount++;
                        }
                    }

                    if (config.bradley.globalLimit > 0 && globalCount >= config.bradley.globalLimit)
                    {
                        Instance.Message(player, "GlobalLimit", config.bradley.globalLimit);
                        return true;
                    }
                    else if (config.bradley.playerLimit > 0 && playerCount >= config.bradley.playerLimit)
                    {
                        Instance.Message(player, "PlayerLimit", config.bradley.playerLimit);
                        return true;
                    }
                }

                return !IsValidDropLocation();
            }

            private bool IsValidDropLocation()
            {
                if (signal == null || Instance == null)
                    return false;

                var signalPos = signal.transform.position;

                if (_apcProfile.ZoneManager.UseZoneManager && _apcProfile.ZoneManager.UseDropZones)
                {
                    bool isPlayerInZone = false;
                    bool isSignalInZone = false;
                    foreach (var id in _apcProfile.ZoneManager.DropZoneIDs)
                    {
                        isPlayerInZone = (bool)Instance.ZoneManager?.CallHook("IsPlayerInZone", id, player);
                        isSignalInZone = (bool)Instance.ZoneManager?.CallHook("IsEntityInZone", id, signal);
                            
                        if (isPlayerInZone && isSignalInZone)
                            return true;
                        
                        continue;
                    }
                    Instance.Message(player, "NotInZone", apcProfile);
                    return false;
                }
                
                if (Instance.IsInSafeZone(signalPos))
                {
                    Instance.Message(player, "InSafeZone", apcProfile);
                    return false;
                }
                
                if (IsWaterNearPosition(signalPos, _apcProfile.Init.PatrolRange))
                {
                    Instance.Message(player, "TooCloseToWater", apcProfile);
                    return false;
                }
                
                if (!signal.IsOutside())
                {
                    Instance.Message(player, "Inside", apcProfile);
                    return false;
                }

                if (!config.options.allowOnFoundations && Instance.IsOnFoundation(signal))
                {
                    Instance.Message(player, "NotOnGround", apcProfile);
                    return false;
                }

                if (config.options.strictProximity && Instance.IsNearCollider(signal, false))
                {
                    Instance.Message(player, "NearCollider", apcProfile);
                    return false;
                }

                var terrainHeight = TerrainMeta.HeightMap?.GetHeight(signalPos) ?? 0f;
                if (!config.options.allowOnFoundations && (signalPos.y - terrainHeight) > 1f)
                {
                    Instance.Message(player, "NotOnGround", apcProfile);
                    return false;
                }

                if (!config.options.allowOnFoundations && IsInBuildingPriv(signalPos))
                {
                    Instance.Message(player, "BuildingPriv", apcProfile);
                    return false;
                }
                
                if (!IsValidMonumentLocation(signalPos))
                {
                    return false;
                }

                return true;
            }

            // Stop people throwing drops too close to their bases and risk having the APC
            // land on structure and get stuck or inside compound etc
            private bool IsInBuildingPriv(Vector3 position)
            {
                float distance = Mathf.Max(config.options.buildPrivRadius, config.options.proximityRadius);
                
                var entities = Facepunch.Pool.Get<List<BaseEntity>>();
                bool hasPrivilege = false;
                
                try
                {
                    Vis.Entities(position, distance, entities, LayerMask.GetMask("Construction"));
                    
                    foreach (var entity in entities)
                    {
                        if (entity == null || entity.IsDestroyed || !entity.OwnerID.IsSteamId())
                            continue;
                        
                        try
                        {
                            BuildingPrivlidge privilege = entity.GetBuildingPrivilege();
                            if (privilege != null && !privilege.IsDestroyed)
                            {
                                hasPrivilege = true;
                                break;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Instance.Log($"Error checking building privilege for entity {entity.ShortPrefabName}: {ex.Message}", LogLevel.ERROR);
                            continue;
                        }
                    }
                }
                finally
                {
                    Facepunch.Pool.FreeUnmanaged(ref entities);
                }
                
                return hasPrivilege;
            }

            public bool IsWaterNearPosition(Vector3 position, float radius = 20f)
            {
                float waterLevel = WaterSystem.OceanLevel;
                // First check signal position
                float terrainHeight = WaterLevel.GetWaterOrTerrainSurface(position, false, false, null);
                if (waterLevel >= terrainHeight)
                    return true;
                
                // Now check 8 equal points around the radius to make sure it's safe from water
                for (int i = 0; i < 8; i++)
                {
                    float angle = (i * 45f) * Mathf.Deg2Rad;
                    Vector3 checkPoint = position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    float terrainAtPoint = WaterLevel.GetWaterOrTerrainSurface(checkPoint, false, false, null);
                    
                    if (waterLevel >= terrainAtPoint)
                        return true;
                }
                return false;
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
 					
					bool allowMonuments = config.bradley.allowMonuments;
                    float distFromMonuments = config.bradley.distFromMonuments;
                    float dist = monument.Distance(position);
                    
                    if (!allowMonuments)
                    {
                        if (config.bradley.excludedMonuments.Contains(monument.name))
                            return true;

                    	if (!config.bradley.excludedMonuments.Contains(monument.name) && dist < config.bradley.distFromMonuments)
                        {
                            Instance.Message(player, "InNamedMonument", apcProfile, monument.displayPhrase.translated);
                            return false;
                        }
                    }
                    else if (allowMonuments)
                    {
                    	if (config.bradley.excludedMonuments.Contains(monument.name) && dist < distFromMonuments)
                    	{
                            Instance.Message(player, "InNamedMonument", apcProfile, monument.displayPhrase.translated);
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

        public class CH47DropComponent : MonoBehaviour, SeekerTarget.ISeekerTargetOwner
        {
            public Vector3 position;
            public CH47Helicopter ch47;
            public CH47HelicopterAIController ch47Ai;
            public CH47AIBrain ch47Brain;
            public BradleyAPC bradley;
            public BradleyDrop bradComp;
            public bool atDropPosition = false;
            public bool isAtEngagementRange = false;
            public Vector3 lastPosition;
            public bool ch47Retiring = false;
            public float lastUpdateTime;
            public float lastNetworkUpdateTime;
            public APCData _apcProfile;
            public BasePlayer lastAttacker;
            public bool isDying;

            private Coroutine destroyCoroutine;
            private float stuckCheckTime = 0f;
            private bool isCh47Orbiting = false;
            private float lastFlareTime = 0f;

            void Start()
            {
                if (Instance == null || config == null || ch47 == null)
                {
                    Destroy(this);
                    return;
                }

                ch47.UpdateNetworkGroup();
                ch47.SendNetworkUpdateImmediate();

                var startTime = Time.realtimeSinceStartup;
                lastUpdateTime = startTime;
                lastNetworkUpdateTime = startTime;

                if (_apcProfile.CH47.SpawnScientists)
                    SpawnFullCrew();
                else
                    SpawnFlightCrew();

                ch47.skinID = bradley.skinID;
                ch47.OwnerID = bradley.OwnerID;

                RegisterFlareSystem();
            }

            void Update()
            {
                if (Instance == null)
                {
                    CleanupAndDestroy();
                    return;
                }

                UpdateCH47Info();
                UpdateCH47NetworkGroup();
            }

            void UpdateCH47Info()
            {
                if (Instance == null)
                {
                    CleanupAndDestroy();
                    return;
                }

                if (Time.realtimeSinceStartup - lastUpdateTime > 0.25f)
                {
                    lastUpdateTime = Time.realtimeSinceStartup;
                    
                    if (bradley == null || ch47Ai == null || ch47 == null || ch47Brain == null)
                    {
                        if (!ch47Retiring)
                        {
                            ch47Retiring = true;
                            if (ch47Ai != null) ch47Ai.CancelAnger();
                            CH47Egress();
                        }
                        return;
                    }

                    ch47.skinID = bradley.skinID;
                    ch47.OwnerID = bradley.OwnerID;

                    if (!bradley.HasParent() && _apcProfile.CH47.OrbitDropZone && !isCh47Orbiting)
                    {
                        isCh47Orbiting = true;

                        ch47Ai.numCrates = 1;       // Or wont orbit! (Set to 0 on kill so we can control whether crate drops or not)
                        ch47Ai.shouldLand = false;  // Or wont orbit!!
                        
                        ch47Brain.mainInterestPoint = position;
                        ch47Brain.SwitchToState(AIState.Patrol);
                        
                        // Make it think it's at patrol destination
                        ch47Brain.mainInterestPoint = ch47Ai.transform.position;
                        
                        // Now switch to Orbit
                        Instance.timer.Once(0.5f, () => 
                        {
                            ch47Brain.mainInterestPoint = position;
                            ch47Brain.SwitchToState(AIState.Orbit);
                            ch47Ai.EnableFacingOverride(true);
                            ch47Ai.InitiateAnger();

                            ch47Ai.SetAltitudeProtection(true);
                            ch47Ai.SetMinHoverHeight(30f);
                        });

                        Instance.timer.Once(_apcProfile.CH47.OrbitDropZoneDuration, ()=>
                        {
                            ch47Retiring = true;
                            CH47Egress();
                        });
                                            
                        return;
                    }
                    else if (!bradley.HasParent() && !_apcProfile.CH47.OrbitDropZone && !ch47Retiring)
                    {
                        ch47Retiring = true;
                        CH47Egress();
                        return;
                    }
                    
                    if (ch47Retiring)
                    {
                        if (Vector3.Distance(ch47.transform.position, lastPosition) < 0.1f)
                        {
                            stuckCheckTime += 1f;
                            if (stuckCheckTime > 10f)
                            {
                                CH47Egress();
                                stuckCheckTime = 0f;
                            }
                        }
                        else
                        {
                            stuckCheckTime = 0f;
                        }
                        lastPosition = ch47.transform.position;
                        return;
                    }
                    
                    if (!isAtEngagementRange && Vector3Ex.Distance2D(position, ch47.transform.position) < _apcProfile.CH47.EngageRange)
                    {
                        isAtEngagementRange = true;

                        if (_apcProfile.CH47.SpawnScientists && ch47Ai != null) 
                            ch47Ai.InitiateAnger();
                    }
                    
                    if (!atDropPosition && Vector3Ex.Distance2D(position, ch47.transform.position) < 10f)
                    {
                        atDropPosition = true;
                        bradComp.atDropZone = true;

                        if (ch47Ai != null)
                        {
                            Instance.timer.Once(5f, () =>
                            {
                            	ch47Ai.SetMinHoverHeight(5f);
                            	ch47Ai.currentDesiredAltitude = ch47Ai.hoverHeight;
                            });
                        }
                    }
                }
            }

            void UpdateCH47NetworkGroup()
            {
                if (Time.realtimeSinceStartup - lastNetworkUpdateTime > 1f)
                {
                    lastNetworkUpdateTime = Time.realtimeSinceStartup;

                    if (ch47 == null)
                        return;
                    
                    ch47.UpdateNetworkGroup();
                }
            }

            #region Flare System

            private void RegisterFlareSystem()
            {
                if (_apcProfile.CH47.CanUseFlares && ch47 != null)
                    SeekerTarget.SetSeekerTarget(this, SeekerTarget.SeekerStrength.HIGHEST);
            }
            
            public void DoFlare()
            {
                if (ch47 == null || ch47.IsDestroyed)
                    return;
                
                if (Time.realtimeSinceStartup - lastFlareTime > _apcProfile.CH47.FlareCooldown)
                {
                    lastFlareTime = Time.realtimeSinceStartup;
                    SeekerTarget.SetSeekerTarget(this, SeekerTarget.SeekerStrength.OFF);
                    InvokeRepeating(nameof(DoFlareEffect), UnityEngine.Random.Range(0.5f, 1f), 2f);
                    Invoke(nameof(ClearFlares), _apcProfile.CH47.FlareDuration);
                }
            }

            private void DoFlareEffect()
            {
                if (ch47 == null || ch47.IsDestroyed)
                    return;
                
                Effect.server.Run(heliFlares, ch47, 0, Vector3.zero, Vector3.zero, null, false, null);
            }
            
            public void ClearFlares()
            {
                if (ch47 == null || ch47.IsDestroyed)
                    return;
                
                SeekerTarget.SetSeekerTarget(this, SeekerTarget.SeekerStrength.HIGHEST);

                CancelInvoke(nameof(DoFlareEffect));
            }
            
            public Vector3 CenterPoint()
            {
                var point = ch47 != null ? ch47.transform.position + Vector3.up : transform.position + Vector3.up;
                return point;
            }
            
            public bool InSafeZone()
            {
                return ch47 != null ? ch47.InSafeZone() : false;
            }
            
            public bool IsValidHomingTarget()
            {
                bool valid = ch47 != null && !ch47.IsDestroyed && _apcProfile.CH47.IsHomingTarget;
                return valid;
            }
            
            public bool IsVisible(Vector3 position, float maxDistance = float.PositiveInfinity)
            {
                if (ch47 == null || ch47.IsDestroyed)
                    return false;
                
                if (ch47.IsVisible(position, ch47.CenterPoint(), maxDistance))
                {
                    return true;
                }
                if (ch47.IsVisible(position, ch47.ClosestPoint(position), maxDistance))
                {
                    return true;
                }
                return false;
            }
            
            public void OnEntityMessage(BaseEntity from, string msg)
            {                
                if (msg == "RadarLock" && !IsInvoking(nameof(DoFlare)))
                    Invoke(nameof(DoFlare), UnityEngine.Random.Range(0.5f, 1f));
            }

            #endregion Flare System

            public void CustomCalculateDesiredAltitude()
            {
                CustomCalculateOverrideAltitude();
                if (ch47Ai.altOverride > ch47Ai.currentDesiredAltitude)
                {
                    ch47Ai.currentDesiredAltitude = ch47Ai.altOverride;
                    return;
                }
                // Increased descent rate (adjust float value)
                ch47Ai.currentDesiredAltitude = Mathf.MoveTowards(ch47Ai.currentDesiredAltitude, ch47Ai.altOverride, Time.fixedDeltaTime * 8f);
            }

            public float CustomCalculateOverrideAltitude()
            {
                if (Time.frameCount == ch47Ai.lastAltitudeCheckFrame)
                {
                    return ch47Ai.altOverride;
                }

                ch47Ai.lastAltitudeCheckFrame = Time.frameCount;
                
                float moveTarget = position.y;
                float waterOrTerrainSurface = WaterLevel.GetWaterOrTerrainSurface(position, false, false, null);
                float baseAltitude = Mathf.Max(moveTarget, waterOrTerrainSurface + ch47Ai.hoverHeight);
                
                if (!ch47Ai.altitudeProtection)
                {
                    ch47Ai.altOverride = baseAltitude;
                    return baseAltitude;
                }

                float finalAltitude = baseAltitude;
                Vector3 heliPos = ch47.transform.position;
                
                // Sample terrain directly below with multiple rays
                float[] terrainHeights = new float[5];
                Vector3[] samplePoints = new Vector3[]
                {
                    heliPos,
                    heliPos + Vector3.forward * 5f,
                    heliPos + Vector3.back * 5f,
                    heliPos + Vector3.left * 5f,
                    heliPos + Vector3.right * 5f
                };
                
                RaycastHit hit;
                int validSamples = 0;
                float totalHeight = 0f;
                
                for (int i = 0; i < samplePoints.Length; i++)
                {
                    if (Physics.Raycast(samplePoints[i], Vector3.down, out hit, 200f, 1218511105))
                    {
                        terrainHeights[validSamples] = hit.point.y;
                        totalHeight += hit.point.y;
                        validSamples++;
                    }
                }
                
                if (validSamples > 0)
                {
                    // Use weighted average favouring lower terrain
                    float avgHeight = totalHeight / validSamples;
                    float minHeight = Mathf.Min(terrainHeights);
                    
                    // Blend between minimum and average, favouring minimum for steep terrain
                    float terrainFactor = 0.7f; // Adjust to favour lower terrain more/less
                    float targetTerrainHeight = Mathf.Lerp(avgHeight, minHeight, terrainFactor);
                    
                    finalAltitude = Mathf.Max(baseAltitude, targetTerrainHeight + ch47Ai.hoverHeight);
                }
                
                float distanceToTarget = Vector3.Distance(heliPos, position);
                float adaptiveRadius = Mathf.Lerp(4f, 8f, distanceToTarget / 50f); // Use smaller radius when closer
                
                // Check for obstacles in movement direction
                Vector3 velocity = ch47Ai.rigidBody.linearVelocity;
                if (velocity.magnitude > 0.1f)
                {
                    Vector3 checkDirection = velocity.normalized;
                    if (Physics.SphereCast(heliPos, adaptiveRadius, checkDirection, out hit, 10f, 1218511105))
                    {
                        // Only raise alt if obstacle is actually in our path and above current altitude
                        if (hit.point.y > finalAltitude - ch47Ai.hoverHeight)
                            finalAltitude = Mathf.Max(finalAltitude, hit.point.y + ch47Ai.hoverHeight);
                    }
                }
                
                // Ground proximity check with gradient compensation (steep terrain fix, hopefully)
                if (Physics.SphereCast(heliPos, adaptiveRadius * 0.5f, Vector3.down, out hit, heliPos.y, 1218511105))
                {
                    // Calculate gradient
                    Vector3 terrainNormal = hit.normal;
                    float slopeFactor = Vector3.Dot(terrainNormal, Vector3.up);
                    
                    // Allow to get closer on steep slopes
                    float adjustedHoverHeight = ch47Ai.hoverHeight * Mathf.Lerp(0.6f, 1f, slopeFactor);
                    float groundAltitude = hit.point.y + adjustedHoverHeight;
                    
                    if (groundAltitude > finalAltitude)
                        finalAltitude = groundAltitude;
                }
                
                // Using smoothing to prevent jerky altitude changes
                float maxAltitudeChange = Time.fixedDeltaTime * 15f; // Max altitude change per frame
                finalAltitude = Mathf.Clamp(finalAltitude, ch47Ai.altOverride - maxAltitudeChange, ch47Ai.altOverride + maxAltitudeChange);
                
                ch47Ai.altOverride = finalAltitude;
                return finalAltitude;
            }

            void CH47Egress()
            {
                if (ch47Ai == null || ch47Brain == null || ch47 == null)
                {
                    CleanupAndDestroy();
                    return;
                }

                try
                {
                    ch47Brain.SwitchToState(AIState.Egress);
                    ch47Ai.ClearLandingTarget();
                    ch47Ai.EnableFacingOverride(false);
                    Transform transforms = ch47Ai.transform;
                    Rigidbody rigidbody = ch47Ai.rigidBody;
                    
                    if (rigidbody != null && transforms != null)
                    {
                        Vector3 vector3 = (rigidbody.linearVelocity.magnitude < 0.1f ? transforms.forward : rigidbody.linearVelocity.normalized);
                        Vector3 vector31 = Vector3.Cross(Vector3.Cross(transforms.up, vector3), Vector3.up);
                        ch47Brain.mainInterestPoint = transforms.position + (vector31 * 8000f);
                        ch47Brain.mainInterestPoint.y = 100f;
                        ch47Ai.SetMoveTarget(ch47Brain.mainInterestPoint);
                    }
                }
                catch (Exception ex)
                {
                    if (Instance != null)
                        Instance.Log($"Error in CH47Egress: {ex.Message}", LogLevel.ERROR);
                }

                if (destroyCoroutine != null)
                    StopCoroutine(destroyCoroutine);
                    
                destroyCoroutine = StartCoroutine(DestroyAfterDelay(600f));
            }

            void SpawnFlightCrew()
            {
                // fOr da rEaLiSm yo ;)
                if (ch47Ai == null || ch47 == null) return;
                
                try
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 vector3 = ch47.transform.position + (ch47.transform.forward * 10f);
                        Quaternion quaternion = Quaternion.identity;
                        var entity = GameManager.server.CreateEntity(ch47Ai.scientistPrefab.resourcePath, vector3, quaternion, true);
                        if (entity == null) continue;
                        
                        ScientistNPC npc = entity.GetComponent<ScientistNPC>();
                        if (npc == null) continue;
                        
                        npc.skinID = ch47.skinID;
                        npc.OwnerID = ch47.OwnerID;
                        npc.Spawn();
                        npc.CancelInvoke(npc.EquipTest);
                        ch47Ai.AttemptMount(npc, true);
                        npc.Brain.SetEnabled(false);
                        ch47Ai.OnSpawnedHuman(npc);
                    }
                }
                catch { } // Silently fail
            }

            void SpawnFullCrew()
            {
                if (ch47Ai == null || ch47 == null) return;
                
                try
                {
                    // Forward flight crew & gunners
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 vector3 = ch47.transform.position + (ch47.transform.forward * 10f);
                        Quaternion quaternion = Quaternion.identity;
                        var entity = GameManager.server.CreateEntity(ch47Ai.scientistPrefab.resourcePath, vector3, quaternion, true);
                        if (entity == null) continue;
                        
                        ScientistNPC npc = entity.GetComponent<ScientistNPC>();
                        if (npc == null) continue;
                        
                        npc.skinID = ch47.skinID;
                        npc.OwnerID = ch47.OwnerID;
                        npc.Spawn();
                        
                        SetupCh47Scientist(npc);
                        
                        if (i <= 1) npc.CancelInvoke(npc.EquipTest);
                        ch47Ai.AttemptMount(npc, true);
                        npc.Brain.SetEnabled(true);
                        ch47Ai.OnSpawnedHuman(npc);
                    }
                    
                    // Aft gunner
                    Vector3 rearPos = ch47.transform.position - (ch47.transform.forward * 15f);
                    var rearEntity = GameManager.server.CreateEntity(ch47Ai.scientistPrefab.resourcePath, rearPos, Quaternion.identity, true);
                    if (rearEntity != null)
                    {
                        ScientistNPC rearNpc = rearEntity.GetComponent<ScientistNPC>();
                        if (rearNpc != null)
                        {
                            rearNpc.skinID = ch47.skinID;
                            rearNpc.OwnerID = ch47.OwnerID;
                            rearNpc.Spawn();
                            
                            SetupCh47Scientist(rearNpc);
                            
                            ch47Ai.AttemptMount(rearNpc, true);
                            rearNpc.Brain.SetEnabled(true);
                            ch47Ai.OnSpawnedHuman(rearNpc);
                        }
                    }
                }
                catch { } // Silently fail
            }

			void SetupCh47Scientist(ScientistNPC npc)
            {
                if (npc == null || npc.IsDead() || Instance == null)
                    return;

				npc.startHealth = _apcProfile.CH47.ScientistHealth;
				npc.damageScale = _apcProfile.CH47.ScientistDamageScale;
				npc.aimConeScale = _apcProfile.CH47.ScientistAimCone;
				npc.InitializeHealth(npc.startHealth, npc.startHealth);

				if (_apcProfile.CH47.UseCustomKits && BradleyProfileCache.TryGetValue(npc.skinID, out string apcProfile))
                    Instance.NextTick(()=> Instance.GiveDeployedKit(npc, apcProfile));
            }
            
            IEnumerator DestroyAfterDelay(float delay)
            {
                yield return new WaitForSeconds(delay);
                CleanupAndDestroy();
            }

            void CleanupAndDestroy()
            {
                try
                {
                    StopAllCoroutines();
                }
                catch
                {
                    // Component was already destroyed, ignore
                }
                
                // Safely dismount and kill all NPCs before killing CH47
                // So NPC don't fall if CH47 is killed mid-air while retiring etc
                if (ch47 != null && !ch47.IsDestroyed && ch47Ai != null)
                {
                    try
                    {
                        ch47Ai.CancelInvoke();
                        ch47Ai.CancelAnger();
                        
                        var mountPoints = ch47.mountPoints;
                        if (mountPoints != null)
                        {
                            foreach (var mountPoint in mountPoints)
                            {
                                if (mountPoint.mountable == null)
                                {
                                    continue;
                                }

                                BasePlayer mounted = mountPoint.mountable.GetMounted();
                                if (!mounted || !mounted.IsAlive())
                                {
                                    continue;
                                }
                                mounted.Kill();
                            }
                        }
                        
                        // Small delay to let dismounting complete
                        ch47.Invoke(() => { if (ch47 != null && !ch47.IsDestroyed) ch47.Kill(); }, 0.1f);
                    }
                    catch
                    {
                        // If graceful cleanup fails, force kill
                        if (ch47 != null && !ch47.IsDestroyed)
                            ch47.Kill();
                    }
                }

                if (_apcProfile.CH47.CanUseFlares)
                    SeekerTarget.SetSeekerTarget(this, SeekerTarget.SeekerStrength.OFF);
                
                //Destroy(this);
            }

            void OnDestroy()
            {
                StopAllCoroutines();
                
                if (Instance != null && CH47List != null && ch47 != null)
                {
                    if (CH47List.Contains(ch47))
                        CH47List.Remove(ch47);
                }
            }
        }

        public class BradleyDropPlane : MonoBehaviour
        {
            public ulong skinId;
            public BasePlayer player;
            public CargoPlane plane;
            public Vector3 calledPosition;
            public Vector3 dropPosition;
            public string apcProfile;
            public bool isWaveBradley;
            public List<string> waveProfileCache;
            public float waveTime;
            public string waveProfile;
            public ulong waveSkinId;
            public bool dropped;
            public APCData _apcProfile;

            public BradleyBuildBlockZone bradleyBuildBlockZone;

            void Start()
            {
                if (!ValidateComponents())
                {
                    Destroy(this);
                    return;
                }

                dropPosition = plane.dropPosition;
                plane.dropped = true;
                this.dropped = false;

                Invoke(nameof(BradleyDropPosition), 0.1f);

                AddBuildBlockZone();
            }

            void Update()
            {
                if (Instance == null || plane == null || plane.IsDestroyed)
                {
                    Destroy(this);
                }

                plane.secondsTaken += UnityEngine.Time.deltaTime;
                float dropTime = Mathf.InverseLerp(0f, plane.secondsToTake, plane.secondsTaken);
                if (!this.dropped && dropTime >= 0.5f)
                {
                    this.dropped = true;
                    DropBradley();

                    if (bradleyBuildBlockZone != null)
                        Destroy(bradleyBuildBlockZone);
                }
            }

            void AddBuildBlockZone()
            {
                if (!config.bradley.buildBlocking.EnableBuildBlockZone)
                    return;

                if (bradleyBuildBlockZone != null)
                    return;

                bradleyBuildBlockZone = plane.gameObject.AddComponent<BradleyBuildBlockZone>();
                if (bradleyBuildBlockZone == null)
                    return;

                bradleyBuildBlockZone.entity = plane;
                bradleyBuildBlockZone.centre = calledPosition;

                Instance.activeBuildBlockZones.Add(bradleyBuildBlockZone);
            }

            void UnregisterBuildBlockZone()
            {
                if (bradleyBuildBlockZone != null)
                    Instance.activeBuildBlockZones.Remove(bradleyBuildBlockZone);
            }

            private void DropBradley()
            {
                if (Instance == null || plane == null || plane.IsDestroyed)
                {
                    Destroy(this);
                }

                var bradley = GameManager.server.CreateEntity(bradleyPrefab, plane.transform.position, Quaternion.identity) as BradleyAPC;
                if (bradley == null)
                    return;

                var apcConfig = config.bradley.apcConfig[apcProfile];
                var bradleySkinId = isWaveBradley ? apcConfig.Init.SignalSkinID : skinId;

                bradley.OwnerID = player.userID;
                bradley.skinID = bradleySkinId;
                bradley._name = apcProfile;
                bradley.Spawn();

                BradleyDrop bradComp = bradley.gameObject.AddComponent<BradleyDrop>();
                if (bradComp == null)
                    return;

                bradComp.owner = player;
                bradComp.bradley = bradley;
                bradComp.apcProfile = apcProfile;
                bradComp.position = calledPosition;
                bradComp.skinId = bradleySkinId;
                bradComp.isWaveBradley = isWaveBradley;
                bradComp.waveProfile = waveProfile;
                bradComp.waveSkinId = waveSkinId;
                bradComp.waveProfileCache = waveProfileCache;
                bradComp.atDropZone = true;
                bradComp._apcProfile = _apcProfile;

                bradley.SendNetworkUpdateImmediate();

                var waveConfig = isWaveBradley ? config.bradley.waveConfig[waveProfile] : null;
                
                BradleyDropData.Add(bradley.net.ID.Value, new ApcStats
                {
                    Owner = player,
                    OwnerID = player.userID,
                    OwnerName = player.displayName,
                    ParentWaveProfile = isWaveBradley ? waveProfile : null,
                    WavePosition = isWaveBradley ? 1 : 0,  // First Bradley is always position 1
                    WaveTotalCount = isWaveBradley ? waveConfig.WaveProfiles.Count : 0
                });

                if (apcConfig.Init.DespawnTime > 0)
                    Instance.DespawnAPC(bradley, apcProfile, apcConfig.Init.DespawnTime);

                GameManager.Destroy(this);
            }

            bool ValidateComponents()
            {
                if (Instance == null)
                {
                    return false;
                }

                if (plane == null || plane.IsDestroyed)
                {
                    if (Instance != null)
                        Instance.Log("BradleyDropPlane: Cargo plane is null or destroyed", LogLevel.ERROR);
                    
                    return false;
                }

                if (config == null || config.plane == null)
                {
                    if (Instance != null)
                        Instance.Log("BradleyDropPlane: Config is not available", LogLevel.ERROR);
                    
                    return false;
                }

                return true;
            }

            public void BradleyDropPosition()
            {
                if (!ValidateComponents())
                {
                    Destroy(this);
                    return;
                }

                try
                {
                    float mapSize = TerrainMeta.Size.x;
                    float highestPoint = TerrainMeta.HighestPoint.y;
                    if (mapSize <= 0 || float.IsNaN(mapSize))
                    {
                        if (Instance != null)
                            Instance.Log("Invalid map size detected", LogLevel.ERROR);
                        
                        Destroy(this);
                        return;
                    }

                    dropPosition = plane.dropPosition.XZ3D();
                    float flightDistance = mapSize * config.plane.mapScaleDistance;
                    float flightHeight = highestPoint + config.plane.planeHeight;
                    Vector3 flightDirection = GenerateFlightDirection();
                    plane.startPos = dropPosition + (flightDirection * flightDistance);
                    plane.startPos.y = flightHeight;
                    plane.endPos = dropPosition - (flightDirection * flightDistance);
                    plane.endPos.y = flightHeight;

                    float totalDistance = Vector3.Distance(plane.startPos, plane.endPos);
                    plane.secondsToTake = totalDistance / config.plane.planeSpeed;
                    if (plane.secondsToTake <= 0 || float.IsNaN(plane.secondsToTake))
                    {
                        plane.secondsToTake = 60f; // Default fallback
                    }

                    plane.transform.position = plane.startPos;
                    plane.transform.rotation = Quaternion.LookRotation(plane.endPos - plane.startPos);

                    plane.dropPosition = dropPosition;
                    if (plane.net != null && plane.net.ID.IsValid)
                    {
                        plane.SendNetworkUpdateImmediate();
                    }
                }
                catch (Exception ex)
                {
                    if (Instance != null)
                        Instance.Log($"Error in BradleyDropPosition: {ex.Message}", LogLevel.ERROR);
                    
                    Destroy(this);
                }
            }

            Vector3 GenerateFlightDirection()
            {
                Vector3 direction = Vector3Ex.Range(-1f, 1f);
                direction.y = 0f;
                
                if (direction.magnitude < 0.1f)
                {
                    direction = Vector3.forward; // Fallback
                }
                
                direction.Normalize();
                return direction;
            }

            void OnDestroy()
            {
                if (Instance != null && CargoPlaneList != null && plane != null)
                {
                    if (CargoPlaneList.Contains(plane))
                        CargoPlaneList.Remove(plane);
                }
                
                CancelInvoke();
            }
        }

        public class BradleyDrop : MonoBehaviour
        {            
            public string apcProfile;
            public BasePlayer owner;
            public BradleyAPC bradley;
            public CH47Helicopter ch47;
            public CH47HelicopterAIController ch47Ai;
            public CH47AIBrain ch47Brain;
            public CargoPlane plane;
            public ulong skinId;
            public HotAirBalloon balloon;
            public Vector3 position;
            public bool isDespawning = false;
            public bool isDying = false;
            public bool hasLanded = false;
            public BasePlayer lastAttacker;
            public bool isWaveBradley;
            public string waveProfile;
            public ulong waveSkinId;
            public float waveTime;
            public float turretCooldown = 0f;
            public bool isShellingTurret = false;
            public float turretAttackTime;
            public List<string> waveProfileCache;
            public List<BradleyAPC.TargetInfo> turretTargets = new List<BradleyAPC.TargetInfo>();
            public List<BasePlayer> callingTeam = new List<BasePlayer>();
            public bool atDropZone = false;
            public APCData _apcProfile;
            public APCData _nextApcProfile;
            public ulong bradleyId;
            public BradleyBuildBlockZone bradleyBuildBlockZone;

            private bool releaseBradley = false;
            private List<AutoTurret> callingTeamTurrets = new List<AutoTurret>();
            private float lastUpdateTargets;
            private float lastPositionCheck;
            private float lastReturnCheck;
            private int liftBalloon = 0;
            private float updateBradleyInfo;
            private float lastCannonTime;
            private float lastTurretTargetUpdate;
            private float lastNetworkUpdateTime;
            private Drone hostileDrone;
            private bool hasDroneTarget = false;

            // AI Pathfinding vars
            public enum BradleyState 
            { 
                PATROL, 
                ENGAGE, 
                HUNT, 
                MOVETOENGAGE 
            }

            public BradleyState currentState = BradleyState.PATROL;
            public BasePlayer primaryTarget;
            public Vector3 lastKnownTargetPosition = Vector3.zero;
            public float lastTargetSeenTime;
            public float huntStartTime;

            // Engagement positioning
            public Vector3 lastEngagePosition = Vector3.zero;
            public float lastEngageRepositionTime = 0f;
            public float engageRepositionCooldown = 5f; // Wait 5 seconds before repositioning again
            public float optimalEngageRangeMin = 30f; // Minimum optimal engagement range
            public float optimalEngageRangeMax = 60f; // Maximum optimal engagement range
            
            // Stuck detection variables
            private Vector3 lastPosition;
            private float positionCheckStartTime;
            private bool isStuck = false;
            private Vector3 lastStuckTarget = Vector3.zero; // Remember what target got us stuck
            private List<Vector3> failedTargets = new List<Vector3>(); // Track failed destinations
            // AI Pathfinding vars End

            private float lastSmokedTime = 0f;
            
            void Start()
            {
                if (!config.bradley.apcConfig.TryGetValue(apcProfile, out _apcProfile))
                {
                    Instance.Log($"Could not find config for profile: {apcProfile}", LogLevel.ERROR);
                    DestroyImmediate(this);
                    return;
                }
                bradleyId = bradley.net.ID.Value;
                skinId = bradley.skinID;
                if (bradley.myRigidBody != null)
                    bradley.myRigidBody.linearDamping = config.plane.fallDrag;

                var startTime = Time.realtimeSinceStartup;
                turretCooldown = startTime;
                turretAttackTime = startTime;
                lastUpdateTargets = startTime;
                lastPositionCheck = startTime;
                lastReturnCheck = startTime;
                updateBradleyInfo = startTime;
                lastCannonTime = startTime;
                lastTurretTargetUpdate = startTime;
                lastNetworkUpdateTime = startTime;

                // AI Pathfinding
                currentState = BradleyState.PATROL;
                lastTargetSeenTime = Time.time;
                lastPosition = bradley.transform.position;
                positionCheckStartTime = Time.time;
                isStuck = false;

                if (config.options.deliveryMethod.ToLower() == "balloon")
                    Instance.timer.Once(1f, ()=> AddBalloon());

                InvokeRepeating(nameof(LandingCheck), 0.5f, 0.25f);
                InvokeRepeating(nameof(GetCallingTeam), 0f, 10f);
                InvokeRepeating(nameof(FilterTargetList), 3f, 0.5f);

                bradley.CancelInvoke(bradley.UpdateTargetVisibilities); // Cancel and replace with our custom method
                InvokeRepeating(nameof(CustomUpdateTargetVisibilities), 1f, BradleyAPC.sightUpdateRate);

                bradley.CancelInvoke(bradley.UpdateTargetList); // Cancel and replace with our custom method
                InvokeRepeating(nameof(CustomUpdateTargetList), 1f, 2f);

                bradley.CancelInvoke(bradley.BuildingCheck); // Cancel and replace with ourr custom method
                InvokeRepeating(nameof(CustomBuildingCheck), 1f, _apcProfile.ObstacleDamage.DamageTickRate);

                if (_apcProfile.Weapons.CanUseSmokeGrenades)
                    InvokeRepeating(nameof(FireSmokeGrenade), 1f, 2f);

                if (_apcProfile.TargetingDamage.TargetDrones)
                    InvokeRepeating(nameof(DetectHostileDrones), 1f, 2f);

                AddBuildBlockZone();

                Instance.Log($"Started event: {apcProfile}", LogLevel.DEBUG);
            }

            void Update()
            {
                if (bradley == null)
                {
                    DestroyImmediate(this);
                    return;
                }

                UpdateBradleyInfo();

                if (isDespawning || isDying || !hasLanded)
                    return;

                if (isShellingTurret)
                {
                    if (turretTargets.Count < 1 || Time.realtimeSinceStartup - turretAttackTime > config.bradley.turretAttackTime)
                    {
                        turretCooldown = Time.realtimeSinceStartup;
                        isShellingTurret = false;
                        turretTargets.Clear();
                        return;
                    }
                }
                PositionCheck();
            }
            
            #region Misc Helpers

            void UpdateBradleyInfo()
            {
                // Brute force re-setting info regularly since some plugins want to change it
                if (Time.realtimeSinceStartup - updateBradleyInfo > 1f)
                {
                    updateBradleyInfo = Time.realtimeSinceStartup;
                    bradley.OwnerID = owner.userID;
                    bradley._name = apcProfile;
                    bradley.skinID = skinId;

                    bradley.UpdateNetworkGroup(); // Deep Sea networking shiz
                }
            }

            public void DetectHostileDrones()
            {
                if (bradley == null || bradley.IsDestroyed || !hasLanded)
                    return;

                if (hostileDrone == null || !hostileDrone.IsRemoteControllableHostile() || !hostileDrone.ControllingViewerId.HasValue)
                    hasDroneTarget = false;

                List<Drone> drones = Pool.Get<List<Drone>>();
                try
                {
                    Vis.Entities(bradley.transform.position, _apcProfile.TargetingDamage.DroneTargetRange, drones, -1, QueryTriggerInteraction.Collide);

                    foreach (var drone in drones)
                    {
                        if (drone == null || !drone.IsRemoteControllableHostile() || !drone.ControllingViewerId.HasValue)
                            continue;

                        // Guard the nullable before using the Value
                        // Note: Fixes "InvalidOperationException: Nullable object must have a value." error
                        var viewerId = drone.ControllingViewerId;
                        if (!viewerId.HasValue)
                            continue;

                        ulong steamId = viewerId.Value.SteamId;
                        if (!steamId.IsSteamId())
                            continue;

                        bool isOwnerOrFriend = Instance.IsOwnerOrFriend(bradley.OwnerID, steamId);
                        if (isOwnerOrFriend && !_apcProfile.TargetingDamage.AttackOwner)
                            continue;

                        if (!isOwnerOrFriend && !_apcProfile.TargetingDamage.TargetOtherPlayers)
                            continue;

                        hasDroneTarget = true;
                        hostileDrone = drone;
                        break;
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref drones);
                }
            }

            public void FireSmokeGrenade()
            {
                if (bradley == null || bradley.IsDestroyed || !hasLanded)
                    return;

                Vector3 targetPosition = Vector3.zero;
                string shortName = string.Empty;
                if (bradley.targetList.Count > 0)
                {
                    BasePlayer player = bradley.targetList[0].entity as BasePlayer;
                    if (player == null)
                        return;

                    targetPosition = player.transform.position;
                    if (targetPosition == null || targetPosition == Vector3.zero)
                        return;

                    Item heldItem = player?.GetActiveItem();
                    if (heldItem == null)
                        return;
                    
                    shortName = heldItem.info.shortname;
                    if (string.IsNullOrEmpty(shortName))
                        return;

                    if (_apcProfile.Weapons.SmokedWeapons.Contains(shortName))
                        StartCoroutine(FireSmokeGrenadeSequence(targetPosition));
                }
            }

            private IEnumerator FireSmokeGrenadeSequence(Vector3 targetPosition)
            {
                if (Time.realtimeSinceStartup - lastSmokedTime < _apcProfile.Weapons.SmokeGrenadesCooldown)
                    yield break;

                lastSmokedTime = Time.realtimeSinceStartup;

                if (bradley == null || bradley.IsDestroyed)
                    yield break;

                Transform launchPoint = bradley.CannonMuzzle;
                if (launchPoint == null)
                    yield break;

                int grenadeAmount = _apcProfile.Weapons.SmokeGrenadesAmount;
                float smokeRadius = _apcProfile.Weapons.SmokeGrenadesRadius;
                float delayBetweenShots = _apcProfile.Weapons.SmokeGrenadesRate;

                for (int i = 0; i < grenadeAmount; i++)
                {
                    if (bradley == null || bradley.IsDestroyed)
                        yield break;

                    // Calculate random offset within radius
                    // Vector2 randomCircle = Random.insideUnitCircle * smokeRadius;
                    Vector3 randomCircle = Random.insideUnitSphere * smokeRadius; // Seems better spread using sphere
                    Vector3 randomOffset = new Vector3(randomCircle.x, 0, randomCircle.y);
                    Vector3 adjustedTarget = targetPosition + randomOffset;

                    Vector3 startPosition = launchPoint.position;
                    Vector3 toTarget = adjustedTarget - startPosition;
                    float horizontalDistance = new Vector3(toTarget.x, 0, toTarget.z).magnitude;
                    float heightDifference = adjustedTarget.y - startPosition.y;
                    
                    float gravity = Mathf.Abs(Physics.gravity.y);
                    
                    // Smooth angle calculation
                    float launchAngleDegrees = 45f + (20f * Mathf.Exp(-horizontalDistance / 40f));
                    float launchAngle = launchAngleDegrees * Mathf.Deg2Rad;
                    
                    // Standard ballistic equation
                    float tanAngle = Mathf.Tan(launchAngle);
                    float cosAngle = Mathf.Cos(launchAngle);
                    
                    float denominator = 2f * cosAngle * cosAngle * (horizontalDistance * tanAngle - heightDifference);
                    float velocity;
                    
                    if (denominator > 0.01f)
                    {
                        velocity = Mathf.Sqrt((gravity * horizontalDistance * horizontalDistance) / denominator);
                    }
                    else
                    {
                        velocity = Mathf.Sqrt(gravity * horizontalDistance / Mathf.Sin(2f * launchAngle));
                    }
                    
                    // Distance-based multiplier: Less yeetage for close range, more for longer range
                    float distanceMultiplier = Mathf.Lerp(0.8f, 1.1f, Mathf.Clamp01(horizontalDistance / 80f));
                    velocity *= distanceMultiplier;
                    
                    // Height-based multiplier: Add extra velocity for elevated targets in 2.5m increments
                    if (heightDifference > 2.5f)
                    {
                        int heightIncrements = Mathf.FloorToInt(heightDifference / 2.5f);
                        // Each 2.5m increment adds 2.5% velocity
                        float heightMultiplier = 1f + (heightIncrements * 0.025f);
                        velocity *= heightMultiplier;
                    }
                    
                    // Clamp velocity to stop it shooting infinitely far
                    velocity = Mathf.Clamp(velocity, 10f, 65f);
                    
                    Vector3 horizontalDirection = new Vector3(toTarget.x, 0, toTarget.z).normalized;
                    Vector3 launchDirection = (horizontalDirection * cosAngle + Vector3.up * Mathf.Sin(launchAngle)).normalized;
                    BaseEntity grenadeEntity = GameManager.server.CreateEntity(grenadePrefab, startPosition, Quaternion.LookRotation(launchDirection), true);
                    if (grenadeEntity != null)
                    {
                        ServerProjectile projectile = grenadeEntity.GetComponent<ServerProjectile>();
                        if (projectile != null)
                        {
                            projectile.gravityModifier = 1f;
                            projectile.speed = velocity;
                            projectile.InitializeVelocity(launchDirection * velocity);
                        }
                        
                        TimedExplosive timedExplosive = grenadeEntity.GetComponent<TimedExplosive>();
                        if (timedExplosive != null)
                            timedExplosive.creatorEntity = bradley;
                        
                        grenadeEntity.Spawn();
                        
                        Effect.server.Run(mpgLauncherFx, bradley, StringPool.Get(launchPoint.gameObject.name), Vector3.zero, Vector3.zero, null, false, null);
                    }

                    if (i < grenadeAmount - 1)
                    {
                        yield return new WaitForSeconds(delayBetweenShots);
                    }
                }
            }

            public void GetCallingTeam() // DEBUG: Check this works properly, people leave team then rejoin etc
            {
                if (!callingTeam.Contains(owner))
                    callingTeam.Add(owner);

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    if (player == null)
                        continue;
                    
                	bool isOwnerOrFriend = Instance.IsOwnerOrFriend(player.userID, owner.userID);
                    if (isOwnerOrFriend && !callingTeam.Contains(player))
                    {
                        callingTeam.Add(player);
                    }
                    else if (!isOwnerOrFriend && callingTeam.Contains(player))
                    {
                    	callingTeam.Remove(player);
                    }
                }
                
                List<AutoTurret> autoTurrets = Pool.Get<List<AutoTurret>>();

                Vis.Entities<AutoTurret>(bradley.transform.position, _apcProfile.Init.SearchRange, autoTurrets, 256, QueryTriggerInteraction.Collide);
                
                foreach (AutoTurret autoTurret in autoTurrets)
                {
                	foreach (BasePlayer player in callingTeam)
                    {
                    	if (autoTurret.OwnerID == 0)
                        {
                        	continue;
                        }
                    	else if (autoTurret.OwnerID == player.userID && !callingTeamTurrets.Contains(autoTurret))
                        {
                        	callingTeamTurrets.Add(autoTurret);
                            continue;
                        }
                    	if (callingTeamTurrets.Contains(autoTurret))
                        {
                        	callingTeamTurrets.Remove(autoTurret);
                        }
                    }
                }
                Pool.FreeUnmanaged<AutoTurret>(ref autoTurrets);
            }

            void LandingCheck()
            {
                if (!atDropZone)
                    return;
                                
                if (!hasLanded)
                {
                    if (balloon != null)
                        balloon.myRigidbody.isKinematic = true;

                    if (!releaseBradley)
                    {
                        LayerMask layerMask = LayerMask.GetMask("Water", "Tree", "Clutter", "Player_Server", "Construction", "Terrain", "World", "Deployed");
                        var colliders = Physics.OverlapSphere(transform.position, 6.5f, layerMask);
                        if (colliders.Length > 0)
                        {
                            releaseBradley = true;
                            if (bradley != null) bradley.myRigidBody.detectCollisions = true;
                        }
                    }
                    else
                    {
                        hasLanded = true;
                        if (config.options.clearLandingZone || config.options.clearTimedExplosives && config.options.strictProximity)
                            Instance.IsNearCollider(bradley, true);

                        if (BradleyDropData.ContainsKey(bradley.net.ID.Value))
                            BradleyDropData[bradley.net.ID.Value].StartTime = Time.realtimeSinceStartup;

                        if (_apcProfile.Init.DespawnTime > 0)
                            Instance.DespawnAPC(bradley, apcProfile, _apcProfile.Init.DespawnTime);
                    }
                }
                else
                {
                    Vector3 pos = bradley.transform.position;
                    Quaternion rot = bradley.transform.rotation;

                    bradley.myRigidBody.isKinematic = false;
                    bradley.SetParent(null);
                    bradley.transform.position = pos;
                    bradley.transform.rotation = rot;
                    bradley._maxHealth = _apcProfile.Init.Health;
                    bradley.health = bradley._maxHealth;

                    // Now set the search and turret range which was set to 0 while parachuting
                    bradley.searchRange = _apcProfile.Init.SearchRange;
                    bradley.memoryDuration = _apcProfile.TargetingDamage.MemoryDuration;
                    bradley.viewDistance = _apcProfile.Weapons.MainGunRange;
                    bradley.topTurretFireRate = _apcProfile.Weapons.TopTurretFireRate;
                    bradley.coaxFireRate = _apcProfile.Weapons.CoaxFireRate;
                    bradley.coaxBurstLength = _apcProfile.Weapons.CoaxBurstLength;
                    bradley.maxCratesToSpawn = 0; // Set this to 0 to allow my custom crate logic to handle
                    bradley.throttle = _apcProfile.Init.ThrottleResponse;
                    bradley.leftThrottle = bradley.throttle;
                    bradley.rightThrottle = bradley.throttle;
                    bradley.ClearPath();
                    bradley.currentPath.Clear();
                    bradley.currentPathIndex = 0;
                    bradley.DoAI = true;

                    // Generate initial smart patrol path
                    List<Vector3> initialPath = GeneratePatrolPathList();
                    ReplacePathSafely(initialPath);

                    bradley.SendNetworkUpdateImmediate();

                    if (bradley.ScientistSpawns[0] != null)
                        bradley.ScientistSpawns[0].BradleyHealth = _apcProfile.BradleyScientists.DeployThreshold1;

                    if (bradley.ScientistSpawns[1] != null)
                        bradley.ScientistSpawns[1].BradleyHealth = _apcProfile.BradleyScientists.DeployThreshold2;

                    if (bradley.ScientistSpawns[2] != null)
                        bradley.ScientistSpawns[2].BradleyHealth = _apcProfile.BradleyScientists.DeployThreshold3;

                    CancelInvoke(nameof(LandingCheck));

                    // Start AI monitoring AFTER native setup
                    InvokeRepeating(nameof(MonitorAIState), 2f, 2f);
                    InvokeRepeating(nameof(CheckStuckState), 2f, 2f);

                    if (balloon != null)
                    {
                        balloon.SetFlagLocal(BaseEntity.Flags.Reserved6, true, false);
                        balloon.myRigidbody.isKinematic = false;
                        balloon.inflationLevel = 1f;
                        balloon.SetParent(null);
                        balloon.sinceLastBlast = Time.realtimeSinceStartup;
                        balloon.transform.position = bradley.transform.position + new Vector3(0, 1f, 0);
                        balloon.SetFlagLocal(BaseEntity.Flags.On, true, false);
                        InvokeRepeating(nameof(LiftBalloon), 0f, 0.5f);
                    }
                }
            }

            void PositionCheck()
            {
                if (Time.realtimeSinceStartup - lastPositionCheck > 2.0f)
                {
                    lastPositionCheck = Time.realtimeSinceStartup;
                    if (bradley != null && !bradley.IsDestroyed)
                    {
                        if (_apcProfile.ZoneManager.UseZoneManager && !_apcProfile.ZoneManager.CanLeaveZone)
                        {
                        	bool isInZone = false;
                            foreach (var id in _apcProfile.ZoneManager.DropZoneIDs)
                            {
                            	isInZone = (bool)Instance.ZoneManager?.CallHook("IsEntityInZone", id, bradley);
                                if (isInZone)
                                	break;
                            }
                            
                            if (!isInZone)
                            {
                                GenerateReturnToPatrolPath();
                                return;
                            }
                        }
                        else if (_apcProfile.ZoneManager.UseZoneManager && _apcProfile.ZoneManager.UseProtectedZones)
                        {
                        	bool isInZone = false;
                            foreach (var id in _apcProfile.ZoneManager.ProtectedZoneIDs)
                            {
                            	isInZone = (bool)Instance.ZoneManager?.CallHook("IsEntityInZone", id, bradley);
                                if (isInZone)
                                	break;
                            }
                            
                            if (isInZone)
							{
                                GenerateReturnToPatrolPath();
                                return;
                            }
                        }
                        
                        if (_apcProfile.Init.KillInSafeZone && Instance.IsInSafeZone(bradley.transform.position))
                        {
                            if (config.bradley.despawnExplosion)
                                Effect.server.Run(bradleyExplosion, bradley.transform.position);
                            
                            bradley.Kill();
                            isDying = true;
                            Instance.Message(owner, "IntoSafeZone", apcProfile);
                            Destroy(this);
                        }
                        else if (bradley.WaterFactor() >= 1f)
                        {
                            if (config.bradley.despawnExplosion)
                                Effect.server.Run(bradleyExplosion, bradley.transform.position);
                            
                            bradley.Kill();
                            isDying = true;
                            Instance.Message(owner, "IntoWater", apcProfile);
                            Destroy(this);
                        }
                    }
                }
            }

            public void CustomBuildingCheck()
            {
      			if (Instance == null)
                	return;
                
                int mask = (1 << (int)Rust.Layer.Construction) | (1 << (int)Rust.Layer.Deployed) | (1 << (int)Rust.Layer.Default);

                List<BaseEntity> baseEntities = Pool.Get<List<BaseEntity>>();
                Vis.Entities<BaseEntity>(bradley.WorldSpaceBounds(), baseEntities, mask, QueryTriggerInteraction.Collide);
                foreach (BaseEntity baseEntity in baseEntities)
                {
                    if (baseEntity == null || baseEntity is BradleyAPC)
                        continue;
                    
                    if (_apcProfile.TargetingDamage.BlockProtectedList && config.bradley.protectedPrefabs.Contains(baseEntity.name))
                        continue;

                    if (Instance.permission.UserHasPermission(baseEntity.OwnerID.ToString(), permBypassBuildingDamage) && bradley.OwnerID == baseEntity.OwnerID)
                        continue;

                    bool isOwnerOrFriend = Instance.IsOwnerOrFriend(bradley.OwnerID, baseEntity.OwnerID);
                    
                    if (_apcProfile.TargetingDamage.BlockOwnerDamage && isOwnerOrFriend)
                        continue;
                    
                    if (_apcProfile.TargetingDamage.BlockOtherDamage && !isOwnerOrFriend)
                    {
						if (baseEntity is not BuildingBlock)
							continue;

                    	if (!Instance.IsDamageAuthorized(bradley, baseEntity))
                        	continue;
                    }

                    string sound = string.Empty;
                    
                    if (baseEntity is Barricade && _apcProfile.ObstacleDamage.Barricade.BarricadeDestroy)
                    {
                        Barricade barricade = baseEntity as Barricade;
                        if (barricade == null || !barricade.IsAlive())
                            continue;

                        string prefab = barricade.ShortPrefabName.ToLower();
                        if (prefab.Contains("wood")) sound = woodGibSound;
                        else if (prefab.Contains("stone")) sound = stoneGibSound;
                        else if (prefab.Contains("concrete")) sound = stoneGibSound;
                        else if (prefab.Contains("metal")) sound = metalGibSound;
                        else sound = stoneGibSound;

                        float amount = barricade.MaxHealth() * _apcProfile.ObstacleDamage.Barricade.BarricadeDamage;
                        Effect.server.Run(sound, barricade.transform.position);

                        if (barricade.health - amount <= 0)
                        {
                            barricade.Kill(BaseNetworkable.DestroyMode.Gib);
                            continue;
                        }
                        barricade.health -= amount;
                        barricade.SendNetworkUpdateImmediate();
                        continue;
                    }
                    else if (baseEntity is BuildingBlock)
                    {
                        BuildingBlock buildingBlock = baseEntity as BuildingBlock;
                        if (buildingBlock == null || !buildingBlock.IsAlive())
                            continue;

                        var grade = buildingBlock.grade;
                        float damage = 0f;
                        switch (grade)
                        {
                            case BuildingGrade.Enum.Twigs:
                                if (!_apcProfile.ObstacleDamage.Twig.TwigDestroy) continue;
                                damage = _apcProfile.ObstacleDamage.Twig.TwigDamage;
                                sound = woodGibSound;
                                break;
                            case BuildingGrade.Enum.Wood:
                                if (!_apcProfile.ObstacleDamage.Wood.WoodDestroy) continue;
                                damage = _apcProfile.ObstacleDamage.Wood.WoodDamage;
                                sound = woodGibSound;
                                break;
                            case BuildingGrade.Enum.Stone:
                                if (!_apcProfile.ObstacleDamage.Stone.StoneDestroy) continue;
                                damage = _apcProfile.ObstacleDamage.Stone.StoneDamage;
                                sound = stoneGibSound;
                                break;
                            case BuildingGrade.Enum.Metal:
                                if (!_apcProfile.ObstacleDamage.Metal.MetalDestroy) continue;
                                damage = _apcProfile.ObstacleDamage.Metal.MetalDamage;
                                sound = metalGibSound;
                                break;
                            case BuildingGrade.Enum.TopTier:
                                if (!_apcProfile.ObstacleDamage.Armored.ArmoredDestroy) continue;
                                damage = _apcProfile.ObstacleDamage.Armored.ArmoredDamage;
                                sound = metalGibSound;
                                break;
                            default:
                                continue;
                        }

                        float amount = buildingBlock.MaxHealth() * damage;
                        Effect.server.Run(sound, buildingBlock.transform.position);

                        if (buildingBlock.health - amount <= 0)
                        {
                            buildingBlock.Kill(BaseNetworkable.DestroyMode.Gib);
                            continue;
                        }
                        buildingBlock.health -= amount;
                        buildingBlock.SendNetworkUpdateImmediate();
                        continue;
                    }
                    else
                    {
                        if (!_apcProfile.ObstacleDamage.Deployable.DeployableDestroy)
                            continue;
                        
                        BaseCombatEntity baseCombatEntity = baseEntity as BaseCombatEntity;
                        if (baseCombatEntity == null || !baseCombatEntity.IsAlive())
                            continue;

                        string prefab = baseCombatEntity.ShortPrefabName.ToLower();
                        if (prefab.Contains("wood")) sound = woodGibSound;
                        else if (prefab.Contains("stone")) sound = stoneGibSound;
                        else if (prefab.Contains("concrete")) sound = stoneGibSound;
                        else if (prefab.Contains("metal")) sound = metalGibSound;
                        else sound = stoneGibSound;

                        float amount = baseCombatEntity.MaxHealth() * _apcProfile.ObstacleDamage.Deployable.DeployableDamage;
                        Effect.server.Run(sound, baseCombatEntity.transform.position);

                        if (baseCombatEntity.health - amount <= 0)
                        {
                            baseCombatEntity.Kill(BaseNetworkable.DestroyMode.Gib);
                            continue;
                        }
                        baseCombatEntity.health -= amount;
                        baseCombatEntity.SendNetworkUpdateImmediate();
                        continue;
                    }
                }
                Pool.FreeUnmanaged<BaseEntity>(ref baseEntities);
            }

            void AddBuildBlockZone()
            {
                if (!config.bradley.buildBlocking.EnableBuildBlockZone)
                    return;

                if (bradleyBuildBlockZone != null)
                    return;

                bradleyBuildBlockZone = bradley.gameObject.AddComponent<BradleyBuildBlockZone>();
                if (bradleyBuildBlockZone == null)
                    return;

                bradleyBuildBlockZone.bradComp = this;
                bradleyBuildBlockZone.entity = bradley;
                bradleyBuildBlockZone.centre = position;

                Instance.activeBuildBlockZones.Add(bradleyBuildBlockZone);
            }

            void UnregisterBuildBlockZone()
            {
                if (bradleyBuildBlockZone != null)
                    Instance.activeBuildBlockZones.Remove(bradleyBuildBlockZone);
            }

            #endregion Misc Helpers

            #region targeting & Weapons

            public void FilterTargetList()
            {
                if (Instance == null || bradley == null)
                    return;
                
                for (int i = bradley.targetList.Count - 1; i >= 0; i--)
                {
                    BradleyAPC.TargetInfo item = bradley.targetList[i];
                    if (item?.entity is BasePlayer)
                    {
                        BasePlayer player = item?.entity as BasePlayer;

                        bool isOwnerOrFriend = Instance.IsOwnerOrFriend(player.userID, owner.userID);

                        if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                        {
                            bradley.targetList.RemoveAt(i);
                        }
                        else if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                        {
                            bradley.targetList.RemoveAt(i);
                        }
                    }
                    else if (item?.entity is AutoTurret)
                    {
                        AutoTurret turret = item?.entity as AutoTurret;

                        bool isOwnerOrFriend = Instance.IsOwnerOrFriend(turret.OwnerID, owner.userID);

                        if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                        {
                            bradley.targetList.RemoveAt(i);
                        }
                        else if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                        {
                            bradley.targetList.RemoveAt(i);
                        }
                    }
                }
            }

            public void CustomDoWeapons()
            {
                BaseCombatEntity _mainGunTarget = bradley.mainGunTarget;
                if (isShellingTurret && turretTargets.Count > 0)
                {
                    AutoTurret turret = turretTargets[0].entity as AutoTurret;
                    if (turret != null)
                    {
                        bradley.mainGunTarget = turret;
                    }
                }

                if (_mainGunTarget != null && bradley.mainTurretEyePos != null && bradley.mainTurretEyePos.transform != null)
                {
                    Vector3 aimPoint = bradley.GetAimPoint(_mainGunTarget) - bradley.mainTurretEyePos.transform.position;
                    if (Vector3.Dot(bradley.turretAimVector, aimPoint.normalized) >= 0.99f)
                    {
                        bool isVisible = CustomVisibilityTest(_mainGunTarget, bradley.viewDistance);
                        float targetDist = Vector3.Distance(_mainGunTarget.transform.position, bradley.transform.position);
                        if (Time.time > bradley.nextCoaxTime & isVisible && targetDist <= _apcProfile.Weapons.CoaxTargetRange)
                        {
                            bradley.numCoaxBursted++;
                            CustomFireGun(bradley.GetAimPoint(_mainGunTarget), _apcProfile.Weapons.CoaxAimCone, true);
                            bradley.nextCoaxTime = Time.time + bradley.coaxFireRate;
                            if (bradley.numCoaxBursted >= bradley.coaxBurstLength)
                            {
                                bradley.nextCoaxTime = Time.time + _apcProfile.Weapons.CoaxBurstDelay;
                                bradley.numCoaxBursted = 0;
                            }
                        }
                        if (targetDist >= 10f & isVisible)
                        {
                            CustomFireGunTest();
                        }
                    }
                }
                
                BaseEntity item = null;
                if (hasDroneTarget && Time.time > _apcProfile.Weapons.TopTurretDroneFireRate && CustomVisibilityTest(hostileDrone, _apcProfile.TargetingDamage.DroneTargetRange))
                {
                    if (hostileDrone == null || hostileDrone.IsDestroyed || !hostileDrone.IsRemoteControllableHostile())
                        return;
                    
                    CustomFireGun(bradley.GetAimPoint(hostileDrone), _apcProfile.Weapons.TopTurretAimCone, false);
                    bradley.nextTopTurretTime = Time.time + bradley.topTurretFireRate;
                    return;
                }

                if (bradley.targetList.Count > 1)
                {
                    item = bradley.targetList[1].entity;
                }
                else if (bradley.targetList.Count == 1 && bradley.targetList[0].entity is BasePlayer)
                {
                    item = bradley.targetList[0].entity;
                }
                if (item != null && Time.time > bradley.nextTopTurretTime && CustomVisibilityTest(item, bradley.viewDistance))
                {
                    CustomFireGun(bradley.GetAimPoint(item), _apcProfile.Weapons.TopTurretAimCone, false);
                    bradley.nextTopTurretTime = Time.time + bradley.topTurretFireRate;
                }
            }

            public void CustomDoWeaponAiming()
            {
                Vector3 aimPoint;
                Vector3 mainTurretTargetPos = Vector3.forward;
                Vector3 topTurretTargetPos;

                if (!isShellingTurret)
                {
                    if (bradley.mainGunTarget != null && bradley.mainGunTarget is BasePlayer)
                    {
                        BasePlayer targetPlayer = bradley.mainGunTarget as BasePlayer;
                        aimPoint = targetPlayer.eyes.position - bradley.mainTurretEyePos.transform.position;
                        mainTurretTargetPos = aimPoint.normalized;
                    }
                    else
                    {
                        mainTurretTargetPos = bradley.desiredAimVector;
                    }
                }
                else if (isShellingTurret)
                {
                    if (turretTargets.Count > 0)
                    {
                        AutoTurret turret = turretTargets[0].entity as AutoTurret;
                        if (turret != null)
                        {
                            aimPoint = turret.CenterPoint() - bradley.mainTurretEyePos.transform.position;
                            mainTurretTargetPos = aimPoint.normalized;
                        }
                        else
                        {
                            mainTurretTargetPos = bradley.desiredAimVector;
                        }
                    }
                }
                bradley.desiredAimVector = mainTurretTargetPos;

                if (hasDroneTarget && hostileDrone.IsRemoteControllableHostile() && CustomVisibilityTest(hostileDrone, _apcProfile.TargetingDamage.DroneTargetRange))
                {
                    if (hostileDrone != null && !hostileDrone.IsDestroyed)
                    {
                        aimPoint = bradley.GetAimPoint(hostileDrone) - bradley.topTurretEyePos.transform.position;
                        topTurretTargetPos = aimPoint.normalized;
                    }
                    else
                    {
                        topTurretTargetPos = bradley.transform.forward;
                    }
                    bradley.desiredTopTurretAimVector = topTurretTargetPos;
                    return;
                }

                BaseEntity item = null;
                if (bradley.targetList.Count > 0)
                {
                    if (bradley.targetList.Count > 1 && bradley.targetList[1].IsValid() && bradley.targetList[1].IsVisible())
                    {
                        item = bradley.targetList[1].entity;
                    }
                    else if (bradley.targetList[0].IsValid() && bradley.targetList[0].IsVisible())
                    {
                        item = bradley.targetList[0].entity;
                    }
                }
                if (item != null)
                {
                    aimPoint = bradley.GetAimPoint(item) - bradley.topTurretEyePos.transform.position;
                    topTurretTargetPos = aimPoint.normalized;
                }
                else
                {
                    topTurretTargetPos = bradley.transform.forward;
                }
                bradley.desiredTopTurretAimVector = topTurretTargetPos;
            }
            
            public void CustomFireGun(Vector3 targetPos, float aimCone, bool isCoax)
            {
                bradley.deployedTimeSinceBradleyAttackedTarget = 0f;
                Transform transforms = (isCoax ? bradley.coaxMuzzle : bradley.topTurretMuzzle);
                Vector3 vector3 = transforms.transform.position - (transforms.forward * 0.25f);
                Vector3 vector31 = targetPos - vector3;
                Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(aimCone, vector31.normalized, true);
                targetPos = vector3 + (modifiedAimConeDirection * 300f);
                List<RaycastHit> raycastHits = Pool.Get<List<RaycastHit>>();
                GamePhysics.TraceAll(new Ray(vector3, modifiedAimConeDirection), 0f, raycastHits, 300f, 1220225809, QueryTriggerInteraction.UseGlobal, null);
                for (int i = 0; i < raycastHits.Count; i++)
                {
                    RaycastHit item = raycastHits[i];
                    BaseEntity entity = item.GetEntity();
                    if ((!(entity != null) || !(entity == bradley) && !entity.EqualNetID(bradley)) && !(entity is ScientistNPC))
                    {
                        BaseCombatEntity baseCombatEntity = entity as BaseCombatEntity;
                        if (baseCombatEntity != null)
                        {
                            bradley.ApplyDamage(baseCombatEntity, item.point, modifiedAimConeDirection);
                        }
                        if (!(entity != null) || entity.ShouldBlockProjectiles())
                        {
                            targetPos = item.point;
                            break;
                        }
                    }
                }
                bradley.ClientRPC(RpcTarget.NetworkGroup("CLIENT_FireGun"), isCoax, targetPos);
                Pool.FreeUnmanaged<RaycastHit>(ref raycastHits);
            }

            public void CustomFireGunTest()
            {
                TimedExplosive timedExplosive;
                if (Time.time < bradley.nextFireTime)
                {
                    return;
                }
                bradley.deployedTimeSinceBradleyAttackedTarget = 0f;
                bradley.nextFireTime = Time.time + _apcProfile.Weapons.MainCannonFireRate;
                bradley.numBursted++;
                if (bradley.numBursted >= _apcProfile.Weapons.MainCannonBurst)
                {
                    bradley.nextFireTime = Time.time + _apcProfile.Weapons.MainCannonBurstDelay;
                    bradley.numBursted = 0;
                }
                Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(_apcProfile.Weapons.MainGunAimCone, bradley.CannonMuzzle.rotation * Vector3.forward, true);
                Vector3 cannonPitch = (bradley.CannonPitch.transform.rotation * Vector3.back) + (bradley.transform.up * -1f);
                Vector3 vector3 = cannonPitch.normalized;
                bradley.myRigidBody.AddForceAtPosition(vector3 * bradley.recoilScale, bradley.CannonPitch.transform.position, ForceMode.Impulse);
                Effect.server.Run(bradley.mainCannonMuzzleFlash.resourcePath, bradley, StringPool.Get(bradley.CannonMuzzle.gameObject.name), Vector3.zero, Vector3.zero, null, false, null);
                BaseEntity baseEntity = GameManager.server.CreateEntity(bradley.mainCannonProjectile.resourcePath, bradley.CannonMuzzle.transform.position, Quaternion.LookRotation(modifiedAimConeDirection), true);
                if (baseEntity == null)
                {
                    return;
                }
                ServerProjectile component = baseEntity.GetComponent<ServerProjectile>();
                if (component)
                {
                    component.speed = _apcProfile.Weapons.MainGunProjectileVelocity;
                    component.InitializeVelocity(modifiedAimConeDirection * component.speed);
                }
                if (baseEntity.TryGetComponent<TimedExplosive>(out timedExplosive))
                {
                    timedExplosive.creatorEntity = bradley;
                }
                baseEntity.Spawn();
            }

            public void CustomUpdateTargetVisibilities()
            {
                foreach (BradleyAPC.TargetInfo targetInfo in bradley.targetList)
                {
                    if (!targetInfo.IsValid() || !CustomVisibilityTest(targetInfo.entity, bradley.viewDistance))
                    {
                        continue;
                    }
                    targetInfo.lastSeenTime = Time.time;
                    targetInfo.lastSeenPosition = targetInfo.entity.transform.position;
                }
            }
            
            public bool CustomVisibilityTest(BaseEntity ent, float viewDistance)
            {

                if (ent == null)
                {
                    return false;
                }
                if (Vector3.Distance(ent.transform.position, bradley.transform.position) >= viewDistance)
                {
                    return false;
                }
                bool flag = false;
                if (!(ent is BasePlayer))
                {
                    flag = bradley.IsVisible(ent.CenterPoint(), float.PositiveInfinity);
                }
                else
                {
                    BasePlayer basePlayer = ent as BasePlayer;
                    Vector3 _position = bradley.mainTurret.transform.position;
                    flag = (bradley.IsVisible(basePlayer.eyes.position, _position, float.PositiveInfinity) ? true : bradley.IsVisible(basePlayer.transform.position + (Vector3.up * 0.1f), _position, float.PositiveInfinity));
                    if (!flag && basePlayer.isMounted && basePlayer.GetMounted().VehicleParent() != null && basePlayer.GetMounted().VehicleParent().AlwaysAllowBradleyTargeting)
                    {
                        flag = bradley.IsVisible(basePlayer.GetMounted().VehicleParent().bounds.center, _position, float.PositiveInfinity);
                    }
                    if (flag)
                    {
                        flag = !Physics.SphereCast(new Ray(_position, Vector3Ex.Direction(basePlayer.eyes.position, _position)), 0.05f, Vector3.Distance(basePlayer.eyes.position, _position), 10551297);
                    }
                }

                object obj = Interface.CallHook("CanBradleyApcTarget", bradley, ent);
                if (obj is bool result)
                {
                    return result;
                }
                return flag;
            }
            
			public void CustomUpdateTargetList()
            {
                if (isShellingTurret)
                {
                    UpdateTurretTargets();
                }

                List<BasePlayer> basePlayers = Pool.Get<List<BasePlayer>>();
                Vis.Entities<BasePlayer>(bradley.transform.position, bradley.searchRange, basePlayers, 131072, QueryTriggerInteraction.Collide);
                foreach (BasePlayer basePlayer in basePlayers)
                {
                    if (basePlayer == null || basePlayer.IsNpc || basePlayer.IsDead() || basePlayer is HumanNPC || basePlayer is NPCPlayer || basePlayer.InSafeZone() || !CustomVisibilityTest(basePlayer, bradley.viewDistance))
                    {
                        continue;
                    }

                    bool isOwnerOrFriend = Instance.IsOwnerOrFriend(basePlayer.userID, owner.userID);
                    if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                    {
                        continue;
                    }
                    else if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                    {
                        continue;
                    }

                    bool isTargeted = false;
                    foreach (BradleyAPC.TargetInfo targetInfo in bradley.targetList)
                    {
                        if (targetInfo?.entity != basePlayer)
                        {
                            continue;
                        }

                        targetInfo.lastSeenTime = Time.time;
                        isTargeted = true;
                        break;
                    }

                    if (isTargeted)
                    {
                        continue;
                    }

                    BradleyAPC.TargetInfo newTargetInfo = Pool.Get<BradleyAPC.TargetInfo>();
                    newTargetInfo.Setup(basePlayer, Time.time);
                    bradley.targetList.Add(newTargetInfo);
                }

                for (int i = bradley.targetList.Count - 1; i >= 0; i--)
                {
                    BradleyAPC.TargetInfo item = bradley.targetList[i];
                    if (item?.entity is BasePlayer)
                    {
                        BasePlayer player = item.entity as BasePlayer;
                        if (player == null || Time.time - item.lastSeenTime > bradley.memoryDuration || player.IsDead() || player.InSafeZone() && !player.IsHostile())
                        {
                            bradley.targetList.RemoveAt(i);
                            Pool.Free<BradleyAPC.TargetInfo>(ref item);
                        }

                        bool isOwnerOrFriend = Instance.IsOwnerOrFriend(player.userID, owner.userID);
                        if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                        {
                            bradley.targetList.RemoveAt(i);
                            Pool.Free<BradleyAPC.TargetInfo>(ref item);
                        }
                        else if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                        {
                            bradley.targetList.RemoveAt(i);
                            Pool.Free<BradleyAPC.TargetInfo>(ref item);
                        }
                    }
                    else
                    {
                        // Remove any non-player targets when not shelling turrets
                        bradley.targetList.RemoveAt(i);
                        Pool.Free<BradleyAPC.TargetInfo>(ref item);
                    }
                }

                Pool.FreeUnmanaged<BasePlayer>(ref basePlayers);
                bradley.targetList.Sort(new Comparison<BradleyAPC.TargetInfo>(bradley.SortTargets));
                if (bradley.targetList.Count > 0)
                {
                    bradley.timeSinceValidTarget = 0f;
                }
            }

			public void UpdateTurretTargets()
            {
                List<AutoTurret> autoTurrets = Pool.Get<List<AutoTurret>>();
                Vis.Entities<AutoTurret>(bradley.transform.position, bradley.searchRange, autoTurrets, 256, QueryTriggerInteraction.Collide);
                foreach (AutoTurret autoTurret in autoTurrets)
                {
                    if (autoTurret == null || autoTurret.OwnerID == 0 || autoTurret.InSafeZone() || !CustomVisibilityTest(autoTurret, bradley.viewDistance))
                    {
                        continue;
                    }

                    bool isTargeted = false;
                    foreach (BradleyAPC.TargetInfo targetInfo in turretTargets)
                    {
                        if (targetInfo?.entity != autoTurret)
                        {
                            continue;
                        }

                        targetInfo.lastSeenTime = Time.time;
                        isTargeted = true;
                        break;
                    }

                    if (isTargeted)
                    {
                        continue;
                    }

                    BradleyAPC.TargetInfo newTargetInfo = Pool.Get<BradleyAPC.TargetInfo>();
                    newTargetInfo.Setup(autoTurret, Time.time);
                    turretTargets.Add(newTargetInfo);
                }
                
                for (int i = turretTargets.Count - 1; i >= 0; i--)
                {
                    BradleyAPC.TargetInfo item = turretTargets[i];
                    AutoTurret turret = item?.entity as AutoTurret;
                    if (turret == null || !isShellingTurret || Time.time - item.lastSeenTime > bradley.memoryDuration || turret.OwnerID == 0 || turret.InSafeZone())
                    {
                        turretTargets.RemoveAt(i);
                        Pool.Free<BradleyAPC.TargetInfo>(ref item);
                    }
                    
                    bool isOwnerOrFriend = Instance.IsOwnerOrFriend(turret.OwnerID, owner.userID);
                    if (!_apcProfile.TargetingDamage.AttackOwner && isOwnerOrFriend)
                    {
                        turretTargets.RemoveAt(i);
                        Pool.Free<BradleyAPC.TargetInfo>(ref item);
                    }
                    else if (!_apcProfile.TargetingDamage.TargetOtherPlayers && !isOwnerOrFriend)
                    {
                        turretTargets.RemoveAt(i);
                        Pool.Free<BradleyAPC.TargetInfo>(ref item);
                    }
                }

                Pool.FreeUnmanaged<AutoTurret>(ref autoTurrets);
                turretTargets.Sort(new Comparison<BradleyAPC.TargetInfo>(SortTurretTargets));
                if (turretTargets.Count > 0)
                {
                    bradley.timeSinceValidTarget = 0f;
                }
            }

            public int SortTurretTargets(BradleyAPC.TargetInfo t1, BradleyAPC.TargetInfo t2)
            {
                float priorityScore = GetTurretPriorityScore(t1);
                return priorityScore.CompareTo(GetTurretPriorityScore(t2));
            }

            public float GetTurretPriorityScore(BradleyAPC.TargetInfo targetInfo)
            {
                AutoTurret turret = targetInfo.entity as AutoTurret;
                if (!turret)
                {
                    return 0f;
                }
                float single = Vector3.Distance(turret.transform.position, bradley.transform.position);
                float single1 = (1f - Mathf.InverseLerp(10f, 80f, single)) * 50f;
                float single2 = Mathf.InverseLerp(4f, 20f, (turret.AttachedWeapon == null ? 0f : turret.AttachedWeapon.hostileScore)) * 100f;
                float single3 = Mathf.InverseLerp(10f, 3f, Time.time - targetInfo.lastSeenTime) * 100f;
                float single4 = Mathf.InverseLerp(0f, 100f, targetInfo.damageReceivedFrom) * 100f;
                float single5 = Mathf.InverseLerp(0f, 100f, (bradley.lastAttacker == turret ? 0f : 50f)) * 100f;
                float single6 = Mathf.InverseLerp(0f, 100f, turret.IsBeingControlled ? 50f : 0f) * 50f;
                return single1 + single2 + single4 + single3 + single5 + single6;
            }

            #endregion targeting & Weapons

            #region Core Pathfinding

            void MonitorAIState()
            {
                if (bradley == null || !hasLanded || isDespawning || isDying)
                    return;

                // Wait while internal AI rebuilds path
                if (waitingForInternalPath)
                {
                    if (bradley.currentPath != null && bradley.currentPath.Count > 0)
                    {
                        waitingForInternalPath = false;
                    }
                    return;
                }

                try
                {
                    if (isStuck)
                    {
                        HandleStuckState();
                        return;
                    }

                    UpdatePrimaryTarget();
                    HandleStateTransitions();

                    // Do not regenerate path if internal AI can do it
                    if (bradley.currentPath == null || bradley.currentPath.Count == 0)
                        return;

                    if (ShouldGenerateNewPath())
                    {
                        GeneratePathForCurrentState();
                    }
                }
                catch (Exception ex)
                {
                    if (Instance != null)
                        Instance.Log($"Error in MonitorAIState: {ex.Message}", LogLevel.ERROR);
                }
            }

            void CheckStuckState()
            {
                if (bradley == null || !hasLanded || isDespawning || isDying)
                    return;
                    
                // Don't check for stuck if Bradley is engaging
                if (currentState == BradleyState.ENGAGE)
                {
                    ResetStuckDetection();
                    return;
                }
                
                Vector3 currentPosition = bradley.transform.position;
                float distanceMoved = Vector3.Distance(currentPosition, lastPosition);
                
                // Check if Bradley has moved < threshold (current minimum threshold 1)
                float stuckDetectionThreshold = Math.Max(_apcProfile.Init.StuckDetectionThreshold, 1f); // << Min movement threshold, maybe add config option later (ZEODE)
                if (distanceMoved >= stuckDetectionThreshold)
                {
                    // Bradley movement > threshold, reset stuck detection
                    ResetStuckDetection();
                }
                else
                {
                    // Bradley hasn't moved much, check if enough time has passed (current time 5)
                    float stuckDetectionTime = Math.Max(_apcProfile.Init.StuckDetectionTime, 5f); // << Stuck detection time, maybe add config option later (ZEODE)
                    if (Time.time - positionCheckStartTime >= stuckDetectionTime)
                    {
                        if (!isStuck)
                            DetectStuck();
                    }
                }
                
                lastPosition = currentPosition;
            }
            
            void ResetStuckDetection()
            {
                lastPosition = bradley.transform.position;
                positionCheckStartTime = Time.time;
                isStuck = false;
            }

            void DetectStuck()
            {
                isStuck = true;

                try
                {
                    var path = bradley?.currentPath;
                    if (path == null || path.Count == 0)
                        return;

                    // Snapshot the path to prevent concurrent mutation
                    List<Vector3> pathSnapshot = new List<Vector3>(path);

                    if (pathSnapshot.Count == 0)
                        return;

                    Vector3 currentTarget = Vector3.zero;
                    int index = bradley.currentPathIndex;

                    // Use snapshot from here
                    if (index >= 0 && index < pathSnapshot.Count)
                    {
                        currentTarget = pathSnapshot[index];
                    }
                    else
                    {
                        currentTarget = pathSnapshot[pathSnapshot.Count - 1];
                    }

                    if (currentTarget != Vector3.zero)
                    {
                        lastStuckTarget = currentTarget;

                        // Avoid duplicates
                        if (!failedTargets.Contains(currentTarget))
                        {
                            failedTargets.Add(currentTarget);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Instance != null)
                        Instance.Log($"Error in DetectStuck: {ex.Message}", LogLevel.ERROR);
                }
            }
            
            void HandleStuckState()
            {
                if (bradley == null)
                    return;
                
                // Check if we've been stuck too many times - might be completely trapped :'(
                int recentFailures = failedTargets.Count;
                if (recentFailures > 8)
                {
                    // Bradley is severely stuck, clear all failed targets and try radical recovery
                    failedTargets.Clear();
                    
                    // Generate a very simple path in the opposite direction
                    List<Vector3> escapeRoute = GenerateEscapeRoute();
                    ReplacePathSafely(escapeRoute);
                }
                else
                {
                    // Normal stuck recovery
                    List<Vector3> newPath = GenerateRecoveryPathList();
                    ReplacePathSafely(newPath);
                }
                    
                // Reset stuck state
                isStuck = false;
                ResetStuckDetection();
            }

            List<Vector3> GenerateEscapeRoute()
            {
                List<Vector3> escapePath = new List<Vector3>();
                Vector3 currentPos = bradley.transform.position;
                Vector3 patrolCenter = position;
                
                // Move directly toward patrol center
                Vector3 directionToCenter = (patrolCenter - currentPos).normalized;
                
                for (int i = 1; i <= 3; i++)
                {
                    Vector3 escapePoint = currentPos + directionToCenter * (i * 15f);
                    escapePoint.y = TerrainMeta.HeightMap.GetHeight(escapePoint);
                    
                    // Avoid water
                    float waterLevel = WaterSystem.OceanLevel;
                    float terrainHeight = TerrainMeta.HeightMap.GetHeight(escapePoint);
                    
                    if (waterLevel < terrainHeight)
                    {
                        escapePath.Add(escapePoint);
                    }
                }
                
                // If we got no points, add one in the direction of patrol center
                if (escapePath.Count == 0)
                {
                    Vector3 simpleEscape = currentPos + directionToCenter * 20f;
                    simpleEscape.y = TerrainMeta.HeightMap.GetHeight(simpleEscape);
                    escapePath.Add(simpleEscape);
                }
                
                return escapePath;
            }

            void ReplacePathSafely(List<Vector3> newPath)
            {
                if (bradley == null || bradley.IsDestroyed)
                    return;

                if (newPath == null || newPath.Count == 0)
                    newPath = GenerateEmergencyPath();

                if (newPath == null || newPath.Count == 0)
                    return;

                // Normalize height
                for (int i = 0; i < newPath.Count; i++)
                {
                    var p = newPath[i];
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    newPath[i] = p;
                }

                SafeRepath(bradley, () =>
                {
                    // Don't mutate existing list instance
                    bradley.currentPath = new List<Vector3>(newPath);
                    bradley.currentPathIndex = 0;

                    // Keep destinations consistent
                    bradley.destination = bradley.currentPath[0];
                    bradley.finalDestination = bradley.currentPath[bradley.currentPath.Count - 1];
                });
            }

            List<Vector3> GenerateEmergencyPath()
            {
                List<Vector3> emergencyPath = new List<Vector3>();
                Vector3 currentPos = bradley.transform.position;
                
                // Try to generate at least one valid point in any direction
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 randomDir = Random.insideUnitSphere;
                    randomDir.y = 0;
                    randomDir.Normalize();
                    
                    Vector3 emergencyPoint = currentPos + randomDir * Random.Range(10f, 20f);
                    emergencyPoint.y = TerrainMeta.HeightMap.GetHeight(emergencyPoint);
                    
                    // Basic check - not in water
                    float waterLevel = WaterSystem.OceanLevel;
                    float terrainHeight = TerrainMeta.HeightMap.GetHeight(emergencyPoint);
                    
                    if (waterLevel < terrainHeight)
                    {
                        emergencyPath.Add(emergencyPoint);
                        break;
                    }
                }
                
                // If still no points, move a short distance in any valid direction
                if (emergencyPath.Count == 0)
                {
                    Vector3 lastResort = currentPos + Vector3.forward * 10f;
                    lastResort.y = TerrainMeta.HeightMap.GetHeight(lastResort);
                    emergencyPath.Add(lastResort);
                }

                return emergencyPath;
            }
            
            List<Vector3> GenerateRecoveryPathList()
            {
                List<Vector3> recoveryPath = new List<Vector3>();
                
                try
                {
                    // Clean up old failed targets (keep only recent 5 points) maybe add config option later?
                    if (failedTargets.Count > 5)
                    {
                        failedTargets.RemoveRange(0, failedTargets.Count - 5);
                    }
                    
                    Vector3 currentPos = bradley.transform.position;
                    Vector3 patrolCenter = position; // Original drop position
                    
                    // Generate new path based on current state, avoiding failed targets
                    if (currentState == BradleyState.HUNT && lastKnownTargetPosition != Vector3.zero)
                    {
                        Vector3[] searchPoints = CreateSearchPattern(lastKnownTargetPosition);
                        foreach (Vector3 point in searchPoints)
                        {
                            if (IsValidPoint(point))
                                recoveryPath.Add(point);
                        }
                    }
                    else
                    {
                        // Generate patrol path
                        int pointCount = Random.Range(3, 6);
                        Vector3 lastPoint = currentPos;
                        
                        for (int i = 0; i < pointCount; i++)
                        {
                            Vector3 patrolPoint = GetValidPatrolPoint(patrolCenter, lastPoint);
                            if (patrolPoint != Vector3.zero)
                            {
                                recoveryPath.Add(patrolPoint);
                                lastPoint = patrolPoint;
                            }
                        }
                    }
                    
                    // Ensure we have at least one point
                    if (recoveryPath.Count == 0)
                    {
                        Vector3 safePoint = GetFallbackPoint(currentPos);
                        recoveryPath.Add(safePoint);
                    }
                    
                }
                catch
                {
                    // Emergency fallback
                    Vector3 emergencyPoint = bradley.transform.position + Random.insideUnitSphere * 20f;
                    emergencyPoint.y = TerrainMeta.HeightMap.GetHeight(emergencyPoint);
                    recoveryPath.Add(emergencyPoint);
                }
                return recoveryPath;
            }
            
            void UpdatePrimaryTarget()
            {
                if (bradley?.targetList == null)
                    return;
                    
                BasePlayer closestTarget = null;
                float closestDistance = float.MaxValue;
                
                var targetListCopy = new List<BradleyAPC.TargetInfo>(bradley.targetList);
                
                foreach (var targetInfo in targetListCopy)
                {
                    BaseEntity targetEnt = targetInfo?.entity;
                    if (targetEnt == null || targetEnt.IsDestroyed)
                        continue;

                    if (targetEnt.IsNpc || targetEnt is NPCPlayer || targetEnt.InSafeZone())
                    {
                        // This shouldn't happen because CustomUpdateTargetList() filters NPC
                        // But in case one slips by somehow!
                        continue;
                    }

                    if (targetInfo?.entity is BasePlayer player && targetInfo.IsVisible())
                    {
                        float distance = Vector3.Distance(bradley.transform.position, player.transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestTarget = player;
                        }
                    }
                }
                
                if (closestTarget != null)
                {
                    primaryTarget = closestTarget;
                    lastKnownTargetPosition = primaryTarget.transform.position;
                    lastTargetSeenTime = Time.time;
                }
                else if (primaryTarget != null && Time.time - lastTargetSeenTime > 3f)
                {
                    primaryTarget = null;
                }
            }
            
            void HandleStateTransitions()
            {
                BradleyState newState = currentState;
                
                if (primaryTarget != null)
                {
                    // Target acquired, enage!
                    newState = BradleyState.ENGAGE;
                }
                else if (currentState == BradleyState.ENGAGE && lastKnownTargetPosition != Vector3.zero && Time.time - lastTargetSeenTime < _apcProfile.TargetingDamage.MemoryDuration)
                {
                    // target lost, start hunting (actually scary! :D)
                    newState = BradleyState.HUNT;
                    if (currentState != BradleyState.HUNT)
                    {
                        huntStartTime = Time.time;
                    }
                }
                else if (currentState == BradleyState.HUNT && Time.time - huntStartTime > _apcProfile.TargetingDamage.MemoryDuration)
                {
                    // Hunt timeout, target lost, return to patrol area :(
                    newState = BradleyState.PATROL;
                }
                else if (currentState != BradleyState.PATROL && primaryTarget == null && (currentState != BradleyState.HUNT || Time.time - huntStartTime > _apcProfile.TargetingDamage.MemoryDuration))
                {
                    // Default back to patrol
                    newState = BradleyState.PATROL;
                }
                
                if (newState != currentState)
                {
                    currentState = newState;
                    
                    // If returning to patrol state, check if should return to patrol area first
                    if (newState == BradleyState.PATROL && IsOutsidePatrolRadius())
                    {
                        GenerateReturnToPatrolPath();
                    }
                    else
                    {
                        GeneratePathForCurrentState();
                    }
                }
            }
            
            bool ShouldGenerateNewPath()
            {
                // Check if Bradley has completed its current path
                if (bradley.currentPath == null || bradley.currentPath.Count == 0)
                    return true;
                    
                if (bradley.currentPathIndex >= bradley.currentPath.Count)
                    return true;
                    
                // Check if Bradley is near the final destination
                if (bradley.currentPath.Count > 0)
                {
                    Vector3 finalDestination = bradley.currentPath[bradley.currentPath.Count - 1];
                    float distanceToFinal = Vector3.Distance(bradley.transform.position, finalDestination);
                    if (distanceToFinal < 8f) // Approx length of Bradley
                        return true;
                }
                
                // If in patrol state and outside patrol radius, re-path
                if (currentState == BradleyState.PATROL && IsOutsidePatrolRadius())
                    return true;
                
                return false;
            }
            
            bool IsOutsidePatrolRadius()
            {
                Vector3 patrolCenter = position; // Original drop position
                float distanceFromCenter = Vector3.Distance(bradley.transform.position, patrolCenter);
                return distanceFromCenter > _apcProfile.Init.PatrolRange;
            }
            
            void GenerateReturnToPatrolPath()
            {
                List<Vector3> returnPath = new List<Vector3>();
                
                Vector3 patrolCenter = position;
                Vector3 currentPos = bradley.transform.position;
                
                // If outside patrol radius, first get back to the edge of patrol area
                if (IsOutsidePatrolRadius())
                {
                    Vector3 directionToCenter = (patrolCenter - currentPos).normalized;
                    Vector3 patrolEdgePoint = patrolCenter + directionToCenter * (_apcProfile.Init.PatrolRange * 0.8f);
                    patrolEdgePoint.y = TerrainMeta.HeightMap.GetHeight(patrolEdgePoint);
                    
                    if (IsValidPoint(patrolEdgePoint))
                    {
                        returnPath.Add(patrolEdgePoint);
                    }
                }
                
                // Now add normal patrol points within radius
                List<Vector3> patrolPoints = GeneratePatrolPathList();
                returnPath.AddRange(patrolPoints);
                
                ReplacePathSafely(returnPath);
            }
            
            void GeneratePathForCurrentState()
            {
                switch (currentState)
                {
                    case BradleyState.PATROL:
                    {
                        var newPath = GeneratePatrolPathList();
                        ReplacePathSafely(newPath);
                        break;
                    }
                    case BradleyState.HUNT:
                    {
                        var newPath = GenerateHuntPathList();
                        ReplacePathSafely(newPath);
                        break;
                    }
                    case BradleyState.ENGAGE:
                        StopMovementForEngagement();
                        break;
                }
            }

            void StopMovementForEngagement()
            {
                try
                {
                    if (bradley == null || bradley.IsDestroyed)
                        return;

                    if (primaryTarget == null)
                    {
                        var path = bradley.currentPath;
                        if (path != null && path.Count > 0)
                        {
                            bradley.currentPathIndex = path.Count - 1;
                        }
                        return;
                    }

                    float distanceToTarget =
                        Vector3.Distance(bradley.transform.position, primaryTarget.transform.position);

                    bool shouldReposition = false;

                    // Too far
                    if (distanceToTarget > optimalEngageRangeMax)
                        shouldReposition = true;

                    // Too close
                    else if (distanceToTarget < optimalEngageRangeMin)
                        shouldReposition = true;

                    // Target moved significantly
                    else if (lastEngagePosition != Vector3.zero &&
                            Vector3.Distance(primaryTarget.transform.position, lastEngagePosition) > 20f)
                        shouldReposition = true;

                    // Reposition cooldown gate
                    if (shouldReposition && Time.time - lastEngageRepositionTime > engageRepositionCooldown)
                    {
                        RepositionForEngagement();
                    }
                    else
                    {
                        // Do NOT touch path lists here, let internal AI handle stopping naturally.
                        var path = bradley.currentPath;
                        if (path != null && path.Count > 0)
                        {
                            bradley.currentPathIndex = path.Count - 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Instance != null)
                        Instance.Log($"Error in StopMovementForEngagement: {ex.Message}", LogLevel.ERROR);
                }
            }

            void RepositionForEngagement()
            {
                if (primaryTarget == null)
                    return;
                    
                Vector3 targetPos = primaryTarget.transform.position;
                Vector3 currentPos = bradley.transform.position;
                float distanceToTarget = Vector3.Distance(currentPos, targetPos);
                
                Vector3 newPosition;
                
                if (distanceToTarget > optimalEngageRangeMax)
                {
                    // Move closer, get to optimal max range
                    Vector3 direction = (targetPos - currentPos).normalized;
                    newPosition = targetPos - direction * (optimalEngageRangeMax * 0.8f);
                }
                else if (distanceToTarget < optimalEngageRangeMin)
                {
                    // Move away, back up to optimal min range
                    Vector3 direction = (currentPos - targetPos).normalized;
                    newPosition = targetPos + direction * (optimalEngageRangeMin * 1.2f);
                }
                else
                {
                    // Target moved, adjust position to track them
                    Vector3 direction = (targetPos - currentPos).normalized;
                    float optimalRange = (optimalEngageRangeMin + optimalEngageRangeMax) / 2f;
                    newPosition = targetPos - direction * optimalRange;
                }
                
                newPosition.y = TerrainMeta.HeightMap.GetHeight(newPosition);
                
                if (!IsValidPoint(newPosition))
                {
                    // If position is invalid, stay where we are
                    if (bradley.currentPath != null && bradley.currentPath.Count > 0)
                    {
                        bradley.currentPathIndex = bradley.currentPath.Count - 1;
                    }
                    return;
                }
                
                // Create a path with current position + new position
                List<Vector3> repositionPath = new List<Vector3>();
                
                // Add current position first to prevent index errors (hopefully!)
                repositionPath.Add(bradley.transform.position);
                
                // Add new position
                repositionPath.Add(newPosition);
                
                ReplacePathSafely(repositionPath);
                
                // Update tracking/engage variables
                lastEngagePosition = targetPos;
                lastEngageRepositionTime = Time.time;
            }

            List<Vector3> GeneratePatrolPathList()
            {
                List<Vector3> patrolPath = new List<Vector3>();
                
                Vector3 patrolCenter = position; // Original drop position
                Vector3 currentPos = bradley.transform.position;
                
                // Generate 3-5 patrol points with smart pathfinding
                int pointCount = Random.Range(3, 6);
                Vector3 lastPoint = currentPos;
                
                for (int i = 0; i < pointCount; i++)
                {
                    Vector3 patrolPoint = GetValidPatrolPoint(patrolCenter, lastPoint);
                    if (patrolPoint != Vector3.zero)
                    {
                        patrolPath.Add(patrolPoint);
                        lastPoint = patrolPoint;
                    }
                }
                
                // Ensure we have at least one point
                if (patrolPath.Count == 0)
                {
                    Vector3 safePoint = GetFallbackPoint(currentPos);
                    patrolPath.Add(safePoint);
                }
                return patrolPath;
            }
            
            List<Vector3> GenerateHuntPathList()
            {
                List<Vector3> huntPath = new List<Vector3>();
                
                if (lastKnownTargetPosition != Vector3.zero)
                {
                    Vector3[] searchPoints = CreateSearchPattern(lastKnownTargetPosition);
                    
                    foreach (Vector3 point in searchPoints)
                    {
                        if (IsValidPoint(point))
                            huntPath.Add(point);
                    }
                }
                return huntPath;
            }
            
            Vector3 GetValidPatrolPoint(Vector3 center, Vector3 fromPoint)
            {
                for (int attempt = 0; attempt < 12; attempt++) // Increased attempts to account for failed targets
                {
                    // Generate random point within patrol radius
                    Vector3 randomDir = Random.insideUnitSphere;
                    randomDir.y = 0;
                    randomDir.Normalize();
                    
                    // Ensure the distance keeps us within patrol radius
                    float maxDistance = _apcProfile.Init.PatrolRange * 0.9f; // Stay within 90% of max radius for safety
                    float minDistance = _apcProfile.Init.PatrolRange * 0.3f; // Minimum distance to avoid clustering around one spot
                    float distance = Random.Range(minDistance, maxDistance);
                    
                    Vector3 newPoint = center + randomDir * distance;
                    newPoint.y = TerrainMeta.HeightMap.GetHeight(newPoint);
                    
                    // Double check that newPoint is within patrol radius
                    float distanceFromCenter = Vector3.Distance(newPoint, center);
                    if (distanceFromCenter > _apcProfile.Init.PatrolRange)
                        continue;
                    
                    // Ensure minimum distance from last point
                    if (Vector3.Distance(newPoint, fromPoint) < 10f)
                        continue;
                    
                    // Check if this point is too close to any failed targets
                    bool tooCloseToFailedTarget = false;
                    foreach (Vector3 failedTarget in failedTargets)
                    {
                        if (Vector3.Distance(newPoint, failedTarget) < 15f) // Avoid failed targets by 15m
                        {
                            tooCloseToFailedTarget = true;
                            break;
                        }
                    }
                    
                    if (tooCloseToFailedTarget)
                        continue;
                        
                    // Validation newPoint
                    if (IsValidPoint(newPoint))
                    {
                        return newPoint;
                    }
                }
                
                // Fallback, if no good point found, get one within patrol radius
                return GetFallbackPatrolPoint(center, fromPoint);
            }
            
            Vector3 GetFallbackPatrolPoint(Vector3 center, Vector3 fromPoint)
            {
                // Try to find a fallback point within patrol radius and away from failed targets
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 randomDir = Random.insideUnitSphere;
                    randomDir.y = 0;
                    randomDir.Normalize();
                    
                    // Use shorter distance for fallback to ensure we stay in patrol radius
                    float distance = Random.Range(10f, _apcProfile.Init.PatrolRange * 0.6f);
                    Vector3 fallback = center + randomDir * distance;
                    fallback.y = TerrainMeta.HeightMap.GetHeight(fallback);
                    
                    // Ensure it's within patrol radius
                    if (Vector3.Distance(fallback, center) > _apcProfile.Init.PatrolRange)
                        continue;
                    
                    // Check if it's away from failed targets
                    bool clearOfFailedTargets = true;
                    foreach (Vector3 failedTarget in failedTargets)
                    {
                        if (Vector3.Distance(fallback, failedTarget) < 10f)
                        {
                            clearOfFailedTargets = false;
                            break;
                        }
                    }
                    
                    if (clearOfFailedTargets && IsValidPoint(fallback))
                    {
                        return fallback;
                    }
                }
                
                // Final fallback, just pick a safe point near center!
                Vector3 ultimateFallback = center + Random.insideUnitSphere * 10f;
                ultimateFallback.y = TerrainMeta.HeightMap.GetHeight(ultimateFallback);
                return ultimateFallback;
            }
            
            Vector3[] CreateSearchPattern(Vector3 centerPoint)
            {
                List<Vector3> searchPoints = new List<Vector3>();
                
                // Create expanding search pattern
                for (int ring = 1; ring <= 2; ring++)
                {
                    int pointsInRing = ring * 6;
                    for (int i = 0; i < pointsInRing; i++)
                    {
                        float angle = (360f / pointsInRing) * i * Mathf.Deg2Rad;
                        float radius = ring * 10f;
                        
                        Vector3 searchPoint = centerPoint + new Vector3(
                            Mathf.Cos(angle) * radius,
                            0,
                            Mathf.Sin(angle) * radius
                        );
                        searchPoint.y = TerrainMeta.HeightMap.GetHeight(searchPoint);
                        
                        if (IsValidPoint(searchPoint))
                            searchPoints.Add(searchPoint);
                            
                        if (searchPoints.Count >= 6) // Limit search points
                            break;
                    }
                    if (searchPoints.Count >= 6)
                        break;
                }
                
                return searchPoints.ToArray();
            }
            
            bool IsValidPoint(Vector3 point)
            {
                // Check for water
                float waterLevel = WaterSystem.OceanLevel;
                float terrainHeight = TerrainMeta.HeightMap.GetHeight(point);
                if (waterLevel >= terrainHeight)
                    return false;
                
                // Check if in safe zone
                if (Instance != null && Instance.IsInSafeZone(point))
                    return false;
                
                // Basic obstacle check
                Collider[] obstacles = Physics.OverlapSphere(point, 3f, LayerMask.GetMask("Construction", "Deployed"));
                if (obstacles.Length > 1) // Allow some small obstacles, add config option?
                    return false;
                
                return true;
            }
            
            Vector3 GetFallbackPoint(Vector3 fromPoint)
            {
                // Try to find a fallback point away from failed points
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 fallback = fromPoint + Random.insideUnitSphere * 20f;
                    fallback.y = TerrainMeta.HeightMap.GetHeight(fallback);
                    
                    bool clearOfFailedTargets = true;
                    foreach (Vector3 failedTarget in failedTargets)
                    {
                        if (Vector3.Distance(fallback, failedTarget) < 10f)
                        {
                            clearOfFailedTargets = false;
                            break;
                        }
                    }
                    
                    if (clearOfFailedTargets && IsValidPoint(fallback))
                    {
                        return fallback;
                    }
                }
                
                // Final fallback, just pick a direction away from failed attempts
                Vector3 ultimateFallback = fromPoint + Random.insideUnitSphere * 15f;
                ultimateFallback.y = TerrainMeta.HeightMap.GetHeight(ultimateFallback);
                return ultimateFallback;
            }

            private readonly HashSet<ulong> _repathBusy = new HashSet<ulong>();
            private bool waitingForInternalPath;

            private void SafeRepath(BradleyAPC apc, Action work)
            {
                if (apc == null || apc.IsDestroyed)
                    return;

                ulong id = apc.net.ID.Value;

                if (!_repathBusy.Add(id))
                    return;

                bool prev = apc.DoAI;
                apc.DoAI = false;

                Instance.NextTick(() =>
                {
                    try
                    {
                        work?.Invoke();
                    }
                    finally
                    {
                        if (apc != null && !apc.IsDestroyed)
                            apc.DoAI = prev;

                        waitingForInternalPath = false;
                        _repathBusy.Remove(id);
                    }
                });
            }

            #endregion Core Pathfinding
            
            #region Entity/Component Creation and Setup

            void AddBalloon()
            {
                balloon = GameManager.server.CreateEntity(balloonPrefab, bradley.transform.position, Quaternion.Euler(new Vector3(0f, 0f, 0f)), true) as HotAirBalloon;
                balloon.gameObject.Identity();
                balloon.myRigidbody.isKinematic = true;
                balloon.myRigidbody.detectCollisions = false;
                balloon.inflationLevel = 1f;
                balloon.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                balloon.OwnerID = owner.OwnerID;
                balloon.skinID = skinId;
                balloon.SetParent(bradley);
                balloon.Spawn();
            }

            void LiftBalloon()
            {
                if (liftBalloon >= 7)
                {
                    if (balloon != null)
                    {
                        Effect.server.Run(balloonExplosion, balloon.transform.position);
                        balloon.Kill(BaseNetworkable.DestroyMode.Gib);
                    }
                    CancelInvoke(nameof(LiftBalloon));
                    liftBalloon = 0;
                    return;
                }
                liftBalloon++;
                if (balloon != null)
                {
                    balloon.currentBuoyancy = 10f;
                    balloon.liftAmount = 1000f;
                    balloon.inflationLevel = 1f;
                    balloon.myRigidbody.AddForceAtPosition(((Vector3.up * balloon.liftAmount) * balloon.currentBuoyancy) * 10f, balloon.buoyancyPoint.position, ForceMode.Force);
                }
            }

            private void HandleCH47Delivery(string nextApcProfile)
            {
                var ch47 = CreateAndConfigureCH47();
                if (ch47 == null)
                    return;

                var bradley = CreateBradleyForCH47(nextApcProfile, ch47);
                if (bradley == null)
                    return;

                var bradComp = CreateBradleyComponent(bradley, nextApcProfile);
                if (bradComp == null)
                    return;

                bradComp.ch47Ai = ch47Ai;
                
                HandleBradleyDespawn(bradley, nextApcProfile);
                CreateCH47Component(ch47, bradley, nextApcProfile);
            }

            private void HandleBalloonDelivery(string nextApcProfile)
            {
                var bradley = CreateBradleyForBalloon(nextApcProfile);
                if (bradley == null)
                    return;

                var bradComp = CreateBradleyComponent(bradley, nextApcProfile);
                if (bradComp == null)
                    return;

                HandleBradleyDespawn(bradley, nextApcProfile);
            }

            private void HandleCargoPlaneDelivery(string nextApcProfile)
            {
                var plane = CreateAndConfigureCargoPlane(nextApcProfile);
                if (plane == null)
                    return;

                CreateCargoPlaneComponent(plane, nextApcProfile);
            }

            private CH47Helicopter CreateAndConfigureCH47()
            {
                float size = TerrainMeta.Size.x * config.ch47.mapScaleDistance;
                Vector2 rand = Random.insideUnitCircle.normalized;
                Vector3 pos = new Vector3(rand.x, 0, rand.y);
                pos *= size;
                pos += position + new Vector3(0f, config.ch47.ch47SpawnHeight, 0f);

                ch47 = GameManager.server.CreateEntity(ch47Prefab, pos, new Quaternion(), true) as CH47Helicopter;
                if (ch47 == null)
                    return null;

                ch47Ai = ch47.GetComponent<CH47HelicopterAIController>();
                if (ch47Ai == null)
                    return null;

                ch47Brain = ch47.GetComponent<CH47AIBrain>();
                if (ch47Brain == null)
                    return null;

                ch47.OwnerID = owner.userID;
                ch47.skinID = skinId;
                ch47.Spawn();

                ch47._maxHealth = _apcProfile.CH47.StartHealth;
                ch47.health = ch47._maxHealth;
                ch47.InitializeHealth(ch47._maxHealth, ch47._maxHealth);
                
                ch47Ai.CancelInvoke(ch47Ai.CheckSpawnScientists);
                ch47Ai.OwnerID = owner.userID;
                ch47Ai.skinID = skinId;
                ch47Ai.SetAimDirection(position);
                ch47Ai.SetDropDoorOpen(true);
                ch47Ai.SetMinHoverHeight(config.ch47.minHoverHeight);
                ch47Ai.SetLandingTarget(position);
                ch47Ai.numCrates = 0;

                ch47Brain.mainInterestPoint = position;

                CH47List.Add(ch47);
                return ch47;
            }

            private BradleyAPC CreateBradleyForCH47(string nextApcProfile, CH47Helicopter ch47)
            {
                var bradley = GameManager.server.CreateEntity(bradleyPrefab, ch47.transform.position, new Quaternion(), true) as BradleyAPC;
                if (bradley == null)
                    return null;

                bradley.OwnerID = owner.userID;
                bradley.skinID = _nextApcProfile.Init.SignalSkinID;
                bradley._name = nextApcProfile;
                bradley.EnableGlobalBroadcast(true);
                bradley.myRigidBody.detectCollisions = false;
                bradley.myRigidBody.isKinematic = true;
                bradley.transform.localPosition = new Vector3(0f, -3.25f, 0f);

                bradley.Spawn();
                bradley.SetParent(ch47);

                AddBradleyToWaveTracking(bradley);
                return bradley;
            }

            private BradleyAPC CreateBradleyForBalloon(string nextApcProfile)
            {
                float highestPoint = TerrainMeta.HighestPoint.y;
                Vector3 startPos = position;
                startPos.y = 0f;
                startPos.y = highestPoint + config.plane.planeHeight;

                var bradley = GameManager.server.CreateEntity(bradleyPrefab, startPos, new Quaternion(), true) as BradleyAPC;
                if (bradley == null)
                    return null;

                bradley.OwnerID = owner.userID;
                bradley.skinID = _nextApcProfile.Init.SignalSkinID;
                bradley._name = nextApcProfile;
                bradley.EnableGlobalBroadcast(true);
                bradley.Spawn();

                AddBradleyToWaveTracking(bradley);
                return bradley;
            }

            private CargoPlane CreateAndConfigureCargoPlane(string nextApcProfile)
            {
                plane = GameManager.server.CreateEntity(planePrefab, new Vector3(), new Quaternion(), true) as CargoPlane;
                if (plane == null)
                    return null;

                plane.OwnerID = owner.userID;
                plane.skinID = skinId;
                plane._name = nextApcProfile;
                plane.SendMessage("InitDropPosition", position, SendMessageOptions.DontRequireReceiver);
                plane.Spawn();

                CargoPlaneList.Add(plane);
                return plane;
            }

            private BradleyDrop CreateBradleyComponent(BradleyAPC bradley, string nextApcProfile)
            {
                var bradComp = bradley.gameObject.AddComponent<BradleyDrop>();
                if (bradComp == null)
                    return null;
                
                bradComp.owner = owner;
                bradComp.bradley = bradley;
                bradComp.apcProfile = nextApcProfile;
                bradComp.skinId = _nextApcProfile.Init.SignalSkinID;
                bradComp.position = position;
                bradComp.isWaveBradley = isWaveBradley;
                bradComp.waveProfile = waveProfile;
                bradComp.waveSkinId = waveSkinId;
                bradComp.waveTime = waveTime;
                bradComp.waveProfileCache = waveProfileCache;

                if (config.plane.skipCargoPlane)
                    bradComp.atDropZone = true;

                AddBradleyDropData(bradley);
                return bradComp;
            }

            private void AddBradleyDropData(BradleyAPC bradley)
            {
                BradleyDropData.Add(bradley.net.ID.Value, new ApcStats 
                { 
                    Owner = owner, 
                    OwnerID = owner.userID, 
                    OwnerName = owner.displayName,
                    ParentWaveProfile = isWaveBradley ? waveProfile : null,
                    WavePosition = config.bradley.waveConfig[waveProfile].WaveProfiles.Count - waveProfileCache.Count + 1,
                    WaveTotalCount = config.bradley.waveConfig[waveProfile].WaveProfiles.Count
                });
            }

            private void HandleBradleyDespawn(BradleyAPC bradley, string nextApcProfile)
            {
                if (Instance == null)
                    return;
                
                var despawnTime = _nextApcProfile.Init.DespawnTime;

                if (despawnTime > 0)
                    Instance.DespawnAPC(bradley, nextApcProfile, despawnTime);
            }

            private void CreateCH47Component(CH47Helicopter ch47, BradleyAPC bradley, string nextApcProfile = null)
            {
                var ch47Comp = ch47.gameObject.AddComponent<CH47DropComponent>();
                if (ch47Comp == null) return;

                ch47Comp.ch47 = ch47;
                ch47Comp.ch47Ai = ch47Ai;
                ch47Comp.ch47Brain = ch47Brain;
                ch47Comp.position = position;
                ch47Comp.bradley = bradley;

                // DEBUG: Fix for next waves not coming - Check this works now - Seems to!
                ch47Comp.bradComp = bradley.GetComponent<BradleyDrop>();
                ch47Comp._apcProfile = !string.IsNullOrEmpty(nextApcProfile)
                    ? config.bradley.apcConfig[nextApcProfile]  // look up the APCData from string key
                    : _apcProfile;                              // fall back to current profile
            }


            private void CreateCargoPlaneComponent(CargoPlane plane, string nextApcProfile)
            {
                var planeComponent = plane.gameObject.AddComponent<BradleyDropPlane>();
                planeComponent.plane = plane;
                planeComponent.player = owner;
                planeComponent.skinId = _nextApcProfile.Init.SignalSkinID;
                planeComponent.apcProfile = nextApcProfile;
                planeComponent.calledPosition = position;
                planeComponent.isWaveBradley = isWaveBradley;
                planeComponent.waveProfile = waveProfile;
                planeComponent.waveSkinId = waveSkinId;
                planeComponent.waveTime = waveTime;
                planeComponent.waveProfileCache = waveProfileCache;
                //planeComponent._apcProfile = _apcProfile;
            }

            #endregion Entity/Component Creation and Setup

            #region Wave Handling

            private void AddBradleyToWaveTracking(BradleyAPC bradley)
            {
                var bradleyId = bradley.net.ID.Value;
                if (isWaveBradley)
                    WavesCalled[waveTime].Add(bradleyId);
            }

            public void CallNextWaveApc(string nextApcProfile)
            {
                if (Instance == null)
                    return;

                if (!config.bradley.apcConfig.TryGetValue(nextApcProfile, out _nextApcProfile))
                {
                	if (Instance != null)
                    	Instance.Log($"No such profile: {nextApcProfile}, check config.", LogLevel.ERROR);
                    return;
                }

                var deliveryMethod = config.options.deliveryMethod.ToLower();

                switch (deliveryMethod)
                {
                    case "ch47":
                        HandleCH47Delivery(nextApcProfile);
                        break;
                    case "balloon" when config.plane.skipCargoPlane:
                        HandleBalloonDelivery(nextApcProfile);
                        break;
                    default:
                        HandleCargoPlaneDelivery(nextApcProfile);
                        break;
                }

                HandleWaveAnnouncement(false);
            }

            private void HandleWaveAnnouncement(bool hasCooldown)
            {
                if (Instance == null)
                    return;
                
                if (!isWaveBradley)
                    return;

                Instance.NextTick(() =>
                {
                    if (hasCooldown)
                    {
                        var waveCooldown = Math.Max(0, Math.Round(config.bradley.waveConfig[waveProfile].WaveCooldown / 60f, 2));
                        var message = Instance.Lang("NextApcCalledDelayed", new object [] {apcProfile, waveProfileCache[0], waveCooldown});
                        List<BasePlayer> attackers = Instance.GetAttackingTeam(owner);
                        if (attackers == null || attackers.Count == 0)
                            return;

                        Instance.AnnounceToChat(owner, attackers, message);
                    }
                    else
                    {
                        var message = Instance.Lang("NextApcInbound", waveProfileCache[0]);
                        List<BasePlayer> attackers = Instance.GetAttackingTeam(owner);
                        if (attackers == null || attackers.Count == 0)
                            return;

                        Instance.AnnounceToChat(owner, attackers, message);
                        // DEBUG: Change to same as helis, put attackers list inside AnnounceToChat hook
                    }
                });
            }

            private void HandleWaveCompletion()
            {
                if (Instance == null)
                    return;
                
                var message = Instance.PlayerLang("WaveFinished", owner.UserIDString, apcProfile);
                List<BasePlayer> attackers = Instance.GetAttackingTeam(owner);
                if (attackers != null && attackers.Count > 0)
                {
                    Instance.AnnounceToChat(owner, attackers, message);
                }                
                // Capture necessary data before component destruction
                var capturedOwner = owner;
                var capturedWaveProfile = waveProfile;
                var capturedBradleyId = bradleyId;
                
                Instance.timer.Once(1f, () => 
                {
                    SendWaveCompletionDiscord(capturedOwner, capturedWaveProfile);
                });
                
                // Use Instance timer for data cleanup
                RemoveApcData();
            }

            private void SendWaveCompletionDiscord(BasePlayer capturedOwner, string capturedWaveProfile)
            {
                if (Instance == null || !config.discord.sendApcKill)
                    return;
                
                try
                {
                    string steamUrl = steamProfileUrl + capturedOwner.userID;
                    string displayNameLink = $"[{capturedOwner.displayName}]({steamUrl})";
                    string steamIdLink = $"[{capturedOwner.UserIDString}]({steamUrl})";
                    
                    List<string> bradleyNames = new List<string>();
                    foreach (var profile in config.bradley.waveConfig[capturedWaveProfile].WaveProfiles)
                    {
                        if (config.bradley.apcConfig.ContainsKey(profile))
                        {
                            bradleyNames.Add($"💥 {config.bradley.apcConfig[profile].Init.APCName}");
                        }
                    }
                    string waveComposition = string.Join("\n", bradleyNames);
                    
                    var title = Instance.Lang("DiscordEmbedTitleWaveComplete", capturedWaveProfile);
                    var desc = Instance.Lang("DiscordEmbedOwner", displayNameLink, steamIdLink);
                    var footer = Instance.Lang("DiscordEmbedFooter", string.Empty) + $"  |  {zeodeFooterUrl}";
                    string color = "#FFD700"; // Gold color

                    var field = new List<DiscordField>
                    {
                        new DiscordField { Name = Instance.Lang("DiscordWaveCompleteStatus", string.Empty), Value = Instance.Lang("DiscordWaveCompleteAllDestroyed", string.Empty), Inline = true},
                        new DiscordField { Name = Instance.Lang("DiscordWaveCompleteTotal", string.Empty), Value = config.bradley.waveConfig[capturedWaveProfile].WaveProfiles.Count.ToString(), Inline = true},
                        new DiscordField { Name = Instance.Lang("DiscordWaveCompleteBradleys", string.Empty), Value = waveComposition, Inline = false}
                    };
                    
                    Instance.SendDiscordEmbed(title, desc, color, footer, field);
                }
                catch (Exception ex)
                {
                    // Log error but don't crash
                    if (Instance != null)
                        Instance.Log($"Error sending Discord notification: {ex.Message}", LogLevel.ERROR);
                }
            }

            #endregion Wave Handling

            public void RemoveApcData()
            {
                if (Instance == null)
                    return;
                
                Instance.timer.Once(10f, () =>
                {
                    if (BradleyDropData == null)
                        return;
                    
                    if (BradleyDropData.ContainsKey(bradleyId))
                        BradleyDropData.Remove(bradleyId);
                });
            }

            void OnDestroy()
            {
                CancelInvoke();
                StopAllCoroutines();
                
                if (balloon != null && !balloon.IsDestroyed)
                {
                    Effect.server.Run(balloonExplosion, balloon.transform.position);
                    balloon.Kill(BaseNetworkable.DestroyMode.Gib);
                }
                
                // Safely remove from cache
                if (bradley != null && bradCompCache != null && bradCompCache.ContainsKey(bradley))
                {
                    try
                    {
                        bradCompCache.Remove(bradley);
                    }
                    catch
                    {
                        // Ignore if collection is being modified
                    }
                }
                
                if (Instance == null)
                {
                    // Do immediate cleanup of data
                    if (BradleyDropData != null && BradleyDropData.ContainsKey(bradleyId))
                    {
                        try
                        {
                            BradleyDropData.Remove(bradleyId);
                        }
                        catch
                        {
                            // Ignore if collection is being modified
                        }
                    }
                    return;
                }

                if (!isDespawning || isDying)
                {
                    ProcessPostDestroy();
                }
            }

            private void ProcessPostDestroy()
            {
                if (Instance == null)
                    return;
                
                if (BradleyDropData != null)
                    Instance.ProcessRewards(bradleyId, owner.userID, apcProfile);
                
                if (isWaveBradley && waveProfileCache != null && waveProfileCache.Count > 0)
                {
                    waveProfileCache.RemoveAt(0);
                    
                    if (waveProfileCache.Count <= 0)
                    {
                        HandleWaveCompletion();
                    }
                    else
                    {
                        HandleWaveAnnouncement(true);

                        if (config.bradley.waveConfig != null && !string.IsNullOrEmpty(waveProfile) && config.bradley.waveConfig.ContainsKey(waveProfile))
                        {
                            float nextWave = config.bradley.waveConfig[waveProfile].WaveCooldown;
                            Instance.timer.Once(nextWave, () =>
                            {
                                if (waveProfileCache != null && waveProfileCache.Count > 0)
                                    CallNextWaveApc(waveProfileCache[0]);
                            });
                        }
                        else
                        {
                        	if (Instance != null)
                            	Instance.Log($"Invalid wave configuration for profile: {waveProfile}", LogLevel.ERROR);
                        }
                    }

                }
                else if (!isWaveBradley)
                {
                    RemoveApcData();
                }
            }
        }

        public class BradleyBuildBlockZone : FacepunchBehaviour
        {
            public BradleyDrop bradComp;
            public BaseEntity entity;
            public Vector3 centre;

            void OnDestroy()
            {
                Instance?.UnregisterBuildBlockZone(this);
            }
        }

        #endregion Monos etc
        
        #region Temporary Data

        private static Dictionary<ulong, ApcStats> BradleyDropData = new Dictionary<ulong, ApcStats>();
        private static List<CargoPlane> CargoPlaneList = new List<CargoPlane>();
        private static List<CH47Helicopter> CH47List = new List<CH47Helicopter>();
        private static Dictionary<ulong, float> PlayerCooldowns = new Dictionary<ulong, float>();
        private static Dictionary<ulong, Dictionary<string, float>> TierCooldowns = new Dictionary<ulong, Dictionary<string, float>>();
        private static readonly Dictionary<ulong, ulong> BradleyCrates = new Dictionary<ulong, ulong>();
        private static readonly Dictionary<ulong, LockedCrateData> LockedCrates = new Dictionary<ulong, LockedCrateData>();
        private static List<string> ApcProfiles = new List<string>();
        private static List<string> WaveProfiles = new List<string>();
        private static Dictionary<float, List<ulong>> WavesCalled = new Dictionary<float, List<ulong>>();
        private static List<MonumentInfo> Monuments = new List<MonumentInfo>();
        private static Dictionary<ulong, string> BradleyProfileCache = new Dictionary<ulong, string>();
        private readonly HashSet<BradleyBuildBlockZone> activeBuildBlockZones = new HashSet<BradleyBuildBlockZone>();

        private class LockedCrateData
        {
            public ulong PlayerId;
            public bool IsCH47;
            public float DecayTime;
        }

        private class ApcStats
        {
            public BasePlayer Owner;
            public ulong OwnerID;
            public string OwnerName;
            public string ParentWaveProfile;
            public int WavePosition;
            public int WaveTotalCount;
            public float StartTime = Time.realtimeSinceStartup;
            public BasePlayer LastAttacker;
            public Dictionary<ulong, AttackersStats> Attackers = new Dictionary<ulong, AttackersStats>();
            public int WarningLevel = 0;
            public float PlayerDamage = 0f;
            public float TurretDamage = 0f;
            public AutoTurret LastTurretAttacker;
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

        #region Config
        
        #region Config Structure

        public static ConfigData config;

        public class ConfigData
        {
            [JsonProperty(PropertyName = "General Options")]
            public Options options { get; set; }
            [JsonProperty(PropertyName = "Cargo Plane Delivery Options")]
            public PlaneDelivery plane { get; set; }
            [JsonProperty(PropertyName = "CH47 Chinook Delivery Options")]
            public CH47Delivery ch47 { get; set; }
            [JsonProperty(PropertyName = "Announce Options")]
            public Announce announce { get; set; }
            [JsonProperty(PropertyName = "Discord Options")]
            public Discord discord { get; set; }
            [JsonProperty(PropertyName = "Reward Options")]
            public Rewards rewards { get; set; }
            [JsonProperty(PropertyName = "Purchasing Options")]
            public Purchasing purchasing { get; set; }
            [JsonProperty(PropertyName = "Bradley APC Options")]
            public Bradley bradley { get; set; }
            
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
                [JsonProperty(PropertyName = "Allow Epic Loot to add items to crates")]
                public bool allowEpicLoot { get; set; }
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
                [JsonProperty(PropertyName = "Bradley Drop Delivery Method (CH47 | Balloon)")]
                public string deliveryMethod { get; set; }
                [JsonProperty(PropertyName = "Min Distance From Building Privilege To Use Signals (Important: Greater or Equal To Proximity Check Radius)")]
                public float buildPrivRadius { get; set; }
                [JsonProperty(PropertyName = "Strict Proximity Check (Checks for objects close to signal, prevents APC landing on objects)")]
                public bool strictProximity { get; set; }
                [JsonProperty(PropertyName = "Strict Proximity Check Radius")]
                public float proximityRadius { get; set; }
                [JsonProperty(PropertyName = "Allow Calling on Foundations (Arena Compatibility)")]
                public bool allowOnFoundations { get; set; }
                [JsonProperty(PropertyName = "Remove Entities In Landing Zone Radius (Requires Strict Proximity Check Enabled)")]
                public bool clearLandingZone { get; set; }
                [JsonProperty(PropertyName = "Remove Timed Explosives In Landing Zone Radius (Requires Strict Proximity Check Enabled)")]
                public bool clearTimedExplosives { get; set; }
                [JsonProperty(PropertyName = "Disable vanilla Bradley APC at Launch Site")]
                public bool noVanillaApc { get; set; }
                [JsonProperty(PropertyName = "Use this plugin to control stacking/combing Bradley Drop signal items")]
                public bool useStacking { get; set; }
                [JsonProperty(PropertyName = "Command to Show Details of Players Own Active Bradleys (Admin Perm Allows to See ALL Active Bradleys)")]
                public string reportCommand { get; set; }
                [JsonProperty(PropertyName = "Enable Debug Logging")]
                public bool debug { get; set; }
            }

            public class PlaneDelivery
            {
                [JsonProperty(PropertyName = "Skip cargo plane & spawn a delivery above the location according to plane height setting")]
                public bool skipCargoPlane { get; set; }
                [JsonProperty(PropertyName = "Map Scale Distance Away to Spawn Cargo Plane for Balloon Delivery (Default: 2 = 2 x Map Size Distance)")]
                public float mapScaleDistance { get; set; }
                [JsonProperty(PropertyName = "Cargo Plane Speed for Balloon Delivery (Rust Default = 35)")]
                public float planeSpeed { get; set; }
                [JsonProperty(PropertyName = "Cargo Plane Height Above The Heighest Point On The Map for Balloon Delivery")]
                public float planeHeight { get; set; }
                [JsonProperty(PropertyName = "Bradley Drop Falling Drag (Lower = Faster. Default: 0.6)")]
                public float fallDrag { get; set; }
            }

            public class CH47Delivery
            {
                [JsonProperty(PropertyName = "Map Scale Distance Away to Spawn CH47 (eg: 1.0 = 1 x Map Size Distance)")]
                public float mapScaleDistance { get; set; }
                [JsonProperty(PropertyName = "Height of CH47 When it Spawns (Increase if it Spawns in Terrain)")]
                public float ch47SpawnHeight { get; set; }
                [JsonProperty(PropertyName = "Minimum Height of CH47 as it Approaches Landing Zone (Increase if it Hits Terrain)")]
                public float minHoverHeight { get; set; }
            }

            public class Announce
            {
                [JsonProperty(PropertyName = "Announce When Player Calls a Bradley Drop in chat")]
                public bool callChat { get; set; }
                [JsonProperty(PropertyName = "Announce Bradley Kill In Chat")]
                public bool killChat { get; set; }
                [JsonProperty(PropertyName = "Announce Damage Report In Chat")]
                public bool reportChat { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Bradley Kill in Chat")]
                public bool killVanilla { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Bradley Damage Report in Chat")]
                public bool reportVanilla { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Bradley Display Name")]
                public string vanillaName { get; set; }
                [JsonProperty(PropertyName = "Announce Server/Vanilla Bradley Owner Name")]
                public string vanillaOwner { get; set; }
                [JsonProperty(PropertyName = "Max Number Players Displayed in Damage Report")]
                public int maxReported { get; set; }
                [JsonProperty(PropertyName = "Announcements Also Go To Global Chat (false = Player/Team Only)")]
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
                [JsonProperty(PropertyName = "Announce to Discord when Bradley is called")]
                public bool sendApcCall { get; set; }
                [JsonProperty(PropertyName = "Announce to Discord when Bradley is killed")]
                public bool sendApcKill { get; set; }
                [JsonProperty(PropertyName = "Announce to Discord when Bradley de-spawns")]
                public bool sendApcDespawn { get; set; }
            }

            public class Rewards
            {
                [JsonProperty(PropertyName = "Rewards Plugin (ServerRewards | Economics)")]
                public string rewardPlugin { get; set; }
                [JsonProperty(PropertyName = "Currency Unit Displayed e.g: RP | $")]
                public string rewardUnit { get; set; }
                [JsonProperty(PropertyName = "Enable Rewards")]
                public bool enableRewards { get; set; }
                [JsonProperty(PropertyName = "Share Reward Between Players Above Damage Threshold")]
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
                public void RegisterPermissions(Permission permission, BradleyDrops plugin)
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
                public List<CurrencyItem> customCurrency { get; set; }
            }

            public class Bradley
            {
                [JsonProperty(PropertyName = "Player Give Up and Despawn Command (Despawns All of That Players Bradleys, NO Refund Given)")]
                public string despawnCommand { get; set; }
                [JsonProperty(PropertyName = "Team Can Deswpan Bradleys Using the Command (Requires Use Friends/Clans/Teams option)")]
                public bool canTeamDespawn { get; set; }
                [JsonProperty(PropertyName = "Global Bradley Limit (0 = No Limit)")]
                public int globalLimit { get; set; }
                [JsonProperty(PropertyName = "Player Bradley Limit (0 = No Limit)")]
                public int playerLimit { get; set; }
                [JsonProperty(PropertyName = "Max Distance Bradley Can Be Damaged By Any Player (0 = Disabled)")]
                public float maxHitDistance { get; set; }
                [JsonProperty(PropertyName = "Use Explosion Effect When Bradley Despawns")]
                public bool despawnExplosion { get; set; }
                [JsonProperty(PropertyName = "Despawn if Attacking Player is Building Blocked, While 'Block Damage to Other Players Bases' is True")]
                public bool DespawnWarning { get; set; }
                [JsonProperty(PropertyName = "Despawn Warning Threshold (Number of Warnings Allowed Before Despawning)")]
                public int WarningThreshold { get; set; }

                // DEBUG: Update old config field name. Remove this later.
                [JsonProperty("Use NoEscape")]
                public bool GetuseNoEscape { get { return useNoEscape; } set { useNoEscape = value; } }
                public bool ShouldSerializeGetuseNoEscape() => false;
                // #######################

                [JsonProperty(PropertyName = "NoEscape: Allow NoEscape to Prevent Calling Bradley Drops")]
                public bool useNoEscape { get; set; }
                [JsonProperty(PropertyName = "Player Cooldown (Seconds) Between Bradley Drop Calls (0 = No Cooldown)")]
                public float playerCooldown { get; set; }
                [JsonProperty(PropertyName = "Player Cooldowns Apply to Each Tier Seperately")]
                public bool tierCooldowns { get; set; }
                [JsonProperty(PropertyName = "Cooldown Applies to Clan/Team/Friends (Requires Use Friends/Use Clan/Use Teams)")]
                public bool teamCooldown { get; set; }
                [JsonProperty(PropertyName = "Allow Players to Damage Bradleys With Remote Auto Turrets")]
                public bool allowTurretDamage { get; set; }
                [JsonProperty(PropertyName = "Bradley Attacks Player Controlled Auto Turrets if Majority Damage Comes From Them")]
                public bool apcTargetTurret { get; set; }
                [JsonProperty(PropertyName = "Bradley Targets and Attacks Players Inside or Behind Vehicles")]
                public bool apcTargetVehicles{ get; set; }
                [JsonProperty(PropertyName = "How Long Attacks On Player Controlled Auto Turrets Lasts Before Changing Target")]
                public float turretAttackTime { get; set; }
                [JsonProperty(PropertyName = "Cooldown Before Bradley Can Attack Player Controlled Turrets Again (Seconds)")]
                public float turretCooldown { get; set; }
                [JsonProperty(PropertyName = "Penalize Players With Majority Damage From Auto Turrets by This Percentage (0 = No Penalty)")]
                public double turretPenalty { get; set; }
                [JsonProperty(PropertyName = "Allow Players to Call Bradleys in Deep Sea")]
                public bool allowInDeepSea { get; set; }
                [JsonProperty(PropertyName = "Allow Players to Call Bradleys at Monuments")]
                public bool allowMonuments { get; set; }
                [JsonProperty(PropertyName = "Minimum Distance From Monuments When Allow at Monuments is False")]
                public float distFromMonuments { get; set; }
                [JsonProperty(PropertyName = "Monuments (Prefabs) to Either Block or Allow When Allow at Monuments is Either True or False")]
                public List<string> excludedMonuments { get; set; }
                [JsonProperty(PropertyName = "Build Blocking")]
                public BuildBlocking buildBlocking { get; set; }
                [JsonProperty(PropertyName = "VIP/Custom Cooldowns")]
                public Hash<string, float> vipCooldowns { get; set; }
                [JsonProperty(PropertyName = "Protected Prefab List (Prefabs Listed Here Will Never Take Damage)")]
                public List<string> protectedPrefabs { get; set; }
                [JsonProperty(PropertyName = "Bradley Wave Options")]
                public Dictionary<string, WaveData> waveConfig { get; set; }
                [JsonProperty(PropertyName = "Profiles")]
                public Dictionary<string, APCData> apcConfig { get; set; }
				
                [JsonIgnore]
                public Permission permission;
                public void RegisterPermissions(Permission permission, BradleyDrops plugin)
                {
                    this.permission = permission;
                    foreach (string key in vipCooldowns.Keys)
                    {
                        if (!permission.PermissionExists(key, plugin))
                            permission.RegisterPermission(key, plugin);
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
            [JsonProperty(PropertyName = "Min amount")]
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

        public class BuildBlocking
        {
            [JsonProperty(PropertyName = "Enable Building Blocked Zone Around Bradleys")]
            public bool EnableBuildBlockZone { get; set; }
            [JsonProperty(PropertyName = "Building Block Zone Radius")]
            public float BuildBlockZoneRadius { get; set; }
            [JsonProperty(PropertyName = "Building Blocked Zone Follows Bradley")]
            public bool BuildBlockZoneFollowsBradley { get; set; }
            [JsonProperty(PropertyName = "Block deployable placement")]
            public bool BlockDeployables { get; set; }
            [JsonProperty(PropertyName = "Prefabs to Allow When Building Blocked Zone is Enabled")]
            public List<string> NoBlockPrefabs { get; set; }
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
            [JsonProperty(PropertyName = "Bradley Wave Profile List (Bradleys Called in Order From Top to Bottom)")]
            public List<string> WaveProfiles { get; set; }
        }

        public class APCData
        {
        	// Init/Setup
            [JsonProperty(PropertyName = "Init & Setup Options")]
            public InitOptions Init { get; set; }
            
            public class InitOptions
            {
                [JsonProperty(PropertyName = "Bradley display name")]
                public string APCName { get; set; }
                [JsonProperty(PropertyName = "Skin ID of the custom Supply Signal")]
                public ulong SignalSkinID { get; set; }
                [JsonProperty(PropertyName = "Profile shortname (for use in permission and give command)")]
                public string GiveItemCommand { get; set; }
                [JsonProperty(PropertyName = "Enable purchasing via the buy command")]
                public bool UseBuyCommand { get; set; }
                [JsonProperty(PropertyName = "Cost to purchase (using buy command)")]
                public int CostToBuy { get; set; }
                [JsonProperty(PropertyName = "Starting health")]
                public float Health { get; set; }
                [JsonProperty(PropertyName = "Patrol radius")]
                public float PatrolRange { get; set; }
                [JsonProperty(PropertyName = "Search range")]
                public float SearchRange { get; set; }
                [JsonProperty(PropertyName = "Stuck detection time (seconds)")]
                public float StuckDetectionTime { get; set; }
                [JsonProperty(PropertyName = "Stuck detection threshold (meters)")]
                public float StuckDetectionThreshold { get; set; }
                [JsonProperty(PropertyName = "Prevent damage while falling")]
                public bool ChuteProtected { get; set; }
                [JsonProperty(PropertyName = "Throttle response (1.0 = default)")]
                public float ThrottleResponse { get; set; }
                [JsonProperty(PropertyName = "Kill if APC goes in SafeZone")]
                public bool KillInSafeZone { get; set; }
                [JsonProperty(PropertyName = "Despawn timer")]
                public float DespawnTime { get; set; }
            }
			
            // Targeting/Damage
            [JsonProperty(PropertyName = "Targeting & Damage")]
            public TargetingDamageOptions TargetingDamage { get; set; }
            
            public class TargetingDamageOptions
            {
                [JsonProperty(PropertyName = "Attack owner")]
                public bool AttackOwner { get; set; }
                [JsonProperty(PropertyName = "Target sleeping players")]
                public bool TargetSleepers { get; set; }
                [JsonProperty(PropertyName = "Target memory duration (default = 20.0)")]
                public float MemoryDuration { get; set; }
                [JsonProperty(PropertyName = "Target hostile drones with top turret")]
                public bool TargetDrones { get; set; }
                [JsonProperty(PropertyName = "Maximum drone targeting distance (default = 80.0)")]
                public float DroneTargetRange { get; set; }
                [JsonProperty(PropertyName = "Only owner can damage (and team if enabled) ")]
                public bool OwnerDamage { get; set; }
                [JsonProperty(PropertyName = "Allow Bradley to target other players")]
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
                [JsonProperty(PropertyName = "Bradley crate despawn time (seconds)")]
                public float BradleyCrateDespawn { get; set; }
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
                [JsonProperty(PropertyName = "Counter players using weapons in the Smoked weapon list")]
                public bool CanUseSmokeGrenades { get; set; }
                [JsonProperty(PropertyName = "Smoke grenade cooldown (seconds)")]
                public float SmokeGrenadesCooldown { get; set; }
                [JsonProperty(PropertyName = "Smoke grenade burst amount")]
                public int SmokeGrenadesAmount { get; set; }
                [JsonProperty(PropertyName = "Smoke grenade target radius")]
                public float SmokeGrenadesRadius { get; set; }
                [JsonProperty(PropertyName = "Smoke grenade fire rate (Default = 0.2)")]
                public float SmokeGrenadesRate { get; set; }
                [JsonProperty(PropertyName = "Smoked weapon list")]
                public List<string> SmokedWeapons { get; set; }
                [JsonProperty(PropertyName = "Coax machine gun burst delay (turret mounted machine gun, default = 1.0)")]
                public float CoaxBurstDelay { get; set; }
                [JsonProperty(PropertyName = "Coax machine gun fire rate (default = 0.05)")]
                public float CoaxFireRate { get; set; }
                [JsonProperty(PropertyName = "Coax machine gun burst length (default = 15)")]
                public int CoaxBurstLength { get; set; }
                [JsonProperty(PropertyName = "Coax machine gun aim cone (default = 3.0)")]
                public float CoaxAimCone { get; set; }
                [JsonProperty(PropertyName = "Coax machine gun target range (default = 40.0)")]
                public float CoaxTargetRange { get; set; }
                [JsonProperty(PropertyName = "Tank gun burst delay (large turret mounted gun, default = 5.0)")]
                public float MainCannonBurstDelay { get; set; }
                [JsonProperty(PropertyName = "Tank gun fire rate (default = 0.25)")]
                public float MainCannonFireRate { get; set; }
                [JsonProperty(PropertyName = "Tank gun burst length (default = 4)")]
                public int MainCannonBurst { get; set; }
                [JsonProperty(PropertyName = "Tank gun aim cone (default = 2.0)")]
                public float MainGunAimCone { get; set; }
                [JsonProperty(PropertyName = "Tank gun projectile velocity (default = 100.0)")]
                public float MainGunProjectileVelocity { get; set; }
                [JsonProperty(PropertyName = "Tank gun range/search range (default = 50.0)")]
                public float MainGunRange { get; set; }
                [JsonProperty(PropertyName = "Top turret machine gun fire rate (swivelling top machine gun, default = 0.25)")]
                public float TopTurretFireRate { get; set; }
                [JsonProperty(PropertyName = "Top turret machine gun fire rate while attacking drones")]
                public float TopTurretDroneFireRate { get; set; }
                [JsonProperty(PropertyName = "Top turret machine gun aim cone (default = 3.0)")]
                public float TopTurretAimCone { get; set; }
                [JsonProperty(PropertyName = "Gun Damage scale - TO PLAYER (1.0 = default, 2.0 = 2x, etc)")]
                public float GunDamage { get; set; }
                [JsonProperty(PropertyName = "Main cannon damage scale - TO PLAYER (1.0 = default, 2.0 = 2x, etc)")]
                public float ShellDamageScale { get; set; }
                [JsonProperty(PropertyName = "Gun Damage scale - TO BUILDINGS (1.0 = default, 2.0 = 2x, etc)")]
                public float GunBuildDamage { get; set; }
                [JsonProperty(PropertyName = "Main cannon damage scale - TO BUILDINGS (1.0 = default, 2.0 = 2x, etc)")]
                public float ShellBuildDamageScale { get; set; }
            }
            
            // Deployed NPCs
            [JsonProperty(PropertyName = "Bradley Scientist Options")]
            public BradleyScientistOptions BradleyScientists { get; set; }
            
            public class BradleyScientistOptions
            {
                [JsonProperty(PropertyName = "Allow Bradley to Deploy Scientists When Under Attack")]
                public bool DeployScientists { get; set; }
                [JsonProperty(PropertyName = "Only Allow Event Owner/Team to Attack Deployed NPCs")]
                public bool OwnDeployedNPC { get; set; }
                [JsonProperty(PropertyName = "Deployed NPCs can be targeted by auto turrets (not player controlled)")]
                public bool CanTurretTargetNpc { get; set; }
                [JsonProperty(PropertyName = "HP Threshold For First Scientist Deployment (Default = 0.8)")]
                public float DeployThreshold1 { get; set; }
                [JsonProperty(PropertyName = "HP Threshold For Second Scientist Deployment (Default = 0.6)")]
                public float DeployThreshold2 { get; set; }
                [JsonProperty(PropertyName = "HP Threshold For Final Scientist Deployment (Default = 0.4)")]
                public float DeployThreshold3 { get; set; }
                [JsonProperty(PropertyName = "Use Custom Kits For Deployed Scientists")]
                public bool UseCustomKits { get; set; }
                [JsonProperty(PropertyName = "Start Health of Deployed Scientists (Default = 150)")]
                public float ScientistHealth { get; set; }
                [JsonProperty(PropertyName = "Damage Scale of Deployed Scientists (Default = 0.75)")]
                public float ScientistDamageScale { get; set; }
                [JsonProperty(PropertyName = "Aim Cone Scale of Deployed Scientists (Less is More Accurate. Default = 2.0)")]
                public float ScientistAimCone { get; set; }
                [JsonProperty(PropertyName = "Scientist Kits")]
                public List<string> ScientistKits { get; set; }
                [JsonProperty(PropertyName = "Start Health For Deployed Heavy Scientists (Default = 300)")]
                public float HeavyHealth { get; set; }
                [JsonProperty(PropertyName = "Damage Scale For Deployed Heavy Scientists (Default = 0.5)")]
                public float HeavyDamageScale { get; set; }
                [JsonProperty(PropertyName = "Aim Cone Scale of Deployed Heavy Scientists (Less is More Accurate. Default = 2.0)")]
                public float HeavyAimCone { get; set; }
                [JsonProperty(PropertyName = "Heavy Scientist Kits")]
                public List<string> HeavyKits { get; set; }
                [JsonProperty(PropertyName = "Despawn Scientists When Bradley Destroyed")]
                public bool Destroy { get; set; }
                [JsonProperty(PropertyName = "Despawn Timer in Seconds After Bradley is Destroyed (0 = Disabled)")]
                public float DespawnTime { get; set; }
            }
			
            // CH47 Options
            [JsonProperty(PropertyName = "CH47 Options")]
            public CH47Options CH47 { get; set; }
            
            public class CH47Options
            {
                [JsonProperty(PropertyName = "CH47 Can Take Damage")]
                public bool CanDamage { get; set; }
                [JsonProperty(PropertyName = "Starting Health (Default = 3000)")]
                public float StartHealth { get; set; }
                [JsonProperty(PropertyName = "Can be Targeted by Homing Missiles")]
                public bool IsHomingTarget { get; set; }
                [JsonProperty(PropertyName = "Can Counter Homing Missiles With Flares")]
                public bool CanUseFlares { get; set; }
                [JsonProperty(PropertyName = "Flare Active Duration (Seconds)")]
                public float FlareDuration { get; set; }
                [JsonProperty(PropertyName = "Flare Cooldown (Seconds)")]
                public float FlareCooldown { get; set; }
                [JsonProperty(PropertyName = "Number of Locked Hackable Crates to Spawn When Destroyed")]
                public int LockedCratesToSpawn { get; set; }
                [JsonProperty(PropertyName = "Hack Time for Locked Crate (Seconds)")]
                public float HackSeconds { get; set; }
                [JsonProperty(PropertyName = "Locked Crate Despawn Time (Seconds)")]
                public float LockedCrateDespawn { get; set; }
                [JsonProperty(PropertyName = "Disable locked crate map markers")]
                public bool DisableMapMarker { get; set; }
                [JsonProperty(PropertyName = "Lock Looting Crates to Owner")]
                public bool ProtectCrates { get; set; }
                [JsonProperty(PropertyName = "Unlock Looting Crates to Others After Time in Seconds (0 = Never)")]
                public float UnlockCrates { get; set; }
                [JsonProperty(PropertyName = "Spawn Full CH47 Gun Crew to Attack Valid Targets at Drop Zone")]
                public bool SpawnScientists { get; set; }
                [JsonProperty(PropertyName = "Distance From Landing Zone Before CH47 Scientists Will Engage")]
                public float EngageRange { get; set; }
                [JsonProperty(PropertyName = "Only Allow Event Owner/Team to Attack CH47 NPCs")]
                public bool OwnDeployedNPC { get; set; }
                [JsonProperty(PropertyName = "NPCs can be targeted by auto turrets")]
                public bool CanTurretTargetNpc { get; set; }
                [JsonProperty(PropertyName = "Use Custom Kits For CH47 Scientists")]
                public bool UseCustomKits { get; set; }
                [JsonProperty(PropertyName = "Start Health of CH47 Scientists (Default = 150)")]
                public float ScientistHealth { get; set; }
                [JsonProperty(PropertyName = "Damage Scale of CH47 Scientists (Default = 0.75)")]
                public float ScientistDamageScale { get; set; }
                [JsonProperty(PropertyName = "Aim Cone Scale of CH47 Scientists (Less is More Accurate. Default = 2.0)")]
                public float ScientistAimCone { get; set; }
                [JsonProperty(PropertyName = "CH47 Scientist Kits")]
                public List<string> ScientistKits { get; set; }
                [JsonProperty(PropertyName = "CH47 Orbits the Drop Zone After Dropping & Scientists Engage Targets")]
                public bool OrbitDropZone { get; set; }
                [JsonProperty(PropertyName = "Orbit Duration (Seconds)")]
                public float OrbitDropZoneDuration { get; set; }
            }
            
            // Gibs
            [JsonProperty(PropertyName = "Gibs Options")]
            public GibsOptions Gibs { get; set; }
            
            public class GibsOptions
            {
                [JsonProperty(PropertyName = "Disable Bradley gibs")]
                public bool KillGibs { get; set; }
                [JsonProperty(PropertyName = "Gibs too hot to harvest time (Seconds)")]
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
                [JsonProperty(PropertyName = "Disable Bradley fire")]
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
                [JsonProperty(PropertyName = "BotReSpawn profile to spawn at Bradley kill site (leave blank for not using)")]
                public string BotReSpawnProfile { get; set; }
            }

            // ZoneManager
            [JsonProperty(PropertyName = "ZoneManager Options")]
            public ZoneManagerOptions ZoneManager { get; set; }
            
            public class ZoneManagerOptions
            {
                [JsonProperty(PropertyName = "Use Zone Manager options")]
                public bool UseZoneManager { get; set; }
                [JsonProperty(PropertyName = "Players can only call in drop zones")]
                public bool UseDropZones { get; set; }
                [JsonProperty(PropertyName = "Bradley can leave drop zone")]
                public bool CanLeaveZone { get; set; }
                [JsonProperty(PropertyName = "Drop zone ID list")]
                public List<string> DropZoneIDs { get; set; }
                [JsonProperty(PropertyName = "Prevent bradley entering protected zones")]
                public bool UseProtectedZones { get; set; }
                [JsonProperty(PropertyName = "Protected zone ID list")]
                public List<string> ProtectedZoneIDs { get; set; }
            }

            // Run Commands
            [JsonProperty(PropertyName = "Commands Options")]
            public CommandsToRunOptions CommandsToRun { get; set; }
            
            public class CommandsToRunOptions
            {
                [JsonProperty(PropertyName = "Run List Of Commands On Bradley Death")]
                public bool RunCommands { get; set; }
                [JsonProperty(PropertyName = "Command list (top -> bottom). Available placeholders: Owner SteamID: {ownerId} Calling Team SteamID List: {teamIds}  Heli Profile (Name): {apcProfile}")]
                public List<string> Commands { get; set; }
            }

			// Obstacle Damage
            [JsonProperty(PropertyName = "Obstacle Damage Options")]
            public ObstacleDamageOptions ObstacleDamage { get; set; }
            
            public class ObstacleDamageOptions
            {
                [JsonProperty(PropertyName = "Barricade Options")]
                public BarricadeDamageOptions Barricade { get; set; }
                [JsonProperty(PropertyName = "Twig Building Options")]
                public TwigDamageOptions Twig { get; set; }
                [JsonProperty(PropertyName = "Wood Building Options")]
                public WoodDamageOptions Wood { get; set; }
                [JsonProperty(PropertyName = "Stone Building Options")]
                public StoneDamageOptions Stone { get; set; }
                [JsonProperty(PropertyName = "Metal Building Options")]
                public MetalDamageOptions Metal { get; set; }
                [JsonProperty(PropertyName = "Armored Building Options")]
                public ArmoredDamageOptions Armored { get; set; }
                [JsonProperty(PropertyName = "Deployable Options")]
                public DeployableDamageOptions Deployable { get; set; }
                [JsonProperty(PropertyName = "Damage tick rate in seconds (Rust default = 5.0)")]
                public float DamageTickRate { get; set; }
                
                public class BarricadeDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Barricades in it's path")]
                    public bool BarricadeDestroy { get; set; }
                    [JsonProperty(PropertyName = "Barricade damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float BarricadeDamage { get; set; }
                }

                public class TwigDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Twig building blocks in it's path")]
                    public bool TwigDestroy { get; set; }
                    [JsonProperty(PropertyName = "Twig damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float TwigDamage { get; set; }
                }

                public class WoodDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Wood building blocks in it's path")]
                    public bool WoodDestroy { get; set; }
                    [JsonProperty(PropertyName = "Wood damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float WoodDamage { get; set; }
                }

                public class StoneDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Stone building blocks in it's path")]
                    public bool StoneDestroy { get; set; }
                    [JsonProperty(PropertyName = "Stone damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float StoneDamage { get; set; }
                }

                public class MetalDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Metal building blocks in it's path")]
                    public bool MetalDestroy { get; set; }
                    [JsonProperty(PropertyName = "Metal damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float MetalDamage { get; set; }
                }

                public class ArmoredDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Armored building blocks in it's path")]
                    public bool ArmoredDestroy { get; set; }
                    [JsonProperty(PropertyName = "Metal damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float ArmoredDamage { get; set; }
                }

                public class DeployableDamageOptions
                {
                    [JsonProperty(PropertyName = "Bradley destroys Deployables in it's path")]
                    public bool DeployableDestroy { get; set; }
                    [JsonProperty(PropertyName = "Deployable damage % per tick (0.1 = 10%, 0.2 = 20% etc)")]
                    public float DeployableDamage { get; set; }
                }
            }
            
            // Bradley Crate Loot
            [JsonProperty(PropertyName = "Loot Options")]
            public LootOptions Loot { get; set; }
            
            public class LootOptions
            {
                [JsonProperty(PropertyName = "Use custom loot table to override crate loot")]
                public bool UseCustomLoot { get; set; }
                [JsonProperty(PropertyName = "Minimum number loot items in crate (1 - 48)")]
                public int MinCrateItems { get; set; }
                [JsonProperty(PropertyName = "Maximum number loot items in crate (1 - 48)")]
                public int MaxCrateItems { get; set; }
                [JsonProperty(PropertyName = "Allow duplication of loot items")]
                public bool AllowDupes { get; set; }
                [JsonProperty(PropertyName = "Maximum number of BPs in each crate")]
                public int MaxBP { get; set; }
                [JsonProperty(PropertyName = "Custom loot table")]
                public List<LootItem> LootTable { get; set; }
            }
            
            // Extra Bradley Crate Loot
            [JsonProperty(PropertyName = "Extra Loot Options")]
            public ExtraLootOptions ExtraLoot { get; set; }
            
            public class ExtraLootOptions
            {
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
            
            // Hackable Crate Loot
            [JsonProperty(PropertyName = "Locked Crate Loot Options")]
            public LockedCrateLootOptions LockedCrateLoot { get; set; }

            public class LockedCrateLootOptions
            {
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

        #endregion Config Structure

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
                    allowEpicLoot = false,
                    chatPrefix = "<color=orange>[Bradley Drops]</color>",
                    usePrefix = true,
                    chatIcon = 0,
                    signalFuseLength = 3.5f,
                    smokeDuration = 210f,
                    deliveryMethod = "CH47",
                    buildPrivRadius = 20f,
                    strictProximity = true,
                    proximityRadius = 20f,
                    allowOnFoundations = false,
                    clearLandingZone = false,
                    clearTimedExplosives = false,
                    noVanillaApc = false,
                    useStacking = true,
                    reportCommand = "bdreport",
                   	debug = true
                },
                plane = new ConfigData.PlaneDelivery
                {
                    skipCargoPlane = false,
                    mapScaleDistance = 2f,
                    planeSpeed = 50.0f,
                    planeHeight = 50.0f,
                    fallDrag = 0.6f
                },
                ch47 = new ConfigData.CH47Delivery
                {
                    mapScaleDistance = 0.1f,
                    ch47SpawnHeight = 150f,
                    minHoverHeight = 40f
                },
                announce = new ConfigData.Announce
                {
                    callChat = true,
                    killChat = true,
                    reportChat = true,
                    killVanilla = false,
                    reportVanilla = false,
                    vanillaName = "Bradley APC",
                    vanillaOwner = "Cobolt (SERVER)",
                    maxReported = 5,
                    announceGlobal = true
                },
                discord = new ConfigData.Discord
                {
                    webhookUrl = defaultWebhookUrl,
                    defaultBotName = "BradleyDrops Bot",
                    customAvatarUrl = "",
                    useCustomAvatar = false,
                    sendApcCall = false,
                    sendApcKill = false,
                    sendApcDespawn = false
                },
                rewards = new ConfigData.Rewards
                {
                    rewardPlugin = "ServerRewards",
                    rewardUnit = "RP",
                    enableRewards = false,
                    shareRewards = false,
                    pluginXP = "SkillTree",
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
                        ["bradleydrops.examplevip1"] = 1.25f,
                        ["bradleydrops.examplevip2"] = 1.50f,
                        ["bradleydrops.examplevip3"] = 1.75f
                    }
                },
                purchasing = new ConfigData.Purchasing
                {
                    buyCommand = "bdbuy",
                    defaultCurrency = "ServerRewards",
                    purchaseUnit = "RP",
                    customCurrency = new List<CurrencyItem>
                    {
                        new CurrencyItem { ShortName = "scrap", SkinId = 0, DisplayName = "Scrap" }
                    },
                },
                bradley = new ConfigData.Bradley
                {
                    despawnCommand = "bddespawn",
                    canTeamDespawn = false,
                    globalLimit = 2,
                    playerLimit = 1,
                    maxHitDistance = 1000f,
                    despawnExplosion = true,
                    DespawnWarning = false,
                    WarningThreshold = 25,
                    useNoEscape = false,
                    playerCooldown = 3600f,
                    tierCooldowns = true,
                    teamCooldown = true,
                    allowTurretDamage = true,
                    apcTargetTurret = true,
                    apcTargetVehicles = true,
                    turretAttackTime = 20f,
                    turretCooldown = 30f,
                    turretPenalty = 0,
                    allowInDeepSea = false,
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
                    buildBlocking = new BuildBlocking()
                    {
                        EnableBuildBlockZone = false,
                        BuildBlockZoneRadius = 30f,
                        BuildBlockZoneFollowsBradley = true,
                        BlockDeployables = true,
                        NoBlockPrefabs = new List<string>
                        {
                            "assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab",
                            "assets/prefabs/deployable/barricades/barricade.concrete.prefab",
                            "assets/prefabs/deployable/barricades/barricade.sandbags.prefab"
                        }
                    },
                    vipCooldowns = new Hash<string, float>
                    {
                        ["bradleydrops.examplevip1"] = 3000f,
                        ["bradleydrops.examplevip2"] = 2400f,
                        ["bradleydrops.examplevip3"] = 1800f
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
                            WaveCooldown = 240f,
                            WaveProfiles = new List<string>()
                        }
                    },
                    apcConfig = new Dictionary<string, APCData>
                    {
                        [easyDrop] = new APCData
                        {
                        	Init = new APCData.InitOptions
                            {
                                APCName = easyDrop,
                                SignalSkinID = easySkinID,
                                GiveItemCommand = "easy",
                                UseBuyCommand = true,
                                CostToBuy = 500,
                                Health = 1000f,
                                PatrolRange = 20f,
                                SearchRange = 60f,
                                StuckDetectionTime = 3f,
                                StuckDetectionThreshold = 1f,
                                ChuteProtected = true,
                                ThrottleResponse = 1f,
                                KillInSafeZone = true,
                                DespawnTime = 1800f
                            },
                        	TargetingDamage = new APCData.TargetingDamageOptions
                            {
                                AttackOwner = true,
                                TargetSleepers = false,
                                MemoryDuration = 20f,
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new APCData.CrateOptions
                            {
                                CratesToSpawn = 3,
                                BradleyCrateDespawn = 900f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                DisableFire = false,
                                FireDuration = 300f,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new APCData.WeaponOptions
                            {
                                CanUseSmokeGrenades = true,
                                SmokeGrenadesCooldown = 60f,
                                SmokeGrenadesAmount = 4,
                                SmokeGrenadesRadius = 10f,
                                SmokeGrenadesRate = 0.2f,
                                SmokedWeapons = new List<string>
                                {
                                    "rocket.launcher",
                                    "rocket.launcher.dragon",
                                    "rocket.launcher.rpg7",
                                    "explosive.timed",
                                    "explosive.satchel",
                                    "grenade.f1",
                                    "grenade.beancan",
                                    "grenade.molotov",
                                    "homingmissile.launcher"
                                },
                                CoaxBurstDelay = 1f,
                                CoaxFireRate = 0.05f,
                                CoaxBurstLength = 15,
                                CoaxAimCone = 3f,
                                CoaxTargetRange = 40f,
                                MainCannonBurstDelay = 5f,
                                MainCannonFireRate = 0.25f,
                                MainCannonBurst = 4,
                                MainGunRange = 50f,
                                MainGunAimCone = 2f,
                                MainGunProjectileVelocity = 100f,
                                TopTurretFireRate = 0.25f,
                                TopTurretDroneFireRate = 0.025f,
                                TopTurretAimCone = 3.0f,
                                GunDamage = 1f,
                                ShellDamageScale = 1f,
                                GunBuildDamage = 1f,
                                ShellBuildDamageScale = 1f
                            },
                            BradleyScientists = new APCData.BradleyScientistOptions
                            {
                                DeployScientists = true,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                DeployThreshold1 = 0.8f,
                                DeployThreshold2 = 0.6f,
                                DeployThreshold3 = 0.4f,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                HeavyHealth = 300f,
                                HeavyDamageScale = 0.5f,
                                HeavyAimCone = 2.0f,
                                HeavyKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                Destroy = true,
                                DespawnTime = 300f
                            },
                            CH47 = new APCData.CH47Options
                            {
                                CanDamage = true,
                                StartHealth = 3000f,
                                IsHomingTarget = true,
                                CanUseFlares = true,
                                FlareDuration = 10f,
                                FlareCooldown = 20f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f,
                                SpawnScientists = true,
                                EngageRange = 100f,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                OrbitDropZone = true,
                                OrbitDropZoneDuration = 120f
                            },
							Gibs = new APCData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new APCData.FireballOptions
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
                            Rewards = new APCData.RewardOptions
                            {
                                RewardPoints = 1000,
                                ScrapReward = 1000,
                                DamageThreshold = 100f
                            },
                            BotReSpawn = new APCData.BotReSpawnOptions
                            {
                            	BotReSpawnProfile = string.Empty
                            },
                            ZoneManager = new APCData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseDropZones = false,
                                CanLeaveZone = false,
                                DropZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new APCData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {apcProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            ObstacleDamage = new APCData.ObstacleDamageOptions
                            {
                                Barricade = new APCData.ObstacleDamageOptions.BarricadeDamageOptions
                                {
                                    BarricadeDestroy = true,
                                    BarricadeDamage = 1f
                                },
                                Twig = new APCData.ObstacleDamageOptions.TwigDamageOptions
                                {
                                    TwigDestroy = false,
                                    TwigDamage = 1f
                                },
                                Wood = new APCData.ObstacleDamageOptions.WoodDamageOptions
                                {
                                    WoodDestroy = false,
                                    WoodDamage = 1f
                                },
                                Stone = new APCData.ObstacleDamageOptions.StoneDamageOptions
                                {
                                    StoneDestroy = false,
                                    StoneDamage = 0.5f
                                },
                                Metal = new APCData.ObstacleDamageOptions.MetalDamageOptions
                                {
                                    MetalDestroy = false,
                                    MetalDamage = 0.25f
                                },
                                Armored = new APCData.ObstacleDamageOptions.ArmoredDamageOptions
                                {
                                    ArmoredDestroy = false,
                                    ArmoredDamage = 0.1f
                                },
                                Deployable = new APCData.ObstacleDamageOptions.DeployableDamageOptions
                                {
                                    DeployableDestroy = false,
                                    DeployableDamage = 1f
                                },
                                DamageTickRate = 5.0f
                            },
                            Loot = new APCData.LootOptions
                            {
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new APCData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new APCData.LockedCrateLootOptions
                            {
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new APCData.LockedCrateExtraLootOptions
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
                        [medDrop] = new APCData
                        {
                        	Init = new APCData.InitOptions
                            {
                                APCName = medDrop,
                                SignalSkinID = medSkinID,
                                GiveItemCommand = "medium",
                                UseBuyCommand = true,
                                CostToBuy = 1000,
                                Health = 2000f,
                                PatrolRange = 20f,
                                SearchRange = 70f,
                                StuckDetectionTime = 3f,
                                StuckDetectionThreshold = 1f,
                                ChuteProtected = true,
                                ThrottleResponse = 1f,
                                KillInSafeZone = true,
                                DespawnTime = 1800f
                            },
                        	TargetingDamage = new APCData.TargetingDamageOptions
                            {
                                AttackOwner = true,
                                TargetSleepers = false,
                                MemoryDuration = 25f,
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new APCData.CrateOptions
                            {
                                CratesToSpawn = 6,
                                BradleyCrateDespawn = 900f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                DisableFire = false,
                                FireDuration = 300f,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new APCData.WeaponOptions
                            {
                                CanUseSmokeGrenades = true,
                                SmokeGrenadesCooldown = 60f,
                                SmokeGrenadesAmount = 4,
                                SmokeGrenadesRadius = 10f,
                                SmokeGrenadesRate = 0.2f,
                                SmokedWeapons = new List<string>
                                {
                                    "rocket.launcher",
                                    "rocket.launcher.dragon",
                                    "rocket.launcher.rpg7",
                                    "explosive.timed",
                                    "explosive.satchel",
                                    "grenade.f1",
                                    "grenade.beancan",
                                    "grenade.molotov",
                                    "homingmissile.launcher"
                                },
                                CoaxBurstDelay = 1f,
                                CoaxFireRate = 0.05f,
                                CoaxBurstLength = 15,
                                CoaxAimCone = 3f,
                                CoaxTargetRange = 40f,
                                MainCannonBurstDelay = 5f,
                                MainCannonFireRate = 0.25f,
                                MainCannonBurst = 4,
                                MainGunRange = 50f,
                                MainGunAimCone = 2f,
                                MainGunProjectileVelocity = 100f,
                                TopTurretFireRate = 0.25f,
                                TopTurretDroneFireRate = 0.025f,
                                TopTurretAimCone = 3.0f,
                                GunDamage = 1f,
                                ShellDamageScale = 1f,
                                GunBuildDamage = 1f,
                                ShellBuildDamageScale = 1f
                            },
                            BradleyScientists = new APCData.BradleyScientistOptions
                            {
                                DeployScientists = true,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                DeployThreshold1 = 0.8f,
                                DeployThreshold2 = 0.6f,
                                DeployThreshold3 = 0.4f,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                HeavyHealth = 300f,
                                HeavyDamageScale = 0.5f,
                                HeavyAimCone = 2.0f,
                                HeavyKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                Destroy = true,
                                DespawnTime = 300f
                            },
                            CH47 = new APCData.CH47Options
                            {
                                CanDamage = true,
                                StartHealth = 6000f,
                                IsHomingTarget = true,
                                CanUseFlares = true,
                                FlareDuration = 10f,
                                FlareCooldown = 20f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f,
                                SpawnScientists = true,
                                EngageRange = 100f,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                OrbitDropZone = true,
                                OrbitDropZoneDuration = 120f
                            },
							Gibs = new APCData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new APCData.FireballOptions
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
                            Rewards = new APCData.RewardOptions
                            {
                                RewardPoints = 2000,
                                ScrapReward = 2000,
                                DamageThreshold = 200f
                            },
                            BotReSpawn = new APCData.BotReSpawnOptions
                            {
                            	BotReSpawnProfile = string.Empty
                            },
                            ZoneManager = new APCData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseDropZones = false,
                                CanLeaveZone = false,
                                DropZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new APCData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {apcProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            ObstacleDamage = new APCData.ObstacleDamageOptions
                            {
                                Barricade = new APCData.ObstacleDamageOptions.BarricadeDamageOptions
                                {
                                    BarricadeDestroy = true,
                                    BarricadeDamage = 1f
                                },
                                Twig = new APCData.ObstacleDamageOptions.TwigDamageOptions
                                {
                                    TwigDestroy = false,
                                    TwigDamage = 1f
                                },
                                Wood = new APCData.ObstacleDamageOptions.WoodDamageOptions
                                {
                                    WoodDestroy = false,
                                    WoodDamage = 1f
                                },
                                Stone = new APCData.ObstacleDamageOptions.StoneDamageOptions
                                {
                                    StoneDestroy = false,
                                    StoneDamage = 0.5f
                                },
                                Metal = new APCData.ObstacleDamageOptions.MetalDamageOptions
                                {
                                    MetalDestroy = false,
                                    MetalDamage = 0.25f
                                },
                                Armored = new APCData.ObstacleDamageOptions.ArmoredDamageOptions
                                {
                                    ArmoredDestroy = false,
                                    ArmoredDamage = 0.1f
                                },
                                Deployable = new APCData.ObstacleDamageOptions.DeployableDamageOptions
                                {
                                    DeployableDestroy = false,
                                    DeployableDamage = 1f
                                },
                                DamageTickRate = 5.0f
                            },
                            Loot = new APCData.LootOptions
                            {
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new APCData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new APCData.LockedCrateLootOptions
                            {
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new APCData.LockedCrateExtraLootOptions
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
                        [hardDrop] = new APCData
                        {
                        	Init = new APCData.InitOptions
                            {
                                APCName = hardDrop,
                                SignalSkinID = hardSkinID,
                                GiveItemCommand = "hard",
                                UseBuyCommand = true,
                                CostToBuy = 1500,
                                Health = 3000f,
                                PatrolRange = 20f,
                                SearchRange = 80f,
                                StuckDetectionTime = 3f,
                                StuckDetectionThreshold = 1f,
                                ChuteProtected = true,
                                ThrottleResponse = 1f,
                                KillInSafeZone = true,
                                DespawnTime = 1800f
                            },
                        	TargetingDamage = new APCData.TargetingDamageOptions
                            {
                                AttackOwner = true,
                                TargetSleepers = false,
                                MemoryDuration = 30f,
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new APCData.CrateOptions
                            {
                                CratesToSpawn = 9,
                                BradleyCrateDespawn = 900f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                DisableFire = false,
                                FireDuration = 300f,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new APCData.WeaponOptions
                            {
                                CanUseSmokeGrenades = true,
                                SmokeGrenadesCooldown = 60f,
                                SmokeGrenadesAmount = 4,
                                SmokeGrenadesRadius = 10f,
                                SmokeGrenadesRate = 0.2f,
                                SmokedWeapons = new List<string>
                                {
                                    "rocket.launcher",
                                    "rocket.launcher.dragon",
                                    "rocket.launcher.rpg7",
                                    "explosive.timed",
                                    "explosive.satchel",
                                    "grenade.f1",
                                    "grenade.beancan",
                                    "grenade.molotov",
                                    "homingmissile.launcher"
                                },
                                CoaxBurstDelay = 1f,
                                CoaxFireRate = 0.05f,
                                CoaxBurstLength = 15,
                                CoaxAimCone = 3f,
                                CoaxTargetRange = 40f,
                                MainCannonBurstDelay = 5f,
                                MainCannonFireRate = 0.25f,
                                MainCannonBurst = 4,
                                MainGunRange = 50f,
                                MainGunAimCone = 2f,
                                MainGunProjectileVelocity = 100f,
                                TopTurretFireRate = 0.25f,
                                TopTurretDroneFireRate = 0.025f,
                                TopTurretAimCone = 3.0f,
                                GunDamage = 1f,
                                ShellDamageScale = 1f,
                                GunBuildDamage = 1f,
                                ShellBuildDamageScale = 1f
                            },
                            BradleyScientists = new APCData.BradleyScientistOptions
                            {
                                DeployScientists = true,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                DeployThreshold1 = 0.8f,
                                DeployThreshold2 = 0.6f,
                                DeployThreshold3 = 0.4f,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                HeavyHealth = 300f,
                                HeavyDamageScale = 0.5f,
                                HeavyAimCone = 2.0f,
                                HeavyKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                Destroy = true,
                                DespawnTime = 300f
                            },
                            CH47 = new APCData.CH47Options
                            {
                                CanDamage = true,
                                StartHealth = 9000f,
                                IsHomingTarget = true,
                                CanUseFlares = true,
                                FlareDuration = 10f,
                                FlareCooldown = 20f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f,
                                SpawnScientists = true,
                                EngageRange = 100f,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                OrbitDropZone = true,
                                OrbitDropZoneDuration = 120f
                            },
							Gibs = new APCData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new APCData.FireballOptions
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
                            Rewards = new APCData.RewardOptions
                            {
                                RewardPoints = 3000,
                                ScrapReward = 3000,
                                DamageThreshold = 300f
                            },
                            BotReSpawn = new APCData.BotReSpawnOptions
                            {
                            	BotReSpawnProfile = string.Empty
                            },
                            ZoneManager = new APCData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseDropZones = false,
                                CanLeaveZone = false,
                                DropZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new APCData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {apcProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            ObstacleDamage = new APCData.ObstacleDamageOptions
                            {
                                Barricade = new APCData.ObstacleDamageOptions.BarricadeDamageOptions
                                {
                                    BarricadeDestroy = true,
                                    BarricadeDamage = 1f
                                },
                                Twig = new APCData.ObstacleDamageOptions.TwigDamageOptions
                                {
                                    TwigDestroy = false,
                                    TwigDamage = 1f
                                },
                                Wood = new APCData.ObstacleDamageOptions.WoodDamageOptions
                                {
                                    WoodDestroy = false,
                                    WoodDamage = 1f
                                },
                                Stone = new APCData.ObstacleDamageOptions.StoneDamageOptions
                                {
                                    StoneDestroy = false,
                                    StoneDamage = 0.5f
                                },
                                Metal = new APCData.ObstacleDamageOptions.MetalDamageOptions
                                {
                                    MetalDestroy = false,
                                    MetalDamage = 0.25f
                                },
                                Armored = new APCData.ObstacleDamageOptions.ArmoredDamageOptions
                                {
                                    ArmoredDestroy = false,
                                    ArmoredDamage = 0.1f
                                },
                                Deployable = new APCData.ObstacleDamageOptions.DeployableDamageOptions
                                {
                                    DeployableDestroy = false,
                                    DeployableDamage = 1f
                                },
                                DamageTickRate = 5.0f
                            },
                            Loot = new APCData.LootOptions
                            {
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new APCData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new APCData.LockedCrateLootOptions
                            {
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new APCData.LockedCrateExtraLootOptions
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
                        [expertDrop] = new APCData
                        {
                        	Init = new APCData.InitOptions
                            {
                                APCName = expertDrop,
                                SignalSkinID = expertSkinID,
                                GiveItemCommand = "expert",
                                UseBuyCommand = true,
                                CostToBuy = 2000,
                                Health = 4000f,
                                PatrolRange = 20f,
                                SearchRange = 85f,
                                StuckDetectionTime = 3f,
                                StuckDetectionThreshold = 1f,
                                ChuteProtected = true,
                                ThrottleResponse = 1f,
                                KillInSafeZone = true,
                                DespawnTime = 1800f
                            },
                        	TargetingDamage = new APCData.TargetingDamageOptions
                            {
                                AttackOwner = true,
                                TargetSleepers = false,
                                MemoryDuration = 35f,
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new APCData.CrateOptions
                            {
                                CratesToSpawn = 12,
                                BradleyCrateDespawn = 900f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                DisableFire = false,
                                FireDuration = 300f,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new APCData.WeaponOptions
                            {
                                CanUseSmokeGrenades = true,
                                SmokeGrenadesCooldown = 60f,
                                SmokeGrenadesAmount = 4,
                                SmokeGrenadesRadius = 10f,
                                SmokeGrenadesRate = 0.2f,
                                SmokedWeapons = new List<string>
                                {
                                    "rocket.launcher",
                                    "rocket.launcher.dragon",
                                    "rocket.launcher.rpg7",
                                    "explosive.timed",
                                    "explosive.satchel",
                                    "grenade.f1",
                                    "grenade.beancan",
                                    "grenade.molotov",
                                    "homingmissile.launcher"
                                },
                                CoaxBurstDelay = 1f,
                                CoaxFireRate = 0.05f,
                                CoaxBurstLength = 15,
                                CoaxAimCone = 3f,
                                CoaxTargetRange = 40f,
                                MainCannonBurstDelay = 5f,
                                MainCannonFireRate = 0.25f,
                                MainCannonBurst = 4,
                                MainGunRange = 50f,
                                MainGunAimCone = 2f,
                                MainGunProjectileVelocity = 100f,
                                TopTurretFireRate = 0.25f,
                                TopTurretDroneFireRate = 0.025f,
                                TopTurretAimCone = 3.0f,
                                GunDamage = 1f,
                                ShellDamageScale = 1f,
                                GunBuildDamage = 1f,
                                ShellBuildDamageScale = 1f
                            },
                            BradleyScientists = new APCData.BradleyScientistOptions
                            {
                                DeployScientists = true,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                DeployThreshold1 = 0.8f,
                                DeployThreshold2 = 0.6f,
                                DeployThreshold3 = 0.4f,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                HeavyHealth = 300f,
                                HeavyDamageScale = 0.5f,
                                HeavyAimCone = 2.0f,
                                HeavyKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                Destroy = true,
                                DespawnTime = 300f
                            },
                            CH47 = new APCData.CH47Options
                            {
                                CanDamage = true,
                                StartHealth = 12000f,
                                IsHomingTarget = true,
                                CanUseFlares = true,
                                FlareDuration = 10f,
                                FlareCooldown = 20f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f,
                                SpawnScientists = true,
                                EngageRange = 100f,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                OrbitDropZone = true,
                                OrbitDropZoneDuration = 120f
                            },
							Gibs = new APCData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new APCData.FireballOptions
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
                            Rewards = new APCData.RewardOptions
                            {
                                RewardPoints = 4000,
                                ScrapReward = 4000,
                                DamageThreshold = 400f
                            },
                            BotReSpawn = new APCData.BotReSpawnOptions
                            {
                            	BotReSpawnProfile = string.Empty
                            },
                            ZoneManager = new APCData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseDropZones = false,
                                CanLeaveZone = false,
                                DropZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new APCData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {apcProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            ObstacleDamage = new APCData.ObstacleDamageOptions
                            {
                                Barricade = new APCData.ObstacleDamageOptions.BarricadeDamageOptions
                                {
                                    BarricadeDestroy = true,
                                    BarricadeDamage = 1f
                                },
                                Twig = new APCData.ObstacleDamageOptions.TwigDamageOptions
                                {
                                    TwigDestroy = false,
                                    TwigDamage = 1f
                                },
                                Wood = new APCData.ObstacleDamageOptions.WoodDamageOptions
                                {
                                    WoodDestroy = false,
                                    WoodDamage = 1f
                                },
                                Stone = new APCData.ObstacleDamageOptions.StoneDamageOptions
                                {
                                    StoneDestroy = false,
                                    StoneDamage = 0.5f
                                },
                                Metal = new APCData.ObstacleDamageOptions.MetalDamageOptions
                                {
                                    MetalDestroy = false,
                                    MetalDamage = 0.25f
                                },
                                Armored = new APCData.ObstacleDamageOptions.ArmoredDamageOptions
                                {
                                    ArmoredDestroy = false,
                                    ArmoredDamage = 0.1f
                                },
                                Deployable = new APCData.ObstacleDamageOptions.DeployableDamageOptions
                                {
                                    DeployableDestroy = false,
                                    DeployableDamage = 1f
                                },
                                DamageTickRate = 5.0f
                            },
                            Loot = new APCData.LootOptions
                            {
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new APCData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new APCData.LockedCrateLootOptions
                            {
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new APCData.LockedCrateExtraLootOptions
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
                        [nightmareDrop] = new APCData
                        {
                        	Init = new APCData.InitOptions
                            {
                                APCName = nightmareDrop,
                                SignalSkinID = nightmareSkinID,
                                GiveItemCommand = "nightmare",
                                UseBuyCommand = true,
                                CostToBuy = 2500,
                                Health = 5000f,
                                PatrolRange = 20f,
                                SearchRange = 100f,
                                StuckDetectionTime = 3f,
                                StuckDetectionThreshold = 1f,
                                ChuteProtected = true,
                                ThrottleResponse = 1.5f,
                                KillInSafeZone = true,
                                DespawnTime = 1800f
                            },
                        	TargetingDamage = new APCData.TargetingDamageOptions
                            {
                                AttackOwner = true,
                                TargetSleepers = false,
                                MemoryDuration = 40f,
                                OwnerDamage = false,
                                TargetOtherPlayers = true,
                                BlockOwnerDamage = false,
                                BlockOtherDamage = false,
                                BlockPlayerDamage = false,
                                BlockProtectedList = false
                            },
                            Crates = new APCData.CrateOptions
                            {
                                CratesToSpawn = 15,
                                BradleyCrateDespawn = 900f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                DisableFire = false,
                                FireDuration = 300f,
                                ProtectCrates = false,
                                UnlockCrates = 300f
                            },
                            Weapons = new APCData.WeaponOptions
                            {
                                CanUseSmokeGrenades = true,
                                SmokeGrenadesCooldown = 60f,
                                SmokeGrenadesAmount = 4,
                                SmokeGrenadesRadius = 10f,
                                SmokeGrenadesRate = 0.2f,
                                SmokedWeapons = new List<string>
                                {
                                    "rocket.launcher",
                                    "rocket.launcher.dragon",
                                    "rocket.launcher.rpg7",
                                    "explosive.timed",
                                    "explosive.satchel",
                                    "grenade.f1",
                                    "grenade.beancan",
                                    "grenade.molotov",
                                    "homingmissile.launcher"
                                },
                                CoaxBurstDelay = 1f,
                                CoaxFireRate = 0.05f,
                                CoaxBurstLength = 15,
                                CoaxAimCone = 3f,
                                CoaxTargetRange = 40f,
                                MainCannonBurstDelay = 5f,
                                MainCannonFireRate = 0.25f,
                                MainCannonBurst = 4,
                                MainGunRange = 50f,
                                MainGunAimCone = 2f,
                                MainGunProjectileVelocity = 100f,
                                TopTurretFireRate = 0.25f,
                                TopTurretDroneFireRate = 0.025f,
                                TopTurretAimCone = 3.0f,
                                GunDamage = 1f,
                                ShellDamageScale = 1f,
                                GunBuildDamage = 1f,
                                ShellBuildDamageScale = 1f
                            },
                            BradleyScientists = new APCData.BradleyScientistOptions
                            {
                                DeployScientists = true,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                DeployThreshold1 = 0.8f,
                                DeployThreshold2 = 0.6f,
                                DeployThreshold3 = 0.4f,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                HeavyHealth = 300f,
                                HeavyDamageScale = 0.5f,
                                HeavyAimCone = 2.0f,
                                HeavyKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                Destroy = true,
                                DespawnTime = 300f
                            },
                            CH47 = new APCData.CH47Options
                            {
                                CanDamage = true,
                                StartHealth = 15000f,
                                IsHomingTarget = true,
                                CanUseFlares = true,
                                FlareDuration = 10f,
                                FlareCooldown = 20f,
                                LockedCratesToSpawn = 0,
                                HackSeconds = 900f,
                                LockedCrateDespawn = 7200f,
                                DisableMapMarker = false,
                                ProtectCrates = false,
                                UnlockCrates = 300f,
                                SpawnScientists = true,
                                EngageRange = 100f,
                                OwnDeployedNPC = false,
                                CanTurretTargetNpc = true,
                                UseCustomKits = false,
                                ScientistHealth = 150f,
                                ScientistDamageScale = 0.75f,
                                ScientistAimCone = 2.0f,
                                ScientistKits = new List<string>
                                {
                                    "Kit1",
                                    "Kit2",
                                    "Kit3"
                                },
                                OrbitDropZone = true,
                                OrbitDropZoneDuration = 120f
                            },
							Gibs = new APCData.GibsOptions
                            {
                                KillGibs = false,
                                GibsHotTime = 600f,
                                GibsHealth = 500f,
                                ProtectGibs = false,
                                UnlockGibs = 300f
                            },
                            Fireball = new APCData.FireballOptions
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
                            Rewards = new APCData.RewardOptions
                            {
                                RewardPoints = 5000,
                                ScrapReward = 5000,
                                DamageThreshold = 500f
                            },
                            BotReSpawn = new APCData.BotReSpawnOptions
                            {
                            	BotReSpawnProfile = string.Empty
                            },
                            ZoneManager = new APCData.ZoneManagerOptions
                            {
                                UseZoneManager = false,
                                UseDropZones = false,
                                CanLeaveZone = false,
                                DropZoneIDs = new List<string>(),
                                UseProtectedZones = false,
                                ProtectedZoneIDs = new List<string>()
                            },
                            CommandsToRun = new APCData.CommandsToRunOptions
                            {
                                RunCommands = false,
                                Commands = new List<string>
                                {
                                    "say {ownerId} destroyed their {apcProfile}",
                                    "o.grant user {teamIds} someplugin.permission"
                                }
                            },
                            ObstacleDamage = new APCData.ObstacleDamageOptions
                            {
                                Barricade = new APCData.ObstacleDamageOptions.BarricadeDamageOptions
                                {
                                    BarricadeDestroy = true,
                                    BarricadeDamage = 1f
                                },
                                Twig = new APCData.ObstacleDamageOptions.TwigDamageOptions
                                {
                                    TwigDestroy = false,
                                    TwigDamage = 1f
                                },
                                Wood = new APCData.ObstacleDamageOptions.WoodDamageOptions
                                {
                                    WoodDestroy = false,
                                    WoodDamage = 1f
                                },
                                Stone = new APCData.ObstacleDamageOptions.StoneDamageOptions
                                {
                                    StoneDestroy = false,
                                    StoneDamage = 0.5f
                                },
                                Metal = new APCData.ObstacleDamageOptions.MetalDamageOptions
                                {
                                    MetalDestroy = false,
                                    MetalDamage = 0.25f
                                },
                                Armored = new APCData.ObstacleDamageOptions.ArmoredDamageOptions
                                {
                                    ArmoredDestroy = false,
                                    ArmoredDamage = 0.1f
                                },
                                Deployable = new APCData.ObstacleDamageOptions.DeployableDamageOptions
                                {
                                    DeployableDestroy = false,
                                    DeployableDamage = 1f
                                },
                                DamageTickRate = 5.0f
                            },
                            Loot = new APCData.LootOptions
                            {
                                UseCustomLoot = false,
                                MinCrateItems = 2,
                                MaxCrateItems = 6,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            ExtraLoot = new APCData.ExtraLootOptions
                            {
                                UseExtraLoot = false,
                                MinExtraItems = 1,
                                MaxExtraItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateLoot = new APCData.LockedCrateLootOptions
                            {
                                UseLockedCrateLoot = false,
                                MinLockedCrateItems = 1,
                                MaxLockedCrateItems = 3,
                                AllowDupes = false,
                                MaxBP = 2,
                                LootTable = new List<LootItem>
                                {
                                    new LootItem { ShortName = "example.shortname1", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f },
                                    new LootItem { ShortName = "example.shortname2", Chance = 50, AmountMin = 1, AmountMax = 2, SkinId = 1234567890, DisplayName = string.Empty, Text = string.Empty, BlueprintChance = 0f }
                                }
                            },
                            LockedCrateExtraLoot = new APCData.LockedCrateExtraLootOptions
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

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();

                if (config == null)
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
                    Log($"INFO: {ex}", LogLevel.ERROR);
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
            Log("Config update detected! Updating config...", LogLevel.WARN);

            var defaultConfig = GetDefaultConfig();

            // Per profile updates
            foreach (var kvp in config.bradley.apcConfig)
            {
                const string FALLBACK_PROFILE_KEY = easyDrop;
                string profileName = kvp.Key;
                var apcData = kvp.Value;

                // Try to get matching default profile OR fall back to base profile if not
                APCData defaultApc = null;
                if (!defaultConfig.bradley.apcConfig.TryGetValue(profileName, out defaultApc))
                {
                    if (!defaultConfig.bradley.apcConfig.TryGetValue(FALLBACK_PROFILE_KEY, out defaultApc))
                    {
                        Log($"Could not find fallback APC profile '{FALLBACK_PROFILE_KEY}'. Custom profile '{profileName}' may not patch correctly.", LogLevel.WARN);
                        continue;
                    }
                }

                if (config.Version < new VersionNumber(1, 3, 2))
                {
                    apcData.TargetingDamage ??= new APCData.TargetingDamageOptions();
                    apcData.Weapons ??= new APCData.WeaponOptions();
                    apcData.ZoneManager ??= new APCData.ZoneManagerOptions();

                    // TargetingDamage
                    apcData.TargetingDamage.TargetDrones = defaultApc.TargetingDamage.TargetDrones;
                    apcData.TargetingDamage.DroneTargetRange = defaultApc.TargetingDamage.DroneTargetRange;
                    // Weapons
                    apcData.Weapons.TopTurretDroneFireRate = defaultApc.Weapons.TopTurretDroneFireRate;
                    apcData.Weapons.SmokeGrenadesAmount = defaultApc.Weapons.SmokeGrenadesAmount;
                    apcData.Weapons.SmokeGrenadesRadius = defaultApc.Weapons.SmokeGrenadesRadius;
                    apcData.Weapons.SmokeGrenadesRate = defaultApc.Weapons.SmokeGrenadesRate;
                    // ZoneManager
                    apcData.ZoneManager = defaultApc.ZoneManager;
                }

                if (config.Version < new VersionNumber(1, 3, 3))
                {
                    apcData.Weapons.SmokedWeapons = new List<string>
                    {
                        "rocket.launcher",
                        "rocket.launcher.dragon",
                        "rocket.launcher.rpg7",
                        "explosive.timed",
                        "explosive.satchel",
                        "grenade.f1",
                        "grenade.beancan",
                        "grenade.molotov",
                        "homingmissile.launcher"
                    };
                }

                if (config.Version < new VersionNumber(1, 3, 4))
                {
                    apcData.BradleyScientists.Destroy = true;
                    apcData.BradleyScientists.DespawnTime = 300f;
                }

                if (config.Version < new VersionNumber(1, 3, 12))
                {
                    foreach (var item in apcData.Loot.LootTable)
                    {
                        item.Text = string.Empty;;
                    }

                    foreach (var item in apcData.ExtraLoot.LootTable)
                    {
                        item.Text = string.Empty;;
                    }

                    foreach (var item in apcData.LockedCrateLoot.LootTable)
                    {
                        item.Text = string.Empty;;
                    }
                }

                if (config.Version < new VersionNumber(1, 3, 13))
                {
                    apcData.Crates.DisableMapMarker = false;
                    apcData.CH47.DisableMapMarker = false;
                }

                if (config.Version < new VersionNumber(1, 4, 0))
                {
                    apcData.LockedCrateExtraLoot = new APCData.LockedCrateExtraLootOptions
                    {
                        UseExtraLoot = defaultApc.LockedCrateExtraLoot.UseExtraLoot,
                        MinExtraItems = defaultApc.LockedCrateExtraLoot.MinExtraItems,
                        MaxExtraItems = defaultApc.LockedCrateExtraLoot.MaxExtraItems,
                        AllowDupes = defaultApc.LockedCrateExtraLoot.AllowDupes,
                        MaxBP = defaultApc.LockedCrateExtraLoot.MaxBP,
                        LootTable = defaultApc.LockedCrateExtraLoot.LootTable
                    };

                    apcData.CommandsToRun = new APCData.CommandsToRunOptions
                    {
                        RunCommands = defaultApc.CommandsToRun.RunCommands,
                        Commands = defaultApc.CommandsToRun.Commands != null
                            ? new List<string>(defaultApc.CommandsToRun.Commands)
                            : new List<string>()
                    };
                }
            }

            // All other config updates
            if (config.Version < new VersionNumber(1, 3, 6))
            {
                config.bradley.allowInDeepSea = defaultConfig.bradley.allowInDeepSea;
            }

            if (config.Version < new VersionNumber(1, 3, 12))
            {
                config.rewards.customRewardItem.Text = defaultConfig.rewards.customRewardItem.Text;
                config.bradley.buildBlocking = defaultConfig.bradley.buildBlocking;
                config.options.allowOnFoundations = defaultConfig.options.allowOnFoundations;
            }

            config.Version = Version;
            SaveConfig();
            Log("Config updated successfully.", LogLevel.WARN);
        }

        #endregion Config

        #region Harmony Patches

        // Harmony patch class for BradleyAPC.DoWeaponAiming method
        public static class BradleyDoWeaponAimingPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(BradleyAPC __instance)
            {
                if (__instance == null)
                    return true;
                
                var bradComp = BradleyDrops.GetCachedBradleyDrop(__instance);
                if (bradComp != null)
                {
                    bradComp.CustomDoWeaponAiming();
                    return false;
                }
                
                return true;
            }
        }

        // Harmony patch class for BradleyAPC.DoWeapons method
        public static class BradleyDoWeaponsPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(BradleyAPC __instance)
            {
                if (__instance == null)
                    return true;
                
                var bradComp = BradleyDrops.GetCachedBradleyDrop(__instance);
                if (bradComp != null)
                {
                    bradComp.CustomDoWeapons();
                    return false;
                }
                
                return true;
            }
        }

        // Harmony patch class for CH47HelicopterAIController.CalculateDesiredAltitude method
        public static class CH47CalculateDesiredAltitudePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(CH47HelicopterAIController __instance)
            {
                if (__instance == null)
                    return true;
                
                var ch47Comp = BradleyDrops.GetCachedCH47DropComponent(__instance);
                if (ch47Comp != null && ch47Comp.atDropPosition && !ch47Comp.ch47Retiring)
                {
                    ch47Comp.CustomCalculateDesiredAltitude();
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

                // Not a Bradley Drop crate, allow native RefreshDecay
                if (!LockedCrates.TryGetValue(crateId, out LockedCrateData crateData) || crateData == null)
                    return true;

                // If it no longer has custoim skinId, stop managing it and allow native logic
                if (!BradleyProfileCache.ContainsKey(__instance.skinID))
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
        
        private static class BradleyKillSpawnedScientistsPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(BradleyAPC __instance)
            {
                if (__instance == null || __instance.IsDestroyed || Instance == null)
                    return true;

                if (!BradleyProfileCache.TryGetValue(__instance.skinID, out string apcProfile) || apcProfile == null)
                    return true;

                if (config.bradley.apcConfig.TryGetValue(apcProfile, out APCData apcData))
                {
                    if (apcData == null)
                        return true;

                    var bradScientists = apcData?.BradleyScientists;

                    if (bradScientists == null || !bradScientists.Destroy)
                        return true;

                    if (bradScientists.Destroy && bradScientists.DespawnTime <= 0)
                    {
                        Instance.KillOrDespawnDeployedScientists(__instance, apcData, false);
                    }
                    else if (bradScientists.Destroy && bradScientists.DespawnTime > 0)
                    {
                        Instance.KillOrDespawnDeployedScientists(__instance, apcData, true);
                    }
                    // Skip native KillSpawnedScientists for plugin Bradleys and run custom method.
                    return false;
                }

                return true;
            }
        }

        #endregion Harmony Patches

    }
} 
 