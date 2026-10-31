using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins;

[Info("EventsManagerNotifier", "Mevent & M&B-Studios", "1.0.11")]
public class EventsManagerNotifier : RustPlugin
{
    #region Fields

    [PluginReference] private Plugin GUIAnnouncements = null;

    #endregion

    #region Config

    private Configuration _config;

    private class Configuration
    {
        [JsonProperty(PropertyName = "Events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<EventConfig> Events = new()
        {
            new EventConfig
            {
                MessagesActive = true,
                EventName = "Airdrop",
                StartMessage = "Airdrop was <color=green>dropped</color>"
            },
            new EventConfig
            {
                MessagesActive = true,
                EventName = "Chinook (CH47)",
                StartMessage = "Chinook 47 has <color=green>started</color> and is making its rounds"
            },
            new EventConfig
            {
                MessagesActive = true,
                EventName = "Cargo Ship",
                StartMessage = "CargoShip sets sail and has <color=green>started</color> the journey"
            },
            new EventConfig
            {
                MessagesActive = true,
                EventName = "Patrol Helicopter",
                StartMessage = "Helicopter <color=green>started</color> the sightseeing flight"
            },
            new EventConfig
            {
                MessagesActive = true,
                EventName = "Deep Sea",
                StartMessage = "Deep Sea event is now <color=green>active</color>!"
            }
        };

        [JsonProperty(PropertyName = "Type 0 - chat, 1 - GUIAnnouncements")]
        public NotifierType Type = NotifierType.Chat;

        [JsonProperty(PropertyName = "Chat Icon (Steam ID, 0 = no icon)")]
        public ulong ChatIcon = 0;

        [JsonProperty(PropertyName = "Discord Settings")]
        public DiscordWebhookSettings Discord = new();

        [JsonProperty(PropertyName = "Version")]
        public VersionNumber Version;
    }

    private class DiscordWebhookSettings
    {
        [JsonProperty(PropertyName = "Enabled")]
        public bool Enabled = false;

        [JsonProperty(PropertyName = "Webhook URL")]
        public string WebhookUrl = string.Empty;

        [JsonProperty(PropertyName = "Embed Color (Decimal)")]
        public int EmbedColor = 3066993; // Green color

        [JsonProperty(PropertyName = "Bot Username")]
        public string BotUsername = "Event Manager";

        [JsonProperty(PropertyName = "Bot Avatar URL")]
        public string BotAvatarUrl = string.Empty;
    }

    private class EventConfig
    {
        [JsonProperty(PropertyName = "Messages Active")]
        public bool MessagesActive = true;

        [JsonProperty(PropertyName = "Event Name")]
        public string EventName = string.Empty;

        [JsonProperty(PropertyName = "Start Message")]
        public string StartMessage = string.Empty;

        [JsonProperty(PropertyName = "Discord Message (empty = use Start Message)")]
        public string DiscordMessage = string.Empty;
    }

    private enum NotifierType
    {
        Chat,
        Announcements
    }

    protected override void LoadConfig()
    {
        base.LoadConfig();
        try
        {
            _config = Config.ReadObject<Configuration>();
            if (_config == null) throw new Exception();

            if (_config.Version < Version)
                UpdateConfigValues();

            SaveConfig();
        }
        catch (Exception ex)
        {
            PrintError("Your configuration file contains an error. Using default configuration values. Message: " +
                ex.Message ?? string.Empty);
            LoadDefaultConfig();
            Debug.LogException(ex);
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

    private void UpdateConfigValues()
    {
        if (_config.Version < new VersionNumber(1, 0, 11))
            RenameFacepunchEventNames();

        _config.Version = Version;
    }

    private void RenameFacepunchEventNames()
    {
        var renames = new Dictionary<string, string>
        {
            ["Cargoship"] = "Cargo Ship",
            ["Chinook 47"] = "Chinook (CH47)",
            ["Helicopter"] = "Patrol Helicopter"
        };

        if (_config.Events == null) return;

        foreach (var ev in _config.Events)
        {
            if (ev == null || string.IsNullOrEmpty(ev.EventName)) continue;
            if (renames.TryGetValue(ev.EventName, out var renamed))
            {
                PrintWarning($"Renaming event '{ev.EventName}' -> '{renamed}' to match EventsManager display names");
                ev.EventName = renamed;
            }
        }
    }

    #endregion

    #region Hooks

    private void OnEventStarted(string eventName)
    {
        var eventConfig = _config?.Events?.Find(x => x.EventName == eventName);
        if (eventConfig == null || !eventConfig.MessagesActive) return;

        switch (_config.Type)
        {
            case NotifierType.Chat:
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player == null || !player.IsConnected) continue;

                    var msg = Msg(eventName, player);
                    if (_config.ChatIcon > 0)
                        player.SendConsoleCommand("chat.add", 2, _config.ChatIcon, msg);
                    else
                        player.ChatMessage(msg);
                }

                break;
            }

            case NotifierType.Announcements:
            {
                if (GUIAnnouncements == null || !GUIAnnouncements.IsLoaded)
                {
                    PrintError("GUIAnnouncements IS NOT INSTALLED!");
                    return;
                }

                GUIAnnouncements.Call("CreateAnnouncement", Msg(eventName), "Grey", "White", null, 0.03f);
                break;
            }
        }

        if (_config?.Discord?.Enabled == true && !string.IsNullOrEmpty(_config.Discord.WebhookUrl))
            SendDiscordNotification(eventConfig);
    }

    #endregion

    #region Discord

    private void SendDiscordNotification(EventConfig eventConfig)
    {
        if (eventConfig == null || string.IsNullOrEmpty(_config.Discord.WebhookUrl)) return;

        var message = string.IsNullOrEmpty(eventConfig.DiscordMessage)
            ? Msg(eventConfig.EventName)
            : eventConfig.DiscordMessage;

        message = Regex.Replace(message, "<.*?>", string.Empty);

        var embed = new DiscordEmbed
        {
            title = eventConfig.EventName,
            description = message,
            color = _config.Discord.EmbedColor,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        var discordMessage = new DiscordMessage
        {
            username = _config.Discord.BotUsername,
            avatar_url = _config.Discord.BotAvatarUrl,
            embeds = new List<DiscordEmbed> {embed}
        };

        webrequest.Enqueue(_config.Discord.WebhookUrl, JsonConvert.SerializeObject(discordMessage),
            (code, response) =>
            {
                if (code != 200 && code != 204)
                    PrintError($"Failed to send Discord notification. Code: {code}, Response: {response}");
            }, this, RequestMethod.POST,
            new Dictionary<string, string> {{"Content-Type", "application/json"}});
    }

    private class DiscordMessage
    {
        [JsonProperty("username")] public string username { get; set; }

        [JsonProperty("avatar_url")] public string avatar_url { get; set; }

        [JsonProperty("embeds")] public List<DiscordEmbed> embeds { get; set; }
    }

    private class DiscordEmbed
    {
        [JsonProperty("title")] public string title { get; set; }

        [JsonProperty("description")] public string description { get; set; }

        [JsonProperty("color")] public int color { get; set; }

        [JsonProperty("timestamp")] public string timestamp { get; set; }
    }

    #endregion

    #region Lang

    protected override void LoadDefaultMessages()
    {
        var en = new Dictionary<string, string>();
        if (_config?.Events != null)
            foreach (var ev in _config.Events)
                if (!string.IsNullOrEmpty(ev.EventName))
                    en[ev.EventName] = ev.StartMessage;

        lang.RegisterMessages(en, this);

        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Airdrop"] = "Аирдроп был <color=green>сброшен</color>",
            ["Chinook (CH47)"] = "Чинук 47 <color=green>начал</color> свой облёт",
            ["Cargo Ship"] = "Грузовой корабль <color=green>отчалил</color> в плавание",
            ["Patrol Helicopter"] = "Патрульный вертолёт <color=green>начал</color> облёт",
            ["Deep Sea"] = "Событие Deep Sea <color=green>активно</color>!"
        }, this, "ru");
    }

    private string Msg(string key, BasePlayer player) =>
        lang.GetMessage(key, this, player?.UserIDString);

    private string Msg(string key) =>
        lang.GetMessage(key, this);

    #endregion
}
