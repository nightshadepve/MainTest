using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Queue Holder", "Tryhard", "1.0.2")]
    [Description("Saves your position in queue if you disconnect")]
    public class QueueHolder : RustPlugin
    {
        #region Fields

        private Dictionary<ulong, DateTime> _queueHolder = new();
        private object _trueObj = true;

        #endregion
        
        #region Hooks
        
        private void Init()
        {
            permission.RegisterPermission("QueueHolder.skip", this);
            permission.RegisterPermission("QueueHolder.grace", this);
        }

        private object CanBypassQueue(Network.Connection connection)
        {
            if (connection == null) return null;

            var id = connection.userid;
            if (_queueHolder.TryGetValue(id, out var time))
            {
                if (DateTime.UtcNow.Subtract(time).TotalSeconds < config.queueTime)
                {
                    _queueHolder.Remove(id);
                    return _trueObj;
                }
                
                _queueHolder.Remove(id);
            }
            
            if (permission.UserHasPermission(id.ToString(), "QueueHolder.skip"))
            {
                return _trueObj;
            }
            
            return null;
        }
        
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !config.eQueue) return;

            if (_queueHolder.ContainsKey(player.userID))
            {
                _queueHolder.Remove(player.userID);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null || !config.eQueue) return;

            if (permission.UserHasPermission(player.UserIDString, "QueueHolder.grace"))
            {
                _queueHolder[player.userID] = DateTime.UtcNow;
            }
        }

        #endregion
        
        #region Config
        
        static Configuration config;
        public class Configuration
        {
            [JsonProperty("Enable queue holding")]
            public bool eQueue = true;

            [JsonProperty("Queue holding timer")]
            public int queueTime = 300;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
                SaveConfig();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                PrintWarning("Creating new config file.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        
        protected override void SaveConfig() => Config.WriteObject(config);
        
        #endregion
    }
}
