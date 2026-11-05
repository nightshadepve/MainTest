using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeVoteDay", "Nightshade", "1.2.0")]
    [Description("Vote for day at configurable night time. 75%+ required. One vote per night. Players can buy day instantly via /buyday.")]
    public class NightshadeVoteDay : RustPlugin
    {
        // ─── Config ───────────────────────────────────────────────────────────
        private PluginConfig _config;

        private class PluginConfig
        {
            [JsonProperty("ChatPrefix")]
            public string ChatPrefix    = "<color=#7B68EE>[VoteDay]</color>";

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

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try   { _config = Config.ReadObject<PluginConfig>(); }
            catch { _config = new PluginConfig(); }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();
        protected override void SaveConfig()         => Config.WriteObject(_config);

        // ─── Lang ─────────────────────────────────────────────────────────────
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                // Vote start
                ["VoteStart"]           = "It's {time}! Type /day to vote for daytime. Need {threshold}% ({needed} votes) • {duration}s to vote.",
                ["VoteStartBuyHint"]    = "Can't wait? Use /buyday to force day instantly!",

                // Voting progress
                ["VoteRegistered"]      = "Vote registered! {current}/{needed} needed.",
                ["VoteProgress"]        = "{current}/{needed} votes ({percent}%). Type /day!",
                ["AlreadyVoted"]        = "You've already voted.",

                // Vote end
                ["VotePassed"]          = "Vote Day PASSED — {votes}/{pop} voted ({percent}%). Time set to {daytime}!",
                ["VoteFailed"]          = "Vote Day FAILED — {votes}/{pop} voted ({percent}%). Needed {needed}. See you tomorrow night!",

                // No active vote
                ["NoVoteYet"]           = "No active vote. The window opens at {time} (~{hours} game hour(s) away).",
                ["NoVoteAtTime"]        = "No active vote right now.",
                ["VoteAlreadyDone"]     = "The vote already happened tonight. Check back tomorrow at {time}.",

                // Buynight
                ["BuyDayInfo"]        = "Force it to {daytime} immediately for {cost}. Use /buyday confirm to purchase.",
                ["BuyDayUsage"]       = "Usage: /buyday confirm",
                ["BuyDayDaytime"]     = "It's already daytime — nothing to buy!",
                ["BuyDayClosed"]      = "The vote window for tonight has already closed.",
                ["BuyDayPurchased"]   = "{player} spent {cost} to skip the night. Time set to {daytime}!",

                // Currency errors
                ["NotEnoughRP"]         = "Not enough RP. Need {need} RP, you have {have}.",
                ["NotEnoughEco"]        = "Not enough coins. Need {need}, you have {have}.",
                ["CurrencyInvalid"]     = "Invalid currency config. Must be ServerRewards or Economics. Contact an admin.",
                ["PluginMissingRP"]     = "ServerRewards plugin not found. Contact an admin.",
                ["PluginMissingEco"]    = "Economics plugin not found. Contact an admin.",
            }, this);
        }

        // Helper — prepends the chat prefix and pulls from lang
        private string L(string key, string playerId = null, params (string token, string value)[] replacements)
        {
            string msg = lang.GetMessage(key, this, playerId);
            foreach (var (token, value) in replacements)
                msg = msg.Replace("{" + token + "}", value);
            return $"{_config.ChatPrefix} {msg}";
        }

        // ─── State ────────────────────────────────────────────────────────────
        private bool            _voteActive     = false;
        private bool            _votedThisNight = false;
        private bool            _windowOpen     = false;
        private Timer           _windowTimer;
        private Timer           _voteTimer;
        private HashSet<ulong>  _votes          = new HashSet<ulong>();

        // ─── Plugin references ────────────────────────────────────────────────
        [PluginReference] Plugin ServerRewards;
        [PluginReference] Plugin Economics;

        // ─── Hooks ────────────────────────────────────────────────────────────

        void OnServerInitialized() => timer.Every(10f, CheckGameTime);

        void Unload()
        {
            _windowTimer?.Destroy();
            _voteTimer?.Destroy();
        }

        // ─── Time watcher ─────────────────────────────────────────────────────

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
                OpenWindow();
        }

        // ─── Window & vote lifecycle ──────────────────────────────────────────

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

            Server.Broadcast(L("VoteStart", null,
                ("time",      FormatHour(_config.VoteStartTime)),
                ("threshold", pct.ToString()),
                ("needed",    needed.ToString()),
                ("duration",  ((int)_config.VoteDuration).ToString())));

            Server.Broadcast(L("VoteStartBuyHint"));

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
                ConsoleSystem.Run(ConsoleSystem.Option.Server, $"env.time {_config.DayTime}");
                Server.Broadcast(L("VotePassed", null,
                    ("votes",   votes.ToString()),
                    ("pop",     pop.ToString()),
                    ("percent", Mathf.RoundToInt(ratio * 100f).ToString()),
                    ("daytime", daytime)));
            }
            else
            {
                Server.Broadcast(L("VoteFailed", null,
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

            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"env.time {_config.DayTime}");

            Server.Broadcast(L("BuyDayPurchased", null,
                ("player",  buyer.displayName),
                ("cost",    CostLabel()),
                ("daytime", FormatHour(_config.DayTime))));
        }

        // ─── /day command ─────────────────────────────────────────────────────

        [ChatCommand("day")]
        private void CmdVoteDay(BasePlayer player, string command, string[] args)
        {
            if (!_voteActive)
            {
                if (_votedThisNight)
                {
                    SendReply(player, L("VoteAlreadyDone", player.UserIDString,
                        ("time", FormatHour(_config.VoteStartTime))));
                }
                else
                {
                    float t         = TOD_Sky.Instance?.Cycle.Hour ?? -1f;
                    int   hoursLeft = t >= 0f ? Mathf.CeilToInt(_config.VoteStartTime - (t < _config.VoteStartTime ? t : t - 24f)) : 0;

                    if (hoursLeft > 0)
                        SendReply(player, L("NoVoteYet", player.UserIDString,
                            ("time",  FormatHour(_config.VoteStartTime)),
                            ("hours", hoursLeft.ToString())));
                    else
                        SendReply(player, L("NoVoteAtTime", player.UserIDString));
                }
                return;
            }

            if (_votes.Contains(player.userID))
            {
                SendReply(player, L("AlreadyVoted", player.UserIDString));
                return;
            }

            _votes.Add(player.userID);

            int pop       = BasePlayer.activePlayerList.Count;
            int needed    = Mathf.CeilToInt(pop * _config.VoteThreshold);
            int voteCount = _votes.Count;

            SendReply(player, L("VoteRegistered", player.UserIDString,
                ("current", voteCount.ToString()),
                ("needed",  needed.ToString())));

            Server.Broadcast(L("VoteProgress", null,
                ("current", voteCount.ToString()),
                ("needed",  needed.ToString()),
                ("percent", Mathf.RoundToInt((float)voteCount / pop * 100f).ToString())));

            if (voteCount >= needed)
            {
                _voteTimer?.Destroy();
                EndVote();
            }
        }

        // ─── /buyday command ────────────────────────────────────────────────

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
                SendReply(player, L("BuyDayDaytime", uid));
                return;
            }

            if (_votedThisNight && !_voteActive)
            {
                SendReply(player, L("BuyDayClosed", uid));
                return;
            }

            if (mode == "serverrewards" && ServerRewards == null)
            {
                SendReply(player, L("PluginMissingRP", uid));
                return;
            }
            if (mode == "economics" && Economics == null)
            {
                SendReply(player, L("PluginMissingEco", uid));
                return;
            }
            if (mode != "serverrewards" && mode != "economics")
            {
                SendReply(player, L("CurrencyInvalid", uid));
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, L("BuyDayInfo", uid,
                    ("daytime", FormatHour(_config.DayTime)),
                    ("cost",    CostLabel())));
                return;
            }

            if (args[0].ToLower() != "confirm")
            {
                SendReply(player, L("BuyDayUsage", uid));
                return;
            }

            if (!TryCharge(player, mode)) return;

            ForceDayByPurchase(player);
        }

        // ─── Currency helpers ─────────────────────────────────────────────────

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
                SendReply(player, L("NotEnoughRP", player.UserIDString,
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
                SendReply(player, L("NotEnoughEco", player.UserIDString,
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

        // ─── Utility ──────────────────────────────────────────────────────────

        private string FormatHour(float hour)
        {
            int h = (int)hour % 24;
            return $"{h:D2}00";
        }
    }
}