using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public sealed partial class BeachNet
{
    public readonly struct LobbyMember
    {
        public readonly ulong Id;
        public readonly int Look;
        public readonly bool Ready;
        public LobbyMember(ulong id,int look,bool ready) { Id=id; Look=look; Ready=ready; }
    }
    readonly SortedDictionary<ulong,LobbyMember> lobby=new();
    public IEnumerable<LobbyMember> Members => lobby.Values;
    public ulong LocalId => manager ? manager.LocalClientId : 0;
    public bool LocalReady => lobby.TryGetValue(LocalId,out var me) && me.Ready;
    public bool AllReady => Hosting && lobby.Count==manager.ConnectedClientsIds.Count && lobby.Values.All(m=>m.Ready);
    public static int SelectedLook { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLook() => SelectedLook=0;
    public static void SelectLook(int look)
    {
        SelectedLook=(look%BeachBody.LookCount+BeachBody.LookCount)%BeachBody.LookCount;
        var game=FindFirstObjectByType<BeachSearch>();
        if(game && game.player) game.player.Wear(SelectedLook);
        if(Instance && Instance.Online) Instance.SetLobbyChoice(SelectedLook,false);
    }
    public void SetLobbyChoice(int look,bool ready)
    {
        look=Mathf.Clamp(look,0,BeachBody.LookCount-1);
        if(Hosting)
        {
            lobby[LocalId]=new LobbyMember(LocalId,look,ready);
            PublishLobby();
        }
        else if(Joined && manager.IsConnectedClient)
            Send(Kind.LobbyChoice,0,new[]{(byte)look,(byte)(ready?1:0)},NetworkManager.ServerClientId,Ordered);
    }
    void PublishLobby()
    {
        if(!Hosting) return;
        using var buffer=new MemoryStream();
        using var writer=new BinaryWriter(buffer);
        writer.Write(lobby.Count);
        foreach(var m in lobby.Values) { writer.Write(m.Id); writer.Write(m.Look); writer.Write(m.Ready); }
        writer.Flush();
        foreach(var id in manager.ConnectedClientsIds)
            if(id!=LocalId) Send(Kind.LobbyRoster,0,buffer.ToArray(),id,Ordered);
    }
    void ReadLobby(byte[] bytes)
    {
        if(bytes.Length<4) return;
        using var reader=new BinaryReader(new MemoryStream(bytes));
        int count=reader.ReadInt32();
        if(count<0 || count>256 || bytes.Length!=4+count*13) return;
        lobby.Clear();
        for(int i=0;i<count;i++)
        {
            ulong id=reader.ReadUInt64();
            int look=reader.ReadInt32();
            bool ready=reader.ReadBoolean();
            lobby[id]=new LobbyMember(id,Mathf.Clamp(look,0,BeachBody.LookCount-1),ready);
        }
    }
}
