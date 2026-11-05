using Facepunch;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using UnityEngine;
using System;

namespace Oxide.Plugins
{
    [Info("Restore Upon Death", "k1lly0u", "0.6.5")]
    [Description("Restores player inventories on death and removes the items from their corpse")]
    class RestoreUponDeath : RustPlugin
    {
        #region Fields

        private const int SmallBackpackItemId = 2068884361;
        private const int LargeBackpackItemId = -907422733;
        private const int KriegBackpackItemId = -874650016;

        private RestoreData restoreData;
        private DynamicConfigFile restorationData;

        private bool wipeData = false;

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            LoadData();

            if (Configuration.DropActiveItem)
                Unsubscribe(nameof(CanDropActiveItem));

            foreach (string perm in Configuration.Permissions.Keys)
                permission.RegisterPermission(!perm.StartsWith("restoreupondeath.") ? $"restoreupondeath.{perm}" : perm, this);

            EnableBackpackDropOnDeath(SmallBackpackItemId, Configuration.DropBackpack);
            EnableBackpackDropOnDeath(LargeBackpackItemId, Configuration.DropBackpack);
            EnableBackpackDropOnDeath(KriegBackpackItemId, Configuration.DropBackpack);
        }

        private void Unload()
        {
            EnableBackpackDropOnDeath(SmallBackpackItemId, false);
            EnableBackpackDropOnDeath(LargeBackpackItemId, false);
            EnableBackpackDropOnDeath(KriegBackpackItemId, false);

            OnServerSave();
        }

        private void OnNewSave(string fileName)
        {
            if (Configuration.WipeOnNewSave)
                wipeData = true;
        }

        private void OnServerSave() => SaveData();

        private object CanDropActiveItem(BasePlayer player) => ShouldBlockRestore(player) ? null : (object)(!HasAnyPermission(player.UserIDString));

        private void OnEntityDeath(BasePlayer player, HitInfo info)
        {
            if (!player || !player.userID.IsSteamId() || restoreData.HasRestoreData(player.userID))
                return;

            if (ShouldBlockRestore(player))
                return;

            if (Configuration.NoSuicideRestore && info != null && info.damageTypes.GetMajorityDamageType() == Rust.DamageType.Suicide)
                return;

            if (HasAnyPermission(player.UserIDString, out ConfigData.LossAmounts lossAmounts))
            {
                double expires = CurrentTime() + (Configuration.PurgeDays * 24 * 60 * 60);
                restoreData.AddData(player, lossAmounts, expires);
            }
        }

        // Handle player death while sleeping in a safe zone.
        private void OnEntityKill(BasePlayer player)
        {
            OnEntityDeath(player, null);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (!player || !restoreData.HasRestoreData(player.userID))
                return;

            if (ShouldBlockRestore(player))
                return;

            TryRestorePlayer(player);
        }

        #endregion

        #region Functions

        private bool ShouldBlockRestore(BasePlayer player) => Interface.Call("isEventPlayer", player) != null || Interface.Call("OnRestoreUponDeath", player) != null;

        private void TryRestorePlayer(BasePlayer player)
        {
            if (!player || player.IsDead())
                return;

            if (!player.IsSleeping())
            {
                if (!Configuration.DefaultItems)
                {
                    StripContainer(player.inventory.containerWear);
                    StripContainer(player.inventory.containerBelt);
                }

                restoreData.RestorePlayer(player);
            }
            else player.Invoke(() => TryRestorePlayer(player), 0.25f);
        }

        private bool HasAnyPermission(string playerId)
        {
            foreach (string key in Configuration.Permissions.Keys)
            {
                if (permission.UserHasPermission(playerId, key))
                    return true;
            }

            return false;
        }

        private bool HasAnyPermission(string playerId, out ConfigData.LossAmounts lossAmounts)
        {
            foreach (KeyValuePair<string, ConfigData.LossAmounts> kvp in Configuration.Permissions)
            {
                if (permission.UserHasPermission(playerId, kvp.Key))
                {
                    lossAmounts = kvp.Value;
                    return true;
                }
            }

            lossAmounts = null;
            return false;
        }

        private void StripContainer(ItemContainer container)
        {
            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item item = container.itemList[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }

        private void EnableBackpackDropOnDeath(int itemId, bool enabled)
        {
            ItemModBackpack itemModBackpack = ItemManager.FindItemDefinition(itemId)?.GetComponent<ItemModBackpack>();
            if (!itemModBackpack)
                return;

            itemModBackpack.DropWhenDowned = enabled;
        }

        #endregion

        #region Config

        private ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty("Give default items upon respawn if the players is having items restored")]
            public bool DefaultItems { get; set; }

            [JsonProperty("Can drop active item on death")]
            public bool DropActiveItem { get; set; }

            [JsonProperty("Can drop backpack on death")]
            public bool DropBackpack { get; set; } = true;

            [JsonProperty("Don't restore items if player commited suicide")]
            public bool NoSuicideRestore { get; set; }

            [JsonProperty("Wipe stored data when the map wipes")]
            public bool WipeOnNewSave { get; set; }

            [JsonProperty("Purge expired user data after X amount of days")]
            public int PurgeDays { get; set; } = 7;

            [JsonProperty("Percentage of total items lost (Permission Name | Percentage (0 - 100))")]
            public Dictionary<string, LossAmounts> Permissions { get; set; }

            public class LossAmounts
            {
                public int Belt { get; set; }
                public int Wear { get; set; }
                public int Main { get; set; }
            }

            public VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            if (Configuration.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                DefaultItems = false,
                DropActiveItem = false,
                DropBackpack = true,
                Permissions = new Dictionary<string, ConfigData.LossAmounts>
                {
                    ["restoreupondeath.default"] = new ConfigData.LossAmounts()
                    {
                        Belt = 75,
                        Main = 75,
                        Wear = 75
                    },
                    ["restoreupondeath.beltonly"] = new ConfigData.LossAmounts()
                    {
                        Belt = 100,
                        Main = 0,
                        Wear = 0
                    },
                    ["restoreupondeath.admin"] = new ConfigData.LossAmounts()
                    {
                        Belt = 0,
                        Main = 0,
                        Wear = 0
                    }
                },
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();

            if (Configuration.Version < new VersionNumber(0, 3, 0))
                Configuration.Permissions = baseConfig.Permissions;

            Configuration.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Data Management
        
        private double CurrentTime() => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;

        public enum ContainerType
        {
            Main,
            Wear,
            Belt
        }

        private void SaveData()
        {
            restorationData.WriteObject(restoreData);
        }

        private void LoadData()
        {
            restorationData = Interface.Oxide.DataFileSystem.GetFile("restoreupondeath_data");

            restoreData = restorationData.ReadObject<RestoreData>();

            if (restoreData?.restoreData == null || wipeData)
                restoreData = new RestoreData();
            
            double currentTime = CurrentTime();
            List<ulong> list = Pool.Get<List<ulong>>();
            
            foreach (KeyValuePair<ulong, RestoreData.PlayerData> kvp in restoreData.restoreData)
            {
                if (kvp.Value.HasExpired(currentTime))
                    list.Add(kvp.Key);
            }
            
            foreach (ulong id in list)
                restoreData.RemoveData(id);
            
            Debug.Log($"[RestoreUponDeath] Purged {list.Count} expired user data entries");
            
            Pool.FreeUnmanaged(ref list);
        }

        private class RestoreData
        {
            public Hash<ulong, PlayerData> restoreData = new Hash<ulong, PlayerData>();

            public void AddData(BasePlayer player, ConfigData.LossAmounts lossAmounts, double expires)
            {
                restoreData[player.userID] = new PlayerData(player, lossAmounts, expires);
            }

            public void RemoveData(ulong playerId)
            {
                if (HasRestoreData(playerId))
                    restoreData.Remove(playerId);
            }

            public bool HasRestoreData(ulong playerId) => restoreData.ContainsKey(playerId);

            public void RestorePlayer(BasePlayer player)
            {
                if (restoreData.TryGetValue(player.userID, out PlayerData playerData))
                {
                    if (playerData == null || !player.IsConnected)
                        return;

                    RestoreAllItems(player, playerData);
                }
            }

            private void RestoreAllItems(BasePlayer player, PlayerData playerData)
            {
                if (RestoreItems(player, playerData.containerBelt, ContainerType.Belt) && RestoreItems(player, playerData.containerWear, ContainerType.Wear) && RestoreItems(player, playerData.containerMain, ContainerType.Main))
                {
                    RemoveData(player.userID);
                    player.ChatMessage("Your inventory has been restored");
                }
            }

            private static bool RestoreItems(BasePlayer player, ItemData[] itemData, ContainerType containerType)
            {
                ItemContainer container = containerType switch
                {
                    ContainerType.Belt => player.inventory.containerBelt,
                    ContainerType.Wear => player.inventory.containerWear,
                    _ => player.inventory.containerMain
                };

                for (int i = 0; i < itemData.Length; i++)
                {
                    Item item = CreateItem(itemData[i]);
                    if (item == null)
                        continue;

                    if (!item.MoveToContainer(container, itemData[i].position) && !item.MoveToContainer(container) && !player.inventory.GiveItem(item))
                    {
                        // If allowing default items, restored items might not fit, so drop them as a last resort.
                        item.Drop(player.inventory.containerMain.dropPosition, player.inventory.containerMain.dropVelocity);
                    }
                }

                return true;
            }

            public class PlayerData
            {
                public ItemData[] containerMain;
                public ItemData[] containerWear;
                public ItemData[] containerBelt;

                public double expires = 0;

                public PlayerData()
                {
                }

                public PlayerData(BasePlayer player, ConfigData.LossAmounts lossAmounts, double expires)
                {
                    containerBelt = GetItems(player.inventory.containerBelt, Mathf.Clamp(lossAmounts.Belt, 0, 100));
                    containerMain = GetItems(player.inventory.containerMain, Mathf.Clamp(lossAmounts.Main, 0, 100));
                    containerWear = GetItems(player.inventory.containerWear, Mathf.Clamp(lossAmounts.Wear, 0, 100));
                    this.expires = expires;
                }
                
                public bool HasExpired(double currentTime) => expires > 0 && expires < currentTime;

                private static ItemData[] GetItems(ItemContainer container, int lossPercentage)
                {
                    int keepPercentage = 100 - lossPercentage;

                    int itemCount = keepPercentage == 100 ? container.itemList.Count : Mathf.CeilToInt((float)container.itemList.Count * (float)(keepPercentage / 100f));
                    if (itemCount == 0)
                        return Array.Empty<ItemData>();

                    ItemData[] itemData = new ItemData[itemCount];

                    for (int i = 0; i < itemCount; i++)
                    {
                        Item item = container.itemList.GetRandom();
                        itemData[i] = new ItemData(item);
                        item.RemoveFromContainer();
                        item.Remove();
                    }

                    // Sort items by position to ensure they are restored in original order if desired slot is taken by pre-existing items.
                    Array.Sort(itemData, (a, b) => a.position.CompareTo(b.position));

                    return itemData;
                }
            }
        }

        public class ItemData
        {
            public int itemid;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public ulong skin;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public string displayName;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public int amount;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public float condition;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public float maxCondition;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public int ammo;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public string ammotype;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public int position;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public int frequency;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public InstanceData instanceData;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public string text;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public Item.Flag flags;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public ItemData[] contents;
            
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
            public ItemContainer container;

            public ItemData()
            {
            }

            public ItemData(Item item)
            {
                BaseEntity heldEntity = item.GetHeldEntity();
                BaseProjectile baseProjectile = heldEntity as BaseProjectile;

                itemid = item.info.itemid;
                amount = item.amount;
                displayName = item.name;
                ammo = baseProjectile ? baseProjectile.primaryMagazine.contents : heldEntity is FlameThrower flameThrower ? flameThrower.ammo : 0;
                ammotype = baseProjectile ? baseProjectile.primaryMagazine.ammoType.shortname : null;
                position = item.position;
                skin = item.skin;
                condition = item.condition;
                maxCondition = item.maxCondition;
                frequency = ItemModAssociatedEntity<PagerEntity>.GetAssociatedEntity(item)?.GetFrequency() ?? -1;
                instanceData = new InstanceData(item);
                text = item.text;
                flags = item.flags;
                container = item.contents != null ? new ItemContainer(item.contents) : null;
            }

            public class InstanceData
            {
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int dataInt;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int blueprintTarget;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int blueprintAmount;

                public InstanceData()
                {
                }

                public InstanceData(Item item)
                {
                    if (item.instanceData == null)
                        return;

                    dataInt = item.instanceData.dataInt;
                    blueprintAmount = item.instanceData.blueprintAmount;
                    blueprintTarget = item.instanceData.blueprintTarget;
                }

                public void Restore(Item item)
                {
                    if (item.instanceData == null)
                        item.instanceData = new ProtoBuf.Item.InstanceData();

                    item.instanceData.ShouldPool = false;

                    item.instanceData.blueprintAmount = blueprintAmount;
                    item.instanceData.blueprintTarget = blueprintTarget;
                    item.instanceData.dataInt = dataInt;

                    item.MarkDirty();
                }

                public bool IsValid()
                {
                    return dataInt != 0 || blueprintAmount != 0 || blueprintTarget != 0;
                }
            }

            public class ItemContainer
            {
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int slots;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public float temperature;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int flags;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int allowedContents;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int maxStackSize;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public List<int> allowedItems;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public List<int> availableSlots;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public int volume;
                
                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
                public List<ItemData> contents;

                public ItemContainer()
                {
                }

                public ItemContainer(global::ItemContainer container)
                {
                    contents = new List<ItemData>();
                    slots = container.capacity;
                    temperature = container.temperature;
                    allowedContents = (int)container.allowedContents;

                    if (container.HasLimitedAllowedItems)
                    {
                        allowedItems = new List<int>();
                        for (int i = 0; i < container.onlyAllowedItems.Length; i++)
                        {
                            if (container.onlyAllowedItems[i])
                            {
                                allowedItems.Add(container.onlyAllowedItems[i].itemid);
                            }
                        }
                    }

                    flags = (int)container.flags;
                    maxStackSize = container.maxStackSize;
                    volume = container.containerVolume;

                    if (container.availableSlots is { Count: > 0 })
                    {
                        availableSlots = new List<int>();
                        for (int j = 0; j < container.availableSlots.Count; j++)
                        {
                            availableSlots.Add((int)container.availableSlots[j]);
                        }
                    }

                    for (int k = 0; k < container.itemList.Count; k++)
                    {
                        Item item = container.itemList[k];
                        if (item.IsValid())
                        {
                            contents.Add(new ItemData(item));
                        }
                    }
                }

                public void Load(global::ItemContainer itemContainer)
                {
                    itemContainer.capacity = slots;
                    itemContainer.itemList = Pool.Get<List<Item>>();
                    itemContainer.temperature = temperature;
                    itemContainer.flags = (global::ItemContainer.Flag)flags;
                    itemContainer.allowedContents = (global::ItemContainer.ContentsType)((allowedContents == 0) ? 1 : allowedContents);

                    if (allowedItems is { Count: > 0 })
                    {
                        itemContainer.onlyAllowedItems = new ItemDefinition[allowedItems.Count];
                        for (int i = 0; i < allowedItems.Count; i++)
                        {
                            itemContainer.onlyAllowedItems[i] = ItemManager.FindItemDefinition(allowedItems[i]);
                        }
                    }
                    else
                    {
                        itemContainer.onlyAllowedItems = null;
                    }

                    itemContainer.maxStackSize = maxStackSize;
                    itemContainer.containerVolume = volume;
                    itemContainer.availableSlots.Clear();

                    if (availableSlots != null)
                    {
                        for (int j = 0; j < availableSlots.Count; j++)
                        {
                            itemContainer.availableSlots.Add((ItemSlot)availableSlots[j]);
                        }
                    }

                    foreach (ItemData itemData in contents)
                    {
                        Item item = CreateItem(itemData);
                        if (item == null)
                            continue;

                        if (!item.MoveToContainer(itemContainer, itemData.position) && !item.MoveToContainer(itemContainer))
                            item.Remove();
                    }

                    itemContainer.MarkDirty();
                }
            }
        }

        public static Item CreateItem(ItemData itemData)
        {
            Item item = ItemManager.CreateByItemID(itemData.itemid, Mathf.Max(1, itemData.amount), itemData.skin);
            if (item == null)
                return null;

            item.condition = itemData.condition;
            item.maxCondition = itemData.maxCondition;

            if (itemData.displayName != null)
            {
                item.name = itemData.displayName;
            }

            if (itemData.text != null)
            {
                item.text = itemData.text;
            }

            item.flags |= itemData.flags;

            if (itemData.frequency > 0)
            {
                ItemModRFListener rfListener = item.info.GetComponentInChildren<ItemModRFListener>();
                if (rfListener)
                {
                    PagerEntity pagerEntity = BaseNetworkable.serverEntities.Find(item.instanceData.subEntity) as PagerEntity;
                    if (pagerEntity)
                    {
                        pagerEntity.ChangeFrequency(itemData.frequency);
                        item.MarkDirty();
                    }
                }
            }

            if (itemData.instanceData != null && itemData.instanceData.IsValid())
                itemData.instanceData.Restore(item);

            FlameThrower flameThrower = item.GetHeldEntity() as FlameThrower;
            if (flameThrower)
                flameThrower.ammo = itemData.ammo;

            if (itemData.contents != null && item.contents != null)
            {
                foreach (ItemData contentData in itemData.contents)
                {
                    Item childItem = CreateItem(contentData);
                    if (childItem == null)
                        continue;

                    if (!childItem.MoveToContainer(item.contents, contentData.position) && !childItem.MoveToContainer(item.contents))
                        item.Remove();
                    
                    item.MarkDirty();
                }
                
                item.contents.MarkDirty();
            }

            if (itemData.container != null)
            {
                if (item.contents == null)
                {
                    ItemModContainerArmorSlot armorSlot = FindItemMod<ItemModContainerArmorSlot>(item);
                    if (armorSlot)
                        armorSlot.CreateAtCapacity(itemData.container.slots, item);
                    else
                    {
                        item.contents = Pool.Get<ItemContainer>();
                        item.contents.ServerInitialize(item, itemData.container.slots);
                        item.contents.GiveUID();
                    }
                }

                itemData.container.Load(item.contents);
            }

            // Process weapon attachments/capacity after child items have been added.
            BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
            if (weapon)
            {
                weapon.DelayedModsChanged();

                if (!string.IsNullOrEmpty(itemData.ammotype))
                    weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(itemData.ammotype);
                weapon.primaryMagazine.contents = itemData.ammo;
            }

            return item;
        }

        private static T FindItemMod<T>(Item item) where T : ItemMod
        {
            foreach (ItemMod itemMod in item.info.itemMods)
            {
                if (itemMod is T mod)
                    return mod;
            }

            return null;
        }

        #endregion
    }
}