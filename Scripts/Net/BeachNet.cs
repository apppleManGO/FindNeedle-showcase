using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The session: one player hosts, friends join through a Steam invite or, on one network, by address. It carries BeachSearch's messages, hands a
// newcomer the whole beach, and shows the other players. It survives the scene reload between beaches.
public sealed partial class BeachNet : MonoBehaviour
{
    public static BeachNet Instance { get; private set; }
    public const ushort Port = 7777;
    const string Channel = "beach";
    // Everything about the beach goes down one ordered channel, so a snapshot and the changes after it cannot swap.
    const NetworkDelivery Ordered = NetworkDelivery.ReliableFragmentedSequenced;
    const int Chunk = 16000;

    enum Kind : byte
    {
        World = 1,   // a BeachSearch message
        Hello,       // host to a new guest: which beach, and whether a visit is under way
        Visit,       // host to guests: everyone goes to this beach
        Ready,       // guest to host: my scene is loaded, send the beach
        Snapshot,    // host to one guest: a piece of the beach
        Pose,        // where a player is standing
        LobbyChoice, LobbyRoster,
        Left,        // host to guests: a player has gone
        Voice        // a player's compressed voice; the host passes guests' voices on
    }

    NetworkManager manager;
    UnityTransport transport;
    BeachSteamTransport steamTransport;
    BeachVoice voice;
    public BeachVoice Voice => voice;
    BeachSearch world;
    // Host: guests who hold the current beach and so receive its changes. Guest: whether this machine does.
    readonly HashSet<ulong> synced = new();
    readonly HashSet<ulong> waiting = new();
    bool caughtUp;
    // Host: the scene is being reloaded for another beach, so the beach in hand is about to vanish.
    bool moving;
    readonly Dictionary<int,BeachGhost> ghosts = new();
    MemoryStream arriving;
    float poseDue;

    public bool Hosting { get; private set; }
    public bool Joined { get; private set; }
    public bool Online => Hosting || Joined;
    public string Status { get; private set; } = "친구와 함께 찾기";
    public string Address { get; private set; } = "127.0.0.1";
    public bool Typing { get; private set; }
    // This session runs through Steam rather than an address.
    public bool ViaSteam { get; private set; }
    public static bool SteamReady => BeachSteam.Instance && BeachSteam.Instance.Ready;
    public bool CanInvite => Hosting && ViaSteam && BeachSteam.Instance && BeachSteam.Instance.Lobby!=0;
    // How many other players are standing on this beach as far as this machine can see.
    public int Seen => ghosts.Count;
    public int Players => manager && manager.IsListening && Hosting ? manager.ConnectedClientsIds.Count : ghosts.Count+1;

    public static BeachNet Ensure()
    {
        if (Instance) return Instance;
        var root = new GameObject("멀티플레이");
        root.SetActive(false);
        var net = root.AddComponent<BeachNet>();
        net.transport = root.AddComponent<UnityTransport>();
        net.steamTransport = root.AddComponent<BeachSteamTransport>();
        net.voice = root.AddComponent<BeachVoice>();
        net.manager = root.AddComponent<NetworkManager>();
        net.manager.NetworkConfig ??= new NetworkConfig();
        net.manager.NetworkConfig.NetworkTransport = net.transport;
        // Each machine loads the beach scene itself; nothing is spawned through the network.
        net.manager.NetworkConfig.EnableSceneManagement = false;
        net.manager.NetworkConfig.ForceSamePrefabs = false;
        Instance = net;
        root.SetActive(true);
        DontDestroyOnLoad(root);
        net.world = FindFirstObjectByType<BeachSearch>();
        return net;
    }

    void OnDestroy()
    {
        if (Instance==this) Instance = null;
    }

    // ---- Starting and ending a session ----

    // With Steam running the room is a friends-only Steam lobby; otherwise it is open on this network.
    public void Host() => Host(SteamReady);

    public void Host(bool overSteam)
    {
        if (Online) return;
        ViaSteam = overSteam && SteamReady;
        if (ViaSteam) manager.NetworkConfig.NetworkTransport = steamTransport;
        else
        {
            manager.NetworkConfig.NetworkTransport = transport;
            transport.SetConnectionData("127.0.0.1",Port,"0.0.0.0");
        }
        if (!manager.StartHost())
        {
            Status = ViaSteam ? "Steam 방을 만들지 못했다" : "방을 만들지 못했다 · 포트 "+Port+" 확인";
            ViaSteam = false;
            return;
        }
        Hosting = true;
        lobby.Clear();
        lobby[0]=new LobbyMember(0,SelectedLook,false);
        Listen();
        if (world) Attach(world,false);
        Describe();
        if (!ViaSteam) return;
        Status = "Steam 방을 여는 중";
        BeachSteam.Instance.CreateLobby((made,why) =>
        {
            if (!Hosting || !ViaSteam) { BeachSteam.Instance.LeaveLobby(); return; }
            if (made) Describe(); else Status = why+" · 지금은 혼자 수색한다";
        });
    }

    // Opens Steam's friend picker. When the overlay is off, friends can still use "Join game" in their list.
    public void Invite()
    {
        if (!CanInvite) return;
        if (!BeachSteam.Instance.Invite())
            Status = "Steam 오버레이가 꺼져 있다 · 친구가 Steam 친구 목록에서 내 이름 옆 '게임 참가'를 누르면 된다";
    }

    // A Steam invite was accepted: leave whatever this machine is doing and go to that friend's room.
    public void AcceptInvite(ulong steamLobby)
    {
        if (!SteamReady) return;
        if (Online) Leave("초대받은 방으로 옮긴다");
        CancelTyping();
        Status = "초대받은 방에 들어가는 중";
        BeachSteam.Instance.EnterLobby(steamLobby,(host,why) =>
        {
            if (host==0) { Status = why; return; }
            if (Online) return;
            // A solo search under way would keep running unsynced underneath; go back to the start screen.
            if (world && world.Started) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            steamTransport.HostSteamId = host;
            manager.NetworkConfig.NetworkTransport = steamTransport;
            ViaSteam = true;
            if (!StartJoining("친구의 방에 접속하는 중")) BeachSteam.Instance.LeaveLobby();
        });
    }

    public void BeginTyping()
    {
        if (Online) return;
        Typing = true;
        if (Keyboard.current!=null) Keyboard.current.onTextInput += Type;
    }

    public void CancelTyping()
    {
        if (!Typing) return;
        Typing = false;
        if (Keyboard.current!=null) Keyboard.current.onTextInput -= Type;
    }

    void Type(char letter)
    {
        if ((char.IsDigit(letter) || letter=='.') && Address.Length<15) Address += letter;
    }

    public void Join(string address)
    {
        Address = address;
        Join();
    }

    public void Join()
    {
        if (Online) return;
        CancelTyping();
        if (!IPAddress.TryParse(Address,out _)) { Status = "주소를 숫자와 점으로 적자 · 예 192.168.0.12"; return; }
        manager.NetworkConfig.NetworkTransport = transport;
        transport.SetConnectionData(Address,Port);
        ViaSteam = false;
        StartJoining(Address+" 에 접속하는 중");
    }

    bool StartJoining(string connecting)
    {
        if (!manager.StartClient()) { Status = "접속을 시작하지 못했다"; ViaSteam = false; return false; }
        Joined = true;
        caughtUp = false;
        Listen();
        manager.OnClientStopped += Stopped;
        // From here the host decides which beach is played.
        if (world) world.Guest = true;
        Status = connecting;
        return true;
    }

    string Unreached => ViaSteam ? "친구의 방에 접속하지 못했다 · 친구가 아직 방에 있는지 확인" : "접속하지 못했다 · 주소와 방이 열렸는지 확인";

    void Listen()
    {
        voice.Outbox = Speak;
        voice.Figure = FigureOf;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel,Hear);
        manager.OnClientConnectedCallback += Arrived;
        manager.OnClientDisconnectCallback += Departed;
    }

    public void Leave() => Leave(Hosting ? "방을 닫았다" : "방에서 나왔다");

    void Stopped(bool wasHost)
    {
        if (Joined) Leave(caughtUp ? "방장과 연결이 끊겼다" : Unreached);
    }

    void Leave(string why)
    {
        if (!Online) return;
        Hosting = Joined = false;
        moving = false;
        if (ViaSteam && BeachSteam.Instance) BeachSteam.Instance.LeaveLobby();
        ViaSteam = false;
        if (manager)
        {
            manager.OnClientConnectedCallback -= Arrived;
            manager.OnClientDisconnectCallback -= Departed;
            manager.OnClientStopped -= Stopped;
            if (manager.IsListening || manager.IsClient) manager.Shutdown();
        }
        synced.Clear();
        waiting.Clear();
        lobby.Clear();
        voice.Clear();
        foreach (var ghost in ghosts.Values) if (ghost) Destroy(ghost.gameObject);
        ghosts.Clear();
        if (world)
        {
            world.SessionEnded();
            world.Outbox = null;
            world.Guest = false;
            if (world.Bucket) world.Bucket.gameObject.SetActive(true);
        }
        Status = why;
    }

    void Describe()
    {
        if (Hosting && ViaSteam) Status = "Steam 방 열림 · 친구 초대로 부르세요 · "+manager.ConnectedClientsIds.Count+"명";
        else if (Hosting) Status = "방 열림 · 내 주소 "+LocalAddress()+" · "+manager.ConnectedClientsIds.Count+"명";
        else if (Joined) Status = caughtUp ? "함께 찾는 중 · "+(ghosts.Count+1)+"명" : "방장이 해변을 고르는 중";
    }

    static string LocalAddress()
    {
        try
        {
            // No packet is sent; this only asks which of this machine's addresses faces the network.
            using var probe = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
            probe.Connect("10.255.255.255",1);
            return ((IPEndPoint)probe.LocalEndPoint).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }

    void Arrived(ulong id)
    {
        if (Hosting)
        {
            if (id==manager.LocalClientId) return;
            lobby[id]=new LobbyMember(id,0,false);
            PublishLobby();
            bool playing = world && world.Started && !world.Won;
            using var body = new MemoryStream();
            using var w = new BinaryWriter(body);
            w.Write((byte)(world ? world.Theme : BeachTheme.Neighborhood));
            w.Write(playing);
            w.Flush();
            Send(Kind.Hello,0,body.ToArray(),id,Ordered);
            Describe();
        }
        else if (id==manager.LocalClientId) { Describe(); SetLobbyChoice(SelectedLook,false); }
    }

    void Departed(ulong id)
    {
        if (!Hosting)
        {
            // For a guest this means the host is gone, or was never reached.
            Leave(caughtUp ? "방장과 연결이 끊겼다" : Unreached);
            return;
        }
        lobby.Remove(id);
        PublishLobby();
        synced.Remove(id);
        waiting.Remove(id);
        Forget((int)id);
        if (world) world.PlayerLeft((int)id);
        foreach (ulong guest in synced) Send(Kind.Left,(int)id,System.Array.Empty<byte>(),guest,Ordered);
        Describe();
    }

    void Forget(int id)
    {
        if (ghosts.Remove(id,out var ghost) && ghost) Destroy(ghost.gameObject);
        voice.Forget(id);
    }

    // The other players standing on this beach, by player number.
    public IEnumerable<(int id,Transform figure)> Figures()
    {
        foreach (var pair in ghosts)
            if (pair.Value) yield return (pair.Key,pair.Value.transform);
    }

    Transform FigureOf(int id) => world && world.Started && ghosts.TryGetValue(id,out var ghost) && ghost ? ghost.transform : null;

    // This player's voice: from the host to everyone, from a guest to the host, who passes it on.
    // Unreliable: a lost packet is a moment of silence, better than a voice arriving late.
    void Speak(byte[] packet)
    {
        if (!manager || !manager.IsListening) return;
        int me = (int)manager.LocalClientId;
        if (Hosting) { foreach (ulong id in manager.ConnectedClientsIds) if (id!=manager.LocalClientId) Send(Kind.Voice,me,packet,id,NetworkDelivery.Unreliable); }
        else if (manager.IsConnectedClient) Send(Kind.Voice,me,packet,NetworkManager.ServerClientId,NetworkDelivery.Unreliable);
    }

    // ---- The beach ----

    // Called by each beach as its scene starts. Returns true when it must wait for the host's snapshot.
    public bool Attach(BeachSearch beach,bool visiting)
    {
        world = beach;
        moving = false;
        ghosts.Clear();
        if (world.player) world.player.Wear(SelectedLook);
        if (!Online) return false;
        world.Outbox = Broadcast;
        world.Guest = Joined;
        world.Others = Standing;
        world.Team = () => ghosts;
        world.MyId = (int)manager.LocalClientId;
        if (world.player) world.player.Wear(SelectedLook);
        // The sandcastle bucket is not shared yet, so it stays out of co-op.
        if (world.Bucket) world.Bucket.gameObject.SetActive(false);
        // Stand side by side rather than inside one another.
        world.player.transform.position += Vector3.right*(.7f*world.MyId);
        world.player.Follow();
        if (Hosting)
        {
            // A new scene: every guest needs this beach afresh. Some may already be asking for it.
            synced.Clear();
            foreach (ulong guest in waiting) SendSnapshot(guest);
            waiting.Clear();
            return false;
        }
        caughtUp = false;
        if (!visiting) return false;
        Send(Kind.Ready,world.MyId,System.Array.Empty<byte>(),NetworkManager.ServerClientId,Ordered);
        Status = "해변을 받아오는 중";
        return true;
    }

    public void Detach(BeachSearch beach)
    {
        if (world==beach) world = null;
    }

    // The host is moving everyone to another beach.
    public void AnnounceVisit(BeachTheme theme)
    {
        if (!Hosting) return;
        synced.Clear();
        moving = world.Started || world.Theme!=theme;
        foreach (ulong guest in manager.ConnectedClientsIds)
            if (guest!=manager.LocalClientId) Send(Kind.Visit,0,new[]{(byte)theme},guest,Ordered);
    }

    void Broadcast(byte[] message)
    {
        if (Hosting) { foreach (ulong guest in synced) Send(Kind.World,world.MyId,message,guest,Ordered); }
        else if (caughtUp)
            Send(Kind.World,world.MyId,message,NetworkManager.ServerClientId,Ordered);
    }

    void SendSnapshot(ulong guest)
    {
        byte[] beach = world.WriteSnapshot();
        int pieces = (beach.Length+Chunk-1)/Chunk;
        for (int i=0; i<pieces; i++)
        {
            int size = Mathf.Min(Chunk,beach.Length-i*Chunk);
            var piece = new byte[size+2];
            piece[0] = (byte)i;
            piece[1] = (byte)pieces;
            System.Buffer.BlockCopy(beach,i*Chunk,piece,2,size);
            Send(Kind.Snapshot,0,piece,guest,Ordered);
        }
        synced.Add(guest);
    }

    void Send(Kind kind,int sender,byte[] body,ulong to,NetworkDelivery delivery)
    {
        using var writer = new FastBufferWriter(body.Length+8,Allocator.Temp);
        writer.WriteByteSafe((byte)kind);
        writer.WriteByteSafe((byte)sender);
        writer.WriteValueSafe(body.Length);
        if (body.Length>0) writer.WriteBytesSafe(body);
        manager.CustomMessagingManager.SendNamedMessage(Channel,to,writer,delivery);
    }

    void Hear(ulong from,FastBufferReader reader)
    {
        reader.ReadByteSafe(out byte kind);
        reader.ReadByteSafe(out byte sender);
        reader.ReadValueSafe(out int length);
        var body = new byte[length];
        if (length>0) reader.ReadBytesSafe(ref body,length);
        // A guest's word about who it is counts for nothing; the connection says.
        if (Hosting) sender = (byte)from;

        switch ((Kind)kind)
        {
            case Kind.LobbyChoice:
                if (Hosting && lobby.ContainsKey(from) && body.Length==2)
                {
                    lobby[from]=new LobbyMember(from,Mathf.Clamp(body[0],0,BeachBody.LookCount-1),body[1]!=0);
                    PublishLobby();
                }
                return;
            case Kind.LobbyRoster:
                if (!Hosting && from==NetworkManager.ServerClientId) ReadLobby(body);
                return;
            case Kind.World:
                if (!world) return;
                if (Hosting)
                {
                    if (!synced.Contains(from)) return;
                    if (world.Receive(body,sender))
                        foreach (ulong guest in synced)
                            if (guest!=from) Send(Kind.World,sender,body,guest,Ordered);
                }
                else if (caughtUp) world.Receive(body,sender);
                return;
            case Kind.Hello:
                if (Hosting) return;
                if (body[1]!=0) Follow((BeachTheme)body[0]);
                else Describe();
                return;
            case Kind.Visit:
                if (!Hosting) Follow((BeachTheme)body[0]);
                return;
            case Kind.Ready:
                if (!Hosting) return;
                if (world && world.Initialized && !moving) SendSnapshot(from); else waiting.Add(from);
                return;
            case Kind.Snapshot:
                if (!Hosting) Piece(body);
                return;
            case Kind.Pose:
                Posed(sender,body);
                if (Hosting)
                    foreach (ulong guest in synced)
                        if (guest!=from) Send(Kind.Pose,sender,body,guest,NetworkDelivery.UnreliableSequenced);
                return;
            case Kind.Left:
                if (!Hosting) { Forget(sender); Describe(); }
                return;
            case Kind.Voice:
                voice.Heard(sender,body);
                if (Hosting)
                    foreach (ulong id in manager.ConnectedClientsIds)
                        if (id!=from && id!=manager.LocalClientId) Send(Kind.Voice,sender,body,id,NetworkDelivery.Unreliable);
                return;
        }
    }

    // A guest goes to the beach the host chose. Its scene asks for the snapshot once it has loaded.
    void Follow(BeachTheme theme)
    {
        caughtUp = false;
        arriving = null;
        Status = "방장의 해변으로 가는 중";
        BeachSearch.FollowHost(theme);
    }

    void Piece(byte[] piece)
    {
        if (piece[0]==0) arriving = new MemoryStream();
        if (arriving==null) return;
        arriving.Write(piece,2,piece.Length-2);
        if (piece[0]+1<piece[1]) return;
        byte[] beach = arriving.ToArray();
        arriving = null;
        if (!world || !world.ReadSnapshot(beach))
        {
            Leave("방장의 해변과 맞지 않는다 · 같은 버전인지 확인");
            return;
        }
        caughtUp = true;
        world.Begin();
        Describe();
    }

    // ---- The other players ----

    void Update()
    {
        if (Typing && Keyboard.current!=null)
        {
            if (Keyboard.current.backspaceKey.wasPressedThisFrame && Address.Length>0) Address = Address.Substring(0,Address.Length-1);
            if (Keyboard.current.enterKey.wasPressedThisFrame) Join();
            if (Keyboard.current.escapeKey.wasPressedThisFrame) CancelTyping();
        }
        if (!Online || !world || !world.Started) return;
        if (Joined && !caughtUp) return;
        poseDue -= Time.unscaledDeltaTime;
        if (poseDue>0) return;
        poseDue = 1f/15f;
        var me = world.player.transform;
        using var body = new MemoryStream(20);
        using var w = new BinaryWriter(body);
        w.Write(me.position);
        w.Write(me.eulerAngles.y);
        w.Write(world.Local.Collapsed);
        // What is in this player's hands, so the others see who is digging, scanning or carrying what.
        w.Write(world.Local.InHand.Code);
        w.Write((byte)((world.Local.Loaded ? 1 : 0) | (world.Local.Working ? 2 : 0)));
        w.Flush();
        byte[] pose = body.ToArray();
        if (Hosting) { foreach (ulong guest in synced) Send(Kind.Pose,world.MyId,pose,guest,NetworkDelivery.UnreliableSequenced); }
        else Send(Kind.Pose,world.MyId,pose,NetworkManager.ServerClientId,NetworkDelivery.UnreliableSequenced);
    }

    void Posed(int id,byte[] body)
    {
        if (!world || (Joined && !caughtUp)) return;
        using var r = new BinaryReader(new MemoryStream(body));
        Vector3 position = r.ReadVector3();
        float yaw = r.ReadSingle();
        bool fallen = r.ReadBoolean();
        var slot = default(BeachSlot);
        byte flags = 0;
        if (body.Length>=19) { slot = BeachSlot.FromCode(r.ReadByte()); flags = r.ReadByte(); }
        if (!ghosts.TryGetValue(id,out var ghost) || !ghost)
        {
            ghosts[id] = ghost = BeachGhost.Create(world.transform,id,position);
            Describe();
        }
        if(lobby.TryGetValue((ulong)id,out var member)) ghost.Wear(member.Look);
        ghost.MoveTo(position,yaw,fallen);
        ghost.Hold(slot,(flags&1)!=0,(flags&2)!=0,world);
    }

    // The other players' figures and where they stand, for the host's volleyball.
    IEnumerable<(int id,Vector3 feet)> Standing()
    {
        foreach (var pair in ghosts)
            if (pair.Value) yield return (pair.Key,pair.Value.transform.position);
    }
}
