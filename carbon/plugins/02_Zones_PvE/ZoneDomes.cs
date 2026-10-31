using Facepunch;
using System;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ZoneDomes", "k1lly0u", "2.0.2")]
    [Description("Assign shaded sphere entities to physically see the border of zones")]
    class ZoneDomes : RustPlugin
    {
        #region Fields

        [PluginReference]
        private Plugin ZoneManager;
        
        private StoredData _storedData;
        private DynamicConfigFile _dataFile;

        private const string SHADED_SPHERE = "assets/prefabs/visualization/sphere.prefab";
        private const string BR_SPHERE_RED = "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab";
        private const string BR_SPHERE_BLUE = "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab";
        private const string BR_SPHERE_GREEN = "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab";
        private const string BR_SPHERE_PURPLE = "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab";

        private const string USE_PERMISSION = "zonedomes.admin";

        private readonly Hash<string, List<SphereEntity>> _sphereEntities = new Hash<string, List<SphereEntity>>();
        
        public enum SphereType { Standard, Red, Blue, Green, Purple }
        
        #endregion

        #region Oxide Hooks

        private void Loaded()
        {
            _dataFile = Interface.Oxide.DataFileSystem.GetFile("zonedomes_data");
            _dataFile.Settings.Converters = new JsonConverter[] { new StringEnumConverter(), new UnityVector3Converter() };
            
            LoadData();
            
            permission.RegisterPermission(USE_PERMISSION, this);
        }

        private void OnServerInitialized()
            => InitializeDomes();

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Error.NoInfo"] = "Unable to find information for: ",
                ["Error.NoEntity"] = "Unable to find the sphere entity for zone ID: ",
                ["Error.InvalidID"] = "Unable to validate the zone ID from ZoneManager for: ",
                ["Error.NoLocation"] = "Unable to retrieve location data from ZoneManager for: ",
                ["Error.NoRadius"] = "Unable to retrieve radius data from ZoneManager for: ",
                ["Error.NoZoneManager"] = "Unable to find ZoneManager, unable to proceed",
                ["Error.NoPermission"] = "You do not have permission to use this command",

                ["Notification.RemoveInvalidData"] = "Removing invalid zone data",
                ["Notification.RemoveSuccess"] = "You have successfully removed the sphere from zone: ",
                ["Notification.Created"] = "You have successfully created a sphere for the zone: ",
                ["Notification.InvalidZones"] = "Found {0} invalid zones. Removing them from data",
                ["Notification.AlreadyExists"] = "This zone already has a dome",
                
                ["Chat.Title"] = "<color=#ce422b>ZoneDomes</color>",
                ["Chat.AddSyntax"] = "<color=#ce422b>/zd add <zoneId> <opt:type> <opt:stack></color><color=#939393> - Adds a sphere to a zone</color>",
                ["Chat.RemoveSyntax"] = "<color=#ce422b>/zd remove <zoneId></color><color=#939393> - Removes a sphere from a zone</color>",
                ["Chat.ListSyntax"] = "<color=#ce422b>/zd list</color><color=#939393> - Lists all current active spheres and their position</color>",
                ["Chat.Types"] = "<color=#ce422b>Sphere Types</color><color=#939393>:\n" +
                                 "0 = <color=#464646>Standard</color>\n" +
                                 "1 = <color=#ce422b>Red</color>\n" +
                                 "2 = <color=#2b7fce>Blue</color>\n" +
                                 "3 = <color=#2bce7f>Green</color>\n" +
                                 "4 = <color=#7f2bce>Purple</color>\n" +
                                 "*NOTE : Colored spheres need to interact with terrain or objects in the world to be visible!</color>",
                
                ["Format.List"] = "--- Sphere List --- \n Zone ID -- Radius -- Position -- Color -- Stack"
            }, this);
        }

        private void Unload()
        {
            foreach (List<SphereEntity> list in _sphereEntities.Values)
            {
                foreach (SphereEntity sphere in list)
                {
                    if (sphere && !sphere.IsDestroyed)
                        sphere.Kill();
                }
            }
            
            _sphereEntities.Clear();
        }

        #endregion

        #region Functions

        private void InitializeDomes()
        {
            List<string> invalidZones = Pool.Get<List<string>>();

            bool dataChanged = false;
            
            foreach (KeyValuePair<string, StoredData.Zone> kvp in _storedData.Zones)
            {
                if (!IsValidZoneID(kvp.Key))
                {
                    invalidZones.Add(kvp.Key);
                    continue;
                }

                if (TryGetZoneLocation(kvp.Key, out Vector3 position) && kvp.Value.Position != position)
                {
                    kvp.Value.Position = position;
                    dataChanged = true;
                }
                
                if (TryGetZoneRadius(kvp.Key, out float radius) && !Mathf.Approximately(kvp.Value.Radius, radius))
                {
                    kvp.Value.Radius = radius;
                    dataChanged = true;
                }

                CreateDome(kvp.Key, kvp.Value);
            }

            if (invalidZones.Count > 0)
            {
                PrintWarning(string.Format(TranslateMessage("Notification.InvalidZones"), invalidZones.Count));
                
                foreach (string zoneId in invalidZones)
                    _storedData.Zones.Remove(zoneId);

                dataChanged = true;
            }
            
            Pool.FreeUnmanaged(ref invalidZones);
            
            if (dataChanged)
                SaveData();
        }

        private void CreateDome(string zoneId, StoredData.Zone zone)
        {
            string prefab = zone.Type switch
            {
                SphereType.Red => BR_SPHERE_RED,
                SphereType.Blue => BR_SPHERE_BLUE,
                SphereType.Green => BR_SPHERE_GREEN,
                SphereType.Purple => BR_SPHERE_PURPLE,
                _ => SHADED_SPHERE
            };
            
            List<SphereEntity> sphereEntities = new List<SphereEntity>();
            _sphereEntities[zoneId] = sphereEntities;
            
            for (int i = 0; i < zone.Stack; i++)
            {
                SphereEntity sphereEntity = GameManager.server.CreateEntity(prefab, zone.Position) as SphereEntity;
                sphereEntity.currentRadius = zone.Radius * 2f;
                //sphereEntity.lerpSpeed = 0f;
                sphereEntity.lerpRadius = sphereEntity.currentRadius;

                sphereEntity.enableSaving = false;
                sphereEntity.Spawn();
                
                sphereEntities.Add(sphereEntity);
            }
        }

        private void DestroyDomeForZone(string zoneId)
        {
            if (_sphereEntities.TryGetValue(zoneId, out List<SphereEntity> sphereEntities))
            {
                foreach (SphereEntity sphereEntity in sphereEntities)
                {
                    if (sphereEntity && !sphereEntity.IsDestroyed)
                        sphereEntity.Kill();
                }
                
                _sphereEntities.Remove(zoneId);
            }
        }

        private void TranslateMessage(BasePlayer player, string key, string additional = "") 
            => player.ChatMessage($"<color=#939393>{TranslateMessage(key, player)}</color><color=#ce422b>{additional}</color>");
        
        private string TranslateMessage(string key, BasePlayer player = null) 
            => lang.GetMessage(key, this, !player ? "" : player.UserIDString);
        
        #endregion
        
        #region ZoneManager API
        
        private bool IsValidZoneID(string zoneId)
        {
            object result = ZoneManager?.Call("CheckZoneID", zoneId);
            return result is string s && !string.IsNullOrEmpty(s);
        }

        private bool TryGetZoneLocation(string zoneId, out Vector3 position)
        {
            object result = ZoneManager?.Call("GetZoneLocation", zoneId);
            if (!(result is Vector3 v))
            {
                position = default;
                return false;
            }

            position = v;
            return true;
        }

        private bool TryGetZoneRadius(string zoneId, out float radius)
        {
            object result = ZoneManager?.Call("GetZoneRadius", zoneId);
            if (!(result is float r))
            {
                radius = default;
                return false;
            }

            radius = r;
            return true;
        }
        
        #endregion
        
        #region Plugin API
        
        [HookMethod("AddNewDome")]
        public bool AddNewDome(BasePlayer player, string zoneId, int type = 0, int stack = 1)
        {
            if (!IsValidZoneID(zoneId))
            {
                TranslateMessage(player, "Error.InvalidID", zoneId);
                return false;
            }
            
            if (!TryGetZoneLocation(zoneId, out Vector3 position))
            {
                TranslateMessage(player, "Error.NoLocation", zoneId);
                return false;
            }
            
            if (!TryGetZoneRadius(zoneId, out float radius))
            {
                TranslateMessage(player, "Error.NoRadius", zoneId);
                return false;
            }
            
            if (_storedData.Zones.ContainsKey(zoneId))
            {
                TranslateMessage(player, "Notification.AlreadyExists");
                return false;
            }
            
            StoredData.Zone zone = _storedData.Zones[zoneId] = new StoredData.Zone
            {
                Position = position,
                Radius = radius,
                Type = (SphereType)type,
                Stack = stack
            };
            
            SaveData();
            CreateDome(zoneId, zone);
            
            TranslateMessage(player, "Notification.Created", zoneId);
            return true;
        }

        [HookMethod("RemoveExistingDome")]
        public bool RemoveExistingDome(BasePlayer player, string zoneId)
        {
            if (!_storedData.Zones.ContainsKey(zoneId))
            {
                TranslateMessage(player, "Error.NoEntity", zoneId);
                return false;
            }
            
            DestroyDomeForZone(zoneId);
            _storedData.Zones.Remove(zoneId);
            SaveData();
            
            TranslateMessage(player, "Notification.RemoveSuccess", zoneId);
            return true;
        }
        
        #endregion
        
        #region Commands

        [ChatCommand("zd")]
        private void CommandZoneDomes(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, USE_PERMISSION) && !player.IsAdmin)
            {
                TranslateMessage(player, "Error.NoPermission");
                return;
            }

            if (args == null || args.Length == 0)
            {
                StringBuilder sb = Pool.Get<StringBuilder>();
                sb.Clear();

                sb.AppendLine(TranslateMessage("Chat.Title", player));
                sb.AppendLine(TranslateMessage("Chat.AddSyntax", player));
                sb.AppendLine();
                sb.AppendLine(TranslateMessage("Chat.RemoveSyntax", player));
                sb.AppendLine();
                sb.AppendLine(TranslateMessage("Chat.ListSyntax", player));
                sb.AppendLine();
                sb.AppendLine(TranslateMessage("Chat.Types", player));
                
                player.ChatMessage(sb.ToString());
                sb.Clear();
                
                Pool.FreeUnmanaged(ref sb);
                return;
            }

            switch (args[0].ToLower())
            {
                case "add":
                {
                    if (args.Length <= 1)
                    {
                        TranslateMessage(player, "Chat.AddSyntax");
                        return;
                    }
                    
                    if (!ZoneManager)
                    {
                        TranslateMessage(player, "Error.NoZoneManager");
                        return;
                    }

                    string zoneId = args[1];
                    int type = 0;
                    int stack = 1;
                    
                    if (args.Length > 2 && int.TryParse(args[2], out int i))
                        type = Mathf.Clamp(i, 0, 4);
                    
                    if (args.Length > 3 && int.TryParse(args[3], out int s))
                        stack = Mathf.Clamp(s, 1, 10);
                    
                    AddNewDome(player, zoneId, (int)type, stack);
                    
                    return;
                }
                
                case "remove":
                {
                    if (args.Length != 2)
                    {
                        TranslateMessage(player, "Chat.RemoveSyntax");
                        return;
                    }

                    RemoveExistingDome(player, args[1]);
                    return;
                }
                
                case "list":
                {
                    StringBuilder sb = Pool.Get<StringBuilder>();
                    sb.Clear();
                    
                    sb.AppendLine(TranslateMessage("Format.List"));
                    
                    foreach (KeyValuePair<string, StoredData.Zone> kvp in _storedData.Zones)
                        sb.AppendLine($"{kvp.Key} -- {kvp.Value.Radius} -- {kvp.Value.Position} -- {kvp.Value.Type} -- {kvp.Value.Stack}");
                    
                    SendReply(player, sb.ToString());

                    sb.Clear();
                    Pool.FreeUnmanaged(ref sb);
                    return;
                }
            }
        }
        
        #endregion

        #region Config

        private ConfigData _configData;

        private class ConfigData
        {
            public VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _configData = Config.ReadObject<ConfigData>();

            if (_configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(_configData, true);
        }

        protected override void LoadDefaultConfig() => _configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(_configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            _configData.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Data Management

        private void SaveData() => _dataFile.WriteObject(_storedData);

        private void LoadData()
        {
            try
            {
                _storedData = _dataFile.ReadObject<StoredData>();
            }
            catch
            {
                _storedData = new StoredData();
            }
            
            if (_storedData == null)
                _storedData = new StoredData();
        }

        private class StoredData
        {
            public Dictionary<string, Zone> Zones = new Dictionary<string, Zone>();
            
            public class Zone
            {
                public Vector3 Position;
                public float Radius;
                public SphereType Type;
                public int Stack;
            }
        }

        private class UnityVector3Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector3 vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    string[] values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                JObject o = JObject.Load(reader);
                return new Vector3(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]), Convert.ToSingle(o["z"]));
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }
        }
        
        #endregion
    }
}