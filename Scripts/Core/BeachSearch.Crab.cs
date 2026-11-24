using System.IO;
using UnityEngine;

// The crab's effect on the shared beach: a find taken off the sand, dropped again when someone came near,
// or buried at the burrow for anyone to dig up. The host's crab decides; these calls tell everyone.
public sealed partial class BeachSearch
{
    public BeachCrab Crab { get; private set; }

    // After the finds are laid out, so the crab knows which ones were on the surface from the start.
    void ReleaseCrab()
    {
        Crab = new GameObject("게").AddComponent<BeachCrab>();
        Crab.transform.SetParent(transform,false);
        Crab.Initialize(this);
    }

    public void SendCrab(Vector3 position,float yaw,BeachCrab.Phase phase,BeachFind carrying)
    {
        if (Outbox==null || Guest) return;
        var w = Pack(Op.CrabState);
        w.Write(position); w.Write(yaw); w.Write((byte)phase);
        w.Write((short)(carrying ? IndexOf(carrying) : -1));
        Send();
    }

    // The crab has taken `item` off the sand.
    public void CrabTook(BeachFind item)
    {
        if (Outbox==null || Guest) return;
        Pack(Op.CrabTook).Write((byte)IndexOf(item));
        Send();
    }

    // The crab let go of `item` at `at`, on top of the sand.
    public void CrabDropped(BeachFind item,Vector3 at)
    {
        PlaceCrabbed(item,at);
        Notice($"게가 {BeachFind.NameWithObject(item.kind)} 놓고 달아났다 · 얼른 줍자");
        if (Outbox==null || Guest) return;
        var w = Pack(Op.CrabDropped); w.Write((byte)IndexOf(item)); w.Write(at);
        Send();
    }

    // The crab buried `item` at `at`, under the sand at its burrow.
    public void CrabBuried(BeachFind item,Vector3 at)
    {
        PlaceCrabbed(item,at);
        if (Crab) Crab.MarkBuried(item);
        Notice($"게가 {BeachFind.NameWithObject(item.kind)} 굴에 숨겼다 · 구멍을 파 보자");
        if (Outbox==null || Guest) return;
        var w = Pack(Op.CrabBuried); w.Write((byte)IndexOf(item)); w.Write(at);
        Send();
    }

    void PlaceCrabbed(BeachFind item,Vector3 at)
    {
        item.transform.position = at;
        item.gameObject.SetActive(true);
        if (item.traces) item.traces.SetActive(false);
    }

    // Returns null when `op` is not about the crab, else whether to pass it on.
    bool? ReceiveCrab(Op op,BinaryReader r)
    {
        switch (op)
        {
            case Op.CrabState:
            {
                Vector3 position = r.ReadVector3();
                float yaw = r.ReadSingle();
                var phase = (BeachCrab.Phase)r.ReadByte();
                int carrying = r.ReadInt16();
                if (Guest && Crab) Crab.Heard(position,yaw,phase,carrying>=0 && carrying<finds.Length ? finds[carrying] : null);
                return false;
            }
            case Op.CrabTook:
            {
                var item = finds[r.ReadByte()];
                if (Guest) item.gameObject.SetActive(false);
                return false;
            }
            case Op.CrabDropped:
            {
                var item = finds[r.ReadByte()];
                Vector3 at = r.ReadVector3();
                if (!Guest) return false;
                PlaceCrabbed(item,at);
                Notice($"게가 {BeachFind.NameWithObject(item.kind)} 놓고 달아났다 · 얼른 줍자");
                return false;
            }
            case Op.CrabBuried:
            {
                var item = finds[r.ReadByte()];
                Vector3 at = r.ReadVector3();
                if (!Guest) return false;
                PlaceCrabbed(item,at);
                if (Crab) Crab.MarkBuried(item);
                Notice($"게가 {BeachFind.NameWithObject(item.kind)} 굴에 숨겼다 · 구멍을 파 보자");
                return false;
            }
        }
        return null;
    }

    // The finds lying in crab burrows, for a late arrival. Their positions travel with the other finds.
    void WriteCrab(BinaryWriter w)
    {
        var lying = new System.Collections.Generic.List<BeachFind>(Crab ? Crab.Buried : System.Array.Empty<BeachFind>());
        w.Write((byte)lying.Count);
        foreach (var item in lying) w.Write((byte)IndexOf(item));
    }

    void ReadCrab(BinaryReader r)
    {
        if (Crab) Crab.ForgetMarks();
        for (int n = r.ReadByte(); n>0; n--)
        {
            var item = finds[r.ReadByte()];
            if (Crab) Crab.MarkBuried(item);
        }
    }
}
