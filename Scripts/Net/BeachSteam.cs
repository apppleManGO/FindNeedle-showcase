#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
#define FINDNEEDLE_STEAM
#endif
using System;
using UnityEngine;
#if FINDNEEDLE_STEAM
using Steamworks;
#endif

// Steam: signing in, the friends-only lobby a host opens, the invite dialog, and an accepted invite
// arriving (in game, or as the launch argument when the invite started the game). The players' traffic
// itself goes through BeachSteamTransport. Without the Steam client running, Ready stays false and the
// game keeps to joining by address.
public sealed class BeachSteam : MonoBehaviour
{
    // Steamworks app id. 480 is Valve's public test app (Spacewar). steam_appid.txt in the project root
    // must hold the same number for the editor and development builds.
    public const uint AppId = 480;
    public const int MaxMembers = 8;

    public static BeachSteam Instance { get; private set; }
    public bool Ready { get; private set; }
    public string Problem { get; private set; } = "";
    // The lobby this machine is in, 0 for none.
    public ulong Lobby { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance) return;
        var root = new GameObject("Steam");
        DontDestroyOnLoad(root);
        Instance = root.AddComponent<BeachSteam>();
    }

#if FINDNEEDLE_STEAM
    Callback<GameLobbyJoinRequested_t> joinRequested;
    CallResult<LobbyCreated_t> created;
    CallResult<LobbyEnter_t> entered;
    Action<bool,string> onCreated;
    Action<ulong,string> onEntered;
    // An invite accepted before the beach had loaded: the launch argument.
    ulong pendingLobby;

    void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        // A release build started outside Steam asks Steam to start it instead, so invites and the overlay work.
        if (AppId!=480)
        {
            try { if (SteamAPI.RestartAppIfNecessary(new AppId_t(AppId))) { Application.Quit(); return; } }
            catch (DllNotFoundException) { }
        }
#endif
        try
        {
            var result = SteamAPI.InitEx(out string message);
            Ready = result==ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
            if (!Ready) Problem = message;
        }
        catch (DllNotFoundException) { Problem = "Steam 라이브러리를 찾지 못했다"; }
        catch (Exception e) { Problem = e.Message; }
        if (!Ready) { Debug.Log("[FindNeedle] Steam 없이 시작한다: "+Problem); return; }

        joinRequested = Callback<GameLobbyJoinRequested_t>.Create(JoinRequested);
        created = CallResult<LobbyCreated_t>.Create(Created);
        entered = CallResult<LobbyEnter_t>.Create(Entered);
        // Start finding relays now, so the first connection does not wait for it.
        SteamNetworkingUtils.InitRelayNetworkAccess();
        pendingLobby = LaunchLobby();
    }

    // Steam starts the game with "+connect_lobby <id>" when an invite is accepted while it is closed.
    static ulong LaunchLobby()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args,"+connect_lobby");
        return at>=0 && at+1<args.Length && ulong.TryParse(args[at+1],out ulong lobby) ? lobby : 0;
    }

    void Update()
    {
        if (!Ready) return;
        SteamAPI.RunCallbacks();
        // Wait a few frames so the beach and its menu exist before joining.
        if (pendingLobby!=0 && Time.frameCount>3)
        {
            ulong lobby = pendingLobby;
            pendingLobby = 0;
            BeachNet.Ensure().AcceptInvite(lobby);
        }
    }

    void OnApplicationQuit()
    {
        if (!Ready) return;
        LeaveLobby();
        SteamAPI.Shutdown();
        Ready = false;
    }

    public string MyName => Ready ? SteamFriends.GetPersonaName() : "";
    public ulong MyId => Ready ? SteamUser.GetSteamID().m_SteamID : 0;
    public bool OverlayOn => Ready && SteamUtils.IsOverlayEnabled();

    public void CreateLobby(Action<bool,string> done)
    {
        if (!Ready) { done(false,Problem); return; }
        LeaveLobby();
        onCreated = done;
        created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly,MaxMembers));
    }

    void Created(LobbyCreated_t result,bool failed)
    {
        var done = onCreated;
        onCreated = null;
        if (failed || result.m_eResult!=EResult.k_EResultOK) { done?.Invoke(false,"Steam 방을 만들지 못했다 · "+result.m_eResult); return; }
        Lobby = result.m_ulSteamIDLobby;
        var id = new CSteamID(Lobby);
        SteamMatchmaking.SetLobbyData(id,"host",MyId.ToString());
        SteamMatchmaking.SetLobbyData(id,"version",Application.version);
        done?.Invoke(true,"");
    }

    // Joins a friend's lobby and reports the host's Steam id, or 0 and why not.
    public void EnterLobby(ulong lobby,Action<ulong,string> done)
    {
        if (!Ready) { done(0,Problem); return; }
        LeaveLobby();
        onEntered = done;
        entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
    }

    void Entered(LobbyEnter_t result,bool failed)
    {
        var done = onEntered;
        onEntered = null;
        if (failed || result.m_EChatRoomEnterResponse!=(uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            done?.Invoke(0,"친구의 방에 들어가지 못했다 · 방이 닫혔거나 가득 찼다");
            return;
        }
        Lobby = result.m_ulSteamIDLobby;
        var id = new CSteamID(Lobby);
        string version = SteamMatchmaking.GetLobbyData(id,"version");
        if (version!=Application.version)
        {
            LeaveLobby();
            done?.Invoke(0,"친구와 게임 버전이 다르다 · 친구 "+version+" / 나 "+Application.version);
            return;
        }
        ulong.TryParse(SteamMatchmaking.GetLobbyData(id,"host"),out ulong host);
        if (host==0) host = SteamMatchmaking.GetLobbyOwner(id).m_SteamID;
        done?.Invoke(host,"");
    }

    public void LeaveLobby()
    {
        if (Lobby==0) return;
        if (Ready) SteamMatchmaking.LeaveLobby(new CSteamID(Lobby));
        Lobby = 0;
    }

    // Opens Steam's friend picker for this lobby. False when the overlay cannot appear.
    public bool Invite()
    {
        if (!Ready || Lobby==0) return false;
        SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(Lobby));
        return OverlayOn;
    }

    // A friend's invite was accepted, or "Join game" was chosen in the friends list, while playing.
    void JoinRequested(GameLobbyJoinRequested_t request)
    {
        if (request.m_steamIDLobby.m_SteamID==Lobby) return;
        BeachNet.Ensure().AcceptInvite(request.m_steamIDLobby.m_SteamID);
    }
#else
    void Awake() => Problem = "이 플랫폼에서는 Steam을 쓰지 않는다";
    public string MyName => "";
    public ulong MyId => 0;
    public bool OverlayOn => false;
    public void CreateLobby(Action<bool,string> done) => done(false,Problem);
    public void EnterLobby(ulong lobby,Action<ulong,string> done) => done(0,Problem);
    public void LeaveLobby() { }
    public bool Invite() => false;
#endif

    void OnDestroy()
    {
        if (Instance==this) Instance = null;
    }
}
