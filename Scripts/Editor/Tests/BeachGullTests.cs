using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// The seagull: it goes for a sack nobody is minding, warns first, gives up if someone comes, lets go when
// chased, and otherwise drops what it took at the far side of the beach. Nothing it takes is lost.
public class BeachGullTests
{
    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    // A ring that was picked up and put in the first sack, with the player standing well away.
    static BeachSack Bait(BeachSearch game,out BeachFind ring)
    {
        var sack = game.Sacks[0];
        ring = System.Array.Find(game.finds,f=>f.kind==FindKind.Ring);
        ring.gameObject.SetActive(false);
        game.Record(ring);
        game.PutInSack(sack,FindKind.Coin);
        game.PutInSack(sack,FindKind.Ring);
        game.PutInSack(sack,FindKind.Straw);
        Vector3 at = sack.transform.position;
        Vector2 half = game.sand.HalfSize;
        // The corner on the sack's side of x but the other side of z: away from the sack and from the gull's way out.
        Stand(game,new Vector2(Mathf.Sign(at.x)*(half.x-.5f),-Mathf.Sign(at.z)*(half.y-.5f)));
        return sack;
    }

    static void Stand(BeachSearch game,Vector2 spot)
    {
        game.player.transform.position = game.Surface(spot);
    }

    static void Run(BeachGull gull,float seconds,System.Func<bool> until = null)
    {
        for (float t=0; t<seconds; t+=.05f)
        {
            gull.Step(.05f);
            if (until!=null && until()) return;
        }
    }

    static BeachGull watched;
    static bool Fleeing() => watched.Now==BeachGull.Phase.Fleeing;
    static bool Gone() => watched.Now==BeachGull.Phase.Leaving || watched.Now==BeachGull.Phase.Away;

    [UnityTest] public IEnumerator AnUnmindedSackLosesItsShiniestFind()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var gull = watched = game.Gull;
        Assert.IsNotNull(gull);
        Assert.AreEqual(BeachGull.Phase.Away,gull.Now);
        var sack = Bait(game,out var ring);
        int picked = game.Picked;

        gull.Summon();
        gull.Step(.05f);
        Assert.AreEqual(BeachGull.Phase.Circling,gull.Now,"it should come for the sack");
        Assert.AreSame(sack,gull.Target);
        // The warning comes first: nothing is taken while it circles.
        Run(gull,BeachGull.Warning*.8f);
        Assert.AreEqual(3,sack.Count);
        Run(gull,10f,Fleeing);
        Assert.AreEqual(BeachGull.Phase.Fleeing,gull.Now);
        Assert.AreEqual(FindKind.Ring,gull.Carrying,"it took the coin over the ring");
        Assert.AreEqual(2,sack.Count);
        Assert.IsFalse(sack.Contents.ContainsKey(FindKind.Ring));

        // Nobody follows: it settles at the far side and leaves the ring there.
        Run(gull,40f,Gone);
        Assert.IsNull(gull.Carrying);
        Assert.IsTrue(ring.gameObject.activeSelf,"the ring is lost for good");
        Assert.Less(Vector2.Distance(ring.Position,new Vector2(gull.Perch.x,gull.Perch.z)),.8f,"the ring is not where it settled");
        Assert.IsTrue(ring.Exposed(game.sand),"the dropped ring should be on top of the sand");
        Assert.AreEqual(picked-1,game.Picked,"the ring will be counted again when picked up");
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator ChasingItMakesItLetGo()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var gull = watched = game.Gull;
        var sack = Bait(game,out var ring);
        gull.Summon();
        Run(gull,15f,Fleeing);
        Assert.AreEqual(BeachGull.Phase.Fleeing,gull.Now);
        Run(gull,.6f);
        // Run underneath it.
        Vector3 above = gull.transform.position;
        Stand(game,new Vector2(above.x,above.z));
        gull.Step(.05f);
        Assert.AreEqual(BeachGull.Phase.Leaving,gull.Now,"it kept flying with someone right under it");
        Assert.IsTrue(ring.gameObject.activeSelf);
        Assert.Less(Vector2.Distance(ring.Position,new Vector2(above.x,above.z)),1f,"the ring fell far from where it was chased");
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator SomeoneComingNearScaresItOff()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var gull = watched = game.Gull;
        var sack = Bait(game,out _);

        // A sack with nothing shiny in it, or with someone beside it, is left alone.
        Stand(game,new Vector2(sack.transform.position.x,sack.transform.position.z));
        gull.Summon();
        gull.Step(.05f);
        Assert.AreEqual(BeachGull.Phase.Away,gull.Now,"it came for a sack someone was standing at");

        Bait(game,out _);
        gull.Summon();
        gull.Step(.05f);
        Assert.AreEqual(BeachGull.Phase.Circling,gull.Now);
        Run(gull,1f);
        Stand(game,new Vector2(sack.transform.position.x,sack.transform.position.z)+Vector2.right);
        gull.Step(.05f);
        Assert.AreEqual(BeachGull.Phase.Leaving,gull.Now,"it did not give up when someone came");
        Assert.AreEqual(6,sack.Count,"it took something anyway");
        yield return new ExitPlayMode();
    }

    static readonly List<byte[]> sent = new();
    static void Collect(byte[] message) => sent.Add(message);
    static byte[] snapshot;

    [UnityTest] public IEnumerator GuestsSeeWhatTheHostsGullDid()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var gull = watched = game.Gull;
        var sack = Bait(game,out var ring);
        int ringIndex = System.Array.IndexOf(game.finds,ring);
        snapshot = game.WriteSnapshot();
        sent.Clear();
        game.MyId = 0;
        game.Guest = false;
        game.Outbox = Collect;
        gull.Summon();
        Run(gull,60f,() => watched.Now==BeachGull.Phase.Leaving && !watched.Carrying.HasValue);
        game.Outbox = null;
        Assert.IsTrue(sent.Exists(m=>m[0]==(byte)BeachSearch.Op.GullTook));
        Assert.IsTrue(sent.Exists(m=>m[0]==(byte)BeachSearch.Op.GullDropped));
        Vector3 dropped = ring.transform.position;

        BeachSearch.FollowHost(game.Theme);
        yield return null;
        yield return null;
        var guest = Object.FindFirstObjectByType<BeachSearch>();
        Assert.AreNotSame(game,guest);
        Assert.IsTrue(guest.ReadSnapshot(snapshot));
        guest.Guest = true;
        guest.MyId = 1;
        guest.Outbox = Collect;
        Assert.AreEqual(3,guest.Sacks[0].Count);
        bool circled = false;
        foreach (var message in sent)
        {
            guest.Receive(message,0);
            circled |= guest.Gull.Now==BeachGull.Phase.Circling;
        }
        Assert.IsTrue(circled,"the guest never saw the gull come");
        Assert.AreEqual(2,guest.Sacks[0].Count);
        Assert.IsFalse(guest.Sacks[0].Contents.ContainsKey(FindKind.Ring));
        var guestRing = guest.finds[ringIndex];
        Assert.IsTrue(guestRing.gameObject.activeSelf);
        Assert.Less(Vector3.Distance(guestRing.transform.position,dropped),.01f,"the ring lies elsewhere for the guest");
        guest.Outbox = null;
        guest.Guest = false;
        yield return new ExitPlayMode();
    }
}
