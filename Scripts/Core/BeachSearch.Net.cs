using System.Collections.Generic;
using System.IO;
using UnityEngine;

// How the shared beach travels between players. Every change made through BeachSearch is packed into
// one small message; the other players unpack it and make the same change. A player who joins late
// first receives the whole beach as a snapshot. Nothing here knows how the bytes are carried.
public sealed partial class BeachSearch
{
    public enum Op : byte
    {
        Footstep = 1, Leak, SweepStep, Scoop, Pour, FinishStroke, Taken, Record, Earnings, Parasol, Win, Clock,
        // Host to guests: the true heights of the tiles worked lately.
        Patch,
        // Guest to host only: asks to pick a find up. The host answers everyone with Taken.
        TakeRequest, Ping, TurtleRequest, TurtleChanged, RakeRequest, Raked, TakeDeclined,
        // The volleyball. Host to guests: where it is and who holds it, who just caught it, a serve, a hit.
        // Guest to host: may I catch it, and where I threw it.
        BallState, BallRequest, BallHolder, BallThrow, BallServed, BallHit,
        // Sacks and the team's gold. SackLiftRequest goes guest to host; SackCarrier is the host's answer.
        Spent, SackBought, SackAdd, SackLiftRequest, SackCarrier, SackDown, SackSold, SackSorted,
        // Host to guests: the seagull's position and phase, a find it took from a sack, and where it let go.
        GullState, GullTook, GullDropped,
        // Host to guests: the crab's position and phase, a find it took off the sand, let go of, or buried.
        CrabState, CrabTook, CrabDropped, CrabBuried,
        // Anyone to everyone: a wave, a cheer, a point or a shake of the head (keys 1-4).
        Gesture
    }

    // Set while in a session. Receives each change made on this machine, ready to send.
    public System.Action<byte[]> Outbox;
    // True for everyone but the host, who owns the clock and settles who picked what up.
    public bool Guest;
    // This player's number in the session; the host is 0.
    public int MyId;
    // Set while in a session: the other players and where they stand, so the host's ball can hit them.
    public System.Func<IEnumerable<(int id,Vector3 feet)>> Others;
    // Set while in a session: the other players' figures by player number.
    public System.Func<IReadOnlyDictionary<int,BeachGhost>> Team;
    float clockSent, patchSent;

    // ---- The volleyball ----

    public void SendBall(Vector3 position,int holder)
    {
        if (Outbox==null || Guest) return;
        var w = Pack(Op.BallState); w.Write(position); w.Write((sbyte)holder);
        Send();
    }

    public void AskForBall()
    {
        if (Outbox==null) return;
        Pack(Op.BallRequest);
        Send();
    }

    public void AnnounceBallHolder(int holder)
    {
        if (Outbox==null || Guest) return;
        var w = Pack(Op.BallHolder); w.Write((byte)holder);
        Send();
    }

    public void ThrowBall(Vector3 from,Vector3 direction)
    {
        if (Outbox==null) return;
        var w = Pack(Op.BallThrow); w.Write(from); w.Write(direction);
        Send();
    }

    public void BallServed()
    {
        WarnOfBall();
        if (Outbox==null || Guest) return;
        Pack(Op.BallServed);
        Send();
    }

    void WarnOfBall()
    {
        Notice("코트 쪽에서 공이 날아온다!");
        PlayBallWarning();
    }

    // The served ball reached player `who`.
    public void HitByBall(int who)
    {
        if (who==MyId) { HurtByBall(); return; }
        if (Outbox==null || Guest) return;
        var w = Pack(Op.BallHit); w.Write((byte)who);
        Send();
    }

    void HurtByBall()
    {
        player.HitByBall();
        Notice("날아온 공에 맞았다 · 체력 -10");
    }

    // Host: a player has left this beach.
    public void PlayerLeft(int who)
    {
        if (Volleyball) Volleyball.Left(who);
        DropSacksOf(who,true);
    }

    // The session is over and this beach is played alone again.
    public void SessionEnded()
    {
        if (Volleyball) Volleyball.Unshare();
        foreach (var sack in Sacks)
            if (sack.Carried && sack.Carrier!=MyId) DropSacksOf(sack.Carrier,false);
        Others = null;
        Team = null;
    }

    // Each machine slides its own sand, and two players' strokes can land in a different order on each.
    // Twice a second the host therefore sends the tiles that changed, and guests take its word for them.
    void SendSandPatch()
    {
        var grid = sand.Grid;
        BinaryWriter w = null;
        for (int tile=0; tile<grid.TileCount; tile++)
        {
            if (!grid.TakeUnsent(tile)) continue;
            w ??= Pack(Op.Patch);
            w.Write((byte)(tile+1));
            int x0 = tile%grid.TilesAcross*SandGrid.Tile, z0 = tile/grid.TilesAcross*SandGrid.Tile;
            for (int z=z0; z<z0+SandGrid.Tile && z<=grid.Rows; z++)
            for (int x=x0; x<x0+SandGrid.Tile && x<=grid.Columns; x++)
            {
                int i = grid.Index(x,z);
                w.Write(grid.Heights[i]);
                w.Write((byte)(Mathf.Clamp01(grid.Damp[i])*255f));
            }
        }
        if (w==null) return;
        w.Write((byte)0);
        Send();
    }

    void ReadSandPatch(BinaryReader r)
    {
        var grid = sand.Grid;
        for (int tile = r.ReadByte(); tile>0; tile = r.ReadByte())
        {
            tile--;
            int x0 = tile%grid.TilesAcross*SandGrid.Tile, z0 = tile/grid.TilesAcross*SandGrid.Tile;
            for (int z=z0; z<z0+SandGrid.Tile && z<=grid.Rows; z++)
            for (int x=x0; x<x0+SandGrid.Tile && x<=grid.Columns; x++)
            {
                float height = r.ReadSingle();
                grid.SetCell(x,z,height,r.ReadByte()/255f);
            }
        }
        SurfaceWorked();
    }

    MemoryStream packing;
    BinaryWriter packer;

    BinaryWriter Pack(Op op)
    {
        packing ??= new MemoryStream(64);
        packer ??= new BinaryWriter(packing);
        packer.Flush();
        packing.SetLength(0);
        packer.Write((byte)op);
        return packer;
    }

    void Send()
    {
        packer.Flush();
        Outbox(packing.ToArray());
    }

    int IndexOf(BeachFind item) => System.Array.IndexOf(finds,item);

    // Makes a change another player announced. Returns true when the host should pass it on to the rest.
    public bool Receive(byte[] message,int from)
    {
        using var r = new BinaryReader(new MemoryStream(message));
        var op = (Op)r.ReadByte();
        if (ReceiveSack(op,r,from) is bool relay) return relay;
        if (ReceiveGull(op,r) is bool passOn) return passOn;
        if (ReceiveCrab(op,r) is bool tell) return tell;
        switch (op)
        {
            case Op.TurtleRequest:
                if (!Guest) AdvanceTurtle((BeachTurtle.Stage)r.ReadByte(),from);
                return false;
            case Op.TurtleChanged:
                if (Guest) ApplyTurtle((BeachTurtle.Stage)r.ReadByte(),r.ReadInt32());
                return false;
            case Op.RakeRequest:
                if (!Guest) Rake(r.ReadVector2(),r.ReadVector2());
                return false;
            case Op.Raked:
                if (!Guest) return false;
                RakeSand(r.ReadVector2(),r.ReadVector2());
                for (int n=r.ReadByte(); n>0; n--)
                    finds[r.ReadByte()].transform.position = r.ReadVector3();
                SurfaceWorked();
                return false;
            case Op.Ping:
                ShowPing(from,r.ReadVector2());
                return true;
            case Op.Gesture:
                ShowSign(from,(BeachBody.Gesture)r.ReadByte());
                return true;
            case Op.Footstep:
                DoFootstep(r.ReadVector2(),r.ReadVector2());
                sand.Refresh();
                return true;
            case Op.Leak:
            {
                Vector3 source = r.ReadVector3();
                Vector2 drop = r.ReadVector2();
                DoLeak(source,drop,r.ReadSingle());
                sand.Refresh();
                return true;
            }
            case Op.SweepStep:
            {
                Vector2 centre = r.ReadVector2(), across = r.ReadVector2();
                float distance = r.ReadSingle();
                DoSweepStep(centre,across,distance,r.ReadBoolean());
                SurfaceWorked();
                return true;
            }
            case Op.Scoop:
            {
                Vector2 spot = r.ReadVector2();
                float radius = r.ReadSingle(), depth = r.ReadSingle();
                bool markTrowel = r.ReadBoolean();
                Vector2 forward = r.ReadVector2();
                sand.Scrape(spot,radius,depth);
                if (markTrowel) sand.MarkTrowel(spot,forward);
                // The digger says which finds came up, so a slightly different surface here cannot disagree.
                for (int n = r.ReadByte(); n>0; n--) Lift(finds[r.ReadByte()]);
                sand.Kick(spot,12,radius*.6f,.9f);
                return true;
            }
            case Op.Pour:
            {
                Vector2 spot = r.ReadVector2();
                float narrow = r.ReadSingle(), radius = r.ReadSingle(), load = r.ReadSingle();
                Vector3 source = r.ReadVector3();
                sand.Fall(source,spot,.38f,260f,narrow*.55f);
                SandSound(pourSound,.6f);
                sand.Pour(spot,load,radius);
                for (int n = r.ReadByte(); n>0; n--)
                {
                    var item = finds[r.ReadByte()];
                    Vector3 position = r.ReadVector3();
                    Land(item,position,r.ReadBoolean());
                }
                return true;
            }
            case Op.FinishStroke:
            {
                Vector2 spot = r.ReadVector2();
                float radius = r.ReadSingle();
                DoFinishStroke(spot,radius,r.ReadBoolean());
                return true;
            }
            case Op.TakeRequest:
            {
                if (Guest) return false;
                var item = finds[r.ReadByte()];
                if (!item.gameObject.activeSelf)
                {
                    var denied=Pack(Op.TakeDeclined);
                    denied.Write((byte)IndexOf(item)); denied.Write((byte)from); Send();
                    return false;
                }
                item.gameObject.SetActive(false);
                Collected.Add(item);
                var w = Pack(Op.Taken); w.Write((byte)IndexOf(item)); w.Write((byte)from);
                Send();
                return false;
            }
            case Op.TakeDeclined:
            {
                var item=finds[r.ReadByte()];
                if (Guest && r.ReadByte()==MyId) FinishTakeRequest(item);
                return false;
            }
            case Op.Taken:
            {
                var item = finds[r.ReadByte()];
                int taker = r.ReadByte();
                item.gameObject.SetActive(false);
                Collected.Add(item);
                FinishTakeRequest(item);
                if (taker==MyId) Local.Granted(item);
                return false;
            }
            case Op.Record:
                DoRecord((FindKind)r.ReadByte(),.35f);
                return true;
            case Op.Earnings:
                Earned += r.ReadInt32();
                if (r.BaseStream.Position<r.BaseStream.Length) Sorted += r.ReadInt32();
                return true;
            case Op.Parasol:
                DoPitchParasol(r.ReadVector2(),.25f);
                return true;
            case Op.Win:
                if (Won) return false;
                Elapsed = r.ReadSingle();
                DoWin(r.BaseStream.Position<r.BaseStream.Length ? r.ReadByte() : from);
                return true;
            case Op.Clock:
                if (Guest) Elapsed = r.ReadSingle();
                return false;
            case Op.Patch:
                if (Guest) ReadSandPatch(r);
                return false;
            case Op.BallState:
                if (Guest && Volleyball) Volleyball.Heard(r.ReadVector3(),r.ReadSByte());
                return false;
            case Op.BallRequest:
                if (!Guest && Volleyball) Volleyball.Requested(from);
                return false;
            case Op.BallHolder:
            {
                if (!Guest || !Volleyball) return false;
                int holder = r.ReadByte();
                if (holder!=MyId) Volleyball.Grab(holder);
                else if (Local.CanTakeBall) Local.TookBall();
                // Hands full by the time the answer came: give the ball straight back.
                else { Volleyball.Grab(MyId); Volleyball.Throw(Vector3.down); }
                return false;
            }
            case Op.BallThrow:
            {
                if (Guest || !Volleyball) return false;
                Vector3 origin = r.ReadVector3();
                Volleyball.ThrownBy(from,origin,r.ReadVector3());
                return false;
            }
            case Op.BallServed:
                if (Guest) WarnOfBall();
                return false;
            case Op.BallHit:
                if (Guest && r.ReadByte()==MyId) HurtByBall();
                return false;
        }
        Debug.LogWarning("모르는 해변 메시지: "+(byte)op);
        return false;
    }

    const float RakeRadius = .24f;

    void RakeSand(Vector2 spot,Vector2 toward)
    {
        Vector2 across = new Vector2(-toward.y,toward.x);
        float sandMoved = 0;
        for (int row=0; row<3; row++)
        for (int tine=-2; tine<=2; tine++)
        {
            Vector2 center = spot+toward*(row*.15f)+across*(tine*(.18f-row*.02f));
            sandMoved += sand.Grid.Scrape(center,RakeRadius,.012f,-.045f);
        }
        sand.Pour(sand.ClampToBeach(spot+toward*.62f,.3f),sandMoved,.34f);
        sand.Kick(spot,12,.35f,.5f);
    }

    public void Rake(Vector2 spot,Vector2 toward)
    {
        if (Guest && Outbox!=null)
        {
            var request = Pack(Op.RakeRequest);
            request.Write(spot); request.Write(toward);
            Send();
            return;
        }
        RakeSand(spot,toward);
        var moved = new List<BeachFind>();
        foreach (var item in finds)
        {
            if (!item.gameObject.activeSelf || !item.CanHook || !item.Exposed(sand)) continue;
            Vector2 offset = item.Position-spot;
            float along = Vector2.Dot(offset,toward);
            if (along < -.12f || along > .49f || (offset-along*toward).magnitude > .48f-along*.25f) continue;
            Vector2 drop = sand.ClampToBeach(item.Position+toward*(.68f-along),.18f);
            item.transform.position = Surface(drop)+Vector3.up*.01f;
            moved.Add(item);
        }
        SurfaceWorked();
        if (Outbox==null) return;
        var w = Pack(Op.Raked);
        w.Write(spot); w.Write(toward); w.Write((byte)moved.Count);
        foreach (var item in moved) { w.Write((byte)IndexOf(item)); w.Write(item.transform.position); }
        Send();
    }

    // The whole beach in one block, for a player who has just arrived.
    public byte[] WriteSnapshot()
    {
        using var stream = new MemoryStream(sand.Heights.Length*5+1024);
        using var w = new BinaryWriter(stream);
        w.Write((byte)Theme);
        w.Write(Elapsed);
        w.Write(Picked);
        w.Write(Earned);
        w.Write((byte)cleaned.Count);
        foreach (var entry in cleaned) { w.Write((byte)entry.Key); w.Write(entry.Value); }
        w.Write((byte)finds.Length);
        foreach (var item in finds)
        {
            w.Write(item.gameObject.activeSelf);
            w.Write(item.transform.position);
            w.Write(item.transform.rotation.eulerAngles);
            w.Write((bool)item.traces);
            if (item.traces) { w.Write(item.traces.activeSelf); w.Write(item.traces.transform.position); }
        }
        w.Write((sbyte)(buriedPourNeedle ? IndexOf(buriedPourNeedle) : -1));
        w.Write((bool)portableParasol);
        if (portableParasol) w.Write(portableParasol.position);
        float[] heights = sand.Heights, damp = sand.Damp;
        w.Write(heights.Length);
        var raw = new byte[heights.Length*4];
        System.Buffer.BlockCopy(heights,0,raw,0,raw.Length);
        w.Write(raw);
        foreach (float d in damp) w.Write((byte)(Mathf.Clamp01(d)*255f));
        if (Turtle) w.Write((byte)Turtle.Current);
        w.Write(Sorted);
        WriteSacks(w);
        WriteCrab(w);
        w.Flush();
        return stream.ToArray();
    }

    // Makes this beach match the one the snapshot was taken from. False when it is a different beach.
    public bool ReadSnapshot(byte[] snapshot)
    {
        using var r = new BinaryReader(new MemoryStream(snapshot));
        if ((BeachTheme)r.ReadByte()!=Theme) return false;
        Elapsed = r.ReadSingle();
        Picked = r.ReadInt32();
        Earned = r.ReadInt32();
        cleaned.Clear();
        for (int n = r.ReadByte(); n>0; n--) { var kind = (FindKind)r.ReadByte(); cleaned[kind] = r.ReadInt32(); }
        if (r.ReadByte()!=finds.Length) return false;
        foreach (var item in finds)
        {
            item.gameObject.SetActive(r.ReadBoolean());
            item.transform.position = r.ReadVector3();
            item.transform.rotation = Quaternion.Euler(r.ReadVector3());
            if (!r.ReadBoolean()) continue;
            bool shown = r.ReadBoolean();
            Vector3 shells = r.ReadVector3();
            if (!item.traces) continue;
            item.traces.SetActive(shown);
            item.traces.transform.position = shells;
        }
        if (buriedPourNeedle) SetNeedleVisible(buriedPourNeedle,true);
        int hidden = r.ReadSByte();
        buriedPourNeedle = hidden<0 ? null : finds[hidden];
        if (buriedPourNeedle) SetNeedleVisible(buriedPourNeedle,false);
        if (r.ReadBoolean())
        {
            Vector3 pole = r.ReadVector3();
            DoPitchParasol(new Vector2(pole.x,pole.z),0f);
        }
        int cells = r.ReadInt32();
        if (cells!=sand.Heights.Length) return false;
        var heights = new float[cells];
        System.Buffer.BlockCopy(r.ReadBytes(cells*4),0,heights,0,cells*4);
        var damp = new float[cells];
        for (int i=0; i<cells; i++) damp[i] = r.ReadByte()/255f;
        if (Turtle) Turtle.SetStage((BeachTurtle.Stage)r.ReadByte(),false);
        Sorted = r.BaseStream.Length-r.BaseStream.Position>=sizeof(int) ? r.ReadInt32() : 0;
        if (r.BaseStream.Length-r.BaseStream.Position>=sizeof(int)+1) ReadSacks(r);
        sand.Restore(heights,damp);
        // After the sand, so the burrow marks sit on it.
        if (r.BaseStream.Length-r.BaseStream.Position>=1) ReadCrab(r);
        return true;
    }
}

// Unity vectors on a plain BinaryWriter and BinaryReader.
public static class BeachWire
{
    public static void Write(this BinaryWriter w,Vector2 v) { w.Write(v.x); w.Write(v.y); }
    public static void Write(this BinaryWriter w,Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    public static Vector2 ReadVector2(this BinaryReader r) { float x = r.ReadSingle(); return new Vector2(x,r.ReadSingle()); }
    public static Vector3 ReadVector3(this BinaryReader r)
    {
        float x = r.ReadSingle(), y = r.ReadSingle();
        return new Vector3(x,y,r.ReadSingle());
    }
}
