using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The cleanup sacks and the team's gold. Finds are gathered in sacks on the sand rather than in each
// player's bag, so the team decides where to keep them and who carries them to the stall. Everything here
// is shared: a find put in, a sack lifted or set down, sold or sorted, bought, and gold spent.
public sealed partial class BeachSearch
{
    public readonly List<BeachSack> Sacks = new();
    // Gold the team has spent. What it can still spend is what it has earned less this.
    public int Spent { get; private set; }
    public int Gold => Earned-Spent;

    public BeachSack CarriedSack => Sacks.Find(sack=>sack.Carrier==MyId);
    public BeachSack SackOf(Collider other) => Sacks.Find(sack=>sack.Is(other));

    // The sacks that count as at the stall: the one this player carries there, and any set down beside it.
    public IEnumerable<BeachSack> SacksAtStall
    {
        get
        {
            foreach (var sack in Sacks)
                if (sack.NearStall || (sack.Carrier==MyId && Local.NearShop)) yield return sack;
        }
    }

    // On the sand in front of the stall.
    Vector3 StallSpot(int index)
    {
        Vector2 stand = new Vector2(stall.position.x,stall.position.z);
        Vector2 toward = (-stand).normalized;
        Vector2 across = new Vector2(-toward.y,toward.x);
        Vector2 spot = sand.ClampToBeach(stand+toward*2.1f+across*(.55f*index-.8f),.35f);
        return Surface(spot);
    }

    void PlaceFirstSack()
    {
        foreach (var sack in Sacks) if (sack) Destroy(sack.gameObject);
        Sacks.Clear();
        Spent = 0;
        AddSack(StallSpot(0));
    }

    BeachSack AddSack(Vector3 at)
    {
        var sack = BeachSack.Create(this,Sacks.Count,at);
        Sacks.Add(sack);
        return sack;
    }

    // ---- Gold ----

    public bool Pay(int price)
    {
        if (Gold<price) return false;
        Spent += price;
        if (Outbox!=null) { Pack(Op.Spent).Write(price); Send(); }
        return true;
    }

    public bool BuySack()
    {
        if (Sacks.Count>=BeachSack.MostSacks || !Pay(BeachSack.Price)) return false;
        Vector3 at = StallSpot(Sacks.Count);
        AddSack(at);
        if (Outbox!=null) { Pack(Op.SackBought).Write(at); Send(); }
        return true;
    }

    // ---- Using a sack ----

    public bool PutInSack(BeachSack sack,FindKind kind)
    {
        if (!sack.Add(kind)) return false;
        if (Outbox!=null) { var w = Pack(Op.SackAdd); w.Write((byte)sack.Index); w.Write((byte)kind); Send(); }
        return true;
    }

    // Alone or as host the sack is lifted at once; a guest asks, and the host settles two hands on one sack.
    public void LiftSack(BeachSack sack)
    {
        if (sack.Carried) return;
        if (Guest && Outbox!=null)
        {
            Pack(Op.SackLiftRequest).Write((byte)sack.Index);
            Send();
            return;
        }
        GiveSack(sack,MyId);
    }

    void GiveSack(BeachSack sack,int who)
    {
        sack.Lift(who);
        if (who==MyId) Local.LiftedSack();
        if (Outbox==null) return;
        var w = Pack(Op.SackCarrier); w.Write((byte)sack.Index); w.Write((byte)who);
        Send();
    }

    public bool SetDownSack(BeachSack sack,Vector2 spot)
    {
        if (sack.Carrier!=MyId || !sack.CanSetDown(spot)) return false;
        PutDown(sack,Surface(spot));
        return true;
    }

    void PutDown(BeachSack sack,Vector3 ground)
    {
        sack.SetDown(ground);
        if (Outbox==null) return;
        var w = Pack(Op.SackDown); w.Write((byte)sack.Index); w.Write(ground);
        Send();
    }

    public int SellSack(BeachSack sack)
    {
        int gold = sack.SellAll();
        if (Outbox!=null) { Pack(Op.SackSold).Write((byte)sack.Index); Send(); }
        if (gold>0) AddEarnings(gold);
        return gold;
    }

    public int SortSack(BeachSack sack,WasteBin bin)
    {
        int sorted = sack.Sort(bin);
        if (sorted==0) return 0;
        if (Outbox!=null) { var w = Pack(Op.SackSorted); w.Write((byte)sack.Index); w.Write((byte)bin); Send(); }
        AddEarnings(sorted*BeachInventory.SortedPrice,sorted);
        return sorted;
    }

    // Someone gone or no longer here: a sack they carried drops where they last stood.
    void DropSacksOf(int who,bool announce)
    {
        foreach (var sack in Sacks)
        {
            if (sack.Carrier!=who) continue;
            Vector3 p = sack.transform.position;
            Vector2 spot = sand.ClampToBeach(new Vector2(p.x,p.z),.3f);
            if (announce) PutDown(sack,Surface(spot)); else sack.SetDown(Surface(spot));
        }
    }

    // Messages about sacks and gold. Returns null when `op` is something else, else whether to pass it on.
    bool? ReceiveSack(Op op,BinaryReader r,int from)
    {
        switch (op)
        {
            case Op.Spent:
                Spent += r.ReadInt32();
                return true;
            case Op.SackBought:
                if (Sacks.Count<BeachSack.MostSacks*2) AddSack(r.ReadVector3());
                return true;
            case Op.SackAdd:
            {
                int index = r.ReadByte();
                var kind = (FindKind)r.ReadByte();
                if (index<Sacks.Count) Sacks[index].Add(kind,true);
                return true;
            }
            case Op.SackLiftRequest:
            {
                int index = r.ReadByte();
                if (!Guest && index<Sacks.Count && !Sacks[index].Carried) GiveSack(Sacks[index],from);
                return false;
            }
            case Op.SackCarrier:
            {
                int index = r.ReadByte(), who = r.ReadByte();
                if (!Guest || index>=Sacks.Count) return false;
                if (who==MyId && !Local.CanLiftSack) { Sacks[index].Lift(who); PutDown(Sacks[index],Sacks[index].transform.position); return false; }
                Sacks[index].Lift(who);
                if (who==MyId) Local.LiftedSack();
                return false;
            }
            case Op.SackDown:
            {
                int index = r.ReadByte();
                Vector3 ground = r.ReadVector3();
                if (index<Sacks.Count) Sacks[index].SetDown(ground);
                return true;
            }
            case Op.SackSold:
            {
                int index = r.ReadByte();
                if (index<Sacks.Count) Sacks[index].SellAll();
                return true;
            }
            case Op.SackSorted:
            {
                int index = r.ReadByte();
                var bin = (WasteBin)r.ReadByte();
                if (index<Sacks.Count) Sacks[index].Sort(bin);
                return true;
            }
        }
        return null;
    }

    void WriteSacks(BinaryWriter w)
    {
        w.Write(Spent);
        w.Write((byte)Sacks.Count);
        foreach (var sack in Sacks)
        {
            w.Write(sack.transform.position);
            w.Write((sbyte)sack.Carrier);
            w.Write((byte)sack.Contents.Count);
            foreach (var pair in sack.Contents) { w.Write((byte)pair.Key); w.Write((byte)pair.Value); }
        }
    }

    void ReadSacks(BinaryReader r)
    {
        Spent = r.ReadInt32();
        int count = r.ReadByte();
        while (Sacks.Count<count) AddSack(StallSpot(Sacks.Count));
        for (int i=0; i<count; i++)
        {
            var sack = Sacks[i];
            Vector3 at = r.ReadVector3();
            int carrier = r.ReadSByte();
            sack.Clear();
            for (int n = r.ReadByte(); n>0; n--)
            {
                var kind = (FindKind)r.ReadByte();
                for (int c = r.ReadByte(); c>0; c--) sack.Add(kind,true);
            }
            sack.SetDown(at);
            if (carrier>=0) sack.Lift(carrier);
        }
    }
}
