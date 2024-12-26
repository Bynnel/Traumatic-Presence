using System.Reflection;
using System.Runtime.CompilerServices;
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Steam;
using DiscordRPC;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using static Barotrauma.CharacterHUD;
using Color = Microsoft.Xna.Framework.Color;

[assembly: IgnoresAccessChecksTo("Barotrauma")]
[assembly: IgnoresAccessChecksTo("BarotraumaCore")]
[assembly: IgnoresAccessChecksTo("DedicatedServer")]

namespace TraumaticPresence;

public partial class Plugin : IAssemblyPlugin
{
    public static DiscordRpcClient RpcClient;
    private static RichPresence _discordPresenceObject;
    private static Party _discordPartyObject;

    public static Lobby _SteamP2PLobbyObject; //Only applicable to P2P servers. 

    /*
     *  TODO:General long-term list of things that should be addressed:
     *      Find a workaround for the main menu (That's probably impossible though)
     *      Replace LuaCS hooks with harmony postfixes for longevity..?
     */
    /// <summary>
    ///     The entry point.
    /// </summary>
    public void InitClient()
    {
        RpcClient = new DiscordRpcClient("1274111447323906088");
        RpcClient.SkipIdenticalPresence =
            true; //idk if this is true or false by default so i'll just set it to true here.
        //Debugconsole RPC logger. Not as useful as I thought it would be.
        /*
        var Dbg = new Dbg
        {
            Level = LogLevel.Trace,
            Coloured = false
        };*/
        //RpcClient.Logger = Dbg;

        RpcClient.Initialize();
        Getters.SessionStartTime = DateTime.UtcNow;
        SetBaseRPC();
        InitEventSubscriptions();
        InitLuaHooks();
        DebugConsole.NewMessage("Traumatic Presence has been loaded", Color.DodgerBlue);
    }

    /// <summary>
    ///     Initializes LuaCS hooks
    /// </summary>
    public static void InitLuaHooks()
    {
        GameMain.LuaCs.Hook.Add("roundEnd", "rpcRoundEnded", args =>
        {
            // Band-aid sub editor fix #2. Now setting the whole status here.
            if (Getters.Biome() == string.Empty) 
            {
                _discordPresenceObject.Details = string.Empty;
                _discordPresenceObject.State = TextManager.Get("traumaticpresence.subeditorstatus")
                    .Fallback("subeditorbutton").ToString();
                _discordPresenceObject.Assets.LargeImageKey = "subeditor";
                _discordPresenceObject.Assets.LargeImageText = string.Empty;
                UpdateRichPresence();
                return null;
            }
            int casualtyCount = GameMain.gameSession.Casualties.Count();
            //Based off RoundSummary.cs
            LocalizedString subName = string.Empty;
            if (GameMain.gameSession.Submarine != null) subName = GameMain.gameSession.SubmarineInfo.DisplayName;
            var gameOver = GameMain.gameSession.GameMode.IsSinglePlayer
                ? GameMain.gameSession.CrewManager.GetCharacters().All(c => c.IsDead || c.IsIncapacitated)
                : GameMain.gameSession.CrewManager.GetCharacters()
                    .All(c => c.IsDead || c.IsIncapacitated || c.IsBot);
            var locationName = (Submarine.MainSub is { AtEndExit: true }
                ? GameMain.gameSession.RoundSummary.endLocation?.DisplayName
                : GameMain.gameSession.RoundSummary.startLocation?.DisplayName)!;

            string textTag;
            if (GameMain.gameSession.GameMode is PvPMode)
            {
                _discordPresenceObject.Details = CombatMission.Winner switch
                {
                    CharacterTeamType.Team1 =>
                        $"{TextManager.Get("traumaticpresence.RoundSummaryRoundHasEnded").Fallback(TextManager.Get("RoundSummaryRoundHasEnded"), false)} {TextManager.Get("missionmessage0.outpostdeathmatch")}",
                    CharacterTeamType.Team2 =>
                        $"{TextManager.Get("traumaticpresence.RoundSummaryRoundHasEnded").Fallback(TextManager.Get("RoundSummaryRoundHasEnded"), false)} {TextManager.Get("missionmessage1.outpostdeathmatch")}",
                    CharacterTeamType.None =>
                        $"{TextManager.Get("traumaticpresence.RoundSummaryRoundHasEnded").Fallback(TextManager.Get("RoundSummaryRoundHasEnded"), false)} {TextManager.Get("missionfailure.pvpmission")}",
                    _ => _discordPresenceObject.Details
                };
            }
            else if (gameOver)
            {
                textTag = "traumaticpresence.RoundSummaryGameOver";
                _discordPresenceObject.Details = TextManager.GetWithVariables(textTag, ("[sub]", subName)).ToString();
            }
            else
            {
                if (GameMain.gameSession.Campaign != null)
                {
                    var switcheroo = GameMain.gameSession.Campaign.GetAvailableTransition();
                    switch (switcheroo)
                    {
                        case CampaignMode.TransitionType.LeaveLocation:
                            locationName = GameMain.gameSession.RoundSummary.startLocation?.DisplayName;
                            textTag = "traumaticpresence.RoundSummaryLeaving";
                            break;
                        case CampaignMode.TransitionType.ProgressToNextLocation:
                            locationName = GameMain.gameSession.RoundSummary.endLocation?.DisplayName;
                            textTag = "traumaticpresence.RoundSummaryProgress";
                            break;
                        case CampaignMode.TransitionType.ProgressToNextEmptyLocation:
                            locationName = GameMain.gameSession.RoundSummary.endLocation?.DisplayName;
                            textTag = "traumaticpresence.RoundSummaryProgressToEmptyLocation";
                            break;
                        case CampaignMode.TransitionType.ReturnToPreviousLocation:
                            locationName = GameMain.gameSession.RoundSummary.startLocation?.DisplayName;
                            textTag = "traumaticpresence.RoundSummaryReturn";
                            break;
                        case CampaignMode.TransitionType.ReturnToPreviousEmptyLocation:
                            locationName = GameMain.gameSession.RoundSummary.startLocation?.DisplayName;
                            textTag = "traumaticpresence.RoundSummaryReturnToEmptyLocation";
                            break;
                        default:
                            if (Submarine.MainSub == null)
                                textTag = "traumaticpresence.RoundSummaryRoundHasEnded";
                            else
                                textTag = Submarine.MainSub.AtEndExit
                                    ? "traumaticpresence.RoundSummaryProgress"
                                    : "traumaticpresence.RoundSummaryReturn";
                            break;
                    }

#if DEBUG
                    DebugConsole.NewMessage(
                        $"Campaign mode: Setting RPC Details to {textTag}, which is localized to {TextManager.Get(textTag)}",
                        Color.DodgerBlue);
                    DebugConsole.NewMessage(
                        $"{TextManager.GetWithVariables(textTag, ("[sub]", subName), ("[location]", locationName))}",
                        Color.DodgerBlue);
#endif
                    
                    _discordPresenceObject.Details = casualtyCount > 0
                        ? TextManager
                              .GetWithVariables(textTag, ("[sub]", subName), ("[location]", locationName)!)
                              .Fallback(TextManager.GetWithVariables(
                                  textTag.Replace("traumaticpresence.", ""), ("[sub]", subName),
                                  ("[location]", locationName)!), false).ToString() + " " +
                          TextManager.GetWithVariables("traumaticpresence.roundsummarycasualties",
                              ("[casualties]", casualtyCount.ToString())).ToString()
                        : TextManager
                            .GetWithVariables(textTag, ("[sub]", subName), ("[location]", locationName)!)
                            .Fallback(TextManager.GetWithVariables(
                                textTag.Replace("traumaticpresence.", ""), ("[sub]", subName),
                                ("[location]", locationName)!), false).ToString();
                }
                else
                {
                    _discordPresenceObject.Details = casualtyCount > 0
                        ? TextManager.Get("traumaticpresence.RoundSummaryRoundHasEnded")
                              .Fallback(TextManager.Get("RoundSummaryRoundHasEnded")).ToString() + " " +
                          TextManager.GetWithVariables("traumaticpresence.roundsummarycasualties", ("[casualties]", casualtyCount.ToString())).ToString()
                        : TextManager.Get("traumaticpresence.RoundSummaryRoundHasEnded")
                            .Fallback(TextManager.Get("RoundSummaryRoundHasEnded")).ToString();
                }
            }

            Timer.Dispose();
            _discordPresenceObject.Assets.SmallImageKey = string.Empty; //Hiding missions icon since the round is over.
            UpdateRichPresence();
            return null;
        });
        GameMain.LuaCs.Hook.Add("roundStart", "rpcRoundStarted", args =>
        {
            if (Character.Controlled == null)
            {
                _discordPresenceObject.Assets.LargeImageKey = "spectator";
                _discordPresenceObject.Assets.LargeImageText =
                    TextManager.Get("traumaticpresence.spectating").ToString();
            }

            CheckRoundDetails(); //Checks if it's pve or pvp. Sets the rpc's details accordingly
            Getters.GetMissions();

            _discordPresenceObject.State = GameMain.IsSingleplayer && GameMain.gameSession.Campaign != null
                ? Getters.Gamemode()
                : $"{Getters.Gamemode()} ({Getters.GameType()})";

            _discordPresenceObject.Assets.SmallImageKey = Getters.MissionIcon();
            _discordPresenceObject.Assets.SmallImageText = Getters.MissionList;
            UpdateRichPresence();
            UpdateMidroundPartySize();
            return null;
        });
    }

    /// <summary>
    ///     Harmony postfixes used as hooks.
    /// </summary>
    private class HarmonyPatches
    {
        // Responsible for Character RPC
        [HarmonyPatch(typeof(Character))]
        [HarmonyPatch("set_Controlled")]
        private class Patch_CharacterControlled
        {
            [HarmonyPostfix]
            private static void Postfix(Character value)
            {
                CharacterRPC.RPC_CharacterControlled(value);
            }
        }

        // Responsible for boss encounters.
        [HarmonyPatch(typeof(CharacterHUD))]
        [HarmonyPatch(nameof(ShowBossHealthBar))]
        private class Patch_ShowBossHealthBar
        {
            [HarmonyPostfix]
            private static void Postfix(Character character, float damage)
            {
                if (character == null) return;
                BossFight.RPC_OnBossDamaged(character, damage);
            }
        }

        // Also responsible for encounters
        [HarmonyPatch(typeof(CharacterHUD))]
        [HarmonyPatch(nameof(UpdateBossProgressBars))]
        [HarmonyPatch(typeof(CharacterHUD))]
        [HarmonyPatch(nameof(UpdateBossProgressBars))]
        private class Patch_UpdateBossProgressBars
        {
            [HarmonyPostfix]
            private static void Postfix(float deltaTime)
            {
                BossFight.RPC_OnBossBarUpdated(deltaTime);
            }
        }

        // Death.
        [HarmonyPatch(typeof(RespawnManager))]
        [HarmonyPatch(nameof(RespawnManager.ShowDeathPromptIfNeeded))]
        private class Patch_CharacterKilled
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                CharacterRPC.RPC_OnCharacterKilled();
            }
        }

        // Late-calling problematic patches and methods.
        [HarmonyPatch(typeof(NetLobbyScreen))]
        [HarmonyPatch(nameof(NetLobbyScreen.AddToGUIUpdateList))]
        private class Patch_NetLobbyScreen_AddToGUIUpdateList
        {
            private static bool isInitialized;

            [HarmonyPostfix]
            private static void Postfix()
            {
                if (isInitialized) return;
                isInitialized = true;
#if DEBUG
                DebugConsole.NewMessage("AddToGUIUpdateList Called.", Color.DodgerBlue);
#endif
                InitLateHarmonyPatches();
                SetBaseParty();
#if DEBUG
                DebugConsole.NewMessage("Unpatching NetLobbyScreen.AddToGUIUpdateList", Color.OrangeRed);
#endif
                //Don't need it anymore.
                harmony.Unpatch(typeof(NetLobbyScreen).GetMethod(nameof(NetLobbyScreen.AddToGUIUpdateList)),
                    HarmonyPatchType.Postfix);
            }
        }
    }

    /// <summary>
    ///     Initializes patches that can only be applied while fully loaded in
    /// </summary>
    public static void InitLateHarmonyPatches()
    {
        harmony.Patch(
            typeof(GameClient).GetMethod(nameof(GameClient.ReadClientList),
                BindingFlags.NonPublic | BindingFlags.Instance),
            null,
            new HarmonyMethod(typeof(Plugin).GetMethod(nameof(RPC_ClientListRead)))
        );
    }

    public static void RPC_ClientListRead()
    {
        UpdateMidroundPartySize();
    }

    public static void InitEventSubscriptions()
    {
        SteamMatchmaking.OnLobbyEntered += SteamPresence.GetJoinedSteamLobby;
    }

    /// <summary>
    ///     Creates the base Discord RPC Object that should be used throughout the code.
    /// </summary>
    public static void SetBaseRPC()
    {
        _discordPresenceObject = new RichPresence
        {
            Assets = new Assets
            {
                LargeImageKey = "gameicon"
            },
            Timestamps = new Timestamps(Getters.SessionStartTime)
        };
        //This doesn't seem like a bad place to set basic states from.
        if (Screen.Selected.IsEditor)
        {
            _discordPresenceObject.State = TextManager.Get("traumaticpresence.subeditorstatus")
                .Fallback("subeditorbutton", false).ToString();
            _discordPresenceObject.Assets.LargeImageKey = "subeditor";
        }
        else if (GameMain.IsMultiplayer)
            _discordPresenceObject.State = TextManager.Get("traumaticpresence.inserverlobby")
                .Fallback(TextManager.Get("tabmenu.inlobby"), false).ToString();
        else
            _discordPresenceObject.State = TextManager.Get("traumaticpresence.inmenu")
                .Fallback(TextManager.Get("pausemenuquit"), false).ToString();
        ;
        UpdateRichPresence();
    }

    /// <summary>
    ///     Absolutely miserable solution to parties.
    /// </summary>
    public static void SetBaseParty()
    {
        if (!GameMain.IsMultiplayer) return;
        {
            if (GameMain.Client.ServerSettings.maxPlayers != 0)
            {
                Timer.Dispose();
                _discordPartyObject = new Party
                {
                    ID = Getters.MultiplayerData.ServerEndpoint(),
                    Privacy = Getters.MultiplayerData.PrivacySetting(),
                    Size = Getters.MultiplayerData.PlayerCount(),
                    Max = Getters.MultiplayerData.MaxPlayerCount()
                };
                _discordPresenceObject.Party = _discordPartyObject;
                try
                {
                    RpcClient.UpdateParty(_discordPartyObject);
                }
                catch
                {
                    RpcClient.Initialize();
                    RpcClient.UpdateParty(_discordPartyObject);
                }
            }
        }
    }

    /// <summary>
    ///     Sets mid-round presence data. Call this to update the rpc in a centralized manner.
    ///     It's not really mid-round anymore.
    /// </summary>
    public static void UpdateRichPresence()
    {
        if (_discordPresenceObject.Details is { Length: > 128 })
        {
            _discordPresenceObject.Details = _discordPresenceObject.Details.Substring(0, 125) + "...";
        }

        if (_discordPresenceObject.State is { Length: > 128 })
        {
            _discordPresenceObject.State = _discordPresenceObject.State.Substring(0, 125) + "...";
        }

        try
        {
            RpcClient.SetPresence(_discordPresenceObject);
        }
        catch (Exception ex)
        {
            DebugConsole.NewMessage(
                $"An error occured while trying to update Rich Presence: \n {ex.Message} \n Recreating RPC in hopes of fixing it.",
                Color.Red);
            RpcClient = new DiscordRpcClient("1274111447323906088");
            RpcClient.SetPresence(_discordPresenceObject);
        }
        //SteamPresence.SetSteamPresence(_discordPresenceObject.Details); //This most likely won't do anything.
        /*
         * In the case where a localization token is not found, the system will attempt to fallback to English.
         * If English is also not found, then rich presence will not be displayed in the Steam client.
         * Similarly, if a token specifies a substitution using a rich presence key that is not set,
         * then rich presence will not be displayed in the Steam client.
         * https://partner.steamgames.com/doc/api/ISteamFriends#richpresencelocalization
         */
        // Some line like #{variable} could theoretically work. Should be able to pass variable as the current discord status.
        // But then again, you'd have to break into fakefish hq to set up anything related to Steam.
    }

    public static void UpdateMidroundPartySize()
    {
        if (!GameMain.IsMultiplayer) return;
        try
        {
            RpcClient.UpdatePartySize(Getters.MultiplayerData.PlayerCount(), Getters.MultiplayerData.MaxPlayerCount());
        }
        catch (Exception ex)
        {
            SetBaseParty();
            RpcClient.UpdatePartySize(Getters.MultiplayerData.PlayerCount());
        }
    }

    public static void CheckRoundDetails()
    {
        string details;
        foreach (var mission in GameMain.gameSession.missions)
            if (mission is CombatMission combatMission)
                switch (combatMission.HasWinScore)
                {
                    case true:
                        CombatMode.Scoreable();
                        return;
                    default:
                        CombatMode.NonScoreable();
                        return;
                }

        if (GameMain.gameSession.LevelData != null &&
            GameMain.gameSession.LevelData.Type != LevelData.LevelType.LocationConnection)
            details = TextManager.GetWithVariables("traumaticpresence.AtOutpost",
                ("[outpost]", GameMain.gameSession.RoundSummary.startLocation.DisplayName),
                ("[biome]", Getters.Biome())).ToString();
        // Band-aid sub editor fix #3 (the conditional operator, not the whole else statement)
        else details = Getters.Biome() == String.Empty ? TextManager.GetWithVariables("traumaticpresence.testingsub", ("[sub]", Getters.MainSub())).ToString() : $"{Getters.MainSub()} | {Getters.Biome()}";
        _discordPresenceObject.Details = details;
    }

    /// <summary>
    ///     This class is practically redundant as any attempts at setting any presence-related values via steamworks have
    ///     failed.
    /// </summary>
    public static class SteamPresence
    {
        public static void GetJoinedSteamLobby(Lobby lobby)
        {
            _SteamP2PLobbyObject = lobby;
            SetSteamPlayerGroup();
        }

        public static void SetSteamPlayerGroup()
        {
            if (!SteamManager.IsInitialized) return;
            if (_SteamP2PLobbyObject.Id != 0 && _SteamP2PLobbyObject.MemberCount > 1)
            {
                SteamFriends.SetRichPresence("steam_player_group", _SteamP2PLobbyObject.Id.ToString());
                SteamFriends.SetRichPresence("steam_player_group_size", _SteamP2PLobbyObject.MemberCount.ToString());
            }
            else if (Getters.MultiplayerData.PlayerCount() > 1)
            {
                SteamFriends.SetRichPresence("steam_player_group", Getters.MultiplayerData.ServerEndpoint());
                SteamFriends.SetRichPresence("steam_player_group_size",
                    Getters.MultiplayerData.PlayerCount().ToString());
            }
            else if (Getters.MultiplayerData.PlayerCount() <= 1)
            {
                SteamFriends.SetRichPresence("steam_player_group", null!);
                SteamFriends.SetRichPresence("steam_player_group_size", null!);
            }
        }

        public static void SetSteamPresence(string presence)
        {
            if (!SteamManager.IsInitialized) return;
            SteamFriends.SetRichPresence("status", presence);
        }
    }

    /// <summary>
    ///     Class that contains a bunch of methods that get data from the game.
    /// </summary>
    public static class Getters
    {
        public static DateTime SessionStartTime;
        public static string MissionList { get; set; }

        /// <summary>
        ///     Whether to display SP, MP or Sub Editor, etc. There's probably a better way to do this
        /// </summary>
        public static string GameType()
        {
            if (GameMain.IsSingleplayer)
            {
                if (Screen.Selected.IsEditor)
                    return TextManager.Get("traumaticpresence.subeditorstatus")
                        .Fallback(TextManager.Get("subeditorbutton"), false).ToString();
                return TextManager.Get("gamemode.singleplayercampaign").ToString();
            }

            foreach (var mission in GameMain.gameSession.missions)
                if (mission is CombatMission combatMission)
                    switch (combatMission.winCondition)
                    {
                        case CombatMission.WinCondition.KillCount:
                            return TextManager.Get("missiontype.OutpostCombat").ToString();
                        case CombatMission.WinCondition.ControlSubmarine:
                            return TextManager.Get("missiontype.kingofthehull").ToString();
                        case CombatMission.WinCondition.LastManStanding:
                            return TextManager.Get("missiontype.SubVsSubCombat").ToString();
                    }

            return TextManager.Get("multiplayerlabel").ToString();
        }

        /// <summary>
        ///     Goes through gamesession's missions and puts them in a list
        ///     Sets MissionList string
        /// </summary>
        public static void GetMissions()
        {
            // List that should have the actual, localized mission names.
            List<string> missionList = new();

            if (GameMain.gameSession != null && GameMain.gameSession.missions != null)
            {
                foreach (var mission in GameMain.gameSession.missions)
                {
#if DEBUG
                    DebugConsole.NewMessage($"Iterated {mission} | {mission.Name}", Color.DodgerBlue);
#endif
                    if (mission.Name.Length ==
                        0) // Prevents things like random pirate encounters from leaving a "," at the end
                        continue;
                    missionList.Add(mission.Name.ToString());
                }

                //RPC doesn't like it when small image text exceeds 128 and yet it doesn't care if lage one exceeds 256
                MissionList = string.Join(", ", missionList);
                if (MissionList.Length > 128) MissionList = MissionList.Substring(0, 125) + "...";
            }
            else
            {
                MissionList = string.Empty;
            }
        }

        /// <summary>
        ///     Used to set SmallImageKey which indicates if the round has any missions.
        /// </summary>
        /// <returns>Mission icon/empty</returns>
        public static string MissionIcon()
        {
            if (MissionList != string.Empty) return "missionicon";
            return string.Empty;
        }

        public static string Gamemode()
        {
            return GameMain.gameSession.GameMode.Name.ToString();
        }
        
        public static string MainSub()
        {
            return GameMain.gameSession.SubmarineInfo.DisplayName.ToString();
        }

        /// <summary>
        /// String that returns the biome in which the level is currently in.
        /// Submarine editor detection heavily relies on this returning an empty string.
        /// If it ever somehow returns empty outside the submarine editor - expect trouble.
        /// </summary>
        /// <returns>Level's biome</returns>
        public static string Biome()
        {
            return GameMain.gameSession.LevelData != null ? GameMain.gameSession.LevelData.Biome.DisplayName.ToString() :
                // Temporary bandaid fix until I figure out how to detect the sub editor in a good way.
                // The line above this one is a lie. This did not end up being temporary.
                string.Empty;
        }

        public static class MultiplayerData
        {
            public static int PlayerCount()
            {
                return GameMain.IsMultiplayer ? GameMain.Client.ConnectedClients.Count() : 1;
            }

            public static int MaxPlayerCount()
            {
                if (_SteamP2PLobbyObject.Id != 0) return _SteamP2PLobbyObject.MaxMembers - 10;
                {
                    if (GameMain.Client.ServerSettings.maxPlayers == 0)
                    {
#if DEBUG
                        DebugConsole.NewMessage("MaxPlayerCount was called too early. Game's returning 0.");
#endif
                        return GameMain.Client.ServerSettings.MaxPlayers;
                    }

                    return GameMain.Client.ServerSettings.MaxPlayers;
                }
            }

            /// <summary>
            ///     Grabs the endpoint of a server and in theory it should work for Steam P2P, Lidgren and the extinct-on-arrival EOS
            ///     players
            ///     One potential issue with this could be that the endpoint might return as 127.0.0.1 but that can happen in basegame
            ///     as well so whatever.
            /// </summary>
            /// <returns>Server Endpoint as a string.</returns>
            public static string ServerEndpoint()
            {
                foreach (var endpoint in GameMain.Client.serverEndpoints)
                {
#if DEBUG
                    DebugConsole.NewMessage($"Fetching endpoint {endpoint.Address}", Color.DeepSkyBlue);
#endif
                    return endpoint.ToString();
                }

#if DEBUG
                DebugConsole.NewMessage("No endpoint found. Does the server not exist? Falling back to empty",
                    Color.OrangeRed);
#endif
                return string.Empty;
            }

            /// <summary>
            ///     Is the server public or private?
            /// </summary>
            /// <returns>Party.PrivacySetting.Public/Private</returns>
            public static Party.PrivacySetting PrivacySetting()
            {
                return GameMain.Client.ServerSettings.IsPublic
                    ? Party.PrivacySetting.Public
                    : Party.PrivacySetting.Private;
            }
        }
    }

    /// <summary>
    ///     Class that contains everything related to the boss fight.
    ///     Works by tracking the HUD's healthbar due to barotraumatising reasons
    ///     This also means that it won't work in spectator. Ah well. YOU'RE not fighting it anyways.
    ///     Note: fightEndTimer gets disposed in two other areas in the code.
    /// </summary>
    public class BossFight
    {
        private const float
            updateInterval = 2.0f; // 1.5/2 seconds should be enough to stop the rpc connection from imploding.

        private static DateTime lastUpdateTime = DateTime.MinValue;
        public static bool IsBossDealtWith = true;

        public static void RPC_OnBossDamaged(Character character, float damage)
        {
            if ((DateTime.Now - lastUpdateTime).TotalSeconds < updateInterval) return;
            lastUpdateTime = DateTime.Now;
            IsBossDealtWith = false;

            switch (character)
            {
                case { IsDead: false, Removed: false }:
                {
                    var bossName = character.DisplayName ?? "Something";
                    var healthCurrentMax = $"{(int)character.Vitality}/{character.MaxVitality}";
                    SetBossFightRPC(bossName, healthCurrentMax);
                    break;
                }
                case { isDead: true, Health: < 0 }:
                    IsBossDealtWith = true;
                    CheckRoundDetails();
                    UpdateRichPresence();
                    break;
            }
        }

        /// <summary>
        ///     Status that displays the current boss' name and health. Health is raw atm
        /// </summary>
        public static void SetBossFightRPC(string bossName, string bossHealth)
        {
            _discordPresenceObject.Details = TextManager
                .GetWithVariables("traumaticpresence.fightingdetails", ("[boss]", bossName), ("[vitality]", bossHealth))
                .Fallback($"{bossName} | {bossHealth}").ToString();
            UpdateRichPresence();
        }

        public static void RPC_OnBossBarUpdated(float deltaTime)
        {
            var allBossBarsCompleted = bossProgressBars.All(bar => bar.Completed || bar.Interrupted);
            if (allBossBarsCompleted && !IsBossDealtWith)
            {
                IsBossDealtWith = true;
#if DEBUG
                DebugConsole.NewMessage("Boss has either been killed or evaded(Interrupted).", Color.OrangeRed);
#endif
                CheckRoundDetails();
                UpdateRichPresence();
            }
            //Note: ClearBossProgressBars() method gets called on endround, not when the health bars fade away.
        }
    }

    /// <summary>
    ///     Class that handles the PvP mode.
    ///     Scoreable is used for Koth & Deathmatch-like modes
    ///     NonScoreable is for modes that don't have a score(Like sub vs sub)
    /// </summary>
    public static class CombatMode
    {
        public static void Scoreable()
        {
            foreach (var mission in GameMain.gameSession.missions)
                if (mission is CombatMission combatMission)
                {
                    var team1Score = combatMission.Scores[0];
                    var team2Score = combatMission.Scores[1];
                    _discordPresenceObject.Details = TextManager.GetWithVariables("traumaticpresence.pvpScore",
                        ("[team1]", CombatMission.teamNames[0]), ("[team1score]", team1Score.ToString()),
                        ("[team2]", CombatMission.teamNames[1]), ("[team2score]", team2Score.ToString())).ToString();
                    UpdateRichPresence();
                }

            Timer.Start(Scoreable, 5000);
        }

        public static void NonScoreable() // Sub VS Sub & alike
        {
            foreach (var mission in GameMain.gameSession.missions)
                if (mission is CombatMission combatMission)
                {
#if DEBUG
                    DebugConsole.NewMessage(
                        $"First & Last existing subs: {combatMission.subs.First().Info.DisplayName} | {combatMission.subs.Last().Info.DisplayName}",
                        Color.MediumVioletRed);
#endif
                    _discordPresenceObject.Details = TextManager.GetWithVariables("traumaticpresence.subvsub",
                            ("[sub1]", combatMission.subs.First().Info.DisplayName),
                            ("[sub2]", combatMission.subs.Last().Info.DisplayName))
                        .Fallback(
                            $"{combatMission.subs.First().Info.DisplayName} VS {combatMission.subs.Last().Info.DisplayName}")
                        .ToString();
                    UpdateRichPresence();
                }
        }
    }

    /// <summary>
    ///     Class that's responsible for basically everything Character-related.
    /// </summary>
    public static class CharacterRPC
    {
        //private static Character lastControlledCharacter;
        private static int TimerRetryCount;

        public static void RPC_OnCharacterKilled()
        {
#if DEBUG
            DebugConsole.NewMessage("Character killed", Color.DodgerBlue);
#endif
            if (GameMain.Client.myCharacter == null || GameMain.Client.myCharacter.isDead)
            {
#if DEBUG
                DebugConsole.NewMessage($"Cause of death via textmanager: {TextManager.Get("CauseOfDeath")}",
                    Color.DodgerBlue);
#endif
                _discordPresenceObject.Assets.LargeImageKey = "icon-dead";
                _discordPresenceObject.Assets.LargeImageText = TextManager
                    .GetWithVariables("traumaticpresence.causeofdeath", ("[causeofdeath]", CauseOfDeath()))
                    .Fallback($"{TextManager.Get("CauseOfDeath")}: {CauseOfDeath()}").ToString();
                UpdateRichPresence();
            }
        }

        public static string CauseOfDeath()
        {
            var controlledCharacter = GameMain.Client?.myCharacter;

            // For ultra mega edge-case scenarios that shouldn't really happen. Ever.
            if (controlledCharacter == null || controlledCharacter.CauseOfDeath == null)
            {
                DebugConsole.NewMessage("Got absolutely nothing as cause of death. How?", Color.OrangeRed);
                return "Shenanigans.";
            }

            var deathType = controlledCharacter.CauseOfDeath.Type.ToString();
            var affliction = controlledCharacter.CauseOfDeath.Affliction?.Name.ToString();

            return !string.IsNullOrEmpty(affliction) ? affliction : deathType;
        }

        private static string JobIcon()
        {
            var basegameIcon =
                Character.controlled switch
                {
                    { IsCaptain: true } => "captain",
                    { IsSecurity: true } => "securityofficer",
                    { IsMedic: true } => "medicaldoctor",
                    { IsEngineer: true } => "engineer",
                    { IsMechanic: true } => "mechanic",
                    { IsAssistant: true } => "assistant",
                    //Less standard basegame jobs
                    { IsWatchman: true } => "spectator",
                    { IsPrisoner: true } => "prisoner",
                    _ => null
                };
            if (basegameIcon != null)
            {
                return basegameIcon;
            }

#if DEBUG
            DebugConsole.NewMessage("Job's not one of the standard ones. Checking if it's one of the known modded ones",
                Color.BlueViolet);
            DebugConsole.NewMessage($"Job Identifier is: {Character.controlled.JobIdentifier.Value}", Color.BlueViolet);
#endif
            var moddedJobs = new Dictionary<string, string>
            {
                // Some idents are renamed for consistency or because they differ a lot from their actual name
                // Hungry Europans
                { "he-chef", "he-chef" },
                // NT addons
                { "surgeon", "surgeon" },
                { "swedic", "swedic" },
                // Husk Acolyte
                { "acolyte", "acolyte" },
                // Scientist job
                { "v_scientist", "v_scientist" },
                // Playable mudraptor (most of them, hopefully. There's an unsettling amount of mudraptor mods.)
                { "PlayerMudraptorJob", "mudraptorjob" },
                // JobsExtended
                { "chief", "chief_of_the_boat"},
                { "executive_officer", "executive_officer" },
                { "navigator", "navigator"},
                { "quartermaster", "quartermaster"},
                { "head_of_security", "head_of_security"}, // space station 13 real
                { "chiefmedicaldoctor", "chief_medical_doctor"},
                { "passenger", "passenger"},
                { "inmate", "je_prisoner"},
                { "janitor", "je_janitor"},
                // MedievalTrauma
                { "vagabond", "vagabond"},
                // Hunter's Husk
                { "PlayerHuskJob", "huskjob"},
                // Barotrauma 40K
                {"magos", "magos"},
                {"skitari", "skitari"},
                {"sororitas", "sororitas"},
                {"engineseer", "engineseer"},
                {"guardsman","guardsman"},
                // Harlequin
                {"harlequin", "harlequin"},
                // Admiral
                {"admiral", "admiral"}
            };
            if (moddedJobs.TryGetValue(Character.controlled.JobIdentifier.value, out var icon))
            {
                return icon;
            }

            DebugConsole.NewMessage($"Couldn't find an icon for job ID {Character.controlled.JobIdentifier.value}. Falling back to generic icon.", Color.OrangeRed);
            return "unknown-role";
        }

        /// <summary>
        ///     Method called by the harmony hook. This initializes the character stuff.
        /// </summary>
        /// <param name="value"></param>
        public static void RPC_CharacterControlled(Character value)
        {
            // Check if the new value is not null. if it ain't then the player has taken control of a character. Hopefully.
            if (value != null) OnCharacterControlled(value);
        }

        /// <summary>
        ///     Method that gets called by postfix, which is called by Harmony. Does things when the client controls gets ahold of
        ///     a character
        /// </summary>
        /// <param name="controlledCharacter"></param>
        public static void OnCharacterControlled(Character controlledCharacter)
        {
            if (GameMain.GameScreen.IsEditor) return; // Band-aid sub editor fix #4 (doesn't seem to be effective)
            if (GameMain.gameSession != null && controlledCharacter != null)
            {
                if (controlledCharacter.Info != null)
                {
                    if (SubEditorScreen.IsSubEditor())
                    {
                        _discordPresenceObject.Assets.LargeImageKey = "subeditor";
                        _discordPresenceObject.Assets.LargeImageText = string.Empty;
                    }
                    else
                    {
                        _discordPresenceObject.Assets.LargeImageText = controlledCharacter.Info.Job.Name.ToString();
                        _discordPresenceObject.Assets.LargeImageKey = JobIcon();
                    }

                    // PVP SmallIcon.
                    if (GameMain.gameSession.GameMode is PvPMode)
                    {
                        foreach (var mission in GameMain.gameSession.missions)
                            if (mission is CombatMission combatMission)
                            {
                                if (combatMission.winCondition == CombatMission.WinCondition.KillCount)
                                {
                                    _discordPresenceObject.Assets.SmallImageText =
                                        $" | {TextManager.Get("missiontype.OutpostCombat")}";
                                }
                                else
                                {
                                    _discordPresenceObject.Assets.SmallImageText = $" | {Getters.MissionList}";
                                }
                            }

                        if (controlledCharacter.teamID == CharacterTeamType.Team1)
                        {
                            _discordPresenceObject.Assets.SmallImageKey = "coalition";
                            _discordPresenceObject.Assets.SmallImageText =
                                $"{CombatMission.teamNames[0]} {_discordPresenceObject.Assets.SmallImageText}";
                        }
                        else if (controlledCharacter.teamID == CharacterTeamType.Team2)
                        {
                            _discordPresenceObject.Assets.SmallImageKey = "jovian";
                            _discordPresenceObject.Assets.SmallImageText =
                                $"{CombatMission.teamNames[1]} {_discordPresenceObject.Assets.SmallImageText}";
                        }
                    }
                    UpdateRichPresence();
                }
                else
                {
                    DebugConsole.NewMessage("Can't find the controlled character. Retrying",
                        Color.OrangeRed);
                    Timer.Start(RetryGettingCharacterInfo, 500);
                }
            }
            else
            {
                if (GameScreen.Selected.IsEditor) return; // Bandaid sub editor fix
                if (TimerRetryCount > 20)
                {
                    DebugConsole.NewMessage("Failed getting character info for the 20th time. " +
                                            "Something must've gone horribly wrong. " +
                                            "Stopping the retry loop.", Color.Red);
                    return;
                }
                TimerRetryCount++;
                Timer.Start(RetryGettingCharacterInfo, 500);
            }
        }

        public static void RetryGettingCharacterInfo()
        {
            DebugConsole.NewMessage("Attempting to fetch character info again.", Color.DodgerBlue);
            OnCharacterControlled(Character.controlled);
        }
    }

    public static class Timer
    {
        private static System.Threading.Timer _TimerObject;

        public static void Start(Action method, int delayMs)
        {
            if (_TimerObject != null) _TimerObject.Dispose();
            _TimerObject = new System.Threading.Timer(_ =>
            {
#if DEBUG
                DebugConsole.NewMessage($"Action Timer firing. Calling method {method.Method}", Color.MediumVioletRed);
#endif
                method();
            }, null, delayMs, Timeout.Infinite);
        }

        /// <summary>
        ///     really really really REALLY make sure that it gets disposed.
        /// </summary>
        public static void Dispose()
        {
            if (_TimerObject != null)
            {
                _TimerObject.Dispose();
                _TimerObject = null;
#if DEBUG
                DebugConsole.NewMessage("Action Timer disposed", Color.MediumVioletRed);
#endif
            }
        }
    }
}
// If anything extraordinarily confusing happens, uncomment this and hope that this sheds some light on whatever's happening. 
/*public class Dbg : DiscordRPC.Logging.ILogger
{
    public LogLevel Level { get; set; }
    public bool Coloured { get; set; }

    public void Trace(string message, params object[] args)
    {
        if (Level > LogLevel.Trace) return;
        DebugConsole.NewMessage("TRACE: " + string.Format(message, args), Color.Green);
    }

    public void Info(string message, params object[] args)
    {
        if (Level > LogLevel.Info) return;
        DebugConsole.NewMessage("INFO: " + string.Format(message, args));
    }

    public void Warning(string message, params object[] args)
    {
        if (Level > LogLevel.Warning) return;
        DebugConsole.NewMessage("WARN: " + string.Format(message, args), color: Color.LightGoldenrodYellow);
    }

    public void Error(string message, params object[] args)
    {
        if (Level > LogLevel.Error) return;
        DebugConsole.NewMessage("ERR : " + string.Format(message, args), Color.OrangeRed);
    }
}*/