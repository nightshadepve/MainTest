using Facepunch;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.ArmoredTrainExtensionMethods;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using Time = UnityEngine.Time;
using static TrainEngine;
using static ProtoBuf.PatternFirework;
using static BaseCombatEntity;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("ArmoredTrain", "Adem", "2.0.5")]
    public class ArmoredTrain : RustPlugin
    {
        #region Variables
        private const bool En = true;
        private static ArmoredTrain _ins;
        private EventController _eventController;
        private ProtectionProperties _protection;
        [PluginReference] private Plugin NpcSpawn, PveMode, GUIAnnouncements, Notify, DiscordMessages, DynamicPVP, Economics, ServerRewards, IQEconomic, TrainHomes, AlphaLoot, CustomLoot, Loottable;

        private readonly HashSet<string> _subscribeMethods = new HashSet<string>
        {
            "OnEntitySpawned",
            "OnEntityTakeDamage",   
            "OnEntityDeath",
            "OnEntityEnter",
            "CanHelicopterTarget",
            "OnCustomNpcTarget",
            "CanBradleyApcTarget",
            "OnTrainCarUncouple",
            "CanTrainCarCouple",
            "CanPickupEntity",
            "CanMountEntity",
            "OnSwitchToggle",
            "OnSwitchToggled",
            "OnCounterModeToggle",
            "OnCounterTargetChange",
            "OnPlayerSleep",
            "CanLootEntity",
            "OnSamSiteModeToggle",
            "OnTurretAuthorize",
            "OnTurretTarget",
            "OnInterferenceUpdate",
            "CanHackCrate",
            "OnLootEntity",
            "OnLootEntityEnd",
            "OnCorpsePopulate",
            "OnCrateSpawned",
            "OnContainerPopulate",
            "CanPopulateLoot",
            "OnCustomLootContainer",
            "OnCustomLootNPC",

            "CanEntityBeTargeted",
            "CanEntityTakeDamage",
            "OnCreateDynamicPVP",
            "SetOwnerPveMode",
            "ClearOwnerPveMode",
            "CanBradleySpawnNpc",
            "CanHelicopterSpawnNpc"
        };
        private EventHeli _eventHeli;
        private readonly Dictionary<ulong, PlayerDamageController> _playerDamageControllers = new Dictionary<ulong, PlayerDamageController>();
        
        private Coroutine _autoEventCoroutine;
        private Coroutine _delayedEventStartCoroutine;
        #endregion Variablesa

        #region ExternalAPI
        private bool IsArmoredTrainActive()
        {
            return EventLauncher.IsEventActive();
        }

        private bool StopArmoredTrain()
        {
            if (!EventLauncher.IsEventActive())
                return false;

            //eventController.StopMoving();
            return true;
        }

        private bool StartArmoredTrainEvent()
        {
            if (EventLauncher.IsEventActive())
                return false;

            EventLauncher.DelayStartEvent();
            return true;
        }

        private bool EndArmoredTrainEvent()
        {
            if (EventLauncher.IsEventActive())
                return false;

            EventLauncher.StopEvent();
            return true;
        }

        private Vector3 ArmoredTrainLocomotivePosition()
        {
            if (!EventLauncher.IsEventActive())
                return Vector3.zero;

            return _eventController.GetEventPosition();
        }

        private bool IsTrainBradley(ulong netID)
        {
            return EventLauncher.IsEventActive() && _eventController.IsTrainBradley(netID);
        }

        private bool IsTrainHeli(ulong netID)
        {
            if (!EventLauncher.IsEventActive())
                return false;

            return EventHeli.GetEventHeliByNetId(netID) != null;
        }

        private bool IsTrainCrate(ulong netID)
        {
            return _eventController != null && _eventController.IsEventCrate(netID);
        }

        private bool IsTrainSamSite(ulong netID)
        {
            return EventLauncher.IsEventActive() && _eventController.IsTrainSamSite(netID);
        }

        private bool IsTrainWagon(ulong netID)
        {
            return EventLauncher.IsEventActive() && _eventController.IsTrainWagon(netID);
        }

        private bool IsTrainTurret(ulong netID)
        {
            return EventLauncher.IsEventActive() && _eventController.IsTrainTurret(netID);
        }
        #endregion ExternalAPI

        #region Hooks
        private void Init()
        {
            Unsubscribes();
        }

        private void OnServerInitialized()
        {
            _ins = this;

            if (!NpcSpawnManager.IsNpcSpawnReady())
                return;

            LoadDefaultMessages();
            UpdateConfig();
            
            PrefabController.CachePrefabs();
            GuiManager.LoadImages();
            WagonCustomizer.LoadCurrentCustomizationProfile();
            EventLauncher.AutoStartEvent();
            _protection = ScriptableObject.CreateInstance<ProtectionProperties>();
            _protection.Add(1);
        }

        private void Unload()
        {
            EventLauncher.StopEvent(true);
            
            if (_autoEventCoroutine != null)
                ServerMgr.Instance.StopCoroutine(_autoEventCoroutine);
            
            _ins = null;
        }

        private void OnEntitySpawned(HelicopterDebris entity)
        {
            if (!entity.IsExists() || entity.ShortPrefabName != "servergibs_bradley")
                return;

            if (Vector3.Distance(_eventController.GetEventPosition(), entity.transform.position) < _eventController.EventConfig.ZoneRadius)
                entity.Kill();
        }

        private object OnEntityTakeDamage(TrainCar trainCar, HitInfo info)
        {
            if (trainCar == null || trainCar.net == null || info == null)
                return null;

            if (_eventController.IsTrainWagon(trainCar.net.ID.Value))
            {
                if (info.InitiatorPlayer.IsRealPlayer())
                    _eventController.OnTrainAttacked(info.InitiatorPlayer);

                return true;
            }

            return null;
        }

        private object OnEntityTakeDamage(PatrolHelicopter patrolHelicopter, HitInfo info)
        {
            if (patrolHelicopter == null || patrolHelicopter.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return null;

            EventHeli eventHeli = EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value);

            if (eventHeli == null)
                return null;

            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, patrolHelicopter, true))
                return true;

            eventHeli.OnHeliAttacked(info.InitiatorPlayer.userID);
            _eventController.OnTrainAttacked(info.InitiatorPlayer);

            return null;
        }

        private object OnEntityTakeDamage(BradleyAPC bradley, HitInfo info)
        {
            if (bradley == null || bradley.net == null || info == null)
                return null;

            if (!_eventController.IsTrainBradley(bradley.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return true;

            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, bradley, true))
                return true;

            _eventController.OnTrainAttacked(info.InitiatorPlayer);

            return null;
        }

        private object OnEntityTakeDamage(AutoTurret autoTurret, HitInfo info)
        {
            if (autoTurret == null || autoTurret.net == null || info == null)
                return null;

            if (!_eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return true;

            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, autoTurret, true))
                return true;

            _eventController.OnTrainAttacked(info.InitiatorPlayer);
            return null;
        }

        private object OnEntityTakeDamage(SamSite samSite, HitInfo info)
        {
            if (samSite == null || samSite.net == null || info == null)
                return null;

            if (!_eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return true;

            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, samSite, true))
                return true;

            _eventController.OnTrainAttacked(info.InitiatorPlayer);

            return null;
        }

        private object OnEntityTakeDamage(ElectricSwitch electricSwitch, HitInfo info)
        {
            if (electricSwitch == null || electricSwitch.net == null || info == null)
                return null;

            if (_eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return true;

            return null;
        }

        private object OnEntityTakeDamage(PowerCounter powerCounter, HitInfo info)
        {
            if (powerCounter == null || powerCounter.net == null || info == null)
                return null;

            if (_eventController.IsTrainCounter(powerCounter.net.ID.Value))
                return true;

            return null;
        }

        private object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            ScientistNPC scientistNpc = player as ScientistNPC;
            if (scientistNpc != null)
            {
                if (scientistNpc == null || scientistNpc.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                    return null;
                
                if (NpcSpawnManager.GetScientistByNetId(scientistNpc.net.ID.Value) == null)
                    return null;
                
                if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, scientistNpc, true))
                {
                    info.damageTypes.ScaleAll(0);
                    _eventController.OnTrainAttacked(info.InitiatorPlayer);
                    return true;
                }
                
                if (scientistNpc.isMounted)
                {
                    if (!EventController.IsPlayerCanStopTrain(info.InitiatorPlayer, true))
                    {
                        info.damageTypes.ScaleAll(0);
                        return true;
                    }
                    
                    if (_config.MainConfig.AllowDriverDamage)
                    {
                        info.damageTypes.ScaleAll(10);
                    }
                    else
                    {
                        info.damageTypes.ScaleAll(0);
                        return true;
                    }
                }
                _eventController.OnTrainAttacked(info.InitiatorPlayer);
            }
            else
            {
                if (!player.IsRealPlayer() || !player.IsSleeping() || info == null || info.InitiatorPlayer == null)
                    return null;

                if (!info.InitiatorPlayer.isMounted || info.InitiatorPlayer.userID.IsSteamId() || info.InitiatorPlayer.net == null)
                    return null;

                if (NpcSpawnManager.GetScientistByNetId(info.InitiatorPlayer.net.ID.Value))
                    return true;
            }

            return null;
        }

        private void OnEntityDeath(PatrolHelicopter patrolHelicopter, HitInfo info)
        {
            if (patrolHelicopter == null || patrolHelicopter.net == null)
                return;

            EventHeli eventHeli = EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value);

            if (eventHeli != null && eventHeli.lastAttackedPlayer != 0)
                EconomyManager.AddBalance(eventHeli.lastAttackedPlayer, _config.SupportedPluginsConfig.EconomicsConfig.HeliPoint);
        }

        private void OnEntityDeath(AutoTurret autoTurret, HitInfo info)
        {
            if (autoTurret == null || autoTurret.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return;

            if (_eventController.IsTrainTurret(autoTurret.net.ID.Value))
                EconomyManager.AddBalance(info.InitiatorPlayer.userID, _config.SupportedPluginsConfig.EconomicsConfig.TurretPoint);
        }

        private void OnEntityDeath(BradleyAPC bradleyApc, HitInfo info)
        {
            if (bradleyApc == null || bradleyApc.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return;

            if (_eventController.IsTrainBradley(bradleyApc.net.ID.Value))
                EconomyManager.AddBalance(info.InitiatorPlayer.userID, _config.SupportedPluginsConfig.EconomicsConfig.BradleyPoint);
        }

        private void OnEntityDeath(ScientistNPC scientistNpc, HitInfo info)
        {
            if (scientistNpc == null || scientistNpc.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return;

            if (NpcSpawnManager.GetScientistByNetId(scientistNpc.net.ID.Value) != null)
            {
                if (scientistNpc.isMounted)
                    _eventController.OnDriverKilled(info.InitiatorPlayer);

                EconomyManager.AddBalance(info.InitiatorPlayer.userID, _config.SupportedPluginsConfig.EconomicsConfig.NpcPoint);
            }
        }

        private object OnEntityEnter(TriggerTrainCollisions trigger, TrainCar trainCar)
        {
            if (!_ins._config.MainConfig.DestroyWagons || trigger == null || !trigger.owner.IsExists() || trigger.owner.net == null || !trainCar.IsExists() || trainCar.net == null)
                return null;

            if (!_eventController.IsTrainWagon(trainCar.net.ID.Value) || _eventController.IsTrainWagon(trigger.owner.net.ID.Value))
                return null;

            if (trainCar is TrainEngine)
            {
                if (_eventController.IsReverse())
                    return null;
            }
            else if (!_eventController.IsReverse())
            {
                return null;
            }

            if (!trigger.owner.IsExists() || !_ins._config.MainConfig.DestroyWagons)
                return null;

            if (_ins.plugins.Exists("TrainHomes") && (bool)TrainHomes.Call("IsTrainHomes", trigger.owner.net.ID.Value) && !(bool)TrainHomes.Call("IsFreeWagon", trigger.owner.net.ID.Value))
                return null;

            _ins.NextTick(() => trigger.owner.Kill(BaseNetworkable.DestroyMode.Gib));

            return null;
        }
        
        private object OnEntityEnter(TargetTrigger trigger, ScientistNPC scientistNpc)
        {
            if (trigger == null || scientistNpc == null || scientistNpc.net == null)
                return null;

            AutoTurret autoTurret = trigger.GetComponentInParent<AutoTurret>();
            if (autoTurret == null || autoTurret.net == null)
                return null;

            if (NpcSpawnManager.GetScientistByNetId(scientistNpc.net.ID.Value) == null)
                return null;

            if (scientistNpc.isMounted)
            {
                if (!_config.MainConfig.IsPlayerTurretAttackTrainNpcs)
                    return true;
            }
            else if (!_config.MainConfig.IsPlayerTurretAttackDismountedNpcs)
                return true;

            return null;
        }

        private object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            if (heli == null || heli.helicopterBase == null || heli.helicopterBase.net == null)
                return null;

            EventHeli eventHeli = EventHeli.GetEventHeliByNetId(heli.helicopterBase.net.ID.Value);

            if (eventHeli != null && !eventHeli.IsHeliCanTarget())
                return false;

            if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                return false;

            return null;
        }

        private object OnCustomNpcTarget(ScientistNPC scientistNpc, BasePlayer player)
        {
            if (_eventController == null || scientistNpc == null || scientistNpc.net == null)
                return null;

            if (NpcSpawnManager.GetScientistByNetId(scientistNpc.net.ID.Value) == null)
                return null;

            if (!_eventController.IsAggressive())
            {
                BaseMountable baseMountable = scientistNpc.GetMounted();

                if (baseMountable != null && baseMountable.ShortPrefabName.Contains("driver"))
                    return null;
                
                return false;
            }

            if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                return false;

            return null;
        }

        private object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity entity)
        {
            if (bradley == null || bradley.net == null)
                return null;

            if (!_eventController.IsTrainBradley(bradley.net.ID.Value))
                return null;

            BasePlayer targetPlayer = entity as BasePlayer;

            if (!targetPlayer.IsRealPlayer())
                return false;

            if (targetPlayer.IsSleeping() || (targetPlayer.InSafeZone() && !targetPlayer.IsHostile()))
                return false;

            return null;
        }

        private object OnTrainCarUncouple(TrainCar trainCar, BasePlayer player)
        {
            if (trainCar == null || player == null || trainCar.net == null)
                return null;

            if (_eventController.IsTrainWagon(trainCar.net.ID.Value))
                return true;

            return null;
        }

        private object CanTrainCarCouple(TrainCar trainCar1, TrainCar trainCar2)
        {
            if (trainCar1 == null || trainCar1.net == null || trainCar2 == null || trainCar2.net == null)
                return null;

            if (_eventController.IsTrainWagon(trainCar1.net.ID.Value) && !_eventController.CanConnectToTrainWagon(trainCar1))
                return false;

            if (_eventController.IsTrainWagon(trainCar2.net.ID.Value) && !_eventController.CanConnectToTrainWagon(trainCar2))
                return false;

            return null;
        }

        private object CanPickupEntity(BasePlayer player, ElectricSwitch electricSwitch)
        {
            if (player == null || electricSwitch == null || electricSwitch.net == null)
                return null;

            if (_eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return false;

            return null;
        }

        private object CanPickupEntity(BasePlayer player, PowerCounter powerCounter)
        {
            if (player == null || powerCounter == null || powerCounter.net == null)
                return null;

            if (_eventController.IsTrainCounter(powerCounter.net.ID.Value))
                return false;

            return null;
        }

        private object CanMountEntity(BasePlayer player, BaseVehicleSeat entity)
        {
            if (!player.IsRealPlayer() || !entity.IsExists())
                return null;

            TrainCar trainCar = entity.VehicleParent() as TrainCar;

            if (!trainCar.IsExists() || trainCar.net == null)
                return null;

            if (_eventController.IsTrainWagon(trainCar.net.ID.Value))
                return true;

            return null;
        }

        private object OnSwitchToggle(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (!electricSwitch.IsExists() || electricSwitch.net == null || player == null)
                return null;

            if (!_eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return null;

            if (!electricSwitch.IsOn() && (!_ins._config.MainConfig.AllowEnableMovingByHandbrake || !_eventController.IsDriverAlive()))
                return true;

            if (electricSwitch.IsOn() && !EventController.IsPlayerCanStopTrain(player, true))
                return true;

            return null;
        }

        private void OnSwitchToggled(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (!electricSwitch.IsExists() || electricSwitch.net == null || player == null)
                return;

            if (!_eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return;

            _eventController.OnSwitchToggled(player);
        }

        private object OnCounterModeToggle(PowerCounter counter, BasePlayer player, bool mode)
        {
            if (!counter.IsExists() || counter.net == null || player == null)
                return null;

            if (_eventController.IsTrainCounter(counter.net.ID.Value))
                return true;

            return null;
        }

        private object OnCounterTargetChange(PowerCounter counter, BasePlayer player, int targetNumber)
        {
            if (!counter.IsExists() || counter.net == null || player == null)
                return null;

            if (_eventController.IsTrainCounter(counter.net.ID.Value))
                return true;

            return null;
        }

        private void OnPlayerSleep(BasePlayer player)
        {
            if (player == null)
                return;

            ZoneController.OnPlayerLeaveZone(player);
        }

        private object CanLootEntity(BasePlayer player, LootContainer container)
        {
            if (player == null || container == null || container.net == null)
                return null;

            if (!_eventController.IsEventCrate(container.net.ID.Value))
                return null;

            if (!_eventController.IsAggressive())
            {
                _eventController.MakeAggressive();
                return true;
            }

            if (!_eventController.IsPlayerCanLoot(player, true))
                return true;

            if (IsLootingCrateThroughTrainCar(player, container))
                return true;

            return null;
        }

        private object CanLootEntity(BasePlayer player, SamSite samSite)
        {
            if (player == null || samSite == null || samSite.net == null)
                return null;

            if (!_eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!_eventController.IsAggressive())
                _eventController.MakeAggressive();

            return true;
        }

        private object OnSamSiteModeToggle(SamSite samSite, BasePlayer player, bool isEnable)
        {
            if (player == null || samSite == null || samSite.net == null)
                return null;

            if (!_eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!_eventController.IsAggressive())
                _eventController.MakeAggressive();

            return true;
        }

        private object OnTurretAuthorize(AutoTurret autoTurret, BasePlayer player)
        {
            if (player == null || autoTurret == null || autoTurret.net == null)
                return null;

            if (!_eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            if (!_eventController.IsAggressive())
                _eventController.MakeAggressive();

            return true;
        }
        
        private object OnTurretTarget(AutoTurret autoTurret, ScientistNPC scientistNpc)
        {
            if (autoTurret == null || scientistNpc == null)
                return null;

            if (_eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return true;
            
            return null;
        }
        
        private object OnInterferenceUpdate(AutoTurret autoTurret)
        {
            if (autoTurret == null || autoTurret.net == null)
                return null;

            if (!_eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            return false;
        }

        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null || crate.net == null)
                return null;

            if (!_eventController.IsEventCrate(crate.net.ID.Value))
                return null;

            if (!_eventController.IsAggressive())
            {
                _eventController.MakeAggressive();
                return true;
            }

            if (!_eventController.IsPlayerCanLoot(player, true))
                return true;
            
            if (IsLootingCrateThroughTrainCar(player, crate))
                return true;

            if (!PveModeManager.IsPveModeBlockAction(player))
                EconomyManager.AddBalance(player.userID, _config.SupportedPluginsConfig.EconomicsConfig.HackCratePoint);

            _eventController.OnPlayerStartHackingCrate((int)(HackableLockedCrate.requiredHackSeconds - crate.hackSeconds));
            return null;
        }

        private void OnLootEntity(BasePlayer player, StorageContainer storageContainer)
        {
            if (player == null || storageContainer == null || storageContainer.net == null)
                return;

            if (_ins._eventController.IsEventCrate(storageContainer.net.ID.Value))
                _ins._eventController.OnEventCrateLooted(storageContainer, player.userID);
        }

        private void OnLootEntity(BasePlayer player, Fridge fridge)
        {
            if (player == null || fridge == null || fridge.net == null)
                return;

            if (_eventController.IsEventCrate(fridge.net.ID.Value))
                _eventController.OnEventCrateLooted(fridge, player.userID);
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer storageContainer)
        {
            if (storageContainer == null || storageContainer.net == null || !player.IsRealPlayer())
                return;

            if (storageContainer is not LootContainer && _eventController.IsEventCrate(storageContainer.net.ID.Value))
                if (storageContainer.inventory.IsEmpty())
                    storageContainer.Kill();
        }

        private void OnLootEntityEnd(BasePlayer player, Fridge fridge)
        {
            if (fridge == null || fridge.net == null || !player.IsRealPlayer())
                return;

            if (_eventController.IsEventCrate(fridge.net.ID.Value))
                if (fridge.inventory.IsEmpty())
                    fridge.Kill();
        }

        #region OtherPlugins
        private object CanPveModeAllowDamage(SamSite samSite, HitInfo info)
        {
            if (_eventController == null || samSite == null || samSite.net == null || info == null)
                return null;
            
            if (!_eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;
            
            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, samSite, true))
                return true;

            return null;
        }
        
        private object CanEntityBeTargeted(BasePlayer player, AutoTurret turret)
        {
            if (_eventController == null || turret == null || turret.net == null)
                return null;

            if (!_eventController.IsTrainTurret(turret.net.ID.Value))
                return null;

            if (!player.IsRealPlayer())
                return false;

            if (!_eventController.IsAggressive() && !_eventController.IsStopped())
                return false;

            if (!_config.SupportedPluginsConfig.PveMode.Enable || !_eventController.IsStopped())
                return true;

            return null;
        }

        private object CanEntityBeTargeted(PlayerHelicopter playerHelicopter, SamSite samSite)
        {
            if (_eventController == null || samSite == null || samSite.net == null)
                return null;

            if (!_eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            return _eventController.IsAggressive();
        }

        private object CanEntityTakeDamage(AutoTurret autoTurret, HitInfo info)
        {
            if (_eventController == null || autoTurret == null || autoTurret.net == null || info == null)
                return null;

            if (!_eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return false;
            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, autoTurret, true))
                return false;

            return true;
        }

        private object CanEntityTakeDamage(SamSite samSite, HitInfo info)
        {
            if (_eventController == null || samSite == null || samSite.net == null || info == null)
                return null;

            if (!_eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;
            
            if (!info.InitiatorPlayer.IsRealPlayer())
                return false;
            if (!EventController.IsPlayerCanDealDamage(info.InitiatorPlayer, samSite, true))
                return false;

            return true;
        }

        private object CanEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            if (_eventController == null || info == null || info.Initiator == null || info.Initiator.net == null || !victim.IsRealPlayer())
                return null;

            if (_config.ZoneConfig.IsPvpZone && !_config.SupportedPluginsConfig.PveMode.Enable)
            {
                if (info.InitiatorPlayer.IsRealPlayer() && ZoneController.IsPlayerInZone(info.InitiatorPlayer.userID) && ZoneController.IsPlayerInZone(victim.userID))
                    return true;
            }

            if (!_config.SupportedPluginsConfig.PveMode.Enable && info.Initiator is AutoTurret && _eventController.IsTrainTurret(info.Initiator.net.ID.Value))
                return true;

            return null;
        }

        private object CanEntityTakeDamage(PlayerHelicopter playerHelicopter, HitInfo info)
        {
            if (_eventController == null || info == null || info.Initiator == null || info.Initiator.net == null)
                return null;

            if (info.Initiator is SamSite)
            {
                if (_eventController.IsTrainSamSite(info.Initiator.net.ID.Value))
                    return true;
            }

            return null;
        }

        private object CanEntityTakeDamage(CustomBradley bradleyApc, HitInfo info)
        {
            if (_eventController == null || info == null || !info.InitiatorPlayer.IsRealPlayer() || bradleyApc.net == null)
                return null;

            if (_config.SupportedPluginsConfig.PveMode.Enable)
                return null;

            if (_eventController.IsTrainBradley(bradleyApc.net.ID.Value))
                return true;

            return null;
        }

        private object OnCreateDynamicPVP(string eventName, PatrolHelicopter patrolHelicopter)
        {
            if (_eventController == null || patrolHelicopter == null || patrolHelicopter.net == null)
                return null;

            if (EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value) != null)
                return true;

            return null;
        }

        private object OnCreateDynamicPVP(string eventName, BradleyAPC bradleyApc)
        {
            if (_eventController == null || bradleyApc == null || bradleyApc.net == null)
                return null;

            if (_eventController.IsTrainBradley(bradleyApc.net.ID.Value))
                return true;

            return null;
        }

        private void SetOwnerPveMode(string eventName, BasePlayer player)
        {
            if (_eventController == null || string.IsNullOrEmpty(eventName) || eventName != Name || !player.IsRealPlayer())
                return;

            if (eventName == Name)
                PveModeManager.OnNewOwnerSet(player);
        }

        private void ClearOwnerPveMode(string shortname)
        {
            if (_eventController == null || string.IsNullOrEmpty(shortname))
                return;

            if (shortname == Name)
                PveModeManager.OnOwnerDeleted();
        }

        private object CanBradleySpawnNpc(BradleyAPC bradley)
        {
            if (_eventController == null || bradley == null || bradley.net == null)
                return null;

            if (_eventController.IsTrainBradley(bradley.net.ID.Value))
                return true;

            return null;
        }

        private object CanHelicopterSpawnNpc(PatrolHelicopter patrolHelicopter)
        {
            if (_eventController == null || patrolHelicopter == null || patrolHelicopter.net == null)
                return null;

            if (_ins._config.SupportedPluginsConfig.BetterNpcConfig.IsHeliNpc)
                return null;

            if (EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value) != null)
                return true;

            return null;
        }
        #endregion OtherPlugins
        
        #region Loot
        private void OnCorpsePopulate(ScientistNPC scientistNpc, NPCPlayerCorpse corpse)
        {
            if (scientistNpc == null || scientistNpc.net == null || corpse == null)
                return;
            
            EntityPresetInfo entityPresetInfo = scientistNpc.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return;
            
            NpcConfig npcConfig = NpcSpawnManager.GetNpcConfigByPresetName(entityPresetInfo.presetName);
            if (npcConfig == null)
                return;
            
            LootController.UpdateNpcCorpse(corpse, npcConfig);
        }
        
        private void OnCrateSpawned(BradleyAPC bradleyApc, LockedByEntCrate crate)
        {
            if (bradleyApc == null || crate == null)
                return;
            
            EntityPresetInfo entityPresetInfo = bradleyApc.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return;

            BradleyConfig bradleyConfig = _config.Bradleys.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (bradleyConfig == null)
                return;
            
            if (bradleyConfig.InstCrateOpen)
            {
                crate.SetLockingEnt(null);
                crate.SetLocked(false);
            }

            if (!_eventController.isUnderGround)
                crate.transform.position += Vector3.up * 5f;
            
            _eventController.AddEventCrate(crate);
            LootController.UpdateHeliOrBradleyCrate(crate, bradleyConfig.LootTableConfig, bradleyConfig.PresetName);
        }

        private void OnCrateSpawned(PatrolHelicopter patrolHelicopter, LockedByEntCrate crate)
        {
            if (patrolHelicopter == null || crate == null)
                return;
            
            EntityPresetInfo entityPresetInfo = patrolHelicopter.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return;
            
            HeliConfig heliConfig = _config.HeliConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (heliConfig == null)
                return;
            
            if (heliConfig.InstCrateOpen)
            {
                crate.SetLockingEnt(null);
                crate.SetLocked(false);
            }

            if (heliConfig.CratesLifeTime > 0 && !Mathf.Approximately(heliConfig.CratesLifeTime, 1800f))
            {
                crate.CancelInvoke(crate.RemoveMe);
                crate.Invoke(crate.RemoveMe, heliConfig.CratesLifeTime);
            }
            
            _eventController.AddEventCrate(crate);
            LootController.UpdateHeliOrBradleyCrate(crate, heliConfig.LootTableConfig, heliConfig.PresetName);
        }
        
        #region LootTablePlugin
        private object OnContainerPopulate(LootContainer lootContainer)
        {
            if (lootContainer == null)
                return null;
            
            LockedByEntCrate lockedByEntCrate = lootContainer as LockedByEntCrate;
            if (lockedByEntCrate != null)
            {
                LootTableConfig lootTableConfig = null;

                if (_eventHeli != null && Vector3.Distance(_eventHeli.transform.position, lockedByEntCrate.transform.position) < 15f)
                    lootTableConfig = _eventHeli.HeliConfig.LootTableConfig;

                if (lootTableConfig == null)
                {
                    float minDistance = float.MaxValue;
                    BradleyAPC minDistanceBradley = null;
                    
                    foreach (BradleyAPC bradleyApc in _eventController.Bradleys)
                    {
                        if (bradleyApc == null)
                            continue;
                        
                        float distance = Vector3.Distance(bradleyApc.transform.position, lockedByEntCrate.transform.position);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            minDistanceBradley = bradleyApc;
                        }
                    }

                    if (minDistanceBradley != null && minDistance < 15)
                    {
                        EntityPresetInfo bradleyPresetInfo = minDistanceBradley.GetComponent<EntityPresetInfo>();
                        if (bradleyPresetInfo == null)
                            return null;

                        BradleyConfig bradleyConfig = _config.Bradleys.FirstOrDefault(x => x.PresetName == bradleyPresetInfo.presetName);
                        if (bradleyConfig == null)
                            return null;

                        lootTableConfig = bradleyConfig.LootTableConfig;
                    }
                        
                }
                
                if (lootTableConfig == null)
                    return null;

                if (!lootTableConfig.IsLoottablePlugin)
                    return true;
                    
                return null;
            }
            
            EntityPresetInfo entityPresetInfo = lootContainer.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return null;

            CrateConfig crateConfig = _config.CrateConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (crateConfig == null)
                return null;
            
            if (!crateConfig.LootTableConfig.IsLoottablePlugin)
                return true;
            
            return null;
        }

        private object OnCorpsePopulate(NPCPlayerCorpse corpse)
        {
            if (corpse == null)
                return null;

            EntityPresetInfo entityPresetInfo = corpse.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return null;
            
            NpcConfig npcConfig =  _config.NpcConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (npcConfig == null)
                return null;
            
            if (!npcConfig.LootTableConfig.IsLoottablePlugin)
                return true;
            
            return null;
        }
        #endregion LootTablePlugin
        
        #region AlphaLoot
        private object CanPopulateLoot(LootContainer lootContainer)
        {
            if (lootContainer == null)
                return null;

            EntityPresetInfo entityPresetInfo = lootContainer.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return null;

            LockedByEntCrate lockedByEntCrate = lootContainer as LockedByEntCrate;
            if (lockedByEntCrate != null)
            {
                LootTableConfig lootTableConfig = null;
                BradleyConfig bradleyConfig =  _config.Bradleys.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
                if (bradleyConfig != null)
                    lootTableConfig = bradleyConfig.LootTableConfig;
                
                if (lootTableConfig == null)
                {
                    HeliConfig heliConfig = _config.HeliConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
                    if (heliConfig != null)
                        lootTableConfig = heliConfig.LootTableConfig;
                }
                
                if (lootTableConfig == null)
                    return null;

                if (!lootTableConfig.IsAlphaLoot)
                    return true;
                    
                return null;
            }

            CrateConfig crateConfig = _config.CrateConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (crateConfig == null)
                return null;
            
            if (!crateConfig.LootTableConfig.IsAlphaLoot)
                return true;
            
            return null;
        }

        private object CanPopulateLoot(ScientistNPC scientistNpc, NPCPlayerCorpse corpse)
        {
            if (corpse == null)
                return null;

            EntityPresetInfo entityPresetInfo = corpse.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return null;
            
            NpcConfig npcConfig =  _config.NpcConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (npcConfig == null)
                return null;
            
            if (!npcConfig.LootTableConfig.IsAlphaLoot)
                return true;
            
            return null;
        }
        #endregion AlphaLoot
        
        #region CustomLoot
        private object OnCustomLootContainer(NetworkableId net)
        {
            BaseEntity crateEntity = _eventController.GetCrateByNetId(net.Value);
            if (crateEntity == null)
                return null;
            
            EntityPresetInfo entityPresetInfo = crateEntity.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return null;
            
            LockedByEntCrate lockedByEntCrate = crateEntity as LockedByEntCrate;
            if (lockedByEntCrate != null)
            {
                LootTableConfig lootTableConfig = null;
                BradleyConfig bradleyConfig =  _config.Bradleys.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
                if (bradleyConfig != null)
                    lootTableConfig = bradleyConfig.LootTableConfig;
                
                if (lootTableConfig == null)
                {
                    HeliConfig heliConfig = _config.HeliConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
                    if (heliConfig != null)
                        lootTableConfig = heliConfig.LootTableConfig;
                }
                
                if (lootTableConfig == null)
                    return null;
                
                if (!lootTableConfig.IsCustomLootPlugin)
                    return true;
                    
                return null;
            }
            
            CrateConfig crateConfig = _config.CrateConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (crateConfig == null)
                return null;
            
            if (!crateConfig.LootTableConfig.IsCustomLootPlugin)
                return true;
            
            return null;
        }

        private object OnCustomLootNPC(NetworkableId net)
        {
            ScientistNPC scientistNpc = NpcSpawnManager.GetScientistByNetId(net.Value);
            if (scientistNpc == null)
                return null;
            
            EntityPresetInfo entityPresetInfo = scientistNpc.GetComponent<EntityPresetInfo>();
            if (entityPresetInfo == null)
                return null;
            
            NpcConfig npcConfig =  _config.NpcConfigs.FirstOrDefault(x => x.PresetName == entityPresetInfo.presetName);
            if (npcConfig == null)
                return null;

            if (!npcConfig.LootTableConfig.IsCustomLootPlugin)
                return true;
            
            return null;
        }
        #endregion CustomLoot

        #endregion Loot
        #endregion Hooks

        #region Commands
        [ChatCommand("atrainstart")]
        private void ChatStartCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (arg == null || arg.Length == 0)
                EventLauncher.DelayStartEvent(false, player);
            else
            {
                string eventPresetName = arg[0];
                EventLauncher.DelayStartEvent(false, player, eventPresetName);
            }
        }

        [ChatCommand("atrainstop")]
        private void ChatStopCommand(BasePlayer player, string command, string[] arg)
        {
            if (player.IsAdmin)
                EventLauncher.StopEvent();
        }

        [ChatCommand("atrainstartunderground")]
        private void ChatUndergroundStartCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (arg == null || arg.Length == 0)
                EventLauncher.DelayStartEvent(false, player, overrideUndergroundChance: 100);
            else
            {
                string eventPresetName = arg[0];
                EventLauncher.DelayStartEvent(false, player, eventPresetName, 100);
            }
        }

        [ChatCommand("atrainstartaboveground")]
        private void ChatAboveGroundStartCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (arg == null || arg.Length == 0)
                EventLauncher.DelayStartEvent(false, player, overrideUndergroundChance: 0);
            else
            {
                string eventPresetName = arg[0];
                EventLauncher.DelayStartEvent(false, player, eventPresetName, 0);
            }
        }

        [ChatCommand("atrainpoint")]
        private void ChatCustomPointCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (!SpawnPositionFinder.IsRailsInPosition(player.transform.position))
            {
                PrintToChat(player, _config.Prefix + " <color=#ce3f27>Couldn't</color> find the rails");
                return;
            }

            PrintToChat(player, _config.Prefix + " New spawn point <color=#738d43>successfully</color> added");
            Vector3 rotation = player.eyes.GetLookRotation().eulerAngles;
            LocationConfig locationConfig = new LocationConfig
            {
                Position = player.transform.position.ToString(),
                Rotation = (new Vector3(0, rotation.y, 0)).ToString()
            };
            _config.MainConfig.CustomSpawnPointConfig.Points.Add(locationConfig);
            SaveConfig();
        }

        
        [ConsoleCommand("atrainstart")]
        private void ConsoleStartCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            if (arg == null || arg.Args == null || arg.Args.Length == 0)
                EventLauncher.DelayStartEvent();
            else
            {
                string eventPresetName = arg.Args[0].ToString();
                EventLauncher.DelayStartEvent(presetName: eventPresetName);
            }
        }

        [ConsoleCommand("atrainstop")]
        private void ConsoleStopCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                EventLauncher.StopEvent();
        }
        
        [ConsoleCommand("atrainstartunderground")]
        private void ConsoleUndergroundStartCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            if (arg == null || arg.Args == null || arg.Args.Length == 0)
                EventLauncher.DelayStartEvent(overrideUndergroundChance: 100);
            else
            {
                string eventPresetName = arg.Args[0].ToString();
                EventLauncher.DelayStartEvent(presetName: eventPresetName, overrideUndergroundChance: 100);
            }
        }

        [ConsoleCommand("atrainstartaboveground")]
        private void ConsoleAboveGroundStartCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            if (arg == null || arg.Args == null || arg.Args.Length == 0)
                EventLauncher.DelayStartEvent(overrideUndergroundChance: 0);
            else
            {
                string eventPresetName = arg.Args[0].ToString();
                EventLauncher.DelayStartEvent(presetName: eventPresetName, overrideUndergroundChance: 0);
            }
        }
        
        [ConsoleCommand("savecustomwagon")]
        private void ConsoleSaveCustomWagonCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            string customizationPresetName = arg.Args[0].ToString();
            string wagonShortPrefabName = arg.Args[1].ToString();

            WagonCustomizer.MapSaver.CreateOrAddNewWagonToData(customizationPresetName, wagonShortPrefabName);
        }
        #endregion Commands

        #region Methods
        private void Unsubscribes()
        {
            foreach (string hook in _subscribeMethods)
                Unsubscribe(hook);
        }

        private void Subscribes()
        {
            foreach (string hook in _subscribeMethods)
                Subscribe(hook);
        }

        private static void Debug(params object[] arg)
        {
            string result = "";

            foreach (object obj in arg)
                if (obj != null)
                    result += obj.ToString() + " ";

            _ins.Puts(result);
        }
        
        private bool IsLootingCrateThroughTrainCar(BasePlayer player, LootContainer container)
        {
            if (container.GetParentEntity() is not TrainCar)
                return false;
            
            BaseEntity playerParentEntity = player.GetParentEntity();
            if (playerParentEntity is TrainCar)
                return false;

            // float deltaY = container.transform.position.y - player.transform.position.y;
            // if (deltaY <= 0.3f)
            //     return false;

            if (!Physics.Raycast(player.eyes.position, player.eyes.HeadForward(), out RaycastHit hit, 5f, 1 << 0 | 1 << 8| 1 << 13| 1 << 15))
                return false;

            BaseEntity entity = hit.GetEntity();
            if (entity == null)
                return false;

            return entity is TrainCar;
        }
        #endregion Methods

        #region Classes
        private static class EventLauncher
        {
            public static bool IsEventActive()
            {
                return _ins != null && _ins._eventController != null;
            }

            public static void AutoStartEvent()
            {
                if (!_ins._config.MainConfig.IsAutoEvent)
                    return;

                if (_ins._autoEventCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_ins._autoEventCoroutine);

                _ins._autoEventCoroutine = ServerMgr.Instance.StartCoroutine(AutoEventCoroutine());
            }

            public static void DelayStartEvent(bool isAutoActivated = false, BasePlayer activator = null, string presetName = "", float overrideUndergroundChance = -1)
            {
                if (IsEventActive() || _ins._delayedEventStartCoroutine != null)
                {
                    NotifyManager.PrintError(activator, "EventActive_Exeption");
                    return;
                }

                if (_ins._autoEventCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_ins._autoEventCoroutine);

                float undergroundChance = overrideUndergroundChance >= 0 ? overrideUndergroundChance : _ins._config.MainConfig.UndergroundChance;
                bool isUnderGround = Random.Range(0f, 100f) < undergroundChance;

                EventConfig eventConfig = DefineEventConfig(presetName, isUnderGround);
                if (eventConfig == null)
                {
                    NotifyManager.PrintError(activator, "ConfigurationNotFound_Exeption");
                    StopEvent();
                    return;
                }

                _ins._delayedEventStartCoroutine = ServerMgr.Instance.StartCoroutine(DelayedStartEventCoroutine(eventConfig, isUnderGround));

                if (!isAutoActivated)
                    NotifyManager.PrintInfoMessage(activator, "SuccessfullyLaunched");
            }

            private static IEnumerator AutoEventCoroutine()
            {
                yield return CoroutineEx.waitForSeconds(Random.Range(_ins._config.MainConfig.MinTimeBetweenEvents, _ins._config.MainConfig.MaxTimeBetweenEvents));
                yield return CoroutineEx.waitForSeconds(5f);
                DelayStartEvent(true);
            }

            private static IEnumerator DelayedStartEventCoroutine(EventConfig eventConfig, bool isUnderGround)
            {
                if (_ins._config.NotifyConfig.PreStartTime > 0)
                    NotifyManager.SendMessageToAll("PreStartTrain", _ins._config.Prefix, eventConfig.DisplayName, _ins._config.NotifyConfig.PreStartTime);

                yield return CoroutineEx.waitForSeconds(_ins._config.NotifyConfig.PreStartTime);

                StartEvent(eventConfig, isUnderGround);
            }

            private static void StartEvent(EventConfig eventConfig, bool isUnderGround)
            {
                GameObject gameObject = new GameObject();
                _ins._eventController = gameObject.AddComponent<EventController>();
                _ins._eventController.Init(eventConfig, isUnderGround);

                if (_ins._config.MainConfig.EnableStartStopLogs)
                    NotifyManager.PrintLogMessage("EventStart_Log", eventConfig.PresetName);

                Interface.CallHook($"On{_ins.Name}EventStart");
            }

            public static void StopEvent(bool isPluginUnloadingOrFailed = false)
            {
                if (IsEventActive())
                {
                    _ins.Unsubscribes();

                    _ins._eventController.DeleteController();
                    ZoneController.TryDeleteZone();
                    PveModeManager.OnEventEnd();
                    EventMapMarker.DeleteMapMarker();
                    NpcSpawnManager.ClearData(false);
                    GuiManager.DestroyAllGui();
                    EconomyManager.OnEventEnd();
                    EventHeli.ClearData();

                    NotifyManager.SendMessageToAll("EndEvent", _ins._config.Prefix);
                    Interface.CallHook($"On{_ins.Name}EventStop");

                    if (_ins._config.MainConfig.EnableStartStopLogs)
                        NotifyManager.PrintLogMessage("EventStop_Log");

                    if (!isPluginUnloadingOrFailed)
                        AutoStartEvent();
                }

                if (_ins._delayedEventStartCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_ins._delayedEventStartCoroutine);
                    _ins._delayedEventStartCoroutine = null;
                }
            }

            private static EventConfig DefineEventConfig(string eventPresetName, bool isUnderGround)
            {
                if (eventPresetName != "")
                    return _ins._config.EventConfigs.FirstOrDefault(x => x.PresetName == eventPresetName);

                HashSet<EventConfig> suitableEventConfigs = _ins._config.EventConfigs.Where(x => x.Chance > 0 && x.IsAutoStart && (!isUnderGround || x.IsUndergroundTrain) && IsEventConfigSuitableByTime(x));

                if (suitableEventConfigs == null || suitableEventConfigs.Count == 0)
                    return null;

                float sumChance = 0;
                foreach (EventConfig eventConfig in suitableEventConfigs)
                    sumChance += eventConfig.Chance;

                float random = Random.Range(0, sumChance);

                foreach (EventConfig eventConfig in suitableEventConfigs)
                {
                    random -= eventConfig.Chance;

                    if (random <= 0)
                        return eventConfig;
                }

                return null;
            }

            private static bool IsEventConfigSuitableByTime(EventConfig eventConfig)
            {
                if (eventConfig.MinTimeAfterWipe <= 0 && eventConfig.MaxTimeAfterWipe <= 0)
                    return true;

                int timeScienceWipe = GetTimeScienceLastWipe();

                if (timeScienceWipe < eventConfig.MinTimeAfterWipe)
                    return false;

                return eventConfig.MaxTimeAfterWipe <= 0 || timeScienceWipe <= eventConfig.MaxTimeAfterWipe;
            }

            private static int GetTimeScienceLastWipe()
            {
                DateTime startTime = new DateTime(2019, 1, 1, 0, 0, 0);

                double realTime = DateTime.UtcNow.Subtract(startTime).TotalSeconds;
                double wipeTime = SaveRestore.SaveCreatedTime.Subtract(startTime).TotalSeconds;

                return Convert.ToInt32(realTime - wipeTime);
            }
        }

        private class EventController : FacepunchBehaviour
        {
            public EventConfig EventConfig;
            private TrainEngine _trainEngine;
            private TrainCar _lastWagon;
            private Coroutine _spawnCoroutine;
            private Coroutine _eventCoroutine;
            private readonly List<WagonData> _wagonDatas = new List<WagonData>();
            public readonly HashSet<BradleyAPC> Bradleys = new HashSet<BradleyAPC>();
            private readonly HashSet<AutoTurret> _turrets = new HashSet<AutoTurret>();
            private readonly HashSet<SamSite> _samSites = new HashSet<SamSite>();
            private readonly HashSet<NpcData> _npcDatas = new HashSet<NpcData>();
            private ScientistNPC _driver;
            private ElectricSwitch _electricSwitch;
            private PowerCounter _stopCounter;
            private PowerCounter _eventCounter;
            private WagonCustomizer _wagonCustomizer;
            private int _eventTime;
            private int _aggressiveTime;
            private int _stopTime;
            private bool _isReverse;
            public bool isUnderGround;
            private bool _isEventLooted;
            private Vector3 _lastGoodPosition;
            private float _lastGoodPositionTime;
            private BasePlayer _lastStopper;

            private readonly HashSet<ulong> _lootedContainersUids = new HashSet<ulong>();
            private readonly HashSet<BaseEntity> _crates = new HashSet<BaseEntity>();
            private int _countOfUnlootedCrates;

            public int GetEventTime()
            {
                return _eventTime;
            }

            public bool IsStopped()
            {
                return _stopTime > 0 && ZoneController.IsZoneCreated();
            }

            public bool IsTrainWagon(ulong netID)
            {
                return _wagonDatas.Any(x => x.TrainCar.IsExists() && x.TrainCar.net != null && x.TrainCar.net.ID.Value == netID);
            }

            public bool IsTrainBradley(ulong netID)
            {
                return Bradleys.Any(x => x.IsExists() && x.net != null && x.net.ID.Value == netID);
            }

            public bool IsTrainTurret(ulong netID)
            {
                return _turrets.Any(x => x.IsExists() && x.net != null && x.net.ID.Value == netID);
            }

            public bool IsTrainSamSite(ulong netID)
            {
                return _samSites.Any(x => x.IsExists() && x.net != null && x.net.ID.Value == netID);
            }

            public bool IsTrainSwitch(ulong netID)
            {
                return _electricSwitch.IsExists() && _electricSwitch.net != null && _electricSwitch.net.ID.Value == netID;
            }

            public bool IsTrainCounter(ulong netID)
            {
                return (_stopCounter.IsExists() && _stopCounter.net != null && _stopCounter.net.ID.Value == netID) || (_eventCounter.IsExists() && _eventCounter.net != null && _eventCounter.net.ID.Value == netID);
            }

            public bool IsAggressive()
            {
                return _ins._config.MainConfig.IsAggressive || _aggressiveTime > 0 || _stopTime > 0 || !_driver.IsExists() || _driver.IsDead();
            }

            public void AddEventCrate(BaseEntity entity)
            {
                _crates.Add(entity);
            }

            public void OnEventCrateLooted(BaseEntity baseEntity, ulong userId)
            {
                if (baseEntity.net == null)
                    return;

                if (!IsCrateLooted(baseEntity.net.ID.Value))
                {
                    if (_ins._config.SupportedPluginsConfig.EconomicsConfig.Crates.TryGetValue(baseEntity.PrefabName, out double cratePoint))
                        EconomyManager.AddBalance(userId, cratePoint);

                    _lootedContainersUids.Add(baseEntity.net.ID.Value);
                }

                EventPassingCheck();
            }

            public BaseEntity GetCrateByNetId(ulong netID)
            {
                return _crates.FirstOrDefault(x => x != null && x.net != null && x.net.ID.Value == netID);
            }

            public void UpdateCountOfUnlootedCrates()
            {
                _countOfUnlootedCrates = _crates.Where(x => x != null && x.IsExists() && x.net != null && !IsCrateLooted(x.net.ID.Value)).Count;
            }

            public int GetCountOfUnlootedCrates()
            {
                return _countOfUnlootedCrates;
            }

            public bool IsEventCrate(ulong netID)
            {
                return _crates.Any(x => x != null && x.net != null && x.net.ID.Value == netID);
            }

            public bool IsCrateLooted(ulong netID)
            {
                return _lootedContainersUids.Contains(netID);
            }

            public HashSet<ulong> GetEventCratesNetIDs()
            {
                HashSet<ulong> eventCrates = new HashSet<ulong>();

                foreach (BaseEntity crate in _crates)
                    if (crate != null && crate.net != null)
                        eventCrates.Add(crate.net.ID.Value);

                return eventCrates;
            }


            public bool IsDriverAlive()
            {
                return _driver.IsExists();
            }

            public static bool IsPlayerCanDealDamage(BasePlayer player, BaseCombatEntity trainEntity, bool shouldSendMessages)
            {
                Vector3 playerGroundPosition = new Vector3(player.transform.position.x, 0, player.transform.position.z);
                Vector3 entityGroundPosition = new Vector3(trainEntity.transform.position.x, 0, trainEntity.transform.position.z);
                float distance = Vector3.Distance(playerGroundPosition, entityGroundPosition);

                if (_ins._config.MainConfig.MaxGroundDamageDistance > 0 && distance > _ins._config.MainConfig.MaxGroundDamageDistance)
                {
                    if (shouldSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "DamageDistance", _ins._config.Prefix);

                    return false;
                }
                
                if (trainEntity is not PatrolHelicopter)
                {
                    if (_ins._config.MainConfig.MaxDamageHeight > 0)
                    {
                        float heightDifference = player.transform.position.y - trainEntity.transform.position.y;

                        if (heightDifference > _ins._config.MainConfig.MaxDamageHeight)
                        {
                            if (shouldSendMessages)
                                NotifyManager.SendMessageToPlayer(player, "DamageHeight", _ins._config.Prefix);

                            return false;
                        }
                    }

                    if (_ins._config.MainConfig.MaxFoundationHeightAboveGround > 0)
                    {
                        PlayerDamageController playerDamageController = PlayerDamageController.GetOrUpdatePlayerDamageController(player.userID);
                        if (playerDamageController.IsDamageBlocked(player))
                        {
                            if (shouldSendMessages)
                                NotifyManager.SendMessageToPlayer(player, "DamageRock", _ins._config.Prefix);

                            return false;
                        }
                    }
                }

                if (PveModeManager.IsPveModeBlockInteract(player))
                {
                    if (player.IsAdmin && _ins._config.SupportedPluginsConfig.PveMode.IgnoreAdmin)
                        return true;

                    NotifyManager.SendMessageToPlayer(player, "PveMode_BlockAction", _ins._config.Prefix);
                    return false;
                }

                return true;
            }

            public bool IsPlayerCanLoot(BasePlayer player, bool shouldSendMessages)
            {
                if (_ins._config.MainConfig.NeedStopTrain && !IsStopped())
                {
                    if (shouldSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "NeedStopTrain", _ins._config.Prefix);

                    return false;
                }

                if (_ins._config.MainConfig.NeedKillNpc && _npcDatas.Any(x => x.ScientistNpc.IsExists()) || _ins._config.MainConfig.NeedKillBradleys && Bradleys.Any(x => x.IsExists()) || _ins._config.MainConfig.NeedKillTurrets && _turrets.Any(x => x.IsExists() && x.totalAmmo > 0) || _ins._config.MainConfig.NeedKillHeli && EventHeli.IsEventHeliAlive())
                {
                    if (shouldSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "NeedKillGuards", _ins._config.Prefix);

                    return false;
                }

                if (player.IsAdmin && _ins._config.SupportedPluginsConfig.PveMode.IgnoreAdmin)
                    return true;

                if (PveModeManager.IsPveModeBlockInteract(player))
                {
                    if (shouldSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "PveMode_BlockAction", _ins._config.Prefix);

                    return false;
                }

                if (PveModeManager.IsPveModeBlockLooting(player))
                {
                    if (shouldSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "PveMode_YouAreNoOwner", _ins._config.Prefix);

                    return false;
                }

                return true;
            }

            public static bool IsPlayerCanStopTrain(BasePlayer player, bool shouldSendMessages)
            {
                if (PveModeManager.IsPveModeBlockInteract(player))
                {
                    if (shouldSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "PveMode_BlockAction", _ins._config.Prefix);

                    return false;
                }

                return true;
            }

            public bool IsReverse()
            {
                return _isReverse;
            }

            public bool CanConnectToTrainWagon(TrainCar myTrainCar)
            {
                if (_stopTime > 0)
                    return false;

                if (_isReverse)
                {
                    if (!_ins._config.MainConfig.EnableFrontConnector && myTrainCar.net.ID.Value == _lastWagon.net.ID.Value)
                        return false;
                    if (!_ins._config.MainConfig.EnableBackConnector && myTrainCar.net.ID.Value == _trainEngine.net.ID.Value)
                        return false;
                }
                else
                {
                    if (!_ins._config.MainConfig.EnableFrontConnector && myTrainCar.net.ID.Value == _trainEngine.net.ID.Value)
                        return false;
                    if (!_ins._config.MainConfig.EnableBackConnector && myTrainCar.net.ID.Value == _lastWagon.net.ID.Value)
                        return false;
                }

                return true;
            }

            public Vector3 GetEventPosition()
            {
                Vector3 resultPositon = Vector3.zero;

                foreach (WagonData wagonData in _wagonDatas)
                    resultPositon += wagonData.TrainCar.transform.position;

                return resultPositon / _wagonDatas.Count;
            }

            public void OnPlayerStartHackingCrate(int hackTime)
            {
                if (!_ins._config.MainConfig.ExtendEventIfNotEnoughTimeForCrate)
                    return;
                
                if (hackTime <= 0)
                    hackTime = 900;

                int minEventTime = hackTime + 30;

                if (!_isEventLooted && _eventTime < minEventTime)
                    _eventTime = minEventTime;
            }

            public HashSet<ulong> GetAliveTurretsNetIds()
            {
                HashSet<ulong> turretsIDs = new HashSet<ulong>();

                foreach (AutoTurret autoTurret in _turrets)
                    if (autoTurret.IsExists() && autoTurret.net != null)
                        turretsIDs.Add(autoTurret.net.ID.Value);

                return turretsIDs;
            }

            public HashSet<ulong> GetAliveBradleysNetIds()
            {
                HashSet<ulong> bradleysIDs = new HashSet<ulong>();

                foreach (BradleyAPC bradley in Bradleys)
                    if (bradley.IsExists() && bradley.net != null)
                        bradleysIDs.Add(bradley.net.ID.Value);

                return bradleysIDs;
            }

            public void Init(EventConfig eventConfig, bool isEventUnderGround)
            {
                EventConfig = eventConfig;
                isUnderGround = isEventUnderGround;

                StartSpawnTrain();
            }

            private void StartSpawnTrain()
            {
                PositionData positionData = SpawnPositionFinder.GetSpawnPositionData(isUnderGround);

                if (positionData == null)
                {
                    EventLauncher.StopEvent();
                    return;
                }

                _spawnCoroutine = ServerMgr.Instance.StartCoroutine(TrainSpawnCoroutine(positionData));
            }

            private IEnumerator TrainSpawnCoroutine(PositionData positionData)
            {
                yield return CoroutineEx.waitForSeconds(1f);
                SpawnLocomotiveAndDriver(positionData);

                if (!_trainEngine.IsExists() || !_driver.IsExists())
                {
                    OnSpawnFailed();
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                ChangeSpeed(EngineSpeeds.Fwd_Hi);
                TrainCar lastTrainCar = _trainEngine;
                float lastSpawnedTime = Time.realtimeSinceStartup;

                foreach (string wagonPresetName in EventConfig.WagonsPreset)
                {
                    WagonConfig wagonConfig = _ins._config.WagonConfigs.FirstOrDefault(x => x.PresetName == wagonPresetName);

                    if (wagonConfig == null)
                    {
                        NotifyManager.PrintError(null, "PresetNotFound_Exeption", wagonPresetName);
                        OnSpawnFailed();
                        break;
                    }

                    while (!IsSpawnFailed(lastSpawnedTime, false) && Vector3.Distance(positionData.Position, lastTrainCar.transform.position) < 25)
                        yield return CoroutineEx.waitForSeconds(0.5f);

                    TrainCar newTrainCar = SpawnTrainCar(wagonConfig.PrefabName, positionData, wagonConfig);
                    yield return CoroutineEx.waitForSeconds(0.5f);

                    if (IsSpawnFailed(lastSpawnedTime, false) || !newTrainCar.IsExists())
                    {
                        OnSpawnFailed();
                        break;
                    }

                    CoupleWagons(lastTrainCar, newTrainCar);
                    lastTrainCar = newTrainCar;
                    _lastWagon = newTrainCar;
                    lastSpawnedTime = Time.realtimeSinceStartup;
                }

                yield return CoroutineEx.waitForSeconds(0.5f);

                if (IsSpawnFailed(lastSpawnedTime, true))
                    OnSpawnFailed();
                else
                    OnTrainSpawned();
            }

            private void SpawnLocomotiveAndDriver(PositionData positionData)
            {
                LocomotiveConfig locomotiveConfig = _ins._config.LocomotiveConfigs.FirstOrDefault(x => x.PresetName == EventConfig.LocomotivePreset);

                if (locomotiveConfig == null)
                {
                    NotifyManager.PrintError(null, "PresetNotFound_Exeption", EventConfig.LocomotivePreset);
                    return;
                }

                _trainEngine = SpawnTrainCar(locomotiveConfig.PrefabName, positionData, locomotiveConfig) as TrainEngine;

                if (_trainEngine == null)
                {
                    NotifyManager.PrintError(null, "LocomotiveSpawn_Exeption", locomotiveConfig.PrefabName);
                    return;
                }

                _trainEngine.engineForce = locomotiveConfig.EngineForce;
                _trainEngine.maxSpeed = locomotiveConfig.MaxSpeed;

                EntityFuelSystem entityFuelSystem = _trainEngine.GetFuelSystem() as EntityFuelSystem;
                entityFuelSystem.cachedHasFuel = true;
                entityFuelSystem.nextFuelCheckTime = float.MaxValue;

                CreateDriver();
                _trainEngine.SetFlagLocal(BaseEntity.Flags.Reserved2, true);
            }

            private void CreateDriver()
            {
                WagonData wagonData = _wagonDatas.FirstOrDefault(x => x.WagonConfig is LocomotiveConfig);

                if (wagonData == null || !wagonData.TrainCar.IsExists())
                    return;

                LocomotiveConfig locomotiveConfig = wagonData.WagonConfig as LocomotiveConfig;

                if (locomotiveConfig == null)
                    return;

                _driver = NpcSpawnManager.SpawnScientistNpc(locomotiveConfig.DriverName, _trainEngine.transform.position, 1, true, true);
                _trainEngine.mountPoints[0].mountable.AttemptMount(_driver);
            }

            private bool IsSpawnFailed(float lastSpawnTime, bool checkWagonDistance)
            {
                if (!_trainEngine.IsExists() || !_driver.IsExists() || _wagonDatas.Any(x => !x.TrainCar.IsExists()) || Time.realtimeSinceStartup - lastSpawnTime > 30f)
                    return true;

                if (!checkWagonDistance)
                    return false;

                for (int i = 1; i < _wagonDatas.Count; i++)
                {
                    WagonData frontWagon = _wagonDatas[i];
                    WagonData backWagon = _wagonDatas[i - 1];

                    if (Vector3.Angle(frontWagon.TrainCar.transform.forward, backWagon.TrainCar.transform.forward) > 90f)
                        return true;

                    float distance = Vector3.Distance(frontWagon.TrainCar.transform.position, backWagon.TrainCar.transform.position);
                    float targetDistance = frontWagon.TrainCar.ShortPrefabName.Contains("workcart") || backWagon.TrainCar.ShortPrefabName.Contains("workcart") ? 13.45f : frontWagon.TrainCar.ShortPrefabName.Contains("locomotive") || backWagon.TrainCar.ShortPrefabName.Contains("locomotive") ? 18.38f : 16.25f;

                    if (Math.Abs(distance - targetDistance) > 1)
                        return true;
                }

                return false;
            }

            private TrainCar SpawnTrainCar(string prefabName, PositionData positionData, WagonConfig wagonConfig)
            {
                TrainCar trainCar = BuildManager.SpawnRegularEntity(prefabName, positionData.Position, positionData.Rotation) as TrainCar;

                if (trainCar == null)
                {
                    NotifyManager.PrintError(null, "EntitySpawn_Exeption", prefabName);
                    return null;
                }

                trainCar.CancelInvoke(trainCar.DecayTick);
                _wagonDatas.Add(new WagonData(trainCar, wagonConfig, trainCar.rigidBody.mass));

                TriggerParentEnclosed triggerParentEnclosed = trainCar.GetComponentInChildren<TriggerParentEnclosed>();
                if (triggerParentEnclosed != null)
                    triggerParentEnclosed.parentSleepers = false;
                
                trainCar.baseProtection = _ins._protection;

                return trainCar;
            }

            private void CoupleWagons(TrainCar frontWagon, TrainCar backWagon)
            {
                backWagon.coupling.frontCoupling.TryCouple(frontWagon.coupling.rearCoupling, false);
                frontWagon.coupling.rearCoupling.TryCouple(backWagon.coupling.frontCoupling, false);
            }

            private void OnSpawnFailed()
            {
                KillTrain();
                _trainEngine = null;
                _wagonDatas.Clear();

                if (_spawnCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_spawnCoroutine);

                StartSpawnTrain();
            }

            private void OnTrainSpawned()
            {
                _spawnCoroutine = ServerMgr.Instance.StartCoroutine(EntitiesSpawnCoroutine());

                _ins.Subscribes();
                _eventTime = EventConfig.EventTime;
                _eventCoroutine = ServerMgr.Instance.StartCoroutine(EventCoroutine());
                UpdateTrainCouples();
                UpdateCountOfUnlootedCrates();
                StartMoving();
                EventMapMarker.CreateMarker();
                NotifyManager.SendMessageToAll("StartTrain", _ins._config.Prefix, EventConfig.DisplayName, MapHelper.GridToString(MapHelper.PositionToGrid(GetEventPosition())));
                Interface.CallHook($"On{_ins.Name}StartMoving", GetEventPosition());
            }

            private void UpdateTrainCouples()
            {
                _trainEngine.coupling.Uncouple(true);
                _lastWagon.coupling.Uncouple(false);
            }

            private IEnumerator EntitiesSpawnCoroutine()
            {
                if (WagonCustomizer.IsCustomizationCanApplied())
                    _wagonCustomizer = gameObject.AddComponent<WagonCustomizer>();

                foreach (WagonData wagonData in _wagonDatas)
                {
                    if (wagonData.WagonConfig is LocomotiveConfig locomotiveConfig)
                    {
                        if (locomotiveConfig.HandleBrakeConfig.IsEnable)
                        {
                            _electricSwitch = BuildManager.SpawnChildEntity(wagonData.TrainCar, "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab", locomotiveConfig.HandleBrakeConfig.Location, 0, false) as ElectricSwitch;
                            _electricSwitch.UpdateFromInput(10, 0);
                            _electricSwitch.SetSwitch(true);
                        }

                        if (locomotiveConfig.StopTimerConfig.IsEnable)
                        {
                            _stopCounter = BuildManager.SpawnChildEntity(wagonData.TrainCar, "assets/prefabs/deployable/playerioents/counter/counter.prefab", locomotiveConfig.StopTimerConfig.Location, 0, false) as PowerCounter;
                            _stopCounter.targetCounterNumber = int.MaxValue;
                        }

                        if (locomotiveConfig.EventTimerConfig.IsEnable)
                        {
                            _eventCounter = BuildManager.SpawnChildEntity(wagonData.TrainCar, "assets/prefabs/deployable/playerioents/counter/counter.prefab", locomotiveConfig.EventTimerConfig.Location, 0, false) as PowerCounter;
                            _eventCounter.targetCounterNumber = int.MaxValue;
                            _eventCounter.UpdateFromInput(10, 0);
                        }
                    }
                    
                    TrainCarUnloadable trainCarUnloadable = wagonData.TrainCar as TrainCarUnloadable;

                    if (trainCarUnloadable != null)
                    {
                        foreach (LootContainer lootContainer in trainCarUnloadable.GetComponentsInChildren<LootContainer>())
                            if (lootContainer.IsExists())
                                lootContainer.Kill();

                        trainCarUnloadable.lifestate = LifeState.Dead;
                    }

                    foreach (KeyValuePair<string, HashSet<LocationConfig>> bradleyData in wagonData.WagonConfig.Bradleys)
                    {
                        BradleyConfig bradleyConfig = _ins._config.Bradleys.FirstOrDefault(x => x.PresetName == bradleyData.Key);

                        if (bradleyConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", bradleyData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in bradleyData.Value)
                        {
                            SpawnBradley(bradleyConfig, locationConfig, wagonData.TrainCar);
                            yield return null;
                        }
                    }

                    foreach (KeyValuePair<string, HashSet<LocationConfig>> turretData in wagonData.WagonConfig.Turrets)
                    {
                        TurretConfig turretConfig = _ins._config.TurretConfigs.FirstOrDefault(x => x.PresetName == turretData.Key);

                        if (turretConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", turretData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in turretData.Value)
                        {
                            SpawnTurret(turretConfig, locationConfig, wagonData.TrainCar);
                            yield return null;
                        }
                    }

                    foreach (KeyValuePair<string, HashSet<LocationConfig>> samsiteData in wagonData.WagonConfig.SamSites)
                    {
                        SamSiteConfig samSiteConfig = _ins._config.SamsiteConfigs.FirstOrDefault(x => x.PresetName == samsiteData.Key);

                        if (samSiteConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", samsiteData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in samsiteData.Value)
                        {
                            SpawnSamSite(samSiteConfig, locationConfig, wagonData.TrainCar);
                            yield return null;
                        }
                    }

                    foreach (KeyValuePair<string, HashSet<LocationConfig>> crateData in wagonData.WagonConfig.Crates)
                    {
                        CrateConfig crateConfig = _ins._config.CrateConfigs.FirstOrDefault(x => x.PresetName == crateData.Key);

                        if (crateConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", crateData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in crateData.Value)
                        {
                            SpawnCrate(crateConfig, locationConfig, wagonData.TrainCar);
                            yield return null;
                        }
                    }

                    foreach (KeyValuePair<string, HashSet<LocationConfig>> npcData in wagonData.WagonConfig.Npcs)
                    {
                        foreach (LocationConfig locationConfig in npcData.Value)
                        {
                            SpawnNpc(npcData.Key, locationConfig, wagonData.TrainCar);
                            yield return null;
                        }
                    }

                    WagonCustomizationData wagonCustomizationData = WagonCustomizer.GetWagonCustomizationData(wagonData.TrainCar.ShortPrefabName, wagonData.WagonConfig.PresetName);

                    if (wagonCustomizationData == null || !wagonCustomizationData.IsBaseDecorDisable)
                    {
                        foreach (KeyValuePair<string, HashSet<LocationConfig>> decorEntityData in wagonData.WagonConfig.Decors)
                        {
                            foreach (LocationConfig locationConfig in decorEntityData.Value)
                            {
                                SpawnDecorEntity(decorEntityData.Key, locationConfig, wagonData.TrainCar);
                                yield return null;
                            }
                        }
                    }
                    if (_wagonCustomizer != null)
                        _wagonCustomizer.DecorateWagon(wagonData.TrainCar, wagonCustomizationData);

                    if (trainCarUnloadable != null)
                        foreach (LootContainer lootContainer in trainCarUnloadable.GetComponentsInChildren<LootContainer>())
                            if (lootContainer != null)
                                lootContainer.inventory.SetLocked(false);
                }

                if (EventConfig.HeliPreset != "" && !isUnderGround)
                {
                    HeliConfig heliConfig = _ins._config.HeliConfigs.FirstOrDefault(x => x.PresetName == EventConfig.HeliPreset);

                    if (heliConfig == null)
                        NotifyManager.PrintError(null, "PresetNotFound_Exeption", EventConfig.HeliPreset);
                    else
                        EventHeli.SpawnHeli(heliConfig);
                }

                yield return CoroutineEx.waitForSeconds(1);

                if (_wagonCustomizer != null)
                    _wagonCustomizer.OnTrainSpawned();
            }

            private void SpawnBradley(BradleyConfig bradleyConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                Vector3 localPosition = locationConfig.Position.ToVector3();
                Vector3 localRotation = locationConfig.Rotation.ToVector3();

                BradleyAPC bradleyApc = CustomBradley.SpawnCustomBradley(localPosition, localRotation, trainCar, bradleyConfig);
                BuildManager.UpdateEntityMaxHealth(bradleyApc, bradleyConfig.Hp);

                bradleyApc.myRigidBody.isKinematic = true;
                bradleyApc.maxCratesToSpawn = bradleyConfig.CrateCount;
                bradleyApc.viewDistance = bradleyConfig.ViewDistance;
                bradleyApc.searchRange = bradleyConfig.SearchDistance;
                bradleyApc.coaxAimCone *= bradleyConfig.CoaxAimCone;
                bradleyApc.coaxFireRate *= bradleyConfig.CoaxFireRate;
                bradleyApc.coaxBurstLength = bradleyConfig.CoaxBurstLength;
                bradleyApc.nextFireTime = bradleyConfig.NextFireTime;
                bradleyApc.topTurretFireRate = bradleyConfig.TopTurretFireRate;
                bradleyApc.ScientistSpawnCount = 0;
                bradleyApc.skinID = 755447;
                Bradleys.Add(bradleyApc);

                bradleyApc.Invoke(() =>
                {
                    if (trainCar != null)
                    {
                        bradleyApc.myRigidBody = trainCar.rigidBody;

                        foreach (var a in bradleyApc.GetComponentsInChildren<TriggerBase>())
                            a.enabled = false;

                        foreach (var a in bradleyApc.GetComponentsInChildren<WheelCollider>())
                            DestroyImmediate(a);

                        bradleyApc.rightWheels = Array.Empty<WheelCollider>();
                        bradleyApc.leftWheels = Array.Empty<WheelCollider>();
                    }
                }, 1f);
            }

            private void SpawnTurret(TurretConfig turretConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                AutoTurret autoTurret = BuildManager.SpawnChildEntity(trainCar, "assets/prefabs/npc/autoturret/autoturret_deployed.prefab", locationConfig, 0, false) as AutoTurret;
                BuildManager.UpdateEntityMaxHealth(autoTurret, turretConfig.Hp);
                
                autoTurret.inventory.Insert(ItemManager.CreateByName(turretConfig.ShortNameWeapon));
                
                if (turretConfig.CountAmmo > 0)
                    autoTurret.inventory.Insert(ItemManager.CreateByName(turretConfig.ShortNameAmmo, turretConfig.CountAmmo));
                
                autoTurret.UpdateFromInput(IsAggressive() ? 10 : 0, 0);
                autoTurret.isLootable = false;
                autoTurret.dropFloats = false;
                autoTurret.dropsLoot = _ins._config.MainConfig.IsTurretDropWeapon;
                _turrets.Add(autoTurret);
                
                if (turretConfig.TargetLossRange != 0)
                    autoTurret.sightRange = turretConfig.TargetLossRange;
                
                if (turretConfig.TargetDetectionRange != 0 && autoTurret.targetTrigger != null)
                {
                    SphereCollider sphereCollider = autoTurret.targetTrigger.GetComponent<SphereCollider>();
                
                    if (sphereCollider != null)
                        sphereCollider.radius = turretConfig.TargetDetectionRange;
                }
                
                TurretOptimizer.Attach(autoTurret, turretConfig.TargetDetectionRange);
            }

            private void SpawnSamSite(SamSiteConfig samSiteConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                SamSite samSite = BuildManager.SpawnChildEntity(trainCar, "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab", locationConfig, 0, false) as SamSite;
                BuildManager.UpdateEntityMaxHealth(samSite, samSiteConfig.Hp);

                if (samSiteConfig.CountAmmo > 0)
                    samSite.inventory.Insert(ItemManager.CreateByName("ammo.rocket.sam", samSiteConfig.CountAmmo));

                samSite.UpdateFromInput(IsAggressive() ? 100 : 0, 0);
                samSite.isLootable = false;
                samSite.dropFloats = false;
                samSite.dropsLoot = false;
                _samSites.Add(samSite);
            }

            private static void SpawnDecorEntity(string prefabName, LocationConfig locationConfig, TrainCar trainCar)
            {
                BaseCombatEntity baseCombatEntity = BuildManager.SpawnChildEntity(trainCar, prefabName, locationConfig, 0, false) as BaseCombatEntity;

                if (baseCombatEntity == null)
                {
                    NotifyManager.PrintError(null, "EntitySpawn_Exeption", prefabName);
                    return;
                }

                baseCombatEntity.lifestate = LifeState.Dead;
                baseCombatEntity.SendNetworkUpdate();
            }

            private void SpawnCrate(CrateConfig crateConfig, LocationConfig locationConfig, BaseEntity trainCar)
            {
                StorageContainer crateEntity = BuildManager.CreateEntity(crateConfig.Prefab, trainCar.transform.position, trainCar.transform.rotation, crateConfig.Skin, false) as StorageContainer;
                if (crateEntity == null)
                {
                    NotifyManager.PrintError(null, "EntitySpawn_Exeption", crateConfig.Prefab);
                    return;
                }
                BuildManager.DestroyUnnecessaryComponents(crateEntity);
                BuildManager.SetParent(trainCar, crateEntity, locationConfig.Position.ToVector3(), locationConfig.Rotation.ToVector3());
                crateEntity.Spawn();
                _ins._eventController.AddEventCrate(crateEntity);
                _crates.Add(crateEntity);

                if (crateEntity is HackableLockedCrate hackableLockedCrate)
                {
                    if (hackableLockedCrate.mapMarkerInstance.IsExists())
                    {
                        hackableLockedCrate.mapMarkerInstance.Kill();
                        hackableLockedCrate.mapMarkerInstance = null;
                    }

                    hackableLockedCrate.Invoke(() => hackableLockedCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - crateConfig.HackTime, 1.1f);
                    hackableLockedCrate.InvokeRepeating(() => hackableLockedCrate.SendNetworkUpdate(), 1f, 1f);
                    hackableLockedCrate.shouldDecay = false;
                    hackableLockedCrate.RefreshDecay();
                }
                else if (crateEntity is SupplyDrop supplyDrop)
                {
                    supplyDrop.RemoveParachute();
                    supplyDrop.MakeLootable();

                    EntityTimedDestroy entityTimedDestroy = supplyDrop.GetComponent<EntityTimedDestroy>();
                    if (entityTimedDestroy != null)
                        Destroy(entityTimedDestroy);

                }
                else if (crateEntity is FreeableLootContainer _)
                {
                    crateEntity.SetFlagLocal(BaseEntity.Flags.Reserved8, false);
                }
                
                LootController.UpdateCrate(crateEntity, crateConfig);
            }

            private void SpawnNpc(string npcPresetName, LocationConfig locationConfig, TrainCar trainCar)
            {
                ScientistNPC scientistNpc = CreateChildNpc(npcPresetName, trainCar, locationConfig, 1);

                if (scientistNpc == null)
                    return;

                NpcData npcData = new NpcData(scientistNpc, npcPresetName, locationConfig, trainCar);
                _npcDatas.Add(npcData);
            }

            private void ReplaceByRoamNpcs()
            {
                if ((isUnderGround && !_ins._config.MainConfig.IsNpcJumpInSubway) || (!isUnderGround && !_ins._config.MainConfig.IsNpcJumpOnSurface))
                    return;

                foreach (NpcData npcData in _npcDatas)
                {
                    if (!npcData.ScientistNpc.IsExists() || npcData.IsRoaming)
                        continue;

                    int scale = npcData.ScientistNpc.transform.localPosition.x < 0 ? -1 : 1;
                    Vector3 spawnPosition = npcData.ScientistNpc.transform.position + npcData.TrainCar.transform.right * (scale * 2.5f);

                    if (!PositionDefiner.GetNavmeshInPoint(spawnPosition, 2, out NavMeshHit navMeshHit))
                        continue;

                    spawnPosition = navMeshHit.position;
                    Vector3 vector3 = PositionDefiner.GetGroundPositionInPoint(spawnPosition);
                    if (Math.Abs(vector3.y - spawnPosition.y) > 0.5f)
                        continue;

                    float healthFraction = npcData.ScientistNpc.healthFraction;
                    npcData.ScientistNpc.Kill();
                    npcData.ScientistNpc = NpcSpawnManager.SpawnScientistNpc(npcData.PresetName, spawnPosition, healthFraction, false);
                    npcData.IsRoaming = true;
                }
            }

            private void ReplaceByStaticNpcs()
            {
                foreach (NpcData npcData in _npcDatas)
                {
                    if (!npcData.ScientistNpc.IsExists() || !npcData.IsRoaming)
                        continue;

                    float healthFraction = npcData.ScientistNpc.healthFraction;
                    npcData.ScientistNpc.Kill();
                    npcData.ScientistNpc = CreateChildNpc(npcData.PresetName, npcData.TrainCar, npcData.LocationConfig, healthFraction);
                    npcData.IsRoaming = false;
                }
            }

            private ScientistNPC CreateChildNpc(string presetName, TrainCar trainCar, LocationConfig locationConfig, float healthFraction)
            {
                Vector3 localPosition = locationConfig.Position.ToVector3();
                Vector3 localRotation = locationConfig.Rotation.ToVector3();
                Vector3 spawnPosition = PositionDefiner.GetGlobalPosition(trainCar.transform, localPosition);
                ScientistNPC scientistNpc = NpcSpawnManager.SpawnScientistNpc(presetName, spawnPosition, healthFraction, true);

                if (scientistNpc == null)
                    return null;

                BuildManager.SetParent(trainCar, scientistNpc, localPosition, localRotation);
                return scientistNpc;
            }

            private IEnumerator EventCoroutine()
            {
                while (_eventTime > 0 || (!_isEventLooted && _ins._config.MainConfig.DontStopEventIfPlayerInZone && ZoneController.IsAnyPlayerInEventZone()))
                {
                    if (_ins._config.NotifyConfig.TimeNotifications.Contains(_eventTime))
                        NotifyManager.SendMessageToAll("RemainTime", _ins._config.Prefix, _eventTime);

                    if (_wagonDatas.Any(x => !x.TrainCar.IsExists()))
                        break;

                    if (_stopTime > 0)
                    {
                        _stopTime--;

                        if (_stopTime <= 0)
                        {
                            _isReverse = false;
                            StartMoving();
                            Interface.CallHook($"On{_ins.Name}StartMoving", GetEventPosition());
                        }
                    }

                    if (!IsStopped() && _aggressiveTime > 0)
                    {
                        _aggressiveTime--;

                        if (_aggressiveTime <= 0 && !IsAggressive())
                            MakeNoAggressive();
                    }

                    if (_stopTime > 0 && !ZoneController.IsZoneCreated() && Math.Abs(_trainEngine.GetSpeed()) <= 1)
                    {
                        OnTrainFullStop();
                    }

                    UpdateElectricCounters();
                    CheckUnderGround();
                    CheckStuck();

                    if (_eventTime % 30 == 0)
                    {
                        UpdateCratesVisibility();

                        if (EventConfig.EventTime - _eventTime > 30)
                            EventPassingCheck();
                    }

                    if (_eventTime > 0)
                        _eventTime--;

                    yield return CoroutineEx.waitForSeconds(1);
                }

                EventLauncher.StopEvent();
            }

            public void EventPassingCheck()
            {
                if (_isEventLooted)
                    return;

                _ins._eventController.UpdateCountOfUnlootedCrates();
                int countOfUnlootedCrates = _ins._eventController.GetCountOfUnlootedCrates();

                if (countOfUnlootedCrates <= 0)
                {
                    _isEventLooted = true;
                    StopMoving();

                    if (_ins._config.MainConfig.KillTrainAfterLoot && _eventTime > _ins._config.MainConfig.EndAfterLootTime)
                        _eventTime = _ins._config.MainConfig.EndAfterLootTime;

                    NotifyManager.SendMessageToAll("Looted", _ins._config.Prefix, EventConfig.DisplayName);
                }
            }

            public void OnSwitchToggled(BasePlayer player)
            {
                MakeAggressive();

                if (_electricSwitch.IsOn())
                    StartMoving();
                else if (_stopTime == 0)
                    OnPlayerStoppedTrain(player);
            }

            public void OnDriverKilled(BasePlayer attacker)
            {
                if (_stopTime == 0)
                    OnPlayerStoppedTrain(attacker);
            }

            public void OnTrainAttacked(BasePlayer attacker)
            {
                if (_stopTime == 0 && _ins._config.MainConfig.StopTrainAfterReceivingDamage)
                    OnPlayerStoppedTrain(attacker);

                if (_stopTime > 0 && _ins._config.MainConfig.IsRestoreStopTimeAfterDamageOrLoot)
                    _stopTime = EventConfig.StopTime;

                MakeAggressive();
            }

            public void MakeAggressive()
            {
                if (_ins._config.MainConfig.IsAggressive)
                    return;

                if (_aggressiveTime > 0)
                {
                    _aggressiveTime = _ins._config.MainConfig.AggressiveTime;
                    return;
                }

                _aggressiveTime = _ins._config.MainConfig.AggressiveTime;

                foreach (AutoTurret autoTurret in _turrets)
                    if (autoTurret.IsExists())
                        autoTurret.UpdateFromInput(10, 0);

                foreach (SamSite samSite in _samSites)
                    if (samSite.IsExists())
                        samSite.UpdateFromInput(100, 0);
            }

            private void MakeNoAggressive()
            {
                if (_ins._config.MainConfig.IsAggressive)
                    return;

                foreach (AutoTurret autoTurret in _turrets)
                    if (autoTurret.IsExists())
                        autoTurret.UpdateFromInput(0, 0);

                foreach (SamSite samSite in _samSites)
                    if (samSite.IsExists())
                        samSite.UpdateFromInput(0, 0);
            }

            private void OnPlayerStoppedTrain(BasePlayer player)
            {
                if (EventConfig.StopTime <= 0)
                    return;

                if (!IsPlayerCanStopTrain(player, true))
                    return;

                if (player != null)
                    NotifyManager.SendMessageToAll("PlayerStopTrain", _ins._config.Prefix, player.displayName);

                _lastStopper = player;
                StopMoving();
            }

            private void StartMoving()
            {
                if (!_driver.IsExists())
                {
                    if (_ins._config.MainConfig.ReviveTrainDriver)
                        CreateDriver();
                    else
                        return;
                }

                if (_isEventLooted)
                    return;

                _stopTime = 0;
                ChangeSpeed(_isReverse ? EngineSpeeds.Rev_Hi : EngineSpeeds.Fwd_Hi);

                if (_electricSwitch.IsExists())
                    _electricSwitch.SetSwitch(true);

                if (_stopCounter.IsExists())
                    _stopCounter.UpdateFromInput(0, 0);

                ZoneController.TryDeleteZone();
                ReplaceByStaticNpcs();
                UpdateTrainCouples();

                _lastGoodPositionTime = Time.realtimeSinceStartup;
                _lastGoodPosition = _trainEngine.transform.position;

                foreach (WagonData wagonData in _wagonDatas)
                    wagonData.TrainCar.rigidBody.mass = wagonData.Mass;
            }

            private void StopMoving()
            {
                if (EventConfig.StopTime <= 0)
                    return;
                
                Interface.CallHook($"On{_ins.Name}StopMoving", GetEventPosition());
                _stopTime = EventConfig.StopTime;
                ChangeSpeed(EngineSpeeds.Zero);

                if (_electricSwitch.IsExists())
                    _electricSwitch.SetSwitch(false);

                UpdateElectricCounters();

                if (_stopCounter.IsExists())
                    _stopCounter.UpdateFromInput(10, 0);

                UpdateTrainCouples();

                foreach (WagonData wagonData in _wagonDatas)
                    wagonData.TrainCar.rigidBody.mass = float.MaxValue;
            }

            private void OnTrainFullStop()
            {
                ReplaceByRoamNpcs();
                UpdateCratesVisibility();
                ZoneController.CreateZone(_ins._config.SupportedPluginsConfig.PveMode.OwnerIsStopper ? _lastStopper : null);
                _isReverse = false;
                _lastStopper = null;
            }

            private void UpdateCratesVisibility()
            {
                foreach (BaseEntity crateEntity in _crates)
                {
                    if (crateEntity.IsExists() && crateEntity is HackableLockedCrate or SupplyDrop)
                    {
                        crateEntity.SendNetworkUpdate();
                    }
                }
            }

            private void UpdateElectricCounters()
            {
                if (_stopCounter.IsExists())
                {
                    _stopCounter.counterNumber = _stopTime;
                    _stopCounter.SendNetworkUpdate();
                }

                if (_eventCounter.IsExists())
                {
                    _eventCounter.counterNumber = _eventTime;
                    _eventCounter.SendNetworkUpdate();
                }
            }

            private void CheckUnderGround()
            {
                if (_stopTime > 0)
                    return;

                isUnderGround = _trainEngine.transform.position.y < 0;
            }

            private void CheckStuck()
            {
                if (_stopTime > 0)
                    return;

                float distanceToLastGoodPosition = Vector3.Distance(_trainEngine.transform.position, _lastGoodPosition);

                if (distanceToLastGoodPosition > 1)
                {
                    _lastGoodPositionTime = Time.realtimeSinceStartup;
                    _lastGoodPosition = _trainEngine.transform.position;
                    return;
                }

                if (Time.realtimeSinceStartup - _lastGoodPositionTime >= 10)
                {
                    _isReverse = !_isReverse;
                    StartMoving();
                }
            }

            private void ChangeSpeed(EngineSpeeds engineSpeeds)
            {
                if (!_trainEngine.engineController.IsStartingOrOn)
                    _trainEngine.engineController.TryStartEngine(_driver);

                _trainEngine.SetThrottle(engineSpeeds);
            }

            public void DeleteController()
            {
                if (_eventCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_eventCoroutine);

                if (_spawnCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_spawnCoroutine);

                if (_wagonCustomizer != null)
                    _wagonCustomizer.DestroyCustomizer();

                KillTrain();
                Destroy(this);
            }

            private void KillTrain()
            {
                foreach (WagonData wagonData in _wagonDatas)
                    if (wagonData.TrainCar.IsExists())
                        wagonData.TrainCar.DismountAllPlayers();

                foreach (AutoTurret autoTurret in _turrets)
                {
                    if (autoTurret.IsExists())
                    {
                        AutoTurret.interferenceUpdateList.Remove(autoTurret);
                        autoTurret.Kill();
                    }
                }

                if (_driver.IsExists())
                {
                    _driver.Kill();
                }

                foreach (NpcData npcData in _npcDatas)
                    if (npcData.ScientistNpc.IsExists())
                        npcData.ScientistNpc.Kill();

                foreach (WagonData wagonData in _wagonDatas)
                    if (wagonData.TrainCar.IsExists())
                        wagonData.TrainCar.Kill();

                _turrets.Clear();
                _npcDatas.Clear();
                _wagonDatas.Clear();
            }

            private class WagonData
            {
                public readonly TrainCar TrainCar;
                public readonly WagonConfig WagonConfig;
                public readonly float Mass;

                public WagonData(TrainCar trainCar, WagonConfig wagonConfig, float mass)
                {
                    TrainCar = trainCar;
                    WagonConfig = wagonConfig;
                    Mass = mass;
                }
            }

            private class NpcData
            {
                public ScientistNPC ScientistNpc;
                public readonly string PresetName;
                public readonly LocationConfig LocationConfig;
                public readonly TrainCar TrainCar;
                public bool IsRoaming;

                public NpcData(ScientistNPC scientistNpc, string presetName, LocationConfig locationConfig, TrainCar trainCar)
                {
                    ScientistNpc = scientistNpc;
                    PresetName = presetName;
                    LocationConfig = locationConfig;
                    TrainCar = trainCar;
                }
            }
        }

        private class TurretOptimizer : FacepunchBehaviour
        {
            private AutoTurret _autoTurret;
            private float _targetRadius;
            
            public static void Attach(AutoTurret autoTurret, float targetRadius)
            {
                TurretOptimizer turretOptimizer = autoTurret.gameObject.AddComponent<TurretOptimizer>();
                turretOptimizer.Init(autoTurret, targetRadius);
            }

            private void Init(AutoTurret autoTurret, float targetRadius)
            {
                _autoTurret = autoTurret;
                _targetRadius = targetRadius;
                
                SphereCollider sphereCollider = autoTurret.targetTrigger.GetComponent<SphereCollider>();
                sphereCollider.enabled = false;
                autoTurret.InvokeRepeating(ScanTargets, 3f, 1f);
            }
            
            private void ScanTargets()
            {
                if (_autoTurret.target != null && _autoTurret.target is not BasePlayer)
                    _autoTurret.SetTarget(null);
                
                if (_autoTurret.targetTrigger.entityContents == null)
                    _autoTurret.targetTrigger.entityContents = new HashSet<BaseEntity>();
                else
                    _autoTurret.targetTrigger.entityContents.Clear();

                if (!_ins._eventController.IsAggressive())
                    return;

                int count = BaseEntity.Query.Server.GetPlayersInSphereFast(transform.position, _targetRadius, AIBrainSenses.playerQueryResults, IsPlayerCanBeTargeted);

                if (count == 0)
                    return;

                _autoTurret.authDirty = true;

                for (int i = 0; i < count; i++)
                {
                    BasePlayer player = AIBrainSenses.playerQueryResults[i];

                    if (Interface.CallHook("OnEntityEnter", _autoTurret.targetTrigger, player) != null)
                        continue;

                    if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                        continue;

                    _autoTurret.targetTrigger.entityContents.Add(player);
                }
            }
            
            private bool IsPlayerCanBeTargeted(BasePlayer player)
            {
                if (!player.IsRealPlayer())
                    return false;

                if (player.IsDead() || player.IsSleeping() || player.IsWounded())
                    return false;

                if (player.InSafeZone() || player._limitedNetworking)
                    return false;

                return true;
            }
        }

        private class WagonCustomizer : FacepunchBehaviour
        {
            private static CustomizeProfile _customizeProfile;

            private static readonly HashSet<string> FireEntities = new HashSet<string>
            {
                "hobobarrel.deployed",
                "largecandleset",
                "jackolantern.happy",
                "skullspikes.candles.deployed",
                "skull_fire_pit"
            };

            private Coroutine _updateCoroutine;
            private readonly HashSet<BaseEntity> _lightEntities = new HashSet<BaseEntity>();
            private readonly HashSet<NeonSign> _neonSigns = new HashSet<NeonSign>();
            private float _lastNeonUpdateTime;
            private ItemThrower _itemThrower;
            private CustomFirework _customFirework;
            private bool _isLightEnable;

            public static void LoadCurrentCustomizationProfile()
            {
                _customizeProfile = null;
                
                string profileName = _ins._config.CustomizationConfig.IsChristmas ? "NewYear" : _ins._config.CustomizationConfig.IsHalloween ? "Halloween" : string.Empty;
                if (string.IsNullOrEmpty(profileName))
                    return;

                _customizeProfile = LoadProfile(profileName);

                if (!IsCustomizationCanApplied())
                    NotifyManager.PrintError(null, "DataFileNotFound_Exeption", profileName);
            }

            public static bool IsCustomizationCanApplied()
            {
                return _customizeProfile != null && _customizeProfile.WagonPresets != null && _customizeProfile.WagonPresets.Count > 0;
            }

            private static CustomizeProfile LoadProfile(string profileName)
            {
                string filePath = $"{_ins.Name}/{profileName}";
                return Interface.Oxide.DataFileSystem.ReadObject<CustomizeProfile>(filePath);
            }

            public static JArray GetCustomizeNpcWearSet()
            {
                if (_customizeProfile == null || _customizeProfile.NpcPresets == null)
                    return null;

                CustomizeNpcConfig randomCustomizeNpcConfig = _customizeProfile.NpcPresets.GetRandom();

                if (randomCustomizeNpcConfig == null)
                    return null;

                return new JArray
                {
                    randomCustomizeNpcConfig.CustomWearItems.Select(x => new JObject
                    {
                        ["ShortName"] = x.ShortName,
                        ["SkinID"] = x.SkinID
                    })
                };
            }

            public void OnTrainSpawned()
            {
                _updateCoroutine = ServerMgr.Instance.StartCoroutine(UpdateCoroutine());
            }

            public void DecorateWagon(TrainCar trainCar, WagonCustomizationData wagonCustomizationData)
            {
                if (wagonCustomizationData == null)
                    return;

                if (wagonCustomizationData.DecorEntityConfigs != null)
                {
                    List<DecorEntityConfig> decorEntityConfigList = wagonCustomizationData.DecorEntityConfigs.ToList();

                    for (int i = 0; i < decorEntityConfigList.Count; i++)
                    {
                        DecorEntityConfig decorEntityConfig = decorEntityConfigList[i];
                        if (i > 0)
                        {
                            DecorEntityConfig previousDecorConfig = decorEntityConfigList[i - 1];
                            if (previousDecorConfig.Position == decorEntityConfig.Position && previousDecorConfig.Rotation == decorEntityConfig.Rotation && previousDecorConfig.PrefabName == decorEntityConfig.PrefabName)
                                continue;
                        }
                        SpawnDecorEntity(decorEntityConfig, trainCar);
                    }
                }

                if (wagonCustomizationData.SignConfigs != null)
                    foreach (PaintedSignConfig decorEntityConfig in wagonCustomizationData.SignConfigs)
                        SpawnDecorEntity(decorEntityConfig, trainCar);

                TrainEngine trainEngine = trainCar as TrainEngine;

                if (trainEngine != null)
                    SpawnCustomCanonShell(trainEngine);
            }

            public static WagonCustomizationData GetWagonCustomizationData(string shortPrefabName, string wagonPresetName)
            {
                if (_customizeProfile == null || _customizeProfile.WagonPresets == null)
                    return null;

                return _customizeProfile.WagonPresets.FirstOrDefault(x => x.IsEnabled && x.ShortPrefabName == shortPrefabName && !x.WagonExceptions.Contains(wagonPresetName) && (x.WagonOnly == null || x.WagonOnly.Count == 0 || x.WagonOnly.Contains(wagonPresetName)));
            }

            private void SpawnDecorEntity(DecorEntityConfig decorEntityConfig, TrainCar trainCar)
            {
                Vector3 localPosition = decorEntityConfig.Position.ToVector3();
                Vector3 localRotation = decorEntityConfig.Rotation.ToVector3();
                bool isNoDecorEntity = decorEntityConfig.PrefabName.Contains("neon") || decorEntityConfig.PrefabName.Contains("skullspikes");

                BaseEntity entity = BuildManager.SpawnChildEntity(trainCar, decorEntityConfig.PrefabName, localPosition, localRotation, decorEntityConfig.Skin, !isNoDecorEntity);

                if (entity != null)
                    UpdateDecorEntity(entity, decorEntityConfig);
            }

            private void UpdateDecorEntity(BaseEntity entity, DecorEntityConfig decorEntityConfig)
            {
                entity.SetFlagLocal(BaseEntity.Flags.Busy, true);
                entity.SetFlagLocal(BaseEntity.Flags.Locked, true);

                NeonSign neonSign = entity as NeonSign;
                if (neonSign != null)
                    UpdateNeonSign(neonSign, decorEntityConfig);

                else
                    UpdateCommonEntities(entity);
            }

            private void UpdateNeonSign(NeonSign neonSign, DecorEntityConfig decorEntityConfig)
            {
                if (decorEntityConfig is PaintedSignConfig paintedSignConfig)
                    if (_ins._config.CustomizationConfig.IsNeonSignsEnable)
                        SignPainter.UpdateNeonSign(neonSign, paintedSignConfig.ImageName);

                _neonSigns.Add(neonSign);
            }

            private void UpdateCommonEntities(BaseEntity entity)
            {
                if (entity.ShortPrefabName == "skulltrophy.deployed")
                    entity.SetFlagLocal(BaseEntity.Flags.Reserved1, true);

                if (_ins._config.CustomizationConfig.IsBoilersEnable && entity.ShortPrefabName == "cursedcauldron.deployed")
                    UpdateLightEntity(entity);

                else if (_ins._config.CustomizationConfig.IsElectricFurnacesEnable && entity.ShortPrefabName == "electricfurnace.deployed")
                    UpdateLightEntity(entity);

                else if (_ins._config.CustomizationConfig.IsFireEnable && IsEntityFire(entity.ShortPrefabName))
                    UpdateLightEntity(entity);

                else if (entity.ShortPrefabName == "industrial.wall.lamp.red.deployed")
                    UpdateLightEntity(entity);

                else if (entity.ShortPrefabName == "xmas_tree.deployed")
                    DecorateChristmasTree(entity);

                if (entity.ShortPrefabName is "wooden_crate_gingerbread" or "gingerbread_barricades_snowman" or "gingerbread_barricades_house" or "gingerbread_barricades_tree")
                    entity.gameObject.layer = 12;
            }

            private static bool IsEntityFire(string shortPrefabName)
            {
                return FireEntities.Contains(shortPrefabName);
            }

            private void UpdateLightEntity(BaseEntity entity)
            {
                if (_ins._config.CustomizationConfig.IsLightOnlyAtNight)
                    _lightEntities.Add(entity);
                else
                    entity.SetFlagLocal(BaseEntity.Flags.On, true);
            }

            private static void DecorateChristmasTree(BaseEntity christmasTree)
            {
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved1, true);
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved2, true);
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved3, true);
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved4, true);
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved5, true);
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved6, true);
                christmasTree.SetFlagLocal(BaseEntity.Flags.Reserved7, true);
            }

            private void SpawnCustomCanonShell(TrainEngine trainEngine)
            {
                if (!_ins._config.CustomizationConfig.GiftCannonSetting.IsGiftCannonEnable && !_ins._config.CustomizationConfig.FireworksSettings.IsFireworksOn)
                    return;

                if (_itemThrower != null || _customFirework != null)
                    return;

                Vector3 canonShellPosition = trainEngine.ShortPrefabName == "locomotive.entity" ? new Vector3(0, 4.649f, 4.478f) : new Vector3(0.719f, 3.814f, 3.513f);
                BuildManager.SpawnChildEntity(trainEngine, "assets/prefabs/deployable/fireworks/mortarpattern.prefab", canonShellPosition, Vector3.zero);

                if (_ins._config.CustomizationConfig.GiftCannonSetting.IsGiftCannonEnable)
                    _itemThrower = new ItemThrower(trainEngine, canonShellPosition);

                if (_ins._config.CustomizationConfig.FireworksSettings.IsFireworksOn && _customizeProfile.FireworkConfigs != null && _customizeProfile.FireworkConfigs.Count > 0)
                    _customFirework = new CustomFirework(trainEngine, canonShellPosition);
            }

            private IEnumerator UpdateCoroutine()
            {
                while (EventLauncher.IsEventActive())
                {
                    PeriodicUpdateOfCustomizationEntities();
                    yield return CoroutineEx.waitForSeconds(1);
                }
            }

            private void PeriodicUpdateOfCustomizationEntities()
            {
                UpdateTimeLight();
                TryUpdateSignEntities();

                if (_itemThrower != null)
                    _itemThrower.UpdateItemThrower();

                if (_customFirework != null)
                    _customFirework.UpdateCustomFirework();
            }

            private void UpdateTimeLight()
            {
                if (!_ins._config.CustomizationConfig.IsLightOnlyAtNight)
                    return;

                if (_isLightEnable)
                {
                    if (ConVar.Env.time > 9 && ConVar.Env.time < 18)
                    {
                        TurnLight(false);
                    }
                }
                else
                {
                    if (ConVar.Env.time < 9 || ConVar.Env.time > 18)
                    {
                        TurnLight(true);
                    }
                }
            }

            private void TurnLight(bool enable)
            {
                _isLightEnable = enable;

                foreach (BaseEntity entity in _lightEntities)
                    if (entity.IsExists())
                        entity.SetFlagLocal(BaseEntity.Flags.On, enable);
            }

            private void TryUpdateSignEntities()
            {
                if (!_ins._config.CustomizationConfig.IsNeonSignsEnable || !(Time.realtimeSinceStartup - _lastNeonUpdateTime > 30))
                    return;

                _lastNeonUpdateTime = Time.realtimeSinceStartup;
                foreach (NeonSign neonSign in _neonSigns)
                {
                    if (!neonSign.IsExists())
                        continue;

                    neonSign.limitNetworking = true;
                    neonSign.limitNetworking = false;
                }
            }

            public void DestroyCustomizer()
            {
                if (_updateCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_updateCoroutine);
            }

            private class ItemThrower
            {
                private readonly TrainCar _trainCar;
                private readonly Vector3 _localThrowingPosition;
                private float _lastThrowItemTime = Time.realtimeSinceStartup;
                private float _nextItemThrowDelay;

                public ItemThrower(TrainCar trainCar, Vector3 localThrowingPosition)
                {
                    _trainCar = trainCar;
                    _localThrowingPosition = localThrowingPosition;

                    _nextItemThrowDelay = Random.Range(_ins._config.CustomizationConfig.GiftCannonSetting.MinTimeBetweenItems, _ins._config.CustomizationConfig.GiftCannonSetting.MaxTimeBetweenItems);
                }

                public void UpdateItemThrower()
                {
                    if (!(Time.realtimeSinceStartup - _lastThrowItemTime > _nextItemThrowDelay))
                        return;

                    _lastThrowItemTime = Time.realtimeSinceStartup;
                    _nextItemThrowDelay = Random.Range(_ins._config.CustomizationConfig.GiftCannonSetting.MinTimeBetweenItems, _ins._config.CustomizationConfig.GiftCannonSetting.MaxTimeBetweenItems);
                    ThrowItem();
                }

                private void ThrowItem()
                {
                    XMasCrateItem itemForThrow = GetItemForThrowing();

                    if (itemForThrow == null)
                        return;

                    Item droppedItem = CreateItemByItemConfig(itemForThrow);
                    if (droppedItem == null)
                        return;

                    Vector3 startPosition = PositionDefiner.GetGlobalPosition(_trainCar.transform, _localThrowingPosition) + Vector3.up;
                    Vector3 velocity = _trainCar.GetWorldVelocity() + new Vector3(Random.Range(-5, 5), Random.Range(7, 20), Random.Range(-5, 5));
                    Quaternion randomItemRotation = Quaternion.Euler(new Vector3(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360)));

                    droppedItem.Drop(startPosition, velocity, randomItemRotation);
                }

                private static XMasCrateItem GetItemForThrowing()
                {
                    int counter = 0;

                    while (counter < 100)
                    {
                        XMasCrateItem item = _ins._config.CustomizationConfig.GiftCannonSetting.Items.GetRandom();

                        if (Random.Range(0.0f, 100.0f) <= item.Chance)
                            return item;
                        counter++;
                    }

                    return _ins._config.CustomizationConfig.GiftCannonSetting.Items.Max(x => x.Chance);
                }

                private static Item CreateItemByItemConfig(XMasCrateItem item)
                {
                    int amount = Random.Range(item.MinAmount, item.MaxAmount + 1);

                    Item newItem;
                    if (item.IsBlueprint)
                    {
                        newItem = ItemManager.CreateByName("blueprintbase");

                        ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.Shortname);
                        if (itemDefinition != null)
                            newItem.blueprintTarget = itemDefinition.itemid;
                    }
                    else
                    {
                        newItem = ItemManager.CreateByName(item.Shortname, amount, item.Skin);
                    }

                    return newItem;
                }
            }

            private class CustomFirework
            {
                private readonly TrainCar _trainCar;
                private PatternFirework _patternFirework;
                private readonly Vector3 _localFireworkPosition;
                private float _lastFireTime = Time.realtimeSinceStartup;

                public CustomFirework(TrainCar trainCar, Vector3 localFireworkPosition)
                {
                    _trainCar = trainCar;
                    _localFireworkPosition = localFireworkPosition;
                }

                public void UpdateCustomFirework()
                {
                    if ((_ins._config.CustomizationConfig.FireworksSettings.IsNighFireworks && !IsNightNow()) || !(Time.realtimeSinceStartup - _lastFireTime >= _ins._config.CustomizationConfig.FireworksSettings.TimeBetweenFireworks))
                        return;

                    _lastFireTime = Time.realtimeSinceStartup;
                    ActivateFireWork();
                }

                private static bool IsNightNow()
                {
                    return ConVar.Env.time < 7 || ConVar.Env.time > 20;
                }

                private void ActivateFireWork()
                {
                    if (_patternFirework.IsExists())
                        _patternFirework.Kill();

                    HashSet<FireworkConfig> suitableFireworkConfigs = _customizeProfile.FireworkConfigs.Where(x => x.IsEnabled);
                    if (suitableFireworkConfigs == null)
                        return;

                    FireworkConfig fireworkConfig = suitableFireworkConfigs.ToList().GetRandom();
                    if (fireworkConfig == null)
                        return;

                    _patternFirework = BuildManager.SpawnChildEntity(_trainCar, "assets/prefabs/deployable/fireworks/mortarpattern.prefab", _localFireworkPosition, Vector3.zero, 0, false) as PatternFirework;
                    UpdateFireworkPaint(fireworkConfig);
                    _patternFirework.TryLightFuse();
                }

                private void UpdateFireworkPaint(FireworkConfig fireworkConfig)
                {
                    _patternFirework.maxRepeats = _ins._config.CustomizationConfig.FireworksSettings.NumberShotsInSalvo;

                    _patternFirework.Design?.Dispose();
                    _patternFirework.MaxStars = 1000;
                    _patternFirework.Design = new Design();
                    _patternFirework.Design.stars = new List<Star>();
                    Vector3 color = fireworkConfig.Color.ToVector3();
                    foreach (string coord in fireworkConfig.PaintCoordinates)
                    {
                        Vector3 position = coord.ToVector3();

                        Star star = new Star
                        {
                            color = new Color(color.x, color.y, color.z),
                            position = new Vector2(position.x, position.y)
                        };

                        _patternFirework.Design.stars.Add(star);
                    }
                }
            }

            public static class MapSaver
            {
                private static readonly Dictionary<string, string> ColliderPrefabNames = new Dictionary<string, string>
                {
                    ["fence_a"] = "assets/prefabs/misc/xmas/icewalls/icewall.prefab",
                    ["christmas_present_LOD0"] = "assets/prefabs/misc/xmas/sleigh/presentdrop.prefab",
                    ["snowman_LOD1"] = "assets/prefabs/misc/xmas/snowman/snowman.deployed.prefab",
                    ["giftbox_LOD0"] = "assets/prefabs/misc/xmas/giftbox/giftbox_loot.prefab"
                };

                public static void CreateOrAddNewWagonToData(string customizationPresetName, string wagonShortPrefabName)
                {
                    CustomizeProfile newCustomizeProfile = LoadProfile(customizationPresetName);
                    if (newCustomizeProfile == null || newCustomizeProfile.WagonPresets == null)
                    {
                        newCustomizeProfile = new CustomizeProfile
                        {
                            WagonPresets = new List<WagonCustomizationData>(),
                            NpcPresets = GetNewNpcCustomizeConfig(),
                            FireworkConfigs = GetFireWorksConfig()
                        };
                    }

                    WagonCustomizationData wagonCustomizationData = SaveWagonFromMap(wagonShortPrefabName);
                    newCustomizeProfile.WagonPresets.Add(wagonCustomizationData);
                    SaveProfile(newCustomizeProfile, customizationPresetName);
                }

                private static WagonCustomizationData SaveWagonFromMap(string wagonShortPrefabName)
                {
                    WagonCustomizationData wagonCustomizationData = new WagonCustomizationData
                    {
                        ShortPrefabName = wagonShortPrefabName,
                        IsEnabled = true,
                        WagonOnly = new HashSet<string>(),
                        WagonExceptions = new HashSet<string>(),
                        TrainExceptions = new HashSet<string>(),
                        DecorEntityConfigs = new HashSet<DecorEntityConfig>(),
                        SignConfigs = new HashSet<PaintedSignConfig>()
                    };

                    CheckAndSaveColliders(ref wagonCustomizationData);
                    return wagonCustomizationData;
                }

                private static void CheckAndSaveColliders(ref WagonCustomizationData wagonCustomizationData)
                {
                    List<Collider> colliders = Physics.OverlapSphere(Vector3.zero, 50).OrderBy(x => x.transform.position.z);

                    foreach (Collider collider in colliders)
                        TrySaveCollider(collider, ref wagonCustomizationData);
                }

                private static void TrySaveCollider(Collider collider, ref WagonCustomizationData wagonCustomizationData)
                {
                    BaseEntity entity = collider.ToBaseEntity();

                    if (entity == null)
                        SaveCollider(collider, ref wagonCustomizationData);
                    else if (IsCustomizingEntity(entity))
                    {
                        NeonSign neonSign = entity as NeonSign;

                        if (neonSign != null)
                            SaveNeonSign(neonSign, ref wagonCustomizationData);
                        else
                            SaveRegularEntity(entity, ref wagonCustomizationData);
                    }
                }

                private static bool IsCustomizingEntity(BaseEntity entity)
                {
                    if (entity == null)
                        return false;

                    if (entity is ResourceEntity or BasePlayer)
                        return false;

                    if (entity is LootContainer)
                        return false;

                    return true;
                }

                private static void SaveNeonSign(NeonSign neonSign, ref WagonCustomizationData wagonCustomizationData)
                {
                    PaintedSignConfig paintedSignConfig = GetPaintedSignConfig(neonSign);

                    if (paintedSignConfig != null && !wagonCustomizationData.SignConfigs.Any(x => x.PrefabName == paintedSignConfig.PrefabName && x.Position == paintedSignConfig.Position && x.Rotation == paintedSignConfig.Rotation))
                        wagonCustomizationData.SignConfigs.Add(paintedSignConfig);
                }

                private static PaintedSignConfig GetPaintedSignConfig(NeonSign neonSign)
                {
                    return new PaintedSignConfig
                    {
                        PrefabName = neonSign.PrefabName,
                        Skin = 0,
                        Position = $"({neonSign.transform.position.x}, {neonSign.transform.position.y}, {neonSign.transform.position.z})",
                        Rotation = neonSign.transform.eulerAngles.ToString(),
                        ImageName = ""
                    };
                }

                private static void SaveRegularEntity(BaseEntity entity, ref WagonCustomizationData wagonCustomizationData)
                {
                    DecorEntityConfig decorLocationConfig = GetDecorEntityConfig(entity);

                    if (decorLocationConfig != null && !wagonCustomizationData.DecorEntityConfigs.Any(x => x.PrefabName == decorLocationConfig.PrefabName && x.Position == decorLocationConfig.Position && x.Rotation == decorLocationConfig.Rotation))
                        wagonCustomizationData.DecorEntityConfigs.Add(decorLocationConfig);
                }

                private static DecorEntityConfig GetDecorEntityConfig(BaseEntity entity)
                {
                    ulong skin = entity.skinID;
                    if (entity.ShortPrefabName == "rug.deployed")
                        skin = 2349822120;
                    else if (entity.ShortPrefabName == "rug.bear.deployed")
                        skin = 91053011;
                    else if (entity.ShortPrefabName == "barricade.sandbags")
                        skin = 809144507;
                    else if (entity.ShortPrefabName == "barricade.concrete")
                        skin = 3103508242;

                    return new DecorEntityConfig
                    {
                        PrefabName = entity.PrefabName,
                        Skin = skin,
                        Position = $"({entity.transform.position.x}, {entity.transform.position.y}, {entity.transform.position.z})",
                        Rotation = entity.transform.eulerAngles.ToString()
                    };
                }

                private static void SaveCollider(Collider collider, ref WagonCustomizationData wagonCustomizationData)
                {
                    DecorEntityConfig colliderEntityConfig = GetColliderConfigAsBaseEntity(collider);

                    if (colliderEntityConfig != null && !wagonCustomizationData.DecorEntityConfigs.Any(x => x.PrefabName == colliderEntityConfig.PrefabName && x.Position == colliderEntityConfig.Position && x.Rotation == colliderEntityConfig.Rotation))
                        wagonCustomizationData.DecorEntityConfigs.Add(colliderEntityConfig);
                }

                private static DecorEntityConfig GetColliderConfigAsBaseEntity(Collider collider)
                {
                    if (!ColliderPrefabNames.TryGetValue(collider.name, out string prefabName))
                        return null;

                    return new DecorEntityConfig
                    {
                        PrefabName = prefabName,
                        Skin = 0,
                        Position = $"({collider.transform.position.x}, {collider.transform.position.y}, {collider.transform.position.z})",
                        Rotation = collider.transform.eulerAngles.ToString()
                    };
                }

                private static void SaveProfile(CustomizeProfile customizeData, string name)
                {
                    Interface.Oxide.DataFileSystem.WriteObject($"{_ins.Name}/{name}", customizeData);
                }

                private static List<CustomizeNpcConfig> GetNewNpcCustomizeConfig()
                {
                    return new List<CustomizeNpcConfig>();
                }

                private static List<FireworkConfig> GetFireWorksConfig()
                {
                    return GetNyFireWorksConfig();
                }

                private static List<FireworkConfig> GetNyFireWorksConfig()
                {
                    return new List<FireworkConfig>
                        {
                            new FireworkConfig
                            {
                                PresetName = "2024",
                                IsEnabled = true,
                                Color = "(0, 1, 0)",
                                PaintCoordinates = new HashSet<string>
                                {
                                    "(0.01, 0.85, 0.00)",
                                    "(0.00, 1.00, 0.00)",
                                    "(0.13, 1.00, 0.00)",
                                    "(0.28, 1.00, 0.00)",
                                    "(0.43, 1.00, 0.00)",
                                    "(0.43, 0.85, 0.00)",
                                    "(0.43, 0.71, 0.00)",
                                    "(0.33, 0.59, 0.00)",
                                    "(0.22, 0.50, 0.00)",
                                    "(0.11, 0.41, 0.00)",
                                    "(0.04, 0.29, 0.00)",
                                    "(0.04, 0.15, 0.00)",
                                    "(0.04, 0.02, 0.00)",
                                    "(0.19, 0.02, 0.00)",
                                    "(0.33, 0.03, 0.00)",
                                    "(0.48, 0.04, 0.00)",
                                    "(0.74, 0.85, 0.00)",
                                    "(0.73, 1.00, 0.00)",
                                    "(0.86, 1.00, 0.00)",
                                    "(1.01, 1.00, 0.00)",
                                    "(1.16, 1.00, 0.00)",
                                    "(1.16, 0.85, 0.00)",
                                    "(1.17, 0.71, 0.00)",
                                    "(0.74, 0.71, 0.00)",
                                    "(0.75, 0.56, 0.00)",
                                    "(0.76, 0.42, 0.00)",
                                    "(0.77, 0.29, 0.00)",
                                    "(0.77, 0.15, 0.00)",
                                    "(0.77, 0.02, 0.00)",
                                    "(0.92, 0.02, 0.00)",
                                    "(1.06, 0.03, 0.00)",
                                    "(1.19, 0.03, 0.00)",
                                    "(1.17, 0.56, 0.00)",
                                    "(1.18, 0.41, 0.00)",
                                    "(1.19, 0.26, 0.00)",
                                    "(1.19, 0.16, 0.00)",
                                    "(1.45, 0.85, 0.00)",
                                    "(1.44, 1.00, 0.00)",
                                    "(1.57, 1.00, 0.00)",
                                    "(1.72, 1.00, 0.00)",
                                    "(1.87, 1.00, 0.00)",
                                    "(1.87, 0.85, 0.00)",
                                    "(1.87, 0.71, 0.00)",
                                    "(1.77, 0.59, 0.00)",
                                    "(1.66, 0.50, 0.00)",
                                    "(1.55, 0.41, 0.00)",
                                    "(1.48, 0.29, 0.00)",
                                    "(1.48, 0.15, 0.00)",
                                    "(1.48, 0.02, 0.00)",
                                    "(1.63, 0.02, 0.00)",
                                    "(1.77, 0.03, 0.00)",
                                    "(1.92, 0.04, 0.00)",
                                    "(2.18, 0.85, 0.00)",
                                    "(2.17, 1.00, 0.00)",
                                    "(2.49, 0.56, 0.00)",
                                    "(2.34, 0.56, 0.00)",
                                    "(2.60, 1.00, 0.00)",
                                    "(2.60, 0.85, 0.00)",
                                    "(2.61, 0.71, 0.00)",
                                    "(2.18, 0.71, 0.00)",
                                    "(2.19, 0.56, 0.00)",
                                    "(2.63, 0.03, 0.00)",
                                    "(2.61, 0.56, 0.00)",
                                    "(2.62, 0.41, 0.00)",
                                    "(2.63, 0.26, 0.00)",
                                    "(2.63, 0.16, 0.00)"
                                }
                            }
                        };
                }
            }

            private static class SignPainter
            {
                private static readonly string ImagePath = $"{_ins.Name}/Images/";

                public static void UpdateNeonSign(NeonSign neonSign, string imageName)
                {
                    if (imageName != "")
                        ServerMgr.Instance.StartCoroutine(LoadImage(neonSign, imageName));

                    neonSign.UpdateFromInput(100, 0);
                }

                private static IEnumerator LoadImage(NeonSign neonSign, string imageName)
                {
                    string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + ImagePath + imageName + ".png";
                    using UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url);
                    yield return unityWebRequest.SendWebRequest();
                    
                    if (unityWebRequest.result == UnityWebRequest.Result.Success)
                    {
                        neonSign.EnsureInitialized();
                        Texture2D tex = DownloadHandlerTexture.GetContent(unityWebRequest);
                        byte[] bt = tex.EncodeToPNG();
                        Array.Resize(ref neonSign.textureIDs, 1);
                        uint textureIndex = 0;
                        uint textureId = FileStorage.server.Store(bt, FileStorage.Type.png, neonSign.net.ID, textureIndex);

                        neonSign.textureIDs[textureIndex] = textureId;
                        neonSign.SendNetworkUpdate();
                    }
                }
            }

            public static class PatternFireworkSignSaver
            {
                private static readonly HashSet<string> Symbol2 = new HashSet<string>
                {
                    "(0.01, 0.85, 0.00)",
                    "(0.00, 1.00, 0.00)",
                    "(0.13, 1.00, 0.00)",
                    "(0.28, 1.00, 0.00)",
                    "(0.43, 1.00, 0.00)",
                    "(0.43, 0.85, 0.00)",
                    "(0.43, 0.71, 0.00)",
                    "(0.33, 0.59, 0.00)",
                    "(0.22, 0.50, 0.00)",
                    "(0.11, 0.41, 0.00)",
                    "(0.04, 0.29, 0.00)",
                    "(0.04, 0.15, 0.00)",
                    "(0.04, 0.02, 0.00)",
                    "(0.19, 0.02, 0.00)",
                    "(0.33, 0.03, 0.00)",
                    "(0.48, 0.04, 0.00)",
                };

                private static readonly HashSet<string> Symbol0 = new HashSet<string>
                {
                    "(0.01, 0.85, 0.00)",
                    "(0.00, 1.00, 0.00)",
                    "(0.13, 1.00, 0.00)",
                    "(0.28, 1.00, 0.00)",
                    "(0.43, 1.00, 0.00)",
                    "(0.43, 0.85, 0.00)",
                    "(0.44, 0.71, 0.00)",
                    "(0.01, 0.71, 0.00)",
                    "(0.02, 0.56, 0.00)",
                    "(0.03, 0.42, 0.00)",
                    "(0.04, 0.29, 0.00)",
                    "(0.04, 0.15, 0.00)",
                    "(0.04, 0.02, 0.00)",
                    "(0.19, 0.02, 0.00)",
                    "(0.33, 0.03, 0.00)",
                    "(0.46, 0.03, 0.00)",
                    "(0.44, 0.56, 0.00)",
                    "(0.45, 0.41, 0.00)",
                    "(0.46, 0.26, 0.00)",
                    "(0.46, 0.16, 0.00)",
                };

                private static readonly HashSet<string> Symbol4 = new HashSet<string>()
                {
                    "(0.01, 0.85, 0.00)",
                    "(0.00, 1.00, 0.00)",
                    "(0.32, 0.56, 0.00)",
                    "(0.17, 0.56, 0.00)",
                    "(0.43, 1.00, 0.00)",
                    "(0.43, 0.85, 0.00)",
                    "(0.44, 0.71, 0.00)",
                    "(0.01, 0.71, 0.00)",
                    "(0.02, 0.56, 0.00)",
                    "(0.46, 0.03, 0.00)",
                    "(0.44, 0.56, 0.00)",
                    "(0.45, 0.41, 0.00)",
                    "(0.46, 0.26, 0.00)",
                    "(0.46, 0.16, 0.00)",
                };

                public static void UpdatePatternFirework(PatternFirework patternFirework)
                {
                    patternFirework.Design?.Dispose();
                    patternFirework.MaxStars = 1000;
                    patternFirework.Design = new Design();
                    patternFirework.Design.stars = new List<Star>();

                    Print2024(patternFirework);
                    patternFirework.SendNetworkUpdateImmediate();
                }

                private static void Print2024(PatternFirework patternFirework)
                {
                    float x0 = -2;

                    PrintSymbol(Symbol2, patternFirework, ref x0);
                    PrintSymbol(Symbol0, patternFirework, ref x0);
                    PrintSymbol(Symbol2, patternFirework, ref x0);
                    PrintSymbol(Symbol4, patternFirework, ref x0);
                }

                private static void PrintSymbol(HashSet<string> symbol, PatternFirework patternFirework, ref float x0)
                {
                    float newX0 = float.MinValue;

                    foreach (string coord in symbol)
                    {
                        Vector3 position = coord.ToVector3();

                        if (position.x + x0 > newX0)
                            newX0 = position.x + x0;

                        patternFirework.Design.stars.Add
                        (
                            new Star
                            {
                                color = new Color(1, 0, 0),
                                position = new Vector2(position.x + x0, position.y)
                            }
                        );
                    }

                    x0 = newX0 + 0.25f;
                }

                public static void ShowStarsCoordinatesOfRegularPaint(PatternFirework patternFirework)
                {
                    foreach (Star start in patternFirework.Design.stars)
                    {
                        Vector3 starPosition = new Vector3(start.position.x, start.position.y, 0) + new Vector3(1, 0, 0);
                        _ins.Puts(starPosition.ToString());
                    }
                }

                public static void ShowStarsCoordinatesOfCustomPaint(PatternFirework patternFirework)
                {
                    foreach (Star start in patternFirework.Design.stars)
                    {
                        Vector3 starPosition = new Vector3(start.position.x, start.position.y, 0) + new Vector3(2, 0, 0);
                        _ins.Puts(starPosition.ToString());
                    }
                }
            }
        }

        private class EventHeli : FacepunchBehaviour
        {
            public HeliConfig HeliConfig;
            private PatrolHelicopter _patrolHelicopter;
            private Vector3 _patrolPosition;
            private int _outsideTime;
            private bool _isFollowing;
            public ulong lastAttackedPlayer;

            public static void SpawnHeli(HeliConfig heliConfig)
            {
                Vector3 position = _ins._eventController.GetEventPosition() + Vector3.up * heliConfig.Height;

                PatrolHelicopter patrolHelicopter = BuildManager.SpawnRegularEntity("assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab", position, Quaternion.identity) as PatrolHelicopter;
                patrolHelicopter.transform.position = position;
                _ins._eventHeli = patrolHelicopter.gameObject.AddComponent<EventHeli>();
                _ins._eventHeli.Init(heliConfig, patrolHelicopter);
                
                EntityPresetInfo.Attach(patrolHelicopter, heliConfig.PresetName);
            }

            public static EventHeli GetEventHeliByNetId(ulong netID)
            {
                if (_ins._eventHeli != null && _ins._eventHeli._patrolHelicopter.IsExists() && _ins._eventHeli._patrolHelicopter.net != null && _ins._eventHeli._patrolHelicopter.net.ID.Value == netID)
                    return _ins._eventHeli;

                return null;
            }

            public static bool IsEventHeliAlive()
            {
                return _ins._eventHeli != null && _ins._eventHeli._patrolHelicopter.IsExists();
            }

            public static HashSet<ulong> GetAliveHeliesNetIds()
            {
                HashSet<ulong> helies = new HashSet<ulong>();

                if (_ins._eventHeli != null && _ins._eventHeli._patrolHelicopter != null && _ins._eventHeli._patrolHelicopter.net != null)
                    helies.Add(_ins._eventHeli._patrolHelicopter.net.ID.Value);

                return helies;
            }

            private void Init(HeliConfig heliConfig, PatrolHelicopter patrolHelicopter)
            {
                HeliConfig = heliConfig;
                _patrolHelicopter = patrolHelicopter;
                UpdateHelicopter();
                StartFollowing();
                patrolHelicopter.InvokeRepeating(UpdatePosition, 1, 1);
            }

            private void UpdateHelicopter()
            {
                _patrolHelicopter.startHealth = HeliConfig.Hp;
                _patrolHelicopter.InitializeHealth(HeliConfig.Hp, HeliConfig.Hp);
                _patrolHelicopter.maxCratesToSpawn = Random.Range(HeliConfig.MinCratesAmount, HeliConfig.MaxCratesAmount + 1);
                _patrolHelicopter.bulletDamage = HeliConfig.BulletDamage;
                _patrolHelicopter.bulletSpeed = HeliConfig.BulletSpeed;

                PatrolHelicopter.weakspot[] weakspots = _patrolHelicopter.weakspots;
                if (weakspots == null || weakspots.Length <= 1)
                    return;

                weakspots[0].maxHealth = HeliConfig.MainRotorHealth;
                weakspots[0].health = HeliConfig.MainRotorHealth;
                weakspots[1].maxHealth = HeliConfig.RearRotorHealth;
                weakspots[1].health = HeliConfig.RearRotorHealth;
            }

            private void UpdatePosition()
            {
                _patrolHelicopter.myAI.spawnTime = Time.realtimeSinceStartup;

                if (_patrolHelicopter.myAI._currentState is PatrolHelicopterAI.aiState.DEATH or PatrolHelicopterAI.aiState.STRAFE)
                    return;

                if (_ins._eventController.IsStopped())
                {
                    if (_isFollowing)
                        StartPatrol();
                }
                else if (!_isFollowing)
                    StartFollowing();

                if (_isFollowing)
                    DoFollowing();
                else
                    DoPatrol();
            }

            private void DoFollowing()
            {
                Vector3 position = _ins._eventController.GetEventPosition() + Vector3.up * HeliConfig.Height;
                _patrolHelicopter.myAI.State_Move_Enter(position);
            }

            private void DoPatrol()
            {
                if (_patrolHelicopter.myAI.leftGun.HasTarget() || _patrolHelicopter.myAI.rightGun.HasTarget())
                {
                    if (Vector3.Distance(_patrolPosition, _patrolHelicopter.transform.position) > HeliConfig.Distance)
                    {
                        _outsideTime++;

                        if (_outsideTime > HeliConfig.OutsideTime)
                        {
                            _patrolHelicopter.myAI.State_Move_Enter(_patrolPosition);
                        }
                    }
                    else
                    {
                        _outsideTime = 0;
                        _patrolHelicopter.myAI.ClearAimTarget();
                        _patrolHelicopter.myAI.leftGun.ClearTarget();
                        _patrolHelicopter.myAI.rightGun.ClearTarget();
                    }
                }
                else if (Vector3.Distance(_patrolPosition, _patrolHelicopter.transform.position) > HeliConfig.Distance)
                {
                    _patrolHelicopter.myAI.State_Move_Enter(_patrolPosition);
                    _outsideTime = 0;
                }
                else
                    _outsideTime = 0;
            }

            private void StartFollowing()
            {
                _isFollowing = true;
            }

            private void StartPatrol()
            {
                _isFollowing = false;
                _outsideTime = 0;
                _patrolPosition = _ins._eventController.GetEventPosition() + Vector3.up * HeliConfig.Height;
            }

            public bool IsHeliCanTarget()
            {
                return _ins._eventController.IsAggressive();
            }

            public void OnHeliAttacked(ulong userId)
            {
                if (_patrolHelicopter.myAI.isDead)
                    return;

                lastAttackedPlayer = userId;
            }

            public void Kill()
            {
                if (_patrolHelicopter.IsExists())
                    _patrolHelicopter.Kill();
            }

            public static void ClearData()
            {
                if (_ins._eventHeli != null)
                    _ins._eventHeli.Kill();

                _ins._eventHeli = null;
            }
        }

        private class CustomBradley : BradleyAPC
        {
            public static CustomBradley SpawnCustomBradley(Vector3 localPosition, Vector3 localRotation, BaseEntity parentEntity, BradleyConfig bradleyConfig)
            {
                BradleyAPC bradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", parentEntity.transform.position, Quaternion.identity) as BradleyAPC;
                bradley.skinID = 755446;
                bradley.enableSaving = false;
                bradley.ScientistSpawns.Clear();

                CustomBradley customBradley = bradley.gameObject.AddComponent<CustomBradley>();
                BuildManager.CopySerializableFields(bradley, customBradley);
                bradley.StopAllCoroutines();
                DestroyImmediate(bradley, true);

                BuildManager.SetParent(parentEntity, customBradley, localPosition, localRotation);
                customBradley.Spawn();

                TriggerHurtNotChild[] triggerHurts = customBradley.GetComponentsInChildren<TriggerHurtNotChild>();
                foreach (TriggerHurtNotChild triggerHurt in triggerHurts)
                    triggerHurt.enabled = false;
                
                EntityPresetInfo.Attach(customBradley, bradleyConfig.PresetName);
                
                return customBradley;
            }

            private new void FixedUpdate()
            {
                SetFlagLocal(Flags.Reserved5, TOD_Sky.Instance.IsNight);

                if (_ins._eventController.IsAggressive())
                {
                    UpdateTarget();
                    DoWeapons();
                }

                DoHealing();
                DoWeaponAiming();
                SendNetworkUpdate();

                if (mainGunTarget == null)
                    desiredAimVector = transform.forward;
            }

            private void UpdateTarget()
            {
                if (targetList.Count > 0)
                {
                    TargetInfo targetInfo = targetList[0];

                    if (targetInfo == null)
                    {
                        mainGunTarget = null;
                        return;
                    }

                    BasePlayer player = targetInfo.entity as BasePlayer;

                    if (player.IsRealPlayer() && targetInfo.IsVisible())
                        mainGunTarget = targetList[0].entity as BaseCombatEntity;
                    else
                        mainGunTarget = null;
                }
                else
                {
                    mainGunTarget = null;
                }
            }
        }
        
        private class PlayerDamageController
        {
            private const float CheckInterval = 5f;
            private float _nextCheckTime;
            private bool _cachedResult;
            private uint _lastBuildingId;
            private bool _lastBuildingResult;

            public static PlayerDamageController GetOrUpdatePlayerDamageController(ulong userId)
            {
                if (!_ins._playerDamageControllers.TryGetValue(userId, out PlayerDamageController controller))
                {
                    controller = new PlayerDamageController();
                    _ins._playerDamageControllers[userId] = controller;
                }

                return controller;
            }

            public bool IsDamageBlocked(BasePlayer player)
            {
                if (Time.realtimeSinceStartup < _nextCheckTime)
                    return _cachedResult;
                
                _nextCheckTime = Time.realtimeSinceStartup + CheckInterval;
                _cachedResult = IsPlayerBaseBlockDamage(player);
                return _cachedResult;
            }

            private bool IsPlayerBaseBlockDamage(BasePlayer player)
            {
                BuildingBlock buildingBlock = GetBlockUnderPlayer(player);
                if (buildingBlock == null)
                    return false;
                
                if (buildingBlock.buildingID == _lastBuildingId)
                    return _lastBuildingResult;

                _lastBuildingId = buildingBlock.buildingID;
                _lastBuildingResult = false;
                BuildingManager.Building building = BuildingManager.server.GetBuilding(buildingBlock.buildingID);
                if (building == null || building.buildingBlocks == null)
                    return false;
                
                BuildingBlock highestFoundation = null;
                float highestY = 0;
                
                foreach (BuildingBlock block in building.buildingBlocks)
                {
                    if (block == null || block.IsDestroyed)
                        continue;

                    if (block.prefabID is not (72949757 or 3234260181))
                        continue;

                    if (block.transform.position.y > highestY)
                    {
                        highestY = block.transform.position.y;
                        highestFoundation = block;
                    }
                }
                
                if (highestFoundation == null)
                    return false;
                
                Vector3 highestPosition = highestFoundation.transform.position;
                Vector3 groundPosition = PositionDefiner.GetGroundPositionInPoint(highestPosition, 1 << 23);

                float deltaY = highestPosition.y - groundPosition.y;
                if (deltaY >= 5f)
                {
                    _lastBuildingResult = true;
                    return true;
                }
                
                return false;
            }
            
            private BuildingBlock GetBlockUnderPlayer(BasePlayer player)
            {
                if (Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f, 1 << 21))
                    return hit.GetEntity() as BuildingBlock;

                return null;
            }
        }

        private static class SpawnPositionFinder
        {
            public static PositionData GetSpawnPositionData(bool isUnderGround)
            {
                PositionData positionData = null;

                if (_ins._config.MainConfig.CustomSpawnPointConfig.IsEnabled)
                {
                    positionData = CustomSpawnPointFinder.GetSpawnPosition();

                    if (positionData == null)
                        NotifyManager.PrintError(null, "CustomSpawnPoint_Exeption");
                }

                if (positionData == null && !isUnderGround)
                    positionData = AboveGroundPositionFinder.GetSpawnPosition();

                if (positionData == null)
                    positionData = UnderGroundPositionFinder.GetSpawnPosition();

                if (positionData == null)
                    NotifyManager.PrintError(null, "Rail_Exeption");

                return positionData;
            }

            public static bool IsRailsInPosition(Vector3 position)
            {
                return TrainTrackSpline.TryFindTrackNear(position, 1, out TrainTrackSpline _, out float _);
            }

            private static bool IsSpawnPositionExist(Vector3 position)
            {
                foreach (Collider collider in Physics.OverlapSphere(position, 10))
                {
                    BaseEntity entity = collider.ToBaseEntity();

                    if (!entity.IsExists())
                        continue;

                    if (entity is TrainCar)
                        return false;
                }

                return true;
            }

            private static void ClearSpawnPoint(Vector3 position)
            {
                foreach (Collider collider in Physics.OverlapSphere(position, 10))
                {
                    TrainCar trainCar = collider.ToBaseEntity() as TrainCar;

                    if (!trainCar.IsExists())
                        continue;

                    if (trainCar.GetFuelSystem() is EntityFuelSystem entityFuelSystem && entityFuelSystem.cachedHasFuel)
                        continue;

                    trainCar.Kill();
                }
            }

            private static class AboveGroundPositionFinder
            {
                public static PositionData GetSpawnPosition()
                {
                    if (TerrainMeta.Path.Rails == null || TerrainMeta.Path.Rails.Count == 0)
                        return null;

                    PositionData positionData = null;
                    PathList pathList = TerrainMeta.Path.Rails.Max(x => x.Path.Length);

                    if (pathList == null || pathList.Path == null || pathList.Path.Points.Length == 0)
                        return null;

                    for (int i = 0; i < 100 && positionData == null; i++)
                        positionData = TryGetSpawnPositionDataOnPathList(pathList);

                    return positionData;
                }

                private static PositionData TryGetSpawnPositionDataOnPathList(PathList pathList)
                {
                    Vector3 position = pathList.Path.Points.GetRandom();

                    if (!TrainTrackSpline.TryFindTrackNear(position, 1, out TrainTrackSpline trainTrackSpline, out float _))
                        return null;

                    float length = trainTrackSpline.GetLength();

                    if (length < 65)
                        return null;

                    float randomLength = Random.Range(60f, length - 60f);
                    position = trainTrackSpline.GetPointAndTangentCubicHermiteWorld(randomLength, out Vector3 rotationVector) + (Vector3.up * 0.5f);
                    Quaternion rotation = Quaternion.LookRotation(rotationVector);

                    if (!IsSpawnPositionExist(position))
                        return null;

                    return new PositionData(position, rotation);
                }
            }

            private static class UnderGroundPositionFinder
            {
                public static PositionData GetSpawnPosition()
                {
                    PositionData positionData = null;

                    for (int i = 0; i < 100 && positionData == null; i++)
                    {
                        DungeonGridCell dungeonGridCell = TerrainMeta.Path.DungeonGridCells.GetRandom();
                        positionData = TryGetPositionDataFromDungeon(dungeonGridCell);
                    }

                    return positionData;
                }

                private static PositionData TryGetPositionDataFromDungeon(DungeonGridCell dungeonGridCell)
                {
                    if (!TrainTrackSpline.TryFindTrackNear(dungeonGridCell.transform.position, 3, out TrainTrackSpline trainTrackSpline, out float _))
                        return null;

                    float length = trainTrackSpline.GetLength();

                    if (length < 65)
                        return null;

                    float randomLength = Random.Range(60f, length - 60f);
                    Vector3 position = trainTrackSpline.GetPointAndTangentCubicHermiteWorld(randomLength, out Vector3 rotationVector) + (Vector3.up * 0.5f);
                    Quaternion rotation = Quaternion.LookRotation(rotationVector);

                    if (!IsSpawnPositionExist(position))
                        return null;

                    return new PositionData(position, rotation);
                }
            }

            private static class CustomSpawnPointFinder
            {
                public static PositionData GetSpawnPosition()
                {
                    List<LocationConfig> suitablePoints = Pool.Get<List<LocationConfig>>();

                    foreach (LocationConfig locationConfig in _ins._config.MainConfig.CustomSpawnPointConfig.Points)
                    {
                        Vector3 position = locationConfig.Position.ToVector3();

                        if (IsRailsInPosition(position) && IsSpawnPositionExist(position))
                            suitablePoints.Add(locationConfig);
                    }

                    if (suitablePoints.Count == 0)
                        foreach (LocationConfig locationConfig in _ins._config.MainConfig.CustomSpawnPointConfig.Points)
                            if (IsRailsInPosition(locationConfig.Position.ToVector3()))
                                suitablePoints.Add(locationConfig);

                    PositionData positionData = null;
                    LocationConfig randomLocationConfig = suitablePoints.GetRandom();

                    if (randomLocationConfig != null)
                    {
                        Vector3 position = randomLocationConfig.Position.ToVector3();
                        ClearSpawnPoint(position);
                        positionData = new PositionData(position, Quaternion.Euler(randomLocationConfig.Rotation.ToVector3()));
                    }

                    Pool.FreeUnmanaged(ref suitablePoints);
                    return positionData;
                }
            }
        }

        private class ZoneController : FacepunchBehaviour
        {
            private static ZoneController _zoneController;
            private SphereCollider _sphereCollider;
            private Coroutine _zoneUpdateCoroutine;
            private readonly HashSet<BaseEntity> _spheres = new HashSet<BaseEntity>();
            private readonly HashSet<BasePlayer> _playersInZone = new HashSet<BasePlayer>();

            public static void CreateZone(BasePlayer externalOwner = null)
            {
                TryDeleteZone();
                Vector3 position = _ins._eventController.GetEventPosition();

                if (position == Vector3.zero)
                    return;

                GameObject gameObject = new GameObject
                {
                    transform =
                    {
                        position = position
                    },
                    layer = (int)Rust.Layer.Reserved1
                };

                _zoneController = gameObject.AddComponent<ZoneController>();
                _zoneController.Init(externalOwner);
            }

            public static bool IsZoneCreated()
            {
                return _zoneController != null;
            }

            public static bool IsPlayerInZone(ulong userID)
            {
                return _zoneController != null && _zoneController._playersInZone.Any(x => x != null && x.userID == userID);
            }

            public static bool IsAnyPlayerInEventZone()
            {
                return _zoneController != null && _zoneController._playersInZone.Any(x => x.IsExists() && !x.IsSleeping());
            }

            public static void OnPlayerLeaveZone(BasePlayer player)
            {
                if (_zoneController == null)
                    return;

                Interface.CallHook($"OnPlayerExit{_ins.Name}", player);
                _zoneController._playersInZone.Remove(player);
                GuiManager.DestroyGui(player);

                if (_ins._config.ZoneConfig.IsPvpZone)
                {
                    if (_ins.plugins.Exists("DynamicPVP") && (bool)_ins.DynamicPVP.Call("IsPlayerInPVPDelay", (ulong)player.userID))
                        return;

                    NotifyManager.SendMessageToPlayer(player, "ExitPVP", _ins._config.Prefix);
                }
            }

            public static bool IsEventPosition(Vector3 position)
            {
                Vector3 eventPosition = _ins._eventController.GetEventPosition();
                return Vector3.Distance(position, eventPosition) < _ins._eventController.EventConfig.ZoneRadius;
            }

            public static HashSet<BasePlayer> GetAllPlayersInZone()
            {
                if (_zoneController == null)
                    return new HashSet<BasePlayer>();
                else
                    return _zoneController._playersInZone;
            }

            private void Init(BasePlayer externalOwner)
            {
                CreateTriggerSphere();
                CreateSpheres();

                if (PveModeManager.IsPveModeReady())
                    PveModeManager.CreatePveModeZone(this.transform.position, externalOwner);

                _zoneUpdateCoroutine = ServerMgr.Instance.StartCoroutine(ZoneUpdateCoroutine());
            }

            private void CreateTriggerSphere()
            {
                _sphereCollider = gameObject.AddComponent<SphereCollider>();
                _sphereCollider.isTrigger = true;
                _sphereCollider.radius = _ins._eventController.EventConfig.ZoneRadius;
            }

            private void CreateSpheres()
            {
                if (_ins._config.ZoneConfig.IsDome)
                    for (int i = 0; i < _ins._config.ZoneConfig.Darkening; i++)
                        CreateSphere("assets/prefabs/visualization/sphere.prefab");

                if (_ins._config.ZoneConfig.IsColoredBorder)
                {
                    string spherePrefab = _ins._config.ZoneConfig.BorderColor == 0 ? "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab" : _ins._config.ZoneConfig.BorderColor == 1 ? "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" :
                         _ins._config.ZoneConfig.BorderColor == 2 ? "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab" : "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab";

                    for (int i = 0; i < _ins._config.ZoneConfig.Brightness; i++)
                        CreateSphere(spherePrefab);
                }
            }

            private void CreateSphere(string prefabName)
            {
                BaseEntity sphere = GameManager.server.CreateEntity(prefabName, gameObject.transform.position);
                SphereEntity entity = sphere.GetComponent<SphereEntity>();
                entity.currentRadius = _ins._eventController.EventConfig.ZoneRadius * 2;
                entity.lerpSpeed = 0f;
                sphere.enableSaving = false;
                sphere.Spawn();
                _spheres.Add(sphere);
            }

            private void OnTriggerEnter(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();
                if (player.IsRealPlayer())
                {
                    Interface.CallHook($"OnPlayerEnter{_ins.Name}", player);
                    _playersInZone.Add(player);

                    if (_ins._config.ZoneConfig.IsPvpZone)
                        NotifyManager.SendMessageToPlayer(player, "EnterPVP", _ins._config.Prefix);

                    GuiManager.CreateGui(player, NotifyManager.GetTimeMessage(player.UserIDString, _ins._eventController.GetEventTime()), _ins._eventController.GetCountOfUnlootedCrates().ToString(), NpcSpawnManager.GetEventNpcCount().ToString());
                }
            }

            private void OnTriggerExit(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();

                if (player.IsRealPlayer())
                    OnPlayerLeaveZone(player);
            }

            private IEnumerator ZoneUpdateCoroutine()
            {
                while (_zoneController != null)
                {
                    int countOfCrates = _ins._eventController.GetCountOfUnlootedCrates();
                    int countOfGuardNpc = NpcSpawnManager.GetEventNpcCount();

                    foreach (BasePlayer player in _playersInZone)
                        if (player != null)
                            GuiManager.CreateGui(player, NotifyManager.GetTimeMessage(player.UserIDString, _ins._eventController.GetEventTime()), countOfCrates.ToString(), countOfGuardNpc.ToString());

                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            public static void TryDeleteZone()
            {
                if (_zoneController != null)
                    _zoneController.DeleteZone();
            }

            private void DeleteZone()
            {
                foreach (BaseEntity sphere in _spheres)
                    if (sphere != null && !sphere.IsDestroyed)
                        sphere.Kill();

                if (_zoneUpdateCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_zoneUpdateCoroutine);

                GuiManager.DestroyAllGui();
                PveModeManager.DeletePveModeZone();
                Destroy(gameObject);
            }
        }

        private static class PveModeManager
        {
            private static HashSet<ulong> _pveModeOwners = new HashSet<ulong>();
            private static BasePlayer _owner;
            private static float _lastZoneDeleteTime;

            public static bool IsPveModeReady()
            {
                return _ins._config.SupportedPluginsConfig.PveMode.Enable && _ins.plugins.Exists("PveMode");
            }

            public static BasePlayer UpdateAndGetEventOwner()
            {
                if (_ins._eventController.IsStopped())
                    return _owner;

                float timeScienceLastZoneDelete = Time.realtimeSinceStartup - _lastZoneDeleteTime;

                if (timeScienceLastZoneDelete > _ins._config.SupportedPluginsConfig.PveMode.TimeExitOwner)
                    _owner = null;

                return _owner;
            }

            public static void CreatePveModeZone(Vector3 position, BasePlayer externalOwner)
            {
                Dictionary<string, object> config = GetPveModeConfig();

                HashSet<ulong> npcs = NpcSpawnManager.GetEventNpcNetIds();
                HashSet<ulong> bradleys = _ins._eventController.GetAliveBradleysNetIds();
                HashSet<ulong> helicopters = EventHeli.GetAliveHeliesNetIds();
                HashSet<ulong> crates = _ins._eventController.GetEventCratesNetIDs();
                HashSet<ulong> turrets = _ins._eventController.GetAliveTurretsNetIds();

                BasePlayer playerOwner = GetEventOwner();

                if (playerOwner == null)
                    playerOwner = externalOwner;

                _ins.PveMode.Call("EventAddPveMode", _ins.Name, config, position, _ins._eventController.EventConfig.ZoneRadius, crates, npcs, bradleys, helicopters, turrets, _pveModeOwners, playerOwner);
            }

            private static BasePlayer GetEventOwner()
            {
                BasePlayer playerOwner = null;

                float timeScienceLastZoneDelete = Time.realtimeSinceStartup - _lastZoneDeleteTime;

                if (_owner != null && (_ins._eventController.IsStopped() || timeScienceLastZoneDelete < _ins._config.SupportedPluginsConfig.PveMode.TimeExitOwner))
                    playerOwner = _owner;

                return playerOwner;
            }

            private static Dictionary<string, object> GetPveModeConfig()
            {
                return new Dictionary<string, object>
                {
                    ["Damage"] = _ins._config.SupportedPluginsConfig.PveMode.Damage,
                    ["ScaleDamage"] = _ins._config.SupportedPluginsConfig.PveMode.ScaleDamage,
                    ["LootCrate"] = _ins._config.SupportedPluginsConfig.PveMode.LootCrate,
                    ["HackCrate"] = _ins._config.SupportedPluginsConfig.PveMode.HackCrate,
                    ["LootNpc"] = _ins._config.SupportedPluginsConfig.PveMode.LootNpc,
                    ["DamageNpc"] = _ins._config.SupportedPluginsConfig.PveMode.DamageNpc,
                    ["DamageTank"] = _ins._config.SupportedPluginsConfig.PveMode.DamageTank,
                    ["DamageHelicopter"] = _ins._config.SupportedPluginsConfig.PveMode.DamageHeli,
                    ["DamageTurret"] = _ins._config.SupportedPluginsConfig.PveMode.DamageTurret,
                    ["TargetNpc"] = _ins._config.SupportedPluginsConfig.PveMode.TargetNpc,
                    ["TargetTank"] = _ins._config.SupportedPluginsConfig.PveMode.TargetTank,
                    ["TargetHelicopter"] = _ins._config.SupportedPluginsConfig.PveMode.TargetHeli,
                    ["TargetTurret"] = _ins._config.SupportedPluginsConfig.PveMode.TargetTurret,
                    ["CanEnter"] = _ins._config.SupportedPluginsConfig.PveMode.CanEnter,
                    ["CanEnterCooldownPlayer"] = _ins._config.SupportedPluginsConfig.PveMode.CanEnterCooldownPlayer,
                    ["TimeExitOwner"] = _ins._config.SupportedPluginsConfig.PveMode.TimeExitOwner,
                    ["AlertTime"] = _ins._config.SupportedPluginsConfig.PveMode.AlertTime,
                    ["RestoreUponDeath"] = _ins._config.SupportedPluginsConfig.PveMode.RestoreUponDeath,
                    ["CooldownOwner"] = _ins._config.SupportedPluginsConfig.PveMode.Cooldown,
                    ["Darkening"] = 0
                };
            }

            public static void DeletePveModeZone()
            {
                if (!IsPveModeReady())
                    return;

                _lastZoneDeleteTime = Time.realtimeSinceStartup;
                _pveModeOwners = (HashSet<ulong>)_ins.PveMode.Call("GetEventOwners", _ins.Name);

                if (_pveModeOwners == null)
                    _pveModeOwners = new HashSet<ulong>();

                ulong userId = (ulong)_ins.PveMode.Call("GetEventOwner", _ins.Name);
                OnNewOwnerSet(userId);

                _ins.PveMode.Call("EventRemovePveMode", _ins.Name, false);
            }

            private static void OnNewOwnerSet(ulong userId)
            {
                if (userId == 0)
                    return;

                BasePlayer player = BasePlayer.FindByID(userId);
                OnNewOwnerSet(player);
            }

            public static void OnNewOwnerSet(BasePlayer player)
            {
                _owner = player;
            }

            public static void OnOwnerDeleted()
            {
                _owner = null;
            }

            public static void OnEventEnd()
            {
                if (IsPveModeReady())
                    _ins.PveMode.Call("EventAddCooldown", _ins.Name, _pveModeOwners, _ins._config.SupportedPluginsConfig.PveMode.Cooldown);

                _lastZoneDeleteTime = 0;
                _pveModeOwners.Clear();
                _owner = null;
            }

            public static bool IsPveModeBlockAction(BasePlayer player)
            {
                if (IsPveModeReady())
                    return _ins.PveMode.Call("CanActionEventNoMessage", _ins.Name, player) != null;

                return false;
            }

            public static bool IsPveModeBlockInteract(BasePlayer player)
            {
                if (!IsPveModeReady())
                    return false;

                BasePlayer eventOwner = GetEventOwner();

                if ((_ins._config.SupportedPluginsConfig.PveMode.NoInteractIfCooldownAndNoOwners && eventOwner == null) || _ins._config.SupportedPluginsConfig.PveMode.NoDealDamageIfCooldownAndTeamOwner)
                    return !(bool)_ins.PveMode.Call("CanTimeOwner", _ins.Name, (ulong)player.userID, _ins._config.SupportedPluginsConfig.PveMode.Cooldown);

                return false;
            }

            public static bool IsPveModeBlockLooting(BasePlayer player)
            {
                if (!IsPveModeReady())
                    return false;

                BasePlayer eventOwner = GetEventOwner();

                if (eventOwner == null)
                    return false;

                if (_ins._config.SupportedPluginsConfig.PveMode.CanLootOnlyOwner && !IsTeam(player, eventOwner.userID))
                    return true;

                return false;
            }

            private static bool IsTeam(BasePlayer player, ulong targetId)
            {
                if (player.userID == targetId)
                    return true;

                if (player.currentTeam != 0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);

                    if (playerTeam == null)
                        return false;

                    if (playerTeam.members.Contains(targetId))
                        return true;
                }
                return false;
            }
        }

        private class EventMapMarker : FacepunchBehaviour
        {
            private static EventMapMarker _eventMapMarker;

            private MapMarkerGenericRadius _radiusMarker;
            private VendingMachineMapMarker _vendingMarker;
            private Coroutine _updateCounter;

            public static void CreateMarker()
            {
                if (!_ins._config.MarkerConfig.Enable) return;

                GameObject gameObject = new GameObject
                {
                    layer = (int)Rust.Layer.Reserved1
                };
                _eventMapMarker = gameObject.AddComponent<EventMapMarker>();
                _eventMapMarker.Init();
            }

            private void Init()
            {
                Vector3 eventPosition = _ins._eventController.GetEventPosition();
                CreateRadiusMarker(eventPosition);
                CreateVendingMarker(eventPosition);
                _updateCounter = ServerMgr.Instance.StartCoroutine(MarkerUpdateCounter());
            }

            private void CreateRadiusMarker(Vector3 position)
            {
                if (!_ins._config.MarkerConfig.UseRingMarker)
                    return;

                _radiusMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", position) as MapMarkerGenericRadius;
                _radiusMarker.enableSaving = false;
                _radiusMarker.Spawn();
                _radiusMarker.radius = _ins._config.MarkerConfig.Radius;
                _radiusMarker.alpha = _ins._config.MarkerConfig.Alpha;
                _radiusMarker.color1 = new Color(_ins._config.MarkerConfig.Color1.R, _ins._config.MarkerConfig.Color1.G, _ins._config.MarkerConfig.Color1.B);
                _radiusMarker.color2 = new Color(_ins._config.MarkerConfig.Color2.R, _ins._config.MarkerConfig.Color2.G, _ins._config.MarkerConfig.Color2.B);
            }

            private void CreateVendingMarker(Vector3 position)
            {
                if (!_ins._config.MarkerConfig.UseShopMarker)
                    return;

                _vendingMarker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", position) as VendingMachineMapMarker;
                _vendingMarker.Spawn();
                _vendingMarker.markerShopName = $"{_ins._eventController.EventConfig.DisplayName} ({NotifyManager.GetTimeMessage(null, _ins._eventController.GetEventTime())})";
            }

            private IEnumerator MarkerUpdateCounter()
            {
                while (EventLauncher.IsEventActive())
                {
                    Vector3 position = _ins._eventController.GetEventPosition();
                    UpdateVendingMarker(position);
                    UpdateRadiusMarker(position);
                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            private void UpdateRadiusMarker(Vector3 position)
            {
                if (!_radiusMarker.IsExists())
                    return;

                _radiusMarker.transform.position = position;
                _radiusMarker.SendUpdate();
                _radiusMarker.SendNetworkUpdate();
            }

            private void UpdateVendingMarker(Vector3 position)
            {
                if (!_vendingMarker.IsExists())
                    return;

                _vendingMarker.transform.position = position;
                BasePlayer pveModeEventOwner = PveModeManager.UpdateAndGetEventOwner();
                string displayEventOwnerName = _ins._config.SupportedPluginsConfig.PveMode.ShowEventOwnerNameOnMap && pveModeEventOwner != null ? GetMessage("Marker_EventOwner", null, pveModeEventOwner.displayName) : "";
                _vendingMarker.markerShopName = $"{_ins._eventController.EventConfig.DisplayName} ({NotifyManager.GetTimeMessage(null, _ins._eventController.GetEventTime())}) {displayEventOwnerName}";
                _vendingMarker.SetFlagLocal(BaseEntity.Flags.Busy, pveModeEventOwner == null);
                _vendingMarker.SendNetworkUpdate();
            }

            public static void DeleteMapMarker()
            {
                if (_eventMapMarker != null)
                    _eventMapMarker.Delete();
            }

            private void Delete()
            {
                if (_radiusMarker.IsExists())
                    _radiusMarker.Kill();

                if (_vendingMarker.IsExists())
                    _vendingMarker.Kill();

                if (_updateCounter != null)
                    ServerMgr.Instance.StopCoroutine(_updateCounter);

                Destroy(_eventMapMarker.gameObject);
            }
        }

        private static class NpcSpawnManager
        {
            private static readonly HashSet<ScientistNPC> EventNpcs = new HashSet<ScientistNPC>();

            public static int GetEventNpcCount()
            {
                return EventNpcs.Where(x => x.IsExists() && !x.isMounted).Count;
            }

            public static HashSet<ulong> GetEventNpcNetIds()
            {
                HashSet<ulong> result = new HashSet<ulong>();

                foreach (ScientistNPC scientistNpc in EventNpcs)
                    if (scientistNpc != null && scientistNpc.net != null)
                        result.Add(scientistNpc.net.ID.Value);

                return result;
            }

            public static ScientistNPC GetScientistByNetId(ulong netId)
            {
                return EventNpcs.FirstOrDefault(x => x != null && x.net != null && x.net.ID.Value == netId);
            }

            public static bool IsNpcSpawnReady()
            {
                if (!_ins.plugins.Exists("NpcSpawn"))
                {
                    _ins.PrintError("NpcSpawn plugin doesn`t exist! Please read the file ReadMe.txt. NPCs will not spawn!");
                    _ins.NextTick(() => Interface.Oxide.UnloadPlugin(_ins.Name));
                    return false;
                }
                else
                    return true;
            }

            public static bool IsEventNpc(ScientistNPC scientistNpc)
            {
                return scientistNpc != null && _ins._config.NpcConfigs.Any(x => x.DisplayName == scientistNpc.displayName);
            }

            public static ScientistNPC SpawnScientistNpc(string npcPresetName, Vector3 position, float healthFraction, bool isStationary, bool isPassive = false)
            {
                NpcConfig npcConfig = GetNpcConfigByPresetName(npcPresetName);
                if (npcConfig == null)
                {
                    NotifyManager.PrintError(null, "PresetNotFound_Exeption", npcPresetName);
                    return null;
                }

                ScientistNPC scientistNpc = SpawnScientistNpc(npcConfig, position, healthFraction, isStationary, isPassive);

                if (isStationary)
                    UpdateClothesWeight(scientistNpc);

                return scientistNpc;
            }

            private static ScientistNPC SpawnScientistNpc(NpcConfig npcConfig, Vector3 position, float healthFraction, bool isStationary, bool isPassive)
            {
                JObject baseNpcConfigObj = GetBaseNpcConfig(npcConfig, healthFraction, isStationary, isPassive);
                ScientistNPC scientistNpc = (ScientistNPC)_ins.NpcSpawn.Call("SpawnNpc", position, baseNpcConfigObj, isPassive);
                if (scientistNpc == null)
                    return null;
                
                EntityPresetInfo.Attach(scientistNpc, npcConfig.PresetName);
                EventNpcs.Add(scientistNpc);
                return scientistNpc;
            }

            public static NpcConfig GetNpcConfigByDisplayName(string displayName)
            {
                return _ins._config.NpcConfigs.FirstOrDefault(x => x.DisplayName == displayName);
            }

            public static NpcConfig GetNpcConfigByPresetName(string npcPresetName)
            {
                return _ins._config.NpcConfigs.FirstOrDefault(x => x.PresetName == npcPresetName);
            }

            private static JObject GetBaseNpcConfig(NpcConfig config, float healthFraction, bool isStationary, bool isPassive)
            {
                JArray wearItems = WagonCustomizer.GetCustomizeNpcWearSet();

                if (wearItems == null)
                {
                    wearItems = new JArray
                    {
                        config.WearItems.Select(x => new JObject
                        {
                            ["ShortName"] = x.ShortName,
                            ["SkinID"] = x.SkinID
                        })
                    };
                }

                return new JObject
                {
                    ["Name"] = config.DisplayName,
                    ["WearItems"] = wearItems,
                    ["BeltItems"] = isPassive ? new JArray() : new JArray { config.BeltItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["Amount"] = x.Amount, ["SkinID"] = x.SkinID, ["mods"] = new JArray { x.Mods.ToHashSet() }, ["Ammo"] = x.Ammo }) },
                    ["Kit"] = config.Kit,
                    ["Health"] = config.Health * healthFraction,
                    ["RoamRange"] = isStationary ? 0 : config.RoamRange,
                    ["ChaseRange"] = isStationary ? 0 : config.ChaseRange,
                    ["SenseRange"] = config.SenseRange,
                    ["ListenRange"] = config.SenseRange / 2,
                    ["AttackRangeMultiplier"] = config.AttackRangeMultiplier,
                    ["CheckVisionCone"] = config.CheckVisionCone,
                    ["VisionCone"] = config.VisionCone,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = config.DamageScale,
                    ["TurretDamageScale"] = config.TurretDamageScale,
                    ["AimConeScale"] = config.AimConeScale,
                    ["DisableRadio"] = config.DisableRadio,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = config.SleepDistance > 0,
                    ["SleepDistance"] = config.SleepDistance,
                    ["IgnoreWoundedPlayers"] = config.IgnoreWoundedPlayers,
                    ["Speed"] = isStationary ? 0 : config.Speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = string.Empty,
                    ["MemoryDuration"] = config.MemoryDuration,
                    ["States"] = isPassive ? new JArray("IdleState") : isStationary ? new JArray { "IdleState", "CombatStationaryState" } : config.BeltItems.Any(x => x.ShortName is "rocket.launcher" or "explosive.timed") ? new JArray { "RaidState", "RoamState", "ChaseState", "CombatState" } : new JArray { "RoamState", "ChaseState", "CombatState" },
                    ["IsRemoveCorpse"] = config.DeleteCorpse,
                };
            }

            private static void UpdateClothesWeight(ScientistNPC scientistNpc)
            {
                foreach (Item item in scientistNpc.inventory.containerWear.itemList)
                {
                    ItemModWearable component = item.info.GetComponent<ItemModWearable>();

                    if (component != null)
                        component.weight = 0;
                }
            }

            public static void ClearData(bool shouldKillNpcs)
            {
                if (shouldKillNpcs)
                    foreach (ScientistNPC scientistNpc in EventNpcs)
                        if (scientistNpc.IsExists())
                            scientistNpc.Kill();

                EventNpcs.Clear();
            }
        }

        private class PositionData
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public PositionData(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }
        }
        
        private static class BuildManager
        {
            private static void UpdateMeshColliders(BaseEntity entity)
            {
                MeshCollider[] meshColliders = entity.GetComponentsInChildren<MeshCollider>();

                foreach (MeshCollider meshCollider in meshColliders)
                {
                    meshCollider.convex = true;
                }
            }

            public static BaseEntity SpawnChildEntity(BaseEntity parentEntity, string prefabName, LocationConfig locationConfig, ulong skinId, bool isDecor)
            {
                Vector3 localPosition = locationConfig.Position.ToVector3();
                Vector3 localRotation = locationConfig.Rotation.ToVector3();
                return SpawnChildEntity(parentEntity, prefabName, localPosition, localRotation, skinId, isDecor);
            }

            public static BaseEntity SpawnRegularEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0, bool enableSaving = false)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, enableSaving);
                entity.Spawn();
                return entity;
            }

            public static BaseEntity SpawnStaticEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, false);
                DestroyUnnecessaryComponents(entity);

                StabilityEntity stabilityEntity = entity as StabilityEntity;
                if (stabilityEntity != null)
                    stabilityEntity.grounded = true;

                BaseCombatEntity baseCombatEntity = entity as BaseCombatEntity;
                if (baseCombatEntity != null)
                    baseCombatEntity.pickup.enabled = false;

                entity.Spawn();
                return entity;
            }

            public static BaseEntity SpawnChildEntity(BaseEntity parentEntity, string prefabName, Vector3 localPosition, Vector3 localRotation, ulong skinId = 0, bool isDecor = true, bool enableSaving = false)
            {
                BaseEntity entity = isDecor ? CreateDecorEntity(prefabName, parentEntity.transform.position, Quaternion.identity, skinId) : CreateEntity(prefabName, parentEntity.transform.position, Quaternion.identity, skinId, enableSaving);
                SetParent(parentEntity, entity, localPosition, localRotation);

                DestroyUnnecessaryComponents(entity);

                if (isDecor)
                    DestroyDecorComponents(entity);

                UpdateMeshColliders(entity);
                entity.Spawn();
                return entity;
            }

            public static void UpdateEntityMaxHealth(BaseCombatEntity baseCombatEntity, float maxHealth)
            {
                baseCombatEntity.startHealth = maxHealth;
                baseCombatEntity.InitializeHealth(maxHealth, maxHealth);
            }

            public static BaseEntity CreateEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId, bool enableSaving)
            {
                BaseEntity entity = GameManager.server.CreateEntity(prefabName, position, rotation);
                entity.enableSaving = enableSaving;
                entity.skinID = skinId;
                return entity;
            }

            private static BaseEntity CreateDecorEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0, bool enableSaving = false)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, enableSaving);

                BaseEntity trueBaseEntity = entity.gameObject.AddComponent<BaseEntity>();
                CopySerializableFields(entity, trueBaseEntity);
                UnityEngine.Object.DestroyImmediate(entity, true);
                entity.SetFlagLocal(BaseEntity.Flags.Busy, true);
                entity.SetFlagLocal(BaseEntity.Flags.Locked, true);

                return trueBaseEntity;
            }

            public static void SetParent(BaseEntity parentEntity, BaseEntity childEntity, Vector3 localPosition, Vector3 localRotation)
            {
                childEntity.SetParent(parentEntity, true);
                childEntity.transform.localPosition = localPosition;
                childEntity.transform.localEulerAngles = localRotation;
            }

            private static void DestroyDecorComponents(BaseEntity entity)
            {
                Component[] components = entity.GetComponentsInChildren<Component>();

                foreach (Component component in components)
                {
                    EntityCollisionMessage entityCollisionMessage = component as EntityCollisionMessage;

                    if (entityCollisionMessage != null || (component != null && component.name != entity.PrefabName))
                    {
                        Transform transform = component as Transform;
                        if (transform != null)
                            continue;

                        Collider collider = component as Collider;
                        if (collider != null && collider is MeshCollider == false)
                            continue;

                        if (component is Model)
                            continue;

                        UnityEngine.Object.DestroyImmediate(component);
                    }
                }
            }

            public static void DestroyUnnecessaryComponents(BaseEntity entity)
            {
                DestroyEntityComponent<GroundWatch>(entity);
                DestroyEntityComponent<DestroyOnGroundMissing>(entity);
                DestroyEntityComponent<TriggerHurtEx>(entity);

                if (entity is BradleyAPC == false)
                    DestroyEntityComponent<Rigidbody>(entity);
            }

            public static void DestroyEntityComponent<TYpeForDestroy>(BaseEntity entity)
            {
                if (entity == null)
                    return;

                TYpeForDestroy component = entity.GetComponent<TYpeForDestroy>();
                if (component != null)
                    UnityEngine.Object.DestroyImmediate(component as UnityEngine.Object);
            }

            public static void DestroyEntityComponents<TYpeForDestroy>(BaseEntity entity)
            {
                if (entity == null)
                    return;

                TYpeForDestroy[] components = entity.GetComponentsInChildren<TYpeForDestroy>();

                foreach (TYpeForDestroy component in components)
                {
                    if (component != null)
                        UnityEngine.Object.DestroyImmediate(component as UnityEngine.Object);
                }
            }

            public static void CopySerializableFields<T>(T src, T dst)
            {
                FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo field in srcFields)
                {
                    object value = field.GetValue(src);
                    field.SetValue(dst, value);
                }
            }
        }

        private static class PositionDefiner
        {
            public static Vector3 GetGlobalPosition(Transform parentTransform, Vector3 position)
            {
                return parentTransform.transform.TransformPoint(position);
            }

            public static Quaternion GetGlobalRotation(Transform parentTransform, Vector3 rotation)
            {
                return parentTransform.rotation * Quaternion.Euler(rotation);
            }

            public static Vector3 GetLocalPosition(Transform parentTransform, Vector3 globalPosition)
            {
                return parentTransform.transform.InverseTransformPoint(globalPosition);
            }

            public static Vector3 GetGroundPositionInPoint(Vector3 position, int layerMask = 1 << 16 | 1 << 23)
            {
                position.y = 100;

                if (Physics.Raycast(position, Vector3.down, out RaycastHit raycastHit, 500, layerMask))
                    position.y = raycastHit.point.y;

                return position;
            }

            public static bool GetNavmeshInPoint(Vector3 position, float radius, out NavMeshHit navMeshHit)
            {
                return NavMesh.SamplePosition(position, out navMeshHit, radius, 1);
            }
        }

        private static class NotifyManager
        {
            public static void PrintInfoMessage(BasePlayer player, string langKey, params object[] args)
            {
                if (player == null)
                    _ins.PrintWarning(ClearColorAndSize(GetMessage(langKey, null, args)));
                else
                    _ins.PrintToChat(player, GetMessage(langKey, player.UserIDString, args));
            }

            public static void PrintError(BasePlayer player, string langKey, params object[] args)
            {
                if (player == null)
                    _ins.PrintError(ClearColorAndSize(GetMessage(langKey, null, args)));
                else
                    _ins.PrintToChat(player, GetMessage(langKey, player.UserIDString, args));
            }

            public static void PrintLogMessage(string langKey, params object[] args)
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] is int)
                        args[i] = GetTimeMessage(null, (int)args[i]);

                _ins.Puts(ClearColorAndSize(GetMessage(langKey, null, args)));
            }

            public static void PrintWarningMessage(string langKey, params object[] args)
            {
                _ins.PrintWarning(ClearColorAndSize(GetMessage(langKey, null, args)));
            }

            private static string ClearColorAndSize(string message)
            {
                message = message.Replace("</color>", string.Empty);
                message = message.Replace("</size>", string.Empty);
                while (message.Contains("<color="))
                {
                    int index = message.IndexOf("<color=");
                    message = message.Remove(index, message.IndexOf(">", index) - index + 1);
                }
                while (message.Contains("<size="))
                {
                    int index = message.IndexOf("<size=");
                    message = message.Remove(index, message.IndexOf(">", index) - index + 1);
                }
                return message;
            }

            public static void SendMessageToAll(string langKey, params object[] args)
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    if (player != null)
                        SendMessageToPlayer(player, langKey, args);

                TrySendDiscordMessage(langKey, args);
            }

            public static void SendMessageToPlayer(BasePlayer player, string langKey, params object[] args)
            {
                object[] argsClone = new object[args.Length];

                for (int i = 0; i < args.Length; i++)
                    argsClone[i] = args[i];

                for (int i = 0; i < argsClone.Length; i++)
                    if (argsClone[i] is int)
                        argsClone[i] = GetTimeMessage(player.UserIDString, (int)argsClone[i]);

                string playerMessage = GetMessage(langKey, player.UserIDString, argsClone);

                if (_ins._config.NotifyConfig.IsChatEnable)
                    _ins.PrintToChat(player, playerMessage);

                if (_ins._config.NotifyConfig.GameTipConfig.IsEnabled)
                    player.SendConsoleCommand("gametip.showtoast", _ins._config.NotifyConfig.GameTipConfig.Style, ClearColorAndSize(playerMessage), string.Empty);

                if (_ins._config.SupportedPluginsConfig.GUIAnnouncementsConfig.IsEnabled && _ins.plugins.Exists("guiAnnouncementsConfig"))
                    _ins.GUIAnnouncements?.Call("CreateAnnouncement", ClearColorAndSize(playerMessage), _ins._config.SupportedPluginsConfig.GUIAnnouncementsConfig.BannerColor, _ins._config.SupportedPluginsConfig.GUIAnnouncementsConfig.TextColor, player, _ins._config.SupportedPluginsConfig.GUIAnnouncementsConfig.APIAdjustVPosition);

                if (_ins._config.SupportedPluginsConfig.NotifyPluginConfig.IsEnabled && _ins.plugins.Exists("Notify"))
                    _ins.Notify?.Call("SendNotify", player, _ins._config.SupportedPluginsConfig.NotifyPluginConfig.Type, ClearColorAndSize(playerMessage));
            }

            public static string GetTimeMessage(string userIDString, int seconds)
            {
                string message = "";

                TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
                if (timeSpan.Hours > 0) message += $" {timeSpan.Hours} {GetMessage("Hours", userIDString)}";
                if (timeSpan.Minutes > 0) message += $" {timeSpan.Minutes} {GetMessage("Minutes", userIDString)}";
                if (message == "") message += $" {timeSpan.Seconds} {GetMessage("Seconds", userIDString)}";

                return message;
            }

            private static void TrySendDiscordMessage(string langKey, params object[] args)
            {
                if (CanSendDiscordMessage(langKey))
                {
                    for (int i = 0; i < args.Length; i++)
                        if (args[i] is int)
                            args[i] = GetTimeMessage(null, (int)args[i]);

                    object fields = new[] { new { name = _ins.Title, value = ClearColorAndSize(GetMessage(langKey, null, args)), inline = false } };
                    _ins.DiscordMessages?.Call("API_SendFancyMessage", _ins._config.SupportedPluginsConfig.DiscordMessagesConfig.WebhookUrl, "", _ins._config.SupportedPluginsConfig.DiscordMessagesConfig.EmbedColor, JsonConvert.SerializeObject(fields), null, _ins);
                }
            }

            private static bool CanSendDiscordMessage(string langKey)
            {
                return _ins._config.SupportedPluginsConfig.DiscordMessagesConfig.Keys.Contains(langKey) && _ins._config.SupportedPluginsConfig.DiscordMessagesConfig.IsEnabled && !string.IsNullOrEmpty(_ins._config.SupportedPluginsConfig.DiscordMessagesConfig.WebhookUrl) && _ins._config.SupportedPluginsConfig.DiscordMessagesConfig.WebhookUrl != "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";
            }
        }

        private static class GuiManager
        {
            private static bool _isLoadingImageFailed;
            private const float TabWidth = 109;
            private const float TabHeight = 25;
            private static readonly ImageInfo TabImageInfo = new ImageInfo("Tab_Adem");
            private static readonly List<ImageInfo> IconImageInfos = new List<ImageInfo>
            {
                new ImageInfo("Clock_Adem"),
                new ImageInfo("Crates_Adem"),
                new ImageInfo("Soldiers_Adem"),
            };

            public static void LoadImages()
            {
                ServerMgr.Instance.StartCoroutine(LoadImagesCoroutine());
            }

            private static IEnumerator LoadImagesCoroutine()
            {
                yield return LoadTabCoroutine();

                if (!_isLoadingImageFailed)
                    yield return LoadIconsCoroutine();
            }

            private static IEnumerator LoadTabCoroutine()
            {
                string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images/" + TabImageInfo.ImageName + ".png";
                using UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url);
                yield return unityWebRequest.SendWebRequest();

                if (unityWebRequest.result != UnityWebRequest.Result.Success)
                {
                    OnImageSaveFailed();
                    _isLoadingImageFailed = true;
                }

                Texture2D texture = DownloadHandlerTexture.GetContent(unityWebRequest);
                uint imageId = FileStorage.server.Store(texture.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                TabImageInfo.ImageId = imageId.ToString();
                UnityEngine.Object.DestroyImmediate(texture);
            }

            private static IEnumerator LoadIconsCoroutine()
            {
                foreach (ImageInfo imageInfo in IconImageInfos)
                {
                    string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images/" + imageInfo.ImageName + ".png";
                    using UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url);
                    yield return unityWebRequest.SendWebRequest();

                    if (unityWebRequest.result != UnityWebRequest.Result.Success)
                    {
                        OnImageSaveFailed();
                        break;
                    }

                    Texture2D texture = DownloadHandlerTexture.GetContent(unityWebRequest);
                    uint imageId = FileStorage.server.Store(texture.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                    imageInfo.ImageId = imageId.ToString();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            private static void OnImageSaveFailed()
            {
                NotifyManager.PrintError(null, $"Move the contents of the data folder from the archive you downloaded from the website into the oxide/data folder on your server!");
                Interface.Oxide.UnloadPlugin(_ins.Name);
            }

            public static void CreateGui(BasePlayer player, params string[] args)
            {
                if (!_ins._config.GUIConfig.IsEnable)
                    return;

                CuiHelper.DestroyUi(player, "Tabs_Adem");
                CuiElementContainer container = new CuiElementContainer();
                float halfWidth = TabWidth / 2 + TabWidth / 2 * (IconImageInfos.Count - 1);

                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-halfWidth} {_ins._config.GUIConfig.OffsetMinY}", OffsetMax = $"{halfWidth} {_ins._config.GUIConfig.OffsetMinY + TabHeight}" },
                    CursorEnabled = false,
                }, "Under", "Tabs_Adem");

                float xMin = 0;

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    DrawTab(ref container, i, arg, xMin);
                    xMin += TabWidth;
                }

                CuiHelper.AddUi(player, container);
            }

            private static void DrawTab(ref CuiElementContainer container, int index, string text, float xMin)
            {
                ImageInfo imageInfo = IconImageInfos[index];

                container.Add(new CuiElement
                {
                    Name = $"Tab_{index}_Adem",
                    Parent = "Tabs_Adem",
                    Components =
                    {
                        new CuiRawImageComponent { Png = TabImageInfo.ImageId },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{xMin} 0", OffsetMax = $"{xMin + TabWidth} {TabHeight}" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{index}_Adem",
                    Components =
                    {
                        new CuiRawImageComponent { Png = imageInfo.ImageId },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "9 5", OffsetMax = "23 19" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{index}_Adem",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = text, Align = TextAnchor.MiddleCenter, FontSize = 10, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "23 5", OffsetMax = $"{TabWidth - 9} 19" }
                    }
                });
            }

            public static void DestroyAllGui()
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    if (player != null)
                        DestroyGui(player);
            }

            public static void DestroyGui(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, "Tabs_Adem");
            }

            private class ImageInfo
            {
                public readonly string ImageName;
                public string ImageId;

                public ImageInfo(string imageName)
                {
                    this.ImageName = imageName;
                }
            }
        }

        private static class EconomyManager
        {
            private static readonly Dictionary<ulong, double> PlayersBalance = new Dictionary<ulong, double>();

            public static void AddBalance(ulong playerId, double balance)
            {
                if (balance == 0 || playerId == 0)
                    return;

                if (!PlayersBalance.TryAdd(playerId, balance))
                    PlayersBalance[playerId] += balance;
            }

            public static void OnEventEnd()
            {
                DefineEventWinner();

                if (!_ins._config.SupportedPluginsConfig.EconomicsConfig.Enable || PlayersBalance.Count == 0)
                {
                    PlayersBalance.Clear();
                    return;
                }

                SendBalanceToPlayers();
                PlayersBalance.Clear();
            }

            private static void DefineEventWinner()
            {
                var winnerPair = PlayersBalance.Max(x => (float)x.Value);

                if (winnerPair.Value > 0)
                    Interface.CallHook($"On{_ins.Name}EventWin", winnerPair.Key);

                if (winnerPair.Value >= _ins._config.SupportedPluginsConfig.EconomicsConfig.MinCommandPoint)
                    foreach (string command in _ins._config.SupportedPluginsConfig.EconomicsConfig.Commands)
                        _ins.Server.Command(command.Replace("{steamid}", $"{winnerPair.Key}"));
            }

            private static void SendBalanceToPlayers()
            {
                foreach (KeyValuePair<ulong, double> pair in PlayersBalance)
                    SendBalanceToPlayer(pair.Key, pair.Value);
            }

            private static void SendBalanceToPlayer(ulong userID, double amount)
            {
                if (amount < _ins._config.SupportedPluginsConfig.EconomicsConfig.MinEconomyPoint)
                    return;

                int intAmount = Convert.ToInt32(amount);

                if (intAmount <= 0)
                    return;

                if (_ins._config.SupportedPluginsConfig.EconomicsConfig.Plugins.Contains("Economics") && _ins.plugins.Exists("Economics"))
                    _ins.Economics.Call("Deposit", userID.ToString(), amount);

                if (_ins._config.SupportedPluginsConfig.EconomicsConfig.Plugins.Contains("Server Rewards") && _ins.plugins.Exists("ServerRewards"))
                    _ins.ServerRewards.Call("AddPoints", userID, intAmount);

                if (_ins._config.SupportedPluginsConfig.EconomicsConfig.Plugins.Contains("IQEconomic") && _ins.plugins.Exists("IQEconomic"))
                    _ins.IQEconomic.Call("API_SET_BALANCE", userID, intAmount);

                BasePlayer player = BasePlayer.FindByID(userID);
                if (player != null)
                    NotifyManager.SendMessageToPlayer(player, "SendEconomy", _ins._config.Prefix, amount);
            }
        }
        #endregion Classes
        
        #region Loot
        private readonly Dictionary<string, PrefabLootInfo> _prefabLootTables = new Dictionary<string, PrefabLootInfo>();
        
        private static class LootController
        {
            public static LootTableConfig GetDefaultLootTable()
            {
                return new LootTableConfig
                {
                    IsAlphaLoot = true,
                    AlphaLootPreset = string.Empty,
                    IsLoottablePlugin = true,
                    LoottablePreset = string.Empty,
                    IsCustomLootPlugin = true,
                    CustomLootPreset = string.Empty,
                    ClearDefaultLoot = false,
                    
                    ItemsTable = new ItemsLootTableConfig
                    {
                        IsEnabled = false,
                        DisableMinMax = false,
                        MinItemsAmount = 1,
                        MaxItemsAmount =1,
                        Items = new List<LootItemConfig>
                        {
                            new LootItemConfig
                            {
                                Shortname = "scrap",
                                Skin = 0,
                                Chance = 100,
                                MinAmount = 100,
                                MaxAmount = 200,
                                DisplayName = string.Empty,
                                OwnerName = string.Empty,
                                IsBlueprint = false,
                                Genomes = new List<string>()
                            }
                        }
                    },
                    PrefabsTable = new PrefabsLootTableConfig
                    {
                        IsEnabled = false,
                        MinPrefabsAmount = 1,
                        MaxPrefabsAmount = 1,
                        Prefabs = new List<LootPrefabConfig>
                        {
                            new LootPrefabConfig
                            {
                                PrefabName = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                                Chance = 100,
                                MinAmount = 1,
                                MaxAmount = 1
                            }
                        }
                    }
                };
            }

            public static HashSet<LootTableConfig> GetAllLootTables()
            {
                HashSet<LootTableConfig> result = new HashSet<LootTableConfig>();
                
                foreach (NpcConfig npcConfig in _ins._config.NpcConfigs)
                    result.Add(npcConfig.LootTableConfig);
                
                foreach (CrateConfig crateConfig in _ins._config.CrateConfigs)
                    result.Add(crateConfig.LootTableConfig);
                
                foreach (BradleyConfig bradleyConfig in _ins._config.Bradleys)
                    result.Add(bradleyConfig.LootTableConfig);
                
                foreach (HeliConfig heliConfig in _ins._config.HeliConfigs)
                    result.Add(heliConfig.LootTableConfig);
                
                return result;
            }

            public static void UpdateNpcCorpse(NPCPlayerCorpse corpse, NpcConfig npcConfig)
            {
                if (corpse.containers.IsNullOrEmpty())
                    return;
                
                EntityPresetInfo.Attach(corpse, npcConfig.PresetName);
                
                if (IsNoAdditionLoot(npcConfig.LootTableConfig))
                    return;
                
                ItemContainer container = corpse.containers[0];
                if (container == null)
                    return;
                
                corpse.Invoke(() =>
                {
                    if (!corpse.IsExists())
                        return;
                    
                    FillContainer(container, npcConfig.LootTableConfig);
                }, 0.1f);
            }

            public static void UpdateCrate(BaseEntity crateEntity, CrateConfig crateConfig)
            {
                if (crateEntity == null)
                    return;
                
                EntityPresetInfo.Attach(crateEntity, crateConfig.PresetName);

                if (IsNoAdditionLoot(crateConfig.LootTableConfig))
                    return;
                
                Fridge fridge = crateEntity as Fridge;
                if (fridge != null)
                {
                    fridge.OnlyAcceptCategory = ItemCategory.All;
                    FillContainer(fridge.inventory, crateConfig.LootTableConfig);
                    return;
                }
                
                LootContainer lootContainer = crateEntity as LootContainer;
                if (lootContainer != null)
                {
                    lootContainer.Invoke(() =>
                    {
                        if (lootContainer.inventory != null)
                            FillContainer(lootContainer.inventory, crateConfig.LootTableConfig);
                    }, 2f);
                    
                    return;
                }
                
                DroppedItemContainer droppedItemContainer = crateEntity as DroppedItemContainer;
                if (droppedItemContainer != null)
                {
                    FillContainer(droppedItemContainer.inventory, crateConfig.LootTableConfig);
                    return;
                }
                
                StorageContainer storageContainer = crateEntity as StorageContainer;
                if (storageContainer != null)
                {
                    FillContainer(storageContainer.inventory, crateConfig.LootTableConfig);
                }
            }

            public static void UpdateHeliOrBradleyCrate(LockedByEntCrate lockedByEntCrate, LootTableConfig lootTableConfig, string presetName)
            {
                EntityPresetInfo.Attach(lockedByEntCrate, presetName);
                
                if (IsNoAdditionLoot(lootTableConfig))
                    return;
                
                lockedByEntCrate.Invoke(() =>
                {
                    if (lockedByEntCrate.inventory != null)
                        FillContainer(lockedByEntCrate.inventory, lootTableConfig);
                }, 2f);
            }
            
            private static bool IsNoAdditionLoot(LootTableConfig lootTableConfig)
            {
                return !lootTableConfig.ClearDefaultLoot &&
                       !lootTableConfig.ItemsTable.IsEnabled &&
                       !lootTableConfig.PrefabsTable.IsEnabled &&
                       string.IsNullOrEmpty(lootTableConfig.AlphaLootPreset) &&
                       string.IsNullOrEmpty(lootTableConfig.LoottablePreset) &&
                       string.IsNullOrEmpty(lootTableConfig.CustomLootPreset);
            }

            private static void FillContainer(ItemContainer container, LootTableConfig lootTable)
            {
                if (lootTable.ClearDefaultLoot)
                    ClearContainer(container);
                
                if (lootTable.ItemsTable.IsEnabled)
                {
                    if (lootTable.ItemsTable.DisableMinMax)
                    {
                        ItemListController.FillContainerWithoutMinMax(container, lootTable.ItemsTable.Items);
                    }
                    else if (lootTable.ItemsTable.Items.Count > 0)
                    {
                        int itemsAmount = Random.Range(lootTable.ItemsTable.MinItemsAmount, lootTable.ItemsTable.MaxItemsAmount + 1);
                        HashSet<LootItemConfig> itemsForSpawn = GetElementsForSpawn(lootTable.ItemsTable.Items, itemsAmount);

                        if (itemsForSpawn.Count > 0)
                            ItemListController.FillContainer(container, itemsForSpawn);
                    }
                }
                
                if (lootTable.PrefabsTable.IsEnabled)
                {
                    int prefabsAmount = Random.Range(lootTable.PrefabsTable.MinPrefabsAmount, lootTable.PrefabsTable.MaxPrefabsAmount + 1);
                    HashSet<LootPrefabConfig> prefabsForSpawn = GetElementsForSpawn(lootTable.PrefabsTable.Prefabs, prefabsAmount);
                    PrefabController.FillContainer(container, prefabsForSpawn);
                }
                
                if (!string.IsNullOrEmpty(lootTable.AlphaLootPreset))
                {
                    if (_ins.plugins.Exists("AlphaLoot") && (bool)_ins.AlphaLoot.Call("ProfileExists", lootTable.AlphaLootPreset))
                    {
                        _ins.AlphaLoot.Call("PopulateLoot", container, lootTable.AlphaLootPreset);
                    }
                }

                if (!string.IsNullOrEmpty(lootTable.CustomLootPreset))
                {
                    if (!_ins.plugins.Exists("CustomLoot"))
                        return;

                    List<Item> items = _ins.CustomLoot?.Call<List<Item>>("MakeLoot", lootTable.CustomLootPreset);
                    if (items != null)
                        foreach (Item item in items)
                            if (item != null && !item.MoveToContainer(container))
                                item.Remove();
                }

                if (!string.IsNullOrEmpty(lootTable.LoottablePreset))
                {
                    if (!_ins.plugins.Exists("Loottable"))
                        return;

                    List<Item> items = _ins.Loottable?.Call<List<Item>>("MakeLoot", lootTable.LoottablePreset);
                    if (items != null)
                    {
                        foreach (Item item in items)
                            if (item != null && !item.MoveToContainer(container))
                                item.Remove();

                        Pool.FreeUnmanaged(ref items);
                    }
                }
            }
            
            private static void ClearContainer(ItemContainer container)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    item.RemoveFromContainer();
                    item.Remove();
                }
            }
            
            private static HashSet<T> GetElementsForSpawn<T>(List<T> elements, int targetAmount) where T : LootElementChanceConfig
            {
                HashSet<T> result = new HashSet<T>();
                if (elements.Count == 0 || targetAmount <= 0)
                    return result;

                HashSet<int> includedIndexes = new HashSet<int>();
                
                for (int i = 0; i < elements.Count; i++)
                {
                    T elementConfig = elements[i];
                    
                    if (elementConfig.Chance >= 100f)
                    {
                        includedIndexes.Add(i);
                        result.Add(elementConfig);
                        
                        if (result.Count >= targetAmount)
                            break;
                    }
                }

                if (result.Count >= targetAmount)
                    return result;
                    
                int counter = 200;
                while (result.Count < targetAmount && counter-- > 0)
                {
                    float sumChance = 0f;
                        
                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (includedIndexes.Contains(i))
                            continue;

                        sumChance += elements[i].Chance;
                    }
                        
                    if (sumChance <= 0f) 
                        break;

                    float random = Random.Range(0f, sumChance);
                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (includedIndexes.Contains(i))
                            continue;

                        T lootElement = elements[i];
                        random -= lootElement.Chance;
                        if (random <= 0f)
                        {
                            includedIndexes.Add(i);
                            result.Add(lootElement);
                            break;
                        }
                    }
                }

                return result;
            }
        }
        
        private static class ItemListController
        {
            public static void FillContainer(ItemContainer container, HashSet<LootItemConfig> items)
            {
                int itemsCount = container.itemList.Count + items.Count;
                if (container.capacity < itemsCount)
                    container.capacity = itemsCount;
                
                foreach (LootItemConfig lootItemConfig in items)
                {
                    Item item = CreateItem(lootItemConfig);
                    if (item == null)
                        continue;

                    if (!item.MoveToContainer(container))
                        item.Remove();
                }
            }

            public static void FillContainerWithoutMinMax(ItemContainer container, List<LootItemConfig> items)
            {
                foreach (LootItemConfig itemConfig in items)
                {
                    float roll = Random.Range(0f, 100f);
                    if (roll > itemConfig.Chance)
                        continue;

                    Item item = CreateItem(itemConfig);
                    if (item == null)
                        continue;

                    if (!item.MoveToContainer(container))
                        item.Remove();
                }
            }

            private static Item CreateItem(LootItemConfig lootItemConfig)
            {
                int amount = Random.Range(lootItemConfig.MinAmount, lootItemConfig.MaxAmount + 1);
                if (amount == 0)
                    return null;

                Item item;

                if (lootItemConfig.IsBlueprint)
                {
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(lootItemConfig.Shortname);
                    if (itemDefinition == null)
                        return null;
                    
                    item = ItemManager.CreateByName("blueprintbase");
                    item.blueprintTarget = itemDefinition.itemid;
                }
                else
                {
                    item = ItemManager.CreateByName(lootItemConfig.Shortname, amount, lootItemConfig.Skin);
                }

                if (item == null)
                {
                    _ins.PrintWarning($"Failed to create item! ({lootItemConfig.Shortname})");
                    return null;
                }

                if (!string.IsNullOrEmpty(lootItemConfig.DisplayName))
                    item.name = lootItemConfig.DisplayName;

                if (lootItemConfig.Genomes != null && lootItemConfig.Genomes.Count > 0)
                {
                    string genome = lootItemConfig.Genomes.GetRandom();
                    UpdateGenome(item, genome);
                }

                return item;
            }

            private static void UpdateGenome(Item item, string genome)
            {
                if (genome.Length != 6)
                    return;

                genome = genome.ToLower();
                GrowableGenes growableGenes = new GrowableGenes();

                for (int i = 0; i < 6 && i < genome.Length; ++i)
                {

                    GrowableGenetics.GeneType geneType = GrowableGenetics.GeneType.Empty;

                    switch (genome[i])
                    {
                        case 'g':
                            geneType = GrowableGenetics.GeneType.GrowthSpeed;
                            break;
                        case 'y':
                            geneType = GrowableGenetics.GeneType.Yield;
                            break;
                        case 'h':
                            geneType = GrowableGenetics.GeneType.Hardiness;
                            break;
                        case 'w':
                            geneType = GrowableGenetics.GeneType.WaterRequirement;
                            break;
                    }

                    growableGenes.Genes[i].Set(geneType, true);
                }

                GrowableGeneEncoding.EncodeGenesToItem(GrowableGeneEncoding.EncodeGenesToInt(growableGenes), item);
            }
        }
        
        private static class PrefabController
        {
            public static void CachePrefabs()
            {
                HashSet<LootTableConfig> lootTableConfigs = LootController.GetAllLootTables();
                
                foreach (LootTableConfig lootTableConfig in lootTableConfigs)
                {
                    if (!lootTableConfig.PrefabsTable.IsEnabled)
                        continue;
        
                    foreach (LootPrefabConfig lootPrefabConfig in lootTableConfig.PrefabsTable.Prefabs)
                        CachePrefab(lootPrefabConfig);
                }
            }

            private static void CachePrefab(LootPrefabConfig lootPrefabConfig)
            {
                if (_ins._prefabLootTables.ContainsKey(lootPrefabConfig.PrefabName))
                    return;
        
                GameObject gameObject = GameManager.server.FindPrefab(lootPrefabConfig.PrefabName);
                if (gameObject == null)
                    return;
        
                LootContainer lootContainer = gameObject.GetComponent<LootContainer>();
                if (lootContainer != null)
                {
                    SavePrefabLootInfo(lootPrefabConfig.PrefabName, lootContainer.LootSpawnSlots, lootContainer.scrapAmount, lootContainer.lootDefinition, lootContainer.maxDefinitionsToSpawn);
                    return;
                }
        
                global::HumanNPC humanNpc = gameObject.GetComponent<global::HumanNPC>();
                if (humanNpc != null)
                {
                    SavePrefabLootInfo(lootPrefabConfig.PrefabName, humanNpc.LootSpawnSlots);
                    return;
                }
        
                global::ScarecrowNPC scarecrowNpc = gameObject.GetComponent<global::ScarecrowNPC>();
                if (scarecrowNpc != null)
                {
                    SavePrefabLootInfo(lootPrefabConfig.PrefabName, scarecrowNpc.LootSpawnSlots);
                }
            }
        
            private static void SavePrefabLootInfo(string prefabName, LootContainer.LootSpawnSlot[] lootSpawnSlot, int scrapAmount = 0, LootSpawn lootDefinition = null, int maxDefinitionsToSpawn = 0)
            {
                PrefabLootInfo prefabLootInfo = new PrefabLootInfo
                {
                    LootSpawnSlots = lootSpawnSlot,
                    LootDefinition = lootDefinition,
                    MaxDefinitionsToSpawn = maxDefinitionsToSpawn,
                    ScrapAmount = scrapAmount
                };
        
                _ins._prefabLootTables.TryAdd(prefabName, prefabLootInfo);
            }
        
            public static void FillContainer(ItemContainer container, HashSet<LootPrefabConfig> prefabs)
            {
                foreach (LootPrefabConfig lootPrefabConfig in prefabs)
                    FillContainer(container, lootPrefabConfig);
            }
        
            private static void FillContainer(ItemContainer container, LootPrefabConfig prefab)
            {
                if (!_ins._prefabLootTables.TryGetValue(prefab.PrefabName, out PrefabLootInfo prefabLootInfo))
                    return;
        
                int lootScale = Random.Range(prefab.MinAmount, prefab.MaxAmount + 1);
        
                for (int i = 0; i < lootScale; i++)
                {
                    if (prefabLootInfo.LootSpawnSlots != null)
                    {
                        foreach (LootContainer.LootSpawnSlot lootSpawnSlot in prefabLootInfo.LootSpawnSlots)
                        {
                            if (lootSpawnSlot.eras == null || lootSpawnSlot.eras.Length == 0 || Array.IndexOf(lootSpawnSlot.eras, ConVar.Server.Era) != -1)
                            {
                                for (int j = 0; j < lootSpawnSlot.numberToSpawn; ++j)
                                {
                                    if (Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                                    {
                                        lootSpawnSlot.definition.SpawnIntoContainer(container);
                                    }
                                }
                            }
                        }
                    }
        
                    if (prefabLootInfo.LootDefinition != null)
                        for (int j = 0; j < prefabLootInfo.MaxDefinitionsToSpawn; ++j)
                            prefabLootInfo.LootDefinition.SpawnIntoContainer(container);
        
                    if (prefabLootInfo.ScrapAmount > 0)
                    {
                        Item item = ItemManager.CreateByName("scrap", prefabLootInfo.ScrapAmount);
                        if (!item.MoveToContainer(container))
                            item.Remove();
                    }
                }
            }
        }
        
        private class PrefabLootInfo
        {
            public LootContainer.LootSpawnSlot[] LootSpawnSlots;
            public LootSpawn LootDefinition;
            public int MaxDefinitionsToSpawn;
            public int ScrapAmount;
        }

        private class EntityPresetInfo : FacepunchBehaviour
        {
            public string presetName;

            public static void Attach(BaseEntity entity, string presetName)
            {
                EntityPresetInfo entityPresetInfo = entity.gameObject.AddComponent<EntityPresetInfo>();
                entityPresetInfo.presetName = presetName;
            }
        }
        #endregion Loot
        
        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["EventActive_Exeption"] = "Ивент в данный момент активен, сначала завершите текущий ивент (<color=#ce3f27>/atrainstop</color>)!",
                ["ConfigurationNotFound_Exeption"] = "<color=#ce3f27>Не удалось</color> найти конфигурацию ивента!",
                ["PresetNotFound_Exeption"] = "Пресет {0} <color=#ce3f27>не найден</color> в конфиге!",
                ["NavMesh_Exeption"] = "Навигационная сетка не найдена!",

                ["SuccessfullyLaunched"] = "Ивент <color=#738d43>успешно</color> запущен!",
                ["PreStartTrain"] = "{0} <color=#738d43>{1}</color> появится через {2}!",
                ["StartTrain"] = "{0} <color=#738d43>{1}</color> был обнаружен в квадрате <color=#738d43>{2}</color>",
                ["PlayerStopTrain"] = "{0} <color=#ce3f27>{1}</color> остановил поезд!",
                ["RemainTime"] = "{0} Поезд будет уничтожен через <color=#ce3f27>{1}</color>!",
                ["EndEvent"] = "{0} Перевозка груза <color=#ce3f27>окончена</color>!",
                ["NeedStopTrain"] = "{0} Необходимо <color=#ce3f27>остановить</color> поезд!",
                ["NeedKillGuards"] = "{0} Необходимо <color=#ce3f27>уничтожить</color> охрану поезда!",
                ["EnterPVP"] = "{0} Вы <color=#ce3f27>вошли</color> в PVP зону, теперь другие игроки <color=#ce3f27>могут</color> наносить вам урон!",
                ["ExitPVP"] = "{0} Вы <color=#738d43>вышли</color> из PVP зоны, теперь другие игроки <color=#738d43>не могут</color> наносить вам урон!",
                ["DamageDistance"] = "{0} Подойдите <color=#ce3f27>ближе</color>!",
                ["DamageHeight"] = "{0} Вы находитесь <color=#ce3f27>слишком высоко</color> над поездом!",
                ["DamageRock"] = "{0} Нельзя атаковать поезд с постройки на <color=#ce3f27>высокой скале</color>!",
                
                ["Looted"] = "{0} <color=#738d43>{1}</color> был <color=#ce3f27>ограблен</color>!",

                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента",

                ["Hours"] = "ч.",
                ["Minutes"] = "м.",
                ["Seconds"] = "с.",

                ["PveMode_BlockAction"] = "{0} Вы <color=#ce3f27>не можете</color> взаимодействовать с ивентом из-за кулдауна!",
                ["PveMode_YouAreNoOwner"] = "{0} Вы <color=#ce3f27>не являетесь</color> владельцем ивента!",
            }, this, "ru");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["EventActive_Exeption"] = "This event is active now. Finish the current event! (<color=#ce3f27>/atrainstop</color>)!",
                ["ConfigurationNotFound_Exeption"] = "The event configuration <color=#ce3f27>could not</color> be found!",
                ["PresetNotFound_Exeption"] = "{0} preset was <color=#ce3f27>not found</color> in the config!",
                ["EntitySpawn_Exeption"] = "Failed to spawn the entity (prefabName - {0})",
                ["LocomotiveSpawn_Exeption"] = "Failed to spawn the locomotive (presetName - {0})",
                ["FileNotFound_Exeption"] = "Data file not found or corrupted! ({0}.json)!",
                ["DataFileNotFound_Exeption"] = "Could not find a data file for customization ({0}.json). Empty the [Customization preset] in the config or upload the data file",
                ["RouteNotFound_Exeption"] = "The route could not be generated! Try to increase the minimum road length or change the route type!",
                ["NavMesh_Exeption"] = "The navigation grid was not found!",
                ["Rail_Exeption"] = "The rails could not be found! Try using custom spawn points",
                ["CustomSpawnPoint_Exeption"] = "Couldn't find a suitable custom spawn point!",

                ["SuccessfullyLaunched"] = "The event has been <color=#738d43>successfully</color> launched!",
                ["PreStartTrain"] = "{0} The <color=#738d43>{1}</color> will spawn in {2}!",
                ["StartTrain"] = "{0} <color=#738d43>{1}</color> is spawned at grid <color=#738d43>{2}</color>",
                ["PlayerStopTrain"] = "{0} <color=#ce3f27>{1}</color> stopped the train!",
                ["RemainTime"] = "{0} The train will be destroyed in <color=#ce3f27>{1}</color>!",
                ["EndEvent"] = "{0} The event is <color=#ce3f27>over</color>!",
                ["NeedStopTrain"] = "{0} You must <color=#ce3f27>stop</color> the train!",
                ["NeedKillGuards"] = "{0} You must <color=#ce3f27>kill</color> train guards!",
                ["EnterPVP"] = "{0} You <color=#ce3f27>have entered</color> the PVP zone, now other players <color=#ce3f27>can damage</color> you!",
                ["ExitPVP"] = "{0} You <color=#738d43>have gone out</color> the PVP zone, now other players <color=#738d43>can’t damage</color> you!",
                ["DamageDistance"] = "{0} Come <color=#ce3f27>closer</color>!",
                ["DamageHeight"] = "{0} You are <color=#ce3f27>too high</color> above the train!",
                ["DamageRock"] = "{0} You can't attack the train from a base built on a <color=#ce3f27>tall rock</color>!",
                ["Looted"] = "{0} <color=#738d43>{1}</color> has been <color=#ce3f27>looted</color>!",

                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> points in economics for participating in the event",

                ["Hours"] = "h.",
                ["Minutes"] = "m.",
                ["Seconds"] = "s.",

                ["Marker_EventOwner"] = "Event Owner: {0}",

                ["EventStart_Log"] = "The event has begun! (Preset name - {0})",
                ["EventStop_Log"] = "The event is over!",

                ["PveMode_BlockAction"] = "{0} You <color=#ce3f27>can't interact</color> with the event because of the cooldown!",
                ["PveMode_YouAreNoOwner"] = "{0} You are not the <color=#ce3f27>owner</color> of the event!",
            }, this);
        }

        private static string GetMessage(string langKey, string userID) => _ins.lang.GetMessage(langKey, _ins, userID);

        private static string GetMessage(string langKey, string userID, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userID) : string.Format(GetMessage(langKey, userID), args);
        #endregion Lang

        #region Config
        private void UpdateConfig()
        {
            if (_config.VersionConfig != Version)
            {
                PluginConfig defaultConfig = PluginConfig.DefaultConfig();
                if (_config.VersionConfig.Major == 1)
                {
                    if (_config.VersionConfig.Minor == 9)
                    {

                        if (_config.VersionConfig.Patch <= 7)
                        {
                            _config.MainConfig.ExtendEventIfNotEnoughTimeForCrate = true;
                        }

                        if (_config.VersionConfig.Patch <= 8)
                        {
                            foreach (HeliConfig heliConfig in _config.HeliConfigs)
                            {
                                heliConfig.MinCratesAmount = 3;
                                heliConfig.MaxCratesAmount = 3;
                            }
                        }
                        
                        _config.VersionConfig = new VersionNumber(2, 0, 0);
                    }
                }

                if (_config.VersionConfig < new VersionNumber(2, 0, 1))
                {
                    _config.MainConfig.MaxDamageHeight = -1f;
                    _config.MainConfig.MaxFoundationHeightAboveGround = 5f;
                            
                    foreach (NpcConfig npcConfig in _config.NpcConfigs)
                    {
                        npcConfig.SleepDistance = 100f;
                        npcConfig.IgnoreWoundedPlayers = false;
                    }
                }

                if (_config.VersionConfig < new VersionNumber(2, 0, 3))
                {
                    _config.MainConfig.IsPlayerTurretAttackTrainNpcs = false;
                    _config.MainConfig.IsPlayerTurretAttackDismountedNpcs = true;
                }

                _config.VersionConfig = Version;
            }

            UpdateConfigValues();
            SaveConfig();
        }
        
        private void UpdateConfigValues()
        {
            foreach (CrateConfig crateConfig in _config.CrateConfigs)
            {
                if (crateConfig.LootTableConfig == null)
                    crateConfig.LootTableConfig = LootController.GetDefaultLootTable();
                else
                    UpdateLootTableValues(crateConfig.LootTableConfig);
            }
            
            foreach (NpcConfig npcConfig in _config.NpcConfigs)
            {
                if (npcConfig.LootTableConfig == null)
                    npcConfig.LootTableConfig = LootController.GetDefaultLootTable();
                else
                    UpdateLootTableValues(npcConfig.LootTableConfig);
            }
            
            foreach (BradleyConfig bradleyConfig in _config.Bradleys)
            {
                if (bradleyConfig.LootTableConfig == null)
                    bradleyConfig.LootTableConfig = LootController.GetDefaultLootTable();
                else
                    UpdateLootTableValues(bradleyConfig.LootTableConfig);
            }
            
            foreach (HeliConfig heliConfig in _config.HeliConfigs)
            {
                if (heliConfig.LootTableConfig == null)
                    heliConfig.LootTableConfig = LootController.GetDefaultLootTable();
                else
                    UpdateLootTableValues(heliConfig.LootTableConfig);
            }
        }

        private void UpdateLootTableValues(LootTableConfig lootTableConfig)
        {
            if (lootTableConfig.ItemsTable.MaxItemsAmount > lootTableConfig.ItemsTable.Items.Count) 
                lootTableConfig.ItemsTable.MaxItemsAmount = lootTableConfig.ItemsTable.Items.Count;
            
            lootTableConfig.ItemsTable.Items = lootTableConfig.ItemsTable.Items.OrderByQuickSort(x => x.Chance);
            
            for (int i = lootTableConfig.ItemsTable.Items.Count - 1; i >= 0; i--)
            {
                LootItemConfig lootItemConfig = lootTableConfig.ItemsTable.Items[i];
                
                if (!ItemManager.itemList.Any(x => x.shortname == lootItemConfig.Shortname))
                {
                    PrintWarning($"Unknown item removed! ({lootItemConfig.Shortname})");
                    lootTableConfig.ItemsTable.Items.RemoveAt(i);
                    continue;
                }

                lootItemConfig.Chance = Math.Clamp(lootItemConfig.Chance, 0f, 100f);
                
                if (lootItemConfig.MaxAmount < lootItemConfig.MinAmount) 
                    lootItemConfig.MaxAmount = lootItemConfig.MinAmount;
                
            }
            
            if (lootTableConfig.PrefabsTable.MaxPrefabsAmount > lootTableConfig.PrefabsTable.Prefabs.Count) 
                lootTableConfig.PrefabsTable.MaxPrefabsAmount = lootTableConfig.PrefabsTable.Prefabs.Count;
            
            lootTableConfig.PrefabsTable.Prefabs = lootTableConfig.PrefabsTable.Prefabs.OrderByQuickSort(x => x.Chance);

            for (int i = lootTableConfig.PrefabsTable.Prefabs.Count - 1; i >= 0; i--)
            {
                LootPrefabConfig lootPrefabConfig = lootTableConfig.PrefabsTable.Prefabs[i];
                
                lootPrefabConfig.Chance = Math.Clamp(lootPrefabConfig.Chance, 0f, 100f);
                
                if (lootPrefabConfig.MaxAmount < lootPrefabConfig.MinAmount) 
                    lootPrefabConfig.MaxAmount = lootPrefabConfig.MinAmount;
            }
        }
        
        private PluginConfig _config;

        protected override void LoadDefaultConfig() => _config = PluginConfig.DefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_config, true);
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #region CustomizationConfig
        private class CustomizeProfile
        {
            [JsonProperty("Wagons presets")]
            public List<WagonCustomizationData> WagonPresets { get; set; }

            [JsonProperty("Npc presets")]
            public List<CustomizeNpcConfig> NpcPresets { get; set; }

            [JsonProperty("Fireworks Presets")]
            public List<FireworkConfig> FireworkConfigs { get; set; }
        }

        private class WagonCustomizationData
        {
            [JsonProperty("Preset Name")]
            public string PresetName { get; set; }

            [JsonProperty("Enable [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty("Short prefab name of the wagon to which customization will be applied")]
            public string ShortPrefabName { get; set; }

            [JsonProperty("Presets of wagons to which this preset will be applied (leave empty for all presets)")]
            public HashSet<string> WagonOnly { get; set; }

            [JsonProperty("Presets of wagons that will NOT be customized")]
            public HashSet<string> WagonExceptions { get; set; }

            [JsonProperty("Presets of trains that will NOT be customized")]
            public HashSet<string> TrainExceptions { get; set; }

            [JsonProperty("Disable the basic decoration on the carriage [true/false]")]
            public bool IsBaseDecorDisable { get; set; }

            [JsonProperty("List of decorations")]
            public HashSet<DecorEntityConfig> DecorEntityConfigs { get; set; }

            [JsonProperty("List of signs")]
            public HashSet<PaintedSignConfig> SignConfigs { get; set; }
        }

        private class PaintedSignConfig : DecorEntityConfig
        {
            [JsonProperty("Image Name")]
            public string ImageName { get; set; }
        }

        private class DecorEntityConfig
        {
            [JsonProperty("Prefab")]
            public string PrefabName { get; set; }

            [JsonProperty("Skin")]
            public ulong Skin { get; set; }

            [JsonProperty("Position")]
            public string Position { get; set; }

            [JsonProperty("Rotation")]
            public string Rotation { get; set; }
        }

        private class CustomizeNpcConfig
        {
            [JsonProperty("Enable [true/false]")]
            public bool Enable { get; set; }

            [JsonProperty("Wear Items")]
            public List<CustomWearItem> CustomWearItems { get; set; }
        }

        private class CustomWearItem
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty("SkinID (0 - default)")]
            public ulong SkinID { get; set; }
        }

        private class FireworkConfig
        {
            [JsonProperty("Preset Name")]
            public string PresetName { get; set; }

            [JsonProperty("Enable [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty("Color (r, g, b)")]
            public string Color { get; set; }

            [JsonProperty("Coordinates for the drawing")]
            public HashSet<string> PaintCoordinates { get; set; }
        }
        #endregion CustomizationConfig

        private class MainConfig
        {
            [JsonProperty(En ? "Enable automatic event holding [true/false]" : "Включить автоматическое проведение ивента [true/false]")]
            public bool IsAutoEvent { get; set; }

            [JsonProperty(En ? "Minimum time between events [sec]" : "Минимальное вермя между ивентами [sec]")]
            public int MinTimeBetweenEvents { get; set; }

            [JsonProperty(En ? "Maximum time between events [sec]" : "Максимальное вермя между ивентами [sec]")]
            public int MaxTimeBetweenEvents { get; set; }

            [JsonProperty(En ? "The probability of holding an event underground [0 - 100]" : "Вероятность проведения ивента под землей [0 - 100]")]
            public float UndergroundChance { get; set; }

            [JsonProperty(En ? "The train attacks first [true/false]" : "Поезд атакует первым [true/false]")]
            public bool IsAggressive { get; set; }

            [JsonProperty(En ? "The time for which the train becomes aggressive after taking damage [sec]" : "Время, на которое поезд становится враждебным, после получения урона [sec]")]
            public int AggressiveTime { get; set; }

            [JsonProperty(En ? "The crates can only be opened when the train is stopped [true/false]" : "Ящики можно открыть только на остановленном поезеде [true/false]")]
            public bool NeedStopTrain { get; set; }

            [JsonProperty(En ? "It is necessary to kill all the NPCs to loot the crates [true/false]" : "Необходимо убить всех NPC чтобы залутать ящики [true/false]")]
            public bool NeedKillNpc { get; set; }

            [JsonProperty(En ? "It is necessary to kill all the Bradleys to loot the crates [true/false]" : "Необходимо убить все Bradley чтобы залутать ящики [true/false]")]
            public bool NeedKillBradleys { get; set; }

            [JsonProperty(En ? "It is necessary to kill all the Turrets to loot the crates [true/false]" : "Необходимо убить все турели чтобы залутать ящики [true/false]")]
            public bool NeedKillTurrets { get; set; }

            [JsonProperty(En ? "It is necessary to kill the Heli to loot the crates [true/false]" : "Необходимо убить Вертолет чтобы залутать ящики [true/false]")]
            public bool NeedKillHeli { get; set; }

            [JsonProperty(En ? "Stop the train after taking damage [true/false]" : "Останавливать поезд после получения урона [true/false]")]
            public bool StopTrainAfterReceivingDamage { get; set; }

            [JsonProperty(En ? "Restore the stop time when the train receives damage/loot crates [true/false]" : "Восстанавливать время остановки при получении поездом урона/лутании ящиков [true/false]")]
            public bool IsRestoreStopTimeAfterDamageOrLoot { get; set; }

            [JsonProperty(En ? "Destroy the train after opening all the crates [true/false]" : "Уничтожать поезд после открытия всех ящиков [true/false]")]
            public bool KillTrainAfterLoot { get; set; }

            [JsonProperty(En ? "Time to destroy the train after opening all the crates [sec]" : "Время до уничтожения поезда после открытия всех ящиков [sec]")]
            public int EndAfterLootTime { get; set; }

            [JsonProperty(En ? "Destroy wagons in front of the train [true/false]" : "Уничтожать вагоны перед поездом [true/false]")]
            public bool DestroyWagons { get; set; }

            [JsonProperty(En ? "Allow damage to the train driver [true/false]" : "Разрешить урон по водителю поезда [true/false]")]
            public bool AllowDriverDamage { get; set; }

            [JsonProperty(En ? "To revive the train driver if he was killed? [true/false]" : "Возрождать водителя поезда, если он был убит [true/false]")]
            public bool ReviveTrainDriver { get; set; }

            [JsonProperty(En ? "Enable logging of the start and end of the event? [true/false]" : "Включить логирование начала и окончания ивента? [true/false]")]
            public bool EnableStartStopLogs { get; set; }

            [JsonProperty(En ? "The turrets of the train will drop loot after destruction? [true/false]" : "Турели поезда будут оставлять лут после уничтожения? [true/false]")]
            public bool IsTurretDropWeapon { get; set; }

            [JsonProperty(En ? "Maximum range for damage to turrets/NPCs/mines (-1 - do not limit)" : "Максимальная дистанция для нанесения урона по турелям/нпс/минам (-1 - не ограничивать)")]
            public int MaxGroundDamageDistance { get; set; }

            [JsonProperty(En ? "Maximum range for damage to heli (-1 - do not limit)" : "Максимальная дистанция для нанесения урона по вертолету (-1 - не ограничивать)")]
            public int MaxHeliDamageDistance { get; set; }

            [JsonProperty(En ? "Allow players to attach wagons to the front of the train [true/false]" : "Разрешить игрокам присоединять вагоны спереди поезда [true/false]")]
            public bool EnableFrontConnector { get; set; }

            [JsonProperty(En ? "Allow players to attach wagons to the back of the train [true/false]" : "Разрешить игрокам присоединять вагоны сзади поезда [true/false]")]
            public bool EnableBackConnector { get; set; }

            [JsonProperty(En ? "Allow the player to resume the movement of the train using a emergency brake [true/false]" : "Разрешить игроку возобновлять движение поезда при помощи стоп крана [true/false]")]
            public bool AllowEnableMovingByHandbrake { get; set; }

            [JsonProperty(En ? "The NPC will jump to the ground when the train stops (above ground)" : "НПС будет спрыгивать на землю при остановке поезда (на поверхности)")]
            public bool IsNpcJumpOnSurface { get; set; }

            [JsonProperty(En ? "The NPC will jump to the ground when the train stops (underground)" : "НПС будет спрыгивать на землю при остановке поезда (в метро)")]
            public bool IsNpcJumpInSubway { get; set; }

            [JsonProperty(En ? "The event will not end if there are players in the event zone [true/false]" : "Ивент не будет заканчиваться, если в зоне ивента есть игроки [true/false]")]
            public bool DontStopEventIfPlayerInZone { get; set; }
            
            [JsonProperty(En ? "Extend event if remaining time is not enough to unlock a locked crate [true/false]" : "Продлевать ивент, если оставшегося времени недостаточно для открытия заблокированного ящика [true/false]")]
            public bool ExtendEventIfNotEnoughTimeForCrate { get; set; }

            [JsonProperty(En ? "Setting up custom spawn points" : "Настройка кастомных тоек спавна")]
            public CustomSpawnPointConfig CustomSpawnPointConfig { get; set; }
            
            [JsonProperty(En ? "Maximum height of the player above the train for dealing damage to Bradleys/NPCs/turrets (protection against attacks from rocks/cliffs/towers) (-1 - do not limit)" : "Максимальная высота игрока над поездом для нанесения урона по турелям/нпс/бредли (защита от атак со скал/утёсов/вышек) (-1 - не ограничивать)")]
            public float MaxDamageHeight { get; set; }
            
            [JsonProperty(En ? "Maximum height of a foundation above the ground beneath it, above which damage to the train is blocked (protection against attacking the train from towers that the raiding NPCs cannot reach) (-1 - do not limit)" : "Максимальная высота фундамента над землёй под ним, при превышении которой блокируется урон по поезду (защита от атаки поезда с неприступных для рейда нпс вышек) (-1 - не ограничивать)")]
            public float MaxFoundationHeightAboveGround { get; set; }
            
            [JsonProperty(En ? "Player turrets will attack NPCs on the train [true/false]" : "Турели игроков будут атаковать NPC, находящихся на поезде [true/false]")]
            public bool IsPlayerTurretAttackTrainNpcs { get; set; }

            [JsonProperty(En ? "Player turrets will attack dismounted NPCs [true/false]" : "Турели игроков будут атаковать спешившихся NPC [true/false]")]
            public bool IsPlayerTurretAttackDismountedNpcs { get; set; }
        }

        private class CustomSpawnPointConfig
        {
            [JsonProperty(En ? "Use custom spawn points [true/false]" : "Использовать кастомные точки спавна [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty(En ? "Custom points for the spawn of the train (/atrainpoint)" : "Кастомные точки для спавна поезда (/atrainpoint)")]
            public HashSet<LocationConfig> Points { get; set; }
        }

        private class CustomizationConfig
        {
            [JsonProperty(En ? "Use Halloween customization [true/false]" : "Использовать кастомизацию Halloween [true/false]")]
            public bool IsHalloween { get; set; }

            [JsonProperty(En ? "Use Christmas customization [true/false]" : "Использовать кастомизацию Рождество [true/false]")]
            public bool IsChristmas { get; set; }

            [JsonProperty(En ? "Turn on the electric furnaces (high impact on performance) [true/false]" : "Включить свечение электрических печей (высокое влияение на производительность) [true/false]")]
            public bool IsElectricFurnacesEnable { get; set; }

            [JsonProperty(En ? "Turn on the boilers (medium impact on performance) [true/false]" : "Включить свечение котлов (среднее влияение на производительность) [true/false]")]
            public bool IsBoilersEnable { get; set; }

            [JsonProperty(En ? "Turn on the fire (medium impact on performance) [true/false]" : "Включить огонь (среднее влияение на производительность) [true/false]")]
            public bool IsFireEnable { get; set; }

            [JsonProperty(En ? "Turn on the lighting entities only at night [true/false]" : "Включать предметы освещения только ночью [true/false]")]
            public bool IsLightOnlyAtNight { get; set; }

            [JsonProperty(En ? "Turn on the Neon Signs [true/false]" : "Включить неоновые таблички [true/false]")]
            public bool IsNeonSignsEnable { get; set; }

            [JsonProperty(En ? "Setting up the Gift cannon" : "Настройка пушки подарков")]
            public GiftCannonSetting GiftCannonSetting { get; set; }

            [JsonProperty(En ? "Setting up fireworks" : "Настройка фейрверков")]
            public FireworksSetting FireworksSettings { get; set; }
        }

        private class GiftCannonSetting
        {
            [JsonProperty(En ? "Enable throwing gifts out of the cannon [true/false]" : "Включить выбрасывание подарков из пушки [true/false]")]
            public bool IsGiftCannonEnable { get; set; }

            [JsonProperty(En ? "Minimum time between throwing gifts [sec]" : "Минимальное время между выбрасываниями подарков [sec]")]
            public int MinTimeBetweenItems { get; set; }

            [JsonProperty(En ? "Maximum time between throwing gifts [sec]" : "Максимальное время между выбрасываниями подарков [sec]")]
            public int MaxTimeBetweenItems { get; set; }

            [JsonProperty(En ? "List of gifts" : "Список подарков")]
            public List<XMasCrateItem> Items { get; set; }
        }

        private class FireworksSetting
        {
            [JsonProperty(En ? "Turn on the fireworks [true/false]" : "Включить фейерверки [true/false]")]
            public bool IsFireworksOn { get; set; }

            [JsonProperty(En ? "The time between fireworks salvos [s]" : "Время между залпами фейерверков [s]")]
            public int TimeBetweenFireworks { get; set; }

            [JsonProperty(En ? "The number of shots in a salvo" : "Количество выстрелов в залпе")]
            public int NumberShotsInSalvo { get; set; }

            [JsonProperty(En ? "Activate fireworks only at night [true/false]" : "Активировать фейерверки только ночью [true/false]")]
            public bool IsNighFireworks { get; set; }
        }

        private class EventConfig
        {
            [JsonProperty(En ? "Preset Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty(En ? "Train Name" : "Название поезда")]
            public string DisplayName { get; set; }

            [JsonProperty(En ? "Event time" : "Время ивента")]
            public int EventTime { get; set; }

            [JsonProperty(En ? "Allow automatic startup? [true/false]" : "Разрешить автоматический запуск? [true/false]")]
            public bool IsAutoStart { get; set; }

            [JsonProperty(En ? "Probability of a preset [0.0-100.0]" : "Вероятность пресета [0.0-100.0]")]
            public float Chance { get; set; }

            [JsonProperty(En ? "The minimum time after the server's wipe when this preset can be selected automatically [sec]" : "Минимальное время после вайпа сервера, когда этот пресет может быть выбран автоматически [sec]")]
            public int MinTimeAfterWipe { get; set; }

            [JsonProperty(En ? "The maximum time after the server's wipe when this preset can be selected automatically [sec] (-1 - do not use this parameter)" : "Максимальное время после вайпа сервера, когда этот пресет может быть выбран автоматически [sec] (-1 - не использовать)")]
            public int MaxTimeAfterWipe { get; set; }

            [JsonProperty(En ? "Radius of the event zone" : "Радиус зоны ивента")]
            public float ZoneRadius { get; set; }

            [JsonProperty(En ? "Train can be spawned underground [true/false]" : "Поезд может появляться под землей [true/false]")]
            public bool IsUndergroundTrain { get; set; }

            [JsonProperty(En ? "Train Stop time" : "Время на которое останавливается поезд")]
            public int StopTime { get; set; }

            [JsonProperty(En ? "Locomotive Preset" : "Пресет локомотива")]
            public string LocomotivePreset { get; set; }

            [JsonProperty(En ? "Order of wagons" : "Порядок вагонов")]
            public List<string> WagonsPreset { get; set; }

            [JsonProperty(En ? "Heli preset" : "Пресет вертолета")]
            public string HeliPreset { get; set; }
        }

        private class LocomotiveConfig : WagonConfig
        {
            [JsonProperty(En ? "Engine force" : "Мощность двигателя", Order = 8)]
            public float EngineForce { get; set; }

            [JsonProperty(En ? "Max speed" : "Максимальная скорость", Order = 9)]
            public float MaxSpeed { get; set; }

            [JsonProperty(En ? "Driver name" : "Имя водителя", Order = 10)]
            public string DriverName { get; set; }

            [JsonProperty(En ? "Setting up the emergency brake" : "Настройка стоп-крана", Order = 11)]
            public EntitySpawnConfig HandleBrakeConfig { get; set; }

            [JsonProperty(En ? "Setting up a timer that displays the event time" : "Настройка таймера времени ивента", Order = 12)]
            public EntitySpawnConfig EventTimerConfig { get; set; }

            [JsonProperty(En ? "Setting up a timer that displays the stop time" : "Настройка таймера времени остановки", Order = 12)]
            public EntitySpawnConfig StopTimerConfig { get; set; }
        }

        private class EntitySpawnConfig
        {
            [JsonProperty(En ? "Enable spawn? [true/false]" : "Включить спавн? [true/false]", Order = 8)]
            public bool IsEnable { get; set; }

            [JsonProperty(En ? "Location" : "Расположение", Order = 8)]
            public LocationConfig Location { get; set; }
        }

        private class WagonConfig
        {
            [JsonProperty(En ? "Preset name" : "Название пресета", Order = 0)]
            public string PresetName { get; set; }

            [JsonProperty(En ? "Prefab name" : "Префаба", Order = 1)]
            public string PrefabName { get; set; }

            [JsonProperty(En ? "Bradley preset - locations" : "Пресет бредли - расположения", Order = 2)]
            public Dictionary<string, HashSet<LocationConfig>> Bradleys { get; set; }

            [JsonProperty(En ? "Turret preset - locations" : "Пресет турели - расположения", Order = 3)]
            public Dictionary<string, HashSet<LocationConfig>> Turrets { get; set; }

            [JsonProperty(En ? "SamSite preset - locations" : "Пресет SamSite - расположения", Order = 4)]
            public Dictionary<string, HashSet<LocationConfig>> SamSites { get; set; }

            [JsonProperty(En ? "Crate preset - locations" : "Пресет крейта - расположения", Order = 6)]
            public Dictionary<string, HashSet<LocationConfig>> Crates { get; set; }

            [JsonProperty(En ? "NPC preset - locations" : "Пресет NPC - расположения", Order = 5)]
            public Dictionary<string, HashSet<LocationConfig>> Npcs { get; set; }

            [JsonProperty(En ? "Decorative prefab - locations" : "Префаб декоративного блока - расположения", Order = 7)]
            public Dictionary<string, HashSet<LocationConfig>> Decors { get; set; }
        }

        private class MarkerConfig
        {
            [JsonProperty(En ? "Do you use the Marker? [true/false]" : "Использовать ли маркер? [true/false]")]
            public bool Enable { get; set; }

            [JsonProperty(En ? "Use a vending marker? [true/false]" : "Добавить маркер магазина? [true/false]")]
            public bool UseShopMarker { get; set; }

            [JsonProperty(En ? "Use a circular marker? [true/false]" : "Добавить круговой маркер? [true/false]")]
            public bool UseRingMarker { get; set; }

            [JsonProperty(En ? "Radius" : "Радиус")]
            public float Radius { get; set; }

            [JsonProperty(En ? "Alpha" : "Прозрачность")]
            public float Alpha { get; set; }

            [JsonProperty(En ? "Marker color" : "Цвет маркера")]
            public ColorConfig Color1 { get; set; }

            [JsonProperty(En ? "Outline color" : "Цвет контура")]
            public ColorConfig Color2 { get; set; }
        }

        private class ColorConfig
        {
            [JsonProperty("r")]
            public float R { get; set; }

            [JsonProperty("g")]
            public float G { get; set; }

            [JsonProperty("b")]
            public float B { get; set; }
        }

        private class BradleyConfig
        {
            [JsonProperty(En ? "Preset Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty("HP")]
            public float Hp { get; set; }

            [JsonProperty(En ? "Scale damage" : "Множитель урона")]
            public float ScaleDamage { get; set; }

            [JsonProperty(En ? "The viewing distance" : "Дальность обзора")]
            public float ViewDistance { get; set; }

            [JsonProperty(En ? "Radius of search" : "Радиус поиска")]
            public float SearchDistance { get; set; }

            [JsonProperty(En ? "The multiplier of Machine-gun aim cone" : "Множитель разброса пулемёта")]
            public float CoaxAimCone { get; set; }

            [JsonProperty(En ? "The multiplier of Machine-gun fire rate" : "Множитель скорострельности пулемёта")]
            public float CoaxFireRate { get; set; }

            [JsonProperty(En ? "Amount of Machine-gun burst shots" : "Кол-во выстрелов очереди пулемёта")]
            public int CoaxBurstLength { get; set; }

            [JsonProperty(En ? "The time between shots of the main gun [sec.]" : "Время между залпами основного орудия [sec.]")]
            public float NextFireTime { get; set; }

            [JsonProperty(En ? "The time between shots of the main gun in a fire rate [sec.]" : "Время между выстрелами основного орудия в залпе [sec.]")]
            public float TopTurretFireRate { get; set; }

            [JsonProperty(En ? "Number of crates" : "Количество крейтов")]
            public int CrateCount { get; set; }
            
            [JsonProperty(En ? "Open the crates immediately after spawn" : "Открывать ящики сразу после спавна")]
            public bool InstCrateOpen { get; set; }
            
            [JsonProperty(En ? "Loot" : "Лут")]
            public LootTableConfig LootTableConfig { get; set; }

            [JsonProperty(En ? "LootManager Preset" : "Пресет LootManager", NullValueHandling = NullValueHandling.Ignore)]
            public string LootManagerPreset { get; set; }
        }

        private class TurretConfig
        {
            [JsonProperty(En ? "Preset Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty(En ? "Health" : "Кол-во ХП")]
            public float Hp { get; set; }

            [JsonProperty(En ? "Weapon ShortName" : "ShortName оружия")]
            public string ShortNameWeapon { get; set; }

            [JsonProperty(En ? "Ammo ShortName" : "ShortName патронов")]
            public string ShortNameAmmo { get; set; }

            [JsonProperty(En ? "Number of ammo" : "Кол-во патронов")]
            public int CountAmmo { get; set; }

            [JsonProperty(En ? "Target detection range (0 - do not change)" : "Дальность обнаружения цели (0 - не изменять)")]
            public float TargetDetectionRange { get; set; }

            [JsonProperty(En ? "Target loss range (0 - do not change)" : "Дальность потери цели (0 - не изменять)")]
            public float TargetLossRange { get; set; }
        }

        private class SamSiteConfig
        {
            [JsonProperty(En ? "Preset Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty(En ? "Health" : "Кол-во ХП")]
            public float Hp { get; set; }

            [JsonProperty(En ? "Number of ammo" : "Кол-во патронов")]
            public int CountAmmo { get; set; }
        }

        private class HeliConfig
        {
            [JsonProperty(En ? "Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty("HP")]
            public float Hp { get; set; }

            [JsonProperty(En ? "HP of the main rotor" : "HP главного винта")]
            public float MainRotorHealth { get; set; }

            [JsonProperty(En ? "HP of tail rotor" : "HP хвостового винта")]
            public float RearRotorHealth { get; set; }

            [JsonProperty(En ? "Flying height" : "Высота полета")]
            public float Height { get; set; }

            [JsonProperty(En ? "Bullet speed" : "Скорость пуль")]
            public float BulletSpeed { get; set; }

            [JsonProperty(En ? "Bullet Damage" : "Урон пуль")]
            public float BulletDamage { get; set; }

            [JsonProperty(En ? "The distance to which the helicopter can move away from the convoy" : "Дистанция, на которую вертолет может отдаляться от конвоя")]
            public float Distance { get; set; }

            [JsonProperty(En ? "Speed" : "Скорость")]
            public float Speed { get; set; }

            [JsonProperty(En ? "The time for which the helicopter can leave the train to attack the target [sec.]" : "Время, на которое верталет может покидать поезд для атаки цели [sec.]")]
            public float OutsideTime { get; set; }
            
            [JsonProperty(En ? "Minimum number of crates the helicopter can drop" : "Минимальное количество ящиков, которое может сбросить вертолет")]
            public int MinCratesAmount { get; set; }
            
            [JsonProperty(En ? "Maximum number of crates the helicopter can drop" : "Максимальное количество ящиков, которое может сбросить вертолет")]
            public int MaxCratesAmount { get; set; }


            [JsonProperty(En ? "The helicopter will not aim for the nearest monument at death [true/false]" : "Вертолет не будет стремиться к ближайшему монументу при смерти [true/false]")]
            public bool ImmediatelyKill { get; set; }

            [JsonProperty(En ? "Open the crates immediately after spawn" : "Открывать ящики сразу после спавна")]
            public bool InstCrateOpen { get; set; }

            [JsonProperty(En ? "Lifetime of crates [sec]" : "Время жизни крейтов [sec]")]
            public float CratesLifeTime { get; set; }
            
            [JsonProperty(En ? "Loot" : "Лут")]
            public LootTableConfig LootTableConfig { get; set; }
        }

        private class NpcConfig
        {
            [JsonProperty(En ? "Preset Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty("Name")]
            public string DisplayName { get; set; }

            [JsonProperty(En ? "Health" : "Кол-во ХП")]
            public float Health { get; set; }

            [JsonProperty(En ? "Wear items" : "Одежда")]
            public List<NpcWear> WearItems { get; set; }

            [JsonProperty(En ? "Belt items" : "Быстрые слоты")]
            public List<NpcBelt> BeltItems { get; set; }

            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")]
            public float AttackRangeMultiplier { get; set; }

            [JsonProperty(En ? "Speed" : "Скорость")]
            public float Speed { get; set; }

            [JsonProperty(En ? "Roam Range" : "Дальность патрулирования местности")]
            public float RoamRange { get; set; }

            [JsonProperty(En ? "Chase Range" : "Дальность погони за целью")]
            public float ChaseRange { get; set; }

            [JsonProperty(En ? "Sense Range" : "Радиус обнаружения цели")]
            public float SenseRange { get; set; }

            [JsonProperty(En ? "Memory duration [sec.]" : "Длительность памяти цели [sec.]")]
            public float MemoryDuration { get; set; }

            [JsonProperty(En ? "Scale damage" : "Множитель урона")]
            public float DamageScale { get; set; }

            [JsonProperty(En ? "Turret damage scale" : "Множитель урона от турелей")]
            public float TurretDamageScale { get; set; }

            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")]
            public float AimConeScale { get; set; }

            [JsonProperty(En ? "Detect the target only in the NPC's viewing vision cone?" : "Обнаруживать цель только в углу обзора NPC? [true/false]")]
            public bool CheckVisionCone { get; set; }

            [JsonProperty(En ? "Vision Cone" : "Угол обзора")]
            public float VisionCone { get; set; }

            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")]
            public bool DisableRadio { get; set; }

            [JsonProperty("Kit")]
            public string Kit { get; set; }

            [JsonProperty(En ? "Should remove the corpse?" : "Удалять труп?")]
            public bool DeleteCorpse { get; set; }
            
            [JsonProperty(En ? "Sleep distance (0 - never sleep)" : "Дистанция сна (0 - никогда не спать)")]
            public float SleepDistance { get; set; }

            [JsonProperty(En ? "Ignore wounded players? [true/false]" : "Игнорировать раненых игроков? [true/false]")]
            public bool IgnoreWoundedPlayers { get; set; }
            
            [JsonProperty(En ? "Loot" : "Лут")]
            public LootTableConfig LootTableConfig { get; set; }
        }

        private class NpcWear
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty(En ? "skinID (0 - default)" : "SkinID (0 - default)")]
            public ulong SkinID { get; set; }
        }

        private class NpcBelt
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty(En ? "Amount" : "Кол-во")]
            public int Amount { get; set; }

            [JsonProperty(En ? "skinID (0 - default)" : "SkinID (0 - default)")]
            public ulong SkinID { get; set; }

            [JsonProperty(En ? "Mods" : "Модификации на оружие")]
            public HashSet<string> Mods { get; set; }

            [JsonProperty(En ? "Ammo" : "Патроны")]
            public string Ammo { get; set; }
        }

        private class CrateConfig
        {
            [JsonProperty(En ? "Preset Name" : "Название пресета")]
            public string PresetName { get; set; }

            [JsonProperty("Prefab")]
            public string Prefab { get; set; }

            [JsonProperty("Skin")]
            public ulong Skin { get; set; }

            [JsonProperty(En ? "Time to unlock the crates (LockedCrate) [sec.]" : "Время до открытия заблокированного ящика (LockedCrate) [sec.]")]
            public float HackTime { get; set; }
            
            [JsonProperty(En ? "Loot" : "Лут")]
            public LootTableConfig LootTableConfig { get; set; }
        }
        
        
        private class LootTableConfig
        {
            [JsonProperty(En ? "Allow AlphaLoot to Modify Loot" : "Разрешить плагину AlphaLoot изменять лут")]
            public bool IsAlphaLoot { get; set; }
            
            [JsonProperty(En ? "Use AlphaLoot Preset with This Name" : "Использовать пресет AlphaLoot с этим названием")]
            public string AlphaLootPreset { get; set; }
            
            [JsonProperty(En ? "Allow LoottableStacksizeGUI to Modify Loot" : "Разрешить плагину LoottableStacksizeGUI изменять лут")]
            public bool IsLoottablePlugin { get; set; }
            
            [JsonProperty(En ? "Use LoottableStacksizeGUI Preset with This Name" : "Использовать пресет LoottableStacksizeGUI с этим названием")]
            public string LoottablePreset { get; set; }
            
            [JsonProperty(En ? "Allow CustomLoot to Modify Loot" : "Разрешить плагину CustomLoot изменять лут")]
            public bool IsCustomLootPlugin { get; set; }
            
            [JsonProperty(En ? "Use CustomLoot Preset with This Name" : "Использовать пресет CustomLoot с этим названием")]
            public string CustomLootPreset { get; set; }

            
            [JsonProperty(En ? "Clear the container before adding items/prefabs" : "Очистить контейнер перед добавлением предметов/префабов")]
            public bool ClearDefaultLoot { get; set; }
            
            [JsonProperty(En ? "Prefabs Table" : "Таблица префабов")]
            public PrefabsLootTableConfig PrefabsTable { get; set; }
            
            [JsonProperty(En ? "Items Table" : "Таблица предметов")]
            public ItemsLootTableConfig ItemsTable { get; set; }
        }

        private class ItemsLootTableConfig
        {
            [JsonProperty(En ? "Enabled" : "Включить")]
            public bool IsEnabled { get; set; }
            
            [JsonProperty(En ? "Use Chance-Based Spawn (Ignore Min/Max Unique Items)" : "Выпадение по шансу (игнорировать Min/Max разных предметов)")]
            public bool DisableMinMax { get; set; }
                
            [JsonProperty(En ? "Minimum Unique Items" : "Минимум разных предметов")]
            public int MinItemsAmount { get; set; }

            [JsonProperty(En ? "Maximum Unique Items" : "Максимум разных предметов")]
            public int MaxItemsAmount { get; set; }
            
            [JsonProperty(En ? "Item Pool" : "Пул предметов")]
            public List<LootItemConfig> Items { get; set; }
        }
        
        private class PrefabsLootTableConfig
        {
            [JsonProperty(En ? "Enabled" : "Включить")]
            public bool IsEnabled { get; set; }
            
            [JsonProperty(En ? "Minimum Unique Prefabs" : "Минимум разных префабов")]
            public int MinPrefabsAmount { get; set; }

            [JsonProperty(En ? "Maximum Unique Prefabs" : "Максимум разных префабов")]
            public int MaxPrefabsAmount { get; set; }
            
            [JsonProperty(En ? "Prefab Pool" : "Пул префабов")]
            public List<LootPrefabConfig> Prefabs { get; set; }
        }
        
        private class LootItemConfig : LootElementChanceConfig
        {
            [JsonProperty("Shortname")]
            public string Shortname { get; set; }
            
            [JsonProperty("SkinID")]
            public ulong Skin { get; set; }
            
            [JsonProperty(En ? "Display Name (empty - default)" : "Отображаемое название (оставить пустым - стандартное)")]
            public string DisplayName { get; set; }
            
            [JsonProperty(En ? "Owner Display Name" : "Имя владельца предмета")]
            public string OwnerName { get; set; }
            
            [JsonProperty(En ? "Is Blueprint" : "Это чертеж")]
            public bool IsBlueprint { get; set; }
            
            [JsonProperty(En ? "Genomes" : "Геномы")]
            public List<string> Genomes { get; set; }
        }

        private class LootPrefabConfig : LootElementChanceConfig
        {
            [JsonProperty(En ? "Prefab" : "Префаб")]
            public string PrefabName { get; set; }
        }

        private class LootElementChanceConfig
        {
            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс [0.0-100.0]")]
            public float Chance { get; set; }
            
            [JsonProperty(En ? "Minimum Amount" : "Минимальное количество")]
            public int MinAmount { get; set; }
            
            [JsonProperty(En ? "Maximum Amount" : "Максимальное количество")]
            public int MaxAmount { get; set; }
        }
        

        private class XMasCrateItem
        {
            [JsonProperty("ShortName")]
            public string Shortname { get; set; }

            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")]
            public int MinAmount { get; set; }

            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")]
            public int MaxAmount { get; set; }

            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")]
            public float Chance { get; set; }

            [JsonProperty(En ? "Is this a blueprint? [true/false]" : "Это чертеж? [true/false]")]
            public bool IsBlueprint { get; set; }

            [JsonProperty("SkinID (0 - default)")]
            public ulong Skin { get; set; }

            [JsonProperty(En ? "Name (empty - default)" : "Название (empty - default)")]
            public string Name { get; set; }

            [JsonProperty(En ? "List of genomes" : "Список геномов")]
            public List<string> Genomes { get; set; }
        }
        
        

        private class LocationConfig
        {
            [JsonProperty(En ? "Position" : "Позиция")]
            public string Position { get; set; }

            [JsonProperty(En ? "Rotation" : "Вращение")]
            public string Rotation { get; set; }
        }

        private class ZoneConfig
        {
            [JsonProperty(En ? "Create a PVP zone in the convoy isStop zone? (only for those who use the TruePVE plugin)[true/false]" : "Создавать зону PVP в зоне проведения ивента? (только для тех, кто использует плагин TruePVE) [true/false]")]
            public bool IsPvpZone { get; set; }

            [JsonProperty(En ? "Use the dome? [true/false]" : "Использовать ли купол? [true/false]")]
            public bool IsDome { get; set; }

            [JsonProperty(En ? "Darkening the dome" : "Затемнение купола")]
            public int Darkening { get; set; }

            [JsonProperty(En ? "Use a colored border? [true/false]" : "Использовать цветную границу? [true/false]")]
            public bool IsColoredBorder { get; set; }

            [JsonProperty(En ? "Border color (0 - blue, 1 - green, 2 - purple, 3 - red)" : "Цвет границы (0 - синий, 1 - зеленый, 2 - фиолетовый, 3 - красный)")]
            public int BorderColor { get; set; }

            [JsonProperty(En ? "Brightness of the color border" : "Яркость цветной границы")]
            public int Brightness { get; set; }

            [JsonProperty(En ? "Radius" : "Радиус")]
            public float Radius { get; set; }
        }

        private class GUIConfig
        {
            [JsonProperty(En ? "Use the Countdown GUI? [true/false]" : "Использовать ли GUI обратного отсчета? [true/false]")]
            public bool IsEnable { get; set; }

            [JsonProperty(En ? "Vertical offset" : "Смещение по вертикали")]
            public int OffsetMinY { get; set; }
        }

        private class NotifyConfig
        {
            [JsonProperty(En ? "The time from the notification to the start of the event [sec]" : "Время от оповещения до начала ивента [sec]")]
            public int PreStartTime { get; set; }

            [JsonProperty(En ? "Use a chat? [true/false]" : "Использовать ли чат? [true/false]")]
            public bool IsChatEnable { get; set; }

            [JsonProperty(En ? "The time until the end of the event, when a message is displayed about the time until the end of the event [sec]" : "Время до конца ивента, когда выводится сообщение о сокром окончании ивента [sec]")]
            public HashSet<int> TimeNotifications { get; set; }

            [JsonProperty(En ? "Facepunch Game Tips setting" : "Настройка сообщений Facepunch Game Tip")]
            public GameTipConfig GameTipConfig { get; set; }
        }

        private class GameTipConfig
        {
            [JsonProperty(En ? "Use Facepunch Game Tips (notification bar above hotbar)? [true/false]" : "Использовать ли Facepunch Game Tip (оповещения над слотами быстрого доступа игрока)? [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty(En ? "Style (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)" : "Стиль (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)")]
            public int Style { get; set; }
        }

        private class PveModeConfig
        {
            [JsonProperty(En ? "Use the PVE mode of the plugin? [true/false]" : "Использовать PVE режим работы плагина? [true/false]")]
            public bool Enable { get; set; }

            [JsonProperty(En ? "Allow administrators to loot crates and cause damage? [true/false]" : "Разрешить администраторам лутать ящики и наносить урон? [true/false]")]
            public bool IgnoreAdmin { get; set; }

            [JsonProperty(En ? "The owner of the event will be the one who stopped the event? [true/false]" : "Владельцем ивента будет становиться тот кто остановил ивент? [true/false]")]
            public bool OwnerIsStopper { get; set; }

            [JsonProperty(En ? "If a player has a cooldown and the event has NO OWNERS, then he will not be able to interact with the event? [true/false]" : "Если у игрока кулдаун, а у ивента НЕТ ВЛАДЕЛЬЦЕВ, то он не сможет взаимодействовать с ивентом? [true/false]")]
            public bool NoInteractIfCooldownAndNoOwners { get; set; }

            [JsonProperty(En ? "If a player has a cooldown, and the event HAS AN OWNER, then he will not be able to interact with the event, even if he is on a team with the owner? [true/false]" : "Если у игрока кулдаун, а у ивента ЕСТЬ ВЛАДЕЛЕЦ, то он не сможет взаимодействовать с ивентом, даже если находится в команде с владельцем? [true/false]")]
            public bool NoDealDamageIfCooldownAndTeamOwner { get; set; }

            [JsonProperty(En ? "Allow only the owner or his teammates to loot crates? [true/false]" : "Разрешить лутать ящики только владельцу или его тиммейтам? [true/false]")]
            public bool CanLootOnlyOwner { get; set; }

            [JsonProperty(En ? "Show the displayName of the event owner on a marker on the map? [true/false]" : "Отображать имя владелца ивента на маркере на карте? [true/false]")]
            public bool ShowEventOwnerNameOnMap { get; set; }

            [JsonProperty(En ? "The amount of damage that the player has to do to become the Event Owner" : "Кол-во урона, которое должен нанести игрок, чтобы стать владельцем ивента")]
            public float Damage { get; set; }

            [JsonProperty(En ? "Damage coefficients for calculate to become the Event Owner." : "Коэффициенты урона для подсчета, чтобы стать владельцем события.")]
            public Dictionary<string, float> ScaleDamage { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event loot the crates? [true/false]" : "Может ли не владелец ивента грабить ящики? [true/false]")]
            public bool LootCrate { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event hack locked crates? [true/false]" : "Может ли не владелец ивента взламывать заблокированные ящики? [true/false]")]
            public bool HackCrate { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event loot NPC corpses? [true/false]" : "Может ли не владелец ивента грабить трупы NPC? [true/false]")]
            public bool LootNpc { get; set; }

            [JsonProperty(En ? "Can an Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")]
            public bool TargetNpc { get; set; }

            [JsonProperty(En ? "Can Bradley attack a non-owner of the event? [true/false]" : "Может ли Bradley атаковать не владельца ивента? [true/false]")]
            public bool TargetTank { get; set; }

            [JsonProperty(En ? "Can Helicopter attack a non-owner of the event? [true/false]" : "Может ли Вертолет атаковать не владельца ивента? [true/false]")]
            public bool TargetHeli { get; set; }

            [JsonProperty(En ? "Can Turret attack a non-owner of the event? [true/false]" : "Может ли Турель атаковать не владельца ивента? [true/false]")]
            public bool TargetTurret { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event deal damage to the NPC? [true/false]" : "Может ли не владелец ивента наносить урон по NPC? [true/false]")]
            public bool DamageNpc { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event do damage to Helicopter? [true/false]" : "Может ли не владелец ивента наносить урон по Вертолету? [true/false]")]
            public bool DamageHeli { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event do damage to Bradley? [true/false]" : "Может ли не владелец ивента наносить урон по Bradley? [true/false]")]
            public bool DamageTank { get; set; }

            [JsonProperty(En ? "Can the non-owner of the event do damage to Turret? [true/false]" : "Может ли не владелец ивента наносить урон по Турелям? [true/false]")]
            public bool DamageTurret { get; set; }

            [JsonProperty(En ? "Allow the non-owner of the event to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента не владельцу ивента? [true/false]")]
            public bool CanEnter { get; set; }

            [JsonProperty(En ? "Allow a player who has an active cooldown of the Event Owner to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента игроку, у которого активен кулдаун на получение статуса владельца ивента? [true/false]")]
            public bool CanEnterCooldownPlayer { get; set; }

            [JsonProperty(En ? "The time that the Event Owner may not be inside the event zone [sec.]" : "Время, которое владелец ивента может не находиться внутри зоны ивента [сек.]")]
            public int TimeExitOwner { get; set; }

            [JsonProperty(En ? "The time until the end of Event Owner status when it is necessary to warn the player [sec.]" : "Время таймера до окончания действия статуса владельца ивента, когда необходимо предупредить игрока [сек.]")]
            public int AlertTime { get; set; }

            [JsonProperty(En ? "Prevent the actions of the RestoreUponDeath plugin in the event zone? [true/false]" : "Запрещать работу плагина RestoreUponDeath в зоне действия ивента? [true/false]")]
            public bool RestoreUponDeath { get; set; }

            [JsonProperty(En ? "The time that the player can`t become the Event Owner, after the end of the event and the player was its owner [sec.]" : "Время, которое игрок не сможет стать владельцем ивента, после того как ивент окончен и игрок был его владельцем [sec.]")]
            public double Cooldown { get; set; }

            [JsonProperty(En ? "Darkening the dome (0 - disables the dome)" : "Затемнение купола (0 - отключает купол)")]
            public int Darkening { get; set; }
        }

        private class EconomyConfig
        {
            [JsonProperty(En ? "Enable economy" : "Включить экономику?")]
            public bool Enable { get; set; }

            [JsonProperty(En ? "Which economy plugins do you want to use? (Economics, Server Rewards, IQEconomic)" : "Какие плагины экономики вы хотите использовать? (Economics, Server Rewards, IQEconomic)")]
            public HashSet<string> Plugins { get; set; }

            [JsonProperty(En ? "The minimum value that a player must collect to get points for the economy" : "Минимальное значение, которое игрок должен заработать, чтобы получить баллы за экономику")]
            public double MinEconomyPoint { get; set; }

            [JsonProperty(En ? "The minimum value that a winner must collect to make the commands work" : "Минимальное значение, которое победитель должен заработать, чтобы сработали команды")]
            public double MinCommandPoint { get; set; }

            [JsonProperty(En ? "Looting of crates" : "Ограбление ящиков")]
            public Dictionary<string, double> Crates { get; set; }

            [JsonProperty(En ? "Killing an NPC" : "Убийство NPC")]
            public double NpcPoint { get; set; }

            [JsonProperty(En ? "Killing an Bradley" : "Уничтожение Bradley")]
            public double BradleyPoint { get; set; }

            [JsonProperty(En ? "Killing an Turret" : "Уничтожение Турели")]
            public double TurretPoint { get; set; }

            [JsonProperty(En ? "Killing an Heli" : "Уничтожение Вертолета")]
            public double HeliPoint { get; set; }

            [JsonProperty(En ? "Hacking a locked crate" : "Взлом заблокированного ящика")]
            public double HackCratePoint { get; set; }

            [JsonProperty(En ? "List of commands that are executed in the console at the end of the event ({steamid} - the player who collected the highest number of points)" : "Список команд, которые выполняются в консоли по окончанию ивента ({steamid} - игрок, который набрал наибольшее кол-во баллов)")]
            public HashSet<string> Commands { get; set; }
        }

        private class GUIAnnouncementsConfig
        {
            [JsonProperty(En ? "Do you use the GUI Announcements? [true/false]" : "Использовать ли GUI Announcements? [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty(En ? "Banner color" : "Цвет баннера")]
            public string BannerColor { get; set; }

            [JsonProperty(En ? "Text color" : "Цвет текста")]
            public string TextColor { get; set; }

            [JsonProperty(En ? "Adjust Vertical Position" : "Отступ от верхнего края")]
            public float APIAdjustVPosition { get; set; }
        }

        private class NotifyPluginConfig
        {
            [JsonProperty(En ? "Do you use the Notify? [true/false]" : "Использовать ли Notify? [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty(En ? "Type" : "Тип")]
            public int Type { get; set; }
        }

        private class DiscordConfig
        {
            [JsonProperty(En ? "Do you use the Discord? [true/false]" : "Использовать ли Discord? [true/false]")]
            public bool IsEnabled { get; set; }

            [JsonProperty("Webhook URL")]
            public string WebhookUrl { get; set; }

            [JsonProperty(En ? "Embed Color (DECIMAL)" : "Цвет полосы (DECIMAL)")]
            public int EmbedColor { get; set; }

            [JsonProperty(En ? "Keys of required messages" : "Ключи необходимых сообщений")]
            public HashSet<string> Keys { get; set; }
        }

        private class SupportedPluginsConfig
        {
            [JsonProperty(En ? "PVE Mode Setting" : "Настройка PVE Mode")]
            public PveModeConfig PveMode { get; set; }

            [JsonProperty(En ? "Economy Setting" : "Настройка экономики")]
            public EconomyConfig EconomicsConfig { get; set; }

            [JsonProperty(En ? "GUI Announcements setting" : "Настройка GUI Announcements")]
            public GUIAnnouncementsConfig GUIAnnouncementsConfig { get; set; }

            [JsonProperty(En ? "Notify setting" : "Настройка Notify")]
            public NotifyPluginConfig NotifyPluginConfig { get; set; }

            [JsonProperty(En ? "DiscordMessages setting" : "Настройка DiscordMessages")]
            public DiscordConfig DiscordMessagesConfig { get; set; }

            [JsonProperty(En ? "BetterNpc setting" : "Настройка BetterNpc")]
            public BetterNpcConfig BetterNpcConfig { get; set; }
        }

        private class BetterNpcConfig
        {
            [JsonProperty(En ? "Allow Npc spawn after destroying Heli" : "Разрешить спавн Npc после уничтожения Вертолета")]
            public bool IsHeliNpc { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Version" : "Версия")]
            public VersionNumber VersionConfig { get; set; }

            [JsonProperty(En ? "Prefix of chat messages" : "Префикс в чате")]
            public string Prefix { get; set; }

            [JsonProperty(En ? "Main Setting" : "Основные настройки")]
            public MainConfig MainConfig { get; set; }

            [JsonProperty(En ? "Customization Settings" : "Настройки кастомизации")]
            public CustomizationConfig CustomizationConfig { get; set; }

            [JsonProperty(En ? "Train presets" : "Пресеты поездов")]
            public HashSet<EventConfig> EventConfigs { get; set; }

            [JsonProperty(En ? "Locomotive presets" : "Пресеты локомотивов")]
            public HashSet<LocomotiveConfig> LocomotiveConfigs { get; set; }

            [JsonProperty(En ? "Wagon presets" : "Пресеты вагонов")]
            public HashSet<WagonConfig> WagonConfigs { get; set; }

            [JsonProperty(En ? "Bradley presets" : "Пресеты бредли")]
            public HashSet<BradleyConfig> Bradleys { get; set; }

            [JsonProperty(En ? "Turrets presets" : "Пресеты турелей")]
            public HashSet<TurretConfig> TurretConfigs { get; set; }

            [JsonProperty(En ? "Samsite presets" : "Пресеты Samsite")]
            public HashSet<SamSiteConfig> SamsiteConfigs { get; set; }

            [JsonProperty(En ? "Crate presets" : "Пресеты ящиков")]
            public HashSet<CrateConfig> CrateConfigs { get; set; }

            [JsonProperty(En ? "Heli presets" : "Пресеты вертолетов")]
            public HashSet<HeliConfig> HeliConfigs { get; set; }

            [JsonProperty(En ? "NPC presets" : "Пресеты NPC")]
            public HashSet<NpcConfig> NpcConfigs { get; set; }

            [JsonProperty(En ? "Marker Setting" : "Настройки маркера")]
            public MarkerConfig MarkerConfig { get; set; }

            [JsonProperty(En ? "Zone Setting" : "Настройки зоны ивента")]
            public ZoneConfig ZoneConfig { get; set; }

            [JsonProperty(En ? "GUI Setting" : "Настройки GUI")]
            public GUIConfig GUIConfig { get; set; }

            [JsonProperty(En ? "Notification Settings" : "Настройки уведомлений")]
            public NotifyConfig NotifyConfig { get; set; }

            [JsonProperty(En ? "Supported Plugins" : "Поддерживаемые плагины")]
            public SupportedPluginsConfig SupportedPluginsConfig { get; set; }

            // ReSharper disable All
            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig()
                {
                    VersionConfig = new VersionNumber(2, 0, 4),
                    Prefix = "[ArmoredTrain]",
                    MainConfig = new MainConfig
                    {
                        IsAutoEvent = true,
                        MinTimeBetweenEvents = 7200,
                        MaxTimeBetweenEvents = 7200,
                        UndergroundChance = 0,
                        IsAggressive = false,
                        AggressiveTime = 300,
                        NeedStopTrain = false,
                        NeedKillNpc = false,
                        NeedKillBradleys = false,
                        NeedKillTurrets = false,
                        NeedKillHeli = false,
                        StopTrainAfterReceivingDamage = false,
                        IsRestoreStopTimeAfterDamageOrLoot = true,
                        KillTrainAfterLoot = true,
                        EndAfterLootTime = 300,
                        DestroyWagons = false,
                        AllowDriverDamage = true,
                        ReviveTrainDriver = true,
                        EnableStartStopLogs = false,
                        IsTurretDropWeapon = false,
                        MaxGroundDamageDistance = 100,
                        MaxHeliDamageDistance = 250,
                        EnableFrontConnector = false,
                        EnableBackConnector = true,
                        AllowEnableMovingByHandbrake = true,
                        IsNpcJumpOnSurface = true,
                        IsNpcJumpInSubway = true,
                        DontStopEventIfPlayerInZone = false,
                        CustomSpawnPointConfig = new CustomSpawnPointConfig
                        {
                            IsEnabled = false,
                            Points = new HashSet<LocationConfig>(),
                        },
                        MaxDamageHeight = -1,
                        MaxFoundationHeightAboveGround = 5f
                    },
                    CustomizationConfig = new CustomizationConfig
                    {
                        IsHalloween = false,
                        IsChristmas = false,
                        IsElectricFurnacesEnable = false,
                        IsBoilersEnable = true,
                        IsFireEnable = true,
                        IsNeonSignsEnable = true,
                        IsLightOnlyAtNight = true,

                        GiftCannonSetting = new GiftCannonSetting
                        {
                            IsGiftCannonEnable = false,
                            MinTimeBetweenItems = 1,
                            MaxTimeBetweenItems = 60,
                            Items = new List<XMasCrateItem>
                            {
                                new XMasCrateItem
                                {
                                    Shortname = "xmas.present.small",
                                    MinAmount = 1,
                                    MaxAmount = 1,
                                    Chance = 80,
                                    IsBlueprint = false,
                                    Skin = 0,
                                    Name = "",
                                    Genomes = new List<string>()
                                },
                                new XMasCrateItem
                                {
                                    Shortname = "xmas.present.medium",
                                    MinAmount = 1,
                                    MaxAmount = 1,
                                    Chance = 15,
                                    IsBlueprint = false,
                                    Skin = 0,
                                    Name = "",
                                    Genomes = new List<string>()
                                },
                                new XMasCrateItem
                                {
                                    Shortname = "xmas.present.large",
                                    MinAmount = 1,
                                    MaxAmount = 1,
                                    Chance = 5,
                                    IsBlueprint = false,
                                    Skin = 0,
                                    Name = "",
                                    Genomes = new List<string>()
                                }
                            }
                        },
                        FireworksSettings = new FireworksSetting
                        {
                            IsFireworksOn = true,
                            TimeBetweenFireworks = 600,
                            NumberShotsInSalvo = 5,
                            IsNighFireworks = true,
                        }
                    },
                    EventConfigs = new HashSet<EventConfig>
                    {
                        new EventConfig
                        {
                            PresetName = "train_easy",
                            DisplayName = En ? "Small Train" : "Небольшой поезд",
                            IsAutoStart = true,
                            Chance = 40,
                            MinTimeAfterWipe = 0,
                            MaxTimeAfterWipe = 172800,
                            ZoneRadius = 100,
                            IsUndergroundTrain = true,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_default",
                            WagonsPreset = new List<string>
                            {
                                "wagon_crate_1"
                            },
                            HeliPreset = ""
                        },
                        new EventConfig
                        {
                            PresetName = "train_normal",
                            DisplayName = En ? "Train" : "Поезд",
                            IsAutoStart = true,
                            Chance = 40,
                            MinTimeAfterWipe = 10800,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_turret",
                            WagonsPreset = new List<string>
                            {
                                "wagon_bradley",
                                "wagon_crate_1",
                                "wagon_samsite"
                            },
                            HeliPreset = ""
                        },
                        new EventConfig
                        {
                            PresetName = "train_hard",
                            DisplayName = En ? "Giant Train" : "Огромный поезд",
                            IsAutoStart = true,
                            Chance = 20,
                            MinTimeAfterWipe = 36000,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_new",
                            WagonsPreset = new List<string>
                            {
                                "wagon_bradley",
                                "wagon_crate_2",
                                "wagon_bradley",
                                "wagon_samsite"
                            },
                            HeliPreset = "heli_1"
                        },
                        new EventConfig
                        {
                            PresetName = "train_caboose",
                            DisplayName = "Caboose",
                            IsAutoStart = false,
                            Chance = 0,
                            MinTimeAfterWipe = 0,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 0,
                            LocomotivePreset = "locomotive_default",
                            WagonsPreset = new List<string>
                            {
                                "caboose_wagon"
                            },
                            HeliPreset = ""
                        },
                        new EventConfig
                        {
                            PresetName = "train_halloween",
                            DisplayName = En ? "Halloween Train" : "Хэллоуинский Поезд",
                            IsAutoStart = false,
                            Chance = 0,
                            MinTimeAfterWipe = 0,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_new",
                            WagonsPreset = new List<string>
                            {
                                "wagon_crate_1",
                                "halloween_wagon",
                                "wagon_samsite"

                            },
                            HeliPreset = ""
                        },
                        new EventConfig
                        {
                            PresetName = "train_xmas_easy",
                            DisplayName = En ? "Small Christmas train" : "Маленький Новогодний Поезд",
                            IsAutoStart = false,
                            Chance = 0,
                            MinTimeAfterWipe = 0,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_default",
                            WagonsPreset = new List<string>
                            {
                                "xmas_wagon_1"
                            },
                            HeliPreset = ""
                        },
                        new EventConfig
                        {
                            PresetName = "train_xmas_medium",
                            DisplayName = En ? "Medium Christmas train" : "Средний Новогодний Поезд",
                            IsAutoStart = true,
                            Chance = 0,
                            MinTimeAfterWipe = 0,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_turret",
                            WagonsPreset = new List<string>
                            {
                                "xmas_wagon_1",
                                "xmas_wagon_2"
                            },
                            HeliPreset = ""
                        },
                        new EventConfig
                        {
                            PresetName = "train_xmas_hard",
                            DisplayName = En ? "Big Christmas train" : "Большой Новогодний Поезд",
                            IsAutoStart = true,
                            Chance = 0,
                            MinTimeAfterWipe = 0,
                            MaxTimeAfterWipe = -1,
                            ZoneRadius = 100,
                            IsUndergroundTrain = false,
                            EventTime = 3600,
                            StopTime = 300,
                            LocomotivePreset = "locomotive_new",
                            WagonsPreset = new List<string>
                            {
                                "wagon_crate_2",
                                "xmas_wagon_2",
                                "wagon_bradley"
                            },
                            HeliPreset = ""
                        }
                    },
                    LocomotiveConfigs = new HashSet<LocomotiveConfig>
                    {
                        new LocomotiveConfig
                        {
                            PresetName = "locomotive_default",
                            PrefabName = "assets/content/vehicles/trains/workcart/workcart_aboveground.entity.prefab",
                            EngineForce = 250000f,
                            MaxSpeed = 12,
                            DriverName = "traindriver",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, 4)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, 2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, 0)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.742, 1.458, -3.5)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, -3.5)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            HandleBrakeConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.097, 2.805, 1.816)",
                                    Rotation = "(0, 180, 0)"
                                }
                            },
                            EventTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.097, 2.412, 1.810)",
                                    Rotation = "(0, 180, 0)"
                                }
                            },
                            StopTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.097, 3.012, 1.810)",
                                    Rotation = "(0, 180, 0)"
                                }
                            }
                        },
                        new LocomotiveConfig
                        {
                            PresetName = "locomotive_turret",
                            PrefabName = "assets/content/vehicles/trains/workcart/workcart_aboveground2.entity.prefab",
                            EngineForce = 250000f,
                            MaxSpeed = 12,
                            DriverName = "traindriver",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0.684, 3.845, 3.683)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                },
                                ["turret_m249"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0.945, 2.627, 0.556)",
                                        Rotation = "(0, 313, 0)"
                                    }
                                }
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, 4)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, 2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, 0)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.742, 1.458, -3.5)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.742, 1.458, -3.5)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            HandleBrakeConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.097, 2.805, 1.816)",
                                    Rotation = "(0, 180, 0)"
                                }
                            },
                            EventTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.097, 2.412, 1.810)",
                                    Rotation = "(0, 180, 0)"
                                }
                            },
                            StopTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.097, 3.012, 1.810)",
                                    Rotation = "(0, 180, 0)"
                                }
                            }
                        },
                        new LocomotiveConfig
                        {
                            PresetName = "locomotive_new",
                            PrefabName = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab",
                            EngineForce = 500000f,
                            MaxSpeed = 14,
                            DriverName = "traindriver",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_m249"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.554, 1.546, -8.849)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.554, 1.546, -8.849)",
                                        Rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, 2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, 0)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, -2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, -4)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, -6)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, -8)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, 2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, 0)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, -2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, -4)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, -6)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, -8)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            HandleBrakeConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.270, 2.805, -7.896)",
                                    Rotation = "(0, 145.462, 0)"
                                }
                            },
                            EventTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.270, 2.412, -7.896)",
                                    Rotation = "(0, 145.462, 0)"
                                }
                            },
                            StopTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.270, 3.012, -7.896)",
                                    Rotation = "(0, 145.462, 0)"
                                }
                            }
                        },

                        new LocomotiveConfig
                        {
                            PresetName = "locomotive_halloween",
                            PrefabName = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab",
                            EngineForce = 500000f,
                            MaxSpeed = 14,
                            DriverName = "traindriver",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, 2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, -2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.341, 1.546, -6)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, 2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, -2)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.341, 1.546, -6)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            HandleBrakeConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.270, 2.805, -7.896)",
                                    Rotation = "(0, 145.462, 0)"
                                }
                            },
                            EventTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.270, 2.412, -7.896)",
                                    Rotation = "(0, 145.462, 0)"
                                }
                            },
                            StopTimerConfig = new EntitySpawnConfig
                            {
                                IsEnable = true,
                                Location = new LocationConfig
                                {
                                    Position = "(0.270, 3.012, -7.896)",
                                    Rotation = "(0, 145.462, 0)"
                                }
                            }
                        }
                    },
                    WagonConfigs = new HashSet<WagonConfig>
                    {
                        new WagonConfig
                        {
                            PresetName = "wagon_crate_1",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonc.entity.prefab",

                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.940, 1.559, -6.811)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.940, 1.559, -6.811)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.940, 1.559, 6.811)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.940, 1.559, 6.811)",
                                        Rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["chinooklockedcrate_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0, 1.550, 0)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                },
                                ["crateelite_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0.7, 1.550, -2.359)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.7, 1.550, -2.359)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.7, 1.550, 2.359)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.7, 1.550, 2.359)",
                                        Rotation = "(0, 180, 0)"
                                    }
                                },
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        },
                        new WagonConfig
                        {
                            PresetName = "wagon_crate_2",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab",

                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-1.177, 1.458, -2.267)",
                                        Rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.177, 1.458, 0.475)",
                                        Rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-1.177, 1.458, 3.202)",
                                        Rotation = "(0, 270, 0)"
                                    }
                                }
                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["chinooklockedcrate_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.772, 1.550, 5.693)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.772, 1.550, -5.693)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                },
                                ["crateelite_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(1.076, 1.550, 1.047)",
                                        Rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.076, 1.550, -0.609)",
                                        Rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(1.076, 1.550, -2.359)",
                                        Rotation = "(0, 270, 0)"
                                    }
                                },
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        },
                        new WagonConfig
                        {
                            PresetName = "wagon_bradley",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["bradley_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.185, 2.206, -3.36)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.185, 2.206, 3.460)",
                                        Rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["assets/content/vehicles/modularcar/module_entities/2module_fuel_tank.prefab"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.772, 3.008, -4.295)",
                                        Rotation = "(0, 0, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.772, 3.008, -0.232)",
                                        Rotation = "(0, 0, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.812, 1.659, -3.221)",
                                        Rotation = "(90, 270, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        Position = "(0.772, 3.008, 4.295)",
                                        Rotation = "(0, 180, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.772, 3.008, 0.232)",
                                        Rotation = "(0, 180, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.812, 1.659, 3.221)",
                                        Rotation = "(90, 90, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        Position = "(-0.757, 1.659, 3.226)",
                                        Rotation = "(90, 270, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        Position = "(0.516, 1.7, 5.521)",
                                        Rotation = "(90, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.516, 1.7, 5.521)",
                                        Rotation = "(90, 0, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        Position = "(0.516, 1.7, -5.521)",
                                        Rotation = "(90, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.516, 1.7, -5.521)",
                                        Rotation = "(90, 180, 0)"
                                    }
                                }
                            }
                        },
                        new WagonConfig
                        {
                            PresetName = "wagon_samsite",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadablefuel.entity.prefab",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0, 4.296, -5.346)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0, 4.296, 5.346)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["samsite_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0, 4.216, 0)",
                                        Rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            Decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        },
                        new WagonConfig
                        {
                            PresetName = "caboose_wagon",
                            PrefabName = "assets/content/vehicles/trains/caboose/traincaboose.entity.prefab",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            Crates = new Dictionary<string, HashSet<LocationConfig>>(),
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>(),
                            Decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>()
                        },
                        new WagonConfig
                        {
                            PresetName = "halloween_wagon",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["crate_normal_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0.407, 2.403, -3.401)",
                                        Rotation = "(303.510, 0, 328.794)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.374, 2.416, 3.104)",
                                        Rotation = "(21.106, 261.772, 352.540)"
                                    }
                                },
                                ["crate_normal2_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.095, 1.817, -0.217)",
                                        Rotation = "(19.048, 336.704, 359.624)"
                                    }
                                }
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>(),
                            Decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.940, 1.559, -6.811)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.940, 1.559, -6.811)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.940, 1.559, 6.811)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.940, 1.559, 6.811)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                        },

                        new WagonConfig
                        {
                            PresetName = "xmas_wagon_1",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["xmas_crate"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.148, 2.707, -1.613)",
                                        Rotation = "(72.214, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.221, 2.478, -2.314)",
                                        Rotation = "(52.555, 180.000, 0)"
                                    }
                                }
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>(),
                            Decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(-0.940, 1.559, -6.811)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.940, 1.559, -6.811)",
                                        Rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(-0.940, 1.559, 6.811)",
                                        Rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.940, 1.559, 6.811)",
                                        Rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                        },
                        new WagonConfig
                        {
                            PresetName = "xmas_wagon_2",
                            PrefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadable.entity.prefab",
                            Bradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            Crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["xmas_crate"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        Position = "(0.027, 3.276, -3.562)",
                                        Rotation = "(0.361, 355.541, 16.972)"
                                    },
                                    new LocationConfig
                                    {
                                        Position = "(0.027, 3.276, 3.897)",
                                        Rotation = "(334.884, 355.419, 352.239)"
                                    }
                                }
                            },
                            SamSites = new Dictionary<string, HashSet<LocationConfig>>(),
                            Decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            Npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            Turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                        }
                    },
                    Bradleys = new HashSet<BradleyConfig>
                    {
                        new BradleyConfig
                        {
                            PresetName = "bradley_default",
                            Hp = 900f,
                            ScaleDamage = 0.3f,
                            ViewDistance = 100.0f,
                            SearchDistance = 100.0f,
                            CoaxAimCone = 1.1f,
                            CoaxFireRate = 1.0f,
                            CoaxBurstLength = 10,
                            NextFireTime = 10f,
                            TopTurretFireRate = 0.25f,
                            InstCrateOpen = true,
                        },
                    },
                    TurretConfigs = new HashSet<TurretConfig>
                    {
                        new TurretConfig
                        {
                            PresetName = "turret_ak",
                            Hp = 250f,
                            ShortNameWeapon = "rifle.ak",
                            ShortNameAmmo = "ammo.rifle",
                            CountAmmo = 200
                        },
                        new TurretConfig
                        {
                            PresetName = "turret_m249",
                            Hp = 300f,
                            ShortNameWeapon = "lmg.m249",
                            ShortNameAmmo = "ammo.rifle",
                            CountAmmo = 400
                        }
                    },
                    SamsiteConfigs = new HashSet<SamSiteConfig>
                    {
                        new SamSiteConfig
                        {
                            PresetName = "samsite_default",
                            Hp = 1000,
                            CountAmmo = 100
                        }
                    },
                    CrateConfigs = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            PresetName = "chinooklockedcrate_default",
                            Prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab",
                            Skin = 0,
                            HackTime = 0
                        },
                        new CrateConfig
                        {
                            PresetName = "crateelite_default",
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_elite.prefab",
                            Skin = 0,
                            HackTime = 0
                        },
                        new CrateConfig
                        {
                            PresetName = "crate_normal_default",
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Skin = 0,
                            HackTime = 0
                        },
                        new CrateConfig
                        {
                            PresetName = "crate_normal2_default",
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Skin = 0,
                            HackTime = 0
                        },

                        new CrateConfig
                        {
                            PresetName = "xmas_crate",
                            Prefab = "assets/prefabs/missions/portal/proceduraldungeon/xmastunnels/loot/xmastunnellootbox.prefab",
                            Skin = 0,
                            HackTime = 0
                        }
                    },
                    HeliConfigs = new HashSet<HeliConfig>
                    {
                        new HeliConfig
                        {
                            PresetName = "heli_1",
                            Hp = 10000f,
                            MainRotorHealth = 750f,
                            RearRotorHealth = 375f,
                            Height = 50f,
                            BulletDamage = 20f,
                            BulletSpeed = 250f,
                            Distance = 250f,
                            Speed = 25f,
                            OutsideTime = 30,
                            InstCrateOpen = false,
                            CratesLifeTime = 1800,
                            ImmediatelyKill = true,
                            MinCratesAmount = 3,
                            MaxCratesAmount = 3
                        }
                    },
                    NpcConfigs = new HashSet<NpcConfig>
                    {
                        new NpcConfig
                        {
                            PresetName = "trainnpc",
                            DisplayName = "TrainNpc",
                            Health = 200f,
                            Speed = 5f,
                            RoamRange = 10,
                            ChaseRange = 110,
                            DeleteCorpse = true,
                            WearItems = new List<NpcWear>
                            {
                                new NpcWear
                                {
                                    ShortName = "metal.plate.torso",
                                    SkinID = 1988476232
                                },
                                new NpcWear
                                {
                                    ShortName = "riot.helmet",
                                    SkinID = 1988478091
                                },
                                new NpcWear
                                {
                                    ShortName = "pants",
                                    SkinID = 1582399729
                                },
                                new NpcWear
                                {
                                    ShortName = "tshirt",
                                    SkinID = 1582403431
                                },
                                new NpcWear
                                {
                                    ShortName = "shoes.boots",
                                    SkinID = 0
                                }
                            },
                            BeltItems = new List<NpcBelt>
                            {
                                new NpcBelt
                                {
                                    ShortName = "rifle.lr300",
                                    Amount = 1,
                                    SkinID = 0,
                                    Mods = new HashSet<string> { "weapon.mod.holosight" },
                                    Ammo = ""
                                },
                                new NpcBelt
                                {
                                    ShortName = "syringe.medical",
                                    Amount = 10,
                                    SkinID = 0,
                                    Mods = new HashSet<string>(),
                                    Ammo = ""
                                },
                                new NpcBelt
                                {
                                    ShortName = "grenade.f1",
                                    Amount = 10,
                                    SkinID = 0,
                                    Mods = new HashSet<string>(),
                                    Ammo = ""
                                }
                            },
                            Kit = "",
                            AttackRangeMultiplier = 1f,
                            SenseRange = 60f,
                            MemoryDuration = 60f,
                            DamageScale = 1f,
                            TurretDamageScale = 1f,
                            AimConeScale = 1f,
                            CheckVisionCone = false,
                            VisionCone = 135f,
                            DisableRadio = false,
                            SleepDistance = 100f,
                            IgnoreWoundedPlayers = false,
                        },
                        new NpcConfig
                        {
                            PresetName = "traindriver",
                            DisplayName = "TrainDriver",
                            Health = 200f,
                            Speed = 5f,
                            RoamRange = 10,
                            ChaseRange = 110,
                            DeleteCorpse = true,
                            WearItems = new List<NpcWear>
                            {
                                new NpcWear
                                {
                                    ShortName = "metal.plate.torso",
                                    SkinID = 1988476232
                                },
                                new NpcWear
                                {
                                    ShortName = "riot.helmet",
                                    SkinID = 1988478091
                                },
                                new NpcWear
                                {
                                    ShortName = "pants",
                                    SkinID = 1582399729
                                },
                                new NpcWear
                                {
                                    ShortName = "tshirt",
                                    SkinID = 1582403431
                                },
                                new NpcWear
                                {
                                    ShortName = "shoes.boots",
                                    SkinID = 0
                                }
                            },
                            BeltItems = new List<NpcBelt>
                            {
                                new NpcBelt
                                {
                                    ShortName = "rifle.lr300",
                                    Amount = 1,
                                    SkinID = 0,
                                    Mods = new HashSet<string> { "weapon.mod.holosight" },
                                    Ammo = ""
                                },
                                new NpcBelt
                                {
                                    ShortName = "syringe.medical",
                                    Amount = 10,
                                    SkinID = 0,
                                    Mods = new HashSet<string>(),
                                    Ammo = ""
                                },
                                new NpcBelt
                                {
                                    ShortName = "grenade.f1",
                                    Amount = 10,
                                    SkinID = 0,
                                    Mods = new HashSet<string>(),
                                    Ammo = ""
                                }
                            },
                            Kit = "",
                            AttackRangeMultiplier = 1f,
                            SenseRange = 60f,
                            MemoryDuration = 60f,
                            DamageScale = 1f,
                            TurretDamageScale = 0f,
                            AimConeScale = 1f,
                            CheckVisionCone = false,
                            VisionCone = 135f,
                            DisableRadio = false,
                            SleepDistance = 100f,
                            IgnoreWoundedPlayers = false,
                        }
                    },
                    MarkerConfig = new MarkerConfig
                    {
                        Enable = true,
                        UseRingMarker = true,
                        UseShopMarker = true,
                        Radius = 0.2f,
                        Alpha = 0.6f,
                        Color1 = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                        Color2 = new ColorConfig { R = 0f, G = 0f, B = 0f }
                    },
                    ZoneConfig = new ZoneConfig
                    {
                        IsPvpZone = false,
                        IsDome = false,
                        Darkening = 5,
                        IsColoredBorder = false,
                        Brightness = 5,
                        BorderColor = 2,
                        Radius = 100
                    },
                    GUIConfig = new GUIConfig
                    {
                        IsEnable = true,
                        OffsetMinY = -56
                    },
                    NotifyConfig = new NotifyConfig
                    {
                        PreStartTime = 0,
                        IsChatEnable = true,
                        TimeNotifications = new HashSet<int>
                        {
                            300,
                            60,
                            30,
                            5
                        },
                        GameTipConfig = new GameTipConfig
                        {
                            IsEnabled = false,
                            Style = 2,
                        }
                    },
                    SupportedPluginsConfig = new SupportedPluginsConfig
                    {
                        PveMode = new PveModeConfig
                        {
                            Enable = false,
                            IgnoreAdmin = false,
                            OwnerIsStopper = true,
                            NoInteractIfCooldownAndNoOwners = true,
                            NoDealDamageIfCooldownAndTeamOwner = false,
                            CanLootOnlyOwner = true,
                            ShowEventOwnerNameOnMap = true,
                            Damage = 500f,
                            ScaleDamage = new Dictionary<string, float>
                            {
                                ["Npc"] = 1f,
                                ["Bradley"] = 2f,
                                ["Helicopter"] = 2f,
                                ["Turret"] = 2f,
                            },
                            LootCrate = false,
                            HackCrate = false,
                            LootNpc = false,
                            DamageNpc = false,
                            TargetNpc = false,
                            DamageTank = false,
                            TargetTank = false,
                            DamageTurret = false,
                            TargetTurret = false,
                            CanEnter = false,
                            CanEnterCooldownPlayer = true,
                            TimeExitOwner = 300,
                            AlertTime = 60,
                            RestoreUponDeath = true,
                            Cooldown = 86400,
                            Darkening = 12
                        },
                        EconomicsConfig = new EconomyConfig
                        {
                            Enable = false,
                            Plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic" },
                            MinCommandPoint = 0,
                            MinEconomyPoint = 0,
                            Crates = new Dictionary<string, double>
                            {
                                ["assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab"] = 0.4
                            },
                            NpcPoint = 2,
                            BradleyPoint = 5,
                            HackCratePoint = 5,
                            TurretPoint = 2,
                            HeliPoint = 5,
                            Commands = new HashSet<string>()
                        },
                        GUIAnnouncementsConfig = new GUIAnnouncementsConfig
                        {
                            IsEnabled = false,
                            BannerColor = "Grey",
                            TextColor = "White",
                            APIAdjustVPosition = 0.03f
                        },
                        NotifyPluginConfig = new NotifyPluginConfig
                        {
                            IsEnabled = false,
                            Type = 0
                        },
                        DiscordMessagesConfig = new DiscordConfig
                        {
                            IsEnabled = false,
                            WebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                            EmbedColor = 13516583,
                            Keys = new HashSet<string>
                            {
                                "PreStartTrain",
                                "PlayerStopTrain",
                                "EndEvent"
                            }
                        },
                        BetterNpcConfig = new BetterNpcConfig
                        {
                            IsHeliNpc = false,
                        }
                    }
                };
            }
        }
        #endregion Config
    }
}

namespace Oxide.Plugins.ArmoredTrainExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static HashSet<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            HashSet<TSource> result = new HashSet<TSource>();

            using (var enumerator = source.GetEnumerator())
                while (enumerator.MoveNext())
                    if (predicate(enumerator.Current))
                        result.Add(enumerator.Current);
            return result;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
            return result;
        }

        public static List<TResult> Select<TSource, TResult>(this IList<TSource> source, Func<TSource, TResult> predicate)
        {
            List<TResult> result = new List<TResult>();
            for (int i = 0; i < source.Count; i++)
            {
                TSource element = source[i];
                result.Add(predicate(element));
            }
            return result;
        }

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static bool IsRealPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static List<TSource> OrderBy<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            List<TSource> result = source.ToList();
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = 0; j < result.Count - 1; j++)
                {
                    if (predicate(result[j]) > predicate(result[j + 1]))
                    {
                        TSource z = result[j];
                        result[j] = result[j + 1];
                        result[j + 1] = z;
                    }
                }
            }
            return result;
        }

        public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source)
        {
            List<TSource> result = new List<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static HashSet<T> OfType<T>(this IEnumerable<BaseNetworkable> source)
        {
            HashSet<T> result = new HashSet<T>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (enumerator.Current is T) result.Add((T)(object)enumerator.Current);
            return result;
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = source.ElementAt(0);
            float resultValue = predicate(result);
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = source.ElementAt(0);
            float resultValue = predicate(result);
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue < resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource ElementAt<TSource>(this IEnumerable<TSource> source, int index)
        {
            int movements = 0;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    if (movements == index) return enumerator.Current;
                    movements++;
                }
            }
            return default(TSource);
        }

        public static TSource First<TSource>(this IList<TSource> source) => source[0];

        public static TSource Last<TSource>(this IList<TSource> source) => source[source.Count - 1];

        public static bool IsEqualVector3(this Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.1f;

        public static List<TSource> OrderByQuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate)
        {
            return source.QuickSort(predicate, 0, source.Count - 1);
        }

        private static List<TSource> QuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate, int minIndex, int maxIndex)
        {
            if (minIndex >= maxIndex) return source;

            int pivotIndex = minIndex - 1;
            for (int i = minIndex; i < maxIndex; i++)
            {
                if (predicate(source[i]) < predicate(source[maxIndex]))
                {
                    pivotIndex++;
                    source.Replace(pivotIndex, i);
                }
            }
            pivotIndex++;
            source.Replace(pivotIndex, maxIndex);

            QuickSort(source, predicate, minIndex, pivotIndex - 1);
            QuickSort(source, predicate, pivotIndex + 1, maxIndex);

            return source;
        }

        private static void Replace<TSource>(this IList<TSource> source, int x, int y)
        {
            TSource t = source[x];
            source[x] = source[y];
            source[y] = t;
        }
    }
}