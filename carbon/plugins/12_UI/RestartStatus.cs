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
*  Codefling plugin page: https://codefling.com/plugins/restart-status
*  Codefling license: https://codefling.com/plugins/restart-status?tab=downloads_field_4
*  
*  Lone.Design plugin page: https://lone.design/product/restart-status/
*
*  Copyright © 2025-2026 IIIaKa
*/

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
	[Info("Restart Status", "IIIaKa", "0.1.1")]
    [Description("The plugin displays the time until restart in the status bar. Depends on AdvancedStatus plugin.")]
    class RestartStatus : RustPlugin
	{
        [PluginReference]
		private Plugin ImageLibrary, AdvancedStatus;
		
		#region ~Variables~
		private bool _imgLibIsLoaded = false;
		private const string BarID = "RestartStatus_Restart", Str_TextKey = "BarText", StatusCreateBar = "CreateBar", StatusDeleteBarForAll = "DeleteBarForAll";
		private readonly string[] HttpScheme = new string[] { "http://", "https://" };
        private double _timeStamp = -1d;
        private Dictionary<int, object> _statusBar;
        #endregion

        #region ~Configuration~
        private static Configuration _config;

		private class Configuration
        {
            [JsonProperty(PropertyName = "Is it worth generating restart hooks? 0 - disabled, 1 - one-time activation, 2 - always enabled")]
            public int GenerateHooks = 2;
            
            [JsonProperty(PropertyName = "List of language keys for creating language files")]
            public List<string> LanguageKeys;
            
            [JsonProperty(PropertyName = "The type of the bar(TimeProgressCounter or TimeCounter)")]
            public string BarType = string.Empty;
            
            [JsonProperty(PropertyName = "The height of the bar")]
            public int BarHeight = 26;

            [JsonProperty(PropertyName = "The display order of the bar")]
            public int BarOrder = 10;

            [JsonProperty(PropertyName = "The background color of the bar(HEX or RGBA)")]
            public string BarBackgroundColor = string.Empty;

            [JsonProperty(PropertyName = "The background transparency of the bar")]
            public float BarBackgroundTransparency = 0.7f;

            [JsonProperty(PropertyName = "The background material of the bar, leave empty to disable")]
            public string BarBackgroundMaterial = string.Empty;

            [JsonProperty(PropertyName = "The URL of the bar image")]
            public string BarImageUrl = string.Empty;

            [JsonProperty(PropertyName = "The local image of the bar, leave empty to use URL")]
            public string BarImageLocal = "RestartStatus_Default";

            [JsonProperty(PropertyName = "The sprite image of the bar, leave empty to use local or URL")]
            public string BarImageSprite = string.Empty;

            [JsonProperty(PropertyName = "Is it worth using a raw image for the bar?")]
            public bool BarIsRawImage = false;

            [JsonProperty(PropertyName = "The color of the bar image(HEX or RGBA)")]
            public string BarImageColor = string.Empty;

            [JsonProperty(PropertyName = "The transparency of the bar image")]
            public float BarImageTransparency = 1f;

            [JsonProperty(PropertyName = "The outline color of the bar image(HEX or RGBA)")]
            public string BarImageOutlineColor = string.Empty;

            [JsonProperty(PropertyName = "The outline transparency of the bar image")]
            public float BarImageOutlineTransparency = 1f;

            [JsonProperty(PropertyName = "The outline distance of the bar image, leave empty to disable. Example '0.75 0.75'")]
            public string BarImageOutlineDistance = string.Empty;

            [JsonProperty(PropertyName = "The size of the bar text")]
            public int BarTextSize = 12;

            [JsonProperty(PropertyName = "The color of the bar text(HEX or RGBA)")]
            public string BarTextColor = string.Empty;

            [JsonProperty(PropertyName = "The transparency of the bar text")]
            public float BarTextTransparency = 1f;

            [JsonProperty(PropertyName = "The font of the bar text(https://umod.org/guides/rust/basic-concepts-of-gui#fonts)")]
            public string BarTextFont = string.Empty;

            [JsonProperty(PropertyName = "The horizontal offset of the bar text")]
            public int BarTextOffsetHorizontal = 0;

            [JsonProperty(PropertyName = "The outline color of the bar text(HEX or RGBA)")]
            public string BarTextOutlineColor = string.Empty;

            [JsonProperty(PropertyName = "The outline transparency of the bar text")]
            public float BarTextOutlineTransparency = 1f;

            [JsonProperty(PropertyName = "The outline distance of the bar text, leave empty to disable")]
            public string BarTextOutlineDistance = string.Empty;

            [JsonProperty(PropertyName = "The size of the bar subtext")]
            public int BarSubTextSize = 12;

            [JsonProperty(PropertyName = "The color of the bar subtext(HEX or RGBA)")]
            public string BarSubTextColor = string.Empty;

            [JsonProperty(PropertyName = "The transparency of the bar subtext")]
            public float BarSubTextTransparency = 1f;

            [JsonProperty(PropertyName = "The font of the bar subtext")]
            public string BarSubTextFont = string.Empty;

            [JsonProperty(PropertyName = "The outline color of the bar subtext(HEX or RGBA)")]
            public string BarSubTextOutlineColor = string.Empty;

            [JsonProperty(PropertyName = "The outline transparency of the bar subtext")]
            public float BarSubTextOutlineTransparency = 1f;

            [JsonProperty(PropertyName = "The outline distance of the bar subtext, leave empty to disable")]
            public string BarSubTextOutlineDistance = string.Empty;
            
            [JsonProperty(PropertyName = "The background color of the progress bar(HEX or RGBA)")]
            public string BarProgressBackgroundColor = string.Empty;
            
            [JsonProperty(PropertyName = "The background transparency of the progress bar")]
            public float BarProgressBackgroundTransparency = 0.15f;
            
            [JsonProperty(PropertyName = "Is it worth making the progress reversible?")]
            public bool BarProgressReverse = true;
			
			[JsonProperty(PropertyName = "The color of the bar progress(HEX or RGBA)")]
			public string BarProgressColor = string.Empty;
            
            [JsonProperty(PropertyName = "The transparency of the bar progress")]
			public float BarProgressTransparency = 0.7f;
            
            [JsonProperty(PropertyName = "The OffsetMin of the bar progress")]
			public string BarProgressOffsetMin = string.Empty;
			
			[JsonProperty(PropertyName = "The OffsetMax of the bar progress")]
			public string BarProgressOffsetMax = string.Empty;
            
            [JsonProperty(PropertyName = "The list of restart hooks. Note: After modifying hooks, do not forget to toggle hook generation")]
            public List<RestartHookCfg> RestartHooks;
            
            public Oxide.Core.VersionNumber Version;
		}
        
        public class RestartHookCfg
        {
            [JsonProperty(PropertyName = "Hook triggered at the start of a restart")]
            public string InitHook = string.Empty;
            
            [JsonProperty(PropertyName = "Argument position containing the number of seconds until restart(counting from 0)")]
            public int InitIndex;
            
            [JsonProperty(PropertyName = "Hook triggered at the cancellation of a restart")]
            public string CancellationHook = string.Empty;
            
            public RestartHookCfg() {}
            public RestartHookCfg(string initHook, int index, string cancelHook)
            {
                InitHook = initHook;
                InitIndex = index;
                CancellationHook = cancelHook;
            }
            
            public string GenerateHookString() => $"void {InitHook}({(InitIndex > 0 ? string.Join(", ", Enumerable.Range(0, InitIndex).Select(i => $"object obj{i}")) : "object obj0")}) => OnServerRestart(string.Empty, Convert.ToInt32(obj{InitIndex}));void {CancellationHook}() => OnServerRestart(string.Empty, -1);";
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
                PrintWarning($"Your configuration file version({_config.Version}) is outdated. Updating it to {Version}...");
                string cfgPath = $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}{Name}.json";
                if (File.Exists(cfgPath))
                    File.Move(cfgPath, $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}_old_{Name}({_config.Version}).json");
                _config.Version = Version;
                _config.GenerateHooks = Math.Max(_config.GenerateHooks, 1);
                PrintWarning($"The configuration file has been successfully updated to version {_config.Version}!");
            }
            
            _config.GenerateHooks = Math.Clamp(_config.GenerateHooks, 0, 2);
            
            string langKey;
            var oldLangKeys = _config.LanguageKeys ?? new List<string>();
            _config.LanguageKeys = new List<string>() { "en" };
            for (int i = 0; i < oldLangKeys.Count; i++)
            {
                langKey = ToLangKey(oldLangKeys[i]);
                if (!langKey.Equals("ru", StringComparison.OrdinalIgnoreCase) && !_config.LanguageKeys.Contains(langKey, StringComparer.OrdinalIgnoreCase))
                    _config.LanguageKeys.Add(langKey);
            }
            
            if (_config.BarType != "TimeProgressCounter" && _config.BarType != "TimeCounter")
				_config.BarType = "TimeCounter";
            _config.BarHeight = Math.Max(_config.BarHeight, 1);
            if (string.IsNullOrWhiteSpace(_config.BarBackgroundColor))
                _config.BarBackgroundColor = "#E5423F";
            _config.BarBackgroundTransparency = Mathf.Clamp01(_config.BarBackgroundTransparency);
            _config.BarBackgroundMaterial ??= string.Empty;
            if (string.IsNullOrWhiteSpace(_config.BarImageUrl))
                _config.BarImageUrl = "https://i.imgur.com/rAiHloW.png";
            if (string.IsNullOrWhiteSpace(_config.BarImageColor))
                _config.BarImageColor = "#E5423F";
            _config.BarImageTransparency = Mathf.Clamp01(_config.BarImageTransparency);
            if (string.IsNullOrWhiteSpace(_config.BarImageOutlineColor))
                _config.BarImageOutlineColor = "0.1 0.3 0.8 0.9";
            _config.BarImageOutlineTransparency = Mathf.Clamp01(_config.BarImageOutlineTransparency);
            _config.BarTextSize = Math.Max(_config.BarTextSize, 1);
            if (string.IsNullOrWhiteSpace(_config.BarTextColor))
                _config.BarTextColor = "#FFFFFF";
            _config.BarTextTransparency = Mathf.Clamp01(_config.BarTextTransparency);
            if (string.IsNullOrWhiteSpace(_config.BarTextFont))
                _config.BarTextFont = "RobotoCondensed-Bold.ttf";
            if (string.IsNullOrWhiteSpace(_config.BarTextOutlineColor))
                _config.BarTextOutlineColor = "#000000";
            _config.BarTextOutlineTransparency = Mathf.Clamp01(_config.BarTextOutlineTransparency);
            _config.BarSubTextSize = Math.Max(_config.BarSubTextSize, 1);
            if (string.IsNullOrWhiteSpace(_config.BarSubTextColor))
                _config.BarSubTextColor = "#FFFFFF";
            _config.BarSubTextTransparency = Mathf.Clamp01(_config.BarSubTextTransparency);
            if (string.IsNullOrWhiteSpace(_config.BarSubTextFont))
                _config.BarSubTextFont = "RobotoCondensed-Bold.ttf";
            if (string.IsNullOrWhiteSpace(_config.BarSubTextOutlineColor))
                _config.BarSubTextOutlineColor = "0.5 0.6 0.7 0.5";
            _config.BarSubTextOutlineTransparency = Mathf.Clamp01(_config.BarSubTextOutlineTransparency);
            if (string.IsNullOrWhiteSpace(_config.BarProgressBackgroundColor))
                _config.BarProgressBackgroundColor = "1 1 1 0.15";
            _config.BarProgressBackgroundTransparency = Mathf.Clamp01(_config.BarProgressBackgroundTransparency);
            if (string.IsNullOrWhiteSpace(_config.BarProgressColor))
                _config.BarProgressColor = "#E5423F";
            _config.BarProgressTransparency = Mathf.Clamp01(_config.BarProgressTransparency);
            if (string.IsNullOrWhiteSpace(_config.BarProgressOffsetMin))
                _config.BarProgressOffsetMin = "0 0";
            if (string.IsNullOrWhiteSpace(_config.BarProgressOffsetMax))
                _config.BarProgressOffsetMax = "0 0";
            
            _statusBar = new Dictionary<int, object>
            {
                { 0, BarID },
                { 1, Name },
                { 2, _config.BarType },
                { 4, _config.BarOrder },
                { 5, _config.BarHeight },
                { 6, _config.BarBackgroundColor },
                { 11, _config.BarIsRawImage },
                { 16, _config.BarTextSize },
                { 17, _config.BarTextColor },
                { 18, _config.BarTextFont },
                { 23, _config.BarSubTextSize },
                { 24, _config.BarSubTextColor },
                { 25, _config.BarSubTextFont }
            };
            
            if (_config.BarBackgroundColor.StartsWith("#"))
                _statusBar.Add(-6, _config.BarBackgroundTransparency);
            if (!string.IsNullOrWhiteSpace(_config.BarBackgroundMaterial))
                _statusBar.Add(7, _config.BarBackgroundMaterial);
            if (!_config.BarIsRawImage)
            {
                _statusBar.Add(12, _config.BarImageColor);
                if (_config.BarImageColor.StartsWith("#"))
                    _statusBar.Add(-12, _config.BarImageTransparency);
            }
            if (!string.IsNullOrWhiteSpace(_config.BarImageOutlineDistance))
            {
                _statusBar.Add(13, _config.BarImageOutlineColor);
                if (_config.BarImageOutlineColor.StartsWith("#"))
                    _statusBar.Add(-13, _config.BarImageOutlineTransparency);
                _statusBar.Add(14, _config.BarImageOutlineDistance);
            }
            if (_config.BarTextColor.StartsWith("#"))
                _statusBar.Add(-17, _config.BarTextTransparency);
            if (_config.BarTextOffsetHorizontal != 0)
                _statusBar.Add(19, _config.BarTextOffsetHorizontal);
            if (!string.IsNullOrWhiteSpace(_config.BarTextOutlineDistance))
            {
                _statusBar.Add(20, _config.BarTextOutlineColor);
                if (_config.BarTextOutlineColor.StartsWith("#"))
                    _statusBar.Add(-20, _config.BarTextOutlineTransparency);
                _statusBar.Add(21, _config.BarTextOutlineDistance);
            }
            if (_config.BarSubTextColor.StartsWith("#"))
                _statusBar.Add(-24, _config.BarSubTextTransparency);
            if (!string.IsNullOrWhiteSpace(_config.BarSubTextOutlineDistance))
            {
                _statusBar.Add(26, _config.BarSubTextOutlineColor);
                if (_config.BarSubTextOutlineColor.StartsWith("#"))
                    _statusBar.Add(-26, _config.BarSubTextOutlineTransparency);
                _statusBar.Add(27, _config.BarSubTextOutlineDistance);
            }
            if (_config.BarType == "TimeProgressCounter")
            {
                _statusBar[6] = _config.BarProgressBackgroundColor;
                if (_config.BarProgressBackgroundColor.StartsWith("#"))
                    _statusBar[-6] = _config.BarProgressBackgroundTransparency;
                _statusBar.Add(32, _config.BarProgressReverse);
				_statusBar.Add(33, _config.BarProgressColor);
				if (_config.BarProgressColor.StartsWith("#"))
					_statusBar.Add(-33, _config.BarProgressTransparency);
				_statusBar.Add(34, _config.BarProgressOffsetMin);
				_statusBar.Add(35, _config.BarProgressOffsetMax);
			}
            
            if (_config.RestartHooks == null || !_config.RestartHooks.Any())
                _config.RestartHooks = new List<RestartHookCfg>() { new RestartHookCfg("OnSmoothRestartInit", 0, "OnSmoothRestartCancelled"), new RestartHookCfg("OnRestartScheduled", 0, "OnRestartCancelled") };
            else
            {
                RestartHookCfg restartHook;
                for (int i = _config.RestartHooks.Count - 1; i >= 0; i--)
                {
                    restartHook = _config.RestartHooks[i];
                    if (restartHook == null || string.IsNullOrWhiteSpace(restartHook.InitHook) || string.IsNullOrWhiteSpace(restartHook.CancellationHook))
                        _config.RestartHooks.RemoveAt(i);
                    else
                        restartHook.InitIndex = Math.Max(restartHook.InitIndex, 0);
                }
            }
            
            SaveConfig();
            if (_config.GenerateHooks < 1)
                return;

            if (_config.GenerateHooks < 2)
            {
                _config.GenerateHooks = 0;
                SaveConfig();
            }

            string path = this.FilePath,
                sourceCode = File.ReadAllText(path);
            int startIndex = sourceCode.LastIndexOf("#region ~Restart Hooks~", StringComparison.OrdinalIgnoreCase),
                endIndex = sourceCode.LastIndexOf("#endregion", StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0 || endIndex <= startIndex)
            {
                PrintError("Failed to find a region for restart hooks! Try installing a clean plugin.");
                return;
            }
            startIndex += 23;
            endIndex--;

            var list = new HashSet<string>();
            for (int i = 0; i < _config.RestartHooks.Count; i++)
                list.Add(_config.RestartHooks[i].GenerateHookString());
            string existingHooks = sourceCode.Substring(startIndex, endIndex - startIndex).Trim(), newHooks = string.Concat(list);
            if (existingHooks == newHooks)
                return;
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
            var enLang = new Dictionary<string, string> { [Str_TextKey] = "RESTART IN" };
            var ruLang = new Dictionary<string, string> { [Str_TextKey] = "РЕСТАРТ ЧЕРЕЗ" };
            for (int i = 0; i < _config.LanguageKeys.Count; i++)
                lang.RegisterMessages(enLang, this, _config.LanguageKeys[i]);
            lang.RegisterMessages(ruLang, this, "ru");
        }
        #endregion

        #region ~Methods~
        private void SendBar(ulong userID) => AdvancedStatus?.Call(StatusCreateBar, userID, new Dictionary<int, object>(_statusBar) { { 15, lang.GetMessage(Str_TextKey, this, userID.ToString()) }, { 29, _timeStamp } });
        
        private static string ToLangKey(string langKey)
        {
            if (string.IsNullOrWhiteSpace(langKey))
                return "en";

            var parts = langKey.Split('-');
            if (parts.Length < 1 || !parts[0].All(c => char.IsLetter(c)))
                return "en";

            if (parts.Length < 2 || !parts[1].All(c => char.IsLetter(c)))
                return parts[0].ToLowerInvariant();

            return $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";
        }
        
        private void SelectBarImage()
        {
            _statusBar.Remove(10);
            _statusBar.Remove(9);
            _statusBar.Remove(8);
            if (!string.IsNullOrWhiteSpace(_config.BarImageSprite))
                _statusBar.Add(10, _config.BarImageSprite);
            else if (!string.IsNullOrWhiteSpace(_config.BarImageLocal))
                _statusBar.Add(9, _config.BarImageLocal);
            else
                _statusBar.Add(8, _imgLibIsLoaded && _config.BarImageUrl.StartsWithAny(HttpScheme) ? BarID : _config.BarImageUrl);
        }
        
        private void ToggleImageLib(bool isLoaded)
        {
            _imgLibIsLoaded = isLoaded;
            if (_imgLibIsLoaded)
            {
                if (string.IsNullOrWhiteSpace(_config.BarImageSprite) && string.IsNullOrWhiteSpace(_config.BarImageLocal) && _config.BarImageUrl.StartsWithAny(HttpScheme))
                    ImageLibrary?.Call("AddImage", _config.BarImageUrl, BarID, 0uL);
            }
            SelectBarImage();
        }
        #endregion

        #region ~Oxide Hooks~
        void OnPlayerConnected(BasePlayer player) => SendBar(player.userID);
        
        void OnServerRestart(string strNotice, int seconds)
        {
            if (seconds > 0)
            {
                double currentTimestamp = Network.TimeEx.currentTimestamp;
                if (_config.BarType == "TimeProgressCounter")
                    _statusBar[28] = currentTimestamp;
                _timeStamp = currentTimestamp + seconds;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player.userID.IsSteamId())
                        SendBar(player.userID);
                }
                Subscribe(nameof(OnPlayerConnected));
                return;
            }
            
            _statusBar.Remove(28);
            _timeStamp = -1d;
            AdvancedStatus?.Call(StatusDeleteBarForAll, BarID, Name);
            Unsubscribe(nameof(OnPlayerConnected));
        }
        
        void OnAdvancedStatusLoaded()
        {
            if (!string.IsNullOrWhiteSpace(_config.BarImageLocal))
				AdvancedStatus?.Call("LoadImage", _config.BarImageLocal);
			SelectBarImage();
            
            if (_timeStamp < 0d)
                return;
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.userID.IsSteamId())
                    SendBar(player.userID);
            }
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
                Unsubscribe(nameof(OnPlayerConnected));
        }
		
		void Init()
        {
            Unsubscribe(nameof(OnPluginLoaded));
            Unsubscribe(nameof(OnPluginUnloaded));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnServerRestart));
            Unsubscribe(nameof(OnAdvancedStatusLoaded));
            RestartHookCfg restartHook;
            for (int i = 0; i < _config.RestartHooks.Count; i++)
            {
                restartHook = _config.RestartHooks[i];
                Unsubscribe(restartHook.InitHook);
                Unsubscribe(restartHook.CancellationHook);
            }
        }
		
		void OnServerInitialized(bool initial)
        {
            ToggleImageLib(ImageLibrary != null && ImageLibrary.IsLoaded);
            if (AdvancedStatus != null && AdvancedStatus?.Call("IsReady") != null)
				OnAdvancedStatusLoaded();
			else
			{
                if (initial && AdvancedStatus != null)
                    PrintWarning("AdvancedStatus plugin found, but not ready yet. Waiting for it to load...");
                else
                    PrintWarning("AdvancedStatus plugin not found! To function, it is necessary to install it!\n* https://codefling.com/plugins/advanced-status\n* https://lone.design/product/advanced-status/");
            }
            Subscribe(nameof(OnAdvancedStatusLoaded));
            Subscribe(nameof(OnServerRestart));
            RestartHookCfg restartHook;
            for (int i = 0; i < _config.RestartHooks.Count; i++)
            {
                restartHook = _config.RestartHooks[i];
                Subscribe(restartHook.InitHook);
                Subscribe(restartHook.CancellationHook);
            }
            Subscribe(nameof(OnPluginLoaded));
            Subscribe(nameof(OnPluginUnloaded));
        }
        
        void Unload() => _config = null;
        #endregion

        #region ~Restart Hooks~
void OnSmoothRestartInit(object obj0) => OnServerRestart(string.Empty, Convert.ToInt32(obj0));void OnSmoothRestartCancelled() => OnServerRestart(string.Empty, -1);void OnRestartScheduled(object obj0) => OnServerRestart(string.Empty, Convert.ToInt32(obj0));void OnRestartCancelled() => OnServerRestart(string.Empty, -1);
 #endregion
    }
}