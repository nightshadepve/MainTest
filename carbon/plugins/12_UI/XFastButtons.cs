using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Linq;

namespace Oxide.Plugins 
{ 
    [Info("XFastButtons", "Monster", "1.1.0")]
    class XFastButtons : RustPlugin
    {
		private const bool LanguageEnglish = true;
		
		private const string permUse = "xfastbuttons.use";
		private const string permSettings = "xfastbuttons.settings";
		
		private class Buttons
		{
			public string Text;
			public string Command;
			public string Color;
			public string Font;
			public bool Individual;
			public string Parent;
			public string Image;
			public string AnchorMinXY;
			public string AnchorMaxXY;
			public string OffsetMinXY;
			public string OffsetMaxXY;
			
			public Buttons(string text, string command, string color, string font, bool individual, string parent, string image, string anchorMinXY, string anchorMaxXY, string offsetMinXY, string offsetMaxXY)
			{
				Text = text; Command = command; Color = color; Font = font; Individual = individual; Parent = parent; Image = image; AnchorMinXY = anchorMinXY; AnchorMaxXY = anchorMaxXY; OffsetMinXY = offsetMinXY; OffsetMaxXY = offsetMaxXY;
			}
		}
		
		private Dictionary<ulong, string> _playersLootEntity = new Dictionary<ulong, string>();
		private Dictionary<ulong, string> _playersMountEntity = new Dictionary<ulong, string>();
		
		private Dictionary<ulong, Buttons> _create_edit_buttons = new Dictionary<ulong, Buttons>();
		
		private readonly List<string> _parents = new List<string> { "Overlay", "Hud", "OverlayNonScaled", "Inventory", "Crafting", "Contacts", "Clans", "TechTree", "Map" }, _fonts = new List<string> { "droidsansmono.ttf", "permanentmarker.ttf", "robotocondensed-bold.ttf", "robotocondensed-regular.ttf" };
		private readonly Dictionary<string, string> _anchors = new Dictionary<string, string> { ["0 0"] = "0 0", ["0.5 0"] = "0.5 0", ["1 0"] = "1 0", ["0 0.5"] = "0 0.5", ["0.5 0.5"] = "0.5 0.5", ["1 0.5"] = "1 0.5", ["0 1"] = "0 1", ["0.5 1"] = "0.5 1", ["1 1"] = "1 1" };
		
		private Dictionary<BasePlayer, DateTime> Cooldowns = new Dictionary<BasePlayer, DateTime>();
		
		private class Offsets
		{
			public float Amount;
			public float OffsetMinX;
			public float OffsetMinY;
			public float OffsetMaxX;
			public float OffsetMaxY;
			
			public Offsets(float amount, float offsetminx, float offsetminy, float offsetmaxx, float offsetmaxy)
			{
				Amount = amount; OffsetMinX = offsetminx; OffsetMinY = offsetminy; OffsetMaxX = offsetmaxx; OffsetMaxY = offsetmaxy;
			}
		}
		
		private Dictionary<ulong, Offsets> _offsets = new Dictionary<ulong, Offsets>();
		
		private readonly List<(string, string)> _move = new List<(string, string)>
		{
			("+", "fb_open_s_i_move upMinX"),
			("+", "fb_open_s_i_move upMinY"),
			("+", "fb_open_s_i_move upMaxX"),
			("+", "fb_open_s_i_move upMaxY"),
			("-", "fb_open_s_i_move downMinX"),
			("-", "fb_open_s_i_move downMinY"),
			("-", "fb_open_s_i_move downMaxX"),
			("-", "fb_open_s_i_move downMaxY")
		};
		
		private const string _c1 = "0.8 0.8 0.8 0.75";
		private const string _c2 = "0.5 0.5 0.5 0.5";
		private const string _c3 = "0.8 0.8 0.8 0.375";
		private const string _c4 = "0.8 0.8 0.8 1";
		private const string _c5 = "0.968 0.917 0.878 0.329";
		private const string _c6 = "0.5 0.5 0.5 0.25";
		private const string _c7 = "0.5 0.5 0.5 0.4";
		
		private const string _f1 = "robotocondensed-regular.ttf";
		
		#region Reference
		
		[PluginReference] private Plugin ImageLibrary;
		
		#endregion
		
		#region Config
		
		private FastButtonsConfig config;
		
		private class FastButtonsConfig
		{
			internal class GeneralSetting
			{
				[JsonProperty(LanguageEnglish ? "Maximum number of buttons a player can create" : "Максимальное кол-во кнопок которые может создать игрок")] public int MaxCustomButtons;
				[JsonProperty(LanguageEnglish ? "Maximum number of individual buttons a player can create" : "Максимальное кол-во отдельных кнопок которые может создать игрок")] public int MaxCustomButtonsIndividual;
				[JsonProperty(LanguageEnglish ? "List of containers - buttons will react to open/close container. [ Leave the list empty to extend this to all containers. Or set null to disable this feature. ]" : "Список контейнеров - кнопки будут реагировать на открытие/закрытие контейнера. [ Оставьте список пустым, чтобы распространить это на все контейнеры. Или установите null, чтобы отключить данную функцию. ]")] public List<string> ListLootableEntity;
				[JsonProperty(LanguageEnglish ? "List of mountable prefabs (chair, transport, etc) - buttons will react to interaction with the prefab. [ Leave the list empty to apply this to all prefabs. Or set null to disable this feature. ]" : "Список монтируемых префабов (кресло, транспорт и т.д) - кнопки будут реагировать на взаимодействие с префабом. [ Оставьте список пустым, чтобы распространить это на все префабы. Или установите null, чтобы отключить данную функцию. ]")] public List<string> ListMountableEntity;
				[JsonProperty(LanguageEnglish ? "List of server buttons - [ You can only configure parameters - Text, Command, Color, Font ]" : "Список серверных кнопок - [ Вы можете настроить только параметры - Text, Command, Color, Font ]")] public List<Buttons> ListButtons;
				[JsonProperty(LanguageEnglish ? "List of server buttons by permissions - [ You can only configure parameters - Text, Command, Color, Font ]" : "Список серверных кнопок по разрешениям - [ Вы можете настроить только параметры - Text, Command, Color, Font ]")] public Dictionary<string, List<Buttons>> ListButtonsPerms;
				[JsonProperty(LanguageEnglish ? "List of individual server buttons - [ You can configure all parameters ]" : "Список отдельных серверных кнопок - [ Вы можете настроить все параметры ]")] public List<Buttons> ListButtonsIndividual;
				[JsonProperty(LanguageEnglish ? "List of individual server buttons by permissions - [ You can configure all parameters ]" : "Список отдельных серверных кнопок по разрешениям - [ Вы можете настроить все параметры ]")] public Dictionary<string, List<Buttons>> ListButtonsIndividualPerms;
			}
			
			internal class GUISetting
			{
				[JsonProperty(LanguageEnglish ? "Color_background_1" : "Цвет_фон_1")] public string BColor1;
				[JsonProperty(LanguageEnglish ? "Color_background_2" : "Цвет_фон_2")] public string BColor2;
				[JsonProperty(LanguageEnglish ? "Close button (icon) color" : "Цвет кнопки (иконки) закрыть")] public string IconColor;
				[JsonProperty(LanguageEnglish ? "Parent layer of buttons under slots - [ Overlay, Hud, OverlayNonScaled, Inventory ]" : "Родительский слой кнопок под слотами - [ Overlay, Hud, OverlayNonScaled, Inventory ]")] public string ParentLayerUnderSlots = "Overlay";
				[JsonProperty(LanguageEnglish ? "Server image list - [ These images are not available to players ]" : "Список серверных изображений - [ Данные изображения недоступны игрокам ]")] public Dictionary<string, string> ListServerImages;
				[JsonProperty(LanguageEnglish ? "Image list - [ These images are available for players to select ]" : "Список изображений - [ Данные изображения доступны игрокам для выбора ]")] public Dictionary<string, string> ListImages;
				[JsonProperty(LanguageEnglish ? "List of button colors" : "Список цветов кнопок")] public List<string> ListColors;
			}
			
			[JsonProperty(LanguageEnglish ? "General setting" : "Общие настройки")]
			public GeneralSetting Setting = new GeneralSetting();
			[JsonProperty(LanguageEnglish ? "GUI setting" : "Настройки GUI")]
			public GUISetting GUI = new GUISetting();
			
			public static FastButtonsConfig GetNewConfiguration()
			{
				return new FastButtonsConfig
				{
					Setting = new GeneralSetting
					{
						MaxCustomButtons = 6,
						MaxCustomButtonsIndividual = 5,
						ListLootableEntity = null,
						ListMountableEntity = new List<string> { "arcadeuser", "computerstation.deployed", "twitchrivals2023_desk" },
						ListButtons = new List<Buttons>
						{
							new Buttons("KIT START", "chat.say \"/kit start\"", "1 0 0 0.5", _f1, false, null, null, null, null, null, null),
							new Buttons("KIT HOME", "chat.say \"/kit home\"", "1 1 0 0.5", _f1, false, null, null, null, null, null, null),
							new Buttons("KIT VIP", "chat.say \"/kit vip\"", "0 1 1 0.5", _f1, false, null, null, null, null, null, null),
							new Buttons("SET HOME", "chat.say /sethome", "1 0.27 0 0.5", _f1, false, null, null, null, null, null, null),
							new Buttons("HOME - 1", "chat.say \"/home 1\"", "0.2 0.8 0.2 0.5", _f1, false, null, null, null, null, null, null),
							new Buttons("HOME - 2", "chat.say \"/home 2\"", "0.2 0.8 0.2 0.5", _f1, false, null, null, null, null, null, null)
						},
						ListButtonsPerms = new Dictionary<string, List<Buttons>>
						{
							["xfastbuttons.bvip1"] = new List<Buttons>
							{
								new Buttons("VIP - 1.1", "chat.say /vip", "1 0 0.4 0.5", _f1, false, null, null, null, null, null, null),
								new Buttons("VIP - 1.2", "chat.say /vip", "1 0 0.4 0.5", _f1, false, null, null, null, null, null, null)
							},
							["xfastbuttons.bvip2"] = new List<Buttons>
							{
								new Buttons("VIP - 2.1", "chat.say /vip", "1 0.3 0.4 0.5", _f1, false, null, null, null, null, null, null),
								new Buttons("VIP - 2.2", "chat.say /vip", "1 0.3 0.4 0.5", _f1, false, null, null, null, null, null, null)
							}
						},
						ListButtonsIndividual = new List<Buttons>
						{
							new Buttons(null, "chat.say /menu", "0 0 0 0", null, true, "Overlay", "FB_SERVER_LOGO", "0 1", "0 1", "5 -55", "55 -5"),
							new Buttons(null, "chat.say \"/grade 1\"", _c7, null, true, "Overlay", "FB_IMAGE_36", "1 0", "1 0", "-229 16", "-210 35"),
							new Buttons(null, "chat.say \"/grade 2\"", _c7, null, true, "Overlay", "FB_IMAGE_37", "1 0", "1 0", "-229 37", "-210 56"),
							new Buttons(null, "chat.say \"/grade 3\"", _c7, null, true, "Overlay", "FB_IMAGE_38", "1 0", "1 0", "-229 58", "-210 77"),
							new Buttons(null, "chat.say \"/grade 4\"", _c7, null, true, "Overlay", "FB_IMAGE_39", "1 0", "1 0", "-229 79", "-210 98"),
							new Buttons(null, "chat.say \"/grade 0\"", _c7, null, true, "Overlay", "FB_CANCEL", "1 0", "1 0", "-250 16", "-231 35"),
							new Buttons(null, "chat.say /remove", _c7, null, true, "Overlay", "FB_REMOVE", "1 0", "1 0", "-250 37", "-231 56"),
							new Buttons(null, "chat.say /stats", _c7, null, true, "Overlay", "FB_IMAGE_56", "0.5 0", "0.5 0", "-232 18", "-203 47"),
							new Buttons(null, "chat.say /bskin", _c7, null, true, "Overlay", "FB_IMAGE_57", "0.5 0", "0.5 0", "-263 18", "-234 47"),
							new Buttons(null, "chat.say /kit", _c7, null, true, "Overlay", "FB_IMAGE_58", "0.5 0", "0.5 0", "-232 49", "-203 78"),
							new Buttons(null, "chat.say /skin", _c7, null, true, "Overlay", "FB_IMAGE_60", "0.5 0", "0.5 0", "-263 49", "-234 78"),
						},
						ListButtonsIndividualPerms = new Dictionary<string, List<Buttons>>
						{
							["xfastbuttons.bvip1"] = new List<Buttons>
							{
								new Buttons(null, "chat.say /vip", "1 0.65 0 0.7", null, true, "Overlay", "FB_IMAGE_05", "0 1", "0 1", "30 -25", "50 -5"),
								new Buttons(null, "chat.say /vip", "1 0.65 0 0.7", null, true, "Overlay", "FB_IMAGE_30", "0 1", "0 1", "5 -25", "25 -5")
							},
							["xfastbuttons.bvip2"] = new List<Buttons>
							{
								new Buttons(null, "chat.say /vip", "0.68 1 0.18 0.7", null, true, "Overlay", "FB_IMAGE_19", "0 1", "0 1", "60 -25", "80 -5"),
								new Buttons(null, "chat.say /vip", "0.68 1 0.18 0.7", null, true, "Overlay", "FB_IMAGE_32", "0 1", "0 1", "85 -25", "105 -5")
							}
						}
					},
					GUI = new GUISetting
					{
						BColor1 = "0.517 0.521 0.509 1",
						BColor2 = "0.217 0.221 0.209 1",
						IconColor = "1 1 1 0.75",
						ParentLayerUnderSlots = "Overlay",
						ListServerImages = new Dictionary<string, string>
						{
							["FB_SERVER_LOGO"] = "https://i.ibb.co/PNXnns7/000.png",
							["FB_CANCEL"] = "https://i.ibb.co/NrGmwvk/002.png",
							["FB_REMOVE"] = "https://i.ibb.co/5BvSq2g/003.png"
						},
						ListImages = new Dictionary<string, string>
						{
							["FB_IMAGE_01"] = "https://i.ibb.co/Xy9Bxdx/01.png", ["FB_IMAGE_02"] = "https://i.ibb.co/ZhsGdYh/02.png", ["FB_IMAGE_03"] = "https://i.ibb.co/q1w5701/03.png", ["FB_IMAGE_04"] = "https://i.ibb.co/GcjpQRS/04.png",
							["FB_IMAGE_05"] = "https://i.ibb.co/tMXzW5N/05.png", ["FB_IMAGE_06"] = "https://i.ibb.co/6r35Rc6/06.png", ["FB_IMAGE_07"] = "https://i.ibb.co/m5MqV7w/07.png", ["FB_IMAGE_08"] = "https://i.ibb.co/TbjkpMT/08.png",
							["FB_IMAGE_09"] = "https://i.ibb.co/PY2SN0N/09.png", ["FB_IMAGE_10"] = "https://i.ibb.co/YtpkCH5/10.png", ["FB_IMAGE_11"] = "https://i.ibb.co/bvW8Snj/11.png", ["FB_IMAGE_12"] = "https://i.ibb.co/RvxWyQT/12.png",
							["FB_IMAGE_13"] = "https://i.ibb.co/MhVY9Lg/13.png", ["FB_IMAGE_14"] = "https://i.ibb.co/CsP2Bcc/14.png", ["FB_IMAGE_15"] = "https://i.ibb.co/9qG5rgv/15.png", ["FB_IMAGE_16"] = "https://i.ibb.co/hX4BYpL/16.png",
							["FB_IMAGE_17"] = "https://i.ibb.co/VCJgr4q/17.png", ["FB_IMAGE_18"] = "https://i.ibb.co/Lg43PPT/18.png", ["FB_IMAGE_19"] = "https://i.ibb.co/vD1GMKk/19.png", ["FB_IMAGE_20"] = "https://i.ibb.co/Nt9tQNv/20.png",
							["FB_IMAGE_21"] = "https://i.ibb.co/Rv3gkq7/21.png", ["FB_IMAGE_22"] = "https://i.ibb.co/dKVGRJ7/22.png", ["FB_IMAGE_23"] = "https://i.ibb.co/YpwxN6p/23.png", ["FB_IMAGE_24"] = "https://i.ibb.co/syGztRF/24.png",
							["FB_IMAGE_25"] = "https://i.ibb.co/2jSc1Ch/25.png", ["FB_IMAGE_26"] = "https://i.ibb.co/p3Z08pY/26.png", ["FB_IMAGE_27"] = "https://i.ibb.co/9YrVkg0/27.png", ["FB_IMAGE_28"] = "https://i.ibb.co/HHbS3tR/28.png",
							["FB_IMAGE_29"] = "https://i.ibb.co/bzVjLH9/29.png", ["FB_IMAGE_30"] = "https://i.ibb.co/BGDgjkT/30.png", ["FB_IMAGE_31"] = "https://i.ibb.co/YddYrSK/31.png", ["FB_IMAGE_32"] = "https://i.ibb.co/nkjJnPj/32.png",
							["FB_IMAGE_33"] = "https://i.ibb.co/J2tGX3M/33.png", ["FB_IMAGE_34"] = "https://i.ibb.co/CvffCjy/34.png", ["FB_IMAGE_35"] = "https://i.ibb.co/dLV7TDq/35.png", ["FB_IMAGE_36"] = "https://i.ibb.co/gyxpKg3/36.png",
							["FB_IMAGE_37"] = "https://i.ibb.co/kyJqpNn/37.png", ["FB_IMAGE_38"] = "https://i.ibb.co/2SBpcH1/38.png", ["FB_IMAGE_39"] = "https://i.ibb.co/G3m8YZw/39.png", ["FB_IMAGE_40"] = "https://i.ibb.co/qR2q3vB/40.png",
							["FB_IMAGE_41"] = "https://i.ibb.co/NZhW591/41.png", ["FB_IMAGE_42"] = "https://i.ibb.co/rmqvKKw/42.png", ["FB_IMAGE_43"] = "https://i.ibb.co/FnThcgJ/43.png", ["FB_IMAGE_44"] = "https://i.ibb.co/xSsMg0R/44.png",
							["FB_IMAGE_45"] = "https://i.ibb.co/N2YbMPw/45.png", ["FB_IMAGE_46"] = "https://i.ibb.co/WgbDxgH/46.png", ["FB_IMAGE_47"] = "https://i.ibb.co/LCt4TPB/47.png", ["FB_IMAGE_48"] = "https://i.ibb.co/d7zMh2W/48.png",
							["FB_IMAGE_49"] = "https://i.ibb.co/Sc052qM/49.png", ["FB_IMAGE_50"] = "https://i.ibb.co/4VJV75h/50.png", ["FB_IMAGE_51"] = "https://i.ibb.co/F53P9CP/51.png", ["FB_IMAGE_52"] = "https://i.ibb.co/xhKHVvX/52.png",
							["FB_IMAGE_53"] = "https://i.ibb.co/R63NwHC/53.png", ["FB_IMAGE_54"] = "https://i.ibb.co/MktdqYk/54.png", ["FB_IMAGE_55"] = "https://i.ibb.co/L6btMHf/55.png", ["FB_IMAGE_56"] = "https://i.ibb.co/Y85DVh7/56.png",
							["FB_IMAGE_57"] = "https://i.ibb.co/5T8Lgv1/57.png", ["FB_IMAGE_58"] = "https://i.ibb.co/ss40N2J/58.png", ["FB_IMAGE_59"] = "https://i.ibb.co/gFnGRbQ/59.png", ["FB_IMAGE_60"] = "https://i.ibb.co/qgTDGKj/60.png"
						},
						ListColors = new List<string>
						{
							"0.98 0.5 0.45 0.7", "0.98 0.5 0.45 0.5", "0.98 0.5 0.45 0.3", "1 0 0 0.7", "1 0 0 0.5", "1 0 0 0.3",
							"1 0.41 0.71 0.7", "1 0.41 0.71 0.5", "1 0.41 0.71 0.3", "1 0.08 0.58 0.7", "1 0.08 0.58 0.5", "1 0.08 0.58 0.3",
							"1 0.27 0 0.7", "1 0.27 0 0.5", "1 0.27 0 0.3", "1 0.65 0 0.7", "1 0.65 0 0.5", "1 0.65 0 0.3",
							"1 0.84 0 0.7", "1 0.84 0 0.5", "1 0.84 0 0.3", "1 1 0 0.7", "1 1 0 0.5", "1 1 0 0.3",
							"1 0.89 0.71 0.7", "1 0.89 0.71 0.5", "1 0.89 0.71 0.3", "0.87 0.63 0.87 0.7", "0.87 0.63 0.87 0.5", "0.87 0.63 0.87 0.3",
							"1 0 1 0.7", "1 0 1 0.5", "1 0 1 0.3", "0.5 0 0.5 0.7", "0.5 0 0.5 0.5", "0.5 0 0.5 0.3",
							"0.29 0 0.51 0.7", "0.29 0 0.51 0.5", "0.29 0 0.51 0.3", "0.68 1 0.18 0.7", "0.68 1 0.18 0.5", "0.68 1 0.18 0.3",
							"0 1 0 0.7", "0 1 0 0.5", "0 1 0 0.3", "0.2 0.8 0.2 0.7", "0.2 0.8 0.2 0.5", "0.2 0.8 0.2 0.3",
							"0 0.5 0 0.7", "0 0.5 0 0.5", "0 0.5 0 0.3", "0.5 0.5 0 0.7", "0.5 0.5 0 0.5", "0.5 0.5 0 0.3",
							"0.4 0.8 0.67 0.7", "0.4 0.8 0.67 0.5", "0.4 0.8 0.67 0.3", "0 1 1 0.7", "0 1 1 0.5", "0 1 1 0.3",
							"0 0.81 0.82 0.7", "0 0.81 0.82 0.5", "0 0.81 0.82 0.3", "0 0 1 0.7", "0 0 1 0.5", "0 0 1 0.3",
							"0 0 0.55 0.7", "0 0 0.55 0.5", "0 0 0.55 0.3", "0.86 0.86 0.86 0.7", "0.86 0.86 0.86 0.5", "0.86 0.86 0.86 0.3",
							"0.66 0.66 0.66 0.7", "0.66 0.66 0.66 0.5", "0.66 0.66 0.66 0.3", "0.5 0.5 0.5 0.7", "0.5 0.5 0.5 0.5", "0.5 0.5 0.5 0.3",
							"0 0 0 0.7", "0 0 0 0.5", "1 1 1 0.7", "1 1 1 0.5", "1 1 1 0.3", "0 0 0 0"
						}
					}
				};
			}
		}
		
		protected override void LoadConfig()
        {
            base.LoadConfig();
			 
			try
			{
				config = Config.ReadObject<FastButtonsConfig>();
			}
			catch  
			{
				PrintWarning(LanguageEnglish ? "Configuration read error! Creating a default configuration!" : "Ошибка чтения конфигурации! Создание дефолтной конфигурации!");
				LoadDefaultConfig();
			}
			
			if(config.Setting.ListButtons == null) config.Setting.ListButtons = new List<Buttons>();
			if(config.Setting.ListButtonsPerms == null) config.Setting.ListButtonsPerms = new Dictionary<string, List<Buttons>>();
			if(config.Setting.ListButtonsIndividual == null) config.Setting.ListButtonsIndividual = new List<Buttons>();
			if(config.Setting.ListButtonsIndividualPerms == null) config.Setting.ListButtonsIndividualPerms = new Dictionary<string, List<Buttons>>();
			
			SaveConfig();
        }
		protected override void LoadDefaultConfig() => config = FastButtonsConfig.GetNewConfiguration();
        protected override void SaveConfig() => Config.WriteObject(config);
		
		#endregion
		
		#region Data
		
		private class Data
		{
			public bool ServerButtons = true;
			public bool CustomButtons = true;
			public bool ShowButtons = true;
			public bool ScreenDeath = true;
			public float ButtonSize = 31.0f;
			public float ButtonsSpacing = 2.0f;
			public int ButtonsOnPage = 6;
			public List<Buttons> ListButtons = new List<Buttons>();
			public List<Buttons> ListButtonsIndividual = new List<Buttons>();
		}
		
		private Dictionary<ulong, Data> StoredData = new Dictionary<ulong, Data>();
		
		private void LoadData(ulong userID)
		{
			var Data = Interface.Oxide.DataFileSystem.ReadObject<Data>($"XDataSystem/XFastButtons/{userID}");
			
			StoredData[userID] = Data ?? new Data();
		}
		
		private void SaveData(ulong userID) => Interface.Oxide.DataFileSystem.WriteObject($"XDataSystem/XFastButtons/{userID}", StoredData[userID]);
		
		#endregion
		
		#region Commands
		
		[ConsoleCommand("fb_open_s_b")]
		private void ccmdSettingsB(ConsoleSystem.Arg args)
		{
			BasePlayer player = args.Player();
			
			if(player == null || args?.Args == null) return;
			
			if(Cooldowns.ContainsKey(player))
				if(Cooldowns[player].Subtract(DateTime.Now).TotalSeconds >= 0) return;
			
			Cooldowns[player] = DateTime.Now.AddSeconds(0.2f);
			
			if(args.GetString(0) == "page")
			{
				int.TryParse(args.GetString(1), out int page);
				
				GUIButtons(player, page);
				
				return;
			}
			
			if(!permission.UserHasPermission(player.UserIDString, permSettings))
			{
				SendReply(player, lang.GetMessage("NOPERM", this, player.UserIDString));
				
				return;
			}
			
			switch(args.GetString(0))
			{
				case "open":
				{
					GUISetting(player);
					
					break;
				}
				case "option":
				{
					switch(args.GetString(1))
					{
						case "serverButtons":
						{
							StoredData[player.userID].ServerButtons = !StoredData[player.userID].ServerButtons;
							
							if(!StoredData[player.userID].ServerButtons)
								RemoveServer(player);
							
							break;
						}
						case "customButtons":
						{
							StoredData[player.userID].CustomButtons = !StoredData[player.userID].CustomButtons;
							
							if(!StoredData[player.userID].CustomButtons)
								RemoveCustom(player);
							
							break;
						}
						case "showButtons":
						{
							StoredData[player.userID].ShowButtons = !StoredData[player.userID].ShowButtons;
							
							if(!StoredData[player.userID].ShowButtons)
							{
								RemoveServer(player);
								RemoveCustom(player);
							}
							
							break;
						}
						case "screenDeath":
						{
							StoredData[player.userID].ScreenDeath = !StoredData[player.userID].ScreenDeath;
							
							break;
						}
						case "buttonSize":
						{
							float.TryParse(args.GetString(2), out float x);
							
							StoredData[player.userID].ButtonSize = x < 10 ? 10 : x;
							
							break;
						}
						case "buttonsSpacing":
						{
							float.TryParse(args.GetString(2), out float x);
							
							StoredData[player.userID].ButtonsSpacing = x < 0 ? 2 : x;
							
							break;
						}
						case "buttonsOnPage":
						{
							int.TryParse(args.GetString(2), out int x);
							
							StoredData[player.userID].ButtonsOnPage = x < 1 ? 1 : x;
							
							break;
						}
					}
					
					GUIButtons(player);
					GUIButtonsIndividual(player);
					GUISetting(player);
					
					break;
				}
			}
		}
		
		[ConsoleCommand("fb_open_s")]
		private void ccmdSettings(ConsoleSystem.Arg args)
		{
			BasePlayer player = args.Player();
			
			if(player == null || args?.Args == null) return;
			
			if(Cooldowns.ContainsKey(player))
				if(Cooldowns[player].Subtract(DateTime.Now).TotalSeconds >= 0) return;
			
			Cooldowns[player] = DateTime.Now.AddSeconds(0.2f);
			
			if(!permission.UserHasPermission(player.UserIDString, permUse))
			{
				SendReply(player, lang.GetMessage("NOPERM", this, player.UserIDString));
				
				return;
			}
			
			int.TryParse(args.GetString(1), out int index);
			bool.TryParse(args.GetString(2), out bool isEdit);
			
			switch(args.GetString(0))
			{
				case "cancel":
				{
					_create_edit_buttons.Remove(player.userID);
					
					CuiHelper.DestroyUi(player, ".CE_GUI");
					CuiHelper.DestroyUi(player, ".B_CI_GUI");
					CuiHelper.DestroyUi(player, ".CBI.EDIT");
					
					break;
				}
				case "add":
				{
					if(StoredData[player.userID].ListButtons.Count < config.Setting.MaxCustomButtons)
					{
						_create_edit_buttons[player.userID] = new Buttons("Text", "chat.say /kit start", _c2, _f1, false, null, null, null, null, null, null);
						
						GUICreateEditButton(player);
					}
					
					break;
				}
				case "addSave":
				{
					if(_create_edit_buttons.ContainsKey(player.userID))
					{
						StoredData[player.userID].ListButtons.Add(_create_edit_buttons[player.userID]);
						
						_create_edit_buttons.Remove(player.userID);
						
						GUIButtons(player);
						GUISetting(player);
					}
					
					break;
				}
				case "remove":
				{
					if(index >= 0 && index < StoredData[player.userID].ListButtons.Count)
					{
						StoredData[player.userID].ListButtons.RemoveAt(index);
						
						_create_edit_buttons.Remove(player.userID);
						
						GUIButtons(player);
						GUISetting(player);
					}
					
					break;
				}
				case "edit":
				{
					if(index >= 0 && index < StoredData[player.userID].ListButtons.Count)
					{
						var edit = StoredData[player.userID].ListButtons[index];
						_create_edit_buttons[player.userID] = new Buttons(edit.Text, edit.Command, edit.Color, edit.Font, false, null, null, null, null, null, null);
						
						GUICreateEditButton(player, index, isEdit);
					}
					
					break;
				}
				case "editSave":
				{
					if(index >= 0 && index < StoredData[player.userID].ListButtons.Count && _create_edit_buttons.ContainsKey(player.userID))
					{
						var edit = _create_edit_buttons[player.userID];
						StoredData[player.userID].ListButtons[index] = new Buttons(edit.Text, edit.Command, edit.Color, edit.Font, false, null, null, null, null, null, null);
						
						_create_edit_buttons.Remove(player.userID);
						
						GUIButtons(player);
						GUISetting(player);
					}
					
					break;
				}
				case "optionCreateEdit":
				{
					if(_create_edit_buttons.ContainsKey(player.userID))
					{
						switch(args.GetString(3))
						{
							case "text":
							{
								_create_edit_buttons[player.userID].Text = string.Join(" ", args.Args.Skip(4));
								
								break;
							}
							case "command":
							{
								if(args.Args.Length > 4)
								{
									string inputCommands = string.Join(" ", args.Args.Skip(4));
									string[] arrayCommands = inputCommands.Split('|');
									List<string> listCommands = new List<string>();
									
									foreach(string command in arrayCommands)
									{
										string cmd = command.Trim();
										if(string.IsNullOrEmpty(cmd)) continue;
										
										if(cmd.StartsWith("chat.say", StringComparison.OrdinalIgnoreCase))
										{
											string argsCommand = cmd.Substring(8).Trim();
											
											listCommands.Add($"chat.say \"{argsCommand}\"");
										}
										else
											listCommands.Add(cmd);
										
										_create_edit_buttons[player.userID].Command = string.Join("|", listCommands);
									}
								}
								else
									_create_edit_buttons[player.userID].Command = "chat.say /kit start";
								
								break;
							}
							case "color":
							{
								_create_edit_buttons[player.userID].Color = args.GetString(4, _c2).Replace("'", "");
								
								break;
							}
							case "font":
							{
								_create_edit_buttons[player.userID].Font = args.GetString(4, _f1);
								
								break;
							}
						}
					
						GUICreateEditButton(player, index, isEdit);
						if(_create_edit_buttons[player.userID].Individual) GUIButtonIndividual(player);
					}
					
					break;
				}
			}
			
			EffectNetwork.Send(new Effect("assets/bundled/prefabs/fx/notice/loot.drag.grab.fx.prefab", player, 0, new Vector3(), new Vector3()), player.Connection);
		}
		
		[ConsoleCommand("fb_open_s_i")]
		private void ccmdSettingsIndividual(ConsoleSystem.Arg args)
		{
			BasePlayer player = args.Player();
			
			if(player == null || args?.Args == null) return;
			
			if(Cooldowns.ContainsKey(player))
				if(Cooldowns[player].Subtract(DateTime.Now).TotalSeconds >= 0) return;
			
			Cooldowns[player] = DateTime.Now.AddSeconds(0.2f);
			
			if(!permission.UserHasPermission(player.UserIDString, permUse))
			{
				SendReply(player, lang.GetMessage("NOPERM", this, player.UserIDString));
				
				return;
			}
			
			int.TryParse(args.GetString(1), out int index);
			bool.TryParse(args.GetString(2), out bool isEdit);
			
			switch(args.GetString(0))
			{
				case "add":
				{
					if(StoredData[player.userID].ListButtonsIndividual.Count < config.Setting.MaxCustomButtonsIndividual)
					{
						_create_edit_buttons[player.userID] = new Buttons("Text", "chat.say /kit start", _c2, _f1, true, "Overlay", "", "1 0.5", "1 0.5", "-25 -25", "25 25");
						
						_offsets[player.userID] = new Offsets(5, -25, -25, 25, 25);
						
						GUICreateEditButton(player);
						GUICreateEditButtonIndividual(player);
						GUIButtonIndividual(player);
					}
					
					break;
				}
				case "addSave":
				{
					if(_create_edit_buttons.ContainsKey(player.userID))
					{
						_create_edit_buttons[player.userID].OffsetMinXY = $"{_offsets[player.userID].OffsetMinX} {_offsets[player.userID].OffsetMinY}";
						_create_edit_buttons[player.userID].OffsetMaxXY = $"{_offsets[player.userID].OffsetMaxX} {_offsets[player.userID].OffsetMaxY}";
						
						StoredData[player.userID].ListButtonsIndividual.Add(_create_edit_buttons[player.userID]);
						
						_create_edit_buttons.Remove(player.userID);
						_offsets.Remove(player.userID);
						
						GUIButtonsIndividual(player);
						GUISetting(player);
						
						CuiHelper.DestroyUi(player, ".CBI.EDIT");
					}
					
					break;
				}
				case "remove":
				{
					if(index >= 0 && index < StoredData[player.userID].ListButtonsIndividual.Count)
					{
						RemoveCustom(player);
						
						StoredData[player.userID].ListButtonsIndividual.RemoveAt(index);
						
						_create_edit_buttons.Remove(player.userID);
						_offsets.Remove(player.userID);
						
						GUIButtonsIndividual(player);
						GUISetting(player);
						
						CuiHelper.DestroyUi(player, ".CBI.EDIT");
					}
					
					break;
				}
				case "edit":
				{
					if(index >= 0 && index < StoredData[player.userID].ListButtonsIndividual.Count)
					{
						var edit = StoredData[player.userID].ListButtonsIndividual[index];
						_create_edit_buttons[player.userID] = new Buttons(edit.Text, edit.Command, edit.Color, edit.Font, edit.Individual, edit.Parent, edit.Image, edit.AnchorMinXY, edit.AnchorMaxXY, edit.OffsetMinXY, edit.OffsetMaxXY);
						
						float[] array1 = edit.OffsetMinXY.Split(' ').Select(str => Convert.ToSingle(str)).ToArray();
						float[] array2 = edit.OffsetMaxXY.Split(' ').Select(str => Convert.ToSingle(str)).ToArray();
						
						_offsets[player.userID] = new Offsets(5, array1[0], array1[1], array2[0], array2[1]);
						
						GUICreateEditButton(player, index, isEdit);
						GUICreateEditButtonIndividual(player);
						GUIButtonIndividual(player);
					}
					
					break;
				}
				case "editSave":
				{
					if(index >= 0 && index < StoredData[player.userID].ListButtonsIndividual.Count && _create_edit_buttons.ContainsKey(player.userID))
					{
						_create_edit_buttons[player.userID].OffsetMinXY = $"{_offsets[player.userID].OffsetMinX} {_offsets[player.userID].OffsetMinY}";
						_create_edit_buttons[player.userID].OffsetMaxXY = $"{_offsets[player.userID].OffsetMaxX} {_offsets[player.userID].OffsetMaxY}";
						
						var edit = _create_edit_buttons[player.userID];
						StoredData[player.userID].ListButtonsIndividual[index] = new Buttons(edit.Text, edit.Command, edit.Color, edit.Font, edit.Individual, edit.Parent, edit.Image, edit.AnchorMinXY, edit.AnchorMaxXY, edit.OffsetMinXY, edit.OffsetMaxXY);
						
						_create_edit_buttons.Remove(player.userID);
						_offsets.Remove(player.userID);
						
						GUIButtonsIndividual(player);
						GUISetting(player);
						
						CuiHelper.DestroyUi(player, ".CBI.EDIT");
					}
					
					break;
				}
				case "anchor":
				{
					_create_edit_buttons[player.userID].AnchorMinXY = args.GetString(1, "1 0.5").Replace("'", "");
					_create_edit_buttons[player.userID].AnchorMaxXY = args.GetString(2, "1 0.5").Replace("'", "");
					
					GUICreateEditButtonIndividual(player);
					GUIButtonIndividual(player);
					
					break;
				}
				case "image":
				{
					_create_edit_buttons[player.userID].Image = args.GetString(1, "");
					_create_edit_buttons[player.userID].Text = "";
					
					GUICreateEditButtonIndividual(player);
					GUIButtonIndividual(player);
								
					break;
				}
				case "parent":
				{
					_create_edit_buttons[player.userID].Parent = args.GetString(1, "Overlay");
					
					GUICreateEditButtonIndividual(player);
					GUIButtonIndividual(player);
								
					break;
				}
			}
			
			EffectNetwork.Send(new Effect("assets/bundled/prefabs/fx/notice/loot.drag.grab.fx.prefab", player, 0, new Vector3(), new Vector3()), player.Connection);
		}
		
		[ConsoleCommand("fb_open_s_i_move")]
		private void ccmdSettingsIndividualMove(ConsoleSystem.Arg args)
		{
			BasePlayer player = args.Player();
			
			if(player == null || args?.Args == null) return;
			
			if(Cooldowns.ContainsKey(player))
				if(Cooldowns[player].Subtract(DateTime.Now).TotalSeconds >= 0) return;
			
			Cooldowns[player] = DateTime.Now.AddSeconds(0.1f);
			
			if(_offsets.ContainsKey(player.userID))
			{
				float amount = _offsets[player.userID].Amount;
				
				switch(args.GetString(0))
				{
					case "amount":
					{
						float.TryParse(args.GetString(1), out float x);
						
						_offsets[player.userID].Amount = x <= 0 ? 5 : x;
						
						break;
					}
					case "upMinX":
					{
						_offsets[player.userID].OffsetMinX += amount;
						
						break;
					}
					case "upMinY":
					{
						_offsets[player.userID].OffsetMinY += amount;
						
						break;
					}
					case "upMaxX":
					{
						_offsets[player.userID].OffsetMaxX += amount;
						
						break;
					}
					case "upMaxY":
					{
						_offsets[player.userID].OffsetMaxY += amount;
						
						break;
					}
					case "downMinX":
					{
						_offsets[player.userID].OffsetMinX -= amount;
						
						break;
					}
					case "downMinY":
					{
						_offsets[player.userID].OffsetMinY -= amount;
						
						break;
					}
					case "downMaxX":
					{
						_offsets[player.userID].OffsetMaxX -= amount;
						
						break;
					}
					case "downMaxY":
					{
						_offsets[player.userID].OffsetMaxY -= amount;
						
						break;
					}
				}
					
				GUIButtonIndividualMove(player);
				GUIButtonIndividual(player);
				
				EffectNetwork.Send(new Effect("assets/bundled/prefabs/fx/notice/loot.drag.grab.fx.prefab", player, 0, new Vector3(), new Vector3()), player.Connection);
			}
		}
		
		[ConsoleCommand("fb_use_command")]
		private void ccmdUseCommand(ConsoleSystem.Arg args)
		{
			BasePlayer player = args.Player();
			
			if(player == null || args?.Args == null) return;
			
			string inputCommands = args.GetString(0).Replace("'", "");
			
			foreach(string command in inputCommands.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
				player.SendConsoleCommand(command.Replace("%ID%", player.UserIDString).Replace("%POS%", player.transform.position.ToString().Replace(" ", "")).Trim());
		}
		
		#endregion
		
		#region Hooks
		
		private void OnServerInitialized()
		{
			PrintWarning("\n-----------------------------\n" +
			"     Author - Monster\n" +
			"     VK - vk.com/idannopol\n" +
			"     Discord - Monster#4837\n" +
			"-----------------------------");
			
			BasePlayer.activePlayerList.ToList().ForEach(OnPlayerConnected);
			
			if(!ImageLibrary)
			{
				PrintError(LanguageEnglish ? "You don't have the plugin installed - ImageLibrary!" : "У вас не установлен плагин - ImageLibrary!");
				Interface.Oxide.UnloadPlugin(Name);
				
				return;
			}
			
			timer.Every(90, () => 
			{
				foreach(BasePlayer player in BasePlayer.activePlayerList)
					if(StoredData.ContainsKey(player.userID))
						SaveData(player.userID);
			});
			
			foreach(var img in config.GUI.ListServerImages)
				ImageLibrary.Call("AddImage", img.Value, img.Key);
				
			foreach(var img in config.GUI.ListImages)
				ImageLibrary.Call("AddImage", img.Value, img.Key);
				
			InitializeLang();
			
			permission.RegisterPermission(permUse, this);
			permission.RegisterPermission(permSettings, this);
			
			foreach(var buttonsPerm in config.Setting.ListButtonsPerms)
				if(!permission.PermissionExists(buttonsPerm.Key, this))
					permission.RegisterPermission(buttonsPerm.Key, this);
			
			foreach(var buttonsIndividualPerms in config.Setting.ListButtonsIndividualPerms)
			{
				if(!permission.PermissionExists(buttonsIndividualPerms.Key, this))
					permission.RegisterPermission(buttonsIndividualPerms.Key, this);
				
				_countButtonsIndividual += buttonsIndividualPerms.Value.Count;
			}
			
			string txtUI = $"{config.GUI.ParentLayerUnderSlots}_TXT_AGR4ZQN=";
			
			_countButtonsIndividual += config.Setting.ListButtonsIndividual.Count;
			
			NextTick(() =>
			{
				if(config.Setting.ListLootableEntity == null)
				{
					Unsubscribe(nameof(OnLootEntity));
					Unsubscribe(nameof(OnPlayerLootEnd));
				}
				
				if(config.Setting.ListMountableEntity == null)
				{
					Unsubscribe(nameof(OnEntityMounted));
					Unsubscribe(nameof(OnEntityDismounted));
				}
			});
		}
		
		private void Unload()
		{
			foreach(BasePlayer player in BasePlayer.activePlayerList)
				if(StoredData.ContainsKey(player.userID))
				{
					RemoveAllGUI(player);
					SaveData(player.userID);
				}
		}
		
		private void OnLootEntity(BasePlayer player, BaseEntity entity)
		{
			if(player == null) return;
			
			if(StoredData.ContainsKey(player.userID) && (config.Setting.ListLootableEntity.Count == 0 || config.Setting.ListLootableEntity.Contains(entity.ShortPrefabName)))
			{
				_playersLootEntity[player.userID] = entity.ShortPrefabName;
				RemoveAllGUI(player);
			}
		}
		
		private void OnPlayerLootEnd(PlayerLoot inventory)
		{
			BasePlayer player = inventory.baseEntity;
			
			if(player != null && StoredData.ContainsKey(player.userID) && _playersLootEntity.ContainsKey(player.userID))
			{
				GUIButtons(player);
				GUIButtonsIndividual(player);
				
				_playersLootEntity.Remove(player.userID);
			}
		}
		
		private void OnEntityMounted(BaseEntity entity, BasePlayer player)
		{
			if(StoredData.ContainsKey(player.userID) && (config.Setting.ListMountableEntity.Count == 0 || config.Setting.ListMountableEntity.Contains(entity.ShortPrefabName)))
			{
				_playersMountEntity[player.userID] = entity.ShortPrefabName;
				RemoveAllGUI(player);
			}
		}
		
		private void OnEntityDismounted(BaseEntity entity, BasePlayer player)
		{
			if(StoredData.ContainsKey(player.userID) && _playersMountEntity.ContainsKey(player.userID))
			{
				GUIButtons(player);
				GUIButtonsIndividual(player);
				
				_playersMountEntity.Remove(player.userID);
			}
		}
		
		private void OnPlayerConnected(BasePlayer player)
		{
			if(player.IsReceivingSnapshot)
            {
                NextTick(() => OnPlayerConnected(player));
                return;
            }
			
			LoadData(player.userID);
			
			GUIButtons(player);
			GUIButtonsIndividual(player);
		}
		
	    private void OnPlayerDisconnected(BasePlayer player)
		{
			if(StoredData.ContainsKey(player.userID))
			{
				SaveData(player.userID);
				StoredData.Remove(player.userID);
			}
		}
		
		#endregion
		
		#region Permissions
		
		private void OnUserPermissionGranted(string id, string permName) => OnUserPermissionRevoked(id, permName);
		private void OnUserPermissionRevoked(string id, string permName)
		{
			if(config.Setting.ListButtonsPerms.ContainsKey(permName) || config.Setting.ListButtonsIndividualPerms.ContainsKey(permName) || permName == permSettings)
				PlayerCheckPermissions(id);
		}
		
		private void OnUserGroupAdded(string id) => PlayerCheckPermissions(id);
		private void OnUserGroupRemoved(string id) => PlayerCheckPermissions(id);
		
		private void PlayerCheckPermissions(string id)
		{
			BasePlayer player = BasePlayer.FindByID(ulong.Parse(id));
			
			if(player != null && !_playersLootEntity.ContainsKey(player.userID) && !_playersMountEntity.ContainsKey(player.userID))
				if(StoredData.ContainsKey(player.userID))
				{
					RemoveAllGUI(player);
					
					GUIButtons(player);
					GUIButtonsIndividual(player);
				}
		}
		
		#endregion
		
		#region ScreenDeath
		
		private void OnPlayerRespawned(BasePlayer player)
		{
			if(StoredData.ContainsKey(player.userID) && StoredData[player.userID].ScreenDeath)
			{
				GUIButtons(player);
				GUIButtonsIndividual(player);
			}
		}
		
		private void OnEntityDeath(BasePlayer player)
		{
			if(StoredData.ContainsKey(player.userID) && StoredData[player.userID].ScreenDeath)
				RemoveAllGUI(player);
		}
		
		#endregion
		
		#region RemovePlayerButtonsIndividual
		
		private int _countButtonsIndividual = 0;
		
		private void RemoveServer(BasePlayer player)
		{
			for(int i = 0; i < _countButtonsIndividual; i++)
				CuiHelper.DestroyUi(player, $".BI.{i}");
		}
		
		private void RemoveCustom(BasePlayer player)
		{
			int x = 0;
			
			foreach(var button in StoredData[player.userID].ListButtonsIndividual)
			{
				CuiHelper.DestroyUi(player, $".CBI.{x}");
				
				x++;
			}
			
			CuiHelper.DestroyUi(player, ".CBI.EDIT");
		}
		
		#endregion
		
		#region GUI
		
		private void RemoveAllGUI(BasePlayer player)
		{
			CuiHelper.DestroyUi(player, ".FB_GUI");
			CuiHelper.DestroyUi(player, ".BSetting_GUI");
			
			RemoveServer(player);
			RemoveCustom(player);
		}
		
		private void GUIButtons(BasePlayer player, int Page = 0)
		{
			var data = StoredData[player.userID];
			
			if(data.ScreenDeath && !player.IsAlive()) return;
			
			CuiElementContainer container = new CuiElementContainer();
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-9.25 0", OffsetMax = "-9.25 0" },
                Image = { Color = "0 0 0 0" }
            }, config.GUI.ParentLayerUnderSlots, ".FB_GUI", ".FB_GUI");
			
			if(permission.UserHasPermission(player.UserIDString, permSettings))
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "201.5 35.5", OffsetMax = "216.5 50.5" },
					Button = { Color = _c5, Sprite = "assets/icons/gear.png", Command = "fb_open_s_b open" },
					Text = { Text = "" }
				}, ".FB_GUI");
			
			if(data.ShowButtons)
			{
				List<Buttons> list_buttons = new List<Buttons>();
				
				if(data.ServerButtons)
				{
					foreach(var buttonsPerm in config.Setting.ListButtonsPerms)
						if(permission.UserHasPermission(player.UserIDString, buttonsPerm.Key))
							list_buttons.AddRange(buttonsPerm.Value);
					
					list_buttons.AddRange(config.Setting.ListButtons);
				}
				
				if(data.CustomButtons)
					list_buttons.AddRange(data.ListButtons);
				
				if(list_buttons.Count != 0)
				{
					int x = 0, count = list_buttons.Count, countOnPage = (count - data.ButtonsOnPage * Page) > data.ButtonsOnPage ? data.ButtonsOnPage : count - data.ButtonsOnPage * Page;
					
					foreach(var button in list_buttons.Skip(data.ButtonsOnPage * Page).Take(countOnPage))
					{
						float offset = -(data.ButtonSize * countOnPage--) + -((data.ButtonsSpacing / 2) * countOnPage--);
						
						container.Add(new CuiButton
						{
							RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{offset} 0", OffsetMax = $"{offset + (data.ButtonSize * 2)} 16" },
							Button = { Color = button.Color, Command = $"fb_use_command '{button.Command}'" },
							Text = { Text = "" }
						}, ".FB_GUI", ".BUTTON");
						
						container.Add(new CuiLabel
						{
							RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
							Text = { Text = button.Text, Align = TextAnchor.MiddleCenter, Font = button.Font, FontSize = 10, Color = "1 1 1 0.8" }
						}, ".BUTTON");
						
						x++;
					}
					
					bool back = Page != 0;
					bool next = list_buttons.Count > ((Page + 1) * data.ButtonsOnPage);
					
					container.Add(new CuiButton
					{
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "192.75 18", OffsetMax = "207.75 33" },
						Button = { Color = back ? _c5 : "0.968 0.917 0.878 0.129", Sprite = "assets/icons/enter.png", Command = back ? $"fb_open_s_b page {Page - 1}" : "" },
						Text = { Text = "" }
					}, ".FB_GUI");
					
					container.Add(new CuiButton
					{
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "210.25 18", OffsetMax = "225.25 33" },
						Button = { Color = next ? _c5 : "0.968 0.917 0.878 0.129", Sprite = "assets/icons/exit.png", Command = next ? $"fb_open_s_b page {Page + 1}" : "" },
						Text = { Text = "" }
					}, ".FB_GUI");
				}
			}
			
			CuiHelper.AddUi(player, container);
		}
		
		private void GUISetting(BasePlayer player)
		{
			CuiElementContainer container = new CuiElementContainer();
			
			bool permUseYes = permission.UserHasPermission(player.UserIDString, permUse);
			var data = StoredData[player.userID];
			
			container.Add(new CuiPanel
			{
				CursorEnabled = true,
				RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = permUseYes ? "-199.5 79.25" : "-199.5 79.25", OffsetMax = permUseYes ? "180.5 434" : "180.5 186" },
				Image = { Color = config.GUI.BColor1, Material = "assets/icons/greyout.mat" }
			}, "Overlay", ".BSetting_GUI", ".BSetting_GUI");
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" },
                Image = { Color = config.GUI.BColor2 }
            }, ".BSetting_GUI", ".Setting_GUI");
			
			container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-23 -23", OffsetMax = "-3.5 -3.5" },
                Button = { Color = config.GUI.IconColor, Sprite = "assets/icons/close.png", Close = ".BSetting_GUI" },
                Text = { Text = "" }
            }, ".Setting_GUI");
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-31.5 -31.5", OffsetMax = "-26.5 0" },
                Image = { Color = config.GUI.BColor1, Material = "assets/content/ui/uibackgroundblur.mat" }
            }, ".Setting_GUI");
			
			container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -26.5", OffsetMax = "-31.5 0" },
                Text = { Text = lang.GetMessage("TITLE", this, player.UserIDString), Align = TextAnchor.MiddleCenter, FontSize = 13, Color = "1 1 1 0.75" }
            }, ".Setting_GUI");
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -31.5", OffsetMax = "0 -26.5" },
                Image = { Color = config.GUI.BColor1, Material = "assets/content/ui/uibackgroundblur.mat" }
            }, ".Setting_GUI");
			
			Dictionary<string, bool> s_buttons = new Dictionary<string, bool>
			{
				["serverButtons"] = data.ServerButtons,
				["customButtons"] = data.CustomButtons,
				["showButtons"] = data.ShowButtons
			};
			
			float offset = 1251800.35f;
			int x = 0, y = 0, z = 0;
			
			foreach(var button in s_buttons)
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-180 + (x * 121.6667)} -50.5", OffsetMax = $"{-63.3333 + (x * 121.6667)} -36.5" },
					Button = { Color = button.Value ? "0.35 0.45 0.25 1" : "0.65 0.29 0.24 1", Command = $"fb_open_s_b option {button.Key}" },
					Text = { Text = lang.GetMessage(button.Key, this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 11, Color = button.Value ? "0.75 0.95 0.41 1" : "0.92 0.79 0.76 1" }
				}, ".Setting_GUI");
				
				x++;
			}
			
			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-88.33335 -69", OffsetMax = "88.33335 -53" },
				Button = { Color = data.ScreenDeath ? "0.35 0.45 0.25 1" : "0.65 0.29 0.24 1", Command = $"fb_open_s_b option screenDeath" },
				Text = { Text = lang.GetMessage("SCREEN_DEATH", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 11, Color = data.ScreenDeath ? "0.75 0.95 0.41 1" : "0.92 0.79 0.76 1" }
			}, ".Setting_GUI");
			
			Dictionary<string, string> s_inputs = new Dictionary<string, string>
			{
				["buttonSize"] = data.ButtonSize.ToString(),
				["buttonsSpacing"] = data.ButtonsSpacing.ToString(),
				["buttonsOnPage"] = data.ButtonsOnPage.ToString()
			};
			
			x = 0;
			
			foreach(var input in s_inputs)
			{
				container.Add(new CuiPanel
				{
					RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-138.875 + (x * 121.5)} -84.3", OffsetMax = $"{-103.625 + (x * 121.5)} -72.3" },
					Image = { Color = _c2 }
				}, ".Setting_GUI", ".INPUT");
				
				container.Add(new CuiElement
				{
					Parent = ".INPUT",
					Components =
					{
						new CuiInputFieldComponent { Text = $"{input.Value}", Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 10, Command = $"fb_open_s_b option {input.Key}", CharsLimit = 5, NeedsKeyboard = true, Color = _c4 },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" }
					}
				});
				
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "-55 -11", OffsetMax = "55 0" },
					Text = { Text = lang.GetMessage(input.Key, this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 9, Color = _c1 }
				}, ".INPUT");
				
				x++;
			}
			
			if(permUseYes)
			{
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -101.5", OffsetMax = "0 -96.5" },
                Image = { Color = config.GUI.BColor1, Material = "assets/content/ui/uibackgroundblur.mat" }
            }, ".Setting_GUI");
			
			x = 0;
			
			foreach(var button in data.ListButtons.Take(36))
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-180 + (x * 60.4)} {52.65 - (y * 15.6)}", OffsetMax = $"{-122.1 + (x * 60.4)} {65.75 - (y * 15.6)}" },
					Button = { Color = button.Color, Command = $"fb_open_s edit {z} true" },
					Text = { Text = button.Text, Align = TextAnchor.MiddleCenter, Font = button.Font, FontSize = 9 }
				}, ".Setting_GUI");
				
				x++;
				z++;
				
				if(x == 6)
				{
					x = 0;
					y++;
				}
			}
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -36.5", OffsetMax = "0 -31.5" },
                Image = { Color = config.GUI.BColor1, Material = "assets/content/ui/uibackgroundblur.mat" }
            }, ".Setting_GUI");
			
			x = 0; y = 0; z = 0;
			
			foreach(var button in data.ListButtonsIndividual.Take(36))
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-180 + (x * 60.4)} {-54.6 - (y * 15.6)}", OffsetMax = $"{-122.1 + (x * 60.4)} {-41.5 - (y * 15.6)}" },
					Button = { Color = button.Color, Command = $"fb_open_s_i edit {z} true" },
					Text = { Text = $"#{z + 1}", Align = TextAnchor.MiddleCenter, Font = button.Font, FontSize = 9 }
				}, ".Setting_GUI");
				
				x++;
				z++;
				
				if(x == 6)
				{
					x = 0;
					y++;
				}
			}
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 30", OffsetMax = "0 35" },
                Image = { Color = config.GUI.BColor1, Material = "assets/content/ui/uibackgroundblur.mat" }
            }, ".Setting_GUI");
			
			bool max = data.ListButtons.Count >= config.Setting.MaxCustomButtons, maxi = data.ListButtonsIndividual.Count >= config.Setting.MaxCustomButtonsIndividual;
			
			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "0.5 0", OffsetMin = "5 5", OffsetMax = "-2.5 25" },
				Button = { Color = maxi ? "0.35 0.45 0.25 0.4" : "0.35 0.45 0.25 1", Command = maxi ? "" : $"fb_open_s_i add" },
				Text = { Text = lang.GetMessage("ADD_INDIVIDUAL_BUTTON", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 12, Color = maxi ? "0.75 0.95 0.41 0.4" : "0.75 0.95 0.41 1" }
			}, ".Setting_GUI");
			
			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0.5 0", AnchorMax = "1 0", OffsetMin = "2.5 5", OffsetMax = "-5 25" },
				Button = { Color = max ? "0.35 0.45 0.25 0.4" : "0.35 0.45 0.25 1", Command = max ? "" : $"fb_open_s add" },
				Text = { Text = lang.GetMessage("ADD_BUTTON", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 12, Color = max ? "0.75 0.95 0.41 0.4" : "0.75 0.95 0.41 1" }
			}, ".Setting_GUI");
			
			}
			
			CuiHelper.AddUi(player, container);
		}
		
		private void GUICreateEditButton(BasePlayer player, int index = 0, bool isEdit = false)
		{
			CuiElementContainer container = new CuiElementContainer();
			
			var settings = _create_edit_buttons[player.userID];
			
			container.Add(new CuiPanel
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
				Image = { Color = config.GUI.BColor2 }
			}, ".Setting_GUI", ".CE_GUI", ".CE_GUI");
			
			int x = 0, y = 0;
			
			foreach(string font in _fonts)
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-180 + (x * 91)} 147.75", OffsetMax = $"{-94 + (x * 91)} 167.75" },
					Button = { Color = settings.Font == font ? _c2 : _c6, Command = $"fb_open_s optionCreateEdit {index} {isEdit} font {font}" },
					Text = { Text = "Text - 123", Align = TextAnchor.MiddleCenter, Font = font, FontSize = 11, Color = settings.Font == font ? _c1 : _c3 }
				}, ".CE_GUI");
				
				x++;
			}
			
			Dictionary<string, string> s_inputs = new Dictionary<string, string>
			{
				["text"] = settings.Text,
				["command"] = settings.Command.Replace("\"", "")
			};
			
			foreach(var input in s_inputs)
			{
				container.Add(new CuiPanel
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-180 {112.5 - (y * 50)}", OffsetMax = $"180 {142.5 - (y * 50)}" },
					Image = { Color = _c2 }
				}, ".CE_GUI", ".INPUT");
				
				container.Add(new CuiElement
				{
					Parent = ".INPUT",
					Components =
					{
						new CuiInputFieldComponent { Text = $"{input.Value}", Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 10, Command = $"fb_open_s optionCreateEdit {index} {isEdit} {input.Key}", CharsLimit = 85, NeedsKeyboard = true, Color = _c4 },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" }
					}
				});
				
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 -14", OffsetMax = "0 -1" },
					Text = { Text = lang.GetMessage(input.Key, this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 9, Color = _c1 }
				}, ".INPUT");
				
				y++;
			}
			
			x = 0; y = 0;
			
			foreach(var color in config.GUI.ListColors.Take(84))
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-172.5 + (x * 25)} {20.75 - (y * 25)}", OffsetMax = $"{-152.5 + (x * 25)} {40.75 - (y * 25)}" },
					Button = { Color = color, Command = $"fb_open_s optionCreateEdit {index} {isEdit} color '{color}'" },
					Text = { Text = "" }
				}, ".CE_GUI", ".COLOR");
				
				if(color == settings.Color)
					container.Add(new CuiPanel
					{
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
						Image = { Color = "0.5 1 0.5 1", Sprite = "assets/icons/vote_up.png" }
					}, ".COLOR");
				
				x++;
				
				if(x == 14)
				{
					x = 0;
					y++;
				}
			}
			
			string command = settings.Individual ? "fb_open_s_i" : "fb_open_s";
			
			if(isEdit)
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-150 26.5", OffsetMax = "-2.5 46.5" },
					Button = { Color = "0.65 0.29 0.24 1", Command = $"{command} remove {index}" },
					Text = { Text = lang.GetMessage("DELETE_BUTTON", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 12, Color = "0.92 0.79 0.76 1" }
				}, ".CE_GUI");
			
			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = isEdit ? "2.5 26.5" : "-150 26.5", OffsetMax = "150 46.5" },
				Button = { Color = "0.35 0.45 0.25 1", Command = isEdit ? $"{command} editSave {index}" : $"{command} addSave" },
				Text = { Text = lang.GetMessage("SAVE_CHANGES", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 12, Color = "0.75 0.95 0.41 1" }
			}, ".CE_GUI");
			
			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-8 5", OffsetMax = "8 21" },
				Button = { Color = config.GUI.IconColor, Sprite = "assets/icons/close.png", Command = "fb_open_s cancel" },
				Text = { Text = "" }
			}, ".CE_GUI");
			
			CuiHelper.AddUi(player, container);
		}
		
		private void GUIButtonsIndividual(BasePlayer player)
		{
			var data = StoredData[player.userID];
			
			if(data.ScreenDeath && !player.IsAlive()) return;
			
			CuiElementContainer container = new CuiElementContainer();
			
			if(data.ShowButtons)
			{
				int x = 0;
				
				if(data.ServerButtons)
				{
					List<Buttons> list_buttons = new List<Buttons>();
					
					foreach(var buttonsIndividualPerms in config.Setting.ListButtonsIndividualPerms)
						if(permission.UserHasPermission(player.UserIDString, buttonsIndividualPerms.Key))
							list_buttons.AddRange(buttonsIndividualPerms.Value);
						
					list_buttons.AddRange(config.Setting.ListButtonsIndividual);
					
					foreach(var button in list_buttons)
					{
						string name = $".BI.{x}";
						
						container.Add(new CuiButton
						{
							RectTransform = { AnchorMin = button.AnchorMinXY, AnchorMax = button.AnchorMaxXY, OffsetMin = button.OffsetMinXY, OffsetMax = button.OffsetMaxXY },
							Button = { Color = button.Color, Command = $"fb_use_command '{button.Command}'" },
							Text = { Text = "" }
						}, button.Parent, name, name);
						
						if(string.IsNullOrEmpty(button.Image))
							container.Add(new CuiLabel
							{
								RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
								Text = { Text = button.Text, Align = TextAnchor.MiddleCenter, Font = button.Font, FontSize = 10, Color = "1 1 1 0.8" }
							}, name);
						else
							container.Add(new CuiElement
							{
								Parent = name,
								Components =
								{
									new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2.5 2.5", OffsetMax = "-2.5 -2.5" },
									new CuiRawImageComponent { Png = (string) ImageLibrary.Call("GetImage", button.Image) }
								}
							});
						
						x++;
					}
				}
				
				x = 0;
				
				if(data.CustomButtons)
					foreach(var button in StoredData[player.userID].ListButtonsIndividual)
					{
						string name = $".CBI.{x}";
						
						container.Add(new CuiButton
						{
							RectTransform = { AnchorMin = button.AnchorMinXY, AnchorMax = button.AnchorMaxXY, OffsetMin = button.OffsetMinXY, OffsetMax = button.OffsetMaxXY },
							Button = { Color = button.Color, Command = $"fb_use_command '{button.Command}'" },
							Text = { Text = "" }
						}, button.Parent, name, name);
						
						if(string.IsNullOrEmpty(button.Image))
							container.Add(new CuiLabel
							{
								RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
								Text = { Text = button.Text, Align = TextAnchor.MiddleCenter, Font = button.Font, FontSize = 10, Color = "1 1 1 0.8" }
							}, name);
						else
							container.Add(new CuiElement
							{
								Parent = name,
								Components =
								{
									new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2.5 2.5", OffsetMax = "-2.5 -2.5" },
									new CuiRawImageComponent { Png = (string) ImageLibrary.Call("GetImage", button.Image) }
								}
							});
						
						x++;
					}
			}
			
			CuiHelper.AddUi(player, container);
		}
		
		private void GUICreateEditButtonIndividual(BasePlayer player)
		{
			CuiElementContainer container = new CuiElementContainer();
			
			var settings = _create_edit_buttons[player.userID];
			
			container.Add(new CuiPanel
			{
				RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "-5 0", OffsetMax = "5 292.125" },
				Image = { Color = config.GUI.BColor1, Material = "assets/icons/greyout.mat" }
			}, ".Setting_GUI", ".B_CI_GUI", ".B_CI_GUI");
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" },
                Image = { Color = config.GUI.BColor2 }
            }, ".B_CI_GUI", ".CI_GUI");
			
			int x = 0, y = 0, y2 = 0;
			
			foreach(string parent in _parents)
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-179.75 + (x * 72.5)} {-13.75 - (y2 * 11.75)}", OffsetMax = $"{-109.75 + (x * 72.5)} {-4.5 - (y2 * 11.75)}" },
					Button = { Color = settings.Parent == parent ? _c2 : _c6, Command = $"fb_open_s_i parent {parent}" },
					Text = { Text = parent, Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 7, Color = settings.Parent == parent ? _c1 : _c3 }
				}, ".CI_GUI");
				
				x++;
				
				if(x == 5)
				{
					x = 0;
					y2++;
				}
			}
			
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -39.5", OffsetMax = "0 -26.5" },
				Text = { Text = lang.GetMessage("parent", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 9, Color = _c1 }
			}, ".CI_GUI");
			
			x = 0;
			
			foreach(var img in config.GUI.ListImages.Take(69))
			{
				container.Add(new CuiPanel
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-172.5 + (x * 25)} {108.25 - (y * 25)}", OffsetMax = $"{-152.5 + (x * 25)} {128.25 - (y * 25)}" },
					Image = { Color = _c2 }
				}, ".CI_GUI", ".IMG");
				
				container.Add(new CuiElement
				{
					Parent = ".IMG",
					Components =
					{
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2.5 2.5", OffsetMax = "-2.5 -2.5" },
						new CuiRawImageComponent { Png = (string) ImageLibrary.Call("GetImage", img.Key) }
					}
				});
				
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
					Button = { Color = settings.Image == img.Key ? "0.5 1 0.5 1" : "0 0 0 0", Sprite = "assets/icons/vote_up.png", Command = $"fb_open_s_i image {img.Key}" },
					Text = { Text = "" }
				}, ".IMG");
				
				x++;
				
				if(x == 14)
				{
					x = 0;
					y++;
				}
			}
			
			if(!string.IsNullOrEmpty(settings.Image))
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "154.5 10.25", OffsetMax = "170.5 26.25" },
					Button = { Color = "0.8 0.8 0.8 0.8", Sprite = "assets/icons/clear.png", Command = "fb_open_s_i image"  },
					Text = { Text = "" }
				}, ".CI_GUI");
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 94.5", OffsetMax = "0 99.5" },
                Image = { Color = config.GUI.BColor1, Material = "assets/content/ui/uibackgroundblur.mat" }
            }, ".CI_GUI");
			
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-180 -66.25", OffsetMax = "35.5 -47.25" },
				Text = { Text = "OFFSETMIN                   OFFSETMAX", Align = TextAnchor.MiddleCenter, FontSize = 11, Color = _c1 }
			}, ".CI_GUI");
			
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "41 -66.25", OffsetMax = "179 -47.25" },
				Text = { Text = "ANCHORMIN   &   ANCHORMAX", Align = TextAnchor.MiddleCenter, FontSize = 10, Color = _c1 }
			}, ".CI_GUI");
			
			container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-140 10", OffsetMax = "-10 69.5" },
                Image = { Color = _c2 }
            }, ".CI_GUI", ".CI_GUI_A");
			
			foreach(var anchor in _anchors)
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = anchor.Key, AnchorMax = anchor.Value, OffsetMin = "-4 -4", OffsetMax = "4 4" },
					Button = { Color = anchor.Key == settings.AnchorMinXY && anchor.Value == settings.AnchorMaxXY ? "0 0.8 0 1" : "0.8 0 0 1", Command = $"fb_open_s_i anchor '{anchor.Key}' '{anchor.Value}'"  },
					Text = { Text = "" }
				}, ".CI_GUI_A");
			
			CuiHelper.AddUi(player, container);
			
			GUIButtonIndividualMove(player);
		}
		
		private void GUIButtonIndividualMove(BasePlayer player)
		{
			CuiElementContainer container = new CuiElementContainer();
			
			var offsets = _offsets[player.userID];
			
			container.Add(new CuiPanel
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "10 10", OffsetMax = "226 89" },
				Image = { Color = "0 0 0 0" }
			}, ".B_CI_GUI", ".B_MOVE_GUI", ".B_MOVE_GUI");
			
			int x = 0, y = 0;
			
			foreach(var move in _move)
			{
				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-108 + (x * 54.5)} {19.5 - (y * 32.25)}", OffsetMax = $"{-56 + (x * 54.5)} {29.5 - (y * 32.25)}" },
					Button = { Color = _c2, Command = move.Item2 },
					Text = { Text = move.Item1, Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 8 }
				}, ".B_MOVE_GUI");
				
				x++;
				
				if(x == 4)
				{
					x = 0;
					y++;
				}
			}
			
			x = 0;
			
			List<string> sides = new List<string>
			{
				$"LEFT SIDE\n{offsets.OffsetMinX.ToString("F2")}",
				$"BOTTOM SIDE\n{offsets.OffsetMinY.ToString("F2")}",
				$"RIGHT SIDE\n{offsets.OffsetMaxX.ToString("F2")}",
				$"TOP SIDE\n{offsets.OffsetMaxY.ToString("F2")}"
			};
			
			foreach(string side in sides)
			{
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-108 + (x * 54.5)} -2", OffsetMax = $"{-56 + (x * 54.5)} 18.5" },
					Text = { Text = side, Align = TextAnchor.MiddleCenter, FontSize = 8, Color = "0.7 0.7 0.7 0.7" }
				}, ".B_MOVE_GUI");
				
				x++;
			}
			
			container.Add(new CuiPanel
			{
				RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-25 11", OffsetMax = "25 23.5" },
				Image = { Color = _c2 }
			}, ".B_MOVE_GUI", ".INPUT");
			
			container.Add(new CuiElement
			{
				Parent = ".INPUT",
				Components =
				{
					new CuiInputFieldComponent { Text = $"{offsets.Amount}", Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 10, Command = $"fb_open_s_i_move amount", CharsLimit = 4, NeedsKeyboard = true, Color = _c4 },
					new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" }
				}
			});
			
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "-60 -14", OffsetMax = "60 -1" },
				Text = { Text = lang.GetMessage("move", this, player.UserIDString), Align = TextAnchor.MiddleCenter, Font = _f1, FontSize = 9, Color = _c1 }
			}, ".INPUT");
			
			CuiHelper.AddUi(player, container);
		}
		
		private void GUIButtonIndividual(BasePlayer player)
		{
			CuiElementContainer container = new CuiElementContainer();
			
			var edit = _create_edit_buttons[player.userID];
			var offset = _offsets[player.userID];
			
			string name = ".CBI.EDIT";
			
			container.Add(new CuiPanel
			{
				RectTransform = { AnchorMin = edit.AnchorMinXY, AnchorMax = edit.AnchorMaxXY, OffsetMin = $"{offset.OffsetMinX} {offset.OffsetMinY}", OffsetMax = $"{offset.OffsetMaxX} {offset.OffsetMaxY}" },
				Image = { Color = edit.Color }
			}, edit.Parent, name, name);
			
			if(string.IsNullOrEmpty(edit.Image))
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
					Text = { Text = edit.Text, Align = TextAnchor.MiddleCenter, Font = edit.Font, FontSize = 10, Color = "1 1 1 0.8" }
				}, name);
			else
				container.Add(new CuiElement
				{
					Parent = name,
					Components =
					{
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2.5 2.5", OffsetMax = "-2.5 -2.5" },
						new CuiRawImageComponent { Png = (string) ImageLibrary.Call("GetImage", edit.Image) }
					}
				});
			
			CuiHelper.AddUi(player, container);
		}
		
		#endregion
		
		#region Lang
		
        private void InitializeLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["TITLE"] = "CUSTOM COOL SERVER BUTTONS",
				["NOPERM"] = "No permissions!",
				["ADD_INDIVIDUAL_BUTTON"] = "ADD INDIVIDUAL BUTTON",
				["ADD_BUTTON"] = "ADD BUTTON",
				["DELETE_BUTTON"] = "DELETE BUTTON",
				["SAVE_CHANGES"] = "SAVE CHANGES",
				["serverButtons"] = "SERVER BUTTONS",
				["customButtons"] = "MY BUTTONS",
				["showButtons"] = "ALL BUTTONS",
				["buttonSize"] = "BUTTON SIZE",
				["buttonsSpacing"] = "SPACING",
				["buttonsOnPage"] = "NUMBER OF BUTTONS",
				["text"] = "BUTTON TEXT",
				["command"] = "BUTTON COMMAND",
				["parent"] = "BUTTON PARENT LAYER",
				["move"] = "NUMBER OF PIXELS TO MOVE",
				["SCREEN_DEATH"] = "CLEAR SCREEN OF DEATH"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["TITLE"] = "КАСТОМНЫЕ КНОПКИ КРУТОГО СЕРВЕРА",
				["NOPERM"] = "Недостаточно прав!",
				["ADD_INDIVIDUAL_BUTTON"] = "ДОБАВИТЬ ОТДЕЛЬНУЮ КНОПКУ",
				["ADD_BUTTON"] = "ДОБАВИТЬ КНОПКУ",
				["DELETE_BUTTON"] = "УДАЛИТЬ КНОПКУ",
				["SAVE_CHANGES"] = "СОХРАНИТЬ ИЗМЕНЕНИЯ",
				["serverButtons"] = "СЕРВЕРНЫЕ КНОПКИ",
				["customButtons"] = "МОИ КНОПКИ",
				["showButtons"] = "ВСЕ КНОПКИ",
				["buttonSize"] = "РАЗМЕР КНОПКИ",
				["buttonsSpacing"] = "ПРОМЕЖУТОК",
				["buttonsOnPage"] = "КОЛ-ВО КНОПОК",
				["text"] = "ТЕКСТ КНОПКИ",
				["command"] = "КОМАНДА КНОПКИ",
				["parent"] = "РОДИТЕЛЬСКИЙ СЛОЙ КНОПКИ",
				["move"] = "КОЛ-ВО ПИКСЕЛЕЙ ДЛЯ ПЕРЕМЕЩЕНИЯ",
				["SCREEN_DEATH"] = "ОЧИСТИТЬ ЭКРАН СМЕРТИ"
            }, this, "ru");
			
			lang.RegisterMessages(new Dictionary<string, string>
            {
                ["TITLE"] = "КАСТОМНІ КНОПКИ КРУТОГО СЕРВЕРА",
				["NOPERM"] = "Недостатньо прав!",
				["ADD_INDIVIDUAL_BUTTON"] = "ДОДАТИ ОКРЕМУ КНОПКУ",
				["ADD_BUTTON"] = "ДОДАТИ КНОПКУ",
				["DELETE_BUTTON"] = "ВИДАЛИТИ КНОПКУ",
				["SAVE_CHANGES"] = "ЗБЕРЕГТИ ЗМІНИ",
				["serverButtons"] = "СЕРВЕРНІ КНОПКИ",
				["customButtons"] = "МОЇ КНОПКИ",
				["showButtons"] = "ВСІ КНОПКИ",
				["buttonSize"] = "РОЗМІР КНОПКИ",
				["buttonsSpacing"] = "ПРОМІЖОК",
				["buttonsOnPage"] = "КІЛЬКІСТЬ КНОПОК",
				["text"] = "ТЕКСТ КНОПКИ",
				["command"] = "КОМАНДА КНОПКИ",
				["parent"] = "БАТЬКІВСЬКИЙ ШАР КНОПКИ",
				["move"] = "КІЛЬКІСТЬ ПІКСЕЛІВ ДЛЯ ПЕРЕМІЩЕННЯ",
				["SCREEN_DEATH"] = "ОЧИСТИТИ ЕКРАН СМЕРТІ"
            }, this, "uk");
			
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["TITLE"] = "BOTONES DE SERVIDOR PERSONALIZADOS",
				["NOPERM"] = "¡No tienes permisos!",
				["ADD_INDIVIDUAL_BUTTON"] = "AÑADA UN BOTÓN INDEPENDIENTE",
				["ADD_BUTTON"] = "AÑADIR BOTÓN",
				["DELETE_BUTTON"] = "BOTÓN QUITAR",
				["SAVE_CHANGES"] = "GUARDAR CAMBIOS",
				["serverButtons"] = "BOTONES SERVIDOR",
				["customButtons"] = "MI BOTONES",
				["showButtons"] = "TODO BOTONES",
				["buttonSize"] = "TAMAÑO BOTÓN",
				["buttonsSpacing"] = "ESPACIO",
				["buttonsOnPage"] = "NÚMERO DE BOTONES",
				["text"] = "TEXTO BOTÓN",
				["command"] = "COMANDO DE BOTÓN",
				["parent"] = "BOTÓN CAPA PADRE",
				["move"] = "NÚMERO DE PÍXELES A MOVER",
				["SCREEN_DEATH"] = "BORRAR LA PANTALLA DE LA MUERTE"
            }, this, "es-ES");
        }
		
		#endregion
	}
} 