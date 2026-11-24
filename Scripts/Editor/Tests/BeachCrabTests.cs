using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// The crab: it comes for a small find uncovered and left lying, warns first with holes in the sand, lets go
// when someone comes near, and otherwise buries the find at its burrow, where digging brings it back.
public class BeachCrabTests
{
    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    // A coin that was buried, now lying uncovered in the middle of the beach, with the player in a far corner.
    static BeachFind Uncover(BeachSearch game)
    {
        var coin = System.Array.Find(game.finds,f=>f.kind==FindKind.Coin && f.gameObject.activeSelf && !f.Exposed(game.sand));
        Assert.IsNotNull(coin,"no buried coin to uncover");
        Vector2 spot = game.sand.ClampToBeach(new Vector2(.4f,-.3f),.5f);
        coin.transform.position = game.Surface(spot)+Vector3.up*.004f;
        Assert.IsTrue(coin.Exposed(game.sand));
        Vector2 half = game.sand.HalfSize;
        Stand(game,new Vector2(-(half.x-.4f),half.y-.4f));
        return coin;
    }

    static void Stand(BeachSearch game,Vector2 spot) => game.player.transform.position = game.Surface(spot);

    static BeachCrab watched;
    static void Run(float seconds,System.Func<bool> until)
    {
        for (float t=0; t<seconds; t+=.05f)
        {
            watched.Step(.05f);
            if (until()) return;
        }
    }
    static bool Warning() => watched.Now==BeachCrab.Phase.Warning;
    static bool Dragging() => watched.Now==BeachCrab.Phase.Dragging;
    static bool Hidden() => watched.Now==BeachCrab.Phase.Hidden;

    static float Flat(Vector3 a,Vector3 b) => new Vector2(a.x-b.x,a.z-b.z).magnitude;

    [Test] public void ItTakesOnlySmallThings()
    {
        Assert.IsFalse(BeachCrab.Small(FindKind.Needle),"the needle is never taken");
        Assert.IsFalse(BeachCrab.Small(FindKind.PlasticBottle));
        Assert.IsFalse(BeachCrab.Small(FindKind.FishingNet));
        Assert.IsTrue(BeachCrab.Small(FindKind.Coin));
        Assert.IsTrue(BeachCrab.Small(FindKind.Ring));
        Assert.IsTrue(BeachCrab.Small(FindKind.Straw),"small litter goes too");
    }

    [UnityTest] public IEnumerator AFindLeftLyingIsBuriedAndDugBackUp()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var crab = watched = game.Crab;
        Assert.IsNotNull(crab);
        Assert.AreEqual(BeachCrab.Phase.Hidden,crab.Now);
        var coin = Uncover(game);

        // Not before it has lain there a while.
        crab.Step(.05f);
        Assert.AreEqual(BeachCrab.Phase.Hidden,crab.Now,"it came for a find that had only just been uncovered");
        crab.Summon();
        crab.Step(.05f);
        Assert.AreEqual(BeachCrab.Phase.Warning,crab.Now);
        Assert.AreSame(coin,crab.Target);
        float away = Flat(crab.Burrow,coin.transform.position);
        Assert.That(away,Is.InRange(1.5f,3.2f),"the burrow should be a couple of metres off");
        Assert.IsNotNull(GameObject.Find("게 구멍"),"no holes in the sand to warn of it");
        // The warning comes first: nothing is taken while the holes bubble.
        Run(BeachCrab.Warning*.8f,Dragging);
        Assert.IsTrue(coin.gameObject.activeSelf);

        Run(15f,Dragging);
        Assert.AreEqual(BeachCrab.Phase.Dragging,crab.Now);
        Assert.AreEqual(FindKind.Coin,crab.Carrying);
        Assert.IsFalse(coin.gameObject.activeSelf);

        // Nobody comes: it buries the coin at the burrow.
        Run(20f,Hidden);
        Assert.AreEqual(BeachCrab.Phase.Hidden,crab.Now);
        Assert.IsNull(crab.Carrying);
        Assert.IsTrue(coin.gameObject.activeSelf,"the coin is gone for good");
        Assert.IsFalse(coin.Exposed(game.sand),"it should be under the sand");
        Assert.Less(Flat(coin.transform.position,crab.Burrow),.1f,"the coin is not at the burrow");
        CollectionAssert.Contains(new List<BeachFind>(crab.Buried),coin);

        // A handful of sand at the holes brings it up.
        var scoop = new List<BeachFind>();
        Vector2 at = new Vector2(crab.Burrow.x,crab.Burrow.z);
        Assert.Greater(game.ScoopSand(at,.28f,.078f,scoop,false,Vector2.up),0f);
        CollectionAssert.Contains(scoop,coin,"digging at the holes did not bring the coin up");
        yield return null;
        CollectionAssert.IsEmpty(new List<BeachFind>(crab.Buried),"the holes stayed after it was dug out");
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator ComingCloseMakesItLetGo()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var crab = watched = game.Crab;
        var coin = Uncover(game);
        crab.Summon();
        Run(20f,Dragging);
        Assert.AreEqual(BeachCrab.Phase.Dragging,crab.Now);
        Run(.5f,Hidden);
        Vector3 there = crab.transform.position;
        Stand(game,new Vector2(there.x+1f,there.z));
        crab.Step(.05f);
        Assert.AreEqual(BeachCrab.Phase.Fleeing,crab.Now,"it kept dragging with someone right by it");
        Assert.IsTrue(coin.gameObject.activeSelf);
        Assert.IsTrue(coin.Exposed(game.sand),"the dropped coin should be on top of the sand");
        Assert.Less(Flat(coin.transform.position,there),.2f,"the coin fell far from where it was startled");
        Run(10f,Hidden);
        Assert.AreEqual(BeachCrab.Phase.Hidden,crab.Now);
        CollectionAssert.IsEmpty(new List<BeachFind>(crab.Buried));

        // Someone standing by a find keeps the crab away from it.
        Stand(game,coin.Position+Vector2.right*.5f);
        crab.Summon();
        crab.Step(.05f);
        Assert.AreEqual(BeachCrab.Phase.Hidden,crab.Now,"it came for a find someone was standing by");
        yield return new ExitPlayMode();
    }

    static readonly List<byte[]> sent = new();
    static void Collect(byte[] message) => sent.Add(message);
    static byte[] before, after;
    static int coinIndex;
    static Vector3 buriedAt;

    [UnityTest] public IEnumerator GuestsSeeWhereTheCrabBuriedIt()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var crab = watched = game.Crab;
        var coin = Uncover(game);
        coinIndex = System.Array.IndexOf(game.finds,coin);
        before = game.WriteSnapshot();
        sent.Clear();
        game.MyId = 0;
        game.Guest = false;
        game.Outbox = Collect;
        crab.Summon();
        Run(40f,() => watched.Now==BeachCrab.Phase.Hidden && !watched.Carrying.HasValue && sent.Count>3);
        game.Outbox = null;
        Assert.IsTrue(sent.Exists(m=>m[0]==(byte)BeachSearch.Op.CrabTook));
        Assert.IsTrue(sent.Exists(m=>m[0]==(byte)BeachSearch.Op.CrabBuried));
        buriedAt = coin.transform.position;
        after = game.WriteSnapshot();

        BeachSearch.FollowHost(game.Theme);
        yield return null;
        yield return null;
        var guest = Object.FindFirstObjectByType<BeachSearch>();
        Assert.AreNotSame(game,guest);
        Assert.IsTrue(guest.ReadSnapshot(before));
        guest.Guest = true;
        guest.MyId = 1;
        guest.Outbox = Collect;
        var guestCoin = guest.finds[coinIndex];
        Assert.IsTrue(guestCoin.Exposed(guest.sand));
        bool warned = false, dragged = false;
        foreach (var message in new List<byte[]>(sent))
        {
            guest.Receive(message,0);
            warned |= guest.Crab.Now==BeachCrab.Phase.Warning;
            dragged |= guest.Crab.Now==BeachCrab.Phase.Dragging && guest.Crab.Carrying==FindKind.Coin && !guestCoin.gameObject.activeSelf;
        }
        Assert.IsTrue(warned,"the guest never saw the holes");
        Assert.IsTrue(dragged,"the guest never saw it drag the coin");
        Assert.IsTrue(guestCoin.gameObject.activeSelf);
        Assert.Less(Vector3.Distance(guestCoin.transform.position,buriedAt),.01f,"the coin lies elsewhere for the guest");
        CollectionAssert.Contains(new List<BeachFind>(guest.Crab.Buried),guestCoin);

        // Someone arriving later sees the same burrow.
        guest.Crab.ForgetMarks();
        Assert.IsTrue(guest.ReadSnapshot(after));
        CollectionAssert.Contains(new List<BeachFind>(guest.Crab.Buried),guestCoin);
        Assert.Less(Vector3.Distance(guestCoin.transform.position,buriedAt),.01f);
        guest.Outbox = null;
        guest.Guest = false;
        yield return new ExitPlayMode();
    }
}
