using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeRecycler", "Nightshade", "3.2.1")]
    [Description("Standalone personal recycler and slot machine system for ShoppyStock. Gives disguised deployables that convert on placement.")]
    public class NightshadeRecycler : RustPlugin
    {
        private const string PermAdmin = "nightshaderecycler.admin";
        private const string PermPickup = "nightshaderecycler.pickup";

        private const string RecyclerItemShortname = "box.wooden.large";
        private const ulong RecyclerSkin = 1234567890;
        private const string RecyclerDisplayName = "Recycler";
        private const string RecyclerPrefab = "assets/bundled/prefabs/static/recycler_static.prefab";

        private const string SlotMachineItemShortname = "beehive";
        private const ulong SlotMachineSkin = 1234567891;
        private const string SlotMachineDisplayName = "Nightshade Slot Machine";
        private const string SlotMachinePrefab = "assets/prefabs/misc/casino/slotmachine/slotmachine.prefab";

        private const float PickupDistance = 4f;

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermPickup, this);
        }

        [ConsoleCommand("nightshaderecycler.give")]
        private void GiveRecyclerCommand(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg))
            {
                Reply(arg, "You do not have permission to use nightshaderecycler.give");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                Reply(arg, "Usage: nightshaderecycler.give <playername|steamid>");
                return;
            }

            string input = arg.Args[0].ToString();
            BasePlayer target = FindPlayer(input);

            if (target == null)
            {
                PrintWarning($"[NightshadeRecycler] Player not found: {input}");
                Reply(arg, $"Player not found: {input}");
                return;
            }

            if (GiveCustomItem(target, RecyclerItemShortname, RecyclerSkin, RecyclerDisplayName))
            {
                Puts($"[NightshadeRecycler] Gave recycler item to {target.displayName} ({target.UserIDString})");
                Reply(arg, $"Gave recycler item to {target.displayName}");
            }
            else
            {
                PrintError($"[NightshadeRecycler] Failed to give recycler item to {target.displayName} ({target.UserIDString})");
                Reply(arg, $"Failed to give recycler item to {target.displayName}");
            }
        }

        [ConsoleCommand("nightshadeslotmachine.give")]
        private void GiveSlotMachineCommand(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg))
            {
                Reply(arg, "You do not have permission to use nightshadeslotmachine.give");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                Reply(arg, "Usage: nightshadeslotmachine.give <playername|steamid>");
                return;
            }

            string input = arg.Args[0].ToString();
            BasePlayer target = FindPlayer(input);

            if (target == null)
            {
                PrintWarning($"[NightshadeRecycler] Player not found: {input}");
                Reply(arg, $"Player not found: {input}");
                return;
            }

            if (GiveCustomItem(target, SlotMachineItemShortname, SlotMachineSkin, SlotMachineDisplayName))
            {
                Puts($"[NightshadeRecycler] Gave slot machine item to {target.displayName} ({target.UserIDString})");
                Reply(arg, $"Gave slot machine item to {target.displayName}");
            }
            else
            {
                PrintError($"[NightshadeRecycler] Failed to give slot machine item to {target.displayName} ({target.UserIDString})");
                Reply(arg, $"Failed to give slot machine item to {target.displayName}");
            }
        }

        [ChatCommand("pickuprecycler")]
        private void PickupRecyclerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PermPickup) && !permission.UserHasPermission(player.UserIDString, PermAdmin) && !player.IsAdmin)
            {
                SendReply(player, "You do not have permission to pick up recyclers.");
                return;
            }

            BaseEntity entity = GetLookEntity(player, PickupDistance);

            if (entity == null || entity.IsDestroyed)
            {
                SendReply(player, "Look directly at your recycler and try again.");
                return;
            }

            if (!IsNightshadeRecycler(entity))
            {
                SendReply(player, "That is not a Nightshade recycler.");
                return;
            }

            if (entity.OwnerID != player.userID && !permission.UserHasPermission(player.UserIDString, PermAdmin) && !player.IsAdmin)
            {
                SendReply(player, "You can only pick up your own recycler.");
                return;
            }

            if (HasItemsInside(entity))
            {
                SendReply(player, "Empty the recycler before picking it up. No snack left behind.");
                return;
            }

            Vector3 position = entity.transform.position;

            entity.Kill();

            GiveCustomItem(player, RecyclerItemShortname, RecyclerSkin, RecyclerDisplayName);

            SendReply(player, "Recycler picked up.");
            Puts($"[NightshadeRecycler] {player.displayName} ({player.UserIDString}) picked up recycler at {position}");
        }

        [ChatCommand("pickupslotmachine")]
        private void PickupSlotMachineCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PermPickup) && !permission.UserHasPermission(player.UserIDString, PermAdmin) && !player.IsAdmin)
            {
                SendReply(player, "You do not have permission to pick up slot machines.");
                return;
            }

            BaseEntity entity = GetLookEntity(player, PickupDistance);

            if (entity == null || entity.IsDestroyed)
            {
                SendReply(player, "Look directly at your slot machine and try again.");
                return;
            }

            if (!IsNightshadeSlotMachine(entity))
            {
                SendReply(player, "That is not a Nightshade slot machine.");
                return;
            }

            if (entity.OwnerID != player.userID && !permission.UserHasPermission(player.UserIDString, PermAdmin) && !player.IsAdmin)
            {
                SendReply(player, "You can only pick up your own slot machine.");
                return;
            }

            Vector3 position = entity.transform.position;

            entity.Kill();

            GiveCustomItem(player, SlotMachineItemShortname, SlotMachineSkin, SlotMachineDisplayName);

            SendReply(player, "Slot machine picked up.");
            Puts($"[NightshadeRecycler] {player.displayName} ({player.UserIDString}) picked up slot machine at {position}");
        }

        private BaseEntity GetLookEntity(BasePlayer player, float distance)
        {
            RaycastHit hit;

            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, distance, -1, QueryTriggerInteraction.Ignore))
                return null;

            return hit.GetEntity();
        }

        private bool IsNightshadeRecycler(BaseEntity entity)
        {
            if (entity == null) return false;

            if (entity.PrefabName == RecyclerPrefab)
                return true;

            return entity.ShortPrefabName == "recycler_static" && entity.OwnerID != 0;
        }

        private bool IsNightshadeSlotMachine(BaseEntity entity)
        {
            if (entity == null) return false;

            if (entity.PrefabName == SlotMachinePrefab)
                return true;

            return entity.ShortPrefabName == "slotmachine" && entity.OwnerID != 0;
        }

        private bool HasItemsInside(BaseEntity entity)
        {
            StorageContainer storage = entity as StorageContainer;

            if (storage == null || storage.inventory == null)
                return false;

            return storage.inventory.itemList != null && storage.inventory.itemList.Count > 0;
        }

        private bool GiveCustomItem(BasePlayer player, string shortname, ulong skin, string displayName)
        {
            Item item = ItemManager.CreateByName(shortname, 1, skin);
            if (item == null)
            {
                PrintError($"[NightshadeRecycler] Failed to create item: {shortname}");
                return false;
            }

            item.name = displayName;

            if (player.inventory.GiveItem(item, player.inventory.containerMain))
                return true;

            if (player.inventory.GiveItem(item, player.inventory.containerBelt))
                return true;

            if (player.inventory.GiveItem(item, player.inventory.containerWear))
                return true;

            item.Drop(player.GetDropPosition(), player.GetDropVelocity());
            PrintWarning($"[NightshadeRecycler] Inventory full for {player.displayName}. Dropped {displayName} item on ground.");
            return true;
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            if (planner == null || go == null) return;

            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return;

            BaseEntity entity = go.ToBaseEntity();
            if (entity == null || entity.IsDestroyed) return;

            Item item = planner.GetItem();
            if (item == null || item.info == null) return;

            if (item.info.shortname == RecyclerItemShortname && item.skin == RecyclerSkin)
            {
                ConvertPlacedEntity(entity, player, RecyclerPrefab, "recycler");
                return;
            }

            if (item.info.shortname == SlotMachineItemShortname && item.skin == SlotMachineSkin)
            {
                ConvertPlacedEntity(entity, player, SlotMachinePrefab, "slot machine");
                return;
            }
        }

        private void ConvertPlacedEntity(BaseEntity entity, BasePlayer player, string prefab, string label)
        {
            NextTick(() =>
            {
                if (entity == null || entity.IsDestroyed) return;

                Vector3 position = entity.transform.position;
                Quaternion rotation = entity.transform.rotation;

                entity.Kill();

                BaseEntity newEntity = GameManager.server.CreateEntity(prefab, position, rotation);
                if (newEntity == null)
                {
                    PrintError($"[NightshadeRecycler] Failed to create {label} entity from prefab: {prefab}");
                    return;
                }

                newEntity.OwnerID = player.userID;
                newEntity.Spawn();

                BaseCombatEntity combatEntity = newEntity as BaseCombatEntity;
                if (combatEntity != null)
                {
                    combatEntity.health = combatEntity.MaxHealth();
                }

                Puts($"[NightshadeRecycler] Converted placed item into {label} for {player.displayName} ({player.UserIDString})");
            });
        }

        private BasePlayer FindPlayer(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            ulong steamId;
            if (ulong.TryParse(input, out steamId))
            {
                BasePlayer byId = BasePlayer.FindByID(steamId);
                if (byId != null) return byId;

                BasePlayer sleepingById = BasePlayer.FindSleeping(steamId);
                if (sleepingById != null) return sleepingById;
            }

            BasePlayer exact = BasePlayer.Find(input);
            if (exact != null) return exact;

            string lowered = input.ToLower();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player.displayName != null && player.displayName.ToLower().Contains(lowered))
                    return player;
            }

            foreach (BasePlayer player in BasePlayer.sleepingPlayerList)
            {
                if (player.displayName != null && player.displayName.ToLower().Contains(lowered))
                    return player;
            }

            return null;
        }

        private bool HasAccess(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null)
                return true;

            BasePlayer player = arg.Player();
            if (player == null)
                return false;

            if (player.IsAdmin)
                return true;

            return permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        private void Reply(ConsoleSystem.Arg arg, string message)
        {
            if (arg.Connection == null)
            {
                Puts(message);
                return;
            }

            SendReply(arg, message);
        }
    }
}