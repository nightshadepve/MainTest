using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Nightshade MLRS Truck", "Nightshade", "1.0.8")]
    [Description("Uses a repair bench placement token and replaces it with an MLRS truck.")]
    public class NightshadeMlrsTruck : RustPlugin
    {
        private const string PermUse = "nightshade.mlrstruck.use";
        private const string PermAdmin = "nightshade.mlrstruck.admin";

        private PluginConfig _config;
        private readonly HashSet<ulong> _processedEntities = new HashSet<ulong>();

        #region Config

        private class PluginConfig
        {
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#C8A2FF>[Nightshade MLRS]</color>";

            [JsonProperty("Placement item shortname")]
            public string PlacementItemShortname = "box.repair.bench";

            [JsonProperty("Placement item display name")]
            public string PlacementItemDisplayName = "MLRS Truck";

            [JsonProperty("Placement item skin ID - use a unique skin to avoid converting normal repair benches")]
            public ulong PlacementItemSkinId = 3442989910;

            [JsonProperty("Require matching skin ID to convert")]
            public bool RequireMatchingSkinId = true;

            [JsonProperty("MLRS prefab path")]
            public string MlrsPrefab = "assets/content/vehicles/mlrs/mlrs.entity.prefab";

            [JsonProperty("Only players with use permission can place MLRS truck")]
            public bool RequireUsePermission = true;

            [JsonProperty("Refund placement item if player lacks permission")]
            public bool RefundIfNoPermission = true;

            [JsonProperty("Set MLRS owner to player")]
            public bool SetOwner = true;

            [JsonProperty("Make MLRS persistent across normal server saves")]
            public bool EnableSaving = true;

            [JsonProperty("Spawn vertical offset")]
            public float SpawnYOffset = 0.00f;

            [JsonProperty("Spawn forward offset")]
            public float SpawnForwardOffset = 3.0f;

            [JsonProperty("Invert spawn direction")]
            public bool InvertSpawnDirection = true;

            [JsonProperty("Refresh MLRS network after spawn")]
            public bool RefreshNetworkAfterSpawn = true;

            [JsonProperty("Network refresh delay")]
            public float NetworkRefreshDelay = 0.25f;

            [JsonProperty("Chat message on successful placement")]
            public string PlacedMessage = "MLRS Truck placed.";

            [JsonProperty("Chat message when missing permission")]
            public string NoPermissionMessage = "You do not have permission to place an MLRS Truck.";

            [JsonProperty("Chat message when spawn fails")]
            public string SpawnFailedMessage = "MLRS placement failed. Please contact an admin.";
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();

                if (_config == null)
                    throw new Exception("Config was null");
            }
            catch
            {
                PrintWarning("Config is invalid; loading default config.");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            if (planner == null || gameObject == null)
                return;

            BasePlayer player = planner.GetOwnerPlayer();

            if (player == null)
                return;

            BaseEntity placedEntity = gameObject.ToBaseEntity();

            if (placedEntity == null || placedEntity.IsDestroyed)
                return;

            TryProcessPlacement(player, placedEntity);
        }

        #endregion

        #region Core

        private void TryProcessPlacement(BasePlayer player, BaseEntity placedEntity)
        {
            if (player == null || placedEntity == null || placedEntity.IsDestroyed)
                return;

            ulong entityId = placedEntity.net != null ? placedEntity.net.ID.Value : 0UL;

            if (entityId != 0UL && !_processedEntities.Add(entityId))
                return;

            if (!IsMlrsPlacementDeployable(placedEntity))
            {
                if (entityId != 0UL)
                    _processedEntities.Remove(entityId);

                return;
            }

            NextTick(() => ReplaceWithMlrs(player, placedEntity));
        }

        private bool IsMlrsPlacementDeployable(BaseEntity placedEntity)
        {
            if (placedEntity == null || placedEntity.IsDestroyed)
                return false;

            string prefabName = placedEntity.PrefabName ?? string.Empty;

            bool looksLikeRepairBench =
                ContainsIgnoreCase(prefabName, "repairbench") ||
                ContainsIgnoreCase(prefabName, "repair_bench") ||
                ContainsIgnoreCase(prefabName, "box.repair.bench");

            if (!looksLikeRepairBench)
                return false;

            if (_config.RequireMatchingSkinId && placedEntity.skinID != _config.PlacementItemSkinId)
                return false;

            return true;
        }

        private void ReplaceWithMlrs(BasePlayer player, BaseEntity tokenEntity)
        {
            if (player == null || tokenEntity == null || tokenEntity.IsDestroyed)
                return;

            if (_config.RequireUsePermission && !permission.UserHasPermission(player.UserIDString, PermUse))
            {
                if (_config.RefundIfNoPermission)
                    GivePlacementItem(player, 1);

                tokenEntity.Kill();
                Reply(player, _config.NoPermissionMessage);
                return;
            }

            Vector3 forward = _config.InvertSpawnDirection
                ? -tokenEntity.transform.forward
                : tokenEntity.transform.forward;

            Vector3 position = tokenEntity.transform.position
                               + (forward * _config.SpawnForwardOffset)
                               + new Vector3(0f, _config.SpawnYOffset, 0f);

            Quaternion rotation = tokenEntity.transform.rotation;

            tokenEntity.Kill();

            BaseEntity mlrs = GameManager.server.CreateEntity(_config.MlrsPrefab, position, rotation, true);

            if (mlrs == null)
            {
                GivePlacementItem(player, 1);
                Reply(player, _config.SpawnFailedMessage);
                PrintWarning($"Failed to create MLRS prefab: {_config.MlrsPrefab}");
                return;
            }

            if (_config.SetOwner)
                mlrs.OwnerID = player.userID;

            mlrs.enableSaving = _config.EnableSaving;
            mlrs.Spawn();

            if (_config.RefreshNetworkAfterSpawn)
            {
                timer.Once(_config.NetworkRefreshDelay, () =>
                {
                    if (mlrs == null || mlrs.IsDestroyed)
                        return;

                    mlrs.UpdateNetworkGroup();
                    mlrs.SendNetworkUpdateImmediate();
                });
            }

            Reply(player, _config.PlacedMessage);
        }

        private void GivePlacementItem(BasePlayer player, int amount)
        {
            if (player == null || amount <= 0)
                return;

            Item item = ItemManager.CreateByName(_config.PlacementItemShortname, amount, _config.PlacementItemSkinId);

            if (item == null)
            {
                Reply(player, $"Could not create placement item: {_config.PlacementItemShortname}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(_config.PlacementItemDisplayName))
                item.name = _config.PlacementItemDisplayName;

            if (!player.inventory.GiveItem(item))
                item.Drop(player.transform.position + Vector3.up, Vector3.zero);
        }

        #endregion

        #region Commands

        [ChatCommand("mlrstruck")]
        private void CmdMlrsTruck(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                Reply(player, "You do not have permission to use this command.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                Reply(player,
                    "Commands:\n" +
                    "<color=#C8A2FF>/mlrstruck give</color> - gives yourself 1 MLRS Truck placement item\n" +
                    "<color=#C8A2FF>/mlrstruck give PLAYER amount</color> - gives a player MLRS Truck placement item");

                return;
            }

            if (!args[0].Equals("give", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, "Unknown command. Use /mlrstruck give");
                return;
            }

            BasePlayer target = player;
            int amount = 1;

            if (args.Length >= 2)
            {
                target = FindPlayer(args[1]);

                if (target == null)
                {
                    Reply(player, $"Could not find player: {args[1]}");
                    return;
                }
            }

            if (args.Length >= 3 && (!int.TryParse(args[2], out amount) || amount <= 0))
            {
                Reply(player, "Amount must be a positive number.");
                return;
            }

            GivePlacementItem(target, amount);
            Reply(player, $"Gave {amount} MLRS Truck placement item(s) to {target.displayName}.");

            if (target != player)
                Reply(target, $"You received {amount} MLRS Truck placement item(s).");
        }

        [ConsoleCommand("mlrstruck.give")]
        private void ConsoleGive(ConsoleSystem.Arg arg)
        {
            if (arg == null)
                return;

            BasePlayer caller = arg.Player();

            if (caller != null && !permission.UserHasPermission(caller.UserIDString, PermAdmin))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: mlrstruck.give PLAYER amount");
                return;
            }

            string targetName = arg.Args[0].ToString();

            BasePlayer target = FindPlayer(targetName);

            if (target == null)
            {
                arg.ReplyWith($"Could not find player: {targetName}");
                return;
            }

            int amount = 1;
            string amountArg = arg.Args.Length >= 2 ? arg.Args[1].ToString() : null;

            if (!string.IsNullOrEmpty(amountArg) && (!int.TryParse(amountArg, out amount) || amount <= 0))
            {
                arg.ReplyWith("Amount must be a positive number.");
                return;
            }

            GivePlacementItem(target, amount);
            arg.ReplyWith($"Gave {amount} MLRS Truck placement item(s) to {target.displayName}.");
            Reply(target, $"You received {amount} MLRS Truck placement item(s).");
        }

        #endregion

        #region Helpers

        private bool ContainsIgnoreCase(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) &&
                   !string.IsNullOrEmpty(needle) &&
                   haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private BasePlayer FindPlayer(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId))
                return null;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                if (player.UserIDString == nameOrId)
                    return player;

                if (player.displayName != null &&
                    player.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return player;
            }

            foreach (BasePlayer player in BasePlayer.sleepingPlayerList)
            {
                if (player == null)
                    continue;

                if (player.UserIDString == nameOrId)
                    return player;

                if (player.displayName != null &&
                    player.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return player;
            }

            return null;
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message))
                return;

            player.ChatMessage($"{_config.ChatPrefix} {message}");
        }

        #endregion
    }
}