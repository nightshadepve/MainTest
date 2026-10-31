using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("IslandStorm", "Grok", "1.0.0")]
    [Description("Island-wide total radiation storm with whipping winds, inspired by RadStorm")]
    public class IslandStorm : RustPlugin
    {
        #region Fields
        private PluginConfig config;
        private bool isStormActive;
        private Timer rampTimer;
        private Timer damageTimer;
        private Timer durationTimer;
        private float currentProgress; // 0 to 1 during ramp
        private bool isRampingUp;
        private bool isRampingDown;
        #endregion

        #region Config
        private class PluginConfig
        {
            public StormSettings Storm { get; set; } = new StormSettings();
            public BonusSettings Bonus { get; set; } = new BonusSettings();
            public Messages Messages { get; set; } = new Messages();
        }

        private class StormSettings
        {
            public float RampTimeSeconds { get; set; } = 120f;
            public float StormDurationSeconds { get; set; } = 300f;
            public float RadiationPerTick { get; set; } = 35f;
            public float DamageTickInterval { get; set; } = 2f;
            public float RadiationSuitMultiplier { get; set; } = 0.35f;
            public bool HurtSleepers { get; set; } = true;
            public bool OnlyOutside { get; set; } = true;
            public float PeakWind { get; set; } = 1.5f;
            public float PeakRain { get; set; } = 1.0f;
            public float PeakThunder { get; set; } = 1.0f;
            public float PeakFog { get; set; } = 0.8f;
            public float RedSkyRayleigh { get; set; } = 0.2f;
            public float RedSkyMie { get; set; } = 1.5f;
            public float RedSkyBrightness { get; set; } = 0.4f;
            public bool AutoStart { get; set; } = false;
            public float MinAutoInterval { get; set; } = 3600f;
            public float MaxAutoInterval { get; set; } = 7200f;
        }

        private class BonusSettings
        {
            public float GatherRateMultiplier { get; set; } = 1.5f;
            public float PickupRateMultiplier { get; set; } = 1.5f;
        }

        private class Messages
        {
            public string StormIncoming { get; set; } = "<color=#ff4444>A massive radiation storm is rolling in across the entire island! Seek shelter or wear protection!</color>";
            public string StormActive { get; set; } = "<color=#ff0000>THE STORM IS HERE! Whipping winds and deadly radiation engulf the island!</color>";
            public string StormEnding { get; set; } = "<color=#44ff44>The storm is clearing... radiation levels dropping.</color>";
            public string StormEnded { get; set; } = "<color=#00ff00>The island-wide storm has ended. All clear.</color>";
            public string NoPermission { get; set; } = "You don't have permission to control the storm.";
            public string AlreadyActive { get; set; } = "A storm is already active.";
            public string NotActive { get; set; } = "No storm is currently active.";
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null) LoadDefaultConfig();
            }
            catch
            {
                PrintWarning("Config file is corrupt, loading defaults.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            permission.RegisterPermission("islandstorm.admin", this);
            permission.RegisterPermission("islandstorm.exempt", this);
            Puts("IslandStorm loaded. Use /storm start (requires islandstorm.admin permission).");
        }

        private void OnServerInitialized()
        {
            Puts("IslandStorm ready. Grant permission with: oxide.grant user <steamid> islandstorm.admin");
            if (config != null && config.Storm.AutoStart)
            {
                ScheduleNextAutoStorm();
            }
        }

        private void Unload()
        {
            StopAllTimers();
            if (isStormActive)
            {
                ResetWeatherImmediate();
            }
        }

        private object OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (!isStormActive || player == null || item == null) return null;
            if (permission.UserHasPermission(player.UserIDString, "islandstorm.exempt")) return null;

            item.amount = Mathf.CeilToInt(item.amount * config.Bonus.GatherRateMultiplier);
            return null;
        }

        private void OnCollectiblePickedup(CollectibleEntity entity, BasePlayer player, Item item)
        {
            if (!isStormActive || player == null || item == null) return;
            if (permission.UserHasPermission(player.UserIDString, "islandstorm.exempt")) return;

            item.amount = Mathf.CeilToInt(item.amount * config.Bonus.PickupRateMultiplier);
        }
        #endregion

        #region Commands
        [ChatCommand("storm")]
        private void StormChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "islandstorm.admin"))
            {
                SendReply(player, config.Messages.NoPermission);
                return;
            }

            HandleStormCommand(player, args);
        }

        [ConsoleCommand("startstorm")]
        private void StormConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Player()?.UserIDString ?? "", "islandstorm.admin"))
            {
                arg.ReplyWith(config?.Messages?.NoPermission ?? "You don't have permission to control the storm.");
                return;
            }

            HandleStormCommand(null, arg.Args?.Select(a => a.ToString()).ToArray() ?? new string[0]);
        }

        private void HandleStormCommand(BasePlayer player, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                string status = isStormActive ? "ACTIVE" : "INACTIVE";
                string msg = $"IslandStorm status: {status}";
                if (player != null) SendReply(player, msg);
                else Puts(msg);
                return;
            }

            switch (args[0].ToLower())
            {
                case "start":
                    if (isStormActive)
                    {
                        Reply(player, config.Messages.AlreadyActive);
                        return;
                    }
                    StartStorm();
                    Reply(player, "Storm started.");
                    break;

                case "stop":
                    if (!isStormActive)
                    {
                        Reply(player, config.Messages.NotActive);
                        return;
                    }
                    StopStorm();
                    Reply(player, "Storm stopped.");
                    break;

                default:
                    Reply(player, "Usage: /storm <start|stop>   or   startstorm <start|stop>");
                    break;
            }
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player != null)
                SendReply(player, message);
            else
                Puts(message);
        }
        #endregion

        #region Storm Logic
        private void StartStorm()
        {
            if (isStormActive) return;

            isStormActive = true;
            isRampingUp = true;
            isRampingDown = false;
            currentProgress = 0.2f;

            string incoming = config?.Messages?.StormIncoming ?? "A massive radiation storm is rolling in across the entire island!";
            Broadcast(incoming);
            Puts("Island-wide storm starting!");

            // Force the built-in storm preset first for immediate effect
            RunWeatherCommand("weather.load", null, "storm");
            ApplyWeather(0.5f);

            StartDamageTimer();
            StartRampTimer(true);
        }

        private void StopStorm()
        {
            if (!isStormActive) return;

            isRampingUp = false;
            isRampingDown = true;
            Broadcast(config.Messages.StormEnding);

            StopDamageTimer();
            if (durationTimer != null && !durationTimer.Destroyed)
                durationTimer.Destroy();

            StartRampTimer(false);
        }

        private void StartRampTimer(bool rampUp)
        {
            if (rampTimer != null && !rampTimer.Destroyed)
                rampTimer.Destroy();

            float steps = 20f;
            float interval = config.Storm.RampTimeSeconds / steps;
            float progressStep = 1f / steps;

            rampTimer = timer.Every(interval, () =>
            {
                if (rampUp)
                {
                    currentProgress = Mathf.Min(1f, currentProgress + progressStep);
                    ApplyWeather(currentProgress);

                    if (currentProgress >= 1f)
                    {
                        isRampingUp = false;
                        OnPeakReached();
                        rampTimer.Destroy();
                    }
                }
                else
                {
                    currentProgress = Mathf.Max(0f, currentProgress - progressStep);
                    ApplyWeather(currentProgress);

                    if (currentProgress <= 0f)
                    {
                        isRampingDown = false;
                        isStormActive = false;
                        ResetWeatherImmediate();
                        Broadcast(config.Messages.StormEnded);
                        Puts("Storm fully ended.");
                        rampTimer.Destroy();

                        if (config.Storm.AutoStart)
                            ScheduleNextAutoStorm();
                    }
                }
            });
        }

        private void OnPeakReached()
        {
            Broadcast(config.Messages.StormActive);
            Puts("Storm peaked - extreme weather and peak radiation.");

            if (durationTimer != null && !durationTimer.Destroyed)
                durationTimer.Destroy();

            durationTimer = timer.Once(config.Storm.StormDurationSeconds, () =>
            {
                if (isStormActive)
                    StopStorm();
            });
        }

        private void StartDamageTimer()
        {
            if (damageTimer != null && !damageTimer.Destroyed)
                damageTimer.Destroy();

            damageTimer = timer.Every(config.Storm.DamageTickInterval, ApplyRadiationDamage);
        }

        private void StopDamageTimer()
        {
            if (damageTimer != null && !damageTimer.Destroyed)
                damageTimer.Destroy();
        }

        private void ApplyRadiationDamage()
        {
            if (!isStormActive) return;

            float totalProgress = GetTotalStormProgress();
            // Triangle / bell curve peaking at 0.5
            float intensity = totalProgress <= 0.5f
                ? totalProgress * 2f
                : (1f - totalProgress) * 2f;
            intensity = Mathf.Clamp01(intensity);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsDead()) continue;
                if (permission.UserHasPermission(player.UserIDString, "islandstorm.exempt")) continue;

                if (player.IsSleeping() && !config.Storm.HurtSleepers) continue;

                float buildingMultiplier = GetBuildingProtectionMultiplier(player);
                if (buildingMultiplier <= 0f) continue;

                float gearMultiplier = HasRadiationProtection(player) ? config.Storm.RadiationSuitMultiplier : 1.0f;

                float damage = config.Storm.RadiationPerTick * intensity * buildingMultiplier * gearMultiplier;
                if (damage > 0)
                {
                    player.metabolism.radiation_poison.Add(damage);
                    player.metabolism.SendChangesToClient();
                }
            }
        }

        private float GetTotalStormProgress()
        {
            // Approximate overall progress: ramp up (0-0.3), hold (0.3-0.7), ramp down (0.7-1.0)
            // For simplicity we use currentProgress during ramp and estimate during hold
            if (isRampingUp) return currentProgress * 0.3f;
            if (isRampingDown) return 0.7f + (1f - currentProgress) * 0.3f;
            return 0.5f; // peak during hold
        }

        /// <summary>
        /// Returns damage multiplier based on building grade above the player.
        /// Twig = 1.0 (no protection), Wood = 0.5 (half), Stone+ = 0.0 (full protection).
        /// Outside / no building = 1.0
        /// </summary>
        private float GetBuildingProtectionMultiplier(BasePlayer player)
        {
            RaycastHit hit;
            Vector3 origin = player.transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.up, out hit, 8f, LayerMask.GetMask("Construction", "Deployed")))
            {
                var block = hit.GetEntity() as BuildingBlock;
                if (block != null)
                {
                    switch (block.grade)
                    {
                        case BuildingGrade.Enum.Twigs:
                            return 1.0f; // No protection
                        case BuildingGrade.Enum.Wood:
                            return 0.5f; // Half protection
                        default:
                            return 0.0f; // Stone, Metal, Armored = full protection
                    }
                }
            }

            // No building overhead — full radiation if "outside" logic wants it
            return config.Storm.OnlyOutside ? 1.0f : 1.0f;
        }

        private bool HasRadiationProtection(BasePlayer player)
        {
            if (player.inventory?.containerWear == null) return false;

            foreach (var item in player.inventory.containerWear.itemList)
            {
                if (item == null || item.info == null) continue;
                string shortname = item.info.shortname.ToLower();
                if (shortname.Contains("hazmat") || shortname.Contains("hazmatsuit"))
                    return true;
            }
            return false;
        }

        private bool IsPlayerOutside(BasePlayer player)
        {
            // Simple check: not in a building privilege area or raycast up for roof
            if (player.IsBuildingBlocked()) return false;

            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + Vector3.up, Vector3.up, out hit, 10f, LayerMask.GetMask("Construction", "Deployed")))
            {
                return false; // Under something
            }
            return true;
        }

        private void ApplyWeather(float progress)
        {
            float wind = Mathf.Lerp(0f, config.Storm.PeakWind, progress);
            float rain = Mathf.Lerp(0f, config.Storm.PeakRain, progress);
            float thunder = Mathf.Lerp(0f, config.Storm.PeakThunder, progress);
            float fog = Mathf.Lerp(0f, config.Storm.PeakFog, progress);

            RunWeatherCommand("weather.wind", wind);
            RunWeatherCommand("weather.rain", rain);
            RunWeatherCommand("weather.thunder", thunder);
            RunWeatherCommand("weather.fog", fog);

            // Red sky effect
            float rayleigh = Mathf.Lerp(-1f, config.Storm.RedSkyRayleigh, progress);
            float mie = Mathf.Lerp(-1f, config.Storm.RedSkyMie, progress);
            float brightness = Mathf.Lerp(-1f, config.Storm.RedSkyBrightness, progress);

            RunWeatherCommand("weather.atmosphere_rayleigh", rayleigh);
            RunWeatherCommand("weather.atmosphere_mie", mie);
            RunWeatherCommand("weather.atmosphere_brightness", brightness);
        }

        private void ResetWeatherImmediate()
        {
            RunWeatherCommand("weather.reset", null);
            RunWeatherCommand("weather.wind", -1f);
            RunWeatherCommand("weather.rain", -1f);
            RunWeatherCommand("weather.thunder", -1f);
            RunWeatherCommand("weather.fog", -1f);
            RunWeatherCommand("weather.atmosphere_rayleigh", -1f);
            RunWeatherCommand("weather.atmosphere_mie", -1f);
            RunWeatherCommand("weather.atmosphere_brightness", -1f);
        }

        private void RunWeatherCommand(string command, float? value, string extra = null)
        {
            try
            {
                string valueStr = value.HasValue ? value.Value.ToString("F2") : (extra ?? "");
                Puts($"[IslandStorm] Running: {command} {valueStr}");

                if (value.HasValue)
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, command, value.Value.ToString("F2"));
                else if (!string.IsNullOrEmpty(extra))
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, command, extra);
                else
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to run weather command {command}: {ex.Message}");
            }
        }

        private void ScheduleNextAutoStorm()
        {
            float delay = UnityEngine.Random.Range(config.Storm.MinAutoInterval, config.Storm.MaxAutoInterval);
            timer.Once(delay, () =>
            {
                if (!isStormActive)
                    StartStorm();
            });
        }

        private void StopAllTimers()
        {
            rampTimer?.Destroy();
            damageTimer?.Destroy();
            durationTimer?.Destroy();
        }

        private void Broadcast(string message)
        {
            Server.Broadcast(message);
        }
        #endregion
    }
}
