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
*  Codefling plugin page: https://codefling.com/plugins/teleport-status
*  Codefling license: https://codefling.com/plugins/teleport-status?tab=downloads_field_4
*  
*  Lone.Design plugin page: https://lone.design/product/teleport-status/
*
*  Copyright © 2024-2025 IIIaKa
*/

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Teleport Status", "IIIaKa", "0.1.2")]
    [Description("A useful plugin that displays the time until teleportation with the ability to automatically generate hooks. Depends on AdvancedStatus plugin.")]
    class TeleportStatus : RustPlugin
    {
        [PluginReference]
        private Plugin ImageLibrary, AdvancedStatus;

        #region ~Variables~
        private static TeleportStatus Instance { get; set; }
        private bool _imgLibIsLoaded = false, _statusIsLoaded = false;
        private const string StatusCreateBar = "CreateBar", StatusDeleteAllBars = "DeleteAllBars";
        private readonly string[] HttpScheme = new string[] { "http://", "https://" };
        private string[] _defaultHooks = Array.Empty<string>();
        #endregion

        #region ~Configuration~
        private static Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Is it worth generating teleport hooks? 0 - disabled, 1 - one-time activation, 2 - always enabled")]
            public int GenerateHooks = 2;
            
            [JsonProperty(PropertyName = "List of language keys for creating language files")]
            public List<string> LanguageKeys;
            
            [JsonProperty(PropertyName = "List of 'cancel' teleport commands")]
            public HashSet<string> CancelCommands;
            
            [JsonProperty(PropertyName = "Limits the length of the displayed teleport name. A value of 0 disables the limitation")]
            public int NameLimit = 10;
            
            [JsonProperty(PropertyName = "List of hooks for generation. Leave empty or null to recreate the default list")]
            public List<TeleportHook> TeleportHooks;
            
            [JsonProperty(PropertyName = "List of status bar settings. The keys of these settings can be used for generated hooks. Leave empty or null to recreate the default list")]
            public Dictionary<string, BarSettings> BarList;
            
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
                PrintWarning($"Your configuration file version({_config.Version}) is outdated. Updating it to {Version}.");
                _config.Version = Version;
                _config.GenerateHooks = Math.Clamp(_config.GenerateHooks, 1, 2);
                PrintWarning($"The configuration file has been successfully updated to version {_config.Version}!");
            }
            
            _config.GenerateHooks = Math.Clamp(_config.GenerateHooks, 0, 2);
            
            _config.LanguageKeys ??= new List<string>();
            if (_config.LanguageKeys.Any())
            {
                for (int i = _config.LanguageKeys.Count - 1; i >= 0; i--)
                {
                    string langKey = ToLangKey(_config.LanguageKeys[i]);
                    if (langKey.Equals("en", StringComparison.OrdinalIgnoreCase) || langKey.Equals("ru", StringComparison.OrdinalIgnoreCase))
                        _config.LanguageKeys.RemoveAt(i);
                    else
                        _config.LanguageKeys[i] = langKey;
                }
            }
            _config.LanguageKeys.Add("en");

            if (_config.CancelCommands == null || !_config.CancelCommands.Any())
                _config.CancelCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tpc" };
            _config.NameLimit = Math.Max(_config.NameLimit, 0);
            
            if (_config.TeleportHooks == null || !_config.TeleportHooks.Any())
            {
                _config.TeleportHooks = new List<TeleportHook>()
                {
                    new TeleportHook("OnTeleportAccepted", "BarTeleportToPlayer", "ToPlayer", "BasePlayer target, BasePlayer player, int countdown", 1, 0, 2, true),
                    new TeleportHook("OnHomeAccepted", "BarTeleportToHome", "Home", "BasePlayer player, string home, int countdown", 0, 1, 2),
                    new TeleportHook("OnHomeAccepted", "BarTeleportToHome", "Home", "BasePlayer player, string home, Vector3 pos, int countdown", 0, 1, 3),
                    new TeleportHook("OnTownAccepted", "BarTeleportToTown", "Town", "BasePlayer player, string name, int countdown", 0, 1, 2),
                    new TeleportHook("OnTeleportBackAccepted", "BarTeleportToBack", "Back", "BasePlayer player, Vector3 location, int countdown", 0, 1, 2),
                    new TeleportHook("OnWarpAccepted", "BarTeleportToWarp", "Warp", "BasePlayer player, Vector3 warpPosition, int countdown", 0, 1, 2),
                    new TeleportHook("OnTeleportInterrupted", string.Empty, string.Empty, "BasePlayer player", 0)
                };
            }
            else
                _config.TeleportHooks = _config.TeleportHooks.Where(teleportHook => teleportHook != null && !string.IsNullOrWhiteSpace(teleportHook.HookName)).ToList();
            
            if (_config.BarList == null || !_config.BarList.Any())
            {
                _config.BarList = new Dictionary<string, BarSettings>()
                {
                    { "ToPlayer", new BarSettings() { Image_Transparency = 1f } },
                    { "FromPlayer", new BarSettings() {
                        Order = 22,
                        Image_Url = "https://i.imgur.com/G3TrHoc.png",
                        Image_Local = "TeleportStatus_FromPlayer",
                        Image_Color = "#E2DBD6",
                        Text_Offset_Horizontal = 2,
                        Progress_OffsetMin = "25 0"
                    }},
                    { "Home", new BarSettings() {
                        Order = 23,
                        Image_Url = "https://i.imgur.com/3xQmCKv.png",
                        Image_Local = "TeleportStatus_Home",
                        Image_Color = "#E2DBD6",
                        Text_Color = "#E2DBD6",
                        Text_Offset_Horizontal = 2,
                        SubText_Color = "#E2DBD6",
                        Progress_Color = "#009688",
                        Progress_OffsetMin = "25 2.5",
                        Progress_OffsetMax = "-3.5 -3.5"
                    }},
                    { "Town", new BarSettings() {
                        Order = 24,
                        Image_Url = "https://i.imgur.com/FswyqvJ.png",
                        Image_Local = "TeleportStatus_Town",
                        Image_Color = "#E2DBD6",
                        Text_Color = "#E2DBD6",
                        SubText_Color = "#E2DBD6"
                    }},
                    { "Back", new BarSettings() {
                        Order = 25,
                        Image_Url = "https://i.imgur.com/I9B2joa.png",
                        Image_Local = "TeleportStatus_Back",
                        Image_Color = "#E2DBD6",
                        Text_Color = "#E2DBD6",
                        Text_Offset_Horizontal = 2,
                        SubText_Color = "#E2DBD6",
                        Progress_OffsetMin = "25 0"
                    }},
                    { "Warp", new BarSettings() {
                        Order = 26,
                        Image_Url = "https://i.imgur.com/sczcAdC.png",
                        Image_Local = "TeleportStatus_Warp",
                        Image_Color = "#E2DBD6",
                        Text_Color = "#E2DBD6",
                        SubText_Color = "#E2DBD6"
                    }}
                };
            }
            else
                _config.BarList = _config.BarList.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            _defaultHooks = _config.TeleportHooks.Where(teleportHook => teleportHook.IsEnabled).Select(teleportHook => teleportHook.HookName).ToArray();
            SaveConfig();
            
            if (_config.GenerateHooks < 1)
                return;
            if (_config.GenerateHooks != 2)
                _config.GenerateHooks = 0;
            
            string path = this.FilePath,
                sourceCode = File.ReadAllText(path);
            int startIndex = sourceCode.LastIndexOf("#region ~Teleport Hooks~", StringComparison.OrdinalIgnoreCase),
                endIndex = sourceCode.LastIndexOf("#endregion", StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0 || endIndex <= startIndex)
            {
                PrintError("Failed to find a region for teleport hooks! Try installing a clean plugin.");
                return;
            }
            startIndex += 24;
            endIndex--;
            
            int currentHash;
            TeleportHook teleportHook;
            var hashList = new List<int>();
            var list = new HashSet<string>();
            for (int i = 0; i < _config.TeleportHooks.Count; i++)
            {
                teleportHook = _config.TeleportHooks[i];
                if (!teleportHook.IsEnabled)
                    continue;
                
                if (string.IsNullOrWhiteSpace(teleportHook.ArgumentList))
                    teleportHook.ArgumentList = "BasePlayer player";
                
                currentHash = HashCode.Combine(teleportHook.HookName, teleportHook.ArgumentList);
                if (hashList.Contains(currentHash))
                {
                    teleportHook.IsEnabled = false;
                    PrintWarning($"Duplicate hook detected: {teleportHook.HookName} ({teleportHook.ArgumentList}), hook index {i}");
                    continue;
                }
                
                if (!teleportHook.TryGenerateHookString(out var hookString))
                    continue;
                list.Add(hookString);
                hashList.Add(currentHash);
            }
            SaveConfig();
            
            string existingHooks = sourceCode.Substring(startIndex, endIndex - startIndex).Trim(), newHooks = string.Concat(list).Trim();
            if (existingHooks == newHooks)
            {
                _defaultHooks = _config.TeleportHooks.Where(teleportHook => teleportHook.IsEnabled).Select(teleportHook => teleportHook.HookName).ToArray();
                return;
            }
            var sb = new System.Text.StringBuilder(sourceCode);
            sb.Remove(startIndex, endIndex - startIndex);
            sb.Insert(startIndex, $"\n{newHooks}\n");
            File.WriteAllText(path, sb.ToString());
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
        protected override void LoadDefaultConfig() => _config = new Configuration() { Version = Version };
        #endregion

        #region ~Language~
        protected override void LoadDefaultMessages()
        {
            Dictionary<string, string> enLang = new Dictionary<string, string>() { { "BarTeleportToPlayer", "Teleport to {0} in:" } },
                ruLang = new Dictionary<string, string>() { { "BarTeleportToPlayer", "Телепорт к {0} через:" } };
            foreach (var teleportHook in _config.TeleportHooks)
            {
                if (!string.IsNullOrWhiteSpace(teleportHook.TextKey))
                {
                    enLang.TryAdd(teleportHook.TextKey, "Teleport to {0} in:");
                    ruLang.TryAdd(teleportHook.TextKey, "Телепорт в {0} через:");
                }
                if (!string.IsNullOrWhiteSpace(teleportHook.TargetTextKey))
                {
                    enLang.TryAdd(teleportHook.TargetTextKey, "{0} will be at you in:");
                    ruLang.TryAdd(teleportHook.TargetTextKey, "{0} будет у вас через:");
                }
            }
            
            for (int i = 0; i < _config.LanguageKeys.Count; i++)
                lang.RegisterMessages(enLang, this, _config.LanguageKeys[i]);
            lang.RegisterMessages(ruLang, this, "ru");
        }
        #endregion

        #region ~Methods~
        private void SendBar(object obj, string name, string textKey, double countdown, BarSettings barSettings)
        {
            string userIDStr = $"{obj}";
            if (ulong.TryParse(userIDStr, out var userID))
                InternalSendBar(userID, userIDStr, name, textKey, countdown, barSettings);
        }
        
        private void SendBar(string userIDStr, string name, string textKey, double countdown, BarSettings barSettings)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                InternalSendBar(userID, userIDStr, name, textKey, countdown, barSettings);
        }
        
        private void SendBar(IPlayer player, string name, string textKey, double countdown, BarSettings barSettings)
        {
            if (ulong.TryParse(player.Id, out var userID))
                InternalSendBar(userID, player.Id, name, textKey, countdown, barSettings);
        }
        
        private void SendBar(ulong userID, string name, string textKey, double countdown, BarSettings barSettings) => InternalSendBar(userID, userID.ToString(), name, textKey, countdown, barSettings);
        private void SendBar(BasePlayer player, string name, string textKey, double countdown, BarSettings barSettings) => InternalSendBar(player.userID, player.UserIDString, name, textKey, countdown, barSettings);
        
        private void InternalSendBar(ulong userID, string userIDStr, string name, string textKey, double countdown, BarSettings barSettings)
        {
            if (_config.NameLimit > 0 && name.Length > _config.NameLimit)
                name = name.Substring(0, _config.NameLimit);
            double currentStamp = Network.TimeEx.currentTimestamp;
            AdvancedStatus?.Call(StatusCreateBar, userID, new Dictionary<int, object>(barSettings.StatusBar) { { 15, string.Format(lang.GetMessage(textKey, this, userIDStr), name) }, { 28, currentStamp }, { 29, currentStamp + countdown } });
        }
        
        private void DeleteBar(object obj)
        {
            string userIDStr = $"{obj}";
            if (ulong.TryParse(userIDStr, out var userID))
                AdvancedStatus?.Call(StatusDeleteAllBars, userID, Name);
        }
        
        private void DeleteBar(string userIDStr)
        {
            if (ulong.TryParse(userIDStr, out var userID))
                AdvancedStatus?.Call(StatusDeleteAllBars, userID, Name);
        }
        
        private void DeleteBar(IPlayer player)
        {
            if (ulong.TryParse(player.Id, out var userID))
                AdvancedStatus?.Call(StatusDeleteAllBars, userID, Name);
        }
        
        private void DeleteBar(ulong userID) => AdvancedStatus?.Call(StatusDeleteAllBars, userID, Name);
        private void DeleteBar(BasePlayer player) => AdvancedStatus?.Call(StatusDeleteAllBars, player.userID, Name);
        
        private static string ToLangKey(string langKey) => string.IsNullOrWhiteSpace(langKey) || langKey.Length != 2 || !langKey.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ? "en" : langKey.ToLower(System.Globalization.CultureInfo.InvariantCulture);
        
        private void ToggleImageLib(bool isLoaded)
        {
            _imgLibIsLoaded = isLoaded;
            if (_imgLibIsLoaded)
            {
                var imgList = new Dictionary<string, string>();
                foreach (var barSettings in _config.BarList.Values)
                {
                    if (string.IsNullOrWhiteSpace(barSettings.Image_Sprite) && string.IsNullOrWhiteSpace(barSettings.Image_Local) && barSettings.Image_Url.StartsWithAny(HttpScheme))
                        imgList.Add(barSettings.ImageId, barSettings.Image_Url);
                }
                if (imgList.Any())
                    ImageLibrary?.Call("ImportImageList", Name, imgList, 0uL, true);
            }
            if (_statusIsLoaded)
            {
                foreach (var barSettings in _config.BarList.Values)
                    barSettings.SelectBarImage();
            }
        }
        #endregion

        #region ~Oxide Hooks~
        void OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (_config.CancelCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
                DeleteBar(player);
        }
        
        void OnAdvancedStatusLoaded()
        {
            _statusIsLoaded = true;
            var imgList = new List<string>();
            foreach (var barSettings in _config.BarList.Values)
            {
                if (!string.IsNullOrWhiteSpace(barSettings.Image_Local) && !imgList.Contains(barSettings.Image_Local))
                    imgList.Add(barSettings.Image_Local);
                barSettings.SelectBarImage();
            }
            if (imgList.Any())
                AdvancedStatus?.Call("LoadImages", imgList);
            
            for (int i = 0; i < _defaultHooks.Length; i++)
                Subscribe(_defaultHooks[i]);
            Subscribe(nameof(OnPlayerCommand));
        }
        
        void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == ImageLibrary)
                ToggleImageLib(true);
        }

        void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin.Name.Equals("ImageLibrary", StringComparison.OrdinalIgnoreCase))
                ToggleImageLib(false);
            else if (plugin.Name.Equals("AdvancedStatus", StringComparison.OrdinalIgnoreCase))
            {
                _statusIsLoaded = false;
                for (int i = 0; i < _defaultHooks.Length; i++)
                    Unsubscribe(_defaultHooks[i]);
                Unsubscribe(nameof(OnPlayerCommand));
            }
        }

        void Init()
        {
            Unsubscribe(nameof(OnPluginLoaded));
            Unsubscribe(nameof(OnPluginUnloaded));
            for (int i = 0; i < _defaultHooks.Length; i++)
                Unsubscribe(_defaultHooks[i]);
            Unsubscribe(nameof(OnPlayerCommand));
            Unsubscribe(nameof(OnAdvancedStatusLoaded));
            Instance = this;
        }

        void OnServerInitialized(bool initial)
        {
            BarSettings barSettings;
            foreach (var kvp in _config.BarList)
            {
                barSettings = kvp.Value;
                barSettings.BarId = $"{Name}_{kvp.Key}";
                barSettings.ImageId = barSettings.BarId;
                barSettings.PrepareStatusBar();
            }
            
            ToggleImageLib(ImageLibrary != null && ImageLibrary.IsLoaded);
            _statusIsLoaded = AdvancedStatus != null && AdvancedStatus?.Call("IsReady") != null;
            if (!_statusIsLoaded)
            {
                if (initial && AdvancedStatus != null)
                    PrintWarning("AdvancedStatus plugin found, but not ready yet. Waiting for it to load...");
                else
                    PrintWarning("AdvancedStatus plugin not found! To function, it is necessary to install it!\n* https://codefling.com/plugins/advanced-status\n* https://lone.design/product/advanced-status/");
            }
            else
                OnAdvancedStatusLoaded();
            Subscribe(nameof(OnAdvancedStatusLoaded));
            Subscribe(nameof(OnPluginLoaded));
            Subscribe(nameof(OnPluginUnloaded));
        }
        
        void Unload()
        {
            Unsubscribe(nameof(OnPluginLoaded));
            Unsubscribe(nameof(OnPluginUnloaded));
            for (int i = 0; i < _defaultHooks.Length; i++)
                Unsubscribe(_defaultHooks[i]);
            Instance = null;
            _config = null;
        }
        #endregion

        #region ~Classes~
        public class TeleportHook
        {
            public bool IsEnabled { get; set; } = true;
            
            [JsonProperty(PropertyName = "Hook method name")]
            public string HookName { get; set; }
            
            [JsonProperty(PropertyName = "Language key for the text")]
            public string TextKey { get; set; }
            
            [JsonProperty(PropertyName = "Specify the status bar Id used for this hook")]
            public string BarConfigId { get; set; }
            
            [JsonProperty(PropertyName = "Is it worth sending the status bar for the target player?")]
            public bool SendTargetBar { get; set; }

            [JsonProperty(PropertyName = "Language key for the text for the target player")]
            public string TargetTextKey { get; set; }

            [JsonProperty(PropertyName = "Specify the status bar Id used for the target player")]
            public string TargetBarConfigId { get; set; }
            
            [JsonProperty(PropertyName = "Specify all hook arguments(type and name) in order, separated by commas. Example: BasePlayer target, BasePlayer player, int countdown")]
            public string ArgumentList { get; set; }
            
            [JsonProperty(PropertyName = "Indicates whether this hook represents a teleport cancelation event(e.g. OnTeleportInterrupted). Only InitiatorIndex is required")]
            public bool IsCancelation { get; set; }

            [JsonProperty(PropertyName = "Specify the index of the argument that contains the initiator")]
            public int InitiatorIndex { get; set; }
            
            [JsonProperty(PropertyName = "Specify the index of the argument that contains the target")]
            public int TargetIndex { get; set; } = -1;
            
            [JsonProperty(PropertyName = "Specify the argument index that contains the teleport countdown. Note: this index must not overlap with others, only int type is supported")]
            public int CountdownIndex { get; set; } = -1;
            
            public TeleportHook() {}
            public TeleportHook(string hookName, string textKey, string barConfigId, string args, int initiator, int target = -1, int coundown = -1, bool twoBars = false)
            {
                HookName = hookName;
                TextKey = textKey;
                BarConfigId = barConfigId;
                SendTargetBar = twoBars;
                ArgumentList = args;
                InitiatorIndex = initiator;
                TargetIndex = target;
                CountdownIndex = coundown;
                IsCancelation = TargetIndex < 0 || CountdownIndex < 0;
            }
            
            public bool TryGenerateHookString(out string result)
            {
                result = string.Empty;
                if (!IsEnabled)
                    return false;
                InitiatorIndex = Math.Max(InitiatorIndex, 0);
                if (!IsCancelation && CountdownIndex >= 0 && (CountdownIndex == InitiatorIndex || CountdownIndex == TargetIndex))
                {
                    IsEnabled = false;
                    Instance.PrintWarning($"Countdown Index in hook overlaps with other indices: {HookName} ({ArgumentList})");
                    return false;
                }
                
                string[] argParts = ArgumentList.Split(',').Select(x => x.Trim()).ToArray();
                if (InitiatorIndex >= argParts.Length)
                {
                    Instance.PrintWarning($"Initiator Index in hook out of avaible range({InitiatorIndex}/{argParts.Length - 1}): {HookName} ({ArgumentList})");
                    IsEnabled = false;
                    return false;
                }
                
                if (SendTargetBar && TargetIndex >= argParts.Length)
                    SendTargetBar = false;
                if (TargetIndex >= argParts.Length || CountdownIndex >= argParts.Length)
                    IsCancelation = true;
                
                result = $"void {HookName}({ArgumentList})";
                if (IsCancelation)
                {
                    result += $" {{ DeleteBar({GetArgumentValue(argParts[InitiatorIndex])});";
                    if (SendTargetBar)
                        result += $" DeleteBar({GetArgumentValue(argParts[TargetIndex])});";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(TextKey))
                        TextKey = "BarTeleportToPlayer";
                    if (string.IsNullOrWhiteSpace(BarConfigId))
                        BarConfigId = "ToPlayer";
                    if (!_config.BarList.ContainsKey(BarConfigId))
                        BarConfigId = _config.BarList.Keys.First();
                    string countdown = GetArgumentValue(argParts[CountdownIndex]);
                    result += $" {{ SendBar({GetArgumentValue(argParts[InitiatorIndex])}, {GetNameValue(argParts[TargetIndex])}, \"{TextKey}\", {countdown}, _config.BarList[\"{BarConfigId}\"]);";
                    if (SendTargetBar)
                    {
                        if (string.IsNullOrWhiteSpace(TargetTextKey))
                            TargetTextKey = "BarTeleportFromPlayer";
                        if (string.IsNullOrWhiteSpace(TargetBarConfigId))
                            TargetBarConfigId = "FromPlayer";
                        if (!_config.BarList.ContainsKey(TargetBarConfigId))
                            TargetBarConfigId = BarConfigId;
                        result += $" SendBar({GetArgumentValue(argParts[TargetIndex])}, {GetNameValue(argParts[InitiatorIndex])}, \"{TargetTextKey}\", {countdown}, _config.BarList[\"{TargetBarConfigId}\"]);";
                    }
                }
                result += " }";
                return true;
            }

            private string GetArgumentValue(string input) => input.Split(' ')[1].Trim();
            
            private string GetNameValue(string input)
            {
                string[] parts = input.Split(' ').Select(x => x.Trim()).ToArray();
                switch (parts[0])
                {
                    case "BasePlayer":
                        return $"{parts[1]}.displayName";
                    case "IPlayer":
                        return $"{parts[1]}.Name";
                    case "string":
                        return parts[1];
                    default:
                        return $"$\"{{{parts[1]}}}\"";
                }
            }
        }

        public class BarSettings
        {
            [JsonIgnore] public string BarId { get; set; }
            [JsonIgnore] public string ImageId { get; set; }
            [JsonIgnore] public Dictionary<int, object> StatusBar { get; set; }
            
            [JsonProperty(PropertyName = "BarType(TimeProgressCounter or TimeCounter)")]
            public string BarType { get; set; } = "TimeProgressCounter";
            
            public int Order { get; set; } = 21;
            public int Height { get; set; } = 26;
            
            [JsonProperty(PropertyName = "Main_Color(Hex or RGBA)")]
            public string Main_Color { get; set; } = "#FFFFFF";
            
            public float Main_Transparency { get; set; } = 0.15f;
            
            [JsonProperty(PropertyName = "Main_Material(empty to disable)")]
            public string Main_Material { get; set; } = string.Empty;
            
            public string Image_Url { get; set; } = "https://i.imgur.com/NuAl6T5.png";
            
            [JsonProperty(PropertyName = "Image_Local(Leave empty to use Image_Url)")]
            public string Image_Local { get; set; } = "TeleportStatus_Player";
            
            [JsonProperty(PropertyName = "Image_Sprite(Leave empty to use Image_Local or Image_Url)")]
            public string Image_Sprite { get; set; } = string.Empty;
            
            public bool Image_IsRawImage { get; set; } = false;
            
            [JsonProperty(PropertyName = "Image_Color(Hex or RGBA)")]
            public string Image_Color { get; set; } = "#15AC9D";
            
            public float Image_Transparency { get; set; } = 0.55f;
            
            [JsonProperty(PropertyName = "Is it worth enabling an outline for the image?")]
            public bool Image_Outline_Enabled { get; set; } = false;
            
            [JsonProperty(PropertyName = "Image_Outline_Color(Hex or RGBA)")]
            public string Image_Outline_Color { get; set; } = "0.1 0.3 0.8 0.9";
            
            public float Image_Outline_Transparency { get; set; } = 1f;
            public string Image_Outline_Distance { get; set; } = "0.75 0.75";
            public int Text_Size { get; set; } = 12;
            
            [JsonProperty(PropertyName = "Text_Color(Hex or RGBA)")]
            public string Text_Color { get; set; } = "#15AC9D";
            
            [JsonProperty(PropertyName = "Text_Font(https://umod.org/guides/rust/basic-concepts-of-gui#fonts)")]
            public string Text_Font { get; set; } = "RobotoCondensed-Bold.ttf";
            
            public int Text_Offset_Horizontal { get; set; } = 0;
            
            [JsonProperty(PropertyName = "Is it worth enabling an outline for the text?")]
            public bool Text_Outline_Enabled { get; set; } = false;
            
            [JsonProperty(PropertyName = "Text_Outline_Color(Hex or RGBA)")]
            public string Text_Outline_Color { get; set; } = "#000000";
            
            public float Text_Outline_Transparency { get; set; } = 1f;
            public string Text_Outline_Distance { get; set; } = "0.75 0.75";
            public int SubText_Size { get; set; } = 12;
            
            [JsonProperty(PropertyName = "SubText_Color(Hex or RGBA)")]
            public string SubText_Color { get; set; } = "#15AC9D";
            
            public string SubText_Font { get; set; } = "RobotoCondensed-Bold.ttf";
            
            [JsonProperty(PropertyName = "Is it worth enabling an outline for the sub text?")]
            public bool SubText_Outline_Enabled { get; set; } = false;
            
            [JsonProperty(PropertyName = "SubText_Outline_Color(Hex or RGBA)")]
            public string SubText_Outline_Color { get; set; } = "0.5 0.6 0.7 0.5";
            
            public float SubText_Outline_Transparency { get; set; } = 1f;
            public string SubText_Outline_Distance { get; set; } = "0.75 0.75";
            public bool Progress_Reverse { get; set; } = false;
            
            [JsonProperty(PropertyName = "Progress_Color(Hex or RGBA)")]
            public string Progress_Color { get; set; } = "#009688";
            
            public float Progress_Transparency { get; set; } = 0.7f;
            public string Progress_OffsetMin { get; set; } = "0 0";
            public string Progress_OffsetMax { get; set; } = "0 0";
            
            public void PrepareStatusBar()
            {
                if (BarType != "TimeProgressCounter" && BarType != "TimeCounter")
                    BarType = "TimeProgressCounter";
                StatusBar = new Dictionary<int, object>
                {
                    { 0, BarId },
                    { 1, Instance.Name },
                    { 2, BarType },
                    { 4, Order },
                    { 5, Height },
                    { 6, Main_Color },
                    { 11, Image_IsRawImage },
                    { 16, Text_Size },
                    { 17, Text_Color },
                    { 18, Text_Font },
                    { 23, SubText_Size },
                    { 24, SubText_Color },
                    { 25, SubText_Font }
                };

                if (Main_Color.StartsWith("#"))
                    StatusBar.Add(-6, Main_Transparency);
                if (!string.IsNullOrWhiteSpace(Main_Material))
                    StatusBar.Add(7, Main_Material);
                if (!Image_IsRawImage)
                {
                    StatusBar.Add(12, Image_Color);
                    if (Image_Color.StartsWith("#"))
                        StatusBar.Add(-12, Image_Transparency);
                }
                if (Image_Outline_Enabled)
                {
                    StatusBar.Add(13, Image_Outline_Color);
                    if (Image_Outline_Color.StartsWith("#"))
                        StatusBar.Add(-13, Image_Outline_Transparency);
                    StatusBar.Add(14, Image_Outline_Distance);
                }
                if (Text_Offset_Horizontal != 0)
                    StatusBar.Add(19, Text_Offset_Horizontal);
                if (Text_Outline_Enabled)
                {
                    StatusBar.Add(20, Text_Outline_Color);
                    if (Text_Outline_Color.StartsWith("#"))
                        StatusBar.Add(-20, Text_Outline_Transparency);
                    StatusBar.Add(21, Text_Outline_Distance);
                }
                if (SubText_Outline_Enabled)
                {
                    StatusBar.Add(26, SubText_Outline_Color);
                    if (SubText_Outline_Color.StartsWith("#"))
                        StatusBar.Add(-26, SubText_Outline_Transparency);
                    StatusBar.Add(27, SubText_Outline_Distance);
                }
                if (BarType == "TimeProgressCounter")
                {
                    StatusBar.Add(32, Progress_Reverse);
                    StatusBar.Add(33, Progress_Color);
                    if (Progress_Color.StartsWith("#"))
                        StatusBar.Add(-33, Progress_Transparency);
                    StatusBar.Add(34, Progress_OffsetMin);
                    StatusBar.Add(35, Progress_OffsetMax);
                }
            }
            
            public void SelectBarImage()
            {
                StatusBar.Remove(10);
                StatusBar.Remove(9);
                StatusBar.Remove(8);
                if (!string.IsNullOrWhiteSpace(Image_Sprite))
                    StatusBar.Add(10, Image_Sprite);
                else if (!string.IsNullOrWhiteSpace(Image_Local))
                    StatusBar.Add(9, Image_Local);
                else
                    StatusBar.Add(8, Instance._imgLibIsLoaded && Image_Url.StartsWithAny(Instance.HttpScheme) ? BarId : Image_Url);
            }
        }
        #endregion
        
        #region ~Teleport Hooks~
void OnTeleportAccepted(BasePlayer target, BasePlayer player, int countdown) { SendBar(player, target.displayName, "BarTeleportToPlayer", countdown, _config.BarList["ToPlayer"]); SendBar(target, player.displayName, "BarTeleportFromPlayer", countdown, _config.BarList["FromPlayer"]); }void OnHomeAccepted(BasePlayer player, string home, int countdown) { SendBar(player, home, "BarTeleportToHome", countdown, _config.BarList["Home"]); }void OnHomeAccepted(BasePlayer player, string home, Vector3 pos, int countdown) { SendBar(player, home, "BarTeleportToHome", countdown, _config.BarList["Home"]); }void OnTownAccepted(BasePlayer player, string name, int countdown) { SendBar(player, name, "BarTeleportToTown", countdown, _config.BarList["Town"]); }void OnTeleportBackAccepted(BasePlayer player, Vector3 location, int countdown) { SendBar(player, $"{location}", "BarTeleportToBack", countdown, _config.BarList["Back"]); }void OnWarpAccepted(BasePlayer player, Vector3 warpPosition, int countdown) { SendBar(player, $"{warpPosition}", "BarTeleportToWarp", countdown, _config.BarList["Warp"]); }void OnTeleportInterrupted(BasePlayer player) { DeleteBar(player); }
 #endregion
    }
}