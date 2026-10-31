using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeConsoleFilter", "Nightshade", "1.0.1")]
    [Description("Suppresses selected Unity/Rust console spam after Carbon loads.")]
    public class NightshadeConsoleFilter : RustPlugin
    {
        private const string HarmonyId = "com.nightshade.consolefilter";

        private static NightshadeConsoleFilter _instance;
        private Harmony _harmony;
        private PluginConfig _config;
        private long _suppressedTotal;
        private long _suppressedSinceReport;

        private class PluginConfig
        {
            public bool Enabled = true;
            public bool IgnoreCase = true;
            public bool OnlyFilterWarningsAndLogs = false;
            public bool PrintStartupMessage = true;
            public float ReportSuppressedCountEverySeconds = 0f;

            public List<string> ContainsFilters = new List<string>
            {
                "Agent still not on navmesh after a warp",
                "No navmesh areas matching agent type?",
                "Failed to create agent because it is not close enough to the NavMesh",
                "failed to sample navmesh"
            };
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();

                if (_config == null)
                {
                    throw new Exception("Config was null");
                }

                if (_config.ContainsFilters == null || _config.ContainsFilters.Count == 0)
                {
                    _config.ContainsFilters = new PluginConfig().ContainsFilters;
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read config. Loading defaults. Error: {ex.Message}");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Init()
        {
            _instance = this;
            LoadConfig();

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll();

                if (_config.PrintStartupMessage)
                {
                    Puts($"Loaded. Active filters: {_config.ContainsFilters.Count}. Suppressing selected Unity/Rust log spam after plugin load.");
                }
            }
            catch (Exception ex)
            {
                PrintError($"Harmony patch failed: {ex}");
            }

            if (_config.ReportSuppressedCountEverySeconds > 0f)
            {
                timer.Every(_config.ReportSuppressedCountEverySeconds, ReportSuppressedCount);
            }
        }

        private void Unload()
        {
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                PrintWarning($"Harmony unpatch warning: {ex.Message}");
            }

            _harmony = null;
            _instance = null;
        }

        private void ReportSuppressedCount()
        {
            if (_suppressedSinceReport <= 0)
            {
                return;
            }

            Puts($"Suppressed {_suppressedSinceReport} matching console message(s). Total suppressed this load: {_suppressedTotal}.");
            _suppressedSinceReport = 0;
        }

        private bool ShouldSuppress(LogType logType, string message)
        {
            if (_config == null || !_config.Enabled || string.IsNullOrEmpty(message))
            {
                return false;
            }

            if (_config.OnlyFilterWarningsAndLogs && logType != LogType.Warning && logType != LogType.Log)
            {
                return false;
            }

            StringComparison comparison = _config.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            for (int i = 0; i < _config.ContainsFilters.Count; i++)
            {
                string filter = _config.ContainsFilters[i];

                if (string.IsNullOrEmpty(filter))
                {
                    continue;
                }

                if (message.IndexOf(filter, comparison) >= 0)
                {
                    _suppressedTotal++;
                    _suppressedSinceReport++;
                    return true;
                }
            }

            return false;
        }

        private static string BuildMessage(string format, object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        private static string ObjectToMessage(object message)
        {
            try
            {
                return message?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        [ConsoleCommand("nsfilter.status")]
        private void CmdStatus(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAccess(arg))
            {
                return;
            }

            SendReply(arg, $"NightshadeConsoleFilter enabled: {_config.Enabled}. Filters: {_config.ContainsFilters.Count}. Suppressed this load: {_suppressedTotal}.");
        }

        [ConsoleCommand("nsfilter.toggle")]
        private void CmdToggle(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAccess(arg))
            {
                return;
            }

            _config.Enabled = !_config.Enabled;
            SaveConfig();
            SendReply(arg, $"NightshadeConsoleFilter enabled: {_config.Enabled}");
        }

        [ConsoleCommand("nsfilter.reload")]
        private void CmdReload(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAccess(arg))
            {
                return;
            }

            LoadConfig();
            SendReply(arg, "NightshadeConsoleFilter config reloaded.");
        }

        [ConsoleCommand("nsfilter.test")]
        private void CmdTest(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAccess(arg))
            {
                return;
            }

            Debug.LogWarning("Agent still not on navmesh after a warp. No navmesh areas matching agent type? Agent type: 0");
            SendReply(arg, "Sent a test warning. If the filter is working, the navmesh test warning should not appear above this line.");
        }

        private bool HasConsoleAccess(ConsoleSystem.Arg arg)
        {
            return arg.Connection == null || arg.Connection.authLevel >= 2;
        }

        [HarmonyPatch]
        private static class DebugLogHandlerLogFormatPatch
        {
            private static MethodBase TargetMethod()
            {
                Type debugLogHandlerType = AccessTools.TypeByName("UnityEngine.DebugLogHandler");

                if (debugLogHandlerType == null)
                {
                    return null;
                }

                return AccessTools.Method(
                    debugLogHandlerType,
                    "LogFormat",
                    new[] { typeof(LogType), typeof(UnityEngine.Object), typeof(string), typeof(object[]) }
                );
            }

            private static bool Prefix(LogType logType, UnityEngine.Object context, string format, object[] args)
            {
                NightshadeConsoleFilter plugin = _instance;

                if (plugin == null)
                {
                    return true;
                }

                string message = BuildMessage(format, args);
                return !plugin.ShouldSuppress(logType, message);
            }
        }

        [HarmonyPatch]
        private static class UnityLoggerLogFormatPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Logger),
                    "LogFormat",
                    new[] { typeof(LogType), typeof(string), typeof(object[]) }
                );
            }

            private static bool Prefix(LogType logType, string format, object[] args)
            {
                NightshadeConsoleFilter plugin = _instance;

                if (plugin == null)
                {
                    return true;
                }

                string message = BuildMessage(format, args);
                return !plugin.ShouldSuppress(logType, message);
            }
        }

        [HarmonyPatch]
        private static class UnityLoggerLogPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Logger),
                    "Log",
                    new[] { typeof(LogType), typeof(object) }
                );
            }

            private static bool Prefix(LogType logType, object message)
            {
                NightshadeConsoleFilter plugin = _instance;

                if (plugin == null)
                {
                    return true;
                }

                return !plugin.ShouldSuppress(logType, ObjectToMessage(message));
            }
        }
    }
}
