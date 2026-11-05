using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("VIPPerks", "Nightshade", "1.2.1")]
    [Description("VIP/VIPx AFK protection, locked crate timer reductions, and daily/4-day kits")]
    class VIPPerks : RustPlugin
    {
        // ─────────────────────────────────────────────
        //  Permissions
        // ─────────────────────────────────────────────
        private const string PERM_VIP  = "vipperks.vip";
        private const string PERM_VIPX = "vipperks.vipx";

        // ─────────────────────────────────────────────
        //  Crate unlock times (seconds)
        // ─────────────────────────────────────────────
        private const float CRATE_TIME_VIP  = 150f; // 2 min 30 s
        private const float CRATE_TIME_VIPX = 60f;  // 1 min

        // ─────────────────────────────────────────────
        //  AFK tracking
        // ─────────────────────────────────────────────
        private class AfkData
        {
            public Vector3    LastPosition;
            public Quaternion LastRotation;
            public bool       IsAfk;
            public float      Health;
            public float      Hydration;
            public float      Food;
            // Cached refs — avoids FindByID calls inside repeating timers
            public BasePlayer Player;
            public Timer      FreezeTimer;
            public Timer      MovementTimer;
        }

        private readonly Dictionary<ulong, AfkData> _afkPlayers = new Dictionary<ulong, AfkData>();

        // Movement threshold — if the player moves more than this many units they exit AFK
        private const float MOVE_THRESHOLD = 0.2f;

        // ─────────────────────────────────────────────
        //  Kit cooldowns
        // ─────────────────────────────────────────────
        // Cooldown durations
        private const double KIT_DAILY_HOURS    = 24.0;
        private const double KIT_4DAY_HOURS     = 96.0;
        private const double WIPE_LOCKOUT_HOURS = 48.0;  // kits locked for 48h after wipe

        // Data file keys
        private const string DATA_DAILY    = "VIPPerks_DailyKit";
        private const string DATA_4DAY     = "VIPPerks_4DayKit";
        private const string DATA_WIPE     = "VIPPerks_WipeTime";

        // In-memory cooldown tables: SteamID → UTC DateTime of last claim
        private Dictionary<ulong, DateTime> _dailyCooldowns = new Dictionary<ulong, DateTime>();
        private Dictionary<ulong, DateTime> _4dayCooldowns  = new Dictionary<ulong, DateTime>();

        // UTC time of last wipe — DateTime.MinValue means "not set / no lockout active"
        private DateTime _wipeTime = DateTime.MinValue;

        // Tracks which players have already been notified for each kit this cycle
        // so we only ping them once when the cooldown expires, not every tick.
        private readonly HashSet<ulong> _notifiedDaily = new HashSet<ulong>();
        private readonly HashSet<ulong> _notified4Day  = new HashSet<ulong>();


        // Each entry: (shortname, amount, skinID)
        // Adjust these to whatever you want in the kits.

        // ── VIP Daily Kit ─────────────────────────────
        private static readonly (string item, int amount, ulong skin)[] KIT_VIP_DAILY = new[]
        {
            // Teas
            ("advanceharvestingtea",      1, 0UL),
            ("healingtea.advanced",       1, 0UL),
            ("oretea.advanced",           1, 0UL),
            ("scraptea.advanced",         1, 0UL),
            ("woodtea.advanced",          1, 0UL),
            // Consumables / medical
            ("lowgradefuel",         1000, 0UL),
            ("pumpkin",                10, 0UL),
            ("bandage",                 5, 0UL),
            ("largemedkit",             5, 0UL),
            ("syringe.medical",        10, 0UL),
            // Components
            ("sewingkit",              10, 0UL),
            ("sheetmetal",             10, 0UL),
            ("roadsigns",              10, 0UL),
            ("metalpipe",               5, 0UL),
            ("cctv.camera",             2, 0UL),
            ("tarp",                    4, 0UL),
        };

        private static readonly (string item, int amount, ulong skin)[] KIT_VIPX_DAILY = new[]
        {
            // Teas
            ("radiationresisttea.pure",  1, 0UL),
            ("purecraftingtea_quality",  1, 0UL),
            ("healingtea.pure",          2, 0UL),
            // Medical
            ("bandage",                 10, 0UL),
            ("largemedkit",              5, 0UL),
            ("syringe.medical",         20, 0UL),
            // Components
            ("sewingkit",               20, 0UL),
            ("sheetmetal",              10, 0UL),
            ("roadsigns",               10, 0UL),
            ("metalpipe",               10, 0UL),
            // Misc
            ("supply.signal",            1, 0UL),
        };

        // ── VIP 4-Day Kit ─────────────────────────────
        // Easy Bradley signal skin: 2905355269  |  Easy Heli signal skin: 2920175997
        private static readonly (string item, int amount, ulong skin)[] KIT_VIP_4DAY = new[]
        {
            ("hazmatsuit",       1,   0UL),
            ("ammo.pistol",    100,   0UL),
            ("keycard_green",    1,   0UL),
            ("bandage",          5,   0UL),
            ("bearmeat.cooked",  1,   0UL),
            ("supply.signal",    1,   0UL),
            ("supply.signal",    1,   2905355269UL),  // Easy Bradley signal
            ("supply.signal",    1,   2920175997UL),  // Easy Heli signal
        };

        private static readonly (string item, int amount, ulong skin)[] KIT_VIPX_4DAY = new[]
        {
            // Explosives
            ("explosive.timed",  2,   0UL),
            ("gunpowder",        5,   0UL),
            // Signals
            ("supply.signal",    1,   2905355269UL),  // Easy Bradley signal
            ("supply.signal",    1,   2920175997UL),  // Easy Heli signal
            ("diesel_barrel",    5,   0UL),
            // Keycards
            ("keycard_green",    1,   0UL),
            ("keycard_blue",     1,   0UL),
            // Misc
            ("supply.signal",    2,   0UL),
            ("syringe.medical", 40,   0UL),
        };

        // ─────────────────────────────────────────────
        //  Oxide hooks
        // ─────────────────────────────────────────────
        void Init()
        {
            permission.RegisterPermission(PERM_VIP,  this);
            permission.RegisterPermission(PERM_VIPX, this);
            LoadKitData();
        }

        void OnServerInitialized()
        {
            // Check every minute whether any VIP kit cooldowns have expired
            timer.Every(60f, KitReadyNotificationTick);
        }

        // ── Console command: run this right after each wipe ──
        // Usage: vipperks.setwipe
        [ConsoleCommand("vipperks.setwipe")]
        void CmdSetWipe(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null) return; // server console only
            _wipeTime = DateTime.UtcNow;
            SaveKitData();
            Puts($"[VIPPerks] Wipe time set to {_wipeTime:u}. Kits locked for {WIPE_LOCKOUT_HOURS}h.");
        }

        // Returns how many hours of lockout remain (0 = open)
        private double WipeLockoutHoursRemaining()
        {
            if (_wipeTime == DateTime.MinValue) return 0;
            double elapsed = (DateTime.UtcNow - _wipeTime).TotalHours;
            return elapsed < WIPE_LOCKOUT_HOURS ? WIPE_LOCKOUT_HOURS - elapsed : 0;
        }


        [ChatCommand("afk")]
        void CmdAfk(BasePlayer player, string command, string[] args)
        {
            if (!IsVip(player))
            {
                SendReply(player, "<color=#ff4444>You need VIP or VIPx to use AFK mode.</color>");
                return;
            }

            if (_afkPlayers.TryGetValue(player.userID, out var data) && data.IsAfk)
            {
                ExitAfk(player);
            }
            else
            {
                EnterAfk(player);
            }
        }

        // ── /perks overview + claim command ──────────
        [ChatCommand("perks")]
        void CmdPerks(BasePlayer player, string command, string[] args)
        {
            bool isVipx = HasPermission(player, PERM_VIPX);
            bool isVip  = HasPermission(player, PERM_VIP);

            if (!isVip && !isVipx)
            {
                SendReply(player, "<color=#ff4444>You do not have a VIP rank on this server.</color>");
                return;
            }

            // Sub-command routing
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "daily": CmdDailyKit(player, command, new string[0]); return;
                    case "4day":  Cmd4DayKit(player, command, new string[0]);  return;
                }
            }

            string tier = isVipx ? "<color=#00BFFF>VIPx</color>" : "<color=#FFD700>VIP</color>";

            // Daily kit status
            string dailyStatus;
            if (_dailyCooldowns.TryGetValue(player.userID, out DateTime lastDaily) &&
                (DateTime.UtcNow - lastDaily).TotalHours < KIT_DAILY_HOURS)
                dailyStatus = $"<color=#ff4444>{FormatHours(KIT_DAILY_HOURS - (DateTime.UtcNow - lastDaily).TotalHours)}</color>";
            else
                dailyStatus = "<color=#00ff88>READY</color>";

            // 4-day kit status
            string fourDayStatus;
            if (_4dayCooldowns.TryGetValue(player.userID, out DateTime last4Day) &&
                (DateTime.UtcNow - last4Day).TotalHours < KIT_4DAY_HOURS)
                fourDayStatus = $"<color=#ff4444>{FormatHours(KIT_4DAY_HOURS - (DateTime.UtcNow - last4Day).TotalHours)}</color>";
            else
                fourDayStatus = "<color=#00ff88>READY</color>";

            // Crate unlock time
            float crateTime = GetCrateTime(player);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"\n{tier} <color=#ffffff>Perks</color>");
            sb.AppendLine($"<color=#aaaaaa>Crate:</color> <color=#ffffff><b>{FormatTime(crateTime)}</b></color>");

            double lockoutLeft = WipeLockoutHoursRemaining();
            if (lockoutLeft > 0)
            {
                sb.AppendLine($"<color=#ff4444>Kits locked for <b>{FormatHours(lockoutLeft)}</b> after wipe.</color>");
            }
            else
            {
                sb.AppendLine($"<color=#aaaaaa>Daily:</color> {dailyStatus}");
                sb.AppendLine($"<color=#aaaaaa>Claim:</color> <color=#ffffff>/perks daily</color>");
                sb.AppendLine($"<color=#aaaaaa>4-Day:</color> {fourDayStatus}");
                sb.AppendLine($"<color=#aaaaaa>Claim:</color> <color=#ffffff>/perks 4day</color>");
            }

            SendReply(player, sb.ToString());
        }


        [ChatCommand("vipdailykit")]
        void CmdDailyKit(BasePlayer player, string command, string[] args)
        {
            if (!IsVip(player))
            {
                SendReply(player, "<color=#ff4444>You need VIP or VIPx to claim this kit.</color>");
                return;
            }

            double lockoutLeft = WipeLockoutHoursRemaining();
            if (lockoutLeft > 0)
            {
                SendReply(player, $"<color=#ff4444>Kits are locked for <b>{FormatHours(lockoutLeft)}</b> after wipe.</color>");
                return;
            }

            if (!TryConsumeCooldown(player, _dailyCooldowns, KIT_DAILY_HOURS, out double hoursLeft))
            {
                SendReply(player, $"<color=#ff4444>Daily kit is on cooldown. Available in <b>{FormatHours(hoursLeft)}</b>.</color>");
                return;
            }

            var kit = HasPermission(player, PERM_VIPX) ? KIT_VIPX_DAILY : KIT_VIP_DAILY;
            if (!TryGiveKit(player, kit))
            {
                // Roll back the cooldown — player didn't receive anything
                _dailyCooldowns.Remove(player.userID);
                SaveKitData();
                SendReply(player, "<color=#ff4444>Your inventory is full! Make space and try again — your cooldown has <b>not</b> been used.</color>");
                return;
            }

            SaveKitData();
            _notifiedDaily.Remove(player.userID);
            string tier = HasPermission(player, PERM_VIPX) ? "<color=#00BFFF>VIPx</color>" : "<color=#FFD700>VIP</color>";
            SendReply(player, $"{tier} <color=#aaffaa>daily kit claimed! Next claim available in <b>24 hours</b>.</color>");
        }

        [ChatCommand("vip4daykit")]
        void Cmd4DayKit(BasePlayer player, string command, string[] args)
        {
            if (!IsVip(player))
            {
                SendReply(player, "<color=#ff4444>You need VIP or VIPx to claim this kit.</color>");
                return;
            }

            double lockoutLeft = WipeLockoutHoursRemaining();
            if (lockoutLeft > 0)
            {
                SendReply(player, $"<color=#ff4444>Kits are locked for <b>{FormatHours(lockoutLeft)}</b> after wipe.</color>");
                return;
            }

            if (!TryConsumeCooldown(player, _4dayCooldowns, KIT_4DAY_HOURS, out double hoursLeft))
            {
                SendReply(player, $"<color=#ff4444>4-day kit is on cooldown. Available in <b>{FormatHours(hoursLeft)}</b>.</color>");
                return;
            }

            var kit = HasPermission(player, PERM_VIPX) ? KIT_VIPX_4DAY : KIT_VIP_4DAY;
            if (!TryGiveKit(player, kit))
            {
                _4dayCooldowns.Remove(player.userID);
                SaveKitData();
                SendReply(player, "<color=#ff4444>Your inventory is full! Make space and try again — your cooldown has <b>not</b> been used.</color>");
                return;
            }

            SaveKitData();
            _notified4Day.Remove(player.userID);
            string tier = HasPermission(player, PERM_VIPX) ? "<color=#00BFFF>VIPx</color>" : "<color=#FFD700>VIP</color>";
            SendReply(player, $"{tier} <color=#aaffaa>4-day kit claimed! Next claim available in <b>4 days</b>.</color>");
        }

        // ── Detect movement to auto-exit AFK ─────────
        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || !_afkPlayers.TryGetValue(player.userID, out var data) || !data.IsAfk)
                return;

            // Any WASD / jump / crouch input breaks AFK
            if (input.IsDown(BUTTON.FORWARD)  ||
                input.IsDown(BUTTON.BACKWARD) ||
                input.IsDown(BUTTON.LEFT)      ||
                input.IsDown(BUTTON.RIGHT)     ||
                input.IsDown(BUTTON.JUMP)      ||
                input.IsDown(BUTTON.DUCK))
            {
                ExitAfk(player);
            }
        }

        // Prevent idle kick while AFK
        object OnPlayerIdle(BasePlayer player)
        {
            if (_afkPlayers.TryGetValue(player.userID, out var data) && data.IsAfk)
                return false; // returning non-null cancels the kick
            return null;
        }

        // Clean up on disconnect
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            _afkPlayers.Remove(player.userID);
            _notifiedDaily.Remove(player.userID);
            _notified4Day.Remove(player.userID);
        }

        // Notify on connect if kits are already available
        void OnPlayerConnected(BasePlayer player)
        {
            if (!IsVip(player)) return;

            // Delay slightly so the player's client is fully loaded before receiving chat
            timer.Once(5f, () =>
            {
                if (player == null || !player.IsConnected) return;

                bool dailyReady = !_dailyCooldowns.TryGetValue(player.userID, out DateTime lastDaily)
                    || (DateTime.UtcNow - lastDaily).TotalHours >= KIT_DAILY_HOURS;

                bool fourDayReady = !_4dayCooldowns.TryGetValue(player.userID, out DateTime last4Day)
                    || (DateTime.UtcNow - last4Day).TotalHours >= KIT_4DAY_HOURS;

                if (dailyReady)
                {
                    _notifiedDaily.Add(player.userID);
                    SendReply(player, "<color=#FFD700>★</color> <color=#aaffaa>Your <b>daily kit</b> is ready! Type <b>/perks daily</b> to claim.</color>");
                }
                if (fourDayReady)
                {
                    _notified4Day.Add(player.userID);
                    SendReply(player, "<color=#FFD700>★</color> <color=#aaffaa>Your <b>4-day kit</b> is ready! Type <b>/perks 4day</b> to claim.</color>");
                }
            });
        }

        // ── Hackable locked crate ─────────────────────

        // Tracks which player initiated each crate hack (CanHackCrate has the player ref,
        // OnCrateHack does not — this bridges the gap).
        private readonly Dictionary<ulong, ulong> _crateHackers = new Dictionary<ulong, ulong>();

        // CanHackCrate fires just BEFORE hacking starts and includes the player.
        // We record who is hacking so OnCrateHack can look them up.
        object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player != null && crate != null)
                _crateHackers[crate.net.ID.Value] = player.userID;

            return null; // null = allow the hack to proceed normally
        }

        // OnCrateHack fires immediately after StartHacking() resets hackSeconds to 0.
        // hackSeconds = seconds already elapsed; to finish in `desired` seconds we set it
        // to (requiredHackSeconds - desired), so the progress counter reaches the threshold
        // after exactly `desired` more ticks.
        void OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null) return;

            ulong netId = crate.net.ID.Value;
            if (!_crateHackers.TryGetValue(netId, out ulong userId)) return;

            BasePlayer player = BasePlayer.FindByID(userId);
            if (player == null) return;

            float desired = GetCrateTime(player);
            if (desired >= HackableLockedCrate.requiredHackSeconds) return; // non-VIP

            float fastForward = HackableLockedCrate.requiredHackSeconds - desired;
            crate.hackSeconds = fastForward;

            // hackSeconds fast-forward is sufficient; the client UI self-corrects on next tick

            SendReply(player, $"<color=#FFD700>VIP perk active — crate unlocks in <b>{FormatTime(desired)}</b>!</color>");
        }

        // Clean up the tracking dict when a crate is destroyed/looted
        void OnEntityKill(HackableLockedCrate crate)
        {
            if (crate?.net != null)
                _crateHackers.Remove(crate.net.ID.Value);
        }

        // ─────────────────────────────────────────────
        //  AFK helpers
        // ─────────────────────────────────────────────
        private void EnterAfk(BasePlayer player)
        {
            var data = new AfkData
            {
                LastPosition  = player.transform.position,
                LastRotation  = player.transform.rotation,
                IsAfk         = true,
                Health        = player.health,
                Hydration     = player.metabolism.hydration.value,
                Food          = player.metabolism.calories.value,
                Player        = player,
            };

            // Per-player 1s freeze timer — only runs while THIS player is AFK
            data.FreezeTimer = timer.Every(1f, () =>
            {
                if (player == null || !player.IsConnected) { ExitAfk(player); return; }
                player.health = data.Health;
                player.metabolism.hydration.SetValue(data.Hydration);
                player.metabolism.calories.SetValue(data.Food);
                player.metabolism.SendChangesToClient();
            });

            // Per-player 0.5s position check — replaces the global OnPlayerTick hook
            data.MovementTimer = timer.Every(0.5f, () =>
            {
                if (player == null || !player.IsConnected) { ExitAfk(player); return; }
                if (Vector3.Distance(player.transform.position, data.LastPosition) > MOVE_THRESHOLD)
                    ExitAfk(player);
            });

            _afkPlayers[player.userID] = data;

            string tier = HasPermission(player, PERM_VIPX) ? "<color=#00BFFF>VIPx</color>" : "<color=#FFD700>VIP</color>";
            SendReply(player, $"{tier} <color=#aaffaa>AFK mode <b>enabled</b>. Your stats are frozen. Move to exit.</color>");
        }

        private void ExitAfk(BasePlayer player)
        {
            if (!_afkPlayers.TryGetValue(player.userID, out var data)) return;
            data.FreezeTimer?.Destroy();
            data.MovementTimer?.Destroy();
            _afkPlayers.Remove(player.userID);
            if (player != null && player.IsConnected)
                SendReply(player, "<color=#aaffaa>AFK mode <b>disabled</b>. Welcome back!</color>");
        }

        // ─────────────────────────────────────────────
        //  Kit ready notifications
        // ─────────────────────────────────────────────
        private void KitReadyNotificationTick()
        {
            DateTime now = DateTime.UtcNow;
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                bool vipx = HasPermission(player, PERM_VIPX);
                if (!vipx && !HasPermission(player, PERM_VIP)) continue;

                ulong uid = player.userID;

                // Daily kit
                bool dailyReady = !_dailyCooldowns.TryGetValue(uid, out DateTime lastDaily)
                    || (now - lastDaily).TotalHours >= KIT_DAILY_HOURS;

                if (dailyReady && _notifiedDaily.Add(uid))
                    SendReply(player, "<color=#FFD700>★</color> <color=#aaffaa>Your <b>daily kit</b> is ready! Type <b>/perks daily</b> to claim.</color>");
                else if (!dailyReady)
                    _notifiedDaily.Remove(uid);

                // 4-day kit
                bool fourDayReady = !_4dayCooldowns.TryGetValue(uid, out DateTime last4Day)
                    || (now - last4Day).TotalHours >= KIT_4DAY_HOURS;

                if (fourDayReady && _notified4Day.Add(uid))
                    SendReply(player, "<color=#FFD700>★</color> <color=#aaffaa>Your <b>4-day kit</b> is ready! Type <b>/perks 4day</b> to claim.</color>");
                else if (!fourDayReady)
                    _notified4Day.Remove(uid);
            }
        }

        // ─────────────────────────────────────────────
        //  Permission helpers
        // ─────────────────────────────────────────────
        private bool IsVip(BasePlayer player)
            => HasPermission(player, PERM_VIP) || HasPermission(player, PERM_VIPX);

        private bool HasPermission(BasePlayer player, string perm)
            => permission.UserHasPermission(player.UserIDString, perm);

        private float GetCrateTime(BasePlayer player)
        {
            if (HasPermission(player, PERM_VIPX)) return CRATE_TIME_VIPX;
            if (HasPermission(player, PERM_VIP))  return CRATE_TIME_VIP;
            return HackableLockedCrate.requiredHackSeconds; // default (15 min)
        }

        // ─────────────────────────────────────────────
        //  Kit helpers
        // ─────────────────────────────────────────────

        /// <summary>
        /// Checks the cooldown table. If the cooldown has expired (or was never set),
        /// stamps the current time and returns true. Returns false + remaining hours if
        /// still on cooldown.
        /// </summary>
        private bool TryConsumeCooldown(BasePlayer player, Dictionary<ulong, DateTime> table,
                                        double cooldownHours, out double hoursLeft)
        {
            hoursLeft = 0;
            if (table.TryGetValue(player.userID, out DateTime last))
            {
                double elapsed = (DateTime.UtcNow - last).TotalHours;
                if (elapsed < cooldownHours)
                {
                    hoursLeft = cooldownHours - elapsed;
                    return false;
                }
            }
            table[player.userID] = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Checks that main inventory + belt have room for the whole kit (counting real
        /// empty slots and skin-matched partial stacks). Returns false without giving
        /// anything if there isn't room. Any overflow at give time is dropped at the
        /// player's feet rather than lost.
        /// </summary>
        private bool TryGiveKit(BasePlayer player, (string item, int amount, ulong skin)[] kit)
        {
            var main = player.inventory.containerMain;
            var belt = player.inventory.containerBelt;

            // Work out how many new stacks (slots) the kit needs
            int slotsNeeded = 0;
            foreach (var (shortname, amount, skin) in kit)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(shortname);
                if (def == null)
                {
                    Puts($"[VIPPerks] Unknown item shortname: {shortname}");
                    continue;
                }

                int remaining = amount;
                remaining -= PartialStackRoom(main, def, skin);
                remaining -= PartialStackRoom(belt, def, skin);

                if (remaining > 0)
                {
                    int stackSize = Math.Max(1, def.stackable);
                    slotsNeeded += (remaining + stackSize - 1) / stackSize;
                }
            }

            int freeSlots = CountFreeSlots(main) + CountFreeSlots(belt);
            if (slotsNeeded > freeSlots)
            {
                Puts($"[VIPPerks] Kit refused for {player.displayName}: needs {slotsNeeded} slots, " +
                     $"free={freeSlots} (main cap={main?.capacity}, items={main?.itemList.Count}; " +
                     $"belt cap={belt?.capacity}, items={belt?.itemList.Count})");
                return false;
            }

            // Space confirmed — hand everything out (GiveItem splits stacks itself)
            foreach (var (shortname, amount, skin) in kit)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(shortname);
                if (def == null) continue;

                Item item = ItemManager.Create(def, amount, skin);
                if (item == null) continue;

                if (!player.inventory.GiveItem(item))
                    item.Drop(player.GetDropPosition(), player.GetDropVelocity());
            }
            return true;
        }

        private int CountFreeSlots(ItemContainer c)
        {
            if (c == null) return 0;
            int free = 0;
            for (int i = 0; i < c.capacity; i++)
                if (c.GetSlot(i) == null) free++;
            return free;
        }

        private int PartialStackRoom(ItemContainer c, ItemDefinition def, ulong skin)
        {
            if (c == null) return 0;
            int room = 0;
            foreach (Item slot in c.itemList)
                if (slot.info == def && slot.skin == skin && slot.amount < slot.MaxStackable())
                    room += slot.MaxStackable() - slot.amount;
            return room;
        }

        // ── Kit data persistence ──────────────────────

        private void LoadKitData()
        {
            _dailyCooldowns = Interface.Oxide.DataFileSystem
                .ReadObject<Dictionary<ulong, DateTime>>(DATA_DAILY)
                ?? new Dictionary<ulong, DateTime>();

            _4dayCooldowns = Interface.Oxide.DataFileSystem
                .ReadObject<Dictionary<ulong, DateTime>>(DATA_4DAY)
                ?? new Dictionary<ulong, DateTime>();

            var wipeData = Interface.Oxide.DataFileSystem
                .ReadObject<Dictionary<string, DateTime>>(DATA_WIPE);
            _wipeTime = wipeData != null && wipeData.TryGetValue("wipe", out DateTime wt)
                ? wt : DateTime.MinValue;
        }

        private void SaveKitData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DATA_DAILY, _dailyCooldowns);
            Interface.Oxide.DataFileSystem.WriteObject(DATA_4DAY,  _4dayCooldowns);
            Interface.Oxide.DataFileSystem.WriteObject(DATA_WIPE,
                new Dictionary<string, DateTime> { ["wipe"] = _wipeTime });
        }

        // ─────────────────────────────────────────────
        //  Utility
        // ─────────────────────────────────────────────
        private string FormatTime(float seconds)
        {
            int m = (int)seconds / 60;
            int s = (int)seconds % 60;
            return m > 0 ? $"{m}m {s:D2}s" : $"{s}s";
        }

        private string FormatHours(double hours)
        {
            if (hours >= 24.0)
            {
                int days  = (int)(hours / 24);
                int hrs   = (int)(hours % 24);
                return hrs > 0 ? $"{days}d {hrs}h" : $"{days}d";
            }
            int h = (int)hours;
            int m = (int)((hours - h) * 60);
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }

        // Human-readable names for /perks display
        private string FriendlyName(string shortname, ulong skin)
        {
            // Skinned supply signals get a custom label
            if (shortname == "supply.signal")
            {
                if (skin == 2905355269UL) return "Easy Bradley Signal";
                if (skin == 2920175997UL) return "Easy Heli Signal";
                return "Supply Signal";
            }

            switch (shortname)
            {
                case "hazmatsuit":               return "Hazmat Suit";
                case "ammo.pistol":              return "Pistol Bullets";
                case "ammo.rifle":               return "Rifle Ammo";
                case "keycard_green":            return "Green Keycard";
                case "keycard_blue":             return "Blue Keycard";
                case "bandage":                  return "Bandage";
                case "bearmeat.cooked":          return "Cooked Bear Meat";
                case "largemedkit":              return "Large Med Kit";
                case "syringe.medical":          return "Medical Syringe";
                case "lowgradefuel":             return "Low Grade Fuel";
                case "pumpkin":                  return "Pumpkin";
                case "sewingkit":                return "Sewing Kit";
                case "sheetmetal":               return "Sheet Metal";
                case "roadsigns":                return "Road Signs";
                case "metalpipe":                return "Metal Pipe";
                case "cctv.camera":              return "CCTV Camera";
                case "tarp":                     return "Tarp";
                case "explosive.timed":          return "C4";
                case "gunpowder":                return "Gun Powder";
                case "diesel_barrel":            return "Diesel Barrel";
                case "advanceharvestingtea":       return "Advanced Harvesting Tea";
                case "healingtea.advanced":        return "Advanced Healing Tea";
                case "oretea.advanced":            return "Advanced Ore Tea";
                case "scraptea.advanced":          return "Advanced Scrap Tea";
                case "woodtea.advanced":           return "Advanced Wood Tea";
                case "radiationresisttea.advanced":return "Advanced Rad. Resist Tea";
                case "advancedcraftingtea_quality":return "Advanced Crafting Tea";
                case "radiationresisttea.pure":    return "Pure Rad. Resist Tea";
                case "purequalitytea":             return "Pure Crafting Tea";
                case "healingtea.pure":            return "Pure Healing Tea";
                default:                         return shortname;
            }
        }
    }
}
