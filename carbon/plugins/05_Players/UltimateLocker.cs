/*
 * Copyright © 2025 Scalbox - All rights reserved.
 * Developer: Scalbox - Contact: https://discord.gg/yryp3MjkF8
 *
 * DIGITAL CONTENT COPYRIGHT LICENSE
 *
 * INTELLECTUAL PROPERTY:
 * This digital content is protected by copyright under international and local laws.
 * All rights are reserved and any unauthorized use is prohibited.
 * The content purchased may not be reproduced, distributed, transmitted, or used in any form or by any means
 * without the written permission of the copyright holder, except as required by law.
 *
 * TERMS OF USE:
 * 1. Personal Use: Your purchase of this digital content grants you a non-exclusive, non-transferable, revocable license for personal use. The content may not be shared or distributed to third parties.
 * 2. Limitations: You may not modify, alter, adapt, create derivative works from, sell, or license this content without the written permission of the copyright holder.
 * 3. Backup: You may make one backup copy of the content for your personal use, provided that it is not distributed.
 *
 * WARRANTIES AND RESPONSIBILITIES:
 * The copyright holder makes no warranties regarding the accuracy, completeness or suitability of the content for a particular purpose.
 * In no event shall the copyright holder be liable for any damages arising from the use or inability to use the purchased digital content.
 *
 * PERMISSION REQUESTS:
 * For requests for permission to use or other information, please contact:
 *
 * Scalbox
 * info@scalbox.com
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Facepunch;
using Facepunch.Extend;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;
using static UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

//==================================================
//CHANGELOG
//==================================================

//1.0.6 => 05/11/2024
// - Fixed: Duplicated message in chat with auto lock pin code

//1.0.7 => 08/11/2024
// - Fixed: Double printing of pin messages in chat

//1.0.8 => 10/11/2024
// - Added: Auto closing system, Door Closer
// - Fixed: Default player setting data

//1.0.9 => 11/11/2024
// - Added: AutoDoors plugin integration: Prevents automatic door closing if a Door Closer is present
// - Fixed: Code Lock unlock fix

//1.1.0 => 14/11/2024
// - Fixed: Vehicle auto lock

//1.1.1 => 22/11/2024
// - Fixed: Invalid code with leading 0s
// - Fixed: Modular car guest code lock

//1.1.2 => 23/11/2024
// - Added: Support for Car Chassis

//1.1.3 => 24/11/2024
// - Fixed: Key Lock Clan/Team sharing

//1.1.4 => 30/11/2024
// - Added: New command /loker and /closer to add lock and door closer manually

//1.1.5 => 15/12/2024
// - Updated: Configuration file for default player settings
// - Added: Added command to change the closing time of the individual Door Closer you are looking at
// - Improved: Code refactoring
// - Fixed: Lock position for players who have the [ultimatelocker.autolock.nolockrequired] role

//1.1.6 => 15/12/2024
// - Fixed: bug fix

//1.1.7 => 16/12/2024
// - Fixed: Shared lock for offline or death players

//1.1.8 => 17/12/2024
// - Added: Message when placing a door closer via the command
// - Fixed: Loss of setted custom time for single door closer after server restart
// - Fixed: Issue where door closers were always placed if added via command, even if a door closer was already present

//1.1.9 => 22/12/2024
// - Fixed: Vanish (CanUseLockedEntity) hook conflict
// - Update: disable lock/unlock effect if player is invisible or flying

//1.2.0 => 30/01/2025
// - Added: Lock and auto closer support for windows: Wood Shutters
// - Improved:  Door closer positioning
// - Added:  Automatic lock closing when dismount of the vehicle

//1.2.1 => 07/02/2025
// - Fixed: Horse lock after Rust update (06/02/2025)

//1.2.2 => 09/02/2025
// - Fixed: Horse prefab name

//1.2.3 => 10/02/2025
// - Added: Door Closer for Medieval door
// - Added: Code Lock support for Medieval entities: Mounted Ballista, Battering Ram, Catapult, Siege Tower, Ballista
// - Added: Medieval entities block usage: opening/closing doors, reloading/firing ammo, mounting, driving, pushing, pulling, etc...

//1.2.4 => 13/02/2025
// - Added: Codelock support for Medieval Large Wood Box

//1.2.5 => 27/02/2025
// - Fix: Integration with plugins [Backpack] and [Bank]

//1.2.6 => 03/03/2025
// - Added: Player Auto Closing default settings
// - Added: New configuration option to resync all player settings to the default settings configured
// - Added: New configuration option to Force share locks with clan/team members.

//1.2.7 => 08/03/2025
// - Added: New lockable entities [Triangle Planter Box, Triangle Rail Road Planter, Single Plant Pot, Beehive, Chicken Coop, Cooking Workbench, Engineering Workbench, Hopper]

//1.2.8 => 12/03/2025
// - Added: New translations [es, es-ES, fr, de, nl, tr, ru, uk, zh-CN]

//1.2.9 => 19/03/2025
// - Fix: Russian translation
// - Added: Added property to enable only use of key locks

//1.3.0 => 22/03/2025
// - Added: New translation [zh-TW] Traditional Chinese

//1.3.1 => 16/04/2025
// - Added: Configuration to disable door closer removal
// - Fixed: bug fix

//1.3.2 => 01/05/2025
// - Fixed: Rust update (01/05/2025)

//1.3.3 => 02/05/2025
// - Added: Codelock support for [Bamboo Barrel, Wicker Barrel]

//1.3.4 => 06/05/2025
// - Added: Show Box and Storage Container lock status in the UI

//1.3.5 => 13/05/2025
// - Added: New permission to bypass some controls during the deployment of the lock in vehicles

//1.3.6 => 27/05/2025
// - Fixed: Fixed issue where CodeLocks were shared even in "Use key lock only" mode

//1.3.7 => 04/07/2025
// - Added: Codelock support for Abyss Horizontal Storage Tank, Abyss Vertical Storage Tank

//1.3.8 => 07/07/2025
// - Fixed: RemoverTool bug fix

//1.3.9 => 08/07/2025
// - Improved: Delay while positioning the lock/Codelock

//1.4.0 => 08/07/2025
// - Improved: Delay while positioning the lock/Codelock (Second improvement)

//1.4.1 => 08/07/2025
// - Improved: Performance improved
// - Improved: Code refactoring

//1.4.2 => 08/07/2025
// - Fixed: Bug fix

//1.4.3 => 08/07/2025
// - Fixed: NRE Bug fix

//1.4.4 => 22/07/2025
// - Fixed: NRE Bug fix

//1.4.5 => 02/08/2025
// - Updated: Compatibility with the Remover Tool plugin, for Door Closer pickup
// - Added: Configuration properties to change vehicle ownership on lock deploy

//1.4.6 => 03/08/2025
// - Added: Configuration property [Player Can Remove Door Closer?]
// - Added: Configuration property [Pickup Door Closer Refund]

//1.4.7 => 09/08/2025
// - Fixed: Codelock authentication

//1.4.8 => 13/08/2025
// - Added: Codelock support for Black Box

//1.4.9 => 13/08/2025
// - Added: Support for new CodeLock: Flight Control Codelock

//1.5.0 => 18/08/2025
// - Fixed: Support for new CodeLock: Flight Control Codelock
// - Fixed: CodeLock disappears/changes position after changing the skin with the spray can

//1.5.1 => 04/09/2025
// - Fixed: Forced Rust update (04/09/2025)

//1.5.2 => 05/09/2025
// - Added: Codelock support for [Wall Cabinet, Clothing Mannequin]

//1.5.3 => 14/09/2025
// - Fixed: Codelock shared with clan/team members

//1.5.4 => 16/09/2025
// - Added: Option to automatically use the Flight Control Codelock skin.

//1.5.5 => 16/09/2025
// - Fixed: Fixed issue: When enabling automatic skin change [Flight Control Codelock], it was possible to place multiple code locks on the same entity.

//1.5.6 => 06/10/2025
// - Fixed: The GUI now scales correctly to screen resolution.
// - Added: Configuration property [Allow Pushing vehicles blocked by the Code Lock if present in your Building Privilege. (Default: TRUE)]

//1.5.7 => 24/10/2025
// - Added: Codelock support for [Krieg Storage Crates, Krieg Storage Barrel]

//1.5.8 => 30/11/2025
// - Added: Skin blacklist configuration for Locks and Door Closers
// - Added: Disable Lock refund if player has permission [ultimatelocker.autolock.nolockrequired]
// - Added: Disable Door Closer refund if player has permission [ultimatelocker.autoclosing.nodoorcloserrequired]
// - Fixed: Key lock sound effect
// - Improved: Code refactoring

//1.5.9 => 04/12/2025
// - Improved: Code refactoring
// - Improved: Performance

//1.6.0 => 02/01/2026
// - Added: Configuration to enable/disable categories for auto lock
// - Fixed: Krieg Storage code lock rotation

//1.6.1 => 28/02/2026
// - Added: Support for Naval Deep Sea entities
// - Added: Support for Drawing / Signs entities
// - Added: Support for locks with scaled size
// - Fixed: Some vehicle commands not working
// - Fixed: Minor bug fixes and improvements

//1.6.2 => 28/02/2026
// - Fixed: Naval Deep Sea, the code lock was placed 2 times

//1.6.3 => 03/03/2026
// - Fixed: Rust update (05/03/2026)

//1.6.4 => 07/03/2026
// - Added: Lock Support for: Storage Box Pack DLC (https://rust.facepunch.com/news/shipshape#StorageBoxPackDLC)

//1.6.5 => 03/04/2026
// - Added: Support for Top tier Ladder hatch

//1.6.6 => 10/04/2026
// - Improved: Improve the graphical experience and UI usage
// - Improved: Code refactoring
// - Improved: Performance

//2.0.0 => 22/04/2026
// - Updated: Removed the use of the Door Closer item.
// - Updated: Automatically closing doors will no longer require the Door Closer item.

//2.0.1 => 27/04/2026
// - Improved: Auto closing system
// - Added: Proximity System
// - Added: Quick UI
// - Fixed: Auto closing bug fix

//2.0.2 => 05/05/2026
// - Improved: Proximity System

//2.0.3 => 10/05/2026
// - Improved: Compatibility with the AutoClose plugin

//2.0.4 => 12/05/2026
// - Updated: Automatic lock commands: ["al", "autolock", "codelock"]
// - Added: Auto Closing Commands: ["ac", "autoclose", "autocloser"]
// - Added: Toggle Auto Lock Command: ["ual"]
// - Added: Toggle Auto Closer Command: ["uad"]

//2.0.5 => 13/05/2026
// - Fixed: Auto closing bug fix

//2.0.6 => 03/06/2026
// - Fixed: Rust update (04/06/2026)

//2.0.7 => 06/06/2026
// - Added: Lock Support for: Industrial Decor Pack DLC (https://rust.facepunch.com/news/built-different#IndustrialDecorPack)

//2.0.8 => 11/06/2026
// - Fixed: Auto Closing support for: Industrial Garage Door

//2.0.9 => 02/07/2026
// - Fixed: LockSign patch to specify parameters and avoid AmbiguousMatchException after Rust update (02/07/2026)

//2.1.0 => 07/07/2026
// - Added: Support for new Clans hook signatures OnClanMemberGone(tag, leaving, members)
// - Fixed: FormatException in OnClanMemberGone timer when Clans passes the clan tag as first parameter

//2.1.1 => 17/07/2026
// - Fixed: Lock lost when SkinBox or Skinner change the skin of entities that Rust natively destroys and recreates (e.g. Furnace -> Legacy Furnace)
// - Improved: Correctly repositioning the lock after changing a skin
// - Improved: Code refactoring and adding new method documentation

//2.1.2 => 23/07/2026
// - Fixed: Bug fix related to skin changes

//2.1.3 => 12/08/2026
// - Updated: Lock, door and vehicle flags are now set through Rust's new StartSetFlags API: Facepunch removes SetFlag with the September wipe (3rd Sep 2026), and without this change the plugin would stop compiling on the server

//2.1.4 => 20/08/2026
// - Added: Can bypass the lock if the player is in Vanish mode

//2.1.5 => 01/09/2026
// - Fixed: Rust update (03/09/2026)

namespace Oxide.Plugins
{
    [Info("UltimateLocker", "Scalbox", "2.1.5")]
    [Description("Place Code Locks everywhere & Auto Lock / Auto Closing. Vehicles, Furnaces, Weapon Racks, Turrets, Deployable Items and much more.")]
    public class UltimateLocker : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin? ImageLibrary;
        [PluginReference] private Plugin? Clans;
        [PluginReference] private Plugin? Vanish;
        [PluginReference] private Plugin? RemoverTool;
        [PluginReference] private Plugin? Skinner;
        [PluginReference] private Plugin? GunShip;
        private static UltimateLocker? self;

        private enum SkinBlacklistType
        {
            Lock,
            DoorCloser
        }

        private enum EntityType
        {
            Vehicle,
            VehicleBike,
            DeployableNavalDeepSea,
            VehicleMedieval,
            DeployableConstruction,
            DeployableMedieval,
            DeployableStorage,
            DeployableWorkbench,
            DeployableElectrical,
            DeployableElectricalButton,
            DeployableElectricSwitch,
            DeployableElectricGenerator,
            DeployableLiquidContainer,
            DeployableTurret,
            DeployableTraps,
            DeployableModularCarLift,
            DeployableSnowFogMachine,
            DeployableElevator,
            DeployableElectricalNeonSign,
            DeployableSignDrawing,
            DeployableMiningQuarry,
            DeployableMiningQuarryStatic,
            DeployableMiningPumpJack,
            DeployableMiningPumpJackStatic,
            DeployableWeaponRack,
            DeployableStash,
            DeployableItems,
            DeployableResources,
            DeployableIndustrial,
            DeployableMisc,
            DeployableFun,
        }

        private enum LockCategory
        {
            None,
            Door,
            Window,
            Box,
            StorageContainer,
            Locker,
            Cupboard,
            Vehicle,
            NavalDeepSea,
            Medieval,
            Furnace,
            VendingMachine,
            Composter,
            MixingTable,
            Planter,
            AutoTurret,
            SamSite,
            Trap,
            WeaponRack,
            Stash,
            NeonSign,
            SignDrawing,
            OtherLockableEntities,
            OtherCustomEntities
        }

        private enum AutoClosingType
        {
            None,
            Door,
            DoubleDoor,
            Window,
            Garage,
            LadderHatch,
            ExternalGate,
            FenceGate,
            LegacyWoodShelterDoor
        }

        private readonly List<LockableEntityConfig> _allLockableEntityConfigs = new();
        private readonly List<LockableEntityConfig> _enabledLockableEntityConfigs = new();
        private readonly HashSet<string> _allowedLockableEntityPrefabs = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _entityRequiredPermission = new();

        // TODO trovare una soluzione migliore senza nested Dictionary
        // OwnerID → (NetId.Value → (Timer, PrefabID))
        private readonly Dictionary<ulong, Dictionary<ulong, (Timer? Timer, uint PrefabID)>> _doorCloserTimers = new();

        //PrefabName
        private readonly HashSet<string> _vehiclesCockpitPrefabName = new(StringComparer.OrdinalIgnoreCase)
        {
            "assets/content/vehicles/modularcar/module_entities/1module_cockpit.prefab",
            "assets/content/vehicles/modularcar/module_entities/1module_cockpit_armored.prefab",
            "assets/content/vehicles/modularcar/module_entities/1module_cockpit_with_engine.prefab"
        };

        //PrefabName
        private readonly HashSet<string> _boxesPrefabName = new(StringComparer.OrdinalIgnoreCase)
        {
            "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
            "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/medieval_large_wood_box/medieval.box.wooden.large.prefab", //Medieval Large Wood Box
            "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_a.prefab",
            "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab",
            "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab",
            "assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab",
            "assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab",
            "assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_vertical/bamboo_barrel.prefab",
            "assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab",
            "assets/prefabs/deployable/woodenbox/skins/pilot_hazmat_wooden_box/pilot_hazmat_woodbox_deployed.prefab",
            "assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_horizontal/krieg_storage_horizontal.prefab", // Krieg Storage Crates
            "assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_vertical/krieg_storage_vertical.prefab", // Krieg Storage Barrel
            "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab",
            "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab",
            "assets/prefabs/deployable/wall cabinet/electric.wallcabinet.deployed.prefab",
            "assets/prefabs/deployable/large wood storage/skins/industrial_dlc/industrial_storage_horizontal/industrial_storage_horizontal.prefab",
            "assets/prefabs/deployable/large wood storage/skins/industrial_dlc/industrial_storage_vertical/industrial_storage_vertical.prefab",

            // #################### Storage Box Pack DLC ########################################
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_ammo/component.box.ammo.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_armor/component.box.armor.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_charcoal/component.box.charcoal.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_clothing/component.box.clothing.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_comps/component.box.comps.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_explosives/component.box.explosives.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_food/component.box.food.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_guns/component.box.guns.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_meds/component.box.meds.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_metal/component.box.metal.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_ore/component.box.ore.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_scrap/component.box.scrap.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_stone/component.box.stone.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_sulfur/component.box.sulfur.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_tools/component.box.tools.large.prefab",
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc/box_wood/component.box.wood.large.prefab",
            // ##################################################################################
        };

        //PrefabName
        private readonly HashSet<string> _windowsPrefabName = new(StringComparer.OrdinalIgnoreCase)
        {
            "assets/prefabs/building/wall.window.shutter/shutter.wood.a.prefab"
        };

        //PrefabName
        private readonly HashSet<string> _medievalPrefabName = new(StringComparer.OrdinalIgnoreCase)
        {
            "assets/content/vehicles/siegeweapons/ballista/ballista.entity.prefab", //Mounted Ballista
            "assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab", //Battering Ram
            "assets/content/vehicles/siegeweapons/catapult/catapult.entity.prefab", //Catapult
            "assets/content/vehicles/siegeweapons/siegetower/siegetower.entity.prefab", //Siege Tower
            "assets/content/vehicles/siegeweapons/ballista/ballistagun.static.entity.prefab" //Ballista
        };

        private readonly Dictionary<AutoClosingType, HashSet<string>> _autoClosingEntities = new()
        {
            {
                AutoClosingType.Door, new HashSet<string>
                {
                    "assets/prefabs/building/door.hinged/door.hinged.wood.prefab",
                    "assets/prefabs/building/door.hinged/door.hinged.metal.prefab",
                    "assets/prefabs/building/door.hinged/door.hinged.toptier.prefab",
                    "assets/prefabs/misc/permstore/factorydoor/door.hinged.industrial.d.prefab",
                    "assets/prefabs/misc/medieval door skin/medieval.door.hinged.metal.prefab"
                }
            },
            {
                AutoClosingType.DoubleDoor, new HashSet<string>
                {
                    "assets/prefabs/building/door.double.hinged/door.double.hinged.wood.prefab",
                    "assets/prefabs/building/door.double.hinged/door.double.hinged.metal.prefab",
                    "assets/prefabs/building/door.double.hinged/door.double.hinged.toptier.prefab",
                    "assets/prefabs/building/wall.frame.cell/wall.frame.cell.gate.prefab",
                    "assets/prefabs/misc/medieval door skin/medieval.door.double.hinged.metal.prefab"
                }
            },
            {
                AutoClosingType.Window, new HashSet<string> { "assets/prefabs/building/wall.window.shutter/shutter.wood.a.prefab" }
            },
            {
                AutoClosingType.Garage, new HashSet<string>
                {
                    "assets/prefabs/building/wall.frame.garagedoor/wall.frame.garagedoor.prefab",
                    "assets/prefabs/building/wall.frame.garagedoor/skins/industrial_garage_door/industrial.garage.door.deployable.prefab" // Industrial Garage Door
                }
            },
            {
                AutoClosingType.LadderHatch, new HashSet<string>
                {
                    "assets/prefabs/building/floor.ladder.hatch/floor.ladder.hatch.prefab",
                    "assets/prefabs/building/floor.triangle.ladder.hatch/floor.triangle.ladder.hatch.prefab",
                    "assets/prefabs/building/floor.ladder.hatch.toptier/floor.ladder.hatch.toptier.prefab",
                    "assets/prefabs/building/floor.ladder.hatch.toptier/floor.triangle.ladder.hatch.toptier.prefab"
                }
            },
            {
                AutoClosingType.ExternalGate, new HashSet<string>
                {
                    "assets/prefabs/building/gates.external.high/gates.external.high.wood/gates.external.high.wood.prefab",
                    "assets/prefabs/building/gates.external.high.legacy/gates.external.high.legacy.prefab",
                    "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab",
                    "assets/prefabs/building/gates.external.high.adobe/gates.external.high.adobe.prefab"
                }
            },
            {
                AutoClosingType.FenceGate, new HashSet<string> { "assets/prefabs/building/wall.frame.fence/wall.frame.fence.gate.prefab" }
            },
            {
                AutoClosingType.LegacyWoodShelterDoor, new HashSet<string>
                {
                    "assets/prefabs/building/legacy.shelter.wood/legacy.shelter.wood.deployed.prefab",
                    "assets/prefabs/building/legacy.shelter.wood/legacy.shelter.wood.door.prefab"
                }
            }
        };

        // GunShip vehicles component name
        private readonly HashSet<string> _gunShipVehiclesComponentNameSet = new(StringComparer.OrdinalIgnoreCase)
        {
            "Oxide.Plugins.GunShip/Apache",
            "Oxide.Plugins.GunShip/ArmedSedan",
            "Oxide.Plugins.GunShip/AttackRhib",
            "Oxide.Plugins.GunShip/CargoTruck",
            "Oxide.Plugins.GunShip/Cougar",
            "Oxide.Plugins.GunShip/Enforcer",
            "Oxide.Plugins.GunShip/Guardian",
            "Oxide.Plugins.GunShip/HeavyTechnical",
            "Oxide.Plugins.GunShip/MiniFighter",
            "Oxide.Plugins.GunShip/Reaper",
            "Oxide.Plugins.GunShip/Stallion",
            "Oxide.Plugins.GunShip/Stinger",
            "Oxide.Plugins.GunShip/TCOPCobra",
            "Oxide.Plugins.GunShip/TCOPHuey",
            "Oxide.Plugins.GunShip/Technical",
            "Oxide.Plugins.GunShip/Viper"
        };

        private const string UITransparentColor = "0 0 0 0";
        private const string UIPanelColorMain = "0.133 0.133 0.133 1";
        private const string UIPanelColor1 = "0.212 0.196 0.184";
        private const string UIGreenColor = "0.235 0.294 0.149 1";
        private const string UIOrangeColor = "0.98 0.612 0 0.5";
        private const string UIOrangeLightColor = "1 0.647 0 1";

        private const string UILocker = "UI.UltimateLocker.Locker";
        private const string UIAutoLock = "UI.UltimateLocker.AutoLock";
        private const string UIAutoLockContainer = "UI.UltimateLocker.AutoLock.ContentPanel";
        private const string UIAutoLockPanelRow1 = "UI.UltimateLocker.AutoLock.PanelRow1";
        private const string UIAutoLockScrollView = "UI.UltimateLocker.AutoLock.ScrollView";
        private const string UIAutoLockScrollContent = "UI.UltimateLocker.AutoLock.ScrollContent";

        private const string UIAutoClosing = "UI.UltimateLocker.AutoClosing";

        private const string UIProximitySystemIcon = "UI.UltimateLocker.UIProximitySystemIcon";
        private const string UIProximitySystemIconContentPanel = "UI.UltimateLocker.UIProximitySystemIcon.Content.Panel";

        private const string UIProximitySystem = "UI.UltimateLocker.UIProximitySystem";
        private const string UIProximitySystemHeaderPanel = "UI.UltimateLocker.UIProximitySystem.Header.Panel";
        private const string UIProximitySystemRow1Panel = "UI.UltimateLocker.UIProximitySystem.PanelRow1.Panel";

        private const string PermissionUse = "ultimatelocker.use";
        private const string PermissionAdmin = "ultimatelocker.admin";
        private const string PermissionBypassForce = "ultimatelocker.bypass.force";
        private const string PermissionBypassDlcSkinRequired = "ultimatelocker.bypass.dlc_skin_required";
        private const string PermissionBypassLockDeployVehicleCheck = "ultimatelocker.bypass.lock_deploy_vehicle_check";
        private const string PermissionAutoLockEnabled = "ultimatelocker.autolock.enabled";
        private const string PermissionAutoLockNoLockRequired = "ultimatelocker.autolock.nolockrequired";
        private const string PermissionAutoClosingEnabled = "ultimatelocker.autoclosing.enabled";

        private const string DefaultTimeZone = "Europe/London";
        private const int KeyLockItemID = -850982208;
        private const int CodeLockItemID = 1159991980;

        private const int FlightControlCodeLockItemID = 1586884551;
        private const int FlightControlCodeLockItemSkinItemID = 1159991980;
        private const ulong FlightControlCodeLockSkinID = 10318;
        private const string FlightControlCodelockPrefab = "assets/prefabs/locks/keypad/skins/codelock_a_pilot/lock.code.a.pilot.prefab";
        private const string FlightControlCodelockShortPrefabName = "lock.code.a.pilot";
        private const uint FlightControlCodelockPrefabID = 319071656;

        private const string CodeLockOnDeployImageName = "UltimateLocker::CodeLockOnDeploy";
        private const string KeyLockOnDeployImageName = "UltimateLocker::KeyLockOnDeploy";
        private const string AutoCloseProximitySystemImageName = "UltimateLocker::AutoCloseProximitySystem";
        private const string CodeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        private const uint CodeLockPrefabID = 3518824735;
        private const string KeyLockPrefab = "assets/prefabs/locks/keylock/lock.key.prefab";
        private const string LegacyShelterWoodDoorPrefabName = "assets/prefabs/building/legacy.shelter.wood/legacy.shelter.wood.door.prefab";
        private const string KeyLockShortPrefabName = "lock.key";
        private const string CodeLockShortPrefabName = "lock.code";
        private const string DefaultModularCarCodeLockCode = "0000";
        private const float DefaultScaleSize = 1.0f;
        private const int GarageOpenDuration = 1;

        #region Steam Workshop DLC/SKIN

        private const string PropertyWorkshopID = "workshopid";

        private bool _steamWorkshopInitialized;

        // Workshop Skin ID to item ID mapping
        private readonly Dictionary<ulong, int> _workshopIdToItemId = new();

        // Item ID to skin mapping
        private readonly Dictionary<int, ItemSkinDirectory.Skin> _itemIdToSkin = new();

        private static readonly HashSet<ulong> SkinIdWhitelisted = new();

        //SteamID : Dictionary of (SkinID, Owned)
        private static readonly Dictionary<ulong, Dictionary<ulong, bool>> PlayerDlcSkinOwnedCache = new();

        // SteaID: Delay Time - Qui viene salvato il nuovo delay time mentre lo si digita nella UI
        private static readonly Dictionary<ulong, int> PlayerSavedDalyTime = new();

        private static Dictionary<string, string>? _prefabToDisplayName;

        #endregion

        private const bool DefaultCodelockSkinFlightControlCodelockEnabled = false;

        private const string ModuleCarPartialShortPrefabName = "module_car_spawned.entity";
        private const string CarChassisModuleCarPartialPrefabName = "assets/content/vehicles/modularcar/car_chassis_";

        private const int CoroutineIteratorsPerFrame = 5;
        private const float CoroutineCodeLocksWait = 0.5F;

        // -------------------------------------------------------
        //  Door Proximity System

        private const string HookOnPlayerEnterDoorRadius = "OnPlayerEnterDoorRadius";
        private const string HookOnPlayerLeaveDoorRadius = "OnPlayerLeaveDoorRadius";

        /// <summary>
        /// Raggio in metri del raycast usato in <see cref="TryGetTargetProximityDoor"/>
        /// per rilevare la porta che il player sta guardando.
        /// </summary>
        private const float ProximityCheckRadius = 3.0f;

        /// <summary>
        /// Raggio in metri entro cui un player viene considerato vicino a una porta
        /// nel check di prossimità di <see cref="IsNearTrackedDoor"/>.
        /// Più piccolo di <see cref="ProximityCheckRadius"/> per ridurre i falsi positivi
        /// quando due porte sono adiacenti.
        /// </summary>
        private const float NearProximityCheckRadius = 2.5f;

        /// <summary>
        /// Intervallo in secondi tra un batch check e il successivo.
        /// <para>
        /// Il numero di player controllati al secondo si calcola come:<br/>
        /// <c>player/sec = (1 / ProximityCheckInterval) * ProximityPlayersPerTick</c><br/>
        /// Esempio con 0.05: (1 / 0.05) * 10 = 20 * 10 = 200 player/sec<br/>
        /// Esempio con 0.20: (1 / 0.20) * 10 =  5 * 10 =  50 player/sec<br/>
        /// Esempio con 0.50: (1 / 0.50) * 10 =  2 * 10 =  20 player/sec
        /// </para>
        /// <para>
        /// L'intervallo di rivalutazione per singolo player (ogni quanti secondi lo stesso player viene rivalutato)
        /// si calcola come:<br/>
        /// <c>sec/player = totalePlayers / player/sec</c><br/>
        /// Nota: non indica il tempo di esecuzione del check, ma il tempo che intercorre tra una rivalutazione
        /// e la successiva dello stesso player.<br/>
        /// Esempio con 0.05 e 100 player: 100 / 200 = 0.5s per player<br/>
        /// Esempio con 0.05 e 200 player: 200 / 200 = 1.0s per player<br/>
        /// Esempio con 0.20 e 100 player: 100 /  50 = 2.0s per player<br/>
        /// Esempio con 0.20 e 200 player: 200 /  50 = 4.0s per player<br/>
        /// Esempio con 0.50 e 100 player: 100 /  20 = 5.0s per player<br/>
        /// Esempio con 0.50 e 200 player: 200 /  20 = 10.0s per player
        /// </para>
        /// <para>
        /// Esempi con <see cref="ProximityPlayersPerTick"/> = 10 e intervallo 0.05:
        /// <list type="table">
        ///   <listheader>
        ///     <term>Player online</term>
        ///     <description>Intervallo rivalutazione per player</description>
        ///   </listheader>
        ///   <item><term>50</term><description>0.25s</description></item>
        ///   <item><term>100</term><description>0.5s</description></item>
        ///   <item><term>200</term><description>1.0s</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Esempi con <see cref="ProximityPlayersPerTick"/> = 10 e intervallo 0.20:
        /// <list type="table">
        ///   <listheader>
        ///     <term>Player online</term>
        ///     <description>Intervallo rivalutazione per player</description>
        ///   </listheader>
        ///   <item><term>50</term><description>1.0s</description></item>
        ///   <item><term>100</term><description>2.0s</description></item>
        ///   <item><term>200</term><description>4.0s</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Esempi con <see cref="ProximityPlayersPerTick"/> = 10 e intervallo 0.50:
        /// <list type="table">
        ///   <listheader>
        ///     <term>Player online</term>
        ///     <description>Intervallo rivalutazione per player</description>
        ///   </listheader>
        ///   <item><term>50</term><description>2.5s</description></item>
        ///   <item><term>100</term><description>5.0s</description></item>
        ///   <item><term>200</term><description>10.0s</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Effetto della modifica di questo valore:<br/>
        /// - Diminuire (es. 0.20 → 0.05): il timer scatta più spesso → i player vengono rivalutati più frequentemente
        ///   → maggiore reattività, maggiore carico sul server.<br/>
        /// - Aumentare (es. 0.05 → 0.20): il timer scatta meno spesso → i player vengono rivalutati meno frequentemente
        ///   → minore reattività, minore carico sul server.
        /// </para>
        /// <para>
        /// Confronto con il tick rate nativo di Rust (<c>server.tickrate 30</c>):<br/>
        /// Rust server tick: 30 tick/sec (30 tick al secondo) → 1 tick ogni  ~33ms<br/>
        /// Questo timer (0.05):  20 tick/sec (20 tick al secondo) → 1 tick ogni   50ms<br/>
        /// Questo timer (0.20):   5 tick/sec ( 5 tick al secondo) → 1 tick ogni  200ms<br/>
        /// Questo timer (0.50):   2 tick/sec ( 2 tick al secondo) → 1 tick ogni  500ms<br/>
        /// Il timer è volutamente sotto il tick rate nativo per non creare pressione sul server.
        /// </para>
        /// </summary>
        private const float ProximityCheckInterval = 0.2f;

        /// <summary>Tempo di inattività in secondi oltre il quale un player viene considerato AFK e saltato dal check di prossimità.</summary>
        private const float ProximityAfkThreshold = 300f;

        /// <summary>Numero massimo di player controllati per ogni batch tick.</summary>
        private const int ProximityPlayersPerTick = 10;

        /// <summary>Layer mask limitata a <c>Construction</c> per filtrare le entità in <see cref="Vis.Entities"/>.</summary>
        private static readonly int ProximityDoorLayer = LayerMask.GetMask("Construction");

        /// <summary>
        /// Insieme degli <c>userID</c> dei player attualmente nel raggio di una porta tracciata.
        /// Usato per rilevare i cambi di stato (entrata/uscita) ed evitare hook ridondanti.
        /// </summary>
        private readonly HashSet<ulong> _nearTrackedDoor = new();

        /// <summary>
        /// <c>userID</c> dei player per cui è in corso un <see cref="RestoreLockAfterReskin"/>: la nuova entità
        /// verrà ricreata da Rust nello stesso tick e farà scattare <see cref="OnEntitySpawned"/>, che altrimenti
        /// vincerebbe la corsa e piazzerebbe subito una serratura "di default" (sempre un CodeLock) prima che
        /// il fix riesca a ripristinare quella corretta. Finché il player è in questo set, <see cref="OnEntitySpawned"/>
        /// salta il proprio auto-deploy per lasciare campo libero al ripristino.
        /// </summary>
        private readonly HashSet<ulong> _pendingReskinLockRestore = new();

        /// <summary>
        /// Indice corrente nella lista <see cref="BasePlayer.activePlayerList"/> da cui parte il batch check.
        /// Avanza di <see cref="ProximityPlayersPerTick"/> ad ogni tick, scorrendo circolarmente tutti i player.
        /// </summary>
        private int _proximityCheckOffset = 0;

        /// <summary>Timer che esegue <see cref="DoorProximityCheckBatch"/> ogni <see cref="ProximityCheckInterval"/> secondi.</summary>
        private Timer _proximityCheckTimer;
        // -------------------------------------------------------

        private const string CodeLockDeployEffect = "assets/prefabs/locks/keypad/effects/lock-code-deploy.prefab";
        private const string CodeLockDeniedEffect = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
        private const string CodeLockLockEffect = "assets/prefabs/locks/keypad/effects/lock.code.lock.prefab";
        private const string CodeLockUnlockEffect = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";
        private const string CodeLockCodeUpdatedEffect = "assets/prefabs/locks/keypad/effects/lock.code.updated.prefab";
        private const string CodeLockShockEffect = "assets/prefabs/locks/keypad/effects/lock.code.shock.prefab";

        private const string CodeLockOnDeployImageUrl = "https://dl.scalbox.com/Rust/Plugins/UltimateLocker/CodeLock_OnDeploy.jpg";
        private const string KeyLockOnDeployImageUrl = "https://dl.scalbox.com/Rust/Plugins/UltimateLocker/KeyLock_OnDeploy.jpg";

        // private const string AutoCloseProximitySystemImageUrl = "https://dl.scalbox.com/Rust/Plugins/UltimateLocker/AutoClose_ProximitySystem.png";
        private const string AutoCloseProximitySystemImageUrl = "https://dl.scalbox.com/Rust/Plugins/UltimateLocker/AutoClose_ProximitySystem_1024x1024.png";

        private static readonly Regex AlphanumericRegex = new Regex("[^a-zA-Z0-9\\._-]", RegexOptions.Compiled);
        // private static readonly Regex AlphanumericRegex = new Regex("alphabet\\.", RegexOptions.Compiled);

        private readonly object _true = true;
        private readonly DateTime _epochDateTime = DateTime.Parse("1970-01-01");
        private bool _skinnerPatchApplied;


        private Harmony _harmony;
        private List<Coroutine> _coroutines = new();
        private Configuration _config;
        private Data _data;

        #endregion

        #region Hooks - Core

        private void Init()
        {
            if (_config.ChatCommand.IsNullOrEmpty())
            {
                Puts("Command missing in configuration");
                Interface.Oxide.UnloadPlugin(Name);
                return;
            }

            foreach (var command in _config.AutoLockConfiguration.ChatCommand)
            {
                AddCovalenceCommand(command, nameof(AutoLockCmdHandler));
            }

            foreach (var command in _config.AutoClosingConfiguration.ChatCommand)
            {
                AddCovalenceCommand(command, nameof(AutoClosingCmdHandler));
            }

            foreach (var command in _config.ChatCommand)
            {
                AddCovalenceCommand(command, nameof(CmdHandler));
            }

            AddCovalenceCommand(new[]
                {
                    _config.AutoLockConfiguration.AddLockerChatCommand, _config.AutoLockConfiguration.ToggleAutoLockChatCommand, _config.AutoClosingConfiguration.ToggleAutoCloserChatCommand,
                    _config.AutoClosingConfiguration.AddCloserChatCommand, _config.AutoClosingConfiguration.RemoveCloserChatCommand
                },
                nameof(LockerCloserCmdHandler));

            _allLockableEntityConfigs.AddRange(_config.LockConfiguration.Vehicles);
            _allLockableEntityConfigs.AddRange(_config.LockConfiguration.Deployables);
            _enabledLockableEntityConfigs.AddRange(_allLockableEntityConfigs.Where(v => v.EnableLock));

            foreach (var config in _enabledLockableEntityConfigs)
            {
                if (!string.IsNullOrEmpty(config.PrefabName))
                {
                    _allowedLockableEntityPrefabs.Add(config.PrefabName);
                }
            }

            self = this;
            _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);

            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionAdmin, this);
            permission.RegisterPermission(PermissionBypassForce, this);
            permission.RegisterPermission(PermissionBypassDlcSkinRequired, this);
            permission.RegisterPermission(PermissionBypassLockDeployVehicleCheck, this);
            permission.RegisterPermission(PermissionAutoLockEnabled, this);
            permission.RegisterPermission(PermissionAutoLockNoLockRequired, this);
            permission.RegisterPermission(PermissionAutoClosingEnabled, this);

            //Register all entity required permissions
            _entityRequiredPermission.UnionWith(EntityRequiredPermission());
            if (!_entityRequiredPermission.IsNullOrEmpty())
            {
                foreach (var perm in _entityRequiredPermission)
                {
                    permission.RegisterPermission(perm, this);
                }
            }
        }

        private void OnServerInitialized()
        {
            if (ImageLibrary == null)
            {
                PrintError("########## WARNING - [ImageLibrary] not found or unloaded! ##########");
                // Interface.Oxide.UnloadPlugin(Title);
                // return;
            }

            BuildPrefabToDisplayNameMap();

            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
            {
                Puts("Waiting for Steam Workshop to update item definitions....");
                Steamworks.SteamInventory.OnDefinitionsUpdated += ProcessSteamWorkshopDefinitions;
            }
            else ProcessSteamWorkshopDefinitions();

            if (_config.AutoLockConfiguration.ResyncPlayersDefaultSettings)
            {
                PrintWarning("Resyncing players default settings, STARTED...");

                if (!_data.AutoLockPlayerSettingsData.IsNullOrEmpty())
                {
                    foreach (var steamID in _data.AutoLockPlayerSettingsData.Keys.ToList())
                    {
                        var currentAutoLockPlayerSettings = _data.AutoLockPlayerSettingsData[steamID];

                        var defaultAutoLockPlayerSettingsData = GetDefaultAutoLockPlayerSettingsData(steamID);
                        defaultAutoLockPlayerSettingsData.CodeLock = currentAutoLockPlayerSettings.CodeLock;
                        defaultAutoLockPlayerSettingsData.GuestCodeLock = currentAutoLockPlayerSettings.GuestCodeLock;

                        _data.AutoLockPlayerSettingsData[steamID] = defaultAutoLockPlayerSettingsData;
                    }

                    SaveData();
                }

                if (!_data.AutoClosingPlayerSettingsData.IsNullOrEmpty())
                {
                    foreach (var autoClosingPlayerSettingsDataKey in _data.AutoClosingPlayerSettingsData.Keys.ToList())
                    {
                        _data.AutoClosingPlayerSettingsData[autoClosingPlayerSettingsDataKey] = GetDefaultAutoClosingPlayerSettingsData(autoClosingPlayerSettingsDataKey);
                    }

                    SaveData();
                }

                _config.AutoLockConfiguration.ResyncPlayersDefaultSettings = false;
                SaveConfig();

                PrintWarning("Resyncing players default settings, DONE!");
            }

            _harmony = new Harmony($"{Name}_Patch_{Version.Major}_{Version.Minor}_{Version.Patch}");

            ApplySkinnerHarmonyPatch();

            _harmony.Patch(
                original: AccessTools.Method(typeof(ModularCarCodeLock), nameof(ModularCarCodeLock.TryAddALock)),
                postfix: new HarmonyMethod(typeof(ModularCarCodeLock_Settings),
                    nameof(ModularCarCodeLock_Settings.Postfix_TryAddALock)));
            _harmony.Patch(
                original: AccessTools.Method(typeof(ModularCarCodeLock), nameof(ModularCarCodeLock.RemoveLock)),
                postfix: new HarmonyMethod(typeof(ModularCarCodeLock_Settings),
                    nameof(ModularCarCodeLock_Settings.Postfix_RemoveLock)));

            _harmony.Patch(original: AccessTools.Method(typeof(Workbench), nameof(Workbench.RPC_TechTreeUnlock)),
                prefix: new HarmonyMethod(typeof(Workbench_Settings),
                    nameof(Workbench_Settings.Prefix_RPC_TechTreeUnlock)));

            _harmony.Patch(original: AccessTools.Method(typeof(SmartSwitch), nameof(SmartSwitch.ToggleSwitch)),
                prefix: new HarmonyMethod(typeof(SmartSwitch_ToggleSwitch), nameof(SmartSwitch_ToggleSwitch.Prefix)));
            _harmony.Patch(original: AccessTools.Method(typeof(TimerSwitch), nameof(TimerSwitch.SVSwitch)),
                prefix: new HarmonyMethod(typeof(TimerSwitch_SVSwitch), nameof(TimerSwitch_SVSwitch.Prefix)));
            _harmony.Patch(
                original: AccessTools.Method(typeof(CustomTimerSwitch), nameof(CustomTimerSwitch.SERVER_SetTime)),
                prefix: new HarmonyMethod(typeof(CustomTimerSwitch_SERVER_SetTime),
                    nameof(CustomTimerSwitch_SERVER_SetTime.Prefix)));

            _harmony.Patch(original: AccessTools.Method(typeof(FogMachine), nameof(FogMachine.SetFogOn)),
                prefix: new HarmonyMethod(typeof(FogMachine_Toggle), nameof(FogMachine_Toggle.Prefix_SetFogOn)));
            _harmony.Patch(original: AccessTools.Method(typeof(FogMachine), nameof(FogMachine.SetFogOff)),
                prefix: new HarmonyMethod(typeof(FogMachine_Toggle), nameof(FogMachine_Toggle.Prefix_SetFogOff)));

            _harmony.Patch(
                original: AccessTools.Method(typeof(StrobeLight), nameof(StrobeLight.SetStrobe),
                    new[] { typeof(BaseEntity.RPCMessage) }),
                prefix: new HarmonyMethod(typeof(StrobeLight_Settings), nameof(StrobeLight_Settings.Prefix_SetStrobe)));
            _harmony.Patch(original: AccessTools.Method(typeof(StrobeLight), nameof(StrobeLight.SetStrobeSpeed)),
                prefix: new HarmonyMethod(typeof(StrobeLight_Settings),
                    nameof(StrobeLight_Settings.Prefix_SetStrobeSpeed)));

            _harmony.Patch(
                original: AccessTools.Method(typeof(AudioVisualisationEntity),
                    nameof(AudioVisualisationEntity.ServerUpdateSettings)),
                prefix: new HarmonyMethod(typeof(AudioVisualisationEntity_Settings),
                    nameof(AudioVisualisationEntity_Settings.Prefix_ServerUpdateSettings)));

            // _harmony.Patch(original: AccessTools.Method(typeof(Signage), nameof(Signage.CanUpdateSign)),
            // prefix: new HarmonyMethod(typeof(Signage_Settings), nameof(Signage_Settings.Prefix_CanUpdateSign)));

            // ######################################## NAVAL DEEP SEA #######################################
            _harmony.Patch(original: AccessTools.Method(typeof(BoatBuildingStation), nameof(BoatBuildingStation.CanEnterEditMode)),
                prefix: new HarmonyMethod(typeof(NavalPatch), nameof(NavalPatch.Prefix_BoatBuildingStation_CanEnterEditMode)));
            _harmony.Patch(original: AccessTools.Method(typeof(BoatBuildingStation), "CanClearArea"),
                prefix: new HarmonyMethod(typeof(NavalPatch), nameof(NavalPatch.Prefix_BoatBuildingStation_CanClearArea)));
            _harmony.Patch(AccessTools.Method(typeof(BoatBuildingStation), nameof(BoatBuildingStation.FinishBuilding), new[] { typeof(BaseEntity.RPCMessage) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NavalPatch), nameof(NavalPatch.Prefix_BoatBuildingStation_FinishBuilding),
                    new[] { typeof(BoatBuildingStation), typeof(BaseEntity.RPCMessage) })));

            _harmony.Patch(original: AccessTools.Method(typeof(Anchor), nameof(Anchor.CanBeRaised)),
                prefix: new HarmonyMethod(typeof(NavalPatch), nameof(NavalPatch.Prefix_Anchor_CanBeRaised)));
            _harmony.Patch(original: AccessTools.Method(typeof(Anchor), nameof(Anchor.CanBeLowered)),
                prefix: new HarmonyMethod(typeof(NavalPatch), nameof(NavalPatch.Prefix_Anchor_CanBeLowered)));

            // Blocca lo sparo con il cannone, ma non verrà implementato per non aggravare sulle performance, in quanto ce già il controllo se il player può usare o meno il cannone
            // _harmony.Patch(original: AccessTools.Method(typeof(Cannon), nameof(Cannon.RequestLightFuse)),
            //     prefix: new HarmonyMethod(typeof(NavalPatch), nameof(NavalPatch.Prefix_Cannon_RequestLightFuse)));
            // ###############################################################################################


            // LockSign ha ora due overload (RPCMessage e BasePlayer): serve indicare i parametri per evitare AmbiguousMatchException
            _harmony.Patch(original: AccessTools.Method(typeof(Signage), nameof(Signage.LockSign), new[] { typeof(BaseEntity.RPCMessage) }),
                prefix: new HarmonyMethod(typeof(Signage_Settings), nameof(Signage_Settings.Prefix_LockSign)));
            _harmony.Patch(original: AccessTools.Method(typeof(Signage), nameof(Signage.UnLockSign)),
                prefix: new HarmonyMethod(typeof(Signage_Settings), nameof(Signage_Settings.Prefix_UnLockSign)));

            _harmony.Patch(original: AccessTools.Method(typeof(ShutterFrame), nameof(ShutterFrame.RPC_ToggleShutter)),
                prefix: new HarmonyMethod(typeof(Signage_Settings), nameof(Signage_Settings.Prefix_ShutterFrameToggleShutter)));
            _harmony.Patch(original: AccessTools.Method(typeof(OrnateFrame), nameof(OrnateFrame.RPC_ConfigureFrame)),
                prefix: new HarmonyMethod(typeof(Signage_Settings), nameof(Signage_Settings.Prefix_OrnateFrameConfigureFrame)));

            _harmony.Patch(original: AccessTools.Method(typeof(NeonSign), nameof(NeonSign.SetAnimationSpeed)),
                prefix: new HarmonyMethod(typeof(NeonSign_Settings),
                    nameof(NeonSign_Settings.Prefix_SetAnimationSpeed)));
            _harmony.Patch(original: AccessTools.Method(typeof(NeonSign), nameof(NeonSign.UpdateNeonColors)),
                prefix: new HarmonyMethod(typeof(NeonSign_Settings),
                    nameof(NeonSign_Settings.Prefix_UpdateNeonColors)));

            _harmony.Patch(original: AccessTools.Method(typeof(SearchLight), nameof(SearchLight.RPC_UseLight)),
                prefix: new HarmonyMethod(typeof(SearchLight_Settings), nameof(SearchLight_Settings.Prefix_UseLight)));

            _harmony.Patch(
                original: AccessTools.Method(typeof(PoweredRemoteControlEntity),
                    nameof(PoweredRemoteControlEntity.CanChangeID)),
                prefix: new HarmonyMethod(typeof(PoweredRemoteControlEntity_Settings),
                    nameof(PoweredRemoteControlEntity_Settings.Prefix_CanChangeID)));
            _harmony.Patch(
                original: AccessTools.Method(typeof(PoweredRemoteControlEntity),
                    nameof(PoweredRemoteControlEntity.CanControl)),
                prefix: new HarmonyMethod(typeof(PoweredRemoteControlEntity_Settings),
                    nameof(PoweredRemoteControlEntity_Settings.Prefix_CanControl)));


            _harmony.Patch(
                original: AccessTools.Method(typeof(RFBroadcaster), nameof(RFBroadcaster.ServerSetFrequency)),
                prefix: new HarmonyMethod(typeof(RFBroadcaster_Settings),
                    nameof(RFBroadcaster_Settings.Prefix_ServerSetFrequency)));
            _harmony.Patch(original: AccessTools.Method(typeof(RFReceiver), nameof(RFReceiver.ServerSetFrequency)),
                prefix: new HarmonyMethod(typeof(RFReceiver_Settings),
                    nameof(RFReceiver_Settings.Prefix_ServerSetFrequency)));

            _harmony.Patch(original: AccessTools.Method(typeof(AutoTurret), nameof(AutoTurret.CanControl)),
                prefix: new HarmonyMethod(typeof(AutoTurret_Settings), nameof(AutoTurret_Settings.Prefix_CanControl)));

            // _harmony.Patch(original: AccessTools.Method(typeof(GrowableEntity), nameof(GrowableEntity.PickFruit)),
            //     prefix: new HarmonyMethod(typeof(GrowableEntity_Settings),
            //         nameof(GrowableEntity_Settings.Prefix_PickFruit)));

            _harmony.Patch(original: AccessTools.Method(typeof(IndustrialCrafter), "SvSwitch"),
                prefix: new HarmonyMethod(typeof(IndustrialCrafter_Settings),
                    nameof(IndustrialCrafter_Settings.Prefix_SvSwitch)));

            //######################################## MEDIEVAL ENTITIES ########################################
            _harmony.Patch(original: AccessTools.Method(typeof(BatteringRam), "RPC_OpenDoor"),
                prefix: new HarmonyMethod(typeof(MedievalEntity_Settings),
                    nameof(MedievalEntity_Settings.BatteringRam_Prefix_RPC_OpenDoor)));

            _harmony.Patch(original: AccessTools.Method(typeof(BatteringRam), "RPC_CloseDoor"),
                prefix: new HarmonyMethod(typeof(MedievalEntity_Settings),
                    nameof(MedievalEntity_Settings.BatteringRam_Prefix_RPC_CloseDoor)));

            _harmony.Patch(original: AccessTools.Method(typeof(Door), "RPC_OpenDoor"),
                prefix: new HarmonyMethod(typeof(MedievalEntity_Settings),
                    nameof(MedievalEntity_Settings.SiegeTower_Prefix_RPC_OpenDoor)));

            _harmony.Patch(original: AccessTools.Method(typeof(Door), "RPC_CloseDoor"),
                prefix: new HarmonyMethod(typeof(MedievalEntity_Settings),
                    nameof(MedievalEntity_Settings.SiegeTower_Prefix_RPC_CloseDoor)));

            _harmony.Patch(original: AccessTools.Method(typeof(Catapult), "SERVER_ReloadStart"),
                prefix: new HarmonyMethod(typeof(MedievalEntity_Settings),
                    nameof(MedievalEntity_Settings.Catapult_Prefix_SERVER_ReloadStart)));

            _harmony.Patch(original: AccessTools.Method(typeof(BaseSiegeWeapon), nameof(BaseSiegeWeapon.SERVER_StartPulling)),
                prefix: new HarmonyMethod(typeof(MedievalEntity_Settings),
                    nameof(MedievalEntity_Settings.BaseSiegeWeapon_Prefix_SERVER_StartPulling)));
            //###################################################################################################

            ImageLibrary?.Call("AddImage", CodeLockOnDeployImageUrl, CodeLockOnDeployImageName);
            ImageLibrary?.Call("AddImage", KeyLockOnDeployImageUrl, KeyLockOnDeployImageName);
            ImageLibrary?.Call("AddImage", AutoCloseProximitySystemImageUrl, AutoCloseProximitySystemImageName);

            //TODO da testare e poi rimuovere
            // NextFrame(() => _coroutines.Add(ServerMgr.Instance.StartCoroutine(HandleStorageContainerLockableOnStartup())));

            //TODO da rimuovere
            // if (!_config.AutoLockConfiguration.UseOnlyKeyLock)
            //     NextFrame(() => _coroutines.Add(ServerMgr.Instance.StartCoroutine(UpdateAllPlayersCodeLockAuth())));

            var proximityConfig = _config.ProximitySystemConfiguration;
            if (proximityConfig is { ShowUIWhenPlayerIsNearDoorWithDoorCloser: true })
            {
                InitDoorProximity();
            }

            NextFrame(() => _coroutines.Add(ServerMgr.Instance.StartCoroutine(HandleStartupCoroutinesInSequence())));

            NextFrame(() => _coroutines.Add(ServerMgr.Instance.StartCoroutine(RestoreAllLockScaleSizes())));
        }

        private IEnumerator HandleStartupCoroutinesInSequence()
        {
            yield return ServerMgr.Instance.StartCoroutine(HandleDoorOwnershipOnStartup());
            yield return ServerMgr.Instance.StartCoroutine(HandleUpdateDoorCloserDelayTimeOnStartup());
        }

        private void ApplySkinnerHarmonyPatch()
        {
            if (_harmony == null) return;

            if (_skinnerPatchApplied)
            {
                Puts("Skinner patch already applied, skip.");
                return;
            }

            if (Skinner == null) return;

            try
            {
                var type = Skinner.GetType();
                if (type == null)
                {
                    Puts("ERROR: Skinner runtime type not found!");
                    return;
                }

                var method = AccessTools.Method(type, "SkinDeployable");
                if (method == null)
                {
                    Puts($"ERROR: SkinDeployable method not found on type {type.FullName}!");
                    return;
                }

                _harmony.Patch(
                    original: method,
                    prefix: new HarmonyMethod(typeof(SkinPatch), nameof(SkinPatch.Prefix_Skinner_SkinDeployable))
                );

                _skinnerPatchApplied = true;
                Puts("Skinner Patch applied successfully.");
            }
            catch (Exception e)
            {
                PrintError($"[ApplySkinnerHarmonyPatch] ERROR: {e.Message}\n{e.StackTrace}");
            }
        }

        private void ProcessSteamWorkshopDefinitions()
        {
            Puts("Processing Steam Workshop definitions...");

            // Popola _itemIdToSkin e _workshopIdToItemId con le skin e skin.id di ItemSkinDirectory
            foreach (var skin in ItemSkinDirectory.Instance.skins)
            {
                _itemIdToSkin[skin.id] = skin;
                _workshopIdToItemId[(ulong)skin.id] = skin.id;
            }

            // Popola _workshopIdToItemId con le definizioni di Steam Workshop
            foreach (var inventoryDef in Steamworks.SteamInventory.Definitions)
            {
                var skinId = _itemIdToSkin.ContainsKey(inventoryDef.Id)
                    ? (ulong)inventoryDef.Id
                    : ulong.TryParse(inventoryDef.GetProperty(PropertyWorkshopID), out var parsedId)
                        ? parsedId
                        : 0;

                if (skinId > 0)
                    _workshopIdToItemId[skinId] = inventoryDef.Id;
            }

            _steamWorkshopInitialized = true;
        }

        private List<string> EntityRequiredPermission()
        {
            var itemPermissions = _enabledLockableEntityConfigs
                .Where(i => !IsNullOrEmpty(i.RequiredPermission))
                .Select(i => i.RequiredPermission)
                .ToList();

            if (IsNullOrEmpty(itemPermissions)) return Array.Empty<string>().ToList();

            var validEntityPermissionsList = new List<string?>();

            foreach (var itemPermission in itemPermissions
                         .Select(GetValidPermission)
                         .Where(permissions => !IsNullOrEmpty(permissions)))
            {
                validEntityPermissionsList.AddRange(itemPermission);
            }

            if (IsNullOrEmpty(validEntityPermissionsList)) return Array.Empty<string>().ToList();

            //Remove empty and duplicated permissions
            validEntityPermissionsList = validEntityPermissionsList
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            return validEntityPermissionsList!;
        }

        private List<string> GetValidPermission(ICollection<string> permissions)
        {
            if (IsNullOrEmpty(permissions)) return Array.Empty<string>().ToList();
            return permissions
                .Select(GetValidPermission)
                .Where(perm => perm != null)
                .ToList()!;
        }

        private string? GetValidPermission(string perm)
        {
            if (string.IsNullOrWhiteSpace(perm)) return null;

            perm = perm.Replace(" ", "_");
            perm = AlphanumericRegex.Replace(perm, "");

            var permissionPrefix = Name.ToLower();

            return string.IsNullOrWhiteSpace(perm)
                ? null
                : $"{permissionPrefix}.{perm.ToLower()}";
        }

        private bool HasValidPermission(BasePlayer? player, ICollection<string> permissions)
        {
            if (player == null || IsNullOrEmpty(permissions)) return false;

            var validPermission = GetValidPermission(permissions);
            if (IsNullOrEmpty(validPermission)) return true;

            return validPermission.Any(perm =>
                _entityRequiredPermission.Contains(perm) && permission.UserHasPermission(player.UserIDString, perm));
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UILocker);
                CuiHelper.DestroyUi(player, UIAutoLock);
                CuiHelper.DestroyUi(player, UIAutoClosing);
                CuiHelper.DestroyUi(player, UIProximitySystemIcon);
                CuiHelper.DestroyUi(player, UIProximitySystem);

                DestroyMonoComponent(player);
            }

            DestroyDoorProximity();

            if (!_coroutines.IsNullOrEmpty())
            {
                foreach (var coroutine in _coroutines.Where(c => c != null))
                {
                    ServerMgr.Instance.StopCoroutine(coroutine);
                }
            }

            foreach (var doors in _doorCloserTimers.Values)
            {
                foreach (var entry in doors.Values)
                    entry.Timer?.Destroy();
            }

            //TODO DA RIMUOVERE
            ServerMgr.Instance.StartCoroutine(HandleStorageContainerLockableOnunload());

            _coroutines.Clear();
            _entityRequiredPermission.Clear();
            _vehiclesCockpitPrefabName.Clear();
            _doorCloserTimers.Clear();
            PlayerSavedDalyTime.Clear();

            if (_harmony != null)
            {
                _harmony.UnpatchAll($"{Name}_Patch_{Version.Major}_{Version.Minor}_{Version.Patch}");
            }

            self = null;
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != Skinner) return;
            ApplySkinnerHarmonyPatch();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin != Skinner) return;
            _skinnerPatchApplied = false;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            DestroyMonoComponent(player);

            //Svuoto la cache dei DLC/Skin del player, cosi che se il DLC/Skin viene acquistato prima della connessione al server, non siano presenti dati sporchi nella cache
            PlayerDlcSkinOwnedCache.Remove(player.userID);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyMonoComponent(player);
            DoorProximityOnPlayerDisconnected(player);
        }

        private void OnNewSave(string filename)
        {
            _data.AutoLockPlayerSettingsData.Clear();
            _data.AutoClosingPlayerSettingsData.Clear();
            _data.LockEntityData.Clear();
            _data.DoorCloserEntityData.Clear();
            SaveData();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();
            MergeConfiguration();

            // // For debug
            // DeleteLangFiles();

            if (!Equals(_config.VersionNumber, Version))
            {
                UpdateConfigValues();
                _config.VersionNumber = Version;
            }

            SaveConfig();

            // // For debug for new entities
            // _config.LockConfiguration.Vehicles.Clear();
            // _config.LockConfiguration.Deployables.Clear();
            //
            // PrintWarning("FORCE CONFIG FOR DEBUG: Started updating the configuration file...");
            //
            // var confDebug = Configuration.CreateConfig();
            // if (!confDebug.LockConfiguration.Vehicles.IsNullOrEmpty())
            // {
            //     foreach (var vehicle in confDebug.LockConfiguration.Vehicles)
            //     {
            //         if (!_config.LockConfiguration.Vehicles.Any(i => i.PrefabName.Equals(vehicle.PrefabName, StringComparison.OrdinalIgnoreCase)))
            //         {
            //             vehicle.EnableLock = true;
            //             _config.LockConfiguration.Vehicles.Add(vehicle);
            //         }
            //     }
            //
            //     SaveConfig();
            // }
            //
            // if (!confDebug.LockConfiguration.Deployables.IsNullOrEmpty())
            // {
            //     foreach (var deployable in confDebug.LockConfiguration.Deployables)
            //     {
            //         if (!_config.LockConfiguration.Deployables.Any(i => i.PrefabName.Equals(deployable.PrefabName, StringComparison.OrdinalIgnoreCase)))
            //         {
            //             deployable.EnableLock = true;
            //             _config.LockConfiguration.Deployables.Add(deployable);
            //         }
            //     }
            //
            //     SaveConfig();
            // }
            //
            // PrintWarning("FORCE CONFIG FOR DEBUG: Config update completed!");
            // //-------------------- End debug --------------------
        }

        //Merge Json Ignored value to the config
        private void MergeConfiguration()
        {
            var tmpConf = Configuration.CreateConfig();

            foreach (var tmpVehicle in tmpConf.LockConfiguration.Vehicles)
            {
                var vehicle = _config.LockConfiguration.Vehicles.FirstOrDefault(v =>
                    v.PrefabName.Equals(tmpVehicle.PrefabName, StringComparison.OrdinalIgnoreCase));
                if (vehicle == null)
                    continue;

                vehicle.LockCategory = tmpVehicle.LockCategory;
                vehicle.EntityType = tmpVehicle.EntityType;
                vehicle.CodeLockPosition = tmpVehicle.CodeLockPosition;
                vehicle.CodeLockRotation = tmpVehicle.CodeLockRotation;
                vehicle.ScaleSize = tmpVehicle.ScaleSize;
            }

            foreach (var tmpDeployable in tmpConf.LockConfiguration.Deployables)
            {
                var deployable = _config.LockConfiguration.Deployables.FirstOrDefault(v =>
                    v.PrefabName.Equals(tmpDeployable.PrefabName, StringComparison.OrdinalIgnoreCase));
                if (deployable == null)
                    continue;

                deployable.LockCategory = tmpDeployable.LockCategory;
                deployable.EntityType = tmpDeployable.EntityType;
                deployable.CodeLockPosition = tmpDeployable.CodeLockPosition;
                deployable.CodeLockRotation = tmpDeployable.CodeLockRotation;
                deployable.ScaleSize = tmpDeployable.ScaleSize;
            }
        }

        private void DeleteLangFiles()
        {
            var langDir = Interface.Oxide.LangDirectory;
            string[] languages = lang.GetLanguages(this);
            foreach (var language in languages)
            {
                var langFile = Path.Combine(langDir, language, $"{Name}.json");
                if (!File.Exists(langFile)) continue;
                File.Delete(langFile);
                Puts($"########## Delete old lang file: {langFile} ##########");
            }
        }

        private void UpdateConfigValues()
        {
            UpdateDataValues();

            if (_config.VersionNumber < new VersionNumber(0, 9, 84))
            {
                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/bikes/motorbike.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Motorbike",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/bikes/motorbike.prefab",

                        EntityType = EntityType.VehicleBike,
                        CodeLockPosition = new Vector3(0.074f, 0.8f, 0.14f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/bikes/motorbike_sidecar.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Motorbike With Sidecar",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/bikes/motorbike_sidecar.prefab",

                        EntityType = EntityType.VehicleBike,
                        CodeLockPosition = new Vector3(0.72f, 0.52f, -0.65f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/bikes/pedalbike.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Pedal Bike",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/bikes/pedalbike.prefab",

                        EntityType = EntityType.VehicleBike,
                        CodeLockPosition = new Vector3(0.0f, 0.6f, -0.07f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/bikes/pedaltrike.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Pedal Trike",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/bikes/pedaltrike.prefab",

                        EntityType = EntityType.VehicleBike,
                        CodeLockPosition = new Vector3(0.0f, 0.7f, -0.5f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }


                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals(
                            "assets/prefabs/deployable/weaponracks/weaponrack_single1.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Frontier Bolts Single Item Rack",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_single1.deployed.prefab",

                        EntityType = EntityType.DeployableWeaponRack,
                        CodeLockPosition = new Vector3(-0.5f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals(
                            "assets/prefabs/deployable/weaponracks/weaponrack_single2.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Frontier Horseshoe Single Item Rack",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_single2.deployed.prefab",

                        EntityType = EntityType.DeployableWeaponRack,
                        CodeLockPosition = new Vector3(-0.5f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals(
                            "assets/prefabs/deployable/weaponracks/weaponrack_single3.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Frontier Horns Single Item Rack",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_single3.deployed.prefab",

                        EntityType = EntityType.DeployableWeaponRack,
                        CodeLockPosition = new Vector3(-0.5f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }
            }


            if (_config.VersionNumber < new VersionNumber(0, 9, 85))
            {
                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/bundled/prefabs/static/miningquarry_static.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Mining Quarry Static",
                        EnableLock = false,
                        PrefabName = "assets/bundled/prefabs/static/miningquarry_static.prefab",

                        EntityType = EntityType.DeployableMiningQuarryStatic,
                        CodeLockPosition = new Vector3(-2.36f, 4.5f, 1.66f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }
            }


            if (_config.VersionNumber < new VersionNumber(0, 9, 86))
            {
                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/deployable/oil jack/mining.pumpjack.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Pump Jack",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/oil jack/mining.pumpjack.prefab",

                        EntityType = EntityType.DeployableMiningPumpJack,
                        CodeLockPosition = new Vector3(3.26f, 4.5f, -1.36f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/bundled/prefabs/static/pumpjack-static.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Pump Jack Static",
                        EnableLock = false,
                        PrefabName = "assets/bundled/prefabs/static/pumpjack-static.prefab",

                        EntityType = EntityType.DeployableMiningPumpJackStatic,
                        CodeLockPosition = new Vector3(3.26f, 4.5f, -1.36f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }
            }

            if (_config.VersionNumber < new VersionNumber(0, 9, 902))
            {
                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "RHIB Storage",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.38f, 0.30f),
                        CodeLockRotation = Quaternion.Euler(0, 90, 0),

                        RequiredPermission = new[] { "" }
                    });
                }
            }

            if (_config.VersionNumber < new VersionNumber(0, 9, 910))
            {
                Puts("Configuration file is outdated. Updating...");
                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Chinese Lantern",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern.deployed.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, -0.4f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern_white.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Chinese Lantern White",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern_white.deployed.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, -0.4f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/deployable/tuna can wall lamp/tunalight.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Tuna Can Lamp",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/tuna can wall lamp/tunalight.deployed.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/deployable/lantern/lantern.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Lantern",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/deployable/campfire/campfire.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Camp Fire",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/campfire/campfire.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/misc/halloween/cursed_cauldron/cursedcauldron.deployed.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Cursed Cauldron",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/misc/halloween/cursed_cauldron/cursedcauldron.deployed.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/misc/halloween/skull_fire_pit/skull_fire_pit.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Skull Fire Pit",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/misc/halloween/skull_fire_pit/skull_fire_pit.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/deployable/jack o lantern/jackolantern.angry.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Jack O Lantern Angry",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/jack o lantern/jackolantern.angry.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.2f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/deployable/jack o lantern/jackolantern.happy.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Jack O Lantern Happy",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/deployable/jack o lantern/jackolantern.happy.prefab",

                        EntityType = EntityType.DeployableStorage,
                        CodeLockPosition = new Vector3(0.0f, 0.2f, 0.0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 0, 0))
            {
                Puts("Configuration file is outdated. Updating...");

                _config.ChatPrefix = "UltimateLocker";

                if (_config.AutoLockConfiguration == null)
                {
                    _config.AutoLockConfiguration = new AutoLockConfiguration();
                }

                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 0, 8))
            {
                Puts("Configuration file is outdated. Updating...");

                if (_config.AutoClosingConfiguration == null)
                {
                    _config.AutoClosingConfiguration = new AutoClosingConfiguration();
                }

                Puts("Configuration file updated.");

                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(1, 1, 2))
            {
                Puts("Configuration file is outdated. Updating...");

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Car Chassis 2 Module Car",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab",

                        LockCategory = LockCategory.Vehicle,
                        EntityType = EntityType.Vehicle,
                        CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Car Chassis 3 Module Car",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab",

                        LockCategory = LockCategory.Vehicle,
                        EntityType = EntityType.Vehicle,
                        CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Car Chassis 4 Module Car",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab",

                        LockCategory = LockCategory.Vehicle,
                        EntityType = EntityType.Vehicle,
                        CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                        RequiredPermission = new[] { "" }
                    });
                }

                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 1, 4))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(1, 1, 5))
            {
                DeleteLangFiles();

                Puts("Configuration file is outdated. Updating...");
                _config.AutoLockConfiguration.AutoLockPlayerConfiguration = new AutoLockPlayerConfiguration();
                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 1, 8))
            {
                DeleteLangFiles();
                SaveConfig();
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 0))
            {
                DeleteLangFiles();

                Puts("Configuration file is outdated. Updating...");

                if (!_config.LockConfiguration.Deployables.Any(dep =>
                        dep.PrefabName.Equals("assets/prefabs/building/wall.window.shutter/shutter.wood.a.prefab")))
                {
                    _config.LockConfiguration.Deployables.Add(new LockableEntityConfig
                    {
                        ItemName = "Wood Shutters",
                        EnableLock = false,
                        PrefabName = "assets/prefabs/building/wall.window.shutter/shutter.wood.a.prefab",

                        LockCategory = LockCategory.Window,
                        EntityType = EntityType.DeployableConstruction,
                        CodeLockPosition = new Vector3(0.18f, 0.26f, 0.96f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 1))
            {
                DeleteLangFiles();

                Puts("Configuration file is outdated. Updating...");

                var horsePrefab = _config.LockConfiguration.Vehicles
                    .FirstOrDefault(lc => lc.PrefabName.Equals("assets/rust.ai/nextai/testridablehorse.prefab", StringComparison.OrdinalIgnoreCase));
                if (horsePrefab != null)
                {
                    horsePrefab.PrefabName = "assets/content/vehicles/horse/_old/testridablehorse.prefab";
                }

                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 2))
            {
                DeleteLangFiles();

                Puts("Configuration file is outdated. Updating...");

                var horsePrefab = _config.LockConfiguration.Vehicles
                    .FirstOrDefault(lc => lc.PrefabName.Equals("assets/content/vehicles/horse/_old/testridablehorse.prefab", StringComparison.OrdinalIgnoreCase));
                if (horsePrefab != null)
                {
                    horsePrefab.PrefabName = "assets/content/vehicles/horse/ridablehorse2.prefab";
                }

                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 3))
            {
                DeleteLangFiles();

                Puts("Configuration file is outdated. Updating...");

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/siegeweapons/ballista/ballista.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Mounted Ballista",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/siegeweapons/ballista/ballista.entity.prefab",

                        LockCategory = LockCategory.Medieval,
                        EntityType = EntityType.VehicleMedieval,
                        CodeLockPosition = new Vector3(0.24f, 0.3f, 0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 90.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Battering Ram",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab",

                        LockCategory = LockCategory.Medieval,
                        EntityType = EntityType.VehicleMedieval,
                        CodeLockPosition = new Vector3(2.4f, 2.0f, 1.64f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/siegeweapons/catapult/catapult.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Catapult",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/siegeweapons/catapult/catapult.entity.prefab",

                        LockCategory = LockCategory.Medieval,
                        EntityType = EntityType.VehicleMedieval,
                        CodeLockPosition = new Vector3(1.124f, 2.74f, 0.9f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/siegeweapons/siegetower/siegetower.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Siege Tower",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/siegeweapons/siegetower/siegetower.entity.prefab",

                        LockCategory = LockCategory.Medieval,
                        EntityType = EntityType.VehicleMedieval,
                        CodeLockPosition = new Vector3(1.27f, 2.4f, -0.4f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                if (!_config.LockConfiguration.Vehicles.Any(dep =>
                        dep.PrefabName.Equals("assets/content/vehicles/siegeweapons/ballista/ballistagun.static.entity.prefab")))
                {
                    _config.LockConfiguration.Vehicles.Add(new LockableEntityConfig
                    {
                        ItemName = "Ballista",
                        EnableLock = false,
                        PrefabName = "assets/content/vehicles/siegeweapons/ballista/ballistagun.static.entity.prefab",

                        LockCategory = LockCategory.Medieval,
                        EntityType = EntityType.DeployableMedieval,
                        CodeLockPosition = new Vector3(0.0f, 0.6f, 0f),
                        CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                        RequiredPermission = new[] { "" }
                    });
                }

                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 6))
            {
                DeleteLangFiles();

                Puts("Configuration file is outdated. Updating...");
                _config.AutoLockConfiguration.AutoClosingPlayerConfiguration = new AutoClosingPlayerConfiguration();
                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 7))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                AddNewItemFromConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 8))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 9))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(1, 3, 0))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(1, 3, 2))
            {
                Puts("Configuration file is outdated. Updating...");

                var horsePrefab = _config.LockConfiguration.Vehicles
                    .FirstOrDefault(lc => lc.PrefabName.Equals("assets/content/vehicles/horse/ridablehorse2.prefab", StringComparison.OrdinalIgnoreCase));
                if (horsePrefab != null)
                {
                    horsePrefab.PrefabName = "assets/content/vehicles/horse/ridablehorse.prefab";
                    horsePrefab.CodeLockPosition = new Vector3(0.0f, 1.24f, 0.0f);
                }

                SaveConfig();
                Puts("Configuration file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 3, 3))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                AddNewItemFromConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 3, 7))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                AddNewItemFromConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 5, 2))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                AddNewItemFromConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 5, 4))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(1, 5, 7))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                AddNewItemFromConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 5, 8))
            {
                DeleteLangFiles();
                PrintWarning("Configuration file is outdated. Updating...");

                _config.SkinBlacklistConfiguration = new SkinBlacklistConfiguration
                {
                    LockBlacklist = new HashSet<ulong>(),
                    DoorCloserBlacklist = new HashSet<ulong>()
                };

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 6, 0))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                var woodShutters = _config.LockConfiguration.Deployables
                    .FirstOrDefault(lc => lc.PrefabName.Equals("assets/prefabs/building/wall.window.shutter/shutter.wood.a.prefab", StringComparison.OrdinalIgnoreCase));
                if (woodShutters != null)
                {
                    woodShutters.LockCategory = LockCategory.Window;
                }

                var kriegStorageCrates = _config.LockConfiguration.Deployables
                    .FirstOrDefault(lc =>
                        lc.PrefabName.Equals("assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_horizontal/krieg_storage_horizontal.prefab",
                            StringComparison.OrdinalIgnoreCase));
                if (kriegStorageCrates != null)
                {
                    kriegStorageCrates.CodeLockRotation = Quaternion.Euler(0, 90, 2);
                }

                var kriegStorageBarrel = _config.LockConfiguration.Deployables
                    .FirstOrDefault(lc => lc.PrefabName.Equals("assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_vertical/krieg_storage_vertical.prefab",
                        StringComparison.OrdinalIgnoreCase));
                if (kriegStorageBarrel != null)
                {
                    kriegStorageBarrel.CodeLockRotation = Quaternion.Euler(0, 90, 0);
                }

                _config.AutoLockConfiguration.AutoLockCategoryConfiguration = new AutoLockCategoryConfiguration();
                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(1, 6, 1))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(2, 0, 1))
            {
                DeleteLangFiles();
                PrintWarning("Configuration file is outdated. Updating...");

                _config.ProximitySystemConfiguration = new ProximitySystemConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }

            if (_config.VersionNumber < new VersionNumber(2, 0, 4))
            {
                DeleteLangFiles();
            }

            if (_config.VersionNumber < new VersionNumber(2, 0, 7))
            {
                PrintWarning("Configuration file is outdated. Updating...");

                AddNewItemFromConfiguration();

                SaveConfig();
                PrintWarning("Config update completed!");
            }
        }

        private void AddNewItemFromConfiguration()
        {
            var conf = Configuration.CreateConfig();
            if (!conf.LockConfiguration.Vehicles.IsNullOrEmpty())
            {
                foreach (var vehicle in conf.LockConfiguration.Vehicles)
                {
                    if (!_config.LockConfiguration.Vehicles.Any(i => i.PrefabName.Equals(vehicle.PrefabName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _config.LockConfiguration.Vehicles.Add(vehicle);
                    }
                }
            }

            if (!conf.LockConfiguration.Deployables.IsNullOrEmpty())
            {
                foreach (var deployable in conf.LockConfiguration.Deployables)
                {
                    if (!_config.LockConfiguration.Deployables.Any(i => i.PrefabName.Equals(deployable.PrefabName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _config.LockConfiguration.Deployables.Add(deployable);
                    }
                }
            }
        }

        private void UpdateDataValues()
        {
            if (_config.VersionNumber < new VersionNumber(1, 2, 0))
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);
                Puts("Updating Data file...");

                if (_data.AutoLockPlayerSettingsData.IsNullOrEmpty())
                    return;

                foreach (var autoLockPlayerSettingsData in _data.AutoLockPlayerSettingsData.Select(psd => psd.Value))
                {
                    autoLockPlayerSettingsData.VehicleLockAutoClosingOnDismountEnabled =
                        _config.AutoLockConfiguration.AutoLockPlayerConfiguration.EnableVehicleLockAutoClosingOnDismountByDefault;
                }

                if (_data.AutoClosingPlayerSettingsData.IsNullOrEmpty())
                    return;

                foreach (var autoClosingPlayerSettingsData in _data.AutoClosingPlayerSettingsData.Select(psd => psd.Value))
                {
                    autoClosingPlayerSettingsData.WindowClosingDelay = _config.AutoClosingConfiguration.DefaultClosingDelayTime;
                }

                SaveData();
                Puts("Data file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 2, 3))
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);
                Puts("Updating Data file...");

                if (_data.AutoLockPlayerSettingsData.IsNullOrEmpty())
                    return;

                foreach (var autoLockPlayerSettingsData in _data.AutoLockPlayerSettingsData.Select(psd => psd.Value))
                {
                    autoLockPlayerSettingsData.MedievalEntityLockEnabled = _config.AutoLockConfiguration.AutoLockPlayerConfiguration.EnableMedievalEntityLockByDefault;
                }

                SaveData();
                Puts("Data file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(1, 5, 4))
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);
                Puts("Updating Data file...");

                if (_data.AutoLockPlayerSettingsData.IsNullOrEmpty())
                    return;

                foreach (var autoLockPlayerSettingsData in _data.AutoLockPlayerSettingsData.Select(psd => psd.Value))
                {
                    autoLockPlayerSettingsData.CodelockSkinFlightControlCodelockEnabled = DefaultCodelockSkinFlightControlCodelockEnabled;
                }

                SaveData();
                Puts("Data file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(2, 0, 0))
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);
                Puts("Updating Data file...");

                var doorCloserToRemoveList = BaseNetworkable.serverEntities
                    .OfType<DoorCloser>()
                    .Where(dc => dc != null && !dc.IsDestroyed && dc.OwnerID.IsSteamId())
                    .ToList();

                if (!doorCloserToRemoveList.IsNullOrEmpty())
                {
                    foreach (var dc in doorCloserToRemoveList)
                    {
                        dc.Kill();
                    }
                }

                _data.DoorCloserEntityData.Clear();

                SaveData();
                Puts("Data file updated.");
            }

            if (_config.VersionNumber < new VersionNumber(2, 0, 1))
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);
                Puts("Updating Data file...");

                if (_data.AutoClosingPlayerSettingsData.IsNullOrEmpty())
                    return;

                foreach (var autoClosingPlayerSettingsData in _data.AutoClosingPlayerSettingsData.Select(psd => psd.Value))
                {
                    autoClosingPlayerSettingsData.ProximitySystemUIEnabled = true;
                }

                SaveData();
                Puts("Data file updated.");
            }
        }

        protected override void LoadDefaultConfig()
        {
            _config = Configuration.CreateConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        #endregion

        #region Hooks - Clans

        private void OnClanDestroy(string clanName)
        {
            timer.Once(5f, () =>
            {
                UpdateAllCodeLockAuthShareByClanName(clanName);
                _coroutines.Add(ServerMgr.Instance.StartCoroutine(UpdateAllPlayersCodeLockAuth()));
            });
        }

        private void OnClanMemberGone(string userID, string tag)
        {
            // Alcune versioni del plugin Clans invocano l'hook con i parametri invertiti (tag, userID)
            if (!ulong.TryParse(userID, out var playerSteamId) && !ulong.TryParse(tag, out playerSteamId)) return;
            timer.Once(5f, () => { UpdateAllCodeLockAuthShareByTeamMembers(new List<ulong> { playerSteamId }); });
        }

        private void OnClanMemberGone(ulong playerId, List<ulong> memberUserIDs)
        {
            if (memberUserIDs == null || memberUserIDs.IsNullOrEmpty()) return;
            timer.Once(5f, () => { UpdateAllCodeLockAuthShareByTeamMembers(memberUserIDs); });
        }

        private void OnClanMemberGone(string playerId, List<string> memberUserIDs)
        {
            if (memberUserIDs == null || memberUserIDs.IsNullOrEmpty()) return;
            var memberSteamIdList = ParseSteamIdList(memberUserIDs);
            if (memberSteamIdList.IsNullOrEmpty()) return;
            timer.Once(5f, () => { UpdateAllCodeLockAuthShareByTeamMembers(memberSteamIdList); });
        }

        private void OnClanMemberGone(string tag, ulong leaving, List<ulong> members)
        {
            var memberSteamIdList = members == null ? new List<ulong>() : new List<ulong>(members);
            if (leaving.IsSteamId() && !memberSteamIdList.Contains(leaving)) memberSteamIdList.Add(leaving);
            if (memberSteamIdList.IsNullOrEmpty()) return;
            timer.Once(5f, () => { UpdateAllCodeLockAuthShareByTeamMembers(memberSteamIdList); });
        }

        private void OnClanMemberGone(string tag, string leaving, List<string> members)
        {
            var memberSteamIdList = ParseSteamIdList(members);
            if (ulong.TryParse(leaving, out var leavingSteamId) && leavingSteamId.IsSteamId() && !memberSteamIdList.Contains(leavingSteamId)) memberSteamIdList.Add(leavingSteamId);
            if (memberSteamIdList.IsNullOrEmpty()) return;
            timer.Once(5f, () => { UpdateAllCodeLockAuthShareByTeamMembers(memberSteamIdList); });
        }

        private void OnClanUpdate(string clanName)
        {
            timer.Once(5f, () => { UpdateAllCodeLockAuthShareByClanName(clanName); });
        }

        #endregion

        #region Hooks - Rust Team

        private object? OnTeamUpdated(ulong currentTeam, RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            if (playerTeam.members.IsNullOrEmpty()) return null;

            var members = new List<ulong>();
            members.AddRange(playerTeam.members);
            members.Add(player.userID);

            NextTick(() => { UpdateAllCodeLockAuthShareByTeamMembers(members); });
            return null;
        }

        private void OnTeamAcceptInvite(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            if (playerTeam.members.IsNullOrEmpty()) return;

            var members = new List<ulong>();
            members.AddRange(playerTeam.members);
            members.Add(player.userID);

            NextTick(() => { UpdateAllCodeLockAuthShareByTeamMembers(members); });
        }

        private object? OnTeamLeave(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            if (playerTeam.members.IsNullOrEmpty()) return null;

            var members = new List<ulong>();
            members.AddRange(playerTeam.members);
            members.Add(player.userID);

            NextTick(() => { UpdateAllCodeLockAuthShareByTeamMembers(members); });

            return null;
        }

        private object? OnTeamKick(RelationshipManager.PlayerTeam playerTeam, BasePlayer leader, ulong target)
        {
            if (playerTeam.members.IsNullOrEmpty()) return null;

            var members = new List<ulong>();
            members.AddRange(playerTeam.members);
            members.Add(target);

            NextTick(() => { UpdateAllCodeLockAuthShareByTeamMembers(members); });

            return null;
        }

        private void OnTeamDisbanded(RelationshipManager.PlayerTeam playerTeam)
        {
            if (playerTeam.members.IsNullOrEmpty()) return;
            NextTick(() =>
            {
                UpdateAllCodeLockAuthShareByTeamMembers(playerTeam.members);
                _coroutines.Add(ServerMgr.Instance.StartCoroutine(UpdateAllPlayersCodeLockAuth()));
            });
        }

        #endregion

        #region Hook - AutoDoors Integration

        //AutoDoors plugin integration: Prevents automatic door closing if a Door Closer is present
        private object? OnDoorAutoClose(BasePlayer player, Door door)
        {
            if (door == null) return null;

            if (HasDoorCloserActiveTimer(door))
            {
                return false;
            }

            return null;
        }

        #endregion

        #region Hooks - Core

        private void OnDoorOpened(Door door, BasePlayer player)
        {
            if (player == null) return;
            AddDoorCloserTimer(player.userID, door);
        }

        private void OnDoorClosed(Door door, BasePlayer player)
        {
            DestroyDoorCloserTimer(door);
        }

        private void OnPlayerEnterDoorRadius(BasePlayer player, Door? door)
        {
            if (player == null || !player.userID.IsSteamId() || door == null || !door.OwnerID.IsSteamId()) return;

            var proximityConfig = _config.ProximitySystemConfiguration;
            if (proximityConfig is not { ShowUIWhenPlayerIsNearDoorWithDoorCloser: true }) return;

            var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(player);
            if (autoClosingPlayerSettings is not { AutoClosingEnabled: true, ProximitySystemUIEnabled: true }) return;

            ShowProximityIconUI(player, door);
        }

        private void OnPlayerLeaveDoorRadius(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId())
                return;

            var proximityConfig = _config.ProximitySystemConfiguration;
            if (proximityConfig is null or { ShowUIWhenPlayerIsNearDoorWithDoorCloser: false, ShowUIWhenPressingKey: false })
                return;

            DestroyProximitySystemIconUI(player);
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            //TODO da testare e poi rimuovere
            // if (entity == null || !entity.OwnerID.IsSteamId())
            //     return;
            //
            // if (!_config.LockConfiguration.Deployables.IsNullOrEmpty() && !_allowedItemsLock.IsNullOrEmpty())
            // {
            //     if (entity != null && entity is StorageContainer storageContainer && storageContainer.OwnerID.IsSteamId())
            //     {
            //         if (_config.LockConfiguration.Deployables.Any(d =>
            //                 d.PrefabName.Equals(storageContainer.PrefabName, StringComparison.OrdinalIgnoreCase)))
            //         {
            //             storageContainer.isLockable = _allowedItemsLock.Any(ai =>
            //                 ai.PrefabName.Equals(storageContainer.PrefabName, StringComparison.OrdinalIgnoreCase));
            //
            //             storageContainer.SendNetworkUpdateImmediate();
            //         }
            //     }
            // }

            if (entity == null || !entity.OwnerID.IsSteamId())
                return;

            // // For debug for new entities
            // if (entity.OwnerID.IsSteamId())
            // {
            //     Puts($"########## ENTITY NAME: {entity.PrefabName} - ENTITYID: {entity.net.ID.Value} ##########");
            // }

            if (IsAnEntityToIgnore(entity)) return;

            var player = BasePlayer.FindByID(entity!.OwnerID);
            if (player == null || player.IsNpc) return;

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse)) return;

            //#################### Auto Closing ####################
            NextFrame(() => { DeployDoorCloser(player, entity, false); });

            //#################### Auto lock ####################
            // Se è in corso un RestoreLockAfterReskin per questo player, evito di piazzare qui una serratura
            // "di default": vincerebbe la corsa sulla nuova entità prima che il fix la sostituisca con quella
            // (dello stesso tipo) che era presente prima del reskin.
            if (!_pendingReskinLockRestore.Contains(player.userID))
                NextFrame(() => { DeployLocker(player, entity, false); });

            //####################################################################################################
        }

        private object? CanDeployItem(BasePlayer basePlayer, Deployer deployer, NetworkableId entityId)
        {
            if (basePlayer == null || basePlayer.IsNpc) return null;

            var activeItem = basePlayer.GetActiveItem();
            if (activeItem == null) return null;
            var itemID = activeItem.info.itemid;

            var isLock = itemID is KeyLockItemID or CodeLockItemID or FlightControlCodeLockItemID;

            if (isLock)
            {
                var targetEntity = BaseNetworkable.serverEntities.Find(entityId) as BaseEntity;
                if (targetEntity == null) return null;

                var skinID = targetEntity.skinID;
                if (skinID == 0) return null;

                var blacklistType = isLock ? SkinBlacklistType.Lock : SkinBlacklistType.DoorCloser;

                if (IsSkinBlacklisted(blacklistType, skinID))
                {
                    SendMessage(basePlayer, Lang("EntitySkinBlacklisted", basePlayer.UserIDString));
                    return true;
                }
            }

            return null;
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            if (go == null || go.ToBaseEntity() == null || go.ToBaseEntity().GetEntity() == null)
                return;

            var baseEntity = go.ToBaseEntity().GetEntity();
            if (baseEntity == null)
                return;

            var plannerPlayer = planner.GetOwnerPlayer();
            if (plannerPlayer != null && _config.SetPlayerAsOwnerWhenPlacedQuarry &&
                baseEntity is MiningQuarry miningQuarry && plannerPlayer.userID.IsSteamId())
            {
                miningQuarry.OwnerID = plannerPlayer.userID;
            }

            if (baseEntity is not CodeLock && baseEntity is not KeyLock)
                return;

            var baseLock = baseEntity as BaseLock;
            if (baseLock == null || !baseLock.OwnerID.IsSteamId()) return;

            var player = BasePlayer.FindByID(baseLock.OwnerID);
            if (player == null) return;

            if (CanDeployLock(player, baseLock))
                EntityDeployed(baseLock, null, null, player);
        }

        private object? SB_CanReskinDeployableWith(BasePlayer player, BaseEntity entity, ItemDefinition itemDefinition, ulong newSkin)
        {
            RestoreLockAfterReskin(entity, player, newSkin, itemDefinition);
            return null;
        }

        // Fix Sparizione/cambio di posizione della serratura dopo che viene cambiata la skin con la bomboletta spray
        private void OnEntityReskinned(CodeLock codeLock, ulong newSkinID, BasePlayer player)
        {
            // // For debug
            // Puts($"########## CODELOCK SKIN: NEW SKIN {skin.id} - STEAM INVENTORY SKIN ID {skin.invItem?.id} ##########");

            if (codeLock == null) return;
            var entity = codeLock.GetParentEntity();
            if (entity == null) return;

            // Applica il fix solo alle entità specificate nella configurazione, quelle che di base non possono avere una serratura.
            // Per le altre, quelle che di base possono avere una serratura, il problema non si verifica
            var lockableEntityConfig = GetLockableEntityConfigForEntity(entity);
            if (lockableEntityConfig == null) return;

            // // For debug
            // Puts($"########## CODELOCK CONFIG: NAME {codeLockConfig.PrefabName} - ITEM NAME: {codeLockConfig.ItemName} - CATEGORY {codeLockConfig.LockCategory} ##########");
            // Puts($"########## CODELOCK PARENT ENTITY: NAME {entity.PrefabName} - ENTITYID: {entity.net.ID.Value} ##########");

            codeLock.SetParent(entity);

            if (!entity.HasSlot(BaseEntity.Slot.Lock))
                entity.SetSlot(BaseEntity.Slot.Lock, codeLock);

            codeLock.SendNetworkUpdate_Position();
            codeLock.SendNetworkUpdateImmediate();
        }

        /// <summary>
        /// Ricentra la serratura (posizione e rotazione locali) dell' <paramref name="entity"/> dopo che
        /// quest'ultima è stata reskinnata con la bomboletta spray vanilla di Rust.
        /// </summary>
        /// <remarks>
        /// Per alcune combinazioni entità/skin (es. Furnace -&gt; Legacy Furnace) Rust distrugge l'entità
        /// originale e ne crea una diversa per applicare la skin. A differenza di SkinBox/Skinner (gestiti
        /// da <see cref="RestoreLockAfterReskin"/>, dove la serratura viene persa del tutto e va ricreata
        /// da zero), con la bomboletta spray vanilla il motore riassegna da solo la serratura (l'entità
        /// figlia nello <see cref="BaseEntity.Slot.Lock"/>) alla nuova entità: qui basta correggerne
        /// l'offset locale, non ricrearla.
        /// <para>
        /// Si applica solo alle entità "custom" configurate in <see cref="GetLockableEntityConfigForEntity"/>
        /// (quelle che di base non supportano nativamente una serratura): per le altre il problema non si
        /// presenta. È l'overload gemello di
        /// <see cref="OnEntityReskinned(CodeLock, ulong, BasePlayer)"/>, che invece gestisce
        /// il caso in cui a essere reskinnata sia la serratura stessa (non l'entità a cui è agganciata).
        /// </para>
        /// </remarks>
        /// <param name="entity">L'entità reskinnata (quella nuova, se Rust l'ha ricreata).</param>
        /// <param name="newSkinID">Lo skin ID applicato.</param>
        /// <param name="player">Il player che ha eseguito il reskin.</param>
        private void OnEntityReskinned(BaseEntity entity, ulong newSkinID, BasePlayer player)
        {
            if (entity == null || !entity.OwnerID.IsSteamId()) return;
            if (player == null || !player.userID.IsSteamId()) return;

            var baseLock = GetLock(entity);
            if (baseLock == null) return;

            var lockableEntityConfig = GetLockableEntityConfigForEntity(entity);
            if (lockableEntityConfig == null) return;

            AdjustBaseLockPosition(entity, baseLock, lockableEntityConfig);

            entity.SendNetworkUpdate_Position();
            entity.SendNetworkUpdateImmediate();
        }

        private object? OnEntityKill(BaseLock baseLock)
        {
            if (baseLock == null || !baseLock.OwnerID.IsSteamId()) return null;
            var player = BasePlayer.FindByID(baseLock.OwnerID);
            if (player == null) return null;

            EntityRemoved(baseLock, null, player);

            return null;
        }

        private object? OnEntityKill(ModularCar modularCar)
        {
            if (modularCar == null || !modularCar.OwnerID.IsSteamId()) return null;
            var player = BasePlayer.FindByID(modularCar.OwnerID);
            if (player == null) return null;

            EntityRemoved(null, modularCar, player);

            return null;
        }

        private object? OnEntityKill(Door door)
        {
            if (door?.net is null || !door.OwnerID.IsSteamId()) return null;
            RemoveDoorCloserData(door);
            return null;
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (newItem == null || player == null) return;

            // // For debug for new entities
            // Puts($"########## ITEM NAME: {newItem.info.name} - {newItem.info.shortname} - ITEMID: {newItem.info.itemid} - SKIN: {newItem.skin} ##########");

            if (permission.UserHasPermission(player.UserIDString, PermissionUse) && newItem.info.itemid is KeyLockItemID or CodeLockItemID or FlightControlCodeLockItemID)
            {
                var lockerBehaviour = player.GetComponent<LockerBehaviour>();
                if (lockerBehaviour == null)
                    player.gameObject.AddComponent<LockerBehaviour>();
            }
            else if (player.HasComponent<LockerBehaviour>())
                Destroy(player.GetComponent<LockerBehaviour>());
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !input.IsAnyDown()) return;

            var proximityConfig = _config.ProximitySystemConfiguration;
            if (proximityConfig is { ShowUIWhenPressingKey: true })
            {
                var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(player);
                if (autoClosingPlayerSettings is { AutoClosingEnabled: true /*, ProximitySystemUIEnabled: true*/ })
                {
                    var show = proximityConfig.ButtonToShowUI switch
                    {
                        1 => input.IsDown(BUTTON.FIRE_THIRD),
                        2 => input.IsDown(BUTTON.SPRINT) && input.IsDown(BUTTON.FIRE_THIRD),
                        3 => input.IsDown(BUTTON.DUCK) && input.IsDown(BUTTON.FIRE_THIRD),
                        _ => false
                    };

                    // GetEntityByRayCast solo se il tasto è premuto — evita il raycast inutile
                    if (show && GetEntityByRayCast(player, NearProximityCheckRadius) is Door door)
                        ShowProximityUI(player, door);
                }
            }

            // Esegue il deploy della serratura solo se è stato premuto il tasto di fuoco
            if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY)) return;

            player.GetComponent<LockerBehaviour>()?.HandleInput(input);
        }

        //TODO DA RIMUOVERE
        // Prevents Rust from managing Key Lock placement
        //
        // private object? CanDeployItem(BasePlayer player, Deployer deployer, NetworkableId entityId)
        // {
        //     if (player == null || !entityId.IsValid) return null;
        //
        //     var activeItem = player.GetActiveItem();
        //     if (activeItem == null || (!activeItem.info.itemid.Equals(CodeLockItemID) && !activeItem.info.itemid.Equals(FlightControlCodelockItemID) &&
        //                                !activeItem.info.itemid.Equals(KeyLockItemID))) return null;
        //
        //     var entity = BaseNetworkable.serverEntities.Find(entityId);
        //     if (entity == null || entity is not BaseEntity targetEntity) return null;
        //
        //     if (IsAllowedEntity(targetEntity))
        //     {
        //         if (activeItem.info.itemid.Equals(KeyLockItemID)) return false;
        //         if (!CanDeployLock(player, targetEntity)) return false;
        //     }
        //
        //     return null;
        // }

//TODO da ricontrollare nel prossimo update di Carbon
#if CARBON
#else
        private void CanLock(BasePlayer player, BaseLock baseLock)
        {
            if (player == null || baseLock == null) return;
            if (baseLock is CodeLock codeLock && !codeLock.whitelistPlayers.Contains(player.userID.Get()))
                UpdateCodeLockAuthShareOrSettings(codeLock, null, player);

            var isAllowed = IsEntityOwner(baseLock, player) || (baseLock is CodeLock cl && cl.whitelistPlayers.Contains(player.userID.Get()));
            // SetFlag è deprecato: Facepunch lo rimuove con il wipe del 03/09/2026. Il modo è
            // SendNetworkUpdate e non SendNetworkUpdate_Flags dell'esempio ufficiale, perché è
            // quello che faceva il vecchio SetFlag.
            if (isAllowed)
            {
                using (var flags = baseLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                    flags.Set(BaseEntity.Flags.Locked, true);
            }
        }
#endif

        private object? CanUnlock(BasePlayer player, BaseLock baseLock)
        {
            var isAllowed = IsEntityOwner(baseLock, player) || (baseLock is CodeLock codeLock && codeLock.whitelistPlayers.Contains(player.userID.Get()));
            if (isAllowed)
            {
                using (var flags = baseLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                    flags.Set(BaseEntity.Flags.Locked, false);
            }

            return isAllowed;
        }

        private void CanChangeCode(BasePlayer player, CodeLock codeLock, string code, bool isGuest)
        {
            if (player == null || codeLock == null) return;
            NextFrame(() =>
            {
                if (!isGuest ? codeLock.code != code : codeLock.guestCode != code)
                {
                    return;
                }

                UpdateCodeLockAuthShareOrSettings(codeLock, null, player);
            });
        }

        // private void CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        // {
        //     if (player == null || baseLock == null || !baseLock.IsLocked()) return;
        //
        //     Puts($"########## CanUseLockedEntity: {player.displayName} - {baseLock.ShortPrefabName}");
        //     
        //     if (baseLock.IsLocked() && baseLock is CodeLock codeLock && !codeLock.whitelistPlayers.Contains(player.userID.Get()))
        //         UpdateCodeLockAuthList(codeLock, null, player);
        // }

        private object? CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (player == null || baseLock == null || !baseLock.IsLocked()) return null;
            //TODO da rimuovere
            // if (baseLock is CodeLock codeLock && !codeLock.whitelistPlayers.Contains(player.userID.Get()) && IsEntityOwner(baseLock, player))
            //     UpdateCodeLockAuthShareOrSettings(codeLock, null, player);

            if (CanByPassCodeLock(player)) return _true;

            return baseLock switch
            {
                CodeLock cl when cl.whitelistPlayers.Contains(player.userID.Get()) => _true,
                KeyLock kl when IsEntityOwner(kl, player) => _true,
                _ => null
            };
        }

        // Compatibility with plugin RemoverTool: disable refound BaseLock if it is an unlockable entity or if the player has [nolockrequired] permission
        private void OnNormalRemovedEntity(BasePlayer player, BaseEntity entity)
        {
            var baseLock = entity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;

            var userId = player.UserIDString;
            if (baseLock != null && permission.UserHasPermission(userId, PermissionUse) && permission.UserHasPermission(userId, PermissionAutoLockNoLockRequired))
            {
                var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID.Get());
                var lockCategory = LockCategoryByEntity(entity);
                if (!IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings)) return;

                switch (baseLock)
                {
                    case KeyLock:
                    {
                        if (entity != null && !entity.IsDestroyed)
                        {
                            NextFrame(() => { player.inventory.Take(null, KeyLockItemID, 1); });
                        }

                        break;
                    }
                    case CodeLock:
                    {
                        if (entity != null && !entity.IsDestroyed)
                        {
                            NextFrame(() => { player.inventory.Take(null, CodeLockItemID, 1); });
                        }

                        break;
                    }
                }
            }
        }

        // Previene il rimborso della serratura se è un entità non bloccabile o se il giocatore ha il permesso [nolockrequired]
        private object? CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if (player == null || baseLock == null || player.IsNpc) return null;

            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse) || !permission.UserHasPermission(userId, PermissionAutoLockNoLockRequired))
                return null;

            var parentEntity = baseLock.GetParentEntity();
            if (parentEntity == null) return null;

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID.Get());
            var lockCategory = LockCategoryByEntity(parentEntity);
            if (!IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings)) return null;

            baseLock.Kill();
            return true;
        }

        //TODO DA RIMUOVERE
        // private bool CanPickupEntity(BasePlayer player, BaseEntity entity)
        // {
        //     if (!CanUseEntity(player, entity)) return false;
        //     return true;
        // }

        private object? CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return null;

            if (entity is BaseCombatEntity { pickup: { enabled: false } })
                return null;

            if (!CanUseEntity(player, entity)) return false;
            return null;
        }

        private object? CanLootEntity(BasePlayer player, BaseEntity baseEntity)
        {
            if (player == null || baseEntity == null) return null;

            if (baseEntity is RidableHorse or ShopFront or StorageContainer)
            {
                return baseEntity switch
                {
                    RidableHorse ridableHorse => CanLootEntity(player, ridableHorse),
                    ShopFront shopFront => CanLootEntity(player, shopFront),
                    StorageContainer storageContainer => CanLootEntity(player, storageContainer),
                    _ => null
                };
            }

            if (!CanUseEntity(player, baseEntity)) return false;
            return null;
        }

        //TODO testare se puo essere rimosso
        private object? CanLootEntity(BasePlayer player, RidableHorse animal)
        {
            if (player == null || animal == null) return null;
            if (!CanUseEntity(player, animal)) return false;
            return null;
        }

        private object? CanLootEntity(BasePlayer player, ContainerIOEntity container)
        {
            if (player == null || container == null) return null;

            if (container != null && container is VendingMachine)
            {
                if (IsVendingMachineOpen(player, container)) return null;
            }

            if (!CanUseEntity(player, container)) return false;
            return null;
        }

        private object? CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (player == null || container == null) return null;

            if (container != null && container is DropBox)
            {
                if (IsDropBoxOpen(player, container)) return null;
            }

            if (!CanUseEntity(player, container)) return false;
            return null;
        }

        private object? CanLootEntity(BasePlayer player, ShopFront shopFront)
        {
            if (player == null || shopFront == null) return null;
            if (!CanUseEntity(player, shopFront)) return false;
            return null;
        }

        private void OnEntityDismounted(BaseMountable baseMountable, BasePlayer player)
        {
            if (baseMountable == null || player == null || !player.userID.IsSteamId() || player.IsNpc) return;

            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse)) return;

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID.Get());
            if (!autoLockPlayerSettings.VehicleLockAutoClosingOnDismountEnabled) return;

            var canUseEntity = CanUseEntity(player, baseMountable);
            if (!canUseEntity) return;

            NextFrame(() =>
            {
                var lockDefinition = GetLockDefinitionByEntity(baseMountable);
                if (lockDefinition == null) return;

                if (!lockDefinition.IsLocked())
                {
                    lockDefinition.Lock();

                    if (!IsInvisible(player) && !lockDefinition.IsKeyLock())
                        RunEffect(CodeLockLockEffect, player);

                    SendMessage(player, Lang("LockClosedAutomaticallyMessage", userId));
                }
            });
        }

        private void OnModularCarLockAdded(ModularCarCodeLock? modularCarCodeLock, string code, ulong userID)
        {
            if (modularCarCodeLock == null || modularCarCodeLock.owner == null || !userID.IsSteamId())
                return;

            var modularCar = modularCarCodeLock.owner;
            if (modularCar == null)
                return;

            modularCar.OwnerID = userID;
            EntityDeployed(null, modularCar, null, BasePlayer.FindByID(userID));
        }

        private void OnModularCarLockRemoved(ModularCarCodeLock? modularCarCodeLock)
        {
            if (modularCarCodeLock == null || modularCarCodeLock.owner == null ||
                !modularCarCodeLock.owner.OwnerID.IsSteamId())
                return;

            var modularCar = modularCarCodeLock.owner;
            if (modularCar == null)
                return;

            EntityRemoved(null, modularCar, BasePlayer.FindByID(modularCar.OwnerID));
            modularCar.OwnerID = 0;
        }

        private object? OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (player == null || oven == null) return null;
            if (!CanUseEntity(player, oven)) return false;
            return null;
        }

        private object? OnMixingTableToggle(MixingTable mixingTable, BasePlayer player)
        {
            if (player == null || mixingTable == null) return null;
            if (!CanUseEntity(player, mixingTable)) return false;
            return null;
        }

        private object? CanAdministerVending(BasePlayer player, VendingMachine machine)
        {
            if (player == null || machine == null) return null;
            if (!CanUseEntity(player, machine)) return false;
            return null;
        }

        private object? OnRotateVendingMachine(VendingMachine machine, BasePlayer player)
        {
            if (player == null || machine == null) return null;
            if (!CanUseEntity(player, machine)) return false;
            return null;
        }

        private object? CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            if (player == null || entity == null) return null;
            if (!CanUseEntity(player, entity)) return false;
            return null;
        }

        private object? OnPlayerWantsMount(BasePlayer player, BaseMountable entity)
        {
            if (player == null || entity == null) return null;
            if (!CanUseEntity(player, entity)) return false;
            return null;
        }

        private object? OnEngineStart(BaseVehicle vehicle, BasePlayer driver)
        {
            if (driver == null || vehicle == null) return null;
            if (!CanUseEntity(driver, vehicle)) return false;
            return null;
        }

        private object? OnVehiclePush(BaseVehicle vehicle, BasePlayer player)
        {
            if (player == null || vehicle == null) return null;

            if (_config.AllowPushVehiclesBlockedByCodeLockInBuildingPrivilege && player.IsBuildingAuthed())
                return null;

            if (_config.AllowPushVehiclesBlockedByCodeLock)
                return null;

            if (!CanUseEntity(player, vehicle)) return false;
            return null;
        }

        //Conduct Horse
        private object? OnHorseLead(RidableHorse animal, BasePlayer player)
        {
            if (player == null || animal == null) return null;

            if (_config.AllowPushVehiclesBlockedByCodeLockInBuildingPrivilege && player.IsBuildingAuthed())
                return null;

            if (_config.AllowPushVehiclesBlockedByCodeLock)
                return null;

            if (!CanUseEntity(player, animal)) return false;
            return null;
        }

        private object? OnHotAirBalloonToggle(HotAirBalloon balloon, BasePlayer player)
        {
            if (player == null || balloon == null) return null;
            if (!CanUseEntity(player, balloon)) return false;
            return null;
        }

        private object? CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (planner == null || planner.GetOwnerPlayer() == null || target.entity == null) return null;
            if (!CanUseEntity(planner.GetOwnerPlayer(), target.entity)) return false;
            return null;
        }

        private object? OnWorkbenchTechTreeUnlock(Workbench workbench, BasePlayer player)
        {
            if (player == null || workbench == null) return null;
            if (!CanUseEntity(player, workbench)) return false;
            return null;
        }

        private object? OnExperimentStart(Workbench workbench, BasePlayer player)
        {
            if (player == null || workbench == null) return null;
            if (!CanUseEntity(player, workbench)) return false;
            return null;
        }

        private object? OnPlayerDrink(BasePlayer player, LiquidContainer liquidContainer)
        {
            if (player == null || liquidContainer == null) return null;
            if (!CanUseEntity(player, liquidContainer)) return false;
            return null;
        }

        private object? OnSwitchToggle(IOEntity entity, BasePlayer player)
        {
            if (player == null || entity == null) return null;
            if (!CanUseEntity(player, entity)) return false;
            return null;
        }

        private object? OnSmartSwitchToggle(SmartSwitch smartSwitch, BasePlayer player)
        {
            if (player == null || smartSwitch == null) return null;
            if (!CanUseEntity(player, smartSwitch)) return false;
            return null;
        }

        private object? OnTimerSwitchToggle(TimerSwitch timerSwitch, BasePlayer player)
        {
            if (player == null || timerSwitch == null) return null;
            if (!CanUseEntity(player, timerSwitch)) return false;
            return null;
        }

        private object? CanTimerSwitchSetTime(CustomTimerSwitch customTimerSwitch, BasePlayer player)
        {
            if (player == null || customTimerSwitch == null) return null;
            if (!CanUseEntity(player, customTimerSwitch)) return false;
            return null;
        }

        private object? OnButtonPress(PressButton button, BasePlayer player)
        {
            if (player == null || button == null) return null;
            if (!CanUseEntity(player, button)) return false;
            return null;
        }

        private object? OnFogMachineToggle(FogMachine fogMachine, BasePlayer player)
        {
            if (player == null || fogMachine == null) return null;
            if (!CanUseEntity(player, fogMachine)) return false;
            return null;
        }

        private object? OnStrobeLightToggle(StrobeLight strobeLight, BasePlayer player)
        {
            if (player == null || strobeLight == null) return null;
            if (!CanUseEntity(player, strobeLight)) return false;
            return null;
        }

        private object? OnStrobeSpeedChange(StrobeLight strobeLight, BasePlayer player)
        {
            if (player == null || strobeLight == null) return null;
            if (!CanUseEntity(player, strobeLight)) return false;
            return null;
        }

        private object? OnAudioVisualisationServerUpdateSettings(AudioVisualisationEntity audioVisualisationEntity, BasePlayer player)
        {
            if (player == null || audioVisualisationEntity == null) return null;
            if (!CanUseEntity(player, audioVisualisationEntity)) return false;
            return null;
        }

        // ######################################## NAVAL DEEP SEA #######################################

        private object? BoatBuildingStationCanEnterEditMode(BoatBuildingStation boatBuildingStation, BasePlayer player)
        {
            return CanPlayerUseEntity(boatBuildingStation, player);
        }

        private object? BoatBuildingStationCanClearArea(BoatBuildingStation boatBuildingStation, BasePlayer player)
        {
            return CanPlayerUseEntity(boatBuildingStation, player);
        }

        private object? BoatBuildingStationFinishBuilding(BoatBuildingStation boatBuildingStation, BasePlayer player)
        {
            return CanPlayerUseEntity(boatBuildingStation, player);
        }

        // private object? CanEditPlayerBoat(PlayerBoat playerBoat, BasePlayer player)
        // {
        //     Puts($"########### CanEditPlayerBoat: {CanPlayerUseEntity(player, playerBoat)}");
        //     return CanPlayerUseEntity(player, playerBoat);
        // }

        private object? CanRotateSail(Sail sail, BasePlayer player)
        {
            return CanPlayerUseEntity(sail, player);
        }

        private object? CanRaiseSail(Sail sail, BasePlayer player)
        {
            return CanPlayerUseEntity(sail, player);
        }

        private object? CanLowerSail(Sail sail, BasePlayer player)
        {
            return CanPlayerUseEntity(sail, player);
        }

        private object? OnEngineStart(SmallEngine smallEngine, BasePlayer player)
        {
            return CanPlayerUseEntity(smallEngine, player);
        }

        private object? OnEngineStop(SmallEngine smallEngine, BasePlayer player)
        {
            return CanPlayerUseEntity(smallEngine, player);
        }

        private object? OnEngineReverse(SmallEngine smallEngine, BasePlayer player)
        {
            return CanPlayerUseEntity(smallEngine, player);
        }

        private object? AnchorCanBeRaised(Anchor anchor, BasePlayer player)
        {
            return CanPlayerUseEntity(anchor, player);
        }

        private object? AnchorCanBeLowered(Anchor anchor, BasePlayer player)
        {
            return CanPlayerUseEntity(anchor, player);
        }

        // Blocca lo sparo con il cannone, ma non verrà implementato per non aggravare sulle performance, in quanto ce già il controllo se il player può usare o meno il cannone
        // private object? CannonCanRequestLightFuse(Cannon cannon, BasePlayer player)
        // {
        //     return CanPlayerUseEntity(cannon, player);
        // }

        // ###############################################################################################


        //TODO DA RIMUOVERE
        // private bool CanUpdateSign(BasePlayer player, Signage sign)
        // {
        //     if (player == null || sign == null) return true;
        //     return CanUseEntity(player, sign);
        // }

        private object? CanUpdateSign(BasePlayer player, Signage sign)
        {
            if (player == null || sign == null) return null;
            if (!CanUseEntity(player, sign)) return false;
            return null;
        }

        private object? CanUpdateSign(BasePlayer player, PhotoFrame photoFrame)
        {
            if (player == null || photoFrame == null) return null;
            if (!CanUseEntity(player, photoFrame)) return false;
            return null;
        }

        private object? CanShutterFrameToggleShutter(BasePlayer player, ShutterFrame shutterFrame)
        {
            if (player == null || shutterFrame == null) return null;
            if (!CanUseEntity(player, shutterFrame)) return false;
            return null;
        }

        private object? CanOrnateFrameConfigureFrame(BasePlayer player, OrnateFrame ornateFrame)
        {
            if (player == null || ornateFrame == null) return null;
            if (!CanUseEntity(player, ornateFrame)) return false;
            return null;
        }

        //TODO DA RIMUOVERE
        // private object? OnSignageCanUpdateSign(Signage signage, BasePlayer player)
        // {
        //     Puts("########## OnSignageCanUpdateSign ##########");
        //     if (player == null || signage == null) return null;
        //     if (!CanUseEntity(player, signage)) return false;
        //     return null;
        // }

        private object? OnSignageLockUnlockSign(Signage signage, BasePlayer player)
        {
            if (player == null || signage == null) return null;
            if (!CanUseEntity(player, signage)) return false;
            return null;
        }

        private object? OnNeonSignSetAnimationSpeed(NeonSign neonSign, BasePlayer player)
        {
            if (player == null || neonSign == null) return null;
            if (!CanUseEntity(player, neonSign)) return false;
            return null;
        }

        private object? OnUpdateNeonColors(NeonSign neonSign, BasePlayer player)
        {
            if (player == null || neonSign == null) return null;
            if (!CanUseEntity(player, neonSign)) return false;
            return null;
        }

        private object? OnSearchLightUseLight(SearchLight searchLight, BasePlayer player)
        {
            if (player == null || searchLight == null) return null;
            if (!CanUseEntity(player, searchLight)) return false;
            return null;
        }

        private object? OnPoweredRemoteControlEntityCanChangeID(BasePlayer player,
            PoweredRemoteControlEntity poweredRemoteControlEntity)
        {
            if (player == null || poweredRemoteControlEntity.GetEnt() == null) return null;
            if (!CanUseEntity(player, poweredRemoteControlEntity.GetEnt())) return false;
            return null;
        }

        private object? OnPoweredRemoteControlEntityCanControl(ulong playerID,
            PoweredRemoteControlEntity poweredRemoteControlEntity)
        {
            if (!playerID.IsSteamId() || poweredRemoteControlEntity == null ||
                poweredRemoteControlEntity.GetEnt() == null) return null;
            if (!CanUseEntity(BasePlayer.Find(playerID.ToString()), poweredRemoteControlEntity.GetEnt())) return false;
            return null;
        }

        private object? OnRFBroadcasterCanChangeFrequency(RFBroadcaster rfBroadcaster, BasePlayer player)
        {
            if (player == null || rfBroadcaster == null) return null;
            if (!CanUseEntity(player, rfBroadcaster)) return false;
            return null;
        }

        private object? OnRFReceiverCanChangeFrequency(RFReceiver rfBroadcaster, BasePlayer player)
        {
            if (player == null || rfBroadcaster == null) return null;
            if (!CanUseEntity(player, rfBroadcaster)) return false;
            return null;
        }

        // private object OnRfListenerAdd(IRFObject obj, int frequency)
        // {
        //     Puts("OnRfListenerAdd works!");
        //     return null;
        // }
        //
        // private object OnRfBroadcasterAdd(IRFObject obj, int frequency)
        // {
        //     Puts("OnRfBroadcasterAdd works!");
        //     return null;
        // }

        //FARM: Called when a player is trying to take a cutting (clone) of a GrowableEntity
        private object? CanTakeCutting(BasePlayer player, GrowableEntity entity)
        {
            if (player == null || entity == null) return null;
            if (!CanUseEntity(player, entity)) return false;
            return null;
        }

        //FARM: Called when a player is trying to harvest a dying growable entity
        private object? OnRemoveDying(GrowableEntity plant, BasePlayer player)
        {
            if (player == null || plant == null) return null;
            if (!CanUseEntity(player, plant)) return false;
            return null;
        }

        //FARM: Called when the player gathers a growable entity
        private object? OnGrowableGather(GrowableEntity plant, BasePlayer player)
        {
            if (player == null || plant == null) return null;
            if (!CanUseEntity(player, plant)) return true;
            return null;
        }

        //TODO DA RIMUOVERE
        //FARM: Called when the player gathers a growable entity
        // private object? CanPickFruit(GrowableEntity plant, BasePlayer player, bool eat)
        // {
        //     if (player == null || plant == null) return null;
        //     if (!CanUseEntity(player, plant)) return false;
        //     return null;
        // }

        private object? OnSamSiteModeToggle(SamSite samSite, BasePlayer player, bool defenderMode)
        {
            if (player == null || samSite == null) return null;
            if (!CanUseEntity(player, samSite)) return false;
            return null;
        }

        private object? OnTurretAuthorize(AutoTurret autoTurret, BasePlayer player)
        {
            if (player == null || autoTurret == null) return null;
            if (!CanUseEntity(player, autoTurret)) return false;
            return null;
        }

        private object? OnTurretRotate(AutoTurret autoTurret, BasePlayer player)
        {
            if (player == null || autoTurret == null) return null;
            if (!CanUseEntity(player, autoTurret)) return false;
            return null;
        }

        private object? OnAutoTurretCanControl(ulong playerID, AutoTurret autoTurret)
        {
            if (!playerID.IsSteamId() || autoTurret == null || autoTurret.GetEnt() == null) return null;
            if (!CanUseEntity(BasePlayer.Find(playerID.ToString()), autoTurret.GetEnt())) return false;
            return null;
        }

        //Electricity and Industrial
        private object? OnWireConnect(BasePlayer player, IOEntity entity1, int inputs, IOEntity entity2, int outputs)
        {
            if (player == null || (entity1 == null && entity2 == null)) return null;
            if (!CanUseEntity(player, entity1, entity2)) return false;
            return null;
        }

        //Electricity and Industrial
        private object? OnWireClear(BasePlayer player, IOEntity entity1, int connected, IOEntity entity2, bool flag)
        {
            if (player == null || (entity1 == null && entity2 == null)) return null;
            if (!CanUseEntity(player, entity1, entity2)) return false;
            return null;
        }

        private object? OnConveyorFiltersChange(IndustrialConveyor industrialConveyor, BasePlayer player,
            ProtoBuf.IndustrialConveyor.ItemFilterList itemFilterList)
        {
            if (player == null || industrialConveyor == null) return null;
            if (!CanUseEntity(player, industrialConveyor)) return false;
            return null;
        }

        private object? OnIndustrialCrafterSwitchToggle(IndustrialCrafter industrialCrafter, BasePlayer player)
        {
            if (player == null || industrialCrafter == null) return null;
            if (!CanUseEntity(player, industrialCrafter)) return false;
            return null;
        }

        private object? OnBoomboxToggle(BoomBox boomBox, BasePlayer player)
        {
            if (player == null || boomBox == null || boomBox.BaseEntity == null) return null;
            if (!CanUseEntity(player, boomBox.BaseEntity)) return false;
            return null;
        }

        private object? OnBoomboxStationUpdate(BoomBox boomBox, string url, BasePlayer player)
        {
            if (player == null || boomBox == null || boomBox.BaseEntity == null) return null;
            if (!CanUseEntity(player, boomBox.BaseEntity)) return false;
            return null;
        }

        private object? OnElevatorButtonPress(ElevatorLift lift, BasePlayer player, Elevator.Direction direction,
            bool toTopOrBottom)
        {
            if (player == null || lift == null) return null;
            if (!CanUseEntity(player, lift)) return false;
            return null;
        }

        private void OnQuarryToggled(MiningQuarry quarry, BasePlayer player)
        {
            if (player == null || quarry == null || quarry.GetEntity() == null ||
                !quarry.GetEntity().OwnerID.IsSteamId()) return;

            if (!CanUseEntity(player, quarry))
                quarry.EngineSwitch(!quarry.IsEngineOn());
        }

        private object? OnRackedWeaponMount(Item slot, BasePlayer player, WeaponRack weaponRack)
        {
            if (!CanUseEntity(player, weaponRack)) return false;
            return null;
        }

        private object? OnRackedWeaponTake(Item slot, BasePlayer player, WeaponRack weaponRack)
        {
            if (!CanUseEntity(player, weaponRack)) return true;
            return null;
        }

        private object? OnRackedWeaponSwap(Item item, WeaponRackSlot weaponAtIndex, BasePlayer player,
            WeaponRack weaponRack)
        {
            if (!CanUseEntity(player, weaponRack)) return false;
            return null;
        }

        private object? OnRackedWeaponLoad(Item slot, ItemDefinition itemdef, BasePlayer player, WeaponRack weaponRack)
        {
            if (!CanUseEntity(player, weaponRack)) return false;
            return null;
        }

        private object? OnRackedWeaponUnload(Item slot, BasePlayer player, WeaponRack weaponRack)
        {
            if (!CanUseEntity(player, weaponRack)) return false;
            return null;
        }

        //######################################## MEDIEVAL ENTITIES ########################################
        private object? CanBatteringRamOpenDoor(BatteringRam batteringRam, BasePlayer player)
        {
            if (player == null || batteringRam == null) return null;
            if (!CanUseEntity(player, batteringRam)) return false;
            return null;
        }

        private object? CanBatteringRamCloseDoor(BatteringRam batteringRam, BasePlayer player)
        {
            if (player == null || batteringRam == null) return null;
            if (!CanUseEntity(player, batteringRam)) return false;
            return null;
        }

        private object? CanCatapultReloadStart(Catapult catapult, BasePlayer player)
        {
            if (player == null || catapult == null) return null;
            if (!CanUseEntity(player, catapult)) return false;
            return null;
        }

        private object? CanSiegeTowerOpenDoor(Door door, BasePlayer player)
        {
            if (player == null || door == null) return null;
            if (!CanUseEntity(player, door)) return false;
            return null;
        }

        private object? CanSiegeTowerCloseDoor(Door door, BasePlayer player)
        {
            if (player == null || door == null) return null;
            if (!CanUseEntity(player, door)) return false;
            return null;
        }

        private object? OnSiegeWeaponFire(BaseSiegeWeapon baseSiegeWeapon, BasePlayer player)
        {
            if (player == null || baseSiegeWeapon == null) return null;
            if (!CanUseEntity(player, baseSiegeWeapon)) return false;
            return null;
        }

        private object? CanBaseSiegeWeaponStartPulling(BaseSiegeWeapon baseSiegeWeapon, BasePlayer player)
        {
            if (player == null || baseSiegeWeapon == null) return null;
            if (!CanUseEntity(player, baseSiegeWeapon)) return false;
            return null;
        }
        //###################################################################################################

        #endregion

        #region Core

        private void SendMessage(BasePlayer player, string message)
        {
            if (player == null)
                return;

            // Verifica che il player sia ancora connesso prima di inviare il messaggio
            if (player.IsDestroyed || !player.IsConnected) return;

            var prefix = !string.IsNullOrWhiteSpace(_config.ChatPrefix) ? _config.ChatPrefix : Name;
            prefix = $"<color=#62de32>{prefix}\n</color>";
            player.ChatMessage($"{prefix}{message}");
        }

        private void PlayerShowToast(ulong userId, string message, bool isError = false)
        {
            if (!userId.IsSteamId()) return;
            var player = BasePlayer.FindByID(userId);
            if (player == null) return;

            PlayerShowToast(player, message, isError);
        }

        private void PlayerShowToast(BasePlayer player, string message, bool isError = false)
        {
            if (player == null)
                return;

            // Verifica che il player sia ancora connesso prima di inviare il toast
            if (player.IsDestroyed || !player.IsConnected) return;

            NextTick(() => player.ShowToast(isError ? GameTip.Styles.Red_Normal : GameTip.Styles.Blue_Long, message, false));
        }

        private void DestroyMonoComponent(BasePlayer player)
        {
            if (player == null) return;
            if (player.HasComponent<LockerBehaviour>())
                Destroy(player.GetComponent<LockerBehaviour>());
        }

        private static bool IsNullOrEmpty<T>(ICollection<T>? collection)
        {
            return collection == null || collection.Count == 0;
        }

        //TODO DA RIMUOVERE
        // private IEnumerator HandleStorageContainerLockableOnStartup()
        // {
        //     if (_config.LockConfiguration.Deployables.IsNullOrEmpty()) yield break;
        //
        //     var allConfiguredStorageContainer = BaseNetworkable.serverEntities
        //         .Where(e => e != null && !string.IsNullOrEmpty(e.PrefabName) &&
        //                     _config.LockConfiguration.Deployables.Any(d =>
        //                         e.PrefabName.Equals(d.PrefabName, StringComparison.OrdinalIgnoreCase)))
        //         .Where(e => e is StorageContainer storage && storage.OwnerID.IsSteamId())
        //         .Cast<StorageContainer>()
        //         .ToList();
        //
        //     var count = 0;
        //     foreach (var storageContainer in allConfiguredStorageContainer)
        //     {
        //         if (_allowedItemsLock.Any(ai =>
        //                 ai.PrefabName.Equals(storageContainer.PrefabName, StringComparison.OrdinalIgnoreCase)))
        //         {
        //             storageContainer.isLockable = true;
        //         }
        //         else
        //         {
        //             storageContainer.isLockable = false;
        //         }
        //
        //         storageContainer.SendNetworkUpdate();
        //         storageContainer.SendNetworkUpdateImmediate();
        //
        //         if (++count % CoroutineIteratorsPerFrame == 0)
        //             yield return CoroutineEx.waitForEndOfFrame;
        //     }
        // }

        //TODO DA RIMUOVERE
        private IEnumerator HandleStorageContainerLockableOnunload()
        {
            if (_config.LockConfiguration.Deployables.IsNullOrEmpty()) yield break;

            var allConfiguredStorageContainer = BaseNetworkable.serverEntities
                .Where(e => e != null && !string.IsNullOrEmpty(e.PrefabName) &&
                            _config.LockConfiguration.Deployables.Any(d =>
                                e.PrefabName.Equals(d.PrefabName, StringComparison.OrdinalIgnoreCase)))
                .Where(e => e is StorageContainer storage && storage.OwnerID.IsSteamId())
                .Cast<StorageContainer>()
                .ToList();

            var count = 0;
            foreach (var storageContainer in allConfiguredStorageContainer)
            {
                if (_enabledLockableEntityConfigs.Any(ai =>
                        ai.PrefabName.Equals(storageContainer.PrefabName, StringComparison.OrdinalIgnoreCase)))
                {
                    storageContainer.isLockable = false;
                }

                storageContainer.SendNetworkUpdate();
                storageContainer.SendNetworkUpdateImmediate();

                if (++count % CoroutineIteratorsPerFrame == 0)
                    yield return CoroutineEx.waitForEndOfFrame;
            }
        }

        //Verifica/Sistema le autorizzazioni sui codelock di tutti i player
        private IEnumerator UpdateAllPlayersCodeLockAuth()
        {
            var count = 0;
            var playerList = BasePlayer.activePlayerList;
            foreach (var player in playerList)
            {
                if (player == null || player.IsNpc) continue;

                var lockEntityList = BaseNetworkable.serverEntities
                    .Where(se =>
                        se is CodeLock cl &&
                        cl.OwnerID.IsSteamId() &&
                        cl.OwnerID.Equals(player.userID))
                    .Cast<BaseEntity>()
                    .ToList();

                lockEntityList.AddRange(BaseNetworkable.serverEntities
                    .Where(se =>
                        se is ModularCar be && (
                            be.ShortPrefabName.Contains(ModuleCarPartialShortPrefabName) ||
                            be.PrefabName.StartsWith(CarChassisModuleCarPartialPrefabName, StringComparison.OrdinalIgnoreCase)
                        ) &&
                        be.OwnerID.IsSteamId() &&
                        be.OwnerID.Equals(player.userID))
                    .Cast<BaseEntity>()
                    .ToList());

                if (count % CoroutineIteratorsPerFrame == 0)
                    yield return ServerMgr.Instance.StartCoroutine(UpdateAllCodeLockAuthShareOrSettings(lockEntityList, player));
                count++;
            }
        }

        /// <summary>
        /// Coroutine eseguita all'avvio del plugin per correggere l'ownership delle
        /// <see cref="LegacyShelterDoor"/> orfane presenti nel mondo.
        /// </summary>
        /// <remarks>
        /// Per ogni <see cref="LegacyShelterDoor"/> priva di owner valido, delega il
        /// ripristino a <see cref="EnsureDoorOwnership"/>, che tenta di ereditare
        /// l'<see cref="BaseEntity.OwnerID"/> dal parent <see cref="LegacyShelter"/>.
        /// <para>
        /// L'iterazione viene suddivisa in frame tramite
        /// <see cref="CoroutineEx.waitForEndOfFrame"/> ogni
        /// <see cref="CoroutineIteratorsPerFrame"/> porte processate, per evitare
        /// spike sul thread principale.
        /// </para>
        /// <para>
        /// Al termine, emette un <see cref="PrintWarning"/> solo se almeno una porta
        /// è stata corretta, per non generare rumore nei log ad ogni avvio pulito.
        /// </para>
        /// </remarks>
        private IEnumerator HandleDoorOwnershipOnStartup()
        {
            var fixedCount = 0;
            var count = 0;

            foreach (var door in BaseNetworkable.serverEntities.OfType<LegacyShelterDoor>())
            {
                if (!door.OwnerID.IsSteamId() && EnsureDoorOwnership(door))
                    fixedCount++;

                if (++count % CoroutineIteratorsPerFrame == 0)
                    yield return CoroutineEx.waitForEndOfFrame;
            }

            if (fixedCount > 0)
                PrintWarning($"[HandleDoorOwnershipOnStartup] Fixed ownership for {fixedCount}/{count} LegacyShelterDoors.");
        }

        /// <summary>
        /// Coroutine che avvia i timer di chiusura automatica per tutte le
        /// <see cref="Door"/> nel mondo all'avvio del plugin, delegando
        /// l'aggiornamento a <see cref="UpdateDoorCloserTimers"/>.
        /// </summary>
        /// <remarks>
        /// Viene eseguita in sequenza dopo <see cref="HandleDoorOwnershipOnStartup"/>
        /// tramite <see cref="HandleStartupCoroutinesInSequence"/>, per garantire che
        /// l'ownership delle door sia già risolta prima di registrare i timer.
        /// <para>
        /// Se non esistono dati di chiusura automatica salvati, la coroutine termina
        /// immediatamente senza interrogare le entità del server.
        /// </para>
        /// <para>
        /// Le door vengono raggruppate per <c>OwnerID</c> tramite
        /// <see cref="GetDoorEntitiesGroupedByOwner"/> — permette di processare
        /// tutte le door di un player in una singola chiamata a
        /// <see cref="UpdateDoorCloserTimers"/> invece di una per ogni door.
        /// </para>
        /// <para>
        /// Ogni gruppo viene atteso con <c>yield return</c> prima di avviare il
        /// successivo, per evitare la creazione concorrente di troppe coroutine.
        /// La coroutine si suddivide inoltre in batch tramite
        /// <see cref="CoroutineIteratorsPerFrame"/> per evitare picchi di CPU
        /// durante l'inizializzazione del server.
        /// </para>
        /// <para>
        /// Nessuna notifica viene inviata ai player — <c>player</c> viene passato
        /// come <c>null</c> a <see cref="UpdateDoorCloserTimers"/> perché
        /// l'aggiornamento è automatico e non richiesto dall'utente.
        /// </para>
        /// </remarks>
        private IEnumerator HandleUpdateDoorCloserDelayTimeOnStartup()
        {
            if (_data.DoorCloserEntityData.IsNullOrEmpty()) yield break;

            var count = 0;
            var doorGroups = GetDoorEntitiesGroupedByOwner();

            if (doorGroups.Count == 0) yield break;

            foreach (var group in doorGroups)
            {
                yield return ServerMgr.Instance.StartCoroutine(UpdateDoorCloserTimers(null, group.ToList()));

                if (++count % CoroutineIteratorsPerFrame == 0)
                    yield return CoroutineEx.waitForEndOfFrame;
            }
        }

        /// <summary>
        /// Esegue il check di prossimità su un sottoinsieme di player per tick, distribuendo
        /// il carico uniformemente nel tempo tramite <see cref="_proximityCheckOffset"/>.
        /// Per ogni player controlla se è entrato o uscito dal raggio di una <see cref="Door"/>
        /// tracciata in <see cref="_doorCloserTimers"/>, invocando i relativi metodi:
        /// <list type="bullet">
        ///   <item><see cref="OnPlayerEnterDoorRadius"/> — player entrato nel raggio.</item>
        ///   <item><see cref="OnPlayerLeaveDoorRadius"/> — player uscito dal raggio.</item>
        /// </list>
        /// I due metodi vengono invocati solo al cambio di stato (entrata/uscita), non a ogni tick,
        /// sfruttando il valore di ritorno di <see cref="HashSet{T}.Add"/> e <see cref="HashSet{T}.Remove"/>
        /// per rilevare il cambio in una singola operazione senza variabili ausiliarie.
        /// <para>
        /// I player AFK da più di <see cref="ProximityAfkThreshold"/> secondi vengono saltati —
        /// se erano tracciati in <see cref="_nearTrackedDoor"/>, viene notificata l'uscita dal raggio
        /// per mantenere lo stato coerente.
        /// </para>
        /// <para>
        /// I metodi vengono chiamati direttamente invece di usare <c>Interface.CallHook</c> —
        /// gli hook sono a uso interno e la chiamata diretta elimina l'overhead di reflection
        /// e lookup di Oxide. Le chiamate <c>Interface.CallHook</c> sono mantenute in commento
        /// per una eventuale esposizione futura ad altri plugin.
        /// </para>
        /// <para>
        /// <see cref="BasePlayer.activePlayerList"/> è un <c>ListHashSet</c> che non supporta
        /// l'accesso per indice — viene copiato in una <see cref="List{T}"/> tramite
        /// <see cref="Facepunch.Pool"/> per evitare allocazioni GC a ogni tick.
        /// La lista viene sempre rilasciata nel blocco <c>finally</c> anche in caso di eccezione.
        /// </para>
        /// </summary>
        private void DoorProximityCheckBatch()
        {
            var players = Pool.Get<List<BasePlayer>>();

            try
            {
                players.AddRange(BasePlayer.activePlayerList);

                var total = players.Count;
                if (total == 0)
                    return;

                if (_proximityCheckOffset >= total)
                    _proximityCheckOffset = 0;

                var count = Mathf.Min(ProximityPlayersPerTick, total);

                for (var i = 0; i < count; i++)
                {
                    var player = players[(_proximityCheckOffset + i) % total];
                    if (player == null || !player.IsConnected || player.IsNpc) continue;

                    var userId = player.userID;

                    // Salta i player AFK — se erano vicini a una porta notifica l'uscita
                    if (player.IdleTime > ProximityAfkThreshold)
                    {
                        if (_nearTrackedDoor.Remove(userId))
                            OnPlayerLeaveDoorRadius(player);
                        continue;
                    }

                    var isNear = IsNearTrackedDoor(player, out var door);

                    if (isNear)
                    {
                        // Add ritorna true solo se il player non era già nell'HashSet → cambio di stato
                        if (_nearTrackedDoor.Add(userId))
                        {
                            OnPlayerEnterDoorRadius(player, door);
                            // Per possibili utilizzi futuri
                            // Interface.CallHook(HookOnPlayerEnterDoorRadius, player, door);
                        }
                    }
                    else
                    {
                        // Remove ritorna true solo se il player era nell'HashSet → cambio di stato
                        if (_nearTrackedDoor.Remove(userId))
                        {
                            OnPlayerLeaveDoorRadius(player);
                            // Per possibili utilizzi futuri
                            // Interface.CallHook(HookOnPlayerLeaveDoorRadius, player);
                        }
                    }
                }

                _proximityCheckOffset = (_proximityCheckOffset + ProximityPlayersPerTick) % total;
            }
            finally
            {
                Pool.FreeUnmanaged(ref players);
            }
        }

        private IEnumerator RestoreAllLockScaleSizes()
        {
            if (_data.LockEntityData.IsNullOrEmpty()) yield break;

            var sw = new Stopwatch();
            sw.Start();

            var scaledLockerMap = _data.LockEntityData.Values.SelectMany(data => data)
                .Where(led => !Mathf.Approximately(led.ScaleSize, DefaultScaleSize))
                .ToDictionary(led => led.NetId);

            var count = 0;
            if (scaledLockerMap.Count == 0)
            {
                Puts($"########## Restored {count} lock scale sizes in {sw.Elapsed.TotalSeconds:N2} seconds ##########");
                yield break;
            }

            foreach (var se in BaseNetworkable.serverEntities)
            {
                if (se == null || se.IsDestroyed || se.net == null || !se.net.ID.IsValid) continue;
                if (se is not BaseLock bl) continue;
                if (!bl.OwnerID.IsSteamId()) continue;
                if (!scaledLockerMap.TryGetValue(bl.net.ID.Value, out var lockerEntityData)) continue;

                ChangeEntityScaleSize(bl, lockerEntityData.ScaleSize);

                if (++count % CoroutineIteratorsPerFrame == 0)
                    yield return CoroutineEx.waitForEndOfFrame;
            }

            sw.Stop();
            Puts($"########## Restored {count} lock scale sizes in {sw.Elapsed.TotalSeconds:N2} seconds ##########");
        }

        private bool CanUseEntity(BasePlayer player, BaseEntity? baseEntity1, BaseEntity? baseEntity2)
        {
            if (player == null || (baseEntity1 == null && baseEntity2 == null)) return true;

            var targetEntity = null as BaseEntity;
            if (baseEntity1 != null && IsAllowedEntity(baseEntity1))
                targetEntity = baseEntity1;
            else if (baseEntity2 != null && IsAllowedEntity(baseEntity2))
                targetEntity = baseEntity2;

            if (targetEntity == null) return true;

            var targetIndustrialStorageAdaptor =
                baseEntity1 as IndustrialStorageAdaptor ?? baseEntity2 as IndustrialStorageAdaptor;
            if (targetIndustrialStorageAdaptor != null && targetIndustrialStorageAdaptor.GetParentEntity() != null &&
                IsAllowedEntity(targetIndustrialStorageAdaptor.GetParentEntity()))
            {
                return CanUseEntity(player, targetEntity) &&
                       CanUseEntity(player, targetIndustrialStorageAdaptor.GetParentEntity());
            }

            if (baseEntity1 != null && baseEntity2 != null && IsAllowedEntity(baseEntity1) &&
                IsAllowedEntity(baseEntity2))
            {
                return CanUseEntity(player, baseEntity1) && CanUseEntity(player, baseEntity2);
            }

            return CanUseEntity(player, targetEntity);
        }

        private object? CanPlayerUseEntity(BaseEntity? baseEntity, BasePlayer? player)
        {
            if (baseEntity == null || player == null) return null;
            return CanUseEntity(player, baseEntity) ? null : false;
        }

        private bool CanUseEntity(BasePlayer player, BaseEntity? baseEntity)
        {
            if (player == null || baseEntity == null || string.IsNullOrEmpty(baseEntity.PrefabName)) return true;

            var entityToCheck = baseEntity;

            var parentEntity = baseEntity.GetParentEntity();
            if (parentEntity != null && IsAllowedEntity(parentEntity.PrefabName))
            {
                if (entityToCheck is LiquidContainer)
                {
                    entityToCheck = parentEntity;
                }

                if (entityToCheck is GrowableEntity)
                {
                    entityToCheck = parentEntity;
                }
            }

            var lockDefinition = GetLockDefinitionByEntity(entityToCheck);

            if (lockDefinition == null || (lockDefinition.ModularCarCodeLock == null && lockDefinition.BaseLock == null) ||
                lockDefinition.BaseEntity == null) return true;

            if (!lockDefinition.IsLocked()) return true;

            //TODO da testare e poi rimuovere
            // if (!permission.UserHasPermission(player.UserIDString, PermissionUse) ||
            //     // !IsAllowedEntity(entityToCheck)) return true;
            //     !IsAllowedEntity(lockDefinition.BaseEntity)) return true;

            if (CanByPassCodeLock(player))
            {
                lockDefinition.Unlock();
                NextFrame(lockDefinition.Lock);
                return true;
            }

            if (lockDefinition.GetOwnerID() == null)
            {
                return true;
            }

            //If the player is authorized to the turret, it bypasses the Code Lock
            if (entityToCheck is AutoTurret turret)
            {
                if (turret.IsAuthed(player))
                    return true;
            }

            if (entityToCheck is ShopFront shopFront)
            {
                if (IsShopFrontCustomerPosOpen(player, shopFront))
                {
                    lockDefinition.Unlock();
                    NextFrame(() => lockDefinition.Lock());
                    return true;
                }
            }

            var codeLockEntity = lockDefinition.GetCodeLockEntity();
            if (lockDefinition.IsLocked() && codeLockEntity != null &&
                (codeLockEntity is not CodeLock || !lockDefinition.GetCodeLockWhitelistPlayers().Contains(player.userID.Get())) &&
                !IsEntityOwner(codeLockEntity, player))
            {
                //CodeLock usage blocked
                if (!IsInvisible(player) && !lockDefinition.IsKeyLock())
                {
                    RunEffect(CodeLockDeniedEffect, player);
                    PrintToChat(player, Lang("NoCodeLockAuth", player.UserIDString));
                }

                return false;
            }

            //CodeLock usage allowed
            if (!IsInvisible(player) && !lockDefinition.IsKeyLock())
                RunEffect(CodeLockUnlockEffect, player);
            return true;
        }

        // TODO da modificare per utilizzare un dizionario con TryGetValue
        private LockableEntityConfig? GetLockableEntityConfigForEntity(BaseEntity? baseEntity)
        {
            if (baseEntity == null || string.IsNullOrEmpty(baseEntity.PrefabName)) return null;
            return _allLockableEntityConfigs.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.PrefabName) && a.PrefabName.Equals(baseEntity.PrefabName, StringComparison.OrdinalIgnoreCase));
        }

        // TODO da modificare per utilizzare un dizionario con TryGetValue
        private LockableEntityConfig? GetLockableEntityConfigForAllowedEntity(BaseEntity? baseEntity)
        {
            if (baseEntity == null || string.IsNullOrEmpty(baseEntity.PrefabName)) return null;
            return _enabledLockableEntityConfigs.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.PrefabName) && a.PrefabName.Equals(baseEntity.PrefabName, StringComparison.OrdinalIgnoreCase));
        }

        private List<LockableEntityConfig> PlayerGetAllowedLockableEntityConfigList(BasePlayer player)
        {
            var playerAllowedLockableEntityList = new List<LockableEntityConfig>();
            if (player == null || player.IsNpc) return playerAllowedLockableEntityList;

            playerAllowedLockableEntityList.AddRange(_enabledLockableEntityConfigs
                .Where(ai => ai.RequiredPermission.IsNullOrEmpty())
                .ToList());

            var allowedLockableEntityRequirePermission = _enabledLockableEntityConfigs
                .Where(ai => !ai.RequiredPermission.IsNullOrEmpty())
                .ToList();

            if (!allowedLockableEntityRequirePermission.IsNullOrEmpty())
            {
                foreach (var lockableEntityRequirePermission in allowedLockableEntityRequirePermission)
                {
                    foreach (var requirePermission in lockableEntityRequirePermission.RequiredPermission)
                    {
                        var perm = GetValidPermission(requirePermission);
                        if (string.IsNullOrWhiteSpace(perm) || permission.UserHasPermission(player.UserIDString, perm))
                        {
                            if (playerAllowedLockableEntityList.Any(pai => pai.PrefabName.Equals(lockableEntityRequirePermission.PrefabName, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            playerAllowedLockableEntityList.Add(lockableEntityRequirePermission);
                        }
                    }
                }
            }

            return playerAllowedLockableEntityList;
        }

        private List<LockCategory> PlayerGetAllowedLockCategory(BasePlayer player)
        {
            var lockCategoryAllowedList = new List<LockCategory>();
            if (player == null || player.IsNpc) return lockCategoryAllowedList;

            var lockableEntityAllowed = PlayerGetAllowedLockableEntityConfigList(player);
            if (lockableEntityAllowed.IsNullOrEmpty()) return lockCategoryAllowedList;

            var seenCategories = new HashSet<LockCategory>();
            foreach (var lockableEntityConfig in lockableEntityAllowed)
            {
                if (seenCategories.Add(lockableEntityConfig.LockCategory))
                    lockCategoryAllowedList.Add(lockableEntityConfig.LockCategory);
            }

            return lockCategoryAllowedList;
        }

        private bool IsAllowedEntity(BaseEntity baseEntity)
        {
            if (baseEntity == null || string.IsNullOrEmpty(baseEntity.PrefabName)) return false;
            return _allowedLockableEntityPrefabs.Contains(baseEntity.PrefabName);
        }

        private bool IsAllowedEntity(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            return _allowedLockableEntityPrefabs.Contains(prefabName);
        }

        /// <summary>
        /// Verifica se <paramref name="entity"/> ha una serratura (<see cref="CodeLock"/> o <see cref="KeyLock"/>)
        /// posizionata nel proprio <see cref="BaseEntity.Slot.Lock"/>.
        /// </summary>
        /// <param name="entity">L'entità da controllare.</param>
        /// <returns><c>true</c> se lo slot Lock è occupato, <c>false</c> se è vuoto o <paramref name="entity"/> è <c>null</c>.</returns>
        private static bool HasLock(BaseEntity? entity) => GetLock(entity) != null;

        /// <summary>
        /// Restituisce la <see cref="BaseLock"/> (<see cref="CodeLock"/> o <see cref="KeyLock"/>) posizionata
        /// nello <see cref="BaseEntity.Slot.Lock"/> di <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">L'entità di cui recuperare la serratura.</param>
        /// <returns>La <see cref="BaseLock"/> trovata, oppure <c>null</c> se assente o <paramref name="entity"/> è <c>null</c>.</returns>
        private static BaseLock? GetLock(BaseEntity? entity) => entity?.GetSlot(BaseEntity.Slot.Lock) as BaseLock;

        /// <summary>
        /// Restituisce lo skin ID della serratura posizionata su <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">L'entità di cui leggere lo skin ID della serratura.</param>
        /// <returns>Lo skin ID della serratura, oppure <c>0</c> se assente o <paramref name="entity"/> è <c>null</c>.</returns>
        private static ulong GetLockSkin(BaseEntity? entity) => GetLock(entity)?.skinID ?? 0;

        private bool CanByPassCodeLock(BasePlayer player)
        {
            if (player == null) return false;

            if (IsInvisible(player))
            {
                return true;
            }

            var userId = player.UserIDString;
            return (_config.AllowAdminToBypassCodeLock &&
                    permission.UserHasPermission(userId, PermissionAdmin)) ||
                   permission.UserHasPermission(userId, PermissionBypassForce);
        }

        private static bool PlayerHasAKeyLock(BasePlayer player) => RetrievePlayerKeyLock(player) != null;

        private static bool PlayerHasACodeLock(BasePlayer player) => RetrievePlayerCodeLock(player, true) != null;

        private static bool PlayerHasAFlightControlCodelock(BasePlayer player) => RetrievePlayerCodeLock(player, false) != null;

        private static Item? RetrievePlayerKeyLock(BasePlayer player)
        {
            if (player == null || player.IsNpc) return null;

            var playerInventoryAllItems = Pool.Get<List<Item>>();
            try
            {
                player.inventory.GetAllItems(playerInventoryAllItems);
                return playerInventoryAllItems.FirstOrDefault(item => item.info.shortname.Equals(KeyLockShortPrefabName));
            }
            finally
            {
                Pool.FreeUnmanaged(ref playerInventoryAllItems);
            }
        }

        private static Item? RetrievePlayerCodeLock(BasePlayer player, bool excludeFlightControlCodelock)
        {
            if (player == null || player.IsNpc) return null;

            // Retrieve all items from the player's inventory and find the first item that matches the CodeLock or FlightControlCodelock PrefabName.
            var items = Pool.Get<List<Item>>();
            try
            {
                player.inventory.GetAllItems(items);
                var item = items.FirstOrDefault(i => i.info.shortname.Equals(CodeLockShortPrefabName));
                if (item == null && !excludeFlightControlCodelock)
                    item = items.FirstOrDefault(i => i.info.shortname.Equals(FlightControlCodelockShortPrefabName));

                return item;
            }
            finally
            {
                Pool.FreeUnmanaged(ref items);
            }
        }

        private LockCategory LockCategoryByEntity(BaseEntity entity)
        {
            if (entity == null) return LockCategory.None;

            if (_windowsPrefabName.Contains(entity.PrefabName))
                return LockCategory.Window;

            if (_medievalPrefabName.Contains(entity.PrefabName))
                return LockCategory.Medieval;

            if (entity is Door and not SmallRamp) return LockCategory.Door;

            if (_boxesPrefabName.Contains(entity.PrefabName))
                return LockCategory.Box;

            switch (entity)
            {
                case BoxStorage:
                    return LockCategory.StorageContainer;
                case Mannequin:
                case Locker:
                    return LockCategory.Locker;
                case BuildingPrivlidge:
                    return LockCategory.Cupboard;
                case BaseVehicle:
                    return LockCategory.Vehicle;
                // TODO IBoatBuildingPiece è dichiarata come internal e non è possibile fare il pattern matching, quindi controllo i tipi concreti che implementano IBoatBuildingPiece (non voglio utilizzare la reflection per questo)
                // case IBoatBuildingPiece:
                case IBoatPropulsion: // Sail, SmallEngine
                case BoatBuildingStation:
                case SteeringWheel:
                case Anchor:
                case Cannon:
                case SmallRamp:
                    return LockCategory.NavalDeepSea;
                case Composter:
                    return LockCategory.Composter;
                case BaseOven:
                    return LockCategory.Furnace;
                case VendingMachine:
                    return LockCategory.VendingMachine;
                case MixingTable:
                    return LockCategory.MixingTable;
                case PlanterBox:
                    return LockCategory.Planter;
                case AutoTurret:
                    return LockCategory.AutoTurret;
                case SamSite:
                    return LockCategory.SamSite;
                case FlameTurret or GunTrap:
                    return LockCategory.Trap;
                case WeaponRack:
                    return LockCategory.WeaponRack;
                case StashContainer:
                    return LockCategory.Stash;
                case NeonSign:
                    return LockCategory.NeonSign;
                case Signage:
                case PhotoFrame:
                case DecorDeployable:
                    return LockCategory.SignDrawing;
            }

            if (IsAllowedEntity(entity)) return LockCategory.OtherCustomEntities;

            if (entity.HasSlot(BaseEntity.Slot.Lock)) return LockCategory.OtherLockableEntities;

            return LockCategory.None;
        }

        private AutoClosingType AutoClosingTypeByEntity(BaseEntity entity, bool bypassOnlyEnabledCondition = false)
        {
            if (entity == null) return AutoClosingType.None;

            var (autoClosingType, value) = _autoClosingEntities.FirstOrDefault(ent => ent.Value.Contains(entity.PrefabName));
            if (value == null || value.IsNullOrEmpty()) return AutoClosingType.None;

            var autoClosingConfiguration = _config.AutoClosingConfiguration;
            return autoClosingType switch
            {
                AutoClosingType.Door when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableDoorAutoClosing => AutoClosingType.Door,
                AutoClosingType.DoubleDoor when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableDoubleDoorAutoClosing => AutoClosingType.DoubleDoor,
                AutoClosingType.Window when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableWindowAutoClosing => AutoClosingType.Window,
                AutoClosingType.Garage when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableGarageAutoClosing => AutoClosingType.Garage,
                AutoClosingType.LadderHatch when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableLadderHatchAutoClosing => AutoClosingType.LadderHatch,
                AutoClosingType.ExternalGate when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableExternalGateAutoClosing => AutoClosingType.ExternalGate,
                AutoClosingType.FenceGate when bypassOnlyEnabledCondition || autoClosingConfiguration.EnableFenceGateAutoClosing => AutoClosingType.FenceGate,
                AutoClosingType.LegacyWoodShelterDoor when (bypassOnlyEnabledCondition || autoClosingConfiguration.EnableLegacyWoodShelterDoorAutoClosing) &&
                                                           entity is LegacyShelter or LegacyShelterDoor => AutoClosingType.LegacyWoodShelterDoor,
                _ => AutoClosingType.None
            };
        }

        private bool IsAutoLockCategoryEnabled(LockCategory? lockCategory)
        {
            if (lockCategory == null) return false;

            var autoLockCategoryConfiguration = _config.AutoLockConfiguration.AutoLockCategoryConfiguration;

            return lockCategory switch
            {
                LockCategory.Door when autoLockCategoryConfiguration.IsDoorsLockEnabled => true,
                LockCategory.Window when autoLockCategoryConfiguration.IsWindowsLockEnabled => true,
                LockCategory.Box when autoLockCategoryConfiguration.IsBoxesLockEnabled => true,
                LockCategory.StorageContainer when autoLockCategoryConfiguration.IsStorageContainerLockEnabled => true,
                LockCategory.Locker when autoLockCategoryConfiguration.IsLockersLockEnabled => true,
                LockCategory.Cupboard when autoLockCategoryConfiguration.IsCupboardsLockEnabled => true,
                LockCategory.Vehicle when autoLockCategoryConfiguration.IsVehicleLockEnabled => true,
                LockCategory.NavalDeepSea when autoLockCategoryConfiguration.IsNavalDeepSeaLockEnabled => true,
                LockCategory.Medieval when autoLockCategoryConfiguration.IsMedievalEntityLockEnabled => true,
                LockCategory.Furnace when autoLockCategoryConfiguration.IsFurnaceLockEnabled => true,
                LockCategory.VendingMachine when autoLockCategoryConfiguration.IsVendingMachineLockEnabled => true,
                LockCategory.Composter when autoLockCategoryConfiguration.IsComposterLockEnabled => true,
                LockCategory.MixingTable when autoLockCategoryConfiguration.IsMixingTableLockEnabled => true,
                LockCategory.Planter when autoLockCategoryConfiguration.IsPlanterLockEnabled => true,
                LockCategory.AutoTurret when autoLockCategoryConfiguration.IsAutoTurretLockEnabled => true,
                LockCategory.SamSite when autoLockCategoryConfiguration.IsSamSiteLockEnabled => true,
                LockCategory.Trap when autoLockCategoryConfiguration.IsTrapsLockEnabled => true,
                LockCategory.WeaponRack when autoLockCategoryConfiguration.IsWeaponRackLockEnabled => true,
                LockCategory.Stash when autoLockCategoryConfiguration.IsStashLockEnabled => true,
                LockCategory.NeonSign when autoLockCategoryConfiguration.IsNeonSignLockEnabled => true,
                LockCategory.SignDrawing when autoLockCategoryConfiguration.IsSignDrawingLockEnabled => true,
                LockCategory.OtherCustomEntities when autoLockCategoryConfiguration.IsOtherCustomEntitiesLockEnabled => true,
                LockCategory.OtherLockableEntities when autoLockCategoryConfiguration.IsOtherLockableEntitiesLockEnabled => true,
                _ => false
            };
        }

        private bool IsLockCategoryEnabledInPlayerSettings(LockCategory? lockCategory, AutoLockPlayerSettingsData playerSettingsData)
        {
            if (lockCategory == null || playerSettingsData == null) return false;

            if (!IsAutoLockCategoryEnabled(lockCategory)) return false;

            return lockCategory switch
            {
                LockCategory.Door when playerSettingsData.DoorsLockEnabled => true,
                LockCategory.Window when playerSettingsData.WindowsLockEnabled => true,
                LockCategory.Box when playerSettingsData.BoxesLockEnabled => true,
                LockCategory.StorageContainer when playerSettingsData.StorageContainerLockEnabled => true,
                LockCategory.Locker when playerSettingsData.LockersLockEnabled => true,
                LockCategory.Cupboard when playerSettingsData.CupboardsLockEnabled => true,
                LockCategory.Vehicle when playerSettingsData.VehicleLockEnabled => true,
                LockCategory.NavalDeepSea when playerSettingsData.NavalDeepSeaLockEnabled => true,
                LockCategory.Medieval when playerSettingsData.MedievalEntityLockEnabled => true,
                LockCategory.Furnace when playerSettingsData.FurnaceLockEnabled => true,
                LockCategory.VendingMachine when playerSettingsData.VendingMachineLockEnabled => true,
                LockCategory.Composter when playerSettingsData.ComposterLockEnabled => true,
                LockCategory.MixingTable when playerSettingsData.MixingTableLockEnabled => true,
                LockCategory.Planter when playerSettingsData.PlanterLockEnabled => true,
                LockCategory.AutoTurret when playerSettingsData.AutoTurretLockEnabled => true,
                LockCategory.SamSite when playerSettingsData.SamSiteLockEnabled => true,
                LockCategory.Trap when playerSettingsData.TrapsLockEnabled => true,
                LockCategory.WeaponRack when playerSettingsData.WeaponRackLockEnabled => true,
                LockCategory.Stash when playerSettingsData.StashLockEnabled => true,
                LockCategory.NeonSign when playerSettingsData.NeonSignLockEnabled => true,
                LockCategory.SignDrawing when playerSettingsData.SignDrawingLockEnabled => true,
                LockCategory.OtherCustomEntities when playerSettingsData.OtherCustomEntitiesLockEnabled => true,
                LockCategory.OtherLockableEntities when playerSettingsData.OtherLockableEntitiesLockEnabled => true,
                _ => false
            };
        }

        private static bool IsAutoClosingTypeEnabledInPlayerSettings(AutoClosingType? autoClosingType, AutoClosingPlayerSettingsData playerSettingsData)
        {
            if (autoClosingType == null || playerSettingsData == null) return false;

            return autoClosingType switch
            {
                AutoClosingType.Door when playerSettingsData.DoorAutoClosingEnabled => true,
                AutoClosingType.DoubleDoor when playerSettingsData.DoubleDoorAutoClosingEnabled => true,
                AutoClosingType.Window when playerSettingsData.WindowAutoClosingEnabled => true,
                AutoClosingType.Garage when playerSettingsData.GarageAutoClosingEnabled => true,
                AutoClosingType.LadderHatch when playerSettingsData.LadderHatchAutoClosingEnabled => true,
                AutoClosingType.ExternalGate when playerSettingsData.ExternalGateAutoClosingEnabled => true,
                AutoClosingType.FenceGate when playerSettingsData.FenceGateAutoClosingEnabled => true,
                AutoClosingType.LegacyWoodShelterDoor when playerSettingsData.LegacyWoodShelterDoorAutoClosingEnabled => true,
                _ => false
            };
        }

        private bool IsLockCategoryEnabledForEntity(BaseEntity targetEntity, AutoLockPlayerSettingsData autoLockPlayerSettings)
        {
            if (targetEntity == null || targetEntity.IsDestroyed || autoLockPlayerSettings == null) return false;

            var lockCategory = LockCategoryByEntity(targetEntity);
            switch (lockCategory)
            {
                case LockCategory.Door when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Window when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Box when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.StorageContainer when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Locker when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Cupboard when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Vehicle when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.NavalDeepSea when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Medieval when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Furnace when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.VendingMachine when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Composter when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.MixingTable when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Planter when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.AutoTurret when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.SamSite when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Trap when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.WeaponRack when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.Stash when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.NeonSign when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.SignDrawing when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.OtherCustomEntities when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                case LockCategory.OtherLockableEntities when IsLockCategoryEnabledInPlayerSettings(lockCategory, autoLockPlayerSettings):
                    return true;
                case LockCategory.None:
                default:
                    return false;
            }
        }

        private bool IsAutoClosingEnabledForEntity(BaseEntity targetEntity, AutoClosingPlayerSettingsData autoClosingPlayerSettings)
        {
            if (targetEntity == null || targetEntity.IsDestroyed || autoClosingPlayerSettings == null) return false;

            var autoClosingType = AutoClosingTypeByEntity(targetEntity);
            switch (autoClosingType)
            {
                case AutoClosingType.Door when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.DoubleDoor when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.Window when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.Garage when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.LadderHatch when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.ExternalGate when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.FenceGate when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                case AutoClosingType.LegacyWoodShelterDoor when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    return true;
                case AutoClosingType.None:
                default:
                    return false;
            }
        }

        private AutoLockPlayerSettingsData GetAutoLockPlayerSettingsData(ulong steamID)
        {
            if (_data.AutoLockPlayerSettingsData.TryGetValue(steamID, out var autoLockPlayerSettingsData))
                return autoLockPlayerSettingsData;

            //Create default Auto Lock Player Settings Data
            autoLockPlayerSettingsData = GetDefaultAutoLockPlayerSettingsData(steamID);

            _data.AutoLockPlayerSettingsData.Add(steamID, autoLockPlayerSettingsData);
            SaveData();
            return autoLockPlayerSettingsData;
        }

        private AutoLockPlayerSettingsData GetDefaultAutoLockPlayerSettingsData(ulong steamID)
        {
            //Create default Auto Lock Player Settings Data
            var defaultAutoLockConfiguration = _config.AutoLockConfiguration;
            return new AutoLockPlayerSettingsData
            {
                SteamID = steamID,
                CodeLock = GenerateRandomCode(),
                GuestCodeLock = GenerateRandomCode(),

                AutoLockOnPlacementEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableAutoLockOnPlacementByDefault,
                AlsoUseKeyLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableAlsoUseKeyLockByDefault,
                GuestCodeEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableGuestCodeByDefault,
                ShareLocksWithClanTeamEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableAutoShareLocksWithClanTeamByDefault,
                StreamerModeEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableStreamerModeByDefault,
                CodelockSkinFlightControlCodelockEnabled = DefaultCodelockSkinFlightControlCodelockEnabled,
                DoorsLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableDoorsLockByDefault,
                WindowsLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableWindowsLockByDefault,
                BoxesLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableBoxesLockByDefault,
                StorageContainerLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableStorageContainerLockByDefault,
                LockersLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableLockersLockByDefault,
                CupboardsLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableCupboardsLockByDefault,
                VehicleLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableVehicleLockByDefault,
                NavalDeepSeaLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableNavalDeepSeaLockByDefault,
                VehicleLockAutoClosingOnDismountEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableVehicleLockAutoClosingOnDismountByDefault,
                MedievalEntityLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableMedievalEntityLockByDefault,
                FurnaceLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableFurnaceLockByDefault,
                VendingMachineLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableVendingMachineLockByDefault,
                ComposterLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableComposterLockByDefault,
                MixingTableLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableMixingTableLockByDefault,
                PlanterLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnablePlanterLockByDefault,
                AutoTurretLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableAutoTurretLockByDefault,
                SamSiteLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableSamSiteLockByDefault,
                TrapsLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableTrapsLockByDefault,
                WeaponRackLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableWeaponRackLockByDefault,
                StashLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableStashLockByDefault,
                NeonSignLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableNeonSignLockByDefault,
                SignDrawingLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableSignDrawingLockByDefault,
                OtherLockableEntitiesLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableOtherLockableEntitiesLockByDefault,
                OtherCustomEntitiesLockEnabled = defaultAutoLockConfiguration.AutoLockPlayerConfiguration.EnableOtherCustomEntitiesLockByDefault,
            };
        }

        private AutoClosingPlayerSettingsData GetAutoClosingPlayerSettingsData(BasePlayer player) => GetAutoClosingPlayerSettingsData(player.userID);

        private AutoClosingPlayerSettingsData GetAutoClosingPlayerSettingsData(ulong userID)
        {
            if (_data.AutoClosingPlayerSettingsData.TryGetValue(userID, out var autoClosingPlayerSettingsData))
                return autoClosingPlayerSettingsData;

            //Create default Auto Closing Player Settings Data
            autoClosingPlayerSettingsData = GetDefaultAutoClosingPlayerSettingsData(userID);

            _data.AutoClosingPlayerSettingsData.Add(userID, autoClosingPlayerSettingsData);
            SaveData();
            return autoClosingPlayerSettingsData;
        }

        private AutoClosingPlayerSettingsData GetDefaultAutoClosingPlayerSettingsData(ulong userID)
        {
            var defaultAutoClosingConfiguration = _config.AutoClosingConfiguration;
            //Create default Auto Closing Player Settings Data
            var defaultPlayerAutoClosingConfiguration = _config.AutoLockConfiguration.AutoClosingPlayerConfiguration;
            return new AutoClosingPlayerSettingsData
            {
                SteamID = userID,

                AutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableAutoClosingByDefault,
                ProximitySystemUIEnabled = true,
                DoorAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableDoorAutoClosingByDefault,
                DoubleDoorAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableDoubleDoorAutoClosingByDefault,
                WindowAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableWindowAutoClosingByDefault,
                GarageAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableGarageAutoClosingByDefault,
                LadderHatchAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableLadderHatchAutoClosingByDefault,
                ExternalGateAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableExternalGateAutoClosingByDefault,
                FenceGateAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableFenceGateAutoClosingByDefault,
                LegacyWoodShelterDoorAutoClosingEnabled = defaultPlayerAutoClosingConfiguration.EnableLegacyWoodShelterDoorAutoClosingByDefault,

                DoorClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime,
                DoubleDoorClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime,
                GarageClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime,
                LadderHatchClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime,
                ExternalGateClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime,
                FenceGateClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime,
                LegacyWoodShelterDoorClosingDelay = defaultAutoClosingConfiguration.DefaultClosingDelayTime
            };
        }

        private bool CheckPlayerHasShareLocksWithClanTeamEnabled(ulong steamID)
        {
            if (!steamID.IsSteamId()) return false;
            //Se ForceShareLocksWithClanTeam è diverso da null allora utilizza il valore forzato, altrimenti utilizza il valore scelto dal player
            return _config.AutoLockConfiguration.ForceShareLocksWithClanTeam ?? GetAutoLockPlayerSettingsData(steamID).ShareLocksWithClanTeamEnabled;
        }

        private int GetDoorCloserDelayTimeByPlayerSettings(ulong userID, Door door)
        {
            if (userID == null || !userID.IsSteamId() || door == null) return 0;

            var autoClosingType = AutoClosingTypeByEntity(door, true);
            if (autoClosingType == AutoClosingType.None) return 0;

            var playerSettingsData = GetAutoClosingPlayerSettingsData(userID);
            if (playerSettingsData == null) return 0;

            return autoClosingType switch
            {
                AutoClosingType.Door when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.DoorClosingDelay,
                AutoClosingType.DoubleDoor when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.DoubleDoorClosingDelay,
                AutoClosingType.Window when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.WindowClosingDelay,
                AutoClosingType.Garage when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.GarageClosingDelay + GarageOpenDuration,
                AutoClosingType.LadderHatch when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.LadderHatchClosingDelay,
                AutoClosingType.ExternalGate when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.ExternalGateClosingDelay,
                AutoClosingType.FenceGate when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.FenceGateClosingDelay,
                AutoClosingType.LegacyWoodShelterDoor when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, playerSettingsData) => playerSettingsData.LegacyWoodShelterDoorClosingDelay,
                _ => 0
            };
        }

        private bool SetPlayerSettingsAutoClosingDelayTime(AutoClosingType autoClosingType, AutoClosingPlayerSettingsData playerSettingsData, int delayTime)
        {
            switch (autoClosingType)
            {
                case AutoClosingType.Door:
                    playerSettingsData.DoorClosingDelay = delayTime;
                    break;
                case AutoClosingType.DoubleDoor:
                    playerSettingsData.DoubleDoorClosingDelay = delayTime;
                    break;
                case AutoClosingType.Window:
                    playerSettingsData.WindowClosingDelay = delayTime;
                    break;
                case AutoClosingType.Garage:
                    playerSettingsData.GarageClosingDelay = delayTime;
                    break;
                case AutoClosingType.LadderHatch:
                    playerSettingsData.LadderHatchClosingDelay = delayTime;
                    break;
                case AutoClosingType.ExternalGate:
                    playerSettingsData.ExternalGateClosingDelay = delayTime;
                    break;
                case AutoClosingType.FenceGate:
                    playerSettingsData.FenceGateClosingDelay = delayTime;
                    break;
                case AutoClosingType.LegacyWoodShelterDoor:
                    playerSettingsData.LegacyWoodShelterDoorClosingDelay = delayTime;
                    break;
                case AutoClosingType.None:
                default:
                    return false;
            }

            SaveData();
            return true;
        }

        /// <summary>
        /// Restituisce la <see cref="BaseEntity"/> colpita dal raycast partendo dagli occhi del <paramref name="player"/>.
        /// </summary>
        /// <param name="player">Il player da cui parte il raycast.</param>
        /// <param name="maxDistance">Distanza massima del raycast in metri. Default: <c>6</c>.</param>
        /// <returns>
        ///   La <see cref="BaseEntity"/> colpita, oppure <c>null</c> se il raycast non colpisce
        ///   nessuna entità nei layer di interesse o se <paramref name="player"/> è <c>null</c>.
        /// </returns>
        private static BaseEntity? GetEntityByRayCast(BasePlayer player, float maxDistance = 6f)
        {
            if (player == null) return null;

            Physics.Raycast(player.eyes.HeadRay(), out var hit, maxDistance,
                Layers.Mask.Vehicle_Detailed | Layers.Mask.Vehicle_Large | Layers.Mask.AI | Layers.Mask.Deployed | Layers.Deploy);

            return hit.GetEntity();
        }

        //Blocca l'auto lock sui veicoli GunShip, in quanto il codelock viene rimosso in automatico dal plugin al riavvio e i veicoli cambialo il loro NetId ad ogni riavvio del plugin
        private bool IsGunShipVehicle(BaseEntity entity)
        {
            if (GunShip == null) return false;

            if (entity.IsDestroyed) return false;
            if (entity.GetParentEntity() is not null)
                entity = entity.GetParentEntity();

            var components = entity.GetComponents<Component>();
            foreach (var component in components)
            {
                var fullName = component.GetType().FullName;
                if (string.IsNullOrWhiteSpace(fullName)) continue;

                var normalizedName = fullName.Replace("+", "/");
                if (_gunShipVehiclesComponentNameSet.Contains(normalizedName))
                    return true;
            }

            return false;
        }

        private static bool IsAnEntityToIgnore(BaseEntity entity)
        {
            if (entity == null) return false;

            if (entity is StorageContainer storageContainer)
            {
                //Ignore storage container with BackpackBox component (Plugin Backpack)
                if (storageContainer.GetComponent("Oxide.Plugins.Backpack/BackpackBox") != null)
                    return true;

                //Ignore storage container created by the plugin Bank
                var groundWatchComponent = storageContainer.GetComponent<GroundWatch>();
                if (groundWatchComponent != null && !groundWatchComponent.enabled && !storageContainer.enableSaving)
                    return true;
            }

            return false;
        }

        private bool IsSkinBlacklisted(SkinBlacklistType blacklistType, BaseEntity entity)
        {
            return entity != null && IsSkinBlacklisted(blacklistType, entity.skinID);
        }

        private bool IsSkinBlacklisted(SkinBlacklistType blacklistType, ulong skinID)
        {
            if (skinID == 0) return false;

            return blacklistType switch
            {
                SkinBlacklistType.Lock when _config.SkinBlacklistConfiguration.LockBlacklist.Contains(skinID) => true,
                SkinBlacklistType.DoorCloser when _config.SkinBlacklistConfiguration.DoorCloserBlacklist.Contains(skinID) => true,
                _ => false
            };
        }

        private bool IsAutoLockerEnabled(BasePlayer player, AutoLockPlayerSettingsData settings)
        {
            if (player == null || settings == null) return false;

            return permission.UserHasPermission(player.UserIDString, PermissionAutoLockEnabled) && settings.AutoLockOnPlacementEnabled;
        }

        private bool IsAutoClosingEnabled(ulong userId)
        {
            if (!userId.IsSteamId() || _config.AutoClosingConfiguration == null) return false;

            var autoClosingConfiguration = _config.AutoClosingConfiguration;

            return permission.UserHasPermission(userId.ToString(), PermissionAutoClosingEnabled) && (
                autoClosingConfiguration.EnableDoorAutoClosing || autoClosingConfiguration.EnableDoubleDoorAutoClosing ||
                autoClosingConfiguration.EnableWindowAutoClosing || autoClosingConfiguration.EnableGarageAutoClosing ||
                autoClosingConfiguration.EnableLadderHatchAutoClosing || autoClosingConfiguration.EnableExternalGateAutoClosing ||
                autoClosingConfiguration.EnableFenceGateAutoClosing || autoClosingConfiguration.EnableLegacyWoodShelterDoorAutoClosing
            );
        }

        /// <summary>
        /// Verifica se la chiusura automatica è abilitata per un player
        /// su una specifica <see cref="Door"/>, controllando permessi,
        /// impostazioni globali, impostazioni del player e categoria della door.
        /// </summary>
        /// <remarks>
        /// I controlli vengono eseguiti in ordine dal meno costoso al più costoso:
        /// <list type="number">
        ///   <item><description>
        ///     <see cref="IsAutoClosingEnabled(ulong)"/> — permesso e feature
        ///     abilitata nella configurazione globale.
        ///   </description></item>
        ///   <item><description>
        ///     <see cref="AutoClosingPlayerSettingsData.AutoClosingEnabled"/> —
        ///     chiusura automatica abilitata nelle impostazioni del player.
        ///   </description></item>
        ///   <item><description>
        ///     <see cref="IsAutoClosingTypeEnabledInPlayerSettings"/> — categoria
        ///     della door abilitata nelle impostazioni del player.
        ///   </description></item>
        /// </list>
        /// </remarks>
        /// <param name="ownerId">
        ///   Lo SteamID del player proprietario della door.
        /// </param>
        /// <param name="door">
        ///   La <see cref="Door"/> da verificare.
        /// </param>
        /// <returns>
        ///   <c>true</c> se tutti i controlli passano;
        ///   <c>false</c> se almeno uno fallisce.
        /// </returns>
        private bool CanAutoCloseDoor(ulong ownerId, Door door)
        {
            if (!IsAutoClosingEnabled(ownerId)) return false;

            var settings = GetAutoClosingPlayerSettingsData(ownerId);
            return settings.AutoClosingEnabled && IsAutoClosingTypeEnabledInPlayerSettings(AutoClosingTypeByEntity(door), settings);
        }

        private static LockDefinition? GetLockDefinitionByEntity(BaseEntity baseEntity)
        {
            if (baseEntity == null) return null;
            var entityToCheck = baseEntity;

            var parentModularCar = entityToCheck.GetComponentInParent<ModularCar>();
            if (parentModularCar != null)
            {
                entityToCheck = parentModularCar;
            }

            switch (entityToCheck)
            {
                case ModularCar modularCar:
                    return new LockDefinition
                    {
                        ModularCarCodeLock = modularCar.CarLock,
                        BaseEntity = modularCar
                    };
                case SteeringWheel steeringWheel:
                    return new LockDefinition
                    {
                        PlayerBoatLock = steeringWheel.BoatLock,
                        BaseEntity = steeringWheel
                    };
            }

            var baseLock = entityToCheck.GetSlot(BaseEntity.Slot.Lock) as BaseLock;

            if (baseLock == null && entityToCheck.GetParentEntity() == null)
                return null;

            if (baseLock == null && entityToCheck.GetParentEntity() != null)
            {
                baseLock = entityToCheck.GetParentEntity().GetSlot(BaseEntity.Slot.Lock) as BaseLock;
                entityToCheck = entityToCheck.GetParentEntity();
            }

            if (baseLock == null || entityToCheck == null) return null;

            return new LockDefinition
            {
                BaseLock = baseLock,
                BaseEntity = entityToCheck
            };
        }

        private static LockDefinition? GetLockDefinitionByLookingAt(BasePlayer player)
        {
            if (player == null) return null;

            var baseEntity = GetEntityByRayCast(player);
            if (baseEntity == null) return null;

            return GetLockDefinitionByEntity(baseEntity);
        }

        private static void AdjustBaseLockPosition(BaseEntity entity, BaseLock baseLock, LockableEntityConfig lockableEntityConfig)
        {
            if (entity == null || baseLock == null) return;

            baseLock.transform.localPosition = lockableEntityConfig.CodeLockPosition;
            baseLock.transform.localRotation = lockableEntityConfig.CodeLockRotation;
        }

        /// <summary>
        /// Clona un CodeLock esistente su un nuovo CodeLock (copia tutte le impostazioni dal vecchio CodeLock al target CodeLock)
        /// </summary>
        /// <param name="codeLock">Il CodeLock originale da cui effettuare la copia</param>
        /// <param name="targetCodeLock">Il CodeLock target sul quale effettuare la copia.</param>
        /// <param name="lockableEntityConfig">Se è diverso da null, allora è una serratura per un entità personalizzata (che di base non può avere una serratura)</param>
        /// <param name="runEffect">Se deve essere eseguito l'effetto sonoro di chiusura del codelock</param>
        /// <returns>True se la copia ha avuto successo, altrimenti ritorna false</returns>
        private bool CloneCodeLock(CodeLock codeLock, CodeLock targetCodeLock, LockableEntityConfig? lockableEntityConfig = null, bool runEffect = true)
        {
            if (codeLock == null || targetCodeLock == null) return false;
            if (codeLock.IsDestroyed || targetCodeLock.IsDestroyed) return false;

            var entity = codeLock.GetParentEntity();
            if (entity != null)
            {
                //If it's a custom entity, use the custom position for the lock
                if (lockableEntityConfig != null)
                    targetCodeLock.SetParent(entity);
                else
                    targetCodeLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));

                entity.SetSlot(BaseEntity.Slot.Lock, targetCodeLock);
            }

            targetCodeLock.OwnerID = codeLock.OwnerID;
            // targetCodeLock.transform.localScale = codeLock.transform.localScale;

            if (!codeLock.whitelistPlayers.IsNullOrEmpty())
            {
                targetCodeLock.whitelistPlayers.Clear();
                targetCodeLock.whitelistPlayers.AddRange(codeLock.whitelistPlayers);
            }

            if (!codeLock.guestPlayers.IsNullOrEmpty())
            {
                targetCodeLock.guestPlayers.Clear();
                targetCodeLock.guestPlayers.AddRange(codeLock.guestPlayers);
            }

            if (codeLock.hasCode)
            {
                targetCodeLock.code = codeLock.code;
            }

            if (codeLock.hasGuestCode)
            {
                targetCodeLock.guestCode = codeLock.guestCode;
            }

            using (var flags = targetCodeLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                flags.Set(BaseEntity.Flags.Locked, codeLock.IsLocked());

            if (runEffect && entity != null && codeLock.IsLocked())
            {
                RunEffect(CodeLockLockEffect, entity);
            }

            targetCodeLock.SendNetworkUpdateImmediate();

            return true;
        }

        private void AutoLockApplySettings(BasePlayer player, BaseLock baseLock, AutoLockPlayerSettingsData settings)
        {
            baseLock.OwnerID = player.userID;

            var isAutoLockerEnabled = IsAutoLockerEnabled(player, settings);
            if (isAutoLockerEnabled)
            {
                using (var flags = baseLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                    flags.Set(BaseEntity.Flags.Locked, true);
            }

            if (baseLock is not CodeLock codeLock) return;

            if (isAutoLockerEnabled)
            {
                codeLock.code = settings.CodeLock;
                codeLock.hasCode = true;

                if (settings.GuestCodeEnabled)
                {
                    codeLock.guestCode = settings.GuestCodeLock;
                    codeLock.hasGuestCode = true;
                }
            }

            codeLock.whitelistPlayers.Clear();
            codeLock.guestPlayers.Clear();

            if (!codeLock.whitelistPlayers.Contains(player.userID.Get()))
            {
                codeLock.whitelistPlayers.Add(player.userID);
            }

            if (CheckPlayerHasShareLocksWithClanTeamEnabled(settings.SteamID))
            {
                var clanInfo = GetClanInfo(player);
                if (clanInfo == null || clanInfo.ClanMemberUserIdList.IsNullOrEmpty())
                    return;

                codeLock.whitelistPlayers.AddRange(clanInfo.ClanMemberUserIdList.Select(ulong.Parse).ToList());
            }
        }

        private void ModularCarApplySettings(BasePlayer player, ModularCar modularCar, AutoLockPlayerSettingsData? settings = null, string? newCode = null)
        {
            settings ??= GetAutoLockPlayerSettingsData(player.userID.Get());
            if (settings == null) return;

            var isAutoLockerEnabled = IsAutoLockerEnabled(player, settings);
            var carLock = modularCar.CarLock;

            if (isAutoLockerEnabled)
            {
                var code = newCode ?? settings.CodeLock;
                if (carLock.TrySetNewCode(code, player.userID))
                {
                    using (var flags = modularCar.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(ModularCarCodeLock.FLAG_CENTRAL_LOCKING, true);
                }
            }

            var modularCarWhitelistPlayers = carLock.WhitelistPlayers.ToList();
            foreach (var modularCarWhitelistPlayer in modularCarWhitelistPlayers)
            {
                carLock.TryRemovePlayer(modularCarWhitelistPlayer);
            }

            if (!carLock.WhitelistPlayers.Contains(player.userID.Get()))
            {
                carLock.TryAddPlayer(player.userID.Get());
            }

            if (CheckPlayerHasShareLocksWithClanTeamEnabled(settings.SteamID))
            {
                var clanInfo = GetClanInfo(player);
                if (clanInfo == null || clanInfo.ClanMemberUserIdList.IsNullOrEmpty())
                    return;

                var clanMemberUserIdList = clanInfo.ClanMemberUserIdList.Select(ulong.Parse).ToList();
                foreach (var userID in clanMemberUserIdList)
                {
                    carLock.TryAddPlayer(userID);
                }
            }
        }

        private void SteeringWheelApplySettings(BasePlayer player, SteeringWheel steeringWheel, AutoLockPlayerSettingsData? settings = null, string? newCode = null)
        {
            settings ??= GetAutoLockPlayerSettingsData(player.userID.Get());
            if (settings == null) return;

            var isAutoLockerEnabled = IsAutoLockerEnabled(player, settings);
            var boatLock = steeringWheel.BoatLock;
            var playerBoatPrivilege = steeringWheel.Privilege;

            if (isAutoLockerEnabled)
            {
                var code = newCode ?? settings.CodeLock;
                if (boatLock.TrySetNewCode(code, player))
                {
                    using (var flags = steeringWheel.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, true);
                }
            }

            playerBoatPrivilege.authorizedPlayers.Clear();
            playerBoatPrivilege.UpdateMaxAuthCapacity();

            if (!playerBoatPrivilege.authorizedPlayers.Contains(player.userID.Get()))
            {
                playerBoatPrivilege.AddPlayer(player);
            }

            if (CheckPlayerHasShareLocksWithClanTeamEnabled(settings.SteamID))
            {
                var clanInfo = GetClanInfo(player);
                if (clanInfo == null || clanInfo.ClanMemberUserIdList.IsNullOrEmpty())
                    return;

                var clanMemberUserIdList = clanInfo.ClanMemberUserIdList.Select(ulong.Parse).ToList();
                foreach (var userID in clanMemberUserIdList)
                {
                    playerBoatPrivilege.authorizedPlayers.Add(userID);
                    playerBoatPrivilege.UpdateMaxAuthCapacity();
                }
            }
        }

        private void AutoLockSendMessage(BasePlayer player, BaseLock? baseLock, PlayerBoatLock? playerBoatLock, AutoLockPlayerSettingsData settings)
        {
            if (player == null || settings == null) return;
            if (baseLock == null && playerBoatLock == null) return;

            var userId = player.UserIDString;
            if (baseLock != null)
            {
                switch (baseLock)
                {
                    case CodeLock when settings.GuestCodeEnabled:
                        SendMessage(player,
                            Lang("AutoLockCodeLockAndGuestCodeDeployedMessage", userId, settings.StreamerModeEnabled ? "****" : settings.CodeLock,
                                settings.StreamerModeEnabled ? "****" : settings.GuestCodeLock));
                        break;
                    case CodeLock:
                        SendMessage(player,
                            Lang("AutoLockCodeLockDeployedMessage", userId, settings.StreamerModeEnabled ? "****" : settings.CodeLock));
                        break;
                    case KeyLock:
                        SendMessage(player, Lang("AutoLockKeyLockDeployedMessage", userId));
                        break;
                }
            }

            if (playerBoatLock != null)
            {
                SendMessage(player,
                    Lang("AutoLockCodeLockDeployedMessage", userId, settings.StreamerModeEnabled ? "****" : settings.CodeLock));
            }
        }

        private bool CanDeployLock(BasePlayer player, BaseEntity baseEntity, bool ignoreExistingLock = false)
        {
            if (string.IsNullOrEmpty(baseEntity.PrefabName)) return false;

            var entityToCheck = GetMainEntity(baseEntity);
            if (entityToCheck is null) return false;

            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse) || !IsAllowedEntity(entityToCheck)) return false;

            if (IsSkinBlacklisted(SkinBlacklistType.Lock, baseEntity))
            {
                return false;
            }

            if (IsGunShipVehicle(baseEntity)) return false;

            //START - Required Permission check
            var lockableEntityConfig = GetLockableEntityConfigForAllowedEntity(entityToCheck);

            if (lockableEntityConfig != null && !HasValidPermission(player, lockableEntityConfig.RequiredPermission)) return false;
            //END - Required Permission check


            if (_config.RequiresBuildingPrivilege && !permission.UserHasPermission(userId, PermissionBypassLockDeployVehicleCheck) && player.IsBuildingBlocked())
                return false;


            if (entityToCheck is MiningQuarry miningQuarry)
            {
                if (miningQuarry.isStatic && !miningQuarry.OwnerID.IsSteamId())
                    return false;
            }

            if (entityToCheck is ModularCar modularCar)
            {
                if (modularCar.CarLock.HasALock)
                    return false;

                if (ignoreExistingLock && modularCar.CarLock.HasALock)
                    return false;

                if (player.GetActiveItem() == null || player.GetActiveItem().info.itemid.Equals(KeyLockItemID))
                    return false;
            }

            if (entityToCheck is BaseVehicle or SteeringWheel)
            {
                switch (entityToCheck)
                {
                    case BaseVehicle baseVehicle when !ignoreExistingLock && baseVehicle.GetSlot(BaseEntity.Slot.Lock) is not null:
                        return false;
                    case SteeringWheel:
                        return ignoreExistingLock;
                }

                if (_config.RequiresBuildingPrivilegeToDeployCodeLockInUnownedVehicles && !entityToCheck.OwnerID.IsSteamId() && player.IsBuildingBlocked())
                    return permission.UserHasPermission(userId, PermissionBypassLockDeployVehicleCheck) || player.IsBuildingAuthed();

                if (_config.AllowDeployCodeLockInOwnedPlayersVehicles && entityToCheck.OwnerID.IsSteamId())
                    return true;

                if (!entityToCheck.OwnerID.IsSteamId() &&
                    (_config.AllowDeployCodeLockInUnownedVehicles || permission.UserHasPermission(userId, PermissionBypassLockDeployVehicleCheck)))
                    return true;
            }

            return (ignoreExistingLock || entityToCheck.GetSlot(BaseEntity.Slot.Lock) is null) && IsEntityOwner(entityToCheck, player);
        }

        private bool DeployLock(BasePlayer player, BaseEntity baseEntity, bool byPassActiveItem = false)
        {
            try
            {
                if (baseEntity.GetSlot(BaseEntity.Slot.Lock) is not null)
                    return false;

                var entityToCheck = GetMainEntity(baseEntity);
                if (entityToCheck is null) return false;

                var userId = player.UserIDString;
                if (!permission.UserHasPermission(userId, PermissionUse) || !IsAllowedEntity(entityToCheck)) return false;

                if (IsSkinBlacklisted(SkinBlacklistType.Lock, baseEntity))
                {
                    return false;
                }

                if (IsGunShipVehicle(baseEntity)) return false;

                var lockableEntityConfig = GetLockableEntityConfigForAllowedEntity(entityToCheck);

                if (lockableEntityConfig == null)
                {
                    RunEffect(CodeLockDeniedEffect, player);
                    return false;
                }

                BaseLock? locker;
                if (!byPassActiveItem)
                {
                    var activeItem = player.GetActiveItem();
                    if (activeItem == null) return false;

                    if (activeItem.info.itemid.Equals(KeyLockItemID))
                        locker = GameManager.server.CreateEntity(KeyLockPrefab, lockableEntityConfig.CodeLockPosition, lockableEntityConfig.CodeLockRotation) as KeyLock;
                    else if (!_config.AutoLockConfiguration.UseOnlyKeyLock && (activeItem.info.itemid.Equals(CodeLockItemID) || activeItem.info.itemid.Equals(FlightControlCodeLockItemID)))
                        locker = GameManager.server.CreateEntity(activeItem.info.itemid == CodeLockItemID ? CodeLockPrefab : FlightControlCodelockPrefab, lockableEntityConfig.CodeLockPosition,
                            lockableEntityConfig.CodeLockRotation) as CodeLock;
                    else
                        return false;
                }
                else
                {
                    locker = GameManager.server.CreateEntity(CodeLockPrefab, lockableEntityConfig.CodeLockPosition, lockableEntityConfig.CodeLockRotation) as CodeLock;
                }

                if (locker is null)
                {
                    RunEffect(CodeLockDeniedEffect, player);
                    return false;
                }

                var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID.Get());

                if (entityToCheck is ModularCar modularCar)
                {
                    if (modularCar.CarLock.HasALock)
                        return false;

                    var hasALock = modularCar.CarLock.HasALock;

                    var codeLockPin = DefaultModularCarCodeLockCode;
                    if (autoLockPlayerSettings is { AutoLockOnPlacementEnabled: true, VehicleLockEnabled: true })
                        codeLockPin = autoLockPlayerSettings.CodeLock;

                    ModularCarApplySettings(player, modularCar, autoLockPlayerSettings, codeLockPin);

                    if (!hasALock)
                    {
                        if (codeLockPin.Equals(DefaultModularCarCodeLockCode))
                            SendMessage(player, Lang("ModularCarNewCodeLockMessage", userId, modularCar.CarLock.Code));
                        else
                            SendMessage(player, Lang("AutoLockCodeLockDeployedMessage", userId,
                                autoLockPlayerSettings.StreamerModeEnabled ? "****" : autoLockPlayerSettings.CodeLock));
                    }

                    ChangeVehicleOwnership(player, entityToCheck);

                    EntityDeployed(null, modularCar, null, player);

                    return true;
                }

                if (locker is KeyLock keyLock)
                {
                    using (var flags = keyLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, true);
                }

                locker.Spawn();
                // codeLock.OwnerID = baseEntity.OwnerID;
                locker.OwnerID = player.userID;
                locker.SetParent(entityToCheck, false, false);

                entityToCheck.SetSlot(BaseEntity.Slot.Lock, locker);

                ChangeLockerScaleSize(lockableEntityConfig, locker, false);

                locker.SendNetworkUpdateImmediate();

                ChangeVehicleOwnership(player, entityToCheck);

                EntityDeployed(locker, null, null, player);

                if (autoLockPlayerSettings.AutoLockOnPlacementEnabled)
                    Interface.CallHook("OnEntitySpawned", locker);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DeployLocker(BasePlayer player, BaseEntity entity, bool sendMessage)
        {
            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse))
            {
                if (sendMessage)
                    SendMessage(player, Lang("NoPermissions", userId));
                return;
            }

            if (IsSkinBlacklisted(SkinBlacklistType.Lock, entity))
            {
                if (sendMessage)
                    SendMessage(player, Lang("EntitySkinBlacklisted", userId));
                return;
            }

            if (IsAnEntityToIgnore(entity)) return;

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID.Get());

            var targetEntity = entity;
            if (entity is BaseLock baseLock && entity.GetParentEntity() != null)
            {
                targetEntity = baseLock.GetParentEntity();
            }

            if (IsLockCategoryEnabledForEntity(targetEntity, autoLockPlayerSettings))
            {
                PlaceLocker(player, targetEntity, autoLockPlayerSettings, sendMessage);
            }
            else
            {
                if (sendMessage)
                    SendMessage(player, Lang("LockCategoryNotValid", userId));
                return;
            }
        }

        private static void ChangeLockerScaleSize(LockableEntityConfig? lockableEntityConfig, BaseLock? locker, bool sendNetworkUpdate = true)
        {
            if (lockableEntityConfig == null || locker == null) return;
            ChangeEntityScaleSize(locker, lockableEntityConfig.ScaleSize, sendNetworkUpdate);
        }

        private static void ChangeEntityScaleSize(BaseEntity? baseEntity, float scaleSize, bool sendNetworkUpdate = true)
        {
            if (baseEntity == null) return;
            if (Mathf.Approximately(scaleSize, DefaultScaleSize)) return;

            var currentScale = baseEntity.transform.localScale;
            // Se l'entità ha già una scala diversa da quella di default, non applicare la scala personalizzata per evitare di scalare più volte l'entità stessa
            if (!Mathf.Approximately(currentScale.x, DefaultScaleSize) || !Mathf.Approximately(currentScale.y, DefaultScaleSize) || !Mathf.Approximately(currentScale.z, DefaultScaleSize))
                return;

            baseEntity.transform.localScale *= scaleSize;

            // locker.transform.localScale = new Vector3(
            //     currentScale.x * scaleSize,
            //     currentScale.y * scaleSize,
            //     currentScale.z * scaleSize
            // );

            baseEntity.networkEntityScale = true;

            if (sendNetworkUpdate)
                baseEntity.SendNetworkUpdate();
        }

        private void DeployDoorCloser(BasePlayer player, BaseEntity entity, bool sendMessage)
        {
            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse))
            {
                if (sendMessage)
                    SendMessage(player, Lang("NoPermissions", userId));
                return;
            }

            if (IsSkinBlacklisted(SkinBlacklistType.DoorCloser, entity))
            {
                if (sendMessage)
                    SendMessage(player, Lang("EntitySkinBlacklisted", userId));
                return;
            }

            if (!IsAutoClosingEnabled(player.userID))
            {
                if (sendMessage)
                    SendMessage(player, Lang("AutoClosingNotEnabled", userId));
                return;
            }

            var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(player.userID);
            if (!autoClosingPlayerSettings.AutoClosingEnabled)
            {
                if (sendMessage)
                    SendMessage(player, Lang("PlayerAutoClosingNotEnabled", userId));
                return;
            }

            var autoClosingType = AutoClosingTypeByEntity(entity);
            if (autoClosingType == AutoClosingType.None)
            {
                if (sendMessage)
                    SendMessage(player, Lang("AutoClosingTypeNotValid", userId));
                return;
            }

            switch (autoClosingType)
            {
                case AutoClosingType.Door when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.Door, autoClosingPlayerSettings.DoorClosingDelay, autoClosingPlayerSettings, sendMessage);
                    break;
                case AutoClosingType.DoubleDoor when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.DoubleDoor, autoClosingPlayerSettings.DoubleDoorClosingDelay,
                        autoClosingPlayerSettings, sendMessage);
                    break;
                case AutoClosingType.Window when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.Window, autoClosingPlayerSettings.WindowClosingDelay,
                        autoClosingPlayerSettings, sendMessage);
                    break;
                case AutoClosingType.Garage when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.Garage, autoClosingPlayerSettings.GarageClosingDelay + GarageOpenDuration,
                        autoClosingPlayerSettings, sendMessage);
                    break;
                case AutoClosingType.LadderHatch when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.LadderHatch, autoClosingPlayerSettings.LadderHatchClosingDelay,
                        autoClosingPlayerSettings, sendMessage);
                    break;
                case AutoClosingType.ExternalGate when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.ExternalGate, autoClosingPlayerSettings.ExternalGateClosingDelay,
                        autoClosingPlayerSettings, sendMessage);
                    break;
                case AutoClosingType.FenceGate when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.FenceGate, autoClosingPlayerSettings.FenceGateClosingDelay, autoClosingPlayerSettings,
                        sendMessage);
                    break;
                case AutoClosingType.LegacyWoodShelterDoor when IsAutoClosingTypeEnabledInPlayerSettings(autoClosingType, autoClosingPlayerSettings):
                    PlaceDoorCloser(player, entity, AutoClosingType.LegacyWoodShelterDoor, autoClosingPlayerSettings.LegacyWoodShelterDoorClosingDelay,
                        autoClosingPlayerSettings, sendMessage);
                    break;
                default:
                    if (sendMessage)
                        SendMessage(player, Lang("AutoClosingTypeNotValid", userId));
                    return;
            }
        }

        /// <summary>
        /// Piazza una serratura sull' <paramref name="entity"/>: riusa quella già presente nello
        /// <see cref="BaseEntity.Slot.Lock"/> se c'è, altrimenti ne crea una nuova secondo le impostazioni
        /// di AutoLock del player (deploy manuale via item, oppure senza consumare item se
        /// <paramref name="noLockRequired"/> è <c>true</c> o il player ha <see cref="PermissionAutoLockNoLockRequired"/>).
        /// </summary>
        /// <param name="player">Il player proprietario, usato per permessi, inventario e messaggi.</param>
        /// <param name="entity">L'entità su cui piazzare/sincronizzare la serratura.</param>
        /// <param name="settings">Le impostazioni di AutoLock del player (codice, guest code, ecc.).</param>
        /// <param name="sendMessage">Se <c>true</c>, invia messaggi di feedback al player in caso di errore o successo.</param>
        /// <param name="runEffect">Se <c>true</c>, riproduce l'effetto sonoro di posizionamento della serratura.</param>
        /// <param name="noLockRequired">
        ///   Se <c>true</c>, crea la serratura direttamente senza richiedere/consumare un item dall'inventario
        ///   del player (usato per il ripristino automatico, es. <see cref="RestoreLockAfterReskin"/>).
        /// </param>
        /// <param name="previousLock">
        ///   La serratura da cui ricavare il tipo (KeyLock o CodeLock) da ricreare, se nota (es. la serratura
        ///   persa in un reskin distruttivo). Se <c>null</c>, il tipo viene deciso dalla config globale
        ///   <c>UseOnlyKeyLock</c>. Può riferirsi a un'entità ormai distrutta: il controllo di validità va
        ///   fatto con <c>is not null</c>/<c>is null</c> e non con <c>==</c>/<c>!=</c>, perché
        ///   <see cref="BaseEntity"/> sovrascrive quegli operatori per considerare "null" anche un'entità
        ///   distrutta pur restando un riferimento .NET valido (i suoi campi, come <c>ShortPrefabName</c>,
        ///   restano comunque leggibili).
        /// </param>
        private void PlaceLocker(BasePlayer player, BaseEntity entity, AutoLockPlayerSettingsData settings, bool sendMessage, bool runEffect = true, bool noLockRequired = false,
            BaseLock? previousLock = null)
        {
            if (player == null || entity == null || settings == null)
            {
                if (sendMessage && player != null)
                    SendMessage(player, Lang("MissingParametersError", player.UserIDString, "player or entity or settings"));
                return;
            }

            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse))
            {
                if (sendMessage)
                    SendMessage(player, Lang("NoPermissions", userId));
                return;
            }

            if (IsSkinBlacklisted(SkinBlacklistType.Lock, entity))
            {
                if (sendMessage)
                    SendMessage(player, Lang("EntitySkinBlacklisted", userId));
                return;
            }

            var isAutoLockerEnabled = IsAutoLockerEnabled(player, settings);
            if (isAutoLockerEnabled)
            {
                if (string.IsNullOrWhiteSpace(settings.CodeLock) || !IsValidLockCode(settings.CodeLock))
                {
                    settings.CodeLock = GenerateRandomCode();
                    SaveData();
                }

                if (settings.GuestCodeEnabled && (string.IsNullOrWhiteSpace(settings.GuestCodeLock) || !IsValidLockCode(settings.GuestCodeLock)))
                {
                    settings.GuestCodeLock = GenerateRandomCode();
                    SaveData();
                }
            }

            //Check if it is a custom entity and if it can be locked
            var lockableEntityConfig = GetLockableEntityConfigForAllowedEntity(entity);
            if (lockableEntityConfig != null && !CanDeployLock(player, entity, true))
            {
                if (sendMessage)
                    SendMessage(player, Lang("EntityNotAllowed", userId));
                return;
            }

            if (IsGunShipVehicle(entity)) return;

            //If it is not a custom entity, verify that it is a lockable entity
            if (lockableEntityConfig == null && !entity.HasSlot(BaseEntity.Slot.Lock))
            {
                if (sendMessage)
                    SendMessage(player, Lang("EntityNotAllowed", userId));
                return;
            }

            var forceFlightControlCodeLockSkinID = IsSkinOwned(player, FlightControlCodeLockSkinID) && settings.CodelockSkinFlightControlCodelockEnabled;
            var codeLockPrefab = forceFlightControlCodeLockSkinID ? FlightControlCodelockPrefab : CodeLockPrefab;

            //If the lock is positioned manually, apply the various settings
            var currentLock = entity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            if (currentLock != null)
            {
                if (currentLock is CodeLock cl && !_config.AutoLockConfiguration.UseOnlyKeyLock)
                {
                    var oldCode = cl.code;

                    // Se viene forzato l'utilizzo della skin [Flight Control Codelock] e il codelock attuale non ha quella skin, lo sostituisco.
                    // Rimuovo il vecchio codelock e ne genero uno nuovo con la skin corretta, copio le impostazioni del vecchio codelock e lo assegno all'entità.
                    if (forceFlightControlCodeLockSkinID && cl.prefabID != FlightControlCodelockPrefabID)
                    {
                        var newLocker = GameManager.server.CreateEntity(FlightControlCodelockPrefab,
                                lockableEntityConfig?.CodeLockPosition ?? default,
                                lockableEntityConfig?.CodeLockRotation ?? default)
                            as CodeLock;

                        if (newLocker != null)
                        {
                            //Genero il nuovo codelock con la skin Flight Control Codelock
                            newLocker.Spawn();

                            ChangeLockerScaleSize(lockableEntityConfig, newLocker);

                            if (CloneCodeLock(cl, newLocker, lockableEntityConfig))
                            {
                                //Rimuovo il vecchio codelock senza skin
                                currentLock.Kill();
                                currentLock.SendNetworkUpdateImmediate();

                                currentLock = newLocker;
                                cl = newLocker;
                            }

                            EntityDeployed(newLocker, null, null, player);
                        }
                    }

                    if (!isAutoLockerEnabled) return;

                    AutoLockApplySettings(player, currentLock, settings);
                    var newCode = cl.code;

                    ChangeVehicleOwnership(player, entity);

                    if (oldCode.Equals(newCode))
                        return;
                }

                AutoLockSendMessage(player, currentLock, null, settings);

                return;
            }


            if (!isAutoLockerEnabled)
            {
                if (sendMessage)
                    SendMessage(player, Lang("AutoLockerNotEnabled", userId));
                return;
            }


            // TODO unificare con DeploLock per evitare duplicazione del codice.
            // TODO CREARE UN MTODO UNICO PER IL DEPLOY DELLA SERRATURA CHE VENGA USATO SIA PER IL DEPLOY MANUALE CHE PER QUELLO AUTOMATICO IN MODO DA EVITARE DUPLICAZIONE DEL CODICE E POSSIBILI INCONGRUENZE FUTURE
            if (entity is SteeringWheel steeringWheel)
            {
                if (_config.AutoLockConfiguration.UseOnlyKeyLock) return;
                if (!isAutoLockerEnabled) return;

                var boatLock = steeringWheel.BoatLock;
                var hasALock = boatLock.HasALock;

                SteeringWheelApplySettings(player, steeringWheel, settings);

                if (!hasALock)
                    AutoLockSendMessage(player, null, boatLock, settings);

                if (runEffect)
                    RunEffect(CodeLockDeployEffect, entity);

                EntityDeployed(null, null, steeringWheel, player);

                return;
            }


            Item? lockerItem = null;
            BaseLock? locker = null;
            if (noLockRequired || permission.UserHasPermission(userId, PermissionAutoLockNoLockRequired))
            {
                // Se conosco il tipo della serratura precedente lo rispetto, invece di ricadere sempre sul
                // CodeLock/sulla config globale UseOnlyKeyLock.
                // Il controllo va fatto con pattern match ("is not null"/"is KeyLock"), mai con "==": quando
                // previousLock arriva da RestoreLockAfterReskin punta alla serratura della vecchia entità,
                // ormai distrutta dal reskin. BaseEntity sovrascrive "==" per far apparire "null" anche
                // un'entità distrutta, pur restando il riferimento .NET valido (tipo e campi restano
                // leggibili normalmente). Con "!= null" si finisce sempre nel ramo else, ignorando il
                // tipo della serratura precedente. "is"/"is not" invece ignorano l'overload e controllano
                // il riferimento reale.
                var useKeyLock = previousLock is not null ? previousLock is KeyLock : _config.AutoLockConfiguration.UseOnlyKeyLock;

                locker = GameManager.server.CreateEntity(useKeyLock ? KeyLockPrefab : codeLockPrefab,
                        lockableEntityConfig?.CodeLockPosition ?? default,
                        lockableEntityConfig?.CodeLockRotation ?? default)
                    as BaseLock;
            }

            if (locker == null)
            {
                if (!_config.AutoLockConfiguration.UseOnlyKeyLock)
                    lockerItem = RetrievePlayerCodeLock(player, false);

                if (lockerItem != null)
                {
                    locker = GameManager.server.CreateEntity(lockerItem.info.itemid == CodeLockItemID ? CodeLockPrefab : FlightControlCodelockPrefab,
                            lockableEntityConfig?.CodeLockPosition ?? default,
                            lockableEntityConfig?.CodeLockRotation ?? default)
                        as CodeLock;
                }

                if (locker == null && (settings.AlsoUseKeyLockEnabled || _config.AutoLockConfiguration.UseOnlyKeyLock))
                {
                    lockerItem = RetrievePlayerKeyLock(player);
                    if (lockerItem != null)
                    {
                        locker = GameManager.server.CreateEntity(KeyLockPrefab,
                                lockableEntityConfig?.CodeLockPosition ?? default,
                                lockableEntityConfig?.CodeLockRotation ?? default)
                            as KeyLock;
                    }
                }

                if ((locker == null || locker is CodeLock) && _config.AutoLockConfiguration.UseOnlyKeyLock)
                {
                    if (sendMessage)
                        SendMessage(player, Lang("UseOnlyKeyLock_KeyLockNotFound", userId));
                    return;
                }
            }

            if (locker == null)
            {
                if (sendMessage)
                    SendMessage(player, Lang("LockItemNotFound", userId));
                return;
            }

            // TODO unificare con DeploLock per evitare duplicazione del codice.
            // TODO CREARE UN MTODO UNICO PER IL DEPLOY DELLA SERRATURA CHE VENGA USATO SIA PER IL DEPLOY MANUALE CHE PER QUELLO AUTOMATICO IN MODO DA EVITARE DUPLICAZIONE DEL CODICE E POSSIBILI INCONGRUENZE FUTURE
            if (entity is ModularCar modularCar)
            {
                var hasALock = modularCar.CarLock.HasALock;

                var codeLockPin = DefaultModularCarCodeLockCode;
                if (settings is { AutoLockOnPlacementEnabled: true, VehicleLockEnabled: true })
                    codeLockPin = settings.CodeLock;

                ModularCarApplySettings(player, modularCar, settings, codeLockPin);

                if (!hasALock)
                {
                    if (codeLockPin.Equals(DefaultModularCarCodeLockCode))
                        SendMessage(player, Lang("ModularCarNewCodeLockMessage", userId, modularCar.CarLock.Code));
                    else
                        SendMessage(player, Lang("AutoLockCodeLockDeployedMessage", userId,
                            settings.StreamerModeEnabled ? "****" : settings.CodeLock));
                }

                ChangeVehicleOwnership(player, entity);

                lockerItem?.UseItem();

                if (runEffect && lockerItem != null && lockerItem.info.itemid.Equals(CodeLockItemID))
                    RunEffect(CodeLockDeployEffect, entity);

                EntityDeployed(null, modularCar, null, player);

                return;
            }


            AutoLockApplySettings(player, locker, settings);
            locker.Spawn();

            //If it's a custom entity, use the custom position for the lock
            if (lockableEntityConfig != null)
                locker.SetParent(entity);
            else
                locker.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));

            entity.SetSlot(BaseEntity.Slot.Lock, locker);

            ChangeLockerScaleSize(lockableEntityConfig, locker);

            lockerItem?.UseItem();


            if (isAutoLockerEnabled && locker is CodeLock cLock)
            {
                AutoLockSendMessage(player, locker, null, settings);

                if (runEffect && cLock.IsLocked())
                {
                    RunEffect(CodeLockLockEffect, entity);
                    return;
                }
            }

            if (runEffect)
                RunEffect(CodeLockDeployEffect, entity);
        }

        /// <summary>
        /// Ripristina la serratura dopo un reskin "distruttivo": per alcune entità (es. Furnace -> Legacy Furnace)
        /// è Rust stesso, non i plugin di reskin, a distruggere e ricreare l'entità come un oggetto completamente
        /// diverso quando ne viene cambiata la skin. Con la bomboletta spray vanilla la serratura sopravvive
        /// (il gioco la riassegna correttamente alla nuova entità); con SkinBox o Skinner invece scompare,
        /// perché questi plugin non gestiscono il riposizionamento delle entità figlie (lo
        /// <see cref="BaseEntity.Slot.Lock"/>) dopo la ricreazione.
        /// </summary>
        /// <remarks>
        /// Il metodo viene chiamato PRIMA che il reskin venga eseguito (hook <c>CanReskinDeployableWith</c>
        /// di SkinBox, prefix Harmony su <c>Skinner_SkinDeployable</c>), quando <paramref name="entity"/> è
        /// ancora la vecchia entità con la serratura ancora presente. Aspetta un frame (tempo necessario al
        /// gioco/plugin per completare la ricreazione dell'entità), poi individua la nuova entità tramite
        /// raycast nella posizione verso cui il player sta guardando e le assegna una nuova serratura
        /// generata dalle impostazioni di AutoLock del player.
        /// </remarks>
        /// <param name="entity">La vecchia entità, ancora con la serratura, su cui sta per essere applicato il reskin.</param>
        /// <param name="player">Il player proprietario dell'entità, usato per il raycast e le impostazioni di AutoLock.</param>
        /// <param name="newSkin">Lo skin ID che verrà applicato. <c>0</c> indica la rimozione della skin corrente.</param>
        /// <param name="itemDefinition">
        ///   La <see cref="ItemDefinition"/> dell'item skinnato, se disponibile (SkinBox la fornisce, Skinner no).
        ///   Se <see cref="ItemDefinition.HasSkins"/> è <c>true</c> e si sta applicando una skin
        ///   (<paramref name="newSkin"/> diverso da <c>0</c>), il cambio è nativo (stesso prefab, nessuna
        ///   ricreazione) e il fix viene saltato.
        /// </param>
        private void RestoreLockAfterReskin(BaseEntity entity, BasePlayer player, ulong newSkin, ItemDefinition? itemDefinition)
        {
            if (entity == null || !entity.OwnerID.IsSteamId()) return;
            if (player == null || !player.userID.IsSteamId()) return;

            // Se l'item supporta nativamente le skin (stesso prefab, nessuna ricreazione), il fix non serve.
            if (newSkin != 0 && itemDefinition?.HasSkins == true) return;

            var baseLock = GetLock(entity);
            if (baseLock == null) return;

            // Impedisco a OnEntitySpawned di piazzare una serratura "di default" sulla nuova entità
            // prima che questo fix arrivi a ripristinare quella (dello stesso tipo) appena persa.
            _pendingReskinLockRestore.Add(player.userID);

            NextFrame(() =>
            {
                _pendingReskinLockRestore.Remove(player.userID);

                var newEntity = GetEntityByRayCast(player);
                if (newEntity == null) return;

                var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID.Get());
                PlaceLocker(player, newEntity, autoLockPlayerSettings, false, false, true, baseLock);
            });
        }

        private void ChangeVehicleOwnership(BasePlayer player, BaseEntity entity)
        {
            if (_config is { AutoClaimUnownedVehiclesWhenDeployLock: false, AutoReplaceVehicleOwnershipWhenDeployLock: false }) return;

            if (player == null || entity == null) return;

            if (entity.GetParentEntity() != null)
                entity = entity.GetParentEntity();

            if (entity is not BaseVehicle baseVehicle) return;

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse) || !IsAllowedEntity(baseVehicle)) return;

            if (_config.AutoClaimUnownedVehiclesWhenDeployLock && !baseVehicle.OwnerID.IsSteamId() || _config.AutoReplaceVehicleOwnershipWhenDeployLock)
            {
                baseVehicle.OwnerID = player.userID;
            }
        }

        /// <summary>
        /// Registra un Door Closer sulla <paramref name="entity"/> specificata per il <paramref name="player"/>.
        /// Gestisce la risoluzione della <see cref="Door"/> reale (anche da <see cref="LegacyShelter"/>),
        /// verifica permessi, blacklist skin e stato della funzionalità, quindi persiste il dato via
        /// <see cref="AddOrUpdateDoorCloserData"/>.
        /// </summary>
        /// <param name="player">Il player che richiede il piazzamento.</param>
        /// <param name="entity">
        ///   L'entità bersaglio: può essere una <see cref="Door"/> diretta o una <see cref="LegacyShelter"/>
        ///   da cui verrà estratta la <see cref="LegacyShelterDoor"/> figlia.
        /// </param>
        /// <param name="autoClosingType">
        ///   Categoria di chiusura automatica. Attualmente non utilizzato internamente;
        ///   viene passato dal chiamante per coerenza con il contesto di invocazione.
        /// </param>
        /// <param name="autoClosingDelayTime">
        ///   Delay di chiusura in secondi. Se ≤ 0, viene sostituito con
        ///   <see cref="AutoClosingConfiguration.DefaultClosingDelayTime"/>.
        /// </param>
        /// <param name="settings">
        ///   Impostazioni del player per la chiusura automatica. Attualmente non utilizzato internamente;
        ///   viene passato dal chiamante per coerenza con il contesto di invocazione.
        /// </param>
        /// <param name="sendMessage">
        ///   Se <c>true</c>, invia al player messaggi di feedback (errori o conferma).
        /// </param>
        private void PlaceDoorCloser(BasePlayer player, BaseEntity entity, AutoClosingType autoClosingType,
            int autoClosingDelayTime, AutoClosingPlayerSettingsData settings, bool sendMessage)
        {
            if (entity == null) return;

            Door? door;
            switch (entity)
            {
                case LegacyShelter legacyShelter when legacyShelter.GetChildDoor() is { } legacyShelterDoor:
                    // Eredita l'OwnerID dal parent LegacyShelter, altrimenti i controlli successivi fallirebbero
                    if (!EnsureDoorOwnership(legacyShelterDoor)) return;
                    door = legacyShelterDoor;
                    break;
                case Door entityDoor:
                    door = entityDoor;
                    break;
                default:
                    if (sendMessage && player is not null)
                        SendMessage(player, Lang("EntityNotAllowed", player.UserIDString));
                    return;
            }

            if (player is null || settings == null)
            {
                if (sendMessage && player != null)
                    SendMessage(player, Lang("MissingParametersError", player.UserIDString, "player or entity or settings"));
                return;
            }

            var userId = player.UserIDString;

            if (!permission.UserHasPermission(userId, PermissionUse))
            {
                if (sendMessage) SendMessage(player, Lang("NoPermissions", userId));
                return;
            }

            if (IsSkinBlacklisted(SkinBlacklistType.DoorCloser, door))
            {
                if (sendMessage) SendMessage(player, Lang("EntitySkinBlacklisted", userId));
                return;
            }

            if (!IsAutoClosingEnabled(player.userID))
            {
                if (sendMessage) SendMessage(player, Lang("AutoClosingNotEnabled", userId));
                return;
            }

            // TODO non più necessario
            // if (HasDoorCloserData(door))
            // {
            //     if (sendMessage)
            //         SendMessage(player, Lang("DoorCloserAlreadyExists", userId));
            //     return;
            // }

            if (autoClosingDelayTime <= 0)
                autoClosingDelayTime = _config.AutoClosingConfiguration.DefaultClosingDelayTime;

            AddOrUpdateDoorCloserData(player.userID, door, autoClosingDelayTime);

            if (sendMessage)
                SendMessage(player, Lang("DoorCloserDeployed", userId, autoClosingDelayTime));
        }

        private BaseEntity? GetMainEntity(BaseEntity baseEntity)
        {
            if (baseEntity == null || string.IsNullOrEmpty(baseEntity.PrefabName)) return null;

            var parentEntity = baseEntity.GetParentEntity();
            if (baseEntity is ModularCar || (parentEntity != null && parentEntity is ModularCar))
            {
                return IsAllowedEntity(baseEntity)
                    ? baseEntity
                    : IsAllowedEntity(parentEntity)
                        ? parentEntity
                        : baseEntity;
            }

            return baseEntity;
        }

        //TODO DA RIMUOVERE
        // private BaseEntity? GetVehicleCockpit(ModularCar modularCar)
        // {
        //     if (modularCar == null || string.IsNullOrEmpty(modularCar.PrefabName)) return null;
        //     var vehicleCockpit =
        //         modularCar.children.FirstOrDefault(ch => _vehiclesCockpitPrefabName.Contains(ch.PrefabName));
        //     return vehicleCockpit == null ? null : vehicleCockpit;
        // }

        private void UpdateAllCodeLockAuthShareByClanName(string clanName)
        {
            var clanMembers = GetClanMembers(clanName);
            if (clanMembers == null || clanMembers.Count == 0) return;

            var clanMembersSteamIdList = ParseSteamIdList(clanMembers.Select(member => (string)member).ToList());
            if (clanMembersSteamIdList.IsNullOrEmpty()) return;

            ClanTeamUpdateAllCodeLockAuthShare(clanMembersSteamIdList);
        }

        private void UpdateAllCodeLockAuthShareByTeamMembers(List<ulong>? memberList)
        {
            ClanTeamUpdateAllCodeLockAuthShare(memberList);
        }

        private void ClanTeamUpdateAllCodeLockAuthShare(List<ulong>? memberList)
        {
            if (memberList == null || memberList.IsNullOrEmpty()) return;

            var allCodeLockList = BaseNetworkable.serverEntities
                .Where(se =>
                    se is CodeLock cl &&
                    cl.OwnerID.IsSteamId() &&
                    memberList.Contains(cl.OwnerID))
                .Cast<BaseEntity>()
                .ToList();

            allCodeLockList.AddRange(BaseNetworkable.serverEntities
                .Where(se =>
                    se is ModularCar be && (
                        be.ShortPrefabName.Contains(ModuleCarPartialShortPrefabName) ||
                        be.PrefabName.StartsWith(CarChassisModuleCarPartialPrefabName, StringComparison.OrdinalIgnoreCase)
                    ) &&
                    be.OwnerID.IsSteamId() &&
                    memberList.Contains(be.OwnerID))
                .Cast<BaseEntity>()
                .ToList());

            foreach (var playerSteamId in memberList)
            {
                if (!playerSteamId.IsSteamId())
                    return;

                var player = BasePlayer.FindAwakeOrSleeping(playerSteamId.ToString());
                if (player == null || player.IsNpc) return;

                _coroutines.Add(ServerMgr.Instance.StartCoroutine(UpdateAllCodeLockAuthShareOrSettings(allCodeLockList, player)));
            }
        }

        private IEnumerator UpdateAllCodeLockAuthShareOrSettings(List<BaseEntity> baseEntityList, BasePlayer basePlayer,
            string newLockCode = "", bool isGuestCode = false, bool removeGuestCode = false, bool sendMessage = false, bool sendSharedAuthMessage = false)
        {
            var count = 0;
            foreach (var baseEntity in baseEntityList)
            {
                if (baseEntity == null || !IsEntityOwner(baseEntity, basePlayer)) continue;

                switch (baseEntity)
                {
                    case ModularCar modularCar:
                        if (!modularCar.CarLock.HasALock) continue;
                        UpdateCodeLockAuthShareOrSettings(null, modularCar, basePlayer, newLockCode, isGuestCode, removeGuestCode);
                        break;

                    case SteeringWheel steeringWheel:
                        if (isGuestCode) continue;
                        SteeringWheelApplySettings(basePlayer, steeringWheel, newCode: newLockCode);
                        break;

                    case CodeLock codeLock:
                        if (!codeLock.OwnerID.IsSteamId()) continue;
                        UpdateCodeLockAuthShareOrSettings(codeLock, null, basePlayer, newLockCode, isGuestCode, removeGuestCode);
                        break;
                    default:
                        continue;
                }

                if (++count % CoroutineIteratorsPerFrame == 0)
                    yield return CoroutineEx.waitForEndOfFrame;
            }

            var userId = basePlayer.UserIDString;
            if (sendMessage)
            {
                SendMessage(basePlayer,
                    removeGuestCode
                        ? Lang("AllGuestCodeLockPinRemovedMessage", userId)
                        : isGuestCode
                            ? Lang("AllGuestCodeLockPinChangedMessage", userId)
                            : Lang("AllCodeLockPinChangedMessage", userId));
            }

            if (sendSharedAuthMessage)
            {
                SendMessage(basePlayer, Lang("AllCodeLockSharedAuthPinChangedMessage", userId));
            }
        }

        private void UpdateCodeLockAuthShareOrSettings(CodeLock? codeLock, ModularCar? modularCar, BasePlayer basePlayer,
            string newLockCode = "", bool isGuestCode = false, bool removeGuestCode = false, bool sendMessage = false)
        {
            if (codeLock == null && modularCar == null)
                return;

            BaseEntity baseCodeLock = codeLock != null ? codeLock : modularCar!;
            if (baseCodeLock == null || !IsEntityOwner(baseCodeLock, basePlayer)) return;

            if (_config.AutoLockConfiguration.UseOnlyKeyLock)
                return;

            NextFrame(() =>
            {
                AutoLockPlayerSettingsData? autoLockPlayerSettings;
                bool isAllowed;
                var userId = basePlayer.UserIDString;

                switch (baseCodeLock)
                {
                    case ModularCar _modularCar when !isGuestCode:

                        var modularCarWhitelistPlayers = _modularCar.CarLock.WhitelistPlayers.ToList();
                        isAllowed = modularCarWhitelistPlayers.Contains(_modularCar.OwnerID);

                        foreach (var modularCarWhitelistPlayer in modularCarWhitelistPlayers)
                        {
                            _modularCar.CarLock.TryRemovePlayer(modularCarWhitelistPlayer);
                        }

                        if (!string.IsNullOrWhiteSpace(newLockCode))
                        {
                            _modularCar.CarLock.TrySetNewCode(newLockCode, basePlayer.userID);
                        }

                        _modularCar.CarLock.TryAddPlayer(basePlayer.userID);
                        _modularCar.SendNetworkUpdateImmediate();

                        if (sendMessage)
                        {
                            SendMessage(basePlayer, Lang("CodeLockPinChangedMessage", userId));
                        }

                        autoLockPlayerSettings = GetAutoLockPlayerSettingsData(basePlayer.userID.Get());
                        if (CheckPlayerHasShareLocksWithClanTeamEnabled(autoLockPlayerSettings.SteamID))
                        {
                            var clanInfo = GetClanInfo(basePlayer);
                            if (clanInfo == null) return;

                            if (!clanInfo.ClanMemberUserIdList.IsNullOrEmpty())
                            {
                                var clanMemberUserIdList = clanInfo.ClanMemberUserIdList.Select(ulong.Parse).ToList();
                                foreach (var clanMember in clanMemberUserIdList)
                                {
                                    _modularCar.CarLock.TryAddPlayer(clanMember);
                                }
                            }
                        }

                        if (isAllowed)
                            _modularCar.CarLock.TryAddPlayer(_modularCar.OwnerID);

                        _modularCar.CarLock.TryAddPlayer(basePlayer.userID);
                        _modularCar.SendNetworkUpdateImmediate();

                        break;

                    case CodeLock _codeLock:

                        isAllowed = _codeLock.whitelistPlayers.Contains(_codeLock.OwnerID);

                        _codeLock.guestPlayers.Clear();
                        _codeLock.whitelistPlayers.Clear();

                        if (!string.IsNullOrWhiteSpace(newLockCode) && !isGuestCode)
                        {
                            _codeLock.whitelistPlayers.Clear();
                            _codeLock.whitelistPlayers.Add(basePlayer.userID);
                            _codeLock.code = newLockCode;
                        }

                        if (!string.IsNullOrWhiteSpace(newLockCode) && isGuestCode)
                        {
                            _codeLock.guestCode = newLockCode;
                            _codeLock.hasGuestCode = isGuestCode;
                        }

                        if (removeGuestCode)
                        {
                            _codeLock.guestCode = string.Empty;
                            _codeLock.hasGuestCode = false;
                        }

                        if (!_codeLock.whitelistPlayers.Contains(basePlayer.userID.Get()))
                        {
                            _codeLock.whitelistPlayers.Add(basePlayer.userID);
                        }

                        _codeLock.SendNetworkUpdateImmediate();

                        if (sendMessage)
                        {
                            SendMessage(basePlayer, Lang("CodeLockPinChangedMessage", userId));
                        }

                        autoLockPlayerSettings = GetAutoLockPlayerSettingsData(basePlayer.userID.Get());
                        if (CheckPlayerHasShareLocksWithClanTeamEnabled(autoLockPlayerSettings.SteamID))
                        {
                            var clanInfo = GetClanInfo(basePlayer);
                            if (clanInfo == null) return;

                            if (!clanInfo.ClanMemberUserIdList.IsNullOrEmpty())
                            {
                                _codeLock.whitelistPlayers.AddRange(clanInfo.ClanMemberUserIdList.Select(ulong.Parse).ToList());
                            }
                        }

                        if (isAllowed)
                            _codeLock.whitelistPlayers.Add(_codeLock.OwnerID);

                        if (!_codeLock.whitelistPlayers.Contains(basePlayer.userID.Get()))
                        {
                            _codeLock.whitelistPlayers.Add(basePlayer.userID);
                        }

                        _codeLock.SendNetworkUpdateImmediate();

                        break;
                }
            });
        }

        //TODO DA RIMUOVERE
        // private static bool IsPlayerEntity(BaseLock baseLock)
        // {
        //     if (baseLock == null)
        //         return false;
        //     return baseLock != null && baseLock.OwnerID.IsSteamId();
        // }

        private bool IsInvisible(BasePlayer player)
        {
            if (player == null) return false;
            return player.IsFlying || (Vanish?.Call<bool>("IsInvisible", player) ?? false);
        }

        private static bool IsVendingMachineOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is VendingMachine vendingMachine)
            {
                return vendingMachine.PlayerInfront(player);
            }

            return false;
        }

        private static bool IsDropBoxOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is DropBox dropBox)
            {
                return dropBox.PlayerInfront(player);
            }

            return false;
        }

        private static bool IsShopFrontCustomerPosOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is ShopFront shopFront)
            {
                return shopFront.PlayerInCustomerPos(player);
            }

            return false;
        }

        // TODO metodo da rivedere completamente
        private void EntityDeployed(BaseLock? baseLock, ModularCar? modularCar, SteeringWheel? steeringWheel, BasePlayer ownerPlayer)
        {
            if ((baseLock != null || modularCar != null || steeringWheel != null) && ownerPlayer == null ||
                !ownerPlayer.userID.IsSteamId()) return;

            BaseEntity codeLockEntity = baseLock != null ? baseLock : modularCar != null ? modularCar : steeringWheel!;

            _data.LockEntityData.TryGetValue(ownerPlayer.userID, out var lockEntityDataList);
            if (lockEntityDataList == null)
            {
                _data.LockEntityData.Add(ownerPlayer.userID, new List<LockEntityData>());
            }

            var teleportCoordinate = CoordinateForTeleport(codeLockEntity.transform.position);
            var currentDateTime = CurrentDateTime();
            var parentEntity = codeLockEntity.GetParentEntity();

            _data.LockEntityData[ownerPlayer.userID].Add(new LockEntityData
            {
                OwnerId = ownerPlayer.userID,
                PlayerName = ownerPlayer.displayName,
                NetId = codeLockEntity.net.ID.Value,
                ShortPrefabName = codeLockEntity.ShortPrefabName,
                ParentEntityNetId = parentEntity != null && parentEntity.net != null ? parentEntity.net.ID.Value : null,
                ParentEntity = parentEntity != null ? parentEntity.ShortPrefabName : "",
                ScaleSize = (codeLockEntity is BaseLock ? GetLockableEntityConfigForEntity(parentEntity)?.ScaleSize : null) ?? DefaultScaleSize,
                DeployCoordinate = CoordinateFromPosition(codeLockEntity.transform.position),
                TeleportCoordinate = $"teleportpos {teleportCoordinate}",
                DeployDate = currentDateTime,
                DeployTimestamp = (ulong)currentDateTime.Subtract(_epochDateTime).TotalMilliseconds
            });
            SaveData();
        }

        private void EntityRemoved(BaseLock? baseLock, ModularCar? modularCar, BasePlayer ownerPlayer)
        {
            if ((baseLock != null || modularCar != null) && ownerPlayer == null ||
                !ownerPlayer.userID.IsSteamId()) return;

            BaseEntity codeLockEntity = baseLock != null ? baseLock : modularCar!;

            if (!_data.LockEntityData.TryGetValue(ownerPlayer.userID, out var lockEntityDataList))
                return;

            if (lockEntityDataList.RemoveAll(cl => cl.NetId.Equals(codeLockEntity.net.ID.Value)) == 0)
                return;

            SaveData();
        }

        /// <summary>
        /// Aggiunge o aggiorna la configurazione del Door Closer per una
        /// <see cref="Door"/> in <see cref="_data.DoorCloserEntityData"/>.
        /// </summary>
        /// <remarks>
        /// Se esiste già una entry per la door:
        /// <list type="bullet">
        ///   <item><description>
        ///     Aggiorna <c>DelayTime</c> solo se <paramref name="delayTime"/> è
        ///     maggiore di zero — altrimenti lo lascia invariato.
        ///   </description></item>
        ///   <item><description>
        ///     Reimposta sempre <c>IsAutoClosingDisabled</c> a <c>false</c>.
        ///   </description></item>
        /// </list>
        /// Se non esiste, crea una nuova entry con tutti i dati incluse le
        /// coordinate di deploy e di teleport.
        /// <para>
        /// La ricerca viene eseguita direttamente tramite <paramref name="ownerId"/>
        /// — O(1) sul dizionario + O(n) sulla lista del solo player.
        /// </para>
        /// </remarks>
        /// <param name="ownerId">
        ///   Lo SteamID del player proprietario della door. Se non valido,
        ///   il metodo non esegue alcuna operazione.
        /// </param>
        /// <param name="door">
        ///   La <see cref="Door"/> su cui configurare il Door Closer. Se <c>null</c>,
        ///   senza <c>net</c> o con <c>OwnerID</c> non valido, il metodo non
        ///   esegue alcuna operazione.
        /// </param>
        /// <param name="delayTime">
        ///   Il ritardo in secondi prima che il Door Closer chiuda la door.
        ///   Se minore o uguale a zero, viene comunque creata una nuova entry.
        ///   Se minore o uguale a zero su una entry esistente, il <c>DelayTime</c>
        ///   non viene modificato. Su una nuova entry, viene salvato così com'è.
        /// </param>
        private void AddOrUpdateDoorCloserData(ulong ownerId, Door door, int delayTime)
        {
            if (!ownerId.IsSteamId()) return;
            if (door?.net is null || !door.OwnerID.IsSteamId()) return;

            if (!_data.DoorCloserEntityData.TryGetValue(ownerId, out var list))
            {
                list = new List<DoorCloserEntityData>();
                _data.DoorCloserEntityData[ownerId] = list;
            }

            // Aggiorna entry esistente
            var existing = list.FirstOrDefault(dc => dc.EntityNetId == door.net.ID.Value);
            if (existing is not null)
            {
                if (delayTime > 0)
                    existing.DelayTime = delayTime;

                existing.IsAutoClosingDisabled = false;
                SaveData();
                return;
            }

            // Aggiunge nuova entry
            var currentDateTime = CurrentDateTime();
            var teleportCoordinate = CoordinateForTeleport(door.transform.position);

            list.Add(new DoorCloserEntityData
            {
                OwnerId = ownerId,
                EntityNetId = door.net.ID.Value,
                EntityPrefabID = door.prefabID,
                DelayTime = delayTime,
                IsAutoClosingDisabled = false,
                DeployCoordinate = CoordinateFromPosition(door.transform.position),
                TeleportCoordinate = $"teleportpos {teleportCoordinate}",
                DeployDate = currentDateTime,
                DeployTimestamp = (ulong)currentDateTime.Subtract(_epochDateTime).TotalMilliseconds
            });

            SaveData();
        }

        /// <summary>
        /// Aggiorna i dati di chiusura automatica in <see cref="_data.DoorCloserEntityData"/>
        /// per tutte le <see cref="Door"/> di un player, opzionalmente filtrate per categoria.
        /// </summary>
        /// <remarks>
        /// Recupera le door del player tramite <see cref="GetDoorEntitiesGroupedByOwner"/>
        /// e aggiorna o crea i dati per ciascuna tramite <see cref="AddOrUpdateDoorCloserData"/>.
        /// <para>
        /// Il delay viene ricavato dalle impostazioni del player tramite
        /// <see cref="GetDoorCloserDelayTimeByPlayerSettings"/> per ogni door.
        /// Se il delay risultante è minore o uguale a zero, la door viene saltata.
        /// </para>
        /// <para>
        /// Se <paramref name="autoClosingType"/> è specificato, aggiorna solo le door
        /// della categoria indicata — utile quando il player modifica il delay di una
        /// categoria specifica tramite UI.
        /// Se non specificato, aggiorna tutte le categorie del player.
        /// </para>
        /// </remarks>
        /// <param name="userId">
        ///   SteamID del player di cui aggiornare i dati.
        ///   Se non valido, il metodo non esegue alcuna operazione.
        /// </param>
        /// <param name="autoClosingType">
        ///   Categoria di door da aggiornare. Se <c>null</c> o
        ///   <see cref="AutoClosingType.None"/>, vengono aggiornate tutte le categorie.
        /// </param>
        /// <param name="sendMessage">
        ///   Se <c>true</c> e almeno una door è stata sincronizzata, invia un toast
        ///   al player con il numero di door aggiornate.
        /// </param>
        private void ResyncDoorCloserData(ulong userId, AutoClosingType? autoClosingType = null, bool sendMessage = false)
        {
            if (!userId.IsSteamId()) return;

            var doorGroups = GetDoorEntitiesGroupedByOwner(userId, autoClosingType);
            if (doorGroups.Count == 0) return;

            var synced = 0;
            foreach (var group in doorGroups)
            {
                foreach (var door in group)
                {
                    var delayTime = GetDoorCloserDelayTimeByPlayerSettings(userId, door);
                    if (delayTime <= 0) continue;

                    AddOrUpdateDoorCloserData(userId, door, delayTime);
                    synced++;
                }
            }

            if (!sendMessage || synced == 0) return;

            var userIdString = userId.ToString();
            timer.Once(1f, () => PlayerShowToast(userId, Lang("DoorCloserListMessage", userIdString,
                Lang("LockerStatus_Updated", userIdString), synced.ToString())));
        }

        /// <summary>
        /// Rimuove la configurazione Door Closer di una <see cref="Door"/>
        /// da <see cref="_data.DoorCloserEntityData"/> e distrugge il
        /// timer attivo se presente in <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <remarks>
        /// Se non esiste una entry per la door, il metodo non esegue alcuna operazione.
        /// </remarks>
        /// <param name="door">
        ///   La <see cref="Door"/> di cui rimuovere la configurazione. Se <c>null</c>,
        ///   senza <c>net</c> o con <c>OwnerID</c> non valido, il metodo non
        ///   esegue alcuna operazione.
        /// </param>
        private void RemoveDoorCloserData(Door? door)
        {
            if (door?.net is null || !door.OwnerID.IsSteamId()) return;

            var ownerId = door.OwnerID;
            var netId = door.net.ID.Value;

            // Distrugge il timer attivo e rimuove la entry
            if (_doorCloserTimers.TryGetValue(ownerId, out var doors) && doors.TryGetValue(netId, out var entry))
            {
                entry.Timer?.Destroy();
                doors.Remove(netId);

                if (doors.Count == 0)
                    _doorCloserTimers.Remove(ownerId);
            }

            // Rimuove la configurazione persistente e salva solo se necessario
            if (_data.DoorCloserEntityData.TryGetValue(ownerId, out var list) &&
                list.RemoveAll(d => d.EntityNetId == netId) > 0)
                SaveData();
        }

        /// <summary>
        /// Verifica se un'entità ha una configurazione Door Closer salvata
        /// in <see cref="_data.DoorCloserEntityData"/>.
        /// </summary>
        /// <remarks>
        /// La ricerca viene eseguita direttamente tramite <c>OwnerID</c> dell'entità.
        /// </remarks>
        /// <param name="entity">
        ///   L'entità da verificare. Se <c>null</c>, senza <c>net</c> o con
        ///   <c>OwnerID</c> non valido, il metodo restituisce <c>false</c>.
        /// </param>
        /// <returns>
        ///   <c>true</c> se l'entità ha una configurazione Door Closer salvata;
        ///   <c>false</c> altrimenti.
        /// </returns>
        private bool HasDoorCloserData(BaseEntity? entity)
        {
            if (entity?.net is null || !entity.OwnerID.IsSteamId()) return false;

            if (!_data.DoorCloserEntityData.TryGetValue(entity.OwnerID, out var list)) return false;

            return list.Any(d => d.EntityNetId == entity.net.ID.Value);
        }

        /// <summary>
        /// Cerca il <see cref="DoorCloserEntityData"/> associato all'entità
        /// nei dati salvati in <see cref="_data.DoorCloserEntityData"/>.
        /// </summary>
        /// <param name="entity">
        ///   L'entità da cercare. Se <c>null</c> o senza <c>net</c>,
        ///   il metodo restituisce <c>null</c>.
        /// </param>
        /// <returns>
        ///   Il <see cref="DoorCloserEntityData"/> configurato, oppure
        ///   <c>null</c> se non trovato.
        /// </returns>
        private DoorCloserEntityData? GetDoorCloserData(BaseEntity? entity)
        {
            if (entity?.net is null || !entity.OwnerID.IsSteamId()) return null;

            if (!_data.DoorCloserEntityData.TryGetValue(entity.OwnerID, out var list)) return null;

            return list.FirstOrDefault(dc => dc.EntityNetId == entity.net.ID.Value);
        }

        /// <summary>
        /// Verifica se la <paramref name="door"/> ha un Door Closer registrato in <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <param name="door">La porta da verificare.</param>
        /// <returns>
        /// <c>true</c> se esiste una entry in <see cref="_doorCloserTimers"/> per il proprietario della porta
        /// e il <see cref="NetworkableId"/> corrisponde; <c>false</c> altrimenti.
        /// </returns>
        public bool HasDoorCloser(Door door)
        {
            if (door == null || door.net == null) return false;
            return _doorCloserTimers.TryGetValue(door.OwnerID, out var doors) && doors.ContainsKey(door.net.ID.Value);
        }

        /// <summary>
        /// Verifica se una <see cref="Door"/> ha un timer Door Closer attivo
        /// in <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <remarks>
        /// Un timer è considerato attivo se è presente nella entry, non è <c>null</c>
        /// e ha un <c>Delay</c> maggiore di zero.
        /// </remarks>
        /// <param name="door">
        ///   La <see cref="Door"/> da verificare. Se <c>null</c>, senza <c>net</c>
        ///   o con <c>OwnerID</c> non valido, il metodo restituisce <c>false</c>.
        /// </param>
        /// <returns>
        ///   <c>true</c> se la door ha un timer Door Closer attivo;
        ///   <c>false</c> altrimenti.
        /// </returns>
        private bool HasDoorCloserActiveTimer(Door? door)
        {
            if (door?.net is null || !door.OwnerID.IsSteamId()) return false;

            return _doorCloserTimers.TryGetValue(door.OwnerID, out var doors) &&
                   doors.TryGetValue(door.net.ID.Value, out var entry) &&
                   entry.Timer is not null &&
                   entry.Timer.Delay > 0;
        }

        /// <summary>
        /// Crea e registra il timer di chiusura automatica per una <see cref="Door"/>
        /// in <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <remarks>
        /// Il flusso di esecuzione è il seguente:
        /// <list type="number">
        ///   <item><description>
        ///     Verifica l'ownership tramite <see cref="EnsureDoorOwnership"/> —
        ///     per le <see cref="LegacyShelterDoor"/> prive di owner tenta di ereditarlo
        ///     dal parent <see cref="LegacyShelter"/>.
        ///   </description></item>
        ///   <item><description>
        ///     Recupera o crea il dizionario delle porte del player in <see cref="_doorCloserTimers"/>
        ///     prima di qualsiasi altro check — il dizionario serve in tutti i branch
        ///     e il <c>TryGetValue</c> viene eseguito una sola volta.
        ///   </description></item>
        ///   <item><description>
        ///     Distrugge l'eventuale timer attivo e reimposta l'entry a <c>Timer = null</c>
        ///     prima di qualsiasi altro check — il timer va sempre fermato
        ///     indipendentemente dall'esito dei controlli successivi.
        ///   </description></item>
        ///   <item><description>
        ///     Verifica tramite <see cref="CanAutoCloseDoor"/> che la chiusura automatica
        ///     sia abilitata per il player e la categoria della door — controlla in sequenza:
        ///     permesso, <see cref="AutoClosingPlayerSettingsData.AutoClosingEnabled"/> e
        ///     categoria della door abilitata nelle impostazioni del player.
        ///     Se almeno uno dei controlli fallisce, mantiene l'entry con <c>Timer = null</c> e termina.
        ///   </description></item>
        ///   <item><description>
        ///     Se i dati del Door Closer non esistono, li crea automaticamente
        ///     tramite <see cref="AddOrUpdateDoorCloserData"/> usando il delay ricavato da
        ///     <see cref="GetDoorCloserDelayTimeByPlayerSettings"/> come valore iniziale.
        ///   </description></item>
        ///   <item><description>
        ///     Verifica <see cref="DoorCloserEntityData.IsAutoClosingDisabled"/> —
        ///     se la chiusura automatica è disabilitata manualmente per la door specifica, termina.
        ///   </description></item>
        ///   <item><description>
        ///     Risolve il <paramref name="delayTime"/> finale nell'ordine:
        ///     parametro → <c>DelayTime</c> salvato in <see cref="DoorCloserEntityData"/> →
        ///     <see cref="GetDoorCloserDelayTimeByPlayerSettings"/> come fallback lazy.
        ///     Il fallback viene invocato solo se nessuna delle sorgenti precedenti
        ///     produce un valore valido (≥ 1).
        ///   </description></item>
        ///   <item><description>
        ///     Se il delay risultante è ≤ 0 o la skin è in blacklist tramite
        ///     <see cref="IsSkinBlacklisted"/>, mantiene l'entry con <c>Timer = null</c>
        ///     senza avviare il timer.
        ///   </description></item>
        /// </list>
        /// <para>
        /// Se al momento dell'esecuzione la door risulta distrutta, i suoi dati vengono
        /// rimossi automaticamente tramite <see cref="RemoveDoorCloserData"/>.
        /// </para>
        /// </remarks>
        /// <param name="userID">
        ///   Lo SteamID del player proprietario della door.
        ///   Se non valido, il metodo non esegue alcuna operazione.
        /// </param>
        /// <param name="door">
        ///   La <see cref="Door"/> su cui applicare il timer di chiusura automatica.
        ///   Se <c>null</c>, senza <c>net</c> o con <c>OwnerID</c> non risolvibile,
        ///   il metodo non esegue alcuna operazione.
        /// </param>
        /// <param name="delayTime">
        ///   Ritardo in secondi prima della chiusura automatica. Se ≤ 0, viene ricavato
        ///   dai dati salvati o dalle impostazioni del player. Default: <c>0</c>.
        /// </param>
        private void AddDoorCloserTimer(ulong userID, Door door, int delayTime = 0)
        {
            if (!userID.IsSteamId()) return;
            if (!EnsureDoorOwnership(door)) return;
            if (door.net is null) return;

            var ownerId = door.OwnerID;
            var netId = door.net.ID.Value;

            // Recupera o crea il dizionario delle porte del player — serve in tutti i branch
            if (!_doorCloserTimers.TryGetValue(ownerId, out var doors))
                _doorCloserTimers[ownerId] = doors = new Dictionary<ulong, (Timer? Timer, uint PrefabID)>();

            // Distrugge il timer attivo e reimposta l'entry a null —
            // va sempre fermato indipendentemente dall'esito dei controlli successivi
            if (doors.TryGetValue(netId, out var existing))
                existing.Timer?.Destroy();
            doors[netId] = (null, door.prefabID);

            // Controlla permesso, AutoClosingEnabled e categoria abilitata in un'unica chiamata
            if (!CanAutoCloseDoor(ownerId, door)) return;

            // Recupera o crea il doorCloserData —
            // GetDoorCloserDelayTimeByPlayerSettings è calcolato qui solo se i dati non esistono ancora
            var doorCloserData = GetDoorCloserData(door);
            if (doorCloserData is null)
            {
                var settingsDelayTime = GetDoorCloserDelayTimeByPlayerSettings(userID, door);
                AddOrUpdateDoorCloserData(ownerId, door, settingsDelayTime);
                doorCloserData = GetDoorCloserData(door);
                if (doorCloserData is null) return;
            }

            // Chiusura automatica disabilitata manualmente per questa door
            // AutoClosingEnabled è già garantito true da CanAutoCloseDoor
            if (doorCloserData.IsAutoClosingDisabled) return;

            // Risoluzione lazy del delay: parametro → dati salvati → impostazioni player come fallback
            // GetDoorCloserDelayTimeByPlayerSettings viene invocato solo se parametro e dati salvati sono ≤ 0
            if (delayTime <= 0)
                delayTime = doorCloserData.DelayTime > 0
                    ? doorCloserData.DelayTime
                    : GetDoorCloserDelayTimeByPlayerSettings(userID, door);

            // Delay non valido o skin in blacklist — entry senza timer attivo
            if (delayTime <= 0 || IsSkinBlacklisted(SkinBlacklistType.DoorCloser, door)) return;

            doors[netId] = (
                timer.Once(delayTime, () =>
                {
                    if (door.IsDestroyed)
                    {
                        RemoveDoorCloserData(door);
                        return;
                    }

                    if (!door.IsOpen()) return;

                    // L'invio immediato qui sotto resta com'era: lo scope manda l'aggiornamento in
                    // coda, esattamente come faceva il vecchio SetFlag, e la riga successiva
                    // continua a forzarne uno immediato.
                    using (var flags = door.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Open, false);

                    door.SendNetworkUpdateImmediate();
                }),
                door.prefabID
            );
        }

        /// <summary>
        /// Distrugge il timer di chiusura automatica di una <see cref="Door"/>
        /// e azzera il riferimento <c>Timer</c> nell'entry di
        /// <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <remarks>
        /// <c>Timer.Destroy()</c> non azzera automaticamente il riferimento —
        /// l'entry viene aggiornata esplicitamente con <c>Timer = null</c> per
        /// evitare riferimenti a timer distrutti.
        /// Se non esiste un'entry per la door o il <c>NetId</c> non corrisponde,
        /// il metodo non esegue alcuna operazione.
        /// </remarks>
        /// <param name="door">
        ///   La <see cref="Door"/> di cui distruggere il timer. Se <c>null</c>,
        ///   senza <c>net</c> o con <c>OwnerID</c> non valido, il metodo non
        ///   esegue alcuna operazione.
        /// </param>
        private void DestroyDoorCloserTimer(Door? door)
        {
            if (door?.net is null || !door.OwnerID.IsSteamId()) return;

            if (!_doorCloserTimers.TryGetValue(door.OwnerID, out var doors)) return;

            var netId = door.net.ID.Value;
            if (!doors.TryGetValue(netId, out var entry)) return;

            entry.Timer?.Destroy();
            doors[netId] = (null, entry.PrefabID);
        }

        /// <summary>
        /// Garantisce che <paramref name="door"/> abbia un <see cref="BaseEntity.OwnerID"/> valido (Steam ID).
        /// <para>
        /// Se la porta è una <see cref="LegacyShelterDoor"/> priva di owner, tenta di ereditare
        /// l'<see cref="BaseEntity.OwnerID"/> dal parent <see cref="LegacyShelter"/> e propaga
        /// l'aggiornamento via rete.
        /// </para>
        /// </summary>
        /// <param name="door">La porta su cui verificare o correggere l'ownership.</param>
        /// <returns>
        /// <c>true</c> se la porta possiede (o ha acquisito) un owner valido; <c>false</c> se
        /// <paramref name="door"/> è <c>null</c> o non è stato possibile risolvere un owner.
        /// </returns>
        private static bool EnsureDoorOwnership(Door door)
        {
            if (door == null) return false;
            if (door.OwnerID.IsSteamId()) return true;

            if (door is LegacyShelterDoor legacyShelterDoor
                && door.GetParentEntity() is LegacyShelter { OwnerID: var shelterOwner }
                && shelterOwner.IsSteamId())
            {
                legacyShelterDoor.OwnerID = shelterOwner;
                legacyShelterDoor.SendNetworkUpdateImmediate();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Recupera tutte le <see cref="Door"/> nel mondo che hanno un tipo di
        /// chiusura automatica valido, opzionalmente filtrate per player, categoria
        /// e tipi da escludere.
        /// </summary>
        /// <remarks>
        /// <see cref="AutoClosingTypeByEntity"/> viene chiamato una sola volta per
        /// door tramite proiezione intermedia, evitando la doppia valutazione che
        /// si avrebbe con due chiamate separate nel predicato <c>Where</c>.
        /// <para>
        /// Le door vengono raggruppate per <c>OwnerID</c> — permette di chiamare
        /// <see cref="GetAutoClosingPlayerSettingsData"/> una sola volta per player
        /// nei metodi che iterano i gruppi, invece di una per ogni door.
        /// </para>
        /// </remarks>
        /// <param name="userId">
        ///   SteamID del player di cui filtrare le door. Se <c>null</c>,
        ///   vengono restituite le door di tutti i player.
        /// </param>
        /// <param name="autoClosingType">
        ///   Categoria di door da includere. Se <c>null</c> o
        ///   <see cref="AutoClosingType.None"/>, vengono restituite tutte le
        ///   categorie valide.
        /// </param>
        /// <param name="excludeTypes">
        ///   Insieme di categorie da escludere dal risultato. Se <c>null</c>,
        ///   nessuna categoria viene esclusa oltre a <see cref="AutoClosingType.None"/>
        ///   che viene sempre esclusa.
        /// </param>
        /// <returns>
        ///   Lista di gruppi di <see cref="Door"/> raggruppate per <c>OwnerID</c>.
        ///   Lista vuota se nessuna door corrisponde ai filtri.
        /// </returns>
        private List<IGrouping<ulong, Door>> GetDoorEntitiesGroupedByOwner(ulong? userId = null, AutoClosingType? autoClosingType = null, HashSet<AutoClosingType>? excludeTypes = null)
        {
            var filterByPlayer = userId.HasValue && userId.Value.IsSteamId();
            var filterByType = autoClosingType.HasValue && autoClosingType != AutoClosingType.None;

            return BaseNetworkable.serverEntities
                .OfType<Door>()
                .Where(d => d.OwnerID.IsSteamId() && (!filterByPlayer || d.OwnerID == userId!.Value))
                // Proiezione intermedia — AutoClosingTypeByEntity chiamato una sola volta per door
                .Select(d => (Door: d, Type: AutoClosingTypeByEntity(d)))
                .Where(x =>
                    x.Type != AutoClosingType.None &&
                    (!filterByType || x.Type == autoClosingType) &&
                    (excludeTypes is null || !excludeTypes.Contains(x.Type)))
                .GroupBy(x => x.Door.OwnerID, x => x.Door)
                .ToList();
        }

        /// <summary>
        /// Coroutine che aggiorna i timer di chiusura automatica per una lista
        /// di <see cref="Door"/>, inserendoli in <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <remarks>
        /// Per ogni door verifica tramite <see cref="CanAutoCloseDoor"/> che la
        /// chiusura automatica sia abilitata — se non lo è, distrugge il timer
        /// tramite <see cref="DestroyDoorCloserTimer"/> e passa alla door successiva.
        /// Se abilitata, delega la creazione del timer a <see cref="AddDoorCloserTimer"/>,
        /// che esegue internamente un secondo controllo <see cref="CanAutoCloseDoor"/>
        /// come guard difensivo. La doppia chiamata è intenzionale: i due siti hanno
        /// responsabilità distinte — routing e conteggio <c>synced</c> qui,
        /// validazione atomica dentro <see cref="AddDoorCloserTimer"/>.
        /// <para>
        /// Le door con <c>net</c> <c>null</c> vengono saltate con un guard esplicito,
        /// senza ricorrere al meccanismo try-catch per le NRE prevedibili; il contatore
        /// di frame viene comunque aggiornato per mantenere il batching corretto.
        /// </para>
        /// <para>
        /// Se i controlli passano, il delay viene ricavato nell'ordine da:
        /// <list type="number">
        ///   <item><description>
        ///     <c>DelayTime</c> salvato in <see cref="_data.DoorCloserEntityData"/>,
        ///     se presente e maggiore di zero.
        ///   </description></item>
        ///   <item><description>
        ///     <see cref="GetDoorCloserDelayTimeByPlayerSettings"/> come fallback,
        ///     tramite <see cref="AddDoorCloserTimer"/>.
        ///   </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// La coroutine si suddivide in batch tramite
        /// <see cref="CoroutineIteratorsPerFrame"/> e
        /// <see cref="CoroutineEx.waitForEndOfFrame"/> per evitare picchi di CPU.
        /// </para>
        /// <para>
        /// Se <paramref name="player"/> è specificato, è ancora connesso e almeno un
        /// timer è stato aggiornato, viene mostrata una notifica tramite
        /// <see cref="PlayerShowToast"/> dopo 1 secondo.
        /// </para>
        /// </remarks>
        /// <param name="player">
        ///   Il player a cui inviare la notifica di aggiornamento.
        ///   Se <c>null</c>, nessuna notifica viene inviata.
        /// </param>
        /// <param name="doorList">
        ///   La lista di <see cref="Door"/> di cui aggiornare i timer.
        ///   Se <c>null</c> o vuota, il metodo non esegue alcuna operazione.
        /// </param>
        private IEnumerator UpdateDoorCloserTimers(BasePlayer? player, List<Door> doorList)
        {
            if (doorList.IsNullOrEmpty()) yield break;

            var count = 0;
            var synced = 0;

            foreach (var door in doorList)
            {
                // Guard esplicito: evita NRE silenziose nel catch senza interrompere il batching
                if (door.net != null)
                {
                    try
                    {
                        var ownerId = door.OwnerID;

                        // Verifica se la chiusura automatica è abilitata — distrugge il timer se non lo è
                        if (!CanAutoCloseDoor(ownerId, door))
                        {
                            // Chiusura automatica disabilitata — rimuove il timer attivo
                            DestroyDoorCloserTimer(door);
                        }
                        else
                        {
                            // Caching del NetId per evitare doppio accesso alla property chain
                            var netId = door.net.ID.Value;

                            // Cerca il delay nei dati salvati — ha priorità sulle impostazioni del player
                            var delayTime = _data.DoorCloserEntityData.TryGetValue(ownerId, out var list)
                                ? list.FirstOrDefault(dc => dc.EntityNetId == netId)?.DelayTime ?? 0
                                : 0;

                            AddDoorCloserTimer(ownerId, door, delayTime);
                            synced++;
                        }
                    }
                    catch (Exception e)
                    {
                        // TODO implementare DebugMode
                        // if (_config.DebugMode)
                        //     PrintError($"[UpdateDoorCloserTimers] ERROR: {e.Message}\nSTACK TRACE: {e.StackTrace}");
                    }
                }

                if (++count % CoroutineIteratorsPerFrame == 0)
                    yield return CoroutineEx.waitForEndOfFrame;
            }

            // Notifica il player solo se l'aggiornamento è stato richiesto dall'UI o da un comando del player
            if (player is not null && synced > 0)
            {
                timer.Once(1f, () =>
                {
                    // Verifica che il player sia ancora connesso — può disconnettersi durante la coroutine
                    if (!player.IsConnected) return;

                    var userIdString = player.UserIDString;
                    PlayerShowToast(player, Lang("DoorCloserListMessage", userIdString,
                        Lang("LockerStatus_Updated", userIdString), synced.ToString()));
                });
            }
        }

        /// <summary>
        /// Esegue il resync dei timer di chiusura automatica per un player,
        /// opzionalmente filtrato per categoria di door.
        /// </summary>
        /// <remarks>
        /// Recupera le door del player tramite <see cref="GetDoorEntitiesGroupedByOwner"/>
        /// e delega l'aggiornamento dei timer a <see cref="UpdateDoorCloserTimers"/>.
        /// <para>
        /// Se <paramref name="autoClosingType"/> è specificato, aggiorna solo le door
        /// della categoria indicata — utile quando il player modifica il delay di una
        /// categoria specifica tramite UI.
        /// Se non specificato, aggiorna tutte le categorie del player.
        /// </para>
        /// <para>
        /// La notifica di aggiornamento viene inviata tramite
        /// <see cref="PlayerShowToast"/> solo se il player è online al momento
        /// del resync.
        /// </para>
        /// </remarks>
        /// <param name="userId">
        ///   SteamID del player di cui eseguire il resync dei timer.
        ///   Se non valido, il metodo non esegue alcuna operazione.
        /// </param>
        /// <param name="autoClosingType">
        ///   Categoria di door da aggiornare. Se <c>null</c> o
        ///   <see cref="AutoClosingType.None"/>, vengono aggiornate tutte le categorie.
        /// </param>
        private void ResyncDoorCloserTimers(ulong userId, AutoClosingType? autoClosingType = null)
        {
            if (!userId.IsSteamId()) return;

            var doorGroups = GetDoorEntitiesGroupedByOwner(userId, autoClosingType);
            if (doorGroups.Count == 0) return;

            var player = BasePlayer.FindByID(userId);

            foreach (var group in doorGroups)
                ServerMgr.Instance.StartCoroutine(UpdateDoorCloserTimers(player, group.ToList()));
        }

        /// <summary>
        /// Avvia il timer di prossimità che esegue <see cref="DoorProximityCheckBatch"/>
        /// ogni <see cref="ProximityCheckInterval"/> secondi.
        /// Da invocare in <c>OnServerInitialized</c>.
        /// </summary>
        private void InitDoorProximity() => _proximityCheckTimer = timer.Every(ProximityCheckInterval, DoorProximityCheckBatch);

        /// <summary>
        /// Ferma il timer di prossimità e svuota lo stato interno.
        /// Da invocare nell'hook <c>Unload</c> per evitare memory leak e timer orfani.
        /// </summary>
        private void DestroyDoorProximity()
        {
            _proximityCheckTimer?.Destroy();
            _nearTrackedDoor.Clear();
        }

        /// <summary>
        /// Rimuove il <paramref name="player"/> da <see cref="_nearTrackedDoor"/> alla disconnessione,
        /// evitando che uno <c>userID</c> obsoleto rimanga nello stato di prossimità.
        /// </summary>
        /// <param name="player">Il player che si è disconnesso.</param>
        private void DoorProximityOnPlayerDisconnected(BasePlayer player) => _nearTrackedDoor.Remove(player.userID);

        /// <summary>
        /// Individua la <see cref="Door"/> con Door Closer attivo con cui il <paramref name="player"/>
        /// intende interagire, usando due strategie in ordine di precisione.
        /// </summary>
        /// <remarks>
        /// Le strategie vengono eseguite in ordine:
        /// <list type="number">
        ///   <item><description>
        ///     <b>Raycast</b> — controlla la porta che il player sta guardando entro
        ///     <see cref="ProximityCheckRadius"/> metri tramite <see cref="Physics.Raycast"/>.
        ///     È la strategia più precisa: corrisponde alla porta con cui il player intende interagire.
        ///   </description></item>
        ///   <item><description>
        ///     <b>Fallback prossimità</b> — usa <see cref="IsNearTrackedDoor"/> per trovare
        ///     una porta entro <see cref="NearProximityCheckRadius"/> metri.
        ///     Copre i casi in cui il raycast non colpisce nulla (angolo di visuale sfavorevole, lag).
        ///   </description></item>
        /// </list>
        /// </remarks>
        /// <param name="player">Il player per cui cercare la porta.</param>
        /// <param name="door">
        ///   La <see cref="Door"/> trovata, oppure <c>null</c> se nessuna porta
        ///   con Door Closer è nelle vicinanze o nel campo visivo del player.
        /// </param>
        /// <returns>
        ///   <c>true</c> se è stata trovata una porta con Door Closer attivo;
        ///   <c>false</c> altrimenti.
        /// </returns>
        private bool TryGetTargetProximityDoor(BasePlayer player, out Door? door)
        {
            door = null;

            // Raycast — porta guardata dal player (più precisa)
            if (Physics.Raycast(player.eyes.HeadRay(), out var hit, ProximityCheckRadius, ProximityDoorLayer, QueryTriggerInteraction.Ignore)
                && hit.GetEntity() is Door lookedDoor
                && HasDoorCloser(lookedDoor))
            {
                door = lookedDoor;
                return true;
            }

            // Fallback — porta nel raggio di prossimità
            return IsNearTrackedDoor(player, out door);
        }

        /// <summary>
        /// Verifica se il <paramref name="player"/> si trova entro <see cref="NearProximityCheckRadius"/> metri
        /// da una <see cref="Door"/> tracciata in <see cref="_doorCloserTimers"/>.
        /// </summary>
        /// <param name="player">Il player da controllare.</param>
        /// <param name="nearestDoor">
        ///   La prima <see cref="Door"/> con Door Closer attivo trovata nel raggio, oppure
        ///   <c>null</c> se nessuna porta tracciata è nelle vicinanze del player.
        /// </param>
        /// <returns>
        ///   <c>true</c> se almeno una porta con Door Closer attivo è entro
        ///   <see cref="NearProximityCheckRadius"/> metri; <c>false</c> altrimenti.
        /// </returns>
        private bool IsNearTrackedDoor(BasePlayer player, out Door? nearestDoor)
        {
            nearestDoor = null;
            var entities = Pool.Get<List<BaseEntity>>();
            try
            {
                Vis.Entities(player.transform.position, NearProximityCheckRadius, entities, ProximityDoorLayer);

                for (var i = 0; i < entities.Count; i++)
                {
                    if (entities[i] is not Door door) continue;
                    if (!HasDoorCloser(door)) continue;

                    nearestDoor = door;
                    return true;
                }

                return false;
            }
            finally
            {
                Pool.FreeUnmanaged(ref entities);
            }
        }

        private DateTime CurrentDateTime()
        {
            try
            {
                return TimeZoneInfo
                    .ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,
                        string.IsNullOrEmpty(_config.TimeZone) ? DefaultTimeZone : _config.TimeZone).DateTime;
            }
            catch
            {
                //return DateTime.Now;
                return DateTime.UtcNow;
            }
        }

        private static string CoordinateFromPosition(Vector3 pos)
        {
            return
                $"X: {pos.x.ToString(CultureInfo.InvariantCulture)} - Y: {pos.y.ToString(CultureInfo.InvariantCulture)} - Z:{pos.z.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string CoordinateForTeleport(Vector3 pos)
        {
            return
                $" {pos.x.ToString(CultureInfo.InvariantCulture)},{pos.y.ToString(CultureInfo.InvariantCulture)},{pos.z.ToString(CultureInfo.InvariantCulture)}";
        }

        private void CmdHandler(IPlayer ipPlayer, string command, string[] arg)
        {
            var playerCmdCaller = ipPlayer?.Object as BasePlayer;
            if (playerCmdCaller == null) return;

            var userId = playerCmdCaller.userID;
            var playerUserIDString = playerCmdCaller.UserIDString;

            if (!permission.UserHasPermission(playerUserIDString, PermissionUse))
            {
                SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                return;
            }

            if (arg.Length == 0) return;

            var action = arg[0].ToLower();
            var isAdmin = permission.UserHasPermission(playerUserIDString, PermissionAdmin);

            var inputCodeLock = 0UL;
            var targetSteamID = 0UL;
            // var isSteamID = arg.Length > 1 && ulong.TryParse(arg[1], out targetSteamID);

            bool isSteamID;
            PlayerInfo? playerInfo;
            BasePlayer? targetBasePlayer;
            List<BaseLock> lockList;
            List<ModularCar> modularCarList;
            List<SteeringWheel> steeringWheelList;

            switch (action)
            {
                case "lockall":
                case "unlockall":

                    //TODO #################### unificare questi controlli ####################
                    if (!isAdmin)
                    {
                        SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                        return;
                    }

                    if (!CanByPassCodeLock(playerCmdCaller))
                    {
                        SendMessage(playerCmdCaller, Lang("NoBypassPermissions", playerUserIDString));
                        return;
                    }
                    //#########################################################################

                    isSteamID = arg.Length >= 2 && ulong.TryParse(arg[1], out targetSteamID);
                    if (!isSteamID || !targetSteamID.IsSteamId())
                    {
                        SendMessage(playerCmdCaller, Lang("InvalidSteamID", playerUserIDString));
                        return;
                    }

                    // TODO implementare un singolo ciclo su BaseNetworkable.serverEntities e poi discriminare in base al tipo concreto per popolare le liste
                    lockList = BaseNetworkable.serverEntities
                        .Where(e => e is BaseLock)
                        .Cast<BaseLock>()
                        .Where(cl => cl.OwnerID.Equals(targetSteamID))
                        .ToList();

                    modularCarList = BaseNetworkable.serverEntities
                        .Where(se =>
                            se is ModularCar be && (
                                be.ShortPrefabName.Contains(ModuleCarPartialShortPrefabName) ||
                                be.PrefabName.StartsWith(CarChassisModuleCarPartialPrefabName, StringComparison.OrdinalIgnoreCase)
                            ) &&
                            be.OwnerID.IsSteamId() &&
                            be.OwnerID.Equals(targetSteamID))
                        .Cast<ModularCar>()
                        .ToList();

                    steeringWheelList = BaseNetworkable.serverEntities
                        .Where(e => e is SteeringWheel)
                        .Cast<SteeringWheel>()
                        .Where(sw => sw.OwnerID.Equals(targetSteamID))
                        .ToList();

                    playerInfo = ConvertToPlayerInfo(playerCmdCaller);
                    _coroutines.Add(ServerMgr.Instance.StartCoroutine(LockSetLocked(playerInfo, lockList, modularCarList, steeringWheelList, action.Equals("lockall"))));

                    break;

                case "removeall":

                    //TODO #################### unificare questi controlli ####################
                    if (!isAdmin)
                    {
                        SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                        return;
                    }

                    if (!CanByPassCodeLock(playerCmdCaller))
                    {
                        SendMessage(playerCmdCaller, Lang("NoBypassPermissions", playerUserIDString));
                        return;
                    }
                    //#########################################################################

                    isSteamID = arg.Length >= 2 && ulong.TryParse(arg[1], out targetSteamID);
                    if (!isSteamID || !targetSteamID.IsSteamId())
                    {
                        SendMessage(playerCmdCaller, Lang("InvalidSteamID", playerUserIDString));
                        return;
                    }

                    // TODO implementare un singolo ciclo su BaseNetworkable.serverEntities e poi discriminare in base al tipo concreto per popolare le liste
                    lockList = BaseNetworkable.serverEntities
                        .Where(e => e is BaseLock)
                        .Cast<BaseLock>()
                        .Where(cl => cl.OwnerID.Equals(targetSteamID))
                        .ToList();

                    modularCarList = BaseNetworkable.serverEntities
                        .Where(se =>
                            se is ModularCar be && (
                                be.ShortPrefabName.Contains(ModuleCarPartialShortPrefabName) ||
                                be.PrefabName.StartsWith(CarChassisModuleCarPartialPrefabName, StringComparison.OrdinalIgnoreCase)
                            ) &&
                            be.OwnerID.IsSteamId() &&
                            be.OwnerID.Equals(targetSteamID))
                        .Cast<ModularCar>()
                        .ToList();

                    steeringWheelList = BaseNetworkable.serverEntities
                        .Where(e => e is SteeringWheel)
                        .Cast<SteeringWheel>()
                        .Where(sw => sw.OwnerID.Equals(targetSteamID))
                        .ToList();

                    playerInfo = ConvertToPlayerInfo(playerCmdCaller);
                    _coroutines.Add(ServerMgr.Instance.StartCoroutine(RemoveLock(playerInfo, lockList, modularCarList, steeringWheelList)));

                    break;

                case "lock":
                case "unlock":
                case "remove":

                    //TODO #################### unificare questi controlli ####################
                    if (!isAdmin)
                    {
                        SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                        return;
                    }

                    if (!CanByPassCodeLock(playerCmdCaller))
                    {
                        SendMessage(playerCmdCaller, Lang("NoBypassPermissions", playerUserIDString));
                        return;
                    }
                    //#########################################################################

                    var lockDefinition = GetLockDefinitionByLookingAt(playerCmdCaller);
                    if (lockDefinition == null || lockDefinition.GetCodeLockEntity() == null ||
                        lockDefinition.BaseEntity == null)
                    {
                        SendMessage(playerCmdCaller, Lang("CodeLockNotFound", playerUserIDString));
                        return;
                    }

                    switch (action)
                    {
                        case "lock":
                        {
                            lockDefinition.Lock();
                            PlayerShowToast(playerCmdCaller,
                                Lang("CodeLockMessage", playerUserIDString, Lang("LockerStatus_Locked", playerUserIDString)));
                            break;
                        }
                        case "unlock":
                        {
                            lockDefinition.Unlock();
                            PlayerShowToast(playerCmdCaller,
                                Lang("CodeLockMessage", playerUserIDString, Lang("LockerStatus_Unlocked", playerUserIDString)));
                            break;
                        }
                        case "remove":
                        {
                            lockDefinition.RemoveLock(playerCmdCaller);
                            PlayerShowToast(playerCmdCaller,
                                Lang("CodeLockMessage", playerUserIDString, Lang("LockerStatus_Removed", playerUserIDString)), true);
                            break;
                        }
                    }

                    break;

                case "code":
                case "codeall":
                case "guestcodeall":

                    var tryParseNewCode = arg.Length > 1 && ulong.TryParse(arg[1], out inputCodeLock);
                    //TODO #################### unificare questi controlli ####################
                    isSteamID = arg.Length > 2 && ulong.TryParse(arg[2], out targetSteamID);
                    if (isSteamID && !targetSteamID.IsSteamId())
                    {
                        SendMessage(playerCmdCaller, Lang("InvalidSteamID", playerUserIDString));
                        return;
                    }

                    if (!IsValidLockCode(inputCodeLock.ToString()))
                    {
                        SendMessage(playerCmdCaller, Lang("LockCodeNotValid", playerUserIDString));
                        return;
                    }

                    var newLockCode = PadWithZeros(inputCodeLock.ToString());

                    if (isAdmin && isSteamID)
                    {
                        targetBasePlayer = BasePlayer.FindAwakeOrSleepingByID(targetSteamID);
                        if (targetBasePlayer == null)
                        {
                            SendMessage(playerCmdCaller, Lang("PlayerNotFound", playerUserIDString));
                            return;
                        }
                    }
                    else if ((isAdmin && !isSteamID) || !isAdmin)
                    {
                        targetBasePlayer = playerCmdCaller;
                    }
                    else
                    {
                        targetBasePlayer = null;
                    }

                    if (!isAdmin && arg.Length > 2 && arg[2] != null)
                    {
                        SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                        return;
                    }
                    //#########################################################################

                    switch (action)
                    {
                        case "code":
                        {
                            lockDefinition = GetLockDefinitionByLookingAt(playerCmdCaller);
                            var codeLockEntity = lockDefinition?.GetCodeLockEntity();
                            if (lockDefinition == null || codeLockEntity == null)
                            {
                                SendMessage(playerCmdCaller, Lang("CodeLockNotFound", playerUserIDString));
                                return;
                            }

                            targetBasePlayer = lockDefinition.GetOwnerPlayer();
                            if (targetBasePlayer == null)
                            {
                                SendMessage(playerCmdCaller, Lang("PlayerNotFound", playerUserIDString));
                                return;
                            }

                            // TODO unificare questi controlli con quelli di altri metodi/codici: [case "ctime": , case "code":, LockerCloserCmdHandler]
                            if (!isAdmin && !IsEntityOwner(codeLockEntity, playerCmdCaller))
                            {
                                SendMessage(playerCmdCaller, Lang("NoEntityOwner", playerUserIDString));
                                return;
                            }


                            //If the admin is not the owner of the entity, send the message, to change the pin code, only to the admin
                            var sendMessage = true;
                            if (isAdmin && !playerCmdCaller.userID.Get().Equals(targetBasePlayer.userID))
                            {
                                if (!CanByPassCodeLock(playerCmdCaller))
                                {
                                    SendMessage(playerCmdCaller, Lang("NoBypassPermissions", playerUserIDString));
                                    return;
                                }

                                sendMessage = false;
                            }


                            switch (lockDefinition.GetCodeLockEntity())
                            {
                                case ModularCar modularCar:
                                    if (!modularCar.CarLock.HasALock) return;
                                    UpdateCodeLockAuthShareOrSettings(null, modularCar, targetBasePlayer, newLockCode, sendMessage: sendMessage);
                                    break;
                                case SteeringWheel steeringWheel:
                                    SteeringWheelApplySettings(targetBasePlayer, steeringWheel, newCode: newLockCode);
                                    break;
                                case CodeLock codeLock:
                                    if (!codeLock.OwnerID.IsSteamId()) return;
                                    UpdateCodeLockAuthShareOrSettings(codeLock, null, targetBasePlayer, newLockCode, sendMessage: sendMessage);
                                    break;
                            }

                            //If the admin is not the owner of the entity, send the message, to change the pin code, only to the admin
                            if (isAdmin && !sendMessage)
                                SendMessage(playerCmdCaller, Lang("CodeLockPinChangedMessage", playerUserIDString));

                            break;
                        }
                        case "codeall":
                        case "guestcodeall":
                        {
                            if (isAdmin)
                            {
                                if (isSteamID && !CanByPassCodeLock(playerCmdCaller))
                                {
                                    SendMessage(playerCmdCaller, Lang("NoBypassPermissions", playerUserIDString));
                                    return;
                                }
                            }

                            if (targetBasePlayer == null)
                            {
                                SendMessage(playerCmdCaller, Lang("PlayerNotFound", playerUserIDString));
                                return;
                            }

                            var lockEntityList = BaseNetworkable.serverEntities
                                .Where(se =>
                                    se is CodeLock cl &&
                                    cl.OwnerID.IsSteamId() &&
                                    cl.OwnerID.Equals(targetBasePlayer.userID))
                                .Cast<BaseEntity>()
                                .ToList();

                            lockEntityList.AddRange(BaseNetworkable.serverEntities
                                .Where(se =>
                                    se is ModularCar be && (
                                        be.ShortPrefabName.Contains(ModuleCarPartialShortPrefabName) ||
                                        be.PrefabName.StartsWith(CarChassisModuleCarPartialPrefabName, StringComparison.OrdinalIgnoreCase)
                                    ) &&
                                    be.OwnerID.IsSteamId() &&
                                    be.OwnerID.Equals(targetBasePlayer.userID))
                                .Cast<BaseEntity>());

                            lockEntityList.AddRange(BaseNetworkable.serverEntities
                                .Where(se =>
                                    se is SteeringWheel sw &&
                                    sw.OwnerID.IsSteamId() &&
                                    sw.OwnerID.Equals(targetBasePlayer.userID))
                                .Cast<BaseEntity>());

                            var isGuestCode = action.Equals("guestcodeall");

                            _coroutines.Add(
                                ServerMgr.Instance.StartCoroutine(UpdateAllCodeLockAuthShareOrSettings(lockEntityList, targetBasePlayer,
                                    newLockCode, isGuestCode: isGuestCode, sendMessage: true)));

                            break;
                        }
                    }

                    break;

                case "show":

                    //TODO #################### unificare questi controlli ####################
                    if (!isAdmin)
                    {
                        SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                        return;
                    }

                    if (!CanByPassCodeLock(playerCmdCaller))
                    {
                        SendMessage(playerCmdCaller, Lang("NoBypassPermissions", playerUserIDString));
                        return;
                    }
                    //#########################################################################

                    lockDefinition = GetLockDefinitionByLookingAt(playerCmdCaller);
                    if (lockDefinition == null || lockDefinition.GetCodeLockEntity() == null)
                    {
                        SendMessage(playerCmdCaller, Lang("CodeLockNotFound", playerUserIDString));
                        return;
                    }

                    if (lockDefinition.BaseEntity == null)
                    {
                        SendMessage(playerCmdCaller, Lang("EntityNotAllowed", playerUserIDString));
                        return;
                    }

                    var codeLockCode = lockDefinition.GetCodeLockCode();
                    if (string.IsNullOrWhiteSpace(codeLockCode))
                    {
                        SendMessage(playerCmdCaller, Lang("CodeLockCodeNotFound", playerUserIDString));
                        return;
                    }

                    var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(playerCmdCaller.userID);
                    if (autoLockPlayerSettings.StreamerModeEnabled)
                    {
                        SendMessage(playerCmdCaller, Lang("ShowCodeLockPinStreamerModeMessage", playerUserIDString));
                        return;
                    }

                    var guestCodeLock = lockDefinition.GetCodeLockGuestCode();

                    SendMessage(playerCmdCaller,
                        string.IsNullOrWhiteSpace(guestCodeLock)
                            ? Lang("ShowCodeLockCodeMessage", playerUserIDString, codeLockCode)
                            : Lang("ShowCodeLockAndGuestCodeMessage", playerUserIDString, codeLockCode, guestCodeLock));

                    break;

                case "ctime":

                    if (arg.Length < 2)
                    {
                        SendMessage(playerCmdCaller, Lang("DelayTimeNotValid", playerUserIDString,
                            _config.AutoClosingConfiguration.MinimumClosingDelayTime,
                            _config.AutoClosingConfiguration.MaximumClosingDelayTime));
                        return;
                    }

                    var delayTimeString = arg[1];

                    var isDoorNetID = arg.Length > 2;
                    var doorNetID = 0UL;
                    if (isDoorNetID && !ulong.TryParse(arg[2], out doorNetID))
                    {
                        SendMessage(playerCmdCaller, Lang("MissingParametersError", playerUserIDString, "door net id"));
                        return;
                    }

                    // TODO unificare questi controlli con quelli di altri metodi/codici: [case "ctime": , case "code":, LockerCloserCmdHandler]
                    if (!permission.UserHasPermission(playerUserIDString, PermissionUse))
                    {
                        SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                        return;
                    }

                    if (!IsValidDelayTime(delayTimeString))
                    {
                        SendMessage(playerCmdCaller, Lang("DelayTimeNotValid", playerUserIDString,
                            _config.AutoClosingConfiguration.MinimumClosingDelayTime, _config.AutoClosingConfiguration.MaximumClosingDelayTime));
                        return;
                    }

                    if (!IsAutoClosingEnabled(userId))
                    {
                        SendMessage(playerCmdCaller, Lang("AutoClosingNotEnabled", playerUserIDString));
                        return;
                    }

                    var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(playerCmdCaller);
                    if (!autoClosingPlayerSettings.AutoClosingEnabled)
                    {
                        SendMessage(playerCmdCaller, Lang("PlayerAutoClosingNotEnabled", playerUserIDString));
                        return;
                    }

                    var entityLookedAt = isDoorNetID
                        ? BaseNetworkable.serverEntities.Find(new NetworkableId(doorNetID)) as Door
                        : GetEntityByRayCast(playerCmdCaller);

                    if (entityLookedAt is null)
                    {
                        SendMessage(playerCmdCaller, Lang("EntityNotFound", playerUserIDString));
                        return;
                    }

                    if (entityLookedAt.IsDestroyed || entityLookedAt is not Door door)
                    {
                        SendMessage(playerCmdCaller, Lang("EntityNotAllowed", playerUserIDString));
                        return;
                    }

                    if (!isAdmin && !IsEntityOwner(door, playerCmdCaller))
                    {
                        SendMessage(playerCmdCaller, Lang("NoEntityOwner", playerUserIDString));
                        return;
                    }

                    var autoClosingType = AutoClosingTypeByEntity(door);
                    if (autoClosingType == AutoClosingType.None)
                    {
                        SendMessage(playerCmdCaller, Lang("AutoClosingTypeNotValid", playerUserIDString));
                        return;
                    }

                    var delayTime = GetValidDelayTime(delayTimeString);

                    if (autoClosingType == AutoClosingType.Garage)
                        delayTime += GarageOpenDuration;

                    AddOrUpdateDoorCloserData(userId, door, delayTime);

                    PlayerShowToast(playerCmdCaller, Lang("SingleDoorCloserCustomTime", playerUserIDString, delayTime));

                    break;
            }
        }

        private void AutoLockCmdHandler(IPlayer ipPlayer, string command, string[] arg)
        {
            var player = ipPlayer?.Object as BasePlayer;
            if (player == null) return;

            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse) ||
                !permission.UserHasPermission(userId, PermissionAutoLockEnabled))
            {
                SendMessage(player, Lang("NoPermissions", userId));
                return;
            }

            if (arg.Length == 0)
            {
                OpenAutoLockSettingsUI(player);
                return;
            }
        }

        private void AutoClosingCmdHandler(IPlayer ipPlayer, string command, string[] arg)
        {
            var player = ipPlayer?.Object as BasePlayer;
            if (player == null) return;

            var userId = player.UserIDString;
            if (!permission.UserHasPermission(userId, PermissionUse) ||
                !permission.UserHasPermission(userId, PermissionAutoClosingEnabled))
            {
                SendMessage(player, Lang("NoPermissions", userId));
                return;
            }

            if (arg.Length == 0)
            {
                OpenAutoClosingSettingsUI(player);
                return;
            }
        }

        private void LockerCloserCmdHandler(IPlayer ipPlayer, string command, string[] arg)
        {
            var playerCmdCaller = ipPlayer?.Object as BasePlayer;
            if (playerCmdCaller == null) return;

            var userId = playerCmdCaller.userID;
            var playerUserIDString = playerCmdCaller.UserIDString;

            var isAdmin = permission.UserHasPermission(playerUserIDString, PermissionAdmin);

            // TODO unificare questi controlli con quelli di altri metodi/codici: [case "ctime": , case "code":, LockerCloserCmdHandler]
            if (!permission.UserHasPermission(playerUserIDString, PermissionUse))
            {
                SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                return;
            }

            if (command.Equals(_config.AutoLockConfiguration.AddLockerChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (!permission.UserHasPermission(playerUserIDString, PermissionAutoLockEnabled))
                {
                    SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                    return;
                }

                var entityLookedAt = GetEntityByRayCast(playerCmdCaller);
                if (entityLookedAt == null)
                {
                    SendMessage(playerCmdCaller, Lang("EntityNotFound", playerUserIDString));
                    return;
                }

                entityLookedAt = entityLookedAt.GetParentEntity() ?? entityLookedAt;

                DeployLocker(playerCmdCaller, entityLookedAt, true);
                return;
            }

            if (command.Equals(_config.AutoLockConfiguration.ToggleAutoLockChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (!permission.UserHasPermission(playerUserIDString, PermissionAutoLockEnabled))
                {
                    SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                    return;
                }

                var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(userId);
                autoLockPlayerSettings.AutoLockOnPlacementEnabled = !autoLockPlayerSettings.AutoLockOnPlacementEnabled;
                SaveData();

                PlayerShowToast(playerCmdCaller, autoLockPlayerSettings.AutoLockOnPlacementEnabled
                    ? Lang("ToggleAutoLock", playerUserIDString, Lang("UiEnabled", playerUserIDString))
                    : Lang("ToggleAutoLock", playerUserIDString, Lang("UiDisabled", playerUserIDString)));

                return;
            }

            if (command.Equals(_config.AutoClosingConfiguration.ToggleAutoCloserChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (!permission.UserHasPermission(playerUserIDString, PermissionAutoClosingEnabled))
                {
                    SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                    return;
                }

                var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(userId);
                autoClosingPlayerSettings.AutoClosingEnabled = !autoClosingPlayerSettings.AutoClosingEnabled;
                SaveData();

                PlayerShowToast(playerCmdCaller, autoClosingPlayerSettings.AutoClosingEnabled
                    ? Lang("ToggleAutoDoorCloser", playerUserIDString, Lang("UiEnabled", playerUserIDString))
                    : Lang("ToggleAutoDoorCloser", playerUserIDString, Lang("UiDisabled", playerUserIDString)));

                return;
            }

            if (command.Equals(_config.AutoClosingConfiguration.AddCloserChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (!permission.UserHasPermission(playerUserIDString, PermissionAutoClosingEnabled))
                {
                    SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                    return;
                }

                var entityLookedAt = GetEntityByRayCast(playerCmdCaller);
                if (entityLookedAt == null)
                {
                    SendMessage(playerCmdCaller, Lang("EntityNotFound", playerUserIDString));
                    return;
                }

                if (entityLookedAt is not Door door)
                {
                    SendMessage(playerCmdCaller, Lang("EntityNotAllowed", playerUserIDString));
                    return;
                }

                DeployDoorCloser(playerCmdCaller, entityLookedAt, true);
                AddDoorCloserTimer(playerCmdCaller.userID, door);
                return;
            }

            if (command.Equals(_config.AutoClosingConfiguration.RemoveCloserChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (!permission.UserHasPermission(playerUserIDString, PermissionAutoClosingEnabled))
                {
                    SendMessage(playerCmdCaller, Lang("NoPermissions", playerUserIDString));
                    return;
                }

                var entityLookedAt = GetEntityByRayCast(playerCmdCaller);
                if (entityLookedAt == null)
                {
                    SendMessage(playerCmdCaller, Lang("EntityNotFound", playerUserIDString));
                    return;
                }

                if (entityLookedAt is not Door door)
                {
                    SendMessage(playerCmdCaller, Lang("EntityNotAllowed", playerUserIDString));
                    return;
                }

                if (!isAdmin && !IsEntityOwner(door, playerCmdCaller))
                {
                    SendMessage(playerCmdCaller, Lang("NoEntityOwner", playerUserIDString));
                    return;
                }


                // Salva la door nel file dati con IsAutoClosingDisabled = true e DelayTime = 0
                // per impedire la riattivazione dell'auto closer al riavvio del plugin
                var doorCloserData = GetDoorCloserData(door);
                if (doorCloserData is null)
                {
                    AddOrUpdateDoorCloserData(userId, door, 0);
                    doorCloserData = GetDoorCloserData(door);
                    if (doorCloserData != null)
                        doorCloserData.DelayTime = 0;

                    if (doorCloserData is null) return;
                }

                doorCloserData.IsAutoClosingDisabled = true;

                DestroyDoorCloserTimer(door);
                SaveData();


                SendMessage(playerCmdCaller, Lang("DoorCloserListMessage", playerUserIDString,
                    Lang("LockerStatus_Removed", playerUserIDString), "1"));

                return;
            }
        }

        [ConsoleCommand("ultimatelocker.autolock.cmd")]
        private void UIAutoLockCommandHandler(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            var action = arg.HasArgs(1) ? arg.GetString(0) : null;

            var userId = player.userID;
            var userIdString = player.UserIDString;

            if (!permission.UserHasPermission(userIdString, PermissionUse) ||
                !permission.UserHasPermission(userIdString, PermissionAutoLockEnabled))
            {
                SendMessage(player, Lang("NoPermissions", userIdString));
                return;
            }

            bool? toggleStatus;
            List<BaseEntity> lockEntityList;

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(userId);

            switch (action)
            {
                case "toggleShareLocksWithClan":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.ShareLocksWithClanTeamEnabled == toggleStatus) return;

                    autoLockPlayerSettings.ShareLocksWithClanTeamEnabled = arg.GetString(1).ToBool();

                    if (!_config.AutoLockConfiguration.UseOnlyKeyLock)
                        player.SendConsoleCommand("ultimatelocker.autolock.cmd updateAllCodeLockAuth");

                    SaveData();
                    BuildAutoLockSettingsBox1(player);
                    break;

                case "toggleAutoLockOnPlacement":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.AutoLockOnPlacementEnabled == toggleStatus) return;

                    autoLockPlayerSettings.AutoLockOnPlacementEnabled = arg.GetString(1).ToBool();

                    if (!_config.AutoLockConfiguration.UseOnlyKeyLock && !autoLockPlayerSettings.AutoLockOnPlacementEnabled)
                        player.SendConsoleCommand("ultimatelocker.autolock.cmd removeAllGuestCodeLockPin");

                    SaveData();
                    BuildAutoLockSettingsBox1(player);
                    break;

                case "toggleGuestCodeEnable":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.GuestCodeEnabled == toggleStatus) return;

                    autoLockPlayerSettings.GuestCodeEnabled = arg.GetString(1).ToBool();

                    if (!autoLockPlayerSettings.GuestCodeEnabled)
                        player.SendConsoleCommand("ultimatelocker.autolock.cmd removeAllGuestCodeLockPin");

                    SaveData();
                    OpenAutoLockSettingsUI(player);
                    break;

                case "toggleAlsoUseKeyLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.AlsoUseKeyLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.AlsoUseKeyLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    BuildAutoLockSettingsBox1(player);
                    break;

                case "toggleStreamerMode":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.StreamerModeEnabled == toggleStatus) return;

                    autoLockPlayerSettings.StreamerModeEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    BuildAutoLockSettingsBox1(player);
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleCodelockSkinFlightControlCodelock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();

                    if (!IsSkinOwned(player, FlightControlCodeLockSkinID))
                    {
                        autoLockPlayerSettings.CodelockSkinFlightControlCodelockEnabled = false;

                        SaveData();
                        RefreshAutoLockScrollContent(player);
                        break;
                    }

                    if (autoLockPlayerSettings.CodelockSkinFlightControlCodelockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.CodelockSkinFlightControlCodelockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleDoorsLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.DoorsLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.DoorsLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleWindowsLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.WindowsLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.WindowsLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleBoxesLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.BoxesLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.BoxesLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleStorageContainerLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.StorageContainerLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.StorageContainerLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleLockersLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.LockersLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.LockersLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleCupboardsLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.CupboardsLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.CupboardsLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleVehicleLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.VehicleLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.VehicleLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleVehicleLockAutoClosingOnDismount":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.VehicleLockAutoClosingOnDismountEnabled == toggleStatus) return;

                    autoLockPlayerSettings.VehicleLockAutoClosingOnDismountEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleNavalDeepSeaLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.NavalDeepSeaLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.NavalDeepSeaLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleMedievalEntityLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.MedievalEntityLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.MedievalEntityLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleFurnaceLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.FurnaceLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.FurnaceLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleVendingMachineLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.VendingMachineLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.VendingMachineLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleComposterLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.ComposterLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.ComposterLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleMixingTableLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.MixingTableLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.MixingTableLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "togglePlanterLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.PlanterLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.PlanterLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleAutoTurretLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.AutoTurretLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.AutoTurretLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleSamSiteLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.SamSiteLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.SamSiteLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleTrapsLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.TrapsLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.TrapsLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleWeaponRackLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.WeaponRackLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.WeaponRackLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleStashLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.StashLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.StashLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleNeonSignLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.NeonSignLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.NeonSignLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleSignDrawingLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.SignDrawingLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.SignDrawingLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleOtherLockableEntitiesLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.OtherLockableEntitiesLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.OtherLockableEntitiesLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "toggleOtherCustomEntitiesLock":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoLockPlayerSettings.OtherCustomEntitiesLockEnabled == toggleStatus) return;

                    autoLockPlayerSettings.OtherCustomEntitiesLockEnabled = arg.GetString(1).ToBool();
                    SaveData();
                    RefreshAutoLockScrollContent(player);
                    break;

                case "saveNewCodeLockPin":
                    if (!arg.HasArgs(2)) return;

                    var newCodeLockPin = arg.GetString(1);
                    newCodeLockPin = PadWithZeros(newCodeLockPin);

                    if (string.IsNullOrWhiteSpace(newCodeLockPin) || !IsValidLockCode(newCodeLockPin))
                    {
                        SendMessage(player, Lang("LockCodeNotValid", userIdString));
                        return;
                    }

                    if (autoLockPlayerSettings.CodeLock.Equals(newCodeLockPin)) return;

                    autoLockPlayerSettings.CodeLock = newCodeLockPin;
                    SaveData();
                    BuildAutoLockSettingsBox1(player);
                    break;

                case "saveNewGuestCodeLockPin":
                    if (!arg.HasArgs(2)) return;

                    var newGuestCodeLockPin = arg.GetString(1);
                    newGuestCodeLockPin = PadWithZeros(newGuestCodeLockPin);

                    if (string.IsNullOrWhiteSpace(newGuestCodeLockPin) || !IsValidLockCode(newGuestCodeLockPin))
                    {
                        SendMessage(player, Lang("LockCodeNotValid", userIdString));
                        return;
                    }

                    if (autoLockPlayerSettings.GuestCodeLock.Equals(newGuestCodeLockPin)) return;

                    autoLockPlayerSettings.GuestCodeLock = newGuestCodeLockPin;
                    SaveData();
                    BuildAutoLockSettingsBox1(player);
                    break;

                case "updateAllCodeLockPin":
                    if (!autoLockPlayerSettings.AutoLockOnPlacementEnabled) return;

                    if (!IsValidLockCode(autoLockPlayerSettings.CodeLock)) return;

                    CmdHandler(player.IPlayer, "", new[] { "codeall", autoLockPlayerSettings.CodeLock });
                    break;

                case "updateAllGuestCodeLockPin":
                    if (!autoLockPlayerSettings.AutoLockOnPlacementEnabled ||
                        !autoLockPlayerSettings.GuestCodeEnabled) return;

                    if (!IsValidLockCode(autoLockPlayerSettings.GuestCodeLock)) return;

                    CmdHandler(player.IPlayer, "", new[] { "guestcodeall", autoLockPlayerSettings.GuestCodeLock });
                    break;

                case "updateAllCodeLockAuth":
                    lockEntityList = BaseNetworkable.serverEntities
                        .Where(se =>
                            se is CodeLock cl &&
                            cl.OwnerID.IsSteamId() &&
                            cl.OwnerID.Equals(userId))
                        .Cast<BaseEntity>()
                        .ToList();

                    // TODO per tutti gli AddRange rimuovere ToList(), in tutto il codice
                    lockEntityList.AddRange(BaseNetworkable.serverEntities
                        .Where(se =>
                            se is ModularCar be && (
                                be.ShortPrefabName.Contains(ModuleCarPartialShortPrefabName) ||
                                be.PrefabName.StartsWith(CarChassisModuleCarPartialPrefabName, StringComparison.OrdinalIgnoreCase)
                            ) &&
                            be.OwnerID.IsSteamId() &&
                            be.OwnerID.Equals(userId))
                        .Cast<BaseEntity>()
                        .ToList());

                    _coroutines.Add(
                        ServerMgr.Instance.StartCoroutine(UpdateAllCodeLockAuthShareOrSettings(lockEntityList, player, sendSharedAuthMessage: true)));

                    break;

                case "removeAllGuestCodeLockPin":
                    lockEntityList = BaseNetworkable.serverEntities
                        .Where(se =>
                            se is CodeLock cl &&
                            cl.OwnerID.IsSteamId() &&
                            cl.OwnerID.Equals(userId))
                        .Cast<BaseEntity>()
                        .ToList();

                    _coroutines.Add(
                        ServerMgr.Instance.StartCoroutine(
                            UpdateAllCodeLockAuthShareOrSettings(lockEntityList, player, removeGuestCode: true, sendMessage: true)));

                    break;
            }
        }

        [ConsoleCommand("ultimatelocker.autoclosing.cmd")]
        private void UIAutoClosingCommandHandler(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            var action = arg.HasArgs(1) ? arg.GetString(0) : null;

            var userId = player.userID;
            var userIdString = player.UserIDString;
            if (!permission.UserHasPermission(userIdString, PermissionUse) || !IsAutoClosingEnabled(userId))
            {
                SendMessage(player, Lang("NoPermissions", userIdString));
                return;
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                OpenAutoClosingSettingsUI(player);
                return;
            }

            bool? toggleStatus;
            string? autoClosingTypeString;
            AutoClosingType autoClosingType;

            string delayTimeString;
            int delayTime;

            var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(userId);

            switch (action)
            {
                case "toggleAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.AutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.AutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleDoorAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.DoorAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.DoorAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.Door);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleDoubleDoorAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.DoubleDoorAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.DoubleDoorAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.DoubleDoor);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleWindowAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.WindowAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.WindowAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.Window);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleGarageAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.GarageAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.GarageAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.Garage);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleLadderHatchAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.LadderHatchAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.LadderHatchAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.LadderHatch);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleExternalGateAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.ExternalGateAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.ExternalGateAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.ExternalGate);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleFenceGateAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.FenceGateAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.FenceGateAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.FenceGate);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "toggleLegacyWoodShelterDoorAutoClosingEnabled":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();
                    if (autoClosingPlayerSettings.LegacyWoodShelterDoorAutoClosingEnabled == toggleStatus) return;

                    autoClosingPlayerSettings.LegacyWoodShelterDoorAutoClosingEnabled = arg.GetString(1).ToBool();

                    ResyncDoorCloserTimers(userId, AutoClosingType.LegacyWoodShelterDoor);

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;

                case "saveNewDelayTime":

                    if (!arg.HasArgs(3) || string.IsNullOrWhiteSpace(arg.GetString(1)) || string.IsNullOrWhiteSpace(arg.GetString(2))) return;

                    autoClosingTypeString = arg.GetString(1);
                    delayTimeString = arg.GetString(2);

                    if (string.IsNullOrWhiteSpace(autoClosingTypeString) || string.IsNullOrWhiteSpace(delayTimeString))
                    {
                        SendMessage(player, Lang("MissingParametersError", userIdString, "autoClosingTypeString or delayTimeString"));
                        return;
                    }

                    if (!IsValidDelayTime(delayTimeString))
                    {
                        SendMessage(player, Lang("DelayTimeNotValid", userIdString, _config.AutoClosingConfiguration.MinimumClosingDelayTime,
                            _config.AutoClosingConfiguration.MaximumClosingDelayTime));
                        return;
                    }

                    if (!Enum.IsDefined(typeof(AutoClosingType), autoClosingTypeString))
                    {
                        SendMessage(player, Lang("GenericError", userIdString, "autoClosingTypeString"));
                        return;
                    }

                    if (!Enum.TryParse(autoClosingTypeString, out autoClosingType))
                    {
                        SendMessage(player, Lang("GenericError", userIdString, "autoClosingTypeString conversion"));
                        return;
                    }

                    delayTime = GetValidDelayTime(delayTimeString);

                    if (!SetPlayerSettingsAutoClosingDelayTime(autoClosingType, autoClosingPlayerSettings, delayTime))
                    {
                        SendMessage(player, Lang("GenericError", userIdString, "autoClosingType case"));
                    }

                    break;

                case "updateAllDoorCloserDelayTime":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;

                    autoClosingTypeString = arg.GetString(1);

                    if (string.IsNullOrWhiteSpace(autoClosingTypeString))
                    {
                        SendMessage(player, Lang("MissingParametersError", userIdString, "autoClosingTypeString"));
                        return;
                    }

                    if (!Enum.IsDefined(typeof(AutoClosingType), autoClosingTypeString))
                    {
                        SendMessage(player, Lang("GenericError", userIdString, "autoClosingTypeString"));
                        return;
                    }

                    if (!Enum.TryParse(autoClosingTypeString, out autoClosingType))
                    {
                        SendMessage(player, Lang("GenericError", userIdString, "autoClosingTypeString conversion"));
                        return;
                    }

                    ResyncDoorCloserData(userId, autoClosingType, true);

                    break;

                case "showProximitySystemUI":
                case "toggleEntityAutoClosingEnabled":
                case "updateEntityAutoClosingDelayTime":
                case "toggleProximitySystemUIEnabled":

                    // TODO unificare questi controlli con quelli di altri metodi/codici: [case "ctime": , case "code":, LockerCloserCmdHandler]
                    if (!TryGetTargetProximityDoor(player, out var door) || door is null)
                    {
                        SendMessage(player, Lang("EntityNotFound", userIdString));
                        return;
                    }

                    if (door.IsDestroyed)
                    {
                        SendMessage(player, Lang("EntityNotAllowed", userIdString));
                        return;
                    }

                    if (!IsEntityOwner(door, player))
                    {
                        SendMessage(player, Lang("NoEntityOwner", userIdString));
                        return;
                    }

                    switch (action)
                    {
                        case "showProximitySystemUI":
                            ShowProximityUI(player, door);
                            break;
                    }

                    var doorCloserData = GetDoorCloserData(door);
                    if (doorCloserData == null)
                    {
                        SendMessage(player, Lang("GenericError", userIdString, "door data not exists"));
                        ShowProximityUI(player, door);
                        return;
                    }

                    switch (action)
                    {
                        case "toggleEntityAutoClosingEnabled":
                            doorCloserData.IsAutoClosingDisabled = !doorCloserData.IsAutoClosingDisabled;
                            SaveData();

                            // PlayerSavedDalyTime.TryGetValue(userId, out delayTime);
                            AddDoorCloserTimer(userId, door);
                            ShowProximityUI(player, door);

                            break;

                        case "updateEntityAutoClosingDelayTime":
                            PlayerSavedDalyTime.TryGetValue(userId, out delayTime);
                            doorCloserData.DelayTime = delayTime;
                            SaveData();

                            AddDoorCloserTimer(userId, door);
                            ShowProximityUI(player, door);

                            PlayerShowToast(player, Lang("DoorCloserListMessage", userIdString,
                                Lang("LockerStatus_Updated", userIdString), "1"));

                            break;

                        case "toggleProximitySystemUIEnabled":
                            autoClosingPlayerSettings.ProximitySystemUIEnabled = !autoClosingPlayerSettings.ProximitySystemUIEnabled;

                            SaveData();
                            ShowProximityUI(player, door);

                            break;
                    }

                    break;

                case "proximitySystemNewDelayTime":

                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;

                    delayTimeString = arg.GetString(1);
                    if (string.IsNullOrWhiteSpace(delayTimeString))
                    {
                        SendMessage(player, Lang("MissingParametersError", userIdString, "delayTimeString"));
                        return;
                    }

                    if (!IsValidDelayTime(delayTimeString))
                    {
                        SendMessage(player, Lang("DelayTimeNotValid", userIdString, _config.AutoClosingConfiguration.MinimumClosingDelayTime,
                            _config.AutoClosingConfiguration.MaximumClosingDelayTime));
                        return;
                    }

                    delayTime = GetValidDelayTime(delayTimeString);
                    PlayerSavedDalyTime[userId] = delayTime;

                    break;

                case "toggleProximitySystemUIEnabledWithoutNetId":
                    if (!arg.HasArgs(2) || string.IsNullOrWhiteSpace(arg.GetString(1))) return;
                    toggleStatus = arg.GetString(1).ToBool();

                    if (autoClosingPlayerSettings.ProximitySystemUIEnabled == toggleStatus) return;
                    autoClosingPlayerSettings.ProximitySystemUIEnabled = arg.GetString(1).ToBool();

                    if (!autoClosingPlayerSettings.ProximitySystemUIEnabled)
                    {
                        DestroyProximitySystemIconUI(player);
                    }

                    SaveData();
                    OpenAutoClosingSettingsUI(player);
                    break;
            }
        }

        private void OpenAutoLockSettingsUI(BasePlayer player)
        {
            DestroyAutoLockUI(player);
            var userId = player.UserIDString;

            var mainContainer = UIHelper.MainContainer(UIAutoLock, "0.5 0.5", "0.5 0.5", "-200 -220", "200 220", requireCursor: true);
            UIHelper.Header(mainContainer, UIAutoLock, UIAutoLock + ".Header", Lang("UiHeaderTitle", userId), closeUI: UIAutoLock, anchorMin: "0 0.94", anchorMax: "0.997 1");

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID);

            var isAutoClosingEnabled = IsAutoClosingEnabled(player.userID);
            var isGuestCodeEnabled = autoLockPlayerSettings.GuestCodeEnabled;

            var scrollOffsetMinY = isAutoClosingEnabled ? "-178" : "-206";
            var scrollOffsetMaxY = !isGuestCodeEnabled ? "46" : "20";

            UIHelper.ParentPanel(mainContainer, UIAutoLock, UIAutoLockContainer, "0 0", "0.997 0.938");

            var scrollContent = BuildAutoLockScrollContent(player);
            UIHelper.ScrollView(mainContainer, UIAutoLockContainer, UIAutoLockScrollView, scrollContent.Height,
                "0.5 0.5", "0.5 0.5", $"-200 {scrollOffsetMinY}", $"199 {scrollOffsetMaxY}");

            if (isAutoClosingEnabled)
                UIHelper.Button(mainContainer, UIAutoLockContainer, "ultimatelocker.autoclosing.cmd",
                    Lang("UiOpenAutoClosingButtonText", userId), "0.027 0.011", "0.50 0.06", fadeIn: 0.5f);

            CuiHelper.AddUi(player, mainContainer);
            BuildAutoLockSettingsBox1(player);
            CuiHelper.AddUi(player, scrollContent.Container);
        }

        private void BuildAutoLockSettingsBox1(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIAutoLockPanelRow1);

            var panelRow1 = UIHelper.SubContainer(UIAutoLockContainer, UIAutoLockPanelRow1,
                "0.5 0.5", "0.5 0.5", "-190 50", "181 200");

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID);

            const float rowHeight = 0.16f;
            const float rowHeightMargin = 0.032f;

            var rowOffsetY = 0.8;
            var userId = player.UserIDString;

            var yesText = Lang("UiButton_Yes", userId);
            var noText = Lang("UiButton_No", userId);
            var updateAllText = Lang("UiUpdateAll", userId);

            if (_config.AutoLockConfiguration.ForceShareLocksWithClanTeam == null)
            {
                //#################### ShareLocksWithClanTeam ####################
                var rowShareLocksWithClan = UIHelper.Row(panelRow1, UIAutoLockPanelRow1, UIAutoLockPanelRow1 + ".ShareLocksWithClan",
                    $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
                UIHelper.Text(panelRow1, rowShareLocksWithClan, Lang("UiShareLocksWithClanTeamEnabled", userId), "0.02 0.2", "0.78 0.7");
                UIHelper.ToggleButton(panelRow1, rowShareLocksWithClan, autoLockPlayerSettings.ShareLocksWithClanTeamEnabled,
                    "ultimatelocker.autolock.cmd toggleShareLocksWithClan", "0.8 0.1", "0.99 0.85",
                    yesText, noText);

                rowOffsetY -= rowHeight + rowHeightMargin;
                //####################################################################################################
            }

            //#################### AutoLockOnPlacement ####################
            var rowAutoLockOnPlacement = UIHelper.Row(panelRow1, UIAutoLockPanelRow1, UIAutoLockPanelRow1 + ".AutoLockOnPlacement",
                $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
            UIHelper.Text(panelRow1, rowAutoLockOnPlacement, Lang("UiAutoLockOnPlacementEnabled", userId), "0.02 0.2", "0.78 0.7");
            UIHelper.ToggleButton(panelRow1, rowAutoLockOnPlacement, autoLockPlayerSettings.AutoLockOnPlacementEnabled,
                "ultimatelocker.autolock.cmd toggleAutoLockOnPlacement", "0.8 0.1", "0.99 0.85",
                yesText, noText);
            //####################################################################################################

            if (!_config.AutoLockConfiguration.UseOnlyKeyLock && autoLockPlayerSettings.AutoLockOnPlacementEnabled)
            {
                //#################### CodeLock Pin ####################
                rowOffsetY -= rowHeight + rowHeightMargin;
                var rowCodeLockPin = UIHelper.Row(panelRow1, UIAutoLockPanelRow1, UIAutoLockPanelRow1 + ".CodeLockPin",
                    $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
                UIHelper.Text(panelRow1, rowCodeLockPin, Lang("UiCodeLockPin", userId), "0.02 0.2", "0.78 0.7");
                UIHelper.Input(panelRow1, rowCodeLockPin, "ultimatelocker.autolock.cmd saveNewCodeLockPin",
                    autoLockPlayerSettings.CodeLock, "0.46 0.1", "0.68 0.85", textAlign: TextAnchor.MiddleCenter, maxLength: 4,
                    isPassword: autoLockPlayerSettings.StreamerModeEnabled, autofocus: autoLockPlayerSettings.StreamerModeEnabled);
                UIHelper.UpdateButton(panelRow1, rowCodeLockPin, "ultimatelocker.autolock.cmd updateAllCodeLockPin",
                    updateAllText, "0.72 0.1", "0.99 0.85", fadeIn: 0.5f);
                //####################################################################################################

                //#################### GuestCodeEnable ####################
                rowOffsetY -= rowHeight + rowHeightMargin;
                var rowGuestCodeEnable = UIHelper.Row(panelRow1, UIAutoLockPanelRow1, UIAutoLockPanelRow1 + ".GuestCodeEnable",
                    $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
                UIHelper.Text(panelRow1, rowGuestCodeEnable, Lang("UiGuestCodeEnabled", userId), "0.02 0.2", "0.78 0.7");
                UIHelper.ToggleButton(panelRow1, rowGuestCodeEnable, autoLockPlayerSettings.GuestCodeEnabled,
                    "ultimatelocker.autolock.cmd toggleGuestCodeEnable", "0.8 0.1", "0.99 0.85",
                    yesText, noText);
                //####################################################################################################

                if (autoLockPlayerSettings.GuestCodeEnabled)
                {
                    //#################### GuestCodeLock Pin ####################
                    rowOffsetY -= rowHeight + rowHeightMargin;
                    var rowGuestCodeLockPin = UIHelper.Row(panelRow1, UIAutoLockPanelRow1, UIAutoLockPanelRow1 + ".GuestCodeLockPin",
                        $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
                    UIHelper.Text(panelRow1, rowGuestCodeLockPin, Lang("UiGuestCodeLockPin", userId), "0.02 0.2", "0.78 0.7");
                    UIHelper.Input(panelRow1, rowGuestCodeLockPin, "ultimatelocker.autolock.cmd saveNewGuestCodeLockPin",
                        autoLockPlayerSettings.GuestCodeLock, "0.46 0.1", "0.68 0.85", textAlign: TextAnchor.MiddleCenter, maxLength: 4,
                        isPassword: autoLockPlayerSettings.StreamerModeEnabled, autofocus: autoLockPlayerSettings.StreamerModeEnabled);
                    UIHelper.UpdateButton(panelRow1, rowGuestCodeLockPin, "ultimatelocker.autolock.cmd updateAllGuestCodeLockPin",
                        updateAllText, "0.72 0.1", "0.99 0.85", fadeIn: 0.5f);
                    //####################################################################################################
                }

                //#################### AlsoUseKeyLock ####################
                rowOffsetY -= rowHeight + rowHeightMargin;
                var rowAlsoUseKeyLock = UIHelper.Row(panelRow1, UIAutoLockPanelRow1, UIAutoLockPanelRow1 + ".AlsoUseKeyLock",
                    $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
                UIHelper.Text(panelRow1, rowAlsoUseKeyLock, Lang("UiAlsoUseKeyLockEnabled", userId), "0.02 0.2", "0.78 0.7");
                UIHelper.ToggleButton(panelRow1, rowAlsoUseKeyLock, autoLockPlayerSettings.AlsoUseKeyLockEnabled,
                    "ultimatelocker.autolock.cmd toggleAlsoUseKeyLock", "0.8 0.1", "0.99 0.85",
                    yesText, noText);
                //####################################################################################################
            }

            CuiHelper.AddUi(player, panelRow1);
        }

        private void RefreshAutoLockScrollContent(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIAutoLockScrollContent);
            var scrollContent = BuildAutoLockScrollContent(player);
            CuiHelper.AddUi(player, scrollContent.Container);
        }

        private UIHelper.ScrollContentResult BuildAutoLockScrollContent(BasePlayer player)
        {
            var panelScrollContent = UIHelper.SubContainer(UIAutoLockScrollView, UIAutoLockScrollContent,
                "0.0 0.0", "1.0 1.0");

            var autoLockPlayerSettings = GetAutoLockPlayerSettingsData(player.userID);
            var allowedLockCategory = PlayerGetAllowedLockCategory(player);

            const float componentLeftMargin = 10f;
            const float componentRightMargin = -20f;
            const float componentHeight = 26f;
            const float componentHeightMargin = 6f;

            var offsetY = -componentHeightMargin;
            var rowCount = 0;
            var userId = player.UserIDString;

            const string basicCmd = "ultimatelocker.autolock.cmd";


            //#################### StreamerMode ####################
            AddRow($"{UIAutoLockScrollView}.StreamerMode", Lang("UiStreamerModeEnabled", userId), $"{basicCmd} toggleStreamerMode",
                autoLockPlayerSettings.StreamerModeEnabled);
            //############################################################

            //#################### CodelockSkinFlightControlCodelockEnabled ####################

            var ownsSkin = IsSkinOwned(player, FlightControlCodeLockSkinID);
            var labelText = $"{Lang("UiCodelockSkinFlightControlCodelock", userId)}";

            if (!ownsSkin)
                labelText += $" <b><color=red>({Lang("UIDlcSkinRequired", userId)})</color></b>";
            else
                labelText += " <b><color=lime>(DLC)</color></b>";

            AddRow($"{UIAutoLockScrollView}.CodelockSkinFlightControlCodelockEnabled", $"{labelText}", $"{basicCmd} toggleCodelockSkinFlightControlCodelock",
                autoLockPlayerSettings.CodelockSkinFlightControlCodelockEnabled, noButton: !ownsSkin);
            //############################################################

            if (IsAutoLockCategoryEnabled(LockCategory.Door))
            {
                //#################### DoorsLock ####################
                AddRow($"{UIAutoLockScrollView}.DoorsLock", Lang("UiDoorsLockEnabled", userId), $"{basicCmd} toggleDoorsLock",
                    autoLockPlayerSettings.DoorsLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Window) && allowedLockCategory.Contains(LockCategory.Window))
            {
                //#################### WindowsLock ####################
                AddRow($"{UIAutoLockScrollView}.WindowsLock", Lang("UiWindowsLockEnabled", userId), $"{basicCmd} toggleWindowsLock",
                    autoLockPlayerSettings.WindowsLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Box))
            {
                //#################### BoxesLock ####################
                AddRow($"{UIAutoLockScrollView}.BoxesLock", Lang("UiBoxesLockEnabled", userId), $"{basicCmd} toggleBoxesLock",
                    autoLockPlayerSettings.BoxesLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.StorageContainer))
            {
                //#################### StorageContainerLock ####################
                AddRow($"{UIAutoLockScrollView}.StorageContainerLock", Lang("UiStorageContainerLockEnabled", userId), $"{basicCmd} toggleStorageContainerLock",
                    autoLockPlayerSettings.StorageContainerLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Locker))
            {
                //#################### LockersLock ####################
                AddRow($"{UIAutoLockScrollView}.LockersLock", Lang("UiLockersLockEnabled", userId), $"{basicCmd} toggleLockersLock",
                    autoLockPlayerSettings.LockersLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Cupboard))
            {
                //#################### CupboardsLock ####################
                AddRow($"{UIAutoLockScrollView}.CupboardsLock", Lang("UiCupboardsLockEnabled", userId), $"{basicCmd} toggleCupboardsLock",
                    autoLockPlayerSettings.CupboardsLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Vehicle) && allowedLockCategory.Contains(LockCategory.Vehicle))
            {
                //#################### VehicleLock ####################
                AddRow($"{UIAutoLockScrollView}.VehicleLock", Lang("UiVehicleLockEnabled", userId), $"{basicCmd} toggleVehicleLock",
                    autoLockPlayerSettings.VehicleLockEnabled);
                //############################################################

                //#################### VehicleLockAutoClosingOnDismount ####################
                AddRow($"{UIAutoLockScrollView}.VehicleLockAutoClosingOnDismount", Lang("UiVehicleLockAutoClosingOnDismountEnabled", userId),
                    $"{basicCmd} toggleVehicleLockAutoClosingOnDismount",
                    autoLockPlayerSettings.VehicleLockAutoClosingOnDismountEnabled, textFontSize: 9);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.NavalDeepSea) && allowedLockCategory.Contains(LockCategory.NavalDeepSea))
            {
                //#################### NavalDeepSeaLock ####################
                AddRow($"{UIAutoLockScrollView}.NavalDeepSeaLock", Lang("UiNavalDeepSeaLockEnabled", userId), $"{basicCmd} toggleNavalDeepSeaLock",
                    autoLockPlayerSettings.NavalDeepSeaLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Medieval) && allowedLockCategory.Contains(LockCategory.Medieval))
            {
                //#################### MedievalEntity ####################
                AddRow($"{UIAutoLockScrollView}.MedievalEntity", Lang("UiMedievalEntityLockEnabled", userId), $"{basicCmd} toggleMedievalEntityLock",
                    autoLockPlayerSettings.MedievalEntityLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Furnace) && allowedLockCategory.Contains(LockCategory.Furnace))
            {
                //#################### FurnaceLock ####################
                AddRow($"{UIAutoLockScrollView}.FurnaceLock", Lang("UiFurnaceLockEnabled", userId), $"{basicCmd} toggleFurnaceLock",
                    autoLockPlayerSettings.FurnaceLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.VendingMachine) && allowedLockCategory.Contains(LockCategory.VendingMachine))
            {
                //#################### VendingMachineLock ####################
                AddRow($"{UIAutoLockScrollView}.VendingMachineLock", Lang("UiVendingMachineLockEnabled", userId), $"{basicCmd} toggleVendingMachineLock",
                    autoLockPlayerSettings.VendingMachineLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Composter) && allowedLockCategory.Contains(LockCategory.Composter))
            {
                //#################### ComposterLock ####################
                AddRow($"{UIAutoLockScrollView}.ComposterLock", Lang("UiComposterLockEnabled", userId), $"{basicCmd} toggleComposterLock",
                    autoLockPlayerSettings.ComposterLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.MixingTable) && allowedLockCategory.Contains(LockCategory.MixingTable))
            {
                //#################### MixingTableLock ####################
                AddRow($"{UIAutoLockScrollView}.MixingTableLock", Lang("UiMixingTableLockEnabled", userId), $"{basicCmd} toggleMixingTableLock",
                    autoLockPlayerSettings.MixingTableLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Planter) && allowedLockCategory.Contains(LockCategory.Planter))
            {
                //#################### PlanterLock ####################
                AddRow($"{UIAutoLockScrollView}.PlanterLock", Lang("UiPlanterLockEnabled", userId), $"{basicCmd} togglePlanterLock",
                    autoLockPlayerSettings.PlanterLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.AutoTurret) && allowedLockCategory.Contains(LockCategory.AutoTurret))
            {
                //#################### AutoTurretLock ####################
                AddRow($"{UIAutoLockScrollView}.AutoTurretLock", Lang("UiAutoTurretLockEnabled", userId), $"{basicCmd} toggleAutoTurretLock",
                    autoLockPlayerSettings.AutoTurretLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.SamSite) && allowedLockCategory.Contains(LockCategory.SamSite))
            {
                //#################### SamSiteLock ####################
                AddRow($"{UIAutoLockScrollView}.SamSiteLock", Lang("UiSamSiteLockEnabled", userId), $"{basicCmd} toggleSamSiteLock",
                    autoLockPlayerSettings.SamSiteLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Trap) && allowedLockCategory.Contains(LockCategory.Trap))
            {
                //#################### TrapsLock ####################
                AddRow($"{UIAutoLockScrollView}.TrapsLock", Lang("UiTrapsLockEnabled", userId), $"{basicCmd} toggleTrapsLock",
                    autoLockPlayerSettings.TrapsLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.WeaponRack) && allowedLockCategory.Contains(LockCategory.WeaponRack))
            {
                //#################### WeaponRackLock ####################
                AddRow($"{UIAutoLockScrollView}.WeaponRackLock", Lang("UiWeaponRackLockEnabled", userId), $"{basicCmd} toggleWeaponRackLock",
                    autoLockPlayerSettings.WeaponRackLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.Stash) && allowedLockCategory.Contains(LockCategory.Stash))
            {
                //#################### StashLock ####################
                AddRow($"{UIAutoLockScrollView}.StashLock", Lang("UiStashLockEnabled", userId), $"{basicCmd} toggleStashLock",
                    autoLockPlayerSettings.StashLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.NeonSign) && allowedLockCategory.Contains(LockCategory.NeonSign))
            {
                //#################### NeonSignLock ####################
                AddRow($"{UIAutoLockScrollView}.NeonSignLock", Lang("UiNeonSignLockEnabled", userId), $"{basicCmd} toggleNeonSignLock",
                    autoLockPlayerSettings.NeonSignLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.SignDrawing) && allowedLockCategory.Contains(LockCategory.SignDrawing))
            {
                //#################### SignDrawingLock ####################
                AddRow($"{UIAutoLockScrollView}.SignDrawingLock", Lang("UiSignDrawingLockEnabled", userId), $"{basicCmd} toggleSignDrawingLock",
                    autoLockPlayerSettings.SignDrawingLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.OtherLockableEntities))
            {
                //#################### OtherLockableEntitiesLock ####################
                AddRow($"{UIAutoLockScrollView}.OtherLockableEntitiesLock", Lang("UiOtherLockableEntitiesLockEnabled", userId), $"{basicCmd} toggleOtherLockableEntitiesLock",
                    autoLockPlayerSettings.OtherLockableEntitiesLockEnabled);
                //############################################################
            }

            if (IsAutoLockCategoryEnabled(LockCategory.OtherCustomEntities) && allowedLockCategory.Contains(LockCategory.OtherCustomEntities))
            {
                //#################### OtherCustomEntitiesLock ####################
                AddRow($"{UIAutoLockScrollView}.OtherCustomEntitiesLock", Lang("UiOtherCustomEntitiesLockEnabled", userId), $"{basicCmd} toggleOtherCustomEntitiesLock",
                    autoLockPlayerSettings.OtherCustomEntitiesLockEnabled);
                //############################################################
            }

            return new UIHelper.ScrollContentResult
            {
                Container = panelScrollContent,
                Height = rowCount * (componentHeight + componentHeightMargin) + componentHeightMargin
            };

            void AddRow(string name, string text, string command, bool value, bool noButton = false, int textFontSize = 10)
            {
                var row = ScrollViewAddRow(
                    panelScrollContent,
                    name,
                    offsetY,
                    componentLeftMargin,
                    componentHeight,
                    componentRightMargin,
                    text,
                    command,
                    value,
                    player,
                    noButton: noButton,
                    textFontSize: textFontSize
                );

                panelScrollContent.Add(row.CuiPanel, UIAutoLockScrollView, row.PanelName);
                row.Render?.Invoke();

                rowCount++;
                offsetY -= componentHeight + componentHeightMargin;
            }
        }

        private void OpenAutoClosingSettingsUI(BasePlayer player)
        {
            DestroyLockerUI(player);

            var userId = player.UserIDString;

            var mainContainer = UIHelper.MainContainer(UIAutoClosing, "0.5 0.5", "0.5 0.5", "-200 -220", "200 220", requireCursor: true);
            UIHelper.Header(mainContainer, UIAutoClosing, UIAutoClosing + ".Header", Lang("UiAutoClosingHeaderTitle", userId),
                closeUI: UIAutoClosing, anchorMin: "0 0.94", anchorMax: "0.997 1");

            var contentPanel = UIHelper.ParentPanel(mainContainer, UIAutoClosing, UIAutoClosing + ".Container",
                "0 0", "0.997 0.938");

            var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(player.userID);

            //#################### AutoClosingEnabled ####################
            const float rowHeight = 0.0620f;
            const float rowHeightMargin = 0.01f;
            var rowOffsetY = 0.9245;

            var rowAutoClosingEnabled = UIHelper.Row(mainContainer, contentPanel, UIAutoClosing + ".Row.AutoClosingEnabled",
                $"0.0274 {rowOffsetY}", $"0.949 {rowOffsetY + rowHeight}");
            UIHelper.Text(mainContainer, rowAutoClosingEnabled, Lang("UiAutoClosingEnabledTitle", userId), "0.02 0.2", "0.78 0.7");
            UIHelper.ToggleButton(mainContainer, rowAutoClosingEnabled, autoClosingPlayerSettings.AutoClosingEnabled,
                "ultimatelocker.autoclosing.cmd toggleAutoClosingEnabled", "0.8 0.1", "0.99 0.85",
                Lang("UiButton_Yes", userId), Lang("UiButton_No", userId));

            rowOffsetY -= rowHeight + rowHeightMargin;
            //####################################################################################################

            //#################### ToggleProximitySystemUIEnabled ####################
            var rowToggleProximitySystemUIEnabled = UIHelper.Row(mainContainer, contentPanel, UIAutoClosing + ".Row.ToggleProximitySystemUIEnabled",
                $"0.0274 {rowOffsetY}", $"0.949 {rowOffsetY + rowHeight}");
            UIHelper.Text(mainContainer, rowToggleProximitySystemUIEnabled, Lang("ToggleProximitySystemUI", userId), "0.02 0.2", "0.78 0.7");
            UIHelper.ToggleButton(mainContainer, rowToggleProximitySystemUIEnabled, autoClosingPlayerSettings.ProximitySystemUIEnabled,
                "ultimatelocker.autoclosing.cmd toggleProximitySystemUIEnabledWithoutNetId", "0.8 0.1", "0.99 0.85",
                Lang("UiButton_Yes", userId), Lang("UiButton_No", userId));
            //####################################################################################################

            if (!autoClosingPlayerSettings.AutoClosingEnabled)
            {
                CuiHelper.AddUi(player, mainContainer);
                return;
            }

            //########################################## Delay Settings ##########################################
            const float componentLeftMargin = 10f;
            const float componentRightMargin = -20f;
            const float componentHeight = 26f;
            const float componentHeightMargin = 6f;

            var offsetY = componentHeightMargin * -1; //Add top margin
            var rowCount = 0;
            var rowCuiPanels = new List<UIHelper.RowCuiPanel>();

            const string basicCmd = "ultimatelocker.autoclosing.cmd";

            var autoClosingConfiguration = _config.AutoClosingConfiguration;
            if (autoClosingConfiguration.EnableDoorAutoClosing)
            {
                //#################### DoorAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.DoorAutoClosingEnabled", Lang("UiDoorAutoClosingEnabled", userId), $"{basicCmd} toggleDoorAutoClosingEnabled",
                    autoClosingPlayerSettings.DoorAutoClosingEnabled);

                if (autoClosingPlayerSettings.DoorAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.DoorAutoClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.DoorClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.Door}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.Door}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableDoubleDoorAutoClosing)
            {
                //#################### DoubleDoorAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.DoubleDoorAutoClosingEnabled", Lang("UiDoubleDoorAutoClosingEnabled", userId), $"{basicCmd} toggleDoubleDoorAutoClosingEnabled",
                    autoClosingPlayerSettings.DoubleDoorAutoClosingEnabled);

                if (autoClosingPlayerSettings.DoubleDoorAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.DoubleDoorClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.DoubleDoorClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.DoubleDoor}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.DoubleDoor}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableWindowAutoClosing)
            {
                //#################### WindowAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.WindowAutoClosingEnabled", Lang("UiWindowsAutoClosingEnabled", userId), $"{basicCmd} toggleWindowAutoClosingEnabled",
                    autoClosingPlayerSettings.WindowAutoClosingEnabled);

                if (autoClosingPlayerSettings.WindowAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.WindowClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.WindowClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.Window}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.Window}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableGarageAutoClosing)
            {
                //#################### GarageAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.GarageAutoClosingEnabled", Lang("UiGarageAutoClosingEnabled", userId), $"{basicCmd} toggleGarageAutoClosingEnabled",
                    autoClosingPlayerSettings.GarageAutoClosingEnabled);

                if (autoClosingPlayerSettings.GarageAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.GarageClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.GarageClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.Garage}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.Garage}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableLadderHatchAutoClosing)
            {
                //#################### LadderHatchAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.LadderHatchAutoClosingEnabled", Lang("UiLadderHatchAutoClosingEnabled", userId), $"{basicCmd} toggleLadderHatchAutoClosingEnabled",
                    autoClosingPlayerSettings.LadderHatchAutoClosingEnabled);

                if (autoClosingPlayerSettings.LadderHatchAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.LadderHatchClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.LadderHatchClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.LadderHatch}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.LadderHatch}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableExternalGateAutoClosing)
            {
                //#################### ExternalGateAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.ExternalGateAutoClosingEnabled", Lang("UiExternalGateAutoClosingEnabled", userId), $"{basicCmd} toggleExternalGateAutoClosingEnabled",
                    autoClosingPlayerSettings.ExternalGateAutoClosingEnabled);

                if (autoClosingPlayerSettings.ExternalGateAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.ExternalGateClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.ExternalGateClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.ExternalGate}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.ExternalGate}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableFenceGateAutoClosing)
            {
                //#################### FenceGateAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.FenceGateAutoClosingEnabled", Lang("UiFenceGateAutoClosingEnabled", userId), $"{basicCmd} toggleFenceGateAutoClosingEnabled",
                    autoClosingPlayerSettings.FenceGateAutoClosingEnabled);

                if (autoClosingPlayerSettings.FenceGateAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.FenceGateClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.FenceGateClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.FenceGate}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.FenceGate}");
                }
                //############################################################
            }

            if (autoClosingConfiguration.EnableLegacyWoodShelterDoorAutoClosing)
            {
                //#################### LegacyWoodShelterDoorAutoClosingEnabled ####################
                AddRow($"{UIAutoClosing}.Row.LegacyWoodShelterDoorAutoClosingEnabled", Lang("UiLegacyWoodShelterDoorAutoClosingEnabled", userId),
                    $"{basicCmd} toggleLegacyWoodShelterDoorAutoClosingEnabled",
                    autoClosingPlayerSettings.LegacyWoodShelterDoorAutoClosingEnabled);

                if (autoClosingPlayerSettings.LegacyWoodShelterDoorAutoClosingEnabled)
                {
                    AddSettingRow($"{UIAutoClosing}.Row.LegacyWoodShelterDoorClosingDelay", Lang("UiClosingDelay", userId),
                        autoClosingPlayerSettings.LegacyWoodShelterDoorClosingDelay, $"{basicCmd} saveNewDelayTime {AutoClosingType.LegacyWoodShelterDoor}",
                        $"{basicCmd} updateAllDoorCloserDelayTime {AutoClosingType.LegacyWoodShelterDoor}");
                }
                //############################################################
            }


            var scrollViewHeight = rowCount * (componentHeight + componentHeightMargin) + componentHeightMargin;
            var scrollView = UIHelper.ScrollView(mainContainer, contentPanel, UIAutoClosing + ".ScrollView", scrollViewHeight,
                "0 0.014", $"0.997 {rowOffsetY - .01}");


            foreach (var rowCuiPanel in rowCuiPanels)
            {
                mainContainer.Add(rowCuiPanel.CuiPanel, scrollView, rowCuiPanel.PanelName);
                rowCuiPanel.Render?.Invoke();
            }
            //####################################################################################################

            CuiHelper.AddUi(player, mainContainer);
            return;


            void AddRow(string name, string text, string command, bool value, bool noButton = false, int textFontSize = 11)
            {
                var row = ScrollViewAddRow(
                    mainContainer,
                    name,
                    offsetY,
                    componentLeftMargin,
                    componentHeight,
                    componentRightMargin,
                    text,
                    command,
                    value,
                    player,
                    noButton: noButton,
                    textFontSize: textFontSize
                );

                rowCuiPanels.Add(row);

                rowCount++;
                offsetY -= componentHeight + componentHeightMargin;
            }

            void AddSettingRow(string name, string text, int delayTime, string commandDelayTimeUpdate, string commandUpdate, string? commandRemove = null, int textFontSize = 8)
            {
                var row = ScrollViewAddAutoClosingSettingRow(
                    mainContainer,
                    name,
                    offsetY,
                    componentLeftMargin,
                    componentHeight,
                    componentRightMargin,
                    text,
                    delayTime,
                    commandDelayTimeUpdate,
                    commandUpdate,
                    null,
                    textFontSize,
                    player
                );

                rowCuiPanels.Add(row);

                rowCount++;
                offsetY -= componentHeight + componentHeightMargin;
            }
        }

        /// <summary>
        /// Verifica se la Proximity UI può essere mostrata
        /// e restituisce i dati del Door Closer associato se tutti i controlli passano.
        /// </summary>
        /// <remarks>
        /// I controlli vengono eseguiti in ordine dal meno costoso al più costoso:
        /// <list type="number">
        ///   <item><description>Validità di <paramref name="player"/> e <paramref name="door"/>.</description></item>
        ///   <item><description>Configurazione Proximity System abilitata in <see cref="_config"/>.</description></item>
        ///   <item><description>Impostazioni player: <c>AutoClosingEnabled</c>, <c>ProximitySystemUIEnabled</c>
        ///     e <see cref="IsAutoClosingEnabledForEntity"/>.</description></item>
        ///   <item><description>Esistenza del Door Closer sulla porta tramite <see cref="GetDoorCloserData"/>.</description></item>
        /// </list>
        /// Il dato viene restituito nella tupla per evitare una seconda chiamata a <see cref="GetDoorCloserData"/> nel chiamante.
        /// </remarks>
        /// <param name="player">Il player da verificare. Se <c>null</c> o con <c>userID</c> non valido, ritorna <c>(false, null)</c>.</param>
        /// <param name="door">La porta da verificare. Se <c>null</c> o con <c>OwnerID</c> non valido, ritorna <c>(false, null)</c>.</param>
        /// <returns>
        ///   <c>(true, DoorCloserEntityData)</c> se tutti i controlli passano;<br/>
        ///   <c>(false, null)</c> altrimenti.
        /// </returns>
        private (bool Result, DoorCloserEntityData? DoorCloserData) TryGetProximityUIData(BasePlayer player, Door door)
        {
            if (player is null || !player.userID.IsSteamId()) return (false, null);
            if (door is null || !door.OwnerID.IsSteamId()) return (false, null);

            var proximityConfig = _config.ProximitySystemConfiguration;
            if (proximityConfig is null or { ShowUIWhenPlayerIsNearDoorWithDoorCloser: false, ShowUIWhenPressingKey: false }) return (false, null);

            var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(player);
            if (autoClosingPlayerSettings is not { AutoClosingEnabled: true /*, ProximitySystemUIEnabled: true*/ }
                || !IsAutoClosingEnabledForEntity(door, autoClosingPlayerSettings)) return (false, null);

            var doorCloserData = GetDoorCloserData(door);
            return doorCloserData is null ? (false, null) : (true, doorCloserData);
        }

        private void ShowProximityIconUI(BasePlayer player, Door door)
        {
            DestroyProximitySystemIconUI(player);

            if (!TryGetProximityUIData(player, door).Result) return;

            var mainContainer = UIHelper.MainContainer(UIProximitySystemIcon, "1 1", "1 1", "-70 -110", "-20 -60",
                backgroundColor: UITransparentColor, requireCursor: false);
            var contentPanel = UIHelper.ParentPanel(mainContainer, UIProximitySystemIcon, UIProximitySystemIconContentPanel, "0 0", "1 1");

            var image = GetImage(AutoCloseProximitySystemImageName);

            if (!string.IsNullOrWhiteSpace(image))
                GuiHelper.ImageUrl(mainContainer, contentPanel, image, "0 0", "1 1");
            else
                GuiHelper.Image(mainContainer, contentPanel, CodeLockItemID, 0, "0 0", "1 1");

            GuiHelper.Button(mainContainer, contentPanel, $"ultimatelocker.autoclosing.cmd showProximitySystemUI", "", UITransparentColor,
                "0 0", "1 1");

            CuiHelper.AddUi(player, mainContainer);
        }

        private void ShowProximityUI(BasePlayer player, Door door)
        {
            DestroyProximitySystemUI(player);

            var autoClosingPlayerSettings = GetAutoClosingPlayerSettingsData(player.userID);
            var (canShowUi, doorCloserData) = TryGetProximityUIData(player, door);
            if (!canShowUi || autoClosingPlayerSettings == null || doorCloserData == null) return;

            var userId = player.userID;
            var userIdUserIDString = player.UserIDString;

            const float componentMargin = 10f;

            var mainContainer = UIHelper.MainContainer(UIProximitySystem, "1 1", "1 1", "-320 -250", "-20 -50", requireCursor: false);

            var headerPanel = UIHelper.ParentPanel(mainContainer, UIProximitySystem, UIProximitySystemHeaderPanel, "0 1", "1 1", "0 -24", "0 0");
            UIHelper.Header(mainContainer, headerPanel, headerPanel + ".Header", Lang("UiAutoClosingHeaderTitle", userIdUserIDString),
                fontSize: 12, closeUI: UIProximitySystem);

            var panelRow1 = UIHelper.ParentPanel(mainContainer, UIProximitySystem, UIProximitySystemRow1Panel,
                "0 0", "1 1", $"{componentMargin} {componentMargin}", $"-{componentMargin} -{componentMargin * 2}");

            const float rowHeight = 0.16f;
            const float rowHeightMargin = 0.032f;

            var rowOffsetY = 0.78f;

            var yesText = Lang("UiButton_Yes", userIdUserIDString);
            var noText = Lang("UiButton_No", userIdUserIDString);
            var updateText = Lang("UiUpdate", userIdUserIDString);

            //#################### EntityName ####################
            var rowEntityName = UIHelper.Row(mainContainer, panelRow1, UIProximitySystemRow1Panel + ".EntityName",
                $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
            UIHelper.Text(mainContainer, rowEntityName, GetEntityDisplayName(door), "0.02 0.2", "0.78 0.7");
            rowOffsetY -= rowHeight + rowHeightMargin;
            //####################################################################################################

            //#################### AutoClosingEnabled ####################
            var isAutoClosingEnabled = !doorCloserData.IsAutoClosingDisabled && IsAutoClosingEnabledForEntity(door, autoClosingPlayerSettings);

            var rowAutoClosingEnabled = UIHelper.Row(mainContainer, panelRow1, UIProximitySystemRow1Panel + ".AutoClosingEnabled",
                $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
            UIHelper.Text(mainContainer, rowAutoClosingEnabled, Lang("UiAutoClosingEnabledTitle", userIdUserIDString), "0.02 0.2", "0.78 0.7");
            UIHelper.ToggleButton(mainContainer, rowAutoClosingEnabled, isAutoClosingEnabled,
                $"ultimatelocker.autoclosing.cmd toggleEntityAutoClosingEnabled", "0.8 0.1", "0.99 0.85",
                yesText, noText);

            rowOffsetY -= rowHeight + rowHeightMargin;
            //####################################################################################################

            //#################### DoorCloserDelayTime ####################
            var rowDoorCloserDelayTime = UIHelper.Row(mainContainer, panelRow1, UIProximitySystemRow1Panel + ".DoorCloserDelayTime",
                $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
            UIHelper.Text(mainContainer, rowDoorCloserDelayTime, Lang("UiClosingDelay", userIdUserIDString), "0.02 0.2", "0.78 0.7", fontSize: 8);
            UIHelper.Input(mainContainer, rowDoorCloserDelayTime, "ultimatelocker.autoclosing.cmd proximitySystemNewDelayTime", doorCloserData.DelayTime.ToString(),
                "0.46 0.1", "0.68 0.85", textAlign: TextAnchor.MiddleCenter, maxLength: 4, isPassword: false, autofocus: false, cursorEnabled: false);
            UIHelper.UpdateButton(mainContainer, rowDoorCloserDelayTime, $"ultimatelocker.autoclosing.cmd updateEntityAutoClosingDelayTime",
                updateText, "0.72 0.1", "0.99 0.85", fadeIn: 0.5f);

            rowOffsetY -= rowHeight + rowHeightMargin;
            //####################################################################################################

            //#################### DisableProximitySystem ####################
            rowOffsetY -= 0.22f;
            var rowDisableProximitySystem = UIHelper.Row(mainContainer, panelRow1, UIProximitySystemRow1Panel + ".DisableProximitySystem",
                $"0.0 {rowOffsetY}", $"1.0 {rowOffsetY + rowHeight}");
            UIHelper.Text(mainContainer, rowDisableProximitySystem, Lang("AlwaysShowUI", userIdUserIDString), "0.02 0.2", "0.78 0.7");
            UIHelper.ToggleButton(mainContainer, rowDisableProximitySystem, autoClosingPlayerSettings.ProximitySystemUIEnabled,
                $"ultimatelocker.autoclosing.cmd toggleProximitySystemUIEnabled", "0.8 0.1", "0.99 0.85",
                yesText, noText);

            rowOffsetY -= rowHeight + rowHeightMargin;
            //####################################################################################################

            CuiHelper.AddUi(player, mainContainer);
        }

        private UIHelper.RowCuiPanel ScrollViewAddRow(CuiElementContainer container, string panelName, float offsetY,
            float componentLeftMargin, float componentHeight, float componentRightMargin, string text, string commandAction, bool toggleState,
            BasePlayer? player = null, int textFontSize = 11, bool noButton = false)
        {
            var rowLockersLock = UIHelper.Row($"{panelName}", "0 1", "1 1",
                $"{componentLeftMargin} {offsetY - componentHeight}", $"{componentRightMargin} {offsetY}");
            rowLockersLock.Render = () =>
            {
                if (!noButton)
                    UIHelper.Text(container, rowLockersLock.PanelName, text, "0.02 0.2", "0.78 0.7", textFontSize);
                else
                    UIHelper.Text(container, rowLockersLock.PanelName, text, "0.02 0.2", "0.99 0.7", textFontSize);

                if (!noButton)
                {
                    if (player != null)
                    {
                        var userId = player.UserIDString;
                        UIHelper.ToggleButton(container, rowLockersLock.PanelName, toggleState, $"{commandAction}", "0.8 0.1", "0.99 0.85",
                            Lang("UiButton_Yes", userId), Lang("UiButton_No", userId));
                    }
                    else
                        UIHelper.ToggleButton(container, rowLockersLock.PanelName, toggleState, $"{commandAction}", "0.8 0.1", "0.99 0.85");
                }
            };

            return rowLockersLock;
        }

        private UIHelper.RowCuiPanel ScrollViewAddAutoClosingSettingRow(CuiElementContainer container, string panelName, float offsetY,
            float componentLeftMargin, float componentHeight, float componentRightMargin, string text, int delayTime, string commandDelayTimeUpdate,
            string? commandUpdate, string? commandRemove = null, int fontSize = 10, BasePlayer? player = null)
        {
            var rowLockersLock = UIHelper.Row($"{panelName}", "0 1", "1 1",
                $"{componentLeftMargin} {offsetY - componentHeight}", $"{componentRightMargin} {offsetY}");
            rowLockersLock.Render = () =>
            {
                UIHelper.Text(container, rowLockersLock.PanelName, text, "0.02 0.2", "0.30 0.7", fontSize);
                UIHelper.Input(container, rowLockersLock.PanelName, commandDelayTimeUpdate, delayTime.ToString(), "0.30 0.1", "0.45 0.85",
                    textAlign: TextAnchor.MiddleCenter, maxLength: 4);

                if (player != null)
                {
                    var userId = player.UserIDString;

                    const string cmdRemoveAnchorMin = "0.74 0.1";
                    const string cmdRemoveAnchorMax = "0.99 0.85";

                    var useRemovePosition = commandRemove == null;
                    if (!string.IsNullOrWhiteSpace(commandUpdate))
                    {
                        UIHelper.UpdateButton(container, rowLockersLock.PanelName, commandUpdate, Lang("UiUpdateAll", userId),
                            useRemovePosition ? cmdRemoveAnchorMin : "0.47 0.1", useRemovePosition ? cmdRemoveAnchorMax : "0.72 0.85", fontSize: 10, fadeIn: 0.5f);
                    }

                    if (!string.IsNullOrWhiteSpace(commandRemove))
                    {
                        UIHelper.DeleteButton(container, rowLockersLock.PanelName, commandRemove, Lang("UiRemoveAll", userId),
                            cmdRemoveAnchorMin, cmdRemoveAnchorMax, fontSize: 10, fadeIn: 0.5f);
                    }
                }
            };

            return rowLockersLock;
        }

        private static string GenerateRandomCode()
        {
            return Random.Range(1000, 9999).ToString();
        }

        private static string PadWithZeros(string input)
        {
            return string.IsNullOrWhiteSpace(input) ? input : input.PadLeft(4, '0');
        }

        private static bool IsValidLockCode(string? code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (code.Length > 4) return false;
            return int.TryParse(code, out var number) && number >= 0;
        }

        private bool IsValidDelayTime(string? delayTimeString)
        {
            if (string.IsNullOrWhiteSpace(delayTimeString) || !delayTimeString.IsNumeric()) return false;
            if (!int.TryParse(delayTimeString, out var delayTime)) return false;
            return delayTime >= _config.AutoClosingConfiguration.MinimumClosingDelayTime &&
                   delayTime <= _config.AutoClosingConfiguration.MaximumClosingDelayTime;
        }

        private int GetValidDelayTime(string? delayTimeString)
        {
            if (string.IsNullOrWhiteSpace(delayTimeString) || !IsValidDelayTime(delayTimeString))
                return _config.AutoClosingConfiguration.DefaultClosingDelayTime;
            var delayTime = int.Parse(delayTimeString);
            return Mathf.Clamp(delayTime, _config.AutoClosingConfiguration.MinimumClosingDelayTime, _config.AutoClosingConfiguration.MaximumClosingDelayTime);
        }

        private static PlayerInfo? FindPlayerInfoBySteamID(ulong steamID)
        {
            var basePlayer = BasePlayer.allPlayerList.FirstOrDefault(p => p.userID == steamID);
            var serverUser = ServerUsers.Get(steamID);
            return ConvertToPlayerInfo(basePlayer, serverUser);
        }

        private static List<ulong> ParseSteamIdList(List<string>? userIdList)
        {
            var steamIdList = new List<ulong>();
            if (userIdList == null) return steamIdList;

            foreach (var userId in userIdList)
            {
                if (ulong.TryParse(userId, out var steamId)) steamIdList.Add(steamId);
            }

            return steamIdList;
        }

        private static PlayerInfo? ConvertToPlayerInfo(BasePlayer? basePlayer, ServerUsers.User? serverUser = null)
        {
            if (basePlayer == null && serverUser == null)
                return null;

            var playerInfo = new PlayerInfo
            {
                Steamid = basePlayer != null ? basePlayer.userID : serverUser!.steamid,
                DisplayName = basePlayer != null ? basePlayer.displayName : serverUser!.username,
                Transform = basePlayer != null ? basePlayer.transform : null,
                IsBasePlayer = basePlayer != null,
                PlayerObject = basePlayer != null ? basePlayer : serverUser
            };

            return playerInfo;
        }

        // TODO da rivedere, creare una lista unica List<BaseEntity> baseEntityList e discriminare per tipo, come fatto per UpdateAllCodeLockAuthShareOrSettings()
        private IEnumerator LockSetLocked(PlayerInfo? player, List<BaseLock>? lockList, List<ModularCar>? modularCarList, List<SteeringWheel>? steeringWheelList, bool isLocked)

        {
            if (lockList == null && modularCarList == null && steeringWheelList == null)
                yield break;

            var count = 0;

            if (lockList != null)
            {
                foreach (var locker in lockList)
                {
                    using (var flags = locker.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, isLocked);

                    if (++count % CoroutineIteratorsPerFrame == 0)
                        yield return CoroutineEx.waitForEndOfFrame;
                }
            }

            if (modularCarList != null)
            {
                foreach (var modularCar in modularCarList)
                {
                    using (var flags = modularCar.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(ModularCarCodeLock.FLAG_CENTRAL_LOCKING, isLocked);

                    if (++count % CoroutineIteratorsPerFrame == 0)
                        yield return CoroutineEx.waitForEndOfFrame;
                }
            }

            if (steeringWheelList != null)
            {
                foreach (var steeringWheel in steeringWheelList)
                {
                    using (var flags = steeringWheel.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, isLocked);

                    if (++count % CoroutineIteratorsPerFrame == 0)
                        yield return CoroutineEx.waitForEndOfFrame;
                }
            }

            if (player is { PlayerObject: BasePlayer basePlayer })
            {
                timer.Once(1f,
                    () =>
                    {
                        var userId = basePlayer.UserIDString;
                        PlayerShowToast(basePlayer,
                            Lang("CodeLockListMessage", userId,
                                isLocked
                                    ? Lang("LockerStatus_Locked", userId)
                                    : Lang("LockerStatus_Unlocked", userId),
                                count.ToString()));
                    });
            }
        }

        // TODO da rivedere, creare una lista unica List<BaseEntity> baseEntityList e discriminare per tipo, come fatto per UpdateAllCodeLockAuthShareOrSettings()
        private IEnumerator RemoveLock(PlayerInfo? player, List<BaseLock>? lockList, List<ModularCar>? modularCarList, List<SteeringWheel>? steeringWheelList)
        {
            if (lockList == null && modularCarList == null && steeringWheelList == null)
                yield break;

            var count = 0;

            if (lockList != null)
            {
                foreach (var locker in lockList)
                {
                    locker.Kill();

                    if (++count % CoroutineIteratorsPerFrame == 0)
                        yield return CoroutineEx.waitForEndOfFrame;
                }
            }

            if (modularCarList != null)
            {
                foreach (var modularCar in modularCarList)
                {
                    modularCar.CarLock.RemoveLock();

                    if (++count % CoroutineIteratorsPerFrame == 0)
                        yield return CoroutineEx.waitForEndOfFrame;
                }
            }

            if (steeringWheelList != null)
            {
                foreach (var steeringWheel in steeringWheelList)
                {
                    steeringWheel.BoatLock.RemoveLock();

                    using (var flags = steeringWheel.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, false);

                    if (++count % CoroutineIteratorsPerFrame == 0)
                        yield return CoroutineEx.waitForEndOfFrame;
                }
            }

            if (player is { PlayerObject: BasePlayer basePlayer })
            {
                timer.Once(1f,
                    () =>
                    {
                        var userId = basePlayer.UserIDString;
                        PlayerShowToast(basePlayer,
                            Lang("CodeLockListMessage", userId, Lang("LockerStatus_Removed", userId),
                                count.ToString()));
                    });
            }
        }

        private static void ShowLockerUI(BasePlayer player, bool hasAlpha = false)
        {
            if (player == null) return;

            DestroyLockerUI(player);
            var container =
                GuiHelper.ContainerWithOffset(UILocker, "0 0 0 0", "0.5 0.5", "0.5 0.5", "-20 -25", "20 25");

            var isKeyLock = false;
            string? image = null;

            var activeItem = player.GetActiveItem();
            if (activeItem == null)
            {
                return;
            }

            switch (activeItem.info.itemid)
            {
                case KeyLockItemID:
                    image = GetImage(KeyLockOnDeployImageName);
                    isKeyLock = true;
                    break;
                case CodeLockItemID:
                case FlightControlCodeLockItemID:
                    image = GetImage(CodeLockOnDeployImageName);
                    break;
            }

            var ancorMax = isKeyLock ? "1 0.8" : "1 1";
            if (!string.IsNullOrWhiteSpace(image))
                GuiHelper.ImageUrl(container, UILocker, image, "0 0", ancorMax, hasAlpha ? "1 1 1 0.5" : "1 1 1 1");
            else
                GuiHelper.Image(container, UILocker, CodeLockItemID, 0, "0 0", ancorMax, hasAlpha ? "1 1 1 0.5" : "1 1 1 1");

            CuiHelper.AddUi(player, container);
        }

        private static void DestroyLockerUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UILocker);
        }

        private static void DestroyAutoLockUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIAutoLock);
        }

        private static void DestroyProximitySystemIconUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIProximitySystemIcon);
            CuiHelper.DestroyUi(player, UIProximitySystem);
        }

        private static void DestroyProximitySystemUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIProximitySystem);
        }

        private static string? GetImage(string? imageName, ulong skin = 0, bool returnUrl = false)
        {
            return self == null || string.IsNullOrEmpty(imageName)
                ? null
                : (string)self.ImageLibrary?.Call("GetImage", imageName, skin, returnUrl)!;
        }

        private static void RunEffect(string effect, BaseEntity baseEntity)
        {
            if (string.IsNullOrEmpty(effect) || baseEntity == null) return;

            // if (_config.DisableCodeLockEffects) return;
            // if (_config.DisableCodeLockEffectsIfInNoClipOrVanish && IsInvisible(player)) return;

            Effect.server.Run(effect, baseEntity.transform.position);
        }

        private static void RunEffect(string effect, BasePlayer player)
        {
            if (string.IsNullOrEmpty(effect) || player == null) return;

            // if (_config.DisableCodeLockEffects) return;
            // if (_config.DisableCodeLockEffectsIfInNoClipOrVanish && IsInvisible(player)) return;

            Effect.server.Run(effect, player.transform.position);
        }

        private bool IsEntityOwner(BaseEntity? baseEntity, BasePlayer basePlayer)
        {
            if (baseEntity == null || !basePlayer.userID.IsSteamId())
                return false;

            if (!baseEntity.OwnerID.IsSteamId())
                return false;

            if (baseEntity.OwnerID.IsSteamId() && baseEntity.OwnerID.Equals(basePlayer.userID))
                return true;

            var clanInfo = GetClanInfo(basePlayer);
            if (clanInfo == null || clanInfo.ClanMemberUserIdList.IsNullOrEmpty()) return false;

            var memberSteamIdList = clanInfo.ClanMemberUserIdList
                .Select(ulong.Parse)
                .Where(CheckPlayerHasShareLocksWithClanTeamEnabled)
                .ToList();

            //TODO da rimuovere
            // if (baseEntity is CodeLock codeLock)
            // {
            //     memberSteamIdList.AddRange(codeLock.whitelistPlayers);
            // }

            return memberSteamIdList.Contains(baseEntity.OwnerID);
        }

        //Verifica se il giocatore ha comprato il DLC/Skin, se è una skin a pagamento o di un DLC, o se è una skin gratuita.
        private bool IsSkinOwned(BasePlayer player, ulong skinId)
        {
            if (!_steamWorkshopInitialized) return false;

            if (skinId == 0 || _config.DisableDlcSkinOwnershipCheck)
                return true;

            if (SkinIdWhitelisted.Contains(skinId))
                return true;

            if (permission.UserHasPermission(player.UserIDString, PermissionBypassDlcSkinRequired))
                return true;

            var playerId = player.userID;

            // Ottieni o crea la cache per il giocatore
            if (!PlayerDlcSkinOwnedCache.TryGetValue(playerId, out var ownedSkins))
            {
                ownedSkins = new Dictionary<ulong, bool>();
                PlayerDlcSkinOwnedCache[playerId] = ownedSkins;
            }

            // Cerca nella cache del player
            if (ownedSkins.TryGetValue(skinId, out var isOwned))
            {
                return isOwned;
            }

            if (!_workshopIdToItemId.TryGetValue(skinId, out var itemId) || itemId == 0 || !_itemIdToSkin.TryGetValue(itemId, out var skin))
            {
                ownedSkins.Add(skinId, true);
                return true;
            }

            var hasOwnership = (skin.invItem && skin.invItem.HasUnlocked(player)) || player.blueprints.steamInventory.HasItem(itemId);
            ownedSkins.Add(skinId, hasOwnership);
            return hasOwnership;
        }

        private static string GetEntityDisplayName(BaseEntity? entity)
        {
            if (entity == null) return "";

            if (_prefabToDisplayName == null)
                BuildPrefabToDisplayNameMap();

            var shortPrefabName = entity.ShortPrefabName;

            return _prefabToDisplayName?.GetValueOrDefault(shortPrefabName, shortPrefabName) ?? "";
        }

        /// <summary>
        /// Costruisce la lookup table <see cref="_prefabToDisplayName"/> iterando
        /// tutti gli <see cref="ItemDefinition"/> registrati e mappando i loro
        /// prefab spawnati al rispettivo <c>displayName.english</c>.
        /// </summary>
        /// <remarks>
        /// Per ogni item vengono letti due possibili componenti:
        /// <list type="bullet">
        ///   <item><description>
        ///     <see cref="ItemModDeployable"/> — prefab spawnato quando l'item viene
        ///     deployato (es. casse, adapter, mobili).
        ///   </description></item>
        ///   <item><description>
        ///     <see cref="ItemModEntity"/> — prefab associato all'item durante l'uso
        ///     (es. armi, strumenti).
        ///   </description></item>
        /// </list>
        /// <para>
        /// La chiave della mappa è il <c>ShortPrefabName</c> estratto dal path completo
        /// del prefab tramite <see cref="GetShortPrefabName"/>. Il confronto è
        /// case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>).
        /// </para>
        /// </remarks>
        private static void BuildPrefabToDisplayNameMap()
        {
            _prefabToDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var itemDef in ItemManager.itemList)
            {
                if (itemDef.displayName?.english == null) continue;

                // ItemModDeployable — prefab spawnato quando l'item viene deployato
                var deployable = itemDef.GetComponent<ItemModDeployable>();
                if (deployable?.entityPrefab?.resourcePath != null)
                {
                    var shortname = GetShortPrefabName(deployable.entityPrefab.resourcePath);
                    _prefabToDisplayName[shortname] = itemDef.displayName.english;
                }

                // ItemModEntity — prefab associato all'item (es. weapons, tools)
                var modEntity = itemDef.GetComponent<ItemModEntity>();
                if (modEntity?.entityPrefab?.resourcePath != null)
                {
                    var shortname = GetShortPrefabName(modEntity.entityPrefab.resourcePath);
                    _prefabToDisplayName[shortname] = itemDef.displayName.english;
                }
            }
        }

        /// <summary>
        /// Estrae il <c>ShortPrefabName</c> dal path completo di un prefab,
        /// rimuovendo la directory e l'estensione <c>.prefab</c>.
        /// </summary>
        /// <remarks>
        /// Esempio: <c>"assets/prefabs/.../storageadaptor.prefab"</c> → <c>"storageadaptor"</c>.
        /// </remarks>
        /// <param name="prefabPath">
        ///   Il path completo del prefab da cui estrarre il nome.
        /// </param>
        /// <returns>
        ///   Il nome del file senza directory e senza estensione <c>.prefab</c>.
        /// </returns>
        private static string GetShortPrefabName(string prefabPath)
        {
            var lastSlash = prefabPath.LastIndexOf('/');
            var start = lastSlash >= 0 ? lastSlash + 1 : 0;

            var fileName = prefabPath.Substring(start);

            // Rimuove l'estensione .prefab se presente
            if (fileName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^".prefab".Length];

            return fileName;
        }

        private IPlayer? GetIPlayer(string partialNameOrId)
        {
            if (string.IsNullOrWhiteSpace(partialNameOrId)) return null;
            var player = covalence.Players.FindPlayer(partialNameOrId);
            return player;
        }

        private ClanInfo? GetClanInfoBySteamID(ulong userID)
        {
            return GetClanInfo(BasePlayer.FindByID(userID));
        }

        private ClanInfo? GetClanInfo(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId())
                return null;

            var clanInfo = new ClanInfo();
            //Native Rust Team
            if (player.Team != null)
            {
                clanInfo.ClanMemberUserIdList = player.Team.members.Select(m => m.ToString()).ToList();
                return clanInfo;
            }

            //Clans plugin
            if (Clans != null)
            {
                if (GetClanOf(player.userID) == null)
                {
                    return null;
                }

                var clanMembers = GetClanMembers(player.userID);
                foreach (var member in clanMembers)
                {
                    clanInfo.ClanMemberUserIdList.Add(member);
                }

                return clanInfo;
            }

            return null;
        }

        private string? GetClanOf(ulong playerID) => Clans?.Call<string>("GetClanOf", playerID);
        private JObject GetClan(string tag) => Clans?.Call<JObject>("GetClan", tag);
        private JArray GetClanMembers(string tag) => (JArray)GetClan(tag)?.SelectToken("members");

        private List<string> GetClanMembers(ulong playerId) =>
            Clans?.Call<List<string>>("GetClanMembers", playerId) ?? new List<string>();

        /// <summary>
        /// Generic helper method for Harmony prefix patches that call Oxide hooks.
        /// Automatically extracts player from RPCMessage if present.
        /// </summary>
        /// <param name="hookName">The name of the Oxide hook to call</param>
        /// <param name="hookArgs">Arguments to pass to the hook</param>
        /// <returns>True to run original code, False to block original code</returns>
        private static bool CallHookFromHarmonyPrefix(string hookName, params object[] hookArgs)
        {
            try
            {
                // Check if hook name is valid
                if (string.IsNullOrWhiteSpace(hookName)) return true; // Run normal code

                // Check if arguments array is valid
                if (hookArgs == null || hookArgs.Length == 0) return true; // Run normal code

                // Process arguments - extract player from RPCMessage if present
                var processedArgs = new List<object>();

                foreach (var arg in hookArgs)
                {
                    if (arg == null) return true; // Run normal code

                    // If argument is RPCMessage, extract the player
                    if (arg is BaseEntity.RPCMessage msg)
                    {
                        if (msg.player == null) return true; // Run normal code
                        processedArgs.Add(msg.player);
                    }
                    else
                    {
                        processedArgs.Add(arg);
                    }
                }

                // Call the hook with processed arguments
                if (Interface.CallHook(hookName, processedArgs.ToArray()) != null)
                    return false; // Block original code
            }
            catch (Exception e)
            {
                self?.Puts($"Exception - Hook {hookName}: {e.Message}");
            }

            return true; // Run normal code
        }

        // private static bool GenericHarmonyPrefix(string hookName, BaseEntity instance, BaseEntity.RPCMessage msg)
        // {
        //     return msg.player == null || GenericHarmonyPrefix(hookName, instance, msg.player);
        // }
        //
        // private static bool GenericHarmonyPrefix(string hookName, BaseEntity instance, BasePlayer player)
        // {
        //     try
        //     {
        //         if (string.IsNullOrWhiteSpace(hookName) || instance == null || player == null) return true; //Run Normal code
        //
        //         if (Interface.CallHook(hookName, instance, player) != null)
        //             return false; //Block Original code
        //     }
        //     catch (Exception e)
        //     {
        //         self?.Puts($"Exception - Hook {hookName}: {e.Message}");
        //     }
        //
        //     return true; //Run Normal code
        // }

        private static class ModularCarCodeLock_Settings
        {
            public static void Postfix_TryAddALock(ModularCarCodeLock __instance, string code, ulong userID)
            {
                Interface.CallHook("OnModularCarLockAdded", (object)__instance, code, userID);
            }

            public static void Postfix_RemoveLock(ModularCarCodeLock __instance)
            {
                Interface.CallHook("OnModularCarLockRemoved", (object)__instance);
            }
        }

        private static class Workbench_Settings
        {
            public static bool Prefix_RPC_TechTreeUnlock(BaseEntity.RPCMessage msg, Workbench __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnWorkbenchTechTreeUnlock", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class SmartSwitch_ToggleSwitch
        {
            public static bool Prefix(BaseEntity.RPCMessage msg, SmartSwitch __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnSmartSwitchToggle", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class TimerSwitch_SVSwitch
        {
            public static bool Prefix(BaseEntity.RPCMessage msg, TimerSwitch __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnTimerSwitchToggle", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class CustomTimerSwitch_SERVER_SetTime
        {
            public static bool Prefix(BaseEntity.RPCMessage msg, CustomTimerSwitch __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("CanTimerSwitchSetTime", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class FogMachine_Toggle
        {
            public static bool Prefix_SetFogOn(BaseEntity.RPCMessage msg, FogMachine __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnFogMachineToggle", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_SetFogOff(BaseEntity.RPCMessage msg, FogMachine __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnFogMachineToggle", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class StrobeLight_Settings
        {
            public static bool Prefix_SetStrobe(BaseEntity.RPCMessage msg, StrobeLight __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnStrobeLightToggle", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_SetStrobeSpeed(BaseEntity.RPCMessage msg, StrobeLight __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnStrobeSpeedChange", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class AudioVisualisationEntity_Settings
        {
            public static bool Prefix_ServerUpdateSettings(BaseEntity.RPCMessage msg,
                AudioVisualisationEntity __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnAudioVisualisationServerUpdateSettings", (object)__instance,
                            (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private class SkinPatch
        {
            public static bool Prefix_Skinner_SkinDeployable(BaseCombatEntity baseCombatEntity, ulong uID, ref BaseCombatEntity __result)
            {
                try
                {
                    if (self == null || baseCombatEntity == null) return true; // Run Normal code

                    var player = BasePlayer.FindAwakeOrSleepingByID(baseCombatEntity.OwnerID);
                    if (player == null) return true; // Run Normal code

                    self.RestoreLockAfterReskin(baseCombatEntity, player, uID, null);
                    return true; // Run Normal code
                }
                catch (Exception e)
                {
                    self?.PrintError($"[Prefix_Skinner_SkinDeployable] - \nERROR: {e.Message}\nSTACK TRACE: {e.StackTrace}\n");
                }

                return true; // Run Normal code
            }
        }

        private static class NavalPatch
        {
            public static bool Prefix_BoatBuildingStation_CanEnterEditMode(BoatBuildingStation __instance, BasePlayer player, bool sendErrorToasts)
            {
                return CallHookFromHarmonyPrefix("BoatBuildingStationCanEnterEditMode", __instance, player);
            }

            public static bool Prefix_BoatBuildingStation_CanClearArea(BoatBuildingStation __instance, BasePlayer player = null)
            {
                return CallHookFromHarmonyPrefix("BoatBuildingStationCanClearArea", __instance, player);
            }

            public static bool Prefix_BoatBuildingStation_FinishBuilding(BoatBuildingStation __instance, BaseEntity.RPCMessage msg)
            {
                return CallHookFromHarmonyPrefix("BoatBuildingStationFinishBuilding", __instance, msg);
            }

            public static bool Prefix_Anchor_CanBeRaised(Anchor __instance, BasePlayer player)
            {
                return CallHookFromHarmonyPrefix("AnchorCanBeRaised", __instance, player);
            }

            public static bool Prefix_Anchor_CanBeLowered(Anchor __instance, BasePlayer player)
            {
                return CallHookFromHarmonyPrefix("AnchorCanBeLowered", __instance, player);
            }

            // Blocca lo sparo con il cannone, ma non verrà implementato per non aggravare sulle performance, in quanto ce già il controllo se il player può usare o meno il cannone
            // public static bool Prefix_Cannon_RequestLightFuse(Cannon __instance, BaseEntity.RPCMessage msg)
            // {
            //     return CallGenericHarmonyPrefix("CannonCanRequestLightFuse", __instance, msg);
            // }
        }

        private static class Signage_Settings
        {
            // public static bool Prefix_CanUpdateSign(BasePlayer player, Signage __instance)
            // {
            //     try
            //     {
            //         if (player == null || __instance == null) return true; //Run Normal code
            //
            //         if (Interface.CallHook("OnSignageCanUpdateSign", (object)__instance, player) != null)
            //             return false; //Block Original code
            //         
            //         // if (Interface.CallHook("OnSignageUpdateSign", (object)__instance, (object)msg.player) != null)
            //         //     return false; //Block Original code
            //     }
            //     catch (Exception e)
            //     {
            //         self?.Puts($"Exception: {e.Message}");
            //     }
            //
            //     return true; //Run Normal code
            // }

            public static bool Prefix_LockSign(BaseEntity.RPCMessage msg, Signage __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnSignageLockUnlockSign", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_UnLockSign(BaseEntity.RPCMessage msg, Signage __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnSignageLockUnlockSign", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_ShutterFrameToggleShutter(BaseEntity.RPCMessage msg, ShutterFrame __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("CanShutterFrameToggleShutter", (object)msg.player, (object)__instance) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_OrnateFrameConfigureFrame(BaseEntity.RPCMessage msg, OrnateFrame __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("CanOrnateFrameConfigureFrame", (object)msg.player, (object)__instance) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class NeonSign_Settings
        {
            public static bool Prefix_SetAnimationSpeed(BaseEntity.RPCMessage msg, NeonSign __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnNeonSignSetAnimationSpeed", (object)__instance, (object)msg.player) !=
                        null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_UpdateNeonColors(BaseEntity.RPCMessage msg, NeonSign __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnUpdateNeonColors", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class SearchLight_Settings
        {
            public static bool Prefix_UseLight(BaseEntity.RPCMessage msg, SearchLight __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnSearchLightUseLight", (object)__instance, (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class PoweredRemoteControlEntity_Settings
        {
            public static bool Prefix_CanChangeID(BasePlayer player, PoweredRemoteControlEntity __instance)
            {
                try
                {
                    if (player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnPoweredRemoteControlEntityCanChangeID", (object)player,
                            (object)__instance) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Prefix_CanControl(ulong playerID, PoweredRemoteControlEntity __instance)
            {
                try
                {
                    if (!playerID.IsSteamId() || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnPoweredRemoteControlEntityCanControl", playerID, (object)__instance) !=
                        null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class RFBroadcaster_Settings
        {
            public static bool Prefix_ServerSetFrequency(BaseEntity.RPCMessage msg, RFBroadcaster __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnRFBroadcasterCanChangeFrequency", (object)__instance,
                            (object)msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class RFReceiver_Settings
        {
            public static bool Prefix_ServerSetFrequency(BaseEntity.RPCMessage msg, RFReceiver __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnRFReceiverCanChangeFrequency", (object)__instance, (object)msg.player) !=
                        null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class AutoTurret_Settings
        {
            public static bool Prefix_CanControl(ulong playerID, AutoTurret __instance)
            {
                try
                {
                    if (!playerID.IsSteamId() || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnAutoTurretCanControl", playerID, (object)__instance) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        // private static class GrowableEntity_Settings
        // {
        //     public static bool Prefix_PickFruit(BasePlayer player, bool eat, GrowableEntity __instance)
        //     {
        //         try
        //         {
        //             if (player == null || __instance == null) return true; //Run Normal code
        //
        //             if (Interface.CallHook("CanPickFruit", (object)__instance, player, eat) != null)
        //                 return false; //Block Original code
        //         }
        //         catch (Exception e)
        //         {
        //             self?.Puts($"Exception: {e.Message}");
        //         }
        //
        //         return true; //Run Normal code
        //     }
        // }

        private static class IndustrialCrafter_Settings
        {
            public static bool Prefix_SvSwitch(BaseEntity.RPCMessage msg, IndustrialCrafter __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code

                    if (Interface.CallHook("OnIndustrialCrafterSwitchToggle", (object)__instance, (object)msg.player) !=
                        null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        private static class MedievalEntity_Settings
        {
            public static bool BatteringRam_Prefix_RPC_OpenDoor(BaseEntity.RPCMessage rpc, BatteringRam __instance)
            {
                try
                {
                    if (rpc.player == null || __instance == null) return true; //Run Normal code
                    if (Interface.CallHook("CanBatteringRamOpenDoor", (object)__instance, (object)rpc.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool BatteringRam_Prefix_RPC_CloseDoor(BaseEntity.RPCMessage rpc, BatteringRam __instance)
            {
                try
                {
                    if (rpc.player == null || __instance == null) return true; //Run Normal code
                    if (Interface.CallHook("CanBatteringRamCloseDoor", __instance, rpc.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool Catapult_Prefix_SERVER_ReloadStart(BaseEntity.RPCMessage msg, Catapult __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code
                    if (Interface.CallHook("CanCatapultReloadStart", __instance, msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool SiegeTower_Prefix_RPC_OpenDoor(BaseEntity.RPCMessage rpc, Door __instance)
            {
                try
                {
                    if (rpc.player == null || __instance == null || __instance is not SiegeTowerDoor) return true; //Run Normal code
                    if (Interface.CallHook("CanSiegeTowerOpenDoor", __instance, rpc.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool SiegeTower_Prefix_RPC_CloseDoor(BaseEntity.RPCMessage rpc, Door __instance)
            {
                try
                {
                    if (rpc.player == null || __instance == null || __instance is not SiegeTowerDoor) return true; //Run Normal code
                    if (Interface.CallHook("CanSiegeTowerCloseDoor", __instance, rpc.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }

            public static bool BaseSiegeWeapon_Prefix_SERVER_StartPulling(BaseEntity.RPCMessage msg, BaseSiegeWeapon __instance)
            {
                try
                {
                    if (msg.player == null || __instance == null) return true; //Run Normal code
                    if (Interface.CallHook("CanBaseSiegeWeaponStartPulling", __instance, msg.player) != null)
                        return false; //Block Original code
                }
                catch (Exception e)
                {
                    self?.Puts($"Exception: {e.Message}");
                }

                return true; //Run Normal code
            }
        }

        public class LockerBehaviour : MonoBehaviour
        {
            private BasePlayer? _player;
            private Coroutine? _raycastCoroutine;

            private void Awake()
            {
                _player = GetComponent<BasePlayer>();
                if (self == null || _player == null || _player.IsNpc || _player.IsDestroyed || _player.GetActiveItem() == null)
                {
                    Destroy(this);
                    return;
                }

                if (!self.permission.UserHasPermission(_player.UserIDString, PermissionUse))
                {
                    Destroy(this);
                    return;
                }

                // Avvia la coroutine per il controllo visivo (Mostrare icona della serratura)
                _raycastCoroutine = ServerMgr.Instance.StartCoroutine(RaycastLoop());
            }

            public void OnDestroy()
            {
                // Ferma la coroutine per evitare che continui a girare in background
                if (_raycastCoroutine != null) ServerMgr.Instance.StopCoroutine(_raycastCoroutine);

                // Distrugge l'UI quando il componente viene rimosso
                if (_player != null) DestroyLockerUI(_player);
            }

            private IEnumerator RaycastLoop()
            {
                // Il loop continua finché il componente esiste e il giocatore è valido
                while (_player is not null && !_player.IsNpc && !_player.IsDestroyed && self != null)
                {
                    var entityLookedAt = GetEntityByRayCast(_player);
                    if (entityLookedAt is not null && self.CanDeployLock(_player, entityLookedAt))
                    {
                        ShowLockerUI(_player);
                    }
                    else
                    {
                        DestroyLockerUI(_player);
                    }

                    // Attendi 0.2 secondi prima del prossimo controllo.
                    yield return new WaitForSeconds(0.2f);
                }
            }

            public void HandleInput(InputState input)
            {
                if (self == null || _player == null || _player.IsNpc || _player.IsDestroyed || input == null)
                {
                    Destroy(this);
                    return;
                }

                var activeItem = _player.GetActiveItem();
                if (activeItem == null)
                {
                    Destroy(this);
                    return;
                }

                if (!activeItem.info.itemid.Equals(KeyLockItemID) && !activeItem.info.itemid.Equals(CodeLockItemID) && !activeItem.info.itemid.Equals(FlightControlCodeLockItemID))
                {
                    Destroy(this);
                    return;
                }

                if (self._config.AutoLockConfiguration.UseOnlyKeyLock && (activeItem.info.itemid.Equals(CodeLockItemID) || activeItem.info.itemid.Equals(FlightControlCodeLockItemID)))
                {
                    Destroy(this);
                    return;
                }

                if (!self.permission.UserHasPermission(_player!.UserIDString, PermissionUse))
                {
                    Destroy(this);
                    return;
                }

                if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY))
                {
                    return; // Esegue il deploy della serratura solo se è stato premuto il tasto di fuoco
                }

                var entityLookedAt = GetEntityByRayCast(_player);
                if (entityLookedAt == null || !self.CanDeployLock(_player, entityLookedAt))
                {
                    return;
                }

                // Se il deploy della serratura va a buon fine, consuma l'oggetto
                if (self.DeployLock(_player, entityLookedAt))
                {
                    activeItem.UseItem(1);
                    RunEffect(CodeLockDeployEffect, _player);

                    // Il giocatore non può più piazzare serrature se sono finite, quindi il componente non serve più e viene distrutto.
                    if (activeItem.amount <= 0)
                    {
                        Destroy(this);
                    }
                }
            }
        }

        #endregion

        #region Class

        private static class UIHelper
        {
            public static CuiElementContainer MainContainer(string panelName, string anchorMin, string anchorMax, string offsetMin = "", string offsetMax = "",
                string backgroundColor = "0.133 0.133 0.133 1", bool requireCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = backgroundColor,
                                Sprite = "assets/content/ui/ui.background.tile.psd",
                                Material = "assets/content/ui/namefontmaterial.mat"
                            },
                            RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                            CursorEnabled = requireCursor
                        },
                        new CuiElement().Parent = parent, panelName, panelName
                    }
                };
            }

            public static CuiElementContainer SubContainer(string parentName, string panelName, string anchorMin, string anchorMax, string offsetMin = "", string offsetMax = "",
                string backgroundColor = "0.133 0.133 0.133 1", bool requireCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = backgroundColor,
                                Sprite = "assets/content/ui/ui.background.tile.psd",
                                Material = "assets/content/ui/namefontmaterial.mat"
                            },
                            RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                            CursorEnabled = requireCursor
                        },
                        parentName, panelName, panelName
                    }
                };
            }

            public static string ParentPanel(CuiElementContainer container, string parentName, string panelName, string anchorMin, string anchorMax, string offsetMin = "", string offsetMax = "",
                string backgroundColor = "0 0 0 0", bool cursorEnabled = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = backgroundColor },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                    CursorEnabled = cursorEnabled,
                }, parentName, panelName);
                return panelName;
            }

            public static string Header(CuiElementContainer container, string parentName, string panelName, string text,
                string backgroundColor = "0 0 0 0.6", int fontSize = 12, string? closeCommand = null, string? closeUI = null, string anchorMin = "0 0", string anchorMax = "1 1")
            {
                container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = backgroundColor,
                        Sprite = "assets/content/ui/ui.background.tile.psd",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }, parentName, panelName);

                container.Add(new CuiPanel
                {
                    Image =
                    {
                        Sprite = "assets/icons/icon-unlocked.png",
                        Material = "assets/icons/iconmaterial.mat",
                        ImageType = UnityEngine.UI.Image.Type.Simple
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.02 0.1", AnchorMax = "0.07 0.8"
                    },
                }, panelName);

                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = text.ToUpper(), FontSize = fontSize, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft,
                        Color = "0.729 0.733 0.729 1", VerticalOverflow = VerticalWrapMode.Truncate
                    },
                    RectTransform = { AnchorMin = "0.09 0", AnchorMax = "0.8 0.9" }
                }, panelName);

                CloseButtonIcon(container, panelName, closeCommand, closeUI, "0.92 0", "0.998 0.96", 5);

                return panelName;
            }

            public static void ToggleButton(CuiElementContainer container, string parentName, bool toggleState, string command,
                string anchorMin, string anchorMax, string textYes = "YES", string textNo = "NO")
            {
                const string activatedColor = "0.235 0.294 0.149 1";
                const string deactivatedColor = "0.122 0.125 0.114 1";

                const string activatedTextColor = "0.675 0.875 0.286 1";
                const string deactivatedTextColor = "0.243 0.263 0.2 1";

                const int fontSize = 14;

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.122 0.125 0.114 1" },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }, parentName, parentName + ".ToggleButton.Panel");

                container.Add(new CuiButton
                {
                    Button = { Command = command + " True", Color = toggleState ? activatedColor : deactivatedColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.5 1" },
                    Text =
                    {
                        Text = textYes, FontSize = fontSize, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter,
                        Color = toggleState ? activatedTextColor : deactivatedTextColor, VerticalOverflow = VerticalWrapMode.Truncate
                    }
                }, parentName + ".ToggleButton.Panel");

                container.Add(new CuiButton
                {
                    Button = { Command = command + " False", Color = toggleState ? deactivatedColor : activatedColor },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "1 1" },
                    Text =
                    {
                        Text = textNo, FontSize = fontSize, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter,
                        Color = toggleState ? deactivatedTextColor : activatedTextColor, VerticalOverflow = VerticalWrapMode.Truncate
                    }
                }, parentName + ".ToggleButton.Panel");
            }

            public static void CloseButtonIcon(CuiElementContainer container, string panelName, string? closeCommand = null, string? closeUI = null,
                string anchorMin = "0.95 0", string anchorMax = "1.0 0.95", int iconPadding = 4)
            {
                container.Add(new CuiButton
                {
                    Button =
                    {
                        Close = closeUI,
                        Command = closeCommand, Color = "1 0.243 0.173 1",
                        ImageType = UnityEngine.UI.Image.Type.Simple
                    },
                    RectTransform =
                    {
                        AnchorMin = anchorMin, AnchorMax = anchorMax
                    }
                }, panelName, panelName + ".CloseButton");

                container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0.8509805 0.854902 0.8509805 1",
                        Sprite = "assets/icons/close.png",
                        Material = "assets/icons/iconmaterial.mat",
                        ImageType = UnityEngine.UI.Image.Type.Simple
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = $"{iconPadding} {iconPadding}", OffsetMax = $"{-iconPadding} {-iconPadding}"
                    },
                }, panelName + ".CloseButton");
            }

            public static void DeleteButton(CuiElementContainer container, string parentName, string command, string text,
                string anchorMin, string anchorMax, string textColor = "1 1 1 1", string backgroundColor = "1 0.243 0.173 0.5",
                int fontSize = 12, TextAnchor align = TextAnchor.MiddleCenter, float fadeIn = 0.0f)
            {
                Button(container, parentName, command, text, anchorMin, anchorMax, textColor, backgroundColor, fontSize, align, fadeIn);
            }

            public static void UpdateButton(CuiElementContainer container, string parentName, string command, string text,
                string anchorMin, string anchorMax, string textColor = "1 1 1 1", string backgroundColor = "1 0.471 0.078 0.5",
                int fontSize = 12, TextAnchor align = TextAnchor.MiddleCenter, float fadeIn = 0.0f)
            {
                Button(container, parentName, command, text, anchorMin, anchorMax, textColor, backgroundColor, fontSize, align, fadeIn);
            }

            public static void Button(CuiElementContainer container, string parentName, string command, string text,
                string anchorMin, string anchorMax, string textColor = "0.675 0.875 0.286 1", string backgroundColor = "0.235 0.294 0.149 1",
                int fontSize = 12, TextAnchor align = TextAnchor.MiddleCenter, float fadeIn = 0.0f)
            {
                container.Add(new CuiButton
                {
                    Button = { Command = command, Color = backgroundColor, FadeIn = fadeIn },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Text =
                    {
                        Text = text, FontSize = fontSize, Font = "robotocondensed-bold.ttf", Align = align,
                        Color = textColor, VerticalOverflow = VerticalWrapMode.Truncate
                    }
                }, parentName);
            }

            public static string ScrollView(CuiElementContainer container, string parentName, string panelName, float scrollViewHeight,
                string anchorMin, string anchorMax, string offsetMin = "", string offsetMax = "", string backgroundColor = "0 0 0 0")
            {
                var scrollView = new CuiElement
                {
                    Name = panelName,
                    Parent = parentName,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            FadeIn = 0.2f,
                            Color = backgroundColor
                        },
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Elastic,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = new CuiRectTransform
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "1 1",
                                OffsetMin = $"0 {-scrollViewHeight}",
                                OffsetMax = "0 0"
                            },
                            VerticalScrollbar = new CuiScrollbar
                            {
                                Invert = false,
                                AutoHide = false,

                                HandleColor = "1 0.184 0.082 1",
                                PressedColor = "1 0.184 0.082 0.2",
                                HighlightColor = "1 0.184 0.082 0.2", //Color when hovering over the scrollbar

                                HandleSprite = "assets/content/ui/ui.rounded.tga",
                                TrackSprite = "assets/content/ui/ui.background.tile.psd",

                                Size = 10f
                            }
                        },
                        new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                    }
                };
                container.Add(scrollView);

                return panelName;
            }

            public static string Row(CuiElementContainer container, string parentName, string panelName,
                string anchorMin, string anchorMax, string offsetMin = "0 0", string offsetMax = "1 1", string backgroundColor = "0 0 0 1",
                bool cursorEnabled = false)
            {
                container.Add(Row(panelName, anchorMin, anchorMax, offsetMin, offsetMax, backgroundColor, cursorEnabled).CuiPanel, parentName, panelName);
                return panelName;
            }

            public static RowCuiPanel Row(string panelName, string anchorMin, string anchorMax, string offsetMin = "0 0", string offsetMax = "1 1",
                string backgroundColor = "0 0 0 1", bool cursorEnabled = false)
            {
                var cuiPanel = new CuiPanel
                {
                    Image = { Color = backgroundColor },
                    RectTransform =
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    },
                    CursorEnabled = cursorEnabled,
                };

                return new RowCuiPanel
                {
                    CuiPanel = cuiPanel,
                    PanelName = panelName
                };
            }

            public static void Text(CuiElementContainer container, string parentName, string text, string anchorMin,
                string anchorMax, int fontSize = 11, TextAnchor align = TextAnchor.MiddleLeft,
                string textColor = "1 1 1 1", string font = "robotocondensed-bold.ttf")
            {
                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = text.ToUpper(), FontSize = fontSize, Font = font, Align = align, Color = textColor, VerticalOverflow = VerticalWrapMode.Truncate
                    },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }, parentName);
            }

            public static void Input(CuiElementContainer container, string parentName, string command, string text, string anchorMin, string anchorMax,
                int fontSize = 12, TextAnchor textAlign = TextAnchor.MiddleLeft, string textColor = "1 1 1 1", string backgroundColor = "0.133 0.133 0.133 0.8",
                int maxLength = 255, bool isPassword = false, bool autofocus = false, bool cursorEnabled = true)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = backgroundColor },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }, parentName, parentName + ".Input.Panel");

                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = parentName + ".Input.Panel",
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = textAlign,
                            CharsLimit = maxLength,
                            Command = command,
                            Color = textColor,
                            FontSize = fontSize,
                            IsPassword = isPassword,
                            Autofocus = autofocus,
                            Text = text
                        },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiNeedsKeyboardComponent
                        {
                            Enabled = cursorEnabled
                        }
                    }
                });
            }

            public static void Image(CuiElementContainer container, string parentName, string image, string anchorMin, string anchorMax, string offsetMin = "", string offsetMax = "",
                string color = "1 1 1 1")
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Components =
                    {
                        new CuiRawImageComponent { Color = color, Png = image },
                        new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                    },
                    Parent = parentName
                });
            }

            public class RowCuiPanel
            {
                public CuiPanel CuiPanel;
                public string PanelName;
                public Action? Render;
            }

            public class ScrollContentResult
            {
                public CuiElementContainer Container;
                public float Height;
            }
        }

        private static class GuiHelper
        {
            public static CuiElementContainer Container(string panelName, string backgroundColor, string anchorMin,
                string anchorMax, bool requireCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = backgroundColor },
                            RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                            CursorEnabled = requireCursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
            }

            public static CuiElementContainer ContainerWithOffset(string panelName, string backgroundColor,
                string anchorMin,
                string anchorMax, string offsetMin = "", string offsetMax = "", bool requireCursor = false,
                string parent = "Hud")
            {
                return new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = backgroundColor },
                            RectTransform =
                            {
                                AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin,
                                OffsetMax = offsetMax
                            },
                            CursorEnabled = requireCursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
            }

            public static string ParentPanel(CuiElementContainer container, string parentName, string panelName, string anchorMin, string anchorMax,
                string offsetMin = "", string offsetMax = "", string backgroundColor = "0 0 0 0", string sprite = "", string material = "", bool cursorEnabled = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = backgroundColor, Sprite = sprite, Material = material },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                    CursorEnabled = cursorEnabled,
                }, parentName, panelName);
                return panelName;
            }

            public static string ParentPanelCircle(CuiElementContainer container, string parentName, string panelName,
                string backgroundColor, string anchorMin, string anchorMax, bool cursorEnabled = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = backgroundColor, Sprite = "assets/icons/circle_closed.png" },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    CursorEnabled = cursorEnabled,
                }, parentName, panelName);
                return panelName;
            }

            public static void Image(CuiElementContainer container, string parentName, int itemId, ulong skinId,
                string anchorMin, string anchorMax, string color = "1 1 1 1")
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = parentName,
                    Components =
                    {
                        new CuiImageComponent { ItemId = itemId, SkinId = skinId, Color = color },
                        new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax }
                    }
                });
            }

            public static void ImageUrl(CuiElementContainer container, string parentName, string image,
                string anchorMin, string anchorMax, string color = "1 1 1 1")
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Components =
                    {
                        new CuiRawImageComponent { Color = color, Png = image },
                        new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax }
                    },
                    Parent = parentName
                });
            }

            public static void CloseButton(CuiElementContainer container, string parentName, string closePanelName,
                string buttonText, string color, string anchorMin, string anchorMax, int fontSize = 14,
                string font = "robotocondensed-bold.ttf", string textColor = "1 1 1 1", string command = "")
            {
                container.Add(new CuiButton
                {
                    Button = { Command = command, Close = closePanelName, Color = color },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Text = { Text = buttonText, FontSize = fontSize, Font = font, Align = TextAnchor.MiddleCenter, Color = textColor }
                }, parentName);
            }

            public static void Button(CuiElementContainer container, string parentName, string command,
                string buttonText, string buttonColor, string anchorMin, string anchorMax, int fontSize = 14,
                string textColor = "1 1 1 1", TextAnchor align = TextAnchor.MiddleCenter,
                string font = "robotocondensed-bold.ttf")
            {
                container.Add(new CuiButton
                {
                    Button = { Command = command, Color = buttonColor },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Text = { Text = buttonText, FontSize = fontSize, Font = font, Align = align, Color = textColor }
                }, parentName);
            }

            public static void Button(CuiElementContainer container, string parentName, string command,
                string buttonText, string buttonColor, string anchorMin, string anchorMax, string sprite, string material, int fontSize = 14,
                string textColor = "1 1 1 1", TextAnchor align = TextAnchor.MiddleCenter, string font = "robotocondensed-bold.ttf")
            {
                container.Add(new CuiButton
                {
                    Button = { Command = command, Color = buttonColor, Sprite = sprite, Material = material },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Text = { Text = buttonText, FontSize = fontSize, Font = font, Align = align, Color = textColor }
                }, parentName);
            }

            public static void ButtonCircle(CuiElementContainer container, string parentName, string command,
                string buttonText, string buttonColor, string anchorMin, string anchorMax, int fontSize = 14,
                string textColor = "1 1 1 1", TextAnchor align = TextAnchor.MiddleCenter,
                string font = "robotocondensed-bold.ttf")
            {
                container.Add(new CuiButton
                {
                    Button = { Command = command, Color = buttonColor, Sprite = "assets/icons/circle_closed.png" },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Text = { Text = buttonText, FontSize = fontSize, Font = font, Align = align, Color = textColor }
                }, parentName);
            }

            public static void Text(CuiElementContainer container, string parentName, string text, string anchorMin,
                string anchorMax, int fontSize = 14, TextAnchor align = TextAnchor.LowerLeft,
                string textColor = "1 1 1 1", string font = "robotocondensed-bold.ttf")
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = text, FontSize = fontSize, Font = font, Align = align, Color = textColor },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }, parentName);
            }
        }

        private class Configuration
        {
            [JsonProperty("TimeZone")] public string TimeZone;

            [JsonProperty("ChatPrefix")] public string ChatPrefix;

            [JsonProperty("AllowAdminToBypassCodeLock (Allows admin to bypass Code Lock without using commands). Default False.)")]
            public bool AllowAdminToBypassCodeLock;

            [JsonProperty("Disable ownership checking on DLC/Skins. (⚠ Only allowed on creative/test servers, otherwise you violate Facepunch's TOS! ⚠) Default false")]
            public bool DisableDlcSkinOwnershipCheck = false;

            // [JsonProperty("Disable Code Lock sound effects")]
            // public bool DisableCodeLockEffects;
            //
            // [JsonProperty("Disable effects if in NoClip or Vanish mode")]
            // public bool DisableCodeLockEffectsIfInNoClipOrVanish = true;

            [JsonProperty("Chat Command")] public HashSet<string> ChatCommand;

            [JsonProperty("Skin Blacklist Configuration")]
            public SkinBlacklistConfiguration SkinBlacklistConfiguration;

            [JsonProperty("Proximity System")] public ProximitySystemConfiguration ProximitySystemConfiguration;

            [JsonProperty("Auto Lock Configuration")]
            public AutoLockConfiguration AutoLockConfiguration;

            [JsonProperty("Auto Closing Configuration")]
            public AutoClosingConfiguration AutoClosingConfiguration;

            [JsonProperty("Requires Building Privilege to place Code Locks. (Default: TRUE)")]
            public bool RequiresBuildingPrivilege = true;

            [JsonProperty("Requires Building Privilege to place Code Locks in unowned vehicles. (Default: FALSE)")]
            public bool RequiresBuildingPrivilegeToDeployCodeLockInUnownedVehicles;

            [JsonProperty("Allow deployment of Code Lock in vehicles owned by other players. (Default: FALSE)")]
            public bool AllowDeployCodeLockInOwnedPlayersVehicles;

            [JsonProperty("Allow deployment of Code Lock in unowned vehicles. (Default: TRUE)")]
            public bool AllowDeployCodeLockInUnownedVehicles = true;

            [JsonProperty("Allow pushing vehicles blocked by the Code Lock (Default: TRUE)")]
            public bool AllowPushVehiclesBlockedByCodeLock = true;

            [JsonProperty("Allow pushing vehicles blocked by the Code Lock if present in your Building Privilege. (Default: TRUE)")]
            public bool AllowPushVehiclesBlockedByCodeLockInBuildingPrivilege = true;

            [JsonProperty("Auto claim unowned vehicles when deploying locks. (Default: FALSE)")]
            public bool AutoClaimUnownedVehiclesWhenDeployLock = false;

            [JsonProperty("Auto replace vehicle ownership when deploying locks. (Default: FALSE)")]
            public bool AutoReplaceVehicleOwnershipWhenDeployLock = false;

            [JsonProperty("Set player as owner when placing a Mining Quarry or Pump Jack (also static). (Default: TRUE)")]
            public bool SetPlayerAsOwnerWhenPlacedQuarry = true;


            [JsonProperty("Enable Lock")] public LockConfiguration LockConfiguration;

            public VersionNumber VersionNumber;

            public static Configuration CreateConfig()
            {
                return new Configuration
                {
                    TimeZone = DefaultTimeZone,
                    ChatPrefix = "UltimateLocker",
                    AllowAdminToBypassCodeLock = false,
                    DisableDlcSkinOwnershipCheck = false,
                    // DisableCodeLockEffects = false,
                    // DisableCodeLockEffectsIfInNoClipOrVanish = true,

                    ChatCommand = new HashSet<string> { "ul", "ultimatelocker" },
                    SkinBlacklistConfiguration = new SkinBlacklistConfiguration
                    {
                        LockBlacklist = new HashSet<ulong>(),
                        DoorCloserBlacklist = new HashSet<ulong>()
                    },
                    ProximitySystemConfiguration = new ProximitySystemConfiguration(),
                    AutoLockConfiguration = new AutoLockConfiguration
                    {
                        UseOnlyKeyLock = false,
                        ResyncPlayersDefaultSettings = false,
                        AutoLockCategoryConfiguration = new AutoLockCategoryConfiguration(),
                        AutoLockPlayerConfiguration = new AutoLockPlayerConfiguration(),
                        AutoClosingPlayerConfiguration = new AutoClosingPlayerConfiguration()
                    },
                    AutoClosingConfiguration = new AutoClosingConfiguration(),

                    RequiresBuildingPrivilege = true,
                    RequiresBuildingPrivilegeToDeployCodeLockInUnownedVehicles = false,
                    AllowDeployCodeLockInOwnedPlayersVehicles = false,
                    AllowDeployCodeLockInUnownedVehicles = true,
                    AllowPushVehiclesBlockedByCodeLock = true,
                    AllowPushVehiclesBlockedByCodeLockInBuildingPrivilege = true,
                    AutoClaimUnownedVehiclesWhenDeployLock = false,
                    AutoReplaceVehicleOwnershipWhenDeployLock = false,
                    SetPlayerAsOwnerWhenPlacedQuarry = true,

                    LockConfiguration = new LockConfiguration
                    {
                        Vehicles = new List<LockableEntityConfig>
                        {
                            new()
                            {
                                ItemName = "Minicopter",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/minicopter/minicopter.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.15f, 0.7f, -0.1f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Scrap Transport Helicopter",
                                EnableLock = false,
                                PrefabName =
                                    "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-1.25f, 1.22f, 1.99f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Attack Helicopter",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.6f, 1.08f, 1.01f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Armored / Hot Air Balloon",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(1.45f, 0.9f, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Kayak",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/boats/kayak/kayak.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(0.0f, 0.16f, -1.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Row Boat",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/boats/rowboat/rowboat.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.83f, 0.51f, -0.57f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "RHIB",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/boats/rhib/rhib.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.68f, 2.00f, 0.7f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Tugboat",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/boats/tugboat/tugboat.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(0.065f, 6.8f, 4.12f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 60),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Submarinesolo",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/submarine/submarinesolo.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(0f, 1.85f, 0f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Submarine Duo",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/submarine/submarineduo.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.455f, 1.29f, 0.75f),
                                CodeLockRotation = Quaternion.Euler(0, 180, 10),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Horse",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/horse/ridablehorse.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(0.0f, 1.24f, 0.0f),
                                // CodeLockPosition = new Vector3(-0.22f, 1.05f, 0.1f), //OLD

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Tomaha Snowmobile",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.37f, 0.4f, 0.125f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Snowmobile",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/snowmobiles/snowmobile.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.205f, 0.59f, 0.4f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Sedan",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/sedan_a/sedantest.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-1.09f, 0.79f, 0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "2 Module Car",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "3 Module Car",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/modularcar/3module_car_spawned.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "4 Module Car",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/modularcar/4module_car_spawned.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Car Chassis 2 Module Car",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Car Chassis 3 Module Car",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Car Chassis 4 Module Car",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.Vehicle,
                                CodeLockPosition = new Vector3(-0.9f, 0.35f, -0.5f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Motorbike",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/bikes/motorbike.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.VehicleBike,
                                CodeLockPosition = new Vector3(0.074f, 0.8f, 0.14f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Motorbike With Sidecar",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/bikes/motorbike_sidecar.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.VehicleBike,
                                CodeLockPosition = new Vector3(0.72f, 0.52f, -0.65f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Pedal Bike",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/bikes/pedalbike.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.VehicleBike,
                                CodeLockPosition = new Vector3(0.0f, 0.6f, -0.07f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Pedal Trike",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/bikes/pedaltrike.prefab",

                                LockCategory = LockCategory.Vehicle,
                                EntityType = EntityType.VehicleBike,
                                CodeLockPosition = new Vector3(0.0f, 0.7f, -0.5f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "boatbuildingstation",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boat building platform/boatbuildingstation.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(0.0f, 0.3f, 0.35f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "steeringwheel.boat",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boatbuilding/steeringwheel/steeringwheel.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "sail",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boatbuilding/sail/sail.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(0.006f, 0.8f, -0.05f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.6f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "smallengine",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boatbuilding/smallengine/smallengine.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(-0.16f, 0.99f, -0.11f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "anchor",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boatbuilding/anchor/anchor.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(0.657f, 0.6f, 0.96f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, -36.0f),
                                ScaleSize = 0.5f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "cannon",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boatbuilding/cannon/cannon.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(-0.294f, 0.4f, -0.49f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 90.0f),
                                ScaleSize = 0.5f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "small_ramp",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/boatbuilding/small_ramp/small_ramp.deployed.prefab",

                                LockCategory = LockCategory.NavalDeepSea,
                                EntityType = EntityType.DeployableNavalDeepSea,
                                CodeLockPosition = new Vector3(0.9f, 1.0f, -0.09f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.5f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Mounted Ballista",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/siegeweapons/ballista/ballista.entity.prefab",

                                LockCategory = LockCategory.Medieval,
                                EntityType = EntityType.VehicleMedieval,
                                CodeLockPosition = new Vector3(0.24f, 0.3f, 0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Battering Ram",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab",

                                LockCategory = LockCategory.Medieval,
                                EntityType = EntityType.VehicleMedieval,
                                CodeLockPosition = new Vector3(2.4f, 2.0f, 1.64f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Catapult",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/siegeweapons/catapult/catapult.entity.prefab",

                                LockCategory = LockCategory.Medieval,
                                EntityType = EntityType.VehicleMedieval,
                                CodeLockPosition = new Vector3(1.124f, 2.74f, 0.9f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Siege Tower",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/siegeweapons/siegetower/siegetower.entity.prefab",

                                LockCategory = LockCategory.Medieval,
                                EntityType = EntityType.VehicleMedieval,
                                CodeLockPosition = new Vector3(1.27f, 2.4f, -0.4f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Ballista",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/siegeweapons/ballista/ballistagun.static.entity.prefab",

                                LockCategory = LockCategory.Medieval,
                                EntityType = EntityType.DeployableMedieval,
                                CodeLockPosition = new Vector3(0.0f, 0.6f, 0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            }
                        },
                        Deployables = new List<LockableEntityConfig>
                        {
                            //TODO Sistemare il fatto che possa essere acceso con un Accensore
                            new()
                            {
                                ItemName = "Large Furnace",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/furnace.large/furnace.large.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.65f, 1.25f, -0.65f),
                                CodeLockRotation = Quaternion.Euler(0, 45, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Large Industrial Furnace",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/furnace.large/skins/industrial_large_furnace/industrial.furnace.large.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.72f, 1.50f, -0.70f),
                                CodeLockRotation = Quaternion.Euler(0, 45, 0),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO Sistemare il fatto che possa essere acceso con un Accensore
                            new()
                            {
                                ItemName = "Furnace",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/furnace/furnace.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.02f, 0.3f, 0.5f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO Sistemare il fatto che possa essere acceso con un Accensore
                            new()
                            {
                                ItemName = "Legacy Furnace",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/legacyfurnace/legacy_furnace.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.02f, 1.2f, 0.32f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Refinery",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.01f, 1.25f, -0.6f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Electric Furnace",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/electricfurnace/electricfurnace.deployed.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.02f, 0.2f, 0.32f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Industrial Electric Furnace",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/electricfurnace/skins/industrial_electric_furnace/industrial_electric_furnace.deployed.prefab",

                                LockCategory = LockCategory.Furnace,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.02f, 0.2f, 0.28f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Stone Fireplace",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/fireplace/fireplace.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                CodeLockPosition = new Vector3(-0.02f, 0.2f, 0.46f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "BBQ",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/bbq/bbq.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.3f, 0.75f, 0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Hobo Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/twitch/hobobarrel/hobobarrel.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.75f, 0.34f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Storage Barrel B",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.85f, 0.42f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Storage Barrel C",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.60f, 0.45f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 350),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Abyss Horizontal Storage Tank",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.60f, 0.42f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 340),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Abyss Vertical Storage Tank",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.58f, 0.42f),
                                CodeLockRotation = Quaternion.Euler(0, 92, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Bamboo Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_vertical/bamboo_barrel.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.8f, 0.38f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 346),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Wicker Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.62f, 0.44f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 354),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Krieg Storage Crates",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_horizontal/krieg_storage_horizontal.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.005f, 0.68f, 0.36f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 2),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Krieg Storage Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_vertical/krieg_storage_vertical.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 1.05f, 0.38f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Industrial Storage Horizontal Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/industrial_dlc/industrial_storage_horizontal/industrial_storage_horizontal.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.60f, 0.38f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 346),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Industrial Storage Vertical Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/large wood storage/skins/industrial_dlc/industrial_storage_vertical/industrial_storage_vertical.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.76f, 0.42f),
                                CodeLockRotation = Quaternion.Euler(0, 92, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "RHIB Storage",
                                EnableLock = false,
                                PrefabName = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab",

                                LockCategory = LockCategory.Box,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.38f, 0.30f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Clothing Mannequin",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/mannequin/mannequin_deployed.prefab",

                                LockCategory = LockCategory.Locker,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Metal Shop Front",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/building/wall.frame.shopfront/wall.frame.shopfront.metal.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.0f, 0.8f, -0.6f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Dropbox",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/dropbox/dropbox.deployed.prefab",

                                LockCategory = LockCategory.StorageContainer,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.18f, 0.23f, -0.3f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Mail Box",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/mailbox/mailbox.deployed.prefab",

                                LockCategory = LockCategory.StorageContainer,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.08f, 1.17f, 0.20f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Vending Machine",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab",

                                LockCategory = LockCategory.VendingMachine,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0, 1.0f, -0.22f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Computer Station",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/computerstation/computerstation.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.61f, 0.54f, 0.42f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Twitch Rivals Desk",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/misc/twitch/twitch_rivals_2023_desk/twitchrivals2023_desk.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.74f, 0.56f, 0.5f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Mixing Table",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/mixingtable/mixingtable.deployed.prefab",

                                LockCategory = LockCategory.MixingTable,
                                CodeLockPosition = new Vector3(-0.50f, 0.62f, 0.432f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Composter",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/composter/composter.prefab",

                                LockCategory = LockCategory.Composter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0f, 1.3f, 0.6f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Small Planter Box",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/planters/planter.small.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(1.22f, 0.45f, 0.28f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Large Planter Box",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/planters/planter.large.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(1.22f, 0.45f, 1.21f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Triangle Planter Box",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/planters/planter.triangle.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.38f, 0.45f, -0.9f),
                                CodeLockRotation = Quaternion.Euler(0, 0, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Minecart Planter",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/misc/decor_dlc/minecart planter/minecart.planter.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.74f, 0.65f, 0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Bath Tub Planter",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/misc/decor_dlc/bath tub planter/bathtub.planter.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.68f, 0.60f, 0.436f),
                                CodeLockRotation = Quaternion.Euler(0, 180, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Rail Road Planter",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/misc/decor_dlc/rail road planter/railroadplanter.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(1.22f, 0.45f, 1.26f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Triangle Rail Road Planter",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/decor_dlc/rail road planter/triangle_railroad_planter.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.34f, 0.32f, -0.9f),
                                CodeLockRotation = Quaternion.Euler(0, 0, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Single Plant Pot",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/plant pots/plantpot.single.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.2f, 0.16f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 8),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Beehive",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/beehive/beehive.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.8f, 0.24f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Chicken Coop",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/chickencoop/chickencoop.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.2f, 1.3f, -0.62f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO Sistemare che il cavallo non possa mangiare
                            new()
                            {
                                ItemName = "Hitch & Trough",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0f, 0.4f, 0.3f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Small Water Catcher",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/water catcher/water_catcher_small.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableLiquidContainer,
                                CodeLockPosition = new Vector3(0.0f, 0.8f, 0.36f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Large Water Catcher",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/water catcher/water_catcher_large.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableLiquidContainer,
                                CodeLockPosition = new Vector3(0.0f, 1.5f, 0.84f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Water Barrel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/liquidbarrel/waterbarrel.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableLiquidContainer,
                                CodeLockPosition = new Vector3(0.0f, 1.5f, 0.40f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Powered Water Purifier",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/poweredwaterpurifier/poweredwaterpurifier.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableLiquidContainer,
                                CodeLockPosition = new Vector3(0.0f, 1.5f, -0.04f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Fluid Switch & Pump",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/playerioents/fluidswitch/fluidswitch.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectricSwitch,
                                CodeLockPosition = new Vector3(0.2f, -0.1f, -0.12f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Repair Bench",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/repair bench/repairbench_deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(-0.80f, 1.0f, -0.36f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Research Table",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/research table/researchtable_deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.6f, 0.96f, 0.24f),
                                CodeLockRotation = Quaternion.Euler(0, 270, 50),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Workbench Level 1",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/tier 1 workbench/workbench1.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableWorkbench,
                                CodeLockPosition = new Vector3(0.56f, 0.82f, 0.26f),
                                CodeLockRotation = Quaternion.Euler(0, 270, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Workbench Level 2",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/tier 2 workbench/workbench2.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableWorkbench,
                                CodeLockPosition = new Vector3(-0.28f, 0.82f, 0.26f),
                                CodeLockRotation = Quaternion.Euler(0, 270, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Workbench Level 3",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/tier 3 workbench/workbench3.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableWorkbench,
                                CodeLockPosition = new Vector3(-0.98f, 0.74f, 0.38f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Cooking Workbench",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/cookingworkbench/cookingworkbench.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,

                                CodeLockPosition = new Vector3(0.0f, 0.54f, 0.26f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),
                                // CodeLockPosition = new Vector3(-0.61f, 1.28f, 0.0f),
                                // CodeLockRotation = Quaternion.Euler(0, 270, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Engineering Workbench",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/io research table/io.table.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.5f, 0.8f, 0.24f),
                                CodeLockRotation = Quaternion.Euler(0, 270, 45),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Hopper",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/hopper/hopper.deployed.prefab",

                                LockCategory = LockCategory.Planter,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.48f, 0.16f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Button",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/playerioents/button/button.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectricalButton,
                                CodeLockPosition = new Vector3(0.12f, -0.24f, -0.03f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Switch",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectricSwitch,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, -0.03f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Smart Switch",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/app/smartswitch/smartswitch.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectricSwitch,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, -0.03f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Timer",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/playerioents/timers/timer.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectricSwitch,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, -0.03f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Small Generator",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/generators/fuel generator/small_fuel_generator.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectricGenerator,
                                CodeLockPosition = new Vector3(0.09f, 0.54f, 0.1f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "SAM Site",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab",

                                LockCategory = LockCategory.SamSite,
                                EntityType = EntityType.DeployableTurret,
                                CodeLockPosition = new Vector3(-0.4f, 0.8f, -0.30f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Auto Turret",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab",

                                LockCategory = LockCategory.AutoTurret,
                                EntityType = EntityType.DeployableTurret,
                                CodeLockPosition = new Vector3(-0.29f, 0.38f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Industrial Auto Turret",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/npc/autoturret/skins/industrial_autoturret/industrial_autoturret_deployed.prefab",

                                LockCategory = LockCategory.AutoTurret,
                                EntityType = EntityType.DeployableTurret,
                                CodeLockPosition = new Vector3(-0.26f, 0.38f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Flame Turret",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/npc/flame turret/flameturret.deployed.prefab",

                                LockCategory = LockCategory.Trap,
                                EntityType = EntityType.DeployableTraps,
                                CodeLockPosition = new Vector3(0.0f, 0.16f, -0.10f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Shotgun Trap",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/single shot trap/guntrap.deployed.prefab",

                                LockCategory = LockCategory.Trap,
                                EntityType = EntityType.DeployableTraps,
                                CodeLockPosition = new Vector3(0.0f, 0.1f, -0.3f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Modular Car Lift",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/modular car lift/electrical.modularcarlift.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableModularCarLift,
                                CodeLockPosition = new Vector3(0.0f, 0.9f, -2.0f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 0),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Snow Machine",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/xmas/snow_machine/models/snowmachine.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableSnowFogMachine,
                                CodeLockPosition = new Vector3(0.0f, 0.5f, -0.18f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 70),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Fogger-3000",
                                EnableLock = false,
                                PrefabName = "assets/content/props/fog machine/fogmachine.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableSnowFogMachine,
                                CodeLockPosition = new Vector3(-0.01f, 0.14f, 0.04f),
                                CodeLockRotation = Quaternion.Euler(0, 90, 90),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Elevator",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/elevator/elevator_lift.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElevator,
                                CodeLockPosition = new Vector3(-1.2f, -0.1f, -0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Mining Quarry",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/quarry/mining_quarry.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableMiningQuarry,
                                CodeLockPosition = new Vector3(-2.36f, 4.5f, 1.66f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Mining Quarry Static",
                                EnableLock = false,
                                PrefabName = "assets/bundled/prefabs/static/miningquarry_static.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableMiningQuarryStatic,
                                CodeLockPosition = new Vector3(-2.36f, 4.5f, 1.66f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Pump Jack",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/oil jack/mining.pumpjack.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableMiningPumpJack,
                                CodeLockPosition = new Vector3(3.26f, 4.5f, -1.36f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Pump Jack Static",
                                EnableLock = false,
                                PrefabName = "assets/bundled/prefabs/static/pumpjack-static.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableMiningPumpJackStatic,
                                CodeLockPosition = new Vector3(3.26f, 4.5f, -1.36f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Tall Weapon Rack",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_tall.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-0.61f, 0.96f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Horizontal Weapon Rack",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/weaponracks/weaponrack_horizontal.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-0.60f, 0.58f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Wide Weapon Rack",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_wide.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-1.14f, 0.58f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Weapon Rack Stand",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_stand.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-0.46f, 1.03f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Frontier Bolts Single Item Rack",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_single1.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-0.5f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Frontier Horseshoe Single Item Rack",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_single2.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-0.5f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Frontier Horns Single Item Rack",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/weaponracks/weaponrack_single3.deployed.prefab",

                                LockCategory = LockCategory.WeaponRack,
                                EntityType = EntityType.DeployableWeaponRack,
                                CodeLockPosition = new Vector3(-0.5f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Small Stash",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/small stash/small_stash_deployed.prefab",

                                LockCategory = LockCategory.Stash,
                                EntityType = EntityType.DeployableStash,
                                CodeLockPosition = new Vector3(0.0f, 0.08f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Chippy Arcade Game",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/chippy arcade/chippyarcademachine.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableItems,
                                CodeLockPosition = new Vector3(0.0f, 0.48f, 0.26f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Strobe Light",
                                EnableLock = false,
                                PrefabName = "assets/content/props/strobe light/strobelight.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableMisc,
                                CodeLockPosition = new Vector3(-0.0f, -0.05f, -0.1f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 270.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Laser Light",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/voiceaudio/laserlight/laserlight.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableFun,
                                CodeLockPosition = new Vector3(0.1f, -0.1f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 180.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Sound Light",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/voiceaudio/soundlight/soundlight.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableMisc,
                                CodeLockPosition = new Vector3(0.0f, -0.1f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 180.0f),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO DA SISTEMARE, NON BLOCCA LE MODIFICHE
                            new()
                            {
                                ItemName = "Small Neon Sign",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/xmas/neon_sign/sign.neon.125x125.prefab",

                                LockCategory = LockCategory.NeonSign,
                                EntityType = EntityType.DeployableElectricalNeonSign,
                                CodeLockPosition = new Vector3(-0.34f, -0.06f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO DA SISTEMARE, NON BLOCCA LE MODIFICHE
                            new()
                            {
                                ItemName = "Medium Neon Sign",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/xmas/neon_sign/sign.neon.125x215.prefab",

                                LockCategory = LockCategory.NeonSign,
                                EntityType = EntityType.DeployableElectricalNeonSign,
                                CodeLockPosition = new Vector3(-0.34f, -0.06f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO DA SISTEMARE, NON BLOCCA LE MODIFICHE
                            new()
                            {
                                ItemName = "Large Neon Sign",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/xmas/neon_sign/sign.neon.xl.prefab",

                                LockCategory = LockCategory.NeonSign,
                                EntityType = EntityType.DeployableElectricalNeonSign,
                                CodeLockPosition = new Vector3(-0.34f, -0.06f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO DA SISTEMARE, NON BLOCCA LE MODIFICHE
                            new()
                            {
                                ItemName = "Medium Animated Neon Sign",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/xmas/neon_sign/sign.neon.125x215.animated.prefab",

                                LockCategory = LockCategory.NeonSign,
                                EntityType = EntityType.DeployableElectricalNeonSign,
                                CodeLockPosition = new Vector3(-0.34f, -0.06f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            //TODO DA SISTEMARE, NON BLOCCA LE MODIFICHE
                            new()
                            {
                                ItemName = "Large Animated Neon Sign",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/xmas/neon_sign/sign.neon.xl.animated.prefab",

                                LockCategory = LockCategory.NeonSign,
                                EntityType = EntityType.DeployableElectricalNeonSign,
                                CodeLockPosition = new Vector3(-0.34f, -0.06f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "sign.artistcanvas.xs",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/artistscanvases/sign.artistcanvas.xs.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.338f, 0.042f, 0.046f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "sign.artistcanvas.m",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/artistscanvases/sign.artistcanvas.m.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.715f, 0.042f, 0.042f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "sign.artistcanvas.l",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/artistscanvases/sign.artistcanvas.l.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.465f, 0.042f, 0.042f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "sign.artistcanvas.xl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/artistscanvases/sign.artistcanvas.xl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(1.226f, 0.042f, 0.042f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "sign.artistcanvas.xxl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/artistscanvases/sign.artistcanvas.xxl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(2.46f, 0.042f, 0.042f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "lightup.large",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/light-up frame/lightupframe.large.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.648f, -0.435f, 0.056f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "lightup.xl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/light-up frame/lightupframe.xl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(1.214f, -1.208f, 0.059f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "lightup.xxl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/light-up frame/lightupframe.xxl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(2.464f, -1.208f, 0.059f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "lightupframe.small",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/light-up frame/lightupframe.small.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.245f, -0.308f, 0.056f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "lightupframe.standing",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/light-up frame/lightupframe.standing.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.246f, -0.93f, 0.056f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "goldframe.small",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/ornate frame/goldframe.small.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.34f, -0.36f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "goldframe.standing",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/ornate frame/goldframe.standing.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.34f, -0.9818f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "goldframe.large",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/ornate frame/goldframe.large.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.742f, -0.4865f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "goldframe.xl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/ornate frame/goldframe.xl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(1.308f, -1.258f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "goldframe.xxl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/ornate frame/goldframe.xxl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(2.5565f, -1.258f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "scrapframe.small",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/scrap frame/scrapframe.small.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.252f, -0.3154f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "scrapframe.large",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/scrap frame/scrapframe.large.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.652f, -0.449f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "scrapframe.xl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/scrap frame/scrapframe.xl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(1.24f, -1.24f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "scrapframe.xxl",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/scrap frame/scrapframe.xxl.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(2.481f, -1.24f, 0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "window.paintable",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/artist_dlc/paintable window/window.paintable.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.03f, 0.194f, -1.062f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "easel",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/easel_deployable/easel.deployed.prefab",

                                LockCategory = LockCategory.SignDrawing,
                                EntityType = EntityType.DeployableSignDrawing,
                                CodeLockPosition = new Vector3(0.08f, 0.43f, -0.13f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, -16.0f),
                                ScaleSize = 0.25f,

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Search Light",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/search light/searchlight.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectrical,
                                CodeLockPosition = new Vector3(0.0f, 0.1f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "CCTV Camera",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/cctvcamera/cctv_deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableResources,
                                CodeLockPosition = new Vector3(0.0f, 0.16f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "PTZ CCTV Camera",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/ptz security camera/ptz_cctv_deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectrical,
                                CodeLockPosition = new Vector3(0.08f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 180.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "RF Broadcaster",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/gates/rfbroadcaster/rfbroadcaster.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectrical,
                                CodeLockPosition = new Vector3(0.05f, 0.25f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "RF Receiver",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/gates/rfreceiver/rfreceiver.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableElectrical,
                                CodeLockPosition = new Vector3(0.0f, -0.06f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Industrial Conveyor",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/industrialconveyor/industrialconveyor.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableIndustrial,
                                CodeLockPosition = new Vector3(0.0f, -0.16f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 180.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Industrial Crafter",
                                EnableLock = false,
                                PrefabName =
                                    "assets/prefabs/deployable/playerioents/industrialcrafter/industrialcrafter.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableIndustrial,
                                CodeLockPosition = new Vector3(-0.1f, 0.16f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Wheelbarrow Piano",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/instruments/piano/piano.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableFun,
                                CodeLockPosition = new Vector3(0.582f, 0.82f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Junkyard Drum Kit",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/instruments/drumkit/drumkit.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableFun,
                                CodeLockPosition = new Vector3(0.5f, 0.46f, 0.26f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Boom Box",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableFun,
                                CodeLockPosition = new Vector3(0.0f, 0.66f, -0.01f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 270.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Chinese Lantern",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, -0.4f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Chinese Lantern White",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern_white.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, -0.4f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Tuna Can Lamp",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/tuna can wall lamp/tunalight.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Lantern",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Camp Fire",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/campfire/campfire.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Cursed Cauldron",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/halloween/cursed_cauldron/cursedcauldron.deployed.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Skull Fire Pit",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/misc/halloween/skull_fire_pit/skull_fire_pit.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.0f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Jack O Lantern Angry",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/jack o lantern/jackolantern.angry.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.2f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Jack O Lantern Happy",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/deployable/jack o lantern/jackolantern.happy.prefab",

                                LockCategory = LockCategory.OtherCustomEntities,
                                EntityType = EntityType.DeployableStorage,
                                CodeLockPosition = new Vector3(0.0f, 0.2f, 0.0f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 270.0f, 90.0f),

                                RequiredPermission = new[] { "" }
                            },
                            new()
                            {
                                ItemName = "Wood Shutters",
                                EnableLock = false,
                                PrefabName = "assets/prefabs/building/wall.window.shutter/shutter.wood.a.prefab",

                                LockCategory = LockCategory.Window,
                                EntityType = EntityType.DeployableConstruction,
                                CodeLockPosition = new Vector3(0.18f, 0.26f, 0.96f),
                                CodeLockRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f),

                                RequiredPermission = new[] { "" }
                            }
                        }
                    },
                    VersionNumber = OxideMod.Version
                };
            }
        }

        private class SkinBlacklistConfiguration
        {
            [JsonProperty("Lock: Block lock for entity with this skin")]
            public HashSet<ulong> LockBlacklist = new();

            [JsonProperty("Door Closer: Block door closer for entity with this skin")]
            public HashSet<ulong> DoorCloserBlacklist = new();
        }

        private class ProximitySystemConfiguration
        {
            [JsonProperty("Show UI when player is near a door with Door Closer. Default false")]
            public bool ShowUIWhenPlayerIsNearDoorWithDoorCloser = false;

            [JsonProperty("Show UI when pressing key. Default true")]
            public bool ShowUIWhenPressingKey = true;

            [JsonProperty("Button to show UI: 1 (MMB - middle mouse button) - 2 (SHIFT + MMB) - 3 (CTRL + MMB). Default 2")]
            public int ButtonToShowUI = 2;
        }

        private class LockConfiguration
        {
            [JsonProperty("Vehicles")] public List<LockableEntityConfig> Vehicles;
            [JsonProperty("Deployables")] public List<LockableEntityConfig> Deployables;
        }

        private class LockableEntityConfig
        {
            [JsonProperty("ItemName")] public string ItemName;
            [JsonProperty("EnableLock")] public bool EnableLock;
            [JsonProperty("PrefabName")] public string PrefabName;

            [JsonIgnore] public LockCategory LockCategory;

            //TODO DA RIMUOVERE
            [JsonIgnore] public EntityType EntityType;
            [JsonIgnore] public Vector3 CodeLockPosition;
            [JsonIgnore] public Quaternion CodeLockRotation;
            [JsonIgnore] public float ScaleSize = DefaultScaleSize;

            [JsonProperty("RequiredPermission")] public string[] RequiredPermission = { "" };
        }

        private class AutoLockConfiguration
        {
            [JsonProperty("Chat Command")] public HashSet<string> ChatCommand = new() { "al", "autolock", "codelock" };

            [JsonProperty("Toggle Auto Lock - Chat Command")]
            public string ToggleAutoLockChatCommand = "ual";

            [JsonProperty("Add Lock manually - Chat Command")]
            public string AddLockerChatCommand = "locker";

            [JsonProperty("Use only Key Lock. [true] to allow only Key Lock. [false] to allow both Key Lock and Code Lock. (Default: FALSE)")]
            public bool UseOnlyKeyLock = false;

            [JsonProperty(
                "RESYNC DEFAULT SETTINGS for all players. If set to [true], resets all player settings to the default settings configured. Once the settings are reset, the parameter will automatically be reset to false. (Default: FALSE)")]
            public bool ResyncPlayersDefaultSettings = false;

            [JsonProperty(
                "FORCE SHARE LOCKS WITH CLAN/TEAM MEMBERS. [true] to force all players to share locks with clan/team members. [false] to force all players to NOT share locks with clan/team members. [null] to use the player's default settings by UI. (Default: null)")]
            public bool? ForceShareLocksWithClanTeam = null;

            [JsonProperty("Auto Lock Category Configuration")]
            public AutoLockCategoryConfiguration AutoLockCategoryConfiguration;

            [JsonProperty("Player default settings")] //TODO da rinominare in: "Player Auto Lock default settings"
            public AutoLockPlayerConfiguration AutoLockPlayerConfiguration;

            [JsonProperty("Player Auto Closing default settings")]
            public AutoClosingPlayerConfiguration AutoClosingPlayerConfiguration;
        }

        /* Queste impostazioni determinano le categorie di auto lock abilitate, indipendentemente dalle entità abilitate nella configurazione o alle impostazioni del giocatore */
        // TODO rimuovere da tutti i JsonProperty la parola "enabled" finale
        private class AutoLockCategoryConfiguration
        {
            [JsonProperty("Is automatic lock enabled on Doors enabled.")]
            public bool IsDoorsLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Windows enabled.")]
            public bool IsWindowsLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Boxes enabled.")]
            public bool IsBoxesLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Storage Container enabled.")]
            public bool IsStorageContainerLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Lockers enabled.")]
            public bool IsLockersLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on TC (Cupboards) enabled.")]
            public bool IsCupboardsLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Vehicles enabled.")]
            public bool IsVehicleLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Naval Deep Sea.")]
            public bool IsNavalDeepSeaLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Medieval entity enabled.")]
            public bool IsMedievalEntityLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Furnace enabled.")]
            public bool IsFurnaceLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Vending Machine enabled.")]
            public bool IsVendingMachineLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Composter enabled.")]
            public bool IsComposterLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Mixing Table enabled.")]
            public bool IsMixingTableLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Planter enabled.")]
            public bool IsPlanterLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Auto Turret enabled.")]
            public bool IsAutoTurretLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on SAM Site enabled.")]
            public bool IsSamSiteLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Traps enabled.")]
            public bool IsTrapsLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Weapon Rack enabled.")]
            public bool IsWeaponRackLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Stash enabled.")]
            public bool IsStashLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Neon Sign enabled.")]
            public bool IsNeonSignLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Sign / Drawing.")]
            public bool IsSignDrawingLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Other Custom Entities enabled.")]
            public bool IsOtherCustomEntitiesLockEnabled = true;

            [JsonProperty("Is automatic lock enabled on Other Lockable Entities enabled.")]
            public bool IsOtherLockableEntitiesLockEnabled = true;
        }

        /* Queste sono le impostazioni dell' l'auto lock del giocatore quando usa il plugin per le prima volta */
        private class AutoLockPlayerConfiguration
        {
            [JsonProperty("Allow automatic lock placement by default. (Default: FALSE)")]
            public bool EnableAutoLockOnPlacementByDefault = false;

            [JsonProperty("Allow automatic placement of Key Locks if there are no Code Locks in the inventory. (Default: TRUE)")]
            public bool EnableAlsoUseKeyLockByDefault = true;

            [JsonProperty("Allow Guest Code by default. (Default: FALSE)")]
            public bool EnableGuestCodeByDefault;

            [JsonProperty("Allow automatic Share locks with clan/team members by default. (Default: TRUE)")]
            public bool EnableAutoShareLocksWithClanTeamByDefault = true;

            [JsonProperty("Enable streamer mode by default. (Default: FALSE)")]
            public bool EnableStreamerModeByDefault;

            [JsonProperty("Allow automatic lock on Doors by default. (Default: TRUE)")]
            public bool EnableDoorsLockByDefault = true;

            [JsonProperty("Allow automatic lock on Windows by default. (Default: TRUE)")]
            public bool EnableWindowsLockByDefault = true;

            [JsonProperty("Allow automatic lock on Boxes by default. (Default: FALSE)")]
            public bool EnableBoxesLockByDefault;

            [JsonProperty("Allow automatic lock on Storage Container by default. (Default: FALSE)")]
            public bool EnableStorageContainerLockByDefault;

            [JsonProperty("Allow automatic lock on Lockers by default. (Default: TRUE)")]
            public bool EnableLockersLockByDefault = true;

            [JsonProperty("Allow automatic lock on TC (Cupboards) by default. (Default: TRUE)")]
            public bool EnableCupboardsLockByDefault = true;

            [JsonProperty("Allow automatic lock on Vehicle by default. (Default: TRUE)")]
            public bool EnableVehicleLockByDefault = true;

            [JsonProperty("Allow automatic lock on Naval Deep Sea by default. (Default: TRUE)")]
            public bool EnableNavalDeepSeaLockByDefault = true;

            [JsonProperty("Allow automatic lock closing when dismount of the vehicle by default. (Default: TRUE)")]
            public bool EnableVehicleLockAutoClosingOnDismountByDefault = true;

            [JsonProperty("Allow automatic lock on Medieval entity by default. (Default: FALSE)")]
            public bool EnableMedievalEntityLockByDefault;

            [JsonProperty("Allow automatic lock on Furnace by default. (Default: FALSE)")]
            public bool EnableFurnaceLockByDefault;

            [JsonProperty("Allow automatic lock on Vending Machine by default. (Default: FALSE)")]
            public bool EnableVendingMachineLockByDefault;

            [JsonProperty("Allow automatic lock on Composter by default. (Default: FALSE)")]
            public bool EnableComposterLockByDefault;

            [JsonProperty("Allow automatic lock on Mixing Table by default. (Default: FALSE)")]
            public bool EnableMixingTableLockByDefault;

            [JsonProperty("Allow automatic lock on Planter by default. (Default: FALSE)")]
            public bool EnablePlanterLockByDefault;

            [JsonProperty("Allow automatic lock on Auto Turret by default. (Default: FALSE)")]
            public bool EnableAutoTurretLockByDefault;

            [JsonProperty("Allow automatic lock on SAM Site by default. (Default: FALSE)")]
            public bool EnableSamSiteLockByDefault;

            [JsonProperty("Allow automatic lock on Traps by default. (Default: FALSE)")]
            public bool EnableTrapsLockByDefault;

            [JsonProperty("Allow automatic lock on Weapon Rack by default. (Default: TRUE)")]
            public bool EnableWeaponRackLockByDefault = true;

            [JsonProperty("Allow automatic lock on Stash by default. (Default: FALSE)")]
            public bool EnableStashLockByDefault;

            [JsonProperty("Allow automatic lock on Neon Sign by default. (Default: FALSE)")]
            public bool EnableNeonSignLockByDefault;

            [JsonProperty("Allow automatic lock on Sign / Drawing by default. (Default: FALSE)")]
            public bool EnableSignDrawingLockByDefault;

            [JsonProperty("Allow automatic lock on Other Lockable Entities by default. (Default: FALSE)")]
            public bool EnableOtherLockableEntitiesLockByDefault;

            [JsonProperty("Allow automatic lock on Other Custom Entities by default. (Default: FALSE)")]
            public bool EnableOtherCustomEntitiesLockByDefault;
        }

        /* Queste sono le impostazioni dell' l'auto closing del giocatore quando usa il plugin per le prima volta */
        private class AutoClosingPlayerConfiguration
        {
            [JsonProperty("Allow automatic closing by default. (Default: FALSE)")]
            public bool EnableAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Door by default. (Default: FALSE)")]
            public bool EnableDoorAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Double Door by default. (Default: FALSE)")]
            public bool EnableDoubleDoorAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Window by default. (Default: FALSE)")]
            public bool EnableWindowAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Garage by default. (Default: FALSE)")]
            public bool EnableGarageAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Ladder Hatch by default. (Default: FALSE)")]
            public bool EnableLadderHatchAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of External Gate by default. (Default: FALSE)")]
            public bool EnableExternalGateAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Fence Gate by default. (Default: FALSE)")]
            public bool EnableFenceGateAutoClosingByDefault = false;

            [JsonProperty("Allow automatic closing of Legacy Wood Shelter Door by default. (Default: FALSE)")]
            public bool EnableLegacyWoodShelterDoorAutoClosingByDefault = false;
        }

        /* Queste impostazioni determinano le categorie di auto closing abilitate, indipendentemente dalle entità abilitate nella configurazione o alle impostazioni del giocatore */
        private class AutoClosingConfiguration
        {
            [JsonProperty("Chat Command")] public HashSet<string> ChatCommand = new() { "ac", "autoclose", "autocloser" };

            [JsonProperty("Toggle Auto Closer - Chat Command")]
            public string ToggleAutoCloserChatCommand = "uad";

            [JsonProperty("Add Door Closer manually - Chat Command")]
            public string AddCloserChatCommand = "closer";

            [JsonProperty("Remove Door Closer manually - Chat Command")]
            public string RemoveCloserChatCommand = "uncloser";

            [JsonProperty("Enable automatic closing of Door. (Default: FALSE)")]
            public bool EnableDoorAutoClosing = false;

            [JsonProperty("Enable automatic closing of Double Door. (Default: FALSE)")]
            public bool EnableDoubleDoorAutoClosing = false;

            [JsonProperty("Enable automatic closing of Window. (Default: FALSE)")]
            public bool EnableWindowAutoClosing = false;

            [JsonProperty("Enable automatic closing of Garage. (Default: FALSE)")]
            public bool EnableGarageAutoClosing = false;

            [JsonProperty("Enable automatic closing of Ladder Hatch. (Default: FALSE)")]
            public bool EnableLadderHatchAutoClosing = false;

            [JsonProperty("Enable automatic closing of External Gate. (Default: FALSE)")]
            public bool EnableExternalGateAutoClosing = false;

            [JsonProperty("Enable automatic closing of Fence Gate. (Default: FALSE)")]
            public bool EnableFenceGateAutoClosing = false;

            [JsonProperty("Enable automatic closing of Legacy Wood Shelter Door. (Default: FALSE)")]
            public bool EnableLegacyWoodShelterDoorAutoClosing = false;


            [JsonProperty("Minimum Closing Delay Time. (Default: 10 seconds)")]
            public int MinimumClosingDelayTime = 10;

            [JsonProperty("Maximum Closing Delay Time. (Default: 60 seconds)")]
            public int MaximumClosingDelayTime = 60;

            [JsonProperty("Default Closing Delay Time. (Default: 30 seconds)")]
            public int DefaultClosingDelayTime = 30;
        }

        private class Data
        {
            [JsonProperty("Auto Lock Player Settings")]
            //SteamID : AutoLockPlayerSettingsData
            public readonly Dictionary<ulong, AutoLockPlayerSettingsData> AutoLockPlayerSettingsData = new();

            [JsonProperty("Auto Closing Player Settings")]
            //SteamID : AutoClosingPlayerSettingsData
            public readonly Dictionary<ulong, AutoClosingPlayerSettingsData> AutoClosingPlayerSettingsData = new();

            [JsonProperty("CodeLockItems")] //TODO rinominare in LockEntityData
            //SteamID : List<LockEntityData>
            public readonly Dictionary<ulong, List<LockEntityData>> LockEntityData = new();

            [JsonProperty("DoorCloserCustomTimeItemsData")] //TODO rinominare in DoorCloserEntityData
            //SteamID : List<DoorCloserEntityData>
            public readonly Dictionary<ulong, List<DoorCloserEntityData>> DoorCloserEntityData = new();
        }

        /* Queste impostazioni determinano le categorie di auto lock abilitate in base alle opzioni abilitate dal giocatore */
        private class AutoLockPlayerSettingsData
        {
            [JsonProperty("SteamID")] public ulong SteamID;

            [JsonProperty("Code Lock")] public string CodeLock;

            [JsonProperty("Guest Code Lock")] public string GuestCodeLock;

            [JsonProperty("Is automatic lock placement enabled.")]
            public bool AutoLockOnPlacementEnabled;

            [JsonProperty("Use Key Locks if there are no Code Locks in inventory.")]
            public bool AlsoUseKeyLockEnabled;

            [JsonProperty("Is automatic Guest Code enabled.")]
            public bool GuestCodeEnabled;

            [JsonProperty("Is automatic Share locks with clan/team members enabled.")]
            public bool ShareLocksWithClanTeamEnabled = true;

            [JsonProperty("Is streamer mode enabled.")]
            public bool StreamerModeEnabled;

            [JsonProperty("Code Lock skin: Flight Control Codelock enabled")]
            public bool CodelockSkinFlightControlCodelockEnabled = false;

            [JsonProperty("Is automatic lock on Doors enabled.")]
            public bool DoorsLockEnabled;

            [JsonProperty("Is automatic lock on Windows enabled.")]
            public bool WindowsLockEnabled;

            [JsonProperty("Is automatic lock on Boxes enabled.")]
            public bool BoxesLockEnabled;

            [JsonProperty("Is automatic lock on Storage Container enabled.")]
            public bool StorageContainerLockEnabled;

            [JsonProperty("Is automatic lock on Lockers enabled.")]
            public bool LockersLockEnabled;

            [JsonProperty("Is automatic lock on TC (Cupboards) enabled.")]
            public bool CupboardsLockEnabled;

            [JsonProperty("Is automatic lock on Vehicles enabled.")]
            public bool VehicleLockEnabled;

            [JsonProperty("Is automatic lock on Naval Deep Sea enabled.")]
            public bool NavalDeepSeaLockEnabled;

            [JsonProperty("Is automatic lock closing when dismount of the vehicle enabled.")]
            public bool VehicleLockAutoClosingOnDismountEnabled;

            [JsonProperty("Is automatic lock on Medieval entity enabled.")]
            public bool MedievalEntityLockEnabled;

            [JsonProperty("Is automatic lock on Furnace enabled.")]
            public bool FurnaceLockEnabled;

            [JsonProperty("Is automatic lock on Vending Machine enabled.")]
            public bool VendingMachineLockEnabled;

            [JsonProperty("Is automatic lock on Composter enabled.")]
            public bool ComposterLockEnabled;

            [JsonProperty("Is automatic lock on Mixing Table enabled.")]
            public bool MixingTableLockEnabled;

            [JsonProperty("Is automatic lock on Planter enabled.")]
            public bool PlanterLockEnabled;

            [JsonProperty("Is automatic lock on Auto Turret enabled.")]
            public bool AutoTurretLockEnabled;

            [JsonProperty("Is automatic lock on SAM Site enabled.")]
            public bool SamSiteLockEnabled;

            [JsonProperty("Is automatic lock on Traps enabled.")]
            public bool TrapsLockEnabled;

            [JsonProperty("Is automatic lock on Weapon Rack enabled.")]
            public bool WeaponRackLockEnabled;

            [JsonProperty("Is automatic lock on Stash enabled.")]
            public bool StashLockEnabled;

            [JsonProperty("Is automatic lock on Neon Sign enabled.")]
            public bool NeonSignLockEnabled;

            [JsonProperty("Is automatic lock on Sign / Drawing enabled.")]
            public bool SignDrawingLockEnabled;

            [JsonProperty("Is automatic lock on Other Lockable Entities enabled.")]
            public bool OtherLockableEntitiesLockEnabled;

            [JsonProperty("Is automatic lock on Other Custom Entities enabled.")]
            public bool OtherCustomEntitiesLockEnabled;
        }

        /* Queste impostazioni determinano le categorie di auto closing abilitate in base alle opzioni abilitate dal giocatore */
        private class AutoClosingPlayerSettingsData
        {
            [JsonProperty("SteamID")] public ulong SteamID;

            [JsonProperty("Is automatic closing enabled.")]
            public bool AutoClosingEnabled;

            [JsonProperty("Is the Proximity System UI enabled.")]
            public bool ProximitySystemUIEnabled;

            [JsonProperty("Is automatic closing of Door enabled.")]
            public bool DoorAutoClosingEnabled;

            [JsonProperty("Is automatic closing of Double Door enabled.")]
            public bool DoubleDoorAutoClosingEnabled;

            [JsonProperty("Is automatic closing of Windows enabled.")]
            public bool WindowAutoClosingEnabled;

            [JsonProperty("Is automatic closing of Garage enabled.")]
            public bool GarageAutoClosingEnabled;

            [JsonProperty("Is automatic closing of Ladder Hatch enabled.")]
            public bool LadderHatchAutoClosingEnabled;

            [JsonProperty("Is automatic closing of External Gate enabled.")]
            public bool ExternalGateAutoClosingEnabled;

            [JsonProperty("Is automatic closing of Fence Gate enabled.")]
            public bool FenceGateAutoClosingEnabled;

            [JsonProperty("Is automatic closing of Legacy Wood Shelter Door enabled.")]
            public bool LegacyWoodShelterDoorAutoClosingEnabled;


            [JsonProperty("Door Closing Delay.")] public int DoorClosingDelay;

            [JsonProperty("Double Door Closing Delay.")]
            public int DoubleDoorClosingDelay;

            [JsonProperty("Window Closing Delay.")]
            public int WindowClosingDelay;

            [JsonProperty("Garage Closing Delay.")]
            public int GarageClosingDelay;

            [JsonProperty("Ladder Hatch Closing Delay.")]
            public int LadderHatchClosingDelay;

            [JsonProperty("External Gate Closing Delay.")]
            public int ExternalGateClosingDelay;

            [JsonProperty("Fence Gate Closing Delay.")]
            public int FenceGateClosingDelay;

            [JsonProperty("Legacy Wood Shelter Door Closing Delay.")]
            public int LegacyWoodShelterDoorClosingDelay;
        }

        private class LockEntityData
        {
            [JsonProperty("owner_id")] public ulong OwnerId;
            [JsonProperty("player_name")] public string PlayerName;
            [JsonProperty("network_Id")] public ulong NetId;
            [JsonProperty("shortPrefabName")] public string ShortPrefabName;

            [JsonProperty("ParentEntity_Network_Id")]
            public ulong? ParentEntityNetId;

            [JsonProperty("ParentEntity")] public string ParentEntity;
            [JsonProperty("Lock Scale Size")] public float ScaleSize = DefaultScaleSize;
            [JsonProperty("deploy_coordinate")] public string DeployCoordinate;
            [JsonProperty("teleport_coordinate")] public string TeleportCoordinate;
            [JsonProperty("deploy_date")] public DateTime DeployDate;
            [JsonProperty("deploy_timestamp")] public ulong DeployTimestamp;
        }

        private class DoorCloserEntityData
        {
            [JsonProperty("owner_id")] public ulong OwnerId;
            [JsonProperty("entity_network_Id")] public ulong EntityNetId;
            [JsonProperty("entity_prefab_id")] public uint EntityPrefabID;
            [JsonProperty("delay_time")] public int DelayTime;

            [JsonProperty("is_auto_closing_disabled")]
            public bool IsAutoClosingDisabled = false;

            [JsonProperty("deploy_coordinate")] public string DeployCoordinate;
            [JsonProperty("teleport_coordinate")] public string TeleportCoordinate;
            [JsonProperty("deploy_date")] public DateTime DeployDate;
            [JsonProperty("deploy_timestamp")] public ulong DeployTimestamp;
        }

        private class LockDefinition
        {
            public ModularCarCodeLock? ModularCarCodeLock;
            public PlayerBoatLock? PlayerBoatLock;
            public BaseLock? BaseLock;
            public BaseEntity? BaseEntity;

            public bool IsKeyLock()
            {
                return BaseLock is KeyLock;
            }

            public bool IsCodeLock()
            {
                return BaseLock is CodeLock;
            }

            public bool IsModularCar()
            {
                return ModularCarCodeLock != null;
            }

            public bool IsPlayerBoatLock()
            {
                return PlayerBoatLock != null;
            }

            public BasePlayer? GetOwnerPlayer()
            {
                var userID = GetOwnerID();
                if (!userID.HasValue || !userID.Value.IsSteamId()) return null;

                return BasePlayer.FindAwakeOrSleeping(userID.ToString());
            }

            public ulong? GetOwnerID()
            {
                var baseEntity = GetCodeLockEntity();
                if (baseEntity == null || !baseEntity.OwnerID.IsSteamId()) return null;

                return baseEntity.OwnerID;
            }

            public BaseEntity? GetCodeLockEntity()
            {
                if (BaseLock != null)
                    return BaseLock;

                return BaseEntity switch
                {
                    ModularCar modularCar => modularCar,
                    SteeringWheel steeringWheel => steeringWheel,
                    _ => null
                };
            }

            public string? GetCodeLockCode()
            {
                if (IsModularCar() && ModularCarCodeLock is { HasALock: true })
                {
                    return ModularCarCodeLock.Code;
                }

                if (IsPlayerBoatLock() && PlayerBoatLock is { HasALock: true })
                {
                    return PlayerBoatLock.Code;
                }

                if (BaseLock != null && BaseLock is CodeLock { hasCode: true } codeLock)
                {
                    return codeLock.code;
                }

                return null;
            }

            public string? GetCodeLockGuestCode()
            {
                if (BaseLock != null && BaseLock is CodeLock { hasGuestCode: true } codeLock)
                {
                    return codeLock.guestCode;
                }

                return null;
            }

            public bool IsLocked()
            {
                if (BaseLock != null)
                    return BaseLock.IsLocked();

                return BaseEntity switch
                {
                    ModularCar modularCar => modularCar.HasFlag(ModularCarCodeLock.FLAG_CENTRAL_LOCKING),
                    SteeringWheel steeringWheel => steeringWheel.HasFlag(BaseEntity.Flags.Locked),
                    _ => false
                };
            }

            public void Lock()
            {
                LockUnlock(true);
            }

            public void Unlock()
            {
                LockUnlock(false);
            }

            private void LockUnlock(bool setLocked)
            {
                if (IsModularCar() && BaseEntity is ModularCar modularCar)
                {
                    using (var flags = modularCar.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(ModularCarCodeLock.FLAG_CENTRAL_LOCKING, setLocked);

                    return;
                }

                if (IsPlayerBoatLock() && BaseEntity is SteeringWheel steeringWheel)
                {
                    using (var flags = steeringWheel.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, setLocked);

                    return;
                }

                if (BaseLock != null)
                {
                    using (var flags = BaseLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        flags.Set(BaseEntity.Flags.Locked, setLocked);

                    return;
                }
            }

            public void RemoveLock(BasePlayer? player)
            {
                if (player == null) return;
                if (IsModularCar() && ModularCarCodeLock is { HasALock: true })
                {
                    self?.EntityRemoved(null, ModularCarCodeLock.owner, player);
                    ModularCarCodeLock.RemoveLock();
                    return;
                }

                if (IsPlayerBoatLock() && PlayerBoatLock is { HasALock: true })
                {
                    PlayerBoatLock.RemoveLock();
                    Unlock();
                    return;
                }

                if (BaseLock != null)
                {
                    self?.EntityRemoved(BaseLock, null, player);
                    BaseLock.Kill();
                    return;
                }
            }

            public List<ulong> GetCodeLockWhitelistPlayers()
            {
                if (BaseLock == null) return Array.Empty<ulong>().ToList();

                if (BaseLock is KeyLock)
                    return Array.Empty<ulong>().ToList();

                if (BaseLock != null && BaseLock is CodeLock codeLock)
                    return codeLock.whitelistPlayers;

                if (BaseEntity != null && BaseEntity is ModularCar modularCar)
                    return modularCar.CarLock.WhitelistPlayers.ToList();

                return Array.Empty<ulong>().ToList();
            }
        }

        private class ClanInfo
        {
            public List<string> ClanMemberUserIdList = new();
        }

        private class PlayerInfo
        {
            public ulong Steamid;
            public string DisplayName;
            public Transform? Transform;
            public bool IsBasePlayer;
            public object? PlayerObject;
        }

        #endregion

        #region TODO

        // 1) I cavalli non possono mangiare
        // 2) Sistemare il fatto che possano essere accese con un Accensore: Fornaci, raffinerie, ecc...
        // 3) Disabilitare craftintg se ci sono delle workbench e sono tutte bloccate con il lucchetto
        // 4) Aggiungere supporto per il drone
        // 5) Signage e NeonSign: ogni tanto non blocca la modifica
        // 6) RF: bloccare uso delle frequenze se bloccato con code lock
        // 7) Bloccare effetti sonori se si è in modalità Vanish o noclip

        #endregion

        #region Localization

        private string Lang(string key, string playerID = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, playerID), args);
        }

        //TODO rimuovere le key non più valide per il door closer
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "LOCKED",
                ["LockerStatus_Unlocked"] = "UNLOCKED",
                ["LockerStatus_Removed"] = "REMOVED",
                ["LockerStatus_Updated"] = "UPDATED",
                ["UiEnabled"] = "ENABLED",
                ["UiDisabled"] = "DISABLED",
                ["UiUpdate"] = "UPDATE",
                ["UiRemove"] = "REMOVE",
                ["UiUpdateAll"] = "UPDATE ALL",
                ["UiRemoveAll"] = "REMOVE ALL",
                ["UiButton_Yes"] = "YES",
                ["UiButton_No"] = "NO",

                ["NoCodeLockAuth"] = "It is blocked...",
                ["NoPermissions"] = "<color=red>You don't have permission to use this command.</color>",
                ["NoBypassPermissions"] = "<color=red>You don't have permission to bypass code.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID is not the owner of the entity.</color>",
                ["NoEntityOwner"] = "<color=red>You do not own this entity.</color>",
                ["PlayerNotFound"] = "<color=red>The player could not be found.</color>",
                ["InvalidSteamID"] = "<color=red>Invalid SteamID.</color>",
                ["CodeLockNotFound"] = "<color=red>CodeLock or Key Lock could not be found.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Code Lock does not have a code.</color>",
                ["EntityNotAllowed"] = "<color=red>Not allowed on this entity.</color>",
                ["LockCodeNotValid"] = "<color=red>Lock code is not valid (Requires 4 numbers).</color>",
                ["MissingParametersError"] = "<color=red>An error occurred: Missing parameters - [{0}].</color>",
                ["GenericError"] = "<color=red>An error occurred: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Delay Time not valid: Please enter a value between {0} and {1}.</color>",
                ["EntityNotFound"] = "<color=red>You have to look at an entity.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entity with skin not allowed.</color>",
                ["ToggleProximitySystemUI"] = "Proximity system: show UI",
                ["AlwaysShowUI"] = "Always show this window",
                ["DisableShowUI"] = "Don't show this window again",

                ["DoorCloserNotFound"] = "<color=red>The entity does not have a Door Closer.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>You do not have permission or Door Closer is not enabled.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>You don't have Door Closer enabled.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>You don't have the Door Closer in your inventory.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Invalid Door Closer category.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>A Door Closer is already placed on this entity.</color>",

                ["LockCategoryNotValid"] = "<color=red>Invalid lock category.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>You don't have permission or auto lock isn't enabled.</color>",
                ["LockItemNotFound"] = "<color=red>You don't have a lock in your inventory.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>You don't have a lock in your inventory.\nYou can only use locks without a code</color>",

                ["ShowCodeLockCodeMessage"] = "Code Lock code is:\nCode: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Code Lock code is:\nCode: <color=#faa511>{0}</color>\nGuest Code: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Streamer mode active:</color> code cannot be viewed",

                ["CodeLockPinChangedMessage"] = "Code Lock Pin changed.",
                ["AllCodeLockPinChangedMessage"] = "All Code Lock Pins have been changed.",
                ["AllGuestCodeLockPinChangedMessage"] = "All Guest Code Lock Pins have been changed.",
                ["AllGuestCodeLockPinRemovedMessage"] = "All Guest Code Lock Pins have been removed.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "All permissions for sharing Code Locks with your clan/team have been changed.",

                ["AutoLockKeyLockDeployedMessage"] = "Key Lock deployed and locked.",
                ["AutoLockCodeLockDeployedMessage"] = "Code Lock deployed and locked\nCode: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Code Lock deployed and locked\nCode: <color=#faa511>{0}</color>\nGuest Code: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] =
                    "Pin Code Lock: <color=red>{0}</color>. You can <color=red>change the pin</color> with the Modular Car Lift.",

                ["CodeLockListMessage"] = "You have {0} {1} Code Locks / key Locks.",
                ["CodeLockMessage"] = "You have {0} a Code Lock / Key Locks.",
                ["LockClosedAutomaticallyMessage"] = "The lock was closed automatically.",
                ["ToggleAutoLock"] = "You have {0} auto-lock (Lock)",

                ["SingleDoorCloserCustomTime"] = "You have set the time for this Door Closer to <color=#faa511>{0}s</color>.",
                ["ToggleAutoDoorCloser"] = "You have {0} auto-close.",
                ["DoorCloserListMessage"] = "You have {0} {1} Door Closer.",
                ["DoorCloserDeployed"] = "Door closer deployed. Delayed closing time set: <color=#faa511>{0}s</color>",

                ["UIDlcSkinRequired"] = "REQUIRES DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>You do not own the DLC/Skin used</color>",

                ["UiHeaderTitle"] = "Auto Lock Settings",
                ["UiShareLocksWithClanTeamEnabled"] = "Share Locks with Clan/Team",
                ["UiAutoLockOnPlacementEnabled"] = "Auto lock",
                ["UiCodeLockPin"] = "Code Lock Pin",
                ["UiGuestCodeEnabled"] = "Guest Code Lock",
                ["UiGuestCodeLockPin"] = "Guest Code Lock Pin",
                ["UiAlsoUseKeyLockEnabled"] = "Also Use Key Lock",
                ["UiStreamerModeEnabled"] = "Streamer Mode",
                ["UiCodelockSkinFlightControlCodelock"] = "Code Lock Skin: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Auto Lock: Doors",
                ["UiWindowsLockEnabled"] = "Auto Lock: Windows",
                ["UiBoxesLockEnabled"] = "Auto Lock: Boxes",
                ["UiStorageContainerLockEnabled"] = "Auto Lock: All Storage Container",
                ["UiLockersLockEnabled"] = "Auto Lock: Lockers",
                ["UiCupboardsLockEnabled"] = "Auto Lock: Cupboards (TC)",
                ["UiVehicleLockEnabled"] = "Auto Lock: Vehicle",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Automatic lock closing when dismount of the vehicle",
                ["UiNavalDeepSeaLockEnabled"] = "Auto Lock: Naval",
                ["UiMedievalEntityLockEnabled"] = "Auto Lock: Medieval Entities",
                ["UiFurnaceLockEnabled"] = "Auto Lock: Furnace / Refineries",
                ["UiVendingMachineLockEnabled"] = "Auto Lock: Vending Machine",
                ["UiComposterLockEnabled"] = "Auto Lock: Composter",
                ["UiMixingTableLockEnabled"] = "Auto Lock: Mixing Table",
                ["UiPlanterLockEnabled"] = "Auto Lock: Planter",
                ["UiAutoTurretLockEnabled"] = "Auto Lock: Auto Turret",
                ["UiSamSiteLockEnabled"] = "Auto Lock: SAM Site",
                ["UiTrapsLockEnabled"] = "Auto Lock: Traps",
                ["UiWeaponRackLockEnabled"] = "Auto Lock: Weapon Rack",
                ["UiStashLockEnabled"] = "Auto Lock: Stash",
                ["UiNeonSignLockEnabled"] = "Auto Lock: Neon Sign",
                ["UiSignDrawingLockEnabled"] = "Auto Lock: Drawing / Sign",
                ["UiOtherLockableEntitiesLockEnabled"] = "Auto Lock: Other Lockable Entities",
                ["UiOtherCustomEntitiesLockEnabled"] = "Auto Lock: Other Custom Entities",

                ["UiOpenAutoClosingButtonText"] = "Auto Closing Settings",
                ["UiAutoClosingHeaderTitle"] = "Auto Closing Settings",
                ["UiAutoClosingEnabledTitle"] = "Auto Closing Enabled",
                ["UiClosingDelay"] = "Closing Delay (s)",

                ["UiDoorAutoClosingEnabled"] = "Auto Closing: Door",
                ["UiDoubleDoorAutoClosingEnabled"] = "Auto Closing: Double Door",
                ["UiWindowsAutoClosingEnabled"] = "Auto Closing: Windows",
                ["UiGarageAutoClosingEnabled"] = "Auto Closing: Garage",
                ["UiLadderHatchAutoClosingEnabled"] = "Auto Closing: Ladder Hatch",
                ["UiExternalGateAutoClosingEnabled"] = "Auto Closing: External Gate",
                ["UiFenceGateAutoClosingEnabled"] = "Auto Closing: Fence Gate",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Auto Closing: Legacy Wood Shelter Door"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "BLOCCATO",
                ["LockerStatus_Unlocked"] = "SBLOCCATO",
                ["LockerStatus_Removed"] = "RIMOSSO",
                ["LockerStatus_Updated"] = "AGGIORNATO",
                ["UiEnabled"] = "ABILITATO",
                ["UiDisabled"] = "DISABILITATO",
                ["UiUpdate"] = "AGGIORNA",
                ["UiRemove"] = "RIMUOVI",
                ["UiUpdateAll"] = "AGGIORNA TUTTO",
                ["UiRemoveAll"] = "RIMUOVI TUTTO",
                ["UiButton_Yes"] = "SI",
                ["UiButton_No"] = "NO",

                ["NoCodeLockAuth"] = "È bloccato...",
                ["NoPermissions"] = "<color=red>Non hai l'autorizzazione per usare questo comando.</color>",
                ["NoBypassPermissions"] = "<color=red>Non hai l'autorizzazione per bypassare il codice.</color>",
                ["NoSteamIdOwner"] = "<color=red>Questo SteamID non è il proprietario dell'entità.</color>",
                ["NoEntityOwner"] = "<color=red>Non sei proprietario di questa entità.</color>",
                ["PlayerNotFound"] = "<color=red>Impossibile trovare il giocatore.</color>",
                ["InvalidSteamID"] = "<color=red>SteamID non valido.</color>",
                ["CodeLockNotFound"] = "<color=red>Impossibile trovare la Serratura a codice o la Serratura.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>La Serratura a codice non ha un codice.</color>",
                ["EntityNotAllowed"] = "<color=red>Non consentito su questa entità.</color>",
                ["LockCodeNotValid"] = "<color=red>Il codice di blocco non è valido (richiede 4 numeri).</color>",
                ["MissingParametersError"] = "<color=red>Si è verificato un errore: Parametri mancanti - [{0}].</color>",
                ["GenericError"] = "<color=red>Si è verificato un errore: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Tempo chiusura ritardata non valido: inserisci un valore compreso tra {0} e {1}.</color>",
                ["EntityNotFound"] = "<color=red>Devi guardare un entità.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entità con skin non consentita.</color>",
                ["ToggleProximitySystemUI"] = "Sistema di prossimità: mostra UI",
                ["AlwaysShowUI"] = "Mostra sempre questa finestra",
                ["DisableShowUI"] = "Non mostrare più questa finestra",

                ["DoorCloserNotFound"] = "<color=red>L'entità non ha un Chiudiporta.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>Non hai i permessi o la chiusura ritardata non è abilitata.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>Non hai la chiusura ritardata abilitata.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>Non hai il Door Closer nell'inventario.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Categoria chiusura automatica non valida.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Un Chiudiporta è gia posizionato su questa entità.</color>",

                ["LockCategoryNotValid"] = "<color=red>Categoria di blocco non valida.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>Non hai i permessi o il blocco automatico non è attivato.</color>",
                ["LockItemNotFound"] = "<color=red>Non hai una serratura nell'inventario.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>Non hai una serratura nell'inventario.\nPuoi usare solo serrature senza codice</color>",

                ["ShowCodeLockCodeMessage"] = "Il codice di blocco della Serratura a codice è:\nCodice: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] =
                    "Il codice di blocco della Serratura a codice è:\nCodice: <color=#faa511>{0}</color>\nCodice ospite: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Modalità streamer attiva:</color> il codice non può essere visualizzato",

                ["CodeLockPinChangedMessage"] = "Il codice PIN della Serratura a codice è stato modificato.",
                ["AllCodeLockPinChangedMessage"] = "Tutti i PIN delle Serrature a Codice sono stati modificati.",
                ["AllGuestCodeLockPinChangedMessage"] = "Tutti i codici PIN per gli ospiti sono stati modificati.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Tutti i codici PIN per gli ospiti sono stati rimossi.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Sono state modificate tutte le autorizzazioni di condivisione delle Serrature a codice con il clan/team.",

                ["AutoLockKeyLockDeployedMessage"] = "Serratura applicata e bloccata.",
                ["AutoLockCodeLockDeployedMessage"] = "Serratura a codice applicata e bloccata\nCodice: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Serratura a codice applicata e bloccata\nCodice: <color=#faa511>{0}</color>\nCodice ospite: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] =
                    "Codice PIN: <color=red>{0}</color>. È possibile <color=red>cambiare il PIN</color> con l'ascensore per auto.",

                ["CodeLockListMessage"] = "Hai {0} {1} Serrature a codice / Serrature.",
                ["CodeLockMessage"] = "Hai {0} la Serratura a codice / Serratura.",
                ["LockClosedAutomaticallyMessage"] = "La serratura è stata chiusa in automatico.",
                ["ToggleAutoLock"] = "Hai {0} il blocco automatico (Serratura)",

                ["SingleDoorCloserCustomTime"] = "Hai impostato a <color=#faa511>{0}s</color> il tempo per questa chiusura ritardata.",
                ["ToggleAutoDoorCloser"] = "Hai {0} la chiusura automatica.",
                ["DoorCloserListMessage"] = "Hai {0} {1} Chiudiporta.",
                ["DoorCloserDeployed"] = "Chiudiporta posizionato. Tempo impostato per la chiusura ritardata: <color=#faa511>{0}s</color>",

                ["UIDlcSkinRequired"] = "RICHIEDE IL DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Non possiedi il DLC/Skin utilizzato</color>",

                ["UiHeaderTitle"] = "Impostazioni blocco automatico",
                ["UiShareLocksWithClanTeamEnabled"] = "Condividi le Serrature con il clan/team",
                ["UiAutoLockOnPlacementEnabled"] = "Abilita il blocco automatico",
                ["UiCodeLockPin"] = "Pin Serratura a codice",
                ["UiGuestCodeEnabled"] = "Abilita Codice di blocco per ospiti",
                ["UiGuestCodeLockPin"] = "Codice PIN per gli ospiti",
                ["UiAlsoUseKeyLockEnabled"] = "Usa anche le Serrature senza codice",
                ["UiStreamerModeEnabled"] = "Modalità Streamer",
                ["UiCodelockSkinFlightControlCodelock"] = "Skin Serratura: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Blocco automatico: Porte",
                ["UiWindowsLockEnabled"] = "Blocco automatico: Finestre",
                ["UiBoxesLockEnabled"] = "Blocco automatico: Contenitori / Barili",
                ["UiStorageContainerLockEnabled"] = "Blocco automatico: Tutti i tipi di contenitore",
                ["UiLockersLockEnabled"] = "Blocco automatico: Armadietti",
                ["UiCupboardsLockEnabled"] = "Blocco automatico: Armadio degli attrezzi (TC)",
                ["UiVehicleLockEnabled"] = "Blocco automatico: Veicoli",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Chiusura automatica serratura quando scendi dal veicolo",
                ["UiNavalDeepSeaLockEnabled"] = "Blocco automatico: Navale",
                ["UiMedievalEntityLockEnabled"] = "Blocco automatico: Entità Medievali",
                ["UiFurnaceLockEnabled"] = "Blocco automatico: Fornaci / Raffinerie",
                ["UiVendingMachineLockEnabled"] = "Blocco automatico: Distributore automatico",
                ["UiComposterLockEnabled"] = "Blocco automatico: Compostiera",
                ["UiMixingTableLockEnabled"] = "Blocco automatico: Tavolo di miscelazione",
                ["UiPlanterLockEnabled"] = "Blocco automatico: Fioriera",
                ["UiAutoTurretLockEnabled"] = "Blocco automatico: Torrette automatiche",
                ["UiSamSiteLockEnabled"] = "Blocco automatico: Torrette SAM",
                ["UiTrapsLockEnabled"] = "Blocco automatico: Trappole",
                ["UiWeaponRackLockEnabled"] = "Blocco automatico: Rastrelliera per armi",
                ["UiStashLockEnabled"] = "Blocco automatico: Piccola Scorta",
                ["UiNeonSignLockEnabled"] = "Blocco automatico: Insegna al neon",
                ["UiSignDrawingLockEnabled"] = "Blocco automatico: Disegno / Cartello",
                ["UiOtherLockableEntitiesLockEnabled"] = "Blocco automatico: Altre entità bloccabili",
                ["UiOtherCustomEntitiesLockEnabled"] = "Blocco automatico: Altre entità personalizzate",

                ["UiOpenAutoClosingButtonText"] = "Impostazioni chiusura automatica",
                ["UiAutoClosingHeaderTitle"] = "Impostazioni chiusura automatica",
                ["UiAutoClosingEnabledTitle"] = "Chiusura automatica abilitata",
                ["UiClosingDelay"] = "Tempo di chiusura (s)",

                ["UiDoorAutoClosingEnabled"] = "Chiusura automatica: Porte",
                ["UiDoubleDoorAutoClosingEnabled"] = "Chiusura automatica: Porte doppie",
                ["UiWindowsAutoClosingEnabled"] = "Chiusura automatica: Finestre",
                ["UiGarageAutoClosingEnabled"] = "Chiusura automatica: Garage",
                ["UiLadderHatchAutoClosingEnabled"] = "Chiusura automatica: Scala a scomparsa",
                ["UiExternalGateAutoClosingEnabled"] = "Chiusura automatica: Portone mura",
                ["UiFenceGateAutoClosingEnabled"] = "Chiusura automatica: Cancello del recinto",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Chiusura automatica: porta del rifugio"
            }, this, "it");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "BLOQUEADO",
                ["LockerStatus_Unlocked"] = "DESABLOQUEADO",
                ["LockerStatus_Removed"] = "REMOVIDO",
                ["LockerStatus_Updated"] = "ACTUALIZADO",
                ["UiEnabled"] = "ACTIVADO",
                ["UiDisabled"] = "DESACTIVADO",
                ["UiUpdate"] = "ACTUALIZAR",
                ["UiRemove"] = "ELIMINAR",
                ["UiUpdateAll"] = "ACTUALIZAR TODO",
                ["UiRemoveAll"] = "REMOVER TODO",
                ["UiButton_Yes"] = "SÍ",
                ["UiButton_No"] = "NO",

                ["NoCodeLockAuth"] = "Está bloqueado...",
                ["NoPermissions"] = "<color=red>No tienes permiso para usar este comando.</color>",
                ["NoBypassPermissions"] = "<color=red>No tienes permiso para omitir el código.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID no es el propietario de la entidad.</color>",
                ["NoEntityOwner"] = "<color=red>No eres el dueño de esta entidad.</color>",
                ["PlayerNotFound"] = "<color=red>No se pudo encontrar al jugador.</color>",
                ["InvalidSteamID"] = "<color=red>SteamID inválido.</color>",
                ["CodeLockNotFound"] = "<color=red>No se pudo encontrar el CodeLock o Key Lock.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>El Code Lock no tiene un código.</color>",
                ["EntityNotAllowed"] = "<color=red>No permitido en esta entidad.</color>",
                ["LockCodeNotValid"] = "<color=red>El código de bloqueo no es válido (Requiere 4 números).</color>",
                ["MissingParametersError"] = "<color=red>Ocurrió un error: Parámetros faltantes - [{0}].</color>",
                ["GenericError"] = "<color=red>Ocurrió un error: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Tiempo de retraso no válido: Por favor ingrese un valor entre {0} y {1}.</color>",
                ["EntityNotFound"] = "<color=red>Tienes que mirar una entidad.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entidad con skin no permitida.</color>",
                ["ToggleProximitySystemUI"] = "Sistema de proximidad: mostrar UI",
                ["AlwaysShowUI"] = "Mostrar siempre esta ventana",
                ["DisableShowUI"] = "No volver a mostrar esta ventana",
                ["DoorCloserNotFound"] = "<color=red>La entidad no tiene un Cierre de Puerta.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>No tienes permiso o el Cierre de Puerta no está habilitado.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>No tienes el Cierre de Puerta habilitado.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>No tienes el Cierre de Puerta en tu inventario.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Categoría de Cierre de Puerta inválida.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Ya hay un Cierre de Puerta colocado en esta entidad.</color>",
                ["LockCategoryNotValid"] = "<color=red>Categoría de bloqueo inválida.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>No tienes permiso o el bloqueo automático no está habilitado.</color>",
                ["LockItemNotFound"] = "<color=red>No tienes un candado en tu inventario.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>No tienes un candado en tu inventario.\nSolo puedes usar candados sin código</color>",
                ["ShowCodeLockCodeMessage"] = "El código del Code Lock es:\nCódigo: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "El código del Code Lock es:\nCódigo: <color=#faa511>{0}</color>\nCódigo de Invitado: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Modo Streamer activo:</color> código no puede ser visto",
                ["CodeLockPinChangedMessage"] = "Pin del Code Lock cambiado.",
                ["AllCodeLockPinChangedMessage"] = "Todos los Pines del Code Lock han sido cambiados.",
                ["AllGuestCodeLockPinChangedMessage"] = "Todos los Pines de Invitado del Code Lock han sido cambiados.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Todos los Pines de Invitado del Code Lock han sido removidos.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Todos los permisos para compartir Code Locks con tu clan/equipo han sido cambiados.",
                ["AutoLockKeyLockDeployedMessage"] = "Candado de llave desplegado y bloqueado.",
                ["AutoLockCodeLockDeployedMessage"] = "Code Lock desplegado y bloqueado\nCódigo: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Code Lock desplegado y bloqueado\nCódigo: <color=#faa511>{0}</color>\nCódigo de Invitado: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Pin Code Lock: <color=red>{0}</color>. Puedes <color=red>cambiar el pin</color> con el Elevador de Coches Modular.",
                ["CodeLockListMessage"] = "Tienes {0} Code Locks / Key Locks {1}.",
                ["CodeLockMessage"] = "Tienes {0} un Code Lock / Key Locks.",
                ["LockClosedAutomaticallyMessage"] = "El candado se cerró automáticamente.",
                ["ToggleAutoLock"] = "Tienes {0} bloqueo automático (Cerradura)",
                ["SingleDoorCloserCustomTime"] = "Has establecido el tiempo para este Cierre de Puerta en <color=#faa511>{0}s</color>.",
                ["ToggleAutoDoorCloser"] = "Tienes {0} cierre automático.",
                ["DoorCloserListMessage"] = "Tienes {0} Cierre de Puerta {1}.",
                ["DoorCloserDeployed"] = "Cierre de Puerta desplegado. Tiempo de cierre diferido establecido: <color=#faa511>{0}s</color>",
                ["UIDlcSkinRequired"] = "REQUIERE DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>No eres dueño del DLC/Skin utilizado</color>",
                ["UiHeaderTitle"] = "Configuraciones de Bloqueo Automático",
                ["UiShareLocksWithClanTeamEnabled"] = "Compartir Bloqueos con Clan/Equipo",
                ["UiAutoLockOnPlacementEnabled"] = "Bloqueo automático",
                ["UiCodeLockPin"] = "Pin del Code Lock",
                ["UiGuestCodeEnabled"] = "Código de Invitado del Code Lock",
                ["UiGuestCodeLockPin"] = "Pin del Código de Invitado del Code Lock",
                ["UiAlsoUseKeyLockEnabled"] = "También Usar Candado de Llave",
                ["UiStreamerModeEnabled"] = "Modo Streamer",
                ["UiCodelockSkinFlightControlCodelock"] = "Skin de Code Lock: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Bloqueo Automático: Puertas",
                ["UiWindowsLockEnabled"] = "Bloqueo Automático: Ventanas",
                ["UiBoxesLockEnabled"] = "Bloqueo Automático: Cajas",
                ["UiStorageContainerLockEnabled"] = "Bloqueo Automático: Todos los Contenedores de Almacenamiento",
                ["UiLockersLockEnabled"] = "Bloqueo Automático: Lockers",
                ["UiCupboardsLockEnabled"] = "Bloqueo Automático: Armarios (TC)",
                ["UiVehicleLockEnabled"] = "Bloqueo Automático: Vehículo",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Cierre automático al desmontar del vehículo",
                ["UiNavalDeepSeaLockEnabled"] = "Bloqueo Automático: Naval",
                ["UiMedievalEntityLockEnabled"] = "Bloqueo Automático: Entidades Medievales",
                ["UiFurnaceLockEnabled"] = "Bloqueo Automático: Horno / Refinerías",
                ["UiVendingMachineLockEnabled"] = "Bloqueo Automático: Máquina Expendedora",
                ["UiComposterLockEnabled"] = "Bloqueo Automático: Compostador",
                ["UiMixingTableLockEnabled"] = "Bloqueo Automático: Mesa de Mezclas",
                ["UiPlanterLockEnabled"] = "Bloqueo Automático: Plantador",
                ["UiAutoTurretLockEnabled"] = "Bloqueo Automático: Torreta Automática",
                ["UiSamSiteLockEnabled"] = "Bloqueo Automático: Sitio SAM",
                ["UiTrapsLockEnabled"] = "Bloqueo Automático: Trampas",
                ["UiWeaponRackLockEnabled"] = "Bloqueo Automático: Soporte de Armas",
                ["UiStashLockEnabled"] = "Bloqueo Automático: Escondite",
                ["UiNeonSignLockEnabled"] = "Bloqueo Automático: Letrero de Neón",
                ["UiSignDrawingLockEnabled"] = "Bloqueo Automático: Dibujo/Señal",
                ["UiOtherLockableEntitiesLockEnabled"] = "Bloqueo Automático: Otras Entidades Bloqueables",
                ["UiOtherCustomEntitiesLockEnabled"] = "Bloqueo Automático: Otras Entidades Personalizadas",
                ["UiOpenAutoClosingButtonText"] = "Configuraciones de Cierre Automático",
                ["UiAutoClosingHeaderTitle"] = "Configuraciones de Cierre Automático",
                ["UiAutoClosingEnabledTitle"] = "Cierre Automático Habilitado",
                ["UiClosingDelay"] = "Retraso de Cierre (s)",
                ["UiDoorAutoClosingEnabled"] = "Cierre Automático: Puerta",
                ["UiDoubleDoorAutoClosingEnabled"] = "Cierre Automático: Puerta Doble",
                ["UiWindowsAutoClosingEnabled"] = "Cierre Automático: Ventanas",
                ["UiGarageAutoClosingEnabled"] = "Cierre Automático: Garaje",
                ["UiLadderHatchAutoClosingEnabled"] = "Cierre Automático: Escotilla de Escalera",
                ["UiExternalGateAutoClosingEnabled"] = "Cierre Automático: Puerta Externa",
                ["UiFenceGateAutoClosingEnabled"] = "Cierre Automático: Puerta de Cerca",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Cierre Automático: Puerta de Refugio de Madera Antiguo"
            }, this, "es");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "BLOQUEADO",
                ["LockerStatus_Unlocked"] = "DESABLOQUEADO",
                ["LockerStatus_Removed"] = "REMOVIDO",
                ["LockerStatus_Updated"] = "ACTUALIZADO",
                ["UiEnabled"] = "ACTIVADO",
                ["UiDisabled"] = "DESACTIVADO",
                ["UiUpdate"] = "ACTUALIZAR",
                ["UiRemove"] = "ELIMINAR",
                ["UiUpdateAll"] = "ACTUALIZAR TODO",
                ["UiRemoveAll"] = "REMOVER TODO",
                ["UiButton_Yes"] = "SÍ",
                ["UiButton_No"] = "NO",

                ["NoCodeLockAuth"] = "Está bloqueado...",
                ["NoPermissions"] = "<color=red>No tienes permiso para usar este comando.</color>",
                ["NoBypassPermissions"] = "<color=red>No tienes permiso para omitir el código.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID no es el propietario de la entidad.</color>",
                ["NoEntityOwner"] = "<color=red>No eres el dueño de esta entidad.</color>",
                ["PlayerNotFound"] = "<color=red>No se pudo encontrar al jugador.</color>",
                ["InvalidSteamID"] = "<color=red>SteamID inválido.</color>",
                ["CodeLockNotFound"] = "<color=red>No se pudo encontrar el CodeLock o Key Lock.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>El Code Lock no tiene un código.</color>",
                ["EntityNotAllowed"] = "<color=red>No permitido en esta entidad.</color>",
                ["LockCodeNotValid"] = "<color=red>El código de bloqueo no es válido (Requiere 4 números).</color>",
                ["MissingParametersError"] = "<color=red>Ocurrió un error: Parámetros faltantes - [{0}].</color>",
                ["GenericError"] = "<color=red>Ocurrió un error: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Tiempo de retraso no válido: Por favor ingrese un valor entre {0} y {1}.</color>",
                ["EntityNotFound"] = "<color=red>Tienes que mirar una entidad.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entidad con skin no permitida.</color>",
                ["ToggleProximitySystemUI"] = "Sistema de proximidad: mostrar UI",
                ["AlwaysShowUI"] = "Mostrar siempre esta ventana",
                ["DisableShowUI"] = "No volver a mostrar esta ventana",
                ["DoorCloserNotFound"] = "<color=red>La entidad no tiene un Cierre de Puerta.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>No tienes permiso o el Cierre de Puerta no está habilitado.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>No tienes el Cierre de Puerta habilitado.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>No tienes el Cierre de Puerta en tu inventario.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Categoría de Cierre de Puerta inválida.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Ya hay un Cierre de Puerta colocado en esta entidad.</color>",
                ["LockCategoryNotValid"] = "<color=red>Categoría de bloqueo inválida.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>No tienes permiso o el bloqueo automático no está habilitado.</color>",
                ["LockItemNotFound"] = "<color=red>No tienes un candado en tu inventario.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>No tienes un candado en tu inventario.\nSolo puedes usar candados sin código</color>",
                ["ShowCodeLockCodeMessage"] = "El código del Code Lock es:\nCódigo: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "El código del Code Lock es:\nCódigo: <color=#faa511>{0}</color>\nCódigo de Invitado: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Modo Streamer activo:</color> código no puede ser visto",
                ["CodeLockPinChangedMessage"] = "Pin del Code Lock cambiado.",
                ["AllCodeLockPinChangedMessage"] = "Todos los Pines del Code Lock han sido cambiados.",
                ["AllGuestCodeLockPinChangedMessage"] = "Todos los Pines de Invitado del Code Lock han sido cambiados.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Todos los Pines de Invitado del Code Lock han sido removidos.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Todos los permisos para compartir Code Locks con tu clan/equipo han sido cambiados.",
                ["AutoLockKeyLockDeployedMessage"] = "Candado de llave desplegado y bloqueado.",
                ["AutoLockCodeLockDeployedMessage"] = "Code Lock desplegado y bloqueado\nCódigo: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Code Lock desplegado y bloqueado\nCódigo: <color=#faa511>{0}</color>\nCódigo de Invitado: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Pin Code Lock: <color=red>{0}</color>. Puedes <color=red>cambiar el pin</color> con el Elevador de Coches Modular.",
                ["CodeLockListMessage"] = "Tienes {0} Code Locks / Key Locks {1}.",
                ["CodeLockMessage"] = "Tienes {0} un Code Lock / Key Locks.",
                ["LockClosedAutomaticallyMessage"] = "El candado se cerró automáticamente.",
                ["ToggleAutoLock"] = "Tienes {0} bloqueo automático (Cerradura)",
                ["SingleDoorCloserCustomTime"] = "Has establecido el tiempo para este Cierre de Puerta en <color=#faa511>{0}s</color>.",
                ["ToggleAutoDoorCloser"] = "Tienes {0} cierre automático.",
                ["DoorCloserListMessage"] = "Tienes {0} Cierre de Puerta {1}.",
                ["DoorCloserDeployed"] = "Cierre de Puerta desplegado. Tiempo de cierre diferido establecido: <color=#faa511>{0}s</color>",
                ["UIDlcSkinRequired"] = "REQUIERE DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>No eres dueño del DLC/Skin utilizado</color>",
                ["UiHeaderTitle"] = "Configuraciones de Bloqueo Automático",
                ["UiShareLocksWithClanTeamEnabled"] = "Compartir Bloqueos con Clan/Equipo",
                ["UiAutoLockOnPlacementEnabled"] = "Bloqueo automático",
                ["UiCodeLockPin"] = "Pin del Code Lock",
                ["UiGuestCodeEnabled"] = "Código de Invitado del Code Lock",
                ["UiGuestCodeLockPin"] = "Pin del Código de Invitado del Code Lock",
                ["UiAlsoUseKeyLockEnabled"] = "También Usar Candado de Llave",
                ["UiStreamerModeEnabled"] = "Modo Streamer",
                ["UiCodelockSkinFlightControlCodelock"] = "Skin de Code Lock: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Bloqueo Automático: Puertas",
                ["UiWindowsLockEnabled"] = "Bloqueo Automático: Ventanas",
                ["UiBoxesLockEnabled"] = "Bloqueo Automático: Cajas",
                ["UiStorageContainerLockEnabled"] = "Bloqueo Automático: Todos los Contenedores de Almacenamiento",
                ["UiLockersLockEnabled"] = "Bloqueo Automático: Lockers",
                ["UiCupboardsLockEnabled"] = "Bloqueo Automático: Armarios (TC)",
                ["UiVehicleLockEnabled"] = "Bloqueo Automático: Vehículo",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Cierre automático al desmontar del vehículo",
                ["UiNavalDeepSeaLockEnabled"] = "Bloqueo Automático: Naval",
                ["UiMedievalEntityLockEnabled"] = "Bloqueo Automático: Entidades Medievales",
                ["UiFurnaceLockEnabled"] = "Bloqueo Automático: Horno / Refinerías",
                ["UiVendingMachineLockEnabled"] = "Bloqueo Automático: Máquina Expendedora",
                ["UiComposterLockEnabled"] = "Bloqueo Automático: Compostador",
                ["UiMixingTableLockEnabled"] = "Bloqueo Automático: Mesa de Mezclas",
                ["UiPlanterLockEnabled"] = "Bloqueo Automático: Plantador",
                ["UiAutoTurretLockEnabled"] = "Bloqueo Automático: Torreta Automática",
                ["UiSamSiteLockEnabled"] = "Bloqueo Automático: Sitio SAM",
                ["UiTrapsLockEnabled"] = "Bloqueo Automático: Trampas",
                ["UiWeaponRackLockEnabled"] = "Bloqueo Automático: Soporte de Armas",
                ["UiStashLockEnabled"] = "Bloqueo Automático: Escondite",
                ["UiNeonSignLockEnabled"] = "Bloqueo Automático: Letrero de Neón",
                ["UiSignDrawingLockEnabled"] = "Bloqueo Automático: Dibujo/Señal",
                ["UiOtherLockableEntitiesLockEnabled"] = "Bloqueo Automático: Otras Entidades Bloqueables",
                ["UiOtherCustomEntitiesLockEnabled"] = "Bloqueo Automático: Otras Entidades Personalizadas",
                ["UiOpenAutoClosingButtonText"] = "Configuraciones de Cierre Automático",
                ["UiAutoClosingHeaderTitle"] = "Configuraciones de Cierre Automático",
                ["UiAutoClosingEnabledTitle"] = "Cierre Automático Habilitado",
                ["UiClosingDelay"] = "Retraso de Cierre (s)",
                ["UiDoorAutoClosingEnabled"] = "Cierre Automático: Puerta",
                ["UiDoubleDoorAutoClosingEnabled"] = "Cierre Automático: Puerta Doble",
                ["UiWindowsAutoClosingEnabled"] = "Cierre Automático: Ventanas",
                ["UiGarageAutoClosingEnabled"] = "Cierre Automático: Garaje",
                ["UiLadderHatchAutoClosingEnabled"] = "Cierre Automático: Escotilla de Escalera",
                ["UiExternalGateAutoClosingEnabled"] = "Cierre Automático: Puerta Externa",
                ["UiFenceGateAutoClosingEnabled"] = "Cierre Automático: Puerta de Cerca",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Cierre Automático: Puerta de Refugio de Madera Antiguo"
            }, this, "es-ES");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "VERROUILLÉ",
                ["LockerStatus_Unlocked"] = "DÉVERROUILLÉ",
                ["LockerStatus_Removed"] = "ENLEVÉ",
                ["LockerStatus_Updated"] = "MIS À JOUR",
                ["UiEnabled"] = "ACTIVÉ",
                ["UiDisabled"] = "DÉSACTIVÉ",
                ["UiUpdate"] = "METTRE À JOUR",
                ["UiRemove"] = "SUPPRIMER",
                ["UiUpdateAll"] = "TOUT METTRE À JOUR",
                ["UiRemoveAll"] = "TOUT SUPPRIMER",
                ["UiButton_Yes"] = "OUI",
                ["UiButton_No"] = "NON",

                ["NoCodeLockAuth"] = "Il est bloqué...",
                ["NoPermissions"] = "<color=red>Vous n'avez pas la permission d'utiliser cette commande.</color>",
                ["NoBypassPermissions"] = "<color=red>Vous n'avez pas la permission de contourner le code.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID n'est pas le propriétaire de l'entité.</color>",
                ["NoEntityOwner"] = "<color=red>Vous ne possédez pas cette entité.</color>",
                ["PlayerNotFound"] = "<color=red>Le joueur n'a pas pu être trouvé.</color>",
                ["InvalidSteamID"] = "<color=red>SteamID invalide.</color>",
                ["CodeLockNotFound"] = "<color=red>CodeLock ou Key Lock n'a pas pu être trouvé.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Le Code Lock n'a pas de code.</color>",
                ["EntityNotAllowed"] = "<color=red>Non autorisé sur cette entité.</color>",
                ["LockCodeNotValid"] = "<color=red>Le code de verrouillage n'est pas valide (nécessite 4 chiffres).</color>",
                ["MissingParametersError"] = "<color=red>Une erreur s'est produite: Paramètres manquants - [{0}].</color>",
                ["GenericError"] = "<color=red>Une erreur s'est produite: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Temps de retard non valide: Veuillez entrer une valeur entre {0} et {1}.</color>",
                ["EntityNotFound"] = "<color=red>Vous devez regarder une entité.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entité avec une peau non autorisée.</color>",
                ["ToggleProximitySystemUI"] = "Système de proximité : afficher l’interface",
                ["AlwaysShowUI"] = "Toujours afficher cette fenêtre",
                ["DisableShowUI"] = "Ne plus afficher cette fenêtre",
                ["DoorCloserNotFound"] = "<color=red>L'entité n'a pas de ferme-porte.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>Vous n'avez pas la permission ou le ferme-porte n'est pas activé.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>Vous n'avez pas activé le ferme-porte.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>Vous n'avez pas le ferme-porte dans votre inventaire.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Catégorie de ferme-porte non valide.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Un ferme-porte est déjà placé sur cette entité.</color>",
                ["LockCategoryNotValid"] = "<color=red>Catégorie de verrouillage non valide.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>Vous n'avez pas la permission ou le verrouillage automatique n'est pas activé.</color>",
                ["LockItemNotFound"] = "<color=red>Vous n'avez pas de verrou dans votre inventaire.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>Vous n'avez pas de cadenas dans votre inventaire.\nVous ne pouvez utiliser que des cadenas sans code</color>",
                ["ShowCodeLockCodeMessage"] = "Le code du Code Lock est:\nCode: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Le code du Code Lock est:\nCode: <color=#faa511>{0}</color>\nCode invité: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Mode Streamer actif:</color> le code ne peut pas être visualisé",
                ["CodeLockPinChangedMessage"] = "Le code du Code Lock a été changé.",
                ["AllCodeLockPinChangedMessage"] = "Tous les codes du Code Lock ont été changés.",
                ["AllGuestCodeLockPinChangedMessage"] = "Tous les codes invités du Code Lock ont été changés.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Tous les codes invités du Code Lock ont été supprimés.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Toutes les autorisations de partage des Code Locks avec votre clan/équipe ont été modifiées.",
                ["AutoLockKeyLockDeployedMessage"] = "Verrou à clé déployé et verrouillé.",
                ["AutoLockCodeLockDeployedMessage"] = "Code Lock déployé et verrouillé\nCode: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] = "Code Lock déployé et verrouillé\nCode: <color=#faa511>{0}</color>\nCode invité: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Code Pin Lock: <color=red>{0}</color>. Vous pouvez <color=red>changer le code</color> avec le Ascenseur de Voiture Modulaire.",
                ["CodeLockListMessage"] = "Vous avez {0} {1} Code Locks / verrous à clé.",
                ["CodeLockMessage"] = "Vous avez {0} un Code Lock / verrous à clé.",
                ["LockClosedAutomaticallyMessage"] = "Le verrou a été fermé automatiquement.",
                ["ToggleAutoLock"] = "Vous avez {0} verrouillage automatique (Cadenas)",
                ["SingleDoorCloserCustomTime"] = "Vous avez défini le temps pour ce ferme-porte à <color=#faa511>{0}s</color>.",
                ["ToggleAutoDoorCloser"] = "Vous avez {0} fermeture automatique.",
                ["DoorCloserListMessage"] = "Vous avez {0} {1} ferme-porte.",
                ["DoorCloserDeployed"] = "Ferme-porte déployé. Temps de fermeture différée défini: <color=#faa511>{0}s</color>",
                ["UIDlcSkinRequired"] = "DLC REQUIS",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Vous ne possédez pas le DLC/Skin utilisé</color>",
                ["UiHeaderTitle"] = "Paramètres de Verrouillage Automatique",
                ["UiShareLocksWithClanTeamEnabled"] = "Partager les verrous avec le Clan/Équipe",
                ["UiAutoLockOnPlacementEnabled"] = "Verrouillage automatique",
                ["UiCodeLockPin"] = "Code Pin du Code Lock",
                ["UiGuestCodeEnabled"] = "Verrouillage du Code invité",
                ["UiGuestCodeLockPin"] = "Code Pin du Code invité",
                ["UiAlsoUseKeyLockEnabled"] = "Utiliser également le verrou à clé",
                ["UiStreamerModeEnabled"] = "Mode Streamer",
                ["UiCodelockSkinFlightControlCodelock"] = "Skin Code Lock: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Verrouillage automatique: Portes",
                ["UiWindowsLockEnabled"] = "Verrouillage automatique: Fenêtres",
                ["UiBoxesLockEnabled"] = "Verrouillage automatique: Boîtes",
                ["UiStorageContainerLockEnabled"] = "Verrouillage automatique: Tous les Conteneurs de Stockage",
                ["UiLockersLockEnabled"] = "Verrouillage automatique: Casiers",
                ["UiCupboardsLockEnabled"] = "Verrouillage automatique: Armoires (TC)",
                ["UiVehicleLockEnabled"] = "Verrouillage automatique: Véhicule",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Fermeture automatique du verrou lors de la descente du véhicule",
                ["UiNavalDeepSeaLockEnabled"] = "Verrouillage automatique: Naval",
                ["UiMedievalEntityLockEnabled"] = "Verrouillage automatique: Entités Médiévales",
                ["UiFurnaceLockEnabled"] = "Verrouillage automatique: Four / Raffineries",
                ["UiVendingMachineLockEnabled"] = "Verrouillage automatique: Distributeur Automatique",
                ["UiComposterLockEnabled"] = "Verrouillage automatique: Composteur",
                ["UiMixingTableLockEnabled"] = "Verrouillage automatique: Table de Mélange",
                ["UiPlanterLockEnabled"] = "Verrouillage automatique: Jardinière",
                ["UiAutoTurretLockEnabled"] = "Verrouillage automatique: Tourelle Automatique",
                ["UiSamSiteLockEnabled"] = "Verrouillage automatique: Site SAM",
                ["UiTrapsLockEnabled"] = "Verrouillage automatique: Pièges",
                ["UiWeaponRackLockEnabled"] = "Verrouillage automatique: Râtelier d'Armes",
                ["UiStashLockEnabled"] = "Verrouillage automatique: Cachette",
                ["UiNeonSignLockEnabled"] = "Verrouillage automatique: Enseigne Lumineuse",
                ["UiSignDrawingLockEnabled"] = "Verrouillage automatique: Dessin / Signe",
                ["UiOtherLockableEntitiesLockEnabled"] = "Verrouillage automatique: Autres Entités Verrouillables",
                ["UiOtherCustomEntitiesLockEnabled"] = "Verrouillage automatique: Autres Entités Personnalisées",
                ["UiOpenAutoClosingButtonText"] = "Paramètres de Fermeture Automatique",
                ["UiAutoClosingHeaderTitle"] = "Paramètres de Fermeture Automatique",
                ["UiAutoClosingEnabledTitle"] = "Fermeture Automatique Activée",
                ["UiClosingDelay"] = "Délai de Fermeture (s)",
                ["UiDoorAutoClosingEnabled"] = "Fermeture Automatique: Porte",
                ["UiDoubleDoorAutoClosingEnabled"] = "Fermeture Automatique: Double Porte",
                ["UiWindowsAutoClosingEnabled"] = "Fermeture Automatique: Fenêtres",
                ["UiGarageAutoClosingEnabled"] = "Fermeture Automatique: Garage",
                ["UiLadderHatchAutoClosingEnabled"] = "Fermeture Automatique: Trappe d'Échelle",
                ["UiExternalGateAutoClosingEnabled"] = "Fermeture Automatique: Portail Externe",
                ["UiFenceGateAutoClosingEnabled"] = "Fermeture Automatique: Portail de Clôture",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Fermeture Automatique: Porte Abri en Bois Ancien"
            }, this, "fr");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "GESCHLOSSEN",
                ["LockerStatus_Unlocked"] = "ENTRIEGELT",
                ["LockerStatus_Removed"] = "ENTFERNT",
                ["LockerStatus_Updated"] = "AKTUALISIERT",
                ["UiEnabled"] = "AKTIVIERT",
                ["UiDisabled"] = "DEAKTIVIERT",
                ["UiUpdate"] = "AKTUALISIEREN",
                ["UiRemove"] = "ENTFERNEN",
                ["UiUpdateAll"] = "ALLE AKTUALISIEREN",
                ["UiRemoveAll"] = "ALLE ENTFERNEN",
                ["UiButton_Yes"] = "JA",
                ["UiButton_No"] = "NEIN",

                ["NoCodeLockAuth"] = "Es ist blockiert...",
                ["NoPermissions"] = "<color=red>Sie haben keine Berechtigung, diesen Befehl zu verwenden.</color>",
                ["NoBypassPermissions"] = "<color=red>Sie haben keine Berechtigung, den Code zu umgehen.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID ist nicht der Besitzer der Entität.</color>",
                ["NoEntityOwner"] = "<color=red>Sie besitzen diese Entität nicht.</color>",
                ["PlayerNotFound"] = "<color=red>Der Spieler konnte nicht gefunden werden.</color>",
                ["InvalidSteamID"] = "<color=red>Ungültige SteamID.</color>",
                ["CodeLockNotFound"] = "<color=red>CodeLock oder Key Lock konnte nicht gefunden werden.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Der Code Lock hat keinen Code.</color>",
                ["EntityNotAllowed"] = "<color=red>Nicht erlaubt auf dieser Entität.</color>",
                ["LockCodeNotValid"] = "<color=red>Schlosscode ist ungültig (erfordert 4 Zahlen).</color>",
                ["MissingParametersError"] = "<color=red>Es ist ein Fehler aufgetreten: Fehlende Parameter - [{0}].</color>",
                ["GenericError"] = "<color=red>Es ist ein Fehler aufgetreten: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Verzögerungszeit ungültig: Bitte geben Sie einen Wert zwischen {0} und {1} ein.</color>",
                ["EntityNotFound"] = "<color=red>Sie müssen eine Entität ansehen.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entitäten mit Skins sind nicht erlaubt.</color>",
                ["ToggleProximitySystemUI"] = "Näherungssystem: UI anzeigen",
                ["AlwaysShowUI"] = "Dieses Fenster immer anzeigen",
                ["DisableShowUI"] = "Dieses Fenster nicht erneut anzeigen",
                ["DoorCloserNotFound"] = "<color=red>Die Entität hat keinen Türschließer.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>Sie haben keine Berechtigung oder der Türschließer ist nicht aktiviert.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>Sie haben den Türschließer nicht aktiviert.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>Sie haben den Türschließer nicht in Ihrem Inventar.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Ungültige Türschließer-Kategorie.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Ein Türschließer ist bereits auf dieser Entität platziert.</color>",
                ["LockCategoryNotValid"] = "<color=red>Ungültige Schlosskategorie.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>Sie haben keine Berechtigung oder die automatische Verriegelung ist nicht aktiviert.</color>",
                ["LockItemNotFound"] = "<color=red>Sie haben kein Schloss in Ihrem Inventar.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>Du hast kein Schloss in deinem Inventar.\nDu kannst nur Schlösser ohne Code verwenden</color>",
                ["ShowCodeLockCodeMessage"] = "Der Code des Code Locks lautet:\nCode: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Der Code des Code Locks lautet:\nCode: <color=#faa511>{0}</color>\nGast-Code: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Streamer-Modus aktiviert:</color> Code kann nicht angezeigt werden",
                ["CodeLockPinChangedMessage"] = "Code Lock Pin geändert.",
                ["AllCodeLockPinChangedMessage"] = "Alle Code Lock Pins wurden geändert.",
                ["AllGuestCodeLockPinChangedMessage"] = "Alle Gast-Code Lock Pins wurden geändert.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Alle Gast-Code Lock Pins wurden entfernt.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Alle Berechtigungen zum Teilen von Code Locks mit Ihrem Clan/Team wurden geändert.",
                ["AutoLockKeyLockDeployedMessage"] = "Key Lock bereitgestellt und verriegelt.",
                ["AutoLockCodeLockDeployedMessage"] = "Code Lock bereitgestellt und verriegelt\nCode: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Code Lock bereitgestellt und verriegelt\nCode: <color=#faa511>{0}</color>\nGast-Code: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Pin Code Lock: <color=red>{0}</color>. Sie können <color=red>den Pin ändern</color> mit dem Modularen Autoaufzug.",
                ["CodeLockListMessage"] = "Sie haben {0} {1} Code Locks / Schlösser.",
                ["CodeLockMessage"] = "Sie haben {0} ein Code Lock / Schlösser.",
                ["LockClosedAutomaticallyMessage"] = "Das Schloss wurde automatisch geschlossen.",
                ["ToggleAutoLock"] = "Du hast {0} automatische Verriegelung (Schloss)",
                ["SingleDoorCloserCustomTime"] = "Sie haben die Zeit für diesen Türschließer auf <color=#faa511>{0}s</color> eingestellt.",
                ["ToggleAutoDoorCloser"] = "Du hast das automatische Schließen {0}.",
                ["DoorCloserListMessage"] = "Sie haben {0} {1} Türschließer.",
                ["DoorCloserDeployed"] = "Türschließer bereitgestellt. Verzögerte Schließzeit festgelegt: <color=#faa511>{0}s</color>",
                ["UIDlcSkinRequired"] = "ERFORDERT DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Du besitzt den verwendeten DLC/Skin nicht</color>",
                ["UiHeaderTitle"] = "Automatische Verriegelungseinstellungen",
                ["UiShareLocksWithClanTeamEnabled"] = "Schlösser mit Clan/Team teilen",
                ["UiAutoLockOnPlacementEnabled"] = "Automatische Verriegelung",
                ["UiCodeLockPin"] = "Code Lock Pin",
                ["UiGuestCodeEnabled"] = "Gast-Code Lock",
                ["UiGuestCodeLockPin"] = "Gast-Code Lock Pin",
                ["UiAlsoUseKeyLockEnabled"] = "Auch Key Lock verwenden",
                ["UiStreamerModeEnabled"] = "Streamer-Modus",
                ["UiCodelockSkinFlightControlCodelock"] = "Codeschloss-Skin: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Automatische Verriegelung: Türen",
                ["UiWindowsLockEnabled"] = "Automatische Verriegelung: Fenster",
                ["UiBoxesLockEnabled"] = "Automatische Verriegelung: Kisten",
                ["UiStorageContainerLockEnabled"] = "Automatische Verriegelung: Alle Lagerbehälter",
                ["UiLockersLockEnabled"] = "Automatische Verriegelung: Schließfächer",
                ["UiCupboardsLockEnabled"] = "Automatische Verriegelung: Schränke (TC)",
                ["UiVehicleLockEnabled"] = "Automatische Verriegelung: Fahrzeug",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Automatisches Schließen beim Absteigen vom Fahrzeug",
                ["UiNavalDeepSeaLockEnabled"] = "Automatische Verriegelung: Marine",
                ["UiMedievalEntityLockEnabled"] = "Automatische Verriegelung: Mittelalterliche Entitäten",
                ["UiFurnaceLockEnabled"] = "Automatische Verriegelung: Ofen / Raffinerien",
                ["UiVendingMachineLockEnabled"] = "Automatische Verriegelung: Verkaufsautomat",
                ["UiComposterLockEnabled"] = "Automatische Verriegelung: Komposter",
                ["UiMixingTableLockEnabled"] = "Automatische Verriegelung: Mischpult",
                ["UiPlanterLockEnabled"] = "Automatische Verriegelung: Pflanzgefäß",
                ["UiAutoTurretLockEnabled"] = "Automatische Verriegelung: Auto-Geschütz",
                ["UiSamSiteLockEnabled"] = "Automatische Verriegelung: SAM-Site",
                ["UiTrapsLockEnabled"] = "Automatische Verriegelung: Fallen",
                ["UiWeaponRackLockEnabled"] = "Automatische Verriegelung: Waffenständer",
                ["UiStashLockEnabled"] = "Automatische Verriegelung: Versteck",
                ["UiNeonSignLockEnabled"] = "Automatische Verriegelung: Neon-Schild",
                ["UiSignDrawingLockEnabled"] = "Automatische Verriegelung: Zeichnung / Zeichen",
                ["UiOtherLockableEntitiesLockEnabled"] = "Automatische Verriegelung: Andere verriegelbare Entitäten",
                ["UiOtherCustomEntitiesLockEnabled"] = "Automatische Verriegelung: Andere benutzerdefinierte Entitäten",
                ["UiOpenAutoClosingButtonText"] = "Auto-Schließ-Einstellungen",
                ["UiAutoClosingHeaderTitle"] = "Auto-Schließ-Einstellungen",
                ["UiAutoClosingEnabledTitle"] = "Auto-Schließung aktiviert",
                ["UiClosingDelay"] = "Schließungsverzögerung (s)",
                ["UiDoorAutoClosingEnabled"] = "Automatische Schließung: Tür",
                ["UiDoubleDoorAutoClosingEnabled"] = "Automatische Schließung: Doppeltür",
                ["UiWindowsAutoClosingEnabled"] = "Automatische Schließung: Fenster",
                ["UiGarageAutoClosingEnabled"] = "Automatische Schließung: Garage",
                ["UiLadderHatchAutoClosingEnabled"] = "Automatische Schließung: Leiterklappe",
                ["UiExternalGateAutoClosingEnabled"] = "Automatische Schließung: Externe Tür",
                ["UiFenceGateAutoClosingEnabled"] = "Automatische Schließung: Zauntor",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Automatische Schließung: Tür des alten Holzunterstands"
            }, this, "de");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "GESLOTEN",
                ["LockerStatus_Unlocked"] = "ONTGRENDELD",
                ["LockerStatus_Removed"] = "VERWIJDERD",
                ["LockerStatus_Updated"] = "BIJGEWERKT",
                ["UiEnabled"] = "INGESCHAKELD",
                ["UiDisabled"] = "UITGESCHAKELD",
                ["UiUpdate"] = "BIJWERKEN",
                ["UiRemove"] = "VERWIJDEREN",
                ["UiUpdateAll"] = "ALLE BIJWERKEN",
                ["UiRemoveAll"] = "ALLE VERWIJDEREN",
                ["UiButton_Yes"] = "JA",
                ["UiButton_No"] = "NEE",

                ["NoCodeLockAuth"] = "Het is geblokkeerd...",
                ["NoPermissions"] = "<color=red>Je hebt geen toestemming om deze opdracht te gebruiken.</color>",
                ["NoBypassPermissions"] = "<color=red>Je hebt geen toestemming om de code te omzeilen.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID is niet de eigenaar van het object.</color>",
                ["NoEntityOwner"] = "<color=red>Je bent niet de eigenaar van dit object.</color>",
                ["PlayerNotFound"] = "<color=red>De speler kon niet worden gevonden.</color>",
                ["InvalidSteamID"] = "<color=red>Ongeldige SteamID.</color>",
                ["CodeLockNotFound"] = "<color=red>CodeLock of Key Lock kon niet worden gevonden.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Code Lock heeft geen code.</color>",
                ["EntityNotAllowed"] = "<color=red>Niet toegestaan op dit object.</color>",
                ["LockCodeNotValid"] = "<color=red>Slotcode is niet geldig (Vereist 4 cijfers).</color>",
                ["MissingParametersError"] = "<color=red>Er is een fout opgetreden: Ontbrekende parameters - [{0}].</color>",
                ["GenericError"] = "<color=red>Er is een fout opgetreden: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Vertragingstijd niet geldig: Voer een waarde in tussen {0} en {1}.</color>",
                ["EntityNotFound"] = "<color=red>Je moet naar een object kijken.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Entiteit met skin is niet toegestaan.</color>",
                ["ToggleProximitySystemUI"] = "Nabijheidssysteem: UI weergeven",
                ["AlwaysShowUI"] = "Dit venster altijd weergeven",
                ["DisableShowUI"] = "Dit venster niet meer tonen",
                ["DoorCloserNotFound"] = "<color=red>Het object heeft geen Deursluiter.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>Je hebt geen toestemming of Deursluiter is niet ingeschakeld.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>Je hebt deursluiter niet ingeschakeld.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>Je hebt de Deursluiter niet in je inventaris.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Ongeldige categorie Deursluiter.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Er is al een Deursluiter geplaatst op dit object.</color>",
                ["LockCategoryNotValid"] = "<color=red>Ongeldige slotcategorie.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>Je hebt geen toestemming of automatisch slot is niet ingeschakeld.</color>",
                ["LockItemNotFound"] = "<color=red>Je hebt geen slot in je inventaris.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>Je hebt geen slot in je inventaris.\nJe kunt alleen sloten zonder code gebruiken</color>",
                ["ShowCodeLockCodeMessage"] = "Code Lock code is:\nCode: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Code Lock code is:\nCode: <color=#faa511>{0}</color>\nGastcode: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Streamermodus actief:</color> code kan niet worden bekeken",
                ["CodeLockPinChangedMessage"] = "Code Lock Pin gewijzigd.",
                ["AllCodeLockPinChangedMessage"] = "Alle Code Lock Pins zijn gewijzigd.",
                ["AllGuestCodeLockPinChangedMessage"] = "Alle Gast Code Lock Pins zijn gewijzigd.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Alle Gast Code Lock Pins zijn verwijderd.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Alle rechten voor het delen van Code Locks met je clan/team zijn gewijzigd.",
                ["AutoLockKeyLockDeployedMessage"] = "Sleutelslot geplaatst en vergrendeld.",
                ["AutoLockCodeLockDeployedMessage"] = "Code Lock geplaatst en vergrendeld\nCode: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] = "Code Lock geplaatst en vergrendeld\nCode: <color=#faa511>{0}</color>\nGastcode: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Pincode slot: <color=red>{0}</color>. Je kunt <color=red>de pincode wijzigen</color> met de Modulaire Autolift.",
                ["CodeLockListMessage"] = "Je hebt {0} {1} Code Locks / sleutelsloten.",
                ["CodeLockMessage"] = "Je hebt {0} een Code Lock / sleutelsloten.",
                ["LockClosedAutomaticallyMessage"] = "Het slot werd automatisch gesloten.",
                ["ToggleAutoLock"] = "Je hebt {0} automatische vergrendelingen (Slot)",
                ["SingleDoorCloserCustomTime"] = "Je hebt de tijd voor deze Deursluiter ingesteld op <color=#faa511>{0}s</color>.",
                ["ToggleAutoDoorCloser"] = "Je hebt automatisch sluiten {0}.",
                ["DoorCloserListMessage"] = "Je hebt {0} {1} Deursluiter.",
                ["DoorCloserDeployed"] = "Deursluiter geplaatst. Vertraagde sluitingstijd ingesteld: <color=#faa511>{0}s</color>",
                ["UIDlcSkinRequired"] = "DLC VEREIST",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Je hebt de gebruikte DLC/skin niet</color>",
                ["UiHeaderTitle"] = "Automatische slotinstellingen",
                ["UiShareLocksWithClanTeamEnabled"] = "Deel sloten met Clan/Team",
                ["UiAutoLockOnPlacementEnabled"] = "Automatisch slot",
                ["UiCodeLockPin"] = "Code Lock Pin",
                ["UiGuestCodeEnabled"] = "Gastcode slot",
                ["UiGuestCodeLockPin"] = "Gastcode slot Pin",
                ["UiAlsoUseKeyLockEnabled"] = "Ook Sleutelslot gebruiken",
                ["UiStreamerModeEnabled"] = "Streamermodus",
                ["UiCodelockSkinFlightControlCodelock"] = "Code Lock Skin: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Automatisch slot: Deuren",
                ["UiWindowsLockEnabled"] = "Automatisch slot: Ramen",
                ["UiBoxesLockEnabled"] = "Automatisch slot: Dozen",
                ["UiStorageContainerLockEnabled"] = "Automatisch slot: Alle Opslagcontainers",
                ["UiLockersLockEnabled"] = "Automatisch slot: Kluisjes",
                ["UiCupboardsLockEnabled"] = "Automatisch slot: Kasten (TC)",
                ["UiVehicleLockEnabled"] = "Automatisch slot: Voertuig",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Automatische slotvergrendeling bij afstappen van het voertuig",
                ["UiNavalDeepSeaLockEnabled"] = "Automatisch slot: Marine",
                ["UiMedievalEntityLockEnabled"] = "Automatisch slot: Middeleeuwse objecten",
                ["UiFurnaceLockEnabled"] = "Automatisch slot: Oven / Raffinaderijen",
                ["UiVendingMachineLockEnabled"] = "Automatisch slot: Verkoopautomaat",
                ["UiComposterLockEnabled"] = "Automatisch slot: Composter",
                ["UiMixingTableLockEnabled"] = "Automatisch slot: Mengtafel",
                ["UiPlanterLockEnabled"] = "Automatisch slot: Planter",
                ["UiAutoTurretLockEnabled"] = "Automatisch slot: Autoturret",
                ["UiSamSiteLockEnabled"] = "Automatisch slot: SAM-site",
                ["UiTrapsLockEnabled"] = "Automatisch slot: Vallen",
                ["UiWeaponRackLockEnabled"] = "Automatisch slot: Wapenrek",
                ["UiStashLockEnabled"] = "Automatisch slot: Verstopplaats",
                ["UiNeonSignLockEnabled"] = "Automatisch slot: Neonbord",
                ["UiSignDrawingLockEnabled"] = "Automatisch slot: tekenen/ondertekenen",
                ["UiOtherLockableEntitiesLockEnabled"] = "Automatisch slot: Andere vergrendelbare objecten",
                ["UiOtherCustomEntitiesLockEnabled"] = "Automatisch slot: Andere aangepaste objecten",
                ["UiOpenAutoClosingButtonText"] = "Automatische sluitingsinstellingen",
                ["UiAutoClosingHeaderTitle"] = "Automatische sluitingsinstellingen",
                ["UiAutoClosingEnabledTitle"] = "Automatische sluiting ingeschakeld",
                ["UiClosingDelay"] = "Sluitingsvertraging (s)",
                ["UiDoorAutoClosingEnabled"] = "Automatische sluiting: Deur",
                ["UiDoubleDoorAutoClosingEnabled"] = "Automatische sluiting: Dubbele deur",
                ["UiWindowsAutoClosingEnabled"] = "Automatische sluiting: Ramen",
                ["UiGarageAutoClosingEnabled"] = "Automatische sluiting: Garage",
                ["UiLadderHatchAutoClosingEnabled"] = "Automatische sluiting: Ladderluik",
                ["UiExternalGateAutoClosingEnabled"] = "Automatische sluiting: Externe poort",
                ["UiFenceGateAutoClosingEnabled"] = "Automatische sluiting: Hekpoort",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Automatische sluiting: Deur van houten schuilplaats uit het verleden"
            }, this, "nl");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "KİLİTLİ",
                ["LockerStatus_Unlocked"] = "KİLİTSİZ",
                ["LockerStatus_Removed"] = "KALDIRILMIŞ",
                ["LockerStatus_Updated"] = "GÜNCELLENDİ",
                ["UiEnabled"] = "ETKİN",
                ["UiDisabled"] = "DEVRE DIŞI",
                ["UiUpdate"] = "GÜNCELLE",
                ["UiRemove"] = "KALDIR",
                ["UiUpdateAll"] = "TÜMÜNÜ GÜNCELLE",
                ["UiRemoveAll"] = "TÜMÜNÜ KALDIR",
                ["UiButton_Yes"] = "EVET",
                ["UiButton_No"] = "HAYIR",

                ["NoCodeLockAuth"] = "Engellendi...",
                ["NoPermissions"] = "<color=red>Bu komutu kullanma izniniz yok.</color>",
                ["NoBypassPermissions"] = "<color=red>Kodu atlayacak izniniz yok.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID varlığın sahibi değil.</color>",
                ["NoEntityOwner"] = "<color=red>Bu varlığın sahibi değilsiniz.</color>",
                ["PlayerNotFound"] = "<color=red>Oyuncu bulunamadı.</color>",
                ["InvalidSteamID"] = "<color=red>Geçersiz SteamID.</color>",
                ["CodeLockNotFound"] = "<color=red>Kod Kilidi veya Anahtar Kilidi bulunamadı.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Kod Kilidinde kod bulunmuyor.</color>",
                ["EntityNotAllowed"] = "<color=red>Bu varlıkta izin verilmiyor.</color>",
                ["LockCodeNotValid"] = "<color=red>Kilit kodu geçerli değil (4 rakam gerektirir).</color>",
                ["MissingParametersError"] = "<color=red>Bir hata oluştu: Eksik parametreler - [{0}].</color>",
                ["GenericError"] = "<color=red>Bir hata oluştu: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Gecikme süresi geçerli değil: Lütfen {0} ve {1} arasında bir değer girin.</color>",
                ["EntityNotFound"] = "<color=red>Bir varlığa bakmalısınız.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Derisi olan varlıklara izin verilmez.</color>",
                ["ToggleProximitySystemUI"] = "Yakınlık sistemi: UI'yi göster",
                ["AlwaysShowUI"] = "Bu pencereyi her zaman göster",
                ["DisableShowUI"] = "Bu pencereyi tekrar gösterme",
                ["DoorCloserNotFound"] = "<color=red>Varlıkta Kapı Kapatıcı bulunamadı.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>İzin yok veya Kapı Kapatıcı etkin değil.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>Kapı Kapatıcı etkin değil.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>Envanterinizde Kapı Kapatıcı yok.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Geçersiz Kapı Kapatıcı kategorisi.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Bu varlıkta zaten bir Kapı Kapatıcı bulunuyor.</color>",
                ["LockCategoryNotValid"] = "<color=red>Geçersiz kilit kategorisi.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>İzin yok veya otomatik kilitleme etkin değil.</color>",
                ["LockItemNotFound"] = "<color=red>Envanterinizde kilit yok.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>Envanterinizde kilit yok.\nSadece kodsuz kilitleri kullanabilirsiniz</color>",
                ["ShowCodeLockCodeMessage"] = "Kod Kilidi kodu:\nKod: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Kod Kilidi kodu:\nKod: <color=#faa511>{0}</color>\nMisafir Kodu: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Yayıncı modu etkin:</color> kod görüntülenemez",
                ["CodeLockPinChangedMessage"] = "Kod Kilidi Pini değiştirildi.",
                ["AllCodeLockPinChangedMessage"] = "Tüm Kod Kilidi Pinleri değiştirildi.",
                ["AllGuestCodeLockPinChangedMessage"] = "Tüm Misafir Kod Kilidi Pinleri değiştirildi.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Tüm Misafir Kod Kilidi Pinleri kaldırıldı.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Klan/takımınızla Kod Kilidini paylaşma izinleri değiştirildi.",
                ["AutoLockKeyLockDeployedMessage"] = "Anahtar Kilidi yerleştirildi ve kilitlendi.",
                ["AutoLockCodeLockDeployedMessage"] = "Kod Kilidi yerleştirildi ve kilitlendi\nKod: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Kod Kilidi yerleştirildi ve kilitlendi\nKod: <color=#faa511>{0}</color>\nMisafir Kodu: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Pin Kod Kilidi: <color=red>{0}</color>. Modüler Araç Kaldırıcı ile <color=red>pini değiştirebilirsiniz</color>.",
                ["CodeLockListMessage"] = "Toplam {0} {1} Kod Kilidi / Anahtar Kilidi var.",
                ["CodeLockMessage"] = "Toplam {0} Kod Kilidi / Anahtar Kilidi var.",
                ["LockClosedAutomaticallyMessage"] = "Kilit otomatik olarak kapatıldı.",
                ["ToggleAutoLock"] = "{0} otomatik kilidiniz var (Kilit)",
                ["SingleDoorCloserCustomTime"] = "Bu Kapı Kapatıcı için zamanı <color=#faa511>{0}s</color> olarak ayarladınız.",
                ["ToggleAutoDoorCloser"] = "{0} otomatik kapatma hakkınız var.",
                ["DoorCloserListMessage"] = "Toplam {0} {1} Kapı Kapatıcı var.",
                ["DoorCloserDeployed"] = "Kapı kapatıcı yerleştirildi. Gecikmeli kapanma süresi ayarlandı: <color=#faa511>{0}s</color>",
                ["UIDlcSkinRequired"] = "DLC GEREKTİRİR",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Kullanılan DLC/Görüntü size ait değil</color>",
                ["UiHeaderTitle"] = "Otomatik Kilit Ayarları",
                ["UiShareLocksWithClanTeamEnabled"] = "Klan/Takım ile Kilidi Paylaş",
                ["UiAutoLockOnPlacementEnabled"] = "Otomatik kilit",
                ["UiCodeLockPin"] = "Kod Kilidi Pini",
                ["UiGuestCodeEnabled"] = "Misafir Kod Kilidi",
                ["UiGuestCodeLockPin"] = "Misafir Kod Kilidi Pini",
                ["UiAlsoUseKeyLockEnabled"] = "Ayrıca Anahtar Kilidi Kullan",
                ["UiStreamerModeEnabled"] = "Yayıncı Modu",
                ["UiCodelockSkinFlightControlCodelock"] = "Kod Kilidi Görünümü: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Otomatik Kilit: Kapılar",
                ["UiWindowsLockEnabled"] = "Otomatik Kilit: Pencereler",
                ["UiBoxesLockEnabled"] = "Otomatik Kilit: Kutular",
                ["UiStorageContainerLockEnabled"] = "Otomatik Kilit: Tüm Depolama Kapları",
                ["UiLockersLockEnabled"] = "Otomatik Kilit: Dolaplar",
                ["UiCupboardsLockEnabled"] = "Otomatik Kilit: Dolaplar (TC)",
                ["UiVehicleLockEnabled"] = "Otomatik Kilit: Araç",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Araçtan İndiğinizde Otomatik Kilit Kapanması",
                ["UiNavalDeepSeaLockEnabled"] = "Otomatik Kilit: Deniz Kuvvetleri",
                ["UiMedievalEntityLockEnabled"] = "Otomatik Kilit: Ortaçağ Varlıkları",
                ["UiFurnaceLockEnabled"] = "Otomatik Kilit: Ocaklar / Rafineriler",
                ["UiVendingMachineLockEnabled"] = "Otomatik Kilit: Satış Makinesi",
                ["UiComposterLockEnabled"] = "Otomatik Kilit: Kompostlama Makinesi",
                ["UiMixingTableLockEnabled"] = "Otomatik Kilit: Karıştırma Masası",
                ["UiPlanterLockEnabled"] = "Otomatik Kilit: Saksı",
                ["UiAutoTurretLockEnabled"] = "Otomatik Kilit: Otomatik Taret",
                ["UiSamSiteLockEnabled"] = "Otomatik Kilit: SAM Site",
                ["UiTrapsLockEnabled"] = "Otomatik Kilit: Tuzaklar",
                ["UiWeaponRackLockEnabled"] = "Otomatik Kilit: Silah Askısı",
                ["UiStashLockEnabled"] = "Otomatik Kilit: Saklama Yeri",
                ["UiNeonSignLockEnabled"] = "Otomatik Kilit: Neon Işığı",
                ["UiSignDrawingLockEnabled"] = "Otomatik Kilit: Çizim / İmza",
                ["UiOtherLockableEntitiesLockEnabled"] = "Otomatik Kilit: Diğer Kilitlenebilir Varlıklar",
                ["UiOtherCustomEntitiesLockEnabled"] = "Otomatik Kilit: Diğer Özel Varlıklar",
                ["UiOpenAutoClosingButtonText"] = "Otomatik Kapanma Ayarları",
                ["UiAutoClosingHeaderTitle"] = "Otomatik Kapanma Ayarları",
                ["UiAutoClosingEnabledTitle"] = "Otomatik Kapanma Etkin",
                ["UiClosingDelay"] = "Kapanma Gecikmesi (s)",
                ["UiDoorAutoClosingEnabled"] = "Otomatik Kapanma: Kapı",
                ["UiDoubleDoorAutoClosingEnabled"] = "Otomatik Kapanma: Çift Kapı",
                ["UiWindowsAutoClosingEnabled"] = "Otomatik Kapanma: Pencereler",
                ["UiGarageAutoClosingEnabled"] = "Otomatik Kapanma: Garaj",
                ["UiLadderHatchAutoClosingEnabled"] = "Otomatik Kapanma: Merdiven Kapısı",
                ["UiExternalGateAutoClosingEnabled"] = "Otomatik Kapanma: Harici Kapı",
                ["UiFenceGateAutoClosingEnabled"] = "Otomatik Kapanma: Çit Kapısı",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Otomatik Kapanma: Eski Ahşap Barınak Kapısı"
            }, this, "tr");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "ЗАБЛОКИРОВАН",
                ["LockerStatus_Unlocked"] = "РАЗБЛОКИРОВАН",
                ["LockerStatus_Removed"] = "УДАЛЕН",
                ["LockerStatus_Updated"] = "ОБНОВЛЕН",
                ["UiEnabled"] = "ВКЛЮЧЕНО",
                ["UiDisabled"] = "ОТКЛЮЧЕНО",
                ["UiUpdate"] = "ОБНОВИТЬ",
                ["UiRemove"] = "УДАЛИТЬ",
                ["UiUpdateAll"] = "ОБНОВИТЬ ВСЕ",
                ["UiRemoveAll"] = "УДАЛИТЬ ВСЕ",
                ["UiButton_Yes"] = "ДА",
                ["UiButton_No"] = "НЕТ",

                ["NoCodeLockAuth"] = "Он заблокирован...",
                ["NoPermissions"] = "<color=red>У вас нет разрешения на использование этой команды.</color>",
                ["NoBypassPermissions"] = "<color=red>У вас нет разрешения на обход кода.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID не является владельцем объекта.</color>",
                ["NoEntityOwner"] = "<color=red>Вы не являетесь владельцем этого объекта.</color>",
                ["PlayerNotFound"] = "<color=red>Игрок не может быть найден.</color>",
                ["InvalidSteamID"] = "<color=red>Недопустимый SteamID.</color>",
                ["CodeLockNotFound"] = "<color=red>Кодовый замок или ключевой замок не может быть найден.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Кодовый замок не имеет кода.</color>",
                ["EntityNotAllowed"] = "<color=red>Не разрешено для этого объекта.</color>",
                ["LockCodeNotValid"] = "<color=red>Код замка недействителен (Требуется 4 цифры).</color>",
                ["MissingParametersError"] = "<color=red>Произошла ошибка: Недостающие параметры - [{0}].</color>",
                ["GenericError"] = "<color=red>Произошла ошибка: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Недопустимое время задержки: Пожалуйста, введите значение между {0} и {1}.</color>",
                ["EntityNotFound"] = "<color=red>Вы должны посмотреть на объект.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Существа с кожей не допускаются.</color>",
                ["ToggleProximitySystemUI"] = "Система приближения: показывать интерфейс",
                ["AlwaysShowUI"] = "Всегда показывать это окно",
                ["DisableShowUI"] = "Больше не показывать это окно",
                ["DoorCloserNotFound"] = "<color=red>Объект не имеет дверной доводчик.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>У вас нет разрешения или дверной доводчик не включен.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>У вас не включен дверной доводчик.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>У вас нет дверного доводчика в вашем инвентаре.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Недопустимая категория дверного доводчика.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Дверной доводчик уже установлен на этом объекте.</color>",
                ["LockCategoryNotValid"] = "<color=red>Недопустимая категория замка.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>У вас нет разрешения или автозамок не включен.</color>",
                ["LockItemNotFound"] = "<color=red>У вас нет замка в вашем инвентаре.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>В вашем инвентаре нет замка.\nВы можете использовать только замки без кода</color>",
                ["ShowCodeLockCodeMessage"] = "Код замка:\nКод: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Код замка:\nКод: <color=#faa511>{0}</color>\nГостевой код: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Режим стримера активен:</color> код не может быть просмотрен",
                ["CodeLockPinChangedMessage"] = "Пин-код замка изменен.",
                ["AllCodeLockPinChangedMessage"] = "Все пин-коды замков изменены.",
                ["AllGuestCodeLockPinChangedMessage"] = "Все гостевые пин-коды замков изменены.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Все гостевые пин-коды замков удалены.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Все разрешения на обмен кодовыми замками с вашим кланом/командой изменены.",
                ["AutoLockKeyLockDeployedMessage"] = "Установлен и заблокирован ключевой замок.",
                ["AutoLockCodeLockDeployedMessage"] = "Установлен и заблокирован кодовый замок\nКод: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Установлен и заблокирован кодовый замок\nКод: <color=#faa511>{0}</color>\nГостевой код: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] =
                    "Пин-код замка: <color=red>{0}</color>. Вы можете <color=red>изменить пин</color> с помощью модульного автомобильного подъемника.",
                ["CodeLockListMessage"] = "У вас {0} {1} кодовых замков / ключевых замков.",
                ["CodeLockMessage"] = "У вас {0} кодовый замок / ключевые замки.",
                ["LockClosedAutomaticallyMessage"] = "Замок был закрыт автоматически.",
                ["ToggleAutoLock"] = "У вас {0} автоматическая блокировка (замок)",
                ["SingleDoorCloserCustomTime"] = "Вы установили время для этого дверного доводчика на <color=#faa511>{0}с</color>.",
                ["ToggleAutoDoorCloser"] = "Автозакрытие: {0}.",
                ["DoorCloserListMessage"] = "У вас {0} {1} дверных доводчиков.",
                ["DoorCloserDeployed"] = "Дверной доводчик установлен. Установлено время задержки закрытия: <color=#faa511>{0}с</color>",
                ["UIDlcSkinRequired"] = "ТРЕБУЕТСЯ DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Вы не являетесь владельцем используемого DLC/скина.</color>",
                ["UiHeaderTitle"] = "Настройки автозамка",
                ["UiShareLocksWithClanTeamEnabled"] = "Поделиться замками с кланом/командой",
                ["UiAutoLockOnPlacementEnabled"] = "Автозамок",
                ["UiCodeLockPin"] = "Пин-код замка",
                ["UiGuestCodeEnabled"] = "Гостевой код замка",
                ["UiGuestCodeLockPin"] = "Гостевой пин-код замка",
                ["UiAlsoUseKeyLockEnabled"] = "Также используйте ключевой замок",
                ["UiStreamerModeEnabled"] = "Режим стримера",
                ["UiCodelockSkinFlightControlCodelock"] = "Скин кодового замка: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Автозамок: Двери",
                ["UiWindowsLockEnabled"] = "Автозамок: Окна",
                ["UiBoxesLockEnabled"] = "Автозамок: Ящики",
                ["UiStorageContainerLockEnabled"] = "Автозамок: Все контейнеры для хранения",
                ["UiLockersLockEnabled"] = "Автозамок: Шкафы",
                ["UiCupboardsLockEnabled"] = "Автозамок: Шкафы (ТС)",
                ["UiVehicleLockEnabled"] = "Автозамок: Транспортное средство",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Автоматическое закрытие замка при спуске с транспортного средства",
                ["UiNavalDeepSeaLockEnabled"] = "Автозамок: Морской",
                ["UiMedievalEntityLockEnabled"] = "Автозамок: Средневековые объекты",
                ["UiFurnaceLockEnabled"] = "Автозамок: Печь / Рефайнеры",
                ["UiVendingMachineLockEnabled"] = "Автозамок: Торговый автомат",
                ["UiComposterLockEnabled"] = "Автозамок: Компостер",
                ["UiMixingTableLockEnabled"] = "Автозамок: Смесительный стол",
                ["UiPlanterLockEnabled"] = "Автозамок: Горшок",
                ["UiAutoTurretLockEnabled"] = "Автозамок: Авто турель",
                ["UiSamSiteLockEnabled"] = "Автозамок: Станция ПВО",
                ["UiTrapsLockEnabled"] = "Автозамок: Ловушки",
                ["UiWeaponRackLockEnabled"] = "Автозамок: Стойка для оружия",
                ["UiStashLockEnabled"] = "Автозамок: Укрытие",
                ["UiNeonSignLockEnabled"] = "Автозамок: Неоновый знак",
                ["UiSignDrawingLockEnabled"] = "Автозамок: Рисунок / Подпись",
                ["UiOtherLockableEntitiesLockEnabled"] = "Автозамок: Другие объекты с замками",
                ["UiOtherCustomEntitiesLockEnabled"] = "Автозамок: Другие пользовательские объекты",
                ["UiOpenAutoClosingButtonText"] = "Настройки автоматического закрытия",
                ["UiAutoClosingHeaderTitle"] = "Настройки автоматического закрытия",
                ["UiAutoClosingEnabledTitle"] = "Автоматическое закрытие включено",
                ["UiClosingDelay"] = "Задержка закрытия (с)",
                ["UiDoorAutoClosingEnabled"] = "Автоматическое закрытие: Дверь",
                ["UiDoubleDoorAutoClosingEnabled"] = "Автоматическое закрытие: Двойная дверь",
                ["UiWindowsAutoClosingEnabled"] = "Автоматическое закрытие: Окна",
                ["UiGarageAutoClosingEnabled"] = "Автоматическое закрытие: Гараж",
                ["UiLadderHatchAutoClosingEnabled"] = "Автоматическое закрытие: Лестничная люк",
                ["UiExternalGateAutoClosingEnabled"] = "Автоматическое закрытие: Внешние ворота",
                ["UiFenceGateAutoClosingEnabled"] = "Автоматическое закрытие: Ворота из забора",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Автоматическое закрытие: Дверь старого деревянного укрытия"
            }, this, "ru");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "ЗАБЛОКОВАНО",
                ["LockerStatus_Unlocked"] = "РОЗБЛОКОВАНО",
                ["LockerStatus_Removed"] = "ВИДАЛЕНО",
                ["LockerStatus_Updated"] = "ОНОВЛЕНО",
                ["UiEnabled"] = "УВІМКНЕНО",
                ["UiDisabled"] = "ВИМКНЕНО",
                ["UiUpdate"] = "ОНОВИТИ",
                ["UiRemove"] = "ВИДАЛИТИ",
                ["UiUpdateAll"] = "ОНОВИТИ ВСЕ",
                ["UiRemoveAll"] = "ВИДАЛИТИ ВСЕ",
                ["UiButton_Yes"] = "ТАК",
                ["UiButton_No"] = "НІ",

                ["NoCodeLockAuth"] = "Це заблоковано...",
                ["NoPermissions"] = "<color=red>У вас немає дозволу використовувати цю команду.</color>",
                ["NoBypassPermissions"] = "<color=red>У вас немає дозволу обійти код.</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID не є власником об'єкту.</color>",
                ["NoEntityOwner"] = "<color=red>Ви не володієте цим об'єктом.</color>",
                ["PlayerNotFound"] = "<color=red>Гравця не вдалося знайти.</color>",
                ["InvalidSteamID"] = "<color=red>Недійсний SteamID.</color>",
                ["CodeLockNotFound"] = "<color=red>Кодовий замок або ключовий замок не вдалося знайти.</color>",
                ["CodeLockCodeNotFound"] = "<color=red>Кодовий замок не має коду.</color>",
                ["EntityNotAllowed"] = "<color=red>Не дозволяється на цьому об'єкті.</color>",
                ["LockCodeNotValid"] = "<color=red>Код замка недійсний (потрібно 4 цифри).</color>",
                ["MissingParametersError"] = "<color=red>Виникла помилка: Відсутні параметри - [{0}].</color>",
                ["GenericError"] = "<color=red>Виникла помилка: [{0}].</color>",
                ["DelayTimeNotValid"] = "<color=red>Недійсний час затримки: Будь ласка, введіть значення між {0} та {1}.</color>",
                ["EntityNotFound"] = "<color=red>Вам потрібно подивитися на об'єкт.</color>",
                ["EntitySkinBlacklisted"] = "<color=red>Сутність зі шкірою не дозволена.</color>",
                ["ToggleProximitySystemUI"] = "Система наближення: показувати UI",
                ["AlwaysShowUI"] = "Завжди показувати це вікно",
                ["DisableShowUI"] = "Більше не показувати це вікно",
                ["DoorCloserNotFound"] = "<color=red>У об'єкта немає дверного захисника.</color>",
                ["AutoClosingNotEnabled"] = "<color=red>У вас немає дозволу або дверний захисник не увімкнено.</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>У вас немає дозволу на дверний захисник.</color>",
                ["DoorCloserItemNotFound"] = "<color=red>У вас немає дверного захисника в інвентарі.</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>Недійсна категорія дверного захисника.</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>Дверний захисник вже встановлено на цьому об'єкті.</color>",
                ["LockCategoryNotValid"] = "<color=red>Недійсна категорія замка.</color>",
                ["AutoLockerNotEnabled"] = "<color=red>У вас немає дозволу або автоматичне блокування не увімкнено.</color>",
                ["LockItemNotFound"] = "<color=red>У вас немає замка в інвентарі.</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>У вашому інвентарі немає замка.\nВи можете використовувати замки лише без коду</color>",
                ["ShowCodeLockCodeMessage"] = "Код замка: \nКод: <color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "Код замка: \nКод: <color=#faa511>{0}</color>\nГостьовий код: <color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>Активований режим стрімера:</color> код не може бути переглянутий",
                ["CodeLockPinChangedMessage"] = "Змінено код PIN замка.",
                ["AllCodeLockPinChangedMessage"] = "Всі коди PIN замків було змінено.",
                ["AllGuestCodeLockPinChangedMessage"] = "Всі гостьові коди PIN замків було змінено.",
                ["AllGuestCodeLockPinRemovedMessage"] = "Всі гостьові коди PIN замків було видалено.",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "Всі дозволи для спільного використання замків були змінені.",
                ["AutoLockKeyLockDeployedMessage"] = "Ключовий замок встановлено і заблоковано.",
                ["AutoLockCodeLockDeployedMessage"] = "Кодовий замок встановлено і заблоковано\nКод: <color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] =
                    "Кодовий замок встановлено і заблоковано\nКод: <color=#faa511>{0}</color>\nГостьовий код: <color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] =
                    "Кодовий замок: <color=red>{0}</color>. Ви можете <color=red>змінити код</color> з допомогою модульного автомобільного підйомника.",
                ["CodeLockListMessage"] = "У вас є {0} {1} кодових замків / ключових замків.",
                ["CodeLockMessage"] = "У вас є {0} кодовий замок / ключові замки.",
                ["LockClosedAutomaticallyMessage"] = "Замок автоматично закрито.",
                ["ToggleAutoLock"] = "У вас є {0} автоблокування (замок)",
                ["SingleDoorCloserCustomTime"] = "Ви встановили час для цього дверного захисника на <color=#faa511>{0}с</color>.",
                ["ToggleAutoDoorCloser"] = "У вас {0} автозакриття.",
                ["DoorCloserListMessage"] = "У вас є {0} {1} дверних захисників.",
                ["DoorCloserDeployed"] = "Дверний захисник встановлено. Встановлено затримку закриття: <color=#faa511>{0}с</color>",
                ["UIDlcSkinRequired"] = "ПОТРІБЕН DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>Ви не володієте DLC/скіном, що використовується</color>",
                ["UiHeaderTitle"] = "Налаштування автоблокування",
                ["UiShareLocksWithClanTeamEnabled"] = "Поділитися замками з кланом/командою",
                ["UiAutoLockOnPlacementEnabled"] = "Автоматичне блокування",
                ["UiCodeLockPin"] = "PIN-код кодового замка",
                ["UiGuestCodeEnabled"] = "Гостьовий кодовий замок",
                ["UiGuestCodeLockPin"] = "PIN-код гостьового кодового замка",
                ["UiAlsoUseKeyLockEnabled"] = "Також використовувати ключовий замок",
                ["UiStreamerModeEnabled"] = "Режим стрімера",
                ["UiCodelockSkinFlightControlCodelock"] = "Скін кодового замка: Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "Автоблокування: Двері",
                ["UiWindowsLockEnabled"] = "Автоблокування: Вікна",
                ["UiBoxesLockEnabled"] = "Автоблокування: Ящики",
                ["UiStorageContainerLockEnabled"] = "Автоблокування: Всі контейнери для зберігання",
                ["UiLockersLockEnabled"] = "Автоблокування: Шафи",
                ["UiCupboardsLockEnabled"] = "Автоблокування: Шафа (TC)",
                ["UiVehicleLockEnabled"] = "Автоблокування: Транспортний засіб",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "Автоматичне закриття блокування при зліті з транспортного засобу",
                ["UiNavalDeepSeaLockEnabled"] = "Автоблокування: Військово-морський",
                ["UiMedievalEntityLockEnabled"] = "Автоблокування: Середньовічні об'єкти",
                ["UiFurnaceLockEnabled"] = "Автоблокування: Піч / Рафінерії",
                ["UiVendingMachineLockEnabled"] = "Автоблокування: Торговий автомат",
                ["UiComposterLockEnabled"] = "Автоблокування: Компостер",
                ["UiMixingTableLockEnabled"] = "Автоблокування: Стіл для змішування",
                ["UiPlanterLockEnabled"] = "Автоблокування: Горщик",
                ["UiAutoTurretLockEnabled"] = "Автоблокування: Авто турель",
                ["UiSamSiteLockEnabled"] = "Автоблокування: SAM-станція",
                ["UiTrapsLockEnabled"] = "Автоблокування: Пастки",
                ["UiWeaponRackLockEnabled"] = "Автоблокування: Стійка для зброї",
                ["UiStashLockEnabled"] = "Автоблокування: Сховище",
                ["UiNeonSignLockEnabled"] = "Автоблокування: Неоновий знак",
                ["UiSignDrawingLockEnabled"] = "Автоблокування: Малювання / Підпис",
                ["UiOtherLockableEntitiesLockEnabled"] = "Автоблокування: Інші об'єкти, які можна заблокувати",
                ["UiOtherCustomEntitiesLockEnabled"] = "Автоблокування: Інші користувацькі об'єкти",
                ["UiOpenAutoClosingButtonText"] = "Налаштування автоматичного закриття",
                ["UiAutoClosingHeaderTitle"] = "Налаштування автоматичного закриття",
                ["UiAutoClosingEnabledTitle"] = "Автоматичне закриття увімкнено",
                ["UiClosingDelay"] = "Затримка закриття (с)",
                ["UiDoorAutoClosingEnabled"] = "Автозакривання: Двері",
                ["UiDoubleDoorAutoClosingEnabled"] = "Автозакривання: Подвійні двері",
                ["UiWindowsAutoClosingEnabled"] = "Автозакривання: Вікна",
                ["UiGarageAutoClosingEnabled"] = "Автозакривання: Гараж",
                ["UiLadderHatchAutoClosingEnabled"] = "Автозакривання: Драбина люка",
                ["UiExternalGateAutoClosingEnabled"] = "Автозакривання: Зовнішні ворота",
                ["UiFenceGateAutoClosingEnabled"] = "Автозакривання: Ворота огорожі",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "Автозакривання: Двері легасі дерев'яної будки"
            }, this, "uk");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "已锁定",
                ["LockerStatus_Unlocked"] = "未锁定",
                ["LockerStatus_Removed"] = "已移除",
                ["LockerStatus_Updated"] = "已更新",
                ["UiEnabled"] = "已启用",
                ["UiDisabled"] = "已禁用",
                ["UiUpdate"] = "更新",
                ["UiRemove"] = "移除",
                ["UiUpdateAll"] = "全部更新",
                ["UiRemoveAll"] = "全部移除",
                ["UiButton_Yes"] = "是",
                ["UiButton_No"] = "否",

                ["NoCodeLockAuth"] = "已阻止...",
                ["NoPermissions"] = "<color=red>您没有使用此命令的权限。</color>",
                ["NoBypassPermissions"] = "<color=red>您没有绕过密码的权限。</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID不是实体的所有者。</color>",
                ["NoEntityOwner"] = "<color=red>您不拥有此实体。</color>",
                ["PlayerNotFound"] = "<color=red>找不到玩家。</color>",
                ["InvalidSteamID"] = "<color=red>无效的SteamID。</color>",
                ["CodeLockNotFound"] = "<color=red>找不到密码锁或钥匙锁。</color>",
                ["CodeLockCodeNotFound"] = "<color=red>密码锁没有密码。</color>",
                ["EntityNotAllowed"] = "<color=red>不允许在此实体上执行此操作。</color>",
                ["LockCodeNotValid"] = "<color=red>锁定代码无效（需要4位数字）。</color>",
                ["MissingParametersError"] = "<color=red>发生错误：缺少参数 - [{0}]。</color>",
                ["GenericError"] = "<color=red>发生错误：[{0}]。</color>",
                ["DelayTimeNotValid"] = "<color=red>延迟时间无效：请输入{0}和{1}之间的值。</color>",
                ["EntityNotFound"] = "<color=red>您必须查看一个实体。</color>",
                ["EntitySkinBlacklisted"] = "<color=red>不允许拥有皮肤的实体。</color>",
                ["ToggleProximitySystemUI"] = "邻近系统：显示界面",
                ["AlwaysShowUI"] = "始终显示此窗口",
                ["DisableShowUI"] = "不再显示此窗口",
                ["DoorCloserNotFound"] = "<color=red>实体没有门闭器。</color>",
                ["AutoClosingNotEnabled"] = "<color=red>您没有权限或门闭器未启用。</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>您未启用门闭器。</color>",
                ["DoorCloserItemNotFound"] = "<color=red>您的库存中没有门闭器。</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>无效的门闭器类别。</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>此实体上已放置了门闭器。</color>",
                ["LockCategoryNotValid"] = "<color=red>无效的锁定类别。</color>",
                ["AutoLockerNotEnabled"] = "<color=red>您没有权限或自动锁定未启用。</color>",
                ["LockItemNotFound"] = "<color=red>您的库存中没有锁。</color>",
                ["UseOnlyKeyLock_KeyLockNotFound"] = "<color=red>您的库存中没有锁。\n您只能使用没有密码的锁</color>",
                ["ShowCodeLockCodeMessage"] = "密码锁代码是：\n代码：<color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "密码锁代码是：\n代码：<color=#faa511>{0}</color>\n访客代码：<color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>流媒体模式激活：</color>无法查看代码",
                ["CodeLockPinChangedMessage"] = "密码锁代码已更改。",
                ["AllCodeLockPinChangedMessage"] = "所有密码锁代码已更改。",
                ["AllGuestCodeLockPinChangedMessage"] = "所有访客密码锁代码已更改。",
                ["AllGuestCodeLockPinRemovedMessage"] = "所有访客密码锁代码已移除。",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "与您的氏族/团队共享密码锁的所有权限已更改。",
                ["AutoLockKeyLockDeployedMessage"] = "钥匙锁已部署并锁定。",
                ["AutoLockCodeLockDeployedMessage"] = "密码锁已部署并锁定\n代码：<color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] = "密码锁已部署并锁定\n代码：<color=#faa511>{0}</color>\n访客代码：<color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "Pin密码锁：<color=red>{0}</color>。您可以使用模块化汽车升降机<color=red>更改密码</color>。",
                ["CodeLockListMessage"] = "您有{0}个{1}密码锁/钥匙锁。",
                ["CodeLockMessage"] = "您有{0}个密码锁/钥匙锁。",
                ["LockClosedAutomaticallyMessage"] = "锁已自动关闭。",
                ["ToggleAutoLock"] = "你有 {0} 个自动上锁（锁）",
                ["SingleDoorCloserCustomTime"] = "您已将此门闭器的时间设置为<color=#faa511>{0}秒</color>。",
                ["ToggleAutoDoorCloser"] = "你有 {0} 个自动关闭。",
                ["DoorCloserListMessage"] = "您有{0}个{1}门闭器。",
                ["DoorCloserDeployed"] = "门闭器已部署。延迟关闭时间设置为：<color=#faa511>{0}秒</color>",
                ["UIDlcSkinRequired"] = "需要 DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>您不拥有所使用的 DLC/皮肤</color>",
                ["UiHeaderTitle"] = "自动锁定设置",
                ["UiShareLocksWithClanTeamEnabled"] = "与氏族/团队共享锁定",
                ["UiAutoLockOnPlacementEnabled"] = "自动锁定",
                ["UiCodeLockPin"] = "密码锁代码",
                ["UiGuestCodeEnabled"] = "访客密码锁",
                ["UiGuestCodeLockPin"] = "访客密码锁代码",
                ["UiAlsoUseKeyLockEnabled"] = "同时使用钥匙锁",
                ["UiStreamerModeEnabled"] = "流媒体模式",
                ["UiCodelockSkinFlightControlCodelock"] = "代码锁皮肤：Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "自动锁定：门",
                ["UiWindowsLockEnabled"] = "自动锁定：窗户",
                ["UiBoxesLockEnabled"] = "自动锁定：箱子",
                ["UiStorageContainerLockEnabled"] = "自动锁定：所有储物容器",
                ["UiLockersLockEnabled"] = "自动锁定：储物柜",
                ["UiCupboardsLockEnabled"] = "自动锁定：碗柜（TC）",
                ["UiVehicleLockEnabled"] = "自动锁定：车辆",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "下车时自动锁定关闭",
                ["UiNavalDeepSeaLockEnabled"] = "自动锁定：海军",
                ["UiMedievalEntityLockEnabled"] = "自动锁定：中世纪实体",
                ["UiFurnaceLockEnabled"] = "自动锁定：熔炉/精炼炉",
                ["UiVendingMachineLockEnabled"] = "自动锁定：自动贩卖机",
                ["UiComposterLockEnabled"] = "自动锁定：堆肥桶",
                ["UiMixingTableLockEnabled"] = "自动锁定：混合桌",
                ["UiPlanterLockEnabled"] = "自动锁定：种植者",
                ["UiAutoTurretLockEnabled"] = "自动锁定：自动炮塔",
                ["UiSamSiteLockEnabled"] = "自动锁定：SAM站点",
                ["UiTrapsLockEnabled"] = "自动锁定：陷阱",
                ["UiWeaponRackLockEnabled"] = "自动锁定：武器架",
                ["UiStashLockEnabled"] = "自动锁定：藏身处",
                ["UiNeonSignLockEnabled"] = "自动锁定：霓虹灯牌",
                ["UiSignDrawingLockEnabled"] = "自动锁定：绘图/符号",
                ["UiOtherLockableEntitiesLockEnabled"] = "自动锁定：其他可锁定实体",
                ["UiOtherCustomEntitiesLockEnabled"] = "自动锁定：其他自定义实体",
                ["UiOpenAutoClosingButtonText"] = "自动关闭设置",
                ["UiAutoClosingHeaderTitle"] = "自动关闭设置",
                ["UiAutoClosingEnabledTitle"] = "启用自动关闭",
                ["UiClosingDelay"] = "关闭延迟（秒）",
                ["UiDoorAutoClosingEnabled"] = "自动关闭：门",
                ["UiDoubleDoorAutoClosingEnabled"] = "自动关闭：双门",
                ["UiWindowsAutoClosingEnabled"] = "自动关闭：窗户",
                ["UiGarageAutoClosingEnabled"] = "自动关闭：车库",
                ["UiLadderHatchAutoClosingEnabled"] = "自动关闭：梯子舱口",
                ["UiExternalGateAutoClosingEnabled"] = "自动关闭：外部门",
                ["UiFenceGateAutoClosingEnabled"] = "自动关闭：围栏门",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "自动关闭：传统木制庇护所门"
            }, this, "zh-CN"); //Cinese semplificato

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LockerStatus_Locked"] = "已鎖定",
                ["LockerStatus_Unlocked"] = "未鎖定",
                ["LockerStatus_Removed"] = "已移除",
                ["LockerStatus_Updated"] = "已更新",
                ["UiEnabled"] = "已啟用",
                ["UiDisabled"] = "已停用",
                ["UiUpdate"] = "更新",
                ["UiRemove"] = "移除",
                ["UiUpdateAll"] = "全部更新",
                ["UiRemoveAll"] = "全部移除",
                ["UiButton_Yes"] = "是",
                ["UiButton_No"] = "否",

                ["NoCodeLockAuth"] = "已阻止...",
                ["NoPermissions"] = "<color=red>您沒有使用此指令的權限。</color>",
                ["NoBypassPermissions"] = "<color=red>您沒有繞過密碼的權限。</color>",
                ["NoSteamIdOwner"] = "<color=red>SteamID 並非此實體的擁有者。</color>",
                ["NoEntityOwner"] = "<color=red>您不擁有此實體。</color>",
                ["PlayerNotFound"] = "<color=red>找不到玩家。</color>",
                ["InvalidSteamID"] = "<color=red>無效的 SteamID。</color>",
                ["CodeLockNotFound"] = "<color=red>找不到密碼鎖或鑰匙鎖。</color>",
                ["CodeLockCodeNotFound"] = "<color=red>密碼鎖沒有設定密碼。</color>",
                ["EntityNotAllowed"] = "<color=red>不允許在此實體上執行該操作。</color>",
                ["LockCodeNotValid"] = "<color=red>無效的鎖定密碼（需要 4 位數字）。</color>",
                ["MissingParametersError"] = "<color=red>發生錯誤：缺少參數 - [{0}]。</color>",
                ["GenericError"] = "<color=red>發生錯誤：[{0}]。</color>",
                ["DelayTimeNotValid"] = "<color=red>無效的延遲時間：請輸入 {0} 至 {1} 之間的數值。</color>",
                ["EntityNotFound"] = "<color=red>您必須查看一個實體。</color>",
                ["EntitySkinBlacklisted"] = "<color=red>不允許擁有皮膚的實體。</color>",
                ["ToggleProximitySystemUI"] = "距離系統：顯示 UI",
                ["AlwaysShowUI"] = "永遠顯示此視窗",
                ["DisableShowUI"] = "不要再顯示此視窗",
                ["DoorCloserNotFound"] = "<color=red>此實體沒有門閉器。</color>",
                ["AutoClosingNotEnabled"] = "<color=red>您沒有權限或門閉器未啟用。</color>",
                ["PlayerAutoClosingNotEnabled"] = "<color=red>您尚未啟用門閉器。</color>",
                ["DoorCloserItemNotFound"] = "<color=red>您的庫存中沒有門閉器。</color>",
                ["AutoClosingTypeNotValid"] = "<color=red>無效的門閉器類型。</color>",
                ["DoorCloserAlreadyExists"] = "<color=red>此實體上已經放置了門閉器。</color>",
                ["LockCategoryNotValid"] = "<color=red>無效的鎖定類別。</color>",
                ["AutoLockerNotEnabled"] = "<color=red>您沒有權限或自動鎖定未啟用。</color>",
                ["LockItemNotFound"] = "<color=red>您的庫存中沒有鎖。</color>",
                ["ShowCodeLockCodeMessage"] = "密碼鎖的密碼是：\n密碼：<color=#faa511>{0}</color>",
                ["ShowCodeLockAndGuestCodeMessage"] = "密碼鎖的密碼是：\n密碼：<color=#faa511>{0}</color>\n訪客密碼：<color=#faa511>{1}</color>",
                ["ShowCodeLockPinStreamerModeMessage"] = "<color=#faa511>串流模式已啟用：</color>無法查看密碼",
                ["CodeLockPinChangedMessage"] = "密碼鎖密碼已更改。",
                ["AllCodeLockPinChangedMessage"] = "所有密碼鎖的密碼已更改。",
                ["AllGuestCodeLockPinChangedMessage"] = "所有訪客密碼鎖的密碼已更改。",
                ["AllGuestCodeLockPinRemovedMessage"] = "所有訪客密碼鎖的密碼已移除。",
                ["AllCodeLockSharedAuthPinChangedMessage"] = "與您的公會/隊伍共享的所有密碼鎖權限已更改。",
                ["AutoLockKeyLockDeployedMessage"] = "鑰匙鎖已部署並鎖定。",
                ["AutoLockCodeLockDeployedMessage"] = "密碼鎖已部署並鎖定\n密碼：<color=#faa511>{0}</color>",
                ["AutoLockCodeLockAndGuestCodeDeployedMessage"] = "密碼鎖已部署並鎖定\n密碼：<color=#faa511>{0}</color>\n訪客密碼：<color=#faa511>{1}</color>",
                ["ModularCarNewCodeLockMessage"] = "密碼鎖 PIN 碼：<color=red>{0}</color>。您可以使用模組化汽車升降機<color=red>更改密碼</color>。",
                ["CodeLockListMessage"] = "您擁有 {0} 個 {1} 密碼鎖/鑰匙鎖。",
                ["CodeLockMessage"] = "您擁有 {0} 個密碼鎖/鑰匙鎖。",
                ["LockClosedAutomaticallyMessage"] = "鎖已自動關閉。",
                ["ToggleAutoLock"] = "你有 {0} 個自動上鎖（鎖）",
                ["SingleDoorCloserCustomTime"] = "您已將此門閉器的時間設置為 <color=#faa511>{0} 秒</color>。",
                ["ToggleAutoDoorCloser"] = "你有 {0} 個自動關閉。",
                ["DoorCloserListMessage"] = "您擁有 {0} 個 {1} 門閉器。",
                ["DoorCloserDeployed"] = "門閉器已部署，延遲關閉時間設置為：<color=#faa511>{0} 秒</color>",
                ["UIDlcSkinRequired"] = "需要 DLC",
                ["DlcSkinRequiredErrorMessage"] = "<color=red>您不擁有所使用的 DLC/皮膚</color>",
                ["UiHeaderTitle"] = "自動鎖定設置",
                ["UiShareLocksWithClanTeamEnabled"] = "與公會/隊伍共享鎖定",
                ["UiAutoLockOnPlacementEnabled"] = "自動鎖定",
                ["UiCodeLockPin"] = "密碼鎖密碼",
                ["UiGuestCodeEnabled"] = "訪客密碼鎖",
                ["UiGuestCodeLockPin"] = "訪客密碼鎖密碼",
                ["UiAlsoUseKeyLockEnabled"] = "同時使用鑰匙鎖",
                ["UiStreamerModeEnabled"] = "串流模式",
                ["UiCodelockSkinFlightControlCodelock"] = "代碼鎖皮膚：Flight Control Codelock",
                ["UiDoorsLockEnabled"] = "自動鎖定：門",
                ["UiWindowsLockEnabled"] = "自動鎖定：窗戶",
                ["UiBoxesLockEnabled"] = "自動鎖定：箱子",
                ["UiStorageContainerLockEnabled"] = "自動鎖定：所有儲物容器",
                ["UiLockersLockEnabled"] = "自動鎖定：儲物櫃",
                ["UiCupboardsLockEnabled"] = "自動鎖定：工具櫃（TC）",
                ["UiVehicleLockEnabled"] = "自動鎖定：載具",
                ["UiVehicleLockAutoClosingOnDismountEnabled"] = "下車時自動鎖定",
                ["UiNavalDeepSeaLockEnabled"] = "自動鎖定：海軍",
                ["UiMedievalEntityLockEnabled"] = "自動鎖定：中世紀實體",
                ["UiFurnaceLockEnabled"] = "自動鎖定：熔爐/精煉爐",
                ["UiVendingMachineLockEnabled"] = "自動鎖定：自動販賣機",
                ["UiComposterLockEnabled"] = "自動鎖定：堆肥桶",
                ["UiMixingTableLockEnabled"] = "自動鎖定：混合桌",
                ["UiPlanterLockEnabled"] = "自動鎖定：種植槽",
                ["UiAutoTurretLockEnabled"] = "自動鎖定：自動砲塔",
                ["UiSamSiteLockEnabled"] = "自動鎖定：防空砲塔（SAM 站）",
                ["UiTrapsLockEnabled"] = "自動鎖定：陷阱",
                ["UiWeaponRackLockEnabled"] = "自動鎖定：武器架",
                ["UiStashLockEnabled"] = "自動鎖定：藏匿處",
                ["UiNeonSignLockEnabled"] = "自動鎖定：霓虹燈牌",
                ["UiSignDrawingLockEnabled"] = "自動鎖定：繪圖/符號",
                ["UiOtherLockableEntitiesLockEnabled"] = "自動鎖定：其他可鎖定實體",
                ["UiOtherCustomEntitiesLockEnabled"] = "自動鎖定：其他自訂實體",
                ["UiOpenAutoClosingButtonText"] = "自動關閉設置",
                ["UiAutoClosingHeaderTitle"] = "自動關閉設置",
                ["UiAutoClosingEnabledTitle"] = "啟用自動關閉",
                ["UiClosingDelay"] = "關閉延遲（秒）",
                ["UiDoorAutoClosingEnabled"] = "自動關閉：門",
                ["UiDoubleDoorAutoClosingEnabled"] = "自動關閉：雙門",
                ["UiWindowsAutoClosingEnabled"] = "自動關閉：窗戶",
                ["UiGarageAutoClosingEnabled"] = "自動關閉：車庫門",
                ["UiLadderHatchAutoClosingEnabled"] = "自動關閉：梯子艙口",
                ["UiExternalGateAutoClosingEnabled"] = "自動關閉：外圍大門",
                ["UiFenceGateAutoClosingEnabled"] = "自動關閉：圍欄門",
                ["UiLegacyWoodShelterDoorAutoClosingEnabled"] = "自動關閉：傳統木製庇護所門"
            }, this, "zh-TW"); //Cinese tradizionale
        }

        #endregion
    }
}  