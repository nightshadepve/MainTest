using Oxide.Core;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Discord Status (Webhook)", "AhmadAkal", "6.1.0")]
    [Description("Sends Rust server status updates. Compatible with both Oxide and Carbon.")]
    public class DiscordStatus : RustPlugin
    {
        #region Fields
        
        private static Configuration _config;
        private Timer _statusTimer;
        private ulong _messageId;
        private bool _isConfigValid;

        #endregion

        #region Configuration
        
        private class Configuration
        {
            [JsonProperty("Discord Webhook URL")]
            public string WebhookUrl { get; set; }

            [JsonProperty("Discord Message ID (0 = Post New Message)")]
            public ulong DiscordMessageId { get; set; }

            [JsonProperty("Direct Connect URL")]
            public string ConnectUrl { get; set; }
            
            [JsonProperty("Connect Header Text")]
            public string ConnectHeaderText { get; set; }

            [JsonProperty("Server Name Character Limit")]
            public int ServerNameCharacterLimit { get; set; }

            [JsonProperty("Embed Color - Online (Hex)")]
            public string OnlineEmbedColorHex { get; set; }

            [JsonProperty("Embed Color - Offline (Hex)")]
            public string OfflineEmbedColorHex { get; set; }

            [JsonProperty("Show Timestamp")]
            public bool ShowTimestamp { get; set; }

            [JsonProperty("Update Interval (Seconds)")]
            public int UpdateIntervalSeconds { get; set; }

            [JsonProperty("Enable Offline Message on Shutdown")]
            public bool EnableOfflineMessage { get; set; }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration
            {
                WebhookUrl = "Put webhook link here",
                DiscordMessageId = 0,
                ConnectUrl = "Put site redirection link here",
                ConnectHeaderText = "Join with a single click",
                ServerNameCharacterLimit = 100,
                OnlineEmbedColorHex = "#2ECC71",
                OfflineEmbedColorHex = "#ff0000",
                ShowTimestamp = true,
                UpdateIntervalSeconds = 60,
                EnableOfflineMessage = true
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadConfig();
            _messageId = _config.DiscordMessageId;

            if (string.IsNullOrEmpty(_config.WebhookUrl) || !_config.WebhookUrl.StartsWith("https://discord.com/api/webhooks/"))
            {
                PrintError("The 'Discord Webhook URL' in the config is invalid or missing. Please set a valid URL. The plugin will be disabled.");
                _isConfigValid = false;
                return;
            }
            _isConfigValid = true;
        }

        private void OnServerInitialized()
        {
            if (!_isConfigValid) return;

            if (_messageId == 0)
            {
                Puts("Message ID is not set in the config. A new message will be posted.");
            }
            else
            {
                Puts($"The plugin will attempt to edit the message with ID: {_messageId}");
            }

            int interval = _config.UpdateIntervalSeconds < 15 ? 15 : _config.UpdateIntervalSeconds;
            if (interval != _config.UpdateIntervalSeconds)
            {
                PrintWarning($"Update interval was too low ({_config.UpdateIntervalSeconds}s). It has been adjusted to the minimum of 15 seconds.");
            }

            if (_statusTimer != null) _statusTimer.Destroy();
            _statusTimer = timer.Every(interval, () => SendStatusUpdate(true));
            SendStatusUpdate(true);
        }
        
        private void Unload()
        {
            if (!_isConfigValid) return;

            if (_config.EnableOfflineMessage)
            {
                SendStatusUpdate(false);
            }
            _statusTimer?.Destroy();
        }
        
        #endregion

        #region Core Logic
        
        private void SendStatusUpdate(bool isOnline)
        {
            if (!_isConfigValid) return;

            string serverName = ConVar.Server.hostname;
            if (_config.ServerNameCharacterLimit > 0 && serverName.Length > _config.ServerNameCharacterLimit)
            {
                serverName = serverName.Substring(0, _config.ServerNameCharacterLimit);
            }

            var fields = new List<EmbedField>();
            string embedColor;

            if (isOnline)
            {
                embedColor = _config.OnlineEmbedColorHex;
                fields.Add(new EmbedField("Status", ":green_circle: Online", true));
                fields.Add(new EmbedField("Players", $"{BasePlayer.activePlayerList.Count} / {ConVar.Server.maxplayers}", true));
                
                string connectInfo = $"[**Connect**]({_config.ConnectUrl})";
                if (!string.IsNullOrEmpty(_config.ConnectHeaderText))
                {
                    connectInfo = $"{_config.ConnectHeaderText} :arrow_right: {connectInfo}";
                }
                fields.Add(new EmbedField("\u200B", connectInfo, false));
            }
            else
            {
                embedColor = _config.OfflineEmbedColorHex;
                fields.Add(new EmbedField("Status", ":red_circle: Offline", true));
                fields.Add(new EmbedField("Players", "-", true));
            }

            var payload = new WebhookPayload
            {
                Embeds = new List<WebhookEmbed>
                {
                    new WebhookEmbed
                    {
                        Title = serverName,
                        Color = int.Parse(embedColor.TrimStart('#'), NumberStyles.HexNumber),
                        Fields = fields,
                        Timestamp = _config.ShowTimestamp ? DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) : null,
                        Footer = _config.ShowTimestamp ? new EmbedFooter { Text = "Last Updated" } : null
                    }
                }
            };
            
            string jsonPayload = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            
            var headers = new Dictionary<string, string> { { "Content-Type", "application/json; charset=utf-8" } };
            
            if (_messageId == 0 && isOnline)
            {
                webrequest.Enqueue(_config.WebhookUrl + "?wait=true", jsonPayload, (code, response) =>
                {
                    if (code == 200 || code == 201)
                    {
                        var responseData = JsonConvert.DeserializeObject<MessageData>(response);
                        if (responseData != null && responseData.MessageId != 0)
                        {
                            _messageId = responseData.MessageId;
                            Puts($"Successfully posted a new message. To continue editing this message after restart, please set 'Discord Message ID' in your config to: {_messageId}");
                        }
                    }
                    else
                    {
                        PrintError($"Failed to post new status message. Code: {code}, Response: {response}");
                    }
                }, this, Core.Libraries.RequestMethod.POST, headers);
            }
            else if (_messageId != 0)
            {
                string editUrl = $"{_config.WebhookUrl}/messages/{_messageId}";
                webrequest.Enqueue(editUrl, jsonPayload, (code, response) =>
                {
                    if (code != 200 && isOnline)
                    {
                        PrintError($"Failed to edit message. Check if the 'Discord Message ID' in your config is correct. Code: {code}");
                    }
                }, this, Core.Libraries.RequestMethod.PATCH, headers);
            }
        }
        
        #endregion

        #region Discord Webhook Structures

        private class WebhookPayload
        {
            [JsonProperty("embeds")]
            public List<WebhookEmbed> Embeds { get; set; }
        }

        private class WebhookEmbed
        {
            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
            public string Description { get; set; }

            [JsonProperty("color")]
            public int Color { get; set; }

            [JsonProperty("fields")]
            public List<EmbedField> Fields { get; set; }

            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }

            [JsonProperty("footer")]
            public EmbedFooter Footer { get; set; }
        }

        private class EmbedField
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("value")]
            public string Value { get; set; }

            [JsonProperty("inline")]
            public bool Inline { get; set; }

            public EmbedField(string name, string value, bool inline)
            {
                Name = name;
                Value = value;
                Inline = inline;
            }
        }

        private class EmbedFooter
        {
            [JsonProperty("text")]
            public string Text { get; set; }
        }
        
        #endregion

        private class MessageData
        {
            [JsonProperty("id")]
            public ulong MessageId { get; set; }
        }
    }
}
 