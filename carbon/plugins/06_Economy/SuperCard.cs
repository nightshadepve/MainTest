using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
	[Info("Super Card", "Mevent", "1.0.7")]
	[Description("Open all doors")]
	public class SuperCard : CovalencePlugin
	{
		#region Config

		private Configuration _config;

		private class Configuration
		{
			[JsonProperty(PropertyName = "Command")]
			public string Cmd = "supercard.give";

			[JsonProperty(PropertyName = "Item settings")]
			public ItemConfig Item = new ItemConfig
			{
				DisplayName = "Super Card",
				ShortName = "keycard_red",
				SkinID = 1988408422,
				EnableBreak = true,
				LoseCondition = 1f
			};

			[JsonProperty(PropertyName = "Enable spawn?")]
			public bool EnableSpawn = true;

			[JsonProperty(PropertyName = "Drop Settings", ObjectCreationHandling = ObjectCreationHandling.Replace)]
			public List<DropInfo> Drop = new List<DropInfo>
			{
				new DropInfo
				{
					PrefabName = "assets/bundled/prefabs/radtown/crate_normal.prefab",
					MinAmount = 1,
					MaxAmount = 1,
					DropChance = 50
				},
				new DropInfo
				{
					PrefabName = "assets/bundled/prefabs/radtown/loot_barrel_2.prefab",
					MinAmount = 1,
					MaxAmount = 1,
					DropChance = 5
				},
				new DropInfo
				{
					PrefabName = "assets/bundled/prefabs/radtown/loot_barrel_1.prefab",
					MinAmount = 1,
					MaxAmount = 1,
					DropChance = 5
				}
			};
			
			[JsonProperty(PropertyName = "Use stacking hooks?")]
			public bool UseStackingHooks = true;
		}

		public class ItemConfig
		{
			[JsonProperty(PropertyName = "DisplayName")]
			public string DisplayName;

			[JsonProperty(PropertyName = "ShortName")]
			public string ShortName;

			[JsonProperty(PropertyName = "SkinID")]
			public ulong SkinID;

			[JsonProperty(PropertyName = "Enable breaking")]
			public bool EnableBreak;

			[JsonProperty(PropertyName = "Breaking the item (1 - standard)")]
			public float LoseCondition;

			public Item ToItem()
			{
				var newItem = ItemManager.CreateByName(ShortName, 1, SkinID);
				if (newItem == null)
				{
					Debug.LogError($"Error creating item with shortName '{ShortName}'!");
					return null;
				}

				if (!string.IsNullOrEmpty(DisplayName)) newItem.name = DisplayName;

				return newItem;
			}

			public bool IsSame(Item item)
			{
				return item != null && item.info.shortname == ShortName && item.skin == SkinID;
			}
		}

		public class DropInfo
		{
			[JsonProperty(PropertyName = "Object prefab name")]
			public string PrefabName;

			[JsonProperty(PropertyName = "Minimum item to drop")]
			public int MinAmount;

			[JsonProperty(PropertyName = "Maximum item to drop")]
			public int MaxAmount;

			[JsonProperty(PropertyName = "Item Drop Chance")]
			public float DropChance;
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				_config = Config.ReadObject<Configuration>();
				if (_config == null) throw new Exception();
				SaveConfig();
			}
			catch
			{
				PrintError("Your configuration file contains an error. Using default configuration values.");
				LoadDefaultConfig();
			}
		}

		protected override void SaveConfig()
		{
			Config.WriteObject(_config);
		}

		protected override void LoadDefaultConfig()
		{
			_config = new Configuration();
		}

		#endregion

		#region Hooks

		private void Init()
		{
			if (!_config.EnableSpawn) Unsubscribe(nameof(OnLootSpawn));

			if (!_config.UseStackingHooks)
			{
				Unsubscribe(nameof(CanCombineDroppedItem));
				Unsubscribe(nameof(CanStackItem));
			}

			AddCovalenceCommand(_config.Cmd, nameof(Cmd));
		}

		private object OnCardSwipe(CardReader cardReader, Keycard keyCard, BasePlayer player)
		{
			if (cardReader == null || keyCard == null || player == null) return null;

			var card = player.GetActiveItem();
			if (card == null || !_config.Item.IsSame(card) || card.conditionNormalized <= 0.0) return null;

			if (cardReader.IsOn()) return true;

			cardReader.Invoke(cardReader.GrantCard, 0.5f);

			if (_config.Item.EnableBreak)
			{
				var origCond = card.condition;

				card.condition -= _config.Item.LoseCondition;

				if (card.condition <= 0f && card.condition < origCond)
					card.OnBroken();
			}

			return true;
		}

		private void OnLootSpawn(LootContainer container)
		{
			if (container == null) return;

			var customItem = _config.Drop.Find(x => x.PrefabName.Contains(container.PrefabName));
			if (customItem == null || !(Random.Range(0f, 100f) <= customItem.DropChance)) return;

			timer.In(0.21f, () =>
			{
				if (container.inventory == null) return;

				var count = Random.Range(customItem.MinAmount, customItem.MaxAmount + 1);

				if (container.inventory.capacity <= container.inventory.itemList.Count)
					container.inventory.capacity = container.inventory.itemList.Count + count;

				for (var i = 0; i < count; i++)
				{
					var item = _config?.Item?.ToItem();
					if (item == null) break;

					item.MoveToContainer(container.inventory);
				}
			});
		}

		#region Split

		private object CanCombineDroppedItem(DroppedItem droppedItem, DroppedItem targetItem)
		{
			if (droppedItem == null || targetItem == null) return null;

			var item = droppedItem.GetItem();
			if (item == null) return null;

			var tItem = targetItem.GetItem();
			if (tItem == null || item.skin == tItem.skin) return null;

			return _config.Item.IsSame(item) || _config.Item.IsSame(tItem);
		}

		private object CanStackItem(Item item, Item targetItem)
		{
			if (item == null || targetItem == null || item.skin == targetItem.skin) return null;

			return item.info.shortname == targetItem.info.shortname &&
			       (item.skin == _config.Item.SkinID || targetItem.skin == _config.Item.SkinID) &&
			       item.skin == targetItem.skin
				? (object) (item.amount + targetItem.amount < item.info.stackable)
				: null;
		}

		#endregion

		#endregion

		#region Commands

		private void Cmd(IPlayer player, string command, string[] args)
		{
			if (!player.IsAdmin)
			{
				Reply(player, NoPermission);
				return;
			}

			if (args.Length == 0)
			{
				Reply(player, Syntax, _config.Cmd);
				return;
			}

			var target = players.FindPlayer(args[0])?.Object as BasePlayer;
			if (target == null)
			{
				Reply(player, NotFound, args[0]);
				return;
			}

			var item = _config?.Item?.ToItem();
			if (item == null) return;

			target.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
		}

		#endregion

		#region Lang

		private const string
			NotFound = "NotFound",
			Syntax = "Syntax",
			NoPermission = "NoPermission";


		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				[NotFound] = "We can't find player with that name/ID! {0}",
				[Syntax] = "Syntax: /{0} name/steamid",
				[NoPermission] = "You don't have permission to use this command!"
			}, this);
		}

		private void Reply(IPlayer player, string key, params object[] obj)
		{
			player.Reply(string.Format(lang.GetMessage(key, this, player.Id), obj));
		}

		#endregion
	}
}