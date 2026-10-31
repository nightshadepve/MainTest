using System;
using System.Collections.Generic;
using Oxide.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("MedioReports", "MediocratyItself", "2.2.1")]
    [Description("Records player reports and sends them to Discord with embed formatting")]
    class MedioReports : RustPlugin
    {
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty("Default Discord Webhook URL")]
            public string DefaultWebhookUrl = "https://discord.com/api/webhooks/YOUR_WEBHOOK_HERE";

            [JsonProperty("Embed Title")]
            public string EmbedTitle = "Player Report Received";

            [JsonProperty("Embed Description")]
            public string EmbedDescription = "A new report has been submitted";

            [JsonProperty("Embed Color (Decimal)")]
            public int EmbedColor = 15158332; // Red color

            [JsonProperty("Show Server Name In Embed")]
            public bool ShowServerName = true;

            [JsonProperty("Include Timestamp")]
            public bool IncludeTimestamp = true;

            [JsonProperty("Show Player IP Addresses")]
            public bool ShowIPAddresses = false;

            [JsonProperty("Report Type Webhooks (leave empty to use default)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, WebhookConfig> ReportTypeWebhooks = new Dictionary<string, WebhookConfig>
            {
                ["racism_sexism_abusive"] = new WebhookConfig
                {
                    WebhookUrl = "",
                    CustomTitle = "Abusive Behavior Report",
                    CustomColor = 15158332
                },
                ["cheating"] = new WebhookConfig
                {
                    WebhookUrl = "",
                    CustomTitle = "Cheating Report",
                    CustomColor = 16711680
                },
                ["spamming"] = new WebhookConfig
                {
                    WebhookUrl = "",
                    CustomTitle = "Spamming Report",
                    CustomColor = 16776960
                },
                ["offensive_name"] = new WebhookConfig
                {
                    WebhookUrl = "",
                    CustomTitle = "Offensive Name Report",
                    CustomColor = 16744272
                },
                ["break_server_rules"] = new WebhookConfig
                {
                    WebhookUrl = "",
                    CustomTitle = "Server Rules Violation Report",
                    CustomColor = 16098851
                }
            };

            public class WebhookConfig
            {
                [JsonProperty("Webhook URL (empty = use default)")]
                public string WebhookUrl = "";

                [JsonProperty("Custom Embed Title (empty = use default)")]
                public string CustomTitle = "";

                [JsonProperty("Custom Embed Color (0 = use default)")]
                public int CustomColor = 0;
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                SaveConfig();
            }
            catch
            {
                PrintWarning("Configuration file contains errors. Using default configuration.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        #endregion

        #region Data Classes

        private JuBaodata Jubaodata;

        private class JuBaodata
        {
            [JsonProperty("info")]
            public Dictionary<ulong, GjData> WanJia = new Dictionary<ulong, GjData>();

            public class GjData
            {
                [JsonProperty("=== Cumulative number of reports initiated by players ===")]
                public int hhh;
                [JsonProperty("=== Report details ===")]
                public List<GjDataB> jb = new List<GjDataB>();
            }

            public class GjDataB
            {
                [JsonProperty("Report date and time")]
                public string time;
                [JsonProperty("Initiating Player - name")]
                public string aaa;
                [JsonProperty("Initiating Player - SteamID")]
                public string bbb;
                [JsonProperty("Target player - name")]
                public string ccc;
                [JsonProperty("Target player - SteamID")]
                public string ddd;
                [JsonProperty("Report title")]
                public string eee;
                [JsonProperty("Report content")]
                public string fff;
                [JsonProperty("Report type")]
                public string ggg;
            }
        }

        #endregion

        #region Discord Embed Classes

        private class WebhookBody
        {
            [JsonProperty("embeds")]
            public List<EmbedBody> Embeds;

            [JsonProperty("content")]
            public string Content = null;
        }

        private class EmbedBody
        {
            [JsonProperty("title")]
            public string Title;

            [JsonProperty("type")]
            public string Type;

            [JsonProperty("description")]
            public string Description;

            [JsonProperty("color")]
            public int Color;

            [JsonProperty("fields")]
            public List<FieldBody> Fields;

            [JsonProperty("footer")]
            public FooterBody Footer;

            [JsonProperty("timestamp")]
            public string Timestamp;

            public class FieldBody
            {
                [JsonProperty("name")]
                public string Name;

                [JsonProperty("value")]
                public string Value;

                [JsonProperty("inline")]
                public bool Inline;
            }

            public class FooterBody
            {
                [JsonProperty("text")]
                public string Text;
            }
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();
        }

        private void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            var data = SzData(reporter);
            if (data != null)
            {
                data.hhh++;
                data.jb.Add(new JuBaodata.GjDataB
                {
                    time = FormatTime(),
                    aaa = reporter.displayName,
                    bbb = reporter.UserIDString,
                    ccc = targetName,
                    ddd = targetId,
                    eee = subject,
                    fff = message,
                    ggg = type
                });
                SaveData();
            }
            SendDiscordEmbed(reporter, targetName, targetId, subject, message, type);
        }

        #endregion

        #region Discord Methods

        private void SendDiscordEmbed(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            string webhookUrl = _config.DefaultWebhookUrl;
            string embedTitle = _config.EmbedTitle;
            int embedColor = _config.EmbedColor;
            if (_config.ReportTypeWebhooks.ContainsKey(type))
            {
                var typeConfig = _config.ReportTypeWebhooks[type];
                
                if (!string.IsNullOrEmpty(typeConfig.WebhookUrl))
                    webhookUrl = typeConfig.WebhookUrl;
                
                if (!string.IsNullOrEmpty(typeConfig.CustomTitle))
                    embedTitle = typeConfig.CustomTitle;
                
                if (typeConfig.CustomColor > 0)
                    embedColor = typeConfig.CustomColor;
            }

            var body = new WebhookBody
            {
                Embeds = new List<EmbedBody>()
            };

            var fields = new List<EmbedBody.FieldBody>();
            fields.Add(new EmbedBody.FieldBody
            {
                Name = "📋 Report Type",
                Value = $"`{type}`",
                Inline = true
            });
            if (!string.IsNullOrEmpty(subject))
            {
                string cleanSubject = subject;
                if (subject.StartsWith($"[{type}]"))
                {
                    cleanSubject = subject.Substring(type.Length + 2).TrimStart();
                }
                
                fields.Add(new EmbedBody.FieldBody
                {
                    Name = "📌 Subject",
                    Value = cleanSubject,
                    Inline = true
                });
            }
            if (!string.IsNullOrEmpty(subject))
            {
                fields.Add(new EmbedBody.FieldBody
                {
                    Name = "\u200b",
                    Value = "\u200b",
                    Inline = true
                });
            }

            string reporterInfo = $"**Name:** {reporter.displayName}\n**Steam ID:** `{reporter.UserIDString}`\n";
            if (_config.ShowIPAddresses && reporter.net?.connection != null)
            {
                reporterInfo += $"**IP Address:** `{reporter.net.connection.ipaddress}`\n";
            }
            reporterInfo += $"**Profile:** [View Profile](https://steamcommunity.com/profiles/{reporter.UserIDString})";

            fields.Add(new EmbedBody.FieldBody
            {
                Name = "👤 Reporter",
                Value = reporterInfo,
                Inline = true
            });

            fields.Add(new EmbedBody.FieldBody
            {
                Name = "\u200b",
                Value = "**|**\n**|**\n**|**\n**|**",
                Inline = true
            });

            BasePlayer targetPlayer = BasePlayer.Find(targetId);
            string targetInfo = $"**Name:** {targetName}\n**Steam ID:** `{targetId}`\n";
            
            if (_config.ShowIPAddresses)
            {
                if (targetPlayer != null && targetPlayer.net?.connection != null)
                {
                    targetInfo += $"**IP Address:** `{targetPlayer.net.connection.ipaddress}`\n";
                }
                else
                {
                    targetInfo += $"**IP Address:** `Offline/Unknown`\n";
                }
            }
            targetInfo += $"**Profile:** [View Profile](https://steamcommunity.com/profiles/{targetId})";

            fields.Add(new EmbedBody.FieldBody
            {
                Name = "🎯 Reported Player",
                Value = targetInfo,
                Inline = true
            });

            if (!string.IsNullOrEmpty(message))
            {
                string formattedMessage = message.Length > 1018 ? message.Substring(0, 1015) + "..." : message;
                fields.Add(new EmbedBody.FieldBody
                {
                    Name = "💬 Report Details",
                    Value = $"```{formattedMessage}```",
                    Inline = false
                });
            }

            var embed = new EmbedBody
            {
                Title = embedTitle,
                Description = _config.EmbedDescription,
                Type = "rich",
                Color = embedColor,
                Fields = fields
            };

            if (_config.ShowServerName)
            {
                embed.Footer = new EmbedBody.FooterBody
                {
                    Text = ConVar.Server.hostname
                };
            }

            if (_config.IncludeTimestamp)
            {
                embed.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            }

            body.Embeds.Add(embed);

            webrequest.Enqueue(webhookUrl, JObject.FromObject(body).ToString(),
                (code, response) =>
                {
                    if (code != 200 && code != 204)
                    {
                        PrintWarning($"Discord webhook failed with code {code}: {response}");
                    }
                }, this, RequestMethod.POST,
                new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json"
                });
        }

        #endregion

        #region Data Management

        private void LoadData()
        {
            try
            {
                Jubaodata = Interface.Oxide.DataFileSystem.ReadObject<JuBaodata>(Name);
            }
            catch
            {
                ClearData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, Jubaodata);

        private void ClearData()
        {
            Jubaodata = new JuBaodata();
            SaveData();
        }

        private void CjGjData(BasePlayer player)
        {
            if (Jubaodata.WanJia.ContainsKey(player.userID)) return;
            Jubaodata.WanJia.Add(player.userID, new JuBaodata.GjData());
        }

        private JuBaodata.GjData SzData(BasePlayer player)
        {
            if (player.userID < 70000000000) return null;
            CjGjData(player);
            return Jubaodata.WanJia[player.userID];
        }

        #endregion

        #region Helper Methods

        private string FormatTime()
        {
            var now = DateTime.Now;
            int days = now.Day;
            int hours = now.Hour;
            int mins = now.Minute;
            return string.Format("{0}/{1}:{2}", days, hours, mins);
        }

        #endregion
    }
} 