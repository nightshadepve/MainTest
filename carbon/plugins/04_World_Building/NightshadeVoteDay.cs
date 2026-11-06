using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeVoteDay", "Nightshade", "1.2.1")]
    [Description("Vote for day at configurable night time. 75%+ required. One vote per night. Players can buy day instantly via /buyday.")]
    public class NightshadeVoteDay : RustPlugin
    {
        // --- Config ---
        private const string SkipDataPath = "Nightshade/NightshadeVoteDay";
        private PluginConfig _config;

        private class PluginConfig
        {
            [JsonProperty("Chat server name (sender of vote messages)")]
            public string ChatServerName = "Nightshade";

            [JsonProperty("Chat avatar SteamID (0 = server icon)")]
            public ulong ChatAvatarSteamId = 0;

            [JsonProperty("Vote mode (Daily = vote every night, SkipMultiple = one passing vote skips several nights)")]
            public string VoteMode = "SkipMultiple";

            [JsonProperty("Nights to skip (SkipMultiple only, includes the voted night, minimum 1)")]
            public int NightsToSkip = 2;

            [JsonProperty("VoteStartTime (24hr format)")]
            public float VoteStartTime  = 19f;

            [JsonProperty("DayTime (Game hour to set if vote passes or day is purchased)")]
            public float DayTime        = 8f;

            [JsonProperty("VoteDuration (Time players have to vote in seconds)")]
            public float VoteDuration   = 60f;

            [JsonProperty("WindowDuration (Voting is locked after this many seconds)")]
            public float WindowDuration = 120f;

            [JsonProperty("VoteThreshold (Fraction of online players required to pass, e.g. 0.75 = 75%)")]
            public float VoteThreshold  = 0.75f;

            [JsonProperty("Currency (ServerRewards or Economics)")]
            public string Currency      = "ServerRewards";

            [JsonProperty("ServerRewards Cost")]
            public int RPCost           = 500;

            [JsonProperty("Economics Cost")]
            public double EcoCost       = 500.0;
        }

        private class SkipState
        {
            public int SkipsRemaining;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try   { _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig(); }
            catch { _config = new PluginConfig(); }
            if (string.IsNullOrWhiteSpace(_config.ChatServerName)) _config.ChatServerName = "Nightshade";
            _config.VoteMode = NormalizeMode(_config.VoteMode);
            if (_config.NightsToSkip < 1) _config.NightsToSkip = 2;
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();
        protected override void SaveConfig()         => Config.WriteObject(_config);

        // --- Lang ---
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["VoteStart"]           = "It's {time}! Type /day to vote for daytime. Need {threshold}% ({needed} votes) \u2022 {duration}s to vote.",
                ["VoteStartBuyHint"]    = "Can't wait? Use /buyday to force day instantly!",

                ["VoteRegistered"]      = "Vote registered! {current}/{needed} needed.",
                ["VoteProgress"]        = "{current}/{needed} votes ({percent}%). Type /day!",
                ["AlreadyVoted"]        = "You've already voted.",

                ["VotePassed"]          = "Vote Day PASSED \u2014 {votes}/{pop} voted ({percent}%). Time set to {daytime}!",
                ["VotePassedExtra"]     = "This vote covers {nights} nights, so the next {extra} night(s) are skipped without another vote.",
                ["VoteFailed"]          = "Vote Day FAILED \u2014 {votes}/{pop} voted ({percent}%). Needed {needed}. See you tomorrow night!",

                ["AutoSkip"]            = "Night skipped from the last vote. {left} more night(s) will be skipped after this one.",
                ["AutoSkipLast"]        = "Night skipped from the last vote. Voting opens again next night.",

                ["NoVoteYet"]           = "No active vote. The window opens at {time} (~{hours} game hour(s) away).",
                ["NoVoteAtTime"]        = "No active vote right now.",
                ["VoteAlreadyDone"]     = "The vote already happened tonight. Check back tomorrow at {time}.",

                ["BuyDayInfo"]        = "Force it to {daytime} immediately for {cost}. Use /buyday confirm to purchase.",
                ["BuyDayUsage"]       = "Usage: /buyday confirm",
                ["BuyDayDaytime"]     = "It's already daytime \u2014 nothing to buy!",
                ["BuyDayClosed"]      = "The vote window for tonight has already closed.",
                ["BuyDayPurchased"]   = "{player} spent {cost} to skip the night. Time set to {daytime}!",

                ["NotEnoughRP"]         = "Not enough RP. Need {need} RP, you have {have}.",
                ["NotEnoughEco"]        = "Not enough coins. Need {need}, you have {have}.",
                ["CurrencyInvalid"]     = "Invalid currency config. Must be ServerRewards or Economics. Contact an admin.",
                ["PluginMissingRP"]     = "ServerRewards plugin not found. Contact an admin.",
                ["PluginMissingEco"]    = "Economics plugin not found. Contact an admin.",
            }, this);
        }

        private string L(string key, string playerId = null, params (string token, string value)[] replacements)
        {
            string msg = lang.GetMessage(key, this, playerId);
            foreach (var (token, value) in replacements)
                msg = msg.Replace("{" + token + "}", value);
            return msg;
        }

        private void Say(BasePlayer player, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            string name = string.IsNullOrWhiteSpace(_config.ChatServerName) ? "Nightshade" : _config.ChatServerName.Trim();
            name = name.Replace("<", string.Empty).Replace(">", string.Empty);
            string line = "<color=#C4A6E0>" + name + "</color>: " + message;
            ulong avatar = _config.ChatAvatarSteamId;
            if (player == null)
            {
                ConsoleNetwork.BroadcastToAllClients("chat.add", new object[] { 2, avatar, line });
                return;
            }
            if (player.IsConnected)
                player.SendConsoleCommand("chat.add", 2, avatar, line);
        }

        // --- State ---
        private bool            _voteActive     = false;
        private bool            _votedThisNight = false;
        private bool            _windowOpen     = false;
        private int             _skipsRemaining = 0;
        private Timer           _windowTimer;
        private Timer           _voteTimer;
        private HashSet<ulong>  _votes          = new HashSet<ulong>();

        // --- Plugin references ---
        [PluginReference] Plugin ServerRewards;
        [PluginReference] Plugin Economics;

        // --- Hooks ---

        void OnServerInitialized()
        {
            LoadSkips();
            timer.Every(10f, CheckGameTime);
        }

        void Unload()
        {
            _windowTimer?.Destroy();
            _voteTimer?.Destroy();
        }

        // --- Time watcher ---

        private void CheckGameTime()
        {
            float t = TOD_Sky.Instance?.Cycle.Hour ?? -1f;
            if (t < 0f) return;

            if (t >= 6f && t < _config.VoteStartTime)
            {
                if (_votedThisNight || _windowOpen)
                {
                    _votedThisNight = false;
                    _windowOpen     = false;
                    _windowTimer?.Destroy();
                }
            }

            if (t >= _config.VoteStartTime && !_votedThisNight && !_windowOpen && !_voteActive)
            {
                if (IsSkipMultiple() && _skipsRemaining > 0)
                    SkipQueuedNight();
                else
                    OpenWindow();
            }
        }

        private void SkipQueuedNight()
        {
            _skipsRemaining = Math.Max(0, _skipsRemaining - 1);
            _votedThisNight = true;
            SaveSkips();
            ApplyDay();
            if (_skipsRemaining > 0)
                Say(null, L("AutoSkip", null, ("left", _skipsRemaining.ToString())));
            else
                Say(null, L("AutoSkipLast"));
        }

        // --- Window & vote lifecycle ---

        private void OpenWindow()
        {
            _windowOpen = true;
            StartVote();

            _windowTimer = timer.Once(_config.WindowDuration, () =>
            {
                _windowOpen     = false;
                _votedThisNight = true;
            });
        }

        private void StartVote()
        {
            _voteActive = true;
            _votes.Clear();

            int    pop    = BasePlayer.activePlayerList.Count;
            int    needed = Mathf.CeilToInt(pop * _config.VoteThreshold);
            int    pct    = Mathf.RoundToInt(_config.VoteThreshold * 100f);

            Say(null, L("VoteStart", null,
                ("time",      FormatHour(_config.VoteStartTime)),
                ("threshold", pct.ToString()),
                ("needed",    needed.ToString()),
                ("duration",  ((int)_config.VoteDuration).ToString())));

            Say(null, L("VoteStartBuyHint"));

            _voteTimer = timer.Once(_config.VoteDuration, EndVote);
        }

        private void EndVote()
        {
            _voteTimer?.Destroy();
            _windowTimer?.Destroy();

            _voteActive     = false;
            _windowOpen     = false;
            _votedThisNight = true;

            int    pop      = BasePlayer.activePlayerList.Count;
            int    votes    = _votes.Count;
            float  ratio    = pop > 0 ? (float)votes / pop : 0f;
            bool   passed   = ratio >= _config.VoteThreshold;
            int    needed   = Mathf.CeilToInt(pop * _config.VoteThreshold);
            string daytime  = FormatHour(_config.DayTime);

            if (passed)
            {
                ApplyDay();
                int nights = Math.Max(1, _config.NightsToSkip);
                int extra = 0;
                if (IsSkipMultiple())
                {
                    extra = nights - 1;
                    _skipsRemaining = extra;
                }
                else
                    _skipsRemaining = 0;
                SaveSkips();
                Say(null, L("VotePassed", null,
                    ("votes",   votes.ToString()),
                    ("pop",     pop.ToString()),
                    ("percent", Mathf.RoundToInt(ratio * 100f).ToString()),
                    ("daytime", daytime)));
                if (extra > 0)
                    Say(null, L("VotePassedExtra", null,
                        ("nights", nights.ToString()),
                        ("extra", extra.ToString())));
            }
            else
            {
                Say(null, L("VoteFailed", null,
                    ("votes",   votes.ToString()),
                    ("pop",     pop.ToString()),
                    ("percent", Mathf.RoundToInt(ratio * 100f).ToString()),
                    ("needed",  needed.ToString())));
            }

            _votes.Clear();
        }

        private void ForceDayByPurchase(BasePlayer buyer)
        {
            _voteTimer?.Destroy();
            _windowTimer?.Destroy();
            _voteActive     = false;
            _windowOpen     = false;
            _votedThisNight = true;
            _votes.Clear();

            ApplyDay();

            Say(null, L("BuyDayPurchased", null,
                ("player",  buyer.displayName),
                ("cost",    CostLabel()),
                ("daytime", FormatHour(_config.DayTime))));
        }

        private void ApplyDay()
        {
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "env.time " + _config.DayTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // --- /day command ---

        [ChatCommand("day")]
        private void CmdVoteDay(BasePlayer player, string command, string[] args)
        {
            if (!_voteActive)
            {
                if (_votedThisNight)
                {
                    Say(player, L("VoteAlreadyDone", player.UserIDString,
                        ("time", FormatHour(_config.VoteStartTime))));
                }
                else
                {
                    float t         = TOD_Sky.Instance?.Cycle.Hour ?? -1f;
                    int   hoursLeft = t >= 0f ? Mathf.CeilToInt(_config.VoteStartTime - (t < _config.VoteStartTime ? t : t - 24f)) : 0;

                    if (hoursLeft > 0)
                        Say(player, L("NoVoteYet", player.UserIDString,
                            ("time",  FormatHour(_config.VoteStartTime)),
                            ("hours", hoursLeft.ToString())));
                    else
                        Say(player, L("NoVoteAtTime", player.UserIDString));
                }
                return;
            }

            if (_votes.Contains(player.userID))
            {
                Say(player, L("AlreadyVoted", player.UserIDString));
                return;
            }

            _votes.Add(player.userID);

            int pop       = BasePlayer.activePlayerList.Count;
            int needed    = Mathf.CeilToInt(pop * _config.VoteThreshold);
            int voteCount = _votes.Count;

            Say(player, L("VoteRegistered", player.UserIDString,
                ("current", voteCount.ToString()),
                ("needed",  needed.ToString())));

            Say(null, L("VoteProgress", null,
                ("current", voteCount.ToString()),
                ("needed",  needed.ToString()),
                ("percent", Mathf.RoundToInt((float)voteCount / pop * 100f).ToString())));

            if (voteCount >= needed)
            {
                _voteTimer?.Destroy();
                EndVote();
            }
        }

        // --- /buyday command ---

        [ChatCommand("buyday")]
        private void CmdBuyDay(BasePlayer player, string command, string[] args)
        {
            float  t    = TOD_Sky.Instance?.Cycle.Hour ?? -1f;
            string uid  = player.UserIDString;
            string mode = _config.Currency.ToLower();

            // /buyday is only available at night: 1900-2359 or 0000-0600
            bool isNight = (t >= _config.VoteStartTime) || (t >= 0f && t < 6f);
            if (!isNight)
            {
                Say(player, L("BuyDayDaytime", uid));
                return;
            }

            if (_votedThisNight && !_voteActive)
            {
                Say(player, L("BuyDayClosed", uid));
                return;
            }

            if (mode == "serverrewards" && ServerRewards == null)
            {
                Say(player, L("PluginMissingRP", uid));
                return;
            }
            if (mode == "economics" && Economics == null)
            {
                Say(player, L("PluginMissingEco", uid));
                return;
            }
            if (mode != "serverrewards" && mode != "economics")
            {
                Say(player, L("CurrencyInvalid", uid));
                return;
            }

            if (args.Length == 0)
            {
                Say(player, L("BuyDayInfo", uid,
                    ("daytime", FormatHour(_config.DayTime)),
                    ("cost",    CostLabel())));
                return;
            }

            if (args[0].ToLower() != "confirm")
            {
                Say(player, L("BuyDayUsage", uid));
                return;
            }

            if (!TryCharge(player, mode)) return;

            ForceDayByPurchase(player);
        }

        // --- Currency helpers ---

        private bool TryCharge(BasePlayer player, string mode)
        {
            if (mode == "serverrewards") return TryChargeRP(player);
            if (mode == "economics")     return TryChargeEco(player);
            return false;
        }

        private bool TryChargeRP(BasePlayer player)
        {
            int balance = (int)(ServerRewards.Call("CheckPoints", player.userID) ?? 0);
            if (balance < _config.RPCost)
            {
                Say(player, L("NotEnoughRP", player.UserIDString,
                    ("need", _config.RPCost.ToString()),
                    ("have", balance.ToString())));
                return false;
            }
            ServerRewards.Call("TakePoints", player.userID, _config.RPCost);
            return true;
        }

        private bool TryChargeEco(BasePlayer player)
        {
            double balance = (double)(Economics.Call("Balance", player.UserIDString) ?? 0.0);
            if (balance < _config.EcoCost)
            {
                Say(player, L("NotEnoughEco", player.UserIDString,
                    ("need", _config.EcoCost.ToString()),
                    ("have", balance.ToString())));
                return false;
            }
            Economics.Call("Withdraw", player.UserIDString, _config.EcoCost);
            return true;
        }

        private string CostLabel()
        {
            return _config.Currency.ToLower() == "economics"
                ? $"{_config.EcoCost} coins"
                : $"{_config.RPCost} RP";
        }

        // --- Utility ---

        private bool IsSkipMultiple()
        {
            return !string.Equals(NormalizeMode(_config.VoteMode), "Daily", StringComparison.Ordinal);
        }

        private static string NormalizeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "SkipMultiple";
            mode = mode.Trim();
            if (string.Equals(mode, "Daily", StringComparison.OrdinalIgnoreCase)) return "Daily";
            if (string.Equals(mode, "SkipMultiple", StringComparison.OrdinalIgnoreCase)) return "SkipMultiple";
            return "SkipMultiple";
        }

        private void LoadSkips()
        {
            try
            {
                SkipState state = Interface.Oxide.DataFileSystem.ReadObject<SkipState>(SkipDataPath);
                _skipsRemaining = state != null && state.SkipsRemaining > 0 ? state.SkipsRemaining : 0;
            }
            catch
            {
                _skipsRemaining = 0;
            }
        }

        private void SaveSkips()
        {
            if (_skipsRemaining < 0) _skipsRemaining = 0;
            Interface.Oxide.DataFileSystem.WriteObject(SkipDataPath, new SkipState { SkipsRemaining = _skipsRemaining });
        }

        private string FormatHour(float hour)
        {
            int h = (int)hour % 24;
            return $"{h:D2}00";
        }
    }
}
