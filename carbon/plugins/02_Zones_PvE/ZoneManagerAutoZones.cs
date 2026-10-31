using Newtonsoft.Json;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Zone Manager Auto Zones", "FastBurst", "1.4.5")]
    [Description("Adds zones and domes to monuments automatically.")]

    public class ZoneManagerAutoZones : RustPlugin
    {
        [PluginReference] Plugin ZoneManager, ZoneDomes, TruePVE, NextGenPVE;
        #region Vars
        BasePlayer player;
        #endregion

        #region Oxide Hooks
        void OnServerInitialized()
        {
            PopulateZoneLocations();
        }
        #endregion

        #region Functions
        private void PopulateZoneLocations()
        {
            ConfigData.LocationOptions.Monument config = configData.Location.Monuments;

            MonumentInfo[] monuments = UnityEngine.Object.FindObjectsOfType<MonumentInfo>();
            int miningoutpost = 0, lighthouse = 0, gasstation = 0, supermarket = 0, compound = 0;
            for (int i = 0; i < monuments.Length; i++)
            {
                MonumentInfo monument = monuments[i];
                if (monument.name.Contains("harbor_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = "Large " + monument.displayPhrase.english;
                    string ID = "harbor_1";

                    if (config.HarborLarge.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.HarborLarge.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.HarborLarge.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.HarborLarge.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.HarborLarge.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.HarborLarge.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.HarborLarge.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.HarborLarge.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("harbor_2", CompareOptions.IgnoreCase))
                {
                    string friendlyname = "Small " + monument.displayPhrase.english;
                    string ID = "harbor_2";

                    if (config.HarborSmall.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.HarborSmall.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.HarborSmall.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.HarborSmall.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.HarborSmall.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.HarborSmall.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.HarborSmall.TruePVERules);

                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.HarborSmall.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("airfield_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "airfield_1";

                    if (config.Airfield.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Airfield.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Airfield.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Airfield.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Airfield.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Airfield.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Airfield.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Airfield.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("launch_site_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "launch_site";

                    if (config.LaunchSite.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.LaunchSite.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.LaunchSite.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.LaunchSite.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.LaunchSite.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.LaunchSite.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LaunchSite.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LaunchSite.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("oilrig_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "oilrig_1";

                    if (config.LargeOilRig.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.LargeOilRig.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.LargeOilRig.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.LargeOilRig.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.LargeOilRig.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.LargeOilRig.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LargeOilRig.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LargeOilRig.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("oilrig_2", CompareOptions.IgnoreCase))
                {
                    string friendlyname = "Small " + monument.displayPhrase.english;
                    string ID = "oilrig_2";

                    if (config.SmallOilRig.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.SmallOilRig.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.SmallOilRig.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.SmallOilRig.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.SmallOilRig.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.SmallOilRig.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SmallOilRig.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SmallOilRig.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("powerplant_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "powerplant_1";

                    if (config.Powerplant.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Powerplant.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Powerplant.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Powerplant.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Powerplant.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Powerplant.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Powerplant.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Powerplant.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("military_tunnel_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "military_tunnel_1";

                    if (config.MilitaryTunnels.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.MilitaryTunnels.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.MilitaryTunnels.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.MilitaryTunnels.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.MilitaryTunnels.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.MilitaryTunnels.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MilitaryTunnels.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MilitaryTunnels.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("junkyard_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "junkyard_1";

                    if (config.Junkyard.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Junkyard.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Junkyard.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Junkyard.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Junkyard.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Junkyard.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Junkyard.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Junkyard.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("water_treatment_plant_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "water_treatment_plant_1";

                    if (config.WaterTreatment.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.WaterTreatment.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.WaterTreatment.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.WaterTreatment.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.WaterTreatment.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.WaterTreatment.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.WaterTreatment.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.WaterTreatment.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("trainyard_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "trainyard_1";

                    if (config.TrainYard.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.TrainYard.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.TrainYard.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.TrainYard.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.TrainYard.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.TrainYard.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.TrainYard.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.TrainYard.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("excavator", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "excavator";

                    if (config.Excavator.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Excavator.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Excavator.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Excavator.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Excavator.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Excavator.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Excavator.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Excavator.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);

                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("satellite_dish", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "satellite_dish";

                    if (config.SatelliteDish.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.SatelliteDish.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.SatelliteDish.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.SatelliteDish.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.SatelliteDish.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.SatelliteDish.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SatelliteDish.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SatelliteDish.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("radtown_small_3", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "radtown_small_3";

                    if (config.SewerBranch.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.SewerBranch.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.SewerBranch.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.SewerBranch.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.SewerBranch.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.SewerBranch.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SewerBranch.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SewerBranch.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("sphere_tank", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "sphere_tank";

                    if (config.Dome.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Dome.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Dome.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Dome.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Dome.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Dome.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Dome.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Dome.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("gas_station_1", CompareOptions.IgnoreCase) && gasstation == 0)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "gas_station_1";

                    if (config.GasStation1.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.GasStation1.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.GasStation1.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.GasStation1.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.GasStation1.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.GasStation1.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.GasStation1.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.GasStation1.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    gasstation++;
                    continue;
                }

                if (monument.name.Contains("gas_station_1", CompareOptions.IgnoreCase) && gasstation == 1)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "gas_station_2";

                    if (config.GasStation2.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #2";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.GasStation2.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.GasStation2.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.GasStation2.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.GasStation2.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.GasStation2.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.GasStation2.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.GasStation2.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    gasstation++;
                    continue;
                }

                if (monument.name.Contains("gas_station_1", CompareOptions.IgnoreCase) && gasstation == 2)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "gas_station_3";

                    if (config.GasStation3.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #3";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.GasStation3.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.GasStation3.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.GasStation3.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.GasStation3.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.GasStation3.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.GasStation3.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.GasStation3.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    gasstation++;
                    continue;
                }

                if (monument.name.Contains("mining_quarry_a", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "mining_quarry_a";

                    if (config.QuarryA.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.QuarryA.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.QuarryA.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.QuarryA.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.QuarryA.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.QuarryA.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.QuarryA.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.QuarryA.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("mining_quarry_b", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "mining_quarry_b";

                    if (config.QuarryB.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.QuarryB.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.QuarryB.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.QuarryB.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.QuarryB.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.QuarryB.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.QuarryB.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.QuarryB.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("mining_quarry_c", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "mining_quarry_c";

                    if (config.QuarryC.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.QuarryC.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.QuarryC.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.QuarryC.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.QuarryC.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.QuarryC.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.QuarryC.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.QuarryC.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("swamp_a", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "swamp_a";

                    if (config.SwampA.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.SwampA.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.SwampA.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.SwampA.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.SwampA.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.SwampA.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SwampA.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SwampA.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("swamp_b", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "swamp_b";

                    if (config.SwampB.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #2";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.SwampB.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.SwampB.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.SwampB.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.SwampB.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.SwampB.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SwampB.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SwampB.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("swamp_c", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "swamp_c";

                    if (config.SwampC.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.SwampC.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.SwampC.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.SwampC.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.SwampC.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.SwampC.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SwampC.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.SwampC.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("lighthouse", CompareOptions.IgnoreCase) && lighthouse == 0)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "lighthouse_1";

                    if (config.LightHouse1.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.LightHouse1.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.LightHouse1.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.LightHouse1.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.LightHouse1.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.LightHouse1.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LightHouse1.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LightHouse1.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    lighthouse++;
                    continue;
                }

                if (monument.name.Contains("lighthouse", CompareOptions.IgnoreCase) && lighthouse == 1)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "lighthouse_2";

                    if (config.LightHouse2.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #2";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.LightHouse2.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.LightHouse2.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.LightHouse2.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.LightHouse2.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.LightHouse2.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LightHouse2.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.LightHouse2.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    lighthouse++;
                    continue;
                }

                if (monument.name.Contains("supermarket_1", CompareOptions.IgnoreCase) && supermarket == 0)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "supermarket_1";

                    if (config.Supermarket1.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Supermarket1.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Supermarket1.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Supermarket1.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Supermarket1.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Supermarket1.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Supermarket1.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Supermarket1.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    supermarket++;
                    continue;
                }

                if (monument.name.Contains("supermarket_1", CompareOptions.IgnoreCase) && supermarket == 1)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "supermarket_2";

                    if (config.Supermarket2.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #2";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Supermarket2.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Supermarket2.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Supermarket2.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Supermarket2.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Supermarket2.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Supermarket2.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Supermarket2.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    supermarket++;
                    continue;
                }

                if (monument.name.Contains("supermarket_1", CompareOptions.IgnoreCase) && supermarket == 2)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "supermarket_3";

                    if (config.Supermarket3.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #3";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Supermarket3.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Supermarket3.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Supermarket3.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Supermarket3.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Supermarket3.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Supermarket3.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Supermarket3.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    supermarket++;
                    continue;
                }

                if (monument.name.Contains("warehouse", CompareOptions.IgnoreCase) && miningoutpost == 0)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "miningoutpost_1";

                    if (config.MiningOutpost1.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.MiningOutpost1.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.MiningOutpost1.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.MiningOutpost1.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.MiningOutpost1.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.MiningOutpost1.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MiningOutpost1.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MiningOutpost1.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    miningoutpost++;
                    continue;
                }

                if (monument.name.Contains("warehouse", CompareOptions.IgnoreCase) && miningoutpost == 1)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "miningoutpost_2";

                    if (config.MiningOutpost2.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #2";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.MiningOutpost2.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.MiningOutpost2.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.MiningOutpost2.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.MiningOutpost2.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.MiningOutpost2.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MiningOutpost2.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MiningOutpost2.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    miningoutpost++;
                    continue;
                }

                if (monument.name.Contains("warehouse", CompareOptions.IgnoreCase) && miningoutpost == 2)
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "miningoutpost_3";

                    if (config.MiningOutpost3.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #3";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.MiningOutpost3.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.MiningOutpost3.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.MiningOutpost3.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.MiningOutpost3.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.MiningOutpost3.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MiningOutpost3.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.MiningOutpost3.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    miningoutpost++;
                    continue;
                }

                if (monument.name.Contains("fishing_village_a", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "fishing_village_a";

                    if (config.FishingVillage1.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.FishingVillage1.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.FishingVillage1.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.FishingVillage1.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.FishingVillage1.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.FishingVillage1.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.FishingVillage1.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.FishingVillage1.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("fishing_village_b", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "fishing_village_b";

                    if (config.FishingVillage2.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #2";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.FishingVillage2.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.FishingVillage2.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.FishingVillage2.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.FishingVillage2.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.FishingVillage2.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.FishingVillage2.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.FishingVillage2.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("fishing_village_c", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "fishing_village_c";

                    if (config.FishingVillage3.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #3";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.FishingVillage3.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.FishingVillage3.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.FishingVillage3.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.FishingVillage3.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.FishingVillage3.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.FishingVillage3.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.FishingVillage3.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("compound"))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "compound_1";

                    if (config.Outpost.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.Outpost.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.Outpost.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.Outpost.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.Outpost.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.Outpost.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Outpost.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.Outpost.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("bandit_town"))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "bandit_town_1";

                    if (config.BanditCamp.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.BanditCamp.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.BanditCamp.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.BanditCamp.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.BanditCamp.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.BanditCamp.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.BanditCamp.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.BanditCamp.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("desert_military_base_a", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "desert_military_base_a";

                    if (config.DesertBaseA.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.DesertBaseA.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.DesertBaseA.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.DesertBaseA.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.DesertBaseA.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.DesertBaseA.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseA.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseA.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("desert_military_base_b", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "desert_military_base_b";

                    if (config.DesertBaseB.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.DesertBaseB.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.DesertBaseB.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.DesertBaseB.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.DesertBaseB.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.DesertBaseB.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseB.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseB.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("desert_military_base_c", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "desert_military_base_c";

                    if (config.DesertBaseC.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.DesertBaseC.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.DesertBaseC.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.DesertBaseC.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.DesertBaseC.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.DesertBaseC.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseC.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseC.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("desert_military_base_d", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "desert_military_base_d";

                    if (config.DesertBaseD.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname + " #1";
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.DesertBaseD.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.DesertBaseD.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.DesertBaseD.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.DesertBaseD.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.DesertBaseD.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseD.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.DesertBaseD.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("stables_a", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "stables_a";

                    if (config.StableA.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.StableA.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.StableA.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.StableA.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.StableA.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.StableA.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.StableA.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.StableA.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("stables_b", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "stables_b";

                    if (config.StableB.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.StableB.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.StableB.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.StableB.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.StableB.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.StableB.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.StableB.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.StableB.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("arctic_research_base_a", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "arctic_research_base_a";

                    if (config.ArticA.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.ArticA.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.ArticA.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.ArticA.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.ArticA.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.ArticA.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.ArticA.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.ArticA.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("nuclear_missile_Silo", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "nuclear_missile_Silo";

                    if (config.missileSilo.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.missileSilo.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.missileSilo.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.missileSilo.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.missileSilo.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.missileSilo.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.missileSilo.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.missileSilo.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("ferry_terminal_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "ferry_terminal_1";

                    if (config.ferryTerminal.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.ferryTerminal.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.ferryTerminal.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.ferryTerminal.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.ferryTerminal.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.ferryTerminal.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.ferryTerminal.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.ferryTerminal.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("radtown_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "radtown_1";

                    if (config.radTown.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.radTown.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.radTown.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.radTown.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.radTown.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.radTown.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.radTown.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.radTown.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("apartments_complex_1", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "apartments_complex_1";

                    if (config.ApartmentComplex.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.ApartmentComplex.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.ApartmentComplex.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.ApartmentComplex.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.ApartmentComplex.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.ApartmentComplex.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.ApartmentComplex.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.ApartmentComplex.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }

                if (monument.name.Contains("jungle_ziggurat", CompareOptions.IgnoreCase))
                {
                    string friendlyname = monument.displayPhrase.english;
                    string ID = "jungle_ziggurat";

                    if (config.JungleZiggurat.Enabled)
                    {
                        string[] messages = new string[8];
                        messages[0] = "name";
                        messages[1] = friendlyname;
                        messages[2] = "enter_message";
                        messages[3] = configData.Location.Monuments.JungleZiggurat.EnterMessage;
                        messages[4] = "leave_message";
                        messages[5] = configData.Location.Monuments.JungleZiggurat.LeaveMessage;
                        messages[6] = "radius";
                        messages[7] = Convert.ToInt32(configData.Location.Monuments.JungleZiggurat.Radius).ToString();

                        ZoneManager?.CallHook("CreateOrUpdateZone", ID, messages, monument.transform.position);

                        if (configData.Location.Monuments.JungleZiggurat.ZoneFlags != null)
                            foreach (var flag in configData.Location.Monuments.JungleZiggurat.ZoneFlags)
                                ZoneManager?.CallHook("AddFlag", ID, flag);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.JungleZiggurat.TruePVERules);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("AddOrUpdateMapping", ID, configData.Location.Monuments.JungleZiggurat.NextGenPVERules);

                        if (configData.ZoneOption.UseZoneDomes)
                        {
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                            ZoneDomes?.CallHook("AddNewDome", player, ID);
                        }
                    }
                    else
                    {
                        ZoneManager?.CallHook("EraseZone", ID);

                        if (configData.ZoneOption.UseTruePVE)
                            TruePVE?.CallHook("RemoveMapping", ID);
                        else if (configData.ZoneOption.UseNextGenPVE)
                            NextGenPVE?.CallHook("RemoveMapping", ID);

                        if (configData.ZoneOption.UseZoneDomes)
                            ZoneDomes?.CallHook("RemoveExistingDome", player, ID);
                    }
                    continue;
                }
            }

            if (configData.ZoneOption.UseZoneDomes)
            {
                ZoneDomes?.Call("DestroyAllSpheres", player);
                ZoneDomes?.Call("OnServerInitialized", player);
            }
        }
        #endregion

        #region Config        
        private ConfigData configData;
        class ConfigData
        {
            [JsonProperty(PropertyName = "ZonesDome Option")]
            public Options ZoneOption { get; set; }
            public class Options
            {
                [JsonProperty(PropertyName = "Use Zone Domes Spheres over Zones")]
                public bool UseZoneDomes { get; set; }
                [JsonProperty(PropertyName = "Enable TruePVE to allow Rule Sets")]
                public bool UseTruePVE { get; set; }
                [JsonProperty(PropertyName = "Enable NextGenPVE to allow Rule Sets")]
                public bool UseNextGenPVE { get; set; }
            }

            [JsonProperty(PropertyName = "Zone Location Options")]
            public LocationOptions Location { get; set; }

            public class LocationOptions
            {
                [JsonProperty(PropertyName = "Monument Options")]
                public Monument Monuments { get; set; }

                public class Monument
                {
                    [JsonProperty(PropertyName = "Airfield")]
                    public Options Airfield { get; set; }
                    [JsonProperty(PropertyName = "Small Harbour")]
                    public Options HarborSmall { get; set; }
                    [JsonProperty(PropertyName = "Large Harbour")]
                    public Options HarborLarge { get; set; }
                    [JsonProperty(PropertyName = "Launch Site")]
                    public Options LaunchSite { get; set; }
                    [JsonProperty(PropertyName = "Large Oil Rig")]
                    public Options LargeOilRig { get; set; }
                    [JsonProperty(PropertyName = "Small Oil Rig")]
                    public Options SmallOilRig { get; set; }
                    [JsonProperty(PropertyName = "Power Plant")]
                    public Options Powerplant { get; set; }
                    [JsonProperty(PropertyName = "Junk Yard")]
                    public Options Junkyard { get; set; }
                    [JsonProperty(PropertyName = "Military Tunnels")]
                    public Options MilitaryTunnels { get; set; }
                    [JsonProperty(PropertyName = "Train Yard")]
                    public Options TrainYard { get; set; }
                    [JsonProperty(PropertyName = "Water Treatment Plant")]
                    public Options WaterTreatment { get; set; }
                    [JsonProperty(PropertyName = "Giant Excavator Pit")]
                    public Options Excavator { get; set; }
                    [JsonProperty(PropertyName = "Satellite Dish")]
                    public Options SatelliteDish { get; set; }
                    [JsonProperty(PropertyName = "Sewer Branch")]
                    public Options SewerBranch { get; set; }
                    [JsonProperty(PropertyName = "The Dome")]
                    public Options Dome { get; set; }
                    [JsonProperty(PropertyName = "Oxum's Gas Station #1")]
                    public Options GasStation1 { get; set; }
                    [JsonProperty(PropertyName = "Oxum's Gas Station #2")]
                    public Options GasStation2 { get; set; }
                    [JsonProperty(PropertyName = "Oxum's Gas Station #3")]
                    public Options GasStation3 { get; set; }
                    [JsonProperty(PropertyName = "Sulfur Quarry")]
                    public Options QuarryA { get; set; }
                    [JsonProperty(PropertyName = "Stone Quarry")]
                    public Options QuarryB { get; set; }
                    [JsonProperty(PropertyName = "HQM Quarry")]
                    public Options QuarryC { get; set; }
                    [JsonProperty(PropertyName = "Wild Swamp 1")]
                    public Options SwampA { get; set; }
                    [JsonProperty(PropertyName = "Wild Swamp 2")]
                    public Options SwampB { get; set; }
                    [JsonProperty(PropertyName = "Abandoned Cabins")]
                    public Options SwampC { get; set; }
                    [JsonProperty(PropertyName = "Light House #1")]
                    public Options LightHouse1 { get; set; }
                    [JsonProperty(PropertyName = "Light House #2")]
                    public Options LightHouse2 { get; set; }
                    [JsonProperty(PropertyName = "Abandoned Supermarket #1")]
                    public Options Supermarket1 { get; set; }
                    [JsonProperty(PropertyName = "Abandoned Supermarket #2")]
                    public Options Supermarket2 { get; set; }
                    [JsonProperty(PropertyName = "Abandoned Supermarket #3")]
                    public Options Supermarket3 { get; set; }
                    [JsonProperty(PropertyName = "Mining Outpost #1")]
                    public Options MiningOutpost1 { get; set; }
                    [JsonProperty(PropertyName = "Mining Outpost #2")]
                    public Options MiningOutpost2 { get; set; }
                    [JsonProperty(PropertyName = "Mining Outpost #3")]
                    public Options MiningOutpost3 { get; set; }
                    [JsonProperty(PropertyName = "Fishing Village #1")]
                    public Options FishingVillage1 { get; set; }
                    [JsonProperty(PropertyName = "Fishing Village #2")]
                    public Options FishingVillage2 { get; set; }
                    [JsonProperty(PropertyName = "Fishing Village #3")]
                    public Options FishingVillage3 { get; set; }
                    [JsonProperty(PropertyName = "Outpost")]
                    public Options Outpost { get; set; }
                    [JsonProperty(PropertyName = "Bandit Camp")]
                    public Options BanditCamp { get; set; }
                    [JsonProperty(PropertyName = "Desert Base 1")]
                    public Options DesertBaseA { get; set; }
                    [JsonProperty(PropertyName = "Desert Base 2")]
                    public Options DesertBaseB { get; set; }
                    [JsonProperty(PropertyName = "Desert Base 3")]
                    public Options DesertBaseC { get; set; }
                    [JsonProperty(PropertyName = "Desert Base 4")]
                    public Options DesertBaseD { get; set; }
                    [JsonProperty(PropertyName = "Stables")]
                    public Options StableA { get; set; }
                    [JsonProperty(PropertyName = "Ranch")]
                    public Options StableB { get; set; }
                    [JsonProperty(PropertyName = "Artic Research Base")]
                    public Options ArticA { get; set; }
                    [JsonProperty(PropertyName = "Nuclear Missile Silo")]
                    public Options missileSilo { get; set; }
                    [JsonProperty(PropertyName = "Ferry Terminal")]
                    public Options ferryTerminal { get; set; }
                    [JsonProperty(PropertyName = "Rad Town")]
                    public Options radTown { get; set; }
                    [JsonProperty(PropertyName = "Jungle Ziggurat")]
                    public Options JungleZiggurat { get; set; }
                    [JsonProperty(PropertyName = "Apartment Complex")]
                    public Options ApartmentComplex { get; set; }
                    public class Options
                    {
                        public bool Enabled { get; set; }
                        [JsonProperty(PropertyName = "Radius")]
                        public int Radius { get; set; }

                        [JsonProperty(PropertyName = "Enter Zone Message")]
                        public string EnterMessage { get; set; }

                        [JsonProperty(PropertyName = "Leave Zone Message")]
                        public string LeaveMessage { get; set; }

                        [JsonProperty(PropertyName = "Zone Flags")]
                        public string[] ZoneFlags { get; set; }
                        [JsonProperty(PropertyName = "TruePVE RuleSet to use if TruePVE is enabled")]
                        public string TruePVERules { get; set; }
                        [JsonProperty(PropertyName = "NextGenPVE RuleSet to use if NextGenPVE is enabled")]
                        public string NextGenPVERules { get; set; }
                        [JsonIgnore]
                        public List<MonumentInfo> Monument { get; set; } = new List<MonumentInfo>();
                    }
                }
            }

            public Oxide.Core.VersionNumber Version { get; set; }
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                ZoneOption = new ConfigData.Options
                {
                    UseZoneDomes = false,
                    UseTruePVE = false,
                    UseNextGenPVE = false
                },
                Location = new ConfigData.LocationOptions
                {
                    Monuments = new ConfigData.LocationOptions.Monument
                    {
                        Airfield = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 200,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        HarborLarge = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 150,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        HarborSmall = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 145,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        LaunchSite = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 295,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        SmallOilRig = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        LargeOilRig = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 165,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Powerplant = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 150,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Junkyard = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 165,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        MilitaryTunnels = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 115,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        TrainYard = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 165,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        WaterTreatment = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 175,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Excavator = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 205,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        SatelliteDish = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        SewerBranch = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 80,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Dome = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 80,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        GasStation1 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        GasStation2 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        GasStation3 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        QuarryA = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        QuarryB = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        QuarryC = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        SwampA = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 95,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        SwampB = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 80,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        SwampC = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 80,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        LightHouse1 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 80,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        LightHouse2 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 80,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Supermarket1 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Supermarket2 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Supermarket3 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        MiningOutpost1 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        MiningOutpost2 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        MiningOutpost3 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        FishingVillage1 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        FishingVillage2 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        FishingVillage3 = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        Outpost = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 135,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVP Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        BanditCamp = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVP Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        DesertBaseA = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        DesertBaseB = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        DesertBaseC = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        DesertBaseD = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        StableA = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVP Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        StableB = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVP Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        ArticA = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        missileSilo = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        ferryTerminal = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 125,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        radTown = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = true,
                            Radius = 85,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        ApartmentComplex = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 100,
                            EnterMessage = "WARNING: You are now entering a Safe Zone",
                            LeaveMessage = "Returning to PVP Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        },
                        JungleZiggurat = new ConfigData.LocationOptions.Monument.Options
                        {
                            Enabled = false,
                            Radius = 50,
                            EnterMessage = "WARNING: You are now entering a PVP Zone",
                            LeaveMessage = "Returning to PVE Area",
                            ZoneFlags = new string[] { "autolights" },
                            TruePVERules = "exclude",
                            NextGenPVERules = "exclude"
                        }
                    }
                },
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();
            if (configData.Version < new Core.VersionNumber(1, 0, 0))
                configData.ZoneOption.UseZoneDomes = baseConfig.ZoneOption.UseZoneDomes;

            if (configData.Version < new Core.VersionNumber(1, 2, 0))
            {
                configData.Location.Monuments = baseConfig.Location.Monuments;
            }

            if (configData.Version < new Core.VersionNumber(1, 2, 1))
            {
                configData.Location.Monuments.GasStation1 = baseConfig.Location.Monuments.GasStation1;
                configData.Location.Monuments.GasStation2 = baseConfig.Location.Monuments.GasStation2;
            }

            if (configData.Version < new Core.VersionNumber(1, 2, 2))
            {
                configData.Location.Monuments.QuarryA = baseConfig.Location.Monuments.QuarryA;
                configData.Location.Monuments.QuarryB = baseConfig.Location.Monuments.QuarryB;
                configData.Location.Monuments.QuarryC = baseConfig.Location.Monuments.QuarryC;
                configData.Location.Monuments.SwampA = baseConfig.Location.Monuments.SwampA;
                configData.Location.Monuments.SwampB = baseConfig.Location.Monuments.SwampB;
                configData.Location.Monuments.SwampC = baseConfig.Location.Monuments.SwampC;
            }

            if (configData.Version < new Core.VersionNumber(1, 2, 3))
            {
                configData.Location.Monuments.MiningOutpost1 = baseConfig.Location.Monuments.MiningOutpost1;
                configData.Location.Monuments.MiningOutpost2 = baseConfig.Location.Monuments.MiningOutpost2;
                configData.Location.Monuments.MiningOutpost3 = baseConfig.Location.Monuments.MiningOutpost3;
                configData.Location.Monuments.Supermarket1 = baseConfig.Location.Monuments.Supermarket1;
                configData.Location.Monuments.Supermarket2 = baseConfig.Location.Monuments.Supermarket2;
                configData.Location.Monuments.Supermarket3 = baseConfig.Location.Monuments.Supermarket3;
                configData.Location.Monuments.LightHouse1 = baseConfig.Location.Monuments.LightHouse1;
                configData.Location.Monuments.LightHouse2 = baseConfig.Location.Monuments.LightHouse2;
            }

            if (configData.Version < new Core.VersionNumber(1, 2, 4))
                configData.Location.Monuments.GasStation3 = baseConfig.Location.Monuments.GasStation3;

            if (configData.Version < new Core.VersionNumber(1, 2, 5))
            {
                configData.Location.Monuments.FishingVillage1 = baseConfig.Location.Monuments.FishingVillage1;
                configData.Location.Monuments.FishingVillage2 = baseConfig.Location.Monuments.FishingVillage2;
                configData.Location.Monuments.FishingVillage3 = baseConfig.Location.Monuments.FishingVillage3;
            }

            if (configData.Version < new Core.VersionNumber(1, 2, 7))
            {
                configData.Location.Monuments.HarborLarge.NextGenPVERules = "exclude";
                configData.Location.Monuments.HarborSmall.NextGenPVERules = "exclude";
                configData.Location.Monuments.Airfield.NextGenPVERules = "exclude";
                configData.Location.Monuments.LargeOilRig.NextGenPVERules = "exclude";
                configData.Location.Monuments.SmallOilRig.NextGenPVERules = "exclude";
                configData.Location.Monuments.Powerplant.NextGenPVERules = "exclude";
                configData.Location.Monuments.MilitaryTunnels.NextGenPVERules = "exclude";
                configData.Location.Monuments.Junkyard.NextGenPVERules = "exclude";
                configData.Location.Monuments.WaterTreatment.NextGenPVERules = "exclude";
                configData.Location.Monuments.TrainYard.NextGenPVERules = "exclude";
                configData.Location.Monuments.Excavator.NextGenPVERules = "exclude";
                configData.Location.Monuments.SatelliteDish.NextGenPVERules = "exclude";
                configData.Location.Monuments.SewerBranch.NextGenPVERules = "exclude";
                configData.Location.Monuments.Dome.NextGenPVERules = "exclude";
                configData.Location.Monuments.GasStation1.NextGenPVERules = "exclude";
                configData.Location.Monuments.GasStation2.NextGenPVERules = "exclude";
                configData.Location.Monuments.GasStation3.NextGenPVERules = "exclude";
                configData.Location.Monuments.QuarryA.NextGenPVERules = "exclude";
                configData.Location.Monuments.QuarryB.NextGenPVERules = "exclude";
                configData.Location.Monuments.QuarryC.NextGenPVERules = "exclude";
                configData.Location.Monuments.SwampA.NextGenPVERules = "exclude";
                configData.Location.Monuments.SwampB.NextGenPVERules = "exclude";
                configData.Location.Monuments.SwampC.NextGenPVERules = "exclude";
                configData.Location.Monuments.MiningOutpost1.NextGenPVERules = "exclude";
                configData.Location.Monuments.MiningOutpost2.NextGenPVERules = "exclude";
                configData.Location.Monuments.MiningOutpost3.NextGenPVERules = "exclude";
                configData.Location.Monuments.Supermarket1.NextGenPVERules = "exclude";
                configData.Location.Monuments.Supermarket2.NextGenPVERules = "exclude";
                configData.Location.Monuments.Supermarket3.NextGenPVERules = "exclude";
                configData.Location.Monuments.LightHouse1.NextGenPVERules = "exclude";
                configData.Location.Monuments.LightHouse2.NextGenPVERules = "exclude";
                configData.Location.Monuments.FishingVillage1.NextGenPVERules = "exclude";
                configData.Location.Monuments.FishingVillage2.NextGenPVERules = "exclude";
                configData.Location.Monuments.FishingVillage3.NextGenPVERules = "exclude";
            }

            if (configData.Version < new Core.VersionNumber(1, 3, 0))
            {
                configData.Location.Monuments.Outpost = baseConfig.Location.Monuments.Outpost;
                configData.Location.Monuments.BanditCamp = baseConfig.Location.Monuments.BanditCamp;
            }

            if (configData.Version < new Core.VersionNumber(1, 3, 1))
            {
                configData.Location.Monuments.DesertBaseA = baseConfig.Location.Monuments.DesertBaseA;
                configData.Location.Monuments.DesertBaseB = baseConfig.Location.Monuments.DesertBaseB;
                configData.Location.Monuments.DesertBaseC = baseConfig.Location.Monuments.DesertBaseC;
                configData.Location.Monuments.DesertBaseD = baseConfig.Location.Monuments.DesertBaseD;
            }

            if (configData.Version < new Core.VersionNumber(1, 3, 3))
            {
                configData.Location.Monuments.StableA = baseConfig.Location.Monuments.StableA;
                configData.Location.Monuments.StableB = baseConfig.Location.Monuments.StableB;
            }

            if (configData.Version < new Core.VersionNumber(1, 3, 5))
            {
                configData.Location.Monuments.ArticA = baseConfig.Location.Monuments.ArticA;
            }

            if (configData.Version < new Core.VersionNumber(1, 3, 7))
            {
                configData.Location.Monuments.missileSilo = baseConfig.Location.Monuments.missileSilo;
            }

            if (configData.Version < new Core.VersionNumber(1, 3, 9))
            {
                configData.Location.Monuments.ferryTerminal = baseConfig.Location.Monuments.ferryTerminal;
            }

            if (configData.Version < new Core.VersionNumber(1, 4, 0))
            {
                configData.Location.Monuments.radTown = baseConfig.Location.Monuments.radTown;
            }
            if (configData.Version < new Core.VersionNumber(1, 4, 5))
            {
                configData.Location.Monuments.JungleZiggurat = baseConfig.Location.Monuments.JungleZiggurat;
                configData.Location.Monuments.ApartmentComplex = baseConfig.Location.Monuments.ApartmentComplex;
            }

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion
    }
}