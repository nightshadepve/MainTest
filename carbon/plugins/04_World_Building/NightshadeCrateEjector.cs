using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NightshadeCrateEjector", "Nightshade", "1.0.0")]
    [Description("Ejects remaining loot from world crates/barrels on close and despawns them immediately for faster respawn.")]
    class NightshadeCrateEjector : RustPlugin
    {
        #region Config

        private PluginConfig config;

        private class PluginConfig
        {
            [JsonProperty("Excluded Prefab Keywords (partial match, case-insensitive)")]
            public List<string> ExcludedPrefabKeywords = new List<string>
            {
                "hackable",
                "supply_drop",
                "heli_crate",
                "bradley_crate",
                "codelockedhackablecrate"
            };

            [JsonProperty("Scatter Radius")]
            public float ScatterRadius = 1.25f;

            [JsonProperty("Throw Force")]
            public float ThrowForce = 1.5f;

            [JsonProperty("Delay Before Eject (seconds)")]
            public float EjectDelay = 0.15f;

            [JsonProperty("Debug Logging")]
            public bool Debug = false;
        }

        protected override void LoadDefaultConfig() => config = new PluginConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null) throw new System.Exception();
            }
            catch
            {
                PrintWarning("Config file corrupt or missing, creating new one.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        #region Hooks

        private readonly HashSet<ulong> pendingEject = new HashSet<ulong>();

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            var container = entity as LootContainer;
            if (container == null) return;
            if (container.IsDestroyed) return;
            if (IsExcluded(container)) return;

            ulong netId = container.net?.ID.Value ?? 0;
            if (netId != 0)
            {
                if (pendingEject.Contains(netId)) return;
                pendingEject.Add(netId);
            }

            timer.Once(config.EjectDelay, () =>
            {
                if (netId != 0) pendingEject.Remove(netId);
                EjectAndDespawn(container);
            });
        }

        #endregion

        #region Core

        private bool IsExcluded(LootContainer container)
        {
            string prefab = container.ShortPrefabName?.ToLower() ?? string.Empty;
            foreach (var keyword in config.ExcludedPrefabKeywords)
            {
                if (!string.IsNullOrEmpty(keyword) && prefab.Contains(keyword.ToLower()))
                    return true;
            }
            return false;
        }

        private void EjectAndDespawn(LootContainer container)
        {
            if (container == null || container.IsDestroyed) return;

            var inventory = container.inventory;
            int itemCount = inventory?.itemList?.Count ?? 0;

            if (inventory != null && itemCount > 0)
            {
                var items = inventory.itemList.ToList();
                foreach (var item in items)
                {
                    item.RemoveFromContainer();

                    Vector3 dropPos = container.transform.position
                        + new Vector3(Random.Range(-config.ScatterRadius, config.ScatterRadius),
                                      0.5f,
                                      Random.Range(-config.ScatterRadius, config.ScatterRadius));

                    Vector3 throwVel = new Vector3(
                        Random.Range(-1f, 1f),
                        Random.Range(0.5f, 1f),
                        Random.Range(-1f, 1f)) * config.ThrowForce;

                    item.Drop(dropPos, throwVel);
                }

                if (config.Debug)
                    Puts($"Ejected {items.Count} item(s) from {container.ShortPrefabName} at {container.transform.position}");
            }

            container.Kill();
        }

        #endregion
    }
}
