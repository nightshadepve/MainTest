using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("NightshadeInfo", "Nightshade", "1.0.0")]
    [Description("Nightshade info panel: live status, rules, and Discord on the home page.")]
    public class NightshadeInfo : RustPlugin
    {
        private const string Root = "NightshadeInfo";
        private const string DataPath = "NightshadeInfo/Players";
        private const string UiCommand = "nightshadeinfo.ui";
        private const string DefaultLogo = "https://raw.githubusercontent.com/nightshadepve/Nightshadeserver/refs/heads/main/260625_Logo_75_nobg.png";

#pragma warning disable 0649
        [PluginReference] private Plugin NightshadeRaidProtection;
        [PluginReference("Nightshade Purge Status")] private Plugin PurgeStatus;
        [PluginReference] private Plugin ServerRewards;
#pragma warning restore 0649

        private Settings settings;
        private Stored stored;
        private TimeZoneInfo zone;
        private bool zoneFallback;
        private Timer clock;
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
            [JsonProperty("Panel width")] public float PanelWidth = 1080f;
            [JsonProperty("Panel height")] public float PanelHeight = 680f;
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
            [JsonProperty("Label")] public string Label = "First Thursday · 2:00 PM Eastern";
        }

        private class Labels
        {
            [JsonProperty("Mode title")] public string ModeTitle = "MODE";
            [JsonProperty("PvE name")] public string PveName = "PVE";
            [JsonProperty("PvP name")] public string PvpName = "PVP";
            [JsonProperty("PvP window")] public string Window = "Friday 8:00 PM – Monday 3:00 AM Eastern";
            [JsonProperty("PvP ends")] public string PvpEnds = "Ends in {time}";
            [JsonProperty("PvP starts")] public string PvpStarts = "Starts in {time}";
            [JsonProperty("Mode unavailable")] public string ModeUnavailable = "Weekend window unavailable";
            [JsonProperty("Purge title")] public string PurgeTitle = "PURGE";
            [JsonProperty("Purge inactive")] public string PurgeInactive = "Not active";
            [JsonProperty("Purge active")] public string PurgeActive = "ACTIVE";
            [JsonProperty("Purge ends")] public string PurgeEnds = "Ends in {time}";
            [JsonProperty("Purge unavailable")] public string PurgeUnavailable = "Purge status unavailable";
            [JsonProperty("Wipe title")] public string WipeTitle = "NEXT WIPE";
            [JsonProperty("Wipe in")] public string WipeIn = "In {time}";
            [JsonProperty("Players title")] public string PlayersTitle = "PLAYERS";
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
            public bool ModeOk;
            public bool Pvp;
            public string ModeValue;
            public string ModeDetail;
            public string ModeColor;
            public bool PurgeOk;
            public bool Purge;
            public string PurgeValue;
            public string PurgeDetail;
            public string WipeValue;
            public string WipeDetail;
            public string Online;
            public string Sleepers;
            public string Points;
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

            float width = Mathf.Clamp(settings.PanelWidth, 760f, 1400f);
            float height = Mathf.Clamp(settings.PanelHeight, 480f, 900f);
            Button(container, Root, Root + ".Dim", UiCommand + " close", Hex(settings.DimColor, settings.DimOpacity / 100f), 0f, 0f, 1f, 1f, true);
            string window = Root + ".Window";
            Box(container, window, Root, Hex(settings.PanelColor, settings.PanelOpacity / 100f),
                "0.5 0.5", "0.5 0.5", Px(-width / 2f, -height / 2f), Px(width / 2f, height / 2f),
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
            const float header = 68f;
            Box(container, window + ".Header", window, Hex("#120E16", 0.55f), "0 1", "1 1", "0 -" + F(header), "0 0", null);
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
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "16 -23", OffsetMax = "62 23" }
                }
            });
            Label(container, window + ".Name", window + ".Header", settings.ServerName, Hex("#F4F1EA"), 22, TextAnchor.MiddleLeft, true,
                "0 0.5", "0 0.5", "74 -2", "360 24");
            float tagW = Mathf.Clamp(settings.ServerTag.Length * 8.2f + 18f, 72f, 160f);
            Box(container, window + ".Tag", window + ".Header", Hex(settings.Accent, 0.22f), "0 0.5", "0 0.5", "74 -24", F(74f + tagW) + " -4", null);
            Label(container, window + ".TagText", window + ".Tag", settings.ServerTag, Hex(settings.Accent), 12, TextAnchor.MiddleCenter, true,
                "0 0", "1 1", "0 0", "0 0");
            Button(container, window + ".Header", window + ".Close", UiCommand + " close", Hex("#2A2033", 0.9f), width - 48f, 18f, 32f, 32f, false);
            Label(container, window + ".CloseX", window + ".Close", "✕", Hex("#F4F1EA"), 16, TextAnchor.MiddleCenter, true, "0 0", "1 1", "0 0", "0 0");
        }

        private void DrawSidebar(CuiElementContainer container, string window, float height, int selected)
        {
            const float side = 196f;
            const float header = 68f;
            Box(container, window + ".Side", window, Hex("#08070B", 0.45f), "0 0", "0 1", "0 0", F(side) + " -" + F(header), null);
            float y = 14f;
            for (int i = 0; i < settings.Pages.Count; i++)
            {
                Page page = settings.Pages[i];
                if (page == null || !page.Enabled) continue;
                bool on = i == selected;
                string color = on ? Hex(settings.Accent, 0.28f) : Hex("#FFFFFF", 0.04f);
                Button(container, window + ".Side", window + ".Nav." + i, UiCommand + " page " + i, color, 12f, y, side - 24f, 36f, false);
                if (on) Box(container, window + ".NavBar." + i, window + ".Nav." + i, Hex(settings.Accent), "0 0", "0 1", "0 4", "3 -4", null);
                Label(container, window + ".NavText." + i, window + ".Nav." + i, page.Title ?? page.Id, on ? Hex("#F4F1EA") : Hex(settings.MutedColor),
                    13, TextAnchor.MiddleLeft, true, "0 0", "1 1", "14 0", "-8 0");
                y += 42f;
                if (y > height - header - 20f) break;
            }
        }

        private void DrawContent(CuiElementContainer container, string window, float width, float height, Page page, int pageIndex, int tabIndex, BasePlayer player)
        {
            const float side = 196f;
            const float header = 68f;
            float contentW = width - side;
            float contentH = height - header;
            string content = window + ".Content";
            Box(container, content, window, "0 0 0 0", "0 0", "1 1", F(side) + " 0", "0 -" + F(header), null);

            Tab tab = page.Tabs != null && page.Tabs.Count > 0 ? page.Tabs[tabIndex] : null;
            float top = 0f;
            if (page.Layout != "home" && page.Tabs != null && page.Tabs.Count > 1)
            {
                float x = 16f;
                for (int i = 0; i < page.Tabs.Count; i++)
                {
                    string title = page.Tabs[i].Title ?? ("Page " + (i + 1));
                    float w = Mathf.Clamp(title.Length * 7.4f + 28f, 92f, 210f);
                    bool on = i == tabIndex;
                    Button(container, content, content + ".Tab." + i, UiCommand + " tab " + pageIndex + " " + i,
                        on ? Hex(settings.Accent, 0.34f) : Hex("#FFFFFF", 0.05f), x, 12f, w, 28f, false);
                    Label(container, content + ".TabText." + i, content + ".Tab." + i, title, on ? Hex("#F4F1EA") : Hex(settings.MutedColor),
                        12, TextAnchor.MiddleCenter, true, "0 0", "1 1", "0 0", "0 0");
                    x += w + 8f;
                }
                top = 48f;
            }

            float viewH = contentH - top - 12f;
            float innerW = contentW - 28f;
            var body = new CuiElementContainer();
            float y = 8f;
            string scroll = content + ".Scroll";
            if (page.Layout == "home") DrawHome(body, scroll, tab, player, innerW, ref y);
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
                        OffsetMax = "-10 -" + F(top + 4f)
                    }
                }
            });
            foreach (CuiElement element in body) container.Add(element);
        }

        private void DrawHome(CuiElementContainer container, string parent, Tab tab, BasePlayer player, float width, ref float y)
        {
            if (tab == null) tab = new Tab();
            if (tab.ShowStatus) DrawStatus(container, parent, player, width, ref y);
            if (tab.About != null)
            {
                foreach (Block block in tab.About) DrawBlock(container, parent, block, 0f, width, ref y);
            }
            float top = y + 6f;
            float leftW = (width - 16f) * 0.62f;
            float rightX = leftW + 16f;
            float rightW = width - rightX;
            float left = top;
            float right = top;
            if (!string.IsNullOrEmpty(tab.RulesTitle))
                DrawHeading(container, parent, tab.RulesTitle, 0f, leftW, ref left);
            if (tab.Rules != null)
            {
                foreach (Block block in tab.Rules) DrawBlock(container, parent, block, 0f, leftW, ref left);
            }
            DrawDiscord(container, parent, tab, rightX, rightW, ref right);
            y = Mathf.Max(left, right);
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

        private void DrawDiscord(CuiElementContainer container, string parent, Tab tab, float x, float width, ref float y)
        {
            string title = string.IsNullOrEmpty(tab.DiscordTitle) ? "DISCORD" : tab.DiscordTitle;
            DrawHeading(container, parent, title, x, width, ref y);
            if (tab.DiscordLines != null)
            {
                foreach (Line line in tab.DiscordLines)
                {
                    var block = new Block { Lines = new List<Line> { line } };
                    DrawBlock(container, parent, block, x, width, ref y);
                }
            }
            if (string.IsNullOrEmpty(tab.DiscordInvite)) return;
            y += 4f;
            Box(container, parent + ".InviteBg", parent, Hex("#FFFFFF", 0.06f), "0 1", "0 1", Px(x, -(y + 32f)), Px(x + width, -y), null);
            container.Add(new CuiElement
            {
                Name = parent + ".Invite",
                Parent = parent + ".InviteBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = tab.DiscordInvite,
                        FontSize = 14,
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
            y += 40f;
        }

        private void DrawStatus(CuiElementContainer container, string parent, BasePlayer player, float width, ref float y)
        {
            Snap snap = ReadStatus(player);
            float gap = 8f;
            float cardW = (width - gap * 3f) / 4f;
            float cardH = 124f;
            DrawCard(container, parent, "Mode", 0f, y, cardW, cardH, snap.ModeColor, settings.Labels.ModeTitle, snap.ModeValue, snap.ModeDetail, snap.ModeColor);
            DrawCard(container, parent, "Purge", cardW + gap, y, cardW, cardH, snap.Purge ? settings.PvpColor : settings.MutedColor, settings.Labels.PurgeTitle, snap.PurgeValue, snap.PurgeDetail, snap.Purge ? settings.PvpColor : settings.TextColor);
            DrawCard(container, parent, "Wipe", (cardW + gap) * 2f, y, cardW, cardH, settings.Accent, settings.Labels.WipeTitle, snap.WipeValue, snap.WipeDetail, settings.Accent);
            DrawCard(container, parent, "Pop", (cardW + gap) * 3f, y, cardW, cardH, settings.Accent, settings.Labels.PlayersTitle, snap.Online, snap.Sleepers + "\n" + snap.Points, settings.TextColor);
            y += cardH + 14f;
        }

        private void DrawCard(CuiElementContainer container, string parent, string key, float x, float y, float w, float h, string bar, string title, string value, string detail, string valueColor)
        {
            string name = "NightshadeInfo.St." + key;
            Box(container, name, parent, Hex("#FFFFFF", 0.045f), "0 1", "0 1", Px(x, -(y + h)), Px(x + w, -y), null);
            Box(container, name + ".Bar", name, Hex(bar), "0 0", "0 1", "0 8", "3 -8", null);
            Label(container, name + ".K", name, title ?? string.Empty, Hex(settings.MutedColor), 10, TextAnchor.UpperLeft, true, "0 1", "1 1", "12 -22", "-8 -6");
            Label(container, name + ".V", name, value ?? string.Empty, Hex(valueColor), 16, TextAnchor.UpperLeft, true, "0 1", "1 1", "12 -46", "-8 -22");
            Label(container, name + ".D", name, detail ?? string.Empty, Hex(settings.TextColor), 11, TextAnchor.UpperLeft, false, "0 1", "1 1", "12 -112", "-8 -48");
        }

        private void DrawHeading(CuiElementContainer container, string parent, string text, float x, float width, ref float y)
        {
            Label(container, parent + ".H." + F(x) + "." + F(y), parent, text, Hex(settings.Accent), 15, TextAnchor.MiddleLeft, true,
                "0 1", "0 1", Px(x, -(y + 22f)), Px(x + width, -y));
            y += 26f;
        }

        private void DrawBlock(CuiElementContainer container, string parent, Block block, float x, float width, ref float y)
        {
            if (block == null) return;
            if (!string.IsNullOrEmpty(block.Heading))
            {
                Label(container, parent + ".B." + F(x) + "." + F(y), parent, block.Heading, Hex(settings.Accent), 13, TextAnchor.UpperLeft, true,
                    "0 1", "0 1", Px(x, -(y + 18f)), Px(x + width, -y));
                y += 20f;
            }
            if (block.Lines == null) return;
            foreach (Line line in block.Lines)
            {
                if (line == null || string.IsNullOrEmpty(line.Text))
                {
                    y += 8f;
                    continue;
                }
                int rows = Rows(Visible(line.Text), width, 12);
                float h = rows * 16f;
                Label(container, parent + ".L." + F(x) + "." + F(y), parent, line.Text, Tone(line), 12, TextAnchor.UpperLeft, false,
                    "0 1", "0 1", Px(x, -(y + h)), Px(x + width, -y));
                y += h + 2f;
            }
            y += 8f;
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
            UpdateText(container, "NightshadeInfo.St.Mode.V", snap.ModeValue, Hex(snap.ModeColor), 16, true);
            UpdateText(container, "NightshadeInfo.St.Mode.D", snap.ModeDetail, Hex(settings.TextColor), 11, false);
            UpdateImage(container, "NightshadeInfo.St.Mode.Bar", Hex(snap.ModeColor));
            UpdateText(container, "NightshadeInfo.St.Purge.V", snap.PurgeValue, Hex(snap.Purge ? settings.PvpColor : settings.TextColor), 16, true);
            UpdateText(container, "NightshadeInfo.St.Purge.D", snap.PurgeDetail, Hex(settings.TextColor), 11, false);
            UpdateImage(container, "NightshadeInfo.St.Purge.Bar", Hex(snap.Purge ? settings.PvpColor : settings.MutedColor));
            UpdateText(container, "NightshadeInfo.St.Wipe.V", snap.WipeValue, Hex(settings.Accent), 16, true);
            UpdateText(container, "NightshadeInfo.St.Wipe.D", snap.WipeDetail, Hex(settings.TextColor), 11, false);
            UpdateText(container, "NightshadeInfo.St.Pop.V", snap.Online, Hex(settings.TextColor), 16, true);
            UpdateText(container, "NightshadeInfo.St.Pop.D", snap.Sleepers + "\n" + snap.Points, Hex(settings.TextColor), 11, false);
            CuiHelper.AddUi(player, container);
        }

        private Snap ReadStatus(BasePlayer player)
        {
            var snap = new Snap();
            bool active = false;
            double seconds = 0d;
            bool modeOk = NightshadeRaidProtection != null && NightshadeRaidProtection.IsLoaded;
            if (modeOk)
            {
                object flag = NightshadeRaidProtection.Call("API_IsPvPActive");
                object left = NightshadeRaidProtection.Call("API_SecondsUntilPvPChange");
                if (flag is bool && left != null)
                {
                    active = (bool)flag;
                    seconds = ToDouble(left);
                    snap.ModeOk = true;
                }
            }
            snap.Pvp = snap.ModeOk && active;
            snap.ModeColor = snap.Pvp ? settings.PvpColor : settings.PveColor;
            if (!snap.ModeOk)
            {
                snap.ModeValue = "—";
                snap.ModeDetail = settings.Labels.ModeUnavailable;
                snap.ModeColor = settings.MutedColor;
            }
            else
            {
                snap.ModeValue = snap.Pvp ? settings.Labels.PvpName : settings.Labels.PveName;
                string when = (snap.Pvp ? settings.Labels.PvpEnds : settings.Labels.PvpStarts).Replace("{time}", FormatDur(seconds));
                snap.ModeDetail = settings.Labels.Window + "\n" + when;
            }

            bool purgeOk = PurgeStatus != null && PurgeStatus.IsLoaded;
            bool purge = false;
            double end = 0d;
            if (purgeOk)
            {
                object flag = PurgeStatus.Call("API_IsPurgeActive");
                if (flag is bool)
                {
                    purge = (bool)flag;
                    snap.PurgeOk = true;
                    object stamp = PurgeStatus.Call("API_GetPurgeEndTimestamp");
                    if (stamp != null) end = ToDouble(stamp);
                }
            }
            snap.Purge = snap.PurgeOk && purge;
            if (!snap.PurgeOk)
            {
                snap.PurgeValue = "—";
                snap.PurgeDetail = settings.Labels.PurgeUnavailable;
            }
            else if (!snap.Purge)
            {
                snap.PurgeValue = settings.Labels.PurgeInactive;
                snap.PurgeDetail = "Last days of wipe, when an admin starts it";
            }
            else
            {
                snap.PurgeValue = settings.Labels.PurgeActive;
                double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                snap.PurgeDetail = end > now ? settings.Labels.PurgeEnds.Replace("{time}", FormatDur(end - now)) : settings.Labels.PurgeActive;
            }

            DateTime next = NextWipeUtc();
            snap.WipeValue = (settings.Labels.WipeIn ?? "In {time}").Replace("{time}", FormatDur((next - DateTime.UtcNow).TotalSeconds));
            snap.WipeDetail = settings.Wipe == null || string.IsNullOrEmpty(settings.Wipe.Label) ? "First Thursday · 2:00 PM Eastern" : settings.Wipe.Label;

            int online = 0;
            foreach (BasePlayer other in BasePlayer.activePlayerList)
            {
                if (other == null) continue;
                if (!settings.CountAdmins && other.IsAdmin) continue;
                online++;
            }
            int sleepers = BasePlayer.sleepingPlayerList == null ? 0 : BasePlayer.sleepingPlayerList.Count;
            snap.Online = settings.Labels.Online.Replace("{online}", online.ToString(CultureInfo.InvariantCulture));
            snap.Sleepers = settings.Labels.Sleepers.Replace("{sleepers}", sleepers.ToString(CultureInfo.InvariantCulture));
            snap.Points = settings.Labels.RewardPoints.Replace("{amount}", RewardPoints(player));
            return snap;
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

        private static void Normalize(Settings value)
        {
            if (value.ToggleCommands == null) value.ToggleCommands = new List<string>();
            if (value.Pages == null) value.Pages = new List<Page>();
            if (value.Wipe == null) value.Wipe = new WipeCfg();
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

        private static void UpdateText(CuiElementContainer container, string name, string text, string color, int size, bool bold)
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
                        Align = TextAnchor.UpperLeft,
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
                Commands = new List<string> { "info", "start", "rules", "rule", "discord", "discordinfo" },
                Tabs = new List<Tab>
                {
                    new Tab
                    {
                        Title = "Home",
                        ShowStatus = true,
                        About = new List<Block>
                        {
                            Bk("WELCOME TO NIGHTSHADE",
                                L("Nightshade is a cozy-but-feral hybrid PvPvE Rust server: PvE outside protected PvP areas, with monuments and event zones ready to bite."),
                                L("Vanilla gathering on resources, components, and food."),
                                L("10x+ stack sizes, with exceptions for balance."),
                                L("Monthly wipe: first Thursday at 2:00 PM Eastern, unless Discord says otherwise."),
                                L("Monuments are always PvP. Watch the zone warnings before wandering in.", "warn"),
                                L("Progression runs through /shop, Skill Tree, quarries, events, and VIP perks."),
                                L("Loot protection covers earned kills, crates, drops, and event rewards."),
                                L("Active admins. Read the rules on this page, then Commands, then /shop when you want to spend RP."))
                        },
                        RulesTitle = "SERVER RULES",
                        Rules = new List<Block>
                        {
                            Bk("BASES & BUILDING",
                                L("No raiding, damaging, griefing, laddering, walling in, trapping, or messing with player bases outside Purge or approved raid events.", "warn"),
                                L("Do not block roads, monuments, caves, vendors, event areas, or another player's build.", "warn"),
                                L("Build for style, defense, roleplay, and deeply suspicious architecture. That part is encouraged.")),
                            Bk("LOOT & OWNERSHIP",
                                L("Do not steal from player containers, bodies, drops, event crates, or protected loot.", "warn"),
                                L("If Loot Protection says it is not yours, it is not yours. This is not a riddle.", "warn"),
                                L("Team up or ask for share access when you want to split loot.")),
                            Bk("PVP",
                                L("PvP is allowed inside marked PvP monuments, PvP domes, PvP-enabled events, during the weekend window, and during Purge."),
                                L("The weekend window is Friday 8:00 PM through Monday 3:00 AM Eastern. The status cards above are the live reading."),
                                L("Outside those areas, do not attack, bait, exploit rules, or play loophole lawyer.", "warn"),
                                L("Watch entry and leave messages. If a dome looks spicy, assume it bites.")),
                            Bk("COMMUNITY",
                                L("No hate speech, harassment, stalking, threats, doxxing, slurs, cheating, exploiting, or ban evading.", "warn"),
                                L("Trash talk can be spicy. Do not make it personal, creepy, or radioactive."),
                                L("Admins have final say on edge cases. If you have to say \"technically,\" step away from the crime scene."))
                        },
                        DiscordTitle = "DISCORD",
                        DiscordInvite = "discord.gg/dFmeweQmsy",
                        DiscordLines = new List<Line>
                        {
                            L("Wipe changes, events, and support are posted in Discord."),
                            L("Select the invite below if you want to copy it."),
                            L("Donations go through the store link in Discord. After you donate, post in the donation channel with your in-game name so an admin can apply VIP."),
                            L("VIP lasts until map wipe unless Discord says otherwise. VIPx follows the vipx group.", "muted")
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
                                    L("/info — open this panel"),
                                    L("/rules — rules on the home page"),
                                    L("/commands — this list"),
                                    L("/systems — economy, loot, vehicles"),
                                    L("/events — events and help"),
                                    L("/vip — VIP perks and donation help")),
                                Bk("HOMES",
                                    L("/sethome <name> — save a home"),
                                    L("/home <name> — teleport home"),
                                    L("/removehome <name> — remove a home"),
                                    L("Player-to-player teleport commands are not listed for next wipe.", "muted"))),
                            Col(
                                Bk("PROGRESSION & QOL",
                                    L("/shop or /store — Nightshade Market, priced in RP"),
                                    L("/vq — virtual quarries"),
                                    L("/skills, /skilltree, /st — Skill Tree"),
                                    L("/limits — entity limits"),
                                    L("/perks — VIP kit status"),
                                    L("/perks daily and /perks 4day — claim VIP kits")),
                                Bk("SKINS / LOOT / HELP",
                                    L("/skin, /skincraft, /skinitem, /skininv, /skinauto, /skinbase"),
                                    L("/skinrequest <WorkshopID> — request a Workshop skin"),
                                    L("/loot, /share, /unshare, /sharelist, /shareclear"),
                                    L("/vehicleinfo — vehicle rules"),
                                    L("/hordehelp — Hordes help"),
                                    L("/checkfoundations — Defendable Homes foundations"),
                                    L("/raidbuilderinfo — vetted raidbase builder help"),
                                    L("/tug or /tugboat — VIPx vehicle license")))
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
                                    L("/shop and /store open the Nightshade Market. Listings are priced in ServerRewards RP."),
                                    L("RP is the player currency. Activity rewards deposit RP. Economics rewards are off, and the ServerRewards exchange tab is off."),
                                    L("The Admin Shop tab is item barter: fish and components trade for Blood, and the Blood Market spends Blood. It is not a second cash currency."),
                                    L("There is no separate Black Market currency. The shop card named Black Market: MLRS Truck is an RP purchase."),
                                    L("Shop stock is whatever is saved in the Nightshade Market. It does not rotate on its own."),
                                    L("The balance on Home is ServerRewards CheckPoints. If that call does not answer, it shows -.")),
                                Bk("LOOT PROTECTION",
                                    L("Kills, crates, drops, and event rewards may lock to the player or team that earned them."),
                                    L("Use /share when you want another player to access protected loot."),
                                    L("If a protected container says no, believe it.", "warn"))),
                            Col(
                                Bk("QUARRIES / PROGRESSION",
                                    L("/vq opens Virtual Quarries when enabled."),
                                    L("Skill Tree unlocks long-term bonuses and progression perks.")),
                                Bk("VEHICLES / QOL",
                                    L("Car chassis are bought with RP from /shop and built at the car lift."),
                                    L("The 4-socket chassis is a one-per-wipe RP listing in the VIP shop category. It grants craftchassis.4 until wipe."),
                                    L("/tug and /tugboat are the VIPx vehicle license. Buying a tugboat with resources is available to the default group, so it is not VIP-only."),
                                    L("Chest stacks: 2 small and 3 large by default. VIP raises both to 5."),
                                    L("VIP has no decay on personal boats and tugboats."),
                                    L("Use /limits before the server starts judging your decor.")))
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
                                    L("Hide and Seek runs on scheduled event days when announced."),
                                    L("Purge is the last stretch of wipe, started by an admin: PvP, raiding, and base destruction are allowed serverwide."),
                                    L("Monuments are always PvP. Watch the zone warnings.", "warn")),
                                Bk("SIGNALS / BOSSES",
                                    L("Heli and Bradley signals may have tiers, cooldowns, rewards, and team recommendations."),
                                    L("Read event warnings before throwing expensive signals like a gremlin with fireworks."))),
                            Col(
                                Bk("SIGNALS / RAID CONTENT",
                                    L("Bradley and Heli signals are player-triggered combat events. Bradley buys spend RP."),
                                    L("Raidable Bases are event bases, not normal player bases."),
                                    L("Read difficulty and tier info before donating your kit to the floor.")),
                                Bk("HELP PAGES",
                                    L("/hordehelp — Hordes rules and reminders"),
                                    L("/raidbuilderinfo — vetted raidbase builder commands"),
                                    L("/rbinfo — raidable bases overview"),
                                    L("/pvpinfo — PvP zone reminders")))
                        }
                    },
                    new Tab
                    {
                        Title = "Hordes",
                        Commands = new List<string> { "hordehelp", "hordeshelp" },
                        Columns = new List<Column>
                        {
                            Col(Bk("HORDES HELP",
                                L("Hordes are Defendable Homes style wave events. Build smart, check your foundations, then try not to become part of the furniture."),
                                L("Your Defendable Home must be built on normal terrain using owned square or triangle foundations."),
                                L("Foundations must belong to you or your team, friends, or clan."),
                                L("You must be inside the Tool Cupboard area of the house when checking."),
                                L("Invalid areas include monuments, cliffs, mountains, clutter or decor terrain, and bad topology."),
                                L("Do not build too close to another active Defendable Homes event."),
                                L("Use /checkfoundations to check all foundations for the event."),
                                L("⊘ You or friends are not the foundation owner.", "muted"),
                                L("⊝ Foundation height or depth does not match.", "muted"),
                                L("⊛ Terrain topology is invalid. Prohibited: Cliff, Cliffside, Decor, Building, Monument, Clutter, Mountain.", "muted"),
                                L("◉ Foundation is within 40m of a custom monument.", "muted"),
                                L("Bring ammo, meds, armor, and room in your inventory."),
                                L("Do not drag hordes into player bases, vendors, safe builds, or random bystanders."),
                                L("PvP only applies if the event area is marked PvP, the weekend window is open, or the server is in Purge."),
                                L("Loot ownership follows Loot Protection and event rules.")))
                        }
                    },
                    new Tab
                    {
                        Title = "Raidable Bases",
                        Commands = new List<string> { "rbinfo", "raidablebasesinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("RAIDABLE BASES",
                                L("Raidable Bases are event bases. They are not normal player bases, and normal player bases stay protected outside Purge or approved raid events."),
                                L("Easy: available immediately."),
                                L("Medium: 24h after wipe. A 12h override exists, but that permission is not on the VIP group."),
                                L("Hard: 72h after wipe. The 48h override is the same story."),
                                L("Expert: 120h after wipe. The 96h override is not granted with VIP."),
                                L("Nightmare: 168h after wipe. The 144h override is not granted with VIP."),
                                L("Do not interfere with another player's PvE raid unless event rules allow it."),
                                L("PvP raid bases, PvP domes, the weekend window, and Purge are different. Read the warnings."),
                                L("If you started it, finish it before the server starts spawning consequences with your name on them.")))
                        }
                    },
                    new Tab
                    {
                        Title = "Builder",
                        Commands = new List<string> { "raidbuilderinfo", "raidbuildinfo", "raidguide" },
                        Columns = new List<Column>
                        {
                            Col(Bk("RAIDBASE BUILDER HELP",
                                L("Raidbase builder access is admin-only after vetting. This is not a public toy box.", "warn"),
                                L("Players approved to help build or test raidbase content."),
                                L("Permissions are assigned by an admin only after vetting."),
                                L("Do not share commands, bases, or access with unapproved players."),
                                L("/raidbasestart — start or set up a player raidbase build session"),
                                L("/raidreadypve — mark the saved raidbase ready for PvE use"),
                                L("/raidreadypvp — mark the saved raidbase ready for PvP use"),
                                L("/raidbaseend — end or clean up the active raidbase session"),
                                L("Build clean, playable bases with accessible paths and fair loot flow."),
                                L("Avoid traps that softlock players, exploit terrain, or create impossible raid paths."),
                                L("Ask an admin before making major format changes.")))
                        }
                    },
                    new Tab
                    {
                        Title = "PvP Zones",
                        Commands = new List<string> { "pvpinfo", "pvpzonesinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("PVP ZONES",
                                L("Monuments are always PvP. Watch the zone warnings before entering.", "warn"),
                                L("Event domes and PvP-marked areas may also allow player combat."),
                                L("The weekend window is Friday 8:00 PM through Monday 3:00 AM Eastern. Home shows whether it is open."),
                                L("Purge is separate. It is an admin toggle for the end of wipe, not the weekend window."),
                                L("Watch entry and exit messages."),
                                L("Watch dome and warning visuals."),
                                L("Watch Purge announcements during the last days of wipe."),
                                L("When the server tells you an area is PvP, believe it.", "warn")))
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
                            Col(
                                Bk("TRAVEL",
                                    L("VIP: 4 homes. Default is 3. Home cooldown and countdown are 5 seconds, down from 10."),
                                    L("VIPx: 5 homes, with no home cooldown or countdown."),
                                    L("Home teleports do not cost RP for anyone. VIP is not a waived fee."),
                                    L("Homes on tugboats are allowed for everyone."),
                                    L("VIPx can use /tug and /tugboat with a free vehicle license. A resource-bought tugboat is not VIP-only.")),
                                Bk("QUEUE & KITS",
                                    L("VIP gets a reconnect grace. VIPx gets the queue skip. They are not the same perk."),
                                    L("/perks shows crate time and kit status. /perks daily and /perks 4day claim the kits."),
                                    L("Kits stay locked for 48 hours after wipe."),
                                    L("Hackable crates: 2.5 minutes for VIP, 60 seconds for VIPx."),
                                    L("/afk is VIP and VIPx only.")),
                                Bk("ECONOMY & REWARDS",
                                    L("Heli signal rewards: 1.25× for VIP, 1.5× for VIPx."),
                                    L("A 1.1× activity-reward multiplier exists, but that permission is not on the VIP group."),
                                    L("Bradley cooldown cuts are configured for permissions the VIP groups do not have, and Bradley RP rewards are disabled. Do not count on a shorter Bradley cooldown."),
                                    L("Chest stacks: 2 small and 3 large by default, 5 and 5 for VIP."),
                                    L("The VIP shop category has claimable tools: iron axe and pickaxe for VIP, salvaged axe and pickaxe for VIPx, plus a one-per-wipe 4-socket chassis for 7000 RP."))),
                            Col(
                                Bk("GAMEPLAY",
                                    L("No decay on personal boats and tugboats for VIP."),
                                    L("Some cosmetic and building skin perks are VIP gated.")),
                                Bk("EARLY WIPE ACCESS",
                                    L("Easy raids are immediate for everyone."),
                                    L("Default unlocks: Medium 24h, Hard 72h, Expert 120h, Nightmare 168h."),
                                    L("Shorter times (12h, 48h, 96h, 144h) are configured, and those permissions are not on the VIP group. VIP alone does not unlock them early.")))
                        }
                    },
                    new Tab
                    {
                        Title = "Donate",
                        Commands = new List<string> { "viphelp", "donationinfo", "donateinfo" },
                        Columns = new List<Column>
                        {
                            Col(Bk("HOW TO GET VIP",
                                L("VIP is donation-supported, and it is not fully automatic yet."),
                                L("Donate through the server store link listed in Discord."),
                                L("After donating, post in the donation channel."),
                                L("Include your in-game name so an admin can apply the right group."),
                                L("VIP lasts until map wipe unless otherwise stated. VIPx follows the vipx group."),
                                L("Discord: discord.gg/dFmeweQmsy", "accent"),
                                L("/vip opens this page."),
                                L("VIP is convenience. Core content stays playable for everyone.", "muted")))
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
