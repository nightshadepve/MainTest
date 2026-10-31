/* v 1.1.0
* Fixed SkillTree Extended_Mag compatibility.
* Prototype Rocketlauncher and Rocketlauncher Tiers magazine size is now also re-applied on equip and fire.
* If you experience any issues with magazine size or magazine behavior, please set ForceMagazineOverride to true in the config.
* More information about my Rust projects, free releases, and other Blackmatic projects can be found on my official website: blackmatic.de
*/

/* v 1.0.9
* Added visual-only Raid Rank System and Admin Balance Stats.
* Raid Rank System can be disabled in config, while admin statistics are always tracked.
* Fixed normal rockets using last tier damage.
* Fixed: Weapons Turbo compatibility issue causing excess rockets to be consumed on Rocketlauncher Tier and Prototype Rocketlauncher reload.
*/

/* v 1.0.8  
* Update Fix.
*/

/* v 1.0.7  
* Added ForceRocketDamageOverride - Optional protection if another plugin resets the rocket damage.
* Fixed Rocket Launcher damage at close range.
* Fixed: Rocket tier and Prototype Rocket Launcher damage is now reliably enforced, even if other plugins try to modify it.
* Fixed: TierC4, Prototype C4, Rocket tier and Prototype Rocket Launcher damage now applies correctly to Bradley/Heli.
* Improved overall stability of the tier/prototype damage logic.
*/

/* v 1.0.6
* Fixed Prototype Rocketlauncher ConditionLossPerShot not being applied reliably.  
* Prototype Launcher durability loss is now handled when the rocket is spawned, matching normal launcher behavior.
*/

/* v 1.0.5
* Added Prototype Rocketlauncher system (proto0–proto4) with its own damage values (Basic/HV) and tier stats
* Added Prototype C4 system (proto_c4_0–proto_c4_4) with its own damage values and tier stats
* Added Prototype upgrade system + costs for Launcher and C4 (optional stack upgrades)
* Added optional conversion: normal Rocketlauncher -> proto0 (costs in config)
* Added optional conversion: normal C4 -> proto_c4_0 (costs in config)
* Added /prototype (or /proto) UI with tabs, paging, Current -> Next preview and upgrade buttons
* Added LootSpawnsConfig: tier items/prototypes can spawn in loot containers with chance + rules + max extra items per container
* Added optional damage display in item names for normal C4/Launcher tiers (HV only shown if EnableHvRocketTiers = true)
*/

/* v 1.0.4
* Added ForceMagazineOverride - If the rocket launcher magazine size conflicts with another plugin, it can be enabled in the config.
*/

/* v 1.0.3
* Added HV Rocket tiers
* Added Rocket Launcher tier wear
* Added Rocket Launcher tier Magazine size
* Added clearer config structure
* Improved item recognition system for more reliable tier detection
*/

//More information about my Rust projects, free releases, and other Blackmatic projects can be found on my official website: blackmatic.de 

using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Rust;
using System;
using System.Collections.Generic;
using System.Reflection;
using Oxide.Game.Rust.Cui;
using System.Linq;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("CustomC4RocketTiers", "Blackmatic", "1.1.0")]
    [Description("Custom C4 and Rocketlauncher with variable damage + Prototype Rocketlauncher/C4 tiers.")]
    public class CustomC4RocketTiers : RustPlugin
    {
        private PluginConfig _config;
                [PluginReference] private Plugin ImageLibrary;


        private static Dictionary<ulong, string> _rocketTierData = new Dictionary<ulong, string>();
        private Dictionary<string, Queue<string>> _playerRocketTiers = new Dictionary<string, Queue<string>>();

        private readonly Dictionary<ulong, Timer> _magEnforceTimers = new Dictionary<ulong, Timer>();

        private readonly Dictionary<ulong, string> _prototypeLauncherTierByItem = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> _protoUiPageByPlayer = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> _protoUiTabByPlayer = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _prototypeC4TierByItem = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, PlayerLastTierInfo> _lastPlayerTierInfo = new Dictionary<ulong, PlayerLastTierInfo>();
        private StoredRaidStatsData _raidStatsData = new StoredRaidStatsData();
        private bool _raidStatsDirty;
        private bool _raidStatsSaveQueued;
        private const string RaidStatsDataFileName = "CustomC4RocketTiers_RaidStats";
        private const string UI_PROTO_MAIN = "CustomC4RT_ProtoUpgradeUI";

                private class PluginConfig
                {
                        public GeneralConfig General { get; set; } = new GeneralConfig();
                        public NameDisplayConfig NameDisplay { get; set; } = new NameDisplayConfig();

                        public BaseC4Config BaseC4 { get; set; } = new BaseC4Config();
                        public BaseRocketsConfig BaseRockets { get; set; } = new BaseRocketsConfig();
                        public NormalLauncherConfig NormalLauncher { get; set; } = new NormalLauncherConfig();

                        public PrototypeLauncherConfig PrototypeLauncher { get; set; } = new PrototypeLauncherConfig();
                        public PrototypeC4Config PrototypeC4 { get; set; } = new PrototypeC4Config();
                        
                        public LootSpawnsConfig LootSpawns { get; set; } = new LootSpawnsConfig();

                        public UiConfig UI { get; set; } = new UiConfig();
                        public RaidRankSystemConfig RaidRankSystem { get; set; } = new RaidRankSystemConfig();
                }

                private class GeneralConfig
                {
                        public bool ForceMagazineOverride { get; set; } = false;
                        public bool ForceRocketDamageOverride { get; set; } = false;

                        public bool EnableBasicRocketTiers { get; set; } = true;
                        public bool EnableHvRocketTiers { get; set; } = true;
                }

                private class NameDisplayConfig
                {
                        public bool ShowDamageInRocketLauncherTierName { get; set; } = true;
                        public bool ShowDamageInC4TierName { get; set; } = true;
                }

                private class BaseC4Config
                {
                        public Dictionary<string, C4Tier> C4Tiers { get; set; } = new Dictionary<string, C4Tier>();
                }

                private class BaseRocketsConfig
                {
                        public Dictionary<string, RocketTier> RocketTiers { get; set; } = new Dictionary<string, RocketTier>();
                        public Dictionary<string, RocketTier> RocketHvTiers { get; set; } = new Dictionary<string, RocketTier>();
                }

                private class NormalLauncherConfig
                {
                        public Dictionary<string, RocketLauncherTier> RocketLauncherTiers { get; set; } = new Dictionary<string, RocketLauncherTier>();
                }

                private class PrototypeLauncherConfig
                {
                        public bool EnablePrototypeLauncher { get; set; } = true;

                        public Dictionary<string, PrototypeLauncherTier> PrototypeLauncherTiers { get; set; } = new Dictionary<string, PrototypeLauncherTier>();
                        public Dictionary<string, PrototypeLauncherUpgradeEntry> PrototypeLauncherUpgradeConfig { get; set; } = new Dictionary<string, PrototypeLauncherUpgradeEntry>();

                        public bool AllowStackPrototypeUpgrades { get; set; } = true;

                        public bool AllowConvertNormalLauncherToPrototype { get; set; } = false;
                        public List<UpgradeCost> ConvertLauncherToProto0Costs { get; set; } = new List<UpgradeCost>();
                }

                private class PrototypeC4Config
                {
                        public bool EnablePrototypeC4 { get; set; } = true;

                        public Dictionary<string, PrototypeC4Tier> PrototypeC4Tiers { get; set; } = new Dictionary<string, PrototypeC4Tier>();
                        public Dictionary<string, PrototypeC4UpgradeEntry> PrototypeC4UpgradeConfig { get; set; } = new Dictionary<string, PrototypeC4UpgradeEntry>();

                        public bool AllowStackPrototypeC4Upgrades { get; set; } = true;

                        public bool AllowConvertNormalC4ToPrototype { get; set; } = false;
                        public List<UpgradeCost> ConvertC4ToProto0Costs { get; set; } = new List<UpgradeCost>();
                }

                private class UiConfig
                {
                        public string PrototypeUiImageName { get; set; } = "proto_launcher_bg";
                        public string PrototypeUiImageUrl { get; set; } = "https://codefling.com/uploads/monthly_2026_02/prototypev2.png.c80600aab383e3c00701cac63d5ac070.png";
                        public float PrototypeUiImageAlpha { get; set; } = 0.35f;
                }

                private class RaidRankSystemConfig
                {
                        public bool Enabled { get; set; } = true;
                        public bool ShowXpMessages { get; set; } = false;
                        public bool ShowRankUpMessages { get; set; } = true;
                        public int TopListAmount { get; set; } = 10;

                        public int XPForC4TierChargeUsed { get; set; } = 3;
                        public int XPForRocketlauncherTierShotFired { get; set; } = 2;
                        public int XPForPrototypeC4ChargeUsed { get; set; } = 5;
                        public int XPForPrototypeRocketlauncherShotFired { get; set; } = 5;
                }
        private class C4Tier
        {
            public float ExplosionDamage { get; set; }
            public ulong SkinId { get; set; }
            public string Info { get; set; }
        }

        private class RocketTier
        {
            public float ExplosionDamage { get; set; }
            public ulong SkinId { get; set; }
            public string Info { get; set; }
        }

        public class RocketTierComponent : MonoBehaviour
        {
            public string Tier;
            public bool IsHV;
            public ulong OwnerId;
            public string OwnerName;
            public string Info { get; set; }
            public float StoredDamage;
        }

        public class C4TierComponent : MonoBehaviour
        {
            public string Tier;
            public ulong OwnerId;
            public string OwnerName;
            public float StoredDamage;
        }

        private class RocketLauncherTier
        {
            public float ConditionLossPerShot { get; set; } 
            public int MagazineSize { get; set; } = 1;
            public string Info { get; set; }

            public string DisplayName { get; set; }
        }

        private class UpgradeCost
        {
            public string ShortName { get; set; }
            public int Amount { get; set; }
        }

        private class PrototypeLauncherTier
        {
            public string DisplayName { get; set; }
            public ulong SkinId { get; set; }

            public float BasicExplosionDamage { get; set; }
            public float HvExplosionDamage { get; set; }

            public float DamageMultiplier { get; set; }
            public int MagazineSize { get; set; }
            public float ConditionLossPerShot { get; set; }
            public string Info { get; set; }
        }

        private class PrototypeLauncherUpgradeEntry
        {
            public string ToTier { get; set; }
            public List<UpgradeCost> Costs { get; set; }
        }

        public class PrototypeLauncherShot : MonoBehaviour
        {
            public string TierId;
            public ulong OwnerId;
            public string OwnerName;
            public float StoredDamage;
        }

        private class PrototypeC4Tier
        {
            public float ExplosionDamage { get; set; }
            public ulong SkinId { get; set; }
            public string Info { get; set; }
        }

        private class PrototypeC4UpgradeEntry
        {
            public string ToTier { get; set; }
            public List<UpgradeCost> Costs { get; set; }
        }

        public class PrototypeC4Component : MonoBehaviour
        {
            public string TierId;
            public ulong OwnerId;
            public string OwnerName;
            public float StoredDamage;
        }

        private class PlayerLastTierInfo
        {
            public string LastRocketTier;
            public bool IsHV;
            public string ProtoLauncherTier;
            public float LastRocketDamage;
            public float LastC4Damage;
            public string LastC4Tier;
            public string LastProtoC4Tier;
        }

        private class StoredRaidStatsData
        {
            public Dictionary<ulong, PlayerRaidStats> Players { get; set; } = new Dictionary<ulong, PlayerRaidStats>();
        }

        private class PlayerRaidStats
        {
            public string LastName { get; set; }
            public int RaidXP { get; set; }

            public int PluginC4TierChargesUsed { get; set; }
            public int PluginRocketlauncherTierShotsFired { get; set; }
            public int PluginPrototypeC4ChargesUsed { get; set; }
            public int PluginPrototypeRocketlauncherShotsFired { get; set; }

            public float TotalPluginRaidDamage { get; set; }
            public float DamageToBuildings { get; set; }
            public float DamageToBradley { get; set; }
            public float DamageToPatrolHeli { get; set; }

            public Dictionary<string, int> C4TierUse { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> RocketlauncherTierUse { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> PrototypeC4Use { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> PrototypeLauncherUse { get; set; } = new Dictionary<string, int>();
        }
                
                private class LootSpawnsConfig
                {
                        public bool Enabled { get; set; } = false;

                        public List<string> ContainerPrefabs { get; set; } = new List<string>
                        {
                                "crate_normal_2",
                                "crate_elite",
                                "crate_toolbox",
                                "bradley_crate"
                        };

                        public int MaxExtraItemsPerContainer { get; set; } = 2;

                        public List<LootSpawnRule> Rules { get; set; } = new List<LootSpawnRule>
                        {
                                new LootSpawnRule { ItemType = "c4", Tier = "c4_tier1", Chance = 0.03f, MinAmount = 1, MaxAmount = 1 },
                                new LootSpawnRule { ItemType = "rocketlauncher", Tier = "rocket_tier1", Chance = 0.02f, MinAmount = 1, MaxAmount = 1 },
                                new LootSpawnRule { ItemType = "proto_launcher", Tier = "proto0", Chance = 0.005f, MinAmount = 1, MaxAmount = 1 },
                                new LootSpawnRule { ItemType = "proto_c4", Tier = "proto_c4_0", Chance = 0.005f, MinAmount = 1, MaxAmount = 1 }
                        };
                }

                private class LootSpawnRule
                {
                        public string ItemType { get; set; }
                        public string Tier { get; set; }
                        public float Chance { get; set; } = 0.01f;
                        public int MinAmount { get; set; } = 1;
                        public int MaxAmount { get; set; } = 1;
                }

        
				protected override void LoadDefaultConfig()
				{
					Config.WriteObject(new PluginConfig
					{
						General = new GeneralConfig
						{
							ForceMagazineOverride = false,
												ForceRocketDamageOverride = false,
							EnableBasicRocketTiers = true,
							EnableHvRocketTiers = true
						},

						NameDisplay = new NameDisplayConfig
						{
							ShowDamageInRocketLauncherTierName = true,
							ShowDamageInC4TierName = true
						},

						BaseC4 = new BaseC4Config
						{
							C4Tiers = new Dictionary<string, C4Tier>
							{
								["c4_tier1"] = new C4Tier
								{
									ExplosionDamage = 1000f,
									SkinId = 3425076124,
									Info = "Tier 1 C4"
								},
								["c4_tier2"] = new C4Tier
								{
									ExplosionDamage = 2000f,
									SkinId = 3425076601,
									Info = "Tier 2 C4"
								},
								["c4_tier3"] = new C4Tier
								{
									ExplosionDamage = 5000f,
									SkinId = 3425076743,
									Info = "Tier 3 C4"
								},
							}
						},

						BaseRockets = new BaseRocketsConfig
						{
							RocketTiers = new Dictionary<string, RocketTier>
							{
								["rocket_tier1"] = new RocketTier
								{
									ExplosionDamage = 500f,
									SkinId = 3447956979,
									Info = "Tier 1 basic rocket"
								},
								["rocket_tier2"] = new RocketTier
								{
									ExplosionDamage = 750f,
									SkinId = 3447957790,
									Info = "Tier 2 basic rocket"
								},
								["rocket_tier3"] = new RocketTier
								{
									ExplosionDamage = 1000f,
									SkinId = 3447954671,
									Info = "Tier 3 basic rocket"
								}
							},

							RocketHvTiers = new Dictionary<string, RocketTier>
							{
								["rocket_tier1"] = new RocketTier
								{
									ExplosionDamage = 250f,
									SkinId = 3447956979,
									Info = "Tier 1 HV rocket"
								},
								["rocket_tier2"] = new RocketTier
								{
									ExplosionDamage = 350f,
									SkinId = 3447957790,
									Info = "Tier 2 HV rocket"
								},
								["rocket_tier3"] = new RocketTier
								{
									ExplosionDamage = 450f,
									SkinId = 3447954671,
									Info = "Tier 3 HV rocket"
								}
							}
						},

						NormalLauncher = new NormalLauncherConfig
						{
							RocketLauncherTiers = new Dictionary<string, RocketLauncherTier>
							{
								["rocket_tier1"] = new RocketLauncherTier
								{
									ConditionLossPerShot = 10f,
									MagazineSize = 9,
									DisplayName = "Rocketlauncher T1",
									Info = "Rocketlauncher T1 | mag 9 | wear/shot 10"
								},
								["rocket_tier2"] = new RocketLauncherTier
								{
									ConditionLossPerShot = 6f,
									MagazineSize = 5,
									DisplayName = "Rocketlauncher T2",
									Info = "Rocketlauncher T2 | mag 5 | wear/shot 6"
								},
								["rocket_tier3"] = new RocketLauncherTier
								{
									ConditionLossPerShot = 3f,
									MagazineSize = 3,
									DisplayName = "Rocketlauncher T3",
									Info = "Rocketlauncher T3 | mag 3 | wear/shot 3"
								}
							}
						},

						PrototypeLauncher = new PrototypeLauncherConfig
						{
							EnablePrototypeLauncher = true,

							PrototypeLauncherTiers = new Dictionary<string, PrototypeLauncherTier>
							{
								["proto0"] = new PrototypeLauncherTier
								{
									DisplayName = "Prototype Rocketlauncher 0",
									SkinId = 3667143033,
									BasicExplosionDamage = 500f,
									HvExplosionDamage = 250f,
									DamageMultiplier = 0f,
									MagazineSize = 1,
									ConditionLossPerShot = 0f,
									Info = "Baseline prototype shell with unmodified firing chamber. It behaves like a standard launcher while engineers collect clean baseline data from your raids."
								},
								["proto1"] = new PrototypeLauncherTier
								{
									DisplayName = "Prototype Rocketlauncher I",
									SkinId = 3667165620,
									BasicExplosionDamage = 800f,
									HvExplosionDamage = 350f,
									DamageMultiplier = 0f,
									MagazineSize = 1,
									ConditionLossPerShot = 8f,
									Info = "First field-tuned prototype. Additional sensors tweak propellant venting, slightly increasing impact yield with almost no extra risk."
								},
								["proto2"] = new PrototypeLauncherTier
								{
									DisplayName = "Prototype Rocketlauncher II",
									SkinId = 3667167584,
									BasicExplosionDamage = 900f,
									HvExplosionDamage = 450f,
									DamageMultiplier = 0f,
									MagazineSize = 2,
									ConditionLossPerShot = 7f,
									Info = "Reinforced rails and experimental chamber lining let the launcher handle hotter propellant mixes for harder breaches."
								},
								["proto3"] = new PrototypeLauncherTier
								{
									DisplayName = "Prototype Rocketlauncher III",
									SkinId = 3667169403,
									BasicExplosionDamage = 1100f,
									HvExplosionDamage = 600f,
									DamageMultiplier = 0f,
									MagazineSize = 3,
									ConditionLossPerShot = 6f,
									Info = "Now considered highly unstable by its designers, this version pushes most safety margins into raw wall-breaking power."
								},
								["proto4"] = new PrototypeLauncherTier
								{
									DisplayName = "Prototype Rocketlauncher IV",
									SkinId = 3667188067,
									BasicExplosionDamage = 1500f,
									HvExplosionDamage = 760f,
									DamageMultiplier = 0f,
									MagazineSize = 4,
									ConditionLossPerShot = 5f,
									Info = "The final \"do not deploy\" prototype. According to lab records it was never officially tested on live structures… until now."
								}
							},

							PrototypeLauncherUpgradeConfig = new Dictionary<string, PrototypeLauncherUpgradeEntry>
							{
								["proto0"] = new PrototypeLauncherUpgradeEntry
								{
									ToTier = "proto1",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "metal.fragments", Amount = 750 },
										new UpgradeCost { ShortName = "gunpowder", Amount = 300 }
									}
								},
								["proto1"] = new PrototypeLauncherUpgradeEntry
								{
									ToTier = "proto2",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "metal.refined", Amount = 80 },
										new UpgradeCost { ShortName = "explosives", Amount = 10 }
									}
								},
								["proto2"] = new PrototypeLauncherUpgradeEntry
								{
									ToTier = "proto3",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "metal.refined", Amount = 150 },
										new UpgradeCost { ShortName = "explosives", Amount = 20 }
									}
								},
								["proto3"] = new PrototypeLauncherUpgradeEntry
								{
									ToTier = "proto4",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "metal.refined", Amount = 250 },
										new UpgradeCost { ShortName = "explosives", Amount = 40 }
									}
								}
							},

							AllowStackPrototypeUpgrades = true,
							AllowConvertNormalLauncherToPrototype = false,
							ConvertLauncherToProto0Costs = new List<UpgradeCost>
							{
								new UpgradeCost { ShortName = "scrap", Amount = 500 },
								new UpgradeCost { ShortName = "metal.refined", Amount = 40 }
							}
						},

						PrototypeC4 = new PrototypeC4Config
						{
							EnablePrototypeC4 = true,

							PrototypeC4Tiers = new Dictionary<string, PrototypeC4Tier>
							{
								["proto_c4_0"] = new PrototypeC4Tier
								{
									ExplosionDamage = 500f,
									SkinId = 3667082709,
									Info = "Prototype C4 0 – baseline charge. Engineers logged this batch as safe enough for field handling."
								},
								["proto_c4_1"] = new PrototypeC4Tier
								{
									ExplosionDamage = 700f,
									SkinId = 3667082876,
									Info = "Prototype C4 I – pressure-stabilized blend with improved brisance."
								},
								["proto_c4_2"] = new PrototypeC4Tier
								{
									ExplosionDamage = 800f,
									SkinId = 3667083073,
									Info = "Prototype C4 II – denser packing and hotter detonation front."
								},
								["proto_c4_3"] = new PrototypeC4Tier
								{
									ExplosionDamage = 950f,
									SkinId = 3667083247,
									Info = "Prototype C4 III – volatile primer chain. Do not store near heat."
								},
								["proto_c4_4"] = new PrototypeC4Tier
								{
									ExplosionDamage = 1400f,
									SkinId = 3667083408,
									Info = "Prototype C4 IV – lab-only formulation, now somehow in your hands."
								}
							},

							PrototypeC4UpgradeConfig = new Dictionary<string, PrototypeC4UpgradeEntry>
							{
								["proto_c4_0"] = new PrototypeC4UpgradeEntry
								{
									ToTier = "proto_c4_1",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "scrap", Amount = 250 },
										new UpgradeCost { ShortName = "metal.fragments", Amount = 500 }
									}
								},
								["proto_c4_1"] = new PrototypeC4UpgradeEntry
								{
									ToTier = "proto_c4_2",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "scrap", Amount = 350 },
										new UpgradeCost { ShortName = "metal.refined", Amount = 20 }
									}
								},
								["proto_c4_2"] = new PrototypeC4UpgradeEntry
								{
									ToTier = "proto_c4_3",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "scrap", Amount = 500 },
										new UpgradeCost { ShortName = "explosives", Amount = 5 }
									}
								},
								["proto_c4_3"] = new PrototypeC4UpgradeEntry
								{
									ToTier = "proto_c4_4",
									Costs = new List<UpgradeCost>
									{
										new UpgradeCost { ShortName = "scrap", Amount = 750 },
										new UpgradeCost { ShortName = "explosives", Amount = 10 }
									}
								}
							},

							AllowStackPrototypeC4Upgrades = true,
							AllowConvertNormalC4ToPrototype = false,
							ConvertC4ToProto0Costs = new List<UpgradeCost>
							{
								new UpgradeCost { ShortName = "scrap", Amount = 250 }
							}
						},
										
										LootSpawns = new LootSpawnsConfig
										{
												Enabled = false,
												ContainerPrefabs = new List<string>
												{
														"crate_normal_2",
														"crate_elite",
														"crate_toolbox",
														"bradley_crate"
												},
												MaxExtraItemsPerContainer = 2,
												Rules = new List<LootSpawnRule>
												{
														new LootSpawnRule { ItemType = "c4", Tier = "c4_tier1", Chance = 0.03f, MinAmount = 1, MaxAmount = 1 },
														new LootSpawnRule { ItemType = "rocketlauncher", Tier = "rocket_tier1", Chance = 0.02f, MinAmount = 1, MaxAmount = 1 },
														new LootSpawnRule { ItemType = "proto_launcher", Tier = "proto0", Chance = 0.005f, MinAmount = 1, MaxAmount = 1 },
														new LootSpawnRule { ItemType = "proto_c4", Tier = "proto_c4_0", Chance = 0.005f, MinAmount = 1, MaxAmount = 1 }
												}
										},

						UI = new UiConfig
						{
							PrototypeUiImageName = "proto_launcher_bg",
							PrototypeUiImageUrl = "https://codefling.com/uploads/monthly_2026_02/prototypev2.png.c80600aab383e3c00701cac63d5ac070.png",
							PrototypeUiImageAlpha = 0.35f
						},

						RaidRankSystem = new RaidRankSystemConfig
						{
							Enabled = true,
							ShowXpMessages = false,
							ShowRankUpMessages = true,
							TopListAmount = 10,
							XPForC4TierChargeUsed = 3,
							XPForRocketlauncherTierShotFired = 2,
							XPForPrototypeC4ChargeUsed = 5,
							XPForPrototypeRocketlauncherShotFired = 5
						}

					}, true);
				}
                
				private void EnsureConfigDefaults()
				{
					if (_config == null)
						_config = new PluginConfig();

					if (_config.General == null)
						_config.General = new GeneralConfig();

					if (_config.NameDisplay == null)
						_config.NameDisplay = new NameDisplayConfig();

					if (_config.BaseC4 == null)
						_config.BaseC4 = new BaseC4Config();

					if (_config.BaseC4.C4Tiers == null)
						_config.BaseC4.C4Tiers = new Dictionary<string, C4Tier>();

					if (_config.BaseRockets == null)
						_config.BaseRockets = new BaseRocketsConfig();

					if (_config.BaseRockets.RocketTiers == null)
						_config.BaseRockets.RocketTiers = new Dictionary<string, RocketTier>();

					if (_config.BaseRockets.RocketHvTiers == null)
						_config.BaseRockets.RocketHvTiers = new Dictionary<string, RocketTier>();

					if (_config.NormalLauncher == null)
						_config.NormalLauncher = new NormalLauncherConfig();

					if (_config.NormalLauncher.RocketLauncherTiers == null)
						_config.NormalLauncher.RocketLauncherTiers = new Dictionary<string, RocketLauncherTier>();

					if (_config.PrototypeLauncher == null)
						_config.PrototypeLauncher = new PrototypeLauncherConfig();

					if (_config.PrototypeLauncher.PrototypeLauncherTiers == null)
						_config.PrototypeLauncher.PrototypeLauncherTiers = new Dictionary<string, PrototypeLauncherTier>();

					if (_config.PrototypeLauncher.PrototypeLauncherUpgradeConfig == null)
						_config.PrototypeLauncher.PrototypeLauncherUpgradeConfig = new Dictionary<string, PrototypeLauncherUpgradeEntry>();

					if (_config.PrototypeLauncher.ConvertLauncherToProto0Costs == null)
						_config.PrototypeLauncher.ConvertLauncherToProto0Costs = new List<UpgradeCost>();

					if (_config.PrototypeC4 == null)
						_config.PrototypeC4 = new PrototypeC4Config();

					if (_config.PrototypeC4.PrototypeC4Tiers == null)
						_config.PrototypeC4.PrototypeC4Tiers = new Dictionary<string, PrototypeC4Tier>();

					if (_config.PrototypeC4.PrototypeC4UpgradeConfig == null)
						_config.PrototypeC4.PrototypeC4UpgradeConfig = new Dictionary<string, PrototypeC4UpgradeEntry>();

					if (_config.PrototypeC4.ConvertC4ToProto0Costs == null)
						_config.PrototypeC4.ConvertC4ToProto0Costs = new List<UpgradeCost>();

					if (_config.LootSpawns == null)
						_config.LootSpawns = new LootSpawnsConfig();

					if (_config.LootSpawns.ContainerPrefabs == null)
						_config.LootSpawns.ContainerPrefabs = new List<string>();

					if (_config.LootSpawns.Rules == null)
						_config.LootSpawns.Rules = new List<LootSpawnRule>();

					if (_config.LootSpawns.MaxExtraItemsPerContainer <= 0)
						_config.LootSpawns.MaxExtraItemsPerContainer = 2;

					if (_config.UI == null)
						_config.UI = new UiConfig();

					if (_config.RaidRankSystem == null)
						_config.RaidRankSystem = new RaidRankSystemConfig();

					if (_config.RaidRankSystem.TopListAmount <= 0)
						_config.RaidRankSystem.TopListAmount = 10;

					if (_config.RaidRankSystem.XPForC4TierChargeUsed <= 0)
						_config.RaidRankSystem.XPForC4TierChargeUsed = 3;

					if (_config.RaidRankSystem.XPForRocketlauncherTierShotFired <= 0)
						_config.RaidRankSystem.XPForRocketlauncherTierShotFired = 2;

					if (_config.RaidRankSystem.XPForPrototypeC4ChargeUsed <= 0)
						_config.RaidRankSystem.XPForPrototypeC4ChargeUsed = 5;

					if (_config.RaidRankSystem.XPForPrototypeRocketlauncherShotFired <= 0)
						_config.RaidRankSystem.XPForPrototypeRocketlauncherShotFired = 5;
				}
                
				protected override void LoadConfig()
				{
					base.LoadConfig();

					try
					{
						_config = Config.ReadObject<PluginConfig>();
					}
					catch
					{
						PrintWarning("Config file is corrupted, creating a new one.");
						LoadDefaultConfig();
						_config = Config.ReadObject<PluginConfig>();
					}

					EnsureConfigDefaults();
					SaveConfig();
				}

                private Item FindItemByUid(BasePlayer player, ulong uid)
                {
                        if (player?.inventory == null) return null;

                        var containers = new[] { player.inventory.containerMain, player.inventory.containerBelt, player.inventory.containerWear };
                        foreach (var c in containers)
                        {
                                if (c == null) continue;
                                foreach (var it in c.itemList)
                                        if (it != null && it.uid.Value == uid)
                                                return it;
                        }
                        return null;
                }

                private List<Item> GetNormalRocketLaunchers(BasePlayer player)
                {
                        var list = new List<Item>();
                        if (player?.inventory == null) return list;

                        foreach (var c in new[] { player.inventory.containerMain, player.inventory.containerBelt })
                        {
                                if (c == null) continue;
                                foreach (var it in c.itemList)
                                {
                                        if (it?.info == null) continue;
                                        if (it.info.shortname != "rocket.launcher") continue;
                                        if (IsPrototypeLauncher(it)) continue;
                                        list.Add(it);
                                }
                        }
                        return list;
                }

                private List<Item> GetNormalC4(BasePlayer player)
                {
                        var list = new List<Item>();
                        if (player?.inventory == null) return list;

                        foreach (var c in new[] { player.inventory.containerMain, player.inventory.containerBelt })
                        {
                                if (c == null) continue;
                                foreach (var it in c.itemList)
                                {
                                        if (it?.info == null) continue;
                                        if (it.info.shortname != "explosive.timed") continue;

                                        var tier = GetPrototypeC4TierFromItem(it);
                                        if (!string.IsNullOrEmpty(tier)) continue;

                                        list.Add(it);
                                }
                        }
                        return list;
                }

                private void EnsurePrototypeC4Name(Item item, string tier)
                {
                        if (item == null || item.info == null || item.info.shortname != "explosive.timed") return;
                        if (_config == null || !_config.PrototypeC4.EnablePrototypeC4) return;

                        if (_config.PrototypeC4.PrototypeC4Tiers == null || !_config.PrototypeC4.PrototypeC4Tiers.TryGetValue(tier, out var cfg) || cfg == null)
                                return;

                        var dmg = cfg.ExplosionDamage;
                        var desired = $"{tier.ToUpper()} [DMG:{dmg:0}]";

                        if (item.name == desired) return;

                        item.name = desired;
                        item.MarkDirty();
                }

                [ConsoleCommand("c4rt.convert.launcher")]
                private void CC_ConvertLauncher(ConsoleSystem.Arg arg)
                {
                        var player = arg.Player();
                        if (player == null) return;

                        if (_config == null || !_config.PrototypeLauncher.AllowConvertNormalLauncherToPrototype) return;

                        var uidStr = arg.GetString(0, null);
                        if (string.IsNullOrEmpty(uidStr) || !ulong.TryParse(uidStr, out var uid)) return;

                        var item = FindItemByUid(player, uid);
                        if (item == null) return;

                        TryConvertNormalLauncherToProto0(player, item);

                        _protoUiTabByPlayer[player.userID] = "launcher";
                        OpenPrototypeUpgradeUI(player);
                }

                [ConsoleCommand("c4rt.convert.c4")]
                private void CC_ConvertC4(ConsoleSystem.Arg arg)
                {
                        var player = arg.Player();
                        if (player == null) return;

                        if (_config == null || !_config.PrototypeC4.AllowConvertNormalC4ToPrototype) return;

                        var uidStr = arg.GetString(0, null);
                        if (string.IsNullOrEmpty(uidStr) || !ulong.TryParse(uidStr, out var uid)) return;

                        var item = FindItemByUid(player, uid);
                        if (item == null) return;

                        TryConvertNormalC4ToProto0(player, item);

                        _protoUiTabByPlayer[player.userID] = "c4";
                        OpenPrototypeC4UI(player);
                }

                private void TryConvertNormalLauncherToProto0(BasePlayer player, Item item)
                {
                        if (player == null || item == null || item.info == null) return;
                        if (_config == null) return;

                        if (!_config.PrototypeLauncher.AllowConvertNormalLauncherToPrototype)
                        {
                                SendReply(player, "<color=red>Launcher conversion is disabled.</color>");
                                return;
                        }

                        if (item.info.shortname != "rocket.launcher")
                        {
                                SendReply(player, "<color=yellow>Hold a normal Rocket Launcher (or click it in UI).</color>");
                                return;
                        }

                        if (IsPrototypeLauncher(item))
                        {
                                SendReply(player, "<color=yellow>This launcher is already a Prototype.</color>");
                                return;
                        }

                        var costs = _config.PrototypeLauncher.ConvertLauncherToProto0Costs;
                        if (costs != null && costs.Count > 0)
                        {
                                var missing = new List<string>();
                                foreach (var cost in costs)
                                {
                                        var def = ItemManager.FindItemDefinition(cost.ShortName);
                                        if (def == null)
                                        {
                                                missing.Add($"{cost.Amount}x {cost.ShortName} (invalid)");
                                                continue;
                                        }
                                        int have = player.inventory.GetAmount(def.itemid);
                                        if (have < cost.Amount)
                                                missing.Add($"{cost.Amount}x {cost.ShortName} (you have {have})");
                                }

                                if (missing.Count > 0)
                                {
                                        SendReply(player, "<color=red>Not enough resources to convert:</color>");
                                        foreach (var line in missing) SendReply(player, "<color=orange>- " + line + "</color>");
                                        return;
                                }

                                foreach (var cost in costs)
                                {
                                        var def = ItemManager.FindItemDefinition(cost.ShortName);
                                        if (def == null) continue;
                                        player.inventory.Take(null, def.itemid, cost.Amount);
                                }
                        }

                        _prototypeLauncherTierByItem[item.uid.Value] = "proto0";

                        if (_config.PrototypeLauncher.PrototypeLauncherTiers != null &&
                                _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue("proto0", out var cfg) &&
                                cfg != null)
                        {
                                if (cfg.SkinId != 0) item.skin = cfg.SkinId;
                        }

                        EnsurePrototypeLauncherName(item, "proto0");
                        item.MarkDirty();
                        ApplyHeldEntitySkin(item);

                        SendReply(player, "<color=green>Converted Rocket Launcher to Prototype (proto0).</color>");
                }

                private void TryConvertNormalC4ToProto0(BasePlayer player, Item item)
                {
                        if (player == null || item == null || item.info == null) return;
                        if (_config == null) return;

                        if (!_config.PrototypeC4.AllowConvertNormalC4ToPrototype)
                        {
                                SendReply(player, "<color=red>C4 conversion is disabled.</color>");
                                return;
                        }

                        if (item.info.shortname != "explosive.timed")
                        {
                                SendReply(player, "<color=yellow>Hold normal C4 (or click it in UI).</color>");
                                return;
                        }

                        var existing = GetPrototypeC4TierFromItem(item);
                        if (!string.IsNullOrEmpty(existing))
                        {
                                SendReply(player, "<color=yellow>This C4 is already Prototype C4.</color>");
                                return;
                        }

                        int units = (_config.PrototypeC4.AllowStackPrototypeC4Upgrades && item.amount > 1) ? item.amount : 1;

                        var costs = _config.PrototypeC4.ConvertC4ToProto0Costs;
                        if (costs != null && costs.Count > 0)
                        {
                                var missing = new List<string>();
                                foreach (var cost in costs)
                                {
                                        var def = ItemManager.FindItemDefinition(cost.ShortName);
                                        if (def == null)
                                        {
                                                missing.Add($"{cost.Amount * units}x {cost.ShortName} (invalid)");
                                                continue;
                                        }
                                        int need = cost.Amount * units;
                                        int have = player.inventory.GetAmount(def.itemid);
                                        if (have < need)
                                                missing.Add($"{need}x {cost.ShortName} (you have {have})");
                                }

                                if (missing.Count > 0)
                                {
                                        SendReply(player, "<color=red>Not enough resources to convert:</color>");
                                        foreach (var line in missing) SendReply(player, "<color=orange>- " + line + "</color>");
                                        return;
                                }

                                foreach (var cost in costs)
                                {
                                        var def = ItemManager.FindItemDefinition(cost.ShortName);
                                        if (def == null) continue;
                                        int need = cost.Amount * units;
                                        player.inventory.Take(null, def.itemid, need);
                                }
                        }

                        var tierId = "proto_c4_0";
                        _prototypeC4TierByItem[item.uid.Value] = tierId;

                        if (_config.PrototypeC4.PrototypeC4Tiers != null &&
                                _config.PrototypeC4.PrototypeC4Tiers.TryGetValue(tierId, out var cfg) &&
                                cfg != null)
                        {
                                if (cfg.SkinId != 0) item.skin = cfg.SkinId;
                                EnsurePrototypeC4Name(item, tierId);
                        }
                        else
                        {
                                item.MarkDirty();
                        }

                        SendReply(player, $"<color=green>Converted C4 to {tierId.ToUpper()}.</color>");
                }


				private void Init()
				{
					EnsureConfigDefaults();
					LoadRaidStatsData();

					permission.RegisterPermission("customc4rockettiers.admin", this);
				}
                
                private IEnumerable<Item> GetAllItems(BasePlayer player)
                {
                        if (player?.inventory == null) yield break;

                        var main = player.inventory.containerMain?.itemList;
                        if (main != null) foreach (var i in main) yield return i;

                        var belt = player.inventory.containerBelt?.itemList;
                        if (belt != null) foreach (var i in belt) yield return i;

                        var wear = player.inventory.containerWear?.itemList;
                        if (wear != null) foreach (var i in wear) yield return i;
                }

				[ChatCommand("givec4")]
				private void GiveC4Command(BasePlayer player, string command, string[] args)
				{
					if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
					{
						SendReply(player, "<color=red>No permission!</color>");
						return;
					}
					if (args.Length < 1)
					{
						SendReply(player, "Usage: <color=yellow>/givec4 [tier1|tier2|tier3]</color>");
						return;
					}

					string rawArg = args[0].ToLower();

					if (rawArg.StartsWith("proto"))
					{
						GivePrototypeLauncherTier(player, player, rawArg);
						return;
					}

					if (rawArg.StartsWith("rocketlauncher") || rawArg.StartsWith("rl_"))
					{
						var mapped = MapLauncherAliasToRocketTier(rawArg);
						if (string.IsNullOrEmpty(mapped))
						{
							SendReply(player, "<color=red>Invalid launcher tier!</color>");
							return;
						}
						args = new[] { mapped.Replace("rocket_tier", "tier") };
					}

					string tierArg = args[0].ToLower();

					string tierId  = "c4_" + tierArg;

					if (_config.BaseC4.C4Tiers == null || !_config.BaseC4.C4Tiers.ContainsKey(tierId))
					{
						SendReply(player, "<color=red>Invalid tier!</color>");
						return;
					}

					Item item = ItemManager.CreateByName("explosive.timed", 1);

					string displayName = tierId.ToUpper();
					item.name = displayName;

					var cfg = _config.BaseC4.C4Tiers[tierId];
					if (cfg != null && cfg.SkinId != 0)
						item.skin = cfg.SkinId;

					item.MarkDirty();
					player.inventory.GiveItem(item);

					NextTick(() =>
					{
						if (item != null && item.GetRootContainer() != null)
						{
							item.name = displayName;

							if (cfg != null && cfg.SkinId != 0)
								item.skin = cfg.SkinId;

							item.MarkDirty();

							EnsureC4TierName(item);
						}
					});

					SendReply(player, $"<color=green>You have received {displayName}!</color>");
				}
						
						private void Unload()
						{
								foreach (var t in _magEnforceTimers.Values)
										t?.Destroy();
								_magEnforceTimers.Clear();

								_prototypeLauncherTierByItem.Clear();
								_prototypeC4TierByItem.Clear();
								_rocketTierData.Clear();
								_protoUiPageByPlayer.Clear();
								_protoUiTabByPlayer.Clear();
								_lastPlayerTierInfo.Clear();
								SaveRaidStatsData();

								foreach (var p in BasePlayer.activePlayerList)
										DestroyPrototypeUI(p);
						}

				[ConsoleCommand("customc4rockettiers.givec4")]
				private void CC_GiveC4(ConsoleSystem.Arg arg)
				{
					if (arg.Args == null || arg.Args.Length < 2)
					{
						arg.ReplyWith("Usage: customc4rockettiers.givec4 <player|steamid> <tier1|tier2|tier3> [amount]");
						return;
					}

					string targetArg = arg.Args[0].ToString();
					string tierArg = arg.Args[1].ToString().ToLower();
					int amount = 1;
					if (arg.Args.Length >= 3 && !int.TryParse(arg.Args[2].ToString(), out amount))
						amount = 1;

					var target = BasePlayer.Find(targetArg) ?? BasePlayer.FindSleeping(targetArg);
					if (target == null)
					{
						arg.ReplyWith($"Player '{targetArg}' not found.");
						return;
					}

					string tierId = "c4_" + tierArg;
					if (_config.BaseC4.C4Tiers == null || !_config.BaseC4.C4Tiers.ContainsKey(tierId))
					{
						arg.ReplyWith("Invalid C4 tier.");
						return;
					}

					var cfg = _config.BaseC4.C4Tiers[tierId];
					for (int i = 0; i < amount; i++)
					{
						Item item = ItemManager.CreateByName("explosive.timed", 1);
						if (item == null) continue;

						string displayName = tierId.ToUpper();
						item.name = displayName;
						if (cfg != null && cfg.SkinId != 0)
							item.skin = cfg.SkinId;

						item.MarkDirty();
						target.inventory.GiveItem(item);

						NextTick(() =>
						{
							if (item != null && item.GetRootContainer() != null)
							{
								item.name = displayName;
								if (cfg != null && cfg.SkinId != 0)
									item.skin = cfg.SkinId;

								item.MarkDirty();
								EnsureC4TierName(item);
							}
						});
					}

					arg.ReplyWith($"Gave {amount}x {tierId.ToUpper()} to {target.displayName}.");
				}

				[ChatCommand("giverocket")]
				private void GiveRocketCommand(BasePlayer player, string command, string[] args)
				{
					if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
					{
						SendReply(player, "<color=red>No permission!</color>");
						return;
					}
					if (args.Length < 1)
					{
						SendReply(player, "Usage: <color=yellow>/giverocket [tier1|tier2|tier3]</color>");
						return;
					}

					string tierArg = args[0].ToLower();
					string tierId = "rocket_tier" + tierArg.Replace("tier", "");
					if (!_config.BaseRockets.RocketTiers.ContainsKey(tierId))
					{
						SendReply(player, "<color=red>Invalid tier!</color>");
						return;
					}

					Item item = ItemManager.CreateByName("rocket.launcher", 1);
					_rocketTierData[item.uid.Value] = tierId;

					if (!_playerRocketTiers.ContainsKey(player.UserIDString))
						_playerRocketTiers[player.UserIDString] = new Queue<string>();
					_playerRocketTiers[player.UserIDString].Enqueue(tierId);

					string displayName = GetRocketLauncherDisplayName(tierId);
					item.name = displayName;

					var rocketTier = _config.BaseRockets.RocketTiers[tierId];
					if (rocketTier.SkinId != 0)
						item.skin = rocketTier.SkinId;

					if (item.hasCondition)
					{
						item.maxCondition = Math.Max(item.maxCondition, 100f);
						item.condition = item.maxCondition;
					}

					item.MarkDirty();
					player.inventory.GiveItem(item);

					NextTick(() =>
					{
						if (item != null && item.GetRootContainer() != null)
						{
							item.name = displayName;
							item.MarkDirty();

							ApplyHeldEntitySkin(item);
							ApplyLauncherTierMagazine(item, tierId);
							EnforceLauncherMagazine(item, tierId);
						}
					});

					SendReply(player, $"<color=green>You have received {displayName}!</color>");
				}

				[ConsoleCommand("customc4rockettiers.giverocket")]
				private void CC_GiveRocket(ConsoleSystem.Arg arg)
				{
					if (arg.Args == null || arg.Args.Length < 2)
					{
						arg.ReplyWith("Usage: customc4rockettiers.giverocket <player|steamid> <tier1|tier2|tier3> [amount]");
						return;
					}

					string targetArg = arg.Args[0].ToString();
					string tierArg = arg.Args[1].ToString().ToLower();
					int amount = 1;
					if (arg.Args.Length >= 3 && !int.TryParse(arg.Args[2].ToString(), out amount))
						amount = 1;

					var target = BasePlayer.Find(targetArg) ?? BasePlayer.FindSleeping(targetArg);
					if (target == null)
					{
						arg.ReplyWith($"Player '{targetArg}' not found.");
						return;
					}

					string tierId = "rocket_tier" + tierArg.Replace("tier", "");
					if (_config.BaseRockets.RocketTiers == null || !_config.BaseRockets.RocketTiers.ContainsKey(tierId))
					{
						arg.ReplyWith("Invalid rocket tier.");
						return;
					}

					var rocketTier = _config.BaseRockets.RocketTiers[tierId];
					for (int i = 0; i < amount; i++)
					{
						Item item = ItemManager.CreateByName("rocket.launcher", 1);
						if (item == null) continue;

						_rocketTierData[item.uid.Value] = tierId;

						string displayName = GetRocketLauncherDisplayName(tierId);
						item.name = displayName;

						if (rocketTier.SkinId != 0)
							item.skin = rocketTier.SkinId;

						if (item.hasCondition)
						{
							item.maxCondition = Math.Max(item.maxCondition, 100f);
							item.condition = item.maxCondition;
						}

						item.MarkDirty();
						target.inventory.GiveItem(item);

						NextTick(() =>
						{
							if (item != null && item.GetRootContainer() != null)
							{
								item.name = displayName;
								item.MarkDirty();

								ApplyHeldEntitySkin(item);
								ApplyLauncherTierMagazine(item, tierId);
								EnforceLauncherMagazine(item, tierId);
							}
						});
					}

					arg.ReplyWith($"Gave {amount}x {tierId.ToUpper()} rocket launcher to {target.displayName}.");
				}

				[ChatCommand("giveproto")]
				private void CmdGivePrototype(BasePlayer player, string command, string[] args)
				{
					if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
					{
						SendReply(player, "<color=red>No permission!</color>");
						return;
					}

					if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
					{
						SendReply(player, "<color=red>Prototype Rocketlauncher system is disabled.</color>");
						return;
					}

					BasePlayer target = player;
					if (args.Length >= 1)
					{
						var p = BasePlayer.Find(args[0]) ?? BasePlayer.FindSleeping(args[0]);
						if (p != null) target = p;
					}

					const string baseTierId = "proto0";

					if (_config.PrototypeLauncher.PrototypeLauncherTiers == null
						|| !_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(baseTierId, out var cfg)
						|| cfg == null)
					{
						SendReply(player, "<color=red>Prototype configuration 'proto0' not found.</color>");
						return;
					}

					var item = ItemManager.CreateByName("rocket.launcher", 1);
					if (item == null)
					{
						SendReply(player, "<color=red>Failed to create rocket launcher item.</color>");
						return;
					}

					_prototypeLauncherTierByItem[item.uid.Value] = baseTierId;

					EnsurePrototypeLauncherName(item, _prototypeLauncherTierByItem[item.uid.Value]);
					if (cfg.SkinId != 0)
						item.skin = cfg.SkinId;

					if (item.hasCondition)
					{
						item.maxCondition = Math.Max(item.maxCondition, 100f);
						item.condition = item.maxCondition;
					}

					item.MarkDirty();
					target.inventory.GiveItem(item);

					NextTick(() =>
					{
						if (item != null && item.GetRootContainer() != null)
						{
							EnsurePrototypeLauncherName(item, _prototypeLauncherTierByItem[item.uid.Value]);
							if (cfg.SkinId != 0)
								item.skin = cfg.SkinId;

							item.MarkDirty();
							ApplyHeldEntitySkin(item);
							ApplyPrototypeLauncherMagazine(item, baseTierId);
						}
					});

					SendReply(player, $"<color=green>Prototype Rocketlauncher 0 has been given to {target.displayName}.</color>");
					SendReply(target, "<color=#88c0ff>Hint:</color> Type <color=#a3be8c>/proto</color> to open the Prototype Rocketlauncher upgrade menu.");
				}

				[ConsoleCommand("customc4rockettiers.giveproto")]
				private void CC_GivePrototype(ConsoleSystem.Arg arg)
				{
					if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
					{
						arg.ReplyWith("Prototype Rocketlauncher system is disabled.");
						return;
					}

					if (arg.Args == null || arg.Args.Length < 1)
					{
						arg.ReplyWith("Usage: customc4rockettiers.giveproto <player|steamid>");
						return;
					}

					string targetArg = arg.Args[0].ToString();
					var target = BasePlayer.Find(targetArg) ?? BasePlayer.FindSleeping(targetArg);
					if (target == null)
					{
						arg.ReplyWith($"Player '{targetArg}' not found.");
						return;
					}

					const string baseTierId = "proto0";

					if (_config.PrototypeLauncher.PrototypeLauncherTiers == null
						|| !_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(baseTierId, out var cfg)
						|| cfg == null)
					{
						arg.ReplyWith("Prototype configuration 'proto0' not found.");
						return;
					}

					var item = ItemManager.CreateByName("rocket.launcher", 1);
					if (item == null)
					{
						arg.ReplyWith("Failed to create rocket launcher item.");
						return;
					}

					_prototypeLauncherTierByItem[item.uid.Value] = baseTierId;

					EnsurePrototypeLauncherName(item, _prototypeLauncherTierByItem[item.uid.Value]);
					if (cfg.SkinId != 0)
						item.skin = cfg.SkinId;

					if (item.hasCondition)
					{
						item.maxCondition = Math.Max(item.maxCondition, 100f);
						item.condition = item.maxCondition;
					}

					item.MarkDirty();
					target.inventory.GiveItem(item);

					NextTick(() =>
					{
						if (item != null && item.GetRootContainer() != null)
						{
							EnsurePrototypeLauncherName(item, _prototypeLauncherTierByItem[item.uid.Value]);
							if (cfg.SkinId != 0)
								item.skin = cfg.SkinId;

							item.MarkDirty();
							ApplyHeldEntitySkin(item);
							ApplyPrototypeLauncherMagazine(item, baseTierId);
						}
					});

					arg.ReplyWith($"Prototype Rocketlauncher 0 given to {target.displayName}.");
				}

				private void ApplyHeldEntitySkin(Item item)
				{
					if (item == null || item.info == null) return;

					var held = item.GetHeldEntity() as BaseEntity;
					if (held == null) return;

					ulong desired = item.skin;
					if (desired != 0 && held.skinID != desired)
					{
						held.skinID = desired;
						held.SendNetworkUpdate();
					}
				}
						
						private void EnsurePrototypeLauncherName(Item item, string tier)
						{
								if (item == null) return;
								if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher) return;

								if (!_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tier, out var cfg) || cfg == null)
										return;

								var basic = cfg.BasicExplosionDamage;
								var hv = cfg.HvExplosionDamage;

								var desired = $"{cfg.DisplayName} [Basic:{basic:0} | HV:{hv:0}]";

								if (item.name == desired) return;

								item.name = desired;
								item.MarkDirty();
						}

						private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
						{
							if (player == null || entity == null) return;

							var c4 = entity as TimedExplosive;
							if (c4 == null) return;

							var activeItem = player.GetActiveItem();
							if (activeItem == null || activeItem.info == null) return;
							if (activeItem.info.shortname != "explosive.timed") return;

							var protoTier = GetPrototypeC4TierFromItem(activeItem);
							if (!string.IsNullOrEmpty(protoTier) && _config != null && _config.PrototypeC4.EnablePrototypeC4)
							{
								var pcomp = c4.gameObject.GetComponent<PrototypeC4Component>();
								if (pcomp == null)
									pcomp = c4.gameObject.AddComponent<PrototypeC4Component>();

								pcomp.TierId = protoTier;
								pcomp.OwnerId = player.userID;
								pcomp.OwnerName = player.displayName;
								RecordPluginC4ChargeUsed(player, protoTier, true);

								if (_config.PrototypeC4.PrototypeC4Tiers != null &&
									_config.PrototypeC4.PrototypeC4Tiers.TryGetValue(protoTier, out var pcfg) &&
									pcfg != null)
								{
									float finalDamage = pcfg.ExplosionDamage * 2f;

									pcomp.StoredDamage = finalDamage;

									if (!_lastPlayerTierInfo.ContainsKey(player.userID))
										_lastPlayerTierInfo[player.userID] = new PlayerLastTierInfo();

									_lastPlayerTierInfo[player.userID].LastC4Damage = finalDamage;
									_lastPlayerTierInfo[player.userID].LastProtoC4Tier = protoTier;
									_lastPlayerTierInfo[player.userID].LastC4Tier = null;

									c4.damageTypes = new List<DamageTypeEntry>
									{
										new DamageTypeEntry
										{
											type = DamageType.Explosion,
											amount = finalDamage,
										}
									};

								}

								return;
							}

							string tierId = GetC4TierFromItem(activeItem);
							if (string.IsNullOrEmpty(tierId)) return;

							var comp = c4.gameObject.GetComponent<C4TierComponent>();
							if (comp == null)
								comp = c4.gameObject.AddComponent<C4TierComponent>();

							comp.Tier = tierId;
							comp.OwnerId = player.userID;
							comp.OwnerName = player.displayName;
							RecordPluginC4ChargeUsed(player, tierId, false);

							if (_config.BaseC4.C4Tiers != null &&
								_config.BaseC4.C4Tiers.TryGetValue(tierId, out var c4Tier) &&
								c4Tier != null)
							{
								float finalDamage = c4Tier.ExplosionDamage * 2f;

								comp.StoredDamage = finalDamage;

								if (!_lastPlayerTierInfo.ContainsKey(player.userID))
									_lastPlayerTierInfo[player.userID] = new PlayerLastTierInfo();

								_lastPlayerTierInfo[player.userID].LastC4Damage = finalDamage;
								_lastPlayerTierInfo[player.userID].LastC4Tier = tierId;
								_lastPlayerTierInfo[player.userID].LastProtoC4Tier = null;

								c4.damageTypes = new List<DamageTypeEntry>
								{
									new DamageTypeEntry
									{
										type = DamageType.Explosion,
										amount = finalDamage,
									}
								};

							}
						}

				private void OnEntitySpawned(BaseEntity entity)
				{
					if (entity == null) return;

					var shortname = entity.ShortPrefabName.ToString();
					if (string.IsNullOrEmpty(shortname)) return;

					if (entity is TimedExplosive explosive)
					{
						var sn = shortname.ToLower();

						if (sn.Contains("rocket_basic") || sn.Contains("rocket_hv"))
						{
							RocketTierComponent comp = explosive.GetComponent<RocketTierComponent>();
							if (comp == null)
								comp = explosive.gameObject.AddComponent<RocketTierComponent>();

							comp.IsHV = sn.Contains("rocket_hv");

							string tier = null;
							string protoTierForStats = null;

							var sourceItem = explosive.GetItem();
							if (sourceItem != null)
							{
								_rocketTierData.TryGetValue(sourceItem.uid.Value, out tier);
							}

							if (string.IsNullOrEmpty(tier) && explosive.creatorEntity is BasePlayer owner)
							{
														var activeItem = owner.GetActiveItem();
														if (activeItem != null && activeItem.info.shortname == "rocket.launcher")
														{
																tier = ResolveRocketTierFromItem(activeItem);
																comp.OwnerId = owner.userID;
																comp.OwnerName = owner.displayName;

																if (!string.IsNullOrEmpty(tier) && _config.NormalLauncher.RocketLauncherTiers != null)
																{
																		if (_config.NormalLauncher.RocketLauncherTiers.TryGetValue(tier, out var wearCfg) && wearCfg != null)
																		{
																				if (activeItem.hasCondition && wearCfg.ConditionLossPerShot > 0f)
																				{
																						activeItem.condition = Mathf.Max(0f, activeItem.condition - wearCfg.ConditionLossPerShot);
																						activeItem.MarkDirty();
																				}
																		}
																}

																if (_config != null && _config.PrototypeLauncher.EnablePrototypeLauncher && IsPrototypeLauncher(activeItem))
																{
																		var protoTier = GetPrototypeTierFromItem(activeItem);
																		if (!string.IsNullOrEmpty(protoTier))
																		{
																				if (_config.PrototypeLauncher.PrototypeLauncherTiers != null &&
																						_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(protoTier, out var protoWearCfg) &&
																						protoWearCfg != null &&
																						activeItem.hasCondition &&
																						protoWearCfg.ConditionLossPerShot > 0f)
																				{
																						activeItem.condition = Mathf.Max(0f, activeItem.condition - protoWearCfg.ConditionLossPerShot);
																						activeItem.MarkDirty();
																				}

																				var shot = explosive.gameObject.GetComponent<PrototypeLauncherShot>()
																								   ?? explosive.gameObject.AddComponent<PrototypeLauncherShot>();
																				shot.TierId = protoTier;
																				shot.OwnerId = owner.userID;
																				shot.OwnerName = owner.displayName;
																				protoTierForStats = protoTier;
																		}
																}
														}

														if (!string.IsNullOrEmpty(protoTierForStats))
														{
																RecordPluginRocketShotFired(owner, protoTierForStats, true);
														}
														else if (!string.IsNullOrEmpty(tier))
														{
																RecordPluginRocketShotFired(owner, tier, false);
														}
												}
							comp.Tier = tier;

							bool isRealPluginRocket =
								!string.IsNullOrEmpty(tier) ||
								!string.IsNullOrEmpty(protoTierForStats) ||
								explosive.GetComponent<PrototypeLauncherShot>() != null;

							if (string.IsNullOrEmpty(tier) && isRealPluginRocket && explosive.creatorEntity is BasePlayer fallbackOwner)
							{
								if (_lastPlayerTierInfo.TryGetValue(fallbackOwner.userID, out var lastInfo) && lastInfo != null)
								{
									if (!string.IsNullOrEmpty(lastInfo.LastRocketTier))
									{
										tier = lastInfo.LastRocketTier;
										comp.Tier = tier;
									}
								}
							}

							float immediateDamage = CalculateRocketDamage(explosive, comp, tier);
							if (immediateDamage > 0f)
							{
								comp.StoredDamage = immediateDamage;
								SetExplosionDamageInDamageTypeList(explosive.damageTypes, immediateDamage);
							}

							if (explosive.creatorEntity is BasePlayer trackOwner && !string.IsNullOrEmpty(tier))
							{
								if (!_lastPlayerTierInfo.ContainsKey(trackOwner.userID))
									_lastPlayerTierInfo[trackOwner.userID] = new PlayerLastTierInfo();
								_lastPlayerTierInfo[trackOwner.userID].LastRocketTier = tier;
								_lastPlayerTierInfo[trackOwner.userID].LastRocketDamage = immediateDamage;
								_lastPlayerTierInfo[trackOwner.userID].IsHV = comp.IsHV;
							}

							timer.Once(0.2f, () => ModifyRocketDamage(explosive));
						}

						else
						{
							var comp = explosive.GetComponent<C4TierComponent>() ?? explosive.gameObject.AddComponent<C4TierComponent>();
							if (string.IsNullOrEmpty(comp.Tier))
							{
								var sourceItem = explosive.GetItem();
								if (sourceItem != null)
									comp.Tier = GetC4TierFromItem(sourceItem);
							}

							timer.Once(0.2f, () => ModifyExplosionDamage(explosive));
						}
					}
					else if (shortname.ToLower().Contains("rocket.launcher"))
					{
						timer.Once(0.2f, () => ModifyRocketDamage(entity));
					}
				}               

				private void ModifyExplosionDamage(TimedExplosive explosive)
				{
					if (explosive == null) return;

					var protoComp = explosive.GetComponent<PrototypeC4Component>();
					if (protoComp != null && !string.IsNullOrEmpty(protoComp.TierId) && _config != null && _config.PrototypeC4.EnablePrototypeC4)
					{
						if (_config.PrototypeC4.PrototypeC4Tiers != null && _config.PrototypeC4.PrototypeC4Tiers.TryGetValue(protoComp.TierId, out var pcfg) && pcfg != null)
						{
							SetExplosionDamageInDamageTypeList(explosive.damageTypes, pcfg.ExplosionDamage * 2f);
							return;
						}
					}

					var c4Comp = explosive.GetComponent<C4TierComponent>();
					if (c4Comp != null && !string.IsNullOrEmpty(c4Comp.Tier))
					{
						if (_config.BaseC4.C4Tiers.TryGetValue(c4Comp.Tier, out var c4Tier))
						{
							float modifiedDamage = c4Tier.ExplosionDamage * 2f;
							SetExplosionDamageInDamageTypeList(explosive.damageTypes, modifiedDamage);
						}
					}
				}

                private void ModifyRocketDamage(BaseEntity rocketEntity)
                {
                        if (rocketEntity == null) return;

                        var te = rocketEntity.GetComponent<TimedExplosive>();
                        if (te == null || te.damageTypes == null) return;

                        var comp = rocketEntity.GetComponent<RocketTierComponent>();
                        bool isHV = comp != null ? comp.IsHV : (rocketEntity.ShortPrefabName.ToString().ToLower().Contains("rocket_hv"));

                        if (_config != null && _config.PrototypeLauncher.EnablePrototypeLauncher)
                        {
                                var protoShot = rocketEntity.GetComponent<PrototypeLauncherShot>(); 
                                if (protoShot != null &&
                                        !string.IsNullOrEmpty(protoShot.TierId) &&
                                        _config.PrototypeLauncher.PrototypeLauncherTiers != null &&
                                        _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(protoShot.TierId, out var protoCfg) &&
                                        protoCfg != null)
                                {
                                        if (protoCfg.BasicExplosionDamage > 2f || protoCfg.HvExplosionDamage > 2f)
                                        {
                                                float protoDamage = 2f;

                                                if (isHV && protoCfg.HvExplosionDamage > 0f)
                                                        protoDamage = protoCfg.HvExplosionDamage * 2f;
                                                else if (!isHV && protoCfg.BasicExplosionDamage > 0f)
                                                        protoDamage = protoCfg.BasicExplosionDamage * 2f;

                                                if (protoDamage > 0f)
                                                {
                                                        SetExplosionDamageInDamageTypeList(te.damageTypes, protoDamage);
                                                        return;
                                                }
                                        }
                                }
                        }

                        string tierId = GetTierIdForRocket(rocketEntity);
                        if (string.IsNullOrEmpty(tierId)) return;

                        if (_config != null)
                        {
                                if (isHV && !_config.General.EnableHvRocketTiers)
                                        return;
                                if (!isHV && !_config.General.EnableBasicRocketTiers)
                                        return;
                        }

                        RocketTier rocketTier;
                        if (isHV)
                        {
                                if (!_config.BaseRockets.RocketHvTiers.TryGetValue(tierId, out rocketTier)) return;
                        }
                        else
                        {
                                if (!_config.BaseRockets.RocketTiers.TryGetValue(tierId, out rocketTier)) return;
                        }

                        float modifiedDamage = rocketTier.ExplosionDamage * 2f;

                        SetExplosionDamageInDamageTypeList(te.damageTypes, modifiedDamage);
                }

        private float CalculateRocketDamage(TimedExplosive explosive, RocketTierComponent comp, string tierId)
        {
            if (explosive == null) return 0f;

            bool isHV = comp != null ? comp.IsHV : (explosive.ShortPrefabName.ToString().ToLower().Contains("rocket_hv"));

            if (_config != null && _config.PrototypeLauncher.EnablePrototypeLauncher)
            {
                var protoShot = explosive.GetComponent<PrototypeLauncherShot>();
                if (protoShot != null &&
                    !string.IsNullOrEmpty(protoShot.TierId) &&
                    _config.PrototypeLauncher.PrototypeLauncherTiers != null &&
                    _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(protoShot.TierId, out var protoCfg) &&
                    protoCfg != null)
                {
                    if (protoCfg.BasicExplosionDamage > 2f || protoCfg.HvExplosionDamage > 2f)
                    {
                        float protoDamage = 2f;
                        if (isHV && protoCfg.HvExplosionDamage > 0f)
                            protoDamage = protoCfg.HvExplosionDamage * 2f;
                        else if (!isHV && protoCfg.BasicExplosionDamage > 0f)
                            protoDamage = protoCfg.BasicExplosionDamage * 2f;

                        if (protoDamage > 0f)
                        {
                            if (protoShot != null) protoShot.StoredDamage = protoDamage;
                            return protoDamage;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(tierId)) return 0f;

            if (_config != null)
            {
                if (isHV && !_config.General.EnableHvRocketTiers) return 0f;
                if (!isHV && !_config.General.EnableBasicRocketTiers) return 0f;
            }

            RocketTier rocketTier = null;
            if (isHV)
                _config.BaseRockets.RocketHvTiers.TryGetValue(tierId, out rocketTier);
            else
                _config.BaseRockets.RocketTiers.TryGetValue(tierId, out rocketTier);

            if (rocketTier == null) return 0f;

            float dmg = rocketTier.ExplosionDamage * 2f;

            if (_config != null && _config.PrototypeLauncher.EnablePrototypeLauncher)
            {
                var protoShot2 = explosive.GetComponent<PrototypeLauncherShot>();
                if (protoShot2 != null &&
                    _config.PrototypeLauncher.PrototypeLauncherTiers != null &&
                    _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(protoShot2.TierId, out var protoCfg2) &&
                    protoCfg2 != null)
                {
                    if (protoCfg2.BasicExplosionDamage > 2f || protoCfg2.HvExplosionDamage > 2f)
                    {
                        if (isHV && protoCfg2.HvExplosionDamage > 0f)
                            dmg = protoCfg2.HvExplosionDamage * 2f;
                        else if (!isHV && protoCfg2.BasicExplosionDamage > 0f)
                            dmg = protoCfg2.BasicExplosionDamage * 2f;
                    }
                    else
                    {
                        dmg *= protoCfg2.DamageMultiplier;
                    }
                }
            }

            return dmg;
        }

		private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
		{
			if (info?.Initiator == null) return null;

			string rocketSource = null;
			BaseEntity rocketEntity = null;

			if (info.WeaponPrefab != null && !string.IsNullOrEmpty(info.WeaponPrefab.ShortPrefabName.ToString()))
			{
				var wpName = info.WeaponPrefab.ShortPrefabName.ToString().ToLower();
				if (wpName.Contains("rocket"))
				{
					rocketSource = info.WeaponPrefab.ShortPrefabName.ToString();
					rocketEntity = info.WeaponPrefab;
				}
			}

			if (rocketSource == null && info.Weapon != null && !string.IsNullOrEmpty(info.Weapon.ShortPrefabName.ToString()))
			{
				var weaponName = info.Weapon.ShortPrefabName.ToString().ToLower();
				if (weaponName.Contains("rocket"))
				{
					rocketSource = info.Weapon.ShortPrefabName.ToString();
					rocketEntity = info.Weapon as BaseEntity;
				}
			}

			if (rocketSource == null && info.Initiator != null)
			{
				var initName = info.Initiator.ShortPrefabName.ToString().ToLower();
				if (initName.Contains("rocket_basic") || initName.Contains("rocket_hv"))
				{
					rocketSource = info.Initiator.ShortPrefabName.ToString();
					rocketEntity = info.Initiator;
				}
			}

			if (rocketSource != null)
			{
				var rocketSourceLower = rocketSource.ToLower();
				float dmg = 0f;

				BaseEntity compEntity = rocketEntity ?? info.Initiator;

				var rocketComp = compEntity?.GetComponent<RocketTierComponent>();
				if (rocketComp == null && compEntity != info.Initiator)
					rocketComp = info.Initiator?.GetComponent<RocketTierComponent>();

				if (rocketComp != null && rocketComp.StoredDamage > 0f)
				{
					dmg = rocketComp.StoredDamage;
				}

				var protoShotComp = compEntity?.GetComponent<PrototypeLauncherShot>();
				if (protoShotComp == null && compEntity != info.Initiator)
					protoShotComp = info.Initiator?.GetComponent<PrototypeLauncherShot>();

				if (protoShotComp != null && protoShotComp.StoredDamage > 0f)
				{
					dmg = protoShotComp.StoredDamage;
				}

				if (dmg <= 0f)
				{
					string tierId = GetTierIdForRocket(compEntity);
					if (string.IsNullOrEmpty(tierId) && compEntity != info.Initiator)
						tierId = GetTierIdForRocket(info.Initiator);

					if (!string.IsNullOrEmpty(tierId))
					{
						bool isHV = rocketComp != null ? rocketComp.IsHV : rocketSourceLower.Contains("rocket_hv");

						if (_config != null)
						{
							if (isHV && !_config.General.EnableHvRocketTiers) return null;
							if (!isHV && !_config.General.EnableBasicRocketTiers) return null;
						}

						RocketTier rocketTier = null;

						if (isHV)
							_config.BaseRockets.RocketHvTiers.TryGetValue(tierId, out rocketTier);
						else
							_config.BaseRockets.RocketTiers.TryGetValue(tierId, out rocketTier);

						if (rocketTier != null)
						{
							dmg = rocketTier.ExplosionDamage;

							if (_config != null && _config.PrototypeLauncher.EnablePrototypeLauncher)
							{
								var protoComp2 = protoShotComp;

								if (protoComp2 != null &&
									_config.PrototypeLauncher.PrototypeLauncherTiers != null &&
									_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(protoComp2.TierId, out var protoCfg) &&
									protoCfg != null)
								{
									if (protoCfg.BasicExplosionDamage > 0f || protoCfg.HvExplosionDamage > 0f)
									{
										if (isHV && protoCfg.HvExplosionDamage > 0f)
											dmg = protoCfg.HvExplosionDamage * 2f;
										else if (!isHV && protoCfg.BasicExplosionDamage > 0f)
											dmg = protoCfg.BasicExplosionDamage * 2f;
									}
									else
									{
										dmg *= protoCfg.DamageMultiplier;
									}
								}
							}
						}
					}
				}

				if (dmg <= 0f)
				{
					bool hasPluginRocketComponent =
						(rocketComp != null && (!string.IsNullOrEmpty(rocketComp.Tier) || rocketComp.StoredDamage > 0f)) ||
						(protoShotComp != null && (!string.IsNullOrEmpty(protoShotComp.TierId) || protoShotComp.StoredDamage > 0f)) ||
						!string.IsNullOrEmpty(GetTierIdForRocket(compEntity)) ||
						(compEntity != info.Initiator && !string.IsNullOrEmpty(GetTierIdForRocket(info.Initiator)));

					if (hasPluginRocketComponent)
					{
						BasePlayer rocketOwner = null;

						if (info.Initiator is BasePlayer directPlayer)
							rocketOwner = directPlayer;
						else
							rocketOwner = info.Initiator?.creatorEntity as BasePlayer;

						if (rocketOwner == null && compEntity != null)
							rocketOwner = compEntity.creatorEntity as BasePlayer;

						if (rocketOwner != null &&
							_lastPlayerTierInfo.TryGetValue(rocketOwner.userID, out var lastInfo) &&
							lastInfo != null)
						{
							if (lastInfo.LastRocketDamage > 0f)
								dmg = lastInfo.LastRocketDamage;
						}
					}
				}

				if (dmg > 0f)
				{
					var damageOwner = GetRocketDamageOwner(compEntity, info.Initiator);
					bool isBradleyOrHeli = entity != null && (
						entity.ShortPrefabName.ToString().Contains("bradleyapc") ||
						entity.ShortPrefabName.ToString().Contains("patrolhelicopter")
					);

					if (isBradleyOrHeli)
					{
						float directDamage = dmg * 0.5f;
						RecordPluginRaidDamage(damageOwner.UserId, damageOwner.Name, entity, directDamage);

						if (entity.health > 0f)
						{
							float oldHealth = entity.health;
							entity.health -= directDamage;

							if (entity.health <= 0f)
							{
								entity.health = 0f;
								entity.Die(info);
							}
						}

						return true;
					}

					RecordPluginRaidDamage(damageOwner.UserId, damageOwner.Name, entity, dmg);
					ApplyRocketDamageOverride(info, dmg);
				}

				return null;
			}

			BaseEntity c4Entity = null;

			if (info.Initiator is TimedExplosive explosiveInitiator)
			{
				c4Entity = explosiveInitiator;
			}
			else
			{
				string initName = info.Initiator != null ? info.Initiator.ShortPrefabName.ToString().ToLower() : "";
				string weaponName = info.Weapon != null ? info.Weapon.ShortPrefabName.ToString().ToLower() : "";
				string prefabName = info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName.ToString().ToLower() : "";

				if (initName.Contains("explosive") || initName.Contains("timed"))
				{
					c4Entity = info.Initiator;
				}
				else if (weaponName.Contains("explosive") || weaponName.Contains("timed"))
				{
					c4Entity = info.Weapon as BaseEntity;
				}
				else if (prefabName.Contains("explosive") || prefabName.Contains("timed"))
				{
					c4Entity = info.WeaponPrefab;
				}
			}

			if (entity != null && entity.ShortPrefabName.ToString().Contains("bradleyapc"))
			{
			}

			if (c4Entity != null)
			{
				float c4Dmg = 0f;
				var c4DamageOwner = GetC4DamageOwner(c4Entity, info.Initiator);

				var protoC4Comp = c4Entity.GetComponent<PrototypeC4Component>();
				if (protoC4Comp != null &&
					!string.IsNullOrEmpty(protoC4Comp.TierId) &&
					_config != null &&
					_config.PrototypeC4 != null &&
					_config.PrototypeC4.EnablePrototypeC4 &&
					_config.PrototypeC4.PrototypeC4Tiers != null)
				{
					if (_config.PrototypeC4.PrototypeC4Tiers.TryGetValue(protoC4Comp.TierId, out var pcfg) && pcfg != null)
					{
						c4Dmg = pcfg.ExplosionDamage * 2f;
					}
				}

				var c4Comp = c4Entity.GetComponent<C4TierComponent>();
				if (c4Dmg <= 0f &&
					c4Comp != null &&
					!string.IsNullOrEmpty(c4Comp.Tier) &&
					_config != null &&
					_config.BaseC4 != null &&
					_config.BaseC4.C4Tiers != null)
				{
					if (_config.BaseC4.C4Tiers.TryGetValue(c4Comp.Tier, out var c4Tier) && c4Tier != null)
					{
						c4Dmg = c4Tier.ExplosionDamage * 2f;
					}
				}

				if (c4Dmg <= 0f && info.Initiator is BasePlayer c4Owner)
				{
					if (_lastPlayerTierInfo.TryGetValue(c4Owner.userID, out var lastInfo) && lastInfo != null)
					{
						if (lastInfo.LastC4Damage > 0f)
						{
							c4Dmg = lastInfo.LastC4Damage;

						}
					}
				}

				if (c4Dmg > 0f)
				{
					bool isBradleyOrHeli = entity != null && (
						entity.ShortPrefabName.ToString().Contains("bradleyapc") ||
						entity.ShortPrefabName.ToString().Contains("patrolhelicopter")
					);

					if (isBradleyOrHeli)
					{
						float directDamage = c4Dmg * 0.5f;
						RecordPluginRaidDamage(c4DamageOwner.UserId, c4DamageOwner.Name, entity, directDamage);

						if (entity.health > 0f)
						{
							float oldHealth = entity.health;
							entity.health -= directDamage;

							if (entity.health <= 0f)
							{
								entity.health = 0f;
								entity.Die(info);
							}
						}

						return true;
					}

					RecordPluginRaidDamage(c4DamageOwner.UserId, c4DamageOwner.Name, entity, c4Dmg);
					SetExplosionDamageInDamageTypeList(info.damageTypes, c4Dmg);
				}
			}

			return null;
		}


        private void OnServerSave()
        {
            SaveRaidStatsData();
        }

        private void LoadRaidStatsData()
        {
            try
            {
                _raidStatsData = Interface.Oxide.DataFileSystem.ReadObject<StoredRaidStatsData>(RaidStatsDataFileName) ?? new StoredRaidStatsData();
                if (_raidStatsData.Players == null)
                    _raidStatsData.Players = new Dictionary<ulong, PlayerRaidStats>();
            }
            catch
            {
                _raidStatsData = new StoredRaidStatsData();
            }
        }

        private void SaveRaidStatsData()
        {
            if (_raidStatsData == null)
                _raidStatsData = new StoredRaidStatsData();
            if (_raidStatsData.Players == null)
                _raidStatsData.Players = new Dictionary<ulong, PlayerRaidStats>();

            Interface.Oxide.DataFileSystem.WriteObject(RaidStatsDataFileName, _raidStatsData);
            _raidStatsDirty = false;
        }

        private void MarkRaidStatsDirty()
        {
            _raidStatsDirty = true;
            if (_raidStatsSaveQueued) return;

            _raidStatsSaveQueued = true;
            timer.Once(60f, () =>
            {
                _raidStatsSaveQueued = false;
                if (_raidStatsDirty)
                    SaveRaidStatsData();
            });
        }

        private PlayerRaidStats GetOrCreateRaidStats(ulong userId, string playerName = null)
        {
            if (_raidStatsData == null)
                _raidStatsData = new StoredRaidStatsData();
            if (_raidStatsData.Players == null)
                _raidStatsData.Players = new Dictionary<ulong, PlayerRaidStats>();

            if (!_raidStatsData.Players.TryGetValue(userId, out var stats) || stats == null)
            {
                stats = new PlayerRaidStats();
                _raidStatsData.Players[userId] = stats;
            }

            if (!string.IsNullOrEmpty(playerName))
                stats.LastName = playerName;

            if (stats.C4TierUse == null) stats.C4TierUse = new Dictionary<string, int>();
            if (stats.RocketlauncherTierUse == null) stats.RocketlauncherTierUse = new Dictionary<string, int>();
            if (stats.PrototypeC4Use == null) stats.PrototypeC4Use = new Dictionary<string, int>();
            if (stats.PrototypeLauncherUse == null) stats.PrototypeLauncherUse = new Dictionary<string, int>();

            return stats;
        }

        private void AddToCounter(Dictionary<string, int> dict, string key)
        {
            if (dict == null || string.IsNullOrEmpty(key)) return;
            if (!dict.ContainsKey(key)) dict[key] = 0;
            dict[key]++;
        }

        private void RecordPluginC4ChargeUsed(BasePlayer player, string tierId, bool prototype)
        {
            if (player == null || string.IsNullOrEmpty(tierId)) return;

            var stats = GetOrCreateRaidStats(player.userID, player.displayName);
            if (prototype)
            {
                stats.PluginPrototypeC4ChargesUsed++;
                AddToCounter(stats.PrototypeC4Use, tierId);
                AddRaidXp(player, _config?.RaidRankSystem?.XPForPrototypeC4ChargeUsed ?? 5);
            }
            else
            {
                stats.PluginC4TierChargesUsed++;
                AddToCounter(stats.C4TierUse, tierId);
                AddRaidXp(player, _config?.RaidRankSystem?.XPForC4TierChargeUsed ?? 3);
            }

            MarkRaidStatsDirty();
        }

        private void RecordPluginRocketShotFired(BasePlayer player, string tierId, bool prototype)
        {
            if (player == null || string.IsNullOrEmpty(tierId)) return;

            var stats = GetOrCreateRaidStats(player.userID, player.displayName);
            if (prototype)
            {
                stats.PluginPrototypeRocketlauncherShotsFired++;
                AddToCounter(stats.PrototypeLauncherUse, tierId);
                AddRaidXp(player, _config?.RaidRankSystem?.XPForPrototypeRocketlauncherShotFired ?? 5);
            }
            else
            {
                stats.PluginRocketlauncherTierShotsFired++;
                AddToCounter(stats.RocketlauncherTierUse, tierId);
                AddRaidXp(player, _config?.RaidRankSystem?.XPForRocketlauncherTierShotFired ?? 2);
            }

            MarkRaidStatsDirty();
        }

        private void AddRaidXp(BasePlayer player, int amount)
        {
            if (player == null || amount <= 0) return;
            if (_config?.RaidRankSystem == null || !_config.RaidRankSystem.Enabled) return;

            var stats = GetOrCreateRaidStats(player.userID, player.displayName);
            int oldLevel = GetRaidLevel(stats.RaidXP);
            stats.RaidXP += amount;
            int newLevel = GetRaidLevel(stats.RaidXP);

            if (_config.RaidRankSystem.ShowXpMessages)
                SendReply(player, $"<color=#88c0ff>+{amount} Raid XP</color>");

            if (newLevel > oldLevel && _config.RaidRankSystem.ShowRankUpMessages)
                PrintToChat($"<color=#88c0ff>[CustomC4RocketTiers]</color> <color=#a3be8c>{player.displayName}</color> reached Raid Rank {newLevel}: <color=#ffd479>{GetRaidRankName(newLevel)}</color>");
        }

        private void RecordPluginRaidDamage(ulong ownerId, string ownerName, BaseCombatEntity target, float damage)
        {
            if (ownerId == 0 || damage <= 0f) return;

            var stats = GetOrCreateRaidStats(ownerId, ownerName);
            stats.TotalPluginRaidDamage += damage;

            string targetName = target != null ? target.ShortPrefabName.ToString() : string.Empty;
            if (targetName.Contains("bradleyapc"))
                stats.DamageToBradley += damage;
            else if (targetName.Contains("patrolhelicopter"))
                stats.DamageToPatrolHeli += damage;
            else if (target is BuildingBlock || targetName.Contains("wall") || targetName.Contains("door") || targetName.Contains("foundation") || targetName.Contains("floor") || targetName.Contains("roof"))
                stats.DamageToBuildings += damage;

            MarkRaidStatsDirty();
        }

        private struct DamageOwnerInfo
        {
            public ulong UserId;
            public string Name;
        }

        private DamageOwnerInfo GetRocketDamageOwner(BaseEntity rocketEntity, BaseEntity initiator)
        {
            var result = new DamageOwnerInfo();

            var rocketComp = rocketEntity?.GetComponent<RocketTierComponent>() ?? initiator?.GetComponent<RocketTierComponent>();
            if (rocketComp != null && rocketComp.OwnerId != 0)
            {
                result.UserId = rocketComp.OwnerId;
                result.Name = rocketComp.OwnerName;
                return result;
            }

            var protoComp = rocketEntity?.GetComponent<PrototypeLauncherShot>() ?? initiator?.GetComponent<PrototypeLauncherShot>();
            if (protoComp != null && protoComp.OwnerId != 0)
            {
                result.UserId = protoComp.OwnerId;
                result.Name = protoComp.OwnerName;
                return result;
            }

            BasePlayer player = initiator as BasePlayer;
            if (player == null)
                player = initiator?.creatorEntity as BasePlayer;
            if (player == null)
                player = rocketEntity?.creatorEntity as BasePlayer;

            if (player != null)
            {
                result.UserId = player.userID;
                result.Name = player.displayName;
            }

            return result;
        }

        private DamageOwnerInfo GetC4DamageOwner(BaseEntity c4Entity, BaseEntity initiator)
        {
            var result = new DamageOwnerInfo();

            var c4Comp = c4Entity?.GetComponent<C4TierComponent>();
            if (c4Comp != null && c4Comp.OwnerId != 0)
            {
                result.UserId = c4Comp.OwnerId;
                result.Name = c4Comp.OwnerName;
                return result;
            }

            var protoComp = c4Entity?.GetComponent<PrototypeC4Component>();
            if (protoComp != null && protoComp.OwnerId != 0)
            {
                result.UserId = protoComp.OwnerId;
                result.Name = protoComp.OwnerName;
                return result;
            }

            BasePlayer player = initiator as BasePlayer;
            if (player == null)
                player = initiator?.creatorEntity as BasePlayer;
            if (player == null)
                player = c4Entity?.creatorEntity as BasePlayer;

            if (player != null)
            {
                result.UserId = player.userID;
                result.Name = player.displayName;
            }

            return result;
        }

        private readonly int[] _raidRankXp = { 0, 100, 250, 500, 900, 1400, 2100, 3000, 4200, 5600, 7300, 9300 };
        private readonly string[] _raidRankNames =
        {
            "Recruit",
            "Operator",
            "Breach Operator",
            "Demolition Operator",
            "Siege Operator",
            "Heavy Weapons Specialist",
            "Explosives Specialist",
            "Prototype Specialist",
            "Veteran Operator",
            "Raid Commander",
            "Elite Commander",
            "Apex Commander"
        };

        private int GetRaidLevel(int xp)
        {
            int level = 1;
            for (int i = 0; i < _raidRankXp.Length; i++)
            {
                if (xp >= _raidRankXp[i])
                    level = i + 1;
            }
            return Mathf.Clamp(level, 1, _raidRankNames.Length);
        }

        private string GetRaidRankName(int level)
        {
            level = Mathf.Clamp(level, 1, _raidRankNames.Length);
            return _raidRankNames[level - 1];
        }

        private int GetNextRankXp(int level)
        {
            if (level >= _raidRankXp.Length) return -1;
            return _raidRankXp[level];
        }

        private string MostUsed(Dictionary<string, int> dict)
        {
            if (dict == null || dict.Count == 0) return "None";
            var best = dict.OrderByDescending(x => x.Value).First();
            return $"{best.Key} ({best.Value})";
        }

        private string FormatNumber(float value)
        {
            return value.ToString("0");
        }

        [ChatCommand("raidrank")]
        private void CmdRaidRank(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (_config?.RaidRankSystem == null || !_config.RaidRankSystem.Enabled)
            {
                SendReply(player, "<color=yellow>The visual Raid Rank system is disabled.</color>");
                return;
            }

            var stats = GetOrCreateRaidStats(player.userID, player.displayName);
            int level = GetRaidLevel(stats.RaidXP);
            int nextXp = GetNextRankXp(level);
            string xpLine = nextXp > 0 ? $"XP: {stats.RaidXP} / {nextXp}" : $"XP: {stats.RaidXP} / MAX";
            string nextLine = nextXp > 0 ? $"Next Rank: {GetRaidRankName(level + 1)}" : "Next Rank: MAX";

            SendReply(player,
                "<color=#88c0ff>=== Raid Rank ===</color>\n\n" +
                $"Rank: <color=#ffd479>{GetRaidRankName(level)}</color>\n" +
                $"Level: {level}\n" +
                $"{xpLine}\n\n" +
                $"Plugin C4 Tier Charges Used: {stats.PluginC4TierChargesUsed}\n" +
                $"Plugin Rocketlauncher Tier Shots Fired: {stats.PluginRocketlauncherTierShotsFired}\n" +
                $"Plugin Prototype C4 Charges Used: {stats.PluginPrototypeC4ChargesUsed}\n" +
                $"Plugin Prototype Rocketlauncher Shots Fired: {stats.PluginPrototypeRocketlauncherShotsFired}\n\n" +
                $"Total Plugin Raid Damage: {FormatNumber(stats.TotalPluginRaidDamage)}\n" +
                $"{nextLine}");
        }

        [ChatCommand("raidtop")]
        private void CmdRaidTop(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (_config?.RaidRankSystem == null || !_config.RaidRankSystem.Enabled)
            {
                SendReply(player, "<color=yellow>The visual Raid Rank system is disabled.</color>");
                return;
            }

            int amount = Mathf.Clamp(_config.RaidRankSystem.TopListAmount, 1, 20);
            var top = _raidStatsData?.Players?.OrderByDescending(x => x.Value.RaidXP).Take(amount).ToList();
            if (top == null || top.Count == 0)
            {
                SendReply(player, "<color=yellow>No raid rank data yet.</color>");
                return;
            }

            var lines = new List<string> { "<color=#88c0ff>=== Top Raiders ===</color>" };
            int pos = 1;
            foreach (var entry in top)
            {
                var st = entry.Value;
                int level = GetRaidLevel(st.RaidXP);
                lines.Add($"{pos}. {st.LastName ?? entry.Key.ToString()} - Level {level} - {GetRaidRankName(level)} - {st.RaidXP} XP");
                pos++;
            }

            SendReply(player, string.Join("\n", lines));
        }

        [ChatCommand("c4rtstats")]
        private void CmdC4RtStats(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
            {
                SendReply(player, "<color=red>No permission!</color>");
                return;
            }

            if (args != null && args.Length > 0)
            {
                var target = FindPlayerByArg(args[0]);
                if (target != null)
                {
                    SendReply(player, FormatPlayerRaidStats(target.userID, target.displayName));
                    return;
                }

                if (ulong.TryParse(args[0], out var id) && _raidStatsData != null && _raidStatsData.Players != null && _raidStatsData.Players.ContainsKey(id))
                {
                    SendReply(player, FormatPlayerRaidStats(id, null));
                    return;
                }

                SendReply(player, "<color=red>Player stats not found.</color>");
                return;
            }

            SendReply(player, FormatGlobalRaidStats());
        }

        [ConsoleCommand("c4rtstats")]
        private void CC_C4RtStats(ConsoleSystem.Arg arg)
        {
            if (arg.Args != null && arg.Args.Length > 0)
            {
                var targetArg = arg.Args[0].ToString();
                var target = FindPlayerByArg(targetArg);
                if (target != null)
                {
                    arg.ReplyWith(StripRustColors(FormatPlayerRaidStats(target.userID, target.displayName)));
                    return;
                }

                if (ulong.TryParse(targetArg, out var id) && _raidStatsData != null && _raidStatsData.Players != null && _raidStatsData.Players.ContainsKey(id))
                {
                    arg.ReplyWith(StripRustColors(FormatPlayerRaidStats(id, null)));
                    return;
                }

                arg.ReplyWith("Player stats not found.");
                return;
            }

            arg.ReplyWith(StripRustColors(FormatGlobalRaidStats()));
        }

        private string FormatGlobalRaidStats()
        {
            var players = _raidStatsData?.Players?.Values?.ToList() ?? new List<PlayerRaidStats>();

            int c4Tier = players.Sum(x => x.PluginC4TierChargesUsed);
            int rocketTier = players.Sum(x => x.PluginRocketlauncherTierShotsFired);
            int protoC4 = players.Sum(x => x.PluginPrototypeC4ChargesUsed);
            int protoLauncher = players.Sum(x => x.PluginPrototypeRocketlauncherShotsFired);
            float totalDamage = players.Sum(x => x.TotalPluginRaidDamage);
            float buildingDamage = players.Sum(x => x.DamageToBuildings);
            float bradleyDamage = players.Sum(x => x.DamageToBradley);
            float heliDamage = players.Sum(x => x.DamageToPatrolHeli);

            var allC4 = MergeCounters(players.Select(x => x.C4TierUse));
            var allRocket = MergeCounters(players.Select(x => x.RocketlauncherTierUse));
            var allProtoC4 = MergeCounters(players.Select(x => x.PrototypeC4Use));
            var allProtoLauncher = MergeCounters(players.Select(x => x.PrototypeLauncherUse));

            return "<color=#88c0ff>=== CustomC4RocketTiers Stats ===</color>\n\n" +
                   $"Plugin C4 Tier Charges Used: {c4Tier}\n" +
                   $"Plugin Rocketlauncher Tier Shots Fired: {rocketTier}\n" +
                   $"Plugin Prototype C4 Charges Used: {protoC4}\n" +
                   $"Plugin Prototype Rocketlauncher Shots Fired: {protoLauncher}\n\n" +
                   $"Total Plugin Raid Damage: {FormatNumber(totalDamage)}\n" +
                   $"Damage to Buildings: {FormatNumber(buildingDamage)}\n" +
                   $"Damage to Bradley: {FormatNumber(bradleyDamage)}\n" +
                   $"Damage to Patrol Heli: {FormatNumber(heliDamage)}\n\n" +
                   $"Most Used C4 Tier: {MostUsed(allC4)}\n" +
                   $"Most Used Rocketlauncher Tier: {MostUsed(allRocket)}\n" +
                   $"Most Used Prototype C4: {MostUsed(allProtoC4)}\n" +
                   $"Most Used Prototype Launcher: {MostUsed(allProtoLauncher)}";
        }

        private string FormatPlayerRaidStats(ulong userId, string name)
        {
            var stats = GetOrCreateRaidStats(userId, name);
            int level = GetRaidLevel(stats.RaidXP);

            return "<color=#88c0ff>=== CustomC4RocketTiers Player Stats ===</color>\n\n" +
                   $"Player: {stats.LastName ?? userId.ToString()}\n" +
                   $"Rank: {GetRaidRankName(level)}\n" +
                   $"Level: {level}\n" +
                   $"XP: {stats.RaidXP}\n\n" +
                   $"Plugin C4 Tier Charges Used: {stats.PluginC4TierChargesUsed}\n" +
                   $"Plugin Rocketlauncher Tier Shots Fired: {stats.PluginRocketlauncherTierShotsFired}\n" +
                   $"Plugin Prototype C4 Charges Used: {stats.PluginPrototypeC4ChargesUsed}\n" +
                   $"Plugin Prototype Rocketlauncher Shots Fired: {stats.PluginPrototypeRocketlauncherShotsFired}\n\n" +
                   $"Total Plugin Raid Damage: {FormatNumber(stats.TotalPluginRaidDamage)}\n" +
                   $"Damage to Buildings: {FormatNumber(stats.DamageToBuildings)}\n" +
                   $"Damage to Bradley: {FormatNumber(stats.DamageToBradley)}\n" +
                   $"Damage to Patrol Heli: {FormatNumber(stats.DamageToPatrolHeli)}\n\n" +
                   $"Most Used C4 Tier: {MostUsed(stats.C4TierUse)}\n" +
                   $"Most Used Rocketlauncher Tier: {MostUsed(stats.RocketlauncherTierUse)}\n" +
                   $"Most Used Prototype C4: {MostUsed(stats.PrototypeC4Use)}\n" +
                   $"Most Used Prototype Launcher: {MostUsed(stats.PrototypeLauncherUse)}";
        }

        private Dictionary<string, int> MergeCounters(IEnumerable<Dictionary<string, int>> counters)
        {
            var result = new Dictionary<string, int>();
            if (counters == null) return result;

            foreach (var counter in counters)
            {
                if (counter == null) continue;
                foreach (var kvp in counter)
                {
                    if (!result.ContainsKey(kvp.Key)) result[kvp.Key] = 0;
                    result[kvp.Key] += kvp.Value;
                }
            }

            return result;
        }

        [ChatCommand("c4rtxp")]
        private void CmdC4RtXp(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
            {
                SendReply(player, "<color=red>No permission!</color>");
                return;
            }

            if (_config?.RaidRankSystem == null || !_config.RaidRankSystem.Enabled)
            {
                SendReply(player, "<color=yellow>The visual Raid Rank system is disabled.</color>");
                return;
            }

            if (args == null || args.Length < 2)
            {
                SendReply(player, "Usage: /c4rtxp add|remove|reset <player> [amount]");
                return;
            }

            HandleRaidXpCommand(player, args[0], args[1], args.Length >= 3 ? args[2] : "0");
        }

        [ConsoleCommand("c4rtxp")]
        private void CC_C4RtXp(ConsoleSystem.Arg arg)
        {
            if (_config?.RaidRankSystem == null || !_config.RaidRankSystem.Enabled)
            {
                arg.ReplyWith("The visual Raid Rank system is disabled.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: c4rtxp add|remove|reset <player> [amount]");
                return;
            }

            string result = HandleRaidXpCommand(null, arg.Args[0].ToString(), arg.Args[1].ToString(), arg.Args.Length >= 3 ? arg.Args[2].ToString() : "0");
            arg.ReplyWith(result);
        }

        private string HandleRaidXpCommand(BasePlayer requester, string action, string targetArg, string amountArg)
        {
            var target = FindPlayerByArg(targetArg);
            if (target == null)
            {
                if (requester != null) SendReply(requester, "<color=red>Player not found.</color>");
                return "Player not found.";
            }

            var stats = GetOrCreateRaidStats(target.userID, target.displayName);
            action = (action ?? string.Empty).ToLower();

            int amount = 0;
            int.TryParse(amountArg, out amount);

            if (action == "add")
                stats.RaidXP += Mathf.Max(0, amount);
            else if (action == "remove")
                stats.RaidXP = Mathf.Max(0, stats.RaidXP - Mathf.Max(0, amount));
            else if (action == "reset")
                stats.RaidXP = 0;
            else
            {
                if (requester != null) SendReply(requester, "<color=red>Invalid action. Use add, remove or reset.</color>");
                return "Invalid action. Use add, remove or reset.";
            }

            MarkRaidStatsDirty();
            string msg = $"Raid XP for {target.displayName}: {stats.RaidXP}";
            if (requester != null) SendReply(requester, "<color=green>" + msg + "</color>");
            return msg;
        }

        [ChatCommand("c4rtstatsreset")]
        private void CmdC4RtStatsReset(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
            {
                SendReply(player, "<color=red>No permission!</color>");
                return;
            }

            _raidStatsData = new StoredRaidStatsData();
            SaveRaidStatsData();
            SendReply(player, "<color=green>CustomC4RocketTiers raid statistics have been reset.</color>");
        }

        private string StripRustColors(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("<color=#88c0ff>", "").Replace("<color=#a3be8c>", "").Replace("<color=#ffd479>", "").Replace("<color=yellow>", "").Replace("<color=green>", "").Replace("<color=red>", "").Replace("</color>", "");
        }

        private void OnItemRemoved(Item item)
        {
            if (_rocketTierData.ContainsKey(item.uid.Value))
                _rocketTierData.Remove(item.uid.Value);

            _prototypeLauncherTierByItem.Remove(item.uid.Value);
            _prototypeC4TierByItem.Remove(item.uid.Value);
        }

        private string GetC4TierFromItem(Item item)
        {
            if (item == null) return null;

            if (item.skin != 0 && _config?.BaseC4?.C4Tiers != null)
            {
                foreach (var kvp in _config.BaseC4.C4Tiers)
                {
                    var tier = kvp.Value;
                    if (tier != null && tier.SkinId != 0 && tier.SkinId == item.skin)
                        return kvp.Key;
                }
            }

            var lowerName = item.name?.ToLower();
            if (!string.IsNullOrEmpty(lowerName))
            {
                if (lowerName.Contains("c4_tier1")) return "c4_tier1";
                if (lowerName.Contains("c4_tier2")) return "c4_tier2";
                if (lowerName.Contains("c4_tier3")) return "c4_tier3";
            }

            return null;
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            CacheTierFromNameIfPresent(item);
            EnsureLauncherTierName(item);
            EnsureC4TierName(item);
            EnsurePrototypeTierName(item);

            if (item != null && item.info != null && item.info.shortname == "rocket.launcher")
            {
                ApplyHeldEntitySkin(item);

                if (IsPrototypeLauncher(item))
                {
                    ApplyPrototypeLauncherMagazine(item, null);
                }
                else
                {
                    EnforceLauncherMagazine(item, null);
                }
            }

            NextTick(() =>
            {
                EnsureLauncherTierName(item);
                EnsureC4TierName(item);
                EnsurePrototypeTierName(item);

                if (item != null && item.info != null && item.info.shortname == "rocket.launcher")
                {
                    ApplyHeldEntitySkin(item);

                    if (IsPrototypeLauncher(item))
                    {
                        ApplyPrototypeLauncherMagazine(item, null);
                    }
                    else
                    {
                        ApplyLauncherTierMagazine(item, null);
                        EnforceLauncherMagazine(item, null);
                    }
                }
            });
        }

                private void OnLootSpawn(LootContainer container)
                {
                        try
                        {
                                if (container == null) return;
                                if (_config?.LootSpawns == null || !_config.LootSpawns.Enabled) return;

                                var list = _config.LootSpawns.ContainerPrefabs;
                                if (list != null && list.Count > 0)
                                {
                                        var prefab = container.PrefabName ?? string.Empty;
                                        if (string.IsNullOrEmpty(prefab)) return;

                                        bool allowed = false;
                                        for (int i = 0; i < list.Count; i++)
                                        {
                                                var p = list[i];
                                                if (string.IsNullOrEmpty(p)) continue;
                                                if (prefab.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                        allowed = true;
                                                        break;
                                                }
                                        }
                                        if (!allowed) return;
                                }

                                SpawnTierLootIntoContainer(container);
                        }
                        catch (Exception ex)
                        {
                                PrintError($"OnLootSpawn error: {ex}");
                        }
				}

				private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
				{
					if (oldItem != null && (newItem == null || newItem.info == null || newItem.info.shortname != "rocket.launcher"))
					{
						StopMagazineEnforcer(player);
					}

					if (newItem == null || newItem.info == null)
						return;

					if (newItem.info.shortname == "rocket.launcher")
					{
						ApplyHeldEntitySkin(newItem);

						if (IsPrototypeLauncher(newItem))
						{
							var protoTier = GetPrototypeTierFromItem(newItem);
							if (!string.IsNullOrEmpty(protoTier))
								EnsurePrototypeLauncherName(newItem, protoTier);
						}
						else
						{
							EnsureLauncherTierName(newItem);
						}

						EnforceAnyLauncherMagazine(newItem);
						StartMagazineEnforcer(player, newItem, null);
						return;
					}

					if (newItem.info.shortname == "explosive.timed")
					{
						var protoTier = GetPrototypeC4TierFromItem(newItem);
						if (!string.IsNullOrEmpty(protoTier))
						{
							EnsurePrototypeC4TierName(newItem);
						}
						else
						{
							EnsureC4TierName(newItem);
						}
					}
				}


				private void OnPlayerDisconnected(BasePlayer player, string reason)
				{
					StopMagazineEnforcer(player);
				}

				private void OnWeaponFired(BaseProjectile projectile, BasePlayer player)
				{
					EnforceLauncherMagazineFromProjectile(projectile, player);

					NextTick(() => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.05f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.15f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.35f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
				}

				private object CanReloadWeapon(BasePlayer player, BaseProjectile projectile)
				{
					EnforceLauncherMagazineFromProjectile(projectile, player);
					return null;
				}

				private object OnReloadWeapon(BasePlayer player, BaseProjectile projectile)
				{
					EnforceLauncherMagazineFromProjectile(projectile, player);

					NextTick(() => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.05f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.15f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.35f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.75f, () => EnforceLauncherMagazineFromProjectile(projectile, player));

					return null;
				}

				private void OnWeaponReload(BaseProjectile projectile, BasePlayer player)
				{
					EnforceLauncherMagazineFromProjectile(projectile, player);

					NextTick(() => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.05f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.15f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.35f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
					timer.Once(0.75f, () => EnforceLauncherMagazineFromProjectile(projectile, player));
				}
                
                private void SpawnTierLootIntoContainer(LootContainer container)
                {
                        var cfg = _config?.LootSpawns;
                        if (cfg?.Rules == null || cfg.Rules.Count == 0) return;

                        var inv = container.inventory;
                        if (inv == null) return;

                        int added = 0;
                        int maxAdd = Mathf.Max(0, cfg.MaxExtraItemsPerContainer);

                        foreach (var rule in cfg.Rules)
                        {
                                if (rule == null) continue;
                                if (added >= maxAdd) break;

                                if (rule.Chance <= 0f) continue;
                                if (UnityEngine.Random.Range(0f, 1f) > rule.Chance) continue;

                                int min = Mathf.Max(1, rule.MinAmount);
                                int max = Mathf.Max(min, rule.MaxAmount);
                                int amount = UnityEngine.Random.Range(min, max + 1);

                                var item = CreateTierSpawnItem(rule.ItemType, rule.Tier, amount);
                                if (item == null) continue;

                                item.MoveToContainer(inv);
                                added++;
                        }
                }

                private Item CreateTierSpawnItem(string itemType, string tier, int amount)
                {
                        if (string.IsNullOrEmpty(itemType) || string.IsNullOrEmpty(tier)) return null;
                        itemType = itemType.ToLowerInvariant();

                        if (itemType == "c4")
                        {
                                var dict = _config?.BaseC4?.C4Tiers;
                                if (dict == null) return null;
                                if (!dict.TryGetValue(tier, out var c4cfg) || c4cfg == null) return null;

                                var item = ItemManager.CreateByName("explosive.timed", amount);
                                if (item == null) return null;

                                item.name = tier.ToUpper();
                                if (c4cfg.SkinId != 0) item.skin = c4cfg.SkinId;
                                item.MarkDirty();

                                EnsureC4TierName(item);
                                return item;
                        }

                        if (itemType == "rocketlauncher")
                        {
                                var ldict = _config?.NormalLauncher?.RocketLauncherTiers;
                                if (ldict == null) return null;
                                if (!ldict.TryGetValue(tier, out var lcfg) || lcfg == null) return null;

                                var item = ItemManager.CreateByName("rocket.launcher", amount);
                                if (item == null) return null;

                                _rocketTierData[item.uid.Value] = tier;

                                item.name = GetRocketLauncherDisplayName(tier);

                                var rdict = _config?.BaseRockets?.RocketTiers;
                                if (rdict != null && rdict.TryGetValue(tier, out var rt) && rt != null && rt.SkinId != 0)
                                        item.skin = rt.SkinId;

                                item.MarkDirty();
                                EnsureLauncherTierName(item);
                                return item;
                        }

                        if (itemType == "proto_launcher")
                        {
                                if (_config?.PrototypeLauncher == null || !_config.PrototypeLauncher.EnablePrototypeLauncher) return null;
                                var pdict = _config.PrototypeLauncher.PrototypeLauncherTiers;
                                if (pdict == null) return null;
                                if (!pdict.TryGetValue(tier, out var pcfg) || pcfg == null) return null;

                                var item = ItemManager.CreateByName("rocket.launcher", amount, pcfg.SkinId);
                                if (item == null) return null;

                                _prototypeLauncherTierByItem[item.uid.Value] = tier;
                                EnsurePrototypeLauncherName(item, tier);
                                item.MarkDirty();
                                return item;
                        }

                        if (itemType == "proto_c4")
                        {
                                if (_config?.PrototypeC4 == null || !_config.PrototypeC4.EnablePrototypeC4) return null;
                                var cdict = _config.PrototypeC4.PrototypeC4Tiers;
                                if (cdict == null) return null;
                                if (!cdict.TryGetValue(tier, out var ccfg) || ccfg == null) return null;

                                var item = ItemManager.CreateByName("explosive.timed", amount, ccfg.SkinId);
                                if (item == null) return null;

                                _prototypeC4TierByItem[item.uid.Value] = tier;
                                EnsurePrototypeC4Name(item, tier);
                                item.MarkDirty();
                                return item;
                        }

                        return null;
                }

        private void OnItemSplit(Item item, Item newItem)
        {
            if (item == null || newItem == null) return;

            if (item.info != null && item.info.shortname == "rocket.launcher")
            {
                if (_rocketTierData.TryGetValue(item.uid.Value, out var tier))
                {
                    _rocketTierData[newItem.uid.Value] = tier;
                    EnsureLauncherTierName(newItem);
                }

                if (_prototypeLauncherTierByItem.TryGetValue(item.uid.Value, out var protoTier))
                {
                    _prototypeLauncherTierByItem[newItem.uid.Value] = protoTier;
                    EnsurePrototypeTierName(newItem);
                }
            }
            else if (item.info != null && item.info.shortname == "explosive.timed")
            {
                if (_prototypeC4TierByItem.TryGetValue(item.uid.Value, out var protoC4Tier))
                {
                    _prototypeC4TierByItem[newItem.uid.Value] = protoC4Tier;
                    EnsurePrototypeC4TierName(newItem);
                }
                else
                {
                    EnsureC4TierName(newItem);
                }
            }
        }

		private void ApplyLauncherTierMagazine(Item item, string tierId = null)
		{
			if (item == null || item.info == null) return;
			if (item.info.shortname != "rocket.launcher") return;

			if (string.IsNullOrEmpty(tierId))
			{
				_rocketTierData.TryGetValue(item.uid.Value, out tierId);
			}

			if (string.IsNullOrEmpty(tierId))
			{
				CacheTierFromNameIfPresent(item);
				_rocketTierData.TryGetValue(item.uid.Value, out tierId);
			}

			if (string.IsNullOrEmpty(tierId))
			{
				tierId = ResolveRocketTierFromItem(item);
			}

			if (string.IsNullOrEmpty(tierId)) return;
			_rocketTierData[item.uid.Value] = tierId;

			if (_config?.NormalLauncher?.RocketLauncherTiers == null) return;
			if (!_config.NormalLauncher.RocketLauncherTiers.TryGetValue(tierId, out var cfg) || cfg == null) return;

			var held = item.GetHeldEntity() as BaseProjectile;
			if (held == null || held.primaryMagazine == null) return;

			int desired = Mathf.Max(1, cfg.MagazineSize);
			var mag = held.primaryMagazine;

			bool changed = false;

			if (mag.capacity != desired)
			{
				mag.capacity = desired;
				changed = true;
			}

			if (mag.contents > desired)
			{
				int excess = mag.contents - desired;

				BasePlayer owner = held.GetOwnerPlayer();
				if (owner == null)
					owner = item.GetOwnerPlayer();

				if (owner != null && mag.ammoType != null && excess > 0)
				{
					Item refund = ItemManager.Create(mag.ammoType, excess);
					if (refund != null)
					{
						if (!owner.inventory.GiveItem(refund))
							refund.Drop(owner.transform.position + Vector3.up, Vector3.zero);
					}
				}

				mag.contents = desired;
				changed = true;
			}

			if (changed)
			{
				held.SendNetworkUpdate();
				item.MarkDirty();
			}
		}

		private void ApplyPrototypeLauncherMagazine(Item item, string tierId = null)
		{
			if (item == null || item.info == null) return;
			if (item.info.shortname != "rocket.launcher") return;
			if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher) return;

			if (string.IsNullOrEmpty(tierId))
				tierId = GetPrototypeTierFromItem(item);

			if (string.IsNullOrEmpty(tierId)) return;
			if (_config.PrototypeLauncher.PrototypeLauncherTiers == null) return;
			if (!_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tierId, out var cfg) || cfg == null) return;

			var held = item.GetHeldEntity() as BaseProjectile;
			if (held == null || held.primaryMagazine == null) return;

			int desired = Mathf.Max(1, cfg.MagazineSize);
			var mag = held.primaryMagazine;

			bool changed = false;

			if (mag.capacity != desired)
			{
				mag.capacity = desired;
				changed = true;
			}

			if (mag.contents > desired)
			{
				int excess = mag.contents - desired;

				BasePlayer owner = held.GetOwnerPlayer();
				if (owner == null)
					owner = item.GetOwnerPlayer();

				if (owner != null && mag.ammoType != null && excess > 0)
				{
					Item refund = ItemManager.Create(mag.ammoType, excess);
					if (refund != null)
					{
						if (!owner.inventory.GiveItem(refund))
							refund.Drop(owner.transform.position + Vector3.up, Vector3.zero);
					}
				}

				mag.contents = desired;
				changed = true;
			}

			if (changed)
			{
				held.SendNetworkUpdate();
				item.MarkDirty();
			}
		}

        private string ResolveRocketTierFromItem(Item item)
        {
            if (item == null) return null;

            if (item.skin != 0 && _config != null)
            {
                if (_config.BaseRockets.RocketTiers != null)
                {
                    foreach (var kvp in _config.BaseRockets.RocketTiers)
                    {
                        var tier = kvp.Value;
                        if (tier != null && tier.SkinId != 0 && tier.SkinId == item.skin)
                            return kvp.Key;
                    }
                }
                if (_config.BaseRockets.RocketHvTiers != null)
                {
                    foreach (var kvp in _config.BaseRockets.RocketHvTiers)
                    {
                        var tier = kvp.Value;
                        if (tier != null && tier.SkinId != 0 && tier.SkinId == item.skin)
                            return kvp.Key;
                    }
                }
            }

            var n = item.name?.ToLower();
            if (!string.IsNullOrEmpty(n))
            {
                if (n.Contains("rocket_tier1") || n.Contains("rocketlauncher t1")) return "rocket_tier1";
                if (n.Contains("rocket_tier2") || n.Contains("rocketlauncher t2")) return "rocket_tier2";
                if (n.Contains("rocket_tier3") || n.Contains("rocketlauncher t3")) return "rocket_tier3";
            }

            return null;
        }

        private string GetRocketLauncherDisplayName(string tierId)
        {
            if (string.IsNullOrEmpty(tierId))
                return "Rocketlauncher";

            if (_config != null &&
                _config.NormalLauncher.RocketLauncherTiers != null &&
                _config.NormalLauncher.RocketLauncherTiers.TryGetValue(tierId, out var cfg) &&
                cfg != null &&
                !string.IsNullOrEmpty(cfg.DisplayName))
            {
                return cfg.DisplayName;
            }

            return tierId.ToUpper();
        }

        private string GetRocketDamageSummary(Dictionary<string, RocketTier> dict)
        {
            if (dict == null || dict.Count == 0)
                return null;

            var values = new List<string>();
            foreach (var kvp in dict)
            {
                var cfg = kvp.Value;
                if (cfg == null) continue;
                values.Add(cfg.ExplosionDamage.ToString("0"));
            }
            values.Sort();
            return string.Join("/", values);
        }

        private string GetPrototypeLauncherDisplayName(string tierId)
        {
            if (string.IsNullOrEmpty(tierId) || _config == null || _config.PrototypeLauncher.PrototypeLauncherTiers == null)
                return "Prototype Rocketlauncher";

            if (!_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tierId, out var cfg) || cfg == null)
                return "Prototype Rocketlauncher";

            var baseName = !string.IsNullOrEmpty(cfg.DisplayName) ? cfg.DisplayName : "Prototype Rocketlauncher";
            var mag = cfg.MagazineSize;
            var wear = cfg.ConditionLossPerShot;

            if (cfg.BasicExplosionDamage > 0f || cfg.HvExplosionDamage > 0f)
            {
                                var b = cfg.BasicExplosionDamage > 0f ? cfg.BasicExplosionDamage : 0f;
                                var h = cfg.HvExplosionDamage > 0f ? cfg.HvExplosionDamage : 0f;
                                return $"{baseName} [Basic:{b:0} | HV:{h:0}] (mag {mag} | wear {wear:0.#})";
            }

            var mult = cfg.DamageMultiplier;
            return $"{baseName} (x{mult:0.0} dmg | mag {mag} | wear {wear:0.0})";
        }


                private void EnsureLauncherTierName(Item item)
                {
                        if (item == null || item.info == null) return;
                        if (item.info.shortname != "rocket.launcher") return;

                        CacheTierFromNameIfPresent(item);

                        string tierId = null;

                        if (_rocketTierData.TryGetValue(item.uid.Value, out var mapped) && !string.IsNullOrEmpty(mapped))
                                tierId = mapped;

                        if (string.IsNullOrEmpty(tierId))
                                tierId = ResolveRocketTierFromItem(item);

                        if (string.IsNullOrEmpty(tierId))
                        {
                                var n = item.name?.ToLower();
                                if (!string.IsNullOrEmpty(n))
                                {
                                        if (n.Contains("rocket_tier1") || n.Contains("rocketlauncher t1")) tierId = "rocket_tier1";
                                        else if (n.Contains("rocket_tier2") || n.Contains("rocketlauncher t2")) tierId = "rocket_tier2";
                                        else if (n.Contains("rocket_tier3") || n.Contains("rocketlauncher t3")) tierId = "rocket_tier3";
                                }
                        }

                        if (string.IsNullOrEmpty(tierId)) return;

                        var baseName = GetRocketLauncherDisplayName(tierId);

                        if (_config != null && !_config.NameDisplay.ShowDamageInRocketLauncherTierName)
                        {
                                if (item.name != baseName)
                                {
                                        item.name = baseName;
                                        item.MarkDirty();
                                }

                                _rocketTierData[item.uid.Value] = tierId;
                                return;
                        }

                        float basicDmg = 0f;
                        float hvDmg = 0f;

                        if (_config != null && _config.General.EnableBasicRocketTiers && _config.BaseRockets.RocketTiers != null &&
                                _config.BaseRockets.RocketTiers.TryGetValue(tierId, out var basicCfg) && basicCfg != null)
                                basicDmg = basicCfg.ExplosionDamage;

                        if (_config != null && _config.General.EnableHvRocketTiers && _config.BaseRockets.RocketHvTiers != null &&
                                _config.BaseRockets.RocketHvTiers.TryGetValue(tierId, out var hvCfg) && hvCfg != null)
                                hvDmg = hvCfg.ExplosionDamage;

                        var parts = new List<string>();

                        if (basicDmg > 0f)
                                parts.Add($"DMG:{basicDmg:0}");

                        if (_config != null && _config.General.EnableHvRocketTiers && hvDmg > 0f)
                                parts.Add($"HV:{hvDmg:0}");

                        var expected = parts.Count > 0
                                ? $"{baseName} [{string.Join(" | ", parts)}]"
                                : baseName;

                        if (item.name != expected)
                        {
                                item.name = expected;
                                item.MarkDirty();
                        }

                        _rocketTierData[item.uid.Value] = tierId;
                }

        private void CacheTierFromNameIfPresent(Item item)
        {
            if (item == null || item.info == null) return;
            var n = item.name?.ToLower();
            if (string.IsNullOrEmpty(n)) return;

            if (item.info.shortname == "rocket.launcher")
            {
                if (n.Contains("rocket_tier1") || n.Contains("rocketlauncher t1")) _rocketTierData[item.uid.Value] = "rocket_tier1";
                else if (n.Contains("rocket_tier2") || n.Contains("rocketlauncher t2")) _rocketTierData[item.uid.Value] = "rocket_tier2";
                else if (n.Contains("rocket_tier3") || n.Contains("rocketlauncher t3")) _rocketTierData[item.uid.Value] = "rocket_tier3";
            }
        }

                private void EnsureC4TierName(Item item)
                {
                        if (item == null || item.info == null) return;
                        if (item.info.shortname != "explosive.timed") return;

                        string tierId = GetC4TierFromItem(item);

                        if (string.IsNullOrEmpty(tierId))
                        {
                                var n = item.name?.ToLower();
                                if (!string.IsNullOrEmpty(n))
                                {
                                        if (n.Contains("c4_tier1")) tierId = "c4_tier1";
                                        else if (n.Contains("c4_tier2")) tierId = "c4_tier2";
                                        else if (n.Contains("c4_tier3")) tierId = "c4_tier3";
                                }
                        }

                        if (string.IsNullOrEmpty(tierId)) return;

                        if (_config != null && !_config.NameDisplay.ShowDamageInC4TierName)
                        {
                                var expectedOld = tierId.ToUpper();
                                if (item.name != expectedOld)
                                {
                                        item.name = expectedOld;
                                        item.MarkDirty();
                                }
                                return;
                        }

                        float dmg = 0f;
                        ulong skinId = 0;

                        if (_config != null && _config.BaseC4.C4Tiers != null &&
                                _config.BaseC4.C4Tiers.TryGetValue(tierId, out var c4cfg) && c4cfg != null)
                        {
                                dmg = c4cfg.ExplosionDamage;
                                skinId = c4cfg.SkinId;
                        }

                        if (skinId != 0)
                                item.skin = skinId;

                        var expected = dmg > 0f
                                ? $"{tierId.ToUpper()} [DMG:{dmg:0}]"
                                : tierId.ToUpper();

                        if (item.name != expected)
                        {
                                item.name = expected;
                                item.MarkDirty();
                        }
                }

				private string GetPrototypeTierFromItem(Item item)
				{
					if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
						return null;

					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return null;

					if (_prototypeLauncherTierByItem.TryGetValue(item.uid.Value, out var tierId) && !string.IsNullOrEmpty(tierId))
						return tierId;

					if (item.skin != 0 && _config.PrototypeLauncher.PrototypeLauncherTiers != null)
					{
						foreach (var kvp in _config.PrototypeLauncher.PrototypeLauncherTiers)
						{
							var cfg = kvp.Value;
							if (cfg != null && cfg.SkinId != 0 && cfg.SkinId == item.skin)
							{
								tierId = kvp.Key;
								break;
							}
						}
					}

					if (string.IsNullOrEmpty(tierId))
					{
						var n = item.name?.ToLowerInvariant();
						if (!string.IsNullOrEmpty(n))
						{
							if (n.Contains("prototype rocketlauncher iv") || n.Contains("prototype rocketlauncher 4"))
								tierId = "proto4";
							else if (n.Contains("prototype rocketlauncher iii") || n.Contains("prototype rocketlauncher 3"))
								tierId = "proto3";
							else if (n.Contains("prototype rocketlauncher ii") || n.Contains("prototype rocketlauncher 2"))
								tierId = "proto2";
							else if (n.Contains("prototype rocketlauncher i") || n.Contains("prototype rocketlauncher 1"))
								tierId = "proto1";
							else if (n.Contains("prototype rocketlauncher 0") || n.Contains("prototype rocketlauncher zero"))
								tierId = "proto0";
						}
					}

					if (!string.IsNullOrEmpty(tierId))
						_prototypeLauncherTierByItem[item.uid.Value] = tierId;

					return tierId;
				}

				private void EnsurePrototypeTierName(Item item)
				{
					if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
						return;

					if (item == null || item.info == null) return;
					if (item.info.shortname != "rocket.launcher") return;

					var tierId = GetPrototypeTierFromItem(item);
					if (string.IsNullOrEmpty(tierId)) return;
					if (_config.PrototypeLauncher.PrototypeLauncherTiers == null) return;
					if (!_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tierId, out var cfg) || cfg == null) return;

					var expected = cfg.DisplayName;
					if (!string.Equals(item.name, expected, StringComparison.Ordinal))
					{
						item.name = expected;
						item.MarkDirty();
					}
				}

				private bool IsPrototypeLauncher(Item item)
				{
					if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
						return false;

					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return false;

					if (_prototypeLauncherTierByItem.ContainsKey(item.uid.Value))
						return true;

					return !string.IsNullOrEmpty(GetPrototypeTierFromItem(item));
				}

				private string GetTierIdForRocket(BaseEntity rocketEntity)
				{
					if (rocketEntity == null)
						return null;

					string tierId = null;

					var sourceItem = rocketEntity.GetItem();
					if (sourceItem != null)
					{
						if (_rocketTierData.TryGetValue(sourceItem.uid.Value, out tierId) && !string.IsNullOrEmpty(tierId))
							return tierId;

						tierId = ResolveRocketTierFromItem(sourceItem);
						if (!string.IsNullOrEmpty(tierId))
							return tierId;
					}

					if (!string.IsNullOrEmpty(rocketEntity.name))
					{
						var rn = rocketEntity.name.ToLower();
						if (rn.Contains("rocket_tier1")) return "rocket_tier1";
						if (rn.Contains("rocket_tier2")) return "rocket_tier2";
						if (rn.Contains("rocket_tier3")) return "rocket_tier3";
					}

					var comp = rocketEntity.GetComponent<RocketTierComponent>();
					if (comp != null && !string.IsNullOrEmpty(comp.Tier))
						return comp.Tier;

					var owner = rocketEntity.creatorEntity as BasePlayer;
					if (owner != null)
					{
						var activeItem = owner.GetActiveItem();
						if (activeItem != null && activeItem.info.shortname == "rocket.launcher")
						{
							tierId = ResolveRocketTierFromItem(activeItem);
							if (!string.IsNullOrEmpty(tierId))
								return tierId;

							var ln = activeItem.name?.ToLower();
							if (!string.IsNullOrEmpty(ln))
							{
								if (ln.Contains("rocket_tier1")) return "rocket_tier1";
								if (ln.Contains("rocket_tier2")) return "rocket_tier2";
								if (ln.Contains("rocket_tier3")) return "rocket_tier3";
							}
						}
					}

					return null;
				}

				private void EnforceLauncherMagazine(Item item, string tierId = null)
				{
					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return;

					ApplyLauncherTierMagazine(item, tierId);

					if (_config != null && _config.General.ForceMagazineOverride == false)
						return;

					NextTick(() => ApplyLauncherTierMagazine(item, tierId));
					timer.Once(0.05f, () => ApplyLauncherTierMagazine(item, tierId));
					timer.Once(0.10f, () => ApplyLauncherTierMagazine(item, tierId));
					timer.Once(0.25f, () => ApplyLauncherTierMagazine(item, tierId));
					timer.Once(0.50f, () => ApplyLauncherTierMagazine(item, tierId));
					timer.Once(1.00f, () => ApplyLauncherTierMagazine(item, tierId));
					timer.Once(2.00f, () => ApplyLauncherTierMagazine(item, tierId));
				}

				private void StartMagazineEnforcer(BasePlayer player, Item launcherItem, string tierId = null)
				{
					if (player == null || launcherItem == null) return;

					StopMagazineEnforcer(player);

					var t = timer.Every(0.10f, () =>
					{
						if (player == null || !player.IsConnected)
						{
							StopMagazineEnforcer(player);
							return;
						}

						var active = player.GetActiveItem();
						if (active == null || active.info == null || active.info.shortname != "rocket.launcher")
						{
							StopMagazineEnforcer(player);
							return;
						}

						EnforceAnyLauncherMagazine(active, tierId);
					});

					_magEnforceTimers[player.userID] = t;
				}
				
				private void EnforceAnyLauncherMagazine(Item item, string tierId = null)
				{
					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return;

					ApplyHeldEntitySkin(item);

					if (_config != null && _config.PrototypeLauncher.EnablePrototypeLauncher && IsPrototypeLauncher(item))
					{
						ApplyPrototypeLauncherMagazine(item, tierId);

						NextTick(() => ApplyPrototypeLauncherMagazine(item, tierId));
						timer.Once(0.05f, () => ApplyPrototypeLauncherMagazine(item, tierId));
						timer.Once(0.15f, () => ApplyPrototypeLauncherMagazine(item, tierId));
						timer.Once(0.35f, () => ApplyPrototypeLauncherMagazine(item, tierId));
						timer.Once(0.75f, () => ApplyPrototypeLauncherMagazine(item, tierId));
						timer.Once(1.50f, () => ApplyPrototypeLauncherMagazine(item, tierId));
						return;
					}

					EnforceLauncherMagazine(item, tierId);

					NextTick(() => EnforceLauncherMagazine(item, tierId));
					timer.Once(0.05f, () => EnforceLauncherMagazine(item, tierId));
					timer.Once(0.15f, () => EnforceLauncherMagazine(item, tierId));
					timer.Once(0.35f, () => EnforceLauncherMagazine(item, tierId));
					timer.Once(0.75f, () => EnforceLauncherMagazine(item, tierId));
					timer.Once(1.50f, () => EnforceLauncherMagazine(item, tierId));
				}

				private void EnforcePrototypeLauncherMagazine(Item item, string tierId = null)
				{
					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return;

					ApplyPrototypeLauncherMagazine(item, tierId);

					if (_config != null && _config.General.ForceMagazineOverride == false)
						return;

					NextTick(() => ApplyPrototypeLauncherMagazine(item, tierId));
					timer.Once(0.05f, () => ApplyPrototypeLauncherMagazine(item, tierId));
					timer.Once(0.10f, () => ApplyPrototypeLauncherMagazine(item, tierId));
					timer.Once(0.25f, () => ApplyPrototypeLauncherMagazine(item, tierId));
					timer.Once(0.50f, () => ApplyPrototypeLauncherMagazine(item, tierId));
					timer.Once(1.00f, () => ApplyPrototypeLauncherMagazine(item, tierId));
					timer.Once(2.00f, () => ApplyPrototypeLauncherMagazine(item, tierId));
				}

				private void ScheduleLauncherMagazineEnforce(Item item)
				{
					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return;

					EnforceAnyLauncherMagazine(item);
					NextTick(() => EnforceAnyLauncherMagazine(item));
					timer.Once(0.05f, () => EnforceAnyLauncherMagazine(item));
					timer.Once(0.10f, () => EnforceAnyLauncherMagazine(item));
					timer.Once(0.25f, () => EnforceAnyLauncherMagazine(item));
					timer.Once(0.50f, () => EnforceAnyLauncherMagazine(item));
					timer.Once(1.00f, () => EnforceAnyLauncherMagazine(item));
					timer.Once(2.00f, () => EnforceAnyLauncherMagazine(item));
				}

				private void EnforceLauncherMagazineFromProjectile(BaseProjectile projectile, BasePlayer player = null)
				{
					if (projectile == null || projectile.primaryMagazine == null)
						return;

					var item = projectile.GetItem();

					if (item == null && player != null)
						item = player.GetActiveItem();

					if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
						return;

					EnforceAnyLauncherMagazine(item);
				}

				private void StopMagazineEnforcer(BasePlayer player)
				{
					if (player == null) return;
					if (_magEnforceTimers.TryGetValue(player.userID, out var t) && t != null)
					{
						t.Destroy();
					}
					_magEnforceTimers.Remove(player.userID);
				}

                private void ApplyRocketDamageOverride(HitInfo info, float damage)  
                {  
                        if (info == null || info.damageTypes == null) return;
                  
                        SetExplosionDamageInDamageTypeList(info.damageTypes, damage);
                        
                  
                        if (_config?.General?.ForceRocketDamageOverride != true) return;
                        
                        var te = info.Initiator?.GetComponent<TimedExplosive>();
                        if (te != null && te.damageTypes != null)
                                SetExplosionDamageInDamageTypeList(te.damageTypes, damage);
                
                        NextTick(() =>  
                        {  
                                if (info != null && info.damageTypes != null)
                                        SetExplosionDamageInDamageTypeList(info.damageTypes, damage);
                        });  
                }

        private bool SetExplosionDamageInDamageTypeList(object damageTypeList, float explosionDamage)
        {
            if (damageTypeList == null) return false;
            try
            {
                var fields = damageTypeList.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo entriesField = null;
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType.IsArray && field.FieldType.GetElementType() == typeof(DamageTypeEntry))
                    {
                        entriesField = field;
                        break;
                    }
                }
                if (entriesField == null) return false;

                var entriesObj = entriesField.GetValue(damageTypeList);
                if (!(entriesObj is DamageTypeEntry[] entries)) return false;

                bool modified = false;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].type == DamageType.Explosion)
                    {
                        entries[i].amount = explosionDamage;
                        modified = true;
                        break;
                    }
                }
                if (modified)
                    entriesField.SetValue(damageTypeList, entries);
                return modified;
            }
            catch (Exception ex)
            {
                PrintError($"[C4RT] SetExplosionDamageInDamageTypeList error: {ex.Message}");
                return false;
            }
        }

        private Item FindItemByUID(BasePlayer player, ulong uid)
        {
            if (player == null) return null;
            Item found = null;

            void Scan(ItemContainer c)
            {
                if (c == null || found != null) return;
                foreach (var it in c.itemList)
                {
                    if (it.uid.Value == uid)
                    {
                        found = it;
                        return;
                    }
                }
            }

            Scan(player.inventory.containerMain);
            Scan(player.inventory.containerBelt);
            Scan(player.inventory.containerWear);

            return found;
        }

        private List<Item> GetPrototypeLaunchers(BasePlayer player)
        {
            var result = new List<Item>();
            if (player == null || _config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher) return result;

            void Scan(ItemContainer c)
            {
                if (c == null) return;
                foreach (var it in c.itemList)
                {
                    if (it.info != null && it.info.shortname == "rocket.launcher" && IsPrototypeLauncher(it))
                        result.Add(it);
                }
            }

            Scan(player.inventory.containerMain);
            Scan(player.inventory.containerBelt);
            Scan(player.inventory.containerWear);

            return result;
        }

        
        private void DestroyPrototypeUI(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            CuiHelper.DestroyUi(player, UI_PROTO_MAIN);
        }

                private void OpenPrototypeUpgradeUI(BasePlayer player)
                {
                        if (player == null || !player.IsConnected) return;
                        if (_config == null) return;

                        if (!_config.PrototypeLauncher.EnablePrototypeLauncher && _config.PrototypeC4.EnablePrototypeC4)
                        {
                                _protoUiTabByPlayer[player.userID] = "c4";
                                OpenPrototypeC4UI(player);
                                return;
                        }

                        if (!_config.PrototypeLauncher.EnablePrototypeLauncher && !_config.PrototypeC4.EnablePrototypeC4)
                        {
                                SendReply(player, "<color=red>Prototype system is disabled.</color>");
                                return;
                        }

                        DestroyPrototypeUI(player);

                        if (!_protoUiTabByPlayer.TryGetValue(player.userID, out var tab) || string.IsNullOrEmpty(tab))
                                tab = "launcher";
                        if (tab == "c4")
                        {
                                OpenPrototypeC4UI(player);
                                return;
                        }

                        var launchers = GetPrototypeLaunchers(player);

                        if (_config.PrototypeLauncher.AllowConvertNormalLauncherToPrototype)
                        {
                                var normal = GetNormalRocketLaunchers(player);
                                if (normal != null && normal.Count > 0)
                                        launchers.AddRange(normal);
                        }

                        var elements = new CuiElementContainer();

                        var mainPanel = new CuiPanel
                        {
                                CursorEnabled = true,
                                Image = { Color = "0 0 0 0.95" },
                                RectTransform =
                                {
                                        AnchorMin = "0.2 0.16",
                                        AnchorMax = "0.8 0.82"
                                }
                        };
                        elements.Add(mainPanel, "Overlay", UI_PROTO_MAIN);

                        if (ImageLibrary != null && !string.IsNullOrEmpty(_config.UI.PrototypeUiImageName))
                        {
                                var png = GetImage(_config.UI.PrototypeUiImageName);
                                if (!string.IsNullOrEmpty(png))
                                {
                                        elements.Add(new CuiElement
                                        {
                                                Parent = UI_PROTO_MAIN,
                                                Name = CuiHelper.GetGuid(),
                                                Components =
                                                {
                                                        new CuiRawImageComponent
                                                        {
                                                                Png = png,
                                                                Color = $"1 1 1 {_config?.UI?.PrototypeUiImageAlpha}"
                                                        },
                                                        new CuiRectTransformComponent
                                                        {
                                                                AnchorMin = "0 0",
                                                                AnchorMax = "1 1"
                                                        }
                                                }
                                        });
                                }
                        }

                        var contentPanel = new CuiPanel
                        {
                                Image = { Color = "0 0 0 0.55" },
                                RectTransform =
                                {
                                        AnchorMin = "0.05 0.07",
                                        AnchorMax = "0.95 0.93"
                                }
                        };
                        var contentName = elements.Add(contentPanel, UI_PROTO_MAIN);

                        elements.Add(new CuiLabel
                        {
                                Text =
                                {
                                        Text = "Prototype Rocketlauncher Upgrades",
                                        FontSize = 18,
                                        Align = TextAnchor.MiddleCenter,
                                        Color = "0.75 0.9 1 1"
                                },
                                RectTransform =
                                {
                                        AnchorMin = "0.1 0.9",
                                        AnchorMax = "0.9 0.99"
                                }
                        }, contentName);

                        int enabledTabs = (_config.PrototypeLauncher.EnablePrototypeLauncher ? 1 : 0) + (_config.PrototypeC4.EnablePrototypeC4 ? 1 : 0);
                        if (enabledTabs == 2)
                        {
                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.tab launcher", Color = "0.15 0.25 0.35 0.85" },
                                        Text = { Text = "Prototype Rocketlauncher", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                                        RectTransform = { AnchorMin = "0.1 0.845", AnchorMax = "0.48 0.89" }
                                }, contentName);

                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.tab c4", Color = "0.15 0.25 0.35 0.85" },
                                        Text = { Text = "Prototype C4", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                                        RectTransform = { AnchorMin = "0.52 0.845", AnchorMax = "0.9 0.89" }
                                }, contentName);
                        }

                        elements.Add(new CuiButton
                        {
                                Button =
                                {
                                        Command = "c4rt.proto.close",
                                        Color = "0.5 0.15 0.15 0.9"
                                },
                                Text =
                                {
                                        Text = "X",
                                        FontSize = 12,
                                        Align = TextAnchor.MiddleCenter
                                },
                                RectTransform =
                                {
                                        AnchorMin = "0.95 0.94",
                                        AnchorMax = "0.99 0.99"
                                }
                        }, contentName);

                        if (launchers.Count == 0)
                        {
                                elements.Add(new CuiLabel
                                {
                                        Text =
                                        {
                                                Text = _config.PrototypeLauncher.AllowConvertNormalLauncherToPrototype
                                                        ? "You do not have any Prototype Rocketlaunchers or normal Rocketlaunchers to convert."
                                                        : "You do not have any Prototype Rocketlaunchers in your inventory.",
                                                FontSize = 14,
                                                Align = TextAnchor.MiddleCenter,
                                                Color = "0.85 0.9 1 1"
                                        },
                                        RectTransform =
                                        {
                                                AnchorMin = "0.05 0.4",
                                                AnchorMax = "0.95 0.6"
                                        }
                                }, contentName);

                                CuiHelper.AddUi(player, elements);
                                return;
                        }

                        const int itemsPerPage = 3;
                        int total = launchers.Count;
                        int totalPages = (total + itemsPerPage - 1) / itemsPerPage;
                        if (totalPages < 1) totalPages = 1;

                        int page = 0;
                        _protoUiPageByPlayer.TryGetValue(player.userID, out page);
                        if (page < 0) page = 0;
                        if (page > totalPages - 1) page = totalPages - 1;
                        _protoUiPageByPlayer[player.userID] = page;

                        float startY = 0.82f;
                        float rowHeight = 0.22f;
                        float spacing = 0.02f;

                        int startIndex = page * itemsPerPage;
                        int endIndex = Mathf.Min(startIndex + itemsPerPage, total);

                        int rowIndex = 0;
                        for (int i = startIndex; i < endIndex; i++, rowIndex++)
                        {
                                var it = launchers[i];
                                float yMax = startY - rowIndex * (rowHeight + spacing);
                                float yMin = yMax - rowHeight;

                                var outerPanel = new CuiPanel
                                {
                                        Image = { Color = "0 0.75 1 0.25" },
                                        RectTransform = { AnchorMin = $"0.03 {yMin}", AnchorMax = $"0.97 {yMax}" }
                                };
                                var outerName = elements.Add(outerPanel, contentName);

                                var rowPanel = new CuiPanel
                                {
                                        Image = { Color = "0 0 0 0.7" },
                                        RectTransform = { AnchorMin = "0.01 0.14", AnchorMax = "0.99 0.86" }
                                };
                                var rowName = elements.Add(rowPanel, outerName);

                                if (!IsPrototypeLauncher(it))
                                {
                                        _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue("proto0", out var p0);
                                        var p0Basic = p0 != null ? p0.BasicExplosionDamage : 0f;
                                        var p0Hv = p0 != null ? p0.HvExplosionDamage : 0f;

                                        var costText = FormatCosts(_config.PrototypeLauncher.ConvertLauncherToProto0Costs, 1);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = $"Normal Rocket Launcher (x{it.amount})", FontSize = 13, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                                                RectTransform = { AnchorMin = "0.02 0.62", AnchorMax = "0.72 0.92" }
                                        }, rowName);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = $"Convert -> proto0   |   Basic {p0Basic:0}   |   HV {p0Hv:0}", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.9 0.9 0.9 1" },
                                                RectTransform = { AnchorMin = "0.02 0.18", AnchorMax = "0.72 0.40" }
                                        }, rowName);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = costText, FontSize = 10, Align = TextAnchor.LowerRight, Color = "0.85 0.85 0.85 1" },
                                                RectTransform = { AnchorMin = "0.60 0.02", AnchorMax = "0.98 0.18" }
                                        }, rowName);

                                        if (_config.PrototypeLauncher.AllowConvertNormalLauncherToPrototype)
                                        {
                                                elements.Add(new CuiButton
                                                {
                                                        Button = { Command = $"c4rt.convert.launcher {it.uid.Value}", Color = "0.6 0.45 0.1 0.9" },
                                                        Text = { Text = "Convert", FontSize = 12, Align = TextAnchor.MiddleCenter },
                                                        RectTransform = { AnchorMin = "0.74 0.35", AnchorMax = "0.96 0.85" }
                                                }, rowName);
                                        }

                                        continue;
                                }

                                string tierId = GetPrototypeTierFromItem(it);
                                _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tierId ?? string.Empty, out var currentCfg);

                                float currentMult = currentCfg?.DamageMultiplier ?? 1f;
                                int currentMag = currentCfg?.MagazineSize ?? 1;
                                float currentWear = currentCfg?.ConditionLossPerShot ?? 0f;
                                float curBasic = (currentCfg != null && currentCfg.BasicExplosionDamage > 0f) ? currentCfg.BasicExplosionDamage : 0f;
                                float curHv = (currentCfg != null && currentCfg.HvExplosionDamage > 0f) ? currentCfg.HvExplosionDamage : 0f;

                                string condStr = it.hasCondition ? $"{Mathf.RoundToInt(it.condition / it.maxCondition * 100f)}%" : "N/A";
                                string lore = currentCfg?.Info ?? string.Empty;

                                PrototypeLauncherTier nextCfg = null;
                                string nextTierId = null;

                                if (!string.IsNullOrEmpty(tierId) &&
                                        _config.PrototypeLauncher.PrototypeLauncherUpgradeConfig != null &&
                                        _config.PrototypeLauncher.PrototypeLauncherUpgradeConfig.TryGetValue(tierId, out var upCfg) &&
                                        upCfg != null &&
                                        !string.IsNullOrEmpty(upCfg.ToTier) &&
                                        _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(upCfg.ToTier, out nextCfg) &&
                                        nextCfg != null)
                                {
                                        nextTierId = upCfg.ToTier;
                                }

                                string currentDisplayName = GetPrototypeLauncherDisplayName(tierId);
                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = currentDisplayName, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.8 0.9 1 1" },
                                        RectTransform = { AnchorMin = "0.02 0.62", AnchorMax = "0.7 0.92" }
                                }, rowName);

                                if (!string.IsNullOrEmpty(lore))
                                {
                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = lore, FontSize = 9, Align = TextAnchor.UpperLeft, Color = "0.78 0.88 1 1" },
                                                RectTransform = { AnchorMin = "0.02 0.40", AnchorMax = "0.7 0.62" }
                                        }, rowName);
                                }
                                /*
                                string statsLine;
                                {
                                        statsLine = $"Current: x{currentMult:0.0} dmg   |   mag {currentMag}   |   wear/shot {currentWear:0.0}   |   condition {condStr}";
                                }
                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = statsLine, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.9 0.95 1 1" },
                                        RectTransform = { AnchorMin = "0.02 0.20", AnchorMax = "0.7 0.40" }
                                }, rowName);
                        */
                                if (nextCfg != null)
                                {
                                        float nextBasic = (nextCfg.BasicExplosionDamage > 0f) ? nextCfg.BasicExplosionDamage : 0f;
                                        float nextHv = (nextCfg.HvExplosionDamage > 0f) ? nextCfg.HvExplosionDamage : 0f;

                                        if (nextBasic <= 0f) nextBasic = Mathf.RoundToInt(curBasic);
                                        if (nextHv <= 0f) nextHv = Mathf.RoundToInt(curHv);

                                        string beforeAfter =
                                                $"Upgrade: {Mathf.RoundToInt(curBasic)} -> {Mathf.RoundToInt(nextBasic)} dmg   |   HV {Mathf.RoundToInt(curHv)} -> {Mathf.RoundToInt(nextHv)}" +
                                                $", mag {currentMag} -> {nextCfg.MagazineSize}, wear {currentWear:0.0} -> {nextCfg.ConditionLossPerShot:0.0}";

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = beforeAfter, FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "0.7 0.85 1 1" },
                                                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.7 0.20" }
                                        }, rowName);
                                }
                                else
                                {
                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = "Max tier reached", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                                                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.7 0.20" }
                                        }, rowName);
                                }

                                string buttonText;
                                string buttonColor;
                                string buttonCommand;

                                if (nextCfg != null)
                                {
                                        buttonText = "Upgrade";
                                        buttonColor = "0.18 0.6 0.3 0.9";
                                        buttonCommand = $"c4rt.proto.upgrade {it.uid.Value}";
                                }
                                else
                                {
                                        buttonText = "Max";
                                        buttonColor = "0.2 0.2 0.2 0.8";
                                        buttonCommand = string.Empty;
                                }

                                elements.Add(new CuiButton
                                {
                                        Button = { Command = buttonCommand, Color = buttonColor },
                                        Text = { Text = buttonText, FontSize = 12, Align = TextAnchor.MiddleCenter },
                                        RectTransform = { AnchorMin = "0.74 0.35", AnchorMax = "0.96 0.85" }
                                }, rowName);

                                if (!string.IsNullOrEmpty(tierId) &&
                                        _config.PrototypeLauncher.PrototypeLauncherUpgradeConfig != null &&
                                        _config.PrototypeLauncher.PrototypeLauncherUpgradeConfig.TryGetValue(tierId, out var upCfg2) &&
                                        upCfg2 != null &&
                                        upCfg2.Costs != null &&
                                        upCfg2.Costs.Count > 0)
                                {
                                        var parts = new List<string>();
                                        foreach (var c in upCfg2.Costs)
                                                parts.Add($"{c.Amount}x {c.ShortName}");

                                        string costText = "Cost: " + string.Join(", ", parts);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = costText, FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "0.8 0.9 1 1" },
                                                RectTransform = { AnchorMin = "0.74 0.05", AnchorMax = "0.96 0.30" }
                                        }, rowName);
                                }
                        }

                        if (totalPages > 1)
                        {
                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.page prev", Color = "0.15 0.4 0.6 0.9" },
                                        Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter },
                                        RectTransform = { AnchorMin = "0.30 0.01", AnchorMax = "0.36 0.09" }
                                }, contentName);

                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = $"Page {page + 1} / {totalPages}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.9 1 1" },
                                        RectTransform = { AnchorMin = "0.36 0.01", AnchorMax = "0.64 0.09" }
                                }, contentName);

                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.page next", Color = "0.15 0.4 0.6 0.9" },
                                        Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter },
                                        RectTransform = { AnchorMin = "0.64 0.01", AnchorMax = "0.70 0.09" }
                                }, contentName);
                        }

                        CuiHelper.AddUi(player, elements);
                }

                [ChatCommand("proto")]
                private void CmdProtoMenu(BasePlayer player, string command, string[] args)
                {
                        if (_config == null) return;

                        if (!_config.PrototypeLauncher.EnablePrototypeLauncher && _config.PrototypeC4.EnablePrototypeC4)
                        {
                                _protoUiTabByPlayer[player.userID] = "c4";
                                OpenPrototypeC4UI(player);
                                return;
                        }

                        if (_config.PrototypeLauncher.EnablePrototypeLauncher && !_config.PrototypeC4.EnablePrototypeC4)
                        {
                                _protoUiTabByPlayer[player.userID] = "launcher";
                                OpenPrototypeUpgradeUI(player);
                                return;
                        }

                        OpenPrototypeUpgradeUI(player);
                }

        private void TryUpgradePrototypeLauncher(BasePlayer player, Item item)
        {
            if (player == null || item == null) return;
            if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher) return;

            if (!IsPrototypeLauncher(item))
            {
                SendReply(player, "<color=red>This launcher is not a Prototype Rocketlauncher.</color>");
                return;
            }

            var currentTier = GetPrototypeTierFromItem(item);
            if (string.IsNullOrEmpty(currentTier))
            {
                SendReply(player, "<color=red>Could not determine this launcher's tier.</color>");
                return;
            }

            if (_config.PrototypeLauncher.PrototypeLauncherUpgradeConfig == null
                || !_config.PrototypeLauncher.PrototypeLauncherUpgradeConfig.TryGetValue(currentTier, out var upCfg)
                || upCfg == null
                || string.IsNullOrEmpty(upCfg.ToTier))
            {
                SendReply(player, "<color=yellow>No further upgrades available for this launcher.</color>");
                return;
            }

            if (upCfg.Costs == null || upCfg.Costs.Count == 0)
            {
                SendReply(player, "<color=red>Upgrade costs are not configured.</color>");
                return;
            }

            var missing = new List<string>();
            foreach (var cost in upCfg.Costs)
            {
                var def = ItemManager.FindItemDefinition(cost.ShortName);
                if (def == null)
                {
                    missing.Add($"{cost.Amount}x {cost.ShortName} (invalid item)");
                    continue;
                }

                int have = player.inventory.GetAmount(def.itemid);
                if (have < cost.Amount)
                {
                    missing.Add($"{cost.Amount}x {cost.ShortName} (you have {have})");
                }
            }

            if (missing.Count > 0)
            {
                SendReply(player, "<color=red>Not enough resources for upgrade:</color>");
                foreach (var line in missing)
                    SendReply(player, "<color=orange>- " + line + "</color>");
                return;
            }

            foreach (var cost in upCfg.Costs)
            {
                var def = ItemManager.FindItemDefinition(cost.ShortName);
                if (def == null) continue;
                player.inventory.Take(null, def.itemid, cost.Amount);
            }
            //player.inventory.SendUpdatedInventory();

            var nextTier = upCfg.ToTier;
            _prototypeLauncherTierByItem[item.uid.Value] = nextTier;

            if (_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(nextTier, out var cfg))
            {
                EnsurePrototypeLauncherName(item, _prototypeLauncherTierByItem[item.uid.Value]);
                if (cfg.SkinId != 0)
                    item.skin = cfg.SkinId;
            }

            item.MarkDirty();
            ApplyHeldEntitySkin(item);
            ApplyPrototypeLauncherMagazine(item, nextTier);

            SendReply(player, $"<color=green>Your Prototype Rocketlauncher has been upgraded to {nextTier.ToUpper()}.</color>");
        }

        [ConsoleCommand("c4rt.proto.close")]
        private void CC_ProtoUIClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            DestroyPrototypeUI(player);
        }

        [ConsoleCommand("c4rt.proto.upgrade")]
        private void CC_ProtoUIUpgrade(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (arg.Args == null || arg.Args.Length < 1) return;
            if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
            {
                SendReply(player, "<color=red>Prototype Rocketlauncher system is disabled.</color>");
                DestroyPrototypeUI(player);
                return;
            }

            if (!ulong.TryParse(arg.Args[0].ToString(), out var uid))
                return;

            var item = FindItemByUID(player, uid);
            if (item == null || item.info == null || item.info.shortname != "rocket.launcher")
            {
                SendReply(player, "<color=red>This Prototype Rocketlauncher no longer exists.</color>");
                DestroyPrototypeUI(player);
                return;
            }

            TryUpgradePrototypeLauncher(player, item);
            OpenPrototypeUpgradeUI(player);
        }

        
        [ConsoleCommand("c4rt.proto.page")]
        private void CC_ProtoUIPage(ConsoleSystem.Arg arg) 
        {
            var player = arg.Player();
            if (player == null) return;
            if (_config == null || (!_config.PrototypeLauncher.EnablePrototypeLauncher && !_config.PrototypeC4.EnablePrototypeC4)) return;

            string dir = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0].ToString().ToLower() : string.Empty;
            if (!_protoUiPageByPlayer.TryGetValue(player.userID, out var page))
                page = 0;

            if (dir == "next")
                page += 1;
            else if (dir == "prev" || dir == "previous" || dir == "back")
                page -= 1;

            _protoUiPageByPlayer[player.userID] = page;
            OpenPrototypeUpgradeUI(player);
        }

                [ConsoleCommand("c4rt.proto.info")]
        private void CC_ProtoInfo(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher)
            {
                SendReply(player, "<color=red>Prototype Rocketlauncher system is disabled.</color>");
                return;
            }

            var launchers = GetPrototypeLaunchers(player);
            if (launchers == null || launchers.Count == 0)
            {
                SendReply(player, "<color=#88c0ff>Prototype Rocketlauncher:</color> You don't currently have a Prototype Rocketlauncher in your inventory.");
                return;
            }

            var item = launchers[0];
            var tierId = GetPrototypeTierFromItem(item) ?? "proto0";

            if (_config.PrototypeLauncher.PrototypeLauncherTiers != null &&
                _config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tierId, out var cfg) &&
                cfg != null)
            {
                SendReply(player, $"<color=#88c0ff>{cfg.DisplayName}:</color> {cfg.Info}");
            }
            else
            {
                SendReply(player, "<color=#88c0ff>Prototype Rocketlauncher:</color> Classified experimental launcher. No records found for this tier.");
            }
        }


        private void OnServerInitialized()
        {
            if (ImageLibrary == null)
            {
                PrintWarning("ImageLibrary plugin not found. Prototype UI background image will not be loaded.");
                return;
            }

            if (!string.IsNullOrEmpty(_config?.UI?.PrototypeUiImageUrl) &&
                !string.IsNullOrEmpty(_config?.UI?.PrototypeUiImageName))
            {
                ImageLibrary.Call("AddImage", _config.UI.PrototypeUiImageUrl, _config.UI.PrototypeUiImageName);
            }
        }

        private string GetImage(string name, ulong skinId = 0UL)
        {
            if (ImageLibrary == null || string.IsNullOrEmpty(name))
                return null;

            return ImageLibrary.Call("GetImage", name, skinId) as string;
        }

                [ChatCommand("prototype")]
                private void CmdPrototypeAlias(BasePlayer player, string command, string[] args)
                {
                        if (_config == null) return;

                        if (!_config.PrototypeLauncher.EnablePrototypeLauncher && _config.PrototypeC4.EnablePrototypeC4)
                        {
                                OpenPrototypeC4UI(player);
                                return;
                        }

                        OpenPrototypeUpgradeUI(player);
                }

        [ChatCommand("giveprotoc4")]
        private void CmdGivePrototypeC4(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
            {
                SendReply(player, "<color=red>No permission!</color>");
                return;
            }

            if (_config == null || !_config.PrototypeC4.EnablePrototypeC4)
            {
                SendReply(player, "<color=red>Prototype C4 system is disabled.</color>");
                return;
            }

            if (args.Length < 1)
            {
                SendReply(player, "Usage: <color=yellow>/giveprotoc4 <0|1|2|3|4|proto_c4_0..4> [amount]</color>");
                return;
            }

            string tierId = NormalizePrototypeC4Tier(args[0]);
            int amount = 1;
            if (args.Length >= 2) int.TryParse(args[1], out amount);
            amount = Mathf.Clamp(amount, 1, 10000);

            if (_config.PrototypeC4.PrototypeC4Tiers == null || !_config.PrototypeC4.PrototypeC4Tiers.TryGetValue(tierId, out var cfg) || cfg == null)
            {
                SendReply(player, "<color=red>Invalid Prototype C4 tier!</color>");
                return;
            }

            var item = ItemManager.CreateByName("explosive.timed", amount);
            if (item == null)
            {
                SendReply(player, "<color=red>Failed to create C4 item.</color>");
                return;
            }

            _prototypeC4TierByItem[item.uid.Value] = tierId;
            item.name = tierId.ToUpper();
            if (cfg.SkinId != 0) item.skin = cfg.SkinId;
            item.MarkDirty();

            player.inventory.GiveItem(item);
            SendReply(player, $"<color=green>You have received {amount}x {item.name}.</color>");
        }

        [ChatCommand("giverocketammo")]
        private void CmdGiveRocketAmmo(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "customc4rockettiers.admin"))
            {
                SendReply(player, "<color=red>No permission!</color>");
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, "Usage: <color=yellow>/giverocketammo <basic|hv> <tier1|tier2|tier3> [amount]</color>");
                return;
            }

            var type = args[0].ToLower();
            bool isHV = type == "hv" || type == "h" || type == "high";
            string tierArg = args[1].ToLower();
            string tierId = "rocket_tier" + tierArg.Replace("tier", "");

            int amount = 1;
            if (args.Length >= 3) int.TryParse(args[2], out amount);
            amount = Mathf.Clamp(amount, 1, 10000);

            if (!isHV)
            {
                if (_config == null || _config.BaseRockets.RocketTiers == null || !_config.BaseRockets.RocketTiers.ContainsKey(tierId))
                {
                    SendReply(player, "<color=red>Invalid tier!</color>");
                    return;
                }
            }
            else
            {
                if (_config == null || _config.BaseRockets.RocketHvTiers == null || !_config.BaseRockets.RocketHvTiers.ContainsKey(tierId))
                {
                    SendReply(player, "<color=red>Invalid tier!</color>");
                    return;
                }
            }

            string shortname = isHV ? "ammo.rocket.hv" : "ammo.rocket.basic";
            var item = ItemManager.CreateByName(shortname, amount);
            if (item == null)
            {
                SendReply(player, "<color=red>Failed to create rocket item.</color>");
                return;
            }

            _rocketTierData[item.uid.Value] = tierId;

            var cfg = isHV ? _config.BaseRockets.RocketHvTiers[tierId] : _config.BaseRockets.RocketTiers[tierId];
            item.name = (isHV ? "HV_" : "BASIC_") + tierId.ToUpper();
            if (cfg != null && cfg.SkinId != 0) item.skin = cfg.SkinId;

            item.MarkDirty();
            player.inventory.GiveItem(item);

            SendReply(player, $"<color=green>You have received {amount}x {item.name}.</color>");
        }

        [ConsoleCommand("c4rt.give.c4")]
        private void CC_AliasGiveC4(ConsoleSystem.Arg arg) => CC_GiveC4(arg);

        [ConsoleCommand("c4rt.give.rocket")]
        private void CC_AliasGiveRocketAmmo(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3)
            {
                arg.ReplyWith("Usage: c4rt.give.rocket <player|steamid> <basic|hv> <tier1|tier2|tier3> [amount]");
                return;
            }

            var target = FindPlayerByArg(arg.Args[0].ToString());
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            var type = arg.Args[1].ToString().ToLower();
            bool isHV = type == "hv" || type == "h" || type == "high";
            var tierArg = arg.Args[2].ToString().ToLower();
            string tierId = "rocket_tier" + tierArg.Replace("tier", "");
            int amount = 1;
            if (arg.Args.Length >= 4) int.TryParse(arg.Args[3].ToString(), out amount);
            amount = Mathf.Clamp(amount, 1, 100000);

            if (!isHV)
            {
                if (_config == null || _config.BaseRockets.RocketTiers == null || !_config.BaseRockets.RocketTiers.ContainsKey(tierId))
                {
                    arg.ReplyWith("Invalid tier.");
                    return;
                }
            }
            else
            {
                if (_config == null || _config.BaseRockets.RocketHvTiers == null || !_config.BaseRockets.RocketHvTiers.ContainsKey(tierId))
                {
                    arg.ReplyWith("Invalid tier.");
                    return;
                }
            }

            string shortname = isHV ? "ammo.rocket.hv" : "ammo.rocket.basic";
            var item = ItemManager.CreateByName(shortname, amount);
            if (item == null)
            {
                arg.ReplyWith("Failed to create rocket item.");
                return;
            }

            _rocketTierData[item.uid.Value] = tierId;
            var cfg = isHV ? _config.BaseRockets.RocketHvTiers[tierId] : _config.BaseRockets.RocketTiers[tierId];
            item.name = (isHV ? "HV_" : "BASIC_") + tierId.ToUpper();
            if (cfg != null && cfg.SkinId != 0) item.skin = cfg.SkinId;

            item.MarkDirty();
            target.inventory.GiveItem(item);
            arg.ReplyWith($"Gave {amount}x {item.name} to {target.displayName}.");
        }

        [ConsoleCommand("c4rt.give.proto.launcher")]
        private void CC_AliasGiveProtoLauncher(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: c4rt.give.proto.launcher <player|steamid> <proto0|proto1|proto2|proto3|proto4>");
                return;
            }

            var target = FindPlayerByArg(arg.Args[0].ToString());
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            string tier = arg.Args[1].ToString().ToLower();
            GivePrototypeLauncherTier(null, target, tier);
            arg.ReplyWith($"Gave {tier} to {target.displayName}.");
        }

        [ConsoleCommand("c4rt.give.proto.c4")]
        private void CC_AliasGiveProtoC4(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: c4rt.give.proto.c4 <player|steamid> <0|1|2|3|4|proto_c4_0..4> [amount]");
                return;
            }

            var target = FindPlayerByArg(arg.Args[0].ToString());
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            string tierId = NormalizePrototypeC4Tier(arg.Args[1].ToString());
            int amount = 1;
            if (arg.Args.Length >= 3) int.TryParse(arg.Args[2].ToString(), out amount);
            amount = Mathf.Clamp(amount, 1, 100000);

            if (_config == null || !_config.PrototypeC4.EnablePrototypeC4 || _config.PrototypeC4.PrototypeC4Tiers == null || !_config.PrototypeC4.PrototypeC4Tiers.TryGetValue(tierId, out var cfg) || cfg == null)
            {
                arg.ReplyWith("Prototype C4 system disabled or tier invalid.");
                return;
            }

            var item = ItemManager.CreateByName("explosive.timed", amount);
            if (item == null)
            {
                arg.ReplyWith("Failed to create item.");
                return;
            }

            _prototypeC4TierByItem[item.uid.Value] = tierId;
            item.name = tierId.ToUpper();
            if (cfg.SkinId != 0) item.skin = cfg.SkinId;
            item.MarkDirty();

            target.inventory.GiveItem(item);
            arg.ReplyWith($"Gave {amount}x {tierId} to {target.displayName}.");
        }

        [ConsoleCommand("c4rt.proto.tab")]
        private void CC_ProtoTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (arg.Args == null || arg.Args.Length < 1) return;

			string tab = arg.Args[0].ToString().ToLower();
			if (tab != "launcher" && tab != "c4") tab = "launcher";
            _protoUiTabByPlayer[player.userID] = tab;
            _protoUiPageByPlayer[player.userID] = 0;
            OpenPrototypeUpgradeUI(player);
        }

        [ConsoleCommand("c4rt.protoc4.upgrade")]
        private void CC_ProtoC4Upgrade(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (arg.Args == null || arg.Args.Length < 1) return;

            if (!ulong.TryParse(arg.Args[0].ToString(), out var uid))
                return;

            var item = FindItemByUID(player, uid);
            if (item == null || item.info == null || item.info.shortname != "explosive.timed")
                return;

            TryUpgradePrototypeC4(player, item);
            OpenPrototypeUpgradeUI(player);
        }

        [ConsoleCommand("c4rt.protoc4.close")]
        private void CC_ProtoC4Close(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            DestroyPrototypeUI(player);
        }

        private string NormalizePrototypeC4Tier(string input)
        {
            if (string.IsNullOrEmpty(input)) return "proto_c4_0";
            var s = input.Trim().ToLower();
            if (s.StartsWith("proto_c4_")) return s;
            if (s == "0" || s == "t0") return "proto_c4_0";
            if (s == "1" || s == "t1") return "proto_c4_1";
            if (s == "2" || s == "t2") return "proto_c4_2";
            if (s == "3" || s == "t3") return "proto_c4_3";
            if (s == "4" || s == "t4") return "proto_c4_4";
            return s;
        }

        private string GetPrototypeC4TierFromItem(Item item)
        {
            if (item == null || item.info == null || item.info.shortname != "explosive.timed")
                return null;

            if (_prototypeC4TierByItem.TryGetValue(item.uid.Value, out var tier) && !string.IsNullOrEmpty(tier))
                return tier;

            if (item.skin != 0 && _config != null && _config.PrototypeC4.PrototypeC4Tiers != null)
            {
                foreach (var kvp in _config.PrototypeC4.PrototypeC4Tiers)
                {
                    if (kvp.Value != null && kvp.Value.SkinId != 0 && kvp.Value.SkinId == item.skin)
                        return kvp.Key;
                }
            }

            var name = (item.name ?? "").ToLower();
            if (name.Contains("proto_c4_0")) return "proto_c4_0";
            if (name.Contains("proto_c4_1")) return "proto_c4_1";
            if (name.Contains("proto_c4_2")) return "proto_c4_2";
            if (name.Contains("proto_c4_3")) return "proto_c4_3";
            if (name.Contains("proto_c4_4")) return "proto_c4_4";

            return null;
        }

                private void EnsurePrototypeC4TierName(Item item)
                {
                        if (item == null || item.info == null || item.info.shortname != "explosive.timed") return;

                        var tier = GetPrototypeC4TierFromItem(item);
                        if (string.IsNullOrEmpty(tier)) return;

                        if (_config != null && _config.PrototypeC4.PrototypeC4Tiers != null &&
                                _config.PrototypeC4.PrototypeC4Tiers.TryGetValue(tier, out var cfg) &&
                                cfg != null && cfg.SkinId != 0)
                                item.skin = cfg.SkinId;

                        EnsurePrototypeC4Name(item, tier);
                }

        private void TryUpgradePrototypeC4(BasePlayer player, Item item)
        {
            if (player == null || item == null) return;
            if (_config == null || !_config.PrototypeC4.EnablePrototypeC4) return;
            if (_config.PrototypeC4.PrototypeC4UpgradeConfig == null || _config.PrototypeC4.PrototypeC4Tiers == null) return;

            string currentTier = GetPrototypeC4TierFromItem(item);
            if (string.IsNullOrEmpty(currentTier))
            {
                SendReply(player, "<color=red>This C4 is not a Prototype C4 item.</color>");
                return;
            }

            if (!_config.PrototypeC4.PrototypeC4UpgradeConfig.TryGetValue(currentTier, out var upCfg) || upCfg == null || string.IsNullOrEmpty(upCfg.ToTier))
            {
                SendReply(player, "<color=yellow>No further upgrades available for this Prototype C4.</color>");
                return;
            }

            int units = 1;
            if (_config.PrototypeC4.AllowStackPrototypeC4Upgrades && item.amount > 1)
                units = item.amount;

            if (upCfg.Costs == null || upCfg.Costs.Count == 0)
            {
                SendReply(player, "<color=red>Upgrade costs are not configured.</color>");
                return;
            }

            var missing = new List<string>();
            foreach (var cost in upCfg.Costs)
            {
                var def = ItemManager.FindItemDefinition(cost.ShortName);
                if (def == null)
                {
                    missing.Add($"{cost.Amount * units}x {cost.ShortName} (invalid item)");
                    continue;
                }

                int need = cost.Amount * units;
                int have = player.inventory.GetAmount(def.itemid);
                if (have < need)
                    missing.Add($"{need}x {cost.ShortName} (you have {have})");
            }

            if (missing.Count > 0)
            {
                SendReply(player, "<color=red>Not enough resources for Prototype C4 upgrade:</color>");
                foreach (var line in missing)
                    SendReply(player, "<color=orange>- " + line + "</color>");
                return;
            }

            foreach (var cost in upCfg.Costs)
            {
                var def = ItemManager.FindItemDefinition(cost.ShortName);
                if (def == null) continue;
                player.inventory.Take(null, def.itemid, cost.Amount * units);
            }

                        string nextTier = upCfg.ToTier;
                        _prototypeC4TierByItem[item.uid.Value] = nextTier;

                        if (_config.PrototypeC4.PrototypeC4Tiers.TryGetValue(nextTier, out var cfg) && cfg != null)
                        {
                                if (cfg.SkinId != 0) item.skin = cfg.SkinId;

                                EnsurePrototypeC4Name(item, nextTier);
                        }
                        else
                        {
                                item.MarkDirty();
                        }

                        SendReply(player, $"<color=green>Prototype C4 upgraded to {nextTier.ToUpper()}.</color>");
        }

        private List<Item> GetPrototypeC4Items(BasePlayer player)
        {
            var list = new List<Item>();
            if (player == null) return list;

            var items = GetAllItems(player);
            foreach (var it in items)
            {
                if (it == null || it.info == null) continue;
                if (it.info.shortname != "explosive.timed") continue;
                if (!string.IsNullOrEmpty(GetPrototypeC4TierFromItem(it)))
                    list.Add(it);
            }
            return list;
        }

                private void OpenPrototypeC4UI(BasePlayer player)
                {
                        if (player == null || !player.IsConnected) return;
                        if (_config == null || !_config.PrototypeC4.EnablePrototypeC4)
                        {
                                SendReply(player, "<color=red>Prototype C4 system is disabled.</color>");
                                return;
                        }

                        DestroyPrototypeUI(player);

                        var items = GetPrototypeC4Items(player);

                        if (_config.PrototypeC4.AllowConvertNormalC4ToPrototype)
                        {
                                var normal = GetNormalC4(player);
                                if (normal != null && normal.Count > 0)
                                        items.AddRange(normal);
                        }

                        var elements = new CuiElementContainer();

                        var mainPanel = new CuiPanel
                        {
                                CursorEnabled = true,
                                Image = { Color = "0 0 0 0.95" },
                                RectTransform = { AnchorMin = "0.2 0.16", AnchorMax = "0.8 0.82" }
                        };
                        elements.Add(mainPanel, "Overlay", UI_PROTO_MAIN);

                        if (ImageLibrary != null && !string.IsNullOrEmpty(_config.UI.PrototypeUiImageName))
                        {
                                var png = GetImage(_config.UI.PrototypeUiImageName);
                                if (!string.IsNullOrEmpty(png))
                                {
                                        elements.Add(new CuiElement
                                        {
                                                Parent = UI_PROTO_MAIN,
                                                Name = CuiHelper.GetGuid(),
                                                Components =
                                                {
                                                        new CuiRawImageComponent { Png = png, Color = $"1 1 1 {_config?.UI?.PrototypeUiImageAlpha}" },
                                                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                                                }
                                        });
                                }
                        }

                        var contentPanel = new CuiPanel
                        {
                                Image = { Color = "0 0 0 0.55" },
                                RectTransform = { AnchorMin = "0.05 0.07", AnchorMax = "0.95 0.93" }
                        };
                        var contentName = elements.Add(contentPanel, UI_PROTO_MAIN);

                        elements.Add(new CuiLabel
                        {
                                Text = { Text = "Prototype C4 Upgrades", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "0.75 0.9 1 1" },
                                RectTransform = { AnchorMin = "0.1 0.9", AnchorMax = "0.9 0.99" }
                        }, contentName);

                        if (_config.PrototypeLauncher.EnablePrototypeLauncher && _config.PrototypeC4.EnablePrototypeC4)
                        {
                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.tab launcher", Color = "0.15 0.25 0.35 0.85" },
                                        Text = { Text = "Prototype Rocketlauncher", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                                        RectTransform = { AnchorMin = "0.1 0.845", AnchorMax = "0.48 0.89" }
                                }, contentName);

                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.tab c4", Color = "0.15 0.25 0.35 0.85" },
                                        Text = { Text = "Prototype C4", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                                        RectTransform = { AnchorMin = "0.52 0.845", AnchorMax = "0.9 0.89" }
                                }, contentName);
                        }

                        elements.Add(new CuiButton
                        {
                                Button = { Command = "c4rt.proto.close", Color = "0.5 0.15 0.15 0.9" },
                                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter },
                                RectTransform = { AnchorMin = "0.95 0.94", AnchorMax = "0.99 0.99" }
                        }, contentName);

                        if (items.Count == 0)
                        {
                                elements.Add(new CuiLabel
                                {
                                        Text =
                                        {
                                                Text = _config.PrototypeC4.AllowConvertNormalC4ToPrototype
                                                        ? "You do not have any Prototype C4 or normal C4 to convert."
                                                        : "You do not have any Prototype C4 in your inventory.",
                                                FontSize = 14,
                                                Align = TextAnchor.MiddleCenter,
                                                Color = "0.85 0.9 1 1"
                                        },
                                        RectTransform = { AnchorMin = "0.05 0.4", AnchorMax = "0.95 0.6" }
                                }, contentName);

                                CuiHelper.AddUi(player, elements);
                                return;
                        }

                        const int itemsPerPage = 3;
                        int total = items.Count;
                        int totalPages = (total + itemsPerPage - 1) / itemsPerPage;
                        if (totalPages < 1) totalPages = 1;

                        int page = 0;
                        _protoUiPageByPlayer.TryGetValue(player.userID, out page);
                        if (page < 0) page = 0;
                        if (page > totalPages - 1) page = totalPages - 1;
                        _protoUiPageByPlayer[player.userID] = page;

                        int startIndex = page * itemsPerPage;
                        int endIndex = Mathf.Min(startIndex + itemsPerPage, total);

                        float startY = 0.82f;
                        float rowHeight = 0.22f;
                        float spacing = 0.02f;

                        int rowIndex = 0;
                        for (int i = startIndex; i < endIndex; i++, rowIndex++)
                        {
                                var it = items[i];
                                float yMax = startY - rowIndex * (rowHeight + spacing);
                                float yMin = yMax - rowHeight;

                                var outerPanel = new CuiPanel
                                {
                                        Image = { Color = "0 0.75 1 0.25" },
                                        RectTransform = { AnchorMin = $"0.03 {yMin}", AnchorMax = $"0.97 {yMax}" }
                                };
                                var outerName = elements.Add(outerPanel, contentName);

                                var rowPanel = new CuiPanel
                                {
                                        Image = { Color = "0 0 0 0.7" },
                                        RectTransform = { AnchorMin = "0.01 0.14", AnchorMax = "0.99 0.86" }
                                };
                                var rowName = elements.Add(rowPanel, outerName);

                                var tier = GetPrototypeC4TierFromItem(it);

                                if (string.IsNullOrEmpty(tier))
                                {
                                        _config.PrototypeC4.PrototypeC4Tiers.TryGetValue("proto_c4_0", out var p0);
                                        var p0Dmg = p0 != null ? p0.ExplosionDamage : 0f;

                                        int units = (_config.PrototypeC4.AllowStackPrototypeC4Upgrades && it.amount > 1) ? it.amount : 1;
                                        var costText = FormatCosts(_config.PrototypeC4.ConvertC4ToProto0Costs, units);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = $"Normal C4 (x{it.amount})", FontSize = 13, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                                                RectTransform = { AnchorMin = "0.02 0.62", AnchorMax = "0.72 0.92" }
                                        }, rowName);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = $"Convert -> PROTO_C4_0   |   DMG {p0Dmg:0}", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.9 0.9 0.9 1" },
                                                RectTransform = { AnchorMin = "0.02 0.18", AnchorMax = "0.72 0.40" }
                                        }, rowName);

                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = costText, FontSize = 10, Align = TextAnchor.LowerRight, Color = "0.85 0.85 0.85 1" },
                                                RectTransform = { AnchorMin = "0.60 0.02", AnchorMax = "0.98 0.18" }
                                        }, rowName);

                                        if (_config.PrototypeC4.AllowConvertNormalC4ToPrototype)
                                        {
                                                elements.Add(new CuiButton
                                                {
                                                        Button = { Command = $"c4rt.convert.c4 {it.uid.Value}", Color = "0.6 0.45 0.1 0.9" },
                                                        Text = { Text = "Convert", FontSize = 12, Align = TextAnchor.MiddleCenter },
                                                        RectTransform = { AnchorMin = "0.74 0.22", AnchorMax = "0.98 0.78" }
                                                }, rowName);
                                        }

                                        continue;
                                }

                                _config.PrototypeC4.PrototypeC4Tiers.TryGetValue(tier, out var cfg);
                                string lore = cfg?.Info ?? string.Empty;

                                string nextTier = null;
                                PrototypeC4UpgradeEntry upCfg;
                                if (_config.PrototypeC4.PrototypeC4UpgradeConfig.TryGetValue(tier, out upCfg) && upCfg != null)
                                        nextTier = upCfg.ToTier;

                                float curDmg = cfg != null ? cfg.ExplosionDamage : 0f;
                                float nextDmg = 0f;

                                string costText2 = "Max tier";
                                if (!string.IsNullOrEmpty(nextTier) && _config.PrototypeC4.PrototypeC4Tiers.TryGetValue(nextTier, out var ncfg) && ncfg != null)
                                {
                                        nextDmg = ncfg.ExplosionDamage;
                                        costText2 = FormatCosts(upCfg?.Costs, _config.PrototypeC4.AllowStackPrototypeC4Upgrades ? it.amount : 1);
                                }

                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = $"{tier.ToUpper()} (x{it.amount})", FontSize = 13, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                                        RectTransform = { AnchorMin = "0.02 0.62", AnchorMax = "0.72 0.92" }
                                }, rowName);

                                if (!string.IsNullOrEmpty(lore))
                                {
                                        elements.Add(new CuiLabel
                                        {
                                                Text = { Text = lore, FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.85 0.9 1 0.85" },
                                                RectTransform = { AnchorMin = "0.02 0.40", AnchorMax = "0.72 0.62" }
                                        }, rowName);
                                }

                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = $"Current: {curDmg:0} dmg    |    Next: {nextDmg:0} dmg", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.9 0.9 0.9 1" },
                                        RectTransform = { AnchorMin = "0.02 0.18", AnchorMax = "0.72 0.40" }
                                }, rowName);

                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = costText2, FontSize = 10, Align = TextAnchor.LowerRight, Color = "0.85 0.85 0.85 1" },
                                        RectTransform = { AnchorMin = "0.60 0.02", AnchorMax = "0.98 0.18" }
                                }, rowName);

                                if (!string.IsNullOrEmpty(nextTier) && _config.PrototypeC4.EnablePrototypeC4)
                                {
                                        elements.Add(new CuiButton
                                        {
                                                Button = { Command = $"c4rt.protoc4.upgrade {it.uid.Value}", Color = "0.2 0.55 0.2 0.9" },
                                                Text = { Text = "Upgrade", FontSize = 12, Align = TextAnchor.MiddleCenter },
                                                RectTransform = { AnchorMin = "0.74 0.22", AnchorMax = "0.98 0.78" }
                                        }, rowName);
                                }
                        }

                        if (totalPages > 1)
                        {
                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.page prev", Color = "0.15 0.4 0.6 0.9" },
                                        Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter },
                                        RectTransform = { AnchorMin = "0.30 0.01", AnchorMax = "0.36 0.09" }
                                }, contentName);

                                elements.Add(new CuiLabel
                                {
                                        Text = { Text = $"Page {page + 1} / {totalPages}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.9 1 1" },
                                        RectTransform = { AnchorMin = "0.36 0.01", AnchorMax = "0.64 0.09" }
                                }, contentName);

                                elements.Add(new CuiButton
                                {
                                        Button = { Command = "c4rt.proto.page next", Color = "0.15 0.4 0.6 0.9" },
                                        Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter },
                                        RectTransform = { AnchorMin = "0.64 0.01", AnchorMax = "0.70 0.09" }
                                }, contentName);
                        }

                        CuiHelper.AddUi(player, elements);
                }

        private string FormatCosts(List<UpgradeCost> costs, int multiplier)
        {
            if (costs == null || costs.Count == 0) return "Cost: -";
            var parts = new List<string>();
            foreach (var c in costs)
            {
                if (c == null || string.IsNullOrEmpty(c.ShortName) || c.Amount <= 0) continue;
                parts.Add($"{c.Amount * multiplier}x {c.ShortName}");
            }
            return "Cost: " + string.Join(", ", parts);
        }

        private BasePlayer FindPlayerByArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            var p = BasePlayer.Find(arg) ?? BasePlayer.FindSleeping(arg);
            if (p != null) return p;
            if (ulong.TryParse(arg, out var uid))
                return BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
            return null;
        }

        private void GivePrototypeLauncherTier(BasePlayer requester, BasePlayer target, string tierId)
        {
            if (target == null) return;
            if (_config == null || !_config.PrototypeLauncher.EnablePrototypeLauncher) return;
            if (string.IsNullOrEmpty(tierId)) tierId = "proto0";
            tierId = tierId.ToLower();

            if (_config.PrototypeLauncher.PrototypeLauncherTiers == null || !_config.PrototypeLauncher.PrototypeLauncherTiers.TryGetValue(tierId, out var cfg) || cfg == null)
            {
                if (requester != null) SendReply(requester, "<color=red>Invalid prototype tier!</color>");
                return;
            }

            var item = ItemManager.CreateByName("rocket.launcher", 1);
            if (item == null) return;

            _prototypeLauncherTierByItem[item.uid.Value] = tierId;
            EnsurePrototypeLauncherName(item, tierId);
            if (cfg.SkinId != 0) item.skin = cfg.SkinId;

            if (item.hasCondition)
            {
                item.maxCondition = Mathf.Max(item.maxCondition, 100f);
                item.condition = item.maxCondition;
            }

            item.MarkDirty();
            target.inventory.GiveItem(item);

            NextTick(() =>
            {
                if (item != null && item.GetRootContainer() != null)
                {
                    EnsurePrototypeLauncherName(item, tierId);
                    if (cfg.SkinId != 0) item.skin = cfg.SkinId;
                    item.MarkDirty();
                    ApplyHeldEntitySkin(item);
                    ApplyPrototypeLauncherMagazine(item, tierId);
                }
            });
        }

        private string MapLauncherAliasToRocketTier(string rawArg)
        {
            if (string.IsNullOrEmpty(rawArg)) return null;
            var s = rawArg.ToLower();
            if (s.Contains("t1")) return "rocket_tier1";
            if (s.Contains("t2")) return "rocket_tier2";
            if (s.Contains("t3")) return "rocket_tier3";
            return null;
        }
}
}  