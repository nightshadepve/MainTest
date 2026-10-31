using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Globalization;

/* Ideas
 * Make permission based mods for finding planters
 * Permission tiers requiring certain permissions to use planters.
 */

/* 1.0.12
 * Fixed seeds freezing in planters.
 * Maximum loot quantity can now drop.
 * "Prevent aging on ripe" option now works.
 * Give command fixes.
 */

namespace Oxide.Plugins
{
    [Info("Hardy Planters", "imthenewguy", "1.0.12")]
    [Description("Planterboxes that require no light or water to grow")]
    class HardyPlanters : RustPlugin
    {
        #region Config       

        private Configuration config;
        public class Configuration
        {
            [JsonProperty("Planterbox profiles")]
            public Dictionary<ulong, PlanterboxInfo> planterProfiles = new Dictionary<ulong, PlanterboxInfo>();

            [JsonProperty("Loot table")]
            public Dictionary<string, LootInfo> LootTable = new Dictionary<string, LootInfo>();

            [JsonProperty("Allow hardy planterboxes to be reskinned [repair bench and SkinBox]")]
            public bool allow_reskin = false;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        public class LootInfo
        {
            [JsonProperty("Drop chance [%]")]
            public float chance;

            [JsonProperty("Minimum quantity")]
            public int min;

            [JsonProperty("Maximum quantity")]
            public int max;

            public LootInfo(float chance, int min, int max)
            {
                this.chance = chance;
                this.min = min;
                this.max = max;
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            config.planterProfiles = DefaultPlanters;
            config.LootTable = DefaultLootTable;
        }

        Dictionary<string, LootInfo> DefaultLootTable
        {
            get
            {
                return new Dictionary<string, LootInfo>()
                {
                    ["crate_elite"] = new LootInfo(15f, 1, 1),
                    ["crate_normal"] = new LootInfo(5f, 1, 1),
                    ["crate_normal_2"] = new LootInfo(3f, 1, 1),
                    ["crate_underwater_advanced"] = new LootInfo(10f, 1, 1),
                    ["crate_underwater_basic"] = new LootInfo(7f, 1, 1),
                };
            }
        }

        Dictionary<ulong, PlanterboxInfo> DefaultPlanters
        {
            get
            {
                return new Dictionary<ulong, PlanterboxInfo>()
                {
                    [3021062193] = new PlanterboxInfo("advanced large planter box t1", 0.8f, "planter.large", 100),
                    [3021062261] = new PlanterboxInfo("advanced large planter box t2", 1, "planter.large", 50),
                    [3021062317] = new PlanterboxInfo("advanced large planter box t3", 1.5f, "planter.large", 20),
                    [3021062393] = new PlanterboxInfo("advanced small planter box t1", 0.8f, "planter.small", 100),
                    [3021062473] = new PlanterboxInfo("advanced small planter box t2", 1, "planter.small", 50),
                    [3021062566] = new PlanterboxInfo("advanced small planter box t3", 1.5f, "planter.small", 20)
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                // PrintToConsole only reaches connected clients F1 console, so a bad config
                // used to fall back to defaults with no trace in the server log at all.
                PrintError($"Configuration file {Name}.json is invalid ({ex.Message}). Reverting to DEFAULT settings - your config has NOT been applied. A copy of the invalid file has been saved as {Name}.json.invalid");
                BackupInvalidConfig();
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        void BackupInvalidConfig()
        {
            try
            {
                var path = Config.Filename;
                if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".invalid", true);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to back up the invalid configuration file: {ex.Message}");
            }
        }

        protected override void SaveConfig()
        {
            PrintToConsole($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        public static HardyPlanters Instance;
        void Init()
        {
            permission.RegisterPermission(perm_use, this);
            permission.RegisterPermission(perm_drop, this);
            permission.RegisterPermission(perm_admin, this);
            Instance = this;

            if (config.allow_reskin)
            {
                Unsubscribe(nameof(OnItemSkinChange));
                Unsubscribe(nameof(SB_CanReskinItem));
            }
        }

        void Unload()
        {
            foreach (var planterBox in PlanterBehaviour)
            {
                try
                {
                    if (planterBox.Value == null) continue;
                    GameObject.Destroy(planterBox.Value);
                }
                catch
                {
                    Puts($"Failed to destroy planter behav for {planterBox}");
                }
            }
        }

        #endregion;

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ReceivedPlanter"] = "You received 1x {0}",
                ["NoPerms"] = "You do not have permission to perform this action.",
                ["CannotReskin"] = "You cannot reskin this planter box."
            }, this);
        }

        #endregion

        #region Hooks

        const string perm_drop = "hardyplanters.drop";
        const string perm_use = "hardyplanters.use";
        const string perm_admin = "hardyplanters.admin";

        List<PlanterBox> Planters = new List<PlanterBox>();
        void OnServerInitialized(bool initial)
        {
            var found = 0;
            foreach (var planter in BaseNetworkable.serverEntities.OfType<PlanterBox>())
            {
                if (!config.planterProfiles.ContainsKey(planter.skinID)) continue;
                if (SetupPlanterBox(planter)) found++;
            }

            bool foundNewContent = false;
            if (config.LootTable.IsNullOrEmpty())
            {
                config.LootTable = DefaultLootTable;
                foundNewContent = true;
            }

            if (foundNewContent) SaveConfig();

            Puts($"Found and setup: {found} planter boxes.");
        }

        void OnUserPermissionGranted(string id, string permName)
        {
            if (permName != perm_use) return;
            HandlePermissionAssignment(Convert.ToUInt64(id));
        }


        void OnGroupPermissionGranted(string name, string perm)
        {
            if (!perm.Contains("hardyplanters.")) return;
            foreach (var user in permission.GetUsersInGroup(name))
            {
                if (!ulong.TryParse(user.Split(' ')[0], out var id)) continue;
                HandlePermissionAssignment(id);
            }
        }

        void HandlePermissionAssignment(ulong id)
        {
            foreach (var planter in Planters)
            {
                if (planter.OwnerID != id || IsPlanterSetup(planter)) continue;
                SetupPlanterBox(planter);
            }
        }

        HashSet<ulong> LootedContainers = new HashSet<ulong>();
        void OnLootEntity(BasePlayer player, LootContainer container)
        {
            if (container?.net == null || !permission.UserHasPermission(player.UserIDString, perm_drop)) return;

            LootInfo lootData;
            if (!config.LootTable.TryGetValue(container.ShortPrefabName, out lootData)) return;

            if (!LootedContainers.Add(container.net.ID.Value)) return;

            if (Interface.Oxide.CallHook("OnAddLootToContainer", container) != null) return;

            var roll = UnityEngine.Random.Range(0f, 100f);
            if (roll < 100 - lootData.chance) return;

            var totalWeight = config.planterProfiles.Sum(x => x.Value.dropWeight);
            if (totalWeight <= 0) return;

            var weightRoll = UnityEngine.Random.Range(1, totalWeight + 1);
            var count = 0;

            foreach (var entry in config.planterProfiles)
            {
                count += entry.Value.dropWeight;
                if (count >= weightRoll)
                {
                    var item = CreatePlanterBox(entry.Value.shortname, UnityEngine.Random.Range(lootData.min, lootData.max + 1), entry.Key, entry.Value.displayName);
                    if (item == null) return;
                    container.inventorySlots++;
                    container.inventory.capacity++;
                    if (!item.MoveToContainer(container.inventory)) item.DropAndTossUpwards(container.transform.position, 1f);
                    return;
                }
            }
        }

        object OnItemSkinChange(int num, Item item, RepairBench bench, BasePlayer player)
        {
            if (item == null || !config.planterProfiles.ContainsKey(item.skin)) return null;
            if (player != null) PrintToChat(player, lang.GetMessage("CannotReskin", this, player.UserIDString));
            return true;
        }

        string SB_CanReskinItem(BasePlayer player, Item item, ulong newSkinID)
        {
            if (item == null || !config.planterProfiles.ContainsKey(item.skin)) return null;
            return lang.GetMessage("CannotReskin", this, player?.UserIDString);
        }

        void OnEntityKill(PlanterBox entity)
        {
            Planters.Remove(entity);
        }

        void OnEntityKill(LootContainer entity)
        {
            if (entity?.net != null) LootedContainers.Remove(entity.net.ID.Value);
        }

        private void OnEntityPickedUp(BaseCombatEntity entity, Item item, BasePlayer player)
        {
            if (!Planters.Contains(entity) || !config.planterProfiles.TryGetValue(entity.skinID, out var planterData)) return;

            item.name = planterData.displayName;
            item.MarkDirty();
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (player == null) return null;

            var item = planner?.GetItem();
            if (item == null || !config.planterProfiles.ContainsKey(item.skin)) return null;
            if (!permission.UserHasPermission(player.UserIDString, perm_use))
            {
                PrintToChat(player, lang.GetMessage("NoPerms", this, player.UserIDString));
                return true;
            }
            return null;
        }

        void OnEntityBuilt(Planner plan, GameObject go)
        {
            var entity = go?.ToBaseEntity();
            if (entity == null) return;
            var player = plan?.GetOwnerPlayer();
            if (player == null) return;

            if (entity is PlanterBox && config.planterProfiles.ContainsKey(entity.skinID))
            {
                entity.OwnerID = player.userID;
                SetupPlanterBox(entity as PlanterBox);
                return;
            }

            GrowableEntity plant = go.GetComponent<GrowableEntity>();
            if (plant == null) return;

            NextTick(() =>
            {
                var parent = plant.planter;
                if (parent == null) return;

                PlanterboxInfo data;
                if (!config.planterProfiles.TryGetValue(parent.skinID, out data)) return;
                if (!IsPlanterSetup(parent)) return;

                plant.CancelInvoke(plant.RunUpdate);
                plant.LightQuality = data.overallQuality;
                plant.WaterQuality = data.overallQuality;
                plant.GroundQuality = data.overallQuality;
                plant.OverallQuality = data.overallQuality;
                plant.TemperatureQuality = data.overallQuality;

                plant.SendNetworkUpdate();
            });            
        }


        #endregion

        #region Behaviour

        bool IsPlanterSetup(PlanterBox planterBox)
        {
            return planterBox.GetComponent<GrowableBehaviour>() != null;
        }
        bool SetupPlanterBox(PlanterBox planterBox)
        {
            PlanterboxInfo data;
            if (!config.planterProfiles.TryGetValue(planterBox.skinID, out data)) return false;
            if (!Planters.Contains(planterBox)) Planters.Add(planterBox);
            if (planterBox.OwnerID != 0 && !permission.UserHasPermission(planterBox.OwnerID.ToString(), perm_use)) return false;

            var behav = planterBox.GetComponent<GrowableBehaviour>();
            if (behav == null)
            {
                behav = planterBox.gameObject.AddComponent<GrowableBehaviour>();
                PlanterBehaviour[planterBox.net.ID.Value] = behav;
                behav.networkID = planterBox.net.ID.Value;
                behav.instance = this;
                behav.planterbox = planterBox;
                behav.baseValue = data.overallQuality;
                behav.growth_gene_multiplier = data.growth_gene_modifier;
                behav.destroy_aging_on_ripe = data.remove_aging_on_ripe;
                behav.owner = planterBox.OwnerID.ToString();

                planterBox.soilSaturation = planterBox.idealSaturation;
                planterBox.SendNetworkUpdate();

                foreach (var child in planterBox.children)
                {
                    var growable = child as GrowableEntity;
                    if (growable == null) continue;
                    growable.CancelInvoke(growable.RunUpdate);
                }
            }
            return true;
        }

        public class PlanterboxInfo
        {
            [JsonProperty("Drop weight")]
            public int dropWeight = 100;

            [JsonProperty("Overall quality of the planterbox [responsible for growth speed]")]
            public float overallQuality = 1;

            [JsonProperty("Item display name")]
            public string displayName;

            [JsonProperty("Item shortname")]
            public string shortname = "planter.large";

            [JsonProperty("Growth gene multiplier [default: 0.25]")]
            public float growth_gene_modifier = 0.25f;

            [JsonProperty("Prevent aging on ripe?")]
            public bool remove_aging_on_ripe = true;

            public PlanterboxInfo(string displayName, float overallQuality = 1, string shortname = "planter.large", int dropWeight = 100, float growth_gene_modifier = 0.25f, bool remove_on_mature = true)
            {
                this.overallQuality = overallQuality;
                this.shortname = shortname;
                this.displayName = displayName;
                this.dropWeight = dropWeight;
                this.growth_gene_modifier = growth_gene_modifier;
                this.remove_aging_on_ripe = remove_on_mature;
            }
        }

        Hash<ulong, GrowableBehaviour> PlanterBehaviour = new Hash<ulong, GrowableBehaviour>();

        public class GrowableBehaviour : FacepunchBehaviour
        {
            public ulong networkID;
            public HardyPlanters instance;
            private float nextUpdate;
            private float nextPermCheck;
            public PlanterBox planterbox;
            public string owner;
            public static float growDeltaTime => ConVar.Server.planttick * ConVar.Server.planttickscale;
            public bool haveSetQualities = false;
            List<GrowableEntity> children = Pool.Get<List<GrowableEntity>>();
            public bool destroy_aging_on_ripe = false;

            public float baseValue;
            public float growth_gene_multiplier;

            public void UpdateAge(GrowableEntity growable)
            {
                if (growable.State == PlantProperties.State.Ripe && destroy_aging_on_ripe) return;

                growable.Age += growDeltaTime;
                float num = Math.Max(baseValue, GetGrowthBonus(growable, baseValue));
                float num2 = growDeltaTime * num;
                //Interface.Oxide.LogInfo($"num: {Math.Max(baseValue, GetGrowthBonus(growable, baseValue))}. num2: {num2}. growDeltaTime: 51800");
                growable.stageAge += num2;

                growable.LightQuality = baseValue;
                growable.WaterQuality = baseValue;
                growable.GroundQuality = baseValue;
                growable.OverallQuality = baseValue;
                growable.TemperatureQuality = baseValue;

                growable.UpdateHealthAndYield(1f, num2);
                growable.UpdateState();
                growable.SendNetworkUpdate();
            }

            private float GetGrowthBonus(GrowableEntity growable, float overallQuality)
            {
                float result = overallQuality + (float)growable.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.GrowthSpeed) * growth_gene_multiplier;
                if (overallQuality <= 0f)
                {
                    result = overallQuality;
                }

                if (growable.State == PlantProperties.State.Ripe) result = Math.Min(1, result);

                return result;
            }

            private void Awake()
            {
                nextUpdate = Time.time + ConVar.Server.planttick;
                nextPermCheck = Time.time + 60;
            }

            public void FixedUpdate()
            {
                try
                {
                    if (!haveSetQualities)
                    {
                        foreach (var child in planterbox.children)
                        {
                            var growable = child as GrowableEntity;
                            if (growable == null) continue;

                            growable.LightQuality = baseValue;
                            growable.WaterQuality = baseValue;
                            growable.GroundQuality = baseValue;
                            growable.OverallQuality = baseValue;
                            growable.TemperatureQuality = baseValue;
                        }
                        haveSetQualities = true;
                    }
                    if (Time.time >= nextUpdate)
                    {
                        if (planterbox == null || planterbox.IsDead())
                        {
                            Destroy(this);
                            return;
                        }
                        children.Clear();
                        foreach (var child in planterbox.children)
                        {
                            if (child == null) continue;
                            var growable = child as GrowableEntity;
                            if (growable == null) continue;
                            if (growable.State != PlantProperties.State.Ripe || !destroy_aging_on_ripe) children.Add(growable);
                        }
                        foreach (var growable in children)
                        {
                            if (growable == null) continue;
                            if (growable.IsDead())
                            {
                                continue;
                            }

                            //growable.CancelInvoke(growable.RunUpdate); -- Not sure if I should comment this out.

                            UpdateAge(growable);
                        }
                        nextUpdate = Time.time + ConVar.Server.planttick;
                    }
                    if (Time.time >= nextPermCheck)
                    {
                        nextPermCheck = Time.time + 60;
                        if (planterbox.OwnerID != 0 && !Instance.permission.UserHasPermission(owner, perm_use))
                        {
                            Destroy(this);
                            return;
                        }
                    }
                }
                catch
                {
                }
            }

            private void RestoreInvoke()
            {
                if (planterbox == null) return;
                var thinkTimeDelta = ConVar.Server.planttick;
                foreach (var child in planterbox.children)
                {
                    var growable = child as GrowableEntity;
                    if (growable != null && growable.IsAlive() && !growable.IsInvoking(growable.RunUpdate)) growable.InvokeRandomized(growable.RunUpdate, thinkTimeDelta, thinkTimeDelta, thinkTimeDelta * 0.1f);
                }
            }

            private void RestoreInvoke(GrowableEntity growable)
            {
                if (growable == null || growable.IsDead()) return;
                var thinkTimeDelta = ConVar.Server.planttick;
                if (!growable.IsInvoking(growable.RunUpdate)) growable.InvokeRandomized(growable.RunUpdate, thinkTimeDelta, thinkTimeDelta, thinkTimeDelta * 0.1f);
            }

            private void OnDestroy()
            {
                instance.PlanterBehaviour.Remove(networkID);
                RestoreInvoke();
                Pool.FreeUnmanaged(ref children);
                enabled = false;
                CancelInvoke();
            }
        }

        #endregion

        #region Commands

        Item CreatePlanterBox(string shortname, int amount, ulong skin, string displayName)
        {
            var item = ItemManager.CreateByName(shortname, amount, skin);
            if (item == null) return null;
            item.name = displayName;
            return item;
        }

        [ChatCommand("giveplanters")]
        void GivePlanterbox(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_admin)) return;
            foreach (var planter in config.planterProfiles)
            {
                var item = CreatePlanterBox(planter.Value.shortname, 1, planter.Key, planter.Value.displayName);
                if (item != null) player.GiveItem(item);
            }
        }

        // target skin
        [ConsoleCommand("givehardyplanter")]
        void GiveHardyPlanter(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith($"Usage: givehardyplanter <target ID/name> <planter skin ID/display name> [optional quantity]");
                return;
            }

            var target = FindPlayer(arg.GetString(0), arg);
            if (target == null) return;

            var last = arg.GetString(arg.Args.Length - 1);
            var configTerm = !last.IsNumeric() ? string.Join(" ", arg.Args.Skip(1)) : string.Join(" ", arg.Args.SkipLast(1).Skip(1));

            int quantity = 1;
            if (arg.Args.Length > 2 && last.IsNumeric() && int.TryParse(last, out var parsedQuantity)) quantity = Math.Max(1, parsedQuantity);

            PlanterboxInfo pi;
            ulong.TryParse(configTerm, out var skin);
            if (!config.planterProfiles.TryGetValue(skin, out pi))
            {
                bool foundMatch = false;
                foreach (var kvp in config.planterProfiles)
                {
                    if (kvp.Value.displayName != null && kvp.Value.displayName.Contains(configTerm))
                    {
                        if (!foundMatch)
                        {
                            foundMatch = true;
                            pi = kvp.Value;
                            skin = kvp.Key;
                        }
                        else
                        {
                            arg.ReplyWith($"Found multiple profiles that matched: {configTerm}");
                            return;
                        }
                    }
                }
            }
            if (pi == null)
            {
                string validProfiles = "Valid profiles:\n";
                foreach (var kvp in config.planterProfiles)
                    validProfiles += string.Format("{0}: {1}\n", kvp.Key, kvp.Value.displayName);
                arg.ReplyWith($"Failed to find match for {configTerm}. {validProfiles}");
                return;
            }

            var item = CreatePlanterBox(pi.shortname, quantity, skin, pi.displayName);
            if (item == null)
            {
                arg.ReplyWith($"Invalid item shortname: {pi.shortname}");
                return;
            }

            var itemName = pi.displayName ?? item.info.displayName.english;
            target.GiveItem(item);
            PrintToChat(target, string.Format(lang.GetMessage("ReceivedPlanter", this, target.UserIDString), itemName));
            arg.ReplyWith($"Gave {target.displayName} {quantity}x {itemName}");
        }

        BasePlayer FindPlayer(string searchTerm, ConsoleSystem.Arg arg)
        {
            var player = BasePlayer.FindAwakeOrSleeping(searchTerm);
            if (player != null) return player;
            List<BasePlayer> foundPlayers = Pool.Get<List<BasePlayer>>();
            foreach (var p in BasePlayer.allPlayerList)
            {
                if (p.displayName.Contains(searchTerm))
                    foundPlayers.Add(p);
            }

            switch (foundPlayers.Count)
            {
                case 0:
                    arg.ReplyWith($"Found no players that matched {searchTerm}.");
                    break;
                case 1:
                    player = foundPlayers[0];
                    break;
                default:
                    arg.ReplyWith($"Found multiple players that matched {searchTerm}.\n{string.Join(", ", foundPlayers.Select(x => x.displayName))}");
                    break;
            }

            Pool.FreeUnmanaged(ref foundPlayers);
            return player;
        }

        #endregion
    }
}
 