using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Craft Car Chassis", "WhiteThunder", "1.2.5")]
    [Description("Allows players to craft a modular car chassis at a car lift using a UI.")]
    internal class CraftChassis : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        private Plugin Economics, ServerRewards;

        private Configuration _config;

        private const string PermissionCraft2 = "craftchassis.2";
        private const string PermissionCraft3 = "craftchassis.3";
        private const string PermissionCraft4 = "craftchassis.4";
        private const string PermissionFree = "craftchassis.free";
        private const string PermissionFuel = "craftchassis.fuel";

        private const string ChassisPrefab2 = "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab";
        private const string ChassisPrefab3 = "assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab";
        private const string ChassisPrefab4 = "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab";
        private const string SpawnEffect = "assets/bundled/prefabs/fx/build/promote_toptier.prefab";

        private readonly Dictionary<BasePlayer, ModularCarGarage> playerLifts = new();
        private readonly ChassisUIManager uiManager = new();

        private enum CurrencyType { Items, Economics, ServerRewards }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionCraft2, this);
            permission.RegisterPermission(PermissionCraft3, this);
            permission.RegisterPermission(PermissionCraft4, this);
            permission.RegisterPermission(PermissionFree, this);
            permission.RegisterPermission(PermissionFuel, this);
        }

        private void Unload()
        {
            uiManager.DestroyAllUIs();
        }

        private void OnLootEntity(BasePlayer player, ModularCarGarage carLift)
        {
            if (carLift == null)
                return;

            if (carLift.carOccupant == null)
            {
                playerLifts.Add(player, carLift);
                uiManager.MaybeSendPlayerUI(this, player);
            }
            else
            {
                uiManager.DestroyPlayerUI(player);
            }
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            var player = inventory.baseEntity;
            if (player == null)
                return;

            playerLifts.Remove(player);
            uiManager.DestroyPlayerUI(player);
        }

        #endregion

        #region Commands

        [Command("craftchassis.ui")]
        private void CraftChassisUICommand(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer || args.Length < 1)
                return;

            if (!int.TryParse(args[0], out var numSockets))
                return;

            var maxAllowedSockets = GetMaxAllowedSockets(player);
            if (numSockets < 2 || numSockets > maxAllowedSockets)
                return;

            if (!CanPlayerCreateChassis(player, numSockets, out var chassisCost))
                return;

            var basePlayer = player.Object as BasePlayer;
            if (!playerLifts.TryGetValue(basePlayer, out var carLift) || carLift.carOccupant != null)
                return;

            var car = SpawnChassis(carLift, numSockets, basePlayer);
            if (car == null)
                return;

            if (_config.EnableEffects)
            {
                Effect.server.Run(SpawnEffect, car.transform.position);
            }

            if (chassisCost != null)
            {
                ChargePlayer(basePlayer, chassisCost);
            }
        }

        #endregion

        #region Helper Methods

        private ModularCar SpawnChassis(ModularCarGarage carLift, int numSockets, BasePlayer player)
        {
            var prefab = GetChassisPrefab(numSockets);

            var position = carLift.transform.position + Vector3.up * 0.7f;
            var rotation = Quaternion.Euler(0, carLift.transform.eulerAngles.y - 90, 0);

            var car = GameManager.server.CreateEntity(prefab, position, rotation) as ModularCar;
            if (car == null)
                return null;

            if (_config.SetOwner)
            {
                car.OwnerID = player.userID;
            }

            car.Spawn();
            AddOrRestoreFuel(car, player);

            return car;
        }

        private void AddOrRestoreFuel(ModularCar car, BasePlayer player)
        {
            var desiredFuelAmount = _config.FuelAmount;
            if (desiredFuelAmount == 0 || !permission.UserHasPermission(player.UserIDString, PermissionFuel))
                return;

            if (car.GetFuelSystem() is not EntityFuelSystem fuelSystem)
                return;

            var fuelContainer = fuelSystem.GetFuelContainer();
            if (desiredFuelAmount < 0)
            {
                desiredFuelAmount = fuelContainer.allowedItem.stackable;
            }

            var fuelItem = fuelContainer.inventory.FindItemByItemID(fuelContainer.allowedItem.itemid);
            if (fuelItem == null)
            {
                fuelContainer.inventory.AddItem(fuelContainer.allowedItem, desiredFuelAmount);
            }
            else if (fuelItem.amount < desiredFuelAmount)
            {
                fuelItem.amount = desiredFuelAmount;
                fuelItem.MarkDirty();
            }
        }

        private string GetChassisPrefab(int numSockets)
        {
            if (numSockets == 4)
                return ChassisPrefab4;

            if (numSockets == 3)
                return ChassisPrefab3;

            return ChassisPrefab2;
        }

        private int GetMaxAllowedSockets(IPlayer player)
        {
            if (player.HasPermission(PermissionCraft4))
                return 4;

            if (player.HasPermission(PermissionCraft3))
                return 3;

            if (player.HasPermission(PermissionCraft2))
                return 2;

            return 0;
        }

        private bool CanPlayerCreateChassis(IPlayer player, int numSockets, out ChassisCost chassisCost)
        {
            chassisCost = null;
            if (player.HasPermission(PermissionFree))
                return true;

            chassisCost = GetCostForSockets(numSockets);
            return CanPlayerAffordCost(player.Object as BasePlayer, chassisCost);
        }

        private bool CanPlayerAffordSockets(BasePlayer basePlayer, int sockets)
        {
            return CanPlayerAffordCost(basePlayer, GetCostForSockets(sockets));
        }

        private bool CanPlayerAffordCost(BasePlayer basePlayer, ChassisCost chassisCost)
        {
            return chassisCost.Amount == 0 || GetPlayerCurrencyAmount(basePlayer, chassisCost, out _) >= chassisCost.Amount;
        }

        private void ChargePlayer(BasePlayer basePlayer, ChassisCost chassisCost)
        {
            if (chassisCost.Amount == 0)
                return;

            if (chassisCost.UseEconomics && Economics != null)
            {
                Economics.Call("Withdraw", (ulong)basePlayer.userID, Convert.ToDouble(chassisCost.Amount));
                return;
            }

            if (chassisCost.UseServerRewards && ServerRewards != null)
            {
                ServerRewards.Call("TakePoints", (ulong)basePlayer.userID, chassisCost.Amount);
                return;
            }

            var itemid = ItemManager.itemDictionaryByName[chassisCost.ItemShortName].itemid;
            basePlayer.inventory.Take(null, itemid, chassisCost.Amount);
            basePlayer.Command("note.inv", itemid, -chassisCost.Amount);
        }

        private double GetPlayerCurrencyAmount(BasePlayer basePlayer, ChassisCost chassisCost, out CurrencyType currencyType)
        {
            if (chassisCost.UseEconomics && Economics != null)
            {
                var balance = Economics.Call("Balance", (ulong)basePlayer.userID);
                currencyType = CurrencyType.Economics;
                return balance as double? ?? 0;
            }

            if (chassisCost.UseServerRewards && ServerRewards != null)
            {
                var points = ServerRewards.Call("CheckPoints", (ulong)basePlayer.userID);
                currencyType = CurrencyType.ServerRewards;
                return points is int i ? i : 0;
            }

            currencyType = CurrencyType.Items;
            return basePlayer.inventory.GetAmount(ItemManager.itemDictionaryByName[chassisCost.ItemShortName].itemid);
        }

        private ChassisCost GetCostForSockets(int numSockets)
        {
            if (numSockets == 4)
                return _config.ChassisCostMap.ChassisCost4;

            if (numSockets == 3)
                return _config.ChassisCostMap.ChassisCost3;

            return _config.ChassisCostMap.ChassisCost2;
        }

        #endregion

        #region UI

        internal class ChassisUIManager
        {
            private const string PanelBackgroundColor = "1 0.96 0.88 0.15";
            private const string TextColor = "0.97 0.92 0.88 1";
            private const string DisabledLabelTextColor = "0.75 0.42 0.14 1";
            private const string ButtonColor = "0.44 0.54 0.26 1";
            private const string DisabledButtonColor = "0.25 0.32 0.19 0.7";

            private const string CraftChassisUIName = "CraftChassis";
            private const string CraftChassisUIHeaderName = "CraftChassis.Header";

            private readonly List<BasePlayer> PlayersWithUIs = new();

            public void DestroyAllUIs()
            {
                var playerList = new BasePlayer[PlayersWithUIs.Count];
                PlayersWithUIs.CopyTo(playerList, 0);

                foreach (var player in playerList)
                {
                    DestroyPlayerUI(player);
                }
            }

            public void DestroyPlayerUI(BasePlayer player)
            {
                if (PlayersWithUIs.Contains(player))
                {
                    CuiHelper.DestroyUi(player, CraftChassisUIName);
                    PlayersWithUIs.Remove(player);
                }
            }

            private CuiLabel CreateCostLabel(CraftChassis plugin, BasePlayer player, bool freeCrafting, int maxAllowedSockets, int numSockets)
            {
                var freeLabel = plugin.GetMessage(player.IPlayer, "UI.CostLabel.Free");

                var text = freeLabel;
                var color = TextColor;

                if (numSockets > maxAllowedSockets)
                {
                    text = plugin.GetMessage(player.IPlayer, "UI.CostLabel.NoPermission");
                    color = DisabledLabelTextColor;
                }
                else if (!freeCrafting)
                {
                    var chassisCost = plugin.GetCostForSockets(numSockets);
                    if (chassisCost.Amount > 0)
                    {
                        var playerCurrencyAmount = plugin.GetPlayerCurrencyAmount(player, chassisCost, out var currencyType);

                        switch (currencyType)
                        {
                            case CurrencyType.Economics:
                                text = plugin.GetMessage(player.IPlayer, "UI.CostLabel.Economics", chassisCost.Amount);
                                break;
                            case CurrencyType.ServerRewards:
                                text = plugin.GetMessage(player.IPlayer, "UI.CostLabel.ServerRewards", chassisCost.Amount);
                                break;
                            default:
                                var itemDefinition = ItemManager.itemDictionaryByName[chassisCost.ItemShortName];
                                text = $"{chassisCost.Amount} {itemDefinition.displayName.translated}";
                                break;
                        }

                        if (playerCurrencyAmount < chassisCost.Amount)
                            color = DisabledLabelTextColor;
                    }
                }

                var offsetMinX = 8 + (numSockets - 2) * 124;
                var offsetMaxX = 124 + (numSockets - 2) * 124;
                var offsetMinY = 43;
                var offsetMaxY = 58;

                return new CuiLabel
                {
                    Text =
                    {
                        Text = text,
                        Color = color,
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 11,
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = $"{offsetMinX} {offsetMinY}",
                        OffsetMax = $"{offsetMaxX} {offsetMaxY}",
                    },
                };
            }

            private CuiButton CreateCraftButton(CraftChassis plugin, BasePlayer player, bool freeCrafting, int maxAllowedSockets, int numSockets)
            {
                var color = ButtonColor;

                if (numSockets > maxAllowedSockets || !freeCrafting && !plugin.CanPlayerAffordSockets(player, numSockets))
                    color = DisabledButtonColor;

                var offsetMinX = 8 + (numSockets - 2) * 124;
                var offsetMaxX = 124 + (numSockets - 2) * 124;
                var offsetMinY = 8;
                var offsetMaxY = 40;

                return new CuiButton
                {
                    Text = {
                        Text = plugin.GetMessage(player.IPlayer, $"UI.ButtonText.Sockets.{numSockets}"),
                        Color = TextColor,
                        Align = TextAnchor.MiddleCenter,
                    },
                    Button =
                    {
                        Color = color,
                        Command = $"craftchassis.ui {numSockets}",
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = $"{offsetMinX} {offsetMinY}",
                        OffsetMax = $"{offsetMaxX} {offsetMaxY}",
                    },
                };
            }

            public void MaybeSendPlayerUI(CraftChassis plugin, BasePlayer player)
            {
                if (PlayersWithUIs.Contains(player))
                    return;

                var maxAllowedSockets = plugin.GetMaxAllowedSockets(player.IPlayer);
                if (maxAllowedSockets == 0)
                    return;

                var freeCrafting = player.IPlayer.HasPermission(PermissionFree);

                var cuiElements = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image = new CuiImageComponent { Color = PanelBackgroundColor },
                            RectTransform =
                            {
                                AnchorMin = "0.5 0",
                                AnchorMax = "0.5 0",
                                OffsetMin = "192.5 431",
                                OffsetMax = "572.5 495",
                            },
                        },
                        "Hud.Menu",
                        CraftChassisUIName
                    },
                    {
                        new CuiPanel
                        {
                            Image = new CuiImageComponent { Color = PanelBackgroundColor },
                            RectTransform =
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = "0 3",
                                OffsetMax = "380 24",
                            },
                        },
                        CraftChassisUIName,
                        CraftChassisUIHeaderName
                    },
                    {
                        new CuiLabel
                        {
                            RectTransform =
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                                OffsetMin = "10 0",
                                OffsetMax = "0 0",
                            },
                            Text =
                            {
                                Text = plugin.GetMessage(player.IPlayer, "UI.Header").ToUpperInvariant(),
                                Align = TextAnchor.MiddleLeft,
                                FontSize = 13,
                            },
                        },
                        CraftChassisUIHeaderName
                    },
                    { CreateCostLabel(plugin, player, freeCrafting, maxAllowedSockets, 2), CraftChassisUIName },
                    { CreateCraftButton(plugin, player, freeCrafting, maxAllowedSockets, 2), CraftChassisUIName },
                    { CreateCostLabel(plugin, player, freeCrafting, maxAllowedSockets, 3), CraftChassisUIName },
                    { CreateCraftButton(plugin, player, freeCrafting, maxAllowedSockets, 3), CraftChassisUIName },
                    { CreateCostLabel(plugin, player, freeCrafting, maxAllowedSockets, 4), CraftChassisUIName },
                    { CreateCraftButton(plugin, player, freeCrafting, maxAllowedSockets, 4), CraftChassisUIName },
                };

                CuiHelper.AddUi(player, cuiElements);
                PlayersWithUIs.Add(player);
            }
        }

        #endregion

        #region Configuration

        private class ChassisCost
        {
            [JsonProperty("Amount")]
            public int Amount;

            [JsonProperty("ItemShortName")]
            public string ItemShortName;

            [JsonProperty("UseEconomics")]
            public bool UseEconomics;

            [JsonProperty("UseServerRewards")]
            public bool UseServerRewards;
        }

        private class ChassisCostMap
        {
            [JsonProperty("2sockets")]
            public ChassisCost ChassisCost2 = new()
            {
                ItemShortName = "metal.fragments",
                Amount = 200,
            };

            [JsonProperty("3sockets")]
            public ChassisCost ChassisCost3 = new()
            {
                ItemShortName = "metal.fragments",
                Amount = 300,
            };

            [JsonProperty("4sockets")]
            public ChassisCost ChassisCost4 = new()
            {
                ItemShortName = "metal.fragments",
                Amount = 400,
            };
        }

        private class Configuration : BaseConfiguration
        {
            [JsonProperty("ChassisCost")]
            public ChassisCostMap ChassisCostMap = new();

            [JsonProperty("FuelAmount")]
            public int FuelAmount = 0;

            [JsonProperty("EnableEffects")]
            public bool EnableEffects = true;

            [JsonProperty("SetOwner")]
            public bool SetOwner = false;
        }

        private Configuration GetDefaultConfig() => new();

        #region Configuration Helpers

        private class BaseConfiguration
        {
            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public Dictionary<string, object> ToDictionary()
            {
                return JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
            }
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json)
            {
                return ToObject(JToken.Parse(json));
            }

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var currentDictValue = currentRawValue as Dictionary<string, object>;
                    if (currentWithDefaults[key] is Dictionary<string, object> defaultDictValue)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #endregion

        #region Localization

        private string GetMessage(IPlayer player, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, player.Id);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["UI.Header"] = "Craft a chassis",
                ["UI.CostLabel.Free"] = "Free",
                ["UI.CostLabel.NoPermission"] = "No Permission",
                ["UI.CostLabel.Economics"] = "{0:C}",
                ["UI.CostLabel.ServerRewards"] = "{0} reward points",
                ["UI.ButtonText.Sockets.2"] = "2 sockets",
                ["UI.ButtonText.Sockets.3"] = "3 sockets",
                ["UI.ButtonText.Sockets.4"] = "4 sockets",
            }, this, "en");
        }

        #endregion
    }
}
