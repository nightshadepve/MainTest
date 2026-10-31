using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HideAndSeek", "Nightshade", "1.1.0")]
    [Description("Randomly selects a Prey who must hide while Seekers hunt them for rewards.")]
    public class HideAndSeek : RustPlugin
    {
        #region References
        [PluginReference] Plugin ServerRewards;
        [PluginReference] Plugin TruePVE;
        [PluginReference] Plugin NextGenPVE;
        #endregion

        #region Fields
        private ulong _preyId = 0;
        private Timer _gameTimer;
        private Timer _gracePeriodTimer;
        private int   _timeRemaining;
        private int   _timeSurvived;
        private bool  _gameActive   = false;
        private bool  _graceActive  = false;
        private float _cooldownUntil = 0f;

        private const string PermAdmin    = "hideandseek.admin";
        private const string PrefabMarker = "assets/prefabs/tools/map/genericradiusmarker.prefab";

        private readonly Dictionary<ulong, MapMarkerGenericRadius> _markers
            = new Dictionary<ulong, MapMarkerGenericRadius>();
        #endregion

        #region Config
        private Configuration _config;
        private const int ConfigVersion = 2;

        private class Configuration
        {
            [JsonProperty("Config Version")]
            public int Version = ConfigVersion;

            [JsonProperty("Survival Time (seconds)")]
            public int SurvivalTime = 1800;

            [JsonProperty("Grace Period (seconds, prey is invulnerable)")]
            public int GracePeriod = 10;

            [JsonProperty("Auto Start Interval (seconds, 0 = disabled)")]
            public int AutoStartInterval = 0;

            [JsonProperty("Post-Game Cooldown (seconds, applies to auto-start)")]
            public int PostGameCooldown = 300;

            [JsonProperty("Minimum Players to Start")]
            public int MinPlayers = 2;

            [JsonProperty("Seeker Reward - ServerRewards Points")]
            public int SeekerRewardPoints = 500;

            [JsonProperty("Seeker Reward - Scrap")]
            public int SeekerRewardScrap = 200;

            [JsonProperty("Prey Survival Reward - ServerRewards Points")]
            public int PreySurvivalPoints = 800;

            [JsonProperty("Prey Survival Reward - Scrap")]
            public int PreySurvivalScrap = 300;

            [JsonProperty("Prey Time Bonus - RP per 30 seconds survived (win or loss)")]
            public float PreyTimeBonusRpPer30s = 0.5f;

            [JsonProperty("Show Map Markers (all players get a dot)")]
            public bool ShowMapMarker = true;

            [JsonProperty("Map Marker Radius (0.01 - 1.0)")]
            public float MarkerRadius = 0.05f;

            [JsonProperty("Map Marker Alpha (0.0 - 1.0)")]
            public float MarkerAlpha = 1.0f;

            [JsonProperty("Map Marker Color (R G B, values 0.0 - 1.0)")]
            public string MarkerColor = "1.0 0.0 0.0";

            [JsonProperty("Announce Game Start in Chat")]
            public bool AnnounceStart = true;

            [JsonProperty("Prey is immune to fall damage")]
            public bool FallDamageImmunity = false;

            [JsonProperty("Chat Icon (Steam64 ID, 0 = default)")]
            public ulong ChatIcon = 0;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) _config = new Configuration();
                MigrateConfig();
            }
            catch
            {
                PrintWarning("Config failed to load, generating defaults.");
                _config = new Configuration();
            }
            SaveConfig();
        }

        private void MigrateConfig()
        {
            if (_config.Version >= ConfigVersion) return;

            PrintWarning($"Migrating config from version {_config.Version} to {ConfigVersion}.");

            // v1 -> v2: added grace period, cooldown, prey rewards, time bonus
            if (_config.Version < 2)
            {
                if (_config.GracePeriod == 0)       _config.GracePeriod = 10;
                if (_config.PostGameCooldown == 0)   _config.PostGameCooldown = 300;
                if (_config.PreySurvivalPoints == 0) _config.PreySurvivalPoints = 800;
                if (_config.PreySurvivalScrap == 0)  _config.PreySurvivalScrap = 300;
                if (_config.PreyTimeBonusRpPer30s == 0f) _config.PreyTimeBonusRpPer30s = 0.5f;
            }

            _config.Version = ConfigVersion;
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["GameStart"]          = "<color=#ff4444>HIDE AND SEEK</color>\n<color=#ffcc00>{0}</color> is the Prey! Seekers, hunt them down for <color=#00ff88>{1} RP</color> + <color=#ccaa00>{2} Scrap</color>!\nThey must survive for <color=#ff8800>{3}</color>!",
                ["GracePeriod"]        = "<color=#ff4444>[Hide and Seek]</color> Grace period active — Prey is safe for <color=#ff8800>{0}s</color>. Get ready, Seekers!",
                ["GraceOver"]          = "<color=#ff4444>[Hide and Seek]</color> Grace period over! The hunt is on!",
                ["PreyNotify"]         = "<color=#ff4444>YOU ARE THE PREY!</color>\nSurvive for <color=#ff8800>{0}</color>!\nYou have a <color=#00ff88>{1}s grace period</color> — use it to hide!\nYou CANNOT hide in locked buildings. Seekers are coming!\nGood luck...",
                ["TimeRemaining"]      = "<color=#ff4444>[Hide and Seek]</color> <color=#ffcc00>{0}</color> (Prey) has <color=#ff8800>{1}</color> remaining! Seekers, close in!",
                ["PreyWin"]            = "<color=#00ff88>PREY ESCAPED!</color>\n<color=#ffcc00>{0}</color> survived the hunt!\nReward: <color=#00ff88>{1} RP</color> + <color=#ccaa00>{2} Scrap</color> + <color=#00ff88>{3} RP time bonus</color>!",
                ["SeekerWin"]          = "<color=#ff4444>PREY CAUGHT!</color>\n<color=#ffcc00>{0}</color> caught the Prey!\nReward: <color=#00ff88>{1} RP</color> + <color=#ccaa00>{2} Scrap</color>!\n<color=#ffcc00>{3}</color> (Prey) earns <color=#00ff88>{4} RP time bonus</color> for surviving <color=#ff8800>{5}</color>.",
                ["GameAlreadyActive"]  = "A Hide and Seek game is already active!",
                ["NotEnoughPlayers"]   = "Not enough players online. Minimum: {0}",
                ["NoGameActive"]       = "No Hide and Seek game is currently active.",
                ["GameStopped"]        = "The Hide and Seek game has been stopped by an admin.",
                ["PreyDisconnected"]   = "The Prey disconnected. Game over!",
                ["PreyFell"]           = "<color=#ff4444>[Hide and Seek]</color> The Prey has fallen! No reward this round.",
                ["NoPlugin"]           = "ServerRewards plugin not found. Points reward disabled.",
                ["HudText"]            = "HIDE AND SEEK — Prey: <color=#ffcc00>{0}</color> | <color=#ff8800>{1}</color>",
                ["HudGrace"]           = "HIDE AND SEEK — Grace period: <color=#00ff88>{0}s</color> remaining",
                ["YouArePrey"]         = "YOU ARE THE PREY | <color=#ff8800>{0}</color>",
                ["YouArePreyGrace"]    = "YOU ARE THE PREY | Grace: <color=#00ff88>{0}s</color> — hide!",
                ["PlayerNotFound"]     = "Could not find a player matching '{0}'. Make sure they are online.",
                ["PreySet"]            = "Prey has been manually set to <color=#ffcc00>{0}</color>.",
                ["OnCooldown"]         = "Hide and Seek is on cooldown. Next game available in <color=#ff8800>{0}s</color>.",
            }, this);
        }

        private string Lang(string key, string userId = null, params object[] args)
            => string.Format(lang.GetMessage(key, this, userId), args);
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);

            if (_config.AutoStartInterval > 0)
            {
                timer.Every(_config.AutoStartInterval, () =>
                {
                    if (!_gameActive) TryAutoStart();
                });
            }
        }

        private void Unload() => EndGame(false);

        private void OnPlayerInit(BasePlayer player)
        {
            if (!_gameActive || !_config.ShowMapMarker) return;

            // Only spawn fresh markers for the connecting client — don't touch existing players
            foreach (var kvp in _markers)
            {
                var tracked = BasePlayer.FindByID(kvp.Key);
                if (tracked == null) continue;
                var marker = GameManager.server.CreateEntity(PrefabMarker, tracked.transform.position)
                             as MapMarkerGenericRadius;
                if (marker == null) continue;
                marker.alpha  = _config.MarkerAlpha;
                var col = ParseMarkerColor();
                marker.color1 = col;
                marker.color2 = col;
                marker.radius = _config.MarkerRadius;
                marker.Spawn();
                marker.SendUpdate();
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (!_gameActive) return;
            KillMarker(player.userID);

            if (player.userID == _preyId)
            {
                BroadcastAll(Lang("PreyDisconnected"));
                EndGame(false);
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (!_gameActive) return;
            if (!(entity is BasePlayer victim)) return;
            if (victim.userID != _preyId) return;

            var killer = info?.Initiator as BasePlayer;
            if (killer != null && killer.userID != _preyId)
                HandlePreyCaught(killer);
            else
            {
                // Died to environment, NPC, trap, fall, etc. — no reward
                BroadcastAll(Lang("PreyFell"));
                EndGame(false);
            }
        }

        // TruePVE / NextGenPVE — allow damage to the prey from seekers
        private object CanEntityTakeDamage(BasePlayer entity, HitInfo info)
        {
            if (!_gameActive || info == null) return null;
            if (_graceActive) return null; // prey is invulnerable during grace period

            // Allow damage when prey is the victim
            if (entity.userID == _preyId)
            {
                if (_config.FallDamageImmunity && info.damageTypes.Has(Rust.DamageType.Fall)) return null;
                return true;
            }

            // Also allow damage when the attacker is targeting the prey
            var attacker = info.Initiator as BasePlayer;
            if (attacker != null && entity.userID != _preyId && attacker.userID != _preyId) return null;
            if (attacker != null && entity.userID == _preyId) return true;

            return null;
        }

        private object CanTeleport(BasePlayer player)
        {
            if (!_gameActive) return null;
            if (player.userID != _preyId) return null;
            return "You cannot teleport while you are the Prey!";
        }

        private object OnPlayerTeleport(BasePlayer player, Vector3 oldPos, Vector3 newPos)
        {
            if (!_gameActive) return null;
            if (player.userID != _preyId) return null;
            SendReply(player, "<color=#ff4444>You cannot teleport while you are the Prey!</color>");
            return true;
        }

        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (!_gameActive) return null;
            if (player.userID != _preyId) return null;

            if (baseLock is CodeLock codeLock)
            {
                if (!codeLock.whitelistPlayers.Contains(player.userID))
                {
                    SendReply(player, "<color=#ff4444>You cannot hide in locked buildings as the Prey!</color>");
                    return "You cannot hide in locked buildings as the Prey!";
                }
            }
            else if (baseLock is KeyLock keyLock)
            {
                if (keyLock.OwnerID != player.userID)
                {
                    SendReply(player, "<color=#ff4444>You cannot hide in locked buildings as the Prey!</color>");
                    return "You cannot hide in locked buildings as the Prey!";
                }
            }

            return null;
        }
        #endregion

        #region Game Logic
        private void TryAutoStart()
        {
            if (_config.PostGameCooldown > 0 && Time.realtimeSinceStartup < _cooldownUntil) return;

            var players = BasePlayer.activePlayerList
                .Where(p => !p.IsNpc && p.IsConnected).ToList();
            if (players.Count >= _config.MinPlayers) StartGame();
        }

        private void StartGame(BasePlayer forcedPrey = null)
        {
            if (_gameActive) return;

            var eligible = BasePlayer.activePlayerList
                .Where(p => !p.IsNpc && p.IsConnected).ToList();

            if (eligible.Count < _config.MinPlayers) return;

            var prey = forcedPrey ?? eligible[UnityEngine.Random.Range(0, eligible.Count)];

            _preyId        = prey.userID;
            _gameActive    = true;
            _graceActive   = _config.GracePeriod > 0;
            _timeRemaining = _config.SurvivalTime;
            _timeSurvived  = 0;

            string timeStr = FormatTime(_config.SurvivalTime);

            if (_config.AnnounceStart)
                BroadcastAll(Lang("GameStart", null, prey.displayName, _config.SeekerRewardPoints, _config.SeekerRewardScrap, timeStr));

            SendReply(prey, Lang("PreyNotify", prey.UserIDString, timeStr, _config.GracePeriod));

            if (_config.ShowMapMarker)
            {
                foreach (var p in eligible)
                    SpawnMarker(p);
            }

            // Grace period
            if (_graceActive)
            {
                BroadcastAll(Lang("GracePeriod", null, _config.GracePeriod));
                int graceRemaining = _config.GracePeriod;

                _gracePeriodTimer = timer.Every(1f, () =>
                {
                    graceRemaining--;
                    if (graceRemaining <= 0)
                    {
                        _graceActive = false;
                        _gracePeriodTimer?.Destroy();
                        _gracePeriodTimer = null;
                        BroadcastAll(Lang("GraceOver"));
                    }
                });
            }

            // Main game tick — countdown, marker refresh, HUD all in one timer
            _gameTimer = timer.Every(1f, () =>
            {
                _timeRemaining--;
                _timeSurvived++;

                if (_config.ShowMapMarker)
                {
                    foreach (var p in BasePlayer.activePlayerList.Where(x => !x.IsNpc && x.IsConnected))
                    {
                        KillMarker(p.userID);
                        SpawnMarker(p);
                    }
                }

                // HUD update
                var preyPlayer = BasePlayer.FindByID(_preyId);
                if (preyPlayer != null)
                {
                    string tLeft = FormatTime(_timeRemaining);
                    int graceLeft = _graceActive
                        ? (_config.GracePeriod - (_config.SurvivalTime - _timeRemaining))
                        : 0;

                    foreach (var p in BasePlayer.activePlayerList)
                    {
                        if (!p.IsConnected) continue;
                        string msg;
                        if (p.userID == _preyId)
                            msg = _graceActive
                                ? Lang("YouArePreyGrace", p.UserIDString, graceLeft)
                                : Lang("YouArePrey", p.UserIDString, tLeft);
                        else
                            msg = _graceActive
                                ? Lang("HudGrace", p.UserIDString, graceLeft)
                                : Lang("HudText", p.UserIDString, preyPlayer.displayName, tLeft);
                        ShowHud(p, msg);
                    }
                }

                if (_timeRemaining <= 0) PreySurvived();
            });

        }

        private void PreySurvived()
        {
            if (!_gameActive) return;
            var prey = BasePlayer.FindByID(_preyId);
            int timeBonus = CalculateTimeBonus(_timeSurvived);

            BroadcastAll(Lang("PreyWin", null,
                prey?.displayName ?? "The Prey",
                _config.PreySurvivalPoints,
                _config.PreySurvivalScrap,
                timeBonus));

            if (prey != null)
            {
                int totalRp = _config.PreySurvivalPoints + timeBonus;
                if (totalRp > 0 && ServerRewards != null)
                    ServerRewards.Call("AddPoints", prey.userID, totalRp);

                if (_config.PreySurvivalScrap > 0)
                    GiveScrap(prey, _config.PreySurvivalScrap);
            }

            EndGame(false);
        }

        private void HandlePreyCaught(BasePlayer seeker)
        {
            if (!_gameActive) return;

            var prey = BasePlayer.FindByID(_preyId);
            int timeBonus = CalculateTimeBonus(_timeSurvived);

            BroadcastAll(Lang("SeekerWin", null,
                seeker.displayName,
                _config.SeekerRewardPoints,
                _config.SeekerRewardScrap,
                prey?.displayName ?? "The Prey",
                timeBonus,
                FormatTime(_timeSurvived)));

            // Seeker reward
            if (_config.SeekerRewardPoints > 0)
            {
                if (ServerRewards != null)
                    ServerRewards.Call("AddPoints", seeker.userID, _config.SeekerRewardPoints);
                else
                    PrintWarning(Lang("NoPlugin"));
            }

            if (_config.SeekerRewardScrap > 0)
                GiveScrap(seeker, _config.SeekerRewardScrap);

            // Prey time bonus (always given, even on loss)
            if (timeBonus > 0 && prey != null)
            {
                if (ServerRewards != null)
                    ServerRewards.Call("AddPoints", prey.userID, timeBonus);
            }

            EndGame(false);
        }

        private int CalculateTimeBonus(int secondsSurvived)
        {
            float bonus = _config.PreyTimeBonusRpPer30s * (secondsSurvived / 30f);
            return Mathf.FloorToInt(bonus);
        }

        private void EndGame(bool broadcast)
        {
            _gameActive  = false;
            _graceActive = false;
            _preyId      = 0;
            _timeSurvived = 0;
            _cooldownUntil = Time.realtimeSinceStartup + _config.PostGameCooldown;

            _gameTimer?.Destroy();
            _gracePeriodTimer?.Destroy();
            _gameTimer        = null;
            _gracePeriodTimer = null;

            KillAllMarkers();

            foreach (var p in BasePlayer.activePlayerList)
                ClearHud(p);

            if (broadcast)
                BroadcastAll(Lang("GameStopped"));
        }

        private void GiveScrap(BasePlayer player, int amount)
        {
            var item = ItemManager.CreateByName("scrap", amount);
            if (item == null) return;
            player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
        }
        #endregion

        #region Map Markers
        private void SpawnMarker(BasePlayer player)
        {
            if (_markers.ContainsKey(player.userID)) return;

            var marker = GameManager.server.CreateEntity(PrefabMarker, player.transform.position)
                         as MapMarkerGenericRadius;

            if (marker == null) return;

            marker.alpha  = _config.MarkerAlpha;
            var col = ParseMarkerColor();
            marker.color1 = col;
            marker.color2 = col;
            marker.radius = _config.MarkerRadius;
            marker.Spawn();
            marker.SendUpdate();

            _markers[player.userID] = marker;
        }

        private void KillMarker(ulong userId)
        {
            if (!_markers.TryGetValue(userId, out var marker)) return;
            if (marker != null && !marker.IsDestroyed) marker.Kill();
            _markers.Remove(userId);
        }

        private void KillAllMarkers()
        {
            foreach (var kvp in _markers)
                if (kvp.Value != null && !kvp.Value.IsDestroyed)
                    kvp.Value.Kill();
            _markers.Clear();
        }
        #endregion

        #region HUD
        private void ShowHud(BasePlayer player, string message)
            => player.SendConsoleCommand("gametip.showgametip", message);

        private void ClearHud(BasePlayer player)
            => player.SendConsoleCommand("gametip.hidegametip");
        #endregion

        #region Helpers
        private Color ParseMarkerColor()
        {
            try
            {
                var parts = _config.MarkerColor.Trim().Split(' ');
                return new Color(
                    float.Parse(parts[0]),
                    float.Parse(parts[1]),
                    float.Parse(parts[2])
                );
            }
            catch { return Color.red; }
        }

        private void BroadcastAll(string message)
            => Server.Broadcast(message, _config.ChatIcon);

        private string FormatTime(int seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s"
                : $"{ts.Seconds}s";
        }

        private BasePlayer FindPlayer(string nameOrId)
        {
            if (ulong.TryParse(nameOrId, out ulong id))
            {
                var exact = BasePlayer.FindByID(id);
                if (exact != null) return exact;
            }

            var matches = BasePlayer.activePlayerList
                .Where(p => p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }
        #endregion

        #region Commands
        [ChatCommand("hideandseek")]
        private void CmdHideAndSeek(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                SendReply(player, "You don't have permission to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player,
                    "<color=#ff4444>[HideAndSeek]</color> Commands:\n" +
                    "/hideandseek start         — Start a game (random prey)\n" +
                    "/hideandseek set <name>    — Start a game with a specific prey\n" +
                    "/hideandseek stop          — Stop the active game\n" +
                    "/hideandseek status        — Show current game status");
                return;
            }

            switch (args[0].ToLower())
            {
                case "start":
                    if (_gameActive) { SendReply(player, Lang("GameAlreadyActive")); return; }
                    if (_cooldownUntil > Time.realtimeSinceStartup)
                    {
                        int remaining = Mathf.CeilToInt(_cooldownUntil - Time.realtimeSinceStartup);
                        SendReply(player, Lang("OnCooldown", null, remaining));
                        return;
                    }
                    var eligible = BasePlayer.activePlayerList
                        .Where(p => !p.IsNpc && p.IsConnected).ToList();
                    if (eligible.Count < _config.MinPlayers)
                    {
                        SendReply(player, Lang("NotEnoughPlayers", null, _config.MinPlayers));
                        return;
                    }
                    StartGame();
                    break;

                case "set":
                    if (_gameActive) { SendReply(player, Lang("GameAlreadyActive")); return; }
                    if (args.Length < 2)
                    {
                        SendReply(player, "Usage: /hideandseek set <player name or Steam ID>");
                        return;
                    }
                    string query = string.Join(" ", args.Skip(1));
                    var target = FindPlayer(query);
                    if (target == null)
                    {
                        SendReply(player, Lang("PlayerNotFound", player.UserIDString, query));
                        return;
                    }
                    var elig2 = BasePlayer.activePlayerList
                        .Where(p => !p.IsNpc && p.IsConnected).ToList();
                    if (elig2.Count < _config.MinPlayers)
                    {
                        SendReply(player, Lang("NotEnoughPlayers", null, _config.MinPlayers));
                        return;
                    }
                    SendReply(player, Lang("PreySet", player.UserIDString, target.displayName));
                    StartGame(target);
                    break;

                case "stop":
                    if (!_gameActive) { SendReply(player, Lang("NoGameActive")); return; }
                    EndGame(true);
                    break;

                case "status":
                    if (!_gameActive) { SendReply(player, "No game is currently active."); return; }
                    var r = BasePlayer.FindByID(_preyId);
                    SendReply(player,
                        $"<color=#ff4444>[HideAndSeek]</color> Active!\n" +
                        $"Prey: <color=#ffcc00>{r?.displayName ?? "Unknown"}</color>\n" +
                        $"Time left: <color=#ff8800>{FormatTime(_timeRemaining)}</color>\n" +
                        $"Grace period: <color=#00ff88>{(_graceActive ? "Active" : "Over")}</color>");
                    break;

                default:
                    SendReply(player, "Unknown subcommand. Use /hideandseek for help.");
                    break;
            }
        }

        [ConsoleCommand("hideandseek.start")]
        private void ConsoleStart(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (_gameActive) { Puts("Game already active."); return; }
            StartGame();
        }

        [ConsoleCommand("hideandseek.stop")]
        private void ConsoleStop(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (!_gameActive) { Puts("No game active."); return; }
            EndGame(true);
        }

        [ConsoleCommand("hideandseek.set")]
        private void ConsoleSet(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (_gameActive) { Puts("Game already active. Stop it first."); return; }
            if (arg.Args == null || arg.Args.Length < 1) { Puts("Usage: hideandseek.set <name or steamid>"); return; }

            string query = string.Join(" ", arg.Args);
            var target = FindPlayer(query);
            if (target == null) { Puts($"Player not found: {query}"); return; }

            Puts($"Setting prey to {target.displayName} and starting game.");
            StartGame(target);
        }
        #endregion
    }
}