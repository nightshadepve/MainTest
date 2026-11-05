using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeAnnouncer", "Nightshade", "1.3.1")]
    [Description("Server announcement helper with consistent Nightshade styling, API support, supply signal announcements, and Deep Sea portal alerts.")]
    public class NightshadeAnnouncer : RustPlugin
    {
        private const string PermAdmin = "nightshadeannouncer.admin";

        // ── Supply signal tracking ──────────────────────────────────────────
        private readonly HashSet<ulong> _announcedSignals = new HashSet<ulong>();
        private readonly HashSet<ulong> _excludedSupplySignalSkins = new HashSet<ulong>
        {
            // Heli Signals
            2920175997, 2920176079, 2920176050, 2920176024, 3099117081, 3099117372,
            // Multi Heli
            3083234542, 3083234833, 3083234755, 3083234647, 3099124338, 3099124426,
            // Heli Wave
            3099117224, 3104667036, 3104666951,
            // Bradley Drops
            2905355269, 2905355312, 2905355296, 2911864795, 3361673979, 3361674045,
            // Bradley Wave
            3502926194, 3502926112
        };

        // ── Deep Sea state ──────────────────────────────────────────────────
        private const string DeepSeaPortalPrefab  = "assets/bundled/prefabs/deepsea/deepsea_portal.prefab";
        private const string DeepSeaManagerPrefab = "assets/bundled/prefabs/system/deep_sea_manager.prefab";

        private Vector3?   _lastPortalPos;
        private int        _timeToWipe          = -1;
        private int        _timeToNextOpening   = -1;
        private Component  _deepSeaManagerComp;
        private PropertyInfo _piTimeToWipe;
        private PropertyInfo _piTimeToNextOpening;
        private FieldInfo    _fiSyncTimeToWipe;
        private FieldInfo    _fiSyncTimeToNextOpening;
        private DateTime   _lastDsAlertUtc      = DateTime.MinValue;
        private readonly Dictionary<ulong, double> _dsCooldownUntil   = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, double> _bradCooldownUntil = new Dictionary<ulong, double>();

        private ConfigData _config;

        // ───────────────────────────────────────────────────────────────────
        // Config
        // ───────────────────────────────────────────────────────────────────

        private class DeepSeaConfig
        {
            public bool   Enabled                    = true;
            public string MessageTemplate            = "A portal tears open at sea… Deep Sea has spawned ({direction}).";
            public bool   AbbreviateDirection        = false;
            public float  AlertCooldownSeconds       = 600f;
            public bool   ScanOnServerInitialized    = true;
            public float  ScanDelaySeconds           = 8f;
            public float  ManagerPollIntervalSeconds = 1f;
            public float  DsCommandCooldownSeconds   = 2f;
            public bool   LogToConsoleWhenTriggered  = true;
        }

        private class ConfigData
        {
            public bool   SendToChat          = true;
            public bool   SendToConsoleSay    = false;

            public string PrefixPlain         = "[Nightshade]";
            public string PrefixRich          = "<color=#b56bff>[Nightshade]</color>";
            public bool   UseRichText         = true;
            public bool   SanitizeRichText    = true;

            public List<string> AllowedTags   = new List<string>
            {
                "color", "/color",
                "size",  "/size",
                "b",     "/b",
                "i",     "/i"
            };

            public string DefaultMessageColor = "#ffd479";
            public int    MaxMessageLength    = 180;
            public bool   AddEllipsisOnTrim   = true;

            public string Command             = "announce";
            public bool   RequirePermission   = true;
            public bool   LogToConsole        = true;

            public bool   AnnounceSupplySignals          = true;
            public bool   IncludeSupplySignalPlayerName  = true;
            public string SupplySignalMessageColor       = "#ffd479";
            public string SupplySignalMessage            = "Supply signal thrown by <color=#ffff00>{player}</color>.";

            public bool   EnableBradCommand          = true;
            public float  BradCommandCooldownSeconds = 2f;
            public string BradCommandOnlineMessage   = "Bradley is currently active. Proceed with poor decisions.";
            public string BradCommandOfflineMessage  = "Bradley is not active right now.";

            public DeepSeaConfig DeepSea = new DeepSeaConfig();
        }

        // ───────────────────────────────────────────────────────────────────
        // Oxide lifecycle
        // ───────────────────────────────────────────────────────────────────

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<ConfigData>() ?? new ConfigData();
            if (_config.DeepSea == null) _config.DeepSea = new DeepSeaConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void OnServerInitialized()
        {
            if (!string.Equals(_config.Command, "announce", StringComparison.OrdinalIgnoreCase))
                AddCovalenceCommand(_config.Command, nameof(CmdAnnounceCovalence));

            if (_config.DeepSea.Enabled)
            {
                timer.Once(_config.DeepSea.ScanDelaySeconds, () =>
                {
                    CacheDeepSeaManager();
                    StartManagerPolling();

                    if (_config.DeepSea.ScanOnServerInitialized)
                    {
                        var portal = FindPortalEntityOnce();
                        if (portal != null)
                        {
                            _lastPortalPos = portal.transform.position;
                            if (_config.DeepSea.LogToConsoleWhenTriggered)
                                Puts("NightshadeAnnouncer: Deep Sea portal found on init scan (cached position).");
                        }
                    }
                });
            }
        }

        private void Unload()
        {
            _dsCooldownUntil.Clear();
            _bradCooldownUntil.Clear();
        }

        // ───────────────────────────────────────────────────────────────────
        // Hooks
        // ───────────────────────────────────────────────────────────────────

        private void OnEntitySpawned(BaseNetworkable net)
        {
            if (!_config.DeepSea.Enabled) return;

            var ent = net as BaseEntity;
            if (ent == null) return;

            if (IsDeepSeaPortal(ent))
            {
                _lastPortalPos = ent.transform.position;
                TriggerDeepSeaAnnouncement(ent);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity?.net == null) return;

            // Supply signal cleanup
            ulong netId = entity.net.ID.Value;
            _announcedSignals.Remove(netId);

            // Deep Sea portal cleanup
            if (_config.DeepSea.Enabled)
            {
                var ent = entity as BaseEntity;
                if (ent?.PrefabName != null &&
                    ent.PrefabName.Equals(DeepSeaPortalPrefab, StringComparison.OrdinalIgnoreCase))
                {
                    _lastPortalPos = null;
                    if (_config.DeepSea.LogToConsoleWhenTriggered)
                        Puts("NightshadeAnnouncer: Deep Sea portal despawned (cleared cached position).");
                }
            }
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            TryAnnounceSupplySignal(player, entity);
        }

        private void OnExplosiveDropped(BasePlayer player, BaseEntity entity, ThrownWeapon thrown)
        {
            TryAnnounceSupplySignal(player, entity);
        }

        // ───────────────────────────────────────────────────────────────────
        // Supply signal logic
        // ───────────────────────────────────────────────────────────────────

        private void TryAnnounceSupplySignal(BasePlayer player, BaseEntity entity)
        {
            if (!_config.AnnounceSupplySignals || player == null || entity == null || !(entity is SupplySignal))
                return;

            if (IsExcludedSupplySignal(player, entity))
                return;

            NextTick(() =>
            {
                if (player == null || !player.IsConnected || entity == null || entity.net == null || !(entity is SupplySignal))
                    return;

                if (IsExcludedSupplySignal(player, entity))
                    return;

                ulong netId = entity.net.ID.Value;
                if (!_announcedSignals.Add(netId))
                    return;

                string message = BuildSupplySignalMessage(player);
                SendAnnouncement(message, _config.UseRichText, _config.SupplySignalMessageColor);
            });
        }

        private bool IsExcludedSupplySignal(BasePlayer player, BaseEntity entity)
        {
            if (entity == null) return false;

            if (_excludedSupplySignalSkins.Contains(entity.skinID))
                return true;

            Item activeItem = player?.GetActiveItem();
            if (activeItem != null && _excludedSupplySignalSkins.Contains(activeItem.skin))
                return true;

            return false;
        }

        private string BuildSupplySignalMessage(BasePlayer player)
        {
            string message = _config.SupplySignalMessage ?? string.Empty;
            string playerName = _config.IncludeSupplySignalPlayerName ? player.displayName : "someone";
            return message.Replace("{player}", playerName);
        }

        // ───────────────────────────────────────────────────────────────────
        // Deep Sea logic
        // ───────────────────────────────────────────────────────────────────

        private bool IsDeepSeaPortal(BaseEntity ent) =>
            string.Equals(ent?.PrefabName, DeepSeaPortalPrefab, StringComparison.OrdinalIgnoreCase);

        private void TriggerDeepSeaAnnouncement(BaseEntity portalEntity)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDsAlertUtc).TotalSeconds < _config.DeepSea.AlertCooldownSeconds)
                return;

            _lastDsAlertUtc = now;

            string direction = GetNearestMapEdgeDirection(portalEntity.transform.position);
            if (_config.DeepSea.AbbreviateDirection) direction = AbbrevDirection(direction);

            string msg = _config.DeepSea.MessageTemplate.Replace("{direction}", direction);
            SendAnnouncement(msg, _config.UseRichText, _config.DefaultMessageColor);

            if (_config.DeepSea.LogToConsoleWhenTriggered)
                Puts($"NightshadeAnnouncer: Deep Sea portal fired at {portalEntity.transform.position} ({direction})");
        }

        // ── DeepSeaManager caching + polling ───────────────────────────────

        private void CacheDeepSeaManager()
        {
            var mgrEnt = FindDeepSeaManagerEntityOnce();
            if (mgrEnt == null)
            {
                if (_config.DeepSea.LogToConsoleWhenTriggered)
                    Puts("NightshadeAnnouncer: DeepSeaManager entity not found (yet). Will keep polling.");
                return;
            }

            var comps = mgrEnt.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;

                if (string.Equals(c.GetType().Name, "DeepSeaManager", StringComparison.OrdinalIgnoreCase))
                {
                    _deepSeaManagerComp = c;

                    var t = c.GetType();
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    _piTimeToWipe          = t.GetProperty("TimeToWipe",          flags);
                    _piTimeToNextOpening   = t.GetProperty("TimeToNextOpening",   flags);
                    _fiSyncTimeToWipe      = t.GetField("__sync_TimeToWipe",      flags);
                    _fiSyncTimeToNextOpening = t.GetField("__sync_TimeToNextOpening", flags);

                    if (_config.DeepSea.LogToConsoleWhenTriggered)
                        Puts("NightshadeAnnouncer: DeepSeaManager component cached successfully.");
                    return;
                }
            }

            if (_config.DeepSea.LogToConsoleWhenTriggered)
                Puts("NightshadeAnnouncer: DeepSeaManager entity found but component was not located.");
        }

        private void StartManagerPolling()
        {
            timer.Every(Mathf.Max(0.25f, _config.DeepSea.ManagerPollIntervalSeconds), () =>
            {
                if (_deepSeaManagerComp == null)
                {
                    CacheDeepSeaManager();
                    return;
                }
                ReadManagerTimesCached();
            });
        }

        private void ReadManagerTimesCached()
        {
            int wipe = -1;
            int next = -1;

            try
            {
                if (_piTimeToWipe != null && _piTimeToWipe.GetIndexParameters().Length == 0)
                {
                    object v = _piTimeToWipe.GetValue(_deepSeaManagerComp, null);
                    if (v is int)   wipe = (int)v;
                    else if (v is float) wipe = Mathf.RoundToInt((float)v);
                }

                if (_piTimeToNextOpening != null && _piTimeToNextOpening.GetIndexParameters().Length == 0)
                {
                    object v = _piTimeToNextOpening.GetValue(_deepSeaManagerComp, null);
                    if (v is int)   next = (int)v;
                    else if (v is float) next = Mathf.RoundToInt((float)v);
                }

                if (wipe < 0 && _fiSyncTimeToWipe != null)
                {
                    object v = _fiSyncTimeToWipe.GetValue(_deepSeaManagerComp);
                    if (v is int) wipe = (int)v;
                }

                if (next < 0 && _fiSyncTimeToNextOpening != null)
                {
                    object v = _fiSyncTimeToNextOpening.GetValue(_deepSeaManagerComp);
                    if (v is int) next = (int)v;
                }
            }
            catch { /* keep unknowns */ }

            _timeToWipe        = wipe;
            _timeToNextOpening = next;
        }

        private BaseEntity FindDeepSeaManagerEntityOnce()
        {
            foreach (var net in BaseNetworkable.serverEntities)
            {
                var ent = net as BaseEntity;
                if (ent == null) continue;
                if (string.Equals(ent.PrefabName, DeepSeaManagerPrefab, StringComparison.OrdinalIgnoreCase))
                    return ent;
            }
            return null;
        }

        private BaseEntity FindPortalEntityOnce()
        {
            foreach (var net in BaseNetworkable.serverEntities)
            {
                var ent = net as BaseEntity;
                if (ent == null) continue;
                if (string.Equals(ent.PrefabName, DeepSeaPortalPrefab, StringComparison.OrdinalIgnoreCase))
                    return ent;
            }
            return null;
        }

        // ───────────────────────────────────────────────────────────────────
        // Public API (called by other plugins via plugin.Call("Announce", ...))
        // ───────────────────────────────────────────────────────────────────

        private bool Announce(string message, object overrideUseRichText = null, object overrideColor = null)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            bool useRich = _config.UseRichText;
            if (overrideUseRichText is bool b) useRich = b;

            string color = _config.DefaultMessageColor;
            if (overrideColor is string s && !string.IsNullOrWhiteSpace(s)) color = s.Trim();

            SendAnnouncement(message, useRich, color);
            return true;
        }

        // ───────────────────────────────────────────────────────────────────
        // Chat commands
        // ───────────────────────────────────────────────────────────────────

        [ChatCommand("announce")]
        private void CmdAnnounce(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (_config.RequirePermission && !permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                player.ChatMessage("Nope. (Missing permission: nightshadeannouncer.admin)");
                return;
            }

            if (args == null || args.Length == 0)
            {
                player.ChatMessage("Usage: /announce <message>");
                return;
            }

            SendAnnouncement(string.Join(" ", args), _config.UseRichText, _config.DefaultMessageColor);
        }

        [ChatCommand("brad")]
        private void CmdBrad(BasePlayer player, string command, string[] args)
        {
            if (player == null || _config == null || !_config.EnableBradCommand) return;

            double now = Time.realtimeSinceStartup;
            double until;
            if (_bradCooldownUntil.TryGetValue(player.userID, out until) && now < until) return;
            _bradCooldownUntil[player.userID] = now + Math.Max(0.5f, _config.BradCommandCooldownSeconds);

            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                var bradley = entity as BradleyAPC;
                if (bradley != null && !bradley.IsDestroyed)
                {
                    player.ChatMessage(_config.BradCommandOnlineMessage ?? "Bradley is currently active.");
                    return;
                }
            }

            player.ChatMessage(_config.BradCommandOfflineMessage ?? "Bradley is not active right now.");
        }

        [ChatCommand("ds")]
        private void CmdDs(BasePlayer player, string command, string[] args)
        {
            if (player == null || !_config.DeepSea.Enabled) return;

            double now = Time.realtimeSinceStartup;
            double until;
            if (_dsCooldownUntil.TryGetValue(player.userID, out until) && now < until) return;
            _dsCooldownUntil[player.userID] = now + Math.Max(0.5f, _config.DeepSea.DsCommandCooldownSeconds);

            if (_timeToWipe > 0)
            {
                string dir = "unknown";
                if (_lastPortalPos.HasValue)
                {
                    dir = GetNearestMapEdgeDirection(_lastPortalPos.Value);
                    if (_config.DeepSea.AbbreviateDirection) dir = AbbrevDirection(dir);
                }

                player.ChatMessage(BuildDsPrefix(
                    $"A portal churns at sea… Deep Sea is <color=#66ff66>ACTIVE</color> (<color=#ffffff>{dir}</color>). Time left: <color=#ffffff>{FormatSeconds(_timeToWipe)}</color>."
                ));
                return;
            }

            if (_timeToNextOpening > 0)
            {
                player.ChatMessage(BuildDsPrefix(
                    $"Deep Sea is <color=#ff6666>NOT ACTIVE</color> — next opening in <color=#ffffff>{FormatSeconds(_timeToNextOpening)}</color>."
                ));
                return;
            }

            player.ChatMessage(BuildDsPrefix(
                "Deep Sea is <color=#ff6666>NOT ACTIVE</color> — no opening timer currently available."
            ));
        }

        [ChatCommand("ds.timer")]
        private void CmdDsTimer(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                player.ChatMessage("No permission.");
                return;
            }

            player.ChatMessage($"DeepSeaManager (cached): TimeToWipe={_timeToWipe}s, TimeToNextOpening={_timeToNextOpening}s");
        }

        [ChatCommand("ds.recache")]
        private void CmdDsRecache(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                player.ChatMessage("No permission.");
                return;
            }

            _deepSeaManagerComp     = null;
            _piTimeToWipe           = null;
            _piTimeToNextOpening    = null;
            _fiSyncTimeToWipe       = null;
            _fiSyncTimeToNextOpening = null;

            CacheDeepSeaManager();
            player.ChatMessage("DeepSeaManager recache attempted. Check /ds.timer.");
        }

        private void CmdAnnounceCovalence(Oxide.Core.Libraries.Covalence.IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;

            if (_config.RequirePermission && !permission.UserHasPermission(iplayer.Id, PermAdmin))
            {
                iplayer.Reply("Nope. (Missing permission: nightshadeannouncer.admin)");
                return;
            }

            if (args == null || args.Length == 0)
            {
                iplayer.Reply($"Usage: /{_config.Command} <message>");
                return;
            }

            SendAnnouncement(string.Join(" ", args), _config.UseRichText, _config.DefaultMessageColor);
        }

        // ───────────────────────────────────────────────────────────────────
        // Core send
        // ───────────────────────────────────────────────────────────────────

        private void SendAnnouncement(string rawMessage, bool useRichText, string wrapColor)
        {
            string msg = rawMessage ?? string.Empty;

            if (!useRichText)
                msg = StripAllTags(msg);
            else if (_config.SanitizeRichText)
                msg = SanitizeToAllowlist(msg, _config.AllowedTags);

            msg = TrimToMax(msg, _config.MaxMessageLength, _config.AddEllipsisOnTrim);

            string prefix = useRichText ? _config.PrefixRich : _config.PrefixPlain;
            string body   = msg;
            if (useRichText && !string.IsNullOrWhiteSpace(wrapColor))
                body = $"<color={wrapColor}>{body}</color>";

            string final = string.IsNullOrWhiteSpace(prefix) ? body : $"{prefix} {body}";

            if (_config.SendToChat)
                PrintToChat(final);

            if (_config.SendToConsoleSay)
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "say", final);

            if (_config.LogToConsole)
                Puts($"Announcement sent: {StripAllTags(final)}");
        }

        // Builds the styled prefix inline for player-only messages (e.g. /ds replies)
        private string BuildDsPrefix(string body) =>
            $"<color=#b56bff>[Nightshade]</color> <color=#ffd479>{body}</color>";

        // ───────────────────────────────────────────────────────────────────
        // Helpers
        // ───────────────────────────────────────────────────────────────────

        private string GetNearestMapEdgeDirection(Vector3 pos)
        {
            float half = TerrainMeta.Size.x / 2f;

            float distWest  = pos.x + half;
            float distEast  = half - pos.x;
            float distSouth = pos.z + half;
            float distNorth = half - pos.z;

            float min  = distWest;
            string dir = "West";

            if (distEast  < min) { min = distEast;  dir = "East";  }
            if (distSouth < min) { min = distSouth;  dir = "South"; }
            if (distNorth < min) { dir = "North"; }

            return dir;
        }

        private string AbbrevDirection(string dir)
        {
            switch (dir)
            {
                case "North": return "N";
                case "South": return "S";
                case "East":  return "E";
                case "West":  return "W";
                default:      return dir;
            }
        }

        private string FormatSeconds(int seconds)
        {
            if (seconds < 0) return "unknown";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private string GetGrid(Vector3 position)
        {
            float size     = TerrainMeta.Size.x;
            float cellSize = 146.3f;
            float half     = size / 2f;

            int x = Mathf.FloorToInt((position.x + half) / cellSize);
            int z = Mathf.FloorToInt((half - position.z) / cellSize);

            x = Mathf.Max(0, x);
            z = Mathf.Max(0, z);

            string letters = string.Empty;
            int index = x;
            do
            {
                letters = (char)('A' + (index % 26)) + letters;
                index = (index / 26) - 1;
            }
            while (index >= 0);

            return $"{letters}{z}";
        }

        private string TrimToMax(string s, int maxLen, bool ellipsis)
        {
            if (maxLen <= 0 || string.IsNullOrEmpty(s)) return s;
            if (s.Length <= maxLen) return s;
            if (!ellipsis) return s.Substring(0, maxLen);

            const string ell = "…";
            if (maxLen <= ell.Length) return s.Substring(0, maxLen);
            return s.Substring(0, maxLen - ell.Length) + ell;
        }

        private string StripAllTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new System.Text.StringBuilder(input.Length);
            bool inTag = false;

            foreach (char c in input)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) result.Append(c);
            }
            return result.ToString();
        }

        private string SanitizeToAllowlist(string input, List<string> allowedTags)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (allowedTags == null || allowedTags.Count == 0) return StripAllTags(input);

            var allowed = new HashSet<string>(allowedTags.Select(t => t.Trim().ToLowerInvariant()));
            var sb = new System.Text.StringBuilder(input.Length);

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c != '<') { sb.Append(c); continue; }

                int end = input.IndexOf('>', i + 1);
                if (end == -1) { sb.Append(c); continue; }

                string tagContent = input.Substring(i + 1, end - i - 1).Trim();
                string tagName    = tagContent;

                int spaceIdx = tagName.IndexOf(' ');
                if (spaceIdx >= 0) tagName = tagName.Substring(0, spaceIdx);

                int eqIdx = tagName.IndexOf('=');
                if (eqIdx >= 0) tagName = tagName.Substring(0, eqIdx);

                tagName = tagName.Trim().ToLowerInvariant();

                if (allowed.Contains(tagName))
                    sb.Append('<').Append(input, i + 1, end - i - 1).Append('>');

                i = end;
            }

            return sb.ToString();
        }
    }
}