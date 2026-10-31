/*▄▄▄    ███▄ ▄███▓  ▄████  ▄▄▄██▀▀▀▓█████▄▄▄█████▓
▓█████▄ ▓██▒▀█▀ ██▒ ██▒ ▀█▒   ▒██   ▓█   ▀▓  ██▒ ▓▒
▒██▒ ▄██▓██    ▓██░▒██░▄▄▄░   ░██   ▒███  ▒ ▓██░ ▒░
▒██░█▀  ▒██    ▒██ ░▓█  ██▓▓██▄██▓  ▒▓█  ▄░ ▓██▓ ░ 
░▓█  ▀█▓▒██▒   ░██▒░▒▓███▀▒ ▓███▒   ░▒████▒ ▒██▒ ░ 
░▒▓███▀▒░ ▒░   ░  ░ ░▒   ▒  ▒▓▒▒░   ░░ ▒░ ░ ▒ ░░   
▒░▒   ░ ░  ░      ░  ░   ░  ▒ ░▒░    ░ ░  ░   ░    
 ░    ░ ░      ░   ░ ░   ░  ░ ░ ░      ░    ░      
 ░             ░         ░  ░   ░      ░  ░*/
using HarmonyLib;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("extendedindustrial", "bmgjet", "1.2.6")]
    [Description("Adds Industrial Adapter To Other Deployables")]
    class extendedindustrial : RustPlugin
    {
        public static extendedindustrial plugin;
        private List<CuiElement> cuiElementList = new List<CuiElement>();
        private static List<ulong> recyclers = new List<ulong>();

        //Permissions
        private string permPlace = "extendedindustrial.place"; // players can place a adapater on what they are looking at if they have permission for that entity
        private string permSpawn = "extendedindustrial.spawn"; //With this perm players can spawn a adapater on what they are looking if it has a container
        private string permAttach = "extendedindustrial.attach"; //With this perm adapter will be spawn on all entitys the player has perms for
        private string permAll = "extendedindustrial.all"; //With this perm you can use all items
        private string permPlanterBox = "extendedindustrial.planterbox"; //Grants planterbox adapter
        private string permFlameTurret = "extendedindustrial.flameturret"; //Grants flameturret adapter
        private string permGunTrap = "extendedindustrial.guntrap";//Grants guntrap adapter
        private string permSnowMachine = "extendedindustrial.snowmachine";//Grants snowmachine adapter
        private string permFogMachine = "extendedindustrial.fogmachine";//Grants fogmachine adapter
        private string permSamSite = "extendedindustrial.samsite";//Grants samesite adapter
        private string permFuelGenerator = "extendedindustrial.fuelgenerator";//Grants fuelgenerator adapter
        private string permAutoTurret = "extendedindustrial.autoturret";//Grants autoturret adapter
        private string permDropBox = "extendedindustrial.dropbox";//Grants dropbox adapter
        private string permComposter = "extendedindustrial.composter";//Grants composter adapter
        private string permHitchTrough = "extendedindustrial.hitchtrough";//Grants hitchtrough adapter
        private string permRecycler = "extendedindustrial.recycler";//Grants recycler adapter
        private string permMixingTable = "extendedindustrial.mixingtable";//Grants mixingtable adapter
        private string permStash = "extendedindustrial.stash";//Grants stash adapter
        private string permLight = "extendedindustrial.tunalight";//Grants tunalight adapter
        private string permHobo = "extendedindustrial.hobobarrel";//Grants hobobarrel adapter
        private string permFirePit = "extendedindustrial.skull_fire_pit";//Grants skull_fire_pit adapter
        private string permCampfire = "extendedindustrial.campfire";//Grants campfire adapter
        private string permBBQ = "extendedindustrial.bbq";//Grants campfire adapter
        private string permFurnace = "extendedindustrial.furnace";//Grants old furnace adapter
        private string permFireplace = "extendedindustrial.fireplace";//Grants fireplace adapter
        private string permHAB = "extendedindustrial.hab_storage";//Grants hotairbloon storage adapter
        private string permFishTrap = "extendedindustrial.fishtrap";//Grants fishtrap storage adapter
        private string permBeeHive = "extendedindustrial.beehive";//Grants beehive storage adapter
        private string permChickenCoop = "extendedindustrial.chickencoop";//Grants chicken coop storage adapter
        private string permAutoRecycle = "extendedindustrial.autorecycle";//Grants auto recycle
        private string permEjector = "extendedindustrial.ejector";//Grants loot ejector
        //Cui Data
        private string CUIData = "H4sIAAAAAAAEAKWPTwvCMAzFv8rIeRZbO527e9hJEHcaO5RZtbimoyviEL+7qYh/boLk0vzykr5XXwGV1VDAqlxDCr3yGgO167P2nRoJ7fQQvBsr8xK1zvYOSTdAUV8hjH08UKEJ4woPBjWrSlZaddAPcec8jafJo+CWvjY2ug1br3DYO29JqrA90ssgzZjMEybnb6oukWYiYdkMbk0883Refvr+wyFPYjFJyARtzQ6KCZ/KJV+IRSZSGE4GIxRLyXMheZb/mCXG/s5BH8UMzR0Bhv6MfwEAAA==";
        private readonly string adapter = "assets/prefabs/deployable/playerioents/industrialadaptors/storageadaptor.deployed.prefab";
        private readonly string deployed = "assets/prefabs/deployable/playerioents/industrialconveyor/effects/industrial-conveyor-deploy.prefab";

        #region Language
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
            {"Placed", "You Placed A Storage Adapter"},
            {"NoStorage", "No Storages Found!"},
            {"AddedTo", "Added Storage Adapter To {0}" },
            {"NoScoket","No Free Sockets To Add An Adapter." },
            {"NoPerm", "You Don't Have Required Permission." },
            {"BuildingBlocked", "Building Blocked!" },
            {"Converted", "Converted To Loot Ejector!" },
            {"UnConverted", "Convert Back To Normal Box!" },
            {"NotOwned", "You Do Not Own This Box!" },
            {"WrongBox", "You can only convert a small wood box to an ejector!" },
            }, this);
        }

        private string message(BasePlayer player, string key, params object[] args)
        {
            if (player == null) { return string.Format(lang.GetMessage(key, this), args); }
            return string.Format(lang.GetMessage(key, this, player.UserIDString), args);
        }
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            plugin = this;
            Unsubscribe(nameof(OnEntitySpawned));
            //Register Permissions
            permission.RegisterPermission(permPlace, this);
            permission.RegisterPermission(permAttach, this);
            permission.RegisterPermission(permAll, this);
            permission.RegisterPermission(permPlanterBox, this);
            permission.RegisterPermission(permFlameTurret, this);
            permission.RegisterPermission(permGunTrap, this);
            permission.RegisterPermission(permSnowMachine, this);
            permission.RegisterPermission(permFogMachine, this);
            permission.RegisterPermission(permSamSite, this);
            permission.RegisterPermission(permFuelGenerator, this);
            permission.RegisterPermission(permAutoTurret, this);
            permission.RegisterPermission(permDropBox, this);
            permission.RegisterPermission(permComposter, this);
            permission.RegisterPermission(permHitchTrough, this);
            permission.RegisterPermission(permRecycler, this);
            permission.RegisterPermission(permMixingTable, this);
            permission.RegisterPermission(permStash, this);
            permission.RegisterPermission(permLight, this);
            permission.RegisterPermission(permHobo, this);
            permission.RegisterPermission(permFirePit, this);
            permission.RegisterPermission(permCampfire, this);
            permission.RegisterPermission(permBBQ, this);
            permission.RegisterPermission(permFurnace, this);
            permission.RegisterPermission(permFireplace, this);
            permission.RegisterPermission(permSpawn, this);
            permission.RegisterPermission(permHAB, this);
            permission.RegisterPermission(permFishTrap, this);
            permission.RegisterPermission(permBeeHive, this);
            permission.RegisterPermission(permChickenCoop, this);
            permission.RegisterPermission(permAutoRecycle, this);
            permission.RegisterPermission(permEjector, this);
        }

        private void OnServerInitialized()
        {
            Subscribe(nameof(OnEntitySpawned));
            cuiElementList = CuiHelper.FromJson(Encoding.UTF8.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String(CUIData))));
        }

        void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList) { CuiHelper.DestroyUi(player, "EIO"); } //Remove any CUI
            plugin = null;
        }

        //Block damage to spawned in parts
        private object OnEntityTakeDamage(BaseCombatEntity baseent, HitInfo info) { if (baseent is IndustrialStorageAdaptor && baseent.skinID == 12345678901) { return true; } return null; }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity baseent)
        {
            if (baseent is IndustrialStorageAdaptor && baseent.skinID == 12345678901) { return false; }  //Blocks picking up adapters with this set skinid
            foreach (BaseEntity be in baseent.children) { if (be is IndustrialStorageAdaptor) { player.inventory.GiveItem(ItemManager.CreateByItemID(-1049172752)); } break; } //Return any storage Adapters that were attached
            return null;
        }

        //Hooks for the adapter on the stash to be hidden
        private void OnStashHidden(StashContainer stash) { if (stash.skinID == 1111111111) { StashAdapter(stash, true); } }
        private void OnStashExposed(StashContainer stash) { if (stash.skinID == 1111111111) { StashAdapter(stash, false); } }

        private void OnEntitySpawned(BaseCombatEntity baseent)
        {
            //Catch the spawn and add parts on targeted bits if player has the permission
            string pid = baseent.OwnerID.ToString(); //Convert to string once
            if (baseent.OwnerID != 0 && permission.UserHasPermission(pid, permAttach)) { Attach(pid, baseent, true, false); } //Dont target server entitys or players with out attach perm
        }

        private void OnActiveItemChanged(BasePlayer __instance)
        {
            if (__instance?.GetActiveItem()?.info?.itemid == -1049172752 && !__instance.IsBuildingBlocked())
            {
                if (plugin.permission.UserHasPermission(__instance.UserIDString, plugin.permPlace)) //Check for storage adapter and that player has permission
                {
                    AdapterPlacer AP = __instance.GetComponent<AdapterPlacer>(); //Check if player has component
                    if (AP == null) { AP = __instance.gameObject.AddComponent<AdapterPlacer>(); } //Add component if player doesnt have it
                    AP.player = __instance; //Set player variable in component
                }
            }
        }

        #endregion

        #region Chat Commands
        [ChatCommand("addejector")] //Chat Command
        private void CommandSpawnEjector(BasePlayer player, string command, string[] args)
        {
            if (permission.UserHasPermission(player.UserIDString, permEjector)) //check permissions
            {
                try
                {
                    BaseEntity be = FindEntity(player.eyes.HeadRay()); //Find what player is looking at
                    if (be == null) { player.ChatMessage(message(player, "NoStorage")); return; } //If null message
                    if(be.OwnerID != player.userID.Get())
                    {
                        player.ChatMessage(message(player, "NotOwned"));
                        return;
                    }
                    if (be.prefabID == 1560881570)
                    {
                        if (be.skinID == 3550001558)
                        {
                            be.skinID = 0;
                            player.ChatMessage(message(player, "UnConverted"));
                        }
                        else
                        {
                            be.skinID = 3550001558;
                            player.ChatMessage(message(player, "Converted"));
                        }
                        be.SendNetworkUpdateImmediate();
                    }
                    else{player.ChatMessage(message(player, "WrongBox"));}
                }
                catch { }
            }
        }

        [ChatCommand("addadapter")] //Chat Command
        private void CommandSpawnAdapter(BasePlayer player, string command, string[] args)
        {
            if (permission.UserHasPermission(player.UserIDString, permSpawn)) //check permissions
            {
                try
                {
                    BaseEntity be = FindEntity(player.eyes.HeadRay()); //Find what player is looking at
                    if (be == null) { player.ChatMessage(message(player, "NoStorage")); return; } //If null message
                    bool added = false; //Bool to hold if anything was found
                    foreach (BaseEntity c in be.children) //Check each child attached to the base entity
                    {
                        bool spawn = true;
                        if (c is StorageContainer)
                        {
                            foreach (IndustrialStorageAdaptor sa in c.children) //Make sure it doesnt already have an adapter
                            {
                                if (sa != null)
                                {
                                    spawn = false;
                                    break;
                                }
                            }
                            if (spawn) //Only add adapter if there isnt one
                            {
                                player.ChatMessage(message(player, "AddedTo", c.ToString()));
                                added = true;
                                SpawnPart(c, Quaternion.Euler(0, 0, 0), new Vector3(0f, 0f, 0f));
                            }
                        }
                    }
                    if (!added) { player.ChatMessage(message(player, "NoScoket")); } //Message that nothing was added
                }
                catch { player.ChatMessage(message(player, "NoStorage")); }
                return;
            }
            player.ChatMessage(message(player, "NoPerm"));
        }
        #endregion

        #region Methods
        private BaseEntity FindEntity(Ray ray)
        {
            //Ray cast to find BaseEntity
            RaycastHit hit;
            var raycast = Physics.Raycast(ray, out hit, 6, -1);
            BaseEntity entity = raycast ? hit.GetEntity() : null;
            if (entity == null) { return null; }
            return entity;
        }

        private bool Attach(string pid, BaseEntity baseent, bool protect, bool checkonly)
        {
            //Check conditions, spawn adapters and grant pickup or destruction protection where valid
            if (baseent is PlanterBox && HasPermission(pid, permPlanterBox))
            {
                switch (baseent.prefabID)
                {
                    case 467313155: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(1.3f, .28f, .35f), protect); } return true; //planter.small
                    case 1162882237: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(1.3f, .28f, 1.28f), protect); } return true; //planter.large
                    case 47518702: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(90, 180, 0), new Vector3(0f, .66f, -.49f), protect); } return true; //minecart
                    case 115096413: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(1.3f, .28f, 1.28f), protect); } return true; //railroadplanter
                    case 2846319393: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, .66f, -.49f), protect); } return true; //bathtub
                    case 2685133268: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(90, 90, 0), new Vector3(.070f, .170f, 0f), protect); } return true; //Single Pot
                    case 375169930: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 90, 0), new Vector3(.38f, .256f, 0f), protect); } return true; //triangle planter
                    case 3449130218: if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 90, 0), new Vector3(.38f, .256f, 0f), protect); } return true; //rail triangle planter
                }
            }
            else if (baseent is ChickenCoop && HasPermission(pid, permChickenCoop)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(-.65f, 1.3f, -1.2f), protect); } return true; }
            else if (baseent is Beehive && HasPermission(pid, permBeeHive)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 1.38f, 0f), protect); } return true; }
            else if (baseent is StashContainer && HasPermission(pid, permStash)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 0f, 0f), protect); } baseent.skinID = 1111111111; return true; }
            else if (baseent is FlameTurret && HasPermission(pid, permFlameTurret)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 0f, 0f), protect); } return true; }
            else if (baseent is GunTrap && HasPermission(pid, permGunTrap)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 0f, 0f), protect); } return true; }
            else if (baseent is SnowMachine && HasPermission(pid, permSnowMachine)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 90, 0), new Vector3(0f, 0f, -.5f), protect); } return true; }
            else if (baseent is FogMachine && HasPermission(pid, permFogMachine)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 90, 0), new Vector3(0f, 0f, -0.2f), protect); } return true; }
            else if (baseent is SamSite && HasPermission(pid, permSamSite)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(90, 90, 180), new Vector3(-.90f, .18f, 0f), protect); } return true; }
            else if (baseent is FuelGenerator && HasPermission(pid, permFuelGenerator)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 90), new Vector3(0f, .52f, 0f), protect); } return true; }
            else if (baseent is AutoTurret && HasPermission(pid, permAutoTurret)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 180), new Vector3(0f, .20f, 0f), protect); } return true; }
            else if (baseent is DropBox && HasPermission(pid, permDropBox)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(270, 0, 0), new Vector3(0f, -.22f, -.31f), protect); } return true; }
            else if (baseent is Composter && HasPermission(pid, permComposter)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 1.66f, 0f), protect); } return true; }
            else if (baseent is HitchTrough && HasPermission(pid, permHitchTrough)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(270, 90, 180), new Vector3(1.02f, .48f, .11f), protect); } return true; }
            else if (baseent is Recycler && HasPermission(pid, permRecycler)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 180), new Vector3(-0.25f, .42f, 0.1f), protect); } return true; }
            else if (baseent is MixingTable && HasPermission(pid, permMixingTable)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 180), new Vector3(0f, .82f, .1f), protect); } return true; }
            else if (baseent is BaseOven)
            {
                switch (baseent.prefabID)
                {
                    case 2013224025: //Legecy furnace
                    case 2931042549: if (HasPermission(pid, permFurnace)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(270, 180, 0), new Vector3(-0.05f, 0.15f, 0.5f), protect); } return true; } break; //furnace
                    case 2409469892: if (HasPermission(pid, permBBQ)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 90, 180), new Vector3(0f, .62f, -.3f), protect); } return true; } break; //BBQ
                    case 1392608348: if (HasPermission(pid, permLight)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(270, 180, 0), new Vector3(0f, .0f, .1f), protect); } return true; } break; //TunaLight
                    case 1748062128: if (HasPermission(pid, permHobo)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(270, 180, 0), new Vector3(0f, .2f, .3f), protect); } return true; } break; //Hobo
                    case 1906669538: if (HasPermission(pid, permFirePit)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, .0f, .6f), protect); } return true; } break; //skullfirepit
                    case 1348425051: if (HasPermission(pid, permFirePit)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 0f, .6f), protect); } return true; } break; //cursedcauldron
                    case 4160694184: if (HasPermission(pid, permCampfire)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(0, 0, 0), new Vector3(0f, 0f, .5f), protect); } return true; } break; //campfire
                    case 110576239: if (HasPermission(pid, permFireplace)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(270, 180, 0), new Vector3(0f, .28f, .45f), protect); } return true; } break; //fireplace
                }
            }
            else if (baseent is SurvivalFishTrap && HasPermission(pid, permFishTrap))
            {
                if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(90, 270, 0), new Vector3(-0.79f, .2f, .42f), protect); }
                return true;
            }
            else if (baseent is StorageContainer)
            {
                switch (baseent.ShortPrefabName)
                {
                    case "hab_storage": if (HasPermission(pid, permHAB)) { if (!checkonly) { SpawnPart(baseent, Quaternion.Euler(90, 0, 0), new Vector3(0, 0f, 0), protect); } return true; } break;
                }
            }
            return false;
        }

        private bool HasPermission(string userid, string perm) { return permission.UserHasPermission(userid, permAll) || permission.UserHasPermission(userid, perm); } //Check has permission or all permissions

        private void StashAdapter(StashContainer stash, bool hide)
        {
            foreach (BaseEntity baseEntity in stash.children) //Adjust adapter on hidden stash actions
            {
                if (baseEntity is IndustrialStorageAdaptor)
                {
                    IndustrialStorageAdaptor adapter = baseEntity as IndustrialStorageAdaptor;
                    adapter.SetFlagLocal(BaseEntity.Flags.Reserved8, true, true);
                    adapter.transform.localPosition = new Vector3(0f, (stash.IsHidden() ? -0.3f : 0f), 0f);
                    baseEntity.SendNetworkUpdateImmediate();
                    return;
                }
            }
        }

        private BaseEntity CanPlace(BasePlayer player, BaseEntity be, bool protect, bool checkonly)
        {
            if (be == null) { return null; } //No entity passed
            if (be.children.Count == 0 && plugin.Attach(player.UserIDString, be, protect, checkonly)) { return be; } //Attach to passed entity
            foreach (BaseEntity b in be.children) { if (b.children.Count == 0 && plugin.Attach(player.UserIDString, b, protect, checkonly)) { return b; } } //Attach to a child of passed entity
            return null; //Nothing valid found
        }

        private BaseEntity SpawnPart(BaseEntity parent, Quaternion rotoffset, Vector3 posoffset, bool protect = true)
        {
            //Spawns in parts of the quarry
            BaseEntity baseent = GameManager.server.CreateEntity(adapter, parent.transform.position, parent.transform.rotation);
            baseent.OwnerID = parent.OwnerID;
            if (protect) { baseent.skinID = 12345678901; }//Set a skinid we use to check for modded adapters.
            baseent.Spawn();
            //Remove componants to stop it breaking
            foreach (var mesh in baseent.GetComponentsInChildren<MeshCollider>()) { UnityEngine.Object.DestroyImmediate(mesh); }
            UnityEngine.Object.DestroyImmediate(baseent.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(baseent.GetComponent<GroundWatch>());
            //Parent it to base entity
            baseent.SetParent(parent, true, true);
            //Adjust offset positions
            if (rotoffset != Quaternion.Euler(0, 0, 0)) { baseent.transform.rotation = baseent.transform.rotation * rotoffset; }
            if (posoffset != new Vector3(0, 0, 0)) { baseent.transform.localPosition = posoffset; }
            baseent.SendNetworkUpdateImmediate();
            return baseent;
        }

        private void CreateTip(string msg, BasePlayer player, int time = 10)
        {
            if (player == null) { return; }
            player.SendConsoleCommand("gametip.hidegametip"); //Remove old tip that might still be shown
            player.SendConsoleCommand("gametip.showgametip", msg); //Create new tip
            timer.Once(time, () => player?.SendConsoleCommand("gametip.hidegametip")); //Remove tip after delay
        }

        private BaseEntity FindBaseEntity(Ray ray)
        {
            //Ray cast to find BaseEntity
            RaycastHit hit;
            var raycast = Physics.Raycast(ray, out hit, 3, -1);
            BaseEntity entity = raycast ? hit.GetEntity() : null;
            if (entity != null) { return entity; }
            return null;
        }

        private Vector2i SlotsMod(BaseEntity entity, Vector2i defaultvalue, bool input)
        {
            //Lets adapter pull from other slots
            if (input)
            {
                switch (entity.prefabID)
                {
                    case 4160694184: case 1906669538: case 1348425051: case 1748062128: case 110576239: return new Vector2i(0, 1);//CampFire SkullFirepit Cauldron Hobo FirePlace
                    case 2409469892: return new Vector2i(0, 3); //BBQ
                    case 1729604075: return new Vector2i(0, 5); //Recycler
                }
            }
            else
            {
                switch (entity.prefabID)
                {
                    case 4160694184: case 1906669538: case 1348425051: case 1748062128: case 110576239: return new Vector2i(2, 3);//CampFire SkullFirepit Cauldron Hobo FirePlace
                    case 2409469892: return new Vector2i(4, 7); //BBQ
                    case 1729604075: return new Vector2i(6, 11); //Recycler
                }
            }
            return defaultvalue;
        }
        #endregion
        [AutoPatch]
        [HarmonyPatch(typeof(IndustrialStorageAdaptor), "OnStorageItemTransferEnd")]
        public static class IndustrialStorageOnStorageItemTransferEnd
        {
            [HarmonyPostfix]
            public static void Postfix(IndustrialStorageAdaptor __instance)
            {
                if (__instance.cachedParent != null && __instance.cachedParent.skinID == 3550001558)
                {
                    var storage = __instance.cachedParent as StorageContainer;
                    if(storage != null)
                    {
                       var itembox = storage.inventory.Drop("assets/prefabs/misc/item drop/item_drop.prefab", storage.GetDropPosition(), storage.Transform.rotation, 0);
                        itembox.SetFlagLocal(BaseEntity.Flags.Reserved1, true);
                        itembox.SetFlagLocal(BaseEntity.Flags.Reserved3, true);
                    }
                }
             }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(IndustrialStorageAdaptor), "get_Container")]
        public static class IndustrialStorageAdaptorPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref ItemContainer __result, IndustrialStorageAdaptor __instance)
            {
                try
                {
                    if (__instance.cachedContainer == null)
                    {
                        if (__instance.cachedParent != null)
                        {
                            StorageContainer storageContainer = __instance.cachedParent as StorageContainer;
                            if (storageContainer != null)
                            {
                                __result = storageContainer.inventory;
                                __instance.cachedContainer = __result;
                                return false;
                            }
                            ContainerIOEntity storageIOContainer = __instance.cachedParent as ContainerIOEntity;
                            if (storageIOContainer != null)
                            {
                                __result = storageIOContainer.inventory;
                                __instance.cachedContainer = __result;
                                return false;
                            }
                            ItemContainer itemContainer = __instance.cachedParent.GetComponent<ItemContainer>();
                            if (itemContainer != null)
                            {
                                __result = itemContainer;
                                __instance.cachedContainer = __result;
                                return false;
                            }
                        }
                    }
                    __result = __instance.cachedContainer;
                    return false;
                }
                catch { }
                return true;
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(IndustrialStorageAdaptor), "InputSlotRange", typeof(int))]
        public static class IndustrialStorageAdaptor_InputSlotRange
        {
            [HarmonyPostfix]
            public static void Postfix(ref Vector2i __result, IndustrialStorageAdaptor __instance)
            {
                try
                {
                    if (__instance.cachedParent != null)
                    {
                        __result = plugin.SlotsMod(__instance.cachedParent, __result, true);
                        if (__instance.cachedParent is Recycler recycler && recycler != null && !recycler.IsOn())
                        {
                            if (plugin.permission.UserHasPermission(recycler.OwnerID.ToString(), plugin.permAutoRecycle) && recycler.HasRecyclable())
                            {
                                recycler.StartRecycling();
                            }
                        }
                    }
                }
                catch { }
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(IndustrialStorageAdaptor), "OutputSlotRange", typeof(int))]
        public static class IndustrialStorageAdaptor_OutputSlotRange
        {
            [HarmonyPostfix]
            public static void Postfix(ref Vector2i __result, IndustrialStorageAdaptor __instance)
            {
                try
                {
                    if (__instance.cachedParent != null)
                    {
                        __result = plugin.SlotsMod(__instance.cachedParent, __result, false);
                    }
                }
                catch { }
            }
        }

        #region Classes
        public class AdapterPlacer : MonoBehaviour
        {
            public BasePlayer player;
            private float UIDelay = 0; //Variable used to slow down method in fixedupdate
            private bool CUIed = false; //Variable to stop excessive network data from cui updates.
            public void OnDestroy() { if (player != null) { CuiHelper.DestroyUi(player, "EIO"); } } //Remove CUI
            public void FixedUpdate()
            {
                try //Try and catch incase something in the future changes to prevent null references
                {
                    if (player == null || plugin == null || player?.GetActiveItem().info.itemid != -1049172752) { Destroy(this); }  //Destroy component when conditions are no longer meet
                    else if (player.serverInput.WasJustReleased(BUTTON.FIRE_PRIMARY)) //Catch mouse fire button
                    {
                        if (player.IsBuildingBlocked() && !player.IsAdmin) { plugin.CreateTip(plugin.message(player, "BuildingBlocked"), player); return; } //Check player isnt building blocked
                        if (plugin.CanPlace(player, plugin.FindBaseEntity(player.eyes.HeadRay()), false, false) != null)//Find ent player is looking at
                        {
                            Effect.server.Run(plugin.deployed, player.PivotPoint(), player.transform.up, null, true);
                            player.inventory.Take(null, -1049172752, 1); //Remove adapter from player inventory if it attached
                            player.ChatMessage(plugin.message(player, "Placed"));
                            if (player.inventory.GetAmount(-1049172752) == 0) { Destroy(this); return; } //Checks player has any adapters left
                        }
                    }
                    if (Time.time >= UIDelay) //Delay UI Updates
                    {
                        UIDelay = Time.time + 1f; //Only check for updates every sec (fixedupdate runs 50hz)
                        if (plugin.CanPlace(player, plugin.FindBaseEntity(player.eyes.HeadRay()), false, true) != null && plugin.cuiElementList.Count > 0) { if (!CUIed) { CuiHelper.AddUi(player, plugin.cuiElementList); CUIed = true; } }//Send CUI if they dont have it already
                        else { if (CUIed) { CuiHelper.DestroyUi(player, "EIO"); CUIed = false; } } //Remove CUI
                    }
                }
                catch { Destroy(this); } //Something went wrong so remove CUI
            }
        }
        #endregion
    }
}