#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
#define FINDNEEDLE_STEAM
#endif
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;
#if FINDNEEDLE_STEAM
using Steamworks;
#endif

// Carries Netcode's bytes between Steam friends over Steam Networking Sockets. The host listens on its
// Steam id; a guest connects to the host's Steam id. Steam finds the route and relays when it must, so no
// address or open port is needed.
public sealed class BeachSteamTransport : NetworkTransport
{
    // Guest: the host's Steam id, set before StartClient.
    public ulong HostSteamId;
    public override ulong ServerClientId => 0;

#if FINDNEEDLE_STEAM
    readonly Dictionary<ulong,HSteamNetConnection> byClient = new();
    readonly Dictionary<HSteamNetConnection,ulong> byConnection = new();
    readonly Queue<(NetworkEvent kind,ulong client,byte[] data)> events = new();
    readonly IntPtr[] inbox = new IntPtr[64];
    Callback<SteamNetConnectionStatusChangedCallback_t> status;
    HSteamListenSocket listen = HSteamListenSocket.Invalid;
    HSteamNetPollGroup group = HSteamNetPollGroup.Invalid;
    HSteamNetConnection server = HSteamNetConnection.Invalid;
    bool hosting, running;
    ulong nextClient = 1;
    IntPtr outgoing;
    int outgoingSize;

    public override bool IsSupported => BeachSteam.Instance && BeachSteam.Instance.Ready;

    public override void Initialize(NetworkManager networkManager = null)
    {
        status ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(Changed);
    }

    public override bool StartServer()
    {
        if (!IsSupported) return false;
        Initialize();
        hosting = true;
        listen = SteamNetworkingSockets.CreateListenSocketP2P(0,0,null);
        group = SteamNetworkingSockets.CreatePollGroup();
        running = listen!=HSteamListenSocket.Invalid && group!=HSteamNetPollGroup.Invalid;
        return running;
    }

    public override bool StartClient()
    {
        if (!IsSupported || HostSteamId==0) return false;
        Initialize();
        hosting = false;
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(new CSteamID(HostSteamId));
        server = SteamNetworkingSockets.ConnectP2P(ref identity,0,0,null);
        running = server!=HSteamNetConnection.Invalid;
        return running;
    }

    void Changed(SteamNetConnectionStatusChangedCallback_t change)
    {
        if (!running) return;
        var connection = change.m_hConn;
        var state = change.m_info.m_eState;
        bool closed = state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                   || state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;
        if (hosting)
        {
            if (state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                if (change.m_info.m_hListenSocket!=listen) return;
                if (SteamNetworkingSockets.AcceptConnection(connection)!=EResult.k_EResultOK
                    || !SteamNetworkingSockets.SetConnectionPollGroup(connection,group))
                    SteamNetworkingSockets.CloseConnection(connection,0,"accept failed",false);
            }
            else if (state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                if (byConnection.ContainsKey(connection)) return;
                ulong id = nextClient++;
                byConnection[connection] = id;
                byClient[id] = connection;
                events.Enqueue((NetworkEvent.Connect,id,null));
            }
            else if (closed)
            {
                if (byConnection.Remove(connection,out ulong id))
                {
                    byClient.Remove(id);
                    events.Enqueue((NetworkEvent.Disconnect,id,null));
                }
                SteamNetworkingSockets.CloseConnection(connection,0,null,false);
            }
            return;
        }
        if (connection!=server) return;
        if (state==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            events.Enqueue((NetworkEvent.Connect,ServerClientId,null));
        else if (closed)
        {
            events.Enqueue((NetworkEvent.Disconnect,ServerClientId,null));
            SteamNetworkingSockets.CloseConnection(connection,0,null,false);
            server = HSteamNetConnection.Invalid;
        }
    }

    public override NetworkEvent PollEvent(out ulong clientId,out ArraySegment<byte> payload,out float receiveTime)
    {
        if (events.Count==0 && running) Receive();
        receiveTime = Time.realtimeSinceStartup;
        if (events.Count==0)
        {
            clientId = 0;
            payload = default;
            return NetworkEvent.Nothing;
        }
        var next = events.Dequeue();
        clientId = next.client;
        payload = new ArraySegment<byte>(next.data ?? Array.Empty<byte>());
        return next.kind;
    }

    void Receive()
    {
        int count = hosting
            ? SteamNetworkingSockets.ReceiveMessagesOnPollGroup(group,inbox,inbox.Length)
            : server!=HSteamNetConnection.Invalid ? SteamNetworkingSockets.ReceiveMessagesOnConnection(server,inbox,inbox.Length) : 0;
        for (int i=0; i<count; i++)
        {
            var message = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
            var data = new byte[message.m_cbSize];
            if (data.Length>0) Marshal.Copy(message.m_pData,data,0,data.Length);
            ulong from = ServerClientId;
            bool known = !hosting || byConnection.TryGetValue(message.m_conn,out from);
            SteamNetworkingMessage_t.Release(inbox[i]);
            if (known) events.Enqueue((NetworkEvent.Data,from,data));
        }
    }

    public override void Send(ulong clientId,ArraySegment<byte> payload,NetworkDelivery networkDelivery)
    {
        HSteamNetConnection connection;
        if (hosting) { if (!byClient.TryGetValue(clientId,out connection)) return; }
        else connection = server;
        if (connection==HSteamNetConnection.Invalid || payload.Count==0) return;
        if (outgoingSize<payload.Count)
        {
            if (outgoing!=IntPtr.Zero) Marshal.FreeHGlobal(outgoing);
            outgoingSize = Mathf.NextPowerOfTwo(payload.Count);
            outgoing = Marshal.AllocHGlobal(outgoingSize);
        }
        Marshal.Copy(payload.Array,payload.Offset,outgoing,payload.Count);
        bool reliable = networkDelivery!=NetworkDelivery.Unreliable && networkDelivery!=NetworkDelivery.UnreliableSequenced;
        int flags = reliable ? Constants.k_nSteamNetworkingSend_ReliableNoNagle : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
        SteamNetworkingSockets.SendMessageToConnection(connection,outgoing,(uint)payload.Count,flags,out _);
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (!byClient.Remove(clientId,out var connection)) return;
        byConnection.Remove(connection);
        SteamNetworkingSockets.CloseConnection(connection,0,"removed",true);
    }

    public override void DisconnectLocalClient()
    {
        if (server==HSteamNetConnection.Invalid) return;
        SteamNetworkingSockets.CloseConnection(server,0,"left",true);
        server = HSteamNetConnection.Invalid;
    }

    public override ulong GetCurrentRtt(ulong clientId)
    {
        var connection = hosting ? byClient.GetValueOrDefault(clientId,HSteamNetConnection.Invalid) : server;
        if (connection==HSteamNetConnection.Invalid) return 0;
        var now = new SteamNetConnectionRealTimeStatus_t();
        var lane = new SteamNetConnectionRealTimeLaneStatus_t();
        return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection,ref now,0,ref lane)==EResult.k_EResultOK
            ? (ulong)Mathf.Max(0,now.m_nPing) : 0;
    }

    public override void Shutdown()
    {
        running = false;
        if (IsSupported)
        {
            foreach (var connection in byClient.Values) SteamNetworkingSockets.CloseConnection(connection,0,"closed",true);
            if (server!=HSteamNetConnection.Invalid) SteamNetworkingSockets.CloseConnection(server,0,"closed",true);
            if (listen!=HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
            if (group!=HSteamNetPollGroup.Invalid) SteamNetworkingSockets.DestroyPollGroup(group);
        }
        byClient.Clear();
        byConnection.Clear();
        events.Clear();
        server = HSteamNetConnection.Invalid;
        listen = HSteamListenSocket.Invalid;
        group = HSteamNetPollGroup.Invalid;
        nextClient = 1;
    }

    void OnDestroy()
    {
        status?.Dispose();
        if (outgoing!=IntPtr.Zero) Marshal.FreeHGlobal(outgoing);
        outgoing = IntPtr.Zero;
    }
#else
    public override bool IsSupported => false;
    public override void Initialize(NetworkManager networkManager = null) { }
    public override bool StartServer() => false;
    public override bool StartClient() => false;
    public override NetworkEvent PollEvent(out ulong clientId,out ArraySegment<byte> payload,out float receiveTime)
    {
        clientId = 0; payload = default; receiveTime = Time.realtimeSinceStartup;
        return NetworkEvent.Nothing;
    }
    public override void Send(ulong clientId,ArraySegment<byte> payload,NetworkDelivery networkDelivery) { }
    public override void DisconnectRemoteClient(ulong clientId) { }
    public override void DisconnectLocalClient() { }
    public override ulong GetCurrentRtt(ulong clientId) => 0;
    public override void Shutdown() { }
#endif
}
