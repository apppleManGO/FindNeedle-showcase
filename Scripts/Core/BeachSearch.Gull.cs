using System.IO;
using UnityEngine;

// The seagull's effect on the shared beach: a find taken out of a sack, and later dropped on the sand
// for anyone to pick up again. The host's gull decides; these calls tell everyone.
public sealed partial class BeachSearch
{
    public BeachGull Gull { get; private set; }
    // Finds picked up off the sand. A gull's drop brings back one of these, never one still riding in
    // someone's scoop of sand.
    readonly System.Collections.Generic.HashSet<BeachFind> Collected = new();

    void ReleaseGull()
    {
        Gull = new GameObject("갈매기").AddComponent<BeachGull>();
        Gull.transform.SetParent(transform,false);
        Gull.Initialize(this);
    }

    public void SendGull(Vector3 position,float yaw,BeachGull.Phase phase,FindKind? carrying)
    {
        if (Outbox==null || Guest) return;
        var w = Pack(Op.GullState);
        w.Write(position); w.Write(yaw); w.Write((byte)phase);
        w.Write((sbyte)(carrying.HasValue ? (int)carrying.Value : -1));
        Send();
    }

    // The gull has taken `kind` out of `sack`.
    public void GullTook(BeachSack sack,FindKind kind)
    {
        if (Outbox==null || Guest) return;
        var w = Pack(Op.GullTook); w.Write((byte)sack.Index); w.Write((byte)kind);
        Send();
    }

    // The gull let go of `kind` over `spot`: it lies on the sand there, to be picked up again.
    public void GullDropped(FindKind kind,Vector2 spot)
    {
        BeachFind item = null;
        foreach (var picked in Collected) if (picked.kind==kind && !picked.gameObject.activeSelf) { item = picked; break; }
        if (!item) item = System.Array.Find(finds,f=>f.kind==kind && !f.gameObject.activeSelf);
        if (!item) return;
        PlaceDropped(item,spot);
        Notice($"갈매기가 {BeachFind.NameWithObject(kind)} 떨어뜨렸다 · 주우러 가자");
        if (Outbox==null || Guest) return;
        var w = Pack(Op.GullDropped); w.Write((byte)IndexOf(item)); w.Write(spot);
        Send();
    }

    void PlaceDropped(BeachFind item,Vector2 spot)
    {
        item.transform.position = Surface(spot)+Vector3.up*.012f;
        item.gameObject.SetActive(true);
        Collected.Remove(item);
        // It was counted when first picked up, and will be again.
        Picked = Mathf.Max(0,Picked-1);
        StartCoroutine(Glint(item));
    }

    // Returns null when `op` is not about the gull, else whether to pass it on.
    bool? ReceiveGull(Op op,BinaryReader r)
    {
        switch (op)
        {
            case Op.GullState:
            {
                Vector3 position = r.ReadVector3();
                float yaw = r.ReadSingle();
                var phase = (BeachGull.Phase)r.ReadByte();
                int carrying = r.ReadSByte();
                if (Guest && Gull) Gull.Heard(position,yaw,phase,carrying<0 ? null : (FindKind?)carrying);
                return false;
            }
            case Op.GullTook:
            {
                int index = r.ReadByte();
                var kind = (FindKind)r.ReadByte();
                if (Guest && index<Sacks.Count) Sacks[index].TakeOne(kind);
                return false;
            }
            case Op.GullDropped:
            {
                var item = finds[r.ReadByte()];
                Vector2 spot = r.ReadVector2();
                if (!Guest) return false;
                PlaceDropped(item,spot);
                Notice($"갈매기가 {BeachFind.NameWithObject(item.kind)} 떨어뜨렸다 · 주우러 가자");
                return false;
            }
        }
        return null;
    }
}
