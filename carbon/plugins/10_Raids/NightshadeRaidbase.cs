using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightshadeRaidbase", "Nightshade", "1.0.16")]
    [Description("Prepares Nightshade raid bases: loot fill, furnace setup, powered turrets/SAMs, ZoneManager zones, ZoneDomes domes, and TruePVE mappings.")]
    public class NightshadeRaidbase : RustPlugin
    {
        private const string PermPve = "nightshade.raidbase.pve";
        private const string PermPvp = "nightshade.raidbase.pvp";
        private const string PermAdmin = "nightshade.raidbase.admin";
        private const string PermStart = "nightshade.raidbase.start";
        private const string PermReady = "nightshade.raidbase.ready";
        private const string CopyPasteCopyPermission = "copypaste.copy";

        [PluginReference] private Plugin ZoneManager;
        [PluginReference] private Plugin ZoneDomes;
        [PluginReference] private Plugin TruePVE;
        [PluginReference] private Plugin CopyPaste;
        [PluginReference] private Plugin AdvancedStatus;
        [PluginReference] private Plugin UnifiedLootProtection;

        private PluginConfig _config;
        private PluginData _data;
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<string, List<LootEntry>> _lootCache = new Dictionary<string, List<LootEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _pendingCopyNumbers = new Dictionary<string, int>();
        private readonly Dictionary<string, PendingCopyRequest> _pendingCopyRequests = new Dictionary<string, PendingCopyRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, string> _pendingCopyByPlayer = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, PreparedRaidBaseState> _preparedRaidBases = new Dictionary<ulong, PreparedRaidBaseState>();

        private ulong _activeRaidMapPinId;
        private ulong _activeRaidMapCircleId;

        private const string CleanupUiName = "NightshadeRaidbaseCleanupTimer";
        private const string AdvancedStatusCreateBar = "CreateBar";
        private const string AdvancedStatusDeleteAllBars = "DeleteAllBars";
        private const string CleanupStatusBarId = "NightshadeRaidbaseCleanup";
        private readonly HashSet<ulong> _activeRaidEntityIds = new HashSet<ulong>();
        private ulong _activeRaidTcId;
        private string _activeRaidZoneId = string.Empty;
        private string _activeRaidRuleSet = string.Empty;
        private Vector3 _activeRaidCenter = Vector3.zero;
        private float _activeRaidRadius;
        private bool _activeRaidPvp;
        private bool _raidStarted;
        private bool _cleanupCountdownActive;
        private float _cleanupEndRealtime;
        private Timer _cleanupEndTimer;
        private Timer _cleanupUiTimer;
        private readonly HashSet<ulong> _playersWithCleanupUi = new HashSet<ulong>();

        private const string DefaultRaidbaseLootJson = @"[
  {
    ""shortname"": ""ammo.shotgun"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 75,
    ""amountMin"": 55,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.shotgun.fire"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 90,
    ""amountMin"": 45,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.shotgun.slug"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 90,
    ""amountMin"": 55,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.grenadelauncher.he"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 6,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.grenadelauncher.buckshot"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 20,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.grenadelauncher.smoke"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 4,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rifle"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 900,
    ""amountMin"": 400,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""weapon.mod.small.scope"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 8,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""apple"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 65,
    ""amountMin"": 55,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""door.hinged.toptier"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""door.double.hinged.toptier"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.ak"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 8,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""autoturret"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""bandage"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 50,
    ""amountMin"": 30,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""grenade.beancan"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 44,
    ""amountMin"": 33,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""bed"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""black.raspberries"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 40,
    ""amountMin"": 20,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""blue.berry"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 15,
    ""probability"": 0.51,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""keycard_blue"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 4,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""blueberries"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 40,
    ""amountMin"": 20,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.bolt"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 6,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""shoes.boots"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 8,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""can.beans"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""can.tuna"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""cctv.camera"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 55,
    ""amountMin"": 30,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""chainsaw"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 9,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""charcoal"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 50000,
    ""amountMin"": 25000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""chocolate"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""cloth"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 35000,
    ""amountMin"": 15000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""computerstation"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""bearmeat.cooked"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""chicken.cooked"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""horsemeat.cooked"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""meat.pork.cooked"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""wolfmeat.cooked"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 150,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""corn"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 300,
    ""amountMin"": 120,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""crude.oil"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 4000,
    ""amountMin"": 2000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""smg.2"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 10,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""diesel_barrel"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 20,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""diving.fins"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""diving.mask"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""diving.tank"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 1,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""drone"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""fuse"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 122,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""propanetank"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 111,
    ""amountMin"": 55,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rifle.explosive"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 700,
    ""amountMin"": 400,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""explosives"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 150,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""furnace"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 10,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""wall.frame.garagedoor"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""gears"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 77,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""granolabar"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 11,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""green.berry"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 15,
    ""probability"": 0.51,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""keycard_green"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""gunpowder"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25000,
    ""amountMin"": 15000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""armor module slots"": {
      ""min"": 1,
      ""max"": 3
    },
    ""shortname"": ""hazmatsuit"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 13,
    ""amountMin"": 8,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""heavy.plate.helmet"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""heavy.plate.jacket"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""heavy.plate.pants"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""revolver.hc"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 4,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""gates.external.high.stone"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""wall.external.high.stone"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metal.refined"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 1200,
    ""amountMin"": 800,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metal.refined"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3200,
    ""amountMin"": 2000,
    ""probability"": 0.5,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""hq.metal.ore"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3000,
    ""amountMin"": 1000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rocket.hv"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rocket.seeker"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""homingmissile.launcher"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 1,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""hoodie"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 20,
    ""amountMin"": 12,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""ammo.rifle.hv"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 150,
    ""amountMin"": 70,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.pistol.hv"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 150,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rifle.incendiary"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 150,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.pistol.fire"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 150,
    ""amountMin"": 100,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rocket.fire"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""armor module slots"": {
      ""min"": 1,
      ""max"": 3
    },
    ""shortname"": ""jacket"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""jackhammer"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 9,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.l96"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""floor.ladder.hatch"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""largebackpack"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""furnace.large"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 8,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""largemedkit"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 40,
    ""amountMin"": 30,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""electric.battery.rechargable.large"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""electric.solarpanel.large"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 6,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""leather"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5000,
    ""amountMin"": 2000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""locker"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""lowgradefuel"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 10000,
    ""amountMin"": 6000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.lr300"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""lmg.m249"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.m39"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 7,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""pistol.m92"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 6,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""syringe.medical"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 100,
    ""amountMin"": 50,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""barricade.metal"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 8,
    ""amountMin"": 6,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metalblade"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 111,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""armor module slots"": {
      ""min"": 1,
      ""max"": 3
    },
    ""shortname"": ""metal.plate.torso"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 10,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""armor module slots"": {
      ""min"": 1,
      ""max"": 1
    },
    ""shortname"": ""metal.facemask"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 10,
    ""amountMin"": 6,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""metal.fragments"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 100000,
    ""amountMin"": 50000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metal.fragments"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 125000,
    ""amountMin"": 60000,
    ""probability"": 0.65,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metal.ore"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 80000,
    ""amountMin"": 42000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metalpipe"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 300,
    ""amountMin"": 222,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""metalspring"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 223,
    ""amountMin"": 111,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""minigun"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""mixingtable"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""aiming.module.mlrs"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rocket.mlrs"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""smg.mp5"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""multiplegrenadelauncher"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""mushroom"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 54,
    ""amountMin"": 15,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""nightvisiongoggles"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""pants"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 20,
    ""amountMin"": 12,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""parachute"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 1,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""ammo.pistol"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 900,
    ""amountMin"": 400,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""potato"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200,
    ""amountMin"": 120,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ptz.cctv.camera"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""pumpkin"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 300,
    ""amountMin"": 120,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""pistol.python"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 10,
    ""amountMin"": 7,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""red.berry"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 15,
    ""probability"": 0.51,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""keycard_red"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""wall.window.bars.toptier"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""riflebody"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 35,
    ""amountMin"": 20,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""armor module slots"": {
      ""min"": 1,
      ""max"": 3
    },
    ""shortname"": ""roadsign.jacket"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""armor module slots"": {
      ""min"": 1,
      ""max"": 3
    },
    ""shortname"": ""roadsign.kilt"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 11,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""roadsigns"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 225,
    ""amountMin"": 131,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""roadsign.gloves"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""ammo.rocket.basic"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 15,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rocket.launcher"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""rope"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 550,
    ""amountMin"": 350,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ammo.rocket.sam"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 90,
    ""amountMin"": 45,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""samsite"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""explosive.satchel"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 50,
    ""amountMin"": 40,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""scrap"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 8000,
    ""amountMin"": 4111,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""semibody"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 255,
    ""amountMin"": 115,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.semiauto"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 15,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""sewingkit"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 400,
    ""amountMin"": 300,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""sheetmetal"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 190,
    ""amountMin"": 123,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""guntrap"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""weapon.mod.silencer"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 4,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""rifle.sks"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 4,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""small.oil.refinery"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 4,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""smallwaterbottle"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""smgbody"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 35,
    ""amountMin"": 25,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""stones"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200000,
    ""amountMin"": 70000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""stones"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 200000,
    ""amountMin"": 80000,
    ""probability"": 0.85,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""sulfur"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 35000,
    ""amountMin"": 15000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""sulfur.ore"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 55000,
    ""amountMin"": 40000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""supply.signal"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 4,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""tactical.gloves"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 11,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""targeting.computer"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 55,
    ""amountMin"": 30,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""tarp"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 65,
    ""amountMin"": 40,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""techparts"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 100,
    ""amountMin"": 55,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""smg.thompson"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 10,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""explosive.timed"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 40,
    ""amountMin"": 25,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""cupboard.tool"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 1,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""floor.triangle.ladder.hatch"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 2,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""weapon.mod.8x.scope"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 7,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""vending.machine"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 3,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""waterjug"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""weapon.mod.flashlight"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 8,
    ""amountMin"": 5,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""weapon.mod.lasersight"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 4,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""white.berry"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 15,
    ""probability"": 0.51,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""generator.wind.scrap"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 6,
    ""amountMin"": 3,
    ""probability"": 1.0,
    ""stacksize"": 1
  },
  {
    ""shortname"": ""wood"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 250000,
    ""amountMin"": 100000,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""ladder.wooden.wall"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 12,
    ""amountMin"": 11,
    ""probability"": 1.0,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""techparts"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 5,
    ""amountMin"": 2,
    ""probability"": 0.6,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""targeting.computer"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 2,
    ""amountMin"": 1,
    ""probability"": 0.5,
    ""stacksize"": -1
  },
  {
    ""shortname"": ""yellow.berry"",
    ""name"": null,
    ""text"": null,
    ""blueprint"": false,
    ""skin"": 0,
    ""amount"": 25,
    ""amountMin"": 15,
    ""probability"": 0.51,
    ""stacksize"": -1
  }
]";

        #region Hooks / Config

        private void Init()
        {
            permission.RegisterPermission(PermPve, this);
            permission.RegisterPermission(PermPvp, this);
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermStart, this);
            permission.RegisterPermission(PermReady, this);

            EnsureNightshadeDataFolders();
            LoadData();
        }

        protected override void LoadDefaultConfig()
        {
            _config = PluginConfig.DefaultConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception("Config was null.");
                _config.Sanitize();
                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintWarning($"Config was invalid; generating default config. Error: {ex.Message}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void Unload()
        {
            StopCleanupCountdown(true);
            DestroyCleanupUiForAll();
            RestoreNormalTurretInterference();
            RemoveMapMarkers();
            RemoveUlpExemption(_activeRaidZoneId);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyCleanupUi(player);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            BuildingPrivlidge tc = entity as BuildingPrivlidge;
            if (tc == null || tc.net == null) return;
            if (!_raidStarted || _activeRaidTcId == 0UL) return;
            if (tc.net.ID.Value != _activeRaidTcId) return;

            StartCleanupCountdown("TC destroyed");
        }

        private void LoadData()
        {
            EnsureNightshadeDataFolders();

            string dataPath = GetPluginDataPath();

            try
            {
                if (File.Exists(dataPath))
                {
                    _data = JsonConvert.DeserializeObject<PluginData>(File.ReadAllText(dataPath)) ?? new PluginData();
                }
                else
                {
                    // One-time migration for older builds that stored data at the root data file location.
                    try
                    {
                        _data = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name) ?? new PluginData();
                    }
                    catch
                    {
                        _data = new PluginData();
                    }

                    SaveData();
                }

                if (_data.PlayerSaveNumbers == null) _data.PlayerSaveNumbers = new Dictionary<string, int>();
                if (_data.PreparedRaidBases == null) _data.PreparedRaidBases = new Dictionary<string, PreparedRaidBaseState>();
                RebuildPreparedRaidCache();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not load Nightshade raidbase data from '{dataPath}'. Starting with empty data. Error: {ex.Message}");
                _data = new PluginData();
            }
        }

        private void SaveData()
        {
            EnsureNightshadeDataFolders();

            string dataPath = GetPluginDataPath();

            try
            {
                File.WriteAllText(dataPath, JsonConvert.SerializeObject(_data ?? new PluginData(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not save Nightshade raidbase data to '{dataPath}': {ex.Message}");
            }
        }

        #endregion

        #region Commands


        [ChatCommand("raid")]
        private void CmdRaid(BasePlayer player, string command, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ShowRaidCommandHelp(player);
                return;
            }

            string subCommand = args[0].ToLowerInvariant();
            string[] rest = args.Skip(1).ToArray();

            switch (subCommand)
            {
                case "ready":
                case "prepare":
                    if (!HasPermission(player, PermReady))
                    {
                        Reply(player, "#ff6b6b", "You do not have permission to ready your raid base.");
                        return;
                    }

                    bool pvp = GetRequestedMode(rest, false);
                    PrepareRaidBase(player, pvp, StripModeArgs(rest));
                    return;

                case "start":
                    if (!HasPermission(player, PermStart))
                    {
                        Reply(player, "#ff6b6b", "You do not have permission to start a raid base.");
                        return;
                    }

                    StartRaidBase(player, rest);
                    return;

                case "end":
                case "stop":
                    if (!HasPermission(player, PermStart))
                    {
                        Reply(player, "#ff6b6b", "You do not have permission to end a raid base.");
                        return;
                    }

                    if (_config.RequireConfirmForManualRaidEnd && !HasArg(rest, "confirm"))
                    {
                        Reply(player, "#ff6b6b", "This will DELETE the tracked raidbase entities. Run <color=#ffffff>/raid end confirm</color> to continue, or use <color=#ffffff>/raid cancel</color> to stop the event without deleting the base.");
                        return;
                    }

                    EndRaidBase(player, "manual confirmed end command", false, true);
                    return;

                case "cancel":
                case "safeend":
                    if (!HasPermission(player, PermStart))
                    {
                        Reply(player, "#ff6b6b", "You do not have permission to cancel a raid base.");
                        return;
                    }

                    EndRaidBase(player, "manual cancel command - no entity cleanup", false, false);
                    return;

                case "reload":
                    if (!HasPermission(player, PermAdmin))
                    {
                        Reply(player, "#ff6b6b", "You do not have permission to reload raidbase loot tables.");
                        return;
                    }

                    _lootCache.Clear();
                    Reply(player, "#b084ff", "Raidbase loot cache cleared.");
                    return;

                default:
                    ShowRaidCommandHelp(player);
                    return;
            }
        }

        private void ShowRaidCommandHelp(BasePlayer player)
        {
            Reply(player, "#d9d2e9", "Commands: <color=#ffffff>/raid ready [pve|pvp] [loot profile]</color>, <color=#ffffff>/raid start [pve|pvp]</color>, <color=#ffffff>/raid end confirm</color>, <color=#ffffff>/raid cancel</color>, <color=#ffffff>/raid reload</color>.");
        }

        [ChatCommand("raidbaseready")]
        private void CmdRaidbaseReady(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, PermReady))
            {
                Reply(player, "#ff6b6b", "You do not have permission to ready your raid base.");
                return;
            }

            bool pvp = GetRequestedMode(args, false);
            PrepareRaidBase(player, pvp, StripModeArgs(args));
        }

        [ChatCommand("raidreadypve")]
        private void CmdRaidReadyPve(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, PermPve))
            {
                Reply(player, "#ff6b6b", "You do not have permission to ready a PvE raid base.");
                return;
            }

            PrepareRaidBase(player, false, StripModeArgs(args));
        }

        [ChatCommand("raidreadypvp")]
        private void CmdRaidReadyPvp(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, PermPvp))
            {
                Reply(player, "#ff6b6b", "You do not have permission to ready a PvP raid base.");
                return;
            }

            PrepareRaidBase(player, true, StripModeArgs(args));
        }


        [ChatCommand("raidbasestart")]
        private void CmdRaidbaseStart(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, PermStart))
            {
                Reply(player, "#ff6b6b", "You do not have permission to start a raid base.");
                return;
            }

            StartRaidBase(player, args);
        }

        [ChatCommand("raidbaseend")]
        private void CmdRaidbaseEnd(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, PermStart))
            {
                Reply(player, "#ff6b6b", "You do not have permission to end a raid base.");
                return;
            }

            if (_config.RequireConfirmForManualRaidEnd && !HasArg(args, "confirm"))
            {
                Reply(player, "#ff6b6b", "This will DELETE the tracked raidbase entities. Run <color=#ffffff>/raidbaseend confirm</color> to continue, or use <color=#ffffff>/raid cancel</color> to stop the event without deleting the base.");
                return;
            }

            EndRaidBase(player, "manual confirmed end command", false, true);
        }

        [ChatCommand("raidbasereload")]
        private void CmdRaidbaseReload(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, PermAdmin))
            {
                Reply(player, "#ff6b6b", "You do not have permission to reload raidbase loot tables.");
                return;
            }

            _lootCache.Clear();
            Reply(player, "#b084ff", "Raidbase loot cache cleared.");
        }

        #endregion

        #region Main Flow

        private void PrepareRaidBase(BasePlayer player, bool pvp, string[] args)
        {
            if (player == null) return;

            BuildingPrivlidge tc = player.GetBuildingPrivilege();
            if (tc == null)
            {
                Reply(player, "#ffcc66", "Stand inside the base TC privilege radius, then run the command again. No staring contest with the TC required.");
                return;
            }

            if (!_config.AllowUnownedCupboards && !tc.IsAuthed(player) && !HasPermission(player, PermAdmin))
            {
                Reply(player, "#ff6b6b", "You are not authorized on this TC.");
                return;
            }

            if (_pendingCopyByPlayer.ContainsKey(player.userID))
            {
                Reply(player, "#ffcc66", "Your raidbase copy is already running. Give CopyPaste a second to finish its paperwork goblin routine.");
                return;
            }

            string profileName = GetProfileName(args, pvp);
            List<LootEntry> loot = GetLootTable(profileName);
            if (loot == null || loot.Count == 0)
            {
                Reply(player, "#ff6b6b", $"No usable loot entries found for profile <color=#ffffff>{profileName}</color>.");
                Reply(player, "#bfbfbf", $"Expected path: <color=#ffffff>{GetLootFilePath(profileName)}</color>");
                return;
            }

            if (!BeginCopyThenPrepare(player, pvp, args, profileName))
                return;
        }

        private void PrepareRaidBaseAfterCopy(BasePlayer player, bool pvp, string[] args, string profileName, string copyName)
        {
            if (player == null || !player.IsConnected) return;

            BuildingPrivlidge tc = player.GetBuildingPrivilege();
            if (tc == null)
            {
                Reply(player, "#ffcc66", $"The base was copied as <color=#ffffff>{copyName}</color>, but I cannot prep it because you are no longer inside the TC privilege radius.");
                return;
            }

            if (!_config.AllowUnownedCupboards && !tc.IsAuthed(player) && !HasPermission(player, PermAdmin))
            {
                Reply(player, "#ff6b6b", $"The base was copied as <color=#ffffff>{copyName}</color>, but I cannot prep it because you are no longer authorized on this TC.");
                return;
            }

            List<LootEntry> loot = GetLootTable(profileName);
            if (loot == null || loot.Count == 0)
            {
                Reply(player, "#ff6b6b", $"The base was copied as <color=#ffffff>{copyName}</color>, but no usable loot entries were found for profile <color=#ffffff>{profileName}</color>.");
                return;
            }

            float radius = GetRadius(tc);
            string zoneId = BuildZoneId(tc, pvp);

            List<BaseEntity> entities = GetEntitiesInRadius(tc.transform.position, radius);

            FillResult fill = FillRaidBase(tc, entities, loot);
            FurnaceResult furnaces = SetupFurnaces(tc, entities, false);

            bool zoneOk = CreateOrUpdateZone(player, zoneId, tc.transform.position, radius, pvp);
            SavePreparedRaidBase(tc, player, pvp, zoneId, profileName, copyName, radius);

            Reply(player, "#b084ff", $"Raid base copied as <color=#ffffff>{copyName}</color> and readied as <color=#ffffff>{(pvp ? "PvP" : "PvE")}</color> using profile <color=#ffffff>{profileName}</color>.");
            Reply(player, "#d9d2e9", $"Loot: {fill.ContainersFilled}/{fill.ContainersFound} boxes touched, {fill.ItemsAdded} item stacks added. Furnaces: {furnaces.FurnacesTouched} filled/staged. Turrets/SAMs will activate when the raid starts.");
            Reply(player, "#bfbfbf", $"Zone: {(zoneOk ? "OK" : "check ZoneManager")}. Dome and TruePVE mapping will activate when the raid starts. Zone ID: <color=#ffffff>{zoneId}</color>");
        }

        private void StartRaidBase(BasePlayer player, string[] args)
        {
            if (player == null) return;

            if (_raidStarted)
            {
                Reply(player, "#ff6b6b", "A raid base is already active. Use <color=#ffffff>/raid end confirm</color> or <color=#ffffff>/raid cancel</color> first.");
                return;
            }

            BuildingPrivlidge tc = player.GetBuildingPrivilege();
            if (tc == null)
            {
                Reply(player, "#ffcc66", "Stand inside the raid base TC privilege radius, then run /raid start again.");
                return;
            }

            if (!_config.AllowUnownedCupboards && !tc.IsAuthed(player) && !HasPermission(player, PermAdmin))
            {
                Reply(player, "#ff6b6b", "You are not authorized on this TC.");
                return;
            }

            bool pvp = ResolveStartMode(tc, args);
            RemoveOppositeModeArtifacts(tc, pvp);
            string zoneId = BuildZoneId(tc, pvp);
            float radius = GetRadius(tc);
            string truePveRuleSet = pvp ? _config.TruePvePvpRuleSet : _config.TruePvePveRuleSet;
            List<BaseEntity> entities = GetEntitiesInRadius(tc.transform.position, radius);

            bool zoneCreatedOk = CreateOrUpdateZone(player, zoneId, tc.transform.position, radius, pvp);
            bool domeOk = AddOrUpdateDome(player, zoneId);
            bool truePveOk = AssignTruePveMapping(zoneId, truePveRuleSet);
            ReloadTruePve();
            ApplyRaidTurretInterference();

            FurnaceResult furnaces = SetupFurnaces(tc, entities, true);
            TurretResult turrets = EnableTurretsAndSams(tc, entities);
            bool zoneOk = RemoveRaidProtectionFlags(zoneId);

            TrackActiveRaidBase(tc, zoneId, truePveRuleSet, tc.transform.position, radius, pvp, entities);

            string ownerName = player != null && !string.IsNullOrWhiteSpace(player.displayName) ? player.displayName : "Unknown";
            SpawnMapMarkers(tc.transform.position, radius, pvp, ownerName);
            ApplyUlpExemption(zoneId, tc.transform.position, radius);

            Reply(player, "#b084ff", $"Raid base started. Dome and TruePVE mapping activated for <color=#ffffff>{zoneId}</color>.");
            Reply(player, "#d9d2e9", $"Furnaces lit: {furnaces.FurnacesTouched}. Turrets: {turrets.TurretsOn}. SAMs: {turrets.SamsOn}. Turret interference max: {_config.RaidStartTurretMaxInterference}. Zone update: {(zoneOk ? "OK" : "check ZoneManager")}.");
            Reply(player, "#bfbfbf", $"Zone: {(zoneCreatedOk ? "OK" : "check ZoneManager")}, Dome: {(domeOk ? "OK" : "check ZoneDomes")}, TruePVE: {(truePveOk ? truePveRuleSet : "check TruePVE mapping")}.");

            BroadcastRaidStart(player, pvp, zoneId, radius);
        }

        private void BroadcastRaidStart(BasePlayer owner, bool pvp, string zoneId, float radius)
        {
            if (!_config.BroadcastRaidStartToChat) return;

            string ownerName = owner != null && !string.IsNullOrWhiteSpace(owner.displayName) ? owner.displayName : "Unknown";
            string mode = pvp ? "PvP" : "PvE";
            string message = _config.RaidStartChatMessage;
            if (string.IsNullOrWhiteSpace(message))
                message = "<color=#b084ff>[Nightshade Raidbase]</color> <color=#d9d2e9>A player-built raidbase is now live! Owner: <color=#ffffff>{owner}</color> | Mode: <color=#ffffff>{mode}</color>. Defend the TC. Raid responsibly, goblins.</color>";

            message = message
                .Replace("{owner}", ownerName)
                .Replace("{mode}", mode)
                .Replace("{zone}", zoneId ?? string.Empty)
                .Replace("{radius}", radius.ToString("0", CultureInfo.InvariantCulture))
                .Replace("{grid}", GetGridFromPosition(_activeRaidCenter))
                .Replace("{cleanupMinutes}", _config.CleanupDelayAfterTcDestroyedMinutes.ToString(CultureInfo.InvariantCulture));

            PrintToChat(message);
        }

        #endregion

        #region CopyPaste Saving

        private bool BeginCopyThenPrepare(BasePlayer player, bool pvp, string[] args, string profileName)
        {
            if (player == null) return false;

            if (!_config.CopyRaidbaseOnReady)
            {
                PrepareRaidBaseAfterCopy(player, pvp, args, profileName, "copy disabled");
                return true;
            }

            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                Reply(player, "#ff6b6b", "CopyPaste is not loaded, so I cannot save this raidbase before preparing it.");
                return false;
            }

            string copyName = GetNextCopyPasteName(player);
            string[] copyArgs = BuildCopyPasteArgs();

            PendingCopyRequest request = new PendingCopyRequest
            {
                CopyName = copyName,
                UserId = player.userID,
                UserIdString = player.UserIDString,
                PlayerName = player.displayName,
                Pvp = pvp,
                Args = args ?? new string[0],
                ProfileName = profileName,
                PollAttempts = 0
            };

            _pendingCopyRequests[copyName] = request;
            _pendingCopyByPlayer[player.userID] = copyName;

            bool temporarilyGrantedCopy = false;
            bool alreadyHadCopy = permission.UserHasPermission(player.UserIDString, CopyPasteCopyPermission);

            // CopyPaste's documented TryCopyFromSteamId API returns null on some current builds/API wrappers.
            // Since the chat command is working on this server, use the real /copy command directly and poll for completion.
            try
            {
                if (_config.TemporarilyGrantCopyPasteCopyPermission && !alreadyHadCopy)
                {
                    permission.GrantUserPermission(player.UserIDString, CopyPasteCopyPermission, null);
                    temporarilyGrantedCopy = true;
                }

                RunCopyPasteCommandFallback(player, copyName, copyArgs);
                StartCopyPasteFallbackPoll(copyName);

                if (temporarilyGrantedCopy && !alreadyHadCopy)
                {
                    timer.Once(5f, () =>
                    {
                        try
                        {
                            if (permission.UserHasPermission(player.UserIDString, CopyPasteCopyPermission))
                                permission.RevokeUserPermission(player.UserIDString, CopyPasteCopyPermission);
                        }
                        catch { }
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                if (temporarilyGrantedCopy && !alreadyHadCopy)
                    permission.RevokeUserPermission(player.UserIDString, CopyPasteCopyPermission);

                FailPendingCopy(copyName, player, $"CopyPaste command fallback failed before the save could start for <color=#ffffff>{copyName}</color>: {ex.Message}");
                return false;
            }
        }

        private void RunCopyPasteCommandFallback(BasePlayer player, string copyName, string[] copyArgs)
        {
            if (player == null || !player.IsConnected) return;

            string argText = copyArgs != null && copyArgs.Length > 0 ? " " + string.Join(" ", copyArgs) : string.Empty;
            string command = $"copy {copyName}{argText}";

            // Use rust.RunClientCommand instead of chat.say to avoid broadcasting the /copy
            // command as a visible player chat message to everyone on the server.
            player.SendConsoleCommand("chat.say", $"/copy {copyName}{argText}");
            Reply(player, "#d9d2e9", $"Saving raidbase copy as <color=#ffffff>{copyName}</color>...");
        }

        private void StartCopyPasteFallbackPoll(string copyName)
        {
            timer.Once(2f, () => PollCopyPasteFallback(copyName));
        }

        private void PollCopyPasteFallback(string copyName)
        {
            PendingCopyRequest request;
            if (!_pendingCopyRequests.TryGetValue(copyName, out request)) return;

            if (CopyPasteFileExists(copyName))
            {
                CompletePendingCopy(copyName);
                return;
            }

            request.PollAttempts++;
            if (request.PollAttempts >= 30)
            {
                BasePlayer player = BasePlayer.FindByID(request.UserId) ?? BasePlayer.FindSleeping(request.UserId);
                FailPendingCopy(copyName, player, $"CopyPaste fallback did not create <color=#ffffff>{copyName}.json</color> after about 60 seconds. Check CopyPaste errors and make sure /copy works manually.");
                return;
            }

            timer.Once(2f, () => PollCopyPasteFallback(copyName));
        }

        private void OnCopyFinished(List<object> rawData, string filename, IPlayer player, Vector3 startPos)
        {
            if (string.IsNullOrWhiteSpace(filename)) return;
            CompletePendingCopy(filename);
        }

        private void CompletePendingCopy(string copyName)
        {
            PendingCopyRequest request;
            if (!_pendingCopyRequests.TryGetValue(copyName, out request)) return;

            _pendingCopyRequests.Remove(copyName);
            _pendingCopyByPlayer.Remove(request.UserId);
            CommitNextCopyNumber(request.UserIdString);

            BasePlayer player = BasePlayer.FindByID(request.UserId) ?? BasePlayer.FindSleeping(request.UserId);
            ArchiveSavedRaidbaseCopy(copyName, player, request.PlayerName);
            if (player == null || !player.IsConnected)
            {
                Puts($"Raidbase copy '{copyName}' finished for {request.PlayerName}, but the player is no longer online. Copy saved; live prep skipped.");
                return;
            }

            timer.Once(0.25f, () => PrepareRaidBaseAfterCopy(player, request.Pvp, request.Args, request.ProfileName, copyName));
        }

        private void FailPendingCopy(string copyName, BasePlayer player, string message)
        {
            PendingCopyRequest request;
            if (_pendingCopyRequests.TryGetValue(copyName, out request))
            {
                _pendingCopyRequests.Remove(copyName);
                _pendingCopyByPlayer.Remove(request.UserId);
                _pendingCopyNumbers.Remove(request.UserIdString);
            }

            if (player != null && player.IsConnected)
                Reply(player, "#ff6b6b", message);
            else
                PrintWarning(message);
        }

        // SaveRaidbaseCopy was removed — it was dead code superseded by BeginCopyThenPrepare,
        // which uses the chat.say fallback poll approach instead of the synchronous TryCopyFromSteamId API.

        private string[] BuildCopyPasteArgs()
        {
            List<string> args = new List<string>();

            // CopyPaste copy syntax is: /copy NAME radius 3 method building share true tree false
            // Do not pass paste-only options such as inventories/deployables here.
            if (_config.CopyPasteRadius > 0f)
            {
                args.Add("radius");
                args.Add(_config.CopyPasteRadius.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(_config.CopyPasteMethod))
            {
                args.Add("method");
                args.Add(_config.CopyPasteMethod.Trim().ToLowerInvariant());
            }

            args.Add("share");
            args.Add(_config.CopyPasteShareData.ToString().ToLowerInvariant());

            args.Add("tree");
            args.Add(_config.CopyPasteCopyTreesAndResources.ToString().ToLowerInvariant());

            return args.ToArray();
        }

        private string GetNextCopyPasteName(BasePlayer player)
        {
            string cleanPlayerName = SanitizeCopyPasteName(player?.displayName ?? "player");
            if (string.IsNullOrWhiteSpace(cleanPlayerName)) cleanPlayerName = "player";

            string userId = player?.UserIDString ?? "0";
            int number = GetStoredNextCopyNumber(userId);
            int guard = 0;

            while (guard++ < 10000)
            {
                string numberText = _config.CopyPasteNumberPadding > 0
                    ? number.ToString(new string('0', _config.CopyPasteNumberPadding), CultureInfo.InvariantCulture)
                    : number.ToString(CultureInfo.InvariantCulture);

                string candidate = _config.CopyPasteFilenamePattern
                    .Replace("{player}", cleanPlayerName)
                    .Replace("{number}", numberText)
                    .Replace("{steamid}", userId);

                candidate = SanitizeCopyPasteName(candidate);
                if (!CopyPasteFileExists(candidate))
                {
                    _pendingCopyNumbers[userId] = number;
                    return candidate;
                }

                number++;
            }

            _pendingCopyNumbers[userId] = number;
            return $"{cleanPlayerName}_{UnityEngine.Random.Range(100000, 999999)}";
        }

        private int GetStoredNextCopyNumber(string userId)
        {
            if (_data == null) LoadData();
            if (_data.PlayerSaveNumbers == null) _data.PlayerSaveNumbers = new Dictionary<string, int>();

            int number;
            if (!_data.PlayerSaveNumbers.TryGetValue(userId, out number) || number < _config.CopyPasteStartingNumber)
                number = _config.CopyPasteStartingNumber;

            return Math.Max(1, number);
        }

        private void CommitNextCopyNumber(string userId)
        {
            if (_data == null) LoadData();
            if (_data.PlayerSaveNumbers == null) _data.PlayerSaveNumbers = new Dictionary<string, int>();

            int current;
            if (!_pendingCopyNumbers.TryGetValue(userId, out current))
                current = GetStoredNextCopyNumber(userId);

            _data.PlayerSaveNumbers[userId] = current + 1;
            _pendingCopyNumbers.Remove(userId);
            SaveData();
        }

        private bool CopyPasteFileExists(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return false;

            string basePath = GetDataSubDirectory(_config.CopyPasteDataFolderUnderOxideData, "copypaste");
            string jsonPath = Path.Combine(basePath, filename + ".json");
            string plainPath = Path.Combine(basePath, filename);

            return File.Exists(jsonPath) || File.Exists(plainPath);
        }

        private void EnsureNightshadeDataFolders()
        {
            if (_config == null) return;

            try
            {
                Directory.CreateDirectory(GetDataSubDirectory(_config.NightshadeRaidbaseDataRootFolder, "NightshadeRaidbases"));
                Directory.CreateDirectory(GetDataSubDirectory(_config.RaidableBasesLootFolder, "NightshadeRaidbases/Loot Tables"));
                Directory.CreateDirectory(GetDataSubDirectory(_config.SavedRaidbaseArchiveFolderUnderOxideData, "NightshadeRaidbases/Player Raidbases"));

                if (_config.CreateDefaultLootTableIfMissing)
                    EnsureDefaultLootTableFile();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not create Nightshade raidbase data folders: {ex.Message}");
            }
        }

        private string GetPluginDataPath()
        {
            string dataFolder = GetDataSubDirectory(_config?.NightshadeRaidbaseDataRootFolder, "NightshadeRaidbases");
            string fileName = _config?.PluginStateDataFileName;

            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Nightshade_Raidbase_Data.json";
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";

            return Path.Combine(dataFolder, fileName);
        }

        private void EnsureDefaultLootTableFile()
        {
            string lootPath = GetLootFilePath(_config.DefaultPveLootProfile);

            if (File.Exists(lootPath)) return;

            try
            {
                string directory = Path.GetDirectoryName(lootPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(lootPath, DefaultRaidbaseLootJson);
                Puts($"Created default raidbase loot table at: {lootPath}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not create default raidbase loot table at '{lootPath}': {ex.Message}");
            }
        }

        private string GetDataSubDirectory(string relativePath, string fallback)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) relativePath = fallback;
            if (string.IsNullOrWhiteSpace(relativePath)) relativePath = string.Empty;

            string cleaned = relativePath.Trim()
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);

            return Path.Combine(Interface.Oxide.DataDirectory, cleaned);
        }

        private void ArchiveSavedRaidbaseCopy(string copyName, BasePlayer player, string playerName)
        {
            if (!_config.ArchiveSavedRaidbaseCopy) return;
            if (string.IsNullOrWhiteSpace(copyName)) return;

            string sourcePath = GetCopyPasteSavePath(copyName);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                string msg = $"Could not archive raidbase copy '{copyName}' because the CopyPaste source file was not found.";
                if (player != null && player.IsConnected) Reply(player, "#ffcc66", msg);
                else PrintWarning(msg);
                return;
            }

            string archiveFolder = _config.SavedRaidbaseArchiveFolderUnderOxideData;
            if (string.IsNullOrWhiteSpace(archiveFolder)) archiveFolder = "NightshadeRaidbases/Player Raidbases";

            string destinationDir = GetDataSubDirectory(archiveFolder, "NightshadeRaidbases/Player Raidbases");
            string destinationPath = Path.Combine(destinationDir, Path.GetFileName(sourcePath));

            try
            {
                Directory.CreateDirectory(destinationDir);

                if (_config.KeepOriginalCopyPasteSaveAfterArchive)
                {
                    File.Copy(sourcePath, destinationPath, true);
                    if (player != null && player.IsConnected)
                        Reply(player, "#d9d2e9", $"Archived raidbase copy to <color=#ffffff>data folder/{archiveFolder}/{Path.GetFileName(sourcePath)}</color>.");
                }
                else
                {
                    if (File.Exists(destinationPath)) File.Delete(destinationPath);
                    File.Move(sourcePath, destinationPath);
                    if (player != null && player.IsConnected)
                        Reply(player, "#d9d2e9", $"Moved raidbase copy to <color=#ffffff>data folder/{archiveFolder}/{Path.GetFileName(sourcePath)}</color>.");
                }
            }
            catch (Exception ex)
            {
                string msg = $"Failed to archive raidbase copy '{copyName}' into data folder/{archiveFolder}: {ex.Message}";
                if (player != null && player.IsConnected) Reply(player, "#ffcc66", msg);
                else PrintWarning(msg);
            }
        }

        private string GetCopyPasteSavePath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return string.Empty;

            string basePath = GetDataSubDirectory(_config.CopyPasteDataFolderUnderOxideData, "copypaste");
            string jsonPath = Path.Combine(basePath, filename + ".json");
            if (File.Exists(jsonPath)) return jsonPath;

            string plainPath = Path.Combine(basePath, filename);
            if (File.Exists(plainPath)) return plainPath;

            return string.Empty;
        }

        private string SanitizeCopyPasteName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            List<char> chars = new List<char>();
            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars.Add(char.ToLowerInvariant(c));
                }
                else if (c == '_' || c == '-' || c == ' ')
                {
                    if (chars.Count == 0 || chars[chars.Count - 1] != '_')
                        chars.Add('_');
                }
            }

            string cleaned = new string(chars.ToArray()).Trim('_');
            while (cleaned.Contains("__")) cleaned = cleaned.Replace("__", "_");

            if (_config.CopyPasteMaxFilenameLength > 0 && cleaned.Length > _config.CopyPasteMaxFilenameLength)
                cleaned = cleaned.Substring(0, _config.CopyPasteMaxFilenameLength).Trim('_');

            return cleaned;
        }

        private void SavePreparedRaidBase(BuildingPrivlidge tc, BasePlayer owner, bool pvp, string zoneId, string profileName, string copyName, float radius)
        {
            if (tc == null || tc.net == null) return;

            PreparedRaidBaseState state = new PreparedRaidBaseState
            {
                TcId = tc.net.ID.Value,
                OwnerId = owner != null ? owner.UserIDString : string.Empty,
                OwnerName = owner != null ? owner.displayName : string.Empty,
                Pvp = pvp,
                ZoneId = zoneId ?? string.Empty,
                ProfileName = profileName ?? string.Empty,
                CopyName = copyName ?? string.Empty,
                Radius = radius
            };

            _preparedRaidBases[state.TcId] = state;
            if (_data == null) LoadData();
            if (_data.PreparedRaidBases == null) _data.PreparedRaidBases = new Dictionary<string, PreparedRaidBaseState>();
            _data.PreparedRaidBases[state.TcId.ToString()] = state;
            SaveData();
        }

        private void RemovePreparedRaidBase(ulong tcId)
        {
            if (tcId == 0UL) return;
            _preparedRaidBases.Remove(tcId);
            if (_data != null && _data.PreparedRaidBases != null)
            {
                _data.PreparedRaidBases.Remove(tcId.ToString());
                SaveData();
            }
        }

        private void RebuildPreparedRaidCache()
        {
            _preparedRaidBases.Clear();
            if (_data == null || _data.PreparedRaidBases == null) return;

            foreach (PreparedRaidBaseState state in _data.PreparedRaidBases.Values)
            {
                if (state == null || state.TcId == 0UL) continue;
                _preparedRaidBases[state.TcId] = state;
            }
        }

        #endregion

        #region Raid Lifecycle / Cleanup

        private void TrackActiveRaidBase(BuildingPrivlidge tc, string zoneId, string ruleSet, Vector3 center, float radius, bool pvp, List<BaseEntity> entities)
        {
            if (tc == null || tc.net == null) return;

            StopCleanupCountdown(true);
            DestroyCleanupUiForAll();

            _activeRaidTcId = tc.net.ID.Value;
            _activeRaidZoneId = zoneId ?? string.Empty;
            _activeRaidRuleSet = ruleSet ?? string.Empty;
            _activeRaidCenter = center;
            _activeRaidRadius = radius;
            _activeRaidPvp = pvp;
            _raidStarted = true;
            _cleanupCountdownActive = false;

            SnapshotRaidEntities(tc, entities);
        }

        private void SnapshotRaidEntities(BuildingPrivlidge tc, List<BaseEntity> entities)
        {
            _activeRaidEntityIds.Clear();
            if (entities == null || tc == null || tc.net == null) return;

            // SAFETY: cleanup snapshots must always be tied to the raidbase TC,
            // regardless of the general RequireSameBuildingPrivilege setting.
            // This prevents nearby/overlapping real bases inside a large dome from
            // being captured and deleted during raid cleanup.
            foreach (BaseEntity entity in entities)
            {
                if (entity == null || entity.IsDestroyed || entity.net == null) continue;
                if (!IsEntityOwnedByRaidTc(entity, tc)) continue;
                if (!IsCleanupEligibleEntity(entity, true)) continue;

                _activeRaidEntityIds.Add(entity.net.ID.Value);
            }

            // Explicitly include the TC itself. IsEntityOwnedByRaidTc already handles TC-as-entity above,
            // so this is a defensive guarantee for the case where it wasn't in the entity scan radius.
            _activeRaidEntityIds.Add(tc.net.ID.Value);

            Puts($"Snapshot saved {_activeRaidEntityIds.Count} raidbase entities for cleanup. Cleanup is locked to TC {tc.net.ID.Value}.");
        }

        private void StartCleanupCountdown(string reason)
        {
            if (_cleanupCountdownActive) return;

            float seconds = Mathf.Max(1f, _config.CleanupDelayAfterTcDestroyedMinutes * 60f);
            _cleanupCountdownActive = true;
            _cleanupEndRealtime = Time.realtimeSinceStartup + seconds;

            Puts($"Raidbase TC destroyed. Cleanup will run in {_config.CleanupDelayAfterTcDestroyedMinutes} minutes. Reason: {reason}");
            PrintToChat($"<color=#b084ff>[Nightshade Raidbase]</color> <color=#d9d2e9>The TC has fallen. Cleanup begins in <color=#ffffff>{_config.CleanupDelayAfterTcDestroyedMinutes} minutes</color>.</color>");

            UpdateCleanupUiForEligiblePlayers();
            _cleanupUiTimer = timer.Every(1f, UpdateCleanupUiForEligiblePlayers);
            _cleanupEndTimer = timer.Once(seconds, () => EndRaidBase(null, "TC destroyed cleanup countdown expired", true));
        }

        private void StopCleanupCountdown(bool destroyUi)
        {
            if (_cleanupEndTimer != null && !_cleanupEndTimer.Destroyed)
                _cleanupEndTimer.Destroy();
            if (_cleanupUiTimer != null && !_cleanupUiTimer.Destroyed)
                _cleanupUiTimer.Destroy();

            _cleanupEndTimer = null;
            _cleanupUiTimer = null;
            _cleanupCountdownActive = false;
            _cleanupEndRealtime = 0f;

            if (destroyUi)
                DestroyCleanupUiForAll();
        }

        private void EndRaidBase(BasePlayer player, string reason, bool automatic, bool deleteEntities = true)
        {
            if (!_raidStarted && _activeRaidTcId == 0UL)
            {
                if (player != null)
                    Reply(player, "#ffcc66", "No raid base is currently active.");
                return;
            }

            StopCleanupCountdown(true);
            RemoveMapMarkers();

            CleanupResult cleanup = deleteEntities ? CleanupRaidBaseEntities() : new CleanupResult();
            RestoreNormalTurretInterference();

            bool domeRemoved = RemoveRaidDome(_activeRaidZoneId);
            bool mappingRemoved = RemoveTruePveMapping(_activeRaidZoneId);
            bool zoneRemoved = !_config.RemoveZoneManagerZoneOnRaidEnd || RemoveZoneManagerZone(_activeRaidZoneId);
            RemoveUlpExemption(_activeRaidZoneId);

            if (mappingRemoved)
                ReloadTruePve();

            string zoneId = string.IsNullOrWhiteSpace(_activeRaidZoneId) ? "unknown" : _activeRaidZoneId;
            Puts($"Raidbase ended. Zone={zoneId}, reason={reason}, automatic={automatic}, deleteEntities={deleteEntities}, removed={cleanup.EntitiesRemoved}, failed={cleanup.EntitiesFailed}, domeRemoved={domeRemoved}, mappingRemoved={mappingRemoved}, zoneRemoved={zoneRemoved}");

            string message = deleteEntities
                ? $"Raidbase cleanup complete. Removed <color=#ffffff>{cleanup.EntitiesRemoved}</color> entities. Turret interference restored."
                : "Raidbase event cancelled safely. Dome/mapping removed, turret interference restored, and no raidbase entities were deleted.";
            if (player != null)
                Reply(player, "#b084ff", message);
            else
                PrintToChat($"<color=#b084ff>[Nightshade Raidbase]</color> <color=#d9d2e9>{message}</color>");

            RemovePreparedRaidBase(_activeRaidTcId);
            ClearActiveRaidBaseState();
        }

        private CleanupResult CleanupRaidBaseEntities()
        {
            CleanupResult result = new CleanupResult();

            if (!_config.RemoveRaidbaseEntitiesOnCleanup)
                return result;

            float radius = _activeRaidRadius + Mathf.Max(0f, _config.CleanupEntityExtraRadius);
            if (radius <= 0f || _activeRaidCenter == Vector3.zero)
                return result;

            List<BaseEntity> entities = GetEntitiesInRadius(_activeRaidCenter, radius);
            foreach (BaseEntity entity in entities)
            {
                if (entity == null || entity.IsDestroyed || entity.net == null) continue;
                if (!ShouldCleanupEntity(entity)) continue;

                try
                {
                    entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    result.EntitiesRemoved++;
                }
                catch (Exception ex)
                {
                    result.EntitiesFailed++;
                    PrintWarning($"Could not remove raidbase entity {entity.ShortPrefabName} ({entity.net.ID.Value}): {ex.Message}");
                }
            }

            return result;
        }

        private bool ShouldCleanupEntity(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return false;
            if (entity is BasePlayer) return false;
            if (entity is NPCPlayer) return false;

            ulong id = entity.net?.ID.Value ?? 0UL;
            if (id != 0UL && _activeRaidEntityIds.Contains(id))
                return true;

            // Do not delete random eligible entities inside the dome/radius by default.
            // Large raid zones can overlap normal player bases, so cleanup should be
            // snapshot-only unless this risky option is intentionally enabled.
            if (!_config.CleanupEligibleEntitiesInRaidRadius)
                return false;

            // Even when the risky radius sweep is enabled, never remove structures or
            // deployables that still belong to another TC. Only loose dropped loot can
            // be swept by radius.
            if (entity is DroppedItem || entity is DroppedItemContainer)
                return IsCleanupEligibleEntity(entity, false);

            return false;
        }

        private bool IsCleanupEligibleEntity(BaseEntity entity, bool snapshotMode)
        {
            if (entity == null || entity.IsDestroyed) return false;
            if (entity is BasePlayer) return false;
            if (entity is NPCPlayer) return false;

            string prefab = (entity.PrefabName ?? string.Empty).ToLowerInvariant();
            string shortPrefab = (entity.ShortPrefabName ?? string.Empty).ToLowerInvariant();

            if (ContainsAny(prefab, _config.CleanupPrefabBlacklistContains) || ContainsAny(shortPrefab, _config.CleanupPrefabBlacklistContains))
                return false;

            if (entity is BuildingBlock) return true;
            if (entity is BuildingPrivlidge) return true;
            if (entity is AutoTurret) return true;
            if (entity is SamSite) return true;
            if (entity is StorageContainer) return true;
            if (entity is BaseOven) return true;
            if (entity is DroppedItem) return !snapshotMode;
            if (entity is DroppedItemContainer) return !snapshotMode;

            if (ContainsAny(prefab, _config.CleanupPrefabWhitelistContains) || ContainsAny(shortPrefab, _config.CleanupPrefabWhitelistContains))
                return true;

            // Most player deployables/traps/doors are DecayEntity-derived. This avoids monuments and players/NPCs above.
            if (entity is DecayEntity) return true;

            return false;
        }

        private void ClearActiveRaidBaseState()
        {
            _activeRaidTcId = 0UL;
            _activeRaidZoneId = string.Empty;
            _activeRaidRuleSet = string.Empty;
            _activeRaidCenter = Vector3.zero;
            _activeRaidRadius = 0f;
            _activeRaidPvp = false;
            _raidStarted = false;
            _activeRaidEntityIds.Clear();
            _activeRaidMapPinId = 0UL;
            _activeRaidMapCircleId = 0UL;
        }

        #endregion

        #region Cleanup Timer UI

        private void UpdateCleanupUiForEligiblePlayers()
        {
            if (!_cleanupCountdownActive)
            {
                DestroyCleanupUiForAll();
                return;
            }

            float remaining = Mathf.Max(0f, _cleanupEndRealtime - Time.realtimeSinceStartup);
            string text = string.Format(_config.CleanupTimerText, FormatDuration(remaining));

            HashSet<ulong> visibleThisTick = new HashSet<ulong>();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;

                if (!ShouldShowCleanupUi(player))
                {
                    DestroyCleanupUi(player);
                    continue;
                }

                visibleThisTick.Add(player.userID);

                // AdvancedStatus has its own time/progress rendering, so only create the bar once per player/entry.
                // The old CUI fallback is refreshed every tick because it renders static text.
                if (!_playersWithCleanupUi.Contains(player.userID) || !UseAdvancedStatusCleanupBar())
                    DrawCleanupUi(player, text, remaining);
            }

            List<ulong> stalePlayers = _playersWithCleanupUi.Where(id => !visibleThisTick.Contains(id)).ToList();
            foreach (ulong userId in stalePlayers)
            {
                BasePlayer stalePlayer = BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
                if (stalePlayer != null)
                    DestroyCleanupUi(stalePlayer);
                else
                    _playersWithCleanupUi.Remove(userId);
            }
        }

        private void DrawCleanupUi(BasePlayer player, string text, float remaining)
        {
            if (player == null) return;

            if (UseAdvancedStatusCleanupBar())
            {
                DrawCleanupStatusBar(player, remaining);
                return;
            }

            DrawCleanupCuiFallback(player, text);
        }

        private bool UseAdvancedStatusCleanupBar()
        {
            return _config.UseAdvancedStatusCleanupBar && AdvancedStatus != null && AdvancedStatus.IsLoaded && AdvancedStatus.Call("IsReady") != null;
        }

        private void DrawCleanupStatusBar(BasePlayer player, float remaining)
        {
            if (player == null || player.userID == 0UL) return;

            double now = Network.TimeEx.currentTimestamp;
            double duration = Math.Max(1d, remaining);
            string label = _config.AdvancedStatusCleanupText;

            AdvancedStatus?.Call(AdvancedStatusCreateBar, player.userID, new Dictionary<int, object>
            {
                { 0, CleanupStatusBarId },
                { 1, Name },
                { 2, "TimeProgressCounter" },
                { 4, _config.AdvancedStatusCleanupOrder },
                { 5, _config.AdvancedStatusCleanupHeight },
                { 6, _config.AdvancedStatusCleanupMainColor },
                { -6, _config.AdvancedStatusCleanupMainTransparency },
                { 10, _config.AdvancedStatusCleanupImageSprite },
                { 12, _config.AdvancedStatusCleanupImageColor },
                { -12, _config.AdvancedStatusCleanupImageTransparency },
                { 15, label },
                { 16, _config.AdvancedStatusCleanupTextSize },
                { 17, _config.AdvancedStatusCleanupTextColor },
                { 18, _config.AdvancedStatusCleanupTextFont },
                { 23, _config.AdvancedStatusCleanupSubTextSize },
                { 24, _config.AdvancedStatusCleanupSubTextColor },
                { 25, _config.AdvancedStatusCleanupSubTextFont },
                { 32, _config.AdvancedStatusCleanupProgressReverse },
                { 33, _config.AdvancedStatusCleanupProgressColor },
                { -33, _config.AdvancedStatusCleanupProgressTransparency },
                { 34, _config.AdvancedStatusCleanupProgressOffsetMin },
                { 35, _config.AdvancedStatusCleanupProgressOffsetMax },
                { 28, now },
                { 29, now + duration }
            });

            _playersWithCleanupUi.Add(player.userID);
        }

        private void DrawCleanupCuiFallback(BasePlayer player, string text)
        {
            if (player == null) return;

            CuiHelper.DestroyUi(player, CleanupUiName);

            CuiElementContainer container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = _config.CleanupTimerBackgroundColor },
                RectTransform =
                {
                    AnchorMin = _config.CleanupTimerAnchorMin,
                    AnchorMax = _config.CleanupTimerAnchorMax
                },
                CursorEnabled = false
            }, "Hud", CleanupUiName);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = text,
                    FontSize = _config.CleanupTimerFontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = _config.CleanupTimerTextColor
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                }
            }, CleanupUiName);

            CuiHelper.AddUi(player, container);
            if (player.userID != 0UL)
                _playersWithCleanupUi.Add(player.userID);
        }

        private bool ShouldShowCleanupUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return false;
            if (!_cleanupCountdownActive) return false;
            if (!_config.ShowCleanupTimerOnlyNearRaidbase || _activeRaidRadius <= 0f) return true;

            float allowedDistance = _activeRaidRadius;
            if (!_config.RemoveCleanupTimerWhenPlayerLeavesRaidZone)
                allowedDistance += Mathf.Max(0f, _config.CleanupTimerExtraShowRadius);

            return Vector3.Distance(player.transform.position, _activeRaidCenter) <= allowedDistance;
        }

        private void DestroyCleanupUi(BasePlayer player)
        {
            if (player == null) return;

            CuiHelper.DestroyUi(player, CleanupUiName);

            if (player.userID != 0UL)
            {
                AdvancedStatus?.Call(AdvancedStatusDeleteAllBars, player.userID, Name);
                _playersWithCleanupUi.Remove(player.userID);
            }
        }

        private void DestroyCleanupUiForAll()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DestroyCleanupUi(player);

            _playersWithCleanupUi.Clear();
        }

        private string FormatDuration(float seconds)
        {
            int totalSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            return $"{minutes:00}:{secs:00}";
        }

        #endregion

        #region Entity Collection

        private List<BaseEntity> GetEntitiesInRadius(Vector3 center, float radius)
        {
            List<BaseEntity> entities = new List<BaseEntity>();
            Vis.Entities(center, radius, entities, _config.EntityLayerMask, QueryTriggerInteraction.Ignore);

            Dictionary<ulong, BaseEntity> deduped = new Dictionary<ulong, BaseEntity>();
            foreach (BaseEntity entity in entities)
            {
                if (entity == null || entity.IsDestroyed || entity.net == null) continue;
                deduped[entity.net.ID.Value] = entity;
            }

            return deduped.Values.ToList();
        }

        private float GetRadius(BuildingPrivlidge tc)
        {
            return _config.TcRadiusOverride > 0f ? _config.TcRadiusOverride : _config.DefaultTcRadius;
        }

        #endregion

        #region Loot Filling

        private FillResult FillRaidBase(BuildingPrivlidge tc, List<BaseEntity> entities, List<LootEntry> loot)
        {
            FillResult result = new FillResult();

            foreach (BaseEntity entity in entities)
            {
                StorageContainer container = entity as StorageContainer;
                if (container == null || container.inventory == null || container.IsDestroyed) continue;
                if (!IsUsableLootBox(container, tc)) continue;

                result.ContainersFound++;

                List<LootEntry> targetLoot = GetLootForContainer(container, loot);
                int added = FillContainerAtLeastHalf(container, targetLoot);
                if (added > 0)
                {
                    result.ContainersFilled++;
                    result.ItemsAdded += added;
                }
            }

            return result;
        }

        private bool IsUsableLootBox(StorageContainer container, BuildingPrivlidge tc)
        {
            if (container == null || container.inventory == null) return false;
            if (container is BuildingPrivlidge) return false;

            string prefab = (container.PrefabName ?? string.Empty).ToLowerInvariant();

            if (ContainsAny(prefab, _config.BlacklistedLootContainerPrefabContains)) return false;

            if (_config.WhitelistedLootContainerPrefabContains != null && _config.WhitelistedLootContainerPrefabContains.Count > 0)
            {
                if (!ContainsAny(prefab, _config.WhitelistedLootContainerPrefabContains)) return false;
            }

            if (_config.RequireSameBuildingPrivilege)
            {
                BuildingPrivlidge containerTc = container.GetBuildingPrivilege();
                if (containerTc == null || containerTc.net == null || tc == null || tc.net == null || containerTc.net.ID != tc.net.ID)
                    return false;
            }

            return true;
        }


        private List<LootEntry> GetLootForContainer(StorageContainer container, List<LootEntry> allLoot)
        {
            if (container == null || allLoot == null) return allLoot;

            string prefab = (container.PrefabName ?? string.Empty).ToLowerInvariant();
            bool isFridge = prefab.Contains("fridge");

            if (!isFridge)
                return allLoot.Where(entry => !IsFoodLoot(entry)).ToList();

            List<LootEntry> foodLoot = allLoot.Where(IsFoodLoot).ToList();
            return foodLoot.Count > 0 ? foodLoot : allLoot;
        }

        private bool IsFoodLoot(LootEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.ShortName)) return false;

            ItemDefinition def = ItemManager.FindItemDefinition(entry.ShortName);
            return def != null && def.category == ItemCategory.Food;
        }

        private int FillContainerAtLeastHalf(StorageContainer container, List<LootEntry> loot)
        {
            ItemContainer inv = container.inventory;
            if (inv == null || loot == null || loot.Count == 0) return 0;

            int capacity = Math.Max(1, inv.capacity);
            int targetSlots = Mathf.Clamp(Mathf.CeilToInt(capacity * _config.MinimumBoxFillPercent), 1, capacity);

            // Existing loot is NEVER cleared. This fills over top of what is already there.
            if (inv.itemList.Count >= targetSlots && !_config.TopOffAlreadyHalfFullBoxes)
                return 0;

            ItemContainer.ContentsType originalContentsType = inv.allowedContents;
            inv.allowedContents = ItemContainer.ContentsType.Generic;
            inv.SetFlag(ItemContainer.Flag.IsLocked, false);
            inv.SetFlag(ItemContainer.Flag.NoItemInput, false);

            int added = 0;
            int attempts = 0;
            int maxAttempts = capacity * 20;
            int consecutiveFails = 0;

            while (inv.itemList.Count < targetSlots && attempts++ < maxAttempts && consecutiveFails < 30)
            {
                LootEntry entry = PickLoot(loot);
                if (entry == null)
                {
                    consecutiveFails++;
                    continue;
                }

                if (entry.Chance < 100f && UnityEngine.Random.Range(0f, 100f) > entry.Chance)
                    continue;

                ItemDefinition def = ItemManager.FindItemDefinition(entry.ShortName);
                if (def == null)
                {
                    if (_config.LogInvalidItems) PrintWarning($"Invalid item shortname in loot table: {entry.ShortName}");
                    consecutiveFails++;
                    continue;
                }

                int amount = entry.RollAmount(_rng, _config.DefaultAmountMultiplier);
                Item item = ItemManager.Create(def, amount, entry.SkinId);
                if (item == null)
                {
                    consecutiveFails++;
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.CustomName))
                    item.name = entry.CustomName;

                bool placed = false;

                if (_config.AllowStackingIntoExistingSlots)
                    placed = item.MoveToContainer(inv, -1, true);

                if (!placed)
                {
                    for (int slot = 0; slot < inv.capacity; slot++)
                    {
                        if (inv.GetSlot(slot) == null)
                        {
                            placed = item.MoveToContainer(inv, slot, false);
                            if (placed) break;
                        }
                    }
                }

                if (!placed)
                {
                    item.Remove();
                    consecutiveFails++;
                }
                else
                {
                    added++;
                    consecutiveFails = 0;
                }
            }

            inv.allowedContents = originalContentsType;
            container.SendNetworkUpdateImmediate();

            return added;
        }

        #endregion

        #region Furnaces

        private FurnaceResult SetupFurnaces(BuildingPrivlidge tc, List<BaseEntity> entities, bool startRaid)
        {
            FurnaceResult result = new FurnaceResult();

            foreach (BaseEntity entity in entities)
            {
                BaseOven oven = entity as BaseOven;
                if (oven == null || oven.inventory == null || oven.IsDestroyed) continue;
                if (!IsSamePrivilege(entity, tc)) continue;

                FillFurnace(oven);
                if (startRaid)
                    TurnOvenOn(oven);

                result.FurnacesTouched++;
            }

            return result;
        }

        private void FillFurnace(BaseOven oven)
        {
            ItemContainer inv = oven.inventory;
            if (inv == null) return;

            ItemContainer.ContentsType originalContentsType = inv.allowedContents;
            inv.allowedContents = ItemContainer.ContentsType.Generic;
            inv.SetFlag(ItemContainer.Flag.IsLocked, false);
            inv.SetFlag(ItemContainer.Flag.NoItemInput, false);

            AddFurnaceItem(inv, "wood", _config.FurnaceWoodAmount);

            if (_config.FurnaceOreAmount > 0)
            {
                AddFurnaceItem(inv, "metal.ore", _config.FurnaceOreAmount);
                AddFurnaceItem(inv, "sulfur.ore", _config.FurnaceSulfurOreAmount);
                if (_config.IncludeHqmOreInFurnaces)
                    AddFurnaceItem(inv, "hq.metal.ore", _config.FurnaceHqmOreAmount);
            }

            inv.allowedContents = originalContentsType;
            oven.SendNetworkUpdateImmediate();
        }

        private void AddFurnaceItem(ItemContainer inv, string shortname, int amount)
        {
            if (inv == null || amount <= 0) return;

            ItemDefinition def = ItemManager.FindItemDefinition(shortname);
            if (def == null) return;

            Item item = ItemManager.Create(def, amount);
            if (item == null) return;

            bool placed = item.MoveToContainer(inv, -1, true);
            if (!placed)
            {
                for (int slot = 0; slot < inv.capacity; slot++)
                {
                    if (inv.GetSlot(slot) == null)
                    {
                        placed = item.MoveToContainer(inv, slot, false);
                        if (placed) break;
                    }
                }
            }

            if (!placed)
                item.Remove();
        }

        private void TurnOvenOn(BaseOven oven)
        {
            if (oven == null || oven.IsDestroyed) return;

            try
            {
                oven.SetFlag(BaseEntity.Flags.On, true);
                oven.StartCooking();
                oven.SendNetworkUpdateImmediate();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not turn oven on: {ex.Message}");
            }
        }

        #endregion

        #region Turrets / SAMs

        private TurretResult EnableTurretsAndSams(BuildingPrivlidge tc, List<BaseEntity> entities)
        {
            TurretResult result = new TurretResult();

            foreach (BaseEntity entity in entities)
            {
                if (entity == null || entity.IsDestroyed) continue;
                if (!IsSamePrivilege(entity, tc)) continue;

                AutoTurret turret = entity as AutoTurret;
                if (turret != null)
                {
                    if (PowerTurretOn(turret))
                        result.TurretsOn++;
                    continue;
                }

                SamSite sam = entity as SamSite;
                if (sam != null)
                {
                    if (PowerSamOn(sam))
                        result.SamsOn++;
                }
            }

            return result;
        }

        private bool PowerTurretOn(AutoTurret turret)
        {
            if (turret == null || turret.IsDestroyed) return false;

            try
            {
                if ((turret.inputs?.Length ?? 0) == 0) return false;

                turret.SetFlag(BaseEntity.Flags.Reserved8, true);
                turret.InitiateStartup();
                turret.UpdateFromInput(_config.PoweredTurretInputAmount, 0);
                turret.SendNetworkUpdateImmediate();
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to power turret: {ex.Message}");
                return false;
            }
        }

        private bool PowerSamOn(SamSite sam)
        {
            if (sam == null || sam.IsDestroyed) return false;

            try
            {
                sam.UpdateHasPower(_config.PoweredSamInputAmount, 0);
                sam.SetFlag(BaseEntity.Flags.Reserved8, true);
                sam.SendNetworkUpdateImmediate();
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to power SAM site: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Raid Turret Interference

        private void ApplyRaidTurretInterference()
        {
            if (!_config.ChangeTurretInterferenceOnRaidStart) return;

            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "sentry.maxinterference", _config.RaidStartTurretMaxInterference.ToString(CultureInfo.InvariantCulture));
                Puts($"Raid turret interference max set to {_config.RaidStartTurretMaxInterference}.");
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to set sentry.maxinterference for raid start: {ex.Message}");
            }
        }

        private void RestoreNormalTurretInterference()
        {
            if (!_config.ChangeTurretInterferenceOnRaidStart || !_config.RestoreTurretInterferenceOnRaidEnd) return;

            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "sentry.maxinterference", _config.NormalTurretMaxInterference.ToString(CultureInfo.InvariantCulture));
                Puts($"Raid turret interference max restored to {_config.NormalTurretMaxInterference}.");
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to restore sentry.maxinterference after raid: {ex.Message}");
            }
        }

        #endregion

        #region ZoneManager / ZoneDomes / TruePVE

        private bool CreateOrUpdateZone(BasePlayer player, string zoneId, Vector3 position, float radius, bool pvp)
        {
            if (ZoneManager == null)
            {
                PrintWarning("ZoneManager is not loaded.");
                return false;
            }

            List<string> args = new List<string>
            {
                "name", pvp ? _config.PvpZoneName : _config.PveZoneName,
                "radius", radius.ToString(CultureInfo.InvariantCulture),
                "nodecay", "true",
                "undestr", "true"
            };

            foreach (string extra in _config.ExtraZoneFlags)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                args.Add(extra.Trim());
                args.Add("true");
            }

            object result = null;

            try
            {
                result = ZoneManager.Call("CreateOrUpdateZone", zoneId, args.ToArray(), position);
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneManager CreateOrUpdateZone call failed: {ex.Message}");
            }

            if (result is bool && (bool)result)
                return true;

            // Fallback for servers that prefer commands over API calls.
            try
            {
                string pos = $"{position.x.ToString(CultureInfo.InvariantCulture)} {position.y.ToString(CultureInfo.InvariantCulture)} {position.z.ToString(CultureInfo.InvariantCulture)}";
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone_add", zoneId);
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone", "name", pvp ? _config.PvpZoneName : _config.PveZoneName);
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone", "radius", radius.ToString(CultureInfo.InvariantCulture));
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone", "location", pos);
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone", "nodecay", "true");
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone", "undestr", "true");

                foreach (string extra in _config.ExtraZoneFlags)
                {
                    if (!string.IsNullOrWhiteSpace(extra))
                        ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone", extra.Trim(), "true");
                }

                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneManager command fallback failed: {ex.Message}");
                return false;
            }
        }

        private bool AddOrUpdateDome(BasePlayer player, string zoneId)
        {
            if (ZoneDomes == null)
            {
                PrintWarning("ZoneDomes is not loaded.");
                return false;
            }

            try
            {
                ZoneDomes.Call("RemoveExistingDome", player, zoneId);
            }
            catch
            {
                // Ignore missing dome/remove errors.
            }

            try
            {
                object result = ZoneDomes.Call("AddNewDome", player, zoneId, _config.ZoneDomeType, _config.ZoneDomeStackCount);
                return !(result is bool) || (bool)result;
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneDomes AddNewDome failed: {ex.Message}");
                return false;
            }
        }


        private bool RemoveRaidProtectionFlags(string zoneId)
        {
            if (ZoneManager == null)
            {
                PrintWarning("ZoneManager is not loaded.");
                return false;
            }

            bool ok = true;

            // IMPORTANT: must pass the real zone position, not Vector3.zero.
            // ZoneManager.CreateOrUpdateZone moves the zone to whatever position is supplied,
            // so passing Vector3.zero would teleport the zone to world origin.
            Vector3 position = _activeRaidCenter != Vector3.zero ? _activeRaidCenter : Vector3.zero;

            try
            {
                object result = ZoneManager.Call("CreateOrUpdateZone", zoneId, new string[]
                {
                    "nodecay", "false",
                    "undestr", "false"
                }, position);

                if (result is bool)
                    ok = (bool)result;
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneManager API protection flag removal failed: {ex.Message}");
                ok = false;
            }

            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone_edit", zoneId, "nodecay", "false");
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone_edit", zoneId, "undestr", "false");
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneManager command protection flag removal failed: {ex.Message}");
                ok = false;
            }

            return ok;
        }

        private bool RemoveRaidDome(string zoneId)
        {
            if (string.IsNullOrWhiteSpace(zoneId)) return false;
            if (ZoneDomes == null) return false;

            // ZoneDomes requires a BasePlayer arg for some versions; use any online player.
            // Prefer the active player list but gracefully handle a completely empty server.
            BasePlayer anyPlayer = BasePlayer.activePlayerList != null && BasePlayer.activePlayerList.Count > 0
                ? BasePlayer.activePlayerList[0]
                : null;

            try
            {
                object result = ZoneDomes.Call("RemoveExistingDome", anyPlayer, zoneId);
                return !(result is bool) || (bool)result;
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneDomes RemoveExistingDome failed: {ex.Message}");
                return false;
            }
        }

        private bool RemoveTruePveMapping(string zoneId)
        {
            if (string.IsNullOrWhiteSpace(zoneId)) return false;

            bool apiOk = false;
            bool configOk = false;

            if (TruePVE != null)
            {
                try
                {
                    object result = TruePVE.Call("RemoveMapping", zoneId);
                    apiOk = !(result is bool) || (bool)result;
                }
                catch (Exception ex)
                {
                    PrintWarning($"TruePVE RemoveMapping API failed: {ex.Message}");
                }
            }

            configOk = RemoveTruePveConfigMapping(zoneId);
            return apiOk || configOk;
        }

        private bool RemoveTruePveConfigMapping(string zoneId)
        {
            string configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "TruePVE.json");
            if (!File.Exists(configPath)) return false;

            try
            {
                string json = File.ReadAllText(configPath);
                JObject root = JObject.Parse(json);
                JObject mappingsObject = FindMappingsObject(root);
                if (mappingsObject == null) return false;

                bool removed = mappingsObject.Remove(zoneId);
                if (removed)
                    File.WriteAllText(configPath, root.ToString(Formatting.Indented));

                return removed;
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to remove TruePVE config mapping: {ex.Message}");
                return false;
            }
        }

        private bool RemoveZoneManagerZone(string zoneId)
        {
            if (string.IsNullOrWhiteSpace(zoneId)) return false;
            if (ZoneManager == null) return false;

            bool ok = false;

            try
            {
                object result = ZoneManager.Call("EraseZone", zoneId);
                ok = !(result is bool) || (bool)result;
            }
            catch { }

            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "zone_remove", zoneId);
                ok = true;
            }
            catch (Exception ex)
            {
                PrintWarning($"ZoneManager zone_remove failed: {ex.Message}");
            }

            return ok;
        }

        private bool AssignTruePveMapping(string zoneId, string ruleSet)
        {
            if (string.IsNullOrWhiteSpace(zoneId) || string.IsNullOrWhiteSpace(ruleSet))
                return false;

            bool apiOk = false;
            bool configOk = false;

            if (TruePVE != null)
            {
                try
                {
                    object result = TruePVE.Call("AddOrUpdateMapping", zoneId, ruleSet);
                    apiOk = !(result is bool) || (bool)result;
                }
                catch (Exception ex)
                {
                    PrintWarning($"TruePVE AddOrUpdateMapping API failed: {ex.Message}");
                }
            }
            else
            {
                PrintWarning("TruePVE is not loaded; attempting direct config mapping fallback.");
            }

            configOk = AddOrUpdateTruePveConfigMapping(zoneId, ruleSet);

            if (!apiOk && !configOk)
                PrintWarning($"Could not add TruePVE mapping '{zoneId}' -> '{ruleSet}'. Check TruePVE config format/API.");

            return apiOk || configOk;
        }

        private bool AddOrUpdateTruePveConfigMapping(string zoneId, string ruleSet)
        {
            string configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "TruePVE.json");
            if (!File.Exists(configPath))
            {
                PrintWarning($"TruePVE config not found at: {configPath}");
                return false;
            }

            try
            {
                string json = File.ReadAllText(configPath);
                JObject root = JObject.Parse(json);

                JObject mappingsObject = FindMappingsObject(root);
                if (mappingsObject == null)
                {
                    PrintWarning("Could not find a TruePVE mappings object in config. Expected a property named 'Mappings'.");
                    return false;
                }

                mappingsObject[zoneId] = ruleSet;

                File.WriteAllText(configPath, root.ToString(Formatting.Indented));
                Puts($"TruePVE config mapping added/updated: {zoneId} -> {ruleSet}");
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to update TruePVE config mapping: {ex.Message}");
                return false;
            }
        }

        private JObject FindMappingsObject(JToken token)
        {
            if (token == null) return null;

            JObject obj = token as JObject;
            if (obj == null)
            {
                foreach (JToken child in token.Children())
                {
                    JObject found = FindMappingsObject(child);
                    if (found != null) return found;
                }

                return null;
            }

            foreach (JProperty prop in obj.Properties())
            {
                if (string.Equals(prop.Name, "Mappings", StringComparison.OrdinalIgnoreCase) && prop.Value is JObject)
                    return (JObject)prop.Value;
            }

            foreach (JProperty prop in obj.Properties())
            {
                JObject found = FindMappingsObject(prop.Value);
                if (found != null) return found;
            }

            return null;
        }

        private void ApplyUlpExemption(string zoneId, Vector3 center, float radius)
        {
            if (UnifiedLootProtection == null || !UnifiedLootProtection.IsLoaded) return;
            if (string.IsNullOrWhiteSpace(zoneId) || radius <= 0f) return;

            try
            {
                object result = UnifiedLootProtection.Call("AddExemptZone", zoneId, center, radius);
                if (result is bool b && b)
                    Puts($"ULP exempt zone registered for raidbase zone {zoneId}.");
                else
                    PrintWarning($"ULP AddExemptZone returned unexpected result for {zoneId}: {result}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to register ULP exempt zone for {zoneId}: {ex.Message}");
            }
        }

        private void RemoveUlpExemption(string zoneId)
        {
            if (UnifiedLootProtection == null || !UnifiedLootProtection.IsLoaded) return;
            if (string.IsNullOrWhiteSpace(zoneId)) return;

            try
            {
                UnifiedLootProtection.Call("RemoveExemptZone", zoneId);
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to remove ULP exempt zone for {zoneId}: {ex.Message}");
            }
        }

        private void ReloadTruePve()
        {
            if (!_config.ReloadTruePveAfterMapping) return;

            timer.Once(_config.TruePveReloadDelaySeconds, () =>
            {
                try
                {
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, _config.TruePveReloadCommand);
                }
                catch (Exception ex)
                {
                    PrintWarning($"TruePVE reload command failed: {ex.Message}");
                }

                if (_config.TryCarbonReloadFallback && !_config.TruePveReloadCommand.Equals("c.reload TruePVE", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        ConsoleSystem.Run(ConsoleSystem.Option.Server, "c.reload TruePVE");
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"TruePVE Carbon fallback reload failed: {ex.Message}");
                    }
                }
            });
        }

        #endregion

        #region Loot Table Loading

        private List<LootEntry> GetLootTable(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return null;

            if (_lootCache.TryGetValue(profileName, out List<LootEntry> cached))
                return cached;

            string path = GetLootFilePath(profileName);
            if (!File.Exists(path))
            {
                string alternate = Path.Combine(Interface.Oxide.DataDirectory, profileName);
                if (File.Exists(alternate)) path = alternate;
            }

            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                JToken root = JToken.Parse(json);
                List<LootEntry> entries = new List<LootEntry>();
                ExtractLootEntries(root, entries);

                entries.RemoveAll(e => string.IsNullOrWhiteSpace(e.ShortName));
                _lootCache[profileName] = entries;
                return entries;
            }
            catch (Exception ex)
            {
                PrintError($"Failed reading loot table '{profileName}': {ex.Message}");
                return null;
            }
        }

        private string GetLootFilePath(string profileName)
        {
            string safeName = profileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? profileName : profileName + ".json";
            string relativeLootFolder = _config.RaidableBasesLootFolder ?? "NightshadeRaidbases/Loot Tables";
            relativeLootFolder = relativeLootFolder
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            return Path.Combine(Interface.Oxide.DataDirectory, relativeLootFolder, safeName);
        }

        private void ExtractLootEntries(JToken token, List<LootEntry> output)
        {
            if (token == null) return;

            if (token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                LootEntry entry = TryParseLootEntry(obj);
                if (entry != null)
                {
                    output.Add(entry);
                    return;
                }

                foreach (JProperty prop in obj.Properties())
                    ExtractLootEntries(prop.Value, output);
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (JToken child in token.Children())
                    ExtractLootEntries(child, output);
            }
        }

        private LootEntry TryParseLootEntry(JObject obj)
        {
            string shortName = ReadString(obj, "ShortName", "shortname", "shortName", "Item Shortname", "ItemShortName", "Name", "name");
            if (string.IsNullOrWhiteSpace(shortName)) return null;

            if (ItemManager.FindItemDefinition(shortName) == null && !HasAnyJsonKey(obj, "ShortName", "shortname", "shortName", "Item Shortname", "ItemShortName"))
                return null;

            int min = ReadInt(obj, 1, "Minimum Amount", "MinimumAmount", "min", "Min", "AmountMin", "amountMin", "Amount Minimum");
            int max = ReadInt(obj, min, "Maximum Amount", "MaximumAmount", "max", "Max", "AmountMax", "amountMax", "Amount Maximum", "Amount");
            if (max < min) max = min;

            float chance = ReadFloat(obj, 100f, "Inclusion Chance Percentage", "Chance", "chance", "Probability", "probability");
            float weight = ReadFloat(obj, 1f, "Weight", "weight", "Rarity Weight", "rarityWeight");
            ulong skin = ReadUlong(obj, 0UL, "SkinID", "SkinId", "skin", "skinid", "Skin");
            string custom = ReadString(obj, "CustomName", "Custom Name", "DisplayName", "displayName");

            return new LootEntry
            {
                ShortName = shortName,
                MinAmount = min,
                MaxAmount = max,
                Chance = Mathf.Clamp(chance, 0f, 100f),
                Weight = Math.Max(0.01f, weight),
                SkinId = skin,
                CustomName = custom
            };
        }

        private LootEntry PickLoot(List<LootEntry> loot)
        {
            if (loot == null || loot.Count == 0) return null;

            double total = 0;
            for (int i = 0; i < loot.Count; i++)
                total += Math.Max(0.01, loot[i].Weight);

            double roll = _rng.NextDouble() * total;
            double cumulative = 0;

            for (int i = 0; i < loot.Count; i++)
            {
                cumulative += Math.Max(0.01, loot[i].Weight);
                if (roll <= cumulative) return loot[i];
            }

            return loot[loot.Count - 1];
        }

        #endregion

        #region Helpers

        private string GetGridFromPosition(Vector3 position)
        {
            float worldSize = ConVar.Server.worldsize;
            float offset = worldSize / 2f;
            int col = Mathf.FloorToInt((position.x + offset) / 146.3f);
            int row = Mathf.FloorToInt((worldSize - (position.z + offset)) / 146.3f);
            string letter = string.Empty;
            int n = col;
            do { letter = (char)('A' + n % 26) + letter; n /= 26; } while (n-- > 0);
            return $"{letter}{row}";
        }

        private void SpawnMapMarkers(Vector3 position, float radius, bool pvp, string ownerName)
        {
            RemoveMapMarkers();

            string mode = pvp ? "PvP" : "PvE";
            string label = $"{ownerName} • {mode} Raidbase";

            try
            {
                VendingMachineMapMarker pin = GameManager.server.CreateEntity(
                    "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab",
                    position) as VendingMachineMapMarker;

                if (pin != null)
                {
                    pin.markerShopName = label;
                    pin.enableSaving = false;
                    pin.Spawn();
                    _activeRaidMapPinId = pin.net?.ID.Value ?? 0UL;
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to spawn raidbase map pin: {ex.Message}");
            }

            try
            {
                MapMarkerGenericRadius circle = GameManager.server.CreateEntity(
                    "assets/prefabs/tools/map/genericradiusmarker.prefab",
                    position) as MapMarkerGenericRadius;

                if (circle != null)
                {
                    // Map radius is in normalised map units: world metres / ~145
                    circle.radius = Mathf.Clamp(radius / 145f, 0.01f, 1f);
                    circle.alpha = pvp ? _config.MapMarkerPvpAlpha : _config.MapMarkerPveAlpha;

                    Color fill;
                    Color border;
                    if (pvp)
                    {
                        ColorUtility.TryParseHtmlString(_config.MapMarkerPvpFillColor, out fill);
                        ColorUtility.TryParseHtmlString(_config.MapMarkerPvpBorderColor, out border);
                    }
                    else
                    {
                        ColorUtility.TryParseHtmlString(_config.MapMarkerPveFillColor, out fill);
                        ColorUtility.TryParseHtmlString(_config.MapMarkerPveBorderColor, out border);
                    }

                    circle.color1 = fill;
                    circle.color2 = border;
                    circle.enableSaving = false;
                    circle.Spawn();
                    circle.SendUpdate();
                    _activeRaidMapCircleId = circle.net?.ID.Value ?? 0UL;
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to spawn raidbase map circle: {ex.Message}");
            }
        }

        private void RemoveMapMarkers()
        {
            KillMapMarkerById(ref _activeRaidMapPinId);
            KillMapMarkerById(ref _activeRaidMapCircleId);
        }

        private void KillMapMarkerById(ref ulong id)
        {
            if (id == 0UL) return;

            BaseNetworkable entity = BaseNetworkable.serverEntities.Find(new NetworkableId(id));
            if (entity != null && !entity.IsDestroyed)
            {
                try { entity.Kill(); }
                catch (Exception ex) { PrintWarning($"Failed to kill map marker ({id}): {ex.Message}"); }
            }

            id = 0UL;
        }

        private bool IsSamePrivilege(BaseEntity entity, BuildingPrivlidge tc)
        {
            if (!_config.RequireSameBuildingPrivilege) return true;
            return IsEntityOwnedByRaidTc(entity, tc);
        }

        private bool IsEntityOwnedByRaidTc(BaseEntity entity, BuildingPrivlidge tc)
        {
            if (entity == null || tc == null || tc.net == null) return false;

            BuildingPrivlidge entityAsTc = entity as BuildingPrivlidge;
            if (entityAsTc != null && entityAsTc.net != null)
                return entityAsTc.net.ID == tc.net.ID;

            BuildingPrivlidge entityTc = entity.GetBuildingPrivilege();
            return entityTc != null && entityTc.net != null && entityTc.net.ID == tc.net.ID;
        }

        private bool ContainsAny(string haystack, List<string> needles)
        {
            if (string.IsNullOrEmpty(haystack) || needles == null) return false;

            foreach (string needle in needles)
            {
                if (string.IsNullOrWhiteSpace(needle)) continue;
                if (haystack.Contains(needle.ToLowerInvariant())) return true;
            }

            return false;
        }

        private string BuildZoneId(BuildingPrivlidge tc, bool pvp)
        {
            string mode = pvp ? "pvp" : "pve";
            string id = tc?.net != null ? tc.net.ID.Value.ToString() : UnityEngine.Random.Range(100000, 999999).ToString();
            return $"{_config.ZoneIdPrefix}_{mode}_{id}";
        }

        private void RemoveOppositeModeArtifacts(BuildingPrivlidge tc, bool pvp)
        {
            if (tc == null || tc.net == null) return;

            string oppositeZoneId = BuildZoneId(tc, !pvp);
            RemoveRaidDome(oppositeZoneId);
            if (RemoveTruePveMapping(oppositeZoneId))
                ReloadTruePve();

            if (_config.RemoveOppositeModeZoneOnStart)
                RemoveZoneManagerZone(oppositeZoneId);
        }

        private bool ResolveStartMode(BuildingPrivlidge tc, string[] args)
        {
            if (HasArg(args, "pvp")) return true;
            if (HasArg(args, "pve")) return false;

            if (tc != null && tc.net != null)
            {
                PreparedRaidBaseState prepared;
                if (_preparedRaidBases.TryGetValue(tc.net.ID.Value, out prepared))
                    return prepared.Pvp;

                if (_data != null && _data.PreparedRaidBases != null && _data.PreparedRaidBases.TryGetValue(tc.net.ID.Value.ToString(), out prepared))
                    return prepared.Pvp;
            }

            return _config.DefaultReadyModeIsPvp;
        }

        private bool GetRequestedMode(string[] args, bool fallbackPvp)
        {
            if (HasArg(args, "pvp")) return true;
            if (HasArg(args, "pve")) return false;
            return fallbackPvp;
        }

        private string[] StripModeArgs(string[] args)
        {
            if (args == null || args.Length == 0) return args ?? new string[0];
            return args.Where(arg => !string.Equals(arg, "pvp", StringComparison.OrdinalIgnoreCase) && !string.Equals(arg, "pve", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        private string GetProfileName(string[] args, bool pvp)
        {
            if (args != null && args.Length > 0)
            {
                string joined = string.Join(" ", args).Trim();
                if (!string.IsNullOrWhiteSpace(joined))
                    return joined;
            }

            return pvp ? _config.DefaultPvpLootProfile : _config.DefaultPveLootProfile;
        }


        private bool HasArg(string[] args, string value)
        {
            if (args == null || string.IsNullOrWhiteSpace(value)) return false;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool HasPermission(BasePlayer player, string perm)
        {
            if (player == null) return false;
            return permission.UserHasPermission(player.UserIDString, perm) || permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        private bool HasAnyJsonKey(JObject obj, params string[] keys)
        {
            if (obj == null || keys == null) return false;

            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.GetValue(keys[i], StringComparison.OrdinalIgnoreCase) != null) return true;
            }

            return false;
        }

        private string ReadString(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj.GetValue(key, StringComparison.OrdinalIgnoreCase);
                if (token == null) continue;

                string value = token.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return string.Empty;
        }

        private int ReadInt(JObject obj, int fallback, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj.GetValue(key, StringComparison.OrdinalIgnoreCase);
                if (token == null) continue;

                if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return value;
                if (float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return Mathf.RoundToInt(f);
            }

            return fallback;
        }

        private float ReadFloat(JObject obj, float fallback, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj.GetValue(key, StringComparison.OrdinalIgnoreCase);
                if (token == null) continue;

                if (float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return value;
            }

            return fallback;
        }

        private ulong ReadUlong(JObject obj, ulong fallback, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj.GetValue(key, StringComparison.OrdinalIgnoreCase);
                if (token == null) continue;

                if (ulong.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value)) return value;
            }

            return fallback;
        }

        private void Reply(BasePlayer player, string color, string message)
        {
            SendReply(player, $"<color=#b084ff>[Nightshade Raidbase]</color> <color={color}>{message}</color>");
        }

        #endregion

        #region Data Models

        private class PendingCopyRequest
        {
            public string CopyName;
            public ulong UserId;
            public string UserIdString;
            public string PlayerName;
            public bool Pvp;
            public string[] Args;
            public string ProfileName;
            public int PollAttempts;
        }

        private class PreparedRaidBaseState
        {
            public ulong TcId;
            public string OwnerId;
            public string OwnerName;
            public bool Pvp;
            public string ZoneId;
            public string ProfileName;
            public string CopyName;
            public float Radius;
        }

        private class PluginData
        {
            public Dictionary<string, int> PlayerSaveNumbers = new Dictionary<string, int>();
            public Dictionary<string, PreparedRaidBaseState> PreparedRaidBases = new Dictionary<string, PreparedRaidBaseState>();
        }

        private class CleanupResult
        {
            public int EntitiesRemoved;
            public int EntitiesFailed;
        }

        private class FillResult
        {
            public int ContainersFound;
            public int ContainersFilled;
            public int ItemsAdded;
        }

        private class FurnaceResult
        {
            public int FurnacesTouched;
        }

        private class TurretResult
        {
            public int TurretsOn;
            public int SamsOn;
        }

        private class LootEntry
        {
            public string ShortName;
            public int MinAmount = 1;
            public int MaxAmount = 1;
            public float Chance = 100f;
            public float Weight = 1f;
            public ulong SkinId;
            public string CustomName;

            public int RollAmount(System.Random rng, float multiplier)
            {
                int min = Math.Max(1, MinAmount);
                int max = Math.Max(min, MaxAmount);
                int rolled = rng.Next(min, max + 1);
                return Mathf.Max(1, Mathf.RoundToInt(rolled * multiplier));
            }
        }

        private class PluginConfig
        {
            [JsonProperty("Default /raidbaseready mode is PvP when no pve/pvp argument is given")]
            public bool DefaultReadyModeIsPvp = false;

            [JsonProperty("Copy raidbase with CopyPaste during ready command")]
            public bool CopyRaidbaseOnReady = true;

            [JsonProperty("Nightshade raidbase data root folder under Carbon/Oxide data folder")]
            public string NightshadeRaidbaseDataRootFolder = "NightshadeRaidbases";

            [JsonProperty("Plugin state data file name inside Nightshade raidbase data root folder")]
            public string PluginStateDataFileName = "Nightshade_Raidbase_Data.json";

            [JsonProperty("Create default raidbase_loot.json if missing")]
            public bool CreateDefaultLootTableIfMissing = true;

            [JsonProperty("CopyPaste filename pattern")]
            public string CopyPasteFilenamePattern = "{player}_{number}";

            [JsonProperty("CopyPaste starting number per player")]
            public int CopyPasteStartingNumber = 1;

            [JsonProperty("CopyPaste number padding digits (3 = 001)")]
            public int CopyPasteNumberPadding = 3;

            [JsonProperty("CopyPaste max filename length")]
            public int CopyPasteMaxFilenameLength = 48;

            [JsonProperty("CopyPaste data folder under Carbon/Oxide data folder")]
            public string CopyPasteDataFolderUnderOxideData = "copypaste";

            [JsonProperty("Archive saved raidbase copies under data folder")]
            public string SavedRaidbaseArchiveFolderUnderOxideData = "NightshadeRaidbases/Player Raidbases";

            [JsonProperty("Archive CopyPaste save into NightshadeRaidbases data folder after successful copy")]
            public bool ArchiveSavedRaidbaseCopy = true;

            [JsonProperty("Keep original save in CopyPaste folder after archiving")]
            public bool KeepOriginalCopyPasteSaveAfterArchive = true;

            [JsonProperty("CopyPaste copy radius (0 = CopyPaste default)")]
            public float CopyPasteRadius = 0f;

            [JsonProperty("CopyPaste copy method (building or proximity)")]
            public string CopyPasteMethod = "building";

            [JsonProperty("CopyPaste copy share data (CodeLocks, TC auth, bags)")]
            public bool CopyPasteShareData = false;

            [JsonProperty("CopyPaste copy trees and resources")]
            public bool CopyPasteCopyTreesAndResources = false;

            [JsonProperty("Temporarily grant copypaste.copy while saving raidbase")]
            public bool TemporarilyGrantCopyPasteCopyPermission = true;

            [JsonProperty("Verify CopyPaste json file exists after copy")]
            public bool VerifyCopyPasteFileCreated = true;

            [JsonProperty("Raidbase loot table folder under data folder")]
            public string RaidableBasesLootFolder = "NightshadeRaidbases/Loot Tables";

            [JsonProperty("Default PvE Loot Profile")]
            public string DefaultPveLootProfile = "raidbase_loot";

            [JsonProperty("Default PvP Loot Profile")]
            public string DefaultPvpLootProfile = "raidbase_loot";

            [JsonProperty("Default TC Radius")]
            public float DefaultTcRadius = 30f;

            [JsonProperty("TC Radius Override (0 = use default above)")]
            public float TcRadiusOverride = 0f;

            [JsonProperty("Minimum Box Fill Percent")]
            public float MinimumBoxFillPercent = 0.5f;

            [JsonProperty("Top Off Already Half-Full Boxes")]
            public bool TopOffAlreadyHalfFullBoxes = false;

            [JsonProperty("Default Amount Multiplier")]
            public float DefaultAmountMultiplier = 1f;

            [JsonProperty("Allow Stacking Into Existing Slots")]
            public bool AllowStackingIntoExistingSlots = true;

            [JsonProperty("Allow Unowned Cupboards")]
            public bool AllowUnownedCupboards = true;

            [JsonProperty("Require Same Building Privilege")]
            public bool RequireSameBuildingPrivilege = true;

            [JsonProperty("Log Invalid Items")]
            public bool LogInvalidItems = true;

            [JsonProperty("Entity Layer Mask")]
            public int EntityLayerMask = -1;

            [JsonProperty("Whitelisted Loot Container Prefab Contains - empty means all storage containers")]
            public List<string> WhitelistedLootContainerPrefabContains = new List<string>();

            [JsonProperty("Blacklisted Loot Container Prefab Contains")]
            public List<string> BlacklistedLootContainerPrefabContains = new List<string>
            {
                "cupboard.tool",
                "furnace",
                "campfire",
                "bbq",
                "fireplace",
                "refinery",
                "vendingmachine",
                "dropbox",
                "mailbox"
            };

            [JsonProperty("Furnace Wood Amount")]
            public int FurnaceWoodAmount = 1000;

            [JsonProperty("Furnace Metal Ore Amount")]
            public int FurnaceOreAmount = 1000;

            [JsonProperty("Furnace Sulfur Ore Amount")]
            public int FurnaceSulfurOreAmount = 1000;

            [JsonProperty("Include HQM Ore In Furnaces")]
            public bool IncludeHqmOreInFurnaces = true;

            [JsonProperty("Furnace HQM Ore Amount")]
            public int FurnaceHqmOreAmount = 100;

            [JsonProperty("Powered Turret Input Amount")]
            public int PoweredTurretInputAmount = 11;

            [JsonProperty("Powered SAM Input Amount")]
            public int PoweredSamInputAmount = 25;

            [JsonProperty("Change sentry.maxinterference on raid start")]
            public bool ChangeTurretInterferenceOnRaidStart = true;

            [JsonProperty("Raid start sentry.maxinterference value")]
            public int RaidStartTurretMaxInterference = 999;

            [JsonProperty("Restore sentry.maxinterference on raid end/plugin unload")]
            public bool RestoreTurretInterferenceOnRaidEnd = true;

            [JsonProperty("Normal sentry.maxinterference value to restore")]
            public int NormalTurretMaxInterference = 12;

            [JsonProperty("Broadcast raid start message to chat")]
            public bool BroadcastRaidStartToChat = true;

            [JsonProperty("Raid start chat message")]
            public string RaidStartChatMessage = "<color=#b084ff>[Nightshade Raidbase]</color> <color=#d9d2e9>A player-built raidbase is now live! Owner: <color=#ffffff>{owner}</color> | Mode: <color=#ffffff>{mode}</color> | Grid: <color=#ffffff>{grid}</color>. Defend the TC. Cleanup starts <color=#ffffff>{cleanupMinutes} minutes</color> after TC destruction.</color>";

            [JsonProperty("Cleanup delay after TC destroyed minutes")]
            public int CleanupDelayAfterTcDestroyedMinutes = 45;

            [JsonProperty("Remove raidbase entities on cleanup")]
            public bool RemoveRaidbaseEntitiesOnCleanup = true;

            [JsonProperty("Require confirm for manual /raid end deletion")]
            public bool RequireConfirmForManualRaidEnd = true;

            [JsonProperty("Cleanup eligible entities in raid radius that were not tracked at start - RISKY, only loose dropped loot is swept when true")]
            public bool CleanupEligibleEntitiesInRaidRadius = false;

            [JsonProperty("Cleanup entity extra radius - used only for finding tracked IDs and optional loose dropped loot sweep")]
            public float CleanupEntityExtraRadius = 0f;

            [JsonProperty("Cleanup prefab whitelist contains")]
            public List<string> CleanupPrefabWhitelistContains = new List<string>
            {
                "deployable",
                "building",
                "door",
                "wall.external",
                "gates.external",
                "barricade",
                "trap",
                "landmine",
                "guntrap",
                "flameturret",
                "electric",
                "industrial"
            };

            [JsonProperty("Cleanup prefab blacklist contains")]
            public List<string> CleanupPrefabBlacklistContains = new List<string>
            {
                "player.prefab",
                "scientist",
                "npc",
                "horse",
                "stag",
                "boar",
                "wolf",
                "bear",
                "chicken"
            };

            [JsonProperty("Use AdvancedStatus cleanup status bar")]
            public bool UseAdvancedStatusCleanupBar = true;

            [JsonProperty("AdvancedStatus cleanup text")]
            public string AdvancedStatusCleanupText = "TC DOWN • Cleanup";

            [JsonProperty("AdvancedStatus cleanup order")]
            public int AdvancedStatusCleanupOrder = 21;

            [JsonProperty("AdvancedStatus cleanup height")]
            public int AdvancedStatusCleanupHeight = 26;

            [JsonProperty("AdvancedStatus cleanup main color")]
            public string AdvancedStatusCleanupMainColor = "#120A1C";

            [JsonProperty("AdvancedStatus cleanup main transparency")]
            public float AdvancedStatusCleanupMainTransparency = 0.35f;

            [JsonProperty("AdvancedStatus cleanup image sprite")]
            public string AdvancedStatusCleanupImageSprite = "assets/icons/stopwatch.png";

            [JsonProperty("AdvancedStatus cleanup image color")]
            public string AdvancedStatusCleanupImageColor = "#B084FF";

            [JsonProperty("AdvancedStatus cleanup image transparency")]
            public float AdvancedStatusCleanupImageTransparency = 0.85f;

            [JsonProperty("AdvancedStatus cleanup text size")]
            public int AdvancedStatusCleanupTextSize = 12;

            [JsonProperty("AdvancedStatus cleanup text color")]
            public string AdvancedStatusCleanupTextColor = "#D9D2E9";

            [JsonProperty("AdvancedStatus cleanup text font")]
            public string AdvancedStatusCleanupTextFont = "RobotoCondensed-Bold.ttf";

            [JsonProperty("AdvancedStatus cleanup subtext size")]
            public int AdvancedStatusCleanupSubTextSize = 12;

            [JsonProperty("AdvancedStatus cleanup subtext color")]
            public string AdvancedStatusCleanupSubTextColor = "#D9D2E9";

            [JsonProperty("AdvancedStatus cleanup subtext font")]
            public string AdvancedStatusCleanupSubTextFont = "RobotoCondensed-Bold.ttf";

            [JsonProperty("AdvancedStatus cleanup progress reverse")]
            public bool AdvancedStatusCleanupProgressReverse = false;

            [JsonProperty("AdvancedStatus cleanup progress color")]
            public string AdvancedStatusCleanupProgressColor = "#B084FF";

            [JsonProperty("AdvancedStatus cleanup progress transparency")]
            public float AdvancedStatusCleanupProgressTransparency = 0.8f;

            [JsonProperty("AdvancedStatus cleanup progress offset min")]
            public string AdvancedStatusCleanupProgressOffsetMin = "25 2.5";

            [JsonProperty("AdvancedStatus cleanup progress offset max")]
            public string AdvancedStatusCleanupProgressOffsetMax = "-3.5 -3.5";

            [JsonProperty("Show cleanup GUI timer only near raidbase")]
            public bool ShowCleanupTimerOnlyNearRaidbase = true;

            [JsonProperty("Remove cleanup GUI timer when player leaves raid zone")]
            public bool RemoveCleanupTimerWhenPlayerLeavesRaidZone = true;

            [JsonProperty("Cleanup GUI extra show radius beyond raid radius")]
            public float CleanupTimerExtraShowRadius = 25f;

            [JsonProperty("Cleanup GUI anchor min")]
            public string CleanupTimerAnchorMin = "0.38 0.145";

            [JsonProperty("Cleanup GUI anchor max")]
            public string CleanupTimerAnchorMax = "0.62 0.185";

            [JsonProperty("Cleanup GUI background color")]
            public string CleanupTimerBackgroundColor = "0.05 0.04 0.08 0.78";

            [JsonProperty("Cleanup GUI text color")]
            public string CleanupTimerTextColor = "0.83 0.70 1.00 1.00";

            [JsonProperty("Cleanup GUI font size")]
            public int CleanupTimerFontSize = 13;

            [JsonProperty("Cleanup GUI text format")]
            public string CleanupTimerText = "TC DOWN • Cleanup in {0}";

            [JsonProperty("Map marker PvE fill color (hex)")]
            public string MapMarkerPveFillColor = "#B084FF";

            [JsonProperty("Map marker PvE border color (hex)")]
            public string MapMarkerPveBorderColor = "#B084FF";

            [JsonProperty("Map marker PvP fill color (hex)")]
            public string MapMarkerPvpFillColor = "#FF4444";

            [JsonProperty("Map marker PvP border color (hex)")]
            public string MapMarkerPvpBorderColor = "#FF4444";

            [JsonProperty("Map marker PvE alpha (0-1)")]
            public float MapMarkerPveAlpha = 0.4f;

            [JsonProperty("Map marker PvP alpha (0-1)")]
            public float MapMarkerPvpAlpha = 0.4f;

            [JsonProperty("Remove ZoneManager zone on raid end")]
            public bool RemoveZoneManagerZoneOnRaidEnd = false;

            [JsonProperty("Remove opposite PvE/PvP zone on raid start")]
            public bool RemoveOppositeModeZoneOnStart = false;

            [JsonProperty("Zone ID Prefix")]
            public string ZoneIdPrefix = "nightshade_raidbase";

            [JsonProperty("PvE Zone Name")]
            public string PveZoneName = "Nightshade Raidbase PvE";

            [JsonProperty("PvP Zone Name")]
            public string PvpZoneName = "Nightshade Raidbase PvP";

            [JsonProperty("ZoneDomes dome type (0 grey, 1 red, 2 blue, 3 green, 4 purple)")]
            public int ZoneDomeType = 4;

            [JsonProperty("ZoneDomes dome stack count / thickness (1-10)")]
            public int ZoneDomeStackCount = 5;


            [JsonProperty("Extra ZoneManager Flags To Set True")]
            public List<string> ExtraZoneFlags = new List<string>();

            [JsonProperty("TruePVE PvE RuleSet")]
            public string TruePvePveRuleSet = "raidbasepve";

            [JsonProperty("TruePVE PvP RuleSet")]
            public string TruePvePvpRuleSet = "raidbasepvp";

            [JsonProperty("Reload TruePVE After Mapping")]
            public bool ReloadTruePveAfterMapping = true;

            [JsonProperty("TruePVE Reload Delay Seconds")]
            public float TruePveReloadDelaySeconds = 1.5f;

            [JsonProperty("TruePVE Reload Command")]
            public string TruePveReloadCommand = "c.reload TruePVE";

            [JsonProperty("Try Carbon Reload Fallback")]
            public bool TryCarbonReloadFallback = true;

            public static PluginConfig DefaultConfig() => new PluginConfig();

            public void Sanitize()
            {
                if (string.IsNullOrWhiteSpace(NightshadeRaidbaseDataRootFolder)) NightshadeRaidbaseDataRootFolder = "NightshadeRaidbases";
                NightshadeRaidbaseDataRootFolder = NightshadeRaidbaseDataRootFolder.Trim().Trim('\\', '/');
                if (string.IsNullOrWhiteSpace(PluginStateDataFileName)) PluginStateDataFileName = "Nightshade_Raidbase_Data.json";
                if (!PluginStateDataFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) PluginStateDataFileName += ".json";
                if (string.IsNullOrWhiteSpace(CopyPasteFilenamePattern)) CopyPasteFilenamePattern = "{player}_{number}";
                if (!CopyPasteFilenamePattern.Contains("{player}")) CopyPasteFilenamePattern = "{player}_{number}";
                if (!CopyPasteFilenamePattern.Contains("{number}")) CopyPasteFilenamePattern += "_{number}";
                if (CopyPasteStartingNumber < 1) CopyPasteStartingNumber = 1;
                if (CopyPasteNumberPadding < 0) CopyPasteNumberPadding = 0;
                if (CopyPasteNumberPadding > 8) CopyPasteNumberPadding = 8;
                if (CopyPasteMaxFilenameLength < 12) CopyPasteMaxFilenameLength = 48;
                if (string.IsNullOrWhiteSpace(CopyPasteDataFolderUnderOxideData)) CopyPasteDataFolderUnderOxideData = "copypaste";
                if (string.IsNullOrWhiteSpace(SavedRaidbaseArchiveFolderUnderOxideData)) SavedRaidbaseArchiveFolderUnderOxideData = "NightshadeRaidbases/Player Raidbases";
                if (CopyPasteRadius < 0f) CopyPasteRadius = 0f;
                if (string.IsNullOrWhiteSpace(CopyPasteMethod)) CopyPasteMethod = "building";
                CopyPasteMethod = CopyPasteMethod.Equals("proximity", StringComparison.OrdinalIgnoreCase) ? "proximity" : "building";
                if (string.IsNullOrWhiteSpace(RaidableBasesLootFolder) || RaidableBasesLootFolder.Equals("RaidableBases/Base_Loot", StringComparison.OrdinalIgnoreCase)) RaidableBasesLootFolder = "NightshadeRaidbases/Loot Tables";
                if (string.IsNullOrWhiteSpace(DefaultPveLootProfile)) DefaultPveLootProfile = "raidbase_loot";
                if (string.IsNullOrWhiteSpace(DefaultPvpLootProfile)) DefaultPvpLootProfile = "raidbase_loot";
                if (DefaultTcRadius <= 0f) DefaultTcRadius = 30f;
                MinimumBoxFillPercent = Mathf.Clamp(MinimumBoxFillPercent, 0.1f, 1f);
                if (DefaultAmountMultiplier <= 0f) DefaultAmountMultiplier = 1f;
                if (RaidStartTurretMaxInterference < 1) RaidStartTurretMaxInterference = 999;
                if (NormalTurretMaxInterference < 1) NormalTurretMaxInterference = 12;
                if (string.IsNullOrWhiteSpace(RaidStartChatMessage)) RaidStartChatMessage = "<color=#b084ff>[Nightshade Raidbase]</color> <color=#d9d2e9>A player-built raidbase is now live! Owner: <color=#ffffff>{owner}</color> | Mode: <color=#ffffff>{mode}</color> | Grid: <color=#ffffff>{grid}</color>. Defend the TC. Cleanup starts <color=#ffffff>{cleanupMinutes} minutes</color> after TC destruction.</color>";
                if (CleanupDelayAfterTcDestroyedMinutes < 1) CleanupDelayAfterTcDestroyedMinutes = 45;
                if (CleanupEntityExtraRadius < 0f) CleanupEntityExtraRadius = 0f;
                if (CleanupTimerExtraShowRadius < 0f) CleanupTimerExtraShowRadius = 0f;
                if (CleanupTimerFontSize < 8) CleanupTimerFontSize = 13;
                if (string.IsNullOrWhiteSpace(CleanupTimerAnchorMin)) CleanupTimerAnchorMin = "0.38 0.145";
                if (string.IsNullOrWhiteSpace(CleanupTimerAnchorMax)) CleanupTimerAnchorMax = "0.62 0.185";
                if (string.IsNullOrWhiteSpace(CleanupTimerBackgroundColor)) CleanupTimerBackgroundColor = "0.05 0.04 0.08 0.78";
                if (string.IsNullOrWhiteSpace(CleanupTimerTextColor)) CleanupTimerTextColor = "0.83 0.70 1.00 1.00";
                if (string.IsNullOrWhiteSpace(CleanupTimerText)) CleanupTimerText = "TC DOWN • Cleanup in {0}";
                if (CleanupPrefabWhitelistContains == null) CleanupPrefabWhitelistContains = new List<string>();
                if (CleanupPrefabBlacklistContains == null) CleanupPrefabBlacklistContains = new List<string>();
                if (WhitelistedLootContainerPrefabContains == null) WhitelistedLootContainerPrefabContains = new List<string>();
                if (BlacklistedLootContainerPrefabContains == null) BlacklistedLootContainerPrefabContains = new List<string>();
                if (ExtraZoneFlags == null) ExtraZoneFlags = new List<string>();
                if (string.IsNullOrWhiteSpace(MapMarkerPveFillColor)) MapMarkerPveFillColor = "#B084FF";
                if (string.IsNullOrWhiteSpace(MapMarkerPveBorderColor)) MapMarkerPveBorderColor = "#B084FF";
                if (string.IsNullOrWhiteSpace(MapMarkerPvpFillColor)) MapMarkerPvpFillColor = "#FF4444";
                if (string.IsNullOrWhiteSpace(MapMarkerPvpBorderColor)) MapMarkerPvpBorderColor = "#FF4444";
                MapMarkerPveAlpha = Mathf.Clamp(MapMarkerPveAlpha, 0f, 1f);
                MapMarkerPvpAlpha = Mathf.Clamp(MapMarkerPvpAlpha, 0f, 1f);
                if (string.IsNullOrWhiteSpace(ZoneIdPrefix)) ZoneIdPrefix = "nightshade_raidbase";
                ZoneDomeType = Mathf.Clamp(ZoneDomeType, 0, 4);
                ZoneDomeStackCount = Mathf.Clamp(ZoneDomeStackCount, 1, 10);
                if (string.IsNullOrWhiteSpace(TruePvePveRuleSet)) TruePvePveRuleSet = "raidbasepve";
                if (string.IsNullOrWhiteSpace(TruePvePvpRuleSet)) TruePvePvpRuleSet = "raidbasepvp";
                if (TruePveReloadDelaySeconds < 0f) TruePveReloadDelaySeconds = 1.5f;
                if (string.IsNullOrWhiteSpace(TruePveReloadCommand)) TruePveReloadCommand = "c.reload TruePVE";
            }
        }

        #endregion
    }
}