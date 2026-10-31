using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("NPC Health Bar", "Chris", "1.0.10")]
    public class NPCHealthBar : RustPlugin
    {

        [PluginReference]
        private Plugin ImageLibrary;

        #region Profile Data

        private Dictionary<int, ProfileData> profileData = new Dictionary<int, ProfileData>();

        class ProfileData
        {
            public bool enableImages;
            public string panelbgAnchorMin;
            public string panelbgAnchorMax;
            public string panelbgColor;
            public string overlineAnchorMin;
            public string overlineAnchorMax;
            public string overlineColor;
            public string panelHpAnchorMin;
            public string panelHpAnchorMax;
            public string panelHpColor;
            public string statusTextText;
            public int statusTextFontsize;
            public string statusTextAnchorMin;
            public string statusTextAnchorMax;
            public string statusTextColor;
            public TextAnchor statusTextAlign;
            public string nameTextText;
            public string nameTextAnchorMin;
            public string nameTextAnchorMax;
            public string nameTextColor;
            public TextAnchor nameTextAlign;
            public string iconPanelAnchorMin;
            public string iconPanelAnchorMax;
            public string iconPanelColor;
            public Dictionary<string, string> images;
        }

        void InitializeProfileData()
        {
            profileData[1] = new ProfileData
            {
                enableImages = false,
                panelbgAnchorMin = "0.4 0.4",
                panelbgAnchorMax = "0.6 0.41",
                panelbgColor = "0.19 0.32 0.06 1",
                overlineAnchorMin = "0 0",
                overlineAnchorMax = "1 1",
                overlineColor = "0 0 0 0",
                panelHpAnchorMin = "0 0",
                panelHpAnchorMax = "1",
                panelHpColor = "1 1 1 1",
                statusTextText = "{currenthp} / {maxhp}",
                statusTextFontsize = 12,
                statusTextAnchorMin = "0 1",
                statusTextAnchorMax = "1 3",
                statusTextColor = "1 1 1 1",
                statusTextAlign = TextAnchor.UpperRight,
                nameTextText = "{name}",
                nameTextAnchorMin = "0 1",
                nameTextAnchorMax = "1 3",
                nameTextColor = "1 1 1 1",
                nameTextAlign = TextAnchor.UpperLeft,
                iconPanelAnchorMin = "-0.1 0",
                iconPanelAnchorMax = "0 1",
                iconPanelColor = "0 0 0 1",
                images = new Dictionary<string, string>()
            };

            profileData[2] = new ProfileData
            {
                enableImages = false,
                panelbgAnchorMin = "0.45 0.43",
                panelbgAnchorMax = "0.55 0.432",
                panelbgColor = "1 1 0 1",
                overlineAnchorMin = "0 0",
                overlineAnchorMax = "1 1",
                overlineColor = "0 0 0 0",
                panelHpAnchorMin = "0 0",
                panelHpAnchorMax = "1",
                panelHpColor = "1 0.55 0 1",
                statusTextText = "{currenthp} / {maxhp}",
                statusTextFontsize = 10,
                statusTextAnchorMin = "0 1",
                statusTextAnchorMax = "1 16",
                statusTextColor = "1 1 1 1",
                statusTextAlign = TextAnchor.MiddleCenter,
                nameTextText = "{name}",
                nameTextAnchorMin = "0 1",
                nameTextAnchorMax = "1 3",
                nameTextColor = "1 1 1 1",
                nameTextAlign = TextAnchor.UpperLeft,
                iconPanelAnchorMin = "-0.1 0",
                iconPanelAnchorMax = "0 1",
                iconPanelColor = "0 0 0 1",
                images = new Dictionary<string, string>()
            };

            profileData[3] = new ProfileData
            {
                enableImages = true,
                panelbgAnchorMin = "0.4 0.8",
                panelbgAnchorMax = "0.6 0.86",
                panelbgColor = "0 0 0 0.7",
                overlineAnchorMin = "0.015 0.10",
                overlineAnchorMax = "0.8 0.40",
                overlineColor = "0 0 0 0",
                panelHpAnchorMin = "0 0",
                panelHpAnchorMax = "1",
                panelHpColor = "0.65 0.2 0 1",
                statusTextText = "{currenthp} / {maxhp}",
                statusTextFontsize = 12,
                statusTextAnchorMin = "0.02 0.35",
                statusTextAnchorMax = "0.77 0.86",
                statusTextColor = "1 1 1 1",
                statusTextAlign = TextAnchor.UpperRight,
                nameTextText = "{name}",
                nameTextAnchorMin = "0.02 0.35",
                nameTextAnchorMax = "1 0.86",
                nameTextColor = "1 1 1 1",
                nameTextAlign = TextAnchor.UpperLeft,
                iconPanelAnchorMin = "0.82 0.07",
                iconPanelAnchorMax = "0.98 0.90",
                iconPanelColor = "0 0 0 1",
                images = new Dictionary<string, string>()
            };

            profileData[4] = new ProfileData
            {
                enableImages = true,
                panelbgAnchorMin = "0.4 0.8",
                panelbgAnchorMax = "0.6 0.84",
                panelbgColor = "0 0 0 0.9",
                overlineAnchorMin = "0.15 0.12",
                overlineAnchorMax = "0.975 0.85",
                overlineColor = "0 0 0 0",
                panelHpAnchorMin = "0 0",
                panelHpAnchorMax = "1",
                panelHpColor = "0.14 0.6 0 1",
                statusTextText = "{currenthp} / {maxhp}",
                statusTextFontsize = 12,
                statusTextAnchorMin = "0.02 0.1",
                statusTextAnchorMax = "0.98 0.75",
                statusTextColor = "1 1 1 1",
                statusTextAlign = TextAnchor.UpperRight,
                nameTextText = "{name}",
                nameTextAnchorMin = "0.17 0.1",
                nameTextAnchorMax = "0.975 0.75",
                nameTextColor = "1 1 1 1",
                nameTextAlign = TextAnchor.UpperLeft,
                iconPanelAnchorMin = "0.01 0.07",
                iconPanelAnchorMax = "0.13 0.88",
                iconPanelColor = "0 0 0 1",
                images = new Dictionary<string, string>
                {
                    { "scientistnpc_roam", "https://i.postimg.cc/t4wDtsGT/4tyicon.png" }
                }
            };

            profileData[5] = new ProfileData
            {
                enableImages = false,
                panelbgAnchorMin = "0.45 0.36",
                panelbgAnchorMax = "0.55 0.5",
                panelbgColor = "1 1 0 0",
                overlineAnchorMin = "0 0",
                overlineAnchorMax = "1 1",
                overlineColor = "0 0 0 0",
                panelHpAnchorMin = "0 0",
                panelHpAnchorMax = "1",
                panelHpColor = "1 0.55 0 0",
                statusTextText = "{currenthp} / {maxhp}",
                statusTextFontsize = 18,
                statusTextAnchorMin = "0 0",
                statusTextAnchorMax = "1 1",
                statusTextAlign = TextAnchor.MiddleCenter,
                nameTextText = "{name}",
                nameTextAnchorMin = "0 1",
                nameTextAnchorMax = "1 3",
                nameTextColor = "1 1 1 0",
                nameTextAlign = TextAnchor.UpperLeft,
                iconPanelAnchorMin = "-0.1 0",
                iconPanelAnchorMax = "0 1",
                iconPanelColor = "0 0 0 1",
                images = new Dictionary<string, string>()
            };
        }

        ProfileData GetPlayerProfile(BasePlayer player)
        {
            if (config != null && config.adminOnly)
            {
                return GetProfileDataFromDefaultProfile();
            }

            if (config != null && config.useDefaultProfile)
            {
                if (player == null || !playerData.ContainsKey(player.userID))
                {
                    return GetProfileDataFromDefaultProfile();
                }

                var pData = playerData[player.userID];
                
                if (!pData.hasExplicitlyChosenProfile)
                {
                    return GetProfileDataFromDefaultProfile();
                }
            }

            if (player == null || !playerData.ContainsKey(player.userID))
            {
                return profileData.ContainsKey(1) ? profileData[1] : null;
            }

            int profileNum = playerData[player.userID].profileNumber;
            if (profileNum < 1 || profileNum > 5 || !profileData.ContainsKey(profileNum))
            {
                return profileData.ContainsKey(1) ? profileData[1] : null;
            }

            return profileData[profileNum];
        }

        ProfileData GetProfileDataFromDefaultProfile()
        {
            if (config == null || config.defaultprofile == null)
            {
                return profileData.ContainsKey(1) ? profileData[1] : null;
            }

            var defaultProf = config.defaultprofile;
            return new ProfileData
            {
                enableImages = defaultProf.enableImages,
                panelbgAnchorMin = defaultProf.panelbgAnchorMin,
                panelbgAnchorMax = defaultProf.panelbgAnchorMax,
                panelbgColor = defaultProf.panelbgColor,
                overlineAnchorMin = defaultProf.overlineAnchorMin,
                overlineAnchorMax = defaultProf.overlineAnchorMax,
                overlineColor = defaultProf.overlineColor,
                panelHpAnchorMin = defaultProf.panelHpAnchorMin,
                panelHpAnchorMax = defaultProf.panelHpAnchorMax,
                panelHpColor = defaultProf.panelHpColor,
                statusTextText = defaultProf.statusTextText,
                statusTextFontsize = defaultProf.statusTextFontsize,
                statusTextAnchorMin = defaultProf.statusTextAnchorMin,
                statusTextAnchorMax = defaultProf.statusTextAnchorMax,
                statusTextColor = defaultProf.statusTextColor,
                statusTextAlign = defaultProf.statusTextAlign,
                nameTextText = defaultProf.nameTextText,
                nameTextAnchorMin = defaultProf.nameTextAnchorMin,
                nameTextAnchorMax = defaultProf.nameTextAnchorMax,
                nameTextColor = defaultProf.nameTextColor,
                nameTextAlign = defaultProf.nameTextAlign,
                iconPanelAnchorMin = defaultProf.iconPanelAnchorMin,
                iconPanelAnchorMax = defaultProf.iconPanelAnchorMax,
                iconPanelColor = defaultProf.iconPanelColor,
                images = defaultProf.images != null ? new Dictionary<string, string>(defaultProf.images) : new Dictionary<string, string>()
            };
        }

        void CopyProfileDataToDefaultProfile(ProfileData source)
        {
            if (config == null || source == null)
            {
                return;
            }

            if (config.defaultprofile == null)
            {
                config.defaultprofile = new Configuration.DefaultProfileData();
            }

            var defaultProf = config.defaultprofile;
            defaultProf.enableImages = source.enableImages;
            defaultProf.panelbgAnchorMin = source.panelbgAnchorMin;
            defaultProf.panelbgAnchorMax = source.panelbgAnchorMax;
            defaultProf.panelbgColor = source.panelbgColor;
            defaultProf.overlineAnchorMin = source.overlineAnchorMin;
            defaultProf.overlineAnchorMax = source.overlineAnchorMax;
            defaultProf.overlineColor = source.overlineColor;
            defaultProf.panelHpAnchorMin = source.panelHpAnchorMin;
            defaultProf.panelHpAnchorMax = source.panelHpAnchorMax;
            defaultProf.panelHpColor = source.panelHpColor;
            defaultProf.statusTextText = source.statusTextText;
            defaultProf.statusTextFontsize = source.statusTextFontsize;
            defaultProf.statusTextAnchorMin = source.statusTextAnchorMin;
            defaultProf.statusTextAnchorMax = source.statusTextAnchorMax;
            defaultProf.statusTextColor = source.statusTextColor;
            defaultProf.statusTextAlign = source.statusTextAlign;
            defaultProf.nameTextText = source.nameTextText;
            defaultProf.nameTextAnchorMin = source.nameTextAnchorMin;
            defaultProf.nameTextAnchorMax = source.nameTextAnchorMax;
            defaultProf.nameTextColor = source.nameTextColor;
            defaultProf.nameTextAlign = source.nameTextAlign;
            defaultProf.iconPanelAnchorMin = source.iconPanelAnchorMin;
            defaultProf.iconPanelAnchorMax = source.iconPanelAnchorMax;
            defaultProf.iconPanelColor = source.iconPanelColor;
            defaultProf.images = source.images != null ? new Dictionary<string, string>(source.images) : new Dictionary<string, string>();
        }

        (float horizontalMultiplier, float verticalMultiplier) GetResizeProportions(int profileNumber)
        {
            switch (profileNumber)
            {
                case 1:
                    return (1f, 0.05f);
                case 2:
                    return (1f, 0.02f);
                case 3:
                    return (1f, 0.3f);
                case 4:
                    return (1f, 0.2f);
                case 5:
                    return (1f, 1.4f);
                default:
                    return (1f, 0.05f);
            }
        }

        void GetPlayerPanelbgAnchors(BasePlayer player, out string panelbgAnchorMin, out string panelbgAnchorMax)
        {
            var profile = GetPlayerProfile(player);
            
            panelbgAnchorMin = profile?.panelbgAnchorMin ?? "";
            panelbgAnchorMax = profile?.panelbgAnchorMax ?? "";

            if (config != null && config.adminOnly)
            {
                return;
            }

            if (player != null && playerData.ContainsKey(player.userID))
            {
                var pData = playerData[player.userID];
                if (!string.IsNullOrEmpty(pData.panelbgAnchorMin)) panelbgAnchorMin = pData.panelbgAnchorMin;
                if (!string.IsNullOrEmpty(pData.panelbgAnchorMax)) panelbgAnchorMax = pData.panelbgAnchorMax;
            }
        }

        void GetPlayerAnchorValues(BasePlayer player, out string panelbgAnchorMin, out string panelbgAnchorMax,
            out string overlineAnchorMin, out string overlineAnchorMax,
            out string panelHpAnchorMin, out string panelHpAnchorMax,
            out string statusTextAnchorMin, out string statusTextAnchorMax,
            out string nameTextAnchorMin, out string nameTextAnchorMax,
            out string iconPanelAnchorMin, out string iconPanelAnchorMax)
        {
            var profile = GetPlayerProfile(player);
            
            panelbgAnchorMin = profile?.panelbgAnchorMin ?? "";
            panelbgAnchorMax = profile?.panelbgAnchorMax ?? "";
            overlineAnchorMin = profile?.overlineAnchorMin ?? "";
            overlineAnchorMax = profile?.overlineAnchorMax ?? "";
            panelHpAnchorMin = profile?.panelHpAnchorMin ?? "";
            panelHpAnchorMax = profile?.panelHpAnchorMax ?? "";
            statusTextAnchorMin = profile?.statusTextAnchorMin ?? "";
            statusTextAnchorMax = profile?.statusTextAnchorMax ?? "";
            nameTextAnchorMin = profile?.nameTextAnchorMin ?? "";
            nameTextAnchorMax = profile?.nameTextAnchorMax ?? "";
            iconPanelAnchorMin = profile?.iconPanelAnchorMin ?? "";
            iconPanelAnchorMax = profile?.iconPanelAnchorMax ?? "";

            if (config != null && config.adminOnly)
            {
                return;
            }

            if (player != null && playerData.ContainsKey(player.userID))
            {
                var pData = playerData[player.userID];
                if (!string.IsNullOrEmpty(pData.panelbgAnchorMin)) panelbgAnchorMin = pData.panelbgAnchorMin;
                if (!string.IsNullOrEmpty(pData.panelbgAnchorMax)) panelbgAnchorMax = pData.panelbgAnchorMax;
            }
        }

        bool ShouldUseProfile5Logic(ProfileData profile)
        {
            if (profile == null) return false;

            if (config != null && config.adminOnly && profileData.ContainsKey(5))
            {
                var profile5 = profileData[5];
                if (profile.statusTextAlign == profile5.statusTextAlign &&
                    profile.statusTextFontsize == profile5.statusTextFontsize &&
                    profile.statusTextAnchorMin == profile5.statusTextAnchorMin &&
                    profile.statusTextAnchorMax == profile5.statusTextAnchorMax &&
                    profile.nameTextColor == profile5.nameTextColor &&
                    profile.panelbgColor == profile5.panelbgColor)
                {
                    return true;
                }
                return false;
            }

            return false;
        }

        int GetEffectiveProfileNumber(BasePlayer player)
        {
            if (config != null && config.adminOnly)
            {
                var profile = GetPlayerProfile(player);
                if (ShouldUseProfile5Logic(profile))
                {
                    return 5;
                }
                return 1;
            }

            if (player != null && playerData.ContainsKey(player.userID))
            {
                return playerData[player.userID].profileNumber;
            }
            return 1;
        }


        #endregion


        Dictionary<BasePlayer, long> lastHits = new Dictionary<BasePlayer, long>();

        List<BasePlayer> toDelete = new List<BasePlayer>();

        void OnServerInitialized(bool initial)
        {
            UiNpcBar();
            InitializeProfileData();
            LoadPlayerData();

            if (config != null)
            {
                if (config.adminOnly && config.useDefaultProfile)
                {
                    config.useDefaultProfile = false;
                    SaveConfig();
                    Puts("Warning: Both 'adminOnly' and 'useDefaultProfile' were true. 'adminOnly' takes priority. 'useDefaultProfile' has been set to false.");
                }
            }

            if (config != null && config.adminOnly && profileData.ContainsKey(1))
            {
                CopyProfileDataToDefaultProfile(profileData[1]);
                if (config.defaultprofile != null)
                {
                    config.defaultprofile.defaultProfileNumber = 1;
                }
                SaveConfig();
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }

            if (config.requirePerm)
                permission.RegisterPermission("npchealthbar.use", this);

            if (ImageLibrary != null)
            {
                foreach (string npc in config.images.Keys)
                {
                    ImageLibrary.Call("AddImage", config.images[npc], config.images[npc]);
                }
            }
            else
            {
                Puts("ImageLibrary not found, images won't load.");
            }

            timer.Every(1f, () =>
            {
                toDelete.Clear();

                foreach(var player in lastHits.Keys)
                {
                    if((DateTimeOffset.UtcNow.ToUnixTimeSeconds()-lastHits[player])>config.timeToDestroy)
                    {
                        CuiHelper.DestroyUi(player, "hp_background");
                        
                        toDelete.Add(player);

                    }
                }

                foreach(var player in toDelete)
                {
                    lastHits.Remove(player);
                }

                toDelete.Clear();
            });
            
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            try
            {
                if (info.InitiatorPlayer != null)
                {
                    CuiHelper.DestroyUi(info.InitiatorPlayer, "hp_background");
                }
            }
            catch
            {
                //
            }
        }

        #region Hooks

        void OnEntityTakeDamage(BradleyAPC apc, HitInfo info)
        {
            

            if (info.InitiatorPlayer != null)
            {
                var attacker = info.InitiatorPlayer;
                if (config.requirePerm)
                {
                    if (!permission.UserHasPermission(attacker.UserIDString, "npchealthbar.use"))
                    {
                        return;
                    }
                }

                lastHits.Remove(attacker);
                lastHits.Add(attacker, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                NextTick(() => {
                    try
                    {
                        var profile = GetPlayerProfile(attacker);
                        if (profile == null) return;

                        if (config.blacklist.Contains(apc.ShortPrefabName))
                        {
                            return;
                        }
                        if (apc.health < 1)
                        {
                            CuiHelper.DestroyUi(info.InitiatorPlayer, "hp_background");
                            return;
                        }
                        float onePercent = apc.MaxHealth() / 100;
                        float leftPercent = apc.Health() / onePercent;

                        var c = new CuiElementContainer();

                        GetPlayerPanelbgAnchors(attacker, out string panelbgAnchorMin, out string panelbgAnchorMax);

                        Create.Panel(ref c, "hp_background", "Overlay", profile.panelbgColor, panelbgAnchorMin, panelbgAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                        Create.Panel(ref c, "bar_overline", "hp_background", profile.overlineColor, profile.overlineAnchorMin, profile.overlineAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                        string progressAnchorMax = $"{leftPercent / 100} {profile.panelHpAnchorMax}";
                        Create.Panel(ref c, "hp_progress", "bar_overline", profile.panelHpColor, profile.panelHpAnchorMin, progressAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                        string statusText;
                        string statusFont;
                        string statusColor;
                        int statusFontSize;
                        int effectiveProfileNum = GetEffectiveProfileNumber(attacker);
                        if (effectiveProfileNum == 5)
                        {
                            int roundedPercent = (int)Math.Ceiling(leftPercent);
                            statusText = $"{roundedPercent}";
                            statusFont = "permanentmarker.ttf";
                            
                            if (roundedPercent > 75)
                            {
                                statusColor = "1 1 1 1";
                                statusFontSize = profile.statusTextFontsize;
                            }
                            else if (roundedPercent > 50)
                            {
                                statusColor = "1 1 0 1";
                                statusFontSize = profile.statusTextFontsize + 2;
                            }
                            else if (roundedPercent > 25)
                            {
                                statusColor = "1 0.5 0 1";
                                statusFontSize = profile.statusTextFontsize + 4;
                            }
                            else
                            {
                                statusColor = "1 0 0 1";
                                statusFontSize = profile.statusTextFontsize + 6;
                            }
                        }
                        else
                        {
                            statusText = profile.statusTextText.Replace("{currenthp}", $"{(int) apc.Health()}").Replace("{maxhp}", $"{apc.MaxHealth()}");
                            statusFont = "robotocondensed-regular.ttf";
                            statusColor = profile.statusTextColor;
                            statusFontSize = profile.statusTextFontsize;
                        }
                        Create.Text(ref c, "hp_statustext", "hp_background", statusColor, statusText, statusFontSize, profile.statusTextAnchorMin, profile.statusTextAnchorMax, profile.statusTextAlign, statusFont, 0.0f, 0.0f, "0 0 0 0", "0 0");

                        if (config.names.ContainsKey(apc.ShortPrefabName))
                        {
                            Create.Text(ref c, "hp_name", "hp_background", profile.nameTextColor, profile.nameTextText.Replace("{name}", $"{config.names[apc.ShortPrefabName]}"), 12, profile.nameTextAnchorMin, profile.nameTextAnchorMax, profile.nameTextAlign, "robotocondensed-regular.ttf", 0.0f, 0.0f, "0 0 0 0", "0 0");
                        }

                        string imageUrl = null;
                        if (profile.images != null && profile.images.ContainsKey(apc.ShortPrefabName))
                        {
                            imageUrl = profile.images[apc.ShortPrefabName];
                        }
                        else if (config.images.ContainsKey(apc.ShortPrefabName))
                        {
                            imageUrl = config.images[apc.ShortPrefabName];
                        }

                        if (ImageLibrary != null && imageUrl != null && (bool)ImageLibrary.Call("HasImage", imageUrl) && profile.enableImages)
                        {
                            Create.Panel(ref c, "hp_imagepanel", "hp_background", profile.iconPanelColor, profile.iconPanelAnchorMin, profile.iconPanelAnchorMax, false, 0.0f, 0.0f, "", "", "", "");
                            Create.Image(ref c, "hp_image", "hp_imagepanel", (string)ImageLibrary.Call("GetImage", imageUrl), "0 0", "1 1", 0.0f, 0f, "", "");
                        }

                        Create.Text(ref c, "notifications_countdown", "Overlay", "1 1 1 1", "", 12, "0 0", "0 0", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");

                        CuiHelper.DestroyUi(attacker, "notifications_countdown");
                        CuiHelper.DestroyUi(attacker, "hp_background");
                        CuiHelper.AddUi(attacker, c);
                    }
                    catch
                    {
                    }
                });
            }

        }


        void OnEntityTakeDamage(PatrolHelicopter heli, HitInfo info)
        {
            

            if (info.InitiatorPlayer != null)
            {
                var attacker = info.InitiatorPlayer;
                if (config.requirePerm)
                {
                    if (!permission.UserHasPermission(attacker.UserIDString, "npchealthbar.use"))
                    {
                        return;
                    }
                }

                lastHits.Remove(attacker);
                lastHits.Add(attacker, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                NextTick(() => {

                    try
                    {
                        var profile = GetPlayerProfile(attacker);
                        if (profile == null) return;

                        // For vehicles like PatrolHelicopter, the HitInfo.HitEntity can become null/destroyed
                        // before the NextTick runs. Use the provided `heli` instance instead.
                        if (heli == null || heli.IsDestroyed)
                        {
                            return;
                        }

                        if (config.blacklist.Contains(heli.ShortPrefabName))
                        {
                            return;
                        }

                        if ((heli.myAI != null && heli.myAI.isDead) || heli.health < 1)
                        {
                            CuiHelper.DestroyUi(attacker, "hp_background");
                            return;
                        }

                        float heliMaxHealth = heli.MaxHealth();
                        if (heliMaxHealth <= 0f) return;
                        float onePercent = heliMaxHealth / 100;
                        float leftPercent = heli.Health() / onePercent;

                        var c = new CuiElementContainer();

                        GetPlayerPanelbgAnchors(attacker, out string panelbgAnchorMin, out string panelbgAnchorMax);

                        Create.Panel(ref c, "hp_background", "Overlay", profile.panelbgColor, panelbgAnchorMin, panelbgAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                        Create.Panel(ref c, "bar_overline", "hp_background", profile.overlineColor, profile.overlineAnchorMin, profile.overlineAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                        string progressAnchorMax = $"{leftPercent / 100} {profile.panelHpAnchorMax}";
                        Create.Panel(ref c, "hp_progress", "bar_overline", profile.panelHpColor, profile.panelHpAnchorMin, progressAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                        string statusText;
                        string statusFont;
                        string statusColor;
                        int statusFontSize;
                        int effectiveProfileNum = GetEffectiveProfileNumber(attacker);
                        if (effectiveProfileNum == 5)
                        {
                            int roundedPercent = (int)Math.Ceiling(leftPercent);
                            statusText = $"{roundedPercent}";
                            statusFont = "permanentmarker.ttf";
                            
                            if (roundedPercent > 75)
                            {
                                statusColor = "1 1 1 1";
                                statusFontSize = profile.statusTextFontsize;
                            }
                            else if (roundedPercent > 50)
                            {
                                statusColor = "1 1 0 1";
                                statusFontSize = profile.statusTextFontsize + 2;
                            }
                            else if (roundedPercent > 25)
                            {
                                statusColor = "1 0.5 0 1";
                                statusFontSize = profile.statusTextFontsize + 4;
                            }
                            else
                            {
                                statusColor = "1 0 0 1";
                                statusFontSize = profile.statusTextFontsize + 6;
                            }
                        }
                        else
                        {
                            statusText = profile.statusTextText
                                .Replace("{currenthp}", $"{(int)heli.Health()}")
                                .Replace("{maxhp}", $"{heliMaxHealth}");
                            statusFont = "robotocondensed-regular.ttf";
                            statusColor = profile.statusTextColor;
                            statusFontSize = profile.statusTextFontsize;
                        }
                        Create.Text(ref c, "hp_statustext", "hp_background", statusColor, statusText, statusFontSize, profile.statusTextAnchorMin, profile.statusTextAnchorMax, profile.statusTextAlign, statusFont, 0.0f, 0.0f, "0 0 0 0", "0 0");

                        if (config.names.ContainsKey(heli.ShortPrefabName))
                        {
                            Create.Text(ref c, "hp_name", "hp_background", profile.nameTextColor, profile.nameTextText.Replace("{name}", $"{config.names[heli.ShortPrefabName]}"), 12, profile.nameTextAnchorMin, profile.nameTextAnchorMax, profile.nameTextAlign, "robotocondensed-regular.ttf", 0.0f, 0.0f, "0 0 0 0", "0 0");
                        }

                        string imageUrl = null;
                        if (profile.images != null && profile.images.ContainsKey(heli.ShortPrefabName))
                        {
                            imageUrl = profile.images[heli.ShortPrefabName];
                        }
                        else if (config.images.ContainsKey(heli.ShortPrefabName))
                        {
                            imageUrl = config.images[heli.ShortPrefabName];
                        }

                        if (ImageLibrary != null && imageUrl != null && (bool)ImageLibrary.Call("HasImage", imageUrl) && profile.enableImages)
                        {
                            Create.Panel(ref c, "hp_imagepanel", "hp_background", profile.iconPanelColor, profile.iconPanelAnchorMin, profile.iconPanelAnchorMax, false, 0.0f, 0.0f, "", "", "", "");
                            Create.Image(ref c, "hp_image", "hp_imagepanel", (string)ImageLibrary.Call("GetImage", imageUrl), "0 0", "1 1", 0.0f, 0f, "", "");
                        }

                        Create.Text(ref c, "notifications_countdown", "Overlay", "1 1 1 1", "", 12, "0 0", "0 0", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");

                        CuiHelper.DestroyUi(attacker, "notifications_countdown");
                        CuiHelper.DestroyUi(attacker, "hp_background");
                        CuiHelper.AddUi(attacker, c);
                    }
                    catch
                    {
                    }

                });
            }

        }

        void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {   
            if (attacker == null) return;

            lastHits.Remove(attacker);
            lastHits.Add(attacker, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // `info.HitEntity` may become null/destroyed by the time the NextTick runs.
            // Capture the target entity now and use it inside NextTick.
            var hitEntity = info?.HitEntity;


            NextTick(() => {
                try
                {   
                    if (attacker == null || attacker.IsDestroyed || !attacker.IsConnected) return;

                    var profile = GetPlayerProfile(attacker);
                    if (profile == null) return;

                    if (config.requirePerm)
                    {
                        if (!permission.UserHasPermission(attacker.UserIDString, "npchealthbar.use"))
                        {
                            return;
                        }
                    }
                    if (hitEntity == null || hitEntity.IsDestroyed || !IsNpcEntity(hitEntity))
                    {
                        return;
                    }

                    if (config.blacklist.Contains(hitEntity.ShortPrefabName))
                    {
                        return;
                    }

                    if (hitEntity.ShortPrefabName.Contains("corpse") || hitEntity.Health() < 1)
                    {
                        CuiHelper.DestroyUi(attacker, "hp_background");
                        return;
                    }


                    float onePercent = hitEntity.MaxHealth() / 100;
                    float leftPercent = hitEntity.Health() / onePercent;

                    var c = new CuiElementContainer();

                    GetPlayerPanelbgAnchors(attacker, out string panelbgAnchorMin, out string panelbgAnchorMax);

                    Create.Panel(ref c, "hp_background", "Overlay", profile.panelbgColor, panelbgAnchorMin, panelbgAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                    Create.Panel(ref c, "bar_overline", "hp_background", profile.overlineColor, profile.overlineAnchorMin, profile.overlineAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                    string progressAnchorMax = $"{leftPercent / 100} {profile.panelHpAnchorMax}";
                    Create.Panel(ref c, "hp_progress", "bar_overline", profile.panelHpColor, profile.panelHpAnchorMin, progressAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

                    string statusText;
                    string statusFont;
                    string statusColor;
                    int statusFontSize;
                    int effectiveProfileNum = GetEffectiveProfileNumber(attacker);
                    if (effectiveProfileNum == 5)
                    {
                        int roundedPercent = (int)Math.Ceiling(leftPercent);
                        statusText = $"{roundedPercent}";
                        statusFont = "permanentmarker.ttf";
                        
                        if (roundedPercent > 75)
                        {
                            statusColor = "1 1 1 1";
                            statusFontSize = profile.statusTextFontsize;
                        }
                        else if (roundedPercent > 50)
                        {
                            statusColor = "1 1 0 1";
                            statusFontSize = profile.statusTextFontsize + 2;
                        }
                        else if (roundedPercent > 25)
                        {
                            statusColor = "1 0.5 0 1";
                            statusFontSize = profile.statusTextFontsize + 4;
                        }
                        else
                        {
                            statusColor = "1 0 0 1";
                            statusFontSize = profile.statusTextFontsize + 6;
                        }
                    }
                    else
                    {
                        statusText = profile.statusTextText
                            .Replace("{currenthp}", $"{(int)hitEntity.Health()}")
                            .Replace("{maxhp}", $"{hitEntity.MaxHealth()}");
                        statusFont = "robotocondensed-regular.ttf";
                        statusColor = profile.statusTextColor;
                        statusFontSize = profile.statusTextFontsize;
                    }
                    Create.Text(ref c, "hp_statustext", "hp_background", statusColor, statusText, statusFontSize, profile.statusTextAnchorMin, profile.statusTextAnchorMax, profile.statusTextAlign, statusFont, 0.0f, 0.0f, "0 0 0 0", "0 0");

                    if (config.names.ContainsKey(hitEntity.ShortPrefabName))
                    {
                        Create.Text(ref c, "hp_name", "hp_background", profile.nameTextColor, profile.nameTextText.Replace("{name}", $"{config.names[hitEntity.ShortPrefabName]}"), 12, profile.nameTextAnchorMin, profile.nameTextAnchorMax, profile.nameTextAlign, "robotocondensed-regular.ttf", 0.0f, 0.0f, "0 0 0 0", "0 0");
                    }

                    string imageUrl = null;
                    if (profile.images != null && profile.images.ContainsKey(hitEntity.ShortPrefabName))
                    {   
                        imageUrl = profile.images[hitEntity.ShortPrefabName];
                    }
                    else if (config.images.ContainsKey(hitEntity.ShortPrefabName))
                    {
                        imageUrl = config.images[hitEntity.ShortPrefabName];
                    }


                    if (ImageLibrary != null && imageUrl != null && (bool)ImageLibrary.Call("HasImage", imageUrl) && profile.enableImages)
                    {
                        Create.Panel(ref c, "hp_imagepanel", "hp_background", profile.iconPanelColor, profile.iconPanelAnchorMin, profile.iconPanelAnchorMax, false, 0.0f, 0.0f, "", "", "", "");
                        Create.Image(ref c, "hp_image", "hp_imagepanel", (string)ImageLibrary.Call("GetImage", imageUrl), "0 0", "1 1", 0.0f, 0f, "", "");
                    }

                    Create.Text(ref c, "notifications_countdown", "Overlay", "1 1 1 1", "", 12, "0 0", "0 0", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");

                    CuiHelper.DestroyUi(attacker, "notifications_countdown");
                    CuiHelper.DestroyUi(attacker, "hp_background");
                    CuiHelper.AddUi(attacker, c);

                }
                catch
                {
                }

            });
        }

        private bool IsNpcEntity(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return false;

            // Many "human NPCs" are BasePlayer with IsNpc=true (scientists, etc.)
            if (entity is BasePlayer bp) return bp.IsNpc;

            // Animals and other NPC types derive from BaseNpc.
            if (entity is BaseNpc) return true;

            return false;
        }

        #endregion


        #region UI

        string _uiNpcBar;

        void UiNpcBar()
        {
            var c = new CuiElementContainer(); 
                    
            Create.Panel(ref c, "baseback","Overlay", "0.70 0.67 0.65 0.15", "0 0", "1 1", true, 0f, 0f, "assets/content/ui/uibackgroundblur.mat", "", "0 0", "0 0");
            Create.Panel(ref c, "baseoffset", "baseback", "0 0 0 0", "0.5 0.5", "0.5 0.5", true, 0f, 0f, "", "", "-800 -400", "800 400");
            Create.Button(ref c, "closeall", "baseoffset", "0 0 0 0", "", 20, "0 0", "1 1", "", "baseback", "", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Panel(ref c, "npcpanel", "baseoffset", "0.18 0.18 0.18 1", "0.43 0.4", "0.57 0.68", true, 0f, 0f, "", "", "0 0", "0 0");
            
            Create.Button(ref c, "editbutton", "npcpanel", "0.23 0.46 0.64 1", "Edit", 13, "0.02 0.90", "0.25 0.983", "editpanel", "npcpanel", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Text(ref c, "optionstext", "npcpanel", "1 1 1 1", "Options", 15, "0.2 0.90", "0.8 0.985", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            Create.Button(ref c, "closebutton", "npcpanel", "0.8 0.1 0.1 1", "X", 13, "0.87 0.90", "0.98 0.983", "", "baseback", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");

            Create.Panel(ref c, "innerpanel", "npcpanel", "0.28 0.28 0.28 1", "0.025 0.025", "0.975 0.875", true, 0f, 0f, "", "", "0 0", "0 0");
            Create.Button(ref c, "profile1btn", "innerpanel", "0.18 0.18 0.18 1", "  Profile 1", 14, "0.02 0.80", "0.98 0.98", "profile.panel.open 1", "", "1 1 1 1", 1f, TextAnchor.MiddleLeft, "", "");
            Create.Panel(ref c, "profile1indicator", "profile1btn", "0.2 0.8 0.2 1", "0.85 0.15", "0.97 0.85", false, 0f, 0f, "", "", "", "");
            Create.Text(ref c, "profile1check", "profile1indicator", "1 1 1 1", "✓", 16, "0 0", "1 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            
            
            Create.Button(ref c, "profile2btn", "innerpanel", "0.18 0.18 0.18 1", "  Profile 2", 14, "0.02 0.605", "0.98 0.785", "profile.panel.open 2", "", "1 1 1 1", 1f, TextAnchor.MiddleLeft, "", "");
            Create.Panel(ref c, "profile2indicator", "profile2btn", "0.2 0.8 0.2 1", "0.85 0.15", "0.97 0.85", false, 0f, 0f, "", "", "", "");
            Create.Text(ref c, "profile2check", "profile2indicator", "1 1 1 1", "✓", 16, "0 0", "1 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            
            Create.Button(ref c, "profile3btn", "innerpanel", "0.18 0.18 0.18 1", "  Profile 3", 14, "0.02 0.41", "0.98 0.59", "profile.panel.open 3", "", "1 1 1 1", 1f, TextAnchor.MiddleLeft, "", "");
            Create.Panel(ref c, "profile3indicator", "profile3btn", "0.2 0.8 0.2 1", "0.85 0.15", "0.97 0.85", false, 0f, 0f, "", "", "", "");
            Create.Text(ref c, "profile3check", "profile3indicator", "1 1 1 1", "✓", 16, "0 0", "1 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            
            Create.Button(ref c, "profile4btn", "innerpanel", "0.18 0.18 0.18 1", "  Profile 4", 14, "0.02 0.215", "0.98 0.395", "profile.panel.open 4", "", "1 1 1 1", 1f, TextAnchor.MiddleLeft, "", "");
            Create.Panel(ref c, "profile4indicator", "profile4btn", "0.2 0.8 0.2 1", "0.85 0.15", "0.97 0.85", false, 0f, 0f, "", "", "", "");
            Create.Text(ref c, "profile4check", "profile4indicator", "1 1 1 1", "✓", 16, "0 0", "1 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            
            Create.Button(ref c, "profile5btn", "innerpanel", "0.18 0.18 0.18 1", "  Profile 5", 14, "0.02 0.02", "0.98 0.20", "profile.panel.open 5", "", "1 1 1 1", 1f, TextAnchor.MiddleLeft, "", "");
            Create.Panel(ref c, "profile5indicator", "profile5btn", "0.2 0.8 0.2 1", "0.85 0.15", "0.97 0.85", false, 0f, 0f, "", "", "", "");
            Create.Text(ref c, "profile5check", "profile5indicator", "1 1 1 1", "✓", 16, "0 0", "1 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");


            _uiNpcBar = c.ToString();
        }

        void OpenMainUi(BasePlayer player) 
        {
            CuiHelper.DestroyUi(player, "baseback");

            CuiHelper.AddUi(player, _uiNpcBar);
            UpdateProfileIndicators(player);
            
            if (player.IsAdmin)
            {
                AdminPanel(player);
            }
        }

        void ProfilePanel(BasePlayer player, int profileNumber, string profileImage) 
        {
            CuiHelper.DestroyUi(player, "profilepanel");
            var c = new CuiElementContainer();

            Create.Panel(ref c, "profilepanel", "baseoffset", "0.18 0.18 0.18 1", "0.575 0.4", "0.7 0.6", true, 0f, 0f, "", "", "0 0", "0 0");

            if(profileNumber == 5)
            {
                Create.Text(ref c, "profiletext", "profilepanel", "1 1 1 1", $"Profile {profileNumber}  -  shows % of HP left", 12, "0 0.87", "1 0.99", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            }
            else
            {
                Create.Text(ref c, "profiletext", "profilepanel", "1 1 1 1", $"Profile {profileNumber}", 12, "0 0.87", "1 0.99", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            }
            Create.Panel(ref c, "profileinnerpanel", "profilepanel", "0.28 0.28 0.28 1", "0.02 0.2", "0.98 0.86", true, 0f, 0f, "", "", "0 0", "0 0");
            Create.Image(ref c, "profileimage", "profileinnerpanel", $"{profileImage}", "0 0", "1 1", 0f, 0f, "0 0", "0 0");
            Create.Button(ref c, "setactivebutton", "profilepanel", "0.23 0.46 0.64 1", "Set as active", 12, "0.25 0.02", "0.75 0.18", $"profile.button.setactive {profileNumber}", "profilepanel", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");

            CuiHelper.AddUi(player, c);
        }

        void AdminPanel(BasePlayer player)
        {
            if (player == null || !player.IsAdmin) return;

            CuiHelper.DestroyUi(player, "adminpanel");
            var c = new CuiElementContainer();

            Create.Panel(ref c, "adminpanel", "npcpanel", "0.18 0.18 0.18 1", "0 -0.68", "1 -0.025", true, 0f, 0f, "", "", "0 0", "0 0");
            Create.Panel(ref c, "adminpanelline", "adminpanel", "0.28 0.28 0.28 1", "0.025 0.805", "0.975 0.820", true, 0f, 0f, "", "", "0 0", "0 0");
            Create.Text(ref c, "admintext", "adminpanel", "1 1 1 1", "Admin panel", 13, "0.2 0.83", "0.8 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            
            Create.Text(ref c, "timetodestroy_text", "adminpanel", "1 1 1 1", $"Time to destroy bar (in seconds):    {config.timeToDestroy}", 12, "0.06 0.65", "0.9 0.8", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 1");
            Create.Panel(ref c, "timetodestroy_inputpanel", "adminpanel", "0.28 0.28 0.28 1", "0.5 0.45", "0.75 0.6", true, 0f, 0f, "", "", "0 0", "0 0");
            Create.Input(ref c, "timetodestroy_input", "timetodestroy_inputpanel", "1 1 1 1", 12, "0.05 0", "1 1", "", "robotocondensed-regular.ttf", "inputfieldcmd_npcbar", TextAnchor.MiddleLeft, 6, "0 0", "0 0");
            Create.Button(ref c, "timetodestroy_button", "adminpanel", "0.23 0.46 0.64 1", "Set", 12, "0.76 0.45", "0.975 0.6", "editcnfgcmd_npcbar timetodestroy", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            
            Create.Text(ref c, "requireperm_text", "adminpanel", "1 1 1 1", "Require Permissions YES / NO", 12, "0.06 0.25", "0.94 0.40", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 1");
            Create.Button(ref c, "requireperm_button", "adminpanel", "0.23 0.46 0.64 1", config.requirePerm ? "YES" : "NO", 12, "0.5 0.05", "0.975 0.2", "requirepermcnfgcmd", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");

            CuiHelper.AddUi(player, c);
        }

        void EditPanel(BasePlayer player)
        {
            if (editOffsets.ContainsKey(player.userID))
            {
                editOffsets[player.userID] = new EditOffsetData();
            }
            else
            {
                editOffsets[player.userID] = new EditOffsetData();
            }

            CuiHelper.DestroyUi(player, "editpanel");
            CuiHelper.DestroyUi(player, "closeall");
            var c = new CuiElementContainer();
            
            Create.Panel(ref c, "editpanel", "baseoffset", "0.18 0.18 0.18 1", "0.13 0.6", "0.22 0.9", true, 0f, 0f, "", "", "0 0", "0 0"); 
            Create.Panel(ref c, "edit_header_panel", "editpanel", "0.28 0.28 0.28 1", "0.02 0.89", "0.98 0.983", true, 0f, 0f, "", "", "0 0", "0 0"); 
            Create.Button(ref c, "close_edit_button", "editpanel", "0.8 0.1 0.1 1", "X", 13, "0.84 0.89", "0.98 0.983", "close_panels", "baseback", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Text(ref c, "edit_header_text", "edit_header_panel", "1 1 1 1", "Edit mode", 13, "0 0", "1 1", TextAnchor.MiddleCenter, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");
            
            Create.Text(ref c, "move_text", "editpanel", "1 1 1 1", "Move:", 13, "0.05 0.78", "0.37 0.88", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");

            Create.Button(ref c, "move_left_button", "editpanel", "0.23 0.46 0.64 1", "", 12, "0.17 0.58", "0.37 0.72", "move_command left", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Image(ref c, "btnleft_img", "move_left_button", "https://i.postimg.cc/1zMwtMhM/arrow-left-bold-outline-custom.png", "0.05 0.05", "0.95 0.95", 0f, 0f, "0 0", "0 0");

            Create.Button(ref c, "move_up_button", "editpanel", "0.23 0.46 0.64 1", "", 12, "0.4 0.655", "0.6 0.78", "move_command up", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Image(ref c, "btnupimg", "move_up_button", "https://i.postimg.cc/bwpRwZSD/arrow-up-bold-outline-custom.png", "0.05 0.05", "0.95 0.95", 0f, 0f, "0 0", "0 0");

            Create.Button(ref c, "move_down_button", "editpanel", "0.23 0.46 0.64 1", "", 12, "0.4 0.52", "0.6 0.645", "move_command down", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Image(ref c, "btndownimg", "move_down_button", "https://i.postimg.cc/vmmL5CvN/arrow-down-bold-outline-custom.png", "0.05 0.05", "0.95 0.95", 0f, 0f, "0 0", "0 0");

            Create.Button(ref c, "move_right_button", "editpanel", "0.23 0.46 0.64 1", "", 12, "0.63 0.58", "0.83 0.72", "move_command right", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Image(ref c, "btnrightimg", "move_right_button", "https://i.postimg.cc/8Cnd4wKC/arrow-right-bold-outline-custom.png", "0.05 0.05", "0.95 0.95", 0f, 0f, "0 0", "0 0");

            Create.Text(ref c, "resize_text", "editpanel", "1 1 1 1", "Resize:", 14, "0.05 0.4", "0.5 0.5", TextAnchor.MiddleLeft, "robotocondensed-regular.ttf", 0f, 0f, "0 0 0 0", "0 0");

            Create.Button(ref c, "resize_decrease_button", "editpanel", "0.16 0.44 0.47 1", "-", 17, "0.3 0.25", "0.485 0.37", "resize_command decrease", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Button(ref c, "resize_increase_button", "editpanel", "0.16 0.44 0.47 1", "+", 17, "0.515 0.25", "0.7 0.37", "resize_command increase", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");


            Create.Button(ref c, "reset_panel_button", "editpanel", "0.47 0.16 0.16 1", "Reset", 12, "0.025 0.102", "0.3 0.175", "edit_reset_command", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Button(ref c, "resetdefault_panel_button", "editpanel", "0.46 0.29 0.19 1", "Reset to default", 12, "0.325 0.102", "0.975 0.175", "edit_resetdefault_command", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");
            Create.Button(ref c, "save_panel_button", "editpanel", "0.25 0.46 0.19 1", "Save", 12, "0.025 0.015", "0.975 0.09", "edit_save_command", "", "1 1 1 1", 1f, TextAnchor.MiddleCenter, "", "");

            CuiHelper.AddUi(player, c);

            ExampleBar(player);
        }

        #endregion



        #region Commands

        [ChatCommand("npcbar")]
        private void npcbar(BasePlayer player)  
        {
            if(config.adminOnly)
            {
                if(!player.IsAdmin)
                {
                    player.ChatMessage("You are not authorized to use this command!");
                    return;
                }
            }

            CuiHelper.DestroyUi(player, "baseback");
            OpenMainUi(player);
        }

        [ConsoleCommand("profile.panel.open")] 
        private void ProfilePanelOpen(ConsoleSystem.Arg arg) 
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            if(args[0] == "1")
            {
                ProfilePanel(player, 1, "https://i.postimg.cc/pTBWCMZs/profile1.png");
            }

            else if(args[0] == "2")
            {
                ProfilePanel(player, 2, "https://i.postimg.cc/bYC8wTFn/profile2.png");
            }

            else if(args[0] == "3")
            {
                ProfilePanel(player, 3, "https://i.postimg.cc/8CSg0njV/profile3.png");
            }

            else if(args[0] == "4")
            {
                ProfilePanel(player, 4, "https://i.postimg.cc/NFL3H3PB/profile4.png");
            }

            else if(args[0] == "5")
            {
                ProfilePanel(player, 5, "https://i.postimg.cc/dtfkwpPf/profile5.png");
            }
        }

        [ConsoleCommand("profile.button.setactive")] 
        private void ProfileButtonSetActive(ConsoleSystem.Arg arg) 
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (args.Length < 1) return;

            int profileNumber;
            if (!int.TryParse(args[0], out profileNumber) || profileNumber < 1 || profileNumber > 5)
            {
                return;
            }

            if (!profileData.ContainsKey(profileNumber))
            {
                player.ChatMessage($"Profile {profileNumber} not found!");
                return;
            }

            if (config != null && config.adminOnly && player.IsAdmin && profileData.ContainsKey(profileNumber))
            {
                CopyProfileDataToDefaultProfile(profileData[profileNumber]);
                if (config.defaultprofile != null)
                {
                    config.defaultprofile.defaultProfileNumber = profileNumber;
                }
            SaveConfig();
        }

            if (editOffsets.ContainsKey(player.userID))
            {
                editOffsets[player.userID] = new EditOffsetData();
            }

            if (!playerData.ContainsKey(player.userID))
            {
                playerData.Add(player.userID, new PlayerData 
                { 
                    profileNumber = profileNumber,
                    panelbgAnchorMin = "",
                    panelbgAnchorMax = "",
                    hasExplicitlyChosenProfile = true
                });
            }
            else
            {
                playerData[player.userID].profileNumber = profileNumber;
                playerData[player.userID].panelbgAnchorMin = "";
                playerData[player.userID].panelbgAnchorMax = "";
                playerData[player.userID].hasExplicitlyChosenProfile = true;
            }
            SavePlayerData();

            UpdateProfileIndicators(player);
            player.ChatMessage($"Profile {profileNumber} activated!");
        }

        [ConsoleCommand("editpanel")]
        private void EditPanelCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            CuiHelper.DestroyUi(player, "editpanel");
            CuiHelper.DestroyUi(player, "profilepanel");

            EditPanel(player);
        }

        [ConsoleCommand("close_panels")]
        private void ClosePanelsCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (editOffsets.ContainsKey(player.userID))
            {
                editOffsets[player.userID] = new EditOffsetData();
            }

            CuiHelper.DestroyUi(player, "editpanel");
            CuiHelper.DestroyUi(player, "hp_background_example");

            CuiHelper.DestroyUi(player, "baseback");
            OpenMainUi(player);
        }

        [ConsoleCommand("move_command")]
        private void MoveCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null || args.Length < 1) return;

            // Initialize offset data if not exists
            if (!editOffsets.ContainsKey(player.userID))
            {
                editOffsets[player.userID] = new EditOffsetData();
            }

            var offsetData = editOffsets[player.userID];

            if (args[0] == "left")
            {
                offsetData.leftCount++;
            }
            else if (args[0] == "right")
            {
                offsetData.rightCount++;
            }
            else if (args[0] == "up")
            {
                offsetData.upCount++;
            }
            else if (args[0] == "down")
            {
                offsetData.downCount++;
            }

            ExampleBar(player);
        }

        [ConsoleCommand("resize_command")]
        private void ResizeCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null || args.Length < 1) return;

            // Initialize offset data if not exists
            if (!editOffsets.ContainsKey(player.userID))
            {
                editOffsets[player.userID] = new EditOffsetData();
            }

            var offsetData = editOffsets[player.userID];

            // Increment appropriate counter
            if (args[0] == "increase")
            {
                offsetData.increaseCount++;
            }
            else if (args[0] == "decrease")
            {
                offsetData.decreaseCount++;
            }

            ExampleBar(player);
        }

        string ModifyAnchorString(string anchorStr, float xOffset, float yOffset)
        {
            if (string.IsNullOrEmpty(anchorStr)) return anchorStr;
            
            string[] parts = anchorStr.Split(' ');
            if (parts.Length == 1)
            {
                // Only X value (e.g., "1" or "0.6")
                float x = 0f;
                if (float.TryParse(parts[0], out x))
                {
                    return $"{Math.Max(0f, Math.Min(1f, x + xOffset))}";
                }
            }
            else if (parts.Length == 2)
            {
                float x = 0f, y = 0f;
                if (float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y))
                {
                    return $"{Math.Max(0f, Math.Min(1f, x + xOffset))} {Math.Max(0f, Math.Min(1f, y + yOffset))}";
                }
            }
            
            return anchorStr;
        }

        [ConsoleCommand("edit_save_command")]
        private void EditSaveCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            GetPlayerAnchorValues(player, out string panelbgAnchorMin, out string panelbgAnchorMax,
                out string overlineAnchorMin, out string overlineAnchorMax,
                out string panelHpAnchorMin, out string panelHpAnchorMax,
                out string statusTextAnchorMin, out string statusTextAnchorMax,
                out string nameTextAnchorMin, out string nameTextAnchorMax,
                out string iconPanelAnchorMin, out string iconPanelAnchorMax);

            if (editOffsets.ContainsKey(player.userID))
            {
                var offsetData = editOffsets[player.userID];
                
                float moveXOffset = (offsetData.rightCount - offsetData.leftCount) * 0.020f;
                float moveYOffset = (offsetData.upCount - offsetData.downCount) * 0.020f;

                panelbgAnchorMin = ModifyAnchorString(panelbgAnchorMin, moveXOffset, moveYOffset);
                
                int effectiveProfileNum = GetEffectiveProfileNumber(player);
                var (horizontalMultiplier, verticalMultiplier) = GetResizeProportions(effectiveProfileNum);
                float baseResizeOffset = (offsetData.increaseCount - offsetData.decreaseCount) * 0.015f;
                float resizeXOffset = baseResizeOffset * horizontalMultiplier;
                float resizeYOffset = baseResizeOffset * verticalMultiplier;
                panelbgAnchorMax = ModifyAnchorString(panelbgAnchorMax, moveXOffset + resizeXOffset, moveYOffset + resizeYOffset);
            }
            
            if (config != null && config.adminOnly && player.IsAdmin)
            {
                if (config.defaultprofile == null)
                {
                    config.defaultprofile = new Configuration.DefaultProfileData();
                }
                
                config.defaultprofile.panelbgAnchorMin = panelbgAnchorMin;
                config.defaultprofile.panelbgAnchorMax = panelbgAnchorMax;
                
                SaveConfig();
            }
            else
            {
                if (!playerData.ContainsKey(player.userID))
                {
                    playerData[player.userID] = new PlayerData 
                    { 
                        profileNumber = 1,
                        panelbgAnchorMin = "",
                        panelbgAnchorMax = "",
                        hasExplicitlyChosenProfile = false
                    };
                }

                var pData = playerData[player.userID];
                pData.panelbgAnchorMin = panelbgAnchorMin;
                pData.panelbgAnchorMax = panelbgAnchorMax;

                SavePlayerData();
            }
            
            if (editOffsets.ContainsKey(player.userID))
            {
                editOffsets[player.userID] = new EditOffsetData();
            }

            ExampleBar(player);
            
            player.ChatMessage("Anchor positions saved!");
        }

        [ConsoleCommand("edit_reset_command")]
        private void EditResetCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            if (arg.Player() == null) return;

            editOffsets[player.userID] = new EditOffsetData();

            ExampleBar(player);
        }

        [ConsoleCommand("edit_resetdefault_command")]
        private void EditResetDefaultCommand(ConsoleSystem.Arg arg)  
        {
            var player = arg?.Player();
            if (arg.Player() == null) return;

            editOffsets[player.userID] = new EditOffsetData();

            if (config != null && config.adminOnly && player.IsAdmin)
            {
                if (config.defaultprofile != null)
                {
                    int defaultProfileNum = config.defaultprofile.defaultProfileNumber;
                    
                    if (defaultProfileNum >= 1 && defaultProfileNum <= 5 && profileData.ContainsKey(defaultProfileNum))
                    {
                        var originalProfile = profileData[defaultProfileNum];
                        
                        config.defaultprofile.panelbgAnchorMin = originalProfile.panelbgAnchorMin;
                        config.defaultprofile.panelbgAnchorMax = originalProfile.panelbgAnchorMax;
                        
                        SaveConfig();
                    }
                }
            }
            else
            {
                if (!playerData.ContainsKey(player.userID))
                {
                    playerData[player.userID] = new PlayerData 
                    { 
                        profileNumber = 1,
                        panelbgAnchorMin = "",
                        panelbgAnchorMax = "",
                        hasExplicitlyChosenProfile = false
                    };
                }

                var pData = playerData[player.userID];
                pData.panelbgAnchorMin = "";
                pData.panelbgAnchorMax = "";

                SavePlayerData();
            }

            ExampleBar(player);
        }

        #endregion




        #region Admin Commands

        private List<int> _cnfginput = new List<int>();

        [ConsoleCommand("inputfieldcmd_npcbar")]
        private void InputFieldCmdNpcBar(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (args.Count() == 0)
            {
                return;
            }

            int inputtext;
            bool canConvert = int.TryParse(args[0], out int result);
            if (canConvert == false)
            {
                _cnfginput.Clear();
                return;
            }
            else
            {
                inputtext = int.Parse(args[0]);
            }

            if (!_cnfginput.Any())
            {
                _cnfginput.Add(inputtext);
            }
            else
            {
                _cnfginput.Clear();
                _cnfginput.Add(inputtext);
            }
        }

        [ConsoleCommand("editcnfgcmd_npcbar")]
        private void EditConfigCommandNpcBar(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (!player.IsAdmin)
            {
                return;
            }

            if (!_cnfginput.Any())
            {
                return;
            }

            if (args[0] == "timetodestroy")
            {
                config.timeToDestroy = _cnfginput[0];
                SaveConfig();
                _cnfginput.Clear();
            }

            AdminUpdate(player);
        }

        [ConsoleCommand("requirepermcnfgcmd")]
        private void RequirePermConfigCommand(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (arg.Player() == null) return;

            if (!player.IsAdmin)
            {
                return;
            }

            if (config.requirePerm)
            {
                config.requirePerm = false;
                SaveConfig();
            }
            else
            {
                config.requirePerm = true;
                SaveConfig();
            }

            AdminPanel(player);
        }

        void AdminUpdate(BasePlayer player)
        {
            if (player == null || config == null) return;

            UpdateUI(player, "text", "timetodestroy_text", $"Time to destroy bar (in seconds):    {config.timeToDestroy}");
        }

        #endregion

        #region Functions

        void ExampleBar(BasePlayer player)
        {
            if (player == null) return;

            var profile = GetPlayerProfile(player);
            if (profile == null) return;

            int effectiveProfileNum = GetEffectiveProfileNumber(player);

            GetPlayerAnchorValues(player, out string panelbgAnchorMin, out string panelbgAnchorMax,
                out string overlineAnchorMin, out string overlineAnchorMax,
                out string panelHpAnchorMin, out string panelHpAnchorMax,
                out string statusTextAnchorMin, out string statusTextAnchorMax,
                out string nameTextAnchorMin, out string nameTextAnchorMax,
                out string iconPanelAnchorMin, out string iconPanelAnchorMax);

            if (editOffsets.ContainsKey(player.userID))
            {
                var offsetData = editOffsets[player.userID];
                
                float moveXOffset = (offsetData.rightCount - offsetData.leftCount) * 0.020f;
                float moveYOffset = (offsetData.upCount - offsetData.downCount) * 0.020f;

                panelbgAnchorMin = ModifyAnchorString(panelbgAnchorMin, moveXOffset, moveYOffset);
                
                var (horizontalMultiplier, verticalMultiplier) = GetResizeProportions(effectiveProfileNum);
                float baseResizeOffset = (offsetData.increaseCount - offsetData.decreaseCount) * 0.015f;
                float resizeXOffset = baseResizeOffset * horizontalMultiplier;
                float resizeYOffset = baseResizeOffset * verticalMultiplier;
                panelbgAnchorMax = ModifyAnchorString(panelbgAnchorMax, moveXOffset + resizeXOffset, moveYOffset + resizeYOffset);
            }

            float exampleHealth = 75f;
            float exampleMaxHealth = 100f;
            float onePercent = exampleMaxHealth / 100;
            float leftPercent = exampleHealth / onePercent;

            var c = new CuiElementContainer();

            Create.Panel(ref c, "hp_background_example", "Overlay", profile.panelbgColor, panelbgAnchorMin, panelbgAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

            Create.Panel(ref c, "bar_overline_example", "hp_background_example", profile.overlineColor, overlineAnchorMin, overlineAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

            string progressAnchorMax = $"{leftPercent / 100} {panelHpAnchorMax}";
            Create.Panel(ref c, "hp_progress_example", "bar_overline_example", profile.panelHpColor, panelHpAnchorMin, progressAnchorMax, false, 0.0f, 0.0f, "", "", "", "");

            string statusText;
            string statusFont;
            string statusColor;
            int statusFontSize;
            if (effectiveProfileNum == 5)
            {
                int roundedPercent = (int)Math.Ceiling(leftPercent);
                statusText = $"{roundedPercent}";
                statusFont = "permanentmarker.ttf";
                
                if (roundedPercent > 75)
                {
                    statusColor = "1 1 1 1";
                    statusFontSize = profile.statusTextFontsize;
                }
                else if (roundedPercent > 50)
                {
                    statusColor = "1 1 0 1";
                    statusFontSize = profile.statusTextFontsize + 2;
                }
                else if (roundedPercent > 25)
                {
                    statusColor = "1 0.5 0 1";
                    statusFontSize = profile.statusTextFontsize + 4;
                }
                else
                {
                    statusColor = "1 0 0 1";
                    statusFontSize = profile.statusTextFontsize + 6;
                }
            }
            else
            {
                statusText = profile.statusTextText.Replace("{currenthp}", $"{(int)exampleHealth}").Replace("{maxhp}", $"{(int)exampleMaxHealth}");
                statusFont = "robotocondensed-regular.ttf";
                statusColor = profile.statusTextColor;
                statusFontSize = profile.statusTextFontsize;
            }
            Create.Text(ref c, "hp_statustext_example", "hp_background_example", statusColor, statusText, statusFontSize, statusTextAnchorMin, statusTextAnchorMax, profile.statusTextAlign, statusFont, 0.0f, 0.0f, "0 0 0 0", "0 0");

            string bearName = "Bear";
            if (config != null && config.names != null && config.names.ContainsKey("bear"))
            {
                bearName = config.names["bear"];
            }
            Create.Text(ref c, "hp_name_example", "hp_background_example", profile.nameTextColor, profile.nameTextText.Replace("{name}", bearName), 12, nameTextAnchorMin, nameTextAnchorMax, profile.nameTextAlign, "robotocondensed-regular.ttf", 0.0f, 0.0f, "0 0 0 0", "0 0");

            string imageUrl = null;
            if (profile.images != null && profile.images.ContainsKey("bear"))
            {
                imageUrl = profile.images["bear"];
            }
            else if (config != null && config.images != null && config.images.ContainsKey("bear"))
            {
                imageUrl = config.images["bear"];
            }

            if (ImageLibrary != null && imageUrl != null && (bool)ImageLibrary.Call("HasImage", imageUrl) && profile.enableImages)
            {
                Create.Panel(ref c, "hp_imagepanel_example", "hp_background_example", profile.iconPanelColor, iconPanelAnchorMin, iconPanelAnchorMax, false, 0.0f, 0.0f, "", "", "", "");
                Create.Image(ref c, "hp_image_example", "hp_imagepanel_example", (string)ImageLibrary.Call("GetImage", imageUrl), "0 0", "1 1", 0.0f, 0f, "", "");
            }

            CuiHelper.DestroyUi(player, "hp_background_example");
            CuiHelper.AddUi(player, c);
        }

        #endregion

        #region Config

        private Configuration config;
        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = Configuration.CreateConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        class Configuration
        {
            [JsonProperty("Health bar editing only for admins")]
            public bool adminOnly { get; set; }

            [JsonProperty("Use default profile as first choice for new players unless they change it later")]
            public bool useDefaultProfile { get; set; }

            [JsonProperty("Time to destroy Health Bar after last attack")]
            public int timeToDestroy { get; set; }

            [JsonProperty("Require permission?")]
            public bool requirePerm { get; set; }

            [JsonProperty("Blacklist")]
            public List<string> blacklist { get; set; }

            [JsonProperty("NPC Names")]
            public Dictionary<string, string> names { get; set; }

            [JsonProperty("Images")]
            public Dictionary<string, string> images { get; set; }

            [JsonProperty("Default Profile")]
            public DefaultProfileData defaultprofile { get; set; }

            public class DefaultProfileData
            {
                public int defaultProfileNumber;
                public bool enableImages;
                public string panelbgAnchorMin;
                public string panelbgAnchorMax;
                public string panelbgColor;
                public string overlineAnchorMin;
                public string overlineAnchorMax;
                public string overlineColor;
                public string panelHpAnchorMin;
                public string panelHpAnchorMax;
                public string panelHpColor;
                public string statusTextText;
                public int statusTextFontsize;
                public string statusTextAnchorMin;
                public string statusTextAnchorMax;
                public string statusTextColor;
                public TextAnchor statusTextAlign;
                public string nameTextText;
                public string nameTextAnchorMin;
                public string nameTextAnchorMax;
                public string nameTextColor;
                public TextAnchor nameTextAlign;
                public string iconPanelAnchorMin;
                public string iconPanelAnchorMax;
                public string iconPanelColor;
                public Dictionary<string, string> images;
            }

            public static Configuration CreateConfig()
            {
                return new Configuration
                {   
                    adminOnly = false,
                    useDefaultProfile = false,
                    timeToDestroy = 4,
                    requirePerm = false,
                    blacklist = new List<string>
                    {
                        "empty"
                    },
                    names = new Dictionary<string, string>{
                       { "scientistnpc_roam", "Roamer" },
                       { "scientistnpc_peacekeeper", "Peacekeeper"},
                       { "scientistnpc_oilrig", "Scientist" },
                       { "scientistnpc_patrol", "Patrol" },
                       { "scientistnpc_heavy", "Buldozer" },
                       { "scientistnpc_roam_nvg_variant", "Roamer" },
                       { "scientistnpc_roamtethered", "Roamer" },
                       { "scientistnpc_junkpile_pistol", "Pistolier" },
                       { "scientistnpc_full_shotgun", "Shotgunner" },
                       { "scientistnpc_full_pistol", "Pistolier" },
                       { "scientistnpc_full_mp5", "Scientist" },
                       { "scientistnpc_full_lr300", "Scientist" },
                       { "scientistnpc_full_any", "Scientist" },
                       { "scientistnpc_excavator", "Excavator" },
                       { "scientistnpc_ch47_gunner", "Gunner" },
                       { "scientistnpc_cargo_turret_lr300", "Gunner" },
                       { "scientistnpc_cargo_turret_any", "Gunner" },
                       { "scientistnpc_cargo", "Scientist" },
                       { "scientistnpc_arena", "Scientist" },
                       { "bradleyapc", "Bradley APC" },
                       { "patrolhelicopter", "Patrol Helicopter" },

                       { "boar", "Hog"},
                       { "scarecrow", "Scarecrow"},
                       { "polarbear", "Polarbear"},
                       { "wolf", "Direwolf"},
                       { "bear", "Grizzly"}
                    },
                    images = new Dictionary<string, string>{
                       { "scientistnpc_roam", "https://i.postimg.cc/t4wDtsGT/4tyicon.png" },
                       { "scientistnpc_peacekeeper", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_oilrig", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_patrol", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_heavy", "https://i.postimg.cc/QxNYc2H3/Humanoidelite-L.png" },
                       { "scientistnpc_roam_nvg_variant", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_roamtethered", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_junkpile_pistol", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_full_shotgun", "https://i.postimg.cc/QxNYc2H3/Humanoidelite-L.png" },
                       { "scientistnpc_full_pistol", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_full_mp5", "https://i.postimg.cc/QxNYc2H3/Humanoidelite-L.png" },
                       { "scientistnpc_full_lr300", "https://i.postimg.cc/QxNYc2H3/Humanoidelite-L.pngt" },
                       { "scientistnpc_full_any", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_excavator", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_ch47_gunner", "https://i.postimg.cc/QxNYc2H3/Humanoidelite-L.png" },
                       { "scientistnpc_cargo_turret_lr300", "https://i.postimg.cc/QxNYc2H3/Humanoidelite-L.png" },
                       { "scientistnpc_cargo_turret_any", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_cargo", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "scientistnpc_arena", "https://i.postimg.cc/kXwZZnkG/Humanoid-L.png" },
                       { "bradleyapc", "https://i.postimg.cc/DzycBfQz/bradley-img-new-jp.jpg" },
                       { "patrolhelicopter", "https://i.postimg.cc/50XSWPkh/heli-pic-new-jp.jpg" },


                       { "bear", "https://i.postimg.cc/wjXtHHqX/BearL.png" },
                       { "boar", "https://i.postimg.cc/Cxws8cQV/BoarL.png" },
                       { "horse", "https://i.postimg.cc/d0FqBQd1/HorseL.png" },
                       { "wolf", "https://i.postimg.cc/VkfwrZcr/WolfL.png" },
                       { "zombie", "https://i.postimg.cc/tTSYTHhM/ZombieL.png" },
                       { "polarbear", "https://i.postimg.cc/Qdks4dmT/Polarbear-L.png" }
                    },
                    defaultprofile = new DefaultProfileData
                    {
                        defaultProfileNumber = 1,
                        enableImages = false,
                        panelbgAnchorMin = "",
                        panelbgAnchorMax = "",
                        panelbgColor = "",
                        overlineAnchorMin = "",
                        overlineAnchorMax = "",
                        overlineColor = "",
                        panelHpAnchorMin = "",
                        panelHpAnchorMax = "",
                        panelHpColor = "",
                        statusTextText = "",
                        statusTextFontsize = 0,
                        statusTextAnchorMin = "",
                        statusTextAnchorMax = "",
                        statusTextColor = "",
                        statusTextAlign = TextAnchor.MiddleCenter,
                        nameTextText = "",
                        nameTextAnchorMin = "",
                        nameTextAnchorMax = "",
                        nameTextColor = "",
                        nameTextAlign = TextAnchor.MiddleCenter,
                        iconPanelAnchorMin = "",
                        iconPanelAnchorMax = "",
                        iconPanelColor = "",
                        images = new Dictionary<string, string>()
                    }
                };
            }
        }
        #endregion

        #region Cui

        public class Create
        {
            public static void Panel(ref CuiElementContainer container, string name, string parent, string color, string anchorMinx, string anchorMax, bool cursorOn = false, float fade = 0f, float fadeOut = 0f, string material = "", string sprite = "", string offsetMin = "", string offsetMax = "", bool keyboard = false)
            {
                container.Add(new CuiPanel
                {

                    Image = { Color = color, Material = material, FadeIn = fade, Sprite = sprite },
                    RectTransform = { AnchorMin = anchorMinx, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                    FadeOut = 0f,
                    CursorEnabled = cursorOn,
                    KeyboardEnabled = keyboard,

                },
                parent,
                name);
            }

            public static void Image(ref CuiElementContainer container, string name, string parent, string image, string anchorMinx, string anchorMax, float fade = 0f, float fadeOut = 0f, string offsetMin = "", string offsetMax = "")
            {
                if (image.StartsWith("http") || image.StartsWith("www"))
                {
                    container.Add(new CuiElement
                    {
                        Name = name,
                        Parent = parent,
                        FadeOut = 0f,
                        Components =
                        {
                            new CuiRawImageComponent { Url = image, Sprite = "assets/content/textures/generic/fulltransparent.tga", FadeIn = fade},
                            new CuiRectTransformComponent { AnchorMin = anchorMinx, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                        } 
                    });
                }
                else
                {
                    container.Add(new CuiElement
                    {
                        Parent = parent,
                        Components =
                        {
                            new CuiRawImageComponent { /* Color = "0 0 0 1", */ Png = image, Sprite = "assets/content/textures/generic/fulltransparent.tga", FadeIn = fade},
                            new CuiRectTransformComponent { AnchorMin = anchorMinx, AnchorMax = anchorMax }
                        }
                    });
                }
            }

            public static void Text(ref CuiElementContainer container, string name, string parent, string color, string text, int size, string anchorMinx, string anchorMax, TextAnchor align = TextAnchor.MiddleCenter, string font = "robotocondensed-regular.ttf", float fade = 0f, float fadeOut = 0f, string _outlineColor = "0 0 0 0", string _outlineScale = "0 0")
            {
                container.Add(new CuiElement
                {
                    Parent = parent,
                    Name = name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = text,
                            FontSize = size,
                            Font = font,
                            Align = align,
                            Color = color,
                            FadeIn = fade,
                        },

                        new CuiOutlineComponent
                        {

                            Color = _outlineColor,
                            Distance = _outlineScale

                        },

                        new CuiRectTransformComponent
                        {
                             AnchorMin = anchorMinx,
                             AnchorMax = anchorMax
                        }
                    },
                    FadeOut = 0f
                });
            }

            public static void Button(ref CuiElementContainer container, string name, string parent, string color, string text, int size, string anchorMinx, string anchorMax, string command = "", string _close = "", string textColor = "0.843 0.816 0.78 1", float fade = 1f, TextAnchor align = TextAnchor.MiddleCenter, string offsetMin = "0 0", string offsetMax = "0 0", string font = "robotocondensed-regular.ttf", string material = "assets/content/ui/uibackgroundblur-ingamemenu.mat")
            {
                container.Add(new CuiButton
                {
                    Button = { Close = _close, Command = command, Color = color, Material = material, FadeIn = fade },
                    RectTransform = { AnchorMin = anchorMinx, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                    Text = { Text = text, FontSize = size, Align = align, Color = textColor, Font = font, FadeIn = fade }
                },
                parent,
                name);
            }

            public static void Input(ref CuiElementContainer container, string name, string parent, string color, int size, string anchorMin, string anchorMax, string defaultText, string font = "robotocondensed-regular.ttf", string command = "command.processinput", TextAnchor align = TextAnchor.MiddleCenter, int charsLimit = 200, string offsetMin = "0 0", string offsetMax = "0 0")
            {
                container.Add(new CuiElement
                {
                    Parent = parent,
                    Name = name,

                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Text = defaultText,
                            //LineType = UnityEngine.UI.InputField.LineType.MultiLineNewline,
                            LineType = UnityEngine.UI.InputField.LineType.SingleLine,
                            //NeedsKeyboard = true,
                            HudMenuInput = true,  
                            CharsLimit = charsLimit,
                            Color = color,
                            IsPassword = false,
                            Command = command,
                            Font = font,
                            FontSize = size,
                            Align = align
                        },

                        new CuiRectTransformComponent
                        {
                            AnchorMin = anchorMin,
                            AnchorMax = anchorMax,
                            OffsetMin = offsetMin,
                            OffsetMax = offsetMax
                        }
                    },
                });
            }

        }

        #endregion

        #region Update

        void UpdateUI(BasePlayer player, string type, string name, string value)
        {   
            string up = "";

            if (type == "position") 
            {   
                string anchorMin = value.Split('_')[0];
                string anchorMax = value.Split('_')[1];

                up = $@"[ {{ ""name"": ""{name}"", ""update"": true, ""components"": [ {{ ""type"":""RectTransform"", ""anchormin"": ""{anchorMin}"", ""anchormax"": ""{anchorMax}""}} ] }} ]";
            }

            if (type == "color")
            {
                up = $@"[ {{ ""name"": ""{name}"", ""update"": true, ""components"": [ {{ ""type"":""UnityEngine.UI.Image"", ""color"": ""{value}""}} ] }} ]";
            }

            if (type == "textcolor")
            {
                up = $@"[ {{ ""name"": ""{name}"", ""update"": true, ""components"": [ {{ ""type"":""UnityEngine.UI.Text"", ""color"": ""{value}""}} ] }} ]";
            }

            if (type == "text")
            {
                up = $@"[ {{ ""name"": ""{name}"", ""update"": true, ""components"": [ {{ ""type"":""UnityEngine.UI.Text"", ""text"": ""{value}""}} ] }} ]";
            }
        
            CuiHelper.AddUi(player, up);     
        }

        void UpdateProfileIndicators(BasePlayer player)
        {
            if (player == null) return;

            int activeProfile = 1; // Default to profile 1
            if (playerData != null && playerData.ContainsKey(player.userID))
            {
                activeProfile = playerData[player.userID].profileNumber;
            }

            // Hide all indicators and checkmarks first
            for (int i = 1; i <= 5; i++)
            {
                UpdateUI(player, "color", $"profile{i}indicator", "0.2 0.8 0.2 0");
                UpdateUI(player, "textcolor", $"profile{i}check", "1 1 1 0");
            }

            // Show the active profile indicator and checkmark
            if (activeProfile >= 1 && activeProfile <= 5)
            {
                UpdateUI(player, "color", $"profile{activeProfile}indicator", "0.2 0.8 0.2 1");
                UpdateUI(player, "textcolor", $"profile{activeProfile}check", "1 1 1 1");
            }
        }

        #endregion

        #region Player Data

        private Dictionary<ulong, PlayerData> playerData;

        private class EditOffsetData
        {
            public int leftCount { get; set; }
            public int rightCount { get; set; }
            public int upCount { get; set; }
            public int downCount { get; set; }
            public int increaseCount { get; set; }
            public int decreaseCount { get; set; }
        }

        private Dictionary<ulong, EditOffsetData> editOffsets = new Dictionary<ulong, EditOffsetData>();

        private class PlayerData
        {
            public int profileNumber { get; set; }
            public string panelbgAnchorMin { get; set; }
            public string panelbgAnchorMax { get; set; }
            public bool hasExplicitlyChosenProfile { get; set; }
        }

        private void SavePlayerData()
        {
            if (playerData != null)
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}/PlayerData", playerData);
        }

        private void LoadPlayerData()
        {
            if (Interface.Oxide.DataFileSystem.ExistsDatafile($"{Name}/PlayerData"))
            {
                playerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>($"{Name}/PlayerData");
            }
            else
            {
                playerData = new Dictionary<ulong, PlayerData>();
                SavePlayerData();
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (!playerData.ContainsKey(player.userID))
            {
                playerData.Add(player.userID, new PlayerData 
                { 
                    profileNumber = 1,
                    panelbgAnchorMin = "",
                    panelbgAnchorMax = "",
                    hasExplicitlyChosenProfile = false
                });
            }
            else
            {
                if (!playerData[player.userID].hasExplicitlyChosenProfile)
                {
                    var pData = playerData[player.userID];
                    if (!string.IsNullOrEmpty(pData.panelbgAnchorMin) || !string.IsNullOrEmpty(pData.panelbgAnchorMax))
                    {
                        pData.hasExplicitlyChosenProfile = true;
                    }
                }
            }
        }

        void Unload()
        {
            SavePlayerData();
        }

        #endregion
    }
}