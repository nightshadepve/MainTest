using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeHud", "Nightshade", "1.0.1")]
    [Description("Top-right text HUD: Eastern clock, weekend PvP countdown, TC shield, and event labels.")]
    public class NightshadeHud : RustPlugin
    {
        private const string Root = "NightshadeHud";
        private const string Bg = "NightshadeHud.Bg";
        private const string Icon = "NightshadeHud.Icon";
        private const string IconHit = "NightshadeHud.IconHit";
        private const string Half = "NightshadeHud.Half";
        private const string Events = "NightshadeHud.Events";
        private const string DataPath = "NightshadeHud/State";
        private const float Pad = 6f;
        private const float Line = 15f;

        [PluginReference] private Plugin NightshadeRaidProtection;
        [PluginReference("Nightshade Purge Status")] private Plugin PurgeStatus;
        [PluginReference("Raidable Bases")] private Plugin RaidableBases;
        [PluginReference] private Plugin ArmoredTrain;
        [PluginReference] private Plugin Convoy;
        [PluginReference] private Plugin DefendableHomes;

        private Settings config;
        private Stored stored;
        private TimeZoneInfo zone;
        private bool zoneFallback;
        private bool scanning;
        private bool eventsDirty;
        private bool popDirty;
        private Timer clockTimer;
        private Coroutine startupRoutine;
        private Coroutine convoyRoutine;

        private readonly Dictionary<ulong, string> mode = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> shown = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> station = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> shieldCache = new Dictionary<ulong, string>();
        private readonly Dictionary<string, string> eventCache = new Dictionary<string, string>();
        private string badgeCache = "\0";
        private string clockCache = "\0";
        private string onlineCache = "\0";
        private string countdownCache = "\0";

        private readonly HashSet<ulong> bradleys = new HashSet<ulong>();
        private readonly HashSet<ulong> helis = new HashSet<ulong>();
        private readonly HashSet<ulong> chinooks = new HashSet<ulong>();
        private readonly HashSet<ulong> drops = new HashSet<ulong>();
        private readonly HashSet<ulong> cargoSailing = new HashSet<ulong>();
        private readonly HashSet<ulong> cargoDocked = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> oilCrates = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> oilHacking = new HashSet<ulong>();
        private bool deepSea;
        private bool convoy;
        private bool train;
        private bool sputnik;
        private bool harborEvent;
        private bool purge;
        private int raids;
        private int dungeons;
        private int defend;
        private Vector3 smallOil;
        private Vector3 largeOil;
        private bool smallOilFound;
        private bool largeOilFound;

        private class Settings
        {
            [JsonProperty("Chat command")] public string Command = "hud";
            [JsonProperty("Default state for new players (minimized, half, full)")] public string DefaultState = "full";
            [JsonProperty("Layer (Overlay shows in the inventory)")] public string Layer = "Overlay";
            [JsonProperty("Anchor")] public string Anchor = "1 1";
            [JsonProperty("Inset from the right (px)")] public float Right = 16f;
            [JsonProperty("Inset from the top (px)")] public float Top = 58f;
            [JsonProperty("Width (px)")] public float Width = 252f;
            [JsonProperty("Icon size (px)")] public float IconSize = 34f;
            [JsonProperty("Event columns")] public int Columns = 3;
            [JsonProperty("Hide while using a computer station")] public bool HideInComputerStation = true;
            [JsonProperty("Logo URL")] public string LogoUrl = "https://raw.githubusercontent.com/nightshadepve/Nightshadeserver/refs/heads/main/260625_Logo_75_nobg.png";
            [JsonProperty("Server name")] public string ServerName = "Nightshade";
            [JsonProperty("Use 24-hour clock")] public bool Use24Hour = false;
            [JsonProperty("Time zone id")] public string TimeZoneId = "America/New_York";
            [JsonProperty("Show ET label")] public bool ShowZoneLabel = true;
            [JsonProperty("Show online count")] public bool ShowOnline = true;
            [JsonProperty("Show sleepers")] public bool ShowSleepers = true;
            [JsonProperty("Count admins in online")] public bool CountAdmins = false;
            [JsonProperty("Show PvP countdown")] public bool ShowCountdown = true;
            [JsonProperty("Show shield line")] public bool ShowShield = true;
            [JsonProperty("Hide shield line when none")] public bool HideShieldWhenNone = false;
            [JsonProperty("Panel color")] public string PanelColor = "#0E0E12";
            [JsonProperty("Panel opacity (0-100)")] public float PanelOpacity = 78f;
            [JsonProperty("Name color")] public string NameColor = "#F4F1EA";
            [JsonProperty("Clock color")] public string ClockColor = "#E7E2F2";
            [JsonProperty("Muted text color")] public string MutedColor = "#8A8A8A";
            [JsonProperty("Inactive event alpha (0-1)")] public float InactiveAlpha = 0.55f;
            [JsonProperty("Active event color")] public string ActiveColor = "#3DDC84";
            [JsonProperty("Contested event color")] public string ContestedColor = "#FFB020";
            [JsonProperty("PvE badge text")] public string BadgePveText = "PVE";
            [JsonProperty("PvP badge text")] public string BadgePvpText = "PVP";
            [JsonProperty("PvE badge color")] public string BadgePveColor = "#3DDC84";
            [JsonProperty("PvP badge color")] public string BadgePvpColor = "#FF4D4D";
            [JsonProperty("PvE badge background")] public string BadgePveBg = "#143528";
            [JsonProperty("PvP badge background")] public string BadgePvpBg = "#3A1212";
            [JsonProperty("Countdown color while PvP is active")] public string CountdownActive = "#FF4D4D";
            [JsonProperty("Countdown color while PvP is waiting")] public string CountdownIdle = "#C8C8C8";
            [JsonProperty("Shield active color")] public string ShieldActive = "#69D8A0";
            [JsonProperty("Shield queued color")] public string ShieldQueued = "#E2B968";
            [JsonProperty("Dungeon portal owner id")] public ulong DungeonPortalOwner = 10203040505UL;
            [JsonProperty("Oil rig crate radius")] public float OilRadius = 50f;
            [JsonProperty("Events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<EventCfg> Events = new List<EventCfg>();
        }

        private class EventCfg
        {
            [JsonProperty("Key")] public string Key;
            [JsonProperty("Label")] public string Label;
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Show count")] public bool ShowCount;
            [JsonProperty("Order")] public int Order;
        }

        private class Stored
        {
            public Dictionary<string, string> Players = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        protected override void LoadDefaultConfig()
        {
            config = new Settings();
            config.Events = DefaultEvents();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<Settings>() ?? new Settings(); }
            catch (Exception ex)
            {
                PrintWarning("Invalid config, using defaults: " + ex.Message);
                config = new Settings();
            }
            if (config.Events == null) config.Events = new List<EventCfg>();
            if (config.Columns < 1) config.Columns = 3;
            if (config.Width < 120f) config.Width = 120f;
            if (config.IconSize < 16f) config.IconSize = 16f;
            bool added = false;
            foreach (var known in DefaultEvents())
            {
                if (config.Events.Exists(e => e != null && e.Key == known.Key)) continue;
                config.Events.Add(known);
                added = true;
            }
            config.Events.RemoveAll(e => e == null || string.IsNullOrEmpty(e.Key));
            if (added || config.Events.Count == 0) SaveConfig();
            else SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void Init()
        {
            cmd.AddChatCommand(string.IsNullOrEmpty(config.Command) ? "hud" : config.Command, this, nameof(CmdHud));
            cmd.AddConsoleCommand("nightshadehud.ui", this, nameof(CmdUi));
            try { stored = Interface.Oxide.DataFileSystem.ReadObject<Stored>(DataPath) ?? new Stored(); }
            catch { stored = new Stored(); }
            if (stored.Players == null) stored.Players = new Dictionary<string, string>(StringComparer.Ordinal);
            zone = ResolveZone(config.TimeZoneId, out zoneFallback);
        }

        private void OnServerInitialized()
        {
            if (zoneFallback)
                PrintWarning("Time zone '" + config.TimeZoneId + "' was not found. Using a built-in US Eastern zone.");
            if (ServerMgr.Instance != null)
                startupRoutine = ServerMgr.Instance.StartCoroutine(Startup());
            else
                timer.Once(1f, () => { if (ServerMgr.Instance != null) startupRoutine = ServerMgr.Instance.StartCoroutine(Startup()); });
        }

        private void Unload()
        {
            clockTimer?.Destroy();
            if (ServerMgr.Instance != null)
            {
                if (startupRoutine != null) ServerMgr.Instance.StopCoroutine(startupRoutine);
                if (convoyRoutine != null) ServerMgr.Instance.StopCoroutine(convoyRoutine);
            }
            startupRoutine = null;
            convoyRoutine = null;
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
                if (player != null) CuiHelper.DestroyUi(player, Root);
        }

        private void OnServerSave() => SaveData();

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(DataPath, stored);

        #region Player UI

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            timer.Once(1f, () => { if (player != null && player.IsConnected) Show(player); });
            MarkPop();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            shown.Remove(player.userID);
            station.Remove(player.userID);
            shieldCache.Remove(player.userID);
            MarkPop();
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            timer.Once(1f, () => { if (player != null && player.IsConnected) Show(player); });
        }

        private void OnEntityMounted(ComputerStation entity, BasePlayer player)
        {
            if (!config.HideInComputerStation || player == null) return;
            station.Add(player.userID);
            CuiHelper.DestroyUi(player, Root);
            shown.Remove(player.userID);
        }

        private void OnEntityDismounted(ComputerStation entity, BasePlayer player)
        {
            if (player == null || !station.Remove(player.userID)) return;
            Show(player);
        }

        private void OnPlayerGainedBuildingPrivilege(BasePlayer player) => RefreshShield(player);
        private void OnPlayerLostBuildingPrivilege(BasePlayer player) => RefreshShield(player);

        private void CmdHud(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            string requested = args == null || args.Length == 0 ? "cycle" : Normalize(args[0]);
            if (requested == null)
            {
                player.ChatMessage("<color=#b084ff>[Nightshade]</color> /" + config.Command + " min | half | full | cycle");
                return;
            }
            SetState(player, requested == "cycle" ? Cycle(StateOf(player)) : requested, true);
        }

        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            SetState(player, Cycle(StateOf(player)), false);
        }

        private void SetState(BasePlayer player, string next, bool announce)
        {
            string id = player.UserIDString;
            string previous = StateOf(player);
            mode[player.userID] = next;
            stored.Players[id] = next;
            SaveData();
            if (station.Contains(player.userID)) return;
            string visible;
            if (!shown.TryGetValue(player.userID, out visible)) Show(player);
            else if (visible != next) Transition(player, visible, next);
            if (announce)
                player.ChatMessage("<color=#b084ff>[Nightshade]</color> HUD: " + next + ".");
        }

        private string StateOf(BasePlayer player)
        {
            string value;
            if (mode.TryGetValue(player.userID, out value)) return value;
            if (stored.Players.TryGetValue(player.UserIDString, out value))
            {
                value = Normalize(value) ?? Normalize(config.DefaultState) ?? "full";
                mode[player.userID] = value;
                return value;
            }
            value = Normalize(config.DefaultState) ?? "full";
            mode[player.userID] = value;
            return value;
        }

        private static string Cycle(string state)
        {
            if (state == "minimized") return "half";
            if (state == "half") return "full";
            return "minimized";
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "min":
                case "minimized":
                case "icon":
                case "1": return "minimized";
                case "half":
                case "top":
                case "2": return "half";
                case "full":
                case "all":
                case "3": return "full";
                case "cycle": return "cycle";
                default: return null;
            }
        }

        private void Show(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            CuiHelper.DestroyUi(player, "UI_Hud");
            CuiHelper.DestroyUi(player, Root);
            shown.Remove(player.userID);
            if (station.Contains(player.userID) || (config.HideInComputerStation && player.GetMounted() is ComputerStation))
            {
                station.Add(player.userID);
                return;
            }
            string state = StateOf(player);
            var container = new CuiElementContainer();
            AddRoot(container, state);
            AddIcon(container, state);
            if (state != "minimized")
            {
                AddBackground(container);
                AddHalf(container, player);
                if (state == "full") AddEvents(container);
            }
            CuiHelper.AddUi(player, container);
            shown[player.userID] = state;
        }

        private void Transition(BasePlayer player, string from, string to)
        {
            if (from == to) return;
            Resize(player, to);
            MoveIcon(player, to);
            if (from == "minimized")
            {
                var add = new CuiElementContainer();
                AddBackground(add);
                AddHalf(add, player);
                if (to == "full") AddEvents(add);
                CuiHelper.AddUi(player, add);
            }
            else if (to == "minimized")
            {
                CuiHelper.DestroyUi(player, Bg);
                CuiHelper.DestroyUi(player, Half);
                CuiHelper.DestroyUi(player, Events);
            }
            else if (from == "half" && to == "full")
            {
                var add = new CuiElementContainer();
                AddEvents(add);
                CuiHelper.AddUi(player, add);
            }
            else if (from == "full" && to == "half")
            {
                CuiHelper.DestroyUi(player, Events);
            }
            shown[player.userID] = to;
            if (to != "minimized") shieldCache[player.userID] = ShieldSignature(player);
        }

        private void AddRoot(CuiElementContainer container, string state)
        {
            container.Add(new CuiElement
            {
                Name = Root,
                Parent = string.IsNullOrEmpty(config.Layer) ? "Overlay" : config.Layer,
                DestroyUi = Root,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = config.Anchor, AnchorMax = config.Anchor, OffsetMin = OffsetMin(state), OffsetMax = OffsetMax() }
                }
            });
        }

        private void AddBackground(CuiElementContainer container)
        {
            container.Add(new CuiElement
            {
                Name = Bg,
                Parent = Root,
                Components =
                {
                    new CuiImageComponent { Color = Hex(config.PanelColor, config.PanelOpacity / 100f) },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
        }

        private void AddIcon(CuiElementContainer container, string state)
        {
            string min = state == "minimized" ? Px(0f, -config.IconSize) : Px(Pad, -Pad - config.IconSize);
            string max = state == "minimized" ? Px(config.IconSize, 0f) : Px(Pad + config.IconSize, -Pad);
            container.Add(new CuiElement
            {
                Name = Icon,
                Parent = Root,
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Url = config.LogoUrl,
                        Color = "1 1 1 1",
                        Sprite = "assets/content/textures/generic/fulltransparent.tga"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = min, OffsetMax = max }
                }
            });
            container.Add(new CuiElement
            {
                Name = IconHit,
                Parent = Root,
                Components =
                {
                    new CuiButtonComponent { Command = "nightshadehud.ui", Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = min, OffsetMax = max }
                }
            });
        }

        private void AddHalf(CuiElementContainer container, BasePlayer player)
        {
            bool pvp = CombatPvp();
            float textX = Pad + config.IconSize + 6f;
            float top = -Pad;
            string parent = HalfParent(container);
            AddImage(container, "NightshadeHud.BadgeBg", parent, Hex(pvp ? config.BadgePvpBg : config.BadgePveBg), Px(textX, top - 16f), Px(textX + 42f, top));
            AddLabel(container, "NightshadeHud.Badge", "NightshadeHud.BadgeBg", pvp ? config.BadgePvpText : config.BadgePveText, Hex(pvp ? config.BadgePvpColor : config.BadgePveColor), 11, TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", true);
            AddLabel(container, "NightshadeHud.Name", parent, config.ServerName, Hex(config.NameColor), 12, TextAnchor.MiddleLeft, "0 1", "0 1", Px(textX + 48f, top - 16f), Px(config.Width - 104f, top), true);
            AddLabel(container, "NightshadeHud.Clock", parent, ClockText(), Hex(config.ClockColor), 11, TextAnchor.MiddleRight, "0 1", "0 1", Px(config.Width - 100f, top - 16f), Px(config.Width - Pad, top));
            float y = top - config.IconSize - 4f;
            AddLabel(container, "NightshadeHud.Online", parent, OnlineText(), Hex(config.MutedColor, 0.9f), 11, TextAnchor.MiddleLeft, "0 1", "1 1", Px(Pad, y - Line), Px(-Pad, y));
            y -= Line;
            string countText, countColor;
            Countdown(out countText, out countColor);
            AddLabel(container, "NightshadeHud.Countdown", parent, countText, countColor, 11, TextAnchor.MiddleLeft, "0 1", "1 1", Px(Pad, y - Line), Px(-Pad, y));
            y -= Line;
            string shieldText, shieldColor;
            ShieldLine(player, out shieldText, out shieldColor);
            AddLabel(container, "NightshadeHud.Shield", parent, shieldText, shieldColor, 11, TextAnchor.MiddleLeft, "0 1", "1 1", Px(Pad, y - Line), Px(-Pad, y));
            if (player != null) shieldCache[player.userID] = shieldText + "|" + shieldColor;
            badgeCache = pvp ? "1" : "0";
            clockCache = ClockText();
            onlineCache = OnlineText();
            countdownCache = countText + "|" + countColor;
        }

        private string HalfParent(CuiElementContainer container)
        {
            container.Add(new CuiElement
            {
                Name = Half,
                Parent = Root,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            return Half;
        }

        private void AddEvents(CuiElementContainer container)
        {
            var enabled = EnabledEvents();
            container.Add(new CuiElement
            {
                Name = Events,
                Parent = Root,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Px(0f, -PanelHeight("full")), OffsetMax = Px(config.Width, -HalfHeight()) }
                }
            });
            int columns = Math.Max(1, config.Columns);
            float colW = (config.Width - Pad * 2f) / columns;
            for (int i = 0; i < enabled.Count; i++)
            {
                var entry = enabled[i];
                int count;
                bool on, contested;
                ReadEvent(entry.Key, out count, out on, out contested);
                string text = EventText(entry, count, on);
                string color = EventColor(on, contested);
                eventCache[entry.Key] = text + "|" + color;
                int col = i % columns;
                int row = i / columns;
                float x = Pad + col * colW;
                float y = -row * Line;
                AddLabel(container, "NightshadeHud.Ev." + entry.Key, Events, text, color, 10, TextAnchor.MiddleLeft, "0 1", "0 1", Px(x, y - Line), Px(x + colW, y));
            }
        }

        private void Resize(BasePlayer player, string state)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = Root,
                Update = true,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = config.Anchor, AnchorMax = config.Anchor, OffsetMin = OffsetMin(state), OffsetMax = OffsetMax() }
                }
            });
            CuiHelper.AddUi(player, container);
        }

        private void MoveIcon(BasePlayer player, string state)
        {
            string min = state == "minimized" ? Px(0f, -config.IconSize) : Px(Pad, -Pad - config.IconSize);
            string max = state == "minimized" ? Px(config.IconSize, 0f) : Px(Pad + config.IconSize, -Pad);
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = Icon,
                Update = true,
                Components =
                {
                    new CuiRawImageComponent { Url = config.LogoUrl, Color = "1 1 1 1", Sprite = "assets/content/textures/generic/fulltransparent.tga" },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = min, OffsetMax = max }
                }
            });
            container.Add(new CuiElement
            {
                Name = IconHit,
                Update = true,
                Components =
                {
                    new CuiButtonComponent { Command = "nightshadehud.ui", Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = min, OffsetMax = max }
                }
            });
            CuiHelper.AddUi(player, container);
        }

        private static string Px(float x, float y) => x.ToString("0.#", CultureInfo.InvariantCulture) + " " + y.ToString("0.#", CultureInfo.InvariantCulture);

        private string OffsetMax() => Px(-config.Right, -config.Top);

        private string OffsetMin(string state)
        {
            float width = state == "minimized" ? config.IconSize : config.Width;
            float height = PanelHeight(state);
            return Px(-config.Right - width, -config.Top - height);
        }

        private float HalfHeight() => Pad + config.IconSize + 4f + Line * 3f + Pad;

        private float PanelHeight(string state)
        {
            if (state == "minimized") return config.IconSize;
            if (state != "full") return HalfHeight();
            int count = EnabledEvents().Count;
            int columns = Math.Max(1, config.Columns);
            int rows = count == 0 ? 0 : (count + columns - 1) / columns;
            return HalfHeight() + rows * Line + (rows > 0 ? 4f : 0f);
        }

        #endregion

        #region Shared updates

        private void ScheduleClock()
        {
            clockTimer?.Destroy();
            double seconds = CountdownSeconds();
            float delay = seconds <= 600d ? 10f : 60f;
            clockTimer = timer.Once(delay, () =>
            {
                TickShared(true);
                ScheduleClock();
            });
        }

        private void TickShared(bool includeShield)
        {
            string clock = ClockText();
            string online = OnlineText();
            string countText, countColor;
            Countdown(out countText, out countColor);
            string countSig = countText + "|" + countColor;
            bool pvp = CombatPvp();
            string badge = pvp ? "1" : "0";
            bool clockChanged = clock != clockCache;
            bool onlineChanged = online != onlineCache;
            bool countChanged = countSig != countdownCache;
            bool badgeChanged = badge != badgeCache;
            if (clockChanged) clockCache = clock;
            if (onlineChanged) onlineCache = online;
            if (countChanged) countdownCache = countSig;
            if (badgeChanged) badgeCache = badge;
            if (!clockChanged && !onlineChanged && !countChanged && !badgeChanged && !includeShield) return;
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                string state;
                if (!shown.TryGetValue(player.userID, out state) || state == "minimized") continue;
                if (badgeChanged) PushBadge(player, pvp);
                if (clockChanged) PushText(player, "NightshadeHud.Clock", clock, Hex(config.ClockColor));
                if (onlineChanged) PushText(player, "NightshadeHud.Online", online, Hex(config.MutedColor, 0.9f));
                if (countChanged) PushText(player, "NightshadeHud.Countdown", countText, countColor);
                if (includeShield) PushShield(player, false);
            }
        }

        private void PushModeLine()
        {
            badgeCache = "\0";
            countdownCache = "\0";
            TickShared(false);
        }

        private void MarkPop()
        {
            if (popDirty) return;
            popDirty = true;
            NextTick(() =>
            {
                popDirty = false;
                onlineCache = "\0";
                TickShared(false);
            });
        }

        private void RefreshShield(BasePlayer player)
        {
            if (player == null) return;
            NextTick(() => PushShield(player, true));
        }

        private void PushShield(BasePlayer player, bool force)
        {
            if (player == null) return;
            string state;
            if (!shown.TryGetValue(player.userID, out state) || state == "minimized") return;
            string sig = ShieldSignature(player);
            string previous;
            if (!force && shieldCache.TryGetValue(player.userID, out previous) && previous == sig) return;
            shieldCache[player.userID] = sig;
            string text, color;
            ShieldLine(player, out text, out color);
            PushText(player, "NightshadeHud.Shield", text, color);
        }

        private void PushBadge(BasePlayer player, bool pvp)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = "NightshadeHud.BadgeBg",
                Update = true,
                Components = { new CuiImageComponent { Color = Hex(pvp ? config.BadgePvpBg : config.BadgePveBg) } }
            });
            container.Add(new CuiElement
            {
                Name = "NightshadeHud.Badge",
                Update = true,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = pvp ? config.BadgePvpText : config.BadgePveText,
                        FontSize = 11,
                        Align = TextAnchor.MiddleCenter,
                        Color = Hex(pvp ? config.BadgePvpColor : config.BadgePveColor),
                        Font = "robotocondensed-bold.ttf"
                    }
                }
            });
            CuiHelper.AddUi(player, container);
        }

        private void PushText(BasePlayer player, string name, string text, string color)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = name,
                Update = true,
                Components = { new CuiTextComponent { Text = text ?? string.Empty, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = color, Font = "robotocondensed-regular.ttf" } }
            });
            CuiHelper.AddUi(player, container);
        }

        private void MarkEvents()
        {
            if (scanning || eventsDirty) return;
            eventsDirty = true;
            timer.Once(0.5f, FlushEvents);
        }

        private void FlushEvents()
        {
            eventsDirty = false;
            var enabled = EnabledEvents();
            var dirty = new List<EventCfg>();
            foreach (var entry in enabled)
            {
                int count;
                bool on, contested;
                ReadEvent(entry.Key, out count, out on, out contested);
                string sig = EventText(entry, count, on) + "|" + EventColor(on, contested);
                string previous;
                if (eventCache.TryGetValue(entry.Key, out previous) && previous == sig) continue;
                eventCache[entry.Key] = sig;
                dirty.Add(entry);
            }
            if (dirty.Count == 0) return;
            foreach (var player in BasePlayer.activePlayerList)
            {
                string state;
                if (player == null || !shown.TryGetValue(player.userID, out state) || state != "full") continue;
                var container = new CuiElementContainer();
                foreach (var entry in dirty)
                {
                    int count;
                    bool on, contested;
                    ReadEvent(entry.Key, out count, out on, out contested);
                    container.Add(new CuiElement
                    {
                        Name = "NightshadeHud.Ev." + entry.Key,
                        Update = true,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = EventText(entry, count, on),
                                FontSize = 10,
                                Align = TextAnchor.MiddleLeft,
                                Color = EventColor(on, contested),
                                Font = "robotocondensed-bold.ttf"
                            }
                        }
                    });
                }
                CuiHelper.AddUi(player, container);
            }
        }

        #endregion

        #region Text

        private bool CombatPvp()
        {
            if (purge) return true;
            return CallBool(NightshadeRaidProtection, "API_IsPvPActive");
        }

        private string ClockText()
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            string text = local.ToString(config.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);
            if (config.ShowZoneLabel) text += " ET";
            return text;
        }

        private string OnlineText()
        {
            if (!config.ShowOnline && !config.ShowSleepers) return string.Empty;
            int online = 0;
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                if (!config.CountAdmins && player.IsAdmin) continue;
                online++;
            }
            int sleepers = BasePlayer.sleepingPlayerList == null ? 0 : BasePlayer.sleepingPlayerList.Count;
            if (config.ShowOnline && config.ShowSleepers)
                return "Online " + online + "  ·  Sleepers " + sleepers;
            if (config.ShowOnline) return "Online " + online;
            return "Sleepers " + sleepers;
        }

        private void Countdown(out string text, out string color)
        {
            if (!config.ShowCountdown)
            {
                text = string.Empty;
                color = Hex(config.CountdownIdle);
                return;
            }
            if (NightshadeRaidProtection == null || !NightshadeRaidProtection.IsLoaded)
            {
                text = "PvP unavailable";
                color = Hex(config.MutedColor, config.InactiveAlpha);
                return;
            }
            bool active = CallBool(NightshadeRaidProtection, "API_IsPvPActive");
            double seconds = ToDouble(NightshadeRaidProtection.Call("API_SecondsUntilPvPChange"));
            text = active ? "PVP · ends in " + FormatDur(seconds) : "PvP in " + FormatDur(seconds);
            color = Hex(active ? config.CountdownActive : config.CountdownIdle);
        }

        private double CountdownSeconds()
        {
            if (NightshadeRaidProtection == null || !NightshadeRaidProtection.IsLoaded) return 3600d;
            return Math.Max(0d, ToDouble(NightshadeRaidProtection.Call("API_SecondsUntilPvPChange")));
        }

        private void ShieldLine(BasePlayer player, out string text, out string color)
        {
            if (!config.ShowShield)
            {
                text = string.Empty;
                color = Hex(config.MutedColor, config.InactiveAlpha);
                return;
            }
            if (player == null || NightshadeRaidProtection == null || !NightshadeRaidProtection.IsLoaded)
            {
                text = "Shield: —";
                color = Hex(config.MutedColor, config.InactiveAlpha);
                return;
            }
            var raw = NightshadeRaidProtection.Call("API_GetPlayerShield", player) as Dictionary<string, object>;
            string state = raw != null && raw.ContainsKey("state") ? raw["state"] as string : "none";
            double seconds = raw != null && raw.ContainsKey("seconds") ? ToDouble(raw["seconds"]) : 0d;
            if (state == "active")
            {
                text = "Shield: active " + FormatDur(seconds);
                color = Hex(config.ShieldActive);
                return;
            }
            if (state == "queued")
            {
                text = "Shield: queued " + FormatDur(seconds);
                color = Hex(config.ShieldQueued);
                return;
            }
            text = config.HideShieldWhenNone ? string.Empty : "Shield: none";
            color = Hex(config.MutedColor, config.InactiveAlpha);
        }

        private string ShieldSignature(BasePlayer player)
        {
            string text, color;
            ShieldLine(player, out text, out color);
            return text + "|" + color;
        }

        private static string FormatDur(double seconds)
        {
            if (seconds < 0d) seconds = 0d;
            var span = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            if (span.TotalDays >= 1d) return ((int)span.TotalDays) + "d " + span.Hours.ToString("00") + "h";
            if (span.TotalHours >= 1d) return ((int)span.TotalHours) + "h " + span.Minutes.ToString("00") + "m";
            if (span.TotalMinutes >= 10d) return span.Minutes + "m";
            return span.Minutes.ToString("00") + "m " + span.Seconds.ToString("00") + "s";
        }

        #endregion

        #region Events

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null || scanning) return;
            timer.Once(1f, () => RefreshFromPlugin(plugin.Name));
        }

        private void RefreshFromPlugin(string name)
        {
            if (scanning) return;
            if (name == "Raidable Bases") RefreshRaids();
            else if (name == "ArmoredTrain") { train = CallBool(ArmoredTrain, "IsArmoredTrainActive"); MarkEvents(); }
            else if (name == "Convoy" && ServerMgr.Instance != null)
            {
                convoy = false;
                if (convoyRoutine != null) ServerMgr.Instance.StopCoroutine(convoyRoutine);
                convoyRoutine = ServerMgr.Instance.StartCoroutine(CollectConvoyVehicles());
            }
            else if (name == "Dungeon Events") RefreshDungeons();
            else if (name == "DefendableHomes") { defend = CountDefend(); MarkEvents(); }
            else if (name == "Nightshade Purge Status") { purge = CallBool(PurgeStatus, "API_IsPurgeActive"); MarkEvents(); PushModeLine(); }
            else if (name == "NightshadeRaidProtection") PushModeLine();
        }

        private void OnNightshadePvPStarted(long startUtcTicks, long endUtcTicks) => PushModeLine();
        private void OnNightshadePvPEnded(long startUtcTicks, long endUtcTicks) => PushModeLine();
        private void OnPurgeStarted(double endTimestamp) { purge = true; MarkEvents(); PushModeLine(); }
        private void OnPurgeEnded() { purge = false; MarkEvents(); PushModeLine(); }

        private void OnEntitySpawned(BradleyAPC entity) => NoteId(bradleys, entity, true, entity == null || entity.OwnerID != 0);
        private void OnEntityKill(BradleyAPC entity) => NoteId(bradleys, entity, false, false);
        private void OnEntitySpawned(PatrolHelicopter entity) => NoteId(helis, entity, true, entity == null || entity.OwnerID != 0);
        private void OnEntityKill(PatrolHelicopter entity) => NoteId(helis, entity, false, false);
        private void OnEntitySpawned(CH47Helicopter entity) => NoteId(chinooks, entity, true, entity == null || entity.OwnerID != 0);
        private void OnEntityKill(CH47Helicopter entity) => NoteId(chinooks, entity, false, false);
        private void OnEntitySpawned(SupplyDrop entity) => NoteId(drops, entity, true, entity == null);
        private void OnEntityKill(SupplyDrop entity) => NoteId(drops, entity, false, false);
        private void OnEntitySpawned(CargoShip entity) { if (!scanning) NoteCargo(entity, true); }
        private void OnEntityKill(CargoShip entity) { if (!scanning) NoteCargo(entity, false); }
        private void OnCargoShipHarborArrived(CargoShip entity) => NextTick(RecountCargo);
        private void OnCargoShipHarborLeave(CargoShip entity) => NextTick(RecountCargo);
        private void OnEntitySpawned(HackableLockedCrate entity) { if (!scanning) NoteOil(entity, true); }
        private void OnEntityKill(HackableLockedCrate entity) { if (!scanning) NoteOil(entity, false); }
        private void OnCrateHack(HackableLockedCrate entity) { if (entity != null && entity.net != null) oilHacking.Add(entity.net.ID.Value); MarkEvents(); }
        private void OnCrateHackEnd(HackableLockedCrate entity) { if (entity != null && entity.net != null) oilHacking.Remove(entity.net.ID.Value); MarkEvents(); }
        private void OnDeepSeaOpened(DeepSeaManager manager) { deepSea = true; MarkEvents(); }
        private void OnDeepSeaClosed(DeepSeaManager manager) { deepSea = false; MarkEvents(); }
        private void OnConvoyStart() { convoy = true; MarkEvents(); }
        private void OnConvoyStop() { convoy = false; MarkEvents(); }
        private void OnArmoredTrainEventStart() { train = true; MarkEvents(); }
        private void OnArmoredTrainEventStop() { train = false; MarkEvents(); }
        private void OnSputnikEventStart() { sputnik = true; MarkEvents(); }
        private void OnSputnikEventStop() { sputnik = false; MarkEvents(); }
        private void OnHarborEventStart() { harborEvent = true; MarkEvents(); }
        private void OnHarborEventEnd() { harborEvent = false; MarkEvents(); }
        private void OnRaidableBaseStarted(object raid) => RefreshRaids();
        private void OnRaidableBaseEnded(object raid) => RefreshRaids();
        private void OnRaidableBaseDespawn(object raid) => RefreshRaids();
        private void OnDungeonSpawn(ulong owner, Vector3 portal, string grid, string tier) => RefreshDungeons();
        private void OnDungeonDespawn(ulong owner, Vector3 portal, string grid, string tier) => RefreshDungeons();
        private void OnDefendableHomesStart(Vector3 position, float radius) { if (!scanning) { defend++; MarkEvents(); } }
        private void OnDefendableHomesEnd(Vector3 position) { if (!scanning) { defend = Math.Max(0, defend - 1); MarkEvents(); } }

        private void NoteId(HashSet<ulong> set, BaseEntity entity, bool alive, bool ignore)
        {
            if (scanning || entity == null || entity.net == null || ignore) return;
            if (alive) set.Add(entity.net.ID.Value);
            else set.Remove(entity.net.ID.Value);
            MarkEvents();
        }

        private void NoteCargo(CargoShip ship, bool alive)
        {
            if (scanning || ship == null || ship.net == null) return;
            ulong id = ship.net.ID.Value;
            cargoSailing.Remove(id);
            cargoDocked.Remove(id);
            if (alive)
            {
                if (ship.IsShipDocked) cargoDocked.Add(id);
                else cargoSailing.Add(id);
            }
            MarkEvents();
        }

        private void RecountCargo()
        {
            if (scanning) return;
            cargoSailing.Clear();
            cargoDocked.Clear();
            var ships = UnityEngine.Object.FindObjectsOfType<CargoShip>();
            if (ships == null) { MarkEvents(); return; }
            foreach (var ship in ships)
                if (ship != null && !ship.IsDestroyed) NoteCargo(ship, true);
        }

        private void NoteOil(HackableLockedCrate crate, bool alive)
        {
            if (scanning || crate == null || crate.net == null) return;
            ulong id = crate.net.ID.Value;
            oilCrates.Remove(id);
            if (!alive) oilHacking.Remove(id);
            else
            {
                string rig = OilRig(crate.transform.position);
                if (rig != null) oilCrates[id] = rig;
                if (crate.IsBeingHacked()) oilHacking.Add(id);
            }
            MarkEvents();
        }

        private string OilRig(Vector3 position)
        {
            float radius = Mathf.Max(1f, config.OilRadius);
            if (smallOilFound && (position - smallOil).sqrMagnitude <= radius * radius) return "SmallOil";
            if (largeOilFound && (position - largeOil).sqrMagnitude <= radius * radius) return "LargeOil";
            return null;
        }

        private void FindOilRigs()
        {
            smallOilFound = false;
            largeOilFound = false;
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || string.IsNullOrEmpty(monument.name)) continue;
                if (monument.name.Contains("oilrig_2")) { smallOil = monument.transform.position; smallOilFound = true; }
                else if (monument.name.Contains("oilrig_1")) { largeOil = monument.transform.position; largeOilFound = true; }
            }
        }

        private IEnumerator Startup()
        {
            // Return from OnServerInitialized before any scene query or CUI build.
            yield return null;
            FindOilRigs();
            yield return null;
            scanning = true;
            bradleys.Clear(); helis.Clear(); chinooks.Clear(); drops.Clear();
            cargoSailing.Clear(); cargoDocked.Clear(); oilCrates.Clear(); oilHacking.Clear();
            dungeons = 0;
            convoy = false;
            var steps = new IEnumerator[]
            {
                CollectOwned<BradleyAPC>(bradleys),
                CollectHelis(),
                CollectOwned<CH47Helicopter>(chinooks),
                CollectAll<SupplyDrop>(drops),
                CollectCargoShips(),
                CollectOilCrates(),
                CollectPortals(),
                CollectConvoyVehicles()
            };
            for (int s = 0; s < steps.Length; s++)
            {
                var step = steps[s];
                while (step != null && step.MoveNext()) yield return step.Current;
            }
            yield return null;
            train = CallBool(ArmoredTrain, "IsArmoredTrainActive");
            raids = RaidableBases != null && RaidableBases.IsLoaded ? (int)ToDouble(RaidableBases.Call("GetAllEventsCount")) : 0;
            deepSea = DeepSeaOpen();
            purge = CallBool(PurgeStatus, "API_IsPurgeActive");
            defend = CountDefend();
            scanning = false;
            eventCache.Clear();
            MarkEvents();
            var players = new List<BasePlayer>();
            if (BasePlayer.activePlayerList != null)
                foreach (var connected in BasePlayer.activePlayerList)
                    if (connected != null) players.Add(connected);
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player != null && player.IsConnected) Show(player);
                if ((i + 1) % 2 == 0) yield return null;
            }
            ScheduleClock();
            startupRoutine = null;
        }


        private IEnumerator CollectOwned<T>(HashSet<ulong> set) where T : BaseEntity
        {
            var entities = UnityEngine.Object.FindObjectsOfType<T>();
            yield return null;
            if (entities == null) yield break;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity != null && !entity.IsDestroyed && entity.net != null && entity.OwnerID == 0)
                    set.Add(entity.net.ID.Value);
                if ((i + 1) % 40 == 0) yield return null;
            }
        }

        private IEnumerator CollectAll<T>(HashSet<ulong> set) where T : BaseEntity
        {
            var entities = UnityEngine.Object.FindObjectsOfType<T>();
            yield return null;
            if (entities == null) yield break;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity != null && !entity.IsDestroyed && entity.net != null)
                    set.Add(entity.net.ID.Value);
                if ((i + 1) % 40 == 0) yield return null;
            }
        }

        private IEnumerator CollectHelis()
        {
            var found = UnityEngine.Object.FindObjectsOfType<PatrolHelicopter>();
            yield return null;
            if (found == null) yield break;
            for (int i = 0; i < found.Length; i++)
            {
                var heli = found[i];
                if (heli != null && !heli.IsDestroyed && heli.net != null)
                {
                    if (heli.OwnerID == 0) helis.Add(heli.net.ID.Value);
                    if (!convoy && Convoy != null && Convoy.IsLoaded && Convoy.Call("IsConvoyHeli", heli) is bool yes && yes)
                        convoy = true;
                }
                if ((i + 1) % 20 == 0) yield return null;
            }
        }

        private IEnumerator CollectCargoShips()
        {
            var ships = UnityEngine.Object.FindObjectsOfType<CargoShip>();
            yield return null;
            if (ships == null) yield break;
            for (int i = 0; i < ships.Length; i++)
            {
                var ship = ships[i];
                if (ship == null || ship.IsDestroyed || ship.net == null) continue;
                if (ship.IsShipDocked) cargoDocked.Add(ship.net.ID.Value);
                else cargoSailing.Add(ship.net.ID.Value);
            }
        }

        private IEnumerator CollectOilCrates()
        {
            var crates = UnityEngine.Object.FindObjectsOfType<HackableLockedCrate>();
            yield return null;
            if (crates == null) yield break;
            for (int i = 0; i < crates.Length; i++)
            {
                var crate = crates[i];
                if (crate == null || crate.IsDestroyed || crate.net == null) continue;
                string rig = OilRig(crate.transform.position);
                if (rig == null) continue;
                oilCrates[crate.net.ID.Value] = rig;
                if (crate.IsBeingHacked()) oilHacking.Add(crate.net.ID.Value);
                if ((i + 1) % 20 == 0) yield return null;
            }
        }

        private IEnumerator CollectPortals()
        {
            var portals = UnityEngine.Object.FindObjectsOfType<BasePortal>();
            yield return null;
            if (portals == null) yield break;
            int count = 0;
            for (int i = 0; i < portals.Length; i++)
            {
                var portal = portals[i];
                if (portal != null && !portal.IsDestroyed && portal.OwnerID == config.DungeonPortalOwner) count++;
                if ((i + 1) % 40 == 0) yield return null;
            }
            dungeons = count;
        }

        private IEnumerator CollectConvoyVehicles()
        {
            if (convoy || Convoy == null || !Convoy.IsLoaded) yield break;
            var vehicles = UnityEngine.Object.FindObjectsOfType<BaseVehicle>();
            yield return null;
            if (vehicles == null) yield break;
            for (int i = 0; i < vehicles.Length && !convoy; i++)
            {
                var vehicle = vehicles[i];
                if (vehicle != null && !vehicle.IsDestroyed && Convoy.Call("IsConvoyVehicle", vehicle) is bool yes && yes)
                {
                    convoy = true;
                    if (!scanning) MarkEvents();
                    yield break;
                }
                if ((i + 1) % 25 == 0) yield return null;
            }
            if (!scanning) MarkEvents();
        }

        private void RefreshRaids()
        {
            if (scanning) return;
            raids = RaidableBases != null && RaidableBases.IsLoaded ? (int)ToDouble(RaidableBases.Call("GetAllEventsCount")) : Math.Max(0, raids);
            MarkEvents();
        }

        private void RefreshDungeons()
        {
            if (scanning) return;
            int count = 0;
            var portals = UnityEngine.Object.FindObjectsOfType<BasePortal>();
            if (portals != null)
            {
                foreach (var portal in portals)
                    if (portal != null && !portal.IsDestroyed && portal.OwnerID == config.DungeonPortalOwner) count++;
            }
            dungeons = count;
            MarkEvents();
        }

        private static bool DeepSeaOpen()
        {
            DeepSeaManager manager = PointEntity<DeepSeaManager>.ServerInstance;
            return manager != null && (manager.IsOpen() || manager.IsBusy());
        }

        private int CountDefend()
        {
            if (DefendableHomes == null || !DefendableHomes.IsLoaded) return defend;
            try
            {
                var property = DefendableHomes.GetType().GetProperty("Controllers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var value = property == null ? null : property.GetValue(DefendableHomes, null);
                var collection = value as System.Collections.ICollection;
                if (collection != null) return collection.Count;
            }
            catch { }
            return defend;
        }

        private void ReadEvent(string key, out int count, out bool on, out bool contested)
        {
            contested = false;
            switch (key)
            {
                case "Bradley": count = bradleys.Count; break;
                case "PatrolHeli": count = helis.Count; break;
                case "CH47": count = chinooks.Count; break;
                case "Cargo": count = cargoSailing.Count; break;
                case "AirDrop": count = drops.Count; break;
                case "Harbor": count = cargoDocked.Count; break;
                case "SmallOil": count = OilCount("SmallOil", out contested); break;
                case "LargeOil": count = OilCount("LargeOil", out contested); break;
                case "DeepSea": count = deepSea ? 1 : 0; break;
                case "Convoy": count = convoy ? 1 : 0; break;
                case "ArmoredTrain": count = train ? 1 : 0; break;
                case "Sputnik": count = sputnik ? 1 : 0; break;
                case "HarborEvent": count = harborEvent ? 1 : 0; break;
                case "Raids": count = raids; break;
                case "Dungeon": count = dungeons; break;
                case "Defend": count = defend; break;
                case "Purge": count = purge ? 1 : 0; break;
                default: count = 0; break;
            }
            on = count > 0;
        }

        private int OilCount(string key, out bool contested)
        {
            int count = 0;
            contested = false;
            foreach (var pair in oilCrates)
            {
                if (pair.Value != key) continue;
                count++;
                if (oilHacking.Contains(pair.Key)) contested = true;
            }
            return count;
        }

        private List<EventCfg> EnabledEvents()
        {
            var list = new List<EventCfg>();
            foreach (var entry in config.Events)
                if (entry != null && entry.Enabled && !string.IsNullOrEmpty(entry.Key)) list.Add(entry);
            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        private static string EventText(EventCfg entry, int count, bool on)
        {
            if (!on || !entry.ShowCount) return entry.Label;
            return entry.Label + " " + count;
        }

        private string EventColor(bool on, bool contested)
        {
            if (contested) return Hex(config.ContestedColor);
            if (on) return Hex(config.ActiveColor);
            return Hex(config.MutedColor, config.InactiveAlpha);
        }

        private static List<EventCfg> DefaultEvents()
        {
            return new List<EventCfg>
            {
                Ev("Bradley", "BRAD", true, true, 10),
                Ev("PatrolHeli", "HELI", true, true, 20),
                Ev("Cargo", "CARGO", true, true, 30),
                Ev("Harbor", "HARBOR", true, false, 40),
                Ev("AirDrop", "DROP", true, true, 50),
                Ev("CH47", "CH47", false, true, 60),
                Ev("SmallOil", "OIL-S", false, false, 70),
                Ev("LargeOil", "OIL-L", false, false, 80),
                Ev("DeepSea", "DEEP", true, false, 90),
                Ev("Convoy", "CONVOY", true, false, 100),
                Ev("ArmoredTrain", "TRAIN", true, false, 110),
                Ev("Raids", "RAIDS", true, true, 120),
                Ev("Dungeon", "DUNGEON", true, true, 130),
                Ev("Defend", "DEFEND", true, true, 140),
                Ev("Purge", "PURGE", true, false, 150),
                Ev("Sputnik", "SPUTNIK", false, false, 160),
                Ev("HarborEvent", "HARBOR-EV", false, false, 170)
            };
        }

        private static EventCfg Ev(string key, string label, bool enabled, bool count, int order)
        {
            return new EventCfg { Key = key, Label = label, Enabled = enabled, ShowCount = count, Order = order };
        }

        #endregion

        #region Helpers

        private void AddImage(CuiElementContainer container, string name, string parent, string color, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = color },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        private void AddLabel(CuiElementContainer container, string name, string parent, string text, string color, int size, TextAnchor align, string anchorMin, string anchorMax, string offsetMin, string offsetMax, bool bold = false)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    new CuiTextComponent { Text = text ?? string.Empty, FontSize = size, Align = align, Color = color, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        private static bool CallBool(Plugin plugin, string method)
        {
            if (plugin == null || !plugin.IsLoaded) return false;
            return plugin.Call(method) is bool value && value;
        }

        private static double ToDouble(object value)
        {
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            double parsed;
            return value != null && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0d;
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
                var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
                var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
                var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), new DateTime(9999, 12, 31), TimeSpan.FromHours(1), start, end);
                return TimeZoneInfo.CreateCustomTimeZone("Nightshade-Eastern", TimeSpan.FromHours(-5), "Eastern Time (fallback)", "Eastern Standard Time", "Eastern Daylight Time", new[] { rule });
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("Nightshade-Eastern-EST", TimeSpan.FromHours(-5), "Eastern Time (fixed EST)", "EST");
            }
        }

        #endregion
    }
}
