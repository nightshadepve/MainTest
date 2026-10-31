
/* Documentation =>
 * Commands:
 * quarry.give «SteamID» «SkinID» => give to player the quarry with specified skin from config
 * quarry.giveme => give all quarries from config to self
 * Permissions:
 * aQuarry.admin => allows to use commands from in-game console
 * 
 * Copyright © 2022-2026 AvG Лаймон (Email: alias.dev@ya.ru | Discord: avglimon | Alias™ development team: https://discord.gg/MWeNJV5e7F) */

/* 
 * * * * * CHANGELOG * * * * *
 * * * 2.0.6 * * *
 * Fix for update
 * * * 2.0.5 * * *
 * Fixed OnLootEntity error on static quarries
 * * * 2.0.4 * * *
 * Added the option to access the quarry management only for the owner/team
 * Fixed a global bug that made some invulnerable objects vulnerable
 * Added ProtectionProperties to main config
 * Fixed remove dupe
 * Added API GetAllQuarrySkins
 * Added Hook OnCustomQuarryKilled
 * Added Hook OnAQuarryLoaded
 * Added support for aHomeManager
 * Minor lang optimization
 * Some other minor fixes
 * * * 2.0.3 * * *
 * Fixed all known bugs
 * Some UI improvements
 * Added sound effect for quarry upgrades
 * Added new loot protection system by permissions for static quarries (default disabled)
*/

/* 
 * * * * * TODO * * * * *
 * Запретить установку рядом с рельсами
 * Запретить установку рядом со скалами
 * Проверить и легализировать рейты по пермишенам для залоченных статичных карьеров
 * Minor UI redisign
*/

using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using static Oxide.Game.Rust.Cui.CuiHelper;
using static MiningQuarry;
using static ConsoleSystem;
using static RelationshipManager;
using Rust;


namespace Oxide.Plugins
{
    [Info("aQuarry", "AvG Лаймон", "2.0.6")]
    class aQuarry : RustPlugin
    {
        #region [Initialization]
        private static aQuarry instance;
        private const bool Rus = false;
        private const string adminPerm = "aQuarry.admin";
        private void Init()
        {
            UnsubscribeAll();
            instance = this;
        }
        private void OnServerInitialized()
        {
            if (!ValidateConfig())
            {
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }
            LoadConfigData();
            LoadLangFiles();
            RegisterPermissions();
            GenerateUI();
            timer.In(1f, LoadCustomQuarries);
            Interface.CallHook("OnAQuarryLoaded");
        }
        private void Unload()
        {
            DestroyCustomQuarries();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                CloseUI(player, true);
            instance = null;
        }
        #endregion
        #region [Library]

        //private static readonly float[] protection = new float[25] { 0.9f, 0, 0, 0, 0, 0.95f, 0, 0, 0, 0.99f, 0.99f, 0.99f, 0, 1, 1, 0.99f, 0.5f, 0, 0, 0, 0, 1, 1, 1, 0 };
        private static Dictionary<float, ProtectionProperties> protectionProperties = new Dictionary<float, ProtectionProperties>();
        private Dictionary<QuarryType, ulong> staticSkins = new Dictionary<QuarryType, ulong>
        {
            [QuarryType.None] = 3969321,
            [QuarryType.Basic] = 3969322,
            [QuarryType.Sulfur] = 3969323,
            [QuarryType.HQM] = 3969324
        };
        private Dictionary<ulong, QuarryType> staticTypes = new Dictionary<ulong, QuarryType>();
        #endregion
        #region [Data]
        private Dictionary<ulong, LimitsData> limitsData = new Dictionary<ulong, LimitsData>();
        private class LimitsData : BuildLimits
        {
            internal int placedQuarries = 0;
            internal int placedJacks = 0;
            internal void Sum(BuildLimits limit)
            {
                this.limitQuarries += limit.limitQuarries;
                this.limitJacks += limit.limitJacks;
            }
            internal void Reset()
            {
                limitQuarries = 0;
                limitJacks = 0;
            }
        }
        private Dictionary<ulong, QuarryCash> quarryCash = new Dictionary<ulong, QuarryCash>();
        private class QuarryCash
        {
            public bool isOn;
            public float hp;
            public float pendingWork = 0;
            public float workDone = 0;
            public int fuelID = 0;
            public ulong fuelSkin = 0;
            public ulong lockOwner = 0;
        }
        #endregion
        #region [Configuration]
        private Configuration DefaultConfig()
        {
            Configuration config = new Configuration();
            config.Limit.permissions.Add("aQuarry.default", new BuildLimits());
            config.Limit.permissions.Add("aQuarry.vip", new BuildLimits { priority = 1, limitQuarries = 4, limitJacks = 2});
            config.Limit.permissions.Add("aQuarry.admin", new BuildLimits { priority = 999, limitQuarries = 999, limitJacks = 999 });
            config.Rates.permissions.Add("aQuarry.vip", 0.2f);
            config.Rates.permissions.Add("aQuarry.admin", 1f);
            return config;
        }
        protected override void LoadDefaultConfig()
        {
            PrintWarning(Rus ? "Создание базовой конфигурации..." : "Creating a default config...");
            config = DefaultConfig();
            config.PluginVersion = Version;

            Interface.Oxide.DataFileSystem.GetFile("aQuarry/StaticQuarries/test").WriteObject("");
            Interface.Oxide.DataFileSystem.DeleteDataFile("aQuarry/StaticQuarries/test");
            Interface.Oxide.DataFileSystem.GetFile("aQuarry/PersonalQuarries/test").WriteObject("");
            Interface.Oxide.DataFileSystem.DeleteDataFile("aQuarry/PersonalQuarries/test");

            PrintWarning(Rus ? "Создание базовой конфигурации завершено!" : "Default config has been created!");
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();
        }
        private bool ValidateConfig()
        {
            if (config.PluginVersion < Version)
            {
                PrintWarning(Rus ? "Обнаружено обновление плагина! Автоматическое обновление конфигурации..." : "Config update detected! Updating config values...");
                if (config.PluginVersion < new VersionNumber(2, 0, 0))
                {
                    PrintWarning("Old configuration file will not work! Removing...");
                    LoadDefaultConfig();
                }
                else if (config.PluginVersion < new VersionNumber(2, 0, 2))
                {
                    PrintWarning("You must install 2.0.2 version first! Please read update intsruction file from archive..");
                    return false;
                }
                else
                {

                }
                PrintWarning(Rus ? "Обновление конфигурации успешно!" : "Config update completed!");
            }
            if (config.plugins.aHomeManager)
                config.optimization.disableLimits = true;
            config.PluginVersion = Version;
            config.FillProtectionProperties();
            SaveConfig();
            return true;
        }
        private void DefaultStaticData(int index)
        {
            string[] fileNames = new string[4] { "StaticPumpjack", "StaticStoneQuarry", "StaticSulfurQuarry", "StaticHQMQuarry" };
            string[] shortNames = new string[4] { "lowgradefuel", "stones", "sulfur.ore", "hq.metal.ore" };
            StaticQuarry quarry = new StaticQuarry();
            quarry.Production.fuelList.Add(new FuelProduction { shortname = "diesel_barrel", amount = 1, resources = new List<ResourceItem> { new ResourceItem { shortname = shortNames[index] } } });
            Interface.Oxide.DataFileSystem.GetFile($"aQuarry/StaticQuarries/{fileNames[index]}").WriteObject(quarry);
        }
        private void DefaultPersonalData()
        {
            ulong[] quarrySkins = new ulong[6] { 3187637123, 3185891808, 3185893553, 3185893848, 3185894100, 3185894375 };
            QRepair repair = new QRepair { allow = true, blockTime = 30f, repair = 100f, needCost = true, Cost = new List<CustomItem> { new CustomItem { shortname = "metal.fragments", skin = 0, amount = 500 } } };
            string[] upgradeShortnames = new string[10] { "wood", "stones", "metal.fragments", "metal.refined", "leather", "carburetor3", "scrap", "rope", "sheetmetal", "tarp" };
            int[] upgradeAmountsBase = new int[10] { 10000, 5000, 0, 0, 0, 1, 500, 5, 5, 5 };
            int[] upgradeAmountsPerLvl = new int[10] { 2000, 1000, 500, 10, 100, 0, 150, 0, 0, 0 };

            for (int lvl = 0; lvl < quarrySkins.Length; lvl++)
            {
                PersonalQuarry quarry = new PersonalQuarry();
                quarry.skinID = quarrySkins[lvl];
                quarry.enable = true;
                quarry.name = lvl == 0 ? "Personal Quarry" : $"Personal Quarry Lvl {lvl}";
                quarry.Structure.health = 2500f + 500f * lvl;
                quarry.Structure.protection = 10f * lvl;
                quarry.Remove.itemList.Add(new NamedItem { name = quarry.name, shortname = "furnace.large", amount = 1, skin = quarry.skinID });
                if (lvl + 1 < quarrySkins.Length)
                    quarry.Upgrade.newSkin = quarrySkins[lvl + 1];
                for (int item = 0; item < upgradeShortnames.Length; item++)
                {
                    if (lvl == quarrySkins.Length - 1) break;
                    int amount = upgradeAmountsBase[item] + upgradeAmountsPerLvl[item] * (lvl + 1);
                    if (amount > 0)
                        quarry.Upgrade.Cost.Add(new CustomItem { shortname = upgradeShortnames[item], amount = amount });
                }
                quarry.Inventory.lootSize = 6 + 6 * lvl;
                quarry.Inventory.fuelSize = 1 + 1 * lvl;
                quarry.Repair = repair;

                quarry.Production.fuelList.Add(new FuelProduction { shortname = "lowgradefuel", skin = 0, amount = 50, fuelTimer = 60, productionTimer = 60 });
                quarry.Production.fuelList[0].resources.Add(new ResourceItem { shortname = "stones", amount = 100 + 10 * lvl, amountMax = 100 + 10 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[0].resources.Add(new ResourceItem { shortname = "metal.ore", amount = 70 + 7 * lvl, amountMax = 70 + 7 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[0].resources.Add(new ResourceItem { shortname = "sulfur.ore", amount = 50 + 5 * lvl, amountMax = 50 + 5 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[0].resources.Add(new ResourceItem { shortname = "hq.metal.ore", amount = 10 + 1 * lvl, amountMax = 10 + 1 * lvl, skin = 0, chance = 10f });

                quarry.Production.fuelList.Add(new FuelProduction { shortname = "crude.oil", skin = 0, amount = 30, fuelTimer = 120, productionTimer = 60 });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "stones", amount = 100 + 10 * lvl, amountMax = 100 + 10 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "metal.ore", amount = 70 + 7 * lvl, amountMax = 70 + 7 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "sulfur.ore", amount = 50 + 5 * lvl, amountMax = 50 + 5 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "charcoal", amount = 30 + 3 * lvl, amountMax = 30 + 3 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "hq.metal.ore", amount = 10 + 1 * lvl, amountMax = 10 + 1 * lvl, skin = 0, chance = 10f });

                quarry.Production.fuelList.Add(new FuelProduction { shortname = "diesel_barrel", skin = 0, amount = 1, fuelTimer = 600, productionTimer = 120 });
                quarry.Production.fuelList[2].resources.Add(new ResourceItem { shortname = "stones", amount = 400 + 40 * lvl, amountMax = 400 + 40 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[2].resources.Add(new ResourceItem { shortname = "metal.ore", amount = 280 + 28 * lvl, amountMax = 280 + 28 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[2].resources.Add(new ResourceItem { shortname = "sulfur.ore", amount = 200 + 20 * lvl, amountMax = 200 + 20 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[2].resources.Add(new ResourceItem { shortname = "charcoal", amount = 120 + 12 * lvl, amountMax = 120 + 12 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[2].resources.Add(new ResourceItem { shortname = "hq.metal.ore", amount = 40 + 4 * lvl, amountMax = 40 + 4 * lvl, skin = 0, chance = 20f });

                quarry.Production.fuelList.Add(new FuelProduction { shortname = "glue", skin = 3188057178, amount = 5, fuelTimer = 180, productionTimer = 90 });
                quarry.Production.fuelList[3].resources.Add(new ResourceItem { shortname = "gunpowder", amount = 150 + 15 * lvl, amountMax = 150 + 15 * lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[3].resources.Add(new ResourceItem { shortname = "hq.metal.ore", amount = 10 + lvl, amountMax = 10 + lvl, skin = 0, chance = 50f });
                quarry.Production.fuelList[3].resources.Add(new ResourceItem { shortname = "explosives", amount = 1, amountMax = 1, skin = 0, chance = 10f + (float)lvl / 2, disableRate = true });

                Interface.Oxide.DataFileSystem.GetFile($"aQuarry/PersonalQuarries/Quarry{(lvl == 0 ? "00" : $"0{lvl}")}").WriteObject(quarry);
            }

            quarrySkins = new ulong[6] { 3188075788, 3185901475, 3185901854, 3185902403, 3185902634, 3185902821 };
            upgradeShortnames = new string[10] { "wood", "stones", "metal.fragments", "metal.refined", "leather", "carburetor3", "scrap", "rope", "sheetmetal", "tarp" };
            upgradeAmountsBase = new int[10] { 10000, 5000, 1000, 50, 500, 1, 0, 2, 2, 2 };
            upgradeAmountsPerLvl = new int[10] { 2000, 500, 300, 15, 50, 0, 100, 0, 0, 0 };
            for (int lvl = 0; lvl < quarrySkins.Length; lvl++)
            {
                PersonalQuarry quarry = new PersonalQuarry();
                quarry.skinID = quarrySkins[lvl];
                quarry.enable = true;
                quarry.name = lvl == 0 ? "Personal Pumpjack" : $"Personal Pumpjack Lvl {lvl}";
                quarry.Structure.health = 2500f + 500f * lvl;
                quarry.Structure.protection = 10f * lvl;
                quarry.Remove.itemList.Add(new NamedItem { name = quarry.name, shortname = "furnace.large", amount = 1, skin = quarry.skinID });
                if (lvl + 1 < quarrySkins.Length)
                    quarry.Upgrade.newSkin = quarrySkins[lvl + 1];
                for (int item = 0; item < upgradeShortnames.Length; item++)
                {
                    if (lvl == quarrySkins.Length - 1) break;
                    int amount = upgradeAmountsBase[item] + upgradeAmountsPerLvl[item] * (lvl + 1);
                    if (amount > 0)
                        quarry.Upgrade.Cost.Add(new CustomItem { shortname = upgradeShortnames[item], amount = amount });
                }
                quarry.Inventory.lootSize = 6 + 6 * lvl;
                quarry.Inventory.fuelSize = 1 + 1 * lvl;
                quarry.Repair = repair;
                quarry.Prefab.quarry = false;

                quarry.Production.fuelList.Add(new FuelProduction { shortname = "crude.oil", skin = 0, amount = 50, fuelTimer = 60, productionTimer = 60 });
                quarry.Production.fuelList[0].resources.Add(new ResourceItem { shortname = "lowgradefuel", amount = 150 + lvl, amountMax = 150 + lvl, skin = 0, chance = 100f });

                quarry.Production.fuelList.Add(new FuelProduction { shortname = "diesel_barrel", skin = 0, amount = 1, fuelTimer = 600, productionTimer = 300 });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "lowgradefuel", amount = 110 + lvl * 3, amountMax = 110 + lvl * 3, skin = 0, chance = 100f });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "crude.oil", amount = 50 + lvl, amountMax = 50 + lvl, skin = 0, chance = 100f });
                quarry.Production.fuelList[1].resources.Add(new ResourceItem { shortname = "glue", amount = 30, amountMax = 50, skin = 3188057178, chance = 5f + (float)lvl / 2, name = "Top Quality Fuel" });

                Interface.Oxide.DataFileSystem.GetFile($"aQuarry/PersonalQuarries/Pumpjack{(lvl == 0 ? "00" : $"0{lvl}")}").WriteObject(quarry);
            }
        }
        private void LoadConfigData()
        {
            DataFileSystem fileSystem = Interface.Oxide.DataFileSystem;
            string[] fileName = new string[4] { "StaticPumpjack", "StaticStoneQuarry", "StaticSulfurQuarry", "StaticHQMQuarry" };
            for (int i = 0; i < fileName.Length; i++)
                if (!fileSystem.ExistsDatafile($"aQuarry/StaticQuarries/{fileName[i]}"))
                    DefaultStaticData(i);
            string[] files = fileSystem.GetFiles("aQuarry/PersonalQuarries/");
            if (files.Length == 0) 
                DefaultPersonalData();
            for (int i = 0; i < fileName.Length; i++)
            {
                StaticQuarry quarry = fileSystem.GetFile($"aQuarry/StaticQuarries/{fileName[i]}").ReadObject<StaticQuarry>();
                if (quarry.enable)
                {
                    quarry.Production.fuelList.ForEach(x => x.ValidateProduction());
                    statics.Add((QuarryType)i, quarry);
                    if (quarry.Protection.enable)
                        staticTypes.Add(staticSkins[(QuarryType)i], (QuarryType)i);
                }
                fileSystem.GetFile($"aQuarry/StaticQuarries/{fileName[i]}").WriteObject(quarry);
            }
            foreach (string path in files)
            {
                string name = path.Split('/').Last().Replace(".json", "");
                PersonalQuarry quarry = fileSystem.GetFile($"aQuarry/PersonalQuarries/{name}").ReadObject<PersonalQuarry>();
                if (!quarry.enable) continue;
                if (quarry.skinID == 0)
                {
                    PrintWarning($"{name} can not be loaded because it has no skinID. Skipping..");
                    continue;
                }
                if (personal.ContainsKey(quarry.skinID))
                {
                    PrintWarning($"{name} can not be loaded because skinID '{quarry.skinID}' already exists. Skipping..");
                    continue;
                }
                if (quarry.Remove.allow)
                    canBeRemoved.Add(quarry.skinID);
                if (quarry.Upgrade.newSkin > 0)
                    canBeUpgraded.Add(quarry.skinID);
                quarry.Remove.itemList.ForEach(x => x.ValidateItem());
                quarry.Upgrade.Cost.ForEach(x => x.ValidateItem());
                quarry.Repair.Cost.ForEach(x => x.ValidateItem());
                quarry.Production.fuelList.ForEach(x => x.ValidateProduction());
                CreateProtectionProperties(quarry.Structure.protection);
                personal.Add(quarry.skinID, quarry);
                fileSystem.GetFile($"aQuarry/PersonalQuarries/{name}").WriteObject(quarry);
            }
            foreach (PersonalQuarry x in personal.Values)
            {
                if (x.Upgrade.newSkin > 0 && !personal.ContainsKey(x.Upgrade.newSkin))
                {
                    PrintWarning($"{x.name}'s upgrade settings are using skin '{x.Upgrade.newSkin}' which are not exist in the plugin or it's config disabled. Disabling upgrade for this quarry...");
                    x.Upgrade.newSkin = 0;
                }
            }
        }
        protected override void SaveConfig() => Config.WriteObject(config);
        private Dictionary<QuarryType, StaticQuarry> statics = new Dictionary<QuarryType, StaticQuarry>();
        private Dictionary<ulong, PersonalQuarry> personal = new Dictionary<ulong, PersonalQuarry>();
        private Configuration config;
        private Dictionary<ulong, UISettings> ui = new Dictionary<ulong, UISettings>();
        private class UISettings
        {
            internal UI main;
            internal UI upgrade;
            internal UI remove;
            internal void CalculateOffset()
            {
                if (upgrade != null)
                {
                    if (upgrade.height > main.height)
                        main.height = upgrade.height;
                    else if (main.height > upgrade.height)
                        upgrade.height = main.height;
                    upgrade.offset = new Offset($"20 -{upgrade.height / 2}", $"410 {upgrade.height / 2}");
                    main.offset = new Offset($"-410 -{main.height / 2}", $"-20 {main.height / 2}");
                }
                else main.offset = new Offset($"-205 -{main.height / 2}", $"185 {main.height / 2}");
                if (remove != null)
                    remove.offset = new Offset($"-205 -{remove.height / 2}", $"185 {remove.height / 2}");
            }
        }
        private class UI
        {
            internal bool isStatic = false;
            internal int itemID = 0;
            internal QuarryType type = QuarryType.None;
            internal float[] stats = new float[4];
            internal ulong skin = 0;
            internal ulong parent = 0;
            internal string name = "";
            internal string sprite = "";
            internal string langKey = "";
            internal float height = 270f;
            internal Offset offset;
            internal Offset fuel;
            internal Offset production;
            internal Offset upgrade;
            internal List<Offset> fuelList;
            internal List<List<Offset>> productionList = new List<List<Offset>>();
            internal List<Offset> upgradeList;
            internal UI(ulong skin, ulong parent, string sprite, string langKey)
            {
                this.skin = skin;
                this.parent = parent;
                this.sprite = sprite;
                this.langKey = langKey;
            }
        }
        private class Offset
        {
            internal string min;
            internal string max;
            internal Offset(string min, string max)
            {
                this.min = min;
                this.max = max;
            }
        }
        private void GenerateUI()
        {
            foreach (KeyValuePair<ulong, PersonalQuarry> x in personal)
            {
                ui.Add(x.Key, new UISettings());
                UISettings set = ui[x.Key];
                set.main = new UI(x.Key, x.Key, "assets/icons/gear.png", "UI.ControlButton");
                set.main.name = x.Value.name;
                set.main.itemID = x.Value.Prefab.quarry ? 1052926200 : -1130709577;
                set.main.stats[0] = x.Value.Structure.health;
                set.main.stats[1] = x.Value.Structure.protection;
                set.main.stats[2] = x.Value.Inventory.lootSize;
                set.main.stats[3] = x.Value.Inventory.fuelSize;
                GenerateUISettings(x.Value.Production.fuelList, set.main);
                if (x.Value.Upgrade.newSkin > 0)
                {
                    var quarry = personal[x.Value.Upgrade.newSkin];
                    set.upgrade = new UI(quarry.skinID, x.Key, "assets/icons/upgrade.png", "UI.Upgrade");
                    set.upgrade.name = quarry.name;
                    set.upgrade.itemID = quarry.Prefab.quarry ? 1052926200 : -1130709577;
                    set.upgrade.stats[0] = quarry.Structure.health;
                    set.upgrade.stats[1] = quarry.Structure.protection;
                    set.upgrade.stats[2] = quarry.Inventory.lootSize;
                    set.upgrade.stats[3] = quarry.Inventory.fuelSize;
                    GenerateUISettings(quarry.Production.fuelList, set.upgrade, x.Value.Upgrade.Cost);
                }
                if (x.Value.Remove.allow)
                {
                    set.remove = new UI(x.Key, x.Key, "assets/icons/level_metal.png", "UI.Remove");
                    GenerateRemoveUI(set.remove, x.Value.Remove.refund ? x.Value.Remove.itemList.Count : 0);
                }
                set.CalculateOffset();
            }
            foreach (KeyValuePair<QuarryType, StaticQuarry> x in statics)
            {
                ulong skin = staticSkins[x.Key];
                ui.Add(skin, new UISettings());
                UISettings set = ui[skin];
                set.main = new UI(skin, skin, "assets/icons/gear.png", "UI.ControlButton");
                set.main.isStatic = true;
                set.main.type = x.Key;
                set.main.itemID = x.Key == QuarryType.None ? -1130709577 : 1052926200;
                set.main.stats[0] = 2500f;
                set.main.stats[1] = 100f;
                set.main.stats[2] = x.Value.Inventory.lootSize;
                set.main.stats[3] = x.Value.Inventory.fuelSize;
                GenerateUISettings(x.Value.Production.fuelList, set.main);
                set.main.offset = new Offset($"-205 -{set.main.height / 2}", $"185 {set.main.height / 2}");
            }
        }
        private void GenerateUISettings(List<FuelProduction> fuelList, UI ui, List<CustomItem> upgrade = null)
        {
            float fuelHeight = CalculateHeight(ui, fuelList.Count);
            int itemsProduction = 0;
            for (int i = 0; i < fuelList.Count; i++)
                if (fuelList[i].resources.Count > itemsProduction)
                    itemsProduction = fuelList[i].resources.Count;
            float prodHeight = 25f + CalculateHeight(ui, itemsProduction);
            float upgradeHeight = upgrade != null ? CalculateHeight(ui, upgrade.Count) : 0f;

            ui.fuel = new Offset($"5 -{150 + fuelHeight}", $"-5 -150");
            ui.fuelList = CalculateItemsOffsets(fuelList.Count);

            ui.production = new Offset($"5 -{175 + fuelHeight + prodHeight}", $"-5 -{175 + fuelHeight}");
            for (int i = 0; i < fuelList.Count; i++)
            {
                ui.productionList.Add(new List<Offset>());
                ui.productionList[i] = CalculateItemsOffsets(fuelList[i].resources.Count);
            }

            if (upgradeHeight > 0)
            {
                ui.height += 25f;
                ui.upgrade = new Offset($"5 -{200 + fuelHeight + prodHeight + upgradeHeight}", $"-5 -{200 + fuelHeight + prodHeight}");
                ui.upgradeList = CalculateItemsOffsets(upgrade.Count);
            }
        }
        private void GenerateRemoveUI(UI ui, int items)
        {
            ui.height = 114f;
            float itemsHeight = items > 0 ? CalculateHeight(ui, items) : 0f;
            if (itemsHeight > 0)
            {
                ui.fuel = new Offset($"5 -{50 + itemsHeight}", $"-5 -50");
                ui.fuelList = CalculateItemsOffsets(items);
            }
        }
        private float CalculateHeight(UI ui, int items)
        {
            float result = 65f;
            while (items > 6)
            {
                result += 60f;
                items -= 6;
            }
            ui.height += result;
            return result;
        }
        private List<Offset> CalculateItemsOffsets(int items)
        {
            List<Offset> result = new List<Offset>();
            int first = 0;
            int second = 0;
            float distance = 5f;
            float size = 55f;
            float formula = 0;
            float formula2 = 0;
            int count = 0;
            for (int i = 0; i < items; i++)
            {
                count = items - 6 * second > 6 ? 6 : items - 6 * second;
                formula = -((size + distance) / 2) * count + (size + distance) * first + distance / 2;
                formula2 = -(distance + size * second + distance * second);
                result.Add(new Offset($"{formula} {formula2 - size}", $"{formula + size} {formula2}"));
                first++;
                if (first == count)
                {
                    second++;
                    first = 0;
                }
            }
            return result;
        }
        private class Configuration
        {
            [JsonProperty(Rus ? "Оптимизация" : "Optimization")] internal Optimization optimization = new Optimization();
            [JsonProperty(Rus ? "Совместная работа с другими плагинами" : "Compatibility with other plugins")] internal Plugins plugins = new Plugins();
            [JsonProperty(Rus ? "SteamID аватарки для чата" : "Avatar SteamID for chat")] internal ulong chatid = 0;
            [JsonProperty(Rus ? "Формат для количества добываемых ресурсов в UI где есть рандом" : "Format of the resources amount in the UI where they can be randomized")] internal string format = "x{0}-{1}";
            [JsonProperty(Rus ? "Логировать полученные игроками карьеры?" : "Log the quarries received by players?")] internal bool log = false;
            [JsonProperty(Rus ? "Лимиты" : "Limits")] internal Limits Limit = new Limits();
            [JsonProperty(Rus ? "Рейты" : "Rates")] internal QRates Rates = new QRates();
            [JsonProperty(Rus ? "Базовые значения защиты для личных карьеров (редактировать на свой риск)" : "Default protection settings for personal quarries (edit at your own risk)")] internal Dictionary<DamageType, float> baseProtection;
            [JsonProperty(Rus ? "Версия плагина" : "Plugin Version")] internal VersionNumber PluginVersion = new VersionNumber();
            [JsonIgnore] internal List<float> protectionAmounts = new List<float>();
            internal void FillProtectionProperties()
            {
                if (baseProtection == null || baseProtection.Count == 0)
                {
                    baseProtection = new Dictionary<DamageType, float>
                    {
                        [DamageType.Generic] = 0.9f,
                        [DamageType.Hunger] = 0f,
                        [DamageType.Thirst] = 0f,
                        [DamageType.Cold] = 0f,
                        [DamageType.Drowned] = 0f,
                        [DamageType.Heat] = 0.95f,
                        [DamageType.Bleeding] = 0f,
                        [DamageType.Poison] = 0f,
                        [DamageType.Suicide] = 0f,
                        [DamageType.Bullet] = 0.99f,
                        [DamageType.Slash] = 0.99f,
                        [DamageType.Blunt] = 0.99f,
                        [DamageType.Fall] = 0f,
                        [DamageType.Radiation] = 1f,
                        [DamageType.Bite] = 1f,
                        [DamageType.Stab] = 0.99f,
                        [DamageType.Explosion] = 0.5f,
                        [DamageType.RadiationExposure] = 0f,
                        [DamageType.ColdExposure] = 0f,
                        [DamageType.Decay] = 0f,
                        [DamageType.ElectricShock] = 0f,
                        [DamageType.Arrow] = 1f,
                        [DamageType.AntiVehicle] = 1f,
                        [DamageType.Collision] = 1f,
                        [DamageType.Fun_Water] = 0f,
                        [DamageType.BeeSting] = 0f,
                        [DamageType.Paintball] = 1f
                    };
                }
                List<string> names = Enum.GetNames(typeof(DamageType)).ToList();
                names.Remove("LAST");
                if (baseProtection.Count != names.Count)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        DamageType type = (DamageType)Enum.Parse(typeof(DamageType), names[i]);
                        if (!baseProtection.ContainsKey(type))
                            baseProtection.Add(type, 1f);
                    }
                }
                for (int i = 0; i < baseProtection.Count; i++)
                    protectionAmounts.Add(baseProtection[(DamageType)i]);
            }
        }
        private class Optimization
        {
            [JsonProperty(Rus ? "Отключить функционал запрета лутания карьеров другими игроками" : "Disable loot protection from other players")] internal bool disableLoot = false;
            [JsonProperty(Rus ? "Отключить лимиты" : "Disable limits")] internal bool disableLimits = false;
            [JsonProperty(Rus ? "Отключить проверки расстояния при установке" : "Disable distance checks during installing")] internal bool disableDistance = false;
            [JsonProperty(Rus ? "Отключить ремонт карьеров киянкой" : "Disable quarry repair with hammer")] internal bool disableRepair = false;
            [JsonProperty(Rus ? "Отключить пермишены" : "Disable permissions")] internal bool disablePermissions = false;
            [JsonProperty(Rus ? "Повторная попытка выдачи карьера игроку если инвентарь переполнен через X секунд" : "Anouther attempt to give quarry to player if his inventory is full in X seconds")] internal int GiveSec = 15;
        }
        private class Plugins
        {
            [JsonProperty(Rus ? "Включить поддержку aHomeManager? (см. документацию)" : "Enable aHomeManager support? (see the documentation)")] internal bool aHomeManager = false;
        }
        private class Limits
        {
            [JsonProperty(Rus ? "Суммировать лимиты со всех пермишенов у игрока?" : "Sum up the limits from all players permissions?")] internal bool sumAll = true;
            [JsonProperty(Rus ? "Лимиты по пермишенам" : "Limits by permissions")] internal Dictionary<string, BuildLimits> permissions = new Dictionary<string, BuildLimits>();
        }
        private class BuildLimits
        {
            [JsonProperty(Rus ? "Приоритет" : "Priority")] internal int priority = 0;
            [JsonProperty(Rus ? "Карьеры" : "Quarries")] internal int limitQuarries = 3;
            [JsonProperty(Rus ? "Нефтекачки" : "Pumpjacks")] internal int limitJacks = 1;
        }
        private class QRates
        {
            [JsonProperty(Rus ? "Суммировать рейты со всех пермишенов у игрока?" : "Sum up the rates from all player's permissions?")] internal bool sumAll = true;
            [JsonProperty(Rus ? "Рейты по пермишенам (1.0 = +100%)" : "Rates by permissions (1.0 = +100%)")] internal Dictionary<string, float> permissions = new Dictionary<string, float>();
        }
        private class BaseQuarry
        {
            [JsonProperty(PropertyName = Rus ? "Включить этот карьер?" : "Enable this quarry?", Order = 1)] internal bool enable = false;
            [JsonProperty(PropertyName = Rus ? "Инвентарь" : "Inventory", Order = 7)] internal QInventory Inventory = new QInventory();
            [JsonProperty(PropertyName = Rus ? "Производство" : "Production", Order = 11)] internal QProduction Production = new QProduction();
        }
        private class StaticQuarry : BaseQuarry
        {
            [JsonProperty(PropertyName = Rus ? "Защита лута" : "Loot Protection", Order = 8)] internal SProtection Protection = new SProtection();
        }
        private class PersonalQuarry : BaseQuarry
        {
            [JsonProperty(PropertyName = Rus ? "Название карьера" : "Quarry custom name", Order = 2)] internal string name = "Custom Name";
            [JsonProperty(PropertyName = Rus ? "SkinID карьерa" : "Quarry SkinID", Order = 3)] internal ulong skinID = 0;
            [JsonProperty(PropertyName = Rus ? "Структура" : "Structure", Order = 4)] internal QStructure Structure = new QStructure();
            [JsonProperty(PropertyName = Rus ? "Ремув" : "Remove", Order = 5)] internal QRemove Remove = new QRemove();
            [JsonProperty(PropertyName = Rus ? "Улучшениe" : "Upgrade", Order = 6)] internal QUpgrade Upgrade = new QUpgrade();
            [JsonProperty(PropertyName = Rus ? "Защита от других игроков" : "Protection from other players", Order = 8)] internal QProtection Protection = new QProtection();
            [JsonProperty(PropertyName = Rus ? "Ремонт" : "Repair", Order = 9)] internal QRepair Repair = new QRepair();
            [JsonProperty(PropertyName = Rus ? "Подмена префаба" : "Prefab substitution", Order = 10)] internal QPrefab Prefab = new QPrefab();
        }
        private class QStructure
        {
            [JsonProperty(Rus ? "Прочность (очки здоровья)" : "Health points")] internal float health = 2500f;
            [JsonProperty(Rus ? "Снижение получаемого урона на % [0.0-100.0] (100 = неуязвимый)" : "Protection percentage [0.0-100.0] (100 = immortal)")] internal float protection = 0f;
        }
        private class QRemove
        {
            [JsonProperty(Rus ? "Разрешить удаление карьера?" : "Enable remove?")] internal bool allow = true;
            [JsonProperty(Rus ? "Возвращать предметы?" : "Enable refund?")] internal bool refund = true;
            [JsonProperty(Rus ? "Список возвращаемых предметов" : "Refund item list")] internal List<NamedItem> itemList = new List<NamedItem>();
        }
        private class QUpgrade
        {
            [JsonProperty(Rus ? "SkinID улучшенного карьера (0 = отключить улучшение)" : "SkinID of the upgraded quarry (0 = disable upgrades)")] internal ulong newSkin = 0;
            [JsonProperty(Rus ? "Стоимость улучшения" : "Upgrade cost")] internal List<CustomItem> Cost = new List<CustomItem>();
        }
        private class QInventory
        {
            [JsonProperty(Rus ? "Количество слотов в ресурсном отсеке" : "Resource container capacity")] internal int lootSize = 18;
            [JsonProperty(Rus ? "Количество слотов в топливном отсеке" : "Fuel container capacity")] internal int fuelSize = 6;
        }
        private class SProtection : QProtection
        {
            [JsonProperty(Rus ? "Включить функционал защиты лута?" : "Enable loot protection functionality?")] internal bool enable = false;
            [JsonProperty(Rus ? "Требуемый пермишен для активации" : "Required permission for activation")] internal string perm = "aQuarry.StaticLock";
            [JsonProperty(Rus ? "Тип уведомлений о начале добычи и блокировке (0 = выкл, 1 = для владельца, 2 = для всех)" : "Type of mining start and lock notifications (0 = disable, 1 = for owner, 2 = for all)")] internal int startNotify = 2;
            [JsonProperty(Rus ? "Тип уведомлений об окончании добычи (0 = выкл, 1 = для владельца, 2 = для всех)" : "The type of notifications about the end of mining (0 = disable, 1 = for owner, 2 = for all)")] internal int endNotify = 2;
            [JsonProperty(Rus ? "Тип уведомлений об окончании блокировки (0 = выкл, 1 = для владельца, 2 = для всех)" : "The type of unlock notifications (0 = disable, 1 = for owner, 2 = for all)")] internal int unlockNotify = 2;
            [JsonProperty(Rus ? "Снимать блокировку через Х минут после окончания добычи" : "Unlock after X minutes after the end of mining")] internal int unlockTimer = 15;
        }
        private class QProtection
        {
            [JsonProperty(Rus ? "Только владелец карьера может его включать/выключать?" : "Only owner can toggle?")] internal bool toggle = true;
            [JsonProperty(Rus ? "Только владелец карьера может его лутать?" : "Only owner can loot?")] internal bool loot = true;
            [JsonProperty(Rus ? "Только владелец карьера может получить доступ к управлению?" : "Only owner can manage?")] internal bool manage = true;
            [JsonProperty(Rus ? "Включая команду владельца?" : "Include his team?")] internal bool team = true;
        }
        private class QRepair
        {
            [JsonProperty(Rus ? "Разрешить ремонт киянкой?" : "Allow repair with hammer hit?")] internal bool allow = false;
            [JsonProperty(Rus ? "Нельзя ремонтировать в течении Х секунд после получения урона" : "Unable to repair X seconds after taking damage")] internal float blockTime = 0f;
            [JsonProperty(Rus ? "Восстановление прочности за удар" : "Repair amount for hit")] internal float repair = 100f;
            [JsonProperty(Rus ? "Требовать плату?" : "Enable cost?")] internal bool needCost = false;
            [JsonProperty(Rus ? "Стоимость ремонта" : "Repair cost")] internal List<CustomItem> Cost = new List<CustomItem>();
        }
        private class QPrefab
        {
            [JsonProperty(Rus ? "Использовать подмену префаба?" : "Use prefab substitution?")] internal bool custom = true;
            [JsonProperty(Rus ? "Это карьер (true) или нефтекачка (false) ?" : "This is a quarry (true) or pumpjack (false) ?")] internal bool quarry = true;
            [JsonProperty(Rus ? "ShortName предмета из которого создаем карьер" : "ShortName of the item from which we create a quarry")] internal string prefab = "furnace.large";
            [JsonProperty(Rus ? "Минимальное расстояние от игрока до карьера (при установке)" : "Minimum distance from player to quarry (when installed)")] internal float distance1 = 3f;
            [JsonProperty(Rus ? "Минимальное расстояние от карьера до строений (при установке)" : "Minimum distance from quarry to constructions (when installed)")] internal float distance2 = 17f;
            [JsonProperty(Rus ? "Дополнительная настройка высоты (при установке)" : "Additional height setting (when installed)")] internal float height = -2f;
        }
        private class QProduction
        {
            [JsonProperty(Rus ? "Список используемого топлива и его настройки добычи" : "List of the fuel used and its production settings")] internal List<FuelProduction> fuelList = new List<FuelProduction>();
        }
        private class FuelProduction : CustomItem
        {
            [JsonProperty(PropertyName = Rus ? "Сколько секунд карьер может работать на одном поглощении этого топлива?" : "How many seconds can quarry work on one consume of this fuel?", Order = 5)] internal float fuelTimer = 5f;
            [JsonProperty(PropertyName = Rus ? "Интервал производства ресурсов на этом топливе (секунды)" : "Resource production interval on this fuel (seconds)", Order = 6)] internal float productionTimer = 5f;
            [JsonProperty(PropertyName = Rus ? "Добываемые ресурсы на этом топливе" : "Gathering resources on this fuel", Order = 7)] internal List<ResourceItem> resources = new List<ResourceItem>();
            internal void ValidateProduction()
            {
                if (fuelTimer < productionTimer)
                {
                    instance.PrintError($"Configuration Error! Work per fuel can not be less than production interval. Fixing...");
                    fuelTimer = productionTimer;
                }
                ValidateItem();
                resources.ForEach(x => x.ValidateItem());
            }
        }
        private class CustomItem
        {
            [JsonIgnore] internal int itemID = 0;
            [JsonProperty(PropertyName = "ShortName", Order = 2)] internal string shortname = "stones";
            [JsonProperty(PropertyName = "SkinID", Order = 3)] internal ulong skin = 0;
            [JsonProperty(PropertyName = Rus ? "Количество" : "Amount", Order = 4)] internal int amount = 1000;
            internal void ValidateItem()
            {
                ItemDefinition def = ItemManager.FindItemDefinition(shortname);
                if (def == null)
                    instance.PrintError($"Configuration Error! Invalid shortname: '{shortname}'");
                else itemID = def.itemid;
            }
        }
        private class NamedItem : CustomItem
        {
            [JsonProperty(PropertyName = Rus ? "Название (пусто = не менять)" : "Custom name (empty = default)", Order = 1)] internal string name = "";
        }
        private class ResourceItem : NamedItem
        {
            [JsonProperty(PropertyName = Rus ? "Максимальное количество" : "Amount max", Order = 5)] internal int amountMax = 1000;
            [JsonProperty(PropertyName = Rus ? "Шанс появления предмета [0.0-100.0]" : "Probability [0.0-100.0]", Order = 6)] internal float chance = 100f;
            [JsonProperty(PropertyName = Rus ? "Не учитывать рейты по пермишенам" : "Do not use permission rates", Order = 7)] internal bool disableRate = false;
        }
        #endregion
        #region [Unity]
        private class CustomQuarry : FacepunchBehaviour
        {
            internal MiningQuarry quarry;
            private BaseEntity engine;
            private ItemContainer lootContainer;
            private List<Item> fuelContainer;
            private ulong fuelContainerId;
            private List<FuelProduction> quarryConfig;
            private Item currentFuel;
            private FuelProduction config;
            internal float rate = 1f;
            internal bool isEnabled = false;
            private float pendingWork = 0;
            private float workDone = 0;
            private float lastProdTime;
            private bool canBeLocked = false;
            private SProtection sProtection;
            private string grid;
            internal List<BasePlayer> looters = new List<BasePlayer>();
            private void Awake()
            {
                quarry = GetComponent<MiningQuarry>();
                engine = quarry.engineSwitchPrefab.instance;
                lootContainer = quarry.hopperPrefab.instance.GetComponent<StorageContainer>().inventory;
                fuelContainer = quarry.fuelStoragePrefab.instance.GetComponent<StorageContainer>().inventory.itemList;
                fuelContainerId = quarry.fuelStoragePrefab.instance.GetComponent<StorageContainer>().inventory.uid.Value;
                instance.CustomQuarries.Add(quarry.net.ID.Value, this);
            }
            internal void UpdateSettings(bool isStatic = false)
            {
                StorageContainer lootStorage = quarry.hopperPrefab.instance.GetComponent<StorageContainer>();
                StorageContainer fuelStorage = quarry.fuelStoragePrefab.instance.GetComponent<StorageContainer>();
                quarry.isStatic = isStatic;
                if (isStatic)
                {
                    StaticQuarry settings = instance.statics[quarry.staticType];
                    fuelStorage.inventory.capacity = settings.Inventory.fuelSize;
                    fuelStorage.skinID = instance.staticSkins[quarry.staticType];
                    lootStorage.inventory.capacity = settings.Inventory.lootSize;
                    lootStorage.skinID = instance.staticSkins[quarry.staticType];
                    quarryConfig = settings.Production.fuelList;
                    canBeLocked = settings.Protection.enable;
                    if (canBeLocked)
                    {
                        sProtection = settings.Protection;
                        grid = MapHelper.PositionToString(quarry.transform.position);
                    }
                }
                else
                {
                    PersonalQuarry settings = instance.personal[quarry.skinID];
                    fuelStorage.inventory.capacity = settings.Inventory.fuelSize;
                    fuelStorage.skinID = quarry.skinID;
                    fuelStorage.OwnerID = quarry.OwnerID;
                    lootStorage.inventory.capacity = settings.Inventory.lootSize;
                    lootStorage.skinID = quarry.skinID;
                    lootStorage.OwnerID = quarry.OwnerID;
                    quarryConfig = settings.Production.fuelList;
                    quarry._maxHealth = settings.Structure.health;
                    quarry._health = settings.Structure.health;
                    quarry.repair.enabled = !instance.config.optimization.disableRepair && settings.Repair.allow;
                    rate = instance.GetPlayerRates(quarry.OwnerID.ToString());
                    quarry.baseProtection = protectionProperties[settings.Structure.protection];
                }
                List<ItemDefinition> definitions = new List<ItemDefinition>();
                List<int> itemIds = new List<int>();
                for (int i = 0; i < quarryConfig.Count; i++)
                {
                    if (itemIds.Contains(quarryConfig[i].itemID)) continue;
                    itemIds.Add(quarryConfig[i].itemID);
                    definitions.Add(ItemManager.FindItemDefinition(quarryConfig[i].itemID));
                }
                fuelStorage.inventory.onlyAllowedItems = definitions.ToArray();
                fuelStorage.inventory.canAcceptItem = null;
                quarry._linkedDeposit = null;
                quarry.pendingWork = 604800f;
                quarry.processRate = 3600f;
                bool isOn = quarry.HasFlag(BaseEntity.Flags.On);
                if (instance.quarryCash.ContainsKey(quarry.net.ID.Value))
                {
                    QuarryCash cash = instance.quarryCash[quarry.net.ID.Value];
                    quarry._health = cash.hp;
                    pendingWork = cash.pendingWork;
                    workDone = cash.workDone;
                    if (cash.fuelID > 0)
                        foreach (FuelProduction x in quarryConfig)
                            if (cash.fuelID == x.itemID && cash.fuelSkin == x.skin)
                                config = x;
                    if (cash.isOn)
                    {
                        if (isStatic && cash.lockOwner > 0 && instance.HasStaticPermission(cash.lockOwner.ToString(), quarry.staticType))
                            SetOwner(cash.lockOwner);
                        isOn = true;
                    }
                }
                else
                {
                    pendingWork = 0;
                    workDone = 0;
                    config = null;
                }
                if (isOn)
                    Toggle();
                else
                    quarry.SendNetworkUpdate();
            }
            internal void Upgrade()
            {
                EndLoot();
                SetOn(false);
                quarry.skinID = instance.personal[quarry.skinID].Upgrade.newSkin;
                UpdateSettings();
                Effect.server.Run("assets/bundled/prefabs/fx/build/promote_toptier.prefab", quarry, 0u, Vector3.zero, Vector3.zero);
            }
            internal void Toggle(ulong userid = 0)
            {
                if (!isEnabled && CheckFuel())
                    SetOn(true, userid);
                else SetOn(false);
            }
            private void SetOn(bool enable, ulong userid = 0)
            {
                if (isEnabled == enable) return;
                isEnabled = enable;
                if (enable)
                {
                    quarry.CancelInvoke(quarry.ProcessResources);
                    lastProdTime = Time.realtimeSinceStartup;
                    InvokeRepeating(Process, config.productionTimer - workDone, config.productionTimer);
                    workDone = 0;
                    if (canBeLocked && userid > 0 && instance.HasStaticPermission(userid.ToString(), quarry.staticType))
                    {
                        if (quarry.OwnerID > 0)
                            CancelInvoke(UnlockQuarry);
                        else LockQuarry(userid);
                    }
                }
                else
                {
                    workDone = Time.realtimeSinceStartup - lastProdTime;
                    CancelInvoke(Process);
                    if (canBeLocked && quarry.OwnerID > 0)
                    {
                        if (sProtection.unlockTimer == 0)
                            UnlockQuarry();
                        else
                        {
                            if (sProtection.endNotify != 0)
                                instance.UnlockNotify(quarry.OwnerID, quarry.staticType, grid, sProtection.endNotify, sProtection.unlockTimer);
                            Invoke(UnlockQuarry, sProtection.unlockTimer * 60);
                        }
                    }
                }
                quarry.SetFlagLocal(BaseEntity.Flags.On, enable);
                engine.SetFlagLocal(BaseEntity.Flags.On, enable);
                quarry.SendNetworkUpdate();
                engine.SendNetworkUpdate();
            }
            private void LockQuarry(ulong userid)
            {
                if (sProtection.startNotify != 0)
                    instance.LockNotify(userid, quarry.staticType, grid, sProtection.startNotify);
                SetOwner(userid);
            }
            private void UnlockQuarry()
            {
                if (sProtection.unlockNotify != 0)
                    instance.UnlockNotify(quarry.OwnerID, quarry.staticType, grid, sProtection.unlockNotify);
                SetOwner(0);
            }
            private void SetOwner(ulong id)
            {
                quarry.OwnerID = id;
                if (sProtection.loot)
                {
                    quarry.hopperPrefab.instance.GetComponent<StorageContainer>().OwnerID = id;
                    quarry.fuelStoragePrefab.instance.GetComponent<StorageContainer>().OwnerID = id;
                }
            }
            private bool TryFindNewFuel()
            {
                if (fuelContainer.Count == 0)
                    return false;
                List<Item> list = fuelContainer.OrderBy(x => x.position).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    foreach (FuelProduction x in quarryConfig)
                    {
                        if (list[i].info.itemid == x.itemID && list[i].skin == x.skin)
                        {
                            if (list[i].amount >= x.amount || CheckAnoutherSlotsForItem(list[i], x.amount - list[i].amount))
                            {
                                if (config == null || (config.itemID != list[i].info.itemid || config.skin != list[i].skin))
                                {
                                    pendingWork = 0;
                                    workDone = 0;
                                    config = x;
                                    if (isEnabled)
                                    {
                                        CancelInvoke(Process);
                                        InvokeRepeating(Process, config.productionTimer, config.productionTimer);
                                    }
                                }
                                currentFuel = list[i];
                                return ConsumeFuel();
                            }
                            break;
                        }
                    }
                }
                return false;
            }
            private bool CheckAnoutherSlotsForItem(Item item, int need)
            {
                for (int i = 0; i < fuelContainer.Count; i++)
                {
                    if (fuelContainer[i] == item) continue;
                    if (fuelContainer[i].info.itemid == item.info.itemid && fuelContainer[i].skin == item.skin)
                    {
                        if (fuelContainer[i].amount + item.amount >= need)
                        {
                            item.amount += need;
                            item.MarkDirty();
                            fuelContainer[i].amount -= need;
                            if (fuelContainer[i].amount == 0)
                            {
                                fuelContainer[i].RemoveFromContainer();
                                fuelContainer[i].Remove();
                            }
                            else fuelContainer[i].MarkDirty();
                            return true;
                        }
                        else
                        {
                            item.amount += fuelContainer[i].amount;
                            item.MarkDirty();
                            need -= fuelContainer[i].amount;
                            fuelContainer[i].RemoveFromContainer();
                            fuelContainer[i].Remove();
                        }
                    }
                }
                return false;
            }
            private bool CheckFuel()
            {
                if (config == null)
                    return TryFindNewFuel();
                if (pendingWork >= config.productionTimer)
                    return true;
                if (currentFuel == null || currentFuel.parent == null || currentFuel.parent.uid.Value != fuelContainerId || !ConsumeFuel())
                    return TryFindNewFuel();
                return true;
            }
            private bool ConsumeFuel()
            {
                if (currentFuel.amount >= config.amount || CheckAnoutherSlotsForItem(currentFuel, config.amount - currentFuel.amount))
                {
                    currentFuel.amount -= config.amount;
                    pendingWork += config.fuelTimer;
                    if (currentFuel.amount == 0)
                    {
                        currentFuel.RemoveFromContainer();
                        currentFuel.Remove();
                        currentFuel = null;
                    }
                    else currentFuel.MarkDirty();
                    return true;
                }
                return false;
            }
            private void Process()
            {
                pendingWork -= config.productionTimer;
                lastProdTime = Time.realtimeSinceStartup;
                if (!SpawnResources() || !CheckFuel())
                    SetOn(false);
            }
            internal bool SpawnResources()
            {
                bool success = true;
                foreach (ResourceItem x in config.resources)
                {
                    if (UnityEngine.Random.Range(0.0f, 100.0f) > x.chance) continue;
                    int amount = x.disableRate ? UnityEngine.Random.Range(x.amount, x.amountMax) : (int)(UnityEngine.Random.Range(x.amount, x.amountMax) * rate);
                    Item item = ItemManager.CreateByItemID(x.itemID, amount, x.skin);
                    if (x.name.Length > 0) item.name = x.name;
                    if (!item.MoveToContainer(lootContainer))
                    {
                        item.Remove();
                        success = false;
                    }
                }
                return success;
            }
            internal void EndLoot()
            {
                foreach (BasePlayer player in looters.ToList())
                    player.EndLooting();
            }
            internal QuarryCash SaveQuarry()
            {
                QuarryCash cash = new QuarryCash { isOn = isEnabled, hp = quarry._health};
                if (isEnabled)
                {
                    if (canBeLocked && quarry.OwnerID.IsSteamId())
                    {
                        cash.lockOwner = quarry.OwnerID;
                        SetOwner(0);
                    }
                    SetOn(false);
                }
                cash.pendingWork = pendingWork;
                cash.workDone = workDone;
                if (config != null)
                {
                    cash.fuelID = config.itemID;
                    cash.fuelSkin = config.skin;
                }
                return cash;
            }
            internal void Destroy() => Destroy(this);
        }
        #endregion
        #region [Functions]
        private Dictionary<ulong, CustomQuarry> CustomQuarries = new Dictionary<ulong, CustomQuarry>();
        private List<ulong> canBeRemoved = new List<ulong>();
        private List<ulong> canBeUpgraded = new List<ulong>();
        private List<ulong> upradeUIUsers = new List<ulong>();
        private void LoadCustomQuarries()
        {
            quarryCash = Interface.Oxide.DataFileSystem.GetFile(Name).ReadObject<Dictionary<ulong, QuarryCash>>();
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                if (entity is MiningQuarry quarry)
                {
                    if (quarry.skinID != 0 && personal.ContainsKey(quarry.skinID))
                        CreateCustomQuarry(quarry);
                    else if (quarry.skinID == 0 && quarry.isStatic && statics.ContainsKey(quarry.staticType))
                        CreateCustomQuarry(quarry, null, true);
                }
            }
            quarryCash.Clear();
            Interface.Oxide.DataFileSystem.GetFile(Name).WriteObject(quarryCash);
        }
        private void CreateCustomQuarry(MiningQuarry quarry, BasePlayer player = null, bool isStatic = false)
        {
            if (!quarry.gameObject.HasComponent<CustomQuarry>())
            {
                quarry.gameObject.AddComponent<CustomQuarry>().UpdateSettings(isStatic);
                if (!isStatic && !config.optimization.disableLimits)
                {
                    if (!limitsData.ContainsKey(quarry.OwnerID))
                    {
                        limitsData.Add(quarry.OwnerID, new LimitsData());
                        UpdateLimits(quarry.OwnerID);
                    }
                    LimitsData limit = limitsData[quarry.OwnerID];
                    if (personal[quarry.skinID].Prefab.quarry)
                    {
                        limit.placedQuarries++;
                        player?.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.QPlaced", this, player.UserIDString), limit.placedQuarries, limit.limitQuarries));
                    }
                    else
                    {
                        limit.placedJacks++;
                        player?.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.JPlaced", this, player.UserIDString), limit.placedJacks, limit.limitJacks));
                    }
                }
                if (player != null)
                    Interface.CallHook("OnCustomQuarryBuilded", quarry, player, personal[quarry.skinID].Prefab.quarry);
            }
            else timer.In(1f, () => CreateCustomQuarry(quarry, player, isStatic));
        }
        private void DestroyCustomQuarries()
        {
            foreach (CustomQuarry x in CustomQuarries.Values)
            {
                if (x == null) continue;
                quarryCash.Add(x.quarry.net.ID.Value, x.SaveQuarry());
                x.Destroy();
            }
            Interface.Oxide.DataFileSystem.GetFile(Name).WriteObject(quarryCash);
            CustomQuarries.Clear();
        }
        private bool CanAffordUpgrade(PlayerInventory inv, List<CustomItem> cost, out Dictionary<Item, int> remove, out int[] has)
        {
            List<Item> items = inv.containerMain.itemList.ToList();
            items.AddRange(inv.containerBelt.itemList);
            remove = new Dictionary<Item, int>();
            has = new int[cost.Count];
            if (items.Count == 0) return false;
            int accepted = 0;
            for (int x = 0; x < cost.Count; x++)
            {
                int need = cost[x].amount;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].info.itemid == cost[x].itemID && items[i].skin == cost[x].skin)
                    {
                        has[x] += items[i].amount;
                        if (need == 0) continue;
                        if (items[i].amount >= need)
                        {
                            remove.Add(items[i], need);
                            need = 0;
                        }
                        else
                        {
                            remove.Add(items[i], items[i].amount);
                            need -= items[i].amount;
                        }
                    }
                }
                if (need == 0)
                    accepted++;
            }
            return accepted == cost.Count;
        }
        private void PayForUpgrade(Dictionary<Item, int> remove)
        {
            foreach (KeyValuePair<Item, int> x in remove)
            {
                if (x.Key.amount > x.Value)
                {
                    x.Key.amount -= x.Value;
                    x.Key.MarkDirty();
                }
                else x.Key.Remove();
            }
        }
        private List<CustomQuarry> GetPlayerQuarries(ulong userid)
        {
            List<CustomQuarry> list = new List<CustomQuarry>();
            foreach (CustomQuarry x in CustomQuarries.Values)
                if (x.quarry.OwnerID == userid)
                    list.Add(x);
            return list;
        }
        private void UpdateLimits(ulong userid)
        {
            string id = userid.ToString();
            limitsData[userid].Reset();
            foreach (KeyValuePair<string, BuildLimits> x in config.Limit.permissions.OrderByDescending(x => x.Value.priority))
            {
                if (permission.UserHasPermission(id, x.Key))
                {
                    limitsData[userid].Sum(x.Value);
                    if (!config.Limit.sumAll) break;
                }
            }
        }
        private float GetPlayerRates(string id)
        {
            if (config.optimization.disablePermissions) return 1f;
            float rate = 1f;
            foreach (KeyValuePair<string, float> x in config.Rates.permissions.OrderByDescending(x => x.Value))
            {
                if (permission.UserHasPermission(id, x.Key))
                {
                    rate += x.Value;
                    if (!config.Rates.sumAll) break;
                }
            }
            return rate;
        }
        private void SetupPermissions(string id)
        {
            ulong userid = ulong.Parse(id);
            if (!config.optimization.disableLimits && limitsData.ContainsKey(userid))
                UpdateLimits(userid);
            List<CustomQuarry> quarry = GetPlayerQuarries(userid);
            if (quarry.Count > 0)
            {
                float rate = GetPlayerRates(id);
                for (int i = 0; i < quarry.Count; i++)
                    quarry[i].rate = rate;
            }
        }
        private void LockNotify(ulong userid, QuarryType type, string grid, int notifyType)
        {
            BasePlayer owner = BasePlayer.FindByID(userid);
            if (notifyType == 1)
                owner.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.LockNotify.Owner", this, owner.UserIDString), lang.GetMessage($"UI.{type}", this, owner.UserIDString)));
            else
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.LockNotify.All", this, player.UserIDString), lang.GetMessage($"UI.{type}", this, player.UserIDString), grid, owner.displayName));
        }
        private void UnlockNotify(ulong userid, QuarryType type, string grid, int notifyType, int timer = 0)
        {
            if (notifyType == 1)
            {
                BasePlayer player = BasePlayer.FindByID(userid);
                if (player != null)
                    Notify(player);
            }
            else
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    Notify(player);
            }
            void Notify(BasePlayer player)
            {
                if (timer > 0)
                    player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.EndNotify", this, player.UserIDString), lang.GetMessage($"UI.{type}", this, player.UserIDString), grid, timer));
                else player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.UnlockNotify", this, player.UserIDString), lang.GetMessage($"UI.{type}", this, player.UserIDString), grid));
            }
        }
        private bool HasStaticPermission(string id, QuarryType type) => permission.UserHasPermission(id, statics[type].Protection.perm);
        private void GiveQuarryToPlayer(BasePlayer player, ulong skin, Item item = null)
        {
            if (item == null)
            {
                string shortname = personal[skin].Prefab.custom ? personal[skin].Prefab.prefab : personal[skin].Prefab.quarry ? "mining.quarry" : "mining.pumpjack";
                item = ItemManager.CreateByName(shortname, 1, skin);
                if (personal[skin].name.Length > 0)
                    item.name = personal[skin].name;
            }
            if (player.inventory.containerMain.itemList.Count >= 24 && player.inventory.containerBelt.itemList.Count >= 6)
            {
                player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.FullInventory", this, player.UserIDString), config.optimization.GiveSec));
                timer.In(config.optimization.GiveSec, () => { GiveQuarryToPlayer(player, skin, item); });
                return;
            }
            player.GiveItem(item);
            Planner entity = item.GetHeldEntity() as Planner;
            if (entity != null)
            {
                entity.skinID = skin;
                entity.SendNetworkUpdate();
            }
            string mes = Rus ? $"Игрок {player.displayName}({player.userID}) получил «{personal[skin].name}» ({skin})" : $"Player {player.displayName}({player.userID}) recieves «{personal[skin].name}» ({skin})";
            PrintWarning(mes);
            if (config.log)
                LogToFile("Log", $"[{DateTime.Now:dd-MM-yyyy HH:mm:ss}] " + mes, this, false);
        }
        #endregion
        #region [API]
        private List<ulong> GetAllQuarrySkins() => personal.Keys.ToList();
        #endregion
        #region [Oxide]
        private void OnUserPermissionGranted(string id, string perm)
        {
            if (!perm.StartsWith("aQuarry.")) return;
            SetupPermissions(id);
        }
        private void OnUserPermissionRevoked(string id, string perm)
        {
            if (!perm.StartsWith("aQuarry.")) return;
            SetupPermissions(id);
        }
        private void OnUserGroupAdded(string id, string group)
        {
            string[] perms = permission.GetGroupPermissions(group);
            if (perms == null) return;
            for (int i = 0;i < perms.Length;i++)
                if (perms[i].StartsWith("aQuarry."))
                {
                    SetupPermissions(id);
                    break;
                }
        }
        private void OnGroupPermissionGranted(string group, string perm)
        {
            if (!perm.StartsWith("aQuarry.")) return;
            string[] players = permission.GetUsersInGroup(group);
            if (players == null) return;
            for (int i = 0; i < players.Length; i++)
                SetupPermissions(players[i].Substring(0, 17));
        }
        private void OnUserGroupRemoved(string id, string group) => OnUserGroupAdded(id, group);
        private void OnGroupPermissionRevoked(string group, string perm) => OnGroupPermissionGranted(group, perm);
        private void OnQuarryToggled(MiningQuarry quarry, BasePlayer player)
        {
            bool canToggle = false;
            if (quarry.skinID != 0 && personal.ContainsKey(quarry.skinID))
            {
                AbortToggle();
                QProtection set = personal[quarry.skinID].Protection;
                canToggle = !set.toggle || quarry.OwnerID == player.userID;
                if (!canToggle && set.team)
                    CheckTeam();
            }
            else if (quarry.isStatic && statics.ContainsKey(quarry.staticType))
            {
                AbortToggle();
                SProtection set = statics[quarry.staticType].Protection;
                canToggle = quarry.OwnerID == 0 || !set.toggle || quarry.OwnerID == player.userID;
                if (!canToggle && set.team)
                    CheckTeam();
            }
            if (canToggle)
                CustomQuarries[quarry.net.ID.Value].Toggle(player.userID);
            void AbortToggle()
            {
                if (quarry.HasFlag(BaseEntity.Flags.On))
                    quarry.SetOn(false);
                else
                {
                    quarry.SetOn(true);
                    quarry.CancelInvoke(quarry.ProcessResources);
                }
            }
            void CheckTeam()
            {
                PlayerTeam team = ServerInstance.FindPlayersTeam(quarry.OwnerID);
                if (team != null && team.members.Contains(player.userID))
                    canToggle = true;
            }
        }
        private object CanBuild(Planner plan, Construction entity, Construction.Target target)
        {
            if (plan.skinID == 0 || !personal.ContainsKey(plan.skinID)) return null;
            BasePlayer player = target.player;
            QPrefab prefab = personal[plan.skinID].Prefab;
            if (Interface.CallHook("CanBuildCustomQuarry", plan, player, prefab.quarry) is bool val && !val)
                return false;
            if (!config.optimization.disableLimits)
            {
                if (!limitsData.ContainsKey(player.userID))
                {
                    limitsData.Add(player.userID, new LimitsData());
                    UpdateLimits(player.userID);
                }
                LimitsData limit = limitsData[player.userID];
                if (prefab.quarry && limit.placedQuarries >= limit.limitQuarries)
                {
                    player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.QLimit", this, player.UserIDString), limit.limitQuarries));
                    return false;
                }
                if (!prefab.quarry && limit.placedJacks >= limit.limitJacks)
                {
                    player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.JLimit", this, player.UserIDString), limit.limitJacks));
                    return false;
                }
            }
            if (prefab.custom && !config.optimization.disableDistance)
            {
                if (Vector3.Distance(player.ServerPosition, target.position) < prefab.distance1)
                {
                    player.SendConsoleCommand("chat.add", 2, config.chatid, lang.GetMessage("Chat.TooClose", this, player.UserIDString));
                    return false;
                }
                List<BaseEntity> list = new List<BaseEntity>();
                Vis.Entities(target.position, prefab.distance2, list, 2097152);
                if (list.Count > 0)
                {
                    player.SendConsoleCommand("chat.add", 2, config.chatid, lang.GetMessage("Chat.HasEntities", this, player.UserIDString));
                    return false;
                }
            }
            return null;
        }
        private void OnEntityBuilt(Planner plan, GameObject obj)
        {
            if (plan.skinID == 0 || !personal.ContainsKey(plan.skinID)) return;
            MiningQuarry quarry;
            BasePlayer player = plan.GetOwnerPlayer();
            QPrefab prefab = personal[plan.skinID].Prefab;
            if (prefab.custom)
            {
                BaseEntity ent = obj.ToBaseEntity();
                NextTick(() => { ent.Kill(); });
                Vector3 pos = ent.transform.position + new Vector3(0f, prefab.height, 0f) + (ent.transform.position - player.transform.position).normalized * 2;
                string asset = prefab.quarry ? "assets/bundled/prefabs/static/miningquarry_static.prefab" : "assets/prefabs/deployable/oil jack/mining.pumpjack.prefab";
                quarry = GameManager.server.CreateEntity(asset, pos, ent.transform.rotation) as MiningQuarry;
                if (quarry == null) return;
                quarry.skinID = plan.skinID;
                quarry.OwnerID = player.userID;
                quarry.Spawn();
            }
            else quarry = obj.ToBaseEntity().GetComponent<MiningQuarry>();
            if (quarry == null) return;
            CreateCustomQuarry(quarry, player);
        }
        private object OnStructureRepair(MiningQuarry quarry, BasePlayer player)
        {
            if (quarry.skinID == 0 || !personal.ContainsKey(quarry.skinID)) return null;
            QRepair repair = personal[quarry.skinID].Repair;
            if (quarry._health >= quarry._maxHealth)
            {
                Effect.server.Run("assets/bundled/prefabs/fx/build/repair_failed.prefab", quarry, 0u, Vector3.zero, Vector3.zero);
                player.SendConsoleCommand("chat.add", 2, config.chatid, lang.GetMessage("Chat.NoRepair", this, player.UserIDString));
            }
            else if (quarry.SecondsSinceAttacked <= repair.blockTime)
            {
                Effect.server.Run("assets/bundled/prefabs/fx/build/repair_failed.prefab", quarry, 0u, Vector3.zero, Vector3.zero);
                player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.NoRepairDamage", this, player.UserIDString), $"{repair.blockTime - quarry.SecondsSinceAttacked:N0}"));
            }
            else
            {
                if (repair.needCost)
                {
                    Dictionary<Item, int> remove;
                    int[] has;
                    if (CanAffordUpgrade(player.inventory, repair.Cost, out remove, out has))
                        PayForUpgrade(remove);
                    else
                    {
                        Effect.server.Run("assets/bundled/prefabs/fx/build/repair_failed.prefab", quarry, 0u, Vector3.zero, Vector3.zero);
                        string mes = lang.GetMessage("Chat.NoRepairRecources", this, player.UserIDString);
                        foreach (CustomItem item in repair.Cost)
                            mes += $"\n{lang.GetMessage($"{item.shortname}_{item.skin}", this, player.UserIDString)} x{item.amount}";
                        player.SendConsoleCommand("chat.add", 2, config.chatid, mes);
                        return false;
                    }
                }
                quarry._health += repair.repair;
                if (quarry._health > quarry._maxHealth)
                {
                    quarry._health = quarry._maxHealth;
                    Effect.server.Run("assets/bundled/prefabs/fx/build/repair_full.prefab", quarry, 0u, Vector3.zero, Vector3.zero);
                }
                else Effect.server.Run("assets/bundled/prefabs/fx/build/repair.prefab", quarry, 0u, Vector3.zero, Vector3.zero);
                quarry.SendNetworkUpdate();
            }
            return false;
        }
        private void OnEntityKill(MiningQuarry quarry)
        {
            if (quarry.OwnerID == 0 || quarry.skinID == 0 || !personal.ContainsKey(quarry.skinID)) return;
            if (!config.optimization.disableLimits && limitsData.ContainsKey(quarry.OwnerID))
            {
                BasePlayer player = BasePlayer.FindByID(quarry.OwnerID);
                LimitsData data = limitsData[quarry.OwnerID];
                if (personal[quarry.skinID].Prefab.quarry)
                {
                    data.placedQuarries--;
                    if (player != null)
                        player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.QDestroyed", this, player.UserIDString), data.limitQuarries - data.placedQuarries));
                }
                else
                {
                    data.placedJacks--;
                    if (player != null)
                        player.SendConsoleCommand("chat.add", 2, config.chatid, string.Format(lang.GetMessage("Chat.JDestroyed", this, player.UserIDString), data.limitJacks - data.placedJacks));
                }
            }
            Interface.CallHook("OnCustomQuarryKilled", quarry, personal[quarry.skinID].Prefab.quarry);
            CustomQuarries.Remove(quarry.net.ID.Value);
        }
        private object CanLootEntity(BasePlayer player, ResourceExtractorFuelStorage container)
        {
            if (container.OwnerID == 0 || container.skinID == 0) return null;
            if (personal.ContainsKey(container.skinID))
            {
                QProtection set = personal[container.skinID].Protection;
                if (set.loot && container.OwnerID != player.userID)
                {
                    if (set.team)
                    {
                        PlayerTeam team = ServerInstance.FindPlayersTeam(container.OwnerID);
                        if (team != null && team.members.Contains(player.userID))
                            return null;
                    }
                    return false;
                }
            }
            else if (staticTypes.ContainsKey(container.skinID))
            {
                SProtection set = statics[staticTypes[container.skinID]].Protection;
                if (container.OwnerID != player.userID)
                {
                    if (set.team)
                    {
                        PlayerTeam team = ServerInstance.FindPlayersTeam(container.OwnerID);
                        if (team != null && team.members.Contains(player.userID))
                            return null;
                    }
                    player.SendConsoleCommand("chat.add", 2, config.chatid, lang.GetMessage("Chat.IsBusy", this, player.UserIDString));
                    return false;
                }
            }
            return null;
        }
        private void OnLootEntity(BasePlayer player, ResourceExtractorFuelStorage container)
        {
            if (container.skinID == 0 || !ui.ContainsKey(container.skinID)) return;
            CustomQuarries[container.parentEntity.uid.Value].looters.Add(player);
            if (CanManage())
                UI_ControlButton(player, container.skinID);
            bool CanManage()
            {
                if (staticTypes.ContainsKey(container.skinID))
                    return true;
                var settings = personal[container.skinID].Protection;
                if (settings.manage && container.OwnerID != player.userID)
                {
                    if (settings.team)
                    {
                        PlayerTeam team = ServerInstance.FindPlayersTeam(container.OwnerID);
                        if (team != null && team.members.Contains(player.userID))
                            return true;
                    }
                    return false;
                }
                return true;
            }
        }
        private void OnLootEntityEnd(BasePlayer player, ResourceExtractorFuelStorage container)
        {
            if (container.skinID == 0 || !ui.ContainsKey(container.skinID)) return;
            CloseUI(player, true);
            CustomQuarries[container.parentEntity.uid.Value].looters.Remove(player);
        }
        #endregion
        #region [Helpers]
        private void RegisterPermissions()
        {
            List<string> perms = new List<string> { adminPerm };
            perms.AddRange(config.Limit.permissions.Keys);
            perms.AddRange(config.Rates.permissions.Keys);
            foreach (StaticQuarry x in statics.Values)
                if (x.Protection.enable)
                    perms.Add(x.Protection.perm);
            for (int i = 0; i < perms.Count; i++)
                if (!permission.PermissionExists(perms[i]))
                    permission.RegisterPermission(perms[i], this);
        }
        private void UnsubscribeAll()
        {
            if (config.optimization.disableLoot)
                Unsubscribe(nameof(CanLootEntity));
            if (config.optimization.disableRepair)
                Unsubscribe(nameof(OnStructureRepair));
            if (config.optimization.disableLimits && config.optimization.disableDistance)
                Unsubscribe(nameof(CanBuild));
            if (config.optimization.disablePermissions)
            {
                Unsubscribe(nameof(OnUserPermissionGranted));
                Unsubscribe(nameof(OnUserPermissionRevoked));
                Unsubscribe(nameof(OnUserGroupAdded));
                Unsubscribe(nameof(OnUserGroupRemoved));
                Unsubscribe(nameof(OnGroupPermissionGranted));
                Unsubscribe(nameof(OnGroupPermissionRevoked));
            }
        }
        private void CreateProtectionProperties(float value)
        {
            if (protectionProperties.ContainsKey(value)) return;
            var properties = ScriptableObject.CreateInstance("ProtectionProperties") as ProtectionProperties;
            float[] amounts = config.protectionAmounts.ToArray();
            if (value != 0)
            {
                for (int i = 0; i < amounts.Length; i++)
                {
                    amounts[i] += (1f - amounts[i]) * (value / 100f);
                    if (amounts[i] > 1f)
                        amounts[i] = 1f;
                }
            }
            properties.amounts = amounts;
            protectionProperties.Add(value, properties);
        }
        #endregion
        #region [Lang]
        private void LoadLangFiles()
        {
            Dictionary<string, string> LangEN = new Dictionary<string, string>
            {
                ["UI.ControlButton"] = "CONTROLS",
                ["UI.Stats"] = "Durability: {0}\nProtection: {1}%\nResource container capacity: {2}\nFuel container capacity: {3}",
                ["UI.Fuel"] = "Fuel",
                ["UI.Prod"] = "Production",
                ["UI.UpgradeCost"] = "Upgrade cost",
                ["UI.Remove"] = "REMOVE",
                ["UI.Upgrade"] = "UPGRADE",
                ["UI.NoResources"] = "NOT ENOUGH RESOURCES",
                ["UI.DoUpgrade"] = "UPGRADE",
                ["UI.FuelProd"] = "Consume fuel every {0} sec.\nResource production every {1} sec.",
                ["UI.NoRefund"] = "Are you sure you want to do this?\nYou will not receive any items back.",
                ["UI.Refund"] = "Are you sure you want to do this?\nYou will receive the following items:",
                ["UI.Accept"] = "CONFIRM",
                ["UI.Decline"] = "CANCEL",
                [$"UI.{QuarryType.None}"] = "Pump Jack",
                [$"UI.{QuarryType.Basic}"] = "Stone Quarry",
                [$"UI.{QuarryType.Sulfur}"] = "Sulfur Quarry",
                [$"UI.{QuarryType.HQM}"] = "HQM Quarry",
                ["Chat.NoSpace"] = "Your inventory is full. You need {0} empty slots.",
                ["Chat.FullInventory"] = "Your inventory is full. Anouther attempt to recieve the item will occur in {0} seconds.",
                ["Chat.TooClose"] = "You are too close to the placement position.",
                ["Chat.HasEntities"] = "Too close to other constructions.",
                ["Chat.QLimit"] = "You are already placed maximum amount ({0}) of quarries.",
                ["Chat.JLimit"] = "You are already placed maximum amount ({0}) of pumpjacks.",
                ["Chat.QPlaced"] = "You are placed {0} from {1} quarries.",
                ["Chat.JPlaced"] = "You are placed {0} from {1} pumpjacks.",
                ["Chat.QDestroyed"] = "Your quarry has been destroyed. You can place {0} more.",
                ["Chat.JDestroyed"] = "Your pumpjack has been destroyed. You can place {0} more.",
                ["Chat.NoRepair"] = "Unable to repair: Not damaged.",
                ["Chat.NoRepairDamage"] = "Unable to repair: Recently damaged. Repairable in: {0}s.",
                ["Chat.NoRepairRecources"] = "You need the following items for repair:",
                ["Chat.Upgrade"] = "You have successfully upgraded your quarry.",
                ["Chat.IsBusy"] = "This quarry is already being used by another player.",
                ["Chat.EndNotify"] = "«{0}» at {1} has completed mining. It will be unlocked in {2} min.",
                ["Chat.UnlockNotify"] = "«{0}» at {1} is unlocked.",
                ["Chat.LockNotify.Owner"] = "«{0}» is locked for you until the end of mining.",
                ["Chat.LockNotify.All"] = "«{0}» at {1} is locked by {2} until the end of mining."
            };
            Dictionary<string, string> LangRU = new Dictionary<string, string>
            {
                ["UI.ControlButton"] = "УПРАВЛЕНИЕ",
                ["UI.Stats"] = "Прочность: {0}\nЗащита: {1}%\nЯчеек в ресурсном отсеке: {2}\nЯчеек в топливном отсеке: {3}",
                ["UI.Fuel"] = "Используемое топливо",
                ["UI.Prod"] = "Производство",
                ["UI.UpgradeCost"] = "Стоимость улучшения",
                ["UI.Remove"] = "ДЕМОНТАЖ",
                ["UI.Upgrade"] = "УЛУЧШЕНИЕ",
                ["UI.NoResources"] = "НЕДОСТАТОЧНО РЕСУРСОВ",
                ["UI.DoUpgrade"] = "УЛУЧШИТЬ",
                ["UI.FuelProd"] = "Потребление топлива каждые {0} сек.\nПроизводство ресурсов каждые {1} сек.",
                ["UI.NoRefund"] = "Вы уверены что хотите сделать это?\nВы не получите назад никаких предметов.",
                ["UI.Refund"] = "Вы уверены что хотите сделать это?\nВы получите следующие предметы:",
                ["UI.Accept"] = "ПОДТВЕРДИТЬ",
                ["UI.Decline"] = "ОТМЕНА",
                [$"UI.{QuarryType.None}"] = "Нефтекачка",
                [$"UI.{QuarryType.Basic}"] = "Каменный карьер",
                [$"UI.{QuarryType.Sulfur}"] = "Серный карьер",
                [$"UI.{QuarryType.HQM}"] = "МВК карьер",
                ["Chat.NoSpace"] = "Ваш инвентарь переполнен. Требуется {0} свободных слотов.",
                ["Chat.FullInventory"] = "Ваш инвентарь переполнен. Повторная попытка выдачи предмета произойдет через {0} секунд.",
                ["Chat.TooClose"] = "Вы слишком близко к месту установки.",
                ["Chat.HasEntities"] = "Слишком близко к другим строениям.",
                ["Chat.QLimit"] = "У вас уже установлено максимальное количество ({0}) карьеров.",
                ["Chat.JLimit"] = "У вас уже установлено максимальное количество ({0}) нефтекачек.",
                ["Chat.QPlaced"] = "Вы установили {0} из {1} карьеров.",
                ["Chat.JPlaced"] = "Вы установили {0} из {1} нефтекачек.",
                ["Chat.QDestroyed"] = "Ваш карьер был уничтожен. Можете установить еще {0}",
                ["Chat.JDestroyed"] = "Ваша нефтекачка была уничтожена. Можете установить еще {0}",
                ["Chat.NoRepair"] = "Ремонт не требуется.",
                ["Chat.NoRepairDamage"] = "Объект был недавно поврежден. Ремонт доступен через: {0} сек",
                ["Chat.NoRepairRecources"] = "Для ремонта требуются следующие предметы:",
                ["Chat.Upgrade"] = "Вы успешно улучшили карьер.",
                ["Chat.IsBusy"] = "Этот карьер уже используется другим игроком.",
                ["Chat.EndNotify"] = "«{0}» в квадрате {1} завершил добычу. Он будет разблокирован через {2} мин.",
                ["Chat.UnlockNotify"] = "«{0}» в квадрате {1} разблокирован.",
                ["Chat.LockNotify.Owner"] = "«{0}» заблокирован за вами до окончания добычи.",
                ["Chat.LockNotify.All"] = "«{0}» в квадрате {1} заблокирован за {2} до окончания добычи."
            };
            foreach (PersonalQuarry quarry in personal.Values)
            {
                foreach (CustomItem item in quarry.Repair.Cost)
                {
                    string key = $"{item.shortname}_{item.skin}";
                    if (!LangEN.ContainsKey(key))
                    {
                        ItemDefinition def = ItemManager.FindItemDefinition(item.itemID);
                        LangEN.Add(key, def.displayName.english);
                        LangRU.Add(key, def.displayName.english);
                    }
                }
            }
            lang.RegisterMessages(LangEN, this);
            lang.RegisterMessages(LangRU, this, "ru");
        }
        #endregion
        #region [UI]
        private void UI_ControlButton(BasePlayer player, ulong skin)
        {
            List<CuiElement> cui = new List<CuiElement>
            {
                new CuiElement
                {
                    Parent = "Overlay", Name = "aQuarry_ControlButton", Components =
                    {
                        new CuiButtonComponent { Command = $"aQuarry_Action 1 {skin}", Color = "0.968 0.921 0.882 0.035", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "198.5 79", OffsetMax = "381 106" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_ControlButton", Name = "aQuarry_ControlButton_Image", Components =
                    {
                        new CuiImageComponent { Sprite = "assets/icons/gear.png", Color = "0.87 0.84 0.8 1"  },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "6 4", OffsetMax = "25 -4" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_ControlButton", Name = "aQuarry_ControlButton_Text", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessage("UI.ControlButton", this, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "32 0", OffsetMax = "0 0" }
                    }
                }
            };
            //string json = $"[{{\"name\":\"aQuarry_ControlButton\",\"parent\":\"Overlay\",\"components\":[{{\"type\":\"UnityEngine.UI.Button\",\"command\":\"\",\"material\":\"assets/content/ui/uibackgroundblur-ingamemenu.mat\",\"color\":\"0.968 0.921 0.882 0.035\"}},{{\"type\":\"RectTransform\",\"anchormin\":\"0.5 0\",\"anchormax\":\"0.5 0\",\"offsetmin\":\"198.5 79\",\"offsetmax\":\"381 106\"}}]}},{{\"name\":\"Image\",\"parent\":\"aQuarry_ControlButton\",\"components\":[{{\"type\":\"UnityEngine.UI.Image\",\"sprite\":\"assets/icons/gear.png\",\"color\":\"0.87 0.84 0.8 1\"}},{{\"type\":\"RectTransform\",\"anchormin\":\"0 0\",\"anchormax\":\"0 0\",\"offsetmin\":\"6 4\",\"offsetmax\":\"25 23\"}}]}},{{\"name\":\"Text\",\"parent\":\"aQuarry_ControlButton\",\"components\":[{{\"type\":\"UnityEngine.UI.Text\",\"text\":\"{lang.GetMessage("UI.ControlButton", this, player.UserIDString)}\",\"fontSize\":14,\"align\":\"MiddleLeft\",\"color\":\"0.87 0.84 0.8 1\"}},{{\"type\":\"RectTransform\",\"anchormin\":\"0 0\",\"anchormax\":\"1 1\",\"offsetmin\":\"32 0\",\"offsetmax\":\"0 0\"}}]}}]";
            //Interface.Oxide.DataFileSystem.GetFile("aQuarry_Json1").WriteObject(json);
            AddUi(player, cui);
        }
        private void UI_Start(BasePlayer player, ulong skin)
        {
            List<CuiElement> cui = new List<CuiElement>
            {
                new CuiElement
                {
                    Parent = "Overlay", Name = "aQuarry_Layer", Components =
                    {
                        new CuiImageComponent { Color = "0 0 0 0" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_Layer", Name = "aQuarry_Close", Components =
                    {
                        new CuiNeedsCursorComponent(),
                        new CuiButtonComponent { Command = "aQuarry_Action 0", Color = "0 0 0 0" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                }
            };
            AddUi(player, cui);
            UI_QuarryInfo(player, ui[skin].main, lang.GetLanguage(player.UserIDString));
        }
        private void UI_QuarryInfo(BasePlayer player, UI set, string langKey, int index = 0)
        {
            List<CuiElement> cui = new List<CuiElement>
            {
                new CuiElement
                {
                    Parent = "aQuarry_Layer", Name = $"aQuarry_{index}", Components =
                    {
                        new CuiImageComponent { Color = "0.149 0.133 0.110 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.55", AnchorMax = "0.5 0.55", OffsetMin = set.offset.min, OffsetMax = set.offset.max }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}", Name = $"aQuarry_{index}_TittleBG", Components =
                    {
                        new CuiImageComponent { Color = "0.242 0.230 0.222 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "2 -27", OffsetMax = "-2 -2" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_TittleBG", Name = $"aQuarry_{index}_Sprite", Components =
                    {
                        new CuiImageComponent { Sprite = set.sprite, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "6 4", OffsetMax = "23 -4" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_TittleBG", Name = $"aQuarry_{index}_Tittle", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage(set.langKey, this, langKey), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "30 0", OffsetMax = "0 0" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}", Name = $"aQuarry_{index}_MainBG", Components =
                    {
                        new CuiImageComponent { Color = "0.188 0.182 0.176 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2 2", OffsetMax = "-2 -27" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_Name", Components =
                    {
                        new CuiTextComponent { Text = set.isStatic ? lang.GetMessageByLanguage($"UI.{set.type}", this, langKey) : set.name, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -25", OffsetMax = "0 0" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_ImageBG", Components =
                    {
                        new CuiImageComponent { Color = "0.094 0.094 0.086 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "5 -125", OffsetMax = "105 -25" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_ImageBG", Name = $"aQuarry_{index}_Image", Components =
                    {
                        new CuiImageComponent { ItemId = set.itemID,  SkinId = set.isStatic ? 0 : set.skin },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_DescBG", Components =
                    {
                        new CuiImageComponent { Color = "0.242 0.230 0.222 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "110 -125", OffsetMax = "-5 -25" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_DescBG", Name = $"aQuarry_{index}_Desc", Components =
                    {
                        new CuiTextComponent { Text = string.Format(lang.GetMessageByLanguage("UI.Stats", this, langKey), set.stats[0], set.stats[1], set.stats[2], set.stats[3]), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "0 0" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_Fuel", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.Fuel", this, langKey), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -150", OffsetMax = "0 -125" }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_Prod", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.Prod", this, langKey), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = set.production.max, OffsetMax = set.fuel.min }
                    }
                }
            };
            if (index == 0)
            {
                bool remove = canBeRemoved.Contains(set.skin);
                bool upgrade = canBeUpgraded.Contains(set.skin);
                if (remove)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_Remove", Components =
                        {
                            new CuiButtonComponent { Command = $"aQuarry_Action 4 {set.skin}", Color = "0.688 0.215 0.149 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = !upgrade ? "1 0" : "0.5 0", OffsetMin = "5 5", OffsetMax = "-5 30" }
                        }
                    });
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_Remove", Components =
                        {
                            new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.Remove", this, langKey), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                        }
                    });
                }
                if (upgrade)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_Upgrade", Components =
                        {
                            new CuiButtonComponent { Command = $"aQuarry_Action 3 {set.skin}", Color = "0.082 0.215 0.318 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                            new CuiRectTransformComponent { AnchorMin = !remove ? "0 0" : "0.5 0", AnchorMax = "1 0", OffsetMin = "5 5", OffsetMax = "-5 30" }
                        }
                    });
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_Upgrade", Components =
                        {
                            new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.Upgrade", this, langKey), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                        }
                    });
                }
            }
            else
            {
                cui.Add(new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_UpCost", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.UpgradeCost", this, langKey), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = set.upgrade.max, OffsetMax = set.production.min }
                    }
                });
                cui.Add(new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_UpgradeBG", Components =
                    {
                        new CuiImageComponent { Color = "0.094 0.094 0.086 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = set.upgrade.min, OffsetMax = set.upgrade.max }
                    }
                });
                List<CustomItem> list = personal[set.parent].Upgrade.Cost;
                Dictionary<Item, int> remove;
                int[] has;
                bool canUpgrade = CanAffordUpgrade(player.inventory, list, out remove, out has);
                for (int i = 0; i < list.Count; i++)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_UpgradeBG", Name = $"aQuarry_{index}_Up_{i}", Components =
                        {
                            new CuiImageComponent { ItemId = list[i].itemID, SkinId = list[i].skin },
                            new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = set.upgradeList[i].min, OffsetMax = set.upgradeList[i].max },
                        }
                    });
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_Up_{i}", Components =
                        {
                            new CuiTextComponent { Text = $"x{has[i]}", Color = has[i] < list[i].amount ? "0.688 0.215 0.149 1" : "0.449 0.549 0.267 1", Align = TextAnchor.UpperLeft, FontSize = 10 },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 0", OffsetMax = "0 -1" },
                        }
                    });
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_Up_{i}", Components =
                        {
                            new CuiTextComponent { Text = $"x{list[i].amount}", Color = "0.87 0.84 0.8 1", Align = TextAnchor.LowerRight, FontSize = 10 },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 1", OffsetMax = "-1 0" },
                        }
                    });
                }
                if (canUpgrade)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_DoUpgrade", Components =
                        {
                            new CuiButtonComponent { Command = "aQuarry_Action 5", Color = "0.449 0.549 0.267 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "5 5", OffsetMax = "-5 30" }
                        }
                    });
                }
                else
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_DoUpgrade", Components =
                        {
                            new CuiImageComponent { Color = "0.688 0.215 0.149 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "5 5", OffsetMax = "-5 30" },
                        }
                    });
                }
                cui.Add(new CuiElement
                {
                    Parent = $"aQuarry_{index}_DoUpgrade", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage(canUpgrade ? "UI.DoUpgrade" : "UI.NoResources", this, langKey), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    }
                });
            }
            AddUi(player, cui);
            UI_ProductionInfo(player, set.isStatic ? statics[set.type].Production.fuelList : personal[set.skin].Production.fuelList, set, index);
        }
        private void UI_ProductionInfo(BasePlayer player, List<FuelProduction> fuelList, UI set, int index = 0, int selected = 0)
        {
            List<CuiElement> cui = new List<CuiElement>
            {
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_FuelBG", DestroyUi = $"aQuarry_{index}_FuelBG", Components =
                    {
                        new CuiImageComponent { Color = "0.094 0.094 0.086 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = set.fuel.min, OffsetMax = set.fuel.max }
                    }
                },
                new CuiElement
                {
                    Parent = $"aQuarry_{index}_MainBG", Name = $"aQuarry_{index}_ProdBG", DestroyUi = $"aQuarry_{index}_ProdBG", Components =
                    {
                        new CuiImageComponent { Color = "0.094 0.094 0.086 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = set.production.min, OffsetMax = set.production.max }
                    }
                }
            };
            for (int i = 0; i < fuelList.Count; i++)
            {
                if (selected == i)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_FuelBG", Name = $"aQuarry_{index}_Fuel_{i}BG", Components =
                        {
                            new CuiImageComponent { Color = "0.449 0.549 0.267 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                            new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = set.fuelList[i].min, OffsetMax = set.fuelList[i].max }
                        }
                    });
                }
                cui.Add(new CuiElement
                {
                    Parent = $"aQuarry_{index}_FuelBG", Name = $"aQuarry_{index}_Fuel_{i}", Components =
                    {
                        new CuiImageComponent { ItemId = fuelList[i].itemID, SkinId = fuelList[i].skin },
                        new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = set.fuelList[i].min, OffsetMax = set.fuelList[i].max },
                    }
                });
                cui.Add(new CuiElement
                {
                    Parent = $"aQuarry_{index}_Fuel_{i}", Components =
                    {
                        new CuiTextComponent { Text = $"x{fuelList[i].amount}", Color = "0.87 0.84 0.8 1", Align = TextAnchor.LowerRight, FontSize = 11 },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 1", OffsetMax = "-1 0" },
                    }
                });
                if (selected != i)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_Fuel_{i}", Name = $"aQuarry_{index}_Fuel_{i}BG", Components =
                        {
                            new CuiButtonComponent { Command = $"aQuarry_Action 2 {set.skin} {set.parent} {index} {i}", Color = "0 0 0 0" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
                        }
                    });
                }
                else
                {
                    for (int r = 0; r < fuelList[i].resources.Count; r++)
                    {
                        cui.Add(new CuiElement
                        {
                            Parent = $"aQuarry_{index}_ProdBG", Name = $"aQuarry_{index}_Prod_{r}", Components =
                            {
                                new CuiImageComponent { ItemId = fuelList[i].resources[r].itemID, SkinId = fuelList[i].resources[r].skin },
                                new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = set.productionList[i][r].min, OffsetMax = set.productionList[i][r].max },
                            }
                        });
                        cui.Add(new CuiElement
                        {
                            Parent = $"aQuarry_{index}_Prod_{r}", Components =
                            {
                                new CuiTextComponent { Text = fuelList[i].resources[r].amount == fuelList[i].resources[r].amountMax ? $"x{fuelList[i].resources[r].amount}" : string.Format(config.format, fuelList[i].resources[r].amount, fuelList[i].resources[r].amountMax), Color = "0.87 0.84 0.8 1", Align = TextAnchor.LowerRight, FontSize = 9 },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 1", OffsetMax = "-1 0" },
                            }
                        });
                        if (fuelList[i].resources[r].chance < 100f)
                        {
                            cui.Add(new CuiElement
                            {
                                Parent = $"aQuarry_{index}_Prod_{r}", Components =
                                {
                                    new CuiTextComponent { Text = $"{fuelList[i].resources[r].chance}%", Color = "0.449 0.549 0.267 1", Align = TextAnchor.UpperCenter, FontSize = 10 },
                                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 1", OffsetMax = "-1 0" },
                                }
                            });
                        }
                    }
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_{index}_ProdBG", Components =
                        {
                            new CuiTextComponent { Text = string.Format(lang.GetMessage("UI.FuelProd", this, player.UserIDString), fuelList[i].fuelTimer, fuelList[i].productionTimer), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 10 },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "5 0", OffsetMax = "0 25" },
                        }
                    });
                }
            }
            AddUi(player, cui);
        }
        private void UI_RemoveConfirm(BasePlayer player, UI set, string langKey)
        {
            List<CuiElement> cui = new List<CuiElement>
            {
                new CuiElement
                {
                    Parent = "aQuarry_Layer", Name = "aQuarry_Layer2", Components =
                    {
                        new CuiImageComponent { Color = "0 0 0 0" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_Layer2", Name = "aQuarry_Remove", Components =
                    {
                        new CuiImageComponent { Color = "0.149 0.133 0.110 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.55", AnchorMax = "0.5 0.55", OffsetMin = set.offset.min, OffsetMax = set.offset.max }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_Remove", Name = "aQuarry_TittleBG", Components =
                    {
                        new CuiImageComponent { Color = "0.242 0.230 0.222 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "2 -27", OffsetMax = "-2 -2" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_TittleBG", Name = "aQuarry_Sprite", Components =
                    {
                        new CuiImageComponent { Sprite = set.sprite, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "6 4", OffsetMax = "23 -4" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_TittleBG", Name = "aQuarry_Tittle", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage(set.langKey, this, langKey), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.87 0.84 0.8 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "30 0", OffsetMax = "0 0" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_Remove", Name = "aQuarry_MainBG", Components =
                    {
                        new CuiImageComponent { Color = "0.188 0.182 0.176 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2 2", OffsetMax = "-2 -27" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_MainBG", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage(set.fuel == null ? "UI.NoRefund" : "UI.Refund", this, langKey), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "5 -50", OffsetMax = "0 0" },
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_MainBG", Name = "aQuarry_Remove1", Components =
                    {
                        new CuiButtonComponent { Close = "aQuarry_Layer2", Color = "0.688 0.215 0.149 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.5 0", OffsetMin = "5 5", OffsetMax = "-5 30" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_Remove1", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.Decline", this, langKey), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_MainBG", Name = "aQuarry_Remove2", Components =
                    {
                        new CuiButtonComponent { Close = "aQuarry_Layer", Command = $"aQuarry_Action 6 {set.skin}", Color = "0.449 0.549 0.267 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "1 0", OffsetMin = "5 5", OffsetMax = "-5 30" }
                    }
                },
                new CuiElement
                {
                    Parent = "aQuarry_Remove2", Components =
                    {
                        new CuiTextComponent { Text = lang.GetMessageByLanguage("UI.Accept", this, langKey), Color = "0.87 0.84 0.8 1", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    }
                }
            };
            if (set.fuel != null)
            {
                cui.Add(new CuiElement
                {
                    Parent = "aQuarry_MainBG", Name = "aQuarry_ItemsBG", Components =
                    {
                        new CuiImageComponent { Color = "0.094 0.094 0.086 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = set.fuel.min, OffsetMax = set.fuel.max },
                    }
                });
                List<NamedItem> list = personal[set.skin].Remove.itemList;
                for (int i = 0; i < list.Count; i++)
                {
                    cui.Add(new CuiElement
                    {
                        Parent = "aQuarry_ItemsBG", Name = $"aQuarry_Item_{i}", Components =
                        {
                            new CuiImageComponent { ItemId = list[i].itemID, SkinId = list[i].skin },
                            new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = set.fuelList[i].min, OffsetMax = set.fuelList[i].max },
                        }
                    });
                    cui.Add(new CuiElement
                    {
                        Parent = $"aQuarry_Item_{i}", Components =
                        {
                            new CuiTextComponent { Text = $"x{list[i].amount}", Color = "0.87 0.84 0.8 1", Align = TextAnchor.LowerRight, FontSize = 11 },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 1", OffsetMax = "-1 0" },
                        }
                    });
                }
            }
            AddUi(player, cui);
        }
        private void CloseUI(BasePlayer player, bool all = false)
        {
            DestroyUi(player, "aQuarry_Layer");
            upradeUIUsers.Remove(player.userID);
            if (all)
                DestroyUi(player, "aQuarry_ControlButton");
        }
        #endregion
        #region [Commands]
        [ConsoleCommand("aQuarry_Action")]
        private void CMD_InterfaceActions(Arg arg)
        {
            BasePlayer player = arg.Player();
            int mode = int.Parse(arg.Args[0].ToString());
            switch (mode)
            {
                case 0:
                    CloseUI(player);
                    return;
                case 1:
                    UI_Start(player, ulong.Parse(arg.Args[1].ToString()));
                    return;
                case 2:
                    ulong skin = ulong.Parse(arg.Args[1].ToString());
                    ulong parent = ulong.Parse(arg.Args[2].ToString());
                    int index = int.Parse(arg.Args[3].ToString());
                    var set = ui[parent];
                    UI_ProductionInfo(player, set.main.isStatic ? statics[set.main.type].Production.fuelList : personal[skin].Production.fuelList, index == 0 ? set.main : set.upgrade, index, int.Parse(arg.Args[4].ToString()));
                    return;
                case 3:
                    if (!upradeUIUsers.Contains(player.userID))
                    {
                        upradeUIUsers.Add(player.userID);
                        UI_QuarryInfo(player, ui[ulong.Parse(arg.Args[1].ToString())].upgrade, lang.GetLanguage(player.UserIDString), 1);
                    }
                    return;
                case 4:
                    UI_RemoveConfirm(player, ui[ulong.Parse(arg.Args[1].ToString())].remove, lang.GetLanguage(player.UserIDString));
                    return;
                case 5:
                    Dictionary<Item, int> remove;
                    int[] has;
                    if (!CanAffordUpgrade(player.inventory, personal[player.inventory.loot.containers[0].entityOwner.skinID].Upgrade.Cost, out remove, out has)) return;
                    if (remove.Count > 0) PayForUpgrade(remove);
                    CustomQuarries[player.inventory.loot.containers[0].entityOwner.parentEntity.uid.Value].Upgrade();
                    player.SendConsoleCommand("chat.add", 2, config.chatid, lang.GetMessage("Chat.Upgrade", this, player.UserIDString));
                    return;
                case 6:
                    ulong skin2 = ulong.Parse(arg.Args[1].ToString());
                    if (personal[skin2].Remove.refund)
                    {
                        List<NamedItem> itemList = personal[skin2].Remove.itemList;
                        if (player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count + itemList.Count > 30)
                        {
                            player.EndLooting();
                            player.SendConsoleCommand("chat.add", 2, config.chatid, lang.GetMessage("Chat.NoSpace", this, player.UserIDString));
                            return;
                        }
                        for (int i = 0; i < itemList.Count; i++)
                        {
                            Item item = ItemManager.CreateByItemID(itemList[i].itemID, itemList[i].amount, itemList[i].skin);
                            if (itemList[i].name.Length > 0) item.name = itemList[i].name;
                            if (!item.MoveToContainer(player.inventory.containerMain) && !item.MoveToContainer(player.inventory.containerBelt))
                                item.Drop(player.inventory.containerMain.dropPosition, player.inventory.containerMain.dropVelocity);
                        }
                    }
                    ulong netID = player.inventory.loot.containers[0].entityOwner.parentEntity.uid.Value;
                    if (!CustomQuarries.ContainsKey(netID)) return;
                    CustomQuarries[netID].EndLoot();
                    CustomQuarries[netID].quarry.Kill();
                    return;
            }
        }
        [ConsoleCommand("quarry.give")]
        private void CMD_GiveCustomQuarry(Arg arg)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, adminPerm)) return;
            ulong userid;
            ulong skin;
            if (arg.Args == null || arg.Args.Length < 2 || !ulong.TryParse(arg.Args[0].ToString(), out userid) || !ulong.TryParse(arg.Args[1].ToString(), out skin))
            {
                PrintError(Rus ? "Неверный формат. Формат:\nquarry.give «SteamID» «SkinID»" : "Invalid format. Format:\nquarry.give «SteamID» «SkinID»");
                return;
            }
            if (!personal.ContainsKey(skin))
            {
                PrintError(Rus ? "Конфигурация не содержит карьера с указанным «SkinID»" : "The configuration does not contain a quarry with this «SkinID»");
                return;
            }
            BasePlayer player = BasePlayer.FindByID(userid);
            if (player == null)
            {
                PrintError(Rus ? "Игрок с указанным «SteamID» не найден" : "Player with intered «SteamID» not found");
                return;
            }
            GiveQuarryToPlayer(player, skin);
        }
        [ConsoleCommand("quarry.giveme")]
        private void CMD_GiveCustomQuarrySelf(Arg arg)
        {
            if (arg.Player() == null || !permission.UserHasPermission(arg.Player().UserIDString, adminPerm)) return;
            foreach (ulong x in personal.Keys)
                GiveQuarryToPlayer(arg.Player(), x);
        }
        #endregion
    }
}
