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
*  GitHub repository page: https://github.com/IIIaKa/MonumentsWatcher
*  
*  uMod plugin page: https://umod.org/plugins/monuments-watcher
*  uMod license: https://umod.org/plugins/monuments-watcher#license
*  
*  Codefling plugin page: https://codefling.com/plugins/monuments-watcher
*  Codefling license: https://codefling.com/plugins/monuments-watcher?tab=downloads_field_4
*  
*  Lone.Design plugin page: https://lone.design/product/monuments-watcher/
*
*  Copyright © 2024-2026 IIIaKa
*/

using HarmonyLib;
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Rust;
using Rust.Ai.Gen2;
using Facepunch;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
	[Info("Monuments Watcher", "IIIaKa", "0.1.12")]
	[Description("An API plugin which helps interact with players, NPCs, animals, and other entities inside monuments.")]
	class MonumentsWatcher : RustPlugin
    {
		#region ~Variables~
        private static MonumentsWatcher Instance { get; set; }
		private bool _isReady = false;
		private Harmony _harmony;
		private const string IdForHarmony = "iiiaka.monumentswatcher", PERMISSION_ADMIN = "monumentswatcher.admin", Str_Leave = "leave", Str_Death = "death", Str_MonumentDestroyed = "monument_destroyed", Str_CargoShip = "CargoShip",
			Hooks_OnLoaded = "OnMonumentsWatcherLoaded", Hooks_OnCargoWatcherCreated = "OnCargoWatcherCreated", Hooks_OnCargoWatcherDeleted = "OnCargoWatcherDeleted",
			Hooks_OnSpawnableWatcherCreated = "OnSpawnableWatcherCreated", Hooks_OnSpawnableWatcherDeleted = "OnSpawnableWatcherDeleted",
			Hooks_OnPlayerEnteredMonument = "OnPlayerEnteredMonument", Hooks_OnNpcEnteredMonument = "OnNpcEnteredMonument", Hooks_OnAnimalEnteredMonument = "OnAnimalEnteredMonument", Hooks_OnEntityEnteredMonument = "OnEntityEnteredMonument",
			Hooks_OnPlayerExitedMonument = "OnPlayerExitedMonument", Hooks_OnNpcExitedMonument = "OnNpcExitedMonument", Hooks_OnAnimalExitedMonument = "OnAnimalExitedMonument", Hooks_OnEntityExitedMonument = "OnEntityExitedMonument";
        private Hash<string, MonumentWatcher> _monumentsList = new Hash<string, MonumentWatcher>();
		private static Hash<ulong, List<MonumentWatcher>> _playersInMonuments, _npcsInMonuments, _animalsInMonuments, _entitiesInMonuments;
		private readonly string[] _defaultHooks = new string[] { "OnEntitySpawned", "OnEntityDeath", "OnEntityKill", "OnPlayerTeleported" };
		#endregion

        #region ~Configuration~
        private static Configuration _config;
		
		private class Configuration
		{
			[JsonProperty(PropertyName = "Chat command")]
			public string Command = string.Empty;
			
			[JsonProperty(PropertyName = "Is it worth enabling GameTips for messages?")]
			public bool GameTips_Enabled = true;
			
			[JsonProperty(PropertyName = "List of language keys for creating language files")]
			public List<string> LanguageKeys;
			
			[JsonProperty(PropertyName = "Is it worth recreating boundaries(excluding custom monuments) upon detecting a wipe?")]
			public bool RecreateOnWipe = true;
			
			[JsonProperty(PropertyName = "List of tracked categories of monuments. Leave blank to track all")]
			public HashSet<MonumentCategory> TrackedCategories;
			
			[JsonProperty(PropertyName = "Wipe ID")]
			public string WipeID = string.Empty;
			
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
				_config.Command = "monument";
			
			string langKey;
            var oldLangKeys = _config.LanguageKeys ?? new List<string>();
            _config.LanguageKeys = new List<string>() { "en" };
            for (int i = 0; i < oldLangKeys.Count; i++)
            {
                langKey = ToLangKey(oldLangKeys[i]);
                if (langKey != "ru" && !_config.LanguageKeys.Contains(langKey))
                    _config.LanguageKeys.Add(langKey);
            }
			
			_config.TrackedCategories ??= new HashSet<MonumentCategory>();
			SaveConfig();
		}
		
		protected override void SaveConfig() => Config.WriteObject(_config);
		protected override void LoadDefaultConfig() => _config = new Configuration() { Version = Version };
		#endregion

		#region ~Language~
		private Dictionary<string, string> _enLang = new Dictionary<string, string>
		{
			["CmdMain"] = string.Join("\n", new string[]
			{
				"Available commands:\n",
				"<color=#D1CBCB>/monument</color> <color=#D1AB9A>show *monumentID*(optional) *floatValue*(optional)</color> - Display the boundary of the monument you are in or specified. The display will last for the specified time or 30 seconds",
                "<color=#D1CBCB>/monument</color> <color=#D1AB9A>list</color> - List of available monuments",
                "<color=#D1CBCB>/monument</color> <color=#D1AB9A>rotate *monumentID*(optional) *floatValue*(optional)</color> - Rotate the monument you are in or specified, either in the direction you are looking or in the specified direction",
				"<color=#D1CBCB>/monument</color> <color=#D1AB9A>recreate custom/all(optional)</color> - Recreate the boundaries of vanilla/custom/all monuments",
				"\n<color=#D1CBCB>Note:</color> Instead of a monument ID, you can leave it empty, but you must be inside a monument. You can also use the word 'closest' to select the nearest monument to you",
				"\n--------------------------------------------------"
			}),
			["CmdMainShowNotFound"] = "Monument not found! You must be inside a monument, or specify the name or ID of the monument",
			["CmdMainShow"] = "Monument '{0}' is located at coordinates: {1}",
			["CmdMainShowList"] = "{1} monuments found and displayed for the key '{0}'",
			["CmdMainList"] = string.Join("\n", new string[]
			{
				"List of available monuments:\n",
				"{0}",
				"\n--------------------------------------------------"
			}),
			["CmdMainRotateNotFound"] = "You must be inside a monument and looking in the correct direction, or specify the name or ID of the monument along with the Y-coordinate for direction",
			["CmdMainRotated"] = "Successful rotation of the {0} by Y-coordinate({1})!",
			["CmdMainRecreated"] = "The boundaries of the monuments have been successfully recreated!",
			[Str_CargoShip] = "CargoShip",
			["airfield_1"] = "Airfield",
			["airfield_1_station"] = "Airfield Station",
			["apartments_complex_1"] = "Apartments Complex",
			["apartments_complex_1_station"] = "Apartments Complex Station",
			["arctic_research_base_a"] = "Arctic Research Base",
			["arctic_research_base_a_station"] = "Arctic Station",
			["bandit_town"] = "Bandit Camp",
			["bandit_town_station"] = "Bandit Station",
			["compound"] = "Outpost",
			["compound_station"] = "Outpost Station",
			["deepsea_floatingcity1"] = "Floating City",
            ["deepsea_floatingcity2"] = "Floating City",
            ["deepsea_floatingcity3"] = "Floating City",
			["deepsea_floatingcity4"] = "Floating City",
			["deepsea_island_tropical1"] = "Tropical Island",
            ["deepsea_island_tropical2"] = "Tropical Island",
            ["deepsea_island_tropical3"] = "Tropical Island",
            ["deepsea_island_tropical4"] = "Tropical Island",
			["desert_military_base_a"] = "Abandoned Military Base",
			["desert_military_base_a_station"] = "Dune Station",
			["desert_military_base_b"] = "Abandoned Military Base",
			["desert_military_base_b_station"] = "Dune Station",
			["desert_military_base_c"] = "Abandoned Military Base",
			["desert_military_base_c_station"] = "Dune Station",
			["desert_military_base_d"] = "Abandoned Military Base",
			["desert_military_base_d_station"] = "Dune Station",
			["excavator_1"] = "Giant Excavator Pit",
			["excavator_1_station"] = "Excavator Station",
			["ferry_terminal_1"] = "Ferry Terminal",
			["ferry_terminal_1_station"] = "Ferry Terminal Station",
			["fishing_village_a"] = "Large Fishing Village",
			["fishing_village_a_station"] = "Large Fishing Station",
			["fishing_village_b"] = "Fishing Village",
			["fishing_village_b_station"] = "Fishing Station",
			["fishing_village_c"] = "Fishing Village",
			["fishing_village_c_station"] = "Fishing Station",
			["gas_station_1"] = "Oxum's Gas Station",
			["ghostship"] = "Ghost Ship",
            ["ghostship_b"] = "Ghost Ship",
            ["ghostship_c"] = "Ghost Ship",
            ["ghostship_d"] = "Ghost Ship",
			["harbor_1"] = "Large Harbor",
			["harbor_1_station"] = "Large Harbor Station",
			["harbor_2"] = "Small Harbor",
			["harbor_2_station"] = "Harbor Station",
			["jungle_ziggurat_a"] = "Ziggurat",
			["jungle_ziggurat_a_station"] = "Ziggurat Station",
			["junkyard_1"] = "Junkyard",
			["junkyard_1_station"] = "Junkyard Station",
			["launch_site_1"] = "Launch Site",
			["launch_site_1_station"] = "Launch Site Station",
			["lighthouse"] = "Lighthouse",
			["military_tunnel_1"] = "Military Tunnel",
			["military_tunnel_1_station"] = "Military Tunnel Station",
			["mining_quarry_a"] = "Sulfur Quarry",
			["mining_quarry_b"] = "Stone Quarry",
			["mining_quarry_c"] = "HQM Quarry",
			["nuclear_missile_silo"] = "Missile Silo",
			["nuclear_missile_silo_station"] = "Silo Station",
			["oilrig_1"] = "Large Oil Rig",
			["oilrig_2"] = "Oil Rig",
			["powerplant_1"] = "Power Plant",
			["powerplant_1_station"] = "Power Plant Station",
			["radtown_1"] = "Rad Town",
            ["radtown_1_station"] = "Rad Town Station",
			["radtown_small_3"] = "Sewer Branch",
			["radtown_small_3_station"] = "Sewer Branch Station",
			["satellite_dish"] = "Satellite Dish",
			["satellite_dish_station"] = "Satellite Station",
			["sphere_tank"] = "The Dome",
			["sphere_tank_station"] = "The Dome Station",
			["stables_a"] = "Ranch",
			["stables_a_station"] = "Ranch Station",
			["stables_b"] = "Large Barn",
			["stables_b_station"] = "Barn Station",
			["station-sn-0"] = "Tunnel Station",
			["station-sn-1"] = "Tunnel Station",
			["station-sn-2"] = "Tunnel Station",
			["station-sn-3"] = "Tunnel Station",
			["station-we-0"] = "Tunnel Station",
			["station-we-1"] = "Tunnel Station",
			["station-we-2"] = "Tunnel Station",
			["station-we-3"] = "Tunnel Station",
			["supermarket_1"] = "Abandoned Supermarket",
			["swamp_a"] = "Wild Swamp",
			["swamp_b"] = "Wild Swamp",
			["swamp_c"] = "Abandoned Cabins",
			["trainyard_1"] = "Train Yard",
			["trainyard_1_station"] = "Train Yard Station",
			["underwater_lab_a"] = "Underwater Lab",
			["underwater_lab_b"] = "Underwater Lab",
			["underwater_lab_c"] = "Underwater Lab",
			["underwater_lab_d"] = "Underwater Lab",
			["warehouse"] = "Mining Outpost",
			["water_treatment_plant_1"] = "Water Treatment Plant",
			["water_treatment_plant_1_station"] = "Water Treatment Station",
			["entrance_bunker_a"] = "Bunker Entrance",
			["entrance_bunker_b"] = "Bunker Entrance",
			["entrance_bunker_c"] = "Bunker Entrance",
			["entrance_bunker_d"] = "Bunker Entrance",
			["cave_small_easy"] = "Small Cave",
			["cave_small_medium"] = "Medium Cave",
			["cave_small_hard"] = "Medium Cave",
			["cave_medium_easy"] = "Medium Cave",
			["cave_medium_medium"] = "Medium Cave",
			["cave_medium_hard"] = "Medium Cave",
			["cave_large_medium"] = "Medium Cave",
			["cave_large_hard"] = "Medium Cave",
			["cave_large_sewers_hard"] = "Large Cave",
			["ice_lake_1"] = "Ice Lake",
			["ice_lake_2"] = "Ice Lake",
			["ice_lake_3"] = "Large Ice Lake",
			["ice_lake_4"] = "Small Ice Lake",
			["power_sub_small_1"] = "Substation",
			["power_sub_small_2"] = "Substation",
			["power_sub_big_1"] = "Large Substation",
			["power_sub_big_2"] = "Large Substation",
			["jungle_ruins_a"] = "Jungle Ruins",
            ["jungle_ruins_b"] = "Jungle Ruins",
            ["jungle_ruins_c"] = "Jungle Ruins",
            ["jungle_ruins_d"] = "Jungle Ruins",
			["jungle_ruins_e"] = "Jungle Ruins",
			["tropical_island_dwelling_ruins_b"] = "Tropical Ruins",
            ["tropical_island_dwelling_ruins_c"] = "Tropical Ruins",
            ["tropical_island_dwelling_ruins_d"] = "Tropical Ruins",
			["water_well_a"] = "Water Well",
			["water_well_b"] = "Water Well",
			["water_well_c"] = "Water Well",
			["water_well_d"] = "Water Well",
			["water_well_e"] = "Water Well"
		};
		
		private Dictionary<string, string> _ruLang = new Dictionary<string, string>
		{
			["CmdMain"] = string.Join("\n", new string[]
			{
				"Доступные команды:\n",
				"<color=#D1CBCB>/monument</color> <color=#D1AB9A>show *айдиМонумента*(опционально) *дробноеЗначение*(опционально)</color> - Отобразить границу монумента, в котором вы находитесь или указали. Отображение будет в течении указанного времени или 30 секунд",
                "<color=#D1CBCB>/monument</color> <color=#D1AB9A>list</color> - Список доступных монументов",
                "<color=#D1CBCB>/monument</color> <color=#D1AB9A>rotate *айдиМонумента*(опционально) *дробноеЗначение*(опционально)</color> - Повернуть монумент, в котором вы находитесь или указали, в направлении вашего взгляда либо в указанном направлении",
                "<color=#D1CBCB>/monument</color> <color=#D1AB9A>recreate custom/all(опционально)</color> - Пересоздать границы ванильных/кастомных/всех монументов",
				"\n<color=#D1CBCB>Примечание:</color> Вместо айди монумента вы можете ничего не указывать, но должны находиться внутри монумента. Также можно указать слово 'closest', чтобы выбрать ближайший к вам монумент",
				"\n--------------------------------------------------"
			}),
			["CmdMainShowNotFound"] = "Монумент не найден! Вы должны находиться в монументе либо указать имя или ID монумента",
			["CmdMainShow"] = "Монумент '{0}' расположен по координатам: {1}",
			["CmdMainShowList"] = "По ключу '{0}' найдено и отображено {1} монументов",
			["CmdMainList"] = string.Join("\n", new string[]
            {
                "Список доступных монументов:\n",
                "{0}",
                "\n--------------------------------------------------"
            }),
			["CmdMainRotateNotFound"] = "Вы должны находиться в монументе и смотреть в нужном направлении, либо указать имя или ID монумента и Y-координату для направления",
			["CmdMainRotated"] = "Успешный поворот у {0} по Y координате({1})!",
			["CmdMainRecreated"] = "Границы монументов успешно пересозданы!",
			[Str_CargoShip] = "Грузовой корабль",
			["airfield_1"] = "Аэропорт",
			["airfield_1_station"] = "Станция Аэропорт",
			["apartments_complex_1"] = "Жилой комплекс",
            ["apartments_complex_1_station"] = "Станция Комплекс",
			["arctic_research_base_a"] = "Арктическая база",
			["arctic_research_base_a_station"] = "Станция Арктическая",
			["bandit_town"] = "Лагерь бандитов",
			["bandit_town_station"] = "Станция бандитов",
			["compound"] = "Город",
			["compound_station"] = "Станция Город",
			["deepsea_floatingcity1"] = "Плавающий город",
            ["deepsea_floatingcity2"] = "Плавающий город",
            ["deepsea_floatingcity3"] = "Плавающий город",
			["deepsea_floatingcity4"] = "Плавающий город",
			["deepsea_island_tropical1"] = "Тропический остров",
			["deepsea_island_tropical2"] = "Тропический остров",
			["deepsea_island_tropical3"] = "Тропический остров",
			["deepsea_island_tropical4"] = "Тропический остров",
			["desert_military_base_a"] = "Заброшенная военная база",
			["desert_military_base_a_station"] = "Станция Дюна",
			["desert_military_base_b"] = "Заброшенная военная база",
			["desert_military_base_b_station"] = "Станция Дюна",
			["desert_military_base_c"] = "Заброшенная военная база",
			["desert_military_base_c_station"] = "Станция Дюна",
			["desert_military_base_d"] = "Заброшенная военная база",
			["desert_military_base_d_station"] = "Станция Дюна",
			["excavator_1"] = "Гигантский экскаватор",
			["excavator_1_station"] = "Станция Экскаваторная",
			["ferry_terminal_1"] = "Паромный терминал",
			["ferry_terminal_1_station"] = "Станция Паромщиков",
			["fishing_village_a"] = "Большая рыбацкая деревня",
			["fishing_village_a_station"] = "Станция Рыбаков",
			["fishing_village_b"] = "Рыбацкая деревня",
			["fishing_village_b_station"] = "Станция Рыбаков",
			["fishing_village_c"] = "Рыбацкая деревня",
			["fishing_village_c_station"] = "Станция Рыбаков",
			["gas_station_1"] = "Заправка",
			["ghostship"] = "Корабль призрак",
			["ghostship_b"] = "Корабль призрак",
			["ghostship_c"] = "Корабль призрак",
			["ghostship_d"] = "Корабль призрак",
			["harbor_1"] = "Большой порт",
			["harbor_1_station"] = "Станция Моряков",
			["harbor_2"] = "Порт",
			["harbor_2_station"] = "Станция Моряков",
			["jungle_ziggurat_a"] = "Зиккурат",
            ["jungle_ziggurat_a_station"] = "Станция Зиккурат",
			["junkyard_1"] = "Свалка",
			["junkyard_1_station"] = "Станция Мусорщиков",
			["launch_site_1"] = "Космодром",
			["launch_site_1_station"] = "Станция Космонавтов",
			["lighthouse"] = "Маяк",
			["military_tunnel_1"] = "Военные туннели",
			["military_tunnel_1_station"] = "Станция Туннельная",
			["mining_quarry_a"] = "Серный карьер",
			["mining_quarry_b"] = "Каменный карьер",
			["mining_quarry_c"] = "МВК карьер",
			["nuclear_missile_silo"] = "Ракетная пусковая шахта",
			["nuclear_missile_silo_station"] = "Станция Ракетная",
			["oilrig_1"] = "Большая нефтяная вышка",
			["oilrig_2"] = "Нефтяная вышка",
			["powerplant_1"] = "Электростанция",
			["powerplant_1_station"] = "Станция Электриков",
			["radtown_1"] = "Токсичная деревня",
            ["radtown_1_station"] = "Станция Легаси",
			["radtown_small_3"] = "Канализационный отвод",
			["radtown_small_3_station"] = "Станция Отвод",
			["satellite_dish"] = "Спутниковая тарелка",
			["satellite_dish_station"] = "Станция Связистов",
			["sphere_tank"] = "Сфера",
			["sphere_tank_station"] = "Станция Сфера",
			["stables_a"] = "Ранчо",
			["stables_a_station"] = "Станция Ранчо",
			["stables_b"] = "Большой амбар",
			["stables_b_station"] = "Станция Амбарная",
			["station-sn-0"] = "Станция метро",
			["station-sn-1"] = "Станция метро",
			["station-sn-2"] = "Станция метро",
			["station-sn-3"] = "Станция метро",
			["station-we-0"] = "Станция метро",
			["station-we-1"] = "Станция метро",
			["station-we-2"] = "Станция метро",
			["station-we-3"] = "Станция метро",
			["supermarket_1"] = "Супермаркет",
			["swamp_a"] = "Болото",
			["swamp_b"] = "Болото",
			["swamp_c"] = "Заброшенные хижины",
			["trainyard_1"] = "Железнодорожное депо",
			["trainyard_1_station"] = "Станция Железнодорожников",
			["underwater_lab_a"] = "Подводная лаборатория",
			["underwater_lab_b"] = "Подводная лаборатория",
			["underwater_lab_c"] = "Подводная лаборатория",
			["underwater_lab_d"] = "Подводная лаборатория",
			["warehouse"] = "Склад",
			["water_treatment_plant_1"] = "Очистные сооружения",
			["water_treatment_plant_1_station"] = "Станция Очистная",
			["entrance_bunker_a"] = "Вход в бункер",
			["entrance_bunker_b"] = "Вход в бункер",
			["entrance_bunker_c"] = "Вход в бункер",
			["entrance_bunker_d"] = "Вход в бункер",
			["cave_small_easy"] = "Маленькая пещера",
			["cave_small_medium"] = "Средняя пещера",
			["cave_small_hard"] = "Средняя пещера",
			["cave_medium_easy"] = "Средняя пещера",
			["cave_medium_medium"] = "Средняя пещера",
			["cave_medium_hard"] = "Средняя пещера",
			["cave_large_medium"] = "Средняя пещера",
			["cave_large_hard"] = "Средняя пещера",
			["cave_large_sewers_hard"] = "Большая пещера",
			["ice_lake_1"] = "Замерзшее озеро",
			["ice_lake_2"] = "Замерзшее озеро",
			["ice_lake_3"] = "Большое замерзшее озеро",
			["ice_lake_4"] = "Маленькое замерзшее озеро",
			["power_sub_small_1"] = "Подстанция",
			["power_sub_small_2"] = "Подстанция",
			["power_sub_big_1"] = "Большая подстанция",
			["power_sub_big_2"] = "Большая подстанция",
			["jungle_ruins_a"] = "Руины",
            ["jungle_ruins_b"] = "Руины",
            ["jungle_ruins_c"] = "Руины",
            ["jungle_ruins_d"] = "Руины",
			["jungle_ruins_e"] = "Руины",
			["tropical_island_dwelling_ruins_b"] = "Тропические руины",
            ["tropical_island_dwelling_ruins_c"] = "Тропические руины",
            ["tropical_island_dwelling_ruins_d"] = "Тропические руины",
			["water_well_a"] = "Колодец с водой",
			["water_well_b"] = "Колодец с водой",
			["water_well_c"] = "Колодец с водой",
			["water_well_d"] = "Колодец с водой",
			["water_well_e"] = "Колодец с водой"
		};
        #endregion

        #region ~Methods~
		private System.Collections.IEnumerator InitMonuments()
		{
			LoadDefaultBounds();
			LoadBoundsConfig(_monumentsBoundsPath, out _monumentsBounds);
			LoadBoundsConfig(_customMonumentsBoundsPath, out _customMonumentsBounds);
			ClearWatchers();
			yield return null;
			
			string monumentKey, prefab;
			var customIds = new Dictionary<MonumentInfo, string>();
            foreach (var kvp in World.SpawnedPrefabs)
            {
                foreach (var go in kvp.Value)
                {
					if (go == null || !go.name.EndsWith("monument_marker.prefab") || !go.TryGetComponent(out MonumentInfo monument))
						continue;
					monumentKey = System.Text.RegularExpressions.Regex.Replace(kvp.Key.Replace(" ", "_"), @"[^\w\d_]", string.Empty).ToLowerInvariant();
					if (!string.IsNullOrWhiteSpace(monumentKey))
						customIds[monument] = monumentKey;
				}
			}
			yield return null;
			
			foreach (var entity in BaseNetworkable.serverEntities)
            {
				if (!entity.IsValid())
					continue;
				if (entity is CargoShip cargoShip)
					CreateCargoWatcher(cargoShip);
				else if (entity is DeepSeaFloatingCity floatingCity)
					CreateSpawnableWatcher(floatingCity, MonumentCategory.SafeZone);
				else if (entity is DeepSeaIsland deepSeaIsland)
					CreateSpawnableWatcher(deepSeaIsland, MonumentCategory.DeepSeaIsland);
				else if (entity is Prefabs.Misc.GhostShip ghostShip)
					CreateSpawnableWatcher(ghostShip, MonumentCategory.RadTownWater);
				else if (entity is NPCDwelling npcDwelling)
				{
					if (npcDwelling.PrefabName.Contains("tropical_island_dwelling_ruins_", StringComparison.OrdinalIgnoreCase))
						CreateSpawnableWatcher(npcDwelling, MonumentCategory.Ruins);
				}
			}
			yield return null;
			
			int miningoutpost = 0, lighthouse = 0, gasstation = 0, supermarket = 0, tunnel = 0, bunker = 0, cave = 0, icelake = 0, power = 0, waterwell = 0;
			foreach (var monument in TerrainMeta.Path.Monuments)
            {
				prefab = monument.name.ToLower();
				if (prefab.EndsWith("monument_marker.prefab"))
                {
					if (customIds.TryGetValue(monument, out monumentKey))
						CreateCustomWatcher(monumentKey, monument.transform, prefab, monumentKey);
					continue;
                }
				
				monumentKey = ClearMonumentName(prefab);
				if (!_defaultBoundsValues.ContainsKey(monumentKey))
					continue;
				if (monument.IsSafeZone || monumentKey == "apartments_complex_1")
                {
					CreateWatcher(monumentKey, MonumentCategory.SafeZone, monument.transform, prefab);
					continue;
                }
				switch (monumentKey)
                {
					case "oilrig_1":
					case "oilrig_2":
					case "underwater_lab_a":
					case "underwater_lab_b":
					case "underwater_lab_c":
					case "underwater_lab_d":
						CreateWatcher(monumentKey, MonumentCategory.RadTownWater, monument.transform, prefab);
						break;
					case "lighthouse":
						lighthouse++;
						CreateWatcher(monumentKey, MonumentCategory.RadTownSmall, monument.transform, prefab, $"_{lighthouse}", $"#{lighthouse}");
						break;
					case "gas_station_1":
						gasstation++;
						CreateWatcher(monumentKey, MonumentCategory.RadTownSmall, monument.transform, prefab, $"_{gasstation}", $"#{gasstation}");
						break;
					case "supermarket_1":
						supermarket++;
						CreateWatcher(monumentKey, MonumentCategory.RadTownSmall, monument.transform, prefab, $"_{supermarket}", $"#{supermarket}");
						break;
					case "warehouse":
						miningoutpost++;
						CreateWatcher(monumentKey, MonumentCategory.RadTownSmall, monument.transform, prefab, $"_{miningoutpost}", $"#{miningoutpost}");
						break;
					case "mining_quarry_a":
					case "mining_quarry_b":
					case "mining_quarry_c":
						CreateWatcher(monumentKey, MonumentCategory.MiningQuarry, monument.transform, prefab);
						break;
					case "swamp_a":
					case "swamp_b":
					case "swamp_c":
						CreateWatcher(monumentKey, MonumentCategory.Swamp, monument.transform, prefab);
						break;
					case "entrance_bunker_a":
					case "entrance_bunker_b":
					case "entrance_bunker_c":
					case "entrance_bunker_d":
						bunker++;
						CreateWatcher(monumentKey, MonumentCategory.BunkerEntrance, monument.transform, prefab, $"_{bunker}", $"#{bunker}");
						break;
					case "cave_small_easy":
					case "cave_small_medium":
					case "cave_small_hard":
					case "cave_medium_easy":
					case "cave_medium_medium":
					case "cave_medium_hard":
					case "cave_large_medium":
					case "cave_large_hard":
					case "cave_large_sewers_hard":
						cave++;
						CreateWatcher(monumentKey, MonumentCategory.Cave, monument.transform, prefab, $"_{cave}", $"#{cave}");
						break;
					case "ice_lake_1":
					case "ice_lake_2":
					case "ice_lake_3":
					case "ice_lake_4":
						icelake++;
						CreateWatcher(monumentKey, MonumentCategory.IceLake, monument.transform, prefab, $"_{icelake}", $"#{icelake}");
						break;
					case "power_sub_small_1":
					case "power_sub_small_2":
					case "power_sub_big_1":
					case "power_sub_big_2":
						power++;
						CreateWatcher(monumentKey, MonumentCategory.PowerSubstation, monument.transform, prefab, $"_{power}", $"#{power}");
						break;
					case "jungle_ruins_a":
                    case "jungle_ruins_b":
                    case "jungle_ruins_c":
                    case "jungle_ruins_d":
					case "jungle_ruins_e":
						CreateWatcher(monumentKey, MonumentCategory.Ruins, monument.transform, prefab);
                        break;
					case "water_well_a":
					case "water_well_b":
					case "water_well_c":
					case "water_well_d":
					case "water_well_e":
						waterwell++;
						CreateWatcher(monumentKey, MonumentCategory.WaterWell, monument.transform, prefab, $"_{waterwell}", $"#{waterwell}");
						break;
					default:
						CreateWatcher(monumentKey, MonumentCategory.RadTown, monument.transform, prefab);
						break;
                }
            }
			yield return null;
			
			Vector3 groundPos;
			float stationDistance = 100f;
			MonumentWatcher parentWatcher;
			foreach (var station in TerrainMeta.Path.DungeonGridCells)
            {
				if (!station.name.Contains("/tunnel-station/station-", StringComparison.OrdinalIgnoreCase))
					continue;
				parentWatcher = null;
				prefab = station.name.ToLower();
				monumentKey = ClearMonumentName(prefab);
				groundPos = new Vector3(station.transform.position.x, TerrainMeta.HeightMap.GetHeight(station.transform.position), station.transform.position.z);
				foreach (var monument in _monumentsList.Values)
                {
					if ((monument.Category == MonumentCategory.SafeZone || monument.Category == MonumentCategory.RadTown) && Vector3.Distance(monument.boxCollider.ClosestPointOnBounds(groundPos), groundPos) <= stationDistance)
						parentWatcher = monument;
                }
				
				if (parentWatcher != null)
                {
					CreateWatcher(monumentKey, MonumentCategory.TunnelStation, station.transform, prefab, $"_{parentWatcher.ID}");
					if (_monumentsList.TryGetValue($"{monumentKey}_{parentWatcher.ID}", out var stationWatcher))
						stationWatcher.TextKey = $"{parentWatcher.ID}_station";
					continue;
				}
				
				tunnel++;
				CreateWatcher(monumentKey, MonumentCategory.TunnelStation, station.transform, prefab, $"_{tunnel}", $"#{tunnel}");
            }
			yield return null;
			
			SaveBoundsConfig(_defaultBoundsPath, _defaultBoundsValues);
			SaveBoundsConfig(_monumentsBoundsPath, _monumentsBounds);
			SaveBoundsConfig(_customMonumentsBoundsPath, _customMonumentsBounds);
			ClearBoundsConfig();
			for (int i = 0; i < _config.LanguageKeys.Count; i++)
                HandleLanguageFile(_enLang, _config.LanguageKeys[i]);
            HandleLanguageFile(_ruLang, "ru");
			_enLang.Clear();
			_ruLang.Clear();
			
			_harmony = new Harmony(IdForHarmony);
			_harmony.Patch(AccessTools.Method(typeof(ConVar.DeepSea), "enterdeepsea"), postfix: new HarmonyMethod(typeof(MonumentsWatcher), nameof(ModEnterDeepSea), new Type[] { typeof(ConsoleSystem.Arg) }));
			_harmony.Patch(AccessTools.Method(typeof(ConVar.DeepSea), "leavedeepsea"), postfix: new HarmonyMethod(typeof(MonumentsWatcher), nameof(ModLeaveDeepSea), new Type[] { typeof(ConsoleSystem.Arg) }));
			
			for (int i = 0; i < _defaultHooks.Length; i++)
				Subscribe(_defaultHooks[i]);
			yield return null;
			
			_isReady = true;
			Interface.CallHook(Hooks_OnLoaded, Version);
		}
		
		private void CreateCargoWatcher(CargoShip cargoShip)
        {
			if (!IsTrackedCategory(MonumentCategory.RadTownWater) || !cargoShip.IsValid())
				return;
			ulong cargoID = cargoShip.net.ID.Value;
			string monumentID = $"CargoShip_{cargoID}";
			var bounds = _spawnableBoundsValues[Str_CargoShip];
			
			var watcher = new GameObject().gameObject.AddComponent<MonumentWatcher>();
			watcher.InitializeProperties(monumentID, MonumentCategory.RadTownWater, cargoShip.name, Str_CargoShip, $"#{cargoID}", parnetID: cargoID);
			watcher.InitializeBounds(bounds.CenterOffset, bounds.Size, Quaternion.identity, cargoShip.transform);
			_monumentsList[monumentID] = watcher;
			Interface.CallHook(Hooks_OnCargoWatcherCreated, monumentID, watcher.CategoryString, cargoShip);
		}
		
		private void CreateSpawnableWatcher(BaseEntity entity, MonumentCategory category)
        {
			if (!IsTrackedCategory(category) || !entity.IsValid())
				return;
			ulong netID = entity.net.ID.Value;
			string prefab = entity.name.ToLower(), monumentKey = ClearMonumentName(prefab), monumentID = $"{monumentKey}_{netID}";
			var bounds = _spawnableBoundsValues[monumentKey];
			
			var watcher = new GameObject().gameObject.AddComponent<MonumentWatcher>();
            watcher.InitializeProperties(monumentID, category, prefab, monumentKey, $"#{netID}", parnetID: netID);
            watcher.InitializeBounds(entity.transform.position + (entity.transform.rotation * bounds.CenterOffset), bounds.Size, entity.transform.rotation);
			_monumentsList[monumentID] = watcher;
			Interface.CallHook(Hooks_OnSpawnableWatcherCreated, monumentID, watcher.CategoryString, entity);
		}
		
		private void CreateCustomWatcher(string monumentID, Transform transform, string prefab, string displayName)
        {
			_enLang[$"custom_{monumentID}"] = displayName;
            _ruLang[$"custom_{monumentID}"] = displayName;
            if (!_customMonumentsBounds.TryGetValue(monumentID, out var bounds) || bounds == null)
            {
				Collider collider;
				var colArray = new Collider[5];
				var rotation = transform.rotation.eulerAngles;
				for (var i = 0; i < Physics.OverlapSphereNonAlloc(transform.position, 1f, colArray, Rust.Layers.Mask.Prevent_Building, QueryTriggerInteraction.Ignore); i++)
                {
					collider = colArray[i];
                    if (collider != null && collider.name.Contains("prevent_building", StringComparison.OrdinalIgnoreCase))
                    {
                        rotation = collider.transform.rotation.eulerAngles;
                        break;
                    }
                }
                _customMonumentsBounds[monumentID] = bounds = new CustomMonumentBounds(_defaultBoundsValues["monument_marker"], transform.position, rotation, MonumentCategory.Custom);
            }
			
			if (bounds.MonumentCategory != MonumentCategory.Custom && !IsTrackedCategory(bounds.MonumentCategory))
				return;
			var watcher = new GameObject().AddComponent<MonumentWatcher>();
			watcher.InitializeProperties(monumentID, bounds.MonumentCategory, prefab, $"custom_{monumentID}", isCustom: true);
			watcher.InitializeBounds(bounds.Center + (Quaternion.Euler(bounds.Rotation) * bounds.CenterOffset), bounds.Size, Quaternion.Euler(bounds.Rotation));
			_monumentsList[monumentID] = watcher;
		}
		
		private void CreateWatcher(string monumentKey, MonumentCategory category, Transform transform, string prefab, string idSuffix = "", string suffix = "")
        {
			if (!IsTrackedCategory(category))
				return;
			string monumentID = $"{monumentKey}{(!string.IsNullOrWhiteSpace(idSuffix) ? idSuffix : string.Empty)}";
			if (!_monumentsBounds.TryGetValue(monumentID, out var bounds) || bounds == null)
				_monumentsBounds[monumentID] = bounds = new MonumentBounds(_defaultBoundsValues[monumentKey], transform.position, transform.rotation.eulerAngles);
			
			var watcher = new GameObject().AddComponent<MonumentWatcher>();
			watcher.InitializeProperties(monumentID, category, prefab, monumentKey, suffix);
			watcher.InitializeBounds(bounds.Center + (Quaternion.Euler(bounds.Rotation) * bounds.CenterOffset), bounds.Size, Quaternion.Euler(bounds.Rotation));
			_monumentsList[monumentID] = watcher;
		}
		
		private void ClearWatchers()
        {
            for (int i = 0; i < _defaultHooks.Length; i++)
                Unsubscribe(_defaultHooks[i]);
			foreach (var watcher in _monumentsList.Values.ToArray())
				UnityEngine.Object.Destroy(watcher.gameObject);
			_monumentsList.Clear();
			_playersInMonuments.Clear();
            _npcsInMonuments.Clear();
			_animalsInMonuments.Clear();
			_entitiesInMonuments.Clear();
		}
		
		private void TryDeleteWatcher(ulong parentID)
        {
			foreach (var watcher in _monumentsList.Values)
            {
                if (watcher.ParentID == parentID)
                {
                    UnityEngine.Object.Destroy(watcher.gameObject);
                    return;
                }
            }
		}
		
		private bool TryGetPlayerWatcher(BasePlayer player, out MonumentWatcher result, bool closest = true)
        {
			result = null;
			if (_playersInMonuments.TryGetValue(player.userID, out var watchers) && watchers.Count > 0)
				result = watchers[^1];
			else if (closest)
			{
				float minDistance = float.MaxValue, distance;
				var pos = player.transform.position;
                foreach (var watcher in _monumentsList.Values)
                {
					distance = (pos - watcher.transform.position).sqrMagnitude;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        result = watcher;
                    }
                }
			}
            return result != null;
        }
		
		private bool IsTrackedCategory(MonumentCategory category) => _config.TrackedCategories.Count < 1 || _config.TrackedCategories.Contains(category);
		
		private void ShowBounds(MonumentWatcher watcher, BasePlayer player, float duration = 20f)
        {
			if (watcher == null || player == null)
				return;
			bool isAdmin = player.IsAdmin;
            try
            {
				if (!isAdmin)
					UpdateFlag(player, BasePlayer.PlayerFlags.IsAdmin, true);
				
				//TEXT
                player.SendConsoleCommand("ddraw.text", duration, Color.magenta, watcher.transform.position, watcher.ID);

                //CENTER
                player.SendConsoleCommand("ddraw.sphere", duration, Color.green, watcher.transform.position, 1f);

                //CORNERS
                Vector3 size = watcher.boxCollider.size * 0.5f,
                    center = watcher.boxCollider.center,
					corner, startPos, endPos;
				var transform = watcher.boxCollider.transform;
                Vector3[] corners = new Vector3[8],
                    offsets = new Vector3[8]
                    {
                        new (-size.x, -size.y, -size.z),
                        new (size.x, -size.y, -size.z),
                        new (size.x, -size.y, size.z),
                        new (-size.x, -size.y, size.z),
                        new (-size.x, size.y, -size.z),
                        new (size.x, size.y, -size.z),
                        new (size.x, size.y, size.z),
                        new (-size.x, size.y, size.z)
                    };
                for (int i = 0; i < offsets.Length; i++)
                {
					corner = corners[i] = transform.TransformPoint(center + offsets[i]);
                    player.SendConsoleCommand("ddraw.sphere", duration, Color.red, corner, 1f);
                }

                //LINES
                for (int i = 0; i < corners.Length; i++)
                {
					startPos = corners[i];
					for (int j = 0; j < corners.Length; j++)
					{
						endPos = corners[j];
						if (endPos != startPos)
							player.SendConsoleCommand("ddraw.line", duration, Color.red, startPos, endPos);
					}
                }
                player.AddPingAtLocation(BasePlayer.PingType.GoTo, watcher.transform.position + watcher.transform.up * 2.5f, duration, new NetworkableId());
            }
            catch {}
            finally
            {
				if (!isAdmin)
					UpdateFlag(player, BasePlayer.PlayerFlags.IsAdmin, false);
			}
		}
		
		private void UpdateFlag(BasePlayer player, BasePlayer.PlayerFlags flag, bool addFlag)
		{
			if (player == null)
				return;
			player.SetPlayerFlag(flag, addFlag);
			player.SendNetworkUpdateImmediate();
		}
		
		private static string ClearMonumentName(string prefabName) => prefabName.Split('/')[^1].Replace(".prefab", string.Empty);
		
		private static void SendMessage(IPlayer player, string message, bool isWarning = true)
        {
            if (_config.GameTips_Enabled && !player.IsServer)
                player.Command("gametip.showtoast", (int)(isWarning ? GameTip.Styles.Error : GameTip.Styles.Blue_Long), message, string.Empty);
            else
                player.Reply(message);
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
		
		private void HandleLanguageFile(Dictionary<string, string> langFile, string langKey)
        {
            var existFile = lang.GetMessages(langKey, this);
            if (existFile == null || existFile.Count < 1)
            {
                if (!Directory.Exists(Path.Combine(Interface.Oxide.LangDirectory, langKey)))
                    Directory.CreateDirectory(Path.Combine(Interface.Oxide.LangDirectory, langKey));
                File.WriteAllText(Path.Combine(Interface.Oxide.LangDirectory, $"{langKey}{Path.DirectorySeparatorChar}{Name}.json"), JsonConvert.SerializeObject(langFile, Formatting.Indented));
            }
            lang.RegisterMessages(langFile, this, langKey);
        }
        #endregion

        #region ~API~
		private object IsReady() => _isReady ? true : null;
		
		private string[] GetAllMonuments() => _isReady ? _monumentsList.Keys.ToArray() : null;
		private object GetAllMonumentsNoAlloc(List<string> list)
		{
			if (_isReady && _monumentsList.Count > 0)
			{
				list.AddRange(_monumentsList.Keys);
				return true;
			}
			return null;
		}
		
		private Dictionary<string, string> GetAllMonumentsWithCategories() => _isReady ? _monumentsList.ToDictionary(watcher => watcher.Key, watcher => watcher.Value.CategoryString) : null;
		private object GetAllMonumentsWithCategoriesNoAlloc(Dictionary<string, string> values)
		{
			if (!_isReady || _monumentsList.Count < 1)
				return null;
			foreach (var watcher in _monumentsList.Values)
				values[watcher.ID] = watcher.CategoryString;
			return true;
		}
		
		private string[] GetMonumentsByCategory(string category) => _isReady ? _monumentsList.Where(watcher => watcher.Value.CategoryString.Equals(category, StringComparison.OrdinalIgnoreCase)).Select(watcher => watcher.Key).ToArray() : null;
		private object GetMonumentsByCategoryNoAlloc(List<string> list, string category)
		{
			if (!_isReady)
				return null;
			foreach (var watcher in _monumentsList.Values)
			{
				if (watcher.CategoryString.Equals(category, StringComparison.OrdinalIgnoreCase))
					list.Add(watcher.ID);
			}
			return list.Count > 0 ? true : null;
		}
		
		private string GetMonumentCategory(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.CategoryString : string.Empty;
		
		private string GetMonumentDisplayName(string monumentID, object obj, bool showSuffix = true) => GetMonumentDisplayName(monumentID, $"{obj}", showSuffix);
		private string GetMonumentDisplayName(string monumentID, ulong userID, bool showSuffix = true) => GetMonumentDisplayName(monumentID, $"{userID}", showSuffix);
		private string GetMonumentDisplayName(string monumentID, BasePlayer player, bool showSuffix = true) => GetMonumentDisplayName(monumentID, player.UserIDString, showSuffix);
		private string GetMonumentDisplayName(string monumentID, IPlayer player, bool showSuffix = true) => GetMonumentDisplayName(monumentID, player.Id, showSuffix);
		private string GetMonumentDisplayName(string monumentID, string userID = "", bool showSuffix = true) => GetMonumentDisplayNameByLang(monumentID, lang.GetLanguage(userID), showSuffix);
		
		private string GetMonumentDisplayNameByLang(string monumentID, string langKey = "en", bool showSuffix = true)
        {
			if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher))
                return $"{lang.GetMessageByLanguage(watcher.TextKey, this, langKey)}{(showSuffix && !string.IsNullOrWhiteSpace(watcher.Suffix) ? $" {watcher.Suffix}" : string.Empty)}";
            return string.Empty;
        }
		
		private Vector3 GetMonumentPosition(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.transform.position : Vector3.zero;
		
		private string GetMonumentByPos(Vector3 pos)
		{
			if (!_isReady)
				return null;
			foreach (var watcher in _monumentsList.Values)
            {
                if (watcher.IsInBounds(pos))
                    return watcher.ID;
            }
			return string.Empty;
		}
		
		private object GetMonumentsByPos(Vector3 pos)
        {
			if (!_isReady)
				return null;
			var result = new List<string>();
            foreach (var watcher in _monumentsList.Values)
            {
				if (watcher.IsInBounds(pos))
                    result.Add(watcher.ID);
            }
			if (result.Count > 0)
				return result.ToArray();
			return null;
		}
		private object GetMonumentsByPosNoAlloc(List<string> list, Vector3 pos)
        {
            if (!_isReady)
                return null;
			foreach (var watcher in _monumentsList.Values)
            {
                if (watcher.IsInBounds(pos))
					list.Add(watcher.ID);
			}
			return list.Count > 0 ? true : null;
		}
		
		private string GetClosestMonument(Vector3 pos)
        {
			if (!_isReady)
				return null;
			MonumentWatcher result = null;
			float minDistance = float.MaxValue, distance;
			foreach (var watcher in _monumentsList.Values)
            {
				distance = (pos - watcher.transform.position).sqrMagnitude;
                if (distance < minDistance)
                {
                    minDistance = distance;
                    result = watcher;
                }
            }
			return result?.ID ?? string.Empty;
		}
		
		private bool IsPosInMonument(string monumentID, Vector3 pos)
		{
			if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher))
				return watcher.IsInBounds(pos);
			return false;
		}
		
		private void ShowBounds(string monumentID, BasePlayer player, float duration = 20f)
		{
			if (_isReady && player != null && _monumentsList.TryGetValue(monumentID, out var watcher))
				ShowBounds(watcher, player, duration);
		}
        #endregion

        #region ~API - Players~
		private object GetMonumentPlayers(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.PlayersList.ToArray() : null;
		private object GetMonumentPlayersNoAlloc(List<BasePlayer> list, string monumentID)
		{
			if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && watcher.PlayersList.Count > 0)
			{
				list.AddRange(watcher.PlayersList);
				return true;
			}
			return null;
		}
		
		private string GetPlayerMonument(object obj) => GetPlayerMonument($"{obj}");
		private string GetPlayerMonument(string userIDStr) => GetPlayerMonument(ulong.TryParse(userIDStr, out var userID) ? userID : 0uL);
		private string GetPlayerMonument(BasePlayer player) => GetPlayerMonument(player.userID);
		private string GetPlayerMonument(ulong userID) => _isReady && _playersInMonuments.TryGetValue(userID, out var watchers) && watchers.Count > 0 ? watchers[^1].ID : string.Empty;
		
		private object GetPlayerMonuments(object obj) => GetPlayerMonuments($"{obj}");
		private object GetPlayerMonuments(string userIDStr) => GetPlayerMonuments(ulong.TryParse(userIDStr, out var userID) ? userID : 0uL);
		private object GetPlayerMonuments(BasePlayer player) => GetPlayerMonuments(player.userID);
		private object GetPlayerMonuments(ulong userID)
        {
            if (_isReady && _playersInMonuments.TryGetValue(userID, out var watchers) && watchers.Count > 0)
            {
				string[] result = new string[watchers.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = watchers[i].ID;
                return result;
            }
            return null;
        }
		
		private object GetPlayerMonumentsNoAlloc(List<string> list, object obj) => GetPlayerMonumentsNoAlloc(list, $"{obj}");
		private object GetPlayerMonumentsNoAlloc(List<string> list, string userIDStr) => GetPlayerMonumentsNoAlloc(list, ulong.TryParse(userIDStr, out var userID) ? userID : 0uL);
		private object GetPlayerMonumentsNoAlloc(List<string> list, BasePlayer player) => GetPlayerMonumentsNoAlloc(list, player.userID);
		private object GetPlayerMonumentsNoAlloc(List<string> list, ulong userID)
        {
            if (_isReady && _playersInMonuments.TryGetValue(userID, out var watchers) && watchers.Count > 0)
            {
                string monumentID;
                for (int i = 0; i < watchers.Count; i++)
                {
                    monumentID = watchers[i].ID;
                    if (!list.Contains(monumentID))
                        list.Add(monumentID);
                }
                return true;
            }
            return null;
        }
		
		private string GetPlayerClosestMonument(object obj) => GetPlayerClosestMonument($"{obj}");
        private string GetPlayerClosestMonument(string userIDStr) => GetPlayerClosestMonument(ulong.TryParse(userIDStr, out var userID) ? userID : 0uL);
        private string GetPlayerClosestMonument(ulong userID) => GetPlayerClosestMonument(BasePlayer.FindAwakeOrSleepingByID(userID));
        private string GetPlayerClosestMonument(BasePlayer player)
        {
			if (!_isReady || player == null)
				return null;
			if (_playersInMonuments.TryGetValue(player.userID, out var watchers) && watchers.Count > 0)
                return watchers[^1].ID;
			MonumentWatcher result = null;
            float minDistance = float.MaxValue;
            var pos = player.transform.position;
            foreach (var watcher in _monumentsList.Values)
            {
                float distance = (pos - watcher.transform.position).sqrMagnitude;
                if (distance < minDistance)
                {
                    minDistance = distance;
                    result = watcher;
                }
            }
			return result?.ID ?? string.Empty;
		}
		
		private bool IsPlayerInMonument(string monumentID, object obj) => IsPlayerInMonument(monumentID, $"{obj}");
		private bool IsPlayerInMonument(string monumentID, string userIDStr) => IsPlayerInMonument(monumentID, ulong.TryParse(userIDStr, out var userID) ? userID : 0uL);
		private bool IsPlayerInMonument(string monumentID, BasePlayer player) => IsPlayerInMonument(monumentID, player.userID);
		private bool IsPlayerInMonument(string monumentID, ulong userID) => _isReady && _playersInMonuments.TryGetValue(userID, out var watchers) && _monumentsList.TryGetValue(monumentID, out var watcher) ? watchers.Contains(watcher) : false;
        #endregion

        #region ~API - NPCs~
		private object GetMonumentNpcs(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.NpcsList.ToArray() : null;
		private object GetMonumentNpcsNoAlloc(List<BaseNPC2> list, string monumentID)
        {
            if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && watcher.NpcsList.Count > 0)
            {
                list.AddRange(watcher.NpcsList);
                return true;
            }
            return null;
        }
		//Old npc
		private object GetMonumentNpcs_Old(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.NpcsList_Old.ToArray() : null;
		private object GetMonumentNpcsNoAlloc(List<BasePlayer> list, string monumentID)
        {
            if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && watcher.NpcsList_Old.Count > 0)
            {
                list.AddRange(watcher.NpcsList_Old);
                return true;
            }
            return null;
        }
		private object GetMonumentNpcsNoAlloc(List<BaseEntity> list, string monumentID)
        {
			object result = null;
			if (!_isReady || !_monumentsList.TryGetValue(monumentID, out var watcher))
				return result;
			if (watcher.NpcsList.Count > 0)
			{
				list.AddRange(watcher.NpcsList);
				result = true;
			}
			if (watcher.NpcsList_Old.Count > 0)
			{
				list.AddRange(watcher.NpcsList_Old);
				result = true;
			}
			return result;
		}
		//----------

        private string GetNpcMonument(BaseNPC2 npcPlayer) => npcPlayer.IsValid() ? GetNpcMonument(npcPlayer.net.ID.Value) : string.Empty;
		private string GetNpcMonument(NetworkableId netID) => GetNpcMonument(netID.Value);
		private string GetNpcMonument(ulong netID)
		{
            if (_isReady && _npcsInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
                return watchers[^1].ID;
            return string.Empty;
        }
		//Old npc
        private string GetNpcMonument(BasePlayer npcPlayer) => npcPlayer.IsValid() ? GetNpcMonument(npcPlayer.net.ID.Value) : string.Empty;
        //----------
		
		private object GetNpcMonuments(BaseNPC2 npcPlayer) => npcPlayer.IsValid() ? GetNpcMonuments(npcPlayer.net.ID.Value) : null;
		private object GetNpcMonuments(NetworkableId netID) => GetNpcMonuments(netID.Value);
		private object GetNpcMonuments(ulong netID)
		{
            if (_isReady && _npcsInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
            {
				string[] result = new string[watchers.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = watchers[i].ID;
                return result;
            }
            return null;
        }
        //Old npc
		private object GetNpcMonuments(BasePlayer npcPlayer) => npcPlayer.IsValid() ? GetNpcMonuments(npcPlayer.net.ID.Value) : null;
        //----------
		
		private object GetNpcMonumentsNoAlloc(List<string> list, BaseNPC2 npcPlayer) => npcPlayer.IsValid() ? GetNpcMonumentsNoAlloc(list, npcPlayer.net.ID.Value) : null;
		private object GetNpcMonumentsNoAlloc(List<string> list, NetworkableId netID) => GetNpcMonumentsNoAlloc(list, netID.Value);
		private object GetNpcMonumentsNoAlloc(List<string> list, ulong netID)
		{
            if (_isReady && _npcsInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
            {
				string monumentID;
				for (int i = 0; i < watchers.Count; i++)
				{
					monumentID = watchers[i].ID;
					if (!list.Contains(monumentID))
						list.Add(monumentID);
				}
				return true;
			}
            return null;
        }
        //Old npc
        private object GetNpcMonumentsNoAlloc(List<string> list, BasePlayer npcPlayer) => npcPlayer.IsValid() ? GetNpcMonumentsNoAlloc(list, npcPlayer.net.ID.Value) : null;
        //----------
		
		private bool IsNpcInMonument(string monumentID, BaseNPC2 npcPlayer) => npcPlayer.IsValid() && IsNpcInMonument(monumentID, npcPlayer.net.ID.Value);
		private bool IsNpcInMonument(string monumentID, NetworkableId netID) => IsNpcInMonument(monumentID, netID.Value);
		private bool IsNpcInMonument(string monumentID, ulong netID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && _npcsInMonuments.TryGetValue(netID, out var watchers) ? watchers.Contains(watcher) : false;
		//Old npc
        private bool IsNpcInMonument(string monumentID, BasePlayer npcPlayer) => npcPlayer.IsValid() && IsNpcInMonument(monumentID, npcPlayer.net.ID.Value);
		private bool IsNpcInMonument(string monumentID, BaseEntity npcPlayer) => npcPlayer.IsValid() && IsNpcInMonument(monumentID, npcPlayer.net.ID.Value);
		//----------
        #endregion

        #region ~API - Animals~
        private object GetMonumentAnimals(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.AnimalsList.ToArray() : null;
        private object GetMonumentAnimalsNoAlloc(List<BaseNPC2> list, string monumentID)
        {
            if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && watcher.AnimalsList.Count > 0)
            {
                list.AddRange(watcher.AnimalsList);
                return true;
            }
            return null;
        }
        //Old npc
        private object GetMonumentAnimals_Old(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.AnimalsList_Old.ToArray() : null;
        private object GetMonumentAnimalsNoAlloc(List<BaseAnimalNPC> list, string monumentID)
        {
            if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && watcher.AnimalsList_Old.Count > 0)
            {
                list.AddRange(watcher.AnimalsList_Old);
                return true;
            }
            return null;
        }
        private object GetMonumentAnimalsNoAlloc(List<BaseEntity> list, string monumentID)
        {
            object result = null;
            if (!_isReady || !_monumentsList.TryGetValue(monumentID, out var watcher))
                return result;
            if (watcher.AnimalsList.Count > 0)
            {
                list.AddRange(watcher.AnimalsList);
                result = true;
            }
            if (watcher.AnimalsList_Old.Count > 0)
            {
                list.AddRange(watcher.AnimalsList_Old);
                result = true;
            }
            return result;
        }
        //----------

        private string GetAnimalMonument(BaseNPC2 animal) => animal.IsValid() ? GetAnimalMonument(animal.net.ID.Value) : string.Empty;
		private string GetAnimalMonument(NetworkableId netID) => GetAnimalMonument(netID.Value);
		private string GetAnimalMonument(ulong netID)
		{
            if (_isReady && _animalsInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
                return watchers[^1].ID;
            return string.Empty;
        }
        //Old npc
        private string GetAnimalMonument(BaseAnimalNPC animal) => animal.IsValid() ? GetAnimalMonument(animal.net.ID.Value) : string.Empty;
        //----------

        private object GetAnimalMonuments(BaseNPC2 animal) => animal.IsValid() ? GetAnimalMonuments(animal.net.ID.Value) : null;
		private object GetAnimalMonuments(NetworkableId netID) => GetAnimalMonuments(netID.Value);
		private object GetAnimalMonuments(ulong netID)
		{
            if (_isReady && _animalsInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
            {
                string[] result = new string[watchers.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = watchers[i].ID;
                return result;
            }
            return null;
        }
        //Old npc
        private object GetAnimalMonuments(BaseAnimalNPC animal) => animal.IsValid() ? GetAnimalMonuments(animal.net.ID.Value) : null;
        //----------

        private object GetAnimalMonumentsNoAlloc(List<string> list, BaseNPC2 animal) => animal.IsValid() ? GetAnimalMonumentsNoAlloc(list, animal.net.ID.Value) : null;
		private object GetAnimalMonumentsNoAlloc(List<string> list, NetworkableId netID) => GetAnimalMonumentsNoAlloc(list, netID.Value);
		private object GetAnimalMonumentsNoAlloc(List<string> list, ulong netID)
		{
            if (_isReady && _animalsInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
            {
                string monumentID;
                for (int i = 0; i < watchers.Count; i++)
                {
                    monumentID = watchers[i].ID;
                    if (!list.Contains(monumentID))
                        list.Add(monumentID);
                }
                return true;
            }
            return null;
        }
        //Old npc
        private object GetAnimalMonumentsNoAlloc(List<string> list, BaseAnimalNPC animal) => animal.IsValid() ? GetAnimalMonumentsNoAlloc(list, animal.net.ID.Value) : null;
        //----------
		
		private bool IsAnimalInMonument(string monumentID, BaseNPC2 animal) => animal.IsValid() ? IsAnimalInMonument(monumentID, animal.net.ID.Value) : false;
		private bool IsAnimalInMonument(string monumentID, NetworkableId netID) => IsAnimalInMonument(monumentID, netID.Value);
		private bool IsAnimalInMonument(string monumentID, ulong netID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && _animalsInMonuments.TryGetValue(netID, out var watchers) ? watchers.Contains(watcher) : false;
		//Old npc
		private bool IsAnimalInMonument(string monumentID, BaseAnimalNPC animal) => animal.IsValid() ? IsAnimalInMonument(monumentID, animal.net.ID.Value) : false;
		private bool IsAnimalInMonument(string monumentID, BaseEntity animal) => animal.IsValid() ? IsAnimalInMonument(monumentID, animal.net.ID.Value) : false;
		//----------
		#endregion

        #region ~API - Entities~
        private object GetMonumentEntities(string monumentID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) ? watcher.EntitiesList.ToArray() : null;
		private object GetMonumentEntitiesNoAlloc(List<BaseEntity> list, string monumentID)
        {
            if (_isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && watcher.EntitiesList.Count > 0)
            {
                list.AddRange(watcher.EntitiesList);
                return true;
            }
            return null;
        }
		
		private string GetEntityMonument(BaseEntity entity) => entity.IsValid() ? GetEntityMonument(entity.net.ID.Value) : string.Empty;
		private string GetEntityMonument(NetworkableId netID) => GetEntityMonument(netID.Value);
		private string GetEntityMonument(ulong netID)
		{
            if (_isReady && _entitiesInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
                return watchers[^1].ID;
            return string.Empty;
        }
		
		private object GetEntityMonuments(BaseEntity entity) => entity.IsValid() ? GetEntityMonuments(entity.net.ID.Value) : null;
		private object GetEntityMonuments(NetworkableId netID) => GetEntityMonuments(netID.Value);
		private object GetEntityMonuments(ulong netID)
		{
            if (_isReady && _entitiesInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
            {
				string[] result = new string[watchers.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = watchers[i].ID;
                return result;
            }
            return null;
        }
		
		private object GetEntityMonumentsNoAlloc(List<string> list, BaseEntity entity) => entity.IsValid() ? GetEntityMonumentsNoAlloc(list, entity.net.ID.Value) : null;
		private object GetEntityMonumentsNoAlloc(List<string> list, NetworkableId netID) => GetEntityMonumentsNoAlloc(list, netID.Value);
		private object GetEntityMonumentsNoAlloc(List<string> list, ulong netID)
		{
            if (_isReady && _entitiesInMonuments.TryGetValue(netID, out var watchers) && watchers.Count > 0)
            {
                string monumentID;
                for (int i = 0; i < watchers.Count; i++)
                {
                    monumentID = watchers[i].ID;
                    if (!list.Contains(monumentID))
                        list.Add(monumentID);
                }
                return true;
            }
            return null;
        }
		
		private bool IsEntityInMonument(string monumentID, BaseEntity entity) => entity.IsValid() ? IsEntityInMonument(monumentID, entity.net.ID.Value) : false;
		private bool IsEntityInMonument(string monumentID, NetworkableId netID) => IsEntityInMonument(monumentID, netID.Value);
		private bool IsEntityInMonument(string monumentID, ulong netID) => _isReady && _monumentsList.TryGetValue(monumentID, out var watcher) && _entitiesInMonuments.TryGetValue(netID, out var watchers) ? watchers.Contains(watcher) : false;
		#endregion
		
		#region ~Oxide Hooks~
        void OnEntitySpawned(CargoShip cargoShip) => CreateCargoWatcher(cargoShip);
		void OnEntitySpawned(DeepSeaFloatingCity floatingCity) => CreateSpawnableWatcher(floatingCity, MonumentCategory.SafeZone);
		void OnEntitySpawned(DeepSeaIsland deepSeaIsland) => CreateSpawnableWatcher(deepSeaIsland, MonumentCategory.DeepSeaIsland);
		void OnEntitySpawned(Prefabs.Misc.GhostShip ghostShip) => CreateSpawnableWatcher(ghostShip, MonumentCategory.RadTownWater);
		void OnEntitySpawned(NPCDwelling npcDwelling)
		{
			if (npcDwelling.IsValid() && npcDwelling.PrefabName.Contains("tropical_island_dwelling_ruins_", StringComparison.OrdinalIgnoreCase))
				CreateSpawnableWatcher(npcDwelling, MonumentCategory.Ruins);
		}
		
		void OnEntityKill(DeepSeaFloatingCity floatingCity)
        {
			if (IsTrackedCategory(MonumentCategory.SafeZone) && floatingCity.IsValid())
				TryDeleteWatcher(floatingCity.net.ID.Value);
		}
		
		void OnEntityKill(DeepSeaIsland deepSeaIsland)
        {
			if (IsTrackedCategory(MonumentCategory.DeepSeaIsland) && deepSeaIsland.IsValid())
                TryDeleteWatcher(deepSeaIsland.net.ID.Value);
        }
		
		void OnEntityKill(Prefabs.Misc.GhostShip ghostShip)
        {
            if (IsTrackedCategory(MonumentCategory.RadTownWater) && ghostShip.IsValid())
                TryDeleteWatcher(ghostShip.net.ID.Value);
        }
		
		void OnEntityKill(NPCDwelling npcDwelling)
        {
			if (IsTrackedCategory(MonumentCategory.Ruins) && npcDwelling.IsValid())
                TryDeleteWatcher(npcDwelling.net.ID.Value);
		}
		
		void OnEntityDeath(BasePlayer player)
		{
			if (player.userID.IsSteamId())
            {
				if (_playersInMonuments.TryGetValue(player.userID, out var watchers))
				{
					for (int i = watchers.Count - 1; i >= 0; i--)
						watchers[i]?.OnPlayerExit(player, Str_Death);
				}
			}
			else if (_npcsInMonuments.TryGetValue(player.net.ID.Value, out var watchers))
			{
				//Old npc
				for (int i = watchers.Count - 1; i >= 0; i--)
                    watchers[i]?.OnNpcExit_Old(player, Str_Death);
			}
		}
		
		void OnEntityDeath(BaseNPC2 baseNpc)
		{
			ulong netID = baseNpc.net.ID.Value;
			if (baseNpc.IsAnimal)
			{
				if (_animalsInMonuments.TryGetValue(netID, out var watchers))
                {
                    for (int i = watchers.Count - 1; i >= 0; i--)
                        watchers[i]?.OnAnimalExit(baseNpc, Str_Death);
                }
			}
			else if (baseNpc.IsNpc)
            {
                if (_npcsInMonuments.TryGetValue(netID, out var watchers))
                {
                    for (int i = watchers.Count - 1; i >= 0; i--)
                        watchers[i]?.OnNpcExit(baseNpc, Str_Death);
                }
            }
			else if (_entitiesInMonuments.TryGetValue(netID, out var watchers))
			{
                for (int i = watchers.Count - 1; i >= 0; i--)
                    watchers[i]?.OnEntityExit(baseNpc, Str_Death);
            }
		}
		//Old npc
		void OnEntityDeath(BaseAnimalNPC animal)
		{
			if (!_animalsInMonuments.TryGetValue(animal.net.ID.Value, out var watchers))
				return;
			for (int i = watchers.Count - 1; i >= 0; i--)
				watchers[i]?.OnAnimalExit_Old(animal, Str_Death);
		}
		
		void OnEntityKill(BaseEntity entity)
		{
			if (!entity.IsValid() || !_entitiesInMonuments.TryGetValue(entity.net.ID.Value, out var watchers))
				return;
			for (int i = watchers.Count - 1; i >= 0; i--)
				watchers[i]?.OnEntityExit(entity, Str_Death);
		}
		
		void OnPlayerTeleported(BasePlayer player, Vector3 oldPos, Vector3 newPos) => HandleTeleport(player);
		
		void Init()
        {
			for (int i = 0; i < _defaultHooks.Length; i++)
				Unsubscribe(_defaultHooks[i]);
			Instance = this;
			permission.RegisterPermission(PERMISSION_ADMIN, this);
			AddCovalenceCommand(_config.Command, nameof(MonumentsWatcher_Command));
			_playersInMonuments = new Hash<ulong, List<MonumentWatcher>>();
			_npcsInMonuments = new Hash<ulong, List<MonumentWatcher>>();
			_animalsInMonuments = new Hash<ulong, List<MonumentWatcher>>();
			_entitiesInMonuments = new Hash<ulong, List<MonumentWatcher>>();
			string path = $"{Name}{Path.DirectorySeparatorChar}";
			_defaultBoundsPath = $"{path}DefaultBounds";
			_monumentsBoundsPath = $"{path}MonumentsBounds";
            _customMonumentsBoundsPath = $"{path}CustomMonumentsBounds";
		}
		
		void OnServerInitialized(bool initial)
        {
			if (string.IsNullOrWhiteSpace(_config.WipeID) || _config.WipeID != SaveRestore.WipeId)
			{
				_config.WipeID = SaveRestore.WipeId;
				if (_config.RecreateOnWipe)
				{
					_monumentsBounds = new Hash<string, MonumentBounds>();
					SaveBoundsConfig(_monumentsBoundsPath, _monumentsBounds);
					PrintWarning("Wipe detected! Monument boundaries(excluding custom ones) have been reset to ensure proper creation of new boundaries.");
				}
				SaveConfig();
            }
			ServerMgr.Instance.StartCoroutine(InitMonuments());
		}
		
		void Unload()
        {
			_harmony?.UnpatchAll(IdForHarmony);
			ClearWatchers();
			_isReady = false;
			_playersInMonuments = null;
			_npcsInMonuments = null;
			_animalsInMonuments = null;
			_entitiesInMonuments = null;
			Instance = null;
			_config = null;
		}
		#endregion

        #region ~Commands~
		private static readonly string[] _cmdKeys = { "show", "list", "rotate", "recreate" };
		private void MonumentsWatcher_Command(IPlayer player, string command, string[] args)
		{
			if (!player.IsAdmin && !permission.UserHasPermission(player.Id, PERMISSION_ADMIN))
				return;
			int index = args != null && args.Length > 0 ? Array.FindIndex(_cmdKeys, key => key.Equals(args[0], StringComparison.OrdinalIgnoreCase)) : -1;
			if (index < 0)
				goto notValid;
			
			var bPlayer = player.Object as BasePlayer;
			if (index == 0)
			{
				//show
				if (bPlayer == null)
				{
					player.Reply("This command is only available to players!");
					return;
				}
				
				var monumentsList = new List<MonumentWatcher>();
				if (args.Length < 2 || args[1].Equals("closest", StringComparison.OrdinalIgnoreCase))
				{
					if (TryGetPlayerWatcher(bPlayer, out var watcher))
						monumentsList.Add(watcher);
                }
				else if (args[1].Equals("help", StringComparison.OrdinalIgnoreCase))
					goto notValid;
				else if (_monumentsList.TryGetValue(args[1], out var watcher))
                    monumentsList.Add(watcher);
                else
                {
                    foreach (var watcher2 in _monumentsList.Values)
                    {
                        if (watcher2.TextKey.Contains(args[1], StringComparison.OrdinalIgnoreCase) && !monumentsList.Contains(watcher2))
                            monumentsList.Add(watcher2);
                    }
                }
				
				int total = monumentsList.Count;
				if (total > 0)
                {
					if (args.Length < 3 || !float.TryParse(args[2], out var displayTime))
						displayTime = 30f;
					if (total == 1)
					{
						var watcher = monumentsList[0];
						ShowBounds(watcher, bPlayer, displayTime);
						SendMessage(player, string.Format(lang.GetMessage("CmdMainShow", this, player.Id), GetMonumentDisplayName(watcher.ID, player.Id), watcher.transform.position), false);
					}
					else
                    {
						for (int i = 0; i < total; i++)
							ShowBounds(monumentsList[i], bPlayer, displayTime);
                        SendMessage(player, string.Format(lang.GetMessage("CmdMainShowList", this, player.Id), args[1], total), false);
                    }
				}
                else
					SendMessage(player, lang.GetMessage("CmdMainShowNotFound", this, player.Id));
				monumentsList.Clear();
            }
			else if (index == 1)
            {
				//list
				player.Reply(string.Format(lang.GetMessage("CmdMainList", this, player.Id), string.Join(", ", _monumentsList.Values.Select(watcher => watcher.ID).ToArray())));
            }
			else if (index == 2)
            {
                //rotate
                if (args.Length > 1 && args[1].Equals("help", StringComparison.OrdinalIgnoreCase))
					goto notValid;
				
				float yRot = 0f;
				MonumentWatcher watcher = null;
				if ((args.Length < 3 || !float.TryParse(args[2], out yRot)) && bPlayer != null)
					yRot = bPlayer.viewAngles.y;
				if ((args.Length < 2 || !_monumentsList.TryGetValue(args[1], out watcher)) && bPlayer != null)
					TryGetPlayerWatcher(bPlayer, out watcher, args.Length > 1 && args[1].Equals("closest", StringComparison.OrdinalIgnoreCase));
				
				if (watcher == null || watcher.ID.Contains(Str_CargoShip))
					SendMessage(player, lang.GetMessage("CmdMainRotateNotFound", this, player.Id));
				else
				{
					var newRot = new Vector3(0f, yRot, 0f);
					
					LoadBoundsConfig(_monumentsBoundsPath, out _monumentsBounds);
					if (_monumentsBounds.TryGetValue(watcher.ID, out var bounds))
					{
						bounds.Rotation = newRot;
						SaveBoundsConfig(_monumentsBoundsPath, _monumentsBounds);
					}
					else
                    {
						LoadBoundsConfig(_customMonumentsBoundsPath, out _customMonumentsBounds);
						if (_customMonumentsBounds.TryGetValue(watcher.ID, out var customBounds))
						{
							customBounds.Rotation = newRot;
                            SaveBoundsConfig(_customMonumentsBoundsPath, _customMonumentsBounds);
						}
					}
					ClearBoundsConfig();
					
					watcher.transform.rotation = Quaternion.Euler(newRot);
					ShowBounds(watcher, bPlayer, 30f);
					SendMessage(player, string.Format(lang.GetMessage("CmdMainRotated", this, player.Id), GetMonumentDisplayName(watcher.ID, player.Id), yRot), false);
				}
			}
			else if (index == 3)
            {
				//recreate
				string[] array = null;
                if (args.Length > 1)
                {
                    if (args[1].Equals("custom", StringComparison.OrdinalIgnoreCase))
                        array = new string[] { $"{Name}{Path.DirectorySeparatorChar}CustomMonumentsBounds" };
                    else if (args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                        array = new string[] { $"{Name}{Path.DirectorySeparatorChar}MonumentsBounds", $"{Name}{Path.DirectorySeparatorChar}CustomMonumentsBounds" };
                }
				array ??= new string[] { $"{Name}{Path.DirectorySeparatorChar}MonumentsBounds" };
                for (int i = 0; i < array.Length; i++)
                    Interface.Oxide.DataFileSystem.DeleteDataFile(array[i]);
				ServerMgr.Instance.StartCoroutine(InitMonuments());
				SendMessage(player, lang.GetMessage("CmdMainRecreated", this, player.Id), false);
            }
			else
				goto notValid;
			return;
		
		notValid:
			player.Reply(lang.GetMessage("CmdMain", this, player.Id));
		}
		#endregion

        #region ~Bounds Config~
		public class BoundsValues
        {
            public Vector3 CenterOffset { get; set; }
            public Vector3 Size { get; set; }

            public BoundsValues(Vector3 offset, Vector3 size)
            {
                CenterOffset = offset;
                Size = size;
            }
        }
		
		public class MonumentBounds
        {
			public Vector3 CenterOffset { get; set; }
			public Vector3 Size { get; set; }
			public Vector3 Center { get; set; }
			public Vector3 Rotation { get; set; }
			
			public MonumentBounds() {}
			public MonumentBounds(BoundsValues bounds, Vector3 center, Vector3 rotation)
            {
				Center = center;
				Rotation = rotation;
				CenterOffset = bounds.CenterOffset;
				Size = bounds.Size;
			}
		}
		
		public class CustomMonumentBounds : MonumentBounds
		{ 
			public MonumentCategory MonumentCategory { get; set; }
			
			public CustomMonumentBounds() {}
			public CustomMonumentBounds(BoundsValues bounds, Vector3 center, Vector3 rotation, MonumentCategory monumentCategory)
				: base(bounds, center, rotation)
			{
				MonumentCategory = monumentCategory;
			}
		}
		
		private Hash<string, MonumentBounds> _monumentsBounds;
		private Hash<string, CustomMonumentBounds> _customMonumentsBounds;
		private Hash<string, BoundsValues> _defaultBoundsValues, _spawnableBoundsValues;
		private string _defaultBoundsPath = string.Empty, _monumentsBoundsPath = string.Empty, _customMonumentsBoundsPath = string.Empty;
		
		private void LoadDefaultBounds()
        {
			var initialBounds = new Hash<string, BoundsValues>()
			{
				{ Str_CargoShip, new BoundsValues(new Vector3(0f, 17f, 10f), new Vector3(26f, 60f, 147f)) },
				{ "airfield_1", new BoundsValues(new Vector3(0f, 20f, -25f), new Vector3(400f, 70f, 250f)) },
				{ "apartments_complex_1", new BoundsValues(new Vector3(-30f, 35f, 20f), new Vector3(220f, 80f, 200f)) },
				{ "arctic_research_base_a", new BoundsValues(new Vector3(-2f, 20f, -2f), new Vector3(180f, 70f, 180f)) },
				{ "bandit_town", new BoundsValues(new Vector3(2.5f, 15f, 0f), new Vector3(220f, 70f, 180f)) },
				{ "compound", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(180f, 60f, 200f)) },
				{ "deepsea_floatingcity1", new BoundsValues(new Vector3(-5f, 40f, -30f), new Vector3(250f, 150f, 280f)) },
				{ "deepsea_floatingcity2", new BoundsValues(new Vector3(-10f, 40f, 15f), new Vector3(220f, 150f, 280f)) },
				{ "deepsea_floatingcity3", new BoundsValues(new Vector3(22.5f, 40f, -40f), new Vector3(300f, 150f, 220f)) },
				{ "deepsea_floatingcity4", new BoundsValues(new Vector3(-2f, 40f, 7f), new Vector3(250f, 150f, 250f)) },
				{ "deepsea_island_tropical1", new BoundsValues(new Vector3(-5f, 15f, 55f), new Vector3(300f, 120f, 250f)) },
				{ "deepsea_island_tropical2", new BoundsValues(new Vector3(35f, 15f, -80f), new Vector3(330f, 120f, 300f)) },
				{ "deepsea_island_tropical3", new BoundsValues(new Vector3(-15f, 15f, 30f), new Vector3(200f, 120f, 160f)) },
				{ "deepsea_island_tropical4", new BoundsValues(new Vector3(-25f, 15f, 10f), new Vector3(260f, 120f, 300f)) },
				{ "desert_military_base_a", new BoundsValues(new Vector3(0f, 15f, 3f), new Vector3(100f, 80f, 100f)) },
				{ "desert_military_base_b", new BoundsValues(new Vector3(0f, 15f, 3f), new Vector3(160f, 80f, 160f)) },
				{ "desert_military_base_c", new BoundsValues(new Vector3(0f, 15f, 18f), new Vector3(160f, 80f, 180f)) },
				{ "desert_military_base_d", new BoundsValues(new Vector3(-5f, 15f, 0f), new Vector3(180f, 80f, 160f)) },
				{ "excavator_1", new BoundsValues(new Vector3(20f, 40f, -23f), new Vector3(250f, 140f, 250f)) },
				{ "ferry_terminal_1", new BoundsValues(new Vector3(4f, 15f, 25f), new Vector3(240f, 70f, 240f)) },
				{ "fishing_village_a", new BoundsValues(new Vector3(-1.5f, 10f, -10f), new Vector3(120f, 60f, 120f)) },
				{ "fishing_village_b", new BoundsValues(new Vector3(-3f, 10f, -4f), new Vector3(80f, 60f, 120f)) },
				{ "fishing_village_c", new BoundsValues(new Vector3(-0.5f, 10f, -4f), new Vector3(80f, 60f, 120f)) },
				{ "gas_station_1", new BoundsValues(new Vector3(0f, 15f, 14f), new Vector3(100f, 40f, 100f)) },
				{ "ghostship", new BoundsValues(new Vector3(0f, 17.5f, 0f), new Vector3(120f, 60f, 80f)) },
				{ "ghostship_b", new BoundsValues(new Vector3(0f, 17.5f, 0f), new Vector3(120f, 60f, 80f)) },
				{ "ghostship_c", new BoundsValues(new Vector3(0f, 17.5f, 0f), new Vector3(120f, 60f, 80f)) },
				{ "ghostship_d", new BoundsValues(new Vector3(0f, 17.5f, 0f), new Vector3(120f, 60f, 80f)) },
				{ "harbor_1", new BoundsValues(new Vector3(5f, 25f, 42f), new Vector3(280f, 70f, 280f)) },
				{ "harbor_1_old", new BoundsValues(new Vector3(0f, 20f, 15f), new Vector3(235f, 60f, 210f)) },
				{ "harbor_2", new BoundsValues(new Vector3(25f, 25f, 5f), new Vector3(260f, 70f, 300f)) },
				{ "harbor_2_old", new BoundsValues(new Vector3(10f, 20f, 15f), new Vector3(220f, 60f, 250f)) },
				{ "jungle_ziggurat_a", new BoundsValues(new Vector3(0f, 10f, 0f), new Vector3(100f, 50f, 100f)) },
				{ "junkyard_1", new BoundsValues(new Vector3(0f, 25f, 10f), new Vector3(200f, 60f, 200f)) },
				{ "launch_site_1", new BoundsValues(new Vector3(0f, 35f, -25f), new Vector3(600f, 140f, 340f)) },
				{ "lighthouse", new BoundsValues(new Vector3(8f, 35f, 2f), new Vector3(100f, 100f, 100f)) },
				{ "military_tunnel_1", new BoundsValues(new Vector3(0f, 75f, 0f), new Vector3(300f, 100f, 300f)) },
				{ "mining_quarry_a", new BoundsValues(new Vector3(2f, 15f, -5f), new Vector3(80f, 40f, 80f)) },
				{ "mining_quarry_b", new BoundsValues(new Vector3(-5f, 15f, -7f), new Vector3(100f, 40f, 90f)) },
				{ "mining_quarry_c", new BoundsValues(new Vector3(-6f, 15f, 0f), new Vector3(80f, 40f, 80f)) },
				{ "nuclear_missile_silo", new BoundsValues(new Vector3(0f, 66f, 0f), new Vector3(180f, 120f, 180f)) },
				{ "oilrig_1", new BoundsValues(new Vector3(3f, 25f, 12f), new Vector3(100f, 150f, 130f)) },
				{ "oilrig_2", new BoundsValues(new Vector3(18f, 10f, -2f), new Vector3(100f, 120f, 100f)) },
				{ "powerplant_1", new BoundsValues(new Vector3(-15f, 35f, -5f), new Vector3(260f, 100f, 300f)) },
				{ "radtown_1", new BoundsValues(new Vector3(2.75f, 10f, 0.5f), new Vector3(160f, 40f, 120f)) },
				{ "radtown_small_3", new BoundsValues(new Vector3(0f, 25f, -13f), new Vector3(200f, 70f, 200f)) },
				{ "satellite_dish", new BoundsValues(new Vector3(0f, 30f, 5f), new Vector3(220f, 100f, 180f)) },
				{ "sphere_tank", new BoundsValues(new Vector3(0f, 40f, 5f), new Vector3(150f, 110f, 150f)) },
				{ "stables_a", new BoundsValues(new Vector3(0f, 20f, 5f), new Vector3(80f, 40f, 80f)) },
				{ "stables_b", new BoundsValues(new Vector3(5f, 20f, 7.5f), new Vector3(100f, 40f, 100f)) },
				{ "station-sn-0", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(105f, 18f, 215f)) },
				{ "station-sn-1", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(105f, 18f, 215f)) },
				{ "station-sn-2", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(105f, 18f, 215f)) },
				{ "station-sn-3", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(105f, 18f, 215f)) },
				{ "station-we-0", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(215f, 18f, 105f)) },
				{ "station-we-1", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(215f, 18f, 105f)) },
				{ "station-we-2", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(215f, 18f, 105f)) },
				{ "station-we-3", new BoundsValues(new Vector3(0f, 8f, 0f), new Vector3(215f, 18f, 105f)) },
				{ "supermarket_1", new BoundsValues(new Vector3(2f, 10f, 0f), new Vector3(80f, 30f, 80f)) },
				{ "swamp_a", new BoundsValues(new Vector3(-11f, 15f, 3f), new Vector3(160f, 40f, 190f)) },
				{ "swamp_b", new BoundsValues(new Vector3(-1f, 15f, -3f), new Vector3(125f, 40f, 125f)) },
				{ "swamp_c", new BoundsValues(new Vector3(6f, 15f, -1f), new Vector3(130f, 40f, 130f)) },
                { "ue_jungle_swamp_a", new BoundsValues(new Vector3(0f, 10f, 0f), new Vector3(80f, 40f, 80f)) },
				{ "trainyard_1", new BoundsValues(new Vector3(10f, 30f, -10f), new Vector3(280f, 100f, 250f)) },
				{ "underwater_lab_a", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(110f, 25f, 110f)) },
				{ "underwater_lab_b", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(110f, 25f, 110f)) },
				{ "underwater_lab_c", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(110f, 25f, 110f)) },
				{ "underwater_lab_d", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(110f, 25f, 110f)) },
				{ "warehouse", new BoundsValues(new Vector3(0f, 10f, -7f), new Vector3(60f, 30f, 60f)) },
				{ "water_treatment_plant_1", new BoundsValues(new Vector3(0f, 35f, -30f), new Vector3(280f, 100f, 320f)) },
				{ "entrance_bunker_a", new BoundsValues(new Vector3(-4f, 1f, -1f), new Vector3(20f, 30f, 20f)) },
				{ "entrance_bunker_b", new BoundsValues(new Vector3(-8f, 1f, 0f), new Vector3(30f, 30f, 20f)) },
				{ "entrance_bunker_c", new BoundsValues(new Vector3(-4f, 1f, -1f), new Vector3(20f, 30f, 20f)) },
				{ "entrance_bunker_d", new BoundsValues(new Vector3(-4f, 1f, -1f), new Vector3(20f, 30f, 20f)) },
				{ "cave_small_easy", new BoundsValues(new Vector3(6f, -28f, 17f), new Vector3(45f, 46f, 66f)) },
				{ "cave_small_medium", new BoundsValues(new Vector3(20f, -30f, -18f), new Vector3(80f, 50f, 65f)) },
				{ "cave_small_hard", new BoundsValues(new Vector3(8f, -21f, 0f), new Vector3(45f, 35f, 80f)) },
				{ "cave_medium_easy", new BoundsValues(new Vector3(8f, -21f, 0f), new Vector3(45f, 35f, 80f)) },
				{ "cave_medium_medium", new BoundsValues(new Vector3(-1f, -25f, 2f), new Vector3(110f, 50f, 110f)) },
				{ "cave_medium_hard", new BoundsValues(new Vector3(8f, -21f, 0f), new Vector3(45f, 35f, 80f)) },
				{ "cave_large_medium", new BoundsValues(new Vector3(8f, -21f, 0f), new Vector3(45f, 35f, 80f)) },
				{ "cave_large_hard", new BoundsValues(new Vector3(8f, -21f, 0f), new Vector3(45f, 35f, 80f)) },
				{ "cave_large_sewers_hard", new BoundsValues(new Vector3(50f, 5f, -7f), new Vector3(170f, 40f, 165f)) },
				{ "ice_lake_1", new BoundsValues(new Vector3(-2f, 15f, 0f), new Vector3(140f, 35f, 160f)) },
				{ "ice_lake_2", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(150f, 35f, 150f)) },
				{ "ice_lake_3", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(180f, 35f, 240f)) },
				{ "ice_lake_4", new BoundsValues(new Vector3(0f, 15f, 0f), new Vector3(85f, 35f, 85f)) },
				{ "power_sub_small_1", new BoundsValues(new Vector3(0f, 5f, 0f), new Vector3(15f, 10f, 15f)) },
				{ "power_sub_small_2", new BoundsValues(new Vector3(0f, 5f, 0f), new Vector3(15f, 10f, 15f)) },
				{ "power_sub_big_1", new BoundsValues(new Vector3(0f, 5f, 1f), new Vector3(20f, 10f, 22f)) },
				{ "power_sub_big_2", new BoundsValues(new Vector3(-1f, 5f, 1f), new Vector3(23f, 10f, 22f)) },
				{ "jungle_ruins_a", new BoundsValues(new Vector3(0f, 10f, 0.5f), new Vector3(30f, 30f, 50f)) },
				{ "jungle_ruins_b", new BoundsValues(new Vector3(2.5f, 10f, 0f), new Vector3(40f, 30f, 40f)) },
				{ "jungle_ruins_c", new BoundsValues(new Vector3(0f, 10f, -0.5f), new Vector3(40f, 30f, 40f)) },
				{ "jungle_ruins_d", new BoundsValues(new Vector3(-1.5f, 10f, 5f), new Vector3(40f, 30f, 40f)) },
				{ "jungle_ruins_e", new BoundsValues(new Vector3(-1.5f, 10f, 1f), new Vector3(40f, 30f, 40f)) },
				{ "tropical_island_dwelling_ruins_b", new BoundsValues(new Vector3(2f, 15f, -0.25f), new Vector3(50f, 30f, 60f)) },
				{ "tropical_island_dwelling_ruins_c", new BoundsValues(new Vector3(0f, 15f, -0.25f), new Vector3(50f, 30f, 60f)) },
				{ "tropical_island_dwelling_ruins_d", new BoundsValues(new Vector3(4f, 15f, 1f), new Vector3(50f, 30f, 50f)) },
				{ "water_well_a", new BoundsValues(new Vector3(-2f, 7f, 0f), new Vector3(25f, 20f, 25f)) },
				{ "water_well_b", new BoundsValues(new Vector3(-1f, 7f, 0f), new Vector3(25f, 20f, 25f)) },
				{ "water_well_c", new BoundsValues(new Vector3(0f, 10f, 1f), new Vector3(32f, 25f, 32f)) },
				{ "water_well_d", new BoundsValues(new Vector3(0f, 10f, 1f), new Vector3(30f, 25f, 30f)) },
				{ "water_well_e", new BoundsValues(new Vector3(-1f, 7f, 0f), new Vector3(25f, 20f, 25f)) },
				{ "monument_marker", new BoundsValues(new Vector3(0f, 0f, 0f), new Vector3(100f, 100f, 100f)) }
			};
			
			LoadBoundsConfig(_defaultBoundsPath, out _defaultBoundsValues);
			_defaultBoundsValues ??= new Hash<string, BoundsValues>();
			foreach (var kvp in initialBounds)
			{
				if (!_defaultBoundsValues.TryGetValue(kvp.Key, out var bounds) || bounds == null)
					_defaultBoundsValues[kvp.Key] = kvp.Value;
			}
			_spawnableBoundsValues = new Hash<string, BoundsValues>(StringComparer.OrdinalIgnoreCase)
			{
				{ Str_CargoShip, _defaultBoundsValues[Str_CargoShip] },
				{ "deepsea_floatingcity1", _defaultBoundsValues["deepsea_floatingcity1"] },
				{ "deepsea_floatingcity2", _defaultBoundsValues["deepsea_floatingcity2"] },
				{ "deepsea_floatingcity3", _defaultBoundsValues["deepsea_floatingcity3"] },
				{ "deepsea_floatingcity4", _defaultBoundsValues["deepsea_floatingcity4"] },
				{ "deepsea_island_tropical1", _defaultBoundsValues["deepsea_island_tropical1"] },
				{ "deepsea_island_tropical2", _defaultBoundsValues["deepsea_island_tropical2"] },
				{ "deepsea_island_tropical3", _defaultBoundsValues["deepsea_island_tropical3"] },
				{ "deepsea_island_tropical4", _defaultBoundsValues["deepsea_island_tropical4"] },
				{ "ghostship", _defaultBoundsValues["ghostship"] },
				{ "ghostship_b", _defaultBoundsValues["ghostship_b"] },
				{ "ghostship_c", _defaultBoundsValues["ghostship_c"] },
				{ "ghostship_d", _defaultBoundsValues["ghostship_d"] },
				{ "tropical_island_dwelling_ruins_b", _defaultBoundsValues["tropical_island_dwelling_ruins_b"] },
                { "tropical_island_dwelling_ruins_c", _defaultBoundsValues["tropical_island_dwelling_ruins_c"] },
                { "tropical_island_dwelling_ruins_d", _defaultBoundsValues["tropical_island_dwelling_ruins_d"] }
			};
		}
		
		private void ClearBoundsConfig()
        {
            _defaultBoundsValues.Clear();
            _monumentsBounds.Clear();
            _customMonumentsBounds.Clear();
        }

        private static void LoadBoundsConfig<T>(string filePath, out T result) where T : new()
		{
            try { result = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); result = new T(); }
            if (result == null)
            {
                result = new T();
                SaveBoundsConfig(filePath, result);
            }
		}
        private static void SaveBoundsConfig<T>(string filePath, T obj) => Interface.Oxide.DataFileSystem.WriteObject(filePath, obj);
        #endregion

        #region ~Monument Watcher~
		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public enum MonumentCategory
        {
            SafeZone,
            RadTown,
            RadTownWater,
            RadTownSmall,
            TunnelStation,
			MiningQuarry,
            BunkerEntrance,
            Cave,
            Swamp,
            IceLake,
            PowerSubstation,
			Ruins,
			WaterWell,
			DeepSeaIsland,
			Custom
        }
		
		public static void ModEnterDeepSea(ConsoleSystem.Arg arg)
        {
			if (Instance == null)
				return;
			var player = ArgEx.Player(arg);
			if (player != null)
				Instance.HandleTeleport(player);
		}
		
		public static void ModLeaveDeepSea(ConsoleSystem.Arg arg)
        {
            if (Instance == null)
                return;
            var player = ArgEx.Player(arg);
            if (player != null)
                Instance.HandleTeleport(player);
        }
		
		private void HandleTeleport(BasePlayer player)
		{
			if (!_playersInMonuments.TryGetValue(player.userID, out var watchers))
				return;
			MonumentWatcher watcher;
			var pos = player.transform.position;
			for (int i = watchers.Count - 1; i >= 0; i--)
            {
                watcher = watchers[i];
                if (watcher != null && !watcher.IsInBounds(pos))
					watcher.OnPlayerExit(player, Str_Leave);
			}
		}
		
		public class MonumentWatcher : MonoBehaviour
		{
			public string ID { get; private set; }
			public ulong ParentID { get; private set; }
			public MonumentCategory Category { get; private set; }
			public string CategoryString { get; private set; }
			
			public string Prefab { get; private set; }
            public string TextKey { get; set; }
            public string Suffix { get; private set; }
			public bool IsMoveable { get; private set; }
			public bool IsCustom { get; private set; }
			public Vector3 Size { get; private set; }
			
			public HashSet<BasePlayer> PlayersList = Pool.Get<HashSet<BasePlayer>>();
			public HashSet<BaseNPC2> NpcsList = Pool.Get<HashSet<BaseNPC2>>();
			public HashSet<BaseNPC2> AnimalsList = Pool.Get<HashSet<BaseNPC2>>();
			public HashSet<BaseEntity> EntitiesList = Pool.Get<HashSet<BaseEntity>>();
			private Rigidbody rigidbody;
			public BoxCollider boxCollider;
			public Bounds colliderBounds;
			
			//Old npc
			public HashSet<BasePlayer> NpcsList_Old = Pool.Get<HashSet<BasePlayer>>();
			public HashSet<BaseAnimalNPC> AnimalsList_Old = Pool.Get<HashSet<BaseAnimalNPC>>();
			//----------
			
			private void Awake()
			{
				gameObject.layer = (int)Layer.Reserved1;
				gameObject.name = "MonumentWatcher";
				enabled = false;
			}

            public void InitializeProperties(string monumentID, MonumentCategory category, string prefab, string textKey, string suffix = "", bool isCustom = false, ulong parnetID = 0uL)
            {
                ID = monumentID;
				ParentID = parnetID;
				Category = category;
				CategoryString = Category.ToString();
				Prefab = prefab;
                TextKey = textKey;
				Suffix = suffix;
				IsCustom = isCustom;
			}
			
			public void InitializeBounds(Vector3 center, Vector3 size, Quaternion rotation, Transform parent = null)
            {
				if (parent is not null)
                {
					IsMoveable = true;
					transform.parent = parent;
                    transform.localPosition = center;
                    transform.localRotation = rotation;
                }
                else
                {
					IsMoveable = false;
					transform.position = center;
                    transform.rotation = rotation;
                }
				
				if (boxCollider is not null)
                    DestroyImmediate(boxCollider);
                if (rigidbody is not null)
                    DestroyImmediate(rigidbody);

                rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
				
				boxCollider = gameObject.GetComponent<BoxCollider>();
                if (boxCollider is null)
                {
                    boxCollider = gameObject.AddComponent<BoxCollider>();
                    boxCollider.isTrigger = true;
                }
                boxCollider.size = Size = size;
                colliderBounds = boxCollider.bounds;
            }
			
			private void OnTriggerEnter(Collider collider)
            {
				if (Instance == null)
                {
					if (this != null && gameObject != null)
						Interface.Oxide.NextTick(() => UnityEngine.Object.Destroy(gameObject));
					return;
                }
				var entity = collider?.gameObject?.ToBaseEntity();
				if (!entity.IsValid())
					return;
				
				ulong netID = entity.net.ID.Value;
				List<MonumentWatcher> watchers;
				string oldMonumentID = string.Empty;
				if (entity is BasePlayer player)
				{
					bool isNpc = !player.userID.IsSteamId();
					if (isNpc)
                    {
						//Old npc
						if (!NpcsList_Old.Add(player))
							return;
						if (!_npcsInMonuments.TryGetValue(netID, out watchers) || watchers == null)
							_npcsInMonuments[netID] = watchers = new List<MonumentWatcher>();
					}
					else
					{
						if (!PlayersList.Add(player))
							return;
						if (!_playersInMonuments.TryGetValue(player.userID, out watchers) || watchers == null)
							_playersInMonuments[player.userID] = watchers = new List<MonumentWatcher>();
                    }
					if (HandleWatcherList())
						Interface.CallHook(isNpc ? Hooks_OnNpcEnteredMonument : Hooks_OnPlayerEnteredMonument, ID, player, CategoryString, oldMonumentID);
                }
				else if (entity is BaseNPC2 baseNpc)
				{
					if (baseNpc.IsAnimal)
					{
						if (!AnimalsList.Add(baseNpc))
                            return;
                        if (!_animalsInMonuments.TryGetValue(netID, out watchers) || watchers == null)
                            _animalsInMonuments[netID] = watchers = new List<MonumentWatcher>();
					}
					else if (baseNpc.IsNpc)
                    {
                        if (!NpcsList.Add(baseNpc))
                            return;
                        if (!_npcsInMonuments.TryGetValue(netID, out watchers) || watchers == null)
                            _npcsInMonuments[netID] = watchers = new List<MonumentWatcher>();
                    }
					else
					{
						if (!EntitiesList.Add(entity))
                            return;
                        if (!_entitiesInMonuments.TryGetValue(netID, out watchers) || watchers == null)
                            _entitiesInMonuments[netID] = watchers = new List<MonumentWatcher>();
                        if (HandleWatcherList())
                            Interface.CallHook(Hooks_OnEntityEnteredMonument, ID, entity, CategoryString, oldMonumentID);
						return;
					}
					if (HandleWatcherList())
                        Interface.CallHook(baseNpc.IsAnimal ? Hooks_OnAnimalEnteredMonument : Hooks_OnNpcEnteredMonument, ID, baseNpc, CategoryString, oldMonumentID);
				}
				else if (entity is BaseAnimalNPC animal)
                {
                    //Old npc
					if (!AnimalsList_Old.Add(animal))
						return;
					if (!_animalsInMonuments.TryGetValue(netID, out watchers) || watchers == null)
						_animalsInMonuments[netID] = watchers = new List<MonumentWatcher>();
					if (HandleWatcherList())
						Interface.CallHook(Hooks_OnAnimalEnteredMonument, ID, animal, CategoryString, oldMonumentID);
				}
				else
				{
					if (!EntitiesList.Add(entity))
						return;
					if (!_entitiesInMonuments.TryGetValue(netID, out watchers) || watchers == null)
						_entitiesInMonuments[netID] = watchers = new List<MonumentWatcher>();
					if (HandleWatcherList())
						Interface.CallHook(Hooks_OnEntityEnteredMonument, ID, entity, CategoryString, oldMonumentID);
				}
				
				bool HandleWatcherList()
                {
					bool callHook = true;
					if (watchers.Count > 0)
                        oldMonumentID = watchers[^1].ID;
                    watchers.Add(this);
					if (this.IsMoveable)
						return callHook;
					MonumentWatcher watcher;
                    int lastIndex = watchers.Count - 1;
                    for (int i = lastIndex; i >= 0; i--)
                    {
                        watcher = watchers[i];
						if (!watcher.IsMoveable)
							continue;
						watchers.RemoveAt(i);
                        watchers.Insert(lastIndex, watcher);
                        lastIndex--;
						callHook = false;
                    }
					return callHook;
				}
            }

            private void OnTriggerExit(Collider collider)
            {
				if (Instance == null)
                {
					if (this != null && gameObject != null)
                        Interface.Oxide.NextTick(() => UnityEngine.Object.Destroy(gameObject));
					return;
                }
				var entity = collider?.gameObject?.ToBaseEntity();
				if (!entity.IsValid())
					return;
				
				if (entity is BasePlayer player)
				{
					if (player.userID.IsSteamId())
						OnPlayerExit(player, Str_Leave);
					else
                        OnNpcExit_Old(player, Str_Leave);//Old npc
				}
				else if (entity is BaseNPC2 baseNpc)
                {
					if (baseNpc.IsAnimal)
						OnAnimalExit(baseNpc, Str_Leave);
					else if (baseNpc.IsNpc)
						OnNpcExit(baseNpc, Str_Leave);
					else
						OnEntityExit(entity, Str_Leave);
				}
				else if (entity is BaseAnimalNPC animal)
                    OnAnimalExit_Old(animal, Str_Leave);//Old npc
				else
					OnEntityExit(entity, Str_Leave);
			}
			
			public void OnPlayerExit(BasePlayer player, string reason, bool remove = true)
            {
				string newMonumentID = string.Empty;
				if (_playersInMonuments.TryGetValue(player.userID, out var watchers))
                {
					watchers.Remove(this);
					if (watchers.Count < 1)
						_playersInMonuments.Remove(player.userID);
					else if (reason != Str_Death)
						newMonumentID = watchers[^1].ID;
				}
				Interface.CallHook(Hooks_OnPlayerExitedMonument, ID, player, CategoryString, reason, newMonumentID);
				if (remove)
					PlayersList.Remove(player);
			}
			
			public void OnNpcExit(BaseNPC2 npcPlayer, string reason, bool remove = true)
            {
				ulong netID = npcPlayer.net.ID.Value;
				string newMonumentID = string.Empty;
				if (_npcsInMonuments.TryGetValue(netID, out var watchers))
				{
					watchers.Remove(this);
					if (watchers.Count < 1)
						_npcsInMonuments.Remove(netID);
					else if (reason != Str_Death)
						newMonumentID = watchers[^1].ID;
				}
				Interface.CallHook(Hooks_OnNpcExitedMonument, ID, npcPlayer, CategoryString, reason, newMonumentID);
                if (remove)
					NpcsList.Remove(npcPlayer);
            }
			//Old npc
			public void OnNpcExit_Old(BasePlayer npcPlayer, string reason, bool remove = true)
            {
				ulong netID = npcPlayer.net.ID.Value;
				string newMonumentID = string.Empty;
                if (_npcsInMonuments.TryGetValue(netID, out var watchers))
                {
                    watchers.Remove(this);
                    if (watchers.Count < 1)
                        _npcsInMonuments.Remove(netID);
                    else if (reason != Str_Death)
                        newMonumentID = watchers[^1].ID;
                }
                Interface.CallHook(Hooks_OnNpcExitedMonument, ID, npcPlayer, CategoryString, reason, newMonumentID);
                if (remove)
                    NpcsList_Old.Remove(npcPlayer);
            }
			
			public void OnAnimalExit(BaseNPC2 animal, string reason, bool remove = true)
            {
				ulong netID = animal.net.ID.Value;
				string newMonumentID = string.Empty;
                if (_animalsInMonuments.TryGetValue(netID, out var watchers))
                {
                    watchers.Remove(this);
                    if (watchers.Count < 1)
                        _animalsInMonuments.Remove(netID);
                    else if (reason != Str_Death)
                        newMonumentID = watchers[^1].ID;
                }
                Interface.CallHook(Hooks_OnAnimalExitedMonument, ID, animal, CategoryString, reason, newMonumentID);
                if (remove)
					AnimalsList.Remove(animal);
			}
			//Old npc
			public void OnAnimalExit_Old(BaseAnimalNPC animal, string reason, bool remove = true)
            {
				ulong netID = animal.net.ID.Value;
				string newMonumentID = string.Empty;
                if (_animalsInMonuments.TryGetValue(netID, out var watchers))
                {
                    watchers.Remove(this);
                    if (watchers.Count < 1)
                        _animalsInMonuments.Remove(netID);
                    else if (reason != Str_Death)
                        newMonumentID = watchers[^1].ID;
                }
                Interface.CallHook(Hooks_OnAnimalExitedMonument, ID, animal, CategoryString, reason, newMonumentID);
                if (remove)
                    AnimalsList_Old.Remove(animal);
            }
			
			public void OnEntityExit(BaseEntity entity, string reason, bool remove = true)
            {
				ulong netID = entity.net.ID.Value;
				string newMonumentID = string.Empty;
				if (_entitiesInMonuments.TryGetValue(netID, out var watchers))
                {
					watchers.Remove(this);
					if (watchers.Count < 1)
						_entitiesInMonuments.Remove(netID);
					else if (reason != Str_Death)
						newMonumentID = watchers[^1].ID;
				}
				Interface.CallHook(Hooks_OnEntityExitedMonument, ID, entity, CategoryString, reason, newMonumentID);
				if (remove)
					EntitiesList.Remove(entity);
			}
			
			public bool IsInBounds(Vector3 pos) => boxCollider.bounds.Contains(pos);
			
			private void OnDestroy()
            {
				if (Instance != null)
				{
					Instance._monumentsList.Remove(ID);
					foreach (var player in PlayersList)
                    {
                        if (player.IsValid())
                            OnPlayerExit(player, Str_MonumentDestroyed, false);
                    }
                    foreach (var npcPlayer in NpcsList)
                    {
                        if (npcPlayer.IsValid())
                            OnNpcExit(npcPlayer, Str_MonumentDestroyed, false);
                    }
					foreach (var animal in AnimalsList)
                    {
						if (animal.IsValid())
                            OnAnimalExit(animal, Str_MonumentDestroyed, false);
                    }
					foreach (var entity in EntitiesList)
                    {
                        if (entity.IsValid())
                            OnEntityExit(entity, Str_MonumentDestroyed, false);
                    }

                    //Old npc
                    foreach (var npcPlayer in NpcsList_Old)
                    {
                        if (npcPlayer.IsValid())
                            OnNpcExit_Old(npcPlayer, Str_MonumentDestroyed, false);
                    }
                    foreach (var animal in AnimalsList_Old)
                    {
                        if (animal.IsValid())
                            OnAnimalExit_Old(animal, Str_MonumentDestroyed, false);
                    }
					//----------

                    if (ParentID > 0uL)
						Interface.CallHook(ID.Contains(Str_CargoShip) ? Hooks_OnCargoWatcherDeleted : Hooks_OnSpawnableWatcherDeleted, ID);
				}
				Pool.FreeUnmanaged(ref PlayersList);
                Pool.FreeUnmanaged(ref NpcsList);
				Pool.FreeUnmanaged(ref AnimalsList);
				Pool.FreeUnmanaged(ref EntitiesList);

                //Old npc
                Pool.FreeUnmanaged(ref NpcsList_Old);
				Pool.FreeUnmanaged(ref AnimalsList_Old);
				//----------
			}
        }
		#endregion
    }
} 