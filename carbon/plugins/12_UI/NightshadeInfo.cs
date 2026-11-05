using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("NightshadeInfo", "Nightshade", "1.2.0")]
    [Description("Nightshade info panel: live status, rules, and Discord.")]
    public class NightshadeInfo : RustPlugin
    {
        private const string Root = "NightshadeInfo";
        private const string DataPath = "NightshadeInfo/Players";
        private const string UiCommand = "nightshadeinfo.ui";
        private const string DefaultLogo = "https://raw.githubusercontent.com/nightshadepve/Nightshadeserver/refs/heads/main/260625_Logo_75_nobg.png";
        private const string DefaultQr = "https://i.ibb.co/fz03Kqzb/discord.png";

#pragma warning disable 0649
        [PluginReference] private Plugin NightshadeRaidProtection;
        [PluginReference("Nightshade Purge Status")] private Plugin PurgeStatus;
        [PluginReference] private Plugin ServerRewards;
        [PluginReference] private Plugin ImageLibrary;
#pragma warning restore 0649

        private Settings settings;
        private Stored stored;
        private TimeZoneInfo zone;
        private bool zoneFallback;
        private Timer clock;
        private bool statusLogged;
        private string rulesetCache = string.Empty;
        private float rulesetChecked = -30f;
        private readonly Dictionary<ulong, View> open = new Dictionary<ulong, View>();
        private readonly Dictionary<string, int[]> commands = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        private class View
        {
            public int Page;
            public int Tab;
            public bool Home;
        }

        private class Settings
        {
            [JsonProperty("Toggle commands")] public List<string> ToggleCommands = new List<string>();
            [JsonProperty("Show on first join")] public bool ShowOnFirstJoin;
            [JsonProperty("Logo URL")] public string LogoUrl = DefaultLogo;
            [JsonProperty("Server name")] public string ServerName = "Nightshade";
            [JsonProperty("Server tag")] public string ServerTag = "Solo/Duo";
            [JsonProperty("Panel width")] public float PanelWidth = 800f;
            [JsonProperty("Panel height")] public float PanelHeight = 420f;
            [JsonProperty("Accent color")] public string Accent = "#A267D3";
            [JsonProperty("PvE color")] public string PveColor = "#3DDC84";
            [JsonProperty("PvP color")] public string PvpColor = "#FF4D4D";
            [JsonProperty("Warning color")] public string WarnColor = "#D58D7C";
            [JsonProperty("Text color")] public string TextColor = "#E8DDD5";
            [JsonProperty("Muted color")] public string MutedColor = "#A39BB0";
            [JsonProperty("Panel color")] public string PanelColor = "#0E0E12";
            [JsonProperty("Panel opacity (0-100)")] public float PanelOpacity = 94f;
            [JsonProperty("Dim color")] public string DimColor = "#000000";
            [JsonProperty("Dim opacity (0-100)")] public float DimOpacity = 72f;
            [JsonProperty("Time zone id")] public string TimeZoneId = "America/New_York";
            [JsonProperty("Count admins in online")] public bool CountAdmins = false;
            [JsonProperty("Status refresh seconds")] public float RefreshSeconds = 1f;
            [JsonProperty("Wipe")] public WipeCfg Wipe = new WipeCfg();
            [JsonProperty("Purge")] public PurgeCfg Purge = new PurgeCfg();
            [JsonProperty("Labels")] public Labels Labels = new Labels();
            [JsonProperty("Pages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Page> Pages = new List<Page>();
        }

        private class WipeCfg
        {
            [JsonProperty("Weekday")] public string Weekday = "Thursday";
            [JsonProperty("Ordinal in month (1 = first)")] public int Ordinal = 1;
            [JsonProperty("Hour")] public int Hour = 14;
            [JsonProperty("Minute")] public int Minute = 0;
            [JsonProperty("Label")] public string Label = "First Thursday \u00B7 2:00 PM Eastern";
        }

        private class PurgeCfg
        {
            [JsonProperty("Days before wipe")] public int DaysBeforeWipe = 2;
            [JsonProperty("Start hour")] public int StartHour = 14;
            [JsonProperty("Start minute")] public int StartMinute = 0;
            [JsonProperty("Color")] public string Color = "#FF8C1A";
        }

        private class Labels
        {
            [JsonProperty("PvE name")] public string PveName = "PVE";
            [JsonProperty("PvP name")] public string PvpName = "PVP";
            [JsonProperty("Purge name")] public string PurgeName = "PURGE";
            [JsonProperty("PvE detail")] public string PveDetail = "Weekday. Monuments and events are still PvP.";
            [JsonProperty("PvP detail")] public string PvpDetail = "Friday 8:00 PM to Monday 3:00 AM Eastern.";
            [JsonProperty("Purge detail")] public string PurgeDetail = "Tuesday of wipe week until the Thursday wipe.";
            [JsonProperty("Manual purge detail")] public string ManualPurgeDetail = "A manual purge is on.";
            [JsonProperty("PvP waiting")] public string PvpWaiting = "Time until PvP weekend";
            [JsonProperty("PvP active")] public string PvpActive = "PvP weekend ends in";
            [JsonProperty("Purge waiting")] public string PurgeWaiting = "Time until Purge";
            [JsonProperty("Purge active")] public string PurgeActive = "Purge ends in";
            [JsonProperty("PvP unavailable")] public string PvpUnavailable = "Weekend window unavailable";
            [JsonProperty("Wipe title")] public string WipeTitle = "Next wipe";
            [JsonProperty("Wipe in")] public string WipeIn = "in {time}";
            [JsonProperty("Online")] public string Online = "Online {online}";
            [JsonProperty("Sleepers")] public string Sleepers = "Sleepers {sleepers}";
            [JsonProperty("Reward points")] public string RewardPoints = "{amount} RP";
        }

        private class Page
        {
            [JsonProperty("Id")] public string Id;
            [JsonProperty("Sidebar title")] public string Title;
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Layout (home or sections)")] public string Layout = "sections";
            [JsonProperty("Commands")] public List<string> Commands = new List<string>();
            [JsonProperty("Tabs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Tab> Tabs = new List<Tab>();
        }

        private class Tab
        {
            [JsonProperty("Title")] public string Title;
            [JsonProperty("Commands")] public List<string> Commands = new List<string>();
            [JsonProperty("Columns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Column> Columns = new List<Column>();
            [JsonProperty("Show status")] public bool ShowStatus;
            [JsonProperty("About", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Block> About = new List<Block>();
            [JsonProperty("Rules title")] public string RulesTitle;
            [JsonProperty("Rules", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Block> Rules = new List<Block>();
            [JsonProperty("Discord title")] public string DiscordTitle;
            [JsonProperty("Discord lines", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Line> DiscordLines = new List<Line>();
            [JsonProperty("Discord invite")] public string DiscordInvite;
            [JsonProperty("Discord QR URL")] public string DiscordQr;
            [JsonProperty("Buttons", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Btn> Buttons = new List<Btn>();
        }

        private class Column
        {
            [JsonProperty("Blocks", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Block> Blocks = new List<Block>();
        }

        private class Block
        {
            [JsonProperty("Heading")] public string Heading;
            [JsonProperty("Lines", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Line> Lines = new List<Line>();
        }

        private class Line
        {
            [JsonProperty("Text")] public string Text;
            [JsonProperty("Tone (body, accent, warn, muted)")] public string Tone = "body";
        }

        private class Btn
        {
            [JsonProperty("Label")] public string Label = string.Empty;
            [JsonProperty("Command")] public string Command = string.Empty;
        }

        private class Stored
        {
            public Dictionary<string, bool> Seen = new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        private class Snap
        {
            public string ModeValue;
            public string ModeDetail;
            public string ModeColor;
            public string PvpValue;
            public string PvpDetail;
            public string PurgeValue;
            public string PurgeDetail;
            public string Online;
            public string PlayersDetail;
        }

        protected override void LoadDefaultConfig()
        {
            settings = Defaults();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            bool save = false;
            try { settings = Config.ReadObject<Settings>(); }
            catch (Exception ex)
            {
                PrintWarning("NightshadeInfo config could not be read. Using defaults. " + ex.Message);
                settings = null;
            }
            if (settings == null || settings.Pages == null || settings.Pages.Count == 0)
            {
                settings = Defaults();
                save = true;
            }
            Normalize(settings);
            if (save) SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(settings, true);

        private void Init()
        {
            try { stored = Interface.Oxide.DataFileSystem.ReadObject<Stored>(DataPath) ?? new Stored(); }
            catch { stored = new Stored(); }
            if (stored.Seen == null) stored.Seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            zone = ResolveZone(settings.TimeZoneId, out zoneFallback);
            IndexCommands();
            foreach (string command in commands.Keys)
                cmd.AddChatCommand(command, this, nameof(CmdChat));
            cmd.AddConsoleCommand(UiCommand, this, nameof(CmdUi));
        }

        private void OnServerInitialized()
        {
            if (zoneFallback)
                PrintWarning("Time zone '" + settings.TimeZoneId + "' was not found. Using an Eastern fallback for the wipe clock.");
            float rate = settings.RefreshSeconds < 0.5f ? 0.5f : settings.RefreshSeconds;
            clock = timer.Every(rate, Tick);
            timer.Once(1f, RegisterImages);
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary") RegisterImages();
        }

        private void Unload()
        {
            if (clock != null) clock.Destroy();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null) CuiHelper.DestroyUi(player, Root);
            }
            open.Clear();
            SaveData();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || settings == null || !settings.ShowOnFirstJoin) return;
            string id = player.UserIDString;
            bool seen;
            if (stored.Seen.TryGetValue(id, out seen) && seen) return;
            timer.Once(4f, () =>
            {
                if (player == null || !player.IsConnected) return;
                if (stored.Seen.ContainsKey(id)) return;
                stored.Seen[id] = true;
                SaveData();
                Show(player, 0, 0);
            });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            open.Remove(player.userID);
        }

        private void CmdChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command)) return;
            string key = command.Trim().TrimStart('/').ToLowerInvariant();
            int[] target;
            if (!commands.TryGetValue(key, out target)) target = new[] { 0, 0 };
            View current;
            if (open.TryGetValue(player.userID, out current))
            {
                if (IsToggle(key) || (current.Page == target[0] && current.Tab == target[1]))
                {
                    Close(player);
                    return;
                }
            }
            Show(player, target[0], target[1]);
        }

        private void CmdUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length == 0) return;
            string action = (string)arg.Args[0];
            if (action == "close") Close(player);
            else if (action == "hold") return;
            else if (action == "page" && arg.Args.Length > 1) Show(player, Parse((string)arg.Args[1]), 0);
            else if (action == "tab" && arg.Args.Length > 2) Show(player, Parse((string)arg.Args[1]), Parse((string)arg.Args[2]));
            else if (action == "run" && arg.Args.Length > 3) RunButton(player, Parse((string)arg.Args[1]), Parse((string)arg.Args[2]), Parse((string)arg.Args[3]));
        }

        private void Show(BasePlayer player, int pageIndex, int tabIndex)
        {
            if (player == null || settings.Pages == null || settings.Pages.Count == 0) return;
            pageIndex = Mathf.Clamp(pageIndex, 0, settings.Pages.Count - 1);
            Page page = settings.Pages[pageIndex];
            if (page.Tabs == null || page.Tabs.Count == 0) tabIndex = 0;
            else tabIndex = Mathf.Clamp(tabIndex, 0, page.Tabs.Count - 1);
            bool home = page.Layout == "home";
            open[player.userID] = new View { Page = pageIndex, Tab = tabIndex, Home = home };

            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = Root,
                Parent = "Overlay",
                DestroyUi = Root,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    new CuiNeedsCursorComponent(),
                    new CuiNeedsKeyboardComponent()
                }
            });

            float width = Mathf.Clamp(settings.PanelWidth, 640f, 1100f);
            float height = Mathf.Clamp(settings.PanelHeight, 340f, 560f);
            Button(container, Root, Root + ".Dim", UiCommand + " close", Hex(settings.DimColor, settings.DimOpacity / 100f), 0f, 0f, 1f, 1f, true);
            string frame = Root + ".Frame";
            Box(container, frame, Root, Hex(settings.Accent, 0.7f),
                "0.5 0.5", "0.5 0.5", Px(-width / 2f - 1f, -height / 2f - 1f), Px(width / 2f + 1f, height / 2f + 1f), null);
            string window = Root + ".Window";
            Box(container, window, frame, Hex(settings.PanelColor, settings.PanelOpacity / 100f),
                "0 0", "1 1", "1 1", "-1 -1",
                "assets/content/ui/uibackgroundblur-ingamemenu.mat");
            Button(container, window, window + ".Catch", UiCommand + " hold", "0 0 0 0", 0f, 0f, 1f, 1f, true);
            Box(container, window + ".Accent", window, Hex(settings.Accent), "0 1", "1 1", "0 -3", "0 0", null);

            DrawHeader(container, window, width);
            DrawSidebar(container, window, height, pageIndex);
            DrawContent(container, window, width, height, page, pageIndex, tabIndex, player);
            CuiHelper.AddUi(player, container);
        }

        private void Close(BasePlayer player)
        {
            if (player == null) return;
            open.Remove(player.userID);
            CuiHelper.DestroyUi(player, Root);
        }

        private void DrawHeader(CuiElementContainer container, string window, float width)
        {
            const float header = 52f;
            Box(container, window + ".Header", window, Hex("#120E16", 0.72f), "0 1", "1 1", "0 -" + F(header), "0 0", null);
            Box(container, window + ".HeaderLine", window + ".Header", Hex(settings.Accent, 0.35f), "0 0", "1 0", "0 0", "0 1", null);
            container.Add(new CuiElement
            {
                Name = window + ".Logo",
                Parent = window + ".Header",
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Url = string.IsNullOrEmpty(settings.LogoUrl) ? DefaultLogo : settings.LogoUrl,
                        Color = "1 1 1 1",
                        Sprite = "assets/content/textures/generic/fulltransparent.tga"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "14 -18", OffsetMax = "50 18" }
                }
            });
            string serverName = string.IsNullOrEmpty(settings.ServerName) ? "Nightshade" : settings.ServerName;
            float nameX = 60f;
            float nameW = Mathf.Clamp(serverName.Length * 12.2f, 96f, 260f);
            Label(container, window + ".Name", window + ".Header", serverName, Hex("#F4F1EA"), 20, TextAnchor.MiddleLeft, true,
                "0 0.5", "0 0.5", F(nameX) + " -16", F(nameX + nameW) + " 16");
            string tag = settings.ServerTag ?? string.Empty;
            if (tag.Length > 0)
            {
                float tagW = Mathf.Clamp(tag.Length * 7.2f + 18f, 72f, 120f);
                float tagX = nameX + nameW + 8f;
                Box(container, window + ".Tag", window + ".Header", Hex(settings.Accent, 0.22f), "0 0.5", "0 0.5",
                    F(tagX) + " -11", F(tagX + tagW) + " 11", null);
                Box(container, window + ".TagEdge", window + ".Tag", Hex(settings.Accent, 0.55f), "0 0", "1 1", "0 0", "0 0", null);
                Box(container, window + ".TagFill", window + ".Tag", Hex("#1A1224", 0.92f), "0 0", "1 1", "1 1", "-1 -1", null);
                Label(container, window + ".TagText", window + ".TagFill", tag, Hex(settings.Accent), 11, TextAnchor.MiddleCenter, true,
                    "0 0", "1 1", "0 0", "0 0");
            }
            Button(container, window + ".Header", window + ".Close", UiCommand + " close", Hex("#2A2033", 0.95f), width - 40f, 13f, 26f, 26f, false);
            Label(container, window + ".CloseX", window + ".Close", "\u2715", Hex("#F4F1EA"), 16, TextAnchor.MiddleCenter, true, "0 0", "1 1", "0 0", "0 0");
        }

        private void DrawSidebar(CuiElementContainer container, string window, float height, int selected)
        {
            const float side = 150f;
            const float header = 52f;
            Box(container, window + ".Side", window, Hex("#08070B", 0.62f), "0 0", "0 1", "0 0", F(side) + " -" + F(header), null);
            Box(container, window + ".SideLine", window + ".Side", Hex("#FFFFFF", 0.06f), "1 0", "1 1", "-1 0", "0 0", null);
            float y = 12f;
            for (int i = 0; i < settings.Pages.Count; i++)
            {
                Page page = settings.Pages[i];
                if (page == null || !page.Enabled) continue;
                bool on = i == selected;
                string color = on ? Hex(settings.Accent, 0.46f) : Hex("#FFFFFF", 0.035f);
                Button(container, window + ".Side", window + ".Nav." + i, UiCommand + " page " + i, color, 10f, y, side - 20f, 32f, false);
                if (on) Box(container, window + ".NavBar." + i, window + ".Nav." + i, Hex(settings.Accent), "0 0", "0 1", "0 5", "4 -5", null);
                Label(container, window + ".NavText." + i, window + ".Nav." + i, page.Title ?? page.Id, on ? Hex("#F4F1EA") : Hex(settings.MutedColor),
                    13, TextAnchor.MiddleLeft, true, "0 0", "1 1", "16 0", "-8 0");
                y += 36f;
                if (y > height - header - 16f) break;
            }
        }

        private void DrawContent(CuiElementContainer container, string window, float width, float height, Page page, int pageIndex, int tabIndex, BasePlayer player)
        {
            const float side = 150f;
            const float header = 52f;
            float contentW = width - side;
            float contentH = height - header;
            string content = window + ".Content";
            Box(container, content, window, "0 0 0 0", "0 0", "1 1", F(side) + " 0", "0 -" + F(header), null);

            Tab tab = page.Tabs != null && page.Tabs.Count > 0 ? page.Tabs[tabIndex] : null;
            float top = 0f;
            bool multi = page.Layout != "home" && page.Tabs != null && page.Tabs.Count > 1;
            if (page.Layout != "home")
            {
                string title = page.Title ?? page.Id ?? string.Empty;
                Label(container, content + ".Title", content, title.ToUpperInvariant(), Hex("#F4F1EA"), 15, TextAnchor.MiddleLeft, true,
                    "0 1", "1 1", "16 -30", "-16 -6");
                Box(container, content + ".TitleLine", content, Hex(settings.Accent, 0.8f), "0 1", "0 1", "16 -33", "58 -31", null);
                top = multi ? 72f : 38f;
            }
            if (multi)
            {
                float x = 16f;
                for (int i = 0; i < page.Tabs.Count; i++)
                {
                    string title = page.Tabs[i].Title ?? ("Page " + (i + 1));
                    float w = Mathf.Clamp(title.Length * 7.2f + 26f, 88f, 200f);
                    bool on = i == tabIndex;
                    Button(container, content, content + ".Tab." + i, UiCommand + " tab " + pageIndex + " " + i,
                        on ? Hex(settings.Accent, 0.5f) : Hex("#FFFFFF", 0.05f), x, 8f, w, 26f, false);
                    if (on) Box(container, content + ".TabBar." + i, content + ".Tab." + i, Hex(settings.Accent), "0 0", "1 0", "8 0", "-8 2", null);
                    Label(container, content + ".TabText." + i, content + ".Tab." + i, title, on ? Hex("#F4F1EA") : Hex(settings.MutedColor),
                        12, TextAnchor.MiddleCenter, true, "0 0", "1 1", "0 0", "0 0");
                    x += w + 8f;
                }
            }

            bool discord = page.Layout == "discord";
            float gutter = discord ? 356f : 0f;
            float viewH = contentH - top - 12f;
            float innerW = contentW - 28f - gutter;
            var body = new CuiElementContainer();
            float y = 8f;
            string scroll = content + ".Scroll";
            if (page.Layout == "home") DrawHome(body, scroll, tab, player, innerW, ref y);
            else if (discord) DrawDiscordText(body, scroll, tab, innerW, ref y);
            else DrawSections(body, scroll, tab, pageIndex, tabIndex, innerW, ref y);
            y += 16f;

            container.Add(new CuiElement
            {
                Name = scroll,
                Parent = content,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiScrollViewComponent
                    {
                        Vertical = true,
                        Horizontal = false,
                        MovementType = ScrollRect.MovementType.Clamped,
                        Elasticity = 0.1f,
                        Inertia = true,
                        DecelerationRate = 0.25f,
                        ScrollSensitivity = 28f,
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = "0 -" + F(Mathf.Max(y, viewH)),
                            OffsetMax = "0 0"
                        },
                        VerticalScrollbar = new CuiScrollbar
                        {
                            Size = 5f,
                            AutoHide = true,
                            HandleColor = Hex(settings.Accent, 0.85f),
                            TrackColor = "1 1 1 0.06",
                            HighlightColor = Hex(settings.Accent),
                            PressedColor = Hex(settings.Accent)
                        }
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "14 " + F(10f),
                        OffsetMax = (discord ? "-356" : "-10") + " -" + F(top + 4f)
                    }
                }
            });
            foreach (CuiElement element in body) container.Add(element);
            if (discord) DrawQrCard(container, content, tab, top);
        }

        private void DrawHome(CuiElementContainer container, string parent, Tab tab, BasePlayer player, float width, ref float y)
        {
            if (tab == null) tab = new Tab();
            if (tab.ShowStatus) DrawStatus(container, parent, player, width, ref y);
            if (tab.About != null)
            {
                foreach (Block block in tab.About) DrawBlock(container, parent, block, 0f, width, ref y);
            }
        }

        private void DrawSections(CuiElementContainer container, string parent, Tab tab, int pageIndex, int tabIndex, float width, ref float y)
        {
            if (tab == null) return;
            int columns = tab.Columns == null ? 0 : tab.Columns.Count;
            if (columns <= 0)
            {
                y += 8f;
                return;
            }
            if (columns == 1)
            {
                foreach (Block block in tab.Columns[0].Blocks) DrawBlock(container, parent, block, 0f, width, ref y);
            }
            else
            {
                float gap = 16f;
                float colW = (width - gap * (columns - 1)) / columns;
                float bottom = y;
                for (int i = 0; i < columns; i++)
                {
                    float colY = y;
                    float x = i * (colW + gap);
                    if (tab.Columns[i].Blocks != null)
                    {
                        foreach (Block block in tab.Columns[i].Blocks) DrawBlock(container, parent, block, x, colW, ref colY);
                    }
                    if (colY > bottom) bottom = colY;
                }
                y = bottom;
            }
            if (tab.Buttons != null)
            {
                float x = 0f;
                y += 8f;
                for (int i = 0; i < tab.Buttons.Count; i++)
                {
                    Btn button = tab.Buttons[i];
                    if (button == null || string.IsNullOrEmpty(button.Label)) continue;
                    float w = Mathf.Clamp(button.Label.Length * 7.6f + 28f, 110f, 280f);
                    Button(container, parent, parent + ".Btn." + pageIndex + "." + tabIndex + "." + i,
                        UiCommand + " run " + pageIndex + " " + tabIndex + " " + i, Hex(settings.Accent, 0.85f), x, y, w, 30f, false);
                    Label(container, parent + ".BtnText." + i, parent + ".Btn." + pageIndex + "." + tabIndex + "." + i, button.Label,
                        Hex("#1A1022"), 12, TextAnchor.MiddleCenter, true, "0 0", "1 1", "0 0", "0 0");
                    x += w + 8f;
                }
                y += 38f;
            }
        }

        private void DrawDiscordText(CuiElementContainer container, string parent, Tab tab, float width, ref float y)
        {
            if (tab == null) tab = new Tab();
            if (!string.IsNullOrEmpty(tab.DiscordTitle))
                DrawHeading(container, parent, tab.DiscordTitle, 0f, width, ref y);
            if (tab.DiscordLines != null)
            {
                var block = new Block { Lines = tab.DiscordLines };
                DrawBlock(container, parent, block, 0f, width, ref y);
            }
            string invite = string.IsNullOrEmpty(tab.DiscordInvite) ? "discord.gg/dFmeweQmsy" : tab.DiscordInvite;
            y += 6f;
            Label(container, parent + ".InviteCap", parent, "INVITE", Hex(settings.MutedColor), 10, TextAnchor.MiddleLeft, true,
                "0 1", "0 1", Px(0f, -(y + 16f)), Px(width, -y));
            y += 20f;
            Box(container, parent + ".InviteEdge", parent, Hex(settings.Accent, 0.45f), "0 1", "0 1", Px(0f, -(y + 36f)), Px(width, -y), null);
            Box(container, parent + ".InviteBg", parent + ".InviteEdge", Hex("#16131C", 0.98f), "0 0", "1 1", "1 1", "-1 -1", null);
            container.Add(new CuiElement
            {
                Name = parent + ".Invite",
                Parent = parent + ".InviteBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = invite,
                        FontSize = 16,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleCenter,
                        Color = Hex(settings.Accent),
                        CharsLimit = 64,
                        ReadOnly = true,
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "8 0", OffsetMax = "-8 0" }
                }
            });
            y += 48f;
            Label(container, parent + ".InviteHint", parent, "Select the invite to copy it.", Hex(settings.MutedColor), 12, TextAnchor.UpperLeft, false,
                "0 1", "0 1", Px(0f, -(y + 18f)), Px(width, -y));
            y += 24f;
        }

        private void DrawQrCard(CuiElementContainer container, string parent, Tab tab, float top)
        {
            string url = DiscordQrUrl(tab);
            float w = 332f;
            float h = 210f;
            float y = top + 8f;
            string card = parent + ".Qr";
            Box(container, card + ".Edge", parent, Hex(settings.Accent, 0.4f), "1 1", "1 1", Px(-(w + 16f), -(y + h)), Px(-12f, -y), null);
            Box(container, card, card + ".Edge", Hex("#16131C", 0.98f), "0 0", "1 1", "1 1", "-1 -1", null);
            AddRawImage(container, card + ".Img", card, url, QrKey(url), "0 0", "1 1", "6 6", "-6 -6");
        }

        private void DrawStatus(CuiElementContainer container, string parent, BasePlayer player, float width, ref float y)
        {
            Snap snap = ReadStatus(player);
            float gap = 6f;
            float cardW = (width - gap * 3f) / 4f;
            float cardH = 96f;
            DrawCard(container, parent, "Mode", 0f, y, cardW, cardH, snap.ModeColor, "MODE", snap.ModeValue, snap.ModeDetail, snap.ModeColor);
            DrawCard(container, parent, "Pvp", cardW + gap, y, cardW, cardH, settings.PvpColor, "PVP WEEKEND", snap.PvpValue, snap.PvpDetail, "#F4F1EA");
            DrawCard(container, parent, "Purge", (cardW + gap) * 2f, y, cardW, cardH, settings.Purge != null ? settings.Purge.Color : "#FF8C1A", "PURGE", snap.PurgeValue, snap.PurgeDetail, "#F4F1EA");
            DrawCard(container, parent, "Pop", (cardW + gap) * 3f, y, cardW, cardH, settings.Accent, "PLAYERS", snap.Online, snap.PlayersDetail, "#F4F1EA");
            y += cardH + 12f;
        }

        private void DrawCard(CuiElementContainer container, string parent, string key, float x, float y, float w, float h, string bar, string title, string value, string detail, string valueColor)
        {
            string name = "NightshadeInfo.St." + key;
            Box(container, name + ".Edge", parent, Hex("#FFFFFF", 0.08f), "0 1", "0 1", Px(x, -(y + h)), Px(x + w, -y), null);
            Box(container, name, name + ".Edge", Hex("#16131C", 0.98f), "0 0", "1 1", "1 1", "-1 -1", null);
            Box(container, name + ".Bar", name, Hex(bar), "0 0", "0 1", "0 8", "3 -8", null);
            Label(container, name + ".K", name, title ?? string.Empty, Hex(settings.MutedColor), 9, TextAnchor.UpperLeft, true, "0 1", "1 1", "12 -18", "-6 -4");
            Label(container, name + ".V", name, value ?? string.Empty, Hex(valueColor), 15, TextAnchor.UpperLeft, true, "0 1", "1 1", "12 -40", "-6 -18");
            Label(container, name + ".D", name, detail ?? string.Empty, Hex(settings.TextColor), 10, TextAnchor.UpperLeft, false, "0 1", "1 1", "12 -92", "-6 -42");
        }

        private void DrawHeading(CuiElementContainer container, string parent, string text, float x, float width, ref float y)
        {
            Label(container, parent + ".H." + F(x) + "." + F(y), parent, text, Hex(settings.Accent), 16, TextAnchor.MiddleLeft, true,
                "0 1", "0 1", Px(x, -(y + 24f)), Px(x + width, -y));
            y += 32f;
        }

        private void DrawBlock(CuiElementContainer container, string parent, Block block, float x, float width, ref float y)
        {
            if (block == null) return;
            if (!string.IsNullOrEmpty(block.Heading))
            {
                Label(container, parent + ".B." + F(x) + "." + F(y), parent, block.Heading, Hex(settings.Accent), 13, TextAnchor.UpperLeft, true,
                    "0 1", "0 1", Px(x, -(y + 18f)), Px(x + width, -y));
                Box(container, parent + ".U." + F(x) + "." + F(y), parent, Hex(settings.Accent, 0.85f), "0 1", "0 1",
                    Px(x, -(y + 22f)), Px(x + Mathf.Min(48f, width), -(y + 20f)), null);
                y += 30f;
            }
            if (block.Lines == null) return;
            foreach (Line line in block.Lines)
            {
                if (line == null || string.IsNullOrEmpty(line.Text))
                {
                    y += 10f;
                    continue;
                }
                int rows = Rows(Visible(line.Text), width, 13);
                float h = rows * 18f;
                Label(container, parent + ".L." + F(x) + "." + F(y), parent, line.Text, Tone(line), 13, TextAnchor.UpperLeft, false,
                    "0 1", "0 1", Px(x, -(y + h)), Px(x + width, -y));
                y += h + 8f;
            }
            y += 14f;
        }

        private void Tick()
        {
            if (open.Count == 0) return;
            var ids = new List<ulong>(open.Keys);
            foreach (ulong id in ids)
            {
                View view;
                if (!open.TryGetValue(id, out view) || !view.Home) continue;
                BasePlayer player = BasePlayer.FindByID(id);
                if (player == null || !player.IsConnected)
                {
                    open.Remove(id);
                    continue;
                }
                PushStatus(player);
            }
        }

        private void PushStatus(BasePlayer player)
        {
            Snap snap = ReadStatus(player);
            var container = new CuiElementContainer();
            UpdateText(container, "NightshadeInfo.St.Mode.V", snap.ModeValue, Hex(snap.ModeColor), 13, true, TextAnchor.UpperLeft);
            UpdateText(container, "NightshadeInfo.St.Mode.D", snap.ModeDetail, Hex(settings.TextColor), 10, false, TextAnchor.UpperLeft);
            UpdateImage(container, "NightshadeInfo.St.Mode.Bar", Hex(snap.ModeColor));
            UpdateText(container, "NightshadeInfo.St.Pvp.V", snap.PvpValue, Hex("#F4F1EA"), 13, true, TextAnchor.UpperLeft);
            UpdateText(container, "NightshadeInfo.St.Purge.V", snap.PurgeValue, Hex("#F4F1EA"), 13, true, TextAnchor.UpperLeft);
            UpdateText(container, "NightshadeInfo.St.Purge.D", snap.PurgeDetail, Hex(settings.TextColor), 10, false, TextAnchor.UpperLeft);
            UpdateText(container, "NightshadeInfo.St.Pop.V", snap.Online, Hex("#F4F1EA"), 13, true, TextAnchor.UpperLeft);
            UpdateText(container, "NightshadeInfo.St.Pop.D", snap.PlayersDetail, Hex(settings.TextColor), 10, false, TextAnchor.UpperLeft);
            CuiHelper.AddUi(player, container);
        }

        private Snap ReadStatus(BasePlayer player)
        {
            var snap = new Snap();
            Labels labels = settings.Labels ?? new Labels();
            bool weekend = false;
            double weekendSeconds = 0d;
            string weekendSource = ReadWeekend(out weekend, out weekendSeconds);

            bool manual = false;
            double manualEnd = 0d;
            string manualSource = ReadManualPurge(out manual, out manualEnd);
            bool rulesetPurge = RulesetIsPurge();

            DateTime now = DateTime.UtcNow;
            DateTime purgeStart;
            DateTime wipe;
            PurgeWindow(out purgeStart, out wipe);
            bool scheduled = now >= purgeStart && now < wipe;
            bool purge = manual || rulesetPurge || scheduled;
            string purgeColor = settings.Purge != null && !string.IsNullOrEmpty(settings.Purge.Color) ? settings.Purge.Color : "#FF8C1A";
            if (purge)
            {
                snap.ModeValue = labels.PurgeName;
                snap.ModeColor = purgeColor;
                snap.ModeDetail = "Until the wipe";
            }
            else if (weekend)
            {
                snap.ModeValue = labels.PvpName;
                snap.ModeColor = settings.PvpColor;
                snap.ModeDetail = "Weekend window";
            }
            else
            {
                snap.ModeValue = labels.PveName;
                snap.ModeColor = settings.PveColor;
                snap.ModeDetail = "Weekday";
            }

            snap.PvpDetail = "Fri 8 PM - Mon 3 AM ET";
            snap.PvpValue = (weekend ? "Ends in " : "Starts in ") + FormatDur(weekendSeconds);
            double purgeSeconds;
            if (purge)
            {
                DateTime end = wipe;
                if (manual)
                {
                    DateTime manualAt;
                    if (TryUnix(manualEnd, out manualAt) && manualAt > now && (!scheduled || manualAt > wipe))
                        end = manualAt;
                }
                purgeSeconds = (end - now).TotalSeconds;
            }
            else purgeSeconds = (purgeStart - now).TotalSeconds;
            snap.PurgeValue = (purge ? "Ends in " : "Starts in ") + FormatDur(purgeSeconds);

            string wipeLabel = settings.Wipe == null || string.IsNullOrEmpty(settings.Wipe.Label) ? "First Thursday \u00B7 2:00 PM Eastern" : settings.Wipe.Label;
            snap.PurgeDetail = "Tue of wipe week - Thu 2 PM wipe\n" + wipeLabel;
            int online = 0;
            foreach (BasePlayer other in BasePlayer.activePlayerList)
            {
                if (other == null) continue;
                if (!settings.CountAdmins && other.IsAdmin) continue;
                online++;
            }
            int sleepers = BasePlayer.sleepingPlayerList == null ? 0 : BasePlayer.sleepingPlayerList.Count;
            string onlineText = labels.Online.Replace("{online}", online.ToString(CultureInfo.InvariantCulture));
            string sleeperText = labels.Sleepers.Replace("{sleepers}", sleepers.ToString(CultureInfo.InvariantCulture));
            string points = labels.RewardPoints.Replace("{amount}", RewardPoints(player));
            snap.Online = onlineText;
            snap.PlayersDetail = sleeperText + "\n" + points;
            if (!statusLogged)
            {
                statusLogged = true;
                string purgeSource = scheduled ? "built-in schedule" : "built-in schedule (not in window)";
                if (manual) purgeSource = manualSource + (scheduled ? " + built-in schedule" : "");
                else if (rulesetPurge) purgeSource = "TruePVE ruleset purge" + (scheduled ? " + built-in schedule" : "");
                Puts("NightshadeInfo status: PvP from " + weekendSource + ". Purge from " + purgeSource + ". Mode " + snap.ModeValue + ".");
            }
            return snap;
        }

        private string ReadWeekend(out bool active, out double seconds)
        {
            object flag;
            object left;
            if (TryPair(NightshadeRaidProtection, "API_IsPvPActive", "API_SecondsUntilPvPChange", out flag, out left) && flag is bool)
            {
                active = (bool)flag;
                seconds = left != null ? ToDouble(left) : LocalSeconds(active);
                return "NightshadeRaidProtection.Call";
            }
            if (TryHookPair("API_IsPvPActive", "API_SecondsUntilPvPChange", out flag, out left) && flag is bool)
            {
                active = (bool)flag;
                seconds = left != null ? ToDouble(left) : LocalSeconds(active);
                return "Interface.CallHook";
            }
            LocalWeekend(out active, out seconds);
            return "built-in Eastern schedule";
        }

        private string ReadManualPurge(out bool active, out double endUnix)
        {
            active = false;
            endUnix = 0d;
            object flag;
            object stamp;
            if (NightshadePurgeStatusLoaded() && TryPair(PurgeStatus, "API_IsPurgeActive", "API_GetPurgeEndTimestamp", out flag, out stamp) && flag is bool)
            {
                active = (bool)flag;
                if (stamp != null) endUnix = ToDouble(stamp);
                return "NightshadePurgeStatus.Call";
            }
            if (TryHookPair("API_IsPurgeActive", "API_GetPurgeEndTimestamp", out flag, out stamp) && flag is bool)
            {
                active = (bool)flag;
                if (stamp != null) endUnix = ToDouble(stamp);
                return "Interface.CallHook";
            }
            return "none";
        }

        private bool NightshadePurgeStatusLoaded()
        {
            return PurgeStatus != null && PurgeStatus.IsLoaded;
        }

        private static bool TryPair(Plugin plugin, string flagName, string valueName, out object flag, out object value)
        {
            flag = null;
            value = null;
            if (plugin == null || !plugin.IsLoaded) return false;
            try
            {
                flag = plugin.Call(flagName);
                value = plugin.Call(valueName);
                return flag != null || value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryHookPair(string flagName, string valueName, out object flag, out object value)
        {
            flag = null;
            value = null;
            try
            {
                flag = Interface.CallHook(flagName);
                value = Interface.CallHook(valueName);
                return flag != null || value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool RulesetIsPurge()
        {
            if (UnityEngine.Time.realtimeSinceStartup - rulesetChecked < 5f) return rulesetCache == "purge";
            rulesetChecked = UnityEngine.Time.realtimeSinceStartup;
            rulesetCache = string.Empty;
            try
            {
                string path = Path.Combine(Interface.Oxide.ConfigDirectory, "TruePVE.json");
                if (!File.Exists(path)) return false;
                JObject obj = JObject.Parse(File.ReadAllText(path));
                JToken token = obj["Default RuleSet"];
                rulesetCache = token == null ? string.Empty : token.ToString().Trim();
            }
            catch (Exception)
            {
                rulesetCache = string.Empty;
            }
            return string.Equals(rulesetCache, "purge", StringComparison.OrdinalIgnoreCase);
        }

        private double LocalSeconds(bool active)
        {
            bool localActive;
            double seconds;
            LocalWeekend(out localActive, out seconds);
            return seconds;
        }

        private void LocalWeekend(out bool active, out double seconds)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            int daysSinceFriday = ((int)local.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            DateTime start = local.Date.AddDays(-daysSinceFriday).AddHours(20);
            DateTime end = start.AddDays(2).AddHours(7);
            if (local < start)
            {
                active = false;
                seconds = (start - local).TotalSeconds;
                return;
            }
            if (local < end)
            {
                active = true;
                seconds = (end - local).TotalSeconds;
                return;
            }
            active = false;
            seconds = (start.AddDays(7) - local).TotalSeconds;
        }

        private static bool TryUnix(double seconds, out DateTime utc)
        {
            utc = DateTime.UtcNow;
            if (seconds < 1000000000d || seconds > 20000000000d) return false;
            try
            {
                utc = DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private void PurgeWindow(out DateTime startUtc, out DateTime wipeUtc)
        {
            wipeUtc = NextWipeUtc();
            PurgeCfg purge = settings.Purge ?? new PurgeCfg();
            DateTime wipeLocal = TimeZoneInfo.ConvertTimeFromUtc(wipeUtc, zone);
            int days = purge.DaysBeforeWipe < 0 ? 0 : purge.DaysBeforeWipe;
            int hour = Mathf.Clamp(purge.StartHour, 0, 23);
            int minute = Mathf.Clamp(purge.StartMinute, 0, 59);
            DateTime startLocal = wipeLocal.Date.AddDays(-days).AddHours(hour).AddMinutes(minute);
            startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), zone);
        }

        private string RewardPoints(BasePlayer player)
        {
            if (player == null || ServerRewards == null || !ServerRewards.IsLoaded) return "-";
            object points = ServerRewards.Call("CheckPoints", player.userID);
            if (points == null) return "-";
            return ToDouble(points).ToString("N0", CultureInfo.InvariantCulture);
        }

        private DateTime NextWipeUtc()
        {
            WipeCfg wipe = settings.Wipe ?? new WipeCfg();
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            int year = localNow.Year;
            int month = localNow.Month;
            DateTime candidate = NthWeekday(year, month, wipe);
            if (localNow >= candidate)
            {
                month++;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }
                candidate = NthWeekday(year, month, wipe);
            }
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), zone);
        }

        private static DateTime NthWeekday(int year, int month, WipeCfg wipe)
        {
            DayOfWeek day = ParseDay(wipe.Weekday);
            int ordinal = wipe.Ordinal < 1 ? 1 : wipe.Ordinal;
            int hour = Mathf.Clamp(wipe.Hour, 0, 23);
            int minute = Mathf.Clamp(wipe.Minute, 0, 59);
            var first = new DateTime(year, month, 1, hour, minute, 0, DateTimeKind.Unspecified);
            int delta = ((int)day - (int)first.DayOfWeek + 7) % 7;
            DateTime result = first.AddDays(delta + 7 * (ordinal - 1));
            if (result.Month != month) result = result.AddDays(-7);
            return result;
        }

        private void RunButton(BasePlayer player, int pageIndex, int tabIndex, int buttonIndex)
        {
            if (pageIndex < 0 || pageIndex >= settings.Pages.Count) return;
            Page page = settings.Pages[pageIndex];
            if (page.Tabs == null || tabIndex < 0 || tabIndex >= page.Tabs.Count) return;
            Tab tab = page.Tabs[tabIndex];
            if (tab.Buttons == null || buttonIndex < 0 || buttonIndex >= tab.Buttons.Count) return;
            Btn button = tab.Buttons[buttonIndex];
            if (button == null || string.IsNullOrEmpty(button.Command)) return;
            string command = button.Command.Trim();
            if (command.StartsWith("/")) player.SendConsoleCommand("chat.say " + command);
            else player.SendConsoleCommand(command);
        }

        private void IndexCommands()
        {
            commands.Clear();
            if (settings.ToggleCommands != null)
            {
                foreach (string command in settings.ToggleCommands) AddCommand(command, 0, 0);
            }
            for (int p = 0; p < settings.Pages.Count; p++)
            {
                Page page = settings.Pages[p];
                if (page == null || !page.Enabled) continue;
                if (page.Commands != null)
                {
                    foreach (string command in page.Commands) AddCommand(command, p, 0);
                }
                if (page.Tabs == null) continue;
                for (int t = 0; t < page.Tabs.Count; t++)
                {
                    if (page.Tabs[t].Commands == null) continue;
                    foreach (string command in page.Tabs[t].Commands) AddCommand(command, p, t);
                }
            }
            if (commands.Count == 0) commands["info"] = new[] { 0, 0 };
        }

        private void AddCommand(string command, int page, int tab)
        {
            if (string.IsNullOrEmpty(command)) return;
            string key = command.Trim().TrimStart('/').ToLowerInvariant();
            if (key.Length == 0) return;
            commands[key] = new[] { page, tab };
        }

        private bool IsToggle(string key)
        {
            if (settings.ToggleCommands == null) return key == "info";
            foreach (string command in settings.ToggleCommands)
            {
                if (string.Equals(command.Trim().TrimStart('/'), key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataPath, stored);
        }

        private void RegisterImages()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded || settings == null || settings.Pages == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Page page in settings.Pages)
            {
                if (page == null || page.Tabs == null) continue;
                foreach (Tab tab in page.Tabs)
                {
                    if (tab == null || page.Layout != "discord") continue;
                    string url = DiscordQrUrl(tab);
                    if (!seen.Add(url)) continue;
                    ImageLibrary.Call("AddImage", url, QrKey(url), 0UL);
                }
            }
        }

        private static string QrKey(string url)
        {
            return string.IsNullOrEmpty(url) ? "NightshadeInfo.Qr" : "NightshadeInfo.Qr";
        }

        private static string DiscordQrUrl(Tab tab)
        {
            if (tab != null && !string.IsNullOrEmpty(tab.DiscordQr)) return tab.DiscordQr;
            return DefaultQr;
        }

        private void AddRawImage(CuiElementContainer container, string name, string parent, string url, string key, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            string png = null;
            if (ImageLibrary != null && ImageLibrary.IsLoaded)
            {
                png = ImageLibrary.Call<string>("GetImage", key);
                if (png == "0" || png == "NONE_0") png = null;
            }
            var image = new CuiRawImageComponent
            {
                Color = "1 1 1 1",
                Sprite = "assets/content/textures/generic/fulltransparent.tga"
            };
            if (!string.IsNullOrEmpty(png)) image.Png = png;
            else image.Url = url ?? string.Empty;
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    image,
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        private static void Normalize(Settings value)
        {
            if (value.ToggleCommands == null) value.ToggleCommands = new List<string>();
            if (value.Pages == null) value.Pages = new List<Page>();
            if (value.Wipe == null) value.Wipe = new WipeCfg();
            if (value.Purge == null) value.Purge = new PurgeCfg();
            if (value.Labels == null) value.Labels = new Labels();
            foreach (Page page in value.Pages)
            {
                if (page.Commands == null) page.Commands = new List<string>();
                if (page.Tabs == null) page.Tabs = new List<Tab>();
                foreach (Tab tab in page.Tabs)
                {
                    if (tab.Commands == null) tab.Commands = new List<string>();
                    if (tab.Columns == null) tab.Columns = new List<Column>();
                    if (tab.About == null) tab.About = new List<Block>();
                    if (tab.RulesTitle == null) tab.RulesTitle = string.Empty;
                    if (tab.Rules == null) tab.Rules = new List<Block>();
                    if (tab.DiscordLines == null) tab.DiscordLines = new List<Line>();
                    if (tab.Buttons == null) tab.Buttons = new List<Btn>();
                    foreach (Column column in tab.Columns)
                    {
                        if (column.Blocks == null) column.Blocks = new List<Block>();
                        foreach (Block block in column.Blocks)
                            if (block.Lines == null) block.Lines = new List<Line>();
                    }
                    foreach (Block block in tab.About)
                        if (block != null && block.Lines == null) block.Lines = new List<Line>();
                    foreach (Block block in tab.Rules)
                        if (block != null && block.Lines == null) block.Lines = new List<Line>();
                }
            }
        }

        private string Tone(Line line)
        {
            if (line.Text != null && (line.Text.Contains("<color") || line.Text.Contains("<size") || line.Text.Contains("<b>")))
                return "1 1 1 1";
            string tone = line.Tone ?? "body";
            if (tone == "warn") return Hex(settings.WarnColor);
            if (tone == "muted") return Hex(settings.MutedColor);
            if (tone == "accent") return Hex(settings.Accent);
            return Hex(settings.TextColor);
        }

        private static int Rows(string text, float width, int size)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int per = Math.Max(18, (int)(width / (size * 0.48f)));
            int rows = 1;
            int col = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    rows++;
                    col = 0;
                    continue;
                }
                col++;
                if (col >= per)
                {
                    rows++;
                    col = 0;
                }
            }
            return rows;
        }

        private static string Visible(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text ?? string.Empty;
            var builder = new System.Text.StringBuilder(text.Length);
            bool tag = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<') { tag = true; continue; }
                if (text[i] == '>') { tag = false; continue; }
                if (!tag) builder.Append(text[i]);
            }
            return builder.ToString();
        }

        private static void UpdateText(CuiElementContainer container, string name, string text, string color, int size, bool bold, TextAnchor align)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Update = true,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = text ?? string.Empty,
                        FontSize = size,
                        Align = align,
                        Color = color,
                        Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf"
                    }
                }
            });
        }

        private static void UpdateImage(CuiElementContainer container, string name, string color)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Update = true,
                Components = { new CuiImageComponent { Color = color } }
            });
        }

        private static void Box(CuiElementContainer container, string name, string parent, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string material)
        {
            var image = new CuiImageComponent { Color = color };
            if (!string.IsNullOrEmpty(material)) image.Material = material;
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    image,
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        private static void Button(CuiElementContainer container, string parent, string name, string command, string color, float x, float y, float w, float h, bool normalized)
        {
            CuiRectTransformComponent rect = normalized
                ? new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                : new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Px(x, -(y + h)), OffsetMax = Px(x + w, -y) };
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    new CuiButtonComponent { Command = command, Color = color },
                    rect
                }
            });
        }

        private static void Label(CuiElementContainer container, string name, string parent, string text, string color, int size, TextAnchor align, bool bold, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = text ?? string.Empty,
                        FontSize = size,
                        Align = align,
                        Color = color,
                        Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf"
                    },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        private static string Hex(string hex, float alpha = -1f)
        {
            if (string.IsNullOrEmpty(hex)) return "1 1 1 1";
            hex = hex.Trim().TrimStart('#');
            if (hex.Length < 6) return "1 1 1 1";
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            float a = alpha >= 0f ? Mathf.Clamp01(alpha) : 1f;
            return (r / 255f).ToString("0.###", CultureInfo.InvariantCulture) + " "
                + (g / 255f).ToString("0.###", CultureInfo.InvariantCulture) + " "
                + (b / 255f).ToString("0.###", CultureInfo.InvariantCulture) + " "
                + a.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Px(float x, float y) => F(x) + " " + F(y);

        private static int Parse(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static double ToDouble(object value)
        {
            if (value is double) return (double)value;
            if (value is float) return (float)value;
            if (value is int) return (int)value;
            if (value is long) return (long)value;
            double parsed;
            return value != null && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0d;
        }

        private static string FormatDur(double seconds)
        {
            if (seconds < 0d) seconds = 0d;
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            if (span.TotalDays >= 1d) return (int)span.TotalDays + "d " + span.Hours + "h";
            if (span.TotalHours >= 1d) return (int)span.TotalHours + "h " + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
            if (span.TotalMinutes >= 1d) return (int)span.TotalMinutes + "m " + span.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
            return Math.Max(0, (int)span.TotalSeconds) + "s";
        }

        private static DayOfWeek ParseDay(string name)
        {
            DayOfWeek day;
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out day)) return day;
            return DayOfWeek.Thursday;
        }

        private static TimeZoneInfo ResolveZone(string id, out bool fallback)
        {
            fallback = false;
            if (!string.IsNullOrEmpty(id))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            string[] ids = { "America/New_York", "Eastern Standard Time", "US/Eastern" };
            foreach (string candidate in ids)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            fallback = true;
            try
            {
                TimeZoneInfo.TransitionTime start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
                TimeZoneInfo.TransitionTime end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
                TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), new DateTime(9999, 12, 31), TimeSpan.FromHours(1), start, end);
                return TimeZoneInfo.CreateCustomTimeZone("Nightshade-Eastern", TimeSpan.FromHours(-5), "Eastern Time", "EST", "EDT", new[] { rule });
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("Nightshade-Eastern-EST", TimeSpan.FromHours(-5), "Eastern Time", "EST");
            }
        }

        private static Settings Defaults()
        {
            return new Settings
            {
                ToggleCommands = new List<string> { "info", "start" },
                ShowOnFirstJoin = false,
                LogoUrl = DefaultLogo,
                ServerName = "Nightshade",
                ServerTag = "Solo/Duo",
                Pages = new List<Page>
                {
                    HomePage(),
                    RulesPage(),
                    DiscordPage(),
                    CommandsPage(),
                    SystemsPage(),
                    EventsPage(),
                    VipPage()
                }
            };
        }

        private static Page HomePage()
        {
            return new Page
            {
                Id = "home",
                Title = "Home",
                Layout = "home",
                Commands = new List<string> { "info", "start" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Home",
                        ShowStatus = true,
                        About = new List<Block>
                        {
                            Bk("WELCOME",
                                L("\u2022 Nightshade is a cozy-but-feral solo/duo server. Max team size is 2."),
                                L("\u2022 The week is PvE. PvP weekend is Friday 8:00 PM to Monday 3:00 AM Eastern."),
                                L("\u2022 Purge is Tuesday of wipe week at 2:00 PM Eastern until the Thursday wipe. Serverwide FAFO."),
                                L("\u2022 Monuments and events are always PvP."),
                                L("\u2022 Vanilla gathering on resources, components, and food."),
                                L("\u2022 10x+ stack sizes, with exceptions for balance."),
                                L("\u2022 Monthly wipe: first Thursday at 2:00 PM Eastern, unless Discord says otherwise."),
                                L("\u2022 Custom progression through /shop, Skill Tree, events, and VIP perks."),
                                L("\u2022 Event loot is public. Hacked crates, supply drops, and heli or Bradley crates are not locked."),
                                L("\u2022 Base boxes stay yours. Sleeping players stay protected.")),
                            Bk("WHERE TO START",
                                L("\u2022 Rules has the full list. Discord has the invite and a QR code."),
                                L("\u2022 Commands is next. /shop is there when you want to spend RP."))
                        }
                    }
                }
            };
        }

        private static Page RulesPage()
        {
            return new Page
            {
                Id = "rules",
                Title = "Rules",
                Commands = new List<string> { "rules", "rule" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Rules",
                        Columns = new List<Column>
                        {
                            Col(
                                Bk("TEAMS",
                                    L("\u2022 Solo/duo only. Max team size is 2.")),
                                Bk("BASES & BUILDING",
                                    L("\u2022 No raiding, damaging, griefing, laddering, walling in, trapping, or messing with player bases outside the PvP weekend and Purge.", "warn"),
                                    L("\u2022 PvP weekend is Friday 8:00 PM to Monday 3:00 AM Eastern."),
                                    L("\u2022 Purge is Tuesday of wipe week at 2:00 PM Eastern until the Thursday wipe."),
                                    L("\u2022 Do not block roads, monuments, caves, vendors, event areas, or another player's build.", "warn"),
                                    L("\u2022 Build for style, defense, roleplay, and deeply suspicious architecture. That part is encouraged.")),
                                Bk("LOOT & OWNERSHIP",
                                    L("\u2022 Do not take from another player's boxes, furnaces, or TC storage.", "warn"),
                                    L("\u2022 You, your teammate, TC auth, or someone you /share with can open them."),
                                    L("\u2022 Sleeping players cannot be looted, including during PvP weekend and Purge.", "warn"),
                                    L("\u2022 Event loot is open to anyone and is not locked."),
                                    L("\u2022 Hacked crates, supply drops, and heli or Bradley crates from events are public."),
                                    L("\u2022 If a base box says the loot is not yours, it is not yours.", "warn")),
                                Bk("PVP",
                                    L("\u2022 Monuments and events are always PvP.", "warn"),
                                    L("\u2022 Raiding and PvP are allowed during the PvP weekend and during Purge."),
                                    L("\u2022 Outside those, do not attack, bait, exploit rules, or play loophole lawyer.", "warn")),
                                Bk("COMMUNITY",
                                    L("\u2022 No hate speech, harassment, stalking, threats, doxxing, slurs, cheating, exploiting, or ban evading.", "warn"),
                                    L("\u2022 Trash talk can be spicy. Do not make it personal, creepy, or radioactive."),
                                    L("\u2022 Admins have final say on edge cases. If you have to say \"technically,\" step away from the crime scene.")))
                        }
                    }
                }
            };
        }

        private static Page DiscordPage()
        {
            return new Page
            {
                Id = "discord",
                Title = "Discord",
                Layout = "discord",
                Commands = new List<string> { "discord", "discordinfo" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Discord",
                        DiscordTitle = "JOIN THE SERVER",
                        DiscordInvite = "discord.gg/dFmeweQmsy",
                        DiscordQr = DefaultQr,
                        DiscordLines = new List<Line>
                        {
                            L("\u2022 Wipe changes, events, and support are posted in Discord."),
                            L("\u2022 Select the invite if you want to copy it, or scan the QR code."),
                            L("\u2022 Donate through the store link in Discord. After you donate, post in the donation channel with your in-game name."),
                            L("\u2022 VIP is permanent after one donation, unless you break the rules."),
                            L("\u2022 VIPx is a monthly subscription.", "muted")
                        }
                    }
                }
            };
        }

        private static Page CommandsPage()
        {
            return new Page
            {
                Id = "commands",
                Title = "Commands",
                Commands = new List<string> { "commands", "cmds", "command" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Commands",
                        Columns = new List<Column>
                        {
                            Col(
                                Bk("CORE",
                                    L("\u2022 /info \u2014 open this panel"),
                                    L("\u2022 /rules \u2014 the Rules tab"),
                                    L("\u2022 /discord \u2014 invite and QR code"),
                                    L("\u2022 /commands \u2014 this list"),
                                    L("\u2022 /systems \u2014 server systems"),
                                    L("\u2022 /events \u2014 events and help"),
                                    L("\u2022 /vip \u2014 VIP perks and donation help")),
                                Bk("HOMES",
                                    L("\u2022 /sethome <name> \u2014 save a home"),
                                    L("\u2022 /home <name> \u2014 teleport home"),
                                    L("\u2022 /removehome <name> \u2014 remove a home"),
                                    L("\u2022 Player-to-player teleport commands are not listed for next wipe.", "muted"))),
                            Col(
                                Bk("PROGRESSION",
                                    L("\u2022 /shop or /store \u2014 the RP shop"),
                                    L("\u2022 /skills, /skilltree, /st \u2014 Skill Tree"),
                                    L("\u2022 /perks \u2014 VIP kit status"),
                                    L("\u2022 /perks daily and /perks 4day \u2014 claim VIP kits")),
                                Bk("SKINS, LOOT, HELP",
                                    L("\u2022 /skin, /skincraft, /skinitem, /skininv, /skinauto, /skinbase"),
                                    L("\u2022 /skinrequest <WorkshopID> \u2014 request a Workshop skin"),
                                    L("\u2022 /loot \u2014 who owns the container you are looking at"),
                                    L("\u2022 /share, /share list, /share remove, /share clear"),
                                    L("\u2022 /hordehelp \u2014 Hordes help"),
                                    L("\u2022 /checkfoundations \u2014 Defendable Homes foundations"),
                                    L("\u2022 /raidbuilderinfo \u2014 vetted raidbase builder help"),
                                    L("\u2022 /tug or /tugboat \u2014 VIPx tugboat license")))
                        }
                    }
                }
            };
        }

        private static Page SystemsPage()
        {
            return new Page
            {
                Id = "systems",
                Title = "Systems",
                Commands = new List<string> { "systems", "sys" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Systems",
                        Columns = new List<Column>
                        {
                            Col(
                                Bk("ECONOMY",
                                    L("\u2022 /shop opens the RP shop. /store does the same."),
                                    L("\u2022 RP is the player currency. It is ServerRewards points."),
                                    L("\u2022 Activity rewards pay RP. Economics is turned off."),
                                    L("\u2022 The Admin Shop tab trades items for Blood. It is not a second cash shop."),
                                    L("\u2022 There is no separate Black Market currency."),
                                    L("\u2022 Your RP on Home comes from ServerRewards. If it does not answer, it shows -.")),
                                Bk("LOOT PROTECTION",
                                    L("\u2022 Boxes, furnaces, and TC storage: you, your teammate, or TC auth. /share opens one container for someone else."),
                                    L("\u2022 Sleeping players stay unlootable all week, including PvP weekend and Purge. Teammates can still loot them."),
                                    L("\u2022 Event loot is open to anyone and is not locked."),
                                    L("\u2022 Hacked crates, supply drops, and heli or Bradley crates from events are public."),
                                    L("\u2022 Purge does not turn base storage or sleeper protection off. A PvP monument does not either."),
                                    L("\u2022 Domes only show over events, not monuments.", "muted"))),
                            Col(
                                Bk("PROGRESSION",
                                    L("\u2022 Skill Tree unlocks long-term bonuses. /skills, /skilltree, or /st.")),
                                Bk("VEHICLES",
                                    L("\u2022 Car chassis are buyable with RP from /shop."),
                                    L("\u2022 Build a purchased chassis at the car lift."),
                                    L("\u2022 4-module chassis are in the VIP shop."),
                                    L("\u2022 /tug or /tugboat is the tugboat license."),
                                    L("\u2022 ChestStacks: 3 stacks for non-VIP, 5 stacks for VIP.")))
                        }
                    }
                }
            };
        }

        private static Page EventsPage()
        {
            return new Page
            {
                Id = "events",
                Title = "Events",
                Commands = new List<string> { "events", "eventinfo", "help" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Overview",
                        Commands = new List<string> { "events", "eventinfo", "help" },
                        Columns = new List<Column>
                        {
                            Col(
                                Bk("REGULAR EVENTS",
                                    L("\u2022 Hide and Seek runs on scheduled event days when announced."),
                                    L("\u2022 Purge is Tuesday of wipe week at 2:00 PM Eastern until the Thursday wipe. PvP, raiding, and base destruction are allowed serverwide."),
                                    L("\u2022 Monuments and events are always PvP.", "warn"),
                                    L("\u2022 The PvP weekend is Friday 8:00 PM to Monday 3:00 AM Eastern.")),
                                Bk("SIGNALS",
                                    L("\u2022 Heli and Bradley signals may have tiers, cooldowns, rewards, and team recommendations."),
                                    L("\u2022 Read the signal card before you call one. Solo/duo is the server cap."))),
                            Col(
                                Bk("RAID CONTENT",
                                    L("\u2022 Bradley and Heli signals are player-triggered combat events."),
                                    L("\u2022 Raidable Bases are event bases, not normal player bases."),
                                    L("\u2022 Read the difficulty before you start one.")),
                                Bk("HELP PAGES",
                                    L("\u2022 /hordehelp \u2014 Hordes rules"),
                                    L("\u2022 /raidbuilderinfo \u2014 vetted raidbase builder commands"),
                                    L("\u2022 /rbinfo \u2014 raidable bases"),
                                    L("\u2022 /pvpinfo \u2014 where PvP is allowed")))
                        }
                    },
                    new Tab
                    {
                        Title = "Hordes",
                        Commands = new List<string> { "hordehelp", "hordeshelp" },
                        Columns = new List<Column>
                        {
                            Col(Bk("HORDES",
                                L("\u2022 Hordes are Defendable Homes style wave events. Build smart, check your foundations, then try not to become part of the furniture."),
                                L("\u2022 Your Defendable Home must be built on normal terrain using owned square or triangle foundations."),
                                L("\u2022 Foundations must belong to you or your team. Max team size is 2."),
                                L("\u2022 You must be inside the Tool Cupboard area of the house when checking."),
                                L("\u2022 Invalid areas include monuments, cliffs, mountains, clutter or decor terrain, and bad topology."),
                                L("\u2022 Do not build too close to another active Defendable Homes event."),
                                L("\u2022 Use /checkfoundations to check all foundations for the event."),
                                L("\u2022 \u2298 You are not the foundation owner.", "muted"),
                                L("\u2022 \u229D Foundation height or depth does not match.", "muted"),
                                L("\u2022 \u229B Terrain topology is invalid. Prohibited: Cliff, Cliffside, Decor, Building, Monument, Clutter, Mountain.", "muted"),
                                L("\u2022 \u25C9 Foundation is within 40m of a custom monument.", "muted"),
                                L("\u2022 Bring ammo, meds, armor, and room in your inventory."),
                                L("\u2022 Do not drag hordes into player bases, vendors, safe builds, or random bystanders."),
                                L("\u2022 Hordes are events, so they are PvP. The rest of the map follows the weekend window and Purge."),
                                L("\u2022 Event loot is public. Base storage and sleepers stay protected.")))
                        }
                    },
                    new Tab
                    {
                        Title = "Raidable Bases",
                        Commands = new List<string> { "rbinfo", "raidablebasesinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("RAIDABLE BASES",
                                L("\u2022 Raidable Bases are event bases. They are not player bases."),
                                L("\u2022 Player bases stay protected outside the PvP weekend and Purge."),
                                L("\u2022 Easy: available right away for everyone."),
                                L("\u2022 Medium: 24h after wipe. VIP: 12h."),
                                L("\u2022 Hard: 72h after wipe. VIP: 48h."),
                                L("\u2022 Expert: 120h after wipe. VIP: 96h."),
                                L("\u2022 Nightmare: 168h after wipe. VIP: 144h."),
                                L("\u2022 Do not interfere with another player's PvE raid unless the event rules allow it."),
                                L("\u2022 PvP raid bases, the weekend window, and Purge are different. Read the card."),
                                L("\u2022 If you started it, finish it.")))
                        }
                    },
                    new Tab
                    {
                        Title = "Builder",
                        Commands = new List<string> { "raidbuilderinfo", "raidbuildinfo", "raidguide" },
                        Columns = new List<Column>
                        {
                            Col(Bk("RAIDBASE BUILDER",
                                L("\u2022 Raidbase builder access is admin-only after vetting. This is not a public toy box.", "warn"),
                                L("\u2022 For players approved to help build or test raidbase content."),
                                L("\u2022 Permissions are assigned by an admin only after vetting."),
                                L("\u2022 Do not share commands, bases, or access with unapproved players."),
                                L("\u2022 /raidbasestart \u2014 start a player raidbase build session"),
                                L("\u2022 /raidreadypve \u2014 mark the saved raidbase ready for PvE"),
                                L("\u2022 /raidreadypvp \u2014 mark the saved raidbase ready for PvP"),
                                L("\u2022 /raidbaseend \u2014 end the active raidbase session"),
                                L("\u2022 Build clean, playable bases with accessible paths and fair loot flow."),
                                L("\u2022 Avoid traps that softlock players, exploit terrain, or create impossible raid paths."),
                                L("\u2022 Ask an admin before making major format changes.")))
                        }
                    },
                    new Tab
                    {
                        Title = "PvP Zones",
                        Commands = new List<string> { "pvpinfo", "pvpzonesinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("WHERE PVP IS ON",
                                L("\u2022 Monuments are always PvP.", "warn"),
                                L("\u2022 Events are always PvP. A dome only appears over an event.", "warn"),
                                L("\u2022 PvP weekend: Friday 8:00 PM to Monday 3:00 AM Eastern. Raiding is allowed."),
                                L("\u2022 Purge: Tuesday of wipe week at 2:00 PM Eastern until the Thursday wipe. Raiding is allowed."),
                                L("\u2022 Outside those, do not attack players or raid bases."),
                                L("\u2022 Home shows which of PVE, PVP, or PURGE is on right now.")))
                        }
                    }
                }
            };
        }

        private static Page VipPage()
        {
            return new Page
            {
                Id = "vip",
                Title = "VIP",
                Commands = new List<string> { "vip", "vipinfo" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Perks",
                        Commands = new List<string> { "vip", "vipinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("VIP",
                                L("\u2022 Permanent \u2014 one donation", "accent"),
                                L("\u2022 Kept unless you break the rules."),
                                L("\u2022 4 /home slots instead of 3."),
                                L("\u2022 Reduced /home cooldown: 5 seconds, down from 10."),
                                L("\u2022 Home teleport has no RP cost."),
                                L("\u2022 Can set a home on your tugboat."),
                                L("\u2022 Queue skip and a 5-minute reconnect hold."),
                                L("\u2022 1.1\u00D7 RP activity multiplier."),
                                L("\u2022 HeliSignals reward multiplier: 1.25\u00D7."),
                                L("\u2022 Bradley signal cooldown: 50 min."),
                                L("\u2022 ChestStacks: 5 stacks instead of 3."),
                                L("\u2022 Hackable crates unlock in 2.5 minutes."),
                                L("\u2022 No decay on personal boats and tugboats."),
                                L("\u2022 4-module car chassis access in the RP shop."),
                                L("\u2022 Claimable tool kits in the VIP shop. /perks daily and /perks 4day."),
                                L("\u2022 Medium raids: 12h after wipe (default 24h)."),
                                L("\u2022 Hard raids: 48h after wipe (default 72h)."),
                                L("\u2022 Expert raids: 96h after wipe (default 120h)."),
                                L("\u2022 Nightmare raids: 144h after wipe (default 168h)."))),
                            Col(Bk("VIPx",
                                L("\u2022 Monthly", "accent"),
                                L("\u2022 A monthly subscription."),
                                L("\u2022 5 /home slots instead of 3."),
                                L("\u2022 No /home cooldown."),
                                L("\u2022 Home teleport has no RP cost."),
                                L("\u2022 Can set a home on your tugboat."),
                                L("\u2022 Can use /tug. The license is free."),
                                L("\u2022 Queue skip and a 5-minute reconnect hold."),
                                L("\u2022 1.1\u00D7 RP activity multiplier."),
                                L("\u2022 HeliSignals reward multiplier: 1.5\u00D7."),
                                L("\u2022 Bradley signal cooldown: 30 min."),
                                L("\u2022 ChestStacks: 5 stacks instead of 3."),
                                L("\u2022 Hackable crates unlock in 60 seconds."),
                                L("\u2022 No decay on personal boats and tugboats."),
                                L("\u2022 4-module car chassis access in the RP shop."),
                                L("\u2022 VIPx shop tools, plus /perks daily and /perks 4day."),
                                L("\u2022 Same early raids as VIP: Medium 12h, Hard 48h, Expert 96h, Nightmare 144h.")))
                        }
                    },
                    new Tab
                    {
                        Title = "Donate",
                        Commands = new List<string> { "viphelp", "donationinfo", "donateinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("HOW TO GET VIP",
                                L("\u2022 VIP is permanent after one donation, unless you break the rules.", "accent"),
                                L("\u2022 VIPx is monthly.", "accent"),
                                L("\u2022 It is donation-supported, and it is not fully automatic yet."),
                                L("\u2022 Donate through the server store link in Discord."),
                                L("\u2022 After donating, post in the donation channel."),
                                L("\u2022 Include your in-game name so an admin can apply the right group."),
                                L("\u2022 Discord: discord.gg/dFmeweQmsy"),
                                L("\u2022 /vip opens this page."),
                                L("\u2022 VIP is convenience. Core content stays playable for everyone.", "muted")))
                        }
                    }
                }
            };
        }

        private static Block Bk(string heading, params Line[] lines)
        {
            return new Block { Heading = heading, Lines = new List<Line>(lines) };
        }

        private static Column Col(params Block[] blocks)
        {
            return new Column { Blocks = new List<Block>(blocks) };
        }

        private static Line L(string text, string tone = "body")
        {
            return new Line { Text = text, Tone = tone };
        }
    }
}
