using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cysharp.Text;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using ProtoBuf;
using Rust;
using UnityEngine;

// Reference: ZString

namespace Oxide.Plugins;

[Info("Chest Stacks", "supreme", "1.4.7")]
[Description("Allows players to stack chests")]
public class ChestStacks : RustPlugin
{
    #region Initialization
    
    private const string UsePermission = "cheststacks.use";
    private const string SmallBoxPrefabPath = "assets/prefabs/deployable/woodenbox";
    private const string LargeBoxPrefabPath = "assets/prefabs/deployable/large wood storage";
    private const string LargeBoxPrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
    private const string DecorDlcPrefabPath = "assets/prefabs/misc/decor_dlc/storagebarrel";
    private const string ComponentDlcPrefabPath =
        "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc";
    private const string SmallBoxEffect = "assets/prefabs/deployable/woodenbox/effects/wooden-box-deploy.prefab";
    private const string LargeBoxEffect = "assets/prefabs/deployable/large wood storage/effects/large-wood-box-deploy.prefab";
    private const int BoxLayer = Layers.Mask.Deployed;
    private const int ConstructionLayer = Layers.Mask.Construction;
    private const int VehicleLargeLayer = Layers.Mask.Vehicle_Large;
    private const BaseEntity.Flags StackedFlag = BaseEntity.Flags.Reserved1;
    
    private static ChestStacks _pluginInstance;
    private readonly object _returnObject = true;
    private readonly Hash<ulong, BoxStackHandler> _components;
    private readonly Dictionary<int, string> _itemPrefabs;
    private readonly Dictionary<uint, Vector3> _boxStorageOffsets;
    private readonly RaycastHit[] _raycastHits;
    private readonly Vector3 _raycastOffset;
    private PluginConfiguration _pluginConfiguration;
    private PluginData _pluginData;

    private enum BoxType
    {
        None = 0,
        SmallBox = 1,
        LargeBox = 2
    }

    public ChestStacks()
    {
        _pluginInstance = this;
        _components = new Hash<ulong, BoxStackHandler>();
        _itemPrefabs = new Dictionary<int, string>();
        _boxStorageOffsets = new Dictionary<uint, Vector3>();
        _raycastHits = new RaycastHit[3];
        _raycastOffset = new Vector3(0f, 0.2f, 0f);
    }

    #endregion

    #region Hooks
        
    private void Init()
    {
        LoadData();

        foreach (string perm in _pluginConfiguration.ChestStacksAmount.Keys)
        {
            permission.RegisterPermission(perm, this);
        }

        if (permission.PermissionExists(UsePermission))
        {
            return;
        }
        
        permission.RegisterPermission(UsePermission, this);
    }

    private void OnServerInitialized()
    {
        CacheItemPrefabs();
        
        foreach (BasePlayer player in BasePlayer.activePlayerList)
        {
            OnPlayerConnected(player);
        }
            
        SaveData();
    }

    private void Unload()
    {
        List<BoxStackHandler> boxStackHandlers = Pool.Get<List<BoxStackHandler>>();
        boxStackHandlers.AddRange(_components.Values);
        
        int boxStackHandlerCount = boxStackHandlers.Count;
        for (int i = 0; i < boxStackHandlerCount; i++)
        {
            boxStackHandlers[i].Destroy();
        }
        
        Pool.FreeUnmanaged(ref boxStackHandlers);

        SaveData();
        _pluginInstance = null;
    }

    private void OnNewSave() => _pluginData.StoredBoxes.Clear();

    private void OnPlayerConnected(BasePlayer player)
    {
        BoxStackHandler boxStackHandler = _components[player.userID];
        if (boxStackHandler)
        {
            return;
        }
            
        boxStackHandler = player.gameObject.AddComponent<BoxStackHandler>();
        boxStackHandler.Initialize(player);
    }
        
    private void OnPlayerDisconnected(BasePlayer player)
    {
        BoxStackHandler boxStackHandler = _components[player.userID];
        if (!boxStackHandler)
        {
            return;
        }
            
        boxStackHandler.Destroy();
    }
    
    private void OnEntityKill(BoxStorage boxStorage)
    {
        if (!boxStorage)
        {
            return;
        }
        
        if (GetTugboat(boxStorage))
        {
            CheckNearbyBoxes(boxStorage, 2f);
        }

        if (!IsStacked(boxStorage))
        {
            return;
        }
        
        HandleBoxUnstack(boxStorage.net.ID.Value);
    }
    
    private object OnEntityGroundMissing(BoxStorage boxStorage)
    {
        if (!boxStorage || !IsStacked(boxStorage))
        {
            return null;
        }
        
        HandleBoxStorageGroundMissing(boxStorage);
        return _returnObject;
    }

    private object CanStackBox(BasePlayer player, Item item, string prefab)
    {
        return null;
    }

    #endregion

    #region Core Methods

    private void HandleBoxStorageGroundMissing(BoxStorage boxStorage)
    {
        if (IsStackedOnBox(boxStorage))
        {
            return;
        }
        
        boxStorage.Die();
    }

    #endregion
        
    #region Helpers
    
    private void CacheItemPrefabs()
    {
        List<ItemDefinition> itemDefinitions = ItemManager.GetItemDefinitions();
        int itemDefinitionCount = itemDefinitions.Count;
        for (int i = 0; i < itemDefinitionCount; i++)
        {
            ItemDefinition itemDefinition = itemDefinitions[i];
            ItemModDeployable deployable = itemDefinition.GetComponent<ItemModDeployable>();
            if (!deployable)
            {
                continue;
            }

            _itemPrefabs[itemDefinition.itemid] = deployable.entityPrefab.resourcePath;
        }
    }
    
    public string? GetItemPrefab(int itemId) => _itemPrefabs.GetValueOrDefault(itemId);
    
    private Vector3 GetBoxOffset(string prefab)
    {
        uint prefabId = StringPool.Get(prefab);
        if (_boxStorageOffsets.TryGetValue(prefabId, out Vector3 offset))
        {
            return offset;
        }
        
        offset = _pluginConfiguration.SupportedBoxOffsets.GetValueOrDefault(prefab);
        if (offset != Vector3.zero)
        {
            return _boxStorageOffsets[prefabId] = offset;
        }
        
        foreach (KeyValuePair<string, Vector3> entry in _pluginConfiguration.SupportedPrefabPathOffsets)
        {
            if (!prefab.StartsWith(entry.Key))
            {
                continue;
            }
                
            return _boxStorageOffsets[prefabId] = entry.Value;
        }

        return Vector3.zero;
    }
    
    private void CheckNearbyBoxes(BoxStorage boxStorage, float radius)
    {
        List<BoxStorage> boxStorages = Pool.Get<List<BoxStorage>>();
        
        Vis.Entities(boxStorage.transform.position, radius, boxStorages, BoxLayer);
        int boxStorageCount = boxStorages.Count;
        try
        {
            for (int i = 0; i < boxStorageCount; i++)
            {
                BoxStorage foundBoxStorage = boxStorages[i];
                if (!foundBoxStorage || boxStorage == foundBoxStorage || !IsStacked(foundBoxStorage))
                {
                    continue;
                }
                    
                NextFrame(() => HandleBoxStorageGroundMissing(foundBoxStorage));
            }
        }
        finally
        {
            Pool.FreeUnmanaged(ref boxStorages);
        }
    }
    
    private bool HasStackedBox(BoxStorage boxStorage)
    {
        if (!boxStorage)
        {
            return false;
        }
        
        Vector3 position = boxStorage.transform.position;
        
        int raycastHitCount = 
            Physics.RaycastNonAlloc(position, Vector3.up, _raycastHits, 1.5f, BoxLayer);
        
        for (int i = 0; i < raycastHitCount; i++)
        {
            RaycastHit raycastHit = _raycastHits[i];
            if (raycastHit.collider.gameObject.ToBaseEntity() is not BoxStorage stackedBoxStorage)
            {
                continue;
            }
            
            if (stackedBoxStorage == boxStorage)
            {
                continue;
            }
            
            return true;
        }
        
        return false;
    }

    private bool IsStackedOnBox(BoxStorage boxStorage)
    {
        if (!boxStorage)
        {
            return false;
        }
        
        Vector3 position = boxStorage.transform.position + _raycastOffset;
        
        int raycastHitCount = 
            Physics.RaycastNonAlloc(position, Vector3.down, _raycastHits, 0.25f, BoxLayer);
        
        for (int i = 0; i < raycastHitCount; i++)
        {
            RaycastHit raycastHit = _raycastHits[i];
            if (raycastHit.collider.gameObject.ToBaseEntity() is not BoxStorage stackedBoxStorage)
            {
                continue;
            }
            
            if (stackedBoxStorage == boxStorage)
            {
                continue;
            }
            
            return true;
        }
        
        return false;
    }

    private bool IsStacked(BoxStorage boxStorage) => boxStorage.HasFlag(StackedFlag) || boxStorage.GetParentEntity() is BoxStorage;

    private ulong GetBottomBoxId(ulong boxId) => _pluginData.StoredBoxes[boxId]?.BottomBoxId ?? 0;

    private int GetStackedBoxes(ulong bottomBoxId) => _pluginData.StoredBoxes[bottomBoxId]?.Boxes ?? 0;

    private void HandleBoxUnstack(ulong boxId)
    {
        ulong bottomBoxId = GetBottomBoxId(boxId);
        if (bottomBoxId == 0)
        {
            return;
        }
        
        _pluginData.StoredBoxes[bottomBoxId].Boxes--;
    }
    
    private Tugboat? GetTugboat<T>(T type) where T : BaseEntity => type.GetParentEntity() as Tugboat;
        
    private bool HasPermission(BasePlayer player, string perm) =>
        permission.UserHasPermission(player.UserIDString, perm);
        
    private int GetPermissionValue(BasePlayer player, Hash<string, ChestTypeConfig> permissions, BoxType boxType)
    {
        foreach ((string perm, ChestTypeConfig chestTypeConfig) in 
                 permissions.OrderByDescending(p => p.Value.BoxTypeLimits[boxType]))
        {
            if (!HasPermission(player, perm))
            {
                continue;
            }
            
            return chestTypeConfig.BoxTypeLimits[boxType];
        }

        return 0;
    }

    #endregion

    #region Box stack handler
        
    public class BoxStackHandler : FacepunchBehaviour
    {
        private BasePlayer _player;
        private float _cooldown;

        public void Initialize(BasePlayer player)
        {
            _player = player;
            _pluginInstance._components[_player.userID] = this;
        }

        private void Update()
        {
            if (!_player || !_pluginInstance.HasPermission(_player, UsePermission))
            {
                return;
            }

            if (!_player.serverInput.WasJustPressed(BUTTON.FIRE_SECONDARY))
            {
                return;
            }
                
            if (_cooldown > Time.time)
            {
                return;
            }
                    
            _cooldown = Time.time + 0.5f;
                    
            Item sourceItem = _player.GetActiveItem();
            if (sourceItem == null || sourceItem.info.category != ItemCategory.Items)
            {
                return;
            }
                    
            if (_pluginInstance._pluginConfiguration.BlacklistedSkins.Contains(sourceItem.skin))
            {
                return;
            }
            
            string? sourcePrefab = _pluginInstance.GetItemPrefab(sourceItem.info.itemid);
            if (sourcePrefab == null)
            {
                return;
            }
            
            if (Interface.Oxide.CallHook("CanStackBox", _player, sourceItem, sourcePrefab) != null)
            {
                return;
            }
            
            Vector3 offset = _pluginInstance.GetBoxOffset(sourcePrefab);
            if (offset == Vector3.zero)
            {
                return;
            }
                        
            BoxStorage? boxStorage = GetBox(_player);
            if (!boxStorage)
            {
                return;
            }

            BoxType sourceBoxType = GetBoxType(sourcePrefab);
            if (sourceBoxType == BoxType.None)
            {
                return;
            }

            string targetPrefab = boxStorage.PrefabName;
            BoxType targetBoxType = GetBoxType(targetPrefab);
            if (targetBoxType == BoxType.None)
            {
                return;
            }
            
            if (sourceBoxType != targetBoxType || sourcePrefab != targetPrefab && !CanStackDifferentPrefabs(sourcePrefab, targetPrefab))
            {
                _player.ChatMessage(_pluginInstance.GetLangKeyString(LangKeys.OnlyStackSameType));
                return;
            }

            StackBoxStorage(boxStorage, sourceItem, sourcePrefab, targetBoxType, offset);
        }
        
        private void StackBoxStorage(BoxStorage boxStorage, Item sourceItem, string sourcePrefab, BoxType targetBoxType, 
            Vector3 offset)
        {
            ulong bottomBoxId = _pluginInstance.GetBottomBoxId(boxStorage.net.ID.Value);
            int boxes = _pluginInstance.GetStackedBoxes(bottomBoxId);
            int allowedBoxAmount = _pluginInstance.GetPermissionValue(_player, 
                _pluginInstance._pluginConfiguration.ChestStacksAmount, targetBoxType);

            if (boxes >= allowedBoxAmount)
            {
                _player.ChatMessage(_pluginInstance.GetLangKeyString(LangKeys.MaxStackAmount, allowedBoxAmount));
                return;
            }
            
            Tugboat? tugboat = _pluginInstance.GetTugboat(_player);
            PlayerBoat? playerBoat = PlayerBoat.GetParentPlayerBoat(_player);
            
            bool isOnVehicle = tugboat || playerBoat;
        
            if (_pluginInstance._pluginConfiguration.BuildingPrivilegeRequired)
            {
                if (tugboat && !_player.CanBuild() || playerBoat && !playerBoat.IsAuthedForBuilding(_player) || 
                    !isOnVehicle && !_player.IsBuildingAuthed())
                {
                    _player.ChatMessage(_pluginInstance.GetLangKeyString(LangKeys.BuildingBlock));
                    return;
                }
            }

            Transform boxTransform = boxStorage.transform;
            Vector3 position = boxTransform.position;
            if (HasCeiling(position + offset, targetBoxType, isOnVehicle))
            {
                _player.ChatMessage(_pluginInstance.GetLangKeyString(LangKeys.CeilingBlock));
                return;
            }

            if (_pluginInstance.HasStackedBox(boxStorage))
            {
                return;
            }
            
            BoxStorage createdBoxStorage = (BoxStorage)GameManager.server.CreateEntity(sourcePrefab, 
                position + offset, boxTransform.rotation);
            
            if (!createdBoxStorage)
            {
                return;
            }
                        
            if (tugboat)
            {
                createdBoxStorage.SetParent(tugboat, true);
            }

            if (playerBoat)
            {
                createdBoxStorage.SetParent(playerBoat, true);
            }
            
            createdBoxStorage.SetFlagLocal(StackedFlag, true);
            createdBoxStorage.Spawn();
            createdBoxStorage.OwnerID = _player.userID;
            createdBoxStorage.skinID = sourceItem.skin;
            createdBoxStorage.AttachToBuilding(boxStorage.buildingID);
                        
            Interface.CallHook("OnEntityBuilt", _player.GetHeldEntity(), createdBoxStorage.transform.gameObject);
            
            Effect.server.Run(targetBoxType == BoxType.SmallBox ? SmallBoxEffect : LargeBoxEffect, position);
            createdBoxStorage.SendNetworkUpdateImmediate();
            
            ProcessBoxStacks(boxStorage, createdBoxStorage);
                
            sourceItem.UseItem();
            _pluginInstance.SaveData();
        }

        private void ProcessBoxStacks(BoxStorage lastBox, BoxStorage newBox)
        {
            ulong bottomBoxId = _pluginInstance.GetBottomBoxId(lastBox.net.ID.Value);
            if (bottomBoxId == 0)
            {
                _pluginInstance._pluginData.StoredBoxes[newBox.net.ID.Value] = new BoxData
                {
                    BottomBoxId = newBox.net.ID.Value,
                    Boxes = 2
                };

                return;
            }

            int stackedBoxes = _pluginInstance.GetStackedBoxes(bottomBoxId);
            _pluginInstance._pluginData.StoredBoxes[newBox.net.ID.Value] = new BoxData
            {
                BottomBoxId = bottomBoxId,
            };

            _pluginInstance._pluginData.StoredBoxes[bottomBoxId].Boxes = ++stackedBoxes;
        }

        private bool CanStackDifferentPrefabs(string sourcePrefab, string targetPrefab) => 
            sourcePrefab.StartsWith(ComponentDlcPrefabPath) || sourcePrefab == LargeBoxPrefab && 
            targetPrefab.StartsWith(ComponentDlcPrefabPath) || targetPrefab == LargeBoxPrefab;
        
        private bool HasCeiling(Vector3 position, BoxType boxType, bool isOnVehicle)
        {
            int layerMask = isOnVehicle ? VehicleLargeLayer : ConstructionLayer;
            return boxType switch
            {
                BoxType.SmallBox => Physics.Raycast(position, Vector3.up, 0.5f, layerMask),
                BoxType.LargeBox => Physics.Raycast(position, Vector3.up, 0.9f, layerMask),
                _ => false
            };
        }
            
        private BoxStorage? GetBox(BasePlayer player)
        {
            if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit raycastHit, 4f, BoxLayer))
            {
                return null;
            }
            
            return raycastHit.GetEntity() as BoxStorage;
        }
        
        private BoxType GetBoxType(string prefab)
        {
            if (prefab.StartsWith(SmallBoxPrefabPath))
            {
                return BoxType.SmallBox;
            }

            return prefab.StartsWith(LargeBoxPrefabPath) || prefab.StartsWith(DecorDlcPrefabPath) ? BoxType.LargeBox : BoxType.None;
        }

        public void Destroy()
        {
            _pluginInstance._components.Remove(_player.userID);
            DestroyImmediate(this);
        }
    }

    #endregion

    #region Configuration
        
    private class PluginConfiguration
    {
        [DefaultValue(true)]
        [JsonProperty("Building privilege required")]
        public bool BuildingPrivilegeRequired { get; set; }
            
        [JsonProperty("Blacklisted Skins")]
        public HashSet<ulong>? BlacklistedSkins { get; set; }
            
        [JsonProperty("Permissions & their amount of stacked chests lmits")]
        public Hash<string, ChestTypeConfig>? ChestStacksAmount { get; set; }
        
        [JsonProperty("Supported box prefab offsets")]
        public Dictionary<string, Vector3>? SupportedBoxOffsets { get; set; }
        
        [JsonProperty("Supported prefab path offsets")]
        public Dictionary<string, Vector3>? SupportedPrefabPathOffsets { get; set; }
    }

    private class ChestTypeConfig
    {
        [JsonProperty("Chest type limits")]
        public Dictionary<BoxType, int>? BoxTypeLimits { get; set; }
    }

    protected override void LoadDefaultConfig()
    {
        PrintWarning("Loading Default Config");
    }

    protected override void LoadConfig()
    {
        base.LoadConfig();
        Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
        _pluginConfiguration = AdditionalConfig(Config.ReadObject<PluginConfiguration>());
        Config.WriteObject(_pluginConfiguration);
    }

    private PluginConfiguration AdditionalConfig(PluginConfiguration pluginConfiguration)
    {
        pluginConfiguration.BlacklistedSkins ??= new HashSet<ulong>
        {
            2618923347
        };

        pluginConfiguration.ChestStacksAmount ??= new Hash<string, ChestTypeConfig>
        {
            ["cheststacks.use"] = new()
            {
                BoxTypeLimits = new Dictionary<BoxType, int>
                {
                    [BoxType.SmallBox] = 3,
                    [BoxType.LargeBox] = 5
                }
            },
            ["cheststacks.vip"] = new()
            {
                BoxTypeLimits = new Dictionary<BoxType, int>
                {
                    [BoxType.SmallBox] = 5,
                    [BoxType.LargeBox] = 10
                }
            },
        };

        pluginConfiguration.SupportedBoxOffsets ??= new Dictionary<string, Vector3>
        {
            ["assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"] = new(0f, 0.57f),
            ["assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"] = new(0f, 0.75f),
            ["assets/prefabs/deployable/large wood storage/skins/medieval_large_wood_box/medieval.box.wooden.large.prefab"] =
                new(0f, 0.76f),
            ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab"] =
                new(0f, 1.2f),
            ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab"] =
                new(0, 0.9f),
            ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab"] =
                new(0, 0.8f),
            ["assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_vertical/krieg_storage_vertical.prefab"] =
                new(0, 1.25f),
            ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab"] = new(0, 1.3f),
            ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab"] = new(0, 0.9f),
        };

        pluginConfiguration.SupportedPrefabPathOffsets ??= new Dictionary<string, Vector3>
        {
            ["assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc"] = new(0f, 0.76f)
        };
            
        return pluginConfiguration;
    }

    #endregion
        
    #region Data

    [ProtoContract]
    private class PluginData
    {
        [ProtoMember(1)]
        public Hash<ulong, BoxData> StoredBoxes { get; set; } = new();
    }

    [ProtoContract]
    private class BoxData
    {
        [ProtoMember(1)]
        public ulong BottomBoxId { get; set; }
        
        [ProtoMember(2)]
        public int Boxes { get; set; }
    }
    
    private void LoadData() => _pluginData = ProtoStorage.Load<PluginData>(Name) ?? new PluginData();

    private void SaveData()
    {
        if (_pluginData == null)
        {
            return;
        }
            
        ProtoStorage.Save(_pluginData, Name);
    }
        
    #endregion
        
    #region Localization
        
    private class LangKeys
    {
        public const string MaxStackAmount = nameof(MaxStackAmount);
        public const string OnlyStackSameType = nameof(OnlyStackSameType);
        public const string CeilingBlock = nameof(CeilingBlock);
        public const string BuildingBlock = nameof(BuildingBlock);
    }
        
    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            [LangKeys.MaxStackAmount] = "You are trying to stack more than {0} chests!",
            [LangKeys.OnlyStackSameType] = "You can only stack the same type of chests!",
            [LangKeys.CeilingBlock] = "A ceiling is blocking you from stacking this chest!",
            [LangKeys.BuildingBlock] = "You need to be Building Privileged in order to stack chests!"

        }, this);
    }
    
    private string GetLangKeyString(string langKey) => lang.GetMessage(langKey, this);
    
    private string GetLangKeyString<T>(string langKey, T arg) => ZString.Format(GetLangKeyString(langKey), arg);
        
    #endregion
}  