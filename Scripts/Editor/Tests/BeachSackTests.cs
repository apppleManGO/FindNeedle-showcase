using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Gathering finds in sacks: into a sack a hand at a time, carried to the stall to sell and to the bins to
// sort, more sacks bought with the team's gold, and all of it the same on every player's beach.
public class BeachSackTests
{
    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    static void StandAtStall(BeachSearch game)
    {
        Vector3 stall = game.stall.position;
        Vector3 toward = -new Vector3(stall.x,0,stall.z).normalized;
        game.player.transform.position = stall+toward*1.5f;
        game.player.Follow();
    }

    [UnityTest] public IEnumerator FindsGoInASackThatIsCarriedToTheStall()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var me = game.Local;
        var bag = game.inventory;
        Assert.AreEqual(1,game.Sacks.Count,"each beach starts with one sack");
        var sack = game.Sacks[0];
        Assert.IsTrue(sack.NearStall,"the first sack should wait by the stall");

        bag.Store(FindKind.Coin);
        me.UseSack(sack);
        Assert.AreEqual(1,sack.Count);
        Assert.IsFalse(bag.Hand.HasValue,"the coin stayed in hand");
        bag.Store(FindKind.PlasticBottle);
        me.UseSack(sack);
        Assert.AreEqual(2,sack.Count);
        Assert.AreEqual(BeachFind.PriceOf(FindKind.Coin)+1,sack.Value);

        // Empty-handed, E lifts it. A carried sack slows its carrier as it fills.
        me.UseSack(sack);
        Assert.AreSame(sack,game.CarriedSack);
        Assert.AreEqual(BeachGear.Sack,me.InHand.Gear);
        Assert.Less(sack.Burden,1f);
        Assert.IsFalse(me.CanTakeBall,"a full pair of hands cannot catch");

        // Litter goes in its bin straight from the sack; the rest sells at the stall.
        Assert.AreEqual(1,game.SortSack(sack,WasteBin.Bottle));
        Assert.AreEqual(1,game.Sorted);
        StandAtStall(game);
        Assert.IsTrue(me.NearShop);
        Assert.AreEqual(BeachFind.PriceOf(FindKind.Coin),me.SaleValue);
        me.Sell();
        Assert.AreEqual(0,sack.Count);
        Assert.AreEqual(BeachFind.PriceOf(FindKind.Coin)+BeachInventory.SortedPrice,game.Gold);

        // Bought with the team's gold, the second sack appears at the stall.
        int before = game.Gold;
        me.BuySack();
        Assert.AreEqual(2,game.Sacks.Count);
        Assert.AreEqual(before-BeachSack.Price,game.Gold);
        Assert.AreEqual(before-BeachSack.Price,bag.Wallet,"the hotbar and the beach disagree about the gold");

        Vector2 spot = game.sand.ClampToBeach(new Vector2(1.2f,.8f),.4f);
        Assert.IsTrue(game.SetDownSack(sack,spot));
        Assert.IsFalse(sack.Carried);

        // Twelve finds fill it; the thirteenth stays out.
        for (int i=0; i<BeachSack.Capacity; i++) Assert.IsTrue(game.PutInSack(sack,FindKind.Cap));
        Assert.IsTrue(sack.Full);
        Assert.IsFalse(game.PutInSack(sack,FindKind.Cap));
        Assert.AreEqual(BeachSack.Capacity,sack.Count);
        yield return new ExitPlayMode();
    }

    static byte[] lastSent;
    static void Keep(byte[] message) => lastSent = message;
    static readonly List<byte[]> sent = new();
    static void Collect(byte[] message) => sent.Add(message);

    [UnityTest] public IEnumerator TheHostSettlesWhoLiftsASack()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var sack = game.Sacks[0];
        sent.Clear();
        game.MyId = 0;
        game.Guest = false;
        game.Outbox = Collect;

        game.Receive(new[]{(byte)BeachSearch.Op.SackLiftRequest,(byte)0},1);
        Assert.AreEqual(1,sack.Carrier);
        Assert.IsTrue(sent.Exists(m=>m[0]==(byte)BeachSearch.Op.SackCarrier));
        game.Receive(new[]{(byte)BeachSearch.Op.SackLiftRequest,(byte)0},2);
        Assert.AreEqual(1,sack.Carrier,"a second pair of hands took a carried sack");
        Assert.IsNull(game.CarriedSack,"the host should not be holding player 1's sack");

        // Player 1 leaves holding it: it drops where they were.
        game.PlayerLeft(1);
        Assert.IsFalse(sack.Carried);

        // A teammate's find and spending reach this beach.
        Assert.IsTrue(game.Receive(new[]{(byte)BeachSearch.Op.SackAdd,(byte)0,(byte)FindKind.Ring},1),"the host should pass it on");
        Assert.AreEqual(1,sack.Count);
        game.AddEarnings(6);
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write((byte)BeachSearch.Op.Spent); w.Write(4); w.Flush();
            game.Receive(stream.ToArray(),1);
        }
        Assert.AreEqual(2,game.Gold,"the team's gold did not go down for a teammate's purchase");
        game.Outbox = null;
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator AGuestWaitsForTheHostToHandOverASack()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var sack = game.Sacks[0];
        game.MyId = 1;
        game.Guest = true;
        lastSent = null;
        game.Outbox = Keep;

        game.Local.UseSack(sack);
        Assert.IsFalse(sack.Carried,"a guest lifted a sack without asking");
        Assert.AreEqual((byte)BeachSearch.Op.SackLiftRequest,lastSent[0]);
        game.Receive(new[]{(byte)BeachSearch.Op.SackCarrier,(byte)0,(byte)1},0);
        Assert.AreSame(sack,game.CarriedSack);
        Assert.AreEqual(BeachGear.Sack,game.Local.InHand.Gear);

        game.SessionEnded();
        game.Outbox = null;
        game.Guest = false;
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator ALateArrivalSeesTheSameSacks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        game.AddEarnings(BeachSack.Price);
        Assert.IsTrue(game.BuySack());
        game.PutInSack(game.Sacks[0],FindKind.Coin);
        game.PutInSack(game.Sacks[0],FindKind.Coin);
        game.PutInSack(game.Sacks[1],FindKind.Straw);
        Vector3 moved = game.Surface(game.sand.ClampToBeach(new Vector2(1.5f,-1f),.5f));
        game.Sacks[1].SetDown(moved);
        snapshot = game.WriteSnapshot();

        BeachSearch.FollowHost(game.Theme);
        yield return null;
        yield return null;
        var guest = Object.FindFirstObjectByType<BeachSearch>();
        Assert.AreNotSame(game,guest);
        Assert.IsTrue(guest.ReadSnapshot(snapshot));
        Assert.AreEqual(2,guest.Sacks.Count);
        Assert.AreEqual(2,guest.Sacks[0].Contents[FindKind.Coin]);
        Assert.AreEqual(1,guest.Sacks[1].Contents[FindKind.Straw]);
        Assert.Less(Vector3.Distance(guest.Sacks[1].transform.position,moved),.05f);
        Assert.AreEqual(BeachSack.Price,guest.Spent);
        Assert.AreEqual(0,guest.Gold);
        yield return new ExitPlayMode();
    }
    static byte[] snapshot;
}
