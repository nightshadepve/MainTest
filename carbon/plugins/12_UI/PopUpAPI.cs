using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using System;
#if CARBON
using Carbon.Base;
using Carbon.Modules;
#endif
using Newtonsoft.Json.Linq;

namespace Oxide.Plugins
{
    [Info("PopUpAPI", "ThePitereq", "2.0.4")]
    public class PopUpAPI : RustPlugin
    {
        [PluginReference] private readonly Plugin ImageLibrary;

        private readonly Dictionary<ulong, Dictionary<string, int>> lastPopId = new Dictionary<ulong, Dictionary<string, int>>();
		private static bool validImages = false;
        
#if CARBON
        public ImageDatabaseModule imgDb = BaseModule.GetModule<ImageDatabaseModule>();
#endif

        private static readonly List<string> availableFonts = new List<string>()
        {
            "DroidSansMono.ttf",
            "PermanentMarker.ttf",
            "RobotoCondensed-Bold.ttf",
            "RobotoCondensed-Regular.ttf"
        };

        private void OnServerInitialized()
        {
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config);
#if CARBON
            validImages = true;
#else
			validImages = ImageLibrary != null;
			if (!validImages)
                PrintWarning("ImageLibrary plugin not found. Background images (if present) won't be displayed!");
#endif
            foreach (var popUp in config.popUps)
            {
                TextAnchor anchor;
                if (!Enum.TryParse(popUp.Value.text.anchor, false, out anchor))
                    PrintWarning($"The {popUp.Value.text.anchor} is not valid TextAnchor! See website for reference.");
                if (!availableFonts.Contains(popUp.Value.text.font))
                    PrintWarning($"The {popUp.Value.text.font} is not valid Font! See website for reference.");
                if (popUp.Value.background.url != "")
                    AddImage(popUp.Value.background.url);
                int counter = 0;
				if (validImages)
				{
					foreach (var detail in popUp.Value.background.details)
					{
						if (detail.url != "")
							AddImage(detail.url);
						counter++;
					}
				}
            }
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                foreach (var popUp in config.popUps)
                    CuiHelper.DestroyUi(player, $"PopUpAPI_{popUp.Key}_Parent");
        }

        [ConsoleCommand("ShowPopUp")]
        private void ShowPopUpConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (arg.Args == null || arg.Args.Length < 5)
            {
                Puts("Usage: ShowPopUp <userId> <configName> <fontSize> <time> \"<text>\"");
                return;
            }
            ulong userId;
            BasePlayer player = null;
            bool allPlayers = false;
            if (arg.Args[0] == "*")
                allPlayers = true;
            else
            {
                userId = arg.GetUInt64(0);
                if (userId == 0)
                {
                    Puts("UserID is invalid! Usage: ShowPopUp <userId> <configName> <fontSize> <time> \"<text>\"");
                    return;
                }
                player = BasePlayer.FindByID(userId);
                if (!player)
                {
                    Puts("Player is offline! Usage: ShowPopUp <userId> <configName> <fontSize> <time> \"<text>\"");
                    return;
                }
            }
            string profileName = arg.GetString(1);
            if (!config.popUps.ContainsKey(profileName))
            {
                Puts("Can't find this config profile name! Usage: ShowPopUp <userId> <configName> <fontSize> <time> \"<text>\"");
                return;
            }
            int fontSize = arg.GetInt(2);
            if (fontSize == 0)
            {
                Puts("Font size is invalid! Usage: ShowPopUp <userId> <configName> <fontSize> <time> \"<text>\"");
                return;
            }
            float time = arg.GetFloat(3);
            if (time == 0)
            {
                Puts("Time is invalid! Usage: ShowPopUp <userId> <configName> <fontSize> <time> \"<text>\"");
                return;
            }
            if (!allPlayers)
                ShowPopUp(player, profileName, arg.GetString(4), fontSize, time);
            else
                foreach (var oPlayer in BasePlayer.activePlayerList)
                    ShowPopUp(oPlayer, profileName, arg.GetString(4), fontSize, time);
        }

        private bool AddNewPopUpSchema(string pluginName, JObject schema)
        {
            config = Config.ReadObject<PluginConfig>();
            string key = schema["key"].ToString();
            if (config.popUps.ContainsKey(key)) return false;
            config.popUps.Add(key, new PopUpConfig()
            {
                anchor = schema["anchor"].ToString(),
                panelName = schema["name"].ToString(),
                parentName = schema["parent"].ToString(),
            });
            if ((bool)schema["background_enabled"])
                config.popUps[key].background = new BackgroundConfig()
                {
                    color = schema["background_color"].ToString(),
                    enabled = true,
                    fadeIn = (float)schema["background_fadeIn"],
                    fadeOut = (float)schema["background_fadeOut"],
                    offsetMax = schema["background_offsetMax"].ToString(),
                    offsetMin = schema["background_offsetMin"].ToString(),
                    smoothBackground = (bool)schema["background_smooth"],
                    url = schema["background_url"].ToString()
                };
            int loopCount = (int)schema["background_additionalObjectCount"];
            if (loopCount > 0)
            {
                for (int i = 0; i < loopCount; i++)
                {
                    config.popUps[key].background.details.Add(new DetailConfig()
                    {
                        color = schema[$"background_detail_{i}_color"].ToString(),
                        offsetMax = schema[$"background_detail_{i}_offsetMax"].ToString(),
                        offsetMin = schema[$"background_detail_{i}_offsetMin"].ToString(),
                        smoothBackground = (bool)schema[$"background_detail_{i}_smooth"],
                        url = schema[$"background_detail_{i}_url"].ToString()
                    });
                }
            }
            config.popUps[key].text = new TextConfig()
            {
                anchor = schema["text_anchor"].ToString(),
                color = schema["text_color"].ToString(),
                fadeIn = (float)schema["text_fadeIn"],
                fadeOut = (float)schema["text_fadeOut"],
                font = schema["text_font"].ToString(),
                offsetMax = schema["text_offsetMax"].ToString(),
                offsetMin = schema["text_offsetMin"].ToString(),
                outlineColor = schema["text_outlineColor"].ToString(),
                outlineSize = schema["text_outlineSize"].ToString()
            };
            Puts($"Plugin {pluginName} created new PopUpAPI preset named {key}. Config has been updated!");
            Config.WriteObject(config);
            return true;
        }

        private void ShowPopUp(BasePlayer player, string panelName, string text, int fontSize = 16, float time = 10f)
        {
            if (!config.popUps.ContainsKey(panelName))
            {
                PrintWarning($"Player {player.displayName} requested PopUp with config key '{panelName}' that doesn't exist!");
                return;
            }
            PopUpConfig conf = config.popUps[panelName];
            CuiElementContainer container = new CuiElementContainer();
            string fixedTextPanelName = $"PopUpAPI_{conf.panelName}_Text";
            string fixedBackgroundPanelName = $"PopUpAPI_{conf.panelName}_Background";
            string fixedParentPanelName = $"PopUpAPI_{conf.panelName}_Parent";
            UI_AddAnchor(container, fixedParentPanelName, conf.parentName, conf.anchor);
            if (conf.background.enabled)
            {
                if (!validImages || conf.background.url == "")
                    UI_AddPanel(container, fixedBackgroundPanelName, fixedParentPanelName, conf.background.color, conf.background.offsetMin, conf.background.offsetMax, conf.background.smoothBackground, conf.background.fadeIn, conf.background.fadeOut);
                else
                    UI_AddImage(container, fixedBackgroundPanelName, fixedParentPanelName, conf.background.url, conf.background.offsetMin, conf.background.offsetMax, conf.background.color, conf.background.fadeIn, conf.background.fadeOut);
            }
            int counter = 0;
            foreach (var detail in conf.background.details)
            {
                if (!validImages || detail.url == "")
                    UI_AddPanel(container, $"{fixedBackgroundPanelName}_Detail_{counter}", fixedBackgroundPanelName, detail.color, detail.offsetMin, detail.offsetMax, detail.smoothBackground, conf.background.fadeIn, conf.background.fadeOut);
                else
                    UI_AddImage(container, $"{fixedBackgroundPanelName}_Detail_{counter}", fixedBackgroundPanelName, detail.url, detail.offsetMin, detail.offsetMax, detail.color, conf.background.fadeIn, conf.background.fadeOut);
                counter++;
            }
            TextAnchor anchor;
            Enum.TryParse(conf.text.anchor, false, out anchor);
			if (conf.text.fontSize != -1)
				fontSize = conf.text.fontSize;
			if (conf.text.displayTime != -1)
				time = conf.text.displayTime;
            UI_AddText(container, fixedTextPanelName, fixedParentPanelName, conf.text.color, conf.text.font, text, anchor, fontSize, conf.text.offsetMin, conf.text.offsetMax, conf.text.fadeIn, conf.text.fadeOut, conf.text.outlineSize, conf.text.outlineColor);
            int popId = Core.Random.Range(0, 1000000);
            lastPopId.TryAdd(player.userID, new Dictionary<string, int>());
            lastPopId[player.userID].TryAdd(conf.panelName, popId);
            lastPopId[player.userID][conf.panelName] = popId;
            CuiHelper.DestroyUi(player, fixedParentPanelName);
            CuiHelper.AddUi(player, container);
            timer.Once(time, () => {
                if (lastPopId[player.userID][conf.panelName] != popId) return;
                counter = 0;
                foreach (var detail in conf.background.details)
                {
                    CuiHelper.DestroyUi(player, $"{fixedBackgroundPanelName}_Detail_{counter}");
                    counter++;
                }
                CuiHelper.DestroyUi(player, fixedBackgroundPanelName);
                CuiHelper.DestroyUi(player, fixedTextPanelName);
                timer.Once(Mathf.Max(conf.background.fadeOut, conf.text.fadeOut), () => CuiHelper.DestroyUi(player, fixedParentPanelName));
            });
        }

        private void API_ShowPopup(BasePlayer player, string text, float time = 10f, string parent = "Hud.Menu", int fontSize = 25)
        {
            int popId = Core.Random.Range(0, 1000000);
            lastPopId.TryAdd(player.userID, new Dictionary<string, int>());
            lastPopId[player.userID].TryAdd("Legacy", popId);
            lastPopId[player.userID]["Legacy"] = popId;
            CuiElementContainer container = new CuiElementContainer();
            UI_AddAnchor(container, "PopUpAPI_Legacy_Parent", parent, "0.5 1");
            UI_AddText(container, "PopUpAPI_Legacy_Text", "PopUpAPI_Legacy_Parent", "1 1 1 1", "RobotoCondensed-Bold.ttf", text, TextAnchor.UpperCenter, fontSize, "-180 -250", "180 -50", 0.5f, 0.5f, "0.7 0.7", "0 0 0 1");
            CuiHelper.DestroyUi(player, "PopUpAPI_Legacy_Parent");
            CuiHelper.AddUi(player, container);
            timer.Once(time, () => {
                if (lastPopId[player.userID]["Legacy"] != popId) return;
                CuiHelper.DestroyUi(player, "PopUpAPI_Legacy_Text");
                timer.Once(0.5f, () => CuiHelper.DestroyUi(player, "PopUpAPI_Legacy_Parent"));
            });
        }

        private static void UI_AddAnchor(CuiElementContainer container, string panelName, string parentName, string anchor)
        {
            container.Add(new CuiElement
            {
                Name = panelName,
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = "0 0 0 0"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchor,
                        AnchorMax = anchor,
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });
        }

        private static void UI_AddPanel(CuiElementContainer container, string panelName, string parentName, string color, string offsetMin, string offsetMax, bool smooth, float fadeInTime, float fadeOutTime)
        {
            string material = smooth ? "" : "assets/content/ui/namefontmaterial.mat";
            container.Add(new CuiElement
            {
                Name = panelName,
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = color,
                        Material = material,
                        FadeIn = fadeInTime
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                },
                FadeOut = fadeOutTime
            });
        }

        private void UI_AddImage(CuiElementContainer container, string panelName, string parentName, string url, string offsetMin, string offsetMax, string color, float fadeInTime, float fadeOutTime)
        {
            container.Add(new CuiElement
            {
                Name = panelName,
                Parent = parentName,
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = GetImage(url),
                        Color = color,
                        FadeIn = fadeInTime
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                },
                FadeOut = fadeOutTime
            });
        }

        private static void UI_AddText(CuiElementContainer container, string panelName, string parentName, string color, string font, string text, TextAnchor textAnchor, int fontSize, string offsetMin, string offsetMax, float fadeInTime, float fadeOutTime, string outlineSize, string outlineColor)
        {
            CuiElement element = new CuiElement
            {
                Name = panelName,
                Parent = parentName,
                Components =
                {
                    new CuiTextComponent
                    {
                        Color = color,
                        Text = text,
                        Align = textAnchor,
                        FontSize = fontSize,
                        Font = font,
                        FadeIn= fadeInTime
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                },
                FadeOut = fadeOutTime
            };
            if (outlineSize != "0 0")
                element.Components.Add(new CuiOutlineComponent
                {
                    Distance = outlineSize,
                    Color = outlineColor
                });
            container.Add(element);
        }

        private PluginConfig config = new PluginConfig();

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(config = new PluginConfig()
            {
                popUps = new Dictionary<string, PopUpConfig>()
                {
                    { "Legacy", new PopUpConfig() },
                    { "NoWay", new PopUpConfig() {
                        text = new TextConfig()
                        {
                            color = "0.91 0.87 0.83 1",
                            fadeIn = 0.5f,
                            fadeOut = 0.5f,
                            offsetMax = "100 -125",
                            offsetMin = "-100 -200",
                            outlineColor = "0 0 0 0",
                            outlineSize = "0 0"
                        },
                        background = new BackgroundConfig()
                        {
                            color = "0.145 0.135 0.12 1",
                            enabled = true,
                            fadeIn = 0.5f,
                            fadeOut = 0.5f,
                            offsetMax = "100 -125",
                            offsetMin ="-100 -200",
                            details = new List<DetailConfig>()
                            {
                                new DetailConfig()
                                {
                                    color = "0.185 0.175 0.16 1",
                                    offsetMax = "196 71",
                                    offsetMin = "4 4"
                                },
                                new DetailConfig()
                                {
                                    color = "1 1 1 1",
                                    offsetMax = "300 110",
                                    offsetMin = "-100 -120",
                                    url = "https://images.pvrust.eu/ui_icons/PopUpAPI/noway_0.png"
                                },
                            }
                        }
                    } }
                }
            }, true);
        }

        private class PluginConfig
        {
            [JsonProperty("PopUp Schematics")]
            public Dictionary<string, PopUpConfig> popUps = new Dictionary<string, PopUpConfig>();
        }

        private class PopUpConfig
        {
            [JsonProperty("Anchor Position")]
            public string anchor = "0.5 1";

            [JsonProperty("Panel Parent")]
            public string parentName = "Hud.Menu";

            [JsonProperty("Panel Family Name")]
            public string panelName = "Legacy";

            [JsonProperty("Text")]
            public TextConfig text = new TextConfig();

            [JsonProperty("Background")]
            public BackgroundConfig background = new BackgroundConfig();

        }

        private class TextConfig
        {
            [JsonProperty("Text Position - Min")]
            public string offsetMin = "-180 -250";

            [JsonProperty("Text Position - Max")]
            public string offsetMax = "180 -50";

            [JsonProperty("Font (list available on website)")]
            public string font = "RobotoCondensed-Bold.ttf";

            [JsonProperty("Text Display Time Override")]
            public float displayTime = -1;

            [JsonProperty("Text Font Size Override")]
            public int fontSize = -1;

            [JsonProperty("Text Color")]
            public string color = "1 1 1 1";

            [JsonProperty("Text Anchor")]
            public string anchor = "MiddleCenter";

            [JsonProperty("Outline - Color")]
            public string outlineColor = "0 0 0 1";

            [JsonProperty("Outline - Size")]
            public string outlineSize = "0.7 0.7";

            [JsonProperty("Fade In Time (in seconds)")]
            public float fadeIn = 0.5f;

            [JsonProperty("Fade Out Time (in seconds)")]
            public float fadeOut = 0.5f;
        }

        private class BackgroundConfig
        {
            [JsonProperty("Enabled")]
            public bool enabled = false;

            [JsonProperty("Background Position - Min")]
            public string offsetMin = "-180 -250";

            [JsonProperty("Background Position - Max")]
            public string offsetMax = "180 -50";

            [JsonProperty("Background Color")]
            public string color = "1 1 1 1";

            [JsonProperty("Smooth Background")]
            public bool smoothBackground = false;

            [JsonProperty("Background Image URL")]
            public string url = "";

            [JsonProperty("Fade In Time (in seconds)")]
            public float fadeIn = 0.5f;

            [JsonProperty("Fade Out Time (in seconds)")]
            public float fadeOut = 0.5f;

            [JsonProperty("Background Details")]
            public List<DetailConfig> details = new List<DetailConfig>();
        }

        private class DetailConfig
        {
            [JsonProperty("Background Position - Min")]
            public string offsetMin = "-180 -250";

            [JsonProperty("Background Position - Max")]
            public string offsetMax = "180 -50";

            [JsonProperty("Background Color")]
            public string color = "1 1 1 1";

            [JsonProperty("Smooth Background")]
            public bool smoothBackground = false;

            [JsonProperty("Background Image URL")]
            public string url = "";
        }

        private string GetImage(string url)
        {
            #if CARBON
            return imgDb.GetImageString(url);
            #else
            return ImageLibrary.Call<string>("GetImage", url, 0);
            #endif
        }

        private void AddImage(string url)
        {
#if CARBON
            imgDb.Queue(url);
#else
           ImageLibrary?.CallHook("AddImage", url, url, 0);
#endif
        }
    }
} 