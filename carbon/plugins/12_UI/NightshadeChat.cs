using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeChat", "Rustonauts & Nightshade", "1.0.0")]
    [Description("Group tags, gradient name/chat colors, moderation and auto broadcasts for Nightshade")]
    public class NightshadeChat : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin SkillTree, XLevels;

        private const string PermAdmin = "nightshadechat.admin";
        private const string PermMute = "nightshadechat.mute";
        private const string PermBypass = "nightshadechat.bypass";
        private const string PermRichText = "nightshadechat.richtext";
        private const string PermCustomName = "nightshadechat.customname";
        private const string PermCustomGradient = "nightshadechat.customname.gradient";

        private const string UiRoot = "NightshadeChat.UI";
        private const string AutoKey = "__auto";
        private const string NoneKey = "__none";
        private const string DefaultKey = "__default";
        private const string CustomKey = "__custom";
        private const int PerPage = 8;

        private PluginConfig config;
        private StoredData data;
        private bool dataDirty;
        private bool configLoadFailed;

        private Regex filterRegex;
        private Timer broadcastTimer;
        private int broadcastIndex;
        private string systemTitleCache;

        private readonly Dictionary<string, string> tagRenderCache = new Dictionary<string, string>();
        private readonly Dictionary<ulong, SpamState> spam = new Dictionary<ulong, SpamState>();
        private readonly Dictionary<ulong, int[]> uiPages = new Dictionary<ulong, int[]>();

        private static readonly Regex RichTextRegex = new Regex(
            @"</?(color|size|b|i|u|s|material|quad|sprite|align|alpha|mark|noparse|font|voffset|pos|space|indent|line-height|cspace|mspace|width|sub|sup|lowercase|uppercase|smallcaps|link|style|gradient|rotate|margin|nobr|page)\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DurationRegex = new Regex(@"(\d+)([smhdw])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private class SpamState
        {
            public float Last;
            public string LastMsg = string.Empty;
            public int Dup;
        }

        private struct UiEntry
        {
            public string Key;
            public string Label;
            public bool Selected;
        }

        #endregion

        #region Config

        private class TagEntry
        {
            [JsonProperty("Text")] public string Text = "[TAG]";
            [JsonProperty("Colors (1 = solid, 2+ = gradient)")] public List<string> Colors;
            [JsonProperty("Priority (higher = shown first, wins Auto)")] public int Priority;
            [JsonProperty("Size (0 = game default)")] public int Size;
        }

        private class ColorEntry
        {
            [JsonProperty("Id (no spaces)")] public string Id;
            [JsonProperty("Display name")] public string Name;
            [JsonProperty("Colors (1 = solid, 2+ = gradient)")] public List<string> Colors;
            [JsonProperty("Groups allowed (empty = everyone)")] public List<string> Groups;
        }

        private class FormatSettings
        {
            [JsonProperty("Layout (placeholders: {level} {tags} {name} {message})")] public string Layout = "{level}{tags}{name}: {message}";
            [JsonProperty("Default name color(s)")] public List<string> DefaultNameColors;
            [JsonProperty("Default chat color(s)")] public List<string> DefaultChatColors;
            [JsonProperty("Name size (0 = game default)")] public int NameSize;
            [JsonProperty("Message size (0 = game default)")] public int MessageSize;
            [JsonProperty("Strip rich text from player messages (bypass perm: nightshadechat.richtext)")] public bool StripRichText = true;
            [JsonProperty("Relay team chat to Rust+ app")] public bool RelayTeamToApp = true;
            [JsonProperty("Log chat to server console")] public bool LogToConsole = true;
        }

        private class SystemSettings
        {
            [JsonProperty("Title")] public string Title = "Nightshade";
            [JsonProperty("Title colors (1 = solid, 2+ = gradient)")] public List<string> TitleColors;
            [JsonProperty("Title size (0 = game default)")] public int TitleSize;
            [JsonProperty("Message color")] public string MessageColor = "#E6DDF0";
            [JsonProperty("Avatar SteamID (0 = default)")] public ulong AvatarSteamId;
        }

        private class LevelSettings
        {
            [JsonProperty("Enabled")] public bool Enabled;
            [JsonProperty("Source (SkillTree or XLevels)")] public string Source = "SkillTree";
            [JsonProperty("Format ({0} = level)")] public string Format = "[Lv {0}]";
            [JsonProperty("Colors (1 = solid, 2+ = gradient)")] public List<string> Colors;
            [JsonProperty("Size (0 = game default)")] public int Size;
            [JsonProperty("Hide when level is 0")] public bool HideZero = true;
        }

        private class MuteSettings
        {
            [JsonProperty("Mute also blocks team chat")] public bool BlockTeamChat = true;
            [JsonProperty("Announce mutes/unmutes in global chat")] public bool Announce = true;
            [JsonProperty("Moderation panel presets (Duration: 30m, 2h, 1d, perm)")] public List<MutePreset> Presets;
        }

        private class MutePreset
        {
            [JsonProperty("Reason")] public string Reason;
            [JsonProperty("Duration")] public string Duration;
        }

        private class AntiSpamSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Seconds between messages")] public float Cooldown = 1.5f;
            [JsonProperty("Repeated identical messages before auto-mute (0 = off)")] public int DuplicateLimit = 3;
            [JsonProperty("Auto-mute minutes for repeats")] public float DuplicateMuteMinutes = 5f;
            [JsonProperty("Caps filter enabled")] public bool CapsFilter = true;
            [JsonProperty("Max caps percent")] public int MaxCapsPercent = 70;
            [JsonProperty("Min letters before caps filter applies")] public int CapsMinLetters = 8;
        }

        private class FilterSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Replacement (empty = stars)")] public string Replacement = string.Empty;
            [JsonProperty("Match inside other words")] public bool MatchInsideWords;
            [JsonProperty("Auto-mute on filtered word")] public bool AutoMute;
            [JsonProperty("Auto-mute minutes")] public float AutoMuteMinutes = 10f;
            [JsonProperty("Words")] public List<string> Words;
        }

        private class ModerationSettings
        {
            [JsonProperty("Mute")] public MuteSettings Mute = new MuteSettings();
            [JsonProperty("Anti-spam")] public AntiSpamSettings AntiSpam = new AntiSpamSettings();
            [JsonProperty("Word filter")] public FilterSettings Filter = new FilterSettings();
        }

        private class BroadcastSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Interval (minutes)")] public float IntervalMinutes = 10f;
            [JsonProperty("Random order (false = sequential)")] public bool Random;
            [JsonProperty("Messages ({online} = players online)")] public List<string> Messages;
        }

        private class CustomNameSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Max gradient colors (perm: nightshadechat.customname.gradient)")] public int MaxStops = 3;
            [JsonProperty("Minimum brightness 0-1 (blocks colors too dark to read)")] public float MinBrightness = 0.3f;
            [JsonProperty("Palette swatches")] public List<string> Palette;
        }

        private class PluginConfig
        {
            [JsonProperty("Menu chat command")] public string MenuCommand = "chat";
            [JsonProperty("UI accent color")] public string Accent = "#8B3FBF";
            [JsonProperty("UI scale (1.0 = full size, 0.75 = compact)")] public float UiScale = 0.75f;
            [JsonProperty("Chat format")] public FormatSettings Format = new FormatSettings();
            [JsonProperty("System messages")] public SystemSettings SystemMsg = new SystemSettings();
            [JsonProperty("Max tags a player can show at once")] public int MaxTags = 1;
            [JsonProperty("Group tags [oxide group name] = tag")] public Dictionary<string, TagEntry> Tags;
            [JsonProperty("Name colors")] public List<ColorEntry> NameColors;
            [JsonProperty("Chat colors")] public List<ColorEntry> ChatColors;
            [JsonProperty("Level tag")] public LevelSettings Level = new LevelSettings();
            [JsonProperty("Moderation")] public ModerationSettings Moderation = new ModerationSettings();
            [JsonProperty("Auto broadcaster")] public BroadcastSettings Broadcast = new BroadcastSettings();
            [JsonProperty("Custom name colors")] public CustomNameSettings CustomName = new CustomNameSettings();

            public static PluginConfig CreateDefault()
            {
                var c = new PluginConfig();
                c.Format.DefaultNameColors = new List<string> { "#55AAFF" };
                c.Format.DefaultChatColors = new List<string> { "#FFFFFF" };
                c.SystemMsg.TitleColors = new List<string> { "#8B3FBF", "#D17BFF" };
                c.Level.Colors = new List<string> { "#8B3FBF", "#C77DFF" };
                c.Tags = new Dictionary<string, TagEntry>
                {
                    ["admin"] = new TagEntry { Text = "[Admin]", Colors = new List<string> { "#FF3B3B", "#8B3FBF" }, Priority = 100 },
                    ["vipx"] = new TagEntry { Text = "[VIPx]", Colors = new List<string> { "#FF4FD8", "#8B3FBF" }, Priority = 60 },
                    ["vip"] = new TagEntry { Text = "[VIP]", Colors = new List<string> { "#FF69B4" }, Priority = 50 },
                    ["og"] = new TagEntry { Text = "[OG]", Colors = new List<string> { "#FFD166", "#F77F00" }, Priority = 40 }
                };
                c.NameColors = new List<ColorEntry>
                {
                    new ColorEntry { Id = "nightshade", Name = "Nightshade", Colors = new List<string> { "#8B3FBF" }, Groups = new List<string> { "vip", "vipx", "admin" } },
                    new ColorEntry { Id = "rose", Name = "Rose", Colors = new List<string> { "#FF69B4" }, Groups = new List<string> { "vip", "vipx", "admin" } },
                    new ColorEntry { Id = "gold", Name = "Gold", Colors = new List<string> { "#FFD166" }, Groups = new List<string> { "og", "admin" } },
                    new ColorEntry { Id = "twilight", Name = "Twilight", Colors = new List<string> { "#8B3FBF", "#FF4FD8" }, Groups = new List<string> { "vipx", "admin" } }
                };
                c.ChatColors = new List<ColorEntry>
                {
                    new ColorEntry { Id = "lavender", Name = "Lavender", Colors = new List<string> { "#D9B8FF" }, Groups = new List<string> { "vip", "vipx", "admin" } },
                    new ColorEntry { Id = "blush", Name = "Blush", Colors = new List<string> { "#FFC2E2" }, Groups = new List<string> { "vip", "vipx", "admin" } },
                    new ColorEntry { Id = "aurora", Name = "Aurora", Colors = new List<string> { "#C77DFF", "#7FDBFF" }, Groups = new List<string> { "vipx", "admin" } }
                };
                c.Moderation.Filter.Words = new List<string> { "examplebadword" };
                c.CustomName.Palette = new List<string>
                {
                    "#8B3FBF", "#9D4EDD", "#C77DFF", "#E0AAFF", "#D9B8FF", "#FF4FD8", "#FF69B4", "#FFC2E2",
                    "#FF3B3B", "#FF7F50", "#F77F00", "#FFD166", "#FFF3B0", "#B8F2A0", "#7CFFB2", "#00F5D4",
                    "#2EC4B6", "#7FDBFF", "#55AAFF", "#3A86FF", "#4361EE", "#FFFFFF", "#CED4DA", "#A0A0A0"
                };
                c.Moderation.Mute.Presets = new List<MutePreset>
                {
                    new MutePreset { Reason = "Spam", Duration = "15m" },
                    new MutePreset { Reason = "Caps / flooding", Duration = "30m" },
                    new MutePreset { Reason = "Toxicity", Duration = "1h" },
                    new MutePreset { Reason = "Advertising", Duration = "1d" },
                    new MutePreset { Reason = "Harassment", Duration = "3d" },
                    new MutePreset { Reason = "Slurs / hate speech", Duration = "perm" }
                };
                c.Broadcast.Messages = new List<string>
                {
                    "Type <color=#C77DFF>/chat</color> to pick your tag and colors.",
                    "Be respectful in chat. Spam and slurs earn a mute.",
                    "There are <color=#C77DFF>{online}</color> players online right now."
                };
                return c;
            }
        }

        protected override void LoadDefaultConfig() => config = PluginConfig.CreateDefault();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null) throw new Exception("config is empty");
            }
            catch (Exception e)
            {
                configLoadFailed = true;
                PrintError($"Config failed to load ({e.Message}). Using defaults in memory; your file will NOT be overwritten until fixed.");
                config = PluginConfig.CreateDefault();
            }
            NormalizeConfig();
            if (!configLoadFailed) SaveConfig();
        }

        protected override void SaveConfig()
        {
            if (configLoadFailed) return;
            Config.WriteObject(config, true);
        }

        private void NormalizeConfig()
        {
            var d = PluginConfig.CreateDefault();
            if (string.IsNullOrWhiteSpace(config.MenuCommand)) config.MenuCommand = "chat";
            if (config.Format == null) config.Format = d.Format;
            if (config.SystemMsg == null) config.SystemMsg = d.SystemMsg;
            if (config.Level == null) config.Level = d.Level;
            if (config.Moderation == null) config.Moderation = d.Moderation;
            if (config.Moderation.Mute == null) config.Moderation.Mute = d.Moderation.Mute;
            if (config.Moderation.Mute.Presets == null) config.Moderation.Mute.Presets = d.Moderation.Mute.Presets;
            config.Moderation.Mute.Presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Duration));
            if (config.Moderation.AntiSpam == null) config.Moderation.AntiSpam = d.Moderation.AntiSpam;
            if (config.Moderation.Filter == null) config.Moderation.Filter = d.Moderation.Filter;
            if (config.Broadcast == null) config.Broadcast = d.Broadcast;
            if (config.CustomName == null) config.CustomName = d.CustomName;
            if (config.CustomName.Palette == null || config.CustomName.Palette.Count == 0) config.CustomName.Palette = d.CustomName.Palette;
            if (config.CustomName.MaxStops < 1) config.CustomName.MaxStops = 1;

            if (config.Format.DefaultNameColors == null || config.Format.DefaultNameColors.Count == 0) config.Format.DefaultNameColors = d.Format.DefaultNameColors;
            if (config.Format.DefaultChatColors == null || config.Format.DefaultChatColors.Count == 0) config.Format.DefaultChatColors = d.Format.DefaultChatColors;
            if (config.SystemMsg.TitleColors == null) config.SystemMsg.TitleColors = d.SystemMsg.TitleColors;
            if (config.Level.Colors == null) config.Level.Colors = d.Level.Colors;
            if (config.Moderation.Filter.Words == null) config.Moderation.Filter.Words = new List<string>();
            if (config.Broadcast.Messages == null) config.Broadcast.Messages = new List<string>();
            if (config.NameColors == null) config.NameColors = new List<ColorEntry>();
            if (config.ChatColors == null) config.ChatColors = new List<ColorEntry>();
            if (config.MaxTags < 1) config.MaxTags = 1;
            if (config.UiScale <= 0f) config.UiScale = 0.75f;
            config.UiScale = Mathf.Clamp(config.UiScale, 0.5f, 1.25f);

            var tags = new Dictionary<string, TagEntry>();
            if (config.Tags != null)
            {
                foreach (var kv in config.Tags)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                    if (kv.Value.Colors == null || kv.Value.Colors.Count == 0) kv.Value.Colors = new List<string> { "#FFFFFF" };
                    if (kv.Value.Text == null) kv.Value.Text = string.Empty;
                    tags[kv.Key.Trim().ToLowerInvariant()] = kv.Value;
                }
            }
            config.Tags = tags;

            NormalizeColorList(config.NameColors);
            NormalizeColorList(config.ChatColors);
        }

        private static void NormalizeColorList(List<ColorEntry> list)
        {
            list.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Id));
            foreach (var e in list)
            {
                e.Id = e.Id.Trim().Replace(" ", "_");
                if (string.IsNullOrEmpty(e.Name)) e.Name = e.Id;
                if (e.Colors == null || e.Colors.Count == 0) e.Colors = new List<string> { "#FFFFFF" };
                if (e.Groups == null) e.Groups = new List<string>();
                for (int i = 0; i < e.Groups.Count; i++) e.Groups[i] = e.Groups[i].Trim().ToLowerInvariant();
            }
        }

        #endregion

        #region Data

        private class PlayerData
        {
            public List<string> Tags = new List<string>();
            public string NameColor = string.Empty;
            public string ChatColor = string.Empty;
            public bool ShowBroadcasts = true;
            public List<string> CustomNameColors;
        }

        private class MuteData
        {
            public string Name;
            public double Expires; // unix seconds, 0 = permanent
            public string Reason;
            public string By;
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();
            public Dictionary<ulong, MuteData> Mutes = new Dictionary<ulong, MuteData>();
        }

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name); }
            catch { data = null; }
            if (data == null) data = new StoredData();
            if (data.Players == null) data.Players = new Dictionary<ulong, PlayerData>();
            if (data.Mutes == null) data.Mutes = new Dictionary<ulong, MuteData>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
            dataDirty = false;
        }

        private PlayerData GetData(ulong id)
        {
            PlayerData d;
            if (!data.Players.TryGetValue(id, out d))
            {
                d = new PlayerData();
                data.Players[id] = d;
                dataDirty = true;
            }
            if (d.Tags == null) d.Tags = new List<string>();
            return d;
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to do that.",
                ["PlayerNotFound"] = "No player found matching '{0}'.",
                ["MultiplePlayers"] = "Multiple players match: {0}",
                ["MuteUsage"] = "Usage: /mute <name|steamid> <duration> [reason]  (e.g. 30m, 2h, 1d, 1h30m, perm; bare number = minutes)",
                ["UnmuteUsage"] = "Usage: /unmute <name|steamid>",
                ["BadDuration"] = "Invalid duration '{0}'. Use 30s, 10m, 2h, 1d, 1w, 1h30m or perm.",
                ["Permanent"] = "permanently",
                ["NoReason"] = "No reason given",
                ["MuteDone"] = "Muted {0} {1}. Reason: {2}",
                ["UnmuteDone"] = "Unmuted {0}.",
                ["NotMuted"] = "{0} is not muted.",
                ["YouWereMuted"] = "You have been muted {0}. Reason: {1}",
                ["YouWereUnmuted"] = "You have been unmuted.",
                ["MuteAnnounce"] = "<color=#FF6B6B>{0}</color> was muted {1}. Reason: {2}",
                ["UnmuteAnnounce"] = "<color=#7CFFB2>{0}</color> was unmuted.",
                ["YouAreMuted"] = "You are muted. Time left: {0}. Reason: {1}",
                ["YouAreMutedPerm"] = "You are permanently muted. Reason: {0}",
                ["MuteListHeader"] = "Active mutes:",
                ["MuteListEntry"] = "{0} ({1}) - {2} left - {3} (by {4})",
                ["MuteListEmpty"] = "Nobody is muted.",
                ["SlowDown"] = "Slow down! Wait {0}s before sending another message.",
                ["NoRepeat"] = "Please don't repeat the same message.",
                ["SpamReason"] = "Spamming",
                ["FilterReason"] = "Inappropriate language",
                ["MaxTags"] = "You can show up to {0} tags. Remove one first.",
                ["TagUsage"] = "Tag commands:\n/ntag list\n/ntag set <group> \"<text>\" <#hex> [#hex ...]\n/ntag color <group> <#hex> [#hex ...]\n/ntag priority <group> <number>\n/ntag size <group> <number>\n/ntag remove <group>\nOne color = solid, two or more = gradient.",
                ["TagSet"] = "Tag for group '{0}' is now: {1}",
                ["TagRemoved"] = "Removed tag for group '{0}'.",
                ["TagNotFound"] = "No tag exists for group '{0}'.",
                ["TagGroupMissing"] = "Note: Oxide group '{0}' doesn't exist yet. The tag will apply once it does.",
                ["TagListHeader"] = "Group tags (group | priority | preview):",
                ["TagListEntry"] = "{0} | {1} | {2}",
                ["TagListEmpty"] = "No group tags configured.",
                ["BadColor"] = "'{0}' is not a valid hex color (use #RRGGBB).",
                ["BadNumber"] = "'{0}' is not a valid number.",
                ["UiTitle"] = "NIGHTSHADE CHAT",
                ["UiTags"] = "TAG",
                ["UiTagsMulti"] = "TAGS ({0}/{1})",
                ["UiNameColor"] = "NAME COLOR",
                ["UiChatColor"] = "CHAT COLOR",
                ["UiSample"] = "Hello, Nightshade!",
                ["UiAuto"] = "Auto (highest rank)",
                ["UiNone"] = "No tag",
                ["UiDefault"] = "Default",
                ["UiClose"] = "X",
                ["UiBack"] = "<",
                ["UiStatusActive"] = "Chat status: <color=#7CFFB2>Active</color>",
                ["UiStatusMuted"] = "Chat status: <color=#FF6B6B>Muted</color> ({0} left)",
                ["UiStatusMutedPerm"] = "Chat status: <color=#FF6B6B>Muted</color> (permanent)",
                ["UiShowTips"] = "Show server tips",
                ["UiModeration"] = "MODERATION",
                ["UiModTitle"] = "NIGHTSHADE MODERATION",
                ["UiModPlayers"] = "ONLINE PLAYERS",
                ["UiModPick"] = "Select a player on the left.",
                ["UiModOffline"] = "That player is no longer online.",
                ["UiModNotMuted"] = "Status: <color=#7CFFB2>Not muted</color>",
                ["UiModMuted"] = "Status: <color=#FF6B6B>Muted</color> ({0} left)\nReason: {1}\nBy: {2}",
                ["UiModMutedPerm"] = "Status: <color=#FF6B6B>Muted</color> (permanent)\nReason: {0}\nBy: {1}",
                ["UiModPresets"] = "MUTE FOR:",
                ["UiModUnmute"] = "UNMUTE",
                ["UiModBadPreset"] = "Preset '{0}' has an invalid duration '{1}'. Fix it in the config.",
                ["UiCustom"] = "+ Custom color",
                ["UiCustomSet"] = "Custom",
                ["UiCnameTitle"] = "CUSTOM NAME COLOR",
                ["UiCnameColors"] = "YOUR COLORS  (click a slot, then pick a color)",
                ["UiCnameHex"] = "HEX",
                ["UiCnameHexHint"] = "Type #RRGGBB and press Enter",
                ["UiCnamePalette"] = "PALETTE",
                ["UiCnameSave"] = "SAVE",
                ["UiCnameReset"] = "REMOVE CUSTOM",
                ["UiCnameCancel"] = "CANCEL",
                ["UiCnameInfoSolid"] = "You can use one solid color. Gradients are a VIPx perk.",
                ["UiCnameInfoGradient"] = "Use up to {0} colors to make a gradient.",
                ["CnameNoPerm"] = "Custom name colors are a VIP perk.",
                ["CnameTooDark"] = "{0} is too dark to read in chat. Pick a brighter color.",
                ["CnameSaved"] = "Custom name color saved: {0}",
                ["CnameRemoved"] = "Custom name color removed.",
                ["CnameTooMany"] = "You can use up to {0} color(s).",
                ["CnameUsage"] = "/cname opens the editor.  /cname #hex [#hex ...] sets it directly.  /cname reset removes it."
            }, this);
        }

        private string Lang(string key, string userId, params object[] args)
        {
            string msg = lang.GetMessage(key, this, userId);
            return args != null && args.Length > 0 ? string.Format(msg, args) : msg;
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermMute, this);
            permission.RegisterPermission(PermBypass, this);
            permission.RegisterPermission(PermRichText, this);
            permission.RegisterPermission(PermCustomName, this);
            permission.RegisterPermission(PermCustomGradient, this);

            LoadData();
            BuildFilter();

            cmd.AddChatCommand(config.MenuCommand, this, nameof(CmdChatMenu));
            cmd.AddChatCommand("cname", this, nameof(CmdCustomName));
            AddCovalenceCommand(new[] { "mute" }, nameof(CmdMute));
            AddCovalenceCommand(new[] { "unmute" }, nameof(CmdUnmute));
            AddCovalenceCommand(new[] { "mutelist" }, nameof(CmdMuteList));
            AddCovalenceCommand(new[] { "ntag" }, nameof(CmdTag));
        }

        private void OnServerInitialized()
        {
            StartBroadcaster();
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(p, UiRoot);
            broadcastTimer?.Destroy();
            SaveData();
        }

        private void OnServerSave()
        {
            if (dataDirty) SaveData();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            ulong id = player.userID.Get();
            spam.Remove(id);
            uiPages.Remove(id);
            modPages.Remove(id);
            modTargets.Remove(id);
            cnamePending.Remove(id);
            cnameSlot.Remove(id);
        }

        #endregion

        #region Chat

        private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return null;

            bool isGlobal = channel == ConVar.Chat.ChatChannel.Global;
            bool isTeam = channel == ConVar.Chat.ChatChannel.Team;
            if (!isGlobal && !isTeam) return null;

            if (Interface.CallHook("CanChatMessage", player, message) != null) return false;

            ulong id = player.userID.Get();
            string uid = player.UserIDString;

            MuteData mute = GetActiveMute(id);
            if (mute != null && (isGlobal || config.Moderation.Mute.BlockTeamChat))
            {
                SendSystem(player, MuteStatusMessage(uid, mute));
                return false;
            }

            bool bypass = permission.UserHasPermission(uid, PermBypass);

            if (config.Format.StripRichText && !permission.UserHasPermission(uid, PermRichText))
                message = StripTags(message);

            message = message.Trim();
            if (message.Length == 0) return false;

            if (!bypass)
            {
                if (!PassesAntiSpam(player, message)) return false;
                message = ApplyCapsFilter(message);
            }

            bool filtered;
            message = ApplyWordFilter(message, out filtered);
            if (filtered && !bypass && config.Moderation.Filter.AutoMute)
            {
                ApplyMute(id, player.displayName, config.Moderation.Filter.AutoMuteMinutes * 60f, Lang("FilterReason", uid), "Auto");
                return false;
            }

            string line = BuildLine(player, message);

            if (isGlobal)
            {
                foreach (var p in BasePlayer.activePlayerList)
                    SendChat(p, channel, id, line);
            }
            else
            {
                var team = player.Team;
                if (team == null) return false;

                foreach (ulong memberId in team.members)
                {
                    var member = BasePlayer.FindByID(memberId);
                    if (member != null && member.IsConnected)
                        SendChat(member, channel, id, line);
                }

                if (config.Format.RelayTeamToApp)
                {
                    RelayTeamChat(team, id, player.displayName, message, FirstHex(ResolveNameColors(uid, GetData(id))));
                }
            }

            RecordChat(player, channel, message);
            return false;
        }

        // Rust+ team relay. Resolved by reflection so the plugin compiles on any Rust/Carbon build,
        // whether the method lives on PlayerTeam or as CompanionServer.Util extension.
        private System.Reflection.MethodInfo teamRelayMethod;
        private bool teamRelayResolved;
        private bool teamRelayStatic;

        private void RelayTeamChat(RelationshipManager.PlayerTeam team, ulong id, string name, string message, string color)
        {
            if (!teamRelayResolved)
            {
                teamRelayResolved = true;
                try
                {
                    var utilType = typeof(BasePlayer).Assembly.GetType("CompanionServer.Util");
                    teamRelayMethod = utilType?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "BroadcastTeamChat" && m.GetParameters().Length == 5);
                    teamRelayStatic = teamRelayMethod != null;

                    if (teamRelayMethod == null)
                        teamRelayMethod = typeof(RelationshipManager.PlayerTeam).GetMethods()
                            .FirstOrDefault(m => m.Name == "BroadcastTeamChat" && m.GetParameters().Length == 4);
                }
                catch { teamRelayMethod = null; }

                if (teamRelayMethod == null)
                    PrintWarning("Rust+ team chat relay isn't available on this Rust build; team chat will show in-game only.");
            }

            if (teamRelayMethod == null) return;

            try
            {
                if (teamRelayStatic) teamRelayMethod.Invoke(null, new object[] { team, id, name, message, color });
                else teamRelayMethod.Invoke(team, new object[] { id, name, message, color });
            }
            catch (Exception e)
            {
                PrintWarning($"Rust+ team relay failed and has been disabled: {e.InnerException?.Message ?? e.Message}");
                teamRelayMethod = null;
            }
        }

        private void SendChat(BasePlayer target, ConVar.Chat.ChatChannel channel, ulong senderId, string line)
        {
            target.SendConsoleCommand("chat.add", (int)channel, senderId, line);
        }

        private void RecordChat(BasePlayer player, ConVar.Chat.ChatChannel channel, string message)
        {
            string plain = StripTags(message);
            try
            {
                ConVar.Chat.Record(new ConVar.Chat.ChatEntry
                {
                    Channel = channel,
                    Message = plain,
                    UserId = player.UserIDString,
                    Username = player.displayName,
                    Time = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }
            catch { }

            if (config.Format.LogToConsole)
                Puts($"[{channel}] {player.displayName} ({player.UserIDString}): {plain}");
        }

        private string BuildLine(BasePlayer player, string message, List<string> nameColorsOverride = null)
        {
            string uid = player.UserIDString;
            PlayerData d = GetData(player.userID.Get());

            string level = GetLevelTag(player);
            string tags = string.Join(" ", GetActiveTags(uid, d).Select(RenderTag).ToArray());
            string name = Sized(Colorize(StripTags(player.displayName), (nameColorsOverride ?? ResolveNameColors(uid, d))), config.Format.NameSize);
            string msg = Sized(Colorize(message, ResolveColors(config.ChatColors, uid, d.ChatColor, config.Format.DefaultChatColors)), config.Format.MessageSize);

            return config.Format.Layout
                .Replace("{level}", level.Length > 0 ? level + " " : string.Empty)
                .Replace("{tags}", tags.Length > 0 ? tags + " " : string.Empty)
                .Replace("{name}", name)
                .Replace("{message}", msg);
        }

        #endregion

        #region Tags, Colors, Level

        private List<string> GetAvailableTags(string uid)
        {
            return config.Tags
                .Where(kv => permission.UserHasGroup(uid, kv.Key))
                .OrderByDescending(kv => kv.Value.Priority)
                .Select(kv => kv.Key)
                .ToList();
        }

        private List<string> GetActiveTags(string uid, PlayerData d)
        {
            var result = new List<string>();
            if (d.Tags.Contains(NoneKey)) return result;

            List<string> available = GetAvailableTags(uid);
            if (d.Tags.Count > 0)
            {
                foreach (string key in available)
                {
                    if (!d.Tags.Contains(key)) continue;
                    result.Add(key);
                    if (result.Count >= config.MaxTags) break;
                }
                if (result.Count > 0) return result;
            }

            if (available.Count > 0) result.Add(available[0]);
            return result;
        }

        private string RenderTag(string group)
        {
            string cached;
            if (tagRenderCache.TryGetValue(group, out cached)) return cached;

            TagEntry tag;
            if (!config.Tags.TryGetValue(group, out tag)) return string.Empty;

            cached = Sized(Colorize(tag.Text, tag.Colors), tag.Size);
            tagRenderCache[group] = cached;
            return cached;
        }

        private bool HasGroupAccess(string uid, List<string> groups)
        {
            if (groups == null || groups.Count == 0) return true;
            foreach (string g in groups)
                if (permission.UserHasGroup(uid, g)) return true;
            return false;
        }

        private List<string> ResolveColors(List<ColorEntry> list, string uid, string selected, List<string> fallback)
        {
            if (!string.IsNullOrEmpty(selected))
            {
                var entry = list.FirstOrDefault(e => e.Id.Equals(selected, StringComparison.OrdinalIgnoreCase));
                if (entry != null && HasGroupAccess(uid, entry.Groups)) return entry.Colors;
            }
            return fallback;
        }

        private int CustomStopsAllowed(string uid)
        {
            if (!config.CustomName.Enabled) return 0;
            if (permission.UserHasPermission(uid, PermCustomGradient)) return config.CustomName.MaxStops;
            if (permission.UserHasPermission(uid, PermCustomName)) return 1;
            return 0;
        }

        private List<string> ResolveNameColors(string uid, PlayerData d)
        {
            if (d.NameColor == CustomKey)
            {
                int allowed = CustomStopsAllowed(uid);
                if (allowed > 0 && d.CustomNameColors != null && d.CustomNameColors.Count > 0)
                    return d.CustomNameColors.Count <= allowed ? d.CustomNameColors : d.CustomNameColors.Take(allowed).ToList();
                return config.Format.DefaultNameColors;
            }
            return ResolveColors(config.NameColors, uid, d.NameColor, config.Format.DefaultNameColors);
        }

        private List<UiEntry> BuildNameEntries(string uid, PlayerData d)
        {
            var list = BuildColorEntries(uid, config.NameColors, d.NameColor, config.Format.DefaultNameColors);
            int allowed = CustomStopsAllowed(uid);
            if (allowed <= 0) return list;

            bool hasCustom = d.CustomNameColors != null && d.CustomNameColors.Count > 0;
            bool customActive = hasCustom && d.NameColor == CustomKey;
            if (customActive)
            {
                UiEntry first = list[0];
                first.Selected = false;
                list[0] = first;
            }

            list.Insert(1, new UiEntry
            {
                Key = CustomKey,
                Label = hasCustom ? Colorize(Lang("UiCustomSet", uid), ResolveCustomPreview(uid, d)) : Lang("UiCustom", uid),
                Selected = customActive
            });
            return list;
        }

        private List<string> ResolveCustomPreview(string uid, PlayerData d)
        {
            int allowed = Math.Max(1, CustomStopsAllowed(uid));
            return d.CustomNameColors.Count <= allowed ? d.CustomNameColors : d.CustomNameColors.Take(allowed).ToList();
        }

        private string GetLevelTag(BasePlayer player)
        {
            var s = config.Level;
            if (!s.Enabled) return string.Empty;

            int level = -1;
            try
            {
                if (s.Source.Equals("XLevels", StringComparison.OrdinalIgnoreCase))
                {
                    if (XLevels == null) return string.Empty;
                    object r = XLevels.Call("API_GetLevel", player);
                    if (r is int) level = (int)r;
                }
                else
                {
                    if (SkillTree == null) return string.Empty;
                    object r = SkillTree.Call("ST_GetPlayerLevel", player);
                    var arr = r as string[];
                    if (arr != null && arr.Length > 0) int.TryParse(arr[0], out level);
                    else if (r is int) level = (int)r;
                }
            }
            catch { return string.Empty; }

            if (level < 0 || (level == 0 && s.HideZero)) return string.Empty;
            return Sized(Colorize(string.Format(s.Format, level), s.Colors), s.Size);
        }

        #endregion

        #region Moderation

        private static double Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private MuteData GetActiveMute(ulong id)
        {
            MuteData m;
            if (!data.Mutes.TryGetValue(id, out m)) return null;
            if (m.Expires > 0 && m.Expires <= Now())
            {
                data.Mutes.Remove(id);
                dataDirty = true;
                return null;
            }
            return m;
        }

        private string MuteStatusMessage(string uid, MuteData m)
        {
            return m.Expires <= 0
                ? Lang("YouAreMutedPerm", uid, m.Reason)
                : Lang("YouAreMuted", uid, FormatDuration(m.Expires - Now(), uid), m.Reason);
        }

        private string DurationPhrase(double seconds, string uid)
        {
            return seconds <= 0 ? Lang("Permanent", uid) : "for " + FormatDuration(seconds, uid);
        }

        private void ApplyMute(ulong id, string name, double seconds, string reason, string by)
        {
            data.Mutes[id] = new MuteData
            {
                Name = name,
                Expires = seconds > 0 ? Now() + seconds : 0,
                Reason = reason,
                By = by
            };
            SaveData();

            var target = BasePlayer.FindByID(id);
            if (target != null && target.IsConnected)
                SendSystem(target, Lang("YouWereMuted", target.UserIDString, DurationPhrase(seconds, target.UserIDString), reason));

            if (config.Moderation.Mute.Announce)
                foreach (var p in BasePlayer.activePlayerList)
                    if (p.userID.Get() != id)
                        SendSystem(p, Lang("MuteAnnounce", p.UserIDString, StripTags(name), DurationPhrase(seconds, p.UserIDString), reason));
        }

        private bool RemoveMute(ulong id, string name)
        {
            if (!data.Mutes.Remove(id)) return false;
            SaveData();

            var target = BasePlayer.FindByID(id);
            if (target != null && target.IsConnected)
                SendSystem(target, Lang("YouWereUnmuted", target.UserIDString));

            if (config.Moderation.Mute.Announce)
                foreach (var p in BasePlayer.activePlayerList)
                    if (p.userID.Get() != id)
                        SendSystem(p, Lang("UnmuteAnnounce", p.UserIDString, StripTags(name)));
            return true;
        }

        private bool PassesAntiSpam(BasePlayer player, string message)
        {
            var s = config.Moderation.AntiSpam;
            if (!s.Enabled) return true;

            ulong id = player.userID.Get();
            string uid = player.UserIDString;
            SpamState st;
            if (!spam.TryGetValue(id, out st))
            {
                st = new SpamState();
                spam[id] = st;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (st.Last > 0f && now - st.Last < s.Cooldown)
            {
                SendSystem(player, Lang("SlowDown", uid, Math.Ceiling(s.Cooldown - (now - st.Last))));
                return false;
            }
            st.Last = now;

            string norm = message.ToLowerInvariant();
            if (s.DuplicateLimit > 0 && norm == st.LastMsg)
            {
                st.Dup++;
                if (st.Dup >= s.DuplicateLimit)
                {
                    st.Dup = 0;
                    if (s.DuplicateMuteMinutes > 0f)
                        ApplyMute(id, player.displayName, s.DuplicateMuteMinutes * 60f, Lang("SpamReason", uid), "Auto");
                    else
                        SendSystem(player, Lang("NoRepeat", uid));
                    return false;
                }
            }
            else
            {
                st.LastMsg = norm;
                st.Dup = 0;
            }
            return true;
        }

        private string ApplyCapsFilter(string message)
        {
            var s = config.Moderation.AntiSpam;
            if (!s.CapsFilter) return message;

            int letters = 0, upper = 0;
            foreach (char c in message)
            {
                if (!char.IsLetter(c)) continue;
                letters++;
                if (char.IsUpper(c)) upper++;
            }
            if (letters < s.CapsMinLetters || upper * 100 / letters <= s.MaxCapsPercent) return message;

            string lower = message.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        private void BuildFilter()
        {
            filterRegex = null;
            var f = config.Moderation.Filter;
            if (!f.Enabled) return;

            var words = f.Words
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => Regex.Escape(w.Trim()))
                .ToList();
            if (words.Count == 0) return;

            string joined = string.Join("|", words.ToArray());
            string pattern = f.MatchInsideWords ? "(" + joined + ")" : @"\b(" + joined + @")\b";
            filterRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private string ApplyWordFilter(string message, out bool hit)
        {
            hit = false;
            if (filterRegex == null) return message;

            bool found = false;
            string repl = config.Moderation.Filter.Replacement;
            string result = filterRegex.Replace(message, m =>
            {
                found = true;
                return string.IsNullOrEmpty(repl) ? new string('*', m.Length) : repl;
            });
            hit = found;
            return result;
        }

        private bool API_IsMuted(ulong id) => GetActiveMute(id) != null;

        #endregion

        #region Commands

        private bool HasPerm(IPlayer p, string perm) => p.IsServer || p.HasPermission(perm);

        private void Reply(IPlayer p, string key, params object[] args)
        {
            string msg = Lang(key, p.IsServer ? null : p.Id, args);
            var bp = p.Object as BasePlayer;
            if (!p.IsServer && bp != null) SendSystem(bp, msg);
            else p.Reply(StripTags(msg));
        }

        private void ReplyRaw(IPlayer p, string msg)
        {
            var bp = p.Object as BasePlayer;
            if (!p.IsServer && bp != null) SendSystem(bp, msg);
            else p.Reply(StripTags(msg));
        }

        private IPlayer FindTarget(IPlayer caller, string input)
        {
            var matches = covalence.Players.FindPlayers(input).ToList();
            if (matches.Count == 0) { Reply(caller, "PlayerNotFound", input); return null; }
            if (matches.Count == 1) return matches[0];

            var exact = matches.Where(p => p.Name != null && p.Name.Equals(input, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];

            var online = matches.Where(p => p.IsConnected).ToList();
            if (online.Count == 1) return online[0];

            Reply(caller, "MultiplePlayers", string.Join(", ", matches.Take(6).Select(p => p.Name).ToArray()));
            return null;
        }

        private void CmdMute(IPlayer caller, string command, string[] args)
        {
            if (!HasPerm(caller, PermMute)) { Reply(caller, "NoPermission"); return; }
            if (args.Length < 2) { Reply(caller, "MuteUsage"); return; }

            IPlayer target = FindTarget(caller, args[0]);
            if (target == null) return;

            double seconds;
            if (!TryParseDuration(args[1], out seconds)) { Reply(caller, "BadDuration", args[1]); return; }

            string callerId = caller.IsServer ? null : caller.Id;
            string reason = args.Length > 2 ? string.Join(" ", args.Skip(2).ToArray()) : Lang("NoReason", callerId);

            ulong id;
            if (!ulong.TryParse(target.Id, out id)) { Reply(caller, "PlayerNotFound", args[0]); return; }

            ApplyMute(id, target.Name, seconds, reason, caller.IsServer ? "Console" : caller.Name);
            Reply(caller, "MuteDone", target.Name, DurationPhrase(seconds, callerId), reason);
        }

        private void CmdUnmute(IPlayer caller, string command, string[] args)
        {
            if (!HasPerm(caller, PermMute)) { Reply(caller, "NoPermission"); return; }
            if (args.Length < 1) { Reply(caller, "UnmuteUsage"); return; }

            IPlayer target = FindTarget(caller, args[0]);
            if (target == null) return;

            ulong id;
            if (!ulong.TryParse(target.Id, out id)) { Reply(caller, "PlayerNotFound", args[0]); return; }

            if (RemoveMute(id, target.Name)) Reply(caller, "UnmuteDone", target.Name);
            else Reply(caller, "NotMuted", target.Name);
        }

        private void CmdMuteList(IPlayer caller, string command, string[] args)
        {
            if (!HasPerm(caller, PermMute)) { Reply(caller, "NoPermission"); return; }

            string callerId = caller.IsServer ? null : caller.Id;
            var active = data.Mutes.Keys.ToList().Where(id => GetActiveMute(id) != null).ToList();
            if (active.Count == 0) { Reply(caller, "MuteListEmpty"); return; }

            var sb = new StringBuilder(Lang("MuteListHeader", callerId));
            foreach (ulong id in active)
            {
                MuteData m = data.Mutes[id];
                string left = m.Expires <= 0 ? Lang("Permanent", callerId) : FormatDuration(m.Expires - Now(), callerId);
                sb.Append('\n').Append(Lang("MuteListEntry", callerId, StripTags(m.Name ?? "?"), id, left, m.Reason, m.By));
            }
            ReplyRaw(caller, sb.ToString());
        }

        private void CmdTag(IPlayer caller, string command, string[] args)
        {
            if (!HasPerm(caller, PermAdmin)) { Reply(caller, "NoPermission"); return; }
            if (args.Length == 0) { Reply(caller, "TagUsage"); return; }

            string callerId = caller.IsServer ? null : caller.Id;
            string sub = args[0].ToLowerInvariant();

            if (sub == "list")
            {
                if (config.Tags.Count == 0) { Reply(caller, "TagListEmpty"); return; }
                var sb = new StringBuilder(Lang("TagListHeader", callerId));
                foreach (var kv in config.Tags.OrderByDescending(k => k.Value.Priority))
                    sb.Append('\n').Append(Lang("TagListEntry", callerId, kv.Key, kv.Value.Priority, RenderTag(kv.Key)));
                ReplyRaw(caller, sb.ToString());
                return;
            }

            if (args.Length < 2) { Reply(caller, "TagUsage"); return; }
            string group = args[1].Trim().ToLowerInvariant();
            TagEntry tag;
            config.Tags.TryGetValue(group, out tag);

            switch (sub)
            {
                case "set":
                {
                    if (args.Length < 3) { Reply(caller, "TagUsage"); return; }
                    List<string> colors;
                    if (!ParseColorArgs(caller, args, 3, out colors)) return;

                    if (tag == null)
                    {
                        tag = new TagEntry { Colors = new List<string> { "#FFFFFF" } };
                        config.Tags[group] = tag;
                    }
                    tag.Text = args[2];
                    if (colors.Count > 0) tag.Colors = colors;
                    break;
                }
                case "color":
                case "colors":
                {
                    if (tag == null) { Reply(caller, "TagNotFound", group); return; }
                    List<string> colors;
                    if (!ParseColorArgs(caller, args, 2, out colors)) return;
                    if (colors.Count == 0) { Reply(caller, "TagUsage"); return; }
                    tag.Colors = colors;
                    break;
                }
                case "priority":
                case "size":
                {
                    if (tag == null) { Reply(caller, "TagNotFound", group); return; }
                    int n;
                    if (args.Length < 3 || !int.TryParse(args[2], out n)) { Reply(caller, "BadNumber", args.Length > 2 ? args[2] : ""); return; }
                    if (sub == "priority") tag.Priority = n;
                    else tag.Size = Math.Max(0, n);
                    break;
                }
                case "remove":
                case "delete":
                {
                    if (tag == null) { Reply(caller, "TagNotFound", group); return; }
                    config.Tags.Remove(group);
                    tagRenderCache.Remove(group);
                    SaveConfig();
                    Reply(caller, "TagRemoved", group);
                    return;
                }
                default:
                    Reply(caller, "TagUsage");
                    return;
            }

            tagRenderCache.Remove(group);
            SaveConfig();
            ReplyRaw(caller, Lang("TagSet", callerId, group, RenderTag(group)));
            if (!permission.GroupExists(group)) Reply(caller, "TagGroupMissing", group);
        }

        private bool ParseColorArgs(IPlayer caller, string[] args, int start, out List<string> colors)
        {
            colors = new List<string>();
            for (int i = start; i < args.Length; i++)
            {
                Color32 c;
                if (!TryParseHex(args[i], out c)) { Reply(caller, "BadColor", args[i]); return false; }
                colors.Add(ToHex(c));
            }
            return true;
        }

        private void CmdChatMenu(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            DrawUi(player);
        }

        #endregion

        #region Broadcaster

        private void StartBroadcaster()
        {
            broadcastTimer?.Destroy();
            var b = config.Broadcast;
            if (!b.Enabled || b.Messages.Count == 0) return;

            broadcastTimer = timer.Every(Mathf.Max(0.5f, b.IntervalMinutes) * 60f, () =>
            {
                if (b.Messages.Count == 0 || BasePlayer.activePlayerList.Count == 0) return;

                string msg = b.Random
                    ? b.Messages[UnityEngine.Random.Range(0, b.Messages.Count)]
                    : b.Messages[broadcastIndex++ % b.Messages.Count];

                msg = msg.Replace("{online}", BasePlayer.activePlayerList.Count.ToString());
                foreach (var p in BasePlayer.activePlayerList)
                {
                    PlayerData pd;
                    if (data.Players.TryGetValue(p.userID.Get(), out pd) && !pd.ShowBroadcasts) continue;
                    SendSystem(p, msg);
                }
            });
        }

        private void SendSystem(BasePlayer player, string msg)
        {
            if (player == null || !player.IsConnected) return;

            if (systemTitleCache == null)
                systemTitleCache = string.IsNullOrEmpty(config.SystemMsg.Title)
                    ? string.Empty
                    : Sized(Colorize(config.SystemMsg.Title, config.SystemMsg.TitleColors), config.SystemMsg.TitleSize);

            string body = $"<color={SafeHex(config.SystemMsg.MessageColor)}>{msg}</color>";
            string line = systemTitleCache.Length > 0 ? $"{systemTitleCache}: {body}" : body;
            player.SendConsoleCommand("chat.add", (int)ConVar.Chat.ChatChannel.Global, config.SystemMsg.AvatarSteamId, line);
        }

        #endregion

        #region UI

        private void DrawUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiRoot);

            ulong id = player.userID.Get();
            string uid = player.UserIDString;
            PlayerData d = GetData(id);
            int[] pages;
            if (!uiPages.TryGetValue(id, out pages)) { pages = new int[3]; uiPages[id] = pages; }

            string accent = config.Accent;
            var c = new CuiElementContainer();
            string main = AddFrame(c, Lang("UiTitle", uid), null, uid);

            // Preview
            string preview = main + ".Preview";
            c.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.09 0.15 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 430", OffsetMax = "748 476" }
            }, main, preview);
            c.Add(new CuiPanel
            {
                Image = { Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "3 0" }
            }, preview);
            c.Add(new CuiLabel
            {
                Text = { Text = BuildLine(player, Lang("UiSample", uid)), FontSize = 16, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "14 0", OffsetMax = "-10 0" }
            }, preview);

            // Columns
            string tagTitle = config.MaxTags > 1
                ? Lang("UiTagsMulti", uid, d.Tags.Count(t => t != NoneKey), config.MaxTags)
                : Lang("UiTags", uid);

            AddColumn(c, main, 12, 0, tagTitle, BuildTagEntries(uid, d), pages);
            AddColumn(c, main, 263, 1, Lang("UiNameColor", uid), BuildNameEntries(uid, d), pages);
            AddColumn(c, main, 514, 2, Lang("UiChatColor", uid), BuildColorEntries(uid, config.ChatColors, d.ChatColor, config.Format.DefaultChatColors), pages);

            // Status bar
            c.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.09 0.15 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 8", OffsetMax = "748 44" }
            }, main, main + ".Footer");

            MuteData mute = GetActiveMute(id);
            string status = mute == null
                ? Lang("UiStatusActive", uid)
                : mute.Expires <= 0 ? Lang("UiStatusMutedPerm", uid) : Lang("UiStatusMuted", uid, FormatDuration(mute.Expires - Now(), uid));
            c.Add(new CuiLabel
            {
                Text = { Text = status, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.9 0.87 0.94 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "12 0", OffsetMax = "280 0" }
            }, main + ".Footer");

            string tipsBtn = main + ".Tips";
            c.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "nchat.ui tips" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "290 0", OffsetMax = "490 0" },
                Text = { Text = string.Empty }
            }, main + ".Footer", tipsBtn);
            c.Add(new CuiPanel
            {
                Image = { Color = d.ShowBroadcasts ? HexToCui(accent, 1f) : "0.3 0.28 0.33 1" },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -8", OffsetMax = "16 8" }
            }, tipsBtn);
            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiShowTips", uid), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.9 0.87 0.94 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "24 0", OffsetMax = "0 0" }
            }, tipsBtn);

            if (permission.UserHasPermission(uid, PermMute))
                c.Add(new CuiButton
                {
                    Button = { Color = HexToCui(accent, 0.85f), Command = "nchat.mod open" },
                    RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = "-150 5", OffsetMax = "-6 -5" },
                    Text = { Text = Lang("UiModeration", uid), FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main + ".Footer");

            ShowUi(player, c);
        }

        // Scales every offset by UiScale and fonts by a gentler factor, so all screens shrink evenly.
        private void ShowUi(BasePlayer player, CuiElementContainer c)
        {
            float s = config.UiScale;
            if (!Mathf.Approximately(s, 1f))
            {
                float fs = Mathf.Lerp(1f, s, 0.6f);
                foreach (CuiElement el in c)
                {
                    if (el.Components == null) continue;
                    foreach (var comp in el.Components)
                    {
                        var rt = comp as CuiRectTransformComponent;
                        if (rt != null)
                        {
                            rt.OffsetMin = ScalePair(rt.OffsetMin, s);
                            rt.OffsetMax = ScalePair(rt.OffsetMax, s);
                            continue;
                        }
                        var text = comp as CuiTextComponent;
                        if (text != null)
                        {
                            text.FontSize = Math.Max(9, Mathf.RoundToInt(text.FontSize * fs));
                            continue;
                        }
                        var input = comp as CuiInputFieldComponent;
                        if (input != null)
                            input.FontSize = Math.Max(9, Mathf.RoundToInt(input.FontSize * fs));
                    }
                }
            }
            CuiHelper.AddUi(player, c);
        }

        private static string ScalePair(string pair, float s)
        {
            if (string.IsNullOrEmpty(pair)) return pair;
            string[] parts = pair.Split(' ');
            if (parts.Length != 2) return pair;
            float x, y;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return pair;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#}", x * s, y * s);
        }

        private string AddFrame(CuiElementContainer c, string title, string backCommand, string uid)
        {
            string accent = config.Accent;

            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.6" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            c.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "nchat.ui close" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = string.Empty }
            }, UiRoot);

            string main = UiRoot + ".Main";
            c.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.05 0.09 0.98" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-380 -262", OffsetMax = "380 262" }
            }, UiRoot, main);

            string header = main + ".Header";
            c.Add(new CuiPanel
            {
                Image = { Color = HexToCui(accent, 0.92f) },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -40", OffsetMax = "0 0" }
            }, main, header);

            int titleLeft = 16;
            if (!string.IsNullOrEmpty(backCommand))
            {
                c.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0.3", Command = backCommand },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "40 0" },
                    Text = { Text = Lang("UiBack", uid), FontSize = 18, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, header);
                titleLeft = 52;
            }

            c.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = 18, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{titleLeft} 0", OffsetMax = "-50 0" }
            }, header);
            c.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0.3", Command = "nchat.ui close" },
                RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = "-40 0", OffsetMax = "0 0" },
                Text = { Text = Lang("UiClose", uid), FontSize = 16, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, header);

            return main;
        }

        private void AddColumn(CuiElementContainer c, string parent, int x, int col, string title, List<UiEntry> entries, int[] pages)
        {
            const int width = 234;
            string accent = config.Accent;

            c.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x + 2} 392", OffsetMax = $"{x + width} 420" }
            }, parent);

            int pageCount = Math.Max(1, (entries.Count + PerPage - 1) / PerPage);
            pages[col] = Mathf.Clamp(pages[col], 0, pageCount - 1);
            int start = pages[col] * PerPage;

            for (int i = 0; i < PerPage && start + i < entries.Count; i++)
            {
                UiEntry e = entries[start + i];
                int top = 384 - i * 37;
                int bottom = top - 33;
                string btn = $"{UiRoot}.C{col}.E{i}";

                c.Add(new CuiButton
                {
                    Button = { Color = e.Selected ? HexToCui(accent, 0.45f) : "0.15 0.12 0.19 0.95", Command = $"nchat.ui sel {col} {e.Key}" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x} {bottom}", OffsetMax = $"{x + width} {top}" },
                    Text = { Text = string.Empty }
                }, parent, btn);

                if (e.Selected)
                    c.Add(new CuiPanel
                    {
                        Image = { Color = HexToCui(accent, 1f) },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "3 0" }
                    }, btn);

                c.Add(new CuiLabel
                {
                    Text = { Text = e.Label, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "8 0", OffsetMax = "-8 0" }
                }, btn);
            }

            if (pageCount <= 1) return;

            c.Add(new CuiButton
            {
                Button = { Color = "0.15 0.12 0.19 0.95", Command = $"nchat.ui page {col} -1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x} 54", OffsetMax = $"{x + 40} 80" },
                Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, parent);
            c.Add(new CuiLabel
            {
                Text = { Text = $"{pages[col] + 1}/{pageCount}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.75 0.85 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x + 44} 54", OffsetMax = $"{x + width - 44} 80" }
            }, parent);
            c.Add(new CuiButton
            {
                Button = { Color = "0.15 0.12 0.19 0.95", Command = $"nchat.ui page {col} 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x + width - 40} 54", OffsetMax = $"{x + width} 80" },
                Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, parent);
        }

        private List<UiEntry> BuildTagEntries(string uid, PlayerData d)
        {
            var list = new List<UiEntry>
            {
                new UiEntry { Key = AutoKey, Label = Lang("UiAuto", uid), Selected = d.Tags.Count == 0 },
                new UiEntry { Key = NoneKey, Label = Lang("UiNone", uid), Selected = d.Tags.Contains(NoneKey) }
            };
            foreach (string group in GetAvailableTags(uid))
                list.Add(new UiEntry { Key = group, Label = RenderTag(group), Selected = d.Tags.Contains(group) });
            return list;
        }

        private List<UiEntry> BuildColorEntries(string uid, List<ColorEntry> source, string selected, List<string> defaults)
        {
            var available = source.Where(e => HasGroupAccess(uid, e.Groups)).ToList();
            bool selectedValid = !string.IsNullOrEmpty(selected) &&
                                 available.Any(e => e.Id.Equals(selected, StringComparison.OrdinalIgnoreCase));

            var list = new List<UiEntry>
            {
                new UiEntry { Key = DefaultKey, Label = Colorize(Lang("UiDefault", uid), defaults), Selected = !selectedValid }
            };
            foreach (var e in available)
                list.Add(new UiEntry
                {
                    Key = e.Id,
                    Label = Colorize(e.Name, e.Colors),
                    Selected = selectedValid && e.Id.Equals(selected, StringComparison.OrdinalIgnoreCase)
                });
            return list;
        }

        [ConsoleCommand("nchat.ui")]
        private void CcmdUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            string action = arg.GetString(0);
            if (action == "close")
            {
                CuiHelper.DestroyUi(player, UiRoot);
                return;
            }

            ulong id = player.userID.Get();

            if (action == "tips")
            {
                PlayerData pd = GetData(id);
                pd.ShowBroadcasts = !pd.ShowBroadcasts;
                dataDirty = true;
                DrawUi(player);
                return;
            }

            int col = arg.GetInt(1);
            if (col < 0 || col > 2) return;

            if (action == "page")
            {
                int[] pages;
                if (!uiPages.TryGetValue(id, out pages)) { pages = new int[3]; uiPages[id] = pages; }
                pages[col] = Math.Max(0, pages[col] + arg.GetInt(2));
            }
            else if (action == "sel")
            {
                if (col == 1 && arg.GetString(2) == CustomKey)
                {
                    OpenCustomEditor(player);
                    return;
                }
                HandleSelect(player, col, arg.GetString(2));
            }

            DrawUi(player);
        }

        private void HandleSelect(BasePlayer player, int col, string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            string uid = player.UserIDString;
            PlayerData d = GetData(player.userID.Get());

            if (col == 0)
            {
                if (key == AutoKey) d.Tags.Clear();
                else if (key == NoneKey) { d.Tags.Clear(); d.Tags.Add(NoneKey); }
                else
                {
                    if (!config.Tags.ContainsKey(key) || !permission.UserHasGroup(uid, key)) return;
                    d.Tags.Remove(NoneKey);
                    var available = GetAvailableTags(uid);
                    d.Tags.RemoveAll(t => !available.Contains(t));

                    if (config.MaxTags <= 1)
                    {
                        d.Tags.Clear();
                        d.Tags.Add(key);
                    }
                    else if (d.Tags.Contains(key)) d.Tags.Remove(key);
                    else if (d.Tags.Count >= config.MaxTags)
                    {
                        SendSystem(player, Lang("MaxTags", uid, config.MaxTags));
                        return;
                    }
                    else d.Tags.Add(key);
                }
            }
            else
            {
                List<ColorEntry> source = col == 1 ? config.NameColors : config.ChatColors;
                string value = string.Empty;
                if (key != DefaultKey)
                {
                    var entry = source.FirstOrDefault(e => e.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (entry == null || !HasGroupAccess(uid, entry.Groups)) return;
                    value = entry.Id;
                }
                if (col == 1) d.NameColor = value;
                else d.ChatColor = value;
            }

            dataDirty = true;
        }

        #endregion

        #region Custom Name Editor

        private readonly Dictionary<ulong, List<string>> cnamePending = new Dictionary<ulong, List<string>>();
        private readonly Dictionary<ulong, int> cnameSlot = new Dictionary<ulong, int>();

        private static float Brightness(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private void CmdCustomName(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            string uid = player.UserIDString;
            int allowed = CustomStopsAllowed(uid);
            if (allowed <= 0) { SendSystem(player, Lang("CnameNoPerm", uid)); return; }

            if (args.Length == 0) { OpenCustomEditor(player); return; }

            string first = args[0].ToLowerInvariant();
            if (first == "reset" || first == "remove") { RemoveCustomName(player); return; }
            if (first == "help") { SendSystem(player, Lang("CnameUsage", uid)); return; }
            if (args.Length > allowed) { SendSystem(player, Lang("CnameTooMany", uid, allowed)); return; }

            var colors = new List<string>();
            foreach (string a in args)
            {
                string hex;
                if (!ValidateCustomColor(player, a, out hex)) return;
                colors.Add(hex);
            }
            SaveCustomName(player, colors);
        }

        private bool ValidateCustomColor(BasePlayer player, string input, out string hex)
        {
            hex = null;
            Color32 col;
            if (!TryParseHex(input, out col))
            {
                SendSystem(player, Lang("BadColor", player.UserIDString, input));
                return false;
            }
            hex = ToHex(col);
            if (Brightness(col) < config.CustomName.MinBrightness)
            {
                SendSystem(player, Lang("CnameTooDark", player.UserIDString, hex));
                return false;
            }
            return true;
        }

        private void SaveCustomName(BasePlayer player, List<string> colors)
        {
            string uid = player.UserIDString;
            int allowed = CustomStopsAllowed(uid);
            if (allowed <= 0 || colors == null || colors.Count == 0) return;
            if (colors.Count > allowed) colors = colors.Take(allowed).ToList();

            PlayerData d = GetData(player.userID.Get());
            d.CustomNameColors = new List<string>(colors);
            d.NameColor = CustomKey;
            dataDirty = true;

            SendSystem(player, Lang("CnameSaved", uid, Colorize(StripTags(player.displayName), colors)));
        }

        private void RemoveCustomName(BasePlayer player)
        {
            PlayerData d = GetData(player.userID.Get());
            d.CustomNameColors = null;
            if (d.NameColor == CustomKey) d.NameColor = string.Empty;
            dataDirty = true;
            SendSystem(player, Lang("CnameRemoved", player.UserIDString));
        }

        private void OpenCustomEditor(BasePlayer player)
        {
            string uid = player.UserIDString;
            int allowed = CustomStopsAllowed(uid);
            if (allowed <= 0)
            {
                SendSystem(player, Lang("CnameNoPerm", uid));
                return;
            }

            ulong id = player.userID.Get();
            PlayerData d = GetData(id);
            List<string> start = d.CustomNameColors != null && d.CustomNameColors.Count > 0
                ? d.CustomNameColors.Take(allowed).ToList()
                : new List<string> { FirstHex(ResolveNameColors(uid, d)) };

            cnamePending[id] = start;
            cnameSlot[id] = 0;
            DrawCustomUi(player);
        }

        private void DrawCustomUi(BasePlayer player)
        {
            ulong id = player.userID.Get();
            string uid = player.UserIDString;
            List<string> pending;
            if (!cnamePending.TryGetValue(id, out pending) || pending.Count == 0) return;

            int allowed = CustomStopsAllowed(uid);
            int slot;
            cnameSlot.TryGetValue(id, out slot);
            slot = Mathf.Clamp(slot, 0, pending.Count - 1);

            CuiHelper.DestroyUi(player, UiRoot);
            string accent = config.Accent;
            var c = new CuiElementContainer();
            string main = AddFrame(c, Lang("UiCnameTitle", uid), "nchat.cname cancel", uid);

            // Preview
            string preview = main + ".Preview";
            c.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.09 0.15 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 430", OffsetMax = "748 476" }
            }, main, preview);
            c.Add(new CuiPanel
            {
                Image = { Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "3 0" }
            }, preview);
            c.Add(new CuiLabel
            {
                Text = { Text = BuildLine(player, Lang("UiSample", uid), pending), FontSize = 16, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "14 0", OffsetMax = "-10 0" }
            }, preview);

            // Color slots
            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiCnameColors", uid), FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "14 392", OffsetMax = "748 420" }
            }, main);

            for (int i = 0; i < pending.Count; i++)
            {
                int x = 12 + i * 124;
                if (i == slot)
                    c.Add(new CuiPanel
                    {
                        Image = { Color = HexToCui(accent, 1f) },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x - 3} 337", OffsetMax = $"{x + 119} 387" }
                    }, main);

                Color32 col;
                TryParseHex(pending[i], out col);
                c.Add(new CuiButton
                {
                    Button = { Color = HexToCui(pending[i], 1f), Command = $"nchat.cname slot {i}" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x} 340", OffsetMax = $"{x + 116} 384" },
                    Text = { Text = pending[i], FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = Brightness(col) > 0.6f ? "0.05 0.05 0.05 1" : "1 1 1 1" }
                }, main);
            }

            int nextX = 12 + pending.Count * 124;
            if (pending.Count < allowed)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = "0.2 0.15 0.26 1", Command = "nchat.cname add" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{nextX} 340", OffsetMax = $"{nextX + 44} 384" },
                    Text = { Text = "+", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main);
                nextX += 52;
            }
            if (pending.Count > 1)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = "0.2 0.15 0.26 1", Command = "nchat.cname remove" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{nextX} 340", OffsetMax = $"{nextX + 44} 384" },
                    Text = { Text = "-", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main);
            }

            c.Add(new CuiLabel
            {
                Text = { Text = allowed > 1 ? Lang("UiCnameInfoGradient", uid, allowed) : Lang("UiCnameInfoSolid", uid), FontSize = 12, Align = TextAnchor.MiddleRight, Color = "0.75 0.7 0.8 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "500 340", OffsetMax = "748 384" }
            }, main);

            // Hex input
            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiCnameHex", uid), FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "14 290", OffsetMax = "60 320" }
            }, main);
            c.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.12 0.19 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "64 290", OffsetMax = "244 320" }
            }, main);
            c.Add(new CuiElement
            {
                Parent = main,
                Name = main + ".HexInput",
                Components =
                {
                    new CuiInputFieldComponent { Command = "nchat.cname hex", FontSize = 14, Align = TextAnchor.MiddleLeft, CharsLimit = 7, Color = "1 1 1 1", Text = string.Empty },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "72 290", OffsetMax = "240 320" }
                }
            });
            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiCnameHexHint", uid), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.6 0.56 0.66 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "254 290", OffsetMax = "748 320" }
            }, main);

            // Palette
            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiCnamePalette", uid), FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "14 250", OffsetMax = "748 276" }
            }, main);

            var palette = config.CustomName.Palette;
            for (int i = 0; i < palette.Count && i < 24; i++)
            {
                Color32 col;
                if (!TryParseHex(palette[i], out col)) continue;
                int x = 12 + (i % 12) * 61;
                int top = 244 - (i / 12) * 46;
                c.Add(new CuiButton
                {
                    Button = { Color = HexToCui(palette[i], 1f), Command = $"nchat.cname pick {ToHex(col)}" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x} {top - 40}", OffsetMax = $"{x + 56} {top}" },
                    Text = { Text = string.Empty }
                }, main);
            }

            // Actions
            c.Add(new CuiButton
            {
                Button = { Color = HexToCui(accent, 0.9f), Command = "nchat.cname save" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 40", OffsetMax = "252 84" },
                Text = { Text = Lang("UiCnameSave", uid), FontSize = 15, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, main);

            PlayerData d = GetData(id);
            if (d.CustomNameColors != null && d.CustomNameColors.Count > 0)
                c.Add(new CuiButton
                {
                    Button = { Color = "0.35 0.1 0.13 1", Command = "nchat.cname reset" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "262 40", OffsetMax = "502 84" },
                    Text = { Text = Lang("UiCnameReset", uid), FontSize = 15, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main);

            c.Add(new CuiButton
            {
                Button = { Color = "0.15 0.12 0.19 1", Command = "nchat.cname cancel" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "512 40", OffsetMax = "748 84" },
                Text = { Text = Lang("UiCnameCancel", uid), FontSize = 15, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, main);

            ShowUi(player, c);
        }

        [ConsoleCommand("nchat.cname")]
        private void CcmdCustomName(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            ulong id = player.userID.Get();
            string uid = player.UserIDString;
            string action = arg.GetString(0);

            if (CustomStopsAllowed(uid) <= 0)
            {
                SendSystem(player, Lang("CnameNoPerm", uid));
                CuiHelper.DestroyUi(player, UiRoot);
                return;
            }

            if (action == "open") { OpenCustomEditor(player); return; }
            if (action == "cancel")
            {
                cnamePending.Remove(id);
                cnameSlot.Remove(id);
                DrawUi(player);
                return;
            }

            List<string> pending;
            if (!cnamePending.TryGetValue(id, out pending) || pending.Count == 0) { OpenCustomEditor(player); return; }

            int allowed = CustomStopsAllowed(uid);
            int slot;
            cnameSlot.TryGetValue(id, out slot);
            slot = Mathf.Clamp(slot, 0, pending.Count - 1);

            switch (action)
            {
                case "slot":
                    cnameSlot[id] = Mathf.Clamp(arg.GetInt(1), 0, pending.Count - 1);
                    break;

                case "add":
                    if (pending.Count < allowed)
                    {
                        pending.Add(pending[pending.Count - 1]);
                        cnameSlot[id] = pending.Count - 1;
                    }
                    break;

                case "remove":
                    if (pending.Count > 1)
                    {
                        pending.RemoveAt(slot);
                        cnameSlot[id] = Mathf.Clamp(slot - 1, 0, pending.Count - 1);
                    }
                    break;

                case "pick":
                case "hex":
                {
                    string input = arg.Args != null && arg.Args.Length > 1 ? string.Join(string.Empty, arg.Args.Skip(1).ToArray()).Trim() : string.Empty;
                    if (input.Length == 0) break;
                    string hex;
                    if (ValidateCustomColor(player, input, out hex)) pending[slot] = hex;
                    break;
                }

                case "save":
                    SaveCustomName(player, pending);
                    cnamePending.Remove(id);
                    cnameSlot.Remove(id);
                    DrawUi(player);
                    return;

                case "reset":
                    RemoveCustomName(player);
                    cnamePending.Remove(id);
                    cnameSlot.Remove(id);
                    DrawUi(player);
                    return;

                default:
                    return;
            }

            DrawCustomUi(player);
        }

        #endregion

        #region Moderation UI

        private const int ModPerPage = 20;
        private readonly Dictionary<ulong, int> modPages = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, ulong> modTargets = new Dictionary<ulong, ulong>();

        private void DrawModUi(BasePlayer mod)
        {
            CuiHelper.DestroyUi(mod, UiRoot);

            ulong modId = mod.userID.Get();
            string uid = mod.UserIDString;
            string accent = config.Accent;
            var c = new CuiElementContainer();
            string main = AddFrame(c, Lang("UiModTitle", uid), "nchat.mod back", uid);

            // Player list
            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiModPlayers", uid), FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "14 452", OffsetMax = "464 478" }
            }, main);

            var players = BasePlayer.activePlayerList
                .Where(p => p != null && p.IsConnected)
                .OrderBy(p => StripTags(p.displayName), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pageCount = Math.Max(1, (players.Count + ModPerPage - 1) / ModPerPage);
            int page;
            modPages.TryGetValue(modId, out page);
            page = Mathf.Clamp(page, 0, pageCount - 1);
            modPages[modId] = page;

            ulong targetId;
            bool hasTarget = modTargets.TryGetValue(modId, out targetId);

            for (int i = 0; i < ModPerPage && page * ModPerPage + i < players.Count; i++)
            {
                BasePlayer p = players[page * ModPerPage + i];
                ulong pid = p.userID.Get();
                bool muted = GetActiveMute(pid) != null;
                bool selected = hasTarget && pid == targetId;

                int x = i < 10 ? 12 : 242;
                int row = i % 10;
                int top = 444 - row * 38;
                int bottom = top - 34;

                string name = StripTags(p.displayName);
                if (name.Length > 22) name = name.Substring(0, 20) + "..";

                c.Add(new CuiButton
                {
                    Button =
                    {
                        Color = selected ? HexToCui(accent, 0.45f) : muted ? "0.35 0.1 0.13 0.95" : "0.15 0.12 0.19 0.95",
                        Command = $"nchat.mod pick {pid}"
                    },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{x} {bottom}", OffsetMax = $"{x + 222} {top}" },
                    Text = { Text = muted ? $"{name}  <color=#FF6B6B>(muted)</color>" : name, FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main);
            }

            if (pageCount > 1)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = "0.15 0.12 0.19 0.95", Command = "nchat.mod page -1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 22", OffsetMax = "52 48" },
                    Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main);
                c.Add(new CuiLabel
                {
                    Text = { Text = $"{page + 1}/{pageCount}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.75 0.85 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "56 22", OffsetMax = "420 48" }
                }, main);
                c.Add(new CuiButton
                {
                    Button = { Color = "0.15 0.12 0.19 0.95", Command = "nchat.mod page 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "424 22", OffsetMax = "464 48" },
                    Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, main);
            }

            // Detail panel
            string detail = main + ".Detail";
            c.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.09 0.15 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "480 22", OffsetMax = "748 478" }
            }, main, detail);

            BasePlayer target = hasTarget ? BasePlayer.FindByID(targetId) : null;
            if (target == null || !target.IsConnected)
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = Lang(hasTarget ? "UiModOffline" : "UiModPick", uid), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.8 0.75 0.85 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "12 0", OffsetMax = "-12 0" }
                }, detail);
                ShowUi(mod, c);
                return;
            }

            // Detail panel is 268 x 456
            c.Add(new CuiLabel
            {
                Text = { Text = Colorize(StripTags(target.displayName), ResolveNameColors(target.UserIDString, GetData(targetId))), FontSize = 16, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 420", OffsetMax = "256 448" }
            }, detail);
            c.Add(new CuiLabel
            {
                Text = { Text = target.UserIDString, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.6 0.56 0.66 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 402", OffsetMax = "256 420" }
            }, detail);

            MuteData m = GetActiveMute(targetId);
            string status = m == null
                ? Lang("UiModNotMuted", uid)
                : m.Expires <= 0
                    ? Lang("UiModMutedPerm", uid, m.Reason, m.By)
                    : Lang("UiModMuted", uid, FormatDuration(m.Expires - Now(), uid), m.Reason, m.By);
            c.Add(new CuiLabel
            {
                Text = { Text = status, FontSize = 12, Align = TextAnchor.UpperLeft, Color = "0.9 0.87 0.94 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 336", OffsetMax = "256 396" }
            }, detail);

            c.Add(new CuiLabel
            {
                Text = { Text = Lang("UiModPresets", uid), FontSize = 12, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = HexToCui(accent, 1f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 306", OffsetMax = "256 328" }
            }, detail);

            var presets = config.Moderation.Mute.Presets;
            for (int i = 0; i < presets.Count && i < 7; i++)
            {
                int top = 300 - i * 34;
                c.Add(new CuiButton
                {
                    Button = { Color = "0.2 0.15 0.26 1", Command = $"nchat.mod mute {targetId} {i}" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"12 {top - 30}", OffsetMax = $"256 {top}" },
                    Text = { Text = $"{presets[i].Reason}  <color=#C77DFF>{presets[i].Duration}</color>", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, detail);
            }

            if (m != null)
                c.Add(new CuiButton
                {
                    Button = { Color = "0.15 0.45 0.3 1", Command = $"nchat.mod unmute {targetId}" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "12 12", OffsetMax = "256 42" },
                    Text = { Text = Lang("UiModUnmute", uid), FontSize = 13, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, detail);

            ShowUi(mod, c);
        }

        [ConsoleCommand("nchat.mod")]
        private void CcmdMod(ConsoleSystem.Arg arg)
        {
            BasePlayer mod = arg.Player();
            if (mod == null) return;
            if (!permission.UserHasPermission(mod.UserIDString, PermMute))
            {
                SendSystem(mod, Lang("NoPermission", mod.UserIDString));
                return;
            }

            ulong modId = mod.userID.Get();
            ulong targetId;

            switch (arg.GetString(0))
            {
                case "open":
                    break;

                case "back":
                    modTargets.Remove(modId);
                    DrawUi(mod);
                    return;

                case "page":
                {
                    int page;
                    modPages.TryGetValue(modId, out page);
                    modPages[modId] = Math.Max(0, page + arg.GetInt(1));
                    break;
                }

                case "pick":
                    if (ulong.TryParse(arg.GetString(1), out targetId)) modTargets[modId] = targetId;
                    break;

                case "mute":
                {
                    if (!ulong.TryParse(arg.GetString(1), out targetId)) break;
                    int index = arg.GetInt(2, -1);
                    var presets = config.Moderation.Mute.Presets;
                    if (index < 0 || index >= presets.Count) break;

                    BasePlayer target = BasePlayer.FindByID(targetId);
                    if (target == null) { SendSystem(mod, Lang("UiModOffline", mod.UserIDString)); break; }

                    MutePreset preset = presets[index];
                    double seconds;
                    if (!TryParseDuration(preset.Duration, out seconds))
                    {
                        SendSystem(mod, Lang("UiModBadPreset", mod.UserIDString, preset.Reason, preset.Duration));
                        break;
                    }

                    ApplyMute(targetId, target.displayName, seconds, preset.Reason, mod.displayName);
                    SendSystem(mod, Lang("MuteDone", mod.UserIDString, StripTags(target.displayName), DurationPhrase(seconds, mod.UserIDString), preset.Reason));
                    break;
                }

                case "unmute":
                {
                    if (!ulong.TryParse(arg.GetString(1), out targetId)) break;
                    MuteData existing;
                    data.Mutes.TryGetValue(targetId, out existing);
                    BasePlayer target = BasePlayer.FindByID(targetId);
                    string name = target != null ? target.displayName : existing?.Name ?? targetId.ToString();
                    if (RemoveMute(targetId, name))
                        SendSystem(mod, Lang("UnmuteDone", mod.UserIDString, StripTags(name)));
                    break;
                }

                default:
                    return;
            }

            DrawModUi(mod);
        }

        #endregion

        #region Helpers

        private static string StripTags(string text) => string.IsNullOrEmpty(text) ? text : RichTextRegex.Replace(text, string.Empty);

        private static string Sized(string text, int size) => size > 0 && !string.IsNullOrEmpty(text) ? $"<size={size}>{text}</size>" : text;

        private static bool TryParseHex(string hex, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            if (string.IsNullOrEmpty(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return false;
            int v;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            color = new Color32((byte)((v >> 16) & 255), (byte)((v >> 8) & 255), (byte)(v & 255), 255);
            return true;
        }

        private static string ToHex(Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        private static string SafeHex(string hex)
        {
            Color32 c;
            TryParseHex(hex, out c);
            return ToHex(c);
        }

        private static string FirstHex(List<string> colors) => colors != null && colors.Count > 0 ? SafeHex(colors[0]) : "#FFFFFF";

        private static string HexToCui(string hex, float alpha)
        {
            Color32 c;
            TryParseHex(hex, out c);
            return string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} {3:0.###}", c.r / 255f, c.g / 255f, c.b / 255f, alpha);
        }

        private static Color32 SampleGradient(List<Color32> stops, float t)
        {
            float scaled = Mathf.Clamp01(t) * (stops.Count - 1);
            int i = Mathf.Min((int)scaled, stops.Count - 2);
            return Color32.Lerp(stops[i], stops[i + 1], scaled - i);
        }

        // One color = solid. Two or more = per-character gradient through every stop (spaces are skipped).
        private static string Colorize(string text, List<string> colors)
        {
            if (string.IsNullOrEmpty(text) || colors == null || colors.Count == 0) return text;
            if (colors.Count == 1) return $"<color={SafeHex(colors[0])}>{text}</color>";

            var stops = new List<Color32>(colors.Count);
            foreach (string h in colors)
            {
                Color32 c;
                TryParseHex(h, out c);
                stops.Add(c);
            }

            int visible = 0;
            for (int i = 0; i < text.Length; i++)
                if (!char.IsWhiteSpace(text[i]) && !char.IsLowSurrogate(text[i])) visible++;

            if (visible <= 1) return $"<color={ToHex(stops[0])}>{text}</color>";

            var sb = new StringBuilder(text.Length * 26);
            int n = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch)) { sb.Append(ch); continue; }

                sb.Append("<color=").Append(ToHex(SampleGradient(stops, (float)n / (visible - 1)))).Append('>').Append(ch);
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    sb.Append(text[++i]);
                sb.Append("</color>");
                n++;
            }
            return sb.ToString();
        }

        // Accepts: perm | 0 | 45 (minutes) | 30s | 10m | 2h | 1d | 1w | combos like 1h30m
        private static bool TryParseDuration(string input, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            input = input.Trim().ToLowerInvariant();

            if (input == "perm" || input == "permanent" || input == "0") return true;

            int bare;
            if (int.TryParse(input, out bare))
            {
                seconds = bare * 60.0;
                return bare > 0;
            }

            var matches = DurationRegex.Matches(input);
            if (matches.Count == 0) return false;

            int consumed = 0;
            foreach (Match m in matches)
            {
                consumed += m.Length;
                double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (m.Groups[2].Value)
                {
                    case "s": seconds += v; break;
                    case "m": seconds += v * 60; break;
                    case "h": seconds += v * 3600; break;
                    case "d": seconds += v * 86400; break;
                    case "w": seconds += v * 604800; break;
                }
            }
            return consumed == input.Length && seconds > 0;
        }

        private string FormatDuration(double seconds, string uid)
        {
            if (seconds <= 0) return Lang("Permanent", uid);
            var ts = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            var parts = new List<string>();
            if (ts.Days > 0) parts.Add(ts.Days + "d");
            if (ts.Hours > 0) parts.Add(ts.Hours + "h");
            if (ts.Minutes > 0) parts.Add(ts.Minutes + "m");
            if (ts.Days == 0 && ts.Hours == 0 && ts.Seconds > 0) parts.Add(ts.Seconds + "s");
            return parts.Count > 0 ? string.Join(" ", parts.ToArray()) : "0s";
        }

        #endregion
    }
}
