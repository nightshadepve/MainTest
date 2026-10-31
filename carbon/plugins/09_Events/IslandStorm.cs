using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    // Local Nightshade change on Grok's IslandStorm 1.0.0.
    // Weather eases in and out. Replacing this file with a stock 1.0.0 copy removes that.
    [Info("IslandStorm", "Grok", "1.1.0")]
    [Description("Island-wide radiation storm with whipping winds. Nightshade build: weather eases in and out (based on Grok 1.0.0).")]
    public class IslandStorm : RustPlugin
    {
        #region Fields
        private const string BackupFile = "IslandStorm_Weather";

        private PluginConfig config;
        private bool isStormActive;
        private Timer weatherTimer;
        private Timer damageTimer;
        private Timer durationTimer;
        private Timer autoTimer;
        private float blend; // 0 = captured baseline, 1 = storm peaks
        private float rampFrom;
        private float rampTo;
        private float rampDuration;
        private float rampStartedAt;
        private bool rampActive;
        private StormPhase phase = StormPhase.Idle;
        private List<WeatherChannel> channels;

        private enum StormPhase
        {
            Idle,
            Rising,
            Peak,
            Falling
        }

        private class WeatherChannel
        {
            public string Key;
            public float Original;
            public float Baseline;
            public float Target;
        }

        private class WeatherBackup
        {
            public bool Dirty;
            public List<BackupChannel> Channels = new List<BackupChannel>();
        }

        private class BackupChannel
        {
            public string Key;
            public float Original;
        }
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
            public float WeatherTickSeconds { get; set; } = 1.5f;
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
            public float PeakCloudCoverage { get; set; } = 1.0f;
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
                if (config == null)
                {
                    LoadDefaultConfig();
                    return;
                }
            }
            catch
            {
                PrintWarning("Config file is corrupt, loading defaults.");
                LoadDefaultConfig();
                return;
            }

            if (config.Storm == null) config.Storm = new StormSettings();
            if (config.Bonus == null) config.Bonus = new BonusSettings();
            if (config.Messages == null) config.Messages = new Messages();

            bool corrected = false;
            if (config.Storm.WeatherTickSeconds < 0.5f)
            {
                config.Storm.WeatherTickSeconds = 1.5f;
                corrected = true;
            }
            if (config.Storm.RampTimeSeconds < 1f)
            {
                config.Storm.RampTimeSeconds = 120f;
                corrected = true;
            }
            if (config.Storm.DamageTickInterval < 0.5f)
            {
                config.Storm.DamageTickInterval = 2f;
                corrected = true;
            }
            if (corrected)
                SaveConfig();
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
            TryRestoreBackupFromDisk();
            if (config != null && config.Storm.AutoStart && phase == StormPhase.Idle)
                ScheduleNextAutoStorm();
        }

        private void OnServerShutdown()
        {
            StopAllTimers();
            if (channels != null && channels.Count > 0)
                RestoreOriginalsImmediate();
            phase = StormPhase.Idle;
            isStormActive = false;
        }

        private void Unload()
        {
            StopAllTimers();
            if (channels != null && channels.Count > 0)
            {
                RestoreOriginalsImmediate();
                Puts("IslandStorm unloaded. Pre-storm weather restored.");
            }
            else
            {
                TryRestoreBackupFromDisk();
            }

            phase = StormPhase.Idle;
            isStormActive = false;
        }

        private object OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (!isStormActive || player == null || item == null) return null;
            if (permission.UserHasPermission(player.UserIDString, "islandstorm.exempt")) return null;

            item.amount = Mathf.CeilToInt(item.amount * config.Bonus.GatherRateMultiplier);
            return null;
        }

        // The old hook was named OnCollectiblePickedup and never ran.
        private void OnCollectiblePickup(CollectibleEntity entity, BasePlayer player)
        {
            if (!isStormActive || player == null || entity == null || entity.itemList == null) return;
            if (permission.UserHasPermission(player.UserIDString, "islandstorm.exempt")) return;

            foreach (var item in entity.itemList)
            {
                if (item == null) continue;
                item.amount = Mathf.CeilToInt(item.amount * config.Bonus.PickupRateMultiplier);
            }
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
                Reply(player, StatusText());
                return;
            }

            switch (args[0].ToLower())
            {
                case "start":
                    if (phase != StormPhase.Idle)
                    {
                        Reply(player, config.Messages.AlreadyActive);
                        return;
                    }
                    StartStorm();
                    Reply(player, "Storm is rolling in.");
                    break;

                case "stop":
                    if (phase == StormPhase.Idle)
                    {
                        Reply(player, config.Messages.NotActive);
                        return;
                    }
                    if (phase == StormPhase.Falling)
                    {
                        Reply(player, "The storm is already clearing.");
                        return;
                    }
                    StopStorm();
                    Reply(player, "Storm is clearing.");
                    break;

                default:
                    Reply(player, "Usage: /storm <start|stop>   or   startstorm <start|stop>");
                    break;
            }
        }

        private string StatusText()
        {
            if (phase == StormPhase.Idle)
                return "IslandStorm status: inactive";

            string label = phase == StormPhase.Rising ? "rolling in"
                : phase == StormPhase.Peak ? "peak"
                : "clearing";
            return $"IslandStorm status: {label} ({Mathf.RoundToInt(blend * 100f)}%).";
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
            if (phase != StormPhase.Idle) return;

            Kill(ref autoTimer);
            CaptureChannels();
            isStormActive = true;
            phase = StormPhase.Rising;
            blend = 0f;

            Broadcast(config.Messages.StormIncoming);
            Puts($"IslandStorm rolling in over {config.Storm.RampTimeSeconds:0}s.");

            StartDamageTimer();
            BeginRamp(1f);
        }

        private void StopStorm()
        {
            if (phase == StormPhase.Idle || phase == StormPhase.Falling) return;

            phase = StormPhase.Falling;
            Kill(ref durationTimer);
            Broadcast(config.Messages.StormEnding);
            Puts("IslandStorm clearing.");
            BeginRamp(0f);
        }

        private void BeginRamp(float target)
        {
            rampFrom = blend;
            rampTo = target;
            rampStartedAt = Time.realtimeSinceStartup;
            float distance = Mathf.Abs(target - blend);
            rampDuration = Mathf.Max(config.Storm.WeatherTickSeconds, config.Storm.RampTimeSeconds * Mathf.Clamp01(distance));

            if (distance <= 0.001f)
            {
                rampActive = false;
                blend = target;
                ApplyBlend();
                OnRampFinished();
                return;
            }

            rampActive = true;
            EnsureWeatherTimer();
            WeatherTick();
        }

        private void EnsureWeatherTimer()
        {
            if (weatherTimer != null && !weatherTimer.Destroyed) return;
            weatherTimer = timer.Every(config.Storm.WeatherTickSeconds, WeatherTick);
        }

        private void WeatherTick()
        {
            if (!rampActive) return;

            float elapsed = Time.realtimeSinceStartup - rampStartedAt;
            float t = rampDuration <= 0.01f ? 1f : Mathf.Clamp01(elapsed / rampDuration);
            float eased = SmoothStep(t);
            blend = Mathf.Lerp(rampFrom, rampTo, eased);
            ApplyBlend();

            if (t < 1f) return;

            rampActive = false;
            blend = rampTo;
            ApplyBlend();
            OnRampFinished();
        }

        private static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void OnRampFinished()
        {
            Kill(ref weatherTimer);

            if (phase == StormPhase.Rising)
            {
                phase = StormPhase.Peak;
                blend = 1f;
                ApplyBlend();
                OnPeakReached();
                return;
            }

            if (phase == StormPhase.Falling)
                FinishStorm();
        }

        private void OnPeakReached()
        {
            Broadcast(config.Messages.StormActive);
            Puts("IslandStorm peaked.");

            Kill(ref durationTimer);
            if (config.Storm.StormDurationSeconds <= 0f)
            {
                StopStorm();
                return;
            }

            durationTimer = timer.Once(config.Storm.StormDurationSeconds, () =>
            {
                durationTimer = null;
                if (phase == StormPhase.Peak)
                    StopStorm();
            });
        }

        private void FinishStorm()
        {
            RestoreOriginalsImmediate();
            Kill(ref damageTimer);
            Kill(ref weatherTimer);
            Kill(ref durationTimer);
            phase = StormPhase.Idle;
            isStormActive = false;
            blend = 0f;
            rampActive = false;

            Broadcast(config.Messages.StormEnded);
            Puts("IslandStorm ended. Weather restored.");

            if (config.Storm.AutoStart)
                ScheduleNextAutoStorm();
        }

        private void StartDamageTimer()
        {
            Kill(ref damageTimer);
            damageTimer = timer.Every(config.Storm.DamageTickInterval, ApplyRadiationDamage);
        }

        private void ApplyRadiationDamage()
        {
            if (!isStormActive || blend <= 0f) return;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsDead()) continue;
                if (permission.UserHasPermission(player.UserIDString, "islandstorm.exempt")) continue;
                if (player.IsSleeping() && !config.Storm.HurtSleepers) continue;

                float buildingMultiplier = GetBuildingProtectionMultiplier(player);
                if (buildingMultiplier <= 0f) continue;

                float gearMultiplier = HasRadiationProtection(player) ? config.Storm.RadiationSuitMultiplier : 1.0f;
                float damage = config.Storm.RadiationPerTick * blend * buildingMultiplier * gearMultiplier;
                if (damage > 0f)
                {
                    player.metabolism.radiation_poison.Add(damage);
                    player.metabolism.SendChangesToClient();
                }
            }
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
                            return 1.0f;
                        case BuildingGrade.Enum.Wood:
                            return 0.5f;
                        default:
                            return 0.0f;
                    }
                }
            }

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
            if (player.IsBuildingBlocked()) return false;

            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + Vector3.up, Vector3.up, out hit, 10f, LayerMask.GetMask("Construction", "Deployed")))
                return false;

            return true;
        }

        private void ScheduleNextAutoStorm()
        {
            Kill(ref autoTimer);

            float min = config.Storm.MinAutoInterval;
            float max = config.Storm.MaxAutoInterval;
            if (max < min)
            {
                float swap = min;
                min = max;
                max = swap;
            }
            if (min < 1f) min = 1f;
            if (max < min) max = min;

            float delay = UnityEngine.Random.Range(min, max);
            autoTimer = timer.Once(delay, () =>
            {
                autoTimer = null;
                if (phase == StormPhase.Idle)
                    StartStorm();
            });
        }

        private void StopAllTimers()
        {
            Kill(ref weatherTimer);
            Kill(ref damageTimer);
            Kill(ref durationTimer);
            Kill(ref autoTimer);
            rampActive = false;
        }

        private void Kill(ref Timer handle)
        {
            if (handle != null && !handle.Destroyed)
                handle.Destroy();
            handle = null;
        }

        private void Broadcast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Server.Broadcast(message);
        }
        #endregion

        #region Weather
        private void CaptureChannels()
        {
            channels = new List<WeatherChannel>(8);
            AddChannel("rain", config.Storm.PeakRain);
            AddChannel("wind", config.Storm.PeakWind);
            AddChannel("thunder", config.Storm.PeakThunder);
            AddChannel("fog", config.Storm.PeakFog);
            AddChannel("cloud_coverage", config.Storm.PeakCloudCoverage);
            AddChannel("atmosphere_rayleigh", config.Storm.RedSkyRayleigh);
            AddChannel("atmosphere_mie", config.Storm.RedSkyMie);
            AddChannel("atmosphere_brightness", config.Storm.RedSkyBrightness);
            SaveBackup();
        }

        private void AddChannel(string key, float target)
        {
            float original = ReadChannel(key);
            channels.Add(new WeatherChannel
            {
                Key = key,
                Original = original,
                Baseline = VisibleBaseline(key, original),
                Target = target
            });
        }

        private void ApplyBlend()
        {
            if (channels == null) return;

            for (int i = 0; i < channels.Count; i++)
            {
                WeatherChannel channel = channels[i];
                float value = Mathf.Lerp(channel.Baseline, channel.Target, blend);
                SetChannel(channel.Key, value);
            }
        }

        private void RestoreOriginalsImmediate()
        {
            if (channels != null)
            {
                for (int i = 0; i < channels.Count; i++)
                    SetChannel(channels[i].Key, channels[i].Original);
            }

            channels = null;
            ClearBackup();
        }

        private void SaveBackup()
        {
            var backup = new WeatherBackup { Dirty = true, Channels = new List<BackupChannel>() };
            if (channels != null)
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    backup.Channels.Add(new BackupChannel
                    {
                        Key = channels[i].Key,
                        Original = channels[i].Original
                    });
                }
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(BackupFile, backup);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not save weather backup: {ex.Message}");
            }
        }

        private void ClearBackup()
        {
            try
            {
                if (Interface.Oxide.DataFileSystem.ExistsDatafile(BackupFile))
                    Interface.Oxide.DataFileSystem.DeleteDataFile(BackupFile);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not clear weather backup: {ex.Message}");
            }
        }

        private void TryRestoreBackupFromDisk()
        {
            WeatherBackup backup = null;
            try
            {
                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(BackupFile))
                    return;
                backup = Interface.Oxide.DataFileSystem.ReadObject<WeatherBackup>(BackupFile);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read weather backup: {ex.Message}");
                return;
            }

            if (backup == null || !backup.Dirty || backup.Channels == null || backup.Channels.Count == 0)
            {
                ClearBackup();
                return;
            }

            for (int i = 0; i < backup.Channels.Count; i++)
            {
                BackupChannel saved = backup.Channels[i];
                if (saved == null || string.IsNullOrEmpty(saved.Key)) continue;
                SetChannel(saved.Key, saved.Original);
            }

            ClearBackup();
            Puts("IslandStorm restored pre-storm weather after an interrupted storm.");
        }

        private float ReadChannel(string key)
        {
            switch (key)
            {
                case "rain": return ConVar.Weather.rain;
                case "wind": return ConVar.Weather.wind;
                case "thunder": return ConVar.Weather.thunder;
                case "fog": return ConVar.Weather.fog;
                case "cloud_coverage": return ConVar.Weather.cloud_coverage;
                case "atmosphere_rayleigh": return ConVar.Weather.atmosphere_rayleigh;
                case "atmosphere_mie": return ConVar.Weather.atmosphere_mie;
                case "atmosphere_brightness": return ConVar.Weather.atmosphere_brightness;
                default: return 0f;
            }
        }

        private void SetChannel(string key, float value)
        {
            switch (key)
            {
                case "rain": ConVar.Weather.rain = value; break;
                case "wind": ConVar.Weather.wind = value; break;
                case "thunder": ConVar.Weather.thunder = value; break;
                case "fog": ConVar.Weather.fog = value; break;
                case "cloud_coverage": ConVar.Weather.cloud_coverage = value; break;
                case "atmosphere_rayleigh": ConVar.Weather.atmosphere_rayleigh = value; break;
                case "atmosphere_mie": ConVar.Weather.atmosphere_mie = value; break;
                case "atmosphere_brightness": ConVar.Weather.atmosphere_brightness = value; break;
            }
        }

        private float VisibleBaseline(string key, float convar)
        {
            if (convar >= 0f)
                return convar;

            try
            {
                Vector3 origin = Vector3.zero;
                switch (key)
                {
                    case "rain": return SanitizeSample(Climate.GetRain(origin));
                    case "wind": return SanitizeSample(Climate.GetWind(origin));
                    case "thunder": return SanitizeSample(Climate.GetThunder(origin));
                    case "fog": return SanitizeSample(Climate.GetFog(origin));
                    case "cloud_coverage": return SanitizeSample(Climate.GetClouds(origin));
                    case "atmosphere_rayleigh": return SampleAtmosphere(0, 1f);
                    case "atmosphere_mie": return SampleAtmosphere(1, 1f);
                    case "atmosphere_brightness": return SampleAtmosphere(2, 1f);
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not sample {key}: {ex.Message}");
            }

            return 0f;
        }

        private float SampleAtmosphere(int part, float fallback)
        {
            Climate climate = Climate.Instance;
            if (climate == null || climate.WeatherState == null || climate.WeatherState.Atmosphere == null)
                return fallback;

            TOD_AtmosphereParameters atmosphere = climate.WeatherState.Atmosphere;
            float value = part == 0 ? atmosphere.RayleighMultiplier
                : part == 1 ? atmosphere.MieMultiplier
                : atmosphere.Brightness;
            return SanitizeSample(value);
        }

        private static float SanitizeSample(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return 0f;
            return value;
        }
        #endregion
    }
}
