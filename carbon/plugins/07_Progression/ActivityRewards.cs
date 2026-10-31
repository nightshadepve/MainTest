/*
*  < ----- End-User License Agreement ----->
*  
*  You may not copy, modify, merge, publish, distribute, sublicense, or sell copies of this software without the developer’s consent.
*
*  THIS SOFTWARE IS PROVIDED BY IIIaKa AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, 
*  THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS 
*  BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE 
*  GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT 
*  LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*
*  Developer: IIIaKa
*      https://t.me/iiiaka
*      Discord: @iiiaka
*      https://github.com/IIIaKa
*      https://umod.org/user/IIIaKa
*      https://codefling.com/iiiaka
*      https://lone.design/vendor/iiiaka/
*      https://www.patreon.com/iiiaka
*      https://boosty.to/iiiaka
*  Codefling plugin page: https://codefling.com/plugins/activity-rewards
*  Codefling license: https://codefling.com/plugins/activity-rewards?tab=downloads_field_4
*  
*  Lone.Design plugin page: https://lone.design/product/activity-rewards/
*
*  Copyright © 2024-2026 IIIaKa
*/

using HarmonyLib;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
	[Info("Activity Rewards", "IIIaKa", "0.1.9")]
	[Description("Plugin rewarding players for their in-game activity.")]
	class ActivityRewards : RustPlugin
	{
        [PluginReference]
		private Plugin ImageLibrary, ZoneManager, AdvancedStatus, MonumentsWatcher, AdvancedPanels;

        #region ~Variables~
		private static ActivityRewards Instance { get; set; }
		private Harmony _harmony;
		private const string IdForHarmony = "iiiaka.activityrewards";
		private bool _imgLibIsLoaded = false, _watcherIsLoaded = false, _statusIsLoaded = false;
		private const string NoteInv = "note.inv";
		private readonly string[] HttpScheme = new string[] { "http://", "https://" };
		private Timer _updateTimer, _clearTimer;
		private bool _isNight = false;
		private string[] _permNames;
		private static Dictionary<string, RewardData> _researchConfig, _openConfig;
		private Dictionary<string, RewardData> _gatherConfig, _killConfig, _pickupConfig, _plantingConfig, _fishingConfig, _miscConfig;
		private Dictionary<string, MonumentRewardData> _monumentConfig;
        private HashSet<string> _plugins = new HashSet<string>();
		private Hash<ulong, ulong> _patrolLastHit = new Hash<ulong, ulong>();
		
		[Flags]
		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public enum RewardCategories
        {
			None = 0,
			Gather = 1 << 0,
			Kill = 1 << 1,
			Research = 1 << 2,
			LootOpen = 1 << 3,
			Pickup = 1 << 4,
			Planting = 1 << 5,
			Fishing = 1 << 6,
			Monuments = 1 << 7,
			Misc = 1 << 8,
            All = Gather | Kill | Research | LootOpen | Pickup | Planting | Fishing | Monuments | Misc
		}
		#endregion

        #region ~Configuration~
        private static Configuration _config;

		private class Configuration
        {
			[JsonProperty(PropertyName = "List of language keys for creating language files")]
            public List<string> LanguageKeys;
			
			[JsonProperty(PropertyName = "Is it worth using the AdvancedStatus plugin?")]
            public bool AdvancedStatus_Enabled = true;
			
			[JsonProperty(PropertyName = "Advanced Status display method(SeparateBars, SingleBar, Cumulative)")]
            public StatusDisplayMode StatusDisplayMode = StatusDisplayMode.SeparateBars;
			
			[JsonProperty(PropertyName = "Enabled reward categories(Gather, Kill, Research, LootOpen, Pickup, Planting, Fishing, Monuments, Misc, All)")]
            public RewardCategories EnabledRewards = RewardCategories.All;
			
			[JsonProperty(PropertyName = "A prefix or suffix required in the player's nickname to receive the permission reward. An empty string disables the reward")]
            public string BonusName;
			
			[JsonProperty(PropertyName = "List of permissions granted when the player's nickname contains the required prefix or suffix")]
            public List<string> BonusName_Permissions;
			
			[JsonProperty(PropertyName = "List of reward multipliers for each permission")]
            public Dictionary<string, double> PermissionsList;
			
			[JsonProperty(PropertyName = "Night bonus multiplier. Will be added to the base reward multiplier. A value of 0 disables this")]
            public double NightMultiplier = 0.1;
			
			[JsonProperty(PropertyName = "The list of economy plugins for rewards")]
			public Dictionary<string, EcoPlugin> EcoPlugins;
			
			public int LastClearDay;
			
			public Oxide.Core.VersionNumber Version;
		}
		
		protected override void LoadConfig()
        {
			base.LoadConfig();
            try { _config = Config.ReadObject<Configuration>(); }
            catch (Exception ex) { PrintError($"{ex.Message}\n\n[{Title}] Your configuration file contains an error."); }
            if (_config == null || _config.Version == new VersionNumber())
            {
                PrintWarning("The configuration file is not found or contains errors. Creating a new one...");
                LoadDefaultConfig();
            }
            else if (_config.Version < Version)
            {
				PrintWarning($"Your configuration file version({_config.Version}) is outdated. Updating it to {Version}...");
                string cfgPath = $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}{Name}.json";
                if (File.Exists(cfgPath))
                    File.Move(cfgPath, $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}_old_{Name}({_config.Version}).json");
                _config.Version = Version;
                PrintWarning($"The configuration file has been successfully updated to version {_config.Version}!");
			}
			
			string langKey;
            var oldLangKeys = _config.LanguageKeys ?? new List<string>();
            _config.LanguageKeys = new List<string>() { "en" };
            for (int i = 0; i < oldLangKeys.Count; i++)
            {
                langKey = ToLangKey(oldLangKeys[i]);
                if (langKey != "ru" && !_config.LanguageKeys.Contains(langKey))
                    _config.LanguageKeys.Add(langKey);
            }
			
			_config.BonusName ??= "#RealPVE";
			if (_config.BonusName_Permissions == null || _config.BonusName_Permissions.Count < 1)
                _config.BonusName_Permissions = new List<string>() { "kits.loyalty" };
			_config.NightMultiplier = Math.Max(_config.NightMultiplier, 0d);
			_config.LastClearDay = Math.Clamp(_config.LastClearDay, 1, 31);
			
			_config.EcoPlugins ??= new Dictionary<string, EcoPlugin>();
			_config.EcoPlugins = _config.EcoPlugins.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			
			if (!_config.EcoPlugins.TryGetValue("Economics", out var economicsPlugin) || economicsPlugin == null)
				_config.EcoPlugins["Economics"] = new EcoPlugin("Economics", false);
			if (!_config.EcoPlugins.TryGetValue("ServerRewards", out var rewardPlugin) || rewardPlugin == null)
				_config.EcoPlugins["ServerRewards"] = new EcoPlugin("ServerRewards", true, "AddPoints", "TakePoints");
			if (!_config.EcoPlugins.TryGetValue("BankSystem", out var bankPlugin) || bankPlugin == null)
				_config.EcoPlugins["BankSystem"] = new EcoPlugin("BankSystem", true);
			if (!_config.EcoPlugins.TryGetValue("IQEconomic", out var iqEcoPlugin) || iqEcoPlugin == null)
				_config.EcoPlugins["IQEconomic"] = new EcoPlugin("IQEconomic", true, "API_SET_BALANCE", "API_REMOVE_BALANCE");
			
			string ecoName;
			EcoPlugin ecoPlugin;
			var list = new List<EcoPlugin>();
			foreach (var kvp in _config.EcoPlugins)
			{
				ecoName = kvp.Key;
				ecoPlugin = kvp.Value;
				if (ecoPlugin == null)
				{
					ecoPlugin = new EcoPlugin(ecoName, true);
					list.Add(ecoPlugin);
				}
				ecoPlugin.Name = ecoName;
				ecoPlugin.BarId = $"{Name}_{ecoPlugin.Name}";
                if (string.IsNullOrWhiteSpace(ecoPlugin.Text_Key))
                    ecoPlugin.Text_Key = $"Msg{ecoPlugin.Name}";
				if (string.IsNullOrWhiteSpace(ecoPlugin.Deposit))
					ecoPlugin.Deposit = "Deposit";
				if (string.IsNullOrWhiteSpace(ecoPlugin.Withdraw))
					ecoPlugin.Withdraw = "Withdraw";
				ecoPlugin.BarSettings ??= new BarSettings();
			}
			for (int i = 0; i < list.Count; i++)
			{
				ecoPlugin = list[i];
				_config.EcoPlugins[ecoPlugin.Name] = ecoPlugin;
            }
			SaveConfig();
        }
		
		protected override void SaveConfig() => Config.WriteObject(_config);
		protected override void LoadDefaultConfig() => _config = new Configuration() { Version = Version };
        #endregion
		
		#region ~DataFile~
        private static Dictionary<string, PlayerData> _playersData;

        public class PlayerData
        {
			public HashSet<string> MonumentList { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			
			[JsonIgnore] public double CachedMultiplier = -1d;
			[JsonIgnore] public Dictionary<string, (double, double)> LastReward;
            [JsonIgnore] public HashSet<string> MonumentsPermissions;
		}
		
		private bool TryGetPlayerData(string userID, out PlayerData result) => _playersData.TryGetValue(userID, out result) && result != null;
		private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _playersData);
        #endregion
		
		#region ~Language~
        protected override void LoadDefaultMessages()
        {
			var keys = _config.EcoPlugins.Select(kvp => kvp.Value.Text_Key).Where(key => !string.IsNullOrWhiteSpace(key)).ToHashSet();
			
			for (int i = 0; i < _config.LanguageKeys.Count; i++)
                lang.RegisterMessages(keys.ToDictionary(key => key, key => "Bonus"), this, _config.LanguageKeys[i]);
			lang.RegisterMessages(keys.ToDictionary(key => key, key => "Бонус"), this, "ru");
		}
        #endregion
		
		#region ~Permissions~
        void OnUserPermissionGranted(string userIDString, string permName)
        {
            if (_permNames.Contains(permName))
				UpdatePermissionLimits(userIDString);
		}
        void OnUserPermissionRevoked(string userIDString, string permName) => OnUserPermissionGranted(userIDString, permName);

        void OnUserGroupAdded(string userIDString, string groupName) => UpdatePermissionLimits(userIDString);
		void OnUserGroupRemoved(string userIDString, string groupName) => UpdatePermissionLimits(userIDString);
		
		void OnGroupPermissionGranted(string groupName, string permName)
        {
			if (!_permNames.Contains(permName))
				return;
			foreach (var userStr in permission.GetUsersInGroup(groupName))
				UpdatePermissionLimits(userStr.Substring(0, userStr.IndexOf('(')).Trim());
		}
        void OnGroupPermissionRevoked(string groupName, string permName) => OnGroupPermissionGranted(groupName, permName);

        void OnGroupDeleted(string groupName)
        {
			foreach (var userID in _playersData.Keys)
				UpdatePermissionLimits(userID);
		}
		
		private void UpdatePermissionLimits(string userID)
        {
			double multiplier = 0d, newMultiplier;
			foreach (var kvp in _config.PermissionsList)
            {
				newMultiplier = kvp.Value;
				if (newMultiplier > multiplier && permission.UserHasPermission(userID, kvp.Key))
					multiplier = newMultiplier;
            }
			if (!_playersData.TryGetValue(userID, out var playerData) || playerData == null)
				_playersData[userID] = playerData = new PlayerData();
			playerData.CachedMultiplier = multiplier;
		}
		#endregion

        #region ~Methods~
        private void ToggleImageLib(bool isLoaded)
        {
			if (!_config.AdvancedStatus_Enabled)
				return;
			
			_imgLibIsLoaded = isLoaded;
            if (_imgLibIsLoaded)
			{
				var imgList = new Dictionary<string, string>();
                foreach (var ecoPlugin in _config.EcoPlugins.Values)
                {
                    if (string.IsNullOrWhiteSpace(ecoPlugin.BarSettings.Image_Sprite) && string.IsNullOrWhiteSpace(ecoPlugin.BarSettings.Image_Local) && ecoPlugin.BarSettings.Image_Url.StartsWithAny(HttpScheme))
                        imgList.Add(ecoPlugin.BarId, ecoPlugin.BarSettings.Image_Url);
                }
                if (imgList.Any())
                    ImageLibrary?.Call("ImportImageList", Name, imgList, 0uL, true);
			}
			if (_statusIsLoaded)
            {
                foreach (var ecoPlugin in _config.EcoPlugins.Values)
                {
                    if (ecoPlugin.StatusBar != null)
                        ecoPlugin.SelectBarImage();
                }
            }
        }
		
		private void TogglePlugin(Plugin plugin, bool isLoad = false)
        {
			foreach (var ecoPlugin in _config.EcoPlugins.Values)
            {
                if (!ecoPlugin.IsEnabled || plugin.Name != ecoPlugin.Name)
					continue;
				ecoPlugin.Plugin = isLoad ? plugin : null;
                ecoPlugin.IsReady = ecoPlugin.Plugin != null;
				if (_config.AdvancedStatus_Enabled)
                {
					if (ecoPlugin.IsReady)
                    {
                        ecoPlugin.PrepareStatusBar();
                        ecoPlugin.SelectBarImage();
                    }
					else if (ecoPlugin.StatusBar != null)
						ecoPlugin.ClearStatusBar();
				}
				break;
			}
		}
		
		private void KillReward(BasePlayer player, PlayerData playerData, string shortName)
        {
			if (_killConfig.TryGetValue(shortName, out var rewData) && rewData != null)
			{
				GiveReward(player, playerData, rewData);
				return;
			}
			foreach (var kvp in _killConfig)
            {
				rewData = kvp.Value;
				if (shortName.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) && rewData != null)
                {
                    GiveReward(player, playerData, rewData);
                    return;
                }
            }
		}
		
		private void MonumentReward(BasePlayer player, PlayerData playerData, string monumentID)
        {
			if (!TryGetMonumentReward(monumentID, out var rewData))
				return;
			if (playerData.MonumentList.Add(monumentID))
				GiveReward(player, playerData, rewData);
			if (rewData.PermissionsList == null)
				return;
			playerData.MonumentsPermissions ??= new HashSet<string>();
			foreach (var permName in rewData.PermissionsList)
            {
				playerData.MonumentsPermissions.Add(permName);
				permission.GrantUserPermission(player.UserIDString, permName, null);
            }
		}
		
		private void GiveReward(BasePlayer player, PlayerData playerData, RewardData rewData)
        {
			int intAmount;
			object[] hookParams;
			ulong userID = player.userID.Get();
			double doubleAmount, currentTimestamp = Network.TimeEx.currentTimestamp, displayEnd = currentTimestamp + 4d, rewMult = playerData.CachedMultiplier;
			if (rewMult < 0d)
			{
				UpdatePermissionLimits(player.UserIDString);
				rewMult = playerData.CachedMultiplier;
            }
			bool isPositive, isNight = _isNight && _config.NightMultiplier > 0d;
			if (isNight)
				rewMult += _config.NightMultiplier;
			foreach (var ecoPlugin in _config.EcoPlugins.Values)
            {
				if (!ecoPlugin.IsReady)
					continue;
				hookParams = new object[2] { userID, null };
				if (ecoPlugin.IsRewardInt)
				{
					intAmount = (int)(rewData.PluginInt * rewMult);
					if (intAmount == 0)
						continue;
					hookParams[1] = intAmount;
					doubleAmount = intAmount;
				}
				else
                {
					doubleAmount = rewData.PluginDouble * rewMult;
					if (doubleAmount == 0d)
						continue;
					hookParams[1] = doubleAmount;
				}
				
				isPositive = doubleAmount > 0d;
				ecoPlugin.Plugin.Call(isPositive ? ecoPlugin.Deposit : ecoPlugin.Withdraw, hookParams);
				
				if (!_statusIsLoaded || !_config.AdvancedStatus_Enabled || ecoPlugin.StatusBar == null)
                {
                    if (isPositive)
                        player.SendConsoleCommand(NoteInv, 963906841, 1, $"<color=#A3FF00>+{doubleAmount} {lang.GetMessage(ecoPlugin.Text_Key, this, player.UserIDString)}</color>");
                    else
                        player.SendConsoleCommand(NoteInv, 963906841, 0, $"{doubleAmount} {lang.GetMessage(ecoPlugin.Text_Key, this, player.UserIDString)}");
                    continue;
                }

                if ((_config.StatusDisplayMode & StatusDisplayMode.Cumulative) != 0)
                {
                    playerData.LastReward ??= new Dictionary<string, (double, double)>();
                    if (!playerData.LastReward.TryGetValue(ecoPlugin.Name, out var lastReward))
                        lastReward = (0d, 0d);
                    if (currentTimestamp < lastReward.Item1)
                        doubleAmount += lastReward.Item2;
                    lastReward.Item1 = displayEnd;
                    lastReward.Item2 = doubleAmount;
                    playerData.LastReward[ecoPlugin.Name] = lastReward;
                }
                var parameters = new Dictionary<int, object>(ecoPlugin.StatusBar)
                {
                    { 15, lang.GetMessage(ecoPlugin.Text_Key, this, player.UserIDString) },
                    { 22, $"{(isPositive ? "+" : string.Empty)}{doubleAmount}" },
                    { 29, displayEnd }
                };
                if ((_config.StatusDisplayMode & StatusDisplayMode.SeparateBars) != 0)
                    parameters[0] = $"{ecoPlugin.BarId}_{currentTimestamp}";
                AdvancedStatus?.Call("CreateBar", userID, parameters);
            }
			
			if (rewData.ItemsList == null)
				return;
			foreach (var itemReward in rewData.ItemsList)
            {
				var item = ItemManager.Create(itemReward.ItemA.itemDef, (int)itemReward.ItemA.amount, itemReward.SkinId);
                if (item == null)
					continue;
				if (player.inventory.GiveItem(item))
					player.SendConsoleCommand(NoteInv, itemReward.ItemA.itemid, item.amount);
				else
					item.Drop(player.inventory.containerMain.dropPosition, player.inventory.containerMain.dropVelocity);
			}
		}
		
		private void PrepareRewardItems<T>(ICollection<T> list) where T : RewardData
		{
			RewardItem rewardItem;
			ItemDefinition itemDef;
			foreach (var rewData in list)
			{
				if (rewData.ItemsList == null)
					continue;
				for (int i = rewData.ItemsList.Count - 1; i >= 0; i--)
				{
					rewardItem = rewData.ItemsList[i];
					itemDef = ItemManager.FindItemDefinition(rewardItem.ShortName);
					if (itemDef == null)
                    {
						PrintError($"Failed to find an item with the name '{rewardItem.ShortName}'!");
						rewData.ItemsList.RemoveAt(i);
						continue;
					}
					rewardItem.Amount = Math.Max(rewardItem.Amount, 1);
					rewardItem.ItemA = new ItemAmount(itemDef, rewardItem.Amount);
				}
			}
		}
		
		private bool TryGetMonumentReward(string monumentID, out MonumentRewardData result)
        {
            if (!_monumentConfig.TryGetValue(monumentID, out result) || result == null)
            {
                foreach (var kvp in _monumentConfig)
                {
                    if (monumentID.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) && kvp.Value != null)
                    {
						result = kvp.Value;
						return true;
					}
                }
            }
            if (_monumentConfig.TryGetValue("default", out result) || result == null)
                return true;
			return false;
        }
		
		private void MonumentTimerTick()
        {
            int day = DateTime.Now.Day;
            bool shouldClear = day != _config.LastClearDay;
            if (shouldClear)
            {
                _config.LastClearDay = day;
                SaveConfig();
            }
            foreach (var playerData in _playersData.Values)
            {
                if (shouldClear || playerData.MonumentList == null || playerData.MonumentList.Count < 1)
                    playerData.MonumentList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                else
                    playerData.MonumentList = new HashSet<string>(playerData.MonumentList, StringComparer.OrdinalIgnoreCase);
            }
            _clearTimer = timer.Once((float)(DateTime.Today.AddDays(1) - DateTime.Now).TotalSeconds + 1f, () => { MonumentTimerTick(); });
        }
		
		private static string ToLangKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "en";
            int inputLength = input.Length;
            if (inputLength != 2 && inputLength != 5)
                return "en";

            string langKey;
            for (int i = 0; i < Translate.allServerLanguages.Count; i++)
            {
                langKey = Translate.allServerLanguages[i];
                if (langKey.Length != inputLength)
                    continue;

                int j;
                for (j = 0; j < inputLength; j++)
                {
                    if (langKey[j] != input[j] && char.ToUpperInvariant(langKey[j]) != char.ToUpperInvariant(input[j]))
                        break;
                }
                if (j == inputLength)
                    return langKey;
            }
            return "en";
        }
		
		private static bool TryLoadReward<T>(string filePath, out Dictionary<string, T> result)
        {
			try 
			{ 
				result = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, T>>(filePath);
				if (result != null && result.Any())
				{
					result = new Dictionary<string, T>(result, StringComparer.OrdinalIgnoreCase);
					return true;
				}
			}
			catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
			result = null;
			return false;
		}
		private static void SaveRewardConfig<T>(string path, Dictionary<string, T> obj) => Interface.Oxide.DataFileSystem.WriteObject(path, obj);
        #endregion

        #region ~Oxide Hooks~
		void OnPlayerConnected(BasePlayer player)
        {
			if (!player.userID.IsSteamId())
				return;
			if (!_playersData.TryGetValue(player.UserIDString, out var playerData) || playerData == null)
				_playersData[player.UserIDString] = playerData = new PlayerData();
			if (player.displayName.StartsWith(_config.BonusName, StringComparison.OrdinalIgnoreCase) || player.displayName.EndsWith(_config.BonusName, StringComparison.OrdinalIgnoreCase))
            {
				for (int i = 0; i < _config.BonusName_Permissions.Count; i++)
					permission.GrantUserPermission(player.UserIDString, _config.BonusName_Permissions[i], null);
			}
			if (_watcherIsLoaded && (_config.EnabledRewards & RewardCategories.Monuments) != 0)
            {
				using PooledList<string> monumentList = Facepunch.Pool.Get<PooledList<string>>();
				if (MonumentsWatcher?.Call("GetPlayerMonumentsNoAlloc", monumentList, player.userID) is bool)
				{
					for (int i = 0; i < monumentList.Count; i++)
                        MonumentReward(player, playerData, monumentList[i]);
				}
			}
		}
		
		void OnPlayerDisconnected(BasePlayer player)
        {
			if (!TryGetPlayerData(player.UserIDString, out var playerData))
				return;
			for (int i = 0; i < _config.BonusName_Permissions.Count; i++)
                permission.RevokeUserPermission(player.UserIDString, _config.BonusName_Permissions[i]);
			if (playerData.MonumentsPermissions != null && playerData.MonumentsPermissions.Count > 0)
			{
				foreach (var permName in playerData.MonumentsPermissions.ToArray())
				{
                    playerData.MonumentsPermissions.Remove(permName);
                    permission.RevokeUserPermission(player.UserIDString, permName);
                }
			}
			playerData.LastReward = null;
			playerData.MonumentsPermissions = null;
		}
		
		void OnUserNameUpdated(string userID, string oldName, string newName)
        {
			if (!newName.StartsWith(_config.BonusName, StringComparison.OrdinalIgnoreCase) && !newName.EndsWith(_config.BonusName, StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < _config.BonusName_Permissions.Count; i++)
                    permission.RevokeUserPermission(userID, _config.BonusName_Permissions[i]);
            }
		}
		
		void OnPatrolHelicopterTakeDamage(PatrolHelicopter patrol)
        {
            if (patrol.lastAttacker is BasePlayer attacker && attacker != null && attacker.userID.IsSteamId())
                _patrolLastHit[patrol.net.ID.Value] = attacker.userID;
        }
		
		void OnItemDropped(Item item, DroppedItem droppedItem)
        {
            if (item != null && droppedItem != null && droppedItem.DroppedBy == 0uL && item.parent == null && droppedItem.DropReason == DroppedItem.DropReasonEnum.Unknown)
                droppedItem.DroppedBy++;
        }
		
		void OnMonumentsWatcherLoaded() => _watcherIsLoaded = true;
		
		void OnAdvancedStatusLoaded()
		{
			_statusIsLoaded = true;
			var imgList = new List<string>();
            foreach (var ecoPlugin in _config.EcoPlugins.Values)
            {
				if (!string.IsNullOrWhiteSpace(ecoPlugin.BarSettings.Image_Local))
                    imgList.Add(ecoPlugin.BarSettings.Image_Local);
			}
			if (imgList.Any())
                AdvancedStatus?.Call("LoadImages", imgList);
		}
		
		void OnPluginLoaded(Plugin plugin)
        {
			if (plugin == ImageLibrary)
				ToggleImageLib(true);
			else if (_plugins.Contains(plugin.Name))
				TogglePlugin(plugin, true);
		}
		
		void OnPluginUnloaded(Plugin plugin)
		{
			if (plugin.Name == "ImageLibrary")
				ToggleImageLib(false);
			else if (plugin.Name == "MonumentsWatcher")
			{
                _watcherIsLoaded = false;
                PlayerData playerData;
                foreach (var player in BasePlayer.activePlayerList)
                {
					if (!TryGetPlayerData(player.UserIDString, out playerData) || playerData.MonumentsPermissions == null || playerData.MonumentsPermissions.Count < 1)
						continue;
					foreach (var permName in playerData.MonumentsPermissions.ToArray())
                    {
                        playerData.MonumentsPermissions.Remove(permName);
                        permission.RevokeUserPermission(player.UserIDString, permName);
                    }
				}
            }
			else if (plugin.Name == "AdvancedStatus")
				_statusIsLoaded = false;
			else if (_plugins.Contains(plugin.Name))
				TogglePlugin(plugin);
		}
		
		void Init()
        {
			Unsubscribe(nameof(OnPluginLoaded));
            Unsubscribe(nameof(OnPluginUnloaded));
			Unsubscribe(nameof(OnUserPermissionGranted));
            Unsubscribe(nameof(OnUserPermissionRevoked));
            Unsubscribe(nameof(OnUserGroupAdded));
            Unsubscribe(nameof(OnUserGroupRemoved));
            Unsubscribe(nameof(OnGroupPermissionGranted));
            Unsubscribe(nameof(OnGroupPermissionRevoked));
            Unsubscribe(nameof(OnGroupDeleted));
			Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPlayerDisconnected));
			Unsubscribe(nameof(OnUserNameUpdated));
			Unsubscribe(nameof(OnDispenserBonusReceived));
			Unsubscribe(nameof(OnEntityDeath));
			Unsubscribe(nameof(OnPatrolHelicopterTakeDamage));
			Unsubscribe(nameof(OnCollectiblePickup));
			Unsubscribe(nameof(OnGrowableGathered));
			Unsubscribe(nameof(OnItemPickup));
			Unsubscribe(nameof(OnItemDropped));
			Unsubscribe(nameof(OnEntitySpawned));
			Unsubscribe(nameof(OnFishCaught));
			Unsubscribe(nameof(OnMonumentsWatcherLoaded));
			Unsubscribe(nameof(OnPlayerEnteredMonument));
			Unsubscribe(nameof(OnPlayerExitedMonument));
			Unsubscribe(nameof(OnAdvancedStatusLoaded));
			Instance = this;
			try { _playersData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, PlayerData>>(Name); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
            if (_playersData == null)
                _playersData = new Dictionary<string, PlayerData>();
            else
                _playersData = _playersData.Where(kvp => kvp.Key.IsSteamId()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}
		
		void OnServerInitialized(bool initial)
        {
			string permName;
			var tempDict = _config.PermissionsList != null ? _config.PermissionsList.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value) : new Dictionary<string, double>();
			tempDict.TryAdd("activityrewards.default", 1d);
			tempDict.TryAdd("realpve.default", 1d);
			tempDict.TryAdd("realpve.vip", 1.1d);
			_config.PermissionsList = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
			foreach (var kvp in tempDict)
			{
				permName = kvp.Key;
				if (permName.StartsWith("activityrewards", StringComparison.OrdinalIgnoreCase))
				{
					permName = permName.Replace(" ", ".").Replace("_", ".").ToLower();
					permission.RegisterPermission(permName, this);
                }
				_config.PermissionsList[permName] = Math.Max(kvp.Value, double.Epsilon);
			}
			_permNames = _config.PermissionsList.Select(kvp => kvp.Key).ToArray();
			
			foreach (var ecoPlugin in _config.EcoPlugins.Values)
            {
                if (!ecoPlugin.IsEnabled)
					continue;
				ecoPlugin.Plugin = Manager.GetPlugin(ecoPlugin.Name);
                ecoPlugin.IsReady = ecoPlugin.Plugin != null;
				if (ecoPlugin.IsReady && _config.AdvancedStatus_Enabled)
                {
                    ecoPlugin.PrepareStatusBar();
                    ecoPlugin.SelectBarImage();
                }
				_plugins.Add(ecoPlugin.Name);
			}
			
			var subList = new HashSet<string>();
			if (!string.IsNullOrWhiteSpace(_config.BonusName))
			{
				for (int i = 0; i < _config.BonusName_Permissions.Count; i++)
                {
					permName = _config.BonusName_Permissions[i];
					if (permName.StartsWith("activityrewards", StringComparison.OrdinalIgnoreCase))
                    {
						_config.BonusName_Permissions[i] = permName = permName.Replace(" ", ".").Replace("_", ".").ToLower();
						if (!permission.PermissionExists(permName))
							permission.RegisterPermission(permName, this);
					}
                }
				subList.Add(nameof(OnUserNameUpdated));
			}
			SaveConfig();
			
			_harmony = new Harmony(IdForHarmony);
			string mainPath = $"{Name}{Path.DirectorySeparatorChar}",
				barPath = $"{mainPath}BarConfig",
				gatherPath = $"{mainPath}GatherRewards",
				killPath = $"{mainPath}KillRewards",
				researchPath = $"{mainPath}ResearchRewards",
				openPath = $"{mainPath}FirstLootOpenRewards",
				pickupPath = $"{mainPath}PickupRewards",
				plantingPath = $"{mainPath}PlantingRewards",
				fishingPath = $"{mainPath}FishingRewards",
				monumentsPath = $"{mainPath}MonumentRewards",
				miscPath = $"{mainPath}MiscRewards";
			if ((_config.EnabledRewards & RewardCategories.Gather) != 0)
			{
				if (!TryLoadReward(gatherPath, out _gatherConfig))
				{
					_gatherConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase) { { "wood", new RewardData(5, 0.5d) }, { "stones", new RewardData(10, 1d) }, { "metal.ore", new RewardData(15, 1.5d) }, { "sulfur.ore", new RewardData(20, 2d) } };
					SaveRewardConfig(gatherPath, _gatherConfig);
                }
				PrepareRewardItems(_gatherConfig.Values);
				subList.Add(nameof(OnDispenserBonusReceived));
			}
			if ((_config.EnabledRewards & RewardCategories.Kill) != 0)
            {
				if (!TryLoadReward(killPath, out _killConfig))
                {
                    _killConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase)
					{
						{ "dead_log_", new RewardData(2, 0.25d) }, { "loot-barrel", new RewardData(5, 0.5d) }, { "loot_barrel", new RewardData(5, 0.5d) }, { "oil_barrel", new RewardData(10, 1d) }, { "roadsign", new RewardData(5, 0.5d) },
						{ "beehive.natural", new RewardData(5, 0.5d) }, { "chicken", new RewardData(5, 0.5d) }, { "simplechicken.entity", new RewardData(5, 0.5d) }, { "snake.entity", new RewardData(10, 1d) }, { "boar", new RewardData(10, 1d) },
						{ "stag", new RewardData(15, 1.5d) }, { "ridablehorse", new RewardData(15, 1.5d) }, { "wolf", new RewardData(20, 2d) }, { "panther", new RewardData(25, 2.5d) }, { "tiger", new RewardData(25, 2.5d) },
						{ "bear", new RewardData(25, 2.5d) }, { "polarbear", new RewardData(25, 2.5d) }, { "crocodile", new RewardData(25, 2.5d) }, { "simpleshark", new RewardData(30, 3d) }, { "chicken.corpse", new RewardData(3, 0.25d) },
						{ "boar.corpse", new RewardData(5, 0.5d) }, { "stag.corpse", new RewardData(7, 0.75d) }, { "horse.corpse", new RewardData(7, 0.75d) }, { "wolf.corpse", new RewardData(15, 1.5d) }, { "panther.corpse", new RewardData(15, 1.5d) },
						{ "tiger.corpse", new RewardData(15, 1.5d) }, { "bear.corpse", new RewardData(15, 1.5d) }, { "polarbear.corpse", new RewardData(15, 1.5d) }, { "crocodile.corpse", new RewardData(15, 1.5d) }, { "shark.corpse", new RewardData(20, 2d) },
						{ "suicide", new RewardData(-5, -0.5d) }, { "player", new RewardData(10, 1d) }, { "scientistnpc", new RewardData(15, 1.5d) }, { "npc_tunneldweller", new RewardData(15, 1.5d) }, { "npc_underwaterdweller", new RewardData(15, 1.5d) },
                        { "scientistnpc_junkpile_pistol", new RewardData(10, 1d) }, { "waterwell_shopkeeper", new RewardData(15, 1.5d) }, { "scientistnpc_heavy", new RewardData(20, 2d) }, { "gingerbread_dungeon", new RewardData(15, 1.5d) }, { "gingerbread_meleedungeon", new RewardData(15, 1.5d) },
						{ "scarecrow", new RewardData(15, 1.5d) }, { "scarecrow_dungeon", new RewardData(15, 1.5d) }, { "scarecrow_dungeonnoroam", new RewardData(15, 1.5d) }, { "scientist_corpse", new RewardData(7, 0.75d) }, { "gingerbread_corpse", new RewardData(7, 0.75d) },
						{ "murderer_corpse", new RewardData(7, 0.75d) }, { "patrolhelicopter", new RewardData(100, 10d) }, { "bradleyapc", new RewardData(100, 10d) }, { "beartrap", new RewardData(0, 0d) }
					};
                    SaveRewardConfig(killPath, _killConfig);
                }
				PrepareRewardItems(_killConfig.Values);
				subList.Add(nameof(OnEntityDeath));
				subList.Add(nameof(OnPatrolHelicopterTakeDamage));
            }
			if ((_config.EnabledRewards & RewardCategories.Research) != 0)
            {
				if (!TryLoadReward(researchPath, out _researchConfig))
					_researchConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase);
				var list = ItemManager.GetItemDefinitions().Where(itemDef => itemDef.Blueprint != null && itemDef.Blueprint.isResearchable && !itemDef.Blueprint.defaultBlueprint)
					.OrderBy(itemDef => itemDef.Blueprint.workbenchLevelRequired).ToList();
				ItemDefinition itemDef;
				for (int i = 0; i < list.Count; i++)
                {
					itemDef = list[i];
					if (!_researchConfig.TryGetValue(itemDef.shortname, out var rewData) || rewData == null)
					{
						int scrapCost = Workbench.ScrapForResearch(itemDef, itemDef.Blueprint.workbenchLevelRequired, out int tax);
						_researchConfig[itemDef.shortname] = new RewardData(scrapCost / 10, scrapCost / 100.0d);
					}
				}
				SaveRewardConfig(researchPath, _researchConfig);
				
				_harmony.Patch(AccessTools.Method(typeof(PlayerBlueprints), "Unlock", new Type[] { typeof(ItemDefinition) }), prefix: new HarmonyMethod(typeof(ActivityRewards), nameof(ModUnlock)));
				PrepareRewardItems(_researchConfig.Values);
			}
			if ((_config.EnabledRewards & RewardCategories.LootOpen) != 0)
			{
				if (!TryLoadReward(openPath, out _openConfig))
                {
					_openConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase)
					{
						{ "foodbox", new RewardData(5, 0.5d) }, { "crate_food_1", new RewardData(5, 0.5d) }, { "crate_food_2", new RewardData(5, 0.5d) }, { "crate_shore", new RewardData(5, 0.5d) },
						{ "food_cache_001", new RewardData(10, 1d) }, { "food_cache_002", new RewardData(10, 1d) }, { "food_cache_003", new RewardData(10, 1d) }, { "food_cache_004", new RewardData(10, 1d) }, { "food_cache_005", new RewardData(10, 1d) },
						{ "crate_normal_2_food", new RewardData(10, 1d) }, { "wagon_crate_normal_2_food", new RewardData(10, 1d) }, { "crate_normal_2_medical", new RewardData(10, 1d) }, { "wagon_crate_normal_2_medical", new RewardData(10, 1d) }, { "vehicle_parts", new RewardData(5, 0.5d) },
						{ "crate_basic", new RewardData(5, 0.5d) }, { "crate_basic_jungle", new RewardData(5, 0.5d) }, { "crate_cannons", new RewardData(5, 0.5d) }, { "crate_normal_2", new RewardData(10, 1d) }, { "wagon_crate_normal_2", new RewardData(10, 1d) },
						{ "crate_mine", new RewardData(10, 1d) }, { "crate_tools", new RewardData(15, 1.5d) }, { "crate_tools_tutorial", new RewardData(15, 1.5d) }, { "crate_normal", new RewardData(20, 2d) }, { "wagon_crate_normal", new RewardData(20, 2d) },
						{ "crate_elite", new RewardData(25, 2.5d) }, { "crate_elite_tutorial", new RewardData(25, 2.5d) }, { "crate_underwater_basic", new RewardData(5, 0.5d) }, { "crate_underwater_advanced", new RewardData(10, 1d) }, { "crate_medical", new RewardData(5, 0.5d) },
						{ "crate_fuel", new RewardData(10, 1d) }, { "crate_ammunition", new RewardData(10, 1d) }, { "heli_crate", new RewardData(30, 3d) }, { "bradley_crate", new RewardData(30, 3d) },
						{ "codelockedhackablecrate", new RewardData(100, 10d) }, { "codelockedhackablecrate_oilrig", new RewardData(100, 10d) }, { "codelockedhackablecrate_ghostship", new RewardData(100, 10d) }
					};
                    SaveRewardConfig(openPath, _openConfig);
                }
				PrepareRewardItems(_openConfig.Values);
				_harmony.Patch(AccessTools.Method(typeof(Facepunch.Rust.Analytics.Azure), "OnFirstLooted", new Type[] { typeof(BaseEntity), typeof(BasePlayer) }), postfix: new HarmonyMethod(typeof(ActivityRewards), nameof(ModFirstLoot)));
			}
            if ((_config.EnabledRewards & RewardCategories.Pickup) != 0)
            {
				if (!TryLoadReward(pickupPath, out _pickupConfig))
                {
                    _pickupConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase)
					{
						{ "Wood", new RewardData(1, 0.1d) }, { "Stone", new RewardData(2, 0.25d) }, { "Metal Ore", new RewardData(5, 0.5d) }, { "Sulfur Ore", new RewardData(7, 0.75d) }, { "Diesel Fuel", new RewardData(10, 1d) },
						{ "Bones", new RewardData(1, 0.1d) }, { "Green Keycard", new RewardData(10, 1d) }, { "Blue Keycard", new RewardData(20, 2d) }, { "Red Keycard", new RewardData(30, 3d) }, { "Hemp Fibers", new RewardData(1, 0.1d) },
						{ "Corn", new RewardData(1, 0.1d) }, { "Potato", new RewardData(1, 0.1d) }, { "Pumpkin", new RewardData(1, 0.1d) }, { "Wild Mushroom", new RewardData(1, 0.1d) }, { "Black Berry", new RewardData(1, 0.1d) },
						{ "Blue Berry", new RewardData(1, 0.1d) }, { "Green Berry", new RewardData(1, 0.1d) }, { "Red Berry", new RewardData(1, 0.1d) }, { "White Berry", new RewardData(1, 0.1d) }, { "Yellow Berry", new RewardData(1, 0.1d) },
						{ "Orchid", new RewardData(1, 0.1d) }, { "Rose", new RewardData(1, 0.1d) }, { "Sunflower", new RewardData(1, 0.1d) }, { "Wheat", new RewardData(1, 0.1d) }
					};
                    SaveRewardConfig(pickupPath, _pickupConfig);
                }
				PrepareRewardItems(_pickupConfig.Values);
				subList.Add(nameof(OnCollectiblePickup));
				subList.Add(nameof(OnGrowableGathered));
				subList.Add(nameof(OnItemPickup));
				subList.Add(nameof(OnItemDropped));
			}
			if ((_config.EnabledRewards & RewardCategories.Planting) != 0)
			{
				if (!TryLoadReward(plantingPath, out _plantingConfig))
                {
                    _plantingConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase)
					{
						{ "hemp.entity", new RewardData(1, 0.1d) }, { "corn.entity", new RewardData(1, 0.1d) }, { "pumpkin.entity", new RewardData(1, 0.1d) }, { "potato.entity", new RewardData(1, 0.1d) }, { "black_berry.entity", new RewardData(1, 0.1d) },
						{ "blue_berry.entity", new RewardData(1, 0.1d) }, { "green_berry.entity", new RewardData(1, 0.1d) }, { "red_berry.entity", new RewardData(1, 0.1d) }, { "white_berry.entity", new RewardData(1, 0.1d) }, { "yellow_berry.entity", new RewardData(1, 0.1d) },
						{ "orchid.entity", new RewardData(1, 0.1d) }, { "rose.entity", new RewardData(1, 0.1d) }, { "sunflower.entity", new RewardData(1, 0.1d) }, { "wheat.entity", new RewardData(1, 0.1d) }
					};
                    SaveRewardConfig(plantingPath, _plantingConfig);
                }
				PrepareRewardItems(_plantingConfig.Values);
				subList.Add(nameof(OnEntitySpawned));
			}
			if ((_config.EnabledRewards & RewardCategories.Fishing) != 0)
			{
				if (!TryLoadReward(fishingPath, out _fishingConfig))
                {
                    _fishingConfig = new Dictionary<string, RewardData>(StringComparer.OrdinalIgnoreCase)
					{
						{ "fish.minnows", new RewardData(1, 0.1d) }, { "fish.anchovy", new RewardData(3, 0.3d) }, { "fish.herring", new RewardData(3, 0.3d) }, { "fish.sardine", new RewardData(3, 0.3d) }, { "fish.troutsmall", new RewardData(5, 0.5d) },
						{ "fish.yellowperch", new RewardData(5, 0.5d) }, { "fish.salmon", new RewardData(10, 1d) }, { "fish.catfish", new RewardData(10, 1d) }, { "fish.orangeroughy", new RewardData(10, 1d) },
						{ "fish.smallshark", new RewardData(50, 5d, new List<RewardItem>() { new RewardItem("scrap", 10) }) }
					};
                    SaveRewardConfig(fishingPath, _fishingConfig);
                }
				PrepareRewardItems(_fishingConfig.Values);
				subList.Add(nameof(OnFishCaught));
            }
			if ((_config.EnabledRewards & RewardCategories.Monuments) != 0)
            {
				if (!TryLoadReward(monumentsPath, out _monumentConfig))
                {
					_monumentConfig = new Dictionary<string, MonumentRewardData>(StringComparer.OrdinalIgnoreCase)
                    {
						{ "default", new MonumentRewardData(3, 0.3d) }, { "cave_small", new MonumentRewardData(5, 0.5d) }, { "cave_medium", new MonumentRewardData(8, 0.75d) }, { "cave_large", new MonumentRewardData(10, 1d) }, { "jungle_ruins", new MonumentRewardData(8, 0.75d) },
						{ "bandit_town", new MonumentRewardData(10, 1d) }, { "compound", new MonumentRewardData(10, 1d) }, { "apartments_complex_1", new MonumentRewardData(10, 1d) }, { "deepsea_floatingcity", new MonumentRewardData(10, 1d) }, { "deepsea_island_tropical", new MonumentRewardData(10, 1d) },
						{ "gas_station_1", new MonumentRewardData(10, 1d) }, { "lighthouse", new MonumentRewardData(10, 1d) }, { "supermarket_1", new MonumentRewardData(10, 1d) }, { "warehouse", new MonumentRewardData(10, 1d) },  { "ghostship", new MonumentRewardData(10, 1d) },
						{ "harbor_2", new MonumentRewardData(10, 1d) }, { "harbor_1", new MonumentRewardData(15, 1.5d) }, { "ferry_terminal_1", new MonumentRewardData(15, 1.5d) }, { "jungle_ziggurat_a", new MonumentRewardData(15, 1.5d) },  { "junkyard_1", new MonumentRewardData(15, 1.5d) },
						{ "radtown_1", new MonumentRewardData(15, 1.5d) }, { "radtown_small_3", new MonumentRewardData(15, 1.5d) }, { "satellite_dish", new MonumentRewardData(15, 1.5d) }, { "water_treatment_plant_1", new MonumentRewardData(15, 1.5d) },  { "airfield_1", new MonumentRewardData(20, 2d) },
						{ "arctic_research_base_a", new MonumentRewardData(20, 2d) }, { "desert_military_base", new MonumentRewardData(20, 2d) }, { "nuclear_missile_silo", new MonumentRewardData(20, 2d) }, { "oilrig_2", new MonumentRewardData(20, 2d) },  { "powerplant_1", new MonumentRewardData(20, 2d) },
						{ "sphere_tank", new MonumentRewardData(20, 2d) }, { "trainyard_1", new MonumentRewardData(20, 2d) }, { "underwater_lab", new MonumentRewardData(20, 2d) }, { "excavator_1", new MonumentRewardData(25, 2.5d) }, { "military_tunnel_1", new MonumentRewardData(25, 2.5d) },
						{ "oilrig_1", new MonumentRewardData(25, 2.5d) }, { "launch_site_1", new MonumentRewardData(30, 3d) }, { "cargoship", new MonumentRewardData(50, 5d, permList: new string[] { "kits.pirate" }) }
					};
                    SaveRewardConfig(monumentsPath, _monumentConfig);
				}
                PrepareRewardItems(_monumentConfig.Values);
				MonumentTimerTick();
				
				if (MonumentsWatcher != null && MonumentsWatcher.Call("IsReady") != null)
                    OnMonumentsWatcherLoaded();
				subList.Add(nameof(OnMonumentsWatcherLoaded));
				subList.Add(nameof(OnPlayerEnteredMonument));
				subList.Add(nameof(OnPlayerExitedMonument));
				if (!_watcherIsLoaded)
					PrintWarning("MonumentsWatcher plugin not found! MonumentsWatcher is required to work with monuments!\n* https://codefling.com/plugins/monuments-watcher");
			}
			
			if ((_config.EnabledRewards & RewardCategories.Misc) != 0)
            {

            }
			
			if (_config.NightMultiplier > 0d)
				_updateTimer = timer.Every(5f, () => { _isNight = TOD_Sky.Instance.IsNight; });
			
			if (_config.AdvancedStatus_Enabled)
			{
				ToggleImageLib(ImageLibrary != null && ImageLibrary.IsLoaded);
				_statusIsLoaded = AdvancedStatus != null && AdvancedStatus?.Call("IsReady") != null;
                if (!_statusIsLoaded)
                {
                    if (initial && AdvancedStatus != null)
                        PrintWarning("AdvancedStatus plugin found, but not ready yet. Waiting for it to load...");
                    else
						PrintWarning("AdvancedStatus plugin not found! To function, it is necessary to install it!\n* https://codefling.com/plugins/advanced-status\n* https://lone.design/product/advanced-status/");
				}
                else
                    OnAdvancedStatusLoaded();
				subList.Add(nameof(OnAdvancedStatusLoaded));
            }
			
			foreach (var player in BasePlayer.activePlayerList)
				OnPlayerConnected(player);
			
			subList.Add(nameof(OnUserPermissionGranted));
			subList.Add(nameof(OnUserPermissionRevoked));
			subList.Add(nameof(OnUserGroupAdded));
			subList.Add(nameof(OnUserGroupRemoved));
			subList.Add(nameof(OnGroupPermissionGranted));
			subList.Add(nameof(OnGroupPermissionRevoked));
			subList.Add(nameof(OnGroupDeleted));
			subList.Add(nameof(OnPlayerConnected));
			subList.Add(nameof(OnPlayerDisconnected));
			subList.Add(nameof(OnPluginLoaded));
			subList.Add(nameof(OnPluginUnloaded));
			foreach (var hookName in subList)
				Subscribe(hookName);
		}
		
		void Unload()
        {
			Unsubscribe(nameof(OnPluginLoaded));
            Unsubscribe(nameof(OnPluginUnloaded));
			_updateTimer?.Destroy();
			_clearTimer?.Destroy();
			SaveData();
			foreach (var player in BasePlayer.activePlayerList)
				OnPlayerDisconnected(player);
			
			_harmony?.UnpatchAll(IdForHarmony);
			_researchConfig = null;
			_openConfig = null;
			Instance = null;
			_playersData = null;
			_config = null;
		}
        #endregion
		
		#region ~Activity Rewards~
        void OnDispenserBonusReceived(ResourceDispenser dispenser, BasePlayer player, Item item)
		{
			if (TryGetPlayerData(player.UserIDString, out var playerData) && _gatherConfig.TryGetValue(item.info.shortname, out var rewData))
				GiveReward(player, playerData, rewData);
		}

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
			if (info != null && info.InitiatorPlayer is BasePlayer attacker && TryGetPlayerData(attacker.UserIDString, out var playerData))
				KillReward(attacker, playerData, entity.ShortPrefabName);
		}
		
		void OnEntityDeath(ResourceEntity entity, HitInfo info)
        {
			if (info != null && info.InitiatorPlayer is BasePlayer attacker && TryGetPlayerData(attacker.UserIDString, out var playerData))
				KillReward(attacker, playerData, entity.ShortPrefabName);
		}
		
		void OnEntityDeath(BasePlayer player, HitInfo info)
        {
			if (info != null && info.InitiatorPlayer is BasePlayer attacker && TryGetPlayerData(attacker.UserIDString, out var playerData))
				KillReward(attacker, playerData, info.damageTypes.GetMajorityDamageType() == Rust.DamageType.Suicide ? "suicide" : player.ShortPrefabName);
		}

        void OnEntityDeath(PatrolHelicopter patrol)
        {
			if (!patrol.IsValid() || !_patrolLastHit.TryGetValue(patrol.net.ID.Value, out var attackerID))
				return;
			if (BasePlayer.TryFindByID(attackerID, out var attacker) && TryGetPlayerData(attacker.UserIDString, out var playerData))
                KillReward(attacker, playerData, patrol.ShortPrefabName);
            _patrolLastHit.Remove(patrol.net.ID.Value);
		}
		
		public static bool ModUnlock(PlayerBlueprints __instance, ItemDefinition itemDef)
        {
			var player = __instance.baseEntity;
			if (Instance != null && player != null && Instance.TryGetPlayerData(player.UserIDString, out var playerData) && !player.PersistantPlayerInfo.unlockedItems.Contains(itemDef.itemid) && _researchConfig.TryGetValue(itemDef.shortname, out var rewData))
				Instance.GiveReward(player, playerData, rewData);
			return true;
        }
		
		public static void ModFirstLoot(LootContainer entity, BasePlayer player)
        {
			if (Instance != null && player != null && Instance.TryGetPlayerData(player.UserIDString, out var playerData) && _openConfig.TryGetValue(entity.ShortPrefabName, out var rewData))
                Instance.GiveReward(player, playerData, rewData);
		}
		
		void OnCollectiblePickup(CollectibleEntity collectible, BasePlayer player)
        {
			NextTick(() =>
			{
				if (collectible.IsDestroyed && TryGetPlayerData(player.UserIDString, out var playerData) && _pickupConfig.TryGetValue(collectible.itemName.english, out var rewData))
					GiveReward(player, playerData, rewData);
			});
		}
		
		void OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
        {
			if (TryGetPlayerData(player.UserIDString, out var playerData) && player.IsConnected && _pickupConfig.TryGetValue(item.info.displayName.english, out var rewData))
				GiveReward(player, playerData, rewData);
		}
		
		void OnItemPickup(Item item, BasePlayer player, DroppedItem droppedItem)
        {
			if (droppedItem.DroppedBy != 0uL)
				return;
			NextTick(() =>
			{
				if (item?.parent != null && TryGetPlayerData(player.UserIDString, out var playerData) && _pickupConfig.TryGetValue(item.info.displayName.english, out var rewData))
					GiveReward(player, playerData, rewData);
			});
		}
		
		void OnEntitySpawned(GrowableEntity growable)
        {
			if (BasePlayer.TryFindByID(growable.OwnerID, out var player) && TryGetPlayerData(player.UserIDString, out var playerData) && _plantingConfig.TryGetValue(growable.ShortPrefabName, out var rewData))
				GiveReward(player, playerData, rewData);
		}

        void OnFishCaught(ItemDefinition itemDef, BaseFishingRod rod, BasePlayer player)
        {
			if (itemDef != null && TryGetPlayerData(player.UserIDString, out var playerData) && _fishingConfig.TryGetValue(itemDef.shortname, out var rewData))
				GiveReward(player, playerData, rewData);
		}
		
		void OnPlayerEnteredMonument(string monumentID, BasePlayer player)
        {
			if (TryGetPlayerData(player.UserIDString, out var playerData) && player.IsConnected)
				MonumentReward(player, playerData, monumentID);
		}
		
		void OnPlayerExitedMonument(string monumentID, BasePlayer player)
        {
			if (TryGetPlayerData(player.UserIDString, out var playerData) && playerData.MonumentsPermissions != null && playerData.MonumentsPermissions.Count > 0 &&
				TryGetMonumentReward(monumentID, out var rewData) && rewData.PermissionsList != null)
            {
				foreach (var permName in rewData.PermissionsList)
                {
					playerData.MonumentsPermissions.Remove(permName);
                    permission.RevokeUserPermission(player.UserIDString, permName);
				}
			}
		}
        #endregion

        #region ~Classes~
        public class RewardData
        {
			public int PluginInt { get; set; }
			public double PluginDouble { get; set; }
			public List<RewardItem> ItemsList { get; set; }
			
			public RewardData() {}
            public RewardData(int pInt, double pDouble, List<RewardItem> items = null)
            {
                PluginInt = pInt;
                PluginDouble = pDouble;
				if (items != null && items.Count > 0)
					ItemsList = items;
			}
		}
		
		public class MonumentRewardData : RewardData
        {
			[JsonProperty(PropertyName = "List of permissions that are granted upon entering a monument and removed upon leaving it. Note: doesn't apply to offline players")]
			public HashSet<string> PermissionsList;
			
			public MonumentRewardData() : base() {}
			public MonumentRewardData(int pInt, double pDouble, List<RewardItem> items = null, IEnumerable<string> permList = null)
				: base(pInt, pDouble, items)
			{
                if (permList != null)
                    PermissionsList = new HashSet<string>(permList);
            }
		}
		
		public class RewardItem
        {
			public string ShortName { get; set; }
			public int Amount { get; set; }
			public ulong SkinId { get; set; }
			
			[JsonIgnore] public ItemAmount ItemA { get; set; }
			
			public RewardItem() {}
			public RewardItem(string shortName, int amount, ulong skinId = 0uL)
            {
				ShortName = shortName;
				Amount = amount;
				SkinId = skinId;
			}
		}
		
		public class EcoPlugin
        {
			[JsonProperty(PropertyName = "Is it worth enabling the plugin for rewards?")]
			public bool IsEnabled { get; set; } = true;

			[JsonProperty(PropertyName = "Reward Type: true - int, false - double")]
			public bool IsRewardInt { get; set; }
			
			[JsonProperty(PropertyName = "Language key for the text")]
			public string Text_Key { get; set; }
			
			[JsonProperty(PropertyName = "API method name for deposit")]
			public string Deposit { get; set; }
			
			[JsonProperty(PropertyName = "API method name for withdraw")]
			public string Withdraw { get; set; }
			
			public BarSettings BarSettings { get; set; }
			
			[JsonIgnore] public string Name { get; set; }
			[JsonIgnore] public Plugin Plugin { get; set; }
			[JsonIgnore] public bool IsReady { get; set; }
			[JsonIgnore] public string BarId { get; set; }
			[JsonIgnore] public Dictionary<int, object> StatusBar { get; private set; }
			
			public EcoPlugin() {}
			public EcoPlugin(string name, bool isInt, string deposit = "Deposit", string withdraw = "Withdraw")
            {
				Name = name;
				IsRewardInt = isInt;
				Deposit = deposit;
				Withdraw = withdraw;
			}
			
			public void ClearStatusBar()
            {
                StatusBar.Clear();
                StatusBar = null;
			}
			
			public void PrepareStatusBar()
            {
				StatusBar = new Dictionary<int, object>
                {
                    { 0, BarId },
                    { 1, Instance.Name },
                    { 2, "Timed" },
                    { 4, BarSettings.Order },
                    { 5, BarSettings.Height },
                    { 6, BarSettings.Main_Color },
                    { 11, BarSettings.Image_IsRawImage },
                    { 16, BarSettings.Text_Size },
                    { 17, BarSettings.Text_Color },
                    { 18, BarSettings.Text_Font },
                    { 23, BarSettings.SubText_Size },
                    { 24, BarSettings.SubText_Color },
                    { 25, BarSettings.SubText_Font }
				};

                if (BarSettings.Main_Color.StartsWith('#'))
                    StatusBar.Add(-6, BarSettings.Main_Transparency);
                if (!string.IsNullOrWhiteSpace(BarSettings.Main_Material))
                    StatusBar.Add(7, BarSettings.Main_Material);
                if (!BarSettings.Image_IsRawImage)
                {
                    StatusBar.Add(12, BarSettings.Image_Color);
                    if (BarSettings.Image_Color.StartsWith('#'))
                        StatusBar.Add(-12, BarSettings.Image_Transparency);
                }
				if (!string.IsNullOrWhiteSpace(BarSettings.Image_Outline_Distance))
				{
                    StatusBar.Add(13, BarSettings.Image_Outline_Color);
                    if (BarSettings.Image_Outline_Color.StartsWith('#'))
                        StatusBar.Add(-13, BarSettings.Image_Outline_Transparency);
                    StatusBar.Add(14, BarSettings.Image_Outline_Distance);
                }
				if (BarSettings.Text_Color.StartsWith('#'))
                    StatusBar.Add(-17, BarSettings.Text_Transparency);
                if (BarSettings.Text_Offset_Horizontal != 0)
                    StatusBar.Add(19, BarSettings.Text_Offset_Horizontal);
				if (!string.IsNullOrWhiteSpace(BarSettings.Text_Outline_Distance))
				{
                    StatusBar.Add(20, BarSettings.Text_Outline_Color);
                    if (BarSettings.Text_Outline_Color.StartsWith('#'))
                        StatusBar.Add(-20, BarSettings.Text_Outline_Transparency);
                    StatusBar.Add(21, BarSettings.Text_Outline_Distance);
                }
				if (BarSettings.SubText_Color.StartsWith('#'))
                    StatusBar.Add(-24, BarSettings.SubText_Transparency);
                if (!string.IsNullOrWhiteSpace(BarSettings.SubText_Outline_Distance))
				{
                    StatusBar.Add(26, BarSettings.SubText_Outline_Color);
                    if (BarSettings.SubText_Outline_Color.StartsWith('#'))
                        StatusBar.Add(-26, BarSettings.SubText_Outline_Transparency);
                    StatusBar.Add(27, BarSettings.SubText_Outline_Distance);
                }
			}
			
			public void SelectBarImage()
            {
                StatusBar.Remove(10);
                StatusBar.Remove(9);
                StatusBar.Remove(8);
                if (!string.IsNullOrWhiteSpace(BarSettings.Image_Sprite))
                    StatusBar.Add(10, BarSettings.Image_Sprite);
                else if (!string.IsNullOrWhiteSpace(BarSettings.Image_Local))
                    StatusBar.Add(9, BarSettings.Image_Local);
                else
                    StatusBar.Add(8, Instance._imgLibIsLoaded && BarSettings.Image_Url.StartsWithAny(Instance.HttpScheme) ? BarId : BarSettings.Image_Url);
            }
		}
		
		[Flags]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public enum StatusDisplayMode
        {
            SingleBar = 0,
            Cumulative = 1 << 0,
            SeparateBars = 1 << 1
        }
		
		public class BarSettings
        {
			public int Order { get; set; } = 40;
            public int Height { get; set; } = 26;

            [JsonProperty(PropertyName = "Main_Color(Hex or RGBA)")]
            public string Main_Color { get; set; } = "#84AB49";

            public float Main_Transparency { get; set; } = 0.8f;

            [JsonProperty(PropertyName = "Main_Material(empty to disable)")]
            public string Main_Material { get; set; } = string.Empty;
			
			public string Image_Url { get; set; } = "https://gitlab.com/IIIaKa/images/-/raw/main/StatusBars/ActivityRewards_Default.png";

            [JsonProperty(PropertyName = "Image_Local(Leave empty to use Image_Url)")]
            public string Image_Local { get; set; } = "ActivityRewards_Default";

            [JsonProperty(PropertyName = "Image_Sprite(Leave empty to use Image_Local or Image_Url)")]
            public string Image_Sprite { get; set; } = string.Empty;

            public bool Image_IsRawImage { get; set; } = false;

            [JsonProperty(PropertyName = "Image_Color(Hex or RGBA)")]
            public string Image_Color { get; set; } = "#B9D134";

            public float Image_Transparency { get; set; } = 1f;

            [JsonProperty(PropertyName = "Image_Outline_Color(Hex or RGBA)")]
            public string Image_Outline_Color { get; set; } = "0.1 0.3 0.8 0.9";

            public float Image_Outline_Transparency { get; set; } = 1f;

            [JsonProperty(PropertyName = "Image_Outline_Distance(Leave empty to disable). Example '0.75 0.75'")]
            public string Image_Outline_Distance { get; set; } = string.Empty;
			
			public int Text_Size { get; set; } = 12;

            [JsonProperty(PropertyName = "Text_Color(Hex or RGBA)")]
            public string Text_Color { get; set; } = "#DAEBAD";

            public float Text_Transparency = 1f;

            [JsonProperty(PropertyName = "Text_Font(https://docs.oxidemod.com/guides/developers/basic-cui/basic-cui#fonts)")]
            public string Text_Font { get; set; } = "RobotoCondensed-Bold.ttf";

            public int Text_Offset_Horizontal { get; set; } = 0;

            [JsonProperty(PropertyName = "Text_Outline_Color(Hex or RGBA)")]
            public string Text_Outline_Color { get; set; } = "#000000";

            public float Text_Outline_Transparency { get; set; } = 1f;

            [JsonProperty(PropertyName = "Text_Outline_Distance(Leave empty to disable)")]
            public string Text_Outline_Distance { get; set; } = string.Empty;
			
			public int SubText_Size { get; set; } = 12;

            [JsonProperty(PropertyName = "SubText_Color(Hex or RGBA)")]
            public string SubText_Color { get; set; } = "#DAEBAD";

            public float SubText_Transparency = 1f;

            public string SubText_Font { get; set; } = "RobotoCondensed-Bold.ttf";

            [JsonProperty(PropertyName = "SubText_Outline_Color(Hex or RGBA)")]
            public string SubText_Outline_Color { get; set; } = "0.5 0.6 0.7 0.5";

            public float SubText_Outline_Transparency { get; set; } = 1f;

            [JsonProperty(PropertyName = "SubText_Outline_Distance(Leave empty to disable)")]
            public string SubText_Outline_Distance { get; set; } = string.Empty;
        }
		#endregion
    }
} 