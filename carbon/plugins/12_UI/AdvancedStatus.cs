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
*  Codefling plugin page: https://codefling.com/plugins/advanced-status
*  Codefling license: https://codefling.com/plugins/advanced-status?tab=downloads_field_4
*  
*  Lone.Design plugin page: https://lone.design/product/advanced-status/
*
*  Copyright © 2023-2026 IIIaKa
*/

using HarmonyLib;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Network;
using Facepunch;
using Facepunch.CardGames;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
	[Info("Advanced Status", "IIIaKa", "0.1.26")]
	[Description("A useful API plugin that mimics in-game status bars and allows the addition of custom ones.")]
	class AdvancedStatus : RustPlugin
    {
        [PluginReference]
		private Plugin ImageLibrary;

		#region ~Variables~
		private static AdvancedStatus Instance { get; set; }
		private Harmony _harmony;
		private const string IdForHarmony = "iiiaka.advancedstatus";
		private bool _isReady = false;
		private const string PERMISSION_ADMIN = "advancedstatus.admin", AdvancedStatusName = "AdvancedStatus", DefaultImage = $"{AdvancedStatusName}_Default", Str_GetImage = "GetImage", NoteInv = "note.inv",
			Hooks_OnLoaded = $"On{AdvancedStatusName}Loaded", Hooks_OnBarDeleted = "OnAdvancedBarDeleted", Hooks_GainedBuilding = "OnPlayerGainedBuildingPrivilege", Hooks_LostBuilding = "OnPlayerLostBuildingPrivilege",
			Placeholders_UserId = "*userId*", Placeholders_UserName = "*userName*";
		private readonly string[] _layersList = new string[] { "Under", "Overall", "Overlay", "Hud", "Hud.Menu" },
            _fontsList = new string[] { "RobotoCondensed-Bold.ttf", "RobotoCondensed-Regular.ttf", "PermanentMarker.ttf", "DroidSansMono.ttf" },
            HttpScheme = new string[] { "http://", "https://" },
			_defaultHooks = new string[]
			{
				"OnPlayerConnected", "OnPlayerDisconnected", "OnPlayerLanguageChanged", "OnPlayerSleep", "OnPlayerSleepEnded",
				"OnEntityDeath", "OnSendCommand", "OnEntityMounted", "OnEntityDismounted", "OnNpcConversationStart",
				"OnNpcConversationEnded", "OnPluginUnloaded"
			};
		private static Coroutine _loadCoroutine;
		private static Hash<ulong, PlayerWatcher> _playersList;
		private string[] _cachedBarsContainer = new string[25];
		
		private readonly ItemCategory[] _buildingItemsExceptedCategories = new ItemCategory[] { ItemCategory.Attire, ItemCategory.Medical, ItemCategory.Component };
		private readonly Dictionary<ItemCategory, int[]> _buildingItems = new Dictionary<ItemCategory, int[]>()
		{
			{ ItemCategory.Weapon, new int[] { 1714509152, -187304968, 1145722690, -759279626, -1290278434 }},
			{ ItemCategory.Resources, new int[] { 634478325 }},
			{ ItemCategory.Tool, new int[] { 1803831286, 200773292 }},
			{ ItemCategory.Food, new int[]
			{
				122783240, 1911552868, 838831151, 803954639, -778875547, 998894949, 1512054436, -2084071424, -1305326964, -1776128552,
				-886280491, -237809779, 1898094925, -1511285251, 2133269020, 830839496, 390728933, -520133715, 1533551194, -992286106,
				-798662404, 1004843240, -19360132, -1037472336, 912235912, 1412103380, 924598634, -1790885730
			}},
			{ ItemCategory.Ammunition, new int[] { -484006286, -1827561369 }},
			{ ItemCategory.Misc, new int[]
			{
				573676040, 1242522330, -1973785141, -695124222, 282103175, 809199956, -1679267738, -489848205, -2058362263, 1358643074,
				-173268125, -173268126, -173268128, -173268129, -173268131, -173268132, 882559853, 1885488976, 2104517339, 699075597,
				-173268127, -1039234836, -173268138
			}}
		};
		
		private readonly Dictionary<ItemCategory, int[]> _buildingItems2 = new Dictionary<ItemCategory, int[]>()
        {
			{ ItemCategory.Items, new int[] { 613961768, 696029452, -1501434104, -593892112, 1099611828, 2047789913, -903796529, 1629564540, -870140677 }},
			{ ItemCategory.Electrical, new int[] { 363163265, -144513264, -566907190, -144417939 }},
			{ ItemCategory.Fun, new int[]
			{
                -2124352573, -1379036069, -1530414568, -1049881973, -1961560162, -979951147, 476066818, -912398867, 1523403414, -583379016,
                -20045316, -2040817543, 273172220, 576509618, -2107018088, 1784406797, 204970153, 1094293920
			}}
		};
		
		private readonly int[] _stackableItems = new int[]
		{
			1055319033, 349762871, 915408809, -1023065463, -1234735557, 215754713, 14241751, 588596902, -2097376851, 785728077,
			51984655, -1691396643, -1211166256, -1321651331, 605467368, 1712070256, -1685290200, -1036635990, -727717969, -1800345240,
			-1671551935, -1199897169, -1199897172, -1023374709, 1553078977, 1401987718, -89874794, -493159321, 1072924620, 1330084809,
			926800282, -1802083073, 479143914, 1882709339, 95950017, 1199391518, 1414245522, 1234880403, -1994909036, -1021495308,
			642482233, 2019042823, 73681876, 1744298439, 1324203999, -656349006, -7270019, -379734527, -1553999294, -280223496, -515830359,
			-1306288356, -1486461488, -99886070, 261913429, -454370658, -1538109120, -586342290, -568419968, 1770475779, 1668129151,
			989925924, 1973684065, -1848736516, -1440987069, -751151717, -78533081, -1509851560, 1422530437, 1917703890, -1162759543,
			-1130350864, -682687162, 1536610005, -1709878924, 1272768630, -989755543, 1873897110, -1520560807, 1827479659, 813023040,
			-395377963, -1167031859, 1391703481, -242084766, 621915341, -996920608, 1819863051, -1770889433, -1824770114, 831955134,
			-1433390281, 1036321299, 1223900335, -602717596, -126305173, -888153050, 1364514421, -455286320, 1762167092, 1223729384,
			1572152877, -282193997, 180752235, -1386082991, 70102328, 22947882, 81423963, 1058261682, -151387974, 550753330,
			-384243979, 1771755747, 122783240, 1911552868, 1112162468, 838831151, 803954639, 858486327, -1305326964, -1776128552,
			1272194103, 2133269020, 830839496, 854447607, 1533551194, -992286106, 1660145984, 390728933, -520133715, 1367190888,
			-778875547, 998894949, -886280491, -237809779, -2086926071, 1512054436, -2084071424, -567909622, 1898094925, -1511285251,
			-1018587433, 1776460938, 1719978075, 634478325, -1938052175, -858312878, -321733511, 1568388703, -592016202, -930193596,
			-265876753, -1579932985, -1982036270, 317398316, 1381010055, -946369541, 69511070, -4031221, -1779183908, -804769727,
			-544317637, -277057363, -932201673, -2099697608, -1157596551, -1581843485, 1523195708, -1779180711, -151838493, -1366326648,
			1735402444, -1673693549, -1513203236, 1858828593, 1601800933, 1348294923, -2035449523, 1130729138, -724146494, 1925646349,
			734320711, -798662404, 1004843240, 1414245519, -19360132, -1037472336, -611118083, 912235912, 1412103380, 1178325727,
			924598634, -1790885730, -170436364, -1318837358, 392828520, -2100458529, -2095813057, -1081599445, -1616704051, -1796837031,
			248643189, -963819285, -594596146, 2036395619, -274709858, -1247485104, 104856514, 54436981, -1896395719, -143481979,
			385645417, -24571537
        };
		
		private static readonly HashSet<string> _doubleMountsList = new HashSet<string> { "horsesaddle", "horsesaddlerear", "bikedriverseat", "bikepassengerseat" },
			_mountsList = new HashSet<string>
			{
				"motorbikedriverseat", "motorbikepassengerseat", "modularcardriverseat", "modularcarpassengerseatleft", "modularcarpassengerseatright",
				"modularcarpassengerseatlesslegroomleft", "modularcarpassengerseatlesslegroomright", "modularcarpassengerseatsidewayleft", "miniheliseat", "minihelipassenger",
				"transporthelipilot", "transporthelicopilot", "attackhelidriver", "attackheligunner", "submarinesolodriverstanding",
				"submarineduodriverseat", "submarineduopassengerseat", "snowmobiledriverseat", "snowmobilepassengerseat", "snowmobilepassengerseat tomaha",
				"workcartdriver", "locomotivedriver", "craneoperator", "batteringramseat", "ballistagun.entity", "mortar.entity"
			};
		#endregion

        #region ~Configuration~
        private static Configuration _config;

		private class Configuration
        {
			[JsonProperty(PropertyName = "Chat command")]
            public string Command = string.Empty;
			
			[JsonProperty(PropertyName = "Is it worth enabling console notifications for the successful loading of local images?")]
            public bool LogImages = false;
			
			[JsonProperty(PropertyName = "Interval(in seconds) for counting in-game status bars")]
			public float CountInterval = 0.5f;
			
			[JsonProperty(PropertyName = "Interval(in seconds) for counting Building Privileges status bars. Note: Calculating Building Privileges is resource-intensive")]
            public float CountIntervalPrivilege = 1f;
			
			[JsonProperty(PropertyName = "Is it worth counting Vehicle Privilege? Useful if you don't have a Tugboat or PlayerBoat")]
			public bool UseVehiclePrivilege = true;
			
			[JsonProperty(PropertyName = "Is it worth counting Entity Privilege? Useful if you don't have a Legacy Shelter")]
            public bool UseEntityPrivilege = true;
			
			[JsonProperty(PropertyName = "Ui layer for displaying bars. For button bars, it is recommended to use the 'Hud' layer(https://docs.oxidemod.com/guides/developers/basic-cui/basic-cui#layers)")]
			public string BarLayer = string.Empty;
			
			[JsonProperty(PropertyName = "Is it worth displaying bar components left to right?")]
			public bool BarLTR = true;

			[JsonProperty(PropertyName = "Vertical spacing between bars")]
			public int BarVerticalSpacing = 2;

			[JsonProperty(PropertyName = "The height of the bar by default")]
			public int BarHeight = 26;

			[JsonProperty(PropertyName = "The background color of the bar by default")]
			public string BarBackgroundColor = string.Empty;

			[JsonProperty(PropertyName = "The background transparency of the bar by default")]
			public float BarBackgroundColorTransparency = 0.7f;

			[JsonProperty(PropertyName = "The background material of the bar by default, leave empty to disable")]
			public string BarBackgroundMaterial = string.Empty;
			
			[JsonProperty(PropertyName = "The image of the bar by default")]
            public string BarImage = string.Empty;
			
			[JsonProperty(PropertyName = "The color of the bar image by default")]
			public string BarImageColor = string.Empty;
			
			[JsonProperty(PropertyName = "The transparency of the bar image by default")]
			public float BarImageTransparency = 1f;
			
			[JsonProperty(PropertyName = "The outline color of the bar image by default")]
            public string BarImageOutlineColor = string.Empty;
			
			[JsonProperty(PropertyName = "The outline transparency of the bar image by default")]
            public float BarImageOutlineTransparency = 1f;
			
			[JsonProperty(PropertyName = "The outline distance of the bar image by default. Example '0.75 0.75'")]
            public string BarImageOutlineDistance = string.Empty;
			
			[JsonProperty(PropertyName = "The size of the bar text by default")]
			public int BarTextSize = 12;

			[JsonProperty(PropertyName = "The color of the bar text by default")]
			public string BarTextColor = string.Empty;
			
			[JsonProperty(PropertyName = "The transparency of the bar text by default")]
            public float BarTextTransparency = 1f;
			
			[JsonProperty(PropertyName = "The font of the bar text by default(https://docs.oxidemod.com/guides/developers/basic-cui/basic-cui#fonts)")]
			public string BarTextFont = string.Empty;
			
			[JsonProperty(PropertyName = "The horizontal offset of the bar text by default")]
			public int BarTextOffsetHorizontal = 0;
			
			[JsonProperty(PropertyName = "The outline color of the bar text by default")]
            public string BarTextOutlineColor = string.Empty;

            [JsonProperty(PropertyName = "The outline transparency of the bar text by default")]
            public float BarTextOutlineTransparency = 1f;

            [JsonProperty(PropertyName = "The outline distance of the bar text by default")]
            public string BarTextOutlineDistance = string.Empty;
			
			[JsonProperty(PropertyName = "The size of the bar subtext by default")]
			public int BarSubTextSize = 12;

			[JsonProperty(PropertyName = "The color of the bar subtext by default")]
			public string BarSubTextColor = string.Empty;
			
			[JsonProperty(PropertyName = "The transparency of the bar subtext by default")]
            public float BarSubTextTransparency = 1f;
			
			[JsonProperty(PropertyName = "The font of the bar subtext by default")]
			public string BarSubTextFont = string.Empty;
			
			[JsonProperty(PropertyName = "The outline color of the bar subtext by default")]
            public string BarSubTextOutlineColor = string.Empty;

            [JsonProperty(PropertyName = "The outline transparency of the bar subtext by default")]
            public float BarSubTextOutlineTransparency = 1f;

            [JsonProperty(PropertyName = "The outline distance of the bar subtext by default")]
            public string BarSubTextOutlineDistance = string.Empty;
			
			[JsonProperty(PropertyName = "The color of the bar progress by default")]
			public string BarProgressColor = string.Empty;

			[JsonProperty(PropertyName = "The transparency of the bar progress by default")]
			public float BarProgressTransparency = 0.7f;

			[JsonProperty(PropertyName = "The OffsetMin of the bar progress by default")]
			public string BarProgressOffsetMin = string.Empty;

			[JsonProperty(PropertyName = "The OffsetMax of the bar progress by default")]
			public string BarProgressOffsetMax = string.Empty;

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
			
			if (string.IsNullOrWhiteSpace(_config.Command))
				_config.Command = "bar";
			_config.CountInterval = Mathf.Clamp(_config.CountInterval, float.Epsilon, 10f);
			_config.CountIntervalPrivilege = Mathf.Clamp(_config.CountIntervalPrivilege, float.Epsilon, 10f);
			if (!_layersList.Contains(_config.BarLayer))
				_config.BarLayer = _layersList[0];
			_config.BarVerticalSpacing = Math.Max(_config.BarVerticalSpacing, 0);
			_config.BarHeight = Math.Max(_config.BarHeight, 1);
			
			if (!IsStringHexColor(_config.BarBackgroundColor))
				_config.BarBackgroundColor = "#505F75";
			_config.BarBackgroundColorTransparency = Mathf.Clamp01(_config.BarBackgroundColorTransparency);
			
			if (string.IsNullOrWhiteSpace(_config.BarImage))
				_config.BarImage = DefaultImage;
			if (!IsStringHexColor(_config.BarImageColor))
                _config.BarImageColor = "#6B7E95";
            _config.BarImageTransparency = Mathf.Clamp01(_config.BarImageTransparency);
			
			if (!IsStringHexColor(_config.BarImageOutlineColor))
                _config.BarImageOutlineColor = "#000000";
            _config.BarImageOutlineTransparency = Mathf.Clamp01(_config.BarImageOutlineTransparency);
			_config.BarImageOutlineDistance ??= string.Empty;
			
			_config.BarTextSize = Math.Clamp(_config.BarTextSize, 1, 25);
			if (!IsStringHexColor(_config.BarTextColor))
                _config.BarTextColor = "#FFFFFF";
            _config.BarTextTransparency = Mathf.Clamp01(_config.BarTextTransparency);
			_config.BarTextFont = ToValidFont(_config.BarTextFont);
			
			if (!IsStringHexColor(_config.BarTextOutlineColor))
                _config.BarTextOutlineColor = "#000000";
            _config.BarTextOutlineTransparency = Mathf.Clamp01(_config.BarTextOutlineTransparency);
			_config.BarTextOutlineDistance ??= string.Empty;
			
			_config.BarSubTextSize = Math.Clamp(_config.BarSubTextSize, 1, 25);
            if (!IsStringHexColor(_config.BarSubTextColor))
                _config.BarSubTextColor = "#FFFFFF";
            _config.BarSubTextTransparency = Mathf.Clamp01(_config.BarSubTextTransparency);
            _config.BarSubTextFont = ToValidFont(_config.BarSubTextFont);
			
			if (!IsStringHexColor(_config.BarSubTextOutlineColor))
                _config.BarSubTextOutlineColor = "#000000";
            _config.BarSubTextOutlineTransparency = Mathf.Clamp01(_config.BarSubTextOutlineTransparency);
			_config.BarSubTextOutlineDistance ??= string.Empty;
			
			if (!IsStringHexColor(_config.BarProgressColor))
                _config.BarProgressColor = "#89B840";
            _config.BarProgressTransparency = Mathf.Clamp01(_config.BarProgressTransparency);
			if (string.IsNullOrWhiteSpace(_config.BarProgressOffsetMin))
				_config.BarProgressOffsetMin = "25 2.5";
			if (string.IsNullOrWhiteSpace(_config.BarProgressOffsetMax))
				_config.BarProgressOffsetMax = "-3.5 -3.5";
			
			SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
        protected override void LoadDefaultConfig() => _config = new Configuration() { Version = Version };
		#endregion

        #region ~Language~
        protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["MsgDays"] = "d",
				["MsgHours"] = "h",
				["MsgMinutes"] = "m",
				["MsgSeconds"] = "s"
			}, this);
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["MsgDays"] = "д",
				["MsgHours"] = "ч",
				["MsgMinutes"] = "м",
				["MsgSeconds"] = "с"
			}, this, "ru");
		}
        #endregion

        #region ~Methods~
		private System.Collections.IEnumerator InitPlugin(bool initial)
        {
			_playersList = new Hash<ulong, PlayerWatcher>();
			if (initial)
				yield return CoroutineEx.waitForSeconds(5f);
			
			string imgDir = $"{Interface.Oxide.DataDirectory}{Path.DirectorySeparatorChar}{Name}{Path.DirectorySeparatorChar}Images";
            if (!Directory.Exists(imgDir))
                Directory.CreateDirectory(imgDir);
            ImagePathFormat = $"{imgDir}{Path.DirectorySeparatorChar}{{0}}.png";
			
			int y;
			for (int i = 0; i <= 24; i++)
            {
				y = 102 + (28 * i);
                _cachedBarsContainer[i] = $@"[{{""name"":""{AdvancedStatusName}"",""parent"":""{_config.BarLayer}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0""}},
{{""type"":""RectTransform"",""anchormin"":""1 0"",""anchormax"":""1 0"",""offsetmin"":""-208 {y}"",""offsetmax"":""-16 {y + 1500}""}}]}}]";
            }
			
			yield return ServerMgr.Instance.StartCoroutine(InitImages());
			yield return null;
			
			_harmony = new Harmony(IdForHarmony);
            _harmony.Patch(AccessTools.Method(typeof(CardGameController), "JoinTable", new Type[] { typeof(ulong) }), postfix: new HarmonyMethod(typeof(AdvancedStatus), nameof(AdvancedStatus_JoinTable)));
            _harmony.Patch(AccessTools.Method(typeof(CardGameController), "LeaveTable", new Type[] { typeof(ulong) }), postfix: new HarmonyMethod(typeof(AdvancedStatus), nameof(AdvancedStatus_LeaveTable)));
			_harmony.Patch(AccessTools.Method(typeof(BasePlayer), "SetActiveTelephone", new Type[] { typeof(PhoneController) }), postfix: new HarmonyMethod(typeof(AdvancedStatus), nameof(AdvancedStatus_SetActiveTelephone)));
			
			foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.userID.IsSteamId())
                    InitPlayer(player);
            }
			
			for (int i = 0; i < _defaultHooks.Length; i++)
                Subscribe(_defaultHooks[i]);
			yield return null;
			
			_isReady = true;
			Interface.CallHook(Hooks_OnLoaded, Version);
			_loadCoroutine = null;
		}
		
		private bool IsBuildingBlockItem(ItemDefinition itemDef)
        {
			if (itemDef == null || _buildingItemsExceptedCategories.Contains(itemDef.category))
				return false;
			if (_buildingItems.TryGetValue(itemDef.category, out var list))
                return list.Contains(itemDef.itemid);
            if (_buildingItems2.TryGetValue(itemDef.category, out var list2))
                return !list2.Contains(itemDef.itemid);
			return true;
		}
		
		public static BuildingPrivlidge GetBuildingPrivilege(BasePlayer player)
        {
			BuildingManager.Building building;
			BuildingBlock buildingBlock, other = null;
			BuildingPrivlidge dominatingPrivilege, result = null;
			var bBlockList = Pool.Get<List<BuildingBlock>>();
			var obb = new OBB(player.transform.position, player.transform.lossyScale, player.transform.rotation, player.bounds);
			Vis.Entities(obb.position, 16f + obb.extents.magnitude, bBlockList, 2097152);
            for (int i = 0; i < bBlockList.Count; i++)
            {
				buildingBlock = bBlockList[i];
				if (buildingBlock.isServer != player.isServer || !buildingBlock.IsOlderThan(other) || obb.Distance(buildingBlock.WorldSpaceBounds()) > 16f)
					continue;
				
				building = buildingBlock.GetBuilding();
				if (building == null)
					continue;
				dominatingPrivilege = building.GetDominatingBuildingPrivilege();
                if (dominatingPrivilege != null)
                {
                    other = buildingBlock;
                    result = dominatingPrivilege;
                }
			}
			Pool.FreeUnmanaged(ref bBlockList);
			return result;
        }
		
		public EntityPrivilege GetEntityBuildingPrivilege(BasePlayer player)
        {
			var obb = new OBB(player.transform.position, player.transform.lossyScale, player.transform.rotation, player.bounds);
			if (BaseEntity.IsCacheValid(player.cachedEntityBuildingPrivilegeTime, 1f, player.cachedEntityBuildingPrivilegePosition, obb.position))
                return player.cachedEntityBuildingPrivilege as EntityPrivilege;
			
			LegacyShelter other = null;
            var shelterList = Pool.Get<List<LegacyShelter>>();
            Vis.Entities(obb.position, 10f, shelterList, 2097152);
            for (int i = 0; i < shelterList.Count; i++)
            {
                var shelter = shelterList[i];
                if (shelter.isServer != player.isServer || !shelter.IsOlderThan(other) || obb.Distance(shelter.WorldSpaceBounds()) > 3f)
                    continue;
                other = shelter;
            }
            Pool.FreeUnmanaged(ref shelterList);
			
			var entityBuildingPrivilege = other?.GetEntityBuildingPrivilege();
			player.cachedEntityBuildingPrivilege = entityBuildingPrivilege;
			player.cachedEntityBuildingPrivilegeBlocked = entityBuildingPrivilege != null && !entityBuildingPrivilege.IsAuthed(player);
			player.cachedEntityBuildingPrivilegeTime = UnityEngine.Time.time;
			player.cachedEntityBuildingPrivilegePosition = obb.position;
			return entityBuildingPrivilege;
		}
		
		public static void AdvancedStatus_JoinTable(CardGameController __instance, ulong userID)
        {
            if (__instance != null && __instance.IsAtTable(userID) && TryGetWatcher(userID, out var watcher))
                watcher.OnVanillaUi(true);
        }

        public static void AdvancedStatus_LeaveTable(ulong userID)
        {
            if (TryGetWatcher(userID, out var watcher))
                watcher.OnVanillaUi(false);
        }
		
		public static void AdvancedStatus_SetActiveTelephone(BasePlayer __instance)
		{
            if (__instance != null && TryGetWatcher(__instance.userID, out var watcher))
                watcher.OnVanillaUi(__instance.activeTelephone != null);
        }
		#endregion

        #region ~API~
        private object IsReady() => _isReady ? true : null;
		
		private bool BarExists(object obj, string barId, string pluginName) => BarExists($"{obj}", barId, pluginName);
        private bool BarExists(string userIDStr, string barId, string pluginName) => ulong.TryParse(userIDStr, out var userID) ? BarExists(userID, barId, pluginName) : false;
        private bool BarExists(BasePlayer player, string barId, string pluginName) => BarExists(player.userID.Get(), barId, pluginName);
        private bool BarExists(ulong userID, string barId, string pluginName)
        {
			if (_isReady && TryGetWatcher(userID, out var watcher))
            {
                AdvancedBar bar;
                for (int i = 0; i < watcher.Bars.Count; i++)
                {
                    bar = watcher.Bars[i];
                    if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
		
		private void DeleteBar(object obj, string barId, string pluginName) => DeleteBar($"{obj}", barId, pluginName);
		private void DeleteBar(string userIDStr, string barId, string pluginName)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                DeleteBar(userID, barId, pluginName);
        }
		private void DeleteBar(BasePlayer player, string barId, string pluginName) => DeleteBar(player.userID.Get(), barId, pluginName);
		private void DeleteBar(ulong userID, string barId, string pluginName)
        {
			if (!_isReady || !TryGetWatcher(userID, out var watcher))
				return;
			AdvancedBar bar;
			for (int i = 0; i < watcher.Bars.Count; i++)
			{
				bar = watcher.Bars[i];
				if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
				{
					bar.ShouldRemove = true;
					break;
				}
			}
		}
		
		private void DeleteBarForAll(string barId, string pluginName)
        {
			if (!_isReady)
                return;
			AdvancedBar bar;
			foreach (var watcher in _playersList.Values)
			{
				for (int i = 0; i < watcher.Bars.Count; i++)
                {
					bar = watcher.Bars[i];
                    if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
                    {
                        bar.ShouldRemove = true;
                        break;
                    }
                }
			}
		}
		
		private void DeleteCategory(object obj, string category, string pluginName) => DeleteCategory($"{obj}", category, pluginName);
		private void DeleteCategory(string userIDStr, string category, string pluginName)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                DeleteCategory(userID, category, pluginName);
        }
		private void DeleteCategory(BasePlayer player, string category, string pluginName) => DeleteCategory(player.userID.Get(), category, pluginName);
        private void DeleteCategory(ulong userID, string category, string pluginName)
        {
			if (!_isReady || !TryGetWatcher(userID, out var watcher))
				return;
			AdvancedBar bar;
			for (int i = 0; i < watcher.Bars.Count; i++)
			{
				bar = watcher.Bars[i];
				if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
					bar.ShouldRemove = true;
			}
        }
		
		private void DeleteCategoryForAll(string category, string pluginName)
        {
			if (!_isReady)
                return;
			AdvancedBar bar;
			foreach (var watcher in _playersList.Values)
			{
				for (int i = 0; i < watcher.Bars.Count; i++)
                {
					bar = watcher.Bars[i];
                    if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                        bar.ShouldRemove = true;
                }
			}
		}
		
		private void DeleteAllBars(object obj, string pluginName) => DeleteAllBars($"{obj}", pluginName);
		private void DeleteAllBars(string userIDStr, string pluginName)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                DeleteAllBars(userID, pluginName);
        }
		private void DeleteAllBars(BasePlayer player, string pluginName) => DeleteAllBars(player.userID.Get(), pluginName);
        private void DeleteAllBars(ulong userID, string pluginName)
        {
			if (!_isReady || !TryGetWatcher(userID, out var watcher))
				return;
			AdvancedBar bar;
			for (int i = 0; i < watcher.Bars.Count; i++)
			{
				bar = watcher.Bars[i];
				if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase))
                    bar.ShouldRemove = true;
            }
        }
		
		private void DeleteAllPluginBars(string pluginName)
        {
			if (!_isReady)
				return;
			AdvancedBar bar;
			foreach (var watcher in _playersList.Values)
            {
				for (int i = 0; i < watcher.Bars.Count; i++)
                {
                    bar = watcher.Bars[i];
                    if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase))
                        bar.ShouldRemove = true;
                }
			}
        }
		
		private bool InBuildingPrivilege(object obj) => InBuildingPrivilege($"{obj}");
		private bool InBuildingPrivilege(string userIDStr) => ulong.TryParse(userIDStr, out var userID) ? InBuildingPrivilege(userID) : false;
		private bool InBuildingPrivilege(BasePlayer player) => InBuildingPrivilege(player.userID.Get());
		private bool InBuildingPrivilege(ulong userID) => _isReady && TryGetWatcher(userID, out var watcher) ? watcher.InOwnBuilding : false;
		
		private int GetTotalClientBars(object obj) => GetTotalClientBars($"{obj}");
        private int GetTotalClientBars(string userIDStr) => ulong.TryParse(userIDStr, out var userID) ? GetTotalClientBars(userID) : 0;
        private int GetTotalClientBars(BasePlayer player) => GetTotalClientBars(player.userID.Get());
        private int GetTotalClientBars(ulong userID) => _isReady && TryGetWatcher(userID, out var watcher) ? Math.Max(watcher.ClientBars, 0) : 0;
		
		private int GetTotalPlayerBars(object obj) => GetTotalPlayerBars($"{obj}");
        private int GetTotalPlayerBars(string userIDStr) => ulong.TryParse(userIDStr, out var userID) ? GetTotalPlayerBars(userID) : 0;
        private int GetTotalPlayerBars(BasePlayer player) => GetTotalPlayerBars(player.userID.Get());
        private int GetTotalPlayerBars(ulong userID)
        {
			int result = 0;
            if (_isReady && TryGetWatcher(userID, out var watcher))
            {
                for (int i = 0; i < watcher.Bars.Count; i++)
                {
                    if (!watcher.Bars[i].ShouldRemove)
                        result++;
                }
            }
            return result;
        }
		#endregion

        #region ~API - string~
        private void CreateBar(object obj, Dictionary<string, object> parameters) => CreateBar($"{obj}", parameters);
        private void CreateBar(string userIDStr, Dictionary<string, object> parameters)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                CreateBar(userID, parameters);
        }
        private void CreateBar(BasePlayer player, Dictionary<string, object> parameters) => CreateBar(player.userID.Get(), parameters);
        private void CreateBar(ulong userID, Dictionary<string, object> parameters)
        {
			if (parameters == null || !userID.IsSteamId() || !parameters.TryGetValue("Plugin", out var pNameObj) || pNameObj is not string pluginName ||
                !parameters.TryGetValue("Id", out var bIdObj) || bIdObj is not string barId)
				return;
			if (!_isReady)
            {
                PrintWarning($"The '{pluginName}' plugin is trying to call the 'CreateBar'(Dictionary<string, object>) API method before the plugin is ready to operate!");
                return;
            }
			if (!TryGetWatcher(userID, out var watcher))
            {
                if (BasePlayer.TryFindByID(userID, out var player))
                    InitPlayer(player);

                if (!TryGetWatcher(userID, out watcher))
                {
                    PrintWarning($"Plugin '{pluginName}' is trying to create a status bar for player '{userID}', but its watcher was not found or hasn't been created yet.");
                    return;
                }
            }
			
			AdvancedBar bar = null;
			bool isOrder = true, found = false;
			for (int i = 0; i < watcher.Bars.Count; i++)
            {
				bar = watcher.Bars[i];
                if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
                {
					found = true;
					break;
				}
			}
			
			if (!found)
			{
                bar = new AdvancedBar(pluginName, barId, parameters);
                watcher.Bars.Add(bar);
            }
			else
				bar.Update(parameters, out var _, out var _, out isOrder);
			
			if (isOrder)
                SortAdvancedBars(watcher.Bars);
            bar.ShouldRemove = false;
            if (watcher.CanShowBars)
                DrawBars(watcher, !watcher.IsUiActive ? 0 : watcher.Bars.IndexOf(bar));
		}

        private void UpdateContent(object obj, Dictionary<string, object> parameters) => UpdateContent($"{obj}", parameters);
        private void UpdateContent(string userIDStr, Dictionary<string, object> parameters)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                UpdateContent(userID, parameters);
        }
        private void UpdateContent(BasePlayer player, Dictionary<string, object> parameters) => UpdateContent(player.userID.Get(), parameters);
        private void UpdateContent(ulong userID, Dictionary<string, object> parameters)
        {
            if (!_isReady || parameters == null || !parameters.TryGetValue("Plugin", out var pNameObj) || pNameObj is not string pluginName ||
                !parameters.TryGetValue("Id", out var bIdObj) || bIdObj is not string barId || !TryGetWatcher(userID, out var watcher))
				return;
			
			AdvancedBar bar;
			for (int i = 0; i < watcher.Bars.Count; i++)
            {
                bar = watcher.Bars[i];
				if (!bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) || !bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
					continue;
				bar.Update(parameters, out var isProgress, out var isText, out var isOrder);
				if (watcher.IsUiActive)
                {
                    if (isProgress)
                        DrawProgress(watcher.Player, bar);
                    if (isText)
                        DrawText(watcher.Player, bar);
                }
				if (isOrder)
					SortAdvancedBars(watcher.Bars);
				return;
			}
		}
		#endregion
        
        #region ~API - int~
        private void CreateBar(object obj, Dictionary<int, object> parameters) => CreateBar($"{obj}", parameters);
        private void CreateBar(string userIDStr, Dictionary<int, object> parameters)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                CreateBar(userID, parameters);
        }
        private void CreateBar(BasePlayer player, Dictionary<int, object> parameters) => CreateBar(player.userID.Get(), parameters);
        private void CreateBar(ulong userID, Dictionary<int, object> parameters)
        {
			if (parameters == null || !userID.IsSteamId() || !parameters.TryGetValue(1, out var pNameObj) || pNameObj is not string pluginName ||
                !parameters.TryGetValue(0, out var bIdObj) || bIdObj is not string barId)
				return;
			if (!_isReady)
            {
                PrintWarning($"The '{pluginName}' plugin is trying to call the 'CreateBar'(Dictionary<int, object>) API method before the plugin is ready to operate!");
                return;
            }
			if (!TryGetWatcher(userID, out var watcher))
            {
                if (BasePlayer.TryFindByID(userID, out var player))
                    InitPlayer(player);

                if (!TryGetWatcher(userID, out watcher))
                {
                    PrintWarning($"Plugin '{pluginName}' is trying to create a status bar for player '{userID}', but its watcher was not found or hasn't been created yet.");
                    return;
                }
            }
			
			AdvancedBar bar = null;
            bool isOrder = true, found = false;
            for (int i = 0; i < watcher.Bars.Count; i++)
            {
				bar = watcher.Bars[i];
				if (bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) && bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
                {
					found = true;
					break;
				}
			}
			
			if (!found)
			{
                bar = new AdvancedBar(pluginName, barId, parameters);
                watcher.Bars.Add(bar);
			}
			else
				bar.Update(parameters, out var _, out var _, out isOrder);
			
			if (isOrder)
                SortAdvancedBars(watcher.Bars);
            bar.ShouldRemove = false;
            if (watcher.CanShowBars)
                DrawBars(watcher, !watcher.IsUiActive ? 0 : watcher.Bars.IndexOf(bar));
        }

        private void UpdateContent(object obj, Dictionary<int, object> parameters) => UpdateContent($"{obj}", parameters);
        private void UpdateContent(string userIDStr, Dictionary<int, object> parameters)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                UpdateContent(userID, parameters);
        }
        private void UpdateContent(BasePlayer player, Dictionary<int, object> parameters) => UpdateContent(player.userID.Get(), parameters);
        private void UpdateContent(ulong userID, Dictionary<int, object> parameters)
        {
			if (!_isReady || parameters == null || !parameters.TryGetValue(1, out var pNameObj) || pNameObj is not string pluginName ||
				!parameters.TryGetValue(0, out var bIdObj) || bIdObj is not string barId || !TryGetWatcher(userID, out var watcher))
				return;
			
			AdvancedBar bar;
			for (int i = 0; i < watcher.Bars.Count; i++)
            {
                bar = watcher.Bars[i];
				if (!bar.Plugin.Equals(pluginName, StringComparison.OrdinalIgnoreCase) || !bar.Id.Equals(barId, StringComparison.OrdinalIgnoreCase))
					continue;
				bar.Update(parameters, out var isProgress, out var isText, out var isOrder);
                if (watcher.IsUiActive)
                {
                    if (isProgress)
                        DrawProgress(watcher.Player, bar);
                    if (isText)
                        DrawText(watcher.Player, bar);
                }
				if (isOrder)
					SortAdvancedBars(watcher.Bars);
				return;
			}
		}
		#endregion
        
        #region ~API - Images~
        private void LoadImages(List<string> imgList, bool force = false)
        {
			if (!_isReady)
				return;
			string path, imgName;
            var images = new Dictionary<string, string>();
			for (int i = 0; i < imgList.Count; i++)
			{
				imgName = imgList[i];
				if (string.IsNullOrWhiteSpace(imgName) || (!force && _imagesList.ContainsKey(imgName)))
                    continue;
                path = string.Format(ImagePathFormat, imgName);
                if (File.Exists(path))
                    images[imgName] = string.Format(ImagePathFormat, imgName);
            }
            if (images.Any())
                ServerMgr.Instance.StartCoroutine(StoreImages(images));
        }

        private void LoadImage(string imgName, bool force = false)
        {
            if (!_isReady || string.IsNullOrWhiteSpace(imgName) || (!force && _imagesList.ContainsKey(imgName)))
                return;
            string path = string.Format(ImagePathFormat, imgName);
            if (File.Exists(path))
                ServerMgr.Instance.StartCoroutine(StoreImage(imgName, path));
        }

        private void CopyImage(string sourceName, string newImage, bool force = false)
        {
            if (!_isReady || string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(newImage))
                return;
            string sourcePath = string.Format(ImagePathFormat, sourceName), newPath = string.Format(ImagePathFormat, newImage);
            if (!File.Exists(sourcePath) || (!force && File.Exists(newPath)))
                return;
            try
            {
                File.Copy(sourcePath, newPath, overwrite: true);
                ServerMgr.Instance.StartCoroutine(StoreImage(newImage, newPath));
            }
            catch {}
        }

        private void DeleteImages(List<string> imgList, bool deleteFile = true)
        {
			if (!_isReady)
				return;
			for (int i = imgList.Count - 1; i >= 0; i--)
                DeleteImage(imgList[i], deleteFile);
        }

        private void DeleteImage(string imgName, bool deleteFile = true)
        {
            if (!_isReady || string.IsNullOrWhiteSpace(imgName))
                return;
            if (_imagesList.TryGetValue(imgName, out var crcStr))
            {
                if (uint.TryParse(crcStr, out var crc))
                    FileStorage.server.Remove(crc, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                _imagesList.Remove(imgName);
            }
            if (deleteFile)
            {
                try { File.Delete(string.Format(ImagePathFormat, imgName)); }
                catch {}
            }
        }

        private string GetImage(string imgName)
        {
            if (_imagesList.TryGetValue(imgName, out var crcStr))
                return crcStr;
            if (_config.LogImages)
                PrintError($"The image '{imgName}' was not found. Please check that the file exists at {string.Format(ImagePathFormat, imgName)}.");
            return _imagesList[DefaultImage];
        }
        #endregion
        
        #region ~Images~
        private string ImagePathFormat = string.Empty;
		private Dictionary<string, string> _imagesList = new Dictionary<string, string>();
		
		private System.Collections.IEnumerator InitImages()
        {
            if (_imagesList.Any())
            {
				uint crc;
				foreach (var crcStr in _imagesList.Values)
                {
                    if (uint.TryParse(crcStr, out crc))
                        FileStorage.server.Remove(crc, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                }
                _imagesList.Clear();
            }

            string[] imgList = Interface.Oxide.DataFileSystem.GetFiles($"{AdvancedStatusName}{Path.DirectorySeparatorChar}Images", "*.png");
            if (imgList.Any())
            {
                int lastSeparator;
                string imgName, path;
                for (int i = 0; i < imgList.Length; i++)
                {
                    path = imgList[i];
                    if (string.IsNullOrWhiteSpace(path))
						continue;
					lastSeparator = path.LastIndexOf(Path.DirectorySeparatorChar);
                    imgName = path.Substring(lastSeparator + 1, path.LastIndexOf('.') - lastSeparator - 1);
                    yield return ServerMgr.Instance.StartCoroutine(StoreImage(imgName, path));
                }
            }

            if (!_imagesList.ContainsKey(DefaultImage))
            {
                _imagesList[DefaultImage] = string.Empty;
                PrintError($"The image folder is empty, or the default image is missing!\nFor proper functionality, the file {string.Format(ImagePathFormat, DefaultImage)} must be present! Please reload the plugin after adding it.");
            }
        }

        private System.Collections.IEnumerator StoreImages(Dictionary<string, string> images)
        {
            foreach (var kvp in images)
                yield return ServerMgr.Instance.StartCoroutine(StoreImage(kvp.Key, kvp.Value));
        }

        private System.Collections.IEnumerator StoreImage(string imgName, string path)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture($"file://{path}"))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    PrintError($"Failed to load the image '{imgName}' from '{path}'");
                    yield break;
                }
                Texture2D tex = DownloadHandlerTexture.GetContent(request);
                if (_imagesList.TryGetValue(imgName, out var crcStr) && uint.TryParse(crcStr, out var crc))
                    FileStorage.server.Remove(crc, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                _imagesList[imgName] = FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString();
                if (_config.LogImages)
                    Puts($"The image '{imgName}' was stored successfully!");
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
		#endregion
        
        #region ~Oxide Hooks~
        void OnPlayerConnected(BasePlayer player)
		{
            if (!_playersList.ContainsKey(player.userID))
				InitPlayer(player);
		}
		
		void OnPlayerDisconnected(BasePlayer player) => RemovePlayer(player);
		
		void OnPlayerSleep(BasePlayer player)
        {
			if (TryGetWatcher(player.userID, out var watcher))
				watcher.OnDeathOrSleeping();
		}
		
		void OnPlayerSleepEnded(BasePlayer player)
		{
			if (!TryGetWatcher(player.userID, out var watcher))
				return;
			watcher.IsDeadOrSleeping = false;
			watcher.StartCount();
		}
		
		void OnEntityDeath(BasePlayer player)
		{
			if (TryGetWatcher(player.userID, out var watcher))
				watcher.OnDeathOrSleeping();
		}
		
		void OnSendCommand(Connection connection, string command, object[] args)
		{
			if (connection == null || !command.StartsWith(NoteInv, StringComparison.OrdinalIgnoreCase) || !TryGetWatcher(connection.userid, out var watcher))
				return;
			int itemId = 0;
            if (!(command.Equals(NoteInv, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[0].ToString(), out itemId)))
            {
                string[] parts = command.Split(" ");
				if (parts.Length > 1)
					int.TryParse(parts[1], out itemId);
			}
			float visibleUntil = Time.realtimeSinceStartup + 3.7f;
			watcher.AddedItems[!_stackableItems.Contains(itemId) ? $"{itemId}_{visibleUntil}" : itemId.ToString()] = visibleUntil;
		}
		
		void OnEntityMounted(BaseMountable mount, BasePlayer player)
        {
			if (TryGetWatcher(player.userID, out var watcher))
				watcher.OnMount(mount);
		}
		
		void OnEntityDismounted(BaseMountable mount, BasePlayer player)
        {
			if (!player.isMounted && TryGetWatcher(player.userID, out var watcher))
				watcher.OnMount(null);
		}
		
		void OnEntityMounted(ComputerStation computerStation, BasePlayer player)
        {
            if (TryGetWatcher(player.userID, out var watcher))
                watcher.OnVanillaUi(true);
        }
		
		void OnEntityDismounted(ComputerStation computerStation, BasePlayer player)
        {
            if (TryGetWatcher(player.userID, out var watcher))
                watcher.OnVanillaUi(false);
        }
		
		void OnNpcConversationStart(NPCTalking npcTalking, BasePlayer player, ConversationData conversationData)
		{
			NextTick(() =>
			{
                if (npcTalking.conversingPlayers.Contains(player) && TryGetWatcher(player.userID, out var watcher))
					watcher.OnVanillaUi(true);
			});
		}
		
		void OnNpcConversationEnded(NPCTalking npcTalking, BasePlayer player)
		{
			if (TryGetWatcher(player.userID, out var watcher))
				watcher.OnVanillaUi(false);
		}
		
		void OnPluginUnloaded(Plugin plugin) => DeleteAllPluginBars(plugin.Name);
		
		void Init()
		{
			for (int i = 0; i < _defaultHooks.Length; i++)
                Unsubscribe(_defaultHooks[i]);
			Instance = this;
			permission.RegisterPermission(PERMISSION_ADMIN, this);
			AddCovalenceCommand(_config.Command, nameof(AdvancedStatus_Command));
		}

		void OnServerInitialized(bool initial) => _loadCoroutine = ServerMgr.Instance.StartCoroutine(InitPlugin(initial));
		
		void Unload()
        {
			if (_loadCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }
			
			_isReady = false;
			for (int i = 0; i < _defaultHooks.Length; i++)
                Unsubscribe(_defaultHooks[i]);
			_harmony?.UnpatchAll(IdForHarmony);
			foreach (var crcStr in _imagesList.Values)
            {
                if (uint.TryParse(crcStr, out var crc))
                    FileStorage.server.Remove(crc, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
            }
			if (_playersList != null)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RemovePlayer(player);
                    CuiHelper.DestroyUi(player, AdvancedStatusName);
                }
                _playersList = null;
            }
			Instance = null;
            _config = null;
        }
		#endregion

        #region ~Commands~
        private void AdvancedStatus_Command(IPlayer player, string command, string[] args)
        {
			if (args == null || args.Length < 3 || (!player.IsServer && !permission.UserHasPermission(player.Id, PERMISSION_ADMIN)))
				return;
			if (args[0].Equals("images", StringComparison.OrdinalIgnoreCase))
			{
				if (args[1].Equals("reload", StringComparison.OrdinalIgnoreCase))
                {
					if (args[2].Equals("all", StringComparison.OrdinalIgnoreCase))
						ServerMgr.Instance.StartCoroutine(InitImages());
					else
						LoadImage(args[2], true);
				}
			}
		}
        #endregion
		
		#region ~Ui~
        private void DrawBars(PlayerWatcher watcher, int startIndex = 0)
		{
			if (startIndex <= 0)
            {
				startIndex = Math.Max(startIndex, 0);
				CuiHelper.DestroyUi(watcher.Player, AdvancedStatusName);
				CuiHelper.AddUi(watcher.Player, _cachedBarsContainer[Math.Clamp(watcher.ClientBars, 0, 24)]);
				watcher.IsUiActive = true;
            }
			
			AdvancedBar bar;
			int verticalOffset = 0;
			string uiName, imgComp, imgColor;
			for (int i = 0; i < watcher.Bars.Count; i++)
            {
				bar = watcher.Bars[i];
				if (i < startIndex)
                {
					verticalOffset += bar.Height + _config.BarVerticalSpacing;
					continue;
				}

				imgColor = bar.Is_RawImage ? "1 1 1 1" : bar.Image_Color;
				if (!string.IsNullOrWhiteSpace(bar.Image_Sprite))
                    imgComp = $@"{{""type"":""UnityEngine.UI.Image"",""color"":""{imgColor}"",""sprite"":""{bar.Image_Sprite}""}}";
                else if (!string.IsNullOrWhiteSpace(bar.Image_Local))
                    imgComp = $@"{{""type"":""UnityEngine.UI.{(bar.Is_RawImage ? "RawImage" : "Image")}"",""color"":""{imgColor}"",""png"":""{_imagesList[_imagesList.ContainsKey(bar.Image_Local) ? bar.Image_Local : DefaultImage]}""}}";
                else if (ImageLibrary == null || !ImageLibrary.IsLoaded || bar.Image.StartsWithAny(HttpScheme))
                    imgComp = $@"{{""type"":""UnityEngine.UI.RawImage"",""color"":""{imgColor}"",""url"":""{bar.Image}""}}";
                else
                    imgComp = $@"{{""type"":""UnityEngine.UI.{(bar.Is_RawImage ? "RawImage" : "Image")}"",""color"":""{imgColor}"",""png"":""{(string)(ImageLibrary?.Call(Str_GetImage, bar.Image) ?? _imagesList[DefaultImage])}""}}";
				
				uiName = bar.BarID;
				CuiHelper.DestroyUi(watcher.Player, uiName);
				CuiHelper.AddUi(watcher.Player,
					$@"[{{""name"":""{uiName}"",""parent"":""{AdvancedStatusName}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""{bar.Main_Color}""{(!string.IsNullOrWhiteSpace(bar.Main_Material) ? $@",""material"":""{bar.Main_Material}""" : string.Empty)}}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 0"",""offsetmin"":""0 {verticalOffset}"",""offsetmax"":""0 {verticalOffset + bar.Height}""}}]}},
{{""name"":""{bar.UiNames[2]}"",""parent"":""{uiName}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0""}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 1"",""offsetmin"":""0 0"",""offsetmax"":""0 0""}}]}},
{{""name"":""{bar.UiNames[0]}"",""parent"":""{uiName}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0""}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 1"",""offsetmin"":""0 0"",""offsetmax"":""0 0""}}]}},
{{""name"":""{bar.UiNames[4]}"",""parent"":""{bar.UiNames[2]}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0""}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 1"",""offsetmin"":""0 0"",""offsetmax"":""0 0""}}]}},
{{""name"":""{bar.UiNames[2]}_Image"",""parent"":""{bar.UiNames[2]}"",""components"":[{imgComp},{(bar._image_Outline ? $@"{{""type"":""UnityEngine.UI.Outline"",""color"":""{bar.Image_Outline_Color}"",""distance"":""{bar.Image_Outline_Distance}""}}," : string.Empty)}{{""type"":""RectTransform"",""anchormin"":""{(_config.BarLTR ? 0 : 1)} 0.5"",""anchormax"":""{(_config.BarLTR ? 0 : 1)} 0.5"",""offsetmin"":""{(_config.BarLTR ? 4 : -20)} -8"",""offsetmax"":""{(_config.BarLTR ? 20 : -4)} 8""}}]}}]");
				
				verticalOffset += bar.Height + _config.BarVerticalSpacing;
				DrawProgress(watcher.Player, bar);
				DrawText(watcher.Player, bar);
				DrawButton(watcher.Player, bar);
			}
		}
		
		public static void DrawProgress(BasePlayer player, AdvancedBar bar)
        {
			string uiName = bar.UiNames[5];
			CuiHelper.DestroyUi(player, uiName);
			if (bar.Progress > 0f)
			{
				CuiHelper.AddUi(player,
					$@"[{{""name"":""{uiName}"",""parent"":""{bar.UiNames[4]}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0""}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 1"",""offsetmin"":""{bar.Progress_OffsetMin}"",""offsetmax"":""{bar.Progress_OffsetMax}""}}]}},
{{""name"":""{bar.UiNames[6]}"",""parent"":""{uiName}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""{bar.Progress_Color}""}},{{""type"":""RectTransform"",""anchormin"":""{(_config.BarLTR ? 0f : 1f - bar.Progress)} 0"",""anchormax"":""{(_config.BarLTR ? bar.Progress : 1f)} 1"",""offsetmin"":""0 0"",""offsetmax"":""0 0""}}]}}]");
			}
		}
		
		public void DrawText(BasePlayer player, AdvancedBar bar)
		{
			string SubText = bar.SubText;
			bool subNotEmpty;
			if (bar.BarType == AdvancedBarType.TimeCounter || bar.BarType == AdvancedBarType.TimeProgressCounter)
			{
				subNotEmpty = true;
				double timeLeft = bar.TimeStamp - Network.TimeEx.currentTimestamp;
				TimeSpan timeSpan = TimeSpan.FromSeconds(timeLeft);
				int days = timeSpan.Days;
				int hours = timeSpan.Hours;
				int minutes = timeSpan.Minutes;
				int seconds = timeSpan.Seconds;
				string[] stringTime = new string[4] {
					days > 0 ? $"{days}{lang.GetMessage("MsgDays", this, player.UserIDString)}" : string.Empty,
					hours > 0 || days > 0 ? $"{hours}{lang.GetMessage("MsgHours", this, player.UserIDString)}" : string.Empty,
					days > 0 || hours > 0 || minutes > 0 ? $"{minutes}{lang.GetMessage("MsgMinutes", this, player.UserIDString)}" : string.Empty,
					days < 1 && hours < 1 ? $"{seconds}{lang.GetMessage("MsgSeconds", this, player.UserIDString)}" : string.Empty };
				SubText = string.Join(" ", stringTime.Where(s => !string.IsNullOrEmpty(s)));
			}
            else
				subNotEmpty = !string.IsNullOrWhiteSpace(SubText);
			
			float width = 1f;
			if (subNotEmpty)
            {
				float subWidth = bar.SubText_Size * (bar.SubText_Size > 15 ? 0.68f : 0.75f) * SubText.Length / 243f;
				if (subWidth > 0.5f)
				{
					width = Mathf.Clamp((bar.Text_Size * (bar.Text_Size > 15 ? 0.68f : 0.75f) * bar.Text.Length / 243f), 0.3f, 0.5f);
					float num = 1f - width;
					if (num < subWidth)
					{
						int num2 = (int)Mathf.Round(num * 243f / (bar.SubText_Size * (bar.SubText_Size > 15 ? 0.68f : 0.75f))) - 3;
						if (num2 > 0 && num2 < SubText.Length)
							SubText = SubText.Substring(0, num2) + "...";
						else if (num2 < SubText.Length)
							SubText = "...";
					}
				}
				else
					width -= subWidth;
			}
			
			string uiName = bar.UiNames[3];
			CuiHelper.DestroyUi(player, uiName);
            CuiHelper.AddUi(player,
				$@"[{{""name"":""{uiName}"",""parent"":""{bar.UiNames[2]}"",""components"":[{{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0""}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 1"",""offsetmin"":""{(_config.BarLTR ? 25 : 5)} 2"",""offsetmax"":""{(_config.BarLTR ? -5 : -25)} -2""}}]}},
{{""name"":""{uiName}_Text"",""parent"":""{uiName}"",""components"":[{{""type"":""UnityEngine.UI.Text"",""text"":""{bar.Text}"",""fontSize"":{bar.Text_Size},""font"":""{bar.Text_Font}"",""align"":""{(_config.BarLTR ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight)}"",""color"":""{bar.Text_Color}""}}{(bar._text_Outline ? $@",{{""type"":""UnityEngine.UI.Outline"",""color"":""{bar.Text_Outline_Color}"",""distance"":""{bar.Text_Outline_Distance}""}}" : string.Empty)},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""{width} 1"",""offsetmin"":""{(_config.BarLTR ? bar.Text_Offset_Horizontal : 0)} 0"",""offsetmax"":""{(_config.BarLTR ? 0 : -bar.Text_Offset_Horizontal)} 0""}}]}}
{(subNotEmpty ? $@",{{""name"":""{uiName}_SubText"",""parent"":""{uiName}"",""components"":[{{""type"":""UnityEngine.UI.Text"",""text"":""{SubText}"",""fontSize"":{bar.SubText_Size},""font"":""{bar.SubText_Font}"",""align"":""{(_config.BarLTR ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft)}"",""color"":""{bar.SubText_Color}""}}{(bar._subText_Outline ? $@",{{""type"":""UnityEngine.UI.Outline"",""color"":""{bar.SubText_Outline_Color}"",""distance"":""{bar.SubText_Outline_Distance}""}}" : string.Empty)},{{""type"":""RectTransform"",""anchormin"":""{width} 0"",""anchormax"":""1 1"",""offsetmin"":""0 0"",""offsetmax"":""0 0""}}]}}" : string.Empty)}]");
		}
		
		public static void DrawButton(BasePlayer player, AdvancedBar bar)
        {
			string uiName = bar.UiNames[1];
			CuiHelper.DestroyUi(player, uiName);
			if (!string.IsNullOrWhiteSpace(bar.Command))
				CuiHelper.AddUi(player, $@"[{{""name"":""{uiName}"",""parent"":""{bar.UiNames[0]}"",""components"":[{{""type"":""UnityEngine.UI.Button"",""command"":""{bar.Command}"",""color"":""0 0 0 0""}},{{""type"":""RectTransform"",""anchormin"":""0 0"",""anchormax"":""1 1"",""offsetmin"":""0 0"",""offsetmax"":""0 0""}}]}}]");
		}
		
		public static bool IsStringHexColor(string hexColor) => !string.IsNullOrWhiteSpace(hexColor) && hexColor[0] == '#' && hexColor.Length == 7;

        public static string StringColorFromHex(string hexColor, float transparent)
        {
            if (string.IsNullOrWhiteSpace(hexColor))
                return $"1 1 1 {transparent:F2}";
            if (hexColor[0] != '#' || hexColor.Length < 7)
                return hexColor;

            int red = Convert.ToInt32(hexColor.Substring(1, 2), 16);
            int green = Convert.ToInt32(hexColor.Substring(3, 2), 16);
            int blue = Convert.ToInt32(hexColor.Substring(5, 2), 16);

            double redPercentage = (double)red / 255;
            double greenPercentage = (double)green / 255;
            double bluePercentage = (double)blue / 255;

            return $"{redPercentage:F2} {greenPercentage:F2} {bluePercentage:F2} {transparent:F2}";
        }
		
		private bool IsHexColor(string input)
        {
            if (input == null || input.Length != 7 || input[0] != '#')
            {
                return false;
            }

            char c;
            bool isHex;
            for (int i = 1; i < input.Length; i++)
            {
                c = input[i];
				isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }
		
		private string ToValidFont(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return _fontsList[0];
            string validFont;
            for (int i = 0; i < _fontsList.Length; i++)
            {
                validFont = _fontsList[i];
                if (validFont.Equals(input, StringComparison.OrdinalIgnoreCase))
                    return validFont;
            }
            return _fontsList[0];
        }
		#endregion

        #region ~AdvancedBar~
        public static void SortAdvancedBars(List<AdvancedBar> bars)
        {
            bars.Sort((b1, b2) =>
            {
                int orderComparison = b1.Order.CompareTo(b2.Order);
                return (orderComparison != 0) ? orderComparison : b1.Id.CompareTo(b2.Id);
            });
        }
		
		public enum AdvancedBarType
		{
			Default,
			Timed,
			TimeCounter,
			TimeProgress,
            TimeProgressCounter
		}
		
		public class AdvancedBar
		{
			public string Id { get; }
			public string Plugin { get; }
			
			public AdvancedBarType BarType { get; private set; }
			public string Category { get; private set; }
			public int Order { get; private set; }
			public int Height { get; private set; }
			public string Main_Color { get; private set; }
			public string Main_Material { get; private set; }
			public string Image { get; private set; }
			public string Image_Local { get; private set; } = string.Empty;
			public string Image_Sprite { get; private set; } = string.Empty;
			public bool Is_RawImage { get; private set; }
			public string Image_Color { get; private set; }
			public bool _image_Outline { get; private set; }
			public string Image_Outline_Color { get; private set; }
			public string Image_Outline_Distance { get; private set; }
			public string Text { get; private set; }
			public int Text_Size { get; private set; }
			public string Text_Color { get; private set; }
			public string Text_Font { get; private set; }
			public int Text_Offset_Horizontal { get; private set; }
			public bool _text_Outline { get; private set; }
			public string Text_Outline_Color { get; private set; }
			public string Text_Outline_Distance { get; private set; }
			public string SubText { get; private set; }
			public int SubText_Size { get; private set; }
			public string SubText_Color { get; private set; }
			public string SubText_Font { get; private set; }
			public bool _subText_Outline { get; private set; }
            public string SubText_Outline_Color { get; private set; }
            public string SubText_Outline_Distance { get; private set; }
			public double TimeStampStart { get; private set; }
			public double TimeStamp { get; private set; }
			public double TimeStampDestroy { get; private set; }
			private float _progress;
			public float Progress
			{
				get { return _progress; }
				set { _progress = Mathf.Clamp01(value); }
			}
			public bool Progress_Reverse { get; private set; }
			public string Progress_Color { get; private set; }
			public string Progress_OffsetMin { get; private set; }
			public string Progress_OffsetMax { get; private set; }
			public string Command { get; private set; }
			public List<string> PlayerCommands { get; private set; } = null;
			public List<string> ConsoleCommands { get; private set; } = null;
			
			public string BarID { get; }
			public bool ShouldRemove { get; set; }
			public string[] UiNames { get; } = new string[7];
			
			public AdvancedBar() {}
			public AdvancedBar(string pluginName, string id, Dictionary<string, object> parameters)
            {
				object obj;
				Id = id;
				Plugin = pluginName;
				BarType = (parameters.TryGetValue("BarType", out obj) && obj is string barTypeString && Enum.TryParse(barTypeString, true, out AdvancedBarType barType)) ? barType : AdvancedBarType.Default;
				Category = parameters.TryGetValue("Category", out obj) && obj is string category ? category : "Default";
				Order = parameters.TryGetValue("Order", out obj) && obj is int order ? order : 10;
				Height = parameters.TryGetValue("Height", out obj) && obj is int height ? height : _config.BarHeight;
				Main_Color = StringColorFromHex(parameters.TryGetValue("Main_Color", out obj) && obj is string mainColor ? mainColor : _config.BarBackgroundColor,
					parameters.TryGetValue("Main_Transparency", out obj) && obj is float mainTransp ? mainTransp : _config.BarBackgroundColorTransparency);
				Main_Material = parameters.TryGetValue("Main_Material", out obj) && obj is string mainMaterial ? mainMaterial : _config.BarBackgroundMaterial;
				
				Image = _config.BarImage;
				if (parameters.TryGetValue("Image_Sprite", out obj) && obj is string imageSprite)
					Image_Sprite = imageSprite;
				else if (parameters.TryGetValue("Image_Local", out obj) && obj is string imageLocal)
					Image_Local = imageLocal;
				else if (parameters.TryGetValue("Image", out obj) && obj is string image && !string.IsNullOrWhiteSpace(image))
					Image = image;
				
				Is_RawImage = parameters.TryGetValue("Is_RawImage", out obj) && obj is bool isRawImage ? isRawImage : false;
				Image_Color = StringColorFromHex(parameters.TryGetValue("Image_Color", out obj) && obj is string imageColor ? imageColor : _config.BarImageColor,
					parameters.TryGetValue("Image_Transparency", out obj) && obj is float imageTransp ? imageTransp : _config.BarImageTransparency);
				Image_Outline_Color = StringColorFromHex(parameters.TryGetValue("Image_Outline_Color", out obj) && obj is string imageOutlineColor ? imageOutlineColor : _config.BarImageOutlineColor,
					parameters.TryGetValue("Image_Outline_Transparency", out obj) && obj is float imageOutlineTransp ? imageOutlineTransp : _config.BarImageOutlineTransparency);
				Image_Outline_Distance = parameters.TryGetValue("Image_Outline_Distance", out obj) && obj is string imageOutlineDistance ? imageOutlineDistance : _config.BarImageOutlineDistance;
				_image_Outline = !string.IsNullOrWhiteSpace(Image_Outline_Distance);
				Text = parameters.TryGetValue("Text", out obj) && obj is string text ? text : string.Empty;
                Text_Size = parameters.TryGetValue("Text_Size", out obj) && obj is int textSize ? textSize : _config.BarTextSize;
				Text_Color = StringColorFromHex(parameters.TryGetValue("Text_Color", out obj) && obj is string textColor ? textColor : _config.BarTextColor,
					parameters.TryGetValue("Text_Transparency", out obj) && obj is float textTransp ? textTransp : _config.BarTextTransparency);
				Text_Font = parameters.TryGetValue("Text_Font", out obj) && obj is string textFont ? textFont : _config.BarTextFont;
                Text_Offset_Horizontal = parameters.TryGetValue("Text_Offset_Horizontal", out obj) && obj is int textOffsetHorizontal ? textOffsetHorizontal : _config.BarTextOffsetHorizontal;
				Text_Outline_Color = StringColorFromHex(parameters.TryGetValue("Text_Outline_Color", out obj) && obj is string textOutlineColor ? textOutlineColor : _config.BarTextOutlineColor,
					parameters.TryGetValue("Text_Outline_Transparency", out obj) && obj is float textOutlineTransp ? textOutlineTransp : _config.BarTextOutlineTransparency);
				Text_Outline_Distance = parameters.TryGetValue("Text_Outline_Distance", out obj) && obj is string textOutlineDistance ? textOutlineDistance : _config.BarTextOutlineDistance;
				_text_Outline = !string.IsNullOrWhiteSpace(Text_Outline_Distance);
				SubText = parameters.TryGetValue("SubText", out obj) && obj is string subText ? subText : string.Empty;
				SubText_Size = parameters.TryGetValue("SubText_Size", out obj) && obj is int subTextSize ? subTextSize : _config.BarSubTextSize;
				SubText_Color = StringColorFromHex(parameters.TryGetValue("SubText_Color", out obj) && obj is string subTextColor ? subTextColor : _config.BarSubTextColor,
                    parameters.TryGetValue("SubText_Transparency", out obj) && obj is float subTextTransp ? subTextTransp : _config.BarSubTextTransparency);
				SubText_Font = parameters.TryGetValue("SubText_Font", out obj) && obj is string subTextFont ? subTextFont : _config.BarSubTextFont;
				SubText_Outline_Color = StringColorFromHex(parameters.TryGetValue("SubText_Outline_Color", out obj) && obj is string subTextOutlineColor ? subTextOutlineColor : _config.BarTextOutlineColor,
					parameters.TryGetValue("SubText_Outline_Transparency", out obj) && obj is float subTextOutlineTransp ? subTextOutlineTransp : _config.BarSubTextOutlineTransparency);
				SubText_Outline_Distance = parameters.TryGetValue("SubText_Outline_Distance", out obj) && obj is string subTextOutlineDistance ? subTextOutlineDistance : _config.BarSubTextOutlineDistance;
				_subText_Outline = !string.IsNullOrWhiteSpace(SubText_Outline_Distance);
				if (parameters.TryGetValue("TimeStampStart", out obj) && obj is double timeStampStart)
					TimeStampStart = timeStampStart;
				if (parameters.TryGetValue("TimeStamp", out obj) && obj is double timeStamp)
					TimeStamp = timeStamp;
				if (parameters.TryGetValue("TimeStampDestroy", out obj) && obj is double timeStampDestroy)
					TimeStampDestroy = timeStampDestroy;
				if (parameters.TryGetValue("Progress", out obj) && obj is float progress)
					Progress = progress;
				Progress_Reverse = parameters.TryGetValue("Progress_Reverse", out obj) && obj is bool progressReverse ? progressReverse : false;
				Progress_Color = StringColorFromHex(parameters.TryGetValue("Progress_Color", out obj) && obj is string progressColor ? progressColor : _config.BarProgressColor,
					parameters.TryGetValue("Progress_Transparency", out obj) && obj is float progressTransp ? progressTransp : _config.BarProgressTransparency);
				Progress_OffsetMin = parameters.TryGetValue("Progress_OffsetMin", out obj) && obj is string progressOffsetMin ? progressOffsetMin : _config.BarProgressOffsetMin;
                Progress_OffsetMax = parameters.TryGetValue("Progress_OffsetMax", out obj) && obj is string progressOffsetMax ? progressOffsetMax : _config.BarProgressOffsetMax;
                Command = parameters.TryGetValue("Command", out obj) && obj is string command ? command : string.Empty;
				if (parameters.TryGetValue("PlayerCommands", out obj) && obj is IEnumerable<string> playerCommands)
					PlayerCommands = new List<string>(playerCommands);
				if (parameters.TryGetValue("ConsoleCommands", out obj) && obj is IEnumerable<string> consoleCommands)
					ConsoleCommands = new List<string>(consoleCommands);
				
				BarID = $"{AdvancedStatusName}_{Plugin}_{Id}";
				InitUiNames();
			}
			
			public AdvancedBar(string pluginName, string id, Dictionary<int, object> parameters)
            {
                object obj;
				Id = id;
				Plugin = pluginName;
				BarType = (parameters.TryGetValue(2, out obj) && obj is string barTypeString && Enum.TryParse(barTypeString, true, out AdvancedBarType barType)) ? barType : AdvancedBarType.Default;
				Category = parameters.TryGetValue(3, out obj) && obj is string category ? category : "Default";
                Order = parameters.TryGetValue(4, out obj) && obj is int order ? order : 10;
                Height = parameters.TryGetValue(5, out obj) && obj is int height ? height : _config.BarHeight;
                Main_Color = StringColorFromHex(parameters.TryGetValue(6, out obj) && obj is string mainColor ? mainColor : _config.BarBackgroundColor,
                    parameters.TryGetValue(-6, out obj) && obj is float mainTransp ? mainTransp : _config.BarBackgroundColorTransparency);
                Main_Material = parameters.TryGetValue(7, out obj) && obj is string mainMaterial ? mainMaterial : _config.BarBackgroundMaterial;
				
				Image = _config.BarImage;
				if (parameters.TryGetValue(10, out obj) && obj is string imageSprite)
                    Image_Sprite = imageSprite;
                else if (parameters.TryGetValue(9, out obj) && obj is string imageLocal)
                    Image_Local = imageLocal;
                else if (parameters.TryGetValue(8, out obj) && obj is string image && !string.IsNullOrWhiteSpace(image))
					Image = image;
				
				Is_RawImage = parameters.TryGetValue(11, out obj) && obj is bool isRawImage ? isRawImage : false;
                Image_Color = StringColorFromHex(parameters.TryGetValue(12, out obj) && obj is string imageColor ? imageColor : _config.BarImageColor,
                    parameters.TryGetValue(-12, out obj) && obj is float imageTransp ? imageTransp : _config.BarImageTransparency);
				Image_Outline_Color = StringColorFromHex(parameters.TryGetValue(13, out obj) && obj is string imageOutlineColor ? imageOutlineColor : _config.BarImageOutlineColor,
					parameters.TryGetValue(-13, out obj) && obj is float imageOutlineTransp ? imageOutlineTransp : _config.BarImageOutlineTransparency);
				Image_Outline_Distance = parameters.TryGetValue(14, out obj) && obj is string imageOutlineDistance ? imageOutlineDistance : _config.BarImageOutlineDistance;
				_image_Outline = !string.IsNullOrWhiteSpace(Image_Outline_Distance);
				Text = parameters.TryGetValue(15, out obj) && obj is string text ? text : string.Empty;
                Text_Size = parameters.TryGetValue(16, out obj) && obj is int textSize ? textSize : _config.BarTextSize;
                Text_Color = StringColorFromHex(parameters.TryGetValue(17, out obj) && obj is string textColor ? textColor : _config.BarTextColor,
                    parameters.TryGetValue(-17, out obj) && obj is float textTransp ? textTransp : _config.BarTextTransparency);
                Text_Font = parameters.TryGetValue(18, out obj) && obj is string textFont ? textFont : _config.BarTextFont;
                Text_Offset_Horizontal = parameters.TryGetValue(19, out obj) && obj is int textOffsetHorizontal ? textOffsetHorizontal : _config.BarTextOffsetHorizontal;
				Text_Outline_Color = StringColorFromHex(parameters.TryGetValue(20, out obj) && obj is string textOutlineColor ? textOutlineColor : _config.BarTextOutlineColor,
					parameters.TryGetValue(-20, out obj) && obj is float textOutlineTransp ? textOutlineTransp : _config.BarTextOutlineTransparency);
				Text_Outline_Distance = parameters.TryGetValue(21, out obj) && obj is string textOutlineDistance ? textOutlineDistance : _config.BarTextOutlineDistance;
				_text_Outline = !string.IsNullOrWhiteSpace(Text_Outline_Distance);
				SubText = parameters.TryGetValue(22, out obj) && obj is string subText ? subText : string.Empty;
                SubText_Size = parameters.TryGetValue(23, out obj) && obj is int subTextSize ? subTextSize : _config.BarSubTextSize;
                SubText_Color = StringColorFromHex(parameters.TryGetValue(24, out obj) && obj is string subTextColor ? subTextColor : _config.BarSubTextColor,
                    parameters.TryGetValue(-24, out obj) && obj is float subTextTransp ? subTextTransp : _config.BarSubTextTransparency);
                SubText_Font = parameters.TryGetValue(25, out obj) && obj is string subTextFont ? subTextFont : _config.BarSubTextFont;
				SubText_Outline_Color = StringColorFromHex(parameters.TryGetValue(26, out obj) && obj is string subTextOutlineColor ? subTextOutlineColor : _config.BarSubTextOutlineColor,
					parameters.TryGetValue(-26, out obj) && obj is float subTextOutlineTransp ? subTextOutlineTransp : _config.BarSubTextOutlineTransparency);
				SubText_Outline_Distance = parameters.TryGetValue(27, out obj) && obj is string subTextOutlineDistance ? subTextOutlineDistance : _config.BarSubTextOutlineDistance;
				_subText_Outline = !string.IsNullOrWhiteSpace(SubText_Outline_Distance);
				if (parameters.TryGetValue(28, out obj) && obj is double timeStampStart)
					TimeStampStart = timeStampStart;
				if (parameters.TryGetValue(29, out obj) && obj is double timeStamp)
					TimeStamp = timeStamp;
				if (parameters.TryGetValue(30, out obj) && obj is double timeStampDestroy)
					TimeStampDestroy = timeStampDestroy;
				if (parameters.TryGetValue(31, out obj) && obj is float progress)
					Progress = progress;
				Progress_Reverse = parameters.TryGetValue(32, out obj) && obj is bool progressReverse ? progressReverse : false;
				Progress_Color = StringColorFromHex(parameters.TryGetValue(33, out obj) && obj is string progressColor ? progressColor : _config.BarProgressColor,
                    parameters.TryGetValue(-33, out obj) && obj is float progressTransp ? progressTransp : _config.BarProgressTransparency);
				Progress_OffsetMin = parameters.TryGetValue(34, out obj) && obj is string progressOffsetMin ? progressOffsetMin : _config.BarProgressOffsetMin;
                Progress_OffsetMax = parameters.TryGetValue(35, out obj) && obj is string progressOffsetMax ? progressOffsetMax : _config.BarProgressOffsetMax;
                Command = parameters.TryGetValue(36, out obj) && obj is string command ? command : string.Empty;
				if (parameters.TryGetValue(37, out obj) && obj is IEnumerable<string> playerCommands)
					PlayerCommands = new List<string>(playerCommands);
				if (parameters.TryGetValue(38, out obj) && obj is IEnumerable<string> consoleCommands)
					ConsoleCommands = new List<string>(consoleCommands);
				
				BarID = $"{AdvancedStatusName}_{Plugin}_{Id}";
				InitUiNames();
			}
			
			public void Update(Dictionary<string, object> parameters, out bool isProgress, out bool isText, out bool isOrder)
            {
                object obj;
                isProgress = false;
                isText = false;
				isOrder = false;
				if (parameters.TryGetValue("BarType", out obj) && obj is string barTypeString && Enum.TryParse(barTypeString, true, out AdvancedBarType barType))
                {
                    if ((BarType == AdvancedBarType.TimeProgress || BarType == AdvancedBarType.TimeProgressCounter) && barType != AdvancedBarType.TimeProgress && barType != AdvancedBarType.TimeProgressCounter)
                    {
                        Progress = 0f;
                        isProgress = true;
                    }
                    BarType = barType;
                }
                if (parameters.TryGetValue("Category", out obj) && obj is string category)
					Category = category;
				if (parameters.TryGetValue("Order", out obj) && obj is int order)
				{
					Order = order;
					isOrder = true;
				}
				if (parameters.TryGetValue("Height", out obj) && obj is int height)
					Height = height;
				if (parameters.TryGetValue("Main_Color", out obj) && obj is string mainColor)
					Main_Color = StringColorFromHex(mainColor, parameters.TryGetValue("Main_Transparency", out obj) && obj is float mainTransp ? mainTransp : _config.BarBackgroundColorTransparency);
				if (parameters.TryGetValue("Main_Material", out obj) && obj is string mainMaterial)
					Main_Material = mainMaterial;
				
				if (parameters.TryGetValue("Image_Sprite", out obj) && obj is string imageSprite)
                {
                    Image = _config.BarImage;
                    Image_Local = string.Empty;
                    Image_Sprite = imageSprite;
                }
				else if (parameters.TryGetValue("Image_Local", out obj) && obj is string imageLocal)
                {
                    Image = _config.BarImage;
                    Image_Local = imageLocal;
                    Image_Sprite = string.Empty;
                }
				else if (parameters.TryGetValue("Image", out obj) && obj is string image)
				{
					Image = string.IsNullOrWhiteSpace(image) ? _config.BarImage : image;
					Image_Local = string.Empty;
					Image_Sprite = string.Empty;
				}
				
				if (parameters.TryGetValue("Is_RawImage", out obj) && obj is bool isRawImage)
					Is_RawImage = isRawImage;
				if (parameters.TryGetValue("Image_Color", out obj) && obj is string imageColor)
					Image_Color = StringColorFromHex(imageColor, parameters.TryGetValue("Image_Transparency", out obj) && obj is float imageTransp ? imageTransp : _config.BarImageTransparency);
				if (parameters.TryGetValue("Image_Outline_Color", out obj) && obj is string imageOutlineColor)
					Image_Outline_Color = StringColorFromHex(imageOutlineColor, parameters.TryGetValue("Image_Outline_Transparency", out obj) && obj is float imageOutlineTransp ? imageOutlineTransp : _config.BarImageOutlineTransparency);
				if (parameters.TryGetValue("Image_Outline_Distance", out obj))
                {
					Image_Outline_Distance = obj is string imageOutlineDistance ? imageOutlineDistance : string.Empty;
                    _image_Outline = !string.IsNullOrWhiteSpace(Image_Outline_Distance);
				}
				if (parameters.TryGetValue("Text", out obj) && obj is string text && Text != text)
                {
                    Text = text;
                    isText = true;
                }
                if (parameters.TryGetValue("Text_Size", out obj) && obj is int textSize)
					Text_Size = textSize;
				if (parameters.TryGetValue("Text_Color", out obj) && obj is string textColor)
					Text_Color = StringColorFromHex(textColor, parameters.TryGetValue("Text_Transparency", out obj) && obj is float textTransp ? textTransp : _config.BarTextTransparency);
				if (parameters.TryGetValue("Text_Font", out obj) && obj is string textFont)
					Text_Font = textFont;
				if (parameters.TryGetValue("Text_Offset_Horizontal", out obj) && obj is int textOffsetHorizontal)
					Text_Offset_Horizontal = textOffsetHorizontal;
				if (parameters.TryGetValue("Text_Outline_Color", out obj) && obj is string textOutlineColor)
					Text_Outline_Color = StringColorFromHex(textOutlineColor, parameters.TryGetValue("Text_Outline_Transparency", out obj) && obj is float textOutlineTransp ? textOutlineTransp : _config.BarTextOutlineTransparency);
				if (parameters.TryGetValue("Text_Outline_Distance", out obj))
				{
					Text_Outline_Distance = obj is string textOutlineDistance ? textOutlineDistance : string.Empty;
					_text_Outline = !string.IsNullOrWhiteSpace(Text_Outline_Distance);
				}
				if (parameters.TryGetValue("SubText", out obj) && obj is string subText && SubText != subText)
                {
                    SubText = subText;
                    isText = true;
                }
                if (parameters.TryGetValue("SubText_Size", out obj) && obj is int subTextSize)
					SubText_Size = subTextSize;
				if (parameters.TryGetValue("SubText_Color", out obj) && obj is string subTextColor)
					SubText_Color = StringColorFromHex(subTextColor, parameters.TryGetValue("SubText_Transparency", out obj) && obj is float subTextTransp ? subTextTransp : _config.BarSubTextTransparency);
				if (parameters.TryGetValue("SubText_Font", out obj) && obj is string subTextFont)
					SubText_Font = subTextFont;
				if (parameters.TryGetValue("SubText_Outline_Color", out obj) && obj is string subTextOutlineColor)
					SubText_Outline_Color = StringColorFromHex(subTextOutlineColor, parameters.TryGetValue("SubText_Outline_Transparency", out obj) && obj is float subTextOutlineTransp ? subTextOutlineTransp : _config.BarSubTextOutlineTransparency);
				if (parameters.TryGetValue("SubText_Outline_Distance", out obj))
                {
					SubText_Outline_Distance = obj is string subTextOutlineDistance ? subTextOutlineDistance : string.Empty;
					_subText_Outline = !string.IsNullOrWhiteSpace(SubText_Outline_Distance);
				}
				if (parameters.TryGetValue("TimeStampStart", out obj) && obj is double timeStampStart)
					TimeStampStart = timeStampStart;
				if (parameters.TryGetValue("TimeStamp", out obj) && obj is double timeStamp)
					TimeStamp = timeStamp;
				if (parameters.TryGetValue("TimeStampDestroy", out obj) && obj is double timeStampDestroy)
					TimeStampDestroy = timeStampDestroy;
				if (parameters.TryGetValue("Progress", out obj) && obj is float progress && Progress != progress)
                {
                    Progress = progress;
                    isProgress = true;
                }
                if (parameters.TryGetValue("Progress_Reverse", out obj) && obj is bool progressReverse)
					Progress_Reverse = progressReverse;
				if (parameters.TryGetValue("Progress_Color", out obj) && obj is string progressColor)
					Progress_Color = StringColorFromHex(progressColor, parameters.TryGetValue("Progress_Transparency", out obj) && obj is float progressTransp ? progressTransp : _config.BarProgressTransparency);
				if (parameters.TryGetValue("Progress_OffsetMin", out obj) && obj is string progressOffsetMin)
					Progress_OffsetMin = progressOffsetMin;
				if (parameters.TryGetValue("Progress_OffsetMax", out obj) && obj is string progressOffsetMax)
					Progress_OffsetMax = progressOffsetMax;
				if (parameters.TryGetValue("Command", out obj) && obj is string command)
					Command = command;
				if (parameters.TryGetValue("PlayerCommands", out obj) && obj is IEnumerable<string> playerCommands)
					PlayerCommands = new List<string>(playerCommands);
				if (parameters.TryGetValue("ConsoleCommands", out obj) && obj is IEnumerable<string> consoleCommands)
					ConsoleCommands = new List<string>(consoleCommands);
			}
			
			public void Update(Dictionary<int, object> parameters, out bool isProgress, out bool isText, out bool isOrder)
            {
				object obj;
				isProgress = false;
				isText = false;
				isOrder = false;
				if (parameters.TryGetValue(2, out obj) && obj is string barTypeString && Enum.TryParse(barTypeString, true, out AdvancedBarType barType))
				{
					if ((BarType == AdvancedBarType.TimeProgress || BarType == AdvancedBarType.TimeProgressCounter) && barType != AdvancedBarType.TimeProgress && barType != AdvancedBarType.TimeProgressCounter)
                    {
						Progress = 0f;
						isProgress = true;
					}
					BarType = barType;
				}
				if (parameters.TryGetValue(3, out obj) && obj is string category)
					Category = category;
				if (parameters.TryGetValue(4, out obj) && obj is int order)
				{
					Order = order;
					isOrder = true;
				}
				if (parameters.TryGetValue(5, out obj) && obj is int height)
					Height = height;
				if (parameters.TryGetValue(6, out obj) && obj is string mainColor)
					Main_Color = StringColorFromHex(mainColor, parameters.TryGetValue(-6, out obj) && obj is float mainTransp ? mainTransp : _config.BarBackgroundColorTransparency);
				if (parameters.TryGetValue(7, out obj) && obj is string mainMaterial)
					Main_Material = mainMaterial;
				
				if (parameters.TryGetValue(10, out obj) && obj is string imageSprite)
                {
                    Image = _config.BarImage;
                    Image_Local = string.Empty;
                    Image_Sprite = imageSprite;
                }
                else if (parameters.TryGetValue(9, out obj) && obj is string imageLocal)
                {
                    Image = _config.BarImage;
                    Image_Local = imageLocal;
                    Image_Sprite = string.Empty;
                }
                else if (parameters.TryGetValue(8, out obj) && obj is string image)
                {
					Image = string.IsNullOrWhiteSpace(image) ? _config.BarImage : image;
                    Image_Local = string.Empty;
                    Image_Sprite = string.Empty;
                }
				
				if (parameters.TryGetValue(11, out obj) && obj is bool isRawImage)
					Is_RawImage = isRawImage;
				if (parameters.TryGetValue(12, out obj) && obj is string imageColor)
					Image_Color = StringColorFromHex(imageColor, parameters.TryGetValue(-12, out obj) && obj is float imageTransp ? imageTransp : _config.BarImageTransparency);
				if (parameters.TryGetValue(13, out obj) && obj is string imageOutlineColor)
					Image_Outline_Color = StringColorFromHex(imageOutlineColor, parameters.TryGetValue(-13, out obj) && obj is float imageOutlineTransp ? imageOutlineTransp : _config.BarImageOutlineTransparency);
				if (parameters.TryGetValue(14, out obj))
                {
					Image_Outline_Distance = obj is string imageOutlineDistance ? imageOutlineDistance : string.Empty;
					_image_Outline = !string.IsNullOrWhiteSpace(Image_Outline_Distance);
				}
				if (parameters.TryGetValue(15, out obj) && obj is string text && Text != text)
                {
					Text = text;
					isText = true;
				}
				if (parameters.TryGetValue(16, out obj) && obj is int textSize)
					Text_Size = textSize;
				if (parameters.TryGetValue(17, out obj) && obj is string textColor)
					Text_Color = StringColorFromHex(textColor, parameters.TryGetValue(-17, out obj) && obj is float textTransp ? textTransp : _config.BarTextTransparency);
				if (parameters.TryGetValue(18, out obj) && obj is string textFont)
					Text_Font = textFont;
				if (parameters.TryGetValue(19, out obj) && obj is int textOffsetHorizontal)
					Text_Offset_Horizontal = textOffsetHorizontal;
				if (parameters.TryGetValue(20, out obj) && obj is string textOutlineColor)
					Text_Outline_Color = StringColorFromHex(textOutlineColor, parameters.TryGetValue(-20, out obj) && obj is float textOutlineTransp ? textOutlineTransp : _config.BarTextOutlineTransparency);
				if (parameters.TryGetValue(21, out obj))
				{
					Text_Outline_Distance = obj is string textOutlineDistance ? textOutlineDistance : string.Empty;
					_text_Outline = !string.IsNullOrWhiteSpace(Text_Outline_Distance);
				}
				if (parameters.TryGetValue(22, out obj) && obj is string subText && SubText != subText)
				{
					SubText = subText;
					isText = true;
				}
				if (parameters.TryGetValue(23, out obj) && obj is int subTextSize)
					SubText_Size = subTextSize;
				if (parameters.TryGetValue(24, out obj) && obj is string subTextColor)
					SubText_Color = StringColorFromHex(subTextColor, parameters.TryGetValue(-24, out obj) && obj is float subTextTransp ? subTextTransp : _config.BarSubTextTransparency);
				if (parameters.TryGetValue(25, out obj) && obj is string subTextFont)
					SubText_Font = subTextFont;
				if (parameters.TryGetValue(26, out obj) && obj is string subTextOutlineColor)
					SubText_Outline_Color = StringColorFromHex(subTextOutlineColor, parameters.TryGetValue(-26, out obj) && obj is float subTextOutlineTransp ? subTextOutlineTransp : _config.BarSubTextOutlineTransparency);
				if (parameters.TryGetValue(27, out obj))
                {
					SubText_Outline_Distance = obj is string subTextOutlineDistance ? subTextOutlineDistance : string.Empty;
					_subText_Outline = !string.IsNullOrWhiteSpace(SubText_Outline_Distance);
				}
				if (parameters.TryGetValue(28, out obj) && obj is double timeStampStart)
					TimeStampStart = timeStampStart;
				if (parameters.TryGetValue(29, out obj) && obj is double timeStamp)
					TimeStamp = timeStamp;
				if (parameters.TryGetValue(30, out obj) && obj is double timeStampDestroy)
					TimeStampDestroy = timeStampDestroy;
				if (parameters.TryGetValue(31, out obj) && obj is float progress && Progress != progress)
				{
					Progress = progress;
					isProgress = true;
                }
				if (parameters.TryGetValue(32, out obj) && obj is bool progressReverse)
					Progress_Reverse = progressReverse;
				if (parameters.TryGetValue(33, out obj) && obj is string progressColor)
					Progress_Color = StringColorFromHex(progressColor, parameters.TryGetValue(-33, out obj) && obj is float progressTransp ? progressTransp : _config.BarProgressTransparency);
				if (parameters.TryGetValue(34, out obj) && obj is string progressOffsetMin)
					Progress_OffsetMin = progressOffsetMin;
				if (parameters.TryGetValue(35, out obj) && obj is string progressOffsetMax)
					Progress_OffsetMax = progressOffsetMax;
				if (parameters.TryGetValue(36, out obj) && obj is string command)
					Command = command;
				if (parameters.TryGetValue(37, out obj) && obj is IEnumerable<string> playerCommands)
					PlayerCommands = new List<string>(playerCommands);
				if (parameters.TryGetValue(38, out obj) && obj is IEnumerable<string> consoleCommands)
					ConsoleCommands = new List<string>(consoleCommands);
			}
			
			private void InitUiNames()
			{
				UiNames[0] = $"{BarID}_ButtonLayer";
                UiNames[1] = $"{UiNames[0]}_Button";
                UiNames[2] = $"{BarID}_MainLayer";
                UiNames[3] = $"{UiNames[2]}_Content";
                UiNames[4] = $"{UiNames[2]}_ProgressBar";
                UiNames[5] = $"{UiNames[4]}_Offset";
                UiNames[6] = $"{UiNames[4]}_Fill";
			}
			
			public void HandleBarDeletion(BasePlayer player)
			{
                CuiHelper.DestroyUi(player, BarID);
                if (PlayerCommands != null)
                {
                    for (int i = 0; i < PlayerCommands.Count; i++)
                        player.Command(PlayerCommands[i].Replace(Placeholders_UserId, player.UserIDString).Replace(Placeholders_UserName, player.displayName));
				}
                if (ConsoleCommands != null)
                {
                    for (int i = 0; i < ConsoleCommands.Count; i++)
                        Instance.rust.RunServerCommand(ConsoleCommands[i].Replace(Placeholders_UserId, player.UserIDString).Replace(Placeholders_UserName, player.displayName));
				}
                Interface.CallHook(Hooks_OnBarDeleted, player, Plugin, Id);
			}
		}

        #endregion

        #region ~PlayerWatcher~
		private void InitPlayer(BasePlayer player)
        {
            RemovePlayer(player);
            player.gameObject.AddComponent<PlayerWatcher>();
        }

        private void RemovePlayer(BasePlayer player)
        {
            var playerWatcher = player.gameObject.GetComponent<PlayerWatcher>();
            if (playerWatcher != null)
                UnityEngine.Object.DestroyImmediate(playerWatcher);
            _playersList.Remove(player.userID);
        }

        private static bool TryGetWatcher(ulong userID, out PlayerWatcher result) => _playersList.TryGetValue(userID, out result);
		
		public class PlayerWatcher : MonoBehaviour
		{
			public ulong UserID { get; private set; }
			public BasePlayer Player { get; private set; }
			
			private List<AdvancedBar> _bars = Pool.Get<List<AdvancedBar>>();
			public List<AdvancedBar> Bars { get { return _bars; } }
			
			private Dictionary<string, float> _addedItems = Pool.Get<Dictionary<string, float>>();
            public Dictionary<string, float> AddedItems { get { return _addedItems; } }
			
			public bool IsDeadOrSleeping { get; set; }
			public bool InVanillaUi { get; set; }
			public bool CanShowBars => !IsDeadOrSleeping && !InVanillaUi;
			public bool IsUiActive { get; set; }
			
			private bool _inOwnBuilding;
			public bool InOwnBuilding
			{
				get { return _inOwnBuilding; }
				private set
				{
					if (_inOwnBuilding != value)
					{
						_inOwnBuilding = value;
						Interface.CallHook(_inOwnBuilding ? Hooks_GainedBuilding : Hooks_LostBuilding, Player);
					}
				}
			}
			
			private int _clientBars = -1;
            public int ClientBars
            {
                get { return _clientBars; }
                private set
                {
                    if (_clientBars != value)
                    {
                        _clientBars = value;
                        Instance.DrawBars(this);
                    }
                }
            }
			
			private float _nextPrivilegeTime;
			private int _mountCount, _privilegeCount;
			public SimplePrivilege BoatPrivilege;
			
			private void Awake()
			{
				Player = GetComponentInParent<BasePlayer>();
				if (Instance == null || !Player.IsValid())
				{
					Destroy(this);
					return;
				}
				
				UserID = Player.userID;
                _playersList[UserID] = this;
				IsDeadOrSleeping = Player.IsDead() || Player.IsSleeping();
				if (Player.isMounted)
                    OnMount(Player.GetMounted());
				if (CanShowBars)
                    StartCount();
                InvokeRepeating("HandleBarsUpdate", 1f, 1f);
			}
			
			private void CountClientBars()
			{
				if (Player == null)
				{
					_playersList?.Remove(UserID);
					Destroy(this);
					return;
				}
				
				int result = 0;
				var metabolism = Player.metabolism;
				if (metabolism.poison.value > 1f) result++;
				if (metabolism.oxygen.value < 1f) result++;
                if (metabolism.temperature.value < 5f || metabolism.temperature.value > 40f) result++;
                if (metabolism.wetness.value >= 0.011f) result++;
                if (metabolism.comfort.value > 0f) result++;
                if (metabolism.hydration.value <= 40f) result++;
                if (metabolism.calories.value < 40f) result++;
                if (metabolism.radiation_poison.value > 0f) result++;
				if (metabolism.bleeding.value != 0f) result++;
				if (Player.currentCraftLevel > 0f) result++;
                if (Player.inventory.crafting.queue.Count > 0) result++;

				Modifier modifier;
				bool hasBuf = false, hasDeBuf = false;
				for (int i = 0; i < Player.modifiers.All.Count; i++)
				{
					modifier = Player.modifiers.All[i];
					if (modifier == null || modifier.Source == Modifier.ModifierSource.Interaction)
						continue;
					if (modifier.Source == Modifier.ModifierSource.Dart)
						hasDeBuf = true;
					else
						hasBuf = true;
				}
				if (hasBuf) result++;
				if (hasDeBuf) result++;
				
				if (Player.PetEntity != null) result++;
				if (Player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone)) result++;
				if (Player.playerFlags.HasFlag(BasePlayer.PlayerFlags.ModifyClan)) result++;
				if (Player.GetHeldEntity()?.HasShieldEquipped() ?? false) result++;
				result += _mountCount;
				
				float realtimeSinceStartup = Time.realtimeSinceStartup;
				if (realtimeSinceStartup > _nextPrivilegeTime)
				{
					CountPrivilege();
					_nextPrivilegeTime = realtimeSinceStartup + _config.CountIntervalPrivilege;
				}
				result += _privilegeCount;
				var remList = Pool.Get<List<string>>();
				foreach (var kvp in AddedItems)
                {
                    if (kvp.Value < realtimeSinceStartup)
                        remList.Add(kvp.Key);
                    else
                        result++;
                }
                foreach (var remItem in remList)
                    AddedItems.Remove(remItem);
                Pool.FreeUnmanaged(ref remList);
				ClientBars = result;
			}
			
			private void CountPrivilege()
            {
				int counter = 0;
				bool isAuthed, isBuildingAuthed = false, isBuildingBlocked = false,
					isRightItem = Instance.IsBuildingBlockItem(Player.GetActiveItem()?.info);
				var buildingPrivilege = GetBuildingPrivilege(Player);
				if (buildingPrivilege != null)
                {
					if (buildingPrivilege.IsAuthed(Player))
					{
						isBuildingAuthed = true;
						counter += 2;
					}
					else if (isRightItem)
						isBuildingBlocked = true;
				}
				
				SimplePrivilege mainPrivilege = null, simplePrivilege;
				if (_config.UseVehiclePrivilege)
				{
					simplePrivilege = Player.GetVehicleBuildingPrivilege(true) as SimplePrivilege;
					if (simplePrivilege != null)
                    {
                        mainPrivilege = simplePrivilege;
                        isAuthed = !Player.cachedVehicleBuildingPrivilegeBlocked;
                        if (isBuildingAuthed && !isAuthed)
                            counter = 0;
                        if (isAuthed)
                            counter++;
                        else if (isRightItem)
                            isBuildingBlocked = true;
                    }
				}
				
				if (_config.UseEntityPrivilege)
				{
					simplePrivilege = Instance.GetEntityBuildingPrivilege(Player);
                    if (simplePrivilege != null)
                    {
                        mainPrivilege ??= simplePrivilege;
                        isAuthed = !Player.cachedEntityBuildingPrivilegeBlocked;
                        if (isBuildingAuthed && !isAuthed)
                            counter = 0;
                        if (isAuthed)
                            counter++;
                        else if (isRightItem)
                            isBuildingBlocked = true;
                    }
				}
				
				if (mainPrivilege != null)
					InOwnBuilding = mainPrivilege.IsAuthed(Player);
				else
					InOwnBuilding = buildingPrivilege != null && buildingPrivilege.IsAuthed(Player);
				if (isBuildingBlocked)
					counter++;
				_privilegeCount = Math.Clamp(counter, 0, 2);
			}
			
			public void HandleBarsUpdate()
			{
				AdvancedBar bar;
				int startIndex = int.MaxValue;
				double currentTimestamp = Network.TimeEx.currentTimestamp;
				for (int i = Bars.Count - 1; i >= 0; i--)
                {
					bar = Bars[i];
					if (bar == null)
                    {
						if (i < startIndex)
							startIndex = i;
						Bars.RemoveAt(i);
						continue;
					}
					if (bar.ShouldRemove)
                    {
                        if (i < startIndex)
                            startIndex = i;
                        bar.HandleBarDeletion(Player);
                        Bars.RemoveAt(i);
                        continue;
                    }
					if (bar.BarType == AdvancedBarType.Default)
						continue;
					
					if (bar.TimeStamp <= currentTimestamp || (bar.TimeStampDestroy > 0d && bar.TimeStampDestroy <= currentTimestamp))
                    {
                        if (i < startIndex)
                            startIndex = i;
                        bar.HandleBarDeletion(Player);
						Bars.RemoveAt(i);
						continue;
					}
					if (bar.BarType == AdvancedBarType.TimeProgress || bar.BarType == AdvancedBarType.TimeProgressCounter)
                    {
                        bar.Progress = (float)((currentTimestamp - bar.TimeStampStart) / (bar.TimeStamp - bar.TimeStampStart));
                        if (bar.Progress_Reverse)
                            bar.Progress = 1f - bar.Progress;
					}
				}
				
				if (!IsUiActive)
					return;
				if (startIndex > 0)
                {
                    for (int i = 0; i < Bars.Count; i++)
                    {
                        if (i == startIndex)
                            break;
                        bar = Bars[i];
                        if (bar.BarType == AdvancedBarType.TimeCounter)
                            Instance.DrawText(Player, bar);
                        else if (bar.BarType == AdvancedBarType.TimeProgress || bar.BarType == AdvancedBarType.TimeProgressCounter)
                        {
                            DrawProgress(Player, bar);
                            Instance.DrawText(Player, bar);
                        }
                    }
                }
                if (startIndex != int.MaxValue && startIndex <= Bars.Count)
                    Instance.DrawBars(this, startIndex);
			}
			
			public void StartCount()
			{
				if (Player == null)
				{
					_playersList?.Remove(UserID);
					Destroy(this);
					return;
				}
				Player.InvokeRepeating(CountClientBars, 0f, _config.CountInterval);
			}
			
			public void OnMount(BaseMountable mount)
            {
				if (mount == null)
					_mountCount = 0;
				else if (_doubleMountsList.Contains(mount.ShortPrefabName))
					_mountCount = 2;
                else if (_mountsList.Contains(mount.ShortPrefabName))
					_mountCount = 1;
				else if (!mount.isMobile)
					_mountCount = 0;
			}
			
			public void OnDeathOrSleeping()
            {
				if (IsDeadOrSleeping)
					return;
				StopCountAndClear();
				IsDeadOrSleeping = true;
				InVanillaUi = false;
				InOwnBuilding = false;
				CuiHelper.DestroyUi(Player, AdvancedStatusName);
			}
			
			public void OnVanillaUi(bool isStart)
            {
				if (isStart)
				{
					if (!InVanillaUi)
					{
						StopCountAndClear();
						InVanillaUi = true;
						CuiHelper.DestroyUi(Player, AdvancedStatusName);
					}
				}
				else if (InVanillaUi)
                {
					InVanillaUi = false;
					if (CanShowBars)
						StartCount();
				}
			}
			
			private void StopCountAndClear()
            {
				IsUiActive = false;
				Player?.CancelInvoke(CountClientBars);
				AddedItems.Clear();
                _clientBars = -1;
				_nextPrivilegeTime = 0f;
			}
			
			private void OnDestroy()
            {
				CancelInvoke("HandleBarsUpdate");
				StopCountAndClear();
				Pool.FreeUnmanaged(ref _bars);
				Pool.FreeUnmanaged(ref _addedItems);
			}
		}
		#endregion
	}
}